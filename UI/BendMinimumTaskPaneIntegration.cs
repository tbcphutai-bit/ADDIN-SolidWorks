using System;
using System.Drawing;
using ADDIN.UI;
using SolidWorks.Interop.sldworks;

namespace ADDIN
{
    public partial class BomTaskPaneControl
    {
        private ModernButton btnCheckBendMinimum;
        private BendAssemblyBatchForm bendCheckWindow;
        private BendAssemblyBatchController bendCheckController;
        private ModelDoc2 bendCheckHost;
        private string bendCheckConfiguration;

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
                    var active = swApp.ActiveDoc as ModelDoc2;
                    if (active != null && swApp.IsSame(active, bendCheckHost) == (int)SolidWorks.Interop.swconst.swObjectEquality.swObjectSame &&
                        active.ConfigurationManager.ActiveConfiguration.Name == bendCheckConfiguration) { bendCheckWindow.Show(); bendCheckWindow.Activate(); return; }
                    bendCheckWindow.Close();
                    if (bendCheckWindow != null && !bendCheckWindow.IsDisposed) return;
                }
                try
                {
                    var model = swApp.ActiveDoc as ModelDoc2;
                    if (model == null) return;
                    bendCheckHost = model; bendCheckConfiguration = model.ConfigurationManager.ActiveConfiguration.Name;
                    bendCheckWindow = new BendAssemblyBatchForm(model.GetTitle(), bendCheckConfiguration);
                    var backend = new BendAssemblyBatchLiveBackend(swApp, model);
                    bendCheckController = new BendAssemblyBatchController(bendCheckWindow, backend);
                    if (model.GetType() == (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocPART) bendCheckWindow.SetPartMode();
                    else if (backend.HasSelection) bendCheckWindow.SetScope(ADDIN.Commands.BendAssembly.AssemblyCheckScope.SelectedComponents);
                    bendCheckWindow.FormClosed += (sender, args) => { bendCheckWindow = null; bendCheckController = null; bendCheckHost = null; };
                    bendCheckWindow.Show();
                }
                catch (Exception ex) { System.Windows.Forms.MessageBox.Show(this, ex.Message, "Check chấn"); }
            };
            tabModelMacroPage.Controls.Add(btnCheckBendMinimum);
            SetBomCommandToolTip(btnCheckBendMinimum,"CHECK CHẤN\nPart / component đã chọn; không chọn thì toàn bộ Assembly. Kích thước phủ bì · bảng tra Excel trong Settings.");
        }
        private void CloseBendMinimumWindow()
        {
            var window = bendCheckWindow;
            if (window != null && !window.IsDisposed) window.Close();
        }
    }
}
