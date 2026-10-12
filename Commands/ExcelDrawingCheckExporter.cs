using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ADDIN.Commands
{
    /// <summary>
    /// Compact Excel exporter for CHECK DRAWING.
    ///
    /// Layout:
    /// 1) TỔNG HỢP
    ///    - Chỉ COMPONENT / Part Drawing
    ///    - Cột đầu tiên là INo gốc từ BOM
    ///    - Field columns only show OK / NG / Warning
    ///
    /// 2) CHI TIẾT KIỂM TRA
    ///    - 1 checked field = 1 row
    ///    - Drawing Value / Expected(BOM or Assembly) / Status / Ghi chú
    ///
    /// COMPONENT nằm ở TỔNG HỢP + CHI TIẾT.
    /// UNIT chỉ nằm ở CHI TIẾT KIỂM TRA.
    /// </summary>
    internal static class ExcelDrawingCheckExporter
    {
        private const int XlCenter = -4108;
        private const int XlTop = -4160;
        private const int XlContinuous = 1;

        public static void Export(DrawingBatchCheckResult result)
        {
            ExportReport(result, null, false);
        }

        public static void ExportToFile(DrawingBatchCheckResult result, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Chưa có đường dẫn file Excel.", nameof(outputPath));

            string fullPath = Path.GetFullPath(outputPath);
            if (!string.Equals(Path.GetExtension(fullPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("File báo cáo phải có đuôi .xlsx.", nameof(outputPath));

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            ExportReport(result, fullPath, true);
        }

        private static void ExportReport(DrawingBatchCheckResult result, string outputPath, bool silent)
        {
            if (result == null ||
                result.Items == null ||
                result.Items.Count == 0)
            {
                return;
            }

            dynamic excel = null;
            dynamic workbook = null;
            dynamic summarySheet = null;
            dynamic detailSheet = null;
            bool eventsEnabled = true;
            var totalTimer = Stopwatch.StartNew();
            var phaseTimer = Stopwatch.StartNew();

            try
            {
                Type excelType =
                    Type.GetTypeFromProgID("Excel.Application");

                if (excelType == null)
                {
                    if (silent)
                        throw new InvalidOperationException("Không tìm thấy Microsoft Excel trên máy tính.");

                    MessageBox.Show(
                        "Không tìm thấy Microsoft Excel trên máy tính.",
                        "CHECK DRAWING — XUẤT EXCEL",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }

                excel =
                    Activator.CreateInstance(excelType);

                excel.ScreenUpdating = false;
                excel.DisplayAlerts = false;
                eventsEnabled = excel.EnableEvents;
                excel.EnableEvents = false;

                workbook =
                    excel.Workbooks.Add();

                PrepareSheetCount(
                    excel,
                    workbook,
                    2);

                Debug.WriteLine("[CHECK DRAWING EXCEL] workbook created ms=" + phaseTimer.ElapsedMilliseconds);

                summarySheet =
                    workbook.Sheets[1];

                summarySheet.Name =
                    "TỔNG HỢP";

                detailSheet =
                    workbook.Sheets[2];

                detailSheet.Name =
                    "CHI TIẾT KIỂM TRA";

                // TỔNG HỢP chỉ dành cho COMPONENT / Part Drawing.
                // UNIT / Assembly Drawing chỉ xuất ở sheet CHI TIẾT KIỂM TRA.
                List<DrawingCheckItemResult> componentItems =
                    GetComponentItems(
                        result.Items);

                List<string> summaryFields =
                    GetSummaryFieldOrder(
                        componentItems);

                Debug.WriteLine(
                    "[CHECK DRAWING EXCEL] summary components=" +
                    componentItems.Count +
                    " / all items=" +
                    result.Items.Count);

                Debug.WriteLine("[CHECK DRAWING EXCEL] write summary start");
                phaseTimer.Restart();

                WriteSummarySheet(
                    summarySheet,
                    componentItems,
                    summaryFields);

                Debug.WriteLine("[CHECK DRAWING EXCEL] write summary done ms=" + phaseTimer.ElapsedMilliseconds);

                FreezeTopRow(
                    summarySheet);

                Debug.WriteLine("[CHECK DRAWING EXCEL] write detail start");
                phaseTimer.Restart();

                WriteGroupedDetailSheet(
                    detailSheet,
                    result.Items);

                Debug.WriteLine("[CHECK DRAWING EXCEL] write detail done ms=" + phaseTimer.ElapsedMilliseconds);

                FreezeTopRow(
                    detailSheet);

                summarySheet.Activate();
                summarySheet.Range["A1"].Select();

                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    // Use the same report and formatting as the normal command.
                    phaseTimer.Restart();
                    workbook.SaveAs(outputPath, 51); // xlOpenXMLWorkbook
                    Debug.WriteLine("[CHECK DRAWING EXCEL] saved ms=" + phaseTimer.ElapsedMilliseconds + ": " + outputPath);
                }

                if (!silent)
                {
                    excel.ScreenUpdating = true;
                    excel.DisplayAlerts = true;
                    excel.EnableEvents = eventsEnabled;
                    excel.Visible = true;
                }

                Debug.WriteLine("[CHECK DRAWING EXCEL] export complete ms=" + totalTimer.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[CHECK DRAWING EXCEL] ERROR: " +
                    ex.ToString());

                if (silent)
                    throw;

                try
                {
                    if (excel != null)
                    {
                        excel.ScreenUpdating = true;
                        excel.DisplayAlerts = true;
                        excel.EnableEvents = eventsEnabled;
                    }
                }
                catch
                {
                }

                MessageBox.Show(
                    "Đã hoàn tất CHECK DRAWING nhưng xảy ra lỗi khi xuất Excel:\n" +
                    ex.Message,
                    "CHECK DRAWING — LỖI XUẤT EXCEL",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                if (silent)
                {
                    if (workbook != null)
                    {
                        try { workbook.Close(false); } catch { }
                    }
                    if (excel != null)
                    {
                        try { excel.Quit(); } catch { }
                    }
                    ReleaseExcelObject((object)detailSheet);
                    ReleaseExcelObject((object)summarySheet);
                    ReleaseExcelObject((object)workbook);
                    ReleaseExcelObject((object)excel);
                }
            }
        }

        private static void ReleaseExcelObject(object value)
        {
            if (value == null || !Marshal.IsComObject(value))
                return;
            try { Marshal.ReleaseComObject(value); } catch { }
        }

        // ================================================================
        // SUMMARY
        // ================================================================

        private static void WriteSummarySheet(
            dynamic sheet,
            List<DrawingCheckItemResult> items,
            List<string> fieldOrder)
        {
            List<string> headers =
                new List<string>
                {
                    "INo",
                    "Component",
                    "Drawing File",
                    "Kết quả",
                    "Số lỗi NG"
                };

            foreach (string fieldName in fieldOrder)
            {
                headers.Add(
                    GetDisplayFieldName(fieldName));
            }

            WriteHeaders(
                sheet,
                headers.ToArray());

            int rowCount =
                items != null
                    ? items.Count
                    : 0;

            int colCount =
                headers.Count;

            if (rowCount > 0)
            {
                object[,] data =
                    new object[rowCount, colCount];

                for (int i = 0;
                     i < rowCount;
                     i++)
                {
                    DrawingCheckItemResult item =
                        items[i];

                    string drawingFile =
                        string.IsNullOrWhiteSpace(
                            item.DrawingPath)
                            ? "(Chưa có Drawing)"
                            : Path.GetFileName(
                                item.DrawingPath);

                    string overallStatus =
                        GetEffectiveSummaryStatus(
                            item);

                    data[i, 0] =
                        item.BomItemNumber;

                    data[i, 1] =
                        item.Component ?? "";

                    data[i, 2] =
                        drawingFile;

                    data[i, 3] =
                        overallStatus;

                    data[i, 4] =
                        CountNgFields(item);

                    for (int f = 0;
                         f < fieldOrder.Count;
                         f++)
                    {
                        DrawingBomFieldResult field =
                            GetField(
                                item.Fields,
                                fieldOrder[f]);

                        data[i, 5 + f] =
                            field != null
                                ? GetEffectiveFieldStatus(
                                    fieldOrder[f],
                                    field)
                                : "-";
                    }
                }

                dynamic dataRange =
                    sheet.Range[
                        sheet.Cells[2, 1],
                        sheet.Cells[
                            rowCount + 1,
                            colCount]];

                dataRange.Value2 = data;

                // Format runs of equal statuses together instead of making
                // several cross-process Excel calls for every status cell.
                var statusRanges = new Dictionary<string, List<string>>();
                for (int i = 0;
                     i < rowCount;
                     i++)
                {
                    int row = i + 2;
                    AddStatusRange(statusRanges, Convert.ToString(data[i, 3]),
                        RangeAddress(row, 4, row, 4));
                    int first = 5;
                    while (first < colCount)
                    {
                        string status = Convert.ToString(data[i, first]);
                        int end = first;
                        while (end + 1 < colCount &&
                               string.Equals(status, Convert.ToString(data[i, end + 1]),
                                   StringComparison.OrdinalIgnoreCase))
                            end++;
                        AddStatusRange(statusRanges, status,
                            RangeAddress(row, first + 1, row, end + 1));
                        first = end + 1;
                    }
                }
                ApplyBatchedStatusColors((object)sheet, statusRanges);
            }

            int lastRow =
                Math.Max(
                    2,
                    rowCount + 1);

            FinishSummarySheet(
                sheet,
                lastRow,
                colCount);

            // KPI của sheet TỔNG HỢP cũng chỉ tính COMPONENT.
            WriteKpiBlock(
                sheet,
                items,
                colCount + 2);
        }

        private static List<string> GetSummaryFieldOrder(
            List<DrawingCheckItemResult> items)
        {
            List<string> result =
                new List<string>();

            // TỔNG HỢP chỉ dành cho COMPONENT / Part Drawing.
            string[] preferred =
            {
                "INo ↔ 部品番号 (BOM)",
                "部品番号",
                "W",
                "L",
                "数量",
                "合番合計 ↔ 数量 (BOM)",
                "材質",
                "板厚",
                "合番",
                "部品ファイル名",
                "DXFファイル名",
                "表/ウラ ↔ 展開",
                "品名",
                "現場名",
                "工事番号"
            };

            foreach (string name in preferred)
            {
                if (ContainsField(
                        items,
                        name))
                {
                    result.Add(name);
                }
            }

            // Future-proof: thêm field Part mới, nhưng tuyệt đối không
            // đưa ASM:* vào sheet TỔNG HỢP.
            if (items != null)
            {
                foreach (DrawingCheckItemResult item in items)
                {
                    if (item == null ||
                        item.Fields == null)
                    {
                        continue;
                    }

                    foreach (DrawingBomFieldResult field in item.Fields)
                    {
                        if (field == null ||
                            string.IsNullOrWhiteSpace(
                                field.FieldName) ||
                            IsAssemblyFieldName(
                                field.FieldName))
                        {
                            continue;
                        }

                        if (!ContainsIgnoreCase(
                                result,
                                field.FieldName))
                        {
                            result.Add(
                                field.FieldName);
                        }
                    }
                }
            }

            return result;
        }

        private static List<DrawingCheckItemResult> GetComponentItems(
            List<DrawingCheckItemResult> items)
        {
            List<DrawingCheckItemResult> result =
                new List<DrawingCheckItemResult>();

            if (items == null)
                return result;

            foreach (DrawingCheckItemResult item in items)
            {
                if (item == null)
                    continue;

                if (!IsUnitItem(item))
                    result.Add(item);
            }

            return result;
        }

        private static bool IsUnitItem(
            DrawingCheckItemResult item)
        {
            if (item == null)
                return false;

            // Tin cậy nhất: nhánh Assembly tạo các field ASM:*.
            if (item.Fields != null)
            {
                foreach (DrawingBomFieldResult field in item.Fields)
                {
                    if (field != null &&
                        IsAssemblyFieldName(
                            field.FieldName))
                    {
                        return true;
                    }
                }
            }

            // Fallback cho trường hợp Assembly bị Warning sớm và chưa tạo ASM field.
            string modelPath =
                item.PartPath ?? "";

            if (modelPath.EndsWith(
                    ".SLDASM",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool IsAssemblyFieldName(
            string fieldName)
        {
            return !string.IsNullOrWhiteSpace(fieldName) &&
                   fieldName.StartsWith(
                       "ASM:",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string GetEffectiveSummaryStatus(DrawingCheckItemResult item)
        {
            return (item?.Status ?? DrawingBomCheckStatus.Warning).ToString();
        }

        private static string GetEffectiveFieldStatus(
            string fieldName, DrawingBomFieldResult field)
        {
            return field == null ? "-" : field.Status.ToString();
        }

        private static bool ContainsField(
            List<DrawingCheckItemResult> items,
            string fieldName)
        {
            if (items == null)
                return false;

            foreach (DrawingCheckItemResult item in items)
            {
                if (item == null)
                    continue;

                if (GetField(
                        item.Fields,
                        fieldName) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsIgnoreCase(
            List<string> values,
            string wanted)
        {
            if (values == null)
                return false;

            foreach (string value in values)
            {
                if (string.Equals(
                        value,
                        wanted,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetDisplayFieldName(
            string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                return "";

            const string asmPrefix =
                "ASM:";

            if (fieldName.StartsWith(
                    asmPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return fieldName.Substring(
                    asmPrefix.Length);
            }

            return fieldName;
        }

        private static int CountNgFields(
            DrawingCheckItemResult item)
        {
            if (item == null ||
                item.Fields == null)
            {
                return 0;
            }

            int count = 0;

            foreach (DrawingBomFieldResult field in item.Fields)
            {
                if (field != null &&
                    field.Status ==
                        DrawingBomCheckStatus.NG)
                {
                    count++;
                }
            }

            return count;
        }

        private static void WriteKpiBlock(
            dynamic sheet,
            List<DrawingCheckItemResult> items,
            int startColumn)
        {
            int total =
                items != null
                    ? items.Count
                    : 0;

            int ok = 0;
            int ng = 0;
            int warning = 0;
            int ngFields = 0;

            if (items != null)
            {
                for (int i = 0;
                     i < items.Count;
                     i++)
                {
                    DrawingCheckItemResult item =
                        items[i];

                    if (item == null)
                        continue;

                    string effectiveStatus =
                        GetEffectiveSummaryStatus(
                            item);

                    if (string.Equals(
                            effectiveStatus,
                            "OK",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        ok++;
                    }
                    else if (string.Equals(
                                 effectiveStatus,
                                 "NG",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        ng++;
                    }
                    else
                    {
                        warning++;
                    }

                    ngFields +=
                        CountNgFields(item);
                }
            }

            sheet.Cells[1, startColumn].Value =
                "Tóm tắt";

            sheet.Cells[1, startColumn + 1].Value =
                "Giá trị";

            dynamic kpiHeader =
                sheet.Range[
                    sheet.Cells[1, startColumn],
                    sheet.Cells[1, startColumn + 1]];

            ApplyHeaderFormat(
                kpiHeader);

            object[,] values =
            {
                { "Tổng Drawing", total },
                { "Drawing OK", ok },
                { "Drawing NG", ng },
                { "Drawing Warning", warning },
                { "Tổng field NG", ngFields }
            };

            dynamic range =
                sheet.Range[
                    sheet.Cells[2, startColumn],
                    sheet.Cells[6, startColumn + 1]];

            range.Value = values;

            dynamic all =
                sheet.Range[
                    sheet.Cells[1, startColumn],
                    sheet.Cells[6, startColumn + 1]];

            all.Borders.LineStyle =
                XlContinuous;

            all.VerticalAlignment =
                XlTop;

            sheet.Columns[startColumn].ColumnWidth =
                18.0;

            sheet.Columns[startColumn + 1].ColumnWidth =
                12.0;

            ApplyStatusColor(
                sheet.Cells[3, startColumn + 1],
                ok > 0
                    ? "OK"
                    : "");

            ApplyStatusColor(
                sheet.Cells[4, startColumn + 1],
                ng > 0
                    ? "NG"
                    : "");

            ApplyStatusColor(
                sheet.Cells[5, startColumn + 1],
                warning > 0
                    ? "WARNING"
                    : "");
        }

        // ================================================================
        // DETAIL
        // ================================================================

        private sealed class GroupedDetailBlock
        {
            public int HeaderRow;
            public int FieldHeaderRow;
            public int LastFieldRow;
            public int SeparatorRow;
            public string Status;
        }

        private static void WriteGroupedDetailSheet(
            dynamic sheet,
            List<DrawingCheckItemResult> items)
        {
            const int colCount = 5;
            var rows = new List<object[]>();
            var blocks = new List<GroupedDetailBlock>();
            var statusRanges = new Dictionary<string, List<string>>();
            var ngRows = new List<string>();
            var warningRows = new List<string>();
            rows.Add(new object[] { "CHI TIẾT KIỂM TRA", null, null, null, null });
            rows.Add(new object[colCount]);

            // Build the complete sheet in managed memory. All values, including
            // Japanese text, multiline notes and raw mass values, cross COM once.
            if (items != null)
            {
                foreach (DrawingCheckItemResult item in items)
                {
                    if (item == null)
                        continue;

                    string status = GetEffectiveSummaryStatus(item);
                    string drawingFile = string.IsNullOrWhiteSpace(item.DrawingPath)
                        ? "(Chưa có Drawing)" : Path.GetFileName(item.DrawingPath);
                    string prefix = IsUnitItem(item)
                        ? "UNIT" : "COMPONENT INo " + item.BomItemNumber;
                    var block = new GroupedDetailBlock
                    {
                        HeaderRow = rows.Count + 1,
                        FieldHeaderRow = rows.Count + 2,
                        Status = status
                    };
                    string title = prefix + ": " + (item.Component ?? "") +
                        "    |    Drawing: " + drawingFile +
                        "    |    Kết quả: " + status +
                        "    |    Số lỗi NG: " + CountNgFields(item);
                    rows.Add(new object[] { title, null, null, null, null });
                    rows.Add(new object[] { "Field", "Drawing Value",
                        "Expected / BOM / Assembly", "Status", "Ghi chú" });

                    if (item.Fields != null && item.Fields.Count > 0)
                    {
                        foreach (DrawingBomFieldResult field in item.Fields)
                        {
                            if (field == null)
                                continue;
                            string fieldStatus = GetEffectiveFieldStatus(field.FieldName, field);
                            int row = rows.Count + 1;
                            rows.Add(new object[] { GetDisplayFieldName(field.FieldName),
                                field.DrawingValue ?? "", field.BomValue ?? "",
                                fieldStatus, field.Message ?? "" });
                            AddStatusRange(statusRanges, fieldStatus, "D" + row);
                            if (string.Equals(fieldStatus, "NG", StringComparison.OrdinalIgnoreCase))
                                ngRows.Add("A" + row + ":E" + row);
                            else if (string.Equals(fieldStatus, "Warning", StringComparison.OrdinalIgnoreCase))
                                warningRows.Add("A" + row + ":E" + row);
                        }
                    }
                    else
                    {
                        int row = rows.Count + 1;
                        rows.Add(new object[] { "(Không có field)", item.Note ?? "",
                            null, item.Status.ToString(), null });
                        AddStatusRange(statusRanges, item.Status.ToString(), "D" + row);
                    }

                    block.LastFieldRow = rows.Count;
                    block.SeparatorRow = rows.Count + 1;
                    rows.Add(new object[colCount]);
                    blocks.Add(block);
                }
            }
            while (rows.Count < 3)
                rows.Add(new object[colCount]);

            object[,] data = new object[rows.Count, colCount];
            for (int row = 0; row < rows.Count; row++)
                for (int col = 0; col < colCount; col++)
                    data[row, col] = rows[row][col];
            sheet.Range["A1:E" + rows.Count].Value2 = data;

            var blockRanges = new List<string>();
            var emptyBlockBorders = new List<string>();
            var fieldHeaders = new List<string>();
            var separators = new List<string>();
            var unitHeaders = new Dictionary<string, List<string>>();
            foreach (GroupedDetailBlock block in blocks)
            {
                blockRanges.Add("A" + block.HeaderRow + ":E" + block.LastFieldRow);
                // Preserve the legacy border when a nonempty field list
                // contains only null entries.
                if (block.LastFieldRow == block.FieldHeaderRow)
                    emptyBlockBorders.Add("A" + block.SeparatorRow + ":E" + block.SeparatorRow);
                fieldHeaders.Add("A" + block.FieldHeaderRow + ":E" + block.FieldHeaderRow);
                separators.Add("A" + block.SeparatorRow);
                AddStatusRange(unitHeaders, block.Status,
                    "A" + block.HeaderRow + ":E" + block.HeaderRow);
            }

            ForEachExcelRange((object)sheet, blockRanges, range =>
            {
                range.Borders.LineStyle = XlContinuous;
                range.WrapText = true;
            });
            ForEachExcelRange((object)sheet, emptyBlockBorders,
                range => range.Borders.LineStyle = XlContinuous);
            ForEachExcelRange((object)sheet, fieldHeaders, range =>
            {
                range.Font.Bold = true;
                range.Font.Color = Rgb(255, 255, 255);
                range.Interior.Color = Rgb(91, 155, 213);
                range.HorizontalAlignment = XlCenter;
                range.VerticalAlignment = XlCenter;
                range.EntireRow.RowHeight = 28.0;
            });
            foreach (KeyValuePair<string, List<string>> entry in unitHeaders)
            {
                string status = entry.Key;
                ForEachExcelRange((object)sheet, entry.Value, range =>
                {
                    // Across merges each heading row, keeping blocks separate.
                    range.Merge(true);
                    range.Font.Bold = true;
                    range.Font.Color = Rgb(255, 255, 255);
                    range.HorizontalAlignment = -4131; // xlLeft
                    range.VerticalAlignment = XlCenter;
                    range.WrapText = false;
                    ApplyUnitHeaderColor(range, status);
                    range.EntireRow.RowHeight = 24.0;
                });
            }
            ForEachExcelRange((object)sheet, separators,
                range => range.EntireRow.RowHeight = 8.0);
            ForEachExcelRange((object)sheet, ngRows,
                range => range.Interior.Color = Rgb(252, 232, 230));
            ForEachExcelRange((object)sheet, warningRows,
                range => range.Interior.Color = Rgb(255, 242, 204));
            ApplyBatchedStatusColors((object)sheet, statusRanges);

            dynamic mainTitle = sheet.Range["A1:E1"];
            mainTitle.Merge();
            mainTitle.Font.Bold = true;
            mainTitle.Font.Size = 14;
            mainTitle.Font.Color = Rgb(255, 255, 255);
            mainTitle.Interior.Color = Rgb(31, 78, 120);
            mainTitle.HorizontalAlignment = XlCenter;
            mainTitle.VerticalAlignment = XlCenter;
            sheet.Rows[1].RowHeight = 28.0;

            sheet.Columns[1].ColumnWidth = 22.0;
            sheet.Columns[2].ColumnWidth = 24.0;
            sheet.Columns[3].ColumnWidth = 27.0;
            sheet.Columns[4].ColumnWidth = 11.0;
            sheet.Columns[5].ColumnWidth = 60.0;
            sheet.Range["A1:E" + rows.Count].VerticalAlignment = XlTop;
            sheet.Range["A3:A" + rows.Count].HorizontalAlignment = XlCenter;
            sheet.Range["D3:D" + rows.Count].HorizontalAlignment = XlCenter;
            // Grouped blocks intentionally have no AutoFilter.
        }

        private static string ColumnName(int column)
        {
            string name = "";
            while (column > 0)
            {
                column--;
                name = (char)('A' + column % 26) + name;
                column /= 26;
            }
            return name;
        }

        private static string RangeAddress(int firstRow, int firstColumn, int lastRow, int lastColumn)
        {
            string first = ColumnName(firstColumn) + firstRow;
            string last = ColumnName(lastColumn) + lastRow;
            return first == last ? first : first + ":" + last;
        }

        private static void AddStatusRange(Dictionary<string, List<string>> ranges,
            string status, string address)
        {
            string key = (status ?? "").Trim().ToUpperInvariant();
            if (key != "OK" && key != "NG" && key != "WARNING")
                return;
            List<string> addresses;
            if (!ranges.TryGetValue(key, out addresses))
            {
                addresses = new List<string>();
                ranges.Add(key, addresses);
            }
            addresses.Add(address);
        }

        private static void ApplyBatchedStatusColors(object sheet,
            Dictionary<string, List<string>> ranges)
        {
            foreach (KeyValuePair<string, List<string>> entry in ranges)
            {
                string status = entry.Key;
                ForEachExcelRange(sheet, entry.Value, range => ApplyStatusColor(range, status));
            }
        }

        private static void ForEachExcelRange(object sheetObject, List<string> addresses,
            Action<dynamic> apply)
        {
            if (addresses.Count == 0)
                return;
            dynamic sheet = sheetObject;
            string batch = "";
            foreach (string address in addresses)
            {
                // Keep each comma-separated address short for Excel's Range parser.
                if (batch.Length > 0 && batch.Length + address.Length + 1 > 200)
                {
                    ApplyExcelRange((object)sheet, batch, apply);
                    batch = "";
                }
                batch += (batch.Length == 0 ? "" : ",") + address;
            }
            if (batch.Length > 0)
                ApplyExcelRange((object)sheet, batch, apply);
        }

        private static void ApplyExcelRange(object sheetObject, string address, Action<dynamic> apply)
        {
            dynamic range = ((dynamic)sheetObject).Range[address];
            try { apply(range); }
            finally { ReleaseExcelObject((object)range); }
        }


        private static void ApplyUnitHeaderColor(
            dynamic range,
            string status)
        {
            if (range == null)
                return;

            string normalized =
                (status ?? "")
                .Trim()
                .ToUpperInvariant();

            if (normalized == "NG")
            {
                range.Interior.Color =
                    Rgb(192, 80, 77);
            }
            else if (normalized == "WARNING")
            {
                range.Interior.Color =
                    Rgb(191, 144, 0);
            }
            else
            {
                range.Interior.Color =
                    Rgb(84, 130, 53);
            }
        }

        private static void WriteDetailSheet(
            dynamic sheet,
            List<DrawingCheckItemResult> items)
        {
            string[] headers =
            {
                "Component",
                "Drawing File",
                "Field",
                "Drawing Value",
                "Expected / BOM / Assembly",
                "Status",
                "Source",
                "Ghi chú",
                "X",
                "Y"
            };

            WriteHeaders(
                sheet,
                headers);

            List<DetailRow> rows =
                BuildDetailRows(
                    items);

            int rowCount =
                rows.Count;

            int colCount =
                headers.Length;

            if (rowCount > 0)
            {
                object[,] data =
                    new object[rowCount, colCount];

                for (int i = 0;
                     i < rowCount;
                     i++)
                {
                    DetailRow row =
                        rows[i];

                    data[i, 0] =
                        row.Component;

                    data[i, 1] =
                        row.DrawingFile;

                    data[i, 2] =
                        row.FieldName;

                    data[i, 3] =
                        row.DrawingValue;

                    data[i, 4] =
                        row.ExpectedValue;

                    data[i, 5] =
                        row.Status;

                    data[i, 6] =
                        row.Source;

                    data[i, 7] =
                        row.Message;

                    data[i, 8] =
                        row.HasLocation
                            ? (object)row.X
                            : "";

                    data[i, 9] =
                        row.HasLocation
                            ? (object)row.Y
                            : "";
                }

                dynamic dataRange =
                    sheet.Range[
                        sheet.Cells[2, 1],
                        sheet.Cells[
                            rowCount + 1,
                            colCount]];

                dataRange.Value = data;

                // Coordinate format.
                dynamic coordinateRange =
                    sheet.Range[
                        sheet.Cells[2, 9],
                        sheet.Cells[
                            rowCount + 1,
                            10]];

                coordinateRange.NumberFormat =
                    "0.000000";

                // Color status + light tint NG row.
                for (int i = 0;
                     i < rowCount;
                     i++)
                {
                    int excelRow =
                        i + 2;

                    string status =
                        rows[i].Status;

                    if (string.Equals(
                            status,
                            "NG",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        dynamic fullRow =
                            sheet.Range[
                                sheet.Cells[excelRow, 1],
                                sheet.Cells[excelRow, colCount]];

                        fullRow.Interior.Color =
                            Rgb(
                                252,
                                232,
                                230);
                    }

                    ApplyStatusColor(
                        sheet.Cells[
                            excelRow,
                            6],
                        status);
                }
            }

            int lastRow =
                Math.Max(
                    2,
                    rowCount + 1);

            FinishDetailSheet(
                sheet,
                lastRow,
                colCount);
        }

        private static List<DetailRow> BuildDetailRows(
            List<DrawingCheckItemResult> items)
        {
            List<DetailRow> rows =
                new List<DetailRow>();

            if (items == null)
                return rows;

            foreach (DrawingCheckItemResult item in items)
            {
                if (item == null)
                    continue;

                string drawingFile =
                    string.IsNullOrWhiteSpace(
                        item.DrawingPath)
                        ? "(Chưa có Drawing)"
                        : Path.GetFileName(
                            item.DrawingPath);

                if (item.Fields != null &&
                    item.Fields.Count > 0)
                {
                    foreach (DrawingBomFieldResult field in item.Fields)
                    {
                        if (field == null)
                            continue;

                        rows.Add(
                            new DetailRow
                            {
                                Component =
                                    item.Component ?? "",

                                DrawingFile =
                                    drawingFile,

                                FieldName =
                                    GetDisplayFieldName(
                                        field.FieldName),

                                DrawingValue =
                                    field.DrawingValue ?? "",

                                ExpectedValue =
                                    field.BomValue ?? "",

                                Status =
                                    field.Status.ToString(),

                                Source =
                                    field.Source ?? "",

                                Message =
                                    field.Message ?? "",

                                HasLocation =
                                    field.HasDrawingLocation,

                                X =
                                    field.DrawingX,

                                Y =
                                    field.DrawingY
                            });
                    }
                }
                else
                {
                    // Still show the Drawing in detail even when no field
                    // was generated (missing Drawing, early warning, etc.).
                    rows.Add(
                        new DetailRow
                        {
                            Component =
                                item.Component ?? "",

                            DrawingFile =
                                drawingFile,

                            FieldName =
                                "(Không có field)",

                            DrawingValue =
                                item.Note ?? "",

                            ExpectedValue =
                                "",

                            Status =
                                item.Status.ToString(),

                            Source =
                                "",

                            Message =
                                item.Note ?? "",

                            HasLocation =
                                false
                        });
                }
            }

            return rows;
        }

        private sealed class DetailRow
        {
            public string Component { get; set; } = "";
            public string DrawingFile { get; set; } = "";
            public string FieldName { get; set; } = "";
            public string DrawingValue { get; set; } = "";
            public string ExpectedValue { get; set; } = "";
            public string Status { get; set; } = "";
            public string Source { get; set; } = "";
            public string Message { get; set; } = "";
            public bool HasLocation { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
        }

        // ================================================================
        // EXCEL FORMATTING
        // ================================================================

        private static void PrepareSheetCount(
            dynamic excel,
            dynamic workbook,
            int requiredSheetCount)
        {
            requiredSheetCount =
                Math.Max(
                    1,
                    requiredSheetCount);

            while (workbook.Sheets.Count <
                   requiredSheetCount)
            {
                workbook.Sheets.Add(
                    After:
                        workbook.Sheets[
                            workbook.Sheets.Count]);
            }

            if (workbook.Sheets.Count <=
                requiredSheetCount)
            {
                return;
            }

            bool oldDisplayAlerts =
                excel.DisplayAlerts;

            try
            {
                excel.DisplayAlerts =
                    false;

                while (workbook.Sheets.Count >
                       requiredSheetCount)
                {
                    workbook.Sheets[
                        workbook.Sheets.Count]
                        .Delete();
                }
            }
            finally
            {
                excel.DisplayAlerts =
                    oldDisplayAlerts;
            }
        }

        private static void WriteHeaders(
            dynamic sheet,
            string[] headers)
        {
            object[,] values = new object[1, headers.Length];
            for (int i = 0;
                 i < headers.Length;
                 i++)
            {
                values[0, i] = headers[i];
            }

            dynamic header = sheet.Range[RangeAddress(1, 1, 1, headers.Length)];
            header.Value2 = values;

            ApplyHeaderFormat(
                header);
        }

        private static void ApplyHeaderFormat(
            dynamic header)
        {
            if (header == null)
                return;

            header.Font.Bold =
                true;

            header.Font.Color =
                Rgb(
                    255,
                    255,
                    255);

            header.Interior.Color =
                Rgb(
                    31,
                    78,
                    120);

            header.HorizontalAlignment =
                XlCenter;

            header.VerticalAlignment =
                XlCenter;

            header.WrapText =
                true;
        }

        private static void FinishSummarySheet(
            dynamic sheet,
            int lastRow,
            int lastColumn)
        {
            FinishCommonSheet(
                sheet,
                lastRow,
                lastColumn);

            // 1=№, 2=Component, 3=Drawing File, 4=Kết quả, 5=Số lỗi NG.
            sheet.Columns[1].ColumnWidth =
                6.0;

            sheet.Columns[2].ColumnWidth =
                22.0;

            sheet.Columns[3].ColumnWidth =
                28.0;

            sheet.Columns[4].ColumnWidth =
                12.0;

            sheet.Columns[5].ColumnWidth =
                12.0;

            if (lastColumn >= 6)
                sheet.Range["F:" + ColumnName(lastColumn)].ColumnWidth = 16.0;

            // №, Kết quả, Số lỗi NG và các field status căn giữa.
            sheet.Range[
                sheet.Cells[2, 1],
                sheet.Cells[lastRow, 1]]
                .HorizontalAlignment =
                    XlCenter;

            dynamic statusRange =
                sheet.Range[
                    sheet.Cells[2, 4],
                    sheet.Cells[
                        lastRow,
                        lastColumn]];

            statusRange.HorizontalAlignment =
                XlCenter;
        }

        private static void FinishDetailSheet(
            dynamic sheet,
            int lastRow,
            int lastColumn)
        {
            FinishCommonSheet(
                sheet,
                lastRow,
                lastColumn);

            sheet.Columns[1].ColumnWidth =
                22.0;

            sheet.Columns[2].ColumnWidth =
                28.0;

            sheet.Columns[3].ColumnWidth =
                20.0;

            sheet.Columns[4].ColumnWidth =
                22.0;

            sheet.Columns[5].ColumnWidth =
                24.0;

            sheet.Columns[6].ColumnWidth =
                10.0;

            sheet.Columns[7].ColumnWidth =
                28.0;

            sheet.Columns[8].ColumnWidth =
                48.0;

            sheet.Columns[9].ColumnWidth =
                13.0;

            sheet.Columns[10].ColumnWidth =
                13.0;

            sheet.Range[
                sheet.Cells[2, 3],
                sheet.Cells[lastRow, 3]]
                .HorizontalAlignment =
                    XlCenter;

            sheet.Range[
                sheet.Cells[2, 6],
                sheet.Cells[lastRow, 7]]
                .HorizontalAlignment =
                    XlCenter;

            sheet.Range[
                sheet.Cells[2, 9],
                sheet.Cells[lastRow, 10]]
                .HorizontalAlignment =
                    XlCenter;
        }

        private static void FinishCommonSheet(
            dynamic sheet,
            int lastRow,
            int lastColumn)
        {
            dynamic used =
                sheet.Range[
                    sheet.Cells[1, 1],
                    sheet.Cells[
                        lastRow,
                        lastColumn]];

            used.Borders.LineStyle =
                XlContinuous;

            used.VerticalAlignment =
                XlTop;

            used.WrapText =
                true;

            used.AutoFilter();

            sheet.Rows[1].RowHeight =
                30.0;

            dynamic dataRows = sheet.Range["A2:A" + lastRow].EntireRow;
            dataRows.AutoFit();
            object commonHeight = dataRows.RowHeight;
            if (commonHeight != null && commonHeight != DBNull.Value)
            {
                double height = Convert.ToDouble(commonHeight);
                if (height > 42.0)
                    dataRows.RowHeight = 42.0;
                else if (height < 18.0)
                    dataRows.RowHeight = 18.0;
            }
            else
            {
                // Mixed row heights still need their individual limits, but
                // AutoFit and height assignments are performed in batches.
                var shortRows = new List<string>();
                var tallRows = new List<string>();
                for (int row = 2; row <= lastRow; row++)
                {
                    dynamic excelRow = sheet.Rows[row];
                    try
                    {
                        double height = Convert.ToDouble(excelRow.RowHeight);
                        if (height > 42.0) tallRows.Add("A" + row);
                        else if (height < 18.0) shortRows.Add("A" + row);
                    }
                    finally { ReleaseExcelObject((object)excelRow); }
                }
                ForEachExcelRange((object)sheet, tallRows,
                    range => range.EntireRow.RowHeight = 42.0);
                ForEachExcelRange((object)sheet, shortRows,
                    range => range.EntireRow.RowHeight = 18.0);
            }
            ReleaseExcelObject((object)dataRows);
        }

        private static void FreezeTopRow(
            dynamic sheet)
        {
            try
            {
                sheet.Activate();
                sheet.Range["A2"].Select();

                dynamic window =
                    sheet.Application.ActiveWindow;

                window.FreezePanes =
                    false;

                window.SplitRow =
                    1;

                window.FreezePanes =
                    true;
            }
            catch
            {
            }
        }

        private static void ApplyStatusColor(
            dynamic range,
            string status)
        {
            if (range == null ||
                string.IsNullOrWhiteSpace(status))
            {
                return;
            }

            string normalized =
                status
                .Trim()
                .ToUpperInvariant();

            if (normalized == "NG")
            {
                range.Interior.Color =
                    Rgb(
                        244,
                        204,
                        204);

                range.Font.Color =
                    Rgb(
                        156,
                        0,
                        6);

                range.Font.Bold =
                    true;
            }
            else if (normalized == "OK")
            {
                range.Interior.Color =
                    Rgb(
                        217,
                        234,
                        211);

                range.Font.Color =
                    Rgb(
                        39,
                        78,
                        19);
            }
            else if (normalized == "WARNING")
            {
                range.Interior.Color =
                    Rgb(
                        255,
                        242,
                        204);

                range.Font.Color =
                    Rgb(
                        127,
                        96,
                        0);

                range.Font.Bold =
                    true;
            }
        }

        private static DrawingBomFieldResult GetField(
            List<DrawingBomFieldResult> fields,
            string name)
        {
            if (fields == null ||
                string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            foreach (DrawingBomFieldResult field in fields)
            {
                if (field == null)
                    continue;

                if (string.Equals(
                        field.FieldName,
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return field;
                }
            }

            return null;
        }

        private static int Rgb(
            int red,
            int green,
            int blue)
        {
            return
                red +
                (green << 8) +
                (blue << 16);
        }
    }
}
