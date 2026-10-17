using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendCollision
{
    internal sealed class CollisionAbort : Exception
    { internal CollisionAbort(string reason) : base(reason) { } }

    internal sealed class CollisionWork
    {
        private readonly Stopwatch timer=Stopwatch.StartNew();
        private readonly CancellationToken token;
        private readonly CollisionQuery2D query;
        internal int Count { get; private set; }
        internal CollisionWork(CollisionQuery2D query, CancellationToken token) { this.query=query; this.token=token; }
        internal void Step()
        {
            if(token.IsCancellationRequested) throw new CollisionAbort("CANCELLED");
            if(!query.Current) throw new CollisionAbort("STALE_COLLISION_INPUT");
            if(++Count>query.Policy.MaximumWork) throw new CollisionAbort("WORK_BUDGET_EXCEEDED");
            if(timer.ElapsedMilliseconds>query.Policy.MaximumMilliseconds) throw new CollisionAbort("TIME_BUDGET_EXCEEDED");
        }
    }

    internal struct CollisionBox
    {
        internal double X0,Y0,X1,Y1;
        internal ToolPoint Center => new ToolPoint(X0/2+X1/2,Y0/2+Y1/2);
        internal double Size => (X1-X0)+(Y1-Y0);
        internal double LowerDistance(CollisionBox b)
        {
            double x=Math.Max(0,Math.Max((CollisionInterval.P(X0)-CollisionInterval.P(b.X1)).Lo,(CollisionInterval.P(b.X0)-CollisionInterval.P(X1)).Lo));
            double y=Math.Max(0,Math.Max((CollisionInterval.P(Y0)-CollisionInterval.P(b.Y1)).Lo,(CollisionInterval.P(b.Y0)-CollisionInterval.P(Y1)).Lo));
            return (CollisionInterval.P(x).Square()+CollisionInterval.P(y).Square()).Sqrt().Lo;
        }
        internal static CollisionBox Union(IEnumerable<CollisionBox> boxes)
        { var a=boxes.ToArray(); return new CollisionBox {X0=a.Min(b=>b.X0),Y0=a.Min(b=>b.Y0),X1=a.Max(b=>b.X1),Y1=a.Max(b=>b.Y1)}; }
    }

    internal sealed class CurvePairFeatures
    {
        internal double Upper=double.PositiveInfinity;
        internal double AParameter,BParameter;
        internal ToolPoint APoint,BPoint;
        internal readonly List<ToolPoint> Contacts=new List<ToolPoint>();
        internal void Add(ToolCurve a, double ta, ToolCurve b, double tb)
        {
            ta=Math.Max(0,Math.Min(1,ta)); tb=Math.Max(0,Math.Min(1,tb));
            var pa=CollisionGeometry.Point(a,ta); var pb=CollisionGeometry.Point(b,tb);
            double distance=ToolPoint.Distance(pa,pb)+CollisionGeometry.Guard(a)+CollisionGeometry.Guard(b);
            if(distance<Upper) { Upper=distance; AParameter=ta; BParameter=tb; APoint=pa; BPoint=pb; }
            if(ToolPoint.Distance(pa,pb)<=4*(CollisionGeometry.Guard(a)+CollisionGeometry.Guard(b))) Contacts.Add(new ToolPoint(pa.X/2+pb.X/2,pa.Y/2+pb.Y/2));
        }
    }

    internal sealed class CurveDistanceCertificate
    {
        internal double Lower;
        internal CurvePairFeatures Features;
        internal bool Separated;
    }

    internal static class CollisionGeometry
    {
        internal const double Tau=2*Math.PI;
        internal static double Guard(ToolCurve c) => 1e-9+2e-12*(1+c.RadiusMm+new[]{c.Start,c.End,c.Center}.Max(p=>Math.Max(Math.Abs(p.X),Math.Abs(p.Y))));
        internal static double Length(ToolCurve c) => c.Kind=="LINE"?ToolPoint.Distance(c.Start,c.End):c.RadiusMm*Math.Abs(c.SweepRadians);
        internal static double Positive(double angle) { angle%=Tau; return angle<0?angle+Tau:angle; }
        internal static ToolPoint Point(ToolCurve c, double t)
        {
            if(c.Kind=="LINE") return new ToolPoint((1-t)*c.Start.X+t*c.End.X,(1-t)*c.Start.Y+t*c.End.Y);
            double angle=Math.Atan2(c.Start.Y-c.Center.Y,c.Start.X-c.Center.X)+t*c.SweepRadians;
            return new ToolPoint(c.Center.X+c.RadiusMm*Math.Cos(angle),c.Center.Y+c.RadiusMm*Math.Sin(angle));
        }
        internal static ToolPoint Normal(ToolCurve c, double t)
        {
            if(c.Kind=="LINE") { double n=Length(c); return new ToolPoint(-(c.End.Y-c.Start.Y)/n,(c.End.X-c.Start.X)/n); }
            var p=Point(c,t); return new ToolPoint((p.X-c.Center.X)/c.RadiusMm,(p.Y-c.Center.Y)/c.RadiusMm);
        }
        internal static bool AngleParameter(ToolCurve arc, double angle, out double t, double angularPad=1e-12)
        {
            double start=Math.Atan2(arc.Start.Y-arc.Center.Y,arc.Start.X-arc.Center.X);
            double travel=Positive((angle-start)*Math.Sign(arc.SweepRadians));
            if(Tau-travel<=angularPad) travel=0;
            t=Math.Max(0,Math.Min(1,travel/Math.Abs(arc.SweepRadians)));
            return travel<=Math.Abs(arc.SweepRadians)+angularPad;
        }
        internal static CollisionBox Bounds(ToolCurve c, double low=0, double high=1)
        {
            if(c.Kind=="LINE")
            {
                var a=CollisionInterval.P(1)-CollisionInterval.P(low);var b=CollisionInterval.P(1)-CollisionInterval.P(high);
                var x0=a*CollisionInterval.P(c.Start.X)+CollisionInterval.P(low)*CollisionInterval.P(c.End.X);
                var y0=a*CollisionInterval.P(c.Start.Y)+CollisionInterval.P(low)*CollisionInterval.P(c.End.Y);
                var x1=b*CollisionInterval.P(c.Start.X)+CollisionInterval.P(high)*CollisionInterval.P(c.End.X);
                var y1=b*CollisionInterval.P(c.Start.Y)+CollisionInterval.P(high)*CollisionInterval.P(c.End.Y);
                return new CollisionBox {X0=Math.Min(x0.Lo,x1.Lo),Y0=Math.Min(y0.Lo,y1.Lo),X1=Math.Max(x0.Hi,x1.Hi),Y1=Math.Max(y0.Hi,y1.Hi)};
            }
            var cx=CollisionInterval.P(c.Center.X);var cy=CollisionInterval.P(c.Center.Y);var radius=CollisionInterval.P(c.RadiusMm);
            if(low==0&&high==1&&Math.Abs(Math.Abs(c.SweepRadians)-Tau)<=1e-12)
                return new CollisionBox {X0=(cx-radius).Lo,Y0=(cy-radius).Lo,X1=(cx+radius).Hi,Y1=(cy+radius).Hi};
            // Midpoint rotation is evaluated by interval Taylor polynomials,
            // independently of Math.Sin/Math.Cos used for upper/witness hints.
            // Arc length bounds deviation from that midpoint for the ENTIRE
            // parameter interval; subdivision tightens this enclosing box.
            double mid=low/2+high/2;CollisionInterval sin,cos;
            CollisionInterval.SinCos(CollisionInterval.P(c.SweepRadians)*CollisionInterval.P(mid),out sin,out cos);
            var vx=CollisionInterval.P(c.Start.X)-cx;var vy=CollisionInterval.P(c.Start.Y)-cy;var norm=(vx.Square()+vy.Square()).Sqrt();
            var x=cx+radius*((vx/norm)*cos-(vy/norm)*sin);var y=cy+radius*((vx/norm)*sin+(vy/norm)*cos);
            var reach=radius*CollisionInterval.Abs(CollisionInterval.P(c.SweepRadians))*(CollisionInterval.P(high)-CollisionInterval.P(low))/CollisionInterval.P(2);
            return new CollisionBox {X0=(x-CollisionInterval.P(reach.Hi)).Lo,Y0=(y-CollisionInterval.P(reach.Hi)).Lo,X1=(x+CollisionInterval.P(reach.Hi)).Hi,Y1=(y+CollisionInterval.P(reach.Hi)).Hi};
        }
        internal static double NearestParameter(ToolCurve c, ToolPoint p)
        {
            if(c.Kind=="LINE")
            {
                double x=c.End.X-c.Start.X,y=c.End.Y-c.Start.Y;
                return Math.Max(0,Math.Min(1,((p.X-c.Start.X)*x+(p.Y-c.Start.Y)*y)/(x*x+y*y)));
            }
            double t;
            if(AngleParameter(c,Math.Atan2(p.Y-c.Center.Y,p.X-c.Center.X),out t)) return t;
            return ToolPoint.Distance(p,Point(c,0))<ToolPoint.Distance(p,Point(c,1))?0:1;
        }
        internal static double PointDistanceLower(ToolCurve c, ToolPoint p)
        {
            var x=CollisionInterval.P(p.X)-CollisionInterval.P(c.Start.X);
            var y=CollisionInterval.P(p.Y)-CollisionInterval.P(c.Start.Y);
            if(c.Kind=="ARC")
            {
                // Distance to the entire supporting circle is a LOWER bound
                // for every partial arc. Avoid an unproven atan2 membership
                // decision in the material-interior certificate.
                x=CollisionInterval.P(p.X)-CollisionInterval.P(c.Center.X);
                y=CollisionInterval.P(p.Y)-CollisionInterval.P(c.Center.Y);
                return Math.Max(0,CollisionInterval.Abs((x.Square()+y.Square()).Sqrt()-CollisionInterval.P(c.RadiusMm)).Lo);
            }
            var dx=CollisionInterval.P(c.End.X)-CollisionInterval.P(c.Start.X);
            var dy=CollisionInterval.P(c.End.Y)-CollisionInterval.P(c.Start.Y);
            var square=dx.Square()+dy.Square();var projection=x*dx+y*dy;
            if(projection.Hi<=0) return Math.Max(0,(x.Square()+y.Square()).Sqrt().Lo);
            if(projection.Lo>=square.Hi)
            {
                x=CollisionInterval.P(p.X)-CollisionInterval.P(c.End.X);
                y=CollisionInterval.P(p.Y)-CollisionInterval.P(c.End.Y);
                return Math.Max(0,(x.Square()+y.Square()).Sqrt().Lo);
            }
            // At a numerically ambiguous endpoint this supporting-line
            // distance remains conservative for the finite segment too.
            return Math.Max(0,(CollisionInterval.Abs(x*dy-y*dx)/square.Sqrt()).Lo);
        }
        private static double Cross(double x, double y, double a, double b) => x*b-y*a;
        private static void AddPoint(CurvePairFeatures f, ToolCurve a, ToolCurve b, ToolPoint p)
        { f.Add(a,NearestParameter(a,p),b,NearestParameter(b,p)); }
        internal static CurvePairFeatures Features(ToolCurve a, ToolCurve b)
        {
            var f=new CurvePairFeatures();
            foreach(double t in new[]{0.0,1.0}) { f.Add(a,t,b,NearestParameter(b,Point(a,t))); f.Add(a,NearestParameter(a,Point(b,t)),b,t); }
            if(a.Kind=="LINE" && b.Kind=="LINE")
            {
                double ax=a.End.X-a.Start.X,ay=a.End.Y-a.Start.Y,bx=b.End.X-b.Start.X,by=b.End.Y-b.Start.Y;
                double dx=b.Start.X-a.Start.X,dy=b.Start.Y-a.Start.Y,den=Cross(ax,ay,bx,by);
                if(den!=0)
                {
                    double ta=Cross(dx,dy,bx,by)/den,tb=Cross(dx,dy,ax,ay)/den;
                    if(ta>=0 && ta<=1 && tb>=0 && tb<=1) f.Add(a,ta,b,tb);
                }
            }
            else if(a.Kind=="LINE" || b.Kind=="LINE")
            {
                var line=a.Kind=="LINE"?a:b; var arc=a.Kind=="ARC"?a:b;
                double length=Length(line),ux=(line.End.X-line.Start.X)/length,uy=(line.End.Y-line.Start.Y)/length;
                double dx=arc.Center.X-line.Start.X,dy=arc.Center.Y-line.Start.Y,along=dx*ux+dy*uy,perp=Cross(dx,dy,ux,uy);
                double square=(arc.RadiusMm-Math.Abs(perp))*(arc.RadiusMm+Math.Abs(perp));
                if(square>=0) foreach(double sign in new[]{-1.0,1.0})
                {
                    double s=along+sign*Math.Sqrt(square);
                    if(s>=0 && s<=length) AddPoint(f,a,b,new ToolPoint(line.Start.X+s*ux,line.Start.Y+s*uy));
                }
                foreach(double sign in new[]{-1.0,1.0})
                    AddPoint(f,a,b,new ToolPoint(arc.Center.X+sign*arc.RadiusMm*(-uy),arc.Center.Y+sign*arc.RadiusMm*ux));
            }
            else
            {
                double d=ToolPoint.Distance(a.Center,b.Center);
                if(d>0)
                {
                    double ux=(b.Center.X-a.Center.X)/d,uy=(b.Center.Y-a.Center.Y)/d;
                    foreach(double sa in new[]{-1.0,1.0}) foreach(double sb in new[]{-1.0,1.0})
                        f.Add(a,NearestParameter(a,new ToolPoint(a.Center.X+sa*a.RadiusMm*ux,a.Center.Y+sa*a.RadiusMm*uy)),
                              b,NearestParameter(b,new ToolPoint(b.Center.X+sb*b.RadiusMm*ux,b.Center.Y+sb*b.RadiusMm*uy)));
                    if(d<=a.RadiusMm+b.RadiusMm && d>=Math.Abs(a.RadiusMm-b.RadiusMm))
                    {
                        double along=d/2+(a.RadiusMm-b.RadiusMm)*(a.RadiusMm+b.RadiusMm)/(2*d);
                        double half=Math.Sqrt(Math.Max(0,(a.RadiusMm-along)*(a.RadiusMm+along)));
                        foreach(double s in new[]{-1.0,1.0}) AddPoint(f,a,b,new ToolPoint(a.Center.X+ux*along-s*uy*half,a.Center.Y+uy*along+s*ux*half));
                    }
                }
            }
            return f;
        }

        private sealed class DistanceNode
        { internal double A0,A1,B0,B1,Lower; internal int Depth; internal CollisionBox ABox,BBox; }
        private sealed class MinHeap
        {
            private readonly List<DistanceNode> nodes=new List<DistanceNode>();
            internal int Count => nodes.Count;
            internal DistanceNode Top => nodes[0];
            internal void Push(DistanceNode n)
            { int i=nodes.Count;nodes.Add(n);while(i>0){int p=(i-1)/2;if(nodes[p].Lower<=n.Lower)break;nodes[i]=nodes[p];i=p;}nodes[i]=n; }
            internal DistanceNode Pop()
            {
                var n=nodes[0];var last=nodes[nodes.Count-1];nodes.RemoveAt(nodes.Count-1);if(nodes.Count==0)return n;
                int i=0;while(2*i+1<nodes.Count){int c=2*i+1;if(c+1<nodes.Count&&nodes[c+1].Lower<nodes[c].Lower)c++;if(last.Lower<=nodes[c].Lower)break;nodes[i]=nodes[c];i=c;}nodes[i]=last;return n;
            }
        }
        internal static CurveDistanceCertificate Separate(ToolCurve a, ToolCurve b, double threshold, CollisionWork work, CollisionPolicy2D policy)
        {
            work.Step();var features=Features(a,b);var heap=new MinHeap();
            Action<double,double,double,double,int> add=(a0,a1,b0,b1,depth)=>
            {
                var ab=Bounds(a,a0,a1);var bb=Bounds(b,b0,b1);
                heap.Push(new DistanceNode {A0=a0,A1=a1,B0=b0,B1=b1,Depth=depth,ABox=ab,BBox=bb,Lower=ab.LowerDistance(bb)});
            };
            add(0,1,0,1,0);
            while(heap.Count>0)
            {
                work.Step();double lower=heap.Top.Lower;
                if(lower>threshold) return new CurveDistanceCertificate {Lower=lower,Features=features,Separated=true};
                if(features.Upper<=threshold) return new CurveDistanceCertificate {Lower=lower,Features=features,Separated=false};
                var node=heap.Pop();
                if(node.Depth>=policy.MaximumSubdivisionDepth) throw new CollisionAbort("SUBDIVISION_OR_NUMERICAL_RESOLUTION_LIMIT");
                if(node.ABox.Size>=node.BBox.Size)
                {
                    double mid=node.A0/2+node.A1/2;
                    features.Add(a,mid,b,NearestParameter(b,Point(a,mid)));
                    add(node.A0,mid,node.B0,node.B1,node.Depth+1);add(mid,node.A1,node.B0,node.B1,node.Depth+1);
                }
                else
                {
                    double mid=node.B0/2+node.B1/2;
                    features.Add(a,NearestParameter(a,Point(b,mid)),b,mid);
                    add(node.A0,node.A1,node.B0,mid,node.Depth+1);add(node.A0,node.A1,mid,node.B1,node.Depth+1);
                }
            }
            throw new CollisionAbort("UNRESOLVED_DISTANCE");
        }
    }
}
