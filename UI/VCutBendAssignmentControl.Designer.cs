namespace ADDIN.UI
{
    partial class VCutBendAssignmentControl
    {
        private System.ComponentModel.IContainer components;
        private System.Windows.Forms.TableLayoutPanel layoutRoot, layoutHeader, layoutProperties, layoutTools, layoutActions, layoutDetails;
        private System.Windows.Forms.Panel panelHeader, panelFooter;
        private System.Windows.Forms.Label lblTitle, lblDocument, lblMaterial, lblProperties, lblPropertyV1, lblPropertyV2, lblPropertyC, lblSummary, lblStatus, lblEmpty;
        private System.Windows.Forms.TextBox txtSearch, lblDetail;
        private System.Windows.Forms.CheckBox chkIssuesOnly;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.DataGridView gridBends;
        private System.Windows.Forms.DataGridViewTextBoxColumn colNo, colFeature, colStatus, colType, colValue;
        private System.Windows.Forms.DataGridViewComboBoxColumn colKind;
        private ADDIN.UI.ModernButton btnRefresh, btnSave, btnClose;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            toolTip = new System.Windows.Forms.ToolTip(components);
            layoutRoot = new System.Windows.Forms.TableLayoutPanel();
            layoutHeader = new System.Windows.Forms.TableLayoutPanel();
            layoutProperties = new System.Windows.Forms.TableLayoutPanel();
            layoutTools = new System.Windows.Forms.TableLayoutPanel();
            layoutActions = new System.Windows.Forms.TableLayoutPanel();
            layoutDetails = new System.Windows.Forms.TableLayoutPanel();
            panelHeader = new System.Windows.Forms.Panel();
            panelFooter = new System.Windows.Forms.Panel();
            lblTitle = new System.Windows.Forms.Label();
            lblDocument = new System.Windows.Forms.Label();
            lblMaterial = new System.Windows.Forms.Label();
            lblProperties = new System.Windows.Forms.Label();
            lblPropertyV1 = new System.Windows.Forms.Label();
            lblPropertyV2 = new System.Windows.Forms.Label();
            lblPropertyC = new System.Windows.Forms.Label();
            lblSummary = new System.Windows.Forms.Label();
            lblStatus = new System.Windows.Forms.Label();
            lblEmpty = new System.Windows.Forms.Label();
            txtSearch = new System.Windows.Forms.TextBox();
            lblDetail = new System.Windows.Forms.TextBox();
            chkIssuesOnly = new System.Windows.Forms.CheckBox();
            gridBends = new System.Windows.Forms.DataGridView();
            colNo = new System.Windows.Forms.DataGridViewTextBoxColumn();
            colFeature = new System.Windows.Forms.DataGridViewTextBoxColumn();
            colStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            colType = new System.Windows.Forms.DataGridViewTextBoxColumn();
            colValue = new System.Windows.Forms.DataGridViewTextBoxColumn();
            colKind = new System.Windows.Forms.DataGridViewComboBoxColumn();
            btnRefresh = new ADDIN.UI.ModernButton();
            btnSave = new ADDIN.UI.ModernButton();
            btnClose = new ADDIN.UI.ModernButton();
            layoutRoot.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)gridBends).BeginInit();
            SuspendLayout();
            Name = "VCutBendAssignmentControl";
            BackColor = System.Drawing.Color.FromArgb(245, 248, 252);
            Font = new System.Drawing.Font("Meiryo UI", 8.5F);
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            Size = new System.Drawing.Size(420, 640);
            Padding = new System.Windows.Forms.Padding(6);

            layoutRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutRoot.Margin = new System.Windows.Forms.Padding(0);
            layoutRoot.ColumnCount = 1;
            layoutRoot.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutRoot.RowCount = 5;
            layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 176F));
            layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 154F));
            layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            layoutRoot.Controls.Add(panelHeader, 0, 0);
            layoutRoot.Controls.Add(layoutTools, 0, 1);
            layoutRoot.Controls.Add(gridBends, 0, 2);
            layoutRoot.Controls.Add(panelFooter, 0, 3);
            layoutRoot.Controls.Add(layoutActions, 0, 4);
            Controls.Add(layoutRoot);

            panelHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            panelHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            panelHeader.BackColor = System.Drawing.Color.White;
            panelHeader.Controls.Add(layoutHeader);
            layoutHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutHeader.Margin = new System.Windows.Forms.Padding(0);
            layoutHeader.ColumnCount = 1;
            layoutHeader.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutHeader.RowCount = 6;
            foreach (float height in new[] { 27F, 36F, 22F, 17F, 46F, 24F })
                layoutHeader.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, height));
            layoutHeader.Controls.Add(lblTitle, 0, 0);
            layoutHeader.Controls.Add(lblDocument, 0, 1);
            layoutHeader.Controls.Add(lblMaterial, 0, 2);
            layoutHeader.Controls.Add(lblProperties, 0, 3);
            layoutHeader.Controls.Add(layoutProperties, 0, 4);
            layoutHeader.Controls.Add(lblSummary, 0, 5);
            lblTitle.Text = "V CUT / 溝設定";
            lblTitle.BackColor = System.Drawing.Color.FromArgb(230, 239, 252);
            lblTitle.Font = new System.Drawing.Font("Meiryo UI", 10F, System.Drawing.FontStyle.Bold);
            lblTitle.ForeColor = System.Drawing.Color.FromArgb(30, 58, 110);
            lblTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            lblTitle.Margin = new System.Windows.Forms.Padding(0);
            lblTitle.Padding = new System.Windows.Forms.Padding(8, 4, 4, 0);
            lblDocument.Dock = System.Windows.Forms.DockStyle.Fill;
            lblDocument.Margin = new System.Windows.Forms.Padding(0);
            lblDocument.Padding = new System.Windows.Forms.Padding(8, 3, 8, 0);
            lblDocument.AutoEllipsis = true;
            lblMaterial.Dock = System.Windows.Forms.DockStyle.Fill;
            lblMaterial.Margin = new System.Windows.Forms.Padding(0);
            lblMaterial.Padding = new System.Windows.Forms.Padding(8, 2, 8, 0);
            lblMaterial.AutoEllipsis = true;
            lblProperties.Dock = System.Windows.Forms.DockStyle.Fill;
            lblProperties.Margin = new System.Windows.Forms.Padding(0);
            lblProperties.Padding = new System.Windows.Forms.Padding(8, 0, 4, 0);
            lblProperties.Font = new System.Drawing.Font("Meiryo UI", 7.5F);
            lblProperties.ForeColor = System.Drawing.Color.FromArgb(90, 105, 125);
            lblProperties.Text = "Property: saved → next / 保存値 → 予定値";
            layoutProperties.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutProperties.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            layoutProperties.ColumnCount = 3;
            for (int i = 0; i < 3; i++) layoutProperties.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.333F));
            layoutProperties.Controls.Add(lblPropertyV1, 0, 0);
            layoutProperties.Controls.Add(lblPropertyV2, 1, 0);
            layoutProperties.Controls.Add(lblPropertyC, 2, 0);
            foreach (var card in new[] { lblPropertyV1, lblPropertyV2, lblPropertyC })
            {
                card.Dock = System.Windows.Forms.DockStyle.Fill;
                card.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
                card.Padding = new System.Windows.Forms.Padding(5, 4, 2, 0);
                card.BackColor = System.Drawing.Color.FromArgb(246, 249, 253);
                card.AutoEllipsis = true;
            }
            lblSummary.Dock = System.Windows.Forms.DockStyle.Fill;
            lblSummary.Margin = new System.Windows.Forms.Padding(0);
            lblSummary.Padding = new System.Windows.Forms.Padding(8, 5, 8, 0);
            lblSummary.Font = new System.Drawing.Font("Meiryo UI", 8F, System.Drawing.FontStyle.Bold);

            layoutTools.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTools.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            layoutTools.ColumnCount = 3;
            layoutTools.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 66F));
            layoutTools.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTools.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 132F));
            var searchLabel = new System.Windows.Forms.Label();
            searchLabel.Text = "Find / 検索";
            searchLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            searchLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            searchLabel.Margin = new System.Windows.Forms.Padding(0);
            layoutTools.Controls.Add(searchLabel, 0, 0);
            txtSearch.Name = "txtSearch";
            txtSearch.Dock = System.Windows.Forms.DockStyle.Fill;
            txtSearch.Margin = new System.Windows.Forms.Padding(0, 3, 3, 0);
            layoutTools.Controls.Add(txtSearch, 1, 0);
            chkIssuesOnly.Name = "chkIssuesOnly";
            chkIssuesOnly.Text = "NG only / NGのみ";
            chkIssuesOnly.Dock = System.Windows.Forms.DockStyle.Fill;
            chkIssuesOnly.Margin = new System.Windows.Forms.Padding(4, 0, 0, 0);
            layoutTools.Controls.Add(chkIssuesOnly, 2, 0);

            gridBends.Name = "gridBends";
            gridBends.Dock = System.Windows.Forms.DockStyle.Fill;
            gridBends.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            gridBends.AllowUserToAddRows = false;
            gridBends.AllowUserToDeleteRows = false;
            gridBends.AllowUserToOrderColumns = false;
            gridBends.AllowUserToResizeRows = false;
            gridBends.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            gridBends.BackgroundColor = System.Drawing.Color.FromArgb(245, 248, 252);
            gridBends.BorderStyle = System.Windows.Forms.BorderStyle.None;
            gridBends.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.SingleHorizontal;
            gridBends.EnableHeadersVisualStyles = false;
            gridBends.ColumnHeadersHeight = 42;
            gridBends.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            gridBends.RowHeadersVisible = false;
            gridBends.MultiSelect = false;
            gridBends.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            gridBends.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnEnter;
            gridBends.RowTemplate.Height = 32;
            gridBends.Columns.AddRange(colNo, colFeature, colStatus, colType, colValue, colKind);
            colNo.Name = "colNo"; colNo.HeaderText = "#"; colNo.Width = 26;
            colNo.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            colFeature.Name = "colFeature"; colFeature.HeaderText = "Bend\n曲げ"; colFeature.MinimumWidth = 82; colFeature.FillWeight = 100;
            colStatus.Name = "colStatus"; colStatus.HeaderText = "State\n状態"; colStatus.Width = 50;
            colStatus.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            colType.Name = "colType"; colType.HeaderText = "Type\n種類"; colType.MinimumWidth = 85; colType.FillWeight = 60;
            colValue.Name = "colValue"; colValue.HeaderText = "V cut\n溝 mm"; colValue.Width = 54;
            colValue.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            colKind.Name = "colKind"; colKind.HeaderText = "Mark\n加工印"; colKind.MinimumWidth = 88; colKind.Width = 106;
            colKind.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            colKind.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            colKind.Items.AddRange("未設定", "V溝1 ○", "V溝2 ◎", "C溝 ●");
            foreach (System.Windows.Forms.DataGridViewColumn column in gridBends.Columns)
            {
                column.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
                column.ReadOnly = column != colKind;
            }
            lblEmpty.Dock = System.Windows.Forms.DockStyle.Fill;
            lblEmpty.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            lblEmpty.ForeColor = System.Drawing.Color.FromArgb(90, 105, 125);
            lblEmpty.BackColor = gridBends.BackgroundColor;
            lblEmpty.Visible = false;
            gridBends.Controls.Add(lblEmpty);

            panelFooter.Dock = System.Windows.Forms.DockStyle.Fill;
            panelFooter.Margin = new System.Windows.Forms.Padding(0);
            panelFooter.Controls.Add(layoutDetails);
            layoutDetails.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutDetails.Margin = new System.Windows.Forms.Padding(0);
            layoutDetails.ColumnCount = 1;
            layoutDetails.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutDetails.RowCount = 2;
            layoutDetails.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutDetails.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 44F));
            layoutDetails.Controls.Add(lblDetail, 0, 0);
            layoutDetails.Controls.Add(lblStatus, 0, 1);
            lblDetail.Name = "lblDetail";
            lblDetail.Dock = System.Windows.Forms.DockStyle.Fill;
            lblDetail.Margin = new System.Windows.Forms.Padding(0, 0, 0, 3);
            lblDetail.Multiline = true;
            lblDetail.ReadOnly = true;
            lblDetail.WordWrap = true;
            lblDetail.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            lblDetail.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            lblDetail.BackColor = System.Drawing.Color.White;
            lblDetail.TabStop = false;
            lblStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            lblStatus.Margin = new System.Windows.Forms.Padding(0);
            lblStatus.Padding = new System.Windows.Forms.Padding(6, 3, 6, 0);
            lblStatus.Font = new System.Drawing.Font("Meiryo UI", 8.5F, System.Drawing.FontStyle.Bold);
            lblStatus.AutoEllipsis = true;

            layoutActions.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutActions.Margin = new System.Windows.Forms.Padding(0, 4, 0, 0);
            layoutActions.ColumnCount = 3;
            for (int i = 0; i < 3; i++) layoutActions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.333F));
            layoutActions.Controls.Add(btnRefresh, 0, 0);
            layoutActions.Controls.Add(btnSave, 1, 0);
            layoutActions.Controls.Add(btnClose, 2, 0);
            btnRefresh.Name = "btnRefresh"; btnRefresh.Text = "Refresh"; btnRefresh.Subtitle = "更新";
            btnSave.Name = "btnSave"; btnSave.Text = "Save"; btnSave.Subtitle = "保存";
            btnClose.Name = "btnClose"; btnClose.Text = "Close"; btnClose.Subtitle = "閉じる";
            foreach (var button in new[] { btnRefresh, btnSave, btnClose })
            {
                button.Dock = System.Windows.Forms.DockStyle.Fill;
                button.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
                button.UseCompactTextLayout = true;
                button.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
                button.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold);
                button.NormalColor = System.Drawing.Color.White;
                button.HoverColor = System.Drawing.Color.FromArgb(236, 243, 253);
                button.PressColor = System.Drawing.Color.FromArgb(222, 233, 250);
                button.ForeColor = System.Drawing.Color.FromArgb(30, 50, 85);
                button.SubtitleColor = button.ForeColor;
                button.BorderColor = System.Drawing.Color.FromArgb(199, 211, 226);
                button.BorderRadius = 5;
            }
            layoutRoot.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)gridBends).EndInit();
            ResumeLayout(false);
        }
    }
}
