// CG2 Live CAD Runner — Fail-Closed Sandbox Runner for SOLIDWORKS CAD Fixtures.
// C# 7.3 / .NET Framework 4.8.
// Compares CG1 baseline against CG2 candidate on isolated Pack-and-Go assemblies.
// Verifies 100% parity on production metrics (Holes, Stacks, NG, Pattern, Missing, Rows, CSV)
// and details CG2 broad-phase diagnostic screening metrics.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Tests
{
    internal static partial class Cg2LiveCadRunner
    {
        private const string MarkerPrefix = "CHECK-HOLE-NG";
        private const string SandboxConsentFile = "CG1_SANDBOX_OK.txt";

        internal sealed class CandidateDetail
        {
            public int Id;
            public string SourceOccurrence;
            public string TargetOccurrence;
            public string SourceHoleLabel;
            public double BoxSeparationMm;
            public double AxisToBoxMm;
            public string Classification;
            public string Reason;
            public string MaterialEvidence;
            public double MaterialSpanMm;
            public int MaterialHitCount;
        }

        private sealed class Metrics
        {
            public int LogicalHoles, GroupedHoles, UnmatchedHoles, StackCount;
            public int NgStacks, PatternNg, MissingHoles;
            public string Configuration;
            public string RowSignature;
            public int RowCount;
            public object RawReport;
            public string ComponentTraversal;
            public string OrderedRowSignature;

            // CG1 metrics
            public bool HasCg1;
            public int Cg1Groups, Cg1Unmatched, Cg1NgWithoutMaster;
            public string Cg1CoverageStatus;

            // CG2 metrics
            public bool HasCg2;
            public int Cg2Bounds, Cg2NoHole, Cg2Pairs, Cg2Candidates;
            public int Cg2ComparedPairs, Cg2TestedHoles;
            public int Cg2SubassemblyContainers, Cg2UnresolvedLeafParts;
            public int Cg2BrepRayTested, Cg2BrepSolid, Cg2BrepNoAxisHit;
            public int Cg2BrepUnknown, Cg2BrepNotTested;
            public bool Cg2IsPartial;
            public string Cg2CoverageStatus;
            public readonly List<CandidateDetail> Cg2ExpectedCandidates = new List<CandidateDetail>();
            public readonly List<string> Cg2Warnings = new List<string>();
        }

        private sealed class ModelComparison
        {
            public string Model;
            public bool Passed;
            public string Error;
            public Metrics Baseline;
            public Metrics Candidate;
        }

        private static bool s_captureRepeatabilityTrace;
        private static string s_baselineDir;
        private static string s_candidateDir;

        private static Assembly ResolveDependency(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string folder in new[] {
                s_candidateDir,
                s_baselineDir,
                @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS",
                @"C:\Tao moi"
            })
            {
                if (string.IsNullOrEmpty(folder)) continue;
                string candidate = Path.Combine(folder, name);
                if (File.Exists(candidate)) return Assembly.Load(File.ReadAllBytes(candidate));
            }
            return null;
        }

        [STAThread]
        public static int Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveDependency;

            if (args.Length == 1 && args[0] == "--self-test")
                return SelfTestExitPolicy();

            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: Cg2LiveCadRunner <cg1_baseline.dll> <cg2_candidate.dll> <sandbox-root> <fixture1.sldasm> [fixture2.sldasm...]");
                Console.Error.WriteLine("Or:    Cg2LiveCadRunner --self-test");
                return 2;
            }

            return RunRunner(args);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int RunRunner(string[] args)
        {
            ISldWorks app = null;
            bool ownsNewSession = false;
            try
            {
                string baselineDll = Path.GetFullPath(args[0]);
                string candidateDll = Path.GetFullPath(args[1]);
                string sandboxRoot = Path.GetFullPath(args[2]);
                bool repeatabilityMode = args.Length == 5 &&
                    string.Equals(args[4], "--repeatability", StringComparison.Ordinal);
                var models = repeatabilityMode
                    ? new[] { Path.GetFullPath(args[3]) }
                    : args.Skip(3).Select(Path.GetFullPath).ToArray();
                s_captureRepeatabilityTrace = repeatabilityMode;

                ValidateSandboxInputs(baselineDll, candidateDll, sandboxRoot, models);

                s_baselineDir = Path.GetDirectoryName(baselineDll);
                s_candidateDir = Path.GetDirectoryName(candidateDll);

                if (Process.GetProcessesByName("SLDWORKS").Length != 0)
                    throw new InvalidOperationException(
                        "An existing SOLIDWORKS session is running. Close it yourself before running this isolated test. No CAD was touched.");

                Type swType = Type.GetTypeFromProgID("SldWorks.Application", true);
                app = (ISldWorks)Activator.CreateInstance(swType);
                if (app == null) throw new InvalidOperationException("SOLIDWORKS did not start.");
                ownsNewSession = true;

                if (app.ActiveDoc != null)
                    throw new InvalidOperationException(
                        "The new SOLIDWORKS session has an active document. Refusing to interact with it.");

                app.Visible = false;
                Console.WriteLine("SOLIDWORKS revision: " + app.RevisionNumber());

                Assembly baselineAsm = Assembly.Load(File.ReadAllBytes(baselineDll));
                Assembly candidateAsm = Assembly.Load(File.ReadAllBytes(candidateDll));
                Type baselineType = RequireType(baselineAsm);
                Type candidateType = RequireType(candidateAsm);

                Console.WriteLine("BASELINE (CG1) SHA256:  " + Sha256(baselineDll));
                Console.WriteLine("CANDIDATE (CG2) SHA256: " + Sha256(candidateDll));
                Console.WriteLine("MARKER POLICY: AutoCreateMarkers=false enforced in safe test mode.");
                Console.WriteLine("MARKER_GENERATION_PARITY: NOT_TESTED (protected fixture sandbox policy)");
                if (repeatabilityMode)
                    return RunRepeatability(app, baselineType, candidateType,
                        baselineAsm, candidateAsm, sandboxRoot, models[0]);

                int passed = 0, failed = 0, skipped = 0;
                foreach (string fixture in models)
                {
                    Console.WriteLine("\n=======================================================");
                    Console.WriteLine("TESTING FIXTURE: " + Path.GetFileName(fixture));
                    Console.WriteLine("=======================================================");
                    ModelComparison result = RunComparison(app, baselineType, candidateType,
                        baselineAsm, candidateAsm, sandboxRoot, fixture);
                    // Print physical evidence even when strict raw CSV parity fails.
                    // The comparison's final FAIL/exit status must remain unchanged.
                    if (result.Candidate != null)
                    {
                        var m = result.Candidate;
                        Console.WriteLine("  PRODUCTION: Holes=" + m.LogicalHoles +
                            " Stacks=" + m.StackCount + " NG=" + m.NgStacks +
                            " PatternNG=" + m.PatternNg + " Missing=" + m.MissingHoles +
                            " Rows=" + m.RowCount);
                        Console.WriteLine("  CG2 COVERAGE: Bounds=" + m.Cg2Bounds +
                            " NoHoles=" + m.Cg2NoHole +
                            " SubassemblyContainers=" + m.Cg2SubassemblyContainers +
                            " UnresolvedLeafParts=" + m.Cg2UnresolvedLeafParts +
                            " Candidates=" + m.Cg2Candidates +
                            " Partial=" + m.Cg2IsPartial + " Status=" + m.Cg2CoverageStatus);
                        Console.WriteLine("  BREP: Tested=" + m.Cg2BrepRayTested +
                            " Solid=" + m.Cg2BrepSolid + " NoAxisHit=" + m.Cg2BrepNoAxisHit +
                            " Unknown=" + m.Cg2BrepUnknown +
                            " NotTested=" + m.Cg2BrepNotTested);
                        LogCandidateInvestigation(Path.GetFileName(fixture), m);
                    }
                    if (result.Passed)
                    {
                        passed++;
                        Console.WriteLine("RESULT: PASS | " + Path.GetFileName(fixture));
                    }
                    else
                    {
                        failed++;
                        Console.WriteLine("RESULT: FAIL | " + Path.GetFileName(fixture) +
                            " | " + result.Error);
                    }
                }

                int code = ExitCode(passed, failed, skipped, models.Length);
                Console.WriteLine("\n===============================================================");
                Console.WriteLine("SUMMARY: PASS=" + passed + " FAIL=" + failed +
                                  " SKIP=" + skipped + " EXIT=" + code);
                Console.WriteLine("===============================================================");
                return code;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL CLOSED: " + ex);
                return 1;
            }
            finally
            {
                if (ownsNewSession && app != null)
                {
                    try { app.ExitApp(); }
                    catch (Exception ex) { Console.Error.WriteLine("SOLIDWORKS shutdown warning: " + ex.Message); }
                }
            }
        }

        // Always report real BREP/AABB candidates, even on strict CSV failure.
        // The fixture manifest's exact site assertion is still a separate gate.
        private static void LogCandidateInvestigation(string modelName, Metrics m)
        {
            Console.WriteLine("  [CG2.1 SITE EVIDENCE: " + modelName + "]");
            Console.WriteLine("  Stored=" + m.Cg2ExpectedCandidates.Count +
                " Total=" + m.Cg2Candidates + " (stored list may be capped)");
            var ordered = m.Cg2ExpectedCandidates
                .OrderBy(c => c.MaterialEvidence == "BREP_SOLID_ON_AXIS" ? 0 :
                    c.MaterialEvidence != null &&
                    c.MaterialEvidence.StartsWith("UNVERIFIED", StringComparison.Ordinal) ? 1 : 2)
                .ThenBy(c => c.Id).Take(20);
            foreach (CandidateDetail c in ordered)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  Candidate #{0}: Source=[{1}] ({2}) -> Target=[{3}] | " +
                    "Gap={4:F3}mm AxisDist={5:F3}mm | {6} | BREP={7} Hits={8} Span={9:F3}mm",
                    c.Id, c.SourceOccurrence, c.SourceHoleLabel, c.TargetOccurrence,
                    c.BoxSeparationMm, c.AxisToBoxMm, c.Classification,
                    c.MaterialEvidence ?? "NOT_RECORDED", c.MaterialHitCount, c.MaterialSpanMm));
            Console.WriteLine("  BREP_HIT_PROVES_MISSING_HOLE=NO");
            Console.WriteLine("  CONTACT_OR_FASTENING_INTENT=NOT_VERIFIED");
            Console.WriteLine("  EXACT_NESTED_SITE=REQUIRES_FIXTURE_MANIFEST_ASSERTION");
        }

        // The fixture manifest identifies exact occurrence IDs; NO substring
        // matching like "B" / "fr", and NO fallback to unrelated candidates.
        // It proves candidate provenance ONLY, not a real missing hole.
        private static void ValidateCg2ExpectedCandidates(string modelName, Metrics m)
        {
            if (!m.HasCg2)
                throw new InvalidOperationException("CG2 diagnostics missing");

            if (m.Cg2ExpectedCandidates.Count > m.Cg2Candidates)
                throw new InvalidOperationException("Stored candidates exceed reported candidate count");

            var seenIds = new HashSet<int>();
            foreach (CandidateDetail c in m.Cg2ExpectedCandidates)
            {
                if (c == null || c.Id <= 0 || !seenIds.Add(c.Id)
                    || string.IsNullOrWhiteSpace(c.SourceOccurrence)
                    || string.IsNullOrWhiteSpace(c.TargetOccurrence)
                    || string.IsNullOrWhiteSpace(c.SourceHoleLabel)
                    || !string.Equals(c.Classification, "CHECK_ONLY", StringComparison.Ordinal)
                    || double.IsNaN(c.BoxSeparationMm) || double.IsInfinity(c.BoxSeparationMm)
                    || double.IsNaN(c.AxisToBoxMm) || double.IsInfinity(c.AxisToBoxMm)
                    || double.IsNaN(c.MaterialSpanMm) || double.IsInfinity(c.MaterialSpanMm)
                    || c.MaterialSpanMm < 0 || c.MaterialHitCount < 0
                    || (c.MaterialEvidence == "BREP_SOLID_ON_AXIS" &&
                        (c.MaterialHitCount < 2 || c.MaterialSpanMm < 0.1))
                    || (c.MaterialEvidence == "BREP_NO_AXIS_HIT" &&
                        (c.MaterialHitCount != 0 || c.MaterialSpanMm != 0)))
                    throw new InvalidOperationException("CG2 candidate provenance/values invalid at ID="
                        + (c == null ? "<null>" : c.Id.ToString(CultureInfo.InvariantCulture)));
            }

            if (string.Equals(modelName, "Fixture_Unmatched.SLDASM", StringComparison.OrdinalIgnoreCase)
                || string.Equals(modelName, "run07_Missing_Ura_BOM.SLDASM", StringComparison.OrdinalIgnoreCase))
            {
                if (m.Cg2Candidates != 0 || m.Cg2ExpectedCandidates.Count != 0)
                    throw new InvalidOperationException("Expected zero candidates for fixture " + modelName);
            }

            if (string.Equals(modelName, "Fixture_OK.SLDASM", StringComparison.OrdinalIgnoreCase)
                || string.Equals(modelName, "Fixture_MultiLayer_ABC.SLDASM", StringComparison.OrdinalIgnoreCase))
            {
                const string sourceA = "1618-04-bh-A10-fr1-2";
                const string expectedB = "1618-04-bh-A10-1-1";
                var found = m.Cg2ExpectedCandidates.Where(c =>
                    string.Equals(c.SourceOccurrence, sourceA, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.TargetOccurrence, expectedB, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (found.Count == 0)
                    throw new InvalidOperationException(
                        "No actual CHECK_ONLY AABB candidate with exact source A and target B: " + modelName);
                Console.WriteLine("  EXACT OCCURRENCE A->B candidates observed: " + found.Count
                    + " (AABB screening ONLY; site material not proven)");
            }
        }

        private static int ExitCode(int pass, int fail, int skip, int expected)
        {
            return fail == 0 && skip == 0 && pass == expected && expected > 0 ? 0 : 1;
        }

        private static int SelfTestExitPolicy()
        {
            bool ok =
                ExitCode(2, 0, 0, 2) == 0 &&
                ExitCode(1, 1, 0, 2) != 0 &&
                ExitCode(1, 0, 1, 2) != 0 &&
                ExitCode(0, 0, 2, 2) != 0 &&
                ExitCode(0, 0, 0, 2) != 0;
            Console.WriteLine(ok
                ? "PASS: exit policy handles PASS / FAIL / SKIP / empty cases"
                : "FAIL: exit policy test");

            // Regression guard test: malformed candidate data MUST be rejected.
            var synthetic = new Metrics { HasCg2 = true, Cg2Candidates = 1 };
            synthetic.Cg2ExpectedCandidates.Add(new CandidateDetail
            {
                Id = 1, SourceOccurrence = "A-1", TargetOccurrence = "B-1",
                SourceHoleLabel = "Hole#1", Classification = "CHECK_ONLY",
                BoxSeparationMm = 0, AxisToBoxMm = 0
            });
            try
            {
                ValidateCg2ExpectedCandidates("unit-fixture.SLDASM", synthetic);
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: valid candidate incorrectly rejected: " + ex.Message);
                ok = false;
            }
            synthetic.Cg2ExpectedCandidates[0].Classification = "NG";
            bool invalidRejected = false;
            try
            {
                ValidateCg2ExpectedCandidates("unit-fixture.SLDASM", synthetic);
            }
            catch (InvalidOperationException) { invalidRejected = true; }
            if (!invalidRejected)
            {
                Console.WriteLine("FAIL: invalid NG candidate was accepted");
                ok = false;
            }
            else Console.WriteLine("PASS: malformed candidate was rejected");

            // Also exercise the linked Review #3 source: multiline CSV records
            // must not be split at embedded CRLF inside quoted fields.
            string sample = "a,b\r\n\"line1\r\nline2\",\"a\"\"b\"\r\n";
            string[] records = SplitCsvRecords(sample).ToArray();
            bool splitOk = records.Length == 2 &&
                records[1] == "\"line1\r\nline2\",\"a\"\"b\"";
            if (!splitOk)
            {
                Console.WriteLine("FAIL: Review3 quoted-multiline CSV record splitter");
                ok = false;
            }
            else Console.WriteLine("PASS: Review3 quoted-multiline CSV record splitter");
            return ok ? 0 : 1;
        }

        private static void ValidateSandboxInputs(
            string baselineDll, string candidateDll, string root, string[] models)
        {
            if (!File.Exists(baselineDll) || !File.Exists(candidateDll))
                throw new FileNotFoundException("Baseline or candidate DLL is missing.");
            if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, SandboxConsentFile)))
                throw new InvalidOperationException(
                    "Isolated fixture root missing, or explicit CG1_SANDBOX_OK.txt acknowledgement missing.");
            if (string.Equals(baselineDll, candidateDll, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("DLL paths cannot be identical.");
            if (models.Distinct(StringComparer.OrdinalIgnoreCase).Count() != models.Length)
                throw new InvalidOperationException("The required test models must be distinct.");
            foreach (string path in models)
            {
                RequireSandboxPath(root, path);
                if (!string.Equals(Path.GetExtension(path), ".sldasm", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Only .sldasm fixture copies are allowed.");
                if (!File.Exists(path))
                    throw new FileNotFoundException("Required fixture is missing; fail, not pass: " + path);
            }
        }

        private static void RequireSandboxPath(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path outside isolated fixture root: " + path);
        }

        private static Type RequireType(Assembly assembly)
        {
            Type type = assembly.GetType("ADDIN.Commands.CheckAssemblyHole");
            if (type == null)
                throw new InvalidOperationException("CheckAssemblyHole missing in DLL " + assembly.FullName);
            return type;
        }

        private static ModelComparison RunComparison(
            ISldWorks app, Type baselineType, Type candidateType, Assembly baseAsm, Assembly candAsm, string root, string model)
        {
            var response = new ModelComparison { Model = Path.GetFileName(model) };
            try
            {
                Metrics baseline = RunOneIsolated(app, baselineType, root, model, isCandidate: false);
                Metrics candidate = RunOneIsolated(app, candidateType, root, model, isCandidate: true);
                response.Baseline = baseline;
                response.Candidate = candidate;

                // 1. Rigorous Row-by-Row Metrics & Signature Comparison (Review #1 BLOCKER B)
                CompareMetrics(baseline, candidate, model);
                ValidateCg2ExpectedCandidates(Path.GetFileName(model), candidate);

                // 2. Real CSV Line-by-Line Parity Verification (Review #1 BLOCKER B)
                VerifyCsvParity(root, Path.GetFileName(model), baseline.RawReport, candidate.RawReport, baseAsm, candAsm);
                Console.WriteLine("  CSV_BYTE_PARITY=PASS (strict raw byte comparison)");

                response.Baseline = baseline;
                response.Candidate = candidate;
                response.Passed = true;
            }
            catch (Exception ex)
            {
                response.Passed = false;
                response.Error = ex.Message;
            }
            return response;
        }

        // STRICT CSV BYTE PARITY. No sorting, no swapping of components,
        // no ReadAllLines (which breaks quoted multiline CSV records).
        // This checks the REAL production CheckHoleCsv.Write() output.
        private static void VerifyCsvParity(
            string root, string modelName,
            object baselineReport, object candidateReport,
            Assembly baseAsm, Assembly candAsm)
        {
            string testDir = Path.Combine(root, "CsvTestParity");
            if (!Directory.Exists(testDir)) Directory.CreateDirectory(testDir);

            string stem = Path.GetFileNameWithoutExtension(modelName);
            string baseCsv = Path.Combine(testDir, stem + "_baseline.csv");
            string candCsv = Path.Combine(testDir, stem + "_candidate.csv");
            Type baseType = baseAsm.GetType("ADDIN.Commands.CheckHoleCsv", true);
            Type candType = candAsm.GetType("ADDIN.Commands.CheckHoleCsv", true);
            MethodInfo baseWrite = baseType.GetMethod("Write", BindingFlags.Public | BindingFlags.Static);
            MethodInfo candWrite = candType.GetMethod("Write", BindingFlags.Public | BindingFlags.Static);
            if (baseWrite == null || candWrite == null)
                throw new MissingMethodException("CheckHoleCsv.Write is missing");

            // Distinct report objects, same isolated output folder.
            baseWrite.Invoke(null, new object[] { baseCsv, baselineReport });
            candWrite.Invoke(null, new object[] { candCsv, candidateReport });

            if (!File.Exists(baseCsv) || !File.Exists(candCsv))
                throw new FileNotFoundException("CSV parity output was not created");
            byte[] a = File.ReadAllBytes(baseCsv);
            byte[] b = File.ReadAllBytes(candCsv);
            if (a.Length == 0 || b.Length == 0)
                throw new InvalidOperationException("CSV parity output is empty");

            if (!a.SequenceEqual(b))
            {
                int first = 0;
                int n = Math.Min(a.Length, b.Length);
                while (first < n && a[first] == b[first]) first++;
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "CSV_BYTE_PARITY_FAIL model={0}, baselineBytes={1}, candidateBytes={2}, firstDiffByte={3}",
                    modelName, a.Length, b.Length, first));
            }
            Console.WriteLine("  CSV_BYTE_PARITY=PASS; exact bytes=" + a.Length
                + "; sha256=" + Sha256(baseCsv));
        }

        private static Metrics RunOneIsolated(
            ISldWorks app, Type checkerType, string root, string modelPath, bool isCandidate)
        {
            RequireSandboxPath(root, modelPath);

            if (app.ActiveDoc != null)
                throw new InvalidOperationException(
                    "Unexpected active document. Refusing to close or replace a document not owned by runner.");

            string originalHash = Sha256(modelPath);
            int errors = 0, warnings = 0;
            int flags = (int)(swOpenDocOptions_e.swOpenDocOptions_Silent |
                              swOpenDocOptions_e.swOpenDocOptions_ReadOnly);
            ModelDoc2 doc = app.OpenDoc6(
                modelPath, (int)swDocumentTypes_e.swDocASSEMBLY, flags, "",
                ref errors, ref warnings);
            if (doc == null)
                throw new InvalidOperationException("OpenDoc6 failed: errors=" + errors + ", warnings=" + warnings);

            bool ownedDocument = true;
            Exception testFailure = null;
            Metrics metrics = null;
            try
            {
                RequireSandboxPath(root, doc.GetPathName());
                VerifyReferencesUnderSandbox(doc, root);
                string componentTraversal = s_captureRepeatabilityTrace
                    ? SnapshotComponentTraversal(doc) : null;
                string beforeFeatures = FeatureSignature(doc);
                EnsureNoOldMarkers(doc);
                if (doc.GetSaveFlag())
                    throw new InvalidOperationException(
                        "Fixture is dirty directly after open. Refusing destructive ambiguity.");

                object checker = Activator.CreateInstance(checkerType, app);
                PropertyInfo markerProperty = checkerType.GetProperty("AutoCreateMarkers");
                if (markerProperty == null)
                    throw new MissingMemberException("AutoCreateMarkers property absent.");
                markerProperty.SetValue(checker, false, null);

                MethodInfo run = checkerType.GetMethod("Run", BindingFlags.Public | BindingFlags.Instance);
                if (run == null) throw new MissingMethodException("CheckAssemblyHole.Run missing.");
                run.Invoke(checker, null);

                FieldInfo reportField = checkerType.GetField(
                    "lastReport", BindingFlags.NonPublic | BindingFlags.Instance);
                if (reportField == null) throw new MissingFieldException("lastReport missing.");
                object report = reportField.GetValue(checker);
                if (report == null) throw new InvalidOperationException("Check Hole did not produce a report.");

                metrics = ReadReport(report);
                metrics.RawReport = report;
                if (s_captureRepeatabilityTrace)
                {
                    metrics.ComponentTraversal = componentTraversal;
                    metrics.OrderedRowSignature = OrderedRowSignatureExact(report);
                }

                if (isCandidate)
                {
                    if (!metrics.HasCg2)
                        throw new InvalidOperationException("CG2 PartRelations diagnostics missing in candidate run.");
                }

                EnsureNoOldMarkers(doc);
                if (!string.Equals(beforeFeatures, FeatureSignature(doc), StringComparison.Ordinal))
                    throw new InvalidOperationException("Assembly feature tree changed IN MEMORY.");
                if (doc.GetSaveFlag())
                    throw new InvalidOperationException(
                        "Assembly became modified in memory. Fixture run is not non-mutating.");
            }
            catch (Exception e)
            {
                testFailure = e;
            }
            finally
            {
                if (ownedDocument)
                {
                    try { app.CloseDoc(doc.GetTitle()); }
                    catch (Exception ex) { if (testFailure == null) testFailure = ex; }
                }
                try
                {
                    if (!string.Equals(originalHash, Sha256(modelPath), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Fixture bytes changed on disk.");
                }
                catch (Exception ex)
                {
                    if (testFailure == null) testFailure = ex;
                }
            }

            if (testFailure != null) throw new InvalidOperationException(
                "Isolated run rejected: " + testFailure.Message, testFailure);
            return metrics;
        }

        private static void VerifyReferencesUnderSandbox(ModelDoc2 model, string root)
        {
            AssemblyDoc assembly = model as AssemblyDoc;
            if (assembly == null) throw new InvalidOperationException("Fixture is not an Assembly.");

            object[] all = assembly.GetComponents(false) as object[];
            if (all == null)
                throw new InvalidOperationException(
                    "Cannot enumerate component references; refusing unverified isolation.");

            foreach (object obj in all)
            {
                Component2 c = obj as Component2;
                if (c == null) throw new InvalidOperationException("Unrecognized component reference.");
                string componentPath = c.GetPathName();
                if (string.IsNullOrWhiteSpace(componentPath))
                    throw new InvalidOperationException(
                        "Virtual/unresolved component reference: " + c.Name2);
                RequireSandboxPath(root, componentPath);
                if (!File.Exists(componentPath))
                    throw new FileNotFoundException(
                        "Pack and Go missing dependency: " + componentPath);
            }
        }

        private static void EnsureNoOldMarkers(ModelDoc2 doc)
        {
            for (Feature f = doc.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
            {
                if ((f.Name ?? "").StartsWith(MarkerPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Existing CHECK-HOLE-NG feature detected. Run only on a disposable fixture without markers.");
            }
        }

        private static string FeatureSignature(ModelDoc2 doc)
        {
            var sb = new StringBuilder();
            for (Feature f = doc.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
                sb.Append(f.Name).Append("|").Append(f.GetTypeName2()).Append("\n");
            return sb.ToString();
        }

        private static Metrics ReadReport(object report)
        {
            Type t = report.GetType();
            var m = new Metrics
            {
                LogicalHoles = IntField(t, report, "LogicalHoles"),
                GroupedHoles = IntField(t, report, "GroupedHoles"),
                UnmatchedHoles = IntField(t, report, "UnmatchedHoles"),
                StackCount = IntField(t, report, "StackCount"),
                NgStacks = IntField(t, report, "NgStacks"),
                PatternNg = IntField(t, report, "PatternNg"),
                MissingHoles = IntField(t, report, "MissingHoles"),
                Configuration = (string)t.GetField("Configuration").GetValue(report),
                RowSignature = ReportRowSignature(t, report),
                RowCount = GetRowCount(t, report)
            };

            FieldInfo cov = t.GetField("ConnectionCoverage");
            if (cov != null && cov.GetValue(report) != null)
            {
                object coverage = cov.GetValue(report);
                Type ct = coverage.GetType();
                m.HasCg1 = true;
                m.Cg1Groups = IntField(ct, coverage, "GroupCount");
                m.Cg1Unmatched = IntField(ct, coverage, "UnmatchedHoleCount");
                m.Cg1NgWithoutMaster = IntField(ct, coverage, "NgGroupsWithoutMaster");
                m.Cg1CoverageStatus = (string)ct.GetField("CoverageStatus").GetValue(coverage);
            }

            FieldInfo pr = t.GetField("PartRelations");
            if (pr != null && pr.GetValue(report) != null)
            {
                object partRel = pr.GetValue(report);
                Type pt = partRel.GetType();
                m.HasCg2 = true;
                m.Cg2Bounds = IntField(pt, partRel, "ComponentsWithBounds");
                m.Cg2NoHole = IntField(pt, partRel, "ComponentsWithoutDetectedHoles");
                m.Cg2Pairs = IntField(pt, partRel, "RecordedNearbyPairs");
                m.Cg2Candidates = IntField(pt, partRel, "CandidateCount");
                m.Cg2ComparedPairs = IntField(pt, partRel, "ComparedComponentPairs");
                m.Cg2TestedHoles = IntField(pt, partRel, "TestedHoleRelations");
                m.Cg2SubassemblyContainers = IntField(pt, partRel, "SubassemblyContainerCount");
                m.Cg2UnresolvedLeafParts = IntField(pt, partRel, "UnresolvedLeafOccurrenceCount");
                m.Cg2BrepRayTested = IntField(pt, partRel, "BrepRayTestedCount");
                m.Cg2BrepSolid = IntField(pt, partRel, "BrepSolidEvidenceCount");
                m.Cg2BrepNoAxisHit = IntField(pt, partRel, "BrepNoAxisHitCount");
                m.Cg2BrepUnknown = IntField(pt, partRel, "BrepUnknownCount");
                m.Cg2BrepNotTested = IntField(pt, partRel, "BrepNotTestedCount");
                m.Cg2IsPartial = (bool)pt.GetField("IsPartial").GetValue(partRel);
                m.Cg2CoverageStatus = (string)pt.GetField("CoverageStatus").GetValue(partRel);

                IEnumerable candList = pt.GetField("ExpectedHoleCandidates").GetValue(partRel) as IEnumerable;
                if (candList != null)
                {
                    foreach (object cRow in candList)
                    {
                        Type crt = cRow.GetType();
                        m.Cg2ExpectedCandidates.Add(new CandidateDetail
                        {
                            Id = (int)crt.GetProperty("Id").GetValue(cRow, null),
                            SourceOccurrence = (string)crt.GetProperty("SourceOccurrence").GetValue(cRow, null),
                            TargetOccurrence = (string)crt.GetProperty("TargetOccurrence").GetValue(cRow, null),
                            SourceHoleLabel = (string)crt.GetProperty("SourceHoleLabel").GetValue(cRow, null),
                            BoxSeparationMm = (double)crt.GetProperty("BoxSeparationMm").GetValue(cRow, null),
                            AxisToBoxMm = (double)crt.GetProperty("AxisToBoxMm").GetValue(cRow, null),
                            Classification = (string)crt.GetProperty("Classification").GetValue(cRow, null),
                            Reason = (string)crt.GetProperty("Reason").GetValue(cRow, null),
                            MaterialEvidence = (string)crt.GetProperty("MaterialEvidence").GetValue(cRow, null),
                            MaterialHitCount = (int)crt.GetProperty("MaterialHitCount").GetValue(cRow, null),
                            MaterialSpanMm = (double)crt.GetProperty("MaterialSpanMm").GetValue(cRow, null)
                        });
                    }
                }
            }

            return m;
        }

        private static int IntField(Type type, object obj, string name)
        {
            FieldInfo field = type.GetField(name);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return (int)field.GetValue(obj);
        }

        private static int GetRowCount(Type t, object report)
        {
            FieldInfo field = t.GetField("Rows");
            if (field == null) return 0;
            IEnumerable rows = field.GetValue(report) as IEnumerable;
            if (rows == null) return 0;
            int count = 0;
            foreach (object _ in rows) count++;
            return count;
        }

        // Expanded for Review #1 BLOCKER B: compares all significant fields on every CheckHoleReportRow
        private static string ReportRowSignature(Type t, object report)
        {
            FieldInfo field = t.GetField("Rows");
            if (field == null) throw new MissingFieldException(t.FullName, "Rows");
            IEnumerable rows = field.GetValue(report) as IEnumerable;
            if (rows == null) throw new InvalidOperationException("Report Rows unavailable.");
            var list = new List<string>();
            int expectedId = 0;
            foreach (object row in rows)
            {
                Type rt = row.GetType();
                var sb = new StringBuilder();
                int id = (int)rt.GetProperty("Id").GetValue(row, null);
                if (id != expectedId++)
                    throw new InvalidOperationException("Report row Id non-contiguous: expected " + (expectedId - 1) + " but got " + id);

                string status = (string)rt.GetProperty("Status").GetValue(row, null) ?? "";
                string kind = (string)rt.GetProperty("Kind").GetValue(row, null) ?? "";
                string refComp = (string)rt.GetProperty("ReferenceComponent").GetValue(row, null) ?? "";
                string actComp = (string)rt.GetProperty("ActualComponent").GetValue(row, null) ?? "";
                double? offset = (double?)rt.GetProperty("OffsetMm").GetValue(row, null);
                double? angle = (double?)rt.GetProperty("AngleDeg").GetValue(row, null);
                string reason = (string)rt.GetProperty("Reason").GetValue(row, null) ?? "";
                double[] refCtr = (double[])rt.GetProperty("ReferenceCenter").GetValue(row, null);
                double[] actCtr = (double[])rt.GetProperty("ActualCenter").GetValue(row, null);
                double[] viewAx = (double[])rt.GetProperty("ViewAxis").GetValue(row, null);
                bool orient = (bool)rt.GetProperty("OrientFromReferenceSide").GetValue(row, null);
                double radius = (double)rt.GetProperty("ViewRadiusM").GetValue(row, null);
                bool canMark = (bool)rt.GetProperty("CanMark").GetValue(row, null);

                // Canonicalize undirected connection pairs:
                // If refComp > actComp, swap components and their respective centers so that
                // signature is invariant to which hole was traversed first in the stack.
                if (!string.IsNullOrEmpty(refComp) && !string.IsNullOrEmpty(actComp) &&
                    string.CompareOrdinal(refComp, actComp) > 0)
                {
                    string tmpComp = refComp; refComp = actComp; actComp = tmpComp;
                    double[] tmpCtr = refCtr; refCtr = actCtr; actCtr = tmpCtr;
                }

                sb.Append(status).Append("|")
                  .Append(kind).Append("|")
                  .Append(refComp).Append("|")
                  .Append(actComp).Append("|")
                  .Append(offset.HasValue ? offset.Value.ToString("F6", CultureInfo.InvariantCulture) : "null").Append("|")
                  .Append(angle.HasValue ? angle.Value.ToString("F6", CultureInfo.InvariantCulture) : "null").Append("|")
                  .Append(reason).Append("|")
                  .Append(FormatVec(refCtr)).Append("|")
                  .Append(FormatVec(actCtr)).Append("|")
                  .Append(FormatVec(viewAx)).Append("|")
                  .Append(orient).Append("|")
                  .Append(radius.ToString("F6", CultureInfo.InvariantCulture)).Append("|")
                  .Append(canMark);

                list.Add(sb.ToString());
            }
            list.Sort(StringComparer.Ordinal);
            return string.Join("\n", list);
        }

        private static string FormatVec(double[] v)
        {
            if (v == null) return "null";
            return string.Join(";", v.Select(d => d.ToString("F6", CultureInfo.InvariantCulture)));
        }

        private static void CompareMetrics(Metrics a, Metrics b, string model)
        {
            if (a.LogicalHoles != b.LogicalHoles ||
                a.GroupedHoles != b.GroupedHoles ||
                a.UnmatchedHoles != b.UnmatchedHoles ||
                a.StackCount != b.StackCount ||
                a.NgStacks != b.NgStacks ||
                a.PatternNg != b.PatternNg ||
                a.MissingHoles != b.MissingHoles)
            {
                throw new InvalidOperationException(string.Format(
                    "Production counters changed on {0}: Baseline(Logical={1},Grouped={2},Unmatched={3},Stacks={4},NG={5},PatternNG={6},Missing={7}) " +
                    "vs Candidate(Logical={8},Grouped={9},Unmatched={10},Stacks={11},NG={12},PatternNG={13},Missing={14})",
                    model, a.LogicalHoles, a.GroupedHoles, a.UnmatchedHoles, a.StackCount, a.NgStacks, a.PatternNg, a.MissingHoles,
                    b.LogicalHoles, b.GroupedHoles, b.UnmatchedHoles, b.StackCount, b.NgStacks, b.PatternNg, b.MissingHoles));
            }

            if (a.RowCount != b.RowCount)
                throw new InvalidOperationException(string.Format(
                    "Report RowCount changed on {0}: Baseline={1} vs Candidate={2}", model, a.RowCount, b.RowCount));

            if (!string.Equals(a.Configuration, b.Configuration, StringComparison.Ordinal))
                throw new InvalidOperationException("Configuration changed: " + a.Configuration + " vs " + b.Configuration);

            if (!string.Equals(a.RowSignature, b.RowSignature, StringComparison.Ordinal))
            {
                string[] linesA = (a.RowSignature ?? "").Split('\n');
                string[] linesB = (b.RowSignature ?? "").Split('\n');
                var sb = new StringBuilder();
                sb.AppendLine("Row detailed signatures differ on " + model + ":");
                sb.AppendLine(string.Format("Total rows: A={0}, B={1}", linesA.Length, linesB.Length));
                int diffCount = 0;
                for (int i = 0; i < Math.Max(linesA.Length, linesB.Length) && diffCount < 5; i++)
                {
                    string la = i < linesA.Length ? linesA[i] : "<missing>";
                    string lb = i < linesB.Length ? linesB[i] : "<missing>";
                    if (!string.Equals(la, lb, StringComparison.Ordinal))
                    {
                        diffCount++;
                        sb.AppendLine(string.Format("Diff #{0} (index {1}):\n  Baseline : {2}\n  Candidate: {3}", diffCount, i, la, lb));
                    }
                }
                throw new InvalidOperationException(sb.ToString());
            }

            if (a.HasCg1 != b.HasCg1 ||
                a.Cg1Groups != b.Cg1Groups ||
                a.Cg1Unmatched != b.Cg1Unmatched ||
                a.Cg1NgWithoutMaster != b.Cg1NgWithoutMaster ||
                !string.Equals(a.Cg1CoverageStatus, b.Cg1CoverageStatus, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("CG1 ConnectionCoverage metrics changed between baseline and candidate on " + model);
            }
        }

        private static string Sha256(string path)
        {
            using (var s = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(s.ComputeHash(stream)).Replace("-", "");
            }
        }
    }
}
