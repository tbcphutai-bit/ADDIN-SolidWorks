using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlanning;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlacement
{
    // A private exchange document, NOT a production certificate or authority
    // configuration. Nullable fields deliberately distinguish unknown from 0.
    internal sealed class ToolEvidenceReviewProposal
    {
        public int Version { get; set; }
        public string Purpose { get; set; }
        public string ToolId { get; set; }
        public string MasterSha256 { get; set; }
        public string ManifestSha256 { get; set; }
        public string BindingSha256 { get; set; }
        public string PunchInsert { get; set; }
        public string HolderInsert { get; set; }
        public string Revision { get; set; }
        public HolderMaterialDefinition Material { get; set; }
        public ToolHolderMappingRecord Mapping { get; set; }
        public ToolDatumRecord Datum { get; set; }
        public ToolPairSeatingRecord Seating { get; set; }
        public List<ToolAxialSetupRecord> Axial { get; set; }
        public List<CadReferenceSimulationRecord> Simulation { get; set; }
    }
    internal sealed class ToolEvidenceReviewDocument
    {
        public int Version { get; set; }
        public List<ToolEvidenceReviewProposal> Proposals { get; set; }
    }
    internal sealed class ToolEvidenceReviewAudit
    {
        public string Status => Reasons.Count==0 ? "REVIEWED_INPUT_SCOPE" : "UNKNOWN";
        public string ManufacturingStatus => "CHUA_XAC_MINH";
        public bool MaterialReady { get; internal set; }
        public bool CoordinatesReady { get; internal set; }
        public bool PhysicalPairVerified { get; internal set; }
        public IReadOnlyList<string> Reasons { get; internal set; }
        public IReadOnlyDictionary<string,string> Payloads { get; internal set; }
    }
    internal static class ToolEvidenceReviewWorkspace
    {
        private const string Purpose="UnsignedIndependentReviewProposalV1";
        private static JavaScriptSerializer Json()=>new JavaScriptSerializer {MaxJsonLength=4*1024*1024,RecursionLimit=64};
        internal static ToolEvidenceReviewProposal Create(HolderMaterialReviewDraft draft, SheetEnvelopeCapture sheet=null)
        {
            RequireCurrent(draft); var source=draft.Source;
            var proposal=new ToolEvidenceReviewProposal {Version=1,Purpose=Purpose,ToolId=draft.ToolId,MasterSha256=draft.MasterSha256,
                ManifestSha256=draft.ManifestSha256,BindingSha256=draft.BindingSha256,PunchInsert=source.CadPair.PunchInsert,HolderInsert=source.CadPair.HolderInsert,
                Material=new HolderMaterialDefinition {ToolId=draft.ToolId,BindingSha256=draft.BindingSha256,Contours=new List<HolderContourDefinition>(),Excluded=new List<HolderExcludedEntity>()},
                Mapping=new ToolHolderMappingRecord {ToolId=source.Id,BindingSha256=draft.BindingSha256,PunchRootInsert=source.CadPair.PunchInsert,PunchBlock=source.CadPair.PunchBlock,
                    HolderRootInsert=source.CadPair.HolderInsert,HolderBlock=source.CadPair.HolderBlock,HolderEntityKeys=source.Holder.EntityKeys.ToArray(),
                    LabelHandle=source.CadPair.LabelHandle,ExpectedLabel=source.CadPair.ExpectedLabel},
                Datum=new ToolDatumRecord {Id=source.Id,SourceSha256=source.MasterSha256,GeometrySha256=source.Punch.GeometrySha256,
                    TipPointMm=new ToolPointDatumRecord(),MountDatumMm=new ToolPointDatumRecord(),WorkingDirection=new ToolDirectionDatumRecord(),
                    LocalCoordinateFrame=new ToolFrameDatumRecord {OriginMm=new ToolVectorRecord(),XAxis=new ToolVectorRecord(),YAxis=new ToolVectorRecord()}},
                Seating=new ToolPairSeatingRecord {ToolId=source.Id,MasterSourceSha256=source.MasterSha256,MasterManifestSha256=source.ManifestSha256,
                    PunchGeometrySha256=source.Punch.GeometrySha256,HolderSelectionSha256=source.Holder.GeometrySha256,PunchSourceFrameId=source.Punch.FrameId,HolderSourceFrameId=source.Holder.FrameId,
                    TipPointMm=new PairPointRecord(),PunchMountDatumMm=new PairPointRecord(),HolderMountDatumMm=new PairPointRecord(),WorkingDirection=new PairPointRecord(),
                    ApproachDirection=new PairPointRecord(),ContactReferenceMm=new PairPointRecord(),HolderSourceToPunchSource=new PairTransformRecord(),PhysicalInspectionConfirmed=false},
                Axial=new List<ToolAxialSetupRecord>(),Simulation=new List<CadReferenceSimulationRecord>()};
            if(sheet!=null)
            {
                if(!sheet.Current)throw new InvalidDataException("STALE_NATIVE_ENVELOPE");
                foreach(var pair in new[]{CollisionPairKind.WorkpiecePunch,CollisionPairKind.WorkpieceHolder})
                {
                    proposal.Axial.Add(new ToolAxialSetupRecord {ToolId=source.Id,Pair=pair.ToString(),FeatureId=sheet.Binding.FeatureId,
                        ModelSnapshotId=sheet.Binding.ModelSnapshotId,ModelUpdateStamp=sheet.Binding.UpdateStamp,Configuration=sheet.Binding.Configuration,
                        EnvelopeGeometrySha256=sheet.Certificate.Method=="FastPrecheckNoVerifiedSetup"?null:sheet.GeometrySha256,
                        FrameId=sheet.Binding.FrameId,MasterSourceSha256=source.MasterSha256,
                        ProfileGeometrySha256=pair==CollisionPairKind.WorkpiecePunch?source.Punch.GeometrySha256:source.HolderObstacles?.DefinitionSha256,
                        ContactPointMm=new PairPointRecord(),ApproachDirection=new PairPointRecord(),Segments=new List<ToolAxialSegmentRecord>()});
                    proposal.Simulation.Add(new CadReferenceSimulationRecord {SourceSha256=source.MasterSha256,ManifestSha256=source.ManifestSha256,
                        SelectionGeometrySha256=pair==CollisionPairKind.WorkpiecePunch?source.Punch.GeometrySha256:source.Holder.GeometrySha256,
                        SourceToWorkFrame=new PairTransformRecord()});
                }
            }
            return proposal;
        }
        private static void RequireCurrent(HolderMaterialReviewDraft d)
        {if(d==null||!d.Current||!d.FixedPairMatched)throw new InvalidDataException("SOURCE_STALE_OR_UNMATCHED");}
        internal static string Serialize(ToolEvidenceReviewProposal p)
        {
            // Whitespace only; preserve exact numeric strings and quoted keys.
            var text=Json().Serialize(p);var result=new System.Text.StringBuilder();bool quoted=false,escape=false;int indent=0;
            foreach(char c in text)
            {
                if(quoted){result.Append(c);if(escape)escape=false;else if(c=='\\')escape=true;else if(c=='"')quoted=false;continue;}
                if(c=='"'){quoted=true;result.Append(c);}
                else if(c=='{'||c=='['){result.Append(c);indent++;result.AppendLine();result.Append(' ',indent*2);}
                else if(c=='}'||c==']'){indent--;result.AppendLine();result.Append(' ',indent*2);result.Append(c);}
                else if(c==','){result.Append(c);result.AppendLine();result.Append(' ',indent*2);}
                else if(c==':')result.Append(": ");else result.Append(c);
            }
            return result.ToString();
        }
        internal static IReadOnlyList<ToolEvidenceReviewProposal> ReadDocument(byte[] bytes)
        {
            string text=System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            var fields=Json().DeserializeObject(text) as Dictionary<string,object>;
            var document=fields?.ContainsKey("Proposals")==true?Json().Deserialize<ToolEvidenceReviewDocument>(text):
                new ToolEvidenceReviewDocument {Version=1,Proposals=new List<ToolEvidenceReviewProposal>{Json().Deserialize<ToolEvidenceReviewProposal>(text)}};
            if(document.Version!=1||document.Proposals==null||document.Proposals.Count==0||document.Proposals.Count>18||
                document.Proposals.Any(p=>p==null||p.Version!=1||p.Purpose!=Purpose||!CompanyToolIds.All.Contains(p.ToolId,StringComparer.Ordinal)||string.IsNullOrWhiteSpace(p.Revision))||
                document.Proposals.Select(p=>p.ToolId).Distinct(StringComparer.Ordinal).Count()!=document.Proposals.Count)
                throw new InvalidDataException("INVALID_REVIEW_WORKSPACE_DOCUMENT");
            return document.Proposals.AsReadOnly();
        }
        internal static ToolEvidenceReviewProposal Read(string text,HolderMaterialReviewDraft draft)
        {
            if(text==null||text.Length>4*1024*1024)throw new InvalidDataException("REVIEW_PROPOSAL_SIZE_LIMIT");
            var p=Json().Deserialize<ToolEvidenceReviewProposal>(text);CheckBinding(p,draft);return p;
        }
        private static void CheckBinding(ToolEvidenceReviewProposal p,HolderMaterialReviewDraft d)
        {
            RequireCurrent(d);
            if(p==null||p.Version!=1||p.Purpose!=Purpose||p.ToolId!=d.ToolId||p.MasterSha256!=d.MasterSha256||p.ManifestSha256!=d.ManifestSha256||
                p.BindingSha256!=d.BindingSha256||p.PunchInsert!=d.Source.CadPair.PunchInsert||p.HolderInsert!=d.Source.CadPair.HolderInsert)
                throw new InvalidDataException("STALE_OR_WRONG_REVIEW_PROPOSAL_BINDING");
            if((p.Axial?.Count??0)>2048||(p.Simulation?.Count??0)>2048)throw new InvalidDataException("REVIEW_PROPOSAL_RECORD_BUDGET");
        }
        // Export is explicitly unsigned. Reject, rather than strip, an imported
        // signature/physical attestation; the editor must not become a signer.
        internal static ToolEvidenceReviewProposal PrepareUnsigned(ToolEvidenceReviewProposal input,HolderMaterialReviewDraft draft)
        {
            var p=Read(Serialize(input),draft);
            ValidateRecordIdentities(p,draft);
            if(p.Material?.Review!=null||p.Mapping?.Review!=null||p.Seating?.Review!=null||p.Seating?.PhysicalInspectionConfirmed==true||
                p.Datum?.TipPointMm?.Evidence!=null||p.Datum?.MountDatumMm?.Evidence!=null||p.Datum?.WorkingDirection?.Evidence!=null||p.Datum?.LocalCoordinateFrame?.Evidence!=null||
                (p.Axial??new List<ToolAxialSetupRecord>()).Any(r=>r==null||r.Review!=null)||
                (p.Simulation??new List<CadReferenceSimulationRecord>()).Any(r=>r==null||r.Review!=null))
                throw new InvalidDataException("EDITOR_CANNOT_SIGN_OR_ATTEST_PHYSICAL_INSPECTION");
            if(p.Material!=null && ((p.Material.Contours?.Count??0)>0 || (p.Material.Excluded?.Count??0)>0))
                p.Material=HolderMaterialReviewDraftBuilder.PrepareUnsigned(draft,p.Material.Contours,p.Material.Excluded,p.Material.CombinationRule);
            ValidateRecordIdentities(p,draft);
            return p;
        }
        internal static void ValidateRecordIdentities(ToolEvidenceReviewProposal p,HolderMaterialReviewDraft draft)
        {
            var source=draft.Source;
            if(p.Material!=null&&(p.Material.ToolId!=draft.ToolId||p.Material.BindingSha256!=draft.BindingSha256))throw new InvalidDataException("WRONG_MATERIAL_PROPOSAL_BINDING");
            if(p.Datum!=null&&(p.Datum.Id!=source.Id||p.Datum.SourceSha256!=source.MasterSha256||p.Datum.GeometrySha256!=source.Punch.GeometrySha256))throw new InvalidDataException("WRONG_DATUM_PROPOSAL_BINDING");
            if(p.Mapping!=null&&(p.Mapping.ToolId!=source.Id||p.Mapping.BindingSha256!=draft.BindingSha256||p.Mapping.PunchRootInsert!=source.CadPair.PunchInsert||
                p.Mapping.ExpectedLabel!=source.CadPair.ExpectedLabel||p.Mapping.LabelHandle!=source.CadPair.LabelHandle||
                p.Mapping.PunchBlock!=source.CadPair.PunchBlock||p.Mapping.HolderRootInsert!=source.CadPair.HolderInsert||p.Mapping.HolderBlock!=source.CadPair.HolderBlock||
                p.Mapping.HolderEntityKeys==null||!source.Holder.EntityKeys.OrderBy(k=>k,StringComparer.Ordinal).SequenceEqual(p.Mapping.HolderEntityKeys.OrderBy(k=>k,StringComparer.Ordinal))))
                throw new InvalidDataException("WRONG_FIXED_PAIR_MAPPING_PROPOSAL");
            if(p.Seating!=null&&(p.Seating.ToolId!=source.Id||p.Seating.MasterSourceSha256!=source.MasterSha256||p.Seating.MasterManifestSha256!=source.ManifestSha256||
                p.Seating.PunchGeometrySha256!=source.Punch.GeometrySha256||p.Seating.HolderSelectionSha256!=source.Holder.GeometrySha256||
                p.Seating.PunchSourceFrameId!=source.Punch.FrameId||p.Seating.HolderSourceFrameId!=source.Holder.FrameId))throw new InvalidDataException("WRONG_SEATING_PROPOSAL_BINDING");
            if((p.Axial??new List<ToolAxialSetupRecord>()).Any(r=>r==null||r.ToolId!=source.Id||r.MasterSourceSha256!=source.MasterSha256))throw new InvalidDataException("WRONG_AXIAL_PROPOSAL_BINDING");
            if((p.Simulation??new List<CadReferenceSimulationRecord>()).Any(r=>r==null||r.SourceSha256!=source.MasterSha256||r.ManifestSha256!=source.ManifestSha256||
                r.SelectionGeometrySha256!=source.Punch.GeometrySha256&&r.SelectionGeometrySha256!=source.Holder.GeometrySha256))throw new InvalidDataException("WRONG_SIMULATION_PROPOSAL_BINDING");
        }
        internal static ToolEvidenceReviewAudit Audit(ToolEvidenceReviewProposal input,HolderMaterialReviewDraft draft,
            ToolReviewVerifier verifier,string evidenceRoot,CancellationToken token,ToolDatumSet reviewedDatums=null)
        {
            // A trusted verifier is injected by the existing source settings,
            // never supplied by the imported document or generated by this UI.
            var p=Read(Serialize(input),draft);ValidateRecordIdentities(p,draft);token.ThrowIfCancellationRequested();
            var s=draft.Source;var missing=new List<string>();var payloads=new Dictionary<string,string>();
            if(string.IsNullOrWhiteSpace(p.Revision))missing.Add("MISSING_REVIEW_REVISION");
            var material=HolderObstacleSet.Read(s.Id,s.Punch,s.Holder,s.ManifestSha256,p.Material,verifier,evidenceRoot,token);
            if(material.DefinitionSha256!=null)payloads["HolderMaterialRule"]=material.DefinitionSha256;
            if(!material.GeometrySupportedForCollision)missing.AddRange(material.Reasons);
            bool physicalMapping=false;
            if(p.Mapping==null)missing.Add("MISSING_MAPPING_REVIEW");
            else {payloads["ToolHolderMapping"]=p.Mapping.Payload();var review=verifier.Verify("ToolHolderMapping",p.Mapping.Payload(),p.Mapping.Review,evidenceRoot,token);
                if(!review.RecordVerified)missing.Add(review.Reason);physicalMapping=review.Status==ToolReviewStatus.VerifiedExternalRecord;}
            // Datum approval must be loaded/bound by ToolDatumEvidence, not
            // created from the editable coordinates in this proposal.
            var datums=reviewedDatums??s.MasterDatums;
            var seating=ToolPairSeatingEvidence.Read(s.Id,s.MasterSha256,s.ManifestSha256,s.Punch,s.Holder,material,datums,p.Seating,verifier,evidenceRoot,token);
            if(seating.PayloadSha256!=null)payloads["ToolPairSeating"]=seating.PayloadSha256;
            if(!seating.CoordinateRecordReady)missing.AddRange(seating.Reasons);
            if(!seating.PhysicalInspectionAccepted)missing.Add("PHYSICAL_PAIR_INSPECTION_UNVERIFIED");
            if(!s.Punch.ObservedClosed||!s.Punch.Region.IsValidRegion)missing.Add("OPEN_OR_INVALID_PUNCH_MATERIAL");
            // Numeric use is ONLY through ToolAxialSetupEvidence.Read and the
            // ProcessPackageImporter after exact native binding, not this audit.
            missing.Add("AXIAL_AND_PROCESS_REQUIRE_NATIVE_BOUND_IMPORT");
            return new ToolEvidenceReviewAudit {MaterialReady=material.GeometrySupportedForCollision,CoordinatesReady=seating.CoordinateRecordReady,
                PhysicalPairVerified=seating.PhysicalInspectionAccepted&&physicalMapping,Reasons=PlacementLists.Reasons(missing),
                Payloads=new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(payloads)};
        }
        internal static ProcessPackageRecord CreateUnsignedProcessTemplate(HolderMaterialReviewDraft draft,SheetEnvelopeCapture sheet)
        {
            RequireCurrent(draft);
            var model=sheet?.Binding.Model;
            if(model==null||!sheet.Current||!model.Lease.Current)throw new InvalidDataException("MISSING_CURRENT_NATIVE_PROCESS_BINDING");
            // Native identity and revision are observations. Unbent geometry,
            // states, setup and transitions are NOT derived from a final Part.
            return new ProcessPackageRecord {Version=1,Configuration=model.Configuration,ModelSnapshotId=model.SnapshotId,ModelUpdateStamp=model.UpdateStamp,
                NativeGeometrySha256=PartSequenceSpace.TargetGeometryHash(model),
                Bends=model.Bends.Select(b=>new ProcessNativeBendRecord {FeatureId=b.FeatureId,PersistentId=b.PersistentId,NativeFrameId=b.Frame?.FrameId}).ToList(),
                Registration=new ProcessRegistrationRecord(),Artifacts=new List<ProcessArtifactRecord>(),Geometry=new List<ProcessPrismRecord>(),
                States=new List<ProcessStateRecord>(),Transitions=new List<ProcessTransitionRecord>(),DeclaredAlternativesComplete=null,GraphReview=null};
        }
        internal static string SerializeProcessTemplate(ProcessPackageRecord package)=>Json().Serialize(package);
    }
}
