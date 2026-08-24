using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ADDIN.Commands
{
    internal static class ExcelDrawingCheckExporter
    {
        public static void Export(DrawingBatchCheckResult result)
        {
            if (result == null || result.Items == null || result.Items.Count == 0)
                return;

            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    MessageBox.Show(
                        "Không tìm thấy Microsoft Excel trên máy tính.",
                        "CHECK DRAWING — XUẤT EXCEL",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                dynamic excel = Activator.CreateInstance(excelType);

                excel.ScreenUpdating = false;
                excel.DisplayAlerts = false;

                dynamic workbook = excel.Workbooks.Add();

                PrepareSheetCount(excel, workbook, 2);

                dynamic summarySheet = workbook.Sheets[1];
                summarySheet.Name = "TONG HOP";

                dynamic detailSheet = workbook.Sheets[2];
                detailSheet.Name = "CHECK DRAWING";

                WriteSummarySheet(summarySheet, result.Items);
                FreezeTopRow(summarySheet);

                WriteDetailSheet(detailSheet, result.Items);
                FreezeTopRow(detailSheet);

                summarySheet.Activate();
                summarySheet.Range["A1"].Select();

                excel.ScreenUpdating = true;
                excel.DisplayAlerts = true;
                excel.Visible = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Đã hoàn tất CHECK DRAWING nhưng xảy ra lỗi khi xuất Excel:\n" + ex.Message,
                    "CHECK DRAWING — LỖI XUẤT EXCEL",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void PrepareSheetCount(dynamic excel, dynamic workbook, int requiredSheetCount)
        {
            requiredSheetCount = Math.Max(1, requiredSheetCount);
            while (workbook.Sheets.Count < requiredSheetCount)
                workbook.Sheets.Add(After: workbook.Sheets[workbook.Sheets.Count]);

            if (workbook.Sheets.Count <= requiredSheetCount)
                return;

            bool oldDisplayAlerts = excel.DisplayAlerts;
            try
            {
                excel.DisplayAlerts = false;
                while (workbook.Sheets.Count > requiredSheetCount)
                    workbook.Sheets[workbook.Sheets.Count].Delete();
            }
            finally
            {
                excel.DisplayAlerts = oldDisplayAlerts;
            }
        }

        private static void WriteSummarySheet(dynamic sheet, List<DrawingCheckItemResult> items)
        {
            string[] headers =
            {
                "部品番号",
                "Component",
                "CHECK DRAWING",
                "Tình trạng xử lý",
                "Ghi chú"
            };
            WriteHeaders(sheet, headers);

            int rowCount = items.Count;
            int colCount = headers.Length;

            if (rowCount > 0)
            {
                object[,] data = new object[rowCount, colCount];
                for (int i = 0; i < rowCount; i++)
                {
                    var item = items[i];
                    data[i, 0] = item.PartNumber ?? "";
                    data[i, 1] = item.Component ?? "";
                    data[i, 2] = item.Status.ToString();
                    data[i, 3] = "ĐÃ XỬ LÝ";
                    data[i, 4] = item.Note ?? "";
                }

                dynamic dataRange = sheet.Range[sheet.Cells[2, 1], sheet.Cells[rowCount + 1, colCount]];
                dataRange.Value = data;

                for (int i = 0; i < rowCount; i++)
                {
                    int r = i + 2;
                    if (items[i].Status == DrawingBomCheckStatus.NG)
                        ApplyStatusColor(sheet.Range["A" + r + ":E" + r], "NG");
                    else if (items[i].Status == DrawingBomCheckStatus.Warning)
                        ApplyStatusColor(sheet.Range["A" + r + ":E" + r], "WARNING");
                }
            }

            int lastRow = Math.Max(2, rowCount + 1);
            FinishSheet(sheet, lastRow, colCount);
            AutoFitNoteColumn(sheet, lastRow, 5);
        }

        private static void WriteDetailSheet(dynamic sheet, List<DrawingCheckItemResult> items)
        {
            string[] headers =
            {
                "部品番号",
                "Component",
                "Drawing File",

                "部品番号 BOM",
                "部品番号 Drawing",
                "部品番号 Status",

                "W BOM",
                "W Drawing",
                "W Status",

                "L BOM",
                "L Drawing",
                "L Status",

                "数量 BOM",
                "数量 Drawing",
                "数量 Source",
                "数量 Status",

                "材質 BOM",
                "材質 Drawing",
                "材質 Status",

                "板厚 BOM",
                "板厚 Drawing",
                "板厚 Status",

                "合番 BOM",
                "合番 Drawing",
                "合番 Status",

                "部品ファイル名 BOM",
                "部品ファイル名 Drawing",
                "部品ファイル名 Status",

                "DXFファイル名 BOM",
                "DXFファイル名 Drawing",
                "DXFファイル名 Status",

                "品名 BOM",
                "品名 Drawing",
                "品名 Status",

                "現場名 BOM",
                "現場名 Drawing",
                "現場名 Status",

                "工事番号 BOM",
                "工事番号 Drawing",
                "工事番号 Status",

                "Overall",
                "Ghi chú"
            };
            WriteHeaders(sheet, headers);

            int rowCount = items.Count;
            int colCount = headers.Length;

            if (rowCount > 0)
            {
                object[,] data = new object[rowCount, colCount];
                for (int i = 0; i < rowCount; i++)
                {
                    var item = items[i];
                    string drwFileName = string.IsNullOrWhiteSpace(item.DrawingPath)
                        ? "(Chưa có Drawing)"
                        : Path.GetFileName(item.DrawingPath);

                    DrawingBomFieldResult fPart = GetField(item.Fields, "部品番号");
                    DrawingBomFieldResult fW = GetField(item.Fields, "W");
                    DrawingBomFieldResult fL = GetField(item.Fields, "L");
                    DrawingBomFieldResult fQty = GetField(item.Fields, "数量");
                    DrawingBomFieldResult fMat = GetField(item.Fields, "材質");
                    DrawingBomFieldResult fThk = GetField(item.Fields, "板厚");
                    DrawingBomFieldResult fGoban = GetField(item.Fields, "合番");
                    DrawingBomFieldResult fFile = GetField(item.Fields, "部品ファイル名");
                    DrawingBomFieldResult fDxf = GetField(item.Fields, "DXFファイル名");
                    DrawingBomFieldResult fProd = GetField(item.Fields, "品名");
                    DrawingBomFieldResult fSite = GetField(item.Fields, "現場名");
                    DrawingBomFieldResult fJob = GetField(item.Fields, "工事番号");

                    data[i, 0] = item.PartNumber ?? "";
                    data[i, 1] = item.Component ?? "";
                    data[i, 2] = drwFileName;

                    // 部品番号
                    data[i, 3] = fPart != null ? fPart.BomValue : "";
                    data[i, 4] = fPart != null ? fPart.DrawingValue : "";
                    data[i, 5] = fPart != null ? fPart.Status.ToString() : "-";

                    // W
                    data[i, 6] = fW != null ? fW.BomValue : "";
                    data[i, 7] = fW != null ? fW.DrawingValue : "";
                    data[i, 8] = fW != null ? fW.Status.ToString() : "-";

                    // L
                    data[i, 9] = fL != null ? fL.BomValue : "";
                    data[i, 10] = fL != null ? fL.DrawingValue : "";
                    data[i, 11] = fL != null ? fL.Status.ToString() : "-";

                    // 数量
                    data[i, 12] = fQty != null ? fQty.BomValue : "";
                    data[i, 13] = fQty != null ? fQty.DrawingValue : "";
                    data[i, 14] = fQty != null ? fQty.Source : "";
                    data[i, 15] = fQty != null ? fQty.Status.ToString() : "-";

                    // 材質
                    data[i, 16] = fMat != null ? fMat.BomValue : "";
                    data[i, 17] = fMat != null ? fMat.DrawingValue : "";
                    data[i, 18] = fMat != null ? fMat.Status.ToString() : "-";

                    // 板厚
                    data[i, 19] = fThk != null ? fThk.BomValue : "";
                    data[i, 20] = fThk != null ? fThk.DrawingValue : "";
                    data[i, 21] = fThk != null ? fThk.Status.ToString() : "-";

                    // 合番
                    data[i, 22] = fGoban != null ? fGoban.BomValue : "";
                    data[i, 23] = fGoban != null ? fGoban.DrawingValue : "";
                    data[i, 24] = fGoban != null ? fGoban.Status.ToString() : "-";

                    // 部品ファイル名
                    data[i, 25] = fFile != null ? fFile.BomValue : "";
                    data[i, 26] = fFile != null ? fFile.DrawingValue : "";
                    data[i, 27] = fFile != null ? fFile.Status.ToString() : "-";

                    // DXFファイル名
                    data[i, 28] = fDxf != null ? fDxf.BomValue : "";
                    data[i, 29] = fDxf != null ? fDxf.DrawingValue : "";
                    data[i, 30] = fDxf != null ? fDxf.Status.ToString() : "-";

                    // 品名
                    data[i, 31] = fProd != null ? fProd.BomValue : "";
                    data[i, 32] = fProd != null ? fProd.DrawingValue : "";
                    data[i, 33] = fProd != null ? fProd.Status.ToString() : "-";

                    // 現場名
                    data[i, 34] = fSite != null ? fSite.BomValue : "";
                    data[i, 35] = fSite != null ? fSite.DrawingValue : "";
                    data[i, 36] = fSite != null ? fSite.Status.ToString() : "-";

                    // 工事番号
                    data[i, 37] = fJob != null ? fJob.BomValue : "";
                    data[i, 38] = fJob != null ? fJob.DrawingValue : "";
                    data[i, 39] = fJob != null ? fJob.Status.ToString() : "-";

                    // Overall & Ghi chú
                    data[i, 40] = item.Status.ToString();
                    data[i, 41] = item.Note ?? "";
                }

                dynamic dataRange = sheet.Range[sheet.Cells[2, 1], sheet.Cells[rowCount + 1, colCount]];
                dataRange.Value = data;

                for (int i = 0; i < rowCount; i++)
                {
                    int r = i + 2;
                    var item = items[i];

                    if (item.Status == DrawingBomCheckStatus.NG)
                        ApplyStatusColor(sheet.Range[sheet.Cells[r, 1], sheet.Cells[r, colCount]], "NG");
                    else if (item.Status == DrawingBomCheckStatus.Warning)
                        ApplyStatusColor(sheet.Range[sheet.Cells[r, 1], sheet.Cells[r, colCount]], "WARNING");
                }
            }

            int lastRow = Math.Max(2, rowCount + 1);
            FinishSheet(sheet, lastRow, colCount);
            AutoFitNoteColumn(sheet, lastRow, colCount);
        }

        private static DrawingBomFieldResult GetField(List<DrawingBomFieldResult> fields, string name)
        {
            if (fields == null)
                return null;

            foreach (var f in fields)
            {
                if (string.Equals(f.FieldName, name, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
            return null;
        }

        private static void WriteHeaders(dynamic sheet, string[] headers)
        {
            for (int i = 0; i < headers.Length; i++)
                sheet.Cells[1, i + 1].Value = headers[i];

            dynamic header = sheet.Range[sheet.Cells[1, 1], sheet.Cells[1, headers.Length]];
            header.Font.Bold = true;
            header.Font.Color = Rgb(255, 255, 255);
            header.Interior.Color = Rgb(47, 117, 181);
            header.HorizontalAlignment = -4108;
        }

        private static void FinishSheet(dynamic sheet, int lastRow, int lastColumn)
        {
            dynamic used = sheet.Range[sheet.Cells[1, 1], sheet.Cells[lastRow, lastColumn]];
            used.Borders.LineStyle = 1;
            used.VerticalAlignment = -4160;
            used.WrapText = true;
            sheet.Range[sheet.Cells[1, 1], sheet.Cells[lastRow, lastColumn]].AutoFilter();

            for (int column = 1; column <= lastColumn; column++)
            {
                dynamic excelColumn = sheet.Columns[column];
                excelColumn.AutoFit();
                double width = Convert.ToDouble(excelColumn.ColumnWidth);
                if (width > 55.0)
                    excelColumn.ColumnWidth = 55.0;
                else if (width < 9.0)
                    excelColumn.ColumnWidth = 9.0;
            }

            sheet.Rows[1].RowHeight = 30.0;
            for (int row = 2; row <= lastRow; row++)
            {
                dynamic excelRow = sheet.Rows[row];
                excelRow.AutoFit();
                double height = Convert.ToDouble(excelRow.RowHeight);
                if (height > 42.0)
                    excelRow.RowHeight = 42.0;
                else if (height < 18.0)
                    excelRow.RowHeight = 18.0;
            }
        }

        private static void AutoFitNoteColumn(dynamic sheet, int lastRow, int noteColumn)
        {
            try
            {
                dynamic noteRange = sheet.Range[
                    sheet.Cells[1, noteColumn],
                    sheet.Cells[lastRow, noteColumn]];
                dynamic excelColumn = sheet.Columns[noteColumn];

                noteRange.WrapText = false;
                excelColumn.AutoFit();
                double width = Convert.ToDouble(excelColumn.ColumnWidth);
                if (width > 80.0)
                    width = 80.0;
                else if (width < 18.0)
                    width = 18.0;

                excelColumn.ColumnWidth = width;
                noteRange.WrapText = true;
                noteRange.Rows.AutoFit();
                sheet.Rows[1].RowHeight = 30.0;
            }
            catch { }
        }

        private static void ApplyStatusColor(dynamic range, string status)
        {
            if (range == null || string.IsNullOrWhiteSpace(status))
                return;

            string normalized = status.Trim().ToUpperInvariant();
            if (normalized == "NG")
            {
                range.Interior.Color = Rgb(255, 199, 206);
            }
            else if (normalized == "WARNING" || normalized == "CHECK" || normalized == "SKIP")
            {
                range.Interior.Color = Rgb(255, 235, 156);
            }
        }

        private static int Rgb(int red, int green, int blue)
        {
            return red | (green << 8) | (blue << 16);
        }

        private static void FreezeTopRow(dynamic sheet)
        {
            if (sheet == null)
                return;

            try
            {
                sheet.Activate();
                dynamic window = sheet.Application.ActiveWindow;
                window.SplitRow = 1;
                window.SplitColumn = 0;
                window.FreezePanes = true;
            }
            catch { }
        }
    }
}
