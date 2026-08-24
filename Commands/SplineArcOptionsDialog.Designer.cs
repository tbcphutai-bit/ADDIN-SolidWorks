namespace ADDIN.Commands
{
    partial class SplineArcOptionsDialog
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Panel pnlHeader;
        private System.Windows.Forms.Label lblSubtitle;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblDirection;
        private System.Windows.Forms.CheckBox chkAutomaticStep;
        private System.Windows.Forms.Label lblStep;
        private System.Windows.Forms.TextBox txtStep;
        private System.Windows.Forms.Label lblTolerance;
        private System.Windows.Forms.TextBox txtTolerance;
        private System.Windows.Forms.Label lblToleranceUnit;
        private System.Windows.Forms.CheckBox chkAdaptive;
        private System.Windows.Forms.CheckBox chkRadiusDimensions;
        private System.Windows.Forms.CheckBox chkStepDimensions;
        private System.Windows.Forms.Button btnCreate;
        private System.Windows.Forms.Button btnCancel;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.pnlHeader = new System.Windows.Forms.Panel();
            this.lblSubtitle = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblDirection = new System.Windows.Forms.Label();
            this.chkAutomaticStep = new System.Windows.Forms.CheckBox();
            this.lblStep = new System.Windows.Forms.Label();
            this.txtStep = new System.Windows.Forms.TextBox();
            this.lblTolerance = new System.Windows.Forms.Label();
            this.txtTolerance = new System.Windows.Forms.TextBox();
            this.lblToleranceUnit = new System.Windows.Forms.Label();
            this.chkAdaptive = new System.Windows.Forms.CheckBox();
            this.chkRadiusDimensions = new System.Windows.Forms.CheckBox();
            this.chkStepDimensions = new System.Windows.Forms.CheckBox();
            this.btnCreate = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.pnlHeader.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlHeader
            // 
            this.pnlHeader.BackColor = System.Drawing.Color.FromArgb(38, 94, 143);
            this.pnlHeader.Controls.Add(this.lblSubtitle);
            this.pnlHeader.Controls.Add(this.lblTitle);
            this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader.Location = new System.Drawing.Point(0, 0);
            this.pnlHeader.Name = "pnlHeader";
            this.pnlHeader.Size = new System.Drawing.Size(430, 64);
            this.pnlHeader.TabIndex = 0;
            // 
            // lblSubtitle
            // 
            this.lblSubtitle.AutoSize = true;
            this.lblSubtitle.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblSubtitle.ForeColor = System.Drawing.Color.FromArgb(222, 238, 250);
            this.lblSubtitle.Location = new System.Drawing.Point(20, 37);
            this.lblSubtitle.Name = "lblSubtitle";
            this.lblSubtitle.Size = new System.Drawing.Size(281, 15);
            this.lblSubtitle.TabIndex = 1;
            this.lblSubtitle.Text = "Noi suy spline thanh chuoi cung tron trong sketch";
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold);
            this.lblTitle.ForeColor = System.Drawing.Color.White;
            this.lblTitle.Location = new System.Drawing.Point(18, 11);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(150, 20);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "SPLINE -> CUNG R";
            // 
            // lblDirection
            // 
            this.lblDirection.BackColor = System.Drawing.Color.FromArgb(240, 247, 253);
            this.lblDirection.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblDirection.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblDirection.ForeColor = System.Drawing.Color.FromArgb(48, 69, 88);
            this.lblDirection.Location = new System.Drawing.Point(20, 78);
            this.lblDirection.Name = "lblDirection";
            this.lblDirection.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.lblDirection.Size = new System.Drawing.Size(390, 46);
            this.lblDirection.TabIndex = 1;
            this.lblDirection.Text = "Huong chay bat dau tu dau spline gan vi tri ban click.\r\nMuon dao huong hay chon lai spline gan dau con lai.";
            // 
            // chkAutomaticStep
            // 
            this.chkAutomaticStep.AutoSize = true;
            this.chkAutomaticStep.Checked = true;
            this.chkAutomaticStep.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkAutomaticStep.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.chkAutomaticStep.ForeColor = System.Drawing.Color.FromArgb(28, 73, 126);
            this.chkAutomaticStep.Location = new System.Drawing.Point(22, 137);
            this.chkAutomaticStep.Name = "chkAutomaticStep";
            this.chkAutomaticStep.Size = new System.Drawing.Size(174, 19);
            this.chkAutomaticStep.TabIndex = 0;
            this.chkAutomaticStep.Text = "Tu tinh theo sai so cho phep";
            this.chkAutomaticStep.UseVisualStyleBackColor = true;
            this.chkAutomaticStep.CheckedChanged += new System.EventHandler(this.chkAutomaticStep_CheckedChanged);
            // 
            // lblStep
            // 
            this.lblStep.AutoSize = true;
            this.lblStep.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblStep.Location = new System.Drawing.Point(38, 171);
            this.lblStep.Name = "lblStep";
            this.lblStep.Size = new System.Drawing.Size(109, 15);
            this.lblStep.TabIndex = 2;
            this.lblStep.Text = "So doan muon chia";
            // 
            // txtStep
            // 
            this.txtStep.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.txtStep.Location = new System.Drawing.Point(258, 167);
            this.txtStep.Name = "txtStep";
            this.txtStep.Size = new System.Drawing.Size(68, 23);
            this.txtStep.TabIndex = 1;
            this.txtStep.Text = "4";
            this.txtStep.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // lblTolerance
            // 
            this.lblTolerance.AutoSize = true;
            this.lblTolerance.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblTolerance.Location = new System.Drawing.Point(38, 204);
            this.lblTolerance.Name = "lblTolerance";
            this.lblTolerance.Size = new System.Drawing.Size(129, 15);
            this.lblTolerance.TabIndex = 4;
            this.lblTolerance.Text = "Sai so cho phep (mm)";
            // 
            // txtTolerance
            // 
            this.txtTolerance.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.txtTolerance.Location = new System.Drawing.Point(258, 200);
            this.txtTolerance.Name = "txtTolerance";
            this.txtTolerance.Size = new System.Drawing.Size(68, 23);
            this.txtTolerance.TabIndex = 2;
            this.txtTolerance.Text = "0.1";
            this.txtTolerance.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // lblToleranceUnit
            // 
            this.lblToleranceUnit.AutoSize = true;
            this.lblToleranceUnit.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblToleranceUnit.Location = new System.Drawing.Point(334, 204);
            this.lblToleranceUnit.Name = "lblToleranceUnit";
            this.lblToleranceUnit.Size = new System.Drawing.Size(28, 15);
            this.lblToleranceUnit.TabIndex = 6;
            this.lblToleranceUnit.Text = "mm";
            // 
            // chkAdaptive
            // 
            this.chkAdaptive.AutoSize = true;
            this.chkAdaptive.Checked = true;
            this.chkAdaptive.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkAdaptive.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.chkAdaptive.Location = new System.Drawing.Point(22, 238);
            this.chkAdaptive.Name = "chkAdaptive";
            this.chkAdaptive.Size = new System.Drawing.Size(172, 19);
            this.chkAdaptive.TabIndex = 3;
            this.chkAdaptive.Text = "Tu chia khi vuot sai so phep";
            this.chkAdaptive.UseVisualStyleBackColor = true;
            // 
            // chkRadiusDimensions
            // 
            this.chkRadiusDimensions.AutoSize = true;
            this.chkRadiusDimensions.Checked = true;
            this.chkRadiusDimensions.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkRadiusDimensions.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.chkRadiusDimensions.Location = new System.Drawing.Point(22, 265);
            this.chkRadiusDimensions.Name = "chkRadiusDimensions";
            this.chkRadiusDimensions.Size = new System.Drawing.Size(137, 19);
            this.chkRadiusDimensions.TabIndex = 4;
            this.chkRadiusDimensions.Text = "Tao kich thuoc ban kinh";
            this.chkRadiusDimensions.UseVisualStyleBackColor = true;
            // 
            // chkStepDimensions
            // 
            this.chkStepDimensions.AutoSize = true;
            this.chkStepDimensions.Checked = true;
            this.chkStepDimensions.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkStepDimensions.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.chkStepDimensions.Location = new System.Drawing.Point(22, 292);
            this.chkStepDimensions.Name = "chkStepDimensions";
            this.chkStepDimensions.Size = new System.Drawing.Size(123, 19);
            this.chkStepDimensions.TabIndex = 5;
            this.chkStepDimensions.Text = "Tao kich thuoc buoc";
            this.chkStepDimensions.UseVisualStyleBackColor = true;
            // 
            // btnCreate
            // 
            this.btnCreate.Location = new System.Drawing.Point(234, 334);
            this.btnCreate.Name = "btnCreate";
            this.btnCreate.Size = new System.Drawing.Size(80, 28);
            this.btnCreate.TabIndex = 6;
            this.btnCreate.Text = "Tao";
            this.btnCreate.UseVisualStyleBackColor = true;
            this.btnCreate.Click += new System.EventHandler(this.btnCreate_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(330, 334);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(80, 28);
            this.btnCancel.TabIndex = 7;
            this.btnCancel.Text = "Huy";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // SplineArcOptionsDialog
            // 
            this.AcceptButton = this.btnCreate;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(430, 381);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnCreate);
            this.Controls.Add(this.chkStepDimensions);
            this.Controls.Add(this.chkRadiusDimensions);
            this.Controls.Add(this.chkAdaptive);
            this.Controls.Add(this.lblToleranceUnit);
            this.Controls.Add(this.txtTolerance);
            this.Controls.Add(this.lblTolerance);
            this.Controls.Add(this.txtStep);
            this.Controls.Add(this.lblStep);
            this.Controls.Add(this.chkAutomaticStep);
            this.Controls.Add(this.lblDirection);
            this.Controls.Add(this.pnlHeader);
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SplineArcOptionsDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Spline -> Cung R";
            this.pnlHeader.ResumeLayout(false);
            this.pnlHeader.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion
    }
}
