using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ADDIN.UI
{
    public class ModernButton : Button
    {
        private int borderRadius = 5;

        [Category("Appearance")]
        [Description("Bán kính bo tròn góc (pixel)")]
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
        public Color NormalColor { get; set; } = Color.FromArgb(246, 248, 250);

        [Category("Appearance")]
        [Description("Màu nền khi rê chuột (Hover)")]
        public Color HoverColor { get; set; } = Color.FromArgb(234, 240, 247);

        [Category("Appearance")]
        [Description("Màu nền khi nhấn giữ chuột (Pressed)")]
        public Color PressColor { get; set; } = Color.FromArgb(220, 228, 238);

        [Category("Appearance")]
        [Description("Màu viền bo góc")]
        public Color BorderColor { get; set; } = Color.FromArgb(210, 216, 224);

        private bool isHovered = false;
        private bool isPressed = false;

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.BorderColor = Color.FromArgb(0, 255, 255, 255);
            BackColor = Color.Transparent;
            UseVisualStyleBackColor = false;
            ForeColor = Color.FromArgb(30, 41, 59);
            Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            Cursor = Cursors.Hand;
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

        protected override bool ShowFocusCues => false;

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            Color bgColor = Color.Transparent;
            Control p = this.Parent;
            while (p != null)
            {
                if (p.BackColor != Color.Transparent && p.BackColor.A > 0)
                {
                    bgColor = p.BackColor;
                    break;
                }
                p = p.Parent;
            }

            if (bgColor.A == 0)
                bgColor = Color.FromArgb(248, 249, 251);

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
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Xóa sạch nền bằng màu nền của container cha để 4 góc bo mịn tuyệt đối, không đọng viền đen
            Color bgColor = Color.Transparent;
            Control p = this.Parent;
            while (p != null)
            {
                if (p.BackColor != Color.Transparent && p.BackColor.A > 0)
                {
                    bgColor = p.BackColor;
                    break;
                }
                p = p.Parent;
            }

            if (bgColor.A == 0)
                bgColor = Color.FromArgb(248, 249, 251);

            g.Clear(bgColor);

            Color current = !Enabled ? Color.FromArgb(244, 245, 247) : (isPressed ? PressColor : (isHovered ? HoverColor : NormalColor));
            Color border = !Enabled ? Color.FromArgb(224, 227, 232) : BorderColor;
            
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UIHelper.GetRoundPath(rect, borderRadius))
            {
                using (var brush = new SolidBrush(current))
                    g.FillPath(brush, path);
                using (var pen = new Pen(border, 1F))
                    g.DrawPath(pen, path);
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

                if (!Enabled)
                {
                    ControlPaint.DrawImageDisabled(g, Image, imgRect.X, imgRect.Y, current);
                }
                else
                {
                    g.DrawImage(Image, imgRect);
                }
            }

            if (!string.IsNullOrEmpty(Text))
            {
                Color txtColor = Enabled ? ForeColor : Color.FromArgb(160, 165, 175);
                TextRenderer.DrawText(g, Text, Font, txtRect, txtColor, 
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
        }
    }
}
