using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands;

namespace ADDIN.UI
{
    internal sealed class BendMinimumSectionPreview : Control
    {
        private BendMinimumPreview section;
        private BendMinimumRowResult result;
        private int number, activeSide = -1;
        private readonly Dictionary<int, RectangleF> hits = new Dictionary<int, RectangleF>();
        internal event Action<int> DatumSelected;
        internal BendMinimumSectionPreview()
        {
            Name = "minimumSectionPreview"; Dock = DockStyle.Fill; BackColor = Color.White;
            DoubleBuffered = true; ResizeRedraw = true; Font = new Font("Segoe UI", 10F);
            AccessibleName = "Mặt cắt thực tế và kích thước phủ bì tại nếp chấn được chọn";
        }
        internal void SetSection(BendMinimumPreview preview, BendMinimumRowResult check, int bendNumber, int side)
        { section = preview; result = check; number = bendNumber; activeSide = side; Invalidate(); }
        private static string Mm(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static IEnumerable<SectionProfilePlanner.Point> Samples(SectionProfilePlanner.Curve curve)
        {
            if (!curve.IsArc) { yield return curve.A; yield return curve.B; yield break; }
            double radius = (curve.A - curve.Center).Length, start = Math.Atan2(curve.A.Y - curve.Center.Y, curve.A.X - curve.Center.X);
            int steps = Math.Max(8, (int)Math.Ceiling(Math.Abs(curve.SweepAngleRadians) * 20));
            for (int i = 0; i <= steps; i++) { double a = start + curve.SweepAngleRadians * i / steps;
                yield return curve.Center + new SectionProfilePlanner.Point(Math.Cos(a), Math.Sin(a)) * radius; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); hits.Clear(); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var border = new Pen(Color.FromArgb(203, 213, 225))) g.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            using (var titleFont = new Font("Segoe UI", 11F, FontStyle.Bold))
                TextRenderer.DrawText(g, "MẶT CẮT THỰC TẾ · NẾP CHẤN #" + number, titleFont, new Rectangle(12, 9, Math.Max(0, Width - 180), 28), Color.FromArgb(30, 41, 59), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            if (section == null || section.Error != null || section.Curves.Count == 0)
            {
                TextRenderer.DrawText(g, section?.Error ?? "Chọn một đường chấn để xem mặt cắt.", Font, new Rectangle(24, 60, Math.Max(1, Width - 48), Math.Max(1, Height - 90)), Color.FromArgb(100, 116, 139), TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter); return;
            }
            var paths = section.Curves.Select(c => Samples(c).ToArray()).ToList();
            var points = paths.SelectMany(p => p).Concat(section.Datums.SelectMany(d => new[] { d.Start, d.End })).ToList();
            double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
            // Leave fixed pixel margins for dimensions; scale uniformly.
            double spanX = Math.Max(.001, maxX - minX), spanY = Math.Max(.001, maxY - minY);
            double scale = Math.Max(1, Math.Min(Math.Max(40, Width - 360) / spanX, Math.Max(40, Height - 170) / spanY));
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            Func<SectionProfilePlanner.Point, PointF> project = p => new PointF((float)(Width / 2d + (p.X - cx) * scale), (float)(Height / 2d + (cy - p.Y) * scale));
            using (var outline = new Pen(Color.FromArgb(51, 65, 85), 1.7F)) foreach (var path in paths) g.DrawLines(outline, path.Select(project).ToArray());
            var outlinePoints = paths.SelectMany(p => p).Select(project).ToArray();
            float left = outlinePoints.Min(p => p.X), right = outlinePoints.Max(p => p.X), top = outlinePoints.Min(p => p.Y), bottom = outlinePoints.Max(p => p.Y);
            using (var fontDim = new Font("Segoe UI", 10F, FontStyle.Bold))
            foreach (var datum in section.Datums)
            {
                if (result == null || datum.Side >= result.Sides.Count) continue;
                var side = result.Sides[datum.Side]; var m = side.Measure;
                bool ng = side.Check.Status == BendMinimumStatus.BelowMinimum;
                Color color = ng ? Color.FromArgb(220, 38, 38) : Color.FromArgb(2, 132, 199);
                var a = project(datum.Start); var b = project(datum.End); float dx = b.X - a.X, dy = b.Y - a.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy); if (length < 1) continue;
                bool horizontal = Math.Abs(dx) >= Math.Abs(dy), firstSide = datum.Side == 0;
                float lane = horizontal ? (firstSide ? top - 27 : bottom + 27) : (firstSide ? left - 27 : right + 27);
                var da = horizontal ? new PointF(a.X, lane) : new PointF(lane, a.Y);
                var db = horizontal ? new PointF(b.X, lane) : new PointF(lane, b.Y);
                using (var extension = new Pen(Color.FromArgb(150, color), 1) { DashStyle = DashStyle.Dash }) { g.DrawLine(extension, a, da); g.DrawLine(extension, b, db); }
                using (var arrow = new Pen(color, activeSide == datum.Side ? 2.4F : 1.6F))
                using (var cap = new AdjustableArrowCap(4, 4, true)) { arrow.CustomStartCap = cap; arrow.CustomEndCap = cap; g.DrawLine(arrow, da, db); }
                string label = (firstSide ? "Phía A: " : "Phía B: ") + m.Shape + "/" + m.Dimension + " = " + Mm(m.ValueMm) + " mm";
                string comparison = (side.LimitMm.HasValue ? (ng ? "< Min " : "≥ Min ") + Mm(side.LimitMm.Value) + " mm" : "Chưa có giới hạn") + (ng ? " · NG (Lỗi)" : side.Check.Status == BendMinimumStatus.MeetsMinimum ? " · Đạt" : " · ?");
                var size1 = TextRenderer.MeasureText(label, fontDim); var size2 = TextRenderer.MeasureText(comparison, fontDim);
                var size = new Size(Math.Max(size1.Width, size2.Width), 46);
                float px = (da.X + db.X) / 2, py = (da.Y + db.Y) / 2;
                float labelX = horizontal ? px - size.Width / 2F : firstSide ? lane - size.Width - 9 : lane + 9;
                float labelY = horizontal ? firstSide ? lane - 48 : lane + 7 : py - 22;
                var box = new RectangleF(Math.Max(7, Math.Min(Width - size.Width - 13, labelX)), Math.Max(38, Math.Min(Height - 62, labelY)), size.Width + 8, 46);
                using (var white = new SolidBrush(Color.FromArgb(245, 255, 255, 255))) g.FillRectangle(white, box);
                TextRenderer.DrawText(g, label + "\r\n" + comparison, fontDim, Rectangle.Round(box), color, TextFormatFlags.Left | TextFormatFlags.NoPadding);
                hits[datum.Side] = box;
            }
            var bend = project(section.Bend); var badge = new RectangleF(bend.X - 14, bend.Y - 14, 28, 28);
            using (var fill = new SolidBrush(Color.FromArgb(29, 78, 216))) g.FillEllipse(fill, badge);
            using (var fontBadge = new Font("Segoe UI", 10F, FontStyle.Bold))
                TextRenderer.DrawText(g, number.ToString(CultureInfo.InvariantCulture), fontBadge, Rectangle.Round(badge), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using (var subFont = new Font("Segoe UI", 9.5F, FontStyle.Regular))
                TextRenderer.DrawText(g, "Đơn vị: mm (phủ bì)", subFont, new Rectangle(Math.Max(220, Width - 160), 9, 150, 26), Color.FromArgb(100, 116, 139), TextFormatFlags.Right);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        { base.OnMouseMove(e); Cursor = hits.Values.Any(r => r.Contains(e.Location)) ? Cursors.Hand : Cursors.Default; }
        protected override void OnMouseClick(MouseEventArgs e)
        { base.OnMouseClick(e); if (e.Button == MouseButtons.Left) foreach (var hit in hits) if (hit.Value.Contains(e.Location)) { DatumSelected?.Invoke(hit.Key); break; } }
    }
}
