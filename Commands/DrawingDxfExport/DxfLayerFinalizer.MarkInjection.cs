// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>One MARK sketch entity in SOURCE sheet coordinates (metres).</summary>
    internal sealed class DxfInjectMark
    {
        /// <summary>LINE (2 points), CIRCLE (centre, point on circle),
        /// ARC (start, end, a point on the arc) or POLYLINE.</summary>
        public string Kind = "";
        public List<double[]> Points = new List<double[]>();
    }

    internal sealed class DxfMarkInjectionResult
    {
        public bool Applied;
        public int MarksWritten;
        public int EntitiesWritten;
        public double ExtentErrorMm;

        /// <summary>Aligned by centre because the sizes differ by an unexplained amount (warn).</summary>
        public bool AlignedByCenter;

        /// <summary>Border margin per side of the source view outline, when that explains the size difference.</summary>
        public double UniformMarginMm;

        /// <summary>True when the position came from the block transform (InjectExactMarks).</summary>
        public bool Exact;

        /// <summary>Offset between the temporary drawing (metres x 1000) and the DXF, in mm.</summary>
        public double OriginOffsetXMm;
        public double OriginOffsetYMm;
        public string Message = "";

        /// <summary>
        /// PlaceSectionMarks: "carried" when the position came from the MARK
        /// sketches SOLIDWORKS exported with the geometry, otherwise the label
        /// of the verified alignment used.
        /// </summary>
        public string Method = "";

        /// <summary>Marks whose own exported copy was found in the DXF.</summary>
        public int MarksFromCopies;
        public int CopiesRelayered;
        public int CopiesRemoved;
        public string Diagnostics = "";
    }

    /// <summary>
    /// A Section view is exported through a sketch block, and a block cannot
    /// hold entities on their own layer, so the MARK sketches drawn on such a
    /// view cannot be redrawn by SOLIDWORKS. They are written into the saved
    /// DXF instead, on the LASER_MARK layer.
    ///
    /// The position is not guessed: the exported geometry is measured in the
    /// DXF (bounding box) and compared with the outline of the source view
    /// (sheet coordinates) and its scale. Only when the two agree - same size
    /// within tolerance - are the sketches moved with that scale and offset;
    /// otherwise nothing is written and the reason is reported.
    /// </summary>
    internal static partial class DxfLayerFinalizer
    {
        private const double MarkInjectionAlignToleranceMm = 0.05;
        private const double MarkInjectionCenterToleranceMm = 1.0;

        /// <summary>Size agreement between the DXF geometry and the block geometry (mm, plus 0.2 %).</summary>
        private const double ExactBoundsToleranceMm = 0.3;

        private sealed class DxfBounds
        {
            public double MinX = double.MaxValue;
            public double MinY = double.MaxValue;
            public double MaxX = double.MinValue;
            public double MaxY = double.MinValue;
            public bool HasData => MinX <= MaxX && MinY <= MaxY;

            public void Add(double x, double y)
            {
                if (double.IsNaN(x) || double.IsNaN(y) ||
                    double.IsInfinity(x) || double.IsInfinity(y))
                    return;
                if (x < MinX) MinX = x;
                if (x > MaxX) MaxX = x;
                if (y < MinY) MinY = y;
                if (y > MaxY) MaxY = y;
            }
        }

        private sealed class DxfBlockDefinition
        {
            public double BaseX;
            public double BaseY;
            public readonly List<DxfRecord> Entities = new List<DxfRecord>();
        }

        public static DxfMarkInjectionResult InjectSheetMarks(
            string filePath,
            IList<DxfInjectMark> marks,
            double[] sourceOutline,
            double viewScale,
            DxfManufacturingLayerSettings layerSettings)
        {
            var result = new DxfMarkInjectionResult();
            if (marks == null || marks.Count == 0)
            {
                result.Message = "không có sketch MARK để ghi";
                return result;
            }
            if (sourceOutline == null || sourceOutline.Length < 4 ||
                !(viewScale > 0))
            {
                result.Message = "không đọc được khung hoặc tỷ lệ của view gốc";
                return result;
            }
            DxfManufacturingLayerSettings settings =
                DxfManufacturingLayerSettings.Normalize(layerSettings);
            List<string> lines;
            Encoding encoding;
            string loadMessage;
            if (!TryLoadDxfLines(filePath, out lines, out encoding, out loadMessage))
            {
                result.Message = loadMessage;
                return result;
            }

            DxfBounds bounds;
            string reason;
            if (!TryGetGeometryBounds(lines, out bounds, out reason))
            {
                result.Message = "không đo được hình trong DXF (" + reason + ")";
                return result;
            }

            // Sheet metres at the source view scale -> DXF millimetres at 1:1.
            double k = 1000.0 / viewScale;
            double outlineWidth = sourceOutline[2] - sourceOutline[0];
            double outlineHeight = sourceOutline[3] - sourceOutline[1];
            double expectedWidth = outlineWidth * k;
            double expectedHeight = outlineHeight * k;
            double dxfWidth = bounds.MaxX - bounds.MinX;
            double dxfHeight = bounds.MaxY - bounds.MinY;

            string measured =
                "kích thước hình trong DXF (" +
                dxfWidth.ToString("0.###", CultureInfo.InvariantCulture) + " x " +
                dxfHeight.ToString("0.###", CultureInfo.InvariantCulture) +
                " mm) so với khung view gốc (" +
                expectedWidth.ToString("0.###", CultureInfo.InvariantCulture) + " x " +
                expectedHeight.ToString("0.###", CultureInfo.InvariantCulture) +
                " mm, tỷ lệ 1:" +
                (1 / viewScale).ToString("0.###", CultureInfo.InvariantCulture) + ")";

            double widthGap = expectedWidth - dxfWidth;
            double heightGap = expectedHeight - dxfHeight;
            double error = Math.Max(Math.Abs(widthGap), Math.Abs(heightGap));
            result.ExtentErrorMm = error;

            // The scale is checked from the DIFFERENCE of the two sides, which
            // a uniform margin cannot change: (dxfW - dxfH) = k * (outW - outH).
            double expectedSideDifference = (outlineWidth - outlineHeight) * k;
            if (Math.Abs(expectedSideDifference) > 5.0)
            {
                double dxfSideDifference = dxfWidth - dxfHeight;
                // 0.5 % of the difference plus 1 mm for a margin that is not
                // perfectly equal on the two sides.
                if (Math.Abs(dxfSideDifference - expectedSideDifference) >
                    0.005 * Math.Abs(expectedSideDifference) +
                        MarkInjectionCenterToleranceMm)
                {
                    result.Message = measured +
                        " không cùng tỷ lệ nên không xác định được vị trí sketch MARK";
                    return result;
                }
            }

            bool center;
            if (error <= MarkInjectionAlignToleranceMm)
            {
                center = false;
            }
            else if (widthGap >= -MarkInjectionAlignToleranceMm &&
                     heightGap >= -MarkInjectionAlignToleranceMm &&
                     Math.Abs(widthGap - heightGap) <= MarkInjectionAlignToleranceMm)
            {
                // The outline of a view is its geometry plus a border margin of
                // the same width on every side: both sides are short by the
                // same amount. Aligning the CENTRES is then exact.
                center = true;
                result.UniformMarginMm = (widthGap + heightGap) / 4;
                result.ExtentErrorMm = 0;
            }
            else if (error <= MarkInjectionCenterToleranceMm)
            {
                center = true;
            }
            else
            {
                result.Message = measured +
                    " lệch nhau không đều nên không xác định được vị trí sketch MARK";
                return result;
            }
            result.AlignedByCenter = center && result.UniformMarginMm <= 0;

            double offsetX = center
                ? (bounds.MinX + bounds.MaxX) / 2 -
                  ((sourceOutline[0] + sourceOutline[2]) / 2) * k
                : bounds.MinX - sourceOutline[0] * k;
            double offsetY = center
                ? (bounds.MinY + bounds.MaxY) / 2 -
                  ((sourceOutline[1] + sourceOutline[3]) / 2) * k
                : bounds.MinY - sourceOutline[1] * k;
            Func<double[], double[]> map = point => new[]
            {
                point[0] * k + offsetX,
                point[1] * k + offsetY
            };

            WriteMarksIntoDxf(filePath, lines, encoding, marks, map, k, settings, result);
            return result;
        }

        /// <summary>
        /// Writes the MARK sketches into the DXF at a position that is NOT
        /// guessed: <paramref name="marks"/> are in the coordinates of the
        /// temporary drawing (metres, block already at 1:1) and
        /// <paramref name="expectedBounds"/> is where the block geometry sits
        /// in that same drawing (minX, minY, maxX, maxY, metres) - both
        /// computed from the block instance transform of SOLIDWORKS itself.
        /// The exported geometry is measured in the DXF; it must have the size
        /// of the block geometry, and its position then gives the offset
        /// between the drawing and the DXF. Nothing depends on the outline
        /// of the source view.
        /// </summary>
        public static DxfMarkInjectionResult InjectExactMarks(
            string filePath,
            IList<DxfInjectMark> marks,
            double[] expectedBounds,
            DxfManufacturingLayerSettings layerSettings)
        {
            var result = new DxfMarkInjectionResult { Exact = true };
            if (marks == null || marks.Count == 0)
            {
                result.Message = "không có sketch MARK để ghi";
                return result;
            }
            if (expectedBounds == null || expectedBounds.Length < 4)
            {
                result.Message = "không đọc được vị trí block trong Drawing tạm";
                return result;
            }

            DxfManufacturingLayerSettings settings =
                DxfManufacturingLayerSettings.Normalize(layerSettings);
            List<string> lines;
            Encoding encoding;
            string loadMessage;
            if (!TryLoadDxfLines(filePath, out lines, out encoding, out loadMessage))
            {
                result.Message = loadMessage;
                return result;
            }

            DxfBounds bounds;
            string reason;
            if (!TryGetGeometryBounds(lines, out bounds, out reason))
            {
                result.Message = "không đo được hình trong DXF (" + reason + ")";
                return result;
            }

            double expectedWidth = (expectedBounds[2] - expectedBounds[0]) * 1000.0;
            double expectedHeight = (expectedBounds[3] - expectedBounds[1]) * 1000.0;
            double dxfWidth = bounds.MaxX - bounds.MinX;
            double dxfHeight = bounds.MaxY - bounds.MinY;
            double error = Math.Max(
                Math.Abs(dxfWidth - expectedWidth),
                Math.Abs(dxfHeight - expectedHeight));
            result.ExtentErrorMm = error;

            double tolerance = ExactSizeToleranceMm(expectedWidth, expectedHeight);
            if (error > tolerance)
            {
                result.Message =
                    "kích thước hình trong DXF (" + FormatMm(dxfWidth) + " x " +
                    FormatMm(dxfHeight) + " mm) không khớp block trong Drawing tạm (" +
                    FormatMm(expectedWidth) + " x " + FormatMm(expectedHeight) +
                    " mm) nên không xác định được vị trí sketch MARK";
                return result;
            }

            // Centre alignment: the residual of an equal-size box is halved.
            double offsetX = (bounds.MinX + bounds.MaxX) / 2 -
                             (expectedBounds[0] + expectedBounds[2]) / 2 * 1000.0;
            double offsetY = (bounds.MinY + bounds.MaxY) / 2 -
                             (expectedBounds[1] + expectedBounds[3]) / 2 * 1000.0;
            result.OriginOffsetXMm = offsetX;
            result.OriginOffsetYMm = offsetY;
            Func<double[], double[]> map = point => new[]
            {
                point[0] * 1000.0 + offsetX,
                point[1] * 1000.0 + offsetY
            };

            WriteMarksIntoDxf(filePath, lines, encoding, marks, map, 1000.0, settings, result);
            return result;
        }

        /// <summary>Accepted size difference (mm) between an expected box and the DXF geometry.</summary>
        internal static double ExactSizeToleranceMm(double expectedWidth, double expectedHeight)
        {
            return ExactBoundsToleranceMm + 0.002 * Math.Max(expectedWidth, expectedHeight);
        }

        /// <summary>
        /// Bounding box (minX, minY, maxX, maxY, DXF units) of the geometry
        /// in an ASCII DXF, the same measure the injections use.
        /// </summary>
        internal static bool TryMeasureGeometryBounds(
            string filePath,
            out double[] bounds,
            out string reason)
        {
            bounds = null;
            List<string> lines;
            Encoding encoding;
            if (!TryLoadDxfLines(filePath, out lines, out encoding, out reason)) return false;

            DxfBounds measured;
            if (!TryGetGeometryBounds(lines, out measured, out reason)) return false;
            bounds = new[] { measured.MinX, measured.MinY, measured.MaxX, measured.MaxY };
            return true;
        }

        private static string FormatMm(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool TryLoadDxfLines(
            string filePath,
            out List<string> lines,
            out Encoding encoding,
            out string message)
        {
            lines = null;
            encoding = Encoding.GetEncoding(932);
            message = "";
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                message = "không thấy file DXF vừa xuất";
                return false;
            }

            byte[] header = new byte[Math.Min(22, (int)new FileInfo(filePath).Length)];
            using (var stream = File.OpenRead(filePath))
            {
                stream.Read(header, 0, header.Length);
            }
            if (Encoding.ASCII.GetString(header).StartsWith(
                    "AutoCAD Binary DXF",
                    StringComparison.OrdinalIgnoreCase))
            {
                message = "DXF nhị phân, không ghi thêm được";
                return false;
            }

            lines = File.ReadAllLines(filePath, encoding).ToList();
            if (lines.Count < 2 || lines.Count % 2 != 0)
            {
                message = "DXF không đủ cặp mã nhóm/giá trị";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Appends the marks to ENTITIES (already mapped by
        /// <paramref name="map"/> into DXF millimetres), adds the layer and
        /// saves the file. Fills <paramref name="result"/>.
        /// </summary>
        private static bool WriteMarksIntoDxf(
            string filePath,
            List<string> lines,
            Encoding encoding,
            IList<DxfInjectMark> marks,
            Func<double[], double[]> map,
            double k,
            DxfManufacturingLayerSettings settings,
            DxfMarkInjectionResult result,
            bool linesAlreadyChanged = false)
        {
            var handles = new DxfHandleAllocator(lines);
            if (marks != null && marks.Count > 0)
            {
                int insertAt = FindEntitiesEnd(lines);
                if (insertAt < 0)
                {
                    result.Message = "DXF không có section ENTITIES";
                    return false;
                }

                int version = GetAcadVersionNumber(lines);
                bool modern = version >= 1012;
                string owner = null;
                List<DxfRecord> existing = CollectEntityRecords(lines);
                if (existing.Count > 0)
                {
                    owner = GetGroupValue(
                        lines,
                        existing[0].Start,
                        existing[0].EndExclusive,
                        "330");
                }

                var added = new List<string>();
                foreach (DxfInjectMark mark in marks)
                {
                    int written = WriteMarkEntities(
                        mark,
                        map,
                        k,
                        settings.LaserMark.Name,
                        modern,
                        owner,
                        handles,
                        added);
                    if (written > 0)
                    {
                        result.MarksWritten++;
                        result.EntitiesWritten += written;
                    }
                }
                if (added.Count == 0 && !linesAlreadyChanged)
                {
                    result.Message = "không dựng được thực thể DXF nào từ sketch MARK";
                    return false;
                }
                lines.InsertRange(insertAt, added);
            }
            else if (!linesAlreadyChanged)
            {
                result.Message = "không có sketch MARK để ghi";
                return false;
            }

            handles.WriteHandleSeed(lines);
            EnsureLayerRecord(lines, settings.LaserMark.Name, settings.LaserMark.Color, handles);
            handles.WriteHandleSeed(lines);

            string tempPath = filePath + ".marks.tmp";
            try
            {
                File.WriteAllLines(tempPath, lines, encoding);
                File.Replace(tempPath, filePath, null);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }

            // Layer colour / true colour of LASER_MARK as configured.
            try { NormalizeMarkLayerOnly(filePath, settings, null); }
            catch (Exception ex)
            {
                result.Message = "đã ghi sketch MARK nhưng không chuẩn hóa được layer: " + ex.Message;
            }

            result.Applied = true;
            return true;
        }

        // ------------------------------------------------------------------
        // Bounding box of the exported geometry
        // ------------------------------------------------------------------
        private static bool TryGetGeometryBounds(
            List<string> lines,
            out DxfBounds bounds,
            out string reason)
        {
            bounds = new DxfBounds();
            reason = "";
            Dictionary<string, DxfBlockDefinition> blocks =
                CollectBlockDefinitions(lines);
            List<DxfRecord> records = CollectEntityRecords(lines);
            Func<DxfPoint2D, DxfPoint2D> identity = point => point;

            for (int index = 0; index < records.Count; index++)
            {
                DxfRecord record = records[index];
                string type = (record.Type ?? string.Empty).Trim().ToUpperInvariant();
                switch (type)
                {
                    case "INSERT":
                        if (!AddInsertBounds(lines, record, blocks, bounds, out reason))
                            return false;
                        break;
                    case "POLYLINE":
                        index = AddOldPolylineBounds(lines, records, index, bounds);
                        break;
                    case "VERTEX":
                    case "SEQEND":
                    case "TEXT":
                    case "MTEXT":
                    case "POINT":
                    case "HATCH":
                    case "DIMENSION":
                    case "ATTRIB":
                    case "ATTDEF":
                    case "LEADER":
                    case "MLEADER":
                    case "IMAGE":
                    case "SOLID":
                    case "3DFACE":
                        break;
                    default:
                        AddEntityBounds(lines, record, bounds, identity);
                        break;
                }
            }

            if (!bounds.HasData)
            {
                reason = "DXF không có đường nào";
                return false;
            }
            return true;
        }

        private static void AddEntityBounds(
            List<string> lines,
            DxfRecord record,
            DxfBounds bounds,
            Func<DxfPoint2D, DxfPoint2D> map)
        {
            List<List<DxfPoint2D>> polylines;
            if (!TryTessellateEntity(
                    lines,
                    record,
                    0.005,
                    double.PositiveInfinity,
                    out polylines))
                return;

            foreach (List<DxfPoint2D> polyline in polylines)
            {
                foreach (DxfPoint2D point in polyline)
                {
                    DxfPoint2D mapped = map(point);
                    bounds.Add(mapped.X, mapped.Y);
                }
            }
        }

        private static int AddOldPolylineBounds(
            List<string> lines,
            List<DxfRecord> records,
            int polylineIndex,
            DxfBounds bounds)
        {
            int index = polylineIndex + 1;
            while (index < records.Count && IsDxfType(records[index], "VERTEX"))
            {
                double x;
                double y;
                if (TryGetDoubleGroupValue(lines, records[index].Start, records[index].EndExclusive, "10", out x) &&
                    TryGetDoubleGroupValue(lines, records[index].Start, records[index].EndExclusive, "20", out y))
                    bounds.Add(x, y);
                index++;
            }
            if (index < records.Count && IsDxfType(records[index], "SEQEND")) index++;
            return index - 1;
        }

        private static bool AddInsertBounds(
            List<string> lines,
            DxfRecord insert,
            Dictionary<string, DxfBlockDefinition> blocks,
            DxfBounds bounds,
            out string reason)
        {
            reason = "";
            string name = GetGroupValue(lines, insert.Start, insert.EndExclusive, "2");
            DxfBlockDefinition definition;
            if (string.IsNullOrEmpty(name) || !blocks.TryGetValue(name, out definition))
            {
                reason = "INSERT trỏ tới block '" + name + "' không có trong file";
                return false;
            }

            double ix, iy;
            double sx = 1, sy = 1, rotation = 0;
            TryGetDoubleGroupValue(lines, insert.Start, insert.EndExclusive, "10", out ix);
            TryGetDoubleGroupValue(lines, insert.Start, insert.EndExclusive, "20", out iy);
            double parsed;
            if (TryGetDoubleGroupValue(lines, insert.Start, insert.EndExclusive, "41", out parsed)) sx = parsed;
            if (TryGetDoubleGroupValue(lines, insert.Start, insert.EndExclusive, "42", out parsed)) sy = parsed;
            if (TryGetDoubleGroupValue(lines, insert.Start, insert.EndExclusive, "50", out parsed)) rotation = parsed;
            double cos = Math.Cos(rotation * Math.PI / 180.0);
            double sin = Math.Sin(rotation * Math.PI / 180.0);
            double baseX = definition.BaseX;
            double baseY = definition.BaseY;
            Func<DxfPoint2D, DxfPoint2D> map = point =>
            {
                double x = sx * (point.X - baseX);
                double y = sy * (point.Y - baseY);
                return new DxfPoint2D
                {
                    X = x * cos - y * sin + ix,
                    Y = x * sin + y * cos + iy
                };
            };

            foreach (DxfRecord entity in definition.Entities)
            {
                string type = (entity.Type ?? string.Empty).Trim().ToUpperInvariant();
                if (type == "INSERT")
                {
                    reason = "block lồng nhau";
                    return false;
                }
                if (type == "TEXT" || type == "MTEXT" || type == "POINT" ||
                    type == "HATCH" || type == "DIMENSION" || type == "ATTRIB" ||
                    type == "ATTDEF" || type == "VERTEX" || type == "SEQEND" ||
                    type == "POLYLINE")
                    continue;
                AddEntityBounds(lines, entity, bounds, map);
            }
            return true;
        }

        private static Dictionary<string, DxfBlockDefinition> CollectBlockDefinitions(
            List<string> lines)
        {
            var blocks = new Dictionary<string, DxfBlockDefinition>(
                StringComparer.OrdinalIgnoreCase);
            DxfBlockDefinition current = null;
            foreach (DxfRecord record in CollectSectionRecords(lines, "BLOCKS"))
            {
                if (IsDxfType(record, "BLOCK"))
                {
                    string name = GetGroupValue(lines, record.Start, record.EndExclusive, "2");
                    current = new DxfBlockDefinition();
                    double x, y;
                    if (TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, "10", out x))
                        current.BaseX = x;
                    if (TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, "20", out y))
                        current.BaseY = y;
                    if (!string.IsNullOrEmpty(name)) blocks[name] = current;
                    continue;
                }
                if (IsDxfType(record, "ENDBLK"))
                {
                    current = null;
                    continue;
                }
                current?.Entities.Add(record);
            }
            return blocks;
        }

        private static List<DxfRecord> CollectSectionRecords(
            IList<string> lines,
            string sectionName)
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
                    if (section == sectionName && currentStart >= 0)
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
                if (section == sectionName && code == "0")
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

        private static int FindEntitiesEnd(List<string> lines)
        {
            string section = string.Empty;
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
                    if (section == "ENTITIES") return index;
                    section = string.Empty;
                }
            }
            return -1;
        }

        private static int GetAcadVersionNumber(List<string> lines)
        {
            for (int index = 0; index + 3 < lines.Count; index += 2)
            {
                if (lines[index].Trim() == "9" &&
                    string.Equals(lines[index + 1].Trim(), "$ACADVER", StringComparison.OrdinalIgnoreCase) &&
                    lines[index + 2].Trim() == "1")
                {
                    string value = lines[index + 3].Trim();
                    int number;
                    if (value.StartsWith("AC", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(value.Substring(2), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out number))
                        return number;
                }
                if (lines[index].Trim() == "0" && lines[index + 1].Trim() == "ENDSEC")
                    break;
            }
            return 1015;
        }

        // ------------------------------------------------------------------
        // Writing the marks
        // ------------------------------------------------------------------
        private static int WriteMarkEntities(
            DxfInjectMark mark,
            Func<double[], double[]> map,
            double scale,
            string layer,
            bool modern,
            string owner,
            DxfHandleAllocator handles,
            List<string> output)
        {
            if (mark?.Points == null || mark.Points.Count < 2) return 0;
            var mapped = mark.Points.Select(point => map(point)).ToList();
            string kind = (mark.Kind ?? string.Empty).Trim().ToUpperInvariant();
            int written = 0;

            switch (kind)
            {
                case "CIRCLE":
                {
                    double radius = Math.Sqrt(
                        Math.Pow(mapped[1][0] - mapped[0][0], 2) +
                        Math.Pow(mapped[1][1] - mapped[0][1], 2));
                    if (radius <= 1e-9) return 0;
                    AddEntity(output, handles, "CIRCLE", layer, modern, owner,
                        "AcDbCircle",
                        new[]
                        {
                            Pair(10, mapped[0][0]), Pair(20, mapped[0][1]), Pair(30, 0.0),
                            Pair(40, radius)
                        });
                    return 1;
                }
                case "ARC":
                {
                    if (mapped.Count >= 3 &&
                        TryBuildArc(mapped[0], mapped[1], mapped[2],
                            out double cx, out double cy, out double radius,
                            out double a0, out double a1))
                    {
                        AddEntity(output, handles, "ARC", layer, modern, owner,
                            "AcDbCircle",
                            new[]
                            {
                                Pair(10, cx), Pair(20, cy), Pair(30, 0.0), Pair(40, radius)
                            },
                            "AcDbArc",
                            new[] { Pair(50, a0), Pair(51, a1) });
                        return 1;
                    }
                    // Nearly straight: the chord through the middle point.
                    var chord = new List<double[]> { mapped[0] };
                    if (mapped.Count >= 3) chord.Add(mapped[2]);
                    chord.Add(mapped[1]);
                    return WriteLines(chord, layer, modern, owner, handles, output);
                }
                case "LINE":
                    return WriteLines(mapped, layer, modern, owner, handles, output);
                default:
                {
                    var points = new List<double[]>();
                    foreach (double[] point in mapped)
                    {
                        if (points.Count == 0 ||
                            Math.Abs(point[0] - points[points.Count - 1][0]) > 1e-9 ||
                            Math.Abs(point[1] - points[points.Count - 1][1]) > 1e-9)
                            points.Add(point);
                    }
                    if (points.Count < 2) return 0;
                    if (!modern) return WriteLines(points, layer, modern, owner, handles, output);

                    bool closed = points.Count > 2 &&
                        Math.Abs(points[0][0] - points[points.Count - 1][0]) <= 1e-6 &&
                        Math.Abs(points[0][1] - points[points.Count - 1][1]) <= 1e-6;
                    if (closed) points.RemoveAt(points.Count - 1);

                    var body = new List<string>();
                    body.AddRange(Pair(90, points.Count.ToString(CultureInfo.InvariantCulture)));
                    body.AddRange(Pair(70, closed ? "1" : "0"));
                    foreach (double[] point in points)
                    {
                        body.AddRange(Pair(10, point[0]));
                        body.AddRange(Pair(20, point[1]));
                    }
                    AddEntity(output, handles, "LWPOLYLINE", layer, modern, owner,
                        "AcDbPolyline", body.ToArray());
                    written = 1;
                    return written;
                }
            }
        }

        private static int WriteLines(
            IList<double[]> points,
            string layer,
            bool modern,
            string owner,
            DxfHandleAllocator handles,
            List<string> output)
        {
            int written = 0;
            for (int index = 1; index < points.Count; index++)
            {
                double[] a = points[index - 1];
                double[] b = points[index];
                if (Math.Abs(a[0] - b[0]) <= 1e-9 && Math.Abs(a[1] - b[1]) <= 1e-9)
                    continue;
                AddEntity(output, handles, "LINE", layer, modern, owner,
                    "AcDbLine",
                    new[]
                    {
                        Pair(10, a[0]), Pair(20, a[1]), Pair(30, 0.0),
                        Pair(11, b[0]), Pair(21, b[1]), Pair(31, 0.0)
                    }.SelectMany(x => x).ToArray());
                written++;
            }
            return written;
        }

        private static string[] Pair(int code, double value)
        {
            return new[]
            {
                code.ToString(CultureInfo.InvariantCulture),
                FormatDxfNumber(value)
            };
        }

        private static string[] Pair(int code, string value)
        {
            return new[] { code.ToString(CultureInfo.InvariantCulture), value };
        }

        private static void AddEntity(
            List<string> output,
            DxfHandleAllocator handles,
            string type,
            string layer,
            bool modern,
            string owner,
            string subclass,
            IEnumerable<string[]> body,
            string secondSubclass = null,
            IEnumerable<string[]> secondBody = null)
        {
            AddEntity(
                output, handles, type, layer, modern, owner, subclass,
                body.SelectMany(pair => pair).ToArray(),
                secondSubclass,
                secondBody?.SelectMany(pair => pair).ToArray());
        }

        private static void AddEntity(
            List<string> output,
            DxfHandleAllocator handles,
            string type,
            string layer,
            bool modern,
            string owner,
            string subclass,
            string[] body,
            string secondSubclass = null,
            string[] secondBody = null)
        {
            var record = new List<string> { "0", type };
            if (modern)
            {
                record.Add("5");
                record.Add("0");
                if (!string.IsNullOrEmpty(owner))
                {
                    record.Add("330");
                    record.Add(owner);
                }
                record.Add("100");
                record.Add("AcDbEntity");
            }
            record.Add("8");
            record.Add(layer);
            if (modern)
            {
                record.Add("100");
                record.Add(subclass);
            }
            record.AddRange(body);
            if (modern && secondSubclass != null)
            {
                record.Add("100");
                record.Add(secondSubclass);
            }
            if (secondBody != null) record.AddRange(secondBody);

            if (modern) handles.AssignNewHandle(record);
            output.AddRange(record);
        }

        /// <summary>
        /// Circle through three points -> DXF arc (centre, radius, start and
        /// end angle in degrees, counter-clockwise from start to end).
        /// </summary>
        private static bool TryBuildArc(
            double[] start,
            double[] end,
            double[] through,
            out double centerX,
            out double centerY,
            out double radius,
            out double startAngle,
            out double endAngle)
        {
            centerX = centerY = radius = startAngle = endAngle = 0;
            double ax = start[0], ay = start[1];
            double bx = through[0], by = through[1];
            double cx = end[0], cy = end[1];
            double determinant =
                2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            double chord = Math.Max(
                Math.Sqrt((cx - ax) * (cx - ax) + (cy - ay) * (cy - ay)),
                1e-9);
            if (Math.Abs(determinant) <= 1e-9 * chord * chord) return false;

            double a2 = ax * ax + ay * ay;
            double b2 = bx * bx + by * by;
            double c2 = cx * cx + cy * cy;
            centerX = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / determinant;
            centerY = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / determinant;
            radius = Math.Sqrt((ax - centerX) * (ax - centerX) + (ay - centerY) * (ay - centerY));
            if (radius <= 1e-9 || radius > 1e4 * chord) return false;

            // start -> through -> end counter-clockwise?
            bool counterClockwise =
                (bx - ax) * (cy - ay) - (by - ay) * (cx - ax) > 0;
            double from = Math.Atan2(ay - centerY, ax - centerX) * 180.0 / Math.PI;
            double to = Math.Atan2(cy - centerY, cx - centerX) * 180.0 / Math.PI;
            startAngle = NormalizeDegrees(counterClockwise ? from : to);
            endAngle = NormalizeDegrees(counterClockwise ? to : from);
            return true;
        }

        private static double NormalizeDegrees(double degrees)
        {
            degrees %= 360.0;
            if (degrees < 0) degrees += 360.0;
            return degrees;
        }

        /// <summary>
        /// Adds a LAYER record for <paramref name="name"/> (copied from an
        /// existing record) when the table does not have it yet.
        /// </summary>
        private static void EnsureLayerRecord(
            List<string> lines,
            string name,
            int colorIndex,
            DxfHandleAllocator handles)
        {
            List<DxfRecord> records = CollectLayerRecords(lines);
            if (records.Count == 0) return;
            if (records.Any(record => string.Equals(
                    record.Type, name, StringComparison.OrdinalIgnoreCase)))
                return;

            DxfRecord template = records.FirstOrDefault(record =>
                !string.Equals(record.Type, "0", StringComparison.OrdinalIgnoreCase)) ??
                records[0];
            List<string> clone = lines.GetRange(
                template.Start,
                template.EndExclusive - template.Start);
            SetGroupValue(clone, 0, clone.Count, "2", name);
            if (GetGroupValue(clone, 0, clone.Count, "62") != null)
            {
                SetGroupValue(
                    clone, 0, clone.Count, "62",
                    Math.Max(1, Math.Min(255, colorIndex)).ToString(CultureInfo.InvariantCulture));
            }
            handles.AssignNewHandle(clone);
            lines.InsertRange(records[records.Count - 1].EndExclusive, clone);
            UpdateLayerTableDeclaredCount(lines);
        }
    }
}
