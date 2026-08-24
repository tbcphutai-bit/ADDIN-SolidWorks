namespace ADDIN.Commands
{
    partial class DeleteYellowAnnotationsDialog
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.lblTitle = new System.Windows.Forms.Label();
            this.chkDimension = new System.Windows.Forms.CheckBox();
            this.chkBalloon = new System.Windows.Forms.CheckBox();
            this.chkText = new System.Windows.Forms.CheckBox();
            this.btnDelete = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Times New Roman", 15.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblTitle.ForeColor = System.Drawing.Color.DarkGoldenrod;
            this.lblTitle.Location = new System.Drawing.Point(20, 16);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(232, 24);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "Xóa đối tượng màu vàng";
            // 
            // chkDimension
            // 
            this.chkDimension.AutoSize = true;
            this.chkDimension.Checked = true;
            this.chkDimension.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkDimension.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.chkDimension.Location = new System.Drawing.Point(20, 52);
            this.chkDimension.Name = "chkDimension";
            this.chkDimension.Size = new System.Drawing.Size(175, 19);
            this.chkDimension.TabIndex = 1;
            this.chkDimension.Text = "Xóa Dimension màu vàng";
            this.chkDimension.UseVisualStyleBackColor = true;
            // 
            // chkBalloon
            // 
            this.chkBalloon.AutoSize = true;
            this.chkBalloon.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.chkBalloon.Location = new System.Drawing.Point(20, 78);
            this.chkBalloon.Name = "chkBalloon";
            this.chkBalloon.Size = new System.Drawing.Size(156, 19);
            this.chkBalloon.TabIndex = 1;
            this.chkBalloon.Text = "Xóa Balloon màu vàng";
            this.chkBalloon.UseVisualStyleBackColor = true;
            // 
            // chkText
            // 
            this.chkText.AutoSize = true;
            this.chkText.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.chkText.Location = new System.Drawing.Point(20, 104);
            this.chkText.Name = "chkText";
            this.chkText.Size = new System.Drawing.Size(181, 19);
            this.chkText.TabIndex = 1;
            this.chkText.Text = "Xóa Text / Note màu vàng";
            this.chkText.UseVisualStyleBackColor = true;
            // 
            // btnDelete
            // 
            this.btnDelete.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnDelete.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnDelete.Location = new System.Drawing.Point(121, 156);
            this.btnDelete.Name = "btnDelete";
            this.btnDelete.Size = new System.Drawing.Size(90, 30);
            this.btnDelete.TabIndex = 2;
            this.btnDelete.Text = "Xóa";
            this.btnDelete.UseVisualStyleBackColor = true;
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnCancel.Location = new System.Drawing.Point(220, 156);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(90, 30);
            this.btnCancel.TabIndex = 2;
            this.btnCancel.Text = "Hủy";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // DeleteYellowAnnotationsDialog
            // 
            this.AcceptButton = this.btnDelete;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(330, 205);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnDelete);
            this.Controls.Add(this.chkText);
            this.Controls.Add(this.chkBalloon);
            this.Controls.Add(this.chkDimension);
            this.Controls.Add(this.lblTitle);
            this.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "DeleteYellowAnnotationsDialog";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Xóa đối tượng màu vàng";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.CheckBox chkDimension;
        private System.Windows.Forms.CheckBox chkBalloon;
        private System.Windows.Forms.CheckBox chkText;
        private System.Windows.Forms.Button btnDelete;
        private System.Windows.Forms.Button btnCancel;
    }
}