using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlacement
{
    internal sealed class HolderPlacedMaterialRegion
    {
        public string OuterId { get; }
        public IReadOnlyList<string> HoleIds { get; }
        internal HolderPlacedMaterialRegion(HolderMaterialRegion region){OuterId=region.Outer.Id;HoleIds=PlacementLists.Copy(region.Holes.Select(h=>h.Id));}
    }
    internal sealed class HolderPlacedContour
    {
        public string Id { get; }
        public string ParentId { get; }
        public string MaterialKind { get; }
        public IReadOnlyList<ToolCurve> NominalBoundary { get; }
        public double BoundaryBandRadiusMm { get; }
        public double PlacementUncertaintyMm { get; }
        internal HolderPlacedContour(HolderContourRegion source,PlacementTransform2D transform)
        {
            Id=source.Id;ParentId=source.ParentId;MaterialKind=source.MaterialKind;
            NominalBoundary=PlacementLists.Copy(source.Boundary.Select(transform.Apply));
            // The rigid validator permits 1e-10 orthonormal roundoff. Bound
            // both that drift (including circular representation) and the
            // binary64 affine evaluation; do not silently drop source bands
            // when the nominal chords move to the work frame.
            double magnitude=source.Boundary.Max(c=>new[]{c.Start,c.End,c.Center}.Max(p=>Math.Max(Math.Abs(p.X),Math.Abs(p.Y)))+c.RadiusMm);
            double radius=source.Boundary.Max(c=>c.RadiusMm);
            const double epsilon=2.2204460492503131e-16;
            PlacementUncertaintyMm=radius*1e-9+64*epsilon*(4*magnitude+Math.Abs(transform.TxMm)+Math.Abs(transform.TyMm)+1);
            BoundaryBandRadiusMm=source.BoundaryUncertaintyMm*(1+1e-9)+PlacementUncertaintyMm;
        }
    }
    // A future collision consumer MUST use the material hierarchy AND the
    // closed uncertainty bands, including around holes. Raw drawing curves in
    // ToolGeometryPlacement2D are an inspection view, not collision obstacles.
    internal sealed class HolderObstaclePlacement2D
    {
        private readonly HolderObstacleSet source;
        private readonly bool placed;
        public string WorkFrameId { get; }
        public string EnvelopeContract => source?.EnvelopeContract;
        public string PlacementErrorPolicy => "ConservativeRigidPlacementMmV1";
        public string Reason { get; }
        public bool HasConservativeGeometry => placed&&source?.GeometrySupportedForCollision==true;
        public IReadOnlyList<HolderPlacedContour> Contours { get; }
        public IReadOnlyList<HolderPlacedMaterialRegion> MaterialRegions { get; }
        public string RegionCombination => source?.RegionCombination;
        internal HolderObstaclePlacement2D(HolderObstacleSet source,PlacementTransform2D transform,string workFrame)
        {
            this.source=source;WorkFrameId=workFrame;
            bool domain=transform==null||Math.Abs(transform.TxMm)<=1e7&&Math.Abs(transform.TyMm)<=1e7;
            Reason=domain?null:"UNSUPPORTED_HOLDER_PLACEMENT_COORDINATE_DOMAIN";
            placed=transform!=null&&workFrame!=null&&domain;
            Contours=PlacementLists.Copy(!placed||source?.GeometrySupportedForCollision!=true?null:source.Contours.Select(c=>new HolderPlacedContour(c,transform)));
            MaterialRegions=PlacementLists.Copy(!placed||source?.GeometrySupportedForCollision!=true?null:source.Regions.Select(r=>new HolderPlacedMaterialRegion(r)));
        }
    }
}
