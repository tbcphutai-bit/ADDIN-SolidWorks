using System;
using System.Windows.Forms;

namespace ADDIN.Commands
{
    internal sealed partial class CheckAllSelectionDialog : Form
    {
        public CheckAllSelectionDialog()
        {
            InitializeComponent();
            UpdateSelectionState();
        }

        public CombinedCheckOptions Options { get; private set; }

        private void CheckOption_CheckedChanged(object sender, EventArgs e)
        {
            UpdateSelectionState();
        }

        private void UpdateSelectionState()
        {
            int selectedCount = 0;
            if (chkUraOmote.Checked) selectedCount++;
            if (chkKegaki.Checked) selectedCount++;

            lblSelectionCount.Text = "Da chon " + selectedCount + "/2 lenh";
            btnRun.Enabled = selectedCount > 0;
        }

        private void btnRun_Click(object sender, EventArgs e)
        {
            Options = new CombinedCheckOptions
            {
                CheckDfTk = false,
                CheckUraOmote = chkUraOmote.Checked,
                CheckKegaki = chkKegaki.Checked
            };

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
