using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            if (result == null ||
                result.Items == null ||
                result.Items.Count == 0)
            {
                return;
            }

            dynamic excel = null;
            dynamic workbook = null;

            try
            {
                Type excelType =
                    Type.GetTypeFromProgID("Excel.Application");

                if (excelType == null)
                {
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

                workbook =
                    excel.Workbooks.Add();

                PrepareSheetCount(
                    excel,
                    workbook,
                    2);

                Debug.WriteLine("[CHECK DRAWING EXCEL] workbook created");

                dynamic summarySheet =
                    workbook.Sheets[1];

                summarySheet.Name =
                    "TỔNG HỢP";

                dynamic detailSheet =
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

                WriteSummarySheet(
                    summarySheet,
                    componentItems,
                    summaryFields);

                Debug.WriteLine("[CHECK DRAWING EXCEL] write summary done");

                FreezeTopRow(
                    summarySheet);

                Debug.WriteLine("[CHECK DRAWING EXCEL] write detail start");

                WriteGroupedDetailSheet(
                    detailSheet,
                    result.Items);

                Debug.WriteLine("[CHECK DRAWING EXCEL] write detail done");

                FreezeTopRow(
                    detailSheet);

                summarySheet.Activate();
                summarySheet.Range["A1"].Select();

                excel.ScreenUpdating = true;
                excel.DisplayAlerts = true;
                excel.Visible = true;

                Debug.WriteLine("[CHECK DRAWING EXCEL] export complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[CHECK DRAWING EXCEL] ERROR: " +
                    ex.ToString());

                try
                {
                    if (excel != null)
                    {
                        excel.ScreenUpdating = true;
                        excel.DisplayAlerts = true;
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

                dataRange.Value = data;

                // Overall status + field status colors.
                for (int i = 0;
                     i < rowCount;
                     i++)
                {
                    int row =
                        i + 2;

                    DrawingCheckItemResult item =
                        items[i];

                    string overallStatus =
                        GetEffectiveSummaryStatus(
                            item);

                    ApplyStatusColor(
                        sheet.Cells[row, 4],
                        overallStatus);

                    for (int f = 0;
                         f < fieldOrder.Count;
                         f++)
                    {
                        DrawingBomFieldResult field =
                            GetField(
                                item.Fields,
                                fieldOrder[f]);

                        if (field == null)
                            continue;

                        string effectiveFieldStatus =
                            GetEffectiveFieldStatus(
                                fieldOrder[f],
                                field);

                        ApplyStatusColor(
                            sheet.Cells[
                                row,
                                6 + f],
                            effectiveFieldStatus);
                    }
                }
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

        private static void WriteGroupedDetailSheet(
            dynamic sheet,
            List<DrawingCheckItemResult> items)
        {
            const int colCount = 5;

            // Main title.
            dynamic mainTitleRange =
                sheet.Range[
                    sheet.Cells[1, 1],
                    sheet.Cells[1, colCount]];

            mainTitleRange.Merge();

            sheet.Cells[1, 1].Value =
                "CHI TIẾT KIỂM TRA";

            dynamic mainTitle =
                sheet.Range[
                    sheet.Cells[1, 1],
                    sheet.Cells[1, colCount]];

            mainTitle.Font.Bold = true;
            mainTitle.Font.Size = 14;
            mainTitle.Font.Color = Rgb(255, 255, 255);
            mainTitle.Interior.Color = Rgb(31, 78, 120);
            mainTitle.HorizontalAlignment = XlCenter;
            mainTitle.VerticalAlignment = XlCenter;
            sheet.Rows[1].RowHeight = 28.0;

            int currentRow = 3;

            if (items != null)
            {
                for (int itemIndex = 0;
                     itemIndex < items.Count;
                     itemIndex++)
                {
                    DrawingCheckItemResult item =
                        items[itemIndex];

                    if (item == null)
                        continue;

                    string component =
                        item.Component ?? "";

                    string drawingFile =
                        string.IsNullOrWhiteSpace(
                            item.DrawingPath)
                            ? "(Chưa có Drawing)"
                            : Path.GetFileName(
                                item.DrawingPath);

                    bool isUnit =
                        IsUnitItem(item);

                    string effectiveStatus =
                        GetEffectiveSummaryStatus(
                            item);

                    int ngCount =
                        CountNgFields(item);

                    // ----------------------------------------------------
                    // COMPONENT / UNIT HEADER
                    // ----------------------------------------------------
                    int unitHeaderRow =
                        currentRow;

                    dynamic unitHeaderMergeRange =
                        sheet.Range[
                            sheet.Cells[unitHeaderRow, 1],
                            sheet.Cells[unitHeaderRow, colCount]];

                    unitHeaderMergeRange.Merge();

                    string blockPrefix =
                        isUnit
                            ? "UNIT"
                            : "COMPONENT INo " + item.BomItemNumber;

                    string unitTitle =
                        blockPrefix + ": " + component +
                        "    |    Drawing: " + drawingFile +
                        "    |    Kết quả: " + effectiveStatus +
                        "    |    Số lỗi NG: " + ngCount;

                    sheet.Cells[
                        unitHeaderRow,
                        1].Value =
                        unitTitle;

                    dynamic unitHeader =
                        sheet.Range[
                            sheet.Cells[unitHeaderRow, 1],
                            sheet.Cells[unitHeaderRow, colCount]];

                    unitHeader.Font.Bold = true;
                    unitHeader.Font.Color =
                        Rgb(255, 255, 255);

                    unitHeader.VerticalAlignment =
                        XlCenter;

                    unitHeader.HorizontalAlignment =
                        -4131; // xlLeft

                    unitHeader.WrapText =
                        false;

                    ApplyUnitHeaderColor(
                        unitHeader,
                        effectiveStatus);

                    unitHeader.Borders.LineStyle =
                        XlContinuous;

                    sheet.Rows[
                        unitHeaderRow].RowHeight =
                        24.0;

                    currentRow++;

                    // ----------------------------------------------------
                    // FIELD HEADER
                    // ----------------------------------------------------
                    string[] detailHeaders =
                    {
                        "Field",
                        "Drawing Value",
                        "Expected / BOM / Assembly",
                        "Status",
                        "Ghi chú"
                    };

                    for (int c = 0;
                         c < detailHeaders.Length;
                         c++)
                    {
                        sheet.Cells[
                            currentRow,
                            c + 1].Value =
                            detailHeaders[c];
                    }

                    dynamic fieldHeader =
                        sheet.Range[
                            sheet.Cells[currentRow, 1],
                            sheet.Cells[currentRow, colCount]];

                    fieldHeader.Font.Bold =
                        true;

                    fieldHeader.Font.Color =
                        Rgb(255, 255, 255);

                    fieldHeader.Interior.Color =
                        Rgb(91, 155, 213);

                    fieldHeader.HorizontalAlignment =
                        XlCenter;

                    fieldHeader.VerticalAlignment =
                        XlCenter;

                    fieldHeader.Borders.LineStyle =
                        XlContinuous;

                    fieldHeader.WrapText =
                        true;

                    sheet.Rows[
                        currentRow].RowHeight =
                        28.0;

                    currentRow++;

                    int firstFieldRow =
                        currentRow;

                    // ----------------------------------------------------
                    // FIELD ROWS
                    // ----------------------------------------------------
                    if (item.Fields != null &&
                        item.Fields.Count > 0)
                    {
                        foreach (DrawingBomFieldResult field in item.Fields)
                        {
                            if (field == null)
                                continue;

                            sheet.Cells[
                                currentRow,
                                1].Value =
                                GetDisplayFieldName(
                                    field.FieldName);

                            sheet.Cells[
                                currentRow,
                                2].Value =
                                field.DrawingValue ?? "";

                            sheet.Cells[
                                currentRow,
                                3].Value =
                                field.BomValue ?? "";

                            string displayedFieldStatus =
                                GetEffectiveFieldStatus(
                                    field.FieldName,
                                    field);

                            sheet.Cells[
                                currentRow,
                                4].Value =
                                displayedFieldStatus;

                            sheet.Cells[
                                currentRow,
                                5].Value =
                                field.Message ?? "";

                            dynamic rowRange =
                                sheet.Range[
                                    sheet.Cells[currentRow, 1],
                                    sheet.Cells[currentRow, colCount]];

                            rowRange.Borders.LineStyle =
                                XlContinuous;

                            rowRange.VerticalAlignment =
                                XlTop;

                            rowRange.WrapText =
                                true;

                            if (string.Equals(
                                    displayedFieldStatus,
                                    "NG",
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                rowRange.Interior.Color =
                                    Rgb(252, 232, 230);
                            }
                            else if (string.Equals(
                                         displayedFieldStatus,
                                         "Warning",
                                         StringComparison.OrdinalIgnoreCase))
                            {
                                rowRange.Interior.Color =
                                    Rgb(255, 242, 204);
                            }

                            ApplyStatusColor(
                                sheet.Cells[currentRow, 4],
                                displayedFieldStatus);

                            currentRow++;
                        }
                    }
                    else
                    {
                        sheet.Cells[
                            currentRow,
                            1].Value =
                            "(Không có field)";

                        sheet.Cells[
                            currentRow,
                            2].Value =
                            item.Note ?? "";

                        sheet.Cells[
                            currentRow,
                            4].Value =
                            item.Status.ToString();

                        dynamic noFieldRow =
                            sheet.Range[
                                sheet.Cells[currentRow, 1],
                                sheet.Cells[currentRow, colCount]];

                        noFieldRow.Borders.LineStyle =
                            XlContinuous;

                        noFieldRow.WrapText =
                            true;

                        ApplyStatusColor(
                            sheet.Cells[currentRow, 4],
                            item.Status.ToString());

                        currentRow++;
                    }

                    int lastFieldRow =
                        currentRow - 1;

                    // Strong outside border around the Unit field block.
                    try
                    {
                        dynamic block =
                            sheet.Range[
                                sheet.Cells[firstFieldRow, 1],
                                sheet.Cells[lastFieldRow, colCount]];

                        block.Borders.LineStyle =
                            XlContinuous;
                    }
                    catch
                    {
                    }

                    // Blank separator row makes Unit boundaries obvious.
                    sheet.Rows[currentRow].RowHeight =
                        8.0;

                    currentRow++;
                }
            }

            int lastRow =
                Math.Max(
                    3,
                    currentRow - 1);

            // ------------------------------------------------------------
            // COLUMN WIDTHS
            // ------------------------------------------------------------
            sheet.Columns[1].ColumnWidth = 22.0;
            sheet.Columns[2].ColumnWidth = 24.0;
            sheet.Columns[3].ColumnWidth = 27.0;
            sheet.Columns[4].ColumnWidth = 11.0;
            sheet.Columns[5].ColumnWidth = 60.0;

            sheet.Range[
                sheet.Cells[1, 1],
                sheet.Cells[lastRow, colCount]]
                .VerticalAlignment =
                    XlTop;

            // Center Field and Status for readability.
            sheet.Range[
                sheet.Cells[3, 1],
                sheet.Cells[lastRow, 1]]
                .HorizontalAlignment =
                    XlCenter;

            sheet.Range[
                sheet.Cells[3, 4],
                sheet.Cells[lastRow, 4]]
                .HorizontalAlignment =
                    XlCenter;

            // No AutoFilter here intentionally:
            // this sheet is formatted as Unit blocks rather than one flat table.
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
            for (int i = 0;
                 i < headers.Length;
                 i++)
            {
                sheet.Cells[
                    1,
                    i + 1].Value =
                    headers[i];
            }

            dynamic header =
                sheet.Range[
                    sheet.Cells[1, 1],
                    sheet.Cells[
                        1,
                        headers.Length]];

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

            for (int col = 6;
                 col <= lastColumn;
                 col++)
            {
                sheet.Columns[col].ColumnWidth =
                    16.0;
            }

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

            for (int row = 2;
                 row <= lastRow;
                 row++)
            {
                dynamic excelRow =
                    sheet.Rows[row];

                excelRow.AutoFit();

                double height =
                    Convert.ToDouble(
                        excelRow.RowHeight);

                if (height > 42.0)
                    excelRow.RowHeight = 42.0;
                else if (height < 18.0)
                    excelRow.RowHeight = 18.0;
            }
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
