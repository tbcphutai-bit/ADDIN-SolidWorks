using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendCollision
{
    // Optional external production input. It never infers contact or approach
    // from the final Part, drawing position, a bounding box or a default origin.
    internal sealed class CollisionWorkPoseRecord
    {
        public string ToolId { get; set; }
        public int FeatureId { get; set; }
        public string ModelSnapshotId { get; set; }
        public int ModelUpdateStamp { get; set; }
        public string Configuration { get; set; }
        public string SheetGeometrySha256 { get; set; }
        public string WorkFrameId { get; set; }
        public string MasterSourceSha256 { get; set; }
        public string PairSeatingPayloadSha256 { get; set; }
        public string StateId { get; set; }
        public PairPointRecord ContactPointMm { get; set; }
        public PairPointRecord ApproachDirection { get; set; }
        public bool MirrorApproved { get; set; }
        public ToolReviewProof Review { get; set; }
    }
    internal sealed class CollisionWorkPoseEvidence
    {
        private readonly Func<bool> current;
        private readonly ToolAssemblyProfile assembly;
        private readonly PlacementModelCapture boundModel;
        private readonly ToolProfileLibrarySnapshot boundLibrary;
        public string ToolId { get; }
        public int FeatureId { get; }
        public ToolReviewValidation Review { get; }
        public string PayloadSha256 { get; }
        public IReadOnlyList<string> Reasons { get; }
        public PlacementTransform2D PunchToWork { get; }
        public PlacementTransform2D HolderToWork { get; }
        public bool Current => current() && Review.Status!=ToolReviewStatus.Stale;
        public bool Approved => Current && Reasons.Count==0 && Review.Status==ToolReviewStatus.VerifiedExternalRecord && assembly.PhysicalPairVerified;
        private CollisionWorkPoseEvidence(string id,int feature,string payload,ToolReviewValidation review,IEnumerable<string> reasons,
            PlacementTransform2D punch,PlacementTransform2D holder,ToolAssemblyProfile assembly,Func<bool> current,PlacementModelCapture model,ToolProfileLibrarySnapshot library)
        { ToolId=id;FeatureId=feature;PayloadSha256=payload;Review=review;Reasons=PlacementLists.Reasons(reasons);
            PunchToWork=punch;HolderToWork=holder;this.assembly=assembly;this.current=current;boundModel=model;boundLibrary=library; }
        internal bool AppliesTo(PlacementModelCapture model,ToolProfileLibrarySnapshot library,SheetSection2D section,ToolAssemblyProfile source) =>
            Approved && ReferenceEquals(model,boundModel) && ReferenceEquals(library,boundLibrary) && ReferenceEquals(source,assembly) && FeatureId==section.FeatureId;
        internal static string SheetHash(SheetSection2D s) => ToolEvidenceCanonical.Hash("CollisionSheetV1",s?.Frame?.FrameId,
            s?.FeatureId.ToString(System.Globalization.CultureInfo.InvariantCulture),s?.Coverage.ToString(),
            s==null?null:ToolEvidenceCanonical.Hash(s.Boundary.Select(c=>ToolEvidenceCanonical.Hash(c.Kind,ToolEvidenceCanonical.Point(c.Start),
                ToolEvidenceCanonical.Point(c.End),ToolEvidenceCanonical.Point(c.Center),ToolEvidenceCanonical.Number(c.RadiusMm),ToolEvidenceCanonical.Number(c.SweepRadians))).ToArray()));
        private static ToolPoint Point(PairPointRecord p) => p==null || !p.X.HasValue || !p.Y.HasValue ?
            throw new InvalidDataException("MISSING_COLLISION_CONTACT_OR_APPROACH") : new ToolPoint(p.X.Value,p.Y.Value);
        internal static string Payload(CollisionWorkPoseRecord r) => ToolEvidenceCanonical.Hash("CollisionWorkPoseV1",r.ToolId,
            r.FeatureId.ToString(System.Globalization.CultureInfo.InvariantCulture),r.ModelSnapshotId,r.ModelUpdateStamp.ToString(System.Globalization.CultureInfo.InvariantCulture),
            r.Configuration,r.SheetGeometrySha256,r.WorkFrameId,r.MasterSourceSha256,r.PairSeatingPayloadSha256,r.StateId,
            ToolEvidenceCanonical.Point(Point(r.ContactPointMm)),ToolEvidenceCanonical.Point(Point(r.ApproachDirection)),r.MirrorApproved?"MIRROR_APPROVED":"NO_MIRROR");
        internal static CollisionWorkPoseEvidence Read(CollisionWorkPoseRecord record,PlacementModelCapture model,ToolProfileLibrarySnapshot library,
            ToolReviewVerifier verifier,string evidenceRoot,CancellationToken token)
        {
            if(record==null)throw new ArgumentNullException(nameof(record));
            var reasons=new List<string>();string payload=null;var review=ToolReviewValidation.Missing("MISSING_COLLISION_WORK_POSE_REVIEW");
            ToolAssemblyProfile assembly=null;PlacementTransform2D punch=null,holder=null;
            try
            {
                payload=Payload(record);var section=model.Sections.SingleOrDefault(s=>s.FeatureId==record.FeatureId);
                assembly=library.PlacementSources.Tools.SingleOrDefault(t=>t.Id==record.ToolId);
                if(model.SimulationOnly || assembly?.PhysicalPairVerified!=true)throw new InvalidDataException("UNVERIFIED_PRODUCTION_PAIR_OR_MODEL");
                if(section==null || section.Coverage!=SheetCoverage.CompleteUniformExtrusion || record.ModelSnapshotId!=model.SnapshotId || record.ModelUpdateStamp!=model.UpdateStamp ||
                    record.Configuration!=model.Configuration || record.SheetGeometrySha256!=SheetHash(section) || record.WorkFrameId!=section.Frame?.FrameId ||
                    record.MasterSourceSha256!=assembly.MasterSha256 || record.PairSeatingPayloadSha256!=assembly.SeatingEvidence.PayloadSha256)
                    throw new InvalidDataException("STALE_OR_WRONG_COLLISION_WORK_POSE");
                if(record.StateId!=BendStateCoverage.FinalModelObserved.ToString())throw new InvalidDataException("UNSUPPORTED_PRODUCTION_STATE_BINDING");
                var contact=Point(record.ContactPointMm);var direction=Point(record.ApproachDirection);PlacementTransform2D.Unit(direction);
                review=verifier.Verify("CollisionWorkPose",payload,record.Review,evidenceRoot,token);
                if(review.Status!=ToolReviewStatus.VerifiedExternalRecord)throw new InvalidDataException("UNVERIFIED_PRODUCTION_CONTACT_AND_APPROACH");
                punch=PlacementTransform2D.Align(assembly.SeatingEvidence.TipPointMm.Value,assembly.SeatingEvidence.WorkingDirection.Value,contact,direction,record.MirrorApproved);
                holder=punch.Then(assembly.SeatingEvidence.HolderSourceToPunchSource);
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex) when(ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException)
            { reasons.Add(ex is InvalidDataException?ex.Message:"INVALID_COLLISION_WORK_POSE"); }
            return new CollisionWorkPoseEvidence(record.ToolId,record.FeatureId,payload,review,reasons,punch,holder,assembly,
                ()=>model.Lease.Current && library.Current && library.PlacementSources.Current,model,library);
        }
    }
}
