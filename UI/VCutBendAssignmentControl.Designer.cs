namespace ADDIN.UI
{
    partial class VCutBendAssignmentControl
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TableLayoutPanel layoutRoot;
        private System.Windows.Forms.Panel panelHeader;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblDocument;
        private System.Windows.Forms.Label lblProperties;
        private ADDIN.UI.ModernButton btnRefresh;
        private System.Windows.Forms.DataGridView gridBends;
        private System.Windows.Forms.DataGridViewTextBoxColumn colNo;
        private System.Windows.Forms.DataGridViewTextBoxColumn colFeature;
        private System.Windows.Forms.DataGridViewTextBoxColumn colStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn colType;
        private System.Windows.Forms.DataGridViewTextBoxColumn colValue;
        private System.Windows.Forms.DataGridViewComboBoxColumn colKind;
        private System.Windows.Forms.Panel panelFooter;
        private System.Windows.Forms.Label lblDetail;
        private System.Windows.Forms.Label lblStatus;
        private ADDIN.UI.ModernButton btnSave;
        private ADDIN.UI.ModernButton btnClose;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.layoutRoot = new System.Windows.Forms.TableLayoutPanel();
            this.panelHeader = new System.Windows.Forms.Panel();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblDocument = new System.Windows.Forms.Label();
            this.lblProperties = new System.Windows.Forms.Label();
            this.btnRefresh = new ADDIN.UI.ModernButton();
            this.gridBends = new System.Windows.Forms.DataGridView();
            this.colNo = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colFeature = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colType = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colValue = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colKind = new System.Windows.Forms.DataGridViewComboBoxColumn();
            this.panelFooter = new System.Windows.Forms.Panel();
            this.lblDetail = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.btnSave = new ADDIN.UI.ModernButton();
            this.btnClose = new ADDIN.UI.ModernButton();
            this.layoutRoot.SuspendLayout();
            this.panelHeader.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridBends)).BeginInit();
            this.panelFooter.SuspendLayout();
            this.SuspendLayout();
            // 
            // layoutRoot
            // 
            this.layoutRoot.ColumnCount = 1;
            this.layoutRoot.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layoutRoot.Controls.Add(this.panelHeader, 0, 0);
            this.layoutRoot.Controls.Add(this.gridBends, 0, 1);
            this.layoutRoot.Controls.Add(this.panelFooter, 0, 2);
            this.layoutRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layoutRoot.Location = new System.Drawing.Point(0, 0);
            this.layoutRoot.Name = "layoutRoot";
            this.layoutRoot.RowCount = 3;
            this.layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 114F));
            this.layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 156F));
            this.layoutRoot.Size = new System.Drawing.Size(378, 495);
            this.layoutRoot.TabIndex = 0;
            // 
            // panelHeader
            // 
            this.panelHeader.BackColor = System.Drawing.Color.FromArgb(246, 250, 254);
            this.panelHeader.Controls.Add(this.lblProperties);
            this.panelHeader.Controls.Add(this.lblDocument);
            this.panelHeader.Controls.Add(this.lblTitle);
            this.panelHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelHeader.Location = new System.Drawing.Point(3, 3);
            this.panelHeader.Name = "panelHeader";
            this.panelHeader.Size = new System.Drawing.Size(372, 112);
            this.panelHeader.TabIndex = 0;
            // 
            // btnRefresh
            // 
            this.btnRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnRefresh.BackColor = System.Drawing.Color.Transparent;
            this.btnRefresh.BorderColor = System.Drawing.Color.FromArgb(203, 213, 225);
            this.btnRefresh.BorderRadius = 4;
            this.btnRefresh.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRefresh.FlatAppearance.BorderSize = 0;
            this.btnRefresh.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRefresh.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold);
            this.btnRefresh.ForeColor = System.Drawing.Color.Black;
            this.btnRefresh.Location = new System.Drawing.Point(34, 116);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.NormalColor = System.Drawing.Color.White;
            this.btnRefresh.HoverColor = System.Drawing.Color.FromArgb(241, 245, 249);
            this.btnRefresh.PressColor = System.Drawing.Color.FromArgb(226, 232, 240);
            this.btnRefresh.Size = new System.Drawing.Size(116, 30);
            this.btnRefresh.TabIndex = 1;
            this.btnRefresh.Text = "Refresh / 更新";
            this.btnRefresh.UseVisualStyleBackColor = false;
            // 
            // lblTitle
            // 
            this.lblTitle.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblTitle.BackColor = System.Drawing.Color.FromArgb(235, 242, 250);
            this.lblTitle.Font = new System.Drawing.Font("Meiryo UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.lblTitle.ForeColor = System.Drawing.Color.Black;
            this.lblTitle.Location = new System.Drawing.Point(0, 0);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Padding = new System.Windows.Forms.Padding(8, 6, 84, 0);
            this.lblTitle.Size = new System.Drawing.Size(372, 32);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "BEND LINE / 曲げ線設定";
            // 
            // lblDocument
            // 
            this.lblDocument.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblDocument.BackColor = System.Drawing.Color.White;
            this.lblDocument.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular);
            this.lblDocument.ForeColor = System.Drawing.Color.Black;
            this.lblDocument.Location = new System.Drawing.Point(0, 32);
            this.lblDocument.Name = "lblDocument";
            this.lblDocument.Padding = new System.Windows.Forms.Padding(8, 5, 0, 0);
            this.lblDocument.Size = new System.Drawing.Size(372, 26);
            this.lblDocument.TabIndex = 1;
            this.lblDocument.Text = "Part / 部品: —    Config / 設定: —";
            // 
            // lblProperties
            // 
            this.lblProperties.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblProperties.BackColor = System.Drawing.Color.FromArgb(246, 250, 254);
            this.lblProperties.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular);
            this.lblProperties.ForeColor = System.Drawing.Color.Black;
            this.lblProperties.Location = new System.Drawing.Point(0, 58);
            this.lblProperties.Name = "lblProperties";
            this.lblProperties.Padding = new System.Windows.Forms.Padding(8, 6, 0, 0);
            this.lblProperties.Size = new System.Drawing.Size(372, 50);
            this.lblProperties.TabIndex = 2;
            this.lblProperties.Text = "○ V溝1  — mm    ◎ V溝2  — mm\r\n● C溝    — mm";
            // 
            // gridBends
            // 
            this.gridBends.AllowUserToAddRows = false;
            this.gridBends.AllowUserToDeleteRows = false;
            this.gridBends.AllowUserToOrderColumns = false;
            this.gridBends.AllowUserToResizeRows = false;
            this.gridBends.AlternatingRowsDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(234)))), ((int)(((byte)(242)))), ((int)(((byte)(250)))));
            this.gridBends.AlternatingRowsDefaultCellStyle.Font = new System.Drawing.Font("Meiryo UI", 8.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.gridBends.AlternatingRowsDefaultCellStyle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.gridBends.AlternatingRowsDefaultCellStyle.Padding = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.gridBends.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.gridBends.BackgroundColor = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(245)))), ((int)(((byte)(248)))));
            this.gridBends.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.gridBends.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.SingleHorizontal;
            this.gridBends.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Single;
            this.gridBends.ColumnHeadersDefaultCellStyle.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.gridBends.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(241)))), ((int)(((byte)(245)))), ((int)(((byte)(249)))));
            this.gridBends.ColumnHeadersDefaultCellStyle.Font = new System.Drawing.Font("Meiryo UI", 8.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.gridBends.ColumnHeadersDefaultCellStyle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(58)))), ((int)(((byte)(138)))));
            this.gridBends.ColumnHeadersDefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(244)))), ((int)(((byte)(248)))));
            this.gridBends.ColumnHeadersDefaultCellStyle.SelectionForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(115)))), ((int)(((byte)(230)))));
            this.gridBends.ColumnHeadersDefaultCellStyle.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.gridBends.ColumnHeadersHeight = 38;
            this.gridBends.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.gridBends.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colNo,
            this.colFeature,
            this.colStatus,
            this.colType,
            this.colValue,
            this.colKind});
            this.gridBends.DefaultCellStyle.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            this.gridBends.DefaultCellStyle.BackColor = System.Drawing.Color.White;
            this.gridBends.DefaultCellStyle.Font = new System.Drawing.Font("Meiryo UI", 8.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.gridBends.DefaultCellStyle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.gridBends.DefaultCellStyle.Padding = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.gridBends.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(224)))), ((int)(((byte)(242)))), ((int)(((byte)(254)))));
            this.gridBends.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(3)))), ((int)(((byte)(105)))), ((int)(((byte)(161)))));
            this.gridBends.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridBends.EnableHeadersVisualStyles = false;
            this.gridBends.GridColor = System.Drawing.Color.FromArgb(((int)(((byte)(215)))), ((int)(((byte)(222)))), ((int)(((byte)(230)))));
            this.gridBends.Location = new System.Drawing.Point(3, 121);
            this.gridBends.MultiSelect = false;
            this.gridBends.Name = "gridBends";
            this.gridBends.RowHeadersVisible = false;
            this.gridBends.RowTemplate.Height = 30;
            this.gridBends.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.gridBends.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.gridBends.Size = new System.Drawing.Size(372, 218);
            this.gridBends.TabIndex = 1;
            // 
            // colNo
            // 
            this.colNo.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colNo.DefaultCellStyle.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.colNo.HeaderText = "No";
            this.colNo.Name = "colNo";
            this.colNo.ReadOnly = true;
            this.colNo.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.colNo.Width = 22;
            // 
            // colFeature
            // 
            this.colFeature.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.colFeature.FillWeight = 28F;
            this.colFeature.HeaderText = "Bend\n曲げ";
            this.colFeature.MinimumWidth = 45;
            this.colFeature.Name = "colFeature";
            this.colFeature.ReadOnly = true;
            // 
            // colStatus
            // 
            this.colStatus.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colStatus.DefaultCellStyle.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.colStatus.DefaultCellStyle.Font = new System.Drawing.Font("Meiryo UI", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.colStatus.HeaderText = "✔";
            this.colStatus.Name = "colStatus";
            this.colStatus.ReadOnly = true;
            this.colStatus.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.colStatus.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.colStatus.Width = 28;
            // 
            // colType
            // 
            this.colType.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.colType.FillWeight = 32F;
            this.colType.HeaderText = "Type\n種類";
            this.colType.MinimumWidth = 50;
            this.colType.Name = "colType";
            this.colType.ReadOnly = true;
            // 
            // colValue
            // 
            this.colValue.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colValue.DefaultCellStyle.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.colValue.HeaderText = "V\ncut";
            this.colValue.Name = "colValue";
            this.colValue.ReadOnly = true;
            this.colValue.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.colValue.Width = 34;
            // 
            // colKind
            // 
            this.colKind.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.colKind.FillWeight = 40F;
            this.colKind.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.colKind.HeaderText = "Mark\n加工印";
            this.colKind.Items.AddRange(new object[] {
            "未設定",
            "V溝1 ○",
            "V溝2 ◎",
            "C溝 ●"});
            this.colKind.MinimumWidth = 65;
            this.colKind.Name = "colKind";
            // 
            // panelFooter
            // 
            this.panelFooter.BackColor = System.Drawing.Color.FromArgb(246, 250, 254);
            this.panelFooter.Controls.Add(this.lblDetail);
            this.panelFooter.Controls.Add(this.lblStatus);
            this.panelFooter.Controls.Add(this.btnRefresh);
            this.panelFooter.Controls.Add(this.btnSave);
            this.panelFooter.Controls.Add(this.btnClose);
            this.panelFooter.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelFooter.Location = new System.Drawing.Point(3, 342);
            this.panelFooter.Name = "panelFooter";
            this.panelFooter.Size = new System.Drawing.Size(372, 150);
            this.panelFooter.TabIndex = 2;
            // 
            // lblDetail
            // 
            this.lblDetail.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblDetail.AutoEllipsis = true;
            this.lblDetail.BackColor = System.Drawing.Color.FromArgb(242, 247, 253);
            this.lblDetail.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.lblDetail.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular);
            this.lblDetail.ForeColor = System.Drawing.Color.Black;
            this.lblDetail.Location = new System.Drawing.Point(6, 4);
            this.lblDetail.Name = "lblDetail";
            this.lblDetail.Padding = new System.Windows.Forms.Padding(8, 6, 8, 6);
            this.lblDetail.Size = new System.Drawing.Size(360, 80);
            this.lblDetail.TabIndex = 0;
            // 
            // lblStatus
            // 
            this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.Font = new System.Drawing.Font("Meiryo UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.lblStatus.ForeColor = System.Drawing.Color.Black;
            this.lblStatus.Location = new System.Drawing.Point(6, 88);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(360, 24);
            this.lblStatus.TabIndex = 1;
            this.lblStatus.Text = "Select a bend / 曲げを選択";
            // 
            // btnSave
            // 
            this.btnSave.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSave.BackColor = System.Drawing.Color.Transparent;
            this.btnSave.BorderColor = System.Drawing.Color.Transparent;
            this.btnSave.BorderRadius = 4;
            this.btnSave.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnSave.FlatAppearance.BorderSize = 0;
            this.btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSave.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnSave.ForeColor = System.Drawing.Color.White;
            this.btnSave.Location = new System.Drawing.Point(156, 116);
            this.btnSave.Name = "btnSave";
            this.btnSave.NormalColor = System.Drawing.Color.FromArgb(0, 103, 198);
            this.btnSave.HoverColor = System.Drawing.Color.FromArgb(14, 116, 214);
            this.btnSave.PressColor = System.Drawing.Color.FromArgb(0, 85, 165);
            this.btnSave.Size = new System.Drawing.Size(106, 30);
            this.btnSave.TabIndex = 2;
            this.btnSave.Text = "Save / 保存";
            this.btnSave.UseVisualStyleBackColor = false;
            this.btnSave.Visible = true;
            this.btnSave.Enabled = false;
            // 
            // btnClose
            // 
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.BackColor = System.Drawing.Color.Transparent;
            this.btnClose.BorderColor = System.Drawing.Color.FromArgb(203, 213, 225);
            this.btnClose.BorderRadius = 4;
            this.btnClose.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnClose.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnClose.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular);
            this.btnClose.ForeColor = System.Drawing.Color.Black;
            this.btnClose.Location = new System.Drawing.Point(268, 116);
            this.btnClose.Name = "btnClose";
            this.btnClose.NormalColor = System.Drawing.Color.White;
            this.btnClose.HoverColor = System.Drawing.Color.FromArgb(241, 245, 249);
            this.btnClose.PressColor = System.Drawing.Color.FromArgb(226, 232, 240);
            this.btnClose.Size = new System.Drawing.Size(98, 30);
            this.btnClose.TabIndex = 3;
            this.btnClose.Text = "Close / 閉じる";
            this.btnClose.UseVisualStyleBackColor = false;
            // 
            // VCutBendAssignmentControl
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = System.Drawing.Color.FromArgb(246, 250, 254);
            this.Controls.Add(this.layoutRoot);
            this.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.Name = "VCutBendAssignmentControl";
            this.Size = new System.Drawing.Size(378, 495);
            this.layoutRoot.ResumeLayout(false);
            this.panelHeader.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridBends)).EndInit();
            this.panelFooter.ResumeLayout(false);
            this.ResumeLayout(false);
        }
    }
}
