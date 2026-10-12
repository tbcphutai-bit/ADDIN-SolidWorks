// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Plain 2D geometry used to recognise copies of MARK sketches among other
    /// geometry (lines, circles, counter-clockwise arcs, sampled curves):
    /// distances, containment, coverage, and the single translation that lays
    /// a set of sketches exactly onto their copies. No SOLIDWORKS calls; the
    /// same code serves the source block (sheet metres) and the saved DXF
    /// (millimetres), with the tolerance of that frame.
    /// </summary>
    internal static class SectionMarkGeometry
    {
        internal enum ShapeKind
        {
            Line,
            Circle,
            Arc,
            Curve
        }

        internal sealed class Shape
        {
            public ShapeKind Kind;

            /// <summary>Line ends, arc ends (counter-clockwise order), curve ends.</summary>
            public double[] Start;
            public double[] End;

            public double[] Center;
            public double Radius;

            /// <summary>Arc: start angle in radians; the arc runs counter-clockwise over Sweep.</summary>
            public double StartAngle;
            public double Sweep;

            public List<double[]> Points;

            /// <summary>minX, minY, maxX, maxY.</summary>
            public double[] Bounds;
            public double Length;

            /// <summary>Owner of the shape (DXF entity, block segment ...).</summary>
            public object Tag;

            public double[] Mid
            {
                get
                {
                    switch (Kind)
                    {
                        case ShapeKind.Arc:
                            return At(StartAngle + Sweep / 2);
                        case ShapeKind.Circle:
                            return new[] { Center[0] - Radius, Center[1], 0.0 };
                        case ShapeKind.Line:
                            return new[] { (Start[0] + End[0]) / 2, (Start[1] + End[1]) / 2, 0.0 };
                        default:
                            return Points[Points.Count / 2];
                    }
                }
            }

            public double[] At(double angle)
            {
                return new[]
                {
                    Center[0] + Radius * Math.Cos(angle),
                    Center[1] + Radius * Math.Sin(angle),
                    0.0
                };
            }

            public Shape Translated(double x, double y)
            {
                var copy = (Shape)MemberwiseClone();
                if (Start != null) copy.Start = new[] { Start[0] + x, Start[1] + y, 0.0 };
                if (End != null) copy.End = new[] { End[0] + x, End[1] + y, 0.0 };
                if (Center != null) copy.Center = new[] { Center[0] + x, Center[1] + y, 0.0 };
                if (Points != null)
                    copy.Points = Points.Select(point => new[] { point[0] + x, point[1] + y, 0.0 }).ToList();
                if (Bounds != null)
                    copy.Bounds = new[] { Bounds[0] + x, Bounds[1] + y, Bounds[2] + x, Bounds[3] + y };
                return copy;
            }

            public override string ToString()
            {
                switch (Kind)
                {
                    case ShapeKind.Line:
                        return "LINE " + Format(Length);
                    case ShapeKind.Circle:
                        return "CIRCLE r" + Format(Radius);
                    case ShapeKind.Arc:
                        return "ARC r" + Format(Radius);
                    default:
                        return "CURVE " + Format(Length);
                }
            }
        }

        /// <summary>Result of <see cref="FindTranslation"/>.</summary>
        internal sealed class TranslationSearch
        {
            public bool Found;

            /// <summary>marks + Translation lie on their copies.</summary>
            public double[] Translation;

            /// <summary>Per mark (same order): the shapes that are its copy.</summary>
            public List<List<Shape>> Copies = new List<List<Shape>>();

            public int Candidates;
            public int BestMatched;
            public int Solutions;

            /// <summary>Every translation that lays all marks on copies, before any preference.</summary>
            public List<double[]> AllSolutions = new List<double[]>();
            public string Diagnostics = "";
        }

        private const int MaxCandidates = 20000;
        private const int MaxSamplesPerShape = 512;

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------

        internal static Shape Line(double[] start, double[] end, object tag = null)
        {
            if (!Usable(start) || !Usable(end)) return null;
            var shape = new Shape
            {
                Kind = ShapeKind.Line,
                Start = Point(start),
                End = Point(end),
                Tag = tag,
                Length = Distance(start, end)
            };
            shape.Bounds = new[]
            {
                Math.Min(start[0], end[0]), Math.Min(start[1], end[1]),
                Math.Max(start[0], end[0]), Math.Max(start[1], end[1])
            };
            return shape;
        }

        internal static Shape Circle(double[] center, double radius, object tag = null)
        {
            if (!Usable(center) || !(radius > 0) || double.IsInfinity(radius)) return null;
            return new Shape
            {
                Kind = ShapeKind.Circle,
                Center = Point(center),
                Radius = radius,
                Start = new[] { center[0] + radius, center[1], 0.0 },
                End = new[] { center[0] + radius, center[1], 0.0 },
                StartAngle = 0,
                Sweep = 2 * Math.PI,
                Tag = tag,
                Length = 2 * Math.PI * radius,
                Bounds = new[] { center[0] - radius, center[1] - radius, center[0] + radius, center[1] + radius }
            };
        }

        /// <summary>Counter-clockwise arc; a sweep of a full turn gives a circle.</summary>
        internal static Shape Arc(double[] center, double radius, double startAngle, double sweep, object tag = null)
        {
            if (!Usable(center) || !(radius > 0) || double.IsInfinity(radius) ||
                double.IsNaN(startAngle) || double.IsNaN(sweep) || !(sweep > 0))
                return null;
            if (sweep >= 2 * Math.PI - 1e-12) return Circle(center, radius, tag);

            var shape = new Shape
            {
                Kind = ShapeKind.Arc,
                Center = Point(center),
                Radius = radius,
                StartAngle = NormalizeAngle(startAngle),
                Sweep = sweep,
                Tag = tag,
                Length = radius * sweep
            };
            shape.Start = shape.At(shape.StartAngle);
            shape.End = shape.At(shape.StartAngle + sweep);

            var bounds = new[]
            {
                Math.Min(shape.Start[0], shape.End[0]), Math.Min(shape.Start[1], shape.End[1]),
                Math.Max(shape.Start[0], shape.End[0]), Math.Max(shape.Start[1], shape.End[1])
            };
            for (int quarter = 0; quarter < 4; quarter++)
            {
                double angle = quarter * Math.PI / 2;
                if (!AngleInSweep(angle, shape.StartAngle, sweep)) continue;
                double[] extreme = shape.At(angle);
                bounds[0] = Math.Min(bounds[0], extreme[0]);
                bounds[1] = Math.Min(bounds[1], extreme[1]);
                bounds[2] = Math.Max(bounds[2], extreme[0]);
                bounds[3] = Math.Max(bounds[3], extreme[1]);
            }
            shape.Bounds = bounds;
            return shape;
        }

        /// <summary>
        /// Arc from <paramref name="start"/> through <paramref name="through"/>
        /// to <paramref name="end"/> (any direction); a line when the three
        /// points are collinear.
        /// </summary>
        internal static Shape ArcThrough(double[] start, double[] end, double[] through, object tag = null)
        {
            if (!Usable(start) || !Usable(end) || !Usable(through)) return null;
            double ax = start[0], ay = start[1];
            double bx = through[0], by = through[1];
            double cx = end[0], cy = end[1];
            double chord = Math.Max(Distance(start, end), Math.Max(Distance(start, through), Distance(through, end)));
            double determinant = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            if (!(chord > 0) || Math.Abs(determinant) <= 1e-12 * chord * chord)
                return Line(start, end, tag);

            double a2 = ax * ax + ay * ay;
            double b2 = bx * bx + by * by;
            double c2 = cx * cx + cy * cy;
            var center = new[]
            {
                (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / determinant,
                (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / determinant,
                0.0
            };
            double radius = Distance(center, start);
            if (!(radius > 0) || radius > 1e6 * chord) return Line(start, end, tag);

            bool counterClockwise = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax) > 0;
            double from = Math.Atan2((counterClockwise ? ay : cy) - center[1], (counterClockwise ? ax : cx) - center[0]);
            double to = Math.Atan2((counterClockwise ? cy : ay) - center[1], (counterClockwise ? cx : ax) - center[0]);
            double sweep = NormalizeAngle(to - from);
            if (sweep <= 1e-12) sweep = 2 * Math.PI;
            return Arc(center, radius, from, sweep, tag);
        }

        internal static Shape Curve(IList<double[]> points, object tag = null)
        {
            if (points == null) return null;
            var clean = new List<double[]>();
            foreach (double[] point in points)
            {
                if (!Usable(point)) continue;
                if (clean.Count > 0 && Distance(clean[clean.Count - 1], point) <= 1e-12) continue;
                clean.Add(Point(point));
            }
            if (clean.Count < 2) return null;

            var bounds = new[] { double.MaxValue, double.MaxValue, double.MinValue, double.MinValue };
            double length = 0;
            for (int index = 0; index < clean.Count; index++)
            {
                double[] point = clean[index];
                bounds[0] = Math.Min(bounds[0], point[0]);
                bounds[1] = Math.Min(bounds[1], point[1]);
                bounds[2] = Math.Max(bounds[2], point[0]);
                bounds[3] = Math.Max(bounds[3], point[1]);
                if (index > 0) length += Distance(clean[index - 1], point);
            }
            return new Shape
            {
                Kind = ShapeKind.Curve,
                Points = clean,
                Start = clean[0],
                End = clean[clean.Count - 1],
                Bounds = bounds,
                Length = length,
                Tag = tag
            };
        }

        /// <summary>
        /// A MARK sketch (LINE: 2 points, CIRCLE: centre + a point on it,
        /// ARC: start, end, a point on it, otherwise a polyline) through
        /// <paramref name="map"/> (a similarity: translation, rotation,
        /// uniform scale, mirror).
        /// </summary>
        internal static Shape FromMark(DxfInjectMark mark, Func<double[], double[]> map, object tag = null)
        {
            if (mark?.Points == null || mark.Points.Count < 2 || map == null) return null;
            List<double[]> points = mark.Points.Select(map).ToList();
            if (points.Any(point => !Usable(point))) return null;
            switch ((mark.Kind ?? "").Trim().ToUpperInvariant())
            {
                case "LINE":
                    return points.Count == 2 ? Line(points[0], points[1], tag) : Curve(points, tag);
                case "CIRCLE":
                    return Circle(points[0], Distance(points[0], points[1]), tag);
                case "ARC":
                    return points.Count >= 3
                        ? ArcThrough(points[0], points[1], points[2], tag)
                        : Line(points[0], points[1], tag);
                default:
                    return Curve(points, tag);
            }
        }

        /// <summary>
        /// The shape through an affine map. Arcs and circles stay exact under a
        /// similarity; any other map turns them into sampled curves.
        /// </summary>
        internal static Shape Map(Shape shape, DxfOutPut.BlockFrameTransform map, double sampleSpacing)
        {
            if (shape == null || map == null) return null;
            switch (shape.Kind)
            {
                case ShapeKind.Line:
                    return Line(map.Apply(shape.Start), map.Apply(shape.End), shape.Tag);
                case ShapeKind.Curve:
                    return Curve(shape.Points.Select(map.Apply).ToList(), shape.Tag);
            }

            if (!IsConformal(map))
                return Curve(Sample(shape, sampleSpacing).Select(map.Apply).ToList(), shape.Tag);
            if (shape.Kind == ShapeKind.Circle)
            {
                double[] center = map.Apply(shape.Center);
                return Circle(center, Distance(center, map.Apply(shape.Start)), shape.Tag);
            }
            return ArcThrough(map.Apply(shape.Start), map.Apply(shape.End), map.Apply(shape.Mid), shape.Tag);
        }

        internal static bool IsConformal(DxfOutPut.BlockFrameTransform map)
        {
            double size = Math.Max(Math.Max(Math.Abs(map.A), Math.Abs(map.B)), Math.Max(Math.Abs(map.C), Math.Abs(map.D)));
            if (!(size > 0)) return false;
            double epsilon = 1e-9 * size;
            bool rotation = Math.Abs(map.A - map.D) <= epsilon && Math.Abs(map.B + map.C) <= epsilon;
            bool mirror = Math.Abs(map.A + map.D) <= epsilon && Math.Abs(map.B - map.C) <= epsilon;
            return rotation || mirror;
        }

        // ------------------------------------------------------------------
        // Measures
        // ------------------------------------------------------------------

        internal static double DistanceTo(Shape shape, double[] point)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Line:
                    return SegmentDistance(point, shape.Start, shape.End);
                case ShapeKind.Circle:
                    return Math.Abs(Distance(point, shape.Center) - shape.Radius);
                case ShapeKind.Arc:
                {
                    double angle = Math.Atan2(point[1] - shape.Center[1], point[0] - shape.Center[0]);
                    if (AngleInSweep(angle, shape.StartAngle, shape.Sweep))
                        return Math.Abs(Distance(point, shape.Center) - shape.Radius);
                    return Math.Min(Distance(point, shape.Start), Distance(point, shape.End));
                }
                default:
                {
                    double best = double.MaxValue;
                    for (int index = 1; index < shape.Points.Count; index++)
                    {
                        double distance = SegmentDistance(point, shape.Points[index - 1], shape.Points[index]);
                        if (distance < best) best = distance;
                    }
                    return best;
                }
            }
        }

        /// <summary>Points on the shape, at most <paramref name="spacing"/> apart (bounded count).</summary>
        internal static List<double[]> Sample(Shape shape, double spacing)
        {
            var points = new List<double[]>();
            double step = Math.Max(spacing, shape.Length / MaxSamplesPerShape);
            if (!(step > 0)) step = 1;
            switch (shape.Kind)
            {
                case ShapeKind.Line:
                {
                    int count = Math.Max(2, (int)Math.Ceiling(shape.Length / step));
                    for (int index = 0; index <= count; index++)
                    {
                        double t = (double)index / count;
                        points.Add(new[]
                        {
                            shape.Start[0] + (shape.End[0] - shape.Start[0]) * t,
                            shape.Start[1] + (shape.End[1] - shape.Start[1]) * t,
                            0.0
                        });
                    }
                    return points;
                }
                case ShapeKind.Circle:
                case ShapeKind.Arc:
                {
                    int count = Math.Max(shape.Kind == ShapeKind.Circle ? 16 : 4,
                        (int)Math.Ceiling(shape.Length / step));
                    int last = shape.Kind == ShapeKind.Circle ? count - 1 : count;
                    for (int index = 0; index <= last; index++)
                        points.Add(shape.At(shape.StartAngle + shape.Sweep * index / count));
                    return points;
                }
                default:
                {
                    points.Add(shape.Points[0]);
                    for (int index = 1; index < shape.Points.Count; index++)
                    {
                        double[] from = shape.Points[index - 1];
                        double[] to = shape.Points[index];
                        int count = Math.Max(1, (int)Math.Ceiling(Distance(from, to) / step));
                        for (int part = 1; part <= count; part++)
                        {
                            double t = (double)part / count;
                            points.Add(new[]
                            {
                                from[0] + (to[0] - from[0]) * t,
                                from[1] + (to[1] - from[1]) * t,
                                0.0
                            });
                        }
                    }
                    return points;
                }
            }
        }

        /// <summary>Every point of <paramref name="part"/> lies on <paramref name="mark"/>.</summary>
        internal static bool IsContained(Shape part, Shape mark, double tolerance, double spacing)
        {
            if (part == null || mark == null) return false;
            if (!BoxInside(part.Bounds, mark.Bounds, tolerance)) return false;
            foreach (double[] point in Sample(part, spacing))
            {
                if (DistanceTo(mark, point) > tolerance) return false;
            }
            return true;
        }

        /// <summary>Every point of <paramref name="mark"/> lies on one of <paramref name="parts"/>.</summary>
        internal static bool IsCovered(Shape mark, IList<Shape> parts, double tolerance, double spacing)
        {
            if (mark == null || parts == null || parts.Count == 0) return false;
            foreach (double[] point in Sample(mark, spacing))
            {
                bool near = false;
                foreach (Shape part in parts)
                {
                    if (DistanceTo(part, point) <= tolerance)
                    {
                        near = true;
                        break;
                    }
                }
                if (!near) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // The translation that lays the marks on their copies
        // ------------------------------------------------------------------

        /// <summary>
        /// Finds the translation T such that EVERY mark, moved by T, is
        /// exactly covered by shapes of <paramref name="targets"/> that lie
        /// on it (its copy). Candidate translations come from the mark with
        /// the fewest same-size counterparts; each is checked against all
        /// marks. Found only when exactly one translation works (or one lies
        /// within <paramref name="preferredRadius"/> of
        /// <paramref name="preferred"/>).
        /// </summary>
        internal static TranslationSearch FindTranslation(
            IList<Shape> marks,
            IList<Shape> targets,
            double tolerance,
            double spacing,
            double[] preferred = null,
            double preferredRadius = 0)
        {
            var result = new TranslationSearch();
            if (marks == null || marks.Count == 0 || marks.Any(mark => mark == null))
            {
                result.Diagnostics = "không có hình MARK hợp lệ";
                return result;
            }
            if (targets == null || targets.Count == 0)
            {
                result.Diagnostics = "không có hình để so";
                return result;
            }

            var grid = new ShapeGrid(targets, tolerance);

            // The anchor is the mark with the fewest same-size counterparts.
            List<double[]> candidates = null;
            Shape anchor = null;
            foreach (Shape mark in marks)
            {
                List<double[]> found = AnchorCandidates(mark, targets, tolerance);
                if (found.Count == 0) continue;
                if (candidates == null || found.Count < candidates.Count)
                {
                    candidates = found;
                    anchor = mark;
                }
            }
            if (candidates == null)
            {
                result.Diagnostics = "không hình nào cùng kích thước với sketch MARK (" +
                                     string.Join(", ", marks.Take(4).Select(mark => mark.ToString())) + ")";
                return result;
            }
            result.Candidates = candidates.Count;

            var solutions = new List<KeyValuePair<double[], List<List<Shape>>>>();
            foreach (double[] translation in candidates)
            {
                var copies = new List<List<Shape>>();
                int matched = 0;
                foreach (Shape mark in marks)
                {
                    Shape moved = mark.Translated(translation[0], translation[1]);
                    List<Shape> parts = grid.Query(moved.Bounds, tolerance)
                        .Where(part => IsContained(part, moved, tolerance, spacing))
                        .ToList();
                    if (!IsCovered(moved, parts, tolerance, spacing)) break;
                    copies.Add(parts);
                    matched++;
                }
                if (matched > result.BestMatched) result.BestMatched = matched;
                if (matched < marks.Count) continue;
                if (solutions.Any(solution => Distance(solution.Key, translation) <= tolerance)) continue;
                solutions.Add(new KeyValuePair<double[], List<List<Shape>>>(translation, copies));
            }
            result.Solutions = solutions.Count;
            result.AllSolutions = solutions.Select(solution => solution.Key).ToList();

            string summary = "neo " + anchor + ": " + candidates.Count + " vị trí thử, khớp đủ " +
                             solutions.Count + ", tốt nhất " + result.BestMatched + "/" + marks.Count;
            if (solutions.Count > 1 && preferred != null)
            {
                solutions = solutions
                    .Where(solution => Distance(solution.Key, preferred) <= preferredRadius)
                    .ToList();
                summary += ", gần vị trí dự kiến: " + solutions.Count;
            }
            if (solutions.Count != 1)
            {
                result.Diagnostics = summary + (solutions.Count > 1 ? " (nhiều vị trí, không chọn)" : "");
                return result;
            }

            result.Found = true;
            result.Translation = solutions[0].Key;
            result.Copies = solutions[0].Value;
            result.Diagnostics = summary + ", dời " + Format(result.Translation[0]) + ", " +
                                 Format(result.Translation[1]);
            return result;
        }

        private static List<double[]> AnchorCandidates(Shape mark, IList<Shape> targets, double tolerance)
        {
            var found = new CandidateSet(tolerance);
            foreach (Shape target in targets)
            {
                if (found.Count >= MaxCandidates) break;
                switch (mark.Kind)
                {
                    case ShapeKind.Line:
                    {
                        if (target.Kind != ShapeKind.Line) continue;
                        if (Math.Abs(target.Length - mark.Length) > 2 * tolerance || !(mark.Length > 0)) continue;
                        double ux = (mark.End[0] - mark.Start[0]) / mark.Length;
                        double uy = (mark.End[1] - mark.Start[1]) / mark.Length;
                        double vx = target.End[0] - target.Start[0];
                        double vy = target.End[1] - target.Start[1];
                        if (Math.Abs(ux * vy - uy * vx) > 2 * tolerance) continue;
                        double[] from = ux * vx + uy * vy >= 0 ? target.Start : target.End;
                        found.Add(from[0] - mark.Start[0], from[1] - mark.Start[1]);
                        break;
                    }
                    case ShapeKind.Circle:
                    case ShapeKind.Arc:
                    {
                        if (target.Kind != ShapeKind.Circle && target.Kind != ShapeKind.Arc) continue;
                        if (Math.Abs(target.Radius - mark.Radius) > 2 * tolerance) continue;
                        found.Add(target.Center[0] - mark.Center[0], target.Center[1] - mark.Center[1]);
                        break;
                    }
                    default:
                    {
                        if (target.Kind != ShapeKind.Curve) continue;
                        if (Math.Abs(target.Length - mark.Length) > 0.01 * mark.Length + 4 * tolerance) continue;
                        found.Add(target.Start[0] - mark.Start[0], target.Start[1] - mark.Start[1]);
                        found.Add(target.End[0] - mark.Start[0], target.End[1] - mark.Start[1]);
                        break;
                    }
                }
            }
            return found.Items;
        }

        /// <summary>
        /// Candidate translations without duplicates (closer than the
        /// tolerance), found through a hash grid instead of a scan of all.
        /// </summary>
        private sealed class CandidateSet
        {
            private readonly double _tolerance;
            private readonly double _cell;
            private readonly Dictionary<long, List<double[]>> _cells = new Dictionary<long, List<double[]>>();
            public readonly List<double[]> Items = new List<double[]>();

            public CandidateSet(double tolerance)
            {
                _tolerance = tolerance;
                _cell = Math.Max(tolerance, 1e-12);
            }

            public int Count => Items.Count;

            public void Add(double x, double y)
            {
                var candidate = new[] { x, y, 0.0 };
                if (!Usable(candidate)) return;
                long cx = (long)Math.Floor(x / _cell);
                long cy = (long)Math.Floor(y / _cell);
                for (long dx = -1; dx <= 1; dx++)
                {
                    for (long dy = -1; dy <= 1; dy++)
                    {
                        List<double[]> list;
                        if (!_cells.TryGetValue(Key(cx + dx, cy + dy), out list)) continue;
                        foreach (double[] existing in list)
                        {
                            if (Distance(existing, candidate) <= _tolerance) return;
                        }
                    }
                }
                long key = Key(cx, cy);
                List<double[]> cell;
                if (!_cells.TryGetValue(key, out cell))
                {
                    cell = new List<double[]>();
                    _cells[key] = cell;
                }
                cell.Add(candidate);
                Items.Add(candidate);
            }

            private static long Key(long x, long y)
            {
                return unchecked(x * 73856093L ^ y * 19349663L);
            }
        }

        // ------------------------------------------------------------------
        // Spatial index
        // ------------------------------------------------------------------

        private sealed class ShapeGrid
        {
            private readonly IList<Shape> _shapes;
            private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
            private readonly List<int> _large = new List<int>();
            private readonly double _size;
            private readonly double _originX;
            private readonly double _originY;

            public ShapeGrid(IList<Shape> shapes, double tolerance)
            {
                _shapes = shapes;
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (Shape shape in shapes)
                {
                    minX = Math.Min(minX, shape.Bounds[0]);
                    minY = Math.Min(minY, shape.Bounds[1]);
                    maxX = Math.Max(maxX, shape.Bounds[2]);
                    maxY = Math.Max(maxY, shape.Bounds[3]);
                }
                _originX = minX;
                _originY = minY;
                double extent = Math.Max(maxX - minX, maxY - minY);
                _size = Math.Max(Math.Max(extent / 128, 16 * tolerance), 1e-12);

                for (int index = 0; index < shapes.Count; index++)
                {
                    double[] bounds = shapes[index].Bounds;
                    int x0 = Cell(bounds[0] - _originX), x1 = Cell(bounds[2] - _originX);
                    int y0 = Cell(bounds[1] - _originY), y1 = Cell(bounds[3] - _originY);
                    if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 4096)
                    {
                        _large.Add(index);
                        continue;
                    }
                    for (int x = x0; x <= x1; x++)
                    {
                        for (int y = y0; y <= y1; y++)
                        {
                            long key = Key(x, y);
                            List<int> list;
                            if (!_cells.TryGetValue(key, out list))
                            {
                                list = new List<int>();
                                _cells[key] = list;
                            }
                            list.Add(index);
                        }
                    }
                }
            }

            public IEnumerable<Shape> Query(double[] bounds, double margin)
            {
                var seen = new HashSet<int>();
                int x0 = Cell(bounds[0] - margin - _originX), x1 = Cell(bounds[2] + margin - _originX);
                int y0 = Cell(bounds[1] - margin - _originY), y1 = Cell(bounds[3] + margin - _originY);
                if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 65536)
                {
                    for (int index = 0; index < _shapes.Count; index++)
                    {
                        if (BoxesTouch(_shapes[index].Bounds, bounds, margin)) yield return _shapes[index];
                    }
                    yield break;
                }
                for (int x = x0; x <= x1; x++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        List<int> list;
                        if (!_cells.TryGetValue(Key(x, y), out list)) continue;
                        foreach (int index in list)
                        {
                            if (seen.Add(index) && BoxesTouch(_shapes[index].Bounds, bounds, margin))
                                yield return _shapes[index];
                        }
                    }
                }
                foreach (int index in _large)
                {
                    if (seen.Add(index) && BoxesTouch(_shapes[index].Bounds, bounds, margin))
                        yield return _shapes[index];
                }
            }

            private int Cell(double value)
            {
                double cell = Math.Floor(value / _size);
                if (cell > 1e9) return 1000000000;
                if (cell < -1e9) return -1000000000;
                return (int)cell;
            }

            private static long Key(int x, int y)
            {
                return ((long)x << 32) ^ (uint)y;
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        internal static double Distance(double[] first, double[] second)
        {
            double dx = first[0] - second[0];
            double dy = first[1] - second[1];
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double SegmentDistance(double[] point, double[] start, double[] end)
        {
            double dx = end[0] - start[0];
            double dy = end[1] - start[1];
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0) return Distance(point, start);
            double t = ((point[0] - start[0]) * dx + (point[1] - start[1]) * dy) / lengthSquared;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            double x = start[0] + dx * t - point[0];
            double y = start[1] + dy * t - point[1];
            return Math.Sqrt(x * x + y * y);
        }

        private static bool AngleInSweep(double angle, double start, double sweep)
        {
            double offset = NormalizeAngle(angle - start);
            return offset <= sweep + 1e-12 || offset >= 2 * Math.PI - 1e-12;
        }

        private static double NormalizeAngle(double angle)
        {
            double twoPi = 2 * Math.PI;
            angle %= twoPi;
            if (angle < 0) angle += twoPi;
            return angle;
        }

        private static bool BoxInside(double[] inner, double[] outer, double margin)
        {
            return inner[0] >= outer[0] - margin && inner[1] >= outer[1] - margin &&
                   inner[2] <= outer[2] + margin && inner[3] <= outer[3] + margin;
        }

        private static bool BoxesTouch(double[] first, double[] second, double margin)
        {
            return first[0] <= second[2] + margin && first[2] >= second[0] - margin &&
                   first[1] <= second[3] + margin && first[3] >= second[1] - margin;
        }

        private static bool Usable(double[] point)
        {
            return point != null && point.Length >= 2 &&
                   !double.IsNaN(point[0]) && !double.IsNaN(point[1]) &&
                   !double.IsInfinity(point[0]) && !double.IsInfinity(point[1]);
        }

        private static double[] Point(double[] point)
        {
            return new[] { point[0], point[1], 0.0 };
        }

        private static string Format(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
