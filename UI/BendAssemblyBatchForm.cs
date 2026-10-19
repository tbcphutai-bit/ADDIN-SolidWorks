using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Diagnostics;
using System.Windows.Forms;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendCollision;

namespace ADDIN.UI
{
    /// <summary>
    /// Professional UI Form for Check Chấn (Assembly Batch Check).
    /// Supports inspecting a single Part or entire Assembly batch with duplicate member parts.
    /// Designed for SolidWorks Add-in x64 (WinForms, .NET Framework 4.8, C# 7.3).
    /// </summary>
    internal partial class BendAssemblyBatchForm : Form
    {
        #region Public Events for Backend Hookup (Codex Integration)

        /// <summary>
        /// Raised when user clicks 'Run Batch Check'.
        /// Codex coordinator hooks this to start AssemblyNativeBatchOperation.
        /// </summary>
        public event EventHandler<AssemblyCheckScope> RunBatchRequested;

        /// <summary>
        /// Raised when user clicks 'Dừng' (Cancel).
        /// Codex coordinator hooks this to cancel the active operation.
        /// </summary>
        public event EventHandler CancelBatchRequested;

        /// <summary>
        /// Raised when user clicks 'Quét lại' (Refresh).
        /// </summary>
        public event EventHandler RefreshRequested;
        public event EventHandler PreviewRequested;
        public event EventHandler<AssemblyCheckScope> ScopeChanged;

        /// <summary>
        /// Raised when a part is selected from the parts list.
        /// </summary>
        public event EventHandler<BatchPartViewModel> PartSelected;

        /// <summary>
        /// Raised when an occurrence is chosen for SolidWorks viewport highlight.
        /// </summary>
        public event EventHandler<BatchOccurrenceViewModel> OccurrenceHighlightRequested;

        /// <summary>
        /// Raised when a bend is chosen for SolidWorks bend line highlight.
        /// </summary>
        public event EventHandler<Tuple<string, int>> BendLocateRequested;

        #endregion

        #region Private Fields & State

        private BatchSummaryViewModel currentSummary = BatchSummaryViewModel.CreateEmpty();
        private List<BatchPartViewModel> allParts = new List<BatchPartViewModel>();
        private List<BatchPartViewModel> displayedParts = new List<BatchPartViewModel>();
        private BatchPartViewModel selectedPart;
        private BatchBendViewModel selectedBend;
        private BatchToolCandidateViewModel selectedTool;
        private AssemblyCheckScope currentScope = AssemblyCheckScope.EntireAssembly;
        private bool isRunning;
        private bool isMockupMode;
        private bool bindingParts, bindingTools;
        internal BendAssemblyBatchTimings LastBindingTimings { get; private set; }
        internal bool IsPreview => isMockupMode;
        internal bool IsRunning => isRunning;
        internal AssemblyCheckScope Scope => currentScope;

        #endregion

        #region Constructors

        public BendAssemblyBatchForm()
        {
            InitializeComponent();
            ApplyCustomStyling();
            WireInternalEvents();
            toolTips.SetToolTip(lblHeaderSub, lblHeaderSub.Text);
            ClearLive("Chưa chạy. Chọn phạm vi rồi bấm Run Batch Check.");
        }

        public BendAssemblyBatchForm(string hostTitle, string configurationName)
            : this()
        {
            if (!string.IsNullOrWhiteSpace(hostTitle))
            {
                lblHeaderSub.Text = $"Cụm lắp ráp: {hostTitle} • Cấu hình: {configurationName ?? "Default"}";
                toolTips.SetToolTip(lblHeaderSub, lblHeaderSub.Text);
                Text = $"CHECK CHẤN — {hostTitle}";
            }
        }

        #endregion

        #region DPI & Responsive Scaling

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            RescaleDpiElements();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            RescaleDpiElements();
        }

        private void RescaleDpiElements()
        {
            dgvParts.RowTemplate.Height = TaiTheme.Px(this, 34);
            dgvTools.RowTemplate.Height = TaiTheme.Px(this, 30);

            // Rescale Part grid columns proportionally to current DPI
            colPartName.Width = TaiTheme.Px(this, 240);
            colPartConfig.Width = TaiTheme.Px(this, 130);
            colPartQty.Width = TaiTheme.Px(this, 70);
            colPartBends.Width = TaiTheme.Px(this, 70);
            colPartStatus.Width = TaiTheme.Px(this, 140);

            // Rescale Tool grid columns
            colToolId.Width = TaiTheme.Px(this, 125);
            colPunch.Width = TaiTheme.Px(this, 115);
            colHolder.Width = TaiTheme.Px(this, 140);
            colMotion.Width = TaiTheme.Px(this, 115);
            colGeomStatus.Width = TaiTheme.Px(this, 105);
            colMfgStatus.Width = TaiTheme.Px(this, 115);
            colClearance.Width = TaiTheme.Px(this, 95);

            // Re-render custom drawn components
            pnlProgressBar.Invalidate();
            cardMetrics1.Invalidate();
            cardMetrics2.Invalidate();
            cardMetrics3.Invalidate();
            cardMetrics4.Invalidate();
            cardMetrics5.Invalidate();
        }

        #endregion

        #region UI Styling & Initialization

        private void ApplyCustomStyling()
        {
            BackColor = TaiTheme.Page;
            Font = TaiTheme.Body;

            // Header styling
            badgeConcept.BackColor = Color.FromArgb(241, 245, 249);
            badgeConcept.ForeColor = Color.FromArgb(71, 85, 105);

            // Grids modern styling
            GridStyler.ApplyModernStyle(dgvParts, false);
            GridStyler.ApplyModernStyle(dgvTools, false);
            dgvParts.MultiSelect = dgvTools.MultiSelect = false;

            // Scaled elements & DPI
            RescaleDpiElements();

            // Double buffering for smooth resize without flicker
            UIHelper.EnableDoubleBuffer(pnlRoot);
            UIHelper.EnableDoubleBuffer(dgvParts);
            UIHelper.EnableDoubleBuffer(dgvTools);
            UIHelper.EnableDoubleBuffer(pnlProgressBar);
            UIHelper.EnableDoubleBuffer(cardMetrics1);
            UIHelper.EnableDoubleBuffer(cardMetrics2);
            UIHelper.EnableDoubleBuffer(cardMetrics3);
            UIHelper.EnableDoubleBuffer(cardMetrics4);
            UIHelper.EnableDoubleBuffer(cardMetrics5);

            // Custom paint handlers
            pnlProgressBar.Paint += PaintProgressBar;
            cardMetrics1.Paint += PaintMetricCard;
            cardMetrics2.Paint += PaintMetricCard;
            cardMetrics3.Paint += PaintMetricCard;
            cardMetrics4.Paint += PaintMetricCard;
            cardMetrics5.Paint += PaintMetricCard;

            // Custom cell paint for status pills
            dgvParts.CellPainting += DgvParts_CellPainting;
            dgvTools.CellPainting += DgvTools_CellPainting;

            // Rich cell tooltips
            dgvParts.CellToolTipTextNeeded += DgvParts_CellToolTipTextNeeded;
            dgvTools.CellToolTipTextNeeded += DgvTools_CellToolTipTextNeeded;

            // Search box placeholder / default styling
            txtSearch.ForeColor = TaiTheme.TextMuted;
            txtSearch.Text = "Tìm Part / cấu hình...";
            txtSearch.GotFocus += (s, e) =>
            {
                if (txtSearch.Text == "Tìm Part / cấu hình...")
                {
                    txtSearch.Text = string.Empty;
                    txtSearch.ForeColor = TaiTheme.Text;
                }
            };
            txtSearch.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtSearch.Text))
                {
                    txtSearch.ForeColor = TaiTheme.TextMuted;
                    txtSearch.Text = "Tìm Part / cấu hình...";
                }
            };

            cboStatusFilter.SelectedIndex = 0;
            UpdateScopeButtonsUI();
        }

        private void WireInternalEvents()
        {
            // Scope switching
            btnScopeEntire.Click += (s, e) => { SetScope(AssemblyCheckScope.EntireAssembly); };
            btnScopeSelected.Click += (s, e) => { SetScope(AssemblyCheckScope.SelectedComponents); };

            // Actions
            btnRunBatch.Click += OnRunBatchClicked;
            btnCancel.Click += OnCancelClicked;
            btnRefresh.Click += OnRefreshClicked;
            btnClose.Click += (s, e) => Close();
            btnToggleMockup.Text = "Preview (dữ liệu mẫu)";
            btnToggleMockup.Click += (s, e) =>
            {
                if (isRunning) return;
                if (PreviewRequested != null) PreviewRequested.Invoke(this, EventArgs.Empty);
                else LoadMockupData();
            };

            // Filters
            txtSearch.TextChanged += (s, e) => ApplyPartFilters();
            cboStatusFilter.SelectedIndexChanged += (s, e) => ApplyPartFilters();

            // Grid selections
            dgvParts.SelectionChanged += DgvParts_SelectionChanged;
            dgvTools.SelectionChanged += DgvTools_SelectionChanged;

            // Occurrence expand
            btnToggleOccurrences.Click += OnToggleOccurrencesClicked;
        }

        #endregion

        #region Scope & Control Actions

        public void SetScope(AssemblyCheckScope scope)
        {
            if (isRunning) return;
            currentScope = scope;
            UpdateScopeButtonsUI();
            ScopeChanged?.Invoke(this, scope);
        }

        private void UpdateScopeButtonsUI()
        {
            bool entire = currentScope == AssemblyCheckScope.EntireAssembly;
            btnScopeEntire.Selected = entire;
            btnScopeEntire.Tone = entire ? ModernButtonTone.Accent : ModernButtonTone.Neutral;
            btnScopeEntire.Font = new Font(btnScopeEntire.Font, entire ? FontStyle.Bold : FontStyle.Regular);

            btnScopeSelected.Selected = !entire;
            btnScopeSelected.Tone = !entire ? ModernButtonTone.Accent : ModernButtonTone.Neutral;
            btnScopeSelected.Font = new Font(btnScopeSelected.Font, !entire ? FontStyle.Bold : FontStyle.Regular);
        }

        private void OnRunBatchClicked(object sender, EventArgs e)
        {
            if (RunBatchRequested != null)
            {
                RunBatchRequested.Invoke(this, currentScope);
            }
            else
            {
                ClearLive("Backend chưa được kết nối. Chưa có kết quả CAD.");
            }
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            if (CancelBatchRequested != null)
            {
                CancelBatchRequested.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnRefreshClicked(object sender, EventArgs e)
        {
            if (RefreshRequested != null)
            {
                RefreshRequested.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ClearLive("Chưa có backend để quét lại.");
            }
        }

        public void SetRunningState(bool running)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => SetRunningState(running)));
                return;
            }

            isRunning = running;
            btnRunBatch.Enabled = !running;
            btnCancel.Enabled = running;
            btnRefresh.Enabled = !running;
            btnScopeEntire.Enabled = !running;
            btnScopeSelected.Enabled = !running;
            btnToggleMockup.Enabled = !running;

            if (running)
            {
                lblProgressTitle.Text = "Đang kiểm tra chấn hàng loạt...";
            }
        }

        internal void SetRunMessage(string message, bool cancelling = false)
        {
            lblProgressTitle.Text = message;
            if (cancelling) btnCancel.Enabled = false;
        }

        internal void ClearLive(string message)
        {
            LastBindingTimings = null;
            isMockupMode = false;
            badgeConcept.Text = "LIVE";
            badgeConcept.BackColor = Color.FromArgb(241, 245, 249);
            badgeConcept.ForeColor = Color.FromArgb(71, 85, 105);
            lblDisclaimer.Text = "*Kết quả hình học theo ứng viên, không kết luận khả năng chấn toàn Part. Quá trình cần bằng chứng riêng. Sản xuất: CHƯA XÁC MINH.";
            currentSummary = BatchSummaryViewModel.CreateEmpty();
            currentSummary.Diagnostics.Add(message);
            allParts.Clear();
            ClearDetailSection();
            RenderSummary();
            ApplyPartFilters();
            SetRunMessage(message);
        }

        internal void ShowStopped(string message, double totalSeconds, AssemblyBatchSnapshot snapshot = null)
        {
            ClearLive(message);
            currentSummary.ElapsedSeconds = totalSeconds;
            currentSummary.StageTimingsAvailable = snapshot != null;
            currentSummary.NativeCaptureSeconds = (snapshot?.CaptureMilliseconds ?? 0) / 1000.0;
            currentSummary.WorkerSeconds = (snapshot?.WorkerMilliseconds ?? 0) / 1000.0;
            RenderSummary();
            SetRunMessage(message);
        }

        #endregion

        #region Public Binding Methods for Backend (Codex)

        /// <summary>
        /// Binds live AssemblyBatchSnapshot data produced by Codex backend.
        /// Thread-safe (marshals to STA UI thread if needed).
        /// </summary>
        public void BindSnapshot(AssemblyBatchSnapshot snapshot, double elapsedSeconds = 0)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => BindSnapshot(snapshot, elapsedSeconds)));
                return;
            }

            ClearLive("Đang kiểm tra hiệu lực kết quả...");
            var projection = BatchViewModelConverter.Convert(snapshot, elapsedSeconds);
            if (!projection.Current)
            {
                SetRunMessage("Kết quả không còn hiệu lực — " + snapshot?.InvalidReason);
                return;
            }

            currentSummary = projection.Summary;
            allParts = projection.Parts;
            LastBindingTimings = new BendAssemblyBatchTimings
            {
                NativeCaptureMilliseconds = snapshot.CaptureMilliseconds,
                WorkerMilliseconds = snapshot.WorkerMilliseconds,
                NumericSolverMilliseconds = snapshot.NumericKernelMilliseconds,
                SnapshotValidationMilliseconds = projection.SnapshotValidationMilliseconds,
                SnapshotCopyMilliseconds = projection.SnapshotCopyMilliseconds,
                ViewModelMilliseconds = projection.ConversionMilliseconds
            };

            var render = Stopwatch.StartNew();
            RenderSummary();
            ApplyPartFilters();
            SetRunMessage(currentSummary.CompletionConfirmed ? "Backend đã hoàn tất kiểm tra. Sản xuất: CHƯA XÁC MINH." :
                "Tác vụ chưa hoàn tất đầy đủ; xem chẩn đoán.");
            // Complete pending grid paints before recording first visible output.
            Update(); dgvParts.Update(); dgvTools.Update();
            LastBindingTimings.GridRenderingMilliseconds = render.Elapsed.TotalMilliseconds;
        }

        internal void RecordVisibleTime(double runMilliseconds)
        {
            if (LastBindingTimings == null) return;
            LastBindingTimings.RunToVisibleMilliseconds = runMilliseconds;
            currentSummary.ElapsedSeconds = runMilliseconds / 1000.0;
            var t = LastBindingTimings;
            txtDiagnostics.AppendText($"\r\n\r\nSnapshot validation: {t.SnapshotValidationMilliseconds:0.00} ms | Đọc kết quả: {t.SnapshotCopyMilliseconds:0.00} ms | ViewModel: {t.ViewModelMilliseconds:0.00} ms | Grid: {t.GridRenderingMilliseconds:0.00} ms | Run → hiển thị: {t.RunToVisibleMilliseconds:0.00} ms");
        }

        /// <summary>
        /// Updates the live batch progress bar and metrics.
        /// </summary>
        public void UpdateProgress(AssemblyBatchProgress progress, double elapsedSeconds)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => UpdateProgress(progress, elapsedSeconds)));
                return;
            }

            if (progress == null) return;
            int total = Math.Max(1, progress.TotalParts);
            int done = Math.Min(total, progress.CompletedParts);
            int percent = Math.Min(99, (int)Math.Round((double)done / total * 100.0));

            currentSummary.CheckedParts = done;
            currentSummary.ProgressPercent = percent;
            currentSummary.ElapsedSeconds = Math.Round(elapsedSeconds, 1);
            currentSummary.CompletionConfirmed = false;

            lblProgressTitle.Text = $"Đã xử lý {done} / {total} lượt ({progress.CompletedBends} nếp, {progress.Candidates} ứng viên); chờ backend xác nhận · {elapsedSeconds:0.0}s";
            lblProgressPercent.Text = $"{percent}%";
            pnlProgressBar.Invalidate();
        }

        /// <summary>
        /// Loads the rich mockup dataset matching media_1792379592993_ec79bc63.png.
        /// </summary>
        public void LoadMockupData()
        {
            if (isRunning) return;
            isMockupMode = true;
            badgeConcept.Text = "PREVIEW";
            lblDisclaimer.Text = "PREVIEW — DỮ LIỆU MẪU, không phải kết quả CAD. Chọn Run để kiểm tra Live.";
            badgeConcept.BackColor = Color.FromArgb(241, 245, 249);
            badgeConcept.ForeColor = Color.FromArgb(71, 85, 105);

            var mockup = BatchViewModelConverter.CreateMockupData();
            currentSummary = mockup.Item1;
            allParts = mockup.Item2;

            RenderSummary();
            SetRunMessage("PREVIEW — DỮ LIỆU MẪU, không phải kết quả CAD.");
            ApplyPartFilters();

            // Select Frame-A by default to match the concept image
            if (dgvParts.Rows.Count > 0)
            {
                dgvParts.Rows[0].Selected = true;
                // SelectionChanged may fire as Add creates the first row,
                // before its Tag is assigned. Bind the detail explicitly.
                selectedPart = dgvParts.Rows[0].Tag as BatchPartViewModel;
                RenderPartDetail(selectedPart);
            }
        }

        #endregion

        #region Rendering & Data Population

        private void RenderSummary()
        {
            // Progress
            lblProgressTitle.Text = $"Đã xử lý {currentSummary.CheckedParts} lượt kiểm tra · {currentSummary.UniqueParts} nguồn Part/cấu hình";
            lblProgressPercent.Text = $"{currentSummary.ProgressPercent}%";
            pnlProgressBar.Invalidate();

            // Metric Cards
            lblMetrics1Val.Text = currentSummary.TotalComponents.ToString();
            lblMetrics2Val.Text = currentSummary.UniqueParts.ToString();
            lblMetrics3Val.Text = currentSummary.UnknownParts.ToString();
            lblMetrics4Val.Text = $"{currentSummary.ClearParts} CLEAR • {currentSummary.CollisionParts} COLLISION";
            lblMetrics5Val.Text = currentSummary.SkippedComponents.ToString();

            // Diagnostics Text
            txtDiagnostics.Clear();
            if (currentSummary.Diagnostics != null && currentSummary.Diagnostics.Count > 0)
            {
                txtDiagnostics.Text = string.Join("\r\n\r\n• ", currentSummary.Diagnostics);
                if (!txtDiagnostics.Text.StartsWith("• ")) txtDiagnostics.Text = "• " + txtDiagnostics.Text;
            }
            else
            {
                txtDiagnostics.Text = "Chưa có dữ liệu kiểm tra.";
            }
            string stages = currentSummary.StageTimingsAvailable ?
                $"Native capture: {currentSummary.NativeCaptureSeconds:0.000}s | Worker: {currentSummary.WorkerSeconds:0.000}s" :
                "Native capture / Worker: Chưa có dữ liệu";
            txtDiagnostics.AppendText($"\r\n\r\nTổng: {currentSummary.ElapsedSeconds:0.000}s | {stages}");
        }

        private void ApplyPartFilters()
        {
            string search = txtSearch.Text;
            if (search == "Tìm Part / cấu hình...") search = string.Empty;
            search = (search ?? string.Empty).Trim().ToLowerInvariant();

            int filterIndex = cboStatusFilter.SelectedIndex;

            displayedParts = allParts.Where(p =>
            {
                // Text search
                if (!string.IsNullOrEmpty(search))
                {
                    bool matchName = (p.PartName ?? string.Empty).ToLowerInvariant().Contains(search);
                    bool matchConfig = (p.Configuration ?? string.Empty).ToLowerInvariant().Contains(search);
                    bool matchReason = (p.Reason ?? string.Empty).ToLowerInvariant().Contains(search);
                    if (!matchName && !matchConfig && !matchReason) return false;
                }

                // Status filter: 0=All, 1=COLLISION, 2=UNKNOWN, 3=CLEAR
                if (filterIndex == 1 && p.Status != CollisionStatus.COLLISION) return false;
                if (filterIndex == 2 && p.Status != CollisionStatus.UNKNOWN) return false;
                if (filterIndex == 3 && p.Status != CollisionStatus.CLEAR) return false;

                return true;
            }).ToList();

            PopulatePartsGrid();
        }

        private void PopulatePartsGrid()
        {
            bindingParts = true; dgvParts.SuspendLayout();
            try
            {
            dgvParts.Rows.Clear();
            foreach (var part in displayedParts)
            {
                int rowIndex = dgvParts.Rows.Add(
                    part.PartName,
                    part.Configuration,
                    part.Quantity,
                    part.BendCount,
                    part.StatusDisplay,
                    part.Reason
                );
                dgvParts.Rows[rowIndex].Tag = part;
            }
            }
            finally { dgvParts.ResumeLayout(); bindingParts = false; }

            if (dgvParts.Rows.Count > 0)
            {
                dgvParts.Rows[0].Selected = true;
                selectedPart = dgvParts.Rows[0].Tag as BatchPartViewModel;
                RenderPartDetail(selectedPart);
            }
            else
            {
                ClearDetailSection();
            }
        }

        private void DgvParts_SelectionChanged(object sender, EventArgs e)
        {
            if (bindingParts) return;
            if (dgvParts.SelectedRows.Count == 0) return;
            var row = dgvParts.SelectedRows[0];
            var part = row.Tag as BatchPartViewModel;
            if (part == null) return;

            selectedPart = part;
            PartSelected?.Invoke(this, part);
            RenderPartDetail(part);
        }

        private void RenderPartDetail(BatchPartViewModel part)
        {
            if (part == null || part.Bends == null || part.Bends.Count == 0)
            {
                ClearDetailSection();
                return;
            }

            // Create bend selector pills
            pnlBendPills.SuspendLayout();
            pnlBendPills.Controls.Clear();

            BatchBendViewModel firstBend = null;
            foreach (var bend in part.Bends)
            {
                if (firstBend == null) firstBend = bend;
                var btnBend = new ModernButton
                {
                    Text = bend.BendName,
                    Kind = ModernButtonKind.Subtle,
                    Tone = bend.Status == CollisionStatus.COLLISION ? ModernButtonTone.Danger :
                           bend.Status == CollisionStatus.CLEAR ? ModernButtonTone.Success : ModernButtonTone.Neutral,
                    Height = TaiTheme.Px(this, 26),
                    AutoSize = true,
                    Margin = new Padding(0, 0, 4, 0),
                    Tag = bend
                };
                toolTips.SetToolTip(btnBend, bend.ToString());

                btnBend.Click += (s, ev) =>
                {
                    SelectBend(btnBend.Tag as BatchBendViewModel);
                };

                pnlBendPills.Controls.Add(btnBend);
            }
            pnlBendPills.ResumeLayout(true);

            SelectBend(firstBend);
        }

        private void SelectBend(BatchBendViewModel bend)
        {
            if (bend == null || selectedPart == null) return;
            selectedBend = bend;

            lblDetailBendTitle.Text = $"{selectedPart.PartName} / {bend.BendName} · Góc: {bend.AngleDisplay} · R: {bend.RadiusDisplay}";
            toolTips.SetToolTip(lblDetailBendTitle, $"{selectedPart.PartName} / {bend.BendName} (FeatureId {bend.FeatureId})\nGóc chấn: {bend.AngleDisplay}\nBán kính: {bend.RadiusDisplay}\nTrạng thái: {bend.StatusDisplay}");

            // Highlight active bend button
            foreach (Control ctrl in pnlBendPills.Controls)
            {
                var mb = ctrl as ModernButton;
                if (mb != null)
                {
                    bool active = ReferenceEquals(mb.Tag, bend);
                    mb.Selected = active;
                    mb.Font = new Font(mb.Font, active ? FontStyle.Bold : FontStyle.Regular);
                }
            }

            if (!isMockupMode) BendLocateRequested?.Invoke(this, Tuple.Create(selectedPart.SourceKey, bend.FeatureId));
            PopulateToolsGrid(bend);
        }

        private void PopulateToolsGrid(BatchBendViewModel bend)
        {
            bindingTools = true; dgvTools.SuspendLayout();
            try
            {
            dgvTools.Rows.Clear();
            if (bend == null || bend.ToolCandidates == null) return;

            foreach (var tool in bend.ToolCandidates)
            {
                string clearanceText = tool.PunchClearanceMm.HasValue ? $"{tool.PunchClearanceMm.Value:+0.00;-0.00} mm" : "—";

                int rowIndex = dgvTools.Rows.Add(
                    tool.ToolId,
                    tool.PunchStatusDisplay,
                    tool.HolderStatusDisplay,
                    tool.MotionDisplay,
                    tool.GeometricStatusDisplay,
                    tool.ManufacturingStatus,
                    clearanceText,
                    tool.CombinedReason
                );
                dgvTools.Rows[rowIndex].Tag = tool;
                dgvTools.Rows[rowIndex].Cells[3].ToolTipText = tool.MotionDetail;
            }
            }
            finally { dgvTools.ResumeLayout(); bindingTools = false; }

            if (dgvTools.Rows.Count > 0)
            {
                // Prefer selecting AMD-STD-004 if available (matches image)
                int defaultIndex = 0;
                for (int i = 0; i < dgvTools.Rows.Count; i++)
                {
                    var cand = dgvTools.Rows[i].Tag as BatchToolCandidateViewModel;
                    if (cand != null && cand.ToolId == "AMD-STD-004")
                    {
                        defaultIndex = i;
                        break;
                    }
                }
                dgvTools.Rows[defaultIndex].Selected = true;
                selectedTool = dgvTools.Rows[defaultIndex].Tag as BatchToolCandidateViewModel;
                if (selectedTool != null) RenderToolCallout(selectedTool);
            }
        }

        private void DgvTools_SelectionChanged(object sender, EventArgs e)
        {
            if (bindingTools) return;
            if (dgvTools.SelectedRows.Count == 0) return;
            var row = dgvTools.SelectedRows[0];
            var tool = row.Tag as BatchToolCandidateViewModel;
            if (tool == null) return;

            selectedTool = tool;
            RenderToolCallout(tool);
        }

        private void RenderToolCallout(BatchToolCandidateViewModel tool)
        {
            lblCalloutTitle.Text = $"Chi tiết dao — {tool.ToolId}";
            lblCalloutPunch.Text = $"Punch – {tool.ToolId} : {tool.PunchStatusDisplay}";
            lblCalloutHolder.Text = $"Upper Holder : {tool.HolderStatusDisplay}";
            lblCalloutReason.Text = $"Lý do: {tool.CombinedReason}";

            // Color coding
            lblCalloutPunch.ForeColor = tool.PunchStatus == CollisionStatus.COLLISION ? TaiTheme.Danger :
                                       tool.PunchStatus == CollisionStatus.CLEAR ? TaiTheme.Success : Color.FromArgb(51, 65, 85);

            lblCalloutHolder.ForeColor = tool.HolderStatus == CollisionStatus.COLLISION ? TaiTheme.Danger :
                                        tool.HolderStatus == CollisionStatus.CLEAR ? TaiTheme.Success : Color.FromArgb(51, 65, 85);

            toolTips.SetToolTip(lblCalloutTitle, $"Ứng viên: {tool.ToolId} (FeatureId {tool.FeatureId})");
            toolTips.SetToolTip(lblCalloutPunch, $"Punch ({tool.ToolId}): {tool.PunchStatusDisplay}");
            toolTips.SetToolTip(lblCalloutHolder, $"Upper Holder: {tool.HolderStatusDisplay}");
            toolTips.SetToolTip(lblCalloutReason, $"Lý do / Bằng chứng:\n{tool.CombinedReason}");
        }

        private void ClearDetailSection()
        {
            selectedPart = null; selectedBend = null; selectedTool = null;
            lblDetailBendTitle.Text = "Chi tiết nếp chấn — Chưa chọn Part";
            toolTips.SetToolTip(lblDetailBendTitle, "Chọn một chi tiết bên trên để xem các nếp chấn.");
            pnlBendPills.Controls.Clear();
            dgvTools.Rows.Clear();
            lblCalloutTitle.Text = "Chi tiết dao đang chọn";
            lblCalloutPunch.Text = "Punch : —";
            lblCalloutHolder.Text = "Upper Holder : —";
            lblCalloutReason.Text = "Chọn một chi tiết bên trên để xem đánh giá 18 bộ dao.";
            toolTips.SetToolTip(lblCalloutPunch, null);
            toolTips.SetToolTip(lblCalloutHolder, null);
            toolTips.SetToolTip(lblCalloutReason, null);
        }

        private void DgvParts_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.RowIndex >= dgvParts.Rows.Count) return;
            var row = dgvParts.Rows[e.RowIndex];
            var part = row.Tag as BatchPartViewModel;
            if (part == null) return;

            switch (e.ColumnIndex)
            {
                case 0:
                    e.ToolTipText = $"{part.PartName}\nSource: {part.SourceKey}\nSố lượng: {part.Quantity}\nSố nếp chấn: {part.BendCount}";
                    break;
                case 1:
                    e.ToolTipText = $"Cấu hình Part: {part.Configuration}";
                    break;
                case 4:
                    e.ToolTipText = $"Trạng thái hình học: {part.StatusDisplay}\nQuá trình: {part.ProcessStatus}\nSản xuất: {part.ManufacturingStatus}";
                    break;
                case 5:
                    if (!string.IsNullOrWhiteSpace(part.Reason))
                        e.ToolTipText = $"Lý do / Cảnh báo:\n{part.Reason}";
                    break;
            }
        }

        private void DgvTools_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.RowIndex >= dgvTools.Rows.Count) return;
            var row = dgvTools.Rows[e.RowIndex];
            var tool = row.Tag as BatchToolCandidateViewModel;
            if (tool == null) return;

            switch (e.ColumnIndex)
            {
                case 0:
                    e.ToolTipText = $"Bộ dao: {tool.ToolId} (FeatureId {tool.FeatureId})";
                    break;
                case 1:
                    e.ToolTipText = $"Chày (Punch): {tool.PunchStatusDisplay}\nTrạng thái tĩnh: {tool.PunchStatus}";
                    break;
                case 2:
                    e.ToolTipText = $"Cối kẹp trên (Holder): {tool.HolderStatusDisplay}\nBằng chứng: {tool.HolderStatus}";
                    break;
                case 3:
                    e.ToolTipText = $"Chuyển động: {tool.MotionDisplay}\nChi tiết: {tool.MotionDetail}";
                    break;
                case 4:
                    e.ToolTipText = $"Đánh giá hình học: {tool.GeometricStatusDisplay}";
                    break;
                case 5:
                    e.ToolTipText = $"Xác minh sản xuất: {tool.ManufacturingStatus}";
                    break;
                case 6:
                    e.ToolTipText = tool.PunchClearanceMm.HasValue ?
                        $"Khe hở chày: {tool.PunchClearanceMm.Value:+0.00;-0.00} mm" :
                        "Không có dữ liệu khe hở (clearance)";
                    break;
                case 7:
                    if (!string.IsNullOrWhiteSpace(tool.CombinedReason))
                        e.ToolTipText = $"Lý do / Bằng chứng:\n{tool.CombinedReason}";
                    break;
            }
        }

        private void OnToggleOccurrencesClicked(object sender, EventArgs e)
        {
            if (selectedPart == null || selectedPart.Occurrences == null || selectedPart.Occurrences.Count == 0)
            {
                MessageBox.Show(this, "Vui lòng chọn một Part để xem danh sách occurrence.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var form = new Form())
            {
                form.Text = $"Danh sách chi tiết lặp lại (Occurrences) — {selectedPart.PartName} ({selectedPart.Quantity} chi tiết)";
                form.Size = new Size(TaiTheme.Px(this, 820), TaiTheme.Px(this, 480));
                form.MinimumSize = new Size(TaiTheme.Px(this, 650), TaiTheme.Px(this, 350));
                form.StartPosition = FormStartPosition.CenterParent;
                form.Font = Font;
                form.BackColor = TaiTheme.Page;
                form.ShowIcon = false;
                form.MinimizeBox = false;
                form.MaximizeBox = true;
                form.FormBorderStyle = FormBorderStyle.Sizable;

                var grid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    MultiSelect = false
                };
                GridStyler.ApplyModernStyle(grid);
                UIHelper.EnableDoubleBuffer(grid);

                grid.Columns.Add("path", "Đường dẫn trong Assembly (Display Path)");
                grid.Columns.Add("config", "Cấu hình");
                grid.Columns.Add("kind", "Loại");
                grid.Columns.Add("status", "Trạng thái");
                grid.Columns.Add("reason", "Lý do");
                grid.Columns[0].Width = TaiTheme.Px(this, 320);
                grid.Columns[1].Width = TaiTheme.Px(this, 110);
                grid.Columns[2].Width = TaiTheme.Px(this, 95);
                grid.Columns[3].Width = TaiTheme.Px(this, 110);
                grid.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

                Action<string> populateOccurrences = (filter) =>
                {
                    grid.Rows.Clear();
                    string f = (filter ?? string.Empty).Trim().ToLowerInvariant();
                    foreach (var occ in selectedPart.Occurrences)
                    {
                        if (!string.IsNullOrEmpty(f))
                        {
                            bool matchPath = (occ.DisplayPath ?? string.Empty).ToLowerInvariant().Contains(f);
                            bool matchConfig = (occ.Configuration ?? string.Empty).ToLowerInvariant().Contains(f);
                            bool matchReason = (occ.Reason ?? string.Empty).ToLowerInvariant().Contains(f);
                            if (!matchPath && !matchConfig && !matchReason) continue;
                        }
                        int idx = grid.Rows.Add(occ.DisplayPath, occ.Configuration, occ.Kind.ToString(), occ.StatusBadge, occ.Reason);
                        grid.Rows[idx].Tag = occ;
                    }
                };

                populateOccurrences(string.Empty);

                grid.CellToolTipTextNeeded += (s, ce) =>
                {
                    if (ce.RowIndex >= 0 && ce.ColumnIndex >= 0 && ce.RowIndex < grid.Rows.Count)
                    {
                        var cellVal = grid.Rows[ce.RowIndex].Cells[ce.ColumnIndex].Value?.ToString();
                        if (!string.IsNullOrWhiteSpace(cellVal)) ce.ToolTipText = cellVal;
                    }
                };

                grid.DoubleClick += (s, ev) =>
                {
                    if (grid.SelectedRows.Count > 0)
                    {
                        var occ = grid.SelectedRows[0].Tag as BatchOccurrenceViewModel;
                        if (occ != null) OccurrenceHighlightRequested?.Invoke(this, occ);
                    }
                };

                // Search top panel
                var pnlTop = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = TaiTheme.Px(this, 40),
                    Padding = new Padding(12, 6, 12, 6),
                    BackColor = TaiTheme.Surface
                };
                var lblSearch = new Label
                {
                    Text = "Lọc occurrence:",
                    Dock = DockStyle.Left,
                    AutoSize = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = TaiTheme.Small,
                    ForeColor = TaiTheme.TextMuted,
                    Margin = new Padding(0, 4, 8, 0)
                };
                var txtOccSearch = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Font = TaiTheme.Body
                };
                txtOccSearch.TextChanged += (s, ev) => populateOccurrences(txtOccSearch.Text);
                pnlTop.Controls.Add(txtOccSearch);
                pnlTop.Controls.Add(lblSearch);

                // Bottom status & close panel
                var pnlBottom = new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = TaiTheme.Px(this, 44),
                    Padding = new Padding(12, 6, 12, 6),
                    BackColor = TaiTheme.Surface
                };
                var lblStatus = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = $"Tổng số: {selectedPart.Occurrences.Count} occurrence(s) · Nhấp đúp vào dòng để highlight trên viewport SolidWorks",
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = TaiTheme.Small,
                    ForeColor = TaiTheme.TextMuted
                };
                var btnOccClose = new ModernButton
                {
                    Dock = DockStyle.Right,
                    Text = "Đóng",
                    Width = TaiTheme.Px(this, 80),
                    Height = TaiTheme.Px(this, 30),
                    Kind = ModernButtonKind.Standard,
                    Tone = ModernButtonTone.Neutral
                };
                btnOccClose.Click += (s, ev) => form.Close();
                pnlBottom.Controls.Add(lblStatus);
                pnlBottom.Controls.Add(btnOccClose);

                form.Controls.Add(grid);
                form.Controls.Add(pnlTop);
                form.Controls.Add(pnlBottom);

                form.CancelButton = btnOccClose;
                form.KeyPreview = true;
                form.KeyDown += (s, ev) => { if (ev.KeyCode == Keys.Escape) form.Close(); };

                form.ShowDialog(this);
            }
        }

        #endregion

        #region Custom Graphics Painting

        private void PaintProgressBar(object sender, PaintEventArgs e)
        {
            var pnl = sender as Panel;
            if (pnl == null) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = pnl.Width, h = pnl.Height;
            if (w <= 0 || h <= 0) return;

            // Track background
            using (var brush = new SolidBrush(Color.FromArgb(226, 232, 240)))
            using (var path = CreateRoundedRectangle(new Rectangle(0, 0, w, h), h / 2))
            {
                g.FillPath(brush, path);
            }

            // Fill bar
            int fillWidth = (int)Math.Round((double)w * currentSummary.ProgressPercent / 100.0);
            if (fillWidth > 2)
            {
                // Teal/Emerald green matching user wireframe (#10B981)
                using (var fillBrush = new SolidBrush(Color.FromArgb(16, 185, 129)))
                using (var fillPath = CreateRoundedRectangle(new Rectangle(0, 0, fillWidth, h), h / 2))
                {
                    g.FillPath(fillBrush, fillPath);
                }
            }
        }

        private void PaintMetricCard(object sender, PaintEventArgs e)
        {
            var pnl = sender as Panel;
            if (pnl == null) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = pnl.Width, h = pnl.Height;
            if (w <= 1 || h <= 1) return;

            // Soft border and rounded corners
            using (var pen = new Pen(Color.FromArgb(226, 232, 240), 1F))
            using (var path = CreateRoundedRectangle(new Rectangle(0, 0, w - 1, h - 1), 6))
            {
                g.DrawPath(pen, path);
            }
        }

        private void DgvParts_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            // Only custom paint the 'Status' column (index 4)
            if (e.RowIndex >= 0 && e.ColumnIndex == 4)
            {
                e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);

                string statusText = e.Value?.ToString() ?? string.Empty;
                Color textColor, bgColor;

                if (statusText.Contains("COLLISION"))
                {
                    textColor = Color.FromArgb(220, 38, 38);
                    bgColor = Color.FromArgb(254, 226, 226);
                }
                else if (statusText.Contains("CLEAR"))
                {
                    textColor = Color.FromArgb(22, 101, 52);
                    bgColor = Color.FromArgb(220, 252, 231);
                }
                else
                {
                    textColor = Color.FromArgb(100, 116, 139);
                    bgColor = Color.FromArgb(241, 245, 249);
                }

                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                int pillH = TaiTheme.Px(this, 22);
                int pillW = Math.Min(e.CellBounds.Width - 10, TaiTheme.Px(this, 100));
                int pillX = e.CellBounds.X + (e.CellBounds.Width - pillW) / 2;
                int pillY = e.CellBounds.Y + (e.CellBounds.Height - pillH) / 2;
                var pillRect = new Rectangle(pillX, pillY, pillW, pillH);

                using (var brush = new SolidBrush(bgColor))
                using (var path = CreateRoundedRectangle(pillRect, pillH / 2))
                {
                    g.FillPath(brush, path);
                }

                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var font = new Font("Segoe UI", 8.5F, FontStyle.Bold))
                using (var textBrush = new SolidBrush(textColor))
                {
                    g.DrawString(statusText, font, textBrush, pillRect, sf);
                }

                e.Handled = true;
            }
        }

        private void DgvTools_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            // Custom paint status columns (Punch=1, Holder=2, Geom=4)
            if (e.RowIndex >= 0 && (e.ColumnIndex == 1 || e.ColumnIndex == 2 || e.ColumnIndex == 4))
            {
                e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);

                string statusText = e.Value?.ToString() ?? string.Empty;
                Color textColor, bgColor;

                if (statusText.Contains("COLLISION"))
                {
                    textColor = Color.FromArgb(220, 38, 38);
                    bgColor = Color.FromArgb(254, 226, 226);
                }
                else if (statusText.Contains("CLEAR"))
                {
                    textColor = Color.FromArgb(22, 101, 52);
                    bgColor = Color.FromArgb(220, 252, 231);
                }
                else
                {
                    textColor = Color.FromArgb(100, 116, 139);
                    bgColor = Color.FromArgb(241, 245, 249);
                }

                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                int pillH = TaiTheme.Px(this, 20);
                int pillW = Math.Min(e.CellBounds.Width - 10, TaiTheme.Px(this, 88));
                int pillX = e.CellBounds.X + (e.CellBounds.Width - pillW) / 2;
                int pillY = e.CellBounds.Y + (e.CellBounds.Height - pillH) / 2;
                var pillRect = new Rectangle(pillX, pillY, pillW, pillH);

                using (var brush = new SolidBrush(bgColor))
                using (var path = CreateRoundedRectangle(pillRect, pillH / 2))
                {
                    g.FillPath(brush, path);
                }

                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var font = new Font("Segoe UI", 8F, FontStyle.Bold))
                using (var textBrush = new SolidBrush(textColor))
                {
                    g.DrawString(statusText, font, textBrush, pillRect, sf);
                }

                e.Handled = true;
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0) return path;

            int diameter = radius * 2;
            if (diameter > rect.Height) diameter = rect.Height;
            if (diameter > rect.Width) diameter = rect.Width;

            Rectangle arc = new Rectangle(rect.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        #endregion
    }
}
