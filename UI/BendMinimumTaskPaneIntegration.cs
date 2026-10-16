using System;
using System.Drawing;
using ADDIN.UI;
using SolidWorks.Interop.sldworks;

namespace ADDIN
{
    public partial class BomTaskPaneControl
    {
        private ModernButton btnCheckBendMinimum;
        private BendCheckForm bendCheckWindow;

        private void EnsureBendMinimumButton()
        {
            if(btnCheckBendMinimum!=null||tabModelMacroPage==null)return;
            btnCheckBendMinimum=new ModernButton {
                Name="btnCheckBendMinimum",Text="Check chấn",Subtitle="Part / Assembly",
                Kind=ModernButtonKind.Tile,Icon=TaiIcon.BendFlat,Tone=ModernButtonTone.Accent,
                Size=new Size(180,44),AutoSize=false,Enabled=false,TabIndex=32
            };
            btnCheckBendMinimum.Click+=(s,e)=>{
                if(!CanRunModelMacro(btnCheckBendMinimum))return;
                if (bendCheckWindow != null && !bendCheckWindow.IsDisposed)
                {
                    if (bendCheckWindow.IsForActiveModel) { bendCheckWindow.Show(); bendCheckWindow.Activate(); return; }
                    bendCheckWindow.Close();
                    if (bendCheckWindow != null && !bendCheckWindow.IsDisposed) return;
                }
                try
                {
                    var model = swApp.ActiveDoc as ModelDoc2;
                    if (model == null) return;
                    bendCheckWindow = new BendCheckForm(swApp, model, () =>
                        vCutBendAssignmentControl != null && !vCutBendAssignmentControl.IsDisposed && vCutBendAssignmentControl.Visible ? vCutBendAssignmentControl.Rows : null);
                    bendCheckWindow.FormClosed += (sender, args) => bendCheckWindow = null;
                    bendCheckWindow.Show();
                }
                catch (Exception ex) { System.Windows.Forms.MessageBox.Show(this, ex.Message, "Check chấn"); }
            };
            tabModelMacroPage.Controls.Add(btnCheckBendMinimum);
            SetBomCommandToolTip(btnCheckBendMinimum,"CHECK CHẤN\nPart / component đã chọn; không chọn thì toàn bộ Assembly. Kích thước phủ bì · bảng tra Excel trong Settings.");
        }
        private void CloseBendMinimumWindow()
        {
            var window = bendCheckWindow; bendCheckWindow = null;
            if (window != null && !window.IsDisposed) window.Close();
        }
    }
}
