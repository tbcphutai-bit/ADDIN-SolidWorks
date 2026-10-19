using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlanning
{
    // An optional, explicitly reviewed SOFTWARE simulation pose for the exact
    // CAD selection. No tip, seating, physical length or material role is
    // inferred from the presentation. Original source entities stay unchanged.
    internal sealed class CadReferenceSimulationRecord
    {
        public string SourceSha256 { get; set; }
        public string ManifestSha256 { get; set; }
        public string SelectionGeometrySha256 { get; set; }
        public string AxialSectionContract { get; set; }
        public PairTransformRecord SourceToWorkFrame { get; set; }
        public double? StartMm { get; set; }
        public double? EndMm { get; set; }
        public double? BoundaryErrorMm { get; set; }
        public double? AxialErrorMm { get; set; }
        public ToolReviewProof Review { get; set; }
    }
    internal static class CadReferenceSimulation
    {
        internal static string Payload(CadReferenceSimulationRecord r,string tool,CollisionPairKind pair,SheetEnvelopeCapture sheet)
        {
            if(r==null||!r.StartMm.HasValue||!r.EndMm.HasValue||!r.BoundaryErrorMm.HasValue||!r.AxialErrorMm.HasValue||r.SourceToWorkFrame==null)
                throw new InvalidDataException("MISSING_CAD_SIMULATION_POSE_EXTENT_OR_ERROR");
            var t=r.SourceToWorkFrame.Read();
            return ToolEvidenceCanonical.Hash("FixedCadPairSimulationV1",tool,pair.ToString(),r.SourceSha256,r.ManifestSha256,r.SelectionGeometrySha256,
                sheet.Binding.ModelSnapshotId,sheet.Binding.UpdateStamp.ToString(System.Globalization.CultureInfo.InvariantCulture),sheet.Binding.Configuration,
                sheet.Binding.FeatureId.ToString(System.Globalization.CultureInfo.InvariantCulture),sheet.Binding.FrameId,sheet.GeometrySha256,r.AxialSectionContract,
                ToolEvidenceCanonical.Hash(new[]{t.A,t.B,t.C,t.D,t.TxMm,t.TyMm,r.StartMm.Value,r.EndMm.Value,r.BoundaryErrorMm.Value,r.AxialErrorMm.Value}.Select(ToolEvidenceCanonical.Number).ToArray()),
                r.SourceToWorkFrame.MirrorApproved.ToString());
        }
        internal static ProcessToolPose Read(CadReferenceSimulationRecord record,string id,CollisionPairKind pair,SheetEnvelopeCapture sheet,
            ToolProfileLibrarySnapshot library,ProcessMotionLaw law,ToolReviewVerifier verifier,string root,CancellationToken token)
        {
            var json=new JavaScriptSerializer();var r=json.Deserialize<CadReferenceSimulationRecord>(json.Serialize(record));
            token.ThrowIfCancellationRequested();var assembly=library?.PlacementSources?.Tools.SingleOrDefault(t=>t.Id==id);
            if(assembly?.CadPair?.Matched!=true||!assembly.MappingIdentified)throw new InvalidDataException("MISSING_FIXED_CAD_PAIR");
            var geometry=pair==CollisionPairKind.WorkpiecePunch?assembly.Punch:assembly.Holder;
            if(r.SourceSha256!=assembly.MasterSha256||r.ManifestSha256!=assembly.ManifestSha256||r.SelectionGeometrySha256!=geometry.GeometrySha256)
                throw new InvalidDataException("STALE_CAD_SIMULATION_SOURCE_OR_PAIR");
            string payload=Payload(r,id,pair,sheet);var review=verifier.Verify("CadReferenceSimulation",payload,r.Review,root,token);
            if(!review.RecordVerified||!review.SimulationOnly)throw new InvalidDataException("CAD_SIMULATION_REQUIRES_EXPLICIT_SIMULATION_REVIEW");
            if(r.AxialSectionContract!="ExplicitSimulationUniformProfileOverListedSpan"||!ToolPoint.Finite(r.StartMm.Value)||!ToolPoint.Finite(r.EndMm.Value)||r.StartMm>=r.EndMm||
                !ToolPoint.Finite(r.BoundaryErrorMm.Value)||r.BoundaryErrorMm<0||r.BoundaryErrorMm>.1||!ToolPoint.Finite(r.AxialErrorMm.Value)||r.AxialErrorMm<0||r.AxialErrorMm>.1)
                throw new InvalidDataException("INVALID_CAD_SIMULATION_EXTENT_OR_ERROR");
            var transform=r.SourceToWorkFrame.Read();string frame=sheet.Binding.FrameId;
            Func<bool> current=()=>sheet.Current&&library.Current&&library.PlacementSources.Current&&review.RecordVerified&&
                (pair!=CollisionPairKind.WorkpieceHolder||assembly.HolderObstacles?.GeometrySupportedForCollision==true);
            CollisionMaterial2D material;
            if(pair==CollisionPairKind.WorkpiecePunch)
            {
                if(!geometry.Supported||!geometry.ObservedClosed||!geometry.Region.IsValidRegion||geometry.Region.MaximumJointGapMm>1e-10)
                    throw new InvalidDataException("OPEN_UNSUPPORTED_OR_INVALID_CAD_PUNCH");
                var placed=new ToolGeometryPlacement2D(geometry,transform,frame);
                var contour=new CollisionContour2D("CAD_PUNCH",placed.PlacedCurves,r.BoundaryErrorMm.Value+
                    PlacementCollisionEvaluator.PlacementUncertainty(geometry.Curves,transform),geometry.Region);
                material=new CollisionMaterial2D(frame,new[]{new CollisionComponent2D(contour)},payload,true,current,true);
            }
            else
            {
                if(assembly.HolderObstacles?.GeometrySupportedForCollision!=true)throw new InvalidDataException("CAD_HOLDER_MATERIAL_ROLES_UNREVIEWED");
                var placed=new HolderObstaclePlacement2D(assembly.HolderObstacles,transform,frame);
                if(!placed.HasConservativeGeometry||placed.RegionCombination!="UnionOfEachOuterMinusItsOwnHoles"||
                    placed.EnvelopeContract!="NominalMaterialUnionPlusEveryBoundaryUncertaintyBandV1")throw new InvalidDataException("INVALID_CAD_HOLDER_ENVELOPE");
                var contours=placed.Contours.ToDictionary(c=>c.Id,c=>new CollisionContour2D(c.Id,c.NominalBoundary,c.BoundaryBandRadiusMm+r.BoundaryErrorMm.Value));
                material=new CollisionMaterial2D(frame,placed.MaterialRegions.Select(m=>new CollisionComponent2D(contours[m.OuterId],m.HoleIds.Select(h=>contours[h]))),payload,true,current,true);
            }
            if(!material.Valid)throw new InvalidDataException(material.InvalidReason);
            var occupancy=new AxialToolOccupancy(material,new[]{new AxialInterval(r.StartMm.Value,r.EndMm.Value)},r.AxialErrorMm.Value,payload,true,current,null);
            return ProcessToolPose.ImportedSimulation(occupancy,law,payload,ToolEvidenceCanonical.Hash("FixedCadPairHardwareV1",id,pair.ToString(),assembly.MasterSha256,
                assembly.ManifestSha256,geometry.GeometrySha256,r.AxialSectionContract,
                pair==CollisionPairKind.WorkpieceHolder?assembly.HolderObstacles.DefinitionSha256:null));
        }
    }
}
