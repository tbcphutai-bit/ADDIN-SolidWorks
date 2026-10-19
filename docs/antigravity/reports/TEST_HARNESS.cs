// CG1 Diagnostic Test Harness
// Standalone regression and verification harness for CheckHole Connection Groups (CG1).
// Run with:
//   csc /r:"C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll" /out:TEST_HARNESS.exe TEST_HARNESS.cs
//   .\TEST_HARNESS.exe

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;

namespace ADDIN.Tests
{
    public class ConnectionGroupsTestHarness
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Console.WriteLine("===============================================================");
            Console.WriteLine("ANTIGRAVITY CHECK HOLE CG1 — REPRODUCIBLE TEST HARNESS");
            Console.WriteLine("Timestamp: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));
            Console.WriteLine("===============================================================");

            string addinDllPath = @"C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll";
            if (args.Length > 0 && !string.IsNullOrEmpty(args[0]))
            {
                addinDllPath = args[0];
            }

            Console.WriteLine("Target Assembly: " + addinDllPath);
            Assembly asm = Assembly.LoadFrom(addinDllPath);
            Console.WriteLine("Loaded Assembly: " + asm.FullName);

            int failures = 0;

            failures += RunEngineTests(asm);
            failures += RunWinFormsUITests(asm);

            Console.WriteLine("===============================================================");
            if (failures == 0)
            {
                Console.WriteLine("RESULT: ALL SYNTHETIC AND WINFORMS TESTS PASSED (0 FAILURES)");
                return 0;
            }
            else
            {
                Console.WriteLine("RESULT: FAILED WITH " + failures + " ERROR(S)");
                return 1;
            }
        }

        private static int RunEngineTests(Assembly asm)
        {
            Console.WriteLine("\n--- 1. Testing CheckAssemblyHole.BuildConnectionCoverage ---");
            int errors = 0;

            Type checkType = asm.GetType("ADDIN.Commands.CheckAssemblyHole");
            MethodInfo buildMethod = checkType.GetMethod("BuildConnectionCoverage", BindingFlags.NonPublic | BindingFlags.Static);
            if (buildMethod == null)
            {
                Console.WriteLine("FAIL: BuildConnectionCoverage method not found on CheckAssemblyHole");
                return 1;
            }

            Type holeAxisType = asm.GetType("ADDIN.Commands.CheckAssemblyHole+HoleAxis");
            Type holeStackType = asm.GetType("ADDIN.Commands.CheckAssemblyHole+HoleStack");
            Type stackResultType = asm.GetType("ADDIN.Commands.CheckAssemblyHole+HoleStackResult");

            Func<string, double[], double[], object> createHole = (comp, center, dir) =>
            {
                object h = Activator.CreateInstance(holeAxisType);
                holeAxisType.GetField("ComponentOccurrence").SetValue(h, comp);
                holeAxisType.GetField("Center").SetValue(h, center);
                holeAxisType.GetField("Direction").SetValue(h, dir);
                return h;
            };

            Func<List<object>, object, bool, double, double, object> createResult = (members, reference, isNg, maxOffsetM, maxAngleDeg) =>
            {
                object stack = Activator.CreateInstance(holeStackType);
                IList stackHoles = (IList)holeStackType.GetField("Holes").GetValue(stack);
                foreach (var m in members) stackHoles.Add(m);

                object res = Activator.CreateInstance(stackResultType);
                stackResultType.GetField("Stack").SetValue(res, stack);
                stackResultType.GetField("Reference").SetValue(res, reference);
                stackResultType.GetField("IsNg").SetValue(res, isNg);
                stackResultType.GetField("MaxOffsetM").SetValue(res, maxOffsetM);
                stackResultType.GetField("MaxAngleDeg").SetValue(res, maxAngleDeg);
                return res;
            };

            // Case E01: Null arguments handling
            try
            {
                buildMethod.Invoke(null, new object[] { null, null });
                Console.WriteLine("FAIL [E01]: Expected TargetInvocationException wrapping ArgumentNullException");
                errors++;
            }
            catch (TargetInvocationException ex)
            {
                if (ex.InnerException is ArgumentNullException)
                {
                    Console.WriteLine("PASS [E01]: Null arguments correctly throw ArgumentNullException");
                }
                else
                {
                    Console.WriteLine("FAIL [E01]: Expected ArgumentNullException, got: " + ex.InnerException);
                    errors++;
                }
            }

            // Case E02: Empty input
            {
                var emptyHoles = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(holeAxisType));
                var emptyResults = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(stackResultType));
                object cov = buildMethod.Invoke(null, new object[] { emptyHoles, emptyResults });
                string status = (string)cov.GetType().GetField("CoverageStatus").GetValue(cov);
                int groupCount = (int)cov.GetType().GetField("GroupCount").GetValue(cov);
                if (status == "KHONG_DOC_DUOC_LO_HOAC_MODEL_KHONG_CO_LO" && groupCount == 0)
                {
                    Console.WriteLine("PASS [E02]: Empty input returns correct empty status");
                }
                else
                {
                    Console.WriteLine("FAIL [E02]: Empty input returned status: " + status);
                    errors++;
                }
            }

            // Case E03 (corresponds to T01): Two concentric round holes (OK)
            {
                var h1 = createHole("PlateA-1", new double[] { 0, 0, 0 }, new double[] { 0, 0, 1 });
                var h2 = createHole("PlateB-1", new double[] { 0, 0, 0.010 }, new double[] { 0, 0, 1 });
                var holes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(holeAxisType));
                holes.Add(h1); holes.Add(h2);
                var results = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(stackResultType));
                results.Add(createResult(new List<object> { h1, h2 }, h1, false, 0.0, 0.0));

                object cov = buildMethod.Invoke(null, new object[] { holes, results });
                IList groups = (IList)cov.GetType().GetField("Groups").GetValue(cov);
                object g = groups[0];
                string st = (string)g.GetType().GetProperty("Status").GetValue(g, null);
                string md = (string)g.GetType().GetProperty("MasterDecision").GetValue(g, null);
                string ec = (string)g.GetType().GetProperty("EndCandidates").GetValue(g, null);
                string cp = (string)g.GetType().GetProperty("Components").GetValue(g, null);

                if (st == "OK" && md == "KHONG_CAN_MASTER_DE_CHECK_DONG_TAM" && ec == "PlateA-1 | PlateB-1" && cp == "PlateA-1 / PlateB-1")
                {
                    Console.WriteLine("PASS [E03/T01]: Two concentric holes diagnosed OK with unassigned master");
                }
                else
                {
                    Console.WriteLine("FAIL [E03/T01]: Expected OK, got st=" + st + ", md=" + md + ", ec=" + ec);
                    errors++;
                }
            }

            // Case E04 (corresponds to T02): Offset 0.09 mm (OK within 0.10 mm tolerance)
            {
                var h1 = createHole("PartA-1", new double[] { 0, 0, 0 }, new double[] { 0, 0, 1 });
                var h2 = createHole("PartB-1", new double[] { 0.00009, 0, 0.010 }, new double[] { 0, 0, 1 });
                var holes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(holeAxisType));
                holes.Add(h1); holes.Add(h2);
                var results = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(stackResultType));
                results.Add(createResult(new List<object> { h1, h2 }, h1, false, 0.00009, 0.05));

                object cov = buildMethod.Invoke(null, new object[] { holes, results });
                IList groups = (IList)cov.GetType().GetField("Groups").GetValue(cov);
                object g = groups[0];
                double offsetMm = (double)g.GetType().GetProperty("MaxOffsetMm").GetValue(g, null);
                string st = (string)g.GetType().GetProperty("Status").GetValue(g, null);
                if (st == "OK" && Math.Abs(offsetMm - 0.09) < 1e-4)
                {
                    Console.WriteLine("PASS [E04/T02]: Offset 0.09 mm preserved as OK with MaxOffsetMm=0.09");
                }
                else
                {
                    Console.WriteLine("FAIL [E04/T02]: st=" + st + ", offset=" + offsetMm);
                    errors++;
                }
            }

            // Case E05 (corresponds to T03): Offset 0.11 mm (NG > 0.10 mm tolerance)
            {
                var h1 = createHole("PartA-1", new double[] { 0, 0, 0 }, new double[] { 0, 0, 1 });
                var h2 = createHole("PartB-1", new double[] { 0.00011, 0, 0.010 }, new double[] { 0, 0, 1 });
                var holes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(holeAxisType));
                holes.Add(h1); holes.Add(h2);
                var results = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(stackResultType));
                results.Add(createResult(new List<object> { h1, h2 }, h1, true, 0.00011, 0.05));

                object cov = buildMethod.Invoke(null, new object[] { holes, results });
                IList groups = (IList)cov.GetType().GetField("Groups").GetValue(cov);
                object g = groups[0];
                string st = (string)g.GetType().GetProperty("Status").GetValue(g, null);
                string md = (string)g.GetType().GetProperty("MasterDecision").GetValue(g, null);
                int ngNoMaster = (int)cov.GetType().GetField("NgGroupsWithoutMaster").GetValue(cov);

                if (st == "NG" && md == "CHUA_XAC_DINH_MASTER" && ngNoMaster == 1)
                {
                    Console.WriteLine("PASS [E05/T03]: Offset 0.11 mm diagnosed NG with CHUA_XAC_DINH_MASTER");
                }
                else
                {
                    Console.WriteLine("FAIL [E05/T03]: st=" + st + ", md=" + md + ", ngNoMaster=" + ngNoMaster);
                    errors++;
                }
            }

            // Case E06: Close axial projection <= 0.05 mm
            {
                var h1 = createHole("PartA-1", new double[] { 0, 0, 0 }, new double[] { 0, 0, 1 });
                var h2 = createHole("PartB-1", new double[] { 0, 0, 0.00004 }, new double[] { 0, 0, 1 });
                var holes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(holeAxisType));
                holes.Add(h1); holes.Add(h2);
                var results = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(stackResultType));
                results.Add(createResult(new List<object> { h1, h2 }, h1, false, 0.0, 0.0));

                object cov = buildMethod.Invoke(null, new object[] { holes, results });
                IList groups = (IList)cov.GetType().GetField("Groups").GetValue(cov);
                object g = groups[0];
                string ec = (string)g.GetType().GetProperty("EndCandidates").GetValue(g, null);

                if (ec == "Khong phan biet duoc hai dau truc")
                {
                    Console.WriteLine("PASS [E06]: Projection <= 0.05 mm diagnosed as 'Khong phan biet duoc hai dau truc'");
                }
                else
                {
                    Console.WriteLine("FAIL [E06]: Expected ambiguous ends, got: " + ec);
                    errors++;
                }
            }

            // Case E07 (corresponds to T05): 3-layer stack A-B-C
            {
                var hA = createHole("PartA-1", new double[] { 0, 0, 0 }, new double[] { 0, 0, 1 });
                var hB = createHole("PartB-1", new double[] { 0, 0, 0.02 }, new double[] { 0, 0, 1 });
                var hC = createHole("PartC-1", new double[] { 0, 0, 0.05 }, new double[] { 0, 0, 1 });
                var holes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(holeAxisType));
                holes.Add(hA); holes.Add(hB); holes.Add(hC);
                var results = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(stackResultType));
                results.Add(createResult(new List<object> { hA, hB, hC }, hA, false, 0.0, 0.0));

                object cov = buildMethod.Invoke(null, new object[] { holes, results });
                IList groups = (IList)cov.GetType().GetField("Groups").GetValue(cov);
                object g = groups[0];
                string ec = (string)g.GetType().GetProperty("EndCandidates").GetValue(g, null);
                string cp = (string)g.GetType().GetProperty("Components").GetValue(g, null);
                int count = (int)g.GetType().GetProperty("HoleCount").GetValue(g, null);

                if (cp == "PartA-1 / PartB-1 / PartC-1" && ec == "PartA-1 | PartC-1" && count == 3)
                {
                    Console.WriteLine("PASS [E07/T05]: 3-layer stack A-B-C diagnosed with endpoints A and C");
                }
                else
                {
                    Console.WriteLine("FAIL [E07/T05]: cp=" + cp + ", ec=" + ec + ", count=" + count);
                    errors++;
                }
            }

            // Case E08 (corresponds to T06): Independent connections A-B and B-D
            {
                var hA = createHole("PartA-1", new double[] { 0, 0, 0 }, new double[] { 0, 0, 1 });
                var hB1 = createHole("PartB-1", new double[] { 0, 0, 0.01 }, new double[] { 0, 0, 1 });
                var hB2 = createHole("PartB-1", new double[] { 0.1, 0, 0 }, new double[] { 0, 0, 1 });
                var hD = createHole("PartD-1", new double[] { 0.1, 0, 0.01 }, new double[] { 0, 0, 1 });
                var holes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(holeAxisType));
                holes.Add(hA); holes.Add(hB1); holes.Add(hB2); holes.Add(hD);
                var results = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(stackResultType));
                results.Add(createResult(new List<object> { hA, hB1 }, hA, false, 0.0, 0.0));
                results.Add(createResult(new List<object> { hB2, hD }, hB2, false, 0.0, 0.0));

                object cov = buildMethod.Invoke(null, new object[] { holes, results });
                int groupCount = (int)cov.GetType().GetField("GroupCount").GetValue(cov);
                string coverageStatus = (string)cov.GetType().GetField("CoverageStatus").GetValue(cov);

                if (groupCount == 2 && coverageStatus == "DA_GHEP_LO_NHAN_DIEN; CHUA_KIEM_TRA_PART_KHONG_CO_LO")
                {
                    Console.WriteLine("PASS [E08/T06]: Independent connections form distinct groups without global Part master");
                }
                else
                {
                    Console.WriteLine("FAIL [E08/T06]: groupCount=" + groupCount + ", status=" + coverageStatus);
                    errors++;
                }
            }

            // Case E09 (corresponds to T07): Unmatched hole present
            {
                var h1 = createHole("PartA-1", new double[] { 0, 0, 0 }, new double[] { 0, 0, 1 });
                var h2 = createHole("PartB-1", new double[] { 0, 0, 0.01 }, new double[] { 0, 0, 1 });
                var hUnmatched = createHole("PartC-1", new double[] { 0.5, 0.5, 0 }, new double[] { 0, 0, 1 });
                var holes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(holeAxisType));
                holes.Add(h1); holes.Add(h2); holes.Add(hUnmatched);
                var results = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(stackResultType));
                results.Add(createResult(new List<object> { h1, h2 }, h1, false, 0.0, 0.0));

                object cov = buildMethod.Invoke(null, new object[] { holes, results });
                int unmatched = (int)cov.GetType().GetField("UnmatchedHoleCount").GetValue(cov);
                string coverageStatus = (string)cov.GetType().GetField("CoverageStatus").GetValue(cov);

                if (unmatched == 1 && coverageStatus == "CHUA_DAY_DU: CO_LO_CHUA_GHEP")
                {
                    Console.WriteLine("PASS [E09/T07]: Unmatched hole preserved and reported in coverage status");
                }
                else
                {
                    Console.WriteLine("FAIL [E09/T07]: unmatched=" + unmatched + ", status=" + coverageStatus);
                    errors++;
                }
            }

            return errors;
        }

        private static int RunWinFormsUITests(Assembly asm)
        {
            Console.WriteLine("\n--- 2. Testing CheckHoleResultsForm and WinForms Integration ---");
            int errors = 0;

            Type formType = asm.GetType("ADDIN.Commands.CheckHoleResultsForm");
            Type reportType = asm.GetType("ADDIN.Commands.CheckHoleReport");
            Type covType = asm.GetType("ADDIN.Commands.CheckHoleConnectionCoverage");
            Type reportRowType = asm.GetType("ADDIN.Commands.CheckHoleReportRow");

            // UI 01: CheckHoleReport.Summary format test
            {
                object rep = Activator.CreateInstance(reportType);
                reportType.GetField("AssemblyTitle").SetValue(rep, "AssyTest");
                reportType.GetField("Configuration").SetValue(rep, "Default");
                reportType.GetField("LogicalHoles").SetValue(rep, 6);
                reportType.GetField("GroupedHoles").SetValue(rep, 4);
                reportType.GetField("UnmatchedHoles").SetValue(rep, 2);
                reportType.GetField("StackCount").SetValue(rep, 2);
                reportType.GetField("NgStacks").SetValue(rep, 1);
                reportType.GetField("MissingHoles").SetValue(rep, 0);

                object cov = Activator.CreateInstance(covType);
                covType.GetField("GroupCount").SetValue(cov, 2);
                covType.GetField("NgGroupsWithoutMaster").SetValue(cov, 1);
                covType.GetField("UnmatchedHoleCount").SetValue(cov, 2);
                covType.GetField("CoverageStatus").SetValue(cov, "CHUA_DAY_DU: CO_LO_CHUA_GHEP");
                reportType.GetField("ConnectionCoverage").SetValue(rep, cov);

                string sum = (string)reportType.GetProperty("Summary").GetValue(rep, null);
                if (sum.Contains("Nhóm lỗ đã ghép (chẩn đoán): 2") &&
                    sum.Contains("NG chưa rõ Master: 1") &&
                    sum.Contains("Lỗ chưa ghép: 2") &&
                    sum.Contains("CHUA_DAY_DU: CO_LO_CHUA_GHEP"))
                {
                    Console.WriteLine("PASS [UI01]: CheckHoleReport.Summary correctly formats diagnostic connection coverage");
                }
                else
                {
                    Console.WriteLine("FAIL [UI01]: Summary string mismatch:\n" + sum);
                    errors++;
                }
            }

            // UI 02: CheckHoleResultsForm instantiation and button state flow
            {
                ConstructorInfo[] ctors = formType.GetConstructors();
                ParameterInfo[] pars = ctors[0].GetParameters();

                Action<bool, bool> actRun = (diag, auto) => { };
                Action actCancel = () => { };
                MethodInfo dummyLocateMethod = typeof(ConnectionGroupsTestHarness)
                    .GetMethod("DummyLocate", BindingFlags.Static | BindingFlags.NonPublic)
                    .MakeGenericMethod(reportRowType);
                object actLocate = Delegate.CreateDelegate(pars[2].ParameterType, dummyLocateMethod);
                Action<IList<int>> actMark = (ids) => { };

                Form form = (Form)ctors[0].Invoke(new object[] { actRun, actCancel, actLocate, actMark });
                FieldInfo btnField = formType.GetField("connectionGroups", BindingFlags.NonPublic | BindingFlags.Instance);
                Button btn = (Button)btnField.GetValue(form);

                if (btn.Text != "Nhóm liên kết")
                {
                    Console.WriteLine("FAIL [UI02]: Button text expected 'Nhóm liên kết', got: " + btn.Text);
                    errors++;
                }
                else if (btn.Enabled)
                {
                    Console.WriteLine("FAIL [UI02]: Button should initially be disabled");
                    errors++;
                }
                else
                {
                    Console.WriteLine("PASS [UI02]: Button created with text 'Nhóm liên kết' and initial state disabled");
                }

                // Feed report
                object rep = Activator.CreateInstance(reportType);
                object cov = Activator.CreateInstance(covType);
                reportType.GetField("ConnectionCoverage").SetValue(rep, cov);
                MethodInfo setReportMethod = formType.GetMethod("SetReport");
                setReportMethod.Invoke(form, new object[] { rep });

                if (!btn.Enabled)
                {
                    Console.WriteLine("FAIL [UI03]: Button should become enabled after SetReport");
                    errors++;
                }
                else
                {
                    Console.WriteLine("PASS [UI03]: Button enabled after valid report with ConnectionCoverage is set");
                }

                // Busy state
                MethodInfo setBusyMethod = formType.GetMethod("SetBusy");
                setBusyMethod.Invoke(form, new object[] { true, "Running..." });
                if (btn.Enabled)
                {
                    Console.WriteLine("FAIL [UI04]: Button should be disabled when busy=true");
                    errors++;
                }
                else
                {
                    Console.WriteLine("PASS [UI04]: Button disabled during busy state");
                }

                setBusyMethod.Invoke(form, new object[] { false, "" });
                if (!btn.Enabled)
                {
                    Console.WriteLine("FAIL [UI05]: Button should re-enable when busy=false");
                    errors++;
                }
                else
                {
                    Console.WriteLine("PASS [UI05]: Button re-enabled when busy=false");
                }

                form.Dispose();
            }

            return errors;
        }

        private static void DummyLocate<T>(T row) { }
    }
}
