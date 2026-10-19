// CG1 Review #4: fail-closed, isolated-fixture runner.
// C# 7.3 / .NET Framework 4.8. Do NOT include in ADDIN.csproj.
// Do NOT run on original production CAD or an open user SOLIDWORKS session.
//
// CLI:
// CG1_REVIEW4_SAFE_RUNNER.exe --self-test
// CG1_REVIEW4_SAFE_RUNNER.exe <main.dll> <cg1.dll> <sandbox-root> <zero-hole.sldasm> <production-copy.sldasm>
//
// Before running, use SOLIDWORKS Pack and Go to copy the FULL assembly plus
// ALL dependencies into <sandbox-root>; manually create a file called
// CG1_SANDBOX_OK.txt in that root to acknowledge this is disposable test data.
// NO sample models are automatically copied or modified by this program.

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
    internal static class Cg1Review4SafeRunner
    {
        private const string MarkerPrefix = "CHECK-HOLE-NG";
        private const string SandboxConsentFile = "CG1_SANDBOX_OK.txt";

        private sealed class Metrics
        {
            public int LogicalHoles, GroupedHoles, UnmatchedHoles, StackCount;
            public int NgStacks, PatternNg, MissingHoles;
            public string Configuration;
            public string RowSignature;
            public bool HasCg1;
            public int Cg1Groups, Cg1Unmatched, Cg1NgWithoutMaster;
            public string Cg1CoverageStatus;
        }

        private sealed class ModelComparison
        {
            public string Model;
            public bool Passed;
            public string Error;
        }

        private static string s_baselineDir;
        private static string s_candidateDir;

        // The assemblies are loaded from isolated byte arrays so the two ADDIN
        // binaries can coexist. Search their dependency folders deterministically.
        private static Assembly ResolveDependency(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string folder in new[] {
                s_candidateDir,
                s_baselineDir,
                @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
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
            if (args.Length == 1 && args[0] == "--self-test")
                return SelfTestExitPolicy();

            if (args.Length != 5)
            {
                Console.Error.WriteLine("Usage: runner <main.dll> <cg1.dll> <sandbox-root> <fixture1.sldasm> <fixture2.sldasm>");
                Console.Error.WriteLine("Or: runner --self-test");
                return 2;
            }

            ISldWorks app = null;
            bool ownsNewSession = false;
            try
            {
                string baselineDll = Path.GetFullPath(args[0]);
                string candidateDll = Path.GetFullPath(args[1]);
                string sandboxRoot = Path.GetFullPath(args[2]);
                var models = new[] { Path.GetFullPath(args[3]), Path.GetFullPath(args[4]) };

                ValidateSandboxInputs(baselineDll, candidateDll, sandboxRoot, models);

                s_baselineDir = Path.GetDirectoryName(baselineDll);
                s_candidateDir = Path.GetDirectoryName(candidateDll);
                AppDomain.CurrentDomain.AssemblyResolve += ResolveDependency;

                // FAIL CLOSED: do not attach to an existing user session or close
                // any document that was open prior to this run.
                if (Process.GetProcessesByName("SLDWORKS").Length != 0)
                    throw new InvalidOperationException(
                        "An existing SOLIDWORKS session is running. Close it yourself before running this isolated test. No CAD was touched.");

                // Do not use Marshal.GetActiveObject or a fallback to a user's ROT.
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
                Type cg1Type = RequireType(candidateAsm);

                if (baselineAsm.ManifestModule.ModuleVersionId ==
                    candidateAsm.ManifestModule.ModuleVersionId)
                    throw new InvalidOperationException(
                        "Baseline and candidate have the same MVID. They may be the same build.");

                Console.WriteLine("BASELINE SHA256: " + Sha256(baselineDll));
                Console.WriteLine("CANDIDATE SHA256: " + Sha256(candidateDll));

                int passed = 0, failed = 0, skipped = 0;
                foreach (string fixture in models)
                {
                    ModelComparison result = RunComparison(app, baselineType, cg1Type, sandboxRoot, fixture);
                    Console.WriteLine(result.Model + ": " + (result.Passed ? "PASS" : "FAIL") +
                        (string.IsNullOrEmpty(result.Error) ? "" : " | " + result.Error));
                    if (result.Passed) passed++; else failed++;
                }
                int code = ExitCode(passed, failed, skipped, models.Length);
                Console.WriteLine("SUMMARY: PASS=" + passed + " FAIL=" + failed +
                                  " SKIP=" + skipped + " EXIT=" + code);
                return code;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL CLOSED: " + ex);
                return 1;
            }
            finally
            {
                // Exit only when this process started a new session after
                // confirming zero pre-existing SLDWORKS processes.
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
            ISldWorks app, Type baselineType, Type cg1Type, string root, string model)
        {
            var response = new ModelComparison { Model = Path.GetFileName(model) };
            try
            {
                // IMPORTANT: baseline and CG1 use fresh OpenDoc6 instances,
                // not two runs on one potentially changed in-memory document.
                Metrics baseline = RunOneIsolated(app, baselineType, root, model, false);
                Metrics candidate = RunOneIsolated(app, cg1Type, root, model, true);
                CompareMetrics(baseline, candidate, model);
                response.Passed = true;
            }
            catch (Exception ex)
            {
                response.Passed = false;
                response.Error = ex.ToString();
            }
            return response;
        }

        private static Metrics RunOneIsolated(
            ISldWorks app, Type checkerType, string root, string modelPath, bool expectCg1)
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

            bool ownedDocument = true; // Session had no active document before OpenDoc6.
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

                // In production, AutoCreateMarkers=false STILL calls
                // DeleteOldMarkers(model). The fixture must have no markers,
                // must be read-only and must be isolated from real work.
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

                if (expectCg1 && !metrics.HasCg1)
                    throw new InvalidOperationException("CG1 ConnectionCoverage missing.");
                if (!expectCg1 && metrics.HasCg1)
                    throw new InvalidOperationException("Baseline unexpectedly contains CG1.");
                if (expectCg1)
                {
                    if (metrics.Cg1Groups != metrics.StackCount ||
                        metrics.Cg1Unmatched != metrics.UnmatchedHoles)
                        throw new InvalidOperationException("ConnectionGroup invariants failed.");
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
            var sb = new StringBuilder();
            foreach (object row in rows)
            {
                Type rt = row.GetType();
                foreach (string n in new[] { "Status", "Kind", "ReferenceComponent",
                                            "ActualComponent", "OffsetMm", "AngleDeg", "Reason" })
                {
                    // CheckHoleReportRow uses public properties, not public fields.
                    PropertyInfo rp = rt.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                    if (rp == null) throw new MissingMemberException(rt.FullName, n);
                    object value = rp.GetValue(row, null);
                    if (value is double)
                        sb.Append(((double)value).ToString("R", CultureInfo.InvariantCulture));
                    else sb.Append(value == null ? "" : value.ToString());
                    sb.Append("\u001F");
                }
                sb.Append("\n");
            }
            return sb.ToString();
        }

        private static void CompareMetrics(Metrics main, Metrics cg1, string path)
        {
            string name = Path.GetFileName(path);
            var differences = new List<string>();
            Compare(differences, "Logical", main.LogicalHoles, cg1.LogicalHoles);
            Compare(differences, "Grouped", main.GroupedHoles, cg1.GroupedHoles);
            Compare(differences, "Unmatched", main.UnmatchedHoles, cg1.UnmatchedHoles);
            Compare(differences, "Stacks", main.StackCount, cg1.StackCount);
            Compare(differences, "NG", main.NgStacks, cg1.NgStacks);
            Compare(differences, "Pattern", main.PatternNg, cg1.PatternNg);
            Compare(differences, "Missing", main.MissingHoles, cg1.MissingHoles);
            if (!string.Equals(main.Configuration, cg1.Configuration, StringComparison.Ordinal))
                differences.Add("Configuration changed");
            if (!string.Equals(main.RowSignature, cg1.RowSignature, StringComparison.Ordinal))
                differences.Add("Per-row report differs");
            if (cg1.Cg1Groups != cg1.StackCount ||
                cg1.Cg1Unmatched != cg1.UnmatchedHoles)
                differences.Add("CG1 coverage != production counters");
            Console.WriteLine(name + " main: holes=" + main.LogicalHoles +
                " groups=" + main.GroupedHoles + " unmatched=" + main.UnmatchedHoles +
                " stacks=" + main.StackCount + " NG=" + main.NgStacks);
            Console.WriteLine(name + " CG1 : holes=" + cg1.LogicalHoles +
                " groups=" + cg1.GroupedHoles + " unmatched=" + cg1.UnmatchedHoles +
                " stacks=" + cg1.StackCount + " NG=" + cg1.NgStacks);
            if (differences.Count > 0)
                throw new InvalidOperationException("Regression mismatch: " + string.Join(", ", differences));
        }

        private static void Compare(List<string> errors, string label, int a, int b)
        {
            if (a != b) errors.Add(label + " " + a + " != " + b);
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }
    }
}
