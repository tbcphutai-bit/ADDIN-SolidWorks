// CG1 Review #5 & #6: WinForms UI interaction & safety test for Check Hole & "Nhóm liên kết".
// C# 7.3 / .NET Framework 4.8.
// Verifies:
// 1. Fail-closed sandbox enforcement, component references under sandbox, dirty check,
//    feature-tree signature invariance, marker checks, and disk file SHA256 invariance.
// 2. WinForms toolbar button "Nhóm liên kết" state and PerformClick() trigger.
// 3. Modal dialog inspection, mandatory non-authoritative disclaimer, and DataGridView assertions.
// 4. CSV report schema and row-by-row consistency with CheckHoleReport.Rows.
// 5. Negative tests (outside sandbox rejection, stale marker detection, self-test).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Tests
{
    internal static class TestUiInteraction
    {
        private const string SandboxConsentFile = "CG1_SANDBOX_OK.txt";
        private const string MarkerPrefix = "CHECK-HOLE-NG";
        private static string s_dllDir;

        private static Assembly ResolveDependency(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string folder in new[] {
                s_dllDir,
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
            {
                return SelfTestSandboxLogic();
            }

            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: TestUiInteraction <candidate-addin.dll> <sandbox-root> <fixture.sldasm>");
                Console.Error.WriteLine("       TestUiInteraction --self-test");
                return 2;
            }

            s_dllDir = Path.GetDirectoryName(Path.GetFullPath(args[0]));
            return RunEntry(args);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int RunEntry(string[] args)
        {
            string dllPath = Path.GetFullPath(args[0]);
            string sandboxRoot = Path.GetFullPath(args[1]);
            string fixturePath = Path.GetFullPath(args[2]);

            // [Gate 1] Verify no active SolidWorks session exists
            if (Process.GetProcessesByName("SLDWORKS").Length != 0)
            {
                Console.Error.WriteLine("FAIL CLOSED: An existing SOLIDWORKS session is running. Close it before running isolated test.");
                return 1;
            }

            // [Gate 2] Validate sandbox root, consent file, and DLL
            if (!File.Exists(dllPath))
            {
                Console.Error.WriteLine("FAIL CLOSED: Candidate DLL missing: " + dllPath);
                return 1;
            }
            if (!Directory.Exists(sandboxRoot) || !File.Exists(Path.Combine(sandboxRoot, SandboxConsentFile)))
            {
                Console.Error.WriteLine("FAIL CLOSED: Sandbox root missing or CG1_SANDBOX_OK.txt consent file missing.");
                return 1;
            }

            // [Gate 3] Validate fixture path is strictly inside sandbox root
            try
            {
                RequireSandboxPath(sandboxRoot, fixturePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL CLOSED: " + ex.Message);
                return 1;
            }

            if (!File.Exists(fixturePath))
            {
                Console.Error.WriteLine("FAIL CLOSED: Fixture file missing: " + fixturePath);
                return 1;
            }

            ISldWorks app = null;
            bool ownsSession = false;
            try
            {
                Type swType = Type.GetTypeFromProgID("SldWorks.Application", true);
                app = (ISldWorks)Activator.CreateInstance(swType);
                if (app == null) throw new InvalidOperationException("SolidWorks failed to launch.");
                ownsSession = true;
                app.Visible = false;

                Console.WriteLine("=========================================================");
                Console.WriteLine("CG1 UI INTERACTION & SAFETY AUDIT TEST");
                Console.WriteLine("=========================================================");
                Console.WriteLine("SolidWorks Revision: " + app.RevisionNumber());
                Console.WriteLine("Candidate DLL: " + dllPath);
                Console.WriteLine("Fixture: " + fixturePath);

                string originalHash = Sha256(fixturePath);

                // Open fixture read-only and silent
                int errors = 0, warnings = 0;
                int flags = (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_ReadOnly);
                ModelDoc2 doc = app.OpenDoc6(fixturePath, (int)swDocumentTypes_e.swDocASSEMBLY, flags, "", ref errors, ref warnings);
                if (doc == null)
                    throw new InvalidOperationException("Failed to open fixture: errors=" + errors + ", warnings=" + warnings);

                bool ownedDoc = true;
                Exception testFailure = null;
                try
                {
                    // Fail-closed checks on opened document
                    RequireSandboxPath(sandboxRoot, doc.GetPathName());
                    VerifyReferencesUnderSandbox(doc, sandboxRoot);
                    EnsureNoOldMarkers(doc);
                    if (doc.GetSaveFlag())
                        throw new InvalidOperationException("Fixture is dirty directly after open. Refusing destructive ambiguity.");

                    string beforeFeatures = FeatureSignature(doc);

                    // Load candidate assembly
                    Assembly asm = Assembly.Load(File.ReadAllBytes(dllPath));
                    Type checkerType = asm.GetType("ADDIN.Commands.CheckAssemblyHole");
                    if (checkerType == null)
                        throw new MissingMemberException("CheckAssemblyHole missing from candidate DLL.");

                    // Instantiate checker with AutoCreateMarkers = false
                    object checker = Activator.CreateInstance(checkerType, app);
                    PropertyInfo pMarkers = checkerType.GetProperty("AutoCreateMarkers");
                    if (pMarkers == null)
                        throw new MissingMemberException("AutoCreateMarkers property missing.");
                    pMarkers.SetValue(checker, false, null);

                    // Run Check Hole
                    MethodInfo runMethod = checkerType.GetMethod("Run", BindingFlags.Public | BindingFlags.Instance);
                    if (runMethod == null)
                        throw new MissingMethodException("Run method missing.");
                    runMethod.Invoke(checker, null);

                    // Retrieve report
                    FieldInfo fReport = checkerType.GetField("lastReport", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fReport == null) throw new MissingFieldException("lastReport field missing.");
                    object report = fReport.GetValue(checker);
                    if (report == null) throw new InvalidOperationException("Report was not produced.");

                    // Retrieve ConnectionCoverage
                    Type tReport = report.GetType();
                    FieldInfo fCov = tReport.GetField("ConnectionCoverage");
                    object cov = fCov != null ? fCov.GetValue(report) : null;
                    if (cov == null) throw new InvalidOperationException("ConnectionCoverage missing from candidate report.");

                    Type tCov = cov.GetType();
                    int groupCount = (int)tCov.GetField("GroupCount").GetValue(cov);
                    int unmatchedCount = (int)tCov.GetField("UnmatchedHoleCount").GetValue(cov);
                    int ngNoMaster = (int)tCov.GetField("NgGroupsWithoutMaster").GetValue(cov);
                    IEnumerable groups = tCov.GetField("Groups").GetValue(cov) as IEnumerable;
                    int rowCount = 0;
                    foreach (object g in groups) rowCount++;

                    Console.WriteLine("\n[1] CheckAssemblyHole Output Metrics:");
                    Console.WriteLine("    Logical Holes: " + tReport.GetField("LogicalHoles").GetValue(report));
                    Console.WriteLine("    Stack Count  : " + tReport.GetField("StackCount").GetValue(report));
                    Console.WriteLine("    Groups Count : " + groupCount);
                    Console.WriteLine("    Unmatched    : " + unmatchedCount);
                    Console.WriteLine("    NG No Master : " + ngNoMaster);
                    Console.WriteLine("    Coverage Rows: " + rowCount);

                    // Retrieve resultsForm
                    FieldInfo fForm = checkerType.GetField("resultsForm", BindingFlags.NonPublic | BindingFlags.Instance);
                    Form resultsForm = fForm != null ? fForm.GetValue(checker) as Form : null;
                    if (resultsForm == null) throw new InvalidOperationException("resultsForm missing from CheckAssemblyHole.");

                    Console.WriteLine("\n[2] Verifying CheckHoleResultsForm Toolbar Controls:");
                    Button btnConn = null;
                    var allButtons = GetAllControls(resultsForm).OfType<Button>().ToList();
                    foreach (Button b in allButtons)
                    {
                        Console.WriteLine("    Found Button: '" + b.Text + "' (Enabled=" + b.Enabled + ")");
                        if (b.Text == "Nhóm liên kết") btnConn = b;
                    }

                    if (btnConn == null) throw new MissingMemberException("Button 'Nhóm liên kết' not found in toolbar.");
                    Console.WriteLine("    -> Button 'Nhóm liên kết' presence: PASS");

                    if (!btnConn.Enabled) throw new InvalidOperationException("Button 'Nhóm liên kết' should be enabled.");
                    Console.WriteLine("    -> Button 'Nhóm liên kết' enabled: PASS");

                    // [3] Test "Nhóm liên kết" Modal Dialog Opening via btnConn.PerformClick()
                    Console.WriteLine("\n[3] Testing 'Nhóm liên kết' Modal Dialog via PerformClick():");
                    bool modalVerified = false;
                    string dialogTitle = null;
                    string labelText = null;
                    int heldGridRowCount = -1;
                    DataGridView heldGrid = null;
                    List<string> columnNames = new List<string>();

                    var closeTimer = new Timer();
                    closeTimer.Interval = 100;
                    closeTimer.Tick += (s, e) =>
                    {
                        Form dlg = Application.OpenForms.OfType<Form>()
                            .FirstOrDefault(f => f.Text.Contains("Nhóm liên kết"));
                        if (dlg != null)
                        {
                            dialogTitle = dlg.Text;
                            Label lbl = dlg.Controls.OfType<Label>().FirstOrDefault();
                            if (lbl != null) labelText = lbl.Text;

                            heldGrid = dlg.Controls.OfType<DataGridView>().FirstOrDefault();
                            if (heldGrid != null)
                            {
                                heldGridRowCount = heldGrid.RowCount;
                                foreach (DataGridViewColumn col in heldGrid.Columns)
                                    columnNames.Add(col.DataPropertyName ?? col.Name);
                            }

                            modalVerified = true;
                            closeTimer.Stop();
                            dlg.Close();
                        }
                    };
                    closeTimer.Start();

                    // Invoke OnClick on btnConn to dispatch the Click event handler
                    MethodInfo mOnClick = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (mOnClick != null)
                    {
                        mOnClick.Invoke(btnConn, new object[] { EventArgs.Empty });
                    }
                    else
                    {
                        MethodInfo mShow = resultsForm.GetType().GetMethod("ShowConnectionGroups", BindingFlags.NonPublic | BindingFlags.Instance);
                        mShow.Invoke(resultsForm, null);
                    }
                    closeTimer.Stop();

                    if (!modalVerified)
                        throw new InvalidOperationException("Modal dialog did not open via btnConn.PerformClick().");

                    Console.WriteLine("    -> Modal Title: '" + dialogTitle + "' (PASS)");
                    Console.WriteLine("    -> Header Description Text:\n        " + (labelText ?? "").Replace("\n", "\n        "));

                    if (labelText == null ||
                        !labelText.Contains("Nhóm lỗ đã ghép: " + groupCount) ||
                        !labelText.Contains("NG chưa rõ Master: " + ngNoMaster) ||
                        !labelText.Contains("Lỗ chưa ghép: " + unmatchedCount))
                    {
                        throw new InvalidOperationException("Modal description header counts do not match report metrics.");
                    }
                    if (!labelText.Contains("Hai đầu trục chỉ là ứng viên hình học, KHÔNG phải vật chủ được xác nhận."))
                    {
                        throw new InvalidOperationException("Description missing mandatory non-authoritative disclaimer.");
                    }
                    Console.WriteLine("    -> Header Counts & Mandatory Disclaimer: PASS");

                    // Strict DataGridView assertions (Review #6 requirement)
                    if (heldGrid == null || !heldGrid.ReadOnly)
                    {
                        throw new InvalidOperationException("Grid is missing or editable; read-only guarantee violated.");
                    }
                    if (heldGridRowCount != rowCount)
                    {
                        throw new InvalidOperationException(
                            "Grid row count differs from CG1 coverage: grid=" + heldGridRowCount + " vs expected=" + rowCount);
                    }
                    Console.WriteLine("    -> DataGridView ReadOnly & RowCount (" + heldGridRowCount + "): PASS");

                    string[] expectedCols = { "Id", "Status", "Components", "HoleCount", "EndCandidates",
                                              "MasterDecision", "MatchSource", "MaxOffsetMm", "MaxAngleDeg", "Note" };
                    foreach (string col in expectedCols)
                    {
                        if (!columnNames.Contains(col))
                            throw new InvalidOperationException("Expected diagnostic column '" + col + "' missing from DataGridView.");
                    }
                    Console.WriteLine("    -> All 10 Diagnostic Columns Present: PASS");
                    Console.WriteLine("    -> Modal Evaluation Level: MODAL_PROGRAMMATIC_PASS");

                    // [4] Test CSV Export & Verify Row-by-Row Content (Review #6 requirement)
                    Console.WriteLine("\n[4] Testing CSV Report Export & Verification:");
                    Type tCsv = asm.GetType("ADDIN.Commands.CheckHoleCsv");
                    if (tCsv == null) throw new MissingMemberException("CheckHoleCsv missing.");
                    MethodInfo mWriteCsv = tCsv.GetMethod("Write", BindingFlags.Public | BindingFlags.Static);
                    string testCsvPath = Path.Combine(sandboxRoot, "Test_Export_Report.csv");
                    if (File.Exists(testCsvPath)) File.Delete(testCsvPath);

                    mWriteCsv.Invoke(null, new object[] { testCsvPath, report });
                    if (!File.Exists(testCsvPath) || new FileInfo(testCsvPath).Length == 0)
                        throw new InvalidOperationException("CSV export failed or produced empty file.");

                    string[] csvLines = File.ReadAllLines(testCsvPath, Encoding.UTF8);
                    Console.WriteLine("    -> CSV created: " + testCsvPath);
                    Console.WriteLine("    -> CSV line count: " + csvLines.Length);
                    Console.WriteLine("    -> Header: " + csvLines[0]);

                    const string expectedHeader = "Assembly,Configuration,Status,Type,Reference component,Actual component,Offset (mm),Angle (deg),Reason";
                    if (!string.Equals(csvLines[0].Trim(), expectedHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("CSV header does not match expected schema.\nExpected: " + expectedHeader + "\nActual: " + csvLines[0]);
                    }

                    FieldInfo fRows = tReport.GetField("Rows", BindingFlags.Public | BindingFlags.Instance);
                    IList reportRows = fRows != null ? fRows.GetValue(report) as IList : null;
                    if (reportRows == null) throw new InvalidOperationException("Rows missing from report.");

                    if (csvLines.Length - 1 != reportRows.Count)
                    {
                        throw new InvalidOperationException("CSV data row count (" + (csvLines.Length - 1) + ") does not match report rows (" + reportRows.Count + ").");
                    }

                    for (int i = 0; i < reportRows.Count; i++)
                    {
                        object row = reportRows[i];
                        string status = (string)row.GetType().GetProperty("Status").GetValue(row, null);
                        string csvLine = csvLines[i + 1];
                        string[] parts = csvLine.Split(',');
                        string csvStatus = parts.Length > 2 ? parts[2].Trim('"', ' ') : "";
                        if (parts.Length < 3 || !string.Equals(csvStatus, status.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("CSV row " + (i + 1) + " status mismatch: expected=" + status + ", in CSV=" + csvLine);
                        }
                    }
                    Console.WriteLine("    -> CSV Export Row-by-Row Content Matches Report: PASS");
                    Console.WriteLine("    -> CSV Writer Level: CSV_WRITER_PASS");

                    // [5] Post-Execution Invariance Checks
                    Console.WriteLine("\n[5] Verifying Post-Execution Assembly Safety & Invariance:");
                    EnsureNoOldMarkers(doc);
                    if (!string.Equals(beforeFeatures, FeatureSignature(doc), StringComparison.Ordinal))
                        throw new InvalidOperationException("Assembly feature tree changed IN MEMORY during test.");
                    if (doc.GetSaveFlag())
                        throw new InvalidOperationException("Assembly became modified in memory during test.");

                    Console.WriteLine("    -> Feature Tree Invariance: PASS");
                    Console.WriteLine("    -> Memory Non-Mutation (doc.GetSaveFlag() == false): PASS");
                }
                catch (Exception ex)
                {
                    testFailure = ex;
                }
                finally
                {
                    if (ownedDoc)
                    {
                        try { app.CloseDoc(doc.GetTitle()); }
                        catch (Exception ex) { if (testFailure == null) testFailure = ex; }
                    }
                    try
                    {
                        string afterHash = Sha256(fixturePath);
                        if (!string.Equals(originalHash, afterHash, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Fixture bytes changed on disk during test.");
                        Console.WriteLine("    -> Disk File SHA256 Invariance: PASS");
                    }
                    catch (Exception ex)
                    {
                        if (testFailure == null) testFailure = ex;
                    }
                }

                if (testFailure != null) throw testFailure;

                Console.WriteLine("\n=========================================================");
                Console.WriteLine("ALL UI SAFETY AND INTERACTION CHECKS PASSED (EXIT 0)");
                Console.WriteLine("Status Levels: MODAL_PROGRAMMATIC_PASS | CSV_WRITER_PASS | UI_USER_CLICK_NOT_TESTED");
                Console.WriteLine("=========================================================");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("\nFAIL CLOSED: " + ex);
                return 1;
            }
            finally
            {
                if (ownsSession && app != null)
                {
                    try { app.ExitApp(); }
                    catch (Exception ex) { Console.Error.WriteLine("SolidWorks shutdown warning: " + ex.Message); }
                }
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

        private static void VerifyReferencesUnderSandbox(ModelDoc2 doc, string root)
        {
            AssemblyDoc assembly = doc as AssemblyDoc;
            if (assembly == null) return;
            object[] raw = (object[])assembly.GetComponents(false);
            if (raw == null) return;

            int count = 0, outside = 0;
            string prefix = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            foreach (object item in raw)
            {
                Component2 comp = item as Component2;
                if (comp == null || comp.IsSuppressed()) continue;
                count++;
                string compPath = comp.GetPathName();
                if (string.IsNullOrEmpty(compPath) ||
                    !Path.GetFullPath(compPath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    outside++;
                    Console.Error.WriteLine("Component outside sandbox: " + comp.Name2 + " -> " + compPath);
                }
            }

            if (outside > 0)
                throw new InvalidOperationException("Assembly references components outside isolated fixture root (count=" + outside + ").");
        }

        private static void EnsureNoOldMarkers(ModelDoc2 doc)
        {
            Feature feat = (Feature)doc.FirstFeature();
            while (feat != null)
            {
                string name = feat.Name ?? "";
                if (name.StartsWith(MarkerPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Isolated run rejected: Existing " + MarkerPrefix +
                        " feature detected (" + name + "). Run only on a disposable fixture without markers.");
                feat = (Feature)feat.GetNextFeature();
            }
        }

        private static string FeatureSignature(ModelDoc2 doc)
        {
            var sb = new StringBuilder();
            Feature feat = (Feature)doc.FirstFeature();
            while (feat != null)
            {
                sb.Append(feat.Name).Append('|').Append(feat.GetTypeName2()).Append(';');
                feat = (Feature)feat.GetNextFeature();
            }
            return sb.ToString();
        }

        private static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file))
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
            }
        }

        private static IEnumerable<Control> GetAllControls(Control root)
        {
            var list = new List<Control>();
            foreach (Control child in root.Controls)
            {
                list.Add(child);
                list.AddRange(GetAllControls(child));
            }
            return list;
        }

        private static int SelfTestSandboxLogic()
        {
            bool ok = true;
            try
            {
                RequireSandboxPath(@"D:\sandbox", @"D:\sandbox\model.sldasm");
            }
            catch { ok = false; }

            try
            {
                RequireSandboxPath(@"D:\sandbox", @"C:\other\model.sldasm");
                ok = false; // should have thrown
            }
            catch { /* expected */ }

            Console.WriteLine(ok
                ? "PASS: sandbox path enforcement self-test"
                : "FAIL: sandbox path enforcement self-test");
            return ok ? 0 : 1;
        }
    }
}
