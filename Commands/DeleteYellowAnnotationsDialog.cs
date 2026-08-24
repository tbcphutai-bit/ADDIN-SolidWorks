using System.Windows.Forms;

namespace ADDIN.Commands
{
    public partial class DeleteYellowAnnotationsDialog : Form
    {
        public DeleteYellowAnnotationsDialog()
        {
            InitializeComponent();
        }

        public bool DeleteDimension => chkDimension.Checked;
        public bool DeleteBalloon => chkBalloon.Checked;
        public bool DeleteText => chkText.Checked;
    }
}