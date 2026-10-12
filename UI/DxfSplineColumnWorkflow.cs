using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands.DrawingDxfExporting;

namespace ADDIN
{
    // Per-row choice for XUẤT DXF, shown as two tick boxes of which exactly one is ticked:
    // POLY (the default) saves curves as POLYLINE, SPL saves them as SPLINE.
    public partial class BomTaskPaneControl
    {
        private const string DxfPolylineColumnName = "colDxfPolyline";
        private HashSet<int> pendingDxfCurveRows;
        private int dxfCurveDownRow = -1, dxfCurveDownColumn = -1;

        private void EnsureDxfSplineColumn()
        {
            if (dgvModelBom == null || dgvModelBom.Columns.Contains(DrawingBomDxfExport.SplineColumnName)) return;
            DataGridViewCheckBoxColumn polyline = DxfCurveColumn(DxfPolylineColumnName, "POLY",
                "XUẤT DXF: lưu đường cong thành POLYLINE (mặc định).");
            // A row nobody touched shows POLY ticked. The export reads only the SPL box: empty = POLYLINE.
            polyline.DefaultCellStyle.NullValue = true;
            dgvModelBom.Columns.Add(polyline);
            dgvModelBom.Columns.Add(DxfCurveColumn(DrawingBomDxfExport.SplineColumnName, "SPL",
                "XUẤT DXF: lưu đường cong thành SPLINE."));
            dgvModelBom.CellMouseDown += DxfCurveColumn_CellMouseDown;
            dgvModelBom.CellMouseUp += DxfCurveColumn_CellMouseUp;
        }

        // Read-only: the pair is ticked here, on a click anywhere in the cell. The built-in tick reacts only to
        // its small glyph, and on a quick second click (a double-click) it toggles one box without telling
        // CellContentClick, which left both boxes empty or both ticked.
        private static DataGridViewCheckBoxColumn DxfCurveColumn(string name, string header, string tip)
        {
            var column = new DataGridViewCheckBoxColumn
            {
                Name = name, HeaderText = header, Width = 42, MinimumWidth = 38, ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Resizable = DataGridViewTriState.False,
                SortMode = DataGridViewColumnSortMode.NotSortable, TrueValue = true, FalseValue = false, ToolTipText = tip
            };
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            return column;
        }

        private bool IsDxfCurveCell(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || columnIndex < 0 || rowIndex >= dgvModelBom.Rows.Count || dgvModelBom.Rows[rowIndex].IsNewRow) return false;
            string name = dgvModelBom.Columns[columnIndex].Name;
            return name == DrawingBomDxfExport.SplineColumnName || name == DxfPolylineColumnName;
        }

        // The click itself collapses a multi-row selection, so remember it before that happens.
        private void DxfCurveColumn_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            pendingDxfCurveRows = null;
            dxfCurveDownRow = dxfCurveDownColumn = -1;
            if (e.Button != MouseButtons.Left || !IsDxfCurveCell(e.RowIndex, e.ColumnIndex)) return;
            dxfCurveDownRow = e.RowIndex;
            dxfCurveDownColumn = e.ColumnIndex;
            var selected = new HashSet<int>(dgvModelBom.SelectedRows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => r.Index));
            if (selected.Count > 1 && selected.Contains(e.RowIndex)) pendingDxfCurveRows = selected;
        }

        // Every click on a box changes its tick, and the other box of the row always shows the opposite.
        // One click sets every row of the selection the clicked row belongs to.
        private void DxfCurveColumn_CellMouseUp(object sender, DataGridViewCellMouseEventArgs e)
        {
            HashSet<int> rows = pendingDxfCurveRows;
            bool sameCell = e.Button == MouseButtons.Left && e.RowIndex == dxfCurveDownRow && e.ColumnIndex == dxfCurveDownColumn;
            pendingDxfCurveRows = null;
            dxfCurveDownRow = dxfCurveDownColumn = -1;
            // A row drag that ends on a box is not a click on it.
            if (!sameCell || !IsDxfCurveCell(e.RowIndex, e.ColumnIndex)) return;
            DataGridViewRow clicked = dgvModelBom.Rows[e.RowIndex];
            bool spline = !Convert.ToBoolean(clicked.Cells[DrawingBomDxfExport.SplineColumnName].Value ?? false);
            foreach (int index in rows ?? new HashSet<int> { e.RowIndex })
            {
                if (index >= dgvModelBom.Rows.Count || dgvModelBom.Rows[index].IsNewRow) continue;
                dgvModelBom.Rows[index].Cells[DrawingBomDxfExport.SplineColumnName].Value = spline;
                dgvModelBom.Rows[index].Cells[DxfPolylineColumnName].Value = !spline;
            }
            if (rows == null) return;
            foreach (int index in rows)
                if (index < dgvModelBom.Rows.Count) dgvModelBom.Rows[index].Selected = true;
        }
    }
}
