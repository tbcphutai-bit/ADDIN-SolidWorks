using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ADDIN.UI
{
    public class ModernCard : Panel
    {
        private int borderRadius = 6;

        [Category("Appearance")]
        [Description("Bán kính bo tròn góc")]
        public int BorderRadius
        {
            get => borderRadius;
            set
            {
                borderRadius = Math.Max(0, value);
                UpdateRegion();
                Invalidate();
            }
        }

        [Category("Appearance")]
        [Description("Màu viền thẻ")]
        public Color BorderColor { get; set; } = Color.FromArgb(215, 220, 228);

        [Category("Appearance")]
        [Description("Màu chữ tiêu đề")]
        public Color HeaderTextColor { get; set; } = Color.FromArgb(30, 48, 70);

        [Category("Appearance")]
        [Description("Màu vạch điểm nhấn tiêu đề")]
        public Color AccentColor { get; set; } = Color.FromArgb(0, 110, 210);

        [Category("Appearance")]
        [Description("Hiển thị đường gạch ngang dưới tiêu đề")]
        public bool ShowHeaderDivider { get; set; } = true;

        public ModernCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            BackColor = Color.FromArgb(248, 249, 251);
            Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (this.Region != null)
            {
                this.Region.Dispose();
                this.Region = null;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent != null && Parent.BackColor.A > 0)
                ? Parent.BackColor
                : Color.FromArgb(240, 243, 246);
            g.Clear(parentBg);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UIHelper.GetRoundPath(rect, borderRadius))
            {
                using (var brush = new SolidBrush(BackColor))
                {
                    g.FillPath(brush, path);
                }
                using (var pen = new Pen(BorderColor, 1F))
                {
                    g.DrawPath(pen, path);
                }
            }

            // Vẽ tiêu đề thẻ nếu có
            if (!string.IsNullOrEmpty(Text))
            {
                // Vạch chỉ báo nhấn dọc nhỏ bên trái tiêu đề
                int accentX = 12;
                int accentY = 8;
                int accentW = 3;
                int accentH = 14;
                using (var brush = new SolidBrush(AccentColor))
                {
                    g.FillRectangle(brush, accentX, accentY, accentW, accentH);
                }

                // Tiêu đề
                using (var headerFont = new Font("Segoe UI", 8.75F, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, Text, headerFont, new Point(accentX + accentW + 6, accentY - 1), HeaderTextColor,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
                }

                if (ShowHeaderDivider)
                {
                    using (var linePen = new Pen(Color.FromArgb(226, 231, 238), 1F))
                    {
                        g.DrawLine(linePen, 12, accentY + accentH + 6, Width - 12, accentY + accentH + 6);
                    }
                }
            }
        }
    }
}
