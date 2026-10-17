using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ADDIN.Commands.BendTools
{
    internal enum ToolRegionStatus { NotEvaluated, ValidSingleLoop, OpenBoundary, InvalidBoundary, Unsupported }

    // Fixed, versioned mm policy. This is a validation tolerance, never a weld,
    // offset, tessellation or permission to repair an explicitly open polyline.
    internal sealed class ToolGeometryTolerance
    {
        internal const string Version = "AnalyticRegionMmV1";
        public double DistanceMm { get; private set; }
        public double AbsoluteAreaMm2 { get; private set; }
        public double MaximumCoordinateMm => 10000000;
        public int MaximumCurves => 4096;
        public double DirectionTolerance => 1e-10;
        internal ToolGeometryTolerance(double distanceMm = 1e-5, double absoluteAreaMm2 = 1e-8)
        {
            if (!ToolPoint.Finite(distanceMm) || distanceMm < 1e-7 || distanceMm > 0.01 ||
                !ToolPoint.Finite(absoluteAreaMm2) || absoluteAreaMm2 <= 0)
                throw new ArgumentOutOfRangeException(nameof(distanceMm));
            DistanceMm = distanceMm; AbsoluteAreaMm2 = absoluteAreaMm2;
        }
        internal double AreaThreshold(double perimeter) => Math.Max(AbsoluteAreaMm2, perimeter * DistanceMm);
    }

    internal sealed class ToolRegionValidation
    {
        public ToolRegionStatus Status { get; internal set; }
        public bool IsValidRegion => Status == ToolRegionStatus.ValidSingleLoop;
        public bool SourceExplicitlyOpen { get; internal set; }
        public int LoopCount { get; internal set; }
        public double? AreaMm2 { get; internal set; }
        public double? AreaThresholdMm2 { get; internal set; }
        public double MaximumJointGapMm { get; internal set; }
        public double DistanceToleranceMm { get; internal set; }
        public string TolerancePolicy => ToolGeometryTolerance.Version;
        public IReadOnlyList<string> Issues { get; internal set; }
    }

    internal static class ToolRegionValidator
    {
        private const double Tau = 2 * Math.PI;
        private const double AngleRoundoff = 1e-12;
        private sealed class Edge
        {
            internal ToolCurve Curve;
            internal int A, B;
            internal bool FullCircle;
        }
        private sealed class Step
        {
            internal Edge Edge;
            internal bool Forward;
            internal ToolPoint Start => Forward ? Edge.Curve.Start : Edge.Curve.End;
            internal ToolPoint End => Forward ? Edge.Curve.End : Edge.Curve.Start;
        }
        private sealed class Contacts
        {
            internal bool Overlap;
            internal readonly List<ToolPoint> Points = new List<ToolPoint>();
        }
        private struct Bounds
        {
            internal double X0, X1, Y0, Y1;
            internal bool Near(Bounds b, double t) => X0 <= b.X1 + t && b.X0 <= X1 + t && Y0 <= b.Y1 + t && b.Y0 <= Y1 + t;
        }

        internal static ToolRegionValidation Validate(IEnumerable<ToolDxfEntity> source,
            ToolGeometryTolerance policy = null, CancellationToken cancellation = default(CancellationToken))
        {
            policy = policy ?? new ToolGeometryTolerance();
            var issues = new HashSet<string>(StringComparer.Ordinal);
            var result = new ToolRegionValidation { Status = ToolRegionStatus.NotEvaluated, DistanceToleranceMm = policy.DistanceMm };
            var entities = source?.ToList();
            if (entities == null || entities.Count == 0) return Finish(result, ToolRegionStatus.NotEvaluated, issues, "NoBoundary");
            result.SourceExplicitlyOpen = entities.Any(e => e != null && e.SourceClosed == false);
            if (entities.Any(e => e == null || e.Annotation || e.PaperSpace || e.UnsupportedReason != null || e.Curves == null || e.Curves.Count == 0))
                return Finish(result, ToolRegionStatus.Unsupported, issues, "UnsupportedOrIncompleteEntity");
            var curves = entities.SelectMany(e => e.Curves).ToArray();
            if (curves.Length > policy.MaximumCurves) return Finish(result, ToolRegionStatus.Unsupported, issues, "CurveBudgetExceeded");
            double t = policy.DistanceMm;
            foreach (var c in curves)
            {
                cancellation.ThrowIfCancellationRequested();
                if (c == null || (c.Kind != "LINE" && c.Kind != "ARC"))
                    return Finish(result, ToolRegionStatus.Unsupported, issues, "UnsupportedCurve");
                var points = c.Kind == "ARC" ? new[] { c.Start, c.End, c.Center } : new[] { c.Start, c.End };
                if (points.Any(p => Math.Abs(p.X) > policy.MaximumCoordinateMm || Math.Abs(p.Y) > policy.MaximumCoordinateMm) ||
                    c.Kind == "ARC" && c.RadiusMm > policy.MaximumCoordinateMm)
                    return Finish(result, ToolRegionStatus.Unsupported, issues, "NumericDomainExceeded");
                if (c.Kind == "LINE")
                {
                    if (ToolPoint.Distance(c.Start, c.End) <= t) issues.Add("DegenerateEdge");
                }
                else
                {
                    if (!ToolPoint.Finite(c.RadiusMm) || !ToolPoint.Finite(c.SweepRadians) || c.RadiusMm <= t || Math.Abs(c.SweepRadians) * c.RadiusMm <= t)
                        issues.Add("DegenerateArc");
                    if (Math.Abs(c.SweepRadians) > Tau + AngleRoundoff)
                        return Finish(result, ToolRegionStatus.Unsupported, issues, "MultiTurnArcUnsupported");
                    if (Math.Abs(ToolPoint.Distance(c.Start, c.Center) - c.RadiusMm) > t ||
                        Math.Abs(ToolPoint.Distance(c.End, c.Center) - c.RadiusMm) > t ||
                        ToolPoint.Distance(At(c, StartAngle(c) + c.SweepRadians), c.End) > t)
                        issues.Add("InconsistentArc");
                }
            }
            if (issues.Count > 0) return Finish(result, ToolRegionStatus.InvalidBoundary, issues);

            // Endpoint equivalence is observed only: the stored coordinates and
            // analytic curves are never moved. Reject non-transitive tolerance
            // chains rather than picking an order-dependent representative.
            var endpoints = curves.SelectMany(c => new[] { c.Start, c.End }).ToArray();
            int[] parent = Enumerable.Range(0, endpoints.Length).ToArray();
            int[] sorted = Enumerable.Range(0, endpoints.Length).OrderBy(i => endpoints[i].X).ToArray();
            for (int x = 0; x < sorted.Length; x++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (int y = x + 1; y < sorted.Length && endpoints[sorted[y]].X - endpoints[sorted[x]].X <= t; y++)
                    if (ToolPoint.Distance(endpoints[sorted[x]], endpoints[sorted[y]]) <= t)
                        parent[Root(parent, sorted[y])] = Root(parent, sorted[x]);
            }
            var clusters = Enumerable.Range(0, endpoints.Length).GroupBy(i => Root(parent, i)).ToArray();
            foreach (var group in clusters)
            {
                int[] members = group.ToArray();
                for (int i = 0; i < members.Length; i++) for (int j = i + 1; j < members.Length; j++)
                {
                    double gap = ToolPoint.Distance(endpoints[members[i]], endpoints[members[j]]);
                    result.MaximumJointGapMm = Math.Max(result.MaximumJointGapMm, gap);
                    if (gap > t) issues.Add("AmbiguousToleranceCluster");
                }
            }
            if (issues.Count > 0) return Finish(result, ToolRegionStatus.InvalidBoundary, issues);
            var edges = curves.Select((c, i) => new Edge { Curve = c, A = Root(parent, 2 * i), B = Root(parent, 2 * i + 1), FullCircle = Full(c) }).ToArray();
            var adjacency = new Dictionary<int, List<Edge>>();
            foreach (var e in edges.Where(e => !e.FullCircle))
            {
                if (e.A == e.B) issues.Add("CollapsedTopologyEdge");
                foreach (int node in new[] { e.A, e.B })
                {
                    List<Edge> list;
                    if (!adjacency.TryGetValue(node, out list)) adjacency.Add(node, list = new List<Edge>());
                    list.Add(e);
                }
            }
            if (adjacency.Values.Any(a => a.Count > 2)) issues.Add("NonManifoldVertex");
            bool open = adjacency.Values.Any(a => a.Count == 1);
            if (open) issues.Add("UnconnectedBoundary");

            Bounds[] bounds = curves.Select(c => GetBounds(c, t)).ToArray();
            // Pairwise analytic intersections, including adjacent pairs. Only
            // their shared endpoint(s) are allowed; a second intersection,
            // overlap or a non-adjacent tangency invalidates a simple region.
            for (int i = 0; i < edges.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (int j = i + 1; j < edges.Length; j++)
                {
                    if (!bounds[i].Near(bounds[j], t)) continue;
                    Contacts contact = Intersections(curves[i], curves[j], t);
                    if (contact.Overlap) issues.Add("OverlappingCurves");
                    if (contact.Points.Any(p => !AllowedJoint(edges[i], edges[j], p, t))) issues.Add("SelfIntersectionOrContact");
                }
            }

            bool graphClosed = !open && !issues.Contains("NonManifoldVertex") && !issues.Contains("CollapsedTopologyEdge");
            var loops = new List<List<Step>>();
            if (graphClosed)
            {
                var used = new HashSet<Edge>();
                foreach (var first in edges)
                {
                    if (used.Contains(first)) continue;
                    var loop = new List<Step>(); loops.Add(loop);
                    if (first.FullCircle) { used.Add(first); loop.Add(new Step { Edge = first, Forward = true }); continue; }
                    Edge e = first; int at = first.A, start = at;
                    do
                    {
                        if (!used.Add(e)) { issues.Add("InvalidLoopTraversal"); break; }
                        bool forward = e.A == at; loop.Add(new Step { Edge = e, Forward = forward });
                        at = forward ? e.B : e.A;
                        if (at == start) break;
                        e = adjacency[at].FirstOrDefault(candidate => !used.Contains(candidate));
                        if (e == null) { issues.Add("InvalidLoopTraversal"); break; }
                    } while (loop.Count <= edges.Length);
                }
            }
            result.LoopCount = loops.Count;
            var areas = new List<double>();
            foreach (var loop in loops)
            {
                double perimeter = loop.Sum(s => Length(s.Edge.Curve));
                double area = Math.Abs(SignedArea(loop));
                areas.Add(area);
                if (!ToolPoint.Finite(area) || area <= policy.AreaThreshold(perimeter)) issues.Add("ZeroOrUnresolvedArea");
                if (loops.Count == 1) { result.AreaMm2 = area; result.AreaThresholdMm2 = policy.AreaThreshold(perimeter); }
            }
            if (result.SourceExplicitlyOpen) issues.Add("ExplicitSourceOpen");
            if (issues.Any(s => s != "ExplicitSourceOpen" && s != "UnconnectedBoundary"))
                return Finish(result, ToolRegionStatus.InvalidBoundary, issues);
            if (open || result.SourceExplicitlyOpen) return Finish(result, ToolRegionStatus.OpenBoundary, issues);
            if (loops.Count > 1)
            {
                bool[] contained = new bool[loops.Count];
                for (int i = 0; i < loops.Count; i++) for (int j = 0; j < loops.Count; j++)
                    if (i != j && Contains(loops[j], loops[i][0].Start)) contained[i] = true;
                // Holes/islands and disconnected solids have no fill-rule or
                // material-side evidence yet. Report, never union or fill them.
                if (contained.Any(c => c)) issues.Add("NestedLoopsUnsupported");
                if (contained.Count(c => !c) > 1) issues.Add("DisjointLoopsUnsupported");
                return Finish(result, ToolRegionStatus.Unsupported, issues);
            }
            if (loops.Count != 1) return Finish(result, ToolRegionStatus.InvalidBoundary, issues, "NoSingleLoop");
            return Finish(result, ToolRegionStatus.ValidSingleLoop, issues);
        }

        private static ToolRegionValidation Finish(ToolRegionValidation r, ToolRegionStatus status, HashSet<string> issues, string issue = null)
        {
            if (issue != null) issues.Add(issue);
            r.Status = status; r.Issues = issues.OrderBy(s => s, StringComparer.Ordinal).ToList().AsReadOnly(); return r;
        }
        private static int Root(int[] parent, int n)
        { while (parent[n] != n) { parent[n] = parent[parent[n]]; n = parent[n]; } return n; }
        private static bool Full(ToolCurve c) => c.Kind == "ARC" && Math.Abs(Math.Abs(c.SweepRadians) - Tau) <= AngleRoundoff;
        private static double StartAngle(ToolCurve c) => Math.Atan2(c.Start.Y - c.Center.Y, c.Start.X - c.Center.X);
        private static double Positive(double a) { a %= Tau; return a < 0 ? a + Tau : a; }
        private static ToolPoint At(ToolCurve c, double angle) => new ToolPoint(c.Center.X + c.RadiusMm * Math.Cos(angle), c.Center.Y + c.RadiusMm * Math.Sin(angle));
        private static double Length(ToolCurve c) => c.Kind == "LINE" ? ToolPoint.Distance(c.Start, c.End) : c.RadiusMm * Math.Abs(c.SweepRadians);
        private static bool OnAngle(ToolCurve c, double angle, double t)
        {
            if (Full(c)) return true;
            double travel = Positive((angle - StartAngle(c)) * Math.Sign(c.SweepRadians));
            double angleTolerance = t / c.RadiusMm + AngleRoundoff;
            return travel <= Math.Abs(c.SweepRadians) + angleTolerance || Tau - travel <= angleTolerance;
        }
        private static bool OnArc(ToolCurve c, ToolPoint p, double t) =>
            Math.Abs(ToolPoint.Distance(c.Center, p) - c.RadiusMm) <= t && OnAngle(c, Math.Atan2(p.Y - c.Center.Y, p.X - c.Center.X), t);
        private static double Dot(double x, double y, double u, double v) => x * u + y * v;
        private static double Cross(double x, double y, double u, double v) => x * v - y * u;
        private static bool AllowedJoint(Edge a, Edge b, ToolPoint p, double t)
        {
            if (a.FullCircle || b.FullCircle) return false;
            return (a.A == b.A && NearBoth(p, a.Curve.Start, b.Curve.Start, t)) ||
                (a.A == b.B && NearBoth(p, a.Curve.Start, b.Curve.End, t)) ||
                (a.B == b.A && NearBoth(p, a.Curve.End, b.Curve.Start, t)) ||
                (a.B == b.B && NearBoth(p, a.Curve.End, b.Curve.End, t));
        }
        private static bool NearBoth(ToolPoint p, ToolPoint a, ToolPoint b, double t) => ToolPoint.Distance(p, a) <= t && ToolPoint.Distance(p, b) <= t;
        private static Bounds GetBounds(ToolCurve c, double t)
        {
            var points = new List<ToolPoint> { c.Start, c.End };
            if (c.Kind == "ARC") for (int i = 0; i < 4; i++) if (OnAngle(c, i * Math.PI / 2, t)) points.Add(At(c, i * Math.PI / 2));
            return new Bounds { X0 = points.Min(p => p.X), X1 = points.Max(p => p.X), Y0 = points.Min(p => p.Y), Y1 = points.Max(p => p.Y) };
        }

        private static Contacts Intersections(ToolCurve a, ToolCurve b, double t)
        {
            var result = new Contacts();
            if (a.Kind == "LINE" && b.Kind == "LINE") LineLine(a, b, t, result);
            else if (a.Kind == "LINE") LineArc(a, b, t, result);
            else if (b.Kind == "LINE") LineArc(b, a, t, result);
            else ArcArc(a, b, t, result);
            // Endpoint distances also detect near misses within the mm policy.
            foreach (var p in new[] { a.Start, a.End }) if (DistanceTo(b, p) <= t) result.Points.Add(p);
            foreach (var p in new[] { b.Start, b.End }) if (DistanceTo(a, p) <= t) result.Points.Add(p);
            return result;
        }
        private static void LineLine(ToolCurve a, ToolCurve b, double t, Contacts result)
        {
            double al = Length(a), bl = Length(b);
            double ax = (a.End.X - a.Start.X) / al, ay = (a.End.Y - a.Start.Y) / al;
            double bx = (b.End.X - b.Start.X) / bl, by = (b.End.Y - b.Start.Y) / bl;
            double dx = b.Start.X - a.Start.X, dy = b.Start.Y - a.Start.Y, cross = Cross(ax, ay, bx, by);
            if (Math.Abs(cross) <= AngleRoundoff)
            {
                if (Math.Abs(Cross(dx, dy, ax, ay)) > t || Math.Abs(Cross(b.End.X - a.Start.X, b.End.Y - a.Start.Y, ax, ay)) > t) return;
                double s = Dot(dx, dy, ax, ay), e = Dot(b.End.X - a.Start.X, b.End.Y - a.Start.Y, ax, ay);
                double low = Math.Max(0, Math.Min(s, e)), high = Math.Min(al, Math.Max(s, e));
                if (high - low > 1e-10) result.Overlap = true;
                else if (high >= low - t) result.Points.Add(new ToolPoint(a.Start.X + ax * low, a.Start.Y + ay * low));
                return;
            }
            double u = Cross(dx, dy, bx, by) / cross, v = Cross(dx, dy, ax, ay) / cross;
            if (u >= -t && u <= al + t && v >= -t && v <= bl + t)
                result.Points.Add(new ToolPoint(a.Start.X + ax * u, a.Start.Y + ay * u));
        }
        private static void LineArc(ToolCurve line, ToolCurve arc, double t, Contacts result)
        {
            double length = Length(line), ux = (line.End.X - line.Start.X) / length, uy = (line.End.Y - line.Start.Y) / length;
            double dx = arc.Center.X - line.Start.X, dy = arc.Center.Y - line.Start.Y;
            double along = Dot(dx, dy, ux, uy), perpendicular = Math.Abs(Cross(dx, dy, ux, uy));
            if (perpendicular > arc.RadiusMm + t) return;
            double half = Math.Sqrt(Math.Max(0, (arc.RadiusMm - perpendicular) * (arc.RadiusMm + perpendicular)));
            foreach (double s in new[] { along - half, along + half })
                if (s >= -t && s <= length + t)
                {
                    var p = new ToolPoint(line.Start.X + s * ux, line.Start.Y + s * uy);
                    if (OnArc(arc, p, t)) result.Points.Add(p);
                }
        }
        private static void ArcArc(ToolCurve a, ToolCurve b, double t, Contacts result)
        {
            double dx = b.Center.X - a.Center.X, dy = b.Center.Y - a.Center.Y, d = ToolPoint.Distance(a.Center, b.Center);
            if (d <= t && Math.Abs(a.RadiusMm - b.RadiusMm) <= t)
            {
                // Co-circular (or unresolved within tolerance). Positive angular
                // overlap is invalid regardless of source orientation.
                double sa = StartAngle(a) + Math.Min(0, a.SweepRadians), la = Math.Abs(a.SweepRadians);
                double sb = StartAngle(b) + Math.Min(0, b.SweepRadians), lb = Math.Abs(b.SweepRadians);
                sa = Positive(sa); sb = Positive(sb);
                for (int shift = -1; shift <= 1; shift++)
                {
                    double low = Math.Max(sa, sb + shift * Tau), high = Math.Min(sa + la, sb + shift * Tau + lb);
                    if ((high - low) * Math.Min(a.RadiusMm, b.RadiusMm) > 1e-10) result.Overlap = true;
                    else if (high >= low - t / Math.Min(a.RadiusMm, b.RadiusMm)) result.Points.Add(At(a, low));
                }
                return;
            }
            if (d == 0 || d > a.RadiusMm + b.RadiusMm + t || d < Math.Abs(a.RadiusMm - b.RadiusMm) - t) return;
            double along = d / 2 + (a.RadiusMm - b.RadiusMm) * (a.RadiusMm + b.RadiusMm) / (2 * d);
            double half = Math.Sqrt(Math.Max(0, (a.RadiusMm - along) * (a.RadiusMm + along)));
            double ux = dx / d, uy = dy / d;
            foreach (int sign in new[] { -1, 1 })
            {
                var p = new ToolPoint(a.Center.X + ux * along - sign * uy * half, a.Center.Y + uy * along + sign * ux * half);
                if (OnArc(a, p, t) && OnArc(b, p, t)) result.Points.Add(p);
            }
        }
        private static double DistanceTo(ToolCurve c, ToolPoint p)
        {
            if (c.Kind == "LINE")
            {
                double dx = c.End.X - c.Start.X, dy = c.End.Y - c.Start.Y;
                double f = Dot(p.X - c.Start.X, p.Y - c.Start.Y, dx, dy) / (dx * dx + dy * dy);
                f = Math.Max(0, Math.Min(1, f));
                return ToolPoint.Distance(p, new ToolPoint(c.Start.X + f * dx, c.Start.Y + f * dy));
            }
            double angle = Math.Atan2(p.Y - c.Center.Y, p.X - c.Center.X);
            return OnAngle(c, angle, 0) ? Math.Abs(ToolPoint.Distance(c.Center, p) - c.RadiusMm) :
                Math.Min(ToolPoint.Distance(p, c.Start), ToolPoint.Distance(p, c.End));
        }
        private static double SignedArea(List<Step> loop)
        {
            ToolPoint origin = loop[0].Start; double sum = 0, correction = 0;
            foreach (var step in loop)
            {
                var c = step.Edge.Curve;
                double sx = c.Start.X - origin.X, sy = c.Start.Y - origin.Y, ex = c.End.X - origin.X, ey = c.End.Y - origin.Y;
                double term = c.Kind == "LINE" ? Cross(sx, sy, ex, ey) :
                    (c.Center.X - origin.X) * (ey - sy) - (c.Center.Y - origin.Y) * (ex - sx) + c.RadiusMm * c.RadiusMm * c.SweepRadians;
                if (!step.Forward) term = -term;
                double adjusted = term - correction, next = sum + adjusted; correction = (next - sum) - adjusted; sum = next;
            }
            return sum / 2;
        }
        private static bool Contains(List<Step> loop, ToolPoint p)
        {
            int crossings = 0;
            foreach (var step in loop)
            {
                var c = step.Edge.Curve;
                if (c.Kind == "LINE")
                {
                    if ((c.Start.Y > p.Y) != (c.End.Y > p.Y) &&
                        c.Start.X + (p.Y - c.Start.Y) * (c.End.X - c.Start.X) / (c.End.Y - c.Start.Y) > p.X) crossings++;
                    continue;
                }
                double sin = (p.Y - c.Center.Y) / c.RadiusMm;
                if (sin <= -1 || sin >= 1) continue; // a ray tangent does not cross
                double angle = Math.Asin(sin);
                foreach (double a in new[] { angle, Math.PI - angle })
                {
                    if (At(c, a).X <= p.X) continue;
                    if (!Full(c))
                    {
                        // Count with CCW geometry, independent of input order.
                        // Half-open endpoint rule matches the straight-edge rule.
                        double start = StartAngle(c) + Math.Min(0, c.SweepRadians), length = Math.Abs(c.SweepRadians);
                        double travel = Positive(a - start);
                        if (Tau - travel < AngleRoundoff) travel = 0;
                        if (travel > length + AngleRoundoff) continue;
                        if (travel < AngleRoundoff && Math.Cos(a) < 0 || Math.Abs(travel - length) < AngleRoundoff && Math.Cos(a) > 0) continue;
                    }
                    crossings++;
                }
            }
            return crossings % 2 == 1;
        }
    }
}
