// Isolated Kadai UI integration: no edits to existing command implementation.
using System;
using System.Windows.Forms;
using ADDIN.UI;

namespace ADDIN
{
    public partial class BomTaskPaneControl
    {
        private ModernButton btnKadai;
        private KadaiForm kadaiForm;

        private void EnsureKadaiMacroButton()
        {
            if (tabModelMacroPage == null || btnKadai != null) return;
            btnKadai = new ModernButton { Name = "btnKadai", Text = "Kadai", TabStop = true,
                AutoSize = false, Visible = true };
            btnKadai.Click += btnKadai_Click;
            tabModelMacroPage.Controls.Add(btnKadai);
        }

        private void btnKadai_Click(object sender, EventArgs e)
        {
            if (!CanRunModelMacro(btnKadai)) return;
            if (kadaiForm != null && !kadaiForm.IsDisposed) { kadaiForm.Activate(); return; }
            kadaiForm = new KadaiForm(swApp);
            kadaiForm.FormClosed += (s, args) => { kadaiForm = null; };
            kadaiForm.Show(this);
        }

        private void CloseKadaiForm()
        {
            if (kadaiForm != null && !kadaiForm.IsDisposed) kadaiForm.Close();
            kadaiForm = null;
        }
    }
}
