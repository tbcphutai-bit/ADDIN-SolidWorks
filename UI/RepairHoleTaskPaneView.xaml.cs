using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ADDIN.UI
{
    public partial class RepairHoleTaskPaneView : UserControl
    {
        internal event Action<RepairHoleRowViewModel> RowActivated;
        private bool nativeCommand;
        private HwndSource keyboardSource;
        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr window);

        public RepairHoleTaskPaneView()
        {
            InitializeComponent();
            RepairGrid.PreviewMouseLeftButtonDown += OnRowMouseDown;
            RepairGrid.CurrentCellChanged += (s, e) => ActivateRow(RepairGrid.CurrentItem as RepairHoleRowViewModel);
            RepairGrid.GotKeyboardFocus += (s, e) => {
                var row = FindParent<DataGridRow>(e.NewFocus as DependencyObject);
                ActivateRow(row == null ? null : row.Item as RepairHoleRowViewModel);
            };
            Loaded += (s, e) => AttachKeyboardSource();
            Unloaded += (s, e) => DetachKeyboardSource();
        }

        internal void ConfigureForNativeCommand()
        {
            nativeCommand = true;
            RepairGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
            Background = System.Windows.Media.Brushes.White;
            ScanPrompt.Visibility = System.Windows.Visibility.Collapsed;
            ScanAction.Visibility = System.Windows.Visibility.Collapsed;
            RepairActions.Visibility = System.Windows.Visibility.Collapsed;
            NativeDiagram.Visibility = System.Windows.Visibility.Visible;
            GuideNote.Text = "Bấm dòng để làm sáng các lỗ trên 3D.\nNhập 4.2 (tròn) hoặc 5x25 (slot).\nĐể trống = bỏ qua; RCM là gợi ý.\n✓ Repair · ✕ Hủy.";
        }

        private void OnRowMouseDown(object sender, MouseButtonEventArgs args)
        {
            if (!nativeCommand) return;
            var row = FindParent<DataGridRow>(args.OriginalSource as DependencyObject);
            var value = row == null ? null : row.Item as RepairHoleRowViewModel;
            if (value == null) return;
            FocusNativeWindow();
            ActivateRow(value);
            // An existing editor keeps its caret/selection. Clicking another
            // column opens the Repair editor of that row with one click.
            if (FindParent<TextBox>(args.OriginalSource as DependencyObject) != null) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)(() => FocusRepairInput(value)));
        }

        internal bool FocusRepairInput(RepairHoleRowViewModel row)
        {
            if (row == null || !RepairGrid.Items.Contains(row) || !IsLoaded) return false;
            RepairGrid.SelectedItem = row;
            var column = RepairGrid.Columns[RepairGrid.Columns.Count - 1];
            RepairGrid.CurrentCell = new DataGridCellInfo(row, column);
            RepairGrid.ScrollIntoView(row, column);
            RepairGrid.UpdateLayout();
            var editor = FindChild<TextBox>(column.GetCellContent(row));
            if (editor == null) return false;
            FocusNativeWindow();
            editor.Focus(); Keyboard.Focus(editor); editor.SelectAll();
            return editor.IsKeyboardFocusWithin;
        }

        private void FocusNativeWindow()
        {
            // WindowFromHandle reparents ElementHost into an unmanaged dialog.
            // Give its WPF HWND native focus before assigning the text caret.
            AttachKeyboardSource();
            if (keyboardSource != null && keyboardSource.Handle != IntPtr.Zero)
                SetFocus(keyboardSource.Handle);
        }

        private void ActivateRow(RepairHoleRowViewModel row)
        { if (nativeCommand) RowActivated?.Invoke(row); }

        private void AttachKeyboardSource()
        {
            if (!nativeCommand) return;
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source == keyboardSource) return;
            DetachKeyboardSource();
            keyboardSource = source;
            keyboardSource?.AddHook(KeyboardWindowHook);
        }
        private void DetachKeyboardSource()
        {
            keyboardSource?.RemoveHook(KeyboardWindowHook);
            keyboardSource = null;
        }
        private IntPtr KeyboardWindowHook(IntPtr window, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            // The native dialog must deliver edit/navigation keys to WPF.
            if (message == 0x0087 && IsKeyboardFocusWithin) // WM_GETDLGCODE
            { handled = true; return new IntPtr(0x0085); } // WANTCHARS | WANTALLKEYS | WANTARROWS
            return IntPtr.Zero;
        }
        private static T FindParent<T>(DependencyObject value) where T : DependencyObject
        {
            while (value != null)
            {
                if (value is T match) return match;
                value = value is Visual || value is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(value) : LogicalTreeHelper.GetParent(value);
            }
            return null;
        }
        private static T FindChild<T>(DependencyObject value) where T : DependencyObject
        {
            if (value == null) return null;
            if (value is T match) return match;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++)
            {
                var child = FindChild<T>(VisualTreeHelper.GetChild(value, i));
                if (child != null) return child;
            }
            return null;
        }
    }
}
