using System.Drawing;
using System.Drawing.Drawing2D;

namespace ADDIN.UI
{
    public static class UIHelper
    {
        public static GraphicsPath GetRoundPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float r = radius * 2F;
            // Thu nhỏ rect đi 0.5 pixel để đường viền không bị tràn ra ngoài gây đọng nét đen ở góc
            RectangleF rf = new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1f, rect.Height - 1f);
            
            path.StartFigure();
            path.AddArc(rf.X, rf.Y, r, r, 180, 90);
            path.AddArc(rf.Right - r, rf.Y, r, r, 270, 90);
            path.AddArc(rf.Right - r, rf.Bottom - r, r, r, 0, 90);
            path.AddArc(rf.X, rf.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
