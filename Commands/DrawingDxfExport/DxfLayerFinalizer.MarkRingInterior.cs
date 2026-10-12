// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.DrawingDxfExporting
{
    internal static partial class DxfLayerFinalizer
    {
        private const double MarkRingInsideTolerance = 0.01;    // mm
        private const double MarkRingCentreTolerance = 0.05;    // mm

        /// <summary>
        /// Workshop rule: a hole marked on LASER_MARK keeps ONLY its outer
        /// LASER_MARK circle. Everything lying inside that circle - the
        /// concentric through bore on CUT (Ø12 inside the Ø20 countersink
        /// ring), inner MARK rims, any other line - leaves the DXF; the hole
        /// is drilled / countersunk after cutting. Applies to LASER_MARK
        /// circles that have at least one CONCENTRIC circle or arc inside
        /// (a hole mark), never to a lone mark circle. Returns the number
        /// of entities removed.
        /// </summary>
        internal static int RemoveGeometryInsideMarkRings(
            List<string> lines,
            DxfManufacturingLayerSettings settings,
            List<KeyValuePair<double, double>> substitutions = null)
        {
            if (lines == null || settings == null) return 0;
            List<DxfRecord> records = CollectEntityRecords(lines);

            var rings = new List<(DxfRecord Record, double X, double Y, double R)>();
            foreach (DxfRecord record in records)
            {
                if (!IsDxfType(record, "CIRCLE")) continue;
                string layer = GetGroupValue(lines, record.Start, record.EndExclusive, "8") ?? "0";
                if (!string.Equals(MapCanonicalLayer(layer, settings), settings.LaserMark.Name,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, "10", out double x) ||
                    !TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, "20", out double y) ||
                    !TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, "40", out double r) ||
                    !(r > MarkRingInsideTolerance))
                    continue;
                // Extrusion -Z mirrors X of the OCS centre.
                if (TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, "230", out double nz) && nz < 0)
                    x = -x;
                rings.Add((record, x, y, r));
            }
            if (rings.Count == 0) return 0;

            // Geometry of every other drawable entity, tessellated once.
            var shapes = new Dictionary<DxfRecord, List<List<DxfPoint2D>>>();
            foreach (DxfRecord record in records)
            {
                string type = (record.Type ?? "").Trim().ToUpperInvariant();
                if (type == "LINE")
                {
                    if (TryGetLineSegment(lines, record, out DxfLineSegment2D segment))
                        shapes[record] = new List<List<DxfPoint2D>> { new List<DxfPoint2D> { segment.Start, segment.End } };
                    continue;
                }
                if (type != "ARC" && type != "CIRCLE" && type != "ELLIPSE" &&
                    type != "SPLINE" && type != "LWPOLYLINE")
                    continue;
                if (TryTessellateEntity(lines, record, LaserChordTolerance, 1.0, out List<List<DxfPoint2D>> polylines) &&
                    polylines.Count > 0)
                    shapes[record] = polylines;
            }

            var doomed = new HashSet<DxfRecord>();
            int ringsUsed = 0;
            foreach (var ring in rings.OrderByDescending(item => item.R))
            {
                if (doomed.Contains(ring.Record)) continue;   // inside a bigger mark ring already
                double limit = ring.R - MarkRingInsideTolerance;
                var inside = new List<DxfRecord>();
                bool concentric = false;
                foreach (KeyValuePair<DxfRecord, List<List<DxfPoint2D>>> shape in shapes)
                {
                    if (ReferenceEquals(shape.Key, ring.Record) || doomed.Contains(shape.Key)) continue;
                    bool allInside = shape.Value.All(polyline => polyline.All(point =>
                        Math.Sqrt((point.X - ring.X) * (point.X - ring.X) +
                                  (point.Y - ring.Y) * (point.Y - ring.Y)) < limit));
                    if (!allInside) continue;
                    inside.Add(shape.Key);
                    if (!concentric && IsConcentricRound(lines, shape.Key, ring.X, ring.Y))
                        concentric = true;
                }
                if (!concentric || inside.Count == 0) continue;
                ringsUsed++;
                // The through bore it replaces: the smallest concentric CUT circle.
                double bore = inside
                    .Where(record => IsDxfType(record, "CIRCLE") &&
                                     IsConcentricRound(lines, record, ring.X, ring.Y) &&
                                     string.Equals(MapCanonicalLayer(GetGroupValue(lines, record.Start, record.EndExclusive, "8") ?? "0", settings),
                                         settings.Cut.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(record => TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, "40", out double r) ? r : 0)
                    .Where(r => r > 0)
                    .DefaultIfEmpty(0)
                    .Min();
                if (bore > 0 && substitutions != null)
                    substitutions.Add(new KeyValuePair<double, double>(ring.R, bore));
                foreach (DxfRecord record in inside) doomed.Add(record);
            }
            if (doomed.Count == 0) return 0;

            foreach (DxfRecord record in doomed.OrderByDescending(item => item.Start))
                lines.RemoveRange(record.Start, record.EndExclusive - record.Start);
            System.Diagnostics.Debug.WriteLine(
                "DXF finalizer: " + doomed.Count + " entities removed inside " + ringsUsed + " LASER_MARK hole rings.");
            return doomed.Count;
        }

        private static bool IsConcentricRound(List<string> lines, DxfRecord record, double x, double y)
        {
            if (!IsDxfType(record, "CIRCLE") && !IsDxfType(record, "ARC")) return false;
            if (!TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, "10", out double cx) ||
                !TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, "20", out double cy))
                return false;
            if (TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, "230", out double nz) && nz < 0)
                cx = -cx;
            return Math.Sqrt((cx - x) * (cx - x) + (cy - y) * (cy - y)) <= MarkRingCentreTolerance;
        }
    }
}
