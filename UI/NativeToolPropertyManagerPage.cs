using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN
{
    internal enum NativeToolKind { SplineArcs, RepairSpline, HoleDimensions, SectionDimensions, KegakiDimensions, RenameHole, Note, DimensionText, Balloon, ViewScale }
}
namespace ADDIN.UI
{
    internal sealed class NativeToolValues
    {
        internal bool Automatic = true, Split = true, Radius = true, Step = true, Center = true;
        internal double Count = 4, ToleranceMm = .1, MaximumRadiusMm = 300, OffsetMm = 10, LaneMm = 5, Scale = 1;
        internal string Text = "", Property = "部品番号";
        internal NativeToolValues Copy() { return (NativeToolValues)MemberwiseClone(); }
        internal static bool Positive(double n) { return n > 0 && !double.IsNaN(n) && !double.IsInfinity(n); }
        internal bool Valid(NativeToolKind kind, out string message)
        {
            message = "Tham số chưa hợp lệ.";
            if (kind == NativeToolKind.SplineArcs && (!Positive(ToleranceMm) || !Positive(MaximumRadiusMm) ||
                (!Automatic && (!Positive(Count) || Count != Math.Floor(Count) || Count > 200)))) return false;
            if ((kind == NativeToolKind.SectionDimensions || kind == NativeToolKind.KegakiDimensions || kind == NativeToolKind.HoleDimensions) && !Positive(OffsetMm)) return false;
            if (kind == NativeToolKind.HoleDimensions && !Positive(LaneMm)) return false;
            if (kind == NativeToolKind.ViewScale && !Automatic && !Positive(Scale)) return false;
            if ((kind == NativeToolKind.Note || kind == NativeToolKind.DimensionText || kind == NativeToolKind.RenameHole) && string.IsNullOrWhiteSpace(Text))
            { message = "Nhập nội dung trước khi bấm ✓."; return false; }
            if (kind == NativeToolKind.Balloon && Property != "部品番号" && Property != "合番") return false;
            message = "Sẵn sàng. Bấm ✓ để chạy."; return true;
        }
        internal SplineArcOptions ArcOptions()
        { return new SplineArcOptions { AutomaticStep = Automatic, ManualSegmentCount = (int)Count, MaximumDeviationMm = ToleranceMm,
            MaximumRadiusMm = MaximumRadiusMm, SplitWhenOverTolerance = Split, AddRadiusDimensions = Radius, AddStepDimensions = Step }; }
    }
    internal sealed class NativeToolPreview
    {
        internal string[] Lines = new string[0];
        internal double SuggestedScale;
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class NativeToolPropertyManagerPage : NativeCommandPropertyManagerPage
    {
        private readonly NativeToolKind kind;
        private readonly Func<object[], NativeToolValues, NativeToolPreview> inspect;
        private readonly Action<NativeToolValues> execute;
        private readonly string[] presets;
        private NativeToolValues values;
        private readonly Dictionary<int, PropertyManagerPageNumberbox> numbers = new Dictionary<int, PropertyManagerPageNumberbox>();
        private readonly Dictionary<int, PropertyManagerPageCheckbox> checks = new Dictionary<int, PropertyManagerPageCheckbox>();
        private PropertyManagerPageTextbox textBox;
        private PropertyManagerPageCombobox propertyBox, presetBox;
        private PropertyManagerPageListbox previewBox;
        private object[] scanned;
        private NativeToolPreview preview;
        private bool scannedAutomatic;
        private double scannedScale;
        private PropertyManagerPageSelectionbox primarySelection;
        private string selectionError;
        private readonly Action<Action> deferPreview;
        private int previewRequest;
        private bool previewPending;
        private readonly List<Pick> commandSelection = new List<Pick>();
        private bool NativeDrawingSelection { get { return kind == NativeToolKind.ViewScale || kind == NativeToolKind.HoleDimensions || kind == NativeToolKind.SplineArcs; } }
        private bool AutomaticPreview { get { return kind == NativeToolKind.ViewScale || kind == NativeToolKind.HoleDimensions || kind == NativeToolKind.RepairSpline; } }

        internal NativeToolPropertyManagerPage(ISldWorks app, NativeToolKind kind, NativeToolValues values, string[] presets,
            Func<object[], NativeToolValues, NativeToolPreview> inspect, Action<NativeToolValues> execute, Action<NativeCommandPropertyManagerPage> closed,
            Action<Action> deferPreview = null)
            : base(app, Title(kind), closed)
        { this.kind = kind; this.values = values.Copy(); this.presets = presets ?? new string[0]; this.inspect = inspect; this.execute = execute; this.deferPreview = deferPreview; }
        internal static string Title(NativeToolKind kind)
        {
            switch (kind) {
                case NativeToolKind.SplineArcs: return "SPLINE → CUNG R";
                case NativeToolKind.RepairSpline: return "REPAIR 3D SPLINE";
                case NativeToolKind.HoleDimensions: return "DIM KÍCH THƯỚC LỖ";
                case NativeToolKind.SectionDimensions: return "DIM MẶT CẮT";
                case NativeToolKind.KegakiDimensions: return "DIM KEGAKI";
                case NativeToolKind.RenameHole: return "CHANGE NAME HOLE";
                case NativeToolKind.Note: return "NOTE";
                case NativeToolKind.DimensionText: return "TEXT DIM";
                case NativeToolKind.Balloon: return "BALLOON";
                default: return "FIX TỈ LỆ VIEW";
            }
        }
        protected override bool Supports(int type)
        { return type == (int)((kind == NativeToolKind.RepairSpline || kind == NativeToolKind.RenameHole) ? swDocumentTypes_e.swDocPART : swDocumentTypes_e.swDocDRAWING); }
        protected override bool UsesSelectionBoxes { get { return kind != NativeToolKind.Note; } }
        protected override int SelectionLimit(int mark)
        { return mark == 1 && (kind == NativeToolKind.RenameHole || kind == NativeToolKind.DimensionText) ? int.MaxValue : 1; }
        protected override int Count(int mark)
        { return NativeDrawingSelection && mark == 1 && !Initializing && primarySelection != null ? commandSelection.Count : base.Count(mark); }
        protected override object Selected(int mark)
        { return NativeDrawingSelection && mark == 1 && !Initializing ? (commandSelection.Count == 1 ? commandSelection[0].Item : null) : base.Selected(mark); }
        private swSelectType_e[] Filters()
        {
            switch (kind) {
                case NativeToolKind.RepairSpline: return new[] { swSelectType_e.swSelSKETCHSEGS };
                case NativeToolKind.RenameHole: return new[] { swSelectType_e.swSelBODYFEATURES };
                case NativeToolKind.DimensionText: return new[] { swSelectType_e.swSelDIMENSIONS };
                case NativeToolKind.ViewScale: case NativeToolKind.HoleDimensions: return new[] { swSelectType_e.swSelDRAWINGVIEWS, swSelectType_e.swSelEDGES, swSelectType_e.swSelDIMENSIONS };
                case NativeToolKind.SplineArcs: case NativeToolKind.SectionDimensions: return new[] { swSelectType_e.swSelEDGES };
                case NativeToolKind.Balloon: return new[] { swSelectType_e.swSelEDGES, swSelectType_e.swSelFACES, swSelectType_e.swSelVERTICES, swSelectType_e.swSelSKETCHPOINTS };
                default: return new[] { swSelectType_e.swSelDRAWINGVIEWS };
            }
        }
        protected override int SelectionMark(object item, int type)
        {
            if (kind == NativeToolKind.Note) return 0;
            if (kind == NativeToolKind.RepairSpline && type == (int)swSelectType_e.swSelEDGES && item is Edge) return 2;
            if (!Filters().Any(t => (int)t == type)) return 0;
            if (kind == NativeToolKind.RepairSpline)
                return item is SketchSegment && (((SketchSegment)item).GetType() == (int)swSketchSegments_e.swSketchSPLINE ||
                    ((SketchSegment)item).GetType() == (int)swSketchSegments_e.swSketchARC) ? 1 : 0;
            if (kind == NativeToolKind.RenameHole) return item is Feature ? 1 : 0;
            if (kind == NativeToolKind.DimensionText) return item is DisplayDimension ? 1 : 0;
            return item != null ? 1 : 0;
        }
        protected override IEnumerable<Pick> PrepareInitialSelections(List<Pick> picks)
        {
            if (kind != NativeToolKind.ViewScale && kind != NativeToolKind.HoleDimensions) return picks;
            SolidWorks.Interop.sldworks.View target = null;
            foreach (var pick in picks) {
                var candidate = pick.Type == (int)swSelectType_e.swSelDRAWINGVIEWS ? pick.Item as SolidWorks.Interop.sldworks.View :
                    pick.Type == (int)swSelectType_e.swSelEDGES || pick.Type == (int)swSelectType_e.swSelDIMENSIONS ? pick.View : null;
                if (candidate == null || candidate.Type == (int)swDrawingViewTypes_e.swDrawingSheet || (target != null && !Same(target, candidate)))
                    return new Pick[0];
                target = candidate;
            }
            return target == null ? (IEnumerable<Pick>)new Pick[0] : new[] { new Pick {
                Item = target, View = target, Type = (int)swSelectType_e.swSelDRAWINGVIEWS, Mark = 1 } };
        }
        protected override void OnShown()
        {
            if (!Interactive) return;
            // Show2 can reset the Enabled flag set while controls are being built.
            UpdateParameterAvailability();
            if (kind == NativeToolKind.RepairSpline)
            {
                if (Count(1) == 0) primarySelection.SetSelectionFocus();
                RequestSplinePreview(); return;
            }
            if (!NativeDrawingSelection) return;
            FocusDrawingSelection();
            if (primarySelection.ItemCount == 0 && deferPreview != null) deferPreview(FocusDrawingSelection);
            RequestDrawingSelection();
        }
        public override void AfterActivation()
        {
            if (NativeDrawingSelection && !Closing && primarySelection != null && primarySelection.ItemCount == 0)
                primarySelection.SetSelectionFocus();
        }
        private void FocusDrawingSelection()
        { if (Interactive && primarySelection.ItemCount == 0) primarySelection.SetSelectionFocus(); }
        private void RequestDrawingSelection()
        {
            if (!NativeDrawingSelection) return;
            int request = ++previewRequest;
            previewPending = true; commandSelection.Clear(); scanned = null; preview = null; selectionError = null;
            if (previewBox != null) previewBox.Clear();
            Action refresh = () => {
                if (!Interactive || request != previewRequest) return;
                previewPending = false;
                Handle(RefreshDrawingSelection);
            };
            // Native selection-box indices identify the actual items even when their
            // selection marks differ. Read geometry after the selection callback.
            if (deferPreview == null) refresh(); else deferPreview(refresh);
        }
        protected override void OnReleased(bool accepted)
        { ++previewRequest; previewPending = false; commandSelection.Clear(); }
        private void RefreshDrawingSelection()
        {
            if (!NativeDrawingSelection) return;
            commandSelection.Clear(); scanned = null; preview = null; selectionError = null;
            if (previewBox != null) previewBox.Clear();
            try {
                if (primarySelection.ItemCount != 1) return;
                int index = primarySelection.get_SelectionIndex(0);
                if (index < 1) return;
                var pick = new Pick { Item = Manager.GetSelectedObject6(index, -1),
                    Type = Manager.GetSelectedObjectType3(index, -1), Mark = 1,
                    View = Manager.GetSelectedObjectsDrawingView2(index, -1) };
                if (kind == NativeToolKind.SplineArcs) {
                    var edge = pick.Item as Edge;
                    if (edge == null || pick.View == null) { selectionError = "Chọn cạnh spline trong Drawing View."; return; }
                    var curve = edge.GetCurve() as Curve;
                    if (curve == null || curve.IsLine() || curve.IsCircle()) { selectionError = "Cạnh đã chọn không phải spline / đường cong."; return; }
                    commandSelection.Add(pick); return;
                }
                var view = pick.Item as SolidWorks.Interop.sldworks.View ?? pick.View;
                if (view == null || view.Type == (int)swDrawingViewTypes_e.swDrawingSheet) { selectionError = "Chọn một Drawing View của chi tiết."; return; }
                pick.Item = view; pick.View = view; pick.Type = (int)swSelectType_e.swSelDRAWINGVIEWS;
                commandSelection.Add(pick);
                string message;
                if (!values.Valid(kind, out message)) return;
                var picks = Picks(); var result = inspect(picks, values.Copy());
                if (result == null || result.Lines == null || result.Lines.Length == 0) { selectionError = "Không có kết quả phù hợp trong View đã chọn."; return; }
                scanned = picks;
                if (!ScanMatches()) { scanned = null; return; }
                preview = result; scannedAutomatic = values.Automatic; scannedScale = values.Scale;
                previewBox.AddItems(result.Lines);
            }
            catch (Exception ex) { scanned = null; preview = null; selectionError = ex.Message; }
        }
        private void RequestSplinePreview()
        {
            if (!Interactive || kind != NativeToolKind.RepairSpline) return;
            int request = ++previewRequest;
            previewPending = true; scanned = null; preview = null; selectionError = null;
            if (previewBox != null) previewBox.Clear();
            Action update = () => {
                if (!Interactive || request != previewRequest) return;
                Handle(() => {
                    previewPending = false;
                    if (Count(1) != 1 || Count(2) > 1) return;
                    try {
                        var picks = Picks(); var result = inspect(picks, values.Copy());
                        if (result == null || result.Lines == null || result.Lines.Length == 0)
                            throw new InvalidOperationException("Không có kết quả phù hợp cho spline.");
                        scanned = picks;
                        if (!ScanMatches()) { scanned = null; return; }
                        preview = result; previewBox.AddItems(result.Lines);
                    }
                    catch (Exception ex) { scanned = null; preview = null; selectionError = ex.Message; }
                });
            };
            // Curve/fit point checks run after the selection callback, without another scan click.
            if (deferPreview == null) update(); else deferPreview(update);
        }
        private void Check(PropertyManagerPageGroup group, int id, string title, bool value)
        { var box = (PropertyManagerPageCheckbox)Control(group, id, swPropertyManagerPageControlType_e.swControlType_Checkbox, title); box.Checked = value; checks[id] = box; }
        private void Number(PropertyManagerPageGroup group, int id, string title, double value, bool length, double minimum, double maximum)
        {
            Label(group, 1000 + id, title, 14);
            var box = (PropertyManagerPageNumberbox)Control(group, id, swPropertyManagerPageControlType_e.swControlType_Numberbox, title);
            box.SetRange2((int)(length ? swNumberboxUnitType_e.swNumberBox_Length : swNumberboxUnitType_e.swNumberBox_UnitlessDouble),
                minimum, maximum, true, length ? .001 : 1, length ? .01 : 10, length ? .0001 : .1);
            if (length) box.DisplayedUnit = (int)swLengthUnit_e.swMM;
            box.Value = length ? value / 1000 : value; numbers[id] = box;
        }
        protected override void BuildControls()
        {
            // Steps are numbered as they appear; a command without parameters has no step 2.
            int step = 1;
            var group = Group(10, step++ + (kind == NativeToolKind.HoleDimensions ? ". View cần tạo DIM lỗ" : ". Đối tượng"));
            if (UsesSelectionBoxes) {
                // A single box needs no caption: the message above the groups says what to pick.
                if (kind == NativeToolKind.RepairSpline) Label(group, 1011, "Arc / spline cần sửa", 14);
                var box = Selection(group, 11, kind == NativeToolKind.RenameHole ? "Feature lỗ / Pattern" : kind == NativeToolKind.DimensionText ? "DIM cần thêm suffix" : "Đối tượng cần xử lý", 1, Filters());
                primarySelection = box;
                box.SingleEntityOnly = SelectionLimit(1) == 1; if (!box.SingleEntityOnly) box.Height = 70;
                if (kind == NativeToolKind.RepairSpline) { Label(group, 1012, "Cạnh tham chiếu (có thể để trống)", 14); Selection(group, 12, "Edge tham chiếu (tùy chọn)", 2, swSelectType_e.swSelEDGES); }
            }
            var options = kind == NativeToolKind.RepairSpline ? null : Group(20, step++ + (kind == NativeToolKind.HoleDimensions ? ". Vị trí chữ DIM" : kind == NativeToolKind.SplineArcs ? ". Nội suy cung R" : ". Tham số"));
            if (kind == NativeToolKind.SplineArcs) {
                Check(options, 21, "Tự tính số cung", values.Automatic);
                Number(options, 22, "Số cung (khi tắt tự động)", values.Count, false, 1, 200);
                Number(options, 23, "Sai số tối đa (mm)", values.ToleranceMm, true, .000000001, 1);
                Number(options, 24, "Bán kính tối đa (mm)", values.MaximumRadiusMm, true, .000000001, 1000);
                Check(options, 25, "Chia thêm cung khi vượt sai số", values.Split);
                Check(options, 26, "Tạo DIM R", values.Radius); Check(options, 27, "Tạo DIM bước", values.Step);
            }
            if (kind == NativeToolKind.HoleDimensions || kind == NativeToolKind.SectionDimensions || kind == NativeToolKind.KegakiDimensions)
                Number(options, 28, kind == NativeToolKind.HoleDimensions ? "Cách đường bao (mm)" : "Khoảng cách DIM (mm)", values.OffsetMm, true, .000000001, 1);
            if (kind == NativeToolKind.HoleDimensions) Number(options, 29, "Khoảng cách giữa các hàng (mm)", values.LaneMm, true, .000000001, 1);
            if (kind == NativeToolKind.RenameHole || kind == NativeToolKind.Note || kind == NativeToolKind.DimensionText) {
                if (presets.Length > 0) presetBox = Combo(options, 31, "Nội dung đã lưu", presets);
                textBox = Text(options, 30, kind == NativeToolKind.RenameHole ? "Hole Type" : kind == NativeToolKind.Note ? "Nội dung Note" : "Suffix DIM"); textBox.Text = values.Text;
            }
            if (kind == NativeToolKind.Balloon) { propertyBox = Combo(options, 32, "Thuộc tính Balloon", new[] { "部品番号", "合番" }); propertyBox.CurrentSelection = (short)(values.Property == "合番" ? 1 : 0); }
            if (kind == NativeToolKind.ViewScale) {
                Check(options, 21, "Tỉ lệ đề xuất theo khổ giấy", values.Automatic);
                Number(options, 33, "Tỉ lệ tùy chỉnh (0.5 = 1:2)", values.Scale, false, .000001, 1000);
                Check(options, 34, "Đưa View vào giữa Sheet", values.Center);
            }
            if (inspect != null) { var result = Group(40, step + (kind == NativeToolKind.HoleDimensions ? ". Lỗ trong View" : ". Xem trước")); if (!AutomaticPreview) Button(result, 41, "Quét / xem trước"); previewBox = List(result, 42, "Kết quả", new string[0]); previewBox.Height = (short)(kind == NativeToolKind.HoleDimensions ? 80 : 96); }
            UpdateParameterAvailability();
        }
        protected override string HelpText
        {
            get
            {
            string hint = "✓ chạy lệnh; ✕ hủy và khôi phục lựa chọn ban đầu.";
            if (kind == NativeToolKind.SplineArcs) hint += "\nClick gần đầu spline để chọn hướng chạy.\nChọn sẵn cạnh spline rồi bấm lệnh thì chạy ngay với thông số mặc định, không mở bảng này.";
            if (kind == NativeToolKind.RepairSpline) hint += "\nEdit 3D Sketch rồi chọn Arc hoặc spline thường.\nArc sẽ được giữ làm Construction; fit spline trong cùng sketch.";
            if (kind == NativeToolKind.RenameHole) hint += "\nToàn bộ Seed / Pattern trong họ lỗ sẽ đổi tên; xem danh sách trước khi ✓.";
            if (kind == NativeToolKind.HoleDimensions) hint += "\nChọn View: danh sách lỗ tự cập nhật. ✓ tạo DIM.\n7 / 5 mm chỉ chỉnh vị trí chữ DIM trên giấy.\nDIM lỗ cũ do lệnh tạo sẽ được thay thế trong View đã chọn.";
            if (kind == NativeToolKind.KegakiDimensions) hint += "\nCác DIM trong View đã chọn sẽ được tạo lại theo engine hiện tại.";
            if (kind == NativeToolKind.ViewScale) hint += "\nChọn sẵn hoặc chọn trực tiếp View / cạnh / DIM; tỉ lệ đề xuất tự cập nhật.\nTỉ lệ được làm tròn về n:1 hoặc 1:n như lệnh hiện tại.";
            return hint;
            }
        }
        private void UpdateParameterAvailability()
        {
            int id = kind == NativeToolKind.SplineArcs ? 22 : kind == NativeToolKind.ViewScale ? 33 : 0;
            PropertyManagerPageNumberbox box;
            if (id != 0 && numbers.TryGetValue(id, out box)) ((PropertyManagerPageControl)box).Enabled = !values.Automatic;
        }
        private object[] Picks()
        {
            if (NativeDrawingSelection) return commandSelection.Select(p => p.Item).ToArray();
            var list = new List<object>();
            for (int i = 1; i <= Count(1); i++) {
                object item = Manager.GetSelectedObject6(i, 1);
                if (kind == NativeToolKind.ViewScale && !(item is SolidWorks.Interop.sldworks.View))
                    item = Manager.GetSelectedObjectsDrawingView2(i, 1);
                list.Add(item);
            }
            for (int i = 1; i <= Count(2); i++) list.Add(Manager.GetSelectedObject6(i, 2));
            return list.ToArray();
        }
        private bool ScanMatches()
        { var picks = Picks(); return scanned != null && picks.Length == scanned.Length && picks.Select((p, i) => Same(p, scanned[i])).All(x => x); }
        private void CacheNumber(int id, double value)
        {
            switch (id) {
                case 22: values.Count = value; break;
                case 23: values.ToleranceMm = value * 1000; break;
                case 24: values.MaximumRadiusMm = value * 1000; break;
                case 28: values.OffsetMm = value * 1000; break;
                case 29: values.LaneMm = value * 1000; break;
                case 33: values.Scale = value; break;
            }
        }
        protected override bool Validate(out string message)
        {
            if (!values.Valid(kind, out message)) return false;
            if (AutomaticPreview && previewPending) { message = kind == NativeToolKind.RepairSpline ? "Đang kiểm tra Arc / spline…" : kind == NativeToolKind.HoleDimensions ? "Đang nhận diện lỗ trong View…" : kind == NativeToolKind.SplineArcs ? "Đang kiểm tra cạnh spline…" : "Đang cập nhật tỉ lệ đề xuất…"; return false; }
            if (UsesSelectionBoxes && (Count(1) < 1 || Count(1) > SelectionLimit(1) || Count(2) > (kind == NativeToolKind.RepairSpline ? 1 : 0)))
            { message = selectionError ?? (kind == NativeToolKind.HoleDimensions ? "Chọn View hoặc cạnh thuộc View cần tạo DIM lỗ." : kind == NativeToolKind.SplineArcs ? "Chọn cạnh spline trong Drawing View." : "Chọn đối tượng phù hợp trong ô chọn của lệnh."); return false; }
            if (kind == NativeToolKind.SectionDimensions) {
                var edge = Selected(1) as Edge;
                if (edge == null || Manager.GetSelectedObjectsDrawingView2(1, 1) == null) { message = "Chọn cạnh thuộc Drawing View."; return false; }
            }
            if (inspect != null && (preview == null || !ScanMatches())) { message = AutomaticPreview ? selectionError ?? (kind == NativeToolKind.RepairSpline ? "Chọn Arc hoặc spline thường trong 3D Sketch đang Edit." : "Chọn một View hoặc cạnh / DIM thuộc View để tự cập nhật kết quả.") : "Bấm Quét / xem trước sau khi chọn đối tượng."; return false; }
            if (kind == NativeToolKind.ViewScale && (values.Automatic != scannedAutomatic || (!values.Automatic && values.Scale != scannedScale)))
            { message = "Tham số tỉ lệ thay đổi; bấm xem trước lại."; return false; }
            if (kind == NativeToolKind.ViewScale && values.Automatic && !NativeToolValues.Positive(preview.SuggestedScale)) return false;
            return true;
        }
        public override void OnSelectionboxListChanged(int id, int count)
        { Handle(() => { scanned = null; preview = null; if (previewBox != null) previewBox.Clear(); if (kind == NativeToolKind.RepairSpline) RequestSplinePreview(); else RequestDrawingSelection(); }); }
        public override bool OnSubmitSelection(int id, object item, int type, ref string text)
        { try { return !Closing && SelectionMark(item, type) == (id == 12 ? 2 : id == 11 ? 1 : 0) && (id == 11 || id == 12); } catch { return false; } }
        public override void OnButtonPress(int id)
        {
            Handle(() => {
                if (id != 41 || inspect == null) return;
                if (AutomaticPreview) { if (kind == NativeToolKind.RepairSpline) RequestSplinePreview(); else RequestDrawingSelection(); return; }
                scanned = null; preview = null; previewBox.Clear();
                if (Count(1) < 1 || Count(1) > SelectionLimit(1)) throw new InvalidOperationException("Chọn đối tượng trước khi quét.");
                var picks = Picks(); var result = inspect(picks, values.Copy());
                if (result == null || result.Lines == null || result.Lines.Length == 0) throw new InvalidOperationException("Không có kết quả phù hợp.");
                scanned = picks; if (!ScanMatches()) { scanned = null; throw new InvalidOperationException("Lựa chọn thay đổi; hãy quét lại."); }
                preview = result; scannedAutomatic = values.Automatic; scannedScale = values.Scale; previewBox.AddItems(result.Lines);
            });
        }
        public override void OnNumberboxChanged(int id, double value) { Handle(() => { CacheNumber(id, value); if (kind == NativeToolKind.ViewScale || (kind == NativeToolKind.HoleDimensions && preview == null)) RequestDrawingSelection(); }); }
        public override void OnCheckboxCheck(int id, bool value)
        { Handle(() => { switch (id) { case 21: values.Automatic = value; UpdateParameterAvailability(); break; case 25: values.Split = value; break; case 26: values.Radius = value; break; case 27: values.Step = value; break; case 34: values.Center = value; break; } if (kind == NativeToolKind.ViewScale) RequestDrawingSelection(); }); }
        public override void OnTextboxChanged(int id, string value) { Handle(() => { if (id == 30) values.Text = value; }); }
        public override void OnComboboxSelectionChanged(int id, int item)
        { Handle(() => { if (id == 31 && item >= 0 && item < presets.Length) { values.Text = presets[item]; textBox.Text = values.Text; } if (kind == NativeToolKind.Balloon && id == 32) { values.Property = item == 1 ? "合番" : "部品番号"; scanned = null; preview = null; previewBox.Clear(); } }); }
        protected override Action CaptureAccepted()
        {
            // SOLIDWORKS can destroy native input controls before OnClose. All input
            // values and drawing picks are cached while the page is still interactive.
            string message;
            if (kind == NativeToolKind.ViewScale) {
                if (!values.Valid(kind, out message) || Count(1) != 1 || previewPending || preview == null || !ScanMatches())
                    throw new InvalidOperationException("Chờ tỉ lệ đề xuất cập nhật và chọn một View hợp lệ trước khi ✓.");
                var snapshot = values.Copy(); var view = Picks()[0];
                // Capture controls only in OnClose; geometry and scale calculation run after teardown.
                return () => {
                    var result = inspect(new[] { view }, snapshot.Copy());
                    if (result == null || !NativeToolValues.Positive(result.SuggestedScale)) throw new InvalidOperationException("Tỉ lệ View không hợp lệ.");
                    snapshot.Scale = result.SuggestedScale; execute(snapshot);
                };
            }
            if (!Validate(out message)) throw new InvalidOperationException(message);
            var acceptedValues = values.Copy();
            return () => execute(acceptedValues);
        }
    }
}
