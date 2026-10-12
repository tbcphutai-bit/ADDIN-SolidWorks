// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Safety net for MARK contours that are only partly on LASER_MARK.
    ///
    /// A rolled/curved sheet flattens the edge of a MARK slot into a spline.
    /// When SOLIDWORKS cannot mirror that one edge into the marker sketch, the
    /// DXF shows a rectangle with three LASER_MARK sides and the fourth side on
    /// CUT. This pass recognises that topology in the DXF itself: an open CUT
    /// chain whose two ends are exactly the two free ends of one open
    /// LASER_MARK chain closes that mark, so it is moved to LASER_MARK.
    ///
    /// Guards against touching real cutting geometry:
    ///  - the CUT chain must be a simple open path with no other connection
    ///    (outer contour and holes are closed loops and never qualify);
    ///  - its ends must be the free ends of the SAME LASER_MARK chain;
    ///  - the CUT chain must be shorter than that LASER_MARK chain, so an
    ///    outline broken by a short mark can never be pulled onto LASER_MARK.
    /// </summary>
    internal static partial class DxfLayerFinalizer
    {
        private const double ContourJoinTolerance = 0.01; // mm

        private sealed class ChainEntity
        {
            public DxfRecord Record;
            public int StartNode;
            public int EndNode;
            public double Length;
            public bool IsLaser;
            public bool IsCut;
        }

        private static int CloseLaserMarkContours(
            List<string> lines,
            DxfManufacturingLayerSettings settings)
        {
            List<DxfRecord> records = CollectEntityRecords(lines);
            var nodes = new ContourNodeIndex(ContourJoinTolerance);
            var entities = new List<ChainEntity>();

            foreach (DxfRecord record in records)
            {
                if (!IsOverlapGeometryType(record.Type)) continue;

                string layer = GetGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "8") ?? "0";
                string canonical = MapCanonicalLayer(layer, settings);

                List<List<DxfPoint2D>> polylines;
                if (!TryTessellateEntity(
                        lines,
                        record,
                        ContourJoinTolerance,
                        double.PositiveInfinity,
                        out polylines) ||
                    polylines.Count != 1 ||
                    polylines[0].Count < 2)
                    continue;

                List<DxfPoint2D> points = polylines[0];
                DxfPoint2D start = points[0];
                DxfPoint2D end = points[points.Count - 1];
                // Closed entities (circles, closed polylines) have no free end.
                if (Distance(start, end) <= ContourJoinTolerance) continue;

                double length = 0;
                for (int index = 1; index < points.Count; index++)
                    length += Distance(points[index - 1], points[index]);

                entities.Add(new ChainEntity
                {
                    Record = record,
                    StartNode = nodes.GetNode(start),
                    EndNode = nodes.GetNode(end),
                    Length = length,
                    IsLaser = string.Equals(
                        canonical,
                        settings.LaserMark.Name,
                        StringComparison.OrdinalIgnoreCase),
                    IsCut = string.Equals(
                        canonical,
                        settings.Cut.Name,
                        StringComparison.OrdinalIgnoreCase)
                });
            }

            List<List<ChainEntity>> laserChains =
                BuildChains(entities.Where(entity => entity.IsLaser).ToList());
            List<List<ChainEntity>> cutChains =
                BuildChains(entities.Where(entity => entity.IsCut).ToList());
            if (laserChains.Count == 0 || cutChains.Count == 0) return 0;

            // Every entity end that is neither CUT nor LASER_MARK (BEND,
            // layer 0, ...) counts as a foreign connection.
            var foreignNodes = new HashSet<int>();
            foreach (ChainEntity entity in entities.Where(item => !item.IsLaser && !item.IsCut))
            {
                foreignNodes.Add(entity.StartNode);
                foreignNodes.Add(entity.EndNode);
            }

            var laserChainByFreeEnd = new Dictionary<int, List<ChainEntity>>();
            var laserNodeDegree = NodeDegrees(entities.Where(item => item.IsLaser));
            foreach (List<ChainEntity> chain in laserChains)
            {
                List<int> freeEnds;
                if (!TryGetOpenPathEnds(chain, out freeEnds)) continue;
                foreach (int node in freeEnds) laserChainByFreeEnd[node] = chain;
            }

            int moved = 0;
            foreach (List<ChainEntity> cutChain in cutChains)
            {
                List<int> ends;
                if (!TryGetOpenPathEnds(cutChain, out ends)) continue;

                List<ChainEntity> firstLaser;
                List<ChainEntity> secondLaser;
                if (!laserChainByFreeEnd.TryGetValue(ends[0], out firstLaser) ||
                    !laserChainByFreeEnd.TryGetValue(ends[1], out secondLaser))
                    continue;

                // Usually the CUT chain closes ONE open LASER_MARK chain (three
                // sides of a slot on LASER_MARK, the curved fourth on CUT). It
                // can also bridge two separate LASER_MARK chains: a marked
                // rectangle with rounded corners whose four straight sides
                // reached LASER_MARK while the four corner ARCs stayed on CUT.
                // Both ends must still be the free ends of LASER_MARK
                // geometry, so a closed cutting contour never qualifies.
                bool sameLaserChain = ReferenceEquals(firstLaser, secondLaser);

                // Interior CUT nodes must not touch LASER_MARK or other
                // geometry; the ends must touch exactly one LASER_MARK entity.
                var chainNodes = new HashSet<int>(
                    cutChain.SelectMany(item => new[] { item.StartNode, item.EndNode }));
                if (chainNodes.Any(node => foreignNodes.Contains(node))) continue;
                if (chainNodes.Any(node =>
                        !ends.Contains(node) &&
                        laserNodeDegree.ContainsKey(node)))
                    continue;
                if (ends.Any(node =>
                        !laserNodeDegree.TryGetValue(node, out int degree) ||
                        degree != 1))
                    continue;

                double cutLength = cutChain.Sum(item => item.Length);
                double laserLength = firstLaser.Sum(item => item.Length);
                if (!sameLaserChain)
                    laserLength += secondLaser.Sum(item => item.Length);
                if (cutLength >= laserLength) continue;

                foreach (ChainEntity entity in cutChain)
                {
                    SetGroupValue(
                        lines,
                        entity.Record.Start,
                        entity.Record.EndExclusive,
                        "8",
                        settings.LaserMark.Name);
                    moved++;
                }
            }

            return moved;
        }

        private static List<List<ChainEntity>> BuildChains(List<ChainEntity> entities)
        {
            var byNode = new Dictionary<int, List<ChainEntity>>();
            foreach (ChainEntity entity in entities)
            {
                AddToNode(byNode, entity.StartNode, entity);
                AddToNode(byNode, entity.EndNode, entity);
            }

            var chains = new List<List<ChainEntity>>();
            var visited = new HashSet<ChainEntity>();
            foreach (ChainEntity seed in entities)
            {
                if (!visited.Add(seed)) continue;
                var chain = new List<ChainEntity>();
                var pending = new Stack<ChainEntity>();
                pending.Push(seed);
                while (pending.Count > 0)
                {
                    ChainEntity current = pending.Pop();
                    chain.Add(current);
                    foreach (int node in new[] { current.StartNode, current.EndNode })
                    {
                        foreach (ChainEntity neighbour in byNode[node])
                        {
                            if (visited.Add(neighbour)) pending.Push(neighbour);
                        }
                    }
                }
                chains.Add(chain);
            }
            return chains;
        }

        /// <summary>
        /// True for a simple open path: every node is used at most twice and
        /// exactly two nodes are used once.
        /// </summary>
        private static bool TryGetOpenPathEnds(
            List<ChainEntity> chain,
            out List<int> ends)
        {
            ends = null;
            Dictionary<int, int> degrees = NodeDegrees(chain);
            if (degrees.Values.Any(degree => degree > 2)) return false;
            List<int> free = degrees
                .Where(pair => pair.Value == 1)
                .Select(pair => pair.Key)
                .ToList();
            if (free.Count != 2) return false;
            ends = free;
            return true;
        }

        private static Dictionary<int, int> NodeDegrees(IEnumerable<ChainEntity> entities)
        {
            var degrees = new Dictionary<int, int>();
            foreach (ChainEntity entity in entities)
            {
                degrees[entity.StartNode] =
                    degrees.TryGetValue(entity.StartNode, out int start) ? start + 1 : 1;
                degrees[entity.EndNode] =
                    degrees.TryGetValue(entity.EndNode, out int end) ? end + 1 : 1;
            }
            return degrees;
        }

        private static void AddToNode(
            IDictionary<int, List<ChainEntity>> byNode,
            int node,
            ChainEntity entity)
        {
            List<ChainEntity> list;
            if (!byNode.TryGetValue(node, out list))
            {
                list = new List<ChainEntity>();
                byNode[node] = list;
            }
            if (!list.Contains(entity)) list.Add(entity);
        }

        private static double Distance(DxfPoint2D first, DxfPoint2D second)
        {
            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        /// <summary>Merges entity end points closer than the tolerance.</summary>
        private sealed class ContourNodeIndex
        {
            private readonly double _tolerance;
            private readonly Dictionary<long, List<int>> _cells =
                new Dictionary<long, List<int>>();
            private readonly List<DxfPoint2D> _points = new List<DxfPoint2D>();

            public ContourNodeIndex(double tolerance)
            {
                _tolerance = tolerance;
            }

            public int GetNode(DxfPoint2D point)
            {
                int cellX = (int)Math.Floor(point.X / _tolerance);
                int cellY = (int)Math.Floor(point.Y / _tolerance);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        List<int> bucket;
                        if (!_cells.TryGetValue(Key(cellX + dx, cellY + dy), out bucket))
                            continue;
                        foreach (int node in bucket)
                        {
                            if (Distance(_points[node], point) <= _tolerance)
                                return node;
                        }
                    }
                }

                int created = _points.Count;
                _points.Add(point);
                long key = Key(cellX, cellY);
                List<int> cell;
                if (!_cells.TryGetValue(key, out cell))
                {
                    cell = new List<int>();
                    _cells[key] = cell;
                }
                cell.Add(created);
                return created;
            }

            private static long Key(int x, int y)
            {
                return ((long)x << 32) | (uint)y;
            }
        }
    }
}
