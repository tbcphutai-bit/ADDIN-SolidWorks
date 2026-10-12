using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace ADDIN.Commands.DxfChecking
{
    internal static class DxfContourCheck
    {
        public static void Analyze(DxfFileResult file, HashSet<DxfEdge> duplicates, CancellationToken token)
        {
            List<DxfContourResult> contours = new List<DxfContourResult>();
            foreach (IGrouping<string, DxfEdge> space in file.Edges.Where(e => !duplicates.Contains(e)).GroupBy(e => e.Space, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                List<DxfContourResult> paths = Build(space.ToList(), token);
                contours.AddRange(paths.Where(p => p.Closed));
                contours.AddRange(MergeOpen(paths.Where(p => !p.Closed).ToList(), token));
            }
            foreach (DxfContourResult contour in contours.Where(c => !c.Closed)) foreach (DxfContourGap gap in contour.Gaps)
            {
                token.ThrowIfCancellationRequested();
                double nearest;
                if (!DxfGeometry.TryJunctionDistance(gap, out nearest)) continue;
                gap.NearestLength = nearest;
                gap.Untrimmed = nearest <= DxfGeometry.ClosureTolerance + 1e-10;
                gap.Touching = nearest <= DxfGeometry.Epsilon;
            }
            // Virtual chords locate unfinished outlines for hierarchy only.
            // Closed requires endpoints connected within the user's 0.001 mm threshold.
            foreach (DxfContourResult contour in contours)
            {
                token.ThrowIfCancellationRequested();
                List<DxfContourResult> parents = contours.Where(p => p != contour && p.EnclosesArea && Same(p.Space, contour.Space)
                    && p.Bounds.Size > contour.Bounds.Size + DxfGeometry.Epsilon
                    && p.Bounds.MinX <= contour.Bounds.MinX + DxfGeometry.Epsilon && p.Bounds.MaxX >= contour.Bounds.MaxX - DxfGeometry.Epsilon
                    && p.Bounds.MinY <= contour.Bounds.MinY + DxfGeometry.Epsilon && p.Bounds.MaxY >= contour.Bounds.MaxY - DxfGeometry.Epsilon
                    && Probes(contour.Points).All(point => Contains(p, point))).ToList();
                contour.Depth = parents.Count;
                contour.Parent = parents.OrderBy(p => p.Bounds.Width * p.Bounds.Height).FirstOrDefault();
                contour.Role = parents.Count > 0 ? "Inner" : contour.EnclosesArea ? "Outer" : "Unknown";
                if (parents.Any(p => !p.Closed)) contour.Note = Note(contour.Note, "Phân loại theo đường bao ngoài dự kiến (chưa nối kín)");
                if (contour.Role == "Unknown" && !contour.Closed) contour.Note = Note(contour.Note, "Chưa đủ đường bao để phân loại ngoài / trong");
            }
            file.Contours = contours.OrderBy(c => c.Role == "Unknown" ? int.MaxValue : c.Depth).ThenBy(c => c.Bounds.MinX).ThenBy(c => c.Bounds.MinY).ToList();
            for (int i = 0; i < file.Contours.Count; i++) file.Contours[i].Id = i + 1;
            foreach (DxfContourResult contour in file.Contours.Where(c => !c.Closed))
            {
                token.ThrowIfCancellationRequested();
                string code = contour.Role == "Outer" ? "OPEN_CONTOUR_OUTER" : contour.Role == "Inner" ? "OPEN_CONTOUR_INNER" : "OPEN_CONTOUR_UNKNOWN";
                foreach (DxfContourGap gap in contour.Gaps)
                {
                    string gapCode = gap.Untrimmed ? "CONTOUR_JOIN_UNTRIMMED" : code;
                    double measurement = gap.Untrimmed ? gap.Length : gap.Opening;
                    string description = "C" + contour.Id + " — " + contour.RoleName + " chưa kín; khoảng cách hai đầu ≈ " + gap.Length.ToString("0.000", CultureInfo.InvariantCulture) + " mm";
                    if (gap.NearestLength.HasValue && !gap.Untrimmed) description += "; khe gần nhất ≈ " + gap.Opening.ToString("0.000", CultureInfo.InvariantCulture) + " mm";
                    if (gap.Untrimmed) description = "C" + contour.Id + " — " + contour.RoleName + ": "
                        + (gap.Touching ? "hai nét chạm/cắt nhau" : "khe gần nhất không quá 0.001 mm")
                        + ", nhưng hai đầu lệch ≈ " + gap.Length.ToString("0.000", CultureInfo.InvariantCulture)
                        + " mm; cần cắt nét thừa và nối đúng đầu";
                    else if (measurement > DxfGeometry.ClosureTolerance) description += " (vượt ngưỡng nối 0.001 mm)";
                    if (gap.Length <= DxfGeometry.Epsilon) description = "C" + contour.Id + " — Biên suy biến không tạo thành vòng kín";
                    if (gap.Length <= 4)
                        Add(file, contour, gapCode, description, new List<DxfEdge> { gap.First, gap.Last }, measurement, DxfBox.From(new[] { gap.A, gap.B }));
                    else
                    {
                        Add(file, contour, gapCode, description + " (đầu 1/2)", new List<DxfEdge> { gap.First }, measurement, DxfBox.From(new[] { gap.A }));
                        Add(file, contour, gapCode, description + " (đầu 2/2)", new List<DxfEdge> { gap.Last }, measurement, DxfBox.From(new[] { gap.B }));
                    }
                }
            }
        }

        private static void Add(DxfFileResult file, DxfContourResult contour, string code, string text, List<DxfEdge> edges, double gap, DxfBox box)
        {
            DxfCheckEngine.Add(file, code, text, edges.Distinct().ToList(), gap, box);
            file.Issues[file.Issues.Count - 1].ContourId = contour.Id;
        }
        private static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        private static string Note(string a, string b) { return string.IsNullOrEmpty(a) ? b : a + "; " + b; }
        private static IEnumerable<DxfPoint> Probes(List<DxfPoint> points)
        { for (int i = 0; i < points.Count; i += Math.Max(1, points.Count / 64)) yield return points[i]; if (points.Count > 0) yield return points[points.Count - 1]; }

        private static bool Contains(DxfContourResult contour, DxfPoint point)
        {
            if (contour.Closed && contour.Edges.All(e => e.Type == "CIRCLE" || e.Type == "ARC")
                && contour.Edges.All(e => e.Center.Distance(contour.Edges[0].Center) <= DxfGeometry.Epsilon && Math.Abs(e.Radius - contour.Edges[0].Radius) <= DxfGeometry.Epsilon))
                return point.Distance(contour.Edges[0].Center) <= contour.Edges[0].Radius + DxfGeometry.Epsilon;
            bool inside = false;
            for (int i = 0, j = contour.Points.Count - 1; i < contour.Points.Count; j = i++)
            {
                DxfPoint a = contour.Points[i], b = contour.Points[j];
                if (DxfGeometry.DistanceSegment(point, a, b) <= DxfGeometry.Epsilon) return true;
                if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private sealed class Directed
        {
            public DxfEdge Edge;
            public bool Reverse;
        }
        private static DxfContourResult Create(List<Directed> path, bool closed)
        {
            DxfContourResult result = new DxfContourResult { Closed = closed, Space = path[0].Edge.Space, First = path[0].Edge, Last = path[path.Count - 1].Edge };
            foreach (Directed part in path)
            {
                result.Edges.Add(part.Edge);
                List<DxfPoint> points = new List<DxfPoint>(part.Edge.Preview);
                if (part.Reverse) points.Reverse();
                result.Points.AddRange(points);
            }
            if (result.Edges.Sum(e => e.Length) <= DxfGeometry.Epsilon) { result.Closed = false; result.Note = "Nét suy biến"; }
            Finish(result);
            if (result.Closed && !result.EnclosesArea) { result.Closed = false; result.Note = "Nét suy biến không tạo thành vùng kín"; }
            if (!result.Closed) result.Gaps.Add(new DxfContourGap { A = result.Points[0], B = result.Points[result.Points.Count - 1], First = result.First, Last = result.Last });
            return result;
        }
        private static void Finish(DxfContourResult result)
        {
            result.Bounds = DxfBox.From(result.Points);
            DxfPoint origin = result.Points[0]; double area = 0;
            for (int i = 0; i < result.Points.Count; i++) area += (result.Points[i] - origin).Cross(result.Points[(i + 1) % result.Points.Count] - origin);
            result.EnclosesArea = Math.Abs(area) > DxfGeometry.Epsilon * DxfGeometry.Epsilon;
        }

        private static List<DxfContourResult> Build(List<DxfEdge> edges, CancellationToken token)
        {
            List<DxfContourResult> results = new List<DxfContourResult>();
            HashSet<DxfEdge> consumed = new HashSet<DxfEdge>();
            foreach (IGrouping<string, DxfEdge> entity in edges.GroupBy(e => e.Handle))
            {
                List<DxfEdge> parts = entity.OrderBy(e => e.Segment).ToList();
                if (!parts.All(e => e.EntityClosed) || parts.Where((e, i) => !DxfGeometry.Connected(e.End, parts[(i + 1) % parts.Count].Start)).Any()) continue;
                results.Add(Create(parts.Select(e => new Directed { Edge = e }).ToList(), true));
                foreach (DxfEdge e in parts) consumed.Add(e);
                token.ThrowIfCancellationRequested();
            }
            List<DxfEdge> remaining = edges.Where(e => !consumed.Contains(e)).ToList();
            Graph graph = new Graph(remaining, token);
            bool[] used = new bool[remaining.Count];
            int[] cursors = new int[graph.Nodes.Count];
            Func<int, int> nextEdge = node => {
                List<int> adjacent = graph.Nodes[node].Edges;
                while (cursors[node] < adjacent.Count && used[adjacent[cursors[node]]]) cursors[node]++;
                return cursors[node] < adjacent.Count ? adjacent[cursors[node]] : -1;
            };
            Action<int> walk = start => {
                List<Directed> path = new List<Directed>();
                List<int> pathNodes = new List<int> { start };
                Dictionary<int, int> positions = new Dictionary<int, int> { { start, 0 } };
                int current = start;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int index = nextEdge(current);
                    if (index < 0) { if (path.Count > 0) results.Add(Create(path, false)); break; }
                    used[index] = true;
                    bool reverse = graph.Starts[index] != current;
                    path.Add(new Directed { Edge = remaining[index], Reverse = reverse });
                    current = reverse ? graph.Starts[index] : graph.Ends[index];
                    int previous;
                    if (positions.TryGetValue(current, out previous))
                    {
                        results.Add(Create(path.Skip(previous).ToList(), true));
                        path.RemoveRange(previous, path.Count - previous);
                        for (int i = previous + 1; i < pathNodes.Count; i++) positions.Remove(pathNodes[i]);
                        pathNodes.RemoveRange(previous + 1, pathNodes.Count - previous - 1);
                    }
                    else { positions[current] = path.Count; pathNodes.Add(current); }
                }
            };
            // Starting at odd-degree vertices preserves open spurs while separating real cycles.
            foreach (int node in Enumerable.Range(0, graph.Nodes.Count).Where(n => graph.Nodes[n].Edges.Count % 2 == 1).OrderBy(n => graph.Nodes[n].Edges.Count))
                if (nextEdge(node) >= 0) walk(node);
            for (int i = 0; i < remaining.Count; i++) if (!used[i]) walk(graph.Starts[i]);
            return results;
        }

        private static List<DxfContourResult> MergeOpen(List<DxfContourResult> paths, CancellationToken token)
        {
            if (paths.Count < 2) return paths;
            List<DxfPoint> points = paths.SelectMany(p => new[] { p.Points[0], p.Points[p.Points.Count - 1] }).ToList();
            int[] nearest = new int[points.Count], mates = Enumerable.Repeat(-1, points.Count).ToArray();
            Dictionary<Tuple<double, double>, List<int>> buckets = new Dictionary<Tuple<double, double>, List<int>>();
            for (int i = 0; i < points.Count; i++)
            {
                Tuple<double, double> key = Tuple.Create(Math.Floor(points[i].X), Math.Floor(points[i].Y)); List<int> bucket;
                if (!buckets.TryGetValue(key, out bucket)) { bucket = new List<int>(); buckets.Add(key, bucket); }
                bucket.Add(i);
            }
            for (int i = 0; i < points.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                nearest[i] = i ^ 1; double distance = points[i].Distance(points[i ^ 1]);
                double x = Math.Floor(points[i].X), y = Math.Floor(points[i].Y);
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                {
                    List<int> candidates; if (!buckets.TryGetValue(Tuple.Create(x + dx, y + dy), out candidates)) continue;
                    foreach (int j in candidates)
                    {
                        if (j == i) continue;
                        double gap = points[i].Distance(points[j]);
                        if (gap < distance) { distance = gap; nearest[i] = j; }
                    }
                }
            }
            for (int i = 0; i < nearest.Length; i++)
            {
                int j = nearest[i];
                // This threshold only groups broken fragments for hierarchy. Every bridge is NG.
                double maxBridge = Math.Min(1, Math.Max(.001, Math.Min(paths[i / 2].Bounds.Size, paths[j / 2].Bounds.Size) * .02));
                if (i / 2 != j / 2 && nearest[j] == i && points[i].Distance(points[j]) <= maxBridge) { mates[i] = j; mates[j] = i; }
            }
            List<DxfContourResult> results = new List<DxfContourResult>(); HashSet<int> visited = new HashSet<int>();
            for (int seed = 0; seed < paths.Count; seed++)
            {
                if (visited.Contains(seed)) continue;
                HashSet<int> component = new HashSet<int>(); Queue<int> queue = new Queue<int>(); queue.Enqueue(seed); component.Add(seed);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    foreach (int endpoint in new[] { current * 2, current * 2 + 1 }) if (mates[endpoint] >= 0 && component.Add(mates[endpoint] / 2)) queue.Enqueue(mates[endpoint] / 2);
                }
                if (component.Count == 1) { visited.Add(seed); results.Add(paths[seed]); continue; }
                List<int> loose = component.SelectMany(i => new[] { i * 2, i * 2 + 1 }).Where(i => mates[i] < 0).ToList();
                int firstEndpoint = loose.Count > 0 ? loose[0] : seed * 2, entering = firstEndpoint;
                DxfContourResult merged = new DxfContourResult { Space = paths[seed].Space, Closed = false };
                while (true)
                {
                    int index = entering / 2; bool reverse = entering % 2 == 1; DxfContourResult path = paths[index];
                    visited.Add(index);
                    List<DxfPoint> samples = new List<DxfPoint>(path.Points); if (reverse) samples.Reverse();
                    merged.Points.AddRange(samples); merged.Edges.AddRange(path.Edges);
                    DxfEdge entryEdge = reverse ? path.Last : path.First, exitEdge = reverse ? path.First : path.Last;
                    if (merged.First == null) merged.First = entryEdge; merged.Last = exitEdge;
                    int exit = index * 2 + (reverse ? 0 : 1), mate = mates[exit];
                    if (mate < 0)
                    {
                        merged.Gaps.Add(new DxfContourGap { A = points[exit], B = points[firstEndpoint], First = exitEdge, Last = merged.First });
                        break;
                    }
                    DxfContourResult next = paths[mate / 2];
                    merged.Gaps.Add(new DxfContourGap { A = points[exit], B = points[mate], First = exitEdge, Last = mate % 2 == 0 ? next.First : next.Last });
                    if (mate == firstEndpoint) break;
                    entering = mate;
                }
                merged.Note = merged.Gaps.Count + " chỗ chưa nối đúng đầu; đường bao dự kiến để phân loại ngoài / trong";
                Finish(merged); results.Add(merged);
            }
            return results;
        }

        private sealed class Node { public DxfPoint Point; public List<int> Edges = new List<int>(); }
        private sealed class Graph
        {
            public List<Node> Nodes = new List<Node>();
            public List<int> Starts = new List<int>(), Ends = new List<int>();
            private readonly Dictionary<Tuple<double, double>, List<int>> buckets = new Dictionary<Tuple<double, double>, List<int>>();
            private const double Grid = DxfGeometry.ClosureTolerance + 1e-10;
            public Graph(List<DxfEdge> edges, CancellationToken token)
            {
                for (int i = 0; i < edges.Count; i++)
                {
                    if (i % 256 == 0) token.ThrowIfCancellationRequested();
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
