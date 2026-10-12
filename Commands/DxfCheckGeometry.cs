using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ADDIN.Commands.DxfChecking
{
    internal static class DxfGeometry
    {
        // Numerical precision only, not an acceptance tolerance for manufacturing.
        public const double Epsilon = 1e-7;
        // User-selected connection threshold in millimeters; circle/overlap tests use Epsilon.
        public const double ClosureTolerance = .001;
        public static bool Connected(DxfPoint a, DxfPoint b)
        { return a.Distance(b) <= ClosureTolerance + 1e-10; }
        public const double Tau = Math.PI * 2;
        public static double Mod(double angle) { angle %= Tau; return angle < 0 ? angle + Tau : angle; }

        public static DxfEdge Line(DxfPoint a, DxfPoint b)
        {
            return new DxfEdge { Type = "LINE", Start = a, End = b, Length = a.Distance(b), Bounds = DxfBox.From(new[] { a, b }), Evaluate = t => a + (b - a) * t, Derivative = t => b - a };
        }

        public static DxfEdge Circle(DxfPoint center, double radius, double start, double sweep)
        {
            if (!(radius > 0)) throw new InvalidDataException("Bán kính phải lớn hơn 0.");
            DxfEdge e = new DxfEdge { Type = Math.Abs(sweep) >= Tau - 1e-12 ? "CIRCLE" : "ARC", Center = center, Radius = radius, StartAngle = start, Sweep = sweep, Closed = Math.Abs(sweep) >= Tau - 1e-12 };
            e.Evaluate = t => center + new DxfPoint(Math.Cos(start + sweep * t), Math.Sin(start + sweep * t)) * radius;
            e.Derivative = t => new DxfPoint(-Math.Sin(start + sweep * t), Math.Cos(start + sweep * t)) * (radius * sweep);
            e.Start = e.At(0); e.End = e.At(1); e.Length = Math.Abs(sweep) * radius;
            e.Bounds = new DxfBox { MinX = center.X - radius, MaxX = center.X + radius, MinY = center.Y - radius, MaxY = center.Y + radius };
            return e;
        }

        public static List<DxfEdge> ReadEdges(DxfRecord r, DxfTransform transform)
        {
            DxfTransform ocs = DxfTransform.Identity;
            if (r.Type != "LINE" && r.Type != "SPLINE" && r.Type != "ELLIPSE" && r.Number(230, 1) < 0) ocs.A = -1;
            DxfTransform tr = ocs.Then(transform);
            if (r.Type == "LINE") return new List<DxfEdge> { Line(transform.Apply(r.Point()), transform.Apply(r.Point(11))) };
            if (r.Type == "ARC" || r.Type == "CIRCLE")
            {
                double start = r.Type == "CIRCLE" ? 0 : r.Number(50) * Math.PI / 180;
                double sweep = r.Type == "CIRCLE" ? Tau : Mod((r.Number(51) - r.Number(50)) * Math.PI / 180);
                return new List<DxfEdge> { TransformedCircle(tr, r.Point(), r.Number(40), start, sweep) };
            }
            if (r.Type == "ELLIPSE")
            {
                DxfPoint major = r.Point(11), minor = new DxfPoint(-major.Y, major.X) * (r.Number(40) * (r.Number(230, 1) < 0 ? -1 : 1));
                double start = r.Number(41), end = r.Number(42, Tau), sweep = end - start;
                if (sweep < 0) sweep += Tau;
                if (!(major.Length > 0) || !(r.Number(40) > 0)) throw new InvalidDataException("ELLIPSE thiếu trục hợp lệ.");
                return new List<DxfEdge> { Ellipse(transform.Apply(r.Point()), transform.Vector(major), transform.Vector(minor), start, sweep) };
            }
            if (r.Type == "SPLINE") return new List<DxfEdge> { ReadSpline(r, transform) };
            List<DxfPoint> points = new List<DxfPoint>(); List<double> bulges = new List<double>();
            if (r.Type == "POLYLINE")
            {
                if (((int)r.Number(70) & (8 | 16 | 64)) != 0) throw new InvalidDataException("POLYLINE 3D/mesh chưa hỗ trợ.");
                if (r.Children.Any(v => Math.Abs(v.Number(30)) > Epsilon)) throw new InvalidDataException("Vertex POLYLINE không nằm trên Z=0.");
                foreach (DxfRecord vertex in r.Children.Where(v => v.Type == "VERTEX")) { points.Add(vertex.Point()); bulges.Add(vertex.Number(42)); }
            }
            else
            {
                foreach (DxfPair p in r.Pairs)
                {
                    if (p.Code == 10) { points.Add(new DxfPoint(double.Parse(p.Value, System.Globalization.CultureInfo.InvariantCulture), 0)); bulges.Add(0); }
                    else if (points.Count > 0 && p.Code == 20) { DxfPoint v = points[points.Count - 1]; v.Y = double.Parse(p.Value, System.Globalization.CultureInfo.InvariantCulture); points[points.Count - 1] = v; }
                    else if (points.Count > 0 && p.Code == 42) bulges[points.Count - 1] = double.Parse(p.Value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            List<DxfEdge> edges = new List<DxfEdge>();
            bool closed = ((int)r.Number(70) & 1) != 0;
            for (int i = 0; i < points.Count - (closed ? 0 : 1); i++)
            {
                DxfPoint a = points[i], b = points[(i + 1) % points.Count];
                DxfEdge e;
                if (Math.Abs(bulges[i]) < 1e-15) e = Line(tr.Apply(a), tr.Apply(b));
                else
                {
                    DxfPoint chord = b - a;
                    if (chord.Length < 1e-15) throw new InvalidDataException("Cung polyline có hai đầu trùng nhau.");
                    double bulge = bulges[i], theta = 4 * Math.Atan(bulge);
                    DxfPoint c = (a + b) * 0.5 + new DxfPoint(-chord.Y, chord.X) * ((1 - bulge * bulge) / (4 * bulge));
                    e = TransformedCircle(tr, c, a.Distance(c), Math.Atan2(a.Y - c.Y, a.X - c.X), theta);
                }
                e.Segment = i + 1; edges.Add(e);
            }
            return edges;
        }

        private static DxfEdge TransformedCircle(DxfTransform tr, DxfPoint c, double r, double start, double sweep)
        {
            if (tr.IsSimilarity)
            {
                DxfPoint v = tr.Vector(new DxfPoint(Math.Cos(start), Math.Sin(start)));
                return Circle(tr.Apply(c), r * v.Length, Math.Atan2(v.Y, v.X), sweep * (tr.Determinant < 0 ? -1 : 1));
            }
            return Ellipse(tr.Apply(c), tr.Vector(new DxfPoint(r, 0)), tr.Vector(new DxfPoint(0, r)), start, sweep);
        }

        private static DxfEdge Ellipse(DxfPoint c, DxfPoint u, DxfPoint v, double start, double sweep)
        {
            DxfEdge e = new DxfEdge { Type = "ELLIPSE", Closed = Math.Abs(sweep) >= Tau - 1e-12 };
            e.Evaluate = t => c + u * Math.Cos(start + sweep * t) + v * Math.Sin(start + sweep * t);
            e.Derivative = t => (u * -Math.Sin(start + sweep * t) + v * Math.Cos(start + sweep * t)) * sweep;
            e.Start = e.At(0); e.End = e.At(1);
            double rx = Math.Sqrt(u.X * u.X + v.X * v.X), ry = Math.Sqrt(u.Y * u.Y + v.Y * v.Y);
            e.Bounds = new DxfBox { MinX = c.X - rx, MaxX = c.X + rx, MinY = c.Y - ry, MaxY = c.Y + ry };
            e.Length = Length(e); return e;
        }

        private struct Homogeneous
        {
            public double X, Y, W;
            public static Homogeneous Blend(Homogeneous a, Homogeneous b, double t)
            { return new Homogeneous { X = a.X * (1 - t) + b.X * t, Y = a.Y * (1 - t) + b.Y * t, W = a.W * (1 - t) + b.W * t }; }
        }

        private static Homogeneous DeBoor(Homogeneous[] cp, double[] knots, int degree, double u)
        {
            int n = cp.Length - 1, k = n;
            if (u < knots[n + 1])
            {
                int lo = degree, hi = n + 1;
                while (hi - lo > 1) { int m = (lo + hi) / 2; if (knots[m] <= u) lo = m; else hi = m; }
                k = lo;
            }
            Homogeneous[] d = new Homogeneous[degree + 1];
            for (int j = 0; j <= degree; j++) d[j] = cp[k - degree + j];
            for (int r = 1; r <= degree; r++) for (int j = degree; j >= r; j--)
            {
                int i = k - degree + j;
                double denominator = knots[i + degree - r + 1] - knots[i];
                double t = denominator <= 0 ? 0 : (u - knots[i]) / denominator;
                d[j] = Homogeneous.Blend(d[j - 1], d[j], t);
            }
            return d[degree];
        }

        private static DxfEdge ReadSpline(DxfRecord r, DxfTransform tr)
        {
            int degree = (int)r.Number(71);
            double[] knots = r.Numbers(40).ToArray(), xs = r.Numbers(10).ToArray(), ys = r.Numbers(20).ToArray(), weights = r.Numbers(41).ToArray();
            if (degree < 1 || degree > 25 || xs.Length != ys.Length || xs.Length <= degree || knots.Length != xs.Length + degree + 1 || (weights.Length != 0 && weights.Length != xs.Length))
                throw new InvalidDataException("SPLINE thiếu knot/control point hợp lệ (fit-point-only chưa hỗ trợ).");
            if (knots.Any(x => double.IsNaN(x) || double.IsInfinity(x)) || xs.Concat(ys).Concat(weights).Any(x => double.IsNaN(x) || double.IsInfinity(x))) throw new InvalidDataException("SPLINE có giá trị không hữu hạn.");
            for (int i = 1; i < knots.Length; i++) if (knots[i] < knots[i - 1]) throw new InvalidDataException("Knot SPLINE không tăng.");
            Homogeneous[] cp = new Homogeneous[xs.Length];
            List<DxfPoint> control = new List<DxfPoint>();
            for (int i = 0; i < cp.Length; i++)
            {
                double w = weights.Length == 0 ? 1 : weights[i];
                if (!(w > 0)) throw new InvalidDataException("Weight SPLINE phải dương.");
                DxfPoint p = tr.Apply(new DxfPoint(xs[i], ys[i])); control.Add(p); cp[i] = new Homogeneous { X = p.X * w, Y = p.Y * w, W = w };
            }
            Homogeneous[] derivative = new Homogeneous[cp.Length - 1];
            for (int i = 0; i < derivative.Length; i++)
            {
                double den = knots[i + degree + 1] - knots[i + 1], scale = den > 0 ? degree / den : 0;
                derivative[i] = new Homogeneous { X = (cp[i + 1].X - cp[i].X) * scale, Y = (cp[i + 1].Y - cp[i].Y) * scale, W = (cp[i + 1].W - cp[i].W) * scale };
            }
            double u0 = knots[degree], u1 = knots[cp.Length], span = u1 - u0;
            if (!(span > 0)) throw new InvalidDataException("Miền tham số SPLINE rỗng.");
            double[] derivativeKnots = knots.Skip(1).Take(knots.Length - 2).ToArray();
            DxfEdge e = new DxfEdge { Type = "SPLINE", Bounds = DxfBox.From(control) };
            e.Evaluate = t => { Homogeneous h = DeBoor(cp, knots, degree, u0 + Math.Max(0, Math.Min(1, t)) * span); return new DxfPoint(h.X / h.W, h.Y / h.W); };
            e.Derivative = t =>
            {
                double u = u0 + Math.Max(0, Math.Min(1, t)) * span;
                Homogeneous h = DeBoor(cp, knots, degree, u), d = DeBoor(derivative, derivativeKnots, degree - 1, u);
                return new DxfPoint((d.X * h.W - h.X * d.W) / (h.W * h.W), (d.Y * h.W - h.Y * d.W) / (h.W * h.W)) * span;
            };
            e.Breaks = knots.Where(k => k >= u0 && k <= u1).Select(k => (k - u0) / span).Distinct().ToList();
            e.Start = e.At(0); e.End = e.At(1);
            e.Closed = e.Start.Distance(e.End) <= Epsilon;
            e.Length = Length(e); return e;
        }

        private static readonly double[] Nodes = { .1834346424956498, .5255324099163290, .7966664774136267, .9602898564975363 };
        private static readonly double[] Weights = { .3626837833783620, .3137066458778873, .2223810344533745, .1012285362903763 };
        private static double Quadrature(DxfEdge edge, double a, double b)
        {
            double mid = (a + b) / 2, half = (b - a) / 2, sum = 0;
            for (int i = 0; i < 4; i++) sum += Weights[i] * (edge.Derivative(mid - half * Nodes[i]).Length + edge.Derivative(mid + half * Nodes[i]).Length);
            return sum * half;
        }
        private static double Integrate(DxfEdge edge, double a, double b, double whole, double eps, int depth)
        {
            double m = (a + b) / 2, left = Quadrature(edge, a, m), right = Quadrature(edge, m, b);
            if (depth >= 18 || Math.Abs(left + right - whole) <= eps) return left + right;
            return Integrate(edge, a, m, left, eps / 2, depth + 1) + Integrate(edge, m, b, right, eps / 2, depth + 1);
        }
        public static double Length(DxfEdge edge)
        {
            double sum = 0;
            for (int i = 1; i < edge.Breaks.Count; i++)
            {
                double a = edge.Breaks[i - 1], b = edge.Breaks[i];
                sum += Integrate(edge, a, b, Quadrature(edge, a, b), 1e-9 / Math.Max(1, edge.Breaks.Count - 1), 0);
            }
            return sum;
        }

        public static List<DxfPoint> Sample(DxfEdge edge, double distance)
        {
            List<DxfPoint> points = new List<DxfPoint> { edge.At(0) };
            for (int i = 1; i < edge.Breaks.Count; i++)
            {
                double a = edge.Breaks[i - 1], b = edge.Breaks[i];
                int divisions = edge.Type == "LINE" ? 1 : 8;
                for (int j = 0; j < divisions; j++) Flatten(edge, a + (b - a) * j / divisions, a + (b - a) * (j + 1) / divisions, distance, 0, points);
            }
            return points;
        }
        private static void Flatten(DxfEdge edge, double a, double b, double eps, int depth, List<DxfPoint> output)
        {
            DxfPoint p = edge.At(a), q = edge.At(b), m = edge.At((a + b) / 2);
            double error = DistanceSegment(m, p, q);
            error = Math.Max(error, DistanceSegment(edge.At((3 * a + b) / 4), p, q));
            error = Math.Max(error, DistanceSegment(edge.At((a + 3 * b) / 4), p, q));
            if (error <= eps || depth >= 18) { output.Add(q); return; }
            Flatten(edge, a, (a + b) / 2, eps, depth + 1, output); Flatten(edge, (a + b) / 2, b, eps, depth + 1, output);
        }
        public static double DistanceSegment(DxfPoint p, DxfPoint a, DxfPoint b)
        { DxfPoint d = b - a; double t = d.Dot(d) > 0 ? Math.Max(0, Math.Min(1, (p - a).Dot(d) / d.Dot(d))) : 0; return p.Distance(a + d * t); }

        // Inspect the actual curves near a proposed join, rather than interpreting
        // the distance between two overshooting endpoints as an empty opening.
        public static bool TryJunctionDistance(DxfContourGap gap, out double distance)
        {
            distance = gap.Length;
            if (gap.First == gap.Last || gap.Length > 1 || gap.Length <= ClosureTolerance
                || gap.First.Type != "LINE" && gap.Last.Type != "LINE") return false;
            DxfEdge line = gap.First.Type == "LINE" ? gap.First : gap.Last;
            DxfEdge curve = line == gap.First ? gap.Last : gap.First;
            DxfPoint lineTip = line == gap.First ? gap.A : gap.B, curveTip = line == gap.First ? gap.B : gap.A;
            double window = Math.Min(1, Math.Max(.01, gap.Length * 2 + ClosureTolerance * 4));
            double lineLow, lineHigh, curveLow, curveHigh;
            EndpointWindow(line, lineTip, window, out lineLow, out lineHigh);
            EndpointWindow(curve, curveTip, window, out curveLow, out curveHigh);
            DxfPoint a = line.At(lineLow), b = line.At(lineHigh);
            Func<double, double> measure = t => DistanceSegment(curve.At(t), a, b);
            double best = Math.Min(measure(curveLow), measure(curveHigh));
            List<double> cuts = curve.Breaks.Where(t => t > curveLow && t < curveHigh)
                .Concat(new[] { curveLow, curveHigh }).OrderBy(t => t).ToList();
            for (int i = 1; i < cuts.Count; i++) for (int j = 0; j < 8; j++)
            {
                double low = cuts[i - 1] + (cuts[i] - cuts[i - 1]) * j / 8;
                double high = cuts[i - 1] + (cuts[i] - cuts[i - 1]) * (j + 1) / 8;
                best = Math.Min(best, Math.Min(measure(low), measure(high)));
                // Work with analytic evaluation, including the original SPLINE knots.
                double ratio = (Math.Sqrt(5) - 1) / 2;
                double x = high - ratio * (high - low), y = low + ratio * (high - low);
                double fx = measure(x), fy = measure(y);
                for (int step = 0; step < 64; step++)
                {
                    if (fx <= fy) { high = y; y = x; fy = fx; x = high - ratio * (high - low); fx = measure(x); }
                    else { low = x; x = y; fx = fy; y = low + ratio * (high - low); fy = measure(y); }
                }
                best = Math.Min(best, Math.Min(fx, fy));
            }
            distance = best;
            return !double.IsNaN(best) && !double.IsInfinity(best);
        }

        private static void EndpointWindow(DxfEdge edge, DxfPoint tip, double radius, out double low, out double high)
        {
            bool reverse = tip.Distance(edge.End) < tip.Distance(edge.Start);
            double start = reverse ? 1 : 0, inside = start, outside = reverse ? 0 : 1;
            bool found = false;
            List<double> breaks = edge.Breaks.OrderBy(t => reverse ? -t : t).ToList();
            for (int i = 1; i < breaks.Count && !found; i++) for (int j = 1; j <= 8; j++)
            {
                double t = breaks[i - 1] + (breaks[i] - breaks[i - 1]) * j / 8;
                if (edge.At(t).Distance(tip) > radius) { outside = t; found = true; break; }
                inside = t;
            }
            if (found) for (int i = 0; i < 48; i++)
            {
                double t = (inside + outside) / 2;
                if (edge.At(t).Distance(tip) <= radius) inside = t; else outside = t;
            }
            else inside = reverse ? 0 : 1;
            low = Math.Min(start, inside); high = Math.Max(start, inside);
        }

        public static bool FitCircle(IEnumerable<DxfPoint> source, out DxfPoint center, out double radius, out double deviation)
        {
            List<DxfPoint> points = source.ToList(); center = new DxfPoint(); radius = 0; deviation = double.PositiveInfinity;
            if (points.Count < 3) return false;
            DxfPoint a = points[0], b = points.OrderByDescending(p => p.Distance(a)).First();
            DxfPoint c = points.OrderByDescending(p => Math.Abs((b - a).Cross(p - a))).First();
            DxfPoint u = b - a, v = c - a; double den = 2 * u.Cross(v);
            if (Math.Abs(den) < 1e-16) return false;
            center = a + new DxfPoint((v.Y * u.Dot(u) - u.Y * v.Dot(v)) / den, (u.X * v.Dot(v) - v.X * u.Dot(u)) / den);
            radius = center.Distance(a); DxfPoint localCenter = center; double localRadius = radius;
            deviation = points.Max(p => Math.Abs(p.Distance(localCenter) - localRadius));
            return radius > Epsilon && !double.IsInfinity(radius);
        }

        public static double DistanceCurve(DxfPoint p, DxfEdge e)
        {
            if (e.Type == "LINE") return DistanceSegment(p, e.Start, e.End);
            if (e.Type == "ARC" || e.Type == "CIRCLE")
            {
                double angle = Math.Atan2(p.Y - e.Center.Y, p.X - e.Center.X);
                double t = Mod((angle - e.StartAngle) * Math.Sign(e.Sweep)) / Math.Max(1e-15, Math.Abs(e.Sweep));
                if (e.Closed || t <= 1) return Math.Abs(p.Distance(e.Center) - e.Radius);
                return Math.Min(p.Distance(e.Start), p.Distance(e.End));
            }
            double best = double.PositiveInfinity, bestA = 0, bestB = 1;
            for (int i = 1; i < e.Breaks.Count; i++) for (int j = 0; j < 8; j++)
            {
                double a = e.Breaks[i - 1] + (e.Breaks[i] - e.Breaks[i - 1]) * j / 8, b = e.Breaks[i - 1] + (e.Breaks[i] - e.Breaks[i - 1]) * (j + 1) / 8;
                double d = DistanceSegment(p, e.At(a), e.At(b));
                if (d < best) { best = d; bestA = a; bestB = b; }
            }
            for (int i = 0; i < 45; i++)
            {
                double a = (2 * bestA + bestB) / 3, b = (bestA + 2 * bestB) / 3;
                if (p.Distance(e.At(a)) < p.Distance(e.At(b))) bestB = b; else bestA = a;
            }
            return Math.Min(Math.Min(p.Distance(e.Start), p.Distance(e.End)), p.Distance(e.At((bestA + bestB) / 2)));
        }
    }
}
