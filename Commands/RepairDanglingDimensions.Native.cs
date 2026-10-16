using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SwView = SolidWorks.Interop.sldworks.View;

namespace ADDIN.Commands
{
    internal sealed class RepairDimReadLimitException : InvalidOperationException
    {
        internal RepairDimReadLimitException() : base("REPAIR DIM đã dừng tìm tham chiếu vì vượt thời gian xử lý. Các DIM chưa sửa được giữ lại để kiểm tra thủ công.") { }
    }
    internal sealed class NativeRepairDimensionTarget
    {
        internal ModelDoc2 Model;
        internal string SheetName, ViewName, DimensionName, AnnotationName, Description, DimensionType;
        internal int RemainingReferences;
        internal NativeRepairDimensionTarget Copy() { return (NativeRepairDimensionTarget)MemberwiseClone(); }
    }
    public static partial class RepairDanglingDimensions
    {
        [ThreadStatic] private static NativeRepairExecution nativeExecution;

        private sealed class NativeRepairExecution
        {
            internal readonly Stopwatch Elapsed = Stopwatch.StartNew();
            internal readonly TimeSpan Limit;
            internal readonly Dictionary<string, ViewGeometryInfo> Geometry = new Dictionary<string, ViewGeometryInfo>(StringComparer.Ordinal);
            internal readonly Dictionary<string, List<ViewCandidateEntity>> Entities = new Dictionary<string, List<ViewCandidateEntity>>(StringComparer.Ordinal);
            internal NativeRepairExecution(TimeSpan? limit = null) { Limit = limit ?? TimeSpan.FromSeconds(90); }
            internal bool CanContinue { get { return Elapsed.Elapsed < Limit; } }
        }

        internal static void CheckRepairReadBudget()
        {
            if (nativeExecution != null && !nativeExecution.CanContinue) throw new RepairDimReadLimitException();
        }

        private static bool RepairTrialsCanContinue()
        { return nativeExecution == null || nativeExecution.CanContinue; }

        private static ViewGeometryInfo GetRepairViewGeometry(ISldWorks app, SwView view, string prefix = null)
        {
            if (nativeExecution == null) return RepairDimCandidateFinder.EnumerateViewGeometry(app, view, prefix);
            CheckRepairReadBudget();
            string key = view.GetName2(); ViewGeometryInfo result;
            if (!nativeExecution.Geometry.TryGetValue(key, out result)) {
                LogDebug("GEOMETRY START: " + key);
                result = RepairDimCandidateFinder.EnumerateViewGeometry(app, view, prefix);
                CheckRepairReadBudget();
                nativeExecution.Geometry.Add(key, result);
                LogDebug("GEOMETRY END: " + key);
            }
            return result;
        }

        private static List<ViewCandidateEntity> GetRepairViewEntities(ISldWorks app, SwView view)
        {
            if (nativeExecution == null) return CollectViewCandidateEntities(app, view);
            CheckRepairReadBudget();
            string key = view.GetName2(); List<ViewCandidateEntity> result;
            if (!nativeExecution.Entities.TryGetValue(key, out result)) {
                result = CollectViewCandidateEntities(app, view);
                CheckRepairReadBudget();
                nativeExecution.Entities.Add(key, result);
            }
            return result;
        }

        private sealed class RepairObjectComparer : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }

        // Native arrays avoid an iterator being reset by another traversal or by DIM creation.
        // The fallback is bounded and detects cycles using COM identity, not DIM names.
        private static List<T> ReadRepairChain<T>(T first, Func<T, T> next) where T : class
        {
            var result = new List<T>();
            var native = new HashSet<IntPtr>();
            var managed = new HashSet<object>(new RepairObjectComparer());
            var elapsed = Stopwatch.StartNew();
            for (var item = first; item != null; item = next(item)) {
                bool added;
                if (Marshal.IsComObject(item)) {
                    IntPtr identity = Marshal.GetIUnknownForObject(item);
                    try { added = native.Add(identity); } finally { Marshal.Release(identity); }
                } else added = managed.Add(item);
                if (!added || result.Count >= 100000 || elapsed.Elapsed > TimeSpan.FromSeconds(30))
                    throw new InvalidOperationException("Không đọc được danh sách View/DIM hữu hạn. REPAIR DIM đã dừng; hãy rebuild Drawing rồi quét lại.");
                result.Add(item);
            }
            return result;
        }

        internal static List<SwView> ReadCurrentRepairViews(DrawingDoc drawing)
        {
            var sheet = drawing.GetCurrentSheet() as Sheet;
            Array values = sheet == null ? null : sheet.GetViews() as Array;
            if (values != null) return values.Cast<object>().OfType<SwView>()
                .Where(v => v.Type != (int)swDrawingViewTypes_e.swDrawingSheet).ToList();
            var first = drawing.GetFirstView() as SwView;
            return ReadRepairChain(first == null ? null : first.GetNextView() as SwView, v => v.GetNextView() as SwView);
        }

        internal static List<DisplayDimension> ReadRepairDimensions(SwView view)
        {
            Array values = view.GetDisplayDimensions() as Array;
            if (values != null) return values.Cast<object>().OfType<DisplayDimension>().ToList();
            return ReadRepairChain(view.GetFirstDisplayDimension5() as DisplayDimension, d => d.GetNext5() as DisplayDimension);
        }

        private static void ActivateRepairSheet(DrawingDoc drawing, string name)
        {
            var current = drawing.GetCurrentSheet() as Sheet;
            if (current != null && string.Equals(current.GetName(), name, StringComparison.Ordinal)) return;
            if (nativeExecution != null) { nativeExecution.Geometry.Clear(); nativeExecution.Entities.Clear(); }
            if (!drawing.ActivateSheet(name)) throw new InvalidOperationException("Không mở được Sheet: " + name);
        }
        internal static bool NativeIdentityMatches(string view, string fullName, string annotation, string expectedView, string expectedFull, string expectedAnnotation)
        { return string.Equals(view, expectedView, StringComparison.Ordinal) &&
            (!string.IsNullOrEmpty(expectedFull) ? string.Equals(fullName, expectedFull, StringComparison.Ordinal) :
             !string.IsNullOrEmpty(expectedAnnotation) && string.Equals(annotation, expectedAnnotation, StringComparison.Ordinal)); }

        internal static List<NativeRepairDimensionTarget> ScanNative(ISldWorks app, SwView scope)
        {
            var model = app.ActiveDoc as ModelDoc2; var drawing = model as DrawingDoc;
            if (drawing == null) throw new InvalidOperationException("Hãy mở Drawing.");
            var sheet = drawing.GetCurrentSheet() as Sheet; if (sheet == null) throw new InvalidOperationException("Không đọc được Sheet.");
            var result = new List<NativeRepairDimensionTarget>(); bool foundScope = scope == null;
            foreach (var view in ReadCurrentRepairViews(drawing)) {
                if (scope != null && !ReferenceEquals(scope, view) && app.IsSame(scope, view) != (int)swObjectEquality.swObjectSame) continue;
                foundScope = true;
                foreach (var dd in ReadRepairDimensions(view)) {
                    var annotation = dd.GetAnnotation() as Annotation; if (annotation == null || !annotation.IsDangling()) continue;
                    var dimension = dd.GetDimension2(0) as Dimension ?? dd.GetDimension() as Dimension;
                    string fullName = dimension == null ? "" : dimension.FullName ?? "", name = annotation.GetName() ?? "";
                    if (fullName.Length == 0 && name.Length == 0) continue;
                    var info = ExtractDanglingInfo(sheet.GetName(), view.GetName2(), dd, annotation);
                    result.Add(new NativeRepairDimensionTarget { Model = model, SheetName = sheet.GetName(), ViewName = view.GetName2(), DimensionName = fullName,
                        AnnotationName = name, DimensionType = info.DimensionType.ToString(), RemainingReferences = info.AttachedEntityCount, Description = view.GetName2() + " | " + (fullName.Length > 0 ? fullName : name) + "\nLoại: " + info.DimensionType + " | tham chiếu còn: " + info.AttachedEntityCount });
                }
            }
            if (!foundScope) throw new InvalidOperationException("View không thuộc Sheet đang mở.");
            // A duplicate identity is unsafe to repair by name; leave it for manual review.
            return result.GroupBy(r => r.ViewName + "\n" + (string.IsNullOrEmpty(r.DimensionName) ? r.AnnotationName : r.DimensionName), StringComparer.Ordinal)
                .Where(g => g.Count() == 1).Select(g => g.First()).ToList();
        }
        private static bool ReacquireNative(DrawingDoc drawing, NativeRepairDimensionTarget target, out SwView foundView, out DisplayDimension foundDimension, out Annotation foundAnnotation)
        {
            foundView = null; foundDimension = null; foundAnnotation = null;
            var sheet = drawing.GetCurrentSheet() as Sheet; if (sheet == null || sheet.GetName() != target.SheetName) return false;
            int count = 0;
            foreach (var view in ReadCurrentRepairViews(drawing)) {
                if (!string.Equals(view.GetName2(), target.ViewName, StringComparison.Ordinal)) continue;
                foreach (var dd in ReadRepairDimensions(view)) {
                    var a = dd.GetAnnotation() as Annotation; if (a == null || !a.IsDangling()) continue;
                    var d = dd.GetDimension2(0) as Dimension ?? dd.GetDimension() as Dimension;
                    if (!NativeIdentityMatches(view.GetName2(), d == null ? "" : d.FullName, a.GetName(), target.ViewName, target.DimensionName, target.AnnotationName)) continue;
                    count++; foundView = view; foundDimension = dd; foundAnnotation = a;
                }
            }
            return count == 1;
        }
        internal static void RunNativeSelected(ISldWorks app, List<NativeRepairDimensionTarget> targets)
        {
            var model = app.ActiveDoc as ModelDoc2; var drawing = model as DrawingDoc;
            if (drawing == null || targets == null || targets.Count == 0) throw new InvalidOperationException("Chưa chọn DIM cần sửa.");
            var sheet = drawing.GetCurrentSheet() as Sheet; string sheetName = sheet == null ? "" : sheet.GetName();
            if (targets.Any(t => t.SheetName != sheetName || (!ReferenceEquals(t.Model, model) && app.IsSame(t.Model, model) != (int)swObjectEquality.swObjectSame)))
                throw new InvalidOperationException("Drawing / Sheet thay đổi; hãy quét lại.");
            if (nativeExecution != null) throw new InvalidOperationException("REPAIR DIM đang chạy; hãy chờ lệnh kết thúc.");
            var uniqueTargets = targets.GroupBy(t => t.ViewName + "\n" + t.DimensionName + "\n" + t.AnnotationName).Select(g => g.First()).ToList();
            bool previousCommand = app.CommandInProgress;
            int preference = (int)swUserPreferenceToggle_e.swInputDimValOnCreate;
            bool previousInput = app.GetUserPreferenceToggle(preference);
            int repaired = 0, review = 0, processed = 0; bool unsafeState = false; string stopped = null;
            InitLog();
            var execution = new NativeRepairExecution(); nativeExecution = execution;
            try {
                app.CommandInProgress = true;
                // AddDimension2 can otherwise wait indefinitely for its value-entry dialog.
                app.SetUserPreferenceToggle(preference, false);
                if (app.GetUserPreferenceToggle(preference)) throw new InvalidOperationException("Không tắt được hộp nhập giá trị DIM; chưa chạy Repair Dim.");
                int display = 0, dangling = 0; bool counted = false;
                LogDebug("NATIVE REPAIR START: targets=" + uniqueTargets.Count);
                foreach (var target in uniqueTargets) {
                    CheckRepairReadBudget();
                    LogDebug("TARGET START: " + target.ViewName + " | " + target.DimensionName);
                    SwView view; DisplayDimension dd; Annotation annotation;
                    if (!ReacquireNative(drawing, target, out view, out dd, out annotation)) { review++; processed++; continue; }
                    string path = view.GetReferencedModelName() ?? "";
                    if (!string.IsNullOrEmpty(path) && IsValidSolidWorksFilePath(path) && !File.Exists(path)) { review++; processed++; continue; }
                    if (!counted) {
                        CountTotalDrawingDimensions(drawing, out display, out dangling); counted = true;
                        if (!ReacquireNative(drawing, target, out view, out dd, out annotation)) { review++; processed++; continue; }
                        LogDebug("BASELINE: display=" + display + ", dangling=" + dangling);
                    }
                    var result = RepairNativeTarget(app, drawing, model, target, view, dd, annotation, path, processed + 1, uniqueTargets.Count, display, dangling);
                    processed++;
                    if (result != null && result.Status == SingleTargetStatus.Success) {
                        repaired++; display = result.PostDisplayCount; dangling = result.PostDanglingCount;
                    } else {
                        review++;
                        // Never continue a batch with a provisional DIM left behind.
                        if (result != null) {
                            int afterDisplay, afterDangling; CountTotalDrawingDimensions(drawing, out afterDisplay, out afterDangling);
                            if (afterDisplay != display || afterDangling != dangling) { unsafeState = true; break; }
                        }
                    }
                    LogDebug("TARGET END: " + (result == null ? "MANUAL_REVIEW" : result.Status + " | " + result.Reason));
                    if (result != null && result.IsUnsafeState) { unsafeState = true; break; }
                }
            } catch (RepairDimReadLimitException ex) {
                stopped = ex.Message; LogDebug("NATIVE REPAIR STOP: " + stopped);
            } finally {
                nativeExecution = null;
                try {
                    try { ActivateRepairSheet(drawing, sheetName); }
                    finally { try { model.ClearSelection2(true); } finally { model.GraphicsRedraw2(); } }
                } finally {
                    try { app.SetUserPreferenceToggle(preference, previousInput); }
                    finally { app.CommandInProgress = previousCommand; }
                }
                LogDebug("NATIVE REPAIR END: repaired=" + repaired + ", review=" + review + ", unprocessed=" + (uniqueTargets.Count - processed) + ", seconds=" + execution.Elapsed.Elapsed.TotalSeconds.ToString("F2"));
            }
            if (stopped == null && !execution.CanContinue) stopped = new RepairDimReadLimitException().Message;
            app.SendMsgToUser2("Đã sửa: " + repaired + ". Cần kiểm tra / bỏ qua: " + (review + uniqueTargets.Count - processed) + "." +
                (stopped == null ? "" : "\n" + stopped) +
                (unsafeState ? "\nDừng vì rollback không được xác nhận; kiểm tra bản vẽ trước khi tiếp tục." : ""),
                (int)swMessageBoxIcon_e.swMbInformation, (int)swMessageBoxBtn_e.swMbOk);
        }

        private static SingleTargetRepairResult RepairNativeTarget(ISldWorks app, DrawingDoc drawing, ModelDoc2 model,
            NativeRepairDimensionTarget target, SwView view, DisplayDimension dd, Annotation annotation, string path,
            int number, int total, int display, int dangling)
        {
            var nativeLinearRepair = TryRepairLinearWithNativeReattach(app, drawing, model, target, view, dd, annotation, display, dangling);
            if (nativeLinearRepair != null) return nativeLinearRepair;

            object[] entities = annotation.GetAttachedEntities3() as object[];
            if (IsSingleEntityDimension(dd)) {
                if (entities != null && entities.Length > 0 && entities[0] != null)
                    return RecreateSingleEntityDimensionFromAttachedEntities(app, drawing, model, view, dd, annotation, entities[0], display, dangling);
                return RecreateSingleEntityDimensionFromCandidates(app, drawing, model, view, dd, annotation, GetRepairViewEntities(app, view), display, dangling);
            }
            if (entities != null && entities.Length >= 2 && entities[0] != null && entities[1] != null)
                return RecreateDimensionFromAttachedEntities(app, drawing, model, view, dd, annotation, entities[0], entities[1], display, dangling);

            var info = ExtractDanglingInfo(target.SheetName, target.ViewName, dd, annotation);
            bool linear = info.DimensionType == swDimensionType_e.swLinearDimension || info.DimensionType == swDimensionType_e.swHorLinearDimension || info.DimensionType == swDimensionType_e.swVertLinearDimension;
            if (!linear) return null;
            var geometry = GetRepairViewGeometry(app, view);
            RepairDimCandidateFinder.AnalyzeCandidatesForDimension(app, info, geometry, view, dd);
            CheckRepairReadBudget();
            ClassifyFailureMode(info, geometry, true, path);
            var snapshot = new BatchTargetSnapshot { ExactIdentityOnly = true, SheetName = target.SheetName, ViewName = target.ViewName, DimensionName = target.AnnotationName,
                OldDimFullName = target.DimensionName, DimensionType = info.DimensionType, SystemValue = info.SystemValue, Position = info.Position,
                AttachedEntityTypes = info.AttachedEntityTypes, FailureMode = info.FailureMode, CandidateDecision = info.CandidateDecision,
                AnchorOccurrenceKey = info.AnchorOccurrenceKey, CandidateOccurrenceKey = info.Candidates.Count > 0 ? info.Candidates[0].ComponentOccurrenceKey : null };
            if (IsSketchPointSelectionType(info.AnchorEntityType) && info.AttachedEntityCount >= 2 && info.AttachedEntityTypes.Contains(0))
                return ExecuteSinglePointAnchorTargetRepair(app, drawing, model, snapshot, display, dangling);
            if (info.CandidateDecision == "HIGH_CONFIDENCE" && info.FailureMode == RepairDimFailureMode.ComponentReinsertedOrGeometryReplaced)
                return ExecuteSingleStep10TargetRepair(app, drawing, model, snapshot, number, total, display, dangling);
            if (info.FailureMode == RepairDimFailureMode.FullyLostReference || info.CandidateDecision == "DEFERRED_FULLY_LOST" ||
                info.FailureMode == RepairDimFailureMode.GeometryChangedNoCandidate || info.CandidateDecision == "NO_CANDIDATE_DIAGNOSTIC" || info.CandidateDecision == "GEOMETRY_UNVERIFIED")
                return ExecuteSingleFullyLostTargetRepair(app, drawing, model, snapshot, number, total, display, dangling);
            return null;
        }

        private static SingleTargetRepairResult TryRepairLinearWithNativeReattach(ISldWorks app, DrawingDoc drawing, ModelDoc2 model,
            NativeRepairDimensionTarget target, SwView view, DisplayDimension dd, Annotation annotation, int display, int dangling)
        {
            var info = ExtractDanglingInfo(target.SheetName, target.ViewName, dd, annotation);
            bool linear = info.DimensionType == swDimensionType_e.swLinearDimension ||
                info.DimensionType == swDimensionType_e.swHorLinearDimension ||
                info.DimensionType == swDimensionType_e.swVertLinearDimension;
            if (!linear) return null;

            var geometry = GetRepairViewGeometry(app, view);
            RepairDimCandidateFinder.AnalyzeCandidatesForDimension(app, info, geometry, view, dd);
            CheckRepairReadBudget();

            var points = new List<double[]>();
            if (info.AnchorReferenceIndex < 0)
            {
                var pair = RepairDimCandidateFinder.FindFullyLostPairCandidate(app, info, geometry, view, dd);
                if (pair == null || pair.BestPair == null ||
                    (pair.Decision != "FULLY_LOST_HIGH_CONFIDENCE" && pair.Decision != "BROKEN_VIEW_PROVISIONAL_HIGH_CONFIDENCE"))
                    return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "NATIVE_REATTACH_NO_UNIQUE_PAIR" };
                points.Add(pair.BestPair.AttachPoint1);
                points.Add(pair.BestPair.AttachPoint2);
            }
            else
            {
                if (info.CandidateDecision != "HIGH_CONFIDENCE" || info.Candidates == null || info.Candidates.Count == 0)
                    return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "NATIVE_REATTACH_NO_UNIQUE_EDGE" };
                var profile = RepairDimGeometry.BuildDisplayWitnessProfile(info.DisplayLineSegments, info.Position);
                if (profile == null || !profile.IsValid)
                    return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "NATIVE_REATTACH_NO_WITNESS_PROFILE" };

                var candidate = info.Candidates[0];
                var edge = geometry.RepairLineRecords.FirstOrDefault(x => x.RawRecordIndex == candidate.RawRecordIndex);
                if (edge == null || edge.SheetStart == null || edge.SheetEnd == null)
                    return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "NATIVE_REATTACH_EDGE_NOT_FOUND" };
                var witness1 = RepairDimGeometry.ClosestPointOnSegment2D(profile.Witness1GeometryPoint[0], profile.Witness1GeometryPoint[1],
                    edge.SheetStart[0], edge.SheetStart[1], edge.SheetEnd[0], edge.SheetEnd[1]);
                var witness2 = RepairDimGeometry.ClosestPointOnSegment2D(profile.Witness2GeometryPoint[0], profile.Witness2GeometryPoint[1],
                    edge.SheetStart[0], edge.SheetStart[1], edge.SheetEnd[0], edge.SheetEnd[1]);
                if (Math.Abs(witness1.DistanceMm - witness2.DistanceMm) <= RepairDimGeometry.AbsoluteDistanceToleranceMm)
                    return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "NATIVE_REATTACH_WITNESS_SIDE_AMBIGUOUS" };
                var selectedPoint = witness1.DistanceMm < witness2.DistanceMm ? witness1.Point : witness2.Point;
                if (selectedPoint == null || selectedPoint.Length < 2 || Math.Min(witness1.DistanceMm, witness2.DistanceMm) > 3.0)
                    return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "NATIVE_REATTACH_WITNESS_TOO_FAR" };
                points.Add(selectedPoint);
            }

            if (points.Count == 0 || points.Any(p => p == null || p.Length < 2 ||
                double.IsNaN(p[0]) || double.IsNaN(p[1]) || double.IsInfinity(p[0]) || double.IsInfinity(p[1])))
                return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "NATIVE_REATTACH_INVALID_POINT" };

            Dimension dimension = dd.GetDimension2(0) as Dimension ?? dd.GetDimension() as Dimension;
            string oldFullName = dimension == null ? "" : (dimension.FullName ?? "");
            string oldAnnotationName = annotation.GetName() ?? "";
            double? oldValue = info.SystemValue;
            bool previousCommand = app.CommandInProgress;
            bool attempted = false;
            bool attached = false;
            string failure = "NATIVE_REATTACH_NOT_CONFIRMED";
            try
            {
                // Reattach is a native UI command. Temporarily release the batch command guard
                // so SOLIDWORKS can enter the same command as the context-menu action.
                app.CommandInProgress = false;
                model.ClearSelection2(true);
                if (!annotation.Select3(false, null) || !IsNativeDimensionSelected(model))
                    return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "NATIVE_REATTACH_DIM_SELECTION_FAILED" };
                attempted = app.RunCommand(3521, "");
                if (!attempted) return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = "NATIVE_REATTACH_COMMAND_NOT_STARTED" };
                LogDebug("NATIVE REATTACH START: " + target.DimensionName + ", points=" + points.Count);

                var modelView = model.ActiveView as ModelView;
                var mouse = modelView == null ? null : modelView.GetMouse() as IMouse;
                if (mouse == null) failure = "NATIVE_REATTACH_MOUSE_UNAVAILABLE";
                else
                {
                    foreach (var point in points)
                    {
                        CheckRepairReadBudget();
                        bool down = mouse.MoveXYZ(point[0], point[1], point.Length > 2 ? point[2] : 0.0, 2);
                        System.Threading.Thread.Sleep(15);
                        bool up = mouse.MoveXYZ(point[0], point[1], point.Length > 2 ? point[2] : 0.0, 4);
                        if (!down || !up) { failure = "NATIVE_REATTACH_CLICK_FAILED"; break; }
                        PumpRepairMessages(120);
                        if (!annotation.IsDangling()) { attached = true; break; }
                    }
                }

                if (attached)
                {
                    model.ForceRebuild3(false);
                    var verified = dd.GetAnnotation() as Annotation;
                    var verifiedDim = dd.GetDimension2(0) as Dimension ?? dd.GetDimension() as Dimension;
                    bool identityPreserved = verified != null && verifiedDim != null &&
                        string.Equals(verified.GetName() ?? "", oldAnnotationName, StringComparison.Ordinal) &&
                        (string.IsNullOrEmpty(oldFullName) || string.Equals(verifiedDim.FullName ?? "", oldFullName, StringComparison.Ordinal));
                    bool valuePreserved = true;
                    if (oldValue.HasValue && verifiedDim != null)
                    {
                        object values = verifiedDim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null);
                        double newValue = values is double[] array && array.Length > 0 ? array[0] : verifiedDim.GetSystemValue2("");
                        double tolerance = Math.Max(0.00015, Math.Abs(oldValue.Value) * 0.001);
                        valuePreserved = Math.Abs(newValue - oldValue.Value) <= tolerance;
                    }
                    attached = verified != null && !verified.IsDangling() && identityPreserved && valuePreserved;
                    if (!attached) failure = "NATIVE_REATTACH_POSTCHECK_FAILED";
                }
            }
            catch (RepairDimReadLimitException) { throw; }
            catch (Exception ex) { failure = "NATIVE_REATTACH_ERROR: " + ex.Message; }
            finally
            {
                if (attempted)
                {
                    try { app.RunCommand(3043, ""); } catch { }
                    try { PumpRepairMessages(50); } catch { }
                }
                try { model.ClearSelection2(true); } finally { app.CommandInProgress = previousCommand; }
            }

            if (!attached) return new SingleTargetRepairResult { Status = SingleTargetStatus.ManualReview, Reason = failure };
            int postDisplay, postDangling;
            CountTotalDrawingDimensions(drawing, out postDisplay, out postDangling);
            return new SingleTargetRepairResult { Status = SingleTargetStatus.Success, PostDisplayCount = postDisplay, PostDanglingCount = postDangling };
        }

        private static bool IsNativeDimensionSelected(ModelDoc2 model)
        {
            var selection = model.SelectionManager as SelectionMgr;
            return selection != null && selection.GetSelectedObjectCount2(-1) == 1 &&
                selection.GetSelectedObjectType3(1, -1) == (int)swSelectType_e.swSelDIMENSIONS;
        }

        private static void PumpRepairMessages(int milliseconds)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < milliseconds)
            {
                System.Windows.Forms.Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
        }
    }
}
