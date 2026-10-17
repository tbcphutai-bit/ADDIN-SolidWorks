using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendCollision
{
    internal static class PlacementCollisionEvaluator
    {
        internal static CandidateCollisionResult Evaluate(ToolCandidateDraft candidate)
        {
            var states=new List<BendStateCollisionResult>();
            foreach(var state in candidate.BendStates)
            {
                var punch=Pair(candidate,state,CollisionPairKind.WorkpiecePunch);
                var holder=Pair(candidate,state,CollisionPairKind.WorkpieceHolder);
                states.Add(new BendStateCollisionResult(state.Coverage,punch,holder));
            }
            return new CandidateCollisionResult(candidate.ToolId,candidate.FeatureId,candidate.SimulationOnly,states);
        }
        private static CollisionResult2D Pair(ToolCandidateDraft c,BendStateDraft2D state,CollisionPairKind kind)
        {
            var missing=new List<string>();CollisionMaterial2D workpiece=null,obstacle=null;var assembly=c.Assembly;
            Func<bool> current=()=>c.Current && assembly?.MasterDatums?.Status!=DatumEvidenceStatus.Stale &&
                (c.ProductionCollisionPose==null||c.ProductionCollisionPose.Approved) &&
                (kind==CollisionPairKind.WorkpiecePunch || assembly?.MappingReview.Status!=ToolReviewStatus.Stale &&
                    assembly?.SeatingEvidence.Status!=ToolReviewStatus.Stale && assembly?.HolderObstacles?.Current!=false);
            try
            {
                if(state.Section==null || state.Coverage!=BendStateCoverage.FinalModelObserved)missing.Add("UNKNOWN_WORKPIECE_STATE");
                if(c.Sheet.Coverage==SheetCoverage.Unsupported2DEnvelope || c.Sheet.Region?.IsValidRegion!=true || c.Sheet.Boundary.Count==0)
                    missing.Add("UNSUPPORTED_2D_ENVELOPE_MULTIPLE_SLICES_OR_3D_REQUIRED");
                if(!c.HasExplicitCollisionPose)missing.Add("MISSING_VERIFIED_CONTACT_APPROACH_OR_DATUM_MATCH");
                if(assembly?.IdentityMatched!=true)missing.Add("UNVERIFIED_TOOL_IDENTITY");
                if(assembly?.MasterDatums?.Status!=DatumEvidenceStatus.VerifiedRecord)missing.Add("MISSING_DATUM_EVIDENCE");
                if(c.PunchPlacement.ToWorkFrame==null)missing.Add("MISSING_PUNCH_PLACEMENT");
                if(c.Sheet.Frame==null || c.PunchPlacement.WorkFrameId!=c.Sheet.Frame.FrameId || c.HolderPlacement.WorkFrameId!=c.Sheet.Frame.FrameId)
                    missing.Add("FRAME_MISMATCH");
                if(!c.SimulationOnly && c.ProductionCollisionPose?.Approved!=true)missing.Add("UNVERIFIED_PRODUCTION_WORK_POSE");
                if(kind==CollisionPairKind.WorkpiecePunch)
                {
                    if(assembly?.Punch==null || !assembly.Punch.Supported || !assembly.Punch.ObservedClosed || !assembly.Punch.Region.IsValidRegion || assembly.Punch.Region.MaximumJointGapMm>1e-10)
                        missing.Add("OPEN_UNSUPPORTED_OR_INVALID_PUNCH_REGION");
                }
                else
                {
                    if(assembly?.MappingIdentified!=true || assembly.MappingReview.RecordVerified!=true)missing.Add("MAPPING_UNRESOLVED_OR_UNREVIEWED");
                    if(assembly?.SeatingEvidence.CoordinateRecordReady!=true)missing.Add("UNVERIFIED_PAIR_PLACEMENT");
                    if(assembly?.HolderObstacles?.GeometrySupportedForCollision!=true || !c.HolderObstaclePlacement.HasConservativeGeometry)
                        missing.Add("UNSUPPORTED_HOLDER_FILL_RULE_OR_MATERIAL");
                    if(c.HolderObstaclePlacement.RegionCombination!="UnionOfEachOuterMinusItsOwnHoles" ||
                        c.HolderObstaclePlacement.EnvelopeContract!="NominalMaterialUnionPlusEveryBoundaryUncertaintyBandV1")missing.Add("UNSUPPORTED_HOLDER_ENVELOPE_CONTRACT");
                }
                if(missing.Count==0)
                {
                    string frame=c.Sheet.Frame.FrameId;
                    var sheet=new CollisionContour2D("WHOLE_SHEET",c.Sheet.Boundary,validation:c.Sheet.Region);
                    workpiece=new CollisionMaterial2D(frame,new[]{new CollisionComponent2D(sheet)},CollisionWorkPoseEvidence.SheetHash(c.Sheet),c.SimulationOnly,current,true);
                    if(kind==CollisionPairKind.WorkpiecePunch)
                    {
                        double uncertainty=PlacementUncertainty(assembly.Punch.Curves,c.PunchPlacement.ToWorkFrame);
                        var punch=new CollisionContour2D("PUNCH",c.PunchPlacement.PlacedCurves,uncertainty,assembly.Punch.Region);
                        obstacle=new CollisionMaterial2D(frame,new[]{new CollisionComponent2D(punch)},assembly.Punch.GeometrySha256,c.SimulationOnly,current,true);
                    }
                    else
                    {
                        // Consume the complete reviewed material expression and
                        // transformed SPLINE bands. The raw drawing Curves
                        // subset is intentionally never used for a holder.
                        var contours=c.HolderObstaclePlacement.Contours.ToDictionary(h=>h.Id,h=>new CollisionContour2D(h.Id,h.NominalBoundary,h.BoundaryBandRadiusMm,
                            assembly.HolderObstacles.Contours.Single(s=>s.Id==h.Id).Validation),StringComparer.Ordinal);
                        var regions=c.HolderObstaclePlacement.MaterialRegions.Select(r=>new CollisionComponent2D(contours[r.OuterId],r.HoleIds.Select(id=>contours[id])));
                        obstacle=new CollisionMaterial2D(frame,regions,assembly.HolderObstacles.DefinitionSha256,c.SimulationOnly,current,true);
                    }
                }
            }
            catch(Exception ex) when(ex is ArgumentException || ex is InvalidOperationException || ex is System.IO.InvalidDataException)
            { missing.Add("COLLISION_INPUT_ADAPTER_FAILURE"); }
            var query=new CollisionQuery2D(kind,workpiece,obstacle,state.Coverage.ToString(),current:current,missingEvidence:missing);
            return CollisionEngine2D.Evaluate(query);
        }
        internal static double PlacementUncertainty(IEnumerable<ToolCurve> curves,PlacementTransform2D t)
        {
            var a=curves.ToArray();double magnitude=a.Max(c=>new[]{c.Start,c.End,c.Center}.Max(p=>Math.Max(Math.Abs(p.X),Math.Abs(p.Y)))+c.RadiusMm);
            return a.Max(c=>c.RadiusMm)*1e-9+64*2.2204460492503131e-16*(4*magnitude+Math.Abs(t.TxMm)+Math.Abs(t.TyMm)+1);
        }
    }
}
