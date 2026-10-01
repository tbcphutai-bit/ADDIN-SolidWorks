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
    internal sealed class Repair3DSplineCommand
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

        internal Repair3DSplineCommand(ISldWorks app) { this.app = app; }

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
                    "Chọn spline trong 3D Sketch; có thể chọn thêm Edge.");
                SketchSegment segment = null;
                Edge optionalEdge = null;
                for (int i = 1; i <= selectedCount; i++)
                {
                    object entity = selection.GetSelectedObject6(i, -1);
                    Log("Selected " + i + ": SketchSegment=" + (entity is SketchSegment) +
                        ", Edge=" + (entity is Edge));
                    if (entity is SketchSegment)
                    {
                        Require(segment == null, "Chỉ chọn một spline.");
                        segment = (SketchSegment)entity;
                    }
                    else if (entity is Edge)
                    {
                        Require(optionalEdge == null, "Chỉ chọn tối đa một Edge.");
                        optionalEdge = (Edge)entity;
                    }
                    else throw new InvalidOperationException("Đối tượng chọn không phải spline hoặc Edge.");
                }
                Require(segment != null &&
                    segment.GetType() == (int)swSketchSegments_e.swSketchSPLINE,
                    "Hãy chọn spline trong 3D Sketch. Arc không hỗ trợ InsertPoint.");
                sketch = segment.GetSketch();
                Require(sketch != null && sketch.Is3D(), "Spline phải thuộc 3D Sketch.");
                Require(model.SketchManager.ActiveSketch != null &&
                    model.SketchManager.ActiveSketch == sketch,
                    "Hãy Edit 3D Sketch chứa spline rồi chạy lại.");
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
                int desiredCount = redistribute ? originalCount : targetCount;
                List<double[]> targets = GetPointsByArcLength(originalCurve,
                    curveStart, curveEnd, desiredCount);
                Log("SplineLength=" + (length * 1000).ToString("F3", CultureInfo.InvariantCulture) +
                    " mm; Mode=" + (redistribute ? "RedistributeExisting" : "Insert") +
                    "; TargetFitPoints=" + desiredCount +
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
                if (redistribute)
                {
                    double initialSpacingError = MaxEqualSpacingError(spline, sourceCurve);
                    Log("InitialMaxSpacingError=" + (initialSpacingError * 1000)
                        .ToString("F4", CultureInfo.InvariantCulture) + " mm");
                    if (initialSpacingError <= SpacingTolerance)
                    {
                        MessageBox.Show(owner,
                            "Các fit point hiện có đã chia đều trong sai số 0.50 mm. " +
                            "Số điểm: " + originalCount + ".",
                            "REPAIR 3D SPLINE", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    List<FitPointSample> initialPoints = OrderedFitPoints(spline, sourceCurve);
                    List<double[]> interiorTargets = targets.Skip(1)
                        .Take(desiredCount - 2).ToList();
                    List<string> oldPointIdsToReplace = initialPoints.Skip(1)
                        .Take(desiredCount - 2)
                        .Where(point => !interiorTargets.Any(target =>
                            Distance(point.Position, target) <= FitPointReuseTolerance))
                        .Select(point => PointKey(point.Point)).ToList();
                    Require(originalCount + oldPointIdsToReplace.Count <= MaxPointCount,
                        "Chèn điểm mới tạm thời sẽ vượt giới hạn " + MaxPointCount + ".");
                    Log("FitPointsToReplace=" + oldPointIdsToReplace.Count);

                    // Insert all target fit points before deleting any original one.
                    // The existing segment remains in the same 3D sketch.
                    for (int i = 1; i < desiredCount - 1; i++)
                    {
                        segment = RefreshSplineSegmentById(sketch, sourceId);
                        spline = (SketchSpline)segment;
                        Curve currentCurve = segment.GetCurve() as Curve;
                        Require(currentCurve != null, "Không lấy được Curve hiện tại của spline.");
                        double[] target = targets[i];
                        if (SplineFitPoints(spline).Any(point =>
                            Distance(Position(point), target) <= FitPointReuseTolerance))
                            continue;
                        double[] onSpline = currentCurve.GetClosestPointOn(
                            target[0], target[1], target[2]) as double[];
                        if (onSpline != null && onSpline.Length >= 4 && Finite(onSpline[3]))
                            onSpline = Evaluate(currentCurve, onSpline[3]);
                        Require(IsPoint(onSpline) &&
                            Distance(target, onSpline) <= ApproximationTolerance,
                            "Spline lệch hình gốc trước khi chèn fit point mới.");
                        if (!recording)
                        {
                            model.Extension.StartRecordingUndoObject();
                            recording = true;
                        }
                        int before = spline.GetPointCount();
                        changed = true;
                        bool accepted = spline.InsertPoint(
                            onSpline[0], onSpline[1], onSpline[2]);
                        if (!accepted && spline.GetPointCount() == before)
                        {
                            double nearest = SplineFitPoints(spline)
                                .Min(point => Distance(Position(point), target));
                            Log("InsertPointRetry[" + i + "]; NearestFitPoint=" +
                                (nearest * 1000).ToString("F6", CultureInfo.InvariantCulture) + " mm");
                            model.EditRebuild3();
                            segment = RefreshSplineSegmentById(sketch, sourceId);
                            spline = (SketchSpline)segment;
                            currentCurve = segment.GetCurve() as Curve;
                            Require(currentCurve != null, "Không đọc được Curve sau rebuild.");
                            double[] retryProjection = currentCurve.GetClosestPointOn(
                                target[0], target[1], target[2]) as double[];
                            Require(retryProjection != null && retryProjection.Length >= 4 &&
                                Finite(retryProjection[3]), "Không đánh giá được vị trí chèn lại.");
                            onSpline = Evaluate(currentCurve, retryProjection[3]);
                            Require(Distance(target, onSpline) <= ApproximationTolerance,
                                "Spline lệch hình gốc khi chèn lại fit point.");
                            accepted = spline.InsertPoint(onSpline[0], onSpline[1], onSpline[2]);
                        }
                        Log("InsertPoint[" + i + "]=" + accepted +
                            "; before=" + before + "; after=" + spline.GetPointCount() +
                            "; Target=" + XYZ(onSpline));
                        Require(accepted,
                            "SOLIDWORKS từ chối chèn fit point mới trên spline thường.");
                        inserted++;
                        segment = RefreshSplineSegmentById(sketch, sourceId);
                        Require(((SketchSpline)segment).GetPointCount() == before + 1,
                            "Số fit point không tăng sau InsertPoint.");
                    }
                    Require(inserted == oldPointIdsToReplace.Count,
                        "Số điểm mới không khớp số điểm cũ cần thay; đã yêu cầu Undo.");
                    segment = RefreshSplineSegmentById(sketch, sourceId);
                    DeviationResult afterInsertion = MeasureDeviation(segment,
                        originalCurve, curveStart, curveEnd);
                    Log("ShapeChangeAfterInsertion=" +
                        (afterInsertion.Distance * 1000).ToString("F4", CultureInfo.InvariantCulture) + " mm");
                    Require(afterInsertion.Distance <= ApproximationTolerance,
                        "Chèn fit point mới đã làm spline lệch quá 0.20 mm.");

                    foreach (string pointId in oldPointIdsToReplace)
                    {
                        segment = RefreshSplineSegmentById(sketch, sourceId);
                        spline = (SketchSpline)segment;
                        SketchPoint oldPoint = SplineFitPoints(spline)
                            .FirstOrDefault(point => PointKey(point) == pointId);
                        Require(oldPoint != null,
                            "Không tìm lại được fit point cũ sau khi chèn điểm mới.");
                        int before = spline.GetPointCount();
                        changed = true;
                        bool accepted = spline.DeletePoint(oldPoint);
                        Log("DeleteOldFitPoint[" + pointId + "]=" + accepted +
                            "; before=" + before + "; after=" + spline.GetPointCount());
                        Require(accepted,
                            "SOLIDWORKS từ chối xóa fit point cũ; đã yêu cầu Undo.");
                        removed++;
                        segment = RefreshSplineSegmentById(sketch, sourceId);
                        Require(((SketchSpline)segment).GetPointCount() == before - 1,
                            "Số fit point không giảm sau DeletePoint.");
                    }
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
                        bool accepted = spline.InsertPoint(onSpline[0], onSpline[1], onSpline[2]);
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
                Require(deviation.Distance <= ApproximationTolerance,
                    "Sau khi chia fit point, hình dạng spline lệch quá 0.20 mm.");
                if (redistribute)
                {
                    Require(spline.GetPointCount() == originalCount,
                        "Số fit point đã đổi sau khi chia đều.");
                    double finalSpacingError = MaxEqualSpacingError(spline,
                        segment.GetCurve() as Curve);
                    Log("FinalMaxSpacingError=" + (finalSpacingError * 1000)
                        .ToString("F4", CultureInfo.InvariantCulture) + " mm");
                    Require(finalSpacingError <= SpacingTolerance,
                        "Sau khi thay fit point, khoảng cách chưa đều trong sai số 0.50 mm.");
                }
                Require(model.Extension.FinishRecordingUndoObject2("DISTRIBUTE FIT POINTS IN 3D SPLINE", false),
                    "Không hoàn tất được Undo group.");
                recording = false;
                model.GraphicsRedraw2();
                Log("InsertedFitPoints=" + inserted + "; RemovedFitPoints=" + removed +
                    "; FinalFitPoints=" + spline.GetPointCount() +
                    "; MaxShapeChange=" + (deviation.Distance * 1000).ToString("F4", CultureInfo.InvariantCulture) + " mm");
                MessageBox.Show(owner,
                    (redistribute ? "Đã thay " + removed + " fit point cũ bằng điểm chia đều.\n" :
                        "Đã thêm " + inserted + " fit point vào chính spline đã chọn.\n") +
                    "Tổng fit point: " + spline.GetPointCount() +
                    "\nKhoảng cách mục tiêu: " + (length * 1000 / (desiredCount - 1))
                        .ToString("F3", CultureInfo.InvariantCulture) + " mm" +
                    "\nĐộ lệch hình dạng: " +
                    (deviation.Distance * 1000).ToString("F3", CultureInfo.InvariantCulture) + " mm" +
                    (rebuilt ? "" : "\nHãy kiểm tra feature khác vì rebuild báo lỗi."),
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
                MessageBox.Show(owner, ex.Message + recovery,
                    "REPAIR 3D SPLINE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { Log("InsertedFitPoints=" + inserted +
                "; RemovedFitPoints=" + removed + "; MovedFitPoints=" + moved); }
        }

        internal static int CalculatePointCount(double length)
        {
            Require(Finite(length) && length > 0, "Chiều dài phải lớn hơn 0.");
            return (int)Math.Max(MinPointCount, Math.Min(MaxPointCount, Math.Ceiling(length / DefaultSpacing) + 1));
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
                double[] edgePoint = edgeCurve.GetClosestPointOn(
                    splinePoint[0], splinePoint[1], splinePoint[2]) as double[];
                Require(IsPoint(edgePoint), "Edge.GetClosestPointOn thất bại.");
                UpdateDeviation(result, splinePoint, edgePoint);
            }

            foreach (double[] edgePoint in SampleByParameter(
                edgeCurve, edgeStart, edgeEnd, DenseValidationSamples))
            {
                double[] splinePoint = splineCurve.GetClosestPointOn(
                    edgePoint[0], edgePoint[1], edgePoint[2]) as double[];
                Require(IsPoint(splinePoint), "Spline.GetClosestPointOn thất bại.");
                UpdateDeviation(result, splinePoint, edgePoint);
            }
            return result;
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

        private static List<SketchPoint> SplineFitPoints(SketchSpline spline)
        {
            var values = spline.GetPoints2() as Array;
            Require(values != null, "Không đọc được fit points của spline.");
            return values.Cast<object>().OfType<SketchPoint>().ToList();
        }
        private static List<FitPointSample> OrderedFitPoints(SketchSpline spline, Curve curve)
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
                    Distance(position, closest) <= 1e-5,
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
            return maxError;
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
