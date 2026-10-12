using System.Windows.Controls;

namespace ADDIN.UI
{
    public partial class RepairHoleTaskPaneView : UserControl
    {
        public RepairHoleTaskPaneView()
        {
            InitializeComponent();
        }

        internal void ConfigureForNativeCommand()
        {
            Background = System.Windows.Media.Brushes.White;
            ScanPrompt.Visibility = System.Windows.Visibility.Collapsed;
            ScanAction.Visibility = System.Windows.Visibility.Collapsed;
            RepairActions.Visibility = System.Windows.Visibility.Collapsed;
            NativeDiagram.Visibility = System.Windows.Visibility.Visible;
            GuideNote.Text = "Nhập 4.2 (tròn) hoặc 5x25 (slot).\nĐể trống = bỏ qua; RCM là gợi ý.\n✓ Repair · ✕ Hủy.";
        }
    }
}
