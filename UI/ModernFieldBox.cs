using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ADDIN.UI
{
    // A caption at the left of the input it hosts.
    public class ModernFieldBox : Panel
    {
        [Category("Appearance")]
        [Description("Nhãn mô tả trường nhập")]
        public string LabelText { get; set; } = "";

        public ModernFieldBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            string text = !string.IsNullOrEmpty(LabelText) ? LabelText : Text;
            if (string.IsNullOrEmpty(text)) return;
            TextRenderer.DrawText(e.Graphics, text, TaiTheme.Body, new Rectangle(TaiTheme.Px(this, 2), 0, TaiTheme.Px(this, 50), Height), TaiTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// Outlined fieldset box with a floating title sitting on top border (XToolPro style).
    /// </summary>
    public class OutlineFieldBox : Panel
    {
        private string title = string.Empty;

        [Category("Appearance")]
        [Description("Tiêu đề nổi trên mép viền trên")]
        public string Title
        {
            get => title;
            set { title = value ?? string.Empty; Invalidate(); }
        }

        public OutlineFieldBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
                ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            BackColor = TaiTheme.Surface;
            Font = TaiTheme.SmallStrong;
            Padding = new Padding(6, 12, 6, 6);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            int pad = TaiTheme.Px(this, 1);
            int topOffset = TaiTheme.Px(this, 7);
            Rectangle borderRect = new Rectangle(pad, topOffset, Width - pad * 2 - 1, Height - topOffset - pad - 1);

            // Draw rounded border
            using (GraphicsPath path = UIHelper.GetRoundPath(borderRect, TaiTheme.Px(this, 4)))
            using (Pen pen = new Pen(TaiTheme.BorderStrong, 1f))
            {
                g.DrawPath(pen, path);
            }

            if (!string.IsNullOrEmpty(title))
            {
                Size textSize = TextRenderer.MeasureText(g, title, Font, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                int textWidth = textSize.Width + TaiTheme.Px(this, 12);

                int labelX = TaiTheme.Px(this, 10);
                Rectangle clearRect = new Rectangle(labelX, 0, textWidth, topOffset * 2);
                Color bg = Parent != null && Parent.BackColor.A == 255 ? Parent.BackColor : BackColor;
                using (SolidBrush brush = new SolidBrush(bg))
                {
                    g.FillRectangle(brush, clearRect);
                }

                Rectangle textRect = new Rectangle(labelX + TaiTheme.Px(this, 6), 0, textSize.Width + TaiTheme.Px(this, 8), topOffset * 2);
                TextRenderer.DrawText(g, title, Font, textRect, TaiTheme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping);
            }
        }
    }
}

