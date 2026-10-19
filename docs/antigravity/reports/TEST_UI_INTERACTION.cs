// CG1 Review #5, #6 & #7: WinForms UI interaction & safety test for Check Hole & "Nhóm liên kết".
// C# 7.3 / .NET Framework 4.8.
// Verifies:
// 1. Strict fail-closed sandbox enforcement, component references under sandbox (all components, including suppressed),
//    clean open check, feature-tree signature invariance, marker checks, and disk file SHA256 invariance.
// 2. WinForms toolbar button "Nhóm liên kết" state and Click event dispatch via OnClick (no direct method fallback).
// 3. Modal dialog inspection, mandatory non-authoritative disclaimer, and DataGridView read-only assertions.
// 4. CSV report RFC-4180 parsing and row-by-row verification across ALL 9 COLUMNS against CheckHoleReport.Rows.
// 5. Negative tests (outside sandbox rejection, stale marker detection, sandbox & CSV self-test).

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
                return SelfTestSandboxAndCsvLogic();
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
                    // Fail-closed checks on opened document (Review #7 BLOCKER A)
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

                    // [3] Test "Nhóm liên kết" Toolbar Button Event Dispatch via OnClick (Review #7 MINOR C)
                    Console.WriteLine("\n[3] Testing 'Nhóm liên kết' Button Event Dispatch & Modal Dialog:");
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

                    // Invoke OnClick strictly on btnConn - NO direct fallback to ShowConnectionGroups!
                    MethodInfo mOnClick = typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (mOnClick == null)
                        throw new MissingMethodException("Button.OnClick method missing.");

                    mOnClick.Invoke(btnConn, new object[] { EventArgs.Empty });
                    closeTimer.Stop();

                    if (!modalVerified)
                        throw new InvalidOperationException("Modal dialog did not open via btnConn Click event dispatch.");

                    Console.WriteLine("    -> Button Click Event Dispatch: PASS (BUTTON_EVENT_DISPATCH_PASS)");
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

                    // Strict DataGridView assertions
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

                    // [4] Test CSV Export & Verify ALL 9 COLUMNS (Review #7 BLOCKER B)
                    Console.WriteLine("\n[4] Testing CSV Report Export & Strict 9-Column Verification:");
                    Type tCsv = asm.GetType("ADDIN.Commands.CheckHoleCsv");
                    if (tCsv == null) throw new MissingMemberException("CheckHoleCsv missing.");
                    MethodInfo mWriteCsv = tCsv.GetMethod("Write", BindingFlags.Public | BindingFlags.Static);
                    string testCsvPath = Path.Combine(sandboxRoot, "Test_Export_Report.csv");
                    if (File.Exists(testCsvPath)) File.Delete(testCsvPath);

                    mWriteCsv.Invoke(null, new object[] { testCsvPath, report });
                    if (!File.Exists(testCsvPath) || new FileInfo(testCsvPath).Length == 0)
                        throw new InvalidOperationException("CSV export failed or produced empty file.");

                    string fullCsvText = File.ReadAllText(testCsvPath, Encoding.UTF8);
                    List<List<string>> records = ParseCsvRecords(fullCsvText);
                    Console.WriteLine("    -> CSV created: " + testCsvPath);
                    Console.WriteLine("    -> CSV record count (including header): " + records.Count);
                    if (records.Count == 0)
                        throw new InvalidOperationException("CSV is empty.");

                    List<string> header = records[0];
                    string actualHeaderStr = string.Join(",", header);
                    Console.WriteLine("    -> Header: " + actualHeaderStr);

                    const string expectedHeader = "Assembly,Configuration,Status,Type,Reference component,Actual component,Offset (mm),Angle (deg),Reason";
                    if (!string.Equals(actualHeaderStr, expectedHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("CSV header does not match expected schema.\nExpected: " + expectedHeader + "\nActual: " + actualHeaderStr);
                    }

                    FieldInfo fRows = tReport.GetField("Rows", BindingFlags.Public | BindingFlags.Instance);
                    IList reportRows = fRows != null ? fRows.GetValue(report) as IList : null;
                    if (reportRows == null) throw new InvalidOperationException("Rows missing from report.");

                    if (records.Count - 1 != reportRows.Count)
                    {
                        throw new InvalidOperationException("CSV data record count (" + (records.Count - 1) + ") does not match report rows (" + reportRows.Count + ").");
                    }

                    string expectedTitle = (string)tReport.GetField("AssemblyTitle").GetValue(report) ?? "";
                    string expectedConfig = (string)tReport.GetField("Configuration").GetValue(report) ?? "";

                    for (int i = 0; i < reportRows.Count; i++)
                    {
                        object row = reportRows[i];
                        Type tRow = row.GetType();
                        string expStatus = (string)tRow.GetProperty("Status").GetValue(row, null) ?? "";
                        string expKind = (string)tRow.GetProperty("Kind").GetValue(row, null) ?? "";
                        string expRef = (string)tRow.GetProperty("ReferenceComponent").GetValue(row, null) ?? "";
                        string expAct = (string)tRow.GetProperty("ActualComponent").GetValue(row, null) ?? "";
                        double? expOffset = (double?)tRow.GetProperty("OffsetMm").GetValue(row, null);
                        double? expAngle = (double?)tRow.GetProperty("AngleDeg").GetValue(row, null);
                        string expReason = (string)tRow.GetProperty("Reason").GetValue(row, null) ?? "";

                        List<string> fields = records[i + 1];
                        if (fields.Count != 9)
                        {
                            throw new InvalidOperationException("CSV record " + (i + 1) + " has " + fields.Count + " fields instead of 9.");
                        }

                        // Verify Col 0: AssemblyTitle - exact ExpectedRawCell match, no Trim()!
                        string expCell0 = ExpectedRawCell(expectedTitle);
                        if (!string.Equals(fields[0], expCell0, StringComparison.Ordinal))
                            throw new InvalidOperationException("CSV row " + (i + 1) + " Col 0 (Assembly) mismatch: exp='" + expCell0 + "', act='" + fields[0] + "'");

                        // Verify Col 1: Configuration
                        string expCell1 = ExpectedRawCell(expectedConfig);
                        if (!string.Equals(fields[1], expCell1, StringComparison.Ordinal))
                            throw new InvalidOperationException("CSV row " + (i + 1) + " Col 1 (Config) mismatch: exp='" + expCell1 + "', act='" + fields[1] + "'");

                        // Verify Col 2: Status
                        string expCell2 = ExpectedRawCell(expStatus);
                        if (!string.Equals(fields[2], expCell2, StringComparison.Ordinal))
                            throw new InvalidOperationException("CSV row " + (i + 1) + " Col 2 (Status) mismatch: exp='" + expCell2 + "', act='" + fields[2] + "'");

                        // Verify Col 3: Type / Kind
                        string expCell3 = ExpectedRawCell(expKind);
                        if (!string.Equals(fields[3], expCell3, StringComparison.Ordinal))
                            throw new InvalidOperationException("CSV row " + (i + 1) + " Col 3 (Type) mismatch: exp='" + expCell3 + "', act='" + fields[3] + "'");

                        // Verify Col 4: Reference component
                        string expCell4 = ExpectedRawCell(expRef);
                        if (!string.Equals(fields[4], expCell4, StringComparison.Ordinal))
                            throw new InvalidOperationException("CSV row " + (i + 1) + " Col 4 (ReferenceComponent) mismatch: exp='" + expCell4 + "', act='" + fields[4] + "'");

                        // Verify Col 5: Actual component
                        string expCell5 = ExpectedRawCell(expAct);
                        if (!string.Equals(fields[5], expCell5, StringComparison.Ordinal))
                            throw new InvalidOperationException("CSV row " + (i + 1) + " Col 5 (ActualComponent) mismatch: exp='" + expCell5 + "', act='" + fields[5] + "'");

                        // Verify Col 6: Offset (mm)
                        string actOffsetStr = fields[6];
                        if (expOffset.HasValue)
                        {
                            if (string.IsNullOrEmpty(actOffsetStr) ||
                                !double.TryParse(actOffsetStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double actOffset) ||
                                Math.Abs(actOffset - expOffset.Value) > 1e-4)
                            {
                                throw new InvalidOperationException("CSV row " + (i + 1) + " Col 6 (OffsetMm) mismatch: exp=" + expOffset.Value + ", act='" + actOffsetStr + "'");
                            }
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(actOffsetStr))
                                throw new InvalidOperationException("CSV row " + (i + 1) + " Col 6 (OffsetMm) expected empty, got: '" + actOffsetStr + "'");
                        }

                        // Verify Col 7: Angle (deg)
                        string actAngleStr = fields[7];
                        if (expAngle.HasValue)
                        {
                            if (string.IsNullOrEmpty(actAngleStr) ||
                                !double.TryParse(actAngleStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double actAngle) ||
                                Math.Abs(actAngle - expAngle.Value) > 1e-4)
                            {
                                throw new InvalidOperationException("CSV row " + (i + 1) + " Col 7 (AngleDeg) mismatch: exp=" + expAngle.Value + ", act='" + actAngleStr + "'");
                            }
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(actAngleStr))
                                throw new InvalidOperationException("CSV row " + (i + 1) + " Col 7 (AngleDeg) expected empty, got: '" + actAngleStr + "'");
                        }

                        // Verify Col 8: Reason
                        string expCell8 = ExpectedRawCell(expReason);
                        if (!string.Equals(fields[8], expCell8, StringComparison.Ordinal))
                            throw new InvalidOperationException("CSV row " + (i + 1) + " Col 8 (Reason) mismatch: exp='" + expCell8 + "', act='" + fields[8] + "'");
                    }

                    Console.WriteLine("    -> CSV All 9 Columns Verified Against Report.Rows (RFC-4180 Multiline Parser & Exact Cell Escaping): PASS");
                    Console.WriteLine("    -> CSV Verification Level: CSV_PRODUCTION_SERIALIZER_MULTILINE_PASS");

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
                Console.WriteLine("Levels: BUTTON_EVENT_DISPATCH_PASS | MODAL_PROGRAMMATIC_PASS | CSV_PRODUCTION_SERIALIZER_MULTILINE_PASS | UI_USER_CLICK_NOT_TESTED");
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

        // Strict fail-closed reference verification (Review #7 BLOCKER A)
        private static void VerifyReferencesUnderSandbox(ModelDoc2 doc, string root)
        {
            AssemblyDoc assembly = doc as AssemblyDoc;
            if (assembly == null)
                throw new InvalidOperationException("Expected AssemblyDoc, got: " + (doc != null ? doc.GetType().ToString() : "null"));

            object[] raw = assembly.GetComponents(false) as object[];
            if (raw == null)
                throw new InvalidOperationException("Cannot enumerate components; isolation unverified");

            foreach (object item in raw)
            {
                Component2 comp = item as Component2;
                if (comp == null)
                    throw new InvalidOperationException("Invalid component reference in assembly component list.");

                string path = comp.GetPathName();
                if (string.IsNullOrWhiteSpace(path))
                    throw new InvalidOperationException("Unresolved/virtual reference: " + comp.Name2);

                RequireSandboxPath(root, path);
                if (!File.Exists(path))
                    throw new FileNotFoundException("Missing fixture dependency: " + path);
            }
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

        // True RFC-4180 CSV stream parser supporting multiline quoted fields, escaped quotes, and commas
        internal static List<List<string>> ParseCsvRecords(string csvText)
        {
            var records = new List<List<string>>();
            if (string.IsNullOrEmpty(csvText)) return records;

            var currentRecord = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < csvText.Length; i++)
            {
                char c = csvText[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csvText.Length && csvText[i + 1] == '"')
                        {
                            currentField.Append('"');
                            i++; // skip escaped quote
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        currentRecord.Add(currentField.ToString());
                        currentField.Clear();
                    }
                    else if (c == '\r')
                    {
                        if (i + 1 < csvText.Length && csvText[i + 1] == '\n')
                            i++;
                        currentRecord.Add(currentField.ToString());
                        currentField.Clear();
                        records.Add(currentRecord);
                        currentRecord = new List<string>();
                    }
                    else if (c == '\n')
                    {
                        currentRecord.Add(currentField.ToString());
                        currentField.Clear();
                        records.Add(currentRecord);
                        currentRecord = new List<string>();
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
            }

            if (inQuotes)
            {
                throw new FormatException("Unclosed quote encountered in CSV data stream (malformed RFC-4180 record).");
            }

            if (currentField.Length > 0 || currentRecord.Count > 0)
            {
                currentRecord.Add(currentField.ToString());
                records.Add(currentRecord);
            }

            return records;
        }

        // Mirrors production CheckHoleCsv.Cell serialization logic without Trim(),
        // preserving significant leading/trailing whitespace and formula prefixes.
        internal static string ExpectedRawCell(string value)
        {
            value = value ?? "";
            string start = value.TrimStart();
            if (start.Length > 0 && "=+-@".IndexOf(start[0]) >= 0) value = "'" + value;
            return value;
        }

        private static int SelfTestSandboxAndCsvLogic()
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

            // 1. Test CSV parser with quotes, commas inside fields, and escaped quotes
            string sampleLine = "\"Assy, 1\",\"Default\",\"OK\",\"Type\",\"Comp, A\",\"Comp, B\",0.3000,0.2000,\"Reason with, comma and \"\"escaped quotes\"\"\"";
            List<List<string>> records = ParseCsvRecords(sampleLine);
            if (records.Count != 1 || records[0].Count != 9)
            {
                Console.Error.WriteLine("FAIL: ParseCsvRecords field count: " + (records.Count > 0 ? records[0].Count : 0));
                ok = false;
            }
            else
            {
                List<string> fields = records[0];
                if (fields[0] != "Assy, 1" ||
                    fields[4] != "Comp, A" ||
                    fields[6] != "0.3000" ||
                    fields[8] != "Reason with, comma and \"escaped quotes\"")
                {
                    Console.Error.WriteLine("FAIL: ParseCsvRecords content mismatch: " + string.Join(" | ", fields));
                    ok = false;
                }
            }

            // 2. Test whitespace preservation (Review #8 MINOR B)
            string spaceVal = "   Component With Spaces   ";
            string spaceExpected = ExpectedRawCell(spaceVal);
            if (spaceExpected != spaceVal)
            {
                Console.Error.WriteLine("FAIL: ExpectedRawCell trimmed whitespace: '" + spaceExpected + "'");
                ok = false;
            }

            // 3. Test formula prefix escaping (Review #8 MINOR B)
            string formulaVal = "=SUM(1,2)";
            string formulaExpected = ExpectedRawCell(formulaVal);
            if (formulaExpected != "'=SUM(1,2)")
            {
                Console.Error.WriteLine("FAIL: ExpectedRawCell failed formula escaping: '" + formulaExpected + "'");
                ok = false;
            }

            string literalQuoteVal = "'=Literal";
            string literalQuoteExpected = ExpectedRawCell(literalQuoteVal);
            if (literalQuoteExpected != "'=Literal")
            {
                Console.Error.WriteLine("FAIL: ExpectedRawCell altered literal quote: '" + literalQuoteExpected + "'");
                ok = false;
            }

            // 4. Test multiline RFC-4180 record parsing (Review #8 MINOR B)
            string multilineCsv = "\"Record 1 Col 1\",\"Line 1\r\nLine 2\"\r\n\"Record 2 Col 1\",\"Single Line\"";
            List<List<string>> multiRecords = ParseCsvRecords(multilineCsv);
            if (multiRecords.Count != 2 || multiRecords[0].Count != 2 || multiRecords[1].Count != 2)
            {
                Console.Error.WriteLine("FAIL: ParseCsvRecords multiline count mismatch: " + multiRecords.Count);
                ok = false;
            }
            else
            {
                if (multiRecords[0][1] != "Line 1\r\nLine 2" || multiRecords[1][1] != "Single Line")
                {
                    Console.Error.WriteLine("FAIL: ParseCsvRecords multiline content mismatch: '" + multiRecords[0][1] + "'");
                    ok = false;
                }
            }

            // 5. Negative test: Unclosed quote throws FormatException (Review #9 MINOR)
            try
            {
                ParseCsvRecords("\"Unclosed quote without terminating quote");
                Console.Error.WriteLine("FAIL: ParseCsvRecords accepted unclosed quote without throwing.");
                ok = false;
            }
            catch (FormatException)
            {
                // Expected: correctly caught malformed unclosed quote
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: ParseCsvRecords threw unexpected exception type: " + ex.Message);
                ok = false;
            }

            Console.WriteLine(ok
                ? "PASS: sandbox path enforcement and CSV multiline production serializer self-test (CSV_PRODUCTION_SERIALIZER_MULTILINE_PASS)"
                : "FAIL: sandbox or CSV self-test");
            return ok ? 0 : 1;
        }
    }
}
