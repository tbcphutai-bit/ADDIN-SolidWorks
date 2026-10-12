using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace ADDIN.UI
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class MakeHolePropertyManagerPage : PropertyManagerPage2Handler9
    {
        private const int SelectionGroupId = 1, EdgeId = 2, FaceId = 3, SidePointId = 4, SeedId = 5;
        private const int HoleGroupId = 10, SizeId = 11, LabelId = 12;
        private const int SpacingGroupId = 20, DirectionId = 21, EdgeOffsetId = 22;
        private const int LeftOffsetId = 23, RightOffsetId = 24, PitchId = 25;
        private const int HintGroupId = 30, HintId = 31;
        private const int PatternGroupId = 40, PatternId = 41, PatternSummaryId = 42;
        private const int EdgeMark = 1, FaceMark = 2, PointMark = 4, SeedMark = 8;
        private readonly ISldWorks app;
        private readonly ModelDoc2 model;
        private MakeHolePageValues draft;
        private readonly MakeHolePageValues openingValues;
        private readonly string[] sizes;
        private readonly string[] labels;
        private readonly Action<MakeHolePageValues, MakeHoleOptions> execute;
        private readonly Action<MakeHolePropertyManagerPage> closed;
        private readonly Action<double> updatePattern;
        private readonly Action resetPending;
        private readonly Action<bool> updateAvailability;
        private readonly Action continuePattern;
        private readonly Func<bool> patternReady;
        private readonly string patternSummary;
        private PropertyManagerPageButton patternButton;
        private bool patternRequested, acceptedPattern;
        private readonly List<Pick> initialSelection = new List<Pick>();
        private readonly Dictionary<int, PropertyManagerPageSelectionbox> selectionBoxes = new Dictionary<int, PropertyManagerPageSelectionbox>();
        private readonly Dictionary<int, List<Pick>> currentSelection = new Dictionary<int, List<Pick>>();
        private readonly Action<Action> deferValidation;
        private int selectionRequest;
        private bool selectionPending;
        private string selectionError;
        private PropertyManagerPage2 page;
        private PropertyManagerPageSelectionbox edgeBox;
        private PropertyManagerPageCombobox sizeBox, labelBox, directionBox;
        private PropertyManagerPageNumberbox edgeOffsetBox, leftOffsetBox, rightOffsetBox, pitchBox;

        private bool updateRequested, resetRequested, acceptedUpdate;
        private PropertyManagerPageBitmap diagram;
        private string diagramPath;
        private ModelDoc2 ownerPart;
        private Component2 editComponent;
        private string configuration;
        private bool shown, closing, dismissed, released, initializing;
        private MakeHoleOptions acceptedOptions;
        private MakeHolePageValues acceptedValues;
        private List<Pick> acceptedSelection;
        private string closeError;

        private sealed class Pick
        {
            internal object Item;
            internal int Type, Mark;
            internal Component2 Component;
            internal double[] Point;
        }

        internal MakeHolePropertyManagerPage(ISldWorks app, MakeHolePageValues values,
            string[] sizes, string[] labels, Action<MakeHolePageValues, MakeHoleOptions> execute,
            Action<MakeHolePropertyManagerPage> closed, Action<Action> deferValidation = null,
            Action<double> updatePattern = null, Action resetPending = null, Action<bool> updateAvailability = null,
            Action continuePattern = null, Func<bool> patternReady = null, string patternSummary = null)
        {
            this.app = app;
            model = app == null ? null : app.ActiveDoc as ModelDoc2;
            draft = (values ?? new MakeHolePageValues()).Copy();
            openingValues = draft.Copy();
            this.sizes = sizes ?? new string[0];
            this.labels = labels ?? new string[0];
            this.execute = execute;
            this.closed = closed;
            this.deferValidation = deferValidation;
            this.updatePattern = updatePattern;
            this.resetPending = resetPending;
            this.updateAvailability = updateAvailability;
            this.continuePattern = continuePattern;
            this.patternReady = patternReady;
            this.patternSummary = patternSummary ?? "";
        }

        public bool IsOpen { get { return shown && !closing; } }
        internal bool IsPatternPhase { get { return continuePattern != null; } }

        public void Show()
        {
            if (IsOpen) return;
            if (released || dismissed) throw new InvalidOperationException("Bảng MAKE HOLE đã đóng.");
            if (!TryResolveContext(out ownerPart, out editComponent))
                throw new InvalidOperationException("Mở Part hoặc Edit Component một Part trong Assembly trước khi dùng MAKE HOLE.");
            configuration = ownerPart.ConfigurationManager.ActiveConfiguration.Name;
            CaptureInitialSelection();
            int error = 0;
            int flags = (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_LockedPage;
            page = app.CreatePropertyManagerPage(IsPatternPhase ? "MAKE HOLE - PATTERN" : "MAKE HOLE", flags, this, ref error) as PropertyManagerPage2;
            if (page == null || error != (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
                throw new InvalidOperationException("Không tạo được PropertyManager MAKE HOLE (" + error + ").");

            initializing = true;
            try
            {
                BuildControls();
                int result = page.Show2(0);
                if (result != (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
                    throw new InvalidOperationException("Không mở được MAKE HOLE. Hãy kết thúc lệnh SolidWorks đang mở (" + result + ").");
                if (released || dismissed) return;
                shown = true;
                if (!IsPatternPhase)
                {
                    model.ClearSelection2(true);
                    int initialPatterns = 0;
                    foreach (Pick pick in initialSelection)
                        if (IsCurvePattern(pick.Item)) initialPatterns++;
                    foreach (Pick pick in initialSelection)
                    {
                        int mark = MarkForType(pick.Type, pick.Item);
                        if (mark == EdgeMark && !(pick.Item is Edge) && initialPatterns > 1) continue;
                        // Each role accepts one object, matching the original command.
                        if (mark != 0 && Count(mark) == 0) Select(pick, mark, true);
                    }
                }
            }
            catch
            {
                Dismiss();
                throw;
            }
            finally { initializing = false; }
            RequestSelectionValidation();
            if (IsOpen && edgeBox != null) edgeBox.SetSelectionFocus();
        }

        private void BuildControls()
        {
            if (IsPatternPhase) { BuildPatternControls(); return; }
            // The edge is the only pick most runs need: it has the first box
            // and the focus, and the group title is its caption.
            var selections = Group(SelectionGroupId, "1. Cạnh đặt lỗ", true);
            edgeBox = Selection(selections, EdgeId, null, EdgeMark,
                "Chọn cạnh để tạo mới bằng ✓; chọn Curve Pattern trên cây để dùng Update hole.",
                swSelectType_e.swSelEDGES, swSelectType_e.swSelBODYFEATURES);

            // The four distances first, then the sketch that explains them.
            var spacing = Group(SpacingGroupId, "2. Bố trí lỗ (mm)", true);
            edgeOffsetBox = Length(spacing, EdgeOffsetId, "X – cách cạnh", draft.EdgeOffsetMm);
            leftOffsetBox = Length(spacing, LeftOffsetId, "L – cách đầu cạnh", draft.LeftOffsetMm);
            rightOffsetBox = Length(spacing, RightOffsetId, "R – cách cuối cạnh", draft.RightOffsetMm);
            pitchBox = Length(spacing, PitchId, "P – bước lỗ tối đa", draft.PitchMm);
            directionBox = Combo(spacing, DirectionId, "Hướng rải lỗ", MakeHolePageValues.Directions, false,
                "Curve Flow: theo cạnh; Line Flow: theo đường thẳng; Spline Flow: theo spline.");
            int direction = Array.IndexOf(MakeHolePageValues.Directions, draft.Direction);
            directionBox.CurrentSelection = (short)Math.Max(0, direction);
            draft.Direction = MakeHolePageValues.Directions[Math.Max(0, direction)];
            BuildDiagram(spacing);

            // Hole Wizard remains the usual place to choose the hole type/size.
            // Keep the command's existing size/label presets as an advanced option.
            var hole = Group(HoleGroupId, "Kích thước, tên lỗ (tùy chọn)", false);
            sizeBox = Combo(hole, SizeId, "Kích thước lỗ tròn / lỗ dài (mm)", sizes, true,
                "Lỗ tròn: 4.2; lỗ dài: 4.2x25. Có thể chỉnh loại và kích thước trong Hole Wizard.");
            sizeBox.EditText = draft.SizeText ?? "";
            labelBox = Combo(hole, LabelId, "Tên lỗ (có thể để trống)", labels, true, "Chọn preset hoặc nhập tên; để trống để dùng tên mặc định.");
            labelBox.EditText = draft.HoleLabel ?? "";

            var advanced = Group(HintGroupId, "Tham chiếu thêm (tùy chọn)", false);
            Selection(advanced, FaceId, "Mặt tạo lỗ", FaceMark,
                "Để trống: tự dùng mặt kề cạnh như lệnh cũ.", swSelectType_e.swSelFACES);
            Selection(advanced, SidePointId, "Điểm phía offset", PointMark,
                "Chọn đỉnh hoặc điểm nếu cần chỉ phía đặt hàng lỗ.",
                swSelectType_e.swSelVERTICES, swSelectType_e.swSelSKETCHPOINTS, swSelectType_e.swSelDATUMPOINTS);
            Selection(advanced, SeedId, "Feature lỗ mẫu", SeedMark,
                "Chọn lỗ mẫu trên cây để dùng lại. Để trống: tiếp tục Hole Wizard.", swSelectType_e.swSelBODYFEATURES);
            page.SetMessage2("Chọn cạnh đặt lỗ.", (int)swPropertyManagerPageMessageVisibility.swMessageBoxVisible, "MAKE HOLE");
        }

        private void BuildPatternControls()
        {
            var actions = Group(PatternGroupId, "Rải lỗ theo cạnh", true);
            patternButton = actions.AddControl2(PatternId, (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Pattern", Align, ControlFlags, "Rải lỗ mẫu đã tạo theo tuyến và thông số MAKE HOLE đã xác nhận.") as PropertyManagerPageButton;
            if (patternButton == null) throw new InvalidOperationException("Không tạo được nút Pattern.");
            patternButton.Caption = "Pattern";
            Label(actions, PatternSummaryId, patternSummary, 60);
            page.SetMessage2("Lỗ mẫu đã xong. Bấm ✓ để rải lỗ theo cạnh.",
                (int)swPropertyManagerPageMessageVisibility.swMessageBoxVisible, "MAKE HOLE - PATTERN");
        }
        private bool ValidatePattern(out string message)
        {
            if (!ContextMatches()) { message = "Part, cấu hình hoặc Edit Component đã thay đổi. Mở lại MAKE HOLE."; return false; }
            if (patternReady == null || !patternReady()) { message = "Bước Pattern không còn hợp lệ. Kiểm tra lỗ mẫu hoặc bấm Reset."; return false; }
            message = "Lỗ mẫu đã xong. Bấm ✓ để rải lỗ theo cạnh.";
            return true;
        }

        private static short Align { get { return (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge; } }
        private static int ControlFlags { get { return (int)swAddControlOptions_e.swControlOptions_Visible | (int)swAddControlOptions_e.swControlOptions_Enabled; } }
        private void BuildDiagram(PropertyManagerPageGroup group)
        {
            diagramPath = Path.Combine(Path.GetTempPath(), "ADDIN-MakeHole-" + Guid.NewGuid().ToString("N") + ".png");
            RenderDiagram(diagramPath);
            diagram = group.AddControl2(26, (short)swPropertyManagerPageControlType_e.swControlType_Bitmap,
                "Minh họa X / L / R / P", Align, ControlFlags, "X: cách cạnh; L/R: cách hai đầu; P: bước lỗ tối đa khi chia đều.") as PropertyManagerPageBitmap;
            if (diagram == null || !diagram.SetBitmapByName(diagramPath, ""))
                throw new InvalidOperationException("Không tải được hình minh họa MAKE HOLE.");
            // Width is relative to the group (100), not a pixel dimension.
            ((PropertyManagerPageControl)diagram).Width = 100;
        }
        internal static void RenderDiagram(string path)
        {
            using (var image = new Drawing.Bitmap(272, 150))
            using (var g = Drawing.Graphics.FromImage(image))
            using (var outline = new Drawing.Pen(Drawing.Color.FromArgb(100, 116, 139), 1))
            using (var edgePen = new Drawing.Pen(Drawing.Color.FromArgb(37, 99, 235), 2.5f))
            using (var dimension = new Drawing.Pen(Drawing.Color.FromArgb(51, 65, 85), 1))
            using (var plate = new Drawing.SolidBrush(Drawing.Color.FromArgb(241, 245, 249)))
            using (var hole = new Drawing.SolidBrush(Drawing.Color.FromArgb(59, 130, 246)))
            using (var ink = new Drawing.SolidBrush(Drawing.Color.FromArgb(30, 41, 59)))
            using (var blue = new Drawing.SolidBrush(Drawing.Color.FromArgb(37, 99, 235)))
            using (var label = new Drawing.Font("Segoe UI", 14, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
            using (var text = new Drawing.Font("Segoe UI", 11, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Pixel))
            using (var arrows = new Drawing.Drawing2D.AdjustableArrowCap(3, 3, true))
            {
                g.Clear(Drawing.Color.White);
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                dimension.CustomStartCap = arrows; dimension.CustomEndCap = arrows;
                g.DrawString("Cạnh chuẩn", text, blue, 42, 17);
                g.FillRectangle(plate, 42, 38, 204, 52);
                g.DrawRectangle(outline, 42, 38, 204, 52);
                g.DrawLine(edgePen, 42, 38, 246, 38);
                g.DrawLine(edgePen, 246, 38, 240, 34);
                g.DrawLine(edgePen, 246, 38, 240, 42);
                foreach (int x in new[] { 68, 118, 168, 218 })
                {
                    g.FillEllipse(hole, x - 4, 64, 8, 8);
                    g.DrawEllipse(edgePen, x - 4, 64, 8, 8);
                }
                g.DrawLine(outline, 22, 38, 40, 38); g.DrawLine(outline, 22, 68, 62, 68);
                g.DrawLine(dimension, 26, 38, 26, 68); g.DrawString("X", label, ink, 8, 44);
                foreach (int x in new[] { 42, 68, 118, 168, 218, 246 })
                    g.DrawLine(outline, x, x == 42 || x == 246 ? 92 : 73, x, 108);
                g.DrawLine(dimension, 42, 103, 68, 103); g.DrawString("L", label, ink, 49, 111);
                g.DrawLine(dimension, 218, 103, 246, 103); g.DrawString("R", label, ink, 226, 111);
                // The existing engine divides this span evenly using P as the maximum pitch.
                g.DrawLine(dimension, 118, 103, 168, 103); g.DrawString("≤P", label, ink, 132, 111);
                g.DrawString("P: bước tối đa; lỗ chia đều", text, ink, 42, 134);
                image.Save(path, Drawing.Imaging.ImageFormat.Png);
            }
        }
        private void DeleteDiagram()
        {
            diagram = null;
            if (diagramPath == null) return;
            try { File.Delete(diagramPath); }
            catch (IOException ex) { Debug.WriteLine("[MAKE HOLE PMP] Diagram cleanup: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Debug.WriteLine("[MAKE HOLE PMP] Diagram cleanup: " + ex.Message); }
            diagramPath = null;
        }
        private PropertyManagerPageGroup Group(int id, string title, bool expanded)
        {
            int flags = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible;
            if (expanded) flags |= (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;
            var group = page.AddGroupBox(id, title, flags) as PropertyManagerPageGroup;
            if (group == null) throw new InvalidOperationException("Không tạo được nhóm " + title + ".");
            return group;
        }
        private PropertyManagerPageLabel Label(PropertyManagerPageGroup group, int id, string title, short height = 14)
        {
            var label = group.AddControl2(id, (short)swPropertyManagerPageControlType_e.swControlType_Label,
                title, Align, ControlFlags, title) as PropertyManagerPageLabel;
            if (label == null) throw new InvalidOperationException("Không tạo được nhãn " + title + ".");
            label.Caption = title;
            label.Height = height;
            return label;
        }
        private PropertyManagerPageSelectionbox Selection(PropertyManagerPageGroup group, int id,
            string title, int mark, string tip, params swSelectType_e[] filters)
        {
            // A null title is a box that needs no caption of its own.
            if (title != null) Label(group, 1000 + id, title);
            var box = group.AddControl2(id, (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                title ?? "", Align, ControlFlags, tip) as PropertyManagerPageSelectionbox;
            if (box == null) throw new InvalidOperationException("Không tạo được ô chọn " + title + ".");
            var values = new int[filters.Length];
            for (int i = 0; i < filters.Length; i++) values[i] = (int)filters[i];
            box.SetSelectionFilters(values);
            box.Mark = mark;
            box.SingleEntityOnly = true;
            box.AllowMultipleSelectOfSameEntity = false;
            box.Height = 26;
            selectionBoxes[mark] = box;
            return box;
        }
        private PropertyManagerPageCombobox Combo(PropertyManagerPageGroup group, int id,
            string title, string[] items, bool editable, string tip)
        {
            Label(group, 1000 + id, title);
            var box = group.AddControl2(id, (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                title, Align, ControlFlags, tip) as PropertyManagerPageCombobox;
            if (box == null) throw new InvalidOperationException("Không tạo được trường " + title + ".");
            box.Style = (int)(editable ? swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditableText
                : swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly);
            if (items.Length > 0) box.AddItems(items);
            return box;
        }
        private PropertyManagerPageNumberbox Length(PropertyManagerPageGroup group, int id, string title, double millimeters)
        {
            Label(group, 1000 + id, title);
            var box = group.AddControl2(id, (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                title, Align, ControlFlags, "Nhập chiều dài lớn hơn 0; đơn vị hiển thị mm.") as PropertyManagerPageNumberbox;
            if (box == null) throw new InvalidOperationException("Không tạo được trường " + title + ".");
            // The native API always stores length in meters, including inch documents.
            box.SetRange2((int)swNumberboxUnitType_e.swNumberBox_Length, 0.000000001, 1000, true, 0.001, 0.01, 0.0001);
            box.DisplayedUnit = (int)swLengthUnit_e.swMM;
            box.Value = millimeters / 1000.0;
            return box;
        }

        private bool TryResolveContext(out ModelDoc2 part, out Component2 component)
        {
            part = null; component = null;
            if (model == null || !Same(app.ActiveDoc, model)) return false;
            if (model.GetType() == (int)swDocumentTypes_e.swDocPART) { part = model; return true; }
            var assembly = model as AssemblyDoc;
            if (assembly == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY) return false;
            component = assembly.GetEditTargetComponent();
            part = assembly.IGetEditTarget2();
            return component != null && part != null && part.GetType() == (int)swDocumentTypes_e.swDocPART;
        }
        private bool Same(object left, object right)
        {
            if (left == null || right == null) return false;
            if (ReferenceEquals(left, right)) return true;
            return app.IsSame(left, right) == (int)swObjectEquality.swObjectSame;
        }
        private bool ContextMatches()
        {
            ModelDoc2 part; Component2 component;
            return TryResolveContext(out part, out component) && Same(part, ownerPart) &&
                (editComponent == null ? component == null : Same(component, editComponent)) &&
                string.Equals(configuration, part.ConfigurationManager.ActiveConfiguration.Name, StringComparison.Ordinal);
        }
        private SelectionMgr Manager { get { return model.SelectionManager as SelectionMgr; } }
        private int Count(int mark) { return Manager == null ? 0 : Manager.GetSelectedObjectCount2(mark); }
        private Pick ReadPick(int index, int mark)
        {
            var manager = Manager;
            object item = manager.GetSelectedObject6(index, mark);
            int type = manager.GetSelectedObjectType3(index, mark);
            double[] point = null;
            // Tree features may have no graphics pick point; that is valid for
            // the optional seed. Coordinates must not block the whole command.
            try { point = manager.GetSelectionPoint2(index, mark) as double[]; }
            catch (COMException) { }
            if (point == null || point.Length < 3)
            {
                try
                {
                    var vertex = item as Vertex;
                    var sketchPoint = item as SketchPoint;
                    if (vertex != null) point = vertex.GetPoint() as double[];
                    else if (sketchPoint != null) point = new[] { sketchPoint.X, sketchPoint.Y, sketchPoint.Z };
                }
                catch (COMException) { }
            }
            return new Pick { Item = item, Type = type, Mark = manager.GetSelectedObjectMark(index),
                Point = point != null && point.Length >= 3 ? (double[])point.Clone() : null };
        }
        private static int MarkForType(int type, object item)
        {
            if (type == (int)swSelectType_e.swSelEDGES && item is Edge) return EdgeMark;
            if (type == (int)swSelectType_e.swSelFACES && item is Face2) return FaceMark;
            if (type == (int)swSelectType_e.swSelVERTICES || type == (int)swSelectType_e.swSelSKETCHPOINTS ||
                type == (int)swSelectType_e.swSelDATUMPOINTS) return PointMark;
            return type == (int)swSelectType_e.swSelBODYFEATURES && item is Feature
                ? (IsCurvePattern(item) ? EdgeMark : SeedMark) : 0;
        }
        private static bool IsCurvePattern(object item)
        {
            var feature = item as Feature;
            if (feature == null) return false;
            try
            {
                string type = feature.GetTypeName2() ?? "";
                return type.IndexOf("CurvePattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    type.IndexOf("CurveDrivenPattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (type.IndexOf("Curve", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     type.IndexOf("Pattern", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (COMException) { return false; }
            catch (InvalidComObjectException) { return false; }
        }
        private void CaptureInitialSelection()
        {
            if (Manager == null) return;
            for (int i = 1; i <= Count(-1); i++) initialSelection.Add(ReadPick(i, -1));
        }
        private bool Select(Pick pick, int mark, bool append)
        {
            var data = Manager.CreateSelectData();
            data.Mark = mark;
            if (pick.Point != null)
            {
                data.X = pick.Point[0]; data.Y = pick.Point[1]; data.Z = pick.Point[2];
            }
            if (model.Extension.MultiSelect2(new[] { new DispatchWrapper(pick.Item) }, append, data) <= 0) return false;
            for (int i = 1; i <= Count(mark); i++)
                if (Same(Manager.GetSelectedObject6(i, mark), pick.Item)) return true;
            return false;
        }
        private int CachedCount(int mark)
        {
            List<Pick> picks;
            return currentSelection.TryGetValue(mark, out picks) ? picks.Count : 0;
        }
        private void RequestSelectionValidation(bool keepEdgeFocus = false)
        {
            if (IsPatternPhase) { UpdateReadyState(); return; }
            if (!IsOpen || initializing) return;
            int request = ++selectionRequest;
            selectionPending = true;
            currentSelection.Clear(); selectionError = null;
            UpdateReadyState();
            Action refresh = () =>
            {
                if (!IsOpen || dismissed || request != selectionRequest) return;
                selectionPending = false;
                try
                {
                    // Native box indices identify their actual objects even when
                    // SOLIDWORKS has left a visible pick with a different mark.
                    foreach (var entry in selectionBoxes)
                    {
                        var picks = new List<Pick>();
                        for (int i = 0; i < entry.Value.ItemCount; i++)
                        {
                            int index = entry.Value.get_SelectionIndex(i);
                            if (index < 1) throw new InvalidOperationException("Lựa chọn đang cập nhật. Hãy chọn lại cạnh.");
                            var pick = ReadPick(index, -1);
                            pick.Mark = entry.Key;
                            if (editComponent != null) pick.Component = Manager.GetSelectedObjectsComponent4(index, -1) as Component2;
                            picks.Add(pick);
                        }
                        currentSelection[entry.Key] = picks;
                    }
                    if (keepEdgeFocus && CachedCount(EdgeMark) == 1) edgeBox.SetSelectionFocus();
                }
                catch (Exception ex)
                {
                    currentSelection.Clear(); selectionError = ex.Message;
                    Debug.WriteLine("[MAKE HOLE PMP] Selection: " + ex.Message);
                }
                UpdateReadyState();
            };
            // Read picks after the native selection callback has returned.
            if (deferValidation == null) refresh(); else deferValidation(refresh);
        }
        private bool ValidateSelections(out string message)
        {
            if (!ContextMatches()) { message = "Part, cấu hình hoặc Edit Component đã thay đổi. Mở lại MAKE HOLE."; return false; }
            if (selectionPending) { message = "Đang cập nhật cạnh đã chọn..."; return false; }
            if (!string.IsNullOrEmpty(selectionError)) { message = selectionError; return false; }
            if (CachedCount(EdgeMark) != 1 || !(currentSelection[EdgeMark][0].Item is Edge))
            { message = "Chọn cạnh đặt lỗ. (Update hole: chọn Curve Pattern trên cây.)"; return false; }
            foreach (int mark in new[] { EdgeMark, FaceMark, PointMark, SeedMark })
            {
                if (CachedCount(mark) > 1) { message = "Mỗi ô chỉ nhận một đối tượng."; return false; }
                if (CachedCount(mark) == 0) continue;
                Pick pick = currentSelection[mark][0];
                if (MarkForType(pick.Type, pick.Item) != mark)
                { message = "Đối tượng chưa đúng loại của ô chọn."; return false; }
                if (editComponent != null && !Same(pick.Component, editComponent))
                { message = "Chọn đối tượng thuộc Part đang Edit Component."; return false; }
            }
            message = ""; return true;
        }
        private bool ValidateUpdate(out string message)
        {
            if (IsPatternPhase) { message = "Hãy hoàn tất bước Pattern trước khi dùng UPDATE HOLE."; return false; }
            if (!ContextMatches()) { message = "Part, cấu hình hoặc Edit Component đã thay đổi. Mở lại MAKE HOLE."; return false; }
            if (selectionPending) { message = "Đang cập nhật đối tượng đã chọn..."; return false; }
            if (!string.IsNullOrEmpty(selectionError)) { message = selectionError; return false; }
            if (updatePattern == null) { message = "UPDATE HOLE chưa được kết nối."; return false; }
            if (!MakeHolePageValues.Positive(draft.PitchMm)) { message = "Bước P phải lớn hơn 0 để cập nhật lỗ."; return false; }
            if (CachedCount(EdgeMark) != 1 || !IsCurvePattern(currentSelection[EdgeMark][0].Item))
            { message = "Chọn một Curve Pattern trên cây để dùng UPDATE HOLE."; return false; }
            if (editComponent != null && !Same(currentSelection[EdgeMark][0].Component, editComponent))
            { message = "Chọn Curve Pattern thuộc Part đang Edit Component."; return false; }
            message = "Chỉnh P rồi bấm Update hole bên Task Pane.";
            return true;
        }
        private void UpdateReadyState()
        {
            if (!IsOpen || initializing || page == null) return;
            if (updateRequested || resetRequested || patternRequested) { updateAvailability?.Invoke(false); return; }
            try
            {
                if (IsPatternPhase)
                {
                    string patternMessage;
                    bool readyForPattern = ValidatePattern(out patternMessage);
                    ((PropertyManagerPageControl)patternButton).Enabled = readyForPattern;
                    updateAvailability?.Invoke(false);
                    page.SetMessage2(patternMessage, (int)swPropertyManagerPageMessageVisibility.swMessageBoxVisible, "MAKE HOLE - PATTERN");
                    page.EnableButton((int)swPropertyManagerPageButtons_e.swPropertyManagerPageButton_Ok, readyForPattern);
                    return;
                }
                string message; MakeHoleOptions options;
                bool ready = draft.TryGetOptions(out options, out message);
                if (ready) ready = ValidateSelections(out message);
                if (ready) message = "Sẵn sàng. ✓ → xác nhận Hole Wizard → tự rải lỗ.";
                string updateMessage;
                bool canUpdate = ValidateUpdate(out updateMessage);
                updateAvailability?.Invoke(canUpdate);
                if (canUpdate || (CachedCount(EdgeMark) == 1 && currentSelection[EdgeMark][0].Item is Feature))
                    message = updateMessage;
                if (!IsOpen) return;
                page.SetMessage2(message, (int)swPropertyManagerPageMessageVisibility.swMessageBoxVisible, "MAKE HOLE");
                if (IsOpen) page.EnableButton((int)swPropertyManagerPageButtons_e.swPropertyManagerPageButton_Ok, ready);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MAKE HOLE PMP] Validation: " + ex.Message);
                if (IsOpen)
                {
                    page.EnableButton((int)swPropertyManagerPageButtons_e.swPropertyManagerPageButton_Ok, false);
                    updateAvailability?.Invoke(false);
                    if (patternButton != null) ((PropertyManagerPageControl)patternButton).Enabled = false;
                }
            }
        }
        public void OnClose(int reason)
        {
            if (closing) return;
            closing = true;
            if (dismissed || reason != (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay) return;
            try
            {
                // Controls may already be destroyed here. Use only the callback
                // values and the selection snapshot captured while interactive.
                if (IsPatternPhase)
                {
                    acceptedPattern = ValidatePattern(out closeError);
                    return;
                }
                if (updateRequested)
                {
                    if (!ValidateUpdate(out closeError)) return;
                    acceptedValues = draft.Copy();
                    acceptedSelection = new List<Pick> { currentSelection[EdgeMark][0] };
                    acceptedUpdate = true;
                    return;
                }
                if (!draft.TryGetOptions(out acceptedOptions, out closeError) || !ValidateSelections(out closeError))
                { acceptedOptions = null; return; }
                acceptedValues = draft.Copy();
                acceptedSelection = new List<Pick>();
                foreach (int mark in new[] { EdgeMark, FaceMark, PointMark, SeedMark })
                    if (CachedCount(mark) == 1) acceptedSelection.Add(currentSelection[mark][0]);
            }
            catch (Exception ex) { acceptedOptions = null; acceptedUpdate = false; acceptedPattern = false; closeError = ex.Message; }
        }
        public void AfterClose()
        {
            if (released) return;
            released = true; shown = false; closing = true;
            ++selectionRequest; selectionPending = false;
            selectionBoxes.Clear(); currentSelection.Clear();
            DeleteDiagram();
            page = null; edgeBox = null; sizeBox = null; labelBox = null; directionBox = null;
            edgeOffsetBox = null; leftOffsetBox = null; rightOffsetBox = null; pitchBox = null; patternButton = null;
            closed?.Invoke(this);
            if (dismissed)
            {
                initialSelection.Clear(); acceptedSelection = null; acceptedValues = null;
                return;
            }
            try
            {
                if (!ContextMatches())
                {
                    if (acceptedOptions != null || acceptedUpdate || acceptedPattern || resetRequested) Notify("Part, cấu hình hoặc Edit Component đã thay đổi. Mở lại MAKE HOLE.");
                    return;
                }
                if (acceptedPattern) { continuePattern(); return; }
                model.ClearSelection2(true);
                if (acceptedOptions == null && !acceptedUpdate)
                {
                    foreach (Pick pick in initialSelection) Select(pick, pick.Mark, true);
                    if (!string.IsNullOrEmpty(closeError)) Notify(closeError);
                    if (resetRequested) resetPending?.Invoke();
                    return;
                }
                foreach (Pick pick in acceptedSelection)
                    if (!Select(pick, 0, true))
                    { Notify("Không khôi phục được đối tượng đã chọn. Chưa chạy MAKE HOLE; hãy chọn lại."); return; }
                // Both actions mutate geometry only after native teardown and pick restoration.
                if (acceptedUpdate) updatePattern?.Invoke(acceptedValues.PitchMm);
                else execute?.Invoke(acceptedValues, acceptedOptions);
            }
            catch (Exception ex) { Notify("Không chạy được MAKE HOLE: " + ex.Message); }
            finally { initialSelection.Clear(); acceptedSelection = null; acceptedValues = null; acceptedOptions = null; acceptedUpdate = false; acceptedPattern = false; resetRequested = false; }
        }
        public void Dismiss()
        {
            dismissed = true;
            acceptedOptions = null; acceptedUpdate = false; acceptedPattern = false; patternRequested = false; updateRequested = false; resetRequested = false;
            if (page != null && shown && !released)
            {
                try { page.Close(false); }
                catch { if (!released) AfterClose(); throw; }
            }
            else if (!released) AfterClose();
        }
        private void Notify(string message)
        {
            try { app.SendMsgToUser2(message, (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk); }
            catch (COMException) { }
            catch (InvalidComObjectException) { }
        }
        public bool OnHelp()
        {
            if (IsPatternPhase)
            {
                Notify("Bấm Pattern hoặc ✓ để rải lỗ mẫu vừa tạo. Reset bên task pane hủy bước đang chờ. ✕ đóng bảng và giữ bước Pattern để tiếp tục sau.");
                return true;
            }
            Notify("Chọn một cạnh chuẩn. Mặt, điểm phía offset và feature lỗ mẫu là tùy chọn.\n\n" +
                "Nhập lỗ tròn (4.2) hoặc lỗ dài (4.2x25), chọn hướng và khoảng cách theo mm.\n\n" +
                "✓ đóng bảng rồi chạy MAKE HOLE. Nếu Hole Wizard mở, xác nhận lỗ mẫu (✓) là lỗ được rải theo cạnh ngay, không cần bấm thêm.\n" +
                "UPDATE HOLE bên task pane: chọn Curve Pattern, chỉnh P để cập nhật lỗ đã tạo.\n" +
                "Reset đưa thông số về lúc mở bảng và giữ đối tượng đã chọn. ✕ bỏ thông số đang nhập.");
            return true;
        }
        public bool OnSubmitSelection(int id, object item, int type, ref string text)
        {
            if (closing || dismissed || IsPatternPhase) return false;
            int expected = id == EdgeId ? EdgeMark : id == FaceId ? FaceMark : id == SidePointId ? PointMark : id == SeedId ? SeedMark : 0;
            // The native callback checks only interface/type; feature metadata is read in deferred validation.
            if (type == (int)swSelectType_e.swSelBODYFEATURES && item is Feature &&
                (expected == EdgeMark || expected == SeedMark)) return true;
            return expected != 0 && item != null && MarkForType(type, item) == expected;
        }
        public void OnNumberboxChanged(int id, double value)
        {
            if (closing) return;
            if (id == EdgeOffsetId) draft.EdgeOffsetMm = value * 1000;
            else if (id == LeftOffsetId) draft.LeftOffsetMm = value * 1000;
            else if (id == RightOffsetId) draft.RightOffsetMm = value * 1000;
            else if (id == PitchId) draft.PitchMm = value * 1000;
            UpdateReadyState();
        }
        public void OnComboboxEditChanged(int id, string value)
        {
            if (closing) return;
            if (id == SizeId) draft.SizeText = value;
            else if (id == LabelId) draft.HoleLabel = value;
            UpdateReadyState();
        }
        public void OnComboboxSelectionChanged(int id, int index)
        {
            if (closing) return;
            if (id == DirectionId) draft.Direction = index >= 0 && index < MakeHolePageValues.Directions.Length ? MakeHolePageValues.Directions[index] : "";
            else if (id == SizeId && index >= 0 && index < sizes.Length) draft.SizeText = sizes[index];
            else if (id == LabelId && index >= 0 && index < labels.Length) draft.HoleLabel = labels[index];
            UpdateReadyState();
        }
        public void OnSelectionboxListChanged(int id, int count)
        { RequestSelectionValidation(id == EdgeId && count > 0); }
        public void AfterActivation()
        {
            if (IsPatternPhase) { UpdateReadyState(); return; }
            if (!IsOpen || initializing || edgeBox == null) return;
            if (edgeBox.ItemCount == 0) edgeBox.SetSelectionFocus();
            RequestSelectionValidation();
        }
        public bool OnPreviousPage() { return false; }
        public bool OnNextPage() { return false; }
        public bool OnPreview() { return false; }
        public void OnWhatsNew() { }
        public void OnUndo() { }
        public void OnRedo() { }
        public bool OnTabClicked(int id) { return true; }
        public void OnGroupExpand(int id, bool expanded) { }
        public void OnGroupCheck(int id, bool isChecked) { }
        public void OnCheckboxCheck(int id, bool isChecked) { }
        public void OnOptionCheck(int id) { }
        internal void RequestUpdate()
        {
            if (!IsOpen || initializing || closing || dismissed || updateRequested || resetRequested || patternRequested || page == null) return;
            try
            {
                string message;
                if (!ValidateUpdate(out message)) { Notify(message); return; }
                updateRequested = true;
                updateAvailability?.Invoke(false);
                // The task-pane button closes the native page. Its engine runs in AfterClose.
                page.Close(true);
            }
            catch (Exception ex)
            {
                updateRequested = false;
                UpdateReadyState();
                Notify("Không cập nhật được MAKE HOLE: " + ex.Message);
            }
        }
        internal void RequestReset()
        {
            if (!IsOpen || initializing || closing || dismissed || updateRequested || resetRequested || patternRequested || page == null || resetPending == null) return;
            try
            {
                if (!ContextMatches()) { Notify("Part, cấu hình hoặc Edit Component đã thay đổi. Mở lại MAKE HOLE."); return; }
                resetRequested = true;
                updateAvailability?.Invoke(false);
                if (IsPatternPhase)
                {
                    // Pending Pattern is cancelled after this page releases its native controls.
                    page.Close(false);
                    return;
                }
                // Reset only the draft and managed continuation data. Keep this page
                // and its selected edge/references available for the next edit.
                initializing = true;
                try
                {
                    draft = openingValues.Copy();
                    sizeBox.EditText = draft.SizeText ?? "";
                    labelBox.EditText = draft.HoleLabel ?? "";
                    int direction = Array.IndexOf(MakeHolePageValues.Directions, draft.Direction);
                    directionBox.CurrentSelection = (short)Math.Max(0, direction);
                    edgeOffsetBox.Value = draft.EdgeOffsetMm / 1000;
                    leftOffsetBox.Value = draft.LeftOffsetMm / 1000;
                    rightOffsetBox.Value = draft.RightOffsetMm / 1000;
                    pitchBox.Value = draft.PitchMm / 1000;
                    resetPending();
                }
                finally { initializing = false; resetRequested = false; }
                UpdateReadyState();
            }
            catch (Exception ex)
            {
                resetRequested = false;
                UpdateReadyState();
                Notify("Không Reset được MAKE HOLE: " + ex.Message);
            }
        }
        internal void RequestPattern()
        {
            if (!IsPatternPhase || !IsOpen || initializing || closing || dismissed || patternRequested || resetRequested || page == null) return;
            try
            {
                string message;
                if (!ValidatePattern(out message)) { Notify(message); return; }
                patternRequested = true;
                page.Close(true);
            }
            catch (Exception ex) { patternRequested = false; UpdateReadyState(); Notify("Không chạy được Pattern: " + ex.Message); }
        }
        public void OnButtonPress(int id) { if (id == PatternId) RequestPattern(); }
        public void OnTextboxChanged(int id, string value) { }
        public void OnListboxSelectionChanged(int id, int item) { }
        public void OnSelectionboxFocusChanged(int id) { }
        public void OnSelectionboxCalloutCreated(int id) { }
        public void OnSelectionboxCalloutDestroyed(int id) { }
        public int OnActiveXControlCreated(int id, bool status) { return 0; }
        public void OnSliderPositionChanged(int id, double value) { }
        public void OnSliderTrackingCompleted(int id, double value) { }
        public bool OnKeystroke(int wparam, int message, int lparam, int id) { return false; }
        public void OnPopupMenuItem(int id) { }
        public void OnPopupMenuItemUpdate(int id, ref int value) { }
        public void OnGainedFocus(int id) { }
        public void OnLostFocus(int id) { }
        public int OnWindowFromHandleControlCreated(int id, bool status) { return 0; }
        public void OnListboxRMBUp(int id, int x, int y) { }
        public void OnNumberBoxTrackingCompleted(int id, double value) { }
    }
}
