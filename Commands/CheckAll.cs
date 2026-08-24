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

            int excelRow = 2;
            foreach (SummaryRow row in rows)
            {
                string overall = OverallStatus(row);
                string processingStatus = ProcessingStatus(row);
                sheet.Cells[excelRow, 1].Value = row.BuhinNo;
                sheet.Cells[excelRow, 2].Value = row.Component;
                sheet.Cells[excelRow, 3].Value = row.DfTk;
                sheet.Cells[excelRow, 4].Value = row.UraOmote;
                sheet.Cells[excelRow, 5].Value = row.Kegaki;
                sheet.Cells[excelRow, 6].Value = processingStatus;
                sheet.Cells[excelRow, 7].Value = overall;
                sheet.Cells[excelRow, 8].Value = row.Note;
                ApplyStatusColor(sheet.Range["A" + excelRow + ":H" + excelRow], overall);
                if (processingStatus.StartsWith("SKIP", StringComparison.OrdinalIgnoreCase))
                    ApplyStatusColor(sheet.Cells[excelRow, 6], "SKIP");
                excelRow++;
            }

            if (rows.Count == 0)
                sheet.Cells[2, 1].Value = "Khong co dong BOM nao duoc chon.";

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

            int row = 2;
            if (result != null)
            {
                foreach (DfTkResult item in result.DiffResults)
                {
                    sheet.Cells[row, 1].Value = item.BuhinNo;
                    sheet.Cells[row, 2].Value = item.Component;
                    sheet.Cells[row, 3].Value = item.OuterDf;
                    sheet.Cells[row, 4].Value = item.OuterTk;
                    sheet.Cells[row, 5].Value = item.InnerDf;
                    sheet.Cells[row, 6].Value = item.InnerTk;
                    sheet.Cells[row, 7].Value = item.AreaDf;
                    sheet.Cells[row, 8].Value = item.AreaTk;
                    sheet.Cells[row, 9].Value = item.GeometryDfSummary;
                    sheet.Cells[row, 10].Value = item.GeometryTkSummary;
                    sheet.Cells[row, 11].Value = "NG";
                    sheet.Cells[row, 12].Value = item.DiffText;
                    ApplyStatusColor(sheet.Range["A" + row + ":L" + row], "NG");
                    row++;
                }

                foreach (string checkLog in result.CheckLogs)
                {
                    DfTkLogRow logRow = ParseDfTkCheckLog(checkLog);
                    sheet.Cells[row, 1].Value = logRow.BuhinNo;
                    sheet.Cells[row, 2].Value = logRow.Component;
                    sheet.Cells[row, 11].Value = "SKIP";
                    sheet.Cells[row, 12].Value = logRow.Note;
                    ApplyStatusColor(sheet.Range["A" + row + ":L" + row], "SKIP");
                    row++;
                }
            }

            if (row == 2)
                sheet.Cells[2, 1].Value = "OK - Khong phat hien khac biet DF/TK.";
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

            int row = 2;
            if (result != null)
            {
                foreach (UraOmoteRowResult item in result.Results)
                {
                    sheet.Cells[row, 1].Value = item.BuhinNo;
                    sheet.Cells[row, 2].Value = item.Status;
                    sheet.Cells[row, 3].Value = item.Component;
                    sheet.Cells[row, 4].Value = item.DefaultPinkFaceCount;
                    sheet.Cells[row, 5].Value = item.DefaultPinkAreaMm2;
                    sheet.Cells[row, 6].Value = item.FlatPinkFaceCount;
                    sheet.Cells[row, 7].Value = item.FlatPinkAreaMm2;
                    sheet.Cells[row, 8].Value = item.Note;
                    ApplyStatusColor(sheet.Range["A" + row + ":H" + row], item.Status);
                    row++;
                }
            }

            if (row == 2)
                sheet.Cells[2, 1].Value = "Khong co du lieu CHECK URA OMOTE.";
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

            int row = 2;
            string previousComponentKey = null;
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

                    sheet.Cells[row, 1].Value =
                        firstBendOfComponent ? item.BuhinNo : "";
                    sheet.Cells[row, 2].Value = item.Status;
                    sheet.Cells[row, 3].Value = item.Component;
                    sheet.Cells[row, 4].Value = item.MaterialName;
                    sheet.Cells[row, 5].Value =
                        item.SheetThicknessMm > 0.0 ? (object)item.SheetThicknessMm : "";
                    sheet.Cells[row, 6].Value = item.BendTableName;
                    sheet.Cells[row, 7].Value =
                        item.BendCoefficientMm.HasValue
                            ? (object)item.BendCoefficientMm.Value
                            : "";
                    sheet.Cells[row, 8].Value = item.StandardBendTableName;
                    sheet.Cells[row, 9].Value =
                        item.StandardBendCoefficientMm.HasValue
                            ? (object)item.StandardBendCoefficientMm.Value
                            : "";
                    sheet.Cells[row, 10].Value =
                        item.BendCoefficientMm.HasValue
                        && item.StandardBendCoefficientMm.HasValue
                            ? (object)Math.Abs(
                                item.BendCoefficientMm.Value
                                - item.StandardBendCoefficientMm.Value)
                            : "";
                    sheet.Cells[row, 11].Value = item.BendName;
                    sheet.Cells[row, 12].Value = item.AngleDeg;
                    sheet.Cells[row, 13].Value = item.RadiusMm;
                    sheet.Cells[row, 14].Value = item.DefaultSetting;
                    sheet.Cells[row, 15].Value = item.BendSetting;
                    sheet.Cells[row, 16].Value = item.Note;
                    ApplyStatusColor(sheet.Range["A" + row + ":P" + row], item.Status);
                    previousComponentKey = componentKey;
                    row++;
                }
            }

            if (row == 2)
                sheet.Cells[2, 1].Value = "Khong co du lieu CHECK KEGAKI.";
            int lastRow = Math.Max(2, row - 1);
            FinishSheet(sheet, lastRow, 16);
            AutoFitNoteColumn(sheet, lastRow, 16);
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
}
