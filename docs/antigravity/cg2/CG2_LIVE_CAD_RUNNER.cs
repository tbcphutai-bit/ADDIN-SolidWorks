// CG2 Live CAD Runner — Fail-Closed Sandbox Runner for SOLIDWORKS CAD Fixtures.
// C# 7.3 / .NET Framework 4.8.
// Compares CG1 baseline against CG2 candidate on isolated Pack-and-Go assemblies.
// Verifies 100% parity on production metrics (Holes, Stacks, NG, Pattern, Missing, Rows, Markers)
// while recording CG2 broad-phase diagnostic screening metrics.

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
    internal static class Cg2LiveCadRunner
    {
        private const string MarkerPrefix = "CHECK-HOLE-NG";
        private const string SandboxConsentFile = "CG1_SANDBOX_OK.txt";

        private sealed class Metrics
        {
            public int LogicalHoles, GroupedHoles, UnmatchedHoles, StackCount;
            public int NgStacks, PatternNg, MissingHoles;
            public string Configuration;
            public string RowSignature;

            // CG1 metrics
            public bool HasCg1;
            public int Cg1Groups, Cg1Unmatched, Cg1NgWithoutMaster;
            public string Cg1CoverageStatus;

            // CG2 metrics
            public bool HasCg2;
            public int Cg2Bounds, Cg2NoHole, Cg2Pairs, Cg2Candidates;
            public int Cg2ComparedPairs, Cg2TestedHoles;
            public bool Cg2IsPartial;
            public string Cg2CoverageStatus;
        }

        private sealed class ModelComparison
        {
            public string Model;
            public bool Passed;
            public string Error;
            public Metrics Baseline;
            public Metrics Candidate;
        }

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
                if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
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
                var models = args.Skip(3).Select(Path.GetFullPath).ToArray();

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

                int passed = 0, failed = 0, skipped = 0;
                foreach (string fixture in models)
                {
                    Console.WriteLine("\nTesting fixture: " + Path.GetFileName(fixture));
                    ModelComparison result = RunComparison(app, baselineType, candidateType, sandboxRoot, fixture);
                    if (result.Passed)
                    {
                        passed++;
                        Console.WriteLine("RESULT: PASS | " + Path.GetFileName(fixture));
                        Console.WriteLine("  Holes=" + result.Candidate.LogicalHoles +
                                          ", Stacks=" + result.Candidate.StackCount +
                                          ", NG=" + result.Candidate.NgStacks +
                                          ", PatternNG=" + result.Candidate.PatternNg +
                                          ", Missing=" + result.Candidate.MissingHoles);
                        Console.WriteLine("  CG1: Groups=" + result.Candidate.Cg1Groups +
                                          ", Unmatched=" + result.Candidate.Cg1Unmatched +
                                          ", Status=" + result.Candidate.Cg1CoverageStatus);
                        Console.WriteLine("  CG2: Bounds=" + result.Candidate.Cg2Bounds +
                                          ", NoHoles=" + result.Candidate.Cg2NoHole +
                                          ", NearbyPairs=" + result.Candidate.Cg2Pairs +
                                          ", Candidates=" + result.Candidate.Cg2Candidates +
                                          ", Status=" + result.Candidate.Cg2CoverageStatus);
                    }
                    else
                    {
                        failed++;
                        Console.WriteLine("RESULT: FAIL | " + Path.GetFileName(fixture) + " | " + result.Error);
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
            ISldWorks app, Type baselineType, Type candidateType, string root, string model)
        {
            var response = new ModelComparison { Model = Path.GetFileName(model) };
            try
            {
                Metrics baseline = RunOneIsolated(app, baselineType, root, model, isCandidate: false);
                Metrics candidate = RunOneIsolated(app, candidateType, root, model, isCandidate: true);
                CompareMetrics(baseline, candidate, model);
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
                RowSignature = ReportRowSignature(t, report)
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
                m.Cg2IsPartial = (bool)pt.GetField("IsPartial").GetValue(partRel);
                m.Cg2CoverageStatus = (string)pt.GetField("CoverageStatus").GetValue(partRel);
            }

            return m;
        }

        private static int IntField(Type type, object obj, string name)
        {
            FieldInfo field = type.GetField(name);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return (int)field.GetValue(obj);
        }

        private static string ReportRowSignature(Type t, object report)
        {
            FieldInfo field = t.GetField("Rows");
            if (field == null) throw new MissingFieldException(t.FullName, "Rows");
            IEnumerable rows = field.GetValue(report) as IEnumerable;
            if (rows == null) throw new InvalidOperationException("Report Rows unavailable.");
            var list = new List<string>();
            foreach (object row in rows)
            {
                Type rt = row.GetType();
                var sb = new StringBuilder();
                PropertyInfo pRef = rt.GetProperty("ReferenceComponent", BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo pAct = rt.GetProperty("ActualComponent", BindingFlags.Public | BindingFlags.Instance);
                string refComp = (pRef != null ? pRef.GetValue(row, null) as string : null) ?? "";
                string actComp = (pAct != null ? pAct.GetValue(row, null) as string : null) ?? "";
                if (string.CompareOrdinal(refComp, actComp) > 0)
                {
                    string tmp = refComp; refComp = actComp; actComp = tmp;
                }
                foreach (string n in new[] { "Status", "Kind" })
                {
                    PropertyInfo rp = rt.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                    object value = rp.GetValue(row, null);
                    sb.Append(value == null ? "" : value.ToString()).Append("\u001F");
                }
                sb.Append(refComp).Append("\u001F").Append(actComp);
                list.Add(sb.ToString());
            }
            list.Sort(StringComparer.Ordinal);
            return string.Join("\n", list);
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

            if (!string.Equals(a.Configuration, b.Configuration, StringComparison.Ordinal))
                throw new InvalidOperationException("Configuration changed: " + a.Configuration + " vs " + b.Configuration);

            if (!string.Equals(a.RowSignature, b.RowSignature, StringComparison.Ordinal))
                throw new InvalidOperationException("Row status/kind/pair signatures differ on " + model);

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
