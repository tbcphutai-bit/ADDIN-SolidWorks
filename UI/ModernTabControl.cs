using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ADDIN.UI
{
    public class ModernTabControl : TabControl
    {
        [Category("Appearance")]
        [Description("Màu đường chỉ báo gạch chân tab đang chọn")]
        public Color ActiveTabColor { get; set; } = Color.FromArgb(0, 114, 206);

        [Category("Appearance")]
        [Description("Màu chữ tab đang chọn")]
        public Color ActiveTextColor { get; set; } = Color.FromArgb(15, 25, 45);

        [Category("Appearance")]
        [Description("Màu chữ tab không chọn")]
        public Color InactiveTextColor { get; set; } = Color.FromArgb(100, 115, 135);

        [Category("Appearance")]
        [Description("Màu nền thanh tab")]
        public Color HeaderBackColor { get; set; } = Color.FromArgb(232, 236, 242);

        [Category("Appearance")]
        [Description("Màu đường phân cách")]
        public Color BorderColor { get; set; } = Color.FromArgb(215, 222, 230);

        public ModernTabControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Normal;
            ItemSize = new Size(110, 30);
            Padding = new Point(14, 6);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= TabCount)
                return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            TabPage page = TabPages[e.Index];
            Rectangle rect = GetTabRect(e.Index);
            bool isSelected = (SelectedIndex == e.Index);

            // Nền tab phẳng dịu mắt đồng bộ với trang Task Pane
            Color backColor = isSelected ? Color.FromArgb(240, 243, 246) : HeaderBackColor;
            using (var brush = new SolidBrush(backColor))
            {
                g.FillRectangle(brush, rect);
            }

            // Đường chỉ báo tab đang chọn (Active Indicator Underline)
            if (isSelected)
            {
                int indicatorHeight = 3;
                Rectangle indicatorRect = new Rectangle(rect.X + 4, rect.Bottom - indicatorHeight, rect.Width - 8, indicatorHeight);
                using (var brush = new SolidBrush(ActiveTabColor))
                {
                    g.FillRectangle(brush, indicatorRect);
                }
            }

            // Chữ tiêu đề tab
            Font tabFont = isSelected
                ? new Font("Segoe UI", 9F, FontStyle.Bold)
                : new Font("Segoe UI", 9F, FontStyle.Regular);

            Color textColor = isSelected ? ActiveTextColor : InactiveTextColor;

            Rectangle textRect = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height - (isSelected ? 3 : 0));
            TextRenderer.DrawText(g, page.Text, tabFont, textRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            tabFont.Dispose();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // 0x000F is WM_PAINT
            if (m.Msg == 0x000F && TabCount > 0)
            {
                try
                {
                    using (Graphics g = Graphics.FromHwnd(Handle))
                    {
                        Rectangle firstTab = GetTabRect(0);
                        int lineY = firstTab.Bottom - 1;
                        using (var pen = new Pen(BorderColor, 1F))
                        {
                            g.DrawLine(pen, 0, lineY, Width, lineY);
                        }
                    }
                }
                catch
                {
                    // Suppress any GDI handle issues on shutdown
                }
            }
        }
    }
}
