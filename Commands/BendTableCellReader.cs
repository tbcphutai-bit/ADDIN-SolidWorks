using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace ADDIN.Commands
{
    // Reads the thickness blocks in a Kikukawa bend table without starting Excel.
    // B is stock thickness; C is the bend-formula value. C denotes remaining
    // groove thickness only when D says 溝.
    internal static class BendTableCellReader
    {
        private const string SharedTables =
            @"C:\sw共有ライブラリ\テーブル\菊川ベンドテーブル_170905";
        private const double ThicknessToleranceMm = 0.01;
        private static readonly XNamespace Spreadsheet =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        public static bool TryRead(string tableFile, double modelThicknessMm,
            out string resolvedPath, out double tableThicknessMm,
            out double tableValueMm, out bool isGroove, out string error)
        {
            resolvedPath = ResolvePath(tableFile);
            tableThicknessMm = 0;
            tableValueMm = 0;
            isGroove = false;
            error = "";
            if (resolvedPath == null)
            {
                error = "Không tìm thấy Bend Table";
                return false;
            }
            try
            {
                using (FileStream file = File.Open(resolvedPath, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite))
                using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
                    if (sheetEntry == null)
                    {
                        error = "Bend Table không có Sheet1";
                        return false;
                    }
                    XDocument sheet;
                    using (Stream stream = sheetEntry.Open()) sheet = XDocument.Load(stream);
                    List<string> strings = ReadSharedStrings(archive);
                    var blocks = new List<ThicknessBlock>();
                    foreach (XElement row in sheet.Descendants(Spreadsheet + "row"))
                    {
                        if (ReadText(Cell(row, "A"), strings).Trim() != "厚み:") continue;
                        double thickness, value;
                        if (!TryNumber(Cell(row, "B"), out thickness) ||
                            !TryNumber(Cell(row, "C"), out value)) continue;
                        blocks.Add(new ThicknessBlock
                        {
                            Thickness = thickness,
                            Value = value,
                            IsGroove = ReadText(Cell(row, "D"), strings).Trim() == "溝"
                        });
                    }
                    if (blocks.Count == 0)
                    {
                        error = "Bend Table không có nhóm 厚み hợp lệ";
                        return false;
                    }
                    List<ThicknessBlock> matching = modelThicknessMm > 0
                        ? blocks.Where(b => Math.Abs(b.Thickness - modelThicknessMm) <=
                            ThicknessToleranceMm).ToList()
                        : blocks.Count == 1 ? blocks : new List<ThicknessBlock>();
                    if (matching.Count != 1)
                    {
                        error = modelThicknessMm > 0
                            ? "Không tìm thấy đúng một nhóm 厚み=" +
                                modelThicknessMm.ToString("0.###", CultureInfo.InvariantCulture) +
                                " mm trong Bend Table"
                            : "Không xác định được độ dày Part để chọn nhóm 厚み trong Bend Table";
                        return false;
                    }
                    ThicknessBlock selected = matching[0];
                    tableThicknessMm = selected.Thickness;
                    tableValueMm = selected.Value;
                    isGroove = selected.IsGroove;
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = "Không mở được Bend Table: " + ex.Message;
                return false;
            }
        }

        private sealed class ThicknessBlock
        {
            public double Thickness;
            public double Value;
            public bool IsGroove;
        }

        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            var result = new List<string>();
            ZipArchiveEntry entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return result;
            XDocument xml;
            using (Stream stream = entry.Open()) xml = XDocument.Load(stream);
            foreach (XElement item in xml.Descendants(Spreadsheet + "si"))
                // Ignore rPh phonetic readings (e.g. 溝 + みぞ); Excel exposes
                // only the base text as the cell value.
                result.Add(string.Concat(item.Elements(Spreadsheet + "t")
                    .Concat(item.Elements(Spreadsheet + "r")
                        .SelectMany(run => run.Elements(Spreadsheet + "t")))
                    .Select(text => text.Value)));
            return result;
        }

        private static XElement Cell(XElement row, string column)
        {
            return row.Elements(Spreadsheet + "c").FirstOrDefault(cell =>
                string.Equals((string)cell.Attribute("r"),
                    column + (string)row.Attribute("r"),
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string ReadText(XElement cell, IList<string> sharedStrings)
        {
            if (cell == null) return "";
            string raw = (string)cell.Element(Spreadsheet + "v");
            if ((string)cell.Attribute("t") == "s")
            {
                int index;
                return int.TryParse(raw, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out index) && index >= 0 &&
                    index < sharedStrings.Count ? sharedStrings[index] : "";
            }
            if ((string)cell.Attribute("t") == "inlineStr")
                return string.Concat(cell.Descendants(Spreadsheet + "t")
                    .Select(text => text.Value));
            return raw ?? "";
        }

        private static bool TryNumber(XElement cell, out double number)
        {
            return double.TryParse(cell == null ? null : (string)cell.Element(Spreadsheet + "v"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
                number > 0 && !double.IsNaN(number) && !double.IsInfinity(number);
        }

        private static string ResolvePath(string tableFile)
        {
            if (string.IsNullOrWhiteSpace(tableFile)) return null;
            try
            {
                if (File.Exists(tableFile)) return tableFile;
                string shared = Path.Combine(SharedTables, Path.GetFileName(tableFile));
                return File.Exists(shared) ? shared : null;
            }
            catch { return null; }
        }
    }
}
