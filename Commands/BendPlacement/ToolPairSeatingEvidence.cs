using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlacement
{
    internal sealed class PairPointRecord { public double? X { get; set; } public double? Y { get; set; } }
    internal sealed class PairTransformRecord
    {
        public double? A { get; set; } public double? B { get; set; } public double? C { get; set; } public double? D { get; set; }
        public double? TxMm { get; set; } public double? TyMm { get; set; }
        public bool MirrorApproved { get; set; }
        internal PlacementTransform2D Read()
        {if(!A.HasValue||!B.HasValue||!C.HasValue||!D.HasValue||!TxMm.HasValue||!TyMm.HasValue)throw new InvalidDataException("MISSING_REVIEWED_SEATING_TRANSFORM_VALUES");return new PlacementTransform2D(A.Value,B.Value,C.Value,D.Value,TxMm.Value,TyMm.Value,MirrorApproved);}
    }
    internal sealed class ToolPairSeatingRecord
    {
        public string ToolId { get; set; }
        public string MasterSourceSha256 { get; set; }
        public string MasterManifestSha256 { get; set; }
        public string PunchGeometrySha256 { get; set; }
        public string HolderSelectionSha256 { get; set; }
        public string HolderMaterialDefinitionSha256 { get; set; }
        public string PunchSourceFrameId { get; set; }
        public string HolderSourceFrameId { get; set; }
        public PairTransformRecord HolderSourceToPunchSource { get; set; }
        public PairPointRecord TipPointMm { get; set; }
        public PairPointRecord PunchMountDatumMm { get; set; }
        public PairPointRecord HolderMountDatumMm { get; set; }
        public PairPointRecord WorkingDirection { get; set; }
        public PairPointRecord ApproachDirection { get; set; }
        public PairPointRecord ContactReferenceMm { get; set; }
        public string ContactReferenceFrameId { get; set; }
        public string DatumLocalFrameSha256 { get; set; }
        public bool PhysicalInspectionConfirmed { get; set; }
        public ToolReviewProof Review { get; set; }
    }
    internal sealed class ToolPairSeatingEvidence
    {
        private readonly PlacementLease lease=new PlacementLease();
        private readonly ToolDatumSet datums;
        private readonly HolderObstacleSet material;
        private readonly bool physical;
        public ToolReviewValidation Review { get; }
        public ToolReviewStatus Status => !lease.Current||datums?.Status==DatumEvidenceStatus.Stale||material?.Current==false?ToolReviewStatus.Stale:Review.Status;
        public bool CoordinateRecordReady => lease.Current&&datums?.Status==DatumEvidenceStatus.VerifiedRecord&&material?.GeometrySupportedForCollision==true&&Review.RecordVerified&&Reasons.Count==0;
        public bool SimulationOnly => Review.SimulationOnly;
        public bool PhysicalInspectionAccepted => CoordinateRecordReady&&physical&&Review.Status==ToolReviewStatus.VerifiedExternalRecord&&Review.MayAttestPhysicalInspection;
        public PlacementTransform2D HolderSourceToPunchSource { get; }
        public IReadOnlyList<string> Reasons { get; }
        public string PayloadSha256 { get; }
        public string ToolId { get; }
        public string MasterSourceSha256 { get; }
        public string MasterManifestSha256 { get; }
        public string PunchGeometrySha256 { get; }
        public string HolderSelectionSha256 { get; }
        public string HolderMaterialDefinitionSha256 { get; }
        public string PunchSourceFrameId { get; }
        public string HolderSourceFrameId { get; }
        public string DatumLocalFrameSha256 { get; }
        public ToolPoint? TipPointMm { get; }
        public ToolPoint? MountDatumMm { get; }
        public ToolPoint? HolderMountDatumMm { get; }
        public ToolPoint? WorkingDirection { get; }
        public ToolPoint? ApproachDirection { get; }
        public ToolPoint? ContactReferenceMm { get; }
        public string ContactReferenceFrameId { get; }
        private ToolPairSeatingEvidence(ToolDatumSet datums,ToolReviewValidation review,PlacementTransform2D transform,bool physical,string payload,IEnumerable<string> reasons,
            ToolPoint? tip=null,ToolPoint? mount=null,ToolPoint? direction=null,ToolPoint? approach=null,ToolPoint? contact=null,string frame=null,ToolPairSeatingRecord record=null,ToolPoint? holderMount=null,HolderObstacleSet material=null)
        {this.datums=datums;this.material=material;Review=review;HolderSourceToPunchSource=transform;this.physical=physical;PayloadSha256=payload;Reasons=PlacementLists.Reasons(reasons);
            TipPointMm=tip;MountDatumMm=mount;HolderMountDatumMm=holderMount;WorkingDirection=direction;ApproachDirection=approach;ContactReferenceMm=contact;ContactReferenceFrameId=frame;
            ToolId=record?.ToolId;MasterSourceSha256=record?.MasterSourceSha256;MasterManifestSha256=record?.MasterManifestSha256;PunchGeometrySha256=record?.PunchGeometrySha256;
            HolderSelectionSha256=record?.HolderSelectionSha256;HolderMaterialDefinitionSha256=record?.HolderMaterialDefinitionSha256;
            PunchSourceFrameId=record?.PunchSourceFrameId;HolderSourceFrameId=record?.HolderSourceFrameId;DatumLocalFrameSha256=record?.DatumLocalFrameSha256;}
        internal void Invalidate(){lease.Invalidate();Review.Invalidate();}
        internal static ToolPairSeatingEvidence Missing(ToolDatumSet datums=null)=>new ToolPairSeatingEvidence(datums,ToolReviewValidation.Missing("MISSING_PAIR_SEATING_EVIDENCE"),null,false,null,new[]{"MISSING_PAIR_SEATING_EVIDENCE"});
        internal static string LocalFrameHash(ToolDatumSet datums)
        {
            var f=datums?.LocalCoordinateFrame;return f==null?null:ToolEvidenceCanonical.Hash("PairDatumFrameV1",ToolEvidenceCanonical.Point(f.OriginMm),ToolEvidenceCanonical.Point(f.XAxis),ToolEvidenceCanonical.Point(f.YAxis));
        }
        private static ToolPoint Point(PairPointRecord p)=>p==null||!p.X.HasValue||!p.Y.HasValue?throw new InvalidDataException("MISSING_PAIR_DATUM_OR_DIRECTION"):new ToolPoint(p.X.Value,p.Y.Value);
        private static string PointHash(PairPointRecord p)=>p==null?null:ToolEvidenceCanonical.Point(Point(p));
        internal static string Payload(ToolPairSeatingRecord r)=>ToolEvidenceCanonical.Hash("ToolPairSeatingV1",r.ToolId,r.MasterSourceSha256,r.MasterManifestSha256,
            r.PunchGeometrySha256,r.HolderSelectionSha256,r.HolderMaterialDefinitionSha256,r.PunchSourceFrameId,r.HolderSourceFrameId,
            r.HolderSourceToPunchSource==null?null:ToolEvidenceCanonical.Transform(r.HolderSourceToPunchSource.Read()),
            PointHash(r.TipPointMm),PointHash(r.PunchMountDatumMm),PointHash(r.HolderMountDatumMm),PointHash(r.WorkingDirection),PointHash(r.ApproachDirection),PointHash(r.ContactReferenceMm),
            r.ContactReferenceFrameId,r.DatumLocalFrameSha256,r.PhysicalInspectionConfirmed?"PHYSICAL_INSPECTION_ATTESTED":"NO_PHYSICAL_ATTESTATION");
        internal static ToolPairSeatingEvidence Read(string id,string source,string manifest,GeometrySelection2D punch,GeometrySelection2D holder,HolderObstacleSet obstacle,ToolDatumSet datums,
            ToolPairSeatingRecord record,ToolReviewVerifier verifier,string root,CancellationToken token)
        {
            if(record==null)return Missing(datums);
            string payload=null;var reasons=new List<string>();var review=ToolReviewValidation.Missing();PlacementTransform2D transform=null;
            ToolPoint? tip=null,mount=null,direction=null,approach=null,contact=null,holderMount=null;
            try
            {
                payload=Payload(record);
                if(record.ToolId!=id||punch==null||holder==null||obstacle==null||record.MasterSourceSha256!=source||record.MasterManifestSha256!=manifest||
                    record.PunchGeometrySha256!=punch.GeometrySha256||record.HolderSelectionSha256!=holder.GeometrySha256||record.HolderMaterialDefinitionSha256!=obstacle.DefinitionSha256||
                    record.PunchSourceFrameId!=punch.FrameId||record.HolderSourceFrameId!=holder.FrameId)
                    throw new InvalidDataException("STALE_OR_WRONG_PAIR_BINDING");
                if(!obstacle.GeometrySupportedForCollision)throw new InvalidDataException("UNVERIFIED_HOLDER_MATERIAL");
                if(datums?.Status!=DatumEvidenceStatus.VerifiedRecord||record.DatumLocalFrameSha256!=LocalFrameHash(datums))throw new InvalidDataException("MISSING_OR_STALE_PAIR_DATUM_EVIDENCE");
                tip=Point(record.TipPointMm);mount=Point(record.PunchMountDatumMm);direction=Point(record.WorkingDirection);approach=Point(record.ApproachDirection);contact=Point(record.ContactReferenceMm);
                PlacementTransform2D.Unit(direction.Value);PlacementTransform2D.Unit(approach.Value);
                if(!datums.TipPointMm.HasValue||!datums.MountDatumMm.HasValue||!datums.WorkingDirection.HasValue||ToolPoint.Distance(tip.Value,datums.TipPointMm.Value)>1e-8||
                    ToolPoint.Distance(mount.Value,datums.MountDatumMm.Value)>1e-8||ToolPoint.Distance(direction.Value,datums.WorkingDirection.Value)>1e-10)
                    throw new InvalidDataException("PAIR_DATUM_DOES_NOT_MATCH_APPROVED_RECORD");
                if(record.ContactReferenceFrameId!=punch.FrameId)throw new InvalidDataException("UNVERIFIED_PAIR_CONTACT_REFERENCE_FRAME");
                transform=record.HolderSourceToPunchSource?.Read();if(transform==null)throw new InvalidDataException("MISSING_REVIEWED_SEATING_TRANSFORM");
                holderMount=Point(record.HolderMountDatumMm);
                if(ToolPoint.Distance(transform.Apply(holderMount.Value),mount.Value)>1e-5)throw new InvalidDataException("INCONSISTENT_MOUNT_DATUM_SEATING");
                review=verifier.Verify("ToolPairSeating",payload,record.Review,root,token);
                if(!review.RecordVerified)reasons.Add(review.Reason);
                // A production seating assertion cannot borrow a simulation
                // material review even when the source geometry is identical.
                if(!review.SimulationOnly&&obstacle.Review.SimulationOnly)reasons.Add("SIMULATION_MATERIAL_NOT_PRODUCTION_EVIDENCE");
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex) when(ex is InvalidDataException||ex is ArgumentException||ex is InvalidOperationException)
            {reasons.Add(ex is InvalidDataException?ex.Message:"INVALID_PAIR_SEATING_RECORD");transform=null;}
            return new ToolPairSeatingEvidence(datums,review,transform,record.PhysicalInspectionConfirmed,payload,reasons,tip,mount,direction,approach,contact,record.ContactReferenceFrameId,record,holderMount,obstacle);
        }
    }
}
