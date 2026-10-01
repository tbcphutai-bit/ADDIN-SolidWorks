using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace ADDIN.UI
{
    // Native SOLIDWORKS PropertyManager for the two workflows of one AUTO SPLINE command.
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
        private const int EdgeMark = 1;
        private const int SplineMark = 2;

        private readonly ISldWorks app;
        private readonly IWin32Window owner;
        private readonly Action<AutoSplinePropertyManagerPage> closed;
        private readonly ModelDoc2 model;
        private PropertyManagerPage2 page;
        private PropertyManagerPageOption createOption;
        private PropertyManagerPageOption moveOption;
        private PropertyManagerPageSelectionbox edgeBox;
        private PropertyManagerPageSelectionbox splineBox;
        private PropertyManagerPageControl moveHint;
        private PropertyManagerPageControl splineBoxControl;
        private PropertyManagerPageGroup splineGroup;
        private PropertyManagerPageGroup targetGroup;
        private Timer modeRefreshTimer;
        private bool? displayedMoveMode;
        private bool moveMode;
        private bool shown;
        private bool executeAfterClose;
        private Edge selectedEdge;
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
            if (shown && page != null)
                page.Close(false);
        }

        public void Show()
        {
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
                "Tạo spline mới từ cạnh", align, controlOptions,
                "Chọn một Edge để tạo 3D spline mới.") as PropertyManagerPageOption;
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
            edgeBox = targetGroup.AddControl2(EdgeBoxId,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Chọn cạnh spline Surface/Body", align, controlOptions,
                "TẠO: cạnh cong làm mẫu. MOVE: cạnh spline Surface/Body làm đích.")
                as PropertyManagerPageSelectionbox;
            edgeBox.Mark = EdgeMark;
            edgeBox.SingleEntityOnly = true;
            edgeBox.Height = 36;
            edgeBox.SetSelectionFilters(new int[] { (int)swSelectType_e.swSelEDGES });

            if (createOption == null || moveOption == null || edgeBox == null ||
                splineGroup == null || splineBox == null ||
                splineBoxControl == null || moveHint == null ||
                targetGroup == null)
                throw new InvalidOperationException("Không tạo đủ mục chọn AUTO SPLINE.");

            createOption.Checked = !moveMode;
            moveOption.Checked = moveMode;
            splineGroup.Visible = moveMode;
            splineGroup.Expanded = moveMode;
            moveHint.Visible = moveMode;
            splineBoxControl.Visible = moveMode;
            targetGroup.Caption = moveMode
                ? "2. Spline đích: cạnh Surface/Body"
                : "Cạnh tạo spline mới";
            page.Show2(0);
            shown = true;
            RestoreInitialSelection();
            // SOLIDWORKS can defer PropertyManager layout changes made during a
            // radio-button COM callback. Reconcile on its UI message loop.
            modeRefreshTimer = new Timer { Interval = 75 };
            modeRefreshTimer.Tick += OnModeRefreshTick;
            modeRefreshTimer.Start();
            RefreshModeDisplay();
        }

        private void OnModeRefreshTick(object sender, EventArgs e)
        {
            if (!shown || moveOption == null) return;
            bool selectedMode = moveOption.Checked;
            if (selectedMode != moveMode)
            {
                moveMode = selectedMode;
                displayedMoveMode = null;
            }
            if (displayedMoveMode != moveMode)
                RefreshModeDisplay();
        }

        private void RefreshModeDisplay()
        {
            if (!shown) return;
            // Release focus from the source selection box before hiding it.
            if (!moveMode) edgeBox.SetSelectionFocus();
            targetGroup.Caption = moveMode
                ? "2. Spline đích: cạnh Surface/Body"
                : "Cạnh tạo spline mới";
            moveHint.Visible = moveMode;
            splineBoxControl.Visible = moveMode;
            splineGroup.Expanded = moveMode;
            splineGroup.Visible = moveMode;
            if (moveMode && selectedSpline == null)
                splineBox.SetSelectionFocus();
            displayedMoveMode = moveMode;
            UpdateReadyState();
        }

        private void CaptureInitialSelection()
        {
            SelectionMgr manager = model.SelectionManager as SelectionMgr;
            if (manager == null) return;
            int count = manager.GetSelectedObjectCount2(-1);
            if (count < 1 || count > 2) return;

            Edge edge = null;
            SketchSpline spline = null;
            for (int i = 1; i <= count; i++)
            {
                object item = manager.GetSelectedObject6(i, -1);
                if (item is Edge) edge = (Edge)item;
                else if (spline == null)
                    spline = EdgeToEqualSplineCommand.ResolveSelectedSketchSpline(model, item);
            }
            selectedEdge = edge;
            selectedSpline = count == 2 ? spline : null;
            moveMode = selectedEdge != null && selectedSpline != null;
        }

        private void RestoreInitialSelection()
        {
            model.ClearSelection2(true);
            if (selectedEdge != null) SelectWithMark(selectedEdge, false, EdgeMark);
            if (moveMode && selectedSpline != null)
                SelectWithMark(selectedSpline, selectedEdge != null, SplineMark);
            if (selectedEdge == null) edgeBox.SetSelectionFocus();
            else if (moveMode && selectedSpline == null) splineBox.SetSelectionFocus();
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
            if (page == null || !shown) return;
            SelectionMgr manager = model.SelectionManager as SelectionMgr;
            bool edgeReady = manager != null &&
                manager.GetSelectedObjectCount2(EdgeMark) == 1 &&
                manager.GetSelectedObject6(1, EdgeMark) is Edge;
            bool splineReady = !moveMode ||
                (manager != null &&
                 manager.GetSelectedObjectCount2(SplineMark) == 1 &&
                 EdgeToEqualSplineCommand.ResolveSelectedSketchSpline(model,
                     manager.GetSelectedObject6(1, SplineMark)) != null);
            page.EnableButton(
                (int)swPropertyManagerPageButtons_e.swPropertyManagerPageButton_Ok,
                edgeReady && splineReady);
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
        }
        public void AfterClose()
        {
            shown = false;
            if (modeRefreshTimer != null)
            {
                modeRefreshTimer.Stop();
                modeRefreshTimer.Tick -= OnModeRefreshTick;
                modeRefreshTimer.Dispose();
                modeRefreshTimer = null;
            }
            page = null;
            closed?.Invoke(this);
            if (!executeAfterClose) return;

            if (selectedEdge == null || (moveMode && selectedSpline == null))
            {
                Notify(moveMode
                    ? "Chọn 2 đường spline: spline nguồn trong 3D Sketch " +
                      "đang Edit và cạnh spline đích của Surface/Body."
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
                if (!selected)
                    throw new InvalidOperationException(
                        "Không khôi phục được cạnh/spline đã chọn.");
                new EdgeToEqualSplineCommand(app).Run(owner, moveMode);
            }
            catch (Exception ex)
            {
                Notify("Không chạy được AUTO SPLINE: " + ex.Message,
                    swMessageBoxIcon_e.swMbStop);
            }
        }

        public bool OnHelp()
        {
            Notify("TẠO: chọn một Edge, bấm dấu ✓.\n\n" +
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
            if (id != CreateOptionId && id != MoveOptionId) return;
            moveMode = id == MoveOptionId;
            displayedMoveMode = null;
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
            UpdateReadyState();
        }
        public void OnSelectionboxCalloutCreated(int id) { }
        public void OnSelectionboxCalloutDestroyed(int id) { }
        public bool OnSubmitSelection(int id, object selection, int type,
            ref string itemText)
        {
            if (id == EdgeBoxId) return selection is Edge;
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
