using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ADDIN.UI
{
    public class ModernButton : Button
    {
        [Category("Appearance")]
        [Description("Bán kính bo tròn góc (pixel)")]
        public int BorderRadius { get; set; } = 4;

        [Browsable(false)]
        public new bool UseVisualStyleBackColor
        {
            get => false;
            set { base.UseVisualStyleBackColor = false; }
        }

        [Browsable(false)]
        public new FlatStyle FlatStyle
        {
            get => FlatStyle.Flat;
            set { base.FlatStyle = FlatStyle.Flat; }
        }

        [Category("Appearance")]
        [Description("Màu nền trạng thái bình thường")]
        public Color NormalColor { get; set; } = Color.FromArgb(248, 249, 250);

        [Category("Appearance")]
        [Description("Màu nền khi rê chuột (Hover)")]
        public Color HoverColor { get; set; } = Color.FromArgb(235, 240, 246);

        [Category("Appearance")]
        [Description("Màu nền khi nhấn giữ chuột (Pressed)")]
        public Color PressColor { get; set; } = Color.FromArgb(220, 230, 240);

        [Category("Appearance")]
        [Description("Màu viền bo góc")]
        public Color BorderColor { get; set; } = Color.FromArgb(190, 195, 200);

        private bool isHovered = false;
        private bool isPressed = false;

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            UseVisualStyleBackColor = false;
            ForeColor = Color.FromArgb(32, 31, 30);
            Font = new Font("Segoe UI", 9.0F, FontStyle.Bold);
            Cursor = Cursors.Hand;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Force the region to the full client rectangle so Button base class doesn't clip corners!
            if (this.Region != null)
            {
                this.Region.Dispose();
                this.Region = null;
            }
        }

        // 1. NGỤY TRANG GÓC ĐEN TRONG SOLIDWORKS
        protected override bool ShowFocusCues => false;

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Traverse parents to find the actual solid background color
            Color bgColor = Color.White; // Default to White for SolidWorks TaskPane
            Control p = this.Parent;
            while (p != null)
            {
                if (p.BackColor != Color.Transparent && p.BackColor.A > 0)
                {
                    bgColor = p.BackColor;
                    // Skip generic control color if possible to find the real tab/panel color
                    if (bgColor != SystemColors.Control && bgColor.Name != "0")
                        break;
                }
                p = p.Parent;
            }
            pevent.Graphics.Clear(bgColor);
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { isPressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { isPressed = false; Invalidate(); base.OnMouseUp(mevent); }

        // 2. VẼ NÚT VÀ CĂN CHỈNH TEXT CHỐNG ĐÈ ICON
        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color current = !Enabled ? Color.FromArgb(200, 198, 196) : (isPressed ? PressColor : (isHovered ? HoverColor : NormalColor));
            
            Rectangle rect = ClientRectangle;
            using (var path = UIHelper.GetRoundPath(rect, BorderRadius))
            {
                using (var brush = new SolidBrush(current)) g.FillPath(brush, path);
                using (var pen = new Pen(BorderColor, 1F)) g.DrawPath(pen, path);
            }

            Rectangle imgRect = Rectangle.Empty;
            Rectangle txtRect = ClientRectangle;

            if (Image != null)
            {
                int imgW = Image.Width, imgH = Image.Height;
                if (TextImageRelation == TextImageRelation.ImageAboveText)
                {
                    imgRect = new Rectangle(rect.X + (rect.Width - imgW) / 2, rect.Y + Padding.Top + 6, imgW, imgH);
                    txtRect = new Rectangle(rect.X, imgRect.Bottom + 2, rect.Width, rect.Height - (imgRect.Bottom - rect.Y) - 2);
                }
                else if (TextImageRelation == TextImageRelation.ImageBeforeText)
                {
                    imgRect = new Rectangle(rect.X + Padding.Left + 6, rect.Y + (rect.Height - imgH) / 2, imgW, imgH);
                    txtRect = new Rectangle(imgRect.Right + 4, rect.Y, rect.Width - (imgRect.Right - rect.X) - 4, rect.Height);
                }
                else
                {
                    imgRect = new Rectangle(rect.X + (rect.Width - imgW) / 2, rect.Y + (rect.Height - imgH) / 2, imgW, imgH);
                }
                g.DrawImage(Image, imgRect);
            }

            if (!string.IsNullOrEmpty(Text))
            {
                Color txtColor = Enabled ? ForeColor : Color.FromArgb(161, 159, 157);
                TextRenderer.DrawText(g, Text, Font, txtRect, txtColor, 
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
        }
    }
}
