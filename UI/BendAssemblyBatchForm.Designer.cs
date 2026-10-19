namespace ADDIN.UI
{
    partial class BendAssemblyBatchForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            this.pnlRoot = new System.Windows.Forms.TableLayoutPanel();
            this.pnlHeader = new System.Windows.Forms.Panel();
            this.lblHeaderTitle = new System.Windows.Forms.Label();
            this.lblHeaderSub = new System.Windows.Forms.Label();
            this.badgeConcept = new System.Windows.Forms.Label();
            this.btnToggleMockup = new ADDIN.UI.ModernButton();
            this.pnlControlBar = new System.Windows.Forms.Panel();
            this.pnlScope = new System.Windows.Forms.FlowLayoutPanel();
            this.btnScopeEntire = new ADDIN.UI.ModernButton();
            this.btnScopeSelected = new ADDIN.UI.ModernButton();
            this.btnRunBatch = new ADDIN.UI.ModernButton();
            this.btnCancel = new ADDIN.UI.ModernButton();
            this.btnRefresh = new ADDIN.UI.ModernButton();
            this.cboStatusFilter = new System.Windows.Forms.ComboBox();
            this.txtSearch = new System.Windows.Forms.TextBox();
            this.pnlOverview = new System.Windows.Forms.Panel();
            this.lblProgressTitle = new System.Windows.Forms.Label();
            this.lblProgressPercent = new System.Windows.Forms.Label();
            this.pnlProgressBar = new System.Windows.Forms.Panel();
            this.tblMetrics = new System.Windows.Forms.TableLayoutPanel();
            this.cardMetrics1 = new System.Windows.Forms.Panel();
            this.lblMetrics1Val = new System.Windows.Forms.Label();
            this.lblMetrics1Title = new System.Windows.Forms.Label();
            this.cardMetrics2 = new System.Windows.Forms.Panel();
            this.lblMetrics2Val = new System.Windows.Forms.Label();
            this.lblMetrics2Title = new System.Windows.Forms.Label();
            this.cardMetrics3 = new System.Windows.Forms.Panel();
            this.lblMetrics3Val = new System.Windows.Forms.Label();
            this.lblMetrics3Title = new System.Windows.Forms.Label();
            this.cardMetrics4 = new System.Windows.Forms.Panel();
            this.lblMetrics4Val = new System.Windows.Forms.Label();
            this.lblMetrics4Title = new System.Windows.Forms.Label();
            this.cardMetrics5 = new System.Windows.Forms.Panel();
            this.lblMetrics5Val = new System.Windows.Forms.Label();
            this.lblMetrics5Title = new System.Windows.Forms.Label();
            this.splitMain = new System.Windows.Forms.SplitContainer();
            this.pnlPartsSection = new System.Windows.Forms.Panel();
            this.pnlPartsHeader = new System.Windows.Forms.Panel();
            this.lblPartsTitle = new System.Windows.Forms.Label();
            this.btnToggleOccurrences = new ADDIN.UI.ModernButton();
            this.dgvParts = new System.Windows.Forms.DataGridView();
            this.colPartName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colPartConfig = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colPartQty = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colPartBends = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colPartStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colPartReason = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.splitDetails = new System.Windows.Forms.SplitContainer();
            this.pnlBendsAndTools = new System.Windows.Forms.Panel();
            this.pnlDetailHeader = new System.Windows.Forms.Panel();
            this.lblDetailBendTitle = new System.Windows.Forms.Label();
            this.pnlBendPills = new System.Windows.Forms.FlowLayoutPanel();
            this.dgvTools = new System.Windows.Forms.DataGridView();
            this.colToolId = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colPunch = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colHolder = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colMotion = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colGeomStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colMfgStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colClearance = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colToolReason = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.pnlToolCallout = new System.Windows.Forms.Panel();
            this.lblCalloutTitle = new System.Windows.Forms.Label();
            this.lblCalloutPunch = new System.Windows.Forms.Label();
            this.lblCalloutHolder = new System.Windows.Forms.Label();
            this.lblCalloutReason = new System.Windows.Forms.Label();
            this.pnlDiagnosticsCard = new System.Windows.Forms.Panel();
            this.lblDiagHeader = new System.Windows.Forms.Label();
            this.txtDiagnostics = new System.Windows.Forms.TextBox();
            this.pnlFooter = new System.Windows.Forms.Panel();
            this.lblDisclaimer = new System.Windows.Forms.Label();
            this.btnClose = new ADDIN.UI.ModernButton();
            this.toolTips = new System.Windows.Forms.ToolTip(this.components);
            this.pnlHeaderLeft = new System.Windows.Forms.TableLayoutPanel();
            this.pnlHeaderRight = new System.Windows.Forms.FlowLayoutPanel();
            this.pnlControlLeft = new System.Windows.Forms.FlowLayoutPanel();
            this.pnlControlRight = new System.Windows.Forms.FlowLayoutPanel();
            this.pnlRoot.SuspendLayout();
            this.pnlHeader.SuspendLayout();
            this.pnlHeaderLeft.SuspendLayout();
            this.pnlHeaderRight.SuspendLayout();
            this.pnlControlBar.SuspendLayout();
            this.pnlControlLeft.SuspendLayout();
            this.pnlControlRight.SuspendLayout();
            this.pnlScope.SuspendLayout();
            this.pnlOverview.SuspendLayout();
            this.tblMetrics.SuspendLayout();
            this.cardMetrics1.SuspendLayout();
            this.cardMetrics2.SuspendLayout();
            this.cardMetrics3.SuspendLayout();
            this.cardMetrics4.SuspendLayout();
            this.cardMetrics5.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).BeginInit();
            this.splitMain.Panel1.SuspendLayout();
            this.splitMain.Panel2.SuspendLayout();
            this.splitMain.SuspendLayout();
            this.pnlPartsSection.SuspendLayout();
            this.pnlPartsHeader.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvParts)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.splitDetails)).BeginInit();
            this.splitDetails.Panel1.SuspendLayout();
            this.splitDetails.Panel2.SuspendLayout();
            this.splitDetails.SuspendLayout();
            this.pnlBendsAndTools.SuspendLayout();
            this.pnlDetailHeader.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvTools)).BeginInit();
            this.pnlToolCallout.SuspendLayout();
            this.pnlDiagnosticsCard.SuspendLayout();
            this.pnlFooter.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlRoot
            // 
            this.pnlRoot.ColumnCount = 1;
            this.pnlRoot.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pnlRoot.Controls.Add(this.pnlHeader, 0, 0);
            this.pnlRoot.Controls.Add(this.pnlControlBar, 0, 1);
            this.pnlRoot.Controls.Add(this.pnlOverview, 0, 2);
            this.pnlRoot.Controls.Add(this.splitMain, 0, 3);
            this.pnlRoot.Controls.Add(this.pnlFooter, 0, 4);
            this.pnlRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlRoot.Location = new System.Drawing.Point(0, 0);
            this.pnlRoot.Margin = new System.Windows.Forms.Padding(0);
            this.pnlRoot.Name = "pnlRoot";
            this.pnlRoot.RowCount = 5;
            this.pnlRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 52F));
            this.pnlRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 46F));
            this.pnlRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 126F));
            this.pnlRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pnlRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 46F));
            this.pnlRoot.Size = new System.Drawing.Size(1264, 821);
            this.pnlRoot.TabIndex = 0;
            // 
            // pnlHeader
            // 
            this.pnlHeader.BackColor = System.Drawing.Color.White;
            this.pnlHeader.Controls.Add(this.pnlHeaderLeft);
            this.pnlHeader.Controls.Add(this.pnlHeaderRight);
            this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlHeader.Location = new System.Drawing.Point(0, 0);
            this.pnlHeader.Margin = new System.Windows.Forms.Padding(0);
            this.pnlHeader.Name = "pnlHeader";
            this.pnlHeader.Padding = new System.Windows.Forms.Padding(16, 6, 16, 6);
            this.pnlHeader.Size = new System.Drawing.Size(1264, 52);
            this.pnlHeader.TabIndex = 0;
            // 
            // pnlHeaderRight
            // 
            this.pnlHeaderRight.AutoSize = true;
            this.pnlHeaderRight.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.pnlHeaderRight.Controls.Add(this.badgeConcept);
            this.pnlHeaderRight.Controls.Add(this.btnToggleMockup);
            this.pnlHeaderRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.pnlHeaderRight.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.pnlHeaderRight.Location = new System.Drawing.Point(985, 6);
            this.pnlHeaderRight.Margin = new System.Windows.Forms.Padding(0);
            this.pnlHeaderRight.Name = "pnlHeaderRight";
            this.pnlHeaderRight.Size = new System.Drawing.Size(263, 40);
            this.pnlHeaderRight.TabIndex = 1;
            this.pnlHeaderRight.WrapContents = false;
            // 
            // badgeConcept
            // 
            this.badgeConcept.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(241)))), ((int)(((byte)(245)))), ((int)(((byte)(249)))));
            this.badgeConcept.Font = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Bold);
            this.badgeConcept.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
            this.badgeConcept.Location = new System.Drawing.Point(173, 5);
            this.badgeConcept.Margin = new System.Windows.Forms.Padding(4, 5, 0, 0);
            this.badgeConcept.Name = "badgeConcept";
            this.badgeConcept.Size = new System.Drawing.Size(90, 26);
            this.badgeConcept.TabIndex = 2;
            this.badgeConcept.Text = "UI Concept";
            this.badgeConcept.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // btnToggleMockup
            // 
            this.btnToggleMockup.AutoSize = true;
            this.btnToggleMockup.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnToggleMockup.Kind = ADDIN.UI.ModernButtonKind.Subtle;
            this.btnToggleMockup.Location = new System.Drawing.Point(4, 4);
            this.btnToggleMockup.Margin = new System.Windows.Forms.Padding(0, 4, 4, 0);
            this.btnToggleMockup.Name = "btnToggleMockup";
            this.btnToggleMockup.Padding = new System.Windows.Forms.Padding(10, 0, 10, 0);
            this.btnToggleMockup.Size = new System.Drawing.Size(165, 28);
            this.btnToggleMockup.TabIndex = 3;
            this.btnToggleMockup.Text = "Nạp dữ liệu mẫu";
            this.btnToggleMockup.Tone = ADDIN.UI.ModernButtonTone.Accent;
            this.toolTips.SetToolTip(this.btnToggleMockup, "Nạp bộ dữ liệu giả lập mẫu (120 components, Frame-A, Bracket-B, Panel-C) để xem thử bố cục giao diện");
            // 
            // pnlHeaderLeft
            // 
            this.pnlHeaderLeft.ColumnCount = 2;
            this.pnlHeaderLeft.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.pnlHeaderLeft.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pnlHeaderLeft.Controls.Add(this.lblHeaderTitle, 0, 0);
            this.pnlHeaderLeft.Controls.Add(this.lblHeaderSub, 1, 0);
            this.pnlHeaderLeft.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlHeaderLeft.Location = new System.Drawing.Point(16, 6);
            this.pnlHeaderLeft.Margin = new System.Windows.Forms.Padding(0);
            this.pnlHeaderLeft.Name = "pnlHeaderLeft";
            this.pnlHeaderLeft.RowCount = 1;
            this.pnlHeaderLeft.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.pnlHeaderLeft.Size = new System.Drawing.Size(969, 40);
            this.pnlHeaderLeft.TabIndex = 0;
            // 
            // lblHeaderTitle
            // 
            this.lblHeaderTitle.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblHeaderTitle.AutoSize = true;
            this.lblHeaderTitle.Font = new System.Drawing.Font("Segoe UI", 13.5F, System.Drawing.FontStyle.Bold);
            this.lblHeaderTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(29)))), ((int)(((byte)(95)))), ((int)(((byte)(208)))));
            this.lblHeaderTitle.Location = new System.Drawing.Point(0, 7);
            this.lblHeaderTitle.Margin = new System.Windows.Forms.Padding(0, 0, 12, 0);
            this.lblHeaderTitle.Name = "lblHeaderTitle";
            this.lblHeaderTitle.Size = new System.Drawing.Size(164, 25);
            this.lblHeaderTitle.TabIndex = 0;
            this.lblHeaderTitle.Text = "☵ CHECK CHẤN";
            // 
            // lblHeaderSub
            // 
            this.lblHeaderSub.AutoEllipsis = true;
            this.lblHeaderSub.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblHeaderSub.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblHeaderSub.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(92)))), ((int)(((byte)(104)))), ((int)(((byte)(122)))));
            this.lblHeaderSub.Location = new System.Drawing.Point(176, 0);
            this.lblHeaderSub.Margin = new System.Windows.Forms.Padding(0);
            this.lblHeaderSub.Name = "lblHeaderSub";
            this.lblHeaderSub.Size = new System.Drawing.Size(793, 40);
            this.lblHeaderSub.TabIndex = 1;
            this.lblHeaderSub.Text = "Kiểm tra chấn hàng loạt cụm chi tiết (Assembly)";
            this.lblHeaderSub.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // pnlControlBar
            // 
            this.pnlControlBar.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(243)))), ((int)(((byte)(245)))), ((int)(((byte)(248)))));
            this.pnlControlBar.Controls.Add(this.pnlControlLeft);
            this.pnlControlBar.Controls.Add(this.pnlControlRight);
            this.pnlControlBar.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlControlBar.Location = new System.Drawing.Point(0, 52);
            this.pnlControlBar.Margin = new System.Windows.Forms.Padding(0);
            this.pnlControlBar.Name = "pnlControlBar";
            this.pnlControlBar.Size = new System.Drawing.Size(1264, 46);
            this.pnlControlBar.TabIndex = 1;
            // 
            // pnlControlRight
            // 
            this.pnlControlRight.AutoSize = true;
            this.pnlControlRight.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.pnlControlRight.Controls.Add(this.btnRunBatch);
            this.pnlControlRight.Controls.Add(this.btnCancel);
            this.pnlControlRight.Controls.Add(this.btnRefresh);
            this.pnlControlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.pnlControlRight.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.pnlControlRight.Location = new System.Drawing.Point(908, 0);
            this.pnlControlRight.Margin = new System.Windows.Forms.Padding(0);
            this.pnlControlRight.Name = "pnlControlRight";
            this.pnlControlRight.Padding = new System.Windows.Forms.Padding(0, 6, 16, 6);
            this.pnlControlRight.Size = new System.Drawing.Size(356, 46);
            this.pnlControlRight.TabIndex = 1;
            this.pnlControlRight.WrapContents = false;
            // 
            // btnRunBatch
            // 
            this.btnRunBatch.AutoSize = true;
            this.btnRunBatch.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.btnRunBatch.Icon = ADDIN.UI.TaiIcon.Refresh;
            this.btnRunBatch.Kind = ADDIN.UI.ModernButtonKind.Primary;
            this.btnRunBatch.Location = new System.Drawing.Point(180, 6);
            this.btnRunBatch.Margin = new System.Windows.Forms.Padding(6, 0, 0, 0);
            this.btnRunBatch.Name = "btnRunBatch";
            this.btnRunBatch.Padding = new System.Windows.Forms.Padding(12, 0, 12, 0);
            this.btnRunBatch.Size = new System.Drawing.Size(160, 34);
            this.btnRunBatch.TabIndex = 1;
            this.btnRunBatch.Text = "Run Batch Check";
            this.btnRunBatch.Tone = ADDIN.UI.ModernButtonTone.Success;
            this.toolTips.SetToolTip(this.btnRunBatch, "Bắt đầu chạy kiểm tra chấn hàng loạt cho các chi tiết");
            // 
            // btnCancel
            // 
            this.btnCancel.AutoSize = true;
            this.btnCancel.Enabled = false;
            this.btnCancel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnCancel.Icon = ADDIN.UI.TaiIcon.Stop;
            this.btnCancel.Kind = ADDIN.UI.ModernButtonKind.Standard;
            this.btnCancel.Location = new System.Drawing.Point(97, 6);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(4, 0, 0, 0);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Padding = new System.Windows.Forms.Padding(10, 0, 10, 0);
            this.btnCancel.Size = new System.Drawing.Size(77, 34);
            this.btnCancel.TabIndex = 2;
            this.btnCancel.Text = "Dừng";
            this.btnCancel.Tone = ADDIN.UI.ModernButtonTone.Danger;
            // 
            // btnRefresh
            // 
            this.btnRefresh.AutoSize = true;
            this.btnRefresh.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnRefresh.Icon = ADDIN.UI.TaiIcon.Refresh;
            this.btnRefresh.Kind = ADDIN.UI.ModernButtonKind.Standard;
            this.btnRefresh.Location = new System.Drawing.Point(2, 6);
            this.btnRefresh.Margin = new System.Windows.Forms.Padding(4, 0, 0, 0);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Padding = new System.Windows.Forms.Padding(10, 0, 10, 0);
            this.btnRefresh.Size = new System.Drawing.Size(91, 34);
            this.btnRefresh.TabIndex = 3;
            this.btnRefresh.Text = "Quét lại";
            this.btnRefresh.Tone = ADDIN.UI.ModernButtonTone.Accent;
            // 
            // pnlControlLeft
            // 
            this.pnlControlLeft.Controls.Add(this.pnlScope);
            this.pnlControlLeft.Controls.Add(this.cboStatusFilter);
            this.pnlControlLeft.Controls.Add(this.txtSearch);
            this.pnlControlLeft.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlControlLeft.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            this.pnlControlLeft.Location = new System.Drawing.Point(0, 0);
            this.pnlControlLeft.Margin = new System.Windows.Forms.Padding(0);
            this.pnlControlLeft.Name = "pnlControlLeft";
            this.pnlControlLeft.Padding = new System.Windows.Forms.Padding(16, 6, 4, 6);
            this.pnlControlLeft.Size = new System.Drawing.Size(908, 46);
            this.pnlControlLeft.TabIndex = 0;
            this.pnlControlLeft.WrapContents = false;
            // 
            // pnlScope
            // 
            this.pnlScope.AutoSize = true;
            this.pnlScope.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.pnlScope.Controls.Add(this.btnScopeEntire);
            this.pnlScope.Controls.Add(this.btnScopeSelected);
            this.pnlScope.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            this.pnlScope.Location = new System.Drawing.Point(16, 6);
            this.pnlScope.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            this.pnlScope.Name = "pnlScope";
            this.pnlScope.Size = new System.Drawing.Size(328, 34);
            this.pnlScope.TabIndex = 0;
            this.pnlScope.WrapContents = false;
            // 
            // btnScopeEntire
            // 
            this.btnScopeEntire.AutoSize = true;
            this.btnScopeEntire.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnScopeEntire.Kind = ADDIN.UI.ModernButtonKind.Standard;
            this.btnScopeEntire.Location = new System.Drawing.Point(0, 0);
            this.btnScopeEntire.Margin = new System.Windows.Forms.Padding(0, 0, 4, 0);
            this.btnScopeEntire.Name = "btnScopeEntire";
            this.btnScopeEntire.Padding = new System.Windows.Forms.Padding(10, 0, 10, 0);
            this.btnScopeEntire.Selected = true;
            this.btnScopeEntire.Size = new System.Drawing.Size(145, 32);
            this.btnScopeEntire.TabIndex = 0;
            this.btnScopeEntire.Text = "Entire Assembly";
            this.btnScopeEntire.Tone = ADDIN.UI.ModernButtonTone.Accent;
            this.toolTips.SetToolTip(this.btnScopeEntire, "Kiểm tra toàn bộ các chi tiết kim loại tấm trong cụm lắp ráp");
            // 
            // btnScopeSelected
            // 
            this.btnScopeSelected.AutoSize = true;
            this.btnScopeSelected.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnScopeSelected.Kind = ADDIN.UI.ModernButtonKind.Standard;
            this.btnScopeSelected.Location = new System.Drawing.Point(149, 0);
            this.btnScopeSelected.Margin = new System.Windows.Forms.Padding(0);
            this.btnScopeSelected.Name = "btnScopeSelected";
            this.btnScopeSelected.Padding = new System.Windows.Forms.Padding(10, 0, 10, 0);
            this.btnScopeSelected.Size = new System.Drawing.Size(175, 32);
            this.btnScopeSelected.TabIndex = 1;
            this.btnScopeSelected.Text = "Selected Components";
            this.btnScopeSelected.Tone = ADDIN.UI.ModernButtonTone.Neutral;
            this.toolTips.SetToolTip(this.btnScopeSelected, "Chỉ kiểm tra các component đang được chọn trong cây FeatureTree");
            // 
            // cboStatusFilter
            // 
            this.cboStatusFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboStatusFilter.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.cboStatusFilter.FormattingEnabled = true;
            this.cboStatusFilter.Items.AddRange(new object[] {
            "Tất cả trạng thái",
            "Chỉ lỗi (COLLISION*)",
            "Chưa xác minh (UNKNOWN)",
            "Đạt hình học (CLEAR*)"});
            this.cboStatusFilter.Location = new System.Drawing.Point(352, 10);
            this.cboStatusFilter.Margin = new System.Windows.Forms.Padding(8, 4, 8, 4);
            this.cboStatusFilter.Name = "cboStatusFilter";
            this.cboStatusFilter.Size = new System.Drawing.Size(185, 25);
            this.cboStatusFilter.TabIndex = 4;
            // 
            // txtSearch
            // 
            this.txtSearch.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.txtSearch.Location = new System.Drawing.Point(553, 10);
            this.txtSearch.Margin = new System.Windows.Forms.Padding(0, 4, 4, 4);
            this.txtSearch.Name = "txtSearch";
            this.txtSearch.Size = new System.Drawing.Size(190, 24);
            this.txtSearch.TabIndex = 5;
            this.toolTips.SetToolTip(this.txtSearch, "Lọc danh sách chi tiết theo tên Part hoặc cấu hình");
            // 
            // pnlOverview
            // 
            this.pnlOverview.BackColor = System.Drawing.Color.White;
            this.pnlOverview.Controls.Add(this.tblMetrics);
            this.pnlOverview.Controls.Add(this.pnlProgressBar);
            this.pnlOverview.Controls.Add(this.lblProgressPercent);
            this.pnlOverview.Controls.Add(this.lblProgressTitle);
            this.pnlOverview.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlOverview.Location = new System.Drawing.Point(0, 98);
            this.pnlOverview.Margin = new System.Windows.Forms.Padding(0);
            this.pnlOverview.Name = "pnlOverview";
            this.pnlOverview.Padding = new System.Windows.Forms.Padding(16, 8, 16, 8);
            this.pnlOverview.Size = new System.Drawing.Size(1264, 126);
            this.pnlOverview.TabIndex = 2;
            // 
            // lblProgressTitle
            // 
            this.lblProgressTitle.AutoSize = true;
            this.lblProgressTitle.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblProgressTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
            this.lblProgressTitle.Location = new System.Drawing.Point(16, 8);
            this.lblProgressTitle.Name = "lblProgressTitle";
            this.lblProgressTitle.Size = new System.Drawing.Size(149, 17);
            this.lblProgressTitle.TabIndex = 0;
            this.lblProgressTitle.Text = "Đã kiểm tra 18 / 25 Part";
            // 
            // lblProgressPercent
            // 
            this.lblProgressPercent.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblProgressPercent.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.lblProgressPercent.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
            this.lblProgressPercent.Location = new System.Drawing.Point(1178, 6);
            this.lblProgressPercent.Name = "lblProgressPercent";
            this.lblProgressPercent.Size = new System.Drawing.Size(70, 20);
            this.lblProgressPercent.TabIndex = 1;
            this.lblProgressPercent.Text = "72%";
            this.lblProgressPercent.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // pnlProgressBar
            // 
            this.pnlProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.pnlProgressBar.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(232)))), ((int)(((byte)(240)))));
            this.pnlProgressBar.Location = new System.Drawing.Point(16, 30);
            this.pnlProgressBar.Name = "pnlProgressBar";
            this.pnlProgressBar.Size = new System.Drawing.Size(1232, 8);
            this.pnlProgressBar.TabIndex = 2;
            // 
            // tblMetrics
            // 
            this.tblMetrics.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tblMetrics.ColumnCount = 5;
            this.tblMetrics.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tblMetrics.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tblMetrics.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tblMetrics.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tblMetrics.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tblMetrics.Controls.Add(this.cardMetrics1, 0, 0);
            this.tblMetrics.Controls.Add(this.cardMetrics2, 1, 0);
            this.tblMetrics.Controls.Add(this.cardMetrics3, 2, 0);
            this.tblMetrics.Controls.Add(this.cardMetrics4, 3, 0);
            this.tblMetrics.Controls.Add(this.cardMetrics5, 4, 0);
            this.tblMetrics.Location = new System.Drawing.Point(16, 46);
            this.tblMetrics.Name = "tblMetrics";
            this.tblMetrics.RowCount = 1;
            this.tblMetrics.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tblMetrics.Size = new System.Drawing.Size(1232, 68);
            this.tblMetrics.TabIndex = 3;
            // 
            // cardMetrics1
            // 
            this.cardMetrics1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
            this.cardMetrics1.Controls.Add(this.lblMetrics1Val);
            this.cardMetrics1.Controls.Add(this.lblMetrics1Title);
            this.cardMetrics1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cardMetrics1.Location = new System.Drawing.Point(0, 0);
            this.cardMetrics1.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            this.cardMetrics1.Name = "cardMetrics1";
            this.cardMetrics1.Padding = new System.Windows.Forms.Padding(12, 6, 12, 6);
            this.cardMetrics1.Size = new System.Drawing.Size(238, 68);
            this.cardMetrics1.TabIndex = 0;
            // 
            // lblMetrics1Val
            // 
            this.lblMetrics1Val.AutoSize = true;
            this.lblMetrics1Val.Font = new System.Drawing.Font("Segoe UI", 16F, System.Drawing.FontStyle.Bold);
            this.lblMetrics1Val.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
            this.lblMetrics1Val.Location = new System.Drawing.Point(8, 26);
            this.lblMetrics1Val.Name = "lblMetrics1Val";
            this.lblMetrics1Val.Size = new System.Drawing.Size(49, 30);
            this.lblMetrics1Val.TabIndex = 1;
            this.lblMetrics1Val.Text = "120";
            // 
            // lblMetrics1Title
            // 
            this.lblMetrics1Title.AutoSize = true;
            this.lblMetrics1Title.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblMetrics1Title.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(116)))), ((int)(((byte)(139)))));
            this.lblMetrics1Title.Location = new System.Drawing.Point(8, 8);
            this.lblMetrics1Title.Name = "lblMetrics1Title";
            this.lblMetrics1Title.Size = new System.Drawing.Size(75, 15);
            this.lblMetrics1Title.TabIndex = 0;
            this.lblMetrics1Title.Text = "Components";
            // 
            // cardMetrics2
            // 
            this.cardMetrics2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
            this.cardMetrics2.Controls.Add(this.lblMetrics2Val);
            this.cardMetrics2.Controls.Add(this.lblMetrics2Title);
            this.cardMetrics2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cardMetrics2.Location = new System.Drawing.Point(246, 0);
            this.cardMetrics2.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            this.cardMetrics2.Name = "cardMetrics2";
            this.cardMetrics2.Padding = new System.Windows.Forms.Padding(12, 6, 12, 6);
            this.cardMetrics2.Size = new System.Drawing.Size(238, 68);
            this.cardMetrics2.TabIndex = 1;
            // 
            // lblMetrics2Val
            // 
            this.lblMetrics2Val.AutoSize = true;
            this.lblMetrics2Val.Font = new System.Drawing.Font("Segoe UI", 16F, System.Drawing.FontStyle.Bold);
            this.lblMetrics2Val.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
            this.lblMetrics2Val.Location = new System.Drawing.Point(8, 26);
            this.lblMetrics2Val.Name = "lblMetrics2Val";
            this.lblMetrics2Val.Size = new System.Drawing.Size(37, 30);
            this.lblMetrics2Val.TabIndex = 1;
            this.lblMetrics2Val.Text = "25";
            // 
            // lblMetrics2Title
            // 
            this.lblMetrics2Title.AutoSize = true;
            this.lblMetrics2Title.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblMetrics2Title.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(116)))), ((int)(((byte)(139)))));
            this.lblMetrics2Title.Location = new System.Drawing.Point(8, 8);
            this.lblMetrics2Title.Name = "lblMetrics2Title";
            this.lblMetrics2Title.Size = new System.Drawing.Size(73, 15);
            this.lblMetrics2Title.TabIndex = 0;
            this.lblMetrics2Title.Text = "Unique Parts";
            // 
            // cardMetrics3
            // 
            this.cardMetrics3.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
            this.cardMetrics3.Controls.Add(this.lblMetrics3Val);
            this.cardMetrics3.Controls.Add(this.lblMetrics3Title);
            this.cardMetrics3.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cardMetrics3.Location = new System.Drawing.Point(492, 0);
            this.cardMetrics3.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            this.cardMetrics3.Name = "cardMetrics3";
            this.cardMetrics3.Padding = new System.Windows.Forms.Padding(12, 6, 12, 6);
            this.cardMetrics3.Size = new System.Drawing.Size(238, 68);
            this.cardMetrics3.TabIndex = 2;
            // 
            // lblMetrics3Val
            // 
            this.lblMetrics3Val.AutoSize = true;
            this.lblMetrics3Val.Font = new System.Drawing.Font("Segoe UI", 16F, System.Drawing.FontStyle.Bold);
            this.lblMetrics3Val.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(116)))), ((int)(((byte)(139)))));
            this.lblMetrics3Val.Location = new System.Drawing.Point(8, 26);
            this.lblMetrics3Val.Name = "lblMetrics3Val";
            this.lblMetrics3Val.Size = new System.Drawing.Size(25, 30);
            this.lblMetrics3Val.TabIndex = 1;
            this.lblMetrics3Val.Text = "7";
            // 
            // lblMetrics3Title
            // 
            this.lblMetrics3Title.AutoSize = true;
            this.lblMetrics3Title.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblMetrics3Title.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(116)))), ((int)(((byte)(139)))));
            this.lblMetrics3Title.Location = new System.Drawing.Point(8, 8);
            this.lblMetrics3Title.Name = "lblMetrics3Title";
            this.lblMetrics3Title.Size = new System.Drawing.Size(91, 15);
            this.lblMetrics3Title.TabIndex = 0;
            this.lblMetrics3Title.Text = "Chưa xác minh";
            // 
            // cardMetrics4
            // 
            this.cardMetrics4.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
            this.cardMetrics4.Controls.Add(this.lblMetrics4Val);
            this.cardMetrics4.Controls.Add(this.lblMetrics4Title);
            this.cardMetrics4.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cardMetrics4.Location = new System.Drawing.Point(738, 0);
            this.cardMetrics4.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            this.cardMetrics4.Name = "cardMetrics4";
            this.cardMetrics4.Padding = new System.Windows.Forms.Padding(12, 6, 12, 6);
            this.cardMetrics4.Size = new System.Drawing.Size(238, 68);
            this.cardMetrics4.TabIndex = 3;
            // 
            // lblMetrics4Val
            // 
            this.lblMetrics4Val.AutoEllipsis = true;
            this.lblMetrics4Val.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblMetrics4Val.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(22)))), ((int)(((byte)(125)))), ((int)(((byte)(76)))));
            this.lblMetrics4Val.Location = new System.Drawing.Point(8, 29);
            this.lblMetrics4Val.Name = "lblMetrics4Val";
            this.lblMetrics4Val.Size = new System.Drawing.Size(222, 25);
            this.lblMetrics4Val.TabIndex = 1;
            this.lblMetrics4Val.Text = "14 CLEAR • 4 COLLISION";
            // 
            // lblMetrics4Title
            // 
            this.lblMetrics4Title.AutoSize = true;
            this.lblMetrics4Title.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblMetrics4Title.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(116)))), ((int)(((byte)(139)))));
            this.lblMetrics4Title.Location = new System.Drawing.Point(8, 8);
            this.lblMetrics4Title.Name = "lblMetrics4Title";
            this.lblMetrics4Title.Size = new System.Drawing.Size(107, 15);
            this.lblMetrics4Title.TabIndex = 0;
            this.lblMetrics4Title.Text = "Kết quả hình học*";
            // 
            // cardMetrics5
            // 
            this.cardMetrics5.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
            this.cardMetrics5.Controls.Add(this.lblMetrics5Val);
            this.cardMetrics5.Controls.Add(this.lblMetrics5Title);
            this.cardMetrics5.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cardMetrics5.Location = new System.Drawing.Point(984, 0);
            this.cardMetrics5.Margin = new System.Windows.Forms.Padding(0);
            this.cardMetrics5.Name = "cardMetrics5";
            this.cardMetrics5.Padding = new System.Windows.Forms.Padding(12, 6, 12, 6);
            this.cardMetrics5.Size = new System.Drawing.Size(248, 68);
            this.cardMetrics5.TabIndex = 4;
            // 
            // lblMetrics5Val
            // 
            this.lblMetrics5Val.AutoSize = true;
            this.lblMetrics5Val.Font = new System.Drawing.Font("Segoe UI", 16F, System.Drawing.FontStyle.Bold);
            this.lblMetrics5Val.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(160)))), ((int)(((byte)(95)))), ((int)(((byte)(0)))));
            this.lblMetrics5Val.Location = new System.Drawing.Point(8, 26);
            this.lblMetrics5Val.Name = "lblMetrics5Val";
            this.lblMetrics5Val.Size = new System.Drawing.Size(37, 30);
            this.lblMetrics5Val.TabIndex = 1;
            this.lblMetrics5Val.Text = "12";
            // 
            // lblMetrics5Title
            // 
            this.lblMetrics5Title.AutoSize = true;
            this.lblMetrics5Title.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblMetrics5Title.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(116)))), ((int)(((byte)(139)))));
            this.lblMetrics5Title.Location = new System.Drawing.Point(8, 8);
            this.lblMetrics5Title.Name = "lblMetrics5Title";
            this.lblMetrics5Title.Size = new System.Drawing.Size(124, 15);
            this.lblMetrics5Title.TabIndex = 0;
            this.lblMetrics5Title.Text = "Bỏ qua / Ngoại lệ";
            // 
            // splitMain
            // 
            this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitMain.Location = new System.Drawing.Point(0, 224);
            this.splitMain.Margin = new System.Windows.Forms.Padding(0);
            this.splitMain.Name = "splitMain";
            this.splitMain.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitMain.Panel1
            // 
            this.splitMain.Panel1.Controls.Add(this.pnlPartsSection);
            this.splitMain.Panel1MinSize = 140;
            // 
            // splitMain.Panel2
            // 
            this.splitMain.Panel2.Controls.Add(this.splitDetails);
            this.splitMain.Panel2MinSize = 220;
            this.splitMain.Size = new System.Drawing.Size(1264, 551);
            this.splitMain.SplitterDistance = 210;
            this.splitMain.SplitterWidth = 6;
            this.splitMain.TabIndex = 3;
            // 
            // pnlPartsSection
            // 
            this.pnlPartsSection.BackColor = System.Drawing.Color.White;
            this.pnlPartsSection.Controls.Add(this.dgvParts);
            this.pnlPartsSection.Controls.Add(this.pnlPartsHeader);
            this.pnlPartsSection.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlPartsSection.Location = new System.Drawing.Point(0, 0);
            this.pnlPartsSection.Margin = new System.Windows.Forms.Padding(0);
            this.pnlPartsSection.Name = "pnlPartsSection";
            this.pnlPartsSection.Padding = new System.Windows.Forms.Padding(16, 4, 16, 4);
            this.pnlPartsSection.Size = new System.Drawing.Size(1264, 210);
            this.pnlPartsSection.TabIndex = 0;
            // 
            // pnlPartsHeader
            // 
            this.pnlPartsHeader.Controls.Add(this.btnToggleOccurrences);
            this.pnlPartsHeader.Controls.Add(this.lblPartsTitle);
            this.pnlPartsHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlPartsHeader.Location = new System.Drawing.Point(16, 4);
            this.pnlPartsHeader.Name = "pnlPartsHeader";
            this.pnlPartsHeader.Size = new System.Drawing.Size(1232, 32);
            this.pnlPartsHeader.TabIndex = 0;
            // 
            // lblPartsTitle
            // 
            this.lblPartsTitle.AutoSize = true;
            this.lblPartsTitle.Font = new System.Drawing.Font("Segoe UI", 10.5F, System.Drawing.FontStyle.Bold);
            this.lblPartsTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
            this.lblPartsTitle.Location = new System.Drawing.Point(0, 6);
            this.lblPartsTitle.Name = "lblPartsTitle";
            this.lblPartsTitle.Size = new System.Drawing.Size(107, 19);
            this.lblPartsTitle.TabIndex = 0;
            this.lblPartsTitle.Text = "Danh sách Part";
            // 
            // btnToggleOccurrences
            // 
            this.btnToggleOccurrences.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnToggleOccurrences.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.btnToggleOccurrences.Kind = ADDIN.UI.ModernButtonKind.Subtle;
            this.btnToggleOccurrences.Location = new System.Drawing.Point(1070, 2);
            this.btnToggleOccurrences.Name = "btnToggleOccurrences";
            this.btnToggleOccurrences.Size = new System.Drawing.Size(162, 26);
            this.btnToggleOccurrences.TabIndex = 1;
            this.btnToggleOccurrences.Text = "Mở rộng danh sách chi tiết";
            this.btnToggleOccurrences.Tone = ADDIN.UI.ModernButtonTone.Neutral;
            // 
            // dgvParts
            // 
            this.dgvParts.AllowUserToAddRows = false;
            this.dgvParts.AllowUserToDeleteRows = false;
            this.dgvParts.AllowUserToResizeRows = false;
            this.dgvParts.BackgroundColor = System.Drawing.Color.White;
            this.dgvParts.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.dgvParts.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvParts.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colPartName,
            this.colPartConfig,
            this.colPartQty,
            this.colPartBends,
            this.colPartStatus,
            this.colPartReason});
            this.dgvParts.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvParts.Location = new System.Drawing.Point(16, 36);
            this.dgvParts.MultiSelect = false;
            this.dgvParts.Name = "dgvParts";
            this.dgvParts.ReadOnly = true;
            this.dgvParts.RowHeadersVisible = false;
            this.dgvParts.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvParts.Size = new System.Drawing.Size(1232, 170);
            this.dgvParts.TabIndex = 1;
            // 
            // colPartName
            // 
            this.colPartName.HeaderText = "Part";
            this.colPartName.Name = "colPartName";
            this.colPartName.ReadOnly = true;
            this.colPartName.Width = 240;
            // 
            // colPartConfig
            // 
            this.colPartConfig.HeaderText = "Configuration";
            this.colPartConfig.Name = "colPartConfig";
            this.colPartConfig.ReadOnly = true;
            this.colPartConfig.Width = 140;
            // 
            // colPartQty
            // 
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.colPartQty.DefaultCellStyle = dataGridViewCellStyle1;
            this.colPartQty.HeaderText = "SL";
            this.colPartQty.Name = "colPartQty";
            this.colPartQty.ReadOnly = true;
            this.colPartQty.Width = 80;
            // 
            // colPartBends
            // 
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.colPartBends.DefaultCellStyle = dataGridViewCellStyle2;
            this.colPartBends.HeaderText = "Nếp";
            this.colPartBends.Name = "colPartBends";
            this.colPartBends.ReadOnly = true;
            this.colPartBends.Width = 80;
            // 
            // colPartStatus
            // 
            this.colPartStatus.HeaderText = "Trạng thái";
            this.colPartStatus.Name = "colPartStatus";
            this.colPartStatus.ReadOnly = true;
            this.colPartStatus.Width = 150;
            // 
            // colPartReason
            // 
            this.colPartReason.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.colPartReason.HeaderText = "Lý do / Cảnh báo";
            this.colPartReason.Name = "colPartReason";
            this.colPartReason.ReadOnly = true;
            // 
            // splitDetails
            // 
            this.splitDetails.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitDetails.Location = new System.Drawing.Point(0, 0);
            this.splitDetails.Name = "splitDetails";
            // 
            // splitDetails.Panel1
            // 
            this.splitDetails.Panel1.Controls.Add(this.pnlBendsAndTools);
            this.splitDetails.Panel1MinSize = 400;
            // 
            // splitDetails.Panel2
            // 
            this.splitDetails.Panel2.Controls.Add(this.pnlDiagnosticsCard);
            this.splitDetails.Panel2.Controls.Add(this.pnlToolCallout);
            this.splitDetails.Panel2MinSize = 280;
            this.splitDetails.Size = new System.Drawing.Size(1264, 335);
            this.splitDetails.SplitterDistance = 860;
            this.splitDetails.SplitterWidth = 6;
            this.splitDetails.TabIndex = 0;
            // 
            // pnlBendsAndTools
            // 
            this.pnlBendsAndTools.BackColor = System.Drawing.Color.White;
            this.pnlBendsAndTools.Controls.Add(this.dgvTools);
            this.pnlBendsAndTools.Controls.Add(this.pnlDetailHeader);
            this.pnlBendsAndTools.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlBendsAndTools.Location = new System.Drawing.Point(0, 0);
            this.pnlBendsAndTools.Margin = new System.Windows.Forms.Padding(0);
            this.pnlBendsAndTools.Name = "pnlBendsAndTools";
            this.pnlBendsAndTools.Padding = new System.Windows.Forms.Padding(16, 4, 8, 4);
            this.pnlBendsAndTools.Size = new System.Drawing.Size(860, 335);
            this.pnlBendsAndTools.TabIndex = 0;
            // 
            // pnlDetailHeader
            // 
            this.pnlDetailHeader.Controls.Add(this.pnlBendPills);
            this.pnlDetailHeader.Controls.Add(this.lblDetailBendTitle);
            this.pnlDetailHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlDetailHeader.Location = new System.Drawing.Point(16, 4);
            this.pnlDetailHeader.Name = "pnlDetailHeader";
            this.pnlDetailHeader.Padding = new System.Windows.Forms.Padding(0, 2, 0, 4);
            this.pnlDetailHeader.Size = new System.Drawing.Size(836, 62);
            this.pnlDetailHeader.TabIndex = 0;
            // 
            // pnlBendPills
            // 
            this.pnlBendPills.AutoScroll = true;
            this.pnlBendPills.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlBendPills.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            this.pnlBendPills.Location = new System.Drawing.Point(0, 26);
            this.pnlBendPills.Margin = new System.Windows.Forms.Padding(0);
            this.pnlBendPills.Name = "pnlBendPills";
            this.pnlBendPills.Padding = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.pnlBendPills.Size = new System.Drawing.Size(836, 32);
            this.pnlBendPills.TabIndex = 1;
            this.pnlBendPills.WrapContents = false;
            // 
            // lblDetailBendTitle
            // 
            this.lblDetailBendTitle.AutoEllipsis = true;
            this.lblDetailBendTitle.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblDetailBendTitle.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.lblDetailBendTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
            this.lblDetailBendTitle.Location = new System.Drawing.Point(0, 2);
            this.lblDetailBendTitle.Name = "lblDetailBendTitle";
            this.lblDetailBendTitle.Size = new System.Drawing.Size(836, 24);
            this.lblDetailBendTitle.TabIndex = 0;
            this.lblDetailBendTitle.Text = "Chi tiết nếp chấn — Frame-A / Bend 01";
            this.lblDetailBendTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // dgvTools
            // 
            this.dgvTools.AllowUserToAddRows = false;
            this.dgvTools.AllowUserToDeleteRows = false;
            this.dgvTools.AllowUserToResizeRows = false;
            this.dgvTools.BackgroundColor = System.Drawing.Color.White;
            this.dgvTools.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.dgvTools.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvTools.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colToolId,
            this.colPunch,
            this.colHolder,
            this.colMotion,
            this.colGeomStatus,
            this.colMfgStatus,
            this.colClearance,
            this.colToolReason});
            this.dgvTools.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvTools.Location = new System.Drawing.Point(16, 44);
            this.dgvTools.MultiSelect = false;
            this.dgvTools.Name = "dgvTools";
            this.dgvTools.ReadOnly = true;
            this.dgvTools.RowHeadersVisible = false;
            this.dgvTools.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvTools.Size = new System.Drawing.Size(836, 287);
            this.dgvTools.TabIndex = 1;
            // 
            // colToolId
            // 
            this.colToolId.HeaderText = "Bộ dao (18 dao)";
            this.colToolId.Name = "colToolId";
            this.colToolId.ReadOnly = true;
            this.colToolId.Width = 130;
            // 
            // colPunch
            // 
            this.colPunch.HeaderText = "Chày (Punch)";
            this.colPunch.Name = "colPunch";
            this.colPunch.ReadOnly = true;
            this.colPunch.Width = 110;
            // 
            // colHolder
            // 
            this.colHolder.HeaderText = "Cối kẹp trên (Holder)";
            this.colHolder.Name = "colHolder";
            this.colHolder.ReadOnly = true;
            this.colHolder.Width = 135;
            // 
            // colMotion
            // 
            this.colMotion.HeaderText = "Chuyển động";
            this.colMotion.Name = "colMotion";
            this.colMotion.ReadOnly = true;
            this.colMotion.Width = 110;
            // 
            // colGeomStatus
            // 
            this.colGeomStatus.HeaderText = "Hình học";
            this.colGeomStatus.Name = "colGeomStatus";
            this.colGeomStatus.ReadOnly = true;
            this.colGeomStatus.Width = 100;
            // 
            // colMfgStatus
            // 
            this.colMfgStatus.HeaderText = "Xác minh SX";
            this.colMfgStatus.Name = "colMfgStatus";
            this.colMfgStatus.ReadOnly = true;
            this.colMfgStatus.Width = 105;
            // 
            // colClearance
            // 
            this.colClearance.HeaderText = "Clearance";
            this.colClearance.Name = "colClearance";
            this.colClearance.ReadOnly = true;
            this.colClearance.Width = 90;
            // 
            // colToolReason
            // 
            this.colToolReason.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.colToolReason.HeaderText = "Lý do / Bằng chứng";
            this.colToolReason.Name = "colToolReason";
            this.colToolReason.ReadOnly = true;
            // 
            // pnlToolCallout
            // 
            this.pnlToolCallout.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
            this.pnlToolCallout.Controls.Add(this.lblCalloutReason);
            this.pnlToolCallout.Controls.Add(this.lblCalloutHolder);
            this.pnlToolCallout.Controls.Add(this.lblCalloutPunch);
            this.pnlToolCallout.Controls.Add(this.lblCalloutTitle);
            this.pnlToolCallout.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlToolCallout.Location = new System.Drawing.Point(0, 0);
            this.pnlToolCallout.Name = "pnlToolCallout";
            this.pnlToolCallout.Padding = new System.Windows.Forms.Padding(16, 10, 16, 10);
            this.pnlToolCallout.Size = new System.Drawing.Size(398, 155);
            this.pnlToolCallout.TabIndex = 0;
            // 
            // lblCalloutTitle
            // 
            this.lblCalloutTitle.AutoEllipsis = true;
            this.lblCalloutTitle.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblCalloutTitle.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.lblCalloutTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
            this.lblCalloutTitle.Location = new System.Drawing.Point(16, 10);
            this.lblCalloutTitle.Name = "lblCalloutTitle";
            this.lblCalloutTitle.Size = new System.Drawing.Size(366, 22);
            this.lblCalloutTitle.TabIndex = 0;
            this.lblCalloutTitle.Text = "Chi tiết dao đang chọn";
            this.lblCalloutTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblCalloutPunch
            // 
            this.lblCalloutPunch.AutoEllipsis = true;
            this.lblCalloutPunch.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblCalloutPunch.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblCalloutPunch.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.lblCalloutPunch.Location = new System.Drawing.Point(16, 32);
            this.lblCalloutPunch.Name = "lblCalloutPunch";
            this.lblCalloutPunch.Size = new System.Drawing.Size(366, 22);
            this.lblCalloutPunch.TabIndex = 1;
            this.lblCalloutPunch.Text = "Punch – AMD-STD-004 : UNKNOWN";
            this.lblCalloutPunch.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblCalloutHolder
            // 
            this.lblCalloutHolder.AutoEllipsis = true;
            this.lblCalloutHolder.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblCalloutHolder.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblCalloutHolder.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.lblCalloutHolder.Location = new System.Drawing.Point(16, 54);
            this.lblCalloutHolder.Name = "lblCalloutHolder";
            this.lblCalloutHolder.Size = new System.Drawing.Size(366, 22);
            this.lblCalloutHolder.TabIndex = 2;
            this.lblCalloutHolder.Text = "Upper Holder : Chưa xác minh";
            this.lblCalloutHolder.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblCalloutReason
            // 
            this.lblCalloutReason.AutoEllipsis = true;
            this.lblCalloutReason.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblCalloutReason.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblCalloutReason.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(116)))), ((int)(((byte)(139)))));
            this.lblCalloutReason.Location = new System.Drawing.Point(16, 76);
            this.lblCalloutReason.Name = "lblCalloutReason";
            this.lblCalloutReason.Size = new System.Drawing.Size(366, 69);
            this.lblCalloutReason.TabIndex = 3;
            this.lblCalloutReason.Text = "Lý do: chưa đủ bằng chứng mapping và vị trí gá Holder.";
            // 
            // pnlDiagnosticsCard
            // 
            this.pnlDiagnosticsCard.BackColor = System.Drawing.Color.White;
            this.pnlDiagnosticsCard.Controls.Add(this.txtDiagnostics);
            this.pnlDiagnosticsCard.Controls.Add(this.lblDiagHeader);
            this.pnlDiagnosticsCard.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlDiagnosticsCard.Location = new System.Drawing.Point(0, 155);
            this.pnlDiagnosticsCard.Name = "pnlDiagnosticsCard";
            this.pnlDiagnosticsCard.Padding = new System.Windows.Forms.Padding(16, 8, 16, 8);
            this.pnlDiagnosticsCard.Size = new System.Drawing.Size(398, 180);
            this.pnlDiagnosticsCard.TabIndex = 1;
            // 
            // lblDiagHeader
            // 
            this.lblDiagHeader.AutoSize = true;
            this.lblDiagHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblDiagHeader.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.lblDiagHeader.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
            this.lblDiagHeader.Location = new System.Drawing.Point(16, 8);
            this.lblDiagHeader.Name = "lblDiagHeader";
            this.lblDiagHeader.Size = new System.Drawing.Size(149, 17);
            this.lblDiagHeader.TabIndex = 0;
            this.lblDiagHeader.Text = "Thông báo & Chẩn đoán";
            // 
            // txtDiagnostics
            // 
            this.txtDiagnostics.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
            this.txtDiagnostics.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtDiagnostics.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtDiagnostics.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.txtDiagnostics.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.txtDiagnostics.Location = new System.Drawing.Point(16, 25);
            this.txtDiagnostics.Multiline = true;
            this.txtDiagnostics.Name = "txtDiagnostics";
            this.txtDiagnostics.ReadOnly = true;
            this.txtDiagnostics.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtDiagnostics.Size = new System.Drawing.Size(366, 147);
            this.txtDiagnostics.TabIndex = 1;
            // 
            // pnlFooter
            // 
            this.pnlFooter.BackColor = System.Drawing.Color.White;
            this.pnlFooter.Controls.Add(this.lblDisclaimer);
            this.pnlFooter.Controls.Add(this.btnClose);
            this.pnlFooter.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlFooter.Location = new System.Drawing.Point(0, 775);
            this.pnlFooter.Margin = new System.Windows.Forms.Padding(0);
            this.pnlFooter.Name = "pnlFooter";
            this.pnlFooter.Padding = new System.Windows.Forms.Padding(16, 6, 16, 6);
            this.pnlFooter.Size = new System.Drawing.Size(1264, 46);
            this.pnlFooter.TabIndex = 4;
            // 
            // btnClose
            // 
            this.btnClose.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnClose.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnClose.Kind = ADDIN.UI.ModernButtonKind.Standard;
            this.btnClose.Location = new System.Drawing.Point(1168, 6);
            this.btnClose.Margin = new System.Windows.Forms.Padding(8, 0, 0, 0);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(80, 34);
            this.btnClose.TabIndex = 1;
            this.btnClose.Text = "Đóng";
            this.btnClose.Tone = ADDIN.UI.ModernButtonTone.Neutral;
            // 
            // lblDisclaimer
            // 
            this.lblDisclaimer.AutoEllipsis = true;
            this.lblDisclaimer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblDisclaimer.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.lblDisclaimer.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(116)))), ((int)(((byte)(139)))));
            this.lblDisclaimer.Location = new System.Drawing.Point(16, 6);
            this.lblDisclaimer.Margin = new System.Windows.Forms.Padding(0);
            this.lblDisclaimer.Name = "lblDisclaimer";
            this.lblDisclaimer.Size = new System.Drawing.Size(1152, 34);
            this.lblDisclaimer.TabIndex = 0;
            this.lblDisclaimer.Text = "Chỉ là minh họa bố cục với dữ liệu giả lập. *CLEAR/COLLISION biểu thị kết quả hìn" +
    "h học trong phạm vi chứng minh, không phải kết luận toàn Part chấn được hoặc khô" +
    "ng.";
            this.lblDisclaimer.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // BendAssemblyBatchForm
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(243)))), ((int)(((byte)(245)))), ((int)(((byte)(248)))));
            this.ClientSize = new System.Drawing.Size(1264, 821);
            this.Controls.Add(this.pnlRoot);
            this.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.MinimumSize = new System.Drawing.Size(1024, 680);
            this.Name = "BendAssemblyBatchForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "CHECK CHẤN — Assembly Batch Inspection";
            this.pnlRoot.ResumeLayout(false);
            this.pnlHeader.ResumeLayout(false);
            this.pnlHeader.PerformLayout();
            this.pnlHeaderLeft.ResumeLayout(false);
            this.pnlHeaderLeft.PerformLayout();
            this.pnlHeaderRight.ResumeLayout(false);
            this.pnlHeaderRight.PerformLayout();
            this.pnlControlBar.ResumeLayout(false);
            this.pnlControlBar.PerformLayout();
            this.pnlControlLeft.ResumeLayout(false);
            this.pnlControlLeft.PerformLayout();
            this.pnlControlRight.ResumeLayout(false);
            this.pnlControlRight.PerformLayout();
            this.pnlScope.ResumeLayout(false);
            this.pnlScope.PerformLayout();
            this.pnlOverview.ResumeLayout(false);
            this.pnlOverview.PerformLayout();
            this.tblMetrics.ResumeLayout(false);
            this.cardMetrics1.ResumeLayout(false);
            this.cardMetrics1.PerformLayout();
            this.cardMetrics2.ResumeLayout(false);
            this.cardMetrics2.PerformLayout();
            this.cardMetrics3.ResumeLayout(false);
            this.cardMetrics3.PerformLayout();
            this.cardMetrics4.ResumeLayout(false);
            this.cardMetrics4.PerformLayout();
            this.cardMetrics5.ResumeLayout(false);
            this.cardMetrics5.PerformLayout();
            this.splitMain.Panel1.ResumeLayout(false);
            this.splitMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).EndInit();
            this.splitMain.ResumeLayout(false);
            this.pnlPartsSection.ResumeLayout(false);
            this.pnlPartsHeader.ResumeLayout(false);
            this.pnlPartsHeader.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvParts)).EndInit();
            this.splitDetails.Panel1.ResumeLayout(false);
            this.splitDetails.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitDetails)).EndInit();
            this.splitDetails.ResumeLayout(false);
            this.pnlBendsAndTools.ResumeLayout(false);
            this.pnlDetailHeader.ResumeLayout(false);
            this.pnlDetailHeader.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvTools)).EndInit();
            this.pnlToolCallout.ResumeLayout(false);
            this.pnlToolCallout.PerformLayout();
            this.pnlDiagnosticsCard.ResumeLayout(false);
            this.pnlDiagnosticsCard.PerformLayout();
            this.pnlFooter.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel pnlRoot;
        private System.Windows.Forms.Panel pnlHeader;
        private System.Windows.Forms.TableLayoutPanel pnlHeaderLeft;
        private System.Windows.Forms.FlowLayoutPanel pnlHeaderRight;
        private System.Windows.Forms.Label lblHeaderTitle;
        private System.Windows.Forms.Label lblHeaderSub;
        private System.Windows.Forms.Label badgeConcept;
        private ModernButton btnToggleMockup;
        private System.Windows.Forms.Panel pnlControlBar;
        private System.Windows.Forms.FlowLayoutPanel pnlControlLeft;
        private System.Windows.Forms.FlowLayoutPanel pnlControlRight;
        private System.Windows.Forms.FlowLayoutPanel pnlScope;
        private ModernButton btnScopeEntire;
        private ModernButton btnScopeSelected;
        private ModernButton btnRunBatch;
        private ModernButton btnCancel;
        private ModernButton btnRefresh;
        private System.Windows.Forms.ComboBox cboStatusFilter;
        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.Panel pnlOverview;
        private System.Windows.Forms.Label lblProgressTitle;
        private System.Windows.Forms.Label lblProgressPercent;
        private System.Windows.Forms.Panel pnlProgressBar;
        private System.Windows.Forms.TableLayoutPanel tblMetrics;
        private System.Windows.Forms.Panel cardMetrics1;
        private System.Windows.Forms.Label lblMetrics1Val;
        private System.Windows.Forms.Label lblMetrics1Title;
        private System.Windows.Forms.Panel cardMetrics2;
        private System.Windows.Forms.Label lblMetrics2Val;
        private System.Windows.Forms.Label lblMetrics2Title;
        private System.Windows.Forms.Panel cardMetrics3;
        private System.Windows.Forms.Label lblMetrics3Val;
        private System.Windows.Forms.Label lblMetrics3Title;
        private System.Windows.Forms.Panel cardMetrics4;
        private System.Windows.Forms.Label lblMetrics4Val;
        private System.Windows.Forms.Label lblMetrics4Title;
        private System.Windows.Forms.Panel cardMetrics5;
        private System.Windows.Forms.Label lblMetrics5Val;
        private System.Windows.Forms.Label lblMetrics5Title;
        private System.Windows.Forms.SplitContainer splitMain;
        private System.Windows.Forms.Panel pnlPartsSection;
        private System.Windows.Forms.Panel pnlPartsHeader;
        private System.Windows.Forms.Label lblPartsTitle;
        private ModernButton btnToggleOccurrences;
        private System.Windows.Forms.DataGridView dgvParts;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPartName;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPartConfig;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPartQty;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPartBends;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPartStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPartReason;
        private System.Windows.Forms.SplitContainer splitDetails;
        private System.Windows.Forms.Panel pnlBendsAndTools;
        private System.Windows.Forms.Panel pnlDetailHeader;
        private System.Windows.Forms.Label lblDetailBendTitle;
        private System.Windows.Forms.FlowLayoutPanel pnlBendPills;
        private System.Windows.Forms.DataGridView dgvTools;
        private System.Windows.Forms.DataGridViewTextBoxColumn colToolId;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPunch;
        private System.Windows.Forms.DataGridViewTextBoxColumn colHolder;
        private System.Windows.Forms.DataGridViewTextBoxColumn colMotion;
        private System.Windows.Forms.DataGridViewTextBoxColumn colGeomStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn colMfgStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn colClearance;
        private System.Windows.Forms.DataGridViewTextBoxColumn colToolReason;
        private System.Windows.Forms.Panel pnlToolCallout;
        private System.Windows.Forms.Label lblCalloutTitle;
        private System.Windows.Forms.Label lblCalloutPunch;
        private System.Windows.Forms.Label lblCalloutHolder;
        private System.Windows.Forms.Label lblCalloutReason;
        private System.Windows.Forms.Panel pnlDiagnosticsCard;
        private System.Windows.Forms.Label lblDiagHeader;
        private System.Windows.Forms.TextBox txtDiagnostics;
        private System.Windows.Forms.Panel pnlFooter;
        private System.Windows.Forms.Label lblDisclaimer;
        private ModernButton btnClose;
        private System.Windows.Forms.ToolTip toolTips;
    }
}
