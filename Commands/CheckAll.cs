using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace ADDIN.Commands
{
    public sealed class CombinedCheckOptions
    {
        public bool CheckDfTk { get; set; }
        public bool CheckUraOmote { get; set; }
        public bool CheckKegaki { get; set; }

        public bool HasSelection
        {
            get { return CheckUraOmote || CheckKegaki; }
        }

        public int SelectedCount
        {
            get
            {
                int count = 0;
                if (CheckUraOmote) count++;
                if (CheckKegaki) count++;
                return count;
            }
        }

        public static CombinedCheckOptions All()
        {
            return new CombinedCheckOptions
            {
                CheckDfTk = false,
                CheckUraOmote = true,
                CheckKegaki = true
            };
        }
    }

    public sealed class CombinedCheckResult
    {
        public KetQuaSoSanhDfTk DfTk { get; set; }
        public UraOmoteCheckResult UraOmote { get; set; }
        public KegakiCheckResult Kegaki { get; set; }

        public bool Canceled
        {
            get
            {
                return (DfTk != null && DfTk.Canceled)
                    || (UraOmote != null && UraOmote.Canceled)
                    || (Kegaki != null && Kegaki.Canceled);
            }
        }
    }

    public static class ExcelCombinedCheckExporter
    {
        private sealed class SummaryRow
        {
            public string BuhinNo;
            public string Component;
            public string PartPath;
            public string DfTk = "OK";
            public string UraOmote = "OK";
            public string Kegaki = "OK";
            public bool DfTkSkipped;
            public bool UraOmoteSkipped;
            public bool KegakiSkipped;
            public string Note = "";
        }

        private sealed class DfTkLogRow
        {
            public string BuhinNo = "";
            public string Component = "";
            public string PartPath = "";
            public string Note = "";
        }

        public static void Export(CombinedCheckResult result, DataGridView gridBom)
        {
            if (result == null)
                return;

            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    MessageBox.Show(
                        "Khong tim thay Microsoft Excel.",
                        "CHECK URA OMOTE KEGAKI",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                dynamic excel = Activator.CreateInstance(excelType);
                dynamic workbook = excel.Workbooks.Add();
                int sheetCount = 1;
                if (result.DfTk != null) sheetCount++;
                if (result.UraOmote != null) sheetCount++;
                if (result.Kegaki != null) sheetCount++;
                using (var exportScope = new ExcelCheckExportScope(excel, "URA/KEGAKI"))
                {
                    PrepareSheetCount(excel, workbook, sheetCount);

                    dynamic summarySheet = workbook.Sheets[1];
                    summarySheet.Name = "TONG HOP";

                    List<SummaryRow> summaryRows = BuildSummaryRows(result, gridBom);
                    WriteSummary(summarySheet, summaryRows);
                    FreezeTopRow(summarySheet);

                    int sheetIndex = 2;
                    if (result.DfTk != null)
                    {
                        dynamic dfTkSheet = workbook.Sheets[sheetIndex++];
                        dfTkSheet.Name = "CHECK DF-TK";
                        WriteDfTk(dfTkSheet, result.DfTk);
                        FreezeTopRow(dfTkSheet);
                    }
                    if (result.UraOmote != null)
                    {
                        dynamic uraSheet = workbook.Sheets[sheetIndex++];
                        uraSheet.Name = "CHECK URA OMOTE";
                        WriteUraOmote(uraSheet, result.UraOmote);
                        FreezeTopRow(uraSheet);
                    }
                    if (result.Kegaki != null)
                    {
                        dynamic kegakiSheet = workbook.Sheets[sheetIndex++];
                        kegakiSheet.Name = "CHECK KEGAKI";
                        WriteKegaki(kegakiSheet, result.Kegaki);
                        FreezeTopRow(kegakiSheet);
                    }

                    summarySheet.Activate();
                    summarySheet.Range["A1"].Select();
                }
                excel.Visible = true;
                MessageBox.Show(
                    "Da xuat CHECK URA OMOTE KEGAKI thanh 1 file Excel gom "
                        + sheetCount + " sheet.",
                    "CHECK URA OMOTE KEGAKI",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Loi xuat Excel CHECK URA OMOTE KEGAKI: " + ex.Message,
                    "CHECK URA OMOTE KEGAKI",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void PrepareSheetCount(
            dynamic excel,
            dynamic workbook,
            int requiredSheetCount)
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

        private static List<SummaryRow> BuildSummaryRows(
            CombinedCheckResult result,
            DataGridView gridBom)
        {
            List<SummaryRow> rows = BuildSelectedBomRows(gridBom);
            foreach (SummaryRow row in rows)
            {
                row.DfTk = result.DfTk == null ? "-" : "OK";
                row.UraOmote = result.UraOmote == null ? "-" : "OK";
                row.Kegaki = result.Kegaki == null ? "-" : "OK";
            }

            if (result.DfTk != null)
            {
                foreach (DfTkResult item in result.DfTk.DiffResults)
                {
                    SummaryRow row = FindOrAdd(rows, item.BuhinNo, item.Component, item.PartPath);
                    row.DfTk = "NG";
                    AppendNote(row, "DF/TK: " + item.DiffText);
                }

                foreach (string checkLog in result.DfTk.CheckLogs)
                {
                    DfTkLogRow logRow = ParseDfTkCheckLog(checkLog);
                    SummaryRow row = FindOrAdd(
                        rows,
                        logRow.BuhinNo,
                        logRow.Component,
                        logRow.PartPath);
                    row.DfTkSkipped = true;
                    row.DfTk = MergeStatus(row.DfTk, "SKIP");
                    AppendNote(row, "DF/TK SKIP: " + logRow.Note);
                }
            }

            if (result.UraOmote != null)
            {
                foreach (UraOmoteRowResult item in result.UraOmote.Results)
                {
                    SummaryRow row = FindOrAdd(rows, item.BuhinNo, item.Component, item.PartPath);
                    if (Normalize(item.Status) == "SKIP")
                        row.UraOmoteSkipped = true;
                    row.UraOmote = MergeStatus(row.UraOmote, item.Status);
                    if (item.Status == "NG" || item.Status == "CHECK" || item.Status == "SKIP")
                        AppendNote(row, "URA: " + item.Note);
                }
            }

            if (result.Kegaki != null)
            {
                foreach (KegakiBendResult item in result.Kegaki.Results)
                {
                    SummaryRow row = FindOrAdd(rows, item.BuhinNo, item.Component, item.PartPath);
                    if (Normalize(item.Status) == "SKIP")
                        row.KegakiSkipped = true;
                    row.Kegaki = MergeStatus(row.Kegaki, item.Status);
                    if (item.Status == "NG" || item.Status == "CHECK" || item.Status == "SKIP")
                        AppendNote(row, "KEGAKI: " + item.Note);
                }
            }

            foreach (SummaryRow row in rows)
            {
                if (result.DfTk == null) row.DfTk = "-";
                if (result.UraOmote == null) row.UraOmote = "-";
                if (result.Kegaki == null) row.Kegaki = "-";
            }

            rows.Sort(delegate(SummaryRow left, SummaryRow right)
            {
                return CompareBuhinNo(left.BuhinNo, right.BuhinNo);
            });
            return rows;
        }

        private static List<SummaryRow> BuildSelectedBomRows(DataGridView gridBom)
        {
            List<SummaryRow> rows = new List<SummaryRow>();
            if (gridBom == null)
                return rows;

            foreach (DataGridViewRow gridRow in gridBom.Rows)
            {
                if (gridRow.IsNewRow || !Convert.ToBoolean(gridRow.Cells[0].Value ?? false))
                    continue;

                string buhinNo = CellText(gridRow, 1);
                string fileName = CellText(gridRow, 5);
                FindOrAdd(rows, buhinNo, fileName, "");
            }
            return rows;
        }

        private static SummaryRow FindOrAdd(
            List<SummaryRow> rows,
            string buhinNo,
            string component,
            string partPath)
        {
            string normalizedBuhin = Normalize(buhinNo);
            string normalizedPath = NormalizePath(partPath);
            string normalizedComponent = Normalize(component);

            foreach (SummaryRow row in rows)
            {
                if (!string.IsNullOrWhiteSpace(normalizedBuhin)
                    && Normalize(row.BuhinNo) == normalizedBuhin)
                {
                    FillIdentity(row, component, partPath);
                    return row;
                }

                if (!string.IsNullOrWhiteSpace(normalizedPath)
                    && NormalizePath(row.PartPath) == normalizedPath)
                {
                    FillIdentity(row, component, partPath);
                    return row;
                }

                if (string.IsNullOrWhiteSpace(normalizedBuhin)
                    && !string.IsNullOrWhiteSpace(normalizedComponent)
                    && Normalize(row.Component) == normalizedComponent)
                {
                    FillIdentity(row, component, partPath);
                    return row;
                }
            }

            SummaryRow created = new SummaryRow
            {
                BuhinNo = buhinNo ?? "",
                Component = component ?? "",
                PartPath = partPath ?? ""
            };
            rows.Add(created);
            return created;
        }

        private static void FillIdentity(SummaryRow row, string component, string partPath)
        {
            if (string.IsNullOrWhiteSpace(row.Component) && !string.IsNullOrWhiteSpace(component))
                row.Component = component;
            if (string.IsNullOrWhiteSpace(row.PartPath) && !string.IsNullOrWhiteSpace(partPath))
                row.PartPath = partPath;
        }

        private static void WriteSummary(dynamic sheet, List<SummaryRow> rows)
        {
            string[] headers =
            {
                "\u90E8\u54C1\u756A\u53F7",
                "Component",
                "CHECK DF/TK",
                "CHECK \u30A6\u30E9\u8868",
                "CHECK KEGAKI",
                "T\u00ECnh tr\u1EA1ng x\u1EED l\u00FD",
                "Tong ket",
                "Ghi chu"
            };
            WriteHeaders(sheet, headers);

            var data = new object[Math.Max(1, rows.Count), 8];
            var fills = new ExcelCheckFillBatch(1, 8);
            var skipped = new ExcelCheckFillBatch(6, 6);
            int excelRow = 2;
            foreach (SummaryRow row in rows)
            {
                string overall = OverallStatus(row);
                string processingStatus = ProcessingStatus(row);
                data[excelRow - 2, 0] = row.BuhinNo;
                data[excelRow - 2, 1] = row.Component;
                data[excelRow - 2, 2] = row.DfTk;
                data[excelRow - 2, 3] = row.UraOmote;
                data[excelRow - 2, 4] = row.Kegaki;
                data[excelRow - 2, 5] = processingStatus;
                data[excelRow - 2, 6] = overall;
                data[excelRow - 2, 7] = row.Note;
                fills.AddStatus(excelRow, overall);
                if (processingStatus.StartsWith("SKIP", StringComparison.OrdinalIgnoreCase))
                    skipped.AddStatus(excelRow, "SKIP");
                excelRow++;
            }

            if (rows.Count == 0)
                data[0, 0] = "Khong co dong BOM nao duoc chon.";

            ExcelCheckReportWriter.WriteBlock(sheet, 2, 1, data);
            fills.Apply(sheet);
            skipped.Apply(sheet);
            sheet.Columns[2].ColumnWidth = 28;
            int lastRow = Math.Max(2, excelRow - 1);
            FinishSheet(sheet, lastRow, 8);
            AutoFitNoteColumn(sheet, lastRow, 8);
        }

        private static void FreezeTopRow(dynamic sheet)
        {
            if (sheet == null)
                return;

            try
            {
                sheet.Activate();
                dynamic window = sheet.Application.ActiveWindow;
                if (window == null)
                    return;

                window.FreezePanes = false;
                window.SplitColumn = 0;
                window.SplitRow = 1;
                window.FreezePanes = true;
            }
            catch
            {
                // Freezing the header is a presentation enhancement only.
                // Excel export must still succeed if a workbook/window does not
                // expose FreezePanes in a particular environment.
            }
        }

        private static void WriteDfTk(dynamic sheet, KetQuaSoSanhDfTk result)
        {
            string[] headers =
            {
                "\u90E8\u54C1\u756A\u53F7", "Component", "\u5916\u5074-DF", "\u5916\u5074-TK",
                "\u5185\u5074-DF", "\u5185\u5074-TK", "Dien tich DF (mm2)",
                "Dien tich TK (mm2)", "Geometry DF", "Geometry TK",
                "Status", "Ghi chu"
            };
            WriteHeaders(sheet, headers);

            int count = result == null ? 0 : result.DiffResults.Count + result.CheckLogs.Count;
            var data = new object[Math.Max(1, count), 12];
            var fills = new ExcelCheckFillBatch(1, 12);
            int row = 2;
            if (result != null)
            {
                foreach (DfTkResult item in result.DiffResults)
                {
                    data[row - 2, 0] = item.BuhinNo;
                    data[row - 2, 1] = item.Component;
                    data[row - 2, 2] = item.OuterDf;
                    data[row - 2, 3] = item.OuterTk;
                    data[row - 2, 4] = item.InnerDf;
                    data[row - 2, 5] = item.InnerTk;
                    data[row - 2, 6] = item.AreaDf;
                    data[row - 2, 7] = item.AreaTk;
                    data[row - 2, 8] = item.GeometryDfSummary;
                    data[row - 2, 9] = item.GeometryTkSummary;
                    data[row - 2, 10] = "NG";
                    data[row - 2, 11] = item.DiffText;
                    fills.AddStatus(row, "NG");
                    row++;
                }

                foreach (string checkLog in result.CheckLogs)
                {
                    DfTkLogRow logRow = ParseDfTkCheckLog(checkLog);
                    data[row - 2, 0] = logRow.BuhinNo;
                    data[row - 2, 1] = logRow.Component;
                    data[row - 2, 10] = "SKIP";
                    data[row - 2, 11] = logRow.Note;
                    fills.AddStatus(row, "SKIP");
                    row++;
                }
            }

            if (row == 2)
                data[0, 0] = "OK - Khong phat hien khac biet DF/TK.";
            ExcelCheckReportWriter.WriteBlock(sheet, 2, 1, data);
            fills.Apply(sheet);
            sheet.Columns[2].ColumnWidth = 28;
            sheet.Columns[9].ColumnWidth = 24;
            sheet.Columns[10].ColumnWidth = 24;
            int lastRow = Math.Max(2, row - 1);
            FinishSheet(sheet, lastRow, 12);
            AutoFitNoteColumn(sheet, lastRow, 12);
        }

        private static void WriteUraOmote(dynamic sheet, UraOmoteCheckResult result)
        {
            string[] headers =
            {
                "\u90E8\u54C1\u756A\u53F7", "Status", "Component",
                "Mat hong Default", "Dien tich Default (mm2)",
                "Mat hong Flat-Pattern", "Dien tich Flat (mm2)", "Ghi chu"
            };
            WriteHeaders(sheet, headers);

            var data = new object[Math.Max(1, result == null ? 0 : result.Results.Count), 8];
            var fills = new ExcelCheckFillBatch(1, 8);
            int row = 2;
            if (result != null)
            {
                foreach (UraOmoteRowResult item in result.Results)
                {
                    data[row - 2, 0] = item.BuhinNo;
                    data[row - 2, 1] = item.Status;
                    data[row - 2, 2] = item.Component;
                    data[row - 2, 3] = item.DefaultPinkFaceCount;
                    data[row - 2, 4] = item.DefaultPinkAreaMm2;
                    data[row - 2, 5] = item.FlatPinkFaceCount;
                    data[row - 2, 6] = item.FlatPinkAreaMm2;
                    data[row - 2, 7] = item.Note;
                    fills.AddStatus(row, item.Status);
                    row++;
                }
            }

            if (row == 2)
                data[0, 0] = "Khong co du lieu CHECK URA OMOTE.";
            ExcelCheckReportWriter.WriteBlock(sheet, 2, 1, data);
            fills.Apply(sheet);
            int lastRow = Math.Max(2, row - 1);
            FinishSheet(sheet, lastRow, 8);
            AutoFitNoteColumn(sheet, lastRow, 8);
        }

        private static void WriteKegaki(dynamic sheet, KegakiCheckResult result)
        {
            string[] headers =
            {
                "\u90E8\u54C1\u756A\u53F7", "Status", "Component", "Vat lieu",
                "\u677F\u539A (mm)", "BendTable thuc te", "He so thuc te (mm)",
                "BendTable chuan", "He so chuan (mm)", "Chenh lech (mm)",
                "Bend", "Goc (deg)", "Ban kinh (mm)", "Setting chung",
                "Setting cua Bend", "Ghi chu"
            };
            WriteHeaders(sheet, headers);

            var data = new object[Math.Max(1, result == null ? 0 : result.Results.Count), 16];
            var fills = new ExcelCheckFillBatch(1, 16);
            var statuses = new ExcelCheckFillBatch(2, 2);
            int row = 2;
            string previousComponentKey = null;
            int componentIndex = -1;
            int pastelColorA = Rgb(235, 244, 252); // Soft Pastel Sky/Ice Blue (#EBF4FC)
            int pastelColorB = Rgb(255, 255, 255); // Pure White (#FFFFFF)

            if (result != null)
            {
                foreach (KegakiBendResult item in result.Results)
                {
                    string componentKey = (item.BuhinNo ?? "") + "\u001F"
                        + (item.Component ?? "") + "\u001F"
                        + (item.PartPath ?? "");
                    bool firstBendOfComponent = !string.Equals(
                        previousComponentKey,
                        componentKey,
                        StringComparison.OrdinalIgnoreCase);

                    if (firstBendOfComponent)
                    {
                        componentIndex++;
                        previousComponentKey = componentKey;
                    }

                    int rowBg = (componentIndex % 2 == 0) ? pastelColorA : pastelColorB;
                    fills.Add(row, rowBg);

                    data[row - 2, 0] =
                        firstBendOfComponent ? item.BuhinNo : "";
                    data[row - 2, 1] = item.Status;
                    data[row - 2, 2] = item.Component;
                    data[row - 2, 3] = item.MaterialName;
                    data[row - 2, 4] =
                        item.SheetThicknessMm > 0.0 ? (object)item.SheetThicknessMm : "";
                    data[row - 2, 5] = item.BendTableName;
                    data[row - 2, 6] =
                        item.BendCoefficientMm.HasValue
                            ? (object)item.BendCoefficientMm.Value
                            : "";
                    data[row - 2, 7] = item.StandardBendTableName;
                    data[row - 2, 8] =
                        item.StandardBendCoefficientMm.HasValue
                            ? (object)item.StandardBendCoefficientMm.Value
                            : "";
                    data[row - 2, 9] =
                        item.BendCoefficientMm.HasValue
                        && item.StandardBendCoefficientMm.HasValue
                            ? (object)Math.Abs(
                                item.BendCoefficientMm.Value
                                - item.StandardBendCoefficientMm.Value)
                            : "";
                    data[row - 2, 10] = item.BendName;
                    data[row - 2, 11] = item.AngleDeg;
                    data[row - 2, 12] = item.RadiusMm;
                    data[row - 2, 13] = item.DefaultSetting;
                    data[row - 2, 14] = item.BendSetting;
                    data[row - 2, 15] = item.Note;

                    int? statusColor = ExcelCheckReportWriter.StatusColor(item.Status);
                    if (Normalize(item.Status) == "OK") statusColor = Rgb(198, 239, 206);
                    if (statusColor.HasValue) statuses.Add(row, statusColor.Value);

                    row++;
                }
            }

            if (row == 2)
                data[0, 0] = "Khong co du lieu CHECK KEGAKI.";
            ExcelCheckReportWriter.WriteBlock(sheet, 2, 1, data);
            fills.Apply(sheet);
            statuses.Apply(sheet, true);
            int lastRow = Math.Max(2, row - 1);
            if (row > 2) sheet.Range["B2:B" + lastRow].HorizontalAlignment = -4108;
            FinishSheet(sheet, lastRow, 16);
            AutoFitNoteColumn(sheet, lastRow, 16);
        }

        private static void WriteHeaders(dynamic sheet, string[] headers)
        {
            ExcelCheckReportWriter.WriteHeaders(sheet, headers);

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

            used.Columns.AutoFit();
            for (int column = 1; column <= lastColumn; column++)
            {
                dynamic excelColumn = sheet.Columns[column];
                double width = Convert.ToDouble(excelColumn.ColumnWidth);
                if (width > 55.0)
                    excelColumn.ColumnWidth = 55.0;
                else if (width < 9.0)
                    excelColumn.ColumnWidth = 9.0;
            }

            // The note formatter below fits all report rows at their final widths.
            // Avoid the old per-row COM loop and its redundant AutoFit.
            sheet.Rows[1].RowHeight = 30.0;
        }

        private static void AutoFitNoteColumn(dynamic sheet, int lastRow, int noteColumn)
        {
            try
            {
                dynamic noteRange = sheet.Range[
                    sheet.Cells[1, noteColumn],
                    sheet.Cells[lastRow, noteColumn]];
                dynamic excelColumn = sheet.Columns[noteColumn];

                // Measure the natural width first, then keep the sheet readable.
                // Long notes wrap and their row heights expand to show all text.
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
            catch
            {
                // Formatting must not prevent the Excel result from opening.
            }
        }

        private static void ApplyStatusColor(dynamic range, string status)
        {
            string normalized = Normalize(status);
            if (normalized == "NG")
                range.Interior.Color = Rgb(255, 199, 206);
            else if (normalized == "CHECK" || normalized == "SKIP")
                range.Interior.Color = Rgb(255, 235, 156);
        }

        private static void ApplyKegakiStatusCell(dynamic cell, string status)
        {
            string normalized = Normalize(status);
            if (normalized == "NG")
            {
                cell.Interior.Color = Rgb(255, 199, 206);
                cell.Font.Color = Rgb(156, 0, 6);
                cell.Font.Bold = true;
            }
            else if (normalized == "CHECK" || normalized == "SKIP")
            {
                cell.Interior.Color = Rgb(255, 235, 156);
                cell.Font.Color = Rgb(156, 87, 0);
                cell.Font.Bold = true;
            }
            else if (normalized == "OK")
            {
                cell.Interior.Color = Rgb(198, 239, 206);
                cell.Font.Color = Rgb(0, 97, 0);
                cell.Font.Bold = true;
            }
            cell.HorizontalAlignment = -4108; // Center
        }

        private static string OverallStatus(SummaryRow row)
        {
            if (Normalize(row.DfTk) == "NG"
                || Normalize(row.UraOmote) == "NG"
                || Normalize(row.Kegaki) == "NG")
                return "NG";

            if (Normalize(row.DfTk) == "CHECK" || Normalize(row.DfTk) == "SKIP"
                || Normalize(row.UraOmote) == "CHECK" || Normalize(row.UraOmote) == "SKIP"
                || Normalize(row.Kegaki) == "CHECK" || Normalize(row.Kegaki) == "SKIP")
                return "CHECK";

            return "OK";
        }

        private static string ProcessingStatus(SummaryRow row)
        {
            List<string> skippedChecks = new List<string>();
            if (row.DfTkSkipped)
                skippedChecks.Add("DF/TK");
            if (row.UraOmoteSkipped)
                skippedChecks.Add("URA");
            if (row.KegakiSkipped)
                skippedChecks.Add("KEGAKI");

            return skippedChecks.Count == 0
                ? "\u0110\u00C3 X\u1EEC L\u00DD"
                : "SKIP: " + string.Join(", ", skippedChecks.ToArray());
        }

        private static string MergeStatus(string current, string next)
        {
            int currentRank = StatusRank(current);
            int nextRank = StatusRank(next);
            return nextRank > currentRank ? (next ?? "") : current;
        }

        private static int StatusRank(string status)
        {
            string normalized = Normalize(status);
            if (normalized == "NG")
                return 3;
            if (normalized == "CHECK")
                return 2;
            if (normalized == "SKIP")
                return 1;
            return 0;
        }

        private static void AppendNote(SummaryRow row, string note)
        {
            if (row == null || string.IsNullOrWhiteSpace(note))
                return;
            if (row.Note.IndexOf(note, StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            row.Note = string.IsNullOrWhiteSpace(row.Note) ? note : row.Note + " | " + note;
        }

        private static DfTkLogRow ParseDfTkCheckLog(string checkLog)
        {
            DfTkLogRow row = new DfTkLogRow();
            string text = (checkLog ?? "").Trim();
            if (text.Length == 0)
            {
                row.Note = "Khong co thong tin kiem tra.";
                return row;
            }

            string identity = text;
            string source = "";
            string reason = "";
            int firstSeparator = text.IndexOf(" | ", StringComparison.Ordinal);
            if (firstSeparator >= 0)
            {
                identity = text.Substring(0, firstSeparator);
                string remaining = text.Substring(firstSeparator + 3);
                int secondSeparator = remaining.IndexOf(" | ", StringComparison.Ordinal);
                if (secondSeparator >= 0)
                {
                    source = remaining.Substring(0, secondSeparator).Trim();
                    reason = remaining.Substring(secondSeparator + 3).Trim();
                }
                else
                {
                    reason = remaining.Trim();
                }
            }

            row.BuhinNo = ExtractLogValue(identity, "BuhinNo=", ", FileName=");
            row.Component = ExtractLogValue(identity, "FileName=", "");

            string sourceComponent = ExtractLogValue(source, "Component=", ", Path=");
            string sourcePath = ExtractLogValue(source, "Path=", "");
            if (!string.IsNullOrWhiteSpace(sourceComponent))
                row.Component = sourceComponent;
            if (!string.IsNullOrWhiteSpace(sourcePath))
                row.PartPath = sourcePath;
            else if (source.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase))
                row.PartPath = source;

            if (string.IsNullOrWhiteSpace(row.Component)
                && !string.IsNullOrWhiteSpace(row.PartPath))
            {
                row.Component = Path.GetFileNameWithoutExtension(row.PartPath);
            }

            row.Note = string.IsNullOrWhiteSpace(reason) ? text : reason;
            return row;
        }

        private static string ExtractLogValue(
            string text,
            string startMarker,
            string endMarker)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(startMarker))
                return "";

            int start = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return "";

            start += startMarker.Length;
            int end = text.Length;
            if (!string.IsNullOrEmpty(endMarker))
            {
                int markerIndex = text.IndexOf(endMarker, start, StringComparison.OrdinalIgnoreCase);
                if (markerIndex >= 0)
                    end = markerIndex;
            }

            return text.Substring(start, Math.Max(0, end - start)).Trim();
        }

        private static string CellText(DataGridViewRow row, int index)
        {
            if (row == null || index < 0 || index >= row.Cells.Count)
                return "";
            return Convert.ToString(row.Cells[index].Value ?? "").Trim();
        }

        private static string Normalize(string value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        private static string NormalizePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";
            try
            {
                return Path.GetFullPath(value.Trim()).TrimEnd('\\').ToUpperInvariant();
            }
            catch
            {
                return Normalize(value).TrimEnd('\\');
            }
        }

        private static int CompareBuhinNo(string left, string right)
        {
            decimal leftNumber;
            decimal rightNumber;
            bool leftIsNumber = decimal.TryParse(left, out leftNumber);
            bool rightIsNumber = decimal.TryParse(right, out rightNumber);
            if (leftIsNumber && rightIsNumber)
                return leftNumber.CompareTo(rightNumber);
            if (leftIsNumber)
                return -1;
            if (rightIsNumber)
                return 1;
            return string.Compare(left, right, StringComparison.CurrentCultureIgnoreCase);
        }

        private static int Rgb(int red, int green, int blue)
        {
            return red + (green << 8) + (blue << 16);
        }
    }
    // Shared by the three URA/KEGAKI report exporters. No SOLIDWORKS access.
    internal static class ExcelCheckReportWriter
    {
        internal static string ColumnName(int column)
        {
            string name = "";
            while (column > 0) { column--; name = (char)('A' + column % 26) + name; column /= 26; }
            return name;
        }

        internal static void WriteHeaders(dynamic sheet, string[] headers)
        {
            var data = new object[1, headers.Length];
            for (int i = 0; i < headers.Length; i++) data[0, i] = headers[i];
            WriteBlock(sheet, 1, 1, data);
        }

        internal static void WriteBlock(dynamic sheet, int firstRow, int firstColumn, object[,] data)
        {
            int rows = data.GetLength(0), columns = data.GetLength(1);
            if (rows == 0 || columns == 0) return;
            const int chunkSize = 4096;
            for (int offset = 0; offset < rows; offset += chunkSize)
            {
                int count = Math.Min(chunkSize, rows - offset);
                object[,] chunk = data;
                if (offset != 0 || count != rows)
                {
                    chunk = new object[count, columns];
                    for (int r = 0; r < count; r++)
                        for (int c = 0; c < columns; c++) chunk[r, c] = data[offset + r, c];
                }
                dynamic range = sheet.Range[ColumnName(firstColumn) + (firstRow + offset)
                    + ":" + ColumnName(firstColumn + columns - 1) + (firstRow + offset + count - 1)];
                try { range.Value2 = chunk; }
                finally { Release((object)range); }
            }
        }

        internal static void Release(object value)
        {
            if (value != null && System.Runtime.InteropServices.Marshal.IsComObject(value))
                try { System.Runtime.InteropServices.Marshal.ReleaseComObject(value); } catch { }
        }

        internal static void FreezeTopRow(dynamic sheet)
        {
            dynamic window = null;
            try
            {
                sheet.Activate(); window = sheet.Application.ActiveWindow;
                if (window == null) return;
                window.FreezePanes = false; window.SplitColumn = 0;
                window.SplitRow = 1; window.FreezePanes = true;
            }
            catch { /* Optional presentation must not abort the data export. */ }
            finally { Release((object)window); }
        }

        internal static int? StatusColor(string status)
        {
            string normalized = (status ?? "").Trim().ToUpperInvariant();
            if (normalized == "NG") return 255 + (199 << 8) + (206 << 16);
            if (normalized == "CHECK" || normalized == "SKIP") return 255 + (235 << 8) + (156 << 16);
            return null;
        }
    }

    internal sealed class ExcelCheckFillBatch
    {
        private sealed class Band { internal int First, Last; }
        private readonly Dictionary<int, List<Band>> colors = new Dictionary<int, List<Band>>();
        private readonly string firstColumn, lastColumn;

        internal ExcelCheckFillBatch(int first, int last)
        { firstColumn = ExcelCheckReportWriter.ColumnName(first); lastColumn = ExcelCheckReportWriter.ColumnName(last); }

        internal void AddStatus(int row, string status)
        {
            int? color = ExcelCheckReportWriter.StatusColor(status);
            if (color.HasValue) Add(row, color.Value);
        }

        internal void Add(int row, int color)
        {
            List<Band> bands;
            if (!colors.TryGetValue(color, out bands)) { bands = new List<Band>(); colors.Add(color, bands); }
            if (bands.Count > 0 && bands[bands.Count - 1].Last == row - 1) bands[bands.Count - 1].Last = row;
            else bands.Add(new Band { First = row, Last = row });
        }

        internal void Apply(dynamic sheet, bool statusText = false)
        {
            foreach (KeyValuePair<int, List<Band>> item in colors)
            {
                var addresses = new List<string>();
                int length = 0;
                foreach (Band band in item.Value)
                {
                    string address = firstColumn + band.First + ":" + lastColumn + band.Last;
                    // Keep the address below Excel's legacy 255 character limit.
                    if (length + address.Length + 1 > 200 && addresses.Count > 0)
                    { ApplyRange(sheet, string.Join(",", addresses.ToArray()), item.Key, statusText); addresses.Clear(); length = 0; }
                    addresses.Add(address); length += address.Length + 1;
                }
                if (addresses.Count > 0) ApplyRange(sheet, string.Join(",", addresses.ToArray()), item.Key, statusText);
            }
        }

        private static void ApplyRange(dynamic sheet, string address, int color, bool statusText)
        {
            dynamic range = sheet.Range[address];
            try
            {
                range.Interior.Color = color;
                if (statusText)
                {
                    int fontColor = color == 255 + (199 << 8) + (206 << 16) ? 156 + (6 << 16)
                        : color == 198 + (239 << 8) + (206 << 16) ? (97 << 8) : 156 + (87 << 8);
                    range.Font.Color = fontColor;
                    range.Font.Bold = true;
                }
            }
            finally { ExcelCheckReportWriter.Release((object)range); }
        }
    }

    internal sealed class ExcelCheckExportScope : IDisposable
    {
        private readonly dynamic application;
        private readonly bool updating, alerts, events;
        private readonly int calculation;
        private readonly System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
        private readonly string report;

        internal ExcelCheckExportScope(dynamic excel, string name)
        {
            application = excel; report = name;
            updating = (bool)excel.ScreenUpdating; alerts = (bool)excel.DisplayAlerts;
            events = (bool)excel.EnableEvents; calculation = (int)excel.Calculation;
            try
            {
                excel.ScreenUpdating = false; excel.DisplayAlerts = false;
                excel.EnableEvents = false; excel.Calculation = -4135; // xlCalculationManual
            }
            catch { Restore(); throw; }
        }

        private void Restore()
        {
            try { application.Calculation = calculation; } catch { }
            try { application.EnableEvents = events; } catch { }
            try { application.DisplayAlerts = alerts; } catch { }
            try { application.ScreenUpdating = updating; } catch { }
        }

        public void Dispose()
        {
            Restore();
            System.Diagnostics.Debug.WriteLine("[CHECK EXCEL] " + report + " write/format ms=" + timer.ElapsedMilliseconds);
        }
    }

}
