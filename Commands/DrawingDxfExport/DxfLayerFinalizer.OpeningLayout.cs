using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ADDIN.Commands.DrawingDxfExporting
{
    internal sealed class DxfOpeningLayoutResult
    {
        public bool Changed;
        public int InformationEntities, ActiveViewports;
        public double[] GeometryBounds, ContentBounds;
        public double ViewCenterX, ViewCenterY, ViewHeight, AspectRatio;
    }

    internal static partial class DxfLayerFinalizer
    {
        // This pass only moves information text and changes the saved view. CUT/MARK/BEND
        // coordinates, units, handles and entity types never change here.
        internal static DxfOpeningLayoutResult PrepareOpeningLayout(string file, DxfManufacturingLayerSettings settings = null)
        {
            var encoding = Encoding.GetEncoding(932);
            var lines = File.ReadAllLines(file, encoding).ToList();
            var result = ApplyOpeningLayout(lines, DxfManufacturingLayerSettings.Normalize(settings));
            if (result.Changed)
            {
                string temporary = file + ".view.tmp";
                try { File.WriteAllLines(temporary, lines, encoding); File.Replace(temporary, file, null); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return result;
        }

        private static DxfOpeningLayoutResult ApplyOpeningLayout(List<string> lines, DxfManufacturingLayerSettings settings)
        {
            var result = new DxfOpeningLayoutResult();
            var geometry = new DxfBounds();
            foreach (var entity in SampleEntities(lines))
                if (!entity.IsText && !IsLayer(entity.Layer, settings.PartInfo.Name))
                    foreach (var point in entity.Points) geometry.Add(point.X, point.Y);
            if (!geometry.HasData) throw new InvalidDataException("DXF không có biên dạng để căn khung nhìn.");
            result.GeometryBounds = LayoutBoundsArray(geometry);
            var content = new DxfBounds();
            content.Add(geometry.MinX, geometry.MinY); content.Add(geometry.MaxX, geometry.MaxY);
            var info = CollectEntityRecords(lines).Where(r =>
                (IsDxfType(r, "TEXT") || IsDxfType(r, "MTEXT")) &&
                IsLayer(GetGroupValue(lines, r.Start, r.EndExclusive, "8"), settings.PartInfo.Name) &&
                LayoutNumber(lines, r, "67", 0) == 0).ToList();
            double height = info.Count == 0 ? 0 : info.Max(r => LayoutNumber(lines, r, "40", 70));
            double gap = Math.Max(10, height * .8);
            double top = geometry.MinY - gap;
            var placements = new List<Tuple<DxfRecord, double, double, double, double>>();
            // Preserve the original information order; one row can contain MTEXT paragraphs.
            foreach (var record in info.OrderByDescending(r => LayoutNumber(lines, r, "20", 0)).ThenBy(r => r.Start))
            {
                double textHeight = Math.Max(.1, LayoutNumber(lines, record, "40", 70));
                string rawText = LayoutText(lines, record);
                string text = LayoutPlainText(rawText);
                string[] rows = text.Replace("\r", "").Split('\n');
                double width = rows.Max(row => LayoutTextWidth(row, textHeight, settings.PartInfoFontName));
                // Include descenders and font substitution in the full drawing extents.
                double blockHeight = textHeight * 1.67 * Math.Max(1, LayoutNumber(lines, record, "44", 1)) * Math.Max(1, rows.Length);
                double x = geometry.MinX;
                placements.Add(Tuple.Create(record, x, top, textHeight, width));
                content.Add(x - textHeight * .12, top + textHeight * .12);
                content.Add(x + width + textHeight * .25, top - blockHeight);
                top -= blockHeight + textHeight * .25;
            }
            // Insert missing group pairs from back to front so stored record offsets remain valid.
            foreach (var placement in placements.OrderByDescending(p => p.Item1.Start))
            {
                var record = placement.Item1;
                double y = IsDxfType(record, "MTEXT") ? placement.Item3 : placement.Item3 - placement.Item4;
                result.Changed |= SetLayoutGroup(lines, record, "10", FormatDxfNumber(placement.Item2));
                result.Changed |= SetLayoutGroup(lines, record, "20", FormatDxfNumber(y));
                result.Changed |= SetLayoutGroup(lines, record, "30", "0.0");
                if (IsDxfType(record, "MTEXT"))
                {
                    result.Changed |= SetLayoutGroup(lines, record, "71", "1"); // top-left
                    // An explicit generous width prevents CAD readers from treating width=0
                    // as a narrow default column and wrapping otherwise single-line notes.
                    result.Changed |= SetLayoutGroup(lines, record, "41", FormatDxfNumber(placement.Item5));
                    result.Changed |= SetLayoutGroup(lines, record, "11", "1.0");
                    result.Changed |= SetLayoutGroup(lines, record, "21", "0.0");
                    result.Changed |= SetLayoutGroup(lines, record, "31", "0.0");
                    result.Changed |= SetLayoutGroup(lines, record, "50", "0.0");
                }
                else
                {
                    result.Changed |= SetLayoutGroup(lines, record, "72", "0");
                    result.Changed |= SetLayoutGroup(lines, record, "73", "0");
                    result.Changed |= SetLayoutGroup(lines, record, "50", "0.0");
                    // Reset any prior aligned/fit TEXT endpoint together with insertion.
                    if (GetGroupValue(lines, record.Start, record.EndExclusive, "11") != null)
                    {
                        result.Changed |= SetLayoutGroup(lines, record, "11", FormatDxfNumber(placement.Item2));
                        result.Changed |= SetLayoutGroup(lines, record, "21", FormatDxfNumber(y));
                    }
                }
            }
            result.InformationEntities = info.Count;
            result.ContentBounds = LayoutBoundsArray(content);
            var ports = LayoutRecordsInSection(lines, "TABLES", "VPORT")
                .Where(r => string.Equals(GetGroupValue(lines, r.Start, r.EndExclusive, "2"), "*ACTIVE", StringComparison.OrdinalIgnoreCase)).ToList();
            double aspect = ports.Count > 0 ? LayoutNumber(lines, ports[0], "41", 1.8) : 1.8;
            if (double.IsNaN(aspect) || double.IsInfinity(aspect) || aspect <= 0) aspect = 1.8;
            // Cap the saved screen ratio to leave room in CAD windows with side panels.
            aspect = Math.Max(1.0, Math.Min(1.8, aspect));
            // Opening zoom follows the part geometry only. Information remains
            // below the part, but its width/height must never shrink or shift this view.
            // EXTMIN/EXTMAX still describe all content for normal CAD Zoom Extents.
            double widthOfGeometry = Math.Max(1, geometry.MaxX - geometry.MinX);
            double heightOfGeometry = Math.Max(1, geometry.MaxY - geometry.MinY);
            double viewHeight = Math.Max(heightOfGeometry, widthOfGeometry / aspect) * 1.2;
            double centerX = (geometry.MinX + geometry.MaxX) * .5;
            double centerY = (geometry.MinY + geometry.MaxY) * .5;
            result.ViewCenterX = centerX; result.ViewCenterY = centerY;
            result.ViewHeight = viewHeight; result.AspectRatio = aspect;
            foreach (var port in ports.OrderByDescending(r => r.Start))
            {
                result.Changed |= SetLayoutGroup(lines, port, "12", FormatDxfNumber(centerX));
                result.Changed |= SetLayoutGroup(lines, port, "22", FormatDxfNumber(centerY));
                result.Changed |= SetLayoutGroup(lines, port, "40", FormatDxfNumber(viewHeight));
                result.Changed |= SetLayoutGroup(lines, port, "41", FormatDxfNumber(aspect));
                foreach (var pair in new[] { Tuple.Create("16","0.0"), Tuple.Create("26","0.0"), Tuple.Create("36","1.0"),
                    Tuple.Create("17","0.0"), Tuple.Create("27","0.0"), Tuple.Create("37","0.0"), Tuple.Create("51","0.0"), Tuple.Create("71","0") })
                    result.Changed |= SetLayoutGroup(lines, port, pair.Item1, pair.Item2);
            }
            result.ActiveViewports = ports.Count;
            // R11 and later use the *ACTIVE VPORT table. Do not emit the obsolete
            // $VIEWCTR/$VIEWSIZE/$VIEWDIR header variables in modern DXF files.
            string version = GetLayoutHeaderValue(lines, "$ACADVER");
            if (version != null && string.CompareOrdinal(version, "AC1006") <= 0)
            {
                result.Changed |= SetLayoutHeader(lines, "$VIEWCTR", "10", FormatDxfNumber(centerX), "20", FormatDxfNumber(centerY));
                result.Changed |= SetLayoutHeader(lines, "$VIEWSIZE", "40", FormatDxfNumber(viewHeight));
                result.Changed |= SetLayoutHeader(lines, "$VIEWDIR", "10", "0.0", "20", "0.0", "30", "1.0");
            }
            result.Changed |= SetLayoutHeader(lines, "$EXTMIN", "10", FormatDxfNumber(content.MinX), "20", FormatDxfNumber(content.MinY), "30", "0.0");
            result.Changed |= SetLayoutHeader(lines, "$EXTMAX", "10", FormatDxfNumber(content.MaxX), "20", FormatDxfNumber(content.MaxY), "30", "0.0");
            result.Changed |= SetLayoutHeader(lines, "$LIMMIN", "10", FormatDxfNumber(centerX - viewHeight * aspect * .5), "20", FormatDxfNumber(centerY - viewHeight * .5));
            result.Changed |= SetLayoutHeader(lines, "$LIMMAX", "10", FormatDxfNumber(centerX + viewHeight * aspect * .5), "20", FormatDxfNumber(centerY + viewHeight * .5));
            result.Changed |= SetLayoutHeader(lines, "$TILEMODE", "70", "1");
            return result;
        }

        private static double[] LayoutBoundsArray(DxfBounds bounds) => new[] { bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY };
        private static double LayoutNumber(IList<string> lines, DxfRecord record, string code, double fallback)
        {
            return double.TryParse(GetGroupValue(lines, record.Start, record.EndExclusive, code), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
                !double.IsNaN(value) && !double.IsInfinity(value) ? value : fallback;
        }
        private static string LayoutText(IList<string> lines, DxfRecord record)
        {
            var result = new StringBuilder();
            for (int i = record.Start; i + 1 < record.EndExclusive; i += 2)
                if (lines[i].Trim() == "1" || lines[i].Trim() == "3") result.Append(lines[i + 1]);
            return result.ToString();
        }
        private static string LayoutPlainText(string text)
        {
            text = text.Replace("\\P", "\n").Replace("\\~", " ");
            text = Regex.Replace(text, @"\\[A-Za-z][^;]*;", "");
            return text.Replace("{", "").Replace("}", "");
        }
        private static double LayoutTextWidth(string text, double height, string family)
        {
            try
            {
                using (var bitmap = new Bitmap(1, 1))
                using (var graphics = Graphics.FromImage(bitmap))
                using (var font = new Font(family, 100, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
                using (var glyph = new GraphicsPath())
                {
                    format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                    // DXF height is capital-letter height; GDI font size is the em square.
                    glyph.AddString("H", font.FontFamily, (int)FontStyle.Regular, 100, PointF.Empty, format);
                    double capitalHeight = Math.Max(1, glyph.GetBounds().Height);
                    return Math.Max(height, graphics.MeasureString(text, font, int.MaxValue, format).Width * height / capitalHeight * 1.2);
                }
            }
            catch { return Math.Max(height, text.Length * height * 1.2); }
        }
        private static bool SetLayoutGroup(List<string> lines, DxfRecord record, string code, string value)
        {
            for (int i = record.Start; i + 1 < record.EndExclusive; i += 2)
                if (lines[i].Trim() == code)
                { if (lines[i + 1].Trim() == value) return false; lines[i + 1] = value; return true; }
            // Standard entity groups must precede XDATA application records.
            int insert = record.EndExclusive;
            for (int i = record.Start; i + 1 < record.EndExclusive; i += 2)
                if (lines[i].Trim() == "1001") { insert = i; break; }
            lines.InsertRange(insert, new[] { code, value }); record.EndExclusive += 2; return true;
        }
        private static List<DxfRecord> LayoutRecordsInSection(IList<string> lines, string wantedSection, string wantedType)
        {
            string section = ""; var result = new List<DxfRecord>();
            for (int i = 0; i + 1 < lines.Count; i += 2)
            {
                if (lines[i].Trim() != "0") continue;
                string kind = lines[i + 1].Trim();
                if (kind == "SECTION" && i + 3 < lines.Count) { section = lines[i + 3].Trim(); continue; }
                if (kind == "ENDSEC") { section = ""; continue; }
                if (section != wantedSection || kind != wantedType) continue;
                int end = i + 2; while (end + 1 < lines.Count && lines[end].Trim() != "0") end += 2;
                result.Add(new DxfRecord { Start = i, EndExclusive = end, Type = kind });
            }
            return result;
        }
        private static bool SetLayoutHeader(List<string> lines, string variable, params string[] pairs)
        {
            int start = -1, end = -1;
            for (int i = 0; i + 3 < lines.Count; i += 2)
                if (lines[i].Trim() == "0" && lines[i + 1].Trim() == "SECTION" && lines[i + 3].Trim() == "HEADER")
                {
                    start = i + 4; end = start;
                    while (end + 1 < lines.Count && !(lines[end].Trim() == "0" && lines[end + 1].Trim() == "ENDSEC")) end += 2;
                    break;
                }
            if (start < 0)
            {
                var header = new List<string> { "0", "SECTION", "2", "HEADER", "9", variable };
                header.AddRange(pairs); header.AddRange(new[] { "0", "ENDSEC" }); lines.InsertRange(0, header); return true;
            }
            for (int i = start; i + 1 < end; i += 2)
                if (lines[i].Trim() == "9" && lines[i + 1].Trim() == variable)
                {
                    int stop = i + 2; while (stop < end && lines[stop].Trim() != "9" && lines[stop].Trim() != "0") stop += 2;
                    if (lines.Skip(i + 2).Take(stop - i - 2).Select(s => s.Trim()).SequenceEqual(pairs)) return false;
                    lines.RemoveRange(i + 2, stop - i - 2); lines.InsertRange(i + 2, pairs); return true;
                }
            lines.InsertRange(end, new[] { "9", variable }.Concat(pairs)); return true;
        }
        private static string GetLayoutHeaderValue(IList<string> lines, string variable)
        {
            for (int i = 0; i + 3 < lines.Count; i += 2)
                if (lines[i].Trim() == "9" && lines[i + 1].Trim() == variable)
                    return lines[i + 3].Trim();
            return null;
        }
    }
}
