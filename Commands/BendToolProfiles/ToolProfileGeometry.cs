using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace ADDIN.Commands.BendTools
{
    internal struct ToolPoint
    {
        public double X { get; private set; }
        public double Y { get; private set; }
        internal ToolPoint(double x, double y)
        {
            if (!Finite(x) || !Finite(y)) throw new InvalidDataException("Non-finite coordinate.");
            X = x; Y = y;
        }
        internal static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        internal static double Distance(ToolPoint a, ToolPoint b) => Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
    }

    // p' = (A*x+C*y+Tx, B*x+D*y+Ty). Matrices remain attached to source entities.
    // The root matrix converts declared file units to mm; no tip recentering.
    internal sealed class ToolTransform
    {
        public double A { get; private set; }
        public double B { get; private set; }
        public double C { get; private set; }
        public double D { get; private set; }
        public double Tx { get; private set; }
        public double Ty { get; private set; }
        internal ToolTransform(double a=1, double b=0, double c=0, double d=1, double tx=0, double ty=0)
        {
            if (new[] {a,b,c,d,tx,ty}.Any(n=>!ToolPoint.Finite(n))) throw new InvalidDataException("Non-finite transform.");
            A=a; B=b; C=c; D=d; Tx=tx; Ty=ty;
        }
        internal ToolPoint Apply(ToolPoint p) => new ToolPoint(A*p.X+C*p.Y+Tx, B*p.X+D*p.Y+Ty);
        internal ToolTransform Then(ToolTransform local) => new ToolTransform(
            A*local.A+C*local.B, B*local.A+D*local.B, A*local.C+C*local.D, B*local.C+D*local.D,
            A*local.Tx+C*local.Ty+Tx, B*local.Tx+D*local.Ty+Ty);
        internal double Determinant => A*D-B*C;
        internal double CircularScale()
        {
            double x=Math.Sqrt(A*A+B*B), y=Math.Sqrt(C*C+D*D);
            if (x<=0 || y<=0 || !ToolPoint.Finite(x) || !ToolPoint.Finite(y) ||
                Math.Abs(x-y)>1e-10*Math.Max(x,y) || Math.Abs(A*C+B*D)>1e-10*x*y)
                throw new InvalidDataException("Non-uniform/sheared circular transform requires an ellipse; no approximation made.");
            return x;
        }
    }

    internal sealed class ToolCurve
    {
        public string Kind { get; private set; }
        public ToolPoint Start { get; private set; }
        public ToolPoint End { get; private set; }
        public ToolPoint Center { get; private set; }
        public double RadiusMm { get; private set; }
        public double SweepRadians { get; private set; }
        public int SourceSegment { get; private set; }
        internal static ToolCurve Line(ToolPoint a, ToolPoint b, ToolTransform matrix, int segment=0) =>
            new ToolCurve { Kind="LINE", Start=matrix.Apply(a), End=matrix.Apply(b), SourceSegment=segment };
        internal static ToolCurve Arc(ToolPoint a, ToolPoint b, ToolPoint center, double radius, double sweep, ToolTransform matrix, int segment=0)
        {
            double scaled=radius*matrix.CircularScale();
            if (!ToolPoint.Finite(scaled) || scaled<=0 || !ToolPoint.Finite(sweep) || sweep==0)
                throw new InvalidDataException("Invalid circular arc.");
            return new ToolCurve { Kind="ARC", Start=matrix.Apply(a), End=matrix.Apply(b), Center=matrix.Apply(center),
                RadiusMm=scaled, SweepRadians=sweep*Math.Sign(matrix.Determinant), SourceSegment=segment };
        }
        internal static ToolCurve Bulge(ToolPoint a, ToolPoint b, double bulge, ToolTransform matrix, int segment)
        {
            if (!ToolPoint.Finite(bulge)) throw new InvalidDataException("Non-finite bulge.");
            if (bulge==0) return Line(a,b,matrix,segment);
            double chord=ToolPoint.Distance(a,b);
            if (chord==0) throw new InvalidDataException("Bulged segment has coincident endpoints.");
            double offset=chord*(1/bulge-bulge)/4;
            var center=new ToolPoint((a.X+b.X)/2-(b.Y-a.Y)*offset/chord, (a.Y+b.Y)/2+(b.X-a.X)*offset/chord);
            double radius=chord*(Math.Abs(bulge)+1/Math.Abs(bulge))/4;
            return Arc(a,b,center,radius,4*Math.Atan(bulge),matrix,segment);
        }
    }

    internal sealed class ToolDxfEntity
    {
        public string Type { get; internal set; }
        public string Handle { get; internal set; }
        public int SourceRecord { get; internal set; }
        public string SourceLayer { get; internal set; }
        public string Layer { get; internal set; }
        public string InstancePath { get; internal set; }
        public IReadOnlyList<string> Blocks { get; internal set; }
        public IReadOnlyList<string> InsertHandles { get; internal set; }
        public ToolTransform Transform { get; internal set; }
        public bool? SourceClosed { get; internal set; }
        public bool Annotation { get; internal set; }
        public bool PaperSpace { get; internal set; }
        public string UnsupportedReason { get; internal set; }
        public IReadOnlyList<ToolPoint> SourceVertices { get; internal set; }
        public IReadOnlyList<double> SourceBulges { get; internal set; }
        public IReadOnlyList<ToolCurve> Curves { get; internal set; }
    }

    internal static class ToolProfileTopology
    {
        // Observes endpoint connectivity only. Never adds closure segments,
        // welds coordinates, offsets skins or repairs a source open polyline.
        internal static bool Closed(IList<ToolDxfEntity> entities)
        {
            if (entities.Count==0 || entities.Any(e=>e.UnsupportedReason!=null || e.SourceClosed==false)) return false;
            var curves=entities.SelectMany(e=>e.Curves).ToList();
            if (curves.Count==0) return false;
            var points=new List<ToolPoint>(); var degree=new List<int>();
            foreach (var c in curves)
            {
                if (c.Kind=="ARC" && Math.Abs(Math.Abs(c.SweepRadians)-2*Math.PI)<1e-12) continue;
                if (ToolPoint.Distance(c.Start,c.End)<=1e-9) return false;
                foreach (var p in new[] {c.Start,c.End})
                {
                    int index=points.FindIndex(q=>ToolPoint.Distance(p,q)<=1e-7);
                    if (index<0) { points.Add(p); degree.Add(1); } else degree[index]++;
                }
            }
            return degree.All(d=>d==2);
        }
    }
}
