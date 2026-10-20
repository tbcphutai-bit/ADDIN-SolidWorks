using System;

using System.Collections.Generic;

using System.ComponentModel;

using System.Globalization;

using System.Drawing;

using System.Drawing.Drawing2D;

using System.IO;

using System.Runtime.InteropServices;

using System.Text;

using System.Windows.Forms;

using ADDIN.Commands;

using ADDIN.HoleManagement;

using SolidWorks.Interop.sldworks;

using SolidWorks.Interop.swconst;



namespace ADDIN

{

    [ComVisible(true)]

    [ProgId("ADDIN.BomTaskPaneControl")]

    public partial class BomTaskPaneControl : System.Windows.Forms.UserControl

    {

        private ISldWorks swApp;

        private DSldWorksEvents_Event swEvents;

        private BomLoader bomLoader;

        private ThaoTacBomTaskPane actions;

        private XoayDrawingView drawingViewRotator;

        private ChinhTiLeDrawingView drawingViewFitter;

        private TaoDimKegaki drawingDimensionGenerator;

        private DimKichThuocLo holeDimensionCommand;

        private LenhDimCanhSongSong sectionEdgeDimensionCommand;

        private SplineToArcsCommand splineToArcsCommand;

        private ADDIN.UI.AutoSplinePropertyManagerPage autoSplinePage;

        private LenhNoteTextBalloon drawingTextAnnotationCommands;

        private CheckBalloon balloonChecker;

        private CheckDrawingBom drawingBomChecker;
        private CheckAssemblyHole assemblyHoleChecker;

        private XepUnitDrawing xepUnitDrawing;

        private LenhMakeHole makeHoleCommand;

        private PaintHoleSummaryCommand paintHoleSummaryCommand;

        private ChangeHoleNameCommand changeHoleNameCommand;

        private HoleNamePresetManager holeNamePresetManager;

        private List<string> holeNamePresets = new List<string>();

        private Label lblHoleType;

        private ComboBox cboHoleType;

        private Button btnManageHoleTypes;

        private Button btnChangeHoleName;

        private VCutPropertySetupCommand vCutPropertySetupCommand;

        private Timer componentDrawingTimer;

        private Timer makeHoleUpdateTimer;

        private Timer initialLayoutTimer;

        private bool initialLayoutQueued;

        private Size initialLayoutObservedSize;

        private ToolTip bomCommandToolTip;

        private Font bomCommandToolTipFont;

        private string manualBomCommandToolTipText;

        private Control lastDisabledBomToolTipControl;

        private int initialLayoutPassesRemaining;

        private bool taskPaneLayoutInProgress;

        private bool drawingBomCommandInProgress;

        private bool drawingBomCancelRequested;

        private bool drawingBomUiLockActive;

        private IMessageFilter solidWorksInputBlocker;

        private readonly Dictionary<Control, bool> drawingBomControlEnabledStates =

            new Dictionary<Control, bool>();

        private Control taskPaneHostControl;

        private string lastSelectedViewKey;

        private bool solidWorksClosing;

        private bool repairHolePanelMode;

        private ADDIN.UI.RepairHoleViewModel repairHoleViewModel;

        private ADDIN.UI.RepairHoleTaskPaneView repairHoleTaskPaneView;

        private Label lblMakeHolePaintName;

        private TextBox txtMakeHolePaintName;

        private const string AppUiFontName = "Segoe UI";

        private readonly Dictionary<string, string> loadedModelPropValues = new Dictionary<string, string>();



        private sealed class SolidWorksInputBlocker : IMessageFilter

        {

            private readonly Control allowedControl;



            [DllImport("user32.dll")]

            private static extern IntPtr GetAncestor(IntPtr handle, uint flags);



            [DllImport("user32.dll", CharSet = CharSet.Auto)]

            private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);



            [DllImport("user32.dll")]

            private static extern IntPtr GetWindow(IntPtr handle, uint command);



            public SolidWorksInputBlocker(Control allowed)

            {

                allowedControl = allowed;

            }



            public bool PreFilterMessage(ref Message message)

            {

                if (!IsUserInputMessage(message.Msg))

                    return false;

                return !IsAllowedTarget(message.HWnd);

            }



            private bool IsAllowedTarget(IntPtr handle)

            {

                Control control = Control.FromHandle(handle);

                while (control != null)

                {

                    if (control == allowedControl)

                        return true;

                    control = control.Parent;

                }

                IntPtr root = GetAncestor(handle, 2);

                StringBuilder className = new StringBuilder(64);

                if (root != IntPtr.Zero && GetClassName(root, className, className.Capacity) > 0

                    && string.Equals(className.ToString(), "#32770", StringComparison.Ordinal)

                    && GetWindow(root, 4) != IntPtr.Zero)

                    return true;

                return false;

            }



            private static bool IsUserInputMessage(int message)

            {

                if (message >= 0x0100 && message <= 0x0109)

                    return true;

                if (message >= 0x0201 && message <= 0x020E)

                    return true;

                if (message >= 0x00A1 && message <= 0x00AD)

                    return true;

                return message == 0x007B;

            }

        }



        private const string MakeHoleSizeHistoryFileName = "make-hole-sizes.txt";

        private const string PropNameHinmei = "\u54c1\u540d";

        private const string PropNameBuhinmei = "\u90e8\u54c1\u540d";

        private const string PropNameMaterial = "\u6750\u8cea";

        private const string PropNameThickness = "\u677f\u539a";

        private const string PropNameGoban = "\u5408\u756a";

        private const string PropNameQty = "\u6570\u91cf";

        private const string PropNameFinish = "\u4ed5\u4e0a\u3052";



        [StructLayout(LayoutKind.Sequential)]

        private struct NativeRect

        {

            public int Left;

            public int Top;

            public int Right;

            public int Bottom;

        }



        [DllImport("user32.dll")]

        private static extern IntPtr GetParent(IntPtr hWnd);



        [DllImport("user32.dll")]

        private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);



        public BomTaskPaneControl()

        {

            InitializeComponent();

            tabBom.TabStyle = ADDIN.UI.ModernTabStyle.Card;

            tabDrawingPages.TabStyle = ADDIN.UI.ModernTabStyle.Underline;

            tabModelPages.TabStyle = ADDIN.UI.ModernTabStyle.Underline;

            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)

                return;

            ADDIN.UI.GridStyler.ApplyModernStyle(this.dgvModelBom);

            ADDIN.UI.UIHelper.EnableDoubleBuffer(this.tabComponentDrawing);

            ADDIN.UI.UIHelper.EnableDoubleBuffer(this.tabDrawingBom);

            ADDIN.UI.UIHelper.EnableDoubleBuffer(this.tabModel);

            ADDIN.UI.UIHelper.EnableDoubleBuffer(this.tabModelMacroPage);

            if (lblCheckAssemblyHoleResult != null)

                lblCheckAssemblyHoleResult.Anchor = AnchorStyles.None;

            if (tabModelMacroPage != null)

                tabModelMacroPage.AutoScroll = true;

            EnsureCheckBalloonButton();

            EnsureCheckDrawingBomButton();

            EnsureCheckDxfButtons();
            EnsureDrawingBomDxfExportButton();

            EnsureMakeHolePaintNameControls();

            InitializeHoleTypeControls();


            SelectModelSideTab("Props");

            InitMakeHoleOptions();
            ShowMakeHoleActions();

            WireEvents();

            InitBomCommandToolTips();

            InitComponentDrawingTimer();

            LayoutDrawingBomTab();

            LayoutModelCommandButtons();

            LayoutModelMacroButtons();

            LayoutMakeHoleOptions();

            InitMakeHoleUpdateMonitor();

            // Names, icons, colours and fonts of the whole pane: UI\BomTaskPaneTheme.cs.
            ApplyTaiTheme();
            EnsureSettingsTab();



            // lblTitle.Text = "DRAWING BOM";

            // lblStatus.Text = "Dang cho ket noi...";



            UpdateBomCommandButtonState();

            UpdateModelMacroCommandAvailability(0);

        }



        public void Init(ISldWorks app)

        {
            assemblyHoleChecker?.CloseResults();
            assemblyHoleChecker = null;

            swApp = app;

            solidWorksClosing = false;

            StartComponentDrawingTimer();

            bomLoader = new BomLoader(swApp);

            drawingViewRotator = new XoayDrawingView(swApp);

            drawingViewFitter = new ChinhTiLeDrawingView(swApp);

            drawingDimensionGenerator = new TaoDimKegaki(swApp);

            holeDimensionCommand = new DimKichThuocLo(swApp);

            sectionEdgeDimensionCommand = new LenhDimCanhSongSong(swApp);

            splineToArcsCommand = new SplineToArcsCommand(swApp);

            xepUnitDrawing = new XepUnitDrawing(swApp);

            makeHoleCommand = new LenhMakeHole(swApp);

            BindRepairHoleViewModelToMakeHoleCommand();

            paintHoleSummaryCommand = new PaintHoleSummaryCommand(swApp);

            changeHoleNameCommand = new ChangeHoleNameCommand(swApp);

            drawingTextAnnotationCommands = new LenhNoteTextBalloon(

                swApp,

                this,

                cboBendLine,

                cboSide,

                cboBalloonProperty);

            balloonChecker = new CheckBalloon(swApp);

            drawingBomChecker = new CheckDrawingBom(swApp, dgvModelBom);

            actions = new ThaoTacBomTaskPane(swApp, bomLoader, dgvModelBom, chkSelectAll, lblStatus, progressCheck, this);

            actions.ConfigureGrid();

            ADDIN.UI.GridStyler.ConfigureColumns(this.dgvModelBom);

            AttachSolidWorksEvents();

            SwitchTabByActiveDocument();

            if (IsActiveModelDocument())

                LoadModelPropsFromActiveDocument(false);

            LayoutComponentViewSize();

            LayoutModelCommandButtons();

            LayoutMakeHoleOptions();



            lblStatus.Text = swApp != null

                ? "● Đã kết nối SOLIDWORKS"

                : "○ Đang chờ kết nối...";

            ScheduleInitialTaskPaneLayout();

        }



        public void ShutdownFromSolidWorks()

        {
            assemblyHoleChecker?.CloseResults();
            assemblyHoleChecker = null;

            CloseDxfCheckWindow();
            CloseBendMinimumWindow();
            CloseDrawingBomCheckReview();

            solidWorksClosing = true;

            DismissNativeMakeHolePage();
            DismissNativeModelCommand();
            autoSplinePage?.Dismiss();

            autoSplinePage = null;

            actions?.RequestCancel();

            EndSolidWorksInputLock();

            DisposeComponentDrawingTimer();

            DisposeInitialLayoutTimer();

            DetachSolidWorksEvents();



            bomLoader = null;

            actions = null;

            drawingViewRotator = null;

            drawingViewFitter = null;

            drawingDimensionGenerator = null;

            holeDimensionCommand = null;

            sectionEdgeDimensionCommand = null;

            splineToArcsCommand = null;

            drawingTextAnnotationCommands = null;

            xepUnitDrawing = null;

            if (repairHoleViewModel != null)

            {

                repairHoleViewModel.Initialize(null, null);

                repairHoleViewModel.ResetPanel();

            }

            makeHoleCommand = null;

            paintHoleSummaryCommand = null;

            vCutPropertySetupCommand = null;

            drawingBomChecker = null;

            swApp = null;

            UpdateModelMacroCommandAvailability(0);

        }



        protected override void OnHandleCreated(EventArgs e)

        {

            base.OnHandleCreated(e);

            ScheduleInitialTaskPaneLayout();

        }



        protected override void OnVisibleChanged(EventArgs e)

        {

            base.OnVisibleChanged(e);

            if (Visible)

                ScheduleInitialTaskPaneLayout();

        }



        protected override void OnParentChanged(EventArgs e)

        {

            if (taskPaneHostControl != null)

                taskPaneHostControl.SizeChanged -= TaskPaneHostControl_SizeChanged;



            base.OnParentChanged(e);

            if (Parent != null)

            {

                taskPaneHostControl = Parent;

                taskPaneHostControl.SizeChanged += TaskPaneHostControl_SizeChanged;

                Margin = System.Windows.Forms.Padding.Empty;

                Dock = DockStyle.Fill;

                SyncSizeWithTaskPaneHost();

                ScheduleInitialTaskPaneLayout();

            }

            else

            {

                taskPaneHostControl = null;

            }

        }



        private void TaskPaneHostControl_SizeChanged(object sender, EventArgs e)

        {

            SyncSizeWithTaskPaneHost();

            ForceTaskPaneLayout();

        }



        private void SyncSizeWithTaskPaneHost()

        {

            // ActiveX hosting can have an HWND parent without a WinForms Parent.

            // Prefer its actual client area before consulting the managed host.

            Size hostSize = taskPaneHostControl != null && !taskPaneHostControl.IsDisposed

                ? taskPaneHostControl.ClientSize

                : Size.Empty;

            bool usingNativeHostSize = false;

            if (IsHandleCreated)

            {

                IntPtr nativeParent = GetParent(Handle);

                NativeRect nativeRect;

                if (nativeParent != IntPtr.Zero && GetClientRect(nativeParent, out nativeRect))

                {

                    int nativeWidth = nativeRect.Right - nativeRect.Left;

                    int nativeHeight = nativeRect.Bottom - nativeRect.Top;

                    if (nativeWidth > 0 && nativeHeight > 0)

                    {

                        hostSize = new Size(nativeWidth, nativeHeight);

                        usingNativeHostSize = true;

                    }

                }

            }



            if (hostSize.Width <= 0 || hostSize.Height <= 0)

                return;



            if (usingNativeHostSize)

            {

                if (Dock != DockStyle.None)

                    Dock = DockStyle.None;

                if (Location != Point.Empty || Size != hostSize)

                    SetBounds(0, 0, hostSize.Width, hostSize.Height);

            }

            else if (taskPaneHostControl != null && !taskPaneHostControl.IsDisposed)

            {

                if (Dock != DockStyle.Fill)

                    Dock = DockStyle.Fill;

                if (Size != hostSize)

                    Size = hostSize;

                taskPaneHostControl.PerformLayout();

            }

        }



        protected override void OnLayout(LayoutEventArgs e)

        {

            base.OnLayout(e);

            if (IsHandleCreated && !IsDisposed && !taskPaneLayoutInProgress)

                ForceTaskPaneLayout();

        }



        private void ScheduleInitialTaskPaneLayout()

        {

            if (!IsHandleCreated || IsDisposed || Disposing || initialLayoutQueued)

                return;



            initialLayoutQueued = true;

            BeginInvoke((Action)(() =>

            {

                initialLayoutQueued = false;

                if (IsDisposed || Disposing || !IsHandleCreated)

                    return;

                SyncSizeWithTaskPaneHost();

                ForceTaskPaneLayout();

                StartInitialLayoutTimer();

            }));

        }



        private void StartInitialLayoutTimer()

        {

            if (IsDisposed)

                return;



            if (initialLayoutTimer == null)

            {

                initialLayoutTimer = new Timer();

                initialLayoutTimer.Interval = 150;

                initialLayoutTimer.Tick += InitialLayoutTimer_Tick;

            }



            // The SOLIDWORKS host assigns the final TaskPane size asynchronously.

            // Repeat layout briefly so the user does not have to drag the pane first.

            if (!initialLayoutTimer.Enabled)

            {

                initialLayoutObservedSize = Size;

                initialLayoutPassesRemaining = 12;

                initialLayoutTimer.Start();

            }

        }



        private void InitialLayoutTimer_Tick(object sender, EventArgs e)

        {

            if (IsDisposed || !Visible || initialLayoutPassesRemaining-- <= 0)

            {

                initialLayoutTimer?.Stop();

                return;

            }



            SyncSizeWithTaskPaneHost();

            // Host sizing is asynchronous. Poll briefly, but do not relayout

            // the entire pane on every tick when its size has not changed.

            if (Size != initialLayoutObservedSize)

            {

                initialLayoutObservedSize = Size;

                ForceTaskPaneLayout();

            }

        }



        private void DisposeInitialLayoutTimer()

        {

            if (taskPaneHostControl != null)

            {

                taskPaneHostControl.SizeChanged -= TaskPaneHostControl_SizeChanged;

                taskPaneHostControl = null;

            }



            if (initialLayoutTimer == null)

                return;



            initialLayoutTimer.Stop();

            initialLayoutTimer.Tick -= InitialLayoutTimer_Tick;

            initialLayoutTimer.Dispose();

            initialLayoutTimer = null;

        }



        private void ForceTaskPaneLayout()

        {

            if (IsDisposed || taskPaneLayoutInProgress)

                return;



            taskPaneLayoutInProgress = true;

            SuspendLayout();

            try

            {

                tabBom?.PerformLayout();

                tabModel?.PerformLayout();

                tabModelPages?.PerformLayout();

                tabModelEditPage?.PerformLayout();

                tabModelMacroPage?.PerformLayout();

                panelModelCommands?.PerformLayout();

                LayoutDrawingBomTab();

                LayoutModelCommandButtons();

                LayoutModelMacroButtons();

                LayoutMakeHoleOptions();

                LayoutComponentViewSize();

                

                panelModelCommands?.Invalidate(true);

                pnlMakeHoleDiagram?.Invalidate();

            }

            finally

            {

                ResumeLayout(true);

                taskPaneLayoutInProgress = false;

            }

        }



        private void RefreshHostedTaskPane()

        {

            if (IsDisposed)

                return;



            ForceTaskPaneLayout();

            PerformLayout();

            Invalidate(true);

            Update();



            if (IsHandleCreated)

            {

                BeginInvoke((Action)(() =>

                {

                    ForceTaskPaneLayout();

                    dgvModelBom?.PerformLayout();

                    dgvModelBom?.Invalidate();

                    dgvModelBom?.Update();

                }));

            }

        }



        private void EnsureCheckBalloonButton()

        {

            if (btnCheckBalloon != null)

                return;



            btnCheckBalloon = new ADDIN.UI.ModernButton();

            btnCheckBalloon.Name = "btnCheckBalloon";

            btnCheckBalloon.Text = "Balloon";

            btnCheckBalloon.Subtitle = "Đánh số chi tiết";

            btnCheckBalloon.Size = new Size(84, 46);

            btnCheckBalloon.TabIndex = 13;

            btnCheckBalloon.UseVisualStyleBackColor = false;

            tabDrawingBom.Controls.Add(btnCheckBalloon);

        }



        private void EnsureCheckDrawingBomButton()

        {

            if (btnCheckDrawingBom != null)

                return;



            btnCheckDrawingBom = new ADDIN.UI.ModernButton();



            btnCheckDrawingBom.Name = "btnCheckDrawingBom";

            btnCheckDrawingBom.Text = "Drawing";

            btnCheckDrawingBom.Subtitle = "Kiểm tra bản vẽ 2D";

            btnCheckDrawingBom.Size = new Size(84, 46);

            btnCheckDrawingBom.TabIndex = 17;

            btnCheckDrawingBom.UseVisualStyleBackColor = false;



            if (tabDrawingBom != null &&

                !tabDrawingBom.Controls.Contains(btnCheckDrawingBom))

            {

                tabDrawingBom.Controls.Add(btnCheckDrawingBom);

            }

        }



        private void WireEvents()

        {

            btnLoadBom.Click += btnLoadBom_Click;

            btnClearBom.Click += btnClearBom_Click;

            btnCheckDfTk.Click += btnCheckDfTk_Click;

            btnCheckUraOmote.Click += btnCheckUraOmote_Click;

            btnCheckKegaki.Click += btnCheckKegaki_Click;

            btnCheckAll.Click += btnCheckAll_Click;

            btnCheckRound.Click += btnCheckRound_Click;

            btnCheckSamePart.Click += btnCheckSamePart_Click;

            if (btnCheckDrawingBom != null)

                btnCheckDrawingBom.Click += btnCheckDrawingBom_Click;

            button1.Click += cancel_Click;

            btnGetWL.Click += btnGetWL_Click;

            btnNote.Click += btnNote_Click;

            btnNote.MouseUp += btnNote_MouseUp;

            btnText.Click += btnText_Click;

            btnInsertBalloon.Click += btnInsertBalloon_Click;

            btnDeleteNote.Click += btnDeleteNote_Click;

            btnDeleteText.Click += btnDeleteText_Click;

            btnHorizontalAlignment.Click += btnHorizontalAlignment_Click;

            btnRotateCw.Click += btnRotateCw_Click;

            btnRotateCcw.Click += btnRotateCcw_Click;

            dimvang.Click += dimvang_Click;

            btnFixScale.Click += btnFixScale_Click;

            btnDimKegaki.Click += btnDimKegaki_Click;

            if (btnDimKichThuocLo != null)

                btnDimKichThuocLo.Click += btnDimKichThuocLo_Click;

            if (btnRepairDim != null)

                btnRepairDim.Click += btnRepairDim_Click;

            btnDimMatCat.Click += btnDimMatCat_Click;

            btnVCutSection.Click += btnVCutSection_Click;

            btnVCutPropertySetup.Click += btnVCutPropertySetup_Click;

            vCutBendAssignmentControl.SaveRequested += vCutBendAssignmentControl_SaveRequested;

            vCutBendAssignmentControl.RefreshRequested += vCutBendAssignmentControl_RefreshRequested;

            vCutBendAssignmentControl.CloseRequested += vCutBendAssignmentControl_CloseRequested;

            vCutBendAssignmentControl.BendSelected += vCutBendAssignmentControl_BendSelected;
            vCutBendAssignmentControl.MinimumSelected += row => vCutPropertySetupCommand?.SelectMinimum(row);

            btnSplineToArcs.Click += btnSplineToArcs_Click;

            btnMakeHole.Click += btnMakeHole_Click;

            btnRepairHole.Click += btnRepairHole_Click;

            btnPaintHoleSummary.Click += btnPaintHoleSummary_Click;

            // Update button for tracked Make Hole patterns

            btnMakeHoleUpdate.Click += btnMakeHoleUpdate_Click;

            btnMakeHoleAccept.Click += btnMakeHoleAccept_Click;

            btnMakeHolePattern.Click += btnMakeHolePattern_Click;

            btnMakeHoleReset.Click += btnMakeHoleReset_Click;

            txtMakeHolePitch.TextChanged += MakeHoleTrackedInputChanged;

            chkMakeHolePaint.CheckedChanged += chkMakeHolePaint_CheckedChanged;

            cboRepairHoleDiameter.Leave += MakeHoleSizeHistory_Leave;

            cboRepairHoleDiameter.Leave += MakeHoleSizeHistory_Leave;

            btnModelApplyProps.Click += btnModelApplyProps_Click;

            btnModelUpdateProps.Click += btnModelUpdateProps_Click;

            btnModelResetProps.Click += btnModelResetProps_Click;

            tabBom.SelectedIndexChanged += tabBom_SelectedIndexChanged;

            if (button2 != null)

            {

                button2.Click -= btnXepUnit_Click;

                button2.Click += btnXepUnit_Click;

            }

            WireXepUnitButton(this);

            btnOpenAssem.Click += btnOpenAssem_Click;

            btnCheckBalloon.Click += btnCheckBalloon_Click;

            if (btnMirrorPart != null)

                btnMirrorPart.Click += btnMirrorPart_Click;

            if (btnCheckAssemblyHole != null)

                btnCheckAssemblyHole.Click += btnCheckAssemblyHole_Click;

            chkSelectAll.CheckedChanged += chkSelectAll_CheckedChanged;



            dgvModelBom.CurrentCellDirtyStateChanged += dgvModelBom_CurrentCellDirtyStateChanged;

            dgvModelBom.CellMouseDown += dgvModelBom_CellMouseDown;

            dgvModelBom.CellContentClick += dgvModelBom_CellContentClick;

            dgvModelBom.KeyDown += dgvModelBom_KeyDown;

            dgvModelBom.SizeChanged += dgvModelBom_SizeChanged;

            tabComponentDrawing.Resize += tabComponentDrawing_Resize;

            tabDrawingBom.Resize += (s, e) => LayoutDrawingBomTab();

            tabDrawingPages.SelectedIndexChanged += (s, e) => LayoutDrawingBomTab();

            tabComponentDrawing.Scroll += (s, e) => { tabComponentDrawing.Invalidate(true); };

            tabDrawingBom.Scroll += (s, e) => { tabDrawingBom.Invalidate(true); };

            if (tabModelMacroPage != null)

            {

                tabModelMacroPage.Resize += (s, e) => LayoutModelMacroButtons();

                tabModelMacroPage.Scroll += (s, e) => { tabModelMacroPage.Invalidate(true); };

            }

            if (tabModelPages != null)

            {

                tabModelPages.SelectedIndexChanged += (s, e) =>

                {

                    LayoutModelCommandButtons();

                    LayoutModelMacroButtons();

                };

            }

            pnlMakeHoleDiagram.Paint += pnlMakeHoleDiagram_Paint;

            Resize += BomTaskPaneControl_Resize;

            panelModelCommands.SizeChanged += PanelModelCommands_SizeChanged;

            Disposed += BomTaskPaneControl_Disposed;

        }



        private void btnCheckDrawingBom_Click(object sender, EventArgs e)

        {

            DrawingBatchCheckResult drawingResult = null;

            RunDrawingBomCommand(() =>

            {

                drawingResult = drawingBomChecker?.RunBatch(

                    BeginProgress,

                    UpdateProgress,

                    FinishProgress,

                    IsDrawingBomCancelRequested, true);
                if (drawingResult != null) { lblStatus.Text = drawingResult.Message; bomCommandToolTip?.SetToolTip(lblStatus, drawingResult.ReportPath ?? ""); }

            }, true, true);

            // Batch execution is silent; show its own completed review only after
            // the current-session display scope and input lock have been released.
            ShowDrawingBomCheckReview(drawingResult);
        }

        private void btnMirrorPart_Click(object sender, EventArgs e)

        {
            if (!CanRunModelMacro(btnMirrorPart)) return;

            try

            {

                CreateMirrorPartPackage mirrorCmd = new CreateMirrorPartPackage(swApp);

                mirrorCmd.Run();

            }

            catch (Exception ex)

            {

                MessageBox.Show("Lỗi khởi chạy MIRROR PART:\n" + ex.Message, "MIRROR PART", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }

        }



        private void btnCheckAssemblyHole_Click(object sender, EventArgs e)
        {
            if (!CanRunModelMacro(btnCheckAssemblyHole)) return;
            try
            {
                if (assemblyHoleChecker == null)
                {
                    assemblyHoleChecker = new CheckAssemblyHole(swApp);
                    assemblyHoleChecker.ResultTextChanged += ShowCheckHoleInspectionReport;
                    assemblyHoleChecker.ResultsViewReady += ShowCheckHoleResultsView;
                }
                btnCheckAssemblyHole.Enabled = false;
                try { assemblyHoleChecker.Run(); }
                finally { if (!btnCheckAssemblyHole.IsDisposed) btnCheckAssemblyHole.Enabled = true; }
            }
            catch (Exception ex)
            {
                ShowModelInspectionReport("Lỗi khởi chạy CHECK ASSEMBLY HOLE:\n" + ex.Message);
            }
        }

        private void btnRepair3DSpline_Click(object sender, EventArgs e)

        {
            if (!CanRunModelMacro(btnRepair3DSpline)) return;
            ShowNativeRepairSpline();
        }


        private void btnVCutSection_Click(object sender, EventArgs e)

        {
            RunSelectedSection();
        }


        private void btnVCutPropertySetup_Click(object sender, EventArgs e)

        {
            if (!CanRunModelMacro(btnVCutPropertySetup)) return;
            if (vCutBendAssignmentControl != null && vCutBendAssignmentControl.Visible &&
                !vCutBendAssignmentControl.ConfirmDiscardChanges("mở lại Set bend line"))
                return;
            LoadVCutBendAssignments(null, true);
        }


        private void LoadVCutBendAssignments(string preferredFeatureName, bool refreshed = false)

        {

            try

            {

                if (swApp == null)

                {

                    UI.VCutNoticeDialog.Inform(this, "Chưa kết nối SOLIDWORKS",

                        "Không thể mở bảng cạnh bẻ. Hãy kiểm tra kết nối SOLIDWORKS rồi thử lại.",

                        MessageBoxIcon.Warning);

                    return;

                }

                var command = new VCutPropertySetupCommand(swApp);

                List<VCutPropertySetupCommand.BendRow> rows;

                Dictionary<string, string> properties;

                string message;

                if (!command.Load(out rows, out properties, out message))

                {

                    UI.VCutNoticeDialog.Inform(this, "Không thể mở Set bend line",

                        message, MessageBoxIcon.Warning);

                    return;

                }

                vCutPropertySetupCommand = command;

                vCutBendAssignmentControl.LoadRows(rows, properties,

                    command.LoadedPartTitle, command.LoadedConfiguration,

                    preferredFeatureName, refreshed);

                vCutBendAssignmentControl.Visible = true;

                SetModelMacroCommandsCollapsed(true);

                vCutBendAssignmentControl.BringToFront();

                vCutBendAssignmentControl.HighlightCurrentRow();

            }

            catch (Exception ex)

            {

                UI.VCutNoticeDialog.Inform(this, "Không nạp được cạnh bẻ",

                    "Đã xảy ra lỗi khi đọc Bend Feature:\r\n\r\n" + ex,

                    MessageBoxIcon.Error);

            }

        }



        private void vCutBendAssignmentControl_SaveRequested(object sender, EventArgs e)

        {

            try

            {

                if (vCutPropertySetupCommand == null) return;

                string message;

                bool saved = vCutPropertySetupCommand.Save(

                    vCutBendAssignmentControl.Rows,

                    vCutBendAssignmentControl.Properties, out message);

                if (saved) vCutBendAssignmentControl.RefreshAfterSave();

                UI.VCutNoticeDialog.Inform(this,

                    saved ? "Đã lưu gán bào" : "Chưa thể lưu gán bào",

                    message, saved ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            }

            catch (Exception ex)

            {

                UI.VCutNoticeDialog.Inform(this, "Không lưu được loại bào",

                    "Đã xảy ra lỗi khi lưu:\r\n\r\n" + ex,

                    MessageBoxIcon.Error);

            }

        }



        private void vCutBendAssignmentControl_RefreshRequested(object sender, EventArgs e)

        {

            LoadVCutBendAssignments(vCutBendAssignmentControl.CurrentFeatureName, true);

        }



        private void vCutBendAssignmentControl_CloseRequested(object sender, EventArgs e)

        {

            vCutBendAssignmentControl.Visible = false;

            vCutPropertySetupCommand = null;

            SetModelMacroCommandsCollapsed(false);

        }



        private void vCutBendAssignmentControl_BendSelected(

            VCutPropertySetupCommand.BendRow row)

        {

            vCutPropertySetupCommand?.SelectFeature(row);

        }



        private void btnEdgeToEqualSpline_Click(object sender, EventArgs e)

        {
            if (!CanRunModelMacro(btnEdgeToEqualSpline)) return;

            try

            {

                if (swApp == null)

                {

                    MessageBox.Show(

                        "Chưa kết nối SOLIDWORKS.",

                        "AUTO SPLINE",

                        MessageBoxButtons.OK,

                        MessageBoxIcon.Warning);

                    return;

                }



                if (autoSplinePage != null && autoSplinePage.IsOpen)

                    return;



                autoSplinePage = new ADDIN.UI.AutoSplinePropertyManagerPage(

                    swApp, this,

                    page =>

                    {

                        if (ReferenceEquals(autoSplinePage, page))

                            autoSplinePage = null;

                    });

                autoSplinePage.Show();

            }

            catch (Exception ex)

            {

                MessageBox.Show(

                    "Lỗi khởi chạy AUTO SPLINE:\n" + ex.Message,

                    "AUTO SPLINE",

                    MessageBoxButtons.OK,

                    MessageBoxIcon.Error);

            }

        }




        private void EnsureMakeHolePaintNameControls()

        {

            if (lblMakeHolePaintName == null)

            {

                lblMakeHolePaintName = new Label();

                lblMakeHolePaintName.Name = "lblMakeHolePaintName";

                lblMakeHolePaintName.Text = "Name hole";

                lblMakeHolePaintName.AutoSize = false;

                lblMakeHolePaintName.Size = new Size(70, 18);

            }



            if (txtMakeHolePaintName == null)

            {

                txtMakeHolePaintName = new TextBox();

                txtMakeHolePaintName.Name = "txtMakeHolePaintName";

                txtMakeHolePaintName.Size = new Size(118, 20);

            }



            if (grpMakeHoleOptions != null)

            {

                if (!grpMakeHoleOptions.Controls.Contains(lblMakeHolePaintName))

                    grpMakeHoleOptions.Controls.Add(lblMakeHolePaintName);

                if (!grpMakeHoleOptions.Controls.Contains(txtMakeHolePaintName))

                    grpMakeHoleOptions.Controls.Add(txtMakeHolePaintName);

                bool isDesigner = System.ComponentModel.LicenseManager.UsageMode ==

                    System.ComponentModel.LicenseUsageMode.Designtime;

                if (!isDesigner)

                {

                    if (lblRepairHoleDiameter != null && !grpMakeHoleOptions.Controls.Contains(lblRepairHoleDiameter))

                        grpMakeHoleOptions.Controls.Add(lblRepairHoleDiameter);

                    if (cboRepairHoleDiameter != null && !grpMakeHoleOptions.Controls.Contains(cboRepairHoleDiameter))

                        grpMakeHoleOptions.Controls.Add(cboRepairHoleDiameter);

                }

            }

        }

        private void InitializeHoleTypeControls()

        {

            holeNamePresetManager = new HoleNamePresetManager();

            try { holeNamePresets = holeNamePresetManager.Load(); }

            catch (Exception ex)

            {

                System.Diagnostics.Debug.WriteLine("[HOLE PRESET] Load failed: " + ex.Message);

                holeNamePresets = new List<string>();

            }



            lblHoleType = new Label { Name = "lblHoleType", Text = "HOLE TYPE PRESETS",

                AutoSize = true, Font = CreateUiFont(8.5F, FontStyle.Bold),

                ForeColor = Color.FromArgb(71, 85, 105) };

            cboHoleType = new ComboBox { Name = "cboHoleType", DropDownStyle = ComboBoxStyle.DropDown,

                Font = CreateUiFont(9F, FontStyle.Regular),

                BackColor = Color.White, ForeColor = Color.FromArgb(15, 23, 42),

                FlatStyle = FlatStyle.Flat };

            btnManageHoleTypes = new UI.ModernButton { Name = "btnManageHoleTypes", Text = "⋯",

                Font = CreateUiFont(10F, FontStyle.Bold), FlatStyle = FlatStyle.Flat,

                BackColor = Color.FromArgb(241, 245, 249), ForeColor = Color.FromArgb(71, 85, 105), TabStop = false };

            btnManageHoleTypes.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);

            btnChangeHoleName = new UI.ModernButton { Name = "btnChangeHoleName", Text = "CHANGE NAME HOLE",

                Font = CreateUiFont(8.5F, FontStyle.Bold), FlatStyle = FlatStyle.Flat,

                BackColor = Color.FromArgb(240, 253, 250),

                ForeColor = Color.FromArgb(13, 148, 136) };

            btnChangeHoleName.FlatAppearance.BorderColor = Color.FromArgb(153, 246, 228);

            panelModelCommands.Controls.Add(lblHoleType);

            panelModelCommands.Controls.Add(cboHoleType);

            panelModelCommands.Controls.Add(btnManageHoleTypes);

            panelModelCommands.Controls.Add(btnChangeHoleName);
            CreateMarkFeatureControls();

            RefreshHoleTypeItems(null);

            btnChangeHoleName.Click += (sender, args) =>

            {
                RenameSelectedHoles();
            };

            btnManageHoleTypes.Click += (sender, args) => ShowHoleTypeManager();

            chkMakeHolePaint.Visible = false;

            lblMakeHolePaintName.Visible = false;

            txtMakeHolePaintName.Visible = false;

        }



        private void RefreshHoleTypeItems(string selected)

        {

            cboHoleType.BeginUpdate();

            cboHoleType.Items.Clear();

            foreach (string value in holeNamePresets) cboHoleType.Items.Add(value);

            cboHoleType.EndUpdate();

            cboHoleType.Text = selected ?? "";

        }



        private void ShowHoleTypeManager()

        {

            using (var dialog = new Form())

            {

                dialog.Text = "Manage Hole Types";

                dialog.ClientSize = new Size(340, 230);

                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;

                dialog.StartPosition = FormStartPosition.CenterParent;

                dialog.MinimizeBox = false;

                dialog.MaximizeBox = false;



                var list = new ListBox { Left = 12, Top = 12, Width = 316, Height = 122 };

                var input = new TextBox { Left = 12, Top = 145, Width = 316,

                    Text = cboHoleType.Text, Font = CreateUiFont(9F) };

                var add = new Button { Text = "Add", Left = 12, Top = 183, Width = 72 };

                var edit = new Button { Text = "Edit", Left = 94, Top = 183, Width = 72 };

                var delete = new Button { Text = "Delete", Left = 176, Top = 183, Width = 72 };

                var close = new Button { Text = "Close", Left = 258, Top = 183, Width = 70,

                    DialogResult = DialogResult.Cancel };

                dialog.Controls.AddRange(new Control[] { list, input, add, edit, delete, close });

                dialog.CancelButton = close;

                list.SelectedIndexChanged += (sender, args) =>

                {

                    if (list.SelectedItem is string value) input.Text = value;

                };

                RefreshHoleTypeManagerList(list, cboHoleType.SelectedItem as string);



                add.Click += (sender, args) =>

                {

                    string value = input.Text.Trim();

                    if (TryUpdateHolePresets(() => holeNamePresetManager.Add(holeNamePresets, value), value))

                        RefreshHoleTypeManagerList(list, value);

                };

                edit.Click += (sender, args) =>

                {

                    string oldValue = list.SelectedItem as string;

                    if (oldValue == null) return;

                    string value = input.Text.Trim();

                    if (TryUpdateHolePresets(() => holeNamePresetManager.Update(holeNamePresets, oldValue, value), value))

                        RefreshHoleTypeManagerList(list, value);

                };

                delete.Click += (sender, args) =>

                {

                    string value = list.SelectedItem as string;

                    if (value == null) return;

                    if (TryUpdateHolePresets(() => holeNamePresetManager.Delete(holeNamePresets, value), null))

                    {

                        RefreshHoleTypeManagerList(list, null);

                        input.Clear();

                    }

                };

                dialog.ActiveControl = input;

                dialog.ShowDialog(this);

            }

        }



        private void RefreshHoleTypeManagerList(ListBox list, string selected)

        {

            list.BeginUpdate();

            try

            {

                list.Items.Clear();

                foreach (string value in holeNamePresets) list.Items.Add(value);

            }

            finally { list.EndUpdate(); }

            if (!string.IsNullOrWhiteSpace(selected)) list.SelectedItem = selected;

        }



        private bool TryUpdateHolePresets(Func<List<string>> action, string selected)

        {

            try

            {

                holeNamePresets = action();

                RefreshHoleTypeItems(selected);

                return true;

            }

            catch (Exception ex)

            {

                MessageBox.Show(ex.Message, "Hole Type", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                return false;

            }

        }


        private Font CreateUiFont(float size, FontStyle style = FontStyle.Regular)

        {

            return new Font(AppUiFontName, size, style, GraphicsUnit.Point, 0);

        }




        private void btnLoadBom_Click(object sender, EventArgs e)

        {
            if (bomDxfExportPane != null) bomDxfExportPane.Visible = false;


            RunDrawingBomCommand(() =>

            {

                actions?.LoadBom(IsDrawingBomCancelRequested);
                string previousDxfBomPath = loadedDxfBomPath;
                loadedDxfBomPath = actions != null && actions.LoadedBomContext != BomCommandContext.None && !IsDrawingBomCancelRequested()
                    ? (swApp.ActiveDoc as ModelDoc2)?.GetPathName() : null;
                if (!string.Equals(previousDxfBomPath, loadedDxfBomPath, StringComparison.OrdinalIgnoreCase)) ResetDxfSource();

                UpdateBomCommandButtonState();

                RefreshHostedTaskPane();

            }, false);

        }



        private void btnClearBom_Click(object sender, EventArgs e)

        {
            if (bomDxfExportPane != null) bomDxfExportPane.Visible = false;


            actions?.ClearBom(); loadedDxfBomPath = null; lastDxfBomResult = null; ResetDxfSource();

            UpdateBomCommandButtonState();

            RefreshHostedTaskPane();

        }



        private void UpdateBomCommandButtonState()

        {

            if (drawingBomCommandInProgress)

            {

                btnCheckDfTk.Enabled = false; if (btnCheckDxf != null) btnCheckDxf.Enabled = false; if (btnDxfSource != null) btnDxfSource.Enabled = false; if (btnStandaloneCurveMacro != null) btnStandaloneCurveMacro.Enabled = false; if (btnExportBomDxf != null) btnExportBomDxf.Enabled = false;

                button2.Enabled = false;

                btnOpenAssem.Enabled = false;

                btnCheckBalloon.Enabled = false;

                btnCheckUraOmote.Enabled = false;

                btnCheckKegaki.Enabled = false;

                btnCheckAll.Enabled = false;

                btnCheckRound.Enabled = false;

                btnCheckSamePart.Enabled = false;

                if (btnCheckDrawingBom != null)

                    btnCheckDrawingBom.Enabled = false;

                return;

            }



            bool hasBomRows = false;



            if (dgvModelBom != null)

            {

                foreach (DataGridViewRow row in dgvModelBom.Rows)

                {

                    if (!row.IsNewRow)

                    {

                        hasBomRows = true;

                        break;

                    }

                }

            }



            BomCommandContext context = actions == null

                ? BomCommandContext.None

                : actions.LoadedBomContext;

            bool detailBomLoaded = hasBomRows && context == BomCommandContext.Detail;

            bool unitBomLoaded = hasBomRows && context == BomCommandContext.Unit;



            // Keep every command visible, but enable only the commands that apply

            // to the selected BOM type.

            btnCheckDfTk.Enabled = detailBomLoaded;

            button2.Enabled = unitBomLoaded;

            btnOpenAssem.Enabled = unitBomLoaded;

            btnCheckBalloon.Enabled = unitBomLoaded;

            btnCheckUraOmote.Enabled = detailBomLoaded;

            btnCheckKegaki.Enabled = detailBomLoaded;

            btnCheckAll.Enabled = detailBomLoaded;

            btnCheckRound.Enabled = detailBomLoaded;

            btnCheckSamePart.Enabled = detailBomLoaded; if (btnCheckDxf != null) btnCheckDxf.Enabled = detailBomLoaded; if (btnDxfSource != null) btnDxfSource.Enabled = detailBomLoaded; if (btnStandaloneCurveMacro != null) btnStandaloneCurveMacro.Enabled = swApp != null; if (btnExportBomDxf != null) btnExportBomDxf.Enabled = detailBomLoaded;

            btnCheckDrawingBom.Enabled = hasBomRows && !drawingBomCommandInProgress;

        }



        private void InitBomCommandToolTips()

        {

            bomCommandToolTipFont = new Font(

                AppUiFontName,

                9.0f,

                FontStyle.Regular,

                GraphicsUnit.Point,

                0);

            bomCommandToolTip = new ToolTip

            {

                AutomaticDelay = 350,

                InitialDelay = 350,

                ReshowDelay = 100,

                AutoPopDelay = 7000,

                ShowAlways = true,

                IsBalloon = false,

                ToolTipIcon = ToolTipIcon.None,

                ToolTipTitle = ""

            };

            bomCommandToolTip.OwnerDraw = true;

            bomCommandToolTip.Popup += bomCommandToolTip_Popup;

            bomCommandToolTip.Draw += bomCommandToolTip_Draw;



            SetBomCommandToolTip(

                btnCheckDfTk,

                "CHECK DF/TK (Uốn & Khai triển):\n" +

                "Đối chiếu hình học giữa mô hình uốn 3D (Default) và bản trải phẳng khai triển (Tenkai/Flat-Pattern).");

            SetBomCommandToolTip(

                button2,

                "XẾP UNIT (Sắp xếp số hiệu cụm):\n" +

                "Sắp xếp số hiệu chi tiết (部品番号 / 合番) theo thứ tự tăng dần của danh mục BOM Unit.");

            SetBomCommandToolTip(

                btnOpenAssem,

                "OPEN ASSEM (Mở cụm lắp ráp 3D):\n" +

                "Mở mô hình cụm 3D Assembly tương ứng của chi tiết đang chọn.");

            SetBomCommandToolTip(

                btnCheckBalloon,

                "CHECK BALLOON (Đánh số chi tiết - Item No):\n" +

                "Quét toàn bộ Sheet/View và đối chiếu số hiệu quả bóng Balloon trên Drawing với danh mục BOM.");

            SetBomCommandToolTip(

                btnCheckDrawingBom,

                "CHECK DRAWING (Kiểm tra bản vẽ 2D):\n" +

                "Kiểm tra và đối chiếu thông tin trên các View của Drawing với dữ liệu bảng kê BOM.");

            SetBomCommandToolTip(

                btnCheckUraOmote,

                "CHECK URA/OMOTE (Mặt uốn trong/ngoài):\n" +

                "Kiểm tra vị trí và hướng mặt uốn (mặt màu hồng) giữa mô hình Default và Flat-Pattern.");

            SetBomCommandToolTip(

                btnCheckKegaki,

                "CHECK KEGAKI (Đường vạch dấu định vị):\n" +

                "Kiểm tra đường vạch dấu định vị gia công Kegaki, Bend Table chung và thiết lập từng cạnh bẻ.");

            SetBomCommandToolTip(

                btnCheckAll,

                "CHECK KEGAKI (Mặt chấn & Vạch dấu):\n" +

                "Kiểm tra kết hợp mặt uốn Ura/Omote (mặt trong/ngoài) và đường vạch dấu định vị gia công Kegaki.");

            SetBomCommandToolTip(

                btnCheckRound,

                "CHECK ROUND (Lỗ tròn & Bo cung R):\n" +

                "Kiểm tra quy cách lỗ tròn và bán kính cung R hai đầu rãnh thoát dao trên phôi trải phẳng Flat-Pattern.");

            SetBomCommandToolTip(

                btnCheckSamePart,

                "CHECK SAME PART (Chi tiết đồng dạng):\n" +

                "Tìm và gom các chi tiết có cùng biên dạng phôi khai triển Flat-Pattern; đối chiếu trạng thái uốn, vật liệu và chiều dày.");



            SetBomCommandToolTip(btnFixScale,
                "FIX TỈ LỆ VIEW:\n" +
                "Chọn sẵn View, cạnh hoặc DIM rồi bấm lệnh: tự chỉnh tỉ lệ ngay.\n" +
                "Chưa chọn: mở bảng để chọn View và chỉnh tỉ lệ; ✓ áp dụng, ✕ hủy.");

            // Tooltips cho các nút Macro trong tab Model -> Macro

            SetBomCommandToolTip(

                btnCheckAssemblyHole,

                "Macro: Kiểm tra vị trí và độ đồng tâm các lỗ trong cụm lắp ráp Assembly.");

            SetBomCommandToolTip(

                btnMirrorPart,

                "Macro: Tạo chi tiết đối xứng (Mirror Part) tự động giữ nguyên thuộc tính.");

            SetBomCommandToolTip(

                btnEdgeToEqualSpline,

                "AUTO SPLINE mở bảng SolidWorks:\n" +

                "TẠO: chọn 1 Edge để tạo spline mới.\n" +

                "DI CHUYỂN: Edit 3D Sketch, chọn spline nguồn và " +

                "cạnh spline đích của Surface/Body.");

            SetBomCommandToolTip(

                btnRepair3DSpline,

                "Macro: Sửa chữa và làm mịn đường 3D Spline.");

            SetBomCommandToolTip(

                btnVCutPropertySetup,

                "Macro: Cài đặt thuộc tính đường gập và gán rãnh bào V-Cut.");



            SetBomCommandToolTip(btnCheckDxf, "DXF ↔ DRAWING: Load BOM chi tiết, tick các dòng rồi bấm để chọn thư mục DXF. Chỉ dùng file trong thư mục đã chọn, hiện đầy đủ đường dẫn file đang kiểm tra. Tự mở Drawing silent, tắt tô màu Flat Pattern; dung sai 0,20 mm; lưu báo cáo Excel.");
            SetBomCommandToolTip(btnDxfSource, "Thư mục DXF gần nhất. Bấm DXF ↔ DRAWING để chọn thư mục trước mỗi lần kiểm tra; chỉ dùng DXF trong thư mục đã chọn.");

            SetBomCommandToolTip(btnStandaloneCurveMacro, "Mở macro rời kiểm tra Drawing hoặc Drawing + DXF, chọn điều kiện SPLINE/POLYLINE và chuyển SPLINE nguồn sang ARC/LINE trong Part. Giữ các dòng BOM đã tick; không tự lưu Part.");

            SetBomCommandToolTip(btnCheckDxfMacro, "CHECK DXF FILE: Kiểm tra hình học các file DXF trong thư mục; xuất Excel và DXF đánh dấu đỏ.");



            // Tooltips cho các nút Model Props

            SetBomCommandToolTip(btnModelApplyProps, "Apply: Lưu toàn bộ thuộc tính đang nhập vào Part/Component 3D.");

            SetBomCommandToolTip(btnModelResetProps, "Clear: Xóa trắng toàn bộ các ô nhập dữ liệu thuộc tính để nhập mới.");

            SetBomCommandToolTip(btnModelUpdateProps, "Reload: Lấy lại các thuộc tính có sẵn từ Part/Component 3D đang chọn.");



            // Disabled WinForms controls do not raise hover events. Listen on the

            // parent as well so the description remains available while buttons are dimmed.

            tabDrawingBom.MouseMove += tabDrawingBom_BomCommandToolTipMouseMove;

            tabDrawingBom.MouseLeave += tabDrawingBom_BomCommandToolTipMouseLeave;

            if (tabModelMacroPage != null)

            {

                tabModelMacroPage.MouseMove += tabModelMacroPage_BomCommandToolTipMouseMove;

                tabModelMacroPage.MouseLeave += tabModelMacroPage_BomCommandToolTipMouseLeave;

            }

        }



        private void SetBomCommandToolTip(Control control, string description)

        {

            if (control == null || bomCommandToolTip == null)

                return;



            bomCommandToolTip.SetToolTip(control, description);

        }



        private string GetBomCommandToolTipText(Control control)

        {

            if (control == button2 || control == btnOpenAssem || control == btnCheckBalloon)

            {

                return "H\u00E3y click v\u00E0o b\u1EA3ng BOM UNIT v\u00E0 b\u1EA5m C\u1EACP NH\u1EACT\n\u0111\u1EC3 th\u1EF1c hi\u1EC7n thao t\u00E1c l\u1EC7nh.";

            }

            if (control == btnCheckDfTk || control == btnCheckUraOmote ||

                control == btnCheckKegaki || control == btnCheckAll ||

                control == btnCheckRound || control == btnCheckSamePart ||

                control == btnCheckDrawingBom)

            {

                return "H\u00E3y click v\u00E0o b\u1EA3ng BOM chi ti\u1EBFt v\u00E0 b\u1EA5m C\u1EACP NH\u1EACT\n\u0111\u1EC3 th\u1EF1c hi\u1EC7n thao t\u00E1c l\u1EC7nh.";

            }



            return "";

        }



        private void bomCommandToolTip_Popup(object sender, PopupEventArgs e)

        {

            if (bomCommandToolTipFont == null)

                return;



            string text = (e.AssociatedControl == tabDrawingBom || e.AssociatedControl == tabModelMacroPage)

                ? manualBomCommandToolTipText

                : bomCommandToolTip.GetToolTip(e.AssociatedControl);

            if (string.IsNullOrWhiteSpace(text))

                return;



            Size measured = TextRenderer.MeasureText(

                text,

                bomCommandToolTipFont,

                new Size(340, 0),

                TextFormatFlags.Left | TextFormatFlags.WordBreak |

                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

            e.ToolTipSize = new Size(

                Math.Min(356, measured.Width + 18),

                measured.Height + 12);

        }



        private void bomCommandToolTip_Draw(object sender, DrawToolTipEventArgs e)

        {

            Font font = bomCommandToolTipFont ?? Font;

            using (SolidBrush background = new SolidBrush(Color.FromArgb(255, 253, 242)))

            using (Pen border = new Pen(Color.FromArgb(165, 165, 165)))

            {

                e.Graphics.FillRectangle(background, e.Bounds);

                e.Graphics.DrawRectangle(

                    border,

                    e.Bounds.X,

                    e.Bounds.Y,

                    Math.Max(0, e.Bounds.Width - 1),

                    Math.Max(0, e.Bounds.Height - 1));

            }



            Rectangle textBounds = Rectangle.Inflate(e.Bounds, -9, -6);

            TextRenderer.DrawText(

                e.Graphics,

                e.ToolTipText,

                font,

                textBounds,

                Color.FromArgb(35, 35, 35),

                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |

                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix |

                TextFormatFlags.NoPadding);

        }



        private void tabDrawingBom_BomCommandToolTipMouseMove(object sender, MouseEventArgs e)

        {

            if (bomCommandToolTip == null || tabDrawingBom == null)

                return;



            Point screenPoint = tabDrawingBom.PointToScreen(e.Location);

            Control hoveredControl = null;

            Control[] commandButtons =

            {

                btnCheckDfTk,

                button2,

                btnOpenAssem,

                btnCheckBalloon,

                btnCheckUraOmote,

                btnCheckKegaki,

                btnCheckAll,

                btnCheckRound,

                btnCheckSamePart,

                btnCheckDrawingBom,

                btnCheckDxf, btnDxfSource, btnStandaloneCurveMacro, btnExportBomDxf

            };



            foreach (Control control in commandButtons)

            {

                if (control != null && control.Visible && !control.Enabled &&

                    control.RectangleToScreen(control.ClientRectangle).Contains(screenPoint))

                {

                    hoveredControl = control;

                    break;

                }

            }



            if (hoveredControl == lastDisabledBomToolTipControl)

                return;



            bomCommandToolTip.Hide(tabDrawingBom);

            lastDisabledBomToolTipControl = hoveredControl;



            string description = GetBomCommandToolTipText(hoveredControl);

            if (string.IsNullOrEmpty(description))

                return;



            manualBomCommandToolTipText = description;

            Rectangle buttonBounds = hoveredControl.RectangleToScreen(hoveredControl.ClientRectangle);

            Point showPoint = tabDrawingBom.PointToClient(new Point(buttonBounds.Left, buttonBounds.Bottom + 2));

            bomCommandToolTip.Show(description, tabDrawingBom, showPoint.X, showPoint.Y, 12000);

        }



        private void tabDrawingBom_BomCommandToolTipMouseLeave(object sender, EventArgs e)

        {

            lastDisabledBomToolTipControl = null;

            manualBomCommandToolTipText = "";

            if (bomCommandToolTip != null)

                bomCommandToolTip.Hide(tabDrawingBom);

        }



        private void tabModelMacroPage_BomCommandToolTipMouseMove(object sender, MouseEventArgs e)

        {

            if (bomCommandToolTip == null || tabModelMacroPage == null)

                return;



            Point screenPoint = tabModelMacroPage.PointToScreen(e.Location);

            Control hoveredControl = null;

            Control[] macroButtons =

            {

                btnCheckAssemblyHole,

                btnMirrorPart,

                btnEdgeToEqualSpline,

                btnRepair3DSpline,

                btnVCutPropertySetup,

                btnCheckDxfMacro,
                btnAutoFrameJoint

            };



            foreach (Control control in macroButtons)

            {

                if (control != null && control.Visible && !control.Enabled &&

                    control.RectangleToScreen(control.ClientRectangle).Contains(screenPoint))

                {

                    hoveredControl = control;

                    break;

                }

            }



            if (hoveredControl == lastDisabledBomToolTipControl)

                return;



            bomCommandToolTip.Hide(tabModelMacroPage);

            lastDisabledBomToolTipControl = hoveredControl;



            if (hoveredControl == null)

                return;



            string description = bomCommandToolTip.GetToolTip(hoveredControl);

            if (string.IsNullOrEmpty(description))

                return;



            manualBomCommandToolTipText = description;

            Rectangle buttonBounds = hoveredControl.RectangleToScreen(hoveredControl.ClientRectangle);

            Point showPoint = tabModelMacroPage.PointToClient(new Point(buttonBounds.Left, buttonBounds.Bottom + 2));

            bomCommandToolTip.Show(description, tabModelMacroPage, showPoint.X, showPoint.Y, 12000);

        }



        private void tabModelMacroPage_BomCommandToolTipMouseLeave(object sender, EventArgs e)

        {

            lastDisabledBomToolTipControl = null;

            manualBomCommandToolTipText = "";

            if (bomCommandToolTip != null)

                bomCommandToolTip.Hide(tabModelMacroPage);

        }



        private void btnCheckDfTk_Click(object sender, EventArgs e)

        {

            RunDrawingBomCommand(() => actions?.CheckDfTk());

        }



        private void btnCheckUraOmote_Click(object sender, EventArgs e)

        {

            RunDrawingBomCommand(() => actions?.CheckUraOmote());

        }



        private void btnCheckKegaki_Click(object sender, EventArgs e)

        {

            RunDrawingBomCommand(() => actions?.CheckKegaki());

        }



        private void btnCheckAll_Click(object sender, EventArgs e)

        {

            CombinedCheckOptions options = null;

            using (CheckAllSelectionDialog dialog = new CheckAllSelectionDialog())

            {

                if (dialog.ShowDialog(this) != DialogResult.OK)

                    return;



                options = dialog.Options;

            }



            if (options == null || !options.HasSelection)

                return;



            RunDrawingBomCommand(() => actions?.CheckAll(options));

        }



        private void btnCheckRound_Click(object sender, EventArgs e)

        {

            RunDrawingBomCommand(() => actions?.CheckRound());

        }



        private void btnCheckSamePart_Click(object sender, EventArgs e)

        {

            SamePartToleranceOptions options = null;

            using (SamePartToleranceInputDialog dialog = new SamePartToleranceInputDialog())

            {

                if (dialog.ShowDialog(this) != DialogResult.OK)

                    return;



                options = dialog.Options;

            }



            if (options == null)

                return;



            RunDrawingBomCommand(() => actions?.CheckSamePart(options));

        }



        private void btnXepUnit_Click(object sender, EventArgs e)

        {

            RunDrawingBomCommand(() =>

                xepUnitDrawing?.Run(dgvModelBom, BeginProgress, UpdateProgress, FinishProgress,

                    IsDrawingBomCancelRequested));

        }



        private void btnCheckBalloon_Click(object sender, EventArgs e)

        {

            RunDrawingBomCommand(() =>

            {

                List<string> drawingPaths = xepUnitDrawing == null

                    ? new List<string>()

                    : xepUnitDrawing.GetCheckedAssemblyDrawingPaths(dgvModelBom);

                if (drawingPaths.Count == 0)

                {

                    MessageBox.Show("Hay tick it nhat mot UNIT co Drawing truoc.",

                        "Balloon", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    return;

                }



                BalloonCheckResult result = balloonChecker == null ? null : balloonChecker.RunBatch(

                    drawingPaths, BeginProgress, UpdateProgress, FinishProgress,

                    IsDrawingBomCancelRequested);

                if (result == null)

                    return;

                if (IsDrawingBomCancelRequested())

                {

                    lblStatus.Text = "Đã dừng CHECK BALLOON.";

                    return;

                }



                lblStatus.Text = result.IsOk

                    ? "CHECK BALLOON: OK - " + result.ValidCount + "/" + result.ExpectedCount

                    : "CHECK BALLOON: thieu " + result.MissingCount + ", trung " + result.DuplicateCount

                        + ", sai so " + result.WrongTextCount + ", dangling " + result.DanglingCount;

                result.ExportToExcel();

            });

        }

        private void btnOpenAssem_Click(object sender, EventArgs e)

        {

            RunDrawingBomCommand(() =>

                xepUnitDrawing?.OpenCheckedAssemblyDrawings(

                    dgvModelBom,

                    BeginProgress,

                    UpdateProgress,

                    FinishProgress,

                    IsDrawingBomCancelRequested));

        }



        private void btnMakeHole_Click(object sender, EventArgs e)
        {
            ShowNativeMakeHolePage();
        }



        private void btnRepairHole_Click(object sender, EventArgs e)

        {
            ShowNativeRepairHolePage();
        }


        private void chkMakeHolePaint_CheckedChanged(object sender, EventArgs e)

        {

            UpdateMakeHolePaintNameState();

        }



        private void UpdateMakeHolePaintNameState()

        {

            if (txtMakeHolePaintName == null || lblMakeHolePaintName == null || chkMakeHolePaint == null)

                return;



            bool enabled = !repairHolePanelMode;

            txtMakeHolePaintName.Enabled = enabled;

            lblMakeHolePaintName.Enabled = enabled;

        }

        private void btnPaintHoleSummary_Click(object sender, EventArgs e)

        {
            ShowNativeCountHolePage();
        }


        private void btnDimMatCat_Click(object sender, EventArgs e)

        {
            RunSimpleDrawingDimensions(NativeToolKind.SectionDimensions);
        }


        private void btnSplineToArcs_Click(object sender, EventArgs e)

        {
            RunSplineArcs();
        }


        private void btnMakeHoleAccept_Click(object sender, EventArgs e)

        {
            if (nativeMakeHolePage != null) return;


            MakeHoleOptions options;

            if (!TryGetMakeHoleOptions(out options))

                return;



            SaveMakeHoleSizeHistory(cboRepairHoleDiameter.Text);



            if (repairHolePanelMode)

                makeHoleCommand?.RunRepairHole(options);

            else

                makeHoleCommand?.Run(options);



            UpdateMakeHolePatternButton();

        }



        private void btnMakeHolePattern_Click(object sender, EventArgs e)

        {
            if (nativeMakeHolePage != null) { nativeMakeHolePage.RequestPattern(); return; }


            if (makeHoleCommand == null)

                return;



            makeHoleCommand.PatternPendingHoleWizard();
            if (!makeHoleCommand.HasPendingHybridPattern) ClearPendingMakeHoleContext();

            UpdateMakeHolePatternButton();

        }



        private void btnMakeHoleReset_Click(object sender, EventArgs e)

        {
            if (nativeMakeHolePage != null) { nativeMakeHolePage.RequestReset(); return; }


            ResetMakeHolePendingWork();

        }



        private void btnMakeHoleUpdate_Click(object sender, EventArgs e)

        {
            if (nativeMakeHolePage != null) { nativeMakeHolePage.RequestUpdate(); return; }


            if (makeHoleCommand == null)

            {

                makeHoleCommand = new LenhMakeHole(swApp);

                BindRepairHoleViewModelToMakeHoleCommand();

            }



            double pitch;

            bool ok;



            if (TryParsePositiveMillimeter(txtMakeHolePitch.Text, out pitch))

                ok = makeHoleCommand.UpdateTrackedMakeHolePattern(pitch);

            else

                ok = makeHoleCommand.UpdateTrackedMakeHolePattern();



            SetMakeHoleUpdateButtonState(!ok && makeHoleCommand.IsMakeHoleUpdateRequired());

        }



        private void UpdateMakeHolePatternButton()

        {

            if (btnMakeHolePattern == null)

                return;



            btnMakeHolePattern.Visible = !repairHolePanelMode && HasTaskPaneMakeHolePattern;
            if (makeHoleActionsOnly) LayoutMakeHoleActions();

        }



        private void InitMakeHoleOptions()

        {

            cboMakeHoleDirection.Items.Clear();

            EnsureComboItem(cboMakeHoleDirection, "Curve Flow");

            EnsureComboItem(cboMakeHoleDirection, "Line Flow");

            EnsureComboItem(cboMakeHoleDirection, "Spline Flow");

            SelectComboItem(cboMakeHoleDirection, "Curve Flow");



            txtMakeHoleEdgeOffset.Text = "20";

            txtMakeHoleLeftOffset.Text = "50";

            txtMakeHoleRightOffset.Text = "50";

            txtMakeHolePitch.Text = "300";

            chkMakeHolePaint.Checked = false;

            InitializeMakeHoleSizeOptions();

            SelectComboItem(cboRepairHoleDiameter, "4.2");



            repairHoleViewModel = new ADDIN.UI.RepairHoleViewModel();

            BindRepairHoleViewModelToMakeHoleCommand();

            repairHoleViewModel.TableVisibilityChanged += (visible) =>

            {

                if (repairHolePanelMode)

                {

                    LayoutMakeHoleOptions();

                }

            };

            if (repairHoleTaskPaneView == null && elementHostRepairHole != null)

            {

                repairHoleTaskPaneView = new ADDIN.UI.RepairHoleTaskPaneView();

                elementHostRepairHole.Child = repairHoleTaskPaneView;

            }

            if (repairHoleTaskPaneView != null)

            {

                repairHoleTaskPaneView.DataContext = repairHoleViewModel;

            }



            // Keep Update/Reset available outside the PropertyManager from startup.
            ShowMakeHoleActions();

        }



        private void BindRepairHoleViewModelToMakeHoleCommand()

        {

            if (repairHoleViewModel == null)

                return;



            repairHoleViewModel.Initialize(

                makeHoleCommand,

                GetRepairHoleOptionsForTaskPane);

        }



        private MakeHoleOptions GetRepairHoleOptionsForTaskPane()

        {

            double thickness = 0.0;

            if (!string.IsNullOrWhiteSpace(txtModelThickness?.Text))

            {

                TryParseFirstPositiveMillimeter(

                    txtModelThickness.Text,

                    out thickness);

            }



            return new MakeHoleOptions

            {

                ThicknessMm = thickness

            };

        }



        private void SetMakeHolePanelMode(bool repairMode)

        {
            if (!repairMode) { ShowMakeHoleActions(); return; }
            makeHoleActionsOnly = false;

            repairHolePanelMode = repairMode;



            grpMakeHoleOptions.Visible = true;

            grpMakeHoleOptions.BringToFront();

            grpMakeHoleOptions.Text = repairMode ? "Repair Hole" : "THÔNG SỐ LỖ (MAKE HOLE OPTIONS)";



            if (btnMakeHole != null)

            {

                btnMakeHole.NormalColor = repairMode ? Color.White : Color.FromArgb(239, 246, 255);

                btnMakeHole.BorderColor = repairMode ? Color.FromArgb(203, 213, 225) : Color.FromArgb(37, 99, 235);

                btnMakeHole.Invalidate();

            }

            if (btnRepairHole != null)

            {

                btnRepairHole.NormalColor = repairMode ? Color.FromArgb(254, 243, 199) : Color.White;

                btnRepairHole.BorderColor = repairMode ? Color.FromArgb(217, 119, 6) : Color.FromArgb(203, 213, 225);

                btnRepairHole.Invalidate();

            }



            bool makeMode = !repairMode;

            if (pnlMakeHoleDiagram != null) pnlMakeHoleDiagram.Visible = makeMode;

            if (lblRepairHoleDiameter != null) lblRepairHoleDiameter.Visible = false;

            if (cboRepairHoleDiameter != null) cboRepairHoleDiameter.Visible = false;

            if (lblRepairHoleType != null) lblRepairHoleType.Visible = false;

            if (cboRepairHoleType != null) cboRepairHoleType.Visible = false;

            if (btnDeleteMakeHoleSize != null) btnDeleteMakeHoleSize.Visible = false;



            if (lblMakeHoleDirection != null) lblMakeHoleDirection.Visible = makeMode;

            if (cboMakeHoleDirection != null) cboMakeHoleDirection.Visible = makeMode;

            if (lblMakeHoleEdgeOffset != null) lblMakeHoleEdgeOffset.Visible = makeMode;

            if (txtMakeHoleEdgeOffset != null) txtMakeHoleEdgeOffset.Visible = makeMode;

            if (lblMakeHoleLeftOffset != null) lblMakeHoleLeftOffset.Visible = makeMode;

            if (txtMakeHoleLeftOffset != null) txtMakeHoleLeftOffset.Visible = makeMode;

            if (lblMakeHoleRightOffset != null) lblMakeHoleRightOffset.Visible = makeMode;

            if (txtMakeHoleRightOffset != null) txtMakeHoleRightOffset.Visible = makeMode;

            if (lblMakeHolePitch != null) lblMakeHolePitch.Visible = makeMode;

            if (txtMakeHolePitch != null) txtMakeHolePitch.Visible = makeMode;

            if (chkMakeHolePaint != null) chkMakeHolePaint.Visible = false;

            if (lblMakeHolePaintName != null) lblMakeHolePaintName.Visible = false;

            if (txtMakeHolePaintName != null) txtMakeHolePaintName.Visible = false;

            if (btnMakeHoleReset != null) btnMakeHoleReset.Visible = makeMode;

            if (btnMakeHoleUpdate != null) btnMakeHoleUpdate.Visible = makeMode;

            if (btnMakeHoleAccept != null) btnMakeHoleAccept.Visible = makeMode;

            if (btnMakeHolePattern != null) btnMakeHolePattern.Visible = makeMode && makeHoleCommand != null && makeHoleCommand.HasPendingHybridPattern;



            if (elementHostRepairHole != null)

            {

                elementHostRepairHole.Visible = repairMode;

                if (repairMode)

                {

                    elementHostRepairHole.BringToFront();

                    repairHoleViewModel?.ResetPanel();

                }

            }



            LayoutMakeHoleOptions();

            UpdateMakeHolePaintNameState();

            UpdateMakeHolePatternButton();

        }



        private void MakeHoleTrackedInputChanged(object sender, EventArgs e)

        {
            if (nativeMakeHolePage != null || (makeHoleCommand != null && makeHoleCommand.HasPendingHybridPattern)) return;

            SetMakeHoleUpdateButtonState(IsCurrentMakeHoleUpdateRequired());

        }



        private void LayoutMakeHoleOptions()

        {
            if (makeHoleActionsOnly) { LayoutMakeHoleActions(); return; }

            if (grpMakeHoleOptions == null || panelModelCommands == null)

                return;



            int groupLeft = 6;

            int groupRightMargin = 6;

            int availableWidth = Math.Max(240, panelModelCommands.ClientSize.Width - groupLeft - groupRightMargin);

            bool compact = availableWidth < 350;

            int groupWidth = availableWidth;

            int inputWidth = compact ? Math.Max(120, groupWidth - 112) : 118;

            int previewWidth = Math.Max(170, groupWidth - 20);



            grpMakeHoleOptions.Left = groupLeft;

            grpMakeHoleOptions.Width = groupWidth;

            pnlMakeHoleDiagram.Left = (groupWidth - previewWidth) / 2;

            pnlMakeHoleDiagram.Width = previewWidth;

            pnlMakeHoleDiagram.Height = 88;



            if (repairHolePanelMode)

            {

                int hostLeft = 4;

                int hostTop = 18;

                int hostWidth = Math.Max(220, groupWidth - hostLeft * 2);



                bool tableVisible = repairHoleViewModel != null && repairHoleViewModel.IsTableVisible;

                int hostHeight = tableVisible ? 365 : 72;



                if (elementHostRepairHole != null)

                {

                    elementHostRepairHole.SetBounds(hostLeft, hostTop, hostWidth, hostHeight);

                }

                grpMakeHoleOptions.Height = hostTop + hostHeight + 12;

                return;

            }



            if (compact)

            {

                int innerLeft = 16;

                int innerWidth = Math.Max(170, groupWidth - 32);

                int labelY = 144;

                int controlY = labelY + 17;

                int rowGap = 42;



                LayoutStackedField(lblMakeHoleDirection, cboMakeHoleDirection, innerLeft, labelY, innerWidth);

                labelY += rowGap;

                LayoutStackedField(lblMakeHoleEdgeOffset, txtMakeHoleEdgeOffset, innerLeft, labelY, innerWidth);

                labelY += rowGap;

                LayoutStackedField(lblMakeHoleLeftOffset, txtMakeHoleLeftOffset, innerLeft, labelY, innerWidth);

                labelY += rowGap;

                LayoutStackedField(lblMakeHoleRightOffset, txtMakeHoleRightOffset, innerLeft, labelY, innerWidth);

                labelY += rowGap;

                LayoutStackedField(lblMakeHolePitch, txtMakeHolePitch, innerLeft, labelY, innerWidth);

                labelY += rowGap;



                int buttonTop = labelY + 8;

                int buttonGap = 8;

                int buttonWidth = Math.Max(78, (innerWidth - buttonGap) / 2);

                btnMakeHoleAccept.SetBounds(innerLeft, buttonTop, buttonWidth, 32);

                btnMakeHoleUpdate.SetBounds(innerLeft + buttonWidth + buttonGap, buttonTop, buttonWidth, 32);

                btnMakeHoleReset.SetBounds(innerLeft, buttonTop + 40, buttonWidth, 32);

                btnMakeHolePattern.SetBounds(innerLeft + buttonWidth + buttonGap, buttonTop + 40, buttonWidth, 32);

                grpMakeHoleOptions.Height = buttonTop + 88;

            }

            else

            {

                int innerLeft = 16;

                int innerWidth = groupWidth - 32;

                int columnGap = 16;

                int columnWidth = (innerWidth - columnGap) / 2;

                int labelWidth = 58;

                int leftLabelX = innerLeft;

                int leftInputX = leftLabelX + labelWidth;

                int rightLabelX = innerLeft + columnWidth + columnGap;

                int rightInputX = rightLabelX + labelWidth;

                int fieldWidth = Math.Max(72, columnWidth - labelWidth);



                lblMakeHoleDirection.Location = new Point(leftLabelX, 148);

                cboMakeHoleDirection.Location = new Point(leftInputX, 145);

                cboMakeHoleDirection.Width = fieldWidth;



                lblMakeHoleEdgeOffset.Location = new Point(rightLabelX, 148);

                txtMakeHoleEdgeOffset.Location = new Point(rightInputX, 145);

                txtMakeHoleEdgeOffset.Width = fieldWidth;



                lblMakeHoleLeftOffset.Location = new Point(leftLabelX, 180);

                txtMakeHoleLeftOffset.Location = new Point(leftInputX, 177);

                txtMakeHoleLeftOffset.Width = fieldWidth;



                lblMakeHoleRightOffset.Location = new Point(rightLabelX, 180);

                txtMakeHoleRightOffset.Location = new Point(rightInputX, 177);

                txtMakeHoleRightOffset.Width = fieldWidth;



                lblMakeHolePitch.Location = new Point(leftLabelX, 212);

                txtMakeHolePitch.Location = new Point(leftInputX, 209);

                txtMakeHolePitch.Width = fieldWidth;



                int buttonGap = 8;

                var visibleButtons = new List<Button>();

                visibleButtons.Add(btnMakeHoleReset);

                if (btnMakeHolePattern != null && btnMakeHolePattern.Visible) visibleButtons.Add(btnMakeHolePattern);

                visibleButtons.Add(btnMakeHoleAccept);

                visibleButtons.Add(btnMakeHoleUpdate);

                int bCount = visibleButtons.Count;

                int bWidth = (innerWidth - buttonGap * (bCount - 1)) / bCount;

                int buttonTop = 248;

                for (int i = 0; i < bCount; i++)

                {

                    visibleButtons[i].SetBounds(innerLeft + i * (bWidth + buttonGap), buttonTop, bWidth, 32);

                }

                grpMakeHoleOptions.Height = 302;

            }



            pnlMakeHoleDiagram.Invalidate();

        }



        private void LayoutStackedField(Label label, Control input, int left, int labelTop, int width)

        {

            if (label != null)

            {

                label.Location = new Point(left, labelTop);

                label.AutoSize = true;

            }



            if (input != null)

            {

                input.Location = new Point(left, labelTop + 17);

                input.Width = width;

            }

        }



        private void LayoutModelCommandButtons()

        {

            LayoutModelEditPage();

        }



        private void LayoutModelMacroButtons()

        {

            LayoutModelMacroCommands();

        }



        private void InitializeMakeHoleSizeOptions()

        {

            if (cboRepairHoleDiameter == null)

                return;



            EnsureComboItem(cboRepairHoleDiameter, "3");

            EnsureComboItem(cboRepairHoleDiameter, "3.2");

            EnsureComboItem(cboRepairHoleDiameter, "4.2");

            EnsureComboItem(cboRepairHoleDiameter, "5");

            EnsureComboItem(cboRepairHoleDiameter, "6");

            EnsureComboItem(cboRepairHoleDiameter, "8");

            EnsureComboItem(cboRepairHoleDiameter, "10");

            EnsureComboItem(cboRepairHoleDiameter, "12");

            EnsureComboItem(cboRepairHoleDiameter, "4.2x25");

            EnsureComboItem(cboRepairHoleDiameter, "10x16");



            foreach (string item in ReadMakeHoleSizeHistory())

                EnsureComboItem(cboRepairHoleDiameter, item);

        }



        private void MakeHoleSizeHistory_Leave(object sender, EventArgs e)

        {

            SaveMakeHoleSizeHistory(cboRepairHoleDiameter.Text);

        }



        private bool TryParseMakeHoleSize(string text, bool repairMode, out double diameter, out string looseSize, out bool slotHole)

        {

            diameter = 0.0;

            looseSize = "None";

            slotHole = false;

            text = NormalizeMakeHoleSizeText(text);

            if (string.IsNullOrWhiteSpace(text))

                return false;



            if (text.IndexOf("x", StringComparison.OrdinalIgnoreCase) >= 0)

            {

                string[] parts = text.ToLowerInvariant().Split(new[] { 'x' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length != 2)

                    return false;



                double a;

                double b;

                if (!TryParsePositiveMillimeter(parts[0], out a) || !TryParsePositiveMillimeter(parts[1], out b))

                    return false;



                double width = Math.Min(a, b);

                double length = Math.Max(a, b);

                if (length <= width)

                    return false;



                diameter = width;

                looseSize = FormatMillimeterText(width) + "x" + FormatMillimeterText(length);

                slotHole = true;

                return true;

            }



            return TryParsePositiveMillimeter(text, out diameter);

        }



        private string NormalizeMakeHoleSizeText(string text)

        {

            text = (text ?? "").Trim();

            if (text.EndsWith("mm", StringComparison.OrdinalIgnoreCase))

                text = text.Substring(0, text.Length - 2);



            return text

                .Replace(" ", "")

                .Replace("*", "x")

                .Replace("X", "x")

                .Replace(",", ".");

        }



        private string FormatMillimeterText(double value)

        {

            return value.ToString("0.###", CultureInfo.InvariantCulture);

        }



        private string GetMakeHoleSizeHistoryPath()

        {

            string folder = Path.Combine(

                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),

                "TAI_TOOL");

            return Path.Combine(folder, MakeHoleSizeHistoryFileName);

        }



        private List<string> ReadMakeHoleSizeHistory()

        {

            string path = GetMakeHoleSizeHistoryPath();

            List<string> result = new List<string>();

            try

            {

                if (!File.Exists(path))

                    return result;



                foreach (string line in File.ReadAllLines(path))

                {

                    string item = NormalizeMakeHoleSizeText(line);

                    if (!string.IsNullOrWhiteSpace(item) && !ContainsText(result, item))

                        result.Add(item);

                }

            }

            catch

            {

            }



            return result;

        }



        private void SaveMakeHoleSizeHistory(string text)

        {

            string item = NormalizeMakeHoleSizeText(text);

            if (string.IsNullOrWhiteSpace(item))

                return;



            bool slotHole;

            double diameter;

            string looseSize;

            if (!TryParseMakeHoleSize(item, false, out diameter, out looseSize, out slotHole))

                return;



            item = slotHole ? looseSize : FormatMillimeterText(diameter);

            List<string> items = ReadMakeHoleSizeHistory();

            items.RemoveAll(value => string.Equals(value, item, StringComparison.OrdinalIgnoreCase));

            items.Insert(0, item);

            while (items.Count > 20)

                items.RemoveAt(items.Count - 1);



            try

            {

                string path = GetMakeHoleSizeHistoryPath();

                Directory.CreateDirectory(Path.GetDirectoryName(path));

                File.WriteAllLines(path, items.ToArray());

            }

            catch

            {

            }



            EnsureComboItem(cboRepairHoleDiameter, item);

            cboRepairHoleDiameter.Text = item;

        }



        private bool ContainsText(List<string> items, string text)

        {

            if (items == null)

                return false;



            foreach (string item in items)

            {

                if (string.Equals(item, text, StringComparison.OrdinalIgnoreCase))

                    return true;

            }



            return false;

        }



        private bool ComboContainsText(ComboBox combo, string text)

        {

            if (combo == null)

                return false;



            foreach (object item in combo.Items)

            {

                if (string.Equals(item?.ToString(), text, StringComparison.OrdinalIgnoreCase))

                    return true;

            }



            return false;

        }



        private void EnsureComboItem(ComboBox combo, string text)

        {

            if (combo == null || string.IsNullOrWhiteSpace(text))

                return;



            if (!ComboContainsText(combo, text))

                combo.Items.Add(text);

        }



        private void SelectComboItem(ComboBox combo, string value)

        {

            if (combo == null)

                return;



            int index = combo.Items.IndexOf(value);

            combo.SelectedIndex = index >= 0 ? index : (combo.Items.Count > 0 ? 0 : -1);

        }



        private bool TryGetMakeHoleOptions(out MakeHoleOptions options)

        {

            options = null;



            double diameter = 4.2;

            double edgeOffset = 0.0;

            double leftOffset = 0.0;

            double rightOffset = 0.0;

            double pitch = 0.0;

            double thickness = 0.0;



            string holeSizeText = NormalizeMakeHoleSizeText(cboRepairHoleDiameter.Text);

            bool slotHole = false;

            string looseSize = "None";

            if (!TryParseMakeHoleSize(holeSizeText, repairHolePanelMode, out diameter, out looseSize, out slotHole))

            {

                string message = "Hole Size phai la so lon hon 0 hoac dang AxB, vi du 4.2x25.";

                MessageBox.Show(message, repairHolePanelMode ? "Repair Hole" : "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return false;

            }



            TryParsePositiveMillimeter(txtModelThickness.Text, out thickness);

            if (thickness <= 0.0)

                TryParseFirstPositiveMillimeter(txtModelThickness.Text, out thickness);



            if (repairHolePanelMode && thickness <= 0.0)

            {

                MessageBox.Show("Chua doc duoc be day vat lieu (板厚). Hay nhap/cap nhat gia tri 板厚 truoc khi Repair Hole.", "Repair Hole", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return false;

            }



            if (!repairHolePanelMode && (!TryParsePositiveMillimeter(txtMakeHoleEdgeOffset.Text, out edgeOffset) ||

                !TryParsePositiveMillimeter(txtMakeHoleLeftOffset.Text, out leftOffset) ||

                !TryParsePositiveMillimeter(txtMakeHoleRightOffset.Text, out rightOffset) ||

                !TryParsePositiveMillimeter(txtMakeHolePitch.Text, out pitch)))

            {

                MessageBox.Show("Thong so Make Hole phai la so lon hon 0.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return false;

            }



            if (repairHolePanelMode)

            {

                edgeOffset = 0.0;

                leftOffset = 0.0;

                rightOffset = 0.0;

                pitch = 0.0;

            }



            options = new MakeHoleOptions

            {

                HoleType = slotHole ? "Loose" : "Circle",

                LooseType = slotHole ? looseSize : "None",

                Direction = NormalizeMakeHoleDirection(cboMakeHoleDirection.Text),

                DiameterMm = diameter,

                EdgeOffsetMm = edgeOffset,

                LeftOffsetMm = leftOffset,

                RightOffsetMm = rightOffset,

                PitchMm = pitch,

                Material = "",

                ThicknessMm = thickness,

                FaceColor = "",

                SigmaType = "",

                Paint = false,

                HoleSizeText = holeSizeText,

                PaintNameText = "",

                HoleLabel = string.IsNullOrWhiteSpace(cboHoleType.Text) ? null : cboHoleType.Text.Trim()

            };



            return true;

        }



        private bool TryParsePositiveMillimeter(string text, out double value)

        {

            text = (text ?? "").Trim().Replace(",", ".");

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;

        }



        private bool TryParseFirstPositiveMillimeter(string text, out double value)

        {

            value = 0.0;

            text = (text ?? "").Trim().Replace(",", ".");

            string numberText = "";

            bool started = false;



            foreach (char c in text)

            {

                if (char.IsDigit(c) || c == '.')

                {

                    numberText += c;

                    started = true;

                    continue;

                }



                if (started)

                    break;

            }



            return double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;

        }



        private bool TryGetCurrentMakeHolePitch(bool showMessage, out double pitchMm)

        {

            if (TryParsePositiveMillimeter(txtMakeHolePitch.Text, out pitchMm))

                return true;



            if (showMessage)

                MessageBox.Show("Pitch @ phai la so lon hon 0.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Information);



            return false;

        }



        private string NormalizeMakeHoleDirection(string text)

        {

            string value = (text ?? "").Trim();



            if (string.Equals(value, "Line Edge", StringComparison.OrdinalIgnoreCase))

                return "Line Flow";



            if (string.Equals(value, "Line Flow", StringComparison.OrdinalIgnoreCase))

                return "Line Flow";



            if (string.Equals(value, "Spline Flow", StringComparison.OrdinalIgnoreCase))

                return "Spline Flow";



            return "Curve Flow";

        }



        private bool IsCurrentMakeHoleUpdateRequired()

        {

            if (makeHoleCommand == null)

                return false;



            double pitchMm;

            if (!TryGetCurrentMakeHolePitch(false, out pitchMm))

                return false;



            return makeHoleCommand.IsMakeHoleUpdateRequired(pitchMm);

        }



        private void pnlMakeHoleDiagram_Paint(object sender, PaintEventArgs e)

        {

            Graphics g = e.Graphics;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;



            int w = pnlMakeHoleDiagram.ClientSize.Width;

            int h = pnlMakeHoleDiagram.ClientSize.Height;

            if (w < 80 || h < 40)

                return;



            // 1. Nền container bo góc mềm mại (chuẩn Tailwind bg-slate-50 border-slate-200 như HTML preview)

            Rectangle containerRect = new Rectangle(0, 0, w - 1, h - 1);

            using (var containerPath = ADDIN.UI.UIHelper.GetRoundPath(containerRect, 6))

            {

                using (var bgBrush = new SolidBrush(Color.FromArgb(248, 250, 252)))

                    g.FillPath(bgBrush, containerPath);

                using (var borderPen = new Pen(Color.FromArgb(226, 232, 240), 1.0f))

                    g.DrawPath(borderPen, containerPath);

            }



            // 2. Tỷ lệ theo viewBox 320x80 chuẩn HTML preview

            float scale = Math.Min((w - 16f) / 320f, (h - 8f) / 80f);

            float ox = (w - 320f * scale) / 2f;

            float oy = (h - 80f * scale) / 2f;



            Func<float, float> sx = x => ox + x * scale;

            Func<float, float> sy = y => oy + y * scale;



            // 3. Tấm kim loại phôi: <rect x="25" y="15" width="270" height="28" rx="2" fill="#e2e8f0" stroke="#475569" stroke-width="1.5" />

            int px = (int)Math.Round(sx(25f));

            int py = (int)Math.Round(sy(15f));

            int pw = (int)Math.Round(270f * scale);

            int ph = (int)Math.Round(28f * scale);

            Rectangle plateRect = new Rectangle(px, py, pw, ph);



            using (var plateBrush = new SolidBrush(Color.FromArgb(226, 232, 240)))

            using (var platePen = new Pen(Color.FromArgb(71, 85, 105), 1.5f))

            using (var platePath = ADDIN.UI.UIHelper.GetRoundPath(plateRect, Math.Max(2, (int)Math.Round(2f * scale))))

            {

                g.FillPath(plateBrush, platePath);

                g.DrawPath(platePen, platePath);

            }



            // 4. Bốn lỗ tròn: cx = 65, 128, 191, 255, cy = 29, r = 4.5

            // fill="#3b82f6" stroke="#1d4ed8" stroke-width="1"

            float r = Math.Max(3f, 4.5f * scale);

            float holeY = sy(29f);

            float[] holes = { 65f, 128f, 191f, 255f };



            using (var holeBrush = new SolidBrush(Color.FromArgb(59, 130, 246)))

            using (var holePen = new Pen(Color.FromArgb(29, 78, 216), 1.0f))

            {

                foreach (float hx in holes)

                {

                    float cx = sx(hx);

                    g.FillEllipse(holeBrush, cx - r, holeY - r, r * 2f, r * 2f);

                    g.DrawEllipse(holePen, cx - r, holeY - r, r * 2f, r * 2f);

                }

            }



            // 5. Đường kích thước stroke="#64748b" stroke-width="1.2"

            float fontSize = Math.Max(7.5f, 8.5f * scale);

            using (var dimPen = new Pen(Color.FromArgb(100, 116, 139), 1.2f))

            using (var textFont = new Font("Segoe UI", fontSize, FontStyle.Bold))

            using (var textBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))

            {

                // Kích thước L:

                g.DrawLine(dimPen, sx(25f), sy(50f), sx(25f), sy(58f));

                g.DrawLine(dimPen, sx(65f), sy(50f), sx(65f), sy(58f));

                g.DrawLine(dimPen, sx(25f), sy(54f), sx(65f), sy(54f));

                DrawCenteredText(g, "L", textFont, textBrush, sx(45f), sy(65f));



                // Kích thước R:

                g.DrawLine(dimPen, sx(255f), sy(50f), sx(255f), sy(58f));

                g.DrawLine(dimPen, sx(295f), sy(50f), sx(295f), sy(58f));

                g.DrawLine(dimPen, sx(255f), sy(54f), sx(295f), sy(54f));

                DrawCenteredText(g, "R", textFont, textBrush, sx(275f), sy(65f));



                // Kích thước Pitch (C):

                g.DrawLine(dimPen, sx(65f), sy(65f), sx(65f), sy(73f));

                g.DrawLine(dimPen, sx(255f), sy(65f), sx(255f), sy(73f));

                g.DrawLine(dimPen, sx(65f), sy(69f), sx(255f), sy(69f));

                DrawCenteredText(g, "Pitch (C)", textFont, textBrush, sx(160f), sy(77f));



                // Kích thước X:

                g.DrawLine(dimPen, sx(12f), sy(15f), sx(20f), sy(15f));

                g.DrawLine(dimPen, sx(12f), sy(29f), sx(20f), sy(29f));

                g.DrawLine(dimPen, sx(16f), sy(15f), sx(16f), sy(29f));

                DrawCenteredText(g, "X", textFont, textBrush, sx(7f), sy(22f));

            }

        }



        private static void DrawCenteredText(Graphics g, string text, Font font, Brush brush, float cx, float cy)

        {

            SizeF size = g.MeasureString(text, font);

            g.DrawString(text, font, brush, cx - size.Width / 2f, cy - size.Height / 2f);

        }



        private void btnModelApplyProps_Click(object sender, EventArgs e)

        {

            SaveModelPropsToSelectedComponent();

        }



        private void btnModelUpdateProps_Click(object sender, EventArgs e)

        {

            LoadModelPropsFromSelectedComponent(true);

        }



        private void btnModelResetProps_Click(object sender, EventArgs e)

        {

            txtModelName.Clear();

            txtModelMaterial.Clear();

            txtModelThickness.Clear();

            txtModelGoban.Clear();

            txtModelQty.Clear();

            txtModelFinish.Clear();

        }



        private void tabBom_SelectedIndexChanged(object sender, EventArgs e)

        {

            if (tabBom.SelectedTab == tabModel)

                LoadModelPropsFromActiveDocument(false);

        }



        private void SelectModelSideTab(string tabName)

        {

            if (tabModelPages == null)

                return;



            if (string.Equals(tabName, "Props", StringComparison.OrdinalIgnoreCase))

                tabModelPages.SelectedTab = tabModelPropsPage;

            else if (string.Equals(tabName, "Macro", StringComparison.OrdinalIgnoreCase))

                tabModelPages.SelectedTab = tabModelMacroPage;

            else

                tabModelPages.SelectedTab = tabModelEditPage;



            LayoutModelCommandButtons();

            LayoutModelMacroButtons();

        }



        private void LoadModelPropsFromSelectedComponent(bool showMessage)

        {

            string configName;

            Component2 component;

            ModelDoc2 model = GetSelectedModelForProps(out configName, out component);

            if (model == null)

            {

                if (showMessage)

                    MessageBox.Show("Hay chon component hoac mo Part truoc.", "Props", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return;

            }



            txtModelName.Text = GetModelProperty(model, component, configName, PropNameHinmei, PropNameBuhinmei);

            txtModelMaterial.Text = GetModelProperty(model, component, configName, PropNameMaterial);

            txtModelThickness.Text = GetModelProperty(model, component, configName, PropNameThickness);

            txtModelGoban.Text = GetModelProperty(model, component, configName, PropNameGoban);

            txtModelQty.Text = GetModelProperty(model, component, configName, PropNameQty);

            txtModelFinish.Text = GetModelProperty(model, component, configName, PropNameFinish);



            SaveLoadedModelPropsSnapshot();

        }



        private void LoadModelPropsFromActiveDocument(bool showMessage)

        {

            LoadModelPropsFromSelectedComponent(showMessage);

        }



        private void SaveModelPropsToSelectedComponent()

        {

            string configName;

            Component2 component;

            ModelDoc2 model = GetSelectedModelForProps(out configName, out component);

            if (model == null)

            {

                MessageBox.Show("Hay chon component hoac mo Part truoc.", "Props", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return;

            }



            int savedCount = 0;

            savedCount += SetChangedModelProperty(model, component, configName, PropNameHinmei, txtModelName.Text) ? 1 : 0;

            savedCount += SetChangedModelProperty(model, component, configName, PropNameMaterial, txtModelMaterial.Text) ? 1 : 0;

            savedCount += SetChangedModelProperty(model, component, configName, PropNameThickness, txtModelThickness.Text) ? 1 : 0;

            savedCount += SetChangedModelProperty(model, component, configName, PropNameGoban, txtModelGoban.Text) ? 1 : 0;

            savedCount += SetChangedModelProperty(model, component, configName, PropNameQty, txtModelQty.Text) ? 1 : 0;

            savedCount += SetChangedModelProperty(model, component, configName, PropNameFinish, txtModelFinish.Text) ? 1 : 0;



            if (savedCount > 0)

            {

                try

                {

                    model.SetSaveFlag();

                }

                catch

                {

                }



                SaveLoadedModelPropsSnapshot();

            }

            else

            {

                MessageBox.Show("Khong co noi dung nao thay doi.", "Props", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return;

            }



            MessageBox.Show("Da luu " + savedCount + " custom properties.", "Props", MessageBoxButtons.OK, MessageBoxIcon.Information);

        }



        private void SaveLoadedModelPropsSnapshot()

        {

            loadedModelPropValues[PropNameHinmei] = NormalizePropText(txtModelName.Text);

            loadedModelPropValues[PropNameMaterial] = NormalizePropText(txtModelMaterial.Text);

            loadedModelPropValues[PropNameThickness] = NormalizePropText(txtModelThickness.Text);

            loadedModelPropValues[PropNameGoban] = NormalizePropText(txtModelGoban.Text);

            loadedModelPropValues[PropNameQty] = NormalizePropText(txtModelQty.Text);

            loadedModelPropValues[PropNameFinish] = NormalizePropText(txtModelFinish.Text);

        }



        private bool SetChangedModelProperty(ModelDoc2 model, Component2 component, string configName, string propertyName, string value)

        {

            string currentValue = NormalizePropText(value);

            string loadedValue;

            if (loadedModelPropValues.TryGetValue(propertyName, out loadedValue) &&

                string.Equals(currentValue, NormalizePropText(loadedValue), StringComparison.Ordinal))

                return false;



            return SetModelProperty(model, component, configName, propertyName, value);

        }



        private string NormalizePropText(string value)

        {

            return value ?? "";

        }



        private ModelDoc2 GetSelectedModelForProps(out string configName, out Component2 component)

        {

            configName = "";

            component = null;



            ModelDoc2 activeModel = swApp?.ActiveDoc as ModelDoc2;

            if (activeModel == null)

                return null;



            int docType = activeModel.GetType();

            if (docType == (int)swDocumentTypes_e.swDocPART)

            {

                return activeModel;

            }



            SelectionMgr selMgr = activeModel.SelectionManager as SelectionMgr;

            int selectedCount = selMgr?.GetSelectedObjectCount2(-1) ?? 0;



            if (docType == (int)swDocumentTypes_e.swDocASSEMBLY || docType == (int)swDocumentTypes_e.swDocDRAWING)

            {

                for (int i = 1; i <= selectedCount; i++)

                {

                    component = GetSelectedComponentForProps(selMgr, i);

                    if (component == null)

                        continue;



                    ModelDoc2 componentModel = component.GetModelDoc2() as ModelDoc2;

                    if (componentModel == null)

                        continue;



                    return componentModel;

                }

            }



            if (docType == (int)swDocumentTypes_e.swDocDRAWING)

            {

                for (int i = 1; i <= selectedCount; i++)

                {

                    SolidWorks.Interop.sldworks.View view =

                        selMgr.GetSelectedObject6(i, -1) as SolidWorks.Interop.sldworks.View;

                    if (view == null)

                    {

                        try

                        {

                            view = selMgr.GetSelectedObjectsDrawingView2(i, -1) as SolidWorks.Interop.sldworks.View;

                        }

                        catch

                        {

                            view = null;

                        }

                    }



                    ModelDoc2 viewModel = view?.ReferencedDocument as ModelDoc2;

                    if (viewModel == null)

                        continue;



                    return viewModel;

                }



                return null;

            }



            if (docType == (int)swDocumentTypes_e.swDocASSEMBLY)

            {

                return activeModel;

            }



            return null;

        }



        private Component2 GetSelectedComponentForProps(SelectionMgr selMgr, int index)

        {

            if (selMgr == null)

                return null;



            Component2 component =

                selMgr.GetSelectedObjectsComponent4(index, -1) as Component2 ??

                selMgr.GetSelectedObjectsComponent3(index, -1) as Component2 ??

                selMgr.GetSelectedObject6(index, -1) as Component2;

            if (component != null)

                return component;



            DrawingComponent drawingComponent = selMgr.GetSelectedObject6(index, -1) as DrawingComponent;

            component = drawingComponent?.Component as Component2;

            if (component != null)

                return component;



            Entity entity = selMgr.GetSelectedObject6(index, -1) as Entity;

            if (entity != null)

            {

                component =

                    entity.GetComponent() as Component2 ??

                    entity.IGetComponent2() as Component2;

                if (component != null)

                    return component;

            }



            return null;

        }



        private string GetModelProperty(ModelDoc2 model, Component2 component, string configName, params string[] propertyNames)

        {

            if (model == null || propertyNames == null)

                return "";



            CustomPropertyManager propMgr = model.Extension.get_CustomPropertyManager("");

            foreach (string propertyName in propertyNames)

            {

                string value = GetModelPropertyFromManager(propMgr, propertyName);

                if (!string.IsNullOrWhiteSpace(value))

                    return value;

            }



            return "";

        }



        private string GetModelPropertyFromManager(CustomPropertyManager propMgr, string propertyName)

        {

            if (propMgr == null || string.IsNullOrWhiteSpace(propertyName))

                return "";



            try

            {

                string valOut;

                string resolvedValOut;

                bool wasResolved;

                bool linkToProperty;

                propMgr.Get6(propertyName, false, out valOut, out resolvedValOut, out wasResolved, out linkToProperty);

                return !string.IsNullOrWhiteSpace(resolvedValOut) ? resolvedValOut : (valOut ?? "");

            }

            catch

            {

                return "";

            }

        }



        private bool SetModelProperty(ModelDoc2 model, Component2 component, string configName, string propertyName, string value)

        {

            CustomPropertyManager propMgr = model?.Extension.get_CustomPropertyManager("");

            return SetModelPropertyValue(propMgr, propertyName, value);

        }



        private bool SetModelPropertyValue(CustomPropertyManager propMgr, string propertyName, string value)

        {

            if (propMgr == null || string.IsNullOrWhiteSpace(propertyName))

                return false;



            try

            {

                int result = propMgr.Set2(propertyName, value ?? "");

                if (result == (int)swCustomInfoSetResult_e.swCustomInfoSetResult_NotPresent)

                {

                    result = propMgr.Add3(

                        propertyName,

                        (int)swCustomInfoType_e.swCustomInfoText,

                        value ?? "",

                        (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);

                }



                return result == (int)swCustomInfoSetResult_e.swCustomInfoSetResult_OK

                    || result == (int)swCustomInfoAddResult_e.swCustomInfoAddResult_AddedOrChanged;

            }

            catch

            {

                return false;

            }

        }

        private void BeginProgress(int totalCount)

        {

            if (progressCheck == null)

                return;



            progressCheck.Minimum = 0;

            progressCheck.Maximum = Math.Max(1, totalCount);

            progressCheck.Value = 0;

            progressCheck.Visible = true;

            progressCheck.Refresh();

            Application.DoEvents();

        }



        private void UpdateProgress(int currentCount, int totalCount)

        {

            if (progressCheck == null)

                return;



            progressCheck.Maximum = Math.Max(1, totalCount);

            progressCheck.Value = Math.Min(progressCheck.Maximum, Math.Max(progressCheck.Minimum, currentCount));

            progressCheck.Refresh();

            Application.DoEvents();

        }



        private void FinishProgress()

        {

            if (progressCheck == null)

                return;



            if (progressCheck.Visible)

                progressCheck.Value = progressCheck.Maximum;



            progressCheck.Refresh();

            Application.DoEvents();

            progressCheck.Value = 0;

            progressCheck.Visible = false;

        }



        private void RunDrawingBomCommand(Action command, bool lockInput = true, bool silent = false)

        {

            if (command == null)

                return;



            // Never permit a leaked/queued click to start a second command while

            // the first command is pumping messages through Application.DoEvents().

            if (drawingBomCommandInProgress)

                return;



            drawingBomCommandInProgress = true;

            drawingBomCancelRequested = false;

            try

            {

                if (lockInput)

                    BeginSolidWorksInputLock();

                KeepDrawingBomTabVisible();

                command();

            }

            finally

            {

                bool showCanceledMessage = drawingBomCancelRequested;

                KeepDrawingBomTabVisible();

                if (lockInput)

                    EndSolidWorksInputLock();

                drawingBomCommandInProgress = false;

                drawingBomCancelRequested = false;

                UpdateBomCommandButtonState();

                if (showCanceledMessage && !silent)

                {

                    MessageBox.Show(

                        "Lenh da duoc huy va qua trinh xu ly da ket thuc.",

                        "CANCEL",

                        MessageBoxButtons.OK,

                        MessageBoxIcon.Information);

                }

            }

        }



        private void KeepDrawingBomTabVisible()

        {

            if (tabBom != null && tabDrawing != null)

                tabBom.SelectedTab = tabDrawing;

            if (tabDrawingPages != null && tabDrawingBom != null)

                tabDrawingPages.SelectedTab = tabDrawingBom;

        }





        private void BeginSolidWorksInputLock()

        {

            // This method must never lock the Task Pane during initialization.

            // It is valid only after RunDrawingBomCommand marks a command active.

            if (!drawingBomCommandInProgress || drawingBomUiLockActive)

                return;



            drawingBomUiLockActive = true;

            LockDrawingBomControls();

            try

            {

                solidWorksInputBlocker = new SolidWorksInputBlocker(button1);

                Application.AddMessageFilter(solidWorksInputBlocker);

            }

            catch

            {

                solidWorksInputBlocker = null;

                RestoreDrawingBomControls();

                drawingBomUiLockActive = false;

                throw;

            }

        }



        private void EndSolidWorksInputLock()

        {

            if (solidWorksInputBlocker != null)

            {

                Application.RemoveMessageFilter(solidWorksInputBlocker);

                solidWorksInputBlocker = null;

            }

            RestoreDrawingBomControls();

            drawingBomUiLockActive = false;

        }



        private void LockDrawingBomControls()

        {

            drawingBomControlEnabledStates.Clear();

            SetDrawingBomControlsLocked(tabDrawingBom);



            // CANCEL is the only command that must remain available.

            if (button1 != null)

                button1.Enabled = true;

        }



        private void SetDrawingBomControlsLocked(Control parent)

        {

            if (parent == null)

                return;



            foreach (Control control in parent.Controls)

            {

                if (control == button1 || control == bomDxfExportPane)

                    continue;



                if (IsDrawingBomInteractiveControl(control))

                {

                    drawingBomControlEnabledStates[control] = control.Enabled;

                    control.Enabled = false;

                    continue;

                }



                SetDrawingBomControlsLocked(control);

            }

        }



        private static bool IsDrawingBomInteractiveControl(Control control)

        {

            return control is ButtonBase

                || control is TextBoxBase

                || control is ComboBox

                || control is ListControl

                || control is DataGridView

                || control is NumericUpDown

                || control is TreeView

                || control is ListView

                || control is PropertyGrid;

        }



        private void RestoreDrawingBomControls()

        {

            foreach (KeyValuePair<Control, bool> state in drawingBomControlEnabledStates)

            {

                if (state.Key != null && !state.Key.IsDisposed)

                    state.Key.Enabled = state.Value;

            }

            drawingBomControlEnabledStates.Clear();

        }



        private bool IsDrawingBomCancelRequested()

        {

            return drawingBomCancelRequested;

        }



        private void cancel_Click(object sender, EventArgs e)

        {

            if (drawingBomCommandInProgress)

            {

                drawingBomCancelRequested = true;

                lblStatus.Text = "Đang dừng lệnh…";

            }

            actions?.RequestCancel();

        }



        private void btnGetWL_Click(object sender, EventArgs e)

        {

            if (!UpdateWidthLengthFromSelectedDimensions())

                LoadWidthLengthFromSelectedView(true);

        }



        private void btnNote_Click(object sender, EventArgs e)

        {
            RunDrawingAnnotation("Note", command => command.InsertNote());
        }


        private void btnNote_MouseUp(object sender, MouseEventArgs e)

        {

            if (e.Button != MouseButtons.Right)

                return;



            if (MessageBox.Show(

                "Xoa noi dung Note da luu?",

                "Note",

                MessageBoxButtons.YesNo,

                MessageBoxIcon.Question) != DialogResult.Yes)

                return;



            drawingTextAnnotationCommands?.DeleteSelectedNote();

        }



        private void btnText_Click(object sender, EventArgs e)

        {
            RunDrawingAnnotation("Text DIM", command => command.InsertText());
        }


        private void btnInsertBalloon_Click(object sender, EventArgs e)

        {
            RunDrawingAnnotation("Balloon", command => command.InsertBalloon());
        }


        private void btnDeleteNote_Click(object sender, EventArgs e)

        {

            drawingTextAnnotationCommands?.DeleteSelectedNote();

        }



        private void btnDeleteText_Click(object sender, EventArgs e)

        {

            drawingTextAnnotationCommands?.DeleteSelectedText();

        }



        private void btnHorizontalAlignment_Click(object sender, EventArgs e)

        {

            drawingViewRotator?.AlignSelectedCurveHorizontal();

            LoadWidthLengthFromSelectedView(true);

        }



        private void btnRotateCw_Click(object sender, EventArgs e)

        {

            drawingViewRotator?.RotateClockwise90();

            LoadWidthLengthFromSelectedView(true);

        }



        private void btnRotateCcw_Click(object sender, EventArgs e)

        {

            drawingViewRotator?.RotateCounterClockwise90();

            LoadWidthLengthFromSelectedView(true);

        }



        private void dimvang_Click(object sender, EventArgs e)

        {

            using (DeleteYellowAnnotationsDialog dialog =

                   new DeleteYellowAnnotationsDialog())

            {

                if (dialog.ShowDialog(this) != DialogResult.OK)

                    return;



                if (dialog.DeleteDimension)

                {

                    XoaDimMauVang cleaner = new XoaDimMauVang(swApp);

                    cleaner.DeleteDanglingDimensions();

                }



                if (dialog.DeleteBalloon)

                {

                    XoaBalloonMauVang cleaner = new XoaBalloonMauVang(swApp);

                    cleaner.DeleteDanglingBalloons();

                }



                if (dialog.DeleteText)

                {

                    XoaTextMauVang cleaner = new XoaTextMauVang(swApp);

                    cleaner.DeleteDanglingText();

                }

            }

        }



        private void btnFixScale_Click(object sender, EventArgs e)

        {
            FixViewScale();
        }


        private void btnDimKegaki_Click(object sender, EventArgs e)

        {
            RunSimpleDrawingDimensions(NativeToolKind.KegakiDimensions);
        }


        private void btnDimKichThuocLo_Click(object sender, EventArgs e)

        {
            RunHoleDimensions();
        }


        private void btnRepairDim_Click(object sender, EventArgs e)

        {
            ShowNativeRepairDimensions();
        }


        private void WireXepUnitButton(Control parent)

        {

            if (parent == null)

                return;



            foreach (Control child in parent.Controls)

            {

                Button button = child as Button;

                if (button != null)

                {

                    string norm = NormalizeButtonText(button.Text);

                    if (button == button2 || norm.Contains("XEPUNIT") || norm.Contains("XẾPUNIT") || norm.Contains("XEP") || norm.Contains("XẾP"))

                    {

                        button.Click -= btnXepUnit_Click;

                        button.Click += btnXepUnit_Click;

                    }

                }



                WireXepUnitButton(child);

            }

        }



        private string NormalizeButtonText(string text)

        {

            return (text ?? "").Replace(" ", "").Replace("\r", "").Replace("\n", "").ToUpperInvariant();

        }



        private void chkSelectAll_CheckedChanged(object sender, EventArgs e)

        {

            actions?.SetAllChecked(chkSelectAll.Checked);

        }



        private void dgvModelBom_CurrentCellDirtyStateChanged(object sender, EventArgs e)

        {

            actions?.CommitCurrentCellIfDirty();

        }



        private void dgvModelBom_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)

        {

            actions?.BeginCheckboxSelection(e.RowIndex, e.ColumnIndex);

        }



        private void dgvModelBom_CellContentClick(object sender, DataGridViewCellEventArgs e)

        {

            actions?.ApplyCheckboxSelection(e.RowIndex, e.ColumnIndex);

        }



        private void dgvModelBom_KeyDown(object sender, KeyEventArgs e)

        {

            actions?.ToggleSelectedRowsBySpace(e);

        }



        private void dgvModelBom_SizeChanged(object sender, EventArgs e)

        {

            actions?.AutoFitBomGrid();

        }



        private void PanelModelCommands_SizeChanged(object sender, EventArgs e)

        {

            LayoutModelCommandButtons();

            LayoutMakeHoleOptions();

        }

        private void BomTaskPaneControl_Resize(object sender, EventArgs e)

        {

            actions?.AutoFitBomGrid();

            LayoutComponentViewSize();

            LayoutDrawingBomTab();

            LayoutModelCommandButtons();

            LayoutModelMacroButtons();

            LayoutMakeHoleOptions();

            

        }



        private void tabComponentDrawing_Resize(object sender, EventArgs e)

        {

            LayoutComponentViewSize();

        }



        private void LayoutDrawingBomTab()

        {

            LayoutDrawingBomCommands();

        }



        private void BomTaskPaneControl_Disposed(object sender, EventArgs e)

        {

            ShutdownFromSolidWorks();

            if (bomCommandToolTip != null)

            {

                bomCommandToolTip.Dispose();

                bomCommandToolTip = null;

            }

            if (bomCommandToolTipFont != null)

            {

                bomCommandToolTipFont.Dispose();

                bomCommandToolTipFont = null;

            }

        }



        private void InitComponentDrawingTimer()

        {

            componentDrawingTimer = new Timer();

            componentDrawingTimer.Interval = 500;

            componentDrawingTimer.Tick += componentDrawingTimer_Tick;

            componentDrawingTimer.Start();

        }



        private void componentDrawingTimer_Tick(object sender, EventArgs e)

        {

            SyncSizeWithTaskPaneHost();



            if (solidWorksClosing || swApp == null)

                return;



            try

            {

                ModelDoc2 model = swApp.ActiveDoc as ModelDoc2;

                if (tabBom.SelectedTab != tabDrawing ||

                    tabDrawingPages.SelectedTab != tabComponentDrawing ||

                    model == null ||

                    model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)

                    return;



                LoadWidthLengthFromSelectedView(false);

            }

            catch (InvalidComObjectException)

            {

                HandleSolidWorksComClosed();

            }

            catch (COMException)

            {

                HandleSolidWorksComClosed();

            }

        }



        private void StartComponentDrawingTimer()

        {

            if (componentDrawingTimer == null)

                return;



            if (!componentDrawingTimer.Enabled)

                componentDrawingTimer.Start();

        }



        private void StopComponentDrawingTimer()

        {

            if (componentDrawingTimer == null)

                return;



            componentDrawingTimer.Stop();

        }



        private void DisposeComponentDrawingTimer()

        {

            if (componentDrawingTimer == null)

                return;



            componentDrawingTimer.Stop();

            componentDrawingTimer.Dispose();

            componentDrawingTimer = null;

        }



        private void InitMakeHoleUpdateMonitor()

        {

            if (makeHoleUpdateTimer != null)

                return;



            makeHoleUpdateTimer = new Timer();

            makeHoleUpdateTimer.Interval = 1500;

            makeHoleUpdateTimer.Tick += MakeHoleUpdateTimer_Tick;

            makeHoleUpdateTimer.Start();



            SetMakeHoleUpdateButtonState(false);

        }



        private void MakeHoleUpdateTimer_Tick(object sender, EventArgs e)

        {
            if (nativeMakeHolePage != null || nativeModelCommandPage != null ||
                (autoSplinePage != null && autoSplinePage.IsOpen) ||
                (makeHoleCommand != null && makeHoleCommand.HasPendingHybridPattern)) return;


            if (solidWorksClosing)

                return;



            bool needUpdate = false;



            try

            {

                ModelDoc2 activeModel = swApp == null ? null : swApp.ActiveDoc as ModelDoc2;

                int documentType = activeModel == null ? 0 : activeModel.GetType();

                UpdateModelMacroCommandAvailability(documentType);
                bool canUseMakeHole = CanUseMakeHoleActions();
                btnMakeHoleReset.Enabled = canUseMakeHole;
                if (makeHoleActionsOnly) txtMakeHolePitch.Enabled = canUseMakeHole;

                if (documentType != (int)swDocumentTypes_e.swDocPART &&

                    documentType != (int)swDocumentTypes_e.swDocASSEMBLY)

                {

                    SetMakeHoleUpdateButtonState(false);

                    return;

                }

                if (makeHoleCommand != null)

                {

                    bool cleaned = makeHoleCommand.CleanupTrackedMakeHoleEquationsIfFeatureMissing();



                    if (cleaned)

                    {

                        SetMakeHoleUpdateButtonState(false);

                        return;

                    }



                    // Keep tracked-feature cleanup, but only probe selection for

                    // the update button when its Model/Edit page is visible.

                    if (tabBom.SelectedTab != tabModel || tabModelPages.SelectedTab != tabModelEditPage)

                        return;

                    double pitch;

                    if (TryParsePositiveMillimeter(txtMakeHolePitch.Text, out pitch))

                        needUpdate = makeHoleCommand.IsMakeHoleUpdateRequired(pitch);

                    else

                        needUpdate = makeHoleCommand.IsMakeHoleUpdateRequired();

                }

            }

            catch

            {

                needUpdate = false;

            }



            SetMakeHoleUpdateButtonState(needUpdate);

        }



        private void SetMakeHoleUpdateButtonState(bool needUpdate)

        {

            if (btnMakeHoleUpdate == null)

                return;



            btnMakeHoleUpdate.Visible = true;

            btnMakeHoleUpdate.Enabled = needUpdate;
            btnMakeHoleUpdate.ForeColor = needUpdate ? Color.FromArgb(30, 64, 175) : Color.FromArgb(148, 163, 184);

            btnMakeHoleUpdate.Text = "Update hole";

        }



        private void HandleSolidWorksComClosed()

        {

            solidWorksClosing = true;

            DismissNativeModelCommand();
            StopComponentDrawingTimer();

            DetachSolidWorksEvents();

            swApp = null;

            UpdateModelMacroCommandAvailability(0);

        }



        private void LayoutComponentViewSize()
        {
            if (grpComponentSize == null || btnGetWL == null)
                return;

            tabComponentDrawing.AutoScroll = true;

            int pageMargin = 8;
            int pageWidth = Math.Max(230, tabComponentDrawing.ClientSize.Width - (pageMargin * 2));
            int margin = 10;
            int gap = 6;

            int fieldH = 36;
            int row1Y = 40;
            int row2Y = row1Y + fieldH + 4; // 80
            int btnH = 32;
            int row3Y = row2Y + fieldH + 6; // 122

            int availableWidth = Math.Max(200, pageWidth - (margin * 2));
            bool singleRowButtons = availableWidth >= 305;
            int cardHeight = singleRowButtons ? (row3Y + btnH + 10) : (row3Y + btnH * 2 + 14);

            grpComponentSize.SetBounds(pageMargin, 6, pageWidth, cardHeight);

            bool narrow = pageWidth < 380;
            int buttonWidth = narrow ? 68 : 80;
            int groupWidth = Math.Max(120, grpComponentSize.ClientSize.Width - margin - buttonWidth - gap - 6);

            groupBox1.SetBounds(margin, row1Y, groupWidth, fieldH);
            groupBox2.SetBounds(margin, row2Y, groupWidth, fieldH);

            int labelW = 54;
            int textW = Math.Max(40, groupBox1.ClientSize.Width - labelW - 2);

            Font dimFont = ADDIN.UI.TaiTheme.Value;
            txtWidth.Font = dimFont;
            txtLength.Font = dimFont;
            txtWidth.SetBounds(labelW, 2, textW, 26);
            txtLength.SetBounds(labelW, 2, textW, 26);
            txtWidth.BackColor = Color.White;
            txtWidth.ForeColor = Color.FromArgb(15, 23, 42);
            txtLength.BackColor = Color.White;
            txtLength.ForeColor = Color.FromArgb(15, 23, 42);

            int getWLHeight = row2Y + fieldH - row1Y; // 68px
            btnGetWL.SetBounds(margin + groupWidth + gap, row1Y, buttonWidth, getWLHeight);

            // Cấu hình nút nhỏ gọn
            Font btnFont = ADDIN.UI.TaiTheme.BodyStrong;
            btnGetWL.Font = btnFont;
            btnHorizontalAlignment.Font = btnFont;
            btnRotateCw.Font = btnFont;
            btnRotateCcw.Font = btnFont;

            availableWidth = grpComponentSize.ClientSize.Width - (margin * 2);
            if (singleRowButtons)
            {
                int horizW = Math.Max(120, (int)(availableWidth * 0.44));
                int rotW = (availableWidth - horizW - (gap * 2)) / 2;

                btnHorizontalAlignment.SetBounds(margin, row3Y, horizW, btnH);
                btnRotateCw.SetBounds(margin + horizW + gap, row3Y, rotW, btnH);
                btnRotateCcw.SetBounds(margin + horizW + gap + rotW + gap, row3Y, availableWidth - horizW - rotW - (gap * 2), btnH);
            }
            else
            {
                int rotW = Math.Max(60, (availableWidth - gap) / 2);
                int row4Y = row3Y + btnH + 4;

                btnHorizontalAlignment.SetBounds(margin, row3Y, availableWidth, btnH);
                btnRotateCw.SetBounds(margin, row4Y, rotW, btnH);
                btnRotateCcw.SetBounds(margin + rotW + gap, row4Y, availableWidth - rotW - gap, btnH);
            }

            grpComponentSize.Invalidate(true);

            LayoutComponentTextGroup(pageMargin, pageWidth, gap, narrow);
            LayoutComponentMacroGroup(pageMargin, pageWidth, gap, narrow);
            tabComponentDrawing.AutoScrollMinSize = new System.Drawing.Size(0, groupBox3.Bottom + pageMargin);
        }



        private void LayoutComponentTextGroup(int pageMargin, int pageWidth, int gap, bool narrow)

        {

            int groupTop = grpComponentSize.Bottom + 8;

            int cardHeight = 162;

            grpComponentBom.SetBounds(pageMargin, groupTop, pageWidth, cardHeight);



            int margin = 12;

            int buttonWidth = narrow ? 72 : 105;

            int deleteWidth = 28;



            btnNote.Text = "Note";

            btnText.Text = "Text";

            int rightGap = narrow ? 6 : margin;

            int smallGap = narrow ? 6 : gap;

            int buttonX = Math.Max(margin + 80, grpComponentBom.ClientSize.Width - rightGap - buttonWidth);

            int deleteX = buttonX - smallGap - deleteWidth;

            int comboWidth = Math.Max(110, deleteX - margin - gap);

            int balloonBtnWidth = grpComponentBom.ClientSize.Width - rightGap - deleteX;

            btnInsertBalloon.Text = balloonBtnWidth >= 120 ? "Insert Balloon" : "Balloon";



            int row1Y = 40;

            int row2Y = 78;

            int row3Y = 116;

            int inputH = 30;

            int btnH = 30;



            cboBendLine.SetBounds(margin, row1Y, comboWidth, inputH);

            cboSide.SetBounds(margin, row2Y, comboWidth, inputH);

            cboBalloonProperty.SetBounds(margin, row3Y, comboWidth, inputH);




            btnDeleteNote.SetBounds(deleteX, row1Y, deleteWidth, btnH);

            btnDeleteText.SetBounds(deleteX, row2Y, deleteWidth, btnH);



            btnNote.SetBounds(buttonX, row1Y, buttonWidth, btnH);

            btnText.SetBounds(buttonX, row2Y, buttonWidth, btnH);

            btnInsertBalloon.SetBounds(deleteX, row3Y, balloonBtnWidth, btnH);



            grpComponentBom.Invalidate(true);

        }



        private void LayoutComponentMacroGroup(int pageMargin, int pageWidth, int gap, bool narrow)

        {

            int groupTop = grpComponentBom.Bottom + 8;

            int minHeight = CommandPixels(252);

            groupBox3.SetBounds(pageMargin, groupTop, pageWidth, minHeight);




            Button[] buttons =

            {

                dimvang,

                btnDimMatCat,

                btnSplineToArcs,

                btnDimKegaki,

                btnDimKichThuocLo,

                btnFixScale,

                btnRepairDim,

                btnVCutSection

            };



            int innerLeft = CommandPixels(12);

            int innerTop = CommandPixels(40);

            int innerGap = CommandPixels(8);

            int availableWidth = Math.Max(1, groupBox3.ClientSize.Width - (innerLeft * 2));

            int tileWidth = CommandPixels(176);

            int columnCount = availableWidth >= (tileWidth * 2 + innerGap) ? 2 : 1;

            int buttonWidth = Math.Max(1, (availableWidth - innerGap * (columnCount - 1)) / columnCount);

            int buttonHeight = CommandPixels(50);



            for (int i = 0; i < buttons.Length; i++)

            {

                Button button = buttons[i];

                if (button == null)

                    continue;



                int column = i % columnCount;

                int row = i / columnCount;

                int gridWidth = columnCount * buttonWidth + (columnCount - 1) * innerGap;

                int gridLeft = innerLeft + Math.Max(0, (availableWidth - gridWidth) / 2);

                int x = gridLeft + column * (buttonWidth + innerGap);

                int y = innerTop + row * (buttonHeight + innerGap);



                button.SetBounds(x, y, buttonWidth, buttonHeight);

            }



            int rowCount = (buttons.Length + columnCount - 1) / columnCount;

            int requiredHeight = innerTop + rowCount * buttonHeight +

                (rowCount - 1) * innerGap + CommandPixels(12);

            // The card ends where its tiles end.

            groupBox3.Height = requiredHeight;



            groupBox3.Invalidate(true);

        }




        private void LoadWidthLengthFromSelectedView(bool forceRefresh)

        {

            ModelDoc2 drawingModel = swApp?.ActiveDoc as ModelDoc2;

            if (drawingModel == null ||

                drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)

            {

                ClearWidthLength();

                return;

            }



            SolidWorks.Interop.sldworks.View selectedView = GetSelectedDrawingView(drawingModel);

            if (selectedView == null)

            {

                return;

            }



            drawingViewRotator?.RememberView(selectedView);



            ModelDoc2 referencedModel = selectedView.ReferencedDocument as ModelDoc2;

            if (referencedModel == null)

            {

                return;

            }



            string referencedConfig = selectedView.ReferencedConfiguration;

            string viewKey = selectedView.Name + "|" + referencedModel.GetPathName() + "|" + referencedConfig;

            if (!forceRefresh && string.Equals(viewKey, lastSelectedViewKey, StringComparison.OrdinalIgnoreCase))

                return;



            lastSelectedViewKey = viewKey;

            txtWidth.Text = FormatWidthLengthText(GetCustomPropertyValue(referencedModel, referencedConfig, "W"));

            txtLength.Text = FormatWidthLengthText(GetCustomPropertyValue(referencedModel, referencedConfig, "L"));

        }



        private bool UpdateWidthLengthFromSelectedDimensions()

        {

            ModelDoc2 drawingModel = swApp?.ActiveDoc as ModelDoc2;

            if (drawingModel == null ||

                drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)

                return false;



            List<SelectedDimensionInfo> selectedDimensions = new List<SelectedDimensionInfo>();

            SolidWorks.Interop.sldworks.View targetView = null;

            SelectionMgr selMgr = drawingModel.SelectionManager as SelectionMgr;

            if (selMgr == null)

                return false;



            int count = selMgr.GetSelectedObjectCount2(-1);

            for (int i = 1; i <= count; i++)

            {

                object selectedObject = selMgr.GetSelectedObject6(i, -1);

                DisplayDimension displayDimension = selectedObject as DisplayDimension;

                if (displayDimension == null)

                    continue;



                SelectedDimensionInfo dimInfo;

                if (!TryGetSelectedDimensionInfo(displayDimension, out dimInfo))

                    continue;



                selectedDimensions.Add(dimInfo);



                if (targetView == null)

                    targetView = GetViewFromDisplayDimension(displayDimension);

            }



            if (selectedDimensions.Count != 2 || targetView == null)

                return false;



            drawingViewRotator?.RememberView(targetView);



            ModelDoc2 referencedModel = targetView.ReferencedDocument as ModelDoc2;

            if (referencedModel == null)

                return false;



            selectedDimensions.Sort((left, right) => left.ValueMillimeters.CompareTo(right.ValueMillimeters));

            string width = FormatDimensionValue(selectedDimensions[0].ValueMillimeters);

            string length = FormatDimensionValue(selectedDimensions[1].ValueMillimeters);

            string widthLink = BuildDimensionLink(selectedDimensions[0].FullName);

            string lengthLink = BuildDimensionLink(selectedDimensions[1].FullName);

            string referencedConfig = targetView.ReferencedConfiguration;



            SetDrawingCustomPropertyValue(drawingModel, "W", widthLink);

            SetDrawingCustomPropertyValue(drawingModel, "L", lengthLink);



            txtWidth.Text = width;

            txtLength.Text = length;

            lastSelectedViewKey = "";



            return true;

        }



        private class SelectedDimensionInfo

        {

            public double ValueMillimeters { get; set; }

            public string FullName { get; set; }

        }



        private bool TryGetSelectedDimensionInfo(DisplayDimension displayDimension, out SelectedDimensionInfo info)

        {

            info = null;



            Dimension dimension = displayDimension.GetDimension() as Dimension;

            if (dimension == null)

                return false;



            double meters = dimension.SystemValue;

            if (double.IsNaN(meters) || double.IsInfinity(meters) || meters <= 0)

                return false;



            string fullName = dimension.FullName;

            if (string.IsNullOrWhiteSpace(fullName))

                fullName = dimension.GetNameForSelection();



            if (string.IsNullOrWhiteSpace(fullName))

                return false;



            info = new SelectedDimensionInfo

            {

                ValueMillimeters = meters * 1000.0,

                FullName = fullName

            };



            return true;

        }



        private string BuildDimensionLink(string dimensionFullName)

        {

            if (string.IsNullOrWhiteSpace(dimensionFullName))

                return "";



            string escapedName = dimensionFullName.Replace("\"", "\"\"");

            return "\"" + escapedName + "\"";

        }



        private bool TryGetDimensionMillimeters(DisplayDimension displayDimension, out double millimeters)

        {

            millimeters = 0;



            Dimension dimension = displayDimension.GetDimension() as Dimension;

            if (dimension == null)

                return false;



            double meters = dimension.SystemValue;

            if (double.IsNaN(meters) || double.IsInfinity(meters) || meters <= 0)

                return false;



            millimeters = meters * 1000.0;

            return true;

        }



        private SolidWorks.Interop.sldworks.View GetViewFromDisplayDimension(DisplayDimension displayDimension)

        {

            try

            {

                Annotation annotation = displayDimension.GetAnnotation() as Annotation;

                if (annotation == null)

                    return null;



                return annotation.Owner as SolidWorks.Interop.sldworks.View;

            }

            catch

            {

                return null;

            }

        }



        private string FormatDimensionValue(double value)

        {

            return value.ToString("0.0", CultureInfo.InvariantCulture);

        }



        private string FormatWidthLengthText(string value)

        {

            if (string.IsNullOrWhiteSpace(value))

                return value;



            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariantValue))

                return FormatDimensionValue(invariantValue);



            if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double currentValue))

                return FormatDimensionValue(currentValue);



            return value;

        }



        private void ClearWidthLength()

        {

            lastSelectedViewKey = "";

            drawingViewRotator?.ClearRememberedView();

            ClearWidthLengthText();

        }



        private void ClearWidthLengthText()

        {

            txtWidth.Text = "";

            txtLength.Text = "";

        }



        private SolidWorks.Interop.sldworks.View GetSelectedDrawingView(ModelDoc2 drawingModel)

        {

            SelectionMgr selMgr = drawingModel.SelectionManager as SelectionMgr;

            if (selMgr == null)

                return null;



            int count = selMgr.GetSelectedObjectCount2(-1);

            for (int i = 1; i <= count; i++)

            {

                object selectedObject = selMgr.GetSelectedObject6(i, -1);

                SolidWorks.Interop.sldworks.View view =

                    selectedObject as SolidWorks.Interop.sldworks.View;



                if (view != null)

                    return view;



                view = selMgr.GetSelectedObjectsDrawingView2(i, -1);

                if (view != null)

                    return view;

            }



            return null;

        }



        private bool DrawingSelectionHasTable(ModelDoc2 drawingModel)

        {

            SelectionMgr selMgr = drawingModel.SelectionManager as SelectionMgr;

            if (selMgr == null)

                return false;



            int count = selMgr.GetSelectedObjectCount2(-1);

            for (int i = 1; i <= count; i++)

            {

                int selectedType = selMgr.GetSelectedObjectType3(i, -1);

                if (selectedType == (int)swSelectType_e.swSelANNOTATIONTABLES)

                    return true;



                object selectedObject = selMgr.GetSelectedObject6(i, -1);

                if (selectedObject as ITableAnnotation != null)

                    return true;



                Annotation annotation = selectedObject as Annotation;

                if (annotation != null &&

                    annotation.GetSpecificAnnotation() as ITableAnnotation != null)

                    return true;

            }



            return false;

        }



        private string GetCustomPropertyValue(ModelDoc2 model, string configName, string propertyName)

        {

            string value = GetCustomPropertyValueFromManager(

                model.Extension.get_CustomPropertyManager(configName ?? ""),

                propertyName);



            if (!string.IsNullOrWhiteSpace(value))

                return value;



            return GetCustomPropertyValueFromManager(

                model.Extension.get_CustomPropertyManager(""),

                propertyName);

        }



        private string GetCustomPropertyValueFromManager(CustomPropertyManager propMgr, string propertyName)

        {

            if (propMgr == null)

                return "";



            string valOut;

            string resolvedVal;

            bool wasResolved;

            bool linkToProp;



            propMgr.Get6(propertyName, true, out valOut, out resolvedVal, out wasResolved, out linkToProp);



            return string.IsNullOrWhiteSpace(resolvedVal) ? valOut : resolvedVal;

        }



        private void SetCustomPropertyValue(ModelDoc2 model, string configName, string propertyName, string value)

        {

            CustomPropertyManager propMgr =

                model.Extension.get_CustomPropertyManager(configName ?? "");



            SetCustomPropertyValueInManager(propMgr, propertyName, value);



            if (string.IsNullOrWhiteSpace(configName))

                return;



            CustomPropertyManager filePropMgr =

                model.Extension.get_CustomPropertyManager("");



            SetCustomPropertyValueInManager(filePropMgr, propertyName, value);

        }



        private void SetDrawingCustomPropertyValue(ModelDoc2 drawingModel, string propertyName, string value)

        {

            if (drawingModel == null ||

                drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)

                return;



            CustomPropertyManager propMgr =

                drawingModel.Extension.get_CustomPropertyManager("");



            SetCustomPropertyValueInManager(propMgr, propertyName, value);

        }



        private void SetCustomPropertyValueInManager(CustomPropertyManager propMgr, string propertyName, string value)

        {

            if (propMgr == null)

                return;



            propMgr.Add3(

                propertyName,

                (int)swCustomInfoType_e.swCustomInfoText,

                value,

                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);

        }



        private void AttachSolidWorksEvents()

        {

            DetachSolidWorksEvents();



            swEvents = swApp as DSldWorksEvents_Event;

            if (swEvents != null)

            {
                swEvents.ActiveDocChangeNotify += OnActiveDocChangeNotify;
                swEvents.CommandCloseNotify += OnMakeHoleCommandCloseNotify;
                swEvents.OnIdleNotify += OnMakeHoleIdleNotify;
            }
            AttachMarkHighlight();

        }



        private void DetachSolidWorksEvents()

        {

            if (swEvents == null)

                return;



            try

            {

                swEvents.ActiveDocChangeNotify -= OnActiveDocChangeNotify;
                swEvents.CommandCloseNotify -= OnMakeHoleCommandCloseNotify;
                swEvents.OnIdleNotify -= OnMakeHoleIdleNotify;

            }

            catch (InvalidComObjectException)

            {

            }

            catch (COMException)

            {

            }



            swEvents = null;

        }



        private int OnActiveDocChangeNotify()

        {

            if (solidWorksClosing)

                return 0;



            try

            {

                if (drawingBomCommandInProgress)

                {

                    RefreshModelMacroCommandAvailability();

                    KeepDrawingBomTabVisible();

                    return 0;

                }

                SwitchTabByActiveDocument();

                if (IsActiveModelDocument())

                    LoadModelPropsFromActiveDocument(false);

            }

            catch (InvalidComObjectException)

            {

                HandleSolidWorksComClosed();

            }

            catch (COMException)

            {

                HandleSolidWorksComClosed();

            }



            return 0;

        }



        private bool IsActiveModelDocument()

        {

            ModelDoc2 model = swApp?.ActiveDoc as ModelDoc2;

            if (model == null)

                return false;



            int docType = model.GetType();

            return docType == (int)swDocumentTypes_e.swDocPART ||

                   docType == (int)swDocumentTypes_e.swDocASSEMBLY;

        }



        private void SwitchTabByActiveDocument()

        {

            if (drawingBomCommandInProgress)

            {
                RefreshModelMacroCommandAvailability();

                KeepDrawingBomTabVisible();

                return;

            }



            ModelDoc2 model = swApp?.ActiveDoc as ModelDoc2;

            if (model == null)

            {

                UpdateModelMacroCommandAvailability(0);

                SetModelCommandAvailability(false);

                return;

            }



            int docType = model.GetType();

            UpdateModelMacroCommandAvailability(docType);

            if (docType == (int)swDocumentTypes_e.swDocDRAWING)

            {

                SetModelCommandAvailability(false);

                tabBom.SelectedTab = tabDrawing;

                tabDrawingPages.SelectedTab = tabComponentDrawing;

                return;

            }



            if (docType == (int)swDocumentTypes_e.swDocPART ||

                docType == (int)swDocumentTypes_e.swDocASSEMBLY)

            {

                SetModelCommandAvailability(true);

                tabBom.SelectedTab = tabModel;

                return;

            }



            SetModelCommandAvailability(false);

        }



        private readonly Dictionary<Control, string> modelMacroDescriptions = new Dictionary<Control, string>();

        private bool IsModelMacroSupported(Control button, int documentType)
        {
            if (ReferenceEquals(button, btnCheckAssemblyHole) ||
                ReferenceEquals(button, btnAutoFrameJoint))
                return documentType == (int)swDocumentTypes_e.swDocASSEMBLY;
            if (ReferenceEquals(button, btnMirrorPart) || ReferenceEquals(button, btnCheckBendMinimum))
                return documentType == (int)swDocumentTypes_e.swDocPART ||
                       documentType == (int)swDocumentTypes_e.swDocASSEMBLY;
            if (ReferenceEquals(button, btnEdgeToEqualSpline) || ReferenceEquals(button, btnRepair3DSpline) ||
                ReferenceEquals(button, btnVCutPropertySetup))
                return documentType == (int)swDocumentTypes_e.swDocPART;
            return false;
        }

        private void UpdateModelMacroButton(Control button, int documentType, string requiredEnvironment)
        {
            if (button == null) return;
            bool supported = IsModelMacroSupported(button, documentType);
            button.Enabled = supported;
            if (bomCommandToolTip == null) return;
            string description;
            if (!modelMacroDescriptions.TryGetValue(button, out description))
            {
                description = bomCommandToolTip.GetToolTip(button);
                modelMacroDescriptions.Add(button, description);
            }
            SetBomCommandToolTip(button, supported ? description :
                "Chỉ dùng trong " + requiredEnvironment + ".\n" + description);
        }

        private void UpdateModelMacroCommandAvailability(int documentType)
        {
            if (IsDisposed || Disposing) return;
            UpdateModelMacroButton(btnCheckAssemblyHole, documentType, "Assembly");
            UpdateModelMacroButton(btnMirrorPart, documentType, "Part hoặc Assembly");
            UpdateModelMacroButton(btnEdgeToEqualSpline, documentType, "Part");
            UpdateModelMacroButton(btnRepair3DSpline, documentType, "Part");
            UpdateModelMacroButton(btnVCutPropertySetup, documentType, "Part");
            UpdateModelMacroButton(btnCheckBendMinimum, documentType, "Part hoặc Assembly");
            UpdateModelMacroButton(btnAutoFrameJoint, documentType, "Assembly");
            // CHECK DXF works on files in a folder, independent of the active SW document.
            if (btnCheckDxfMacro != null) btnCheckDxfMacro.Enabled = true;
        }

        private void RefreshModelMacroCommandAvailability()
        {
            int documentType = 0;
            try
            {
                var model = solidWorksClosing || swApp == null ? null : swApp.ActiveDoc as ModelDoc2;
                if (model != null) documentType = model.GetType();
            }
            catch (InvalidComObjectException) { }
            catch (COMException) { }
            UpdateModelMacroCommandAvailability(documentType);
        }

        private bool CanRunModelMacro(Control button)
        {
            if (IsDisposed || Disposing || solidWorksClosing || !Enabled) return false;
            // Recheck on invocation as well: a document can switch before the UI notification arrives.
            RefreshModelMacroCommandAvailability();
            return button != null && button.Enabled;
        }

        private void SetModelCommandAvailability(bool enabled)

        {

            if (panelModelCommands != null)

                panelModelCommands.Enabled = enabled;
            if (nativeMakeHolePage == null && nativeModelCommandPage == null && !repairHolePanelMode)
                ShowMakeHoleActions();

        }



        private void btnGetWL_Click_1(object sender, EventArgs e)

        {



        }

    }

}













