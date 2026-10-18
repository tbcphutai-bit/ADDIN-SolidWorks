using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Tools.BendToolAudit
{
    internal static class AuditExport
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        internal static string Json(object value) => new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }.Serialize(value);
        private static void Write(string output, string name, string value)
        { using (var stream = new FileStream(Path.Combine(output, name), FileMode.CreateNew, FileAccess.Write)) using (var writer = new StreamWriter(stream, Utf8)) writer.Write(value); }
        internal static object SummaryRow(ToolAuditRow r) => new { r.ToolId, r.SeparateMappingFollowup, r.Readiness, r.PunchRead,
            r.PunchRegion, r.MasterPunchRegion, r.HolderSelection, r.MappingReview, r.MaterialReview, r.DatumReview, r.SeatingReview,
            r.PhysicalPair, r.XYWorkPose, r.PunchAxial, r.HolderAxial, r.SourceRevision, r.LimitApplicability,
            ReasonCodes = r.ReasonCodes.Where(c => ToolDataAudit.Actions.ContainsKey(c)).ToArray() };
        internal static string SummaryMarkdown(IReadOnlyList<ToolAuditRow> rows)
        {
            var b = new StringBuilder("# Tình trạng 18 Punch–Holder — Issue #17\n\n");
            b.Append("Báo cáo tại thời điểm đọc nguồn; chưa phải kết quả va chạm hoặc khả năng chấn. Revision yêu cầu cần được đối chiếu bằng bằng chứng riêng.\n\n");
            b.Append("| Mã | Revision nguồn | DIM / Master region | Holder | Mapping review | Vật liệu | Datum | Seating | Gá vật lý | XY | Dài Punch / Holder | Excel áp dụng | Readiness |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |\n");
            foreach (var r in rows) b.Append('|').Append(r.ToolId).Append(r.SeparateMappingFollowup ? " *" : "").Append('|')
                .Append(r.SourceRevision).Append('|').Append(r.PunchRegion).Append(" / ").Append(r.MasterPunchRegion).Append('|').Append(r.HolderSelection).Append('|')
                .Append(r.MappingReview).Append('|').Append(r.MaterialReview).Append('|').Append(r.DatumReview).Append('|').Append(r.SeatingReview).Append('|')
                .Append(r.PhysicalPair).Append('|').Append(r.XYWorkPose).Append('|').Append(r.PunchAxial).Append(" / ").Append(r.HolderAxial)
                .Append('|').Append(r.LimitApplicability).Append('|').Append(r.Readiness).Append("|\n");
            b.Append("\nDấu *: theo dõi mapping riêng. `Missing`/`UNKNOWN` là chưa có bằng chứng được chấp nhận. `REFERENCE_SELECTED` chỉ là lựa chọn tham chiếu.\n\n");
            foreach (var group in rows.SelectMany(r => r.ReasonCodes.Select(code => new { r.ToolId, Code = code })).GroupBy(x => x.Code).OrderBy(g => g.Key))
                b.Append("- `").Append(group.Key).Append("`: ").Append(group.Count()).Append("/18 — ").Append(ToolDataAudit.Actions[group.Key]).Append('\n');
            return b.ToString();
        }
        internal static void Report(string output, IReadOnlyList<ToolAuditRow> rows, string codeRevision, string inputFingerprint)
        {
            var now = DateTime.UtcNow.ToString("o");
            Write(output, "PRIVATE-audit.json", Json(new { Version = 1, ObservedUtc = now, CodeRevision = codeRevision, InputFingerprint = inputFingerprint,
                Scope = "Saved-source data readiness only; no collision query, live CAD freshness or manufacturing verdict", Tools = rows }));
            // Even the minimized summary contains company IDs/status. Every
            // generated artifact stays private; none is a GitHub export.
            Write(output, "PRIVATE-status-summary.json", Json(new { Version = 1, ObservedUtc = now, Tools = rows.Select(SummaryRow).ToArray() }));
            Write(output, "PRIVATE-status-summary.md", SummaryMarkdown(rows));
            string Q(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
            var csv = new StringBuilder("ToolId,Readiness,SourceRevision,PunchRead,DIMRegion,MasterRegion,HolderSelection,MappingReview,MaterialReview,DatumReview,SeatingReview,PhysicalPair,XYWorkPose,PunchAxial,HolderAxial,LimitApplicability,MissingCodes\r\n");
            foreach (var r in rows) csv.AppendLine(string.Join(",", new[] { r.ToolId, r.Readiness, r.SourceRevision, r.PunchRead, r.PunchRegion, r.MasterPunchRegion, r.HolderSelection,
                r.MappingReview, r.MaterialReview, r.DatumReview, r.SeatingReview, r.PhysicalPair, r.XYWorkPose, r.PunchAxial, r.HolderAxial, r.LimitApplicability, string.Join(";", r.ReasonCodes) }.Select(Q)));
            Write(output, "18-tool-status.csv", csv.ToString());
            Write(output, "PRIVATE-four-mappings.json", Json(rows.Where(r => r.SeparateMappingFollowup).ToArray()));
            var b = new StringBuilder("# PRIVATE — phiếu kiểm định 18 Punch–Holder\n\n");
            b.Append("Phiếu chưa ký. Không có mục đo/duyệt nào được tự điền. Tất cả raw CAD, Excel, tọa độ, reviewer, artifact, chữ ký và khóa phải ở kho công ty.\n\n");
            b.Append("Máy: ______  Setup/revision: ______  Ngày đo: ______  Người đo: ______  Người duyệt có thẩm quyền: ______\n\n");
            b.Append("Mỗi mục cần: giá trị/đơn vị/frame, phương pháp và dụng cụ đo/sai số, artifact + SHA, reviewer/key có đúng purpose, ngày duyệt/hết hạn. Khi nguồn/selection/material/datum/pose đổi, lập lại các hồ sơ phụ thuộc.\n\n");
            foreach (var r in rows)
            {
                b.Append("## ").Append(r.ToolId).Append(r.SeparateMappingFollowup ? " — mapping theo dõi riêng" : "").Append("\n\n");
                b.Append("Trạng thái quan sát: `").Append(r.Readiness).Append("`. Không ký khi còn mục trống.\n\n");
                foreach (var issue in r.Issues) b.Append("- [ ] `").Append(issue.Code).Append("` — `").Append(issue.Field).Append("`: ")
                    .Append(issue.RequiredEvidence).Append("\n  Artifact/ref: ______  Giá trị/frame/sai số: ______  Người duyệt/ngày/hết hạn: ______\n");
                b.Append("\n- [ ] Đo chiều dài thực từng đoạn Punch độc lập: start/end ______; origin ______; sign ±1 ______; uncertainty ______ mm.\n")
                    .Append("- [ ] Đo chiều dài thực từng đoạn Holder độc lập: start/end ______; origin ______; sign ±1 ______; uncertainty ______ mm.\n")
                    .Append("- [ ] Ghi dải lắp thực theo bend axis cho mọi đoạn, kể cả khoảng trống; xác nhận uniform profile trên từng đoạn. Không chép chiều dài Punch sang Holder.\n")
                    .Append("- [ ] Đối chiếu family Excel và máy/setup; 80/105 mm và 4200/6000 mm không phải default. Deep-bend example cần quy tắc kỹ thuật riêng.\n")
                    .Append("- [ ] Kiểm tra nguồn sau đo; chỉ người có thẩm quyền hoàn tất hồ sơ ngoài Git và ký đúng purpose.\n\n");
            }
            Write(output, "PRIVATE-operator-checklist.md", b.ToString());
        }
        internal static void Templates(string output, ToolProfileLibrarySnapshot library, IReadOnlyList<AuditSourceFact> facts, MasterAssemblyManifest manifest)
        {
            var intake = new AuditIntake();
            foreach (var fact in facts)
            {
                intake.ExpectedRevisions[fact.Key] = null;
                intake.SourceRevisions.Add(new SourceRevisionRecord { Key = fact.Key, SourceSha256 = fact.SourceSha256,
                    SelectionManifestSha256 = fact.SelectionManifestSha256, Review = new ToolReviewProof() });
            }
            Write(output, "PRIVATE-intake-UNFILLED.json", Json(intake));
            var materials = new List<HolderMaterialDefinition>(); var mappings = new List<ToolHolderMappingRecord>();
            var pairs = new List<ToolPairSeatingRecord>(); var datums = new List<ToolDatumRecord>(); var axial = new List<object>();
            foreach (var t in library.PlacementSources.Tools)
            {
                var m = manifest?.Tools?.SingleOrDefault(s => s.Id == t.Id);
                materials.Add(new HolderMaterialDefinition { ToolId = t.Id, BindingSha256 = t.HolderObstacles?.BindingSha256,
                    Contours = new List<HolderContourDefinition>(), Excluded = new List<HolderExcludedEntity>(), Review = new ToolReviewProof() });
                mappings.Add(new ToolHolderMappingRecord { ToolId = t.Id, BindingSha256 = t.HolderObstacles?.BindingSha256, LabelHandle = m?.LabelHandle, ExpectedLabel = m?.ExpectedLabel,
                    PunchRootInsert = m?.PunchRootInsert, PunchBlock = m?.PunchBlock, HolderRootInsert = m?.HolderRootInsert, HolderBlock = m?.HolderBlock,
                    HolderEntityKeys = t.Holder?.EntityKeys.ToArray(), Review = new ToolReviewProof() });
                pairs.Add(new ToolPairSeatingRecord { ToolId = t.Id, MasterSourceSha256 = t.MasterSha256, MasterManifestSha256 = t.ManifestSha256,
                    PunchGeometrySha256 = t.Punch?.GeometrySha256, HolderSelectionSha256 = t.Holder?.GeometrySha256, PunchSourceFrameId = t.Punch?.FrameId, HolderSourceFrameId = t.Holder?.FrameId,
                    HolderSourceToPunchSource = new PairTransformRecord(), TipPointMm = new PairPointRecord(), PunchMountDatumMm = new PairPointRecord(), HolderMountDatumMm = new PairPointRecord(),
                    WorkingDirection = new PairPointRecord(), ApproachDirection = new PairPointRecord(), ContactReferenceMm = new PairPointRecord(), Review = new ToolReviewProof() });
                datums.Add(new ToolDatumRecord { Id = t.Id, SourceSha256 = t.MasterSha256, GeometrySha256 = t.Punch?.GeometrySha256,
                    TipPointMm = new ToolPointDatumRecord { Evidence = new ToolDatumApproval() }, MountDatumMm = new ToolPointDatumRecord { Evidence = new ToolDatumApproval() },
                    WorkingDirection = new ToolDirectionDatumRecord { Evidence = new ToolDatumApproval() }, LocalCoordinateFrame = new ToolFrameDatumRecord {
                        OriginMm = new ToolVectorRecord(), XAxis = new ToolVectorRecord(), YAxis = new ToolVectorRecord(), Evidence = new ToolDatumApproval() } });
                foreach (CollisionPairKind pair in Enum.GetValues(typeof(CollisionPairKind))) axial.Add(new {
                    ToolId = t.Id, Pair = pair.ToString(), FeatureId = (int?)null, ModelSnapshotId = (string)null, ModelUpdateStamp = (int?)null, Configuration = (string)null,
                    EnvelopeGeometrySha256 = (string)null, FrameId = (string)null, MasterSourceSha256 = t.MasterSha256,
                    ProfileGeometrySha256 = pair == CollisionPairKind.WorkpiecePunch ? t.Punch?.GeometrySha256 : t.HolderObstacles?.DefinitionSha256,
                    SeatingPayloadSha256 = (string)null, AxialSectionContract = (string)null, ToolAxialOriginMm = (double?)null, ToolAxisSign = (int?)null, AxialUncertaintyMm = (double?)null,
                    Segments = new[] { new { LocalStartMm = (double?)null, LocalEndMm = (double?)null } }, ContactPointMm = new PairPointRecord(), ApproachDirection = new PairPointRecord(),
                    MirrorApproved = (bool?)null, Review = new ToolReviewProof() });
            }
            Write(output, "PRIVATE-holder-review-UNFILLED.json", Json(new HolderEvidenceDocument { Version = 1, Materials = materials, Mappings = mappings }));
            Write(output, "PRIVATE-seating-review-UNFILLED.json", Json(new PairSeatingDocument { Version = 1, Pairs = pairs }));
            Write(output, "PRIVATE-master-datums-UNFILLED.json", Json(new ToolDatumEvidenceFile { Version = 1, Tools = datums }));
            Write(output, "PRIVATE-axial-measurements-UNFILLED.json", Json(new { Version = 1, NotConfigured = true, AxialSetups = axial }));
            Write(output, "PRIVATE-limit-applicability-UNFILLED.json", Json(new { Version = 1, NotConfigured = true,
                LimitApplications = CompanyToolIds.All.Select(id => new LimitApplicationRecord { ToolId = id,
                    WorkbookSha256 = facts.SingleOrDefault(f => f.Key == "Workbook")?.SourceSha256, Review = new ToolReviewProof() }).ToArray() }));
            Write(output, "PRIVATE-authorities-UNFILLED.json", Json(new ToolReviewAuthorities { Version = 1, Keys = new List<ToolReviewAuthority>() }));
            Write(output, "PRIVATE-selection-inventory.json", Json(library.PlacementSources.Tools.Select(t => new { t.Id, t.MasterSha256, t.ManifestSha256,
                Selection = manifest?.Tools?.SingleOrDefault(s => s.Id == t.Id), PunchKeys = t.Punch?.EntityKeys, HolderKeys = t.Holder?.EntityKeys,
                HolderInventory = t.HolderObstacles?.Inventory.Select(e => new { e.EntityKey, e.SourceType, e.SourceLineType, e.Layer, e.SourceClosed, e.Supported, e.Reason, e.ErrorBoundMm }),
                Topology = t.HolderObstacles?.Topology, MaterialRoles = "UNREVIEWED; topology is not material classification" }).ToArray()));
        }
    }
}
