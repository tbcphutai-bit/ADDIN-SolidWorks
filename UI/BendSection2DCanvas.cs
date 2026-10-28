// CHECK CHAN V4.4 - Excel-first, single-section visual viewer, replacing V4.3 in-place.
// Target: ADDIN, C# 7.3, .NET Framework 4.8, WinForms. No SOLIDWORKS COM calls.
// Input is COPIED numerical 2D geometry. The picture is not collision evidence.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands.BendTools;

namespace ADDIN.UI
{
    internal sealed class BendSection2DScene
    {
        internal Func<bool> EvidenceCurrent;
        internal bool Current {get {try{return EvidenceCurrent==null||EvidenceCurrent();}catch{return false;}}}
        internal string PartTitle = "Chưa chọn nếp chấn";
        internal string PartEvidence = "Chưa có mặt cắt CAD cho nếp này.";
        internal string ToolTitle = "Chưa chọn bộ dao";
        internal string ToolEvidence = "Chưa xác minh tọa độ gá dao.";
        internal string ExcelCaseCaption = "Tự dò Excel: chưa xác minh trường hợp áp dụng";
        internal string ExcelCaseSource = "深曲げ限界(4).xlsx";
        internal string DecisionText = "CHƯA XÁC MINH · Ưu tiên Excel trước khi xét CAD";
        // The source CAD curves below are preserved in-memory for provenance,
        // but must NOT be placed on the Part without reviewed transforms.
        internal IReadOnlyList<PointF[]> RegisteredPunch = new PointF[0][];
        internal IReadOnlyList<PointF[]> RegisteredHolder = new PointF[0][];
        internal IReadOnlyList<PointF[]> VerifiedPartContact = new PointF[0][];
        internal IReadOnlyList<PointF[]> VerifiedToolContact = new PointF[0][];
        // Numeric solid/axial witness POINTS are not proof of which polygon
        // EDGE was hit. Render a point marker, NEVER invent a red edge.
        internal IReadOnlyList<PointF> VerifiedContactPoints = new PointF[0];
        internal bool ContactPointProven;
        internal bool PlacementRegistered;
        internal bool ViolationProven;
        internal bool CollisionProven;
        internal string ProofReference;
        internal IReadOnlyList<PointF[]> Part = new PointF[0][];
        internal IReadOnlyList<PointF[]> Punch = new PointF[0][];
        internal IReadOnlyList<PointF[]> Holder = new PointF[0][];
        internal string SourceKey;
        internal int FeatureId;
        internal string ToolId;

        // Conversion is a VIEWER-ONLY polyline approximation. It must NEVER be
        // passed to the collision kernel as source geometry or evidence.
        internal static IReadOnlyList<PointF[]> CopyCurves(IEnumerable<ToolCurve> curves)
        {
            var result = new List<PointF[]>();
            if (curves == null) return result.AsReadOnly();
            int budget = 20000;
            foreach (var c in curves)
            {
                if (budget-- <= 0) throw new InvalidOperationException("SECTION_VIEW_CURVE_LIMIT");
                if (c == null || (c.Kind != "LINE" && c.Kind != "ARC"))
                    throw new InvalidOperationException("SECTION_VIEW_UNSUPPORTED_CURVE");
                int samples = 2;
                if (c.Kind == "ARC")
                {
                    if (!Finite(c.RadiusMm) || c.RadiusMm <= 0 || !Finite(c.SweepRadians) ||
                        Math.Abs(c.SweepRadians) > 2.0 * Math.PI + 1e-6)
                        throw new InvalidOperationException("SECTION_VIEW_INVALID_ARC");
                    // Angular sampling is only for anti-aliased display, max 97 points/arc.
                    samples = Math.Max(9, Math.Min(97, (int)Math.Ceiling(Math.Abs(c.SweepRadians) / (Math.PI / 48)) + 1));
                }
                var pts = new PointF[samples];
                for (int i = 0; i < samples; ++i)
                {
                    double t = (double)i / (samples - 1);
                    double x, y;
                    if (c.Kind == "LINE")
                    {
                        x = c.Start.X + (c.End.X - c.Start.X) * t;
                        y = c.Start.Y + (c.End.Y - c.Start.Y) * t;
                    }
                    else
                    {
                        double origin = Math.Atan2(c.Start.Y - c.Center.Y, c.Start.X - c.Center.X);
                        double angle = origin + c.SweepRadians * t;
                        x = c.Center.X + c.RadiusMm * Math.Cos(angle);
                        y = c.Center.Y + c.RadiusMm * Math.Sin(angle);
                    }
                    if (!Finite(x) || !Finite(y) || Math.Abs(x) > 1e7 || Math.Abs(y) > 1e7)
                        throw new InvalidOperationException("SECTION_VIEW_NONFINITE_POINT");
                    pts[i] = new PointF((float)x, (float)y);
                }
                result.Add(pts);
            }
            return result.AsReadOnly();
        }

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }

    // V4.4 replaces the V4.3 three independent fit windows with ONE workpiece
    // coordinate drawing. No unregistered punch/holder source geometry is rendered.
    internal sealed class BendSection2DCanvas : Control
    {
        private BendSection2DScene scene = new BendSection2DScene();
        private float zoom = 1f;
        private PointF pan = PointF.Empty;
        private bool dragging;
        private Point mouse;
        private readonly Font headerFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        private readonly Font smallFont = new Font("Segoe UI", 8.5f);
        private readonly Font statusFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        private static readonly Color Orange = Color.FromArgb(211, 116, 33);
        private static readonly Color PunchBlue = Color.FromArgb(41, 111, 174);
        private static readonly Color HolderGray = Color.FromArgb(90, 106, 124);
        private static readonly Color ContactRed = Color.FromArgb(206, 45, 45);

        internal BendSection2DCanvas()
        {
            Name = "checkChanSectionV44OneCanvas";
            Dock = DockStyle.Fill;
            BackColor = Color.White;
            DoubleBuffered = true;
            TabStop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        internal void SetScene(BendSection2DScene input)
        {
            if (InvokeRequired) throw new InvalidOperationException("SECTION_CANVAS_REQUIRES_UI_THREAD");
            var next = input ?? new BendSection2DScene();
            bool sameBend = scene != null && next.FeatureId > 0 &&
                string.Equals(next.SourceKey, scene.SourceKey, StringComparison.Ordinal) &&
                next.FeatureId == scene.FeatureId;
            // Invalid-state guards: a verified overlap MUST be attached to a
            // registered tool pose or a reviewed Excel source, not an artist overlay.
            if (!next.PlacementRegistered)
            {
                next.RegisteredPunch = new PointF[0][];
                next.RegisteredHolder = new PointF[0][];
            }
            if (string.IsNullOrWhiteSpace(next.ProofReference) ||
                (!next.PlacementRegistered && next.CollisionProven))
            {
                next.ViolationProven = false;
                next.CollisionProven = false;
                next.VerifiedPartContact = new PointF[0][];
                next.VerifiedToolContact = new PointF[0][];
                next.VerifiedContactPoints = new PointF[0];
                next.ContactPointProven = false;
            }
            scene = next;
            if (!sameBend) { zoom = 1f; pan = PointF.Empty; }
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { dragging = true; mouse = e.Location; Capture = true; Focus(); }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            pan = new PointF(pan.X + e.X - mouse.X, pan.Y + e.Y - mouse.Y);
            mouse = e.Location; Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        { dragging = false; Capture = false; base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            zoom = Math.Max(0.2f, Math.Min(20f, zoom * (e.Delta > 0 ? 1.18f : .85f)));
            Invalidate();
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        { zoom = 1f; pan = PointF.Empty; Invalidate(); base.OnMouseDoubleClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (Width < 190 || Height < 180) return;

            if(!scene.Current)
            {
                TextRenderer.DrawText(g,"Nguồn đã thay đổi · cần Refresh",statusFont,ClientRectangle,Orange,
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
                return;
            }
            int pad = 14;
            var top = new Rectangle(pad, 5, Math.Max(1, Width - pad * 2), 28);
            var drawing = new Rectangle(pad, 37, Math.Max(1, Width - pad * 2), Math.Max(40, Height - 81));
            var bottom = new Rectangle(pad, drawing.Bottom + 5, Math.Max(1, Width - pad * 2), 28);

            TextRenderer.DrawText(g, "MẶT CẮT 2D", headerFont, top,
                Color.FromArgb(29, 49, 75), TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (var fill = new SolidBrush(Color.FromArgb(251, 252, 254)))
                g.FillRectangle(fill, drawing);
            using (var border = new Pen(Color.FromArgb(217, 225, 237)))
                g.DrawRectangle(border, drawing);

            var registered = scene.PlacementRegistered && scene.RegisteredPunch != null &&
                scene.RegisteredHolder != null &&
                (scene.RegisteredPunch.Count > 0 || scene.RegisteredHolder.Count > 0);
            var sources = new List<IReadOnlyList<PointF[]>> { scene.Part };
            if (registered) { sources.Add(scene.RegisteredPunch); sources.Add(scene.RegisteredHolder); }
            var points = sources.Where(x => x != null).SelectMany(x => x).Where(x => x != null).SelectMany(x => x).ToArray();
            var box = points.Length == 0 ? (RectangleF?)null : Bounds(points);
            var viewport = Rectangle.Inflate(drawing, -18, -17);
            if (box != null)
            {
                RectangleF bb = box.Value;
                float fw = Math.Max(.01f, bb.Width), fh = Math.Max(.01f, bb.Height);
                float factor = Math.Min(viewport.Width / fw, viewport.Height / fh) * .90f * zoom;
                float cx = bb.Left + bb.Width / 2, cy = bb.Top + bb.Height / 2;
                Func<PointF, PointF> map = pt => new PointF(
                    viewport.Left + viewport.Width / 2f + (pt.X - cx) * factor + pan.X,
                    viewport.Top + viewport.Height / 2f - (pt.Y - cy) * factor + pan.Y);
                var old = g.Save();
                g.SetClip(viewport);
                Draw(g, scene.Part, Orange, 2.7f, map);
                if (registered)
                {
                    Draw(g, scene.RegisteredPunch, PunchBlue, 2.3f, map);
                    Draw(g, scene.RegisteredHolder, HolderGray, 2.3f, map);
                }
                if (scene.ViolationProven || scene.CollisionProven)
                {
                    Draw(g, scene.VerifiedPartContact, ContactRed, 5.2f, map);
                    if (registered) Draw(g, scene.VerifiedToolContact, ContactRed, 4.5f, map);
                }
                if(scene.ContactPointProven && !string.IsNullOrWhiteSpace(scene.ProofReference) &&
                    scene.VerifiedContactPoints!=null)
                {
                    using(var outer=new Pen(ContactRed,2.5f))
                    using(var fill=new SolidBrush(Color.FromArgb(250,232,232)))
                        foreach(var witness in scene.VerifiedContactPoints)
                        {
                            var q=map(witness);
                            g.FillEllipse(fill,q.X-5,q.Y-5,10,10);
                            g.DrawEllipse(outer,q.X-5,q.Y-5,10,10);
                        }
                }
                g.Restore(old);
                DrawLegend(g, drawing);
            }
            else
            {
                TextRenderer.DrawText(g, Trim(scene.PartEvidence, 210), smallFont, viewport,
                    Color.FromArgb(100, 116, 139), TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
            string message = scene.ContactPointProven && !string.IsNullOrWhiteSpace(scene.ProofReference) ?
                "Điểm va chạm xác minh bằng số · chưa xác định cạnh · " + scene.ProofReference :
                (scene.ViolationProven || scene.CollisionProven) &&
                !string.IsNullOrWhiteSpace(scene.ProofReference) ?
                "Đã xác minh vùng vi phạm · " + scene.ProofReference :
                registered ? "Đã ghép dao theo tọa độ gá được cung cấp" :
                box.HasValue ? "Mặt cắt Part · chưa xác minh vị trí gá dao" :
                "Chưa có mặt cắt CAD cho nếp này";
            TextRenderer.DrawText(g, Trim(message, 140), smallFont, bottom,
                Color.FromArgb(89, 102, 119), TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static void Draw(Graphics g, IReadOnlyList<PointF[]> curves, Color color, float width,
            Func<PointF, PointF> map)
        {
            if (curves == null) return;
            using (var pen = new Pen(color, width) { LineJoin = LineJoin.Round,
                StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                foreach (var curve in curves)
                {
                    if (curve == null || curve.Length < 2) continue;
                    PointF[] pixels = curve.Select(map).ToArray();
                    g.DrawLines(pen, pixels);
                }
            }
        }
        private static RectangleF? Bounds(PointF[] pts)
        {
            if (pts == null || pts.Length == 0) return null;
            float minX = pts.Min(p => p.X), minY = pts.Min(p => p.Y);
            float maxX = pts.Max(p => p.X), maxY = pts.Max(p => p.Y);
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }
        private void DrawLegend(Graphics g, Rectangle r)
        {
            using (var p = new Pen(Orange, 3)) g.DrawLine(p, r.Left + 12, r.Top + 18, r.Left + 28, r.Top + 18);
            TextRenderer.DrawText(g, "Part", smallFont,
                new Rectangle(r.Left + 32, r.Top + 9, 44, 17), Color.FromArgb(75, 88, 105));
            if (scene.PlacementRegistered)
            {
                using (var p = new Pen(PunchBlue, 3)) g.DrawLine(p, r.Left + 80, r.Top + 18, r.Left + 96, r.Top + 18);
                TextRenderer.DrawText(g, "Chày", smallFont,
                    new Rectangle(r.Left + 100, r.Top + 9, 46, 17), Color.FromArgb(75, 88, 105));
                using (var p = new Pen(HolderGray, 3)) g.DrawLine(p, r.Left + 151, r.Top + 18, r.Left + 167, r.Top + 18);
                TextRenderer.DrawText(g, "Đầu kẹp", smallFont,
                    new Rectangle(r.Left + 171, r.Top + 9, 80, 17), Color.FromArgb(75, 88, 105));
            }
            if (scene.ViolationProven || scene.CollisionProven)
            {
                using (var p = new Pen(ContactRed, 4)) g.DrawLine(p, r.Right - 110, r.Top + 18, r.Right - 94, r.Top + 18);
                TextRenderer.DrawText(g, "Vùng vi phạm", smallFont,
                    new Rectangle(r.Right - 90, r.Top + 9, 87, 18), ContactRed);
            }
        }
        private static string Trim(string text, int limit)
        {
            if (string.IsNullOrWhiteSpace(text)) return "—";
            return text.Length <= limit ? text : text.Substring(0, limit - 1) + "…";
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { headerFont.Dispose(); smallFont.Dispose(); statusFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
