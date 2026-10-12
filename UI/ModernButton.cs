using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace ADDIN.UI
{
    public enum ModernButtonIcon
    {
        None,
        CheckDfTk,
        CheckKegaki,
        CheckRound,
        CheckSamePart,
        XepUnit,
        OpenAssem,
        CheckBalloon,
        CheckDrawing,
        Refresh,
        Trash,
        Cancel,
        CheckDxf
    }

    // What a button is for. The look follows from the role, never the other way round.
    public enum ModernButtonKind
    {
        Auto,       // role read from the legacy colour properties (see ResolveKind)
        Standard,   // ordinary action: white, hairline border
        Primary,    // the main action of its group
        Success,    // confirm / apply
        Danger,     // deletes or discards
        Warning,    // MARK related
        Subtle,     // no frame until the pointer is over it
        Tile        // command tile: icon, name and one line saying what it does
    }

    public enum ModernButtonTone { Accent, Neutral, Danger, Success, Warning }

    public class ModernButton : Button
    {
        private ModernButtonKind kind = ModernButtonKind.Auto;
        private ModernButtonTone tone = ModernButtonTone.Accent;
        private TaiIcon icon = TaiIcon.None;
        private string subtitle = string.Empty;
        private bool selected, hovered, pressed;

        [Category("Appearance")]
        [Description("Vai trò của nút; giao diện lấy theo TaiTheme")]
        [DefaultValue(ModernButtonKind.Auto)]
        public ModernButtonKind Kind
        {
            get { return kind; }
            set { kind = value; Invalidate(); }
        }

        [Category("Appearance")]
        [Description("Biểu tượng vector trong bộ TaiIcons")]
        [DefaultValue(TaiIcon.None)]
        public TaiIcon Icon
        {
            get { return icon; }
            set { icon = value; Invalidate(); }
        }

        [Category("Appearance")]
        [Description("Sắc độ của biểu tượng: chỉ dùng khi màu mang ý nghĩa (xóa, xác nhận, MARK)")]
        [DefaultValue(ModernButtonTone.Accent)]
        public ModernButtonTone Tone
        {
            get { return tone; }
            set { tone = value; Invalidate(); }
        }

        [Category("Appearance")]
        [Description("Nút đang ở trạng thái được chọn (chế độ đang dùng)")]
        [DefaultValue(false)]
        public bool Selected
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; Invalidate(); }
        }

        [Category("Appearance")]
        [Description("Dòng mô tả dưới tên lệnh")]
        [DefaultValue("")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Subtitle
        {
            get { return subtitle; }
            set { subtitle = value ?? string.Empty; Invalidate(); }
        }

        // Appearance properties from before TaiTheme. Designer code still assigns
        // them. NormalColor and ForeColor only tell an Auto button its role; the
        // others no longer change the painting.
        [Browsable(false)] public int BorderRadius { get; set; } = 6;
        [Browsable(false)] [DefaultValue(ModernButtonIcon.None)] public ModernButtonIcon VectorIcon { get; set; } = ModernButtonIcon.None;
        [Browsable(false)] public Color IconColor { get; set; } = Color.Empty;
        [Browsable(false)] public Color IconBoxBorderColor { get; set; } = Color.Empty;
        [Browsable(false)] public Color NormalColor { get; set; } = Color.FromArgb(246, 248, 250);
        [Browsable(false)] public Color HoverColor { get; set; } = Color.FromArgb(234, 240, 247);
        [Browsable(false)] public Color PressColor { get; set; } = Color.FromArgb(220, 228, 238);
        [Browsable(false)] public Color BorderColor { get; set; } = Color.FromArgb(210, 216, 224);
        [Browsable(false)] [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color SubtitleColor { get; set; } = Color.FromArgb(100, 116, 139);
        [Browsable(false)] [DefaultValue(false)] public bool UseCompactTextLayout { get; set; }

        [Browsable(false)]
        public new bool UseVisualStyleBackColor
        {
            get { return false; }
            set { base.UseVisualStyleBackColor = false; }
        }

        [Browsable(false)]
        public new FlatStyle FlatStyle
        {
            get { return FlatStyle.Flat; }
            set { base.FlatStyle = FlatStyle.Flat; }
        }

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            UseVisualStyleBackColor = false;
            ForeColor = TaiTheme.Text;
            Font = TaiTheme.BodyStrong;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { pressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { pressed = false; Invalidate(); base.OnMouseUp(mevent); }
        protected override void OnPaintBackground(PaintEventArgs pevent) { }

        private string Title
        {
            get
            {
                string text = Text ?? string.Empty;
                int breakAt = text.IndexOf('\n');
                return (breakAt < 0 ? text : text.Substring(0, breakAt)).Trim();
            }
        }

        private string Detail
        {
            get
            {
                if (!string.IsNullOrEmpty(subtitle)) return subtitle;
                string text = Text ?? string.Empty;
                int breakAt = text.IndexOf('\n');
                return breakAt < 0 ? string.Empty : text.Substring(breakAt + 1).Replace("\r", "").Replace("\n", " ").Trim();
            }
        }

        // An Auto button takes its role from what the old code asked for: two
        // lines of text make a tile, a strong fill makes the main action, red
        // text marks a delete. Every pastel fill becomes a standard button.
        private ModernButtonKind ResolveKind()
        {
            if (kind != ModernButtonKind.Auto) return kind;
            if (Detail.Length > 0) return ModernButtonKind.Tile;
            Color fill = NormalColor;
            if (fill.A == 255 && fill.GetBrightness() < 0.6F && fill.GetSaturation() > 0.35F)
            {
                float hue = fill.GetHue();
                if (hue >= 80 && hue < 180) return ModernButtonKind.Success;
                return hue < 20 || hue >= 330 ? ModernButtonKind.Danger : ModernButtonKind.Primary;
            }
            Color text = ForeColor;
            return text.R > 160 && text.G < 110 && text.B < 110 ? ModernButtonKind.Danger : ModernButtonKind.Standard;
        }

        private TaiIcon EffectiveIcon
        {
            get { return icon != TaiIcon.None ? icon : TaiIcons.FromLegacy(LegacyIcon()); }
        }

        private ModernButtonIcon LegacyIcon()
        {
            if (VectorIcon != ModernButtonIcon.None) return VectorIcon;
            string t = (Text ?? "").ToUpperInvariant();
            string n = (Name ?? "").ToUpperInvariant();
            if (t.Contains("DF/TK") || n.Contains("DFTK")) return ModernButtonIcon.CheckDfTk;
            if (t.Contains("KEGAKI") || n.Contains("KEGAKI") || n.Contains("CHECKALL")) return ModernButtonIcon.CheckKegaki;
            if (t.Contains("ROUND") || n.Contains("ROUND")) return ModernButtonIcon.CheckRound;
            if (t.Contains("SAME") || n.Contains("SAMEPART")) return ModernButtonIcon.CheckSamePart;
            if (t.Contains("UNIT") || n.Contains("UNIT") || n == "BUTTON2") return ModernButtonIcon.XepUnit;
            if (t.Contains("ASSEM") || n.Contains("ASSEM")) return ModernButtonIcon.OpenAssem;
            if (t.Contains("BALLOON") || n.Contains("BALLOON")) return ModernButtonIcon.CheckBalloon;
            if (t.Contains("DRAWING") || n.Contains("DRAWINGBOM")) return ModernButtonIcon.CheckDrawing;
            if (t.Contains("CẬP NHẬT") || t.Contains("LOAD") || n.Contains("LOAD")) return ModernButtonIcon.Refresh;
            if (t.Contains("XÓA BẢNG") || t.Contains("CLEAR") || n.Contains("CLEAR")) return ModernButtonIcon.Trash;
            if (t.Contains("CANCEL") || n.Contains("CANCEL") || n == "BUTTON1") return ModernButtonIcon.Cancel;
            if (t.Contains("DXF") || n.Contains("DXF")) return ModernButtonIcon.CheckDxf;
            return ModernButtonIcon.None;
        }

        private struct Look
        {
            internal Color Fill, Border, Lip, Text, Detail, Icon, Chip;
        }

        private void ToneColors(out Color strong, out Color soft)
        {
            switch (tone)
            {
                case ModernButtonTone.Danger: strong = TaiTheme.Danger; soft = TaiTheme.DangerSoft; break;
                case ModernButtonTone.Success: strong = TaiTheme.Success; soft = TaiTheme.SuccessSoft; break;
                case ModernButtonTone.Warning: strong = TaiTheme.Warning; soft = TaiTheme.WarningSoft; break;
                case ModernButtonTone.Neutral: strong = TaiTheme.TextMuted; soft = TaiTheme.Sunken; break;
                default: strong = TaiTheme.Accent; soft = TaiTheme.AccentSoft; break;
            }
        }

        private Look LookFor(ModernButtonKind role, Color backdrop)
        {
            Color strong, soft;
            ToneColors(out strong, out soft);
            Look look = new Look { Chip = Color.Empty, Lip = Color.Empty };
            bool down = pressed && hovered;

            if (!Enabled)
            {
                look.Fill = TaiTheme.SurfaceDisabled; look.Border = TaiTheme.Divider;
                look.Text = look.Detail = look.Icon = TaiTheme.TextDisabled;
                if (role == ModernButtonKind.Tile) look.Chip = Color.FromArgb(240, 242, 246);
                else if (role == ModernButtonKind.Subtle) { look.Fill = backdrop; look.Border = Color.Empty; }
                else if (role == ModernButtonKind.Primary || role == ModernButtonKind.Success)
                {
                    look.Fill = look.Border = TaiTheme.Mix(role == ModernButtonKind.Primary ? TaiTheme.Accent : TaiTheme.Success, TaiTheme.Surface, 0.68);
                    look.Text = look.Icon = Color.White;
                }
                return look;
            }

            switch (role)
            {
                case ModernButtonKind.Primary:
                case ModernButtonKind.Success:
                    bool main = role == ModernButtonKind.Primary;
                    look.Fill = down ? (main ? TaiTheme.AccentPressed : TaiTheme.SuccessHover)
                        : hovered ? (main ? TaiTheme.AccentHover : TaiTheme.SuccessHover) : (main ? TaiTheme.Accent : TaiTheme.Success);
                    look.Border = look.Fill; look.Text = look.Icon = Color.White;
                    break;
                case ModernButtonKind.Danger:
                    look.Fill = down ? TaiTheme.Mix(TaiTheme.DangerSoft, TaiTheme.Danger, 0.12) : hovered ? TaiTheme.DangerSoft : TaiTheme.Surface;
                    look.Border = hovered ? TaiTheme.DangerBorder : TaiTheme.Border;
                    look.Lip = hovered ? Color.Empty : TaiTheme.BorderStrong;
                    look.Text = look.Icon = TaiTheme.Danger;
                    break;
                case ModernButtonKind.Warning:
                    look.Fill = down ? TaiTheme.Mix(TaiTheme.WarningSoftHover, TaiTheme.Warning, 0.1) : hovered ? TaiTheme.WarningSoftHover : TaiTheme.WarningSoft;
                    look.Border = TaiTheme.WarningBorder; look.Text = look.Icon = TaiTheme.Warning;
                    break;
                case ModernButtonKind.Subtle:
                    bool alert = tone == ModernButtonTone.Danger && hovered;
                    look.Fill = !hovered ? backdrop : alert ? TaiTheme.DangerSoft : down ? TaiTheme.Sunken : TaiTheme.SurfacePressed;
                    look.Border = Color.Empty;
                    look.Text = look.Icon = alert ? TaiTheme.Danger : hovered ? TaiTheme.Text : TaiTheme.TextMuted;
                    break;
                case ModernButtonKind.Tile:
                    look.Text = TaiTheme.Text; look.Detail = TaiTheme.TextMuted; look.Icon = strong; look.Chip = soft;
                    if (selected)
                    {
                        look.Fill = TaiTheme.AccentSoft; look.Border = TaiTheme.Accent;
                        look.Chip = TaiTheme.Accent; look.Icon = Color.White;
                    }
                    else
                    {
                        look.Fill = down ? TaiTheme.AccentSoft : hovered ? TaiTheme.AccentSoftHover : TaiTheme.Surface;
                        look.Border = hovered ? TaiTheme.AccentBorder : TaiTheme.Border;
                    }
                    break;
                default:
                    if (selected)
                    {
                        look.Fill = TaiTheme.AccentSoft; look.Border = TaiTheme.AccentBorder;
                        look.Text = look.Icon = TaiTheme.AccentHover;
                        break;
                    }
                    look.Fill = down ? TaiTheme.SurfacePressed : hovered ? TaiTheme.SurfaceHover : TaiTheme.Surface;
                    look.Border = hovered ? TaiTheme.BorderStrong : TaiTheme.Border;
                    look.Lip = down ? Color.Empty : TaiTheme.BorderStrong;
                    look.Text = TaiTheme.Text;
                    look.Icon = tone == ModernButtonTone.Accent ? TaiTheme.TextMuted : strong;
                    break;
            }
            return look;
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            TaiTheme.Prepare(g);
            Color backdrop = TaiTheme.BackdropOf(this);
            g.Clear(backdrop);
            if (Width < 3 || Height < 3) return;

            ModernButtonKind role = ResolveKind();
            Look look = LookFor(role, backdrop);
            Rectangle frame = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = TaiTheme.Px(this, role == ModernButtonKind.Tile ? 8 : TaiTheme.ButtonRadius);
            TaiTheme.FillRound(g, frame, radius, look.Fill, look.Border);
            if (look.Lip.A > 0)
                using (Pen lip = new Pen(look.Lip, 1F))
                    g.DrawLine(lip, radius, Height - 1, Width - 1 - radius, Height - 1);
            if (Focused && ShowFocusCues)
                TaiTheme.FillRound(g, Rectangle.Inflate(frame, -2, -2), Math.Max(1, radius - 2), Color.Empty, TaiTheme.Accent);

            if (role != ModernButtonKind.Tile) PaintInline(g, look);
            else if (Height >= TaiTheme.Px(this, 56) && (TextImageRelation == TextImageRelation.ImageAboveText || Width < TaiTheme.Px(this, 124)))
                PaintStackedTile(g, look);
            else PaintTile(g, look);
        }

        private const TextFormatFlags LineFlags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping;

        private static int LineHeight(Graphics g, Font font)
        {
            return TextRenderer.MeasureText(g, "Ag", font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height;
        }

        private void PaintGlyph(Graphics g, Rectangle box, Color color)
        {
            TaiIcon glyph = EffectiveIcon;
            if (glyph != TaiIcon.None) { TaiIcons.Draw(g, glyph, box, color); return; }
            if (Image == null) return;
            float scale = Math.Min((float)box.Width / Image.Width, (float)box.Height / Image.Height);
            int width = Math.Max(1, (int)Math.Round(Image.Width * scale)), height = Math.Max(1, (int)Math.Round(Image.Height * scale));
            Rectangle target = new Rectangle(box.X + (box.Width - width) / 2, box.Y + (box.Height - height) / 2, width, height);
            if (Enabled) { g.DrawImage(Image, target); return; }
            using (Bitmap scaled = new Bitmap(Image, target.Size))
                ControlPaint.DrawImageDisabled(g, scaled, target.X, target.Y, TaiTheme.Surface);
        }

        private bool HasGlyph { get { return EffectiveIcon != TaiIcon.None || Image != null; } }

        // Icon and label on one line, centred.
        private void PaintInline(Graphics g, Look look)
        {
            string title = Title;
            Font font = Height <= TaiTheme.Px(this, 24) ? TaiTheme.SmallStrong : TaiTheme.BodyStrong;
            int pad = TaiTheme.Px(this, 6), gap = TaiTheme.Px(this, 6);
            int glyph = HasGlyph ? Math.Min(TaiTheme.Px(this, 16), Height - TaiTheme.Px(this, 6)) : 0;
            int textWidth = title.Length == 0 ? 0 : TextRenderer.MeasureText(g, title, font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            int room = Math.Max(1, Width - pad * 2);
            int content = glyph + (glyph > 0 && textWidth > 0 ? gap : 0) + textWidth;
            int left = content >= room ? pad : (Width - content) / 2;
            if (glyph > 0)
            {
                PaintGlyph(g, new Rectangle(left, (Height - glyph) / 2, glyph, glyph), look.Icon);
                left += glyph + (textWidth > 0 ? gap : 0);
            }
            if (textWidth > 0)
                TextRenderer.DrawText(g, title, font, new Rectangle(left, 0, Math.Max(1, Width - pad - left), Height - 1), look.Text,
                    TextFormatFlags.Left | LineFlags);
        }

        // Icon chip at the left, name and description at the right.
        private void PaintTile(Graphics g, Look look)
        {
            string title = Title, detail = Detail;
            bool roomy = Height >= TaiTheme.Px(this, 34);
            int pad = TaiTheme.Px(this, roomy ? 7 : 8);
            int left = pad + TaiTheme.Px(this, 2);
            if (HasGlyph)
            {
                if (roomy)
                {
                    int chip = Math.Max(TaiTheme.Px(this, 24), Math.Min(TaiTheme.Px(this, 32), Height - TaiTheme.Px(this, 10)));
                    Rectangle box = new Rectangle(pad, (Height - chip) / 2, chip, chip);
                    TaiTheme.FillRound(g, new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1), TaiTheme.Px(this, 7), look.Chip, Color.Empty);
                    int glyph = chip >= TaiTheme.Px(this, 30) ? TaiTheme.Px(this, 20) : TaiTheme.Px(this, 18);
                    PaintGlyph(g, new Rectangle(box.X + (chip - glyph) / 2, box.Y + (chip - glyph) / 2, glyph, glyph), look.Icon);
                    left = box.Right + TaiTheme.Px(this, 9);
                }
                else
                {
                    int glyph = TaiTheme.Px(this, 16);
                    PaintGlyph(g, new Rectangle(pad, (Height - glyph) / 2, glyph, glyph), look.Icon);
                    left = pad + glyph + TaiTheme.Px(this, 7);
                }
            }

            int width = Math.Max(1, Width - left - pad);
            if (!roomy || detail.Length == 0)
            {
                TextRenderer.DrawText(g, title, TaiTheme.BodyStrong, new Rectangle(left, 0, width, Height - 1), look.Text, TextFormatFlags.Left | LineFlags);
                return;
            }
            int titleHeight = LineHeight(g, TaiTheme.BodyStrong), detailHeight = LineHeight(g, TaiTheme.Small);
            int top = Math.Max(0, (Height - titleHeight - detailHeight) / 2 - 1);
            TextRenderer.DrawText(g, title, TaiTheme.BodyStrong, new Rectangle(left, top, width, titleHeight), look.Text, TextFormatFlags.Left | LineFlags);
            TextRenderer.DrawText(g, detail, TaiTheme.Small, new Rectangle(left, top + titleHeight, width, detailHeight), look.Detail, TextFormatFlags.Left | LineFlags);
        }

        // Icon chip above the name: for the few large mode buttons.
        private void PaintStackedTile(Graphics g, Look look)
        {
            string title = Title;
            int chip = HasGlyph ? TaiTheme.Px(this, 34) : 0, gap = HasGlyph ? TaiTheme.Px(this, 6) : 0;
            int titleHeight = LineHeight(g, TaiTheme.BodyStrong);
            int top = Math.Max(TaiTheme.Px(this, 4), (Height - chip - gap - titleHeight) / 2);
            if (chip > 0)
            {
                Rectangle box = new Rectangle((Width - chip) / 2, top, chip, chip);
                TaiTheme.FillRound(g, new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1), TaiTheme.Px(this, 8), look.Chip, Color.Empty);
                int glyph = TaiTheme.Px(this, 20);
                PaintGlyph(g, new Rectangle(box.X + (chip - glyph) / 2, box.Y + (chip - glyph) / 2, glyph, glyph), look.Icon);
            }
            int pad = TaiTheme.Px(this, 4);
            TextRenderer.DrawText(g, title, TaiTheme.BodyStrong, new Rectangle(pad, top + chip + gap, Math.Max(1, Width - pad * 2), titleHeight),
                look.Text, TextFormatFlags.HorizontalCenter | LineFlags);
        }
    }
}
