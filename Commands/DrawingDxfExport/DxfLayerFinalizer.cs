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
    /// SOLIDWORKS can create the requested Drawing layers yet still emit
    /// generated bend lines and visible model-sketch entities on DXF layer 0.
    /// This final pass changes only those residual geometry entity records and
    /// preserves all coordinates, handles and other DXF data byte-for-byte at
    /// the group-pair level.
    /// </summary>
    internal static partial class DxfLayerFinalizer
    {
        // Model edges do not expose a drawing-layer setter in the
        // SOLIDWORKS API. The exporter mirrors marked Cut-Extrude geometry
        // into the temporary view sketch on this private layer, then this
        // pass maps it to the user's LASER_MARK layer.
        // SOLIDWORKS cuts layer names to 26 characters when it writes the
        // DXF: the old "__XTOOLPRO_CUT_EXTRUDE_MARK__" came out as
        // "__XTOOLPRO_CUT_EXTRUDE_MAR", was never matched here, and the
        // Cut-Extrude MARK geometry stayed on that private layer. Keep every
        // private layer name at 26 characters or less.
        internal const string CutExtrudeMarkerLayerName =
            "__XTOOLPRO_CUTEXT_MARK__";

        // A marker arc that SOLIDWORKS drew wrongly (checked by reading it
        // back) is parked on this private layer instead of being deleted from
        // the view sketch; the finalizer drops every entity on it.
        internal const string MarkDiscardLayerName =
            "__XTOOLPRO_MARK_DISCARD__";

        // Inner rim of a slanted MARK hole (two rings seen through the
        // sheet: only the outermost one is engraved). The exporter draws the
        // inner rim on this private layer; the finalizer removes every
        // CUT/BEND/layer-0 copy lying under it, then the rim itself, so the
        // inner ring is neither engraved nor cut.
        internal const string MarkEraseLayerName =
            "__XTOOLPRO_MARK_ERASE__";

        internal sealed class Result
        {
            public int LaserLineCount { get; set; }
            public int LaserArcCount { get; set; }
            public int LaserCircleCount { get; set; }
            public int LaserOtherCount { get; set; }
            public int DiscardedMarkerCount { get; set; }
            public int BendEntityCount { get; set; }
            public int LaserMarkEntityCount { get; set; }
            public int ReassignedEntityCount { get; set; }
            public int RemainingDefaultGeometryCount { get; set; }
            public int PartInfoTextCount { get; set; }
            public string PartInfoFontFile { get; set; }
            public DxfOpeningLayoutResult OpeningLayout { get; set; }
            public int OverlapRemovedCount { get; set; }
            public int OverlapTrimmedCount { get; set; }
            public int OverlapPartialCount { get; set; }
            public int ClosedMarkContourCount { get; set; }
            /// <summary>Nét LASER_MARK nằm trọn trên nét LASER_MARK khác đã bỏ (khắc một lần).</summary>
            public int MarkOverlapRemovedCount { get; set; }
            /// <summary>Nét trùng hoàn toàn với một nét khác cùng layer đã bị gỡ (máy sẽ cắt hai lần).</summary>
            public int DuplicateRemovedCount { get; set; }
            /// <summary>Góc ô MARK hở ≤ 0,02 mm (hai mép ô MARK xuyên tấm lệch vài µm) đã nối lại.</summary>
            public int MarkCornerGapsClosed { get; set; }
            /// <summary>Inner rims of slanted MARK holes removed (with their CUT copy).</summary>
            public int ErasedMarkRimCount { get; set; }

            /// <summary>
            /// LASER_MARK hole rings that replaced a concentric through bore
            /// (ring radius, bore radius in mm): DXF Check compares against
            /// the FlatPattern bore.
            /// </summary>
            public List<KeyValuePair<double, double>> MarkRingSubstitutions { get; } =
                new List<KeyValuePair<double, double>>();

            public string Summary =>
                $"DXF layers: BEND={BendEntityCount}, " +
                $"LASER_MARK={LaserMarkEntityCount} " +
                $"(LINE={LaserLineCount}, ARC={LaserArcCount}, " +
                $"CIRCLE={LaserCircleCount}, other={LaserOtherCount}), " +
                $"discarded-markers={DiscardedMarkerCount}, " +
                $"reassigned={ReassignedEntityCount}, " +
                $"overlap-removed={OverlapRemovedCount}, " +
                $"overlap-trimmed={OverlapTrimmedCount}, " +
                $"overlap-partial={OverlapPartialCount}, " +
                $"closed-mark-contours={ClosedMarkContourCount}, " +
                $"mark-overlap-removed={MarkOverlapRemovedCount}, " +
                $"duplicates-removed={DuplicateRemovedCount}, " +
                $"mark-corners-closed={MarkCornerGapsClosed}, " +
                $"erased-mark-rims={ErasedMarkRimCount}, " +
                $"remaining-layer-0={RemainingDefaultGeometryCount}, " +
                $"PART_INFO_TEXT={PartInfoTextCount}, " +
                $"opening-layout={OpeningLayout?.Changed == true}, " +
                $"font='{PartInfoFontFile ?? "unresolved"}'.";
        }

        private sealed class DxfRecord
        {
            public int Start { get; set; }
            public int EndExclusive { get; set; }
            public string Type { get; set; }
        }

        private struct DxfPoint2D
        {
            public double X;
            public double Y;
        }

        private struct DxfLineSegment2D
        {
            public DxfPoint2D Start;
            public DxfPoint2D End;
        }

        // DXF output is normally in millimetres. This tolerance is small
        // enough not to absorb a real gap, but covers export rounding where a
        // generated bend line terminates on the cutting contour.
        private const double BoundaryTouchTolerance = 0.001;
        private const double CurveBoundaryTouchTolerance = 0.02;

        private static readonly HashSet<string> GeometryTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "LINE",
                "ARC",
                "CIRCLE",
                "ELLIPSE",
                "LWPOLYLINE",
                "POLYLINE",
                "SPLINE",
                "POINT"
            };

        public static Result NormalizeManufacturingLayers(
            string filePath,
            bool includeBendLines,
            bool includeLaserMarks,
            DxfManufacturingLayerSettings layerSettings = null,
            bool applyMarkRingInterior = true,
            bool semanticLayers = false)
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
            string signature = Encoding.ASCII.GetString(header);
            if (signature.StartsWith(
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

            // Wrong marker arcs parked by the exporter must leave the file
            // before any classification sees them.
            int discardedMarkers = RemoveEntitiesOnLayer(
                lines,
                MarkDiscardLayerName);

            // An Area Hatch on the FlatPattern view reaches the DXF as
            // SW_HATCH_n blocks on layer 0 (hatch lines across the part) that
            // the laser would cut. They are drawing decoration, never part
            // geometry.
            int removedHatches = RemoveAreaHatches(lines);
            if (removedHatches > 0)
                System.Diagnostics.Debug.WriteLine(
                    "DXF finalizer removed " + removedHatches + " area hatch entities.");

            List<DxfRecord> entityRecords = CollectEntityRecords(lines);
            List<DxfLineSegment2D> cutBoundarySegments =
                CollectCutBoundarySegments(lines, entityRecords, settings);
            List<DxfLineSegment2D> cutCurveSegments =
                CollectCutCurveSegments(lines, entityRecords, settings);
            int reassigned = 0;
            bool entityColorChanged = false;

            // Process backwards because removing an entity true-colour pair
            // changes indices only for records that have already been handled.
            for (int index = entityRecords.Count - 1; index >= 0; index--)
            {
                DxfRecord record = entityRecords[index];
                string layer = GetGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "8") ?? "0";
                string targetLayer = MapCanonicalLayer(layer, settings);
                bool isDefaultLayer = string.Equals(
                    layer,
                    "0",
                    StringComparison.OrdinalIgnoreCase);
                bool isLaserLayer = string.Equals(
                    targetLayer,
                    settings.LaserMark.Name,
                    StringComparison.OrdinalIgnoreCase);

                // A layer-0 line lying along a CUT edge is a second copy of
                // that edge (for example the end edge of a rolled sheet). It
                // used to become LASER_MARK, and the overlap pass then deleted
                // the CUT edge under it: the contour was left open and the
                // edge was engraved instead of cut. Drop the copy, keep CUT.
                if (isDefaultLayer &&
                    (includeBendLines || includeLaserMarks) &&
                    IsDxfType(record, "LINE") &&
                    LineLiesAlongSegments(lines, record, cutBoundarySegments))
                {
                    lines.RemoveRange(
                        record.Start,
                        record.EndExclusive - record.Start);
                    reassigned++;
                    continue;
                }

                // semanticLayers: LASER_MARK đã được ApplySemanticPlan đặt bằng hình học từ feature MARK của
                // model — không đoán lại. Luật «nét thẳng chạm biên hai đầu = đường uốn» (dành cho bản map
                // .slddxfmap cũ) đổi vạch/rãnh MARK chạy từ biên sang biên thành BEND: laser không khắc.
                if (GeometryTypes.Contains(record.Type) &&
                    (isDefaultLayer ||
                     (includeBendLines && includeLaserMarks && isLaserLayer && !semanticLayers)) &&
                    !string.Equals(
                        layer,
                        CutExtrudeMarkerLayerName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    targetLayer = ClassifyTargetLayer(
                        lines,
                        record,
                        includeBendLines,
                        includeLaserMarks,
                        settings,
                        cutBoundarySegments,
                        cutCurveSegments);
                    if (string.IsNullOrEmpty(targetLayer)) continue;
                }

                if (!string.Equals(
                        targetLayer,
                        layer,
                        StringComparison.OrdinalIgnoreCase))
                {
                    SetGroupValue(
                        lines,
                        record.Start,
                        record.EndExclusive,
                        "8",
                        targetLayer);
                    reassigned++;
                }

                // An old-style POLYLINE carries its layer on the header AND on
                // every VERTEX/SEQEND record. Keep them identical: CAM/CAD
                // readers differ in which one they honour, and a stale copy
                // leaves part of the marking on the wrong layer. Done before
                // any pair is removed from this record so the (later) child
                // record offsets are still valid.
                if (string.Equals(
                        record.Type,
                        "POLYLINE",
                        StringComparison.OrdinalIgnoreCase))
                {
                    SyncPolylineChildLayers(
                        lines,
                        entityRecords,
                        index,
                        targetLayer);
                }

                if (!settings.ContainsLayer(targetLayer)) continue;

                if (GeometryTypes.Contains(record.Type) &&
                    string.Equals(
                        targetLayer,
                        settings.Bend.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    entityColorChanged |= SetAllExistingGroupValues(
                        lines,
                        record.Start,
                        record.EndExclusive,
                        "6",
                        "CENTER");
                }

                // One standard line type for every LASER_MARK entity, whatever
                // its geometry (LINE, ARC, CIRCLE, ELLIPSE, SPLINE, POLYLINE):
                // marking is continuous and takes the line type of its layer.
                // SOLIDWORKS can leave a sketch line style (dashed, phantom...)
                // on the entity, which CAM software then reads as a different
                // kind of line.
                if (GeometryTypes.Contains(record.Type) &&
                    string.Equals(
                        targetLayer,
                        settings.LaserMark.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    entityColorChanged |= SetAllExistingGroupValues(
                        lines,
                        record.Start,
                        record.EndExclusive,
                        "6",
                        "BYLAYER");
                }

                // SOLIDWORKS writes explicit colour 7 on these layer-0 lines.
                // Change it to BYLAYER so every manufacturing entity inherits
                // the independently configured colour of its layer.
                entityColorChanged |= SetAllExistingGroupValues(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "62",
                    "256");
                entityColorChanged |= RemoveGroupPairs(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "420") > 0;
            }

            // A marked model edge is exported once from the model component
            // (CUT) and once from the temporary view sketch (the private
            // marker layer); a LAZER sketch can also be drawn over an existing
            // CUT/BEND line. Keep only the LASER_MARK copy. Geometry that is
            // not under a LASER_MARK entity (unmarked holes) stays on CUT.
            OverlapCleanupResult overlap = includeLaserMarks
                ? RemoveGeometryCoveredByLaserMarks(lines, settings)
                : new OverlapCleanupResult();
            reassigned += overlap.RemovedCount + overlap.TrimmedCount;

            // Inner rims of slanted MARK holes: their CUT copy is gone (above),
            // now the rims themselves leave the file. Without LASER_MARK the
            // overlap pass did not run, so run it for the erase layer alone.
            if (!includeLaserMarks && LayerIsReferenced(lines, MarkEraseLayerName))
            {
                OverlapCleanupResult erasedOnly =
                    RemoveGeometryCoveredByLaserMarks(lines, settings, coverWithLaserMarks: false);
                reassigned += erasedOnly.RemovedCount + erasedOnly.TrimmedCount;
            }
            int erasedRims = RemoveEntitiesOnLayer(lines, MarkEraseLayerName);
            reassigned += erasedRims;

            // A hole marked on LASER_MARK keeps only its outer mark circle:
            // the concentric bore and anything else inside it leave the DXF.
            var markRingSubstitutions = new List<KeyValuePair<double, double>>();
            if (includeLaserMarks && applyMarkRingInterior)
                reassigned += RemoveGeometryInsideMarkRings(lines, settings, markRingSubstitutions);

            // A MARK edge that SOLIDWORKS could not mirror (for example the
            // spline edge of a slot in a rolled sheet) is still on CUT and
            // leaves its LASER_MARK contour open. Close such contours.
            int closedMarkContours = includeLaserMarks
                ? CloseLaserMarkContours(lines, settings)
                : 0;
            reassigned += closedMarkContours;

            // The FlatPattern of a rolled sheet can show a "bend line" exactly
            // on its straight end edges: a BEND copy on top of the CUT edge.
            if (includeBendLines)
                reassigned += RemoveBendLinesOnCutBoundary(lines, settings);

            // A through hole is exported twice (a whole CIRCLE for one rim,
            // ARC pieces for the other) and some lines twice: the laser would
            // cut them twice. Keep one copy per layer.
            int duplicatesRemoved = RemoveDuplicateGeometry(lines);
            reassigned += duplicatesRemoved;

            // Nét LASER_MARK nằm trọn trên nét LASER_MARK khác (mép và đáy rãnh MARK
            // không xuyên chiếu trùng nhau, vòng ghi vừa CIRCLE vừa ARC, sketch LAZER
            // vẽ đè cạnh MARK): laser chỉ khắc một lần.
            int markOverlapRemoved = includeLaserMarks
                ? RemoveLaserMarksCoveredByLaserMarks(lines, settings)
                : 0;
            reassigned += markOverlapRemoved;

            // Gỡ nét chồng giữa HAI mép của ô MARK xuyên tấm (lệch vài µm) có thể để lại góc hở
            // ≤ 0,02 mm: nối lại để ô MARK khép kín (4119 chi tiết 48/49).
            int markCornerGapsClosed = includeLaserMarks
                ? CloseLaserMarkCornerGaps(lines, settings)
                : 0;
            reassigned += markCornerGapsClosed;

            bool layerTableChanged = NormalizeLayerTable(lines, settings);
            layerTableChanged |= RemoveUnreferencedLayer(
                lines,
                CutExtrudeMarkerLayerName);
            layerTableChanged |= RemoveUnreferencedLayer(
                lines,
                MarkDiscardLayerName);
            layerTableChanged |= RemoveUnreferencedLayer(
                lines,
                MarkEraseLayerName);
            // Layer nào CÓ nét nhưng chưa có bản ghi trong bảng LAYER thì tạo bản ghi (màu,
            // true color, kiểu nét theo bảng cài đặt). Cơ chế «đặt layer bằng hình học» chỉ đổi tên
            // layer của nét, SolidWorks không ghi bản ghi layer LASER_MARK/BEND → CAD không biết
            // màu của layer ấy và vẽ màu mặc định thay vì màu trong bảng cài đặt.
            int layerRecordsBefore = CollectLayerRecords(lines).Count;
            var layerHandles = new DxfHandleAllocator(lines);
            foreach (DxfLayerMapOption option in settings.All)
            {
                if (LayerIsReferenced(lines, option.Name))
                    EnsureLayerRecord(lines, option.Name, option.Color, layerHandles);
            }
            layerHandles.WriteHandleSeed(lines);
            layerTableChanged |= CollectLayerRecords(lines).Count != layerRecordsBefore;
            bool layerColorChanged = false;
            foreach (DxfLayerMapOption option in settings.All)
            {
                layerColorChanged |= EnsureLayerColor(
                    lines,
                    option.Name,
                    option.Color.ToString(CultureInfo.InvariantCulture));
                layerColorChanged |= EnsureLayerTrueColor(
                    lines,
                    option.Name,
                    option.TrueColorRgb);
            }
            layerTableChanged |= EnsureLayerLineType(
                lines,
                settings.Bend.Name,
                "CENTER");

            bool visiblePatternsChanged = semanticLayers && NormalizeVisibleLinePatterns(lines, settings);
            int partInfoTextCount;
            string partInfoFontFile;
            bool partInfoFontChanged = NormalizePartInfoFont(
                lines,
                settings,
                out partInfoTextCount,
                out partInfoFontFile);

            DxfOpeningLayoutResult openingLayout = ApplyOpeningLayout(lines, settings);
            Result result = Inspect(lines, settings);
            result.OpeningLayout = openingLayout;
            result.ReassignedEntityCount = reassigned;
            result.PartInfoTextCount = partInfoTextCount;
            result.PartInfoFontFile = partInfoFontFile;
            result.OverlapRemovedCount = overlap.RemovedCount;
            result.OverlapTrimmedCount = overlap.TrimmedCount;
            result.OverlapPartialCount = overlap.PartialCount;
            result.ClosedMarkContourCount = closedMarkContours;
            result.MarkOverlapRemovedCount = markOverlapRemoved;
            result.DuplicateRemovedCount = duplicatesRemoved;
            result.MarkCornerGapsClosed = markCornerGapsClosed;
            result.DiscardedMarkerCount = discardedMarkers;
            result.ErasedMarkRimCount = erasedRims;
            result.MarkRingSubstitutions.AddRange(markRingSubstitutions);

            if (reassigned > 0 || entityColorChanged || layerTableChanged ||
                layerColorChanged || partInfoFontChanged || openingLayout.Changed || visiblePatternsChanged ||
                discardedMarkers > 0)
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

        private static string ClassifyTargetLayer(
            IList<string> lines,
            DxfRecord record,
            bool includeBendLines,
            bool includeLaserMarks,
            DxfManufacturingLayerSettings settings,
            IList<DxfLineSegment2D> cutBoundarySegments,
            IList<DxfLineSegment2D> cutCurveSegments)
        {
            if (includeBendLines && !includeLaserMarks)
                return settings.Bend.Name;
            if (!includeBendLines && includeLaserMarks)
                return settings.LaserMark.Name;
            if (!includeBendLines && !includeLaserMarks) return null;

            // A bend line is always straight. An ARC, CIRCLE, ELLIPSE, SPLINE
            // or POINT can never be one, whatever its line type or colour say
            // (a MARK sketch arc drawn with a dashed/centre style or a colour
            // close to the bend colour used to be sent to BEND, so the marking
            // arc never reached the LASER_MARK layer).
            if (!CanBeBendLine(record.Type)) return settings.LaserMark.Name;

            if (HasExclusiveEntityColor(
                    lines,
                    record,
                    settings.Bend,
                    settings.LaserMark))
                return settings.Bend.Name;
            if (HasExclusiveEntityColor(
                    lines,
                    record,
                    settings.LaserMark,
                    settings.Bend))
                return settings.LaserMark.Name;

            string lineType = GetGroupValue(
                lines,
                record.Start,
                record.EndExclusive,
                "6");
            if (IsBendLineType(lineType)) return settings.Bend.Name;

            // SOLIDWORKS sometimes exports one direction of generated bend
            // lines as Continuous sketch entities. The mapping file therefore
            // puts them on LASER_MARK together with genuine marking sketches.
            // A straight line whose two endpoints terminate on the CUT
            // contour is a bend crossing the flat pattern, not an interior
            // laser mark. Recheck existing LASER_MARK entities as well as
            // residual layer-0 entities so that case is corrected.
            if (IsBoundarySpanningLine(
                    lines,
                    record,
                    cutBoundarySegments,
                    cutCurveSegments))
                return settings.Bend.Name;

            return settings.LaserMark.Name;
        }

        private static bool CanBeBendLine(string type)
        {
            switch ((type ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "LINE":
                case "LWPOLYLINE":
                case "POLYLINE":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsDxfType(DxfRecord record, string type)
        {
            return string.Equals(
                record?.Type,
                type,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Copies the layer of an old-style POLYLINE onto its VERTEX and
        /// SEQEND records. <paramref name="parentIndex"/> is the index of the
        /// POLYLINE in <paramref name="records"/>.
        /// </summary>
        private static void SyncPolylineChildLayers(
            IList<string> lines,
            IList<DxfRecord> records,
            int parentIndex,
            string layer)
        {
            for (int index = parentIndex + 1; index < records.Count; index++)
            {
                DxfRecord child = records[index];
                bool vertex = IsDxfType(child, "VERTEX");
                bool sequenceEnd = IsDxfType(child, "SEQEND");
                if (!vertex && !sequenceEnd) return;

                if (GetGroupValue(
                        lines,
                        child.Start,
                        child.EndExclusive,
                        "8") != null)
                {
                    SetGroupValue(
                        lines,
                        child.Start,
                        child.EndExclusive,
                        "8",
                        layer);
                }
                if (sequenceEnd) return;
            }
        }

        /// <summary>
        /// Removes every entity whose layer is <paramref name="layerName"/>
        /// (an old-style POLYLINE goes together with its VERTEX/SEQEND
        /// records). Returns the number of entities removed.
        /// </summary>
        /// <summary>
        /// Removes HATCH entities and inserts of SOLIDWORKS hatch blocks
        /// (SW_HATCH_n) from the ENTITIES section.
        /// </summary>
        private static int RemoveAreaHatches(List<string> lines)
        {
            List<DxfRecord> records = CollectEntityRecords(lines);
            int removed = 0;
            for (int index = records.Count - 1; index >= 0; index--)
            {
                DxfRecord record = records[index];
                bool hatch = IsDxfType(record, "HATCH");
                if (!hatch && IsDxfType(record, "INSERT"))
                {
                    string block = GetGroupValue(
                        lines,
                        record.Start,
                        record.EndExclusive,
                        "2") ?? "";
                    hatch = block.StartsWith(
                        "SW_HATCH",
                        StringComparison.OrdinalIgnoreCase);
                }
                if (!hatch) continue;

                lines.RemoveRange(
                    record.Start,
                    record.EndExclusive - record.Start);
                removed++;
            }
            return removed;
        }

        private static int RemoveEntitiesOnLayer(
            List<string> lines,
            string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName)) return 0;

            List<DxfRecord> records = CollectEntityRecords(lines);
            var doomed = new List<DxfRecord>();
            for (int index = 0; index < records.Count; index++)
            {
                DxfRecord record = records[index];
                if (IsDxfType(record, "VERTEX") ||
                    IsDxfType(record, "SEQEND"))
                    continue;

                string layer = GetGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "8");
                if (!string.Equals(
                        layer,
                        layerName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                int endExclusive = record.EndExclusive;
                if (IsDxfType(record, "POLYLINE"))
                {
                    int cursor = index + 1;
                    while (cursor < records.Count &&
                           IsDxfType(records[cursor], "VERTEX"))
                        cursor++;
                    if (cursor < records.Count &&
                        IsDxfType(records[cursor], "SEQEND"))
                        cursor++;
                    if (cursor > index + 1)
                    {
                        endExclusive = records[cursor - 1].EndExclusive;
                        index = cursor - 1;
                    }
                }

                doomed.Add(new DxfRecord
                {
                    Start = record.Start,
                    EndExclusive = endExclusive,
                    Type = record.Type
                });
            }

            for (int index = doomed.Count - 1; index >= 0; index--)
            {
                lines.RemoveRange(
                    doomed[index].Start,
                    doomed[index].EndExclusive - doomed[index].Start);
            }
            return doomed.Count;
        }

        private static bool HasExclusiveEntityColor(
            IList<string> lines,
            DxfRecord record,
            DxfLayerMapOption candidate,
            DxfLayerMapOption other)
        {
            int aciColor;
            string aciRaw = GetGroupValue(
                lines,
                record.Start,
                record.EndExclusive,
                "62");
            if (int.TryParse(
                    aciRaw,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out aciColor))
            {
                aciColor = Math.Abs(aciColor);
                if (aciColor > 0 && aciColor < 256 &&
                    candidate.Color != other.Color &&
                    aciColor == candidate.Color)
                    return true;
            }

            int trueColor;
            string trueColorRaw = GetGroupValue(
                lines,
                record.Start,
                record.EndExclusive,
                "420");
            if (!int.TryParse(
                    trueColorRaw,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out trueColor))
                return false;

            int candidateRgb = GetLayerRgb(candidate);
            int otherRgb = GetLayerRgb(other);
            return candidateRgb != otherRgb && trueColor == candidateRgb;
        }

        private static int GetLayerRgb(DxfLayerMapOption option)
        { return option.TrueColorRgb ?? DxfPalette.Rgb(option.Color); }
        private static List<DxfLineSegment2D> CollectCutBoundarySegments(
            IList<string> lines,
            IEnumerable<DxfRecord> records,
            DxfManufacturingLayerSettings settings)
        {
            var segments = new List<DxfLineSegment2D>();
            foreach (DxfRecord record in records)
            {
                if (!string.Equals(
                        record.Type,
                        "LINE",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                string layer = GetGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "8") ?? "0";
                string canonicalLayer = MapCanonicalLayer(layer, settings);
                if (!string.Equals(
                        canonicalLayer,
                        settings.Cut.Name,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                DxfLineSegment2D segment;
                if (TryGetLineSegment(lines, record, out segment))
                    segments.Add(segment);
            }

            return segments;
        }

        /// <summary>
        /// CUT ARC / SPLINE / ELLIPSE / LWPOLYLINE contour cut into short
        /// straight pieces (at most 1 mm long). A skewed strip of a rolled
        /// sheet has SPLINE long sides: a bend crossing it ends on them, not
        /// on a CUT LINE.
        /// </summary>
        private static List<DxfLineSegment2D> CollectCutCurveSegments(
            IList<string> lines,
            IEnumerable<DxfRecord> records,
            DxfManufacturingLayerSettings settings)
        {
            var segments = new List<DxfLineSegment2D>();
            foreach (DxfRecord record in records)
            {
                string type = (record.Type ?? "").Trim().ToUpperInvariant();
                if (type != "ARC" && type != "SPLINE" &&
                    type != "ELLIPSE" && type != "LWPOLYLINE")
                    continue;

                string layer = GetGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "8") ?? "0";
                if (!string.Equals(
                        MapCanonicalLayer(layer, settings),
                        settings.Cut.Name,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                List<List<DxfPoint2D>> polylines;
                if (!TryTessellateEntity(lines, record, LaserChordTolerance,
                        1.0, out polylines))
                    continue;
                foreach (List<DxfPoint2D> polyline in polylines)
                {
                    for (int index = 1; index < polyline.Count; index++)
                        segments.Add(new DxfLineSegment2D
                        {
                            Start = polyline[index - 1],
                            End = polyline[index]
                        });
                }
            }
            return segments;
        }

        private static bool IsBoundarySpanningLine(
            IList<string> lines,
            DxfRecord record,
            IList<DxfLineSegment2D> cutBoundarySegments,
            IList<DxfLineSegment2D> cutCurveSegments = null)
        {
            bool hasLines = cutBoundarySegments != null && cutBoundarySegments.Count > 0;
            bool hasCurves = cutCurveSegments != null && cutCurveSegments.Count > 0;
            if (!string.Equals(
                    record.Type,
                    "LINE",
                    StringComparison.OrdinalIgnoreCase) ||
                (!hasLines && !hasCurves))
                return false;

            DxfLineSegment2D candidate;
            if (!TryGetLineSegment(lines, record, out candidate)) return false;

            double dx = candidate.End.X - candidate.Start.X;
            double dy = candidate.End.Y - candidate.Start.Y;
            double toleranceSquared =
                BoundaryTouchTolerance * BoundaryTouchTolerance;
            if ((dx * dx) + (dy * dy) <= toleranceSquared) return false;

            // A tessellated curve is a chord approximation: allow its chord
            // error on top of the touch tolerance.
            Func<DxfPoint2D, bool> touches = point =>
                (hasLines && PointTouchesBoundary(point, cutBoundarySegments)) ||
                (hasCurves && PointTouchesBoundary(point, cutCurveSegments,
                    CurveBoundaryTouchTolerance));

            if (!touches(candidate.Start) || !touches(candidate.End))
                return false;

            // A bend crosses the flat pattern, so its interior is away from
            // the contour. A LASER_MARK line drawn ON a CUT line also has both
            // ends on CUT; treating it as a bend left a BEND copy on top of
            // the old line instead of keeping only the laser mark.
            foreach (double fraction in new[] { 0.25, 0.5, 0.75 })
            {
                var interior = new DxfPoint2D
                {
                    X = candidate.Start.X + (dx * fraction),
                    Y = candidate.Start.Y + (dy * fraction)
                };
                if (!touches(interior))
                    return true;
            }
            return false;
        }

        private static bool TryGetLineSegment(
            IList<string> lines,
            DxfRecord record,
            out DxfLineSegment2D segment)
        {
            segment = new DxfLineSegment2D();
            double startX;
            double startY;
            double endX;
            double endY;
            if (!TryGetDoubleGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "10",
                    out startX) ||
                !TryGetDoubleGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "20",
                    out startY) ||
                !TryGetDoubleGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "11",
                    out endX) ||
                !TryGetDoubleGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "21",
                    out endY))
                return false;

            segment.Start = new DxfPoint2D { X = startX, Y = startY };
            segment.End = new DxfPoint2D { X = endX, Y = endY };
            return true;
        }

        private static bool TryGetDoubleGroupValue(
            IList<string> lines,
            int start,
            int endExclusive,
            string groupCode,
            out double value)
        {
            value = 0;
            string raw = GetGroupValue(
                lines,
                start,
                endExclusive,
                groupCode);
            return double.TryParse(
                raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        /// <summary>
        /// Removes BEND lines that lie along a CUT line over their whole
        /// length (both ends and the interior within the overlap tolerance).
        /// </summary>
        private static int RemoveBendLinesOnCutBoundary(
            List<string> lines,
            DxfManufacturingLayerSettings settings)
        {
            List<DxfRecord> records = CollectEntityRecords(lines);
            List<DxfLineSegment2D> cut =
                CollectCutBoundarySegments(lines, records, settings);
            if (cut.Count == 0) return 0;

            int removed = 0;
            for (int index = records.Count - 1; index >= 0; index--)
            {
                DxfRecord record = records[index];
                if (!IsDxfType(record, "LINE")) continue;
                string layer = GetGroupValue(
                    lines, record.Start, record.EndExclusive, "8") ?? "0";
                if (!string.Equals(
                        MapCanonicalLayer(layer, settings),
                        settings.Bend.Name,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!LineLiesAlongSegments(lines, record, cut)) continue;

                lines.RemoveRange(
                    record.Start,
                    record.EndExclusive - record.Start);
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// True when the LINE record lies along the given segments over its
        /// whole length (ends and interior within the overlap tolerance).
        /// </summary>
        private static bool LineLiesAlongSegments(
            IList<string> lines,
            DxfRecord record,
            IList<DxfLineSegment2D> segments)
        {
            if (segments == null || segments.Count == 0) return false;
            DxfLineSegment2D line;
            if (!TryGetLineSegment(lines, record, out line)) return false;
            double toleranceSquared = OverlapTolerance * OverlapTolerance;
            foreach (double fraction in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            {
                var point = new DxfPoint2D
                {
                    X = line.Start.X + ((line.End.X - line.Start.X) * fraction),
                    Y = line.Start.Y + ((line.End.Y - line.Start.Y) * fraction)
                };
                if (!segments.Any(segment =>
                        DistanceToSegmentSquared(point, segment) <= toleranceSquared))
                    return false;
            }
            return true;
        }

        private static bool PointTouchesBoundary(
            DxfPoint2D point,
            IEnumerable<DxfLineSegment2D> cutBoundarySegments,
            double tolerance = BoundaryTouchTolerance)
        {
            double toleranceSquared = tolerance * tolerance;
            foreach (DxfLineSegment2D segment in cutBoundarySegments)
            {
                if (DistanceToSegmentSquared(point, segment) <=
                    toleranceSquared)
                    return true;
            }

            return false;
        }

        private static double DistanceToSegmentSquared(
            DxfPoint2D point,
            DxfLineSegment2D segment)
        {
            double dx = segment.End.X - segment.Start.X;
            double dy = segment.End.Y - segment.Start.Y;
            double lengthSquared = (dx * dx) + (dy * dy);
            if (lengthSquared <= double.Epsilon)
            {
                double pointDx = point.X - segment.Start.X;
                double pointDy = point.Y - segment.Start.Y;
                return (pointDx * pointDx) + (pointDy * pointDy);
            }

            double projection =
                ((point.X - segment.Start.X) * dx +
                 (point.Y - segment.Start.Y) * dy) /
                lengthSquared;
            projection = Math.Max(0, Math.Min(1, projection));
            double nearestX = segment.Start.X + projection * dx;
            double nearestY = segment.Start.Y + projection * dy;
            double nearestDx = point.X - nearestX;
            double nearestDy = point.Y - nearestY;
            return (nearestDx * nearestDx) + (nearestDy * nearestDy);
        }

        private static bool IsBendLineType(string lineType)
        {
            if (string.IsNullOrWhiteSpace(lineType)) return false;

            string normalized = lineType.Trim();
            return !string.Equals(normalized, "BYLAYER", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(normalized, "BYBLOCK", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(normalized, "CONTINUOUS", StringComparison.OrdinalIgnoreCase);
        }

        private static string MapCanonicalLayer(
            string layer,
            DxfManufacturingLayerSettings settings)
        {
            if (string.Equals(
                    layer,
                    CutExtrudeMarkerLayerName,
                    StringComparison.OrdinalIgnoreCase))
                return settings.LaserMark.Name;
            if (string.Equals(layer, "CUT", StringComparison.OrdinalIgnoreCase))
                return settings.Cut.Name;
            if (string.Equals(layer, "BEND", StringComparison.OrdinalIgnoreCase))
                return settings.Bend.Name;
            if (string.Equals(
                    layer,
                    "LASER_MARK",
                    StringComparison.OrdinalIgnoreCase))
                return settings.LaserMark.Name;
            if (string.Equals(
                    layer,
                    "PART_INFO",
                    StringComparison.OrdinalIgnoreCase))
                return settings.PartInfo.Name;
            return layer;
        }

        private static bool RemoveUnreferencedLayer(
            List<string> lines,
            string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName) ||
                LayerIsReferenced(lines, layerName))
                return false;

            DxfRecord record = CollectLayerRecords(lines)
                .FirstOrDefault(candidate => string.Equals(
                    candidate.Type,
                    layerName,
                    StringComparison.OrdinalIgnoreCase));
            if (record == null) return false;

            lines.RemoveRange(
                record.Start,
                record.EndExclusive - record.Start);
            UpdateLayerTableDeclaredCount(lines);
            return true;
        }

        private static Result Inspect(
            IList<string> lines,
            DxfManufacturingLayerSettings settings)
        {
            var result = new Result();
            foreach (DxfRecord record in CollectEntityRecords(lines))
            {
                if (!GeometryTypes.Contains(record.Type)) continue;
                string layer = GetGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "8") ?? "0";
                if (string.Equals(
                        layer,
                        settings.Bend.Name,
                        StringComparison.OrdinalIgnoreCase))
                    result.BendEntityCount++;
                else if (string.Equals(
                             layer,
                             settings.LaserMark.Name,
                             StringComparison.OrdinalIgnoreCase))
                {
                    result.LaserMarkEntityCount++;
                    switch ((record.Type ?? string.Empty).Trim().ToUpperInvariant())
                    {
                        case "LINE": result.LaserLineCount++; break;
                        case "ARC": result.LaserArcCount++; break;
                        case "CIRCLE": result.LaserCircleCount++; break;
                        default: result.LaserOtherCount++; break;
                    }
                }
                else if (string.Equals(layer, "0", StringComparison.OrdinalIgnoreCase))
                    result.RemainingDefaultGeometryCount++;
            }
            return result;
        }

        private static List<DxfRecord> CollectEntityRecords(IList<string> lines)
        {
            var records = new List<DxfRecord>();
            string section = string.Empty;
            int currentStart = -1;
            string currentType = null;

            for (int index = 0; index + 1 < lines.Count; index += 2)
            {
                string code = lines[index].Trim();
                string value = lines[index + 1].Trim();

                if (code == "0" && value == "SECTION" && index + 3 < lines.Count)
                {
                    section = lines[index + 3].Trim();
                    index += 2;
                    continue;
                }

                if (code == "0" && value == "ENDSEC")
                {
                    if (section == "ENTITIES" && currentStart >= 0)
                    {
                        records.Add(new DxfRecord
                        {
                            Start = currentStart,
                            EndExclusive = index,
                            Type = currentType
                        });
                    }
                    currentStart = -1;
                    currentType = null;
                    section = string.Empty;
                    continue;
                }

                if (section == "ENTITIES" && code == "0")
                {
                    if (currentStart >= 0)
                    {
                        records.Add(new DxfRecord
                        {
                            Start = currentStart,
                            EndExclusive = index,
                            Type = currentType
                        });
                    }
                    currentStart = index;
                    currentType = value;
                }
            }

            return records;
        }

        private static bool NormalizeLayerTable(
            List<string> lines,
            DxfManufacturingLayerSettings settings)
        {
            var mappings = new[]
            {
                new { Canonical = "CUT", Target = settings.Cut.Name },
                new { Canonical = "BEND", Target = settings.Bend.Name },
                new { Canonical = "LASER_MARK", Target = settings.LaserMark.Name },
                new { Canonical = "PART_INFO", Target = settings.PartInfo.Name }
            };
            bool changed = false;
            bool removedRecord = false;

            foreach (var mapping in mappings)
            {
                if (string.Equals(
                        mapping.Canonical,
                        mapping.Target,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                List<DxfRecord> records = CollectLayerRecords(lines);
                DxfRecord canonicalRecord = records.FirstOrDefault(record =>
                    string.Equals(
                        record.Type,
                        mapping.Canonical,
                        StringComparison.OrdinalIgnoreCase));
                if (canonicalRecord == null) continue;

                DxfRecord targetRecord = records.FirstOrDefault(record =>
                    string.Equals(
                        record.Type,
                        mapping.Target,
                        StringComparison.OrdinalIgnoreCase));

                // All manufacturing entity references have already been
                // remapped above. Keep a canonical record only if some other
                // DXF section still refers to it.
                if (LayerIsReferenced(lines, mapping.Canonical)) continue;

                if (targetRecord == null)
                {
                    SetGroupValue(
                        lines,
                        canonicalRecord.Start,
                        canonicalRecord.EndExclusive,
                        "2",
                        mapping.Target);
                    changed = true;
                }
                else
                {
                    lines.RemoveRange(
                        canonicalRecord.Start,
                        canonicalRecord.EndExclusive - canonicalRecord.Start);
                    changed = true;
                    removedRecord = true;
                }
            }

            if (removedRecord) UpdateLayerTableDeclaredCount(lines);
            return changed;
        }

        private static List<DxfRecord> CollectLayerRecords(
            IList<string> lines)
        {
            return CollectTableRecords(lines, "LAYER", "LAYER");
        }

        private static List<DxfRecord> CollectStyleRecords(
            IList<string> lines)
        {
            return CollectTableRecords(lines, "STYLE", "STYLE");
        }

        private static List<DxfRecord> CollectTableRecords(
            IList<string> lines,
            string tableName,
            string recordName)
        {
            var records = new List<DxfRecord>();
            string section = string.Empty;
            string table = string.Empty;
            int currentStart = -1;

            for (int index = 0; index + 1 < lines.Count; index += 2)
            {
                string code = lines[index].Trim();
                string value = lines[index + 1].Trim();
                if (code == "0" && value == "SECTION" && index + 3 < lines.Count)
                {
                    section = lines[index + 3].Trim();
                    index += 2;
                    continue;
                }
                if (code == "0" && value == "ENDSEC")
                {
                    if (currentStart >= 0)
                        AddNamedTableRecord(
                            lines,
                            records,
                            currentStart,
                            index);
                    currentStart = -1;
                    section = string.Empty;
                    table = string.Empty;
                    continue;
                }
                if (section != "TABLES") continue;

                if (code == "0" && value == "TABLE" && index + 3 < lines.Count)
                {
                    if (currentStart >= 0)
                        AddNamedTableRecord(
                            lines,
                            records,
                            currentStart,
                            index);
                    currentStart = -1;
                    table = lines[index + 3].Trim();
                    index += 2;
                    continue;
                }
                if (code == "0" && value == "ENDTAB")
                {
                    if (currentStart >= 0)
                        AddNamedTableRecord(
                            lines,
                            records,
                            currentStart,
                            index);
                    currentStart = -1;
                    table = string.Empty;
                    continue;
                }
                if (string.Equals(
                        table,
                        tableName,
                        StringComparison.OrdinalIgnoreCase) &&
                    code == "0" &&
                    string.Equals(
                        value,
                        recordName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (currentStart >= 0)
                        AddNamedTableRecord(
                            lines,
                            records,
                            currentStart,
                            index);
                    currentStart = index;
                }
            }
            return records;
        }

        private static void AddNamedTableRecord(
            IList<string> lines,
            ICollection<DxfRecord> records,
            int start,
            int endExclusive)
        {
            records.Add(new DxfRecord
            {
                Start = start,
                EndExclusive = endExclusive,
                Type = GetGroupValue(lines, start, endExclusive, "2")
            });
        }

        /// <summary>
        /// SOLIDWORKS may ignore ITextFormat.TypeFaceName while saving a
        /// Drawing note to DXF and keep its generated TXT/SHX STYLE. Point only
        /// the styles referenced by PART_INFO TEXT/MTEXT entities at the actual
        /// installed Windows font file selected by the user.
        /// </summary>
        private static bool NormalizePartInfoFont(
            List<string> lines,
            DxfManufacturingLayerSettings settings,
            out int partInfoTextCount,
            out string fontFile)
        {
            partInfoTextCount = 0;
            fontFile = DxfFontFileResolver.Resolve(
                settings.PartInfoFontName);
            var styleNames = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (DxfRecord record in CollectEntityRecords(lines))
            {
                if (!string.Equals(
                        record.Type,
                        "TEXT",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(
                        record.Type,
                        "MTEXT",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string layer = GetGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "8");
                if (!string.Equals(
                        layer,
                        settings.PartInfo.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                partInfoTextCount++;
                string styleName = GetGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "7");
                styleNames.Add(string.IsNullOrWhiteSpace(styleName)
                    ? "Standard"
                    : styleName);
            }

            if (partInfoTextCount == 0 ||
                styleNames.Count == 0 ||
                string.IsNullOrWhiteSpace(fontFile))
            {
                return false;
            }

            bool changed = false;
            foreach (DxfRecord record in CollectStyleRecords(lines))
            {
                if (!styleNames.Contains(record.Type)) continue;

                changed |= SetAllExistingGroupValues(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "3",
                    fontFile);
                // A DXF big-font entry takes precedence for many viewers.
                // Clear SOLIDWORKS' fixed "bigfont" value for TrueType text.
                changed |= SetAllExistingGroupValues(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "4",
                    string.Empty);
            }

            return changed;
        }

        private static bool LayerIsReferenced(
            IList<string> lines,
            string layerName)
        {
            for (int index = 0; index + 1 < lines.Count; index += 2)
            {
                if (lines[index].Trim() == "8" &&
                    string.Equals(
                        lines[index + 1].Trim(),
                        layerName,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void UpdateLayerTableDeclaredCount(List<string> lines)
        {
            int actualCount = CollectLayerRecords(lines).Count;
            string section = string.Empty;
            for (int index = 0; index + 3 < lines.Count; index += 2)
            {
                string code = lines[index].Trim();
                string value = lines[index + 1].Trim();
                if (code == "0" && value == "SECTION")
                {
                    section = lines[index + 3].Trim();
                    index += 2;
                    continue;
                }
                if (code == "0" && value == "ENDSEC")
                {
                    section = string.Empty;
                    continue;
                }
                if (section != "TABLES" || code != "0" || value != "TABLE")
                    continue;
                if (!string.Equals(
                        lines[index + 3].Trim(),
                        "LAYER",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                for (int cursor = index + 4;
                     cursor + 1 < lines.Count && lines[cursor].Trim() != "0";
                     cursor += 2)
                {
                    if (lines[cursor].Trim() != "70") continue;
                    lines[cursor + 1] =
                        actualCount.ToString(CultureInfo.InvariantCulture);
                    return;
                }
            }
        }

        private static string GetGroupValue(
            IList<string> lines,
            int start,
            int endExclusive,
            string groupCode)
        {
            for (int index = start; index + 1 < endExclusive; index += 2)
            {
                if (lines[index].Trim() == groupCode)
                {
                    string value = lines[index + 1].Trim();
                    return groupCode == "8"
                        ? CanonicalPrivateLayerName(value)
                        : value;
                }
            }
            return null;
        }

        private static readonly string[] PrivateLayerNames =
        {
            CutExtrudeMarkerLayerName,
            MarkDiscardLayerName,
            MarkEraseLayerName,
            "__XTOOLPRO_CUT_EXTRUDE_MARK__"   // name used before 26-char fix
        };

        /// <summary>
        /// SOLIDWORKS may write a private layer name cut short (26 characters
        /// seen in SOLIDWORKS 2025). Map such a prefix back to the private
        /// name so the entity is still recognised; the legacy Cut-Extrude
        /// marker name maps to the current one.
        /// </summary>
        private static string CanonicalPrivateLayerName(string layer)
        {
            if (string.IsNullOrEmpty(layer) ||
                layer.Length < 20 ||
                !layer.StartsWith("__XTOOLPRO_", StringComparison.OrdinalIgnoreCase))
                return layer;
            foreach (string name in PrivateLayerNames)
            {
                if (name.StartsWith(layer, StringComparison.OrdinalIgnoreCase))
                {
                    return name.StartsWith("__XTOOLPRO_CUT_EXTRUDE", StringComparison.OrdinalIgnoreCase)
                        ? CutExtrudeMarkerLayerName
                        : name;
                }
            }
            return layer;
        }

        private static void SetGroupValue(
            IList<string> lines,
            int start,
            int endExclusive,
            string groupCode,
            string value)
        {
            for (int index = start; index + 1 < endExclusive; index += 2)
            {
                if (lines[index].Trim() == groupCode)
                {
                    lines[index + 1] = value;
                    return;
                }
            }
            throw new InvalidDataException(
                "DXF entity does not contain group code " + groupCode + ".");
        }

        private static bool SetAllExistingGroupValues(
            IList<string> lines,
            int start,
            int endExclusive,
            string groupCode,
            string value)
        {
            bool changed = false;
            for (int index = start; index + 1 < endExclusive; index += 2)
            {
                if (lines[index].Trim() == groupCode)
                {
                    if (!string.Equals(
                            lines[index + 1].Trim(),
                            value,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        lines[index + 1] = value;
                        changed = true;
                    }
                }
            }
            return changed;
        }

        private static int RemoveGroupPairs(
            List<string> lines,
            int start,
            int endExclusive,
            string groupCode)
        {
            int removed = 0;
            for (int index = endExclusive - 2; index >= start; index -= 2)
            {
                if (lines[index].Trim() != groupCode) continue;
                lines.RemoveAt(index + 1);
                lines.RemoveAt(index);
                removed++;
            }
            return removed;
        }

        private static bool EnsureLayerColor(
            IList<string> lines,
            string layerName,
            string colorIndex)
        {
            string section = string.Empty;
            int layerStart = -1;

            for (int index = 0; index + 1 < lines.Count; index += 2)
            {
                string code = lines[index].Trim();
                string value = lines[index + 1].Trim();
                if (code == "0" && value == "SECTION" && index + 3 < lines.Count)
                {
                    section = lines[index + 3].Trim();
                    index += 2;
                    continue;
                }
                if (code == "0" && value == "ENDSEC")
                {
                    section = string.Empty;
                    layerStart = -1;
                    continue;
                }
                if (section != "TABLES") continue;

                if (code == "0")
                {
                    layerStart = value == "LAYER" ? index : -1;
                    continue;
                }
                if (layerStart < 0 || code != "2" ||
                    !string.Equals(value, layerName, StringComparison.OrdinalIgnoreCase))
                    continue;

                for (int cursor = index + 2;
                     cursor + 1 < lines.Count && lines[cursor].Trim() != "0";
                     cursor += 2)
                {
                    if (lines[cursor].Trim() == "62")
                    {
                        if (string.Equals(
                                lines[cursor + 1].Trim(),
                                colorIndex,
                                StringComparison.OrdinalIgnoreCase))
                            return false;

                        lines[cursor + 1] = colorIndex;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool EnsureLayerLineType(
            IList<string> lines,
            string layerName,
            string lineType)
        {
            string section = string.Empty;
            int layerStart = -1;

            for (int index = 0; index + 1 < lines.Count; index += 2)
            {
                string code = lines[index].Trim();
                string value = lines[index + 1].Trim();
                if (code == "0" && value == "SECTION" && index + 3 < lines.Count)
                {
                    section = lines[index + 3].Trim();
                    index += 2;
                    continue;
                }
                if (code == "0" && value == "ENDSEC")
                {
                    section = string.Empty;
                    layerStart = -1;
                    continue;
                }
                if (section != "TABLES") continue;

                if (code == "0")
                {
                    layerStart = value == "LAYER" ? index : -1;
                    continue;
                }
                if (layerStart < 0 || code != "2" ||
                    !string.Equals(
                        value,
                        layerName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                for (int cursor = index + 2;
                     cursor + 1 < lines.Count && lines[cursor].Trim() != "0";
                     cursor += 2)
                {
                    if (lines[cursor].Trim() != "6") continue;
                    if (string.Equals(
                            lines[cursor + 1].Trim(),
                            lineType,
                            StringComparison.OrdinalIgnoreCase))
                        return false;

                    lines[cursor + 1] = lineType;
                    return true;
                }
            }
            return false;
        }

        private static bool EnsureLayerTrueColor(
            List<string> lines,
            string layerName,
            int? trueColorRgb)
        {
            foreach (DxfRecord record in CollectLayerRecords(lines))
            {
                if (!string.Equals(
                        record.Type,
                        layerName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                for (int index = record.Start;
                     index + 1 < record.EndExclusive;
                     index += 2)
                {
                    if (lines[index].Trim() != "420") continue;
                    if (!trueColorRgb.HasValue)
                    {
                        lines.RemoveAt(index + 1);
                        lines.RemoveAt(index);
                        return true;
                    }

                    string value = trueColorRgb.Value.ToString(
                        CultureInfo.InvariantCulture);
                    if (string.Equals(
                            lines[index + 1].Trim(),
                            value,
                            StringComparison.OrdinalIgnoreCase))
                        return false;
                    lines[index + 1] = value;
                    return true;
                }

                if (!trueColorRgb.HasValue) return false;
                lines.Insert(record.EndExclusive, "420");
                lines.Insert(
                    record.EndExclusive + 1,
                    trueColorRgb.Value.ToString(
                        CultureInfo.InvariantCulture));
                return true;
            }
            return false;
        }
    }
}
