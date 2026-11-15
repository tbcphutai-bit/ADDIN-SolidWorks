// PR #62 / CHECK DRAWING ONLY
// Isolated out-of-process SOLIDWORKS visible-view verifier for hidden batch mode
// when IView.GetVisibleEntities2 returns 0 and/or display-state color evaluation
// requires activating the view configuration/display state on a disposable copy.
// Never touches user/production Part, Assembly, or Drawing originals and never saves.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal delegate FlatDrawingViewSideResult CdIsolatedInspectDelegate(
        ISldWorks workerApp,
        SolidWorks.Interop.sldworks.View view,
        ModelDoc2 ownerDrawing,
        string expectedPartPath,
        out CheckDrawingBom.CdDirectFlatSideProbe probe);

    internal sealed class CdIsolatedVerifyResult
    {
        public bool Success { get; set; }
        public string ErrorReason { get; set; } = "";
        public FlatDrawingViewSideResult Result { get; set; }
        public CheckDrawingBom.CdDirectFlatSideProbe Probe { get; set; }
        public bool RestartedWorker { get; set; }
    }

    internal sealed class CdIsolatedVisibleFlatSideVerifier : IDisposable
    {
        private const int MaxStartupWaitMs = 90000;
        private readonly string _swExePath;
        private readonly Action<string> _log;
        private readonly string _baseTempRoot;

        private ISldWorks _workerApp;
        private int? _workerPid;
        private bool _disposed;

        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

        public CdIsolatedVisibleFlatSideVerifier(Action<string> log = null)
        {
            _log = log;
            _swExePath = ResolveSolidWorksExePath();
            _baseTempRoot = Path.Combine(
                Path.GetTempPath(),
                "ADDIN_CdIso_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        }

        public CdIsolatedVerifyResult VerifyView(
            string drawingPath,
            string sheetName,
            string viewName,
            string expectedPartPath,
            CdIsolatedInspectDelegate inspectDelegate)
        {
            if (_disposed)
                return Fail("DISPOSED");
            if (inspectDelegate == null)
                return Fail("NO_INSPECT_DELEGATE");
            if (string.IsNullOrWhiteSpace(drawingPath) || !File.Exists(drawingPath))
                return Fail("DRAWING_FILE_MISSING");
            if (string.IsNullOrWhiteSpace(viewName))
                return Fail("VIEW_NAME_MISSING");
            if (string.IsNullOrWhiteSpace(_swExePath) || !File.Exists(_swExePath))
                return Fail("SLDWORKS_EXE_NOT_FOUND");

            bool restarted = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    CdIsolatedVerifyResult res = VerifyViewOnce(
                        drawingPath,
                        sheetName,
                        viewName,
                        expectedPartPath,
                        inspectDelegate);
                    res.RestartedWorker = restarted;
                    return res;
                }
                catch (COMException comEx)
                {
                    Log("[CD ISO VERIFIER] COM failure on attempt " + (attempt + 1)
                        + " hresult=0x" + comEx.HResult.ToString("X8")
                        + ": " + comEx.Message);
                    KillWorkerProcessOnly();
                    if (attempt == 0)
                    {
                        restarted = true;
                        continue;
                    }
                    return Fail("WORKER_COM_EXCEPTION: 0x" + comEx.HResult.ToString("X8"));
                }
                catch (Exception ex)
                {
                    Log("[CD ISO VERIFIER] Error on attempt " + (attempt + 1) + ": " + ex.Message);
                    if (!IsWorkerAlive() && attempt == 0)
                    {
                        KillWorkerProcessOnly();
                        restarted = true;
                        continue;
                    }
                    return Fail("WORKER_EXCEPTION: " + ex.Message);
                }
            }

            return Fail("WORKER_RETRY_EXHAUSTED");
        }

        private CdIsolatedVerifyResult VerifyViewOnce(
            string drawingPath,
            string sheetName,
            string viewName,
            string expectedPartPath,
            CdIsolatedInspectDelegate inspectDelegate)
        {
            EnsureWorker();

            Directory.CreateDirectory(_baseTempRoot);
            string itemTempDir = Path.Combine(
                _baseTempRoot,
                Guid.NewGuid().ToString("N").Substring(0, 10));
            Directory.CreateDirectory(itemTempDir);

            ModelDoc2 drwDoc = null;
            string drwCopyPath = Path.Combine(itemTempDir, Path.GetFileName(drawingPath));

            try
            {
                Stopwatch stepSw = Stopwatch.StartNew();
                Log(FormatStageLog("BEFORE_REPOINT", drawingPath, stepSw));

                File.Copy(drawingPath, drwCopyPath, true);
                EnsureWritableNormal(drwCopyPath);

                string expectedTempPartPath = "";
                CopyAndRepointPartDependencies(
                    _workerApp,
                    drawingPath,
                    drwCopyPath,
                    itemTempDir,
                    expectedPartPath,
                    out expectedTempPartPath);

                SafeSetDocumentVisible(_workerApp, true);

                try
                {
                    _workerApp.SetCurrentWorkingDirectory(itemTempDir);
                }
                catch { }

                AuditWorkerDocs("beforeOpen", itemTempDir, out int beforeOutside, out int beforeAsm, out string beforeSummary);
                if (beforeOutside > 0 || beforeAsm > 0)
                {
                    CloseAllWorkerDocuments();
                    AuditWorkerDocs("beforeOpenRetry", itemTempDir, out beforeOutside, out beforeAsm, out beforeSummary);
                }

                Log(FormatStageLog("BEFORE_OPENDOC", drawingPath, stepSw));
                int openErr = 0;
                int openWarn = 0;
                drwDoc = _workerApp.OpenDoc6(
                    drwCopyPath,
                    (int)swDocumentTypes_e.swDocDRAWING,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref openErr,
                    ref openWarn) as ModelDoc2;
                Log(FormatStageLog("AFTER_OPENDOC", drawingPath, stepSw) +
                    " ok=" + (drwDoc != null) + " err=" + openErr + " warn=" + openWarn);

                if (drwDoc == null)
                    return Fail("ISOLATED_OPEN_DRAWING_FAILED err=" + openErr + " warn=" + openWarn);

                KeepWorkerWindowHidden(_workerApp);

                AuditWorkerDocs("afterOpen", itemTempDir, out int openOutside, out int openAsm, out string openSummary);
                if (openOutside > 0 || openAsm > 0)
                    return Fail("ISOLATED_UNEXPECTED_DOC_LOADED_AFTER_OPEN: " + openSummary);

                try
                {
                    int actErr = 0;
                    _workerApp.ActivateDoc3(
                        drwDoc.GetTitle(),
                        true,
                        (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                        ref actErr);
                }
                catch { }

                DrawingDoc drawing = drwDoc as DrawingDoc;
                if (drawing == null)
                    return Fail("NOT_A_DRAWING_DOC");

                if (!string.IsNullOrWhiteSpace(sheetName))
                {
                    try { drawing.ActivateSheet(sheetName); }
                    catch { }
                }

                SolidWorks.Interop.sldworks.View targetView = FindTargetView(drawing, sheetName, viewName);
                if (targetView == null)
                    return Fail("VIEW_NOT_FOUND_IN_ISOLATED_COPY: " + sheetName + "/" + viewName);

                // Safety check: confirm the view's referenced part inside the worker resolves
                // inside itemTempDir before allowing config/display-state activation.
                ModelDoc2 refDoc = targetView.ReferencedDocument as ModelDoc2;
                string actualRefPath = "";
                try { actualRefPath = refDoc != null ? (refDoc.GetPathName() ?? "") : ""; }
                catch { actualRefPath = ""; }

                if (string.IsNullOrWhiteSpace(actualRefPath) ||
                    !IsPathInsideDirectory(actualRefPath, itemTempDir) ||
                    (!string.IsNullOrWhiteSpace(expectedTempPartPath) &&
                     !string.Equals(SafeFullPath(actualRefPath), SafeFullPath(expectedTempPartPath), StringComparison.OrdinalIgnoreCase)))
                {
                    return Fail("ISOLATED_PATH_GUARD_BLOCKED: actualRef=" + actualRefPath + " expected=" + expectedTempPartPath);
                }

                string partPathForDelegate = !string.IsNullOrWhiteSpace(expectedTempPartPath)
                    ? expectedTempPartPath
                    : actualRefPath;

                Log(FormatStageLog("BEFORE_DELEGATE", drawingPath, stepSw));
                CheckDrawingBom.CdDirectFlatSideProbe probe;
                FlatDrawingViewSideResult sideResult = inspectDelegate(
                    _workerApp,
                    targetView,
                    drwDoc,
                    partPathForDelegate,
                    out probe);
                Log(FormatStageLog("AFTER_DELEGATE", drawingPath, stepSw));

                AuditWorkerDocs("afterDelegate", itemTempDir, out int delOutside, out int delAsm, out string delSummary);
                if (delOutside > 0 || delAsm > 0)
                    return Fail("ISOLATED_UNEXPECTED_DOC_LOADED_AFTER_DELEGATE: " + delSummary);

                return new CdIsolatedVerifyResult
                {
                    Success = true,
                    Result = sideResult,
                    Probe = probe
                };
            }
            finally
            {
                Log("[CD ISO VERIFIER STAGE] stage=BEFORE_CLOSE pid=" + (_workerPid ?? 0) + " file=" + Path.GetFileName(drawingPath));
                int remainingDocs = CloseAllWorkerDocuments();
                Log("[CD ISO VERIFIER STAGE] stage=AFTER_CLOSE pid=" + (_workerPid ?? 0) + " remainingDocs=" + remainingDocs);
                if (remainingDocs > 0)
                {
                    AuditWorkerDocs("afterCloseNonZero", itemTempDir, out _, out _, out _);
                }
                if (!TryDeleteDirectory(itemTempDir))
                {
                    Log("[CD ISO VERIFIER CLEANUP WARN] Failed to delete itemTempDir=" + itemTempDir);
                }
            }
        }

        private void CopyAndRepointPartDependencies(
            ISldWorks workerApp,
            string origDrawingPath,
            string drwCopyPath,
            string itemTempDir,
            string expectedPartPath,
            out string expectedTempPartPath)
        {
            expectedTempPartPath = "";
            var copiedBySourcePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            usedFileNames.Add(Path.GetFileName(drwCopyPath) ?? "");

            var replacePairs = new List<KeyValuePair<string, string>>();
            string expectedPartName = "";

            if (!string.IsNullOrWhiteSpace(expectedPartPath))
            {
                string normExpected = SafeFullPath(expectedPartPath);
                if (File.Exists(normExpected) &&
                    normExpected.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase))
                {
                    string destName = MakeUniqueTempFileName(
                        Path.GetFileName(normExpected),
                        usedFileNames);
                    expectedTempPartPath = Path.Combine(itemTempDir, destName);
                    File.Copy(normExpected, expectedTempPartPath, true);
                    EnsureWritableNormal(expectedTempPartPath);
                    copiedBySourcePath[normExpected] = expectedTempPartPath;
                    expectedPartName = Path.GetFileName(normExpected) ?? "";
                    replacePairs.Add(new KeyValuePair<string, string>(normExpected, expectedTempPartPath));
                    if (!string.Equals(expectedPartPath, normExpected, StringComparison.OrdinalIgnoreCase))
                        replacePairs.Add(new KeyValuePair<string, string>(expectedPartPath, expectedTempPartPath));
                }
            }

            // Copy direct Part dependencies ONLY (never assemblies) via both search=true & search=false.
            foreach (bool searchFlag in new[] { true, false })
            {
                Array deps = null;
                try
                {
                    deps = workerApp.GetDocumentDependencies2(
                        origDrawingPath,
                        false, // direct dependencies of drawing only
                        searchFlag,
                        false) as Array;
                }
                catch { deps = null; }

                if (deps == null || deps.Length < 2)
                    continue;

                for (int i = 0; i + 1 < deps.Length; i += 2)
                {
                    string depPath = deps.GetValue(i + 1) as string ?? "";
                    if (string.IsNullOrWhiteSpace(depPath))
                        continue;

                    string trimmedDep = depPath.Trim();
                    if (!trimmedDep.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string depFileName = "";
                    try { depFileName = Path.GetFileName(trimmedDep) ?? ""; }
                    catch { depFileName = ""; }

                    // In SOLIDWORKS only one part with a given filename can be referenced;
                    // always repoint any dependency matching expectedPartName to the single expectedTempPartPath copy.
                    if (!string.IsNullOrWhiteSpace(expectedTempPartPath) &&
                        !string.IsNullOrWhiteSpace(expectedPartName) &&
                        string.Equals(depFileName, expectedPartName, StringComparison.OrdinalIgnoreCase))
                    {
                        replacePairs.Add(new KeyValuePair<string, string>(depPath, expectedTempPartPath));
                        continue;
                    }

                    string sourceToCopy = ResolveExistingPartFile(trimmedDep, origDrawingPath, expectedPartPath);
                    if (string.IsNullOrWhiteSpace(sourceToCopy))
                        continue;

                    string existingCopy;
                    if (!copiedBySourcePath.TryGetValue(sourceToCopy, out existingCopy))
                    {
                        string destName = MakeUniqueTempFileName(
                            Path.GetFileName(sourceToCopy),
                            usedFileNames);
                        existingCopy = Path.Combine(itemTempDir, destName);
                        File.Copy(sourceToCopy, existingCopy, true);
                        EnsureWritableNormal(existingCopy);
                        copiedBySourcePath[sourceToCopy] = existingCopy;
                    }

                    replacePairs.Add(new KeyValuePair<string, string>(depPath, existingCopy));
                }
            }

            var seenOldRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in replacePairs)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || !seenOldRefs.Add(pair.Key))
                    continue;
                try
                {
                    workerApp.ReplaceReferencedDocument(drwCopyPath, pair.Key, pair.Value);
                }
                catch { }
            }
        }

        private static string ResolveExistingPartFile(string depPath, string origDrawingPath, string expectedPartPath)
        {
            if (string.IsNullOrWhiteSpace(depPath))
                return "";

            try
            {
                if (!Path.IsPathRooted(depPath) && !string.IsNullOrWhiteSpace(origDrawingPath))
                {
                    string drwDir = Path.GetDirectoryName(origDrawingPath) ?? "";
                    if (!string.IsNullOrWhiteSpace(drwDir))
                    {
                        string relCandidate = Path.Combine(drwDir, depPath);
                        if (File.Exists(relCandidate))
                            return SafeFullPath(relCandidate);
                    }
                }
            }
            catch { }

            string fullDep = SafeFullPath(depPath);
            if (!string.IsNullOrWhiteSpace(fullDep) && File.Exists(fullDep))
                return fullDep;

            if (!string.IsNullOrWhiteSpace(expectedPartPath) &&
                File.Exists(expectedPartPath) &&
                string.Equals(
                    Path.GetFileName(depPath),
                    Path.GetFileName(expectedPartPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return SafeFullPath(expectedPartPath);
            }
            return "";
        }

        private static string MakeUniqueTempFileName(string desiredName, HashSet<string> usedNames)
        {
            string baseName = Path.GetFileNameWithoutExtension(desiredName) ?? "part";
            string ext = Path.GetExtension(desiredName) ?? ".SLDPRT";
            string candidate = baseName + ext;
            int idx = 1;
            while (usedNames.Contains(candidate))
            {
                candidate = baseName + "_" + idx + ext;
                idx++;
            }
            usedNames.Add(candidate);
            return candidate;
        }

        private static SolidWorks.Interop.sldworks.View FindTargetView(
            DrawingDoc drawing,
            string sheetName,
            string viewName)
        {
            SolidWorks.Interop.sldworks.View current =
                drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
            while (current != null)
            {
                try
                {
                    if (current.Type == (int)swDrawingViewTypes_e.swDrawingSheet)
                    {
                        current = current.GetNextView() as SolidWorks.Interop.sldworks.View;
                        continue;
                    }

                    string vTypeName = "";
                    try { vTypeName = current.GetName2() ?? ""; } catch { }
                    if (string.IsNullOrWhiteSpace(vTypeName))
                    {
                        try { vTypeName = current.Name ?? ""; } catch { }
                    }

                    Sheet ownerSheet = null;
                    try { ownerSheet = current.Sheet as Sheet; } catch { }

                    if (string.Equals(vTypeName.Trim(), viewName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(sheetName) ||
                            (ownerSheet != null &&
                             string.Equals((ownerSheet.GetName() ?? "").Trim(), sheetName.Trim(), StringComparison.OrdinalIgnoreCase)))
                        {
                            return current;
                        }
                    }
                }
                catch { }

                current = current.GetNextView() as SolidWorks.Interop.sldworks.View;
            }
            return null;
        }

        private void EnsureWorker()
        {
            if (IsWorkerAlive())
                return;

            KillWorkerProcessOnly();

            var psi = new ProcessStartInfo(_swExePath)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process proc = Process.Start(psi);
            if (proc == null)
                throw new InvalidOperationException("Failed to start isolated SLDWORKS.exe process.");

            _workerPid = proc.Id;
            string expectedMoniker = "SolidWorks_PID_" + proc.Id;
            Log("[CD ISO VERIFIER] Starting hidden worker pid=" + proc.Id);

            Stopwatch sw = Stopwatch.StartNew();
            ISldWorks app = null;
            while (sw.ElapsedMilliseconds < MaxStartupWaitMs)
            {
                if (proc.HasExited)
                    throw new InvalidOperationException(
                        "Isolated SLDWORKS.exe exited prematurely code=" + proc.ExitCode);

                app = TryGetAppFromRot(expectedMoniker, proc.Id);
                if (app != null)
                {
                    try
                    {
                        string rev = app.RevisionNumber();
                        if (!string.IsNullOrEmpty(rev))
                            break;
                    }
                    catch { }
                    app = null;
                }

                Thread.Sleep(500);
            }

            if (app == null)
            {
                KillWorkerProcessOnly();
                throw new TimeoutException("Timed out waiting for ROT moniker " + expectedMoniker);
            }

            _workerApp = app;
            KeepWorkerWindowHidden(_workerApp);
            try { _workerApp.UserControl = false; } catch { }
            Log("[CD ISO VERIFIER] Worker ready pid=" + proc.Id);
        }

        private bool IsWorkerAlive()
        {
            if (_workerApp == null || !_workerPid.HasValue)
                return false;
            try
            {
                using (Process p = Process.GetProcessById(_workerPid.Value))
                {
                    if (p.HasExited)
                        return false;
                }
                string rev = _workerApp.RevisionNumber();
                return !string.IsNullOrEmpty(rev);
            }
            catch
            {
                return false;
            }
        }

        private static ISldWorks TryGetAppFromRot(string expectedName, int expectedPid)
        {
            IRunningObjectTable table = null;
            IBindCtx ctx = null;
            IEnumMoniker enumerator = null;

            try
            {
                if (GetRunningObjectTable(0, out table) != 0 || table == null)
                    return null;
                if (CreateBindCtx(0, out ctx) != 0 || ctx == null)
                    return null;

                table.EnumRunning(out enumerator);
                if (enumerator == null)
                    return null;

                IMoniker[] monikers = new IMoniker[1];
                while (enumerator.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    IMoniker moniker = monikers[0];
                    if (moniker == null)
                        continue;
                    try
                    {
                        string name;
                        moniker.GetDisplayName(ctx, null, out name);
                        string normalizedMoniker = (name ?? "").Trim().TrimStart('!');
                        if (string.Equals(normalizedMoniker, expectedName, StringComparison.OrdinalIgnoreCase))
                        {
                            object obj;
                            if (table.GetObject(moniker, out obj) == 0 && obj is ISldWorks sw)
                            {
                                try
                                {
                                    if (sw.GetProcessID() == expectedPid)
                                        return sw;
                                }
                                catch { }

                                try
                                {
                                    if (Marshal.IsComObject(obj))
                                        Marshal.ReleaseComObject(obj);
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        Marshal.ReleaseComObject(moniker);
                    }
                }
            }
            catch { }
            finally
            {
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
                if (ctx != null) Marshal.ReleaseComObject(ctx);
                if (table != null) Marshal.ReleaseComObject(table);
            }

            return null;
        }

        private string FormatStageLog(string stage, string drawingPath, Stopwatch sw)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "[CD ISO VERIFIER STAGE] stage={0} pid={1} t={2} elapsedMs={3} file={4}",
                stage,
                _workerPid ?? 0,
                DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
                sw != null ? sw.ElapsedMilliseconds : 0,
                Path.GetFileName(drawingPath ?? ""));
        }

        private void AuditWorkerDocs(
            string stage,
            string itemTempDir,
            out int outsideTempCount,
            out int asmCount,
            out string summary)
        {
            outsideTempCount = 0;
            asmCount = 0;
            summary = "none";
            if (_workerApp == null)
                return;

            var entries = new List<string>();
            int total = 0;
            try
            {
                Array docs = _workerApp.GetDocuments() as Array;
                if (docs != null)
                {
                    for (int i = docs.GetLowerBound(0); i <= docs.GetUpperBound(0); i++)
                    {
                        ModelDoc2 md = docs.GetValue(i) as ModelDoc2;
                        if (md == null)
                            continue;
                        total++;
                        int docType = -1;
                        string path = "";
                        string title = "";
                        bool dirty = false;
                        bool ro = false;
                        try { docType = md.GetType(); } catch { }
                        try { path = md.GetPathName() ?? ""; } catch { }
                        try { title = md.GetTitle() ?? ""; } catch { }
                        try { dirty = md.GetSaveFlag(); } catch { }
                        try { ro = md.IsOpenedReadOnly(); } catch { }

                        bool isAsm = docType == (int)swDocumentTypes_e.swDocASSEMBLY ||
                            path.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase);
                        bool insideTemp = !string.IsNullOrWhiteSpace(path) && IsPathInsideDirectory(path, itemTempDir);
                        if (isAsm) asmCount++;
                        if (!insideTemp) outsideTempCount++;

                        entries.Add(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0}:type={1}:inTemp={2}:asm={3}:dirty={4}:ro={5}:path={6}",
                            title,
                            docType,
                            insideTemp,
                            isAsm,
                            dirty,
                            ro,
                            path));
                    }
                }
            }
            catch (Exception ex)
            {
                summary = "audit_err=" + ex.Message;
                Log("[CD ISO VERIFIER DOC AUDIT] stage=" + stage + " pid=" + (_workerPid ?? 0) + " error=" + ex.Message);
                return;
            }

            summary = entries.Count > 0 ? string.Join(" ; ", entries) : "none";
            Log("[CD ISO VERIFIER DOC AUDIT] stage=" + stage +
                " pid=" + (_workerPid ?? 0) +
                " count=" + total +
                " outsideTemp=" + outsideTempCount +
                " asmCount=" + asmCount +
                " docs=[" + summary + "]");
        }

        private int CloseAllWorkerDocuments()
        {
            if (_workerApp == null)
                return 0;

            for (int guard = 0; guard < 16; guard++)
            {
                Array docs = null;
                try { docs = _workerApp.GetDocuments() as Array; }
                catch { break; }

                if (docs == null || docs.Length == 0)
                    break;

                // Close Drawings before Assemblies and Assemblies before Parts via QuitDoc (close without saving)
                // to mirror main batch cleanup order and avoid CloseDoc saving prompts or dependency traps.
                var targets = new List<KeyValuePair<int, string>>();
                for (int i = docs.GetLowerBound(0); i <= docs.GetUpperBound(0); i++)
                {
                    ModelDoc2 md = docs.GetValue(i) as ModelDoc2;
                    if (md == null) continue;
                    int t = -1;
                    string title = "";
                    try { t = md.GetType(); } catch { }
                    try { title = md.GetTitle() ?? ""; } catch { }
                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    int prio = t == (int)swDocumentTypes_e.swDocDRAWING ? 0
                             : t == (int)swDocumentTypes_e.swDocASSEMBLY ? 1
                             : t == (int)swDocumentTypes_e.swDocPART ? 2 : 3;
                    targets.Add(new KeyValuePair<int, string>(prio, title));
                }

                if (targets.Count == 0)
                    break;

                targets.Sort((a, b) => a.Key.CompareTo(b.Key));
                int topPriority = targets[0].Key;
                bool closedAny = false;
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i].Key != topPriority)
                        break;
                    try
                    {
                        _workerApp.QuitDoc(targets[i].Value);
                        closedAny = true;
                    }
                    catch
                    {
                        try
                        {
                            _workerApp.CloseDoc(targets[i].Value);
                            closedAny = true;
                        }
                        catch { }
                    }
                }

                if (!closedAny)
                    break;
            }

            try { SafeSetDocumentVisible(_workerApp, false); } catch { }

            try
            {
                Array remaining = _workerApp.GetDocuments() as Array;
                return remaining != null ? remaining.Length : 0;
            }
            catch
            {
                return -1;
            }
        }

        private static void SafeSetDocumentVisible(ISldWorks app, bool visible)
        {
            if (app == null) return;
            try { app.DocumentVisible(visible, (int)swDocumentTypes_e.swDocDRAWING); } catch { }
            try { app.DocumentVisible(visible, (int)swDocumentTypes_e.swDocPART); } catch { }
        }

        private static void KeepWorkerWindowHidden(ISldWorks app)
        {
            if (app == null) return;
            try { app.Visible = false; } catch { }
        }

        private void KillWorkerProcessOnly()
        {
            if (_workerApp != null)
            {
                try { CloseAllWorkerDocuments(); } catch { }
                try { _workerApp.ExitApp(); } catch { }
                try { Marshal.FinalReleaseComObject(_workerApp); } catch { }
                _workerApp = null;
            }

            if (_workerPid.HasValue)
            {
                int pid = _workerPid.Value;
                _workerPid = null;
                try
                {
                    using (Process proc = Process.GetProcessById(pid))
                    {
                        if (!proc.HasExited)
                        {
                            if (!proc.WaitForExit(4000))
                            {
                                proc.Kill();
                                proc.WaitForExit(3000);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private static string ResolveSolidWorksExePath()
        {
            try
            {
                string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrWhiteSpace(currentExe) &&
                    string.Equals(Path.GetFileName(currentExe), "SLDWORKS.exe", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(currentExe))
                {
                    return currentExe;
                }
            }
            catch { }

            string default2024 = @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe";
            if (File.Exists(default2024))
                return default2024;

            return "";
        }

        private static void EnsureWritableNormal(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.SetAttributes(path, FileAttributes.Normal);
            }
            catch { }
        }

        private static string SafeFullPath(string path)
        {
            try { return Path.GetFullPath(path ?? ""); }
            catch { return path ?? ""; }
        }

        private static bool IsPathInsideDirectory(string filePath, string dirPath)
        {
            try
            {
                string fullFile = Path.GetFullPath(filePath).TrimEnd('\\');
                string fullDir = Path.GetFullPath(dirPath).TrimEnd('\\') + "\\";
                return fullFile.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                    }
                    Directory.Delete(path, true);
                }
                return string.IsNullOrWhiteSpace(path) || !Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private CdIsolatedVerifyResult Fail(string reason)
        {
            return new CdIsolatedVerifyResult
            {
                Success = false,
                ErrorReason = reason ?? "UNKNOWN"
            };
        }

        private void Log(string message)
        {
            try
            {
                if (_log != null)
                    _log(message);
                else
                    Debug.WriteLine(message);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            KillWorkerProcessOnly();
            TryDeleteDirectory(_baseTempRoot);
        }
    }
}
