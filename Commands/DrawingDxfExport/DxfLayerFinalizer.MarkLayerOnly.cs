// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Light clean-up for the "View -> DXF" button. That export has no bend
    /// or cutting-layer logic, so unlike <see cref="NormalizeManufacturingLayers"/>
    /// it must not reclassify the geometry on layer 0. It only handles the
    /// user's MARK sketches: entities on the private marker layer are moved
    /// to LASER_MARK (exactly what Flatten -> DXF does), then everything on
    /// LASER_MARK gets one standard line type and BYLAYER colour, POLYLINE
    /// children follow the parent layer, wrong marker arcs are removed and
    /// the layer keeps the configured colour.
    /// </summary>
    internal static partial class DxfLayerFinalizer
    {
        /// <summary>
        /// Number of geometry entities on each of <paramref name="layerNames"/>
        /// in an ASCII DXF (layer name -> count; missing layers count 0).
        /// </summary>
        public static Dictionary<string, int> CountEntitiesOnLayers(
            string filePath,
            params string[] layerNames)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in layerNames)
            {
                if (!string.IsNullOrWhiteSpace(name)) counts[name] = 0;
            }
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return counts;

            List<string> lines = File.ReadAllLines(filePath, Encoding.GetEncoding(932)).ToList();
            if (lines.Count < 2) return counts;
            foreach (DxfRecord record in CollectEntityRecords(lines))
            {
                if (!GeometryTypes.Contains(record.Type)) continue;
                string layer = GetGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "8") ?? "0";
                if (counts.ContainsKey(layer)) counts[layer]++;
            }
            return counts;
        }

        /// <param name="removeCoveredCopies">
        /// Cut-Extrude MARK markers were drawn: like Flatten -> DXF keep only
        /// the LASER_MARK copy of a marked model edge (the model copy on layer
        /// 0 would otherwise be cut). Erased inner rims of slanted MARK holes
        /// always remove the copy under them.
        /// </param>
        public static Result NormalizeMarkLayerOnly(
            string filePath,
            DxfManufacturingLayerSettings layerSettings,
            string markerLayerName = null,
            bool removeCoveredCopies = false)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException(
                    "DXF layer finalization input was not found.",
                    filePath);

            byte[] header = new byte[Math.Min(22, (int)new FileInfo(filePath).Length)];
            using (var stream = File.OpenRead(filePath))
            {
                stream.Read(header, 0, header.Length);
            }
            if (Encoding.ASCII.GetString(header).StartsWith(
                    "AutoCAD Binary DXF",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    "Binary DXF cannot be finalized safely; configure SOLIDWORKS for ASCII DXF.");
            }

            DxfManufacturingLayerSettings settings =
                DxfManufacturingLayerSettings.Normalize(layerSettings);
            Encoding encoding = Encoding.GetEncoding(932);
            var lines = File.ReadAllLines(filePath, encoding).ToList();
            if (lines.Count < 2 || lines.Count % 2 != 0)
                throw new InvalidDataException(
                    "DXF does not contain complete group-code/value pairs.");

            int discarded = RemoveEntitiesOnLayer(lines, MarkDiscardLayerName);
            bool changed = discarded > 0;

            List<DxfRecord> records = CollectEntityRecords(lines);
            for (int index = records.Count - 1; index >= 0; index--)
            {
                DxfRecord record = records[index];
                if (!GeometryTypes.Contains(record.Type)) continue;

                string layer = GetGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "8");
                bool onMarkerLayer =
                    !string.IsNullOrWhiteSpace(markerLayerName) &&
                    string.Equals(
                        layer,
                        markerLayerName,
                        StringComparison.OrdinalIgnoreCase);
                bool onLaserLayer = string.Equals(
                    layer,
                    settings.LaserMark.Name,
                    StringComparison.OrdinalIgnoreCase);
                if (!onMarkerLayer && !onLaserLayer) continue;

                if (onMarkerLayer)
                {
                    SetGroupValue(
                        lines,
                        record.Start,
                        record.EndExclusive,
                        "8",
                        settings.LaserMark.Name);
                    changed = true;
                }

                // Children first: their offsets are valid until a pair is
                // removed from this record.
                if (IsDxfType(record, "POLYLINE"))
                {
                    SyncPolylineChildLayers(
                        lines,
                        records,
                        index,
                        settings.LaserMark.Name);
                }

                changed |= SetAllExistingGroupValues(
                    lines, record.Start, record.EndExclusive, "6", "BYLAYER");
                changed |= SetAllExistingGroupValues(
                    lines, record.Start, record.EndExclusive, "62", "256");
                changed |= RemoveGroupPairs(
                    lines, record.Start, record.EndExclusive, "420") > 0;
            }

            int overlapRemoved = 0;
            if (removeCoveredCopies || LayerIsReferenced(lines, MarkEraseLayerName))
            {
                OverlapCleanupResult overlap = RemoveGeometryCoveredByLaserMarks(
                    lines, settings, coverWithLaserMarks: removeCoveredCopies);
                overlapRemoved = overlap.RemovedCount + overlap.TrimmedCount;
                changed |= overlapRemoved > 0;
            }
            int erasedRims = RemoveEntitiesOnLayer(lines, MarkEraseLayerName);
            changed |= erasedRims > 0;
            changed |= RemoveUnreferencedLayer(lines, MarkEraseLayerName);

            if (!string.IsNullOrWhiteSpace(markerLayerName))
            {
                changed |= RenameOrRemoveLayerRecord(
                    lines,
                    markerLayerName,
                    settings.LaserMark.Name);
            }
            changed |= RemoveUnreferencedLayer(lines, MarkDiscardLayerName);
            changed |= EnsureLayerColor(
                lines,
                settings.LaserMark.Name,
                settings.LaserMark.Color.ToString(CultureInfo.InvariantCulture));
            changed |= EnsureLayerTrueColor(
                lines,
                settings.LaserMark.Name,
                settings.LaserMark.TrueColorRgb);

            Result result = Inspect(lines, settings);
            result.DiscardedMarkerCount = discarded;
            result.ErasedMarkRimCount = erasedRims;
            result.OverlapRemovedCount = overlapRemoved;

            if (changed)
            {
                string tempPath = filePath + ".layers.tmp";
                try
                {
                    File.WriteAllLines(tempPath, lines, encoding);
                    File.Replace(tempPath, filePath, null);
                }
                finally
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
            }
            return result;
        }

        /// <summary>
        /// View -> DXF: SOLIDWORKS writes the edges of the back face of a
        /// FlatPattern / sheet view on top of the front ones, and a marker
        /// can land on the LASER_MARK copy SOLIDWORKS already wrote. Removes
        /// LINE/ARC/CIRCLE entities that repeat another one on the SAME layer
        /// (same geometry within 0.001 mm). Returns the number removed.
        /// </summary>
        public static int RemoveDuplicateGeometry(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return 0;
            Encoding encoding = Encoding.GetEncoding(932);
            List<string> lines = File.ReadAllLines(filePath, encoding).ToList();
            if (lines.Count < 2 || lines.Count % 2 != 0) return 0;

            int removed = RemoveDuplicateGeometry(lines);
            if (removed == 0) return 0;

            string tempPath = filePath + ".dedupe.tmp";
            try
            {
                File.WriteAllLines(tempPath, lines, encoding);
                File.Replace(tempPath, filePath, null);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            return removed;
        }

        /// <summary>
        /// Same as the file overload, on DXF lines in memory. Also drops an
        /// ARC lying on a CIRCLE of the same layer, centre and radius (the two
        /// rims of a through hole: one exported whole, the other in pieces).
        /// </summary>
        internal static int RemoveDuplicateGeometry(List<string> lines)
        {
            if (lines == null || lines.Count < 2) return 0;
            List<DxfRecord> allRecords = CollectEntityRecords(lines);
            var circles = new List<KeyValuePair<string, double[]>>();
            foreach (DxfRecord record in allRecords)
            {
                if (!IsDxfType(record, "CIRCLE")) continue;
                string circleKind;
                double[] circle = DuplicateSignature(lines, record, out circleKind);
                if (circle != null)
                    circles.Add(new KeyValuePair<string, double[]>(
                        circleKind.Substring(0, circleKind.LastIndexOf('|')), circle));
            }

            // Compared with a tolerance, not rounded keys: two copies 0.0002 mm
            // apart fall on different sides of a rounding boundary.
            // Bucketed by kind and the 1 mm cell of the mid/centre point; a
            // match is looked for in the neighbouring cells too.
            var kept = new Dictionary<string, List<double[]>>(StringComparer.Ordinal);
            var doomed = new List<DxfRecord>();
            foreach (DxfRecord record in allRecords)
            {
                string kind;
                double[] values = DuplicateSignature(lines, record, out kind);
                if (values == null) continue;
                if (kind.EndsWith("|ARC", StringComparison.Ordinal))
                {
                    string layerOnly = kind.Substring(0, kind.LastIndexOf('|'));
                    if (circles.Any(circle =>
                            circle.Key == layerOnly &&
                            Math.Abs(circle.Value[0] - values[0]) <= DuplicateTolerance &&
                            Math.Abs(circle.Value[1] - values[1]) <= DuplicateTolerance &&
                            Math.Abs(circle.Value[2] - values[2]) <= DuplicateTolerance))
                    {
                        doomed.Add(record);
                        continue;
                    }
                }
                bool isLine = kind.EndsWith("|LINE", StringComparison.Ordinal);
                long cx = (long)Math.Floor(isLine ? (values[0] + values[2]) / 2 : values[0]);
                long cy = (long)Math.Floor(isLine ? (values[1] + values[3]) / 2 : values[1]);
                bool duplicate = false;
                for (long dx = -1; dx <= 1 && !duplicate; dx++)
                {
                    for (long dy = -1; dy <= 1 && !duplicate; dy++)
                    {
                        List<double[]> bucket;
                        if (kept.TryGetValue(kind + "|" + (cx + dx) + "|" + (cy + dy), out bucket))
                            duplicate = bucket.Any(other => SameSignature(kind, other, values));
                    }
                }
                if (duplicate)
                {
                    doomed.Add(record);
                    continue;
                }
                string cell = kind + "|" + cx + "|" + cy;
                List<double[]> list;
                if (!kept.TryGetValue(cell, out list)) kept[cell] = list = new List<double[]>();
                list.Add(values);
            }
            if (doomed.Count == 0) return 0;

            foreach (DxfRecord record in doomed.OrderByDescending(item => item.Start))
            {
                lines.RemoveRange(
                    record.Start,
                    record.EndExclusive - record.Start);
            }
            return doomed.Count;
        }

        private const double DuplicateTolerance = 0.002;   // mm

        /// <summary>Layer|type and the numbers that define the entity (null: not compared).</summary>
        private static double[] DuplicateSignature(IList<string> lines, DxfRecord record, out string kind)
        {
            kind = null;
            string type = (record.Type ?? "").Trim().ToUpperInvariant();
            string[] codes;
            switch (type)
            {
                case "LINE": codes = new[] { "10", "20", "11", "21" }; break;
                case "CIRCLE": codes = new[] { "10", "20", "40" }; break;
                case "ARC": codes = new[] { "10", "20", "40", "50", "51" }; break;
                default: return null;
            }
            var values = new double[codes.Length];
            for (int index = 0; index < codes.Length; index++)
            {
                if (!TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, codes[index], out values[index]))
                    return null;
            }
            // A point-like LINE piece is real (model) geometry: never merged.
            if (type == "LINE" &&
                Math.Abs(values[0] - values[2]) <= DuplicateTolerance &&
                Math.Abs(values[1] - values[3]) <= DuplicateTolerance)
                return null;
            string layer = GetGroupValue(lines, record.Start, record.EndExclusive, "8") ?? "0";
            kind = layer.ToUpperInvariant() + "|" + type;
            return values;
        }

        private static bool SameSignature(string kind, double[] a, double[] b)
        {
            Func<double, double, bool> near = (x, y) => Math.Abs(x - y) <= DuplicateTolerance;
            if (kind.EndsWith("|LINE", StringComparison.Ordinal))
            {
                return (near(a[0], b[0]) && near(a[1], b[1]) && near(a[2], b[2]) && near(a[3], b[3])) ||
                       (near(a[0], b[2]) && near(a[1], b[3]) && near(a[2], b[0]) && near(a[3], b[1]));
            }
            if (!near(a[0], b[0]) || !near(a[1], b[1]) || !near(a[2], b[2])) return false;
            if (a.Length == 3) return true;
            // ARC angles (degrees): compare on the circle.
            Func<double, double, bool> sameAngle = (x, y) =>
            {
                double d = Math.Abs(((x - y) % 360 + 540) % 360 - 180);
                return d * Math.PI / 180 * Math.Max(a[2], 1e-9) <= DuplicateTolerance;
            };
            return sameAngle(a[3], b[3]) && sameAngle(a[4], b[4]);
        }

        /// <summary>
        /// After the marker layer was emptied: drop its LAYER record when the
        /// target layer already has one, otherwise rename it to the target so
        /// the target layer exists with the exporter's settings.
        /// </summary>
        private static bool RenameOrRemoveLayerRecord(
            List<string> lines,
            string fromLayer,
            string toLayer)
        {
            if (LayerIsReferenced(lines, fromLayer)) return false;

            List<DxfRecord> records = CollectLayerRecords(lines);
            DxfRecord from = records.FirstOrDefault(record => string.Equals(
                record.Type, fromLayer, StringComparison.OrdinalIgnoreCase));
            if (from == null) return false;

            bool targetExists = records.Any(record => string.Equals(
                record.Type, toLayer, StringComparison.OrdinalIgnoreCase));
            if (targetExists)
                return RemoveUnreferencedLayer(lines, fromLayer);

            SetGroupValue(lines, from.Start, from.EndExclusive, "2", toLayer);
            return true;
        }
    }
}
