using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace ADDIN.UI
{
    public static class GridStyler
    {
        private static readonly ConditionalWeakTable<DataGridView, string[]> emptyStates = new ConditionalWeakTable<DataGridView, string[]>();

        private sealed class ColumnFitState
        {
            internal bool Configuring;
        }

        private static readonly ConditionalWeakTable<DataGridView, ColumnFitState> columnFitStates =
            new ConditionalWeakTable<DataGridView, ColumnFitState>();

        // The BOM grid in the TaiTheme look: quiet header, hairline row
        // separators, accent selection and themed check boxes.
        public static void ApplyModernStyle(DataGridView grid)
        { ApplyModernStyle(grid, true); }

        // Non-BOM forms retain their own Designer column schema and widths.
        public static void ApplyModernStyle(DataGridView grid, bool fitBomColumns)
        {
            if (grid == null) return;
            EnableDoubleBuffer(grid);

            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.BackgroundColor = TaiTheme.Surface;
            grid.GridColor = TaiTheme.Divider;
            grid.RowHeadersVisible = false;
            grid.EnableHeadersVisualStyles = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToOrderColumns = true;
            grid.Font = TaiTheme.Body;

            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersDefaultCellStyle.BackColor = TaiTheme.SurfaceHover;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TaiTheme.TextMuted;
            grid.ColumnHeadersDefaultCellStyle.Font = TaiTheme.SmallStrong;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = TaiTheme.SurfaceHover;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TaiTheme.TextMuted;
            grid.ColumnHeadersHeight = HeaderHeight;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

            grid.DefaultCellStyle.BackColor = TaiTheme.Surface;
            grid.DefaultCellStyle.ForeColor = TaiTheme.Text;
            grid.DefaultCellStyle.Font = TaiTheme.Body;
            grid.DefaultCellStyle.SelectionBackColor = TaiTheme.AccentSoft;
            grid.DefaultCellStyle.SelectionForeColor = TaiTheme.Text;
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;

            grid.AlternatingRowsDefaultCellStyle.BackColor = TaiTheme.Surface;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = TaiTheme.Text;
            grid.AlternatingRowsDefaultCellStyle.Font = TaiTheme.Body;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = TaiTheme.AccentSoft;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = TaiTheme.Text;
            // No padding here: a row style would override the padding each column sets for itself.
            grid.AlternatingRowsDefaultCellStyle.Padding = Padding.Empty;

            grid.RowTemplate.Height = 28;

            grid.Paint -= PaintFrame;
            grid.Paint += PaintFrame;
            grid.CellPainting -= PaintCheckCell;
            grid.CellPainting += PaintCheckCell;
            grid.CellPainting -= PaintBomHeader;
            grid.CellPainting += PaintBomHeader;

            if (fitBomColumns) ConfigureColumns(grid);
        }

        private const int HeaderHeight = 40;

        // What an empty grid tells the user to do next.
        public static void SetEmptyState(DataGridView grid, string title, string hint)
        {
            if (grid == null) return;
            emptyStates.Remove(grid);
            emptyStates.Add(grid, new[] { title ?? string.Empty, hint ?? string.Empty });
            grid.Invalidate();
        }

        private static void PaintFrame(object sender, PaintEventArgs e)
        {
            DataGridView grid = sender as DataGridView;
            if (grid == null) return;
            using (Pen pen = new Pen(TaiTheme.Border, 1F))
                e.Graphics.DrawRectangle(pen, 0, 0, grid.Width - 1, grid.Height - 1);

            string[] empty;
            if (grid.Rows.Count > 0 || !emptyStates.TryGetValue(grid, out empty)) return;
            int top = grid.ColumnHeadersVisible ? grid.ColumnHeadersHeight : 0;
            Rectangle area = new Rectangle(TaiTheme.Px(grid, 16), top, Math.Max(1, grid.Width - TaiTheme.Px(grid, 32)), Math.Max(1, grid.Height - top));
            if (area.Height < TaiTheme.Px(grid, 70)) return;
            int glyph = TaiTheme.Px(grid, 28), line = TaiTheme.Px(grid, 18);
            int y = area.Y + Math.Max(TaiTheme.Px(grid, 8), Math.Min(TaiTheme.Px(grid, 70), (area.Height - glyph - line * 3) / 2));
            TaiIcons.Draw(e.Graphics, TaiIcon.Sheet, new Rectangle(area.X + (area.Width - glyph) / 2, y, glyph, glyph), TaiTheme.TextDisabled);
            y += glyph + TaiTheme.Px(grid, 8);
            const TextFormatFlags centre = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(e.Graphics, empty[0], TaiTheme.BodyStrong, new Rectangle(area.X, y, area.Width, line), TaiTheme.TextMuted, centre | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(e.Graphics, empty[1], TaiTheme.Small, new Rectangle(area.X, y + line + 2, area.Width, line * 3), TaiTheme.TextFaint, centre | TextFormatFlags.WordBreak);
        }

        private static void PaintCheckCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            DataGridView grid = sender as DataGridView;
            if (grid == null || e.RowIndex < 0 || e.ColumnIndex < 0 || !(grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn)) return;
            e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
            bool on = e.FormattedValue is bool ? (bool)e.FormattedValue
                : e.FormattedValue is CheckState && (CheckState)e.FormattedValue == CheckState.Checked;
            int size = TaiTheme.Px(grid, 16);
            Rectangle box = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - size) / 2, e.CellBounds.Y + (e.CellBounds.Height - size) / 2, size, size);
            System.Drawing.Drawing2D.GraphicsState state = e.Graphics.Save();
            try
            {
                TaiTheme.Prepare(e.Graphics);
                TaiTheme.FillRound(e.Graphics, new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1), TaiTheme.Px(grid, 4),
                    on ? TaiTheme.Accent : TaiTheme.Surface, on ? TaiTheme.Accent : TaiTheme.BorderStrong);
                if (on) TaiIcons.Draw(e.Graphics, TaiIcon.Check, Rectangle.Inflate(box, -1, -1), Color.White);
            }
            finally { e.Graphics.Restore(state); }
            e.Handled = true;
        }

        private static void PaintBomHeader(object sender, DataGridViewCellPaintingEventArgs e)
        {
            DataGridView grid = sender as DataGridView;
            if (grid == null || e.RowIndex != -1 || e.ColumnIndex < 1 || e.ColumnIndex > 5 || grid.Columns.Count < 6) return;
            e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            Rectangle text = Rectangle.Inflate(e.CellBounds, -2, 0);
            SortOrder order = column.HeaderCell.SortGlyphDirection;
            if (order != SortOrder.None)
            {
                int size = TaiTheme.Px(grid, 5);
                text.Width = Math.Max(1, text.Width - size - 2);
                int x = e.CellBounds.Right - size - 2, y = e.CellBounds.Top + (e.CellBounds.Height - size) / 2;
                Point[] triangle = order == SortOrder.Ascending
                    ? new[] { new Point(x, y + size), new Point(x + size / 2, y), new Point(x + size, y + size) }
                    : new[] { new Point(x, y), new Point(x + size / 2, y + size), new Point(x + size, y) };
                using (var brush = new SolidBrush(e.CellStyle.ForeColor)) e.Graphics.FillPolygon(brush, triangle);
            }
            // Native sortable headers reserve a large empty arrow area even
            // without a glyph. Use that space for narrow two-line BOM captions.
            TextRenderer.DrawText(e.Graphics, column.HeaderText, e.CellStyle.Font ?? grid.Font,
                text, e.CellStyle.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            e.Handled = true;
        }

        public static void ConfigureColumns(DataGridView grid)
        {
            if (grid == null || grid.IsDisposed || grid.Disposing) return;
            ColumnFitState state = columnFitStates.GetValue(grid, ConfigureColumnFitEvents);
            if (state.Configuring || grid.Columns.Count == 0) return;
            state.Configuring = true;
            grid.SuspendLayout();
            try
            {
                grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
                grid.ColumnHeadersHeight = HeaderHeight;
                grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
                grid.ShowCellToolTips = true;
                foreach (DataGridViewColumn col in grid.Columns)
                {
                    col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    col.HeaderCell.Style.WrapMode = DataGridViewTriState.True;
                    col.HeaderCell.Style.Padding = new Padding(1, 0, 1, 0);
                }
                if (grid.Columns[0] is DataGridViewCheckBoxColumn)
                {
                    grid.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    grid.Columns[0].HeaderText = "";
                }
                if (grid.Columns.Count < 6) return;

                grid.Columns[1].HeaderText = "部品\n番号";
                grid.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns[1].DefaultCellStyle.Padding = new Padding(2, 0, 2, 0);
                grid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                grid.Columns[2].DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
                foreach (int index in new[] { 3, 4 })
                {
                    grid.Columns[index].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    grid.Columns[index].DefaultCellStyle.Padding = new Padding(2, 0, 2, 0);
                }
                grid.Columns[5].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                grid.Columns[5].DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);

                FitBomColumns(grid);
                grid.Columns[5].HeaderText = grid.Columns[5].Width < TaiTheme.Px(grid, 64)
                    ? "ファイル名" : "部品ファイル名";
            }
            finally
            {
                grid.ResumeLayout(true);
                state.Configuring = false;
                grid.Invalidate();
            }
        }

        private static ColumnFitState ConfigureColumnFitEvents(DataGridView grid)
        {
            grid.SizeChanged += (sender, args) =>
            {
                RefitColumns(sender, args);
                (sender as DataGridView)?.Invalidate();
            };
            grid.Scroll += (sender, args) => (sender as DataGridView)?.Invalidate();
            grid.FontChanged += RefitColumns;
            grid.ColumnAdded += (sender, args) => RefitColumns(sender, EventArgs.Empty);
            grid.ColumnRemoved += (sender, args) => RefitColumns(sender, EventArgs.Empty);
            grid.ColumnStateChanged += (sender, args) =>
            {
                if ((args.StateChanged & DataGridViewElementStates.Visible) != 0)
                    RefitColumns(sender, EventArgs.Empty);
            };
            return new ColumnFitState();
        }

        private static void RefitColumns(object sender, EventArgs args)
        {
            ConfigureColumns(sender as DataGridView);
        }

        private static void FitBomColumns(DataGridView grid)
        {
            // Reserve the trailing export switches first. A Fill column's fixed
            // minimum cannot otherwise keep POLY/SPL inside a narrow viewport.
            int[] minimum = { 26, 32, 46, 28, 28, 44 };
            int[] widths = { 30, 42, 56, 40, 40, 80 };
            if (grid.Columns[1].HeaderCell.SortGlyphDirection != SortOrder.None) minimum[1] = 38;
            for (int i = 0; i < 6; i++)
            {
                grid.Columns[i].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                grid.Columns[i].MinimumWidth = 2;
                minimum[i] = TaiTheme.Px(grid, minimum[i]);
                widths[i] = TaiTheme.Px(grid, widths[i]);
            }
            widths[2] = Math.Max(widths[2], Math.Min(TaiTheme.Px(grid, 100),
                grid.Columns[2].GetPreferredWidth(DataGridViewAutoSizeColumnMode.DisplayedCellsExceptHeader, false)));
            int fixedWidth = 0;
            for (int i = 6; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn column = grid.Columns[i];
                if (column is DataGridViewCheckBoxColumn)
                {
                    Font font = column.HeaderCell.InheritedStyle.Font ?? grid.Font;
                    int labelWidth = TextRenderer.MeasureText(column.HeaderText, font, Size.Empty,
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                    int width = Math.Max(TaiTheme.Px(grid, 34), labelWidth + TaiTheme.Px(grid, 6));
                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    column.MinimumWidth = width;
                    column.Width = width;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    column.DefaultCellStyle.Padding = Padding.Empty;
                }
                if (column.Visible) fixedWidth += column.Width;
            }
            int available = grid.ClientSize.Width - 2;
            if (grid.BorderStyle != BorderStyle.None) available -= SystemInformation.BorderSize.Width * 2;
            if (grid.RowHeadersVisible) available -= grid.RowHeadersWidth;
            int bodyHeight = grid.ClientSize.Height - (grid.ColumnHeadersVisible ? grid.ColumnHeadersHeight : 0);
            if (grid.Rows.GetRowsHeight(DataGridViewElementStates.Visible) > bodyHeight &&
                (grid.ScrollBars == ScrollBars.Both || grid.ScrollBars == ScrollBars.Vertical))
                available -= SystemInformation.VerticalScrollBarWidth;

            int total = fixedWidth;
            for (int i = 0; i < 6; i++) if (grid.Columns[i].Visible) total += widths[i];
            int shortage = Math.Max(0, total - available);
            int[] shrinkOrder = { 5, 2, 1, 3, 4, 0 };
            foreach (int i in shrinkOrder)
            {
                if (!grid.Columns[i].Visible) continue;
                int reduction = Math.Min(shortage, widths[i] - minimum[i]);
                widths[i] -= reduction;
                shortage -= reduction;
            }
            // Very small hosted panes still prioritise the two clickable
            // switches; text stays available through the standard cell tooltip.
            foreach (int i in shrinkOrder)
            {
                if (shortage == 0) break;
                if (!grid.Columns[i].Visible) continue;
                int compactMinimum = TaiTheme.Px(grid, i == 0 ? 20 : 18);
                int reduction = Math.Min(shortage, Math.Max(0, widths[i] - compactMinimum));
                widths[i] -= reduction;
                shortage -= reduction;
            }
            for (int i = 0; i < 6; i++)
            {
                grid.Columns[i].MinimumWidth = Math.Max(2, Math.Min(minimum[i], widths[i]));
                grid.Columns[i].Width = widths[i];
            }
            // With all fixed widths budgeted, the file name takes the remaining
            // space and automatically gives it back when the scrollbar appears.
            if (grid.Columns[5].Visible)
                grid.Columns[5].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            if (shortage == 0 && grid.HorizontalScrollingOffset != 0)
                grid.HorizontalScrollingOffset = 0;
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
