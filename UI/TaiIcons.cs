using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ADDIN.UI
{
    public enum TaiIcon
    {
        None,
        // General
        Refresh, Undo, Trash, Close, Stop, Check, ChevronUp, ChevronDown, More, Info, Save, Sheet, Cube,
        // Drawing BOM
        BendFlat, Scribe, HoleRadius, SameParts, Compare, Curve, Export, SortList, Assembly, Balloon, SheetCheck,
        // Component Drawing
        Measure, AlignHorizontal, RotateCw, RotateCcw, Note, TextT, Eraser, DimSection, ArcRadius, DimScribe,
        DimHole, FitView, Wrench, Section,
        // Model
        HoleMake, HoleRepair, HoleCount, Pattern, Tag, Mark, Concentric, Mirror, Spline, SplineSmooth, VCut, FileSearch
    }

    // One line-icon set for the whole add-in, drawn in code so it stays sharp at
    // any DPI and takes the colour of the control that shows it. Every icon is
    // laid out on a 20 x 20 grid with a 1.5 unit stroke.
    public static class TaiIcons
    {
        public static void Draw(Graphics g, TaiIcon icon, Rectangle box, Color color)
        {
            if (icon == TaiIcon.None || box.Width <= 0 || box.Height <= 0) return;
            int size = Math.Min(box.Width, box.Height);
            float left = box.X + (box.Width - size) / 2, top = box.Y + (box.Height - size) / 2;
            GraphicsState state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.Default;
                using (Ink ink = new Ink(g, left, top, size / 20F, color)) Paint(ink, icon);
            }
            finally { g.Restore(state); }
        }

        public static TaiIcon FromLegacy(ModernButtonIcon icon)
        {
            switch (icon)
            {
                case ModernButtonIcon.CheckDfTk: return TaiIcon.BendFlat;
                case ModernButtonIcon.CheckKegaki: return TaiIcon.Scribe;
                case ModernButtonIcon.CheckRound: return TaiIcon.HoleRadius;
                case ModernButtonIcon.CheckSamePart: return TaiIcon.SameParts;
                case ModernButtonIcon.XepUnit: return TaiIcon.SortList;
                case ModernButtonIcon.OpenAssem: return TaiIcon.Assembly;
                case ModernButtonIcon.CheckBalloon: return TaiIcon.Balloon;
                case ModernButtonIcon.CheckDrawing: return TaiIcon.SheetCheck;
                case ModernButtonIcon.Refresh: return TaiIcon.Refresh;
                case ModernButtonIcon.Trash: return TaiIcon.Trash;
                case ModernButtonIcon.Cancel: return TaiIcon.Close;
                case ModernButtonIcon.CheckDxf: return TaiIcon.Compare;
                default: return TaiIcon.None;
            }
        }

        private static void Paint(Ink i, TaiIcon icon)
        {
            switch (icon)
            {
                case TaiIcon.Refresh:
                    i.ArcArrow(10, 10, 6.25F, 35, 280); break;
                case TaiIcon.Undo:
                    i.ArcArrow(10, 10, 6.25F, 145, -280); break;
                case TaiIcon.Trash:
                    i.Line(3.5F, 5.5F, 16.5F, 5.5F); i.Line(8, 5.5F, 8, 3.5F, 12, 3.5F, 12, 5.5F);
                    i.Line(5, 5.5F, 5.8F, 16.5F, 14.2F, 16.5F, 15, 5.5F);
                    i.Line(8.4F, 8.5F, 8.4F, 13.5F); i.Line(11.6F, 8.5F, 11.6F, 13.5F); break;
                case TaiIcon.Close:
                    i.Line(5.5F, 5.5F, 14.5F, 14.5F); i.Line(14.5F, 5.5F, 5.5F, 14.5F); break;
                case TaiIcon.Stop:
                    i.Rect(5.5F, 5.5F, 9, 9, 1.5F, true); break;
                case TaiIcon.Check:
                    i.Line(4.5F, 10.5F, 8.2F, 14.2F, 15.5F, 6.2F); break;
                case TaiIcon.ChevronUp:
                    i.Line(5.5F, 12.5F, 10, 8, 14.5F, 12.5F); break;
                case TaiIcon.ChevronDown:
                    i.Line(5.5F, 7.5F, 10, 12, 14.5F, 7.5F); break;
                case TaiIcon.More:
                    i.Dot(5, 10, 1.3F); i.Dot(10, 10, 1.3F); i.Dot(15, 10, 1.3F); break;
                case TaiIcon.Info:
                    i.Circle(10, 10, 7); i.Line(10, 9.5F, 10, 13.5F); i.Dot(10, 6.6F, 1); break;
                case TaiIcon.Save:
                    i.Shape(false, 3.5F, 3.5F, 13, 3.5F, 16.5F, 7, 16.5F, 16.5F, 3.5F, 16.5F);
                    i.Line(6.5F, 3.5F, 6.5F, 7.5F, 12, 7.5F, 12, 3.5F); i.Rect(6.5F, 11, 7, 5.5F, 0.8F, true); break;
                case TaiIcon.Sheet:
                    Page(i, false); i.Line(7.5F, 10, 12.5F, 10); i.Line(7.5F, 13.5F, 12.5F, 13.5F); break;
                case TaiIcon.Cube:
                    i.Shape(false, 10, 2.5F, 16.5F, 6.25F, 16.5F, 13.75F, 10, 17.5F, 3.5F, 13.75F, 3.5F, 6.25F);
                    i.Line(3.5F, 6.25F, 10, 10, 16.5F, 6.25F); i.Line(10, 10, 10, 17.5F); break;

                case TaiIcon.BendFlat:
                    // A bent profile, the flat it unfolds to, and the unfolding move.
                    i.Line(11, 3.5F, 11, 13); i.Curve(11, 13, 11, 15, 12, 16, 14, 16); i.Line(14, 16, 17, 16);
                    i.Dashed(3, 16, 9.5F, 16); i.ArcArrow(11, 16, 7.5F, 250, -50, 2.4F); break;
                case TaiIcon.Scribe:
                    i.Rect(3, 5.5F, 14, 9, 1.5F, true); i.Dashed(10, 7, 10, 13.2F);
                    i.Line(10, 3, 10, 5.5F); i.Line(10, 14.5F, 10, 17); break;
                case TaiIcon.HoleRadius:
                    i.Circle(9.5F, 10.5F, 6, true); i.Dot(9.5F, 10.5F, 1.1F);
                    i.Line(9.5F, 10.5F, 13.74F, 6.26F, 16.5F, 3.5F); break;
                case TaiIcon.SameParts:
                    i.Rect(3, 3, 9.5F, 9.5F, 1.8F); i.Rect(7.5F, 7.5F, 9.5F, 9.5F, 1.8F, true); break;
                case TaiIcon.Compare:
                    i.Arrow(3.5F, 7, 16.5F, 7); i.Arrow(16.5F, 13, 3.5F, 13); break;
                case TaiIcon.Curve:
                    i.Curve(3.5F, 14.5F, 7, 3, 12, 17, 16.5F, 5.5F); i.Dot(3.5F, 14.5F, 1.5F); i.Dot(16.5F, 5.5F, 1.5F); break;
                case TaiIcon.Export:
                    i.Arrow(10, 12.5F, 10, 3); i.Line(4, 11.5F, 4, 15, 5.5F, 16.5F, 14.5F, 16.5F, 16, 15, 16, 11.5F); break;
                case TaiIcon.SortList:
                    i.Line(3, 5.5F, 10.5F, 5.5F); i.Line(3, 10, 8.5F, 10); i.Line(3, 14.5F, 6.5F, 14.5F);
                    i.Arrow(14.5F, 4, 14.5F, 16); break;
                case TaiIcon.Assembly:
                    i.Rect(3, 10.5F, 6.5F, 6.5F, 1); i.Rect(10.5F, 10.5F, 6.5F, 6.5F, 1); i.Rect(6.75F, 3, 6.5F, 6.5F, 1, true); break;
                case TaiIcon.Balloon:
                    // A numbered balloon and its leader, as on the drawing.
                    i.Circle(11.5F, 7.5F, 5, true); i.Arrow(8, 11, 3.2F, 16.8F, 2.4F);
                    i.Line(10.2F, 6.5F, 11.7F, 5.3F, 11.7F, 9.8F); break;
                case TaiIcon.SheetCheck:
                    Page(i, false); i.Line(7.2F, 11.8F, 9.2F, 13.8F, 12.8F, 9.6F); break;

                case TaiIcon.Measure:
                    i.Rect(8, 3, 9, 9, 1.5F, true); i.Arrow(12.5F, 16, 17, 16, 2); i.Arrow(12.5F, 16, 8, 16, 2);
                    i.Arrow(4, 7.5F, 4, 3, 2); i.Arrow(4, 7.5F, 4, 12, 2); break;
                case TaiIcon.AlignHorizontal:
                    i.Line(2.5F, 15.5F, 17.5F, 15.5F); i.Rect(5, 7.5F, 10, 5.5F, 1.2F, true); break;
                case TaiIcon.RotateCw:
                    i.ArcArrow(10, 11, 6.5F, 200, 140, 2.4F); i.Rect(7.5F, 9.5F, 5, 5, 1, true); break;
                case TaiIcon.RotateCcw:
                    i.ArcArrow(10, 11, 6.5F, 340, -140, 2.4F); i.Rect(7.5F, 9.5F, 5, 5, 1, true); break;
                case TaiIcon.Note:
                    i.Rect(3, 3.5F, 14, 13, 2); i.Line(6, 7.5F, 14, 7.5F); i.Line(6, 10.5F, 14, 10.5F); i.Line(6, 13.5F, 10.5F, 13.5F); break;
                case TaiIcon.TextT:
                    i.Line(5, 6.5F, 5, 4.5F, 15, 4.5F, 15, 6.5F); i.Line(10, 4.5F, 10, 15.5F); i.Line(8, 15.5F, 12, 15.5F); break;
                case TaiIcon.Eraser:
                    i.Shape(true, 11.5F, 3, 17, 8.5F, 9.5F, 16, 6.5F, 16, 3, 12.5F);
                    i.Line(6.6F, 8.5F, 11.8F, 13.7F); i.Line(9.5F, 16, 17, 16); break;
                case TaiIcon.DimSection:
                    i.Rect(4, 3, 12, 7.5F, 1); i.Line(5.5F, 10.5F, 13, 3); i.Line(9.5F, 10.5F, 16, 4);
                    Span(i, 4, 16, 15.3F); break;
                case TaiIcon.ArcRadius:
                    i.Arc(5, 15, 10, 270, 90); i.Dashed(5, 15, 5, 6.8F); i.Dashed(5, 15, 13.2F, 15);
                    i.Dot(5, 15, 1.3F); i.Dot(5, 5, 1.3F); i.Dot(15, 15, 1.3F); break;
                case TaiIcon.DimScribe:
                    i.Line(4, 3, 4, 17); i.Dashed(14, 3, 14, 17); i.Arrow(9, 10, 4.6F, 10, 2); i.Arrow(9, 10, 13.4F, 10, 2); break;
                case TaiIcon.DimHole:
                    i.Circle(7.5F, 12.5F, 4.5F, true); i.Dot(7.5F, 12.5F, 0.9F);
                    i.Line(10.7F, 9.3F, 13.5F, 6.5F, 17.5F, 6.5F); break;
                case TaiIcon.FitView:
                    i.Line(3, 7, 3, 3, 7, 3); i.Line(13, 3, 17, 3, 17, 7); i.Line(17, 13, 17, 17, 13, 17);
                    i.Line(7, 17, 3, 17, 3, 13); i.Rect(7, 7, 6, 6, 1, true); break;
                case TaiIcon.Wrench:
                    i.Arc(13.3F, 6.7F, 3.4F, 355, 280); i.Line(10.9F, 9.1F, 4.5F, 15.5F); break;
                case TaiIcon.Section:
                    i.Line(3.5F, 11, 16.5F, 11); i.Arrow(3.5F, 11, 3.5F, 4.5F, 2.2F); i.Arrow(16.5F, 11, 16.5F, 4.5F, 2.2F);
                    i.Rect(6, 13.5F, 8, 4, 1, true); break;

                case TaiIcon.HoleMake:
                    i.Circle(8.5F, 11.5F, 5.5F, true); i.Line(15.5F, 2.5F, 15.5F, 7.5F); i.Line(13, 5, 18, 5); break;
                case TaiIcon.HoleRepair:
                    i.Circle(7.5F, 12.5F, 4.5F, true); i.Arc(14.6F, 5.4F, 2.7F, 355, 280); i.Line(12.7F, 7.3F, 10.9F, 9.1F); break;
                case TaiIcon.HoleCount:
                    i.Circle(6, 6, 2.6F); i.Circle(14, 6, 2.6F); i.Circle(6, 14, 2.6F);
                    i.Line(13, 11.6F, 12.2F, 16.4F); i.Line(15.8F, 11.6F, 15, 16.4F);
                    i.Line(11.4F, 13.2F, 16.8F, 13.2F); i.Line(11, 14.9F, 16.4F, 14.9F); break;
                case TaiIcon.Pattern:
                    foreach (float y in new[] { 5F, 10F, 15F })
                        foreach (float x in new[] { 5F, 10F, 15F }) i.Dot(x, y, 1.25F);
                    break;
                case TaiIcon.Tag:
                    i.Shape(false, 3.5F, 4, 11, 4, 17, 10, 11, 16, 3.5F, 16); i.Dot(11.8F, 10, 1.3F); break;
                case TaiIcon.Mark:
                    i.Circle(10, 10, 6.5F); i.Dot(10, 10, 2.6F); break;
                case TaiIcon.Concentric:
                    i.Circle(10, 10, 6); i.Circle(10, 10, 2.4F, true); i.Line(10, 1.8F, 10, 4.2F); i.Line(10, 15.8F, 10, 18.2F);
                    i.Line(1.8F, 10, 4.2F, 10); i.Line(15.8F, 10, 18.2F, 10); break;
                case TaiIcon.Mirror:
                    i.Dashed(10, 2.5F, 10, 17.5F); i.Shape(false, 7.5F, 5, 7.5F, 15, 2.5F, 15); i.Shape(true, 12.5F, 5, 12.5F, 15, 17.5F, 15); break;
                case TaiIcon.Spline:
                    i.Curve(3, 14.5F, 6.5F, 2.5F, 12.5F, 17.5F, 17, 6); i.Dot(3, 14.5F, 1.4F); i.Dot(9.6F, 10.1F, 1.4F); i.Dot(17, 6, 1.4F); break;
                case TaiIcon.SplineSmooth:
                    i.Curve(2.5F, 13.5F, 5.5F, 2.5F, 9.5F, 14.5F, 13.5F, 5);
                    i.Line(15.5F, 10.8F, 15.5F, 15.2F); i.Line(13.3F, 13, 17.7F, 13); break;
                case TaiIcon.VCut:
                    i.Line(2.5F, 8, 7, 8, 10, 12.5F, 13, 8, 17.5F, 8); i.Line(2.5F, 14.5F, 17.5F, 14.5F); break;
                case TaiIcon.FileSearch:
                    Page(i, false); i.Circle(9.5F, 11.5F, 2.5F); i.Line(11.3F, 13.3F, 13, 15); break;
            }
        }

        private static void Page(Ink i, bool soft)
        {
            i.Shape(soft, 4.5F, 2.5F, 12, 2.5F, 15.5F, 6, 15.5F, 17.5F, 4.5F, 17.5F);
            i.Line(12, 2.5F, 12, 6, 15.5F, 6);
        }

        // A dimension line with its two extension lines.
        private static void Span(Ink i, float x1, float x2, float y)
        {
            float middle = (x1 + x2) / 2;
            i.Line(x1, y - 2.3F, x1, y + 2.2F); i.Line(x2, y - 2.3F, x2, y + 2.2F);
            i.Arrow(middle, y, x1 + 0.6F, y, 2); i.Arrow(middle, y, x2 - 0.6F, y, 2);
        }

        private sealed class Ink : IDisposable
        {
            private readonly Graphics g;
            private readonly float left, top, unit;
            private readonly Pen pen;
            private readonly SolidBrush solid, soft;

            internal Ink(Graphics g, float left, float top, float unit, Color color)
            {
                this.g = g; this.left = left; this.top = top; this.unit = unit;
                pen = new Pen(color, Math.Max(1.2F, 1.5F * unit)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                solid = new SolidBrush(color);
                soft = new SolidBrush(Color.FromArgb(40, color));
            }

            private PointF P(float x, float y) { return new PointF(left + x * unit, top + y * unit); }

            private PointF[] Points(float[] values)
            {
                PointF[] points = new PointF[values.Length / 2];
                for (int n = 0; n < points.Length; n++) points[n] = P(values[n * 2], values[n * 2 + 1]);
                return points;
            }

            internal void Line(params float[] values) { g.DrawLines(pen, Points(values)); }

            internal void Shape(bool fill, params float[] values)
            {
                PointF[] points = Points(values);
                if (fill) g.FillPolygon(soft, points);
                g.DrawPolygon(pen, points);
            }

            internal void Rect(float x, float y, float width, float height, float radius, bool fill = false)
            {
                PointF origin = P(x, y);
                float w = width * unit, h = height * unit, d = Math.Min(Math.Min(w, h), radius * unit * 2);
                using (GraphicsPath path = new GraphicsPath())
                {
                    if (d <= 0.01F) path.AddRectangle(new RectangleF(origin.X, origin.Y, w, h));
                    else
                    {
                        path.AddArc(origin.X, origin.Y, d, d, 180, 90);
                        path.AddArc(origin.X + w - d, origin.Y, d, d, 270, 90);
                        path.AddArc(origin.X + w - d, origin.Y + h - d, d, d, 0, 90);
                        path.AddArc(origin.X, origin.Y + h - d, d, d, 90, 90);
                        path.CloseFigure();
                    }
                    if (fill) g.FillPath(soft, path);
                    g.DrawPath(pen, path);
                }
            }

            internal void Circle(float cx, float cy, float radius, bool fill = false)
            {
                PointF origin = P(cx - radius, cy - radius);
                float d = radius * 2 * unit;
                if (fill) g.FillEllipse(soft, origin.X, origin.Y, d, d);
                g.DrawEllipse(pen, origin.X, origin.Y, d, d);
            }

            internal void Dot(float cx, float cy, float radius)
            {
                PointF origin = P(cx - radius, cy - radius);
                g.FillEllipse(solid, origin.X, origin.Y, radius * 2 * unit, radius * 2 * unit);
            }

            internal void Arc(float cx, float cy, float radius, float start, float sweep)
            {
                PointF origin = P(cx - radius, cy - radius);
                g.DrawArc(pen, origin.X, origin.Y, radius * 2 * unit, radius * 2 * unit, start, sweep);
            }

            internal void Curve(params float[] values) { g.DrawBeziers(pen, Points(values)); }

            internal void Dashed(float x1, float y1, float x2, float y2)
            {
                using (Pen dash = new Pen(pen.Color, pen.Width) { DashStyle = DashStyle.Custom, DashPattern = new[] { 1.6F, 1.4F } })
                    g.DrawLine(dash, P(x1, y1), P(x2, y2));
            }

            internal void Arrow(float x1, float y1, float x2, float y2, float head = 2.6F)
            {
                g.DrawLine(pen, P(x1, y1), P(x2, y2));
                double length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                if (length > 0.001) Head(x2, y2, (x2 - x1) / length, (y2 - y1) / length, head);
            }

            // Angles as in GDI+: 0 at three o'clock, positive sweep clockwise.
            internal void ArcArrow(float cx, float cy, float radius, float start, float sweep, float head = 2.6F)
            {
                Arc(cx, cy, radius, start, sweep);
                double end = (start + sweep) * Math.PI / 180;
                double sign = sweep >= 0 ? 1 : -1;
                Head(cx + radius * (float)Math.Cos(end), cy + radius * (float)Math.Sin(end),
                    -Math.Sin(end) * sign, Math.Cos(end) * sign, head);
            }

            private void Head(float tipX, float tipY, double directionX, double directionY, float size)
            {
                const double Spread = 35 * Math.PI / 180;
                PointF[] points = new PointF[3];
                points[0] = Wing(tipX, tipY, directionX, directionY, size, Spread);
                points[1] = P(tipX, tipY);
                points[2] = Wing(tipX, tipY, directionX, directionY, size, -Spread);
                g.DrawLines(pen, points);
            }

            private PointF Wing(float tipX, float tipY, double dx, double dy, float size, double angle)
            {
                double x = dx * Math.Cos(angle) - dy * Math.Sin(angle), y = dx * Math.Sin(angle) + dy * Math.Cos(angle);
                return P(tipX - (float)(x * size), tipY - (float)(y * size));
            }

            public void Dispose() { pen.Dispose(); solid.Dispose(); soft.Dispose(); }
        }
    }
}
