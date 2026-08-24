namespace ADDIN
{
    partial class BomTaskPaneControl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.tabBom = new System.Windows.Forms.TabControl();
            this.tabDrawing = new System.Windows.Forms.TabPage();
            this.tabDrawingPages = new System.Windows.Forms.TabControl();
            this.tabDrawingBom = new System.Windows.Forms.TabPage();
            this.button1 = new System.Windows.Forms.Button();
            this.chkSelectAll = new System.Windows.Forms.CheckBox();
            this.dgvModelBom = new System.Windows.Forms.DataGridView();
            this.Column5 = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.Column1 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column3 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column6 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column4 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Column2 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.button2 = new System.Windows.Forms.Button();
            this.btnOpenAssem = new System.Windows.Forms.Button();
            this.btnCheckBalloon = new System.Windows.Forms.Button();
            this.btnCheckDfTk = new System.Windows.Forms.Button();
            this.btnCheckAll = new System.Windows.Forms.Button();
            this.btnCheckRound = new System.Windows.Forms.Button();
            this.btnCheckSamePart = new System.Windows.Forms.Button();
            this.btnCheckDrawingBom = new System.Windows.Forms.Button();
            this.progressCheck = new System.Windows.Forms.ProgressBar();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.btnClearBom = new System.Windows.Forms.Button();
            this.btnLoadBom = new System.Windows.Forms.Button();
            this.tabComponentDrawing = new System.Windows.Forms.TabPage();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.btnDimKegaki = new System.Windows.Forms.Button();
            this.btnFixScale = new System.Windows.Forms.Button();
            this.btnSplineToArcs = new System.Windows.Forms.Button();
            this.btnDimMatCat = new System.Windows.Forms.Button();
            this.dimvang = new System.Windows.Forms.Button();
            this.grpComponentBom = new System.Windows.Forms.GroupBox();
            this.btnInsertBalloon = new System.Windows.Forms.Button();
            this.cboBalloonProperty = new System.Windows.Forms.ComboBox();
            this.btnDeleteText = new System.Windows.Forms.Button();
            this.btnText = new System.Windows.Forms.Button();
            this.cboSide = new ADDIN.Commands.HistoryTextBox();
            this.btnDeleteNote = new System.Windows.Forms.Button();
            this.btnNote = new System.Windows.Forms.Button();
            this.cboBendLine = new ADDIN.Commands.HistoryTextBox();
            this.grpComponentSize = new System.Windows.Forms.GroupBox();
            this.btnGetWL = new System.Windows.Forms.Button();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.txtLength = new System.Windows.Forms.TextBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.txtWidth = new System.Windows.Forms.TextBox();
            this.btnRotateCcw = new System.Windows.Forms.Button();
            this.btnRotateCw = new System.Windows.Forms.Button();
            this.btnHorizontalAlignment = new System.Windows.Forms.Button();
            this.tabModel = new System.Windows.Forms.TabPage();
            this.tabModelPages = new System.Windows.Forms.TabControl();
            this.tabModelPropsPage = new System.Windows.Forms.TabPage();
            this.panelModelProps = new System.Windows.Forms.Panel();
            this.btnModelUpdateProps = new System.Windows.Forms.Button();
            this.btnModelResetProps = new System.Windows.Forms.Button();
            this.btnModelApplyProps = new System.Windows.Forms.Button();
            this.txtModelFinish = new System.Windows.Forms.TextBox();
            this.lblModelFinish = new System.Windows.Forms.Label();
            this.txtModelQty = new System.Windows.Forms.TextBox();
            this.lblModelQty = new System.Windows.Forms.Label();
            this.txtModelGoban = new System.Windows.Forms.TextBox();
            this.lblModelGoban = new System.Windows.Forms.Label();
            this.txtModelThickness = new System.Windows.Forms.TextBox();
            this.lblModelThickness = new System.Windows.Forms.Label();
            this.txtModelMaterial = new System.Windows.Forms.TextBox();
            this.lblModelMaterial = new System.Windows.Forms.Label();
            this.txtModelName = new System.Windows.Forms.TextBox();
            this.lblModelName = new System.Windows.Forms.Label();
            this.tabModelEditPage = new System.Windows.Forms.TabPage();
            this.panelModelCommands = new System.Windows.Forms.Panel();
            this.btnMakeHole = new System.Windows.Forms.Button();
            this.btnRepairHole = new System.Windows.Forms.Button();
            this.btnPaintHoleSummary = new System.Windows.Forms.Button();
            this.grpMakeHoleOptions = new System.Windows.Forms.GroupBox();
            this.pnlMakeHoleDiagram = new System.Windows.Forms.Panel();
            this.lblMakeHoleDirection = new System.Windows.Forms.Label();
            this.cboMakeHoleDirection = new System.Windows.Forms.ComboBox();
            this.lblMakeHoleEdgeOffset = new System.Windows.Forms.Label();
            this.txtMakeHoleEdgeOffset = new System.Windows.Forms.TextBox();
            this.lblMakeHoleLeftOffset = new System.Windows.Forms.Label();
            this.txtMakeHoleLeftOffset = new System.Windows.Forms.TextBox();
            this.lblMakeHoleRightOffset = new System.Windows.Forms.Label();
            this.txtMakeHoleRightOffset = new System.Windows.Forms.TextBox();
            this.lblMakeHolePitch = new System.Windows.Forms.Label();
            this.txtMakeHolePitch = new System.Windows.Forms.TextBox();
            this.lblRepairHoleType = new System.Windows.Forms.Label();
            this.cboRepairHoleType = new System.Windows.Forms.ComboBox();
            this.lblRepairHoleDiameter = new System.Windows.Forms.Label();
            this.cboRepairHoleDiameter = new System.Windows.Forms.ComboBox();
            this.btnDeleteMakeHoleSize = new System.Windows.Forms.Button();
            this.chkMakeHolePaint = new System.Windows.Forms.CheckBox();
            this.lblMakeHolePaintName = new System.Windows.Forms.Label();
            this.txtMakeHolePaintName = new System.Windows.Forms.TextBox();
            this.btnMakeHoleAccept = new System.Windows.Forms.Button();
            this.btnMakeHoleUpdate = new System.Windows.Forms.Button();
            this.btnMakeHolePattern = new System.Windows.Forms.Button();
            this.btnMakeHoleReset = new System.Windows.Forms.Button();
            this.tabModelMacroPage = new System.Windows.Forms.TabPage();
            this.lblCheckAssemblyHoleResult = new System.Windows.Forms.Label();
            this.btnMirrorPart = new System.Windows.Forms.Button();
            this.btnCheckAssemblyHole = new System.Windows.Forms.Button();
            this.btnCheckKegaki = new System.Windows.Forms.Button();
            this.btnCheckUraOmote = new System.Windows.Forms.Button();
            this.tabBom.SuspendLayout();
            this.tabDrawing.SuspendLayout();
            this.tabDrawingPages.SuspendLayout();
            this.tabDrawingBom.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvModelBom)).BeginInit();
            this.tabComponentDrawing.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.grpComponentBom.SuspendLayout();
            this.grpComponentSize.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.tabModel.SuspendLayout();
            this.tabModelPages.SuspendLayout();
            this.tabModelPropsPage.SuspendLayout();
            this.panelModelProps.SuspendLayout();
            this.tabModelEditPage.SuspendLayout();
            this.panelModelCommands.SuspendLayout();
            this.grpMakeHoleOptions.SuspendLayout();
            this.tabModelMacroPage.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabBom
            // 
            this.tabBom.Controls.Add(this.tabDrawing);
            this.tabBom.Controls.Add(this.tabModel);
            this.tabBom.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabBom.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.tabBom.Location = new System.Drawing.Point(0, 0);
            this.tabBom.Name = "tabBom";
            this.tabBom.SelectedIndex = 0;
            this.tabBom.Size = new System.Drawing.Size(400, 577);
            this.tabBom.TabIndex = 12;
            // 
            // tabDrawing
            // 
            this.tabDrawing.Controls.Add(this.tabDrawingPages);
            this.tabDrawing.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.tabDrawing.Location = new System.Drawing.Point(4, 24);
            this.tabDrawing.Name = "tabDrawing";
            this.tabDrawing.Padding = new System.Windows.Forms.Padding(3);
            this.tabDrawing.Size = new System.Drawing.Size(392, 549);
            this.tabDrawing.TabIndex = 0;
            this.tabDrawing.Text = "Drawing";
            this.tabDrawing.UseVisualStyleBackColor = true;
            // 
            // tabDrawingPages
            // 
            this.tabDrawingPages.Controls.Add(this.tabDrawingBom);
            this.tabDrawingPages.Controls.Add(this.tabComponentDrawing);
            this.tabDrawingPages.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabDrawingPages.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.tabDrawingPages.Location = new System.Drawing.Point(3, 3);
            this.tabDrawingPages.Name = "tabDrawingPages";
            this.tabDrawingPages.SelectedIndex = 0;
            this.tabDrawingPages.Size = new System.Drawing.Size(386, 543);
            this.tabDrawingPages.TabIndex = 14;
            // 
            // tabDrawingBom
            // 
            this.tabDrawingBom.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(250)))), ((int)(((byte)(251)))), ((int)(((byte)(253)))));
            this.tabDrawingBom.Controls.Add(this.button1);
            this.tabDrawingBom.Controls.Add(this.chkSelectAll);
            this.tabDrawingBom.Controls.Add(this.dgvModelBom);
            this.tabDrawingBom.Controls.Add(this.button2);
            this.tabDrawingBom.Controls.Add(this.btnOpenAssem);
            this.tabDrawingBom.Controls.Add(this.btnCheckBalloon);
            this.tabDrawingBom.Controls.Add(this.btnCheckDfTk);
            this.tabDrawingBom.Controls.Add(this.btnCheckAll);
            this.tabDrawingBom.Controls.Add(this.btnCheckRound);
            this.tabDrawingBom.Controls.Add(this.btnCheckSamePart);
            this.tabDrawingBom.Controls.Add(this.btnCheckDrawingBom);
            this.tabDrawingBom.Controls.Add(this.progressCheck);
            this.tabDrawingBom.Controls.Add(this.lblStatus);
            this.tabDrawingBom.Controls.Add(this.lblTitle);
            this.tabDrawingBom.Controls.Add(this.btnClearBom);
            this.tabDrawingBom.Controls.Add(this.btnLoadBom);
            this.tabDrawingBom.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.tabDrawingBom.Location = new System.Drawing.Point(4, 24);
            this.tabDrawingBom.Name = "tabDrawingBom";
            this.tabDrawingBom.Padding = new System.Windows.Forms.Padding(3);
            this.tabDrawingBom.Size = new System.Drawing.Size(378, 515);
            this.tabDrawingBom.TabIndex = 0;
            this.tabDrawingBom.Text = "Drawing BOM";
            // 
            // button1
            // 
            this.button1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.button1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(246)))), ((int)(((byte)(247)))), ((int)(((byte)(249)))));
            this.button1.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(197)))), ((int)(((byte)(204)))), ((int)(((byte)(213)))));
            this.button1.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(240)))), ((int)(((byte)(246)))));
            this.button1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button1.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.button1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.button1.Location = new System.Drawing.Point(249, 471);
            this.button1.Name = "button1";
            this.button1.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.button1.Size = new System.Drawing.Size(104, 32);
            this.button1.TabIndex = 13;
            this.button1.Text = "CANCEL";
            this.button1.UseVisualStyleBackColor = true;
            // 
            // chkSelectAll
            // 
            this.chkSelectAll.AutoSize = true;
            this.chkSelectAll.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.chkSelectAll.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.chkSelectAll.Location = new System.Drawing.Point(12, 178);
            this.chkSelectAll.Name = "chkSelectAll";
            this.chkSelectAll.Size = new System.Drawing.Size(85, 19);
            this.chkSelectAll.TabIndex = 11;
            this.chkSelectAll.Text = "Select All";
            this.chkSelectAll.UseVisualStyleBackColor = true;
            // 
            // dgvModelBom
            // 
            this.dgvModelBom.AllowUserToOrderColumns = true;
            this.dgvModelBom.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dgvModelBom.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvModelBom.AutoSizeRowsMode = System.Windows.Forms.DataGridViewAutoSizeRowsMode.AllCells;
            this.dgvModelBom.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvModelBom.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Column5,
            this.Column1,
            this.Column3,
            this.Column6,
            this.Column4,
            this.Column2});
            this.dgvModelBom.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.dgvModelBom.Location = new System.Drawing.Point(12, 227);
            this.dgvModelBom.Name = "dgvModelBom";
            this.dgvModelBom.Size = new System.Drawing.Size(354, 234);
            this.dgvModelBom.TabIndex = 3;
            // 
            // Column5
            // 
            this.Column5.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.Column5.HeaderText = "chon";
            this.Column5.MinimumWidth = 45;
            this.Column5.Name = "Column5";
            this.Column5.Width = 45;
            // 
            // Column1
            // 
            this.Column1.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.Column1.FillWeight = 120F;
            this.Column1.HeaderText = "部品番号";
            this.Column1.Name = "Column1";
            // 
            // Column3
            // 
            this.Column3.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.Column3.FillWeight = 80F;
            this.Column3.HeaderText = "材質";
            this.Column3.Name = "Column3";
            // 
            // Column6
            // 
            this.Column6.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.Column6.FillWeight = 60F;
            this.Column6.HeaderText = "板厚";
            this.Column6.Name = "Column6";
            // 
            // Column4
            // 
            this.Column4.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.Column4.FillWeight = 60F;
            this.Column4.HeaderText = "数量";
            this.Column4.Name = "Column4";
            // 
            // Column2
            // 
            this.Column2.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.Column2.FillWeight = 180F;
            this.Column2.HeaderText = "部品ファイル名";
            this.Column2.Name = "Column2";
            // 
            // button2
            // 
            this.button2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(235)))), ((int)(((byte)(252)))));
            this.button2.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.button2.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(82)))), ((int)(((byte)(132)))), ((int)(((byte)(190)))));
            this.button2.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(202)))), ((int)(((byte)(224)))), ((int)(((byte)(249)))));
            this.button2.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button2.Font = new System.Drawing.Font("Meiryo UI", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.button2.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(24)))), ((int)(((byte)(74)))), ((int)(((byte)(126)))));
            this.button2.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.button2.Location = new System.Drawing.Point(13, 128);
            this.button2.Name = "button2";
            this.button2.Padding = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.button2.Size = new System.Drawing.Size(84, 38);
            this.button2.TabIndex = 11;
            this.button2.Text = "XEP\r\nUNIT";
            this.button2.UseVisualStyleBackColor = false;
            // 
            // btnOpenAssem
            // 
            this.btnOpenAssem.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(235)))), ((int)(((byte)(252)))));
            this.btnOpenAssem.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnOpenAssem.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(82)))), ((int)(((byte)(132)))), ((int)(((byte)(190)))));
            this.btnOpenAssem.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(202)))), ((int)(((byte)(224)))), ((int)(((byte)(249)))));
            this.btnOpenAssem.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenAssem.Font = new System.Drawing.Font("Meiryo UI", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnOpenAssem.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(24)))), ((int)(((byte)(74)))), ((int)(((byte)(126)))));
            this.btnOpenAssem.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnOpenAssem.Location = new System.Drawing.Point(103, 128);
            this.btnOpenAssem.Name = "btnOpenAssem";
            this.btnOpenAssem.Padding = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.btnOpenAssem.Size = new System.Drawing.Size(84, 38);
            this.btnOpenAssem.TabIndex = 12;
            this.btnOpenAssem.Text = "OPEN\r\nASSEM";
            this.btnOpenAssem.UseVisualStyleBackColor = false;
            // 
            // btnCheckBalloon
            // 
            this.btnCheckBalloon.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(235)))), ((int)(((byte)(252)))));
            this.btnCheckBalloon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnCheckBalloon.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(82)))), ((int)(((byte)(132)))), ((int)(((byte)(190)))));
            this.btnCheckBalloon.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(202)))), ((int)(((byte)(224)))), ((int)(((byte)(249)))));
            this.btnCheckBalloon.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckBalloon.Font = new System.Drawing.Font("Meiryo UI", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnCheckBalloon.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(24)))), ((int)(((byte)(74)))), ((int)(((byte)(126)))));
            this.btnCheckBalloon.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnCheckBalloon.Location = new System.Drawing.Point(195, 128);
            this.btnCheckBalloon.Name = "btnCheckBalloon";
            this.btnCheckBalloon.Padding = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.btnCheckBalloon.Size = new System.Drawing.Size(84, 38);
            this.btnCheckBalloon.TabIndex = 15;
            this.btnCheckBalloon.Text = "CHECK\r\nBALLOON";
            this.btnCheckBalloon.UseVisualStyleBackColor = false;
            // 
            // btnCheckDfTk
            // 
            this.btnCheckDfTk.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(207)))), ((int)(((byte)(244)))));
            this.btnCheckDfTk.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnCheckDfTk.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(171)))), ((int)(((byte)(96)))), ((int)(((byte)(194)))));
            this.btnCheckDfTk.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(225)))), ((int)(((byte)(190)))), ((int)(((byte)(238)))));
            this.btnCheckDfTk.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckDfTk.Font = new System.Drawing.Font("Meiryo UI", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnCheckDfTk.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(90)))), ((int)(((byte)(34)))), ((int)(((byte)(118)))));
            this.btnCheckDfTk.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnCheckDfTk.Location = new System.Drawing.Point(12, 84);
            this.btnCheckDfTk.Name = "btnCheckDfTk";
            this.btnCheckDfTk.Padding = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.btnCheckDfTk.Size = new System.Drawing.Size(80, 38);
            this.btnCheckDfTk.TabIndex = 10;
            this.btnCheckDfTk.Text = "①CHECK\r\nDF/TK";
            this.btnCheckDfTk.UseVisualStyleBackColor = false;
            // 
            // btnCheckAll
            // 
            this.btnCheckAll.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(207)))), ((int)(((byte)(244)))));
            this.btnCheckAll.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnCheckAll.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(171)))), ((int)(((byte)(96)))), ((int)(((byte)(194)))));
            this.btnCheckAll.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(225)))), ((int)(((byte)(190)))), ((int)(((byte)(238)))));
            this.btnCheckAll.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckAll.Font = new System.Drawing.Font("Meiryo UI", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnCheckAll.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(90)))), ((int)(((byte)(34)))), ((int)(((byte)(118)))));
            this.btnCheckAll.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnCheckAll.Location = new System.Drawing.Point(98, 84);
            this.btnCheckAll.Name = "btnCheckAll";
            this.btnCheckAll.Padding = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.btnCheckAll.Size = new System.Drawing.Size(96, 38);
            this.btnCheckAll.TabIndex = 16;
            this.btnCheckAll.Text = "CHECK ウラ表\r\nKEGAKI";
            this.btnCheckAll.UseVisualStyleBackColor = false;
            // 
            // btnCheckRound
            // 
            this.btnCheckRound.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(207)))), ((int)(((byte)(244)))));
            this.btnCheckRound.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnCheckRound.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(171)))), ((int)(((byte)(96)))), ((int)(((byte)(194)))));
            this.btnCheckRound.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(225)))), ((int)(((byte)(190)))), ((int)(((byte)(238)))));
            this.btnCheckRound.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckRound.Font = new System.Drawing.Font("Meiryo UI", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnCheckRound.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(90)))), ((int)(((byte)(34)))), ((int)(((byte)(118)))));
            this.btnCheckRound.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnCheckRound.Location = new System.Drawing.Point(200, 84);
            this.btnCheckRound.Name = "btnCheckRound";
            this.btnCheckRound.Padding = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.btnCheckRound.Size = new System.Drawing.Size(80, 38);
            this.btnCheckRound.TabIndex = 17;
            this.btnCheckRound.Text = "CHECK\r\nROUND";
            this.btnCheckRound.UseVisualStyleBackColor = false;
            // 
            // btnCheckSamePart
            // 
            this.btnCheckSamePart.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(207)))), ((int)(((byte)(244)))));
            this.btnCheckSamePart.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnCheckSamePart.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(171)))), ((int)(((byte)(96)))), ((int)(((byte)(194)))));
            this.btnCheckSamePart.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(225)))), ((int)(((byte)(190)))), ((int)(((byte)(238)))));
            this.btnCheckSamePart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckSamePart.Font = new System.Drawing.Font("Meiryo UI", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnCheckSamePart.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(90)))), ((int)(((byte)(34)))), ((int)(((byte)(118)))));
            this.btnCheckSamePart.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnCheckSamePart.Location = new System.Drawing.Point(286, 84);
            this.btnCheckSamePart.Name = "btnCheckSamePart";
            this.btnCheckSamePart.Padding = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.btnCheckSamePart.Size = new System.Drawing.Size(80, 38);
            this.btnCheckSamePart.TabIndex = 18;
            this.btnCheckSamePart.Text = "CHECK SAME\r\nPART";
            this.btnCheckSamePart.UseVisualStyleBackColor = false;
            // 
            // btnCheckDrawingBom
            // 
            this.btnCheckDrawingBom.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(207)))), ((int)(((byte)(244)))));
            this.btnCheckDrawingBom.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnCheckDrawingBom.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(171)))), ((int)(((byte)(96)))), ((int)(((byte)(194)))));
            this.btnCheckDrawingBom.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(225)))), ((int)(((byte)(190)))), ((int)(((byte)(238)))));
            this.btnCheckDrawingBom.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckDrawingBom.Font = new System.Drawing.Font("Meiryo UI", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnCheckDrawingBom.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(90)))), ((int)(((byte)(34)))), ((int)(((byte)(118)))));
            this.btnCheckDrawingBom.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnCheckDrawingBom.Location = new System.Drawing.Point(286, 128);
            this.btnCheckDrawingBom.Name = "btnCheckDrawingBom";
            this.btnCheckDrawingBom.Padding = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.btnCheckDrawingBom.Size = new System.Drawing.Size(80, 38);
            this.btnCheckDrawingBom.TabIndex = 19;
            this.btnCheckDrawingBom.Text = "CHECK\r\nDRAWING";
            this.btnCheckDrawingBom.UseVisualStyleBackColor = false;
            // 
            // progressCheck
            // 
            this.progressCheck.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressCheck.Location = new System.Drawing.Point(12, 203);
            this.progressCheck.Name = "progressCheck";
            this.progressCheck.Size = new System.Drawing.Size(354, 16);
            this.progressCheck.TabIndex = 12;
            this.progressCheck.Visible = false;
            // 
            // lblStatus
            // 
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.lblStatus.Location = new System.Drawing.Point(12, 42);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(354, 34);
            this.lblStatus.TabIndex = 8;
            this.lblStatus.Text = "Dang cho ket noi...";
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Meiryo UI", 9.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblTitle.Location = new System.Drawing.Point(12, 16);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(116, 17);
            this.lblTitle.TabIndex = 7;
            this.lblTitle.Text = "DRAWING BOM";
            // 
            // btnClearBom
            // 
            this.btnClearBom.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnClearBom.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(246)))), ((int)(((byte)(246)))));
            this.btnClearBom.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(214)))), ((int)(((byte)(158)))), ((int)(((byte)(158)))));
            this.btnClearBom.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(235)))), ((int)(((byte)(235)))));
            this.btnClearBom.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnClearBom.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnClearBom.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.btnClearBom.Location = new System.Drawing.Point(137, 471);
            this.btnClearBom.Name = "btnClearBom";
            this.btnClearBom.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.btnClearBom.Size = new System.Drawing.Size(104, 32);
            this.btnClearBom.TabIndex = 5;
            this.btnClearBom.Text = "XOA BANG";
            this.btnClearBom.UseVisualStyleBackColor = true;
            // 
            // btnLoadBom
            // 
            this.btnLoadBom.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnLoadBom.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(246)))), ((int)(((byte)(249)))), ((int)(((byte)(252)))));
            this.btnLoadBom.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(186)))), ((int)(((byte)(200)))), ((int)(((byte)(216)))));
            this.btnLoadBom.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(234)))), ((int)(((byte)(242)))), ((int)(((byte)(250)))));
            this.btnLoadBom.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLoadBom.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnLoadBom.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.btnLoadBom.Location = new System.Drawing.Point(25, 471);
            this.btnLoadBom.Name = "btnLoadBom";
            this.btnLoadBom.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.btnLoadBom.Size = new System.Drawing.Size(104, 32);
            this.btnLoadBom.TabIndex = 4;
            this.btnLoadBom.Text = "CAP NHAT";
            this.btnLoadBom.UseVisualStyleBackColor = true;
            // 
            // tabComponentDrawing
            // 
            this.tabComponentDrawing.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(250)))), ((int)(((byte)(251)))), ((int)(((byte)(253)))));
            this.tabComponentDrawing.Controls.Add(this.groupBox3);
            this.tabComponentDrawing.Controls.Add(this.grpComponentBom);
            this.tabComponentDrawing.Controls.Add(this.grpComponentSize);
            this.tabComponentDrawing.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.tabComponentDrawing.Location = new System.Drawing.Point(4, 24);
            this.tabComponentDrawing.Name = "tabComponentDrawing";
            this.tabComponentDrawing.Padding = new System.Windows.Forms.Padding(3);
            this.tabComponentDrawing.Size = new System.Drawing.Size(378, 515);
            this.tabComponentDrawing.TabIndex = 1;
            this.tabComponentDrawing.Text = "Component Drawing";
            // 
            // groupBox3
            // 
            this.groupBox3.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox3.Controls.Add(this.btnDimKegaki);
            this.groupBox3.Controls.Add(this.btnFixScale);
            this.groupBox3.Controls.Add(this.btnSplineToArcs);
            this.groupBox3.Controls.Add(this.btnDimMatCat);
            this.groupBox3.Controls.Add(this.dimvang);
            this.groupBox3.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.groupBox3.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.groupBox3.Location = new System.Drawing.Point(6, 341);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(366, 180);
            this.groupBox3.TabIndex = 2;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Macro";
            // 
            // btnDimKegaki
            // 
            this.btnDimKegaki.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(219)))), ((int)(((byte)(228)))), ((int)(((byte)(255)))));
            this.btnDimKegaki.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(92)))), ((int)(((byte)(119)))), ((int)(((byte)(202)))));
            this.btnDimKegaki.FlatAppearance.MouseOverBackColor = System.Drawing.Color.WhiteSmoke;
            this.btnDimKegaki.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDimKegaki.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnDimKegaki.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(36)))), ((int)(((byte)(66)))), ((int)(((byte)(148)))));
            this.btnDimKegaki.Image = global::ADDIN.Properties.Resources.DimKegaki;
            this.btnDimKegaki.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnDimKegaki.Location = new System.Drawing.Point(50, 78);
            this.btnDimKegaki.Name = "btnDimKegaki";
            this.btnDimKegaki.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
            this.btnDimKegaki.Size = new System.Drawing.Size(126, 44);
            this.btnDimKegaki.TabIndex = 5;
            this.btnDimKegaki.Text = "Dim\r\nkegaki";
            this.btnDimKegaki.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.btnDimKegaki.UseVisualStyleBackColor = false;
            // 
            // btnFixScale
            // 
            this.btnFixScale.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(215)))), ((int)(((byte)(199)))));
            this.btnFixScale.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(204)))), ((int)(((byte)(103)))), ((int)(((byte)(70)))));
            this.btnFixScale.FlatAppearance.MouseOverBackColor = System.Drawing.Color.WhiteSmoke;
            this.btnFixScale.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnFixScale.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnFixScale.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(139)))), ((int)(((byte)(55)))), ((int)(((byte)(30)))));
            this.btnFixScale.Image = global::ADDIN.Properties.Resources.FixScale;
            this.btnFixScale.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnFixScale.Location = new System.Drawing.Point(186, 78);
            this.btnFixScale.Name = "btnFixScale";
            this.btnFixScale.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
            this.btnFixScale.Size = new System.Drawing.Size(126, 44);
            this.btnFixScale.TabIndex = 4;
            this.btnFixScale.Text = "Fix ti le";
            this.btnFixScale.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.btnFixScale.UseVisualStyleBackColor = false;
            // 
            // btnSplineToArcs
            // 
            this.btnSplineToArcs.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(225)))), ((int)(((byte)(238)))), ((int)(((byte)(255)))));
            this.btnSplineToArcs.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(84)))), ((int)(((byte)(132)))), ((int)(((byte)(190)))));
            this.btnSplineToArcs.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(207)))), ((int)(((byte)(227)))), ((int)(((byte)(252)))));
            this.btnSplineToArcs.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSplineToArcs.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnSplineToArcs.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(73)))), ((int)(((byte)(126)))));
            this.btnSplineToArcs.Location = new System.Drawing.Point(50, 132);
            this.btnSplineToArcs.Name = "btnSplineToArcs";
            this.btnSplineToArcs.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
            this.btnSplineToArcs.Size = new System.Drawing.Size(126, 44);
            this.btnSplineToArcs.TabIndex = 7;
            this.btnSplineToArcs.Text = "Spline\r\n→ cung R";
            this.btnSplineToArcs.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.btnSplineToArcs.UseVisualStyleBackColor = false;
            // 
            // btnDimMatCat
            // 
            this.btnDimMatCat.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(235)))), ((int)(((byte)(158)))));
            this.btnDimMatCat.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(205)))), ((int)(((byte)(154)))), ((int)(((byte)(28)))));
            this.btnDimMatCat.FlatAppearance.MouseOverBackColor = System.Drawing.Color.WhiteSmoke;
            this.btnDimMatCat.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDimMatCat.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnDimMatCat.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(118)))), ((int)(((byte)(78)))), ((int)(((byte)(0)))));
            this.btnDimMatCat.Image = global::ADDIN.Properties.Resources.DimMatCat;
            this.btnDimMatCat.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnDimMatCat.Location = new System.Drawing.Point(186, 24);
            this.btnDimMatCat.Name = "btnDimMatCat";
            this.btnDimMatCat.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
            this.btnDimMatCat.Size = new System.Drawing.Size(126, 44);
            this.btnDimMatCat.TabIndex = 3;
            this.btnDimMatCat.Text = "Dim\r\nmat cat";
            this.btnDimMatCat.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.btnDimMatCat.UseVisualStyleBackColor = false;
            // 
            // dimvang
            // 
            this.dimvang.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(201)))), ((int)(((byte)(241)))), ((int)(((byte)(211)))));
            this.dimvang.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(68)))), ((int)(((byte)(154)))), ((int)(((byte)(88)))));
            this.dimvang.FlatAppearance.MouseOverBackColor = System.Drawing.Color.WhiteSmoke;
            this.dimvang.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.dimvang.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.dimvang.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(20)))), ((int)(((byte)(102)))), ((int)(((byte)(44)))));
            this.dimvang.Image = global::ADDIN.Properties.Resources.DimVang;
            this.dimvang.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.dimvang.Location = new System.Drawing.Point(50, 24);
            this.dimvang.Name = "dimvang";
            this.dimvang.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
            this.dimvang.Size = new System.Drawing.Size(126, 44);
            this.dimvang.TabIndex = 3;
            this.dimvang.Text = "Xoa DIM\r\nmau vang";
            this.dimvang.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.dimvang.UseVisualStyleBackColor = false;
            // 
            // grpComponentBom
            // 
            this.grpComponentBom.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpComponentBom.Controls.Add(this.btnInsertBalloon);
            this.grpComponentBom.Controls.Add(this.cboBalloonProperty);
            this.grpComponentBom.Controls.Add(this.btnDeleteText);
            this.grpComponentBom.Controls.Add(this.btnText);
            this.grpComponentBom.Controls.Add(this.cboSide);
            this.grpComponentBom.Controls.Add(this.btnDeleteNote);
            this.grpComponentBom.Controls.Add(this.btnNote);
            this.grpComponentBom.Controls.Add(this.cboBendLine);
            this.grpComponentBom.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.grpComponentBom.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.grpComponentBom.Location = new System.Drawing.Point(6, 196);
            this.grpComponentBom.Name = "grpComponentBom";
            this.grpComponentBom.Size = new System.Drawing.Size(366, 139);
            this.grpComponentBom.TabIndex = 1;
            this.grpComponentBom.TabStop = false;
            this.grpComponentBom.Text = "Text";
            // 
            // btnInsertBalloon
            // 
            this.btnInsertBalloon.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnInsertBalloon.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(246)))), ((int)(((byte)(249)))), ((int)(((byte)(252)))));
            this.btnInsertBalloon.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(186)))), ((int)(((byte)(200)))), ((int)(((byte)(216)))));
            this.btnInsertBalloon.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(234)))), ((int)(((byte)(242)))), ((int)(((byte)(250)))));
            this.btnInsertBalloon.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnInsertBalloon.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnInsertBalloon.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(72)))), ((int)(((byte)(112)))));
            this.btnInsertBalloon.Location = new System.Drawing.Point(288, 93);
            this.btnInsertBalloon.Name = "btnInsertBalloon";
            this.btnInsertBalloon.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.btnInsertBalloon.Size = new System.Drawing.Size(68, 28);
            this.btnInsertBalloon.TabIndex = 8;
            this.btnInsertBalloon.Text = "Balloon";
            this.btnInsertBalloon.UseVisualStyleBackColor = true;
            // 
            // cboBalloonProperty
            // 
            this.cboBalloonProperty.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cboBalloonProperty.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboBalloonProperty.FormattingEnabled = true;
            this.cboBalloonProperty.Items.AddRange(new object[] {
            "部品番号",
            "合番"});
            this.cboBalloonProperty.Location = new System.Drawing.Point(12, 96);
            this.cboBalloonProperty.Name = "cboBalloonProperty";
            this.cboBalloonProperty.Size = new System.Drawing.Size(232, 23);
            this.cboBalloonProperty.TabIndex = 6;
            // 
            // btnDeleteText
            // 
            this.btnDeleteText.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnDeleteText.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(252)))), ((int)(((byte)(249)))));
            this.btnDeleteText.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(180)))), ((int)(((byte)(207)))), ((int)(((byte)(188)))));
            this.btnDeleteText.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeleteText.Location = new System.Drawing.Point(254, 58);
            this.btnDeleteText.Name = "btnDeleteText";
            this.btnDeleteText.Size = new System.Drawing.Size(28, 28);
            this.btnDeleteText.TabIndex = 9;
            this.btnDeleteText.UseVisualStyleBackColor = true;
            // 
            // btnText
            // 
            this.btnText.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnText.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(246)))), ((int)(((byte)(249)))), ((int)(((byte)(252)))));
            this.btnText.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(186)))), ((int)(((byte)(200)))), ((int)(((byte)(216)))));
            this.btnText.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(234)))), ((int)(((byte)(242)))), ((int)(((byte)(250)))));
            this.btnText.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnText.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnText.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(72)))), ((int)(((byte)(112)))));
            this.btnText.Location = new System.Drawing.Point(288, 58);
            this.btnText.Name = "btnText";
            this.btnText.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.btnText.Size = new System.Drawing.Size(68, 28);
            this.btnText.TabIndex = 5;
            this.btnText.Text = "Text";
            this.btnText.UseVisualStyleBackColor = true;
            // 
            // cboSide
            // 
            this.cboSide.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cboSide.BackColor = System.Drawing.SystemColors.Window;
            this.cboSide.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.cboSide.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.cboSide.Location = new System.Drawing.Point(12, 61);
            this.cboSide.MinimumSize = new System.Drawing.Size(40, 23);
            this.cboSide.Name = "cboSide";
            this.cboSide.Padding = new System.Windows.Forms.Padding(3, 3, 0, 2);
            this.cboSide.Size = new System.Drawing.Size(232, 23);
            this.cboSide.TabIndex = 4;
            // 
            // btnDeleteNote
            // 
            this.btnDeleteNote.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnDeleteNote.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(252)))), ((int)(((byte)(249)))));
            this.btnDeleteNote.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(180)))), ((int)(((byte)(207)))), ((int)(((byte)(188)))));
            this.btnDeleteNote.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeleteNote.Location = new System.Drawing.Point(254, 23);
            this.btnDeleteNote.Name = "btnDeleteNote";
            this.btnDeleteNote.Size = new System.Drawing.Size(28, 28);
            this.btnDeleteNote.TabIndex = 10;
            this.btnDeleteNote.UseVisualStyleBackColor = true;
            // 
            // btnNote
            // 
            this.btnNote.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnNote.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(246)))), ((int)(((byte)(249)))), ((int)(((byte)(252)))));
            this.btnNote.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(186)))), ((int)(((byte)(200)))), ((int)(((byte)(216)))));
            this.btnNote.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(234)))), ((int)(((byte)(242)))), ((int)(((byte)(250)))));
            this.btnNote.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNote.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnNote.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(72)))), ((int)(((byte)(112)))));
            this.btnNote.Location = new System.Drawing.Point(288, 23);
            this.btnNote.Name = "btnNote";
            this.btnNote.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.btnNote.Size = new System.Drawing.Size(68, 28);
            this.btnNote.TabIndex = 3;
            this.btnNote.Text = "Note";
            this.btnNote.UseVisualStyleBackColor = true;
            // 
            // cboBendLine
            // 
            this.cboBendLine.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cboBendLine.BackColor = System.Drawing.SystemColors.Window;
            this.cboBendLine.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.cboBendLine.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.cboBendLine.Location = new System.Drawing.Point(12, 26);
            this.cboBendLine.MinimumSize = new System.Drawing.Size(40, 23);
            this.cboBendLine.Name = "cboBendLine";
            this.cboBendLine.Padding = new System.Windows.Forms.Padding(3, 3, 0, 2);
            this.cboBendLine.Size = new System.Drawing.Size(232, 23);
            this.cboBendLine.TabIndex = 2;
            // 
            // grpComponentSize
            // 
            this.grpComponentSize.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpComponentSize.Controls.Add(this.btnGetWL);
            this.grpComponentSize.Controls.Add(this.groupBox2);
            this.grpComponentSize.Controls.Add(this.groupBox1);
            this.grpComponentSize.Controls.Add(this.btnRotateCcw);
            this.grpComponentSize.Controls.Add(this.btnRotateCw);
            this.grpComponentSize.Controls.Add(this.btnHorizontalAlignment);
            this.grpComponentSize.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.grpComponentSize.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.grpComponentSize.Location = new System.Drawing.Point(6, 6);
            this.grpComponentSize.Name = "grpComponentSize";
            this.grpComponentSize.Size = new System.Drawing.Size(366, 190);
            this.grpComponentSize.TabIndex = 0;
            this.grpComponentSize.TabStop = false;
            this.grpComponentSize.Text = "View size";
            // 
            // btnGetWL
            // 
            this.btnGetWL.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(244)))), ((int)(((byte)(248)))), ((int)(((byte)(253)))));
            this.btnGetWL.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(173)))), ((int)(((byte)(193)))), ((int)(((byte)(216)))));
            this.btnGetWL.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(241)))), ((int)(((byte)(252)))));
            this.btnGetWL.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnGetWL.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnGetWL.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(65)))), ((int)(((byte)(105)))));
            this.btnGetWL.Location = new System.Drawing.Point(282, 19);
            this.btnGetWL.Name = "btnGetWL";
            this.btnGetWL.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.btnGetWL.Size = new System.Drawing.Size(72, 88);
            this.btnGetWL.TabIndex = 9;
            this.btnGetWL.Text = "Lay W,L";
            this.btnGetWL.UseVisualStyleBackColor = true;
            this.btnGetWL.Click += new System.EventHandler(this.btnGetWL_Click_1);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.txtLength);
            this.groupBox2.Font = new System.Drawing.Font("Meiryo UI", 8.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.groupBox2.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.groupBox2.Location = new System.Drawing.Point(13, 65);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(259, 42);
            this.groupBox2.TabIndex = 8;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Length";
            // 
            // txtLength
            // 
            this.txtLength.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtLength.Location = new System.Drawing.Point(9, 17);
            this.txtLength.Name = "txtLength";
            this.txtLength.ReadOnly = true;
            this.txtLength.Size = new System.Drawing.Size(241, 22);
            this.txtLength.TabIndex = 0;
            this.txtLength.TabStop = false;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.txtWidth);
            this.groupBox1.Font = new System.Drawing.Font("Meiryo UI", 8.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.groupBox1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.groupBox1.Location = new System.Drawing.Point(13, 19);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(259, 42);
            this.groupBox1.TabIndex = 7;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Width";
            // 
            // txtWidth
            // 
            this.txtWidth.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtWidth.Location = new System.Drawing.Point(9, 17);
            this.txtWidth.Name = "txtWidth";
            this.txtWidth.ReadOnly = true;
            this.txtWidth.Size = new System.Drawing.Size(241, 22);
            this.txtWidth.TabIndex = 0;
            this.txtWidth.TabStop = false;
            // 
            // btnRotateCcw
            // 
            this.btnRotateCcw.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(246)))), ((int)(((byte)(247)))), ((int)(((byte)(249)))));
            this.btnRotateCcw.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(197)))), ((int)(((byte)(204)))), ((int)(((byte)(213)))));
            this.btnRotateCcw.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(240)))), ((int)(((byte)(246)))));
            this.btnRotateCcw.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRotateCcw.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnRotateCcw.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(42)))), ((int)(((byte)(53)))), ((int)(((byte)(66)))));
            this.btnRotateCcw.Location = new System.Drawing.Point(183, 153);
            this.btnRotateCcw.Name = "btnRotateCcw";
            this.btnRotateCcw.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.btnRotateCcw.Size = new System.Drawing.Size(160, 30);
            this.btnRotateCcw.TabIndex = 6;
            this.btnRotateCcw.TabStop = false;
            this.btnRotateCcw.Text = "Ro90-c";
            this.btnRotateCcw.UseCompatibleTextRendering = true;
            this.btnRotateCcw.UseVisualStyleBackColor = true;
            // 
            // btnRotateCw
            // 
            this.btnRotateCw.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(246)))), ((int)(((byte)(247)))), ((int)(((byte)(249)))));
            this.btnRotateCw.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(197)))), ((int)(((byte)(204)))), ((int)(((byte)(213)))));
            this.btnRotateCw.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(240)))), ((int)(((byte)(246)))));
            this.btnRotateCw.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRotateCw.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnRotateCw.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(42)))), ((int)(((byte)(53)))), ((int)(((byte)(66)))));
            this.btnRotateCw.Location = new System.Drawing.Point(13, 153);
            this.btnRotateCw.Name = "btnRotateCw";
            this.btnRotateCw.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.btnRotateCw.Size = new System.Drawing.Size(160, 30);
            this.btnRotateCw.TabIndex = 5;
            this.btnRotateCw.TabStop = false;
            this.btnRotateCw.Text = "Ro90+c";
            this.btnRotateCw.UseVisualStyleBackColor = true;
            // 
            // btnHorizontalAlignment
            // 
            this.btnHorizontalAlignment.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(246)))), ((int)(((byte)(247)))), ((int)(((byte)(249)))));
            this.btnHorizontalAlignment.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(197)))), ((int)(((byte)(204)))), ((int)(((byte)(213)))));
            this.btnHorizontalAlignment.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(240)))), ((int)(((byte)(246)))));
            this.btnHorizontalAlignment.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnHorizontalAlignment.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnHorizontalAlignment.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(42)))), ((int)(((byte)(53)))), ((int)(((byte)(66)))));
            this.btnHorizontalAlignment.Location = new System.Drawing.Point(13, 112);
            this.btnHorizontalAlignment.Name = "btnHorizontalAlignment";
            this.btnHorizontalAlignment.Padding = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.btnHorizontalAlignment.Size = new System.Drawing.Size(330, 30);
            this.btnHorizontalAlignment.TabIndex = 4;
            this.btnHorizontalAlignment.TabStop = false;
            this.btnHorizontalAlignment.Text = "HorizontalAlignment";
            this.btnHorizontalAlignment.UseVisualStyleBackColor = true;
            // 
            // tabModel
            // 
            this.tabModel.Controls.Add(this.tabModelPages);
            this.tabModel.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.tabModel.Location = new System.Drawing.Point(4, 24);
            this.tabModel.Name = "tabModel";
            this.tabModel.Padding = new System.Windows.Forms.Padding(3);
            this.tabModel.Size = new System.Drawing.Size(392, 549);
            this.tabModel.TabIndex = 1;
            this.tabModel.Text = "Model";
            this.tabModel.UseVisualStyleBackColor = true;
            // 
            // tabModelPages
            // 
            this.tabModelPages.Controls.Add(this.tabModelPropsPage);
            this.tabModelPages.Controls.Add(this.tabModelEditPage);
            this.tabModelPages.Controls.Add(this.tabModelMacroPage);
            this.tabModelPages.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabModelPages.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.tabModelPages.Location = new System.Drawing.Point(3, 3);
            this.tabModelPages.Name = "tabModelPages";
            this.tabModelPages.SelectedIndex = 0;
            this.tabModelPages.Size = new System.Drawing.Size(386, 543);
            this.tabModelPages.TabIndex = 2;
            // 
            // tabModelPropsPage
            // 
            this.tabModelPropsPage.Controls.Add(this.panelModelProps);
            this.tabModelPropsPage.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.tabModelPropsPage.Location = new System.Drawing.Point(4, 24);
            this.tabModelPropsPage.Name = "tabModelPropsPage";
            this.tabModelPropsPage.Padding = new System.Windows.Forms.Padding(3);
            this.tabModelPropsPage.Size = new System.Drawing.Size(378, 515);
            this.tabModelPropsPage.TabIndex = 0;
            this.tabModelPropsPage.Text = "Props";
            this.tabModelPropsPage.UseVisualStyleBackColor = true;
            // 
            // panelModelProps
            // 
            this.panelModelProps.AutoScroll = true;
            this.panelModelProps.Controls.Add(this.btnModelUpdateProps);
            this.panelModelProps.Controls.Add(this.btnModelResetProps);
            this.panelModelProps.Controls.Add(this.btnModelApplyProps);
            this.panelModelProps.Controls.Add(this.txtModelFinish);
            this.panelModelProps.Controls.Add(this.lblModelFinish);
            this.panelModelProps.Controls.Add(this.txtModelQty);
            this.panelModelProps.Controls.Add(this.lblModelQty);
            this.panelModelProps.Controls.Add(this.txtModelGoban);
            this.panelModelProps.Controls.Add(this.lblModelGoban);
            this.panelModelProps.Controls.Add(this.txtModelThickness);
            this.panelModelProps.Controls.Add(this.lblModelThickness);
            this.panelModelProps.Controls.Add(this.txtModelMaterial);
            this.panelModelProps.Controls.Add(this.lblModelMaterial);
            this.panelModelProps.Controls.Add(this.txtModelName);
            this.panelModelProps.Controls.Add(this.lblModelName);
            this.panelModelProps.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelModelProps.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.panelModelProps.Location = new System.Drawing.Point(3, 3);
            this.panelModelProps.Name = "panelModelProps";
            this.panelModelProps.Size = new System.Drawing.Size(372, 509);
            this.panelModelProps.TabIndex = 1;
            // 
            // btnModelUpdateProps
            // 
            this.btnModelUpdateProps.BackColor = System.Drawing.Color.Transparent;
            this.btnModelUpdateProps.FlatAppearance.BorderSize = 0;
            this.btnModelUpdateProps.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnModelUpdateProps.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnModelUpdateProps.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(65)))), ((int)(((byte)(105)))));
            this.btnModelUpdateProps.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnModelUpdateProps.Location = new System.Drawing.Point(178, 8);
            this.btnModelUpdateProps.Name = "btnModelUpdateProps";
            this.btnModelUpdateProps.Padding = new System.Windows.Forms.Padding(0, 4, 0, 3);
            this.btnModelUpdateProps.Size = new System.Drawing.Size(66, 66);
            this.btnModelUpdateProps.TabIndex = 19;
            this.btnModelUpdateProps.Text = "refresh";
            this.btnModelUpdateProps.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnModelUpdateProps.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnModelUpdateProps.UseVisualStyleBackColor = true;
            // 
            // btnModelResetProps
            // 
            this.btnModelResetProps.BackColor = System.Drawing.Color.Transparent;
            this.btnModelResetProps.FlatAppearance.BorderSize = 0;
            this.btnModelResetProps.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnModelResetProps.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnModelResetProps.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(65)))), ((int)(((byte)(105)))));
            this.btnModelResetProps.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnModelResetProps.Location = new System.Drawing.Point(98, 8);
            this.btnModelResetProps.Name = "btnModelResetProps";
            this.btnModelResetProps.Padding = new System.Windows.Forms.Padding(0, 4, 0, 3);
            this.btnModelResetProps.Size = new System.Drawing.Size(66, 66);
            this.btnModelResetProps.TabIndex = 18;
            this.btnModelResetProps.Text = "reset";
            this.btnModelResetProps.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnModelResetProps.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnModelResetProps.UseVisualStyleBackColor = true;
            // 
            // btnModelApplyProps
            // 
            this.btnModelApplyProps.BackColor = System.Drawing.Color.Transparent;
            this.btnModelApplyProps.FlatAppearance.BorderSize = 0;
            this.btnModelApplyProps.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnModelApplyProps.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnModelApplyProps.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(65)))), ((int)(((byte)(105)))));
            this.btnModelApplyProps.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnModelApplyProps.Location = new System.Drawing.Point(18, 8);
            this.btnModelApplyProps.Name = "btnModelApplyProps";
            this.btnModelApplyProps.Padding = new System.Windows.Forms.Padding(0, 4, 0, 3);
            this.btnModelApplyProps.Size = new System.Drawing.Size(66, 66);
            this.btnModelApplyProps.TabIndex = 17;
            this.btnModelApplyProps.Text = "apply";
            this.btnModelApplyProps.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnModelApplyProps.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnModelApplyProps.UseVisualStyleBackColor = true;
            // 
            // txtModelFinish
            // 
            this.txtModelFinish.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtModelFinish.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.txtModelFinish.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.txtModelFinish.Location = new System.Drawing.Point(45, 334);
            this.txtModelFinish.Name = "txtModelFinish";
            this.txtModelFinish.Size = new System.Drawing.Size(276, 23);
            this.txtModelFinish.TabIndex = 11;
            // 
            // lblModelFinish
            // 
            this.lblModelFinish.AutoSize = true;
            this.lblModelFinish.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblModelFinish.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblModelFinish.Location = new System.Drawing.Point(45, 317);
            this.lblModelFinish.Name = "lblModelFinish";
            this.lblModelFinish.Size = new System.Drawing.Size(42, 15);
            this.lblModelFinish.TabIndex = 10;
            this.lblModelFinish.Text = "仕上げ";
            // 
            // txtModelQty
            // 
            this.txtModelQty.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtModelQty.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.txtModelQty.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.txtModelQty.Location = new System.Drawing.Point(45, 291);
            this.txtModelQty.Name = "txtModelQty";
            this.txtModelQty.Size = new System.Drawing.Size(276, 23);
            this.txtModelQty.TabIndex = 9;
            // 
            // lblModelQty
            // 
            this.lblModelQty.AutoSize = true;
            this.lblModelQty.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblModelQty.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblModelQty.Location = new System.Drawing.Point(45, 274);
            this.lblModelQty.Name = "lblModelQty";
            this.lblModelQty.Size = new System.Drawing.Size(31, 15);
            this.lblModelQty.TabIndex = 8;
            this.lblModelQty.Text = "数量";
            // 
            // txtModelGoban
            // 
            this.txtModelGoban.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtModelGoban.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.txtModelGoban.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.txtModelGoban.Location = new System.Drawing.Point(45, 248);
            this.txtModelGoban.Name = "txtModelGoban";
            this.txtModelGoban.Size = new System.Drawing.Size(276, 23);
            this.txtModelGoban.TabIndex = 7;
            // 
            // lblModelGoban
            // 
            this.lblModelGoban.AutoSize = true;
            this.lblModelGoban.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblModelGoban.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblModelGoban.Location = new System.Drawing.Point(45, 231);
            this.lblModelGoban.Name = "lblModelGoban";
            this.lblModelGoban.Size = new System.Drawing.Size(31, 15);
            this.lblModelGoban.TabIndex = 6;
            this.lblModelGoban.Text = "合番";
            // 
            // txtModelThickness
            // 
            this.txtModelThickness.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtModelThickness.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.txtModelThickness.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.txtModelThickness.Location = new System.Drawing.Point(45, 205);
            this.txtModelThickness.Name = "txtModelThickness";
            this.txtModelThickness.Size = new System.Drawing.Size(276, 23);
            this.txtModelThickness.TabIndex = 5;
            // 
            // lblModelThickness
            // 
            this.lblModelThickness.AutoSize = true;
            this.lblModelThickness.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblModelThickness.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblModelThickness.Location = new System.Drawing.Point(45, 188);
            this.lblModelThickness.Name = "lblModelThickness";
            this.lblModelThickness.Size = new System.Drawing.Size(31, 15);
            this.lblModelThickness.TabIndex = 4;
            this.lblModelThickness.Text = "板厚";
            // 
            // txtModelMaterial
            // 
            this.txtModelMaterial.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtModelMaterial.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.txtModelMaterial.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.txtModelMaterial.Location = new System.Drawing.Point(45, 162);
            this.txtModelMaterial.Name = "txtModelMaterial";
            this.txtModelMaterial.Size = new System.Drawing.Size(276, 23);
            this.txtModelMaterial.TabIndex = 3;
            // 
            // lblModelMaterial
            // 
            this.lblModelMaterial.AutoSize = true;
            this.lblModelMaterial.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblModelMaterial.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblModelMaterial.Location = new System.Drawing.Point(45, 145);
            this.lblModelMaterial.Name = "lblModelMaterial";
            this.lblModelMaterial.Size = new System.Drawing.Size(31, 15);
            this.lblModelMaterial.TabIndex = 2;
            this.lblModelMaterial.Text = "材質";
            // 
            // txtModelName
            // 
            this.txtModelName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtModelName.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.txtModelName.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.txtModelName.Location = new System.Drawing.Point(45, 119);
            this.txtModelName.Name = "txtModelName";
            this.txtModelName.Size = new System.Drawing.Size(276, 23);
            this.txtModelName.TabIndex = 1;
            // 
            // lblModelName
            // 
            this.lblModelName.AutoSize = true;
            this.lblModelName.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblModelName.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblModelName.Location = new System.Drawing.Point(45, 102);
            this.lblModelName.Name = "lblModelName";
            this.lblModelName.Size = new System.Drawing.Size(31, 15);
            this.lblModelName.TabIndex = 0;
            this.lblModelName.Text = "品名";
            // 
            // tabModelEditPage
            // 
            this.tabModelEditPage.Controls.Add(this.panelModelCommands);
            this.tabModelEditPage.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.tabModelEditPage.Location = new System.Drawing.Point(4, 24);
            this.tabModelEditPage.Name = "tabModelEditPage";
            this.tabModelEditPage.Padding = new System.Windows.Forms.Padding(3);
            this.tabModelEditPage.Size = new System.Drawing.Size(378, 515);
            this.tabModelEditPage.TabIndex = 1;
            this.tabModelEditPage.Text = "Edit";
            this.tabModelEditPage.UseVisualStyleBackColor = true;
            // 
            // panelModelCommands
            // 
            this.panelModelCommands.AutoScroll = true;
            this.panelModelCommands.Controls.Add(this.btnMakeHole);
            this.panelModelCommands.Controls.Add(this.btnRepairHole);
            this.panelModelCommands.Controls.Add(this.btnPaintHoleSummary);
            this.panelModelCommands.Controls.Add(this.grpMakeHoleOptions);
            this.panelModelCommands.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelModelCommands.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.panelModelCommands.Location = new System.Drawing.Point(3, 3);
            this.panelModelCommands.Name = "panelModelCommands";
            this.panelModelCommands.Padding = new System.Windows.Forms.Padding(18, 16, 8, 8);
            this.panelModelCommands.Size = new System.Drawing.Size(372, 509);
            this.panelModelCommands.TabIndex = 0;
            // 
            // btnMakeHole
            // 
            this.btnMakeHole.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(238)))), ((int)(((byte)(249)))), ((int)(((byte)(242)))));
            this.btnMakeHole.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(132)))), ((int)(((byte)(183)))), ((int)(((byte)(150)))));
            this.btnMakeHole.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(210)))), ((int)(((byte)(235)))), ((int)(((byte)(220)))));
            this.btnMakeHole.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(244)))), ((int)(((byte)(233)))));
            this.btnMakeHole.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMakeHole.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnMakeHole.Image = global::ADDIN.Properties.Resources.MakeHole;
            this.btnMakeHole.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnMakeHole.Location = new System.Drawing.Point(18, 16);
            this.btnMakeHole.Name = "btnMakeHole";
            this.btnMakeHole.Padding = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this.btnMakeHole.Size = new System.Drawing.Size(96, 78);
            this.btnMakeHole.TabIndex = 2;
            this.btnMakeHole.Text = "Make hole";
            this.btnMakeHole.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnMakeHole.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnMakeHole.UseVisualStyleBackColor = false;
            // 
            // btnRepairHole
            // 
            this.btnRepairHole.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(239)))), ((int)(((byte)(246)))), ((int)(((byte)(255)))));
            this.btnRepairHole.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(126)))), ((int)(((byte)(165)))), ((int)(((byte)(210)))));
            this.btnRepairHole.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(210)))), ((int)(((byte)(229)))), ((int)(((byte)(248)))));
            this.btnRepairHole.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(239)))), ((int)(((byte)(253)))));
            this.btnRepairHole.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRepairHole.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnRepairHole.Image = global::ADDIN.Properties.Resources.RepairHole;
            this.btnRepairHole.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnRepairHole.Location = new System.Drawing.Point(126, 16);
            this.btnRepairHole.Name = "btnRepairHole";
            this.btnRepairHole.Padding = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this.btnRepairHole.Size = new System.Drawing.Size(96, 78);
            this.btnRepairHole.TabIndex = 3;
            this.btnRepairHole.TabStop = false;
            this.btnRepairHole.Text = "Repair Hole";
            this.btnRepairHole.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnRepairHole.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnRepairHole.UseVisualStyleBackColor = false;
            // 
            // btnPaintHoleSummary
            // 
            this.btnPaintHoleSummary.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(248)))), ((int)(((byte)(229)))));
            this.btnPaintHoleSummary.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(204)))), ((int)(((byte)(176)))), ((int)(((byte)(102)))));
            this.btnPaintHoleSummary.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(228)))), ((int)(((byte)(183)))));
            this.btnPaintHoleSummary.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(241)))), ((int)(((byte)(205)))));
            this.btnPaintHoleSummary.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPaintHoleSummary.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnPaintHoleSummary.Image = global::ADDIN.Properties.Resources.CountHole;
            this.btnPaintHoleSummary.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
            this.btnPaintHoleSummary.Location = new System.Drawing.Point(234, 16);
            this.btnPaintHoleSummary.Name = "btnPaintHoleSummary";
            this.btnPaintHoleSummary.Padding = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this.btnPaintHoleSummary.Size = new System.Drawing.Size(96, 78);
            this.btnPaintHoleSummary.TabIndex = 5;
            this.btnPaintHoleSummary.TabStop = false;
            this.btnPaintHoleSummary.Text = "Dem hole";
            this.btnPaintHoleSummary.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnPaintHoleSummary.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.btnPaintHoleSummary.UseVisualStyleBackColor = false;
            // 
            // grpMakeHoleOptions
            // 
            this.grpMakeHoleOptions.Controls.Add(this.pnlMakeHoleDiagram);
            this.grpMakeHoleOptions.Controls.Add(this.lblMakeHoleDirection);
            this.grpMakeHoleOptions.Controls.Add(this.cboMakeHoleDirection);
            this.grpMakeHoleOptions.Controls.Add(this.lblMakeHoleEdgeOffset);
            this.grpMakeHoleOptions.Controls.Add(this.txtMakeHoleEdgeOffset);
            this.grpMakeHoleOptions.Controls.Add(this.lblMakeHoleLeftOffset);
            this.grpMakeHoleOptions.Controls.Add(this.txtMakeHoleLeftOffset);
            this.grpMakeHoleOptions.Controls.Add(this.lblMakeHoleRightOffset);
            this.grpMakeHoleOptions.Controls.Add(this.txtMakeHoleRightOffset);
            this.grpMakeHoleOptions.Controls.Add(this.lblMakeHolePitch);
            this.grpMakeHoleOptions.Controls.Add(this.txtMakeHolePitch);
            this.grpMakeHoleOptions.Controls.Add(this.lblRepairHoleType);
            this.grpMakeHoleOptions.Controls.Add(this.cboRepairHoleType);
            this.grpMakeHoleOptions.Controls.Add(this.lblRepairHoleDiameter);
            this.grpMakeHoleOptions.Controls.Add(this.cboRepairHoleDiameter);
            this.grpMakeHoleOptions.Controls.Add(this.btnDeleteMakeHoleSize);
            this.grpMakeHoleOptions.Controls.Add(this.chkMakeHolePaint);
            this.grpMakeHoleOptions.Controls.Add(this.lblMakeHolePaintName);
            this.grpMakeHoleOptions.Controls.Add(this.txtMakeHolePaintName);
            this.grpMakeHoleOptions.Controls.Add(this.btnMakeHoleAccept);
            this.grpMakeHoleOptions.Controls.Add(this.btnMakeHoleUpdate);
            this.grpMakeHoleOptions.Controls.Add(this.btnMakeHolePattern);
            this.grpMakeHoleOptions.Controls.Add(this.btnMakeHoleReset);
            this.grpMakeHoleOptions.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.grpMakeHoleOptions.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.grpMakeHoleOptions.Location = new System.Drawing.Point(18, 110);
            this.grpMakeHoleOptions.Name = "grpMakeHoleOptions";
            this.grpMakeHoleOptions.Size = new System.Drawing.Size(336, 516);
            this.grpMakeHoleOptions.TabIndex = 4;
            this.grpMakeHoleOptions.TabStop = false;
            this.grpMakeHoleOptions.Text = "Make Hole";
            this.grpMakeHoleOptions.Visible = false;
            // 
            // pnlMakeHoleDiagram
            // 
            this.pnlMakeHoleDiagram.BackColor = System.Drawing.Color.White;
            this.pnlMakeHoleDiagram.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlMakeHoleDiagram.Location = new System.Drawing.Point(16, 24);
            this.pnlMakeHoleDiagram.Name = "pnlMakeHoleDiagram";
            this.pnlMakeHoleDiagram.Size = new System.Drawing.Size(304, 105);
            this.pnlMakeHoleDiagram.TabIndex = 0;
            // 
            // lblMakeHoleDirection
            // 
            this.lblMakeHoleDirection.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblMakeHoleDirection.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblMakeHoleDirection.Location = new System.Drawing.Point(16, 144);
            this.lblMakeHoleDirection.Name = "lblMakeHoleDirection";
            this.lblMakeHoleDirection.Size = new System.Drawing.Size(76, 18);
            this.lblMakeHoleDirection.TabIndex = 7;
            this.lblMakeHoleDirection.Text = "Direction";
            // 
            // cboMakeHoleDirection
            // 
            this.cboMakeHoleDirection.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboMakeHoleDirection.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.cboMakeHoleDirection.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.cboMakeHoleDirection.FormattingEnabled = true;
            this.cboMakeHoleDirection.Items.AddRange(new object[] {
            "Curve Flow",
            "Line Flow",
            "Spline Flow"});
            this.cboMakeHoleDirection.Location = new System.Drawing.Point(16, 161);
            this.cboMakeHoleDirection.Name = "cboMakeHoleDirection";
            this.cboMakeHoleDirection.Size = new System.Drawing.Size(304, 23);
            this.cboMakeHoleDirection.TabIndex = 8;
            // 
            // lblMakeHoleEdgeOffset
            // 
            this.lblMakeHoleEdgeOffset.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblMakeHoleEdgeOffset.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblMakeHoleEdgeOffset.Location = new System.Drawing.Point(16, 186);
            this.lblMakeHoleEdgeOffset.Name = "lblMakeHoleEdgeOffset";
            this.lblMakeHoleEdgeOffset.Size = new System.Drawing.Size(76, 18);
            this.lblMakeHoleEdgeOffset.TabIndex = 9;
            this.lblMakeHoleEdgeOffset.Text = "Dim Edge X";
            // 
            // txtMakeHoleEdgeOffset
            // 
            this.txtMakeHoleEdgeOffset.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.txtMakeHoleEdgeOffset.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.txtMakeHoleEdgeOffset.Location = new System.Drawing.Point(16, 203);
            this.txtMakeHoleEdgeOffset.Name = "txtMakeHoleEdgeOffset";
            this.txtMakeHoleEdgeOffset.Size = new System.Drawing.Size(304, 23);
            this.txtMakeHoleEdgeOffset.TabIndex = 10;
            this.txtMakeHoleEdgeOffset.Text = "20";
            // 
            // lblMakeHoleLeftOffset
            // 
            this.lblMakeHoleLeftOffset.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblMakeHoleLeftOffset.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblMakeHoleLeftOffset.Location = new System.Drawing.Point(16, 228);
            this.lblMakeHoleLeftOffset.Name = "lblMakeHoleLeftOffset";
            this.lblMakeHoleLeftOffset.Size = new System.Drawing.Size(70, 18);
            this.lblMakeHoleLeftOffset.TabIndex = 11;
            this.lblMakeHoleLeftOffset.Text = "Dim Left L";
            // 
            // txtMakeHoleLeftOffset
            // 
            this.txtMakeHoleLeftOffset.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.txtMakeHoleLeftOffset.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.txtMakeHoleLeftOffset.Location = new System.Drawing.Point(16, 245);
            this.txtMakeHoleLeftOffset.Name = "txtMakeHoleLeftOffset";
            this.txtMakeHoleLeftOffset.Size = new System.Drawing.Size(304, 23);
            this.txtMakeHoleLeftOffset.TabIndex = 12;
            this.txtMakeHoleLeftOffset.Text = "50";
            // 
            // lblMakeHoleRightOffset
            // 
            this.lblMakeHoleRightOffset.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblMakeHoleRightOffset.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblMakeHoleRightOffset.Location = new System.Drawing.Point(16, 270);
            this.lblMakeHoleRightOffset.Name = "lblMakeHoleRightOffset";
            this.lblMakeHoleRightOffset.Size = new System.Drawing.Size(76, 18);
            this.lblMakeHoleRightOffset.TabIndex = 13;
            this.lblMakeHoleRightOffset.Text = "Dim Right R";
            // 
            // txtMakeHoleRightOffset
            // 
            this.txtMakeHoleRightOffset.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.txtMakeHoleRightOffset.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.txtMakeHoleRightOffset.Location = new System.Drawing.Point(16, 287);
            this.txtMakeHoleRightOffset.Name = "txtMakeHoleRightOffset";
            this.txtMakeHoleRightOffset.Size = new System.Drawing.Size(304, 23);
            this.txtMakeHoleRightOffset.TabIndex = 14;
            this.txtMakeHoleRightOffset.Text = "50";
            // 
            // lblMakeHolePitch
            // 
            this.lblMakeHolePitch.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblMakeHolePitch.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblMakeHolePitch.Location = new System.Drawing.Point(16, 312);
            this.lblMakeHolePitch.Name = "lblMakeHolePitch";
            this.lblMakeHolePitch.Size = new System.Drawing.Size(70, 18);
            this.lblMakeHolePitch.TabIndex = 15;
            this.lblMakeHolePitch.Text = "Pitch @";
            // 
            // txtMakeHolePitch
            // 
            this.txtMakeHolePitch.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.txtMakeHolePitch.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.txtMakeHolePitch.Location = new System.Drawing.Point(16, 329);
            this.txtMakeHolePitch.Name = "txtMakeHolePitch";
            this.txtMakeHolePitch.Size = new System.Drawing.Size(304, 23);
            this.txtMakeHolePitch.TabIndex = 16;
            this.txtMakeHolePitch.Text = "300";
            // 
            // lblRepairHoleType
            // 
            this.lblRepairHoleType.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblRepairHoleType.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblRepairHoleType.Location = new System.Drawing.Point(16, 148);
            this.lblRepairHoleType.Name = "lblRepairHoleType";
            this.lblRepairHoleType.Size = new System.Drawing.Size(76, 18);
            this.lblRepairHoleType.TabIndex = 21;
            this.lblRepairHoleType.Text = "Hole Type";
            this.lblRepairHoleType.Visible = false;
            // 
            // cboRepairHoleType
            // 
            this.cboRepairHoleType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboRepairHoleType.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.cboRepairHoleType.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.cboRepairHoleType.FormattingEnabled = true;
            this.cboRepairHoleType.Items.AddRange(new object[] {
            "丸穴",
            "ルーズホル穴"});
            this.cboRepairHoleType.Location = new System.Drawing.Point(92, 145);
            this.cboRepairHoleType.Name = "cboRepairHoleType";
            this.cboRepairHoleType.Size = new System.Drawing.Size(228, 23);
            this.cboRepairHoleType.TabIndex = 22;
            this.cboRepairHoleType.Visible = false;
            // 
            // lblRepairHoleDiameter
            // 
            this.lblRepairHoleDiameter.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblRepairHoleDiameter.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblRepairHoleDiameter.Location = new System.Drawing.Point(16, 180);
            this.lblRepairHoleDiameter.Name = "lblRepairHoleDiameter";
            this.lblRepairHoleDiameter.Size = new System.Drawing.Size(76, 18);
            this.lblRepairHoleDiameter.TabIndex = 23;
            this.lblRepairHoleDiameter.Text = "Hole Size";
            this.lblRepairHoleDiameter.Visible = false;
            // 
            // cboRepairHoleDiameter
            // 
            this.cboRepairHoleDiameter.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.cboRepairHoleDiameter.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.cboRepairHoleDiameter.FormattingEnabled = true;
            this.cboRepairHoleDiameter.Items.AddRange(new object[] {
            "3.3",
            "4.2",
            "5.2",
            "6.2",
            "6.5",
            "8",
            "10"});
            this.cboRepairHoleDiameter.Location = new System.Drawing.Point(92, 177);
            this.cboRepairHoleDiameter.Name = "cboRepairHoleDiameter";
            this.cboRepairHoleDiameter.Size = new System.Drawing.Size(166, 23);
            this.cboRepairHoleDiameter.TabIndex = 24;
            this.cboRepairHoleDiameter.Text = "4.2";
            this.cboRepairHoleDiameter.Visible = false;
            // 
            // btnDeleteMakeHoleSize
            // 
            this.btnDeleteMakeHoleSize.BackColor = System.Drawing.Color.MistyRose;
            this.btnDeleteMakeHoleSize.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeleteMakeHoleSize.Font = new System.Drawing.Font("Meiryo UI", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnDeleteMakeHoleSize.Location = new System.Drawing.Point(264, 177);
            this.btnDeleteMakeHoleSize.Name = "btnDeleteMakeHoleSize";
            this.btnDeleteMakeHoleSize.Size = new System.Drawing.Size(56, 23);
            this.btnDeleteMakeHoleSize.TabIndex = 25;
            this.btnDeleteMakeHoleSize.Text = "削除";
            this.btnDeleteMakeHoleSize.UseVisualStyleBackColor = false;
            this.btnDeleteMakeHoleSize.Visible = false;
            // 
            // chkMakeHolePaint
            // 
            this.chkMakeHolePaint.AutoSize = true;
            this.chkMakeHolePaint.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.chkMakeHolePaint.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.chkMakeHolePaint.Location = new System.Drawing.Point(16, 352);
            this.chkMakeHolePaint.Name = "chkMakeHolePaint";
            this.chkMakeHolePaint.Size = new System.Drawing.Size(50, 19);
            this.chkMakeHolePaint.TabIndex = 23;
            this.chkMakeHolePaint.Text = "塗装";
            this.chkMakeHolePaint.UseVisualStyleBackColor = true;
            // 
            // lblMakeHolePaintName
            // 
            this.lblMakeHolePaintName.AutoSize = true;
            this.lblMakeHolePaintName.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblMakeHolePaintName.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(229)))), ((int)(((byte)(83)))), ((int)(((byte)(12)))));
            this.lblMakeHolePaintName.Location = new System.Drawing.Point(16, 378);
            this.lblMakeHolePaintName.Name = "lblMakeHolePaintName";
            this.lblMakeHolePaintName.Size = new System.Drawing.Size(75, 15);
            this.lblMakeHolePaintName.TabIndex = 24;
            this.lblMakeHolePaintName.Text = "Name hole";
            // 
            // txtMakeHolePaintName
            // 
            this.txtMakeHolePaintName.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.txtMakeHolePaintName.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.txtMakeHolePaintName.Location = new System.Drawing.Point(16, 395);
            this.txtMakeHolePaintName.Name = "txtMakeHolePaintName";
            this.txtMakeHolePaintName.Size = new System.Drawing.Size(304, 23);
            this.txtMakeHolePaintName.TabIndex = 25;
            // 
            // btnMakeHoleAccept
            // 
            this.btnMakeHoleAccept.BackColor = System.Drawing.Color.Honeydew;
            this.btnMakeHoleAccept.Location = new System.Drawing.Point(16, 428);
            this.btnMakeHoleAccept.Name = "btnMakeHoleAccept";
            this.btnMakeHoleAccept.Size = new System.Drawing.Size(148, 32);
            this.btnMakeHoleAccept.TabIndex = 17;
            this.btnMakeHoleAccept.Text = "Accept";
            this.btnMakeHoleAccept.UseVisualStyleBackColor = false;
            // 
            // btnMakeHoleUpdate
            // 
            this.btnMakeHoleUpdate.BackColor = System.Drawing.Color.Khaki;
            this.btnMakeHoleUpdate.Enabled = false;
            this.btnMakeHoleUpdate.Location = new System.Drawing.Point(172, 428);
            this.btnMakeHoleUpdate.Name = "btnMakeHoleUpdate";
            this.btnMakeHoleUpdate.Size = new System.Drawing.Size(148, 32);
            this.btnMakeHoleUpdate.TabIndex = 20;
            this.btnMakeHoleUpdate.Text = "UPDATE HOLE";
            this.btnMakeHoleUpdate.UseVisualStyleBackColor = false;
            // 
            // btnMakeHolePattern
            // 
            this.btnMakeHolePattern.BackColor = System.Drawing.Color.Lavender;
            this.btnMakeHolePattern.Location = new System.Drawing.Point(172, 468);
            this.btnMakeHolePattern.Name = "btnMakeHolePattern";
            this.btnMakeHolePattern.Size = new System.Drawing.Size(148, 32);
            this.btnMakeHolePattern.TabIndex = 18;
            this.btnMakeHolePattern.Text = "Pattern";
            this.btnMakeHolePattern.UseVisualStyleBackColor = false;
            this.btnMakeHolePattern.Visible = false;
            // 
            // btnMakeHoleReset
            // 
            this.btnMakeHoleReset.BackColor = System.Drawing.Color.MistyRose;
            this.btnMakeHoleReset.Location = new System.Drawing.Point(16, 468);
            this.btnMakeHoleReset.Name = "btnMakeHoleReset";
            this.btnMakeHoleReset.Size = new System.Drawing.Size(148, 32);
            this.btnMakeHoleReset.TabIndex = 19;
            this.btnMakeHoleReset.Text = "Reset";
            this.btnMakeHoleReset.UseVisualStyleBackColor = false;
            // 
            // tabModelMacroPage
            // 
            this.tabModelMacroPage.Controls.Add(this.lblCheckAssemblyHoleResult);
            this.tabModelMacroPage.Controls.Add(this.btnMirrorPart);
            this.tabModelMacroPage.Controls.Add(this.btnCheckAssemblyHole);
            this.tabModelMacroPage.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.tabModelMacroPage.Location = new System.Drawing.Point(4, 24);
            this.tabModelMacroPage.Name = "tabModelMacroPage";
            this.tabModelMacroPage.Padding = new System.Windows.Forms.Padding(3);
            this.tabModelMacroPage.Size = new System.Drawing.Size(378, 515);
            this.tabModelMacroPage.TabIndex = 2;
            this.tabModelMacroPage.Text = "Macro";
            this.tabModelMacroPage.UseVisualStyleBackColor = true;
            // 
            // lblCheckAssemblyHoleResult
            // 
            this.lblCheckAssemblyHoleResult.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblCheckAssemblyHoleResult.Font = new System.Drawing.Font("Meiryo UI", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblCheckAssemblyHoleResult.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(45)))), ((int)(((byte)(55)))));
            this.lblCheckAssemblyHoleResult.Location = new System.Drawing.Point(18, 80);
            this.lblCheckAssemblyHoleResult.Name = "lblCheckAssemblyHoleResult";
            this.lblCheckAssemblyHoleResult.Padding = new System.Windows.Forms.Padding(8, 6, 8, 6);
            this.lblCheckAssemblyHoleResult.Size = new System.Drawing.Size(342, 215);
            this.lblCheckAssemblyHoleResult.TabIndex = 0;
            // 
            // btnMirrorPart
            // 
            this.btnMirrorPart.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(239)))), ((int)(((byte)(252)))));
            this.btnMirrorPart.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(77)))), ((int)(((byte)(133)))), ((int)(((byte)(190)))));
            this.btnMirrorPart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMirrorPart.Font = new System.Drawing.Font("Meiryo UI", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnMirrorPart.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(25)))), ((int)(((byte)(70)))), ((int)(((byte)(112)))));
            this.btnMirrorPart.Image = global::ADDIN.Properties.Resources.MirrorPart3D;
            this.btnMirrorPart.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnMirrorPart.Location = new System.Drawing.Point(126, 16);
            this.btnMirrorPart.Name = "btnMirrorPart";
            this.btnMirrorPart.Padding = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.btnMirrorPart.Size = new System.Drawing.Size(108, 48);
            this.btnMirrorPart.TabIndex = 1;
            this.btnMirrorPart.Text = "MIRROR\r\nPART";
            this.btnMirrorPart.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.btnMirrorPart.UseVisualStyleBackColor = false;
            // 
            // btnCheckAssemblyHole
            // 
            this.btnCheckAssemblyHole.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(223)))), ((int)(((byte)(244)))), ((int)(((byte)(232)))));
            this.btnCheckAssemblyHole.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(156)))), ((int)(((byte)(96)))));
            this.btnCheckAssemblyHole.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckAssemblyHole.Font = new System.Drawing.Font("Meiryo UI", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnCheckAssemblyHole.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(70)))), ((int)(((byte)(42)))));
            this.btnCheckAssemblyHole.Image = global::ADDIN.Properties.Resources.CheckHole3D;
            this.btnCheckAssemblyHole.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnCheckAssemblyHole.Location = new System.Drawing.Point(18, 16);
            this.btnCheckAssemblyHole.Name = "btnCheckAssemblyHole";
            this.btnCheckAssemblyHole.Padding = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.btnCheckAssemblyHole.Size = new System.Drawing.Size(96, 48);
            this.btnCheckAssemblyHole.TabIndex = 0;
            this.btnCheckAssemblyHole.Text = "CHECK\r\nHOLE";
            this.btnCheckAssemblyHole.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.btnCheckAssemblyHole.UseVisualStyleBackColor = false;
            // 
            // btnCheckKegaki
            // 
            this.btnCheckKegaki.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(207)))), ((int)(((byte)(244)))));
            this.btnCheckKegaki.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnCheckKegaki.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(171)))), ((int)(((byte)(96)))), ((int)(((byte)(194)))));
            this.btnCheckKegaki.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(225)))), ((int)(((byte)(190)))), ((int)(((byte)(238)))));
            this.btnCheckKegaki.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckKegaki.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnCheckKegaki.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(90)))), ((int)(((byte)(34)))), ((int)(((byte)(118)))));
            this.btnCheckKegaki.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnCheckKegaki.Location = new System.Drawing.Point(242, 116);
            this.btnCheckKegaki.Name = "btnCheckKegaki";
            this.btnCheckKegaki.Padding = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.btnCheckKegaki.Size = new System.Drawing.Size(90, 42);
            this.btnCheckKegaki.TabIndex = 14;
            this.btnCheckKegaki.Text = "CHECK\r\nKEGAKI";
            this.btnCheckKegaki.UseVisualStyleBackColor = false;
            this.btnCheckKegaki.Visible = false;
            // 
            // btnCheckUraOmote
            // 
            this.btnCheckUraOmote.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(207)))), ((int)(((byte)(244)))));
            this.btnCheckUraOmote.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnCheckUraOmote.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(171)))), ((int)(((byte)(96)))), ((int)(((byte)(194)))));
            this.btnCheckUraOmote.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(225)))), ((int)(((byte)(190)))), ((int)(((byte)(238)))));
            this.btnCheckUraOmote.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckUraOmote.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnCheckUraOmote.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(90)))), ((int)(((byte)(34)))), ((int)(((byte)(118)))));
            this.btnCheckUraOmote.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnCheckUraOmote.Location = new System.Drawing.Point(144, 116);
            this.btnCheckUraOmote.Name = "btnCheckUraOmote";
            this.btnCheckUraOmote.Padding = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.btnCheckUraOmote.Size = new System.Drawing.Size(90, 42);
            this.btnCheckUraOmote.TabIndex = 13;
            this.btnCheckUraOmote.Text = "CHECK\r\nウラ表";
            this.btnCheckUraOmote.UseVisualStyleBackColor = false;
            this.btnCheckUraOmote.Visible = false;
            // 
            // BomTaskPaneControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.tabBom);
            this.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(22)))), ((int)(((byte)(28)))));
            this.Name = "BomTaskPaneControl";
            this.Size = new System.Drawing.Size(400, 577);
            this.tabBom.ResumeLayout(false);
            this.tabDrawing.ResumeLayout(false);
            this.tabDrawingPages.ResumeLayout(false);
            this.tabDrawingBom.ResumeLayout(false);
            this.tabDrawingBom.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvModelBom)).EndInit();
            this.tabComponentDrawing.ResumeLayout(false);
            this.groupBox3.ResumeLayout(false);
            this.grpComponentBom.ResumeLayout(false);
            this.grpComponentSize.ResumeLayout(false);
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.tabModel.ResumeLayout(false);
            this.tabModelPages.ResumeLayout(false);
            this.tabModelPropsPage.ResumeLayout(false);
            this.panelModelProps.ResumeLayout(false);
            this.panelModelProps.PerformLayout();
            this.tabModelEditPage.ResumeLayout(false);
            this.panelModelCommands.ResumeLayout(false);
            this.grpMakeHoleOptions.ResumeLayout(false);
            this.grpMakeHoleOptions.PerformLayout();
            this.tabModelMacroPage.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.TabControl tabBom;
        private System.Windows.Forms.TabPage tabDrawing;
        private System.Windows.Forms.TabControl tabDrawingPages;
        private System.Windows.Forms.TabPage tabDrawingBom;
        private System.Windows.Forms.TabPage tabComponentDrawing;
        private System.Windows.Forms.GroupBox grpComponentSize;
        private System.Windows.Forms.Button btnGetWL;
        private System.Windows.Forms.Button btnRotateCcw;
        private System.Windows.Forms.Button btnRotateCw;
        private System.Windows.Forms.Button btnHorizontalAlignment;
        private System.Windows.Forms.GroupBox grpComponentBom;
        private System.Windows.Forms.Button btnInsertBalloon;
        private System.Windows.Forms.ComboBox cboBalloonProperty;
        private System.Windows.Forms.Button btnDeleteText;
        private System.Windows.Forms.Button btnText;
        private ADDIN.Commands.HistoryTextBox cboSide;
        private System.Windows.Forms.Button btnDeleteNote;
        private System.Windows.Forms.Button btnNote;
        private ADDIN.Commands.HistoryTextBox cboBendLine;
        private System.Windows.Forms.TabPage tabModel;
        private System.Windows.Forms.DataGridView dgvModelBom;
        private System.Windows.Forms.Button btnLoadBom;
        private System.Windows.Forms.Button btnClearBom;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Button btnCheckDfTk;
        private System.Windows.Forms.Button btnCheckUraOmote;
        private System.Windows.Forms.Button btnCheckKegaki;
        private System.Windows.Forms.ProgressBar progressCheck;
        private System.Windows.Forms.DataGridViewCheckBoxColumn Column5;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column1;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column3;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column6;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column4;
        private System.Windows.Forms.DataGridViewTextBoxColumn Column2;
        private System.Windows.Forms.CheckBox chkSelectAll;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TextBox txtWidth;
        private System.Windows.Forms.TextBox txtLength;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.Button btnDimKegaki;
        private System.Windows.Forms.Button btnFixScale;
        private System.Windows.Forms.Button dimvang;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Button btnOpenAssem;
        private System.Windows.Forms.Button btnCheckBalloon;
        private System.Windows.Forms.Button btnCheckAll;
        private System.Windows.Forms.Button btnCheckRound;
        private System.Windows.Forms.Button btnCheckSamePart;
        private System.Windows.Forms.Button btnCheckDrawingBom;
        private System.Windows.Forms.Button btnMakeHole;
        private System.Windows.Forms.Panel panelModelCommands;
        private System.Windows.Forms.Button btnRepairHole;
        private System.Windows.Forms.GroupBox grpMakeHoleOptions;
        private System.Windows.Forms.Panel pnlMakeHoleDiagram;
        private System.Windows.Forms.Label lblMakeHoleDirection;
        private System.Windows.Forms.ComboBox cboMakeHoleDirection;
        private System.Windows.Forms.Label lblMakeHoleEdgeOffset;
        private System.Windows.Forms.TextBox txtMakeHoleEdgeOffset;
        private System.Windows.Forms.Label lblMakeHoleLeftOffset;
        private System.Windows.Forms.TextBox txtMakeHoleLeftOffset;
        private System.Windows.Forms.Label lblMakeHoleRightOffset;
        private System.Windows.Forms.TextBox txtMakeHoleRightOffset;
        private System.Windows.Forms.Label lblMakeHolePitch;
        private System.Windows.Forms.TextBox txtMakeHolePitch;
        private System.Windows.Forms.Label lblRepairHoleType;
        private System.Windows.Forms.ComboBox cboRepairHoleType;
        private System.Windows.Forms.Label lblRepairHoleDiameter;
        private System.Windows.Forms.ComboBox cboRepairHoleDiameter;
        private System.Windows.Forms.Button btnDeleteMakeHoleSize;
        private System.Windows.Forms.CheckBox chkMakeHolePaint;
        private System.Windows.Forms.Button btnMakeHoleUpdate;
        private System.Windows.Forms.Button btnMakeHoleAccept;
        private System.Windows.Forms.Button btnMakeHolePattern;
        private System.Windows.Forms.Button btnMakeHoleReset;
        private System.Windows.Forms.Button btnPaintHoleSummary;
        private System.Windows.Forms.TabControl tabModelPages;
        private System.Windows.Forms.TabPage tabModelPropsPage;
        private System.Windows.Forms.TabPage tabModelEditPage;
        private System.Windows.Forms.TabPage tabModelMacroPage;
        private System.Windows.Forms.Label lblCheckAssemblyHoleResult;
        private System.Windows.Forms.Button btnCheckAssemblyHole;
        private System.Windows.Forms.Button btnMirrorPart;
        private System.Windows.Forms.Panel panelModelProps;
        private System.Windows.Forms.Button btnModelApplyProps;
        private System.Windows.Forms.Button btnModelResetProps;
        private System.Windows.Forms.Button btnModelUpdateProps;
        private System.Windows.Forms.TextBox txtModelFinish;
        private System.Windows.Forms.Label lblModelFinish;
        private System.Windows.Forms.TextBox txtModelQty;
        private System.Windows.Forms.Label lblModelQty;
        private System.Windows.Forms.TextBox txtModelGoban;
        private System.Windows.Forms.Label lblModelGoban;
        private System.Windows.Forms.TextBox txtModelThickness;
        private System.Windows.Forms.Label lblModelThickness;
        private System.Windows.Forms.TextBox txtModelMaterial;
        private System.Windows.Forms.Label lblModelMaterial;
        private System.Windows.Forms.TextBox txtModelName;
        private System.Windows.Forms.Label lblModelName;
        private System.Windows.Forms.Button btnDimMatCat;
        private System.Windows.Forms.Button btnSplineToArcs;
    }
}


