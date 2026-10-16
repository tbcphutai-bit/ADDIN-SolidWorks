using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ADDIN.Commands.DxfChecking
{
    internal static class GeometryLoopBuilder
    {
        // Strict topology remains available; comparison-only repairs are explicit CHECK evidence.
        private const double JoinTolerance = .001;
        private sealed class Node { public DxfPoint Point; public List<int> Edges = new List<int>(); }

        public static DrawingGeometry2D Build(IEnumerable<DxfEdge> input)
        {
            var result = new DrawingGeometry2D { Edges = input.ToList() };
            var loops = new List<GeometryLoop2D>();
            var nodes = new List<Node>();
            var edges = new List<DxfEdge>();
            var starts = new List<int>(); var ends = new List<int>();
            foreach (var edge in result.Edges)
            {
                if (edge.Closed) { AddLoop(new List<DxfEdge> { edge }, DxfGeometry.Sample(edge, .002), loops, result); continue; }
                if (edge.Length <= 1e-8) { result.Unclosed.Add(edge); continue; }
                int a = FindNode(nodes, edge.Start), b = FindNode(nodes, edge.End), index = edges.Count;
                if (a == b) { AddLoop(new List<DxfEdge> { edge }, DxfGeometry.Sample(edge, .002), loops, result); continue; }
                edges.Add(edge); starts.Add(a); ends.Add(b); nodes[a].Edges.Add(index); nodes[b].Edges.Add(index);
            }
            var seen = new HashSet<int>();
            for (int first = 0; first < edges.Count; first++)
            {
                if (seen.Contains(first)) continue;
                var component = new HashSet<int>(); var queue = new Queue<int>(); queue.Enqueue(first);
                while (queue.Count > 0)
                {
                    int e = queue.Dequeue(); if (!component.Add(e)) continue;
                    foreach (int next in nodes[starts[e]].Edges.Concat(nodes[ends[e]].Edges)) if (!component.Contains(next)) queue.Enqueue(next);
                }
                foreach (int e in component) seen.Add(e);
                if (component.Any(e => nodes[starts[e]].Edges.Count != 2 || nodes[ends[e]].Edges.Count != 2))
                { result.Unclosed.AddRange(component.Select(e => edges[e])); continue; }
                var ordered = new List<DxfEdge>(); var points = new List<DxfPoint>();
                int current = first, node = starts[first], startNode = node; var walked = new HashSet<int>();
                while (walked.Add(current))
                {
                    var edge = edges[current]; bool reverse = starts[current] != node;
                    var samples = DxfGeometry.Sample(edge, .002); if (reverse) samples.Reverse();
                    if (points.Count > 0) samples.RemoveAt(0);
                    points.AddRange(samples); ordered.Add(edge);
                    node = reverse ? starts[current] : ends[current];
                    current = nodes[node].Edges.First(e => e != current);
                }
                if (node != startNode || walked.Count != component.Count) result.Unclosed.AddRange(component.Select(e => edges[e]));
                else AddLoop(ordered, points, loops, result);
            }
            if (loops.Count == 0) return result;
            result.Outer = loops.OrderByDescending(l => l.Area).First();
            var containment = new PolygonContainmentIndex(result.Outer.Points, JoinTolerance);
            foreach (var loop in loops.Where(l => l != result.Outer))
            {
                if (loop.Points.All(containment.Contains)) result.Inside.Add(loop);
                else result.OtherOuterCount++;
            }
            result.Outer.Id = 0;
            for (int i = 0; i < result.Inside.Count; i++) result.Inside[i].Id = i + 1;
            return result;
        }

        private static int FindNode(List<Node> nodes, DxfPoint point)
        {
            for (int i = 0; i < nodes.Count; i++) if (nodes[i].Point.Distance(point) <= JoinTolerance) return i;
            nodes.Add(new Node { Point = point }); return nodes.Count - 1;
        }

        private static void AddLoop(List<DxfEdge> entities, List<DxfPoint> points, List<GeometryLoop2D> loops, DrawingGeometry2D result)
        {
            var loop = MakeLoop(entities, points);
            if (loop == null) result.Unclosed.AddRange(entities);
            else loops.Add(loop);
        }

        private static GeometryLoop2D MakeLoop(List<DxfEdge> entities, List<DxfPoint> points)
        {
            double signed = 0, cx = 0, cy = 0; DxfPoint origin = points[0];
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i] - origin; var b = points[(i + 1) % points.Count] - origin; double cross = a.Cross(b);
                signed += cross; cx += (a.X + b.X) * cross; cy += (a.Y + b.Y) * cross;
            }
            if (Math.Abs(signed) < 1e-10) return null;
            var loop = new GeometryLoop2D { Entities = entities, Points = points, Bounds = DxfBox.From(points),
                Area = Math.Abs(signed) / 2, Centroid = origin + new DxfPoint(cx / (3 * signed), cy / (3 * signed)), Perimeter = entities.Sum(e => e.Length) };
            // A valid simple contour's area centroid stays inside its bounding box.
            // Self-overlapping slivers can produce near-zero signed area and a distant
            // centroid; flag that contour instead of placing a misleading error marker.
            if (loop.Centroid.X < loop.Bounds.MinX - JoinTolerance || loop.Centroid.X > loop.Bounds.MaxX + JoinTolerance ||
                loop.Centroid.Y < loop.Bounds.MinY - JoinTolerance || loop.Centroid.Y > loop.Bounds.MaxY + JoinTolerance) return null;
            if (loop.IsCircle)
            { loop.Area = Math.PI * entities[0].Radius * entities[0].Radius; loop.Centroid = entities[0].Center; loop.Bounds = entities[0].Bounds; }
            return loop;
        }

        private static bool Contains(GeometryLoop2D loop, DxfPoint p)
        {
            bool inside = false;
            for (int i = 0, j = loop.Points.Count - 1; i < loop.Points.Count; j = i++)
            {
                var a = loop.Points[j]; var b = loop.Points[i];
                if (DxfGeometry.DistanceSegment(p, a, b) <= JoinTolerance) return true;
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }
    }

    // Work only on a separate list. Equations, endpoints and files remain unchanged.
    // A numerical join cannot exceed either 0.01 mm or the requested comparison tolerance.
    internal static class ComparisonTopologyRepair
    {
        private const double StrictJoin = .001, MaximumJoin = .01;
        private const string JoinTag = "__CHECK_TOPOLOGY_JOIN__";
        internal static bool IsJoin(DxfEdge edge) { return edge.OriginalType == JoinTag; }
        private sealed class End
        {
            public int Edge;
            public DxfPoint Point;
        }
        private sealed class Node
        {
            public List<End> Ends = new List<End>();
        }
        private sealed class Graph
        {
            public List<Node> Nodes = new List<Node>();
            public int[] Starts, Ends;
        }
        private sealed class Chain
        {
            public int Start, End;
            public List<int> Edges = new List<int>();
        }

        internal static DrawingGeometry2D Build(IEnumerable<DxfEdge> input, double comparisonTolerance)
        {
            var source = input.ToList();
            var original = GeometryLoopBuilder.Build(source);
            if (original.Unclosed.Count == 0 || original.Unclosed.All(e => e.Bounds.Size <= StrictJoin)) return original;
            double limit = Math.Min(MaximumJoin, comparisonTolerance);
            var reviews = new List<GeometryReviewPoint>();
            // A valid closed component needs no repair. In particular, preserve
            // small legitimate slots while repairing an unrelated outer gap.
            var unresolved = new HashSet<DxfEdge>(original.Unclosed);
            var preserved = source.Where(e => !unresolved.Contains(e)).ToList();
            var edges = source.Where(e => unresolved.Contains(e) && (e.Closed || e.Bounds.Size > StrictJoin)).ToList();
            // Isolated tiny cuts remain visible to the existing cut-quality check.
            foreach (var tiny in source.Where(e => unresolved.Contains(e) && !e.Closed && e.Bounds.Size <= StrictJoin))
            {
                bool attached = edges.Any(e => e.Start.Distance(tiny.Start) <= limit || e.End.Distance(tiny.Start) <= limit);
                if (!attached) edges.Add(tiny);
                else reviews.Add(Review("Tiny projection", "Nét rất ngắn/chiếu thành điểm tại biên; bỏ khỏi chuỗi so sánh, giữ nguyên file.", tiny.Start, tiny.Length));
            }
            // Valid contours may serve as overlap references, but their edges are
            // never removed or joined while repairing a separate open component.
            var matchEdges = preserved.Concat(edges).ToList();
            var graph = MakeGraph(matchEdges, limit);
            var chains = Chains(graph);
            var removed = new HashSet<int>();
            foreach (var group in chains.Where(c => c.Start != c.End).GroupBy(c => Math.Min(c.Start, c.End) + ":" + Math.Max(c.Start, c.End)))
            {
                var kept = new List<Chain>();
                foreach (var chain in group.OrderBy(c => c.Edges.All(i => unresolved.Contains(matchEdges[i])) ? 1 : 0))
                {
                    if (chain.Edges.Any(i => !unresolved.Contains(matchEdges[i]))) { kept.Add(chain); continue; }
                    DxfPoint position = new DxfPoint(); double deviation = 0;
                    var duplicate = kept.FirstOrDefault(other => NearChains(chain, other, matchEdges, limit, out position, out deviation));
                    if (duplicate == null) kept.Add(chain);
                    else
                    {
                        foreach (int index in chain.Edges) removed.Add(index);
                        reviews.Add(Review("Near-overlap", "Nét gần trùng trong giới hạn " + Mm(limit) + " mm; dùng một chuỗi để so biên dạng. Cần kiểm tra nét cắt tại đây.", position, deviation));
                    }
                }
            }
            if (removed.Count > 0) edges = matchEdges.Where((edge, index) => unresolved.Contains(edge) && !removed.Contains(index)).ToList();
            graph = MakeGraph(edges, limit);
            var prepared = preserved.Concat(edges).ToList();
            foreach (var node in graph.Nodes.Where(n => n.Ends.Count == 2))
            {
                var a = node.Ends[0].Point; var b = node.Ends[1].Point;
                double gap = a.Distance(b);
                if (gap <= StrictJoin || gap > limit) continue;
                // An explicit connector keeps curve equations intact. Never snap only
                // a spline's metadata while its evaluator still returns the old point.
                var connector = DxfGeometry.Line(a, b); connector.OriginalType = JoinTag;
                prepared.Add(connector);
                reviews.Add(Review("Joined endpoints", "Đã ghép khe " + Mm(gap) + " mm trong dữ liệu so sánh; kiểm tra hai đầu nối tại đây. File gốc giữ nguyên.", (a + b) * .5, gap));
            }
            if (reviews.Count == 0) return original;
            var repaired = GeometryLoopBuilder.Build(prepared);
            double previousOpen = original.Unclosed.Sum(e => e.Length), remainingOpen = repaired.Unclosed.Sum(e => e.Length);
            if (remainingOpen >= previousOpen - 1e-8 && !(original.Outer == null && repaired.Outer != null)) return original;
            repaired.Reviews = reviews;
            return repaired;
        }

        private static GeometryReviewPoint Review(string type, string description, DxfPoint position, double deviation)
        { return new GeometryReviewPoint { Type = type, Description = description, Position = position, Deviation = deviation }; }
        private static string Mm(double value)
        { return value.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture); }

        private static Graph MakeGraph(List<DxfEdge> edges, double tolerance)
        {
            var graph = new Graph { Starts = Enumerable.Repeat(-1, edges.Count).ToArray(), Ends = Enumerable.Repeat(-1, edges.Count).ToArray() };
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].Closed || edges[i].Bounds.Size <= StrictJoin) continue;
                graph.Starts[i] = AddEnd(graph, new End { Edge = i, Point = edges[i].Start }, tolerance);
                graph.Ends[i] = AddEnd(graph, new End { Edge = i, Point = edges[i].End }, tolerance);
            }
            return graph;
        }
        private static int AddEnd(Graph graph, End end, double tolerance)
        {
            // Complete-link clusters prevent a series of close points from bridging
            // a total gap larger than the fixed repair limit.
            int node = graph.Nodes.FindIndex(n => n.Ends.All(other => other.Point.Distance(end.Point) <= tolerance));
            if (node < 0) { node = graph.Nodes.Count; graph.Nodes.Add(new Node()); }
            graph.Nodes[node].Ends.Add(end); return node;
        }
        private static List<Chain> Chains(Graph graph)
        {
            var result = new List<Chain>(); var visited = new HashSet<int>();
            for (int start = 0; start < graph.Nodes.Count; start++)
            {
                if (graph.Nodes[start].Ends.Count == 2) continue;
                foreach (var endpoint in graph.Nodes[start].Ends)
                {
                    if (visited.Contains(endpoint.Edge)) continue;
                    var chain = new Chain { Start = start }; int node = start, edge = endpoint.Edge;
                    while (visited.Add(edge))
                    {
                        chain.Edges.Add(edge);
                        node = graph.Starts[edge] == node ? graph.Ends[edge] : graph.Starts[edge];
                        if (graph.Nodes[node].Ends.Count != 2) break;
                        int next = graph.Nodes[node].Ends[0].Edge;
                        if (next == edge) next = graph.Nodes[node].Ends[1].Edge;
                        edge = next;
                    }
                    chain.End = node; result.Add(chain);
                }
            }
            return result;
        }
        private static bool NearChains(Chain first, Chain second, List<DxfEdge> edges, double tolerance, out DxfPoint position, out double deviation)
        {
            position = new DxfPoint(); deviation = 0;
            if (Math.Abs(first.Edges.Sum(i => edges[i].Length) - second.Edges.Sum(i => edges[i].Length)) > tolerance * 2) return false;
            double chord = Math.Min(.0002, tolerance / 10);
            var a = Segments(first, edges, chord); var b = Segments(second, edges, chord);
            if (a.Count == 0 || b.Count == 0) return false;
            var indexA = new SegmentSpatialIndex(a); var indexB = new SegmentSpatialIndex(b);
            DxfPoint worstA, worstB;
            double ab = Directed(a, indexB, tolerance, out worstA), ba = Directed(b, indexA, tolerance, out worstB);
            deviation = Math.Max(ab, ba); position = ab >= ba ? worstA : worstB;
            // Include the chord approximation budget rather than accepting a curve
            // whose unsampled bulge might exceed the repair tolerance.
            return deviation + chord * 2 <= tolerance + 1e-9;
        }
        private static List<DxfPoint[]> Segments(Chain chain, List<DxfEdge> edges, double chord)
        {
            var result = new List<DxfPoint[]>();
            foreach (int i in chain.Edges)
            {
                var points = DxfGeometry.Sample(edges[i], chord);
                for (int j = 1; j < points.Count; j++) result.Add(new[] { points[j - 1], points[j] });
            }
            return result;
        }
        private static double Directed(List<DxfPoint[]> segments, SegmentSpatialIndex target, double tolerance, out DxfPoint worst)
        {
            double maximum = 0; worst = segments[0][0]; int total = 0;
            foreach (var segment in segments)
            {
                int steps = Math.Max(1, (int)Math.Ceiling(segment[0].Distance(segment[1]) / tolerance));
                total += steps; if (total > 500000) return double.PositiveInfinity;
                for (int i = 0; i <= steps; i++)
                {
                    var point = segment[0] + (segment[1] - segment[0]) * ((double)i / steps);
                    double distance = point.Distance(target.Nearest(point));
                    if (distance > maximum) { maximum = distance; worst = point; }
                }
            }
            return maximum;
        }
    }

    // Only edges crossing the point's horizontal strip can affect ray parity or
    // the boundary-distance test. Keep the same sampled polygon and join tolerance.
    internal sealed class PolygonContainmentIndex
    {
        private readonly List<DxfPoint[]>[] strips;
        private readonly DxfBox bounds;
        private readonly double tolerance, stripHeight;
        internal PolygonContainmentIndex(List<DxfPoint> points, double joinTolerance)
        {
            bounds = DxfBox.From(points); tolerance = joinTolerance;
            int count = Math.Max(1, Math.Min(128, (int)Math.Ceiling(Math.Sqrt(points.Count))));
            stripHeight = Math.Max(bounds.Height, joinTolerance) / count;
            strips = Enumerable.Range(0, count).Select(i => new List<DxfPoint[]>()).ToArray();
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                var a = points[j]; var b = points[i]; var edge = new[] { a, b };
                int first = Strip(Math.Min(a.Y, b.Y) - tolerance), last = Strip(Math.Max(a.Y, b.Y) + tolerance);
                for (int k = first; k <= last; k++) strips[k].Add(edge);
            }
        }
        private int Strip(double y)
        { return Math.Max(0, Math.Min(strips.Length - 1, (int)Math.Floor((y - bounds.MinY) / stripHeight))); }
        internal bool Contains(DxfPoint point)
        {
            if (point.X < bounds.MinX - tolerance || point.X > bounds.MaxX + tolerance ||
                point.Y < bounds.MinY - tolerance || point.Y > bounds.MaxY + tolerance) return false;
            bool inside = false;
            foreach (var edge in strips[Strip(point.Y)])
            {
                var a = edge[0]; var b = edge[1];
                if (DxfGeometry.DistanceSegment(point, a, b) <= tolerance) return true;
                if ((a.Y > point.Y) != (b.Y > point.Y) &&
                    point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }
    }
}
