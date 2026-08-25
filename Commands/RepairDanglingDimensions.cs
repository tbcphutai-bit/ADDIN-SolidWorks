using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public static class RepairDanglingDimensions
    {
        private const string REPAIR_DIM_BUILD = "STEP11D_BROKEN_VIEW_FULLY_LOST_REPAIR_20260825";

        private class BatchTargetSnapshot
        {
            public int TargetIndex { get; set; }
            public string SheetName { get; set; }
            public string ViewName { get; set; }
            public string DimensionName { get; set; }
            public string OldDimFullName { get; set; }
            public swDimensionType_e DimensionType { get; set; }
            public double? SystemValue { get; set; }
            public double[] Position { get; set; }
            public List<int> AttachedEntityTypes { get; set; } = new List<int>();
            public RepairDimFailureMode FailureMode { get; set; }
            public string CandidateDecision { get; set; }
            public string AnchorOccurrenceKey { get; set; }
            public string CandidateOccurrenceKey { get; set; }
        }

        private enum SingleTargetStatus
        {
            Success,
            Skipped,
            ManualReview,
            Failed
        }

        private class SingleTargetRepairResult
        {
            public SingleTargetStatus Status { get; set; }
            public string Reason { get; set; } = "";
            public bool IsUnsafeState { get; set; }
            public int PostDisplayCount { get; set; }
            public int PostDanglingCount { get; set; }
        }

        public static void Run(ISldWorks swApp, DrawingDoc swDrawing)
        {
            if (swApp == null || swDrawing == null)
            {
                MessageBox.Show(
                    "swApp hoặc swDrawing = null.",
                    "REPAIR DIM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            ModelDoc2 swModel = swDrawing as ModelDoc2;
            if (swModel == null || swModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                MessageBox.Show(
                    "REPAIR DIM chỉ sử dụng trong môi trường Drawing.",
                    "REPAIR DIM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            InitLog();
            LogDebug($"=== REPAIR DIM BUILD: {REPAIR_DIM_BUILD} ===");
            LogDebug("=== REPAIR DIM SESSION START (STEP 11D: BROKEN VIEW FULLY LOST REPAIR) ===");

            try
            {
                string dllPath = typeof(RepairDanglingDimensions).Assembly.Location;
                LogDebug($"DLL Path          : {dllPath}");

                if (File.Exists(dllPath))
                {
                    LogDebug($"DLL LastWriteTime : {File.GetLastWriteTime(dllPath):yyyy-MM-dd HH:mm:ss}");
                }

                LogDebug($"Assembly Version  : {typeof(RepairDanglingDimensions).Assembly.GetName().Version}");
            }
            catch (Exception ex)
            {
                LogDebug("DLL INFO ERROR: " + ex.Message);
            }

            string docTitle = swModel.GetTitle() ?? "";
            string docPath = swModel.GetPathName() ?? "";
            LogDebug($"Drawing Title: {docTitle}");
            LogDebug($"Drawing Path : {docPath}");

            // COPY FILE GUARD
            bool isCopyFile = docTitle.Contains("コピー") || docPath.Contains("コピー") ||
                              docTitle.IndexOf("copy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              docPath.IndexOf("copy", StringComparison.OrdinalIgnoreCase) >= 0;

            LogDebug($"Copy File Guard Check: Title='{docTitle}', Path='{docPath}', IsCopy={isCopyFile}");

            // Baseline Counts Before Any Mutation
            int initialDrawingDisplayDimCount = 0;
            int initialDrawingDanglingCount = 0;
            CountTotalDrawingDimensions(
                swDrawing,
                out initialDrawingDisplayDimCount,
                out initialDrawingDanglingCount);

            LogDebug($"Baseline Counts: DrawingDisplayDims={initialDrawingDisplayDimCount}, DrawingDangling={initialDrawingDanglingCount}");

            // 1. DOCUMENT-LEVEL MISSING MODEL REFERENCE SCAN (Semantic Name + Path Pair Parsing)
            List<DocumentDependencyInfo> dependencies = ScanMissingModelReferences(swApp, swDrawing, swModel);

            LogDebug("\n=== MODEL REFERENCE STATUS ===");
            if (dependencies.Count == 0)
            {
                LogDebug("  No document dependencies found.");
            }
            else
            {
                foreach (var dep in dependencies)
                {
                    LogDebug($"\nDependency #{dep.Index}");
                    LogDebug($"  Name           : {dep.Name}");
                    LogDebug($"  Path           : {dep.Path}");
                    LogDebug($"  Valid File Path: {dep.IsValidFilePath}");
                    LogDebug($"  Exists         : {dep.FileExists}");
                    LogDebug($"  Status         : {(dep.IsResolved ? "RESOLVED" : (dep.IsValidFilePath ? "MISSING / UNRESOLVED" : "NAME_ONLY"))}");
                }
            }

            // =========================================================================
            // PHASE 1 — INITIAL SCAN (COLLECT STEP 10 1-LIVE-ANCHOR TARGETS)
            // =========================================================================
            int totalDisplayDimensions = 0;
            int totalDanglingDimensions = 0;
            List<DanglingDimensionInfo> initialDanglingList = new List<DanglingDimensionInfo>();
            List<BatchTargetSnapshot> step10BatchTargets = new List<BatchTargetSnapshot>();

            string initialSheet = "";
            try
            {
                Sheet activeSheet = swDrawing.GetCurrentSheet() as Sheet;
                if (activeSheet != null)
                {
                    initialSheet = activeSheet.GetName();
                }
            }
            catch {}

            try
            {
                string[] sheetNames = swDrawing.GetSheetNames() as string[];
                if (sheetNames == null || sheetNames.Length == 0)
                {
                    MessageBox.Show(
                        "Không tìm thấy Sheet nào trong bản vẽ.",
                        "REPAIR DIM",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                foreach (string sheetName in sheetNames)
                {
                    swDrawing.ActivateSheet(sheetName);
                    LogDebug($"\nScanning Sheet: {sheetName}");

                    SolidWorks.Interop.sldworks.View sheetView = swDrawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                    SolidWorks.Interop.sldworks.View currentView = sheetView?.GetNextView() as SolidWorks.Interop.sldworks.View;

                    while (currentView != null)
                    {
                        bool viewModelResolved = true;
                        string viewRefModelName = "";
                        try { viewRefModelName = currentView.GetReferencedModelName() ?? ""; } catch {}

                        ModelDoc2 refDoc = null;
                        try { refDoc = currentView.ReferencedDocument; } catch {}

                        string refDocPath = "";
                        try { refDocPath = refDoc?.GetPathName() ?? ""; } catch {}

                        bool viewRefExists = false;
                        if (!string.IsNullOrEmpty(viewRefModelName) && IsValidSolidWorksFilePath(viewRefModelName))
                        {
                            try { viewRefExists = File.Exists(viewRefModelName); } catch {}
                        }

                        bool refDocExists = false;
                        if (!string.IsNullOrEmpty(refDocPath) && IsValidSolidWorksFilePath(refDocPath))
                        {
                            try { refDocExists = File.Exists(refDocPath); } catch {}
                        }

                        if (viewRefExists || refDocExists || refDoc != null)
                        {
                            viewModelResolved = true;
                        }
                        else if (!string.IsNullOrEmpty(viewRefModelName) && IsValidSolidWorksFilePath(viewRefModelName) && !viewRefExists)
                        {
                            viewModelResolved = false;
                        }
                        else
                        {
                            viewModelResolved = true;
                        }

                        ViewGeometryInfo viewGeom = RepairDimCandidateFinder.EnumerateViewGeometry(swApp, currentView);

                        LogDebug($"\n--------------------------------------------------");
                        LogDebug($"VIEW: {viewGeom.ViewName} (Type: {viewGeom.ViewTypeString})");
                        LogDebug($"  Referenced Document    : {viewGeom.ReferencedDoc}");
                        LogDebug($"  Referenced Config      : {viewGeom.ReferencedConfig}");
                        LogDebug($"  Referenced Model Name  : {viewRefModelName}");
                        LogDebug($"  View Model Resolved    : {viewModelResolved} (RefExists: {viewRefExists}, DocExists: {refDocExists})");
                        LogDebug($"  View Scale Ratio       : {viewGeom.ScaleRatio}");
                        LogDebug($"  Visible Components     : {viewGeom.VisibleComponentCount}");
                        LogDebug($"  Visible Edges          : {viewGeom.VisibleEdgeCount} (Unique: {viewGeom.Edges.Count})");
                        LogDebug($"  Repair Line Records    : {viewGeom.RepairLineRecords.Count}");
                        LogDebug($"--------------------------------------------------");

                        DisplayDimension dispDim = currentView.GetFirstDisplayDimension5() as DisplayDimension;
                        while (dispDim != null)
                        {
                            totalDisplayDimensions++;
                            Annotation annot = dispDim.GetAnnotation() as Annotation;

                            bool isDangling = (annot != null) && annot.IsDangling();

                            if (isDangling)
                            {
                                totalDanglingDimensions++;

                                DanglingDimensionInfo info = ExtractDanglingInfo(sheetName, viewGeom.ViewName, dispDim, annot);

                                if (viewModelResolved)
                                {
                                    RepairDimCandidateFinder.AnalyzeCandidatesForDimension(swApp, info, viewGeom, currentView, dispDim);
                                }
                                else
                                {
                                    info.CandidateDecision = "MODEL_FILE_UNRESOLVED";
                                    info.DiagnosticNotes.Add($"ViewModelUnresolved: Model path '{viewRefModelName}' missing or unresolved.");
                                }

                                ClassifyFailureMode(info, viewGeom, viewModelResolved, viewRefModelName);
                                initialDanglingList.Add(info);
                                LogDanglingDetail(info, viewGeom);

                                // Check STEP 10 1-Live-Anchor Batch Eligibility
                                bool isStep10Eligible = (info.CandidateDecision == "HIGH_CONFIDENCE") &&
                                                        (info.FailureMode == RepairDimFailureMode.ComponentReinsertedOrGeometryReplaced) &&
                                                        (info.RecommendedAction == "RECREATE_DIMENSION_REQUIRED") &&
                                                        (info.AnchorEntityType == (int)swSelectType_e.swSelEDGES) &&
                                                        (info.AnchorPolylineMatches.Count > 0) &&
                                                        (info.Candidates.Count > 0);

                                if (isStep10Eligible)
                                {
                                    step10BatchTargets.Add(new BatchTargetSnapshot
                                    {
                                        TargetIndex = step10BatchTargets.Count + 1,
                                        SheetName = sheetName,
                                        ViewName = viewGeom.ViewName,
                                        DimensionName = annot.GetName() ?? info.DimensionName,
                                        OldDimFullName = info.DimensionName,
                                        DimensionType = info.DimensionType,
                                        SystemValue = info.SystemValue,
                                        Position = info.Position != null ? new double[] { info.Position[0], info.Position[1], info.Position[2] } : null,
                                        AttachedEntityTypes = new List<int>(info.AttachedEntityTypes),
                                        FailureMode = info.FailureMode,
                                        CandidateDecision = info.CandidateDecision,
                                        AnchorOccurrenceKey = info.AnchorOccurrenceKey,
                                        CandidateOccurrenceKey = info.Candidates[0].ComponentOccurrenceKey
                                    });
                                }
                            }

                            dispDim = dispDim.GetNext5() as DisplayDimension;
                        }

                        currentView = currentView.GetNextView() as SolidWorks.Interop.sldworks.View;
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug("ERROR during initial scan: " + ex.Message);
                MessageBox.Show(
                    "Lỗi trong quá trình quét Dangling Dimensions:\n" + ex.Message,
                    "REPAIR DIM - ERROR",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
            finally
            {
                if (!string.IsNullOrEmpty(initialSheet))
                {
                    try { swDrawing.ActivateSheet(initialSheet); } catch {}
                }
            }

            LogDebug($"\n=== PHASE 1 SCAN COMPLETE ===");
            LogDebug($"Total Display Dimensions : {totalDisplayDimensions}");
            LogDebug($"Total Dangling Dimensions: {totalDanglingDimensions}");
            LogDebug($"STEP 10 Batch Targets    : {step10BatchTargets.Count}");

            if (!isCopyFile)
            {
                LogDebug("BATCH_ABORT_NOT_COPY_DRAWING (File title/path does not contain 'コピー')");
                MessageBox.Show(
                    "REPAIR DIM ABORT: Drawing file is NOT a copy file (Title/Path does not contain 'コピー').\nMutation cancelled for safety.",
                    "REPAIR DIM - GUARD",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // =========================================================================
            // PHASE 2 — RUN STEP 10 BATCH (1-LIVE-ANCHOR HIGH_CONFIDENCE TARGETS)
            // =========================================================================
            int runningDisplayCount = initialDrawingDisplayDimCount;
            int runningDanglingCount = initialDrawingDanglingCount;
            int step10SuccessCount = 0;
            bool step10Aborted = false;

            if (step10BatchTargets.Count > 0)
            {
                LogDebug("\n=== STEP 10 BATCH START ===");
                LogDebug($"Initial Display        : {runningDisplayCount}");
                LogDebug($"Initial Dangling       : {runningDanglingCount}");
                LogDebug($"Batch Target Count     : {step10BatchTargets.Count}");

                for (int tIdx = 0; tIdx < step10BatchTargets.Count; tIdx++)
                {
                    var target = step10BatchTargets[tIdx];
                    int targetNum = tIdx + 1;

                    LogDebug($"\n--------------------------------");
                    LogDebug($"STEP10 TARGET {targetNum}/{step10BatchTargets.Count}");
                    LogDebug($"Old Full Name: {target.OldDimFullName}");
                    LogDebug($"View         : {target.ViewName}");
                    LogDebug($"Value        : {(target.SystemValue.HasValue ? $"{target.SystemValue.Value * 1000.0:F6} mm" : "<null>")}");

                    SingleTargetRepairResult res = ExecuteSingleStep10TargetRepair(
                        swApp,
                        swDrawing,
                        swModel,
                        target,
                        targetNum,
                        step10BatchTargets.Count,
                        runningDisplayCount,
                        runningDanglingCount);

                    if (res.Status == SingleTargetStatus.Success)
                    {
                        step10SuccessCount++;
                        runningDisplayCount = res.PostDisplayCount;
                        runningDanglingCount = res.PostDanglingCount;
                        LogDebug($"TARGET RESULT: SUCCESS");
                    }
                    else if (res.Status == SingleTargetStatus.Skipped)
                    {
                        LogDebug($"TARGET RESULT: SKIPPED ({res.Reason})");
                    }
                    else if (res.Status == SingleTargetStatus.Failed)
                    {
                        LogDebug($"TARGET RESULT: FAILED ({res.Reason})");
                        if (res.IsUnsafeState)
                        {
                            LogDebug("STEP10 BATCH_ABORT_UNSAFE_STATE: Aborting remaining batch.");
                            step10Aborted = true;
                            break;
                        }
                    }
                }

                LogDebug($"\n=== STEP 10 BATCH FINISHED: Repaired={step10SuccessCount}/{step10BatchTargets.Count}, RunningDisplay={runningDisplayCount}, RunningDangling={runningDanglingCount} ===");
            }

            if (step10Aborted)
            {
                LogDebug("STEP 11B ABORTED: STEP 10 encountered unsafe state.");
                MessageBox.Show("STEP 10 encountered unsafe state. STEP 11B aborted.", "REPAIR DIM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // =========================================================================
            // PHASE 3 — FRESH SCAN & GENERIC FULLY LOST BATCH (STEP 11B)
            // =========================================================================
            LogDebug("\n=== PHASE 3: FRESH SCAN FOR GENERIC FULLY LOST BATCH ===");
            List<BatchTargetSnapshot> fullyLostBatchTargets = new List<BatchTargetSnapshot>();

            try
            {
                if (!string.IsNullOrEmpty(initialSheet)) { try { swDrawing.ActivateSheet(initialSheet); } catch {} }

                string[] sheetNames = swDrawing.GetSheetNames() as string[];
                if (sheetNames != null)
                {
                    foreach (string sheetName in sheetNames)
                    {
                        swDrawing.ActivateSheet(sheetName);
                        SolidWorks.Interop.sldworks.View sheetView = swDrawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                        SolidWorks.Interop.sldworks.View currentView = sheetView?.GetNextView() as SolidWorks.Interop.sldworks.View;

                        while (currentView != null)
                        {
                            bool viewModelResolved = true;
                            string viewRefModelName = "";
                            try { viewRefModelName = currentView.GetReferencedModelName() ?? ""; } catch {}
                            if (!string.IsNullOrEmpty(viewRefModelName) && IsValidSolidWorksFilePath(viewRefModelName))
                            {
                                try { viewModelResolved = File.Exists(viewRefModelName); } catch { viewModelResolved = false; }
                            }

                            ViewGeometryInfo viewGeom = RepairDimCandidateFinder.EnumerateViewGeometry(swApp, currentView);

                            DisplayDimension dispDim = currentView.GetFirstDisplayDimension5() as DisplayDimension;
                            while (dispDim != null)
                            {
                                Annotation annot = dispDim.GetAnnotation() as Annotation;
                                if (annot != null && annot.IsDangling())
                                {
                                    DanglingDimensionInfo info = ExtractDanglingInfo(sheetName, viewGeom.ViewName, dispDim, annot);
                                    if (viewModelResolved)
                                    {
                                        RepairDimCandidateFinder.AnalyzeCandidatesForDimension(swApp, info, viewGeom, currentView, dispDim);
                                    }
                                    ClassifyFailureMode(info, viewGeom, viewModelResolved, viewRefModelName);

                                    bool isLinear = info.DimensionType == swDimensionType_e.swLinearDimension ||
                                                    info.DimensionType == swDimensionType_e.swHorLinearDimension ||
                                                    info.DimensionType == swDimensionType_e.swVertLinearDimension;

                                    if (info.FailureMode == RepairDimFailureMode.FullyLostReference && isLinear && viewModelResolved)
                                    {
                                        fullyLostBatchTargets.Add(new BatchTargetSnapshot
                                        {
                                            TargetIndex = fullyLostBatchTargets.Count + 1,
                                            SheetName = sheetName,
                                            ViewName = viewGeom.ViewName,
                                            DimensionName = annot.GetName() ?? info.DimensionName,
                                            OldDimFullName = info.DimensionName,
                                            DimensionType = info.DimensionType,
                                            SystemValue = info.SystemValue,
                                            Position = info.Position != null ? new double[] { info.Position[0], info.Position[1], info.Position[2] } : null,
                                            AttachedEntityTypes = new List<int>(info.AttachedEntityTypes),
                                            FailureMode = info.FailureMode,
                                            CandidateDecision = info.CandidateDecision
                                        });
                                    }
                                }
                                dispDim = dispDim.GetNext5() as DisplayDimension;
                            }
                            currentView = currentView.GetNextView() as SolidWorks.Interop.sldworks.View;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug("ERROR during Phase 3 Fresh Scan: " + ex.Message);
            }
            finally
            {
                if (!string.IsNullOrEmpty(initialSheet)) { try { swDrawing.ActivateSheet(initialSheet); } catch {} }
            }

            LogDebug($"Generic FullyLost Targets Detected: {fullyLostBatchTargets.Count}");

            int fullyLostSuccessCount = 0;
            int fullyLostManualReviewCount = 0;
            int fullyLostFailedCount = 0;

            if (fullyLostBatchTargets.Count > 0)
            {
                LogDebug("\n=== GENERIC FULLY LOST BATCH EXECUTION START ===");
                LogDebug($"Total FullyLost Batch Targets: {fullyLostBatchTargets.Count}");

                for (int flIdx = 0; flIdx < fullyLostBatchTargets.Count; flIdx++)
                {
                    var target = fullyLostBatchTargets[flIdx];
                    int targetNum = flIdx + 1;

                    SingleTargetRepairResult res = ExecuteSingleFullyLostTargetRepair(
                        swApp,
                        swDrawing,
                        swModel,
                        target,
                        targetNum,
                        fullyLostBatchTargets.Count,
                        runningDisplayCount,
                        runningDanglingCount);

                    if (res.Status == SingleTargetStatus.Success)
                    {
                        fullyLostSuccessCount++;
                        runningDisplayCount = res.PostDisplayCount;
                        runningDanglingCount = res.PostDanglingCount;
                    }
                    else if (res.Status == SingleTargetStatus.ManualReview || res.Status == SingleTargetStatus.Skipped)
                    {
                        fullyLostManualReviewCount++;
                    }
                    else if (res.Status == SingleTargetStatus.Failed)
                    {
                        fullyLostFailedCount++;
                        if (res.IsUnsafeState)
                        {
                            LogDebug("FULLY_LOST_BATCH_ABORT_UNSAFE_STATE: Aborting remaining batch.");
                            break;
                        }
                    }
                }

                LogDebug($"\n=== GENERIC FULLY LOST BATCH FINISHED: Repaired={fullyLostSuccessCount}/{fullyLostBatchTargets.Count}, ManualReview={fullyLostManualReviewCount}, Failed={fullyLostFailedCount} ===");
            }

            // =========================================================================
            // PHASE 4 — FINAL FRESH RESCAN & DYNAMIC SUMMARY REPORT
            // =========================================================================
            int finalTotalDisplay = 0;
            int finalTotalDangling = 0;
            int remainingHighConfidence = 0;
            int remainingFullyLost = 0;
            int remainingUnsupported = 0;
            int remainingModelMissing = 0;
            int remainingGeomChanged = 0;

            try
            {
                if (!string.IsNullOrEmpty(initialSheet)) { try { swDrawing.ActivateSheet(initialSheet); } catch {} }

                string[] sheetNames = swDrawing.GetSheetNames() as string[];
                if (sheetNames != null)
                {
                    foreach (string sName in sheetNames)
                    {
                        swDrawing.ActivateSheet(sName);
                        SolidWorks.Interop.sldworks.View sView = swDrawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                        SolidWorks.Interop.sldworks.View cView = sView?.GetNextView() as SolidWorks.Interop.sldworks.View;

                        while (cView != null)
                        {
                            bool vResolved = true;
                            string vModel = "";
                            try { vModel = cView.GetReferencedModelName() ?? ""; } catch {}
                            if (!string.IsNullOrEmpty(vModel) && IsValidSolidWorksFilePath(vModel))
                            {
                                try { vResolved = File.Exists(vModel); } catch { vResolved = false; }
                            }

                            ViewGeometryInfo vGeom = RepairDimCandidateFinder.EnumerateViewGeometry(swApp, cView);
                            DisplayDimension dd = cView.GetFirstDisplayDimension5() as DisplayDimension;
                            while (dd != null)
                            {
                                finalTotalDisplay++;
                                Annotation a = dd.GetAnnotation() as Annotation;
                                if (a != null && a.IsDangling())
                                {
                                    finalTotalDangling++;
                                    DanglingDimensionInfo dInfo = ExtractDanglingInfo(sName, vGeom.ViewName, dd, a);
                                    if (vResolved)
                                    {
                                        RepairDimCandidateFinder.AnalyzeCandidatesForDimension(swApp, dInfo, vGeom, cView, dd);
                                    }
                                    ClassifyFailureMode(dInfo, vGeom, vResolved, vModel);

                                    switch (dInfo.FailureMode)
                                    {
                                        case RepairDimFailureMode.ComponentReinsertedOrGeometryReplaced:
                                            remainingHighConfidence++;
                                            break;
                                        case RepairDimFailureMode.FullyLostReference:
                                            remainingFullyLost++;
                                            break;
                                        case RepairDimFailureMode.UnsupportedAnchor:
                                            remainingUnsupported++;
                                            break;
                                        case RepairDimFailureMode.ModelFileMissingOrUnresolved:
                                            remainingModelMissing++;
                                            break;
                                        case RepairDimFailureMode.GeometryChangedNoCandidate:
                                            remainingGeomChanged++;
                                            break;
                                    }
                                }
                                dd = dd.GetNext5() as DisplayDimension;
                            }
                            cView = cView.GetNextView() as SolidWorks.Interop.sldworks.View;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug("ERROR during final rescan: " + ex.Message);
            }
            finally
            {
                if (!string.IsNullOrEmpty(initialSheet)) { try { swDrawing.ActivateSheet(initialSheet); } catch {} }
            }

            StringBuilder sbSummary = new StringBuilder();
            sbSummary.AppendLine("\n=== FINAL DRAWING SUMMARY ===");
            sbSummary.AppendLine($"Build: {REPAIR_DIM_BUILD}");
            sbSummary.AppendLine();
            sbSummary.AppendLine($"Initial Display Dimensions : {initialDrawingDisplayDimCount}");
            sbSummary.AppendLine($"Final Display Dimensions   : {finalTotalDisplay}");
            sbSummary.AppendLine();
            sbSummary.AppendLine($"Initial Dangling Dimensions: {initialDrawingDanglingCount}");
            sbSummary.AppendLine($"Final Dangling Dimensions  : {finalTotalDangling}");
            sbSummary.AppendLine();
            sbSummary.AppendLine($"STEP 10 (1-Live-Anchor) Repaired: {step10SuccessCount}");
            sbSummary.AppendLine($"FullyLost Detected         : {fullyLostBatchTargets.Count}");
            sbSummary.AppendLine($"FullyLost Repaired         : {fullyLostSuccessCount}");
            sbSummary.AppendLine($"Remaining FullyLost        : {remainingFullyLost}");
            sbSummary.AppendLine($"Remaining Unsupported      : {remainingUnsupported}");
            sbSummary.AppendLine($"Remaining HighConfidence   : {remainingHighConfidence}");
            sbSummary.AppendLine($"Remaining ModelMissing     : {remainingModelMissing}");
            sbSummary.AppendLine();
            sbSummary.AppendLine("OTHER NON-TARGET DIMENSIONS MODIFIED: NO");
            sbSummary.AppendLine("DRAWING SAVED: NO");
            sbSummary.AppendLine();
            sbSummary.AppendLine("STOP.");

            LogDebug(sbSummary.ToString().TrimEnd());

            MessageBox.Show(
                sbSummary.ToString(),
                "REPAIR DIM - STEP 11D BATCH RUN COMPLETE",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static SingleTargetRepairResult ExecuteSingleFullyLostTargetRepair(
            ISldWorks swApp,
            DrawingDoc swDrawing,
            ModelDoc2 swModel,
            BatchTargetSnapshot target,
            int targetNum,
            int totalTargets,
            int currentDisplayBefore,
            int currentDanglingBefore)
        {
            StringBuilder sbLog = new StringBuilder();
            sbLog.AppendLine();
            sbLog.AppendLine("-----------------------------------");
            sbLog.AppendLine($"FULLY LOST TARGET {targetNum}/{totalTargets}");
            sbLog.AppendLine($"Sheet: {target.SheetName}");
            sbLog.AppendLine($"View: {target.ViewName}");
            sbLog.AppendLine($"Old Full Name: {target.OldDimFullName}");
            sbLog.AppendLine($"Old Value: {(target.SystemValue.HasValue ? $"{target.SystemValue.Value * 1000.0:F6} mm" : "<null>")}");
            sbLog.AppendLine($"Dimension Type: {target.DimensionType}");

            try { swDrawing.ActivateSheet(target.SheetName); } catch {}

            SolidWorks.Interop.sldworks.View targetView = null;
            DisplayDimension targetDispDim = null;
            Annotation targetAnnot = null;

            SolidWorks.Interop.sldworks.View sView = swDrawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
            SolidWorks.Interop.sldworks.View cView = sView?.GetNextView() as SolidWorks.Interop.sldworks.View;

            while (cView != null)
            {
                string vName = cView.GetName2() ?? "";
                if (vName.Equals(target.ViewName, StringComparison.OrdinalIgnoreCase) ||
                    vName.IndexOf(target.ViewName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    target.ViewName.IndexOf(vName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    targetView = cView;
                    DisplayDimension dd = cView.GetFirstDisplayDimension5() as DisplayDimension;
                    while (dd != null)
                    {
                        Annotation a = dd.GetAnnotation() as Annotation;
                        if (a != null && a.IsDangling())
                        {
                            string aName = a.GetName() ?? "";
                            Dimension dObj = dd.GetDimension2(0) as Dimension ?? dd.GetDimension() as Dimension;
                            string dFull = dObj?.FullName ?? "";

                            bool nameMatch = (!string.IsNullOrEmpty(target.OldDimFullName) && !string.IsNullOrEmpty(dFull) && dFull.Equals(target.OldDimFullName, StringComparison.OrdinalIgnoreCase)) ||
                                             (!string.IsNullOrEmpty(target.DimensionName) && !string.IsNullOrEmpty(aName) && aName.Equals(target.DimensionName, StringComparison.OrdinalIgnoreCase)) ||
                                             (!string.IsNullOrEmpty(target.DimensionName) && !string.IsNullOrEmpty(dFull) && dFull.IndexOf(target.DimensionName, StringComparison.OrdinalIgnoreCase) >= 0);

                            if (nameMatch)
                            {
                                targetDispDim = dd;
                                targetAnnot = a;
                                break;
                            }
                        }
                        dd = dd.GetNext5() as DisplayDimension;
                    }
                    if (targetDispDim != null) break;
                }
                cView = cView.GetNextView() as SolidWorks.Interop.sldworks.View;
            }

            if (targetView == null || targetDispDim == null || targetAnnot == null)
            {
                sbLog.AppendLine("\nRESULT: SKIPPED (TARGET_REACQUIRE_FAILED)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Skipped, Reason = "TARGET_REACQUIRE_FAILED" };
            }

            DanglingDimensionInfo info = ExtractDanglingInfo(target.SheetName, target.ViewName, targetDispDim, targetAnnot);
            ViewGeometryInfo viewGeom = RepairDimCandidateFinder.EnumerateViewGeometry(swApp, targetView);
            ClassifyFailureMode(info, viewGeom, true, targetView.GetReferencedModelName() ?? "");

            if (info.FailureMode != RepairDimFailureMode.FullyLostReference)
            {
                sbLog.AppendLine($"\nRESULT: SKIPPED (NOT_FULLY_LOST: {info.FailureMode})");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Skipped, Reason = $"NOT_FULLY_LOST ({info.FailureMode})" };
            }

            // Snapshot old DisplayData
            List<DisplayDimLine> oldDisplayLines = new List<DisplayDimLine>();
            try
            {
                DisplayData dd = targetDispDim.GetDisplayData() as DisplayData;
                if (dd != null)
                {
                    int lCount = dd.GetLineCount();
                    for (int li = 0; li < lCount; li++)
                    {
                        object lObj = dd.GetLineAtIndex3(li);
                        if (lObj is double[] lArr && lArr.Length >= 10)
                        {
                            oldDisplayLines.Add(new DisplayDimLine
                            {
                                LineIndex = li,
                                LineType = Convert.ToInt32(lArr[1]),
                                StartX = lArr[4], StartY = lArr[5], StartZ = lArr[6],
                                EndX = lArr[7], EndY = lArr[8], EndZ = lArr[9]
                            });
                        }
                    }
                }
            }
            catch {}

            info.DisplayLineSegments = oldDisplayLines;

            // Build Witness Profile
            DisplayWitnessProfile oldWitnessProfile = RepairDimGeometry.BuildDisplayWitnessProfile(oldDisplayLines, info.Position);

            sbLog.AppendLine();
            sbLog.AppendLine("DISPLAY PROFILE:");
            sbLog.AppendLine($"  Line Count: {oldDisplayLines.Count}");
            sbLog.AppendLine($"  Profile Hypotheses: {oldWitnessProfile.HypothesisCount}");
            sbLog.AppendLine($"  Best Profile Score: {oldWitnessProfile.BestScore:F1}");
            sbLog.AppendLine($"  Second Profile Score: {(oldWitnessProfile.HypothesisCount > 1 ? $"{oldWitnessProfile.SecondScore:F1}" : "N/A")}");
            sbLog.AppendLine($"  Profile Decision: {oldWitnessProfile.Status} ({oldWitnessProfile.Confidence})");

            if (oldWitnessProfile.IsValid)
            {
                sbLog.AppendLine($"  Dimension Axis: {oldWitnessProfile.DimensionLineOrientation} (Vector: [{oldWitnessProfile.DimensionAxisUnitVector[0]:F4}, {oldWitnessProfile.DimensionAxisUnitVector[1]:F4}])");
                sbLog.AppendLine($"  Witness Direction: {oldWitnessProfile.WitnessOrientation} (Vector: [{oldWitnessProfile.WitnessDirectionUnitVector[0]:F4}, {oldWitnessProfile.WitnessDirectionUnitVector[1]:F4}])");
                sbLog.AppendLine($"  Witness Origin 1: ({oldWitnessProfile.Witness1GeometryPoint[0]:F4}, {oldWitnessProfile.Witness1GeometryPoint[1]:F4})");
                sbLog.AppendLine($"  Witness Origin 2: ({oldWitnessProfile.Witness2GeometryPoint[0]:F4}, {oldWitnessProfile.Witness2GeometryPoint[1]:F4})");
            }

            if (!oldWitnessProfile.IsValid)
            {
                sbLog.AppendLine($"\nRESULT: MANUAL_REVIEW (DISPLAY_PROFILE_INVALID: {oldWitnessProfile.ErrorReason})");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "DISPLAY_PROFILE_INVALID" };
            }

            FullyLostPairDecision decision = RepairDimCandidateFinder.FindFullyLostPairCandidate(
                swApp,
                info,
                viewGeom,
                targetView,
                targetDispDim);

            sbLog.AppendLine();
            sbLog.AppendLine("VIEW BREAK INFO:");
            sbLog.AppendLine($"  IsBroken: {(decision.BrokenViewInfo != null && decision.BrokenViewInfo.IsBroken)}");
            sbLog.AppendLine($"  BreakLineCount: {(decision.BrokenViewInfo != null ? decision.BrokenViewInfo.BreakCount : 0)}");
            if (decision.BrokenViewInfo != null && decision.BrokenViewInfo.BreakLines != null && decision.BrokenViewInfo.BreakLines.Count > 0)
            {
                foreach (var bl in decision.BrokenViewInfo.BreakLines)
                {
                    sbLog.AppendLine($"  Break #{bl.Index}: Orientation={bl.OrientationString}, Style={bl.Style}, Pos1={bl.Position1:F4}, Pos2={bl.Position2:F4}, SheetSpan=[{bl.SheetMinCoord:F4}, {bl.SheetMaxCoord:F4}]");
                }
            }

            sbLog.AppendLine();
            sbLog.AppendLine("DIM BREAK RELATION:");
            sbLog.AppendLine($"  DistanceMode: {decision.DistanceMode}");
            sbLog.AppendLine($"  CrossesActiveBreak: {decision.CrossesActiveBreak}");
            sbLog.AppendLine($"  CrossingCount: {decision.BreakCrossingCount}");

            sbLog.AppendLine();
            sbLog.AppendLine("SIDE 1:");
            sbLog.AppendLine($"  Candidate Count: {decision.Side1Candidates.Count}");
            if (decision.Side1Candidates.Count > 0)
            {
                var top1 = decision.Side1Candidates[0];
                sbLog.AppendLine($"  Top Candidate: RawRec #{top1.RawRecordIndex}, Comp={top1.ComponentName}");
                sbLog.AppendLine($"  Attach Point: ({top1.AttachPoint[0]:F4}, {top1.AttachPoint[1]:F4}) [t={top1.AttachParamT:F2}]");
                sbLog.AppendLine($"  Proximity: {top1.WitnessProximityMm:F2} mm");
                sbLog.AppendLine($"  Ray Angular Error: {top1.WitnessRayAngularErrorDeg:F1}° (Consistent: {top1.WitnessRayConsistency})");
            }
            else
            {
                sbLog.AppendLine("  Top Candidate: NONE");
                sbLog.AppendLine("  Proximity: N/A");
            }

            sbLog.AppendLine();
            sbLog.AppendLine("SIDE 2:");
            sbLog.AppendLine($"  Candidate Count: {decision.Side2Candidates.Count}");
            if (decision.Side2Candidates.Count > 0)
            {
                var top2 = decision.Side2Candidates[0];
                sbLog.AppendLine($"  Top Candidate: RawRec #{top2.RawRecordIndex}, Comp={top2.ComponentName}");
                sbLog.AppendLine($"  Attach Point: ({top2.AttachPoint[0]:F4}, {top2.AttachPoint[1]:F4}) [t={top2.AttachParamT:F2}]");
                sbLog.AppendLine($"  Proximity: {top2.WitnessProximityMm:F2} mm");
                sbLog.AppendLine($"  Ray Angular Error: {top2.WitnessRayAngularErrorDeg:F1}° (Consistent: {top2.WitnessRayConsistency})");
            }
            else
            {
                sbLog.AppendLine("  Top Candidate: NONE");
                sbLog.AppendLine("  Proximity: N/A");
            }

            sbLog.AppendLine();
            sbLog.AppendLine($"PAIR EVALUATION ({decision.EvaluatedCombinations.Count} Combinations Tested):");
            foreach (var eval in decision.EvaluatedCombinations)
            {
                string resStr = eval.IsAccepted ? "ACCEPT" : $"REJECT ({string.Join(", ", eval.RejectionReasons)})";
                sbLog.AppendLine($"  Comb S1(Rec #{eval.Side1RawIndex}) x S2(Rec #{eval.Side2RawIndex}):");
                sbLog.AppendLine($"    Attach1: ({eval.AttachPoint1[0]:F4}, {eval.AttachPoint1[1]:F4}), Attach2: ({eval.AttachPoint2[0]:F4}, {eval.AttachPoint2[1]:F4})");
                sbLog.AppendLine($"    DistanceMode: {eval.DistanceMode}, CrossesBreak: {eval.CrossesActiveBreak} (Count: {eval.BreakCrossingCount})");
                sbLog.AppendLine($"    SheetDist: {eval.SheetSeparationMm:F4} mm, ModelDist: {eval.ModelDistanceMm:F4} mm, Target: {eval.TargetDistanceMm:F4} mm, Err: {eval.DistanceErrorMm:F4} mm (Tol: {eval.DistanceToleranceMm:F4} mm)");
                if (!eval.PreCreateDistanceComparable)
                {
                    sbLog.AppendLine($"    NOTE: {eval.PreCreateDistanceReason}");
                }
                sbLog.AppendLine($"    PerpResidual: {eval.PerpendicularResidualMm:F4} mm, TotalWitnessErr: {eval.TotalWitnessErrorMm:F4} mm (Max: {eval.MaxWitnessErrorMm:F4} mm)");
                sbLog.AppendLine($"    RayAngErr1: {eval.RayAngularError1Deg:F1}°, RayAngErr2: {eval.RayAngularError2Deg:F1}°");
                sbLog.AppendLine($"    RESULT: {resStr}");
            }

            sbLog.AppendLine();
            sbLog.AppendLine($"PAIR RAW COUNT: {decision.RawPairCount}");
            sbLog.AppendLine($"PAIR PHYSICAL UNIQUE COUNT: {decision.PhysicalUniquePairCount}");
            if (decision.DuplicatePairLogs.Count > 0)
            {
                sbLog.AppendLine("  DUPLICATE PAIR LOGS:");
                foreach (var dupLog in decision.DuplicatePairLogs)
                {
                    sbLog.AppendLine($"    * {dupLog}");
                }
            }

            sbLog.AppendLine();
            sbLog.AppendLine("PAIR DECISION SUMMARY:");
            sbLog.AppendLine($"  Unique Pair Candidates: {decision.PairCandidates.Count}");
            if (decision.PairCandidates.Count > 0)
            {
                var bestP = decision.PairCandidates[0];
                sbLog.AppendLine($"  Best (Rank 1): S1(Rec #{bestP.Side1.RawRecordIndex}) + S2(Rec #{bestP.Side2.RawRecordIndex}), Score={bestP.PairScore:F1}, TotalWitnessErr={bestP.TotalWitnessErrorMm:F4} mm, MaxWitnessErr={bestP.MaxWitnessErrorMm:F4} mm, DistErr={bestP.DistanceErrorMm:F4} mm, PerpRes={bestP.PerpendicularResidualMm:F4} mm");
                if (bestP.DistanceMode == DistanceVerificationMode.BROKEN_VIEW_CROSS_BREAK)
                {
                    sbLog.AppendLine("    NOTE: NAIVE DISTANCE INVALID DUE TO ACTIVE BREAK (Will verify true model distance after provisional create)");
                }
                if (decision.PairCandidates.Count > 1)
                {
                    var secP = decision.PairCandidates[1];
                    sbLog.AppendLine($"  Second (Rank 2): S1(Rec #{secP.Side1.RawRecordIndex}) + S2(Rec #{secP.Side2.RawRecordIndex}), Score={secP.PairScore:F1}, TotalWitnessErr={secP.TotalWitnessErrorMm:F4} mm, MaxWitnessErr={secP.MaxWitnessErrorMm:F4} mm, DistErr={secP.DistanceErrorMm:F4} mm, PerpRes={secP.PerpendicularResidualMm:F4} mm");
                }
                else
                {
                    sbLog.AppendLine("  Second: N/A");
                }
                sbLog.AppendLine($"  Uniqueness: {decision.PairUniqueness}");
                sbLog.AppendLine($"  Score Gap: {(decision.PairCandidates.Count > 1 ? $"{decision.ScoreGap:F1}" : "N/A")}");
                sbLog.AppendLine($"  Witness Error Gap: {(decision.PairCandidates.Count > 1 ? $"{decision.WitnessErrorGap:F4} mm" : "N/A")}");
                sbLog.AppendLine($"  Measured Value: {bestP.MeasuredModelDistanceMm:F4} mm");
                sbLog.AppendLine($"  Distance Error: {bestP.DistanceErrorMm:F4} mm");
                sbLog.AppendLine($"  Decision: {decision.Decision}");
                if (!string.IsNullOrEmpty(decision.AmbiguityReason))
                {
                    sbLog.AppendLine($"  Ambiguity Reason: {decision.AmbiguityReason}");
                }
            }
            else
            {
                sbLog.AppendLine("  Best: NONE");
                sbLog.AppendLine("  Second: N/A");
                sbLog.AppendLine("  Uniqueness: NO_PAIR");
                sbLog.AppendLine("  Score Gap: N/A");
                sbLog.AppendLine("  Witness Error Gap: N/A");
                sbLog.AppendLine("  Measured Value: N/A");
                sbLog.AppendLine("  Distance Error: N/A");
                sbLog.AppendLine($"  Decision: {decision.Decision}");
            }

            bool isEligibleForCreate = (decision.Decision == "FULLY_LOST_HIGH_CONFIDENCE" || decision.Decision == "BROKEN_VIEW_PROVISIONAL_HIGH_CONFIDENCE") && decision.BestPair != null;

            if (!isEligibleForCreate)
            {
                string finalReason = !string.IsNullOrEmpty(decision.AmbiguityReason) ? $"{decision.Decision} ({decision.AmbiguityReason})" : decision.Decision;
                sbLog.AppendLine($"\nRESULT: MANUAL_REVIEW ({finalReason})");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = finalReason };
            }

            // Snapshot old state & presentation
            bool oldIsDangling = false;
            try { oldIsDangling = targetAnnot.IsDangling(); } catch {}
            double? oldSysVal = info.SystemValue;
            double[] oldPos = null;
            try { oldPos = targetAnnot.GetPosition() as double[]; } catch {}

            Dimension oldDimObj = targetDispDim.GetDimension2(0) as Dimension ?? targetDispDim.GetDimension() as Dimension;
            string oldDimFullName = "";
            if (oldDimObj != null) { try { oldDimFullName = oldDimObj.FullName ?? ""; } catch {} }
            if (string.IsNullOrEmpty(oldDimFullName) && targetAnnot != null) { try { oldDimFullName = targetAnnot.GetName() ?? ""; } catch {} }

            string oldPrefix = "", oldSuffix = "", oldCalloutAbove = "", oldCalloutBelow = "";
            try
            {
                oldPrefix = targetDispDim.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? "";
                oldSuffix = targetDispDim.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix) ?? "";
                oldCalloutAbove = targetDispDim.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutAbove) ?? "";
                oldCalloutBelow = targetDispDim.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow) ?? "";
            }
            catch {}

            int oldPrimaryPrecision = -1, oldDualPrecision = -1, oldPrimaryTolPrecision = -1, oldDualTolPrecision = -1;
            try
            {
                oldPrimaryPrecision = targetDispDim.GetPrimaryPrecision2();
                oldDualPrecision = targetDispDim.GetAlternatePrecision2();
                oldPrimaryTolPrecision = targetDispDim.GetPrimaryTolPrecision2();
                oldDualTolPrecision = targetDispDim.GetAlternateTolPrecision2();
            }
            catch {}

            DimensionTolerance oldTol = oldDimObj?.Tolerance;
            int oldTolType = (int)swTolType_e.swTolNONE;
            double oldTolMax = 0.0, oldTolMin = 0.0;
            if (oldTol != null)
            {
                try { oldTolType = oldTol.Type; } catch {}
                try { oldTolMax = oldTol.GetMaxValue(); } catch {}
                try { oldTolMin = oldTol.GetMinValue(); } catch {}
            }

            bool oldUseDocFormat = true;
            TextFormat oldTf = null;
            try { oldUseDocFormat = targetAnnot.GetUseDocTextFormat(0); } catch {}
            try { oldTf = targetAnnot.GetTextFormat(0) as TextFormat; } catch {}

            bool oldUseDocUnits = true;
            int oldLengthUnit = -1, oldFractionBase = -1, oldFractionValue = -1;
            bool oldRoundToFraction = false;
            try
            {
                oldUseDocUnits = targetDispDim.GetUseDocUnits();
                oldLengthUnit = targetDispDim.GetUnits();
                oldFractionBase = targetDispDim.GetFractionBase();
                oldFractionValue = targetDispDim.GetFractionValue();
                oldRoundToFraction = targetDispDim.GetRoundToFraction();
            }
            catch {}

            int oldArrowSide = -1;
            try { oldArrowSide = targetDispDim.ArrowSide; } catch {}

            string oldLayer = "";
            try { oldLayer = targetAnnot.Layer ?? ""; } catch {}
            int oldColor = -1;
            try { oldColor = targetAnnot.Color; } catch {}

            // Resolve Drawing Edges
            var bestPair = decision.BestPair;
            object cand1ModelEntity = bestPair.Side1.EdgeInfo.ModelEntity;
            object cand2ModelEntity = bestPair.Side2.EdgeInfo.ModelEntity;

            object cand1DrawingEntity = null;
            object cand2DrawingEntity = null;
            try { if (cand1ModelEntity != null) cand1DrawingEntity = targetView.GetCorrespondingEntity(cand1ModelEntity); } catch {}
            try { if (cand2ModelEntity != null) cand2DrawingEntity = targetView.GetCorrespondingEntity(cand2ModelEntity); } catch {}

            IEntity cand1IEnt = cand1DrawingEntity as IEntity;
            IEntity cand2IEnt = cand2DrawingEntity as IEntity;

            if (cand1IEnt == null || cand2IEnt == null)
            {
                sbLog.AppendLine("\nRESULT: FAILED (DRAWING_EDGES_NULL)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "DRAWING_EDGES_NULL" };
            }

            swModel.ClearSelection2(true);
            ISelectionMgr selMgr = swModel.SelectionManager as ISelectionMgr;

            SelectData selData1 = selMgr?.CreateSelectData();
            if (selData1 != null) selData1.View = targetView;

            SelectData selData2 = selMgr?.CreateSelectData();
            if (selData2 != null) selData2.View = targetView;

            bool sel1 = false;
            try { sel1 = cand1IEnt.Select4(false, selData1); } catch {}

            bool sel2 = false;
            try { sel2 = cand2IEnt.Select4(true, selData2); } catch {}

            int selCount = 0;
            if (selMgr != null) { try { selCount = selMgr.GetSelectedObjectCount2(-1); } catch {} }

            if (selCount != 2)
            {
                sbLog.AppendLine($"\nRESULT: FAILED (SELECTION_COUNT_INVALID: {selCount})");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = $"SELECTION_COUNT_INVALID ({selCount})" };
            }

            DisplayDimension newDisp = null;
            double initialTestX = (oldPos != null && oldPos.Length >= 2) ? oldPos[0] + 0.010 : (viewGeom.ViewX + 0.010);
            double initialTestY = (oldPos != null && oldPos.Length >= 2) ? oldPos[1] + 0.010 : (viewGeom.ViewY + 0.010);
            double initialTestZ = (oldPos != null && oldPos.Length >= 3) ? oldPos[2] : 0.0;

            try
            {
                newDisp = swModel.AddDimension2(initialTestX, initialTestY, initialTestZ) as DisplayDimension;
            }
            catch {}

            Annotation newAnnot = null;
            if (newDisp != null) { try { newAnnot = newDisp.GetAnnotation() as Annotation; } catch {} }

            if (newDisp == null || newAnnot == null)
            {
                sbLog.AppendLine("\nRESULT: FAILED (ADD_DIMENSION_NULL)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "ADD_DIMENSION_NULL" };
            }

            bool newIsDangling = true;
            try { newIsDangling = newAnnot.IsDangling(); } catch {}

            int newAttachedCount = 0;
            try { newAttachedCount = newAnnot.GetAttachedEntityCount3(); } catch {}

            List<int> newAttachedTypes = new List<int>();
            try
            {
                object natObj = newAnnot.GetAttachedEntityTypes();
                if (natObj is int[] nIntArr) newAttachedTypes.AddRange(nIntArr);
                else if (natObj is object[] nObjArr) foreach (var o in nObjArr) newAttachedTypes.Add(Convert.ToInt32(o));
            }
            catch {}

            double? newSysVal = null;
            try
            {
                Dimension nd = newDisp.GetDimension2(0) as Dimension ?? newDisp.GetDimension() as Dimension;
                if (nd != null)
                {
                    object v = nd.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                    if (v is double[] arr && arr.Length > 0) newSysVal = arr[0];
                    else if (v is double d) newSysVal = d;
                    else newSysVal = nd.GetSystemValue2("");
                }
            }
            catch {}

            double deltaValMm = -1.0;
            if (oldSysVal.HasValue && newSysVal.HasValue)
            {
                deltaValMm = Math.Abs(newSysVal.Value - oldSysVal.Value) * 1000.0;
            }

            double effTolMm = oldSysVal.HasValue ? Math.Max(0.15, Math.Abs(oldSysVal.Value * 1000.0) * 0.001) : 0.15;
            bool newGeomPass = (!newIsDangling && newAttachedCount == 2 && newSysVal.HasValue && deltaValMm <= effTolMm);

            string newDimFullName = "";
            try
            {
                Dimension nd = newDisp.GetDimension2(0) as Dimension ?? newDisp.GetDimension() as Dimension;
                if (nd != null) newDimFullName = nd.FullName ?? "";
            }
            catch {}
            if (string.IsNullOrEmpty(newDimFullName) && newAnnot != null) { try { newDimFullName = newAnnot.GetName() ?? ""; } catch {} }

            sbLog.AppendLine();
            sbLog.AppendLine("CREATE:");
            sbLog.AppendLine($"  New DIM: {newDimFullName}");
            sbLog.AppendLine($"  Value: {(newSysVal.HasValue ? $"{newSysVal.Value * 1000.0:F6} mm" : "<null>")}");
            sbLog.AppendLine($"  Dangling: {newIsDangling}");
            sbLog.AppendLine($"  Attached: {newAttachedCount} [{string.Join(", ", newAttachedTypes)}]");

            if (!newGeomPass)
            {
                swModel.ClearSelection2(true);
                IModelDocExtension extCleanup = swModel.Extension;
                bool cleanSelected = false;
                if (!string.IsNullOrEmpty(newDimFullName))
                {
                    try { cleanSelected = extCleanup.SelectByID2(newDimFullName, "DIMENSION", 0.0, 0.0, 0.0, false, 0, null, 0); } catch {}
                }
                bool cleanDeleted = false;
                if (cleanSelected)
                {
                    try { cleanDeleted = extCleanup.DeleteSelection2(0); } catch {}
                }
                swModel.ClearSelection2(true);

                sbLog.AppendLine($"  PROVISIONAL CLEANUP: Selected={cleanSelected}, Deleted={cleanDeleted}");
                sbLog.AppendLine($"\nRESULT: MANUAL_REVIEW (BROKEN_VIEW_POSTCREATE_VALUE_MISMATCH: NewVal={(newSysVal.HasValue ? $"{newSysVal.Value * 1000.0:F4} mm" : "<null>")} vs OldVal={(oldSysVal.HasValue ? $"{oldSysVal.Value * 1000.0:F4} mm" : "<null>")}, Delta={deltaValMm:F4} mm)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "BROKEN_VIEW_POSTCREATE_VALUE_MISMATCH" };
            }

            // Clone presentation
            Dimension newDimObj = newDisp.GetDimension2(0) as Dimension ?? newDisp.GetDimension() as Dimension;
            string propCopyText = "MATCH";
            try
            {
                if (!string.IsNullOrEmpty(oldPrefix)) newDisp.SetText((int)swDimensionTextParts_e.swDimensionTextPrefix, oldPrefix);
                if (!string.IsNullOrEmpty(oldSuffix)) newDisp.SetText((int)swDimensionTextParts_e.swDimensionTextSuffix, oldSuffix);
                if (!string.IsNullOrEmpty(oldCalloutAbove)) newDisp.SetText((int)swDimensionTextParts_e.swDimensionTextCalloutAbove, oldCalloutAbove);
                if (!string.IsNullOrEmpty(oldCalloutBelow)) newDisp.SetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow, oldCalloutBelow);
            }
            catch (Exception ex) { propCopyText = "ERROR: " + ex.Message; }

            string propCopyPrecision = "MATCH";
            try
            {
                if (oldPrimaryPrecision >= 0)
                {
                    newDisp.SetPrecision2(
                        oldPrimaryPrecision,
                        oldDualPrecision >= 0 ? oldDualPrecision : 0,
                        oldPrimaryTolPrecision >= 0 ? oldPrimaryTolPrecision : 0,
                        oldDualTolPrecision >= 0 ? oldDualTolPrecision : 0);
                }
            }
            catch (Exception ex) { propCopyPrecision = "ERROR: " + ex.Message; }

            string propCopyTolerance = "MATCH";
            try
            {
                DimensionTolerance newTol = newDimObj?.Tolerance;
                if (newTol != null && oldTolType >= 0)
                {
                    newTol.Type = oldTolType;
                    if (oldTolType != (int)swTolType_e.swTolNONE)
                    {
                        newTol.SetValues(oldTolMin, oldTolMax);
                    }
                }
            }
            catch (Exception ex) { propCopyTolerance = "ERROR: " + ex.Message; }

            string propCopyFormat = "MATCH";
            try { newAnnot.SetTextFormat(0, oldUseDocFormat, oldTf); } catch (Exception ex) { propCopyFormat = "ERROR: " + ex.Message; }

            string propCopyUnits = "MATCH";
            try
            {
                if (oldLengthUnit >= 0)
                {
                    newDisp.SetUnits(oldUseDocUnits, oldLengthUnit, oldFractionBase, oldFractionValue, oldRoundToFraction);
                }
            }
            catch (Exception ex) { propCopyUnits = "ERROR: " + ex.Message; }

            string propCopyArrow = "MATCH";
            try { if (oldArrowSide >= 0) newDisp.ArrowSide = oldArrowSide; } catch (Exception ex) { propCopyArrow = "ERROR: " + ex.Message; }

            string propCopyLayer = "MATCH";
            try
            {
                if (!string.IsNullOrEmpty(oldLayer)) newAnnot.Layer = oldLayer;
                if (oldColor != -1) newAnnot.Color = oldColor;
            }
            catch (Exception ex) { propCopyLayer = "ERROR: " + ex.Message; }

            string propMatchLevel = (propCopyText == "MATCH" && propCopyPrecision == "MATCH" && propCopyTolerance == "MATCH" && propCopyFormat == "MATCH" && propCopyUnits == "MATCH" && propCopyArrow == "MATCH" && propCopyLayer == "MATCH") ? "FULL_MATCH" : "PARTIAL_MATCH";

            // Restore position
            if (oldPos != null && oldPos.Length >= 3)
            {
                try { newAnnot.SetPosition2(oldPos[0], oldPos[1], oldPos[2]); } catch {}
            }

            double[] newPosAfterMove = null;
            try { newPosAfterMove = newAnnot.GetPosition() as double[]; } catch {}

            double deltaPosMm = 0.0;
            if (oldPos != null && newPosAfterMove != null && oldPos.Length >= 2 && newPosAfterMove.Length >= 2)
            {
                double dx = Math.Abs(newPosAfterMove[0] - oldPos[0]) * 1000.0;
                double dy = Math.Abs(newPosAfterMove[1] - oldPos[1]) * 1000.0;
                deltaPosMm = Math.Sqrt(dx * dx + dy * dy);
            }

            // Build NEW Witness Profile & Verify Old vs New Witness Origins
            List<DisplayDimLine> newDisplayLines = new List<DisplayDimLine>();
            try
            {
                DisplayData newDd = newDisp.GetDisplayData() as DisplayData;
                if (newDd != null)
                {
                    int lCount = newDd.GetLineCount();
                    for (int li = 0; li < lCount; li++)
                    {
                        object lObj = newDd.GetLineAtIndex3(li);
                        if (lObj is double[] lArr && lArr.Length >= 10)
                        {
                            newDisplayLines.Add(new DisplayDimLine
                            {
                                LineIndex = li,
                                LineType = Convert.ToInt32(lArr[1]),
                                StartX = lArr[4], StartY = lArr[5], StartZ = lArr[6],
                                EndX = lArr[7], EndY = lArr[8], EndZ = lArr[9]
                            });
                        }
                    }
                }
            }
            catch {}

            DisplayWitnessProfile newWitnessProfile = RepairDimGeometry.BuildDisplayWitnessProfile(newDisplayLines, newPosAfterMove);

            double w1DeltaMm = 999.0;
            double w2DeltaMm = 999.0;
            bool witnessPairMatch = false;

            if (oldWitnessProfile.IsValid && newWitnessProfile.IsValid)
            {
                double d11 = Math.Sqrt(Math.Pow(oldWitnessProfile.Witness1GeometryPoint[0] - newWitnessProfile.Witness1GeometryPoint[0], 2) + Math.Pow(oldWitnessProfile.Witness1GeometryPoint[1] - newWitnessProfile.Witness1GeometryPoint[1], 2)) * 1000.0;
                double d22 = Math.Sqrt(Math.Pow(oldWitnessProfile.Witness2GeometryPoint[0] - newWitnessProfile.Witness2GeometryPoint[0], 2) + Math.Pow(oldWitnessProfile.Witness2GeometryPoint[1] - newWitnessProfile.Witness2GeometryPoint[1], 2)) * 1000.0;

                double d12 = Math.Sqrt(Math.Pow(oldWitnessProfile.Witness1GeometryPoint[0] - newWitnessProfile.Witness2GeometryPoint[0], 2) + Math.Pow(oldWitnessProfile.Witness1GeometryPoint[1] - newWitnessProfile.Witness2GeometryPoint[1], 2)) * 1000.0;
                double d21 = Math.Sqrt(Math.Pow(oldWitnessProfile.Witness2GeometryPoint[0] - newWitnessProfile.Witness1GeometryPoint[0], 2) + Math.Pow(oldWitnessProfile.Witness2GeometryPoint[1] - newWitnessProfile.Witness1GeometryPoint[1], 2)) * 1000.0;

                if (Math.Max(d11, d22) <= Math.Max(d12, d21))
                {
                    w1DeltaMm = d11;
                    w2DeltaMm = d22;
                }
                else
                {
                    w1DeltaMm = d12;
                    w2DeltaMm = d21;
                }

                witnessPairMatch = (w1DeltaMm <= 1.5 && w2DeltaMm <= 1.5);
            }

            sbLog.AppendLine();
            sbLog.AppendLine("OLD vs NEW:");
            sbLog.AppendLine($"  Position Match: {(deltaPosMm <= 0.2)} (Delta: {deltaPosMm:F4} mm)");
            sbLog.AppendLine($"  Presentation Match: {propMatchLevel}");
            sbLog.AppendLine($"  Witness 1 Delta: {w1DeltaMm:F4} mm");
            sbLog.AppendLine($"  Witness 2 Delta: {w2DeltaMm:F4} mm");
            sbLog.AppendLine($"  Witness Pair Match: {witnessPairMatch}");

            bool valueMatchPass = (newSysVal.HasValue && deltaValMm <= effTolMm);
            bool posMatchPass = (deltaPosMm <= 0.2);
            bool refValidPass = (!newIsDangling && newAttachedCount == 2);
            bool presentationPass = (propMatchLevel == "FULL_MATCH" || propMatchLevel == "PARTIAL_MATCH");
            bool decisionPass = (decision.Decision == "FULLY_LOST_HIGH_CONFIDENCE" || decision.Decision == "BROKEN_VIEW_PROVISIONAL_HIGH_CONFIDENCE");

            bool deleteAllowed = valueMatchPass && posMatchPass && refValidPass && presentationPass &&
                                 oldWitnessProfile.IsValid && newWitnessProfile.IsValid && witnessPairMatch &&
                                 decisionPass;

            sbLog.AppendLine();
            sbLog.AppendLine("DELETE:");
            sbLog.AppendLine($"  Allowed: {deleteAllowed}");

            if (!deleteAllowed)
            {
                swModel.ClearSelection2(true);
                IModelDocExtension extCleanup = swModel.Extension;
                bool cleanSelected = false;
                if (!string.IsNullOrEmpty(newDimFullName))
                {
                    try { cleanSelected = extCleanup.SelectByID2(newDimFullName, "DIMENSION", 0.0, 0.0, 0.0, false, 0, null, 0); } catch {}
                }
                bool cleanDeleted = false;
                if (cleanSelected)
                {
                    try { cleanDeleted = extCleanup.DeleteSelection2(0); } catch {}
                }
                swModel.ClearSelection2(true);

                sbLog.AppendLine($"  PROVISIONAL CLEANUP: Selected={cleanSelected}, Deleted={cleanDeleted}");
                sbLog.AppendLine("\nRESULT: FAILED (DELETE_SAFETY_GATE_FAILED)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "DELETE_SAFETY_GATE_FAILED" };
            }

            if (string.IsNullOrEmpty(oldDimFullName) || oldDimFullName.Equals(newDimFullName, StringComparison.OrdinalIgnoreCase))
            {
                sbLog.AppendLine("\nRESULT: FAILED (AMBIGUOUS_DIM_IDENTITY)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "AMBIGUOUS_DIM_IDENTITY" };
            }

            targetAnnot = null;
            targetDispDim = null;
            oldDimObj = null;

            swModel.ClearSelection2(true);

            IModelDocExtension ext = swModel.Extension;
            bool selectByIdResult = false;
            try
            {
                selectByIdResult = ext.SelectByID2(
                    oldDimFullName,
                    "DIMENSION",
                    0.0,
                    0.0,
                    0.0,
                    false,
                    0,
                    null,
                    0);
            }
            catch {}

            int selCountAfterSelect = 0;
            int selTypeRaw = -1;
            string selTypeName = "<none>";

            if (selMgr != null)
            {
                try
                {
                    selCountAfterSelect = selMgr.GetSelectedObjectCount2(-1);
                    if (selCountAfterSelect >= 1)
                    {
                        selTypeRaw = selMgr.GetSelectedObjectType3(1, -1);
                        selTypeName = ((swSelectType_e)selTypeRaw).ToString();
                    }
                }
                catch {}
            }

            bool selectOk = (selectByIdResult && selCountAfterSelect == 1 && (selTypeRaw == (int)swSelectType_e.swSelDIMENSIONS || selTypeName.IndexOf("DIMENSION", StringComparison.OrdinalIgnoreCase) >= 0));

            sbLog.AppendLine($"  Selected: {selectByIdResult}");
            sbLog.AppendLine($"  Selection Count: {selCountAfterSelect}");
            sbLog.AppendLine($"  Selection Type: {selTypeName} ({selTypeRaw})");

            if (!selectOk)
            {
                sbLog.AppendLine("\nRESULT: FAILED (SAFE_DELETE_SELECTION_FAILED)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "SAFE_DELETE_SELECTION_FAILED" };
            }

            bool deleteResult = false;
            try
            {
                deleteResult = ext.DeleteSelection2(0);
            }
            catch {}

            sbLog.AppendLine($"  Deleted: {deleteResult}");
            swModel.ClearSelection2(true);

            if (!deleteResult)
            {
                sbLog.AppendLine("\nRESULT: FAILED (DELETE_RETURNED_FALSE)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "DELETE_RETURNED_FALSE" };
            }

            int postDisplayCount = 0;
            int postDanglingCount = 0;
            CountTotalDrawingDimensions(swDrawing, out postDisplayCount, out postDanglingCount);

            bool newPostDangling = true;
            try { newPostDangling = newAnnot.IsDangling(); } catch {}

            int newPostAttached = 0;
            try { newPostAttached = newAnnot.GetAttachedEntityCount3(); } catch {}

            bool newValidAfterDelete = (!newPostDangling && newPostAttached == 2);

            sbLog.AppendLine();
            sbLog.AppendLine("POST VERIFY:");
            sbLog.AppendLine($"  Display Count: {postDisplayCount} (Before: {currentDisplayBefore})");
            sbLog.AppendLine($"  Dangling Count: {postDanglingCount} (Before: {currentDanglingBefore})");
            sbLog.AppendLine($"  New Valid: {newValidAfterDelete}");

            if (!newValidAfterDelete)
            {
                sbLog.AppendLine("\nRESULT: FAILED (NEW_DIM_INVALID_AFTER_DELETE)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult
                {
                    Status = SingleTargetStatus.Failed,
                    Reason = "NEW_DIM_INVALID_AFTER_DELETE",
                    IsUnsafeState = true,
                    PostDisplayCount = postDisplayCount,
                    PostDanglingCount = postDanglingCount
                };
            }

            sbLog.AppendLine();
            sbLog.AppendLine("RESULT: SUCCESS");
            sbLog.AppendLine("-----------------------------------");

            LogDebug(sbLog.ToString().TrimEnd());

            return new SingleTargetRepairResult
            {
                Status = SingleTargetStatus.Success,
                PostDisplayCount = postDisplayCount,
                PostDanglingCount = postDanglingCount
            };
        }

        private static SingleTargetRepairResult ExecuteSingleStep10TargetRepair(
            ISldWorks swApp,
            DrawingDoc swDrawing,
            ModelDoc2 swModel,
            BatchTargetSnapshot target,
            int targetNum,
            int totalTargets,
            int currentDisplayBefore,
            int currentDanglingBefore)
        {
            string tPrefix = $"BATCH T{targetNum:D2}";

            try { swDrawing.ActivateSheet(target.SheetName); } catch {}

            SolidWorks.Interop.sldworks.View targetView = null;
            DisplayDimension targetDispDim = null;
            Annotation targetAnnot = null;

            SolidWorks.Interop.sldworks.View sView = swDrawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
            SolidWorks.Interop.sldworks.View cView = sView?.GetNextView() as SolidWorks.Interop.sldworks.View;

            while (cView != null)
            {
                string vName = cView.GetName2() ?? "";
                if (vName.Equals(target.ViewName, StringComparison.OrdinalIgnoreCase) ||
                    vName.IndexOf(target.ViewName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    target.ViewName.IndexOf(vName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    targetView = cView;
                    DisplayDimension dd = cView.GetFirstDisplayDimension5() as DisplayDimension;
                    while (dd != null)
                    {
                        Annotation a = dd.GetAnnotation() as Annotation;
                        if (a != null && a.IsDangling())
                        {
                            string aName = a.GetName() ?? "";
                            Dimension dObj = dd.GetDimension2(0) as Dimension ?? dd.GetDimension() as Dimension;
                            string dFull = dObj?.FullName ?? "";

                            bool nameMatch = false;
                            if (!string.IsNullOrEmpty(target.OldDimFullName) && !string.IsNullOrEmpty(dFull) && dFull.Equals(target.OldDimFullName, StringComparison.OrdinalIgnoreCase))
                            {
                                nameMatch = true;
                            }
                            else if (!string.IsNullOrEmpty(target.DimensionName) && !string.IsNullOrEmpty(aName) && aName.Equals(target.DimensionName, StringComparison.OrdinalIgnoreCase))
                            {
                                nameMatch = true;
                            }
                            else if (!string.IsNullOrEmpty(target.DimensionName) && !string.IsNullOrEmpty(dFull) && dFull.IndexOf(target.DimensionName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                nameMatch = true;
                            }

                            if (nameMatch)
                            {
                                targetDispDim = dd;
                                targetAnnot = a;
                                break;
                            }
                        }
                        dd = dd.GetNext5() as DisplayDimension;
                    }
                    if (targetDispDim != null) break;
                }
                cView = cView.GetNextView() as SolidWorks.Interop.sldworks.View;
            }

            if (targetView == null || targetDispDim == null || targetAnnot == null)
            {
                LogDebug($"{tPrefix} SKIPPED: Target no longer found or not dangling.");
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Skipped, Reason = "TARGET_NOT_FOUND_OR_NOT_DANGLING" };
            }

            LogDebug($"{tPrefix} A TARGET_REACQUIRED");

            bool viewModelResolved = true;
            string viewRefModelName = "";
            try { viewRefModelName = targetView.GetReferencedModelName() ?? ""; } catch {}
            if (!string.IsNullOrEmpty(viewRefModelName) && IsValidSolidWorksFilePath(viewRefModelName))
            {
                try { viewModelResolved = File.Exists(viewRefModelName); } catch { viewModelResolved = false; }
            }

            if (!viewModelResolved)
            {
                LogDebug($"{tPrefix} SKIPPED: View model unresolved.");
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Skipped, Reason = "VIEW_MODEL_UNRESOLVED" };
            }

            LogDebug($"{tPrefix} B CLASSIFIER_VERIFIED");

            ViewGeometryInfo freshViewGeom = RepairDimCandidateFinder.EnumerateViewGeometry(swApp, targetView);
            DanglingDimensionInfo freshInfo = ExtractDanglingInfo(target.SheetName, target.ViewName, targetDispDim, targetAnnot);
            RepairDimCandidateFinder.AnalyzeCandidatesForDimension(swApp, freshInfo, freshViewGeom, targetView, targetDispDim);
            ClassifyFailureMode(freshInfo, freshViewGeom, viewModelResolved, viewRefModelName);

            LogDebug($"{tPrefix} C ROUTE_C_RESOLVED");

            bool isHighConfidence = (freshInfo.CandidateDecision == "HIGH_CONFIDENCE") &&
                                    (freshInfo.FailureMode == RepairDimFailureMode.ComponentReinsertedOrGeometryReplaced) &&
                                    (freshInfo.AnchorPolylineMatches.Count > 0) &&
                                    (freshInfo.Candidates.Count > 0);

            if (!isHighConfidence)
            {
                LogDebug($"{tPrefix} SKIPPED: Decision is no longer HIGH_CONFIDENCE ({freshInfo.CandidateDecision}).");
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Skipped, Reason = $"NOT_HIGH_CONFIDENCE ({freshInfo.CandidateDecision})" };
            }

            LogDebug($"{tPrefix} D HIGH_CONFIDENCE_CONFIRMED");

            bool oldIsDangling = false;
            try { oldIsDangling = targetAnnot.IsDangling(); } catch {}
            double? oldSysVal = freshInfo.SystemValue;
            double[] oldPos = null;
            try { oldPos = targetAnnot.GetPosition() as double[]; } catch {}

            Dimension oldDimObj = targetDispDim.GetDimension2(0) as Dimension ?? targetDispDim.GetDimension() as Dimension;
            string oldDimFullName = "";
            if (oldDimObj != null) { try { oldDimFullName = oldDimObj.FullName ?? ""; } catch {} }
            if (string.IsNullOrEmpty(oldDimFullName) && targetAnnot != null) { try { oldDimFullName = targetAnnot.GetName() ?? ""; } catch {} }

            string oldPrefix = "", oldSuffix = "", oldCalloutAbove = "", oldCalloutBelow = "";
            try
            {
                oldPrefix = targetDispDim.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? "";
                oldSuffix = targetDispDim.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix) ?? "";
                oldCalloutAbove = targetDispDim.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutAbove) ?? "";
                oldCalloutBelow = targetDispDim.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow) ?? "";
            }
            catch {}

            int oldPrimaryPrecision = -1, oldDualPrecision = -1, oldPrimaryTolPrecision = -1, oldDualTolPrecision = -1;
            try
            {
                oldPrimaryPrecision = targetDispDim.GetPrimaryPrecision2();
                oldDualPrecision = targetDispDim.GetAlternatePrecision2();
                oldPrimaryTolPrecision = targetDispDim.GetPrimaryTolPrecision2();
                oldDualTolPrecision = targetDispDim.GetAlternateTolPrecision2();
            }
            catch {}

            DimensionTolerance oldTol = oldDimObj?.Tolerance;
            int oldTolType = (int)swTolType_e.swTolNONE;
            double oldTolMax = 0.0, oldTolMin = 0.0;
            if (oldTol != null)
            {
                try { oldTolType = oldTol.Type; } catch {}
                try { oldTolMax = oldTol.GetMaxValue(); } catch {}
                try { oldTolMin = oldTol.GetMinValue(); } catch {}
            }

            bool oldUseDocFormat = true;
            TextFormat oldTf = null;
            try { oldUseDocFormat = targetAnnot.GetUseDocTextFormat(0); } catch {}
            try { oldTf = targetAnnot.GetTextFormat(0) as TextFormat; } catch {}

            bool oldUseDocUnits = true;
            int oldLengthUnit = -1, oldFractionBase = -1, oldFractionValue = -1;
            bool oldRoundToFraction = false;
            try
            {
                oldUseDocUnits = targetDispDim.GetUseDocUnits();
                oldLengthUnit = targetDispDim.GetUnits();
                oldFractionBase = targetDispDim.GetFractionBase();
                oldFractionValue = targetDispDim.GetFractionValue();
                oldRoundToFraction = targetDispDim.GetRoundToFraction();
            }
            catch {}

            int oldArrowSide = -1;
            try { oldArrowSide = targetDispDim.ArrowSide; } catch {}

            string oldLayer = "";
            try { oldLayer = targetAnnot.Layer ?? ""; } catch {}
            int oldColor = -1;
            try { oldColor = targetAnnot.Color; } catch {}

            LogDebug($"{tPrefix} E OLD_STATE_SNAPSHOTTED");

            var anchorMatch = freshInfo.AnchorPolylineMatches[0];
            object anchorModelEntity = anchorMatch.ModelEntity;
            object anchorDrawingEntity = null;
            try { if (anchorModelEntity != null) anchorDrawingEntity = targetView.GetCorrespondingEntity(anchorModelEntity); } catch {}

            RepairCandidate bestCand = freshInfo.Candidates[0];
            object candidateModelEntity = bestCand.Entity;
            object candidateDrawingEntity = null;
            try { if (candidateModelEntity != null) candidateDrawingEntity = targetView.GetCorrespondingEntity(candidateModelEntity); } catch {}

            IEntity anchorIEnt = anchorDrawingEntity as IEntity;
            IEntity candIEnt = candidateDrawingEntity as IEntity;

            if (anchorIEnt == null || candIEnt == null)
            {
                LogDebug($"{tPrefix} FAILED: GetCorrespondingEntity returned null.");
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "DRAWING_ENTITY_MAP_NULL" };
            }

            swModel.ClearSelection2(true);
            ISelectionMgr selMgr = swModel.SelectionManager as ISelectionMgr;

            SelectData selDataAnchor = selMgr?.CreateSelectData();
            if (selDataAnchor != null) selDataAnchor.View = targetView;

            SelectData selDataCand = selMgr?.CreateSelectData();
            if (selDataCand != null) selDataCand.View = targetView;

            bool selAnchor = false;
            try { selAnchor = anchorIEnt.Select4(false, selDataAnchor); } catch {}

            bool selCand = false;
            try { selCand = candIEnt.Select4(true, selDataCand); } catch {}

            int selCount = 0;
            if (selMgr != null) { try { selCount = selMgr.GetSelectedObjectCount2(-1); } catch {} }

            if (selCount != 2)
            {
                LogDebug($"{tPrefix} FAILED: Selection count != 2 (Count={selCount}).");
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = $"SELECTION_COUNT_INVALID ({selCount})" };
            }

            LogDebug($"{tPrefix} F ABOUT_TO_CREATE");

            DisplayDimension newDisp = null;
            double initialTestX = (oldPos != null && oldPos.Length >= 2) ? oldPos[0] + 0.010 : (freshViewGeom.ViewX + 0.010);
            double initialTestY = (oldPos != null && oldPos.Length >= 2) ? oldPos[1] + 0.010 : (freshViewGeom.ViewY + 0.010);
            double initialTestZ = (oldPos != null && oldPos.Length >= 3) ? oldPos[2] : 0.0;

            try
            {
                newDisp = swModel.AddDimension2(initialTestX, initialTestY, initialTestZ) as DisplayDimension;
            }
            catch (Exception ex)
            {
                LogDebug($"{tPrefix} AddDimension2 Exception: " + ex.Message);
            }

            Annotation newAnnot = null;
            if (newDisp != null) { try { newAnnot = newDisp.GetAnnotation() as Annotation; } catch {} }

            if (newDisp == null || newAnnot == null)
            {
                LogDebug($"{tPrefix} FAILED: AddDimension2 returned null.");
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "ADD_DIMENSION_NULL" };
            }

            LogDebug($"{tPrefix} G NEW_CREATED");

            bool newIsDangling = true;
            try { newIsDangling = newAnnot.IsDangling(); } catch {}

            int newAttachedCount = 0;
            try { newAttachedCount = newAnnot.GetAttachedEntityCount3(); } catch {}

            double? newSysVal = null;
            try
            {
                Dimension nd = newDisp.GetDimension2(0) as Dimension ?? newDisp.GetDimension() as Dimension;
                if (nd != null)
                {
                    object v = nd.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                    if (v is double[] arr && arr.Length > 0) newSysVal = arr[0];
                    else if (v is double d) newSysVal = d;
                    else newSysVal = nd.GetSystemValue2("");
                }
            }
            catch {}

            double deltaValMm = -1.0;
            if (oldSysVal.HasValue && newSysVal.HasValue)
            {
                deltaValMm = Math.Abs(newSysVal.Value - oldSysVal.Value) * 1000.0;
            }

            double effTolMm = oldSysVal.HasValue ? Math.Max(0.15, Math.Abs(oldSysVal.Value * 1000.0) * 0.001) : 0.15;

            if (newIsDangling || newAttachedCount != 2 || !newSysVal.HasValue || deltaValMm > effTolMm)
            {
                LogDebug($"{tPrefix} FAILED: TARGET_CREATE_VERIFY_FAILED (Dangling={newIsDangling}, Attached={newAttachedCount}, DeltaVal={deltaValMm:F4} mm).");
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "TARGET_CREATE_VERIFY_FAILED" };
            }

            LogDebug($"{tPrefix} H NEW_GEOMETRY_VERIFIED");

            Dimension newDimObj = newDisp.GetDimension2(0) as Dimension ?? newDisp.GetDimension() as Dimension;
            string propCopyText = "MATCH";
            try
            {
                if (!string.IsNullOrEmpty(oldPrefix)) newDisp.SetText((int)swDimensionTextParts_e.swDimensionTextPrefix, oldPrefix);
                if (!string.IsNullOrEmpty(oldSuffix)) newDisp.SetText((int)swDimensionTextParts_e.swDimensionTextSuffix, oldSuffix);
                if (!string.IsNullOrEmpty(oldCalloutAbove)) newDisp.SetText((int)swDimensionTextParts_e.swDimensionTextCalloutAbove, oldCalloutAbove);
                if (!string.IsNullOrEmpty(oldCalloutBelow)) newDisp.SetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow, oldCalloutBelow);
            }
            catch (Exception ex) { propCopyText = "ERROR: " + ex.Message; }

            string propCopyPrecision = "MATCH";
            try
            {
                if (oldPrimaryPrecision >= 0)
                {
                    newDisp.SetPrecision2(
                        oldPrimaryPrecision,
                        oldDualPrecision >= 0 ? oldDualPrecision : 0,
                        oldPrimaryTolPrecision >= 0 ? oldPrimaryTolPrecision : 0,
                        oldDualTolPrecision >= 0 ? oldDualTolPrecision : 0);
                }
            }
            catch (Exception ex) { propCopyPrecision = "ERROR: " + ex.Message; }

            string propCopyTolerance = "MATCH";
            try
            {
                DimensionTolerance newTol = newDimObj?.Tolerance;
                if (newTol != null && oldTolType >= 0)
                {
                    newTol.Type = oldTolType;
                    if (oldTolType != (int)swTolType_e.swTolNONE)
                    {
                        newTol.SetValues(oldTolMin, oldTolMax);
                    }
                }
            }
            catch (Exception ex) { propCopyTolerance = "ERROR: " + ex.Message; }

            string propCopyFormat = "MATCH";
            try { newAnnot.SetTextFormat(0, oldUseDocFormat, oldTf); } catch (Exception ex) { propCopyFormat = "ERROR: " + ex.Message; }

            string propCopyUnits = "MATCH";
            try
            {
                if (oldLengthUnit >= 0)
                {
                    newDisp.SetUnits(oldUseDocUnits, oldLengthUnit, oldFractionBase, oldFractionValue, oldRoundToFraction);
                }
            }
            catch (Exception ex) { propCopyUnits = "ERROR: " + ex.Message; }

            string propCopyArrow = "MATCH";
            try { if (oldArrowSide >= 0) newDisp.ArrowSide = oldArrowSide; } catch (Exception ex) { propCopyArrow = "ERROR: " + ex.Message; }

            string propCopyLayer = "MATCH";
            try
            {
                if (!string.IsNullOrEmpty(oldLayer)) newAnnot.Layer = oldLayer;
                if (oldColor != -1) newAnnot.Color = oldColor;
            }
            catch (Exception ex) { propCopyLayer = "ERROR: " + ex.Message; }

            string propMatchLevel = (propCopyText == "MATCH" && propCopyPrecision == "MATCH" && propCopyTolerance == "MATCH" && propCopyFormat == "MATCH" && propCopyUnits == "MATCH" && propCopyArrow == "MATCH" && propCopyLayer == "MATCH") ? "FULL_MATCH" : "PARTIAL_MATCH";

            LogDebug($"{tPrefix} I PRESENTATION_CLONED");

            if (oldPos != null && oldPos.Length >= 3)
            {
                try { newAnnot.SetPosition2(oldPos[0], oldPos[1], oldPos[2]); } catch {}
            }

            double[] newPosAfterMove = null;
            try { newPosAfterMove = newAnnot.GetPosition() as double[]; } catch {}

            double deltaPosMm = 0.0;
            if (oldPos != null && newPosAfterMove != null && oldPos.Length >= 2 && newPosAfterMove.Length >= 2)
            {
                double dx = Math.Abs(newPosAfterMove[0] - oldPos[0]) * 1000.0;
                double dy = Math.Abs(newPosAfterMove[1] - oldPos[1]) * 1000.0;
                deltaPosMm = Math.Sqrt(dx * dx + dy * dy);
            }

            LogDebug($"{tPrefix} J POSITION_RESTORED");

            bool valueMatchPass = (newSysVal.HasValue && deltaValMm <= effTolMm);
            bool posMatchPass = (deltaPosMm <= 0.2);
            bool refValidPass = (!newIsDangling && newAttachedCount == 2);
            bool presentationPass = (propMatchLevel == "FULL_MATCH" || propMatchLevel == "PARTIAL_MATCH");

            bool deleteAllowed = valueMatchPass && posMatchPass && refValidPass && presentationPass;

            if (!deleteAllowed)
            {
                LogDebug($"{tPrefix} FAILED: DELETE_ALLOWED = FALSE (ValueMatch={valueMatchPass}, PosMatch={posMatchPass}, RefValid={refValidPass}, PresMatch={presentationPass}).");
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "DELETE_SAFETY_GATE_FAILED" };
            }

            LogDebug($"{tPrefix} K DELETE_ALLOWED");

            string newDimFullName = "";
            try
            {
                Dimension nd = newDisp.GetDimension2(0) as Dimension ?? newDisp.GetDimension() as Dimension;
                if (nd != null) newDimFullName = nd.FullName ?? "";
            }
            catch {}
            if (string.IsNullOrEmpty(newDimFullName) && newAnnot != null)
            {
                try { newDimFullName = newAnnot.GetName() ?? ""; } catch {}
            }

            if (string.IsNullOrEmpty(oldDimFullName) || oldDimFullName.Equals(newDimFullName, StringComparison.OrdinalIgnoreCase))
            {
                LogDebug($"{tPrefix} FAILED: Ambiguous dimension identity (Names identical or empty).");
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "AMBIGUOUS_DIM_IDENTITY" };
            }

            targetAnnot = null;
            targetDispDim = null;
            oldDimObj = null;

            swModel.ClearSelection2(true);
            LogDebug($"{tPrefix} L SELECTION_CLEARED");

            IModelDocExtension ext = swModel.Extension;
            bool selectByIdResult = false;
            try
            {
                selectByIdResult = ext.SelectByID2(
                    oldDimFullName,
                    "DIMENSION",
                    0.0,
                    0.0,
                    0.0,
                    false,
                    0,
                    null,
                    0);
            }
            catch (Exception ex)
            {
                LogDebug($"{tPrefix} SelectByID2 Exception: " + ex.Message);
            }

            LogDebug($"{tPrefix} M SELECT_BY_ID_RETURNED");

            if (!selectByIdResult)
            {
                LogDebug($"{tPrefix} FAILED: SelectByID2 returned false.");
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "FAIL_SELECT_BY_ID" };
            }

            int selCountAfterSelect = 0;
            int selTypeRaw = -1;
            string selTypeName = "<none>";

            if (selMgr != null)
            {
                try
                {
                    selCountAfterSelect = selMgr.GetSelectedObjectCount2(-1);
                    if (selCountAfterSelect >= 1)
                    {
                        selTypeRaw = selMgr.GetSelectedObjectType3(1, -1);
                        selTypeName = ((swSelectType_e)selTypeRaw).ToString();
                    }
                }
                catch {}
            }

            if (selCountAfterSelect != 1 || (selTypeRaw != (int)swSelectType_e.swSelDIMENSIONS && selTypeName.IndexOf("DIMENSION", StringComparison.OrdinalIgnoreCase) < 0))
            {
                LogDebug($"{tPrefix} FAILED: Invalid old selection (Count={selCountAfterSelect}, Type={selTypeName}).");
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "OLD_SELECTION_VERIFY_FAILED" };
            }

            LogDebug($"{tPrefix} N OLD_SELECTION_VERIFIED");

            const int deleteOptions = 0;
            LogDebug($"{tPrefix} O ABOUT_TO_DELETE");

            bool deleteResult = false;
            try
            {
                deleteResult = ext.DeleteSelection2(deleteOptions);
            }
            catch (Exception ex)
            {
                LogDebug($"{tPrefix} DeleteSelection2 Exception: " + ex.Message);
            }

            LogDebug($"{tPrefix} P DELETE_RETURNED");

            if (!deleteResult)
            {
                LogDebug($"{tPrefix} FAILED: DeleteSelection2 returned false.");
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "FAIL_DELETE_RETURNED_FALSE" };
            }

            swModel.ClearSelection2(true);
            LogDebug($"{tPrefix} Q POST_DELETE_CLEARED");

            int postDisplayCount = 0;
            int postDanglingCount = 0;
            CountTotalDrawingDimensions(swDrawing, out postDisplayCount, out postDanglingCount);

            bool newPostDangling = true;
            try { newPostDangling = newAnnot.IsDangling(); } catch {}

            int newPostAttached = 0;
            try { newPostAttached = newAnnot.GetAttachedEntityCount3(); } catch {}

            LogDebug($"{tPrefix} R FRESH_VERIFY_COMPLETE");

            if (newPostDangling || newPostAttached != 2)
            {
                LogDebug($"{tPrefix} FAILED: Post-delete new dimension invalid (Dangling={newPostDangling}, Attached={newPostAttached}).");
                return new SingleTargetRepairResult
                {
                    Status = SingleTargetStatus.Failed,
                    Reason = "NEW_DIM_INVALID_AFTER_DELETE",
                    IsUnsafeState = true,
                    PostDisplayCount = postDisplayCount,
                    PostDanglingCount = postDanglingCount
                };
            }

            bool displayCountMaintained = (postDisplayCount == currentDisplayBefore);
            bool danglingCountDecreased = (postDanglingCount == currentDanglingBefore - 1);

            if (!displayCountMaintained || !danglingCountDecreased)
            {
                LogDebug($"{tPrefix} COUNT_ANOMALY: Display ({currentDisplayBefore}->{postDisplayCount}), Dangling ({currentDanglingBefore}->{postDanglingCount}).");
                return new SingleTargetRepairResult
                {
                    Status = SingleTargetStatus.Failed,
                    Reason = $"COUNT_ANOMALY (Display: {currentDisplayBefore}->{postDisplayCount}, Dangling: {currentDanglingBefore}->{postDanglingCount})",
                    IsUnsafeState = true,
                    PostDisplayCount = postDisplayCount,
                    PostDanglingCount = postDanglingCount
                };
            }

            LogDebug($"{tPrefix} S SUCCESS");

            return new SingleTargetRepairResult
            {
                Status = SingleTargetStatus.Success,
                PostDisplayCount = postDisplayCount,
                PostDanglingCount = postDanglingCount
            };
        }

        private static void CountTotalDrawingDimensions(
            DrawingDoc swDrawing,
            out int totalDisplayDims,
            out int totalDanglingDims)
        {
            totalDisplayDims = 0;
            totalDanglingDims = 0;

            if (swDrawing == null) return;

            try
            {
                string[] sheetNames = swDrawing.GetSheetNames() as string[];
                if (sheetNames == null) return;

                foreach (string sName in sheetNames)
                {
                    SolidWorks.Interop.sldworks.View sView = swDrawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                    SolidWorks.Interop.sldworks.View cView = sView?.GetNextView() as SolidWorks.Interop.sldworks.View;

                    while (cView != null)
                    {
                        DisplayDimension dd = cView.GetFirstDisplayDimension5() as DisplayDimension;
                        while (dd != null)
                        {
                            totalDisplayDims++;
                            Annotation a = dd.GetAnnotation() as Annotation;
                            bool isDang = (a != null) && a.IsDangling();

                            if (isDang)
                            {
                                totalDanglingDims++;
                            }

                            dd = dd.GetNext5() as DisplayDimension;
                        }

                        cView = cView.GetNextView() as SolidWorks.Interop.sldworks.View;
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug("CountTotalDrawingDimensions Exception: " + ex.Message);
            }
        }

        public static bool IsValidSolidWorksFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.ToUpperInvariant();
            return ext == ".SLDPRT" || ext == ".SLDASM" || ext == ".SLDDRW";
        }

        private static List<DocumentDependencyInfo> ScanMissingModelReferences(
            ISldWorks swApp,
            DrawingDoc swDrawing,
            ModelDoc2 swModel)
        {
            List<DocumentDependencyInfo> list = new List<DocumentDependencyInfo>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string drawingPath = swModel.GetPathName();
                if (!string.IsNullOrEmpty(drawingPath) && swApp != null)
                {
                    object depsObj = swApp.GetDocumentDependencies2(drawingPath, false, false, false);
                    if (depsObj is string[] depArr && depArr.Length > 0)
                    {
                        for (int i = 0; i < depArr.Length; i += 2)
                            {
                            string depName = depArr[i] ?? "";
                            string depPath = (i + 1 < depArr.Length) ? (depArr[i + 1] ?? "") : "";

                            if (string.IsNullOrWhiteSpace(depPath) && IsValidSolidWorksFilePath(depName))
                            {
                                depPath = depName;
                                depName = Path.GetFileNameWithoutExtension(depPath);
                            }

                            string normalizedPath = "";
                            bool isValidPath = false;
                            bool fileExists = false;

                            if (!string.IsNullOrWhiteSpace(depPath) && IsValidSolidWorksFilePath(depPath))
                            {
                                try
                                {
                                    normalizedPath = Path.GetFullPath(depPath);
                                    isValidPath = true;
                                    fileExists = File.Exists(normalizedPath);
                                }
                                catch
                                {
                                    normalizedPath = depPath;
                                    isValidPath = false;
                                    fileExists = false;
                                }
                            }

                            if (!string.IsNullOrEmpty(normalizedPath))
                            {
                                if (seenPaths.Add(normalizedPath))
                                {
                                    list.Add(new DocumentDependencyInfo
                                    {
                                        Index = list.Count + 1,
                                        Name = !string.IsNullOrWhiteSpace(depName) ? depName : Path.GetFileNameWithoutExtension(normalizedPath),
                                        Path = depPath,
                                        NormalizedPath = normalizedPath,
                                        IsValidFilePath = isValidPath,
                                        FileExists = fileExists,
                                        IsResolved = fileExists
                                    });
                                }
                            }
                            else if (!string.IsNullOrWhiteSpace(depName))
                            {
                                list.Add(new DocumentDependencyInfo
                                {
                                    Index = list.Count + 1,
                                    Name = depName,
                                    Path = "<none>",
                                    NormalizedPath = "",
                                    IsValidFilePath = false,
                                    FileExists = false,
                                    IsResolved = false
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug("ScanMissingModelReferences Exception: " + ex.Message);
            }

            try
            {
                string[] sheetNames = swDrawing.GetSheetNames() as string[];
                if (sheetNames != null)
                {
                    foreach (string sName in sheetNames)
                    {
                        SolidWorks.Interop.sldworks.View sView = swDrawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                        SolidWorks.Interop.sldworks.View cView = sView?.GetNextView() as SolidWorks.Interop.sldworks.View;

                        while (cView != null)
                        {
                            string refModelPath = "";
                            try { refModelPath = cView.GetReferencedModelName() ?? ""; } catch {}

                            if (!string.IsNullOrWhiteSpace(refModelPath) && IsValidSolidWorksFilePath(refModelPath))
                            {
                                string norm = "";
                                bool exists = false;
                                try
                                {
                                    norm = Path.GetFullPath(refModelPath);
                                    exists = File.Exists(norm);
                                }
                                catch
                                {
                                    norm = refModelPath;
                                }

                                if (seenPaths.Add(norm))
                                {
                                    list.Add(new DocumentDependencyInfo
                                    {
                                        Index = list.Count + 1,
                                        Name = Path.GetFileNameWithoutExtension(norm),
                                        Path = refModelPath,
                                        NormalizedPath = norm,
                                        IsValidFilePath = true,
                                        FileExists = exists,
                                        IsResolved = exists
                                    });
                                }
                            }

                            cView = cView.GetNextView() as SolidWorks.Interop.sldworks.View;
                        }
                    }
                }
            }
            catch {}

            return list;
        }

        private static void ClassifyFailureMode(
            DanglingDimensionInfo info,
            ViewGeometryInfo viewGeom,
            bool viewModelResolved,
            string viewRefModelName)
        {
            if (info == null) return;

            bool viewActualModelExists = false;
            if (!string.IsNullOrEmpty(viewRefModelName) && IsValidSolidWorksFilePath(viewRefModelName))
            {
                try { viewActualModelExists = File.Exists(viewRefModelName); } catch {}
            }

            if (!viewModelResolved && !viewActualModelExists)
            {
                info.FailureMode = RepairDimFailureMode.ModelFileMissingOrUnresolved;
                info.FailureModeReason = "Referenced model file does not exist on disk or drawing view is unresolved.";
                info.HasMissingModelReference = true;
                info.MissingModelPath = !string.IsNullOrEmpty(viewRefModelName) ? viewRefModelName : (viewGeom?.ReferencedDoc ?? "<unknown>");
                info.MissingModelName = Path.GetFileName(info.MissingModelPath);
                info.CurrentViewModelResolved = false;
                info.RouteCCandidateAvailable = false;
                info.RequiresDimensionRecreate = false;
                info.RecommendedAction = "RESTORE_MODEL_REFERENCE_FIRST";
                return;
            }

            if (!viewModelResolved && viewActualModelExists)
            {
                LogDebug($"  [CLASSIFIER_CONTRADICTION] View model exists on disk at '{viewRefModelName}' but viewModelResolved was false. Overriding to RESOLVED.");
                viewModelResolved = true;
            }

            info.CurrentViewModelResolved = true;
            info.HasMissingModelReference = false;

            bool hasLiveAnchor = (info.AnchorReferenceIndex >= 0) &&
                                 (info.AnchorEntity != null) &&
                                 (info.AnchorEntityType != (int)swSelectType_e.swSelNOTHING);

            if (!hasLiveAnchor || info.AttachedEntityCount < 2 || info.CandidateDecision == "DEFERRED_FULLY_LOST")
            {
                info.FailureMode = RepairDimFailureMode.FullyLostReference;
                info.FailureModeReason = "Dimension has no surviving live reference anchor.";
                info.RouteCCandidateAvailable = false;
                info.RequiresDimensionRecreate = false;
                info.RecommendedAction = "MANUAL_REVIEW";
                return;
            }

            bool isLinear = info.DimensionType == swDimensionType_e.swLinearDimension ||
                            info.DimensionType == swDimensionType_e.swHorLinearDimension ||
                            info.DimensionType == swDimensionType_e.swVertLinearDimension;

            if (!isLinear || info.AnchorEntityType != (int)swSelectType_e.swSelEDGES)
            {
                info.FailureMode = RepairDimFailureMode.UnsupportedAnchor;
                info.FailureModeReason = !isLinear ? $"Dimension type '{info.DimensionTypeString}' is not supported in linear pipeline." : $"Anchor entity type '{((swSelectType_e)info.AnchorEntityType).ToString()}' is not a linear edge.";
                info.RouteCCandidateAvailable = false;
                info.RequiresDimensionRecreate = false;
                info.RecommendedAction = "UNSUPPORTED";
                return;
            }

            if (info.CandidateDecision == "HIGH_CONFIDENCE" && info.Candidates.Count > 0)
            {
                info.FailureMode = RepairDimFailureMode.ComponentReinsertedOrGeometryReplaced;
                info.FailureModeReason = "Old attached entity reference is dead, but high-confidence Route C replacement edge geometry exists on current model.";
                info.RouteCCandidateAvailable = true;
                info.RequiresDimensionRecreate = true;
                info.RecommendedAction = "RECREATE_DIMENSION_REQUIRED";
            }
            else
            {
                info.FailureMode = RepairDimFailureMode.GeometryChangedNoCandidate;
                info.FailureModeReason = info.CandidateDecision == "AMBIGUOUS" ? "Multiple ambiguous Route C candidates found." : "No replacement geometry matching target dimension distance found.";
                info.RouteCCandidateAvailable = false;
                info.RequiresDimensionRecreate = false;
                info.RecommendedAction = "MANUAL_REVIEW";
            }
        }

        private static DanglingDimensionInfo ExtractDanglingInfo(
            string sheetName,
            string viewName,
            DisplayDimension dispDim,
            Annotation annot)
        {
            DanglingDimensionInfo info = new DanglingDimensionInfo
            {
                SheetName = sheetName,
                ViewName = viewName
            };

            Dimension dim = null;
            try { dim = dispDim.GetDimension2(0) as Dimension; } catch {}
            if (dim == null) { try { dim = dispDim.GetDimension() as Dimension; } catch {} }

            if (dim != null) { info.DimensionName = dim.FullName; }
            else if (annot != null) { info.DimensionName = annot.GetName(); }
            else { info.DimensionName = "<Unknown>"; }

            try
            {
                info.DimensionTypeRaw = dispDim.Type2;
                info.DimensionType = (swDimensionType_e)info.DimensionTypeRaw;
            }
            catch
            {
                info.DimensionTypeRaw = 0;
                info.DimensionType = swDimensionType_e.swDimensionTypeUnknown;
            }

            try
            {
                string prefix = dispDim.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? "";
                string suffix = dispDim.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix) ?? "";
                info.DisplayText = (prefix + " " + suffix).Trim();
            }
            catch { info.DisplayText = ""; }

            try
            {
                if (dim != null)
                {
                    object values = dim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                    if (values is double[] arr && arr.Length > 0) info.SystemValue = arr[0];
                    else if (values is double d) info.SystemValue = d;
                    else info.SystemValue = dim.GetSystemValue2("");
                }
            }
            catch { info.SystemValue = null; }

            if (annot != null)
            {
                try
                {
                    double[] pos = annot.GetPosition() as double[];
                    if (pos != null && pos.Length >= 3)
                    {
                        info.Position = new double[] { pos[0], pos[1], pos[2] };
                    }
                }
                catch {}

                try { info.AttachedEntityCount = annot.GetAttachedEntityCount3(); } catch { info.AttachedEntityCount = 0; }

                object[] attachedEnts = null;
                try
                {
                    object entsObj = annot.GetAttachedEntities3();
                    if (entsObj is object[] arr) attachedEnts = arr;
                }
                catch {}

                object typesObj = null;
                try { typesObj = annot.GetAttachedEntityTypes(); } catch {}

                int[] typesArr = null;
                if (typesObj is int[] intArr) { typesArr = intArr; }
                else if (typesObj is object[] objArr)
                {
                    typesArr = new int[objArr.Length];
                    for (int i = 0; i < objArr.Length; i++)
                    {
                        try { typesArr[i] = Convert.ToInt32(objArr[i]); } catch { typesArr[i] = (int)swSelectType_e.swSelNOTHING; }
                    }
                }

                int maxEntries = Math.Max(info.AttachedEntityCount, Math.Max(attachedEnts != null ? attachedEnts.Length : 0, typesArr != null ? typesArr.Length : 0));
                if (maxEntries == 0)
                {
                    info.LostReferences.Add("No attached references returned by API (All lost)");
                }

                int lostCount = 0;
                int validCount = 0;

                for (int i = 0; i < maxEntries; i++)
                {
                    object ent = (attachedEnts != null && i < attachedEnts.Length) ? attachedEnts[i] : null;
                    int t = (typesArr != null && i < typesArr.Length) ? typesArr[i] : (int)swSelectType_e.swSelNOTHING;

                    info.AttachedEntityTypes.Add(t);

                    string typeStr = ((swSelectType_e)t).ToString();
                    bool isNullOrNothing = (ent == null) || (t == (int)swSelectType_e.swSelNOTHING);

                    if (isNullOrNothing)
                    {
                        lostCount++;
                        if (info.LostReferenceIndex == -1) info.LostReferenceIndex = i;
                        string lostDesc = $"Ref[{i}]: NULL / swSelNOTHING (Type: {typeStr})";
                        info.LostReferences.Add(lostDesc);
                        info.AttachedEntityDescriptions.Add($"Ref[{i}]: [LOST / NULL] (Type: {typeStr})");
                    }
                    else
                    {
                        validCount++;
                        if (info.AnchorReferenceIndex == -1)
                        {
                            info.AnchorReferenceIndex = i;
                            info.AnchorEntity = ent;
                            info.AnchorEntityType = t;
                        }
                        string entDesc = $"Ref[{i}]: {ent.GetType().Name} (Type: {typeStr})";
                        info.AttachedEntityDescriptions.Add(entDesc);
                    }
                }

                if (lostCount != 1 || validCount != 1)
                {
                    if (lostCount > 1) info.AnchorReferenceIndex = -1;
                }
            }

            return info;
        }

        private static string FormatDimensionValue(double systemVal, swDimensionType_e dimType)
        {
            switch (dimType)
            {
                case swDimensionType_e.swAngularDimension:
                case swDimensionType_e.swAngularOrdinateDimension:
                    double deg = systemVal * 180.0 / Math.PI;
                    return $"{deg:G6}° ({systemVal:G6} rad)";

                case swDimensionType_e.swRadialDimension:
                    double rMm = systemVal * 1000.0;
                    return $"R{rMm:G6} mm ({systemVal:G6} m)";

                case swDimensionType_e.swDiameterDimension:
                    double dMm = systemVal * 1000.0;
                    return $"Ø{dMm:G6} mm ({systemVal:G6} m)";

                default:
                    double mm = systemVal * 1000.0;
                    return $"{mm:G6} mm ({systemVal:G6} m)";
            }
        }

        private static string FormatScale(double scaleDecimal)
        {
            if (scaleDecimal <= 0.0)
                return "<invalid>";

            if (Math.Abs(scaleDecimal - 1.0) < 1e-9)
                return "1:1";

            if (scaleDecimal < 1.0)
            {
                double denominator = 1.0 / scaleDecimal;
                return $"1:{denominator:G6}";
            }

            return $"{scaleDecimal:G6}:1";
        }

        private static void LogDanglingDetail(DanglingDimensionInfo info, ViewGeometryInfo viewGeom)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\n[DANGLING]");
            sb.AppendLine($"  View                   : {info.ViewName}");
            sb.AppendLine($"  Name                   : {info.DimensionName}");
            sb.AppendLine($"  Type                   : {info.DimensionTypeString} (Raw: {info.DimensionTypeRaw})");
            sb.AppendLine($"  Display Text           : '{info.DisplayText}'");
            if (info.SystemValue.HasValue)
            {
                sb.AppendLine($"  Value                  : {FormatDimensionValue(info.SystemValue.Value, info.DimensionType)}");
                sb.AppendLine($"  Raw System Value       : {info.SystemValue.Value:G8}");
            }
            else
            {
                sb.AppendLine("  Value                  : <null>");
            }

            if (info.Position != null)
            {
                sb.AppendLine($"  Annotation Position    : ({info.Position[0]:F4}, {info.Position[1]:F4}, {info.Position[2]:F4})");
            }

            sb.AppendLine($"  Attached Entity Count  : {info.AttachedEntityCount}");
            sb.AppendLine($"  Attached Entity Types  : [{string.Join(", ", info.AttachedEntityTypes)}]");

            if (info.LostReferenceIndex >= 0)
                sb.AppendLine($"  Lost Ref               : {info.LostReferenceIndex}");
            else
                sb.AppendLine("  Lost Ref               : ALL / NONE");

            if (info.AnchorReferenceIndex >= 0)
            {
                sb.AppendLine($"  Anchor Ref             : {info.AnchorReferenceIndex}");
                sb.AppendLine($"  Anchor Type            : {((swSelectType_e)info.AnchorEntityType).ToString()}");
                sb.AppendLine($"  Anchor Orientation     : {info.AnchorOrientation}");
                sb.AppendLine($"  Anchor Component       : {info.AnchorComponentName ?? "<none>"}");
                sb.AppendLine($"  Anchor Component Path  : {info.AnchorComponentPath ?? "<none>"}");
                sb.AppendLine($"  Anchor Occurrence Key  : {info.AnchorOccurrenceKey ?? "<none>"}");

                sb.AppendLine("  === ANCHOR COORDINATE COMPARISON ===");
                if (info.AnchorDrawingStartPt != null && info.AnchorDrawingEndPt != null)
                {
                    sb.AppendLine($"    ROUTE A (Component -> Assembly -> View): Start=({info.AnchorDrawingStartPt[0]:F4}, {info.AnchorDrawingStartPt[1]:F4}), End=({info.AnchorDrawingEndPt[0]:F4}, {info.AnchorDrawingEndPt[1]:F4}), Prox={info.AnchorDisplayProximityRouteA:F2} mm");
                }
                if (info.AnchorDirectViewStartPt != null && info.AnchorDirectViewEndPt != null)
                {
                    sb.AppendLine($"    ROUTE B (Direct Model -> View): Start=({info.AnchorDirectViewStartPt[0]:F4}, {info.AnchorDirectViewStartPt[1]:F4}), End=({info.AnchorDirectViewEndPt[0]:F4}, {info.AnchorDirectViewEndPt[1]:F4}), Prox={info.AnchorDisplayProximityRouteB:F2} mm");
                }
                if (info.AnchorPolylineMatches.Count > 0)
                {
                    sb.AppendLine($"    ROUTE C (View Polyline Ground Truth): Match Count={info.AnchorPolylineMatches.Count}");
                    for (int mi = 0; mi < info.AnchorPolylineMatches.Count; mi++)
                    {
                        var m = info.AnchorPolylineMatches[mi];
                        sb.AppendLine($"      Match #{mi + 1}: RawRecordIdx={m.RawRecordIndex}, EntityArrayIdx={m.EntityArrayIndex}, OwnerMethod={m.OwnerMethod}");
                        sb.AppendLine($"        Entity Type : {(m.ModelEntity != null ? m.ModelEntity.GetType().Name : "NULL")} (Edge: {m.ModelEdge != null})");
                        sb.AppendLine($"        Component   : {m.ComponentName ?? "<none>"} (Key: {m.ComponentOccurrenceKey ?? "<none>"})");
                        if (m.SheetStart != null && m.SheetEnd != null)
                        {
                            sb.AppendLine($"        SHEET COORD : Start=({m.SheetStart[0]:F4}, {m.SheetStart[1]:F4}), End=({m.SheetEnd[0]:F4}, {m.SheetEnd[1]:F4}), LenSheet={m.LengthSheetMm:F2} mm, Orient={m.Orientation}");
                        }
                        sb.AppendLine($"        Display Prox: {m.DisplayProximityMm:F2} mm");
                    }
                }
                else
                {
                    sb.AppendLine("    ROUTE C (View Polyline Ground Truth): UNMATCHED");
                }
            }

            if (info.DisplayLines.Count > 0)
            {
                sb.AppendLine($"  [DISPLAY DATA LINES ({info.DisplayLines.Count})]");
                int maxLines = Math.Min(6, info.DisplayLines.Count);
                for (int i = 0; i < maxLines; i++)
                {
                    sb.AppendLine($"    {info.DisplayLines[i]}");
                }
            }

            if (info.Candidates.Count > 0)
            {
                sb.AppendLine($"  [CANDIDATES - TOP {Math.Min(5, info.Candidates.Count)} (TOTAL AFTER HARD GATE: {info.Candidates.Count})]");
                int topCount = Math.Min(5, info.Candidates.Count);
                for (int i = 0; i < topCount; i++)
                {
                    var c = info.Candidates[i];
                    sb.AppendLine($"    Candidate #{c.Rank} (RawRecord #{c.RawRecordIndex:D3}, EntityIdx #{c.EntityArrayIndex:D3})");
                    sb.AppendLine($"      Component            : {c.ComponentName ?? "<none>"} (Key: {c.ComponentOccurrenceKey ?? "<none>"})");
                    sb.AppendLine($"      Anchor Component     : {info.AnchorComponentName ?? "<none>"} (Key: {info.AnchorOccurrenceKey ?? "<none>"})");
                    sb.AppendLine($"      Geometry             : {c.GeometryType} ({c.EntityTypeName})");
                    sb.AppendLine($"      Orientation          : {c.Orientation}");
                    sb.AppendLine($"      Same Component       : {c.SameComponentAsAnchor}");
                    sb.AppendLine($"      Coord Method         : {c.CoordinateMethod ?? "<none>"}");
                    if (c.DrawingStartPt != null && c.DrawingEndPt != null)
                    {
                        sb.AppendLine($"      Sheet Coords         : Start=({c.DrawingStartPt[0]:F4}, {c.DrawingStartPt[1]:F4}), End=({c.DrawingEndPt[0]:F4}, {c.DrawingEndPt[1]:F4})");
                    }
                    sb.AppendLine($"      Sheet Distance       : {c.MeasuredSheetDistanceMm:F4} mm");
                    sb.AppendLine($"      View Scale           : {FormatScale(c.ViewScaleDecimal)}");
                    sb.AppendLine($"      Model Distance       : {c.MeasuredModelDistanceMm:F4} mm");
                    sb.AppendLine($"      Signed Offset        : {c.SignedOffsetMm:F4} mm");
                    sb.AppendLine($"      Preferred Side       : {c.PreferredSide}");
                    sb.AppendLine($"      Display Witness Prox : {c.DisplayWitnessProximityMm:F2} mm ({c.DisplayWitnessCategory})");
                    sb.AppendLine($"      Target DIM           : {c.TargetDimensionMm:F4} mm");
                    sb.AppendLine($"      Distance Error       : {c.DistanceErrorMm:F4} mm");
                    sb.AppendLine($"      Distance Match       : {c.DistanceMatched}");
                    sb.AppendLine($"      Annotation Dist      : {c.AnnotationDistanceMm:F4} mm");
                    sb.AppendLine($"      Score                : {c.Score:F1}");
                    sb.AppendLine($"      Reason               : {c.Reason}");
                }
            }
            else
            {
                sb.AppendLine("  [CANDIDATES]: None");
            }

            if (info.DiagnosticNotes.Count > 0)
            {
                sb.AppendLine("  [DIAGNOSTIC NOTES]");
                foreach (var note in info.DiagnosticNotes)
                {
                    sb.AppendLine($"    * {note}");
                }
            }

            sb.AppendLine($"  Candidate Decision     : {info.CandidateDecision}");

            sb.AppendLine("  === FAILURE CLASSIFICATION ===");
            sb.AppendLine($"    Failure Mode         : {info.FailureMode}");
            sb.AppendLine($"    Failure Reason       : {info.FailureModeReason}");
            sb.AppendLine($"    View Model Resolved  : {info.CurrentViewModelResolved}");
            sb.AppendLine($"    Missing Model        : {info.HasMissingModelReference}");
            sb.AppendLine($"    Missing Model Path   : {info.MissingModelPath ?? "<none>"}");
            sb.AppendLine($"    Route C Candidate Avail: {info.RouteCCandidateAvailable}");
            sb.AppendLine($"    Requires Dimension Recreate: {info.RequiresDimensionRecreate}");
            sb.AppendLine($"    Recommended Action   : {info.RecommendedAction}");

            LogDebug(sb.ToString().TrimEnd());
        }

        private static void LogDebug(string msg)
        {
            try
            {
                string temp = Path.GetTempPath();
                string path = Path.Combine(temp, "RepairDimDebug.log");
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
                Debug.WriteLine(line);
                File.AppendAllText(path, line + System.Environment.NewLine);
            }
            catch {}
        }

        private static void InitLog()
        {
            try
            {
                string temp = Path.GetTempPath();
                string path = Path.Combine(temp, "RepairDimDebug.log");
                string header = $"=== REPAIR DIM SESSION: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===";
                File.WriteAllText(path, header + System.Environment.NewLine);
            }
            catch {}
        }
    }
}
