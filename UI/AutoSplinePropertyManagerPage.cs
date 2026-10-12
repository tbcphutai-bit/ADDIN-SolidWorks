using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace ADDIN.UI
{
    // Native SOLIDWORKS PropertyManager for AUTO SPLINE creation and movement.
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class AutoSplinePropertyManagerPage : PropertyManagerPage2Handler9
    {
        private const int ModeGroupId = 1;
        private const int CreateOptionId = 2;
        private const int MoveOptionId = 3;
        private const int TargetGroupId = 4;
        private const int EdgeBoxId = 5;
        private const int SplineGroupId = 6;
        private const int MoveHintId = 7;
        private const int SplineBoxId = 8;
        private const int Create2DOptionId = 9;
        private const int Create2DHintId = 10;
        private const int CreateTwoEdgesOptionId = 11;
        private const int SecondEdgeGroupId = 12;
        private const int SecondEdgeBoxId = 13;
        private const int TwoEdgesHintId = 14;
        private const int EdgeMark = 1;
        private const int SplineMark = 2;
        private const int SecondEdgeMark = 4;

        private readonly ISldWorks app;
        private readonly IWin32Window owner;
        private readonly Action<AutoSplinePropertyManagerPage> closed;
        private readonly ModelDoc2 model;
        private PropertyManagerPage2 page;
        private PropertyManagerPageOption createOption;
        private PropertyManagerPageOption moveOption;
        private PropertyManagerPageOption create2DOption;
        private PropertyManagerPageOption createTwoEdgesOption;
        private PropertyManagerPageSelectionbox edgeBox;
        private PropertyManagerPageSelectionbox secondEdgeBox;
        private PropertyManagerPageSelectionbox splineBox;
        private PropertyManagerPageControl moveHint;
        private PropertyManagerPageControl create2DHint;
        private PropertyManagerPageControl twoEdgesHint;
        private PropertyManagerPageControl splineBoxControl;
        private PropertyManagerPageGroup splineGroup;
        private PropertyManagerPageGroup targetGroup;
        private PropertyManagerPageGroup secondEdgeGroup;
        private Timer modeRefreshTimer;
        private bool? displayedMoveMode;
        private bool? displayedCreate2DMode;
        private bool? displayedCreateTwoEdgesMode;
        private bool moveMode;
        private bool create2DMode;
        private bool createTwoEdgesMode;
        private bool shown;
        private bool closing;
        private bool refreshingModeDisplay;
        private bool executeAfterClose;
        private Edge selectedEdge;
        private Edge selectedSecondEdge;
        private SketchSpline selectedSpline;

        public AutoSplinePropertyManagerPage(
            ISldWorks app,
            IWin32Window owner,
            Action<AutoSplinePropertyManagerPage> closed)
        {
            this.app = app;
            this.owner = owner;
            this.closed = closed;
            model = app == null ? null : app.ActiveDoc as ModelDoc2;
        }

        public bool IsOpen { get { return shown; } }

        public void Dismiss()
        {
            if (!shown) return;
            // Close can pump the UI message loop before OnClose is delivered.
            // Invalidate pending refreshes before entering native teardown.
            closing = true;
            shown = false;
            CancelModeRefresh();
            if (page != null)
                page.Close(false);
        }

        public void Show()
        {
            if (shown) return;
            closing = false;
            executeAfterClose = false;
            displayedMoveMode = null;
            displayedCreate2DMode = null;
            displayedCreateTwoEdgesMode = null;
            CancelModeRefresh();
            if (model == null ||
                model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                Notify("Hãy mở một file Part trước khi dùng AUTO SPLINE.",
                    swMessageBoxIcon_e.swMbWarning);
                return;
            }

            CaptureInitialSelection();
            int error = 0;
            int options =
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton;
            page = app.CreatePropertyManagerPage("AUTO SPLINE", options, this,
                ref error) as PropertyManagerPage2;
            if (page == null ||
                error != (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
                throw new InvalidOperationException(
                    "Không tạo được PropertyManager AUTO SPLINE (" + error + ").");

            int groupOptions =
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible |
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;
            int controlOptions =
                (int)swAddControlOptions_e.swControlOptions_Visible |
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            short align = (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;

            var modeGroup = page.AddGroupBox(ModeGroupId, "Chế độ", groupOptions)
                as PropertyManagerPageGroup;
            createOption = modeGroup.AddControl2(CreateOptionId,
                (short)swPropertyManagerPageControlType_e.swControlType_Option,
                "Tạo spline 3D từ cạnh", align, controlOptions,
                "Chọn một Edge để tạo 3D spline mới.") as PropertyManagerPageOption;
            createTwoEdgesOption = modeGroup.AddControl2(CreateTwoEdgesOptionId,
                (short)swPropertyManagerPageControlType_e.swControlType_Option,
                "Gộp 2 cạnh thành spline 3D", align, controlOptions,
                "Chọn hai cạnh nối tiếp để tạo một spline 3D liên tục.")
                as PropertyManagerPageOption;
            create2DOption = modeGroup.AddControl2(Create2DOptionId,
                (short)swPropertyManagerPageControlType_e.swControlType_Option,
                "Tạo spline 2D từ cạnh", align, controlOptions,
                "Edit 2D Sketch rồi chọn cạnh Surface/Body nằm trên mặt phẳng Sketch.")
                as PropertyManagerPageOption;
            moveOption = modeGroup.AddControl2(MoveOptionId,
                (short)swPropertyManagerPageControlType_e.swControlType_Option,
                "Di chuyển spline hiện có", align, controlOptions,
                "Chọn spline trong 3D Sketch và cạnh spline của Surface/Body.")
                as PropertyManagerPageOption;

            splineGroup = page.AddGroupBox(SplineGroupId,
                "1. Spline nguồn trong 3D Sketch", groupOptions)
                as PropertyManagerPageGroup;
            moveHint = splineGroup.AddControl2(MoveHintId,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Edit 3D Sketch chứa spline trước khi chọn.",
                align, controlOptions,
                "Giữ nguyên 3D Sketch và spline hiện có.")
                as PropertyManagerPageControl;
            splineBox = splineGroup.AddControl2(SplineBoxId,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Chọn spline đang Edit", align, controlOptions,
                "Chọn đường spline hiện có trong 3D Sketch đang Edit.")
                as PropertyManagerPageSelectionbox;
            splineBox.Mark = SplineMark;
            splineBox.SingleEntityOnly = true;
            splineBox.Height = 36;
            splineBox.SetSelectionFilters(new int[] { (int)swSelectType_e.swSelSKETCHSEGS });
            splineBoxControl = splineBox as PropertyManagerPageControl;

            targetGroup = page.AddGroupBox(TargetGroupId,
                "Cạnh tạo spline mới", groupOptions) as PropertyManagerPageGroup;
            create2DHint = targetGroup.AddControl2(Create2DHintId,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Edit 2D Sketch trước. Chọn cạnh nằm trên mặt phẳng Sketch.",
                align, controlOptions,
                "Chia điểm đều theo chiều dài cạnh, dùng ít fit point nhất đã kiểm tra, sai số ≤ 0,20 mm.")
                as PropertyManagerPageControl;
            twoEdgesHint = targetGroup.AddControl2(TwoEdgesHintId,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Chọn 2 cạnh nối tiếp nhau. Thoát 2D Sketch trước khi dùng.",
                align, controlOptions,
                "Một spline liên tục; điểm chia đều trên tổng chiều dài hai cạnh, sai số ≤ 0,20 mm.")
                as PropertyManagerPageControl;
            edgeBox = targetGroup.AddControl2(EdgeBoxId,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Chọn cạnh spline Surface/Body", align, controlOptions,
                "TẠO: cạnh cong làm mẫu. MOVE: cạnh spline Surface/Body làm đích.")
                as PropertyManagerPageSelectionbox;
            edgeBox.Mark = EdgeMark;
            edgeBox.SingleEntityOnly = true;
            edgeBox.Height = 36;
            edgeBox.SetSelectionFilters(new int[] { (int)swSelectType_e.swSelEDGES });

            secondEdgeGroup = page.AddGroupBox(SecondEdgeGroupId,
                "2. Cạnh thứ hai nối tiếp", groupOptions) as PropertyManagerPageGroup;
            secondEdgeBox = secondEdgeGroup.AddControl2(SecondEdgeBoxId,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Chọn cạnh thứ hai", align, controlOptions,
                "Chọn cạnh Surface/Body nối với một endpoint của cạnh thứ nhất.")
                as PropertyManagerPageSelectionbox;
            secondEdgeBox.Mark = SecondEdgeMark;
            secondEdgeBox.SingleEntityOnly = true;
            secondEdgeBox.Height = 36;
            secondEdgeBox.SetSelectionFilters(new int[] { (int)swSelectType_e.swSelEDGES });

            if (createOption == null || create2DOption == null || create2DHint == null ||
                createTwoEdgesOption == null || twoEdgesHint == null ||
                secondEdgeGroup == null || secondEdgeBox == null ||
                moveOption == null || edgeBox == null ||
                splineGroup == null || splineBox == null ||
                splineBoxControl == null || moveHint == null ||
                targetGroup == null)
                throw new InvalidOperationException("Không tạo đủ mục chọn AUTO SPLINE.");

            bool initialMoveMode = moveMode;
            bool initialCreate2DMode = create2DMode;
            bool initialCreateTwoEdgesMode = createTwoEdgesMode;
            createOption.Checked = !initialMoveMode && !initialCreate2DMode && !initialCreateTwoEdgesMode;
            createTwoEdgesOption.Checked = initialCreateTwoEdgesMode;
            create2DOption.Checked = initialCreate2DMode;
            moveOption.Checked = initialMoveMode;
            moveMode = initialMoveMode;
            create2DMode = initialCreate2DMode;
            createTwoEdgesMode = initialCreateTwoEdgesMode;
            splineGroup.Visible = moveMode;
            splineGroup.Expanded = moveMode;
            moveHint.Visible = moveMode;
            splineBoxControl.Visible = moveMode;
            create2DHint.Visible = create2DMode;
            twoEdgesHint.Visible = createTwoEdgesMode;
            secondEdgeGroup.Visible = createTwoEdgesMode;
            secondEdgeGroup.Expanded = createTwoEdgesMode;
            targetGroup.Caption = moveMode
                ? "2. Spline đích: cạnh Surface/Body"
                : create2DMode ? "Cạnh tạo spline trong 2D Sketch"
                : createTwoEdgesMode ? "1. Cạnh thứ nhất" : "Cạnh tạo spline 3D";
            page.Show2(0);
            if (closing || page == null) return;
            shown = true;
            RestoreInitialSelection();
            RefreshModeDisplay();
        }

        private void ScheduleModeRefresh()
        {
            if (!shown || closing ||
                (displayedMoveMode == moveMode && displayedCreate2DMode == create2DMode &&
                 displayedCreateTwoEdgesMode == createTwoEdgesMode)) return;
            // Defer only the requested layout change until the radio-button
            // callback returns. Never poll Checked on a native COM control.
            if (modeRefreshTimer == null)
            {
                modeRefreshTimer = new Timer { Interval = 1 };
                modeRefreshTimer.Tick += OnModeRefreshTick;
            }
            modeRefreshTimer.Stop();
            modeRefreshTimer.Start();
        }

        private void CancelModeRefresh()
        {
            Timer timer = modeRefreshTimer;
            modeRefreshTimer = null;
            if (timer == null) return;
            timer.Stop();
            timer.Tick -= OnModeRefreshTick;
            timer.Dispose();
        }

        private void OnModeRefreshTick(object sender, EventArgs e)
        {
            // Ignore a queued callback from a timer that has already been
            // cancelled, including callbacks from an earlier page opening.
            if (!ReferenceEquals(sender, modeRefreshTimer)) return;
            CancelModeRefresh();
            if (!shown || closing ||
                (displayedMoveMode == moveMode && displayedCreate2DMode == create2DMode &&
                 displayedCreateTwoEdgesMode == createTwoEdgesMode)) return;
            RefreshModeDisplay();
        }

        private void RefreshModeDisplay()
        {
            if (!shown || closing || refreshingModeDisplay) return;
            refreshingModeDisplay = true;
            try
            {
                bool modeForDisplay = moveMode;
                bool create2DForDisplay = create2DMode;
                bool createTwoEdgesForDisplay = createTwoEdgesMode;
                // Focus changes can re-enter SolidWorks and close the page.
                // Check the managed lifetime again after each native call.
                if (!modeForDisplay) edgeBox.SetSelectionFocus();
                if (!shown || closing) return;
                targetGroup.Caption = modeForDisplay
                    ? "2. Spline đích: cạnh Surface/Body"
                    : create2DForDisplay ? "Cạnh tạo spline trong 2D Sketch"
                    : createTwoEdgesForDisplay ? "1. Cạnh thứ nhất" : "Cạnh tạo spline 3D";
                if (!shown || closing) return;
                create2DHint.Visible = create2DForDisplay;
                if (!shown || closing) return;
                twoEdgesHint.Visible = createTwoEdgesForDisplay;
                if (!shown || closing) return;
                secondEdgeGroup.Expanded = createTwoEdgesForDisplay;
                if (!shown || closing) return;
                secondEdgeGroup.Visible = createTwoEdgesForDisplay;
                if (!shown || closing) return;
                moveHint.Visible = modeForDisplay;
                if (!shown || closing) return;
                splineBoxControl.Visible = modeForDisplay;
                if (!shown || closing) return;
                splineGroup.Expanded = modeForDisplay;
                if (!shown || closing) return;
                splineGroup.Visible = modeForDisplay;
                if (!shown || closing) return;
                if (modeForDisplay && selectedSpline == null)
                    splineBox.SetSelectionFocus();
                if (!shown || closing) return;
                displayedMoveMode = modeForDisplay;
                displayedCreate2DMode = create2DForDisplay;
                displayedCreateTwoEdgesMode = createTwoEdgesForDisplay;
                UpdateReadyState();
            }
            finally
            {
                refreshingModeDisplay = false;
            }
            ScheduleModeRefresh();
        }

        private void CaptureInitialSelection()
        {
            selectedEdge = null;
            selectedSecondEdge = null;
            selectedSpline = null;
            moveMode = false;
            createTwoEdgesMode = false;
            Sketch activeSketch = model.SketchManager.ActiveSketch as Sketch;
            create2DMode = activeSketch != null && !activeSketch.Is3D();
            SelectionMgr manager = model.SelectionManager as SelectionMgr;
            if (manager == null) return;
            int count = manager.GetSelectedObjectCount2(-1);
            if (count < 1 || count > 2) return;

            Edge edge = null;
            Edge secondEdge = null;
            SketchSpline spline = null;
            for (int i = 1; i <= count; i++)
            {
                object item = manager.GetSelectedObject6(i, -1);
                if (item is Edge)
                {
                    if (edge == null) edge = (Edge)item;
                    else secondEdge = (Edge)item;
                }
                else if (spline == null)
                    spline = EdgeToEqualSplineCommand.ResolveSelectedSketchSpline(model, item);
            }
            selectedEdge = edge;
            selectedSecondEdge = secondEdge;
            selectedSpline = count == 2 ? spline : null;
            moveMode = selectedEdge != null && selectedSpline != null;
            createTwoEdgesMode = selectedEdge != null && selectedSecondEdge != null;
            if (moveMode || createTwoEdgesMode) create2DMode = false;
        }

        private void RestoreInitialSelection()
        {
            model.ClearSelection2(true);
            if (selectedEdge != null) SelectWithMark(selectedEdge, false, EdgeMark);
            if (moveMode && selectedSpline != null)
                SelectWithMark(selectedSpline, selectedEdge != null, SplineMark);
            if (createTwoEdgesMode && selectedSecondEdge != null)
                SelectWithMark(selectedSecondEdge, selectedEdge != null, SecondEdgeMark);
            if (selectedEdge == null) edgeBox.SetSelectionFocus();
            else if (moveMode && selectedSpline == null) splineBox.SetSelectionFocus();
            else if (createTwoEdgesMode && selectedSecondEdge == null) secondEdgeBox.SetSelectionFocus();
        }

        private bool SelectWithMark(object item, bool append, int mark)
        {
            SelectionMgr manager = model.SelectionManager as SelectionMgr;
            SelectData data = mark == 0 || manager == null
                ? null : manager.CreateSelectData();
            if (data != null) data.Mark = mark;
            SketchSegment segment = item as SketchSegment;
            if (segment != null) return segment.Select4(append, data);
            Entity entity = item as Entity;
            return entity != null && entity.Select4(append, data);
        }

        private void UpdateReadyState()
        {
            if (page == null || !shown || closing) return;
            SelectionMgr manager = model.SelectionManager as SelectionMgr;
            bool edgeReady = manager != null &&
                manager.GetSelectedObjectCount2(EdgeMark) == 1 &&
                manager.GetSelectedObject6(1, EdgeMark) is Edge;
            bool splineReady = !moveMode ||
                (manager != null &&
                 manager.GetSelectedObjectCount2(SplineMark) == 1 &&
                 EdgeToEqualSplineCommand.ResolveSelectedSketchSpline(model,
                     manager.GetSelectedObject6(1, SplineMark)) != null);
            bool secondEdgeReady = !createTwoEdgesMode ||
                (manager != null && manager.GetSelectedObjectCount2(SecondEdgeMark) == 1 &&
                 manager.GetSelectedObject6(1, SecondEdgeMark) is Edge);
            bool sketchReady = true;
            if (create2DMode || createTwoEdgesMode)
            {
                Sketch activeSketch = model.SketchManager.ActiveSketch as Sketch;
                sketchReady = create2DMode
                    ? activeSketch != null && !activeSketch.Is3D()
                    : activeSketch == null || activeSketch.Is3D();
            }
            if (page == null || !shown || closing) return;
            page.EnableButton(
                (int)swPropertyManagerPageButtons_e.swPropertyManagerPageButton_Ok,
                edgeReady && splineReady && secondEdgeReady && sketchReady);
        }

        private void Notify(string message, swMessageBoxIcon_e icon)
        {
            if (app != null)
                app.SendMsgToUser2(message, (int)icon,
                    (int)swMessageBoxBtn_e.swMbOk);
        }

        public void AfterActivation() { }
        public void OnClose(int reason)
        {
            // OnClose runs before the native page and its controls are torn
            // down. AfterClose is too late to stop UI timer callbacks safely.
            closing = true;
            shown = false;
            CancelModeRefresh();
            executeAfterClose = reason ==
                (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay;
            if (!executeAfterClose) return;
            SelectionMgr manager = model.SelectionManager as SelectionMgr;
            selectedEdge = manager == null ||
                manager.GetSelectedObjectCount2(EdgeMark) != 1
                    ? null : manager.GetSelectedObject6(1, EdgeMark) as Edge;
            selectedSpline = !moveMode || manager == null ||
                manager.GetSelectedObjectCount2(SplineMark) != 1
                    ? null : EdgeToEqualSplineCommand.ResolveSelectedSketchSpline(
                        model, manager.GetSelectedObject6(1, SplineMark));
            selectedSecondEdge = !createTwoEdgesMode || manager == null ||
                manager.GetSelectedObjectCount2(SecondEdgeMark) != 1
                    ? null : manager.GetSelectedObject6(1, SecondEdgeMark) as Edge;
        }
        public void AfterClose()
        {
            closing = true;
            shown = false;
            CancelModeRefresh();
            page = null;
            createOption = null;
            moveOption = null;
            create2DOption = null;
            createTwoEdgesOption = null;
            edgeBox = null;
            secondEdgeBox = null;
            splineBox = null;
            moveHint = null;
            create2DHint = null;
            twoEdgesHint = null;
            splineBoxControl = null;
            splineGroup = null;
            targetGroup = null;
            secondEdgeGroup = null;
            closed?.Invoke(this);
            if (!executeAfterClose) return;

            if (selectedEdge == null || (moveMode && selectedSpline == null) ||
                (createTwoEdgesMode && selectedSecondEdge == null))
            {
                Notify(moveMode
                    ? "Chọn 2 đường spline: spline nguồn trong 3D Sketch " +
                      "đang Edit và cạnh spline đích của Surface/Body."
                    : create2DMode
                        ? "Edit 2D Sketch, chọn 1 cạnh Surface/Body trên mặt phẳng Sketch."
                        : createTwoEdgesMode
                        ? "Chọn 2 cạnh Surface/Body nối tiếp trong hai ô chọn cạnh."
                        : "Chọn 1 Edge Surface/Body để tạo spline.",
                    swMessageBoxIcon_e.swMbInformation);
                return;
            }
            ModelDoc2 active = app.ActiveDoc as ModelDoc2;
            if (active == null ||
                (!ReferenceEquals(active, model) &&
                 active.GetTitle() != model.GetTitle()))
            {
                Notify("Part đã thay đổi. Hãy mở AUTO SPLINE lại.",
                    swMessageBoxIcon_e.swMbWarning);
                return;
            }

            try
            {
                model.ClearSelection2(true);
                bool selected = SelectWithMark(selectedEdge, false, 0);
                if (selected && moveMode)
                    selected = SelectWithMark(selectedSpline, true, 0);
                if (selected && createTwoEdgesMode)
                    selected = SelectWithMark(selectedSecondEdge, true, 0);
                if (!selected)
                    throw new InvalidOperationException(
                        "Không khôi phục được cạnh/spline đã chọn.");
                var command = new EdgeToEqualSplineCommand(app);
                if (createTwoEdgesMode) command.RunCreate3DFromTwoEdges(owner);
                else if (create2DMode) command.RunCreate2D(owner);
                else command.Run(owner, moveMode);
            }
            catch (Exception ex)
            {
                Notify("Không chạy được AUTO SPLINE: " + ex.Message,
                    swMessageBoxIcon_e.swMbStop);
            }
        }

        public bool OnHelp()
        {
            Notify("TẠO 3D: chọn một Edge, bấm dấu ✓.\n\n" +
                   "GỘP 2 CẠNH: chọn hai cạnh Surface/Body nối tiếp trong hai ô chọn, bấm dấu ✓. " +
                   "Tạo một spline liên tục trong 3D Sketch đang Edit hoặc 3D Sketch mới. " +
                   "Điểm chia đều trên tổng chiều dài hai cạnh, sai số ≤ 0,20 mm.\n\n" +
                   "TẠO 2D: Edit 2D Sketch, chọn cạnh Surface/Body nằm trên mặt phẳng Sketch, " +
                   "rồi bấm dấu ✓. Spline được thêm vào Sketch đang Edit; điểm chia đều, sai số ≤ 0,20 mm.\n\n" +
                   "DI CHUYỂN: Edit 3D Sketch, chọn hai đường spline: " +
                   "spline nguồn trong Sketch và cạnh spline đích " +
                   "của Surface/Body, rồi bấm dấu ✓.\n\n" +
                   "Dấu ✕ đóng bảng mà không chạy lệnh.",
                   swMessageBoxIcon_e.swMbInformation);
            return true;
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
        public void OnOptionCheck(int id)
        {
            if (closing || (id != CreateOptionId && id != MoveOptionId &&
                id != Create2DOptionId && id != CreateTwoEdgesOptionId)) return;
            moveMode = id == MoveOptionId;
            create2DMode = id == Create2DOptionId;
            createTwoEdgesMode = id == CreateTwoEdgesOptionId;
            displayedMoveMode = null;
            displayedCreate2DMode = null;
            displayedCreateTwoEdgesMode = null;
            ScheduleModeRefresh();
        }
        public void OnButtonPress(int id) { }
        public void OnTextboxChanged(int id, string value) { }
        public void OnNumberboxChanged(int id, double value) { }
        public void OnComboboxEditChanged(int id, string value) { }
        public void OnComboboxSelectionChanged(int id, int item) { }
        public void OnListboxSelectionChanged(int id, int item) { }
        public void OnSelectionboxFocusChanged(int id) { }
        public void OnSelectionboxListChanged(int id, int count)
        {
            if (refreshingModeDisplay) return;
            UpdateReadyState();
        }
        public void OnSelectionboxCalloutCreated(int id) { }
        public void OnSelectionboxCalloutDestroyed(int id) { }
        public bool OnSubmitSelection(int id, object selection, int type,
            ref string itemText)
        {
            if (closing) return false;
            if (id == EdgeBoxId) return selection is Edge;
            if (id == SecondEdgeBoxId) return createTwoEdgesMode && selection is Edge;
            return id == SplineBoxId && moveMode &&
                EdgeToEqualSplineCommand.ResolveSelectedSketchSpline(
                    model, selection) != null;
        }
        public int OnActiveXControlCreated(int id, bool status) { return 0; }
        public void OnSliderPositionChanged(int id, double value) { }
        public void OnSliderTrackingCompleted(int id, double value) { }
        public bool OnKeystroke(int wparam, int message, int lparam, int id)
        {
            return false;
        }
        public void OnPopupMenuItem(int id) { }
        public void OnPopupMenuItemUpdate(int id, ref int value) { }
        public void OnGainedFocus(int id) { }
        public void OnLostFocus(int id) { }
        public int OnWindowFromHandleControlCreated(int id, bool status)
        {
            return 0;
        }
        public void OnListboxRMBUp(int id, int x, int y) { }
        public void OnNumberBoxTrackingCompleted(int id, double value) { }
    }
}
