using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Tools.BendToolAudit
{
    // Additional intake attestations use the existing authority, artifact,
    // canonical hashing, expiry and purpose-specific signature verifier.
    // There is deliberately no signing/key-generation operation in this CLI.
    internal sealed class SourceRevisionRecord
    {
        public string Key { get; set; }
        public string RequestedRevision { get; set; }
        public string SourceSha256 { get; set; }
        public string SelectionManifestSha256 { get; set; }
        public ToolReviewProof Review { get; set; }
        internal string Payload() => ToolEvidenceCanonical.Hash("ToolSourceRevisionV1", Key,
            RequestedRevision, SourceSha256, SelectionManifestSha256);
    }
    internal sealed class LimitApplicationRecord
    {
        public string ToolId { get; set; }
        public string RuleId { get; set; }
        public string WorkbookSha256 { get; set; }
        public string ObjectSha256 { get; set; }
        public string Kind { get; set; }
        public string Unit { get; set; }
        public double? ValueMm { get; set; }
        public string MachineId { get; set; }
        public string SetupId { get; set; }
        public string SeatingPayloadSha256 { get; set; }
        public ToolReviewProof Review { get; set; }
        internal string Payload() => ToolEvidenceCanonical.Hash("ToolLimitApplicabilityV1", ToolId, RuleId,
            WorkbookSha256, ObjectSha256, Kind, Unit, ValueMm.HasValue ? ToolEvidenceCanonical.Number(ValueMm.Value) : null,
            MachineId, SetupId, SeatingPayloadSha256);
    }
    internal sealed class AuditIntake
    {
        public int Version { get; set; } = 1;
        public string MachineId { get; set; }
        public string SetupId { get; set; }
        // Supplied by the operator from the actual revision request. Never
        // populated from a basename, suffix, mtime or the observed source hash.
        public Dictionary<string, string> ExpectedRevisions { get; set; } = new Dictionary<string, string>();
        public List<SourceRevisionRecord> SourceRevisions { get; set; } = new List<SourceRevisionRecord>();
        public List<LimitApplicationRecord> LimitApplications { get; set; } = new List<LimitApplicationRecord>();
        public List<ADDIN.Commands.BendEnvelope.ToolAxialSetupRecord> AxialSetups { get; set; } = new List<ADDIN.Commands.BendEnvelope.ToolAxialSetupRecord>();
        public string ModelCapturePath { get; set; }
        public string ModelSourcePath { get; set; }
        public string ModelSourceSha256 { get; set; }
        public ToolReviewProof ModelCaptureReview { get; set; }
    }
    internal sealed class AuditSourceFact
    {
        public string Key { get; set; }
        public string Path { get; set; }
        public string SourceSha256 { get; set; }
        public string SelectionManifestSha256 { get; set; }
    }
    internal sealed class AuditIssue
    {
        public string Code { get; set; }
        public string Field { get; set; }
        public string Diagnostic { get; set; }
        public string RequiredEvidence { get; set; }
    }
    internal static class AuditAttestations
    {
        internal static ToolReviewValidation Revision(AuditSourceFact fact, AuditIntake intake,
            ToolReviewVerifier verifier, string root, CancellationToken token)
        {
            if (fact == null) return ToolReviewValidation.Missing("SOURCE_NOT_OBSERVED");
            string expected;
            if (intake.ExpectedRevisions == null || !intake.ExpectedRevisions.TryGetValue(fact.Key, out expected) || string.IsNullOrWhiteSpace(expected))
                return ToolReviewValidation.Missing("REQUESTED_REVISION_NOT_SPECIFIED");
            var records = (intake.SourceRevisions ?? new List<SourceRevisionRecord>()).Where(record => record?.Key == fact.Key).ToArray();
            if (records.Length != 1) return ToolReviewValidation.Missing(records.Length == 0 ? "MISSING_SOURCE_REVISION_REVIEW" : "DUPLICATE_SOURCE_REVISION");
            var r = records[0];
            if (!ToolDatumEvidence.IsSha256(fact.SourceSha256) || !ToolDatumEvidence.IsSha256(fact.SelectionManifestSha256) ||
                r.RequestedRevision != expected || r.SourceSha256 != fact.SourceSha256 || r.SelectionManifestSha256 != fact.SelectionManifestSha256)
                return new ToolReviewValidation(ToolReviewStatus.Stale, "SOURCE_OR_SELECTION_REVISION_MISMATCH");
            return verifier.Verify("ToolSourceRevision", r.Payload(), r.Review, root, token);
        }
        internal static ToolReviewValidation Limit(LimitApplicationRecord r, BendLimitRuleDraft source,
            ToolAssemblyProfile tool, AuditIntake intake, ToolReviewVerifier verifier, string root, CancellationToken token)
        {
            if (r == null || source == null || tool == null) return ToolReviewValidation.Missing("LIMIT_RULE_NOT_FOUND");
            // A drawing's clearance example cannot become a universal machine
            // limit merely by signing it. An engineering contract is needed.
            if (source.Kind != BendLimitKind.MaxBendLength)
                return new ToolReviewValidation(ToolReviewStatus.Invalid, "REFERENCE_EXAMPLE_ONLY");
            if (string.IsNullOrWhiteSpace(intake.MachineId) || string.IsNullOrWhiteSpace(intake.SetupId) ||
                r.ToolId != tool.Id || r.MachineId != intake.MachineId || r.SetupId != intake.SetupId ||
                r.SeatingPayloadSha256 != tool.SeatingEvidence.PayloadSha256 || !ToolDatumEvidence.IsSha256(r.SeatingPayloadSha256) ||
                r.RuleId != source.Id || r.WorkbookSha256 != source.WorkbookSha256 || r.ObjectSha256 != source.ObjectSha256 ||
                r.Kind != source.Kind.ToString() || r.Unit != source.Unit || !source.ValueMm.HasValue || !r.ValueMm.HasValue || r.ValueMm != source.ValueMm ||
                source.Reasons.Any(x => x != "UNVERIFIED_TOOL_FAMILY_MAPPING"))
                return new ToolReviewValidation(ToolReviewStatus.Stale, "LIMIT_SOURCE_VALUE_SCOPE_OR_SETUP_MISMATCH");
            return verifier.Verify("ToolLimitApplicability", r.Payload(), r.Review, root, token);
        }
    }
}
