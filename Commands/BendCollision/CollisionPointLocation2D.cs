using System;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendCollision
{
    internal enum MaterialLocation { Unknown, Outside, Inside }
    internal struct MaterialPointProof
    {
        internal MaterialLocation Location;
        internal double Margin;
        internal string Component;
    }

    internal static class CollisionPointLocation
    {
        internal static MaterialPointProof Locate(CollisionMaterial2D material, ToolPoint point, CollisionWork work, double tolerance)
        {
            bool unresolved=false;
            foreach(var component in material.Components)
            {
                var outer=Contour(component.Outer,point,work,tolerance);
                if(outer.Location==MaterialLocation.Outside) continue;
                bool excluded=false,unknown=outer.Location==MaterialLocation.Unknown;
                double margin=outer.Margin;
                foreach(var hole in component.Holes)
                {
                    var h=Contour(hole,point,work,tolerance);
                    if(h.Location==MaterialLocation.Inside) { excluded=true; break; }
                    if(h.Location==MaterialLocation.Unknown) unknown=true;
                    margin=Math.Min(margin,h.Margin);
                }
                if(excluded) continue;
                if(!unknown) return new MaterialPointProof {Location=MaterialLocation.Inside,Margin=margin,Component=component.Outer.Id};
                unresolved=true;
            }
            return new MaterialPointProof {Location=unresolved?MaterialLocation.Unknown:MaterialLocation.Outside};
        }
        internal static MaterialPointProof Contour(CollisionContour2D contour, ToolPoint p, CollisionWork work, double tolerance)
        {
            double lower=double.PositiveInfinity,guard=0;
            foreach(var c in contour.Boundary)
            { work.Step(); lower=Math.Min(lower,CollisionGeometry.PointDistanceLower(c,p)); guard=Math.Max(guard,8*CollisionGeometry.Guard(c)); }
            double margin=(CollisionInterval.P(lower)-CollisionInterval.P(contour.UncertaintyMm)-CollisionInterval.P(guard)).Lo;
            if(margin<=tolerance) return new MaterialPointProof {Location=MaterialLocation.Unknown};
            // Analytic ray counting, with alternate rays when a vertex or a
            // tangency is numerically unresolved. No sampled polyline replaces
            // an ARC, and an ambiguous ray is never counted as outside.
            foreach(var direction in new[]{new[]{1,0},new[]{4,1},new[]{2,1},new[]{1,1},new[]{1,2},new[]{0,1},new[]{-1,2},new[]{-3,1}})
            {
                bool inside;
                if(TryRay(contour,p,direction[0],direction[1],guard,work,out inside))
                    return new MaterialPointProof {Location=inside?MaterialLocation.Inside:MaterialLocation.Outside,Margin=margin};
            }
            return new MaterialPointProof {Location=MaterialLocation.Unknown};
        }
        private struct RayPoint
        {
            internal CollisionInterval X,Y;
            internal RayPoint(CollisionInterval x,CollisionInterval y) { X=x;Y=y; }
        }
        private static CollisionInterval Cross(RayPoint a,RayPoint b)=>a.X*b.Y-a.Y*b.X;
        private static bool TryRay(CollisionContour2D contour, ToolPoint p, int dx, int dy, double guard, CollisionWork work, out bool inside)
        {
            int crossings=0;inside=false;var ix=CollisionInterval.P(dx);var iy=CollisionInterval.P(dy);
            double rayGuard=guard*(Math.Abs(dx)+Math.Abs(dy));
            Func<CollisionInterval,CollisionInterval,RayPoint> project=(x,y)=>new RayPoint(ix*x+iy*y,ix*y-iy*x);
            Func<ToolPoint,RayPoint> relative=q=>project(CollisionInterval.P(q.X)-CollisionInterval.P(p.X),CollisionInterval.P(q.Y)-CollisionInterval.P(p.Y));
            foreach(var c in contour.Boundary)
            {
                work.Step();var a=relative(c.Start);var b=relative(c.End);
                if(c.Kind=="LINE")
                {
                    if(NearZero(a.Y,rayGuard)||NearZero(b.Y,rayGuard))return false;
                    if((a.Y.Lo>0)!=(b.Y.Lo>0))
                    {
                        var x=a.X-a.Y*(b.X-a.X)/(b.Y-a.Y);
                        if(NearZero(x,rayGuard))return false;
                        if(x.Lo>0)crossings++;
                    }
                }
                else
                {
                    var center=relative(c.Center);var radius=CollisionInterval.P(c.RadiusMm);
                    bool full=Math.Abs(c.SweepRadians)==CollisionGeometry.Tau;
                    RayPoint start=default(RayPoint),end=default(RayPoint);
                    if(!full)
                    {
                        var vx=CollisionInterval.P(c.Start.X)-CollisionInterval.P(c.Center.X);
                        var vy=CollisionInterval.P(c.Start.Y)-CollisionInterval.P(c.Center.Y);
                        var norm=(vx.Square()+vy.Square()).Sqrt();vx=radius*(vx/norm);vy=radius*(vy/norm);
                        CollisionInterval sin,cos;CollisionInterval.SinCos(CollisionInterval.P(c.SweepRadians),out sin,out cos);
                        start=project(vx,vy);end=project(vx*cos-vy*sin,vx*sin+vy*cos);
                        if(NearZero(center.Y+start.Y,rayGuard)||NearZero(center.Y+end.Y,rayGuard))return false;
                    }
                    // Integer ray directions avoid trigonometric predicates.
                    // Every projection, discriminant and crossing is outward
                    // interval arithmetic; unresolved tangencies choose a new
                    // ray rather than silently adding/removing a crossing.
                    var discriminant=radius.Square()*(ix.Square()+iy.Square())-center.Y.Square();
                    if(discriminant.Hi<0)continue;
                    if(discriminant.Lo<=0)return false;
                    var half=discriminant.Sqrt();
                    foreach(double sign in new[]{-1.0,1.0})
                    {
                        var offset=CollisionInterval.P(sign)*half;var x=center.X+offset;
                        if(NearZero(x,rayGuard))return false;
                        if(x.Hi<0)continue;
                        if(!full)
                        {
                            var hit=new RayPoint(offset,CollisionInterval.P(0)-center.Y);
                            var s=CollisionInterval.P(Math.Sign(c.SweepRadians));
                            var first=Cross(start,hit)*s;var second=Cross(hit,end)*s;
                            double pad=4*guard*c.RadiusMm*(dx*dx+dy*dy);
                            bool positiveA=first.Lo>pad,positiveB=second.Lo>pad,negativeA=first.Hi<-pad,negativeB=second.Hi<-pad;
                            bool onArc;
                            if(Math.Abs(c.SweepRadians)<Math.PI-1e-12)
                            { if(negativeA||negativeB)onArc=false;else if(positiveA&&positiveB)onArc=true;else return false; }
                            else if(Math.Abs(c.SweepRadians)>Math.PI+1e-12)
                            { if(positiveA||positiveB)onArc=true;else if(negativeA&&negativeB)onArc=false;else return false; }
                            else
                            { if(positiveA&&positiveB)onArc=true;else if(negativeA&&negativeB)onArc=false;else return false; }
                            if(!onArc)continue;
                        }
                        crossings++;
                    }
                }
            }
            inside=(crossings&1)!=0;return true;
        }
        private static bool NearZero(CollisionInterval n,double guard)=>n.Lo<=guard&&n.Hi>=-guard;
    }
}
