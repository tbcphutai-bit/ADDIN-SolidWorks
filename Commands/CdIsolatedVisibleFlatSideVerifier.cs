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
    internal enum CdWorkerCleanupMode
    {
        DefaultQuitThenClose = 0,
        CloseDocFirst = 1,
        CloseAllOwnedDocs = 2
    }

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
        public bool SafetyViolation { get; set; }
        public int? QuarantinedWorkerPid { get; set; }
    }

    internal sealed class CdWorkerDocSnapshot
    {
        public bool ReadSucceeded { get; set; }
        public string ReadError { get; set; } = "";
        public int DocType { get; set; } = -1;
        public string Path { get; set; } = "";
        public string Title { get; set; } = "";
        public bool Dirty { get; set; }
        public bool ReadOnly { get; set; }
    }

    internal sealed class CdWorkerDocAuditResult
    {
        public bool AuditSucceeded { get; set; }
        public string AuditError { get; set; } = "";
        public int ReportedDocCount { get; set; }
        public int TotalCount { get; set; }
        public int OutsideTempCount { get; set; }
        public int AsmCount { get; set; }
        public int UnreadableCount { get; set; }
        public List<string> OffendingDocs { get; } = new List<string>();
        public string Summary { get; set; } = "none";

        public bool IsSafeForZeroDocs =>
            AuditSucceeded && TotalCount == 0 && OutsideTempCount == 0 && AsmCount == 0 && UnreadableCount == 0;

        public bool IsSafeForTempDocsOnly =>
            AuditSucceeded && OutsideTempCount == 0 && AsmCount == 0 && UnreadableCount == 0;
    }

    internal sealed class CdWorkerCleanupResult
    {
        public bool Success { get; set; }
        public int RemainingDocs { get; set; }
        public string ErrorReason { get; set; } = "";
        public List<string> UnownedDocsSkipped { get; } = new List<string>();
        public int CloseCallsExecuted { get; set; }
    }

    internal interface ICdWorkerRuntime
    {
        bool HasWorker { get; }
        int? WorkerPid { get; }
        ISldWorks LiveApp { get; }
        bool IsWorkerAlive();
        void EnsureWorker(
            string swExePath,
            Action<string> log,
            HashSet<int> ownedPids,
            Action<string> quarantineCallback);
        bool TryGetDocuments(out int reportedCount, out List<CdWorkerDocSnapshot> docs, out string error);
        void CopyAndRepointPartDependencies(
            string origDrawingPath,
            string drwCopyPath,
            string itemTempDir,
            string expectedPartPath,
            out string expectedTempPartPath);
        void SafeSetDocumentVisible(bool visible);
        void KeepWorkerWindowHidden();
        bool TrySetCurrentWorkingDirectory(string dirPath);
        bool TryOpenDrawing(string drwCopyPath, out int openErr, out int openWarn, out string openErrorDetail);
        bool TryResolveAndInspect(
            string sheetName,
            string viewName,
            string itemTempDir,
            string expectedTempPartPath,
            CdIsolatedInspectDelegate inspectDelegate,
            Stopwatch stepSw,
            string drawingPath,
            Action<string> log,
            out FlatDrawingViewSideResult sideResult,
            out CheckDrawingBom.CdDirectFlatSideProbe probe,
            out string errorReason,
            out bool isUnexpectedRefViolation);
        bool TryQuitDoc(string title, out string error);
        bool TryCloseDoc(string title, out string error);
        bool TryCloseAllDocuments(bool includeUnsaved, out string error);
        void GracefulExitIfClean();
        void DetachComReferenceOnly();
        bool TerminateOwnedProcessOnly(int pid, HashSet<int> ownedPids, Action<string> log);
    }

    internal sealed class CdIsolatedVisibleFlatSideVerifier : IDisposable
    {
        private const int MaxStartupWaitMs = 90000;
        private readonly string _swExePath;
        private readonly Action<string> _log;
        private readonly string _baseTempRoot;
        private readonly ICdWorkerRuntime _runtime;
        private readonly HashSet<int> _ownedWorkerPids = new HashSet<int>();

        private bool _disposed;

        // Test-only opt-in hook for bounded cleanup A/B diagnostics & fault-injection self-tests.
        internal CdWorkerCleanupMode CleanupModeForTestsOnly { get; set; } = CdWorkerCleanupMode.DefaultQuitThenClose;

        internal IReadOnlyCollection<int> OwnedWorkerPidsForTestsOnly => _ownedWorkerPids;

        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

        public CdIsolatedVisibleFlatSideVerifier(Action<string> log = null)
            : this(log, null, null)
        {
        }

        internal CdIsolatedVisibleFlatSideVerifier(
            Action<string> log,
            ICdWorkerRuntime runtimeOverride,
            string swExePathOverride)
        {
            _log = log;
            _swExePath = !string.IsNullOrWhiteSpace(swExePathOverride)
                ? swExePathOverride
                : ResolveSolidWorksExePath();
            _runtime = runtimeOverride ?? new LiveCdWorkerRuntime(this);
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
            if (inspectDelegate == null && _runtime is LiveCdWorkerRuntime)
                return Fail("NO_INSPECT_DELEGATE");
            if (string.IsNullOrWhiteSpace(drawingPath) || !File.Exists(drawingPath))
                return Fail("DRAWING_FILE_MISSING");
            if (string.IsNullOrWhiteSpace(viewName))
                return Fail("VIEW_NAME_MISSING");
            if (_runtime is LiveCdWorkerRuntime &&
                (string.IsNullOrWhiteSpace(_swExePath) || !File.Exists(_swExePath)))
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
                    int? killedPid = QuarantineAndTerminateOwnedWorker("COM_EXCEPTION_0x" + comEx.HResult.ToString("X8"));
                    if (attempt == 0)
                    {
                        restarted = true;
                        continue;
                    }
                    CdIsolatedVerifyResult failRes = Fail("WORKER_COM_EXCEPTION: 0x" + comEx.HResult.ToString("X8"));
                    failRes.QuarantinedWorkerPid = killedPid;
                    return failRes;
                }
                catch (Exception ex)
                {
                    Log("[CD ISO VERIFIER] Error on attempt " + (attempt + 1) + ": " + ex.Message);
                    if (!_runtime.IsWorkerAlive() && attempt == 0)
                    {
                        QuarantineAndTerminateOwnedWorker("WORKER_DEAD_EXCEPTION: " + ex.Message);
                        restarted = true;
                        continue;
                    }
                    int? killedPid = QuarantineAndTerminateOwnedWorker("WORKER_EXCEPTION: " + ex.Message);
                    CdIsolatedVerifyResult failRes = Fail("WORKER_EXCEPTION: " + ex.Message);
                    failRes.QuarantinedWorkerPid = killedPid;
                    return failRes;
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
            _runtime.EnsureWorker(
                _swExePath,
                Log,
                _ownedWorkerPids,
                reason => QuarantineAndTerminateOwnedWorker(reason));

            Directory.CreateDirectory(_baseTempRoot);
            string itemTempDir = Path.Combine(
                _baseTempRoot,
                Guid.NewGuid().ToString("N").Substring(0, 10));
            Directory.CreateDirectory(itemTempDir);

            string drwCopyPath = Path.Combine(itemTempDir, Path.GetFileName(drawingPath));
            CdIsolatedVerifyResult result = null;
            bool skipDocCloseInFinally = false;

            try
            {
                do
                {
                    Stopwatch stepSw = Stopwatch.StartNew();
                    Log(FormatStageLog("BEFORE_REPOINT", drawingPath, stepSw));

                    File.Copy(drawingPath, drwCopyPath, true);
                    EnsureWritableNormal(drwCopyPath);

                    // Verify zero stale/unexpected documents BEFORE opening anything or repointing in-session,
                    // and NEVER auto-close unowned documents if beforeOpen fails.
                    CdWorkerDocAuditResult beforeAudit = AuditWorkerDocs("beforeOpen", itemTempDir, allowZeroOnly: true);
                    if (!beforeAudit.IsSafeForZeroDocs)
                    {
                        skipDocCloseInFinally = true;
                        string reason = !beforeAudit.AuditSucceeded
                            ? "ISOLATED_DOC_AUDIT_FAILED_BEFORE_OPEN: " + beforeAudit.AuditError
                            : "ISOLATED_UNEXPECTED_DOC_PRESENT_BEFORE_OPEN: " + beforeAudit.Summary;
                        int? qPid = QuarantineAndTerminateOwnedWorker(reason);
                        result = FailSafety(reason, qPid);
                        break;
                    }

                    _runtime.CopyAndRepointPartDependencies(
                        drawingPath,
                        drwCopyPath,
                        itemTempDir,
                        expectedPartPath,
                        out string expectedTempPartPath);

                    _runtime.SafeSetDocumentVisible(true);
                    _runtime.TrySetCurrentWorkingDirectory(itemTempDir);

                    Log(FormatStageLog("BEFORE_OPENDOC", drawingPath, stepSw));
                    bool openOk = _runtime.TryOpenDrawing(drwCopyPath, out int openErr, out int openWarn, out string openDetail);
                    Log(FormatStageLog("AFTER_OPENDOC", drawingPath, stepSw) +
                        " ok=" + openOk + " err=" + openErr + " warn=" + openWarn +
                        (string.IsNullOrEmpty(openDetail) ? "" : " detail=" + openDetail));

                    if (!openOk)
                    {
                        result = Fail("ISOLATED_OPEN_DRAWING_FAILED err=" + openErr + " warn=" + openWarn);
                        break;
                    }

                    _runtime.KeepWorkerWindowHidden();

                    CdWorkerDocAuditResult openAudit = AuditWorkerDocs("afterOpen", itemTempDir, allowZeroOnly: false);
                    if (!openAudit.IsSafeForTempDocsOnly)
                    {
                        skipDocCloseInFinally = true;
                        string reason = !openAudit.AuditSucceeded
                            ? "ISOLATED_DOC_AUDIT_FAILED_AFTER_OPEN: " + openAudit.AuditError
                            : "ISOLATED_UNEXPECTED_DOC_LOADED_AFTER_OPEN: " + openAudit.Summary;
                        int? qPid = QuarantineAndTerminateOwnedWorker(reason);
                        result = FailSafety(reason, qPid);
                        break;
                    }

                    Log(FormatStageLog("BEFORE_DELEGATE", drawingPath, stepSw));
                    bool inspected = _runtime.TryResolveAndInspect(
                        sheetName,
                        viewName,
                        itemTempDir,
                        expectedTempPartPath,
                        inspectDelegate,
                        stepSw,
                        drawingPath,
                        Log,
                        out FlatDrawingViewSideResult sideResult,
                        out CheckDrawingBom.CdDirectFlatSideProbe probe,
                        out string inspectErr,
                        out bool unexpectedRefViolation);
                    Log(FormatStageLog("AFTER_DELEGATE", drawingPath, stepSw));

                    if (!inspected)
                    {
                        if (unexpectedRefViolation)
                        {
                            skipDocCloseInFinally = true;
                            int? qPid = QuarantineAndTerminateOwnedWorker(inspectErr);
                            result = FailSafety(inspectErr, qPid);
                        }
                        else
                        {
                            result = Fail(inspectErr);
                        }
                        break;
                    }

                    Log(FormatStageLog("BEFORE_AUDIT_AFTER_DELEGATE", drawingPath, stepSw));
                    CdWorkerDocAuditResult delAudit = AuditWorkerDocs("afterDelegate", itemTempDir, allowZeroOnly: false);
                    Log(FormatStageLog("AFTER_AUDIT_AFTER_DELEGATE", drawingPath, stepSw) + " auditOk=" + delAudit.AuditSucceeded);
                    if (!delAudit.IsSafeForTempDocsOnly)
                    {
                        skipDocCloseInFinally = true;
                        string reason = !delAudit.AuditSucceeded
                            ? "ISOLATED_DOC_AUDIT_FAILED_AFTER_DELEGATE: " + delAudit.AuditError
                            : "ISOLATED_UNEXPECTED_DOC_LOADED_AFTER_DELEGATE: " + delAudit.Summary;
                        int? qPid = QuarantineAndTerminateOwnedWorker(reason);
                        result = FailSafety(reason, qPid);
                        break;
                    }

                    result = new CdIsolatedVerifyResult
                    {
                        Success = true,
                        Result = sideResult,
                        Probe = probe
                    };
                }
                while (false);
            }
            finally
            {
                Stopwatch closeSw = Stopwatch.StartNew();
                if (!skipDocCloseInFinally)
                {
                    Log(FormatStageLog("BEFORE_CLOSE", drawingPath, closeSw) + " mode=" + CleanupModeForTestsOnly);
                    CdWorkerCleanupResult cleanupRes = CloseAllWorkerDocuments(itemTempDir, closeSw, drawingPath);
                    Log(FormatStageLog("AFTER_CLOSE", drawingPath, closeSw) +
                        " remainingDocs=" + cleanupRes.RemainingDocs +
                        " success=" + cleanupRes.Success +
                        " closeCalls=" + cleanupRes.CloseCallsExecuted +
                        " mode=" + CleanupModeForTestsOnly);

                    if (!cleanupRes.Success)
                    {
                        if (cleanupRes.RemainingDocs > 0)
                        {
                            AuditWorkerDocs("afterCloseNonZero", itemTempDir, allowZeroOnly: false);
                        }
                        int? qPid = QuarantineAndTerminateOwnedWorker("ISOLATED_CLEANUP_FAILED: " + cleanupRes.ErrorReason);
                        result = FailSafety("ISOLATED_CLEANUP_FAILED: " + cleanupRes.ErrorReason, qPid);
                    }
                }
                else
                {
                    Log(FormatStageLog("SKIP_DOC_CLOSE_SAFETY_VIOLATION", drawingPath, closeSw));
                }

                // Release current working directory handle off itemTempDir before deleting the temp folder.
                if (!skipDocCloseInFinally && _runtime.HasWorker)
                {
                    _runtime.TrySetCurrentWorkingDirectory(_baseTempRoot);
                }

                if (!TryDeleteDirectory(itemTempDir))
                {
                    Log("[CD ISO VERIFIER CLEANUP WARN] Failed to delete itemTempDir=" + itemTempDir);
                }
            }

            return result ?? Fail("UNKNOWN");
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

        private string FormatStageLog(string stage, string drawingPath, Stopwatch sw)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "[CD ISO VERIFIER STAGE] stage={0} pid={1} t={2} elapsedMs={3} file={4}",
                stage,
                _runtime.WorkerPid ?? 0,
                DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
                sw != null ? sw.ElapsedMilliseconds : 0,
                Path.GetFileName(drawingPath ?? ""));
        }

        private CdWorkerDocAuditResult AuditWorkerDocs(string stage, string itemTempDir, bool allowZeroOnly)
        {
            var result = new CdWorkerDocAuditResult();
            int pid = _runtime.WorkerPid ?? 0;

            if (!_runtime.HasWorker)
            {
                result.AuditSucceeded = false;
                result.AuditError = "WORKER_UNAVAILABLE";
                result.Summary = "audit_err=WORKER_UNAVAILABLE";
                Log("[CD ISO VERIFIER DOC AUDIT] stage=" + stage + " pid=" + pid + " auditOk=False error=WORKER_UNAVAILABLE");
                return result;
            }

            if (!_runtime.TryGetDocuments(out int reportedCount, out List<CdWorkerDocSnapshot> docs, out string enumError))
            {
                result.AuditSucceeded = false;
                result.AuditError = string.IsNullOrWhiteSpace(enumError) ? "ENUM_FAILED" : enumError;
                result.Summary = "audit_err=" + result.AuditError;
                Log("[CD ISO VERIFIER DOC AUDIT] stage=" + stage + " pid=" + pid + " auditOk=False error=" + result.AuditError);
                return result;
            }

            result.ReportedDocCount = reportedCount;
            var entries = new List<string>();
            if (docs != null)
            {
                foreach (CdWorkerDocSnapshot doc in docs)
                {
                    result.TotalCount++;
                    if (doc == null || !doc.ReadSucceeded)
                    {
                        result.UnreadableCount++;
                        string err = doc != null ? doc.ReadError : "null_snapshot";
                        result.OffendingDocs.Add("unreadable:" + (doc != null ? doc.Title : "") + ":" + err);
                        entries.Add("UNREADABLE:" + err);
                        continue;
                    }

                    bool isAsm = IsAssemblySnapshot(doc);
                    bool insideTemp = !string.IsNullOrWhiteSpace(doc.Path) && IsPathInsideDirectory(doc.Path, itemTempDir);
                    if (isAsm) result.AsmCount++;
                    if (!insideTemp) result.OutsideTempCount++;
                    if (allowZeroOnly || isAsm || !insideTemp)
                    {
                        result.OffendingDocs.Add(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0}|asm={1}|inTemp={2}|path={3}",
                            doc.Title,
                            isAsm,
                            insideTemp,
                            doc.Path));
                    }

                    entries.Add(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0}:type={1}:inTemp={2}:asm={3}:dirty={4}:ro={5}:path={6}",
                        doc.Title,
                        doc.DocType,
                        insideTemp,
                        isAsm,
                        doc.Dirty,
                        doc.ReadOnly,
                        doc.Path));
                }
            }

            if (result.UnreadableCount > 0)
            {
                result.AuditSucceeded = false;
                result.AuditError = "UNREADABLE_DOC_PROPERTY count=" + result.UnreadableCount;
            }
            else
            {
                result.AuditSucceeded = true;
            }

            result.Summary = entries.Count > 0 ? string.Join(" ; ", entries) : "none";
            Log("[CD ISO VERIFIER DOC AUDIT] stage=" + stage +
                " pid=" + pid +
                " auditOk=" + result.AuditSucceeded +
                " count=" + result.TotalCount +
                " outsideTemp=" + result.OutsideTempCount +
                " asmCount=" + result.AsmCount +
                " unreadable=" + result.UnreadableCount +
                " docs=[" + result.Summary + "]");
            return result;
        }

        private CdWorkerCleanupResult CloseAllWorkerDocuments(string ownedTempDir, Stopwatch closeSw, string drawingPath)
        {
            var res = new CdWorkerCleanupResult();
            if (!_runtime.HasWorker)
            {
                res.Success = true;
                res.RemainingDocs = 0;
                return res;
            }

            for (int guard = 0; guard < 16; guard++)
            {
                if (!_runtime.TryGetDocuments(out int docCount, out List<CdWorkerDocSnapshot> docs, out string enumErr))
                {
                    res.Success = false;
                    res.RemainingDocs = -1;
                    res.ErrorReason = "PRE_CLOSE_ENUM_FAILED: " + enumErr;
                    return res;
                }

                if (docCount == 0 && (docs == null || docs.Count == 0))
                    break;

                // Ownership guard BEFORE closing anything: never call QuitDoc/CloseDoc/CloseAllDocuments
                // if any open document is unreadable, is an Assembly, or sits outside ownedTempDir.
                if (docs != null)
                {
                    foreach (CdWorkerDocSnapshot doc in docs)
                    {
                        bool isAsm = doc == null || IsAssemblySnapshot(doc);
                        bool insideOwned = doc != null &&
                            doc.ReadSucceeded &&
                            !string.IsNullOrWhiteSpace(doc.Path) &&
                            IsPathInsideDirectory(doc.Path, ownedTempDir);

                        if (doc == null || !doc.ReadSucceeded || isAsm || !insideOwned)
                        {
                            string desc = doc == null
                                ? "null_doc"
                                : string.Format(
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    "title={0}|asm={1}|inTemp={2}|readOk={3}|path={4}|err={5}",
                                    doc.Title,
                                    isAsm,
                                    insideOwned,
                                    doc.ReadSucceeded,
                                    doc.Path,
                                    doc.ReadError);
                            res.UnownedDocsSkipped.Add(desc);
                        }
                    }
                }

                if (res.UnownedDocsSkipped.Count > 0)
                {
                    res.Success = false;
                    res.RemainingDocs = docs != null ? docs.Count : docCount;
                    res.ErrorReason = "UNOWNED_OR_UNEXPECTED_DOC_IN_CLEANUP: " + string.Join("; ", res.UnownedDocsSkipped);
                    Log("[CD ISO VERIFIER CLEANUP ABORT] pid=" + (_runtime.WorkerPid ?? 0) + " skippedUnowned=" + res.ErrorReason);
                    return res;
                }

                if (CleanupModeForTestsOnly == CdWorkerCleanupMode.CloseAllOwnedDocs)
                {
                    Log(FormatStageLog("BEFORE_CLOSEALL", drawingPath, closeSw));
                    bool okAll = _runtime.TryCloseAllDocuments(true, out string closeAllErr);
                    res.CloseCallsExecuted++;
                    Log(FormatStageLog("AFTER_CLOSEALL", drawingPath, closeSw) + " ok=" + okAll +
                        (string.IsNullOrEmpty(closeAllErr) ? "" : " err=" + closeAllErr));
                    break;
                }

                // Close Drawings before Parts (Assemblies were already rejected above).
                var targets = new List<KeyValuePair<int, string>>();
                if (docs != null)
                {
                    foreach (CdWorkerDocSnapshot doc in docs)
                    {
                        if (string.IsNullOrWhiteSpace(doc.Title))
                        {
                            res.Success = false;
                            res.RemainingDocs = docs.Count;
                            res.ErrorReason = "EMPTY_DOC_TITLE_IN_CLEANUP path=" + doc.Path;
                            return res;
                        }

                        int prio = doc.DocType == (int)swDocumentTypes_e.swDocDRAWING ? 0
                                 : doc.DocType == (int)swDocumentTypes_e.swDocASSEMBLY ? 1
                                 : doc.DocType == (int)swDocumentTypes_e.swDocPART ? 2 : 3;
                        targets.Add(new KeyValuePair<int, string>(prio, doc.Title));
                    }
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

                    string title = targets[i].Value;
                    bool closed = CloseSingleOwnedDoc(title, topPriority, closeSw, drawingPath, out string methodUsed);
                    res.CloseCallsExecuted++;
                    if (closed)
                        closedAny = true;
                    else
                        Log("[CD ISO VERIFIER CLEANUP WARN] Failed " + methodUsed + " target=" + title);
                }

                if (!closedAny)
                    break;
            }

            _runtime.SafeSetDocumentVisible(false);

            if (!_runtime.TryGetDocuments(out int remCount, out List<CdWorkerDocSnapshot> remDocs, out string postErr))
            {
                res.Success = false;
                res.RemainingDocs = -1;
                res.ErrorReason = "POST_CLOSE_ENUM_FAILED: " + postErr;
                return res;
            }

            int actualCount = remDocs != null ? remDocs.Count : 0;
            res.RemainingDocs = remCount != 0 ? remCount : actualCount;
            if (remCount != 0 || actualCount != 0)
            {
                res.Success = false;
                res.ErrorReason = "POST_CLOSE_NONZERO_REMAINING reported=" + remCount + " enumerated=" + actualCount;
                return res;
            }

            res.Success = true;
            res.RemainingDocs = 0;
            return res;
        }

        private bool CloseSingleOwnedDoc(
            string title,
            int prio,
            Stopwatch closeSw,
            string drawingPath,
            out string methodUsed)
        {
            Log(FormatStageLog("BEFORE_CLOSE_TARGET", drawingPath, closeSw) +
                " mode=" + CleanupModeForTestsOnly + " prio=" + prio + " target=" + title);

            bool ok;
            if (CleanupModeForTestsOnly == CdWorkerCleanupMode.CloseDocFirst)
            {
                methodUsed = "CloseDoc";
                ok = _runtime.TryCloseDoc(title, out _);
                if (!ok)
                {
                    methodUsed = "QuitDocFallback";
                    ok = _runtime.TryQuitDoc(title, out _);
                }
            }
            else
            {
                methodUsed = "QuitDoc";
                ok = _runtime.TryQuitDoc(title, out _);
                if (!ok)
                {
                    methodUsed = "CloseDocFallback";
                    ok = _runtime.TryCloseDoc(title, out _);
                }
            }

            Log(FormatStageLog("AFTER_CLOSE_TARGET", drawingPath, closeSw) +
                " method=" + methodUsed + " ok=" + ok + " target=" + title);
            return ok;
        }

        private static bool IsAssemblySnapshot(CdWorkerDocSnapshot doc)
        {
            if (doc == null)
                return false;
            return doc.DocType == (int)swDocumentTypes_e.swDocASSEMBLY ||
                (!string.IsNullOrWhiteSpace(doc.Path) && doc.Path.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(doc.Title) && doc.Title.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase));
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

        private int? QuarantineAndTerminateOwnedWorker(string reason)
        {
            int? pid = _runtime.WorkerPid;
            if (_runtime.HasWorker || pid.HasValue)
            {
                Log("[CD ISO VERIFIER QUARANTINE] pid=" + (pid ?? 0) + " reason=" + (reason ?? "UNKNOWN"));
            }

            // Never call CloseAllWorkerDocuments(), QuitDoc(), CloseDoc(), or ExitApp() on quarantine:
            // detach COM references only and terminate ONLY verified owned child SLDWORKS PID.
            _runtime.DetachComReferenceOnly();

            if (pid.HasValue)
            {
                _runtime.TerminateOwnedProcessOnly(pid.Value, _ownedWorkerPids, Log);
            }

            return pid;
        }

        private void GracefulDisposeWorker()
        {
            if (!_runtime.HasWorker && !_runtime.WorkerPid.HasValue)
                return;

            if (!_runtime.HasWorker)
            {
                QuarantineAndTerminateOwnedWorker("DISPOSE_CLEANUP_ORPHAN_PID");
                return;
            }

            CdWorkerDocAuditResult disposeAudit = AuditWorkerDocs("dispose", _baseTempRoot, allowZeroOnly: true);
            if (!disposeAudit.IsSafeForZeroDocs)
            {
                QuarantineAndTerminateOwnedWorker("DISPOSE_UNSAFE_OR_NONZERO_DOCS: " +
                    (!disposeAudit.AuditSucceeded ? disposeAudit.AuditError : disposeAudit.Summary));
                return;
            }

            int? pid = _runtime.WorkerPid;
            _runtime.GracefulExitIfClean();
            if (pid.HasValue)
            {
                _runtime.TerminateOwnedProcessOnly(pid.Value, _ownedWorkerPids, Log);
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

        private static CdIsolatedVerifyResult Fail(string reason)
        {
            return new CdIsolatedVerifyResult
            {
                Success = false,
                ErrorReason = reason ?? "UNKNOWN"
            };
        }

        private static CdIsolatedVerifyResult FailSafety(string reason, int? quarantinedPid)
        {
            return new CdIsolatedVerifyResult
            {
                Success = false,
                ErrorReason = reason ?? "UNKNOWN",
                SafetyViolation = true,
                QuarantinedWorkerPid = quarantinedPid
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
            GracefulDisposeWorker();
            TryDeleteDirectory(_baseTempRoot);
        }

        private sealed class LiveCdWorkerRuntime : ICdWorkerRuntime
        {
            private readonly CdIsolatedVisibleFlatSideVerifier _owner;
            private ISldWorks _workerApp;
            private int? _workerPid;
            private ModelDoc2 _openDrawingDoc;

            public LiveCdWorkerRuntime(CdIsolatedVisibleFlatSideVerifier owner)
            {
                _owner = owner;
            }

            public bool HasWorker => _workerApp != null;
            public int? WorkerPid => _workerPid;
            public ISldWorks LiveApp => _workerApp;

            public bool IsWorkerAlive()
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

            public void EnsureWorker(
                string swExePath,
                Action<string> log,
                HashSet<int> ownedPids,
                Action<string> quarantineCallback)
            {
                if (IsWorkerAlive())
                    return;

                quarantineCallback?.Invoke("ENSURE_WORKER_REPLACE_DEAD");

                var psi = new ProcessStartInfo(swExePath)
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process proc = Process.Start(psi);
                if (proc == null)
                    throw new InvalidOperationException("Failed to start isolated SLDWORKS.exe process.");

                ownedPids.Add(proc.Id);
                _workerPid = proc.Id;
                string expectedMoniker = "SolidWorks_PID_" + proc.Id;
                log?.Invoke("[CD ISO VERIFIER] Starting hidden worker pid=" + proc.Id);

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
                    quarantineCallback?.Invoke("ENSURE_WORKER_STARTUP_TIMEOUT");
                    throw new TimeoutException("Timed out waiting for ROT moniker " + expectedMoniker);
                }

                _workerApp = app;
                CdIsolatedVisibleFlatSideVerifier.KeepWorkerWindowHidden(_workerApp);
                try { _workerApp.UserControl = false; } catch { }
                log?.Invoke("[CD ISO VERIFIER] Worker ready pid=" + proc.Id);
            }

            public bool TryGetDocuments(out int reportedCount, out List<CdWorkerDocSnapshot> docs, out string error)
            {
                reportedCount = -1;
                docs = new List<CdWorkerDocSnapshot>();
                error = "";
                if (_workerApp == null)
                {
                    error = "WORKER_APP_NULL";
                    return false;
                }

                try
                {
                    reportedCount = _workerApp.GetDocumentCount();
                    if (reportedCount < 0)
                    {
                        error = "NEGATIVE_DOC_COUNT=" + reportedCount;
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    error = "GetDocumentCount_ERR: " + ex.Message;
                    return false;
                }

                Array rawDocs;
                try
                {
                    rawDocs = _workerApp.GetDocuments() as Array;
                }
                catch (Exception ex)
                {
                    error = "GetDocuments_ERR: " + ex.Message;
                    return false;
                }

                if (rawDocs == null)
                {
                    if (reportedCount == 0)
                        return true;
                    error = "GetDocuments_NULL_WITH_COUNT=" + reportedCount;
                    return false;
                }

                for (int i = rawDocs.GetLowerBound(0); i <= rawDocs.GetUpperBound(0); i++)
                {
                    ModelDoc2 md = rawDocs.GetValue(i) as ModelDoc2;
                    if (md == null)
                    {
                        docs.Add(new CdWorkerDocSnapshot
                        {
                            ReadSucceeded = false,
                            ReadError = "NULL_MODELDOC2_AT_" + i
                        });
                        continue;
                    }

                    var snap = new CdWorkerDocSnapshot();
                    var errParts = new List<string>();
                    try { snap.DocType = md.GetType(); } catch (Exception ex) { errParts.Add("GetType:" + ex.Message); }
                    try { snap.Path = md.GetPathName() ?? ""; } catch (Exception ex) { errParts.Add("GetPathName:" + ex.Message); }
                    try { snap.Title = md.GetTitle() ?? ""; } catch (Exception ex) { errParts.Add("GetTitle:" + ex.Message); }
                    try { snap.Dirty = md.GetSaveFlag(); } catch (Exception ex) { errParts.Add("GetSaveFlag:" + ex.Message); }
                    try { snap.ReadOnly = md.IsOpenedReadOnly(); } catch (Exception ex) { errParts.Add("IsOpenedReadOnly:" + ex.Message); }

                    snap.ReadSucceeded = errParts.Count == 0;
                    snap.ReadError = errParts.Count > 0 ? string.Join("; ", errParts) : "";
                    docs.Add(snap);
                }

                if (docs.Count != reportedCount)
                {
                    error = "DOC_COUNT_MISMATCH reported=" + reportedCount + " enumerated=" + docs.Count;
                    return false;
                }

                return true;
            }

            public void CopyAndRepointPartDependencies(
                string origDrawingPath,
                string drwCopyPath,
                string itemTempDir,
                string expectedPartPath,
                out string expectedTempPartPath)
            {
                _owner.CopyAndRepointPartDependencies(
                    _workerApp,
                    origDrawingPath,
                    drwCopyPath,
                    itemTempDir,
                    expectedPartPath,
                    out expectedTempPartPath);
            }

            public void SafeSetDocumentVisible(bool visible)
            {
                CdIsolatedVisibleFlatSideVerifier.SafeSetDocumentVisible(_workerApp, visible);
            }

            public void KeepWorkerWindowHidden()
            {
                CdIsolatedVisibleFlatSideVerifier.KeepWorkerWindowHidden(_workerApp);
            }

            public bool TrySetCurrentWorkingDirectory(string dirPath)
            {
                if (_workerApp == null || string.IsNullOrWhiteSpace(dirPath))
                    return false;
                try
                {
                    return _workerApp.SetCurrentWorkingDirectory(dirPath);
                }
                catch
                {
                    return false;
                }
            }

            public bool TryOpenDrawing(string drwCopyPath, out int openErr, out int openWarn, out string openErrorDetail)
            {
                openErr = 0;
                openWarn = 0;
                openErrorDetail = "";
                _openDrawingDoc = null;
                if (_workerApp == null)
                {
                    openErrorDetail = "WORKER_APP_NULL";
                    return false;
                }

                _openDrawingDoc = _workerApp.OpenDoc6(
                    drwCopyPath,
                    (int)swDocumentTypes_e.swDocDRAWING,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref openErr,
                    ref openWarn) as ModelDoc2;
                return _openDrawingDoc != null;
            }

            public bool TryResolveAndInspect(
                string sheetName,
                string viewName,
                string itemTempDir,
                string expectedTempPartPath,
                CdIsolatedInspectDelegate inspectDelegate,
                Stopwatch stepSw,
                string drawingPath,
                Action<string> log,
                out FlatDrawingViewSideResult sideResult,
                out CheckDrawingBom.CdDirectFlatSideProbe probe,
                out string errorReason,
                out bool isUnexpectedRefViolation)
            {
                sideResult = default;
                probe = null;
                errorReason = "";
                isUnexpectedRefViolation = false;

                ModelDoc2 drwDoc = _openDrawingDoc;
                if (drwDoc == null)
                {
                    errorReason = "DRAWING_NOT_OPEN";
                    return false;
                }

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
                {
                    errorReason = "NOT_A_DRAWING_DOC";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(sheetName))
                {
                    try { drawing.ActivateSheet(sheetName); }
                    catch { }
                }

                SolidWorks.Interop.sldworks.View targetView = FindTargetView(drawing, sheetName, viewName);
                if (targetView == null)
                {
                    errorReason = "VIEW_NOT_FOUND_IN_ISOLATED_COPY: " + sheetName + "/" + viewName;
                    return false;
                }

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
                    errorReason = "ISOLATED_PATH_GUARD_BLOCKED: actualRef=" + actualRefPath + " expected=" + expectedTempPartPath;
                    isUnexpectedRefViolation = true;
                    return false;
                }

                string partPathForDelegate = !string.IsNullOrWhiteSpace(expectedTempPartPath)
                    ? expectedTempPartPath
                    : actualRefPath;

                sideResult = inspectDelegate(
                    _workerApp,
                    targetView,
                    drwDoc,
                    partPathForDelegate,
                    out probe);
                return true;
            }

            public bool TryQuitDoc(string title, out string error)
            {
                error = "";
                if (_workerApp == null)
                {
                    error = "WORKER_APP_NULL";
                    return false;
                }
                try
                {
                    _workerApp.QuitDoc(title);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            public bool TryCloseDoc(string title, out string error)
            {
                error = "";
                if (_workerApp == null)
                {
                    error = "WORKER_APP_NULL";
                    return false;
                }
                try
                {
                    _workerApp.CloseDoc(title);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            public bool TryCloseAllDocuments(bool includeUnsaved, out string error)
            {
                error = "";
                if (_workerApp == null)
                {
                    error = "WORKER_APP_NULL";
                    return false;
                }
                try
                {
                    return _workerApp.CloseAllDocuments(includeUnsaved);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            public void GracefulExitIfClean()
            {
                if (_workerApp == null)
                    return;
                try { _workerApp.ExitApp(); } catch { }
                DetachComReferenceOnly();
            }

            public void DetachComReferenceOnly()
            {
                _openDrawingDoc = null;
                if (_workerApp != null)
                {
                    try { Marshal.FinalReleaseComObject(_workerApp); } catch { }
                    _workerApp = null;
                }
                _workerPid = null;
            }

            public bool TerminateOwnedProcessOnly(int pid, HashSet<int> ownedPids, Action<string> log)
            {
                int currentPid = Process.GetCurrentProcess().Id;
                if (pid <= 0 || pid == currentPid || ownedPids == null || !ownedPids.Contains(pid))
                {
                    log?.Invoke("[CD ISO VERIFIER TERMINATE BLOCKED] Refused to terminate unverified/unowned pid=" + pid);
                    return false;
                }

                try
                {
                    using (Process proc = Process.GetProcessById(pid))
                    {
                        if (!string.Equals(proc.ProcessName, "SLDWORKS", StringComparison.OrdinalIgnoreCase))
                        {
                            log?.Invoke("[CD ISO VERIFIER TERMINATE BLOCKED] Pid=" + pid + " processName=" + proc.ProcessName + " is not SLDWORKS.");
                            return false;
                        }

                        if (!proc.HasExited)
                        {
                            if (!proc.WaitForExit(3000))
                            {
                                proc.Kill();
                                proc.WaitForExit(3000);
                            }
                        }
                    }
                    return true;
                }
                catch (ArgumentException)
                {
                    // Process already exited.
                    return true;
                }
                catch (Exception ex)
                {
                    log?.Invoke("[CD ISO VERIFIER TERMINATE WARN] pid=" + pid + " err=" + ex.Message);
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
        }
    }
}
