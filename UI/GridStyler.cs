using System.Drawing;
using System.Windows.Forms;

namespace ADDIN.UI
{
    public static class GridStyler
    {
        public static void ApplyModernStyle(DataGridView grid)
        {
            if (grid == null) return;

            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.AdvancedCellBorderStyle.All = DataGridViewAdvancedCellBorderStyle.None;
            grid.AdvancedCellBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single;
            grid.BackgroundColor = Color.White;
            grid.GridColor = Color.FromArgb(235, 235, 235);
            grid.RowHeadersVisible = false;
            grid.EnableHeadersVisualStyles = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AllowUserToResizeRows = false;

            Font headerFont = new Font("Segoe UI", 9F, FontStyle.Bold);
            Font cellFont = new Font("Segoe UI", 9F, FontStyle.Regular);
            grid.Font = cellFont;

            // Khóa WrapMode để Header không bị rớt dòng khổng lồ
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(250, 249, 248);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(32, 31, 30);
            grid.ColumnHeadersDefaultCellStyle.Font = headerFont;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False; 
            grid.ColumnHeadersHeight = 34;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(32, 31, 30);
            grid.DefaultCellStyle.Font = cellFont;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(237, 244, 250);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(0, 120, 212);
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False; 

            grid.RowTemplate.Height = 28;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(252, 253, 254);
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        }
    }
}
