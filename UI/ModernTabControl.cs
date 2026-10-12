using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace ADDIN.UI
{
    public enum ModernTabStyle
    {
        Card,       // Level 1: a segmented switch (Drawing / Model)
        Underline   // Level 2: text tabs with an underline for the open page
    }

    // A tab control that paints its own header and gives its pages the full
    // width. Hit-testing uses the painted tabs, not the native ones.
    public class ModernTabControl : TabControl
    {
        private ModernTabStyle tabStyle = ModernTabStyle.Card;
        private int hoverIndex = -1;

        [Category("Appearance")]
        [Description("Card: công tắc phân đoạn cấp 1. Underline: tab chữ gạch chân cấp 2")]
        [DefaultValue(ModernTabStyle.Card)]
        public ModernTabStyle TabStyle
        {
            get
            {
                if (Name == "tabDrawingPages" || Name == "tabModelPages" || Parent is TabPage)
                    return ModernTabStyle.Underline;
                return tabStyle;
            }
            set { tabStyle = value; Invalidate(); }
        }

        // Colour properties from before TaiTheme; Designer code still assigns them.
        [Browsable(false)] public Color ActiveTabColor { get; set; } = TaiTheme.Accent;
        [Browsable(false)] public Color ActiveTextColor { get; set; } = TaiTheme.Accent;
        [Browsable(false)] public Color InactiveTextColor { get; set; } = TaiTheme.TextMuted;
        [Browsable(false)] public Color HeaderBackColor { get; set; } = TaiTheme.Page;
        [Browsable(false)] public Color BorderColor { get; set; } = TaiTheme.Border;

        // The native tabs are never shown. They stay narrow so the native control
        // has no reason to add its scroll arrows; sizes assigned by Designer
        // code are for those hidden tabs and are not applied.
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public new Size ItemSize
        {
            get { return base.ItemSize; }
            set { }
        }

        public ModernTabControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            SizeMode = TabSizeMode.Fixed;
            base.ItemSize = new Size(8, 24);
        }

        private int HeaderHeight { get { return TaiTheme.Px(this, TabStyle == ModernTabStyle.Card ? 44 : 34); } }

        // Pages start right under the painted header and reach every edge.
        public override Rectangle DisplayRectangle
        {
            get { return new Rectangle(0, HeaderHeight, Math.Max(0, Width), Math.Max(0, Height - HeaderHeight)); }
        }

        private Rectangle Track
        {
            get
            {
                int side = TaiTheme.Px(this, 8);
                return new Rectangle(side, TaiTheme.Px(this, 7), Math.Max(1, Width - side * 2), TaiTheme.Px(this, 30));
            }
        }

        private Rectangle TabBounds(Graphics g, int index)
        {
            if (TabStyle == ModernTabStyle.Card)
            {
                Rectangle track = Track;
                int count = Math.Max(1, TabCount), inset = TaiTheme.Px(this, 3);
                int left = track.X + track.Width * index / count, right = track.X + track.Width * (index + 1) / count;
                return new Rectangle(left + inset, track.Y + inset, Math.Max(1, right - left - inset * 2), track.Height - inset * 2);
            }
            int x = TaiTheme.Px(this, 10), gap = TaiTheme.Px(this, 6), width = 0;
            for (int n = 0; n <= index; n++)
            {
                if (n > 0) x += width + gap;
                width = TextRenderer.MeasureText(g, TabPages[n].Text, TaiTheme.BodyStrong, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + TaiTheme.Px(this, 16);
            }
            return new Rectangle(x, 0, width, HeaderHeight - 1);
        }

        private int TabAt(Point point)
        {
            if (point.Y < 0 || point.Y >= HeaderHeight || TabCount == 0) return -1;
            using (Graphics g = CreateGraphics())
                for (int n = 0; n < TabCount; n++)
                    if (TabBounds(g, n).Contains(point)) return n;
            return -1;
        }

        protected override void WndProc(ref Message m)
        {
            const int HitTest = 0x0084, HitClient = 1, LeftButtonDown = 0x0201, LeftButtonDoubleClick = 0x0203;
            // Away from its own tabs, which stay hidden here, the native control answers
            // "transparent" and Windows gives a click on the painted header to the parent.
            if (m.Msg == HitTest) { m.Result = (IntPtr)HitClient; return; }
            if (m.Msg == LeftButtonDown || m.Msg == LeftButtonDoubleClick)
            {
                long position = m.LParam.ToInt64();
                Point point = new Point((short)(position & 0xFFFF), (short)((position >> 16) & 0xFFFF));
                if (point.Y < HeaderHeight)
                {
                    int index = TabAt(point);
                    if (index >= 0 && index != SelectedIndex) SelectedIndex = index;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = TabAt(e.Location);
            if (index == hoverIndex) return;
            hoverIndex = index;
            Invalidate(new Rectangle(0, 0, Width, HeaderHeight));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverIndex < 0) return;
            hoverIndex = -1;
            Invalidate(new Rectangle(0, 0, Width, HeaderHeight));
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent) { }

        private static TaiIcon IconFor(string text)
        {
            if (string.Equals(text, "Drawing", StringComparison.OrdinalIgnoreCase)) return TaiIcon.Sheet;
            return string.Equals(text, "Model", StringComparison.OrdinalIgnoreCase) ? TaiIcon.Cube : TaiIcon.None;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);
            g.Clear(TaiTheme.Page);
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

            if (TabStyle == ModernTabStyle.Card)
            {
                Rectangle track = Track;
                TaiTheme.FillRound(g, new Rectangle(track.X, track.Y, track.Width - 1, track.Height - 1), TaiTheme.Px(this, 8), TaiTheme.Sunken, Color.Empty);
                for (int n = 0; n < TabCount; n++)
                {
                    Rectangle tab = TabBounds(g, n);
                    bool selected = n == SelectedIndex;
                    if (selected)
                        TaiTheme.FillRound(g, new Rectangle(tab.X, tab.Y, tab.Width - 1, tab.Height - 1), TaiTheme.Px(this, 6), TaiTheme.Surface, TaiTheme.Border);
                    Color color = selected ? TaiTheme.Text : n == hoverIndex ? TaiTheme.Text : TaiTheme.TextMuted;
                    string text = TabPages[n].Text;
                    TaiIcon icon = IconFor(text);
                    int glyph = icon == TaiIcon.None ? 0 : TaiTheme.Px(this, 16), gap = glyph == 0 ? 0 : TaiTheme.Px(this, 6);
                    int textWidth = TextRenderer.MeasureText(g, text, TaiTheme.BodyStrong, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                    int left = tab.X + Math.Max(TaiTheme.Px(this, 4), (tab.Width - glyph - gap - textWidth) / 2);
                    if (glyph > 0)
                        TaiIcons.Draw(g, icon, new Rectangle(left, tab.Y + (tab.Height - glyph) / 2, glyph, glyph), selected ? TaiTheme.Accent : color);
                    TextRenderer.DrawText(g, text, TaiTheme.BodyStrong, new Rectangle(left + glyph + gap, tab.Y, Math.Max(1, tab.Right - left - glyph - gap), tab.Height - 1),
                        color, TextFormatFlags.Left | flags);
                }
                return;
            }

            int baseline = HeaderHeight - 1;
            using (Pen pen = new Pen(TaiTheme.Divider, 1F)) g.DrawLine(pen, 0, baseline, Width, baseline);
            for (int n = 0; n < TabCount; n++)
            {
                Rectangle tab = TabBounds(g, n);
                bool selected = n == SelectedIndex;
                Color color = selected ? TaiTheme.Accent : n == hoverIndex ? TaiTheme.Text : TaiTheme.TextMuted;
                TextRenderer.DrawText(g, TabPages[n].Text, selected ? TaiTheme.BodyStrong : TaiTheme.Body, tab, color, TextFormatFlags.HorizontalCenter | flags);
                if (!selected) continue;
                int inset = TaiTheme.Px(this, 8), thickness = TaiTheme.Px(this, 2);
                using (SolidBrush brush = new SolidBrush(TaiTheme.Accent))
                    g.FillRectangle(brush, tab.X + inset, baseline - thickness + 1, Math.Max(1, tab.Width - inset * 2), thickness);
            }
        }
    }
}
