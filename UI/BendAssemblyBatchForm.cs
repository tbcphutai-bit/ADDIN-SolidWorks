using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Diagnostics;
using System.Windows.Forms;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendTools;

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
        private bool technicalDetails;
        private string Explain(string reason) => BendCheckDisplayText.Reasons(reason,technicalDetails);
        private bool bindingParts, bindingTools;
        private readonly BendSequencePlanningControl sequenceView = new BendSequencePlanningControl();
        private readonly BendToolEvidenceControl toolEvidence = new BendToolEvidenceControl();
        private string preservePart, preserveTool;
        private int? preserveFeature;
        private readonly DataGridView phaseEvidence = new DataGridView();
        private readonly Label pageLabel = new Label { AutoSize = true, Padding = new Padding(4, 5, 4, 0) };
        private int partPage;
        private const int PartsPerPage = 250;
        private readonly DataGridView minimumEvidence = new DataGridView();
        private readonly TextBox minimumDescription = new TextBox();
        private Font bendActiveFont, bendNormalFont;
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
            bendActiveFont = new Font(Font, FontStyle.Bold); bendNormalFont = new Font(Font, FontStyle.Regular);
            Disposed += (s, e) => { bendActiveFont?.Dispose(); bendNormalFont?.Dispose(); };
            AddPlanningNavigation();
            WireInternalEvents();
            InstallUxV4(); // new UI shell, reuses original controllers and controls
            toolTips.SetToolTip(lblHeaderSub, lblHeaderSub.Text);
            ClearLive("Chọn phạm vi rồi bấm Bắt đầu kiểm tra.");
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

        private void AddPlanningNavigation()
        {
            // Keep Antigravity's controls/layout intact within the existing
            // window; add functional navigation around those controls.
            var tabs = new TabControl { Name = "checkChanDetailTabs", Dock = DockStyle.Fill };
            var bends = new TabPage("Nếp chấn & 18 bộ dao"); var plan = new TabPage("Chọn dao & thứ tự chấn"); var diagnostic = new TabPage("Lý do / Chi tiết kỹ thuật");
            var minimum = new TabPage("Cạnh chấn tối thiểu");
            minimumEvidence.Name = "batchMinimumEvidence"; minimumEvidence.Dock = DockStyle.Fill; minimumEvidence.ReadOnly = true;
            minimumEvidence.AllowUserToAddRows = false; minimumEvidence.RowHeadersVisible = false; minimumEvidence.MultiSelect = false;
            minimumEvidence.SelectionMode = DataGridViewSelectionMode.FullRowSelect; minimumEvidence.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            foreach (string heading in new[] { "Mã nếp", "Kết quả cạnh chấn", "Phía", "Dạng / mốc", "Phủ bì (mm)", "Tối thiểu (mm)" })
            { int i = minimumEvidence.Columns.Add("min" + minimumEvidence.Columns.Count, heading); minimumEvidence.Columns[i].MinimumWidth = 85; }
            minimumDescription.Name = "batchMinimumDescription"; minimumDescription.Dock = DockStyle.Bottom; minimumDescription.Height = 95;
            minimumDescription.Multiline = true; minimumDescription.ReadOnly = true; minimumDescription.ScrollBars = ScrollBars.Vertical;
            minimum.Controls.Add(minimumEvidence); minimum.Controls.Add(minimumDescription);
            minimumEvidence.CellClick += (s, e) => {
                if (e.RowIndex < 0 || selectedPart?.RawPartResult?.Current != true || isMockupMode) return;
                var metadata = minimumEvidence.Rows[e.RowIndex].Tag as AssemblyBendMetadata;
                if (metadata != null) { minimumDescription.Text = metadata.MinimumDetails; BendLocateRequested?.Invoke(this, Tuple.Create(selectedPart.SelectionKey, metadata.FeatureId)); }
            };
            splitMain.Panel2.Controls.Remove(splitDetails); bends.Controls.Add(splitDetails);
            splitDetails.Panel2.Controls.Remove(pnlDiagnosticsCard); diagnostic.Controls.Add(pnlDiagnosticsCard);
            pnlToolCallout.Dock = DockStyle.Top; pnlToolCallout.Height = 145;
            colToolId.AutoSizeMode = DataGridViewAutoSizeColumnMode.None; colToolId.MinimumWidth = 165;
            colPunch.HeaderText = "Chày chấn"; colHolder.HeaderText = "Đầu kẹp chày"; colMotion.HeaderText = "Quá trình chấn"; colGeomStatus.HeaderText = "Vị trí đang xét";
            phaseEvidence.Name = "candidatePhaseEvidence"; phaseEvidence.Dock = DockStyle.Fill; phaseEvidence.ReadOnly = true;
            phaseEvidence.AllowUserToAddRows = false; phaseEvidence.RowHeadersVisible = false; phaseEvidence.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            phaseEvidence.DefaultCellStyle.WrapMode=DataGridViewTriState.True;phaseEvidence.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.DisplayedCells;
            foreach (string heading in new[] { "Giai đoạn / cặp", "Kết quả", "Bằng chứng / lý do" })
            { int column = phaseEvidence.Columns.Add("p" + phaseEvidence.Columns.Count, heading); phaseEvidence.Columns[column].MinimumWidth = 90; }
            phaseEvidence.CellToolTipTextNeeded += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0) e.ToolTipText = Convert.ToString(phaseEvidence[e.ColumnIndex, e.RowIndex].Value); };
            splitDetails.Panel2.Controls.Add(phaseEvidence); phaseEvidence.BringToFront();
            var review = new TabPage("Hồ sơ dao"); review.Controls.Add(toolEvidence);
            plan.Controls.Add(sequenceView); tabs.TabPages.AddRange(new[] { bends, plan, diagnostic, minimum, review }); splitMain.Panel2.Controls.Add(tabs);
            sequenceView.FeatureSelected += (s, feature) => { if (!isMockupMode && selectedPart?.RawPartResult?.Current == true) BendLocateRequested?.Invoke(this, Tuple.Create(selectedPart.SelectionKey, feature)); };
            var pager = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
            var previous = new Button { Name = "partsPagePrevious", Text = "‹", Width = 30 }; var next = new Button { Name = "partsPageNext", Text = "›", Width = 30 };
            previous.Click += (s, e) => { if (partPage > 0) { partPage--; PopulatePartsGrid(); } };
            next.Click += (s, e) => { if ((partPage + 1) * PartsPerPage < displayedParts.Count) { partPage++; PopulatePartsGrid(); } };
            pager.Controls.AddRange(new Control[] { previous, pageLabel, next }); pnlPartsHeader.Controls.Add(pager);
            pnlPartsHeader.Controls.Remove(btnToggleOccurrences); btnToggleOccurrences.Anchor = AnchorStyles.None;
            btnToggleOccurrences.AutoSize = true; btnToggleOccurrences.Text = "Vị trí lắp"; pager.Controls.Add(btnToggleOccurrences);
            var technical=new CheckBox {Name="checkChanTechnical",Text="Chi tiết kỹ thuật",AutoSize=true};
            pager.Controls.Add(technical);
            technical.CheckedChanged+=(s,e)=>{
                technicalDetails=technical.Checked;sequenceView.SetTechnicalDetails(technicalDetails);
                if(!isRunning)RenderSummary();
                foreach(DataGridViewRow row in dgvParts.Rows)if(row.Tag is BatchPartViewModel part)row.Cells[5].Value=BendCheckDisplayText.PartReason(part,technicalDetails);
                foreach(DataGridViewRow row in dgvTools.Rows)if(row.Tag is BatchToolCandidateViewModel tool)row.Cells[7].Value=Explain(tool.CombinedReason);
                if(selectedTool!=null)RenderToolCallout(selectedTool,false);
            };
            toolTips.SetToolTip(technical,"Hiện nguyên mã lý do, mã nếp và bằng chứng nguồn. Không thay đổi kết quả kiểm tra.");
            toolTips.SetToolTip(colClearance.DataGridView,"Khoảng hở chỉ có khi đã chứng minh được; thiếu dữ liệu không được coi là 0 mm.");
        }
        internal void RecordDiagnostic(string message) => txtDiagnostics.AppendText("\r\n" + message);
        internal void SetPartMode()
        {
            btnScopeEntire.Text = "Chi tiết đang mở"; btnScopeSelected.Visible = false;
            lblHeaderTitle.Text = "CHECK CHẤN · CHI TIẾT"; LayoutControlBar();
        }

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

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RescaleDpiElements();
            LayoutControlBar();
        }

        private bool inControlBarLayout;

        public void LayoutControlBar()
        {
            if (inControlBarLayout || pnlControlBar == null || pnlControlRight == null || pnlScope == null) return;
            int w = pnlControlBar.ClientSize.Width;
            if (w <= 0) return;

            try
            {
                inControlBarLayout = true;
                pnlControlBar.SuspendLayout();

                int padLeft = TaiTheme.Px(this, 16);
                int padRight = TaiTheme.Px(this, 16);
                int gap = TaiTheme.Px(this, 8);
                foreach(var button in new[]{btnScopeEntire,btnScopeSelected})
                    button.MinimumSize=new Size(TextRenderer.MeasureText(button.Text,button.Font).Width+button.Padding.Horizontal+TaiTheme.Px(this,16),TaiTheme.Px(this,32));

                int scopeW = pnlScope.PreferredSize.Width;
                int scopeH = pnlScope.PreferredSize.Height;
                int rightW = pnlControlRight.PreferredSize.Width;
                int rightH = pnlControlRight.PreferredSize.Height;

                int filterW = TaiTheme.Px(this, 160);
                int filterH = TaiTheme.Px(this, 26);
                int minSearchW = TaiTheme.Px(this, 120);
                int searchH = TaiTheme.Px(this, 24);

                int neededSingleRow = padLeft + scopeW + gap + filterW + gap + minSearchW + gap + rightW + padRight;

                bool singleRow = w >= neededSingleRow;

                if (singleRow)
                {
                    int barH = TaiTheme.Px(this, 46);
                    if (pnlRoot != null && pnlRoot.RowStyles.Count > 1 && Math.Abs(pnlRoot.RowStyles[1].Height - barH) > 0.5f)
                    {
                        pnlRoot.RowStyles[1].Height = barH;
                    }

                    int scopeY = Math.Max(0, (barH - scopeH) / 2);
                    pnlScope.SetBounds(padLeft, scopeY, scopeW, scopeH);

                    int filterY = Math.Max(0, (barH - filterH) / 2);
                    cboStatusFilter.SetBounds(pnlScope.Right + gap, filterY, filterW, filterH);

                    int rightX = Math.Max(cboStatusFilter.Right + gap + minSearchW + gap, w - padRight - rightW);
                    int rightY = Math.Max(0, (barH - rightH) / 2);
                    pnlControlRight.SetBounds(rightX, rightY, rightW, rightH);

                    int searchX = cboStatusFilter.Right + gap;
                    int searchW = Math.Max(minSearchW, rightX - gap - searchX);
                    int searchY = Math.Max(0, (barH - searchH) / 2);
                    txtSearch.SetBounds(searchX, searchY, searchW, searchH);
                }
                else
                {
                    // 2-tier responsive mode: prevents clipping at narrow widths or 125%/150% DPI
                    int tierH = Math.Max(scopeH, rightH);
                    if (tierH < TaiTheme.Px(this, 36)) tierH = TaiTheme.Px(this, 36);
                    int barH = tierH * 2 + TaiTheme.Px(this, 10);
                    if (pnlRoot != null && pnlRoot.RowStyles.Count > 1 && Math.Abs(pnlRoot.RowStyles[1].Height - barH) > 0.5f)
                    {
                        pnlRoot.RowStyles[1].Height = barH;
                    }

                    // Row 1: Scope on left, Actions on right
                    int y1 = TaiTheme.Px(this, 4);
                    pnlScope.SetBounds(padLeft, y1 + (tierH - scopeH) / 2, scopeW, scopeH);

                    int rightX = Math.Max(padLeft + scopeW + gap, w - padRight - rightW);
                    pnlControlRight.SetBounds(rightX, y1 + (tierH - rightH) / 2, rightW, rightH);

                    // Row 2: Filter and Search spanning full width
                    int y2 = y1 + tierH + TaiTheme.Px(this, 4);
                    int filterY2 = y2 + (tierH - filterH) / 2;
                    cboStatusFilter.SetBounds(padLeft, filterY2, filterW, filterH);

                    int searchX2 = padLeft + filterW + gap;
                    int searchW2 = Math.Max(minSearchW, w - padRight - searchX2);
                    int searchY2 = y2 + (tierH - searchH) / 2;
                    txtSearch.SetBounds(searchX2, searchY2, searchW2, searchH);
                }

                pnlControlBar.ResumeLayout(true);
            }
            finally
            {
                inControlBarLayout = false;
            }
        }

        private void RescaleDpiElements()
        {
            int partRowH = TaiTheme.Px(this, 40);
            dgvParts.RowTemplate.Height = partRowH;
            foreach (DataGridViewRow row in dgvParts.Rows)
            {
                row.Height = partRowH;
            }
            dgvParts.ColumnHeadersHeight = TaiTheme.Px(this, 32);

            int toolRowH = TaiTheme.Px(this, 42);
            dgvTools.RowTemplate.Height = toolRowH;
            foreach (DataGridViewRow row in dgvTools.Rows)
            {
                row.Height = toolRowH;
            }
            dgvTools.ColumnHeadersHeight = TaiTheme.Px(this, 30);

            // Rescale Part grid columns proportionally to current DPI
            colPartName.Width = TaiTheme.Px(this, 240);
            colPartConfig.Width = TaiTheme.Px(this, 130);
            colPartQty.Width = TaiTheme.Px(this, 70);
            colPartBends.Width = TaiTheme.Px(this, 70);
            colPartStatus.Width = TaiTheme.Px(this, 200);

            // Rescale Tool grid columns
            colToolId.Width = TaiTheme.Px(this, 125);
            colPunch.Width = TaiTheme.Px(this, 115);
            colHolder.Width = TaiTheme.Px(this, 140);
            colMotion.Width = TaiTheme.Px(this, 115);
            colGeomStatus.Width = TaiTheme.Px(this, 105);
            colMfgStatus.Width = TaiTheme.Px(this, 115);
            colClearance.Width = TaiTheme.Px(this, 95);

            // Rescale layout heights in pnlRoot
            if (pnlRoot != null && pnlRoot.RowStyles.Count >= 5)
            {
                pnlRoot.RowStyles[0].Height = TaiTheme.Px(this, 52);
                // V4 removed the overview panel. Keep its row at zero on resize/DPI.
                pnlRoot.RowStyles[2].Height = uxV4Tabs == null ? TaiTheme.Px(this, 126) : 0;
                pnlRoot.RowStyles[4].Height = TaiTheme.Px(this, 46);
            }

            // Re-layout control bar for current DPI
            LayoutControlBar();

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
            badgeConcept.AutoSize=true;badgeConcept.Padding=new Padding(8,5,8,5);

            // Grids modern styling
            GridStyler.ApplyModernStyle(dgvParts, false);
            GridStyler.ApplyModernStyle(dgvTools, false);
            dgvTools.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
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
            txtSearch.Text = "Tìm chi tiết / cấu hình...";
            txtSearch.GotFocus += (s, e) =>
            {
                if (txtSearch.Text == "Tìm chi tiết / cấu hình...")
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
                    txtSearch.Text = "Tìm chi tiết / cấu hình...";
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
            btnToggleMockup.Text = "Dữ liệu minh họa";
            btnToggleMockup.Click += (s, e) =>
            {
                if (isRunning) return;
                if (PreviewRequested != null) PreviewRequested.Invoke(this, EventArgs.Empty);
                else LoadMockupData();
            };

            // Filters
            txtSearch.TextChanged += (s, e) => ApplyPartFilters();
            cboStatusFilter.SelectedIndexChanged += (s, e) => ApplyPartFilters();

            // Responsive control bar resize
            pnlControlBar.Resize += (s, e) => LayoutControlBar();

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
            BendCheckRunDiagnostics.Write("OnRunBatchClicked.begin",new {Form=GetHashCode(),Scope=currentScope.ToString(),isRunning,Handlers=RunBatchRequested?.GetInvocationList().Length??0});
            if (RunBatchRequested != null)
            {
                RunBatchRequested.Invoke(this, currentScope);
                BendCheckRunDiagnostics.Write("OnRunBatchClicked.dispatched",new {Form=GetHashCode()});
            }
            else
            {
                ClearLive("Chưa kết nối bộ kiểm tra. Chưa có kết quả CAD.");
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
            UxV4SyncRunning(running);
            if (running) UxV4SyncStatus(lblProgressTitle.Text);
        }

        internal void SetRunMessage(string message, bool cancelling = false)
        {
            lblProgressTitle.Text = message;
            if (cancelling) btnCancel.Enabled = false;
            UxV4SyncStatus(message);
        }

        internal void ClearLive(string message)
        {
            if (selectedPart != null) { preservePart = selectedPart.SelectionKey; preserveFeature = selectedBend?.FeatureId; preserveTool = selectedTool?.ToolId; }
            LastBindingTimings = null;
            isMockupMode = false;
            badgeConcept.Text = "CAD đang mở";
            badgeConcept.BackColor = Color.FromArgb(241, 245, 249);
            badgeConcept.ForeColor = Color.FromArgb(71, 85, 105);
            lblDisclaimer.Text = "*Kết quả hình học theo ứng viên, không kết luận khả năng chấn toàn Part. Quá trình cần bằng chứng riêng. Sản xuất: CHƯA XÁC MINH.";
            currentSummary = BatchSummaryViewModel.CreateEmpty();
            currentSummary.Diagnostics.Add(message);
            allParts.Clear();
            sequenceView.SetResult(null, false);
            ClearDetailSection();
            RenderSummary();
            ApplyPartFilters();
            SetRunMessage(message);
        }

        internal void ShowStopped(string message, double totalSeconds, AssemblyBatchSnapshot snapshot = null)
        {
            BendCheckRunDiagnostics.Write("UI.ShowStopped",new {Form=GetHashCode(),message,totalSeconds,Parts=snapshot?.Parts.Count});
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

            BendCheckRunDiagnostics.Write("BindSnapshot.begin",new {Form=GetHashCode(),Current=snapshot?.Current,Parts=snapshot?.Parts.Count,Reason=snapshot?.InvalidReason});
            ClearLive("Đang kiểm tra hiệu lực kết quả...");
            var projection = BatchViewModelConverter.Convert(snapshot, elapsedSeconds);
            if (!projection.Current)
            {
                SetRunMessage("Kết quả không còn hiệu lực — " + snapshot?.InvalidReason);
                return;
            }

            currentSummary = projection.Summary;
            allParts = projection.Parts;
            if (allParts.Count > 0 && allParts.All(p => p.RawPartResult?.SimulationOnly == true)) badgeConcept.Text = "Dữ liệu mô phỏng";
            LastBindingTimings = new BendAssemblyBatchTimings
            {
                NativeCaptureMilliseconds = snapshot.CaptureMilliseconds,
                WorkerMilliseconds = snapshot.WorkerMilliseconds,
                NumericSolverMilliseconds = snapshot.NumericKernelMilliseconds,
                PlanningMilliseconds = snapshot.PlanningMilliseconds,
                SnapshotValidationMilliseconds = projection.SnapshotValidationMilliseconds,
                SnapshotCopyMilliseconds = projection.SnapshotCopyMilliseconds,
                ViewModelMilliseconds = projection.ConversionMilliseconds
            };

            var render = Stopwatch.StartNew();
            RenderSummary();
            ApplyPartFilters();
            SetRunMessage(currentSummary.CompletionConfirmed ? "Đã hoàn tất kiểm tra. Sản xuất: CHƯA XÁC MINH." :
                "Tác vụ chưa hoàn tất đầy đủ; xem chẩn đoán.");
            // Complete pending grid paints before recording first visible output.
            Update(); dgvParts.Update(); dgvTools.Update();
            LastBindingTimings.GridRenderingMilliseconds = render.Elapsed.TotalMilliseconds;
            BendCheckRunDiagnostics.Write("BindSnapshot.end",new {Form=GetHashCode(),Parts=allParts.Count,
                ToolPairs=snapshot.ToolPairs.Count,MatchedPairs=snapshot.ToolPairs.Count(p=>p.CadPair?.Matched==true),currentSummary.NumericQueries});
        }

        internal void RecordVisibleTime(double runMilliseconds)
        {
            if (LastBindingTimings == null) return;
            LastBindingTimings.RunToVisibleMilliseconds = runMilliseconds;
            currentSummary.ElapsedSeconds = runMilliseconds / 1000.0;
            var t = LastBindingTimings;
            txtDiagnostics.AppendText($"\r\n\r\nSnapshot validation: {t.SnapshotValidationMilliseconds:0.00} ms | Đọc kết quả: {t.SnapshotCopyMilliseconds:0.00} ms | ViewModel: {t.ViewModelMilliseconds:0.00} ms | Grid: {t.GridRenderingMilliseconds:0.00} ms | Run → hiển thị: {t.RunToVisibleMilliseconds:0.00} ms");
            txtDiagnostics.AppendText($"\r\nPlanning (tổng worker): {t.PlanningMilliseconds:0.00} ms");
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

            lblProgressTitle.Text = $"Đã xử lý {done} / {total} lượt ({progress.CompletedBends} nếp, {progress.Candidates} ứng viên); chờ xác nhận · {elapsedSeconds:0.0}s";
            if (progress.PlanningToolId != null) lblProgressTitle.Text = $"Thử thứ tự · {progress.PlanningToolId} · {progress.PlanningNodes} bước tìm · {progress.PlanningQueries} lượt tính · {done}/{total} chi tiết · {elapsedSeconds:0.0}s";
            lblProgressPercent.Text = $"{percent}%";
            pnlProgressBar.Invalidate();
            UxV4SyncStatus(lblProgressTitle.Text);
        }

        /// <summary>
        /// Loads the rich mockup dataset matching media_1792379592993_ec79bc63.png.
        /// </summary>
        public void LoadMockupData()
        {
            if (isRunning) return;
            isMockupMode = true;
            badgeConcept.Text = "DỮ LIỆU MINH HỌA";
            lblDisclaimer.Text = "DỮ LIỆU MINH HỌA — không phải kết quả CAD. Bấm Bắt đầu kiểm tra để đọc chi tiết đang mở.";
            badgeConcept.BackColor = Color.FromArgb(241, 245, 249);
            badgeConcept.ForeColor = Color.FromArgb(71, 85, 105);

            var mockup = BatchViewModelConverter.CreateMockupData();
            currentSummary = mockup.Item1;
            allParts = mockup.Item2;

            RenderSummary();
            SetRunMessage("DỮ LIỆU MINH HỌA — không phải kết quả CAD.");
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
            lblProgressTitle.Text = $"Đã xử lý {currentSummary.CheckedParts} lượt kiểm tra · {currentSummary.UniqueParts} mẫu chi tiết/cấu hình";
            lblProgressPercent.Text = $"{currentSummary.ProgressPercent}%";
            pnlProgressBar.Invalidate();

            // Metric Cards
            lblMetrics1Val.Text = currentSummary.TotalComponents.ToString();
            lblMetrics2Val.Text = currentSummary.UniqueParts.ToString();
            lblMetrics3Val.Text = currentSummary.UnknownParts.ToString();
            lblMetrics4Val.Text = $"{currentSummary.ClearParts} chưa thấy • {currentSummary.CollisionParts} có*";
            toolTips.SetToolTip(lblMetrics4Val,"Va chạm theo phương án dao trong phạm vi đã kiểm tra; chưa xác minh khả năng sản xuất.");
            lblMetrics5Val.Text = currentSummary.SkippedComponents.ToString();

            // Diagnostics Text
            txtDiagnostics.Clear();
            if (currentSummary.Diagnostics != null && currentSummary.Diagnostics.Count > 0)
            {
                txtDiagnostics.Text = string.Join("\r\n\r\n• ", currentSummary.Diagnostics);
                if (!txtDiagnostics.Text.StartsWith("• ")) txtDiagnostics.Text = "• " + txtDiagnostics.Text;
            }
            else txtDiagnostics.Text = "Chưa có dữ liệu kiểm tra.";
            if(!technicalDetails)
            {
                // An empty/failed run must keep its operational error or
                // instruction visible. Generic missing-evidence text cannot
                // replace the reason the task produced no Part results.
                txtDiagnostics.Text=allParts.Count==0&&currentSummary.Diagnostics?.Count>0?
                    string.Join("\r\n",currentSummary.Diagnostics):
                    currentSummary.NumericQueries==0&&currentSummary.ClearParts+currentSummary.CollisionParts==0?
                    "Chưa thực hiện phép tính va chạm vì chưa đủ dữ liệu. Xem hồ sơ gá, chiều dài dao và trạng thái phôi trung gian.":
                    "Lượt tính va chạm trong lần kiểm tra: "+currentSummary.NumericQueries+". Kết quả dùng lại phải còn hiệu lực.";
                txtDiagnostics.AppendText("\r\nChưa xác minh điều kiện sản xuất. Không kiểm tra cối V. Bật Chi tiết kỹ thuật để xem mã gốc và số liệu kiểm thử.");
            }
            string stages = currentSummary.StageTimingsAvailable ?
                $"Đọc hình học: {currentSummary.NativeCaptureSeconds:0.000} s | Tính toán: {currentSummary.WorkerSeconds:0.000} s" :
                "Đọc hình học / Tính toán: Chưa có dữ liệu";
            txtDiagnostics.AppendText($"\r\n\r\nTổng: {currentSummary.ElapsedSeconds:0.000}s | {stages}");
        }

        private void ApplyPartFilters()
        {
            string search = txtSearch.Text;
            if (search == "Tìm chi tiết / cấu hình...") search = string.Empty;
            search = (search ?? string.Empty).Trim().ToLowerInvariant();

            int filterIndex = cboStatusFilter.SelectedIndex;
            partPage = 0;

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

            int previousIndex = displayedParts.FindIndex(p => p.SelectionKey == preservePart);
            if (previousIndex >= 0) partPage = previousIndex / PartsPerPage;

            PopulatePartsGrid();
            UxV4RefreshParts();
        }

        private void PopulatePartsGrid()
        {
            bindingParts = true; dgvParts.SuspendLayout();
            try
            {
            dgvParts.Rows.Clear();
            partPage = Math.Min(partPage, Math.Max(0, (displayedParts.Count - 1) / PartsPerPage));
            pageLabel.Text = (partPage + 1) + "/" + Math.Max(1, (int)Math.Ceiling(displayedParts.Count / (double)PartsPerPage));
            foreach (var part in displayedParts.Skip(partPage * PartsPerPage).Take(PartsPerPage))
            {
                int rowIndex = dgvParts.Rows.Add(
                    part.PartName,
                    part.Configuration,
                    part.Quantity,
                    part.BendCount,
                    part.StatusDisplay,
                    BendCheckDisplayText.PartReason(part,technicalDetails)
                );
                dgvParts.Rows[rowIndex].Tag = part;
            }
            }
            finally { dgvParts.ResumeLayout(); bindingParts = false; }

            if (dgvParts.Rows.Count > 0)
            {
                if (displayedParts.Count == 1 && splitMain.Height > 400)
                {
                    splitMain.Panel1MinSize = 110;
                    splitMain.SplitterDistance = Math.Min(splitMain.SplitterDistance, Math.Max(110,
                        pnlPartsHeader.Height + dgvParts.ColumnHeadersHeight + dgvParts.Rows[0].Height + 16));
                }
                var row = dgvParts.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (r.Tag as BatchPartViewModel)?.SelectionKey == preservePart) ??
                    dgvParts.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (r.Tag as BatchPartViewModel)?.Status != CollisionStatus.CLEAR) ?? dgvParts.Rows[0];
                dgvParts.CurrentCell = row.Cells[0]; row.Selected = true;
                selectedPart = row.Tag as BatchPartViewModel;
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
            sequenceView.SetResult(part?.RawPartResult?.Planning, isMockupMode, part?.RawPartResult?.Reason ?? part?.Reason);
            RenderMinimum(part);
            if (part == null || part.Bends == null || part.Bends.Count == 0)
            {
                ClearDetailSection();
                selectedPart = part; RenderMinimum(part);
                return;
            }

            // Create bend selector pills
            pnlBendPills.SuspendLayout();
            ClearBendPills();

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

            SelectBend(part.Bends.FirstOrDefault(b => b.FeatureId == preserveFeature) ?? firstBend);
        }

        private void RenderMinimum(BatchPartViewModel part)
        {
            minimumEvidence.Rows.Clear(); minimumDescription.Text = "Chưa có kết quả cạnh chấn tối thiểu của lần đọc hiện tại.";
            if (isMockupMode || part?.RawPartResult?.Current != true) return;
            foreach (var bend in part.RawPartResult.BendMetadata)
            {
                string status = bend.MinimumStatus == "MeetsMinimum" ? "Đạt Min" : bend.MinimumStatus == "BelowMinimum" ? "Không đạt tối thiểu" :
                    bend.MinimumStatus == "GroovedAccepted" ? "Cạnh bào" : "Cần xác minh";
                int sides = Math.Max(1, bend.Sides.Count);
                for (int side = 0; side < sides; side++)
                {
                    var datum = bend.Sides.Count > side ? bend.Sides[side] : null;
                    int row = minimumEvidence.Rows.Add(bend.FeatureId, status, side + 1, datum == null ? "—" : datum.Shape + "/" + datum.Dimension,
                        datum?.ActualMm.ToString("0.###"), datum?.MinimumMm?.ToString("0.###") ?? "—");
                    minimumEvidence.Rows[row].Tag = bend;
                    foreach (DataGridViewCell cell in minimumEvidence.Rows[row].Cells) cell.ToolTipText = bend.Name + "\n" + bend.MinimumSummary;
                }
            }
            minimumDescription.Text = "Kết quả cạnh chấn tối thiểu hiện có · kích thước phủ bì · L→A, Z→B, C(U)→C. Chọn dòng để xem lý do và sáng nếp trên CAD.\r\n" +
                string.Join("\r\n", part.RawPartResult.BendMetadata.Select(b => b.FeatureId + ": " + b.MinimumSummary));
        }

        private void SelectBend(BatchBendViewModel bend)
        {
            if (bend == null || selectedPart == null) return;
            selectedBend = bend;
            preserveFeature = bend.FeatureId; preservePart = selectedPart.SelectionKey;

            lblDetailBendTitle.Text = $"{selectedPart.PartName} / {bend.BendName} · Góc: {bend.AngleDisplay} · R: {bend.RadiusDisplay}";
            toolTips.SetToolTip(lblDetailBendTitle, $"{selectedPart.PartName} / {bend.BendName} (Mã nếp SOLIDWORKS {bend.FeatureId})\nGóc chấn: {bend.AngleDisplay}\nBán kính: {bend.RadiusDisplay}\nTrạng thái: {bend.StatusDisplay}");

            // Highlight active bend button
            foreach (Control ctrl in pnlBendPills.Controls)
            {
                var mb = ctrl as ModernButton;
                if (mb != null)
                {
                    bool active = ReferenceEquals(mb.Tag, bend);
                    mb.Selected = active;
                    mb.Font = active ? bendActiveFont : bendNormalFont;
                }
            }

            if (!isMockupMode) BendLocateRequested?.Invoke(this, Tuple.Create(selectedPart.SelectionKey, bend.FeatureId));
            PopulateToolsGrid(bend);
            UxV4RefreshSelection();
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
                    Explain(tool.CombinedReason)
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
                    if (cand != null && cand.ToolId == (preserveTool ?? "AMD-STD-004"))
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
            preserveTool = tool.ToolId;
            RenderToolCallout(tool);
        }

        private void RenderToolCallout(BatchToolCandidateViewModel tool,bool updateReview=true)
        {
            var partForReview=selectedPart?.RawPartResult;
            if(updateReview)toolEvidence.ShowTool(tool.CadAssembly,tool.NativeEnvelope,()=>!isMockupMode&&partForReview?.Current==true);
            phaseEvidence.Rows.Clear();
            var cad=tool.CadAssembly?.CadPair;
            if(cad!=null)
            {
                phaseEvidence.Rows.Add("CAD / Chày chấn",cad.Matched?"Đã nhận đúng nguồn":"Chưa xác nhận",technicalDetails?cad.PunchBlock+" · INSERT "+cad.PunchInsert+" · "+string.Join(", ",cad.PunchLayers):"Cặp chày–đầu kẹp cố định theo CAD; xem mã nguồn ở chi tiết kỹ thuật.");
                phaseEvidence.Rows.Add("CAD / Đầu kẹp chày",cad.Matched?"Đã nhận đúng nguồn":"Chưa xác nhận",technicalDetails?cad.HolderBlock+" · INSERT "+cad.HolderInsert+" · "+string.Join(", ",cad.HolderLayers):"Hình học tham khảo; chưa xác nhận gá thực tế.");
                phaseEvidence.Rows.Add("CAD / Gá",tool.CadAssembly.ClampVerified?"Đã kiểm tra hồ sơ gá":tool.CadAssembly.SeatingEvidence.CoordinateRecordReady?"Có tọa độ được kiểm tra":"Chưa đủ dữ liệu",
                    tool.CadAssembly.ClampVerified?"Phạm vi hồ sơ gá đã xác minh; chiều dài và quá trình vẫn được kiểm tra riêng.":"Chưa xác minh đầy đủ mốc gá và gá thực tế; chiều dài làm việc cần hồ sơ riêng.");
            }
            phaseEvidence.Rows.Add("Vị trí tĩnh / Chày chấn",tool.PunchStatusDisplay,(tool.StaticSimulationOnly?"Dữ liệu mô phỏng · ":"")+Explain(tool.PunchReason)+
                " · khoảng hở: "+BendCheckDisplayText.Clearance(tool.PunchClearanceMm));
            phaseEvidence.Rows.Add("Vị trí tĩnh / Đầu kẹp chày",tool.HolderStatusDisplay,(tool.StaticSimulationOnly?"Dữ liệu mô phỏng · ":"")+Explain(tool.HolderReason)+
                " · khoảng hở: "+BendCheckDisplayText.Clearance(tool.HolderClearanceMm));
            foreach (var segment in tool.MotionSegments)
                phaseEvidence.Rows.Add(BendCheckDisplayText.Phase(segment.Phase) + " / " + (segment.Pair == CollisionPairKind.WorkpiecePunch ? "Chày chấn" : "Đầu kẹp chày"), BendCheckDisplayText.Collision(segment.Status),
                    (tool.ProcessSimulationOnly?"Dữ liệu mô phỏng · ":"")+(segment.Witness == null ? Explain(segment.Reason) : "Bằng chứng tại " + (segment.Witness.Parameter*100).ToString("0.####") + "% chuyển động · độ dự phòng >" + segment.Witness.Geometry.CrossSectionInteriorMarginLowerMm.ToString("0.######") + " mm"));
            lblCalloutTitle.Text = $"Chi tiết dao — {tool.ToolId}";
            lblCalloutPunch.Text = $"Chày chấn – {tool.ToolId}: {tool.PunchStatusDisplay}";
            lblCalloutHolder.Text = $"Đầu kẹp chày: {tool.HolderStatusDisplay}"+(cad?.Matched==true?" · CAD tham khảo":"");
            lblCalloutReason.Text = "Lý do: "+Explain(tool.CombinedReason);

            // Color coding
            lblCalloutPunch.ForeColor = tool.PunchStatus == CollisionStatus.COLLISION ? TaiTheme.Danger :
                                       tool.PunchStatus == CollisionStatus.CLEAR ? TaiTheme.Success : Color.FromArgb(51, 65, 85);

            lblCalloutHolder.ForeColor = tool.HolderStatus == CollisionStatus.COLLISION ? TaiTheme.Danger :
                                        tool.HolderStatus == CollisionStatus.CLEAR ? TaiTheme.Success : Color.FromArgb(51, 65, 85);

            toolTips.SetToolTip(lblCalloutTitle, $"Ứng viên: {tool.ToolId} (Mã nếp SOLIDWORKS {tool.FeatureId})");
            if(cad!=null)toolTips.SetToolTip(lblCalloutTitle, $"{tool.ToolId} · ô {cad.SlotId} · {cad.Status}\nDXF SHA256: {cad.SourceSha256}\n{cad.CoordinateSystem}\n{cad.EvidenceReference}");
            toolTips.SetToolTip(lblCalloutPunch, $"Punch ({tool.ToolId}): {tool.PunchStatusDisplay}");
            toolTips.SetToolTip(lblCalloutHolder, $"Đầu kẹp chày: {tool.HolderStatusDisplay}");
            toolTips.SetToolTip(lblCalloutPunch, "Chày chấn: "+tool.PunchStatusDisplay+" · Khoảng hở: "+BendCheckDisplayText.Clearance(tool.PunchClearanceMm)+"\n"+Explain(tool.PunchReason)+"\nChi tiết kỹ thuật: "+tool.PunchReason+
                (technicalDetails&&tool.PunchWitness!=null?"\nVị trí bằng chứng va chạm: "+tool.PunchWitness.PointMm:""));
            toolTips.SetToolTip(lblCalloutHolder, "Đầu kẹp chày: "+tool.HolderStatusDisplay+" · Khoảng hở: "+BendCheckDisplayText.Clearance(tool.HolderClearanceMm)+"\n"+Explain(tool.HolderReason)+"\nChi tiết kỹ thuật: "+tool.HolderReason+
                (technicalDetails&&tool.HolderWitness!=null?"\nVị trí bằng chứng va chạm: "+tool.HolderWitness.PointMm:""));
            toolTips.SetToolTip(lblCalloutReason, $"Lý do / Bằng chứng:\n{tool.CombinedReason}");
            UxV4RefreshToolStatus();
        }

        private void ClearDetailSection()
        {
            toolEvidence.ShowTool(null,null,null);
            selectedPart = null; selectedBend = null; selectedTool = null;
            lblDetailBendTitle.Text = "Chi tiết nếp chấn — Chưa chọn chi tiết";
            toolTips.SetToolTip(lblDetailBendTitle, "Chọn một chi tiết bên trên để xem các nếp chấn.");
            ClearBendPills();
            dgvTools.Rows.Clear();
            phaseEvidence.Rows.Clear();
            minimumEvidence.Rows.Clear(); minimumDescription.Clear();
            lblCalloutTitle.Text = "Chi tiết dao đang chọn";
            lblCalloutPunch.Text = "Chày chấn: —";
            lblCalloutHolder.Text = "Đầu kẹp chày: —";
            lblCalloutReason.Text = "Chọn một chi tiết bên trên để xem đánh giá 18 bộ dao.";
            toolTips.SetToolTip(lblCalloutPunch, null);
            toolTips.SetToolTip(lblCalloutHolder, null);
            toolTips.SetToolTip(lblCalloutReason, null);
        }
        private void ClearBendPills()
        {
            foreach (Control control in pnlBendPills.Controls.Cast<Control>().ToArray()) control.Dispose();
            pnlBendPills.Controls.Clear();
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
                    e.ToolTipText = $"{part.PartName}\nMã liên kết nguồn: {part.SourceKey}\nSố lượng: {part.Quantity}\nSố nếp chấn: {part.BendCount}";
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
                    e.ToolTipText = $"Bộ dao: {tool.ToolId} (Mã nếp SOLIDWORKS {tool.FeatureId})";
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

                int occRowH = TaiTheme.Px(this, 30);
                grid.RowTemplate.Height = occRowH;
                grid.ColumnHeadersHeight = TaiTheme.Px(this, 30);

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
                        grid.Rows[idx].Height = occRowH;
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
                    Text = $"Tổng số: {selectedPart.Occurrences.Count} vị trí lắp · Nhấp đúp vào dòng để làm sáng trên SOLIDWORKS",
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
                var status=(dgvParts.Rows[e.RowIndex].Tag as BatchPartViewModel)?.Status??CollisionStatus.UNKNOWN;

                if (status==CollisionStatus.COLLISION)
                {
                    textColor = Color.FromArgb(220, 38, 38);
                    bgColor = Color.FromArgb(254, 226, 226);
                }
                else if (status==CollisionStatus.CLEAR)
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

                int pillH = TaiTheme.Px(this, 34);
                int pillW = e.CellBounds.Width - 8;
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
                var item=dgvTools.Rows[e.RowIndex].Tag as BatchToolCandidateViewModel;
                var status=item==null?CollisionStatus.UNKNOWN:e.ColumnIndex==1?item.PunchStatus:e.ColumnIndex==2?item.HolderStatus:item.GeometricStatus;

                if (status==CollisionStatus.COLLISION)
                {
                    textColor = Color.FromArgb(220, 38, 38);
                    bgColor = Color.FromArgb(254, 226, 226);
                }
                else if (status==CollisionStatus.CLEAR)
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

                int pillH = TaiTheme.Px(this, 34);
                int pillW = e.CellBounds.Width - 8;
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
