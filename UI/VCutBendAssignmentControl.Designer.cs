namespace ADDIN.UI
{
    partial class VCutBendAssignmentControl
    {
        private System.ComponentModel.IContainer components;
        private System.Windows.Forms.TableLayoutPanel layoutRoot, layoutHeader, layoutProperties, layoutTools, layoutActions, layoutDetails;
        private System.Windows.Forms.Panel panelHeader, panelFooter;
        private System.Windows.Forms.Label lblTitle, lblDocument, lblMaterial, lblProperties, lblPropertyV1, lblPropertyV2, lblPropertyC, lblDetail, lblSummary, lblStatus, lblEmpty;
        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.CheckBox chkIssuesOnly;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.DataGridView gridBends;
        private System.Windows.Forms.DataGridViewTextBoxColumn colNo, colFeature, colStatus, colType, colValue;
        private System.Windows.Forms.DataGridViewComboBoxColumn colKind;
        private ADDIN.UI.ModernButton btnRefresh, btnSave, btnClose;

        protected override void Dispose(bool disposing)
        {
            if (disposing) ReleaseMinimum();
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
            lblDetail = new System.Windows.Forms.Label();
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
            BackColor = System.Drawing.Color.White;
            Font = new System.Drawing.Font("Segoe UI", 9.5F);
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            Size = new System.Drawing.Size(420, 560);
            Padding = new System.Windows.Forms.Padding(1);

            layoutRoot.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutRoot.Margin = new System.Windows.Forms.Padding(0);
            layoutRoot.BackColor = System.Drawing.Color.White;
            layoutRoot.ColumnCount = 1;
            layoutRoot.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutRoot.RowCount = 5;
            layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 100F));
            layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 0F));
            layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 82F));
            layoutRoot.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 44F));
            layoutRoot.Controls.Add(panelHeader, 0, 0);
            layoutRoot.Controls.Add(layoutTools, 0, 1);
            layoutRoot.Controls.Add(gridBends, 0, 2);
            layoutRoot.Controls.Add(panelFooter, 0, 3);
            layoutRoot.Controls.Add(layoutActions, 0, 4);
            Controls.Add(layoutRoot);

            panelHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            panelHeader.Margin = new System.Windows.Forms.Padding(0);
            panelHeader.BackColor = System.Drawing.Color.FromArgb(248, 250, 252);
            panelHeader.Controls.Add(layoutHeader);
            layoutHeader.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutHeader.Margin = new System.Windows.Forms.Padding(0);
            layoutHeader.BackColor = System.Drawing.Color.Transparent;
            layoutHeader.ColumnCount = 1;
            layoutHeader.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutHeader.RowCount = 3;
            layoutHeader.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F));
            layoutHeader.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 28F));
            layoutHeader.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 38F));
            layoutHeader.Controls.Add(lblTitle, 0, 0);
            layoutHeader.Controls.Add(lblDocument, 0, 1);
            layoutHeader.Controls.Add(layoutProperties, 0, 2);

            lblTitle.Text = "SET BEND LINE";
            lblTitle.BackColor = System.Drawing.Color.Transparent;
            lblTitle.Font = new System.Drawing.Font("Segoe UI", 10.5F, System.Drawing.FontStyle.Bold);
            lblTitle.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            lblTitle.Dock = System.Windows.Forms.DockStyle.Fill;
            lblTitle.Margin = new System.Windows.Forms.Padding(0);
            lblTitle.Padding = new System.Windows.Forms.Padding(16, 4, 8, 0);

            lblDocument.Dock = System.Windows.Forms.DockStyle.Fill;
            lblDocument.Margin = new System.Windows.Forms.Padding(0);
            lblDocument.Padding = new System.Windows.Forms.Padding(10, 1, 10, 0);
            lblDocument.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            lblDocument.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            lblDocument.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            lblDocument.AutoEllipsis = true;

            lblMaterial.Visible = false;
            lblProperties.Visible = false;

            layoutProperties.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutProperties.Margin = new System.Windows.Forms.Padding(6, 2, 6, 4);
            layoutProperties.BackColor = System.Drawing.Color.Transparent;
            layoutProperties.ColumnCount = 3;
            for (int i = 0; i < 3; i++) layoutProperties.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.333F));
            layoutProperties.Controls.Add(lblPropertyV1, 0, 0);
            layoutProperties.Controls.Add(lblPropertyV2, 1, 0);
            layoutProperties.Controls.Add(lblPropertyC, 2, 0);
            foreach (var card in new[] { lblPropertyV1, lblPropertyV2, lblPropertyC })
            {
                card.Dock = System.Windows.Forms.DockStyle.Fill;
                card.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
                card.Padding = new System.Windows.Forms.Padding(6, 0, 6, 0);
                card.BackColor = System.Drawing.Color.White;
                card.AutoEllipsis = true;
            }

            layoutTools.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutTools.Margin = new System.Windows.Forms.Padding(8, 3, 8, 3);
            layoutTools.BackColor = System.Drawing.Color.White;
            layoutTools.Visible = false;
            layoutTools.ColumnCount = 3;
            layoutTools.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            layoutTools.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutTools.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 88F));
            var searchLabel = new System.Windows.Forms.Label();
            searchLabel.Text = "Find";
            searchLabel.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            searchLabel.ForeColor = System.Drawing.Color.FromArgb(51, 65, 85);
            searchLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            searchLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            searchLabel.Margin = new System.Windows.Forms.Padding(0);
            layoutTools.Controls.Add(searchLabel, 0, 0);
            txtSearch.Name = "txtSearch";
            txtSearch.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            txtSearch.Dock = System.Windows.Forms.DockStyle.Fill;
            txtSearch.Margin = new System.Windows.Forms.Padding(0, 2, 6, 2);
            layoutTools.Controls.Add(txtSearch, 1, 0);
            chkIssuesOnly.Name = "chkIssuesOnly";
            chkIssuesOnly.Text = "NG only";
            chkIssuesOnly.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            chkIssuesOnly.ForeColor = System.Drawing.Color.FromArgb(51, 65, 85);
            chkIssuesOnly.Dock = System.Windows.Forms.DockStyle.Fill;
            chkIssuesOnly.Margin = new System.Windows.Forms.Padding(2, 0, 0, 0);
            layoutTools.Controls.Add(chkIssuesOnly, 2, 0);

            gridBends.Name = "gridBends";
            gridBends.Dock = System.Windows.Forms.DockStyle.Fill;
            gridBends.Margin = new System.Windows.Forms.Padding(0);
            gridBends.AllowUserToAddRows = false;
            gridBends.AllowUserToDeleteRows = false;
            gridBends.AllowUserToOrderColumns = false;
            gridBends.AllowUserToResizeRows = false;
            gridBends.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            gridBends.BackgroundColor = System.Drawing.Color.White;
            gridBends.BorderStyle = System.Windows.Forms.BorderStyle.None;
            gridBends.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.SingleHorizontal;
            gridBends.EnableHeadersVisualStyles = false;
            gridBends.ColumnHeadersHeight = 36;
            gridBends.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            gridBends.RowHeadersVisible = false;
            gridBends.MultiSelect = false;
            gridBends.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            gridBends.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnEnter;
            gridBends.RowTemplate.Height = 34;
            gridBends.Columns.AddRange(colNo, colFeature, colType, colValue, colStatus, colKind);
            colNo.Name = "colNo"; colNo.HeaderText = "#"; colNo.Width = 28;
            colNo.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            colFeature.Name = "colFeature"; colFeature.HeaderText = "Bend"; colFeature.MinimumWidth = 68; colFeature.FillWeight = 100;
            colType.Name = "colType"; colType.HeaderText = "Type"; colType.MinimumWidth = 72; colType.FillWeight = 55;
            colValue.Name = "colValue"; colValue.HeaderText = "V-Cut (残厚)"; colValue.Width = 82;
            colValue.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            colStatus.Name = "colStatus"; colStatus.HeaderText = "State"; colStatus.Width = 44;
            colStatus.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            colKind.Name = "colKind"; colKind.HeaderText = "Mark"; colKind.MinimumWidth = 72; colKind.Width = 78;
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
            lblEmpty.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            lblEmpty.ForeColor = System.Drawing.Color.FromArgb(100, 116, 139);
            lblEmpty.BackColor = System.Drawing.Color.White;
            lblEmpty.Visible = false;
            gridBends.Controls.Add(lblEmpty);

            panelFooter.Dock = System.Windows.Forms.DockStyle.Fill;
            panelFooter.Margin = new System.Windows.Forms.Padding(0);
            panelFooter.BackColor = System.Drawing.Color.FromArgb(248, 250, 252);
            panelFooter.Controls.Add(layoutDetails);
            layoutDetails.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutDetails.Margin = new System.Windows.Forms.Padding(10, 4, 10, 4);
            layoutDetails.BackColor = System.Drawing.Color.Transparent;
            layoutDetails.ColumnCount = 2;
            layoutDetails.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 80F));
            layoutDetails.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            layoutDetails.RowCount = 2;
            layoutDetails.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            layoutDetails.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 28F));
            layoutDetails.Controls.Add(lblDetail, 0, 0);
            layoutDetails.SetColumnSpan(lblDetail, 2);
            layoutDetails.Controls.Add(lblSummary, 0, 1);
            layoutDetails.Controls.Add(lblStatus, 1, 1);

            lblDetail.Name = "lblDetail";
            lblDetail.Dock = System.Windows.Forms.DockStyle.Fill;
            lblDetail.Margin = new System.Windows.Forms.Padding(0, 0, 0, 2);
            lblDetail.BackColor = System.Drawing.Color.Transparent;
            lblDetail.ForeColor = System.Drawing.Color.FromArgb(15, 23, 42);
            lblDetail.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);

            lblSummary.Dock = System.Windows.Forms.DockStyle.Fill;
            lblSummary.Margin = new System.Windows.Forms.Padding(0);
            lblSummary.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            lblSummary.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            lblSummary.AutoEllipsis = true;

            lblStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            lblStatus.Margin = new System.Windows.Forms.Padding(0);
            lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            lblStatus.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            lblStatus.AutoEllipsis = true;

            layoutActions.Dock = System.Windows.Forms.DockStyle.Fill;
            layoutActions.Margin = new System.Windows.Forms.Padding(0);
            layoutActions.Padding = new System.Windows.Forms.Padding(6, 4, 6, 6);
            layoutActions.BackColor = System.Drawing.Color.FromArgb(248, 250, 252);
            layoutActions.ColumnCount = 3;
            layoutActions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 32F));
            layoutActions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 32F));
            layoutActions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 36F));
            layoutActions.Controls.Add(btnRefresh, 0, 0);
            layoutActions.Controls.Add(btnClose, 1, 0);
            layoutActions.Controls.Add(btnSave, 2, 0);

            btnRefresh.Name = "btnRefresh"; btnRefresh.Text = "Refresh"; btnRefresh.Subtitle = "";
            btnRefresh.Kind = ModernButtonKind.Standard; btnRefresh.Icon = TaiIcon.Refresh;
            btnClose.Name = "btnClose"; btnClose.Text = "Close"; btnClose.Subtitle = "";
            btnClose.Kind = ModernButtonKind.Standard; btnClose.Icon = TaiIcon.Close;
            btnSave.Name = "btnSave"; btnSave.Text = "Save"; btnSave.Subtitle = "";
            btnSave.Kind = ModernButtonKind.Primary; btnSave.Icon = TaiIcon.Check;

            foreach (var button in new[] { btnRefresh, btnClose, btnSave })
            {
                button.Dock = System.Windows.Forms.DockStyle.Fill;
                button.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
                button.UseCompactTextLayout = false;
                button.Font = new System.Drawing.Font("Segoe UI Semibold", 9.25F, System.Drawing.FontStyle.Bold);
                button.BorderRadius = 6;
            }
            layoutRoot.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)gridBends).EndInit();
            ResumeLayout(false);
        }
    }
}
