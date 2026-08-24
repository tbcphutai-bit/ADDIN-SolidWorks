namespace ADDIN.Commands
{
    partial class SamePartToleranceInputDialog
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.pnlHeader = new System.Windows.Forms.Panel();
            this.lblSubtitle = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.grpTolerance = new System.Windows.Forms.GroupBox();
            this.lblAreaAbsolute = new System.Windows.Forms.Label();
            this.lblAreaRelative = new System.Windows.Forms.Label();
            this.lblEdgeLength = new System.Windows.Forms.Label();
            this.lblVolumeAbsolute = new System.Windows.Forms.Label();
            this.lblVolumeRelative = new System.Windows.Forms.Label();
            this.lblPrincipalMoment = new System.Windows.Forms.Label();
            this.lblHoleLinear = new System.Windows.Forms.Label();
            this.lblHoleRadius = new System.Windows.Forms.Label();
            this.numAreaAbsolute = new System.Windows.Forms.NumericUpDown();
            this.numAreaRelative = new System.Windows.Forms.NumericUpDown();
            this.numEdgeLength = new System.Windows.Forms.NumericUpDown();
            this.numVolumeAbsolute = new System.Windows.Forms.NumericUpDown();
            this.numVolumeRelative = new System.Windows.Forms.NumericUpDown();
            this.numPrincipalMoment = new System.Windows.Forms.NumericUpDown();
            this.numHoleLinear = new System.Windows.Forms.NumericUpDown();
            this.numHoleRadius = new System.Windows.Forms.NumericUpDown();
            this.lblUnitAreaAbsolute = new System.Windows.Forms.Label();
            this.lblUnitAreaRelative = new System.Windows.Forms.Label();
            this.lblUnitEdgeLength = new System.Windows.Forms.Label();
            this.lblUnitVolumeAbsolute = new System.Windows.Forms.Label();
            this.lblUnitVolumeRelative = new System.Windows.Forms.Label();
            this.lblUnitPrincipalMoment = new System.Windows.Forms.Label();
            this.lblUnitHoleLinear = new System.Windows.Forms.Label();
            this.lblUnitHoleRadius = new System.Windows.Forms.Label();
            this.btnRun = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.pnlHeader.SuspendLayout();
            this.grpTolerance.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numAreaAbsolute)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAreaRelative)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numEdgeLength)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numVolumeAbsolute)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numVolumeRelative)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPrincipalMoment)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numHoleLinear)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numHoleRadius)).BeginInit();
            this.SuspendLayout();
            // 
            // pnlHeader
            // 
            this.pnlHeader.BackColor = System.Drawing.Color.FromArgb(37, 88, 137);
            this.pnlHeader.Controls.Add(this.lblSubtitle);
            this.pnlHeader.Controls.Add(this.lblTitle);
            this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader.Location = new System.Drawing.Point(0, 0);
            this.pnlHeader.Name = "pnlHeader";
            this.pnlHeader.Size = new System.Drawing.Size(540, 68);
            this.pnlHeader.TabIndex = 0;
            // 
            // lblSubtitle
            // 
            this.lblSubtitle.AutoSize = true;
            this.lblSubtitle.Font = new System.Drawing.Font("Meiryo UI", 8.5F);
            this.lblSubtitle.ForeColor = System.Drawing.Color.FromArgb(220, 235, 249);
            this.lblSubtitle.Location = new System.Drawing.Point(21, 39);
            this.lblSubtitle.Name = "lblSubtitle";
            this.lblSubtitle.Size = new System.Drawing.Size(346, 15);
            this.lblSubtitle.TabIndex = 1;
            this.lblSubtitle.Text = "Nhap gia tri dung sai truoc khi so sanh hinh hoc chi tiet";
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Meiryo UI", 11F, System.Drawing.FontStyle.Bold);
            this.lblTitle.ForeColor = System.Drawing.Color.White;
            this.lblTitle.Location = new System.Drawing.Point(20, 11);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(250, 19);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "CHECK SAME PART - DUNG SAI";
            // 
            // grpTolerance
            // 
            this.grpTolerance.BackColor = System.Drawing.Color.White;
            this.grpTolerance.Controls.Add(this.lblUnitHoleRadius);
            this.grpTolerance.Controls.Add(this.lblUnitHoleLinear);
            this.grpTolerance.Controls.Add(this.lblUnitPrincipalMoment);
            this.grpTolerance.Controls.Add(this.lblUnitVolumeRelative);
            this.grpTolerance.Controls.Add(this.lblUnitVolumeAbsolute);
            this.grpTolerance.Controls.Add(this.lblUnitEdgeLength);
            this.grpTolerance.Controls.Add(this.lblUnitAreaRelative);
            this.grpTolerance.Controls.Add(this.lblUnitAreaAbsolute);
            this.grpTolerance.Controls.Add(this.numHoleRadius);
            this.grpTolerance.Controls.Add(this.numHoleLinear);
            this.grpTolerance.Controls.Add(this.numPrincipalMoment);
            this.grpTolerance.Controls.Add(this.numVolumeRelative);
            this.grpTolerance.Controls.Add(this.numVolumeAbsolute);
            this.grpTolerance.Controls.Add(this.numEdgeLength);
            this.grpTolerance.Controls.Add(this.numAreaRelative);
            this.grpTolerance.Controls.Add(this.numAreaAbsolute);
            this.grpTolerance.Controls.Add(this.lblHoleRadius);
            this.grpTolerance.Controls.Add(this.lblHoleLinear);
            this.grpTolerance.Controls.Add(this.lblPrincipalMoment);
            this.grpTolerance.Controls.Add(this.lblVolumeRelative);
            this.grpTolerance.Controls.Add(this.lblVolumeAbsolute);
            this.grpTolerance.Controls.Add(this.lblEdgeLength);
            this.grpTolerance.Controls.Add(this.lblAreaRelative);
            this.grpTolerance.Controls.Add(this.lblAreaAbsolute);
            this.grpTolerance.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold);
            this.grpTolerance.ForeColor = System.Drawing.Color.FromArgb(35, 55, 75);
            this.grpTolerance.Location = new System.Drawing.Point(18, 82);
            this.grpTolerance.Name = "grpTolerance";
            this.grpTolerance.Size = new System.Drawing.Size(504, 338);
            this.grpTolerance.TabIndex = 1;
            this.grpTolerance.TabStop = false;
            this.grpTolerance.Text = "Dung sai so sanh hinh hoc";
            // 
            // lblAreaAbsolute
            // 
            this.lblAreaAbsolute.AutoSize = true;
            this.lblAreaAbsolute.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblAreaAbsolute.ForeColor = System.Drawing.Color.FromArgb(35, 45, 55);
            this.lblAreaAbsolute.Location = new System.Drawing.Point(24, 31);
            this.lblAreaAbsolute.Name = "lblAreaAbsolute";
            this.lblAreaAbsolute.TabIndex = 0;
            this.lblAreaAbsolute.Text = "Dien tich - sai lech tuyet doi";
            // lblAreaRelative
            // 
            this.lblAreaRelative.AutoSize = true;
            this.lblAreaRelative.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblAreaRelative.ForeColor = System.Drawing.Color.FromArgb(35, 45, 55);
            this.lblAreaRelative.Location = new System.Drawing.Point(24, 68);
            this.lblAreaRelative.Name = "lblAreaRelative";
            this.lblAreaRelative.TabIndex = 1;
            this.lblAreaRelative.Text = "Dien tich - sai lech tuong doi";
            // lblEdgeLength
            // 
            this.lblEdgeLength.AutoSize = true;
            this.lblEdgeLength.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblEdgeLength.ForeColor = System.Drawing.Color.FromArgb(35, 45, 55);
            this.lblEdgeLength.Location = new System.Drawing.Point(24, 105);
            this.lblEdgeLength.Name = "lblEdgeLength";
            this.lblEdgeLength.TabIndex = 2;
            this.lblEdgeLength.Text = "Tong chieu dai canh";
            // lblVolumeAbsolute
            // 
            this.lblVolumeAbsolute.AutoSize = true;
            this.lblVolumeAbsolute.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblVolumeAbsolute.ForeColor = System.Drawing.Color.FromArgb(35, 45, 55);
            this.lblVolumeAbsolute.Location = new System.Drawing.Point(24, 142);
            this.lblVolumeAbsolute.Name = "lblVolumeAbsolute";
            this.lblVolumeAbsolute.TabIndex = 3;
            this.lblVolumeAbsolute.Text = "The tich - sai lech tuyet doi";
            // lblVolumeRelative
            // 
            this.lblVolumeRelative.AutoSize = true;
            this.lblVolumeRelative.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblVolumeRelative.ForeColor = System.Drawing.Color.FromArgb(35, 45, 55);
            this.lblVolumeRelative.Location = new System.Drawing.Point(24, 179);
            this.lblVolumeRelative.Name = "lblVolumeRelative";
            this.lblVolumeRelative.TabIndex = 4;
            this.lblVolumeRelative.Text = "The tich - sai lech tuong doi";
            // lblPrincipalMoment
            // 
            this.lblPrincipalMoment.AutoSize = true;
            this.lblPrincipalMoment.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblPrincipalMoment.ForeColor = System.Drawing.Color.FromArgb(35, 45, 55);
            this.lblPrincipalMoment.Location = new System.Drawing.Point(24, 216);
            this.lblPrincipalMoment.Name = "lblPrincipalMoment";
            this.lblPrincipalMoment.TabIndex = 5;
            this.lblPrincipalMoment.Text = "Momen chinh - sai lech tuong doi";
            // lblHoleLinear
            // 
            this.lblHoleLinear.AutoSize = true;
            this.lblHoleLinear.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblHoleLinear.ForeColor = System.Drawing.Color.FromArgb(35, 45, 55);
            this.lblHoleLinear.Location = new System.Drawing.Point(24, 253);
            this.lblHoleLinear.Name = "lblHoleLinear";
            this.lblHoleLinear.TabIndex = 6;
            this.lblHoleLinear.Text = "Vi tri / chu vi / canh lo";
            // lblHoleRadius
            // 
            this.lblHoleRadius.AutoSize = true;
            this.lblHoleRadius.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblHoleRadius.ForeColor = System.Drawing.Color.FromArgb(35, 45, 55);
            this.lblHoleRadius.Location = new System.Drawing.Point(24, 290);
            this.lblHoleRadius.Name = "lblHoleRadius";
            this.lblHoleRadius.TabIndex = 7;
            this.lblHoleRadius.Text = "Ban kinh lo";
            // numAreaAbsolute
            // 
            this.numAreaAbsolute.DecimalPlaces = 3;
            this.numAreaAbsolute.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.numAreaAbsolute.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            this.numAreaAbsolute.Location = new System.Drawing.Point(322, 27);
            this.numAreaAbsolute.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.numAreaAbsolute.Name = "numAreaAbsolute";
            this.numAreaAbsolute.Size = new System.Drawing.Size(98, 23);
            this.numAreaAbsolute.TabIndex = 0;
            this.numAreaAbsolute.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numAreaAbsolute.ThousandsSeparator = true;
            // numAreaRelative
            // 
            this.numAreaRelative.DecimalPlaces = 4;
            this.numAreaRelative.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.numAreaRelative.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            this.numAreaRelative.Location = new System.Drawing.Point(322, 64);
            this.numAreaRelative.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numAreaRelative.Name = "numAreaRelative";
            this.numAreaRelative.Size = new System.Drawing.Size(98, 23);
            this.numAreaRelative.TabIndex = 1;
            this.numAreaRelative.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numAreaRelative.ThousandsSeparator = true;
            // numEdgeLength
            // 
            this.numEdgeLength.DecimalPlaces = 3;
            this.numEdgeLength.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.numEdgeLength.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            this.numEdgeLength.Location = new System.Drawing.Point(322, 101);
            this.numEdgeLength.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.numEdgeLength.Name = "numEdgeLength";
            this.numEdgeLength.Size = new System.Drawing.Size(98, 23);
            this.numEdgeLength.TabIndex = 2;
            this.numEdgeLength.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numEdgeLength.ThousandsSeparator = true;
            // numVolumeAbsolute
            // 
            this.numVolumeAbsolute.DecimalPlaces = 3;
            this.numVolumeAbsolute.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.numVolumeAbsolute.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            this.numVolumeAbsolute.Location = new System.Drawing.Point(322, 138);
            this.numVolumeAbsolute.Maximum = new decimal(new int[] { 100000000, 0, 0, 0 });
            this.numVolumeAbsolute.Name = "numVolumeAbsolute";
            this.numVolumeAbsolute.Size = new System.Drawing.Size(98, 23);
            this.numVolumeAbsolute.TabIndex = 3;
            this.numVolumeAbsolute.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numVolumeAbsolute.ThousandsSeparator = true;
            // numVolumeRelative
            // 
            this.numVolumeRelative.DecimalPlaces = 4;
            this.numVolumeRelative.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.numVolumeRelative.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            this.numVolumeRelative.Location = new System.Drawing.Point(322, 175);
            this.numVolumeRelative.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numVolumeRelative.Name = "numVolumeRelative";
            this.numVolumeRelative.Size = new System.Drawing.Size(98, 23);
            this.numVolumeRelative.TabIndex = 4;
            this.numVolumeRelative.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numVolumeRelative.ThousandsSeparator = true;
            // numPrincipalMoment
            // 
            this.numPrincipalMoment.DecimalPlaces = 4;
            this.numPrincipalMoment.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.numPrincipalMoment.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            this.numPrincipalMoment.Location = new System.Drawing.Point(322, 212);
            this.numPrincipalMoment.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numPrincipalMoment.Name = "numPrincipalMoment";
            this.numPrincipalMoment.Size = new System.Drawing.Size(98, 23);
            this.numPrincipalMoment.TabIndex = 5;
            this.numPrincipalMoment.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numPrincipalMoment.ThousandsSeparator = true;
            // numHoleLinear
            // 
            this.numHoleLinear.DecimalPlaces = 3;
            this.numHoleLinear.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.numHoleLinear.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            this.numHoleLinear.Location = new System.Drawing.Point(322, 249);
            this.numHoleLinear.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.numHoleLinear.Name = "numHoleLinear";
            this.numHoleLinear.Size = new System.Drawing.Size(98, 23);
            this.numHoleLinear.TabIndex = 6;
            this.numHoleLinear.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numHoleLinear.ThousandsSeparator = true;
            // numHoleRadius
            // 
            this.numHoleRadius.DecimalPlaces = 3;
            this.numHoleRadius.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.numHoleRadius.Increment = new decimal(new int[] { 1, 0, 0, 131072 });
            this.numHoleRadius.Location = new System.Drawing.Point(322, 286);
            this.numHoleRadius.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.numHoleRadius.Name = "numHoleRadius";
            this.numHoleRadius.Size = new System.Drawing.Size(98, 23);
            this.numHoleRadius.TabIndex = 7;
            this.numHoleRadius.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numHoleRadius.ThousandsSeparator = true;
            // lblUnitAreaAbsolute
            // 
            this.lblUnitAreaAbsolute.AutoSize = true;
            this.lblUnitAreaAbsolute.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblUnitAreaAbsolute.ForeColor = System.Drawing.Color.FromArgb(90, 100, 110);
            this.lblUnitAreaAbsolute.Location = new System.Drawing.Point(434, 31);
            this.lblUnitAreaAbsolute.Name = "lblUnitAreaAbsolute";
            this.lblUnitAreaAbsolute.TabIndex = 8;
            this.lblUnitAreaAbsolute.Text = "mm\u00b2";
            // lblUnitAreaRelative
            // 
            this.lblUnitAreaRelative.AutoSize = true;
            this.lblUnitAreaRelative.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblUnitAreaRelative.ForeColor = System.Drawing.Color.FromArgb(90, 100, 110);
            this.lblUnitAreaRelative.Location = new System.Drawing.Point(434, 68);
            this.lblUnitAreaRelative.Name = "lblUnitAreaRelative";
            this.lblUnitAreaRelative.TabIndex = 9;
            this.lblUnitAreaRelative.Text = "%";
            // lblUnitEdgeLength
            // 
            this.lblUnitEdgeLength.AutoSize = true;
            this.lblUnitEdgeLength.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblUnitEdgeLength.ForeColor = System.Drawing.Color.FromArgb(90, 100, 110);
            this.lblUnitEdgeLength.Location = new System.Drawing.Point(434, 105);
            this.lblUnitEdgeLength.Name = "lblUnitEdgeLength";
            this.lblUnitEdgeLength.TabIndex = 10;
            this.lblUnitEdgeLength.Text = "mm";
            // lblUnitVolumeAbsolute
            // 
            this.lblUnitVolumeAbsolute.AutoSize = true;
            this.lblUnitVolumeAbsolute.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblUnitVolumeAbsolute.ForeColor = System.Drawing.Color.FromArgb(90, 100, 110);
            this.lblUnitVolumeAbsolute.Location = new System.Drawing.Point(434, 142);
            this.lblUnitVolumeAbsolute.Name = "lblUnitVolumeAbsolute";
            this.lblUnitVolumeAbsolute.TabIndex = 11;
            this.lblUnitVolumeAbsolute.Text = "mm\u00b3";
            // lblUnitVolumeRelative
            // 
            this.lblUnitVolumeRelative.AutoSize = true;
            this.lblUnitVolumeRelative.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblUnitVolumeRelative.ForeColor = System.Drawing.Color.FromArgb(90, 100, 110);
            this.lblUnitVolumeRelative.Location = new System.Drawing.Point(434, 179);
            this.lblUnitVolumeRelative.Name = "lblUnitVolumeRelative";
            this.lblUnitVolumeRelative.TabIndex = 12;
            this.lblUnitVolumeRelative.Text = "%";
            // lblUnitPrincipalMoment
            // 
            this.lblUnitPrincipalMoment.AutoSize = true;
            this.lblUnitPrincipalMoment.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblUnitPrincipalMoment.ForeColor = System.Drawing.Color.FromArgb(90, 100, 110);
            this.lblUnitPrincipalMoment.Location = new System.Drawing.Point(434, 216);
            this.lblUnitPrincipalMoment.Name = "lblUnitPrincipalMoment";
            this.lblUnitPrincipalMoment.TabIndex = 13;
            this.lblUnitPrincipalMoment.Text = "%";
            // lblUnitHoleLinear
            // 
            this.lblUnitHoleLinear.AutoSize = true;
            this.lblUnitHoleLinear.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblUnitHoleLinear.ForeColor = System.Drawing.Color.FromArgb(90, 100, 110);
            this.lblUnitHoleLinear.Location = new System.Drawing.Point(434, 253);
            this.lblUnitHoleLinear.Name = "lblUnitHoleLinear";
            this.lblUnitHoleLinear.TabIndex = 14;
            this.lblUnitHoleLinear.Text = "mm";
            // lblUnitHoleRadius
            // 
            this.lblUnitHoleRadius.AutoSize = true;
            this.lblUnitHoleRadius.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblUnitHoleRadius.ForeColor = System.Drawing.Color.FromArgb(90, 100, 110);
            this.lblUnitHoleRadius.Location = new System.Drawing.Point(434, 290);
            this.lblUnitHoleRadius.Name = "lblUnitHoleRadius";
            this.lblUnitHoleRadius.TabIndex = 15;
            this.lblUnitHoleRadius.Text = "mm";
            // 
            // btnRun
            // 
            this.btnRun.BackColor = System.Drawing.Color.FromArgb(220, 235, 252);
            this.btnRun.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(82, 132, 190);
            this.btnRun.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(202, 224, 249);
            this.btnRun.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRun.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnRun.ForeColor = System.Drawing.Color.FromArgb(24, 74, 126);
            this.btnRun.Location = new System.Drawing.Point(286, 438);
            this.btnRun.Name = "btnRun";
            this.btnRun.Size = new System.Drawing.Size(132, 34);
            this.btnRun.TabIndex = 8;
            this.btnRun.Text = "CHAY KIEM TRA";
            this.btnRun.UseVisualStyleBackColor = false;
            this.btnRun.Click += new System.EventHandler(this.btnRun_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.BackColor = System.Drawing.Color.FromArgb(246, 247, 249);
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(197, 204, 213);
            this.btnCancel.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(235, 240, 246);
            this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCancel.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnCancel.ForeColor = System.Drawing.Color.FromArgb(42, 48, 56);
            this.btnCancel.Location = new System.Drawing.Point(428, 438);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(94, 34);
            this.btnCancel.TabIndex = 9;
            this.btnCancel.Text = "HUY";
            this.btnCancel.UseVisualStyleBackColor = false;
            // 
            // SamePartToleranceInputDialog
            // 
            this.AcceptButton = this.btnRun;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(250, 251, 253);
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(540, 486);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnRun);
            this.Controls.Add(this.grpTolerance);
            this.Controls.Add(this.pnlHeader);
            this.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SamePartToleranceInputDialog";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "CHECK SAME PART - DUNG SAI";
            this.pnlHeader.ResumeLayout(false);
            this.pnlHeader.PerformLayout();
            this.grpTolerance.ResumeLayout(false);
            this.grpTolerance.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numAreaAbsolute)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAreaRelative)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numEdgeLength)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numVolumeAbsolute)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numVolumeRelative)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPrincipalMoment)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numHoleLinear)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numHoleRadius)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel pnlHeader;
        private System.Windows.Forms.Label lblSubtitle;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.GroupBox grpTolerance;
        private System.Windows.Forms.Label lblAreaAbsolute;
        private System.Windows.Forms.Label lblAreaRelative;
        private System.Windows.Forms.Label lblEdgeLength;
        private System.Windows.Forms.Label lblVolumeAbsolute;
        private System.Windows.Forms.Label lblVolumeRelative;
        private System.Windows.Forms.Label lblPrincipalMoment;
        private System.Windows.Forms.Label lblHoleLinear;
        private System.Windows.Forms.Label lblHoleRadius;
        private System.Windows.Forms.NumericUpDown numAreaAbsolute;
        private System.Windows.Forms.NumericUpDown numAreaRelative;
        private System.Windows.Forms.NumericUpDown numEdgeLength;
        private System.Windows.Forms.NumericUpDown numVolumeAbsolute;
        private System.Windows.Forms.NumericUpDown numVolumeRelative;
        private System.Windows.Forms.NumericUpDown numPrincipalMoment;
        private System.Windows.Forms.NumericUpDown numHoleLinear;
        private System.Windows.Forms.NumericUpDown numHoleRadius;
        private System.Windows.Forms.Label lblUnitAreaAbsolute;
        private System.Windows.Forms.Label lblUnitAreaRelative;
        private System.Windows.Forms.Label lblUnitEdgeLength;
        private System.Windows.Forms.Label lblUnitVolumeAbsolute;
        private System.Windows.Forms.Label lblUnitVolumeRelative;
        private System.Windows.Forms.Label lblUnitPrincipalMoment;
        private System.Windows.Forms.Label lblUnitHoleLinear;
        private System.Windows.Forms.Label lblUnitHoleRadius;
        private System.Windows.Forms.Button btnRun;
        private System.Windows.Forms.Button btnCancel;
    }
}
