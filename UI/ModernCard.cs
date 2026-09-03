using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ADDIN.UI
{
    public class ModernCard : Panel
    {
        public int BorderRadius { get; set; } = 6;
        public Color BorderColor { get; set; } = Color.FromArgb(225, 223, 221);

        public ModernCard()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UIHelper.GetRoundPath(rect, BorderRadius))
            using (var pen = new Pen(BorderColor, 1F))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }
    }
}
