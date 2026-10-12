using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal sealed partial class Repair3DSplineCommand
    {
        private const double DefaultSpacing = 0.100; // SolidWorks uses metres.
        private const int MinPointCount = 3;
        private const int MaxPointCount = 100;
        private const double PointTolerance = 1e-7;
        private const double ApproximationTolerance = 0.0002; // 0.20 mm
        private const double SpacingTolerance = 0.0005; // 0.50 mm
        private const double FitPointReuseTolerance = 0.00025; // 0.25 mm
        private const int DenseValidationSamples = 401;
        private readonly ISldWorks app;
        private readonly Action<string, bool> report;

        internal Repair3DSplineCommand(ISldWorks app, Action<string, bool> report = null)
        { this.app = app; this.report = report; }

        public void Run(IWin32Window owner)
        {
            ModelDoc2 model = null;
            Sketch sketch = null;
            int[] sourceId = null;
            int originalCount = 0;
            bool recording = false;
            bool changed = false;
            int inserted = 0;
            int moved = 0;
            int removed = 0;
            List<double[]> originalFitPositions = null;
            Curve originalCurve = null;
            double curveStart = 0, curveEnd = 0;
            try
            {
                model = app == null ? null : app.ActiveDoc as ModelDoc2;
                Require(model != null, "Chưa có tài liệu đang mở.");
                Require(model.GetType() == (int)swDocumentTypes_e.swDocPART,
                    "Lệnh hỗ trợ Part có 3D Sketch.");
                SelectionMgr selection = (SelectionMgr)model.SelectionManager;
                int selectedCount = selection.GetSelectedObjectCount2(-1);
                Require(selectedCount == 1 || selectedCount == 2,
                    "Chọn Arc hoặc spline trong 3D Sketch; có thể chọn thêm Edge.");
                SketchSegment segment = null;
                Edge optionalEdge = null;
                for (int i = 1; i <= selectedCount; i++)
                {
                    object entity = selection.GetSelectedObject6(i, -1);
                    Log("Selected " + i + ": SketchSegment=" + (entity is SketchSegment) +
                        ", Edge=" + (entity is Edge));
                    if (entity is SketchSegment)
                    {
                        Require(segment == null, "Chỉ chọn một Arc hoặc spline.");
                        segment = (SketchSegment)entity;
                    }
                    else if (entity is Edge)
                    {
                        Require(optionalEdge == null, "Chỉ chọn tối đa một Edge.");
                        optionalEdge = (Edge)entity;
                    }
                    else throw new InvalidOperationException("Đối tượng chọn không phải Arc, spline hoặc Edge.");
                }
                Require(segment != null && (segment.GetType() == (int)swSketchSegments_e.swSketchSPLINE ||
                    segment.GetType() == (int)swSketchSegments_e.swSketchARC),
                    "Hãy chọn Arc hoặc spline trong 3D Sketch.");
                sketch = segment.GetSketch();
                Require(sketch != null && sketch.Is3D(), "Arc / spline phải thuộc 3D Sketch.");
                Require(model.SketchManager.ActiveSketch != null &&
                    (model.SketchManager.ActiveSketch == sketch ||
                        app.IsSame(model.SketchManager.ActiveSketch, sketch) == (int)swObjectEquality.swObjectSame),
                    "Hãy Edit 3D Sketch chứa Arc / spline rồi chạy lại.");
                if (segment.GetType() == (int)swSketchSegments_e.swSketchARC)
                {
                    RunArcFit(owner, model, sketch, segment);
                    return;
                }
                SketchSpline spline = (SketchSpline)segment;
                Require(!spline.IsStyleSpline,
                    "Style Spline không dùng fit point; hãy chuyển sang Spline thường.");
                originalCount = spline.GetPointCount();
                Require(originalCount >= 2 && originalCount <= MaxPointCount,
                    "Không đọc được số fit point hợp lệ của spline.");
                sourceId = SegmentId(segment);
                List<SketchRelation> originalRelations = Relations(segment);
                Log("Source=" + segment.GetName() + "; ID=" + string.Join(",", sourceId) +
                    "; FitPoints=" + originalCount +
                    "; Relations=" + RelationSummary(originalRelations) +
                    "; SelectedEdge=" + (optionalEdge != null));
                Curve sourceCurve = segment.GetCurve() as Curve;
                Require(sourceCurve != null, "Không lấy được Curve của spline.");
                originalCurve = sourceCurve.Copy() as Curve;
                Require(originalCurve != null,
                    "Không sao chép được Curve để kiểm tra hình dạng gốc.");
                bool closed, periodic;
                Require(originalCurve.GetEndParams(out curveStart, out curveEnd,
                    out closed, out periodic) && !closed && curveEnd > curveStart,
                    "Lệnh chỉ hỗ trợ spline mở.");
                double length = originalCurve.GetLength3(curveStart, curveEnd);
                int targetCount = CalculatePointCount(length);
                bool redistribute = originalCount >= MinPointCount;
                bool reduceExisting = originalCount > MinPointCount && originalRelations.Count == 0 &&
                    CanResetFreeHandles(spline);
                int desiredCount = reduceExisting ? MinPointCount : redistribute ? originalCount : targetCount;
                List<double[]> targets = GetPointsByArcLength(originalCurve,
                    curveStart, curveEnd, desiredCount);
                Log("SplineLength=" + (length * 1000).ToString("F3", CultureInfo.InvariantCulture) +
                    " mm; Mode=" + (redistribute ? "RedistributeBatch" : "Insert") +
                    "; PreferFewerFitPoints=" + reduceExisting + "; TargetFitPoints=" + desiredCount +
                    "; EqualSpacing=" + (length * 1000 / (desiredCount - 1))
                        .ToString("F3", CultureInfo.InvariantCulture) + " mm");
                originalFitPositions = SplineFitPoints(spline).Select(Position).ToList();
                List<SketchRelation> onEdgeRelations = originalRelations.Where(relation =>
                    relation.GetRelationType() == (int)swConstraintType_e.swConstraintType_USEEDGE).ToList();
                Require(onEdgeRelations.Count <= 1,
                    "Spline có nhiều On Edge relation; không thể kiểm tra bảo toàn tham chiếu.");
                Require(onEdgeRelations.Count == 0,
                    "Spline đang liên kết On Edge nên SOLIDWORKS không cho thêm fit point. " +
                    "Trong Edit 3D Sketch: chọn spline, xóa quan hệ On Edge trong Existing Relations, " +
                    "nhấp phải spline và chọn Convert to Spline (không chọn Style Spline). " +
                    "Sau đó chọn spline đã chuyển đổi và chạy lệnh lại. " +
                    "Hãy kiểm tra các feature phụ thuộc vì việc bỏ On Edge thay đổi tham chiếu của sketch.");
                bool resetFreeHandles = redistribute && originalRelations.Count == 0 &&
                    !SplineHandlesHaveControls(spline) &&
                    SplineFitPoints(spline).All(point => point.GetRelationsCount() == 0);
                if (redistribute)
                {
                    double initialSpacingError = MaxEqualSpacingError(spline, sourceCurve);
                    Log("InitialMaxSpacingError=" + (initialSpacingError * 1000)
                        .ToString("F4", CultureInfo.InvariantCulture) + " mm");
                    if (initialSpacingError <= SpacingTolerance && !reduceExisting)
                    {
                        ShowMessage(owner,
                            "Các fit point hiện có đã chia đều trong sai số 0.50 mm. " +
                            "Số điểm: " + originalCount + ".",
                            "REPAIR 3D SPLINE", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    Log("RedistributeBatch; ResetFreeHandles=" + resetFreeHandles);
                    model.Extension.StartRecordingUndoObject();
                    recording = true;
                    if (reduceExisting)
                    {
                        changed = true;
                        removed += RemoveInteriorFits(model, sketch, sourceId, originalCurve, desiredCount);
                        segment = RefreshSplineSegmentById(sketch, sourceId);
                        spline = (SketchSpline)segment;
                    }
                    if (resetFreeHandles)
                    {
                        changed = true;
                        spline.ResetAllHandles();
                    }
                    changed = true;
                    moved += RedistributeOnOriginalCurve(model, sketch, sourceId, originalCurve,
                        curveStart, curveEnd, length, targets, resetFreeHandles);
                    // Intermediate shapes are transient. Validate the completed batch
                    // against the original curve; the Undo group restores a failed batch.
                }
                else
                {
                    // A two-point spline needs new fit points; existing splines
                    // keep their original number of fit points.
                    for (int i = 1; i < desiredCount - 1; i++)
                    {
                        segment = RefreshSplineSegmentById(sketch, sourceId);
                        spline = (SketchSpline)segment;
                        Curve currentCurve = segment.GetCurve() as Curve;
                        Require(currentCurve != null, "Không lấy được Curve hiện tại của spline.");
                        double[] target = targets[i];
                        double[] onSpline = currentCurve.GetClosestPointOn(
                            target[0], target[1], target[2]) as double[];
                        Require(IsPoint(onSpline) && Distance(target, onSpline) <= ApproximationTolerance,
                            "Spline đã đổi hình dạng trước khi thêm fit point tiếp theo.");
                        if (SplineFitPoints(spline).Any(point =>
                            Distance(Position(point), onSpline) <= 1e-5))
                            continue;
                        if (!recording)
                        {
                            model.Extension.StartRecordingUndoObject();
                            recording = true;
                        }
                        int before = spline.GetPointCount();
                        changed = true;
                        bool accepted = TryInsertOnSameSpline(model, spline, before + 1, Log, onSpline);
                        Log("InsertPoint[" + i + "]=" + accepted +
                            "; before=" + before + "; after=" + spline.GetPointCount());
                        Require(accepted,
                            "SOLIDWORKS từ chối thêm fit point vào spline đã chọn.");
                        inserted++;
                        segment = RefreshSplineSegmentById(sketch, sourceId);
                        Require(((SketchSpline)segment).GetPointCount() == before + 1,
                            "InsertPoint báo thành công nhưng số fit point không tăng.");
                    }
                }
                Require(inserted + moved + removed > 0, "Không có fit point nào cần thay đổi.");
                bool rebuilt = model.EditRebuild3();
                Log("Rebuild after updating fit points=" + rebuilt);
                segment = RefreshSplineSegmentById(sketch, sourceId);
                spline = (SketchSpline)segment;
                DeviationResult deviation = MeasureDeviation(segment,
                    originalCurve, curveStart, curveEnd);
                Log("ShapeChangeAfterUpdate=" + (deviation.Distance * 1000)
                    .ToString("F4", CultureInfo.InvariantCulture) + " mm");
                if (redistribute)
                {
                    Log("TargetPositionErrorAfterUpdate=" +
                        (MaxTargetPositionError(spline, targets) * 1000)
                            .ToString("F4", CultureInfo.InvariantCulture) + " mm");
                    if (deviation.Distance > ApproximationTolerance && originalRelations.Count == 0 &&
                        !SplineHandlesHaveControls(spline))
                    {
                        Log("ResetAllHandles after moving all fit points");
                        spline.ResetAllHandles();
                        rebuilt = model.EditRebuild3();
                        segment = RefreshSplineSegmentById(sketch, sourceId);
                        spline = (SketchSpline)segment;
                        deviation = MeasureDeviation(segment, originalCurve, curveStart, curveEnd);
                        Log("ShapeChangeAfterResetHandles=" + (deviation.Distance * 1000)
                            .ToString("F4", CultureInfo.InvariantCulture) + " mm; Rebuild=" + rebuilt);
                    }
                }
                // Start with a small free fit and test EVERY count in order.
                // Native spline error is not monotonic in its point count.
                // Increase on the SAME entity until shape and spacing pass.
                while (redistribute && deviation.Distance > ApproximationTolerance &&
                    desiredCount < MaxPointCount)
                {
                    desiredCount++;
                    segment = RefreshSplineSegmentById(sketch, sourceId);
                    spline = (SketchSpline)segment;
                    Require(TryInsertOnSameSpline(model, spline, desiredCount, Log),
                        "Không thêm được fit point để giảm sai số hình dạng; đã yêu cầu Undo.");
                    inserted++;
                    targets = GetPointsByArcLength(originalCurve, curveStart, curveEnd, desiredCount);
                    moved += RedistributeOnOriginalCurve(model, sketch, sourceId, originalCurve,
                        curveStart, curveEnd, length, targets, resetFreeHandles, true);
                    rebuilt = model.EditRebuild3();
                    segment = RefreshSplineSegmentById(sketch, sourceId);
                    spline = (SketchSpline)segment;
                    deviation = MeasureDeviation(segment, originalCurve, curveStart, curveEnd);
                    Log("AdaptiveEqualFit; FitPoints=" + desiredCount + "; ShapeChange=" +
                        (deviation.Distance * 1000).ToString("F4", CultureInfo.InvariantCulture) + " mm");
                }
                Require(deviation.Distance <= ApproximationTolerance,
                    "Sau khi chia đều fit point, hình dạng spline lệch " +
                    (deviation.Distance * 1000).ToString("F4", CultureInfo.InvariantCulture) +
                    " mm, vượt giới hạn 0.20 mm; đã yêu cầu Undo.");
                if (redistribute)
                {
                    Require(spline.GetPointCount() == desiredCount,
                        "Số fit point không khớp với phương án chia đều đã kiểm tra.");
                    double targetError = MaxTargetPositionError(spline, targets);
                    Log("FinalTargetPositionError=" + (targetError * 1000)
                        .ToString("F4", CultureInfo.InvariantCulture) + " mm");
                    Require(targetError <= FitPointReuseTolerance,
                        "Fit point đã lệch khỏi các vị trí chia đều trên đường gốc.");
                    double finalSpacingError = MaxEqualSpacingError(spline,
                        segment.GetCurve() as Curve);
                    Log("FinalMaxSpacingError=" + (finalSpacingError * 1000)
                        .ToString("F4", CultureInfo.InvariantCulture) + " mm");
                    Require(finalSpacingError <= SpacingTolerance,
                        "Sau khi chia lại fit point, khoảng cách chưa đều trong sai số 0.50 mm.");
                }
                bool finishedUndo = model.Extension.FinishRecordingUndoObject2("DISTRIBUTE FIT POINTS IN 3D SPLINE", false);
                recording = false;
                Log("UndoGroupAvailable=" + finishedUndo);
                model.GraphicsRedraw2();
                Log("InsertedFitPoints=" + inserted + "; RemovedFitPoints=" + removed +
                    "; FinalFitPoints=" + spline.GetPointCount() +
                    "; MaxShapeChange=" + (deviation.Distance * 1000).ToString("F4", CultureInfo.InvariantCulture) + " mm");
                ShowMessage(owner,
                    (redistribute ? "Đã chia đều fit point trên chính spline đã chọn.\n" :
                        "Đã thêm " + inserted + " fit point vào chính spline đã chọn.\n") +
                    (reduceExisting && spline.GetPointCount() < originalCount ?
                        "Đã giảm từ " + originalCount + " xuống " + spline.GetPointCount() + " fit point.\n" :
                        redistribute && inserted > 0 ? "Đã bổ sung " + inserted + " fit point để giữ sai số ≤ 0.20 mm.\n" : "") +
                    "Tổng fit point: " + spline.GetPointCount() +
                    "\nKhoảng cách mục tiêu: " + (length * 1000 / (spline.GetPointCount() - 1))
                        .ToString("F3", CultureInfo.InvariantCulture) + " mm" +
                    "\nĐộ lệch hình dạng: " +
                    (deviation.Distance * 1000).ToString("F3", CultureInfo.InvariantCulture) + " mm" +
                    (rebuilt ? "" : "\nHãy kiểm tra feature khác vì rebuild báo lỗi.") +
                    (finishedUndo ? "" : "\nSOLIDWORKS không tạo được Undo nhóm cho lần chạy này."),
                    "REPAIR 3D SPLINE", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                string recovery = "";
                if (recording && model != null)
                {
                    try
                    {
                        bool finished = model.Extension.FinishRecordingUndoObject2(
                            "INSERT FIT POINTS INTO 3D SPLINE (failed)", false);
                        if (finished && changed)
                        {
                            model.EditUndo2(1);
                            SketchSpline restoredSpline = sketch != null && sourceId != null
                                ? (SketchSpline)RefreshSplineSegmentById(sketch, sourceId) : null;
                            bool restored = restoredSpline != null &&
                                restoredSpline.GetPointCount() == originalCount &&
                                originalFitPositions != null &&
                                originalFitPositions.All(position =>
                                    SplineFitPoints(restoredSpline).Any(point =>
                                        Distance(position, Position(point)) <= 1e-5));
                            if (restored && originalCurve != null)
                            {
                                DeviationResult undoDeviation = MeasureDeviation(
                                    RefreshSplineSegmentById(sketch, sourceId),
                                    originalCurve, curveStart, curveEnd);
                                Log("ShapeChangeAfterUndo=" + (undoDeviation.Distance * 1000)
                                    .ToString("F4", CultureInfo.InvariantCulture) + " mm");
                                restored = undoDeviation.Distance <= ApproximationTolerance;
                            }
                            Log("Undo requested; fit point positions restored=" + restored);
                            if (restored) { inserted = 0; moved = 0; removed = 0; }
                            recovery = restored
                                ? "\nĐã Undo về fit point ban đầu; hãy kiểm tra spline trước khi lưu."
                                : "\nUndo chưa khôi phục đầy đủ fit point. Nếu chưa lưu, hãy đóng Part không lưu rồi mở lại bản gốc.";
                        }
                        else if (changed)
                            recovery = "\nKhông hoàn tất Undo; hãy kiểm tra spline trước khi lưu.";
                    }
                    catch (Exception undoError)
                    {
                        Log("Undo failure: " + undoError);
                        recovery = "\nUndo thất bại; nếu chưa lưu, hãy đóng Part không lưu rồi mở lại bản gốc.";
                    }
                }
                Log("FAILED: " + ex);
                ShowMessage(owner, ex.Message + recovery,
                    "REPAIR 3D SPLINE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { Log("InsertedFitPoints=" + inserted +
                "; RemovedFitPoints=" + removed + "; MovedFitPoints=" + moved); }
        }

        // Fit an Arc in its existing sketch. The original entity is retained as
        // construction geometry, so its ID, radius dimension and relations survive.
        // CreateSpline2 adds a native fit spline, not separate sketch points.
        private void RunArcFit(IWin32Window owner, ModelDoc2 model, Sketch sketch, SketchSegment arcSegment)
        {
            bool recording = false, attemptedChange = false;
            ArcFitSnapshot before = null;
            HashSet<string> originalIds = null;
            Feature sketchOwner = null;
            Dictionary<string, double> originalDimensions = null;
            int[] createdId = null;
            try
            {
                before = CaptureArcFit(arcSegment);
                originalIds = SketchSegmentKeys(sketch);
                sketchOwner = FindSketchOwner(model.FirstFeature() as Feature, sketch, false);
                Require(sketchOwner != null, "Không tìm được feature của 3D Sketch gốc.");
                originalDimensions = ArcFitSketchDimensions(sketchOwner);
                double start, end;
                Curve reference = GetArcFitReference(arcSegment, out start, out end);
                double length = reference.GetLength3(start, end);
                Log("ArcFitSource=" + before.Name + "; ID=" + string.Join(",", before.Id) +
                    "; RadiusMm=" + (before.Radius * 1000).ToString("F6", CultureInfo.InvariantCulture) +
                    "; LengthMm=" + (length * 1000).ToString("F6", CultureInfo.InvariantCulture));

                // Repeatedly selecting the retained construction Arc must not add
                // another coincident spline. Its existing free fit can be repaired.
                if (before.Construction)
                {
                    var existing = SketchSegments(sketch).Where(s =>
                        s.GetType() == (int)swSketchSegments_e.swSketchSPLINE && !s.ConstructionGeometry)
                        .Where(s => {
                            var fit = s as SketchSpline;
                            if (fit == null || fit.IsStyleSpline || fit.GetPointCount() < MinPointCount) return false;
                            var points = SplineFitPoints(fit).Select(Position).ToList();
                            return points.Any(p => Distance(p, before.Start) <= PointTolerance) &&
                                points.Any(p => Distance(p, before.End) <= PointTolerance) &&
                                MeasureDeviation(s, reference, start, end).Distance <= ApproximationTolerance;
                        }).ToList();
                    Require(existing.Count <= 1, "Có nhiều spline cùng Arc này; hãy chọn trực tiếp spline cần Repair.");
                    if (existing.Count == 1)
                    {
                        model.ClearSelection2(true);
                        Require(existing[0].Select4(false, null), "Không chọn được spline đã fit từ Arc.");
                        Log("ArcFitReuse=" + existing[0].GetName());
                        Run(owner);
                        return;
                    }
                }

                model.Extension.StartRecordingUndoObject();
                recording = true;
                SketchSegment fitted = null;
                DeviationResult deviation = null;
                double spacingError = double.MaxValue;
                int count;
                for (count = MinPointCount; count <= MaxPointCount; count++)
                {
                    EnsureSketchIsBeingEdited(model, sketch);
                    model.ClearSelection2(true);
                    var targets = GetPointsByArcLength(reference, start, end, count);
                    // Match the source Arc's endpoint direction even if its trimmed
                    // native Curve runs in the opposite parametric direction.
                    if (Distance(targets[0], before.Start) > PointTolerance) targets.Reverse();
                    Require(Distance(targets[0], before.Start) <= PointTolerance &&
                        Distance(targets[targets.Count - 1], before.End) <= PointTolerance,
                        "Hai đầu đoạn Arc đã trim không trùng Arc gốc.");
                    attemptedChange = true;
                    bool previousAddToDb = model.SketchManager.AddToDB;
                    bool previousDisplay = model.SketchManager.DisplayWhenAdded;
                    try
                    {
                        model.SketchManager.AddToDB = true;
                        model.SketchManager.DisplayWhenAdded = false;
                        fitted = model.SketchManager.CreateSpline2(targets.SelectMany(p => p).ToArray(), false);
                    }
                    finally
                    {
                        model.SketchManager.DisplayWhenAdded = previousDisplay;
                        model.SketchManager.AddToDB = previousAddToDb;
                    }
                    Require(fitted != null, "SOLIDWORKS không fit được spline từ Arc.");
                    createdId = SegmentId(fitted);
                    Require(fitted.GetSketch() == sketch && model.SketchManager.ActiveSketch == sketch,
                        "Spline không thuộc 3D Sketch gốc; đã yêu cầu Undo.");
                    var spline = fitted as SketchSpline;
                    Require(spline != null && !spline.IsStyleSpline && spline.GetPointCount() == count &&
                        !SplineHandlesHaveControls(spline) && SplineFitPoints(spline)
                            .Where(p => !before.PointIds.Contains(PointKey(p))).All(p => p.GetRelationsCount() == 0),
                        "Spline mới không có đúng số fit point hoặc điểm mới bị ràng buộc.");
                    Log("ArcPointIds=" + string.Join(";", before.PointIds) + "; FitPointIds=" +
                        string.Join(";", SplineFitPoints(spline).Select(PointKey)));
                    // SOLIDWORKS can reuse the Arc's endpoint vertices. Interior
                    // fits belong to the new spline; Arc geometry is verified below.
                    double targetError = MaxTargetPositionError(spline, targets);
                    deviation = MeasureDeviation(fitted, reference, start, end);
                    spacingError = MaxEqualSpacingError(spline, fitted.GetCurve() as Curve);
                    Log("ArcFitCandidate; FitPoints=" + count + "; ShapeChangeMm=" + deviation.Distance * 1000 +
                        "; SpacingErrorMm=" + spacingError * 1000 + "; TargetErrorMm=" + targetError * 1000);
                    if (deviation.Distance <= ApproximationTolerance && spacingError <= SpacingTolerance &&
                        targetError <= PointTolerance) break;
                    DeleteArcFitCandidate(model, sketch, createdId, originalIds);
                    createdId = null;
                    fitted = null;
                }
                Require(fitted != null && count <= MaxPointCount,
                    "Không fit được Arc với tối đa 100 điểm trong sai số hình dạng / khoảng cách cho phép.");
                arcSegment = ArcFitSegmentById(sketch, before.Id);
                arcSegment.ConstructionGeometry = true;
                bool rebuilt = model.EditRebuild3();
                EnsureSketchIsBeingEdited(model, sketch);
                VerifyArcFitPreserved(sketch, before, true);
                VerifyArcFitDimensions(sketchOwner, originalDimensions);
                var expectedIds = new HashSet<string>(originalIds) { string.Join(",", createdId) };
                Require(SketchSegmentKeys(sketch).SetEquals(expectedIds),
                    "Các đoạn khác trong 3D Sketch đã thay đổi; đã yêu cầu Undo.");
                fitted = RefreshSplineSegmentById(sketch, createdId);
                Require(((SketchSpline)fitted).GetPointCount() == count && !fitted.ConstructionGeometry,
                    "Spline sau rebuild không có đúng số fit point hoặc đã thành Construction.");
                var finalTargets = GetPointsByArcLength(reference, start, end, count);
                deviation = MeasureDeviation(fitted, reference, start, end);
                spacingError = MaxEqualSpacingError((SketchSpline)fitted, fitted.GetCurve() as Curve);
                Require(deviation.Distance <= ApproximationTolerance && spacingError <= SpacingTolerance &&
                    MaxTargetPositionError((SketchSpline)fitted, finalTargets) <= PointTolerance,
                    "Spline lệch sau rebuild; đã yêu cầu Undo.");
                model.ClearSelection2(true);
                Require(fitted.Select4(false, null), "Không chọn lại được spline đã fit.");
                bool finishedUndo = model.Extension.FinishRecordingUndoObject2("FIT ARC TO SPLINE IN EXISTING 3D SKETCH", false);
                recording = false;
                model.GraphicsRedraw2();
                Log("ArcFitComplete; OriginalArcId=" + string.Join(",", before.Id) +
                    "; SplineId=" + string.Join(",", createdId) + "; FitPoints=" + count +
                    "; ShapeChangeMm=" + deviation.Distance * 1000 + "; SpacingErrorMm=" + spacingError * 1000 +
                    "; UndoGroupAvailable=" + finishedUndo);
                ShowMessage(owner, "Đã fit Arc thành spline trong cùng 3D Sketch.\n" +
                    "Arc gốc được giữ làm Construction, giữ bán kính và quan hệ cũ.\n" +
                    "Fit point thuộc spline: " + count + "\nKhoảng cách: " +
                    (length * 1000 / (count - 1)).ToString("F3", CultureInfo.InvariantCulture) +
                    " mm\nĐộ lệch hình dạng: " + (deviation.Distance * 1000).ToString("F4", CultureInfo.InvariantCulture) + " mm" +
                    (rebuilt ? "" : "\nHãy kiểm tra các feature phụ thuộc vì rebuild báo lỗi.") +
                    (finishedUndo ? "" : "\nSOLIDWORKS không tạo được Undo nhóm cho lần chạy này."),
                    "REPAIR 3D SPLINE", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                string recovery = "";
                if (recording)
                {
                    try
                    {
                        bool finished = model.Extension.FinishRecordingUndoObject2("FIT ARC TO SPLINE (failed)", false);
                        recording = false;
                        if (attemptedChange)
                        {
                            if (finished) model.EditUndo2(1);
                            sketch = EnsureSketchIsBeingEdited(model, sketch);
                            // Cleanup is restricted to this command's new spline.
                            if (createdId != null && SketchSegmentKeys(sketch).Contains(string.Join(",", createdId)))
                                DeleteArcFitCandidate(model, sketch, createdId, originalIds);
                            ArcFitSegmentById(sketch, before.Id).ConstructionGeometry = before.Construction;
                            VerifyArcFitPreserved(sketch, before, before.Construction);
                            VerifyArcFitDimensions(sketchOwner, originalDimensions);
                            Require(SketchSegmentKeys(sketch).SetEquals(originalIds), "Chưa khôi phục đủ các đoạn sketch gốc.");
                            recovery = "\nĐã khôi phục Arc và các đoạn sketch ban đầu.";
                            Log("ArcFitRecoveryVerified=True");
                        }
                    }
                    catch (Exception undoError)
                    {
                        Log("ArcFitRecoveryFailure=" + undoError);
                        recovery = "\nChưa kiểm tra được khôi phục đầy đủ; hãy kiểm tra sketch trước khi lưu.";
                    }
                }
                Log("ArcFitFailed=" + error);
                ShowMessage(owner, error.Message + recovery, "REPAIR 3D SPLINE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static Curve GetArcFitReference(SketchSegment segment, out double start, out double end)
        {
            var arc = segment as SketchArc;
            Require(arc != null && arc.IsCircle() == 0, "Chỉ hỗ trợ Arc mở; chưa hỗ trợ đường tròn kín.");
            var first = arc.GetStartPoint2() as SketchPoint;
            var last = arc.GetEndPoint2() as SketchPoint;
            Require(first != null && last != null, "Không lấy được hai đầu Arc.");
            double[] p = Position(first), q = Position(last);
            Require(Distance(p, q) > PointTolerance, "Hai đầu Arc trùng nhau hoặc Arc quá ngắn.");
            double length = segment.GetLength();
            Require(Finite(length) && length > PointTolerance, "Chiều dài Arc không hợp lệ.");
            var underlying = segment.GetCurve() as Curve;
            Require(underlying != null, "Không lấy được Curve chứa Arc.");
            // GetCurve returns the whole supporting circle. Trimming and matching
            // the segment's native length selects the correct minor or major Arc.
            foreach (var bounds in new[] { new[] { p, q }, new[] { q, p } })
            {
                Curve trimmed = underlying.CreateTrimmedCurve2(bounds[0][0], bounds[0][1], bounds[0][2],
                    bounds[1][0], bounds[1][1], bounds[1][2]);
                bool closed, periodic;
                if (trimmed != null && trimmed.GetEndParams(out start, out end, out closed, out periodic) &&
                    !closed && end > start && Math.Abs(trimmed.GetLength3(start, end) - length) <= PointTolerance)
                {
                    Curve copied = trimmed.Copy() as Curve;
                    Require(copied != null, "Không sao chép được đoạn Arc để kiểm tra hình dạng.");
                    return copied;
                }
            }
            throw new InvalidOperationException("Không trim được đúng đoạn Arc gốc; chưa thay đổi sketch.");
        }

        private static List<SketchSegment> SketchSegments(Sketch sketch)
        {
            var segments = sketch.GetSketchSegments() as Array;
            Require(segments != null, "Không đọc được các đoạn trong 3D Sketch.");
            return segments.Cast<object>().OfType<SketchSegment>().ToList();
        }
        private static HashSet<string> SketchSegmentKeys(Sketch sketch)
        { return new HashSet<string>(SketchSegments(sketch).Select(s => string.Join(",", SegmentId(s)))); }
        private static SketchSegment ArcFitSegmentById(Sketch sketch, int[] id)
        {
            string key = string.Join(",", id);
            var segment = SketchSegments(sketch).SingleOrDefault(s => string.Join(",", SegmentId(s)) == key);
            Require(segment != null && segment.GetType() == (int)swSketchSegments_e.swSketchARC,
                "Arc gốc không còn cùng ID trong sketch; đã yêu cầu Undo.");
            return segment;
        }
        private static void DeleteArcFitCandidate(ModelDoc2 model, Sketch sketch, int[] id, HashSet<string> originalIds)
        {
            Require(id != null && !originalIds.Contains(string.Join(",", id)), "Không được xóa đoạn sketch gốc.");
            EnsureSketchIsBeingEdited(model, sketch);
            var candidate = RefreshSplineSegmentById(sketch, id);
            model.ClearSelection2(true);
            Require(candidate.Select4(false, null), "Không chọn được spline thử cần bỏ.");
            model.Extension.DeleteSelection2(0);
            Require(!SketchSegmentKeys(sketch).Contains(string.Join(",", id)), "Không bỏ được spline thử.");
            model.ClearSelection2(true);
        }
        private static ArcFitSnapshot CaptureArcFit(SketchSegment segment)
        {
            var arc = segment as SketchArc;
            Require(arc != null && arc.IsCircle() == 0, "Chọn Arc mở trong 3D Sketch.");
            var points = new[] { arc.GetStartPoint2(), arc.GetEndPoint2(), arc.GetCenterPoint2() }.Cast<SketchPoint>().ToList();
            Require(points.All(p => p != null), "Không đọc được các điểm của Arc.");
            return new ArcFitSnapshot { Id = SegmentId(segment), Name = segment.GetName(),
                Radius = arc.GetRadius(), Length = segment.GetLength(), Construction = segment.ConstructionGeometry,
                Start = Position(points[0]), End = Position(points[1]), Center = Position(points[2]),
                PointIds = points.Select(PointKey).ToList(), PointRelations = points.Select(p => p.GetRelationsCount()).ToList(),
                Relations = RelationSummary(Relations(segment)) };
        }
        private static void VerifyArcFitPreserved(Sketch sketch, ArcFitSnapshot before, bool construction)
        {
            var after = CaptureArcFit(ArcFitSegmentById(sketch, before.Id));
            Require(after.Name == before.Name && after.Construction == construction && after.Relations == before.Relations &&
                after.PointIds.SequenceEqual(before.PointIds) && after.PointRelations.SequenceEqual(before.PointRelations) &&
                Math.Abs(after.Radius - before.Radius) <= PointTolerance && Math.Abs(after.Length - before.Length) <= PointTolerance &&
                Distance(after.Start, before.Start) <= PointTolerance && Distance(after.End, before.End) <= PointTolerance &&
                Distance(after.Center, before.Center) <= PointTolerance,
                "Arc gốc đã đổi ID, bán kính, hình học hoặc quan hệ; đã yêu cầu Undo.");
            Log("ArcPreserved=True; ID=" + string.Join(",", before.Id) + "; Construction=" + construction);
        }
        private sealed class ArcFitSnapshot
        {
            internal int[] Id;
            internal string Name, Relations;
            internal bool Construction;
            internal double Radius, Length;
            internal double[] Start, End, Center;
            internal List<string> PointIds;
            internal List<int> PointRelations;
        }

        private static Dictionary<string, double> ArcFitSketchDimensions(Feature owner)
        {
            var result = new Dictionary<string, double>();
            for (var display = owner.GetFirstDisplayDimension() as DisplayDimension; display != null;
                display = owner.GetNextDisplayDimension(display) as DisplayDimension)
            {
                var dimension = display.GetDimension2(0) as Dimension;
                Require(dimension != null && Finite(dimension.SystemValue), "Không đọc được DIM của sketch gốc.");
                result.Add(dimension.FullName, dimension.SystemValue);
            }
            return result;
        }
        private static void VerifyArcFitDimensions(Feature owner, Dictionary<string, double> before)
        {
            var after = ArcFitSketchDimensions(owner);
            Require(after.Count == before.Count && before.All(d => after.ContainsKey(d.Key) &&
                Math.Abs(after[d.Key] - d.Value) <= PointTolerance), "DIM của sketch gốc đã thay đổi; đã yêu cầu Undo.");
            Log("ArcSketchDimensionsPreserved=True; Count=" + after.Count + "; Values=" +
                string.Join(";", after.Select(d => d.Key + "=" + d.Value.ToString("G17", CultureInfo.InvariantCulture))));
        }

        internal static int CalculatePointCount(double length)
        {
            Require(Finite(length) && length > 0, "Chiều dài phải lớn hơn 0.");
            return (int)Math.Max(MinPointCount, Math.Min(MaxPointCount, Math.Ceiling(length / DefaultSpacing) + 1));
        }

        private static int RemoveInteriorFits(ModelDoc2 model, Sketch sketch, int[] sourceId,
            Curve reference, int desiredCount)
        {
            var source = (SketchSpline)RefreshSplineSegmentById(sketch, sourceId);
            var initial = OrderedFitPoints(source, reference);
            var endpoints = new HashSet<string> { PointKey(initial.First().Point), PointKey(initial.Last().Point) };
            int removed = 0;
            while (source.GetPointCount() > desiredCount)
            {
                var points = OrderedFitPoints(source, reference);
                var chosen = points.Select((sample, index) => new { Sample = sample, Index = index })
                    .Where(candidate => candidate.Index > 0 && candidate.Index < points.Count - 1 &&
                        !endpoints.Contains(PointKey(candidate.Sample.Point)))
                    .OrderBy(candidate => Distance(points[candidate.Index - 1].Position, candidate.Sample.Position) +
                        Distance(candidate.Sample.Position, points[candidate.Index + 1].Position) -
                        Distance(points[candidate.Index - 1].Position, points[candidate.Index + 1].Position))
                    .First();
                string deletedKey = PointKey(chosen.Sample.Point);
                var beforeKeys = new HashSet<string>(points.Select(sample => PointKey(sample.Point)));
                int before = source.GetPointCount();
                bool accepted = EditAndVerify(() => source.DeletePoint(chosen.Sample.Point), () =>
                {
                    source = (SketchSpline)RefreshSplineSegmentById(sketch, sourceId);
                    var after = SplineFitPoints(source);
                    if (after.Count == before) return false;
                    Require(after.Count == before - 1 && !after.Any(point => PointKey(point) == deletedKey) &&
                        after.All(point => beforeKeys.Contains(PointKey(point))) &&
                        endpoints.All(key => after.Any(point => PointKey(point) == key)),
                        "DeletePoint đổi các fit point khác hoặc hai đầu spline; đã yêu cầu Undo.");
                    return true;
                }, Log);
                Require(accepted, "SOLIDWORKS không giảm được fit point trên spline đã chọn; đã yêu cầu Undo.");
                removed++;
                Log("RemoveInteriorFit; before=" + before + "; after=" + source.GetPointCount() + "; ID=" + deletedKey);
            }
            return removed;
        }

        // Native Boolean results are not authoritative: a COM call can report
        // false (or throw) after updating geometry. Always read the SAME entity.
        internal static bool EditAndVerify(Func<bool> edit, Func<bool> verify, Action<string> log)
        {
            bool returned = false;
            string error = "";
            try { returned = edit(); }
            catch (Exception ex) { error = ex.Message; }
            bool verified = verify();
            if (!returned || !verified || error.Length > 0)
                log?.Invoke("NativeEdit returned=" + returned + "; verified=" + verified + "; error=" + error);
            return verified;
        }

        internal static bool TryInsertOnSameSpline(ModelDoc2 model, SketchSpline selected,
            int targetCount, Action<string> log, double[] preferred = null)
        {
            SketchSegment selectedSegment = selected as SketchSegment;
            Require(model != null && selectedSegment != null, "Thiếu spline để thêm fit point.");
            Sketch sketch = EnsureSketchIsBeingEdited(model, selectedSegment.GetSketch());
            int[] id = SegmentId(selectedSegment);
            selected = (SketchSpline)RefreshSplineSegmentById(sketch, id);
            int before = selected.GetPointCount();
            Require(targetCount == before + 1, "Chỉ thêm một fit point vào spline cũ mỗi lần.");
            var beforeKeys = new HashSet<string>(SplineFitPoints(selected).Select(PointKey));
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass > 0)
                    log?.Invoke("Insert retry rebuild=" + model.EditRebuild3());
                sketch = EnsureSketchIsBeingEdited(model, sketch);
                SketchSpline spline = (SketchSpline)RefreshSplineSegmentById(sketch, id);
                Curve curve = ((SketchSegment)spline).GetCurve() as Curve;
                Require(curve != null, "Không đọc được Curve mới nhất của spline.");
                double start, end; bool closed, periodic;
                Require(curve.GetEndParams(out start, out end, out closed, out periodic) && end > start,
                    "Miền tham số spline không hợp lệ khi thêm điểm.");
                var existing = SplineFitPoints(spline).Select(Position).ToList();
                var candidates = GetPointsByArcLength(curve, start, end,
                    Math.Max(64, targetCount * 6)).Skip(1).Take(Math.Max(64, targetCount * 6) - 2)
                    .Select(point => new { Point = point, Clearance = existing.Min(fit => Distance(point, fit)) })
                    .Where(candidate => candidate.Clearance > 1e-7)
                    .OrderByDescending(candidate => candidate.Clearance)
                    .Select(candidate => candidate.Point).Take(32).ToList();
                if (preferred != null && IsPoint(preferred)) candidates.Insert(0, preferred);
                int attempt = 0;
                foreach (double[] candidate in candidates)
                {
                    spline = (SketchSpline)RefreshSplineSegmentById(sketch, id);
                    Require(spline.GetPointCount() == before, "Số điểm đổi ngoài thao tác chèn đang kiểm tra.");
                    model.ClearSelection2(true);
                    Require(((SketchSegment)spline).Select4(false, null), "Không chọn lại được spline cũ khi thêm điểm.");
                    // Selecting/rebuilding can refresh native parameterization.
                    // Project onto a newly acquired Curve before EVERY attempt.
                    curve = ((SketchSegment)spline).GetCurve() as Curve;
                    double[] onCurve = curve == null ? null : curve.GetClosestPointOn(
                        candidate[0], candidate[1], candidate[2]) as double[];
                    if (!IsPoint(onCurve) || SplineFitPoints(spline).Any(point => Distance(Position(point), onCurve) <= 1e-7))
                        continue;
                    Curve beforeCurve = curve.Copy() as Curve;
                    Require(beforeCurve != null, "Không giữ được Curve trước khi thêm fit point.");
                    bool inserted = EditAndVerify(
                        () => spline.InsertPoint(onCurve[0], onCurve[1], onCurve[2]),
                        () =>
                        {
                            var refreshed = (SketchSpline)RefreshSplineSegmentById(sketch, id);
                            int after = refreshed.GetPointCount();
                            Require(after == before || after == targetCount,
                                "InsertPoint đổi số điểm không đúng; phải Undo.");
                            if (after == before) return false;
                            List<SketchPoint> points = SplineFitPoints(refreshed);
                            Require(beforeKeys.All(key => points.Any(point => PointKey(point) == key)),
                                "InsertPoint làm mất fit point cũ; phải Undo.");
                            List<SketchPoint> added = points.Where(point => !beforeKeys.Contains(PointKey(point))).ToList();
                            Require(added.Count == 1, "Không xác định được một fit point mới; phải Undo.");
                            double[] actual = Position(added[0]);
                            double[] projection = beforeCurve.GetClosestPointOn(actual[0], actual[1], actual[2]) as double[];
                            Require(IsPoint(projection), "Không đo được fit point mới trên Curve cũ; phải Undo.");
                            double normalError = Distance(actual, projection);
                            log?.Invoke("InsertedFitPoint readback; ID=" + PointKey(added[0]) +
                                "; requestOffsetMm=" + Distance(actual, onCurve) * 1000 +
                                "; curveErrorMm=" + normalError * 1000 + "; XYZ=" + XYZ(actual));
                            // SOLIDWORKS may insert at a nearby native parameter
                            // rather than at the exact requested coordinate. The
                            // new fit is redistributed afterwards; validate its
                            // distance to the old curve, not its along-curve offset.
                            Require(normalError <= ApproximationTolerance,
                                "Fit point mới lệch khỏi Curve cũ quá 0.20 mm; phải Undo.");
                            return true;
                        }, log);
                    log?.Invoke("InsertPoint attempt=" + (++attempt) + "; pass=" + pass +
                        "; targetCount=" + targetCount + "; accepted=" + inserted + "; XYZ=" + XYZ(onCurve));
                    model.ClearSelection2(true);
                    if (inserted) return true;
                }
            }
            return false;
        }

        internal static int RedistributeOnOriginalCurve(ModelDoc2 model, Sketch sketch, int[] sourceId,
            Curve original, double start, double end, double length, List<double[]> targets, bool resetFreeHandles,
            bool allowReferenceOffset = false)
        {
            sketch = EnsureSketchIsBeingEdited(model, sketch);
            SketchSegment segment = RefreshSplineSegmentById(sketch, sourceId);
            SketchSpline spline = (SketchSpline)segment;
            // The immutable reference also defines direction. This remains
            // stable after native insertion and supports an Edge whose native
            // direction is opposite to the existing spline's direction.
            // An inserted fit can belong to the transient candidate curve,
            // which has not yet been fitted back to the immutable reference.
            // Project only to determine order; final positions and geometry
            // are verified after the complete redistribution.
            List<FitPointSample> initialPoints = OrderedFitPoints(spline, original, !allowReferenceOffset);
            Require(initialPoints.Count == targets.Count, "Số fit point không khớp với vị trí chia đều.");
            var moves = initialPoints.Select((point, index) => new
            {
                PointId = PointKey(point.Point), Index = index, Position = point.Position,
                Length = point.LengthFromStart, TargetLength = length * index / (targets.Count - 1)
            }).Where(move => move.Index > 0 && move.Index < targets.Count - 1 &&
                Distance(move.Position, targets[move.Index]) > PointTolerance).ToList();
            // Moving toward the start first, and toward the end in reverse
            // order, keeps each fit point from crossing its neighbour.
            var orderedMoves = moves.Where(move => move.TargetLength <= move.Length).OrderBy(move => move.Index)
                .Concat(moves.Where(move => move.TargetLength > move.Length).OrderByDescending(move => move.Index));
            int moved = 0;
            foreach (var move in orderedMoves)
            {
                double[] projection = original.GetClosestPointOn(move.Position[0], move.Position[1], move.Position[2]) as double[];
                Require(IsPoint(projection) && projection.Length >= 4 && Finite(projection[3]),
                    "Không chiếu được fit point về đường gốc.");
                double sourceArc = original.GetLength3(start, Math.Max(start, Math.Min(end, projection[3])));
                bool accepted = MoveFitPointWithArcSteps(model, sketch, sourceId, move.PointId,
                    original, start, end, sourceArc, move.TargetLength, targets[move.Index]);
                Log("MoveFitPoint[" + move.Index + "]=" + accepted +
                    "; ID=" + move.PointId + "; Target=" + XYZ(targets[move.Index]));
                Require(accepted, "SOLIDWORKS không chấp nhận vị trí mới của fit point; đã yêu cầu Undo.");
                moved++;
                if (resetFreeHandles) ((SketchSpline)RefreshSplineSegmentById(sketch, sourceId)).ResetAllHandles();
            }
            return moved;
        }

        internal static bool TryMoveFitPoint(ModelDoc2 model, Sketch sketch, int[] sourceId,
            string pointKey, double[] coordinates)
        {
            Func<SketchPoint> readPoint = () => SplineFitPoints(
                (SketchSpline)RefreshSplineSegmentById(sketch, sourceId))
                .FirstOrDefault(candidate => PointKey(candidate) == pointKey);
                SketchPoint point = readPoint();
                Require(point != null, "Mất fit point cũ trong lúc chia đều.");
                if (Distance(Position(point), coordinates) <= PointTolerance) return true;
                Func<bool> verify = () =>
                {
                    SketchPoint refreshed = readPoint();
                    return refreshed != null && Distance(Position(refreshed), coordinates) <= PointTolerance;
                };
                if (EditAndVerify(() => point.SetCoords(coordinates[0], coordinates[1], coordinates[2]), verify, Log))
                    return true;
                point = readPoint();
                double[] before = Position(point);
                model.ClearSelection2(true);
                try
                {
                    if (!point.Select4(false, null)) return false;
                    return EditAndVerify(() =>
                    {
                        model.Extension.MoveOrCopy(false, 0, true, before[0], before[1], before[2],
                            coordinates[0], coordinates[1], coordinates[2]);
                        return true;
                    }, verify, Log);
                }
                finally { model.ClearSelection2(true); }
        }

        private static Sketch EnsureSketchIsBeingEdited(ModelDoc2 model, Sketch sketch)
        {
            Sketch active = model.SketchManager.ActiveSketch;
            if (active == sketch) return active;
            Require(active == null, "Một sketch khác đang Edit; không thể sửa spline nguồn.");
            Feature owner = FindSketchOwner(model.FirstFeature() as Feature, sketch, false);
            Require(owner != null, "Không tìm lại được feature của sketch nguồn.");
            model.ClearSelection2(true);
            Require(owner.Select2(false, 0), "Không chọn lại được sketch nguồn.");
            model.EditSketch();
            active = model.SketchManager.ActiveSketch;
            Require(active == sketch, "Không vào lại được Edit Sketch nguồn sau rebuild.");
            Log("RestoredEditSketch=" + owner.Name);
            return active;
        }

        private static Feature FindSketchOwner(Feature first, Sketch sketch, bool subfeature)
        {
            for (Feature feature = first; feature != null;
                feature = (subfeature ? feature.GetNextSubFeature() : feature.GetNextFeature()) as Feature)
            {
                string type = feature.GetTypeName2();
                if ((type == "ProfileFeature" || type == "3DProfileFeature") &&
                    (feature.GetSpecificFeature2() as Sketch) == sketch) return feature;
                Feature child = FindSketchOwner(feature.GetFirstSubFeature() as Feature, sketch, true);
                if (child != null) return child;
            }
            return null;
        }

        private static bool MoveFitPointWithArcSteps(ModelDoc2 model, Sketch sketch, int[] sourceId,
            string pointKey, Curve reference, double start, double end, double sourceArc,
            double targetArc, double[] target)
        {
            Func<double[], bool> move = coordinates => TryMoveFitPoint(model, sketch, sourceId, pointKey, coordinates);
            if (move(target)) return true;
            // Converted splines may refuse a large single jump. Follow the
            // immutable original Curve in bounded arc steps instead.
            double total = reference.GetLength3(start, end);
            bool completed = AdvanceAlongArc(sourceArc, targetArc, next =>
            {
                double lo = start, hi = end;
                for (int iteration = 0; iteration < 48; iteration++)
                {
                    double mid = (lo + hi) * 0.5;
                    if (reference.GetLength3(start, mid) < next) lo = mid;
                    else hi = mid;
                }
                double[] position = next <= 0 ? Evaluate(reference, start)
                    : next >= total ? Evaluate(reference, end) : Evaluate(reference, (lo + hi) * 0.5);
                return move(position);
            });
            return completed && move(target);
        }

        internal static bool AdvanceAlongArc(double sourceArc, double targetArc, Func<double, bool> move)
        {
            Require(Finite(sourceArc) && Finite(targetArc) && sourceArc >= 0 && targetArc >= 0,
                "Khoảng di chuyển theo arc không hợp lệ.");
            double direction = Math.Sign(targetArc - sourceArc);
            double progress = sourceArc;
            double step = Math.Min(0.010, Math.Abs(targetArc - sourceArc));
            if (step <= PointTolerance) return move(targetArc);
            for (int attempt = 0; attempt < 1024 && step >= PointTolerance; attempt++)
            {
                double remaining = direction * (targetArc - progress);
                if (remaining <= PointTolerance) return move(targetArc);
                double next = progress + direction * Math.Min(step, remaining);
                if (move(next)) { progress = next; step = Math.Min(0.010, step * 1.5); }
                else step *= 0.5;
            }
            Log("Arc-step move stopped; progressMm=" + progress * 1000 + "; targetMm=" + targetArc * 1000);
            return false;
        }

        private void ShowMessage(IWin32Window owner, string message, string title,
            MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            if (report != null) report(message, icon != MessageBoxIcon.Warning);
            else MessageBox.Show(owner, message, title, buttons, icon);
        }

        internal static bool CanResetFreeHandles(SketchSpline spline)
        {
            return !SplineHandlesHaveControls(spline) &&
                SplineFitPoints(spline).All(point => point.GetRelationsCount() == 0);
        }

        private static bool SplineHandlesHaveControls(SketchSpline spline)
        {
            Array handles = spline.GetSplineHandles() as Array;
            if (handles == null) return spline.GetSplineHandleCount() != 0;
            return handles.Cast<object>().OfType<SplineHandle>().Any(handle =>
                handle.TangentDriving || handle.CurvatureControlled);
        }

        private static List<double[]> GetPointsByArcLength(Curve curve, double a, double b, int count)
        {
            return SampleByArcLength(a, b, count, curve.GetLength3, p => Evaluate(curve, p));
        }

        private static DeviationResult MeasureDeviation(
            SketchSegment segment,
            Curve edgeCurve,
            double edgeStart,
            double edgeEnd)
        {
            Curve splineCurve = segment.GetCurve() as Curve;
            Require(splineCurve != null, "Không lấy được Curve của spline sau khi nội suy.");
            double splineStart, splineEnd;
            bool closed, periodic;
            Require(splineCurve.GetEndParams(out splineStart, out splineEnd, out closed, out periodic),
                "Không lấy được khoảng tham số spline sau khi nội suy.");

            var result = new DeviationResult();
            foreach (double[] splinePoint in SampleByParameter(
                splineCurve, splineStart, splineEnd, DenseValidationSamples))
            {
                double[] edgePoint = ClosestWithinBounds(edgeCurve, edgeStart, edgeEnd, splinePoint);
                Require(IsPoint(edgePoint), "Edge.GetClosestPointOn thất bại.");
                UpdateDeviation(result, splinePoint, edgePoint);
            }

            foreach (double[] edgePoint in SampleByParameter(
                edgeCurve, edgeStart, edgeEnd, DenseValidationSamples))
            {
                double[] splinePoint = ClosestWithinBounds(splineCurve, splineStart, splineEnd, edgePoint);
                Require(IsPoint(splinePoint), "Spline.GetClosestPointOn thất bại.");
                UpdateDeviation(result, splinePoint, edgePoint);
            }
            // Parameter scans can miss narrow bends in imported splines.
            // Add a chord-tolerance tessellation in both directions and clamp
            // nearest points to the actual trimmed curve bounds.
            foreach (double[] splinePoint in TessellatedPoints(splineCurve, splineStart, splineEnd))
                UpdateDeviation(result, splinePoint, ClosestWithinBounds(edgeCurve, edgeStart, edgeEnd, splinePoint));
            foreach (double[] edgePoint in TessellatedPoints(edgeCurve, edgeStart, edgeEnd))
                UpdateDeviation(result, ClosestWithinBounds(splineCurve, splineStart, splineEnd, edgePoint), edgePoint);
            return result;
        }

        private static IEnumerable<double[]> TessellatedPoints(Curve curve, double start, double end)
        {
            double[] flat = curve.GetTessPts(5e-8, 0, Evaluate(curve, start), Evaluate(curve, end)) as double[];
            Require(flat != null && flat.Length >= 6 && flat.Length % 3 == 0 && flat.All(Finite),
                "Không chia được Curve thành mẫu để kiểm tra hình dạng.");
            for (int index = 0; index < flat.Length; index += 3)
                yield return new[] { flat[index], flat[index + 1], flat[index + 2] };
        }

        private static double[] ClosestWithinBounds(Curve curve, double start, double end, double[] point)
        {
            double[] closest = curve.GetClosestPointOn(point[0], point[1], point[2]) as double[];
            Require(IsPoint(closest) && closest.Length >= 4 && Finite(closest[3]),
                "Không đo được khoảng cách tới Curve trong giới hạn tham số.");
            double parameter = closest[3];
            if (curve.IsCircle())
            {
                // A trimmed Arc may cross the circle's 0 / 2π seam. Its nearest
                // parameter must be moved into the same turn as the trimmed bounds.
                parameter += Math.Ceiling((start - parameter) / (2 * Math.PI)) * 2 * Math.PI;
                if (parameter >= start && parameter <= end) return Evaluate(curve, parameter);
            }
            else if (parameter >= start && parameter <= end) return closest;
            double[] first = Evaluate(curve, start), last = Evaluate(curve, end);
            return Distance(point, first) <= Distance(point, last) ? first : last;
        }

        private static void UpdateDeviation(
            DeviationResult result,
            double[] splinePoint,
            double[] edgePoint)
        {
            double distance = Distance(splinePoint, edgePoint);
            if (distance <= result.Distance) return;
            result.Distance = distance;
            result.SplinePoint = new[] { splinePoint[0], splinePoint[1], splinePoint[2] };
            result.EdgePoint = new[] { edgePoint[0], edgePoint[1], edgePoint[2] };
        }

        internal static List<double[]> SampleByArcLength(double a, double b, int count,
            Func<double, double, double> arcLength, Func<double, double[]> evaluate)
        {
            Require(count >= 2 && Finite(a) && Finite(b) && b > a, "Khoảng lấy mẫu không hợp lệ.");
            double length = arcLength(a, b);
            Require(Finite(length) && length > PointTolerance, "Curve không có chiều dài hợp lệ.");
            var result = new List<double[]> { evaluate(a) };
            for (int i = 1; i < count - 1; i++)
            {
                double target = length * i / (count - 1), lo = a, hi = b;
                for (int step = 0; step < 48; step++)
                {
                    double mid = (lo + hi) / 2;
                    double partial = arcLength(a, mid);
                    Require(Finite(partial) && partial >= 0, "Không tính được arc length.");
                    if (partial < target) lo = mid; else hi = mid;
                }
                result.Add(evaluate((lo + hi) / 2));
            }
            result.Add(evaluate(b));
            return result;
        }

        private static IEnumerable<double[]> SampleByParameter(
            Curve curve, double start, double end, int count)
        {
            Require(count >= 2 && Finite(start) && Finite(end) && end > start,
                "Khoảng kiểm tra curve không hợp lệ.");
            for (int i = 0; i < count; i++)
                yield return Evaluate(curve, start + (end - start) * i / (count - 1));
        }

        private static List<SketchRelation> Relations(SketchSegment segment)
        {
            var raw = segment.GetRelations() as Array;
            if (raw == null)
            {
                Require(segment.GetRelationsCount() == 0,
                    "Không đọc được relation của spline đã chọn.");
                return new List<SketchRelation>();
            }
            return raw.Cast<object>().OfType<SketchRelation>().ToList();
        }

        private static object[] DefinitionEntities(SketchRelation relation)
        {
            Array entities = null;
            try { entities = relation.GetDefinitionEntities2() as Array; }
            catch (COMException) { }
            if (entities == null) entities = relation.GetDefinitionEntities() as Array;
            return entities == null ? new object[0] :
                entities.Cast<object>().Where(entity => entity != null).ToArray();
        }

        private static string RelationSummary(IEnumerable<SketchRelation> relations)
        {
            return string.Join("; ", relations.Select(relation =>
                ((swConstraintType_e)relation.GetRelationType()) + "[" +
                string.Join(",", DefinitionEntities(relation).Select(entity =>
                    entity is Edge ? "Edge" :
                    entity is SketchSegment ? "SketchSegment" :
                    entity is SketchPoint ? "SketchPoint" : entity.GetType().Name)) + "]"));
        }

        private static int[] SegmentId(SketchSegment segment)
        {
            var values = segment.GetID() as Array;
            Require(values != null && values.Length == 2, "Không đọc được ID của đoạn sketch.");
            return values.Cast<object>().Select(Convert.ToInt32).ToArray();
        }
        private static string PointKey(SketchPoint point)
        {
            var values = point.GetID() as Array;
            Require(values != null && values.Length == 2,
                "Không đọc được ID của fit point.");
            return string.Join(",", values.Cast<object>().Select(Convert.ToInt64));
        }
        internal static int[] MatchFitPoints(List<double[]> existing,
            List<double[]> targets, double tolerance)
        {
            var used = new HashSet<int>();
            var matches = Enumerable.Repeat(-1, targets.Count).ToArray();
            for (int target = 0; target < targets.Count; target++)
            {
                double nearest = tolerance;
                for (int point = 0; point < existing.Count; point++)
                {
                    if (used.Contains(point)) continue;
                    double distance = Distance(existing[point], targets[target]);
                    if (distance > nearest) continue;
                    nearest = distance;
                    matches[target] = point;
                }
                if (matches[target] >= 0) used.Add(matches[target]);
            }
            return matches;
        }

        private static List<SketchPoint> SplineFitPoints(SketchSpline spline)
        {
            var values = spline.GetPoints2() as Array;
            Require(values != null, "Không đọc được fit points của spline.");
            return values.Cast<object>().OfType<SketchPoint>().ToList();
        }
        private static List<FitPointSample> OrderedFitPoints(SketchSpline spline, Curve curve,
            bool requireOnCurve = true)
        {
            Require(curve != null, "Không lấy được Curve để đo khoảng cách fit point.");
            double start, end;
            bool closed, periodic;
            Require(curve.GetEndParams(out start, out end, out closed, out periodic) &&
                !closed && end > start, "Không đọc được miền tham số của spline mở.");
            var result = new List<FitPointSample>();
            foreach (SketchPoint point in SplineFitPoints(spline))
            {
                double[] position = Position(point);
                double[] closest = curve.GetClosestPointOn(
                    position[0], position[1], position[2]) as double[];
                Require(closest != null && closest.Length >= 4 &&
                    IsPoint(closest) && Finite(closest[3]) &&
                    (!requireOnCurve || Distance(position, closest) <= 1e-5),
                    "Fit point không còn nằm trên spline hiện tại.");
                double arcLength;
                if (Distance(position, Evaluate(curve, start)) <= 1e-5)
                    arcLength = 0;
                else if (Distance(position, Evaluate(curve, end)) <= 1e-5)
                    arcLength = curve.GetLength3(start, end);
                else
                {
                    Require(closest[3] > start && closest[3] < end,
                        "Tham số fit point nằm ngoài spline: " +
                        closest[3].ToString("G17", CultureInfo.InvariantCulture) +
                        " (" + start.ToString("G17", CultureInfo.InvariantCulture) +
                        ".." + end.ToString("G17", CultureInfo.InvariantCulture) + ").");
                    arcLength = curve.GetLength3(start, closest[3]);
                }
                Require(Finite(arcLength) && arcLength >= 0,
                    "Không đo được chiều dài đến fit point; U=" +
                    closest[3].ToString("G17", CultureInfo.InvariantCulture) +
                    "; Start=" + start.ToString("G17", CultureInfo.InvariantCulture) +
                    "; End=" + end.ToString("G17", CultureInfo.InvariantCulture) +
                    "; Result=" + arcLength.ToString("G17", CultureInfo.InvariantCulture));
                result.Add(new FitPointSample
                {
                    Point = point,
                    Position = position,
                    Parameter = closest[3],
                    LengthFromStart = arcLength
                });
            }
            return result.OrderBy(point => point.Parameter).ToList();
        }
        private static double MaxEqualSpacingError(SketchSpline spline, Curve curve)
        {
            List<FitPointSample> points = OrderedFitPoints(spline, curve);
            Require(points.Count >= 2, "Spline không đủ fit point để chia đều.");
            double start, end;
            bool closed, periodic;
            Require(curve.GetEndParams(out start, out end, out closed, out periodic),
                "Không lấy được chiều dài spline.");
            double total = curve.GetLength3(start, end);
            Require(Finite(total) && total > 0, "Chiều dài spline không hợp lệ.");
            double maxError = 0;
            for (int i = 0; i < points.Count; i++)
                maxError = Math.Max(maxError,
                    Math.Abs(points[i].LengthFromStart - total * i / (points.Count - 1)));
            List<double> gaps = points.Skip(1).Select((point, index) =>
                point.LengthFromStart - points[index].LengthFromStart).ToList();
            double expectedGap = total / (points.Count - 1);
            maxError = Math.Max(maxError, gaps.Max(gap => Math.Abs(gap - expectedGap)));
            Log("MeasuredArcSpacing; Min=" + (gaps.Min() * 1000)
                .ToString("F4", CultureInfo.InvariantCulture) + "; Max=" + (gaps.Max() * 1000)
                .ToString("F4", CultureInfo.InvariantCulture) + "; Target=" + (expectedGap * 1000)
                .ToString("F4", CultureInfo.InvariantCulture) + " mm");
            return maxError;
        }
        private static double MaxTargetPositionError(SketchSpline spline, List<double[]> targets)
        {
            List<double[]> points = SplineFitPoints(spline).Select(Position).ToList();
            int[] matches = MatchFitPoints(points, targets, double.MaxValue);
            Require(points.Count == targets.Count && matches.All(index => index >= 0),
                "Số fit point không khớp với số vị trí chia đều.");
            return targets.Select((target, index) =>
                Distance(target, points[matches[index]])).Max();
        }
        private static SketchSegment RefreshSplineSegmentById(Sketch sketch, int[] id)
        {
            object[] segments = sketch.GetSketchSegments() as object[];
            Require(segments != null, "Không đọc lại được các đoạn trong 3D Sketch.");
            foreach (SketchSegment candidate in segments.Cast<SketchSegment>())
            {
                if (candidate.GetType() != (int)swSketchSegments_e.swSketchSPLINE) continue;
                int[] candidateId = SegmentId(candidate);
                if (candidateId[0] == id[0] && candidateId[1] == id[1])
                    return candidate;
            }
            throw new InvalidOperationException(
                "Spline gốc không còn cùng ID sau thao tác; đã yêu cầu Undo.");
        }
        private static double[] Position(SketchPoint point) { return new[] { point.X, point.Y, point.Z }; }
        private static double[] Evaluate(Curve curve, double parameter)
        {
            var p = curve.Evaluate2(parameter, 0) as double[];
            Require(p != null && p.Length >= 3 && p.Take(3).All(Finite), "Curve trả về XYZ không hợp lệ.");
            return new[] { p[0], p[1], p[2] };
        }
        private static double Distance(double[] a, double[] b)
        { return Math.Sqrt(Math.Pow(a[0] - b[0], 2) + Math.Pow(a[1] - b[1], 2) + Math.Pow(a[2] - b[2], 2)); }
        private static bool IsPoint(double[] point)
        { return point != null && point.Length >= 3 && point.Take(3).All(Finite); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static string XYZ(double[] point)
        { return string.Join(",", point.Take(3).Select(x => x.ToString("G9", CultureInfo.InvariantCulture))); }
        private static void Log(string message) { Trace.WriteLine("[Repair3DSpline] " + message); }

        private sealed class DeviationResult
        {
            internal double Distance;
            internal double[] SplinePoint;
            internal double[] EdgePoint;
        }
        private sealed class FitPointSample
        {
            internal SketchPoint Point;
            internal double[] Position;
            internal double Parameter;
            internal double LengthFromStart;
        }
    }
}
