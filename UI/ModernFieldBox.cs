using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace ADDIN.UI
{
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
            if (!string.IsNullOrEmpty(text))
            {
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var font = new Font("Segoe UI", 8.75F, FontStyle.Bold))
                {
                    TextRenderer.DrawText(e.Graphics, text, font, new Point(2, Height / 2 - 8), Color.FromArgb(71, 85, 105),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                }
            }
        }
    }
}
