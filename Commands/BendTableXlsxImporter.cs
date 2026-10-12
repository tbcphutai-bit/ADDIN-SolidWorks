using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ADDIN.Commands
{
    // Imports the saved table without opening Excel or touching a SolidWorks model.
    // Re-evaluate Kiku's formulas from C and A: cached Excel results can be stale
    // when a replacement workbook changes its coefficient without recalculation.
    internal static class BendTableXlsxImporter
    {
        private static readonly XNamespace SheetNs =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelationshipNs =
            "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace DocumentRelationshipNs =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly Regex KikuFormula = new Regex(
            @"^(?:\$?C\$?(?<c>\d+)\*2|2\*\$?C\$?(?<c>\d+))/(?:\(TAN\(RADIANS\(180-\$?A\$?(?<a>\d+)\)/2\)\))$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static BendTableWorkbookData Read(byte[] bytes, string path)
        {
            var result = new BendTableWorkbookData { FilePath = path };
            try
            {
                using (var memory = new MemoryStream(bytes, false))
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Read))
                {
                    List<string> strings = ReadStrings(zip);
                    XDocument sheet = ReadXml(zip, FirstSheetPath(zip));
                    var cells = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
                    var sharedFormulas = new Dictionary<string, KeyValuePair<string, XElement>>();
                    int rows = 1, columns = 1;
                    foreach (XElement cell in sheet.Descendants(SheetNs + "c"))
                    {
                        string reference = (string)cell.Attribute("r");
                        int row, column;
                        if (!TryPosition(reference, out row, out column))
                            throw new InvalidDataException("Dia chi o Excel khong hop le: " + reference);
                        if (row > 20000 || column > 128)
                            throw new InvalidDataException("Bend Table vuot kich thuoc bang ho tro.");
                        if (cells.ContainsKey(reference))
                            throw new InvalidDataException("Trung dia chi o: " + reference);
                        cells.Add(reference, cell);
                        XElement shared = cell.Element(SheetNs + "f");
                        if (shared != null && (string)shared.Attribute("t") == "shared"
                            && !string.IsNullOrWhiteSpace(shared.Value))
                            sharedFormulas.Add((string)shared.Attribute("si"),
                                new KeyValuePair<string, XElement>(reference, shared));
                        rows = Math.Max(rows, row);
                        columns = Math.Max(columns, column);
                    }
                    Array values = Array.CreateInstance(typeof(object),
                        new[] { rows, columns }, new[] { 1, 1 });
                    foreach (var pair in cells)
                    {
                        int row, column;
                        TryPosition(pair.Key, out row, out column);
                        values.SetValue(ReadCell(pair.Value, strings), row, column);
                    }
                    foreach (var pair in cells)
                    {
                        XElement formula = pair.Value.Element(SheetNs + "f");
                        if (formula == null) continue;
                        string expression = Regex.Replace(ResolveFormula(pair.Key, formula, sharedFormulas), @"\s+", "").TrimStart('=');
                        Match match = KikuFormula.Match(expression);
                        if (!match.Success)
                            throw new InvalidDataException("Cong thuc chua ho tro tai " + pair.Key + ": " + expression);
                        int coefficientRow = int.Parse(match.Groups["c"].Value, CultureInfo.InvariantCulture);
                        int angleRow = int.Parse(match.Groups["a"].Value, CultureInfo.InvariantCulture);
                        int row, column;
                        TryPosition(pair.Key, out row, out column);
                        double coefficient = NumberAt(values, coefficientRow, 3);
                        double angle = NumberAt(values, angleRow, 1);
                        double value = 2.0 * coefficient / Math.Tan((180.0 - angle) * Math.PI / 360.0);
                        if (!BendValueMath.IsFinite(value))
                            throw new InvalidDataException("Ket qua cong thuc khong hop le tai " + pair.Key);
                        values.SetValue(value, row, column);
                    }
                    BendTableDeepReader.ParseBlocks(values, result);
                    Validate(result);
                }
            }
            catch (Exception ex)
            {
                result.Error = "Khong nhap duoc Excel chuan: " + ex.Message;
            }
            return result;
        }

        internal static void Validate(BendTableWorkbookData data)
        {
            if (!data.IsMillimeters || data.ValueKind == BendTableValueKind.Unknown
                || !Enum.IsDefined(typeof(BendTableValueKind), data.ValueKind))
                throw new InvalidDataException("Chua xac dinh duoc don vi mm/loai gia tri Bend Table.");
            if (data.Blocks.Count == 0)
                throw new InvalidDataException("Khong co block do day trong Bend Table.");
            var thicknesses = new HashSet<double>();
            foreach (BendTableBlockData block in data.Blocks)
            {
                if (!BendValueMath.IsFinite(block.ThicknessMm) || block.ThicknessMm <= 0
                    || !thicknesses.Add(block.ThicknessMm))
                    throw new InvalidDataException("Do day bi trung hoac khong hop le.");
                if (block.HasCoefficient && !BendValueMath.IsFinite(block.CoefficientMm))
                    throw new InvalidDataException("He so block khong hop le.");
                // Preserve the source blocks exactly. Some library templates
                // include thickness == residual; manufacturing applicability
                // is decided for the selected part, not during database import.
                if (block.RadiiMm.Count == 0 || block.Angles.Count == 0)
                    throw new InvalidDataException("Block thieu bang goc/R.");
                ValidateAxis(block.RadiiMm, false);
                ValidateAxis(block.Angles.Select(a => a.AngleDeg).ToList(), true);
                foreach (BendTableAngleData angle in block.Angles)
                {
                    if (angle.ValuesMm.Count != block.RadiiMm.Count)
                        throw new InvalidDataException("So cot gia tri khong khop cot R.");
                    foreach (double? value in angle.ValuesMm)
                        if (value.HasValue && !BendValueMath.IsFinite(value.Value))
                            throw new InvalidDataException("Gia tri bang goc/R khong hop le.");
                }
            }
        }

        private static void ValidateAxis(IList<double> values, bool isAngle)
        {
            var unique = new HashSet<double>();
            foreach (double value in values)
                if (!BendValueMath.IsFinite(value) || value < 0 || (isAngle && value >= 180)
                    || !unique.Add(value))
                    throw new InvalidDataException("Goc/R bi trung hoac khong hop le.");
        }

        private static double NumberAt(Array values, int row, int column)
        {
            if (row < 1 || row > values.GetUpperBound(0) || column > values.GetUpperBound(1))
                throw new InvalidDataException("Cong thuc tham chieu ra ngoai bang.");
            object raw = values.GetValue(row, column);
            double value;
            if (raw == null || !double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !BendValueMath.IsFinite(value))
                throw new InvalidDataException("Cong thuc thieu gia tri so tai dong " + row);
            return value;
        }

        private static string ResolveFormula(string reference, XElement formula,
            IDictionary<string, KeyValuePair<string, XElement>> sharedFormulas)
        {
            if (!string.IsNullOrWhiteSpace(formula.Value)) return formula.Value;
            KeyValuePair<string, XElement> master;
            string index = (string)formula.Attribute("si");
            if ((string)formula.Attribute("t") != "shared" || index == null
                || !sharedFormulas.TryGetValue(index, out master))
                throw new InvalidDataException("Cong thuc Excel thieu noi dung tai " + reference);
            int row, column, masterRow, masterColumn;
            TryPosition(reference, out row, out column);
            TryPosition(master.Key, out masterRow, out masterColumn);
            return Regex.Replace(master.Value.Value,
                @"(?<![A-Z0-9_])(?<ac>\$?)(?<col>[A-Z]{1,3})(?<ar>\$?)(?<row>\d+)(?![A-Z0-9_])", match =>
                {
                    int sourceRow, sourceColumn;
                    TryPosition(match.Groups["col"].Value + match.Groups["row"].Value, out sourceRow, out sourceColumn);
                    if (match.Groups["ac"].Value != "$") sourceColumn += column - masterColumn;
                    if (match.Groups["ar"].Value != "$") sourceRow += row - masterRow;
                    if (sourceColumn < 1 || sourceRow < 1) throw new InvalidDataException("Tham chieu cong thuc khong hop le.");
                    string letters = "";
                    while (sourceColumn > 0)
                    { sourceColumn--; letters = (char)('A' + sourceColumn % 26) + letters; sourceColumn /= 26; }
                    return match.Groups["ac"].Value + letters + match.Groups["ar"].Value
                        + sourceRow.ToString(CultureInfo.InvariantCulture);
                }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static object ReadCell(XElement cell, IList<string> strings)
        {
            string type = (string)cell.Attribute("t");
            string raw = (string)cell.Element(SheetNs + "v");
            if (type == "s")
            {
                int index;
                if (!int.TryParse(raw, out index) || index < 0 || index >= strings.Count)
                    throw new InvalidDataException("Chi so chuoi Excel khong hop le.");
                return strings[index];
            }
            if (type == "inlineStr") return BaseText(cell.Element(SheetNs + "is"));
            if (type == "e")
                throw new InvalidDataException("Excel co loi " + raw + " tai " + (string)cell.Attribute("r"));
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (type == "str" || type == "b") return raw;
            double number;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                return number;
            return raw;
        }

        private static List<string> ReadStrings(ZipArchive zip)
        {
            if (zip.GetEntry("xl/sharedStrings.xml") == null) return new List<string>();
            return ReadXml(zip, "xl/sharedStrings.xml").Root.Elements(SheetNs + "si")
                .Select(BaseText).ToList();
        }

        private static string BaseText(XElement element)
        {
            if (element == null) return "";
            // Phonetic guides rPh are not part of the actual Japanese cell text.
            return string.Concat(element.Elements(SheetNs + "t")
                .Concat(element.Elements(SheetNs + "r").SelectMany(r => r.Elements(SheetNs + "t")))
                .Select(t => t.Value));
        }

        private static string FirstSheetPath(ZipArchive zip)
        {
            XElement sheet = ReadXml(zip, "xl/workbook.xml").Descendants(SheetNs + "sheet").FirstOrDefault();
            if (sheet == null) throw new InvalidDataException("Excel khong co worksheet.");
            string id = (string)sheet.Attribute(DocumentRelationshipNs + "id");
            XElement rel = ReadXml(zip, "xl/_rels/workbook.xml.rels").Root
                .Elements(RelationshipNs + "Relationship").FirstOrDefault(r => (string)r.Attribute("Id") == id);
            if (rel == null || (string)rel.Attribute("TargetMode") == "External")
                throw new InvalidDataException("Worksheet khong co duong dan noi bo hop le.");
            string target = (string)rel.Attribute("Target");
            if (string.IsNullOrWhiteSpace(target)) throw new InvalidDataException("Worksheet thieu duong dan.");
            Uri resolved = new Uri(new Uri("http://xlsx.local/xl/workbook.xml"), target.Replace('\\', '/'));
            if (resolved.Host != "xlsx.local") throw new InvalidDataException("Duong dan worksheet khong hop le.");
            return Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/'));
        }

        private static XDocument ReadXml(ZipArchive zip, string path)
        {
            ZipArchiveEntry entry = zip.GetEntry(path);
            if (entry == null) throw new InvalidDataException("Excel thieu " + path);
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 25000000 };
            using (Stream stream = entry.Open())
            using (XmlReader reader = XmlReader.Create(stream, settings)) return XDocument.Load(reader);
        }

        private static bool TryPosition(string reference, out int row, out int column)
        {
            row = 0; column = 0;
            if (string.IsNullOrEmpty(reference)) return false;
            int index = 0;
            while (index < reference.Length && char.IsLetter(reference[index]))
            {
                int letter = char.ToUpperInvariant(reference[index]) - 'A' + 1;
                if (letter < 1 || letter > 26 || column > 16384) return false;
                column = column * 26 + letter;
                index++;
            }
            return column > 0 && int.TryParse(reference.Substring(index), out row) && row > 0;
        }
    }
}
