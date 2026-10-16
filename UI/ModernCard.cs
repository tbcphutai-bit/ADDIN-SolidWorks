using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ADDIN.UI
{
    public enum ModernCardHeaderStyle
    {
        Standard,
        DarkNavy
    }

    // A white panel that groups related commands under one short title.
    public class ModernCard : Panel
    {
        private string badgeText = string.Empty;
        private ModernButtonTone badgeTone = ModernButtonTone.Neutral;
        private ModernCardHeaderStyle headerStyle = ModernCardHeaderStyle.DarkNavy;

        [Category("Appearance")]
        [Description("Phong cách thanh tiêu đề: Standard hoặc DarkNavy")]
        [DefaultValue(ModernCardHeaderStyle.DarkNavy)]
        public ModernCardHeaderStyle HeaderStyle
        {
            get { return headerStyle; }
            set { if (headerStyle == value) return; headerStyle = value; Invalidate(); }
        }

        [Category("Appearance")]
        [Description("Nhãn trạng thái ở góc phải tiêu đề")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string BadgeText
        {
            get { return badgeText; }
            set { value = value ?? string.Empty; if (badgeText == value) return; badgeText = value; Invalidate(); }
        }

        [Category("Appearance")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ModernButtonTone BadgeTone
        {
            get { return badgeTone; }
            set { if (badgeTone == value) return; badgeTone = value; Invalidate(); }
        }

        [Category("Appearance")]
        [Description("Kẻ một đường mảnh dưới tiêu đề")]
        [DefaultValue(false)]
        public bool ShowHeaderDivider { get; set; }

        // Appearance properties from before TaiTheme; Designer code still assigns
        // them, the theme now decides the colours.
        [Browsable(false)] public int BorderRadius { get; set; } = TaiTheme.CardRadius;
        [Browsable(false)] public Color BorderColor { get; set; } = TaiTheme.Border;
        [Browsable(false)] public Color HeaderTextColor { get; set; } = TaiTheme.Text;
        [Browsable(false)] public Color AccentColor { get; set; } = TaiTheme.Accent;
        [Browsable(false)] [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BadgeBackColor { get; set; } = TaiTheme.Sunken;
        [Browsable(false)] [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BadgeBorderColor { get; set; } = TaiTheme.Border;
        [Browsable(false)] [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BadgeTextColor { get; set; } = TaiTheme.TextMuted;

        public ModernCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            BackColor = TaiTheme.Surface;
            Font = TaiTheme.BodyStrong;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        // Children clear their corners with BackColor, so the card surface is fixed.
        public override Color BackColor
        {
            get { return TaiTheme.Surface; }
            set { base.BackColor = TaiTheme.Surface; }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int targetWidth = proposedSize.Width > 0 ? proposedSize.Width : Width;
            int contentWidth = Math.Max(1, targetWidth - Padding.Horizontal);
            int maxBottom = 0;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                int childH = c.AutoSize ? c.GetPreferredSize(new Size(contentWidth, 0)).Height : c.Height;
                int bottom = (c.Dock == DockStyle.Top ? Padding.Top : c.Top) + childH;
                if (bottom > maxBottom) maxBottom = bottom;
            }
            int minHeight = TaiTheme.Px(this, TaiTheme.CardHeader);
            int height = Math.Max(minHeight, maxBottom + Padding.Bottom);
            return new Size(targetWidth, height);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (AutoSize && AutoSizeMode == AutoSizeMode.GrowAndShrink)
            {
                int pref = GetPreferredSize(new Size(Width, 0)).Height;
                if (Height != pref) Height = pref;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);
            g.Clear(TaiTheme.BackdropOf(this));

            Rectangle frame = new Rectangle(0, 0, Width - 1, Height - 1);
            int rad = TaiTheme.Px(this, BorderRadius);

            // 1. Draw card background & border
            using (GraphicsPath path = UIHelper.GetRoundPath(frame, rad))
            {
                using (SolidBrush sb = new SolidBrush(TaiTheme.Surface)) g.FillPath(sb, path);
                using (Pen pen = new Pen(TaiTheme.Border, 1f)) g.DrawPath(pen, path);
            }

            // 2. Draw Dark Navy Header if enabled
            if (headerStyle == ModernCardHeaderStyle.DarkNavy && !string.IsNullOrEmpty(Text))
            {
                int headerH = TaiTheme.Px(this, 34);
                using (GraphicsPath topPath = UIHelper.GetRoundPath(new Rectangle(0, 0, Width - 1, headerH * 2), rad))
                {
                    g.SetClip(new Rectangle(0, 0, Width, headerH));
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(30, 41, 59))) // Slate-800
                        g.FillPath(sb, topPath);
                    g.ResetClip();
                }

                // Cyan accent indicator
                int pillX = TaiTheme.Px(this, 12);
                int pillH = TaiTheme.Px(this, 14);
                int pillY = (headerH - pillH) / 2;
                int pillW = TaiTheme.Px(this, 3);
                using (SolidBrush pb = new SolidBrush(Color.FromArgb(56, 189, 248))) // Cyan-400
                    g.FillRectangle(pb, pillX, pillY, pillW, pillH);

                int textX = pillX + pillW + TaiTheme.Px(this, 7);
                int rightLimit = Width - TaiTheme.Px(this, 12);
                foreach (Control child in Controls)
                {
                    if (child.Visible && child.Top < headerH && child.Bottom <= headerH + TaiTheme.Px(this, 6))
                    {
                        if (child.Left < rightLimit)
                            rightLimit = Math.Min(rightLimit, child.Left - TaiTheme.Px(this, 6));
                    }
                }

                if (badgeText.Length > 0)
                {
                    int bWidth = TextRenderer.MeasureText(g, badgeText, TaiTheme.Small, Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + TaiTheme.Px(this, 14);
                    Rectangle pill = new Rectangle(rightLimit - bWidth, (headerH - TaiTheme.Px(this, 18)) / 2, bWidth, TaiTheme.Px(this, 18));
                    Color bbFill, bbText;
                    switch (badgeTone)
                    {
                        case ModernButtonTone.Success:
                            bbFill = Color.FromArgb(20, 83, 45);
                            bbText = Color.FromArgb(187, 247, 208);
                            break;
                        case ModernButtonTone.Danger:
                            bbFill = Color.FromArgb(127, 29, 29);
                            bbText = Color.FromArgb(254, 202, 202);
                            break;
                        case ModernButtonTone.Warning:
                            bbFill = Color.FromArgb(120, 53, 15);
                            bbText = Color.FromArgb(254, 215, 170);
                            break;
                        case ModernButtonTone.Accent:
                            bbFill = Color.FromArgb(30, 58, 138);
                            bbText = Color.FromArgb(191, 219, 254);
                            break;
                        default:
                            bbFill = Color.FromArgb(51, 65, 85);
                            bbText = Color.FromArgb(226, 232, 240);
                            break;
                    }
                    using (SolidBrush bb = new SolidBrush(bbFill))
                        g.FillRectangle(bb, pill);
                    TextRenderer.DrawText(g, badgeText, TaiTheme.Small, pill, bbText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    rightLimit = pill.X - TaiTheme.Px(this, 8);
                }

                int maxW = Math.Max(1, rightLimit - textX);
                using (Font titleFont = new Font("Segoe UI", 9.0f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, Text, titleFont,
                        new Rectangle(textX, 0, maxW, headerH),
                        Color.White,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
                return;
            }

            // Standard Header Painting
            int pad = TaiTheme.Px(this, TaiTheme.CardPadding), top = TaiTheme.Px(this, 7), line = TaiTheme.Px(this, 22);
            int right = Width - pad;

            if (badgeText.Length > 0)
            {
                Color text, fill;
                switch (badgeTone)
                {
                    case ModernButtonTone.Accent: text = TaiTheme.Accent; fill = TaiTheme.AccentSoft; break;
                    case ModernButtonTone.Success: text = TaiTheme.Success; fill = TaiTheme.SuccessSoft; break;
                    case ModernButtonTone.Danger: text = TaiTheme.Danger; fill = TaiTheme.DangerSoft; break;
                    case ModernButtonTone.Warning: text = TaiTheme.Warning; fill = TaiTheme.WarningSoft; break;
                    default: text = TaiTheme.TextMuted; fill = TaiTheme.Sunken; break;
                }
                int width = TextRenderer.MeasureText(g, badgeText, TaiTheme.Small, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + TaiTheme.Px(this, 14);
                Rectangle pill = new Rectangle(right - width, top, width, line);
                TaiTheme.FillRound(g, new Rectangle(pill.X, pill.Y, pill.Width - 1, pill.Height - 1), pill.Height / 2, fill, Color.Empty);
                TextRenderer.DrawText(g, badgeText, TaiTheme.Small, pill, text, TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                right = pill.X - TaiTheme.Px(this, 8);
            }

            if (!string.IsNullOrEmpty(Text))
                TextRenderer.DrawText(g, Text, TaiTheme.BodyStrong, new Rectangle(pad, top, Math.Max(1, right - pad), line), TaiTheme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

            if (ShowHeaderDivider && !string.IsNullOrEmpty(Text))
                using (Pen pen = new Pen(TaiTheme.Divider, 1F))
                    g.DrawLine(pen, pad, TaiTheme.Px(this, TaiTheme.CardHeader) - 1, Width - pad, TaiTheme.Px(this, TaiTheme.CardHeader) - 1);
        }
    }
}
