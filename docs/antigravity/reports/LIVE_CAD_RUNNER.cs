// Live SOLIDWORKS CAD regression and benchmark runner for CheckAssemblyHole (CG1).
// Run with:
//   csc /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll" /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.swconst.dll" /out:LIVE_CAD_RUNNER.exe LIVE_CAD_RUNNER.cs
//   .\LIVE_CAD_RUNNER.exe

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Tests
{
    public class LiveCadRunner
    {
        public class CheckMetrics
        {
            public int LogicalHoles;
            public int GroupedHoles;
            public int UnmatchedHoles;
            public int StackCount;
            public int NgStacks;
            public int PatternNg;
            public int MissingHoles;
            public bool HasCg1;
            public int Cg1Groups;
            public string Cg1CoverageStatus;
            public int Cg1NgWithoutMaster;
            public int Cg1Unmatched;
        }

        public class TestCaseResult
        {
            public string ModelName;
            public string ModelPath;
            public string Status; // PASS, FAIL, SKIP
            public string ErrorMessage;
            public CheckMetrics BaselineMetrics;
            public CheckMetrics Cg1Metrics;
            public bool HashVerified;
        }

        private static string s_baselineDllPath = @"C:\Tao moi\ADDIN_MAIN_BASELINE\bin\x64\Release\ADDIN.dll";
        private static string s_cg1DllPath = @"C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll";

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length >= 1 && !string.IsNullOrEmpty(args[0])) s_baselineDllPath = args[0];
            if (args.Length >= 2 && !string.IsNullOrEmpty(args[1])) s_cg1DllPath = args[1];

            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssemblies;
            return RunApp(args);
        }

        private static Assembly ResolveAssemblies(object sender, ResolveEventArgs ev)
        {
            string reqName = new AssemblyName(ev.Name).Name;
            if (reqName.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase))
            {
                string rootPath = Path.Combine(@"C:\Tao moi", reqName + ".dll");
                if (File.Exists(rootPath)) return Assembly.LoadFrom(rootPath);
                string swPath = Path.Combine(@"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS", reqName + ".dll");
                if (File.Exists(swPath)) return Assembly.LoadFrom(swPath);
            }
            string cg1Dir = Path.GetDirectoryName(s_cg1DllPath);
            string cand1 = Path.Combine(cg1Dir, reqName + ".dll");
            if (File.Exists(cand1)) return Assembly.LoadFrom(cand1);
            string cand2 = Path.Combine(@"C:\Tao moi", reqName + ".dll");
            if (File.Exists(cand2)) return Assembly.LoadFrom(cand2);
            return null;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int RunApp(string[] args)
        {
            Console.WriteLine("===============================================================================");
            Console.WriteLine("ANTIGRAVITY CHECK HOLE CG1 — LIVE SOLIDWORKS REGRESSION & BENCHMARK RUNNER");
            Console.WriteLine("Timestamp (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));
            Console.WriteLine("===============================================================================");

            string baselineDllPath = s_baselineDllPath;
            string cg1DllPath = s_cg1DllPath;

            Console.WriteLine("[SETUP] Baseline DLL:  " + baselineDllPath);
            Console.WriteLine("[SETUP] CG1 Target DLL: " + cg1DllPath);

            if (!File.Exists(baselineDllPath))
            {
                Console.WriteLine("[FATAL] Baseline DLL not found: " + baselineDllPath);
                return 1;
            }
            if (!File.Exists(cg1DllPath))
            {
                Console.WriteLine("[FATAL] CG1 Target DLL not found: " + cg1DllPath);
                return 1;
            }

            // Inspect and display DLL hashes and MVIDs
            Assembly asmBaseline = Assembly.Load(File.ReadAllBytes(baselineDllPath));
            Assembly asmCg1 = Assembly.Load(File.ReadAllBytes(cg1DllPath));

            string baseHash = ComputeSha256(baselineDllPath);
            string cg1Hash = ComputeSha256(cg1DllPath);

            Console.WriteLine(string.Format("[SETUP] Baseline MVID: {0} | SHA256: {1}", asmBaseline.ManifestModule.ModuleVersionId, baseHash));
            Console.WriteLine(string.Format("[SETUP] CG1      MVID: {0} | SHA256: {1}", asmCg1.ManifestModule.ModuleVersionId, cg1Hash));

            Type typeBaseline = asmBaseline.GetType("ADDIN.Commands.CheckAssemblyHole");
            Type typeCg1 = asmCg1.GetType("ADDIN.Commands.CheckAssemblyHole");

            if (typeBaseline == null) { Console.WriteLine("[FATAL] Type CheckAssemblyHole missing in baseline DLL"); return 1; }
            if (typeCg1 == null) { Console.WriteLine("[FATAL] Type CheckAssemblyHole missing in CG1 DLL"); return 1; }

            // SOLIDWORKS session safety
            bool swAlreadyRunning = Process.GetProcessesByName("SLDWORKS").Length > 0;
            ISldWorks app = null;

            if (swAlreadyRunning)
            {
                Console.WriteLine("[SAFETY] Pre-existing SOLIDWORKS session detected. Attaching to ROT/ProgID without closing user session.");
                try { app = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
                catch { app = (ISldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application")); }
            }
            else
            {
                Console.WriteLine("[SAFETY] No active SOLIDWORKS session. Launching a dedicated runner instance.");
                app = (ISldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
                app.Visible = false;
            }

            if (app == null)
            {
                Console.WriteLine("[FATAL] Unable to connect to or instantiate SOLIDWORKS.");
                return 1;
            }

            Console.WriteLine("[SOLIDWORKS] Connected version: " + app.RevisionNumber());

            var testModels = new List<string>
            {
                @"C:\Users\SGN26\Documents\addin\work\missing_ura_test_20261004\cases\run07\run07_Missing_Ura_BOM.SLDASM",
                @"C:\Users\SGN26\Documents\addin\work\same_part_1618_20261010\all-models\1618-01-BH.SLDASM"
            };

            var results = new List<TestCaseResult>();
            int passCount = 0;
            int failCount = 0;
            int skipCount = 0;

            try
            {
                foreach (string modelPath in testModels)
                {
                    TestCaseResult res = RunComparisonTest(app, typeBaseline, typeCg1, modelPath);
                    results.Add(res);
                    if (res.Status == "PASS") passCount++;
                    else if (res.Status == "FAIL") failCount++;
                    else if (res.Status == "SKIP") skipCount++;
                }

                // Print comprehensive markdown report
                Console.WriteLine("\n===============================================================================");
                Console.WriteLine("REGRESSION AND BENCHMARK RESULTS TABLE");
                Console.WriteLine("===============================================================================");
                Console.WriteLine("| Model | Status | Logical | Grouped | Unmatched | Stacks | NG Stacks | Pattern/Miss | CG1 Groups | Coverage Status | SHA Match |");
                Console.WriteLine("| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");

                foreach (var r in results)
                {
                    if (r.Status == "SKIP")
                    {
                        Console.WriteLine(string.Format("| {0} | **SKIP** | N/A | N/A | N/A | N/A | N/A | N/A | N/A | {1} | N/A |",
                            r.ModelName, r.ErrorMessage));
                    }
                    else if (r.Status == "FAIL")
                    {
                        Console.WriteLine(string.Format("| {0} | **FAIL** | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} |",
                            r.ModelName,
                            r.Cg1Metrics != null ? r.Cg1Metrics.LogicalHoles.ToString() : "ERR",
                            r.Cg1Metrics != null ? r.Cg1Metrics.GroupedHoles.ToString() : "ERR",
                            r.Cg1Metrics != null ? r.Cg1Metrics.UnmatchedHoles.ToString() : "ERR",
                            r.Cg1Metrics != null ? r.Cg1Metrics.StackCount.ToString() : "ERR",
                            r.Cg1Metrics != null ? r.Cg1Metrics.NgStacks.ToString() : "ERR",
                            r.Cg1Metrics != null ? (r.Cg1Metrics.PatternNg + "/" + r.Cg1Metrics.MissingHoles) : "ERR",
                            r.Cg1Metrics != null && r.Cg1Metrics.HasCg1 ? r.Cg1Metrics.Cg1Groups.ToString() : "N/A",
                            r.ErrorMessage,
                            r.HashVerified ? "YES" : "NO"));
                    }
                    else
                    {
                        Console.WriteLine(string.Format("| {0} | **PASS** | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} |",
                            r.ModelName,
                            r.Cg1Metrics.LogicalHoles,
                            r.Cg1Metrics.GroupedHoles,
                            r.Cg1Metrics.UnmatchedHoles,
                            r.Cg1Metrics.StackCount,
                            r.Cg1Metrics.NgStacks,
                            r.Cg1Metrics.PatternNg + "/" + r.Cg1Metrics.MissingHoles,
                            r.Cg1Metrics.Cg1Groups,
                            r.Cg1Metrics.Cg1CoverageStatus,
                            r.HashVerified ? "YES" : "NO"));
                    }
                }

                Console.WriteLine("\nSUMMARY: Total=" + results.Count + " | PASS=" + passCount + " | FAIL=" + failCount + " | SKIP=" + skipCount);

                if (failCount > 0 || passCount == 0)
                {
                    Console.WriteLine("TEST OUTCOME: FAILED (Exit Code 1)");
                    return 1;
                }
                else
                {
                    Console.WriteLine("TEST OUTCOME: SUCCEEDED (Exit Code 0)");
                    return 0;
                }
            }
            finally
            {
                if (app != null)
                {
                    if (!swAlreadyRunning)
                    {
                        Console.WriteLine("[SAFETY] Closing temporary SOLIDWORKS runner session.");
                        try { app.ExitApp(); } catch { }
                    }
                    else
                    {
                        Console.WriteLine("[SAFETY] Preserving user's pre-existing SOLIDWORKS session intact.");
                    }
                }
            }
        }

        private static TestCaseResult RunComparisonTest(ISldWorks app, Type typeBaseline, Type typeCg1, string modelPath)
        {
            var res = new TestCaseResult
            {
                ModelName = Path.GetFileName(modelPath),
                ModelPath = modelPath
            };

            Console.WriteLine("\n-------------------------------------------------------------------------------");
            Console.WriteLine("Evaluating: " + res.ModelName);
            Console.WriteLine("Path:       " + modelPath);

            if (!File.Exists(modelPath))
            {
                Console.WriteLine("STATUS: [SKIP] File does not exist on disk.");
                res.Status = "SKIP";
                res.ErrorMessage = "File not found";
                return res;
            }

            string hashBefore = ComputeSha256(modelPath);

            // Open model in Silent and Read-Only mode to protect CAD data
            int errors = 0, warnings = 0;
            int openOptions = (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_ReadOnly);
            ModelDoc2 doc = app.OpenDoc6(modelPath, (int)swDocumentTypes_e.swDocASSEMBLY, openOptions, "", ref errors, ref warnings);

            if (doc == null)
            {
                Console.WriteLine(string.Format("STATUS: [FAIL] OpenDoc6 failed (errors={0}, warnings={1})", errors, warnings));
                res.Status = "FAIL";
                res.ErrorMessage = "OpenDoc6 failed (errors=" + errors + ", warnings=" + warnings + ")";
                return res;
            }

            try
            {
                // 1. Run Baseline (main)
                Console.WriteLine("-> Running Baseline (main branch code)...");
                res.BaselineMetrics = RunChecker(typeBaseline, app);
                Console.WriteLine(string.Format("   [Baseline] Logical={0}, Grouped={1}, Unmatched={2}, Stacks={3}, NG={4}, PatNG={5}, Miss={6}",
                    res.BaselineMetrics.LogicalHoles, res.BaselineMetrics.GroupedHoles, res.BaselineMetrics.UnmatchedHoles,
                    res.BaselineMetrics.StackCount, res.BaselineMetrics.NgStacks,
                    res.BaselineMetrics.PatternNg, res.BaselineMetrics.MissingHoles));

                // 2. Run CG1 Candidate
                Console.WriteLine("-> Running CG1 Candidate (feat/check-hole-connection-groups-cg1)...");
                res.Cg1Metrics = RunChecker(typeCg1, app);
                Console.WriteLine(string.Format("   [CG1 Target] Logical={0}, Grouped={1}, Unmatched={2}, Stacks={3}, NG={4}, PatNG={5}, Miss={6}",
                    res.Cg1Metrics.LogicalHoles, res.Cg1Metrics.GroupedHoles, res.Cg1Metrics.UnmatchedHoles,
                    res.Cg1Metrics.StackCount, res.Cg1Metrics.NgStacks,
                    res.Cg1Metrics.PatternNg, res.Cg1Metrics.MissingHoles));
                Console.WriteLine(string.Format("   [CG1 Diag]   Groups={0}, CoverageStatus={1}, NgNoMaster={2}, UnmatchedHoles={3}",
                    res.Cg1Metrics.Cg1Groups, res.Cg1Metrics.Cg1CoverageStatus,
                    res.Cg1Metrics.Cg1NgWithoutMaster, res.Cg1Metrics.Cg1Unmatched));

                // 3. Regression Comparisons
                var errorsList = new List<string>();

                if (res.BaselineMetrics.LogicalHoles != res.Cg1Metrics.LogicalHoles)
                    errorsList.Add(string.Format("LogicalHoles mismatch: baseline={0}, cg1={1}", res.BaselineMetrics.LogicalHoles, res.Cg1Metrics.LogicalHoles));

                if (res.BaselineMetrics.GroupedHoles != res.Cg1Metrics.GroupedHoles)
                    errorsList.Add(string.Format("GroupedHoles mismatch: baseline={0}, cg1={1}", res.BaselineMetrics.GroupedHoles, res.Cg1Metrics.GroupedHoles));

                if (res.BaselineMetrics.UnmatchedHoles != res.Cg1Metrics.UnmatchedHoles)
                    errorsList.Add(string.Format("UnmatchedHoles mismatch: baseline={0}, cg1={1}", res.BaselineMetrics.UnmatchedHoles, res.Cg1Metrics.UnmatchedHoles));

                if (res.BaselineMetrics.StackCount != res.Cg1Metrics.StackCount)
                    errorsList.Add(string.Format("StackCount mismatch: baseline={0}, cg1={1}", res.BaselineMetrics.StackCount, res.Cg1Metrics.StackCount));

                if (res.BaselineMetrics.NgStacks != res.Cg1Metrics.NgStacks)
                    errorsList.Add(string.Format("NgStacks mismatch: baseline={0}, cg1={1}", res.BaselineMetrics.NgStacks, res.Cg1Metrics.NgStacks));

                if (res.BaselineMetrics.PatternNg != res.Cg1Metrics.PatternNg)
                    errorsList.Add(string.Format("PatternNg mismatch: baseline={0}, cg1={1}", res.BaselineMetrics.PatternNg, res.Cg1Metrics.PatternNg));

                if (res.BaselineMetrics.MissingHoles != res.Cg1Metrics.MissingHoles)
                    errorsList.Add(string.Format("MissingHoles mismatch: baseline={0}, cg1={1}", res.BaselineMetrics.MissingHoles, res.Cg1Metrics.MissingHoles));

                // 4. CG1 Invariant Assertions
                if (!res.Cg1Metrics.HasCg1)
                    errorsList.Add("CG1 ConnectionCoverage is null");

                if (res.Cg1Metrics.Cg1Groups != res.Cg1Metrics.StackCount)
                    errorsList.Add(string.Format("CG1 GroupCount ({0}) != StackCount ({1})", res.Cg1Metrics.Cg1Groups, res.Cg1Metrics.StackCount));

                if (res.Cg1Metrics.Cg1Unmatched != res.Cg1Metrics.UnmatchedHoles)
                    errorsList.Add(string.Format("CG1 UnmatchedHoleCount ({0}) != UnmatchedHoles ({1})", res.Cg1Metrics.Cg1Unmatched, res.Cg1Metrics.UnmatchedHoles));

                // 5. Specific Boundary Assertions
                if (res.ModelName.IndexOf("run07", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (res.Cg1Metrics.LogicalHoles != 0) errorsList.Add("run07 LogicalHoles expected 0, got " + res.Cg1Metrics.LogicalHoles);
                    if (res.Cg1Metrics.StackCount != 0) errorsList.Add("run07 StackCount expected 0, got " + res.Cg1Metrics.StackCount);
                    if (res.Cg1Metrics.Cg1Groups != 0) errorsList.Add("run07 CG1 Groups expected 0, got " + res.Cg1Metrics.Cg1Groups);
                    if (res.Cg1Metrics.Cg1CoverageStatus != "KHONG_DOC_DUOC_LO_HOAC_MODEL_KHONG_CO_LO")
                        errorsList.Add("run07 CoverageStatus expected KHONG_DOC_DUOC_LO_HOAC_MODEL_KHONG_CO_LO, got " + res.Cg1Metrics.Cg1CoverageStatus);
                }
                else if (res.ModelName.IndexOf("1618-01-BH", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (res.Cg1Metrics.LogicalHoles != 916) errorsList.Add("1618-01-BH LogicalHoles expected 916, got " + res.Cg1Metrics.LogicalHoles);
                    if (res.Cg1Metrics.GroupedHoles != 696) errorsList.Add("1618-01-BH GroupedHoles expected 696, got " + res.Cg1Metrics.GroupedHoles);
                    if (res.Cg1Metrics.UnmatchedHoles != 220) errorsList.Add("1618-01-BH UnmatchedHoles expected 220, got " + res.Cg1Metrics.UnmatchedHoles);
                    if (res.Cg1Metrics.StackCount != 348) errorsList.Add("1618-01-BH StackCount expected 348, got " + res.Cg1Metrics.StackCount);
                    if (res.Cg1Metrics.Cg1Groups != 348) errorsList.Add("1618-01-BH CG1 Groups expected 348, got " + res.Cg1Metrics.Cg1Groups);
                    if (res.Cg1Metrics.Cg1CoverageStatus != "CHUA_DAY_DU: CO_LO_CHUA_GHEP") errorsList.Add("1618-01-BH CoverageStatus expected CHUA_DAY_DU: CO_LO_CHUA_GHEP, got " + res.Cg1Metrics.Cg1CoverageStatus);
                }

                if (errorsList.Count > 0)
                {
                    res.Status = "FAIL";
                    res.ErrorMessage = string.Join("; ", errorsList.ToArray());
                    Console.WriteLine("STATUS: [FAIL] Assertions failed: " + res.ErrorMessage);
                }
                else
                {
                    res.Status = "PASS";
                    Console.WriteLine("STATUS: [PASS] 100% equivalence between baseline and CG1. All invariants satisfied.");
                }
            }
            catch (Exception ex)
            {
                res.Status = "FAIL";
                res.ErrorMessage = "Exception during check: " + ex.Message;
                Console.WriteLine("STATUS: [FAIL] " + ex);
            }
            finally
            {
                // Close doc without saving
                app.CloseDoc(doc.GetTitle());

                string hashAfter = ComputeSha256(modelPath);
                res.HashVerified = (hashBefore == hashAfter);
                if (!res.HashVerified)
                {
                    res.Status = "FAIL";
                    res.ErrorMessage = (res.ErrorMessage != null ? res.ErrorMessage + "; " : "") + "CRITICAL: Assembly file hash changed after test!";
                    Console.WriteLine("STATUS: [CRITICAL FAIL] Original model file on disk was modified!");
                }
                else
                {
                    Console.WriteLine("[SAFETY VERIFIED] Original file SHA256 identical before and after test (" + hashAfter.Substring(0, 8) + "...)");
                }
            }

            return res;
        }

        private static CheckMetrics RunChecker(Type checkerType, ISldWorks app)
        {
            object checker = Activator.CreateInstance(checkerType, app);

            // Safety: disable AutoCreateMarkers
            PropertyInfo propMarkers = checkerType.GetProperty("AutoCreateMarkers");
            if (propMarkers != null) propMarkers.SetValue(checker, false, null);

            MethodInfo runMethod = checkerType.GetMethod("Run");
            runMethod.Invoke(checker, null);

            FieldInfo reportField = checkerType.GetField("lastReport", BindingFlags.NonPublic | BindingFlags.Instance);
            object report = reportField.GetValue(checker);
            if (report == null) throw new InvalidOperationException("CheckAssemblyHole lastReport was null.");

            Type repType = report.GetType();
            var m = new CheckMetrics
            {
                LogicalHoles = (int)repType.GetField("LogicalHoles").GetValue(report),
                GroupedHoles = (int)repType.GetField("GroupedHoles").GetValue(report),
                UnmatchedHoles = (int)repType.GetField("UnmatchedHoles").GetValue(report),
                StackCount = (int)repType.GetField("StackCount").GetValue(report),
                NgStacks = (int)repType.GetField("NgStacks").GetValue(report),
                PatternNg = (int)repType.GetField("PatternNg").GetValue(report),
                MissingHoles = (int)repType.GetField("MissingHoles").GetValue(report)
            };

            FieldInfo covField = repType.GetField("ConnectionCoverage");
            if (covField != null)
            {
                object cov = covField.GetValue(report);
                if (cov != null)
                {
                    m.HasCg1 = true;
                    Type covType = cov.GetType();
                    m.Cg1Groups = (int)covType.GetField("GroupCount").GetValue(cov);
                    m.Cg1CoverageStatus = (string)covType.GetField("CoverageStatus").GetValue(cov);
                    m.Cg1NgWithoutMaster = (int)covType.GetField("NgGroupsWithoutMaster").GetValue(cov);
                    m.Cg1Unmatched = (int)covType.GetField("UnmatchedHoleCount").GetValue(cov);
                }
            }

            return m;
        }

        private static string ComputeSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] bytes = sha.ComputeHash(stream);
                return BitConverter.ToString(bytes).Replace("-", "");
            }
        }
    }
}
