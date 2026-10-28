using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendTools;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendCollision;

namespace ADDIN.Commands.BendPlacement
{
    internal sealed class BendPlacementConfiguration
    {
        public int Version { get; set; }
        public string MasterPath { get; set; }
        public string MasterManifestPath { get; set; }
        public string MasterDatumEvidencePath { get; set; }
        public string LimitWorkbookPath { get; set; }
        public string LimitManifestPath { get; set; }
        public string HolderEvidencePath { get; set; }
        public string PairSeatingEvidencePath { get; set; }
        public string ReviewAuthoritiesPath { get; set; }
        public string ReviewWorkspacePath { get; set; }
        public string ExcelMeasurementsPath { get; set; }
    }
    internal sealed class BendExcelMeasurementDocument
    {
        public int Version {get;set;}
        public List<BendExcelMeasurementRecord> Measurements {get;set;}
    }
    internal sealed class BendExcelMeasurementRecord
    {
        public string SourceKey {get;set;}
        public string ModelSnapshotId {get;set;}
        public int UpdateStamp {get;set;}
        public string Configuration {get;set;}
        public int FeatureId {get;set;}
        public string PersistentId {get;set;}
        public string ToolId {get;set;}
        public string FrameId {get;set;}
        public string EnvelopeGeometrySha256 {get;set;}
        public string MasterSourceSha256 {get;set;}
        public string WorkbookSha256 {get;set;}
        public string PunchSetupPayloadSha256 {get;set;}
        public string HolderSetupPayloadSha256 {get;set;}
        public string MachineSystem {get;set;}
        public string MaterialCode {get;set;}
        public string SectionKind {get;set;}
        public string PoseId {get;set;}
        public double? ThicknessMm {get;set;}
        public double? AngleDeg {get;set;}
        public double? Wmm {get;set;}
        public double? Lmm {get;set;}
        public double? BendLengthMm {get;set;}
        public double? MeasurementToleranceMm {get;set;}
        public double? AngleToleranceDeg {get;set;}
        public ToolReviewProof Review {get;set;}
    }
    internal sealed class BendExcelMeasurement
    {
        private readonly BendExcelMeasurementRecord record;
        internal ToolReviewValidation Review {get;}
        internal string WorkbookSha256 => record.WorkbookSha256;
        internal string PayloadSha256 {get;}
        internal BendExcelMeasurement(BendExcelMeasurementRecord record,ToolReviewVerifier verifier,string root,CancellationToken token)
        {
            // The deserialized document is private; keep only this frozen clone.
            var json=new System.Web.Script.Serialization.JavaScriptSerializer();
            this.record=json.Deserialize<BendExcelMeasurementRecord>(json.Serialize(record));
            PayloadSha256=Payload(this.record);
            Review=verifier.Verify("BendExcelNativeMeasurements",PayloadSha256,this.record.Review,root,token);
        }
        internal static string Payload(BendExcelMeasurementRecord r)
        {
            Func<double?,string> n=v=>v.HasValue&&ToolPoint.Finite(v.Value)?ToolEvidenceCanonical.Number(v.Value):"MISSING";
            return ToolEvidenceCanonical.Hash("BendExcelMeasurementsV1",r.SourceKey,r.ModelSnapshotId,r.UpdateStamp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                r.Configuration,r.FeatureId.ToString(System.Globalization.CultureInfo.InvariantCulture),r.PersistentId,r.ToolId,r.FrameId,
                r.EnvelopeGeometrySha256,r.MasterSourceSha256,r.WorkbookSha256,r.PunchSetupPayloadSha256,r.HolderSetupPayloadSha256,
                r.MachineSystem,r.MaterialCode,r.SectionKind,r.PoseId,n(r.ThicknessMm),n(r.AngleDeg),n(r.Wmm),n(r.Lmm),n(r.BendLengthMm),n(r.MeasurementToleranceMm),n(r.AngleToleranceDeg));
        }
        internal bool Identifies(string source,string configuration,int feature,string tool) => record.SourceKey==source&&record.Configuration==configuration&&record.FeatureId==feature&&record.ToolId==tool;
        private static BendExcelObservation Reject(out string reason,string value) {reason=value;return null;}
        internal BendExcelObservation Bind(AssemblySourceInput source,IReadOnlyList<SheetEnvelopeCapture> envelopes,ToolProfileLibrarySnapshot library,out string reason)
        {
            var r=record;var model=source.Model;
            if(Review.Status!=ToolReviewStatus.VerifiedExternalRecord)return Reject(out reason,"EXCEL_MEASUREMENT_REVIEW:"+Review.Status+"/"+Review.Reason);
            if(model?.Lease.Current!=true||library.Current!=true||r.ModelSnapshotId!=model.SnapshotId||r.UpdateStamp!=model.UpdateStamp||r.Configuration!=model.Configuration)
                return Reject(out reason,"STALE_EXCEL_NATIVE_MODEL_OR_CONFIGURATION");
            if(
                string.IsNullOrWhiteSpace(r.PersistentId)||string.IsNullOrWhiteSpace(r.PoseId)||
                !ToolDatumEvidence.IsSha256(r.EnvelopeGeometrySha256)||!ToolDatumEvidence.IsSha256(r.MasterSourceSha256)||
                !ToolDatumEvidence.IsSha256(r.WorkbookSha256)||!r.MeasurementToleranceMm.HasValue||!r.AngleToleranceDeg.HasValue)
                return Reject(out reason,"MISSING_EXCEL_SOURCE_HASH_POSE_PERSISTENT_ID_OR_UNCERTAINTY");
            var bends=model.Bends.Where(b=>b.FeatureId==r.FeatureId&&b.PersistentId==r.PersistentId).ToArray();
            var metadata=source.BendMetadata.Where(m=>m.FeatureId==r.FeatureId&&m.PersistentId==r.PersistentId).ToArray();
            var envs=envelopes.Where(e=>e.Current&&ReferenceEquals(e.Binding.Model,model)&&e.Binding.FeatureId==r.FeatureId&&
                e.Binding.FrameId==r.FrameId&&e.GeometrySha256==r.EnvelopeGeometrySha256).ToArray();
            var pairs=library.PlacementSources.Tools.Where(t=>t.Id==r.ToolId&&t.MasterSha256==r.MasterSourceSha256).ToArray();
            if(bends.Length!=1||metadata.Length!=1||envs.Length!=1||pairs.Length!=1)
                return Reject(out reason,"MISSING_OR_AMBIGUOUS_EXCEL_FEATURE_FRAME_ENVELOPE_OR_TOOL_BINDING");
            if(!pairs[0].ClampVerified)return Reject(out reason,"UNVERIFIED_PHYSICAL_PAIR_FOR_EXCEL_POSE");
            // The current kernel-validated setup is the source of registration,
            // not a drawing origin, bounding box or an operator's unchecked pose.
            foreach(var kind in new[]{CollisionPairKind.WorkpiecePunch,CollisionPairKind.WorkpieceHolder})
            {
                string required=kind==CollisionPairKind.WorkpiecePunch?r.PunchSetupPayloadSha256:r.HolderSetupPayloadSha256;
                var setups=source.Setups.Where(s=>s.ToolId==r.ToolId&&s.FeatureId==r.FeatureId&&s.Pair==kind).ToArray();
                if(!ToolDatumEvidence.IsSha256(required)||setups.Length!=1||setups[0].PayloadSha256!=required||
                    !setups[0].AppliesTo(envs[0],library)||!setups[0].Occupancy.Verified||setups[0].Occupancy.SimulationOnly||
                    setups[0].Review.Status!=ToolReviewStatus.VerifiedExternalRecord)
                    return Reject(out reason,"MISSING_OR_UNREVIEWED_"+kind+"_SETUP_FOR_EXCEL");
            }
            if(!string.Equals(metadata[0].MaterialCode,r.MaterialCode,StringComparison.OrdinalIgnoreCase))return Reject(out reason,"EXCEL_NATIVE_MATERIAL_MISMATCH");
            double error=r.MeasurementToleranceMm.Value,angleError=r.AngleToleranceDeg.Value;
            if(!ToolPoint.Finite(error)||error<0||error>.1||!ToolPoint.Finite(angleError)||angleError<0||angleError>1||
                !r.ThicknessMm.HasValue||!r.AngleDeg.HasValue||!r.BendLengthMm.HasValue||!bends[0].ThicknessMm.HasValue||!metadata[0].AngleDeg.HasValue||
                !ToolPoint.Finite(bends[0].ThicknessMm.Value)||!ToolPoint.Finite(metadata[0].AngleDeg.Value)||
                !ToolPoint.Finite(bends[0].BendMinMm)||!ToolPoint.Finite(bends[0].BendMaxMm)||
                Math.Abs(r.ThicknessMm.Value-bends[0].ThicknessMm.Value)>error||
                Math.Abs(r.AngleDeg.Value-metadata[0].AngleDeg.Value)>angleError||
                Math.Abs(r.BendLengthMm.Value-(bends[0].BendMaxMm-bends[0].BendMinMm))>error)
                return Reject(out reason,"EXCEL_NATIVE_DIMENSION_OR_UNCERTAINTY_MISMATCH");
            reason=null;
            return new BendExcelObservation {ToolId=r.ToolId,MachineSystem=r.MachineSystem,MaterialCode=r.MaterialCode,SectionKind=r.SectionKind,PoseId=r.PoseId,
                ThicknessMm=r.ThicknessMm,AngleDeg=r.AngleDeg,Wmm=r.Wmm,Lmm=r.Lmm,BendLengthMm=r.BendLengthMm,
                MeasurementToleranceMm=error,AngleToleranceDeg=angleError,SourceBinding=PayloadSha256,WorkbookSha256=r.WorkbookSha256,Review=Review,Current=true};
        }
    }
    internal static class BendExcelApplicabilityAdapter
    {
        internal static BendExcelCaseResult Evaluate(AssemblySourceInput source,IReadOnlyList<SheetEnvelopeCapture> envelopes,ToolProfileLibrarySnapshot library,int feature,string tool)
        {
            Func<string,BendExcelCaseResult> unknown=reason=>new BendExcelCaseResult(BendExcelCaseApplicability.MATCH_UNRESOLVED,null,reason);
            if(source?.Current!=true||library?.Current!=true)return unknown("STALE_EXCEL_MODEL_OR_SOURCE");
            var placement=library.PlacementSources;
            var matches=placement.ExcelMeasurements.Where(m=>m.Identifies(source.SourceKey,source.Model?.Configuration,feature,tool)).ToArray();
            if(matches.Length!=1)return unknown(matches.Length==0?"MISSING_REVIEWED_W_L_MATERIAL_MACHINE_POSE_MEASUREMENTS":"AMBIGUOUS_EXCEL_MEASUREMENT_BINDING");
            string bindingReason;var observation=matches[0].Bind(source,envelopes,library,out bindingReason);
            if(observation?.Complete!=true)return unknown(bindingReason??"MISSING_OR_INVALID_EXCEL_W_L_MACHINE_SHAPE_OR_MEASUREMENTS");
            return MatchReviewedRules(placement,observation);
        }
        internal static BendExcelCaseResult MatchReviewedRules(PlacementSourceSnapshot placement,BendExcelObservation observation)
        {
            Func<string,BendExcelCaseResult> unknown=reason=>new BendExcelCaseResult(BendExcelCaseApplicability.MATCH_UNRESOLVED,null,reason);
            if(placement?.Current!=true||observation?.Complete!=true)return unknown("STALE_OR_INCOMPLETE_EXCEL_INPUT");
            var rules=placement.LimitRules.ToArray();
            if(rules.Length==0||rules.Any(r=>r.WorkbookSha256!=observation.WorkbookSha256))return unknown("MISSING_OR_MISMATCHED_EXCEL_WORKBOOK");
            // Only a separately signed informational disposition may remove a
            // reference illustration from the production-applicability set.
            var results=rules.Where(r=>!r.ReviewedInformationalReference).Select(r=>BendExcelCaseMatcher.Evaluate(r,observation)).ToArray();
            if(results.Any(r=>r.Applicability==BendExcelCaseApplicability.MATCH_UNRESOLVED))
                return unknown(string.Join("; ",results.Where(r=>r.Applicability==BendExcelCaseApplicability.MATCH_UNRESOLVED).Select(r=>r.Evidence).Distinct()));
            var applicable=results.Where(r=>r.Applicability==BendExcelCaseApplicability.MATCH_CONFIRMED).ToArray();
            if(applicable.Length>0)
            {
                var applicableRules=rules.Where(r=>!r.ReviewedInformationalReference).Zip(results,(rule,result)=>new{rule,result})
                    .Where(v=>v.result.Applicability==BendExcelCaseApplicability.MATCH_CONFIRMED).Select(v=>v.rule).ToArray();
                if(applicableRules.Select(r=>r.ValueMm).Distinct().Count()!=1)return unknown("CONFLICTING_APPROVED_EXCEL_LIMIT_VALUES");
                if(applicable.Select(r=>r.ExceedsCertifiedLength).Distinct().Count()!=1)return unknown("CONFLICTING_APPROVED_EXCEL_CASES");
                return new BendExcelCaseResult(BendExcelCaseApplicability.MATCH_CONFIRMED,applicable[0].ExceedsCertifiedLength,string.Join("; ",applicable.Select(r=>r.Evidence)));
            }
            if(placement.ExcelCaseSetReview.Status!=ToolReviewStatus.VerifiedExternalRecord)return unknown("EXCEL_CASE_SET_COMPLETENESS_NOT_REVIEWED");
            return new BendExcelCaseResult(BendExcelCaseApplicability.NOT_APPLICABLE_CONFIRMED,null,
                "ALL_REVIEWED_EXCEL_CASES_NOT_APPLICABLE:"+placement.ExcelCaseSetReview.PayloadSha256,placement.ExcelCaseSetReview);
        }
        internal static string Key(string tool,int feature) => tool+"/"+feature.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    internal static class BendPlacementSources
    {
        // Called only by the existing managed library worker. This adapter has
        // no model/control/COM dependency and writes no settings or CAD.
        internal static PlacementSourceSnapshot Read(string configurationPath,CancellationToken token,string relativeRoot=null)
        {
            if(string.IsNullOrWhiteSpace(configurationPath))return PlacementSourceSnapshot.Missing("PLACEMENT_SOURCE_NOT_CONFIGURED");
            try
            {
                configurationPath=PlacementSourceIo.Resolve(relativeRoot??Directory.GetCurrentDirectory(),configurationPath);
                byte[] bytes=PlacementSourceIo.Read(configurationPath,65536,token);var c=PlacementSourceIo.Json<BendPlacementConfiguration>(bytes);
                if(c==null||c.Version!=1)throw new InvalidDataException("Invalid placement source version.");
                string root=Path.GetDirectoryName(Path.GetFullPath(configurationPath));
                var master=BendMasterAssemblyReader.Read(PlacementSourceIo.Resolve(root,c.MasterPath),PlacementSourceIo.Resolve(root,c.MasterManifestPath),PlacementSourceIo.Resolve(root,c.MasterDatumEvidencePath),token,
                    PlacementSourceIo.Resolve(root,c.HolderEvidencePath),PlacementSourceIo.Resolve(root,c.PairSeatingEvidencePath),PlacementSourceIo.Resolve(root,c.ReviewAuthoritiesPath),PlacementSourceIo.Resolve(root,c.ReviewWorkspacePath));
                var limits=BendLimitWorkbookReader.Read(PlacementSourceIo.Resolve(root,c.LimitWorkbookPath),
                    PlacementSourceIo.Resolve(root,c.LimitManifestPath),token,PlacementSourceIo.Resolve(root,c.ReviewAuthoritiesPath));
                var measurements=new List<BendExcelMeasurement>();string measurementSha="MISSING";
                var measurementReasons=new List<string>();
                if(!string.IsNullOrWhiteSpace(c.ExcelMeasurementsPath))
                {
                    try
                    {
                        string path=PlacementSourceIo.Resolve(root,c.ExcelMeasurementsPath);
                        byte[] measured=PlacementSourceIo.Read(path,2*1024*1024,token);measurementSha=PlacementSourceIo.Hash(measured);
                        var doc=PlacementSourceIo.Json<BendExcelMeasurementDocument>(measured);
                        if(doc?.Version!=1||doc.Measurements==null||doc.Measurements.Count>8192||doc.Measurements.Any(m=>m==null))
                            throw new InvalidDataException("INVALID_EXCEL_MEASUREMENT_DOCUMENT");
                        var verifier=ToolReviewVerifier.Load(PlacementSourceIo.Resolve(root,c.ReviewAuthoritiesPath),token);
                        measurements.AddRange(doc.Measurements.Select(m=>new BendExcelMeasurement(m,verifier,Path.GetDirectoryName(path),token)));
                        measurementSha=PlacementSourceIo.Hash(measurementSha+verifier.Fingerprint+verifier.ObservationFingerprint);
                    }
                    catch(OperationCanceledException){throw;}
                    catch(Exception ex){measurements.Clear();measurementReasons.Add("INVALID_EXCEL_MEASUREMENTS:"+ex.GetType().Name);
                        BendCheckRunDiagnostics.WriteChanged("BendExcelMeasurements.Read.error",new {c.ExcelMeasurementsPath},ex,token);}
                }
                else measurementReasons.Add("MISSING_REVIEWED_EXCEL_MEASUREMENTS");
                return new PlacementSourceSnapshot(PlacementSourceIo.Hash(PlacementSourceIo.Hash(bytes)+master.Fingerprint+limits.Fingerprint+measurementSha),
                    master.Tools,limits.Rules,master.Reasons.Concat(limits.Reasons).Concat(measurementReasons),limits.CaseSetReview,measurements);
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex){BendCheckRunDiagnostics.WriteChanged("PlacementSources.Read.error",new {configurationPath},ex,token);
                return PlacementSourceSnapshot.Missing("INVALID_PLACEMENT_CONFIGURATION:"+ex.GetType().Name);}
        }
    }
}
