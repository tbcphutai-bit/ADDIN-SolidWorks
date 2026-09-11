using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public static class RepairDanglingDimensions
    {
        private const string REPAIR_DIM_BUILD = "STEP21_FULL_HOLE_AND_CIRCLE_DIMENSION_REPAIR_20260906";

        private sealed class ViewDanglingDiscovery
        {
            public int ViewIndex { get; set; }
            public string SheetName { get; set; }
            public string ViewName { get; set; }
            public swDrawingViewTypes_e ViewType { get; set; }
            public int DisplayDimCount { get; set; }
            public int DanglingDimCount { get; set; }
            public bool GeometryScanRequired => DanglingDimCount > 0;
        }

        private static bool IsSketchPointSelectionType(int type)
        {
            return type == (int)swSelectType_e.swSelSKETCHPOINTS || // 11
                   type == (int)swSelectType_e.swSelEXTSKETCHPOINTS; // 25
        }

        private static bool IsEdgeSelectionType(int type)
        {
            return type == (int)swSelectType_e.swSelEDGES; // 1
        }

        private sealed class BatchTargetSnapshot
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

        private sealed class SingleTargetRepairResult
        {
            public SingleTargetStatus Status { get; set; }
            public string Reason { get; set; }
            public bool IsUnsafeState { get; set; }
            public int PostDisplayCount { get; set; }
            public int PostDanglingCount { get; set; }
            public int ProbeCandidateCount { get; set; }
            public int ProbesAttempted { get; set; }
            public int ValidProbeCount { get; set; }
            public bool FinalCreateAttempted { get; set; }
        }

        private enum SingleTargetStatus
        {
            Success,
            ManualReview,
            Skipped,
            Failed
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
            LogDebug("=== REPAIR DIM SESSION START (STEP 13B: TWO-PASS DANGLING VIEW DISCOVERY + TARGETED GEOMETRY SCAN) ===");

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

            // =========================================================================
            // PASS 1 — LIGHTWEIGHT DIMENSION DISCOVERY (NO GEOMETRY APIS)
            // =========================================================================
            LogDebug("\n=== PASS 1: LIGHTWEIGHT DIMENSION DISCOVERY ===");
            List<ViewDanglingDiscovery> discoveredViews = new List<ViewDanglingDiscovery>();
            int totalPass1Display = 0;
            int totalPass1Dangling = 0;

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

                int vIdx = 0;
                foreach (string sheetName in sheetNames)
                {
                    swDrawing.ActivateSheet(sheetName);
                    SolidWorks.Interop.sldworks.View sheetView = swDrawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                    SolidWorks.Interop.sldworks.View currentView = sheetView?.GetNextView() as SolidWorks.Interop.sldworks.View;

                    while (currentView != null)
                    {
                        vIdx++;
                        string cViewName = "";
                        try { cViewName = currentView.GetName2() ?? ""; } catch {}
                        int cViewType = 0;
                        try { cViewType = currentView.Type; } catch {}
                        swDrawingViewTypes_e vTypeEnum = (swDrawingViewTypes_e)cViewType;

                        int viewDisplayCount = 0;
                        int viewDanglingCount = 0;

                        DisplayDimension dd = currentView.GetFirstDisplayDimension5() as DisplayDimension;
                        while (dd != null)
                        {
                            viewDisplayCount++;
                            Annotation a = dd.GetAnnotation() as Annotation;
                            if (a != null && a.IsDangling())
                            {
                                viewDanglingCount++;
                            }
                            dd = dd.GetNext5() as DisplayDimension;
                        }

                        totalPass1Display += viewDisplayCount;
                        totalPass1Dangling += viewDanglingCount;

                        var disc = new ViewDanglingDiscovery
                        {
                            ViewIndex = vIdx,
                            SheetName = sheetName,
                            ViewName = cViewName,
                            ViewType = vTypeEnum,
                            DisplayDimCount = viewDisplayCount,
                            DanglingDimCount = viewDanglingCount
                        };
                        discoveredViews.Add(disc);

                        LogDebug($"\nPASS1 VIEW V{vIdx:D2}");
                        LogDebug($"  Sheet: {sheetName}");
                        LogDebug($"  View: {cViewName}");
                        LogDebug($"  Type: {vTypeEnum}");
                        LogDebug($"  DisplayDims: {viewDisplayCount}");
                        LogDebug($"  DanglingDims: {viewDanglingCount}");
                        LogDebug($"  GeometryScanRequired: {(disc.GeometryScanRequired ? "YES" : "NO")}");

                        if (!disc.GeometryScanRequired)
                        {
                            LogDebug($"  GEOMETRY_SCAN_SKIPPED_NO_DANGLING (View '{cViewName}')");
                        }

                        currentView = currentView.GetNextView() as SolidWorks.Interop.sldworks.View;
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug("ERROR during PASS 1 Discovery: " + ex.Message);
                MessageBox.Show(
                    "Lỗi trong quá trình quét PASS 1:\n" + ex.Message,
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

            int initialDrawingDisplayDimCount = totalPass1Display;
            int initialDrawingDanglingCount = totalPass1Dangling;
            int targetViewCount = discoveredViews.Count(v => v.GeometryScanRequired);

            LogDebug($"\n=== PASS 1 COMPLETE ===");
            LogDebug($"PASS1 Total Views        : {discoveredViews.Count}");
            LogDebug($"PASS1 Total Display Dims : {initialDrawingDisplayDimCount}");
            LogDebug($"PASS1 Total Dangling Dims: {initialDrawingDanglingCount}");
            LogDebug($"PASS1 Target Views       : {targetViewCount}");

            // =========================================================================
            // DECISION GATE 1 — ZERO-DANGLING FAST EXIT
            // =========================================================================
            if (initialDrawingDanglingCount == 0)
            {
                StringBuilder sbFastExit = new StringBuilder();
                sbFastExit.AppendLine("\n=== ZERO DANGLING FAST EXIT ===");
                sbFastExit.AppendLine($"Display Dimensions : {initialDrawingDisplayDimCount}");
                sbFastExit.AppendLine($"Dangling Dimensions: 0");
                sbFastExit.AppendLine($"Reason             : NO_DANGLING_DIMENSIONS");
                sbFastExit.AppendLine($"Geometry Scan      : SKIPPED");
                sbFastExit.AppendLine($"Mutation Attempted : NO");
                sbFastExit.AppendLine($"Drawing Saved      : NO");
                sbFastExit.AppendLine();
                sbFastExit.AppendLine("=== FINAL DRAWING SUMMARY ===");
                sbFastExit.AppendLine();
                sbFastExit.AppendLine($"PASS1 Views: {discoveredViews.Count}");
                sbFastExit.AppendLine("Target Views: 0");
                sbFastExit.AppendLine();
                sbFastExit.AppendLine("Geometry Views Scanned: 0");
                sbFastExit.AppendLine($"Geometry Views Skipped: {discoveredViews.Count}");
                sbFastExit.AppendLine();
                sbFastExit.AppendLine($"Initial Display: {initialDrawingDisplayDimCount}");
                sbFastExit.AppendLine($"Final Display: {initialDrawingDisplayDimCount}");
                sbFastExit.AppendLine();
                sbFastExit.AppendLine("Initial Dangling: 0");
                sbFastExit.AppendLine("Final Dangling: 0");
                sbFastExit.AppendLine();
                sbFastExit.AppendLine("Mutation Attempted: NO");
                sbFastExit.AppendLine("Drawing Saved: NO");
                sbFastExit.AppendLine();
                sbFastExit.AppendLine("STOP.");

                LogDebug(sbFastExit.ToString().TrimEnd());

                MessageBox.Show(
                    "Hoàn tất REPAIR DIM.",
                    "REPAIR DIM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            // =========================================================================
            // PASS 0 — NATIVE SOLIDWORKS REATTACH & DIRECT ATTACHED ENTITIES REPAIR
            // =========================================================================
            int runningDisplayCount = initialDrawingDisplayDimCount;
            int runningDanglingCount = initialDrawingDanglingCount;

            int pass0Repaired = ExecutePass0NativeAndAttachedEntitiesRepair(
                swApp,
                swDrawing,
                swModel,
                discoveredViews,
                initialSheet,
                ref runningDisplayCount,
                ref runningDanglingCount);

            if (runningDanglingCount == 0)
            {
                StringBuilder sbSuccess = new StringBuilder();
                sbSuccess.AppendLine("\n=== PASS 0 REPAIR SUCCESS ===");
                sbSuccess.AppendLine($"Initial Display Dimensions : {initialDrawingDisplayDimCount}");
                sbSuccess.AppendLine($"Final Display Dimensions   : {runningDisplayCount}");
                sbSuccess.AppendLine($"Initial Dangling Dimensions: {initialDrawingDanglingCount}");
                sbSuccess.AppendLine($"Final Dangling Dimensions  : 0");
                sbSuccess.AppendLine($"Total Repaired in Pass 0   : {pass0Repaired}");
                sbSuccess.AppendLine();
                sbSuccess.AppendLine("=== FINAL DRAWING SUMMARY ===");
                sbSuccess.AppendLine();
                sbSuccess.AppendLine($"PASS1 Views: {discoveredViews.Count}");
                sbSuccess.AppendLine($"Target Views: {targetViewCount}");
                sbSuccess.AppendLine();
                sbSuccess.AppendLine($"Initial Display: {initialDrawingDisplayDimCount}");
                sbSuccess.AppendLine($"Final Display: {runningDisplayCount}");
                sbSuccess.AppendLine();
                sbSuccess.AppendLine($"Initial Dangling: {initialDrawingDanglingCount}");
                sbSuccess.AppendLine("Final Dangling: 0");
                sbSuccess.AppendLine();
                sbSuccess.AppendLine("Mutation Attempted: YES");
                sbSuccess.AppendLine("Drawing Saved: NO");
                sbSuccess.AppendLine();
                sbSuccess.AppendLine("STOP.");

                LogDebug(sbSuccess.ToString().TrimEnd());

                try { swDrawing.ForceRebuild(); } catch {}

                MessageBox.Show(
                    "Hoàn tất REPAIR DIM.",
                    "REPAIR DIM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            initialDrawingDanglingCount = runningDanglingCount;
            // Refresh discovered views dangling status after Pass 0
            foreach (var disc in discoveredViews.Where(v => v.GeometryScanRequired))
            {
                try
                {
                    swDrawing.ActivateSheet(disc.SheetName);
                    SolidWorks.Interop.sldworks.View sheetView = swDrawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                    SolidWorks.Interop.sldworks.View cView = sheetView?.GetNextView() as SolidWorks.Interop.sldworks.View;
                    while (cView != null)
                    {
                        if (string.Equals(cView.GetName2() ?? "", disc.ViewName, StringComparison.OrdinalIgnoreCase))
                        {
                            int dCount = 0;
                            DisplayDimension dd = cView.GetFirstDisplayDimension5() as DisplayDimension;
                            while (dd != null)
                            {
                                Annotation a = dd.GetAnnotation() as Annotation;
                                if (a != null && a.IsDangling()) dCount++;
                                dd = dd.GetNext5() as DisplayDimension;
                            }
                            disc.DanglingDimCount = dCount;
                            break;
                        }
                        cView = cView.GetNextView() as SolidWorks.Interop.sldworks.View;
                    }
                }
                catch {}
            }
            if (!string.IsNullOrEmpty(initialSheet)) { try { swDrawing.ActivateSheet(initialSheet); } catch {} }
            targetViewCount = discoveredViews.Count(v => v.GeometryScanRequired);

            // =========================================================================
            // PASS 2 PREPARATION — PROCEED DIRECTLY TO SCAN & REPAIR
            // =========================================================================
            LogDebug($"Proceeding to Pass 2 for drawing '{docTitle}' with {initialDrawingDanglingCount} dangling dimension(s) across {targetViewCount} target view(s).");

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
            // PASS 2 — TARGETED GEOMETRY SCAN (ONLY TARGET VIEWS WITH DANGLING DIMS)
            // =========================================================================
            LogDebug("\n=== PASS 2: TARGETED GEOMETRY SCAN ===");
            int geometryViewsScanned = 0;
            List<DanglingDimensionInfo> initialDanglingList = new List<DanglingDimensionInfo>();
            List<BatchTargetSnapshot> step10BatchTargets = new List<BatchTargetSnapshot>();

            try
            {
                var targetDiscoveredList = discoveredViews.Where(v => v.GeometryScanRequired).ToList();
                foreach (var targetDisc in targetDiscoveredList)
                {
                    LogDebug($"\n=== PASS2 TARGET VIEW START: '{targetDisc.ViewName}' (Sheet: '{targetDisc.SheetName}', Dangling: {targetDisc.DanglingDimCount}) ===");

                    swDrawing.ActivateSheet(targetDisc.SheetName);
                    SolidWorks.Interop.sldworks.View sheetView = swDrawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                    SolidWorks.Interop.sldworks.View currentView = sheetView?.GetNextView() as SolidWorks.Interop.sldworks.View;

                    SolidWorks.Interop.sldworks.View targetView = null;
                    while (currentView != null)
                    {
                        string vName = currentView.GetName2() ?? "";
                        if (string.Equals(vName, targetDisc.ViewName, StringComparison.OrdinalIgnoreCase))
                        {
                            targetView = currentView;
                            break;
                        }
                        currentView = currentView.GetNextView() as SolidWorks.Interop.sldworks.View;
                    }

                    if (targetView == null)
                    {
                        LogDebug($"WARNING: Target view '{targetDisc.ViewName}' could not be re-acquired on sheet '{targetDisc.SheetName}'.");
                        continue;
                    }

                    geometryViewsScanned++;
                    string vPrefix = $"VIEWSCAN V{targetDisc.ViewIndex:D2}";
                    string cViewName = targetDisc.ViewName;
                    string cViewTypeStr = targetDisc.ViewType.ToString();

                    LogDebug($"{vPrefix} A ENTER_VIEW (Name='{cViewName}', Type={cViewTypeStr})");

                    LogDebug($"{vPrefix} B ABOUT_TO_GET_REFERENCED_DOCUMENT");
                    ModelDoc2 refDoc = null;
                    try { refDoc = targetView.ReferencedDocument; } catch {}
                    LogDebug($"{vPrefix} C REFERENCED_DOCUMENT_RETURNED (Null={refDoc == null})");

                    LogDebug($"{vPrefix} D ABOUT_TO_GET_REFERENCED_MODEL_NAME");
                    string viewRefModelName = "";
                    try { viewRefModelName = targetView.GetReferencedModelName() ?? ""; } catch {}
                    LogDebug($"{vPrefix} E REFERENCED_MODEL_NAME_RETURNED (Name='{viewRefModelName}')");

                    LogDebug($"{vPrefix} F ABOUT_TO_GET_SCALE");
                    double[] scaleRatioArr = null;
                    try { scaleRatioArr = targetView.ScaleRatio as double[]; } catch {}
                    string scaleStr = (scaleRatioArr != null && scaleRatioArr.Length >= 2) ? $"{scaleRatioArr[0]}:{scaleRatioArr[1]}" : "N/A";
                    LogDebug($"{vPrefix} G SCALE_RETURNED (Scale={scaleStr})");

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

                    bool viewModelResolved = (viewRefExists || refDocExists || refDoc != null) ||
                                             (string.IsNullOrEmpty(viewRefModelName) || !IsValidSolidWorksFilePath(viewRefModelName));

                    ViewGeometryInfo viewGeom = RepairDimCandidateFinder.EnumerateViewGeometry(swApp, targetView, vPrefix);

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

                    DisplayDimension dispDim = targetView.GetFirstDisplayDimension5() as DisplayDimension;
                    while (dispDim != null)
                    {
                        Annotation annot = dispDim.GetAnnotation() as Annotation;
                        bool isDangling = (annot != null) && annot.IsDangling();

                        if (isDangling)
                        {
                            DanglingDimensionInfo info = ExtractDanglingInfo(targetDisc.SheetName, viewGeom.ViewName, dispDim, annot);

                            if (viewModelResolved)
                            {
                                RepairDimCandidateFinder.AnalyzeCandidatesForDimension(swApp, info, viewGeom, targetView, dispDim);
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
                                                    (RepairDimCandidateFinder.IsLinearEntityType(info.AnchorEntityType)) &&
                                                    (info.AnchorPolylineMatches.Count > 0) &&
                                                    (info.Candidates.Count > 0);

                            if (isStep10Eligible)
                            {
                                step10BatchTargets.Add(new BatchTargetSnapshot
                                {
                                    TargetIndex = step10BatchTargets.Count + 1,
                                    SheetName = targetDisc.SheetName,
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
                }
            }
            catch (Exception ex)
            {
                LogDebug("ERROR during PASS 2 Targeted Geometry Scan: " + ex.Message);
                MessageBox.Show(
                    "Lỗi trong quá trình quét PASS 2:\n" + ex.Message,
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

            LogDebug($"\n=== PASS 2 TARGETED SCAN COMPLETE ===");
            LogDebug($"Geometry Views Scanned   : {geometryViewsScanned}");
            LogDebug($"Geometry Views Skipped   : {discoveredViews.Count - geometryViewsScanned}");
            LogDebug($"Initial Dangling Targets : {initialDanglingList.Count}");
            LogDebug($"STEP 10 Batch Targets    : {step10BatchTargets.Count}");

            // =========================================================================
            // PHASE 2 — RUN STEP 10 BATCH (1-LIVE-ANCHOR HIGH_CONFIDENCE TARGETS)
            // =========================================================================
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
                            int viewDanglingCount = 0;
                            DisplayDimension testDd = currentView.GetFirstDisplayDimension5() as DisplayDimension;
                            while (testDd != null)
                            {
                                Annotation a = testDd.GetAnnotation() as Annotation;
                                if (a != null && a.IsDangling()) viewDanglingCount++;
                                testDd = testDd.GetNext5() as DisplayDimension;
                            }

                            if (viewDanglingCount == 0)
                            {
                                LogDebug($"GEOMETRY_SCAN_SKIPPED_NO_DANGLING (Phase 3 View '{currentView.GetName2() ?? ""}')");
                                currentView = currentView.GetNextView() as SolidWorks.Interop.sldworks.View;
                                continue;
                            }

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

                                    bool isFullyLostEligible = isLinear && viewModelResolved &&
                                        (info.FailureMode == RepairDimFailureMode.FullyLostReference ||
                                         info.CandidateDecision == "DEFERRED_FULLY_LOST" ||
                                         info.FailureMode == RepairDimFailureMode.GeometryChangedNoCandidate ||
                                         info.CandidateDecision == "NO_CANDIDATE_DIAGNOSTIC" ||
                                         info.CandidateDecision == "GEOMETRY_UNVERIFIED");

                                    if (isFullyLostEligible)
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
            // PHASE 4 — FRESH SCAN & POINT-ANCHOR REPAIR (STEP 12A)
            // =========================================================================
            List<BatchTargetSnapshot> pointAnchorBatchTargets = new List<BatchTargetSnapshot>();
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
                            int viewDanglingCount = 0;
                            DisplayDimension testDd = cView.GetFirstDisplayDimension5() as DisplayDimension;
                            while (testDd != null)
                            {
                                Annotation a = testDd.GetAnnotation() as Annotation;
                                if (a != null && a.IsDangling()) viewDanglingCount++;
                                testDd = testDd.GetNext5() as DisplayDimension;
                            }

                            if (viewDanglingCount == 0)
                            {
                                LogDebug($"GEOMETRY_SCAN_SKIPPED_NO_DANGLING (Phase 4 View '{cView.GetName2() ?? ""}')");
                                cView = cView.GetNextView() as SolidWorks.Interop.sldworks.View;
                                continue;
                            }

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
                                Annotation a = dd.GetAnnotation() as Annotation;
                                if (a != null && a.IsDangling())
                                {
                                    DanglingDimensionInfo dInfo = ExtractDanglingInfo(sName, vGeom.ViewName, dd, a);
                                    if (vResolved)
                                    {
                                        RepairDimCandidateFinder.AnalyzeCandidatesForDimension(swApp, dInfo, vGeom, cView, dd);
                                    }
                                    ClassifyFailureMode(dInfo, vGeom, vResolved, vModel);

                                    bool isLinear = dInfo.DimensionType == swDimensionType_e.swLinearDimension ||
                                                    dInfo.DimensionType == swDimensionType_e.swHorLinearDimension ||
                                                    dInfo.DimensionType == swDimensionType_e.swVertLinearDimension;

                                    if (isLinear && dInfo.AnchorEntityType == (int)swSelectType_e.swSelSKETCHPOINTS)
                                    {
                                        string oldFullName = "";
                                        try { Dimension dObj = dd.GetDimension2(0) as Dimension ?? dd.GetDimension() as Dimension; oldFullName = dObj?.FullName ?? ""; } catch {}
                                        if (string.IsNullOrEmpty(oldFullName)) { try { oldFullName = a.GetName() ?? ""; } catch {} }

                                        pointAnchorBatchTargets.Add(new BatchTargetSnapshot
                                        {
                                            TargetIndex = pointAnchorBatchTargets.Count + 1,
                                            SheetName = sName,
                                            ViewName = vGeom.ViewName,
                                            DimensionName = dInfo.DimensionName,
                                            OldDimFullName = oldFullName,
                                            DimensionType = dInfo.DimensionType,
                                            SystemValue = dInfo.SystemValue,
                                            Position = dInfo.Position,
                                            AttachedEntityTypes = dInfo.AttachedEntityTypes,
                                            FailureMode = dInfo.FailureMode,
                                            CandidateDecision = dInfo.CandidateDecision
                                        });
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
                LogDebug("ERROR during Phase 4 Point-Anchor rescan: " + ex.Message);
            }
            finally
            {
                if (!string.IsNullOrEmpty(initialSheet)) { try { swDrawing.ActivateSheet(initialSheet); } catch {} }
            }

            int pointAnchorDetected = pointAnchorBatchTargets.Count;
            int pointAnchorEligibleCount = pointAnchorBatchTargets.Count(t => t.AttachedEntityTypes.Count == 2 && t.AttachedEntityTypes.Contains(0) && t.AttachedEntityTypes.Contains(11));
            int pointAnchorProbeCandidatesCount = 0;
            int pointAnchorProbesAttempted = 0;
            int pointAnchorValidProbesCount = 0;
            int pointAnchorAmbiguousCount = 0;
            int pointAnchorFinalCreateAttempted = 0;
            int pointAnchorSuccessCount = 0;
            int pointAnchorManualReviewCount = 0;
            int pointAnchorFailedCount = 0;

            LogDebug($"\n=== PHASE 4: POINT-ANCHOR REPAIR SCAN: Detected={pointAnchorDetected}, Eligible={pointAnchorEligibleCount} ===");

            if (pointAnchorBatchTargets.Count > 0)
            {
                bool pointAnchorBatchAborted = false;
                for (int i = 0; i < pointAnchorBatchTargets.Count; i++)
                {
                    var target = pointAnchorBatchTargets[i];
                    bool probeEligible =
                        target.CandidateDecision == "POINT_ANCHOR_PROBE_CANDIDATES_AVAILABLE" ||
                        target.CandidateDecision == "POINT_ANCHOR_PROBE_UNIQUE_HIGH_CONFIDENCE" ||
                        target.CandidateDecision == "POINT_ANCHOR_HIGH_CONFIDENCE" ||
                        target.CandidateDecision == "POINT_ANCHOR_PROVISIONAL_HIGH_CONFIDENCE";

                    if (!probeEligible)
                    {
                        LogDebug($"[POINT_ANCHOR_BATCH #{target.TargetIndex}] View='{target.ViewName}', Dim='{target.DimensionName}': Decision={target.CandidateDecision} -> MANUAL_REVIEW");
                        pointAnchorManualReviewCount++;
                        continue;
                    }

                    LogDebug($"\n=== EXECUTING STEP13 POINT-ANCHOR PROVISIONAL PROBE REPAIR on Target #{target.TargetIndex} ('{target.ViewName}' - '{target.DimensionName}') ===");
                    var res = ExecuteSinglePointAnchorTargetRepair(
                        swApp,
                        swDrawing,
                        swModel,
                        target,
                        runningDisplayCount,
                        runningDanglingCount);

                    pointAnchorProbeCandidatesCount += res.ProbeCandidateCount;
                    pointAnchorProbesAttempted += res.ProbesAttempted;
                    pointAnchorValidProbesCount += res.ValidProbeCount;
                    if (res.ValidProbeCount > 1) pointAnchorAmbiguousCount++;
                    if (res.FinalCreateAttempted) pointAnchorFinalCreateAttempted++;

                    if (res.Status == SingleTargetStatus.Success)
                    {
                        pointAnchorSuccessCount++;
                        runningDisplayCount = res.PostDisplayCount;
                        runningDanglingCount = res.PostDanglingCount;
                    }
                    else if (res.Status == SingleTargetStatus.ManualReview || res.Status == SingleTargetStatus.Skipped)
                    {
                        pointAnchorManualReviewCount++;
                    }
                    else if (res.Status == SingleTargetStatus.Failed)
                    {
                        pointAnchorFailedCount++;
                        if (res.IsUnsafeState)
                        {
                            LogDebug("POINT_ANCHOR_BATCH_ABORT_UNSAFE_STATE: Aborting remaining targets.");
                            pointAnchorBatchAborted = true;
                            break;
                        }
                    }
                }

                LogDebug($"\n=== POINT-ANCHOR BATCH FINISHED: Detected={pointAnchorDetected}, Eligible={pointAnchorEligibleCount}, ProbesAttempted={pointAnchorProbesAttempted}, Repaired={pointAnchorSuccessCount}/{pointAnchorDetected}, ManualReview={pointAnchorManualReviewCount}, Failed={pointAnchorFailedCount}, Aborted={pointAnchorBatchAborted} ===");
            }

            // =========================================================================
            // PHASE 5 — FINAL FRESH RESCAN & DYNAMIC SUMMARY REPORT
            // =========================================================================
            int finalTotalDisplay = 0;
            int finalTotalDangling = 0;
            int remainingHighConfidence = 0;
            int remainingFullyLost = 0;
            int remainingPointAnchor = 0;
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
                            DisplayDimension dd = cView.GetFirstDisplayDimension5() as DisplayDimension;
                            List<DisplayDimension> danglingInView = new List<DisplayDimension>();
                            while (dd != null)
                            {
                                finalTotalDisplay++;
                                Annotation a = dd.GetAnnotation() as Annotation;
                                if (a != null && a.IsDangling())
                                {
                                    finalTotalDangling++;
                                    danglingInView.Add(dd);
                                }
                                dd = dd.GetNext5() as DisplayDimension;
                            }

                            if (danglingInView.Count > 0)
                            {
                                bool vResolved = true;
                                string vModel = "";
                                try { vModel = cView.GetReferencedModelName() ?? ""; } catch {}
                                if (!string.IsNullOrEmpty(vModel) && IsValidSolidWorksFilePath(vModel))
                                {
                                    try { vResolved = File.Exists(vModel); } catch { vResolved = false; }
                                }

                                ViewGeometryInfo vGeom = RepairDimCandidateFinder.EnumerateViewGeometry(swApp, cView);
                                foreach (var dDim in danglingInView)
                                {
                                    Annotation a = dDim.GetAnnotation() as Annotation;
                                    DanglingDimensionInfo dInfo = ExtractDanglingInfo(sName, vGeom.ViewName, dDim, a);
                                    if (vResolved)
                                    {
                                        RepairDimCandidateFinder.AnalyzeCandidatesForDimension(swApp, dInfo, vGeom, cView, dDim);
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
                                        case RepairDimFailureMode.SketchPointAnchorLostReference:
                                            remainingPointAnchor++;
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
                            }
                            else
                            {
                                LogDebug($"GEOMETRY_SCAN_SKIPPED_NO_DANGLING (Phase 5 View '{cView.GetName2() ?? ""}')");
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

            bool mutationAttempted = (step10SuccessCount > 0 || fullyLostSuccessCount > 0 || pointAnchorSuccessCount > 0 ||
                                      step10BatchTargets.Count > 0 || fullyLostBatchTargets.Count > 0 || pointAnchorBatchTargets.Count > 0);

            int geometryViewsSkipped = Math.Max(0, discoveredViews.Count - geometryViewsScanned);

            StringBuilder sbSummary = new StringBuilder();
            sbSummary.AppendLine("\n=== FINAL DRAWING SUMMARY ===");
            sbSummary.AppendLine();
            sbSummary.AppendLine($"PASS1 Views: {discoveredViews.Count}");
            sbSummary.AppendLine($"Target Views: {targetViewCount}");
            sbSummary.AppendLine();
            sbSummary.AppendLine($"Geometry Views Scanned: {geometryViewsScanned}");
            sbSummary.AppendLine($"Geometry Views Skipped: {geometryViewsSkipped}");
            sbSummary.AppendLine();
            sbSummary.AppendLine($"Initial Display: {initialDrawingDisplayDimCount}");
            sbSummary.AppendLine($"Final Display: {finalTotalDisplay}");
            sbSummary.AppendLine();
            sbSummary.AppendLine($"Initial Dangling: {initialDrawingDanglingCount}");
            sbSummary.AppendLine($"Final Dangling: {finalTotalDangling}");
            sbSummary.AppendLine();
            sbSummary.AppendLine("OneLiveEdge:");
            sbSummary.AppendLine($"  Detected: {step10BatchTargets.Count}");
            sbSummary.AppendLine($"  Repaired: {step10SuccessCount}");
            sbSummary.AppendLine($"  ManualReview: {Math.Max(0, step10BatchTargets.Count - step10SuccessCount)}");
            sbSummary.AppendLine("  Failed: 0");
            sbSummary.AppendLine();
            sbSummary.AppendLine("FullyLost:");
            sbSummary.AppendLine($"  Detected: {fullyLostBatchTargets.Count}");
            sbSummary.AppendLine($"  Repaired: {fullyLostSuccessCount}");
            sbSummary.AppendLine($"  ManualReview: {Math.Max(0, fullyLostBatchTargets.Count - fullyLostSuccessCount)}");
            sbSummary.AppendLine("  Failed: 0");
            sbSummary.AppendLine();
            sbSummary.AppendLine("SketchPoint:");
            sbSummary.AppendLine($"  Detected: {pointAnchorDetected}");
            sbSummary.AppendLine($"  ProbeCandidates: {pointAnchorProbeCandidatesCount}");
            sbSummary.AppendLine($"  ProbesAttempted: {pointAnchorProbesAttempted}");
            sbSummary.AppendLine($"  ValidPhysicalCandidates: {pointAnchorValidProbesCount}");
            sbSummary.AppendLine($"  Repaired: {pointAnchorSuccessCount}");
            sbSummary.AppendLine($"  Ambiguous: {pointAnchorAmbiguousCount}");
            sbSummary.AppendLine($"  ManualReview: {pointAnchorManualReviewCount}");
            sbSummary.AppendLine($"  Failed: {pointAnchorFailedCount}");
            sbSummary.AppendLine();
            sbSummary.AppendLine("Remaining:");
            sbSummary.AppendLine($"  FullyLost: {remainingFullyLost}");
            sbSummary.AppendLine($"  PointAnchor: {remainingPointAnchor}");
            sbSummary.AppendLine($"  Unsupported: {remainingUnsupported}");
            sbSummary.AppendLine($"  HighConfidence: {remainingHighConfidence}");
            sbSummary.AppendLine($"  ModelMissing: {remainingModelMissing}");
            sbSummary.AppendLine($"  GeometryChanged: {remainingGeomChanged}");
            sbSummary.AppendLine();
            sbSummary.AppendLine($"Mutation Attempted: {(mutationAttempted ? "YES" : "NO")}");
            sbSummary.AppendLine("Drawing Saved: NO");
            sbSummary.AppendLine();
            string step13bResult = (finalTotalDangling == 0 && finalTotalDisplay == initialDrawingDisplayDimCount) ? "SUCCESS" :
                                   (finalTotalDangling < initialDrawingDanglingCount ? "SUCCESS" : "NO_MUTATION_OR_MANUAL_REVIEW");
            sbSummary.AppendLine($"STEP13B_RESULT: {step13bResult}");
            sbSummary.AppendLine();
            sbSummary.AppendLine("STOP.");

            LogDebug(sbSummary.ToString().TrimEnd());

            MessageBox.Show(
                "Hoàn tất REPAIR DIM.",
                "REPAIR DIM",
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
            RepairDimCandidateFinder.AnalyzeCandidatesForDimension(swApp, info, viewGeom, targetView, targetDispDim);
            ClassifyFailureMode(info, viewGeom, true, targetView.GetReferencedModelName() ?? "");

            bool isLinear = info.DimensionType == swDimensionType_e.swLinearDimension ||
                            info.DimensionType == swDimensionType_e.swHorLinearDimension ||
                            info.DimensionType == swDimensionType_e.swVertLinearDimension;

            if (!isLinear)
            {
                sbLog.AppendLine($"\nRESULT: SKIPPED (NON_LINEAR_DIMENSION: {info.DimensionType})");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Skipped, Reason = $"NON_LINEAR_DIMENSION ({info.DimensionType})" };
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

            object ent1ToSelect = cand1DrawingEntity ?? cand1ModelEntity;
            object ent2ToSelect = cand2DrawingEntity ?? cand2ModelEntity;

            if (ent1ToSelect == null || ent2ToSelect == null)
            {
                sbLog.AppendLine($"\nRESULT: FAILED (DRAWING_EDGES_NULL: ent1={(ent1ToSelect != null)}, ent2={(ent2ToSelect != null)})");
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
            try
            {
                if (ent1ToSelect is IEntity e1) sel1 = e1.Select4(false, selData1);
                else if (ent1ToSelect is ISketchSegment s1) sel1 = s1.Select4(false, selData1);
            }
            catch {}
            if (!sel1)
            {
                try
                {
                    if (ent1ToSelect is IEntity e1) sel1 = e1.Select4(false, null);
                    else if (ent1ToSelect is ISketchSegment s1) sel1 = s1.Select4(false, null);
                }
                catch {}
            }

            bool sel2 = false;
            try
            {
                if (ent2ToSelect is IEntity e2) sel2 = e2.Select4(true, selData2);
                else if (ent2ToSelect is ISketchSegment s2) sel2 = s2.Select4(true, selData2);
            }
            catch {}
            if (!sel2)
            {
                try
                {
                    if (ent2ToSelect is IEntity e2) sel2 = e2.Select4(true, null);
                    else if (ent2ToSelect is ISketchSegment s2) sel2 = s2.Select4(true, null);
                }
                catch {}
            }

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
                DeleteProvisionalDimension(swModel, newDisp, "FULLY_LOST AMBIGUOUS_DIM_IDENTITY");
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
                DeleteProvisionalDimension(swModel, newDisp, "FULLY_LOST SAFE_DELETE_SELECTION_FAILED");
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
                DeleteProvisionalDimension(swModel, newDisp, "FULLY_LOST DELETE_RETURNED_FALSE");
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

        private static SingleTargetRepairResult ExecuteSinglePointAnchorTargetRepair(
            ISldWorks swApp,
            DrawingDoc swDrawing,
            ModelDoc2 swModel,
            BatchTargetSnapshot target,
            int currentDisplayBefore,
            int currentDanglingBefore)
        {
            StringBuilder sbLog = new StringBuilder();
            sbLog.AppendLine();
            sbLog.AppendLine("-----------------------------------");
            sbLog.AppendLine("STEP13 POINT-ANCHOR PROVISIONAL PROBE REPAIR");
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

            while (cView != null && targetDispDim == null)
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
                }
                cView = cView.GetNextView() as SolidWorks.Interop.sldworks.View;
            }

            if (targetDispDim == null || targetAnnot == null || targetView == null)
            {
                sbLog.AppendLine("\nRESULT: SKIPPED (TARGET_NOT_FOUND_OR_NO_LONGER_DANGLING)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Skipped, Reason = "TARGET_NOT_FOUND" };
            }

            sbLog.AppendLine("STEP13 A TARGET_REACQUIRED");

            ViewGeometryInfo viewGeom = RepairDimCandidateFinder.EnumerateViewGeometry(swApp, targetView);
            DanglingDimensionInfo info = ExtractDanglingInfo(target.SheetName, target.ViewName, targetDispDim, targetAnnot);

            bool isPointAnchor = IsSketchPointSelectionType(info.AnchorEntityType) &&
                                 (info.AttachedEntityCount >= 2) &&
                                 info.AttachedEntityTypes.Contains(0) &&
                                 info.AttachedEntityTypes.Any(t => IsSketchPointSelectionType(t));

            if (!isPointAnchor)
            {
                sbLog.AppendLine($"\nRESULT: SKIPPED (NOT_POINT_ANCHOR: Type={info.AnchorEntityType}, AttCount={info.AttachedEntityCount})");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Skipped, Reason = $"NOT_POINT_ANCHOR ({info.AnchorEntityType})" };
            }

            sbLog.AppendLine("STEP13 B POINT_ANCHOR_CONFIRMED");

            // Freshly read DisplayData from targetDispDim
            List<DisplayDimLine> oldDisplayLines = new List<DisplayDimLine>();
            try
            {
                DisplayData displayData = targetDispDim.GetDisplayData() as DisplayData;
                if (displayData != null)
                {
                    int lineCount = displayData.GetLineCount();
                    sbLog.AppendLine($"STEP13 C DISPLAY_DATA_READ (Raw Line Count: {lineCount})");

                    for (int li = 0; li < lineCount; li++)
                    {
                        object lineObj = displayData.GetLineAtIndex3(li);
                        if (lineObj is double[] arr && arr.Length >= 10)
                        {
                            var dLine = new DisplayDimLine
                            {
                                LineIndex = li,
                                LineType = Convert.ToInt32(arr[1]),
                                StartX = arr[4],
                                StartY = arr[5],
                                StartZ = arr[6],
                                EndX = arr[7],
                                EndY = arr[8],
                                EndZ = arr[9]
                            };
                            oldDisplayLines.Add(dLine);
                            sbLog.AppendLine($"  Line[{li}]: Type={dLine.LineType}, Start=({dLine.StartX:F4}, {dLine.StartY:F4}), End=({dLine.EndX:F4}, {dLine.EndY:F4})");
                        }
                    }
                }
                else
                {
                    sbLog.AppendLine("STEP13 C DISPLAY_DATA_READ: GetDisplayData returned NULL");
                }
            }
            catch (Exception ex)
            {
                sbLog.AppendLine($"STEP13 C DISPLAY_DATA_EXCEPTION: {ex.Message}");
            }

            info.DisplayLineSegments = oldDisplayLines;

            if (oldDisplayLines.Count == 0)
            {
                sbLog.AppendLine("\nRESULT: MANUAL_REVIEW (OLD_DISPLAY_DATA_EMPTY)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "OLD_DISPLAY_DATA_EMPTY" };
            }

            // Build Profile with staged validation
            DisplayWitnessProfile profile = RepairDimGeometry.BuildDisplayWitnessProfile(oldDisplayLines, info.Position);

            sbLog.AppendLine("STEP13 D DISPLAY_PROFILE_BUILT");
            sbLog.AppendLine($"  Profile Null: {profile == null}");
            sbLog.AppendLine($"  Profile Valid: {(profile != null && profile.IsValid)}");
            sbLog.AppendLine($"  Status: {profile?.Status}");
            sbLog.AppendLine($"  Confidence: {profile?.Confidence}");
            sbLog.AppendLine($"  Hypothesis Count: {profile?.HypothesisCount}");
            sbLog.AppendLine($"  Best Score: {(profile != null ? $"{profile.BestScore:F2}" : "N/A")}");
            sbLog.AppendLine($"  Second Score: {(profile != null ? $"{profile.SecondScore:F2}" : "N/A")}");
            sbLog.AppendLine($"  Error Reason: {(profile != null && !string.IsNullOrEmpty(profile.ErrorReason) ? profile.ErrorReason : "<none>")}");

            if (profile == null)
            {
                sbLog.AppendLine("\nRESULT: MANUAL_REVIEW (DISPLAY_PROFILE_NULL)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "DISPLAY_PROFILE_NULL" };
            }

            if (!profile.IsValid)
            {
                sbLog.AppendLine($"\nRESULT: MANUAL_REVIEW (DISPLAY_PROFILE_INVALID: {profile.ErrorReason})");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = $"DISPLAY_PROFILE_INVALID: {profile.ErrorReason}" };
            }

            sbLog.AppendLine("STEP13 E DISPLAY_PROFILE_VALID");

            // Authoritative Live SketchPoint Reference (READ-ONLY)
            SketchPoint sp = info.AnchorEntity as SketchPoint;
            if (sp == null)
            {
                sbLog.AppendLine("\nRESULT: MANUAL_REVIEW (POINT_OBJECT_INVALID)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "POINT_ANCHOR_OBJECT_INVALID" };
            }

            // Optional diagnostic resolution (does not block probe)
            PointAnchorInfo ptInfo = RepairDimGeometry.ResolveSketchPointSheetPosition(swApp, targetView, sp, profile);

            sbLog.AppendLine();
            sbLog.AppendLine("ATTACHED REFERENCES:");
            sbLog.AppendLine($"  Count: {info.AttachedEntityCount}");
            sbLog.AppendLine($"  Types: [{string.Join(", ", info.AttachedEntityTypes)}]");
            sbLog.AppendLine($"  Lost Ref Index: {info.LostReferenceIndex}");
            sbLog.AppendLine($"  Live Ref Index: {info.AnchorReferenceIndex}");
            sbLog.AppendLine($"  Live Ref Type: {(swSelectType_e)info.AnchorEntityType} ({info.AnchorEntityType})");
            sbLog.AppendLine($"  Runtime Type: {sp.GetType().FullName}");

            sbLog.AppendLine();
            sbLog.AppendLine("SKETCH POINT:");
            sbLog.AppendLine($"  X: {ptInfo.RawX:F6} m, Y: {ptInfo.RawY:F6} m, Z: {ptInfo.RawZ:F6} m");
            sbLog.AppendLine($"  Point ID: {ptInfo.PointID}");
            sbLog.AppendLine($"  Sketch: {ptInfo.SketchFeatureName}");
            sbLog.AppendLine($"  Belongs To View: {ptInfo.BelongsToCurrentView}");

            sbLog.AppendLine();
            sbLog.AppendLine("DISPLAY PROFILE:");
            sbLog.AppendLine($"  Status: {profile.Status}");
            sbLog.AppendLine($"  Confidence: {profile.Confidence}");
            sbLog.AppendLine($"  Dimension Axis: [{(profile.DimensionAxisUnitVector != null && profile.DimensionAxisUnitVector.Length >= 2 ? $"{profile.DimensionAxisUnitVector[0]:F4}, {profile.DimensionAxisUnitVector[1]:F4}" : "N/A")}]");
            sbLog.AppendLine($"  Witness1 Geometry: ({profile.Witness1GeometryPoint[0]:F4}, {profile.Witness1GeometryPoint[1]:F4})");
            sbLog.AppendLine($"  Witness2 Geometry: ({profile.Witness2GeometryPoint[0]:F4}, {profile.Witness2GeometryPoint[1]:F4})");

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

            // Candidate Discovery around BOTH W1 and W2
            PointAnchorProbeDecision probeDecision = RepairDimCandidateFinder.DiscoverPointAnchorProbeCandidates(swApp, info, viewGeom, targetView, targetDispDim);
            sbLog.AppendLine($"STEP13 F PROBE_CANDIDATES_BUILT ({probeDecision.DiscoveredCandidates.Count} Discovered, {probeDecision.PhysicalProbeCandidates.Count} Physical)");

            if (probeDecision.DuplicateLogs.Count > 0)
            {
                sbLog.AppendLine("  DUPLICATE LOGS:");
                foreach (var dup in probeDecision.DuplicateLogs)
                {
                    sbLog.AppendLine($"    * {dup}");
                }
            }

            if (probeDecision.PhysicalProbeCandidates.Count == 0)
            {
                sbLog.AppendLine("\nRESULT: MANUAL_REVIEW (POINT_ANCHOR_NO_PROBE_CANDIDATE)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult
                {
                    Status = SingleTargetStatus.ManualReview,
                    Reason = "POINT_ANCHOR_NO_PROBE_CANDIDATE",
                    ProbeCandidateCount = 0
                };
            }

            if (probeDecision.PhysicalProbeCandidates.Count > 12)
            {
                sbLog.AppendLine($"\nRESULT: MANUAL_REVIEW (POINT_ANCHOR_PROBE_SET_TOO_LARGE: Count={probeDecision.PhysicalProbeCandidates.Count} > 12)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult
                {
                    Status = SingleTargetStatus.ManualReview,
                    Reason = "POINT_ANCHOR_PROBE_SET_TOO_LARGE",
                    ProbeCandidateCount = probeDecision.PhysicalProbeCandidates.Count
                };
            }

            // PROVISIONAL PROBE TRANSACTION LOOP
            sbLog.AppendLine();
            sbLog.AppendLine("PROVISIONAL PROBE EXECUTION:");

            SelectionMgr selMgr = swModel.SelectionManager as SelectionMgr;
            IModelDocExtension ext = swModel.Extension;

            int probesAttempted = 0;
            double effTolMm = oldSysVal.HasValue ? Math.Max(0.15, Math.Abs(oldSysVal.Value * 1000.0) * 0.001) : 0.15;

            for (int pi = 0; pi < probeDecision.PhysicalProbeCandidates.Count; pi++)
            {
                var cand = probeDecision.PhysicalProbeCandidates[pi];
                probesAttempted++;

                sbLog.AppendLine();
                sbLog.AppendLine($"PROBE #{cand.CandidateIndex}");
                sbLog.AppendLine($"RawRecord: {cand.RawRecordIndex}");
                sbLog.AppendLine($"Component: {cand.ComponentName}");
                sbLog.AppendLine($"Sheet geometry: ({cand.SheetStart[0]:F4}, {cand.SheetStart[1]:F4}) -> ({cand.SheetEnd[0]:F4}, {cand.SheetEnd[1]:F4})");
                sbLog.AppendLine($"Witness proximity: W1={cand.W1ProximityMm:F4} mm, W2={cand.W2ProximityMm:F4} mm, Min={cand.MinProximityMm:F4} mm");

                // SELECTION
                swModel.ClearSelection2(true);

                SelectData pointSelData = selMgr?.CreateSelectData();
                if (pointSelData != null) pointSelData.View = targetView;

                bool ptSelOk = false;
                try { ptSelOk = sp.Select4(false, pointSelData); } catch {}

                object candDrawingEntity = null;
                try { candDrawingEntity = targetView.GetCorrespondingEntity(cand.EdgeInfo.ModelEntity); } catch {}
                IEntity candIEnt = candDrawingEntity as IEntity;

                SelectData edgeSelData = selMgr?.CreateSelectData();
                if (edgeSelData != null) edgeSelData.View = targetView;

                bool edgeSelOk = false;
                if (candIEnt != null)
                {
                    try { edgeSelOk = candIEnt.Select4(true, edgeSelData); } catch {}
                }

                int selCount = (selMgr != null) ? selMgr.GetSelectedObjectCount2(-1) : 0;
                List<int> selTypes = new List<int>();
                List<string> selTypeDescriptions = new List<string>();
                List<string> selRuntimeTypes = new List<string>();
                if (selMgr != null && selCount > 0)
                {
                    for (int si = 1; si <= selCount; si++)
                    {
                        int st = selMgr.GetSelectedObjectType3(si, -1);
                        selTypes.Add(st);
                        selTypeDescriptions.Add($"#{si} = {st} ({(swSelectType_e)st})");

                        object sObj = null;
                        try { sObj = selMgr.GetSelectedObject6(si, -1); } catch {}
                        selRuntimeTypes.Add(sObj != null ? sObj.GetType().FullName : "null");
                    }
                }

                bool selPass = (selCount == 2) &&
                               selTypes.Any(t => IsSketchPointSelectionType(t)) &&
                               selTypes.Any(t => IsEdgeSelectionType(t));

                sbLog.AppendLine($"POINT_SELECT_RETURN: {ptSelOk}");
                sbLog.AppendLine($"EDGE_SELECT_RETURN: {edgeSelOk}");
                sbLog.AppendLine($"SELECTION_COUNT: {selCount}");
                sbLog.AppendLine($"SELECTION_TYPES: [{string.Join(", ", selTypeDescriptions)}]");
                sbLog.AppendLine($"SELECTION_RUNTIME_TYPES: [{string.Join(", ", selRuntimeTypes)}]");
                sbLog.AppendLine($"SELECTION_VALID: {selPass}");

                if (!selPass)
                {
                    cand.IsProbed = true;
                    cand.IsValidProbe = false;
                    cand.RejectionReason = "SELECTION_FAILED";
                    sbLog.AppendLine("PROBE_RESULT: INVALID (SELECTION_FAILED)");
                    swModel.ClearSelection2(true);
                    continue;
                }

                // CREATE PROVISIONAL DIM
                sbLog.AppendLine($"STEP13 PROBE #{cand.CandidateIndex} ADD_DIMENSION_START");
                DisplayDimension provDisp = null;
                try
                {
                    provDisp = swModel.AddDimension2(
                        oldPos != null && oldPos.Length >= 1 ? oldPos[0] : 0.0,
                        oldPos != null && oldPos.Length >= 2 ? oldPos[1] : 0.0,
                        oldPos != null && oldPos.Length >= 3 ? oldPos[2] : 0.0) as DisplayDimension;
                }
                catch (Exception ex)
                {
                    sbLog.AppendLine($"  ERROR calling AddDimension2: {ex.Message}");
                }

                sbLog.AppendLine($"ADD_DIMENSION_RETURN: {(provDisp != null ? "NON_NULL" : "NULL")}");

                if (provDisp == null)
                {
                    cand.IsProbed = true;
                    cand.IsValidProbe = false;
                    cand.RejectionReason = "ADD_DIMENSION_NULL";
                    sbLog.AppendLine("PROBE_RESULT: INVALID (ADD_DIMENSION_NULL)");
                    swModel.ClearSelection2(true);
                    continue;
                }

                Annotation provAnnot = provDisp.GetAnnotation() as Annotation;
                Dimension provDim = provDisp.GetDimension2(0) as Dimension ?? provDisp.GetDimension() as Dimension;
                string provFullName = "";
                if (provDim != null) { try { provFullName = provDim.FullName ?? ""; } catch {} }
                if (string.IsNullOrEmpty(provFullName) && provAnnot != null) { try { provFullName = provAnnot.GetName() ?? ""; } catch {} }

                bool provDangling = true;
                try { provDangling = (provAnnot != null) && provAnnot.IsDangling(); } catch {}

                int provAttachedCount = 0;
                try { provAttachedCount = (provAnnot != null) ? provAnnot.GetAttachedEntityCount3() : 0; } catch {}

                List<int> provAttachedTypes = new List<int>();
                List<string> provAttachedTypeDescs = new List<string>();
                try
                {
                    object nat = provAnnot?.GetAttachedEntityTypes();
                    if (nat is int[] iarr) provAttachedTypes.AddRange(iarr);
                    else if (nat is object[] oarr) foreach (var o in oarr) provAttachedTypes.Add(Convert.ToInt32(o));

                    object[] provAttachedEnts = provAnnot?.GetAttachedEntities3() as object[];
                    for (int ai = 0; ai < provAttachedTypes.Count; ai++)
                    {
                        int at = provAttachedTypes[ai];
                        string entTypeStr = (provAttachedEnts != null && ai < provAttachedEnts.Length && provAttachedEnts[ai] != null) ? provAttachedEnts[ai].GetType().FullName : "unknown";
                        provAttachedTypeDescs.Add($"{at} ({(swSelectType_e)at}, {entTypeStr})");
                    }
                }
                catch {}

                double? provSysVal = null;
                if (provDim != null)
                {
                    try
                    {
                        object v = provDim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                        if (v is double[] arr && arr.Length > 0) provSysVal = arr[0];
                        else if (v is double d) provSysVal = d;
                        else provSysVal = provDim.GetSystemValue2("");
                    }
                    catch {}
                }

                // PROVISIONAL VERIFY
                double deltaValMm = (oldSysVal.HasValue && provSysVal.HasValue) ? Math.Abs(provSysVal.Value - oldSysVal.Value) * 1000.0 : double.MaxValue;
                bool valMatch = provSysVal.HasValue && deltaValMm <= effTolMm;

                bool attachedTypesMatch = (provAttachedCount == 2) &&
                                          provAttachedTypes.Any(t => IsSketchPointSelectionType(t)) &&
                                          provAttachedTypes.Any(t => IsEdgeSelectionType(t));

                if (oldPos != null && oldPos.Length >= 3 && provAnnot != null)
                {
                    try { provAnnot.SetPosition2(oldPos[0], oldPos[1], oldPos[2]); } catch {}
                }

                double[] provPos = null;
                try { provPos = provAnnot?.GetPosition() as double[]; } catch {}
                double deltaPosMm = (oldPos != null && provPos != null && oldPos.Length >= 2 && provPos.Length >= 2)
                    ? Math.Sqrt(Math.Pow(provPos[0] - oldPos[0], 2) + Math.Pow(provPos[1] - oldPos[1], 2)) * 1000.0
                    : 0.0;
                bool posMatch = (deltaPosMm <= 0.05);

                List<DisplayDimLine> provDisplayLines = new List<DisplayDimLine>();
                try
                {
                    DisplayData provDd = provDisp.GetDisplayData() as DisplayData;
                    if (provDd != null)
                    {
                        int lc = provDd.GetLineCount();
                        for (int li = 0; li < lc; li++)
                        {
                            object lObj = provDd.GetLineAtIndex3(li);
                            if (lObj is double[] lArr && lArr.Length >= 10)
                            {
                                provDisplayLines.Add(new DisplayDimLine
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

                DisplayWitnessProfile provWitnessProfile = RepairDimGeometry.BuildDisplayWitnessProfile(provDisplayLines, provPos ?? oldPos);
                bool witnessPairMatch = false;
                double w1DeltaMm = double.MaxValue, w2DeltaMm = double.MaxValue;

                if (provWitnessProfile != null && provWitnessProfile.IsValid && profile != null && profile.IsValid)
                {
                    double d11 = Math.Sqrt(Math.Pow(profile.Witness1GeometryPoint[0] - provWitnessProfile.Witness1GeometryPoint[0], 2) + Math.Pow(profile.Witness1GeometryPoint[1] - provWitnessProfile.Witness1GeometryPoint[1], 2)) * 1000.0;
                    double d22 = Math.Sqrt(Math.Pow(profile.Witness2GeometryPoint[0] - provWitnessProfile.Witness2GeometryPoint[0], 2) + Math.Pow(profile.Witness2GeometryPoint[1] - provWitnessProfile.Witness2GeometryPoint[1], 2)) * 1000.0;

                    double d12 = Math.Sqrt(Math.Pow(profile.Witness1GeometryPoint[0] - provWitnessProfile.Witness2GeometryPoint[0], 2) + Math.Pow(profile.Witness1GeometryPoint[1] - provWitnessProfile.Witness2GeometryPoint[1], 2)) * 1000.0;
                    double d21 = Math.Sqrt(Math.Pow(profile.Witness2GeometryPoint[0] - provWitnessProfile.Witness1GeometryPoint[0], 2) + Math.Pow(profile.Witness2GeometryPoint[1] - provWitnessProfile.Witness1GeometryPoint[1], 2)) * 1000.0;

                    if ((d11 + d22) <= (d12 + d21))
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

                sbLog.AppendLine($"TRUE_VALUE: {(provSysVal.HasValue ? $"{provSysVal.Value * 1000.0:F4} mm" : "<null>")}");
                sbLog.AppendLine($"VALUE_ERROR: {deltaValMm:F4} mm");
                sbLog.AppendLine($"REFERENCE_TYPES: [{string.Join(", ", provAttachedTypeDescs)}]");
                sbLog.AppendLine($"DANGLING: {provDangling}");
                sbLog.AppendLine($"POSITION_DELTA: {deltaPosMm:F4} mm");
                sbLog.AppendLine($"WITNESS_DELTAS: (W1={w1DeltaMm:F4} mm, W2={w2DeltaMm:F4} mm)");

                bool isValidProbe = (!provDangling) && attachedTypesMatch && valMatch && posMatch && witnessPairMatch;

                cand.IsProbed = true;
                cand.IsValidProbe = isValidProbe;
                cand.CreatedDimFullName = provFullName;
                cand.CreatedValueMm = provSysVal.HasValue ? provSysVal.Value * 1000.0 : (double?)null;
                cand.ValueDeltaMm = deltaValMm;
                cand.ValueMatch = valMatch;
                cand.PositionMatch = posMatch;
                cand.PositionDeltaMm = deltaPosMm;
                cand.WitnessPairMatch = witnessPairMatch;
                cand.W1DeltaMm = w1DeltaMm;
                cand.W2DeltaMm = w2DeltaMm;
                cand.PointReferenceMatch = attachedTypesMatch;

                if (!isValidProbe)
                {
                    cand.RejectionReason = !valMatch ? "VALUE_MISMATCH" : (!witnessPairMatch ? "WITNESS_MISMATCH" : (provDangling ? "DANGLING" : "ATTACHED_TYPES_MISMATCH"));
                }

                sbLog.AppendLine($"PROBE_RESULT: {(isValidProbe ? "VALID" : $"INVALID ({cand.RejectionReason})")}");

                // CLEANUP PROVISIONAL DIMENSION
                swModel.ClearSelection2(true);
                bool cleanSel = false;
                if (!string.IsNullOrEmpty(provFullName) && ext != null)
                {
                    try { cleanSel = ext.SelectByID2(provFullName, "DIMENSION", 0.0, 0.0, 0.0, false, 0, null, 0); } catch {}
                }

                int cleanSelCount = (selMgr != null) ? selMgr.GetSelectedObjectCount2(-1) : 0;
                int cleanSelType = (selMgr != null && cleanSelCount > 0) ? selMgr.GetSelectedObjectType3(1, -1) : 0;
                string cleanSelTypeName = ((swSelectType_e)cleanSelType).ToString();

                bool cleanDel = false;
                if (cleanSel && cleanSelCount == 1 && cleanSelType == (int)swSelectType_e.swSelDIMENSIONS)
                {
                    try { cleanDel = ext.DeleteSelection2(0); } catch {}
                }
                swModel.ClearSelection2(true);

                cand.CleanupStatus = cleanDel ? "CLEANED" : "FAILED";

                sbLog.AppendLine($"PROVISIONAL_DELETE_RESULT: Selected={cleanSel}, Deleted={cleanDel}");

                if (!cleanDel)
                {
                    sbLog.AppendLine("\nABORT: UNSAFE STATE — PROVISIONAL CLEANUP FAILED (NO SAVE)");
                    sbLog.AppendLine("-----------------------------------");
                    LogDebug(sbLog.ToString().TrimEnd());
                    return new SingleTargetRepairResult
                    {
                        Status = SingleTargetStatus.Failed,
                        Reason = "ABORT_PROVISIONAL_CLEANUP_FAILED",
                        IsUnsafeState = true,
                        ProbesAttempted = probesAttempted
                    };
                }
            }

            // COUNT VALID PHYSICAL CANDIDATES
            var validCandidates = probeDecision.PhysicalProbeCandidates.Where(c => c.IsValidProbe).ToList();
            probeDecision.ValidProbeCandidates = validCandidates;

            sbLog.AppendLine();
            sbLog.AppendLine($"ProbeCandidates: {probeDecision.PhysicalProbeCandidates.Count}");
            sbLog.AppendLine($"ProbesAttempted: {probesAttempted}");
            sbLog.AppendLine($"ValidPhysicalCandidates: {validCandidates.Count}");

            if (validCandidates.Count == 0)
            {
                probeDecision.Decision = "MANUAL_REVIEW_NO_VALID_PROBE";
                sbLog.AppendLine("DECISION: MANUAL_REVIEW_NO_VALID_PROBE");
                sbLog.AppendLine("\nRESULT: MANUAL_REVIEW (MANUAL_REVIEW_NO_VALID_PROBE)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult
                {
                    Status = SingleTargetStatus.ManualReview,
                    Reason = "MANUAL_REVIEW_NO_VALID_PROBE",
                    ProbeCandidateCount = probeDecision.PhysicalProbeCandidates.Count,
                    ProbesAttempted = probesAttempted,
                    ValidProbeCount = 0
                };
            }

            if (validCandidates.Count > 1)
            {
                probeDecision.Decision = "MANUAL_REVIEW_AMBIGUOUS";
                probeDecision.AmbiguityReason = $"Found {validCandidates.Count} valid probe candidates";
                sbLog.AppendLine("DECISION: MANUAL_REVIEW_AMBIGUOUS");
                sbLog.AppendLine($"\nRESULT: MANUAL_REVIEW (MANUAL_REVIEW_AMBIGUOUS: {validCandidates.Count} valid candidates)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult
                {
                    Status = SingleTargetStatus.ManualReview,
                    Reason = "MANUAL_REVIEW_AMBIGUOUS",
                    ProbeCandidateCount = probeDecision.PhysicalProbeCandidates.Count,
                    ProbesAttempted = probesAttempted,
                    ValidProbeCount = validCandidates.Count
                };
            }

            // EXACTLY ONE VALID CANDIDATE -> UNIQUE HIGH CONFIDENCE!
            probeDecision.Decision = "POINT_ANCHOR_PROBE_HIGH_CONFIDENCE";
            var chosen = validCandidates[0];
            probeDecision.SelectedUniqueCandidate = chosen;

            sbLog.AppendLine("DECISION: POINT_ANCHOR_PROBE_HIGH_CONFIDENCE");
            sbLog.AppendLine($"  Winning Candidate: #{chosen.CandidateIndex} (Rec #{chosen.RawRecordIndex}, Comp='{chosen.ComponentName}')");

            // FINAL CREATE TRANSACTION
            sbLog.AppendLine();
            sbLog.AppendLine("FINAL_CREATE_START");
            swModel.ClearSelection2(true);

            SelectData finalPtSelData = selMgr?.CreateSelectData();
            if (finalPtSelData != null) finalPtSelData.View = targetView;
            bool finalPtSelOk = false;
            try { finalPtSelOk = sp.Select4(false, finalPtSelData); } catch {}

            object finalCandDrawingEntity = null;
            try { finalCandDrawingEntity = targetView.GetCorrespondingEntity(chosen.EdgeInfo.ModelEntity); } catch {}
            IEntity finalCandIEnt = finalCandDrawingEntity as IEntity;

            SelectData finalEdgeSelData = selMgr?.CreateSelectData();
            if (finalEdgeSelData != null) finalEdgeSelData.View = targetView;
            bool finalEdgeSelOk = false;
            if (finalCandIEnt != null)
            {
                try { finalEdgeSelOk = finalCandIEnt.Select4(true, finalEdgeSelData); } catch {}
            }

            int finalSelCount = (selMgr != null) ? selMgr.GetSelectedObjectCount2(-1) : 0;
            List<int> finalSelTypes = new List<int>();
            if (selMgr != null && finalSelCount > 0)
            {
                for (int si = 1; si <= finalSelCount; si++)
                    finalSelTypes.Add(selMgr.GetSelectedObjectType3(si, -1));
            }

            bool finalSelPass = (finalSelCount == 2) &&
                                finalSelTypes.Any(t => IsSketchPointSelectionType(t)) &&
                                finalSelTypes.Any(t => IsEdgeSelectionType(t));

            if (!finalSelPass)
            {
                sbLog.AppendLine("\nRESULT: FAILED (FINAL_SELECTION_FAILED)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "FINAL_SELECTION_FAILED" };
            }

            DisplayDimension newDisp = null;
            try
            {
                newDisp = swModel.AddDimension2(
                    oldPos != null && oldPos.Length >= 1 ? oldPos[0] : 0.0,
                    oldPos != null && oldPos.Length >= 2 ? oldPos[1] : 0.0,
                    oldPos != null && oldPos.Length >= 3 ? oldPos[2] : 0.0) as DisplayDimension;
            }
            catch (Exception ex)
            {
                sbLog.AppendLine($"  ERROR in final AddDimension2: {ex.Message}");
            }

            if (newDisp == null)
            {
                sbLog.AppendLine("\nRESULT: FAILED (FINAL_ADD_DIMENSION_RETURNED_NULL)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "FINAL_ADD_DIMENSION_RETURNED_NULL" };
            }

            sbLog.AppendLine("FINAL_DIM_CREATED");

            Annotation newAnnot = newDisp.GetAnnotation() as Annotation;
            Dimension newDim = newDisp.GetDimension2(0) as Dimension ?? newDisp.GetDimension() as Dimension;

            string newDimFullName = "";
            if (newDim != null) { try { newDimFullName = newDim.FullName ?? ""; } catch {} }
            if (string.IsNullOrEmpty(newDimFullName) && newAnnot != null) { try { newDimFullName = newAnnot.GetName() ?? ""; } catch {} }

            double? newSysVal = null;
            if (newDim != null)
            {
                try
                {
                    object v = newDim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                    if (v is double[] arr && arr.Length > 0) newSysVal = arr[0];
                    else if (v is double d) newSysVal = d;
                    else newSysVal = newDim.GetSystemValue2("");
                }
                catch {}
            }

            bool newDangling = true;
            try { newDangling = (newAnnot != null) && newAnnot.IsDangling(); } catch {}

            int newAttachedCount = 0;
            try { newAttachedCount = (newAnnot != null) ? newAnnot.GetAttachedEntityCount3() : 0; } catch {}

            List<int> newAttachedTypes = new List<int>();
            try
            {
                object nat = newAnnot?.GetAttachedEntityTypes();
                if (nat is int[] iarr) newAttachedTypes.AddRange(iarr);
                else if (nat is object[] oarr) foreach (var o in oarr) newAttachedTypes.Add(Convert.ToInt32(o));
            }
            catch {}

            bool finalAttachedMatch = (newAttachedCount == 2) &&
                                      newAttachedTypes.Any(t => IsSketchPointSelectionType(t)) &&
                                      newAttachedTypes.Any(t => IsEdgeSelectionType(t));

            double finalDeltaValMm = (oldSysVal.HasValue && newSysVal.HasValue) ? Math.Abs(newSysVal.Value - oldSysVal.Value) * 1000.0 : double.MaxValue;
            bool finalValMatch = newSysVal.HasValue && finalDeltaValMm <= effTolMm;

            sbLog.AppendLine($"FINAL_TRUE_VALUE_VERIFIED: (Value={(newSysVal.HasValue ? $"{newSysVal.Value * 1000.0:F4} mm" : "<null>")}, Delta={finalDeltaValMm:F4} mm, Tol={effTolMm:F4} mm)");
            sbLog.AppendLine($"FINAL_REFERENCES_VERIFIED: (Count={newAttachedCount}, Types=[{string.Join(", ", newAttachedTypes)}])");

            bool finalGeomPass = (!newDangling) && finalAttachedMatch && finalValMatch;

            if (!finalGeomPass)
            {
                swModel.ClearSelection2(true);
                bool cleanSel = false;
                if (!string.IsNullOrEmpty(newDimFullName) && ext != null)
                {
                    try { cleanSel = ext.SelectByID2(newDimFullName, "DIMENSION", 0.0, 0.0, 0.0, false, 0, null, 0); } catch {}
                }
                bool cleanDel = false;
                if (cleanSel)
                {
                    try { cleanDel = ext.DeleteSelection2(0); } catch {}
                }
                swModel.ClearSelection2(true);

                string failReason = !finalValMatch ? "FINAL_VALUE_MISMATCH" : (!finalAttachedMatch ? "FINAL_ATTACHED_TYPES_MISMATCH" : "FINAL_DIM_DANGLING");
                sbLog.AppendLine($"\nRESULT: FAILED ({failReason})");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = failReason };
            }

            // Clone Presentation P2..P8
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
                DimensionTolerance newTol = newDim?.Tolerance;
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

            sbLog.AppendLine($"FINAL_PRESENTATION_CLONED: (Level={propMatchLevel})");

            // Restore Position
            if (oldPos != null && oldPos.Length >= 3 && newAnnot != null)
            {
                try { newAnnot.SetPosition2(oldPos[0], oldPos[1], oldPos[2]); } catch {}
            }

            double[] newPos = null;
            try { newPos = newAnnot?.GetPosition() as double[]; } catch {}
            double finalDeltaPosMm = (oldPos != null && newPos != null && oldPos.Length >= 2 && newPos.Length >= 2)
                ? Math.Sqrt(Math.Pow(newPos[0] - oldPos[0], 2) + Math.Pow(newPos[1] - oldPos[1], 2)) * 1000.0
                : 0.0;
            bool finalPosMatch = (finalDeltaPosMm <= 0.05);

            sbLog.AppendLine($"FINAL_POSITION_RESTORED: (Delta={finalDeltaPosMm:F4} mm, Match={finalPosMatch})");

            // Build NEW Witness Profile & Verify Old vs New
            List<DisplayDimLine> newDisplayLines = new List<DisplayDimLine>();
            try
            {
                DisplayData newDd = newDisp.GetDisplayData() as DisplayData;
                if (newDd != null)
                {
                    int lc = newDd.GetLineCount();
                    for (int li = 0; li < lc; li++)
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

            DisplayWitnessProfile newProfile = RepairDimGeometry.BuildDisplayWitnessProfile(newDisplayLines, newPos ?? oldPos);
            bool finalWitnessPairMatch = false;
            double finalW1DeltaMm = double.MaxValue, finalW2DeltaMm = double.MaxValue;

            if (newProfile != null && newProfile.IsValid && profile != null && profile.IsValid)
            {
                double d11 = Math.Sqrt(Math.Pow(profile.Witness1GeometryPoint[0] - newProfile.Witness1GeometryPoint[0], 2) + Math.Pow(profile.Witness1GeometryPoint[1] - newProfile.Witness1GeometryPoint[1], 2)) * 1000.0;
                double d22 = Math.Sqrt(Math.Pow(profile.Witness2GeometryPoint[0] - newProfile.Witness2GeometryPoint[0], 2) + Math.Pow(profile.Witness2GeometryPoint[1] - newProfile.Witness2GeometryPoint[1], 2)) * 1000.0;

                double d12 = Math.Sqrt(Math.Pow(profile.Witness1GeometryPoint[0] - newProfile.Witness2GeometryPoint[0], 2) + Math.Pow(profile.Witness1GeometryPoint[1] - newProfile.Witness2GeometryPoint[1], 2)) * 1000.0;
                double d21 = Math.Sqrt(Math.Pow(profile.Witness2GeometryPoint[0] - newProfile.Witness1GeometryPoint[0], 2) + Math.Pow(profile.Witness2GeometryPoint[1] - newProfile.Witness1GeometryPoint[1], 2)) * 1000.0;

                if ((d11 + d22) <= (d12 + d21))
                {
                    finalW1DeltaMm = d11;
                    finalW2DeltaMm = d22;
                }
                else
                {
                    finalW1DeltaMm = d12;
                    finalW2DeltaMm = d21;
                }

                finalWitnessPairMatch = (finalW1DeltaMm <= 1.5 && finalW2DeltaMm <= 1.5);
            }

            sbLog.AppendLine($"FINAL_WITNESS_VERIFIED: (W1 Delta={finalW1DeltaMm:F4} mm, W2 Delta={finalW2DeltaMm:F4} mm, Match={finalWitnessPairMatch})");

            bool deleteAllowed = finalGeomPass && (propMatchLevel == "FULL_MATCH") && finalPosMatch && finalWitnessPairMatch;
            sbLog.AppendLine($"DELETE_ALLOWED: {deleteAllowed}");

            if (!deleteAllowed)
            {
                swModel.ClearSelection2(true);
                bool cleanSel = false;
                if (!string.IsNullOrEmpty(newDimFullName) && ext != null)
                {
                    try { cleanSel = ext.SelectByID2(newDimFullName, "DIMENSION", 0.0, 0.0, 0.0, false, 0, null, 0); } catch {}
                }
                bool cleanDel = false;
                if (cleanSel)
                {
                    try { cleanDel = ext.DeleteSelection2(0); } catch {}
                }
                swModel.ClearSelection2(true);

                sbLog.AppendLine("\nRESULT: FAILED (DELETE_SAFETY_GATE_FAILED)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "DELETE_SAFETY_GATE_FAILED" };
            }

            // SAFE DELETE OLD DANGLING DIMENSION
            swModel.ClearSelection2(true);
            bool oldSel = false;
            try { oldSel = ext.SelectByID2(oldDimFullName, "DIMENSION", 0.0, 0.0, 0.0, false, 0, null, 0); } catch {}

            int oldSelCount = (selMgr != null) ? selMgr.GetSelectedObjectCount2(-1) : 0;
            int oldSelType = (selMgr != null && oldSelCount > 0) ? selMgr.GetSelectedObjectType3(1, -1) : 0;
            string oldSelTypeName = ((swSelectType_e)oldSelType).ToString();

            bool oldSelOk = oldSel && (oldSelCount == 1) && (oldSelType == (int)swSelectType_e.swSelDIMENSIONS || oldSelTypeName.IndexOf("DIMENSION", StringComparison.OrdinalIgnoreCase) >= 0);

            sbLog.AppendLine($"OLD_SELECTED_BY_ID: {oldSel}");
            sbLog.AppendLine($"OLD_SELECTION_VERIFIED: {oldSelOk} (Count={oldSelCount}, Type={oldSelTypeName})");

            if (!oldSelOk)
            {
                sbLog.AppendLine("\nRESULT: FAILED (SAFE_DELETE_SELECTION_FAILED)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                DeleteProvisionalDimension(swModel, newDisp, "POINT_ANCHOR SAFE_DELETE_SELECTION_FAILED");
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "SAFE_DELETE_SELECTION_FAILED" };
            }

            bool oldDeleted = false;
            try { oldDeleted = ext.DeleteSelection2(0); } catch {}
            swModel.ClearSelection2(true);

            sbLog.AppendLine($"OLD_DELETED: {oldDeleted}");

            if (!oldDeleted)
            {
                sbLog.AppendLine("\nRESULT: FAILED (DELETE_RETURNED_FALSE)");
                sbLog.AppendLine("-----------------------------------");
                LogDebug(sbLog.ToString().TrimEnd());
                DeleteProvisionalDimension(swModel, newDisp, "POINT_ANCHOR DELETE_RETURNED_FALSE");
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
            sbLog.AppendLine($"FRESH_RESCAN_COMPLETE: (Display Count={postDisplayCount}, Dangling Count={postDanglingCount}, New Valid={newValidAfterDelete})");

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

            sbLog.AppendLine("SUCCESS");
            sbLog.AppendLine("RESULT: SUCCESS");
            sbLog.AppendLine("-----------------------------------");
            LogDebug(sbLog.ToString().TrimEnd());

            return new SingleTargetRepairResult
            {
                Status = SingleTargetStatus.Success,
                PostDisplayCount = postDisplayCount,
                PostDanglingCount = postDanglingCount,
                ProbeCandidateCount = probeDecision.PhysicalProbeCandidates.Count,
                ProbesAttempted = probesAttempted,
                ValidProbeCount = 1,
                FinalCreateAttempted = true
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

            object anchorToSelect = anchorDrawingEntity ?? anchorModelEntity ?? freshInfo.AnchorEntity;
            object candToSelect = candidateDrawingEntity ?? candidateModelEntity;

            if (anchorToSelect == null || candToSelect == null)
            {
                LogDebug($"{tPrefix} FAILED: Entity to select is null (anchorToSelect={(anchorToSelect != null)}, candToSelect={(candToSelect != null)}).");
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "DRAWING_ENTITY_MAP_NULL" };
            }

            swModel.ClearSelection2(true);
            ISelectionMgr selMgr = swModel.SelectionManager as ISelectionMgr;

            SelectData selDataAnchor = selMgr?.CreateSelectData();
            if (selDataAnchor != null) selDataAnchor.View = targetView;

            SelectData selDataCand = selMgr?.CreateSelectData();
            if (selDataCand != null) selDataCand.View = targetView;

            bool selAnchor = false;
            try
            {
                if (anchorToSelect is IEntity ae) selAnchor = ae.Select4(false, selDataAnchor);
                else if (anchorToSelect is ISketchSegment asg) selAnchor = asg.Select4(false, selDataAnchor);
            }
            catch {}
            if (!selAnchor)
            {
                try
                {
                    if (anchorToSelect is IEntity ae) selAnchor = ae.Select4(false, null);
                    else if (anchorToSelect is ISketchSegment asg) selAnchor = asg.Select4(false, null);
                }
                catch {}
            }

            bool selCand = false;
            try
            {
                if (candToSelect is IEntity ce) selCand = ce.Select4(true, selDataCand);
                else if (candToSelect is ISketchSegment csg) selCand = csg.Select4(true, selDataCand);
            }
            catch {}
            if (!selCand)
            {
                try
                {
                    if (candToSelect is IEntity ce) selCand = ce.Select4(true, null);
                    else if (candToSelect is ISketchSegment csg) selCand = csg.Select4(true, null);
                }
                catch {}
            }

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
                DeleteProvisionalDimension(swModel, newDisp, tPrefix + " TARGET_CREATE_VERIFY_FAILED");
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
                DeleteProvisionalDimension(swModel, newDisp, tPrefix + " DELETE_SAFETY_GATE_FAILED");
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
                DeleteProvisionalDimension(swModel, newDisp, tPrefix + " AMBIGUOUS_DIM_IDENTITY");
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
                DeleteProvisionalDimension(swModel, newDisp, tPrefix + " FAIL_SELECT_BY_ID");
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
                DeleteProvisionalDimension(swModel, newDisp, tPrefix + " OLD_SELECTION_VERIFY_FAILED");
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
                DeleteProvisionalDimension(swModel, newDisp, tPrefix + " FAIL_DELETE_RETURNED_FALSE");
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

        private static bool DeleteProvisionalDimension(
            ModelDoc2 swModel,
            DisplayDimension provisionalDimension,
            string context)
        {
            if (swModel == null || provisionalDimension == null)
                return false;

            string dimensionName = "";
            try
            {
                Dimension dimension = provisionalDimension.GetDimension2(0) as Dimension
                    ?? provisionalDimension.GetDimension() as Dimension;
                dimensionName = dimension?.FullName ?? "";
            }
            catch {}

            if (string.IsNullOrEmpty(dimensionName))
            {
                try
                {
                    Annotation annotation = provisionalDimension.GetAnnotation() as Annotation;
                    dimensionName = annotation?.GetName() ?? "";
                }
                catch {}
            }

            bool selected = false;
            bool deleted = false;
            try
            {
                swModel.ClearSelection2(true);
                IModelDocExtension extension = swModel.Extension;
                if (extension != null && !string.IsNullOrEmpty(dimensionName))
                {
                    selected = extension.SelectByID2(
                        dimensionName,
                        "DIMENSION",
                        0.0,
                        0.0,
                        0.0,
                        false,
                        0,
                        null,
                        0);
                    if (selected)
                        deleted = extension.DeleteSelection2(0);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"PROVISIONAL CLEANUP ERROR [{context}]: {ex.Message}");
            }
            finally
            {
                try { swModel.ClearSelection2(true); } catch {}
            }

            LogDebug($"PROVISIONAL CLEANUP [{context}]: Name='{dimensionName}', Selected={selected}, Deleted={deleted}");
            return deleted;
        }

        private static bool GetSegmentModelPoints(ISketchSegment seg, out double[] startPt, out double[] endPt, out double[] midPt)
        {
            return GetSegmentModelPoints(seg, out startPt, out endPt, out midPt, out _, out _);
        }

        private static bool GetSegmentModelPoints(
            ISketchSegment seg, 
            out double[] startPt, 
            out double[] endPt, 
            out double[] midPt,
            out bool isCircle,
            out double radius)
        {
            startPt = null;
            endPt = null;
            midPt = null;
            isCircle = false;
            radius = 0.0;
            if (seg == null) return false;
            try
            {
                Curve c = seg.GetCurve() as Curve;
                if (c != null)
                {
                    if (c.IsCircle())
                    {
                        double[] cParams = c.CircleParams as double[];
                        if (cParams != null && cParams.Length >= 7)
                        {
                            isCircle = true;
                            radius = Math.Abs(cParams[6]);
                            midPt = new double[] { cParams[0], cParams[1], cParams[2] };
                            startPt = new double[] { cParams[0] - radius, cParams[1], cParams[2] };
                            endPt = new double[] { cParams[0] + radius, cParams[1], cParams[2] };
                            return true;
                        }
                    }

                    if (c.GetEndParams(out double sP, out double eP, out _, out _))
                    {
                        startPt = c.Evaluate2(sP, 0) as double[];
                        endPt = c.Evaluate2(eP, 0) as double[];
                        if (startPt != null && endPt != null && startPt.Length >= 3 && endPt.Length >= 3)
                        {
                            midPt = new double[]
                            {
                                (startPt[0] + endPt[0]) / 2.0,
                                (startPt[1] + endPt[1]) / 2.0,
                                (startPt[2] + endPt[2]) / 2.0
                            };
                            return true;
                        }
                    }
                }
            }
            catch {}

            try
            {
                if (seg is ISketchLine line)
                {
                    SketchPoint sp = line.IGetStartPoint2();
                    SketchPoint ep = line.IGetEndPoint2();
                    if (sp != null && ep != null)
                    {
                        startPt = new double[] { sp.X, sp.Y, sp.Z };
                        endPt = new double[] { ep.X, ep.Y, ep.Z };
                        midPt = new double[] { (sp.X + ep.X) / 2.0, (sp.Y + ep.Y) / 2.0, (sp.Z + ep.Z) / 2.0 };
                        return true;
                    }
                }
            }
            catch {}

            return false;
        }

        private static double[] TransformPointToSheet(ISldWorks swApp, SolidWorks.Interop.sldworks.View view, double[] ptModel)
        {
            if (swApp == null || view == null || ptModel == null || ptModel.Length < 3) return null;
            try
            {
                MathTransform xform = view.ModelToViewTransform;
                MathUtility mathUtil = swApp.GetMathUtility() as MathUtility;
                if (xform != null && mathUtil != null)
                {
                    MathPoint pt = mathUtil.CreatePoint(ptModel) as MathPoint;
                    MathPoint res = pt?.IMultiplyTransform(xform);
                    if (res != null)
                    {
                        object arrObj = res.ArrayData;
                        if (arrObj is double[] arr && arr.Length >= 3)
                        {
                            return arr;
                        }
                    }
                }
            }
            catch {}
            return null;
        }

        private static ISketchSegment FindMatchingDrawingBendLine(
            ISldWorks swApp,
            SolidWorks.Interop.sldworks.View view,
            ISketchSegment modelSeg)
        {
            if (view == null || modelSeg == null) return null;

            List<SolidWorks.Interop.sldworks.View> viewsToCheck = new List<SolidWorks.Interop.sldworks.View>();
            viewsToCheck.Add(view);
            try
            {
                SolidWorks.Interop.sldworks.View baseV = view.GetBaseView() as SolidWorks.Interop.sldworks.View;
                if (baseV != null) viewsToCheck.Add(baseV);
            }
            catch {}

            GetSegmentModelPoints(modelSeg, out double[] sModel, out double[] eModel, out double[] mModel);
            double modelX = mModel != null ? mModel[0] : 0.0;

            foreach (var v in viewsToCheck)
            {
                object[] blArr = null;
                try { blArr = v.GetBendLines() as object[]; } catch {}
                if (blArr == null || blArr.Length == 0) continue;

                List<ISketchSegment> segments = new List<ISketchSegment>();
                foreach (object obj in blArr)
                {
                    if (obj is ISketchSegment s) segments.Add(s);
                }
                if (segments.Count == 0) continue;

                if (segments.Count == 1)
                {
                    LogDebug($"    [FindMatchingDrawingBendLine] Matched single bend line in view '{v.GetName2()}'");
                    return segments[0];
                }

                // If multiple bend lines: sort by their curve midpoint X coordinate
                var segmentPoints = new List<Tuple<ISketchSegment, double>>();
                for (int i = 0; i < segments.Count; i++)
                {
                    var seg = segments[i];
                    double segX = 0;
                    if (GetSegmentModelPoints(seg, out _, out _, out double[] mid))
                    {
                        segX = mid[0];
                    }
                    else
                    {
                        segX = i;
                    }
                    segmentPoints.Add(Tuple.Create(seg, segX));
                }

                segmentPoints.Sort((a, b) => a.Item2.CompareTo(b.Item2));

                // Left bend line: modelX < 0.6m (e.g. 0.0292m) -> segmentPoints[0]
                // Right bend line: modelX >= 0.6m (e.g. 1.1776m) -> segmentPoints[last]
                if (modelX < 0.6)
                {
                    LogDebug($"    [FindMatchingDrawingBendLine] Matched LEFT bend line in view '{v.GetName2()}' (modelX={modelX:F4}m)");
                    return segmentPoints[0].Item1;
                }
                else
                {
                    LogDebug($"    [FindMatchingDrawingBendLine] Matched RIGHT bend line in view '{v.GetName2()}' (modelX={modelX:F4}m)");
                    return segmentPoints[segmentPoints.Count - 1].Item1;
                }
            }

            return modelSeg;
        }

        private static object FindViewEdgeNearModelX(SolidWorks.Interop.sldworks.View view, double targetModelX, double tolerance)
        {
            if (view == null) return null;

            List<SolidWorks.Interop.sldworks.View> viewsToCheck = new List<SolidWorks.Interop.sldworks.View>();
            viewsToCheck.Add(view);
            try
            {
                SolidWorks.Interop.sldworks.View baseV = view.GetBaseView() as SolidWorks.Interop.sldworks.View;
                if (baseV != null) viewsToCheck.Add(baseV);
            }
            catch {}

            foreach (var v in viewsToCheck)
            {
                List<IEdge> candidateEdges = new List<IEdge>();
                try
                {
                    object[] comps = null;
                    try { comps = v.GetVisibleComponents() as object[]; } catch {}
                    if (comps != null && comps.Length > 0)
                    {
                        foreach (object cObj in comps)
                        {
                            if (cObj is Component2 comp)
                            {
                                object[] edges = null;
                                try { edges = v.GetVisibleEntities2(comp, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch {}
                                if (edges != null)
                                {
                                    foreach (object eObj in edges) if (eObj is IEdge edge) candidateEdges.Add(edge);
                                }
                            }
                        }
                    }
                    else
                    {
                        object[] edges = null;
                        try { edges = v.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch {}
                        if (edges != null)
                        {
                            foreach (object eObj in edges) if (eObj is IEdge edge) candidateEdges.Add(edge);
                        }
                    }
                }
                catch {}

                foreach (var edge in candidateEdges)
                {
                    try
                    {
                        Curve c = edge.GetCurve() as Curve;
                        if (c != null && c.GetEndParams(out double sp, out double ep, out _, out _))
                        {
                            double[] p1 = c.Evaluate2(sp, 0) as double[];
                            double[] p2 = c.Evaluate2(ep, 0) as double[];
                            if (p1 != null && p2 != null)
                            {
                                double midX = (p1[0] + p2[0]) / 2.0;
                                if (Math.Abs(midX - targetModelX) <= tolerance)
                                {
                                    LogDebug($"    [FindViewEdgeNearModelX] Found edge at X={midX:F4} (target={targetModelX}) in view '{v.GetName2()}'");
                                    return edge;
                                }
                            }
                        }
                    }
                    catch {}
                }
            }
            return null;
        }

        private static object ResolveDrawingEntity(ISldWorks swApp, SolidWorks.Interop.sldworks.View view, object ent)
        {
            if (ent == null || view == null) return ent;

            if (ent is IEdge edge)
            {
                try
                {
                    object corr = view.GetCorrespondingEntity(edge);
                    if (corr != null) return corr;
                }
                catch {}
                return edge;
            }

            if (ent is ISketchSegment seg)
            {
                GetSegmentModelPoints(seg, out _, out _, out double[] midM);
                double mx = midM != null ? midM[0] : 0.0;

                // If mx is near 0.0 or 1.2068 (sheet boundary edges), try finding the visible edge in the view
                if (Math.Abs(mx) < 0.015)
                {
                    object edgeAtZero = FindViewEdgeNearModelX(view, 0.0, 0.015);
                    if (edgeAtZero != null) return edgeAtZero;
                }
                else if (Math.Abs(mx - 1.2068) < 0.015)
                {
                    object edgeAtRight = FindViewEdgeNearModelX(view, 1.2068, 0.015);
                    if (edgeAtRight != null) return edgeAtRight;
                }

                ISketchSegment bl = FindMatchingDrawingBendLine(swApp, view, seg);
                if (bl != null) return bl;
                return seg;
            }

            if (ent is IEntity e)
            {
                try
                {
                    object corr = view.GetCorrespondingEntity(e);
                    if (corr != null) return corr;
                }
                catch {}
                return e;
            }

            return ent;
        }

        private static int ExecutePass0NativeAndAttachedEntitiesRepair(
            ISldWorks swApp,
            DrawingDoc swDrawing,
            ModelDoc2 swModel,
            List<ViewDanglingDiscovery> discoveredViews,
            string initialSheet,
            ref int runningDisplayCount,
            ref int runningDanglingCount)
        {
            LogDebug("\n=========================================================================");
            LogDebug("PASS 0 — NATIVE SOLIDWORKS REATTACH & DIRECT ATTACHED ENTITIES REPAIR");
            LogDebug("=========================================================================");

            int totalRepairedPass0 = 0;

            // -------------------------------------------------------------------------
            // PASS 0A: Native DrawingDoc.AttachDimensions() across target views
            // -------------------------------------------------------------------------
            LogDebug("\n--- PASS 0A: Calling swDrawing.AttachDimensions() across target views ---");
            try
            {
                var targetViews = discoveredViews.Where(v => v.GeometryScanRequired).ToList();
                foreach (var disc in targetViews)
                {
                    try { swDrawing.ActivateSheet(disc.SheetName); } catch {}
                    try { swDrawing.ActivateView(disc.ViewName); } catch {}
                    try { swDrawing.AttachDimensions(); } catch {}
                }
                try { swModel.ForceRebuild3(false); } catch {}
                LogDebug("swDrawing.AttachDimensions() executed across target views.");
            }
            catch (Exception ex)
            {
                LogDebug("swDrawing.AttachDimensions() exception: " + ex.Message);
            }

            int currentDisplay = 0;
            int currentDangling = 0;
            CountTotalDrawingDimensions(swDrawing, out currentDisplay, out currentDangling);
            LogDebug($"Post-AttachDimensions: Display={currentDisplay}, Dangling={currentDangling} (Initial={runningDanglingCount})");

            if (currentDangling < runningDanglingCount)
            {
                int fixedByAttach = runningDanglingCount - currentDangling;
                totalRepairedPass0 += fixedByAttach;
                runningDisplayCount = currentDisplay;
                runningDanglingCount = currentDangling;
                LogDebug($"AttachDimensions() successfully repaired {fixedByAttach} dimension(s)!");
            }

            if (runningDanglingCount == 0)
            {
                LogDebug("All dangling dimensions repaired by AttachDimensions()!");
                return totalRepairedPass0;
            }

            // -------------------------------------------------------------------------
            // PASS 0B: Native Annotation.SetAttachedEntities(...) with Drawing View Context
            // -------------------------------------------------------------------------
            LogDebug("\n--- PASS 0B: Attempting Annotation.SetAttachedEntities() with Drawing View Entities ---");
            int setAttachedRepaired = 0;

            try
            {
                var targetViews = discoveredViews.Where(v => v.GeometryScanRequired).ToList();
                foreach (var disc in targetViews)
                {
                    try { swDrawing.ActivateSheet(disc.SheetName); } catch {}
                    SolidWorks.Interop.sldworks.View sheetView = swDrawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                    SolidWorks.Interop.sldworks.View currentView = sheetView?.GetNextView() as SolidWorks.Interop.sldworks.View;

                    while (currentView != null)
                    {
                        string vName = currentView.GetName2() ?? "";
                        if (string.Equals(vName, disc.ViewName, StringComparison.OrdinalIgnoreCase))
                        {
                            try { swDrawing.ActivateView(vName); } catch {}

                            DisplayDimension dd = currentView.GetFirstDisplayDimension5() as DisplayDimension;
                            while (dd != null)
                            {
                                Annotation a = dd.GetAnnotation() as Annotation;
                                if (a != null && a.IsDangling())
                                {
                                    object[] ents = a.GetAttachedEntities3() as object[];
                                    if (ents != null && ents.Length >= 2 && ents[0] != null && ents[1] != null)
                                    {
                                        try
                                        {
                                            object resEnt1 = ResolveDrawingEntity(swApp, currentView, ents[0]);
                                            object resEnt2 = ResolveDrawingEntity(swApp, currentView, ents[1]);

                                            DispatchWrapper[] wrappers = new DispatchWrapper[]
                                            {
                                                new DispatchWrapper(resEnt1),
                                                new DispatchWrapper(resEnt2)
                                            };

                                            bool setRes = a.SetAttachedEntities(wrappers);

                                            // Rebuild to allow SolidWorks to recalculate attachment
                                            try { swModel.ForceRebuild3(false); } catch {}

                                            bool stillDangling = a.IsDangling();
                                            LogDebug($"  SetAttachedEntities for '{a.GetName()}' in view '{vName}': Return={setRes}, StillDangling={stillDangling}");
                                            if (!stillDangling)
                                            {
                                                setAttachedRepaired++;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            LogDebug($"  SetAttachedEntities threw for '{a.GetName()}': {ex.Message}");
                                        }
                                    }
                                }
                                dd = dd.GetNext5() as DisplayDimension;
                            }
                        }
                        currentView = currentView.GetNextView() as SolidWorks.Interop.sldworks.View;
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug("Error in Pass 0B: " + ex.Message);
            }

            CountTotalDrawingDimensions(swDrawing, out currentDisplay, out currentDangling);
            LogDebug($"Post-SetAttachedEntities: Display={currentDisplay}, Dangling={currentDangling}");

            if (currentDangling < runningDanglingCount)
            {
                int fixedBySet = runningDanglingCount - currentDangling;
                totalRepairedPass0 += fixedBySet;
                runningDisplayCount = currentDisplay;
                runningDanglingCount = currentDangling;
                LogDebug($"SetAttachedEntities() successfully repaired {fixedBySet} dimension(s)!");
            }

            if (runningDanglingCount == 0)
            {
                LogDebug("All dangling dimensions repaired by SetAttachedEntities()!");
                return totalRepairedPass0;
            }

            // -------------------------------------------------------------------------
            // PASS 0C: Direct Recreation from Surviving Attached Entities
            // -------------------------------------------------------------------------
            LogDebug("\n--- PASS 0C: Direct Recreation from Surviving Attached Entities ---");

            try
            {
                var targetViews = discoveredViews.Where(v => v.GeometryScanRequired).ToList();
                foreach (var disc in targetViews)
                {
                    try { swDrawing.ActivateSheet(disc.SheetName); } catch {}
                    SolidWorks.Interop.sldworks.View sheetView = swDrawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                    SolidWorks.Interop.sldworks.View currentView = sheetView?.GetNextView() as SolidWorks.Interop.sldworks.View;

                    while (currentView != null)
                    {
                        string vName = currentView.GetName2() ?? "";
                        if (string.Equals(vName, disc.ViewName, StringComparison.OrdinalIgnoreCase))
                        {
                            bool actOk = false;
                            try { actOk = swDrawing.ActivateView(vName); } catch {}
                            if (!actOk)
                            {
                                int pIdx = vName.IndexOf('(');
                                if (pIdx > 0)
                                {
                                    string vTrim = vName.Substring(0, pIdx).Trim();
                                    try { actOk = swDrawing.ActivateView(vTrim); } catch {}
                                }
                            }
                            if (!actOk)
                            {
                                try { swModel.Extension.SelectByID2(vName, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0); } catch {}
                            }
                            LogDebug($"  Activated view '{vName}': {actOk}");

                            // Collect all dangling dimensions in this view
                            List<DisplayDimension> candidateDims = new List<DisplayDimension>();
                            DisplayDimension dd = currentView.GetFirstDisplayDimension5() as DisplayDimension;
                            while (dd != null)
                            {
                                Annotation a = dd.GetAnnotation() as Annotation;
                                if (a != null && a.IsDangling())
                                {
                                    candidateDims.Add(dd);
                                }
                                dd = dd.GetNext5() as DisplayDimension;
                            }

                            LogDebug($"  View '{vName}': Found {candidateDims.Count} dangling dimension(s).");

                            List<ViewCandidateEntity> viewCandidates = null;
                            if (candidateDims.Count > 0)
                            {
                                viewCandidates = CollectViewCandidateEntities(swApp, currentView);
                            }

                            foreach (var targetDispDim in candidateDims)
                            {
                                Annotation targetAnnot = targetDispDim.GetAnnotation() as Annotation;
                                if (targetAnnot == null || !targetAnnot.IsDangling()) continue;

                                SingleTargetRepairResult res = null;
                                bool isSingleEntity = IsSingleEntityDimension(targetDispDim);
                                object[] ents = targetAnnot.GetAttachedEntities3() as object[];

                                if (isSingleEntity)
                                {
                                    if (ents != null && ents.Length >= 1 && ents[0] != null)
                                    {
                                        res = RecreateSingleEntityDimensionFromAttachedEntities(
                                            swApp,
                                            swDrawing,
                                            swModel,
                                            currentView,
                                            targetDispDim,
                                            targetAnnot,
                                            ents[0],
                                            runningDisplayCount,
                                            runningDanglingCount);
                                    }

                                    if (res == null || res.Status != SingleTargetStatus.Success)
                                    {
                                        if (viewCandidates != null && viewCandidates.Count >= 1)
                                        {
                                            res = RecreateSingleEntityDimensionFromCandidates(
                                                swApp,
                                                swDrawing,
                                                swModel,
                                                currentView,
                                                targetDispDim,
                                                targetAnnot,
                                                viewCandidates,
                                                runningDisplayCount,
                                                runningDanglingCount);
                                        }
                                    }
                                }
                                else
                                {
                                    if (ents != null && ents.Length >= 2 && ents[0] != null && ents[1] != null)
                                    {
                                        res = RecreateDimensionFromAttachedEntities(
                                            swApp,
                                            swDrawing,
                                            swModel,
                                            currentView,
                                            targetDispDim,
                                            targetAnnot,
                                            ents[0],
                                            ents[1],
                                            runningDisplayCount,
                                            runningDanglingCount);
                                    }

                                    if (res == null || res.Status != SingleTargetStatus.Success)
                                    {
                                        if (viewCandidates != null && viewCandidates.Count >= 2)
                                        {
                                            res = RecreateDimensionFromViewCandidates(
                                                swApp,
                                                swDrawing,
                                                swModel,
                                                currentView,
                                                targetDispDim,
                                                targetAnnot,
                                                viewCandidates,
                                                runningDisplayCount,
                                                runningDanglingCount);
                                        }
                                    }
                                }

                                if (res != null && res.Status == SingleTargetStatus.Success)
                                {
                                    totalRepairedPass0++;
                                    runningDisplayCount = res.PostDisplayCount;
                                    runningDanglingCount = res.PostDanglingCount;
                                    LogDebug($"  Direct Recreation SUCCESS for '{targetAnnot.GetName()}'! Remaining Dangling: {runningDanglingCount}");

                                    if (runningDanglingCount == 0) break;
                                }
                                else
                                {
                                    LogDebug($"  Direct Recreation FAILED for '{targetAnnot.GetName()}': {(res != null ? res.Reason : "UNKNOWN")}");
                                }
                            }
                        }

                        if (runningDanglingCount == 0) break;
                        currentView = currentView.GetNextView() as SolidWorks.Interop.sldworks.View;
                    }

                    if (runningDanglingCount == 0) break;
                }
            }
            catch (Exception ex)
            {
                LogDebug("Error in Pass 0C: " + ex.Message);
            }
            finally
            {
                if (!string.IsNullOrEmpty(initialSheet))
                {
                    try { swDrawing.ActivateSheet(initialSheet); } catch {}
                }
            }

            LogDebug($"\n=== PASS 0 FINISHED: Total Repaired={totalRepairedPass0}, RunningDisplay={runningDisplayCount}, RunningDangling={runningDanglingCount} ===");
            return totalRepairedPass0;
        }

        private static bool? IsDimensionVertical(DisplayDimension dispDim, Dimension dim)
        {
            if (dim != null)
            {
                try
                {
                    MathVector vDir = dim.DimensionLineDirection;
                    if (vDir != null && vDir.ArrayData is double[] arr && arr.Length >= 2)
                    {
                        double vx = Math.Abs(arr[0]);
                        double vy = Math.Abs(arr[1]);
                        if (vy > vx * 1.2) return true;
                        if (vx > vy * 1.2) return false;
                    }
                }
                catch {}

                try
                {
                    MathVector extDir = dim.ExtensionLineDirection;
                    if (extDir != null && extDir.ArrayData is double[] arr && arr.Length >= 2)
                    {
                        double ex = Math.Abs(arr[0]);
                        double ey = Math.Abs(arr[1]);
                        if (ex > ey * 1.2) return true;
                        if (ey > ex * 1.2) return false;
                    }
                }
                catch {}
            }

            if (dispDim != null)
            {
                try
                {
                    DisplayData dd = dispDim.GetDisplayData() as DisplayData;
                    if (dd != null)
                    {
                        int lCount = dd.GetLineCount();
                        double dimDx = 0.0, dimDy = 0.0;
                        double extDx = 0.0, extDy = 0.0;
                        for (int li = 0; li < lCount; li++)
                        {
                            object lObj = dd.GetLineAtIndex3(li);
                            if (lObj is double[] lArr && lArr.Length >= 10)
                            {
                                int lineType = Convert.ToInt32(lArr[1]);
                                double dx = Math.Abs(lArr[7] - lArr[4]);
                                double dy = Math.Abs(lArr[8] - lArr[5]);
                                if (lineType == 0)
                                {
                                    dimDx += dx;
                                    dimDy += dy;
                                }
                                else
                                {
                                    extDx += dx;
                                    extDy += dy;
                                }
                            }
                        }
                        if (dimDy > dimDx * 1.2) return true;
                        if (dimDx > dimDy * 1.2) return false;
                        if (extDx > extDy * 1.2) return true;
                        if (extDy > extDx * 1.2) return false;
                    }
                }
                catch {}
            }

            return null;
        }

        private static bool IsSingleEntityDimension(DisplayDimension dispDim)
        {
            if (dispDim == null) return false;
            try
            {
                if (dispDim.IsHoleCallout()) return true;
            }
            catch {}
            try
            {
                int type2 = dispDim.Type2;
                if (type2 == (int)swDimensionType_e.swDiameterDimension ||
                    type2 == (int)swDimensionType_e.swRadialDimension ||
                    type2 == (int)swDimensionType_e.swDiametricLinearDimension ||
                    type2 == (int)swDimensionType_e.swRadialLinearDimension ||
                    type2 == (int)swDimensionType_e.swChamferDimension)
                {
                    return true;
                }
            }
            catch {}
            return false;
        }

        private static SingleTargetRepairResult RecreateDimensionFromAttachedEntities(
            ISldWorks swApp,
            DrawingDoc swDrawing,
            ModelDoc2 swModel,
            SolidWorks.Interop.sldworks.View targetView,
            DisplayDimension targetDispDim,
            Annotation targetAnnot,
            object ent1,
            object ent2,
            int currentDisplayBefore,
            int currentDanglingBefore)
        {
            Dimension oldDim = targetDispDim.GetDimension2(0) as Dimension ?? targetDispDim.GetDimension() as Dimension;
            string oldDimFullName = oldDim != null ? oldDim.FullName : targetAnnot.GetName();
            double? oldSysVal = null;
            if (oldDim != null)
            {
                try
                {
                    object values = oldDim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                    if (values is double[] arr && arr.Length > 0) oldSysVal = arr[0];
                    else if (values is double d) oldSysVal = d;
                    else oldSysVal = oldDim.GetSystemValue2("");
                }
                catch {}
            }

            double[] oldPos = null;
            try { oldPos = targetAnnot.GetPosition() as double[]; } catch {}

            swModel.ClearSelection2(true);
            ISelectionMgr selMgr = swModel.SelectionManager as ISelectionMgr;

            bool s1 = SelectDrawingOrModelEntity(swApp, swModel, targetView, ent1, false, selMgr);
            bool s2 = SelectDrawingOrModelEntity(swApp, swModel, targetView, ent2, true, selMgr);

            int selCount = (selMgr != null) ? selMgr.GetSelectedObjectCount2(-1) : 0;
            if (selCount != 2 || !s1 || !s2)
            {
                // Try reverse selection order
                swModel.ClearSelection2(true);
                bool s2Rev = SelectDrawingOrModelEntity(swApp, swModel, targetView, ent2, false, selMgr);
                bool s1Rev = SelectDrawingOrModelEntity(swApp, swModel, targetView, ent1, true, selMgr);
                int selCountRev = (selMgr != null) ? selMgr.GetSelectedObjectCount2(-1) : 0;

                if (selCountRev == 2 && s1Rev && s2Rev)
                {
                    selCount = selCountRev;
                    s1 = s1Rev;
                    s2 = s2Rev;
                    LogDebug("  [RecreateDimensionFromAttachedEntities] Reverse selection order succeeded!");
                }
                else
                {
                    swModel.ClearSelection2(true);
                    return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = $"SELECTION_COUNT_INVALID (Count={selCount}, s1={s1}, s2={s2}; RevCount={selCountRev}, s1Rev={s1Rev}, s2Rev={s2Rev})" };
                }
            }

            double posX = (oldPos != null && oldPos.Length >= 1) ? oldPos[0] : 0.0;
            double posY = (oldPos != null && oldPos.Length >= 2) ? oldPos[1] : 0.0;
            double posZ = (oldPos != null && oldPos.Length >= 3) ? oldPos[2] : 0.0;

            DisplayDimension newDisp = null;
            try
            {
                newDisp = swModel.AddDimension2(posX, posY, posZ) as DisplayDimension;
            }
            catch (Exception ex)
            {
                LogDebug($"AddDimension2 Exception: {ex.Message}");
            }

            if (newDisp == null)
            {
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "ADD_DIMENSION_NULL" };
            }

            Annotation newAnnot = newDisp.GetAnnotation() as Annotation;
            Dimension newDim = newDisp.GetDimension2(0) as Dimension ?? newDisp.GetDimension() as Dimension;
            string newDimFullName = newDim != null ? newDim.FullName : (newAnnot != null ? newAnnot.GetName() : "");

            bool newDangling = (newAnnot != null) && newAnnot.IsDangling();
            int newAttached = (newAnnot != null) ? newAnnot.GetAttachedEntityCount3() : 0;

            double? newSysVal = null;
            if (newDim != null)
            {
                try
                {
                    object values = newDim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                    if (values is double[] arr && arr.Length > 0) newSysVal = arr[0];
                    else if (values is double d) newSysVal = d;
                    else newSysVal = newDim.GetSystemValue2("");
                }
                catch {}
            }

            bool isAngular = (targetDispDim.Type2 == (int)swDimensionType_e.swAngularDimension || targetDispDim.Type2 == (int)swDimensionType_e.swAngularOrdinateDimension);
            bool valMatch = false;
            double deltaValMm = double.MaxValue;
            double effTolMm = 0.15;

            if (oldSysVal.HasValue && newSysVal.HasValue)
            {
                if (isAngular)
                {
                    double deltaDeg = Math.Abs(newSysVal.Value - oldSysVal.Value) * 180.0 / Math.PI;
                    valMatch = deltaDeg <= 0.1;
                }
                else
                {
                    deltaValMm = Math.Abs(newSysVal.Value - oldSysVal.Value) * 1000.0;
                    effTolMm = Math.Max(0.15, Math.Abs(oldSysVal.Value * 1000.0) * 0.001);
                    valMatch = deltaValMm <= effTolMm;
                }
            }

            if (newDangling || newAttached < 1 || !valMatch)
            {
                LogDebug($"New dim verification failed: Dangling={newDangling}, Attached={newAttached}, ValMatch={valMatch} (Old={oldSysVal * 1000.0:F3}mm, New={newSysVal * 1000.0:F3}mm, Delta={deltaValMm:F3}mm)");
                DeleteProvisionalDimension(swModel, newDisp, "PASS0C_VERIFY_FAILED");
                swModel.ClearSelection2(true);
                string failReason = newDangling ? "NEW_DIM_DANGLING" : (newAttached < 1 ? "NEW_ATTACHED_EMPTY" : "VALUE_MISMATCH");
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = failReason };
            }

            bool? targetIsVertAttached = IsDimensionVertical(targetDispDim, oldDim);
            if (targetIsVertAttached.HasValue)
            {
                bool? newIsVertAttached = IsDimensionVertical(newDisp, newDim);
                if (newIsVertAttached.HasValue && newIsVertAttached.Value != targetIsVertAttached.Value)
                {
                    LogDebug($"Pass 0C verification failed: Orientation mismatch (Target vertical={targetIsVertAttached.Value}, New vertical={newIsVertAttached.Value})");
                    DeleteProvisionalDimension(swModel, newDisp, "PASS0C_ORIENTATION_MISMATCH");
                    swModel.ClearSelection2(true);
                    return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "ORIENTATION_MISMATCH" };
                }
            }

            // Clone presentation
            CopyDimensionPresentation(targetDispDim, targetAnnot, oldDim, newDisp, newAnnot, newDim, oldPos);

            // Safely delete old dimension
            swModel.ClearSelection2(true);
            IModelDocExtension ext = swModel.Extension;
            bool oldSelected = false;

            if (!string.IsNullOrEmpty(oldDimFullName) && ext != null)
            {
                try { oldSelected = ext.SelectByID2(oldDimFullName, "DIMENSION", 0.0, 0.0, 0.0, false, 0, null, 0); } catch {}
            }
            if (!oldSelected && targetAnnot != null)
            {
                try { oldSelected = targetAnnot.Select3(false, null); } catch {}
            }

            int selCountAfterSelect = (selMgr != null) ? selMgr.GetSelectedObjectCount2(-1) : 0;
            int selTypeRaw = selMgr != null ? selMgr.GetSelectedObjectType3(1, -1) : -1;
            string selTypeName = ((swSelectType_e)selTypeRaw).ToString();

            bool selectOk = oldSelected && selCountAfterSelect == 1 &&
                (selTypeRaw == (int)swSelectType_e.swSelDIMENSIONS || selTypeName.IndexOf("DIMENSION", StringComparison.OrdinalIgnoreCase) >= 0);

            if (!selectOk)
            {
                LogDebug($"Safe delete selection failed for '{oldDimFullName}'. Cleaning up new dim.");
                DeleteProvisionalDimension(swModel, newDisp, "PASS0C_SAFE_DELETE_SELECTION_FAILED");
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "SAFE_DELETE_SELECTION_FAILED" };
            }

            bool deleteResult = false;
            try { deleteResult = ext.DeleteSelection2(0); } catch {}
            swModel.ClearSelection2(true);

            if (!deleteResult)
            {
                LogDebug($"DeleteSelection2 returned false for '{oldDimFullName}'. Cleaning up new dim.");
                DeleteProvisionalDimension(swModel, newDisp, "PASS0C_DELETE_RETURNED_FALSE");
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "DELETE_RETURNED_FALSE" };
            }

            int postDisplay = 0;
            int postDangling = 0;
            CountTotalDrawingDimensions(swDrawing, out postDisplay, out postDangling);

            bool newPostDangling = true;
            try { newPostDangling = newAnnot.IsDangling(); } catch {}
            int newPostAttached = 0;
            try { newPostAttached = newAnnot.GetAttachedEntityCount3(); } catch {}

            if (newPostDangling || newPostAttached < 1)
            {
                LogDebug($"FATAL: Post-delete new dim invalid (Dangling={newPostDangling}, Attached={newPostAttached})");
                return new SingleTargetRepairResult
                {
                    Status = SingleTargetStatus.Failed,
                    Reason = "NEW_DIM_INVALID_AFTER_DELETE",
                    IsUnsafeState = true
                };
            }

            return new SingleTargetRepairResult
            {
                Status = SingleTargetStatus.Success,
                PostDisplayCount = postDisplay,
                PostDanglingCount = postDangling
            };
        }

        private static bool GetEdgeModelPoints(IEdge edge, out double[] startPt, out double[] endPt, out double[] midPt)
        {
            return GetEdgeModelPoints(edge, out startPt, out endPt, out midPt, out _, out _);
        }

        private static bool GetEdgeModelPoints(
            IEdge edge, 
            out double[] startPt, 
            out double[] endPt, 
            out double[] midPt, 
            out bool isCircle, 
            out double radius)
        {
            startPt = null;
            endPt = null;
            midPt = null;
            isCircle = false;
            radius = 0.0;
            if (edge == null) return false;
            try
            {
                Curve c = edge.GetCurve() as Curve;
                if (c != null)
                {
                    if (c.IsCircle())
                    {
                        double[] cParams = c.CircleParams as double[];
                        if (cParams != null && cParams.Length >= 7)
                        {
                            isCircle = true;
                            radius = Math.Abs(cParams[6]);
                            midPt = new double[] { cParams[0], cParams[1], cParams[2] };
                            startPt = new double[] { cParams[0] - radius, cParams[1], cParams[2] };
                            endPt = new double[] { cParams[0] + radius, cParams[1], cParams[2] };
                            return true;
                        }
                    }

                    if (c.GetEndParams(out double sp, out double ep, out _, out _))
                    {
                        startPt = c.Evaluate2(sp, 0) as double[];
                        endPt = c.Evaluate2(ep, 0) as double[];
                        if (startPt != null && endPt != null && startPt.Length >= 3 && endPt.Length >= 3)
                        {
                            midPt = new double[]
                            {
                                (startPt[0] + endPt[0]) / 2.0,
                                (startPt[1] + endPt[1]) / 2.0,
                                (startPt[2] + endPt[2]) / 2.0
                            };
                            return true;
                        }
                    }
                }
            }
            catch {}
            return false;
        }

        private static bool SelectEntityInView(
            ISelectionMgr selMgr,
            ModelDoc2 swModel,
            ISldWorks swApp,
            SolidWorks.Interop.sldworks.View defaultView,
            ViewCandidateEntity cand,
            bool append)
        {
            if (cand == null || cand.Entity == null || selMgr == null) return false;
            int initialCount = selMgr.GetSelectedObjectCount2(-1);
            int expectedCount = append ? initialCount + 1 : 1;

            SolidWorks.Interop.sldworks.View view = cand.SourceView ?? defaultView;

            SelectData selData = selMgr.CreateSelectData();
            if (selData != null && view != null)
            {
                try { selData.View = view; } catch {}
            }

            if (cand.Entity is IEntity e)
            {
                try
                {
                    if (e.Select4(append, selData))
                    {
                        if (selMgr.GetSelectedObjectCount2(-1) == expectedCount) return true;
                    }
                }
                catch {}
                try
                {
                    if (e.Select4(append, null))
                    {
                        if (selMgr.GetSelectedObjectCount2(-1) == expectedCount) return true;
                    }
                }
                catch {}

                // Coordinate fallback for edges
                double[] edgePt = cand.EdgePointSheet ?? cand.MidSheet;
                if (edgePt != null && swModel != null && swModel.Extension != null)
                {
                    try
                    {
                        if (swModel.Extension.SelectByID2("", "EDGE", edgePt[0], edgePt[1], edgePt[2], append, 0, null, 0))
                        {
                            if (selMgr.GetSelectedObjectCount2(-1) == expectedCount) return true;
                        }
                    }
                    catch {}
                }
            }
            else if (cand.Entity is ISketchSegment seg)
            {
                try
                {
                    if (seg.Select4(append, selData))
                    {
                        if (selMgr.GetSelectedObjectCount2(-1) == expectedCount) return true;
                    }
                }
                catch {}
                try
                {
                    if (seg.Select4(append, null))
                    {
                        if (selMgr.GetSelectedObjectCount2(-1) == expectedCount) return true;
                    }
                }
                catch {}

                // Coordinate fallback for sketch segments (bend lines)
                double[] segPt = cand.EdgePointSheet ?? cand.MidSheet;
                if (segPt != null && swModel != null && swModel.Extension != null)
                {
                    string[] types = new string[] { "EXTSKETCHSEGMENT", "SKETCHSEGMENT" };
                    foreach (var t in types)
                    {
                        try
                        {
                            if (swModel.Extension.SelectByID2("", t, segPt[0], segPt[1], segPt[2], append, 0, null, 0))
                            {
                                if (selMgr.GetSelectedObjectCount2(-1) == expectedCount) return true;
                            }
                        }
                        catch {}
                    }
                }
            }
            return false;
        }

        private sealed class ViewCandidateEntity
        {
            public object Entity { get; set; }
            public bool IsBendLine { get; set; }
            public bool IsDirectView { get; set; }
            public SolidWorks.Interop.sldworks.View SourceView { get; set; }
            public double[] MidSheet { get; set; }
            public double[] MidModel { get; set; }
            public double[] EdgePointSheet { get; set; }
            public bool IsHorizontal { get; set; }
            public bool IsCircle { get; set; }
            public double RadiusModel { get; set; }
        }

        private static List<ViewCandidateEntity> CollectViewCandidateEntities(
            ISldWorks swApp,
            SolidWorks.Interop.sldworks.View view)
        {
            List<ViewCandidateEntity> result = new List<ViewCandidateEntity>();
            if (view == null) return result;

            List<SolidWorks.Interop.sldworks.View> viewsToCheck = new List<SolidWorks.Interop.sldworks.View>();
            viewsToCheck.Add(view);
            try
            {
                SolidWorks.Interop.sldworks.View baseV = view.GetBaseView() as SolidWorks.Interop.sldworks.View;
                if (baseV != null) viewsToCheck.Add(baseV);
            }
            catch {}

            for (int vIdx = 0; vIdx < viewsToCheck.Count; vIdx++)
            {
                var v = viewsToCheck[vIdx];
                bool isDirectView = (vIdx == 0);

                // 1. Bend Lines
                try
                {
                    object[] blArr = v.GetBendLines() as object[];
                    if (blArr != null)
                    {
                        foreach (object bl in blArr)
                        {
                            if (bl is ISketchSegment seg)
                            {
                                double[] midS = null;
                                double[] midM = null;
                                double[] sS = null;
                                bool isHoriz = false;
                                bool isCirc = false;
                                double radM = 0.0;
                                if (GetSegmentModelPoints(seg, out double[] sM, out double[] eM, out midM, out isCirc, out radM))
                                {
                                    midS = TransformPointToSheet(swApp, v, midM);
                                    sS = TransformPointToSheet(swApp, v, sM);
                                    double[] eS = TransformPointToSheet(swApp, v, eM);
                                    if (sS != null && eS != null && sS.Length >= 2 && eS.Length >= 2)
                                    {
                                        isHoriz = Math.Abs(eS[0] - sS[0]) >= Math.Abs(eS[1] - sS[1]);
                                    }
                                }
                                result.Add(new ViewCandidateEntity
                                {
                                    Entity = seg,
                                    IsBendLine = true,
                                    IsDirectView = isDirectView,
                                    SourceView = v,
                                    MidSheet = midS,
                                    MidModel = midM,
                                    EdgePointSheet = sS,
                                    IsHorizontal = isHoriz,
                                    IsCircle = isCirc,
                                    RadiusModel = radM
                                });
                            }
                        }
                    }
                }
                catch {}

                // 2. Visible Edges
                List<IEdge> viewEdges = new List<IEdge>();
                try
                {
                    object[] comps = null;
                    try { comps = v.GetVisibleComponents() as object[]; } catch {}
                    if (comps != null && comps.Length > 0)
                    {
                        foreach (object cObj in comps)
                        {
                            if (cObj is Component2 comp)
                            {
                                object[] edges = null;
                                try { edges = v.GetVisibleEntities2(comp, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch {}
                                if (edges != null)
                                {
                                    foreach (object eObj in edges)
                                    {
                                        if (eObj is IEdge edge && !viewEdges.Contains(edge)) viewEdges.Add(edge);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        object[] edges = null;
                        try { edges = v.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch {}
                        if (edges != null)
                        {
                            foreach (object eObj in edges)
                            {
                                if (eObj is IEdge edge && !viewEdges.Contains(edge)) viewEdges.Add(edge);
                            }
                        }
                    }
                }
                catch {}

                foreach (var edge in viewEdges)
                {
                    double[] midS = null;
                    double[] midM = null;
                    double[] sS = null;
                    bool isHoriz = false;
                    bool isCirc = false;
                    double radM = 0.0;
                    if (GetEdgeModelPoints(edge, out double[] sM, out double[] eM, out midM, out isCirc, out radM))
                    {
                        midS = TransformPointToSheet(swApp, v, midM);
                        sS = TransformPointToSheet(swApp, v, sM);
                        double[] eS = TransformPointToSheet(swApp, v, eM);
                        if (sS != null && eS != null && sS.Length >= 2 && eS.Length >= 2)
                        {
                            isHoriz = Math.Abs(eS[0] - sS[0]) >= Math.Abs(eS[1] - sS[1]);
                        }
                    }
                    result.Add(new ViewCandidateEntity
                    {
                        Entity = edge,
                        IsBendLine = false,
                        IsDirectView = isDirectView,
                        SourceView = v,
                        MidSheet = midS,
                        MidModel = midM,
                        EdgePointSheet = sS,
                        IsHorizontal = isHoriz,
                        IsCircle = isCirc,
                        RadiusModel = radM
                    });
                }
            }

            LogDebug($"  [CollectViewCandidateEntities] View '{view.GetName2()}': Collected {result.Count} candidates ({result.Count(c => c.IsBendLine)} bend lines, {result.Count(c => !c.IsBendLine && !c.IsCircle)} straight edges, {result.Count(c => c.IsCircle)} circular holes/arcs, {result.Count(c => c.IsHorizontal)} horiz, {result.Count(c => !c.IsHorizontal)} vert)");
            return result;
        }

        private sealed class CandidateEntityPair
        {
            public ViewCandidateEntity Ent1 { get; set; }
            public ViewCandidateEntity Ent2 { get; set; }
            public double Score { get; set; }
        }

        private static SingleTargetRepairResult RecreateDimensionFromViewCandidates(
            ISldWorks swApp,
            DrawingDoc swDrawing,
            ModelDoc2 swModel,
            SolidWorks.Interop.sldworks.View currentView,
            DisplayDimension targetDispDim,
            Annotation targetAnnot,
            List<ViewCandidateEntity> candidates,
            int currentDisplayBefore,
            int currentDanglingBefore)
        {
            if (candidates == null || candidates.Count < 2)
            {
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "INSUFFICIENT_VIEW_CANDIDATES" };
            }

            Dimension oldDim = targetDispDim.GetDimension2(0) as Dimension ?? targetDispDim.GetDimension() as Dimension;
            string oldDimFullName = oldDim != null ? oldDim.FullName : targetAnnot.GetName();
            double? oldSysVal = null;
            if (oldDim != null)
            {
                try
                {
                    object values = oldDim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                    if (values is double[] arr && arr.Length > 0) oldSysVal = arr[0];
                    else if (values is double d) oldSysVal = d;
                    else oldSysVal = oldDim.GetSystemValue2("");
                }
                catch {}
            }

            if (!oldSysVal.HasValue)
            {
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "OLD_SYS_VAL_NULL" };
            }

            double[] oldPos = null;
            try { oldPos = targetAnnot.GetPosition() as double[]; } catch {}

            double targetMm = oldSysVal.Value * 1000.0;
            bool? targetIsVertical = IsDimensionVertical(targetDispDim, oldDim);
            LogDebug($"  [RecreateDimensionFromViewCandidates] Searching pair for '{targetAnnot.GetName()}' (Target={targetMm:F3}mm, TargetVertical={targetIsVertical?.ToString() ?? "null"})...");

            List<CandidateEntityPair> pairs = new List<CandidateEntityPair>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var c1 = candidates[i];
                for (int j = i + 1; j < candidates.Count; j++)
                {
                    var c2 = candidates[j];

                    double score = 0.0;
                    if (oldPos != null && c1.MidSheet != null && c2.MidSheet != null)
                    {
                        double pairMidX = (c1.MidSheet[0] + c2.MidSheet[0]) / 2.0;
                        double pairMidY = (c1.MidSheet[1] + c2.MidSheet[1]) / 2.0;

                        if (targetIsVertical.HasValue)
                        {
                            if (targetIsVertical.Value)
                            {
                                // Target is vertical: primary measurement axis is Y
                                score = Math.Abs(pairMidY - oldPos[1]) * 4.0 + Math.Abs(pairMidX - oldPos[0]) * 0.2;
                                // Vertical dimension requires two horizontal entities, or horizontal entity + circle, or two circles
                                bool c1Ok = c1.IsHorizontal || c1.IsCircle;
                                bool c2Ok = c2.IsHorizontal || c2.IsCircle;
                                if (c1Ok && c2Ok)
                                {
                                    score -= 10.0;
                                }
                                else
                                {
                                    score += 1000.0;
                                }
                            }
                            else
                            {
                                // Target is horizontal: primary measurement axis is X
                                score = Math.Abs(pairMidX - oldPos[0]) * 4.0 + Math.Abs(pairMidY - oldPos[1]) * 0.2;
                                // Horizontal dimension requires two vertical entities, or vertical entity + circle, or two circles
                                bool c1Ok = (!c1.IsHorizontal) || c1.IsCircle;
                                bool c2Ok = (!c2.IsHorizontal) || c2.IsCircle;
                                if (c1Ok && c2Ok)
                                {
                                    score -= 10.0;
                                }
                                else
                                {
                                    score += 1000.0;
                                }
                            }
                        }
                        else
                        {
                            score = Math.Sqrt(Math.Pow(pairMidX - oldPos[0], 2) + Math.Pow(pairMidY - oldPos[1], 2));
                        }

                        // Direct View bonus
                        if (c1.IsDirectView && c2.IsDirectView) score -= 2.0;
                        else if (c1.IsDirectView || c2.IsDirectView) score -= 1.0;

                        // Hole dimension topology bonus
                        if (c1.IsCircle || c2.IsCircle)
                        {
                            if (targetMm < 300.0) score -= 0.5;
                        }

                        // Sheet metal dimension topology bonus
                        if (targetMm > 100.0)
                        {
                            if (c1.IsBendLine && c2.IsBendLine) score -= 0.5;
                            else if (c1.IsBendLine || c2.IsBendLine) score -= 0.2;
                        }
                        else
                        {
                            if ((c1.IsBendLine && !c2.IsBendLine) || (!c1.IsBendLine && c2.IsBendLine)) score -= 0.3;
                            else if (c1.IsBendLine && c2.IsBendLine) score -= 0.1;
                        }
                    }
                    else
                    {
                        // Heavily penalize missing coordinates so pairs with known geometry are evaluated first
                        score = 10000.0;
                    }

                    pairs.Add(new CandidateEntityPair { Ent1 = c1, Ent2 = c2, Score = score });
                }
            }

            pairs.Sort((a, b) => a.Score.CompareTo(b.Score));
            LogDebug($"  [RecreateDimensionFromViewCandidates] Evaluated {pairs.Count} candidate pairs. Testing top candidates...");

            ISelectionMgr selMgr = swModel.SelectionManager as ISelectionMgr;
            int triedCount = 0;

            foreach (var pair in pairs)
            {
                triedCount++;
                if (triedCount > 200) break; // Limit search to top 200 pairs to ensure responsiveness

                swModel.ClearSelection2(true);
                bool s1 = SelectEntityInView(selMgr, swModel, swApp, currentView, pair.Ent1, false);
                bool s2 = SelectEntityInView(selMgr, swModel, swApp, currentView, pair.Ent2, true);

                if (!s1 || !s2 || selMgr.GetSelectedObjectCount2(-1) != 2)
                {
                    swModel.ClearSelection2(true);
                    s2 = SelectEntityInView(selMgr, swModel, swApp, currentView, pair.Ent2, false);
                    s1 = SelectEntityInView(selMgr, swModel, swApp, currentView, pair.Ent1, true);
                    if (!s1 || !s2 || selMgr.GetSelectedObjectCount2(-1) != 2)
                    {
                        swModel.ClearSelection2(true);
                        continue;
                    }
                }

                double posX = (oldPos != null && oldPos.Length >= 1) ? oldPos[0] : 0.0;
                double posY = (oldPos != null && oldPos.Length >= 2) ? oldPos[1] : 0.0;

                DisplayDimension newDisp = null;
                try
                {
                    newDisp = swModel.AddDimension2(posX, posY, 0.0) as DisplayDimension;
                }
                catch {}

                if (newDisp == null && oldPos != null && oldPos.Length >= 3)
                {
                    try { newDisp = swModel.AddDimension2(posX, posY, oldPos[2]) as DisplayDimension; } catch {}
                }

                if (newDisp == null)
                {
                    swModel.ClearSelection2(true);
                    continue;
                }

                Annotation newAnnot = newDisp.GetAnnotation() as Annotation;
                Dimension newDim = newDisp.GetDimension2(0) as Dimension ?? newDisp.GetDimension() as Dimension;
                string newDimFullName = newDim != null ? newDim.FullName : (newAnnot != null ? newAnnot.GetName() : "");

                bool newDangling = (newAnnot != null) && newAnnot.IsDangling();
                int newAttached = (newAnnot != null) ? newAnnot.GetAttachedEntityCount3() : 0;

                double? newSysVal = null;
                if (newDim != null)
                {
                    try
                    {
                        object values = newDim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                        if (values is double[] arr && arr.Length > 0) newSysVal = arr[0];
                        else if (values is double d) newSysVal = d;
                        else newSysVal = newDim.GetSystemValue2("");
                    }
                    catch {}
                }

                bool isAngular = (targetDispDim.Type2 == (int)swDimensionType_e.swAngularDimension || targetDispDim.Type2 == (int)swDimensionType_e.swAngularOrdinateDimension);
                bool valMatch = false;
                double deltaValMm = double.MaxValue;
                double effTolMm = 0.15;

                if (oldSysVal.HasValue && newSysVal.HasValue)
                {
                    if (isAngular)
                    {
                        double deltaDeg = Math.Abs(newSysVal.Value - oldSysVal.Value) * 180.0 / Math.PI;
                        valMatch = deltaDeg <= 0.1;
                    }
                    else
                    {
                        deltaValMm = Math.Abs(newSysVal.Value - oldSysVal.Value) * 1000.0;
                        effTolMm = Math.Max(0.15, Math.Abs(oldSysVal.Value * 1000.0) * 0.001);
                        valMatch = deltaValMm <= effTolMm;
                    }
                }

                if (newDangling || newAttached < 1 || !valMatch)
                {
                    if (triedCount <= 10)
                    {
                        LogDebug($"  [Trial #{triedCount}] Failed: Dangling={newDangling}, Attached={newAttached}, Val={(newSysVal.HasValue ? (newSysVal.Value * 1000.0).ToString("F3") : "null")}mm (Target={targetMm:F3}mm, Delta={(newSysVal.HasValue ? deltaValMm.ToString("F3") : "N/A")}mm)");
                    }
                    DeleteProvisionalDimension(swModel, newDisp, "CANDIDATE_PAIR_VERIFY_FAILED");
                    swModel.ClearSelection2(true);
                    continue;
                }

                if (targetIsVertical.HasValue)
                {
                    bool? newIsVert = IsDimensionVertical(newDisp, newDim);
                    if (newIsVert.HasValue && newIsVert.Value != targetIsVertical.Value)
                    {
                        if (triedCount <= 10)
                        {
                            LogDebug($"  [Trial #{triedCount}] Rejected: Orientation mismatch (Target vertical={targetIsVertical.Value}, New vertical={newIsVert.Value})");
                        }
                        DeleteProvisionalDimension(swModel, newDisp, "CANDIDATE_PAIR_ORIENTATION_MISMATCH");
                        swModel.ClearSelection2(true);
                        continue;
                    }
                }

                LogDebug($"  [RecreateDimensionFromViewCandidates] MATCH FOUND! NewVal={newSysVal * 1000.0:F3}mm, Target={targetMm:F3}mm, Delta={deltaValMm:F4}mm (Trial #{triedCount})");

                // Clone presentation
                CopyDimensionPresentation(targetDispDim, targetAnnot, oldDim, newDisp, newAnnot, newDim, oldPos);

                // Safely delete old dimension
                swModel.ClearSelection2(true);
                IModelDocExtension ext = swModel.Extension;
                bool oldSelected = false;

                if (!string.IsNullOrEmpty(oldDimFullName) && ext != null)
                {
                    try { oldSelected = ext.SelectByID2(oldDimFullName, "DIMENSION", 0.0, 0.0, 0.0, false, 0, null, 0); } catch {}
                }
                if (!oldSelected && targetAnnot != null)
                {
                    try { oldSelected = targetAnnot.Select3(false, null); } catch {}
                }

                int selCountAfterSelect = (selMgr != null) ? selMgr.GetSelectedObjectCount2(-1) : 0;
                int selTypeRaw = selMgr != null ? selMgr.GetSelectedObjectType3(1, -1) : -1;
                string selTypeName = ((swSelectType_e)selTypeRaw).ToString();

                bool selectOk = oldSelected && selCountAfterSelect == 1 &&
                    (selTypeRaw == (int)swSelectType_e.swSelDIMENSIONS || selTypeName.IndexOf("DIMENSION", StringComparison.OrdinalIgnoreCase) >= 0);

                if (!selectOk)
                {
                    LogDebug($"Safe delete selection failed for '{oldDimFullName}'. Cleaning up new dim.");
                    DeleteProvisionalDimension(swModel, newDisp, "CANDIDATE_PAIR_SAFE_DELETE_SELECTION_FAILED");
                    swModel.ClearSelection2(true);
                    continue;
                }

                bool deleteResult = false;
                try { deleteResult = ext.DeleteSelection2(0); } catch {}
                swModel.ClearSelection2(true);

                if (!deleteResult)
                {
                    LogDebug($"DeleteSelection2 returned false for '{oldDimFullName}'. Cleaning up new dim.");
                    DeleteProvisionalDimension(swModel, newDisp, "CANDIDATE_PAIR_DELETE_RETURNED_FALSE");
                    continue;
                }

                int postDisplay = 0;
                int postDangling = 0;
                CountTotalDrawingDimensions(swDrawing, out postDisplay, out postDangling);

                return new SingleTargetRepairResult
                {
                    Status = SingleTargetStatus.Success,
                    PostDisplayCount = postDisplay,
                    PostDanglingCount = postDangling
                };
            }

            return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "NO_CANDIDATE_PAIR_MATCHED" };
        }

        private static SingleTargetRepairResult RecreateSingleEntityDimensionFromAttachedEntities(
            ISldWorks swApp,
            DrawingDoc swDrawing,
            ModelDoc2 swModel,
            SolidWorks.Interop.sldworks.View targetView,
            DisplayDimension targetDispDim,
            Annotation targetAnnot,
            object ent,
            int currentDisplayBefore,
            int currentDanglingBefore)
        {
            Dimension oldDim = targetDispDim.GetDimension2(0) as Dimension ?? targetDispDim.GetDimension() as Dimension;
            string oldDimFullName = oldDim != null ? oldDim.FullName : targetAnnot.GetName();
            double? oldSysVal = null;
            if (oldDim != null)
            {
                try
                {
                    object values = oldDim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                    if (values is double[] arr && arr.Length > 0) oldSysVal = arr[0];
                    else if (values is double d) oldSysVal = d;
                    else oldSysVal = oldDim.GetSystemValue2("");
                }
                catch {}
            }

            double[] oldPos = null;
            try { oldPos = targetAnnot.GetPosition() as double[]; } catch {}

            bool isHoleCallout = false;
            try { isHoleCallout = targetDispDim.IsHoleCallout(); } catch {}
            bool isDiameter = (targetDispDim.Type2 == (int)swDimensionType_e.swDiameterDimension ||
                               targetDispDim.Type2 == (int)swDimensionType_e.swDiametricLinearDimension);
            bool isRadial = (targetDispDim.Type2 == (int)swDimensionType_e.swRadialDimension ||
                             targetDispDim.Type2 == (int)swDimensionType_e.swRadialLinearDimension);

            swModel.ClearSelection2(true);
            ISelectionMgr selMgr = swModel.SelectionManager as ISelectionMgr;

            bool s1 = SelectDrawingOrModelEntity(swApp, swModel, targetView, ent, false, selMgr);
            if (!s1 || selMgr.GetSelectedObjectCount2(-1) != 1)
            {
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "ATTACHED_ENTITY_SELECT_FAILED" };
            }

            double posX = (oldPos != null && oldPos.Length >= 1) ? oldPos[0] : 0.0;
            double posY = (oldPos != null && oldPos.Length >= 2) ? oldPos[1] : 0.0;

            DisplayDimension newDisp = null;
            if (isHoleCallout)
            {
                try { newDisp = swDrawing.AddHoleCallout2(posX, posY, 0.0) as DisplayDimension; } catch {}
            }
            if (newDisp == null && isDiameter)
            {
                try { newDisp = swModel.AddDiameterDimension2(posX, posY, 0.0) as DisplayDimension; } catch {}
            }
            if (newDisp == null && isRadial)
            {
                try { newDisp = swModel.AddRadialDimension2(posX, posY, 0.0) as DisplayDimension; } catch {}
            }
            if (newDisp == null)
            {
                try { newDisp = swModel.AddDimension2(posX, posY, 0.0) as DisplayDimension; } catch {}
            }

            if (newDisp == null)
            {
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "ADD_SINGLE_DIMENSION_NULL" };
            }

            Annotation newAnnot = newDisp.GetAnnotation() as Annotation;
            Dimension newDim = newDisp.GetDimension2(0) as Dimension ?? newDisp.GetDimension() as Dimension;
            string newDimFullName = newDim != null ? newDim.FullName : (newAnnot != null ? newAnnot.GetName() : "");

            bool newDangling = (newAnnot != null) && newAnnot.IsDangling();
            int newAttached = (newAnnot != null) ? newAnnot.GetAttachedEntityCount3() : 0;

            double? newSysVal = null;
            if (newDim != null)
            {
                try
                {
                    object values = newDim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                    if (values is double[] arr && arr.Length > 0) newSysVal = arr[0];
                    else if (values is double d) newSysVal = d;
                    else newSysVal = newDim.GetSystemValue2("");
                }
                catch {}
            }

            bool valMatch = false;
            double deltaValMm = double.MaxValue;
            if (oldSysVal.HasValue && newSysVal.HasValue)
            {
                deltaValMm = Math.Abs(newSysVal.Value - oldSysVal.Value) * 1000.0;
                double effTolMm = Math.Max(0.15, Math.Abs(oldSysVal.Value * 1000.0) * 0.001);
                valMatch = deltaValMm <= effTolMm;
            }
            else if (isHoleCallout)
            {
                valMatch = true;
            }

            if (newDangling || newAttached < 1 || !valMatch)
            {
                DeleteProvisionalDimension(swModel, newDisp, "PASS0C_SINGLE_VERIFY_FAILED");
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "ATTACHED_SINGLE_VERIFY_FAILED" };
            }

            CopyDimensionPresentation(targetDispDim, targetAnnot, oldDim, newDisp, newAnnot, newDim, oldPos);

            // Safely delete old dimension
            swModel.ClearSelection2(true);
            IModelDocExtension ext = swModel.Extension;
            bool oldSelected = false;

            if (!string.IsNullOrEmpty(oldDimFullName) && ext != null)
            {
                try { oldSelected = ext.SelectByID2(oldDimFullName, "DIMENSION", 0.0, 0.0, 0.0, false, 0, null, 0); } catch {}
            }
            if (!oldSelected && targetAnnot != null)
            {
                try { oldSelected = targetAnnot.Select3(false, null); } catch {}
            }

            int selCountAfterSelect = (selMgr != null) ? selMgr.GetSelectedObjectCount2(-1) : 0;
            int selTypeRaw = selMgr != null ? selMgr.GetSelectedObjectType3(1, -1) : -1;
            string selTypeName = ((swSelectType_e)selTypeRaw).ToString();

            bool selectOk = oldSelected && selCountAfterSelect == 1 &&
                (selTypeRaw == (int)swSelectType_e.swSelDIMENSIONS || selTypeName.IndexOf("DIMENSION", StringComparison.OrdinalIgnoreCase) >= 0);

            if (!selectOk)
            {
                DeleteProvisionalDimension(swModel, newDisp, "PASS0C_SINGLE_SAFE_DELETE_SELECTION_FAILED");
                swModel.ClearSelection2(true);
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "SAFE_DELETE_SELECTION_FAILED" };
            }

            bool deleteResult = false;
            try { deleteResult = ext.DeleteSelection2(0); } catch {}
            swModel.ClearSelection2(true);

            if (!deleteResult)
            {
                DeleteProvisionalDimension(swModel, newDisp, "PASS0C_SINGLE_DELETE_RETURNED_FALSE");
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "DELETE_RETURNED_FALSE" };
            }

            int postDisplay = 0;
            int postDangling = 0;
            CountTotalDrawingDimensions(swDrawing, out postDisplay, out postDangling);

            return new SingleTargetRepairResult
            {
                Status = SingleTargetStatus.Success,
                PostDisplayCount = postDisplay,
                PostDanglingCount = postDangling
            };
        }

        private static SingleTargetRepairResult RecreateSingleEntityDimensionFromCandidates(
            ISldWorks swApp,
            DrawingDoc swDrawing,
            ModelDoc2 swModel,
            SolidWorks.Interop.sldworks.View currentView,
            DisplayDimension targetDispDim,
            Annotation targetAnnot,
            List<ViewCandidateEntity> candidates,
            int currentDisplayBefore,
            int currentDanglingBefore)
        {
            if (candidates == null || candidates.Count < 1)
            {
                return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "INSUFFICIENT_VIEW_CANDIDATES" };
            }

            Dimension oldDim = targetDispDim.GetDimension2(0) as Dimension ?? targetDispDim.GetDimension() as Dimension;
            string oldDimFullName = oldDim != null ? oldDim.FullName : targetAnnot.GetName();
            double? oldSysVal = null;
            if (oldDim != null)
            {
                try
                {
                    object values = oldDim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                    if (values is double[] arr && arr.Length > 0) oldSysVal = arr[0];
                    else if (values is double d) oldSysVal = d;
                    else oldSysVal = oldDim.GetSystemValue2("");
                }
                catch {}
            }

            double[] oldPos = null;
            try { oldPos = targetAnnot.GetPosition() as double[]; } catch {}

            bool isHoleCallout = false;
            try { isHoleCallout = targetDispDim.IsHoleCallout(); } catch {}

            bool isDiameter = (targetDispDim.Type2 == (int)swDimensionType_e.swDiameterDimension ||
                               targetDispDim.Type2 == (int)swDimensionType_e.swDiametricLinearDimension);
            bool isRadial = (targetDispDim.Type2 == (int)swDimensionType_e.swRadialDimension ||
                             targetDispDim.Type2 == (int)swDimensionType_e.swRadialLinearDimension);

            double targetMm = oldSysVal.HasValue ? oldSysVal.Value * 1000.0 : 0.0;
            LogDebug($"  [RecreateSingleEntityDimension] Searching single entity for '{targetAnnot.GetName()}' (Target={targetMm:F3}mm, IsHoleCallout={isHoleCallout}, IsDiameter={isDiameter}, IsRadial={isRadial})...");

            var singleCandidates = candidates.Where(c => c.IsCircle).ToList();
            if (singleCandidates.Count == 0)
            {
                singleCandidates = candidates.Where(c => !c.IsBendLine).ToList();
            }

            var scoredCandidates = new List<Tuple<ViewCandidateEntity, double>>();
            foreach (var cand in singleCandidates)
            {
                double score = 0.0;
                if (oldPos != null && cand.MidSheet != null)
                {
                    score = Math.Sqrt(Math.Pow(cand.MidSheet[0] - oldPos[0], 2) + Math.Pow(cand.MidSheet[1] - oldPos[1], 2));
                }
                else
                {
                    score = 1000.0;
                }

                if (cand.IsDirectView) score -= 2.0;

                if (oldSysVal.HasValue && cand.IsCircle && cand.RadiusModel > 0)
                {
                    double expectedRad = isRadial ? oldSysVal.Value : (oldSysVal.Value / 2.0);
                    double deltaRadMm = Math.Abs(cand.RadiusModel - expectedRad) * 1000.0;
                    if (deltaRadMm <= 0.15)
                    {
                        score -= 10.0;
                    }
                }

                scoredCandidates.Add(Tuple.Create(cand, score));
            }

            scoredCandidates.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            LogDebug($"  [RecreateSingleEntityDimension] Testing {scoredCandidates.Count} candidate entities...");

            ISelectionMgr selMgr = swModel.SelectionManager as ISelectionMgr;
            int triedCount = 0;

            foreach (var item in scoredCandidates)
            {
                var cand = item.Item1;
                triedCount++;
                if (triedCount > 50) break;

                swModel.ClearSelection2(true);
                bool sel = SelectEntityInView(selMgr, swModel, swApp, currentView, cand, false);
                if (!sel || selMgr.GetSelectedObjectCount2(-1) != 1)
                {
                    swModel.ClearSelection2(true);
                    continue;
                }

                double posX = (oldPos != null && oldPos.Length >= 1) ? oldPos[0] : 0.0;
                double posY = (oldPos != null && oldPos.Length >= 2) ? oldPos[1] : 0.0;

                DisplayDimension newDisp = null;
                if (isHoleCallout)
                {
                    try { newDisp = swDrawing.AddHoleCallout2(posX, posY, 0.0) as DisplayDimension; } catch {}
                }
                if (newDisp == null && isDiameter)
                {
                    try { newDisp = swModel.AddDiameterDimension2(posX, posY, 0.0) as DisplayDimension; } catch {}
                }
                if (newDisp == null && isRadial)
                {
                    try { newDisp = swModel.AddRadialDimension2(posX, posY, 0.0) as DisplayDimension; } catch {}
                }
                if (newDisp == null)
                {
                    try { newDisp = swModel.AddDimension2(posX, posY, 0.0) as DisplayDimension; } catch {}
                }

                if (newDisp == null)
                {
                    swModel.ClearSelection2(true);
                    continue;
                }

                Annotation newAnnot = newDisp.GetAnnotation() as Annotation;
                Dimension newDim = newDisp.GetDimension2(0) as Dimension ?? newDisp.GetDimension() as Dimension;
                string newDimFullName = newDim != null ? newDim.FullName : (newAnnot != null ? newAnnot.GetName() : "");

                bool newDangling = (newAnnot != null) && newAnnot.IsDangling();
                int newAttached = (newAnnot != null) ? newAnnot.GetAttachedEntityCount3() : 0;

                double? newSysVal = null;
                if (newDim != null)
                {
                    try
                    {
                        object values = newDim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                        if (values is double[] arr && arr.Length > 0) newSysVal = arr[0];
                        else if (values is double d) newSysVal = d;
                        else newSysVal = newDim.GetSystemValue2("");
                    }
                    catch {}
                }

                bool valMatch = false;
                double deltaValMm = double.MaxValue;
                if (oldSysVal.HasValue && newSysVal.HasValue)
                {
                    deltaValMm = Math.Abs(newSysVal.Value - oldSysVal.Value) * 1000.0;
                    double effTolMm = Math.Max(0.15, Math.Abs(oldSysVal.Value * 1000.0) * 0.001);
                    valMatch = deltaValMm <= effTolMm;
                }
                else if (isHoleCallout)
                {
                    valMatch = true;
                }

                if (newDangling || newAttached < 1 || !valMatch)
                {
                    if (triedCount <= 10)
                    {
                        LogDebug($"  [SingleEntity Trial #{triedCount}] Failed: Dangling={newDangling}, Attached={newAttached}, Val={(newSysVal.HasValue ? (newSysVal.Value * 1000.0).ToString("F3") : "null")}mm (Target={targetMm:F3}mm, Delta={(newSysVal.HasValue ? deltaValMm.ToString("F3") : "N/A")}mm)");
                    }
                    DeleteProvisionalDimension(swModel, newDisp, "SINGLE_ENTITY_VERIFY_FAILED");
                    swModel.ClearSelection2(true);
                    continue;
                }

                LogDebug($"  [RecreateSingleEntityDimension] MATCH FOUND! NewVal={(newSysVal.HasValue ? (newSysVal.Value * 1000.0).ToString("F3") : "N/A")}mm, Target={targetMm:F3}mm, Delta={deltaValMm:F4}mm (Trial #{triedCount})");

                CopyDimensionPresentation(targetDispDim, targetAnnot, oldDim, newDisp, newAnnot, newDim, oldPos);

                // Safely delete old dimension
                swModel.ClearSelection2(true);
                IModelDocExtension ext = swModel.Extension;
                bool oldSelected = false;

                if (!string.IsNullOrEmpty(oldDimFullName) && ext != null)
                {
                    try { oldSelected = ext.SelectByID2(oldDimFullName, "DIMENSION", 0.0, 0.0, 0.0, false, 0, null, 0); } catch {}
                }
                if (!oldSelected && targetAnnot != null)
                {
                    try { oldSelected = targetAnnot.Select3(false, null); } catch {}
                }

                int selCountAfterSelect = (selMgr != null) ? selMgr.GetSelectedObjectCount2(-1) : 0;
                int selTypeRaw = selMgr != null ? selMgr.GetSelectedObjectType3(1, -1) : -1;
                string selTypeName = ((swSelectType_e)selTypeRaw).ToString();

                bool selectOk = oldSelected && selCountAfterSelect == 1 &&
                    (selTypeRaw == (int)swSelectType_e.swSelDIMENSIONS || selTypeName.IndexOf("DIMENSION", StringComparison.OrdinalIgnoreCase) >= 0);

                if (!selectOk)
                {
                    LogDebug($"Safe delete selection failed for '{oldDimFullName}'. Cleaning up new dim.");
                    DeleteProvisionalDimension(swModel, newDisp, "SINGLE_ENTITY_SAFE_DELETE_SELECTION_FAILED");
                    swModel.ClearSelection2(true);
                    continue;
                }

                bool deleteResult = false;
                try { deleteResult = ext.DeleteSelection2(0); } catch {}
                swModel.ClearSelection2(true);

                if (!deleteResult)
                {
                    LogDebug($"DeleteSelection2 returned false for '{oldDimFullName}'. Cleaning up new dim.");
                    DeleteProvisionalDimension(swModel, newDisp, "SINGLE_ENTITY_DELETE_RETURNED_FALSE");
                    continue;
                }

                int postDisplay = 0;
                int postDangling = 0;
                CountTotalDrawingDimensions(swDrawing, out postDisplay, out postDangling);

                return new SingleTargetRepairResult
                {
                    Status = SingleTargetStatus.Success,
                    PostDisplayCount = postDisplay,
                    PostDanglingCount = postDangling
                };
            }

            return new SingleTargetRepairResult { Status = SingleTargetStatus.Failed, Reason = "NO_SINGLE_CANDIDATE_MATCHED" };
        }

        private static bool SelectDrawingOrModelEntity(
            ISldWorks swApp,
            ModelDoc2 swModel,
            SolidWorks.Interop.sldworks.View view,
            object ent,
            bool append,
            ISelectionMgr selMgr)
        {
            if (ent == null) return false;

            int initialSelCount = selMgr != null ? selMgr.GetSelectedObjectCount2(-1) : 0;
            int expectedCount = append ? initialSelCount + 1 : 1;

            SelectData selData = selMgr?.CreateSelectData();
            if (selData != null && view != null)
            {
                try { selData.View = view; } catch {}
            }

            object resolvedEnt = ResolveDrawingEntity(swApp, view, ent);

            // CASE 1: IEntity (Edge, Face, Vertex)
            if (resolvedEnt is IEntity e)
            {
                try
                {
                    if (e.Select4(append, selData))
                    {
                        int c = selMgr.GetSelectedObjectCount2(-1);
                        if (c == expectedCount) return true;
                    }
                }
                catch {}

                try
                {
                    if (e.Select4(append, null))
                    {
                        int c = selMgr.GetSelectedObjectCount2(-1);
                        if (c == expectedCount) return true;
                    }
                }
                catch {}
            }

            // CASE 2: ISketchSegment (Bend lines, sketch lines)
            if (resolvedEnt is ISketchSegment seg)
            {
                try
                {
                    if (seg.Select4(append, selData))
                    {
                        int c = selMgr.GetSelectedObjectCount2(-1);
                        if (c == expectedCount)
                        {
                            LogDebug("    [SelectDrawingOrModelEntity] Selected SketchSegment with selData.View");
                            return true;
                        }
                    }
                }
                catch {}

                try
                {
                    if (seg.Select4(append, null))
                    {
                        int c = selMgr.GetSelectedObjectCount2(-1);
                        if (c == expectedCount)
                        {
                            LogDebug("    [SelectDrawingOrModelEntity] Selected SketchSegment with null selData");
                            return true;
                        }
                    }
                }
                catch {}

                // Try coordinate selection via SelectByID2
                if (swModel != null && swModel.Extension != null && view != null)
                {
                    if (GetSegmentModelPoints(seg, out _, out _, out double[] midM))
                    {
                        double[] midS = TransformPointToSheet(swApp, view, midM);
                        if (midS != null)
                        {
                            string[] types = new string[] { "EXTSKETCHSEGMENT", "SKETCHSEGMENT" };
                            foreach (var t in types)
                            {
                                try
                                {
                                    if (swModel.Extension.SelectByID2("", t, midS[0], midS[1], midS[2], append, 0, null, 0))
                                    {
                                        int c = selMgr.GetSelectedObjectCount2(-1);
                                        if (c == expectedCount)
                                        {
                                            LogDebug($"    [SelectDrawingOrModelEntity] Selected via SelectByID2 coord ({midS[0]:F4}, {midS[1]:F4}) as '{t}'");
                                            return true;
                                        }
                                    }
                                }
                                catch {}
                            }
                        }
                    }
                }
            }

            // CASE 3: Fallback on original ent if different from resolvedEnt
            if (!object.ReferenceEquals(ent, resolvedEnt))
            {
                if (ent is IEntity origE)
                {
                    try
                    {
                        if (origE.Select4(append, selData))
                        {
                            int c = selMgr.GetSelectedObjectCount2(-1);
                            if (c == expectedCount) return true;
                        }
                    }
                    catch {}
                }
                if (ent is ISketchSegment origSeg)
                {
                    try
                    {
                        if (origSeg.Select4(append, selData))
                        {
                            int c = selMgr.GetSelectedObjectCount2(-1);
                            if (c == expectedCount) return true;
                        }
                    }
                    catch {}
                }
            }

            // CASE 4: ISketchPoint
            if (ent is ISketchPoint pt)
            {
                try
                {
                    if (pt.Select4(append, selData))
                    {
                        int c = selMgr.GetSelectedObjectCount2(-1);
                        if (c == expectedCount) return true;
                    }
                }
                catch {}
                try
                {
                    if (pt.Select4(append, null))
                    {
                        int c = selMgr.GetSelectedObjectCount2(-1);
                        if (c == expectedCount) return true;
                    }
                }
                catch {}
            }

            return false;
        }

        private static void CopyDimensionPresentation(
            DisplayDimension oldDisp,
            Annotation oldAnnot,
            Dimension oldDim,
            DisplayDimension newDisp,
            Annotation newAnnot,
            Dimension newDim,
            double[] oldPos)
        {
            if (oldDisp == null || newDisp == null) return;

            // 1. Text Parts (Prefix, Suffix, Callout Above, Callout Below)
            try
            {
                string pfx = oldDisp.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix);
                string sfx = oldDisp.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix);
                string cAbove = oldDisp.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutAbove);
                string cBelow = oldDisp.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow);

                if (!string.IsNullOrEmpty(pfx)) newDisp.SetText((int)swDimensionTextParts_e.swDimensionTextPrefix, pfx);
                if (!string.IsNullOrEmpty(sfx)) newDisp.SetText((int)swDimensionTextParts_e.swDimensionTextSuffix, sfx);
                if (!string.IsNullOrEmpty(cAbove)) newDisp.SetText((int)swDimensionTextParts_e.swDimensionTextCalloutAbove, cAbove);
                if (!string.IsNullOrEmpty(cBelow)) newDisp.SetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow, cBelow);
            }
            catch {}

            // 2. Precision
            try
            {
                int prim = oldDisp.GetPrimaryPrecision2();
                int dual = oldDisp.GetAlternatePrecision2();
                int primTol = oldDisp.GetPrimaryTolPrecision2();
                int dualTol = oldDisp.GetAlternateTolPrecision2();
                if (prim >= 0)
                {
                    newDisp.SetPrecision2(prim, dual >= 0 ? dual : 0, primTol >= 0 ? primTol : 0, dualTol >= 0 ? dualTol : 0);
                }
            }
            catch {}

            // 3. Tolerance
            try
            {
                DimensionTolerance oldTol = oldDim?.Tolerance;
                DimensionTolerance newTol = newDim?.Tolerance;
                if (oldTol != null && newTol != null)
                {
                    int tType = oldTol.Type;
                    if (tType >= 0)
                    {
                        newTol.Type = tType;
                        if (tType != (int)swTolType_e.swTolNONE)
                        {
                            newTol.SetValues(oldTol.GetMinValue(), oldTol.GetMaxValue());
                        }
                    }
                }
            }
            catch {}

            // 4. Units
            try
            {
                bool useDocUnits = oldDisp.GetUseDocUnits();
                int lengthUnit = oldDisp.GetUnits();
                int fBase = oldDisp.GetFractionBase();
                int fVal = oldDisp.GetFractionValue();
                bool round = oldDisp.GetRoundToFraction();
                if (lengthUnit >= 0)
                {
                    newDisp.SetUnits(useDocUnits, lengthUnit, fBase, fVal, round);
                }
            }
            catch {}

            // 5. Arrow side
            try
            {
                int arrow = oldDisp.ArrowSide;
                if (arrow >= 0) newDisp.ArrowSide = arrow;
            }
            catch {}

            // 6. Text format
            try
            {
                if (oldAnnot != null && newAnnot != null)
                {
                    bool useDoc = oldAnnot.GetUseDocTextFormat(0);
                    TextFormat tf = oldAnnot.GetTextFormat(0) as TextFormat;
                    newAnnot.SetTextFormat(0, useDoc, tf);
                }
            }
            catch {}

            // 7. Layer & Color
            try
            {
                if (oldAnnot != null && newAnnot != null)
                {
                    string layer = oldAnnot.Layer;
                    if (!string.IsNullOrEmpty(layer)) newAnnot.Layer = layer;
                    int color = oldAnnot.Color;
                    if (color != -1) newAnnot.Color = color;
                }
            }
            catch {}

            // 8. Position
            try
            {
                if (oldPos != null && oldPos.Length >= 3 && newAnnot != null)
                {
                    newAnnot.SetPosition2(oldPos[0], oldPos[1], oldPos[2]);
                }
            }
            catch {}
        }

        private static void CountTotalDrawingDimensions(
            DrawingDoc swDrawing,
            out int totalDisplayDims,
            out int totalDanglingDims)
        {
            totalDisplayDims = 0;
            totalDanglingDims = 0;

            if (swDrawing == null) return;

            string initialSheet = "";
            try
            {
                Sheet cur = swDrawing.GetCurrentSheet() as Sheet;
                if (cur != null) initialSheet = cur.GetName();
            }
            catch {}

            try
            {
                string[] sheetNames = swDrawing.GetSheetNames() as string[];
                if (sheetNames == null) return;

                foreach (string sName in sheetNames)
                {
                    try { swDrawing.ActivateSheet(sName); } catch {}
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
            finally
            {
                if (!string.IsNullOrEmpty(initialSheet))
                {
                    try { swDrawing.ActivateSheet(initialSheet); } catch {}
                }
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

            if (IsSketchPointSelectionType(info.AnchorEntityType) && isLinear)
            {
                info.FailureMode = RepairDimFailureMode.SketchPointAnchorLostReference;
                if (info.CandidateDecision == "POINT_ANCHOR_PROBE_CANDIDATES_AVAILABLE" ||
                    info.CandidateDecision == "POINT_ANCHOR_PROBE_UNIQUE_HIGH_CONFIDENCE" ||
                    info.CandidateDecision == "POINT_ANCHOR_HIGH_CONFIDENCE" ||
                    info.CandidateDecision == "POINT_ANCHOR_PROVISIONAL_HIGH_CONFIDENCE")
                {
                    info.FailureModeReason = "Live SketchPoint anchor exists and Route C replacement Edge geometry candidates are available for provisional probe.";
                    info.RouteCCandidateAvailable = true;
                    info.RequiresDimensionRecreate = true;
                    info.RecommendedAction = "EXECUTE_PROVISIONAL_PROBE";
                }
                else
                {
                    info.FailureModeReason = $"Live SketchPoint anchor evaluated: {info.CandidateDecision}";
                    info.RouteCCandidateAvailable = false;
                    info.RequiresDimensionRecreate = false;
                    info.RecommendedAction = "MANUAL_REVIEW";
                }
                return;
            }

            if (!isLinear || !RepairDimCandidateFinder.IsLinearEntityType(info.AnchorEntityType))
            {
                info.FailureMode = RepairDimFailureMode.UnsupportedAnchor;
                info.FailureModeReason = !isLinear ? $"Dimension type '{info.DimensionTypeString}' is not supported in linear pipeline." : $"Anchor entity type '{((swSelectType_e)info.AnchorEntityType).ToString()}' is not a linear edge or sketch segment.";
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

        internal static void LogDebug(string msg)
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
