using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.UI
{
    internal class NativeResultTableHost : UserControl
    {
        internal bool Released { get; private set; }
        internal NativeResultTableHost()
        { BackColor = Color.FromArgb(244, 248, 253); Font = new Font("Meiryo UI", 8.5f); }
        internal void Release() { Released = true; }
        internal static DataGridView CreateGrid(string name, bool readOnly)
        {
            return new DataGridView {
                Name = name, Dock = DockStyle.Fill, BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                ReadOnly = readOnly, RowHeadersVisible = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditOnEnter, AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                    BackColor = Color.FromArgb(235, 242, 250), ForeColor = Color.FromArgb(15, 23, 42),
                    WrapMode = DataGridViewTriState.True }, EnableHeadersVisualStyles = false,
                RowTemplate = { Height = 27 }, GridColor = Color.FromArgb(210, 220, 230) };
        }
        internal static void Column(DataGridView grid, string name, string title, int width)
        { grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, Width = width, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable }); }
        [StructLayout(LayoutKind.Sequential)] private struct WindowPosition
        { internal IntPtr Window, InsertAfter; internal int X, Y, Width, Height; internal uint Flags; }
        [StructLayout(LayoutKind.Sequential)] private struct ClientRect
        { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out ClientRect rect);
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0046 && message.LParam != IntPtr.Zero)
            {
                var position = Marshal.PtrToStructure<WindowPosition>(message.LParam);
                var parent = GetParent(message.HWnd); ClientRect rect;
                int left = (position.Flags & 2) == 0 ? position.X : Left;
                if ((position.Flags & 1) == 0 && parent != IntPtr.Zero && left >= 0 && GetClientRect(parent, out rect) && rect.Right > rect.Left)
                { position.Width = Math.Max(1, rect.Right - rect.Left - 2 * left); Marshal.StructureToPtr(position, message.LParam, false); }
            }
            base.WndProc(ref message);
        }
    }

    internal sealed class NativeCountHoleTable : NativeResultTableHost
    {
        internal readonly DataGridView SummaryGrid = CreateGrid("HoleSummaryGrid", true);
        internal readonly DataGridView DetailGrid = CreateGrid("HoleDetailGrid", true);
        private readonly TextBox detail = new TextBox { Dock = DockStyle.Bottom, Height = 110,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(244, 248, 253), BorderStyle = BorderStyle.None };
        internal NativeCountHoleTable(PaintHoleSummaryCommand.PaintHoleScanResult result)
        {
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var summary = new TabPage("Tổng hợp (" + result.Summary.Count + ")");
            var records = new TabPage("Chi tiết (" + result.Records.Count + ")");
            summary.Controls.Add(SummaryGrid); records.Controls.Add(DetailGrid);
            tabs.TabPages.Add(summary); tabs.TabPages.Add(records); Controls.Add(tabs); Controls.Add(detail);
            Column(SummaryGrid, "Hole", "Kích thước\n(mm)", 100);
            Column(SummaryGrid, "Label", "Tên lỗ", 190); Column(SummaryGrid, "Quantity", "SL", 50);
            SummaryGrid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            foreach (var row in result.GetSortedSummary())
            { int index = SummaryGrid.Rows.Add(row.HoleKey, row.HoleLabel, row.Quantity); SummaryGrid.Rows[index].Tag = row; }
            var columns = new[] { "Part", "Component", "Configuration", "Feature", "Type", "Hole", "Label", "Family", "Quantity", "Pattern", "Source", "Path" };
            var titles = new[] { "部品番号", "Component", "Cấu hình", "Feature", "Loại feature", "Kích thước (mm)", "Tên lỗ", "Family ID", "SL", "Pattern", "Nguồn", "Đường dẫn Part" };
            for (int i = 0; i < columns.Length; i++) Column(DetailGrid, columns[i], titles[i], i == 8 ? 45 : i == 9 ? 65 : 130);
            foreach (var row in result.Records)
            {
                int index = DetailGrid.Rows.Add(row.PartNumber, row.ComponentName, row.Configuration, row.FeatureName,
                    row.FeatureType, row.HoleKey, row.HoleLabel, row.FamilyId, row.Quantity, row.IsPattern ? "Có" : "", row.Source, row.PartPath);
                DetailGrid.Rows[index].Tag = row;
                foreach (DataGridViewCell cell in DetailGrid.Rows[index].Cells) cell.ToolTipText = Convert.ToString(cell.Value);
            }
            SummaryGrid.CurrentCellChanged += (s, e) => ShowSummary(); DetailGrid.CurrentCellChanged += (s, e) => ShowDetail();
            tabs.SelectedIndexChanged += (s, e) => { if (tabs.SelectedIndex == 0) ShowSummary(); else ShowDetail(); }; ShowSummary();
        }
        private void ShowSummary()
        {
            if (Released || Disposing || IsDisposed) return;
            var current = SummaryGrid.CurrentRow ?? (SummaryGrid.Rows.Count > 0 ? SummaryGrid.Rows[0] : null);
            var row = current == null ? null : current.Tag as PaintHoleSummaryCommand.HoleSummary;
            detail.Text = row == null ? "Không có lỗ trong phạm vi đã quét." : "Kích thước: " + row.HoleKey + " mm\r\nTên lỗ: " + row.HoleLabel + "\r\nTổng số lượng: " + row.Quantity;
        }
        private void ShowDetail()
        {
            if (Released || Disposing || IsDisposed) return;
            var current = DetailGrid.CurrentRow ?? (DetailGrid.Rows.Count > 0 ? DetailGrid.Rows[0] : null);
            var row = current == null ? null : current.Tag as PaintHoleSummaryCommand.HoleRecord;
            detail.Text = row == null ? "Không có dòng chi tiết." :
                "Part: " + row.PartNumber + " | Component: " + row.ComponentName + "\r\nCấu hình: " + row.Configuration +
                "\r\nFeature: " + row.FeatureName + " | " + row.FeatureType + "\r\nLỗ: " + row.HoleKey + " | " + row.HoleLabel +
                " | SL: " + row.Quantity + " | Pattern: " + (row.IsPattern ? "Có" : "Không") +
                "\r\nFamily: " + row.FamilyId + " | Nguồn: " + row.Source + "\r\nĐường dẫn: " + row.PartPath;
        }
    }

    [ComVisible(true)] [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class CountHolePropertyManagerPage : NativeCommandPropertyManagerPage
    {
        private readonly PaintHoleSummaryCommand.PaintHoleScanResult result;
        private readonly Action export, refresh;
        private NativeCountHoleTable table;
        private bool refreshing, shouldExport;
        private int previousPanelWidth, expandedPanelWidth;
        internal CountHolePropertyManagerPage(ISldWorks app, PaintHoleSummaryCommand.PaintHoleScanResult result,
            Action export, Action refresh, Action<NativeCommandPropertyManagerPage> closed) : base(app, "ĐẾM HOLE", closed)
        { this.result = result; this.export = export; this.refresh = refresh; shouldExport = result.TotalFeatureRows > 0; }
        protected override bool Supports(int type)
        { return type == (int)swDocumentTypes_e.swDocPART || type == (int)swDocumentTypes_e.swDocASSEMBLY; }
        protected override void OnPreparingPage()
        {
            table = new NativeCountHoleTable(result); previousPanelWidth = Model.GetFeatureManagerWidth();
            int minimum = (int)Math.Ceiling(440.0 * table.DeviceDpi / 96);
            if (previousPanelWidth > 0 && previousPanelWidth < minimum && Model.SetFeatureManagerWidth(minimum) == 0)
                expandedPanelWidth = Model.GetFeatureManagerWidth();
        }
        protected override void BuildControls()
        {
            var summary = Group(1, "Kết quả thống kê");
            Label(summary, 2, "Loại lỗ: " + result.Summary.Count + " · Tổng SL: " + result.TotalQuantity + " · Feature: " + result.TotalFeatureRows, 16);
            var window = (PropertyManagerPageWindowFromHandle)Control(summary, 3, swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "Tổng hợp và chi tiết", "Chuyển tab Chi tiết để xem component, cấu hình và feature. Kéo thanh ngang để xem các cột.");
            window.Height = 245;
            if (!window.SetWindowHandlex64(table.Handle.ToInt64())) throw new InvalidOperationException("Không mở được bảng Đếm Hole.");
            Button(summary, 4, "Quét lại model (Refresh)");
            var output = Group(10, "Kết quả xuất");
            var exportBox = (PropertyManagerPageCheckbox)Control(output, 11, swPropertyManagerPageControlType_e.swControlType_Checkbox, "Xuất Excel khi bấm ✓ (tổng hợp + chi tiết)");
            exportBox.Checked = shouldExport;
        }
        protected override string HelpText
        { get { return "Assembly: thống kê theo từng occurrence và cấu hình.\n✓ hoàn tất (xuất Excel nếu đang tick); ✕ đóng và không xuất Excel."; } }
        public override void OnButtonPress(int id) { Handle(() => { if (id == 4) { refreshing = true; CloseOkay(); } }); }
        public override void OnCheckboxCheck(int id, bool value) { Handle(() => { if (id == 11) shouldExport = value; }); }
        protected override bool Validate(out string message)
        {
            message = result.TotalFeatureRows == 0 ? "Không tìm thấy lỗ. ✓ đóng bảng; Refresh để quét lại." : "Đã thống kê " + result.TotalQuantity + " lỗ. ✓ hoàn tất; ✕ không xuất Excel.";
            return refreshing || !shouldExport || result.TotalFeatureRows > 0;
        }
        protected override Action CaptureAccepted()
        {
            string message; if (!Validate(out message)) throw new InvalidOperationException(message);
            if (refreshing) return refresh; bool capturedExport = shouldExport;
            return () => { if (capturedExport) export(); };
        }
        protected override void OnReleased(bool accepted)
        {
            if (table != null) { table.Release(); table.Dispose(); table = null; }
            if (previousPanelWidth > 0 && expandedPanelWidth > 0 && Same(App.ActiveDoc, Model) && Model.GetFeatureManagerWidth() == expandedPanelWidth)
                Model.SetFeatureManagerWidth(previousPanelWidth);
        }
    }
}
