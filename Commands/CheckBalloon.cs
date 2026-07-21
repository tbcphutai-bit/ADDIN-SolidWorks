using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public sealed class BalloonCheckErrorRow
    {
        public string UnitDrawing { get; set; }
        public string KetQua { get; set; }
        public string Sheet { get; set; }
        public string DrawingView { get; set; }
        public string BoPhanSo { get; set; }
        public int SoLuongBom { get; set; }
        public int InstanceCoBalloon { get; set; }
        public int TongBalloon { get; set; }
        public string ChiTiet { get; set; }
    }

    public sealed class BalloonCheckResult
    {
        public int SheetCount { get; internal set; }
        public int ViewCount { get; internal set; }
        public int BomRowCount { get; internal set; }
        public int ExpectedCount { get; internal set; }
        public int ValidCount { get; internal set; }
        public int MissingCount { get; internal set; }
        public int ExcessCount { get; internal set; }
        public int DuplicateCount { get; internal set; }
        public int DanglingCount { get; internal set; }
        public int UnknownCount { get; internal set; }
        public int WrongTextCount { get; internal set; }
        public int BomDataErrorCount { get; internal set; }
        public int TotalBalloonFoundCount { get; internal set; }
        public string UnitName { get; internal set; }

        private readonly List<string> details = new List<string>();
        private readonly List<BalloonCheckErrorRow> rows = new List<BalloonCheckErrorRow>();

        public List<string> Details { get { return details; } }
        public List<BalloonCheckErrorRow> ErrorRows { get { return rows; } }

        public bool IsOk
        {
            get
            {
                return MissingCount == 0 && ExcessCount == 0 && DuplicateCount == 0
                    && DanglingCount == 0 && UnknownCount == 0 && WrongTextCount == 0
                    && BomDataErrorCount == 0;
            }
        }

        public void AddRow(string status, string sheet, string view, string partNumber,
            int bomQuantity, int uniqueBalloon, int totalBalloon, string detail)
        {
            rows.Add(new BalloonCheckErrorRow
            {
                UnitDrawing = UnitName ?? "",
                KetQua = status ?? "",
                Sheet = sheet ?? "",
                DrawingView = view ?? "",
                BoPhanSo = partNumber ?? "",
                SoLuongBom = bomQuantity,
                InstanceCoBalloon = uniqueBalloon,
                TongBalloon = totalBalloon,
                ChiTiet = detail ?? ""
            });
            if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
                details.Add((status ?? "LỖI") + ": " + (detail ?? ""));
        }

        public void Merge(BalloonCheckResult child)
        {
            if (child == null)
                return;
            SheetCount += child.SheetCount;
            ViewCount += child.ViewCount;
            BomRowCount += child.BomRowCount;
            ExpectedCount += child.ExpectedCount;
            ValidCount += child.ValidCount;
            MissingCount += child.MissingCount;
            ExcessCount += child.ExcessCount;
            DuplicateCount += child.DuplicateCount;
            DanglingCount += child.DanglingCount;
            UnknownCount += child.UnknownCount;
            WrongTextCount += child.WrongTextCount;
            BomDataErrorCount += child.BomDataErrorCount;
            TotalBalloonFoundCount += child.TotalBalloonFoundCount;
            rows.AddRange(child.ErrorRows);
            details.AddRange(child.Details);
        }

        public string BuildMessage()
        {
            return (IsOk ? "CHECK BALLOON: OK" : "CHECK BALLOON: CÓ LỖI")
                + "\r\nSheet: " + SheetCount + " | View: " + ViewCount
                + " | Dòng BOM: " + BomRowCount
                + "\r\nComponent: " + ExpectedCount + " | Balloon tìm thấy: " + TotalBalloonFoundCount
                + " | Balloon khớp: " + ValidCount
                + " | Thiếu: " + MissingCount + " | Dư: " + ExcessCount
                + "\r\nTrùng view: " + DuplicateCount + " | Sai số: " + WrongTextCount
                + " | Dangling: " + DanglingCount + " | Ngoài BOM: " + UnknownCount;
        }

        public void ShowSummary(IWin32Window owner)
        {
            using (Form form = new Form())
            using (Label summary = new Label())
            using (DataGridView grid = new DataGridView())
            using (Panel bottom = new Panel())
            using (Button export = new Button())
            using (Button close = new Button())
            {
                form.Text = IsOk ? "CHECK BALLOON - OK" : "CHECK BALLOON - KẾT QUẢ";
                form.StartPosition = FormStartPosition.CenterParent;
                form.Size = new Size(1160, 650);
                form.MinimumSize = new Size(900, 460);
                form.Font = new Font("Segoe UI", 9F);

                summary.Dock = DockStyle.Top;
                summary.Height = 70;
                summary.Padding = new Padding(12, 8, 12, 5);
                summary.Text = BuildMessage();

                grid.Dock = DockStyle.Fill;
                grid.ReadOnly = true;
                grid.AllowUserToAddRows = false;
                grid.AllowUserToDeleteRows = false;
                grid.AllowUserToResizeRows = false;
                grid.RowHeadersVisible = false;
                grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                grid.AutoGenerateColumns = true;
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
                grid.DataSource = ErrorRows;
                grid.DataBindingComplete += delegate
                {
                    if (grid.Columns["ChiTiet"] != null)
                        grid.Columns["ChiTiet"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        string status = Convert.ToString(row.Cells["KetQua"].Value);
                        if (status == "OK") row.DefaultCellStyle.BackColor = Color.Honeydew;
                        else if (status.Contains("THIẾU")) row.DefaultCellStyle.BackColor = Color.MistyRose;
                        else if (status.Contains("DƯ") || status.Contains("TRÙNG")) row.DefaultCellStyle.BackColor = Color.LightYellow;
                        else if (status.Contains("SAI")) row.DefaultCellStyle.BackColor = Color.PeachPuff;
                        else row.DefaultCellStyle.BackColor = Color.Gainsboro;
                    }
                };

                bottom.Dock = DockStyle.Bottom;
                bottom.Height = 48;
                export.Text = "XUẤT EXCEL";
                export.Size = new Size(125, 30);
                export.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                export.Location = new Point(form.ClientSize.Width - 275, 9);
                export.Click += delegate { ExportToExcel(); };
                close.Text = "ĐÓNG";
                close.Size = new Size(110, 30);
                close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                close.Location = new Point(form.ClientSize.Width - 135, 9);
                close.DialogResult = DialogResult.OK;
                bottom.Controls.Add(export);
                bottom.Controls.Add(close);

                form.Controls.Add(grid);
                form.Controls.Add(bottom);
                form.Controls.Add(summary);
                form.AcceptButton = close;
                form.CancelButton = close;
                form.ShowDialog(owner);
            }
        }

        public void ExportToExcel()
        {
            if (ErrorRows.Count == 0)
            {
                MessageBox.Show("Không có dữ liệu để xuất.", "CHECK BALLOON",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            dynamic excel = null;
            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    MessageBox.Show("Không tìm thấy Microsoft Excel.", "CHECK BALLOON",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                excel = Activator.CreateInstance(excelType);
                dynamic workbook = excel.Workbooks.Add();
                dynamic summarySheet = workbook.Sheets[1];
                summarySheet.Name = "TONG HOP";
                Dictionary<string, int> unitColorIndexes =
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                List<BalloonCheckErrorRow> summaryRows = new List<BalloonCheckErrorRow>();
                List<BalloonCheckErrorRow> detailRows = new List<BalloonCheckErrorRow>();
                foreach (BalloonCheckErrorRow row in ErrorRows)
                {
                    if (string.Equals(row.DrawingView, "TẤT CẢ VIEW", StringComparison.OrdinalIgnoreCase)
                        || (string.IsNullOrWhiteSpace(row.DrawingView)
                            && string.IsNullOrWhiteSpace(row.BoPhanSo)))
                        summaryRows.Add(row);
                    else
                        detailRows.Add(row);
                }

                WriteExcelTitle(summarySheet, "CHECK BALLOON - " + (UnitName ?? "DRAWING"),
                    BuildMessage().Replace("\r\n", "   |   "), 8);
                string[] summaryHeaders =
                {
                    "UNIT Drawing", "部品番号", "SL từ Assembly", "SL Balloon",
                    "Chênh lệch", "Kết quả", "Cảnh báo thiếu Balloon", "Ghi chú"
                };
                WriteExcelHeaders(summarySheet, summaryHeaders);

                int excelRow = 5;
                foreach (BalloonCheckErrorRow row in summaryRows)
                {
                    summarySheet.Cells[excelRow, 1].Value = row.UnitDrawing;
                    summarySheet.Cells[excelRow, 2].Value = row.BoPhanSo;
                    summarySheet.Cells[excelRow, 3].Value = row.SoLuongBom;
                    summarySheet.Cells[excelRow, 4].Value = row.TongBalloon;
                    summarySheet.Cells[excelRow, 5].Value = row.TongBalloon - row.SoLuongBom;
                    summarySheet.Cells[excelRow, 6].Value = row.KetQua;
                    summarySheet.Cells[excelRow, 8].Value = row.ChiTiet;
                    ColorExcelUnitRow(summarySheet.Range["A" + excelRow + ":H" + excelRow],
                        row.UnitDrawing, unitColorIndexes);
                    WriteMissingBalloonNotice(
                        summarySheet.Cells[excelRow, 7],
                        summarySheet.Cells[excelRow, 2],
                        row);
                    excelRow++;
                }
                FormatExcelTable(summarySheet, 8, Math.Max(5, excelRow - 1));
                summarySheet.Columns[7].ColumnWidth = 24;
                summarySheet.Columns[8].ColumnWidth = 48;
                summarySheet.Columns[8].WrapText = true;

                if (detailRows.Count > 0)
                {
                    dynamic detailSheet = workbook.Sheets.Add(After: summarySheet);
                    detailSheet.Name = "CHI TIET LOI";
                    WriteExcelTitle(detailSheet, "CHI TIẾT BALLOON CẦN KIỂM TRA",
                        "Chỉ mở sheet này khi cần tìm vị trí lỗi theo Sheet và Drawing View.", 9);
                    string[] detailHeaders =
                    {
                        "UNIT Drawing", "Kết quả", "Sheet", "Drawing View", "部品番号",
                        "SL từ Assembly", "SL Balloon", "Cảnh báo thiếu Balloon", "Chi tiết"
                    };
                    WriteExcelHeaders(detailSheet, detailHeaders);
                    int detailRow = 5;
                    foreach (BalloonCheckErrorRow row in detailRows)
                    {
                        detailSheet.Cells[detailRow, 1].Value = row.UnitDrawing;
                        detailSheet.Cells[detailRow, 2].Value = row.KetQua;
                        detailSheet.Cells[detailRow, 3].Value = row.Sheet;
                        detailSheet.Cells[detailRow, 4].Value = row.DrawingView;
                        detailSheet.Cells[detailRow, 5].Value = row.BoPhanSo;
                        detailSheet.Cells[detailRow, 6].Value = row.SoLuongBom;
                        detailSheet.Cells[detailRow, 7].Value = row.TongBalloon;
                        detailSheet.Cells[detailRow, 9].Value = row.ChiTiet;
                        ColorExcelUnitRow(detailSheet.Range["A" + detailRow + ":I" + detailRow],
                            row.UnitDrawing, unitColorIndexes);
                        WriteMissingBalloonNotice(
                            detailSheet.Cells[detailRow, 8],
                            detailSheet.Cells[detailRow, 5],
                            row);
                        detailRow++;
                    }
                    FormatExcelTable(detailSheet, 9, Math.Max(5, detailRow - 1));
                    detailSheet.Columns[8].ColumnWidth = 24;
                    detailSheet.Columns[9].ColumnWidth = 60;
                    detailSheet.Columns[9].WrapText = true;
                }

                summarySheet.Activate();
                summarySheet.Range["A5"].Select();
                excel.ActiveWindow.FreezePanes = true;
                excel.Visible = true;
            }
            catch (Exception ex)
            {
                try { if (excel != null) excel.Visible = true; }
                catch { }
                MessageBox.Show("Lỗi xuất Excel: " + ex.Message, "CHECK BALLOON",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WriteExcelTitle(dynamic sheet, string title, string subtitle, int columnCount)
        {
            string lastColumn = GetExcelColumnName(columnCount);
            sheet.Range["A1:" + lastColumn + "1"].Merge();
            sheet.Cells[1, 1].Value = title;
            sheet.Range["A1:" + lastColumn + "1"].Font.Bold = true;
            sheet.Range["A1:" + lastColumn + "1"].Font.Size = 15;
            sheet.Range["A1:" + lastColumn + "1"].HorizontalAlignment = -4108;
            sheet.Range["A1:" + lastColumn + "1"].Interior.Color = ExcelRgb(31, 78, 121);
            sheet.Range["A1:" + lastColumn + "1"].Font.Color = ExcelRgb(255, 255, 255);
            sheet.Range["A2:" + lastColumn + "2"].Merge();
            sheet.Cells[2, 1].Value = subtitle;
            sheet.Range["A2:" + lastColumn + "2"].WrapText = true;
            sheet.Range["A2:" + lastColumn + "2"].Interior.Color = ExcelRgb(255, 235, 156);
        }

        private void WriteExcelHeaders(dynamic sheet, string[] headers)
        {
            for (int column = 0; column < headers.Length; column++)
                sheet.Cells[4, column + 1].Value = headers[column];
            string lastColumn = GetExcelColumnName(headers.Length);
            sheet.Range["A4:" + lastColumn + "4"].Font.Bold = true;
            sheet.Range["A4:" + lastColumn + "4"].Interior.Color = ExcelRgb(91, 155, 213);
            sheet.Range["A4:" + lastColumn + "4"].Font.Color = ExcelRgb(255, 255, 255);
            sheet.Range["A4:" + lastColumn + "4"].HorizontalAlignment = -4108;
        }

        private void FormatExcelTable(dynamic sheet, int columnCount, int lastRow)
        {
            string lastColumn = GetExcelColumnName(columnCount);
            dynamic range = sheet.Range["A4:" + lastColumn + lastRow];
            range.Borders.LineStyle = 1;
            range.AutoFilter();
            sheet.Columns.AutoFit();
            sheet.Rows[1].RowHeight = 24;
            sheet.Rows[2].RowHeight = 34;
        }

        private void ColorExcelStatusRow(dynamic range, string status)
        {
            string value = status ?? "";
            if (value == "OK") range.Interior.Color = ExcelRgb(226, 239, 218);
            else if (value.IndexOf("THIẾU", StringComparison.OrdinalIgnoreCase) >= 0)
                range.Interior.Color = ExcelRgb(255, 199, 206);
            else if (value.IndexOf("DƯ", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("TRÙNG", StringComparison.OrdinalIgnoreCase) >= 0)
                range.Interior.Color = ExcelRgb(255, 235, 156);
            else if (value.IndexOf("SAI", StringComparison.OrdinalIgnoreCase) >= 0)
                range.Interior.Color = ExcelRgb(255, 217, 102);
            else range.Interior.Color = ExcelRgb(217, 217, 217);
        }

        private void WriteMissingBalloonNotice(
            dynamic noticeCell,
            dynamic partNumberCell,
            BalloonCheckErrorRow row)
        {
            if (row == null || row.SoLuongBom <= row.InstanceCoBalloon)
                return;

            int missing = row.SoLuongBom - row.InstanceCoBalloon;
            noticeCell.Value = "THIẾU " + missing + " BALLOON";
            noticeCell.Interior.Color = ExcelRgb(192, 0, 0);
            noticeCell.Font.Color = ExcelRgb(255, 255, 255);
            noticeCell.Font.Bold = true;
            noticeCell.HorizontalAlignment = -4108;
            partNumberCell.Interior.Color = ExcelRgb(192, 0, 0);
            partNumberCell.Font.Color = ExcelRgb(255, 255, 255);
            partNumberCell.Font.Bold = true;
        }

        private void ColorExcelUnitRow(dynamic range, string unitName,
            Dictionary<string, int> unitColorIndexes)
        {
            string key = string.IsNullOrWhiteSpace(unitName) ? "(KHÔNG CÓ UNIT)" : unitName.Trim();
            int colorIndex;
            if (!unitColorIndexes.TryGetValue(key, out colorIndex))
            {
                colorIndex = unitColorIndexes.Count % 6;
                unitColorIndexes[key] = colorIndex;
            }

            switch (colorIndex)
            {
                case 0: range.Interior.Color = ExcelRgb(221, 235, 247); break;
                case 1: range.Interior.Color = ExcelRgb(226, 239, 218); break;
                case 2: range.Interior.Color = ExcelRgb(252, 228, 214); break;
                case 3: range.Interior.Color = ExcelRgb(228, 223, 236); break;
                case 4: range.Interior.Color = ExcelRgb(255, 242, 204); break;
                default: range.Interior.Color = ExcelRgb(208, 224, 227); break;
            }
        }

        private string GetExcelColumnName(int column)
        {
            return Convert.ToChar('A' + column - 1).ToString();
        }

        private static int ExcelRgb(int red, int green, int blue)
        {
            return red + (green << 8) + (blue << 16);
        }
    }

    /// <summary>
    /// Scans all sheets and all model views (including section/detail views).
    /// Each physical Component2 occurrence is counted once, even if ballooned
    /// in more than one view. The unique count is compared with BOM 数量.
    /// </summary>
    public sealed class CheckBalloon
    {
        private readonly ISldWorks swApp;
        private Func<bool> cancellationRequested;

        private sealed class DrawingViewGroup
        {
            public string SheetName { get; set; }
            public List<SolidWorks.Interop.sldworks.View> Views { get; private set; }

            public DrawingViewGroup()
            {
                Views = new List<SolidWorks.Interop.sldworks.View>();
            }
        }

        public CheckBalloon(ISldWorks app)
        {
            swApp = app;
        }

        public BalloonCheckResult Run()
        {
            ModelDoc2 activeModel = swApp == null ? null : swApp.ActiveDoc as ModelDoc2;
            return Run(activeModel);
        }

        public BalloonCheckResult Run(ModelDoc2 targetModel)
        {
            BalloonCheckResult result = new BalloonCheckResult();
            ModelDoc2 model = targetModel;
            DrawingDoc drawing = model as DrawingDoc;
            if (model == null || drawing == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                result.BomDataErrorCount++;
                result.AddRow("LỖI", "", "", "", 0, 0, 0, "Hãy mở Drawing trước.");
                return result;
            }

            result.UnitName = GetDocumentDisplayName(model);

            string originalSheet = GetCurrentSheetName(drawing);
            Dictionary<string, BomItem> bomItems = new Dictionary<string, BomItem>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> componentToPart = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<DrawingViewGroup> viewGroups = GetDrawingViewGroups(drawing, true);
            Debug.WriteLine("[CHECK BALLOON] run drawing=" + result.UnitName
                + ", sheets=" + viewGroups.Count);

            try
            {
                // The expected list comes directly from the top-level components
                // of the assembly/configuration referenced by the root drawing view.
                BuildExpectedFromReferencedAssembly(
                    viewGroups, result, bomItems, componentToPart);
                if (IsCancellationRequested())
                    return result;

                // GetViews returns one group per sheet. The sheet view itself is
                // removed by GetDrawingViewGroups, leaving only model views.
                foreach (DrawingViewGroup group in viewGroups)
                {
                    Application.DoEvents();
                    if (IsCancellationRequested())
                        return result;
                    result.SheetCount++;
                    foreach (SolidWorks.Interop.sldworks.View view in group.Views)
                    {
                        Application.DoEvents();
                        if (IsCancellationRequested())
                            return result;
                        result.ViewCount++;
                        ScanView(view, group.SheetName, bomItems, componentToPart, result);
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalSheet))
                    drawing.ActivateSheet(originalSheet);
            }

            BuildBomSummary(bomItems, result);
            MarkMissingComponents(model, viewGroups, bomItems, result);
            model.GraphicsRedraw2();
            Debug.WriteLine("[CHECK BALLOON] done sheets=" + result.SheetCount
                + ", views=" + result.ViewCount + ", bomRows=" + result.BomRowCount
                + ", quantity=" + result.ExpectedCount + ", covered=" + result.ValidCount
                + ", missing=" + result.MissingCount + ", excess=" + result.ExcessCount
                + ", duplicate=" + result.DuplicateCount + ", dangling=" + result.DanglingCount
                + ", unknown=" + result.UnknownCount + ", wrong=" + result.WrongTextCount);
            return result;
        }

        public BalloonCheckResult RunBatch(IEnumerable<string> drawingPaths,
            Action<int> beginProgress, Action<int, int> updateProgress, Action finishProgress,
            Func<bool> isCancellationRequested = null)
        {
            BalloonCheckResult combined = new BalloonCheckResult();
            List<string> paths = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (drawingPaths != null)
            {
                foreach (string value in drawingPaths)
                {
                    string path = value ?? "";
                    if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
                        paths.Add(path);
                }
            }

            combined.UnitName = paths.Count == 1
                ? Path.GetFileNameWithoutExtension(paths[0])
                : "TẤT CẢ UNIT";
            Debug.WriteLine("[CHECK BALLOON] selected drawings=" + paths.Count
                + ", report=" + combined.UnitName);
            LogInteropIdentity();

            ModelDoc2 original = swApp == null ? null : swApp.ActiveDoc as ModelDoc2;
            HashSet<string> initiallyOpenDocumentPaths = GetOpenDocumentPaths();
            Func<bool> previousCancellation = cancellationRequested;
            cancellationRequested = isCancellationRequested;
            beginProgress?.Invoke(Math.Max(1, paths.Count));
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    string path = paths[i];
                    Debug.WriteLine("[CHECK BALLOON] batch " + (i + 1) + "/" + paths.Count
                        + " drawing=" + path);
                    updateProgress?.Invoke(i + 1, paths.Count);
                    Application.DoEvents();
                    if (IsCancellationRequested())
                        break;

                    if (!File.Exists(path))
                    {
                        combined.BomDataErrorCount++;
                        combined.UnitName = Path.GetFileNameWithoutExtension(path);
                        combined.AddRow("KHÔNG TÌM THẤY", "", "", "", 0, 0, 0,
                            "Không tìm thấy Drawing: " + path);
                        combined.UnitName = "TẤT CẢ UNIT";
                        continue;
                    }

                    string normalizedPath = NormalizeDocumentPath(path);
                    bool wasOpenBeforeBatch = initiallyOpenDocumentPaths.Contains(normalizedPath);
                    ModelDoc2 drawing = FindOpenDocument(path);
                    bool openedByCommand = false;
                    if (drawing == null)
                    {
                        int openErrors = 0;
                        int openWarnings = 0;
                        int openOptions = (int)swOpenDocOptions_e.swOpenDocOptions_Silent
                            | (int)swOpenDocOptions_e.swOpenDocOptions_LoadModel;
                        drawing = swApp.OpenDoc6(path, (int)swDocumentTypes_e.swDocDRAWING,
                            openOptions, "",
                            ref openErrors, ref openWarnings) as ModelDoc2;
                        openedByCommand = drawing != null
                            && !wasOpenBeforeBatch
                            && !IsSameDocument(drawing, original);
                        Debug.WriteLine("[CHECK BALLOON] OpenDoc6 errors=" + openErrors
                            + ", warnings=" + openWarnings
                            + ", openedByCommand=" + openedByCommand);
                    }
                    if (drawing == null)
                    {
                        combined.BomDataErrorCount++;
                        combined.UnitName = Path.GetFileNameWithoutExtension(path);
                        combined.AddRow("KHÔNG MỞ ĐƯỢC", "", "", "", 0, 0, 0,
                            "Không mở được Drawing để kiểm tra.");
                        combined.UnitName = "TẤT CẢ UNIT";
                        continue;
                    }

                    try
                    {
                        int activateErrors = 0;
                        swApp.ActivateDoc3(drawing.GetTitle(), false, 0, ref activateErrors);
                        ModelDoc2 activeAfterActivate = swApp.ActiveDoc as ModelDoc2;
                        string targetPath = GetModelPath(drawing);
                        string activePath = GetModelPath(activeAfterActivate);
                        Debug.WriteLine("[CHECK BALLOON] ActivateDoc3 errors=" + activateErrors
                            + ", target=" + targetPath + ", active=" + activePath);

                        bool activatedTarget = activeAfterActivate != null
                            && string.Equals(targetPath, activePath, StringComparison.OrdinalIgnoreCase);
                        bool drawingReady = activatedTarget && WaitForDrawingReady(drawing);
                        if (!drawingReady)
                        {
                            combined.BomDataErrorCount++;
                            combined.UnitName = Path.GetFileNameWithoutExtension(path);
                            combined.AddRow("DRAWING CHƯA NẠP", "", "", "", 0, 0, 0,
                                activatedTarget
                                    ? "SolidWorks chưa nạp đủ Sheet/View của Drawing để kiểm tra. Drawing được giữ mở."
                                    : "Không kích hoạt đúng Drawing từ grid. Target=" + targetPath
                                        + "; Active=" + activePath + ". Drawing được giữ mở.");
                            combined.UnitName = "TẤT CẢ UNIT";
                            continue;
                        }
                        BalloonCheckResult oneDrawing = Run(drawing);
                        combined.Merge(oneDrawing);
                    }
                    finally
                    {
                        if (openedByCommand)
                        {
                            try { swApp.CloseDoc(drawing.GetTitle()); }
                            catch { }
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    finishProgress?.Invoke();
                }
                finally
                {
                    cancellationRequested = previousCancellation;
                }
            }
            return combined;
        }

        private bool WaitForDrawingReady(ModelDoc2 model)
        {
            DrawingDoc drawing = model as DrawingDoc;
            if (drawing == null)
                return false;
            try { model.ForceRebuild3(false); }
            catch { }

            Stopwatch wait = Stopwatch.StartNew();
            int attempt = 0;
            int lastNamedSheetCount = 0;
            string lastCurrentName = "";
            string lastFirstViewName = "";
            string lastError = "";
            while (wait.Elapsed < TimeSpan.FromSeconds(3))
            {
                if (IsCancellationRequested())
                    return false;
                try
                {
                    if (attempt == 0)
                        LogDrawingApiSnapshot(drawing, "initial");

                    List<DrawingViewGroup> apiViewGroups = GetDrawingViewGroups(drawing, attempt == 0);
                    bool hasModelView = false;
                    foreach (DrawingViewGroup group in apiViewGroups)
                    {
                        if (group.Views.Count > 0)
                        {
                            hasModelView = true;
                            break;
                        }
                    }
                    if (apiViewGroups.Count > 0 && hasModelView)
                    {
                        Debug.WriteLine("[CHECK BALLOON] Drawing ready by GetViews attempt=" + attempt
                            + ", sheets=" + apiViewGroups.Count);
                        return true;
                    }

                    object raw = drawing.GetSheetNames();
                    Array values = raw as Array;
                    int namedSheetCount = 0;
                    if (values != null)
                    {
                        foreach (object value in values)
                        {
                            if (!string.IsNullOrWhiteSpace(Convert.ToString(value)))
                                namedSheetCount++;
                        }
                    }
                    Sheet current = drawing.GetCurrentSheet() as Sheet;
                    string currentName = current == null ? "" : current.GetName() ?? "";

                    if (current == null && values != null)
                    {
                        foreach (object value in values)
                        {
                            string sheetName = Convert.ToString(value);
                            if (string.IsNullOrWhiteSpace(sheetName))
                                continue;
                            drawing.ActivateSheet(sheetName);
                            current = drawing.GetCurrentSheet() as Sheet;
                            currentName = current == null ? "" : current.GetName() ?? "";
                            break;
                        }
                    }

                    SolidWorks.Interop.sldworks.View firstView =
                        drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;

                    lastNamedSheetCount = namedSheetCount;
                    lastCurrentName = currentName;
                    lastFirstViewName = GetViewName(firstView);
                    if ((namedSheetCount > 0 || !string.IsNullOrWhiteSpace(currentName))
                        && current != null && firstView != null)
                    {
                        Debug.WriteLine("[CHECK BALLOON] Drawing ready attempt=" + attempt
                            + ", namedSheets=" + namedSheetCount
                            + ", currentSheet=" + currentName
                            + ", firstView=" + GetViewName(firstView));
                        return true;
                    }

                    if (attempt % 20 == 0)
                    {
                        Debug.WriteLine("[CHECK BALLOON] waiting drawing attempt=" + attempt
                            + ", elapsedMs=" + wait.ElapsedMilliseconds
                            + ", namedSheets=" + namedSheetCount
                            + ", currentSheet=" + currentName
                            + ", firstView=" + lastFirstViewName);
                        if (!string.IsNullOrWhiteSpace(currentName))
                            drawing.ActivateSheet(currentName);
                        try { model.ForceRebuild3(false); }
                        catch { }
                        try { model.GraphicsRedraw2(); }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    if (attempt % 20 == 0)
                        Debug.WriteLine("[CHECK BALLOON] waiting drawing error=" + ex.Message);
                }
                Application.DoEvents();
                Thread.Sleep(100);
                attempt++;
            }
            Debug.WriteLine("[CHECK BALLOON] Drawing not ready after "
                + wait.ElapsedMilliseconds + " ms: " + model.GetPathName()
                + ", namedSheets=" + lastNamedSheetCount
                + ", currentSheet=" + lastCurrentName
                + ", firstView=" + lastFirstViewName
                + ", lastError=" + lastError);
            return false;
        }

        private List<DrawingViewGroup> GetDrawingViewGroups(DrawingDoc drawing, bool writeLog)
        {
            List<DrawingViewGroup> result = new List<DrawingViewGroup>();
            if (drawing == null)
                return result;

            try
            {
                object rawViews = drawing.GetViews();
                Array groups = rawViews as Array;
                if (groups == null)
                    return result;

                int sheetIndex = 0;
                foreach (object rawGroup in groups)
                {
                    sheetIndex++;
                    Array values = rawGroup as Array;
                    if (values == null || values.Length == 0)
                        continue;

                    DrawingViewGroup group = new DrawingViewGroup();
                    group.SheetName = "Sheet " + sheetIndex;
                    int viewIndex = 0;
                    foreach (object rawView in values)
                    {
                        SolidWorks.Interop.sldworks.View view =
                            rawView as SolidWorks.Interop.sldworks.View;
                        if (view == null)
                            continue;

                        if (viewIndex == 0)
                        {
                            string sheetName = GetViewName(view);
                            if (!string.IsNullOrWhiteSpace(sheetName) && sheetName != "(view)")
                                group.SheetName = sheetName;
                        }
                        else
                        {
                            group.Views.Add(view);
                        }
                        viewIndex++;
                    }
                    result.Add(group);
                }

                if (writeLog)
                {
                    List<string> summary = new List<string>();
                    foreach (DrawingViewGroup group in result)
                        summary.Add(group.SheetName + "=" + group.Views.Count);
                    Debug.WriteLine("[CHECK BALLOON] GetViews groups=" + result.Count
                        + ", modelViews=" + string.Join(",", summary));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK BALLOON] GetViews ERROR: " + ex.Message);
            }
            return result;
        }

        private void LogInteropIdentity()
        {
            try
            {
                System.Reflection.Assembly interopAssembly = typeof(ModelDoc2).Assembly;
                Debug.WriteLine("[CHECK BALLOON][API] interop=" + interopAssembly.FullName
                    + ", location=" + interopAssembly.Location);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK BALLOON][API] interop identity error=" + ex.Message);
            }
        }

        private void LogDrawingApiSnapshot(DrawingDoc drawing, string stage)
        {
            if (drawing == null)
                return;

            int sheetCount = -1;
            int viewCount = -1;
            string viewsType = "null";
            int viewGroupCount = -1;
            List<string> groupSizes = new List<string>();
            string error = "";

            try { sheetCount = drawing.GetSheetCount(); }
            catch (Exception ex) { error += " GetSheetCount=" + ex.Message; }

            try { viewCount = drawing.GetViewCount(); }
            catch (Exception ex) { error += " GetViewCount=" + ex.Message; }

            try
            {
                object rawViews = drawing.GetViews();
                viewsType = rawViews == null ? "null" : rawViews.GetType().FullName;
                Array groups = rawViews as Array;
                if (groups != null)
                {
                    viewGroupCount = groups.Length;
                    int index = 0;
                    foreach (object rawGroup in groups)
                    {
                        Array group = rawGroup as Array;
                        groupSizes.Add(index + ":" + (group == null ? -1 : group.Length));
                        index++;
                    }
                }
            }
            catch (Exception ex)
            {
                error += " GetViews=" + ex.Message;
            }

            Debug.WriteLine("[CHECK BALLOON][API] stage=" + stage
                + ", GetSheetCount=" + sheetCount
                + ", GetViewCount=" + viewCount
                + ", GetViewsType=" + viewsType
                + ", groups=" + viewGroupCount
                + ", groupSizes=" + string.Join(",", groupSizes)
                + ", errors=" + error.Trim());
        }

        private string GetModelPath(ModelDoc2 model)
        {
            if (model == null)
                return "";
            try
            {
                string path = model.GetPathName();
                return string.IsNullOrWhiteSpace(path) ? model.GetTitle() ?? "" : path;
            }
            catch { return ""; }
        }

        private string GetDocumentTitle(ModelDoc2 model)
        {
            if (model == null)
                return "";
            try { return model.GetTitle() ?? ""; }
            catch { return ""; }
        }

        private string NormalizeDocumentPath(string path)
        {
            string value = (path ?? "").Trim();
            if (value.Length == 0)
                return "";
            try
            {
                if (Path.IsPathRooted(value))
                    value = Path.GetFullPath(value);
            }
            catch { }
            return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }

        private List<ModelDoc2> GetOpenDocuments()
        {
            List<ModelDoc2> documents = new List<ModelDoc2>();
            if (swApp == null)
                return documents;
            try
            {
                Array values = ((dynamic)swApp).GetDocuments() as Array;
                if (values == null)
                    return documents;
                foreach (object value in values)
                {
                    ModelDoc2 document = value as ModelDoc2;
                    if (document != null)
                        documents.Add(document);
                }
            }
            catch { }
            return documents;
        }

        private HashSet<string> GetOpenDocumentPaths()
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModelDoc2 document in GetOpenDocuments())
            {
                string path = NormalizeDocumentPath(GetModelPath(document));
                if (path.Length > 0)
                    paths.Add(path);
            }
            return paths;
        }

        private ModelDoc2 FindOpenDocument(string pathOrTitle)
        {
            if (swApp == null || string.IsNullOrWhiteSpace(pathOrTitle))
                return null;
            try
            {
                ModelDoc2 direct = swApp.GetOpenDocumentByName(pathOrTitle) as ModelDoc2;
                if (direct != null)
                    return direct;
            }
            catch { }

            string wanted = NormalizeDocumentPath(pathOrTitle);
            foreach (ModelDoc2 document in GetOpenDocuments())
            {
                if (string.Equals(NormalizeDocumentPath(GetModelPath(document)), wanted,
                    StringComparison.OrdinalIgnoreCase))
                    return document;
            }
            return null;
        }

        private bool IsSameDocument(ModelDoc2 left, ModelDoc2 right)
        {
            if (left == null || right == null)
                return false;
            return string.Equals(
                NormalizeDocumentPath(GetModelPath(left)),
                NormalizeDocumentPath(GetModelPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }

        private void RestoreOriginalDocument(ModelDoc2 original, string originalPath,
            string originalTitle, string originalSheet)
        {
            if (swApp == null)
                return;

            ModelDoc2 target = FindOpenDocument(originalPath);
            if (target == null)
                target = FindOpenDocument(originalTitle);
            if (target == null && original != null)
            {
                try
                {
                    original.GetTitle();
                    target = original;
                }
                catch { }
            }
            if (target == null)
            {
                Debug.WriteLine("[CHECK BALLOON] restore original failed: document is no longer open."
                    + " path=" + originalPath + ", title=" + originalTitle);
                return;
            }

            try
            {
                int activateErrors = 0;
                string title = GetDocumentTitle(target);
                swApp.ActivateDoc3(title, false, 0, ref activateErrors);
                ModelDoc2 active = swApp.ActiveDoc as ModelDoc2;
                DrawingDoc activeDrawing = active as DrawingDoc;
                if (activeDrawing != null && !string.IsNullOrWhiteSpace(originalSheet))
                    activeDrawing.ActivateSheet(originalSheet);
                try { active?.GraphicsRedraw2(); }
                catch { }
                Debug.WriteLine("[CHECK BALLOON] restore original errors=" + activateErrors
                    + ", target=" + originalPath + ", active=" + GetModelPath(active)
                    + ", sheet=" + originalSheet);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK BALLOON] restore original exception=" + ex.Message);
            }
        }

        private void BuildExpectedFromReferencedAssembly(IEnumerable<DrawingViewGroup> viewGroups,
            BalloonCheckResult result, Dictionary<string, BomItem> bomItems,
            Dictionary<string, string> componentToPart)
        {
            ModelDoc2 referencedAssembly = null;
            string referencedConfiguration = "";
            string sourceSheet = "";
            string sourceView = "";

            foreach (DrawingViewGroup group in viewGroups)
            {
                foreach (SolidWorks.Interop.sldworks.View view in group.Views)
                {
                    ModelDoc2 referenced = null;
                    try { referenced = view.ReferencedDocument as ModelDoc2; }
                    catch { }
                    if (referenced != null && referenced.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
                    {
                        referencedAssembly = referenced;
                        try { referencedConfiguration = view.ReferencedConfiguration ?? ""; }
                        catch { }
                        sourceSheet = group.SheetName;
                        sourceView = GetViewName(view);
                        break;
                    }
                }
                if (referencedAssembly != null)
                    break;
            }

            if (referencedAssembly == null)
            {
                result.BomDataErrorCount++;
                result.AddRow("KHÔNG CÓ ASSEMBLY", "", "", "", 0, 0, 0,
                    "Không tìm thấy Drawing View gốc tham chiếu assembly.");
                return;
            }

            ConfigurationManager configurationManager = null;
            string originalConfiguration = "";
            try
            {
                configurationManager = referencedAssembly.ConfigurationManager as ConfigurationManager;
                Configuration activeConfiguration = configurationManager == null
                    ? null : configurationManager.ActiveConfiguration as Configuration;
                originalConfiguration = activeConfiguration == null ? "" : activeConfiguration.Name ?? "";
                if (!string.IsNullOrWhiteSpace(referencedConfiguration)
                    && !string.Equals(originalConfiguration, referencedConfiguration, StringComparison.OrdinalIgnoreCase))
                    referencedAssembly.ShowConfiguration2(referencedConfiguration);

                AssemblyDoc assembly = referencedAssembly as AssemblyDoc;
                object[] components = GetTopLevelComponentsForConfiguration(
                    referencedAssembly, referencedConfiguration);
                if (components == null)
                    components = new object[0];

                foreach (object value in components)
                {
                    Application.DoEvents();
                    if (IsCancellationRequested())
                        return;
                    Component2 component = value as Component2;
                    bool include = ShouldIncludeTopLevelComponent(component);
                    if (!include)
                    {
                        Debug.WriteLine("[CHECK BALLOON] expected skip component="
                            + GetComponentDisplayName(component));
                        continue;
                    }

                    string partNumber = GetComponentPartNumber(component).Trim();
                    Debug.WriteLine("[CHECK BALLOON] expected include component="
                        + GetComponentDisplayName(component) + ", partNumber=" + partNumber
                        + ", model=" + GetComponentModelKey(component));
                    if (string.IsNullOrWhiteSpace(partNumber))
                    {
                        result.BomDataErrorCount++;
                        result.AddRow("THIẾU 部品番号", sourceSheet, sourceView, "", 0, 0, 0,
                            "Component không có custom property 部品番号: " + GetComponentDisplayName(component));
                        continue;
                    }

                    string normalizedPart = Normalize(partNumber);
                    BomItem item;
                    if (!bomItems.TryGetValue(normalizedPart, out item))
                    {
                        item = new BomItem { PartNumber = partNumber, SheetName = sourceSheet };
                        bomItems.Add(normalizedPart, item);
                    }
                    item.Quantity++;
                    string componentKey = GetComponentKey(component);
                    if (!string.IsNullOrWhiteSpace(componentKey))
                    {
                        item.BomComponentKeys.Add(componentKey);
                        componentToPart[componentKey] = normalizedPart;
                    }
                    string modelKey = GetComponentModelKey(component);
                    if (!string.IsNullOrWhiteSpace(modelKey))
                        componentToPart[modelKey] = normalizedPart;
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalConfiguration)
                    && !string.Equals(originalConfiguration, referencedConfiguration, StringComparison.OrdinalIgnoreCase))
                {
                    try { referencedAssembly.ShowConfiguration2(originalConfiguration); }
                    catch { }
                }
            }

            result.BomRowCount = bomItems.Count;
            Debug.WriteLine("[CHECK BALLOON] expected from assembly=" + referencedAssembly.GetPathName()
                + ", configuration=" + referencedConfiguration + ", items=" + bomItems.Count);
        }

        private bool IsCancellationRequested()
        {
            try
            {
                return cancellationRequested != null && cancellationRequested();
            }
            catch
            {
                return false;
            }
        }

        private bool ShouldIncludeTopLevelComponent(Component2 component)
        {
            if (component == null)
                return false;
            if (IsComponentSuppressed(component))
            {
                Debug.WriteLine("[CHECK BALLOON] expected skip suppressed component="
                    + GetComponentDisplayName(component));
                return false;
            }
            try { if (component.IsEnvelope()) return false; }
            catch { }
            try { if (component.ExcludeFromBOM) return false; }
            catch { }
            return true;
        }

        private bool IsComponentSuppressed(Component2 component)
        {
            if (component == null)
                return true;
            try
            {
                int state = component.GetSuppression2();
                return state == (int)swComponentSuppressionState_e.swComponentSuppressed;
            }
            catch { }
            try
            {
                return component.GetSuppression()
                    == (int)swComponentSuppressionState_e.swComponentSuppressed;
            }
            catch
            {
                // An unknown/unloaded state is not proof that the component is
                // suppressed. Keep it instead of deleting a valid BOM item.
                return false;
            }
        }

        private object[] GetTopLevelComponentsForConfiguration(
            ModelDoc2 assemblyModel, string configurationName)
        {
            if (assemblyModel == null)
                return null;

            try
            {
                Configuration configuration = null;
                if (!string.IsNullOrWhiteSpace(configurationName))
                    configuration = assemblyModel.GetConfigurationByName(configurationName) as Configuration;
                if (configuration == null)
                {
                    ConfigurationManager manager = assemblyModel.ConfigurationManager as ConfigurationManager;
                    configuration = manager == null
                        ? null : manager.ActiveConfiguration as Configuration;
                }

                Component2 root = configuration == null
                    ? null : configuration.GetRootComponent3(true) as Component2;
                object[] children = root == null ? null : root.GetChildren() as object[];
                if (children != null)
                {
                    Debug.WriteLine("[CHECK BALLOON] configuration tree="
                        + (configuration == null ? "" : configuration.Name)
                        + ", top components=" + children.Length);
                    return children;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK BALLOON] configuration tree failed=" + ex.Message);
            }

            AssemblyDoc assembly = assemblyModel as AssemblyDoc;
            return assembly == null ? null : assembly.GetComponents(true) as object[];
        }

        private bool IsComponentOrAncestorSuppressed(Component2 component)
        {
            Component2 current = component;
            int depth = 0;
            while (current != null && depth++ < 32)
            {
                if (IsComponentSuppressed(current))
                    return true;
                try { current = current.GetParent() as Component2; }
                catch { current = null; }
            }
            return false;
        }

        private void ReadBomTablesOnCurrentSheet(DrawingDoc drawing, BalloonCheckResult result,
            Dictionary<string, BomItem> bomItems, Dictionary<string, string> componentToPart, string sheet)
        {
            HashSet<ITableAnnotation> seen = new HashSet<ITableAnnotation>();
            SolidWorks.Interop.sldworks.View view = drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
            while (view != null)
            {
                ITableAnnotation table = null;
                try { table = view.GetFirstTableAnnotation() as ITableAnnotation; }
                catch { }
                while (table != null)
                {
                    ITableAnnotation next = null;
                    try { next = table.GetNext() as ITableAnnotation; }
                    catch { }
                    if (seen.Add(table))
                        ReadBomTable(table, result, bomItems, componentToPart, sheet);
                    table = next;
                }
                view = view.GetNextView() as SolidWorks.Interop.sldworks.View;
            }
        }

        private void ReadBomTable(ITableAnnotation table, BalloonCheckResult result,
            Dictionary<string, BomItem> bomItems, Dictionary<string, string> componentToPart, string sheet)
        {
            IBomTableAnnotation bom = table as IBomTableAnnotation;
            if (bom == null)
                return;

            int partColumn = FindColumn(table, "部品番号");
            int quantityColumn = FindColumn(table, "個数");
            if (quantityColumn < 0)
                quantityColumn = FindColumn(table, "数量");
            if (partColumn < 0 || quantityColumn < 0)
            {
                result.BomDataErrorCount++;
                result.AddRow("LỖI BOM", sheet, "", "", 0, 0, 0,
                    "Bảng BOM không có đủ cột 部品番号 và 個数/数量.");
                return;
            }

            for (int row = 1; row < table.RowCount; row++)
            {
                string partNumber = SafeCellText(table, row, partColumn).Trim();
                int quantity;
                if (string.IsNullOrWhiteSpace(partNumber))
                    continue;
                if (!TryParseQuantity(SafeCellText(table, row, quantityColumn), out quantity))
                {
                    result.BomDataErrorCount++;
                    result.AddRow("LỖI BOM", sheet, "", partNumber, 0, 0, 0,
                        "Không đọc được 数量 ở dòng BOM " + (row + 1) + ".");
                    continue;
                }

                string normalizedPart = Normalize(partNumber);
                BomItem item;
                if (!bomItems.TryGetValue(normalizedPart, out item))
                {
                    item = new BomItem { PartNumber = partNumber, SheetName = sheet };
                    bomItems.Add(normalizedPart, item);
                }
                item.Quantity += quantity;
                result.BomRowCount++;

                object[] components = null;
                try { components = bom.GetComponents2(row, "") as object[]; }
                catch { }
                if (components == null)
                    continue;
                foreach (object value in components)
                {
                    Component2 component = value as Component2;
                    string key = GetComponentKey(component);
                    if (string.IsNullOrWhiteSpace(key))
                        continue;
                    item.BomComponentKeys.Add(key);
                    componentToPart[key] = normalizedPart;
                    string modelKey = GetComponentModelKey(component);
                    if (!string.IsNullOrWhiteSpace(modelKey))
                        componentToPart[modelKey] = normalizedPart;
                }
            }
        }

        private void ScanView(SolidWorks.Interop.sldworks.View view, string sheet,
            Dictionary<string, BomItem> bomItems, Dictionary<string, string> componentToPart,
            BalloonCheckResult result)
        {
            string viewName = GetViewName(view);
            int balloonCount = 0;
            Note note = null;
            try { note = view.GetFirstNote() as Note; }
            catch { }
            while (note != null)
            {
                Note next = null;
                try { next = note.GetNext() as Note; }
                catch { }

                if (IsBomBalloon(note))
                {
                    string balloonText = GetBalloonText(note).Trim();
                    Annotation annotation = null;
                    try { annotation = note.GetAnnotation() as Annotation; }
                    catch { }
                    Component2 component = GetAttachedComponent(annotation, view);
                    if (component == null)
                    {
                        result.DanglingCount++;
                        result.AddRow("DANGLING", sheet, viewName, balloonText, 0, 0, 1,
                            "Leader không còn trỏ vào component; Balloon này không được tính.");
                        note = next;
                        continue;
                    }

                    if (IsComponentOrAncestorSuppressed(component))
                    {
                        Debug.WriteLine("[CHECK BALLOON] ignore balloon on suppressed component="
                            + GetComponentDisplayName(component) + ", view=" + viewName);
                        note = next;
                        continue;
                    }

                    balloonCount++;
                    result.TotalBalloonFoundCount++;

                    Component2 matchedComponent;
                    BomItem item = ResolveExpectedItem(
                        component, bomItems, componentToPart, out matchedComponent);
                    if (item == null && !string.IsNullOrWhiteSpace(balloonText))
                        bomItems.TryGetValue(Normalize(balloonText), out item);

                    if (item == null)
                    {
                        result.UnknownCount++;
                        result.AddRow("KHÔNG THUỘC BOM", sheet, viewName, balloonText, 0, 0, 1,
                            "Balloon trỏ vào " + GetComponentDisplayName(component) + " nhưng không tìm thấy trong BOM.");
                        note = next;
                        continue;
                    }

                    item.TotalBalloonCount++;
                    string componentKey = GetComponentKey(matchedComponent ?? component);
                    if (!string.IsNullOrWhiteSpace(componentKey))
                    {
                        if (!item.BalloonComponentKeys.Add(componentKey))
                        {
                            item.DuplicateBalloonCount++;
                            result.DuplicateCount++;
                            result.AddRow("TRÙNG INSTANCE", sheet, viewName, item.PartNumber,
                                item.Quantity, item.BalloonComponentKeys.Count, item.TotalBalloonCount,
                                "Component " + GetComponentDisplayName(matchedComponent ?? component)
                                    + " đã có Balloon ở view khác hoặc Balloon khác trong cùng view.");
                        }
                    }

                    if (!IsPropertyLinkText(balloonText)
                        && !string.Equals(Normalize(balloonText), Normalize(item.PartNumber), StringComparison.OrdinalIgnoreCase))
                    {
                        item.WrongTextCount++;
                        result.WrongTextCount++;
                        result.AddRow("SAI SỐ", sheet, viewName, item.PartNumber,
                            item.Quantity, item.BalloonComponentKeys.Count, item.TotalBalloonCount,
                            "Balloon hiển thị '" + balloonText + "' nhưng component thuộc 部品番号 '" + item.PartNumber + "'.");
                    }
                }
                note = next;
            }
            Debug.WriteLine("[CHECK BALLOON] scan sheet=" + sheet + ", view=" + viewName
                + ", balloons=" + balloonCount);
        }

        private void BuildBomSummary(Dictionary<string, BomItem> bomItems, BalloonCheckResult result)
        {
            List<BomItem> sortedItems = new List<BomItem>(bomItems.Values);
            sortedItems.Sort(delegate(BomItem left, BomItem right)
            {
                return ComparePartNumbers(left == null ? "" : left.PartNumber,
                    right == null ? "" : right.PartNumber);
            });
            foreach (BomItem item in sortedItems)
            {
                int unique = item.BalloonComponentKeys.Count;
                int actual = item.TotalBalloonCount;
                int coveredInstances = 0;
                foreach (string componentKey in item.BomComponentKeys)
                {
                    if (item.BalloonComponentKeys.Contains(componentKey))
                        coveredInstances++;
                }
                int missing = Math.Max(0, item.Quantity - coveredInstances);
                int excess = Math.Max(0, actual - item.Quantity);
                result.ExpectedCount += item.Quantity;
                result.ValidCount += coveredInstances;
                result.MissingCount += missing;
                result.ExcessCount += excess;

                List<string> messages = new List<string>();
                if (missing > 0) messages.Add("thiếu " + missing);
                if (excess > 0) messages.Add("dư " + excess);
                if (item.DuplicateBalloonCount > 0)
                    messages.Add("trùng instance " + item.DuplicateBalloonCount);
                if (item.WrongTextCount > 0) messages.Add("sai số " + item.WrongTextCount);
                string status = messages.Count == 0 ? "OK" : string.Join(", ", messages).ToUpperInvariant();
                result.AddRow(status, item.SheetName, "TẤT CẢ VIEW", item.PartNumber,
                    item.Quantity, unique, item.TotalBalloonCount,
                    messages.Count == 0
                        ? "Số Balloon khớp với số component từ assembly."
                        : "Kết quả tổng hợp: " + string.Join(", ", messages) + ".");
            }

            if (bomItems.Count == 0)
            {
                result.BomDataErrorCount++;
                result.AddRow("KHÔNG CÓ DỮ LIỆU", "", "", "", 0, 0, 0,
                    "Không lấy được component cấp trên cùng từ assembly của Drawing View.");
            }
        }

        private void MarkMissingComponents(ModelDoc2 drawingModel,
            IEnumerable<DrawingViewGroup> viewGroups,
            Dictionary<string, BomItem> bomItems,
            BalloonCheckResult result)
        {
            if (drawingModel == null || viewGroups == null || bomItems == null)
                return;

            Dictionary<string, string> missingPartByComponentKey =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (BomItem item in bomItems.Values)
            {
                foreach (string componentKey in item.BomComponentKeys)
                {
                    if (!item.BalloonComponentKeys.Contains(componentKey))
                        missingPartByComponentKey[componentKey] = item.PartNumber;
                }
            }
            if (missingPartByComponentKey.Count == 0)
                return;

            try { drawingModel.ClearSelection2(true); }
            catch { }

            HashSet<string> markedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DrawingViewGroup group in viewGroups)
            {
                foreach (SolidWorks.Interop.sldworks.View view in group.Views)
                {
                    DrawingComponent root = null;
                    try { root = view.RootDrawingComponent as DrawingComponent; }
                    catch { }
                    if (root == null)
                        continue;

                    MarkMissingInDrawingComponentTree(root, group.SheetName, GetViewName(view),
                        missingPartByComponentKey, markedKeys, result);
                    if (markedKeys.Count == missingPartByComponentKey.Count)
                        break;
                }
                if (markedKeys.Count == missingPartByComponentKey.Count)
                    break;
            }

            foreach (KeyValuePair<string, string> missing in missingPartByComponentKey)
            {
                if (markedKeys.Contains(missing.Key))
                    continue;
                result.AddRow("THIẾU BALLOON", "", "", missing.Value, 1, 0, 0,
                    "Không tìm thấy DrawingComponent để đánh dấu instance thiếu Balloon.");
            }
            Debug.WriteLine("[CHECK BALLOON] marked missing components=" + markedKeys.Count
                + "/" + missingPartByComponentKey.Count);
        }

        private void MarkMissingInDrawingComponentTree(DrawingComponent parent,
            string sheet, string viewName,
            Dictionary<string, string> missingPartByComponentKey,
            HashSet<string> markedKeys, BalloonCheckResult result)
        {
            if (parent == null)
                return;

            Array children = null;
            try { children = parent.GetChildren() as Array; }
            catch { }
            if (children == null)
                return;

            foreach (object value in children)
            {
                DrawingComponent drawingComponent = value as DrawingComponent;
                if (drawingComponent == null)
                    continue;

                Component2 component = null;
                try { component = drawingComponent.Component as Component2; }
                catch { }
                string componentKey = GetComponentKey(component);
                string partNumber;
                if (!string.IsNullOrWhiteSpace(componentKey)
                    && missingPartByComponentKey.TryGetValue(componentKey, out partNumber)
                    && !markedKeys.Contains(componentKey))
                {
                    bool selected = false;
                    try { selected = drawingComponent.Select(true, null); }
                    catch { }
                    if (selected)
                    {
                        markedKeys.Add(componentKey);
                        result.AddRow("THIẾU BALLOON - ĐÃ ĐÁNH DẤU", sheet, viewName,
                            partNumber, 1, 0, 0,
                            "Đã chọn component thiếu Balloon: " + GetComponentDisplayName(component) + ".");
                    }
                }

                if (markedKeys.Count < missingPartByComponentKey.Count)
                    MarkMissingInDrawingComponentTree(drawingComponent, sheet, viewName,
                        missingPartByComponentKey, markedKeys, result);
            }
        }

        private BomItem ResolveExpectedItem(Component2 component,
            Dictionary<string, BomItem> bomItems, Dictionary<string, string> componentToPart,
            out Component2 matchedComponent)
        {
            matchedComponent = null;
            Component2 current = component;
            int depth = 0;
            while (current != null && depth++ < 32)
            {
                string normalizedPart;
                BomItem item;
                string componentKey = GetComponentKey(current);
                if (!string.IsNullOrWhiteSpace(componentKey)
                    && componentToPart.TryGetValue(componentKey, out normalizedPart)
                    && bomItems.TryGetValue(normalizedPart, out item))
                {
                    matchedComponent = current;
                    return item;
                }

                string modelKey = GetComponentModelKey(current);
                if (!string.IsNullOrWhiteSpace(modelKey)
                    && componentToPart.TryGetValue(modelKey, out normalizedPart)
                    && bomItems.TryGetValue(normalizedPart, out item))
                {
                    matchedComponent = current;
                    return item;
                }

                string partNumber = GetComponentPartNumber(current);
                if (!string.IsNullOrWhiteSpace(partNumber)
                    && bomItems.TryGetValue(Normalize(partNumber), out item))
                {
                    matchedComponent = current;
                    return item;
                }

                try { current = current.GetParent() as Component2; }
                catch { current = null; }
            }
            return null;
        }

        private int ComparePartNumbers(string left, string right)
        {
            string[] leftParts = Regex.Split(left ?? "", "(\\d+)");
            string[] rightParts = Regex.Split(right ?? "", "(\\d+)");
            int count = Math.Min(leftParts.Length, rightParts.Length);
            for (int i = 0; i < count; i++)
            {
                int leftNumber;
                int rightNumber;
                bool leftIsNumber = int.TryParse(leftParts[i], out leftNumber);
                bool rightIsNumber = int.TryParse(rightParts[i], out rightNumber);
                int comparison = leftIsNumber && rightIsNumber
                    ? leftNumber.CompareTo(rightNumber)
                    : string.Compare(leftParts[i], rightParts[i], StringComparison.OrdinalIgnoreCase);
                if (comparison != 0)
                    return comparison;
            }
            return leftParts.Length.CompareTo(rightParts.Length);
        }

        private bool IsBomBalloon(Note note)
        {
            try { return ((dynamic)note).IsBomBalloon(); }
            catch { return false; }
        }

        private string GetBalloonText(Note note)
        {
            try { return Convert.ToString(((dynamic)note).GetBomBalloonText(true)) ?? ""; }
            catch { }
            try { return Convert.ToString(((dynamic)note).GetText()) ?? ""; }
            catch { return ""; }
        }

        private Component2 GetAttachedComponent(Annotation annotation, SolidWorks.Interop.sldworks.View view)
        {
            if (annotation == null)
                return null;
            object[] entities = GetAttachedEntities(annotation);
            if (entities == null)
                return null;
            foreach (object value in entities)
            {
                Entity entity = value as Entity;
                if (entity != null)
                {
                    Component2 component = null;
                    try
                    {
                        DrawingComponent dc = entity.GetDrawingComponent(view) as DrawingComponent;
                        component = dc == null ? null : dc.Component as Component2;
                    }
                    catch { }
                    if (component == null)
                    {
                        try { component = entity.GetComponent() as Component2; }
                        catch { }
                    }
                    if (component != null)
                        return component;
                }
                DrawingComponent drawingComponent = value as DrawingComponent;
                if (drawingComponent != null)
                {
                    try
                    {
                        Component2 component = drawingComponent.Component as Component2;
                        if (component != null)
                            return component;
                    }
                    catch { }
                }
            }
            return null;
        }

        private object[] GetAttachedEntities(Annotation annotation)
        {
            try { return ((dynamic)annotation).GetAttachedEntities3() as object[]; }
            catch { }
            try { return ((dynamic)annotation).GetAttachedEntities2() as object[]; }
            catch { }
            try { return ((dynamic)annotation).GetAttachedEntities() as object[]; }
            catch { return null; }
        }

        private string GetComponentPartNumber(Component2 component)
        {
            if (component == null)
                return "";
            ModelDoc2 model = null;
            bool openedForProperty = false;
            try
            {
                string configuration = "";
                string path = "";
                try { configuration = component.ReferencedConfiguration ?? ""; }
                catch { }
                try { model = component.GetModelDoc2() as ModelDoc2; }
                catch { }
                try { path = component.GetPathName() ?? ""; }
                catch { }

                if (model == null && !string.IsNullOrWhiteSpace(path))
                {
                    model = swApp.GetOpenDocumentByName(path) as ModelDoc2;
                    if (model == null)
                    {
                        int errors = 0;
                        int warnings = 0;
                        int documentType = string.Equals(Path.GetExtension(path), ".SLDASM",
                            StringComparison.OrdinalIgnoreCase)
                            ? (int)swDocumentTypes_e.swDocASSEMBLY
                            : (int)swDocumentTypes_e.swDocPART;
                        model = OpenComponentDocumentSilently(
                            path, documentType, ref errors, ref warnings);
                        openedForProperty = model != null;
                    }
                }
                if (model == null)
                    return "";
                string value = ReadCustomProperty(model, configuration, "部品番号");
                if (string.IsNullOrWhiteSpace(value))
                    value = ReadCustomProperty(model, "", "部品番号");
                return value;
            }
            catch { return ""; }
            finally
            {
                if (openedForProperty && model != null)
                {
                    try { swApp.CloseDoc(model.GetTitle()); }
                    catch { }
                }
            }
        }

        private ModelDoc2 OpenComponentDocumentSilently(
            string path,
            int documentType,
            ref int errors,
            ref int warnings)
        {
            if (swApp == null || string.IsNullOrWhiteSpace(path))
                return null;

            ModelDoc2 alreadyOpen = swApp.GetOpenDocumentByName(path) as ModelDoc2;
            if (alreadyOpen != null)
                return alreadyOpen;

            bool visibilityChanged = false;
            try
            {
                // Silent suppresses dialogs; DocumentVisible(false) also prevents
                // the component document from appearing while its properties are read.
                swApp.DocumentVisible(false, documentType);
                visibilityChanged = true;

                ModelDoc2 opened = swApp.OpenDoc6(
                    path,
                    documentType,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings) as ModelDoc2;
                Debug.WriteLine("[CHECK BALLOON] silent component open type=" + documentType
                    + ", errors=" + errors + ", warnings=" + warnings + ", path=" + path);
                return opened;
            }
            finally
            {
                if (visibilityChanged)
                {
                    try { swApp.DocumentVisible(true, documentType); }
                    catch { }
                }
            }
        }

        private string ReadCustomProperty(ModelDoc2 model, string configuration, string propertyName)
        {
            try
            {
                CustomPropertyManager manager = model.Extension.get_CustomPropertyManager(configuration ?? "");
                string raw = "";
                string resolved = "";
                bool wasResolved = false;
                bool link = false;
                ((dynamic)manager).Get6(propertyName, false, out raw, out resolved, out wasResolved, out link);
                return string.IsNullOrWhiteSpace(resolved) ? raw ?? "" : resolved;
            }
            catch { return ""; }
        }

        private int FindColumn(ITableAnnotation table, string name)
        {
            string wanted = Normalize(name);
            for (int column = 0; column < table.ColumnCount; column++)
            {
                if (string.Equals(Normalize(SafeCellText(table, 0, column)), wanted,
                    StringComparison.OrdinalIgnoreCase))
                    return column;
            }
            return -1;
        }

        private string SafeCellText(ITableAnnotation table, int row, int column)
        {
            try { return table == null || column < 0 ? "" : table.get_Text(row, column) ?? ""; }
            catch { return ""; }
        }

        private bool TryParseQuantity(string value, out int quantity)
        {
            quantity = 0;
            string text = (value ?? "").Trim().Replace(",", "");
            double number;
            if (!double.TryParse(text, out number) || number < 0)
                return false;
            quantity = Convert.ToInt32(number);
            return true;
        }

        private string GetCurrentSheetName(DrawingDoc drawing)
        {
            try
            {
                Sheet sheet = drawing.GetCurrentSheet() as Sheet;
                return sheet == null ? "" : sheet.GetName() ?? "";
            }
            catch { return ""; }
        }

        private List<string> GetDrawingSheetNames(DrawingDoc drawing)
        {
            List<string> names = new List<string>();
            if (drawing == null)
                return names;
            try
            {
                object raw = drawing.GetSheetNames();
                Array values = raw as Array;
                if (values != null)
                {
                    foreach (object value in values)
                    {
                        string name = Convert.ToString(value);
                        if (!string.IsNullOrWhiteSpace(name))
                            names.Add(name);
                    }
                }
                Debug.WriteLine("[CHECK BALLOON] GetSheetNames type="
                    + (raw == null ? "null" : raw.GetType().FullName)
                    + ", count=" + names.Count);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK BALLOON] GetSheetNames ERROR: " + ex.Message);
            }
            if (names.Count == 0)
            {
                string current = GetCurrentSheetName(drawing);
                if (!string.IsNullOrWhiteSpace(current))
                    names.Add(current);
            }
            return names;
        }

        private string GetDocumentDisplayName(ModelDoc2 model)
        {
            if (model == null)
                return "";
            try
            {
                string path = model.GetPathName();
                if (!string.IsNullOrWhiteSpace(path))
                    return Path.GetFileNameWithoutExtension(path);
                return model.GetTitle() ?? "";
            }
            catch { return ""; }
        }

        private string GetViewName(SolidWorks.Interop.sldworks.View view)
        {
            try { return view == null ? "(view)" : view.Name ?? "(view)"; }
            catch { return "(view)"; }
        }

        private string Normalize(string value)
        {
            return (value ?? "").Replace(" ", "").Replace("　", "")
                .Replace("\r", "").Replace("\n", "").Trim();
        }

        private bool IsPropertyLinkText(string value)
        {
            string text = (value ?? "").Trim();
            return text.StartsWith("$PRP", StringComparison.OrdinalIgnoreCase);
        }

        private string GetComponentModelKey(Component2 component)
        {
            if (component == null)
                return "";
            string path = "";
            string configuration = "";
            try { path = component.GetPathName() ?? ""; }
            catch { }
            try { configuration = component.ReferencedConfiguration ?? ""; }
            catch { }
            if (string.IsNullOrWhiteSpace(path))
                return "";
            return "MODEL|" + path.Trim().ToUpperInvariant() + "|"
                + configuration.Trim().ToUpperInvariant();
        }

        private string GetComponentKey(Component2 component)
        {
            if (component == null)
                return "";
            string name = "";
            string path = "";
            string configuration = "";
            try { name = component.Name2 ?? ""; }
            catch { }
            try { path = component.GetPathName() ?? ""; }
            catch { }
            try { configuration = component.ReferencedConfiguration ?? ""; }
            catch { }
            return path.Trim().ToUpperInvariant() + "|" + configuration.Trim().ToUpperInvariant()
                + "|" + name.Trim().ToUpperInvariant();
        }

        private string GetComponentDisplayName(Component2 component)
        {
            if (component == null)
                return "(unknown)";
            try { return component.Name2 ?? component.GetPathName() ?? "(unknown)"; }
            catch { return "(unknown)"; }
        }

        private sealed class BomItem
        {
            public string PartNumber { get; set; }
            public string SheetName { get; set; }
            public int Quantity { get; set; }
            public int TotalBalloonCount { get; set; }
            public int DuplicateBalloonCount { get; set; }
            public int WrongTextCount { get; set; }
            public HashSet<string> BomComponentKeys { get; private set; }
            public HashSet<string> BalloonComponentKeys { get; private set; }

            public BomItem()
            {
                BomComponentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                BalloonComponentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
