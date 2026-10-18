using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;
using ADDIN.Tools.BendToolAudit;

internal static partial class ToolProfileTests
{
    // All approvals, machine/setup labels, measurements and source files here
    // are controlled software fixtures. No company inspection is asserted.
    private sealed class AuditFixture : IDisposable
    {
        internal SeatingFixture Pair;
        internal ToolProfileLibrarySnapshot Library;
        internal AuditIntake Intake;
        internal AuditSourceFact[] Sources;
        internal AuditModelContext Context;
        internal IReadOnlyList<ToolAuditRow> Rows(bool model = true) => ToolDataAudit.Evaluate(Library, Intake, Sources,
            Pair.Reviews.Verifier, Pair.Reviews.Root, model ? Context : null);
        internal ToolAuditRow Row(bool model = true) => Rows(model).Single(r => r.ToolId == Pair.Assembly.Id);
        public void Dispose() => Pair.Dispose();
    }
    private static AuditFixture AuditSetup(bool simulation = false)
    {
        var f = new AuditFixture { Pair = Seating(simulation, !simulation) }; var p = f.Pair; var a = p.Assembly;
        p.Reviews.Authority.Purposes = p.Reviews.Authority.Purposes.Concat(new[] { "ToolSourceRevision", "ToolLimitApplicability", "OfflineModelCapture", "PunchAxialSetup", "HolderAxialSetup" }).ToArray();
        p.Reviews.Reload();
        var dim = new CompanyToolProfile { Id = a.Id, Parts = new[] { new ToolGeometryPart { Role = ToolGeometryRole.CompanyPunchProfile,
            Source = new ToolProfileProvenance { Sha256 = a.MasterSha256 }, Entities = a.Punch.SourceEntities } },
            Validation = new ToolProfileValidationResult { PunchGeometryReadComplete = true, PunchClosed = true, PunchEntitiesSupported = true, Region = a.Punch.Region } };
        var limit = new BendLimitRuleDraft("SYNTHETIC-LIMIT", "SYNTHETIC-FAMILY", BendLimitKind.MaxBendLength, new string('a', 64), "TEST", "cell:A1",
            new string('b', 64), "mm", 10, new string[0], new[] { "UNVERIFIED_TOOL_FAMILY_MAPPING" });
        f.Library = new ToolProfileLibrarySnapshot { Tools = CompanyToolIds.All.Select(id => id == a.Id ? dim : new CompanyToolProfile {
            Id = id, Parts = new ToolGeometryPart[0], Validation = new ToolProfileValidationResult() }).ToArray(),
            PlacementSources = new PlacementSourceSnapshot("SYNTHETIC_AUDIT", new[] { a }, new[] { limit }, new string[0]) };
        f.Intake = new AuditIntake { MachineId = "SYNTHETIC-MACHINE", SetupId = "SYNTHETIC-SETUP" };
        f.Sources = new[] { new AuditSourceFact { Key = "Master", SourceSha256 = a.MasterSha256, SelectionManifestSha256 = a.ManifestSha256 },
            new AuditSourceFact { Key = "Workbook", SourceSha256 = limit.WorkbookSha256, SelectionManifestSha256 = new string('c', 64) },
            new AuditSourceFact { Key = "DIM:" + a.Id, SourceSha256 = a.MasterSha256, SelectionManifestSha256 = new string('d', 64) } };
        foreach (var fact in f.Sources)
        {
            f.Intake.ExpectedRevisions[fact.Key] = "SYNTHETIC-REQUESTED-REVISION-(2)";
            var r = new SourceRevisionRecord { Key = fact.Key, RequestedRevision = f.Intake.ExpectedRevisions[fact.Key],
                SourceSha256 = fact.SourceSha256, SelectionManifestSha256 = fact.SelectionManifestSha256 };
            r.Review = p.Reviews.Sign("ToolSourceRevision", r.Payload(), simulation); f.Intake.SourceRevisions.Add(r);
        }
        var application = new LimitApplicationRecord { ToolId = a.Id, RuleId = limit.Id, WorkbookSha256 = limit.WorkbookSha256,
            ObjectSha256 = limit.ObjectSha256, Kind = limit.Kind.ToString(), Unit = limit.Unit, ValueMm = limit.ValueMm,
            MachineId = f.Intake.MachineId, SetupId = f.Intake.SetupId, SeatingPayloadSha256 = a.SeatingEvidence.PayloadSha256 };
        application.Review = p.Reviews.Sign("ToolLimitApplicability", application.Payload(), simulation); f.Intake.LimitApplications.Add(application);
        var model = CollisionModel(Rect("SYNTHETIC-SECTION", 0, 0, 1, 1), 1, production: !simulation);
        var capture = Encoding.UTF8.GetBytes(Json.Serialize(model));
        f.Intake.ModelCapturePath = "capture.json"; f.Intake.ModelSourcePath = "source.fixture";
        File.WriteAllBytes(Path.Combine(p.Reviews.Root, f.Intake.ModelCapturePath), capture);
        File.WriteAllText(Path.Combine(p.Reviews.Root, f.Intake.ModelSourcePath), "CONTROLLED SOFTWARE SOURCE - NOT COMPANY CAD");
        f.Intake.ModelSourceSha256 = PlacementSourceIo.Hash(File.ReadAllBytes(Path.Combine(p.Reviews.Root, f.Intake.ModelSourcePath)));
        f.Intake.ModelCaptureReview = p.Reviews.Sign("OfflineModelCapture", OfflineModelReader.Payload(capture, f.Intake.ModelSourceSha256, f.Intake.MachineId, f.Intake.SetupId), simulation);
        f.Context = OfflineModelReader.Read(f.Intake, p.Reviews.Root, p.Reviews.Verifier, CancellationToken.None);
        if (f.Context.Model == null) throw new Exception("Fixture capture did not decode: " + f.Context.Diagnostic);
        foreach (CollisionPairKind pair in Enum.GetValues(typeof(CollisionPairKind)))
        {
            var r = AxialRecord(p, f.Context.Envelopes[0], pair, P(21, 22));
            r.Review = p.Reviews.Sign(pair == CollisionPairKind.WorkpiecePunch ? "PunchAxialSetup" : "HolderAxialSetup", ToolAxialSetupEvidence.Payload(r), simulation);
            f.Intake.AxialSetups.Add(r);
        }
        return f;
    }
    private static void AuditTests()
    {
        Test("audit: missing input preserves exact 18 IDs, all independent blockers and four mapping followups", () => {
            var library = new ToolProfileLibraryProvider(() => new ToolProfileSourceConfiguration { DirectoryPath = Folder() }).Read(CancellationToken.None);
            var rows = ToolDataAudit.Evaluate(library, new AuditIntake(), new AuditSourceFact[0], ToolReviewVerifier.Load(null, CancellationToken.None), temporary);
            Check(rows.Select(r => r.ToolId).SequenceEqual(CompanyToolIds.All), "Exact order and 18 slots");
            Check(rows.Count(r => r.SeparateMappingFollowup) == 4 && rows.Where(r => r.SeparateMappingFollowup).Select(r => r.ToolId).SequenceEqual(ToolDataAudit.SeparateMappings), "Four exact mappings");
            foreach (var row in rows)
            {
                Check(row.Readiness == "UNKNOWN" && row.PhysicalPair == "UNKNOWN" && row.PunchAxial == "UNKNOWN" && row.HolderAxial == "UNKNOWN", "NULL never becomes PASS");
                foreach (var code in new[] { "SOURCE_REVISION_UNVERIFIED", "MAPPING_UNRESOLVED", "MATERIAL_RULE_UNREVIEWED", "DATUM_UNVERIFIED", "PHYSICAL_PAIR_UNVERIFIED", "XY_WORK_POSE_UNVERIFIED", "AXIAL_LENGTH_UNVERIFIED", "LIMIT_RULE_UNMAPPED" })
                    Check(row.ReasonCodes.Contains(code), "Independent missing issue " + code);
                Check(row.Issues.All(i => !string.IsNullOrWhiteSpace(i.Field) && !string.IsNullOrWhiteSpace(i.RequiredEvidence)), "Actionable field paths and evidence requirements");
            }
        });
        Test("audit: fully fulfilled external contracts can reach saved-context static readiness", () => {
            using (var f = AuditSetup())
            {
                var row = f.Row(); Check(row.Readiness == "READY_PRODUCTION_STATIC_CHECK", "Only complete synthetic external contract fixture becomes ready: " + string.Join(";", row.Issues.Select(i => i.Code + ":" + i.Diagnostic)));
                Check(row.Issues.Count == 0 && row.PhysicalPair == "ATTESTED" && row.PunchAxial == "ATTESTED" && row.HolderAxial == "ATTESTED", "No missing evidence hidden");
                Check(f.Rows().Count == 18 && f.Rows().Skip(1).All(r => r.Readiness == "UNKNOWN"), "One ready fixture never approves other slots");
            }
        });
        Test("audit: simulation reviews allow geometry inspection but never production readiness", () => {
            using (var f = AuditSetup(true))
            {
                var row = f.Row(); Check(row.Readiness == "READY_GEOMETRY_ONLY" && row.PhysicalPair == "UNKNOWN", "Simulation cannot attest real pairing");
                Check(row.ReasonCodes.Contains("PHYSICAL_PAIR_UNVERIFIED") && row.ReasonCodes.Contains("SOURCE_REVISION_UNVERIFIED"), "External revision and physical requirements remain visible");
            }
        });
        Test("audit: a verified pair without an exact model still has unknown pose and independent finite lengths", () => {
            using (var f = AuditSetup())
            {
                var row = f.Row(false); Check(row.Readiness == "READY_GEOMETRY_ONLY", "No context means geometry only");
                Check(row.PunchAxial == "UNKNOWN" && row.HolderAxial == "UNKNOWN" && row.XYWorkPose == "UNKNOWN", "No extrapolation from paired source datums");
            }
        });
        Test("audit: a missing workbook review does not erase independently reviewed tool geometry", () => {
            using (var f = AuditSetup())
            {
                f.Intake.SourceRevisions.RemoveAll(s => s.Key == "Workbook");
                var row = f.Row(false);
                Check(row.Readiness == "READY_GEOMETRY_ONLY" && row.SourceRevision == "UNVERIFIED", "Workbook approval is a separate production blocker");
            }
        });
        Test("audit: source hash, requested revision and selection manifest must match separately", () => {
            using (var f = AuditSetup())
            {
                var source = f.Intake.SourceRevisions[0]; string sha = source.SourceSha256, selection = source.SelectionManifestSha256, revision = source.RequestedRevision;
                foreach (int mode in Enumerable.Range(0, 3))
                {
                    source.SourceSha256 = mode == 0 ? new string('e', 64) : sha;
                    source.SelectionManifestSha256 = mode == 1 ? new string('e', 64) : selection;
                    source.RequestedRevision = mode == 2 ? "SYNTHETIC-WRONG-(3)" : revision;
                    source.Review = f.Pair.Reviews.Sign("ToolSourceRevision", source.Payload(), false);
                    Check(f.Row().Readiness == "UNKNOWN" && f.Row().Issues.Any(i => i.Diagnostic == "SOURCE_OR_SELECTION_REVISION_MISMATCH"), "A valid signature cannot override source/request/selection mismatch");
                }
            }
        });
        Test("audit: source signatures, signer purposes, artifact bytes and expiry are checked", () => {
            using (var f = AuditSetup())
            {
                var record = f.Intake.SourceRevisions[0]; var proof = record.Review;
                record.Review = f.Pair.Reviews.Sign("ToolLimitApplicability", record.Payload(), false);
                Check(f.Row().Readiness == "UNKNOWN", "Wrong signing purpose rejected");
                record.Review = proof; string signature = proof.SignatureBase64;
                proof.SignatureBase64 = Convert.ToBase64String(new byte[256]); Check(f.Row().Readiness == "UNKNOWN", "Invalid RSA signature rejected");
                proof.SignatureBase64 = signature;
                File.AppendAllText(Path.Combine(f.Pair.Reviews.Root, "review-artifact.txt"), " edited");
                Check(f.Row().Readiness == "UNKNOWN", "Artifact-only mutation is stale");
            }
            using (var f = AuditSetup()) { f.Pair.Reviews.Now = f.Pair.Reviews.Now.AddDays(2); Check(f.Row().Readiness == "UNKNOWN", "Expired reviews cannot retain readiness"); }
        });
        Test("audit: missing holder length never inherits punch length and mismatched XY poses fail", () => {
            using (var f = AuditSetup())
            {
                f.Intake.AxialSetups.RemoveAt(1); var row = f.Row();
                Check(row.PunchAxial == "ATTESTED" && row.HolderAxial == "UNKNOWN" && row.Readiness != "READY_PRODUCTION_STATIC_CHECK", "Independent finite occupancy");
            }
            using (var f = AuditSetup())
            {
                var record = f.Intake.AxialSetups[1]; record.ContactPointMm.X += 1;
                record.Review = f.Pair.Reviews.Sign("HolderAxialSetup", ToolAxialSetupEvidence.Payload(record), false);
                Check(f.Row().XYWorkPose == "UNKNOWN" && f.Row().Readiness != "READY_PRODUCTION_STATIC_CHECK", "Different signed poses are not one installed pair");
            }
        });
        Test("audit: null axial measurement, stale profile/frame and duplicate setup fail closed", () => {
            foreach (int mode in Enumerable.Range(0, 4)) using (var f = AuditSetup())
            {
                var record = f.Intake.AxialSetups[0];
                if (mode == 0) record.Segments[0].LocalEndMm = null;
                if (mode == 1) record.ProfileGeometrySha256 = new string('f', 64);
                if (mode == 2) record.FrameId = "WRONG_FRAME";
                if (mode == 3) f.Intake.AxialSetups.Add(record);
                if (mode != 0) record.Review = f.Pair.Reviews.Sign("PunchAxialSetup", ToolAxialSetupEvidence.Payload(record), false);
                Check(f.Row().Readiness != "READY_PRODUCTION_STATIC_CHECK" && f.Row().PunchAxial == "UNKNOWN", "Rejected malformed/source/duplicate axial record " + mode);
            }
        });
        Test("audit: source workbook values and per-machine applicability stay distinct", () => {
            using (var f = AuditSetup())
            {
                f.Intake.LimitApplications.Clear(); Check(f.Row().ReasonCodes.Contains("LIMIT_RULE_UNMAPPED"), "Raw workbook value cannot approve an ID");
                Check(f.Row().ReferenceLimits.Count == 1, "Source reference remains inspectable locally");
            }
            foreach (int mode in Enumerable.Range(0, 5)) using (var f = AuditSetup())
            {
                var record = f.Intake.LimitApplications[0];
                if (mode == 0) record.WorkbookSha256 = new string('e', 64);
                if (mode == 1) record.ObjectSha256 = new string('e', 64);
                if (mode == 2) record.ValueMm = 999;
                if (mode == 3) record.MachineId = "WRONG_MACHINE";
                if (mode == 4) record.SeatingPayloadSha256 = new string('e', 64);
                record.Review = f.Pair.Reviews.Sign("ToolLimitApplicability", record.Payload(), false);
                Check(f.Row().Readiness != "READY_PRODUCTION_STATIC_CHECK" && f.Row().ReasonCodes.Contains("LIMIT_RULE_UNMAPPED"), "Signed but mismatched limit " + mode);
            }
        });
        Test("audit: deep-bend examples cannot be promoted to machine limits by a signature", () => {
            using (var f = AuditSetup())
            {
                var r = f.Intake.LimitApplications[0]; var example = new BendLimitRuleDraft(r.RuleId, "SYNTHETIC", BendLimitKind.DeepBendClearanceExample,
                    r.WorkbookSha256, "TEST", "cell:A1", r.ObjectSha256, "mm", r.ValueMm, new[] { r.ToolId }, new[] { "UNVERIFIED_TOOL_FAMILY_MAPPING", "REFERENCE_EXAMPLE_ONLY" });
                r.Kind = example.Kind.ToString(); r.Review = f.Pair.Reviews.Sign("ToolLimitApplicability", r.Payload(), false);
                var review = AuditAttestations.Limit(r, example, f.Pair.Assembly, f.Intake, f.Pair.Reviews.Verifier, f.Pair.Reviews.Root, CancellationToken.None);
                Check(!review.RecordVerified && review.Reason == "REFERENCE_EXAMPLE_ONLY", "No guessed machine default");
            }
        });
        Test("audit: saved capture is recompiled and source-file changes invalidate its attestation", () => {
            using (var f = AuditSetup())
            {
                Check(f.Context.Envelopes[0].Certificate.ContinuousCoverageProven && f.Context.CaptureReview.RecordVerified, "Full DTO rehydration and original compiler");
                File.AppendAllText(Path.Combine(f.Pair.Reviews.Root, f.Intake.ModelSourcePath), " edited");
                var context = OfflineModelReader.Read(f.Intake, f.Pair.Reviews.Root, f.Pair.Reviews.Verifier, CancellationToken.None);
                Check(context.Model == null && context.Diagnostic == "OFFLINE_MODEL_SOURCE_SHA_MISMATCH", "Saved Part hash is required");
            }
        });
        Test("audit: source/model leases and held authority expiry remove readiness", () => {
            using (var f = AuditSetup()) { f.Library.Invalidate(); Check(f.Row().Readiness == "UNKNOWN", "Stale library lease"); }
            using (var f = AuditSetup()) { f.Context.Model.Lease.Invalidate(); Check(f.Row().Readiness != "READY_PRODUCTION_STATIC_CHECK", "Stale model lease"); }
        });
        Test("audit: minimized private summary excludes paths, hashes, artifacts, coordinates and free-text diagnostics", () => {
            using (var f = AuditSetup())
            {
                var row = f.Row(); const string secret = "PRIVATE_CANARY_12345";
                row.PrivateSelection = new { Coordinates = secret, SourcePath = secret, Reviewer = secret };
                row.Issues.Add(new AuditIssue { Code = "DATUM_UNVERIFIED", Field = secret, Diagnostic = secret, RequiredEvidence = secret });
                string json = AuditExport.Json(AuditExport.SummaryRow(row)), md = AuditExport.SummaryMarkdown(new[] { row });
                Check(!json.Contains(secret) && !md.Contains(secret) && !json.Contains(f.Pair.Assembly.MasterSha256), "Minimized private summary drops detailed and free-text fields");
            }
        });
        Test("audit: intake rejects unknown IDs/duplicates; ledger detects artifact-only mutations", () => {
            var intake = new AuditIntake { SourceRevisions = new List<SourceRevisionRecord> { new SourceRevisionRecord { Key = "Master" }, new SourceRevisionRecord { Key = "Master" } } };
            bool rejected = false; try { ADDIN.Tools.BendToolAudit.Program.ValidateIntake(intake); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Duplicate source identity rejected");
            string root = Folder(); Save(root, "artifact.txt", "before"); Save(root, "packet.json", "{\"Review\":{\"ArtifactPath\":\"artifact.txt\"}}");
            var ledger = new AuditFileLedger(); ledger.JsonArtifacts(Path.Combine(root, "packet.json")); Check(ledger.Current, "Initial input ledger");
            Save(root, "artifact.txt", "after"); Check(!ledger.Current, "Artifact bytes are included in source freshness");
        });
        Test("audit: private output refuses checkouts and overwrites; templates never invent measurements or approvals", () => {
            string root = Folder(); Directory.CreateDirectory(Path.Combine(root, ".git")); Save(Path.Combine(root, ".git"), "HEAD", "ref: refs/heads/synthetic");
            bool rejected = false; try { ADDIN.Tools.BendToolAudit.Program.PrivateOutput(Path.Combine(root, "report"), new string[0]); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Private outputs cannot be written inside a Git checkout");
            using (var f = AuditSetup())
            {
                string output = Folder(); AuditExport.Templates(output, f.Library, f.Sources, f.Pair.Master.Mapping);
                string axial = File.ReadAllText(Path.Combine(output, "PRIVATE-axial-measurements-UNFILLED.json"));
                string seating = File.ReadAllText(Path.Combine(output, "PRIVATE-seating-review-UNFILLED.json"));
                Check(axial.Contains("\"LocalEndMm\":null") && axial.Contains("\"FeatureId\":null") && axial.Contains("\"ToolAxialOriginMm\":null"), "Measurements and context are NULL, never invented zero values");
                Check(seating.Contains("\"SignatureBase64\":null") && seating.Contains("\"ReviewerId\":null") && seating.Contains("\"PhysicalInspectionConfirmed\":false"), "No signer, review date, physical claim or key generated");
                rejected = false; try { ADDIN.Tools.BendToolAudit.Program.PrivateOutput(output, new string[0]); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Existing completed packets cannot be overwritten");
            }
        });
    }
}
