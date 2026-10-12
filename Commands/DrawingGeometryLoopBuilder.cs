using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ADDIN.Commands.DxfChecking
{
    internal static class GeometryLoopBuilder
    {
        // Topology is separate from the user's manufacturing tolerance. Never bridge an open cut.
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
