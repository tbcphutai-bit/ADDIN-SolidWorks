using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ADDIN.Commands.DxfChecking
{
    internal sealed class DxfPair
    {
        public int Code, Start, End, ValueStart, ValueEnd;
        public string Value;
    }
    internal sealed class DxfRecord
    {
        public string Type, Section;
        public List<DxfPair> Pairs = new List<DxfPair>();
        public List<DxfRecord> Children = new List<DxfRecord>();
        public string Text(int code, string fallback = "") { DxfPair p = Pairs.FirstOrDefault(x => x.Code == code); return p == null ? fallback : p.Value; }
        public double Number(int code, double fallback = 0)
        {
            string value = Text(code, null);
            if (value == null) return fallback;
            double n;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out n) || double.IsInfinity(n) || double.IsNaN(n)) throw new InvalidDataException("Giá trị DXF không hợp lệ ở group " + code + ": " + value);
            return n;
        }
        public List<double> Numbers(int code) { return Pairs.Where(p => p.Code == code).Select(p => double.Parse(p.Value, CultureInfo.InvariantCulture)).ToList(); }
        public DxfPoint Point(int code = 10) { return new DxfPoint(Number(code), Number(code + 10)); }
    }

    internal sealed class DxfRawDocument
    {
        // One character per original byte: adding ASCII annotations never transcodes Japanese source data.
        public string Raw, NewLine = "\r\n", Version;
        public Encoding TextEncoding;
        public List<DxfPair> Pairs = new List<DxfPair>();
        public List<DxfRecord> Records = new List<DxfRecord>();
        public List<DxfRecord> Model = new List<DxfRecord>();
        public Dictionary<string, DxfRecord> Blocks = new Dictionary<string, DxfRecord>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DxfLayerInfo> Layers = new Dictionary<string, DxfLayerInfo>(StringComparer.OrdinalIgnoreCase);
        public DxfPair EntitiesEnd;
        public double UnitToMm = 1;
        private HashSet<DxfRecord> toolAnnotations;
        public string Decode(string raw) { return TextEncoding.GetString(Encoding.GetEncoding(28591).GetBytes(raw)); }

        public static DxfRawDocument Read(string path)
        {
            return ReadSavedOutput(path);
        }

        public static DxfRawDocument ReadSavedOutput(string path)
        {
            // Both source and edited DXFs may remain open for writing in CAD.
            // Read saved bytes without requesting a conflicting write lock.
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            byte[] bytes;
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (MemoryStream buffer = new MemoryStream())
            {
                long length = file.Length;
                file.CopyTo(buffer);
                if (length != file.Length || buffer.Length != length || stamp != File.GetLastWriteTimeUtc(path))
                    throw new IOException("DXF đang được lưu. Hãy đợi CAD lưu xong rồi kiểm tra lại.");
                bytes = buffer.ToArray();
            }
            return Parse(bytes);
        }

        private static DxfRawDocument Parse(byte[] bytes)
        {
            if (bytes.Length >= 18 && Encoding.ASCII.GetString(bytes, 0, 18).StartsWith("AutoCAD Binary DXF", StringComparison.Ordinal)) throw new InvalidDataException("DXF binary chưa được hỗ trợ. Hãy lưu DXF dạng ASCII trong ZWCAD.");
            DxfRawDocument doc = new DxfRawDocument { Raw = Encoding.GetEncoding(28591).GetString(bytes) };
            if (!doc.Raw.Contains("\r\n")) doc.NewLine = "\n";
            int offset = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
            List<Tuple<int, int, int, string>> lines = new List<Tuple<int, int, int, string>>();
            while (offset < doc.Raw.Length)
            {
                int start = offset;
                while (offset < doc.Raw.Length && doc.Raw[offset] != '\r' && doc.Raw[offset] != '\n') offset++;
                int end = offset;
                if (offset < doc.Raw.Length && doc.Raw[offset] == '\r') offset++;
                if (offset < doc.Raw.Length && doc.Raw[offset] == '\n') offset++;
                lines.Add(Tuple.Create(start, end, offset, doc.Raw.Substring(start, end - start)));
            }
            if (lines.Count % 2 != 0) throw new InvalidDataException("DXF bị thiếu cặp group code/value.");
            for (int i = 0; i < lines.Count; i += 2)
            {
                int code;
                if (!int.TryParse(lines[i].Item4.Trim(), out code) || code < 0 || code > 1071) throw new InvalidDataException("Group code DXF không hợp lệ ở dòng " + (i + 1));
                doc.Pairs.Add(new DxfPair { Code = code, Value = lines[i + 1].Item4.Trim(' ', '\t'), Start = lines[i].Item1, End = lines[i + 1].Item3, ValueStart = lines[i + 1].Item1, ValueEnd = lines[i + 1].Item2 });
            }
            string section = "";
            DxfRecord record = null, block = null, poly = null;
            for (int i = 0; i < doc.Pairs.Count; i++)
            {
                DxfPair pair = doc.Pairs[i];
                if (pair.Code == 0)
                {
                    if (pair.Value == "SECTION")
                    {
                        if (i + 1 >= doc.Pairs.Count || doc.Pairs[i + 1].Code != 2) throw new InvalidDataException("SECTION thiếu tên.");
                        section = doc.Pairs[i + 1].Value;
                    }
                    if (pair.Value == "ENDSEC") { if (section == "ENTITIES") doc.EntitiesEnd = pair; section = ""; }
                    record = new DxfRecord { Type = pair.Value, Section = section };
                    doc.Records.Add(record);
                    if (section == "ENTITIES" && pair.Value != "SECTION")
                    {
                        if (poly != null && (pair.Value == "VERTEX" || pair.Value == "SEQEND")) { poly.Children.Add(record); if (pair.Value == "SEQEND") poly = null; }
                        else { doc.Model.Add(record); poly = pair.Value == "POLYLINE" ? record : null; }
                    }
                    if (section == "BLOCKS")
                    {
                        if (pair.Value == "BLOCK") { block = record; poly = null; }
                        else if (pair.Value == "ENDBLK") { if (block != null) doc.Blocks[block.Text(2)] = block; block = null; poly = null; }
                        else if (block != null)
                        {
                            if (poly != null && (pair.Value == "VERTEX" || pair.Value == "SEQEND")) { poly.Children.Add(record); if (pair.Value == "SEQEND") poly = null; }
                            else { block.Children.Add(record); poly = pair.Value == "POLYLINE" ? record : null; }
                        }
                    }
                }
                if (record != null) record.Pairs.Add(pair);
            }
            if (doc.EntitiesEnd == null || !doc.Pairs.Any(p => p.Code == 0 && p.Value == "EOF")) throw new InvalidDataException("DXF thiếu ENTITIES/EOF.");
            doc.Version = doc.Header("$ACADVER") ?? "AC1009";
            int version;
            bool unicode = int.TryParse(doc.Version.Substring(2), out version) && version >= 1021;
            string page = doc.Header("$DWGCODEPAGE") ?? "ANSI_1252";
            int cp = 1252;
            int.TryParse(new string(page.Where(char.IsDigit).ToArray()), out cp);
            doc.TextEncoding = unicode ? new UTF8Encoding(false) : Encoding.GetEncoding(cp > 0 ? cp : 1252);
            foreach (DxfRecord layer in doc.Records.Where(r => r.Section == "TABLES" && r.Type == "LAYER"))
            {
                string name = doc.Decode(layer.Text(2));
                doc.Layers[name] = new DxfLayerInfo { Name = name, Color = ReadColor(layer, 7), Off = layer.Number(62, 7) < 0, Frozen = ((int)layer.Number(70) & 1) != 0 };
            }
            string units = doc.Header("$INSUNITS");
            if (units != null && units != "0" && units != "4")
                throw new InvalidDataException("DXF khai báo đơn vị khác mm ($INSUNITS=" + units + "). Tool này kiểm tra theo mm.");
            return doc;
        }

        public string Header(string name)
        {
            for (int i = 0; i < Pairs.Count - 1; i++) if (Pairs[i].Code == 9 && Pairs[i].Value == name) return Pairs[i + 1].Value;
            return null;
        }

        public void Extract(DxfFileResult result)
        {
            toolAnnotations = DxfCheckExport.FindAnnotations(this);
            result.YellowLayers = Layers.Values.Where(l => l.Color.IsYellow && !string.Equals(l.Name, DxfCheckExport.MarkerLayer, StringComparison.OrdinalIgnoreCase)).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
            Expand(Model, DxfTransform.Identity, "", "", "", new DxfColor { Aci = 7 }, 0, new HashSet<string>(), result);
        }

        private static DxfColor ReadColor(DxfRecord record, int fallback)
        {
            int aci = (int)record.Number(62, fallback);
            string rawRgb = record.Text(420, null);
            int rgb;
            if (rawRgb != null && (!int.TryParse(rawRgb, NumberStyles.Integer, CultureInfo.InvariantCulture, out rgb) || rgb < 0 || rgb > 0xFFFFFF))
                throw new InvalidDataException("Màu TrueColor DXF không hợp lệ: " + rawRgb);
            return new DxfColor { Aci = aci, Rgb = rawRgb == null ? (int?)null : int.Parse(rawRgb, CultureInfo.InvariantCulture) };
        }

        private void Expand(IEnumerable<DxfRecord> records, DxfTransform transform, string path, string inheritedLayer, string inheritedSpace, DxfColor inheritedColor, int depth, HashSet<string> visiting, DxfFileResult result)
        {
            if (depth > 32) throw new InvalidDataException("Block lồng quá 32 cấp.");
            foreach (DxfRecord record in records)
            {
                if (toolAnnotations.Contains(record)) continue;
                string rawLayer = record.Text(8, "0"), layer = Decode(rawLayer);
                if (layer == "0" && inheritedLayer.Length > 0) layer = inheritedLayer;
                if (string.Equals(layer, "CHECK_DXF_NG", StringComparison.OrdinalIgnoreCase)) continue;
                DxfLayerInfo layerInfo;
                Layers.TryGetValue(layer, out layerInfo);
                DxfColor color = ReadColor(record, 256);
                if (!color.Rgb.HasValue && color.Aci == 256) color = layerInfo == null ? new DxfColor { Aci = 7 } : layerInfo.Color;
                else if (!color.Rgb.HasValue && color.Aci == 0) color = inheritedColor;
                bool layerYellow = layerInfo != null && layerInfo.Color.IsYellow;
                string space = inheritedSpace.Length > 0 ? inheritedSpace : record.Text(410, record.Number(67) == 1 ? "PAPER" : "MODEL");
                string handle = path + record.Text(5, "@" + record.Pairs[0].Start);
                if (record.Type == "INSERT")
                {
                    DxfRecord block;
                    string name = record.Text(2);
                    if (!Blocks.TryGetValue(name, out block)) throw new InvalidDataException("Không tìm thấy block " + Decode(name));
                    if (!visiting.Add(name)) throw new InvalidDataException("Block tham chiếu vòng: " + Decode(name));
                    if (((int)block.Number(70) & 12) != 0) throw new InvalidDataException("XREF chưa được bind: " + Decode(name));
                    RequirePlane(record);
                    double a = record.Number(50) * Math.PI / 180, sx = record.Number(41, 1), sy = record.Number(42, 1);
                    if (record.Number(230, 1) < 0) sx = -sx;
                    int columns = Math.Max(1, (int)record.Number(70, 1)), rows = Math.Max(1, (int)record.Number(71, 1));
                    if ((long)columns * rows > 100000) throw new InvalidDataException("INSERT array quá lớn.");
                    for (int row = 0; row < rows; row++) for (int col = 0; col < columns; col++)
                    {
                        DxfTransform local = new DxfTransform { A = sx * Math.Cos(a), B = sx * Math.Sin(a), C = -sy * Math.Sin(a), D = sy * Math.Cos(a) };
                        DxfPoint shift = record.Point() - local.Vector(block.Point()) + new DxfPoint(Math.Cos(a) * col * record.Number(44) - Math.Sin(a) * row * record.Number(45), Math.Sin(a) * col * record.Number(44) + Math.Cos(a) * row * record.Number(45));
                        // INSERT coordinates use OCS; a negative Z normal reverses OCS X.
                        if (record.Number(230, 1) < 0) shift.X = -record.Number(10) - local.Vector(block.Point()).X + Math.Cos(a) * col * record.Number(44) - Math.Sin(a) * row * record.Number(45);
                        local.X = shift.X; local.Y = shift.Y;
                        Expand(block.Children, local.Then(transform), handle + "[" + row + "," + col + "]>", layer, space, color, depth + 1, visiting, result);
                    }
                    visiting.Remove(name);
                    continue;
                }
                if (record.Type == "LINE" || record.Type == "ARC" || record.Type == "CIRCLE" || record.Type == "ELLIPSE" || record.Type == "SPLINE" || record.Type == "POLYLINE" || record.Type == "LWPOLYLINE")
                {
                    RequirePlane(record);
                    List<DxfEdge> edges = DxfGeometry.ReadEdges(record, transform);
                    if (edges.Count == 0) throw new InvalidDataException("Biên dạng rỗng: " + handle);
                    result.GeometryCount++;
                    bool closedEntity = (record.Type == "POLYLINE" || record.Type == "LWPOLYLINE") && ((int)record.Number(70) & 1) != 0;
                    foreach (DxfEdge edge in edges) {
                        edge.Handle = handle; edge.Layer = layer; edge.Space = space; edge.OriginalType = record.Type;
                        edge.EntityClosed = closedEntity || edge.Closed; edge.LayerIsYellow = layerYellow; edge.IsYellow = color.IsYellow; result.Edges.Add(edge);
                    }
                }
                else if (record.Type == "REGION" || record.Type == "3DSOLID" || record.Type == "BODY" || record.Type == "ACAD_PROXY_ENTITY" || record.Type == "HATCH" || record.Type == "MESH" || record.Type == "HELIX" || record.Type == "XLINE" || record.Type == "RAY")
                    throw new InvalidDataException("Có đối tượng chưa hỗ trợ kiểm tra: " + record.Type + " (" + handle + "). Hãy explode thành biên dạng 2D.");
                else if ((layerYellow || color.IsYellow) && (record.Type == "TEXT" || record.Type == "MTEXT" || record.Type == "ATTRIB" || record.Type == "ATTDEF" || record.Type == "POINT" || record.Type == "DIMENSION" || record.Type == "SOLID" || record.Type == "TRACE"))
                    result.YellowItems.Add(new DxfNonCutItem { Layer = layer, Space = space, Type = record.Type, LayerIsYellow = layerYellow, IsYellow = color.IsYellow });
            }
        }

        private static void RequirePlane(DxfRecord r)
        {
            if (Math.Abs(r.Number(210)) > 1e-12 || Math.Abs(r.Number(220)) > 1e-12 || Math.Abs(Math.Abs(r.Number(230, 1)) - 1) > 1e-12)
                throw new InvalidDataException("Biên dạng có mặt phẳng 3D nghiêng: " + r.Text(5));
            List<double> zs = r.Numbers(30).Concat(r.Numbers(31)).ToList();
            if (zs.Any(z => Math.Abs(z) > 1e-7) || Math.Abs(r.Number(38)) > 1e-7)
                throw new InvalidDataException("Biên dạng không nằm trên Z=0: " + r.Text(5));
        }
    }
}
