using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace ADDIN.UI
{
    public static class UIHelper
    {
        public static void EnableDoubleBuffer(Control control)
        {
            if (control == null) return;
            try
            {
                typeof(Control).InvokeMember(
                    "DoubleBuffered",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                    null,
                    control,
                    new object[] { true });
            }
            catch
            {
            }
        }
        public static GraphicsPath GetRoundPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float r = radius * 2F;
            if (r > rect.Width) r = rect.Width;
            if (r > rect.Height) r = rect.Height;
            if (r <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            RectangleF rf = new RectangleF(rect.X, rect.Y, rect.Width, rect.Height);
            path.StartFigure();
            path.AddArc(rf.X, rf.Y, r, r, 180, 90);
            path.AddArc(rf.Right - r, rf.Y, r, r, 270, 90);
            path.AddArc(rf.Right - r, rf.Bottom - r, r, r, 0, 90);
            path.AddArc(rf.X, rf.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath GetRegionPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float r = radius * 2F;
            if (r > rect.Width) r = rect.Width;
            if (r > rect.Height) r = rect.Height;
            if (r <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            RectangleF rf = new RectangleF(rect.X, rect.Y, rect.Width, rect.Height);
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
