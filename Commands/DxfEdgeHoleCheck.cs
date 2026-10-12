using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace ADDIN.Commands.DxfChecking
{
    internal static partial class DxfCheckEngine
    {
        private sealed class EdgeHoleArc
        {
            public DxfEdge Edge;
            public DxfPoint Center, Start, End;
            public double Radius, Sweep;
            public List<DxfPoint> Points;
        }

        private static void CheckRoundHolesAtEdge(DxfFileResult file, CancellationToken token)
        {
            List<DxfContourResult> outlines = file.Contours.Where(c => c.Role == "Outer" && c.Closed && c.EnclosesArea).ToList();
            foreach (DxfContourResult outline in outlines)
            {
                token.ThrowIfCancellationRequested();
                if (IsSlot(outline.Edges)) continue;
                double orientation = Math.Sign(EdgeHoleArea(outline.Points));
                if (orientation == 0) continue;
                List<List<EdgeHoleArc>> runs = new List<List<EdgeHoleArc>>();
                List<EdgeHoleArc> run = null;
                int offset = 0;
                foreach (DxfEdge edge in outline.Edges)
                {
                    token.ThrowIfCancellationRequested();
                    bool reverse = outline.Points[offset].Distance(edge.End) < outline.Points[offset].Distance(edge.Start);
                    offset += edge.Preview.Count;
                    foreach (EdgeHoleArc arc in EdgeHoleSpans(edge, reverse))
                    {
                        if (arc == null) { run = null; continue; }
                        if (run == null || !EdgeHoleSameCircle(run[run.Count - 1], arc) || run[run.Count - 1].End.Distance(arc.Start) > DxfGeometry.Epsilon)
                        { run = new List<EdgeHoleArc>(); runs.Add(run); }
                        run.Add(arc);
                    }
                }
                // A pocket can cross the arbitrary first entity of a cyclic contour.
                if (runs.Count > 1 && EdgeHoleSameCircle(runs[runs.Count - 1].Last(), runs[0][0])
                    && runs[runs.Count - 1].Last().End.Distance(runs[0][0].Start) <= DxfGeometry.Epsilon)
                { runs[0].InsertRange(0, runs[runs.Count - 1]); runs.RemoveAt(runs.Count - 1); }
                foreach (List<EdgeHoleArc> arcs in runs)
                {
                    double sweep = arcs.Sum(a => a.Sweep), radius = arcs[0].Radius;
                    // A concave remainder greater than a semicircle identifies a round pocket
                    // opening through a smaller mouth. Quarter-round fillets and semicircular
                    // slot ends do not establish a broken circular hole from DXF geometry alone.
                    if (sweep * orientation >= 0 || (Math.Abs(sweep) - Math.PI) * radius <= DxfGeometry.Epsilon
                        || (DxfGeometry.Tau - Math.Abs(sweep)) * radius <= DxfGeometry.Epsilon) continue;
                    List<DxfEdge> edges = arcs.Select(a => a.Edge).Distinct().ToList();
                    Add(file, "ROUND_HOLE_TO_EDGE", "C" + outline.Id + " — Hốc tròn bị khuyết thông ra biên ngoài; cung còn lại "
                        + (Math.Abs(sweep) * 180 / Math.PI).ToString("0.######", CultureInfo.InvariantCulture) + "°; R"
                        + radius.ToString("0.#########", CultureInfo.InvariantCulture) + " mm", edges, radius, DxfBox.From(arcs.SelectMany(a => a.Points)));
                    file.Issues[file.Issues.Count - 1].ContourId = outline.Id;
                }
            }
            // Also detect a full circle still present in the DXF that touches/crosses
            // a larger outer outline; trimming is not required for the defect to be NG.
            foreach (DxfContourResult circle in file.Contours.Where(c => c.Closed))
            {
                token.ThrowIfCancellationRequested();
                if (circle.Edges.All(e => e.Type == "LINE")) continue;
                DxfPoint center; double radius, deviation;
                if (circle.Edges.Count == 1 && circle.Edges[0].Type == "CIRCLE") { center = circle.Edges[0].Center; radius = circle.Edges[0].Radius; }
                else if (!DxfGeometry.FitCircle(circle.Edges.SelectMany(e => ProbeParameters(e).Select(e.At)).ToList(), out center, out radius, out deviation)
                    || deviation > DxfGeometry.Epsilon) continue;
                DxfEdge analytic = DxfGeometry.Circle(center, radius, 0, DxfGeometry.Tau);
                foreach (DxfContourResult outline in outlines.Where(o => o != circle && o.Bounds.Size > circle.Bounds.Size + DxfGeometry.Epsilon
                    && string.Equals(o.Space, circle.Space, StringComparison.OrdinalIgnoreCase) && o.Bounds.Intersects(circle.Bounds, DxfGeometry.Epsilon)))
                {
                    List<DxfPoint> hits = new List<DxfPoint>(); List<DxfEdge> touching = new List<DxfEdge>();
                    foreach (DxfEdge edge in outline.Edges)
                    {
                        List<DxfPoint> points = Intersections(analytic, edge);
                        if (edge.Type == "LINE" && edge.Length > DxfGeometry.Epsilon)
                        {
                            DxfPoint direction = edge.End - edge.Start;
                            double t = Math.Max(0, Math.Min(1, (center - edge.Start).Dot(direction) / direction.Dot(direction)));
                            DxfPoint nearest = edge.Start + direction * t;
                            if (Math.Abs(nearest.Distance(center) - radius) <= DxfGeometry.Epsilon) points.Add(nearest);
                        }
                        if (points.Count > 0) { hits.AddRange(points); touching.Add(edge); }
                    }
                    if (hits.Count == 0) continue;
                    Add(file, "ROUND_HOLE_TO_EDGE", "C" + circle.Id + " — Lỗ tròn chạm hoặc cắt qua biên ngoài C" + outline.Id + "; R"
                        + radius.ToString("0.#########", CultureInfo.InvariantCulture) + " mm", circle.Edges.Concat(touching).Distinct().ToList(), radius, DxfBox.From(hits));
                    file.Issues[file.Issues.Count - 1].ContourId = circle.Id;
                }
            }
        }

        private static double EdgeHoleArea(List<DxfPoint> points)
        {
            DxfPoint origin = points[0]; double area = 0;
            for (int i = 0; i < points.Count; i++) area += (points[i] - origin).Cross(points[(i + 1) % points.Count] - origin);
            return area;
        }
        private static bool EdgeHoleSameCircle(EdgeHoleArc a, EdgeHoleArc b)
        { return a.Center.Distance(b.Center) <= DxfGeometry.Epsilon && Math.Abs(a.Radius - b.Radius) <= DxfGeometry.Epsilon && a.Sweep * b.Sweep > 0; }

        private static IEnumerable<EdgeHoleArc> EdgeHoleSpans(DxfEdge edge, bool reverse)
        {
            if (edge.Type == "ARC" || edge.Type == "CIRCLE")
            {
                List<DxfPoint> points = new List<DxfPoint>(edge.Preview); if (reverse) points.Reverse();
                yield return new EdgeHoleArc { Edge = edge, Center = edge.Center, Radius = edge.Radius, Sweep = reverse ? -edge.Sweep : edge.Sweep, Start = points[0], End = points.Last(), Points = points };
                yield break;
            }
            if (edge.Type != "SPLINE") { yield return null; yield break; }
            IEnumerable<int> indices = Enumerable.Range(1, edge.Breaks.Count - 1); if (reverse) indices = indices.Reverse();
            foreach (int index in indices)
            {
                double low = edge.Breaks[index - 1], high = edge.Breaks[index];
                List<DxfPoint> points = Enumerable.Range(0, 33).Select(i => edge.At(low + (high - low) * i / 32.0)).ToList();
                if (reverse) points.Reverse();
                DxfPoint center; double radius, deviation;
                if (!DxfGeometry.FitCircle(points, out center, out radius, out deviation) || deviation > DxfGeometry.Epsilon) { yield return null; continue; }
                double sweep = 0;
                for (int i = 1; i < points.Count; i++)
                {
                    double turn = Math.Atan2(points[i].Y - center.Y, points[i].X - center.X) - Math.Atan2(points[i - 1].Y - center.Y, points[i - 1].X - center.X);
                    if (turn > Math.PI) turn -= DxfGeometry.Tau; if (turn < -Math.PI) turn += DxfGeometry.Tau; sweep += turn;
                }
                if (Math.Abs(sweep) * radius <= DxfGeometry.Epsilon) { yield return null; continue; }
                yield return new EdgeHoleArc { Edge = edge, Center = center, Radius = radius, Sweep = sweep, Start = points[0], End = points.Last(), Points = points };
            }
        }
    }
}
