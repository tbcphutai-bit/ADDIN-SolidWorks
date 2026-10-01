namespace ADDIN.UI
{
    partial class VCutNoticeDialog
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Panel panelAccent;
        private System.Windows.Forms.Panel panelHeader;
        private System.Windows.Forms.Panel pnlIcon;
        private System.Windows.Forms.Label lblEyebrow;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Panel panelBodyCard;
        private System.Windows.Forms.Label lblBody;
        private System.Windows.Forms.Panel panelFooter;
        private ADDIN.UI.ModernButton btnPrimary;
        private ADDIN.UI.ModernButton btnSecondary;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.panelAccent = new System.Windows.Forms.Panel();
            this.panelHeader = new System.Windows.Forms.Panel();
            this.pnlIcon = new System.Windows.Forms.Panel();
            this.lblEyebrow = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.panelBodyCard = new System.Windows.Forms.Panel();
            this.lblBody = new System.Windows.Forms.Label();
            this.panelFooter = new System.Windows.Forms.Panel();
            this.btnPrimary = new ADDIN.UI.ModernButton();
            this.btnSecondary = new ADDIN.UI.ModernButton();
            this.panelHeader.SuspendLayout();
            this.panelBodyCard.SuspendLayout();
            this.panelFooter.SuspendLayout();
            this.SuspendLayout();
            // 
            // panelAccent
            // 
            this.panelAccent.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(119)))), ((int)(((byte)(6)))));
            this.panelAccent.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelAccent.Location = new System.Drawing.Point(0, 0);
            this.panelAccent.Name = "panelAccent";
            this.panelAccent.Size = new System.Drawing.Size(500, 3);
            this.panelAccent.TabIndex = 0;
            // 
            // panelHeader
            // 
            this.panelHeader.BackColor = System.Drawing.Color.White;
            this.panelHeader.Controls.Add(this.pnlIcon);
            this.panelHeader.Controls.Add(this.lblEyebrow);
            this.panelHeader.Controls.Add(this.lblTitle);
            this.panelHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelHeader.Location = new System.Drawing.Point(0, 3);
            this.panelHeader.Name = "panelHeader";
            this.panelHeader.Size = new System.Drawing.Size(500, 64);
            this.panelHeader.TabIndex = 1;
            // 
            // pnlIcon
            // 
            this.pnlIcon.Location = new System.Drawing.Point(20, 11);
            this.pnlIcon.Name = "pnlIcon";
            this.pnlIcon.Size = new System.Drawing.Size(42, 42);
            this.pnlIcon.TabIndex = 0;
            // 
            // lblEyebrow
            // 
            this.lblEyebrow.AutoSize = true;
            this.lblEyebrow.Font = new System.Drawing.Font("Meiryo UI", 8.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblEyebrow.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(119)))), ((int)(((byte)(6)))));
            this.lblEyebrow.Location = new System.Drawing.Point(72, 12);
            this.lblEyebrow.Name = "lblEyebrow";
            this.lblEyebrow.Size = new System.Drawing.Size(155, 15);
            this.lblEyebrow.TabIndex = 1;
            this.lblEyebrow.Text = "TAI TOOL · THÔNG BÁO";
            // 
            // lblTitle
            // 
            this.lblTitle.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblTitle.AutoEllipsis = true;
            this.lblTitle.Font = new System.Drawing.Font("Meiryo UI", 11.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
            this.lblTitle.Location = new System.Drawing.Point(71, 29);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(410, 26);
            this.lblTitle.TabIndex = 2;
            this.lblTitle.Text = "Thông báo";
            // 
            // panelBodyCard
            // 
            this.panelBodyCard.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelBodyCard.AutoScroll = true;
            this.panelBodyCard.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(254)))), ((int)(((byte)(252)))), ((int)(((byte)(248)))));
            this.panelBodyCard.Controls.Add(this.lblBody);
            this.panelBodyCard.Location = new System.Drawing.Point(20, 78);
            this.panelBodyCard.Name = "panelBodyCard";
            this.panelBodyCard.Padding = new System.Windows.Forms.Padding(1);
            this.panelBodyCard.Size = new System.Drawing.Size(460, 140);
            this.panelBodyCard.TabIndex = 2;
            // 
            // lblBody
            // 
            this.lblBody.AutoEllipsis = false;
            this.lblBody.AutoSize = true;
            this.lblBody.BackColor = System.Drawing.Color.Transparent;
            this.lblBody.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblBody.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblBody.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(15)))), ((int)(((byte)(23)))), ((int)(((byte)(42)))));
            this.lblBody.Location = new System.Drawing.Point(1, 1);
            this.lblBody.Name = "lblBody";
            this.lblBody.Padding = new System.Windows.Forms.Padding(16, 14, 16, 14);
            this.lblBody.Size = new System.Drawing.Size(458, 43);
            this.lblBody.TabIndex = 0;
            // 
            // panelFooter
            // 
            this.panelFooter.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
            this.panelFooter.Controls.Add(this.btnPrimary);
            this.panelFooter.Controls.Add(this.btnSecondary);
            this.panelFooter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelFooter.Location = new System.Drawing.Point(0, 230);
            this.panelFooter.Name = "panelFooter";
            this.panelFooter.Size = new System.Drawing.Size(500, 54);
            this.panelFooter.TabIndex = 3;
            // 
            // btnPrimary
            // 
            this.btnPrimary.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnPrimary.BackColor = System.Drawing.Color.Transparent;
            this.btnPrimary.BorderColor = System.Drawing.Color.Transparent;
            this.btnPrimary.BorderRadius = 5;
            this.btnPrimary.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnPrimary.FlatAppearance.BorderSize = 0;
            this.btnPrimary.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPrimary.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnPrimary.ForeColor = System.Drawing.Color.White;
            this.btnPrimary.Location = new System.Drawing.Point(384, 11);
            this.btnPrimary.Name = "btnPrimary";
            this.btnPrimary.NormalColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(119)))), ((int)(((byte)(6)))));
            this.btnPrimary.Size = new System.Drawing.Size(96, 32);
            this.btnPrimary.TabIndex = 0;
            this.btnPrimary.Text = "Đóng";
            this.btnPrimary.UseVisualStyleBackColor = false;
            // 
            // btnSecondary
            // 
            this.btnSecondary.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSecondary.BackColor = System.Drawing.Color.Transparent;
            this.btnSecondary.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(203)))), ((int)(((byte)(213)))), ((int)(((byte)(225)))));
            this.btnSecondary.BorderRadius = 5;
            this.btnSecondary.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnSecondary.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSecondary.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnSecondary.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.btnSecondary.Location = new System.Drawing.Point(288, 11);
            this.btnSecondary.Name = "btnSecondary";
            this.btnSecondary.NormalColor = System.Drawing.Color.White;
            this.btnSecondary.HoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(241)))), ((int)(((byte)(245)))), ((int)(((byte)(249)))));
            this.btnSecondary.PressColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(232)))), ((int)(((byte)(240)))));
            this.btnSecondary.Size = new System.Drawing.Size(86, 32);
            this.btnSecondary.TabIndex = 1;
            this.btnSecondary.Text = "Hủy";
            this.btnSecondary.UseVisualStyleBackColor = false;
            // 
            // VCutNoticeDialog
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(500, 284);
            this.Controls.Add(this.panelBodyCard);
            this.Controls.Add(this.panelFooter);
            this.Controls.Add(this.panelHeader);
            this.Controls.Add(this.panelAccent);
            this.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "VCutNoticeDialog";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Thông báo";
            this.panelHeader.ResumeLayout(false);
            this.panelHeader.PerformLayout();
            this.panelBodyCard.ResumeLayout(false);
            this.panelBodyCard.PerformLayout();
            this.panelFooter.ResumeLayout(false);
            this.ResumeLayout(false);
        }
    }
}
