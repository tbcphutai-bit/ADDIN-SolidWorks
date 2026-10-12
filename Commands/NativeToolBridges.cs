using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ADDIN.HoleManagement;
using ADDIN.UI;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SwView = SolidWorks.Interop.sldworks.View;

namespace ADDIN.Commands
{
    internal sealed partial class SplineToArcsCommand
    {
        internal void RunNative(SplineArcOptions options)
        {
            var model = swApp.ActiveDoc as ModelDoc2;
            Edge edge; SwView view; double[] point;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING ||
                !TryGetSelectedCurve(model, out edge, out view, out point)) throw new InvalidOperationException("Chọn cạnh spline trong Drawing View.");
            var curve = edge.GetCurve() as Curve;
            if (curve == null || IsLineOrCircle(curve)) throw new InvalidOperationException("Cạnh đã chọn không phải spline.");
            currentOptions = options; Execute(model, edge, view, point, options);
        }
    }
    internal sealed partial class Repair3DSplineCommand
    {
        internal NativeToolPreview InspectNative(SketchSegment segment)
        {
            var model = app.ActiveDoc as ModelDoc2;
            Require(model != null && model.GetType() == (int)swDocumentTypes_e.swDocPART, "Hãy mở Part.");
            Require(segment != null && (segment.GetType() == (int)swSketchSegments_e.swSketchSPLINE ||
                segment.GetType() == (int)swSketchSegments_e.swSketchARC), "Chọn Arc hoặc spline thường.");
            var sketch = segment.GetSketch();
            Require(sketch != null && sketch.Is3D() && model.SketchManager.ActiveSketch != null &&
                (ReferenceEquals(model.SketchManager.ActiveSketch, sketch) || app.IsSame(model.SketchManager.ActiveSketch, sketch) == (int)swObjectEquality.swObjectSame), "Hãy Edit 3D Sketch chứa Arc / spline.");
            if (segment.GetType() == (int)swSketchSegments_e.swSketchARC)
            {
                double arcStart, arcEnd;
                var reference = GetArcFitReference(segment, out arcStart, out arcEnd);
                double arcLength = reference.GetLength3(arcStart, arcEnd);
                return new NativeToolPreview { Lines = new[] { segment.GetName() + " | Arc → Spline",
                    "Fit trong cùng 3D Sketch đang Edit; giữ Arc gốc làm Construction.",
                    "Giữ bán kính và quan hệ Arc; spline có fit point để chỉnh sửa.",
                    "Ưu tiên ít điểm; thử từ " + MinPointCount + " fit point, kiểm tra sai số ≤ 0.20 mm.",
                    "Chiều dài Arc: " + (arcLength * 1000).ToString("0.###") + " mm",
                    "Chia đều theo chiều dài đường; số điểm được xác định khi chạy." } };
            }
            var spline = segment as SketchSpline;
            Require(spline != null && !spline.IsStyleSpline, "Style Spline không hỗ trợ fit point.");
            int count = spline.GetPointCount(); Require(count >= 2 && count <= MaxPointCount, "Số fit point không hợp lệ.");
            Require(!Relations(segment).Any(r => r.GetRelationType() == (int)swConstraintType_e.swConstraintType_USEEDGE), "Spline còn liên kết On Edge; chuyển sang spline thường trước khi Repair.");
            var curve = segment.GetCurve() as Curve; Require(curve != null, "Không lấy được curve.");
            double start, end; bool closed, periodic;
            Require(curve.GetEndParams(out start, out end, out closed, out periodic) && !closed && end > start, "Chỉ hỗ trợ spline mở.");
            double length = curve.GetLength3(start, end); Require(NativeToolValues.Positive(length), "Chiều dài spline không hợp lệ.");
            int target = count >= MinPointCount ? count : CalculatePointCount(length);
            bool reduce = count > MinPointCount && Relations(segment).Count == 0 && CanResetFreeHandles(spline);
            return new NativeToolPreview { Lines = new[] { segment.GetName(), "Fit point hiện tại: " + count,
                reduce ? "Ưu tiên giảm fit point; giữ sai số hình dạng ≤ 0.20 mm." : "Fit point sau Repair: " + target,
                "Chiều dài: " + (length * 1000).ToString("0.###") + " mm",
                reduce ? "Chia đều theo chiều dài đường; số điểm được kiểm tra khi chạy." :
                    "Bước chia: " + (length * 1000 / (target - 1)).ToString("0.###") + " mm" } };
        }
    }
    public partial class DimKichThuocLo
    {
        private double nativeCalloutOffsetM = .007, nativeLaneStepM = .005;
        internal NativeToolPreview InspectNative(SwView view)
        {
            var math = swApp.IGetMathUtility(); var transform = view.ModelToViewTransform;
            if (math == null || transform == null) throw new InvalidOperationException("Không lấy được transform của View.");
            var holes = CollectVisibleHoles(view, math, transform);
            // Preview visible geometry without AccessSelections (which rolls back a
            // referenced Part). The existing engine resolves pattern seeds after close.
            var groups = GroupPatternHoles(holes);
            if (holes.Count == 0 || groups.Count == 0) throw new InvalidOperationException("Không thấy lỗ trong View đã chọn.");
            return new NativeToolPreview { Lines = groups.Select((g, i) => (i + 1) + " | " +
                (g.Representative.IsSlot ? "Slot " + (g.Representative.ModelRadius * 2000).ToString("0.###") + " × " + (g.Representative.SlotLength * 1000).ToString("0.###") : "φ" + (g.Representative.ModelRadius * 2000).ToString("0.###")) +
                " mm | SL " + g.Holes.Count + (g.SeedFeature == null ? "" : " | " + g.SeedFeature.Name)).ToArray() };
        }
        internal void RunNative(double offsetMm, double laneMm)
        {
            if (!NativeToolValues.Positive(offsetMm) || !NativeToolValues.Positive(laneMm)) throw new InvalidOperationException("Khoảng cách DIM phải lớn hơn 0.");
            double oldOffset = nativeCalloutOffsetM, oldLane = nativeLaneStepM;
            try { nativeCalloutOffsetM = offsetMm / 1000; nativeLaneStepM = laneMm / 1000; GenerateHoleDimensions(); }
            finally { nativeCalloutOffsetM = oldOffset; nativeLaneStepM = oldLane; }
        }
    }
    public partial class TaoDimKegaki
    {
        private double nativeOffsetFactor = 1;
        private bool nativeShowCompletion = true;
        internal void RunNative(double offsetMm, bool showCompletion = true)
        { if (!NativeToolValues.Positive(offsetMm)) throw new InvalidOperationException("Khoảng cách DIM phải lớn hơn 0."); double old = nativeOffsetFactor; bool oldCompletion = nativeShowCompletion; try { nativeOffsetFactor = offsetMm / 10; nativeShowCompletion = showCompletion; GenerateKegakiDimensions(); } finally { nativeOffsetFactor = old; nativeShowCompletion = oldCompletion; } }
    }
    public partial class LenhDimCanhSongSong
    {
        private bool nativeShowCompletion = true;
        internal void RunNative(double offsetMm, bool showCompletion = true)
        { if (!NativeToolValues.Positive(offsetMm)) throw new InvalidOperationException("Khoảng cách DIM phải lớn hơn 0."); double old = DimOffsetMm; bool oldCompletion = nativeShowCompletion; try { DimOffsetMm = offsetMm; nativeShowCompletion = showCompletion; Run(); } finally { DimOffsetMm = old; nativeShowCompletion = oldCompletion; } }
    }
    internal sealed partial class ChangeHoleNameCommand
    {
        internal NativeToolPreview InspectNative(IEnumerable<Feature> features)
        {
            var model = app.ActiveDoc as ModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART) throw new InvalidOperationException("Hãy mở Part.");
            HoleMetadataService.Initialize(app);
            var resolver = new HoleFamilyResolver(model); var families = new HashSet<HoleFamily>();
            foreach (var feature in features) { var family = resolver.Find(feature); if (family == null) throw new InvalidOperationException("Không xác định được họ lỗ: " + feature.Name); families.Add(family); }
            return new NativeToolPreview { Lines = families.SelectMany((family, index) => family.Features.Select(f => "Họ " + (index + 1) + " | " + f.Name + " | " + (HoleFeatureClassifier.Classify(f) == HoleFeatureKind.Pattern ? "Pattern" : "Seed"))).ToArray() };
        }
    }
    public partial class LenhNoteTextBalloon
    {
        internal NativeToolPreview InspectNativeBalloon(string property)
        { var model = swApp.ActiveDoc as ModelDoc2; string value = GetSelectedBalloonPropertyValue(model, property); if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Không đọc được " + property + " từ chi tiết đã chọn."); return new NativeToolPreview { Lines = new[] { "Thuộc tính: " + property, "Giá trị: " + value } }; }
        internal void InsertNativeNote(string text) { if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Nhập nội dung Note."); noteComboBox.Text = text; InsertNote(); }
        internal void InsertNativeText(string text) { if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Nhập suffix DIM."); textComboBox.Text = text; InsertText(); }
        internal void InsertNativeBalloon(string property) { balloonPropertyComboBox.Text = property; InsertBalloon(); }
    }
    public partial class ChinhTiLeDrawingView
    {
        internal bool TryGetQuickView(out SwView view)
        {
            view = null;
            var model = swApp.ActiveDoc as ModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING) return false;
            var selection = model.SelectionManager as SelectionMgr;
            int count = selection == null ? 0 : selection.GetSelectedObjectCount2(-1);
            if (count == 0) return false;
            SwView target = null;
            for (int i = 1; i <= count; i++) {
                int type = selection.GetSelectedObjectType3(i, -1);
                SwView candidate;
                if (type == (int)swSelectType_e.swSelDRAWINGVIEWS)
                    candidate = selection.GetSelectedObject6(i, -1) as SwView;
                else if (type == (int)swSelectType_e.swSelEDGES || type == (int)swSelectType_e.swSelDIMENSIONS)
                    candidate = selection.GetSelectedObjectsDrawingView2(i, -1) as SwView;
                else return false;
                if (candidate == null || candidate.Type == (int)swDrawingViewTypes_e.swDrawingSheet) return false;
                if (target != null && !ReferenceEquals(target, candidate) && swApp.IsSame(target, candidate) != (int)swObjectEquality.swObjectSame)
                    return false;
                target = candidate;
            }
            view = target;
            return true;
        }

        internal NativeToolPreview InspectNative(SwView view, double? requestedScale = null)
        {
            double sw, sh, vw, vh;
            var drawing = swApp.ActiveDoc as DrawingDoc;
            if (drawing == null || !TryGetSheetSize(drawing, out sw, out sh) || !TryGetViewSize(view, out vw, out vh) || vw <= 0 || vh <= 0)
                throw new InvalidOperationException("Không đọc được kích thước View / Sheet.");
            double scale = requestedScale ?? view.ScaleDecimal * (vw / vh > 5 ? .7 * sw / vw : Math.Min(sh / 2.3 / vh, .8 * sw / vw));
            double n, d; NormalizeScaleToIntegerRatio(scale, out n, out d);
            if (!NativeToolValues.Positive(scale)) throw new InvalidOperationException("Tỉ lệ View không hợp lệ.");
            return new NativeToolPreview { SuggestedScale = n / d, Lines = new[] { view.GetName2(), "Tỉ lệ hiện tại: " + view.ScaleDecimal.ToString("0.######"),
                (requestedScale.HasValue ? "Tỉ lệ tùy chỉnh sau làm tròn: " : "Tỉ lệ đề xuất: ") + n.ToString(CultureInfo.InvariantCulture) + ":" + d.ToString(CultureInfo.InvariantCulture), "Sheet: " + (sw * 1000).ToString("0.##") + " × " + (sh * 1000).ToString("0.##") + " mm" } };
        }
        internal void RunNative(double scale, bool center)
        {
            SwView view;
            if (!TryGetQuickView(out view)) throw new InvalidOperationException("Chọn một Drawing View hoặc cạnh / DIM thuộc View.");
            RunNative(view, scale, center);
        }
        internal void RunNative(SwView view, double scale, bool center)
        {
            if (!NativeToolValues.Positive(scale)) throw new InvalidOperationException("Tỉ lệ phải lớn hơn 0.");
            var model = swApp.ActiveDoc as ModelDoc2; var drawing = model as DrawingDoc;
            if (drawing == null || view == null) throw new InvalidOperationException("Chọn một Drawing View.");
            double sw, sh;
            if (!TryGetSheetSize(drawing, out sw, out sh)) throw new InvalidOperationException("Không đọc được khổ Sheet.");
            SetViewScale(view, scale); model.GraphicsRedraw2();
            if (center) SetViewOutlineCenter(view, sw / 2, sh / 2);
            model.ForceRebuild3(false); model.GraphicsRedraw2();
        }
    }
}
