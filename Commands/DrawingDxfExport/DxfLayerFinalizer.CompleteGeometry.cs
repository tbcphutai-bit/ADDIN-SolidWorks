using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ADDIN.Commands.DxfChecking;

namespace ADDIN.Commands.DrawingDxfExporting
{
    internal sealed class DxfCompleteGeometryVerdict
    {
        public bool Passed, ScalePassed;
        public double ToleranceMm = .05, MaximumDeviationMm, TranslationX, TranslationY;
        public double ExpectedWidthMm, ExpectedHeightMm, ActualWidthMm, ActualHeightMm;
        public long Milliseconds;
        public readonly List<string> Issues = new List<string>();
        public string Summary { get { return (Passed ? "PASS" : "REVIEW") +
            "; scale1:1=" + ScalePassed + "; deviation=" + MaximumDeviationMm.ToString("0.000", CultureInfo.InvariantCulture) +
            " mm; " + Milliseconds + " ms" + (Issues.Count == 0 ? "" : "; " + string.Join("; ", Issues)); } }
    }

    internal static partial class DxfLayerFinalizer
    {
        // No fitted scale, rotation or reflection. Both directed curve distances are required.
        internal static DxfCompleteGeometryVerdict VerifyCompleteGeometry(string file, DxfSemanticPlan plan, DxfManufacturingLayerSettings layers)
        {
            var verdict = new DxfCompleteGeometryVerdict();
            var clock = Stopwatch.StartNew();
            try
            {
                if (!(plan.ViewScale > 0) || double.IsNaN(plan.ViewScale) || double.IsInfinity(plan.ViewScale))
                    throw new InvalidDataException("Tỉ lệ Drawing View không hợp lệ.");
                layers = DxfManufacturingLayerSettings.Normalize(layers);
                var expected = new Dictionary<string, List<DxfEdge>>(StringComparer.OrdinalIgnoreCase)
                {
                    [layers.Cut.Name] = new List<DxfEdge>(), [layers.LaserMark.Name] = new List<DxfEdge>(), [layers.Bend.Name] = new List<DxfEdge>()
                };
                double coincident = SemanticCoincidentToleranceMm * plan.ViewScale;
                var eraseItems = plan.Erase.Where(e => !plan.Marks.Any(m => SameProjectedEdge(e, m, coincident))).ToList();
                if (plan.ReferenceCurveBuffer == null || plan.ReferenceCurveBuffer.Length == 0)
                    throw new InvalidDataException("Thiếu curve tham chiếu bất biến của Drawing.");
                var sourceCurves = VisibleDrawingReference(plan, out double paperX, out double paperY);
                var marks = new ShapeSet(plan.Marks.Select(GateShapeItem));
                var userMarks = new ShapeSet(plan.UserMarks.Select(GateShapeItem));
                var erase = new ShapeSet(eraseItems);
                var model = new ShapeSet(plan.VisibleEdges);
                var bends = new ShapeSet(plan.Bends);
                foreach (DxfEdge curve in sourceCurves)
                {
                    var points = GateCurveSamples(curve).Select(p => new DxfPoint2D { X = p.X * plan.ViewScale + paperX, Y = p.Y * plan.ViewScale + paperY }).ToList();
                    bool onModel = model.Deviation(points) <= SemanticOnEdgeTolerance;
                    // A sketch overlay adds MARK without converting a coincident model CUT edge.
                    bool onMark = marks.Deviation(points) <= SemanticOnEdgeTolerance ||
                        (!onModel && userMarks.Deviation(points) <= SemanticOnEdgeTolerance);
                    bool onErase = erase.Deviation(points) <= SemanticOnEdgeTolerance;
                    if (onErase && onMark) onErase = erase.Deviation(points) < marks.Deviation(points) - coincident;
                    if (onErase) continue;
                    bool onBend = !onMark && bends.Deviation(points) <= SemanticOnEdgeTolerance && !onModel;
                    expected[onMark ? layers.LaserMark.Name : onBend ? layers.Bend.Name : layers.Cut.Name].Add(curve);
                }
                foreach (var item in plan.Marks.Concat(plan.UserMarks))
                    AddUncoveredPlanGeometry(expected[layers.LaserMark.Name], item, plan.ViewScale, paperX, paperY, SemanticOnEdgeTolerance / plan.ViewScale);
                foreach (var item in plan.Bends)
                {
                    var points = GateShapeItem(item).Points.Select(p => new DxfPoint2D { X = p[0], Y = p[1] }).ToList();
                    if (item.Kind == "LINE" && points.Count == 2)
                    {
                        var bendStart = points[0]; var bendEnd = points[1];
                        points = Enumerable.Range(0, 9).Select(i => new DxfPoint2D { X = bendStart.X + (bendEnd.X-bendStart.X)*i/8, Y = bendStart.Y + (bendEnd.Y-bendStart.Y)*i/8 }).ToList();
                    }
                    if (model.Deviation(points) <= SemanticOnEdgeTolerance) continue; // bend on a model boundary stays CUT
                    AddUncoveredPlanGeometry(expected[layers.Bend.Name], item, plan.ViewScale, paperX, paperY, .005);
                }
                var extracted = new DxfFileResult();
                DxfRawDocument.Read(file).Extract(extracted);
                var actual = extracted.Edges;
                var cutExpected = expected[layers.Cut.Name];
                var cutActual = actual.Where(e => string.Equals(e.Layer, layers.Cut.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (cutExpected.Count == 0 || cutActual.Count == 0) throw new InvalidDataException("Không có đủ nét CUT để đối chiếu.");
                // DxfEdge.Bounds may conservatively bound an entire arc's circle. Measure actual curves.
                var a = DxfBox.From(cutExpected.SelectMany(e => DxfGeometry.Sample(e, .001)));
                var b = DxfBox.From(cutActual.SelectMany(e => DxfGeometry.Sample(e, .001)));
                verdict.ExpectedWidthMm = a.Width; verdict.ExpectedHeightMm = a.Height;
                verdict.ActualWidthMm = b.Width; verdict.ActualHeightMm = b.Height;
                // Width spans two boundary locations, each allowed the pointwise tolerance.
                verdict.ScalePassed = Math.Abs(a.Width - b.Width) <= 2 * verdict.ToleranceMm && Math.Abs(a.Height - b.Height) <= 2 * verdict.ToleranceMm;
                if (!verdict.ScalePassed) verdict.Issues.Add("Kích thước CUT khác tham chiếu 1:1");
                var shift = new Translation2D { OffsetX = a.MinX - b.MinX, OffsetY = a.MinY - b.MinY };
                verdict.TranslationX = shift.OffsetX; verdict.TranslationY = shift.OffsetY;
                foreach (var layer in expected)
                {
                    var found = actual.Where(e => string.Equals(e.Layer, layer.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (layer.Value.Count == 0 && found.Count == 0) continue;
                    if (layer.Value.Count == 0 || found.Count == 0) { verdict.Issues.Add(layer.Key + ": thiếu/thừa toàn bộ nét"); continue; }
                    var sourceLoop = new GeometryLoop2D { Entities = layer.Value };
                    var exportedLoop = new GeometryLoop2D { Entities = found };
                    // GeometryMatcher's analytical circle path uses the loop centroids.
                    if (sourceLoop.IsCircle) sourceLoop.Centroid = sourceLoop.Entities[0].Center;
                    if (exportedLoop.IsCircle) exportedLoop.Centroid = exportedLoop.Entities[0].Center;
                    GeometryMatch match = GeometryMatcher.Compare(sourceLoop, exportedLoop, shift, verdict.ToleranceMm);
                    verdict.MaximumDeviationMm = Math.Max(verdict.MaximumDeviationMm, match.Deviation);
                    if (!match.Passed) verdict.Issues.Add(layer.Key + ": thiếu/thừa/sai vị trí biên dạng, lệch " + match.Deviation.ToString("0.000", CultureInfo.InvariantCulture) + " mm");
                }
                var knownLayers = new HashSet<string>(expected.Keys, StringComparer.OrdinalIgnoreCase);
                if (actual.Any(e => !knownLayers.Contains(e.Layer))) verdict.Issues.Add("Có nét hình học trên layer ngoài CUT / MARK / BEND");
                verdict.Passed = verdict.ScalePassed && verdict.Issues.Count == 0;
            }
            catch (Exception ex) { verdict.Issues.Add("Không thể xác nhận: " + ex.Message); verdict.Passed = false; }
            finally { verdict.Milliseconds = clock.ElapsedMilliseconds; }
            return verdict;
        }

        private static DxfSemanticItem GateShapeItem(DxfSemanticItem item)
        {
            if (item.Kind != "CIRCLE" || item.Points.Count < 2) return item;
            var p = item.Points[0]; var q = item.Points[1];
            double radius = Math.Sqrt((q[0]-p[0])*(q[0]-p[0])+(q[1]-p[1])*(q[1]-p[1]));
            var result = new DxfSemanticItem { Kind = "POLYLINE", Source = item.Source };
            for (int i = 0; i <= 72; i++) result.Points.Add(new[] { p[0]+radius*Math.Cos(i*Math.PI/36), p[1]+radius*Math.Sin(i*Math.PI/36) });
            return result;
        }

        private static void AddUncoveredPlanGeometry(List<DxfEdge> target, DxfSemanticItem item, double scale, double tx, double ty, double tolerance)
        {
            var local = new DxfSemanticItem { Kind = item.Kind, Source = item.Source };
            foreach (var point in item.Points) local.Points.Add(new[] { point[0]-tx, point[1]-ty });
            var candidates = new List<DxfEdge>(); AddExpectedGeometry(candidates, local, scale);
            foreach (var edge in candidates)
            {
                bool covered = target.Count > 0 && GateCurveSamples(edge).All(p => target.Min(e => DxfGeometry.DistanceCurve(p, e)) <= tolerance);
                if (!covered) target.Add(edge);
            }
        }

        private static List<DxfPoint> GateCurveSamples(DxfEdge edge)
        {
            var points = DxfGeometry.Sample(edge, .002);
            // Endpoints can sit on two neighbouring sides while the entire middle side is absent.
            if (edge.Type == "LINE")
                for (int i = 1; i < 16; i++) points.Add(edge.At(i / 16.0));
            return points;
        }

        private static void AddExpectedGeometry(List<DxfEdge> edges, DxfSemanticItem item, double scale)
        {
            if (item.Points.Count < 2) return;
            var added = new List<DxfEdge>();
            Func<double[], DxfPoint> point = p => new DxfPoint(p[0] / scale, p[1] / scale);
            if (item.Kind == "CIRCLE")
                added.Add(DxfGeometry.Circle(point(item.Points[0]), point(item.Points[0]).Distance(point(item.Points[1])), 0, DxfGeometry.Tau));
            else if (TryExactCircle(item.Points, out double cx, out double cy, out double radius))
                added.Add(DxfGeometry.Circle(new DxfPoint(cx / scale, cy / scale), radius / scale, 0, DxfGeometry.Tau));
            else
                for (int i = 1; i < item.Points.Count; i++)
                {
                    DxfPoint a = point(item.Points[i-1]), b = point(item.Points[i]);
                    if (a.Distance(b) > 1e-5) added.Add(DxfGeometry.Line(a, b));
                }
            foreach (var edge in added)
                if (!edges.Any(e => DrawingViewCurveBuffer.SameProjection(e, edge, .001))) edges.Add(edge);
        }
    }
}
