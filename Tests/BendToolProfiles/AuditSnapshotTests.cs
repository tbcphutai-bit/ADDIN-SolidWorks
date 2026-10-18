using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;
using ADDIN.Tools.BendToolAudit;

internal static partial class ToolProfileTests
{
    private static Dictionary<string, object> CaptureObject(object value) => (Dictionary<string, object>)value;
    private static Dictionary<string, object> CaptureMap(PlacementModelCapture model) => CaptureObject(Json.DeserializeObject(Json.Serialize(model)));
    private static Dictionary<string, object> CaptureBody(Dictionary<string, object> model) => CaptureObject(((object[])model["Bodies"])[0]);
    private static Dictionary<string, object> CaptureShell(Dictionary<string, object> model) => CaptureObject(CaptureBody(model)["EnvelopeTopology"]);
    private static byte[] CaptureBytes(object value) => Encoding.UTF8.GetBytes(Json.Serialize(value));
    private static void CaptureRejected(Dictionary<string, object> model, string reason)
    {
        bool rejected = false;
        try { OfflineModelReader.Decode(CaptureBytes(model)); }
        catch (InvalidDataException ex) { rejected = ex.Message.StartsWith(reason, StringComparison.Ordinal); }
        Check(rejected, "Capture must fail closed with " + reason);
    }
    private static PlacementModelCapture SnapshotRoundtrip(PlacementModelCapture original)
    {
        var decoded = OfflineModelReader.Decode(CaptureBytes(original));
        Check(decoded.SnapshotId == original.SnapshotId && decoded.Configuration == original.Configuration && decoded.UpdateStamp == original.UpdateStamp, "Exact snapshot identity");
        foreach (var body in original.Bodies)
        {
            var actual = decoded.Bodies.Single(b => b.Id == body.Id).EnvelopeTopology;
            Check(actual.SourceSha256 == body.EnvelopeTopology.SourceSha256, "Exact canonical V2 hash, including source circle order");
            Check(actual.Circles.Count == body.EnvelopeTopology.Circles.Count, "Every source circle retained");
            for (int i = 0; i < actual.Circles.Count; i++)
            {
                var a = actual.Circles[i]; var b = body.EnvelopeTopology.Circles[i];
                Check(a.EdgeId == b.EdgeId && (a.Axis - b.Axis).Length == 0 && a.CurveAndEdgeSameSense == b.CurveAndEdgeSameSense, "Exact edge, axis and sense; no inferred plane/default true sense");
            }
        }
        var sheet = CompileNative(decoded);
        Check(sheet.Certificate.ContinuousCoverageProven && sheet.Certificate.Tier == EnvelopeCoverageTier.AxialSlicesWithCertifiedCoverage, "Restored native circle family is continuously certified");
        Check(sheet.GeometrySha256 == CompileNative(original).GeometrySha256, "Envelope/tool evidence binding unchanged by roundtrip");
        return decoded;
    }
    private static void AuditSnapshotTests(bool roundtripOnly = false)
    {
        Test("offline V2: full-circle bore roundtrips exact axis/sense/hash and certified coverage", () => SnapshotRoundtrip(NativeBoreModel()));
        Test("offline V2: transverse quarter bend roundtrips both circle senses and certified coverage", () => SnapshotRoundtrip(NativeQuarterBendModel()));
        if (roundtripOnly) return;
        Test("offline V2: audit is x64 managed-only and preserves small binary64 source tokens", () => {
            Check(Environment.Is64BitProcess, "Canonical native snapshots use the same x64 runtime as SOLIDWORKS");
            Check(!typeof(OfflineModelReader).Assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase)), "No STA/COM adapter or interop reference in offline executable");
            double tiny = 7.654321098765432e-16;
            SnapshotRoundtrip(NativeBoreModel(new BendWorkFrame("SYNTHETIC_SECTION", new PlacementVector3(),
                new PlacementVector3(1, tiny, 0), new PlacementVector3(-tiny, 1, 0), new PlacementVector3(0, 0, 1))));
        });
        Test("offline V2: equivalent reversed circle parameters and rigid frames retain exact source metadata", () => {
            var model = NativeBoreModel(); var s = model.Bodies[0].EnvelopeTopology;
            var reversed = new NativeEnvelopeBody(s.BodyId, s.SolidBody, s.NativeFaceCount, s.NativeEdgeCount, s.Edges, s.Faces,
                circles: s.Circles.Select(c => new NativeCircleDefinition(c.EdgeId, c.Axis * -1, !c.CurveAndEdgeSameSense)));
            SnapshotRoundtrip(NativeFixtureModel(reversed, model.Bends[0].Frame));
            double angle = .431; var x = new PlacementVector3(Math.Cos(angle), Math.Sin(angle), 0);
            SnapshotRoundtrip(NativeBoreModel(new BendWorkFrame("SYNTHETIC_SECTION", new PlacementVector3(51, -37, 12),
                x, new PlacementVector3(-Math.Sin(angle), Math.Cos(angle), 0), new PlacementVector3(0, 0, 1))));
        });
        Test("offline V2: absent/null circle metadata cannot be inferred from circular endpoints", () => {
            foreach (int mode in Enumerable.Range(0, 3))
            {
                var map = CaptureMap(NativeBoreModel()); var shell = CaptureShell(map);
                if (mode == 0) shell.Remove("Circles"); else shell["Circles"] = mode == 1 ? null : new object[0];
                CaptureRejected(map, mode == 2 ? "CAPTURE_NATIVE_CIRCLE_METADATA_INCOMPLETE" : "CAPTURE_NATIVE_CIRCLE_METADATA_MISSING");
            }
        });
        Test("offline V2: axis, sense and circle identity are required typed source fields", () => {
            foreach (string field in new[] { "Axis", "CurveAndEdgeSameSense", "EdgeId" })
            {
                var map = CaptureMap(NativeBoreModel()); var c = CaptureObject(((object[])CaptureShell(map)["Circles"])[0]); c.Remove(field);
                CaptureRejected(map, "MISSING_CAPTURE_FIELD:");
            }
            foreach (double axis in new[] { 0.0, 2.0 })
            {
                var map = CaptureMap(NativeBoreModel()); var c = CaptureObject(((object[])CaptureShell(map)["Circles"])[0]);
                c["Axis"] = new { X = axis, Y = 0, Z = 0 }; CaptureRejected(map, "CAPTURE_NATIVE_CIRCLE_AXIS_INVALID");
            }
        });
        Test("offline V2: duplicate, unknown or linear-edge circle bindings are rejected", () => {
            foreach (int mode in Enumerable.Range(0, 3))
            {
                var map = CaptureMap(NativeBoreModel()); var shell = CaptureShell(map); var circles = (object[])shell["Circles"];
                if (mode == 0) shell["Circles"] = circles.Concat(new[] { circles[0] }).ToArray();
                else CaptureObject(circles[0])["EdgeId"] = mode == 1 ? 987654 : 0;
                CaptureRejected(map, "CAPTURE_NATIVE_CIRCLE_EDGE_BINDING_INVALID");
            }
        });
        Test("offline V2: sense/axis/geometry tampering cannot retain the original native hash", () => {
            foreach (int mode in Enumerable.Range(0, 4))
            {
                var map = CaptureMap(NativeBoreModel()); var shell = CaptureShell(map); var circle = CaptureObject(((object[])shell["Circles"])[0]);
                if (mode == 0) circle["CurveAndEdgeSameSense"] = false;
                if (mode == 1) circle["Axis"] = new { X = 0, Y = 1, Z = 0 };
                if (mode == 2) CaptureObject(CaptureObject(((object[])shell["Edges"])[12])["Arc"])["RadiusMm"] = 2.25;
                if (mode == 3) shell["SourceSha256"] = new string('0', 64);
                CaptureRejected(map, "CAPTURE_NATIVE_SOURCE_HASH_MISMATCH");
            }
        });
        Test("offline V2: hash is mandatory and legacy V1 native packets require a fresh capture", () => {
            foreach (int mode in Enumerable.Range(0, 3))
            {
                var map = CaptureMap(NativeBoreModel()); var shell = CaptureShell(map);
                if (mode == 0) shell.Remove("SourceSha256"); else shell["SourceSha256"] = mode == 1 ? null : "not-a-sha";
                CaptureRejected(map, "CAPTURE_NATIVE_SOURCE_HASH_REQUIRED");
            }
            var legacy = CaptureMap(PolycellModel(new[] { 0.0, 1 }, new[] { 0.0, 1 }, new[] { -1.0, 1 }, (x, y, z) => true));
            CaptureShell(legacy).Remove("Circles"); CaptureRejected(legacy, "CAPTURE_NATIVE_CIRCLE_METADATA_MISSING");
        });
        Test("offline V2: a correctly hashed but inconsistent circle still fails the original geometry proof", () => {
            foreach (bool wrongAxis in new[] { false, true })
            {
                var model = NativeBoreModel(); var s = model.Bodies[0].EnvelopeTopology;
                var bad = new NativeEnvelopeBody(s.BodyId, true, s.NativeFaceCount, s.NativeEdgeCount, s.Edges, s.Faces,
                    circles: s.Circles.Select((c, i) => i == 0 ? new NativeCircleDefinition(c.EdgeId,
                        wrongAxis ? model.Bends[0].Frame.Y : c.Axis, wrongAxis ? c.CurveAndEdgeSameSense : !c.CurveAndEdgeSameSense) : c));
                var decoded = OfflineModelReader.Decode(CaptureBytes(NativeFixtureModel(bad, model.Bends[0].Frame)));
                Check(!CompileNative(decoded).Certificate.ContinuousCoverageProven, "A new synthetic source hash cannot bless inconsistent planes/winding");
            }
        });
        Test("offline V2: legacy managed uniform and current prismatic V2 paths retain their contracts", () => {
            var uniform = CollisionModel(Rect("LEGACY_UNIFORM", 0, 0, 1, 1), 1);
            var roundtrip = OfflineModelReader.Decode(CaptureBytes(uniform));
            Check(CompileNative(roundtrip).Certificate.Tier == CompileNative(uniform).Certificate.Tier, "Managed uniform proof unchanged");
            var prism = PolycellModel(new[] { 0.0, 2, 8, 10 }, new[] { 0.0, 2, 8, 10 }, new[] { -5.0, 5 }, (x, y, z) => x != 1 || y != 1);
            var native = OfflineModelReader.Decode(CaptureBytes(prism));
            Check(native.Bodies[0].EnvelopeTopology.SourceSha256 == prism.Bodies[0].EnvelopeTopology.SourceSha256 && CompileNative(native).Certificate.ContinuousCoverageProven, "Empty explicit Circles array is valid for a LINE-only V2 shell");
        });
        Test("offline V2: malformed JSON and duplicate native edge IDs fail closed", () => {
            bool rejected = false; try { OfflineModelReader.Decode(Encoding.UTF8.GetBytes("{")); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Malformed JSON is not an empty ready model");
            var map = CaptureMap(NativeBoreModel()); var edges = (object[])CaptureShell(map)["Edges"];
            CaptureObject(edges[0])["Id"] = CaptureObject(edges[1])["Id"];
            CaptureRejected(map, "CAPTURE_NATIVE_EDGE_IDENTITY_INVALID");
        });
        Test("offline V2: signed capture context still rejects wrong Part SHA, stale model/config and bad signature", () => {
            foreach (int mode in Enumerable.Range(0, 4)) using (var f = AuditSetup())
            {
                if (mode == 0) f.Intake.ModelSourceSha256 = new string('e', 64);
                if (mode == 1 || mode == 2)
                {
                    var path = Path.Combine(f.Pair.Reviews.Root, f.Intake.ModelCapturePath);
                    var map = CaptureObject(Json.DeserializeObject(File.ReadAllText(path)));
                    map[mode == 1 ? "UpdateStamp" : "Configuration"] = mode == 1 ? (object)98765 : "WRONG_CONFIG";
                    var bytes = CaptureBytes(map); File.WriteAllBytes(path, bytes);
                    f.Intake.ModelCaptureReview = f.Pair.Reviews.Sign("OfflineModelCapture", OfflineModelReader.Payload(bytes,
                        f.Intake.ModelSourceSha256, f.Intake.MachineId, f.Intake.SetupId), false);
                }
                if (mode == 3) f.Intake.ModelCaptureReview.SignatureBase64 = Convert.ToBase64String(new byte[256]);
                var context = OfflineModelReader.Read(f.Intake, f.Pair.Reviews.Root, f.Pair.Reviews.Verifier, CancellationToken.None);
                var rows = ToolDataAudit.Evaluate(f.Library, f.Intake, f.Sources, f.Pair.Reviews.Verifier, f.Pair.Reviews.Root, context);
                Check(rows.Count == 18 && rows.All(r => r.Readiness != "READY_PRODUCTION_STATIC_CHECK"), "Exact Part/snapshot/config/signature binding remains required");
            }
        });
        Test("offline V2: real CLI restores signed synthetic native snapshots but keeps every unreviewed tool UNKNOWN", () => {
            foreach (var model in new[] { NativeBoreModel(), NativeQuarterBendModel() }) using (var f = AuditSetup())
            {
                var bytes = CaptureBytes(model); File.WriteAllBytes(Path.Combine(f.Pair.Reviews.Root, f.Intake.ModelCapturePath), bytes);
                f.Intake.ModelCaptureReview = f.Pair.Reviews.Sign("OfflineModelCapture", OfflineModelReader.Payload(bytes,
                    f.Intake.ModelSourceSha256, f.Intake.MachineId, f.Intake.SetupId), false);
                f.Intake.AxialSetups.Clear(); // No fictional installation/lengths for the new source.
                string config = Path.Combine(f.Pair.Reviews.Root, "placement.json"), intake = Path.Combine(f.Pair.Reviews.Root, "intake.json"), output = Folder();
                File.WriteAllText(config, Json.Serialize(new BendPlacementConfiguration { Version = 1, MasterPath = f.Pair.Master.Source,
                    MasterManifestPath = f.Pair.Master.Manifest, LimitWorkbookPath = Path.Combine(f.Pair.Reviews.Root, "unconfigured.xlsx"),
                    LimitManifestPath = Path.Combine(f.Pair.Reviews.Root, "unconfigured-limits.json"), ReviewAuthoritiesPath = f.Pair.Reviews.TrustPath }));
                File.WriteAllText(intake, Json.Serialize(f.Intake));
                var cliArguments = new[] { "--placement", config, "--dim-directory", Folder(),
                    "--intake", intake, "--authorities", f.Pair.Reviews.TrustPath, "--output", output };
                int exit = ADDIN.Tools.BendToolAudit.Program.Run(cliArguments);
                Check(exit == 2, "CLI records unresolved evidence, not a production success");
                var context = CaptureObject(Json.DeserializeObject(File.ReadAllText(Path.Combine(output, "PRIVATE-model-context.json"))));
                Check((string)context["NativeSourceContract"] == "NativeAnalyticShellV2" && context["Diagnostic"] == null, "Verified V2 context reaches report");
                Check(((object[])context["Envelopes"]).All(e => (string)CaptureObject(e)["Tier"] == "AxialSlicesWithCertifiedCoverage"), "Fallback compiler is used end to end");
                var report = CaptureObject(Json.DeserializeObject(File.ReadAllText(Path.Combine(output, "PRIVATE-audit.json"))));
                var rows = (object[])report["Tools"];
                Check(rows.Length == 18 && rows.All(r => (string)CaptureObject(r)["Readiness"] == "UNKNOWN"), "Coverage is not tool/setup approval");
                Check(rows.Count(r => (bool)CaptureObject(r)["SeparateMappingFollowup"]) == 4, "Four requested followups survive native rehydration");
                // A synthetic authorized signature cannot override missing V2
                // source metadata. All unresolved slots still get a report.
                var broken = CaptureMap(model); CaptureShell(broken).Remove("Circles"); bytes = CaptureBytes(broken);
                File.WriteAllBytes(Path.Combine(f.Pair.Reviews.Root, f.Intake.ModelCapturePath), bytes);
                f.Intake.ModelCaptureReview = f.Pair.Reviews.Sign("OfflineModelCapture", OfflineModelReader.Payload(bytes,
                    f.Intake.ModelSourceSha256, f.Intake.MachineId, f.Intake.SetupId), false);
                File.WriteAllText(intake, Json.Serialize(f.Intake)); cliArguments[cliArguments.Length - 1] = Folder();
                Check(ADDIN.Tools.BendToolAudit.Program.Run(cliArguments) == 2, "Signed malformed source becomes UNKNOWN, not a lost report");
                var rejectedContext = CaptureObject(Json.DeserializeObject(File.ReadAllText(Path.Combine(cliArguments.Last(), "PRIVATE-model-context.json"))));
                Check((string)rejectedContext["Diagnostic"] == "CAPTURE_NATIVE_CIRCLE_METADATA_MISSING", "Exact actionable diagnostic survives the CLI");
                var rejectedRows = (object[])CaptureObject(Json.DeserializeObject(File.ReadAllText(Path.Combine(cliArguments.Last(), "PRIVATE-audit.json"))))["Tools"];
                Check(rejectedRows.Length == 18 && rejectedRows.All(r => (string)CaptureObject(r)["Readiness"] == "UNKNOWN"), "Invalid circle metadata cannot approve or remove any tool slot");
            }
        });
    }
    private static int AuditSnapshotFocused(string[] args)
    {
        temporary = Path.Combine(Path.GetTempPath(), "ADDIN-AuditSnapshotTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        AuditSnapshotTests(args[0] == "--audit-snapshot-roundtrip");
        Console.WriteLine("RESULT " + passed + " passed; " + failed + " failed; " + assertions + " assertions; synthetic snapshots only.");
        return failed == 0 ? 0 : 1;
    }
    // Explicit QA replay only: no signature, physical approval or source repair.
    // Caller supplies a private saved capture and the existing private Part copy.
    private static int AuditSnapshotPrivate(string[] args)
    {
        try
        {
            var capturePath = Path.GetFullPath(args[1]); var partPath = Path.GetFullPath(args[2]); var output = Path.GetFullPath(args[3]);
            int expectedBends = int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
            ADDIN.Tools.BendToolAudit.Program.PrivateOutput(output, new[] { Path.GetDirectoryName(capturePath), Path.GetDirectoryName(partPath) });
            var ledger = new AuditFileLedger(); ledger.Watch(capturePath); ledger.Watch(partPath);
            var model = OfflineModelReader.Decode(File.ReadAllBytes(capturePath));
            Check(model.Bends.Count == expectedBends, "Caller-specified saved native model scope");
            var decodedAgain = OfflineModelReader.Decode(CaptureBytes(model));
            Check(model.Bodies.Select(b => b.EnvelopeTopology.SourceSha256).SequenceEqual(decodedAgain.Bodies.Select(b => b.EnvelopeTopology.SourceSha256)), "Private native V2 hashes survive two roundtrips");
            var envelopes = model.Bends.Select(b => PrismaticEnvelopeCompiler.Read(model, b, CancellationToken.None)).ToArray();
            Check(envelopes.All(e => e.Certificate.ContinuousCoverageProven), "Every saved native frame retains continuous coverage");
            Check(ledger.Current, "Saved capture and private Part bytes unchanged");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "PRIVATE-offline-replay.json"), Json.Serialize(new { Scope = "UnreviewedGeometryQAOnly_NoProductionApproval", Model = model.SnapshotId,
                model.Configuration, model.UpdateStamp, InputFingerprint = ledger.Fingerprint, Assemblies = typeof(OfflineModelReader).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray(),
                Envelopes = envelopes.Select(e => new { e.Binding.FeatureId, Tier = e.Certificate.Tier.ToString(), e.Certificate.Method, e.GeometrySha256,
                    e.Binding.SourceSha256, Intervals = e.Certificate.Intervals.Count, e.Reasons }).ToArray() }));
            Console.WriteLine("PRIVATE SNAPSHOT REPLAY PASS; canonical native hashes, coverage and unchanged saved-source bytes verified; no COM capture or production approval."); return 0;
        }
        catch (Exception ex) { Console.WriteLine("PRIVATE SNAPSHOT REPLAY FAIL: " + (ex is InvalidDataException ? ex.Message : ex.GetType().Name)); return 1; }
    }
}
