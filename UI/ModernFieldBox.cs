using System;
using System.ComponentModel;
using System.Drawing;
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
}
