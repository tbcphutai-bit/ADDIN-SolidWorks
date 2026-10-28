using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendTools;
using ADDIN.Commands.BendCollision;

namespace ADDIN.Commands.BendPlacement
{
    internal static class PlacementLists
    {
        internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> source) => Array.AsReadOnly((source??Enumerable.Empty<T>()).ToArray());
        internal static IReadOnlyList<string> Reasons(IEnumerable<string> source) => Copy(source.Distinct().OrderBy(x=>x,StringComparer.Ordinal));
    }
    internal sealed class PlacementLease
    {
        private int current=1;
        public bool Current => Volatile.Read(ref current)==1;
        internal void Invalidate() { Interlocked.Exchange(ref current,0); }
    }
    internal enum SheetCoverage { Unsupported2DEnvelope, CompleteUniformExtrusion, SimulationOnly }
    internal enum BendStateCoverage { UnknownBefore, UnknownDuring, UnknownAfter, FinalModelObserved, SimulationOnly }
    internal enum PlacementReadiness { PlacementDraft, SimulationOnly, ReadyForCollisionCheck }

    internal sealed class GeometrySelection2D
    {
        public string SourceSha256 { get; }
        public string FrameId { get; }
        public string GeometrySha256 { get; }
        public IReadOnlyList<string> EntityKeys { get; }
        public IReadOnlyList<string> UnsupportedTypes { get; }
        public IReadOnlyList<ToolCurve> Curves { get; }
        public IReadOnlyList<ToolTransform> InstanceTransforms { get; }
        public bool ObservedClosed { get; }
        public ToolRegionValidation Region { get; }
        public bool ReferenceOnly { get; }
        internal IReadOnlyList<ToolDxfEntity> SourceEntities { get; }
        public bool Supported => EntityKeys.Count>0&&UnsupportedTypes.Count==0&&Curves.Count>0;
        internal GeometrySelection2D(string sha,string geometrySha,IEnumerable<ToolDxfEntity> entities,bool reference,CancellationToken cancellation)
        {
            var e=entities.ToArray();SourceEntities=PlacementLists.Copy(e);SourceSha256=sha;
            GeometrySha256=geometrySha??HolderDigest(sha,e,cancellation);
            FrameId="MasterWcsMm:"+sha;ReferenceOnly=reference;
            EntityKeys=PlacementLists.Copy(e.Select(x=>x.InstancePath+"|"+x.SourceRecord+"|"+x.Handle));
            Curves=PlacementLists.Copy(e.SelectMany(x=>x.Curves));InstanceTransforms=PlacementLists.Copy(e.Select(x=>x.Transform));
            UnsupportedTypes=PlacementLists.Copy(e.Where(x=>x.UnsupportedReason!=null||x.Curves.Count==0).Select(x=>x.Type).Distinct());
            ObservedClosed=ToolProfileTopology.Closed(e);Region=ToolRegionValidator.Validate(e,cancellation:cancellation);
        }
        private static string HolderDigest(string sha,ToolDxfEntity[] entities,CancellationToken token)
        {
            var splineBindings=new List<string>();
            foreach(var e in entities)
            {
                token.ThrowIfCancellationRequested();var s=e.Spline;
                splineBindings.Add(ToolEvidenceCanonical.Hash(e.SourceLineType,s==null?null:ToolEvidenceCanonical.Hash(s.Degree.ToString(System.Globalization.CultureInfo.InvariantCulture),s.Flags.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ToolEvidenceCanonical.Hash(s.SourceControlPoints.Select(ToolEvidenceCanonical.Point).ToArray()),ToolEvidenceCanonical.Hash(s.ControlPointsMm.Select(ToolEvidenceCanonical.Point).ToArray()),
                    ToolEvidenceCanonical.Hash(s.Knots.Select(ToolEvidenceCanonical.Number).ToArray()),ToolEvidenceCanonical.Hash(s.Weights.Select(ToolEvidenceCanonical.Number).ToArray()))));
            }
            return ToolEvidenceCanonical.Hash("HolderSelectionV1",sha,ToolDatumEvidence.GeometryHash(new CompanyToolProfile {Id="HolderSelection",Parts=new[]{new ToolGeometryPart {Source=new ToolProfileProvenance {Sha256=sha},Role=ToolGeometryRole.CompanyPunchProfile,Entities=entities}}},token),ToolEvidenceCanonical.Hash(splineBindings.ToArray()));
        }
    }
    internal sealed class ToolAssemblyProfile
    {
        public string Id { get; }
        public string MasterSha256 { get; }
        public string ManifestSha256 { get; }
        public GeometrySelection2D Punch { get; }
        public GeometrySelection2D Holder { get; }
        public ToolDatumSet MasterDatums { get; }
        public bool IdentityMatched { get; }
        public bool MappingIdentified => IdentityMatched&&Punch!=null&&Holder!=null;
        public HolderObstacleSet HolderObstacles { get; }
        public ToolPairSeatingEvidence SeatingEvidence { get; }
        public ToolReviewValidation MappingReview { get; }
        public CadPairBinding CadPair { get; }
        public ToolProfileProvenance Source { get; }
        public bool GeometryCheckReady => MappingIdentified&&MappingReview.RecordVerified&&HolderObstacles?.GeometrySupportedForCollision==true&&
            Punch.Supported&&Punch.ObservedClosed&&Punch.Region.IsValidRegion&&Punch.Region.MaximumJointGapMm<=1e-10&&SeatingEvidence.CoordinateRecordReady;
        public bool ClampVerified => GeometryCheckReady&&SeatingEvidence.PhysicalInspectionAccepted&&MappingReview.Status==ToolReviewStatus.VerifiedExternalRecord;
        public bool PhysicalPairVerified => ClampVerified;
        public IReadOnlyList<string> Reasons { get; }
        internal ToolAssemblyProfile(string id,string sha,string manifest,GeometrySelection2D punch,GeometrySelection2D holder,ToolDatumSet datums,bool mapped,IEnumerable<string> reasons,
            HolderObstacleSet obstacle=null,ToolPairSeatingEvidence seating=null,ToolReviewValidation mappingReview=null,CadPairBinding cadPair=null,ToolProfileProvenance source=null)
        {Id=id;MasterSha256=sha;ManifestSha256=manifest;Punch=punch;Holder=holder;MasterDatums=datums;IdentityMatched=mapped;Reasons=PlacementLists.Reasons(reasons);
            HolderObstacles=obstacle;SeatingEvidence=seating??ToolPairSeatingEvidence.Missing(datums);MappingReview=mappingReview??ToolReviewValidation.Missing("MAPPING_SELECTION_UNREVIEWED");CadPair=cadPair;Source=source;}
        internal void Invalidate(){MasterDatums?.Invalidate();HolderObstacles?.Invalidate();SeatingEvidence.Invalidate();MappingReview.Invalidate();}
    }
    internal enum BendLimitKind { MaxBendLength, DeepBendClearanceExample }

    // Conditions in an externally signed Excel case. This is an applicability
    // envelope, NOT a value measured from a picture or a candidate tool pose.
    internal sealed class BendExcelCaseConditions
    {
        public string MachineSystem { get; }
        public string MaterialCode { get; }
        public string SectionKind { get; }
        public string PoseId { get; }
        public double? ThicknessMinMm { get; }
        public double? ThicknessMaxMm { get; }
        public double? AngleMinDeg { get; }
        public double? AngleMaxDeg { get; }
        public double? WMinMm { get; }
        public double? WMaxMm { get; }
        public double? LMinMm { get; }
        public double? LMaxMm { get; }
        internal BendExcelCaseConditions(BendLimitSelection s)
        {
            MachineSystem=s?.MachineSystem;MaterialCode=s?.MaterialCode;SectionKind=s?.SectionKind;PoseId=s?.PoseId;
            ThicknessMinMm=s?.ThicknessMinMm;ThicknessMaxMm=s?.ThicknessMaxMm;
            AngleMinDeg=s?.AngleMinDeg;AngleMaxDeg=s?.AngleMaxDeg;
            WMinMm=s?.WMinMm;WMaxMm=s?.WMaxMm;LMinMm=s?.LMinMm;LMaxMm=s?.LMaxMm;
        }
        private static bool Range(double? lo,double? hi,bool positive)
        {
            return lo.HasValue && hi.HasValue && ToolPoint.Finite(lo.Value) && ToolPoint.Finite(hi.Value) &&
                lo.Value<=hi.Value && (!positive || lo.Value>0);
        }
        public bool Complete => !string.IsNullOrWhiteSpace(MachineSystem) && !string.IsNullOrWhiteSpace(MaterialCode) &&
            !string.IsNullOrWhiteSpace(SectionKind) && !string.IsNullOrWhiteSpace(PoseId) &&
            Range(ThicknessMinMm,ThicknessMaxMm,true) && Range(AngleMinDeg,AngleMaxDeg,false) &&
            Range(WMinMm,WMaxMm,true) && Range(LMinMm,LMaxMm,true);
        private static string N(double? d)=>d.HasValue?ToolEvidenceCanonical.Number(d.Value):"MISSING";
        public string Binding => Complete ? ToolEvidenceCanonical.Hash("ExcelCaseConditionsV2",MachineSystem,MaterialCode,SectionKind,PoseId,
            N(ThicknessMinMm),N(ThicknessMaxMm),N(AngleMinDeg),N(AngleMaxDeg),
            N(WMinMm),N(WMaxMm),N(LMinMm),N(LMaxMm)) : null;
    }
    internal sealed class BendExcelObservation
    {
        // Populated ONLY from one current native model/source + real measurement.
        internal string ToolId,MachineSystem,MaterialCode,SectionKind,PoseId;
        internal double MeasurementToleranceMm,AngleToleranceDeg;
        internal string SourceBinding,WorkbookSha256;
        internal ToolReviewValidation Review;
        internal double? ThicknessMm,AngleDeg,Wmm,Lmm,BendLengthMm;
        internal bool Current;
        internal bool Complete => Current && Review?.Status==ToolReviewStatus.VerifiedExternalRecord &&
            ToolDatumEvidence.IsSha256(SourceBinding) && ToolDatumEvidence.IsSha256(WorkbookSha256) && Review.PayloadSha256==SourceBinding && !string.IsNullOrWhiteSpace(PoseId) &&
            ToolPoint.Finite(MeasurementToleranceMm)&&MeasurementToleranceMm>=0&&MeasurementToleranceMm<=.1&&
            ToolPoint.Finite(AngleToleranceDeg)&&AngleToleranceDeg>=0&&AngleToleranceDeg<=1&& !string.IsNullOrWhiteSpace(ToolId) &&
            !string.IsNullOrWhiteSpace(MachineSystem) && !string.IsNullOrWhiteSpace(MaterialCode) &&
            !string.IsNullOrWhiteSpace(SectionKind) && new[]{ThicknessMm,AngleDeg,Wmm,Lmm,BendLengthMm}
                .All(x=>x.HasValue&&ToolPoint.Finite(x.Value)) && ThicknessMm>0 && Wmm>0 && Lmm>0 && BendLengthMm>0;
    }
    internal enum BendExcelCaseApplicability { MATCH_UNRESOLVED, MATCH_CONFIRMED, NOT_APPLICABLE_CONFIRMED }
    internal sealed class BendExcelCaseResult
    {
        internal BendExcelCaseApplicability Applicability {get;}
        internal bool? ExceedsCertifiedLength {get;}
        internal string Evidence {get;}
        // Only a reviewed exhaustive negative match authorizes a CAD fallback.
        private readonly ToolReviewValidation completeCaseSet;
        internal bool AllowsCadFallback => Applicability==BendExcelCaseApplicability.NOT_APPLICABLE_CONFIRMED&&
            completeCaseSet?.Status==ToolReviewStatus.VerifiedExternalRecord;
        internal string AdmissionReason(bool process)
        {
            if(AllowsCadFallback)return null;
            if(Applicability==BendExcelCaseApplicability.NOT_APPLICABLE_CONFIRMED)return "EXCEL_CASE_SET_COMPLETENESS_NOT_REVIEWED";
            if(process&&Applicability==BendExcelCaseApplicability.MATCH_CONFIRMED&&ExceedsCertifiedLength==false)return null;
            return Applicability==BendExcelCaseApplicability.MATCH_UNRESOLVED?"EXCEL_APPLICABILITY_UNRESOLVED:"+Evidence:
                ExceedsCertifiedLength==true?"EXCEL_CERTIFIED_LENGTH_EXCEEDED":"EXCEL_CASE_APPLIES_NO_CAD_MAX_FALLBACK";
        }
        internal BendExcelCaseResult(BendExcelCaseApplicability state,bool? exceeded,string evidence,ToolReviewValidation completeCaseSet=null)
        {Applicability=state;ExceedsCertifiedLength=exceeded;Evidence=evidence;this.completeCaseSet=completeCaseSet;}
    }
    internal static class BendExcelCaseMatcher
    {
        private static bool InRange(double v,double lo,double hi) => v>=lo && v<=hi;
        private static CollisionInterval Band(double v,double e) => e==0?CollisionInterval.P(v):CollisionInterval.P(v)+new CollisionInterval(-e,e);
        private static bool Touches(double v,double e,double lo,double hi) {var band=Band(v,e);return band.Lo<lo||band.Hi>hi;}
        private static bool Overlaps(double v,double e,double lo,double hi) {var band=Band(v,e);return band.Hi>=lo&&band.Lo<=hi;}
        internal static BendExcelCaseResult Evaluate(BendLimitRuleDraft rule,BendExcelObservation measured)
        {
            if(rule==null||rule.ReferenceOnly||rule.Kind!=BendLimitKind.MaxBendLength||
                !rule.FamilyMappingVerified||rule.ApprovedConditions?.Complete!=true||!rule.ValueMm.HasValue)
                return new BendExcelCaseResult(BendExcelCaseApplicability.MATCH_UNRESOLVED,null,"UNAPPROVED_OR_REFERENCE_EXCEL_RULE:"+
                    rule?.Id+":"+(rule==null?"MISSING_RULE":string.Join(",",rule.Reasons)));
            if(measured?.Complete!=true)
                return new BendExcelCaseResult(BendExcelCaseApplicability.MATCH_UNRESOLVED,null,"MISSING_NATIVE_EXCEL_APPLICABILITY_MEASUREMENTS");
            if(!string.Equals(measured.WorkbookSha256,rule.WorkbookSha256,StringComparison.OrdinalIgnoreCase))
                return new BendExcelCaseResult(BendExcelCaseApplicability.MATCH_UNRESOLVED,null,"EXCEL_MEASUREMENT_WORKBOOK_SHA_MISMATCH");
            var c=rule.ApprovedConditions;
            if(!string.Equals(c.PoseId,measured.PoseId,StringComparison.Ordinal))
                return new BendExcelCaseResult(BendExcelCaseApplicability.NOT_APPLICABLE_CONFIRMED,null,
                    "REVIEWED_CASE_POSE_DIFFERS:"+rule.WorkbookSha256+"/"+rule.Id);
            bool applies=rule.ToolIds.Contains(measured.ToolId,StringComparer.OrdinalIgnoreCase) &&
                string.Equals(c.MachineSystem,measured.MachineSystem,StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.MaterialCode,measured.MaterialCode,StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.SectionKind,measured.SectionKind,StringComparison.OrdinalIgnoreCase) &&
                InRange(measured.ThicknessMm.Value,c.ThicknessMinMm.Value,c.ThicknessMaxMm.Value) &&
                InRange(measured.AngleDeg.Value,c.AngleMinDeg.Value,c.AngleMaxDeg.Value) &&
                InRange(measured.Wmm.Value,c.WMinMm.Value,c.WMaxMm.Value) &&
                InRange(measured.Lmm.Value,c.LMinMm.Value,c.LMaxMm.Value);
            string evidence=rule.WorkbookSha256+"/"+rule.Sheet+"!"+rule.Locator+"/"+rule.Id;

            if(applies && (Touches(measured.ThicknessMm.Value,measured.MeasurementToleranceMm,c.ThicknessMinMm.Value,c.ThicknessMaxMm.Value)||
                Touches(measured.AngleDeg.Value,measured.AngleToleranceDeg,c.AngleMinDeg.Value,c.AngleMaxDeg.Value)||
                Touches(measured.Wmm.Value,measured.MeasurementToleranceMm,c.WMinMm.Value,c.WMaxMm.Value)||
                Touches(measured.Lmm.Value,measured.MeasurementToleranceMm,c.LMinMm.Value,c.LMaxMm.Value)))
                return new BendExcelCaseResult(BendExcelCaseApplicability.MATCH_UNRESOLVED,null,"EXCEL_CONDITION_WITHIN_MEASUREMENT_UNCERTAINTY");
            // A near miss is also unresolved, rather than a certified negative.
            if(!applies && rule.ToolIds.Contains(measured.ToolId,StringComparer.OrdinalIgnoreCase) &&
                string.Equals(c.MachineSystem,measured.MachineSystem,StringComparison.OrdinalIgnoreCase)&&
                string.Equals(c.MaterialCode,measured.MaterialCode,StringComparison.OrdinalIgnoreCase)&&
                string.Equals(c.SectionKind,measured.SectionKind,StringComparison.OrdinalIgnoreCase)&&
                Overlaps(measured.ThicknessMm.Value,measured.MeasurementToleranceMm,c.ThicknessMinMm.Value,c.ThicknessMaxMm.Value)&&
                Overlaps(measured.AngleDeg.Value,measured.AngleToleranceDeg,c.AngleMinDeg.Value,c.AngleMaxDeg.Value)&&
                Overlaps(measured.Wmm.Value,measured.MeasurementToleranceMm,c.WMinMm.Value,c.WMaxMm.Value)&&
                Overlaps(measured.Lmm.Value,measured.MeasurementToleranceMm,c.LMinMm.Value,c.LMaxMm.Value))
                return new BendExcelCaseResult(BendExcelCaseApplicability.MATCH_UNRESOLVED,null,"EXCEL_CONDITION_WITHIN_MEASUREMENT_UNCERTAINTY");
            // This checks certified machine-supported bend-line length only.
            // It never pretends the Excel 4200/6000 values are flange heights.
            if(!applies)return new BendExcelCaseResult(BendExcelCaseApplicability.NOT_APPLICABLE_CONFIRMED,null,evidence);
            double length=measured.BendLengthMm.Value,error=measured.MeasurementToleranceMm,limit=rule.ValueMm.Value;
            var lengthBand=Band(length,error);
            if(error>0&&lengthBand.Lo<=limit&&lengthBand.Hi>limit)
                return new BendExcelCaseResult(BendExcelCaseApplicability.MATCH_UNRESOLVED,null,"EXCEL_LENGTH_WITHIN_MEASUREMENT_UNCERTAINTY:"+evidence);
            return new BendExcelCaseResult(BendExcelCaseApplicability.MATCH_CONFIRMED,lengthBand.Lo>limit,evidence);
        }
    }

    internal sealed class BendLimitRuleDraft
    {
        public string Id { get; }
        public string FamilyId { get; }
        public BendLimitKind Kind { get; }
        public string WorkbookSha256 { get; }
        public string Sheet { get; }
        public string Locator { get; }
        public string ObjectSha256 { get; }
        public string Unit { get; }
        public double? ValueMm { get; }
        public ToolReviewValidation ApplicabilityReview { get; }
        public ToolReviewValidation ReferenceDispositionReview {get;}
        public bool ReviewedInformationalReference => Kind==BendLimitKind.DeepBendClearanceExample&&
            ReferenceDispositionReview.Status==ToolReviewStatus.VerifiedExternalRecord;
        public BendExcelCaseConditions ApprovedConditions { get; }
        public bool FamilyMappingVerified => Kind==BendLimitKind.MaxBendLength && ApplicabilityReview?.Status==ToolReviewStatus.VerifiedExternalRecord &&
            ApprovedConditions?.Complete==true && ToolDatumEvidence.IsSha256(WorkbookSha256)&&ToolDatumEvidence.IsSha256(ObjectSha256)&&
            ValueMm.HasValue&&ToolPoint.Finite(ValueMm.Value)&&ValueMm.Value>=0&&ToolIds.Count>0&&
            ToolIds.All(id=>CompanyToolIds.All.Contains(id,StringComparer.OrdinalIgnoreCase))&&Reasons.Count==0;
        public bool ReferenceOnly => !FamilyMappingVerified;
        public IReadOnlyList<string> ToolIds { get; }
        public IReadOnlyList<string> Reasons { get; }
        internal BendLimitRuleDraft(string id,string family,BendLimitKind kind,string sha,string sheet,string locator,string objectSha,string unit,double? value,IEnumerable<string> ids,IEnumerable<string> reasons,
            ToolReviewValidation applicabilityReview=null,BendExcelCaseConditions approvedConditions=null,ToolReviewValidation referenceDispositionReview=null)
        {Id=id;FamilyId=family;Kind=kind;WorkbookSha256=sha;Sheet=sheet;Locator=locator;ObjectSha256=objectSha;Unit=unit;ValueMm=value;
            ToolIds=PlacementLists.Copy(ids);Reasons=PlacementLists.Reasons(reasons);
            ApplicabilityReview=applicabilityReview??ToolReviewValidation.Missing("EXCEL_CASE_NOT_APPROVED");
            ApprovedConditions=approvedConditions;ReferenceDispositionReview=referenceDispositionReview??ToolReviewValidation.Missing("EXCEL_REFERENCE_DISPOSITION_NOT_REVIEWED");}
    }
    internal sealed class PlacementSourceSnapshot
    {
        private readonly PlacementLease lease=new PlacementLease();
        internal IReadOnlyList<BendExcelMeasurement> ExcelMeasurements { get; }
        internal ToolReviewValidation ExcelCaseSetReview { get; }
        // A once-signed Excel rule expires dynamically, not only when a JSON
        // file is reloaded. Invalidate the whole held source snapshot on expiry.
        public bool Current => lease.Current && LimitRules.All(r=>r.ApplicabilityReview?.Status!=ToolReviewStatus.Stale&&r.ReferenceDispositionReview.Status!=ToolReviewStatus.Stale) &&
            ExcelCaseSetReview.Status!=ToolReviewStatus.Stale && ExcelMeasurements.All(m=>m.Review.Status!=ToolReviewStatus.Stale);
        public string Status => "CHUA_XAC_MINH";
        public string Fingerprint { get; }
        public IReadOnlyList<ToolAssemblyProfile> Tools { get; }
        public IReadOnlyList<BendLimitRuleDraft> LimitRules { get; }
        public IReadOnlyList<string> Reasons { get; }
        internal PlacementSourceSnapshot(string fingerprint,IEnumerable<ToolAssemblyProfile> tools,IEnumerable<BendLimitRuleDraft> rules,IEnumerable<string> reasons,
            ToolReviewValidation caseSetReview=null,IEnumerable<BendExcelMeasurement> measurements=null)
        {Fingerprint=fingerprint;Tools=PlacementLists.Copy(tools);LimitRules=PlacementLists.Copy(rules);Reasons=PlacementLists.Reasons(reasons);
            ExcelCaseSetReview=caseSetReview??ToolReviewValidation.Missing("EXCEL_CASE_SET_COMPLETENESS_NOT_REVIEWED");ExcelMeasurements=PlacementLists.Copy(measurements);}
        internal void Invalidate(){lease.Invalidate();foreach(var t in Tools)t.Invalidate();}
        internal static PlacementSourceSnapshot Missing(string reason) => new PlacementSourceSnapshot("Missing:"+reason,CompanyToolIds.All.Select(id=>
            new ToolAssemblyProfile(id,null,null,null,null,null,false,new[]{reason,"MISSING_MASTER_MAPPING","UNVERIFIED_HOLDER"})),null,new[]{reason});
    }
    internal sealed class SheetSection2D
    {
        public int FeatureId { get; }
        public BendWorkFrame Frame { get; }
        public double? ThicknessMm { get; }
        public SheetCoverage Coverage { get; }
        public IReadOnlyList<ToolCurve> Boundary { get; }
        public ToolRegionValidation Region { get; }
        public IReadOnlyList<string> Reasons { get; }
        internal SheetSection2D(int id,BendWorkFrame frame,double? thickness,SheetCoverage coverage,IEnumerable<ToolCurve> curves,ToolRegionValidation region,IEnumerable<string> reasons)
        {FeatureId=id;Frame=frame;ThicknessMm=thickness;Coverage=coverage;Boundary=PlacementLists.Copy(curves);Region=region;Reasons=PlacementLists.Reasons(reasons);}
    }
    internal sealed class BendStateDraft2D
    {
        public BendStateCoverage Coverage { get; }
        public SheetSection2D Section { get; }
        public bool SequenceVerified => false;
        internal BendStateDraft2D(BendStateCoverage coverage,SheetSection2D section=null){Coverage=coverage;Section=section;}
    }
    internal sealed class ToolGeometryPlacement2D
    {
        public GeometrySelection2D Geometry { get; }
        public PlacementTransform2D ToWorkFrame { get; }
        public IReadOnlyList<ToolCurve> PlacedCurves { get; }
        public string WorkFrameId { get; }
        internal ToolGeometryPlacement2D(GeometrySelection2D geometry,PlacementTransform2D transform,string frame)
        {Geometry=geometry;ToWorkFrame=transform;WorkFrameId=frame;PlacedCurves=PlacementLists.Copy(geometry==null||transform==null?null:geometry.Curves.Select(c=>transform.Apply(c)));}
    }
    internal sealed class ToolCandidateDraft
    {
        private readonly Func<bool> current;
        public string ToolId { get; }
        public int FeatureId { get; }
        public string Status => "CHUA_XAC_MINH";
        public bool Current => current();
        public bool ReadyForCollisionCheck => false; // physical/process evidence is not established in Phase 3B
        // This is readiness to inspect a fully attested SIMULATION placement,
        // not production PASS or permission to ignore approximation bands.
        public bool GeometryCheckReady => Current&&SimulationOnly&&Assembly?.GeometryCheckReady==true&&Sheet.Coverage!=SheetCoverage.Unsupported2DEnvelope&&
            PunchPlacement.ToWorkFrame!=null&&HolderPlacement.ToWorkFrame!=null&&HolderObstaclePlacement.HasConservativeGeometry;
        public SheetEnvelopeReadiness EnvelopeReadiness => new SheetEnvelopeReadiness(Sheet);
        public PlacementReadiness Readiness { get; }
        public bool SimulationOnly { get; }
        public bool HasExplicitCollisionPose { get; }
        public CollisionWorkPoseEvidence ProductionCollisionPose { get; }
        public CandidateCollisionResult Collision { get; }
        public bool PlacementGeometryComplete => Current&&Assembly?.MappingIdentified==true&&Sheet.Coverage!=SheetCoverage.Unsupported2DEnvelope&&
            PunchPlacement.ToWorkFrame!=null&&HolderPlacement.ToWorkFrame!=null&&Assembly.Punch.Supported&&Assembly.Holder.Supported&&
            Assembly.Punch.Region.IsValidRegion&&Assembly.Holder.Region.IsValidRegion&&Assembly.Punch.ObservedClosed&&Assembly.Holder.ObservedClosed&&
            Assembly.Punch.Region.MaximumJointGapMm<=1e-10&&Assembly.Holder.Region.MaximumJointGapMm<=1e-10;
        public ToolAssemblyProfile Assembly { get; }
        public DatumEvidenceStatus DatumStatus => Assembly?.MasterDatums?.Status??DatumEvidenceStatus.Missing;
        public ToolGeometryPlacement2D PunchPlacement { get; }
        public ToolGeometryPlacement2D HolderPlacement { get; }
        public HolderObstaclePlacement2D HolderObstaclePlacement { get; }
        public SheetSection2D Sheet { get; }
        public IReadOnlyList<BendStateDraft2D> BendStates { get; }
        public IReadOnlyList<BendLimitRuleDraft> LimitRules { get; }
        private readonly IReadOnlyList<string> reasons;
        public IReadOnlyList<string> Reasons => PlacementLists.Reasons(reasons
            .Concat(HolderObstaclePlacement.Reason==null?new string[0]:new[]{HolderObstaclePlacement.Reason})
            .Concat(Current?new string[0]:new[]{"STALE_SOURCE"})
            .Concat(DatumStatus==DatumEvidenceStatus.Stale?new[]{"STALE_DATUM_EVIDENCE"}:new string[0])
            .Concat(Assembly?.SeatingEvidence.Status==ToolReviewStatus.Stale||Assembly?.MappingReview.Status==ToolReviewStatus.Stale||Assembly?.HolderObstacles?.Current==false?new[]{"STALE_HOLDER_OR_SEATING_EVIDENCE"}:new string[0]));
        internal ToolCandidateDraft(string id,int feature,ToolAssemblyProfile assembly,SheetSection2D sheet,ToolGeometryPlacement2D punch,ToolGeometryPlacement2D holder,
            IEnumerable<BendLimitRuleDraft> rules,IEnumerable<string> reasons,bool simulation,Func<bool> current,bool explicitCollisionPose=false,CollisionWorkPoseEvidence productionPose=null,bool evaluateLegacyCollision=true)
        {
            ToolId=id;FeatureId=feature;Assembly=assembly;Sheet=sheet;PunchPlacement=punch;HolderPlacement=holder;LimitRules=PlacementLists.Copy(rules);
            HolderObstaclePlacement=new HolderObstaclePlacement2D(assembly?.HolderObstacles,holder.ToWorkFrame,sheet.Frame?.FrameId);
            this.reasons=PlacementLists.Reasons(reasons);SimulationOnly=simulation;Readiness=simulation?PlacementReadiness.SimulationOnly:PlacementReadiness.PlacementDraft;this.current=current;
            HasExplicitCollisionPose=explicitCollisionPose;ProductionCollisionPose=productionPose;
            BendStates=PlacementLists.Copy(new[]{new BendStateDraft2D(BendStateCoverage.UnknownBefore),new BendStateDraft2D(BendStateCoverage.UnknownDuring),
                new BendStateDraft2D(BendStateCoverage.UnknownAfter),new BendStateDraft2D(BendStateCoverage.FinalModelObserved,sheet)});
            if(evaluateLegacyCollision)Collision=PlacementCollisionEvaluator.Evaluate(this);
        }
    }
    internal sealed class PlacementDraft
    {
        private readonly Func<bool> current;
        public string Status => "CHUA_XAC_MINH";
        public bool Current => current();
        public string ModelSnapshotId { get; }
        public int ModelUpdateStamp { get; }
        public string Configuration { get; }
        public IReadOnlyList<SheetSection2D> Sections { get; }
        public IReadOnlyList<ToolCandidateDraft> Candidates { get; }
        public IReadOnlyList<CandidateCollisionResult> CollisionResults { get; }
        public string WinnerToolId => null;
        internal PlacementDraft(string modelId,int stamp,string configuration,IEnumerable<SheetSection2D> sections,IEnumerable<ToolCandidateDraft> candidates,Func<bool> current)
        {ModelSnapshotId=modelId;ModelUpdateStamp=stamp;Configuration=configuration;Sections=PlacementLists.Copy(sections);Candidates=PlacementLists.Copy(candidates);CollisionResults=PlacementLists.Copy(Candidates.Select(c=>c.Collision).Where(c=>c!=null));this.current=current;}
    }
}
