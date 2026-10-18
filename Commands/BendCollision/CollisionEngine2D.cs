using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendCollision
{
    internal static class CollisionEngine2D
    {
        internal static CollisionResult2D Evaluate(CollisionQuery2D query, CancellationToken cancellation=default(CancellationToken))
        {
            var observer=CollisionTimingScope.Observer;
            if(observer==null)return EvaluateCore(query,cancellation);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            try{return EvaluateCore(query,cancellation);}
            finally{try{observer(timer.Elapsed.TotalMilliseconds);}catch{ /* Timing cannot change geometry. */ }}
        }
        private static CollisionResult2D EvaluateCore(CollisionQuery2D query, CancellationToken cancellation)
        {
            if(query==null) throw new ArgumentNullException(nameof(query));
            var work=new CollisionWork(query,cancellation);double guard=0;
            try
            {
                if(!query.Policy.Valid) return CollisionResult2D.Unknown(query,"INVALID_COLLISION_POLICY");
                work.Step();
                if(query.MissingEvidence.Count>0) return new CollisionResult2D(query,CollisionStatus.UNKNOWN,query.MissingEvidence,work.Count);
                var a=query.Workpiece;var b=query.Obstacle;
                if(a==null || b==null) return CollisionResult2D.Unknown(query,"MISSING_MATERIAL_ENVELOPE");
                if(!a.Valid || !b.Valid) return CollisionResult2D.Unknown(query,a.InvalidReason??b.InvalidReason);
                if(a.FrameId!=b.FrameId) return CollisionResult2D.Unknown(query,"FRAME_MISMATCH");
                if(string.IsNullOrWhiteSpace(query.StateId)) return CollisionResult2D.Unknown(query,"UNKNOWN_WORKPIECE_STATE");
                guard=8*a.Contours.Concat(b.Contours).SelectMany(c=>c.Boundary).Max(CollisionGeometry.Guard);
                var ab=CollisionBox.Union(a.Contours.SelectMany(c=>c.Boundary).Select(c=>CollisionGeometry.Bounds(c)));
                var bb=CollisionBox.Union(b.Contours.SelectMany(c=>c.Boundary).Select(c=>CollisionGeometry.Bounds(c)));
                // Include the declared source-endpoint/joint roundoff guard
                // as well as SPLINE/placement bands in every CLEAR proof.
                double maxBand=(CollisionInterval.P(a.Contours.Max(c=>c.UncertaintyMm))+CollisionInterval.P(b.Contours.Max(c=>c.UncertaintyMm))+CollisionInterval.P(guard)).Hi;
                bool enclosingBoxesSeparate=ab.LowerDistance(bb)>(CollisionInterval.P(maxBand)+CollisionInterval.P(query.Policy.ToleranceMm)).Hi;
                CollisionWitness2D witness;
                if(!enclosingBoxesSeparate)
                    foreach(var component in a.Components.Concat(b.Components))
                    {
                        var center=CollisionBox.Union(component.Outer.Boundary.Select(c=>CollisionGeometry.Bounds(c))).Center;
                        if(TryWitness(query,center,work,out witness)) return Hit(query,work,guard,witness);
                    }

                bool allSeparated=true;double lower=double.PositiveInfinity,upper=double.PositiveInfinity;
                foreach(var ac in a.Contours) foreach(var bc in b.Contours)
                    foreach(var ae in ac.Boundary) foreach(var be in bc.Boundary)
                    {
                        double bands=(CollisionInterval.P(ac.UncertaintyMm)+CollisionInterval.P(bc.UncertaintyMm)+CollisionInterval.P(guard)).Hi;
                        var distance=CollisionGeometry.Separate(ae,be,(CollisionInterval.P(bands)+CollisionInterval.P(query.Policy.ToleranceMm)).Hi,work,query.Policy);
                        lower=Math.Min(lower,Math.Max(0,(CollisionInterval.P(distance.Lower)-CollisionInterval.P(bands)).Lo));upper=Math.Min(upper,distance.Features.Upper);
                        if(!distance.Separated)
                        {
                            allSeparated=false;
                            if(FeatureWitness(query,ae,be,distance.Features,work,out witness)) return Hit(query,work,guard,witness);
                        }
                    }
                if(!allSeparated) return new CollisionResult2D(query,CollisionStatus.UNKNOWN,new[]{"CONTACT_OR_UNCERTAINTY_BAND"},work.Count,guard,nominalUpper:upper);

                // A representative of EACH closed contour is sufficient only
                // after every cross-material boundary pair has a positive
                // separation certificate. Its location is then constant on
                // that connected contour. Check both directions, including
                // holes/islands; no boundary crossing does not imply no fill.
                bool ambiguous=false,possibleContainment=false;
                if(!enclosingBoxesSeparate) foreach(bool reverse in new[]{false,true})
                {
                    var first=reverse?b:a;var other=reverse?a:b;
                    foreach(var contour in first.Contours)
                    {
                        var edge=contour.Boundary[0];var p=CollisionGeometry.Point(edge,.371);
                        var proof=CollisionPointLocation.Locate(other,p,work,query.Policy.ToleranceMm);
                        if(proof.Location==MaterialLocation.Unknown) ambiguous=true;
                        if(proof.Location==MaterialLocation.Inside)
                        {
                            possibleContainment=true;
                            if(Probe(query,p,CollisionGeometry.Normal(edge,.371),new ToolPoint(0,0),
                                Math.Min(CollisionGeometry.Length(edge)/8,proof.Margin/4),work,out witness)) return Hit(query,work,guard,witness);
                        }
                    }
                }
                work.Step();
                if(ambiguous || possibleContainment) return new CollisionResult2D(query,CollisionStatus.UNKNOWN,
                    new[]{ambiguous?"POINT_CLASSIFICATION_UNRESOLVED":"OVERLAP_NOT_ROBUST_OUTSIDE_UNCERTAINTY"},work.Count,guard,nominalUpper:upper);
                if(lower<=query.Policy.ToleranceMm) return CollisionResult2D.Unknown(query,"CLEARANCE_NOT_PROVEN");
                return new CollisionResult2D(query,CollisionStatus.CLEAR,new[]{"CERTIFIED_BOUNDARY_SEPARATION_AND_MATERIAL_EXCLUSION"},work.Count,guard,lower,upper);
            }
            catch(CollisionAbort ex) { return new CollisionResult2D(query,CollisionStatus.UNKNOWN,new[]{ex.Message},work.Count,guard); }
            catch(Exception ex) when(ex is ArgumentException || ex is InvalidOperationException || ex is System.IO.InvalidDataException || ex is ArithmeticException)
            { return new CollisionResult2D(query,CollisionStatus.UNKNOWN,new[]{"COLLISION_NUMERICAL_OR_INPUT_FAILURE"},work.Count,guard); }
        }
        private static CollisionResult2D Hit(CollisionQuery2D query, CollisionWork work, double guard, CollisionWitness2D witness)
        { work.Step();return new CollisionResult2D(query,CollisionStatus.COLLISION,new[]{"CERTIFIED_MATERIAL_INTERIOR_WITNESS"},work.Count,guard,witness:witness); }
        private static bool TryWitness(CollisionQuery2D q, ToolPoint p, CollisionWork work, out CollisionWitness2D witness)
        {
            work.Step();witness=null;
            var a=CollisionPointLocation.Locate(q.Workpiece,p,work,q.Policy.ToleranceMm);
            if(a.Location!=MaterialLocation.Inside) return false;
            var b=CollisionPointLocation.Locate(q.Obstacle,p,work,q.Policy.ToleranceMm);
            if(b.Location!=MaterialLocation.Inside) return false;
            witness=new CollisionWitness2D(p,Math.Min(a.Margin,b.Margin),a.Component,b.Component);return true;
        }
        private static bool FeatureWitness(CollisionQuery2D q, ToolCurve a, ToolCurve b, CurvePairFeatures features, CollisionWork work, out CollisionWitness2D witness)
        {
            var seeds=features.Contacts.Take(8).ToList();
            if(features.Contacts.Count>=2) seeds.Add(new ToolPoint(features.Contacts[0].X/2+features.Contacts.Last().X/2,features.Contacts[0].Y/2+features.Contacts.Last().Y/2));
            seeds.Add(new ToolPoint(features.APoint.X/2+features.BPoint.X/2,features.APoint.Y/2+features.BPoint.Y/2));
            foreach(var seed in seeds)
                if(Probe(q,seed,CollisionGeometry.Normal(a,CollisionGeometry.NearestParameter(a,seed)),
                    CollisionGeometry.Normal(b,CollisionGeometry.NearestParameter(b,seed)),Math.Min(CollisionGeometry.Length(a),CollisionGeometry.Length(b))/8,work,out witness)) return true;
            witness=null;return false;
        }
        private static bool Probe(CollisionQuery2D q, ToolPoint seed, ToolPoint n, ToolPoint m, double scale, CollisionWork work, out CollisionWitness2D witness)
        {
            if(TryWitness(q,seed,work,out witness)) return true;
            var directions=new List<ToolPoint> {n,m,new ToolPoint(n.X+m.X,n.Y+m.Y),new ToolPoint(n.X-m.X,n.Y-m.Y)};
            double step=Math.Max(q.Policy.ToleranceMm*8,Math.Min(10000,scale));
            for(int level=0;level<32 && step>q.Policy.ToleranceMm;level++,step/=2)
                foreach(var d in directions)
                {
                    double length=Math.Sqrt(d.X*d.X+d.Y*d.Y);if(length<1e-12)continue;
                    foreach(int sign in new[]{-1,1})
                        if(TryWitness(q,new ToolPoint(seed.X+sign*step*d.X/length,seed.Y+sign*step*d.Y/length),work,out witness)) return true;
                }
            witness=null;return false;
        }
    }
}
