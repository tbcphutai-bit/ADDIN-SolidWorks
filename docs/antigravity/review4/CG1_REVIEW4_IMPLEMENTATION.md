# CHECK HOLE — Review #4: C# IMPLEMENTATION FOR ANTIGRAVITY

**Repository:** `tbcphutai-bit/ADDIN-SolidWorks`  
**Branch:** `feat/check-hole-connection-groups-cg1`  
**PR:** https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/27

> THIS IS COMPLETE C# IMPLEMENTATION PROVIDED BY CHATGPT, **NOT YET VERIFIED BY A WINDOWS/CAD BUILD**.
> Anti must compile/test it and report real outcomes. Do not report PASS merely because code is published.

## 1. Scope and hard safety rules

The production CG1 engine and `CheckAssemblyHole.Run()` must NOT be modified for Review #4. The defect is in the **test runner** and its incorrect safety assumptions.

Important existing production line:

```csharp
if (!AutoCreateMarkers || markerRows.Count == 0) DeleteOldMarkers(model);
```

So `AutoCreateMarkers=false` does not stop existing marker deletion. The new runner instead:

1. Requires two real DLLs: independent `main` baseline and CG1 candidate.
2. Rejects any existing SOLIDWORKS session (fail-closed); never attaches to or closes user sessions.
3. Requires an explicit dedicated **Pack and Go sandbox** and `CG1_SANDBOX_OK.txt` acknowledgement. **Never pass the real working production .SLDASM path**.
4. Verifies all known component paths are contained inside the sandbox before running Check Hole. Unknown/virtual/external references block execution.
5. Opens test files read-only and silent in a new runner-owned SOLIDWORKS instance only.
6. Rejects a fixture with any `CHECK-HOLE-NG*` marker before `Run()` — do not bypass this check.
7. Captures assembly feature-tree signature and `GetSaveFlag()` before/after; a mutation is FAIL. SHA256 also checks on-disk file integrity.
8. Closes only the fixture document opened by the runner in its isolated session; closes the session only when the runner created it.
9. Uses separate freshly opened document instances for baseline and CG1.
10. Compares all seven production counters, configuration and a per-row result signature; validates CG1 coverage invariants.
11. `FAIL`, missing mandatory fixture, `SKIP`, or incomplete run returns nonzero exit code.
12. The source code is a reference implementation to be compiled and validated, not a declaration that the entire CG1 test gate is met.

## 2. File usage

**New full reference C# source:** `docs/antigravity/review4/CG1_REVIEW4_SAFE_RUNNER.cs`  
**This Markdown also embeds the entire source in section 5.**

The current `docs/antigravity/reports/LIVE_CAD_RUNNER.cs` remains unchanged for now. It is **unsafe to execute on working CAD**. Anti must:

1. Open `docs/antigravity/review4/CG1_REVIEW4_SAFE_RUNNER.cs` and inspect every precondition and fail-closed check.
2. Compile it as standalone .NET Framework C# 7.3 executable and run `--self-test` first.
3. If compile errors are caused by API signatures/environment, fix **the new runner only**, preserve its safety invariants, document fixes.
4. Prepare isolated fixture copies using SOLIDWORKS Pack and Go with all dependencies physically inside one new disposable directory. No original production or user-active documents.
5. Put manual acknowledgement file `CG1_SANDBOX_OK.txt` in that directory; this is not auto-created by test runner.
6. With **all SOLIDWORKS windows/processes closed**, run the safe runner and record exit code.
7. After successful validation, replace `docs/antigravity/reports/LIVE_CAD_RUNNER.cs` with the validated code. Keep the reference file here for audit; don't add it to `ADDIN.csproj`.
8. Update `CHECK_HOLE_CG1_REPORT.md`, `TEST_HARNESS_EVIDENCE.md`, build evidence and PR review comment. Never merge.

**Do not weaken the safeguards to make difficult models pass.** Instead record `NOT_TESTED` / `BLOCKED_ISOLATION` and ask for an approved disposable Pack and Go fixture.

## 3. Exact Windows build commands (example)

Run in the repository root; substitute the actual Windows sandbox and model names. Quotes required for paths with spaces.

```powershell
$sw = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
$csc = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
$src = "docs\antigravity\review4\CG1_REVIEW4_SAFE_RUNNER.cs"
$exe = "docs\antigravity\review4\CG1_REVIEW4_SAFE_RUNNER.exe"

& $csc /target:exe /r:"$sw\SolidWorks.Interop.sldworks.dll" /r:"$sw\SolidWorks.Interop.swconst.dll" /out:$exe $src
if ($LASTEXITCODE -ne 0) { throw "CG1 safe runner build failed" }

& $exe --self-test
if ($LASTEXITCODE -ne 0) { throw "Safe runner exit-policy self-test failed" }

# STOP HERE unless the full isolated Pack and Go fixture is ready.
$root = "D:\CG1-ISOLATED-TEST-COPIES"
$mainDll = "C:\Tao moi\ADDIN_MAIN_BASELINE\bin\x64\Release\ADDIN.dll"
$cg1Dll = "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll"
$zero = "$root\ZeroHole\run07_Missing_Ura_BOM.SLDASM"
$large = "$root\ProductionCopy\1618-01-BH.SLDASM"

# A human must have separately confirmed Pack and Go copied ALL dependencies.
# Do not automatically make or bypass the acknowledgement file here.
if (-not (Test-Path "$root\CG1_SANDBOX_OK.txt")) { throw "Sandbox approval missing" }

# Explicit warning: existing SOLIDWORKS sessions prevent the runner from starting.
& $exe $mainDll $cg1Dll $root $zero $large
if ($LASTEXITCODE -ne 0) { throw "CG1 CAD regression failed or incomplete" }
```

Do not commit the built EXE, SOLIDWORKS DLLs, CAD fixture copies, or private CAD paths with user identifiers. Commit only code, sanitized logs and truthful test results.

## 4. Antigravity acceptance matrix

| Requirement | Minimum accepted evidence |
|---|---|
| Compile safe runner | Real C# 7.3 compiler log, exit 0 |
| Self-test exit semantics | `--self-test` returns 0, PASS |
| Existing SOLIDWORKS session | Must refuse to run, return nonzero |
| Missing consent / file outside sandbox | Must refuse, return nonzero |
| Missing required fixture | Must refuse, return nonzero |
| Old `CHECK-HOLE-NG*` feature in fixture | Must refuse before `Run()` |
| External component dependency | Must refuse; don't write to referenced originals |
| Real baseline & CG1 | Two different MVIDs/hashes; metrics and per-row comparisons |
| Model safety | Assembly not dirty, feature signature stable, hash unchanged |
| Negative FAIL/SKIP | Incomplete run returns nonzero |
| User session | Never attach, hide, close or change user SOLIDWORKS session |
| Interactive WinForms, Sketch marker behavior, CSV | Still `NOT_TESTED` until interactive sessions with sanctioned fixture |
| T01–T10 complete fixture set | Still `NOT_TESTED` if unavailable |

**Status remains `CODE_COMPLETE=NOT_VERIFIED` and `READY_FOR_MERGE=NO` until all original CG1 acceptance gates are satisfied.**

## 5. FULL C# CODE — copy exact source if needed

> The live source of truth is `docs/antigravity/review4/CG1_REVIEW4_SAFE_RUNNER.cs` on this branch. This full duplication lets Anti read one Markdown document. If these diverge after fixes, document the diff and update both.

```csharp
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
                    FieldInfo rf = rt.GetField(n);
                    if (rf == null) throw new MissingFieldException(rt.FullName, n);
                    object value = rf.GetValue(row);
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
```
