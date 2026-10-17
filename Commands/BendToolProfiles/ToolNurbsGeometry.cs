using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ADDIN.Commands.BendTools
{
    internal sealed class ToolSplineData
    {
        public int Degree { get; }
        public int Flags { get; }
        public bool Closed => (Flags&1)!=0;
        public bool Periodic => (Flags&2)!=0;
        public bool Rational => (Flags&4)!=0;
        public IReadOnlyList<ToolPoint> SourceControlPoints { get; }
        public IReadOnlyList<ToolPoint> ControlPointsMm { get; }
        public IReadOnlyList<ToolPoint> SourceFitPoints { get; }
        public IReadOnlyList<double> Knots { get; }
        public IReadOnlyList<double> Weights { get; }
        public ToolTransform SourceToMm { get; }
        public IReadOnlyList<double> SourceNormal { get; }
        internal ToolSplineData(int degree,int flags,IEnumerable<ToolPoint> points,IEnumerable<double> knots,IEnumerable<double> weights,
            IEnumerable<ToolPoint> fit,ToolTransform transform,IEnumerable<double> normal=null)
        {
            Degree=degree;Flags=flags;SourceToMm=transform;SourceControlPoints=Array.AsReadOnly(points.ToArray());
            ControlPointsMm=Array.AsReadOnly(SourceControlPoints.Select(transform.Apply).ToArray());
            SourceFitPoints=Array.AsReadOnly(fit.ToArray());Knots=Array.AsReadOnly(knots.ToArray());Weights=Array.AsReadOnly(weights.ToArray());
            SourceNormal=Array.AsReadOnly((normal??new double[0]).ToArray());
        }
    }
    internal sealed class SplineChordBound
    {
        public ToolCurve Chord { get; }
        public double ParameterMin { get; }
        public double ParameterMax { get; }
        public double DeviationBoundMm { get; }
        internal SplineChordBound(ToolCurve chord,double a,double b,double error){Chord=chord;ParameterMin=a;ParameterMax=b;DeviationBoundMm=error;}
    }
    internal sealed class SplineApproximation
    {
        public const string PolicyVersion="PositiveRationalBezierHullMmV1";
        public bool Supported { get; }
        public string Reason { get; }
        public double RequestedToleranceMm { get; }
        public double MaximumDeviationMm => Chords.Count==0?0:Chords.Max(c=>c.DeviationBoundMm);
        public IReadOnlyList<SplineChordBound> Chords { get; }
        public bool RequiresConservativeBoundaryBand => Supported;
        internal SplineApproximation(bool supported,string reason,double tolerance,IEnumerable<SplineChordBound> chords)
        {Supported=supported;Reason=reason;RequestedToleranceMm=tolerance;Chords=Array.AsReadOnly(chords.ToArray());}
    }
    internal static class ToolNurbsGeometry
    {
        internal const double DefaultToleranceMm=0.001;
        private const int MaxDegree=7,MaxControl=4096,MaxChords=16384,MaxDepth=26;
        // Every elementary arithmetic result is widened by one binary64 ULP.
        // Hull boxes therefore include knot insertion, affine application and
        // rational division error instead of adding an estimated sample error.
        private struct I
        {
            internal readonly double Lo,Hi;
            internal I(double lo,double hi){if(!ToolPoint.Finite(lo)||!ToolPoint.Finite(hi)||lo>hi)throw new InvalidDataException("UNRESOLVED_SPLINE_INTERVAL");Lo=lo;Hi=hi;}
            internal static I P(double n)=>new I(n,n);
            internal double Mid => Lo/2+Hi/2;
            internal static double Up(double x){if(!ToolPoint.Finite(x))throw new InvalidDataException("UNRESOLVED_SPLINE_INTERVAL");if(x==0)return double.Epsilon;return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x)+(x>0?1:-1));}
            internal static double Down(double x)=>-Up(-x);
            public static I operator +(I a,I b)=>new I(Down(a.Lo+b.Lo),Up(a.Hi+b.Hi));
            public static I operator -(I a,I b)=>new I(Down(a.Lo-b.Hi),Up(a.Hi-b.Lo));
            public static I operator *(I a,I b){var p=new[]{a.Lo*b.Lo,a.Lo*b.Hi,a.Hi*b.Lo,a.Hi*b.Hi};return new I(Down(p.Min()),Up(p.Max()));}
            public static I operator /(I a,I b){if(b.Lo<=0&&b.Hi>=0)throw new InvalidDataException("UNRESOLVED_SPLINE_DENOMINATOR");var q=new[]{a.Lo/b.Lo,a.Lo/b.Hi,a.Hi/b.Lo,a.Hi/b.Hi};return new I(Down(q.Min()),Up(q.Max()));}
        }
        private struct H
        {
            internal I X,Y,W;
            internal H(I x,I y,I w){X=x;Y=y;W=w;}
            internal I CartesianX => X/W;
            internal I CartesianY => Y/W;
            internal ToolPoint Point => new ToolPoint(CartesianX.Mid,CartesianY.Mid);
            internal static H Mix(H a,H b,I t)=>new H(a.X*(I.P(1)-t)+b.X*t,a.Y*(I.P(1)-t)+b.Y*t,a.W*(I.P(1)-t)+b.W*t);
        }
        private sealed class Bezier {internal H[] P;internal double A,B;internal int Depth;}
        internal static SplineApproximation Approximate(ToolSplineData source,double toleranceMm=DefaultToleranceMm,CancellationToken cancellation=default(CancellationToken))
        {
            var output=new List<SplineChordBound>();
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if(source==null)throw new InvalidDataException("MISSING_SPLINE_METADATA");
                if(!ToolPoint.Finite(toleranceMm)||toleranceMm<1e-6||toleranceMm>0.01)throw new InvalidDataException("UNSUPPORTED_SPLINE_TOLERANCE");
                int p=source.Degree,n=source.ControlPointsMm.Count-1;
                if(p<1||p>MaxDegree||n<p||n>=MaxControl||source.Knots.Count!=n+p+2)throw new InvalidDataException("UNSUPPORTED_SPLINE_DEGREE_OR_COUNTS");
                if((source.Flags&~31)!=0)throw new InvalidDataException("UNSUPPORTED_SPLINE_FLAGS");
                if(source.Weights.Count!=0&&source.Weights.Count!=n+1)throw new InvalidDataException("INVALID_SPLINE_WEIGHTS");
                var weights=source.Weights.Count==0?Enumerable.Repeat(1.0,n+1).ToArray():source.Weights.ToArray();
                if(!source.Rational&&weights.Any(w=>w!=1))throw new InvalidDataException("INCONSISTENT_RATIONAL_SPLINE_FLAG");
                if(weights.Any(w=>!ToolPoint.Finite(w)||w<=0)||weights.Max()/weights.Min()>1e10)throw new InvalidDataException("UNSUPPORTED_NONPOSITIVE_OR_ILL_CONDITIONED_WEIGHT");
                var u=source.Knots.ToList();
                if(u.Any(k=>!ToolPoint.Finite(k))||u.Where((k,i)=>i>0&&k<u[i-1]).Any())throw new InvalidDataException("INVALID_SPLINE_KNOT_ORDER");
                double a=u[p],b=u[n+1];if(b<=a)throw new InvalidDataException("EMPTY_SPLINE_DOMAIN");
                if(u.GroupBy(k=>k).Any(g=>g.Count()>p+1))throw new InvalidDataException("UNSUPPORTED_SPLINE_MULTIPLICITY");
                double maxWeight=weights.Max();var m=source.SourceToMm;
                var h=source.SourceControlPoints.Select((v,i)=>{
                    var w=I.P(weights[i])/I.P(maxWeight);
                    var x=I.P(m.A)*I.P(v.X)+I.P(m.C)*I.P(v.Y)+I.P(m.Tx);
                    var y=I.P(m.B)*I.P(v.X)+I.P(m.D)*I.P(v.Y)+I.P(m.Ty);
                    return new H(x*w,y*w,w);}).ToList();
                if(source.ControlPointsMm.Any(v=>Math.Abs(v.X)>1e7||Math.Abs(v.Y)>1e7))throw new InvalidDataException("UNSUPPORTED_SPLINE_COORDINATE_DOMAIN");
                // Exact homogeneous knot insertion. Retain the original active
                // interval, including unclamped periodic representations.
                var breaks=u.Where(v=>v>=a&&v<=b).Distinct().ToArray();
                foreach(double value in breaks)
                {
                    cancellation.ThrowIfCancellationRequested();
                    while(u.Count(k=>k==value)<p)Insert(u,h,p,value);
                }
                var stack=new Stack<Bezier>();
                for(int k=p;k<h.Count;k++)if(u[k+1]>u[k]&&u[k]>=a&&u[k+1]<=b)
                    stack.Push(new Bezier {P=h.Skip(k-p).Take(p+1).ToArray(),A=u[k],B=u[k+1]});
                if(stack.Count==0)throw new InvalidDataException("NO_SPLINE_SPANS");
                int work=0;
                while(stack.Count>0)
                {
                    cancellation.ThrowIfCancellationRequested();if(++work>4*MaxChords)throw new InvalidDataException("SPLINE_SUBDIVISION_BUDGET");
                    var span=stack.Pop();var points=span.P.Select(v=>v.Point).ToArray();var first=points[0];var last=points[p];
                    double length=ToolPoint.Distance(first,last),bound=span.P.Max(v=>HullDistanceBound(v,first,last));
                    // Positive weights put the rational Bezier inside the
                    // Cartesian control hull. The finite-chord hull distance
                    // bounds the entire curve, not just sampled points.
                    if(bound<=toleranceMm&&length>1e-8)
                    {
                        if(output.Count>=MaxChords)throw new InvalidDataException("SPLINE_CHORD_BUDGET");
                        output.Add(new SplineChordBound(ToolCurve.Line(first,last,new ToolTransform()),span.A,span.B,bound));continue;
                    }
                    if(span.Depth>=MaxDepth||span.B-span.A<=1e-14*Math.Max(1,Math.Abs(span.A)))throw new InvalidDataException("UNRESOLVED_SPLINE_SUBDIVISION");
                    var left=new H[p+1];var right=new H[p+1];var q=span.P.ToArray();left[0]=q[0];right[p]=q[p];
                    for(int level=1;level<=p;level++){for(int i=0;i<=p-level;i++)q[i]=H.Mix(q[i],q[i+1],I.P(0.5));left[level]=q[0];right[p-level]=q[p-level];}
                    double mid=span.A/2+span.B/2;
                    stack.Push(new Bezier {P=right,A=mid,B=span.B,Depth=span.Depth+1});stack.Push(new Bezier {P=left,A=span.A,B=mid,Depth=span.Depth+1});
                }
                output=output.OrderBy(v=>v.ParameterMin).ToList();
                for(int i=1;i<output.Count;i++)if(ToolPoint.Distance(output[i-1].Chord.End,output[i].Chord.Start)>1e-7)throw new InvalidDataException("DISCONTINUOUS_SPLINE");
                if((source.Closed||source.Periodic)&&ToolPoint.Distance(output[0].Chord.Start,output[output.Count-1].Chord.End)>1e-7)throw new InvalidDataException("INCONSISTENT_CLOSED_OR_PERIODIC_SPLINE");
                return new SplineApproximation(true,null,toleranceMm,output);
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex) when(ex is InvalidDataException||ex is ArgumentException||ex is OverflowException)
            {return new SplineApproximation(false,ex.Message,toleranceMm,new SplineChordBound[0]);}
        }
        private static void Insert(List<double> u,List<H> points,int p,double value)
        {
            int n=points.Count-1,k;
            // An unclamped active-domain end still has source knots/controls
            // to its right. Inserting at n (the clamped shortcut) changes the
            // endpoint. Use the actual span n+1 when that endpoint is unclamped.
            if(value==u[n+1])k=n+1;else{k=p;while(k<n&&u[k+1]<=value)k++;}
            int s=u.Count(v=>v==value);if(s>=p+1)throw new InvalidDataException("INVALID_KNOT_INSERTION");
            var q=new H[n+2];for(int i=0;i<=k-p;i++)q[i]=points[i];for(int i=k-s;i<=n;i++)q[i+1]=points[i];
            for(int i=k-p+1;i<=k-s;i++)
            {
                double denominator=u[i+p]-u[i];if(denominator<=0)throw new InvalidDataException("ILL_CONDITIONED_SPLINE_KNOT");
                double alpha=(value-u[i])/denominator;if(alpha<0||alpha>1)throw new InvalidDataException("INVALID_KNOT_INSERTION");
                q[i]=H.Mix(points[i-1],points[i],(I.P(value)-I.P(u[i]))/(I.P(u[i+p])-I.P(u[i])));
            }
            u.Insert(k+1,value);points.Clear();points.AddRange(q);
        }
        internal static double DistanceToSegment(ToolPoint p,ToolPoint a,ToolPoint b)
        {
            double x=b.X-a.X,y=b.Y-a.Y,l=x*x+y*y;if(l==0)return ToolPoint.Distance(p,a);
            double t=Math.Max(0,Math.Min(1,((p.X-a.X)*x+(p.Y-a.Y)*y)/l));return ToolPoint.Distance(p,new ToolPoint(a.X+t*x,a.Y+t*y));
        }
        private static double HullDistanceBound(H h,ToolPoint a,ToolPoint b)
        {
            var x=h.CartesianX;var y=h.CartesianY;double dx=b.X-a.X,dy=b.Y-a.Y,length=dx*dx+dy*dy;
            double t=length==0?0:Math.Max(0,Math.Min(1,((x.Mid-a.X)*dx+(y.Mid-a.Y)*dy)/length));
            // Any t in [0,1] chooses a true chord point. Interval evaluation of
            // that point bounds the distance for every corner of the hull box.
            var qx=I.P(a.X)+I.P(t)*(I.P(b.X)-I.P(a.X));var qy=I.P(a.Y)+I.P(t)*(I.P(b.Y)-I.P(a.Y));
            var ex=x-qx;var ey=y-qy;double mx=Math.Max(Math.Abs(ex.Lo),Math.Abs(ex.Hi)),my=Math.Max(Math.Abs(ey.Lo),Math.Abs(ey.Hi));
            return I.Up(Math.Sqrt(I.Up(I.Up(mx*mx)+I.Up(my*my))));
        }
    }
}
