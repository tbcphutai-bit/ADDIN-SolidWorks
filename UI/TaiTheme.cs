using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ADDIN.UI
{
    // The task pane's colours, fonts and measurements live here. Controls read
    // these values instead of carrying their own, so the pane keeps one look.
    public static class TaiTheme
    {
        // Surfaces
        public static readonly Color Page = Color.FromArgb(243, 245, 248);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceHover = Color.FromArgb(246, 248, 251);
        public static readonly Color SurfacePressed = Color.FromArgb(235, 239, 244);
        public static readonly Color SurfaceDisabled = Color.FromArgb(249, 250, 251);
        public static readonly Color Sunken = Color.FromArgb(231, 235, 241);
        public static readonly Color Border = Color.FromArgb(217, 222, 230);
        public static readonly Color BorderStrong = Color.FromArgb(193, 200, 211);
        public static readonly Color Divider = Color.FromArgb(233, 236, 241);

        // Text
        public static readonly Color Text = Color.FromArgb(23, 32, 46);
        public static readonly Color TextMuted = Color.FromArgb(92, 104, 122);
        public static readonly Color TextFaint = Color.FromArgb(140, 150, 164);
        public static readonly Color TextDisabled = Color.FromArgb(172, 180, 192);

        // One accent; the other hues carry a meaning and nothing else.
        public static readonly Color Accent = Color.FromArgb(29, 95, 208);
        public static readonly Color AccentHover = Color.FromArgb(24, 80, 178);
        public static readonly Color AccentPressed = Color.FromArgb(20, 67, 150);
        public static readonly Color AccentSoft = Color.FromArgb(233, 241, 253);
        public static readonly Color AccentSoftHover = Color.FromArgb(247, 250, 255);
        public static readonly Color AccentBorder = Color.FromArgb(160, 190, 244);

        public static readonly Color Success = Color.FromArgb(22, 125, 76);
        public static readonly Color SuccessHover = Color.FromArgb(18, 106, 64);
        public static readonly Color SuccessSoft = Color.FromArgb(229, 244, 236);
        public static readonly Color SuccessBorder = Color.FromArgb(160, 212, 183);

        public static readonly Color Danger = Color.FromArgb(196, 52, 44);
        public static readonly Color DangerSoft = Color.FromArgb(253, 237, 235);
        public static readonly Color DangerBorder = Color.FromArgb(240, 178, 172);

        public static readonly Color Warning = Color.FromArgb(160, 95, 0);
        public static readonly Color WarningSoft = Color.FromArgb(255, 245, 219);
        public static readonly Color WarningSoftHover = Color.FromArgb(255, 238, 196);
        public static readonly Color WarningBorder = Color.FromArgb(236, 203, 128);

        // Fonts are shared and live as long as the add-in.
        public static readonly Font Body = Create("Segoe UI", 11F, FontStyle.Regular, FontStyle.Regular);
        public static readonly Font BodyStrong = Create("Segoe UI Semibold", 11F, FontStyle.Regular, FontStyle.Bold);
        public static readonly Font Small = Create("Segoe UI", 10F, FontStyle.Regular, FontStyle.Regular);
        public static readonly Font SmallStrong = Create("Segoe UI Semibold", 10F, FontStyle.Regular, FontStyle.Bold);
        public static readonly Font Value = Create("Segoe UI Semibold", 11.5F, FontStyle.Regular, FontStyle.Bold);
        public static readonly Font Title = Create("Segoe UI Semibold", 12F, FontStyle.Regular, FontStyle.Bold);

        private static Font Create(string family, float size, FontStyle style, FontStyle fallbackStyle)
        {
            Font font = new Font(family, size, style, GraphicsUnit.Point);
            // GDI+ substitutes another family when the requested one is not installed.
            if (string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase)) return font;
            font.Dispose();
            return new Font("Segoe UI", size, fallbackStyle, GraphicsUnit.Point);
        }

        public static int Px(Control control, int value)
        {
            int dpi = control == null ? 96 : Math.Max(96, control.DeviceDpi);
            return Math.Max(1, (int)Math.Round(value * dpi / 96.0));
        }

        public static float Scale(Control control)
        {
            return control == null ? 1F : Math.Max(96, control.DeviceDpi) / 96F;
        }

        public static Color Mix(Color from, Color to, double amount)
        {
            return Color.FromArgb(
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }

        // Colour a child has to clear its corners with: the nearest opaque parent.
        public static Color BackdropOf(Control control)
        {
            for (Control parent = control == null ? null : control.Parent; parent != null; parent = parent.Parent)
                if (parent.BackColor.A == 255) return parent.BackColor;
            return Page;
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Integer rectangles then land on pixel centres: straight edges stay one pixel wide.
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }

        public static void FillRound(Graphics g, Rectangle bounds, int radius, Color fill, Color border)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (GraphicsPath path = UIHelper.GetRoundPath(bounds, radius))
            {
                if (fill.A > 0)
                    using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (border.A > 0)
                    using (Pen pen = new Pen(border, 1F)) g.DrawPath(pen, path);
            }
        }

        // A white panel with a hairline border and an optional title line.
        public static void PaintCard(Graphics g, Control owner, Rectangle bounds, string title)
        {
            Prepare(g);
            Rectangle frame = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            FillRound(g, frame, Px(owner, CardRadius), Surface, Border);
            if (string.IsNullOrEmpty(title)) return;
            Rectangle text = new Rectangle(bounds.X + Px(owner, CardPadding), bounds.Y + Px(owner, 7),
                Math.Max(1, bounds.Width - Px(owner, CardPadding) * 2), Px(owner, 22));
            TextRenderer.DrawText(g, title, BodyStrong, text, Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        // A GroupBox drawn as a card: same surface, border and title line as ModernCard.
        public static void StyleGroupAsCard(GroupBox group)
        {
            if (group == null) return;
            group.FlatStyle = FlatStyle.Standard;
            group.BackColor = Surface;
            UIHelper.EnableDoubleBuffer(group);
            group.Paint -= PaintGroupAsCard;
            group.Paint += PaintGroupAsCard;
        }

        private static void PaintGroupAsCard(object sender, PaintEventArgs e)
        {
            GroupBox group = (GroupBox)sender;
            e.Graphics.Clear(BackdropOf(group));
            PaintCard(e.Graphics, group, group.ClientRectangle, group.Text);
        }

        public const int CardRadius = 8;
        public const int CardPadding = 10;
        public const int CardHeader = 34;
        public const int ButtonRadius = 6;
    }

    // Draws the theme's border on a text box or list in place of the system
    // one: a quiet hairline, the accent colour while the control has the focus.
    internal sealed class TaiInputFrame : NativeWindow
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);

        private readonly Control box;
        private readonly bool innerRing;

        private TaiInputFrame(Control box, bool innerRing) { this.box = box; this.innerRing = innerRing; }

        // A text box gets the two-pixel 3D frame, whose inner pixel is repainted in the box colour.
        internal static void Attach(TextBoxBase box)
        {
            if (box == null || box.BorderStyle == BorderStyle.None) return;
            box.BorderStyle = BorderStyle.Fixed3D;
            Hook(box, new TaiInputFrame(box, true));
        }

        // A list keeps its one-pixel frame; only its colour changes.
        internal static void Attach(ListBox list)
        {
            if (list == null || list.BorderStyle == BorderStyle.None) return;
            list.BorderStyle = BorderStyle.FixedSingle;
            Hook(list, new TaiInputFrame(list, false));
        }

        private static void Hook(Control box, TaiInputFrame frame)
        {
            if (box.IsHandleCreated) frame.AssignHandle(box.Handle);
            box.HandleCreated += (sender, e) => { if (frame.Handle == IntPtr.Zero) frame.AssignHandle(box.Handle); };
            box.HandleDestroyed += (sender, e) => frame.ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            const int Paint = 0x000F, NonClientPaint = 0x0085, SetFocus = 0x0007, KillFocus = 0x0008, Enable = 0x000A;
            base.WndProc(ref m);
            if (m.Msg == Paint || m.Msg == NonClientPaint || m.Msg == SetFocus || m.Msg == KillFocus || m.Msg == Enable) Draw();
        }

        private void Draw()
        {
            if (box.IsDisposed || box.Width < 4 || box.Height < 4) return;
            IntPtr dc = GetWindowDC(Handle);
            if (dc == IntPtr.Zero) return;
            try
            {
                using (Graphics g = Graphics.FromHdc(dc))
                {
                    Color edge = !box.Enabled ? TaiTheme.Divider : box.Focused ? TaiTheme.Accent : TaiTheme.BorderStrong;
                    using (Pen outer = new Pen(edge)) g.DrawRectangle(outer, 0, 0, box.Width - 1, box.Height - 1);
                    if (innerRing)
                        using (Pen inner = new Pen(box.BackColor)) g.DrawRectangle(inner, 1, 1, box.Width - 3, box.Height - 3);
                }
            }
            finally { ReleaseDC(Handle, dc); }
        }
    }

    // The same border for combo boxes. Their frame is part of the client area,
    // so it is drawn over the system one after each paint.
    internal sealed class TaiComboFrame : NativeWindow
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr dc, int x, int y);

        private readonly ComboBox box;

        private TaiComboFrame(ComboBox box) { this.box = box; }

        internal static void Attach(ComboBox box)
        {
            if (box == null) return;
            TaiComboFrame frame = new TaiComboFrame(box);
            if (box.IsHandleCreated) frame.AssignHandle(box.Handle);
            box.HandleCreated += (sender, e) => { if (frame.Handle == IntPtr.Zero) frame.AssignHandle(box.Handle); };
            box.HandleDestroyed += (sender, e) => frame.ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            const int Paint = 0x000F, SetFocus = 0x0007, KillFocus = 0x0008, Enable = 0x000A;
            base.WndProc(ref m);
            if (m.Msg == Paint || m.Msg == SetFocus || m.Msg == KillFocus || m.Msg == Enable) Draw();
        }

        private void Draw()
        {
            if (box.IsDisposed || box.Width < 12 || box.Height < 8) return;
            IntPtr dc = GetDC(Handle);
            if (dc == IntPtr.Zero) return;
            try
            {
                // The face colour differs between the editable and the list style; take it from the control itself.
                uint face = GetPixel(dc, 4, box.Height / 2);
                Color inside = face == 0xFFFFFFFF ? box.BackColor
                    : Color.FromArgb((int)(face & 0xFF), (int)((face >> 8) & 0xFF), (int)((face >> 16) & 0xFF));
                using (Graphics g = Graphics.FromHdc(dc))
                {
                    Color edge = !box.Enabled ? TaiTheme.Divider : box.ContainsFocus ? TaiTheme.Accent : TaiTheme.BorderStrong;
                    using (Pen outer = new Pen(edge)) g.DrawRectangle(outer, 0, 0, box.Width - 1, box.Height - 1);
                    using (Pen inner = new Pen(inside)) g.DrawRectangle(inner, 1, 1, box.Width - 3, box.Height - 3);
                }
            }
            finally { ReleaseDC(Handle, dc); }
        }
    }
}
