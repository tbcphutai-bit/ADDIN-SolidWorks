using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SwView = SolidWorks.Interop.sldworks.View;

namespace ADDIN.UI
{
    internal sealed class NativeRepairDimensionTable : NativeResultTableHost
    {
        internal readonly DataGridView Targets = CreateGrid("RepairDimensionGrid", false);
        internal event Action<int, bool> Changed;
        private bool loading;
        private readonly TextBox detail = new TextBox { Dock = DockStyle.Bottom, Height = 78, Multiline = true,
            ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(244, 248, 253), Text = "Đang quét DIM dangling…" };
        internal NativeRepairDimensionTable()
        {
            Targets.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Repair", HeaderText = "Sửa", Width = 40, SortMode = DataGridViewColumnSortMode.NotSortable });
            Column(Targets, "View", "View", 100); Column(Targets, "Dimension", "DIM", 170);
            Column(Targets, "Type", "Loại DIM", 95); Column(Targets, "References", "Tham chiếu\ncòn", 75);
            var weights = new[] { 0f, 20f, 40f, 25f, 15f };
            var minimums = new[] { 40, 60, 100, 65, 50 };
            for (int i = 1; i < Targets.Columns.Count; i++) {
                Targets.Columns[i].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                Targets.Columns[i].FillWeight = weights[i]; Targets.Columns[i].MinimumWidth = minimums[i];
            }
            Controls.Add(Targets); Controls.Add(detail);
            Targets.CurrentCellDirtyStateChanged += (s, e) => { if (!Released && Targets.IsCurrentCellDirty) Targets.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            Targets.CellValueChanged += (s, e) => {
                if (!loading && !Released && e.RowIndex >= 0 && e.ColumnIndex == 0)
                    Changed?.Invoke(e.RowIndex, Convert.ToBoolean(Targets.Rows[e.RowIndex].Cells[0].Value));
            };
            Targets.CurrentCellChanged += (s, e) => ShowDetail();
        }
        internal void LoadRows(List<NativeRepairDimensionTarget> rows, bool[] selected, string status)
        {
            if (Released) return;
            loading = true;
            try {
                Targets.Rows.Clear();
                for (int i = 0; i < rows.Count; i++) {
                    var row = rows[i]; int index = Targets.Rows.Add(selected[i], row.ViewName,
                        string.IsNullOrEmpty(row.DimensionName) ? row.AnnotationName : row.DimensionName, DimensionLabel(row.DimensionType), row.RemainingReferences);
                    Targets.Rows[index].Tag = row;
                    foreach (DataGridViewCell cell in Targets.Rows[index].Cells) cell.ToolTipText = Convert.ToString(cell.Value);
                    Targets.Rows[index].Cells[3].ToolTipText = row.DimensionType;
                }
                detail.Text = status;
            } finally { loading = false; }
            ShowDetail();
        }
        internal void SetAll(bool value)
        {
            loading = true;
            try { foreach (DataGridViewRow row in Targets.Rows) row.Cells[0].Value = value; }
            finally { loading = false; }
        }
        private static string DimensionLabel(string type)
        {
            switch (type) {
                case "swDiameterDimension": return "Đường kính";
                case "swRadialDimension": return "Bán kính";
                case "swAngularDimension": return "Góc";
                case "swLinearDimension": return "Tuyến tính";
                case "swHorLinearDimension": return "Ngang";
                case "swVertLinearDimension": return "Đứng";
                case "swOrdinateDimension": return "Tọa độ";
                case "swHorOrdinateDimension": return "Tọa độ ngang";
                case "swVertOrdinateDimension": return "Tọa độ đứng";
                case "swArcLengthDimension": return "Độ dài cung";
                default: return type;
            }
        }
        private void ShowDetail()
        {
            if (loading || Released || Disposing || IsDisposed) return;
            var current = Targets.CurrentRow ?? (Targets.Rows.Count > 0 ? Targets.Rows[0] : null);
            var row = current == null ? null : current.Tag as NativeRepairDimensionTarget;
            if (row != null) detail.Text = "Sheet: " + row.SheetName + "\r\n" + row.Description + "\r\nAnnotation: " + row.AnnotationName;
        }
    }

    [ComVisible(true)] [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class RepairDimensionsPropertyManagerPage : NativeCommandPropertyManagerPage
    {
        private readonly Func<SwView, List<NativeRepairDimensionTarget>> scan;
        private readonly Action<List<NativeRepairDimensionTarget>> repair;
        private NativeRepairDimensionTable table;
        private List<NativeRepairDimensionTarget> rows = new List<NativeRepairDimensionTarget>();
        private bool[] selected = new bool[0];
        private SwView scope;
        private bool scanned, pending, scopeRequested;
        private int request, requestedCount, previousPanelWidth, expandedPanelWidth;
        internal RepairDimensionsPropertyManagerPage(ISldWorks app, Func<SwView, List<NativeRepairDimensionTarget>> scan,
            Action<List<NativeRepairDimensionTarget>> repair, Action<NativeCommandPropertyManagerPage> closed)
            : base(app, "REPAIR DIM", closed) { this.scan = scan; this.repair = repair; }
        protected override bool Supports(int type) { return type == (int)swDocumentTypes_e.swDocDRAWING; }
        protected override bool UsesSelectionBoxes { get { return true; } }
        protected override int SelectionMark(object item, int type)
        { return type == (int)swSelectType_e.swSelDRAWINGVIEWS && item is SwView ? 1 : 0; }
        protected override void OnPreparingPage()
        {
            table = new NativeRepairDimensionTable(); table.Changed += OnRowChanged;
            previousPanelWidth = Model.GetFeatureManagerWidth();
            int minimum = (int)Math.Ceiling(440.0 * table.DeviceDpi / 96);
            if (previousPanelWidth > 0 && previousPanelWidth < minimum && Model.SetFeatureManagerWidth(minimum) == 0)
                expandedPanelWidth = Model.GetFeatureManagerWidth();
        }
        protected override void BuildControls()
        {
            var source = Group(10, "1. View cần quét (để trống = cả Sheet)");
            Selection(source, 11, "View (để trống = Sheet đang mở)", 1, swSelectType_e.swSelDRAWINGVIEWS);
            Button(source, 12, "Quét lại");
            var targets = Group(20, "2. DIM cần sửa");
            var window = (PropertyManagerPageWindowFromHandle)Control(targets, 21,
                swPropertyManagerPageControlType_e.swControlType_WindowFromHandle, "DIM cần sửa", "Tick ô Sửa trong từng dòng; các cột còn lại chỉ để xem.");
            window.Height = 230;
            if (!window.SetWindowHandlex64(table.Handle.ToInt64())) throw new InvalidOperationException("Không mở được bảng REPAIR DIM.");
            Button(targets, 24, "Chọn tất cả"); Button(targets, 25, "Bỏ chọn tất cả");
        }
        protected override string HelpText
        {
            get
            {
                return "Danh sách tự cập nhật khi mở lệnh hoặc đổi View. Mặc định chưa tick DIM nào.\n" +
                    "✓ chỉ sửa các dòng đã tick; DIM thiếu tham chiếu chắc chắn sẽ được bỏ qua để kiểm tra thủ công.\n" +
                    "✕ không sửa bản vẽ.";
            }
        }
        protected override void OnShown() { RequestScan(); }
        private void RequestScan(bool force = true)
        {
            if (!Interactive) return;
            int count = Count(1);
            var requestedScope = count == 1 ? Selected(1) as SwView : null;
            // SolidWorks can report the same selection more than once. Keep
            // the pending/completed scan and checkbox draft for unchanged scope.
            if (!force && scopeRequested && count == requestedCount &&
                (scope == null ? requestedScope == null : Same(scope, requestedScope))) return;
            scopeRequested = true; requestedCount = count; scope = requestedScope;
            int generation = ++request;
            pending = true; scanned = false; rows.Clear(); selected = new bool[0];
            table.LoadRows(rows, selected, "Đang quét DIM dangling…"); UpdateReady();
            // Reading view geometry is deferred beyond the native selection callback.
            table.BeginInvoke((Action)(() => {
                if (!Interactive || generation != request) return;
                try {
                    Handle(() => {
                        if (requestedCount > 1) return;
                        var found = scan(requestedScope) ?? new List<NativeRepairDimensionTarget>();
                        if (!Interactive || generation != request) return;
                        if (requestedScope == null ? Count(1) != 0 : !Same(requestedScope, Selected(1)))
                            throw new InvalidOperationException("Phạm vi thay đổi khi quét; hãy quét lại.");
                        rows = found; selected = new bool[rows.Count]; scanned = true;
                        table.LoadRows(rows, selected, rows.Count == 0 ? "Không có DIM dangling trong phạm vi đã quét." : "Tick trực tiếp trong cột Sửa.");
                    });
                } finally {
                    if (generation == request) { pending = false; UpdateReady(); }
                }
            }));
        }
        private void OnRowChanged(int index, bool value)
        { Handle(() => { if (index >= 0 && index < selected.Length) selected[index] = value; }); }
        public override void OnButtonPress(int id)
        {
            if (id == 12) { RequestScan(); return; }
            Handle(() => {
                if ((id == 24 || id == 25) && scanned && !pending) {
                    for (int i = 0; i < selected.Length; i++) selected[i] = id == 24;
                    table.SetAll(id == 24);
                }
            });
        }
        public override void OnSelectionboxListChanged(int id, int count) { if (id == 11) RequestScan(false); }
        protected override bool Validate(out string message)
        {
            message = pending ? "Đang quét DIM dangling…" : "Chọn View hoặc quét lại Sheet đang mở.";
            if (pending || !scanned || Count(1) > 1 || (scope == null ? Count(1) != 0 : !Same(scope, Selected(1)))) return false;
            int count = selected.Count(x => x);
            message = rows.Count == 0 ? "Không có DIM dangling trong phạm vi này. ✕ đóng bảng." :
                count == 0 ? "Tick trong cột Sửa để chọn DIM." : "✓ sửa " + count + "/" + rows.Count + " DIM đã tick.";
            return count > 0;
        }
        protected override Action CaptureAccepted()
        {
            string message; if (!Validate(out message)) throw new InvalidOperationException(message);
            // Every checkbox change updates the cached draft before native teardown.
            var targets = rows.Where((r, i) => selected[i]).Select(r => r.Copy()).ToList(); return () => repair(targets);
        }
        protected override void OnReleased(bool accepted)
        {
            ++request;
            if (table != null) { table.Changed -= OnRowChanged; table.Release(); table.Dispose(); table = null; }
            if (previousPanelWidth > 0 && expandedPanelWidth > 0 && Same(App.ActiveDoc, Model) && Model.GetFeatureManagerWidth() == expandedPanelWidth)
                Model.SetFeatureManagerWidth(previousPanelWidth);
        }
    }
}
