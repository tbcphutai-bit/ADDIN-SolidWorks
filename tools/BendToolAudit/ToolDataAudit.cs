using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Tools.BendToolAudit
{
    internal sealed class ToolAuditRow
    {
        public string ToolId { get; set; }
        public bool SeparateMappingFollowup { get; set; }
        public string Readiness { get; set; }
        public string PunchRead { get; set; }
        public string PunchRegion { get; set; }
        public string MasterPunchRegion { get; set; }
        public string HolderSelection { get; set; }
        public string MappingReview { get; set; }
        public string MaterialReview { get; set; }
        public string DatumReview { get; set; }
        public string SeatingReview { get; set; }
        public string PhysicalPair { get; set; }
        public string XYWorkPose { get; set; }
        public string PunchAxial { get; set; }
        public string HolderAxial { get; set; }
        public int SelectedSplineCount { get; set; }
        public int SupportedSplineCount { get; set; }
        public List<AuditIssue> Issues { get; set; } = new List<AuditIssue>();
        public List<object> SourceRevisions { get; set; } = new List<object>();
        public List<object> ApplicableLimits { get; set; } = new List<object>();
        public List<object> ReferenceLimits { get; set; } = new List<object>();
        public List<object> AxialObservations { get; set; } = new List<object>();
        public object PrivateSelection { get; set; }
        public string[] ReasonCodes => Issues.Select(i => i.Code).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        public string SourceRevision => ReasonCodes.Contains("SOURCE_REVISION_UNVERIFIED") ? "UNVERIFIED" : "ATTESTED";
        public string LimitApplicability => ReasonCodes.Contains("LIMIT_RULE_UNMAPPED") ? "UNMAPPED" : "ATTESTED";
    }
    internal sealed class AuditModelContext
    {
        internal PlacementModelCapture Model;
        internal IReadOnlyList<SheetEnvelopeCapture> Envelopes;
        internal ToolReviewValidation CaptureReview;
        internal string Diagnostic;
    }
    internal static class ToolDataAudit
    {
        internal static readonly string[] SeparateMappings = { "AMD-STD-004", "AMD-STD-008", "AMD-STD-117", "AMD-STD-452" };
        internal static readonly IReadOnlyDictionary<string, string> Actions = new Dictionary<string, string>
        {
            ["CODE_UNSUPPORTED"] = "Gửi hợp đồng/nguồn chưa hỗ trợ cho kỹ thuật; không sửa biên dạng để vượt kiểm tra.",
            ["SOURCE_REVISION_UNVERIFIED"] = "Đối chiếu đúng revision được yêu cầu, SHA file và manifest; người có thẩm quyền ký ToolSourceRevision kèm tài liệu nguồn.",
            ["PUNCH_REGION_INVALID"] = "Kiểm tra Master và DIM gốc. Với KIK hở, lấy nguồn biên vật liệu đúng đã duyệt; không tự thêm cạnh đóng.",
            ["MAPPING_UNRESOLVED"] = "Xác định label/TEXT, root INSERT, block và toàn bộ entity instance keys từ bản vẽ và dao thật; ký ToolHolderMapping.",
            ["MAPPING_SELECTION_UNREVIEWED"] = "Đối chiếu lựa chọn Holder tham chiếu với Punch và máy thật; người có thẩm quyền ký ToolHolderMapping cho đúng instance keys.",
            ["MATERIAL_RULE_UNREVIEWED"] = "Phân loại mỗi entity đúng một lần: Solid/Hole/Island có parent hoặc exclusion có vai trò; duyệt HolderMaterialRule và mọi boundary band.",
            ["DATUM_UNVERIFIED"] = "Đo riêng tip Punch, mount Punch, mount Holder; xác nhận source frame, trục/hướng và sai số; lập datum evidence.",
            ["PHYSICAL_PAIR_UNVERIFIED"] = "Kiểm tra đúng Punch–Holder trên máy, lập biên bản/ảnh/đo; người có quyền physical inspection ký ToolPairSeating, ngày duyệt và hết hạn.",
            ["SEATING_UNVERIFIED"] = "Đo relative rigid transform và mặt tựa; holder mount phải trùng punch mount trong tolerance hiện có. Ký payload đúng source/material/datum.",
            ["XY_WORK_POSE_UNVERIFIED"] = "Đo contact, approach và mirror cho từng feature/work frame của setup; Punch và Holder phải dùng cùng pose đã ký.",
            ["AXIAL_LENGTH_UNVERIFIED"] = "Đo độc lập từng đoạn Punch và Holder: LocalStartMm/LocalEndMm, origin, axis sign và axial uncertainty; ký hai purpose PunchAxialSetup/HolderAxialSetup.",
            ["LIMIT_RULE_UNMAPPED"] = "Kỹ thuật duyệt family → tool ID → máy/setup, đúng workbook/object SHA, loại giới hạn, đơn vị và giá trị. Ví dụ deep-bend vẫn chỉ là tham khảo.",
            ["MODEL_CONTEXT_UNVERIFIED"] = "Cung cấp full managed capture và SHA Part đã lưu, configuration/stamp/frame; ký OfflineModelCapture. Chạy lại audit khi nguồn thay đổi.",
            ["STALE_OR_INVALID_EVIDENCE"] = "Thay hồ sơ sai/hết hạn, kiểm tra artifact bytes và quyền signer; kiểm định lại trước khi ký revision mới."
        };
        private static void Missing(ToolAuditRow r, string code, string field, string diagnostic)
        {
            r.Issues.Add(new AuditIssue { Code = code, Field = field, Diagnostic = diagnostic,
                RequiredEvidence = Actions[code] });
        }
        internal static IReadOnlyList<ToolAuditRow> Evaluate(ToolProfileLibrarySnapshot library, AuditIntake intake,
            IReadOnlyList<AuditSourceFact> sources, ToolReviewVerifier verifier, string evidenceRoot,
            AuditModelContext context = null, CancellationToken token = default(CancellationToken))
        {
            var rows = new List<ToolAuditRow>();
            foreach (string id in CompanyToolIds.All)
            {
                token.ThrowIfCancellationRequested();
                var dim = library.Tools.SingleOrDefault(t => t.Id == id);
                var a = library.PlacementSources.Tools.SingleOrDefault(t => t.Id == id);
                var r = new ToolAuditRow { ToolId = id, SeparateMappingFollowup = SeparateMappings.Contains(id),
                    PunchRead = dim?.Validation.PunchGeometryReadComplete == true ? "PARSED" : "UNKNOWN",
                    PunchRegion = dim?.Validation.Region.Status.ToString() ?? "UNKNOWN",
                    MasterPunchRegion = a?.Punch?.Region.Status.ToString() ?? "UNKNOWN",
                    HolderSelection = a?.Holder == null ? "UNRESOLVED" : "REFERENCE_SELECTED",
                    MappingReview = a?.MappingReview.Status.ToString() ?? "Missing",
                    MaterialReview = a?.HolderObstacles?.Review.Status.ToString() ?? "Missing",
                    DatumReview = a?.MasterDatums?.Status.ToString() ?? "Missing",
                    SeatingReview = a?.SeatingEvidence.Status.ToString() ?? "Missing",
                    PhysicalPair = a?.PhysicalPairVerified == true ? "ATTESTED" : "UNKNOWN",
                    SelectedSplineCount = a?.HolderObstacles?.SplineCount ?? 0,
                    SupportedSplineCount = a?.HolderObstacles?.SupportedSplineCount ?? 0 };
                bool sourceExternal = true, sourceGeometry = true;
                foreach (string key in new[] { "Master", "Workbook", "DIM:" + id })
                {
                    var matches = sources.Where(s => s.Key == key).ToArray();
                    var fact = matches.Length == 1 ? matches[0] : null;
                    var review = AuditAttestations.Revision(fact, intake, verifier, evidenceRoot, token);
                    r.SourceRevisions.Add(new { Key = key, Fact = fact, Claim = intake.SourceRevisions?.Where(s => s?.Key == key).ToArray(), Review = review });
                    if (review.Status != ToolReviewStatus.VerifiedExternalRecord)
                        Missing(r, "SOURCE_REVISION_UNVERIFIED", "SourceRevisions[" + key + "]", review.Reason);
                    sourceExternal &= review.Status == ToolReviewStatus.VerifiedExternalRecord;
                    if (key != "Workbook") sourceGeometry &= review.RecordVerified;
                }
                bool punch = dim?.Validation.PunchGeometryReadComplete == true && dim.Validation.PunchClosed && dim.Validation.Region.IsValidRegion &&
                    a?.Punch?.Supported == true && a.Punch.ObservedClosed && a.Punch.Region.IsValidRegion && a.Punch.Region.MaximumJointGapMm <= 1e-10;
                if (!punch) Missing(r, "PUNCH_REGION_INVALID", "Punch.DIM_and_Master.Region", r.PunchRegion + "/" + r.MasterPunchRegion);
                if (a?.Punch?.UnsupportedTypes.Count > 0 || dim?.Validation.PunchEntitiesSupported == false)
                    Missing(r, "CODE_UNSUPPORTED", "Punch.Entities", "UNSUPPORTED_OR_MISSING_PUNCH_SOURCE");
                if (a?.MappingIdentified != true)
                    Missing(r, "MAPPING_UNRESOLVED", "Mappings[" + id + "]", a?.MappingReview.Reason ?? "MISSING_MASTER_MAPPING");
                else if (!a.MappingReview.RecordVerified)
                    Missing(r, "MAPPING_SELECTION_UNREVIEWED", "Mappings[" + id + "].Review", a.MappingReview.Reason);
                if (a?.HolderObstacles?.GeometrySupportedForCollision != true)
                    Missing(r, "MATERIAL_RULE_UNREVIEWED", "Materials[" + id + "].Contours/Excluded/Review",
                        string.Join(";", a?.HolderObstacles?.Reasons ?? new[] { "MISSING_HOLDER_MATERIAL" }));
                if (a?.HolderObstacles?.Inventory.Any(e => !e.Supported) == true)
                    Missing(r, "CODE_UNSUPPORTED", "Holder.Inventory", string.Join(";", a.HolderObstacles.Inventory.Where(e => !e.Supported).Select(e => e.Reason).Distinct()));
                if (a?.MasterDatums?.Status != DatumEvidenceStatus.VerifiedRecord)
                    Missing(r, "DATUM_UNVERIFIED", "MasterDatums.Tip/Mount/WorkingDirection/LocalCoordinateFrame", r.DatumReview);
                if (a?.SeatingEvidence.CoordinateRecordReady != true)
                    Missing(r, "SEATING_UNVERIFIED", "Pairs[" + id + "].HolderSourceToPunchSource/Datums/Review",
                        string.Join(";", a?.SeatingEvidence.Reasons ?? new[] { "MISSING_PAIR_SEATING_EVIDENCE" }));
                if (a?.PhysicalPairVerified != true)
                    Missing(r, "PHYSICAL_PAIR_UNVERIFIED", "Pairs[" + id + "].PhysicalInspectionConfirmed/Review", "NO_CURRENT_AUTHORIZED_PHYSICAL_ATTESTATION");

                bool limitsExternal = false;
                foreach (var rule in library.PlacementSources.LimitRules)
                {
                    r.ReferenceLimits.Add(new { rule.Id, rule.FamilyId, Kind = rule.Kind.ToString(), rule.WorkbookSha256, rule.Sheet, rule.Locator,
                        rule.ObjectSha256, rule.Unit, rule.ValueMm, Applicability = "REFERENCE_ONLY", rule.Reasons });
                }
                var limitRecords = (intake.LimitApplications ?? new List<LimitApplicationRecord>()).Where(l => l?.ToolId == id).ToArray();
                foreach (var record in limitRecords)
                {
                    var rule = library.PlacementSources.LimitRules.SingleOrDefault(l => l.Id == record.RuleId);
                    var review = AuditAttestations.Limit(record, rule, a, intake, verifier, evidenceRoot, token);
                    bool duplicate = limitRecords.Count(l => l.RuleId == record.RuleId) != 1;
                    r.ApplicableLimits.Add(new { Record = record, Review = review, Accepted = !duplicate && review.Status == ToolReviewStatus.VerifiedExternalRecord });
                    if (duplicate || review.Status != ToolReviewStatus.VerifiedExternalRecord)
                        Missing(r, "LIMIT_RULE_UNMAPPED", "LimitApplications[" + id + "/" + record.RuleId + "]", duplicate ? "DUPLICATE_LIMIT_APPLICATION" : review.Reason);
                    else limitsExternal = true;
                }
                if (!limitsExternal) Missing(r, "LIMIT_RULE_UNMAPPED", "LimitApplications[" + id + "]", "NO_VERIFIED_MACHINE_SETUP_LIMIT_APPLICABILITY");

                bool contextReady = context?.Model?.Lease.Current == true && !context.Model.SimulationOnly && context.Model.Bends.Count > 0 &&
                    context.CaptureReview?.Status == ToolReviewStatus.VerifiedExternalRecord && context.Diagnostic == null;
                if (!contextReady) Missing(r, "MODEL_CONTEXT_UNVERIFIED", "ModelCapturePath/ModelSourceSha256/ModelCaptureReview", context?.Diagnostic ?? "MISSING_ATTESTED_OFFLINE_MODEL_CONTEXT");
                bool xy = context?.Model?.Bends.Count > 0, punchAxial = xy, holderAxial = xy, coverage = xy;
                foreach (var extra in (intake.AxialSetups ?? new List<ToolAxialSetupRecord>()).Where(s => s?.ToolId == id &&
                    !(context?.Model?.Bends.Any(b => b.FeatureId == s.FeatureId) ?? false)))
                    Missing(r, "AXIAL_LENGTH_UNVERIFIED", "AxialSetups[" + id + "/" + extra.FeatureId + "]", "AXIAL_SETUP_WITHOUT_MATCHING_MODEL_FEATURE");
                foreach (var bend in context?.Model?.Bends ?? new PlacementBendSnapshot[0])
                {
                    var envelopes = (context.Envelopes ?? new SheetEnvelopeCapture[0]).Where(e => e.Binding.FeatureId == bend.FeatureId).ToArray();
                    var sheet = envelopes.Length == 1 ? envelopes[0] : null;
                    if (sheet?.Certificate.ContinuousCoverageProven != true || !sheet.Current)
                    {
                        coverage = false;
                        Missing(r, "CODE_UNSUPPORTED", "Envelope[" + bend.FeatureId + "]", string.Join(";", sheet?.Reasons ?? new[] { "MISSING_OR_DUPLICATE_ENVELOPE" }));
                    }
                    var records = (intake.AxialSetups ?? new List<ToolAxialSetupRecord>()).Where(s => s?.ToolId == id && s.FeatureId == bend.FeatureId).ToArray();
                    ToolAxialSetupRecord first = null;
                    foreach (CollisionPairKind pair in Enum.GetValues(typeof(CollisionPairKind)))
                    {
                        var matching = records.Where(s => s.Pair == pair.ToString()).ToArray();
                        ToolAxialSetupEvidence evidence = null;
                        string reason = matching.Length > 1 ? "DUPLICATE_AXIAL_SETUP" : "MISSING_AXIAL_SETUP";
                        if (matching.Length == 1 && sheet != null)
                        {
                            evidence = ToolAxialSetupEvidence.Read(matching[0], sheet, library, verifier, evidenceRoot, token);
                            reason = string.Join(";", evidence.Occupancy.Reasons);
                        }
                        bool valid = evidence?.Occupancy.Verified == true && evidence.Review.RecordVerified;
                        bool external = valid && evidence.Review.Status == ToolReviewStatus.VerifiedExternalRecord && !evidence.Occupancy.SimulationOnly;
                        if (pair == CollisionPairKind.WorkpiecePunch) punchAxial &= external; else holderAxial &= external;
                        if (!external) Missing(r, "AXIAL_LENGTH_UNVERIFIED", "AxialSetups[" + id + "/" + bend.FeatureId + "/" + pair + "]", reason);
                        r.AxialObservations.Add(new { FeatureId = bend.FeatureId, Pair = pair.ToString(), Record = matching.Length == 1 ? matching[0] : null,
                            Review = evidence?.Review, Verified = evidence?.Occupancy.Verified, Segments = evidence?.Occupancy.Segments, Diagnostic = reason });
                        if (!valid) { xy = false; continue; }
                        if (first != null && (first.ContactPointMm.X != matching[0].ContactPointMm.X || first.ContactPointMm.Y != matching[0].ContactPointMm.Y ||
                            first.ApproachDirection.X != matching[0].ApproachDirection.X || first.ApproachDirection.Y != matching[0].ApproachDirection.Y ||
                            first.MirrorApproved != matching[0].MirrorApproved)) xy = false;
                        first = matching[0];
                        xy &= external;
                    }
                }
                if (context?.Model?.Bends.Count > 0 != true)
                {
                    Missing(r, "AXIAL_LENGTH_UNVERIFIED", "AxialSetups[" + id + "].Punch", "MISSING_SOURCE_BOUND_FINITE_PUNCH_SEGMENTS");
                    Missing(r, "AXIAL_LENGTH_UNVERIFIED", "AxialSetups[" + id + "].Holder", "MISSING_SOURCE_BOUND_FINITE_HOLDER_SEGMENTS");
                }
                xy &= contextReady; punchAxial &= contextReady; holderAxial &= contextReady;
                if (!xy) Missing(r, "XY_WORK_POSE_UNVERIFIED", "AxialSetups[" + id + "].ContactPointMm/ApproachDirection/MirrorApproved", "MISSING_OR_INCONSISTENT_EXACT_WORK_POSE");
                r.XYWorkPose = xy ? "ATTESTED" : "UNKNOWN"; r.PunchAxial = punchAxial ? "ATTESTED" : "UNKNOWN"; r.HolderAxial = holderAxial ? "ATTESTED" : "UNKNOWN";
                bool current = library.Current && library.PlacementSources.Current;
                if (!current) Missing(r, "STALE_OR_INVALID_EVIDENCE", "Library.Current", "STALE_SOURCE_SNAPSHOT");
                bool geometry = current && punch && sourceGeometry && a?.GeometryCheckReady == true;
                bool production = geometry && sourceExternal && a.PhysicalPairVerified && a.MappingReview.Status == ToolReviewStatus.VerifiedExternalRecord &&
                    a.HolderObstacles.Review.Status == ToolReviewStatus.VerifiedExternalRecord && contextReady && coverage && xy && punchAxial && holderAxial && limitsExternal && r.Issues.Count == 0;
                r.Readiness = production ? "READY_PRODUCTION_STATIC_CHECK" : geometry ? "READY_GEOMETRY_ONLY" : "UNKNOWN";
                r.PrivateSelection = new { MasterSha256 = a?.MasterSha256, ManifestSha256 = a?.ManifestSha256,
                    Punch = a?.Punch?.EntityKeys, Holder = a?.Holder?.EntityKeys, Material = a?.HolderObstacles?.DefinitionSha256,
                    DrawingRole = "HolderClampReference", RawReasons = a?.Reasons };
                rows.Add(r);
            }
            return rows.AsReadOnly();
        }
    }
}
