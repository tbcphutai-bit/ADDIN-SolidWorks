namespace ADDIN.Commands
{
    partial class CheckAllSelectionDialog
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
            this.pnlHeader = new System.Windows.Forms.Panel();
            this.lblSubtitle = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblInstruction = new System.Windows.Forms.Label();
            this.pnlCheckList = new System.Windows.Forms.Panel();
            this.lblKegakiDescription = new System.Windows.Forms.Label();
            this.lblUraDescription = new System.Windows.Forms.Label();
            this.lblDfTkDescription = new System.Windows.Forms.Label();
            this.chkKegaki = new System.Windows.Forms.CheckBox();
            this.chkUraOmote = new System.Windows.Forms.CheckBox();
            this.chkDfTk = new System.Windows.Forms.CheckBox();
            this.lblSelectionCount = new System.Windows.Forms.Label();
            this.btnRun = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.pnlHeader.SuspendLayout();
            this.pnlCheckList.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlHeader
            // 
            this.pnlHeader.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(37)))), ((int)(((byte)(88)))), ((int)(((byte)(137)))));
            this.pnlHeader.Controls.Add(this.lblSubtitle);
            this.pnlHeader.Controls.Add(this.lblTitle);
            this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader.Location = new System.Drawing.Point(0, 0);
            this.pnlHeader.Name = "pnlHeader";
            this.pnlHeader.Size = new System.Drawing.Size(438, 60);
            this.pnlHeader.TabIndex = 0;
            // 
            // lblSubtitle
            // 
            this.lblSubtitle.AutoSize = true;
            this.lblSubtitle.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblSubtitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(235)))), ((int)(((byte)(249)))));
            this.lblSubtitle.Location = new System.Drawing.Point(20, 35);
            this.lblSubtitle.Name = "lblSubtitle";
            this.lblSubtitle.Size = new System.Drawing.Size(245, 15);
            this.lblSubtitle.TabIndex = 1;
            this.lblSubtitle.Text = "Chon noi dung can kiem tra va xuat Excel";
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblTitle.ForeColor = System.Drawing.Color.White;
            this.lblTitle.Location = new System.Drawing.Point(18, 11);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(203, 20);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "CHECK \u30A6\u30E9\u8868 KEGAKI - CHON LENH";
            // 
            // lblInstruction
            // 
            this.lblInstruction.AutoSize = true;
            this.lblInstruction.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblInstruction.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(80)))), ((int)(((byte)(89)))), ((int)(((byte)(101)))));
            this.lblInstruction.Location = new System.Drawing.Point(20, 72);
            this.lblInstruction.Name = "lblInstruction";
            this.lblInstruction.Size = new System.Drawing.Size(184, 15);
            this.lblInstruction.TabIndex = 1;
            this.lblInstruction.Text = "Mac dinh da chon san ca hai lenh.";
            // 
            // pnlCheckList
            // 
            this.pnlCheckList.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(245)))), ((int)(((byte)(251)))));
            this.pnlCheckList.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlCheckList.Controls.Add(this.lblKegakiDescription);
            this.pnlCheckList.Controls.Add(this.lblUraDescription);
            this.pnlCheckList.Controls.Add(this.chkKegaki);
            this.pnlCheckList.Controls.Add(this.chkUraOmote);
            this.pnlCheckList.Location = new System.Drawing.Point(20, 94);
            this.pnlCheckList.Name = "pnlCheckList";
            this.pnlCheckList.Size = new System.Drawing.Size(398, 83);
            this.pnlCheckList.TabIndex = 2;
            // 
            // lblKegakiDescription
            // 
            this.lblKegakiDescription.AutoSize = true;
            this.lblKegakiDescription.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblKegakiDescription.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(104)))), ((int)(((byte)(92)))), ((int)(((byte)(112)))));
            this.lblKegakiDescription.Location = new System.Drawing.Point(174, 49);
            this.lblKegakiDescription.Name = "lblKegakiDescription";
            this.lblKegakiDescription.Size = new System.Drawing.Size(120, 15);
            this.lblKegakiDescription.TabIndex = 5;
            this.lblKegakiDescription.Text = "Bend Table / he so be";
            // 
            // lblUraDescription
            // 
            this.lblUraDescription.AutoSize = true;
            this.lblUraDescription.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblUraDescription.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(104)))), ((int)(((byte)(92)))), ((int)(((byte)(112)))));
            this.lblUraDescription.Location = new System.Drawing.Point(174, 15);
            this.lblUraDescription.Name = "lblUraDescription";
            this.lblUraDescription.Size = new System.Drawing.Size(112, 15);
            this.lblUraDescription.TabIndex = 4;
            this.lblUraDescription.Text = "Mat truoc / mat sau";
            // 
            // lblDfTkDescription
            // 
            this.lblDfTkDescription.AutoSize = true;
            this.lblDfTkDescription.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblDfTkDescription.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(104)))), ((int)(((byte)(92)))), ((int)(((byte)(112)))));
            this.lblDfTkDescription.Location = new System.Drawing.Point(174, 15);
            this.lblDfTkDescription.Name = "lblDfTkDescription";
            this.lblDfTkDescription.Size = new System.Drawing.Size(118, 15);
            this.lblDfTkDescription.TabIndex = 3;
            this.lblDfTkDescription.Text = "Default / Flat-Pattern";
            // 
            // chkKegaki
            // 
            this.chkKegaki.Checked = true;
            this.chkKegaki.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkKegaki.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.chkKegaki.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(79)))), ((int)(((byte)(40)))), ((int)(((byte)(102)))));
            this.chkKegaki.Location = new System.Drawing.Point(16, 41);
            this.chkKegaki.Name = "chkKegaki";
            this.chkKegaki.Size = new System.Drawing.Size(150, 28);
            this.chkKegaki.TabIndex = 2;
            this.chkKegaki.Text = "CHECK KEGAKI";
            this.chkKegaki.UseVisualStyleBackColor = true;
            this.chkKegaki.CheckedChanged += new System.EventHandler(this.CheckOption_CheckedChanged);
            // 
            // chkUraOmote
            // 
            this.chkUraOmote.Checked = true;
            this.chkUraOmote.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkUraOmote.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.chkUraOmote.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(79)))), ((int)(((byte)(40)))), ((int)(((byte)(102)))));
            this.chkUraOmote.Location = new System.Drawing.Point(16, 7);
            this.chkUraOmote.Name = "chkUraOmote";
            this.chkUraOmote.Size = new System.Drawing.Size(150, 28);
            this.chkUraOmote.TabIndex = 1;
            this.chkUraOmote.Text = "CHECK \u30A6\u30E9\u8868";
            this.chkUraOmote.UseVisualStyleBackColor = true;
            this.chkUraOmote.CheckedChanged += new System.EventHandler(this.CheckOption_CheckedChanged);
            // 
            // chkDfTk
            // 
            this.chkDfTk.Checked = true;
            this.chkDfTk.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkDfTk.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.chkDfTk.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(79)))), ((int)(((byte)(40)))), ((int)(((byte)(102)))));
            this.chkDfTk.Location = new System.Drawing.Point(16, 7);
            this.chkDfTk.Name = "chkDfTk";
            this.chkDfTk.Size = new System.Drawing.Size(150, 28);
            this.chkDfTk.TabIndex = 0;
            this.chkDfTk.Text = "CHECK DF/TK";
            this.chkDfTk.UseVisualStyleBackColor = true;
            this.chkDfTk.CheckedChanged += new System.EventHandler(this.CheckOption_CheckedChanged);
            // 
            // lblSelectionCount
            // 
            this.lblSelectionCount.AutoSize = true;
            this.lblSelectionCount.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblSelectionCount.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(37)))), ((int)(((byte)(88)))), ((int)(((byte)(137)))));
            this.lblSelectionCount.Location = new System.Drawing.Point(20, 188);
            this.lblSelectionCount.Name = "lblSelectionCount";
            this.lblSelectionCount.Size = new System.Drawing.Size(101, 15);
            this.lblSelectionCount.TabIndex = 3;
            this.lblSelectionCount.Text = "Da chon 2/2 lenh";
            // 
            // btnRun
            // 
            this.btnRun.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(235)))), ((int)(((byte)(252)))));
            this.btnRun.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(82)))), ((int)(((byte)(132)))), ((int)(((byte)(190)))));
            this.btnRun.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(202)))), ((int)(((byte)(224)))), ((int)(((byte)(249)))));
            this.btnRun.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRun.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnRun.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(24)))), ((int)(((byte)(74)))), ((int)(((byte)(126)))));
            this.btnRun.Location = new System.Drawing.Point(226, 214);
            this.btnRun.Name = "btnRun";
            this.btnRun.Size = new System.Drawing.Size(92, 30);
            this.btnRun.TabIndex = 4;
            this.btnRun.Text = "CHAY";
            this.btnRun.UseVisualStyleBackColor = false;
            this.btnRun.Click += new System.EventHandler(this.btnRun_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(246)))), ((int)(((byte)(247)))), ((int)(((byte)(249)))));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(197)))), ((int)(((byte)(204)))), ((int)(((byte)(213)))));
            this.btnCancel.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(240)))), ((int)(((byte)(246)))));
            this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCancel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnCancel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(42)))), ((int)(((byte)(48)))), ((int)(((byte)(56)))));
            this.btnCancel.Location = new System.Drawing.Point(326, 214);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(92, 30);
            this.btnCancel.TabIndex = 5;
            this.btnCancel.Text = "HUY";
            this.btnCancel.UseVisualStyleBackColor = false;
            // 
            // CheckAllSelectionDialog
            // 
            this.AcceptButton = this.btnRun;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(250)))), ((int)(((byte)(251)))), ((int)(((byte)(253)))));
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(438, 256);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnRun);
            this.Controls.Add(this.lblSelectionCount);
            this.Controls.Add(this.pnlCheckList);
            this.Controls.Add(this.lblInstruction);
            this.Controls.Add(this.pnlHeader);
            this.Font = new System.Drawing.Font("Times New Roman", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "CheckAllSelectionDialog";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "CHECK \u30A6\u30E9\u8868 KEGAKI";
            this.pnlHeader.ResumeLayout(false);
            this.pnlHeader.PerformLayout();
            this.pnlCheckList.ResumeLayout(false);
            this.pnlCheckList.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Panel pnlHeader;
        private System.Windows.Forms.Label lblSubtitle;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblInstruction;
        private System.Windows.Forms.Panel pnlCheckList;
        private System.Windows.Forms.CheckBox chkDfTk;
        private System.Windows.Forms.CheckBox chkUraOmote;
        private System.Windows.Forms.CheckBox chkKegaki;
        private System.Windows.Forms.Label lblDfTkDescription;
        private System.Windows.Forms.Label lblUraDescription;
        private System.Windows.Forms.Label lblKegakiDescription;
        private System.Windows.Forms.Label lblSelectionCount;
        private System.Windows.Forms.Button btnRun;
        private System.Windows.Forms.Button btnCancel;
    }
}
