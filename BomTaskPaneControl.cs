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
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN
{
    [ComVisible(true)]
    [ProgId("ADDIN.BomTaskPaneControl")]
    public partial class BomTaskPaneControl : UserControl
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
        private LenhNoteTextBalloon drawingTextAnnotationCommands;
        private CheckBalloon balloonChecker;
        private Button btnCheckBalloon;
        private XepUnitDrawing xepUnitDrawing;
        private LenhMakeHole makeHoleCommand;
        private PaintHoleSummaryCommand paintHoleSummaryCommand;
        private Timer componentDrawingTimer;
        private Timer makeHoleUpdateTimer;
        private Timer initialLayoutTimer;
        private Button btnDimKichThuocLo;
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
        private Label lblMakeHolePaintName;
        private TextBox txtMakeHolePaintName;
        private const string AppUiFontName = "Meiryo UI";
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
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
                return;
            EnsureCheckBalloonButton();
            EnsureDimHoleButton();
            EnsureMakeHolePaintNameControls();
            ApplyUnifiedTypography(this);
            ApplyDeleteButtonIcons();
            ApplyModelPropsActionIcons();
            ApplyModelTypography();
            SelectModelSideTab("Props");
            InitMakeHoleOptions();
            ApplyButtonIcons();
            ApplyDrawingUiStyles();
            WireEvents();
            InitBomCommandToolTips();
            InitComponentDrawingTimer();
            LayoutDrawingBomTab();
            LayoutModelCommandButtons();
            LayoutMakeHoleOptions();
            InitMakeHoleUpdateMonitor();

            lblTitle.Text = "DRAWING BOM";
            lblStatus.Text = "Dang cho ket noi...";
            ApplyReadableContrast();
            UpdateBomCommandButtonState();
        }

        public void Init(ISldWorks app)
        {
            swApp = app;
            solidWorksClosing = false;
            StartComponentDrawingTimer();
            bomLoader = new BomLoader(swApp);
            drawingViewRotator = new XoayDrawingView(swApp);
            drawingViewFitter = new ChinhTiLeDrawingView(swApp);
            drawingDimensionGenerator = new TaoDimKegaki(swApp);
            holeDimensionCommand = new DimKichThuocLo(swApp);
            sectionEdgeDimensionCommand = new LenhDimCanhSongSong(swApp);
            xepUnitDrawing = new XepUnitDrawing(swApp);
            makeHoleCommand = new LenhMakeHole(swApp);
            paintHoleSummaryCommand = new PaintHoleSummaryCommand(swApp);
            drawingTextAnnotationCommands = new LenhNoteTextBalloon(
                swApp,
                this,
                cboBendLine,
                cboSide,
                cboBalloonProperty);
            balloonChecker = new CheckBalloon(swApp);
            actions = new ThaoTacBomTaskPane(swApp, bomLoader, dgvModelBom, chkSelectAll, lblStatus, progressCheck, this);
            actions.ConfigureGrid();
            AttachSolidWorksEvents();
            SwitchTabByActiveDocument();
            if (IsActiveModelDocument())
                LoadModelPropsFromActiveDocument(false);
            LayoutComponentViewSize();
            LayoutModelCommandButtons();
            LayoutMakeHoleOptions();

            lblStatus.Text = swApp != null
                ? "Da ket noi SOLIDWORKS"
                : "Dang cho ket noi...";
            ApplyReadableContrast();
            ScheduleInitialTaskPaneLayout();
        }

        public void ShutdownFromSolidWorks()
        {
            solidWorksClosing = true;
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
            drawingTextAnnotationCommands = null;
            balloonChecker = null;
            xepUnitDrawing = null;
            makeHoleCommand = null;
            paintHoleSummaryCommand = null;
            swApp = null;
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
            if (taskPaneHostControl == null || taskPaneHostControl.IsDisposed)
                return;

            Size hostSize = taskPaneHostControl.ClientSize;
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
            else
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
            if (!IsHandleCreated || IsDisposed)
                return;

            BeginInvoke((Action)(() =>
            {
                ForceTaskPaneLayout();
                BeginInvoke((Action)ForceTaskPaneLayout);
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
            initialLayoutPassesRemaining = 12;
            initialLayoutTimer.Start();
        }

        private void InitialLayoutTimer_Tick(object sender, EventArgs e)
        {
            if (IsDisposed || !Visible || initialLayoutPassesRemaining-- <= 0)
            {
                initialLayoutTimer?.Stop();
                return;
            }

            SyncSizeWithTaskPaneHost();
            ForceTaskPaneLayout();
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
                panelModelCommands?.PerformLayout();
                LayoutDrawingBomTab();
                LayoutModelCommandButtons();
                LayoutMakeHoleOptions();
                LayoutComponentViewSize();
                ApplyReadableContrast();
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

        private void EnsureDimHoleButton()
        {
            if (btnDimKichThuocLo != null)
                return;

            btnDimKichThuocLo = new Button();
            btnDimKichThuocLo.Name = "btnDimKichThuocLo";
            btnDimKichThuocLo.Text = "Dim kich\r\nthuoc lo";
            btnDimKichThuocLo.Size = new Size(126, 44);
            btnDimKichThuocLo.TabIndex = 6;
            btnDimKichThuocLo.UseVisualStyleBackColor = false;

            if (groupBox3 != null && !groupBox3.Controls.Contains(btnDimKichThuocLo))
                groupBox3.Controls.Add(btnDimKichThuocLo);
        }

        private void EnsureCheckBalloonButton()
        {
            if (btnCheckBalloon != null)
                return;

            btnCheckBalloon = new Button();
            btnCheckBalloon.Name = "btnCheckBalloon";
            btnCheckBalloon.Text = "CHECK\r\nBALLOON";
            btnCheckBalloon.Size = new Size(126, 44);
            btnCheckBalloon.TabIndex = 7;
            btnCheckBalloon.UseVisualStyleBackColor = false;

            if (tabDrawingBom != null && !tabDrawingBom.Controls.Contains(btnCheckBalloon))
                tabDrawingBom.Controls.Add(btnCheckBalloon);
        }

        private void WireEvents()
        {
            EnsureCheckBalloonButton();
            btnLoadBom.Click += btnLoadBom_Click;
            btnClearBom.Click += btnClearBom_Click;
            btnCheckDfTk.Click += btnCheckDfTk_Click;
            btnCheckUraOmote.Click += btnCheckUraOmote_Click;
            btnCheckKegaki.Click += btnCheckKegaki_Click;
            button1.Click += cancel_Click;
            btnGetWL.Click += btnGetWL_Click;
            btnNote.Click += btnNote_Click;
            btnNote.MouseUp += btnNote_MouseUp;
            btnText.Click += btnText_Click;
            btnInsertBalloon.Click += btnInsertBalloon_Click;
            btnCheckBalloon.Click += btnCheckBalloon_Click;
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
            btnDimMatCat.Click += btnDimMatCat_Click;
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
            WireXepUnitButton(this);
            chkSelectAll.CheckedChanged += chkSelectAll_CheckedChanged;

            dgvModelBom.CurrentCellDirtyStateChanged += dgvModelBom_CurrentCellDirtyStateChanged;
            dgvModelBom.CellMouseDown += dgvModelBom_CellMouseDown;
            dgvModelBom.CellContentClick += dgvModelBom_CellContentClick;
            dgvModelBom.KeyDown += dgvModelBom_KeyDown;
            dgvModelBom.SizeChanged += dgvModelBom_SizeChanged;
            tabComponentDrawing.Resize += tabComponentDrawing_Resize;
            pnlMakeHoleDiagram.Paint += pnlMakeHoleDiagram_Paint;
            Resize += BomTaskPaneControl_Resize;
            panelModelCommands.SizeChanged += PanelModelCommands_SizeChanged;
            Disposed += BomTaskPaneControl_Disposed;
        }

        private void ApplyDeleteButtonIcons()
        {
            ApplyDeleteButtonIcon(btnDeleteNote);
            ApplyDeleteButtonIcon(btnDeleteText);
        }

        private void ApplyDeleteButtonIcon(Button button)
        {
            if (button == null)
                return;

            button.Text = "";
            button.Image = CreateTrashIcon(16);
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.TextImageRelation = TextImageRelation.Overlay;
            button.AccessibleName = "Delete";
        }

        private Bitmap CreateTrashIcon(int size)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(Color.SeaGreen, 1.7f))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                float x = 4.0f;
                float y = 5.0f;
                float w = size - 8.0f;
                float h = size - 7.0f;

                graphics.DrawLine(pen, x - 1.0f, y, x + w + 1.0f, y);
                graphics.DrawLine(pen, x + 2.0f, y - 3.0f, x + w - 2.0f, y - 3.0f);
                graphics.DrawLine(pen, x + 4.0f, y - 4.0f, x + w - 4.0f, y - 4.0f);
                graphics.DrawRectangle(pen, x, y + 1.0f, w, h);
                graphics.DrawLine(pen, x + 3.0f, y + 3.0f, x + 3.0f, y + h - 2.0f);
                graphics.DrawLine(pen, x + w - 3.0f, y + 3.0f, x + w - 3.0f, y + h - 2.0f);
            }

            return bitmap;
        }

        private void ApplyButtonIcons()
        {
            try
            {
                string baseDir = Path.GetDirectoryName(typeof(BomTaskPaneControl).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
                string imagesDir = Path.Combine(baseDir, "Images");

                SetButtonImageIfExists(btnLoadBom, Path.Combine(imagesDir, "load.png"));
                SetButtonImageIfExists(btnClearBom, Path.Combine(imagesDir, "clear.png"));
                SetButtonImageIfExists(btnCheckDfTk, Path.Combine(imagesDir, "check.png"));
                SetButtonImageIfExists(btnCheckUraOmote, Path.Combine(imagesDir, "check.png"));
                SetButtonImageIfExists(btnCheckKegaki, Path.Combine(imagesDir, "check.png"));
                SetButtonImageIfExists(btnGetWL, Path.Combine(imagesDir, "getwl.png"));
                SetButtonImageIfExists(btnNote, Path.Combine(imagesDir, "note.png"));
                SetButtonImageIfExists(btnText, Path.Combine(imagesDir, "text.png"));
                SetButtonImageIfExists(btnInsertBalloon, Path.Combine(imagesDir, "balloon.png"));
                SetButtonImageIfExists(dimvang, Path.Combine(imagesDir, "dimvang.png"));
                SetButtonImageIfExists(btnFixScale, Path.Combine(imagesDir, "fixscale.png"));
                SetButtonImageIfExists(btnDimKegaki, Path.Combine(imagesDir, "dimkegaki.png"));
                SetButtonImageIfExists(btnDimKichThuocLo, Path.Combine(imagesDir, "dimmatcat.png"));

                SetModelCommandImageIfExists(btnMakeHole, Path.Combine(imagesDir, "makehole.png"));
                SetModelCommandImageIfExists(btnRepairHole, Path.Combine(imagesDir, "repairhole.png"));
                SetModelCommandImageIfExists(btnPaintHoleSummary, Path.Combine(imagesDir, "counthole.png"));
                ApplyModelCommandButtonStyles();
                SetButtonImageIfExists(btnDimMatCat, Path.Combine(imagesDir, "dimmatcat.png"));
            }
            catch
            {
                // ignore failures, icons are optional
            }
        }

        private void SetButtonImageIfExists(Button button, string path)
        {
            if (button == null || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try
            {
                using (Image img = Image.FromFile(path))
                {
                    button.Image = new Bitmap(img);
                }

                button.ImageAlign = ContentAlignment.MiddleLeft;
                button.TextImageRelation = TextImageRelation.ImageBeforeText;
            }
            catch
            {
                // ignore image load errors
            }
        }

        private void SetModelCommandImageIfExists(Button button, string path)
        {
            if (button == null || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try
            {
                using (Image img = Image.FromFile(path))
                {
                    button.Image = new Bitmap(img);
                }

                button.ImageAlign = ContentAlignment.TopCenter;
                button.TextAlign = ContentAlignment.BottomCenter;
                button.TextImageRelation = TextImageRelation.ImageAboveText;
            }
            catch
            {
                // ignore image load errors
            }
        }

        private void ApplyModelCommandButtonStyles()
        {
            ApplyModelCommandButtonStyle(
                btnMakeHole,
                Color.FromArgb(238, 249, 242),
                Color.FromArgb(132, 183, 150),
                Color.FromArgb(226, 244, 233),
                Color.FromArgb(210, 235, 220));
            ApplyModelCommandButtonStyle(
                btnRepairHole,
                Color.FromArgb(239, 246, 255),
                Color.FromArgb(126, 165, 210),
                Color.FromArgb(226, 239, 253),
                Color.FromArgb(210, 229, 248));
            ApplyModelCommandButtonStyle(
                btnPaintHoleSummary,
                Color.FromArgb(255, 248, 229),
                Color.FromArgb(204, 176, 102),
                Color.FromArgb(255, 241, 205),
                Color.FromArgb(248, 228, 183));
        }

        private void ApplyDrawingUiStyles()
        {
            Color pageBack = Color.FromArgb(250, 251, 253);
            Color titleColor = Color.FromArgb(229, 83, 12);
            Color textColor = Color.FromArgb(18, 22, 28);

            tabDrawingBom.BackColor = pageBack;
            tabDrawingBom.UseVisualStyleBackColor = false;
            lblTitle.Font = CreateUiFont(9.25F, FontStyle.Bold);
            lblTitle.ForeColor = titleColor;
            lblStatus.Font = CreateUiFont(8.75F, FontStyle.Bold);
            lblStatus.ForeColor = textColor;
            chkSelectAll.Font = CreateUiFont(8.75F, FontStyle.Bold);
            chkSelectAll.ForeColor = textColor;
            StyleBomTopButton(btnCheckDfTk);
            StyleBomTopButton(button2);
            StyleBomTopButton(btnCheckUraOmote);
            StyleBomTopButton(btnCheckKegaki);
            StyleBomTopButton(btnCheckBalloon);
            StyleToolButton(btnLoadBom, Color.FromArgb(246, 249, 252), Color.FromArgb(186, 200, 216), Color.FromArgb(234, 242, 250), textColor);
            StyleToolButton(btnClearBom, Color.FromArgb(255, 246, 246), Color.FromArgb(214, 158, 158), Color.FromArgb(255, 235, 235), textColor);
            StyleToolButton(button1, Color.FromArgb(246, 247, 249), Color.FromArgb(197, 204, 213), Color.FromArgb(235, 240, 246), textColor);

            tabComponentDrawing.BackColor = pageBack;
            tabComponentDrawing.UseVisualStyleBackColor = false;
            grpComponentSize.ForeColor = titleColor;
            grpComponentBom.ForeColor = titleColor;
            groupBox1.ForeColor = titleColor;
            groupBox2.ForeColor = titleColor;
            groupBox3.ForeColor = titleColor;
            groupBox3.Text = "Macro";
            groupBox1.Text = "Width";
            groupBox2.Text = "Length";

            grpComponentSize.Font = CreateUiFont(8.75F, FontStyle.Bold);
            grpComponentBom.Font = CreateUiFont(8.75F, FontStyle.Bold);
            groupBox1.Font = CreateUiFont(8.5F, FontStyle.Bold);
            groupBox2.Font = CreateUiFont(8.5F, FontStyle.Bold);
            groupBox3.Font = CreateUiFont(8.75F, FontStyle.Bold);

            txtWidth.Font = CreateUiFont(9.0F, FontStyle.Bold);
            txtLength.Font = CreateUiFont(9.0F, FontStyle.Bold);
            txtWidth.ForeColor = textColor;
            txtLength.ForeColor = textColor;

            StyleToolButton(btnGetWL, Color.FromArgb(244, 248, 253), Color.FromArgb(173, 193, 216), Color.FromArgb(232, 241, 252), Color.FromArgb(28, 65, 105));
            StyleToolButton(btnHorizontalAlignment, Color.FromArgb(246, 247, 249), Color.FromArgb(197, 204, 213), Color.FromArgb(235, 240, 246), Color.FromArgb(42, 53, 66));
            StyleToolButton(btnRotateCw, Color.FromArgb(246, 247, 249), Color.FromArgb(197, 204, 213), Color.FromArgb(235, 240, 246), Color.FromArgb(42, 53, 66));
            StyleToolButton(btnRotateCcw, Color.FromArgb(246, 247, 249), Color.FromArgb(197, 204, 213), Color.FromArgb(235, 240, 246), Color.FromArgb(42, 53, 66));
            StyleToolButton(btnNote, Color.FromArgb(246, 249, 252), Color.FromArgb(186, 200, 216), Color.FromArgb(234, 242, 250), Color.FromArgb(28, 72, 112));
            StyleToolButton(btnText, Color.FromArgb(246, 249, 252), Color.FromArgb(186, 200, 216), Color.FromArgb(234, 242, 250), Color.FromArgb(28, 72, 112));
            StyleToolButton(btnInsertBalloon, Color.FromArgb(246, 249, 252), Color.FromArgb(186, 200, 216), Color.FromArgb(234, 242, 250), Color.FromArgb(28, 72, 112));
            StyleIconOnlyButton(btnDeleteNote);
            StyleIconOnlyButton(btnDeleteText);

            StyleMacroButton(dimvang, Color.FromArgb(201, 241, 211), Color.FromArgb(68, 154, 88), Color.FromArgb(183, 231, 196), Color.FromArgb(20, 102, 44));
            StyleMacroButton(btnDimMatCat, Color.FromArgb(255, 235, 158), Color.FromArgb(205, 154, 28), Color.FromArgb(255, 223, 125), Color.FromArgb(118, 78, 0));
            StyleMacroButton(btnDimKegaki, Color.FromArgb(219, 228, 255), Color.FromArgb(92, 119, 202), Color.FromArgb(202, 215, 252), Color.FromArgb(36, 66, 148));
            StyleMacroButton(btnDimKichThuocLo, Color.FromArgb(226, 244, 255), Color.FromArgb(76, 151, 204), Color.FromArgb(207, 235, 252), Color.FromArgb(22, 89, 142));
            StyleMacroButton(btnFixScale, Color.FromArgb(255, 215, 199), Color.FromArgb(204, 103, 70), Color.FromArgb(255, 199, 178), Color.FromArgb(139, 55, 30));

            if (dimvang != null)
                dimvang.Text = "Xoa DIM\r\nmau vang";
            if (btnDimMatCat != null)
                btnDimMatCat.Text = "Dim\r\nmat cat";
            if (btnDimKegaki != null)
                btnDimKegaki.Text = "Dim\r\nkegaki";
            if (btnDimKichThuocLo != null)
                btnDimKichThuocLo.Text = "Dim kich\r\nthuoc lo";
            if (btnFixScale != null)
                btnFixScale.Text = "Fix ti le";
        }

        private void StyleBomTopButton(Button button)
        {
            StyleToolButton(button, Color.FromArgb(235, 207, 244), Color.FromArgb(171, 96, 194), Color.FromArgb(225, 190, 238), Color.FromArgb(90, 34, 118));
            if (button != null)
            {
                button.Image = null;
                button.Font = CreateUiFont(8.75F, FontStyle.Bold);
                button.ImageAlign = ContentAlignment.MiddleLeft;
                button.TextAlign = ContentAlignment.MiddleCenter;
                button.TextImageRelation = TextImageRelation.Overlay;
                button.Padding = new Padding(3, 0, 3, 0);
            }
        }

        private void StyleToolButton(Button button, Color backColor, Color borderColor, Color hoverColor, Color textColor)
        {
            if (button == null)
                return;

            button.BackColor = backColor;
            button.ForeColor = textColor;
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = borderColor;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = hoverColor;
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(hoverColor, 0.05F);
            button.AutoEllipsis = false;
            button.UseCompatibleTextRendering = true;
            button.Font = CreateUiFont(8.75F, FontStyle.Bold);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextImageRelation = button.Image == null ? TextImageRelation.Overlay : TextImageRelation.ImageBeforeText;
            button.Padding = new Padding(4, 0, 4, 0);
        }

        private void StyleIconOnlyButton(Button button)
        {
            if (button == null)
                return;

            StyleToolButton(button, Color.FromArgb(248, 252, 249), Color.FromArgb(180, 207, 188), Color.FromArgb(235, 248, 239), Color.FromArgb(35, 43, 52));
            button.Text = "";
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.TextImageRelation = TextImageRelation.Overlay;
            button.Padding = new Padding(0);
        }

        private void StyleMacroButton(Button button, Color backColor, Color borderColor, Color hoverColor, Color textColor)
        {
            if (button == null)
                return;

            StyleToolButton(button, backColor, borderColor, hoverColor, textColor);
            button.Font = CreateUiFont(9.0F, FontStyle.Bold);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Padding = new Padding(8, 0, 8, 0);
        }

        private void ApplyModelCommandButtonStyle(Button button, Color backColor, Color borderColor, Color hoverColor, Color downColor)
        {
            if (button == null)
                return;

            button.BackColor = backColor;
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = borderColor;
            button.FlatAppearance.MouseOverBackColor = hoverColor;
            button.FlatAppearance.MouseDownBackColor = downColor;
            button.ForeColor = ControlPaint.Dark(borderColor, 0.55F);
            button.Font = CreateUiFont(9.0F, FontStyle.Bold);
            button.AutoEllipsis = false;
            button.UseCompatibleTextRendering = true;
            button.Padding = new Padding(0, 4, 0, 4);
            button.ImageAlign = ContentAlignment.TopCenter;
            button.TextAlign = ContentAlignment.BottomCenter;
            button.TextImageRelation = TextImageRelation.ImageAboveText;
            button.Margin = new Padding(0);
            button.TabStop = false;
        }

        private void ApplyModelPropsActionIcons()
        {
            if (btnModelApplyProps != null)
                btnModelApplyProps.Image = CreateCheckIcon(28);
            if (btnModelResetProps != null)
                btnModelResetProps.Image = CreateRefreshIcon(28, false);
            if (btnModelUpdateProps != null)
                btnModelUpdateProps.Image = CreateRefreshIcon(28, true);

            ConfigureModelPropsActionButton(btnModelApplyProps);
            ConfigureModelPropsActionButton(btnModelResetProps);
            ConfigureModelPropsActionButton(btnModelUpdateProps);
        }

        private void ConfigureModelPropsActionButton(Button button)
        {
            if (button == null)
                return;

            button.ImageAlign = ContentAlignment.TopCenter;
            button.TextAlign = ContentAlignment.BottomCenter;
            button.TextImageRelation = TextImageRelation.ImageAboveText;
            button.ForeColor = Color.FromArgb(28, 65, 105);
            button.Font = CreateUiFont(9.0F, FontStyle.Bold);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 244, 250);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(224, 235, 246);
            button.UseVisualStyleBackColor = false;
            button.BackColor = Color.Transparent;
            button.Padding = new Padding(0, 4, 0, 3);
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
        private void ApplyModelTypography()
        {
            Font uiFont = CreateUiFont(9.0F, FontStyle.Regular);
            Font inputFont = CreateUiFont(9.0F, FontStyle.Bold);
            Font labelFont = CreateUiFont(8.75F, FontStyle.Bold);
            Font groupFont = CreateUiFont(9.0F, FontStyle.Bold);
            Color labelColor = Color.FromArgb(229, 83, 12);
            Color textColor = Color.FromArgb(18, 22, 28);

            panelModelCommands.Font = uiFont;
            panelModelProps.Font = uiFont;
            grpMakeHoleOptions.Font = groupFont;
            grpMakeHoleOptions.ForeColor = labelColor;

            Label[] labels =
            {
                lblModelName,
                lblModelMaterial,
                lblModelThickness,
                lblModelGoban,
                lblModelQty,
                lblModelFinish,
                lblMakeHoleDirection,
                lblMakeHoleEdgeOffset,
                lblMakeHoleLeftOffset,
                lblMakeHoleRightOffset,
                lblMakeHolePitch,
                lblRepairHoleDiameter,
                lblMakeHolePaintName
            };
            foreach (Label label in labels)
            {
                if (label == null)
                    continue;

                label.Font = labelFont;
                label.ForeColor = labelColor;
            }

            Control[] textControls =
            {
                txtModelName,
                txtModelMaterial,
                txtModelThickness,
                txtModelGoban,
                txtModelQty,
                txtModelFinish,
                txtMakeHoleEdgeOffset,
                txtMakeHoleLeftOffset,
                txtMakeHoleRightOffset,
                txtMakeHolePitch,
                cboMakeHoleDirection,
                cboRepairHoleDiameter,
                chkMakeHolePaint,
                txtMakeHolePaintName
            };
            foreach (Control control in textControls)
            {
                if (control == null)
                    continue;

                control.Font = inputFont;
                control.ForeColor = textColor;
            }

            if (btnMakeHole != null)
                btnMakeHole.Text = "Make Hole";
            if (btnRepairHole != null)
                btnRepairHole.Text = "Repair Hole";
            if (btnPaintHoleSummary != null)
                btnPaintHoleSummary.Text = "Dem hole";
        }

        private void ApplyReadableContrast()
        {
            if (btnCheckDfTk != null)
                btnCheckDfTk.Text = "CHECK\r\nDF/TK";
            if (button2 != null)
                button2.Text = "XEP\r\nUNIT";
            if (btnCheckUraOmote != null)
                btnCheckUraOmote.Text = "CHECK\r\nウラ表";
            if (btnCheckKegaki != null)
                btnCheckKegaki.Text = "CHECK\r\nKEGAKI";

            if (btnLoadBom != null)
                btnLoadBom.Text = "CAP NHAT";
            if (btnClearBom != null)
                btnClearBom.Text = "XOA BANG";
            if (button1 != null)
                button1.Text = "CANCEL";

            StyleBomTopButton(btnCheckDfTk);
            StyleBomTopButton(button2);
            StyleBomTopButton(btnCheckUraOmote);
            StyleBomTopButton(btnCheckKegaki);
        }

        private Font CreateUiFont(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font(AppUiFontName, size, style, GraphicsUnit.Point, 128);
        }

        private void ApplyUnifiedTypography(Control root)
        {
            if (root == null)
                return;

            root.Font = CreateUiFont(9.0F);
            root.ForeColor = Color.FromArgb(18, 22, 28);

            foreach (Control child in root.Controls)
            {
                ApplyUnifiedTypography(child);
            }

            if (root is DataGridView grid)
            {
                grid.Font = CreateUiFont(8.75F);
                grid.ColumnHeadersDefaultCellStyle.Font = CreateUiFont(8.75F, FontStyle.Bold);
                grid.RowHeadersDefaultCellStyle.Font = CreateUiFont(8.75F);
                grid.DefaultCellStyle.Font = CreateUiFont(8.75F);
            }
            else if (root is TabControl || root is TabPage || root is GroupBox)
            {
                root.Font = CreateUiFont(9.0F, FontStyle.Bold);
            }
            else if (root is Button)
            {
                root.Font = CreateUiFont(8.75F, FontStyle.Bold);
            }
            else if (root is Label)
            {
                root.Font = CreateUiFont(8.75F, FontStyle.Bold);
            }
        }

        private Bitmap CreateCheckIcon(int size)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(Color.Black, 3.0f))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                graphics.DrawLines(pen, new[]
                {
                    new PointF(size * 0.20f, size * 0.55f),
                    new PointF(size * 0.42f, size * 0.78f),
                    new PointF(size * 0.82f, size * 0.22f)
                });
            }

            return bitmap;
        }

        private Bitmap CreateRefreshIcon(int size, bool clockwise)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(Color.Black, 3.0f))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                RectangleF rect = new RectangleF(5, 5, size - 10, size - 10);
                graphics.DrawArc(pen, rect, clockwise ? 35 : 210, 285);

                PointF p1 = clockwise
                    ? new PointF(size * 0.78f, size * 0.35f)
                    : new PointF(size * 0.22f, size * 0.65f);
                PointF p2 = clockwise
                    ? new PointF(size * 0.90f, size * 0.35f)
                    : new PointF(size * 0.10f, size * 0.65f);
                PointF p3 = clockwise
                    ? new PointF(size * 0.82f, size * 0.52f)
                    : new PointF(size * 0.18f, size * 0.48f);
                graphics.DrawLines(pen, new[] { p1, p2, p3 });
            }

            return bitmap;
        }

        private void btnLoadBom_Click(object sender, EventArgs e)
        {
            RunDrawingBomCommand(() =>
            {
                actions?.LoadBom(IsDrawingBomCancelRequested);
                UpdateBomCommandButtonState();
                RefreshHostedTaskPane();
            }, false);
        }

        private void btnClearBom_Click(object sender, EventArgs e)
        {
            actions?.ClearBom();
            UpdateBomCommandButtonState();
            RefreshHostedTaskPane();
        }

        private void UpdateBomCommandButtonState()
        {
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
            btnCheckUraOmote.Enabled = detailBomLoaded;
            btnCheckKegaki.Enabled = detailBomLoaded;
            btnCheckBalloon.Enabled = hasBomRows;
        }

        private void InitBomCommandToolTips()
        {
            bomCommandToolTipFont = new Font(
                AppUiFontName,
                9.0f,
                FontStyle.Regular,
                GraphicsUnit.Point,
                128);
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
                "BOM chi ti\u1EBFt: so s\u00E1nh d\u1EEF li\u1EC7u gi\u1EEFa Default v\u00E0 tr\u1EA1ng th\u00E1i tr\u1EA3i.");
            SetBomCommandToolTip(
                button2,
                "BOM UNIT: s\u1EAFp x\u1EBFp 部品番号 theo th\u1EE9 t\u1EF1 t\u0103ng d\u1EA7n.");
            SetBomCommandToolTip(
                btnCheckUraOmote,
                "BOM chi ti\u1EBFt: ki\u1EC3m tra v\u1ECB tr\u00ED m\u1EB7t m\u00E0u h\u1ED3ng gi\u1EEFa Default v\u00E0 Flat-Pattern.");
            SetBomCommandToolTip(
                btnCheckKegaki,
                "BOM chi ti\u1EBFt: ki\u1EC3m tra Bend Table chung v\u00E0 setting ri\u00EAng c\u1EE7a t\u1EEBng c\u1EA1nh b\u1EBB.");
            SetBomCommandToolTip(
                btnCheckBalloon,
                "Qu\u00E9t to\u00E0n b\u1ED9 sheet/view v\u00E0 ki\u1EC3m tra m\u1ED7i component instance c\u00F3 \u0111\u00FAng m\u1ED9t Balloon.");

            // Disabled WinForms controls do not raise hover events. Listen on the
            // parent as well so the description remains available while buttons are dimmed.
            tabDrawingBom.MouseMove += tabDrawingBom_BomCommandToolTipMouseMove;
            tabDrawingBom.MouseLeave += tabDrawingBom_BomCommandToolTipMouseLeave;
        }

        private void SetBomCommandToolTip(Control control, string description)
        {
            if (control == null || bomCommandToolTip == null)
                return;

            bomCommandToolTip.SetToolTip(control, description);
        }

        private string GetBomCommandToolTipText(Control control)
        {
            if (control == button2)
            {
                return "H\u00E3y click v\u00E0o b\u1EA3ng BOM UNIT v\u00E0 b\u1EA5m C\u1EACP NH\u1EACT\n\u0111\u1EC3 th\u1EF1c hi\u1EC7n thao t\u00E1c l\u1EC7nh.";
            }
            if (control == btnCheckDfTk || control == btnCheckUraOmote ||
                control == btnCheckKegaki || control == btnCheckBalloon)
            {
                return "H\u00E3y click v\u00E0o b\u1EA3ng BOM chi ti\u1EBFt v\u00E0 b\u1EA5m C\u1EACP NH\u1EACT\n\u0111\u1EC3 th\u1EF1c hi\u1EC7n thao t\u00E1c l\u1EC7nh.";
            }

            return "";
        }

        private void bomCommandToolTip_Popup(object sender, PopupEventArgs e)
        {
            if (bomCommandToolTipFont == null)
                return;

            string text = e.AssociatedControl == tabDrawingBom
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
            Control[] commandButtons = { btnCheckDfTk, button2, btnCheckUraOmote, btnCheckKegaki, btnCheckBalloon };

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

        private void btnXepUnit_Click(object sender, EventArgs e)
        {
            RunDrawingBomCommand(() =>
                xepUnitDrawing?.Run(dgvModelBom, BeginProgress, UpdateProgress, FinishProgress,
                    IsDrawingBomCancelRequested));
        }

        private void btnMakeHole_Click(object sender, EventArgs e)
        {
            SetMakeHolePanelMode(false);
            cboMakeHoleDirection.Focus();
        }

        private void btnRepairHole_Click(object sender, EventArgs e)
        {
            SetMakeHolePanelMode(true);
            cboRepairHoleDiameter.Focus();
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
            if (paintHoleSummaryCommand == null)
                paintHoleSummaryCommand = new PaintHoleSummaryCommand(swApp);

            paintHoleSummaryCommand.Run();
        }

        private void btnDimMatCat_Click(object sender, EventArgs e)
        {
            sectionEdgeDimensionCommand?.Run();
        }

        private void btnMakeHoleAccept_Click(object sender, EventArgs e)
        {
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
            if (makeHoleCommand == null)
                return;

            makeHoleCommand.PatternPendingHoleWizard();
            UpdateMakeHolePatternButton();
        }

        private void btnMakeHoleReset_Click(object sender, EventArgs e)
        {
            makeHoleCommand?.ResetPendingCommand();
            UpdateMakeHolePatternButton();
        }

        private void btnMakeHoleUpdate_Click(object sender, EventArgs e)
        {
            if (makeHoleCommand == null)
                makeHoleCommand = new LenhMakeHole(swApp);

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

            btnMakeHolePattern.Visible = !repairHolePanelMode && makeHoleCommand != null && makeHoleCommand.HasPendingHybridPattern;
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
            SetMakeHolePanelMode(false);
            // Keep the command area compact on startup. The options are shown
            // only after the user chooses Make Hole or Repair Hole.
            grpMakeHoleOptions.Visible = false;
        }

        private void SetMakeHolePanelMode(bool repairMode)
        {
            repairHolePanelMode = repairMode;

            grpMakeHoleOptions.Visible = true;
            grpMakeHoleOptions.BringToFront();
            grpMakeHoleOptions.Text = repairMode ? "Repair Hole" : "Make Hole";

            bool makeMode = !repairMode;
            lblRepairHoleDiameter.Text = "Hole Dia";
            lblRepairHoleDiameter.Visible = repairMode;
            cboRepairHoleDiameter.Visible = repairMode;
            lblMakeHoleDirection.Visible = makeMode;
            cboMakeHoleDirection.Visible = makeMode;
            lblMakeHoleEdgeOffset.Visible = makeMode;
            txtMakeHoleEdgeOffset.Visible = makeMode;
            lblMakeHoleLeftOffset.Visible = makeMode;
            txtMakeHoleLeftOffset.Visible = makeMode;
            lblMakeHoleRightOffset.Visible = makeMode;
            txtMakeHoleRightOffset.Visible = makeMode;
            lblMakeHolePitch.Visible = makeMode;
            txtMakeHolePitch.Visible = makeMode;
            chkMakeHolePaint.Visible = makeMode;
            if (lblMakeHolePaintName != null) lblMakeHolePaintName.Visible = makeMode;
            if (txtMakeHolePaintName != null) txtMakeHolePaintName.Visible = makeMode;
            btnMakeHoleReset.Visible = makeMode;
            btnMakeHoleUpdate.Visible = makeMode;
            btnMakeHolePattern.Visible = makeMode && makeHoleCommand != null && makeHoleCommand.HasPendingHybridPattern;

            btnMakeHoleAccept.Text = repairMode ? "Repair" : "Accept";

            LayoutMakeHoleOptions();
            UpdateMakeHolePaintNameState();
            UpdateMakeHolePatternButton();
        }

        private void MakeHoleTrackedInputChanged(object sender, EventArgs e)
        {
            SetMakeHoleUpdateButtonState(IsCurrentMakeHoleUpdateRequired());
        }

        private void LayoutMakeHoleOptions()
        {
            if (grpMakeHoleOptions == null || panelModelCommands == null)
                return;

            int availableWidth = Math.Max(230, panelModelCommands.ClientSize.Width - grpMakeHoleOptions.Left - 18);
            bool compact = availableWidth < 350;
            int groupWidth = availableWidth;
            int inputWidth = compact ? Math.Max(120, groupWidth - 112) : 118;
            int previewWidth = Math.Max(170, groupWidth - 32);

            grpMakeHoleOptions.Width = groupWidth;
            pnlMakeHoleDiagram.Width = previewWidth;

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

                if (repairHolePanelMode)
                {
                    LayoutStackedField(lblRepairHoleDiameter, cboRepairHoleDiameter, innerLeft, labelY, innerWidth);
                    labelY += rowGap;
                }

                chkMakeHolePaint.Location = new Point(innerLeft, labelY - 2);
                LayoutStackedField(lblMakeHolePaintName, txtMakeHolePaintName, innerLeft, labelY + 24, innerWidth);

                int buttonTop = labelY + 74;
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
                int labelWidth = 76;
                int leftLabelX = innerLeft;
                int leftInputX = leftLabelX + labelWidth;
                int rightLabelX = innerLeft + columnWidth + columnGap;
                int rightInputX = rightLabelX + labelWidth;
                int fieldWidth = Math.Max(54, columnWidth - labelWidth);

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

                if (repairHolePanelMode)
                {
                    lblRepairHoleDiameter.Location = new Point(rightLabelX, 212);
                    cboRepairHoleDiameter.Location = new Point(rightInputX, 209);
                    cboRepairHoleDiameter.Width = fieldWidth;
                }

                chkMakeHolePaint.Location = new Point(rightLabelX, 214);
                lblMakeHolePaintName.Location = new Point(leftLabelX, 244);
                txtMakeHolePaintName.Location = new Point(leftInputX, 241);
                txtMakeHolePaintName.Width = Math.Max(80, innerWidth - labelWidth);

                int buttonGap = 12;
                int buttonCount = 4;
                int buttonWidth = (innerWidth - buttonGap * (buttonCount - 1)) / buttonCount;
                int buttonTop = repairHolePanelMode ? 256 : 276;
                btnMakeHoleReset.Location = new Point(innerLeft, buttonTop);
                btnMakeHoleReset.Width = buttonWidth;
                btnMakeHolePattern.Location = new Point(innerLeft + (buttonWidth + buttonGap) * 1, buttonTop);
                btnMakeHolePattern.Width = buttonWidth;
                btnMakeHoleAccept.Location = new Point(innerLeft + (buttonWidth + buttonGap) * 2, buttonTop);
                btnMakeHoleAccept.Width = buttonWidth;
                btnMakeHoleUpdate.Location = new Point(innerLeft + (buttonWidth + buttonGap) * 3, buttonTop);
                btnMakeHoleUpdate.Width = buttonWidth;
                grpMakeHoleOptions.Height = repairHolePanelMode ? 244 : 331;
            }

            if (repairHolePanelMode)
            {
                if (compact)
                {
                    int innerLeft = 16;
                    int innerWidth = Math.Max(170, groupWidth - 32);
                    LayoutStackedField(lblRepairHoleDiameter, cboRepairHoleDiameter, innerLeft, 148, innerWidth);
                    btnMakeHoleAccept.SetBounds(innerLeft, 206, Math.Min(140, innerWidth), 32);
                    grpMakeHoleOptions.Height = 260;
                }
                else
                {
                    int innerLeft = 16;
                    int innerWidth = groupWidth - 32;
                    btnMakeHoleAccept.Location = new Point(innerLeft, 180);
                    btnMakeHoleAccept.Width = Math.Min(140, innerWidth);
                }
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
            if (panelModelCommands == null || grpMakeHoleOptions == null)
                return;

            Button[] buttons =
            {
                btnMakeHole,
                btnRepairHole,
                btnPaintHoleSummary
            };

            int left = 18;
            int top = 16;
            int gap = panelModelCommands.ClientSize.Width < 340 ? 8 : 12;
            int buttonWidth = panelModelCommands.ClientSize.Width < 340 ? 84 : 96;
            int buttonHeight = panelModelCommands.ClientSize.Width < 340 ? 76 : 78;
            int maxRight = Math.Max(left + buttonWidth, panelModelCommands.ClientSize.Width - 18);
            int x = left;
            int y = top;
            int rowBottom = top;

            foreach (Button button in buttons)
            {
                if (button == null)
                    continue;

                button.Size = new Size(buttonWidth, buttonHeight);
                if (x > left && x + buttonWidth > maxRight)
                {
                    x = left;
                    y += buttonHeight + gap;
                }

                button.Location = new Point(x, y);
                rowBottom = Math.Max(rowBottom, y + buttonHeight);
                x += buttonWidth + gap;
            }

            grpMakeHoleOptions.Location = new Point(left, rowBottom + 16);
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
                if (repairMode)
                    return false;

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
                string message = repairHolePanelMode
                    ? "Hole Dia phai la so lon hon 0."
                    : "Hole Size phai la so lon hon 0 hoac dang AxB, vi du 4.2x25.";
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
                Paint = !repairHolePanelMode && chkMakeHolePaint.Checked,
                HoleSizeText = holeSizeText,
                PaintNameText = txtMakeHolePaintName != null ? txtMakeHolePaintName.Text : ""
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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.White);

            Rectangle area = pnlMakeHoleDiagram.ClientRectangle;
            if (area.Width < 120 || area.Height < 60)
                return;

            int partLeft = 14;
            int partRight = area.Width - 16;
            int partTop = 18;
            int partHeight = 36;
            int partBottom = partTop + partHeight;
            int holeY = partTop + partHeight / 2;
            int firstHoleX = partLeft + 22;
            int lastHoleX = partRight - 24;

            using (Pen partPen = new Pen(Color.Black, 1.5F))
            using (Pen dimPen = new Pen(Color.Black, 1.0F))
            using (Brush textBrush = new SolidBrush(Color.Black))
            using (Font smallFont = new Font("MS UI Gothic", 8F, FontStyle.Regular, GraphicsUnit.Point, 128))
            {
                Rectangle part = new Rectangle(partLeft, partTop, partRight - partLeft, partHeight);
                e.Graphics.DrawRectangle(partPen, part);

                int holeCount = 4;
                for (int i = 0; i < holeCount; i++)
                {
                    float x = firstHoleX + (lastHoleX - firstHoleX) * i / (float)(holeCount - 1);
                    e.Graphics.DrawEllipse(partPen, x - 3, holeY - 3, 6, 6);
                }

                int baseDimY = partBottom + 28;
                int shortDimY = partBottom + 17;

                e.Graphics.DrawLine(dimPen, partLeft, partBottom, partLeft, baseDimY + 4);
                e.Graphics.DrawLine(dimPen, partRight, partBottom, partRight, baseDimY + 4);
                e.Graphics.DrawLine(dimPen, firstHoleX, holeY, firstHoleX, shortDimY + 4);
                e.Graphics.DrawLine(dimPen, lastHoleX, holeY, lastHoleX, shortDimY + 4);

                DrawHorizontalDimLine(e.Graphics, dimPen, partLeft, shortDimY, firstHoleX, shortDimY);
                e.Graphics.DrawString("L", smallFont, textBrush, (partLeft + firstHoleX) / 2 - 4, shortDimY + 3);

                DrawHorizontalDimLine(e.Graphics, dimPen, firstHoleX, baseDimY, lastHoleX, baseDimY);
                e.Graphics.DrawString("C", smallFont, textBrush, (firstHoleX + lastHoleX) / 2 - 4, baseDimY + 3);

                DrawHorizontalDimLine(e.Graphics, dimPen, lastHoleX, shortDimY, partRight, shortDimY);
                e.Graphics.DrawString("R", smallFont, textBrush, (lastHoleX + partRight) / 2 - 4, shortDimY + 3);

                int xDimX = firstHoleX - 12;
                DrawVerticalDimLine(e.Graphics, dimPen, xDimX, partTop, xDimX, holeY);
                e.Graphics.DrawString("X", smallFont, textBrush, xDimX + 4, (partTop + holeY) / 2 - 5);

                e.Graphics.DrawLine(dimPen, firstHoleX, holeY, firstHoleX - 30, partTop + 7);
                e.Graphics.DrawString("H", smallFont, textBrush, firstHoleX - 25, partTop + 1);
            }
        }

        private void DrawHorizontalDimLine(Graphics graphics, Pen pen, int x1, int y1, int x2, int y2)
        {
            graphics.DrawLine(pen, x1, y1, x2, y2);
            graphics.DrawLine(pen, x1, y1 - 4, x1, y1 + 4);
            graphics.DrawLine(pen, x2, y2 - 4, x2, y2 + 4);
        }

        private void DrawVerticalDimLine(Graphics graphics, Pen pen, int x1, int y1, int x2, int y2)
        {
            graphics.DrawLine(pen, x1, y1, x2, y2);
            graphics.DrawLine(pen, x1 - 4, y1, x1 + 4, y1);
            graphics.DrawLine(pen, x2 - 4, y2, x2 + 4, y2);
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
            bool isProps = string.Equals(tabName, "Props", StringComparison.OrdinalIgnoreCase);

            if (tabModelPages == null)
                return;

            tabModelPages.SelectedTab = isProps ? tabModelPropsPage : tabModelEditPage;
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

        private void RunDrawingBomCommand(Action command, bool lockInput = true)
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
                if (showCanceledMessage)
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
                if (control == button1)
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
                lblStatus.Text = "Dang huy lenh...";
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
            drawingTextAnnotationCommands?.InsertNote();
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
            drawingTextAnnotationCommands?.InsertText();
        }

        private void btnInsertBalloon_Click(object sender, EventArgs e)
        {
            drawingTextAnnotationCommands?.InsertBalloon();
        }

        private void btnCheckBalloon_Click(object sender, EventArgs e)
        {
            RunDrawingBomCommand(() =>
            {
                BalloonCheckResult result = balloonChecker?.Run();
                if (result == null)
                    return;

                lblStatus.Text = result.IsOk
                    ? "CHECK BALLOON: OK - " + result.ValidCount + "/" + result.ExpectedCount
                    : "CHECK BALLOON: thieu " + result.MissingCount + ", trung " + result.DuplicateCount
                        + ", sai so " + result.WrongTextCount + ", dangling " + result.DanglingCount;

                result.ExportToExcel();
            });
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
            XoaDimMauVang cleaner = new XoaDimMauVang(swApp);
            cleaner.DeleteDanglingDimensions();
        }

        private void btnFixScale_Click(object sender, EventArgs e)
        {
            drawingViewFitter?.FitSelectedViewByAspectRule();
            LoadWidthLengthFromSelectedView(true);
        }

        private void btnDimKegaki_Click(object sender, EventArgs e)
        {
            drawingDimensionGenerator?.GenerateKegakiDimensions();
        }

        private void btnDimKichThuocLo_Click(object sender, EventArgs e)
        {
            holeDimensionCommand?.GenerateHoleDimensions();
        }

        private void WireXepUnitButton(Control parent)
        {
            if (parent == null)
                return;

            foreach (Control child in parent.Controls)
            {
                Button button = child as Button;
                if (button != null &&
                    NormalizeButtonText(button.Text).Contains("XEPUNIT"))
                {
                    button.Click -= btnXepUnit_Click;
                    button.Click += btnXepUnit_Click;
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
            LayoutMakeHoleOptions();
            ApplyReadableContrast();
        }

        private void tabComponentDrawing_Resize(object sender, EventArgs e)
        {
            LayoutComponentViewSize();
        }

        private void LayoutDrawingBomTab()
        {
            if (tabDrawingBom == null || dgvModelBom == null)
                return;

            tabDrawingBom.AutoScroll = true;

            int margin = 12;
            int pageWidth = Math.Max(220, tabDrawingBom.ClientSize.Width - margin * 2);
            int pageHeight = Math.Max(260, tabDrawingBom.ClientSize.Height);

            lblTitle.Location = new Point(margin, 16);
            lblStatus.Location = new Point(margin, 44);

            Button[] topButtons =
            {
                btnCheckDfTk,
                button2,
                btnCheckUraOmote,
                btnCheckKegaki,
                btnCheckBalloon
            };

            int topButtonGap = 8;
            int topButtonHeight = 44;
            int topButtonWidth = pageWidth < 310 ? 88 : 96;
            int maxTopColumns = Math.Max(1, (pageWidth + topButtonGap) / (topButtonWidth + topButtonGap));
            int topColumns = Math.Max(1, Math.Min(topButtons.Length, maxTopColumns));
            int topGridWidth = topColumns * topButtonWidth + (topColumns - 1) * topButtonGap;
            int desiredTopLeft = pageWidth >= 430 ? margin + 150 : margin + Math.Max(0, (pageWidth - topGridWidth) / 2);
            int maxTopLeft = margin + Math.Max(0, pageWidth - topGridWidth);
            int topLeft = Math.Min(desiredTopLeft, maxTopLeft);
            int topTop = pageWidth >= 430 ? 12 : 66;
            int topBottom = topTop;

            for (int i = 0; i < topButtons.Length; i++)
            {
                Button button = topButtons[i];
                if (button == null)
                    continue;

                int column = i % topColumns;
                int row = i / topColumns;
                int x = topLeft + column * (topButtonWidth + topButtonGap);
                int y = topTop + row * (topButtonHeight + topButtonGap);
                button.SetBounds(x, y, topButtonWidth, topButtonHeight);
                topBottom = Math.Max(topBottom, y + topButtonHeight);
            }

            int selectTop = Math.Max(86, topBottom + 12);
            chkSelectAll.Location = new Point(margin, selectTop);

            int progressTop = chkSelectAll.Bottom + 6;
            if (progressCheck != null)
                progressCheck.SetBounds(margin, progressTop, pageWidth, 16);

            int bottomButtonHeight = 32;
            int bottomTop = pageHeight - margin - bottomButtonHeight;
            if (bottomTop < selectTop + 170)
            {
                bottomTop = selectTop + 170;
            }

            int gridTop = progressTop + 16 + 8;
            int gridHeight = Math.Max(120, bottomTop - gridTop - 10);
            dgvModelBom.SetBounds(margin, gridTop, pageWidth, gridHeight);

            Button[] bottomButtons =
            {
                btnLoadBom,
                btnClearBom,
                button1
            };

            int bottomGap = 8;
            int bottomColumns = Math.Max(1, Math.Min(bottomButtons.Length, (pageWidth + bottomGap) / (92 + bottomGap)));
            int bottomButtonWidth = bottomColumns == 1
                ? Math.Min(120, pageWidth)
                : Math.Min(104, Math.Max(86, (pageWidth - bottomGap * (bottomColumns - 1)) / bottomColumns));
            int bottomGridWidth = bottomColumns * bottomButtonWidth + (bottomColumns - 1) * bottomGap;
            int bottomLeft = margin + Math.Max(0, (pageWidth - bottomGridWidth) / 2);

            for (int i = 0; i < bottomButtons.Length; i++)
            {
                Button button = bottomButtons[i];
                if (button == null)
                    continue;

                int column = i % bottomColumns;
                int row = i / bottomColumns;
                int x = bottomLeft + column * (bottomButtonWidth + bottomGap);
                int y = bottomTop + row * (bottomButtonHeight + bottomGap);
                button.SetBounds(x, y, bottomButtonWidth, bottomButtonHeight);
            }

            tabDrawingBom.AutoScrollMinSize = new Size(0, bottomTop + bottomButtonHeight + margin);
            actions?.AutoFitBomGrid();
            ApplyReadableContrast();
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
            if (solidWorksClosing)
                return;

            bool needUpdate = false;

            try
            {
                if (makeHoleCommand != null)
                {
                    bool cleaned = makeHoleCommand.CleanupTrackedMakeHoleEquationsIfFeatureMissing();

                    if (cleaned)
                    {
                        SetMakeHoleUpdateButtonState(false);
                        return;
                    }

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
            btnMakeHoleUpdate.Text = needUpdate ? "UPDATE HOLE" : "UPDATE HOLE";
        }

        private void HandleSolidWorksComClosed()
        {
            solidWorksClosing = true;
            StopComponentDrawingTimer();
            DetachSolidWorksEvents();
            swApp = null;
        }

        private void LayoutComponentViewSize()
        {
            if (grpComponentSize == null || btnGetWL == null)
                return;

            tabComponentDrawing.AutoScroll = true;

            int pageMargin = 6;
            int pageWidth = Math.Max(230, tabComponentDrawing.ClientSize.Width - (pageMargin * 2));
            int margin = 13;
            int gap = 10;
            bool narrow = pageWidth < 430;
            int buttonWidth = narrow ? 72 : 92;

            grpComponentSize.SetBounds(pageMargin, 6, pageWidth, narrow ? 190 : 148);

            int groupWidth = narrow
                ? Math.Max(90, grpComponentSize.ClientSize.Width - margin - buttonWidth - gap - 12)
                : Math.Max(160, grpComponentSize.ClientSize.Width - margin - buttonWidth - gap - 18);

            groupBox1.SetBounds(margin, 19, groupWidth, 42);
            groupBox2.SetBounds(margin, 65, groupWidth, 42);

            txtWidth.SetBounds(9, 17, Math.Max(40, groupBox1.ClientSize.Width - 18), 24);
            txtLength.SetBounds(9, 17, Math.Max(40, groupBox2.ClientSize.Width - 18), 24);

            int availableWidth = grpComponentSize.ClientSize.Width - (margin * 2);
            if (narrow)
            {
                btnGetWL.SetBounds(margin + groupWidth + gap, 19, buttonWidth, 88);

                int rotateWidth = Math.Max(70, (availableWidth - gap) / 2);
                btnHorizontalAlignment.SetBounds(margin, 111, availableWidth, 30);
                btnRotateCw.SetBounds(margin, 153, rotateWidth, 30);
                btnRotateCcw.SetBounds(margin + rotateWidth + gap, 153, rotateWidth, 30);
            }
            else
            {
                btnGetWL.SetBounds(margin + groupWidth + gap, 19, buttonWidth, 72);

                int horizontalWidth = Math.Min(173, Math.Max(120, availableWidth / 3));
                int rotateWidth = Math.Min(92, Math.Max(70, (availableWidth - horizontalWidth - (gap * 2)) / 2));

                btnHorizontalAlignment.SetBounds(margin, 112, horizontalWidth, 30);
                btnRotateCw.SetBounds(margin + horizontalWidth + gap, 111, rotateWidth, 30);
                btnRotateCcw.SetBounds(margin + horizontalWidth + gap + rotateWidth + gap, 111, rotateWidth, 30);
            }

            LayoutComponentTextGroup(pageMargin, pageWidth, gap, narrow);
            LayoutComponentMacroGroup(pageMargin, pageWidth, gap, narrow);
            tabComponentDrawing.AutoScrollMinSize = new System.Drawing.Size(0, groupBox3.Bottom + pageMargin);
        }

        private void LayoutComponentTextGroup(int pageMargin, int pageWidth, int gap, bool narrow)
        {
            int groupTop = grpComponentSize.Bottom + 6;
            grpComponentBom.SetBounds(pageMargin, groupTop, pageWidth, 139);

            int margin = 12;
            int buttonWidth = narrow ? 68 : 105;
            int deleteWidth = 28;

            if (narrow)
            {
                btnNote.Text = "Note";
                btnText.Text = "Text";
                btnInsertBalloon.Text = "Balloon";
            }
            else
            {
                btnNote.Text = "Note";
                btnText.Text = "Text";
                btnInsertBalloon.Text = "Insert Balloon";
            }

            int rightGap = narrow ? 6 : margin;
            int smallGap = narrow ? 6 : gap;
            int buttonX = Math.Max(margin + 80, grpComponentBom.ClientSize.Width - rightGap - buttonWidth);
            int deleteX = buttonX - smallGap - deleteWidth;
            int comboWidth = Math.Max(120, deleteX - margin - gap);

            cboBendLine.SetBounds(margin, 26, comboWidth, 24);
            cboSide.SetBounds(margin, 61, comboWidth, 24);
            cboBalloonProperty.SetBounds(margin, 96, comboWidth, 24);

            btnDeleteNote.SetBounds(deleteX, 23, deleteWidth, 28);
            btnDeleteText.SetBounds(deleteX, 58, deleteWidth, 28);
            btnNote.SetBounds(buttonX, 23, buttonWidth, 28);
            btnText.SetBounds(buttonX, 58, buttonWidth, 28);
            btnInsertBalloon.SetBounds(buttonX, 93, buttonWidth, 28);
        }

        private void LayoutComponentMacroGroup(int pageMargin, int pageWidth, int gap, bool narrow)
        {
            int groupTop = grpComponentBom.Bottom + 6;
            int minHeight = narrow ? 180 : 148;
            int remainingHeight = tabComponentDrawing.ClientSize.Height - groupTop - pageMargin;
            groupBox3.SetBounds(pageMargin, groupTop, pageWidth, Math.Max(minHeight, remainingHeight));

            ConfigureMacroButton(dimvang);
            ConfigureMacroButton(btnDimMatCat);
            ConfigureMacroButton(btnDimKegaki);
            ConfigureMacroButton(btnDimKichThuocLo);
            ConfigureMacroButton(btnFixScale);

            Button[] buttons =
            {
                dimvang,
                btnDimMatCat,
                btnDimKegaki,
                btnDimKichThuocLo,
                btnFixScale
            };

            int innerLeft = 12;
            int innerTop = 24;
            int innerGap = 10;
            int availableWidth = Math.Max(120, groupBox3.ClientSize.Width - (innerLeft * 2));
            int tileWidth = Math.Min(126, Math.Max(104, (availableWidth - innerGap) / 2));
            int columnCount = availableWidth >= (tileWidth * 2 + innerGap) ? 2 : 1;
            int buttonWidth = columnCount == 1 ? Math.Min(126, availableWidth) : tileWidth;
            int buttonHeight = 44;

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
        }

        private void ConfigureMacroButton(Button button)
        {
            if (button == null)
                return;

            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Padding = new Padding(8, 0, 8, 0);
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
                swEvents.ActiveDocChangeNotify += OnActiveDocChangeNotify;
        }

        private void DetachSolidWorksEvents()
        {
            if (swEvents == null)
                return;

            try
            {
                swEvents.ActiveDocChangeNotify -= OnActiveDocChangeNotify;
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
                KeepDrawingBomTabVisible();
                return;
            }

            ModelDoc2 model = swApp?.ActiveDoc as ModelDoc2;
            if (model == null)
            {
                SetModelCommandAvailability(false);
                return;
            }

            int docType = model.GetType();
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

        private void SetModelCommandAvailability(bool enabled)
        {
            if (panelModelCommands != null)
                panelModelCommands.Enabled = enabled;
        }

        private void btnGetWL_Click_1(object sender, EventArgs e)
        {

        }
    }
}
