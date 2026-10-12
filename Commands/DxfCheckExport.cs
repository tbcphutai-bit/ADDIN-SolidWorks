using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Threading;

namespace ADDIN.Commands.DxfChecking
{
    internal static partial class DxfCheckExport
    {
        public const string MarkerLayer = "CHECK_DXF_NG";
        private sealed class Edit { public int Start, Length; public string Text; }
        private static readonly Regex LegacyLabel = new Regex(
            @"^NG[1-9][0-9]*\s+(POLYLINE|SPLINE|OVERLAP|ROUND_DEFORMED|ELLIPSE|SHORT_EDGE|HOLE_INTERSECTION|OPEN_CONTOUR_[A-Z_]+|CONTOUR_JOIN_UNTRIMMED|ROUND_HOLE_TO_EDGE|SPLINE_ROUND_HOLE)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static HashSet<DxfRecord> FindAnnotations(DxfRawDocument doc)
        {
            HashSet<DxfRecord> marks = new HashSet<DxfRecord>();
            Dictionary<DxfRecord, string> scopes = new Dictionary<DxfRecord, string>();
            string block = "";
            foreach (DxfRecord record in doc.Records) {
                if (record.Type == "BLOCK") block = record.Text(2) + "@" + record.Pairs[0].Start;
                if (IsDrawingEntity(record)) {
                    scopes[record] = record.Section == "BLOCKS" ? block
                        : record.Text(410, record.Number(67) == 1 ? "PAPER" : "MODEL") + "|" + record.Text(330);
                    if (string.Equals(doc.Decode(record.Text(8)), MarkerLayer, StringComparison.OrdinalIgnoreCase))
                        AddAnnotation(doc, marks, record);
                }
                if (record.Type == "ENDBLK") block = "";
            }
            foreach (DxfRecord label in scopes.Keys.Where(r => IsLegacyLabel(doc, r)).ToList()) {
                AddAnnotation(doc, marks, label);
                // Old CAD saves can move an NG label and its rectangle to a
                // different layer. Match the actual generated rectangle, not
                // arbitrary red cutting edges.
                DxfPoint origin = label.Point();
                double top = origin.Y - .3;
                List<DxfRecord> nearby = scopes.Keys.Where(r => scopes[r] == scopes[label] && IsRed(doc, r)).ToList();
                foreach (DxfRecord frame in nearby.Where(r => !marks.Contains(r) && (r.Type == "POLYLINE" || r.Type == "LWPOLYLINE"))) {
                    if (((int)frame.Number(70) & 1) == 0) continue;
                    if (frame.Type == "POLYLINE" && ((int)frame.Number(70) & (8 | 16 | 32 | 64)) != 0) continue;
                    List<DxfEdge> edges = DxfGeometry.ReadEdges(frame, DxfTransform.Identity);
                    if (edges.Count != 4 || edges.Any(e => e.Type != "LINE")) continue;
                    DxfBox box = DxfBox.From(edges.SelectMany(e => new[] { e.Start, e.End }));
                    if (!Near(box.MinX, origin.X) || !Near(box.MaxY, top) || box.Width <= 0 || box.MaxY <= box.MinY) continue;
                    DxfPoint[] corners = { new DxfPoint(box.MinX, box.MinY), new DxfPoint(box.MaxX, box.MinY),
                        new DxfPoint(box.MaxX, box.MaxY), new DxfPoint(box.MinX, box.MaxY) };
                    if (edges.All(e => (Near(e.Start.X, e.End.X) != Near(e.Start.Y, e.End.Y))
                        && corners.Any(p => p.Distance(e.Start) <= DxfGeometry.Epsilon)
                        && corners.Any(p => p.Distance(e.End) <= DxfGeometry.Epsilon)))
                        AddAnnotation(doc, marks, frame);
                }
                List<DxfRecord> lines = nearby.Where(r => r.Type == "LINE").ToList();
                foreach (DxfRecord upper in lines.Where(r => Near(r.Point().Y, top) && Near(r.Point(11).Y, top))) {
                    double right = Math.Max(upper.Point().X, upper.Point(11).X);
                    if (!Near(Math.Min(upper.Point().X, upper.Point(11).X), origin.X) || right <= origin.X) continue;
                    foreach (DxfRecord left in lines.Where(r => Near(r.Point().X, origin.X) && Near(r.Point(11).X, origin.X)
                        && Near(Math.Max(r.Point().Y, r.Point(11).Y), top))) {
                        double bottom = Math.Min(left.Point().Y, left.Point(11).Y);
                        if (bottom >= top) continue;
                        DxfRecord lower = lines.FirstOrDefault(r => SameSegment(r, new DxfPoint(origin.X, bottom), new DxfPoint(right, bottom)));
                        DxfRecord end = lines.FirstOrDefault(r => SameSegment(r, new DxfPoint(right, bottom), new DxfPoint(right, top)));
                        if (lower == null || end == null) continue;
                        foreach (DxfRecord side in new[] { upper, left, lower, end }) AddAnnotation(doc, marks, side);
                    }
                }
            }
            return marks;
        }

        private static bool IsDrawingEntity(DxfRecord record)
        {
            return (record.Section == "ENTITIES" || record.Section == "BLOCKS")
                && record.Type != "SECTION" && record.Type != "ENDSEC" && record.Type != "EOF"
                && record.Type != "BLOCK" && record.Type != "ENDBLK";
        }
        private static void AddAnnotation(DxfRawDocument doc, HashSet<DxfRecord> marks, DxfRecord record)
        {
            if (!marks.Add(record)) return;
            foreach (DxfRecord child in record.Children) AddAnnotation(doc, marks, child);
            if (record.Type == "INSERT" && record.Number(66) == 1) {
                foreach (DxfRecord child in doc.Records.Skip(doc.Records.IndexOf(record) + 1)) {
                    if (child.Type != "ATTRIB" && child.Type != "SEQEND") break;
                    marks.Add(child);
                    if (child.Type == "SEQEND") break;
                }
            }
        }
        private static bool IsLegacyLabel(DxfRawDocument doc, DxfRecord record)
        {
            if (record.Type != "TEXT" && record.Type != "MTEXT" && record.Type != "ATTRIB" && record.Type != "ATTDEF") return false;
            string raw = doc.Decode(string.Concat(record.Pairs.Where(p => p.Code == 1 || p.Code == 3).Select(p => p.Value)));
            bool inlineRed = raw.Contains(@"\C1;") || raw.Contains(@"\c16711680;");
            string text = raw.Replace(@"\P", " ").Replace(@"\~", " ");
            text = Regex.Replace(text, @"\\[A-Za-z][^;]*;", "").Replace("{", "").Replace("}", "").Trim();
            return (inlineRed || IsRed(doc, record)) && LegacyLabel.IsMatch(text);
        }
        private static bool IsRed(DxfRawDocument doc, DxfRecord record)
        {
            string rgb = record.Text(420, null);
            if (rgb != null) return rgb == "16711680";
            int color = (int)record.Number(62, 256);
            if (Math.Abs(color) == 1) return true;
            DxfLayerInfo layer;
            return color == 256 && doc.Layers.TryGetValue(doc.Decode(record.Text(8, "0")), out layer)
                && (layer.Color.Rgb.HasValue ? layer.Color.Rgb.Value == 0xFF0000 : Math.Abs(layer.Color.Aci) == 1);
        }
        private static bool Near(double a, double b) { return Math.Abs(a - b) <= DxfGeometry.Epsilon; }
        private static bool SameSegment(DxfRecord line, DxfPoint a, DxfPoint b)
        {
            return (line.Point().Distance(a) <= DxfGeometry.Epsilon && line.Point(11).Distance(b) <= DxfGeometry.Epsilon)
                || (line.Point().Distance(b) <= DxfGeometry.Epsilon && line.Point(11).Distance(a) <= DxfGeometry.Epsilon);
        }

        public static void WriteAnnotated(DxfRawDocument doc, DxfFileResult result)
        {
            List<Edit> edits = new List<Edit>();
            ulong handle = 0;
            foreach (DxfPair p in doc.Pairs.Where(p => p.Code == 5 || p.Code == 105))
            { ulong parsed; if (ulong.TryParse(p.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed)) handle = Math.Max(handle, parsed); }
            Func<string> next = () => { handle = checked(handle + 1); return handle.ToString("X"); };
            bool modern = string.CompareOrdinal(doc.Version, "AC1009") > 0;
            foreach (DxfRecord old in FindAnnotations(doc))
                edits.Add(new Edit { Start = old.Pairs[0].Start, Length = old.Pairs.Last().End - old.Pairs[0].Start, Text = "" });
            StringBuilder annotations = new StringBuilder();
            if (result.Issues.Count > 0)
            {
                DxfRecord existing = doc.Records.FirstOrDefault(r => r.Type == "LAYER" && r.Text(2) == MarkerLayer);
                if (existing == null)
                {
                    DxfRecord table = doc.Records.FirstOrDefault(r => r.Type == "TABLE" && r.Text(2) == "LAYER");
                    string owner = table == null ? next() : table.Text(5);
                    string layer = Pair(doc, 0, "LAYER") + (modern ? Pair(doc, 5, next()) + (owner.Length > 0 ? Pair(doc, 330, owner) : "") + Pair(doc, 100, "AcDbSymbolTableRecord") + Pair(doc, 100, "AcDbLayerTableRecord") : "") + Pair(doc, 2, MarkerLayer) + Pair(doc, 70, "0") + Pair(doc, 62, "1") + Pair(doc, 6, "CONTINUOUS");
                    if (table != null)
                    {
                        int index = doc.Records.IndexOf(table);
                        DxfRecord end = doc.Records.Skip(index + 1).First(r => r.Type == "ENDTAB");
                        edits.Add(new Edit { Start = end.Pairs[0].Start, Text = layer });
                        DxfPair count = table.Pairs.FirstOrDefault(p => p.Code == 70);
                        if (count != null) Replace(edits, count, (doc.Records.Skip(index + 1).TakeWhile(r => r != end).Count(r => r.Type == "LAYER") + 1).ToString());
                    }
                    else
                    {
                        string text = Pair(doc, 0, "TABLE") + Pair(doc, 2, "LAYER") + (modern ? Pair(doc, 5, owner) + Pair(doc, 100, "AcDbSymbolTable") : "") + Pair(doc, 70, "1") + layer + Pair(doc, 0, "ENDTAB");
                        DxfRecord tableSection = doc.Records.FirstOrDefault(r => r.Type == "SECTION" && r.Section == "TABLES");
                        if (tableSection != null)
                        {
                            DxfRecord end = doc.Records.Skip(doc.Records.IndexOf(tableSection) + 1).First(r => r.Type == "ENDSEC");
                            edits.Add(new Edit { Start = end.Pairs[0].Start, Text = text });
                        }
                        else
                        {
                            DxfRecord before = doc.Records.First(r => r.Type == "SECTION" && (r.Section == "BLOCKS" || r.Section == "ENTITIES"));
                            edits.Add(new Edit { Start = before.Pairs[0].Start, Text = Pair(doc, 0, "SECTION") + Pair(doc, 2, "TABLES") + text + Pair(doc, 0, "ENDSEC") });
                        }
                    }
                }
                else
                {
                    foreach (DxfPair p in existing.Pairs.Where(p => p.Code == 70 || p.Code == 62)) Replace(edits, p, p.Code == 62 ? "1" : "0");
                    foreach (DxfPair p in existing.Pairs.Where(p => p.Code == 420 || p.Code == 430)) edits.Add(new Edit { Start = p.Start, Length = p.End - p.Start, Text = "" });
                }
                foreach (DxfIssue issue in result.Issues)
                {
                    DxfBox box = issue.Bounds;
                    double pad = Math.Max(.8, Math.Min(4, box.Size * .025));
                    double x1 = box.MinX - pad, y1 = box.MinY - pad, x2 = box.MaxX + pad, y2 = box.MaxY + pad;
                    DxfPoint[] corners = { new DxfPoint(x1, y1), new DxfPoint(x2, y1), new DxfPoint(x2, y2), new DxfPoint(x1, y2) };
                    for (int i = 0; i < 4; i++)
                    {
                        DxfPoint a = corners[i], b = corners[(i + 1) % 4];
                        annotations.Append(EntityHeader(doc, next(), "LINE", "AcDbLine", issue.Space, modern));
                        annotations.Append(PointPairs(doc, a, 10)); annotations.Append(PointPairs(doc, b, 11));
                    }
                    annotations.Append(EntityHeader(doc, next(), "TEXT", "AcDbText", issue.Space, modern));
                    annotations.Append(PointPairs(doc, new DxfPoint(x1, y2 + .3), 10));
                    annotations.Append(Pair(doc, 40, Math.Max(.8, Math.Min(3, pad)).ToString("R", CultureInfo.InvariantCulture)));
                    annotations.Append(Pair(doc, 1, "NG" + issue.Id + " " + issue.Code));
                    annotations.Append(Pair(doc, 50, "0")); annotations.Append(Pair(doc, 7, "STANDARD"));
                    if (modern) annotations.Append(Pair(doc, 100, "AcDbText"));
                }
                edits.Add(new Edit { Start = doc.EntitiesEnd.Start, Text = annotations.ToString() });
            }
            for (int i = 0; i < doc.Pairs.Count - 1; i++) if (doc.Pairs[i].Code == 9 && doc.Pairs[i].Value == "$HANDSEED") { Replace(edits, doc.Pairs[i + 1], checked(handle + 1).ToString("X")); break; }
            StringBuilder output = new StringBuilder(doc.Raw);
            foreach (Edit edit in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length)) { if (edit.Length > 0) output.Remove(edit.Start, edit.Length); if (!string.IsNullOrEmpty(edit.Text)) output.Insert(edit.Start, edit.Text); }
            Directory.CreateDirectory(Path.GetDirectoryName(result.OutputPath));
            Atomic(result.OutputPath, temp => File.WriteAllBytes(temp, Encoding.GetEncoding(28591).GetBytes(output.ToString())));
        }

        public static void WriteCleanCopy(string editedOutputPath, string cleanPath)
        {
            DxfRawDocument doc = DxfRawDocument.ReadSavedOutput(editedOutputPath);
            WriteCleanDocument(doc, cleanPath);
        }

        internal static void WriteCleanDocument(DxfRawDocument doc, string cleanPath, string backupPath = null)
        {
            List<Edit> edits = new List<Edit>();
            List<DxfRecord> marks = FindAnnotations(doc).ToList();
            foreach (DxfRecord mark in marks)
                edits.Add(new Edit { Start = mark.Pairs[0].Start, Length = mark.Pairs.Last().End - mark.Pairs[0].Start, Text = "" });
            // Retain any real objects using a similarly named layer. Remove the
            // tool's empty layer only if no retained object references it.
            bool referenced = doc.Records.Except(marks).Any(r => r.Type != "LAYER" && string.Equals(r.Text(8), MarkerLayer, StringComparison.OrdinalIgnoreCase));
            DxfRecord layer = doc.Records.FirstOrDefault(r => r.Type == "LAYER" && string.Equals(r.Text(2), MarkerLayer, StringComparison.OrdinalIgnoreCase));
            if (!referenced && layer != null)
            {
                edits.Add(new Edit { Start = layer.Pairs[0].Start, Length = layer.Pairs.Last().End - layer.Pairs[0].Start, Text = "" });
                DxfRecord table = doc.Records.FirstOrDefault(r => r.Type == "TABLE" && r.Text(2) == "LAYER");
                if (table != null)
                {
                    DxfPair count = table.Pairs.FirstOrDefault(p => p.Code == 70);
                    if (count != null)
                    {
                        int start = doc.Records.IndexOf(table);
                        int remaining = doc.Records.Skip(start + 1).TakeWhile(r => r.Type != "ENDTAB").Count(r => r.Type == "LAYER" && r != layer);
                        Replace(edits, count, remaining.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
            StringBuilder output = new StringBuilder(doc.Raw);
            foreach (Edit edit in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
            { if (edit.Length > 0) output.Remove(edit.Start, edit.Length); if (!string.IsNullOrEmpty(edit.Text)) output.Insert(edit.Start, edit.Text); }
            Directory.CreateDirectory(Path.GetDirectoryName(cleanPath));
            byte[] bytes = Encoding.GetEncoding(28591).GetBytes(output.ToString());
            // A saved clean file may still be open in CAD. An unchanged file
            // needs no replacement and keeps its current filename and handle.
            if (backupPath != null && SavedBytesEqual(cleanPath, bytes)) return;
            Atomic(cleanPath, temp => File.WriteAllBytes(temp, bytes), backupPath);
        }

        private static bool SavedBytesEqual(string path, byte[] bytes)
        {
            if (!File.Exists(path)) return false;
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (MemoryStream buffer = new MemoryStream()) {
                if (file.Length != bytes.Length) return false;
                file.CopyTo(buffer);
                return file.Length == bytes.Length && stamp == File.GetLastWriteTimeUtc(path) && buffer.ToArray().SequenceEqual(bytes);
            }
        }

        internal static void WriteBack(string cleanPath, string sourcePath, string backupPath, CancellationToken token)
        {
            string clean = Path.GetFullPath(cleanPath), source = Path.GetFullPath(sourcePath), backup = Path.GetFullPath(backupPath);
            if (string.Equals(clean, source, StringComparison.OrdinalIgnoreCase) || string.Equals(source, backup, StringComparison.OrdinalIgnoreCase) || string.Equals(clean, backup, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Đường dẫn bản sạch, file gốc và bản sao lưu phải khác nhau.");
            if (!File.Exists(source)) throw new FileNotFoundException("Không tìm thấy DXF gốc tại đường dẫn đã kiểm tra.", source);
            if (File.Exists(backup)) throw new IOException("Bản sao lưu đã tồn tại; không ghi đè bản sao lưu cũ.");
            if ((File.GetAttributes(source) & FileAttributes.ReadOnly) != 0) throw new UnauthorizedAccessException("DXF gốc đang ở chế độ chỉ đọc.");
            token.ThrowIfCancellationRequested();
            // Reject a locked original before creating the replacement. File.Replace
            // also refuses a destination still held open without delete sharing.
            using (FileStream original = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            string temp = source + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (FileStream input = new FileStream(clean, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    input.CopyTo(output); output.Flush(true);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                token.ThrowIfCancellationRequested();
                // Replace the exact source path and back up its current content in
                // the same operation; never truncate the source before copying.
                File.Replace(temp, source, backup);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static string EntityHeader(DxfRawDocument doc, string handle, string type, string subclass, string space, bool modern)
        {
            bool paper = !string.Equals(space, "MODEL", StringComparison.OrdinalIgnoreCase);
            DxfRecord owner = doc.Records.FirstOrDefault(r => r.Type == "BLOCK_RECORD" && string.Equals(r.Text(2), paper ? "*Paper_Space" : "*Model_Space", StringComparison.OrdinalIgnoreCase));
            string text = Pair(doc, 0, type);
            if (modern) text += Pair(doc, 5, handle) + (owner != null ? Pair(doc, 330, owner.Text(5)) : "") + Pair(doc, 100, "AcDbEntity");
            if (paper) text += Pair(doc, 67, "1");
            text += Pair(doc, 8, MarkerLayer) + Pair(doc, 62, "1");
            if (modern) text += Pair(doc, 100, subclass);
            return text;
        }
        private static string PointPairs(DxfRawDocument doc, DxfPoint p, int code)
        { return Pair(doc, code, p.X.ToString("R", CultureInfo.InvariantCulture)) + Pair(doc, code + 10, p.Y.ToString("R", CultureInfo.InvariantCulture)) + Pair(doc, code + 20, "0"); }
        private static string Pair(DxfRawDocument doc, int code, string value) { return code.ToString(CultureInfo.InvariantCulture) + doc.NewLine + value + doc.NewLine; }
        private static void Replace(List<Edit> edits, DxfPair p, string value) { edits.Add(new Edit { Start = p.ValueStart, Length = p.ValueEnd - p.ValueStart, Text = value }); }
        private static void Atomic(string path, Action<string> write, string backupPath = null)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                write(temp);
                if (backupPath != null && File.Exists(path)) {
                    if (File.Exists(backupPath)) throw new IOException("Bản sao lưu DXF sạch đã tồn tại.");
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
                    File.Replace(temp, path, backupPath);
                }
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
        public static void WriteExcel(DxfBatchResult batch)
        {
            Atomic(batch.ExcelPath, temp =>
            {
                using (FileStream file = File.Create(temp)) using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    Entry(zip, "[Content_Types].xml", new XElement(XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Types",
                        Content("Default", "Extension", "rels", "application/vnd.openxmlformats-package.relationships+xml"), Content("Default", "Extension", "xml", "application/xml"),
                        Content("Override", "PartName", "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
                        Content("Override", "PartName", "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"),
                        Content("Override", "PartName", "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
                        Content("Override", "PartName", "/xl/worksheets/sheet2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
                        Content("Override", "PartName", "/xl/worksheets/sheet3.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
                        Content("Override", "PartName", "/xl/worksheets/sheet4.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
                    Entry(zip, "_rels/.rels", new XElement(P + "Relationships", Relation("rId1", "officeDocument", "xl/workbook.xml")));
                    Entry(zip, "xl/workbook.xml", new XElement(S + "workbook", new XAttribute(XNamespace.Xmlns + "r", R), new XElement(S + "sheets", SheetName("TỔNG HỢP", 1), SheetName("CHI TIẾT LỖI", 2), SheetName("LAYER VÀNG", 3), SheetName("ĐỘ KÍN", 4))));
                    Entry(zip, "xl/_rels/workbook.xml.rels", new XElement(P + "Relationships", Relation("rId1", "worksheet", "worksheets/sheet1.xml"), Relation("rId2", "worksheet", "worksheets/sheet2.xml"), Relation("rId3", "worksheet", "worksheets/sheet3.xml"), Relation("rId4", "worksheet", "worksheets/sheet4.xml"), Relation("rId5", "styles", "styles.xml")));
                    Entry(zip, "xl/styles.xml", Styles());
                    List<object[]> summary = new List<object[]> { new object[] { "STT", "FILE DXF", "KẾT QUẢ", "ĐƯỜNG TRÙNG", "LỖ MÉO", "ELLIPSE", "SỐ POLYLINE", "ĐOẠN < 0,5 mm", "LỖ ĐAN NHAU", "SỐ LỖI", "GIÂY", "DXF OUTPUT", "LAYER VÀNG CÓ ĐỐI TƯỢNG", "BIÊN KÍN VÀNG", "BIÊN NGOÀI CHƯA KÍN", "BIÊN TRONG CHƯA KÍN", "BIÊN CHƯA KÍN CHƯA PHÂN LOẠI", "LỖ TRÒN THÔNG MÉP", "LỖ TRÒN SPLINE", "FILE TRÙNG TÊN", "KIỂM TRA LẠI LÚC", "CẬP NHẬT FILE GỐC LÚC", "BẢN SAO LƯU GỐC", "CHỖ CHƯA NỐI ĐÚNG ĐẦU", "ĐIỀU KIỆN ĐÃ CHECK", "ĐƯỜNG SPLINE (NG)", "ĐƯỜNG POLYLINE (NG)" } };
                    List<object[]> detail = new List<object[]> { new object[] { "FILE DXF", "MÃ DẤU", "LOẠI LỖI", "ĐỐI TƯỢNG", "HANDLE / ĐOẠN", "LAYER", "X (mm)", "Y (mm)", "GIÁ TRỊ (mm)", "MÔ TẢ" } };
                    List<object[]> yellow = new List<object[]> { new object[] { "FILE DXF", "LAYER", "NGUỒN MÀU", "KHÔNG GIAN", "HÌNH HỌC", "SỐ LƯỢNG", "SỐ ĐỐI TƯỢNG DXF", "GHI CHÚ" } };
                    List<object[]> closure = new List<object[]> { new object[] { "FILE DXF", "BIÊN", "VỊ TRÍ", "TRONG BIÊN", "CẤP LỒNG", "ĐỘ KÍN", "SỐ CHỖ HỞ", "HỞ LỚN NHẤT (mm)", "LAYER", "GHI CHÚ" } };
                    List<Tuple<int, string>> links = new List<Tuple<int, string>>();
                    for (int i = 0; i < batch.Files.Count; i++)
                    {
                        DxfFileResult f = batch.Files[i];
                        summary.Add(new object[] { i + 1, f.RelativeName, f.Status, Count(f, "OVERLAP"), Count(f, "ROUND_DEFORMED"), Count(f, "ELLIPSE"), f.Error == null ? (object)PolylineCount(f) : null, Count(f, "SHORT_EDGE"), Count(f, "HOLE_INTERSECTION"), f.Issues.Count, f.Milliseconds / 1000, f.OutputPath == null ? (f.Status == "OK" ? "OK — không cần xuất DXF" : "Không xuất được DXF") : (f.CleanPath == null ? f.RelativeName : Path.Combine("DXF_SACH", Path.GetFileName(f.CleanPath))), f.Error == null && f.Options.Has(DxfCheckCondition.YellowLayers) ? (object)f.YellowLayerWithEntitiesCount : null, f.Error == null && f.Options.Has(DxfCheckCondition.YellowLayers) ? (object)f.YellowContourCount : null, f.Error == null && f.Options.Has(DxfCheckCondition.Closure) ? (object)f.OpenOuterCount : null, f.Error == null && f.Options.Has(DxfCheckCondition.Closure) ? (object)f.OpenInnerCount : null, f.Error == null && f.Options.Has(DxfCheckCondition.Closure) ? (object)f.OpenOtherCount : null, f.Error == null ? (object)Count(f, "ROUND_HOLE_TO_EDGE") : null, f.Error == null ? (object)Count(f, "SPLINE_ROUND_HOLE") : null, f.DuplicateNameCount > 1 ? (object)f.DuplicateNameCount : null, f.RecheckedAt.HasValue ? f.RecheckedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : null, f.SourceUpdatedAt.HasValue ? f.SourceUpdatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : null, f.SourceBackupPath, f.Error == null ? (object)Count(f, "CONTOUR_JOIN_UNTRIMMED") : null, f.Options.SelectedNames, Count(f, "SPLINE"), Count(f, "POLYLINE") });
                        if (f.OutputPath != null) {
                            Uri reportFolder = new Uri(Path.GetFullPath(Path.GetDirectoryName(batch.ExcelPath)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
                            links.Add(Tuple.Create(i + 5, reportFolder.MakeRelativeUri(new Uri(Path.GetFullPath(f.OutputPath))).OriginalString));
                        }
                        foreach (DxfIssue issue in f.Issues) detail.Add(new object[] { f.RelativeName, "NG" + issue.Id, issue.Code, issue.EntityType, issue.Handle, issue.Layer, issue.Position.X, issue.Position.Y, issue.Measurement, issue.Description });
                        if (f.Error != null) detail.Add(new object[] { f.RelativeName, "", "READ_ERROR", "", "", "", null, null, null, f.Error });
                        if (!f.Options.Has(DxfCheckCondition.Closure)) closure.Add(new object[] { f.RelativeName, "", "", "", null, "Không chọn điều kiện", null, null, "", "Biên kín / đầu nét nối đúng không được kiểm tra trong lượt này." });
                        else if (f.Error != null) closure.Add(new object[] { f.RelativeName, "", "", "", null, "Không kiểm tra được", null, null, "", f.Error });
                        else foreach (DxfContourResult contour in f.Contours) closure.Add(new object[] { f.RelativeName, "C" + contour.Id, contour.RoleName, contour.Parent == null ? "" : "C" + contour.Parent.Id, contour.Depth, contour.ClosureName, contour.OpenGapCount, contour.GapLength, contour.Layers, contour.Note });
                        if (!f.Options.Has(DxfCheckCondition.YellowLayers)) yellow.Add(new object[] { f.RelativeName, "", "", "", "Không chọn điều kiện", null, null, "Thống kê layer / đối tượng vàng không được chọn trong lượt này." });
                        else if (f.Error != null) yellow.Add(new object[] { f.RelativeName, "", "", "", "Không thống kê được", null, null, f.Error });
                        else {
                            foreach (DxfShapeStatistic shape in f.YellowShapes) yellow.Add(new object[] { f.RelativeName, shape.Layer, shape.ColorSource, shape.Space, shape.ShapeName, shape.Count, shape.EntityCount, shape.Note });
                            if (f.YellowShapes.Count == 0) yellow.Add(new object[] { f.RelativeName, "", "", "", "Không có layer / đối tượng vàng", 0, 0, "" });
                        }
                    }
                    string context = "Đơn vị: mm. Lỗ tròn không có dung sai cho phép. Đoạn ngắn: < 0,5 mm.";
                    context += batch.Options.Has(DxfCheckCondition.PolylineEntity) ? " Báo NG mọi POLYLINE/LWPOLYLINE theo loại đối tượng." : " Cho phép Polyline; các lỗi hình học áp dụng theo điều kiện đã chọn.";
                    if (batch.Options.Has(DxfCheckCondition.SplineEntity)) context += " Báo NG mọi SPLINE theo loại đối tượng.";
                    context += " Điều kiện đã chọn (" + batch.Options.Count + "/" + DxfCheckOptions.Rules.Count + "): " + batch.Options.SelectedNames + ". OK/NG chỉ dựa trên các mục đã tick; ô trống trong cột kiểm tra là mục chưa chọn hoặc lỗi đọc.";
                    string summaryContext = batch.Files.Count + " file; " + batch.NgCount + " NG; " + batch.ErrorCount + " lỗi đọc" + (batch.Cancelled ? "; ĐÃ HỦY — kết quả chưa đầy đủ." : ".") + " " + context;
                    summaryContext += " Chỉ xuất DXF NG; bản DXF bỏ dấu NG được tạo riêng sau khi kiểm tra lại file đã sửa đạt OK.";
                    if (batch.DuplicateNameWarning != null) summaryContext += " " + batch.DuplicateNameWarning + ". Cột FILE TRÙNG TÊN: số file cùng tên ở các thư mục khác nhau.";
                    int rechecked = batch.Files.Count(f => f.RecheckedAt.HasValue);
                    if (rechecked > 0) summaryContext += " " + rechecked + " file dùng kết quả kiểm tra lại bản output đã sửa và lưu. DXF sạch nằm chung trong DXF_SACH, tên <tên gốc>__SACH.dxf; file trùng tên thêm tên thư mục nguồn trước __SACH.";
                    int written = batch.Files.Count(f => f.SourceUpdatedAt.HasValue);
                    if (written > 0) summaryContext += " " + written + " file gốc đã được cập nhật; bản cũ lưu trong SAO_LUU_GOC.";
                    Entry(zip, "xl/worksheets/sheet1.xml", Worksheet("CHECK DXF", summaryContext, summary, new double[] { 7, 23, 12, 17, 12, 12, 13, 21, 18, 12, 12, 28, 19, 19, 25, 25, 32, 26, 23, 24, 25, 28, 70, 29, 110, 26, 29 }, links));
                    Entry(zip, "xl/worksheets/sheet2.xml", Worksheet("CHI TIẾT LỖI DXF", "Mã NG trùng với nhãn màu đỏ trên DXF. Giá trị (mm): chiều dài chồng / đoạn ngắn / độ lệch lỗ / khoảng hở / bán kính R của lỗ thông mép hoặc lỗ tròn SPLINE. " + context, detail, new double[] { 23, 12, 29, 18, 32, 24, 19, 19, 23, 80 }, null, 9, 8, new[] { "OPEN_CONTOUR_", "CONTOUR_JOIN_UNTRIMMED" }));
                    Entry(zip, "xl/worksheets/sheet3.xml", Worksheet("THỐNG KÊ LAYER VÀNG", "Vàng: ACI 2 / RGB #FFFF00. Đếm biên dạng kín theo layer; đối tượng vàng riêng ghi riêng. Layer trống chỉ ghi để tham khảo, không tính vào số layer vàng có đối tượng. Cụm nét hở/có nhánh ghi rõ. Biên trùng vẫn được đếm theo đối tượng trong DXF.", yellow, new double[] { 23, 40, 23, 18, 36, 15, 22, 60 }, null, 7));
                    Entry(zip, "xl/worksheets/sheet4.xml", Worksheet("ĐỘ KÍN BIÊN DẠNG", "Xét biên ngoài trước, sau đó lỗ/rãnh bên trong. Ngưỡng nối đầu: 0.001 mm; khoảng hở hiển thị 3 số lẻ. Đầu nét vượt/chạm/cắt nhau nhưng chưa nối được báo riêng; đường bao dự kiến chỉ dùng để phân loại.", closure, new double[] { 23, 10, 31, 17, 14, 23, 17, 25, 38, 70 }, null, 9, 7));
                    if (links.Count > 0) Entry(zip, "xl/worksheets/_rels/sheet1.xml.rels", new XElement(P + "Relationships", links.Select((link, i) => new XElement(P + "Relationship", new XAttribute("Id", "rId" + (i + 1)), new XAttribute("Type", R.NamespaceName + "/hyperlink"), new XAttribute("Target", link.Item2), new XAttribute("TargetMode", "External")))));
                }
            });
        }
        private static int PolylineCount(DxfFileResult f)
        { return f.Edges.Where(e => e.OriginalType == "POLYLINE" || e.OriginalType == "LWPOLYLINE").Select(e => e.Handle).Distinct().Count(); }
        private static object Count(DxfFileResult f, string code) { return f.Error == null && f.Options.IssueEnabled(code) ? (object)f.Issues.Count(i => i.Code == code) : null; }
        private static XElement Content(string tag, string key, string value, string type) { return new XElement(XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + tag, new XAttribute(key, value), new XAttribute("ContentType", type)); }
        private static XElement Relation(string id, string type, string target) { return new XElement(P + "Relationship", new XAttribute("Id", id), new XAttribute("Type", R.NamespaceName + "/" + type), new XAttribute("Target", target)); }
        private static XElement SheetName(string name, int id) { return new XElement(S + "sheet", new XAttribute("name", name), new XAttribute("sheetId", id), new XAttribute(R + "id", "rId" + id)); }
        private static void Entry(ZipArchive zip, string name, XElement xml)
        { using (Stream stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open()) new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), xml).Save(stream); }
        private static string Column(int c) { string s = ""; for (c++; c > 0; c = (c - 1) / 26) s = (char)('A' + (c - 1) % 26) + s; return s; }
        private static XElement Cell(object value, int row, int col, int style)
        {
            XElement cell = new XElement(S + "c", new XAttribute("r", Column(col) + row), new XAttribute("s", style));
            if (value == null) return cell;
            if (value is int || value is double) cell.Add(new XElement(S + "v", Convert.ToString(value, CultureInfo.InvariantCulture)));
            else cell.Add(new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value.ToString())));
            return cell;
        }
        private static XElement Worksheet(string title, string context, List<object[]> rows, double[] widths, List<Tuple<int, string>> links, int wrapColumn = 9, int precisionColumn = -1, string[] precisionCodes = null, int[] millimetreColumns = null)
        {
            int last = rows.Count + 3;
            XElement data = new XElement(S + "sheetData", new XElement(S + "row", new XAttribute("r", 1), new XAttribute("ht", 25), new XAttribute("customHeight", 1), Cell(title, 1, 0, 5)), new XElement(S + "row", new XAttribute("r", 2), new XAttribute("ht", 30), new XAttribute("customHeight", 1), Cell(context, 2, 0, 6)));
            for (int i = 0; i < rows.Count; i++)
            {
                XElement row = new XElement(S + "row", new XAttribute("r", i + 4), new XAttribute("ht", links != null && i > 0 && rows[i].Length > 22 && rows[i][22] != null ? 60 : links == null && i > 0 ? 34 : 25), new XAttribute("customHeight", 1));
                for (int j = 0; j < rows[i].Length; j++)
                {
                    object value = rows[i][j]; int style = i == 0 ? 1 : value is double ? 4 : links == null && j == wrapColumn ? 7 : 0;
                    if (i > 0 && j == 2 && value is string && links != null) style = (string)value == "OK" ? 2 : 3;
                    if (i > 0 && j == 11 && links != null && links.Any(link => link.Item1 == i + 4)) style = 8;
                    if (i > 0 && j == 19 && links != null && value is int && (int)value > 1) style = 9;
                    if (i > 0 && j == 22 && links != null && value != null) style = 7;
                    if (i > 0 && value is double && j == precisionColumn
                        && (precisionCodes == null || precisionCodes.Any(code => Convert.ToString(rows[i][2]).StartsWith(code, StringComparison.Ordinal)))) style = 10;
                    if (i > 0 && value is double && millimetreColumns != null && millimetreColumns.Contains(j)) style = 10;
                    if (i > 0 && links != null && j == 24 && value is int && (int)value > 0) style = 9;
                    if (i > 0 && links != null && (j == 25 || j == 27 || j == 28)) style = 7;
                    row.Add(Cell(value, i + 4, j, style));
                }
                data.Add(row);
            }
            XElement sheet = new XElement(S + "worksheet", new XAttribute(XNamespace.Xmlns + "r", R), new XElement(S + "dimension", new XAttribute("ref", "A1:" + Column(widths.Length - 1) + last)),
                new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", 0), new XAttribute("showGridLines", 0), new XElement(S + "pane", new XAttribute("ySplit", 4), new XAttribute("topLeftCell", "A5"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                new XElement(S + "sheetFormatPr", new XAttribute("defaultRowHeight", 22)), new XElement(S + "cols", widths.Select((w, i) => new XElement(S + "col", new XAttribute("min", i + 1), new XAttribute("max", i + 1), new XAttribute("width", w), new XAttribute("customWidth", 1)))), data,
                new XElement(S + "autoFilter", new XAttribute("ref", "A4:" + Column(widths.Length - 1) + last)),
                new XElement(S + "mergeCells", new XElement(S + "mergeCell", new XAttribute("ref", "A2:" + Column(widths.Length - 1) + "2"))));
            if (links != null && links.Count > 0) sheet.Add(new XElement(S + "hyperlinks", links.Select((link, i) => new XElement(S + "hyperlink", new XAttribute("ref", "L" + link.Item1), new XAttribute(R + "id", "rId" + (i + 1))))));
            sheet.Add(new XElement(S + "pageMargins", new XAttribute("left", .3), new XAttribute("right", .3), new XAttribute("top", .5), new XAttribute("bottom", .5), new XAttribute("header", .2), new XAttribute("footer", .2)));
            return sheet;
        }
        private static XElement Styles()
        {
            return XElement.Parse(@"<styleSheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'>
              <numFmts count='2'><numFmt numFmtId='164' formatCode='0.0000000'/><numFmt numFmtId='165' formatCode='0.000'/></numFmts>
              <fonts count='7'>
                <font><sz val='11'/><name val='Arial'/><color rgb='FF263238'/></font>
                <font><b/><sz val='11'/><name val='Arial'/><color rgb='FFFFFFFF'/></font>
                <font><b/><sz val='11'/><name val='Arial'/><color rgb='FF176B35'/></font>
                <font><b/><sz val='11'/><name val='Arial'/><color rgb='FFB71C1C'/></font>
                <font><b/><sz val='15'/><name val='Arial'/><color rgb='FF263238'/></font>
                <font><sz val='11'/><name val='Arial'/><color rgb='FF1565C0'/><u/></font>
                <font><b/><sz val='11'/><name val='Arial'/><color rgb='FFA66A15'/></font>
              </fonts>
              <fills count='6'><fill><patternFill patternType='none'/></fill><fill><patternFill patternType='gray125'/></fill><fill><patternFill patternType='solid'><fgColor rgb='FF37474F'/><bgColor indexed='64'/></patternFill></fill><fill><patternFill patternType='solid'><fgColor rgb='FFE8F5E9'/><bgColor indexed='64'/></patternFill></fill><fill><patternFill patternType='solid'><fgColor rgb='FFFFEBEE'/><bgColor indexed='64'/></patternFill></fill><fill><patternFill patternType='solid'><fgColor rgb='FFFFF4D6'/><bgColor indexed='64'/></patternFill></fill></fills>
              <borders count='1'><border><left/><right/><top/><bottom/><diagonal/></border></borders>
              <cellStyleXfs count='1'><xf numFmtId='0' fontId='0' fillId='0' borderId='0'/></cellStyleXfs>
              <cellXfs count='11'>
                <xf numFmtId='0' fontId='0' fillId='0' borderId='0' xfId='0'><alignment vertical='center'/></xf>
                <xf numFmtId='0' fontId='1' fillId='2' borderId='0' xfId='0' applyFont='1' applyFill='1'><alignment horizontal='center' vertical='center' wrapText='1'/></xf>
                <xf numFmtId='0' fontId='2' fillId='3' borderId='0' xfId='0' applyFont='1' applyFill='1'><alignment horizontal='center' vertical='center'/></xf>
                <xf numFmtId='0' fontId='3' fillId='4' borderId='0' xfId='0' applyFont='1' applyFill='1'><alignment horizontal='center' vertical='center'/></xf>
                <xf numFmtId='164' fontId='0' fillId='0' borderId='0' xfId='0' applyNumberFormat='1'><alignment horizontal='right' vertical='center'/></xf>
                <xf numFmtId='0' fontId='4' fillId='0' borderId='0' xfId='0' applyFont='1'/>
                <xf numFmtId='0' fontId='0' fillId='0' borderId='0' xfId='0'><alignment vertical='center' wrapText='1'/></xf>
                <xf numFmtId='0' fontId='0' fillId='0' borderId='0' xfId='0'><alignment vertical='top' wrapText='1'/></xf>
                <xf numFmtId='0' fontId='5' fillId='0' borderId='0' xfId='0' applyFont='1'/>
                <xf numFmtId='0' fontId='6' fillId='5' borderId='0' xfId='0' applyFont='1' applyFill='1'><alignment horizontal='center' vertical='center'/></xf>
                <xf numFmtId='165' fontId='0' fillId='0' borderId='0' xfId='0' applyNumberFormat='1'><alignment horizontal='right' vertical='center'/></xf>
              </cellXfs><cellStyles count='1'><cellStyle name='Normal' xfId='0' builtinId='0'/></cellStyles>
            </styleSheet>");
        }
    }
}
