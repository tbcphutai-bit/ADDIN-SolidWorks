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

                int openErr = 0;
                int openWarn = 0;
                drwDoc = _workerApp.OpenDoc6(
                    drwCopyPath,
                    (int)swDocumentTypes_e.swDocDRAWING,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref openErr,
                    ref openWarn) as ModelDoc2;

                if (drwDoc == null)
                    return Fail("ISOLATED_OPEN_DRAWING_FAILED err=" + openErr + " warn=" + openWarn);

                KeepWorkerWindowHidden(_workerApp);

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

                CheckDrawingBom.CdDirectFlatSideProbe probe;
                FlatDrawingViewSideResult sideResult = inspectDelegate(
                    _workerApp,
                    targetView,
                    drwDoc,
                    partPathForDelegate,
                    out probe);

                return new CdIsolatedVerifyResult
                {
                    Success = true,
                    Result = sideResult,
                    Probe = probe
                };
            }
            finally
            {
                CloseAllWorkerDocuments();
                TryDeleteDirectory(itemTempDir);
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

                app = TryGetAppFromRot(expectedMoniker);
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

        private static ISldWorks TryGetAppFromRot(string expectedName)
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
                        if (!string.IsNullOrEmpty(name) &&
                            name.IndexOf(expectedName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            object obj;
                            if (table.GetObject(moniker, out obj) == 0 && obj is ISldWorks)
                                return (ISldWorks)obj;
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

        private void CloseAllWorkerDocuments()
        {
            if (_workerApp == null)
                return;
            try
            {
                Array docs = _workerApp.GetDocuments() as Array;
                if (docs != null)
                {
                    var titles = new List<string>();
                    foreach (object d in docs)
                    {
                        ModelDoc2 md = d as ModelDoc2;
                        if (md == null) continue;
                        try
                        {
                            string t = md.GetTitle();
                            if (!string.IsNullOrWhiteSpace(t))
                                titles.Add(t);
                        }
                        catch { }
                    }
                    for (int i = 0; i < titles.Count; i++)
                    {
                        try { _workerApp.CloseDoc(titles[i]); } catch { }
                    }
                }
            }
            catch { }

            try { SafeSetDocumentVisible(_workerApp, false); } catch { }
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

        private static void TryDeleteDirectory(string path)
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
            }
            catch { }
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
