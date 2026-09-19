using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace ADDIN.UI
{
    public static class GridStyler
    {
        /// <summary>
        /// Nâng cấp giao diện DataGridView theo phong cách CAD Engineering chuyên nghiệp:
        /// - Viền ô vuông 1px sắc nét (Single Cell & Header borders)
        /// - Header màu xám bạc ánh xanh, chữ xanh CAD Cyan/Blue đậm nét
        /// - Chiều cao dòng gọn gàng, căn chỉnh hợp lý cho từng loại dữ liệu
        /// - Chống giật lag khi cuộn (DoubleBuffered)
        /// </summary>
        public static void ApplyModernStyle(DataGridView grid)
        {
            if (grid == null) return;

            // Bật bộ đệm đôi chống nhấp nháy khi cuộn danh sách
            EnableDoubleBuffer(grid);

            // Khung viền & lưới ô vuông sắc sảo kiểu bảng vẽ kỹ thuật
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            grid.AdvancedCellBorderStyle.All = DataGridViewAdvancedCellBorderStyle.Single;
            grid.BackgroundColor = Color.FromArgb(242, 245, 248);
            grid.GridColor = Color.FromArgb(185, 195, 206);
            grid.RowHeadersVisible = false;
            grid.EnableHeadersVisualStyles = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToOrderColumns = true;

            Font headerFont = new Font("Segoe UI", 9F, FontStyle.Bold);
            Font cellFont = new Font("Segoe UI", 9F, FontStyle.Regular);
            grid.Font = cellFont;

            // Thiết kế Header: nền xám bạc nhạt, chữ xanh CAD đậm, căn giữa
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 244, 248);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(0, 115, 230);
            grid.ColumnHeadersDefaultCellStyle.Font = headerFont;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(240, 244, 248);
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(0, 115, 230);
            grid.ColumnHeadersHeight = 26;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

            // Thiết kế nội dung Cell
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(25, 30, 36);
            grid.DefaultCellStyle.Font = cellFont;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(215, 235, 255);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(10, 50, 110);
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;

            // Hàng xen kẽ: màu trắng pha xám bạc dịu mắt, dễ theo dõi hàng ngang
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Color.FromArgb(25, 30, 36);
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Color.FromArgb(215, 235, 255);
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.FromArgb(10, 50, 110);
            grid.AlternatingRowsDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);

            grid.RowTemplate.Height = 24;

            // Tự động định dạng các cột theo chuẩn bảng BOM kỹ thuật
            ConfigureColumns(grid);
        }

        public static void ConfigureColumns(DataGridView grid)
        {
            if (grid == null || grid.Columns.Count == 0) return;

            foreach (DataGridViewColumn col in grid.Columns)
            {
                col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            // Cột Checkbox (cột 0)
            if (grid.Columns.Count > 0 && grid.Columns[0] is DataGridViewCheckBoxColumn)
            {
                grid.Columns[0].Width = 36;
                grid.Columns[0].MinimumWidth = 32;
                grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                grid.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns[0].HeaderText = "✔";
            }

            // Cột dữ liệu BOM tiêu chuẩn
            if (grid.Columns.Count >= 6)
            {
                grid.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                grid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns[4].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns[5].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            }
        }

        private static void EnableDoubleBuffer(DataGridView grid)
        {
            try
            {
                typeof(DataGridView).InvokeMember(
                    "DoubleBuffered",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                    null,
                    grid,
                    new object[] { true });
            }
            catch
            {
            }
        }
    }
}
