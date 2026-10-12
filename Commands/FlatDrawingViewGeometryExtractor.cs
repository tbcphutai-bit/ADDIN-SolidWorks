using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.DxfChecking
{
    internal sealed class FlatDrawingViewGeometryExtractor
    {
        public double[] LastBuffer { get; private set; }
        public DrawingNormalizationReport Normalization { get; private set; }
        public List<DxfEdge> Extract(IView view)
        {
            if (view == null || !view.IsFlatPatternView()) throw new InvalidOperationException("Hãy chọn một Flat Pattern Drawing View.");
            // These are projected Drawing entities, not Part/Flat-Pattern feature curves.
            // API documentation: IView.GetPolyLinesAndCurves, crosshatch filter 1.
            var buffer = view.GetPolyLinesAndCurves(1) as double[];
            if (buffer == null || buffer.Length == 0)
                throw new InvalidDataException("View chưa có dữ liệu biên dạng. Zoom to Fit và dùng View chất lượng cao, Hidden Lines Removed rồi thử lại.");
            LastBuffer = (double[])buffer.Clone();
            // GetPolyLinesAndCurves already returns View-local physical coordinates in meters,
            // independent of ScaleDecimal (verified on SW 2024 at 1:2, 1:1 and 2:1).
            // Do not remove the display scale a second time. Only convert meters to mm here.
            DrawingNormalizationReport normalized;
            var edges = DrawingViewCurveBuffer.Read(buffer, 1, out normalized);
            Normalization = normalized;
            return edges;
        }
    }

    internal sealed class DrawingNormalizationReport
    {
        public int RawEdges, DuplicateEdges, PointProjections, ShortThicknessLines, CoveredArcs, AcceptedEdges;
    }

    internal static class DrawingViewCurveBuffer
    {
        public static List<DxfEdge> Read(double[] buffer, double coordinateScale)
        {
            DrawingNormalizationReport report;
            return Read(buffer, coordinateScale, out report);
        }

        public static List<DxfEdge> Read(double[] buffer, double coordinateScale, out DrawingNormalizationReport report)
        {
            report = new DrawingNormalizationReport();
            if (!(coordinateScale > 0) || double.IsInfinity(coordinateScale)) throw new InvalidDataException("Tỉ lệ dữ liệu Drawing không hợp lệ.");
            if (buffer.Any(x => double.IsNaN(x) || double.IsInfinity(x))) throw new InvalidDataException("Drawing geometry có giá trị không hữu hạn.");
            double factor = 1000.0 / coordinateScale; int index = 0;
            // SW 2025 may overallocate spline buffers with a zero-filled tail. Stop only
            // at a record boundary past the final nonzero value; record data stays intact.
            int dataEnd = buffer.Length;
            while (dataEnd > 0 && buffer[dataEnd - 1] == 0) dataEnd--;
            var result = new List<DxfEdge>();
            while (index < dataEnd)
            {
                int type = Count(buffer, ref index), size = Count(buffer, ref index);
                Require(buffer, index, size + 7);
                double[] geometry = buffer.Skip(index).Take(size).ToArray(); index += size;
                index += 6; // Drawing line attributes; dimensions/annotations are not in this buffer.
                int count = Count(buffer, ref index); Require(buffer, index, count * 3);
                var points = new List<DxfPoint>();
                for (int i = 0; i < count; i++) points.Add(new DxfPoint(buffer[index + 3 * i] * factor, buffer[index + 3 * i + 1] * factor));
                index += count * 3;
                if (count < 2) throw new InvalidDataException("Drawing entity thiếu điểm biên dạng.");
                var added = new List<DxfEdge>();
                if (type == 0)
                {
                    for (int i = 1; i < points.Count; i++) if (points[i - 1].Distance(points[i]) > 1e-8) added.Add(DxfGeometry.Line(points[i - 1], points[i]));
                }
                else if (type == 1)
                {
                    if (size != 12) throw new InvalidDataException("Cung Drawing thiếu tham số.");
                    if (Math.Abs(geometry[9]) > 1e-6 || Math.Abs(geometry[10]) > 1e-6 || Math.Abs(geometry[11]) < .99)
                        added.Add(ReadProjectedArc(geometry, factor));
                    else
                    {
                    DxfPoint c = Point(geometry, 0, factor), a = Point(geometry, 3, factor), b = Point(geometry, 6, factor);
                    double radius = a.Distance(c), start = Math.Atan2(a.Y - c.Y, a.X - c.X), sign = Math.Sign(geometry[11]);
                    double sweep = a.Distance(b) <= 1e-6 ? sign * DxfGeometry.Tau : sign * DxfGeometry.Mod(sign * (Math.Atan2(b.Y - c.Y, b.X - c.X) - start));
                    added.Add(DxfGeometry.Circle(c, radius, start, sweep));
                    }
                }
                else if (type == 3) added.Add(ReadSpline(geometry, factor));
                else if (type == 2) added.Add(ReadEllipse(geometry, factor));
                else throw new InvalidDataException("Drawing curve type chưa hỗ trợ: " + type);
                // Check the equation against projected tessellation; never silently use unprojected data.
                if (type != 0 && points.Any(p => DxfGeometry.DistanceCurve(p, added[0]) > .01))
                    throw new InvalidDataException("Định nghĩa curve không khớp dữ liệu chiếu Drawing. Không thể kiểm tra View này tin cậy.");
                foreach (var edge in added)
                {
                    // A thickness edge seen end-on can project to a point, with a few
                    // nanometers of numeric noise. It contributes no 2D cut contour.
                    report.RawEdges++;
                    if (edge.Bounds.Size <= 1e-5) { report.PointProjections++; continue; }
                    // Below the topology resolution, a projected thickness line
                    // cannot form a separate cut. This applies only to the reference.
                    if (edge.Type == "LINE" && edge.Length <= .001) { report.ShortThicknessLines++; continue; }
                    edge.Space = "Model"; edge.Layer = "DrawingView";
                    // A projected front/back edge can appear twice in API output. Deduplicate
                    // the reference only; duplicate entities in DXF remain detectable.
                    if (result.Any(e => SameProjection(e, edge))) report.DuplicateEdges++;
                    else result.Add(edge);
                }
            }
            var circles = result.Where(e => e.Type == "CIRCLE").ToList();
            report.CoveredArcs = result.RemoveAll(e => e.Type == "ARC" && circles.Any(c =>
                c.Center.Distance(e.Center) < 1e-5 && Math.Abs(c.Radius - e.Radius) < 1e-5));
            report.AcceptedEdges = result.Count;
            if (result.Count == 0) throw new InvalidDataException("Không đọc được biên dạng Drawing View.");
            return result;
        }

        internal static bool SameProjection(DxfEdge a, DxfEdge b, double epsilon = 1e-5)
        {
            if (a.Type != b.Type || Math.Abs(a.Length - b.Length) > epsilon) return false;
            bool forward = a.Start.Distance(b.Start) < epsilon && a.End.Distance(b.End) < epsilon;
            bool reverse = a.Start.Distance(b.End) < epsilon && a.End.Distance(b.Start) < epsilon;
            if (a.Type == "CIRCLE") return a.Center.Distance(b.Center) < epsilon && Math.Abs(a.Radius - b.Radius) < epsilon;
            return (forward || reverse) && Enumerable.Range(1, 7).All(i => a.At(i / 8.0).Distance(b.At(forward ? i / 8.0 : 1 - i / 8.0)) < epsilon);
        }

        private static DxfEdge ReadSpline(double[] g, double factor)
        {
            if (g.Length < 5) throw new InvalidDataException("Spline Drawing thiếu tham số.");
            int degree = (int)g[0], dims = (int)g[2], count = (int)g[3], knots = (int)g[4];
            if ((dims != 3 && dims != 4) || count < 2 || count > 100000 || knots < 2 || g.Length != 5 + count * dims + knots)
                throw new InvalidDataException("Spline Drawing có dữ liệu control/knot không hợp lệ.");
            var record = new DxfRecord { Type = "SPLINE" }; Add(record, 71, degree);
            for (int i = 0; i < count; i++)
            {
                int k = 5 + i * dims;
                Add(record, 10, g[k] * factor); Add(record, 20, g[k + 1] * factor);
                if (dims == 4) Add(record, 41, g[k + 3]);
            }
            for (int i = 0; i < knots; i++) Add(record, 40, g[5 + count * dims + i]);
            return DxfGeometry.ReadEdges(record, DxfTransform.Identity)[0];
        }

        private static DxfEdge ReadProjectedArc(double[] g, double factor)
        {
            // Project the Drawing API's own 3D arc equation to View XY. This never
            // retrieves Part curves and never changes or aligns the exported DXF.
            double x=g[3]-g[0], y=g[4]-g[1], z=g[5]-g[2];
            double r=Math.Sqrt(x*x+y*y+z*z), nx=g[9], ny=g[10], nz=g[11];
            double normal=Math.Sqrt(nx*nx+ny*ny+nz*nz);
            if(r<=1e-12 || normal<=1e-12)throw new InvalidDataException("Cung Drawing suy biến.");
            x/=r;y/=r;z/=r;nx/=normal;ny/=normal;nz/=normal;
            if(Math.Abs(x*nx+y*ny+z*nz)>1e-6)throw new InvalidDataException("Normal của cung Drawing không hợp lệ.");
            double vx=ny*z-nz*y, vy=nz*x-nx*z, vz=nx*y-ny*x;
            double bx=g[6]-g[0], by=g[7]-g[1], bz=g[8]-g[2];
            double dx=g[6]-g[3],dy=g[7]-g[4],dz=g[8]-g[5];
            double sweep=Math.Sqrt(dx*dx+dy*dy+dz*dz)<=1e-9 ? DxfGeometry.Tau : DxfGeometry.Mod(Math.Atan2(bx*vx+by*vy+bz*vz,bx*x+by*y+bz*z));
            DxfPoint c=Point(g,0,factor),u=new DxfPoint(x*r*factor,y*r*factor),v=new DxfPoint(vx*r*factor,vy*r*factor);
            var edge=new DxfEdge{Type="ELLIPSE",Closed=sweep>=DxfGeometry.Tau-1e-9,
                Evaluate=t=>c+u*Math.Cos(sweep*t)+v*Math.Sin(sweep*t),
                Derivative=t=>(u*(-Math.Sin(sweep*t))+v*Math.Cos(sweep*t))*sweep};
            edge.Start=edge.At(0);edge.End=edge.At(1);edge.Length=DxfGeometry.Length(edge);edge.Bounds=DxfBox.From(DxfGeometry.Sample(edge,.002));return edge;
        }

        private static DxfEdge ReadEllipse(double[] g, double factor)
        {
            if (g.Length != 17) throw new InvalidDataException("Ellipse Drawing thiếu tham số.");
            var c = Point(g, 0, factor); var u = Point(g, 3, 1); var v = Point(g, 6, 1);
            if (Math.Abs(g[5]) > 1e-6 || Math.Abs(g[8]) > 1e-6) throw new InvalidDataException("Ellipse Drawing không nằm trong XY.");
            double major = g[9] * factor, minor = g[10] * factor;
            var a = Point(g, 11, factor) - c; var b = Point(g, 14, factor) - c;
            double start = Math.Atan2(a.Dot(v) / minor, a.Dot(u) / major), end = Math.Atan2(b.Dot(v) / minor, b.Dot(u) / major);
            double sweep = a.Distance(b) < 1e-6 ? DxfGeometry.Tau : DxfGeometry.Mod(end - start);
            var edge = new DxfEdge { Type = "ELLIPSE", Closed = sweep >= DxfGeometry.Tau - 1e-9,
                Evaluate = t => c + u * (major * Math.Cos(start + sweep * t)) + v * (minor * Math.Sin(start + sweep * t)),
                Derivative = t => (u * (-major * Math.Sin(start + sweep * t)) + v * (minor * Math.Cos(start + sweep * t))) * sweep };
            edge.Start = edge.At(0); edge.End = edge.At(1); edge.Length = DxfGeometry.Length(edge); edge.Bounds = DxfBox.From(DxfGeometry.Sample(edge, .002)); return edge;
        }

        private static void Add(DxfRecord r, int code, double value) { r.Pairs.Add(new DxfPair { Code = code, Value = value.ToString("R", CultureInfo.InvariantCulture) }); }
        private static DxfPoint Point(double[] g, int i, double factor) { return new DxfPoint(g[i] * factor, g[i + 1] * factor); }
        private static int Count(double[] b, ref int i)
        {
            Require(b, i, 1); double value = b[i++];
            if (value < 0 || value > 1000000 || value != Math.Truncate(value)) throw new InvalidDataException("Drawing buffer có số phần tử không hợp lệ.");
            return (int)value;
        }
        private static void Require(double[] b, int index, int count) { if (count < 0 || index > b.Length - count) throw new InvalidDataException("Drawing buffer bị thiếu dữ liệu."); }
    }
}
