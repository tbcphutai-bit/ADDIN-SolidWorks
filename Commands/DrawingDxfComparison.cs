using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ADDIN.Commands.DxfChecking
{
    internal static class ScaleChecker
    {
        public static void Check(GeometryLoop2D drawing, GeometryLoop2D dxf, double tolerance, DxfCheckResult result)
        {
            double[] a = { drawing.Bounds.Width, drawing.Bounds.Height, drawing.Perimeter, Math.Sqrt(drawing.Area),
                Math.Sqrt(drawing.Bounds.Width * drawing.Bounds.Width + drawing.Bounds.Height * drawing.Bounds.Height) };
            double[] b = { dxf.Bounds.Width, dxf.Bounds.Height, dxf.Perimeter, Math.Sqrt(dxf.Area),
                Math.Sqrt(dxf.Bounds.Width * dxf.Bounds.Width + dxf.Bounds.Height * dxf.Bounds.Height) };
            string[] names = { "Width", "Height", "Perimeter", "sqrt(Area)", "Diagonal" };
            var ratios = new List<double>();
            for (int i = 0; i < a.Length; i++) if (a[i] > 1e-8)
            {
                ratios.Add(b[i] / a[i]);
                result.ScaleMeasurements.Add(string.Format(CultureInfo.InvariantCulture, "{0}: Drawing {1:0.000} / DXF {2:0.000}; ratio {3:0.000000}", names[i], a[i], b[i], b[i] / a[i]));
            }
            ratios.Sort(); result.EstimatedScale = ratios[ratios.Count / 2]; result.ScaleEvaluated = true;
            double uncertainty = tolerance / Math.Max(1, drawing.Bounds.Size);
            // A notch can alter perimeter/area without changing export scale. Require agreement
            // across independent dimensions before describing the error as a uniform scale error.
            double consensus = Math.Max(uncertainty * 2, Math.Abs(result.EstimatedScale) * .0001);
            int support = ratios.Count(r => Math.Abs(r - result.EstimatedScale) <= consensus);
            result.ScalePassed = !(Math.Abs(result.EstimatedScale - 1) > uncertainty && support >= 3);
        }
    }

    internal static class TranslationFinder
    {
        private sealed class Vote { public DxfPoint Sum; public int Count; public DxfPoint Offset { get { return Sum * (1.0 / Count); } } }
        public static bool Find(List<DxfEdge> drawing, List<DxfEdge> dxf, double tolerance, out Translation2D translation)
        {
            translation = new Translation2D(); double cell = Math.Max(.001, tolerance);
            var votes = new Dictionary<string, Vote>();
            // Adjacent bins are merged below, so values straddling a cell boundary still vote together.
            foreach (var a in drawing) foreach (var b in dxf)
            {
                if (!Compatible(a, b, tolerance)) continue;
                DxfPoint offset = Anchor(a) - Anchor(b);
                string key = Math.Round(offset.X / cell).ToString(CultureInfo.InvariantCulture) + ":" + Math.Round(offset.Y / cell).ToString(CultureInfo.InvariantCulture);
                Vote vote; if (!votes.TryGetValue(key, out vote)) { vote = new Vote(); votes.Add(key, vote); }
                vote.Sum += offset; vote.Count++;
                if (votes.Count > 100000) throw new InvalidDataException("Quá nhiều ứng viên dịch tọa độ; hãy chọn DXF chỉ chứa một Flat View.");
            }
            var candidates = votes.Values.OrderByDescending(v => v.Count).ThenBy(v => v.Offset.X).ThenBy(v => v.Offset.Y).Take(64).ToList();
            int bestCount = 0; double bestResidual = double.PositiveInfinity; DxfPoint best = new DxfPoint();
            foreach (var candidate in candidates)
            {
                var cluster = votes.Values.Where(v => v.Offset.Distance(candidate.Offset) <= cell * 2).ToList();
                DxfPoint sum = new DxfPoint(); int count = 0;
                foreach (var vote in cluster) { sum += vote.Sum; count += vote.Count; }
                DxfPoint offset = sum * (1.0 / count); var used = new HashSet<int>(); int matches = 0; double residual = 0;
                foreach (var a in drawing)
                {
                    int found = -1; double minimum = double.PositiveInfinity;
                    for (int j = 0; j < dxf.Count; j++)
                    {
                        if (used.Contains(j) || !Compatible(a, dxf[j], tolerance)) continue;
                        double distance = Anchor(a).Distance(Anchor(dxf[j]) + offset);
                        if (distance < minimum) { minimum = distance; found = j; }
                    }
                    if (found >= 0 && minimum <= tolerance) { used.Add(found); matches++; residual += minimum; }
                }
                if (matches > bestCount || (matches == bestCount && residual < bestResidual))
                { bestCount = matches; bestResidual = residual; best = offset; }
            }
            if (bestCount < Math.Min(2, Math.Min(drawing.Count, dxf.Count)))
                return FindFromVertices(drawing, dxf, tolerance, out translation);
            translation = new Translation2D { OffsetX = best.X, OffsetY = best.Y }; return true;
        }

        private static DxfPoint Anchor(DxfEdge e) { return e.Type == "CIRCLE" || e.Type == "ARC" ? e.Center : (e.Start + e.End) * .5; }
        private static bool FindFromVertices(List<DxfEdge> drawing, List<DxfEdge> dxf, double tolerance, out Translation2D translation)
        {
            // Exporters may split a Drawing edge into several DXF segments. In that case
            // vote from shared vertices and score the complete curves in both directions.
            translation = new Translation2D();
            var a = drawing.SelectMany(e => new[] { e.Start, e.End }).Distinct().ToList();
            var b = dxf.SelectMany(e => new[] { e.Start, e.End }).Distinct().ToList();
            if ((long)a.Count * b.Count > 1000000) return false;
            var votes = new Dictionary<string, Vote>(); double cell = Math.Max(.001, tolerance);
            foreach (var p in a) foreach (var q in b)
            {
                var offset = p - q; string key = Math.Round(offset.X / cell).ToString(CultureInfo.InvariantCulture) + ":" + Math.Round(offset.Y / cell).ToString(CultureInfo.InvariantCulture);
                Vote vote; if (!votes.TryGetValue(key, out vote)) { vote = new Vote(); votes.Add(key, vote); } vote.Sum += offset; vote.Count++;
            }
            double bestScore = -1, bestResidual = double.PositiveInfinity; DxfPoint best = new DxfPoint();
            foreach (var vote in votes.Values.OrderByDescending(v => v.Count).ThenBy(v => v.Offset.X).ThenBy(v => v.Offset.Y).Take(64))
            {
                var offset = vote.Offset; double scoreA = 0, scoreB = 0, residual = 0;
                foreach (var p in a) { double d = dxf.Min(e => DxfGeometry.DistanceCurve(p - offset, e)); if (d <= tolerance) scoreA++; residual += Math.Min(d, tolerance * 10); }
                foreach (var p in b) { double d = drawing.Min(e => DxfGeometry.DistanceCurve(p + offset, e)); if (d <= tolerance) scoreB++; residual += Math.Min(d, tolerance * 10); }
                double score = scoreA / a.Count + scoreB / b.Count;
                if (score > bestScore + 1e-12 || (Math.Abs(score - bestScore) <= 1e-12 && residual < bestResidual)) { bestScore = score; bestResidual = residual; best = offset; }
            }
            if (bestScore < 1) return false;
            translation = new Translation2D { OffsetX = best.X, OffsetY = best.Y }; return true;
        }
        private static bool Compatible(DxfEdge a, DxfEdge b, double tol)
        {
            if (a.Type != b.Type) return false;
            if (a.Type == "CIRCLE" || a.Type == "ARC") return Math.Abs(a.Radius - b.Radius) <= tol && Math.Abs(a.Length - b.Length) <= tol * 2;
            if (a.Type == "LINE")
            {
                var da = a.End - a.Start; var db = b.End - b.Start;
                return Math.Min((da - db).Length, (da + db).Length) <= tol * 2;
            }
            return Math.Abs(a.Length - b.Length) <= tol * 2 && Math.Abs(a.Bounds.Width - b.Bounds.Width) <= tol * 2 && Math.Abs(a.Bounds.Height - b.Bounds.Height) <= tol * 2;
        }
    }

    internal sealed class GeometryMatch
    {
        public double Deviation;
        public DxfPoint DrawingPosition, DxfPosition;
        public bool Passed;
    }

    internal static class GeometryMatcher
    {
        public static GeometryMatch Compare(GeometryLoop2D drawing, GeometryLoop2D dxf, Translation2D offset, double tolerance)
        {
            var result = new GeometryMatch { DrawingPosition = drawing.Centroid, DxfPosition = offset.Apply(dxf.Centroid) };
            if (drawing.IsCircle && dxf.IsCircle)
            {
                double radiusDifference = Math.Abs(drawing.Entities[0].Radius - dxf.Entities[0].Radius);
                result.Deviation = Math.Max(result.DrawingPosition.Distance(result.DxfPosition) + radiusDifference, radiusDifference * 2);
            }
            else
            {
                var a = Segments(drawing, new Translation2D(), tolerance);
                var b = Segments(dxf, offset, tolerance);
                var indexA = new SegmentSpatialIndex(a); var indexB = new SegmentSpatialIndex(b);
                DxfPoint pointA, pointB;
                double ab = Directed(a, indexB, tolerance, out pointA), ba = Directed(b, indexA, tolerance, out pointB);
                result.Deviation = Math.Max(ab, ba);
                if (ab >= ba) { result.DrawingPosition = pointA; result.DxfPosition = indexB.Nearest(pointA); }
                else { result.DrawingPosition = indexA.Nearest(pointB); result.DxfPosition = pointB; }
            }
            result.Passed = result.Deviation <= tolerance + 1e-9; return result;
        }

        private static List<DxfPoint[]> Segments(GeometryLoop2D loop, Translation2D offset, double tolerance)
        {
            var segments = new List<DxfPoint[]>();
            foreach (var e in loop.Entities)
            {
                var points = DxfGeometry.Sample(e, Math.Min(.002, tolerance / 20));
                for (int i = 1; i < points.Count; i++) segments.Add(new[] { offset.Apply(points[i - 1]), offset.Apply(points[i]) });
            }
            return segments;
        }

        private static double Directed(List<DxfPoint[]> source, SegmentSpatialIndex target, double tolerance, out DxfPoint worst)
        {
            double max = 0; worst = source[0][0]; int total = 0;
            foreach (var segment in source)
            {
                int steps = Math.Max(1, (int)Math.Ceiling(segment[0].Distance(segment[1]) / Math.Min(.2, tolerance)));
                total += steps; if (total > 500000) throw new InvalidDataException("Biên dạng quá lớn cho dung sai đã chọn.");
                for (int i = 0; i <= steps; i++)
                {
                    DxfPoint point = segment[0] + (segment[1] - segment[0]) * ((double)i / steps);
                    double distance = point.Distance(target.Nearest(point));
                    if (distance > max) { max = distance; worst = point; }
                }
            }
            return max;
        }

    }

    // Bounding-box search preserves the exact point-to-segment distance while avoiding
    // scanning every sampled spline segment for every point on the opposite contour.
    internal sealed class SegmentSpatialIndex
    {
        private sealed class Node
        {
            public DxfBox Bounds;
            public Node Left, Right;
            public DxfPoint[][] Segments;
        }
        private readonly Node root;
        public SegmentSpatialIndex(List<DxfPoint[]> segments)
        {
            if (segments.Count == 0) throw new InvalidDataException("Biên dạng không có đoạn hợp lệ.");
            root = Build(segments.ToArray());
        }
        private static Node Build(DxfPoint[][] segments)
        {
            var node = new Node { Bounds = DxfBox.From(segments.SelectMany(s => s)) };
            if (segments.Length <= 8) { node.Segments = segments; return node; }
            bool x = node.Bounds.Width >= node.Bounds.Height;
            var sorted = segments.OrderBy(s => x ? s[0].X + s[1].X : s[0].Y + s[1].Y).ToArray();
            int half = sorted.Length / 2;
            node.Left = Build(sorted.Take(half).ToArray()); node.Right = Build(sorted.Skip(half).ToArray()); return node;
        }
        public DxfPoint Nearest(DxfPoint point)
        {
            double best = double.PositiveInfinity; DxfPoint nearest = new DxfPoint(); Search(root, point, ref best, ref nearest); return nearest;
        }
        private static double BoundDistance(DxfPoint p, DxfBox box)
        {
            double x = Math.Max(0, Math.Max(box.MinX - p.X, p.X - box.MaxX));
            double y = Math.Max(0, Math.Max(box.MinY - p.Y, p.Y - box.MaxY)); return x * x + y * y;
        }
        private static void Search(Node node, DxfPoint point, ref double best, ref DxfPoint nearest)
        {
            if (BoundDistance(point, node.Bounds) > best) return;
            if (node.Segments == null)
            {
                Node first = node.Left, second = node.Right;
                if (BoundDistance(point, second.Bounds) < BoundDistance(point, first.Bounds)) { first = node.Right; second = node.Left; }
                Search(first, point, ref best, ref nearest); Search(second, point, ref best, ref nearest); return;
            }
            foreach (var segment in node.Segments)
            {
                var vector = segment[1] - segment[0]; double length2 = vector.Dot(vector);
                double t = length2 <= 0 ? 0 : Math.Max(0, Math.Min(1, (point - segment[0]).Dot(vector) / length2));
                DxfPoint p = segment[0] + vector * t; var delta = point - p; double distance = delta.Dot(delta);
                if (distance < best) { best = distance; nearest = p; }
            }
        }
    }

    internal static class ContourMatcher
    {
        public static DxfCheckResult Check(IEnumerable<DxfEdge> reference, IEnumerable<DxfEdge> exported, DrawingDxfCheckOptions options)
        {
            options.Validate(); var drawing = GeometryLoopBuilder.Build(reference);
            var distinctDxf = new List<DxfEdge>(); var duplicates = new List<DxfEdge>();
            foreach (var edge in exported)
            {
                if (distinctDxf.Any(e => DrawingViewCurveBuffer.SameProjection(e, edge, 1e-6))) duplicates.Add(edge);
                else distinctDxf.Add(edge);
            }
            // Compare a separate geometry copy. The saved DXF and the preview keep
            // every entity; duplicated/open cuts are reported independently.
            var dxf = GeometryLoopBuilder.Build(distinctDxf);
            if (drawing.Outer == null || drawing.Unclosed.Count > 0 || drawing.OtherOuterCount > 0)
                throw new InvalidDataException("Drawing View không có một biên ngoài kín hợp lệ hoặc có nét hở/nhánh. Kiểm tra View trước khi so DXF.");
            var result = new DxfCheckResult { DrawingInsideContourCount = drawing.Inside.Count, DxfInsideContourCount = dxf.Inside.Count };
            foreach (var edge in duplicates) result.AddQuality("Duplicate DXF geometry", "DXF có nét cắt lặp.", edge.Start);
            foreach (var edge in dxf.Unclosed) result.AddQuality("Open DXF geometry", "DXF có nét hở hoặc nhánh không thuộc biên kín.", edge.Start);
            if (dxf.Outer == null)
            { result.Add("DXF contour", "DXF không có biên ngoài kín.", drawing.Outer.Centroid, null); return result; }
            ScaleChecker.Check(drawing.Outer, dxf.Outer, options.ToleranceMm, result);
            if (!result.ScalePassed)
            { result.Add("SCALE FAIL", "DXF sai tỉ lệ; tỉ lệ ước tính chỉ dùng báo cáo, không chỉnh DXF.", drawing.Outer.Centroid, null); return result; }
            Translation2D translation;
            // Interior holes must not vote for the origin: moved holes can be the
            // majority of the edges while the outer outline is still correct.
            result.AlignmentFound = TranslationFinder.Find(drawing.Outer.Entities, dxf.Outer.Entities, options.ToleranceMm, out translation);
            result.Translation = translation;
            if (!result.AlignmentFound)
            { result.Add("Alignment", "Không đủ hình học tương ứng để xác định dịch X/Y. Không xoay, mirror hoặc fit DXF.", drawing.Outer.Centroid, null); return result; }
            foreach (var issue in result.Mismatches.Where(m => m.CutQualityIssue && m.HasDxfPosition))
                issue.DxfPosition = translation.Apply(issue.DxfPosition);
            if (dxf.OtherOuterCount > 0) result.Add("Extra outer contour", "DXF có thêm biên ngoài rời: " + dxf.OtherOuterCount, null, translation.Apply(dxf.Outer.Centroid));
            var outer = GeometryMatcher.Compare(drawing.Outer, dxf.Outer, translation, options.ToleranceMm);
            result.MaximumDeviation = outer.Deviation; result.OuterContourPassed = outer.Passed && dxf.OtherOuterCount == 0;
            if (!outer.Passed) result.Add("Outer contour mismatch", "Biên ngoài khác hình dạng hoặc kích thước.", outer.DrawingPosition, outer.DxfPosition, outer.Deviation);
            MatchInside(drawing.Inside, dxf.Inside, options.ToleranceMm, result);
            return result;
        }

        private static void MatchInside(List<GeometryLoop2D> drawing, List<GeometryLoop2D> dxf, double tolerance, DxfCheckResult result)
        {
            var matches = new GeometryMatch[drawing.Count, dxf.Count]; var assigned = Enumerable.Repeat(-1, dxf.Count).ToArray();
            for (int i = 0; i < drawing.Count; i++) for (int j = 0; j < dxf.Count; j++)
            {
                // Descriptor prefilter only skips obviously distant candidates. Counts/order do not decide PASS.
                if (drawing[i].Centroid.Distance(result.Translation.Apply(dxf[j].Centroid)) <= tolerance * 2 &&
                    Math.Abs(drawing[i].Bounds.Width - dxf[j].Bounds.Width) <= tolerance * 2 &&
                    Math.Abs(drawing[i].Bounds.Height - dxf[j].Bounds.Height) <= tolerance * 2)
                    matches[i, j] = GeometryMatcher.Compare(drawing[i], dxf[j], result.Translation, tolerance);
            }
            for (int i = 0; i < drawing.Count; i++) Assign(i, matches, assigned, new bool[dxf.Count]);
            var usedDrawing = new HashSet<int>(assigned.Where(i => i >= 0)); var usedDxf = new HashSet<int>(Enumerable.Range(0, dxf.Count).Where(j => assigned[j] >= 0));
            result.MatchedInsideContours = usedDrawing.Count;
            for (int j = 0; j < assigned.Length; j++) if (assigned[j] >= 0) result.MaximumDeviation = Math.Max(result.MaximumDeviation, matches[assigned[j], j].Deviation);
            foreach (int i in Enumerable.Range(0, drawing.Count).Where(i => !usedDrawing.Contains(i)))
            {
                int best = -1; double distance = double.PositiveInfinity;
                for (int j = 0; j < dxf.Count; j++) if (!usedDxf.Contains(j))
                {
                    double score = drawing[i].Centroid.Distance(result.Translation.Apply(dxf[j].Centroid)) + Math.Abs(drawing[i].Bounds.Width - dxf[j].Bounds.Width) + Math.Abs(drawing[i].Bounds.Height - dxf[j].Bounds.Height);
                    if (score < distance) { distance = score; best = j; }
                }
                if (best < 0) result.Add("Missing inside contour", "Thiếu lỗ/rãnh bên trong Drawing C" + drawing[i].Id, drawing[i].Centroid, null);
                else
                {
                    usedDxf.Add(best); var match = GeometryMatcher.Compare(drawing[i], dxf[best], result.Translation, tolerance);
                    result.Add("Inside contour mismatch", "Lỗ/rãnh C" + drawing[i].Id + " khác vị trí, kích thước hoặc hình dạng.", match.DrawingPosition, match.DxfPosition, match.Deviation);
                }
                result.FailedInsideContours++;
            }
            foreach (int j in Enumerable.Range(0, dxf.Count).Where(j => !usedDxf.Contains(j)))
            { result.Add("Extra DXF contour", "DXF có thêm lỗ/rãnh C" + dxf[j].Id, null, result.Translation.Apply(dxf[j].Centroid)); result.FailedInsideContours++; }
        }

        private static bool Assign(int i, GeometryMatch[,] matches, int[] assigned, bool[] visited)
        {
            for (int j = 0; j < assigned.Length; j++)
            {
                if (visited[j] || matches[i, j] == null || !matches[i, j].Passed) continue;
                visited[j] = true;
                if (assigned[j] < 0 || Assign(assigned[j], matches, assigned, visited)) { assigned[j] = i; return true; }
            }
            return false;
        }
    }
}
