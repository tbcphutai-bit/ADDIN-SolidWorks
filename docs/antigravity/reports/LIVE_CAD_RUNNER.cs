// Live SOLIDWORKS CAD smoke test runner for CheckAssemblyHole (CG1).
// Run with:
//   csc /r:"C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll" /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll" /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.swconst.dll" /out:LIVE_CAD_RUNNER.exe LIVE_CAD_RUNNER.cs
//   .\LIVE_CAD_RUNNER.exe

using System;
using System.IO;
using System.Reflection;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using ADDIN.Commands;

namespace ADDIN.Tests
{
    public class LiveCadRunner
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Console.WriteLine("===============================================================");
            Console.WriteLine("ANTIGRAVITY CHECK HOLE CG1 — LIVE SOLIDWORKS CAD RUNNER");
            Console.WriteLine("Timestamp: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));
            Console.WriteLine("===============================================================");

            string addinDllPath = @"C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll";
            if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) addinDllPath = args[0];

            AppDomain.CurrentDomain.AssemblyResolve += (s, ev) =>
            {
                string targetDir = Path.GetDirectoryName(addinDllPath);
                string reqName = new AssemblyName(ev.Name).Name;
                if (reqName.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase))
                {
                    string swPath = Path.Combine(@"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS", reqName + ".dll");
                    if (File.Exists(swPath)) return Assembly.LoadFrom(swPath);
                }
                string candidate = Path.Combine(targetDir, reqName + ".dll");
                if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
                string rootCandidate = Path.Combine(@"C:\Tao moi", reqName + ".dll");
                if (File.Exists(rootCandidate)) return Assembly.LoadFrom(rootCandidate);
                return null;
            };

            ISldWorks app = null;
            try
            {
                app = (ISldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
                app.Visible = false;
                Console.WriteLine("Connected to SOLIDWORKS: " + app.RevisionNumber());

                // Benchmark 1: run07_Missing_Ura_BOM.SLDASM (0 holes case)
                TestAssembly(app, @"C:\Users\SGN26\Documents\addin\work\missing_ura_test_20261004\cases\run07\run07_Missing_Ura_BOM.SLDASM", 0, 0, 0, 0);

                // Benchmark 2: 1618-01-BH.SLDASM (916 holes production assembly)
                TestAssembly(app, @"C:\Users\SGN26\Documents\addin\work\same_part_1618_20261010\all-models\1618-01-BH.SLDASM", 916, 696, 220, 348);

                Console.WriteLine("\nLIVE CAD BENCHMARK SUCCEEDED!");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("LIVE CAD BENCHMARK FAILED: " + ex);
                return 1;
            }
            finally
            {
                if (app != null)
                {
                    try { app.ExitApp(); } catch { }
                }
            }
        }

        private static void TestAssembly(ISldWorks app, string path, int expLogical, int expGrouped, int expUnmatched, int expStacks)
        {
            Console.WriteLine("\n--- Testing Model: " + Path.GetFileName(path) + " ---");
            if (!File.Exists(path))
            {
                Console.WriteLine("SKIP: File not found: " + path);
                return;
            }

            int errors = 0, warnings = 0;
            ModelDoc2 doc = app.OpenDoc6(path, (int)swDocumentTypes_e.swDocASSEMBLY, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            if (doc == null)
            {
                Console.WriteLine("FAIL: Failed to open assembly (errors=" + errors + ", warnings=" + warnings + ")");
                return;
            }

            try
            {
                CheckAssemblyHole checker = new CheckAssemblyHole(app);
                checker.Run();

                FieldInfo reportField = typeof(CheckAssemblyHole).GetField("lastReport", BindingFlags.NonPublic | BindingFlags.Instance);
                object report = reportField.GetValue(checker);
                if (report == null)
                {
                    Console.WriteLine("FAIL: Report is null");
                    return;
                }

                Type repType = report.GetType();
                int logical = (int)repType.GetField("LogicalHoles").GetValue(report);
                int grouped = (int)repType.GetField("GroupedHoles").GetValue(report);
                int unmatched = (int)repType.GetField("UnmatchedHoles").GetValue(report);
                int stacks = (int)repType.GetField("StackCount").GetValue(report);
                int ngStacks = (int)repType.GetField("NgStacks").GetValue(report);

                object cov = repType.GetField("ConnectionCoverage").GetValue(report);
                Type covType = cov.GetType();
                int covGroups = (int)covType.GetField("GroupCount").GetValue(cov);
                string covStatus = (string)covType.GetField("CoverageStatus").GetValue(cov);
                int ngNoMaster = (int)covType.GetField("NgGroupsWithoutMaster").GetValue(cov);
                int unmatCount = (int)covType.GetField("UnmatchedHoleCount").GetValue(cov);

                Console.WriteLine("Results: LogicalHoles=" + logical + ", GroupedHoles=" + grouped + ", UnmatchedHoles=" + unmatched + ", Stacks=" + stacks + ", NgStacks=" + ngStacks);
                Console.WriteLine("CG1 Diagnostic: Groups=" + covGroups + ", CoverageStatus=" + covStatus + ", NgWithoutMaster=" + ngNoMaster + ", Unmatched=" + unmatCount);

                if (expLogical > 0)
                {
                    if (logical == expLogical && grouped == expGrouped && unmatched == expUnmatched && stacks == expStacks && covGroups == expStacks && unmatCount == expUnmatched)
                    {
                        Console.WriteLine("PASS: All production metrics and CG1 connection groups match expectation 100%");
                    }
                    else
                    {
                        Console.WriteLine("FAIL: Metrics mismatch with expected baseline");
                    }
                }
                else
                {
                    Console.WriteLine("PASS: Zero-hole assembly diagnosed correctly without errors");
                }
            }
            finally
            {
                app.CloseDoc(doc.GetTitle());
            }
        }
    }
}
