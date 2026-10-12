using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace ADDIN.UI
{
    // A white panel that groups related commands under one short title.
    public class ModernCard : Panel
    {
        private string badgeText = string.Empty;
        private ModernButtonTone badgeTone = ModernButtonTone.Neutral;

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

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(TaiTheme.BackdropOf(this));
            TaiTheme.PaintCard(g, this, ClientRectangle, null);
            int pad = TaiTheme.Px(this, TaiTheme.CardPadding), top = TaiTheme.Px(this, 7), line = TaiTheme.Px(this, 18);
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
