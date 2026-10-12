using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ADDIN.Commands.DxfChecking
{
    internal static class DxfLayerStatistics
    {
        // Count drawn contours. Exact duplicates still represent two DXF objects;
        // the existing overlap check reports that manufacturing problem separately.
        public static void Analyze(DxfFileResult file, CancellationToken token)
        {
            file.YellowShapes.Clear();
            foreach (DxfLayerInfo layer in file.YellowLayers)
            {
                token.ThrowIfCancellationRequested();
                AnalyzeLayer(file, layer.Name, true, layer.Note,
                    file.Edges.Where(e => Same(e.Layer, layer.Name)).ToList(),
                    file.YellowItems.Where(e => Same(e.Layer, layer.Name)).ToList(), token);
            }
            List<DxfEdge> extraEdges = file.Edges.Where(e => e.IsYellow && !e.LayerIsYellow).ToList();
            List<DxfNonCutItem> extraItems = file.YellowItems.Where(e => e.IsYellow && !e.LayerIsYellow).ToList();
            foreach (string layer in extraEdges.Select(e => e.Layer).Concat(extraItems.Select(e => e.Layer)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                AnalyzeLayer(file, layer, false, "Màu của đối tượng / block", extraEdges.Where(e => Same(e.Layer, layer)).ToList(), extraItems.Where(e => Same(e.Layer, layer)).ToList(), token);
            }
            file.YellowShapes = file.YellowShapes.OrderBy(s => !s.YellowLayer).ThenBy(s => s.Layer, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Space, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.ShapeName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        private static void AnalyzeLayer(DxfFileResult file, string layer, bool byLayer, string note, List<DxfEdge> edges, List<DxfNonCutItem> items, CancellationToken token)
        {
            if (edges.Count == 0 && items.Count == 0) { Add(file, layer, "", byLayer, "Empty", 0, 0, note); return; }
            foreach (string space in edges.Select(e => e.Space).Concat(items.Select(e => e.Space)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                List<DxfEdge> group = edges.Where(e => Same(e.Space, space)).ToList();
                HashSet<DxfEdge> consumed = new HashSet<DxfEdge>();
                // A closed entity owns its contour even if another shape touches it.
                foreach (IGrouping<string, DxfEdge> entity in group.GroupBy(e => e.Handle))
                {
                    List<DxfEdge> parts = entity.OrderBy(e => e.Segment).ToList();
                    if (!parts.All(e => e.EntityClosed)) continue;
                    if (parts.Count > 1 && parts.Where((e, i) => !DxfGeometry.Connected(e.End, parts[(i + 1) % parts.Count].Start)).Any()) continue;
                    List<Directed> contour = parts.Select(e => new Directed { Edge = e }).ToList();
                    Add(file, layer, space, byLayer, Classify(contour), 1, 1, note, parts);
                    foreach (DxfEdge e in parts) consumed.Add(e);
                    token.ThrowIfCancellationRequested();
                }
                List<DxfEdge> remaining = group.Where(e => !consumed.Contains(e)).ToList();
                Graph graph = new Graph(remaining);
                HashSet<int> visited = new HashSet<int>();
                for (int seed = 0; seed < remaining.Count; seed++)
                {
                    if (visited.Contains(seed)) continue;
                    token.ThrowIfCancellationRequested();
                    HashSet<int> component = new HashSet<int>(), nodes = new HashSet<int>();
                    Queue<int> queue = new Queue<int>(); queue.Enqueue(seed); visited.Add(seed);
                    while (queue.Count > 0)
                    {
                        int index = queue.Dequeue(); component.Add(index);
                        foreach (int node in new[] { graph.Starts[index], graph.Ends[index] })
                        {
                            nodes.Add(node);
                            foreach (int adjacent in graph.Nodes[node].Edges) if (visited.Add(adjacent)) queue.Enqueue(adjacent);
                        }
                    }
                    int entityCount = component.Select(i => remaining[i].Handle).Distinct().Count();
                    if (nodes.Any(n => graph.Nodes[n].Edges.Count > 2))
                        Add(file, layer, space, byLayer, "Ambiguous", 1, entityCount, JoinNote(note, "Có nhánh; chưa xác định số biên dạng kín"), component.Select(i => remaining[i]));
                    else if (nodes.Any(n => graph.Nodes[n].Edges.Count != 2))
                        Add(file, layer, space, byLayer, "Open", 1, entityCount, JoinNote(note, "Chưa khép kín"), component.Select(i => remaining[i]));
                    else
                    {
                        List<Directed> contour = new List<Directed>();
                        int first = graph.Starts[seed], current = first, index = seed;
                        HashSet<int> walked = new HashSet<int>();
                        while (walked.Add(index))
                        {
                            bool reverse = graph.Starts[index] != current;
                            contour.Add(new Directed { Edge = remaining[index], Reverse = reverse });
                            current = reverse ? graph.Starts[index] : graph.Ends[index];
                            if (current == first) break;
                            index = graph.Nodes[current].Edges.First(e => !walked.Contains(e));
                        }
                        string shape = walked.Count == component.Count && current == first ? Classify(contour) : "Ambiguous";
                        Add(file, layer, space, byLayer, shape, 1, entityCount, shape == "Ambiguous" ? JoinNote(note, "Chưa xác định số biên dạng kín") : note, component.Select(i => remaining[i]));
                    }
                }
                foreach (IGrouping<string, DxfNonCutItem> itemGroup in items.Where(e => Same(e.Space, space)).GroupBy(e => e.Type == "POINT" ? "Point" : e.Type == "TEXT" || e.Type == "MTEXT" || e.Type == "ATTRIB" || e.Type == "ATTDEF" ? "Text" : "Other"))
                    Add(file, layer, space, byLayer, itemGroup.Key, itemGroup.Count(), itemGroup.Count(), JoinNote(note, "Không phải biên dạng cắt"));
            }
        }

        private static void Add(DxfFileResult file, string layer, string space, bool byLayer, string shape, int count, int entities, string note, IEnumerable<DxfEdge> contourEdges = null)
        {
            DxfShapeStatistic row = file.YellowShapes.FirstOrDefault(s => Same(s.Layer, layer) && Same(s.Space, space) && s.YellowLayer == byLayer && s.Shape == shape);
            if (row == null) { row = new DxfShapeStatistic { Layer = layer, Space = space, YellowLayer = byLayer, Shape = shape, Note = note ?? "" }; file.YellowShapes.Add(row); }
            row.Count += count; row.EntityCount += entities;
            if (contourEdges != null) row.Edges.AddRange(contourEdges);
        }
        private static string JoinNote(string a, string b) { return string.IsNullOrEmpty(a) ? b : a + "; " + b; }

        private sealed class Directed
        {
            public DxfEdge Edge;
            public bool Reverse;
            public DxfPoint Start { get { return Reverse ? Edge.End : Edge.Start; } }
        }
        private static string Classify(List<Directed> contour)
        {
            List<DxfEdge> edges = contour.Select(c => c.Edge).ToList();
            if (edges.Count == 1 && edges[0].Type == "ELLIPSE") return "Ellipse";
            bool straight = edges.All(e => e.Type == "LINE" || DxfCheckEngine.ProbeParameters(e).All(t => DxfGeometry.DistanceSegment(e.At(t), e.Start, e.End) <= DxfGeometry.Epsilon));
            if (straight)
            {
                List<DxfPoint> points = contour.Select(c => c.Start).ToList();
                bool changed = true;
                while (changed && points.Count >= 3)
                {
                    changed = false;
                    for (int i = 0; i < points.Count; i++)
                    {
                        DxfPoint a = points[(i + points.Count - 1) % points.Count], b = points[i], c = points[(i + 1) % points.Count];
                        if (a.Distance(b) <= DxfGeometry.Epsilon || b.Distance(c) <= DxfGeometry.Epsilon || DxfGeometry.DistanceSegment(b, a, c) <= DxfGeometry.Epsilon)
                        { points.RemoveAt(i); changed = true; break; }
                    }
                }
                if (points.Count < 3) return "Degenerate";
                if (!SimplePolygon(points)) return "ClosedOther";
                if (points.Count == 4)
                {
                    List<DxfPoint> sides = points.Select((p, i) => points[(i + 1) % 4] - p).ToList();
                    bool rightAngles = sides.Where((v, i) => Math.Abs(v.Dot(sides[(i + 1) % 4])) > DxfGeometry.Epsilon * Math.Max(v.Length, sides[(i + 1) % 4].Length)).Any() == false;
                    if (rightAngles)
                        return sides.All(v => Math.Abs(v.Length - sides[0].Length) <= DxfGeometry.Epsilon) ? "Square" : "Rectangle";
                }
                return points.Count == 3 ? "Triangle" : "Polygon";
            }
            if (edges.All(e => e.Type == "CIRCLE" || e.Type == "ARC"))
            {
                DxfEdge first = edges[0];
                if (edges.All(e => e.Center.Distance(first.Center) <= DxfGeometry.Epsilon && Math.Abs(e.Radius - first.Radius) <= DxfGeometry.Epsilon)
                    && Math.Abs(edges.Sum(e => Math.Abs(e.Sweep)) - 2 * Math.PI) * first.Radius <= DxfGeometry.Epsilon) return "Circle";
            }
            DxfPoint center; double radius, deviation;
            if (DxfGeometry.FitCircle(edges.SelectMany(e => DxfCheckEngine.ProbeParameters(e).Select(e.At)).ToList(), out center, out radius, out deviation) && deviation <= DxfGeometry.Epsilon)
                return "Circle";
            if (DxfCheckEngine.IsSlot(edges)) return "Slot";
            return "ClosedOther";
        }

        private static bool SimplePolygon(List<DxfPoint> points)
        {
            double area = 0;
            DxfPoint origin = points[0];
            for (int i = 0; i < points.Count; i++) area += (points[i] - origin).Cross(points[(i + 1) % points.Count] - origin);
            if (Math.Abs(area) <= DxfGeometry.Epsilon * DxfGeometry.Epsilon) return false;
            for (int i = 0; i < points.Count; i++) for (int j = i + 2; j < points.Count; j++)
            {
                if (i == 0 && j == points.Count - 1) continue;
                DxfPoint a = points[i], u = points[(i + 1) % points.Count] - a;
                DxfPoint b = points[j], v = points[(j + 1) % points.Count] - b;
                double cross = u.Cross(v);
                if (Math.Abs(cross) <= DxfGeometry.Epsilon * Math.Max(u.Length, v.Length)) continue;
                double t = (b - a).Cross(v) / cross, s = (b - a).Cross(u) / cross;
                if (t >= 0 && t <= 1 && s >= 0 && s <= 1) return false;
            }
            return true;
        }

        private sealed class Node
        {
            public DxfPoint Point;
            public List<int> Edges = new List<int>();
        }
        private sealed class Graph
        {
            public List<Node> Nodes = new List<Node>();
            public List<int> Starts = new List<int>(), Ends = new List<int>();
            private readonly Dictionary<Tuple<double, double>, List<int>> buckets = new Dictionary<Tuple<double, double>, List<int>>();
            private const double Grid = DxfGeometry.ClosureTolerance + 1e-10;
            public Graph(List<DxfEdge> edges)
            {
                for (int i = 0; i < edges.Count; i++)
                {
                    int a = Find(edges[i].Start), b = Find(edges[i].End);
                    Starts.Add(a); Ends.Add(b); Nodes[a].Edges.Add(i); Nodes[b].Edges.Add(i);
                }
            }
            private int Find(DxfPoint p)
            {
                double x = Math.Floor(p.X / Grid), y = Math.Floor(p.Y / Grid);
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                {
                    List<int> candidates;
                    if (buckets.TryGetValue(Tuple.Create(x + dx, y + dy), out candidates))
                        foreach (int index in candidates) if (DxfGeometry.Connected(Nodes[index].Point, p)) return index;
                }
                int result = Nodes.Count; Nodes.Add(new Node { Point = p });
                Tuple<double, double> key = Tuple.Create(x, y); List<int> bucket;
                if (!buckets.TryGetValue(key, out bucket)) { bucket = new List<int>(); buckets.Add(key, bucket); }
                bucket.Add(result); return result;
            }
        }
    }
}
