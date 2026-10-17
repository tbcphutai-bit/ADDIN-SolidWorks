using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendCollision
{
    internal enum CollisionStatus { UNKNOWN, CLEAR, COLLISION }
    internal enum CollisionPairKind { WorkpiecePunch, WorkpieceHolder }
    internal enum CollisionConfidence { Unresolved, CertifiedWithinNumericalPolicy }

    internal sealed class CollisionPolicy2D
    {
        public const string Version = "AnalyticMaterialBandsMmV1";
        public double ToleranceMm { get; }
        public int MaximumWork { get; }
        public int MaximumMilliseconds { get; }
        public int MaximumSubdivisionDepth { get; }
        public bool Valid => ToolPoint.Finite(ToleranceMm) && ToleranceMm >= 1e-7 && ToleranceMm <= .1 &&
            MaximumWork > 0 && MaximumWork <= 2000000 && MaximumMilliseconds > 0 && MaximumMilliseconds <= 10000 &&
            MaximumSubdivisionDepth > 0 && MaximumSubdivisionDepth <= 48;
        internal CollisionPolicy2D(double toleranceMm=1e-5, int maximumWork=250000, int maximumMilliseconds=1000, int maximumSubdivisionDepth=36)
        { ToleranceMm=toleranceMm; MaximumWork=maximumWork; MaximumMilliseconds=maximumMilliseconds; MaximumSubdivisionDepth=maximumSubdivisionDepth; }
    }

    internal sealed class CollisionContour2D
    {
        public string Id { get; }
        public IReadOnlyList<ToolCurve> Boundary { get; }
        public double UncertaintyMm { get; }
        public ToolRegionValidation Validation { get; }
        public bool AnalyticEndpointsConsistent { get; }
        public bool Valid => !string.IsNullOrWhiteSpace(Id) && ToolPoint.Finite(UncertaintyMm) && UncertaintyMm >= 0 &&
            UncertaintyMm <= 100 && AnalyticEndpointsConsistent && Validation?.IsValidRegion == true && Validation.MaximumJointGapMm <= 1e-10;
        // Only the placement adapter passes an existing validation, whose
        // topology is preserved by the already checked rigid transform.
        internal CollisionContour2D(string id, IEnumerable<ToolCurve> curves, double uncertaintyMm=0, ToolRegionValidation validation=null)
        {
            Id=id; Boundary=PlacementLists.Copy(curves); UncertaintyMm=uncertaintyMm;
            Validation=validation ?? ToolRegionValidator.Validate(new[]{new ToolDxfEntity {Type="COLLISION_CONTOUR", Curves=Boundary}});
            AnalyticEndpointsConsistent=Boundary.All(c=>c!=null && (c.Kind!="ARC" ||
                ToolPoint.Distance(CollisionGeometry.Point(c,0),c.Start)<=1e-10 && ToolPoint.Distance(CollisionGeometry.Point(c,1),c.End)<=1e-10));
        }
    }

    internal sealed class CollisionComponent2D
    {
        public CollisionContour2D Outer { get; }
        public IReadOnlyList<CollisionContour2D> Holes { get; }
        internal CollisionComponent2D(CollisionContour2D outer, IEnumerable<CollisionContour2D> holes=null)
        { Outer=outer; Holes=PlacementLists.Copy(holes); }
    }

    // Explicit union of component materials, each subtracting ONLY its own
    // holes. Islands are independent components. This never infers fill roles.
    internal sealed class CollisionMaterial2D
    {
        private readonly Func<bool> current;
        public string FrameId { get; }
        public string Provenance { get; }
        public bool SimulationOnly { get; }
        public IReadOnlyList<CollisionComponent2D> Components { get; }
        public IReadOnlyList<CollisionContour2D> Contours { get; }
        public string InvalidReason { get; }
        public bool Current { get { try { return current(); } catch { return false; } } }
        public bool Valid => InvalidReason == null;
        internal CollisionMaterial2D(string frame, IEnumerable<CollisionComponent2D> components, string provenance,
            bool simulationOnly, Func<bool> current=null, bool hierarchyAlreadyValidated=false)
        {
            FrameId=frame; Provenance=provenance; SimulationOnly=simulationOnly; this.current=current ?? (()=>true);
            Components=PlacementLists.Copy(components);
            Contours=PlacementLists.Copy(Components.Where(c=>c!=null).SelectMany(c=>new[]{c.Outer}.Concat(c.Holes)).Where(c=>c!=null).Distinct());
            if(string.IsNullOrWhiteSpace(frame)||string.IsNullOrWhiteSpace(provenance)) InvalidReason="MISSING_FRAME_OR_PROVENANCE";
            else if(Components.Count==0 || Components.Count>128 || Components.Any(c=>c==null || c.Outer==null || c.Holes.Any(h=>h==null)) ||
                Contours.Any(c=>!c.Valid) || Contours.Sum(c=>c.Boundary.Count)>8192 || Contours.GroupBy(c=>c.Id,StringComparer.Ordinal).Any(g=>g.Count()>1))
                InvalidReason="INVALID_OR_EMPTY_MATERIAL_TOPOLOGY";
            else if(!hierarchyAlreadyValidated)
            {
                foreach(var c in Components)
                {
                    var loops=new[]{c.Outer}.Concat(c.Holes).ToArray();
                    for(int i=0;i<loops.Length;i++) for(int j=i+1;j<loops.Length;j++)
                        if(ToolRegionValidator.BoundariesContact(loops[i].Boundary,loops[j].Boundary,1e-5,CancellationToken.None))
                            InvalidReason="INVALID_TOUCHING_COMPONENT_HIERARCHY";
                    foreach(var h in c.Holes)
                        if(!ToolRegionValidator.ContainsPoint(c.Outer.Boundary,h.Boundary[0].Start)) InvalidReason="HOLE_OUTSIDE_ITS_COMPONENT";
                    for(int i=0;i<c.Holes.Count;i++) for(int j=i+1;j<c.Holes.Count;j++)
                        if(ToolRegionValidator.ContainsPoint(c.Holes[i].Boundary,c.Holes[j].Boundary[0].Start) ||
                           ToolRegionValidator.ContainsPoint(c.Holes[j].Boundary,c.Holes[i].Boundary[0].Start)) InvalidReason="NESTED_HOLES_REQUIRE_EXPLICIT_ISLAND_COMPONENT";
                }
            }
        }
    }

    internal sealed class CollisionQuery2D
    {
        private readonly Func<bool> current;
        public CollisionPairKind Pair { get; }
        public CollisionMaterial2D Workpiece { get; }
        public CollisionMaterial2D Obstacle { get; }
        public string StateId { get; }
        public CollisionPolicy2D Policy { get; }
        public IReadOnlyList<string> MissingEvidence { get; }
        public bool Current { get { try { return current() && Workpiece?.Current != false && Obstacle?.Current != false; } catch { return false; } } }
        internal CollisionQuery2D(CollisionPairKind pair, CollisionMaterial2D workpiece, CollisionMaterial2D obstacle, string stateId,
            CollisionPolicy2D policy=null, Func<bool> current=null, IEnumerable<string> missingEvidence=null)
        { Pair=pair; Workpiece=workpiece; Obstacle=obstacle; StateId=stateId; Policy=policy??new CollisionPolicy2D();
            this.current=current??(()=>true); MissingEvidence=PlacementLists.Reasons(missingEvidence??new string[0]); }
    }

    internal sealed class CollisionWitness2D
    {
        public ToolPoint PointMm { get; }
        public double InteriorMarginLowerMm { get; }
        public string WorkpieceComponent { get; }
        public string ObstacleComponent { get; }
        internal CollisionWitness2D(ToolPoint p, double margin, string a, string b)
        { PointMm=p; InteriorMarginLowerMm=margin; WorkpieceComponent=a; ObstacleComponent=b; }
    }

    internal sealed class CollisionResult2D
    {
        private readonly CollisionQuery2D query;
        private readonly CollisionStatus observed;
        private readonly IReadOnlyList<string> reasons;
        private readonly double? clearance;
        private readonly CollisionWitness2D witness;
        private readonly double? nominalUpper;
        public CollisionPairKind Pair => query.Pair;
        public string StateId => query.StateId;
        public string FrameId => query.Workpiece?.FrameId;
        public string WorkpieceProvenance => query.Workpiece?.Provenance;
        public string ObstacleProvenance => query.Obstacle?.Provenance;
        public bool SimulationOnly => query.Workpiece?.SimulationOnly == true || query.Obstacle?.SimulationOnly == true;
        public bool Current => query.Current;
        public CollisionStatus Status => Current ? observed : CollisionStatus.UNKNOWN;
        public CollisionConfidence Confidence => Status==CollisionStatus.UNKNOWN ? CollisionConfidence.Unresolved : CollisionConfidence.CertifiedWithinNumericalPolicy;
        public string PolicyVersion => CollisionPolicy2D.Version;
        public double ToleranceMm => query.Policy.ToleranceMm;
        public double NumericalGuardMm { get; }
        public int WorkUnits { get; }
        public double? CertifiedClearanceLowerMm => Status==CollisionStatus.CLEAR ? clearance : null;
        // A witnessed distance on the nominal boundaries, not a certified
        // physical clearance upper bound for an uncertain source curve.
        public double? NominalBoundaryDistanceUpperMm => Current ? nominalUpper : null;
        public CollisionWitness2D Witness => Status==CollisionStatus.COLLISION ? witness : null;
        public IReadOnlyList<string> Reasons => PlacementLists.Reasons(reasons.Concat(Current?new string[0]:new[]{"STALE_COLLISION_INPUT"}));
        internal CollisionResult2D(CollisionQuery2D query, CollisionStatus status, IEnumerable<string> reasons, int work=0,
            double numericalGuard=0, double? clearance=null, double? nominalUpper=null, CollisionWitness2D witness=null)
        { this.query=query; observed=status; this.reasons=PlacementLists.Reasons(reasons); WorkUnits=work;
            NumericalGuardMm=numericalGuard; this.clearance=clearance; this.nominalUpper=nominalUpper; this.witness=witness; }
        internal static CollisionResult2D Unknown(CollisionQuery2D query, params string[] reasons) => new CollisionResult2D(query,CollisionStatus.UNKNOWN,reasons);
    }
}
