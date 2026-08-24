using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal enum DrawingBomCheckStatus
    {
        OK,
        NG,
        Warning
    }

    internal sealed class DrawingDisplayedData
    {
        // 4 trường cốt lõi (Cụm góc dưới bên phải)
        public string PartNumber { get; set; } = "";
        public string Width { get; set; } = "";
        public string Length { get; set; } = "";
        public string Quantity { get; set; } = "";

        public string PartNumberRaw { get; set; } = "";
        public string WidthRaw { get; set; } = "";
        public string LengthRaw { get; set; } = "";
        public string QuantityRaw { get; set; } = "";

        public string PartNumberSource { get; set; } = "MANUAL_OR_STATIC";
        public string WidthSource { get; set; } = "MANUAL_OR_STATIC";
        public string LengthSource { get; set; } = "MANUAL_OR_STATIC";
        public string QuantitySource { get; set; } = "MANUAL_OR_STATIC";

        // Cụm góc dưới bên phải (Bảng 合番)
        public string Goban { get; set; } = "";

        // Cụm góc dưới bên trái (Tên file & DXF)
        public string PartFileName { get; set; } = "";
        public string DxfFileName { get; set; } = "";

        // Cụm góc trên bên trái (Khung tên công trình)
        public string Material { get; set; } = "";
        public string Thickness { get; set; } = "";
        public string Finish { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string SiteName { get; set; } = "";
        public string JobNo { get; set; } = "";
        public string TehaiNo { get; set; } = "";

        public bool HeaderFound { get; set; }
        public double HeaderY { get; set; }
    }

    internal sealed class DrawingBomFieldResult
    {
        public string FieldName { get; set; } = "";
        public string BomValue { get; set; } = "";
        public string DrawingValue { get; set; } = "";
        public string Source { get; set; } = "MANUAL_OR_STATIC";
        public DrawingBomCheckStatus Status { get; set; } = DrawingBomCheckStatus.OK;
        public string Message { get; set; } = "";
    }

    internal sealed class DrawingCheckItemResult
    {
        public int BomRowIndex { get; set; } = -1;
        public string PartNumber { get; set; } = "";
        public string Component { get; set; } = "";
        public string PartPath { get; set; } = "";
        public string DrawingPath { get; set; } = "";
        public DrawingBomCheckStatus Status { get; set; } = DrawingBomCheckStatus.OK;
        public List<DrawingBomFieldResult> Fields { get; } = new List<DrawingBomFieldResult>();
        public string Note { get; set; } = "";
    }

    internal sealed class DrawingBatchCheckResult
    {
        public List<DrawingCheckItemResult> Items { get; } = new List<DrawingCheckItemResult>();
        public int TotalSelected { get; set; }
        public int ProcessedCount { get; set; }
        public bool Canceled { get; set; }

        public int OkCount
        {
            get
            {
                int c = 0;
                foreach (var it in Items) if (it.Status == DrawingBomCheckStatus.OK) c++;
                return c;
            }
        }

        public int NgCount
        {
            get
            {
                int c = 0;
                foreach (var it in Items) if (it.Status == DrawingBomCheckStatus.NG) c++;
                return c;
            }
        }

        public int WarningCount
        {
            get
            {
                int c = 0;
                foreach (var it in Items) if (it.Status == DrawingBomCheckStatus.Warning) c++;
                return c;
            }
        }
    }

    internal class CheckDrawingBom
    {
        private readonly ISldWorks swApp;
        private readonly DataGridView bomGrid;

        private const double NumericTolerance = 0.01; // 0.01 mm tolerance cho W, L, 板厚
        private const double HeaderYBandTolerance = 0.010; // 10mm band độ chênh Y giữa các Header
        private const double ValueMaxYDistance = 0.025; // 25mm khoảng cách tối đa bên dưới Header

        public CheckDrawingBom(ISldWorks app, DataGridView grid = null)
        {
            swApp = app;
            bomGrid = grid;
        }

        #region Batch Execution Engine (Silent & Multi-Field)

        public DrawingBatchCheckResult RunBatch(
            Action<int> beginProgress,
            Action<int, int> updateProgress,
            Action finishProgress,
            Func<bool> isCancelRequested)
        {
            DrawingBatchCheckResult result = new DrawingBatchCheckResult();

            try
            {
                LogDebug("==================================================");
                LogDebug("START CHECK DRAWING BOM — PRECISE MULTI-BLOCK BATCH");
                LogDebug("==================================================");

                if (swApp == null)
                {
                    ShowWarning("Chưa kết nối SOLIDWORKS.");
                    return result;
                }

                if (bomGrid == null || bomGrid.Rows.Count == 0)
                {
                    ShowWarning("Bảng BOM (dgvModelBom) chưa có dữ liệu.\nVui lòng bấm 'CẬP NHẬT' để tải BOM trước khi kiểm tra.");
                    return result;
                }

                List<DataGridViewRow> checkedRows = new List<DataGridViewRow>();
                foreach (DataGridViewRow row in bomGrid.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    bool isChecked = Convert.ToBoolean(row.Cells[0].Value ?? false);
                    if (isChecked)
                    {
                        checkedRows.Add(row);
                    }
                }

                result.TotalSelected = checkedRows.Count;

                if (checkedRows.Count == 0)
                {
                    ShowWarning("Hãy tick ít nhất một chi tiết trước.");
                    return result;
                }

                LogDebug($"Total selected BOM rows = {checkedRows.Count}");

                ModelDoc2 originalDocument = swApp.ActiveDoc as ModelDoc2;
                string originalTitle = originalDocument != null ? originalDocument.GetTitle() : "";

                List<string> searchDirectories = BuildSearchDirectories(originalDocument);

                beginProgress?.Invoke(checkedRows.Count);

                for (int i = 0; i < checkedRows.Count; i++)
                {
                    if (isCancelRequested != null && isCancelRequested())
                    {
                        LogDebug($"[CANCEL] Batch cancel requested at item {i + 1}/{checkedRows.Count}");
                        result.Canceled = true;
                        break;
                    }

                    DataGridViewRow row = checkedRows[i];
                    updateProgress?.Invoke(i + 1, checkedRows.Count);

                    string buhinNo = GetCellText(row, 1);
                    string fileName = GetCellText(row, 5);
                    string componentName = Path.GetFileNameWithoutExtension(fileName);
                    if (string.IsNullOrWhiteSpace(componentName))
                        componentName = fileName;

                    DrawingCheckItemResult itemResult = new DrawingCheckItemResult
                    {
                        BomRowIndex = row.Index,
                        PartNumber = buhinNo,
                        Component = componentName
                    };

                    try
                    {
                        string resolvedPartPath = "";
                        string drawingPath = ResolveDrawingPath(row, searchDirectories, out resolvedPartPath);

                        itemResult.PartPath = resolvedPartPath;
                        itemResult.DrawingPath = drawingPath;

                        if (string.IsNullOrWhiteSpace(drawingPath) || !File.Exists(drawingPath))
                        {
                            itemResult.Status = DrawingBomCheckStatus.Warning;
                            itemResult.Note = "Drawing not found";
                            itemResult.Fields.Add(new DrawingBomFieldResult
                            {
                                FieldName = "Drawing",
                                Status = DrawingBomCheckStatus.Warning,
                                Message = "Không tìm thấy file Drawing tương ứng."
                            });
                            result.Items.Add(itemResult);
                            result.ProcessedCount++;
                            continue;
                        }

                        bool wasAlreadyOpen = swApp.GetOpenDocumentByName(drawingPath) != null;
                        bool openedByCommand = false;

                        ModelDoc2 drawingDoc = OpenDrawingDocumentSilent(drawingPath, out openedByCommand);
                        if (drawingDoc == null)
                        {
                            itemResult.Status = DrawingBomCheckStatus.Warning;
                            itemResult.Note = "Cannot open Drawing: " + Path.GetFileName(drawingPath);
                            itemResult.Fields.Add(new DrawingBomFieldResult
                            {
                                FieldName = "Drawing",
                                Status = DrawingBomCheckStatus.Warning,
                                Message = "Không thể mở file Drawing: " + drawingPath
                            });
                            result.Items.Add(itemResult);
                            result.ProcessedCount++;
                            continue;
                        }

                        itemResult = CheckOneDrawing(drawingDoc, row, drawingPath, resolvedPartPath);

                        if (openedByCommand && !wasAlreadyOpen)
                        {
                            try
                            {
                                swApp.CloseDoc(drawingDoc.GetTitle());
                            }
                            catch { }
                        }

                        result.Items.Add(itemResult);
                        result.ProcessedCount++;
                    }
                    catch (Exception exItem)
                    {
                        LogDebug($"[ITEM ERROR] Row {row.Index} ({buhinNo}): {exItem.Message}");
                        itemResult.Status = DrawingBomCheckStatus.Warning;
                        itemResult.Note = "Lỗi xử lý: " + exItem.Message;
                        result.Items.Add(itemResult);
                        result.ProcessedCount++;
                    }
                }

                if (originalDocument != null && !string.IsNullOrEmpty(originalTitle))
                {
                    try
                    {
                        int activateErrors = 0;
                        swApp.ActivateDoc3(originalTitle, false, 0, ref activateErrors);
                    }
                    catch { }
                }

                finishProgress?.Invoke();

                if (result.ProcessedCount > 0)
                {
                    ExcelDrawingCheckExporter.Export(result);
                }

                ShowBatchSummaryDialog(result);
            }
            catch (Exception ex)
            {
                finishProgress?.Invoke();
                LogDebug($"[BATCH ERROR] {ex}");
                MessageBox.Show(
                    "Đã xảy ra lỗi khi thực hiện CHECK DRAWING Batch:\n" + ex.Message,
                    "CHECK DRAWING — ERROR",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            return result;
        }

        #endregion

        #region Single Drawing Check (CheckOneDrawing - Complete 12-Field Verification)

        public DrawingCheckItemResult CheckOneDrawing(
            ModelDoc2 drawingModel,
            DataGridViewRow bomRow,
            string drawingPath = "",
            string partPath = "")
        {
            string buhinNoBom = GetCellText(bomRow, 1);
            string fileNameBom = GetCellText(bomRow, 5);
            string componentName = Path.GetFileNameWithoutExtension(fileNameBom);
            if (string.IsNullOrWhiteSpace(componentName))
                componentName = fileNameBom;

            DrawingCheckItemResult itemResult = new DrawingCheckItemResult
            {
                BomRowIndex = bomRow != null ? bomRow.Index : -1,
                PartNumber = buhinNoBom,
                Component = componentName,
                PartPath = partPath,
                DrawingPath = !string.IsNullOrEmpty(drawingPath) ? drawingPath : (drawingModel != null ? drawingModel.GetPathName() : "")
            };

            if (drawingModel == null)
            {
                itemResult.Status = DrawingBomCheckStatus.Warning;
                itemResult.Note = "Drawing document is null";
                return itemResult;
            }

            DrawingDoc drawing = drawingModel as DrawingDoc;
            if (drawing == null)
            {
                itemResult.Status = DrawingBomCheckStatus.Warning;
                itemResult.Note = "Document không phải DrawingDoc";
                return itemResult;
            }

            Sheet activeSheet = drawing.GetCurrentSheet() as Sheet;
            string sheetName = activeSheet != null ? activeSheet.GetName() : "";

            // 1. Quét Notes và Tables trên Sheet hiện tại
            List<NoteDiagnosticInfo> notes = ScanCurrentSheetNotes(drawing, sheetName);
            List<TableDiagnosticInfo> tables = ScanCurrentSheetTables(drawing, sheetName, drawingModel);

            // 2. Trích xuất toàn bộ giá trị Displayed Text từ 3 cụm khung tên
            DrawingDisplayedData drawingData = ExtractAllTitleBlockValues(notes, tables, drawingModel);

            // 3. Nếu không nhận diện được khung tên hoặc 部品番号
            if (!drawingData.HeaderFound || string.IsNullOrWhiteSpace(drawingData.PartNumber))
            {
                itemResult.Status = DrawingBomCheckStatus.Warning;
                itemResult.Note = "Không tìm thấy khung tên / 部品番号 trên Drawing";
                itemResult.Fields.Add(new DrawingBomFieldResult
                {
                    FieldName = "部品番号",
                    DrawingValue = "(Not Found)",
                    BomValue = buhinNoBom,
                    Status = DrawingBomCheckStatus.Warning,
                    Message = "Không đọc được giá trị 部品番号 trên bản vẽ."
                });
                return itemResult;
            }

            // 4. Đọc các giá trị tương ứng từ BOM và Part Custom Properties
            string bomQty = GetCellText(bomRow, 4);
            string bomMaterial = GetCellText(bomRow, 2);
            string bomThickness = GetCellText(bomRow, 3);
            string bomGoban = "";
            string bomW = "";
            string bomL = "";
            string bomJobNo = "";
            string bomTehaiNo = "";
            string bomSiteName = "";
            string bomProductName = "";

            ReadBomAndComponentProperties(
                bomRow,
                buhinNoBom,
                ref bomW,
                ref bomL,
                ref bomMaterial,
                ref bomThickness,
                ref bomGoban,
                ref bomJobNo,
                ref bomTehaiNo,
                ref bomSiteName,
                ref bomProductName);

            // Expected DXF Name: "{手配番号} / {部品番号}" (ví dụ: 8198 / 2)
            string expectedDxf = "";
            string tehaiToUse = !string.IsNullOrWhiteSpace(bomTehaiNo) ? bomTehaiNo : drawingData.TehaiNo;
            if (!string.IsNullOrWhiteSpace(tehaiToUse) && !string.IsNullOrWhiteSpace(buhinNoBom))
                expectedDxf = $"{tehaiToUse} / {buhinNoBom}";
            else if (!string.IsNullOrWhiteSpace(buhinNoBom))
                expectedDxf = buhinNoBom;

            // 5. Tiến hành so sánh tất cả 12 trường
            // 1. 部品番号 (Part No)
            itemResult.Fields.Add(ComparePartNumber(drawingData.PartNumber, buhinNoBom, drawingData.PartNumberSource));

            // 2. W (Width - Làm tròn LÊN 1 số thập phân)
            itemResult.Fields.Add(CompareNumericField("W", drawingData.Width, bomW, drawingData.WidthSource));

            // 3. L (Length - Làm tròn LÊN 1 số thập phân)
            itemResult.Fields.Add(CompareNumericField("L", drawingData.Length, bomL, drawingData.LengthSource));

            // 4. 数量 (Quantity)
            itemResult.Fields.Add(CompareQuantityField(drawingData.Quantity, bomQty, drawingData.QuantitySource));

            // 5. 材質 (Material)
            itemResult.Fields.Add(CompareMaterialField(drawingData.Material, bomMaterial));

            // 6. 板厚 (Thickness)
            itemResult.Fields.Add(CompareThicknessField(drawingData.Thickness, bomThickness));

            // 7. 合番 (Goban)
            itemResult.Fields.Add(CompareGobanField(drawingData.Goban, bomGoban));

            // 8. 部品ファイル名 / Part-ファイル名
            itemResult.Fields.Add(CompareFileNameField(drawingData.PartFileName, componentName));

            // 9. DXFファイル名 (Lấy theo 手配番号 ở property)
            itemResult.Fields.Add(CompareDxfField(drawingData.DxfFileName, expectedDxf));

            // 10. 品名 (Product Name)
            itemResult.Fields.Add(CompareOptionalStringField("品名", drawingData.ProductName, bomProductName));

            // 11. 現場名 (Site Name)
            itemResult.Fields.Add(CompareOptionalStringField("現場名", drawingData.SiteName, bomSiteName));

            // 12. 工事番号 (Job No)
            itemResult.Fields.Add(CompareOptionalStringField("工事番号", drawingData.JobNo, bomJobNo));

            // 6. Tổng hợp Overall Status & Summary Note
            bool hasNg = false;
            bool hasWarning = false;
            List<string> errorNotes = new List<string>();

            foreach (var f in itemResult.Fields)
            {
                if (f.Status == DrawingBomCheckStatus.NG)
                {
                    hasNg = true;
                    errorNotes.Add($"{f.FieldName}: BOM={f.BomValue} / Drawing={f.DrawingValue}");
                }
                else if (f.Status == DrawingBomCheckStatus.Warning)
                {
                    hasWarning = true;
                    if (!string.IsNullOrEmpty(f.Message))
                        errorNotes.Add($"{f.FieldName}: {f.Message}");
                }
            }

            if (hasNg)
                itemResult.Status = DrawingBomCheckStatus.NG;
            else if (hasWarning)
                itemResult.Status = DrawingBomCheckStatus.Warning;
            else
                itemResult.Status = DrawingBomCheckStatus.OK;

            itemResult.Note = string.Join(" | ", errorNotes);

            return itemResult;
        }

        #endregion

        #region Title Block Exact Geometric & Property Recognition (3 Blocks)

        private DrawingDisplayedData ExtractAllTitleBlockValues(
            List<NoteDiagnosticInfo> notes,
            List<TableDiagnosticInfo> tables,
            ModelDoc2 drawingDoc)
        {
            DrawingDisplayedData data = new DrawingDisplayedData();
            if (notes == null || notes.Count == 0)
                return data;

            // =========================================================================
            // CỤM 1: GÓC DƯỚI BÊN PHẢI (Bottom-Right Title Block: 部品番号, W, L, 数量)
            // =========================================================================
            NoteDiagnosticInfo partNoHeader = null;
            NoteDiagnosticInfo qtyHeader = null;

            foreach (var note in notes)
            {
                string norm = NormalizeText(note.DisplayedText);
                if (string.Equals(norm, "部品番号", StringComparison.OrdinalIgnoreCase))
                    partNoHeader = note;
                else if (string.Equals(norm, "数量", StringComparison.OrdinalIgnoreCase))
                    qtyHeader = note;
            }

            if (partNoHeader != null && qtyHeader != null &&
                Math.Abs(partNoHeader.Y - qtyHeader.Y) <= HeaderYBandTolerance &&
                partNoHeader.X < qtyHeader.X)
            {
                double headerY = (partNoHeader.Y + qtyHeader.Y) / 2.0;
                data.HeaderFound = true;
                data.HeaderY = headerY;

                NoteDiagnosticInfo wHeader = null;
                NoteDiagnosticInfo lHeader = null;

                foreach (var note in notes)
                {
                    if (note == partNoHeader || note == qtyHeader)
                        continue;

                    if (Math.Abs(note.Y - headerY) <= HeaderYBandTolerance &&
                        note.X > partNoHeader.X && note.X < qtyHeader.X)
                    {
                        string norm = NormalizeText(note.DisplayedText);
                        if (string.Equals(norm, "W", StringComparison.OrdinalIgnoreCase))
                            wHeader = note;
                        else if (string.Equals(norm, "L", StringComparison.OrdinalIgnoreCase))
                            lHeader = note;
                    }
                }

                List<HeaderColumnDef> headers = new List<HeaderColumnDef>();
                headers.Add(new HeaderColumnDef { Key = "PartNumber", HeaderNote = partNoHeader, X = partNoHeader.X });

                if (wHeader != null)
                    headers.Add(new HeaderColumnDef { Key = "Width", HeaderNote = wHeader, X = wHeader.X });
                if (lHeader != null)
                    headers.Add(new HeaderColumnDef { Key = "Length", HeaderNote = lHeader, X = lHeader.X });

                headers.Add(new HeaderColumnDef { Key = "Quantity", HeaderNote = qtyHeader, X = qtyHeader.X });
                headers.Sort((a, b) => a.X.CompareTo(b.X));

                for (int i = 0; i < headers.Count; i++)
                {
                    double minX;
                    double maxX;

                    if (i == 0)
                    {
                        double nextMid = (headers[i].X + headers[i + 1].X) / 2.0;
                        double widthSpan = nextMid - headers[i].X;
                        minX = headers[i].X - widthSpan;
                        maxX = nextMid;
                    }
                    else if (i == headers.Count - 1)
                    {
                        double prevMid = (headers[i - 1].X + headers[i].X) / 2.0;
                        double widthSpan = headers[i].X - prevMid;
                        minX = prevMid;
                        maxX = headers[i].X + widthSpan;
                    }
                    else
                    {
                        minX = (headers[i - 1].X + headers[i].X) / 2.0;
                        maxX = (headers[i].X + headers[i + 1].X) / 2.0;
                    }

                    headers[i].MinX = minX;
                    headers[i].MaxX = maxX;
                }

                foreach (var col in headers)
                {
                    NoteDiagnosticInfo bestValueNote = null;
                    double minVertDistance = double.MaxValue;

                    foreach (var note in notes)
                    {
                        if (note == partNoHeader || note == qtyHeader || note == wHeader || note == lHeader)
                            continue;

                        if (note.Y < headerY)
                        {
                            double vertDist = headerY - note.Y;
                            if (vertDist >= 0.0005 && vertDist <= ValueMaxYDistance)
                            {
                                if (note.X >= col.MinX && note.X <= col.MaxX)
                                {
                                    if (vertDist < minVertDistance)
                                    {
                                        minVertDistance = vertDist;
                                        bestValueNote = note;
                                    }
                                }
                            }
                        }
                    }

                    if (bestValueNote != null)
                    {
                        string disp = (bestValueNote.DisplayedText ?? "").Trim();
                        string raw = (bestValueNote.RawText ?? "").Trim();
                        string src = bestValueNote.Source;

                        switch (col.Key)
                        {
                            case "PartNumber":
                                data.PartNumber = disp;
                                data.PartNumberRaw = raw;
                                data.PartNumberSource = src;
                                break;
                            case "Width":
                                data.Width = disp;
                                data.WidthRaw = raw;
                                data.WidthSource = src;
                                break;
                            case "Length":
                                data.Length = disp;
                                data.LengthRaw = raw;
                                data.LengthSource = src;
                                break;
                            case "Quantity":
                                data.Quantity = disp;
                                data.QuantityRaw = raw;
                                data.QuantitySource = src;
                                break;
                        }
                    }
                }
            }

            // =========================================================================
            // CỤM 2: GÓC DƯỚI BÊN PHẢI (Bảng 合番: Table [01] R0 C1)
            // =========================================================================
            if (tables != null)
            {
                foreach (var table in tables)
                {
                    for (int r = 0; r < table.RowCount; r++)
                    {
                        for (int c = 0; c < table.ColumnCount; c++)
                        {
                            string cellText = NormalizeText(table.Cells.Find(x => x.Row == r && x.Column == c)?.DisplayedText ?? "");
                            if (cellText == "合番" && c + 1 < table.ColumnCount)
                            {
                                string val = (table.Cells.Find(x => x.Row == r && x.Column == c + 1)?.DisplayedText ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(val))
                                {
                                    data.Goban = val;
                                }
                            }
                        }
                    }
                }
            }

            // =========================================================================
            // CỤM 3: GÓC DƯỚI BÊN TRÁI (Bottom-Left Block: DXFファイル名 & Part-ファイル名)
            // =========================================================================
            string dxfJobNo = "";
            string dxfPartNo = "";

            foreach (var note in notes)
            {
                string raw = note.RawText ?? "";
                string disp = (note.DisplayedText ?? "").Trim();

                // Part-ファイル名 (ví dụ: "Part-ファイル名 : 8198-04-TI-sa1-2" hoặc "8198-04-TI-sa1-2")
                if (raw.Contains("\"SW-ﾌｧｲﾙ名") || raw.Contains("\"SW-ファイル名") || raw.Contains("SW-File Name") ||
                    (note.Y <= 0.020 && note.X >= 0.050 && note.X <= 0.150 && !disp.Contains("/") && disp.Length > 3))
                {
                    if (string.IsNullOrWhiteSpace(data.PartFileName))
                    {
                        string cleanName = disp;
                        int colonIdx = cleanName.IndexOf(':');
                        if (colonIdx >= 0) cleanName = cleanName.Substring(colonIdx + 1).Trim();
                        data.PartFileName = Path.GetFileNameWithoutExtension(cleanName);
                    }
                }

                // DXF Header & Value parts (Y <= 0.020, X <= 0.070)
                if (note.Y <= 0.020)
                {
                    if (raw.Contains("\"手配番号\"") || (note.X >= 0.030 && note.X <= 0.050 && !disp.Contains("/")))
                    {
                        if (string.IsNullOrWhiteSpace(dxfJobNo) && !string.IsNullOrWhiteSpace(disp))
                            dxfJobNo = disp;
                    }

                    if (disp.StartsWith("/") || raw.Contains("/ $PRP:\"部品番号\"") || raw.Contains("/$PRP:\"部品番号\""))
                    {
                        dxfPartNo = disp;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(dxfJobNo) && !string.IsNullOrWhiteSpace(dxfPartNo))
            {
                data.DxfFileName = $"{dxfJobNo} {dxfPartNo}".Trim();
            }
            else if (!string.IsNullOrWhiteSpace(dxfJobNo) && !string.IsNullOrWhiteSpace(data.PartNumber))
            {
                data.DxfFileName = $"{dxfJobNo} / {data.PartNumber}".Trim();
            }

            // =========================================================================
            // CỤM 4: GÓC TRÊN BÊN TRÁI (Top-Left Block: 工事No., 現場名, 品名, 材質, 板厚, No.)
            // =========================================================================
            foreach (var note in notes)
            {
                string raw = note.RawText ?? "";
                string disp = (note.DisplayedText ?? "").Trim();

                // 1. 工事番号 / 工事No. (X ≈ 0.062, Y ≈ 0.198)
                if (raw.Contains("\"工事番号\"") || raw.Contains("'工事番号'") ||
                    (note.Y >= 0.190 && note.X >= 0.050 && note.X <= 0.085 && Regex.IsMatch(disp, @"^\d+$")))
                {
                    if (string.IsNullOrWhiteSpace(data.JobNo)) data.JobNo = disp;
                }

                // 2. 現場名 (X ≈ 0.062, Y ≈ 0.192)
                if (raw.Contains("\"現場名\"") || raw.Contains("'現場名'") ||
                    (note.Y >= 0.188 && note.Y <= 0.195 && note.X >= 0.050 && note.X <= 0.100 && (disp.Contains("工事") || disp.Contains("AOYAMA"))))
                {
                    if (string.IsNullOrWhiteSpace(data.SiteName)) data.SiteName = disp;
                }

                // 3. 品名 (X ≈ 0.062, Y ≈ 0.186)
                if (raw.Contains("\"品名\"") || raw.Contains("'品名'") ||
                    (note.Y >= 0.180 && note.Y <= 0.188 && note.X >= 0.050 && note.X <= 0.100 && (disp.Contains("ベンチ") || disp.Contains("外構") || disp.Contains("項目"))))
                {
                    if (string.IsNullOrWhiteSpace(data.ProductName)) data.ProductName = disp;
                }

                // 4. 材質 (X ≈ 0.110, Y ≈ 0.196)
                if (raw.Contains("\"材質\"") || raw.Contains("'材質'") ||
                    (note.Y >= 0.190 && note.X >= 0.095 && note.X <= 0.125 && (disp.StartsWith("SUS") || disp.StartsWith("SECC") || disp.StartsWith("SPCC") || disp.StartsWith("NSD"))))
                {
                    if (string.IsNullOrWhiteSpace(data.Material)) data.Material = disp;
                }

                // 5. 板厚 (X ≈ 0.140, Y ≈ 0.196, ví dụ: "2t" hoặc "1.6t")
                if (raw.Contains("\"板厚\"") || raw.Contains("'板厚'") ||
                    (note.Y >= 0.190 && note.X >= 0.130 && note.X <= 0.155 && Regex.IsMatch(disp, @"^\d+(\.\d+)?t?$", RegexOptions.IgnoreCase)))
                {
                    if (string.IsNullOrWhiteSpace(data.Thickness)) data.Thickness = disp;
                }

                // 6. 手配番号 / No. (X ≈ 0.277, Y ≈ 0.197, ví dụ: "8198")
                if (raw.Contains("\"手配番号\"") || (note.Y >= 0.190 && note.X >= 0.260 && note.X <= 0.290 && Regex.IsMatch(disp, @"^\d+$")))
                {
                    if (string.IsNullOrWhiteSpace(data.TehaiNo)) data.TehaiNo = disp;
                }

                // 7. 仕上げ (X ≈ 0.110, Y ≈ 0.187, ví dụ: "粉体塗装")
                if (raw.Contains("\"仕上げ\"") || (note.Y >= 0.180 && note.Y <= 0.190 && note.X >= 0.095 && note.X <= 0.125))
                {
                    if (string.IsNullOrWhiteSpace(data.Finish)) data.Finish = disp;
                }
            }

            // Fallback resolve từ model properties nếu trường nào trên drawing còn thiếu
            DrawingDoc drw = drawingDoc as DrawingDoc;
            if (drw != null)
            {
                if (string.IsNullOrWhiteSpace(data.Goban)) data.Goban = ResolvePropertyFromDrawingViews(drw, "合番");
                if (string.IsNullOrWhiteSpace(data.Material)) data.Material = ResolvePropertyFromDrawingViews(drw, "材質");
                if (string.IsNullOrWhiteSpace(data.Thickness)) data.Thickness = ResolvePropertyFromDrawingViews(drw, "板厚");
                if (string.IsNullOrWhiteSpace(data.JobNo)) data.JobNo = ResolvePropertyFromDrawingViews(drw, "工事番号");
                if (string.IsNullOrWhiteSpace(data.TehaiNo)) data.TehaiNo = ResolvePropertyFromDrawingViews(drw, "手配番号");
                if (string.IsNullOrWhiteSpace(data.SiteName)) data.SiteName = ResolvePropertyFromDrawingViews(drw, "現場名");
                if (string.IsNullOrWhiteSpace(data.ProductName)) data.ProductName = ResolvePropertyFromDrawingViews(drw, "品名");
            }

            return data;
        }

        private static string ResolvePropertyFromDrawingViews(DrawingDoc drawingDoc, string propName)
        {
            if (drawingDoc == null || string.IsNullOrWhiteSpace(propName))
                return "";

            try
            {
                SolidWorks.Interop.sldworks.View view = drawingDoc.GetFirstView() as SolidWorks.Interop.sldworks.View;
                while (view != null)
                {
                    ModelDoc2 refModel = view.ReferencedDocument as ModelDoc2;
                    if (refModel != null)
                    {
                        string val = GetModelCustomProperty(refModel, view.ReferencedConfiguration ?? "", propName);
                        if (!string.IsNullOrWhiteSpace(val))
                            return val;
                    }
                    view = view.GetNextView() as SolidWorks.Interop.sldworks.View;
                }
            }
            catch { }

            return "";
        }

        private sealed class HeaderColumnDef
        {
            public string Key { get; set; }
            public NoteDiagnosticInfo HeaderNote { get; set; }
            public double X { get; set; }
            public double MinX { get; set; }
            public double MaxX { get; set; }
        }

        #endregion

        #region Extended Comparison Methods

        private static DrawingBomFieldResult ComparePartNumber(string drawVal, string bomVal, string source)
        {
            string normDraw = NormalizeText(drawVal);
            string normBom = NormalizeText(bomVal);

            DrawingBomCheckStatus status;
            string msg = "";

            if (string.Equals(normDraw, normBom, StringComparison.OrdinalIgnoreCase))
            {
                status = DrawingBomCheckStatus.OK;
            }
            else
            {
                status = DrawingBomCheckStatus.NG;
                msg = $"Khác biệt: Drawing='{drawVal}' != BOM='{bomVal}'";
            }

            return new DrawingBomFieldResult
            {
                FieldName = "部品番号",
                DrawingValue = drawVal,
                BomValue = bomVal,
                Source = source,
                Status = status,
                Message = msg
            };
        }

        private static DrawingBomFieldResult CompareNumericField(string fieldName, string drawVal, string bomVal, string source)
        {
            if (string.IsNullOrWhiteSpace(drawVal))
            {
                return new DrawingBomFieldResult
                {
                    FieldName = fieldName,
                    DrawingValue = "(Trống)",
                    BomValue = FormatOneDecimalRoundUp(bomVal),
                    Source = source,
                    Status = DrawingBomCheckStatus.Warning,
                    Message = $"Không đọc được {fieldName} trên Drawing."
                };
            }

            if (string.IsNullOrWhiteSpace(bomVal))
            {
                return new DrawingBomFieldResult
                {
                    FieldName = fieldName,
                    DrawingValue = FormatOneDecimalRoundUp(drawVal),
                    BomValue = "(Trống)",
                    Source = source,
                    Status = DrawingBomCheckStatus.Warning,
                    Message = $"Không có {fieldName} trong BOM."
                };
            }

            double dVal;
            double bVal;

            bool parsedDraw = double.TryParse(drawVal.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out dVal);
            bool parsedBom = double.TryParse(bomVal.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out bVal);

            if (parsedDraw && parsedBom)
            {
                double dValUp = RoundUpOneDecimal(dVal);
                double bValUp = RoundUpOneDecimal(bVal);

                string formattedDraw = dValUp.ToString("F1", CultureInfo.InvariantCulture);
                string formattedBom = bValUp.ToString("F1", CultureInfo.InvariantCulture);

                if (Math.Abs(dValUp - bValUp) <= 1e-4 || Math.Abs(dVal - bVal) <= NumericTolerance)
                {
                    return new DrawingBomFieldResult
                    {
                        FieldName = fieldName,
                        DrawingValue = formattedDraw,
                        BomValue = formattedBom,
                        Source = source,
                        Status = DrawingBomCheckStatus.OK
                    };
                }
                else
                {
                    return new DrawingBomFieldResult
                    {
                        FieldName = fieldName,
                        DrawingValue = formattedDraw,
                        BomValue = formattedBom,
                        Source = source,
                        Status = DrawingBomCheckStatus.NG,
                        Message = $"Sai lệch số học ({formattedDraw} != {formattedBom})"
                    };
                }
            }

            string rawFormatDraw = FormatOneDecimalRoundUp(drawVal);
            string rawFormatBom = FormatOneDecimalRoundUp(bomVal);

            if (string.Equals(NormalizeText(rawFormatDraw), NormalizeText(rawFormatBom), StringComparison.OrdinalIgnoreCase))
            {
                return new DrawingBomFieldResult
                {
                    FieldName = fieldName,
                    DrawingValue = rawFormatDraw,
                    BomValue = rawFormatBom,
                    Source = source,
                    Status = DrawingBomCheckStatus.OK
                };
            }

            return new DrawingBomFieldResult
            {
                FieldName = fieldName,
                DrawingValue = rawFormatDraw,
                BomValue = rawFormatBom,
                Source = source,
                Status = DrawingBomCheckStatus.Warning,
                Message = $"Không thể chuyển đổi số (Drawing: '{drawVal}', BOM: '{bomVal}')"
            };
        }

        private static double RoundUpOneDecimal(double val)
        {
            // Làm tròn LÊN 1 chữ số thập phân (Ceiling to 1 decimal place)
            // Ví dụ: 140.21 -> 140.3, 140.20 -> 140.2
            double rounded4 = Math.Round(val, 4);
            return Math.Ceiling(rounded4 * 10.0) / 10.0;
        }

        private static string FormatOneDecimalRoundUp(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            double d;
            if (double.TryParse(value.Replace(',', '.').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                double rUp = RoundUpOneDecimal(d);
                return rUp.ToString("F1", CultureInfo.InvariantCulture);
            }
            return value.Trim();
        }

        private static DrawingBomFieldResult CompareQuantityField(string drawVal, string bomVal, string source)
        {
            if (string.IsNullOrWhiteSpace(drawVal))
            {
                return new DrawingBomFieldResult
                {
                    FieldName = "数量",
                    DrawingValue = "(Trống)",
                    BomValue = bomVal,
                    Source = source,
                    Status = DrawingBomCheckStatus.Warning,
                    Message = "Không đọc được 数量 trên Drawing."
                };
            }

            if (string.IsNullOrWhiteSpace(bomVal))
            {
                return new DrawingBomFieldResult
                {
                    FieldName = "数量",
                    DrawingValue = drawVal,
                    BomValue = "(Trống)",
                    Source = source,
                    Status = DrawingBomCheckStatus.Warning,
                    Message = "Không có 数量 trong BOM."
                };
            }

            double dValDouble;
            double bValDouble;

            bool parsedDraw = double.TryParse(drawVal.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out dValDouble);
            bool parsedBom = double.TryParse(bomVal.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out bValDouble);

            if (parsedDraw && parsedBom)
            {
                bool isDrawInt = Math.Abs(dValDouble - Math.Round(dValDouble)) < 1e-6;
                bool isBomInt = Math.Abs(bValDouble - Math.Round(bValDouble)) < 1e-6;

                if (isDrawInt && isBomInt)
                {
                    int dInt = (int)Math.Round(dValDouble);
                    int bInt = (int)Math.Round(bValDouble);

                    if (dInt == bInt)
                    {
                        return new DrawingBomFieldResult
                        {
                            FieldName = "数量",
                            DrawingValue = drawVal,
                            BomValue = bomVal,
                            Source = source,
                            Status = DrawingBomCheckStatus.OK
                        };
                    }
                    else
                    {
                        return new DrawingBomFieldResult
                        {
                            FieldName = "数量",
                            DrawingValue = drawVal,
                            BomValue = bomVal,
                            Source = source,
                            Status = DrawingBomCheckStatus.NG,
                            Message = $"Số lượng không khớp: Drawing={dInt} != BOM={bInt}"
                        };
                    }
                }
                else
                {
                    if (Math.Abs(dValDouble - bValDouble) <= NumericTolerance)
                    {
                        return new DrawingBomFieldResult
                        {
                            FieldName = "数量",
                            DrawingValue = drawVal,
                            BomValue = bomVal,
                            Source = source,
                            Status = DrawingBomCheckStatus.OK
                        };
                    }
                    else
                    {
                        return new DrawingBomFieldResult
                        {
                            FieldName = "数量",
                            DrawingValue = drawVal,
                            BomValue = bomVal,
                            Source = source,
                            Status = DrawingBomCheckStatus.NG,
                            Message = $"Số lượng lẻ không khớp ({dValDouble} != {bValDouble})"
                        };
                    }
                }
            }

            if (string.Equals(NormalizeText(drawVal), NormalizeText(bomVal), StringComparison.OrdinalIgnoreCase))
            {
                return new DrawingBomFieldResult
                {
                    FieldName = "数量",
                    DrawingValue = drawVal,
                    BomValue = bomVal,
                    Source = source,
                    Status = DrawingBomCheckStatus.OK
                };
            }

            return new DrawingBomFieldResult
            {
                FieldName = "数量",
                DrawingValue = drawVal,
                BomValue = bomVal,
                Source = source,
                Status = DrawingBomCheckStatus.NG,
                Message = $"Số lượng không khớp: Drawing='{drawVal}' != BOM='{bomVal}'"
            };
        }

        private static DrawingBomFieldResult CompareMaterialField(string drawVal, string bomVal)
        {
            string nDraw = NormalizeText(drawVal).TrimEnd('-').Trim();
            string nBom = NormalizeText(bomVal).TrimEnd('-').Trim();

            if (string.IsNullOrWhiteSpace(nDraw) && string.IsNullOrWhiteSpace(nBom))
                return new DrawingBomFieldResult { FieldName = "材質", DrawingValue = "-", BomValue = "-", Status = DrawingBomCheckStatus.OK };

            if (string.Equals(nDraw, nBom, StringComparison.OrdinalIgnoreCase))
                return new DrawingBomFieldResult { FieldName = "材質", DrawingValue = drawVal, BomValue = bomVal, Status = DrawingBomCheckStatus.OK };

            return new DrawingBomFieldResult
            {
                FieldName = "材質",
                DrawingValue = drawVal,
                BomValue = bomVal,
                Status = DrawingBomCheckStatus.NG,
                Message = "Vật liệu không khớp"
            };
        }

        private static DrawingBomFieldResult CompareThicknessField(string drawVal, string bomVal)
        {
            if (string.IsNullOrWhiteSpace(drawVal) && string.IsNullOrWhiteSpace(bomVal))
            {
                return new DrawingBomFieldResult { FieldName = "板厚", DrawingValue = "-", BomValue = "-", Status = DrawingBomCheckStatus.OK };
            }

            string cleanDraw = Regex.Replace(drawVal ?? "", @"[tTmM\s]", "").Trim();
            string cleanBom = Regex.Replace(bomVal ?? "", @"[tTmM\s]", "").Trim();

            double dVal, bVal;
            if (double.TryParse(cleanDraw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out dVal) &&
                double.TryParse(cleanBom.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out bVal))
            {
                if (Math.Abs(dVal - bVal) <= NumericTolerance)
                {
                    return new DrawingBomFieldResult { FieldName = "板厚", DrawingValue = drawVal, BomValue = bomVal, Status = DrawingBomCheckStatus.OK };
                }
                else
                {
                    return new DrawingBomFieldResult { FieldName = "板厚", DrawingValue = drawVal, BomValue = bomVal, Status = DrawingBomCheckStatus.NG, Message = $"Độ dày lệch: {dVal} != {bVal}" };
                }
            }

            if (string.Equals(NormalizeText(drawVal), NormalizeText(bomVal), StringComparison.OrdinalIgnoreCase))
            {
                return new DrawingBomFieldResult { FieldName = "板厚", DrawingValue = drawVal, BomValue = bomVal, Status = DrawingBomCheckStatus.OK };
            }

            return new DrawingBomFieldResult { FieldName = "板厚", DrawingValue = drawVal, BomValue = bomVal, Status = DrawingBomCheckStatus.NG, Message = "Độ dày không khớp" };
        }

        private static DrawingBomFieldResult CompareGobanField(string drawVal, string bomVal)
        {
            string nDraw = NormalizeText(drawVal);
            string nBom = NormalizeText(bomVal);

            if (string.IsNullOrWhiteSpace(nDraw) && string.IsNullOrWhiteSpace(nBom))
                return new DrawingBomFieldResult { FieldName = "合番", DrawingValue = "-", BomValue = "-", Status = DrawingBomCheckStatus.OK };

            if (string.IsNullOrWhiteSpace(nDraw))
                return new DrawingBomFieldResult { FieldName = "合番", DrawingValue = "(Trống)", BomValue = bomVal, Status = DrawingBomCheckStatus.Warning, Message = "Không đọc được 合番 trên Drawing" };

            if (string.IsNullOrWhiteSpace(nBom))
                return new DrawingBomFieldResult { FieldName = "合番", DrawingValue = drawVal, BomValue = "(Trống)", Status = DrawingBomCheckStatus.Warning, Message = "Không có 合番 trong BOM" };

            // 1. So khớp trực tiếp chuỗi
            if (string.Equals(nDraw, nBom, StringComparison.OrdinalIgnoreCase))
                return new DrawingBomFieldResult { FieldName = "合番", DrawingValue = drawVal, BomValue = bomVal, Status = DrawingBomCheckStatus.OK };

            // 2. Bỏ khoảng trắng & chuẩn hóa dấu :
            string sDraw = nDraw.Replace(" ", "").Replace("：", ":");
            string sBom = nBom.Replace(" ", "").Replace("：", ":");
            if (string.Equals(sDraw, sBom, StringComparison.OrdinalIgnoreCase))
                return new DrawingBomFieldResult { FieldName = "合番", DrawingValue = drawVal, BomValue = bomVal, Status = DrawingBomCheckStatus.OK };

            // 3. So khớp phần tiền tố/mã Unit (ví dụ: "sb1" hoặc "sa1" hoặc "CB3-13C")
            string unitDraw = ExtractGobanUnit(sDraw);
            string unitBom = ExtractGobanUnit(sBom);
            if (!string.IsNullOrEmpty(unitDraw) && !string.IsNullOrEmpty(unitBom) &&
                string.Equals(unitDraw, unitBom, StringComparison.OrdinalIgnoreCase))
            {
                return new DrawingBomFieldResult { FieldName = "合番", DrawingValue = drawVal, BomValue = bomVal, Status = DrawingBomCheckStatus.OK };
            }

            return new DrawingBomFieldResult
            {
                FieldName = "合番",
                DrawingValue = drawVal,
                BomValue = bomVal,
                Status = DrawingBomCheckStatus.NG,
                Message = $"合番 không khớp: Drawing='{drawVal}' != BOM='{bomVal}'"
            };
        }

        private static string ExtractGobanUnit(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            int idx = text.IndexOfAny(new[] { '(', '（', ':', '：' });
            if (idx > 0) return text.Substring(0, idx).Trim();
            return text.Trim();
        }

        private static DrawingBomFieldResult CompareFileNameField(string drawVal, string bomVal)
        {
            string nDraw = Path.GetFileNameWithoutExtension(NormalizeText(drawVal));
            string nBom = Path.GetFileNameWithoutExtension(NormalizeText(bomVal));

            if (string.IsNullOrWhiteSpace(nDraw) && string.IsNullOrWhiteSpace(nBom))
                return new DrawingBomFieldResult { FieldName = "部品ファイル名", DrawingValue = "-", BomValue = "-", Status = DrawingBomCheckStatus.OK };

            if (string.Equals(nDraw, nBom, StringComparison.OrdinalIgnoreCase))
                return new DrawingBomFieldResult { FieldName = "部品ファイル名", DrawingValue = drawVal, BomValue = bomVal, Status = DrawingBomCheckStatus.OK };

            return new DrawingBomFieldResult
            {
                FieldName = "部品ファイル名",
                DrawingValue = drawVal,
                BomValue = bomVal,
                Status = DrawingBomCheckStatus.NG,
                Message = "Tên file chi tiết không khớp"
            };
        }

        private static DrawingBomFieldResult CompareDxfField(string drawVal, string bomVal)
        {
            string nDraw = NormalizeText(drawVal).Replace(" ", "");
            string nBom = NormalizeText(bomVal).Replace(" ", "");

            if (string.IsNullOrWhiteSpace(nDraw) && string.IsNullOrWhiteSpace(nBom))
                return new DrawingBomFieldResult { FieldName = "DXFファイル名", DrawingValue = "-", BomValue = "-", Status = DrawingBomCheckStatus.OK };

            if (string.IsNullOrWhiteSpace(nDraw))
                return new DrawingBomFieldResult { FieldName = "DXFファイル名", DrawingValue = "(Trống)", BomValue = bomVal, Status = DrawingBomCheckStatus.Warning, Message = "Không đọc được DXF trên Drawing" };

            if (string.IsNullOrWhiteSpace(nBom))
                return new DrawingBomFieldResult { FieldName = "DXFファイル名", DrawingValue = drawVal, BomValue = "(Trống)", Status = DrawingBomCheckStatus.Warning, Message = "Không có DXF trong BOM" };

            if (string.Equals(nDraw, nBom, StringComparison.OrdinalIgnoreCase))
                return new DrawingBomFieldResult { FieldName = "DXFファイル名", DrawingValue = drawVal, BomValue = bomVal, Status = DrawingBomCheckStatus.OK };

            return new DrawingBomFieldResult
            {
                FieldName = "DXFファイル名",
                DrawingValue = drawVal,
                BomValue = bomVal,
                Status = DrawingBomCheckStatus.NG,
                Message = "Tên file DXF không khớp"
            };
        }

        private static DrawingBomFieldResult CompareOptionalStringField(string fieldName, string drawVal, string bomVal)
        {
            string nDraw = NormalizeText(drawVal);
            string nBom = NormalizeText(bomVal);

            if (string.IsNullOrWhiteSpace(nDraw) || string.IsNullOrWhiteSpace(nBom))
            {
                return new DrawingBomFieldResult { FieldName = fieldName, DrawingValue = drawVal, BomValue = bomVal, Status = DrawingBomCheckStatus.OK };
            }

            if (string.Equals(nDraw, nBom, StringComparison.OrdinalIgnoreCase))
                return new DrawingBomFieldResult { FieldName = fieldName, DrawingValue = drawVal, BomValue = bomVal, Status = DrawingBomCheckStatus.OK };

            return new DrawingBomFieldResult
            {
                FieldName = fieldName,
                DrawingValue = drawVal,
                BomValue = bomVal,
                Status = DrawingBomCheckStatus.NG,
                Message = $"{fieldName} không khớp"
            };
        }

        private void ReadBomAndComponentProperties(
            DataGridViewRow row,
            string targetPartNo,
            ref string w,
            ref string l,
            ref string material,
            ref string thickness,
            ref string goban,
            ref string jobNo,
            ref string tehaiNo,
            ref string siteName,
            ref string productName)
        {
            if (row != null && row.DataGridView != null)
            {
                for (int c = 0; c < row.DataGridView.Columns.Count; c++)
                {
                    string h = NormalizeText(row.DataGridView.Columns[c].HeaderText);
                    if (h == "W" && string.IsNullOrWhiteSpace(w)) w = Convert.ToString(row.Cells[c].Value ?? "").Trim();
                    if (h == "L" && string.IsNullOrWhiteSpace(l)) l = Convert.ToString(row.Cells[c].Value ?? "").Trim();
                    if (h == "材質" && string.IsNullOrWhiteSpace(material)) material = Convert.ToString(row.Cells[c].Value ?? "").Trim();
                    if (h == "板厚" && string.IsNullOrWhiteSpace(thickness)) thickness = Convert.ToString(row.Cells[c].Value ?? "").Trim();
                    if (h == "合番" && string.IsNullOrWhiteSpace(goban)) goban = Convert.ToString(row.Cells[c].Value ?? "").Trim();
                }
            }

            try
            {
                object tag = row.Tag;
                if (tag is object[] comps && comps.Length > 0)
                {
                    foreach (object obj in comps)
                    {
                        Component2 comp = obj as Component2;
                        if (comp != null)
                        {
                            ModelDoc2 compModel = comp.GetModelDoc2() as ModelDoc2;
                            if (compModel != null)
                            {
                                string cfg = comp.ReferencedConfiguration ?? "";
                                if (string.IsNullOrWhiteSpace(w)) w = GetModelCustomProperty(compModel, cfg, "W");
                                if (string.IsNullOrWhiteSpace(l)) l = GetModelCustomProperty(compModel, cfg, "L");
                                if (string.IsNullOrWhiteSpace(material)) material = GetModelCustomProperty(compModel, cfg, "材質");
                                if (string.IsNullOrWhiteSpace(thickness)) thickness = GetModelCustomProperty(compModel, cfg, "板厚");
                                if (string.IsNullOrWhiteSpace(goban)) goban = GetModelCustomProperty(compModel, cfg, "合番");

                                if (string.IsNullOrWhiteSpace(tehaiNo)) tehaiNo = GetModelCustomProperty(compModel, cfg, "手配番号");
                                if (string.IsNullOrWhiteSpace(tehaiNo)) tehaiNo = GetModelCustomProperty(compModel, "", "手配番号");

                                if (string.IsNullOrWhiteSpace(jobNo)) jobNo = GetModelCustomProperty(compModel, cfg, "工事番号");
                                if (string.IsNullOrWhiteSpace(jobNo)) jobNo = GetModelCustomProperty(compModel, "", "工事番号");

                                if (string.IsNullOrWhiteSpace(siteName)) siteName = GetModelCustomProperty(compModel, cfg, "現場名");
                                if (string.IsNullOrWhiteSpace(productName)) productName = GetModelCustomProperty(compModel, cfg, "品名");
                            }
                        }
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(tehaiNo) && swApp != null)
            {
                ModelDoc2 activeDoc = swApp.ActiveDoc as ModelDoc2;
                if (activeDoc != null)
                {
                    tehaiNo = GetModelCustomProperty(activeDoc, "", "手配番号");
                    if (string.IsNullOrWhiteSpace(jobNo)) jobNo = GetModelCustomProperty(activeDoc, "", "工事番号");
                }
            }
        }

        private static string GetModelCustomProperty(ModelDoc2 model, string configurationName, string propName)
        {
            if (model == null || string.IsNullOrWhiteSpace(propName))
                return "";

            try
            {
                CustomPropertyManager propMgr = model.Extension.get_CustomPropertyManager(configurationName ?? "");
                string valOut;
                string resolvedValOut;
                bool wasResolved;
                bool linkToProperty;
                propMgr.Get6(propName, false, out valOut, out resolvedValOut, out wasResolved, out linkToProperty);
                if (!string.IsNullOrWhiteSpace(resolvedValOut))
                    return resolvedValOut;

                propMgr = model.Extension.get_CustomPropertyManager("");
                propMgr.Get6(propName, false, out valOut, out resolvedValOut, out wasResolved, out linkToProperty);
                return !string.IsNullOrWhiteSpace(resolvedValOut) ? resolvedValOut : (valOut ?? "");
            }
            catch
            {
                return "";
            }
        }

        #endregion

        #region Path Resolution & Silent Document Helpers

        private string ResolveDrawingPath(
            DataGridViewRow row,
            List<string> searchDirectories,
            out string resolvedPartPath)
        {
            resolvedPartPath = "";
            if (row == null)
                return "";

            string fileName = GetCellText(row, 5);
            string baseName = Path.GetFileNameWithoutExtension(fileName);

            object tag = row.Tag;
            if (tag is object[] comps && comps.Length > 0)
            {
                foreach (object obj in comps)
                {
                    Component2 comp = obj as Component2;
                    if (comp != null)
                    {
                        string path = comp.GetPathName();
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        {
                            resolvedPartPath = path;
                            string drwUpper = Path.ChangeExtension(path, ".SLDDRW");
                            if (File.Exists(drwUpper)) return drwUpper;
                            string drwLower = Path.ChangeExtension(path, ".slddrw");
                            if (File.Exists(drwLower)) return drwLower;
                        }
                    }
                }
            }
            else if (tag is string pathStr && !string.IsNullOrWhiteSpace(pathStr) && File.Exists(pathStr))
            {
                resolvedPartPath = pathStr;
                string drwUpper = Path.ChangeExtension(pathStr, ".SLDDRW");
                if (File.Exists(drwUpper)) return drwUpper;
                string drwLower = Path.ChangeExtension(pathStr, ".slddrw");
                if (File.Exists(drwLower)) return drwLower;
            }

            if (!string.IsNullOrWhiteSpace(fileName) && Path.IsPathRooted(fileName))
            {
                resolvedPartPath = fileName;
                string drwUpper = Path.ChangeExtension(fileName, ".SLDDRW");
                if (File.Exists(drwUpper)) return drwUpper;
                string drwLower = Path.ChangeExtension(fileName, ".slddrw");
                if (File.Exists(drwLower)) return drwLower;
            }

            if (!string.IsNullOrWhiteSpace(baseName) && searchDirectories != null)
            {
                foreach (string dir in searchDirectories)
                {
                    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                        continue;

                    string drwUpper = Path.Combine(dir, baseName + ".SLDDRW");
                    if (File.Exists(drwUpper)) return drwUpper;

                    string drwLower = Path.Combine(dir, baseName + ".slddrw");
                    if (File.Exists(drwLower)) return drwLower;

                    if (string.IsNullOrWhiteSpace(resolvedPartPath))
                    {
                        string prtUpper = Path.Combine(dir, baseName + ".SLDPRT");
                        if (File.Exists(prtUpper)) resolvedPartPath = prtUpper;
                        else
                        {
                            string prtLower = Path.Combine(dir, baseName + ".sldprt");
                            if (File.Exists(prtLower)) resolvedPartPath = prtLower;
                        }
                    }
                }
            }

            return "";
        }

        private List<string> BuildSearchDirectories(ModelDoc2 activeModel)
        {
            List<string> directories = new List<string>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (activeModel != null)
            {
                string activePath = activeModel.GetPathName();
                if (!string.IsNullOrWhiteSpace(activePath))
                {
                    string dir = Path.GetDirectoryName(activePath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) && visited.Add(dir))
                        directories.Add(dir);
                }
            }

            if (bomGrid != null)
            {
                foreach (DataGridViewRow row in bomGrid.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    object tag = row.Tag;
                    if (tag is object[] comps)
                    {
                        foreach (object obj in comps)
                        {
                            Component2 comp = obj as Component2;
                            if (comp != null)
                            {
                                string compPath = comp.GetPathName();
                                if (!string.IsNullOrWhiteSpace(compPath))
                                {
                                    string dir = Path.GetDirectoryName(compPath);
                                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) && visited.Add(dir))
                                        directories.Add(dir);
                                }
                            }
                        }
                    }
                    else if (tag is string partPath && !string.IsNullOrWhiteSpace(partPath))
                    {
                        string dir = Path.GetDirectoryName(partPath);
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) && visited.Add(dir))
                            directories.Add(dir);
                    }
                }
            }

            return directories;
        }

        private ModelDoc2 OpenDrawingDocumentSilent(string drawingPath, out bool openedByCommand)
        {
            openedByCommand = false;

            if (string.IsNullOrWhiteSpace(drawingPath) || !File.Exists(drawingPath))
                return null;

            ModelDoc2 openDoc = swApp.GetOpenDocumentByName(drawingPath) as ModelDoc2;
            if (openDoc != null)
                return openDoc;

            int errors = 0;
            int warnings = 0;
            bool restoreVisibility = false;

            try
            {
                swApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocDRAWING);
                restoreVisibility = true;

                int openOptions = (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_ReadOnly);
                ModelDoc2 openedDoc = swApp.OpenDoc6(
                    drawingPath,
                    (int)swDocumentTypes_e.swDocDRAWING,
                    openOptions,
                    "",
                    ref errors,
                    ref warnings) as ModelDoc2;

                openedByCommand = (openedDoc != null);
                return openedDoc;
            }
            finally
            {
                if (restoreVisibility)
                {
                    try
                    {
                        swApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocDRAWING);
                    }
                    catch { }
                }
            }
        }

        #endregion

        #region Scanning & Data Helper Methods

        private List<NoteDiagnosticInfo> ScanCurrentSheetNotes(DrawingDoc drawing, string targetSheetName)
        {
            List<NoteDiagnosticInfo> result = new List<NoteDiagnosticInfo>();
            if (drawing == null)
                return result;

            int noteCounter = 1;

            try
            {
                SolidWorks.Interop.sldworks.View view = drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                while (view != null)
                {
                    if (IsViewOnSheet(view, targetSheetName))
                    {
                        string viewName = view.Name ?? "";

                        try
                        {
                            Note note = view.GetFirstNote() as Note;
                            while (note != null)
                            {
                                try
                                {
                                    Annotation ann = note.GetAnnotation() as Annotation;
                                    NoteDiagnosticInfo info = ExtractNoteInfo(note, ann, viewName, targetSheetName, noteCounter);
                                    if (info != null)
                                    {
                                        result.Add(info);
                                        noteCounter++;
                                    }
                                }
                                catch { }

                                note = note.GetNext() as Note;
                            }
                        }
                        catch { }
                    }

                    view = view.GetNextView() as SolidWorks.Interop.sldworks.View;
                }
            }
            catch { }

            return result;
        }

        private NoteDiagnosticInfo ExtractNoteInfo(
            Note note,
            Annotation ann,
            string viewName,
            string sheetName,
            int index)
        {
            if (note == null)
                return null;

            string displayedText = "";
            try { displayedText = note.GetText() ?? ""; }
            catch { displayedText = ""; }

            string rawText = "";
            try { rawText = note.PropertyLinkedText ?? ""; }
            catch { rawText = displayedText; }

            if (string.IsNullOrEmpty(rawText))
                rawText = displayedText;

            double x = 0;
            double y = 0;
            string annName = "";

            if (ann != null)
            {
                try
                {
                    annName = ann.GetName() ?? "";
                    double[] pos = ann.GetPosition() as double[];
                    if (pos != null && pos.Length >= 2)
                    {
                        x = pos[0];
                        y = pos[1];
                    }
                }
                catch { }
            }

            string objType = "Note";
            try
            {
                bool isBalloon = false;
                try { isBalloon = ((dynamic)note).IsBomBalloon(); }
                catch { }
                if (!isBalloon)
                {
                    try { isBalloon = ((dynamic)note).IsBalloon(); }
                    catch { }
                }
                if (isBalloon)
                    objType = "Balloon Note";
            }
            catch { }

            string source = "MANUAL_OR_STATIC";
            if (!string.IsNullOrEmpty(rawText) &&
                (rawText.Contains("$PRP") ||
                 rawText.Contains("$PRPSHEET") ||
                 rawText.Contains("$PRPMODEL") ||
                 rawText.Contains("$PRPVIEW") ||
                 rawText.Contains("\"SW-") ||
                 rawText.Contains("\"sw-")))
            {
                source = "LINKED";
            }

            return new NoteDiagnosticInfo
            {
                Index = index,
                Name = annName,
                ViewName = viewName,
                SheetName = sheetName,
                Type = objType,
                DisplayedText = displayedText,
                RawText = rawText,
                Source = source,
                X = x,
                Y = y
            };
        }

        private List<TableDiagnosticInfo> ScanCurrentSheetTables(
            DrawingDoc drawing,
            string targetSheetName,
            ModelDoc2 drawingModel)
        {
            List<TableDiagnosticInfo> result = new List<TableDiagnosticInfo>();
            HashSet<ITableAnnotation> visited = new HashSet<ITableAnnotation>();
            int tableCounter = 1;

            if (drawing != null)
            {
                try
                {
                    SolidWorks.Interop.sldworks.View view = drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                    while (view != null)
                    {
                        if (IsViewOnSheet(view, targetSheetName))
                        {
                            string viewName = view.Name ?? "";

                            object[] tables = null;
                            try { tables = view.GetTableAnnotations() as object[]; }
                            catch { }

                            if (tables != null && tables.Length > 0)
                            {
                                foreach (object obj in tables)
                                {
                                    ITableAnnotation table = obj as ITableAnnotation;
                                    if (table != null && visited.Add(table))
                                    {
                                        TableDiagnosticInfo info = ExtractTableInfo(table, viewName, targetSheetName, tableCounter, drawingModel);
                                        if (info != null)
                                        {
                                            result.Add(info);
                                            tableCounter++;
                                        }
                                    }
                                }
                            }
                        }

                        view = view.GetNextView() as SolidWorks.Interop.sldworks.View;
                    }
                }
                catch { }
            }

            return result;
        }

        private TableDiagnosticInfo ExtractTableInfo(
            ITableAnnotation table,
            string viewName,
            string sheetName,
            int index,
            ModelDoc2 drawingModel)
        {
            if (table == null)
                return null;

            try
            {
                Annotation ann = table.GetAnnotation() as Annotation;
                string annName = ann != null ? (ann.GetName() ?? "") : "";
                int rowCount = table.RowCount;
                int colCount = table.ColumnCount;

                TableDiagnosticInfo tableInfo = new TableDiagnosticInfo
                {
                    Index = index,
                    Name = annName,
                    ViewName = viewName,
                    SheetName = sheetName,
                    RowCount = rowCount,
                    ColumnCount = colCount
                };

                for (int r = 0; r < rowCount; r++)
                {
                    for (int c = 0; c < colCount; c++)
                    {
                        string displayText = "";
                        try { displayText = table.get_DisplayedText2(r, c, false); } catch { }
                        if (string.IsNullOrWhiteSpace(displayText) || displayText.StartsWith("$PRP"))
                        {
                            try { displayText = table.get_Text(r, c) ?? ""; } catch { }
                        }

                        if (!string.IsNullOrWhiteSpace(displayText) && displayText.StartsWith("$PRP"))
                        {
                            Match m = Regex.Match(displayText, @"\$PRP(?:SHEET|MODEL)?:\s*""([^""]+)""");
                            if (m.Success)
                            {
                                string propName = m.Groups[1].Value;
                                string resolved = ResolvePropertyFromDrawingViews(drawingModel as DrawingDoc, propName);
                                if (!string.IsNullOrWhiteSpace(resolved))
                                    displayText = resolved;
                            }
                        }

                        tableInfo.Cells.Add(new TableCellDiagnosticInfo
                        {
                            Row = r,
                            Column = c,
                            DisplayedText = displayText
                        });
                    }
                }

                return tableInfo;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsViewOnSheet(SolidWorks.Interop.sldworks.View view, string targetSheetName)
        {
            if (view == null || string.IsNullOrWhiteSpace(targetSheetName))
                return false;

            try
            {
                if (view.Type == (int)swDrawingViewTypes_e.swDrawingSheet)
                {
                    return string.Equals(view.Name, targetSheetName, StringComparison.OrdinalIgnoreCase);
                }

                Sheet sheet = view.Sheet as Sheet;
                if (sheet != null)
                {
                    return string.Equals(sheet.GetName(), targetSheetName, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            return true;
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            string s = text.Replace('\u3000', ' ').Trim();
            s = Regex.Replace(s, @"\s+", " ");
            return s;
        }

        private static string GetCellText(DataGridViewRow row, int colIndex)
        {
            if (row == null || colIndex < 0 || colIndex >= row.Cells.Count)
                return "";

            return Convert.ToString(row.Cells[colIndex].Value ?? "").Trim();
        }

        #endregion

        #region Dialogs & Notifications

        private static void ShowBatchSummaryDialog(DrawingBatchCheckResult result)
        {
            StringBuilder sb = new StringBuilder();

            if (result.Canceled)
            {
                sb.AppendLine("⚠️ CHECK DRAWING đã hủy.");
                sb.AppendLine();
                sb.AppendLine($"• Đã xử lý : {result.ProcessedCount} / {result.TotalSelected}");
                sb.AppendLine($"• OK        : {result.OkCount}");
                sb.AppendLine($"• NG        : {result.NgCount}");
                sb.AppendLine($"• Warning   : {result.WarningCount}");
                sb.AppendLine();
                sb.AppendLine("Excel chứa kết quả của các Drawing đã được xử lý.");
            }
            else
            {
                sb.AppendLine("=== CHECK DRAWING HOÀN TẤT ===");
                sb.AppendLine();
                sb.AppendLine($"• Tổng số chi tiết chọn : {result.TotalSelected}");
                sb.AppendLine($"• ✅ OK                 : {result.OkCount}");
                sb.AppendLine($"• ❌ NG                 : {result.NgCount}");
                sb.AppendLine($"• ⚠️ Warning            : {result.WarningCount}");
                sb.AppendLine();
                sb.AppendLine("📊 Đã xuất toàn bộ kết quả chi tiết sang Excel.");
            }

            MessageBoxIcon icon = result.NgCount > 0
                ? MessageBoxIcon.Warning
                : (result.WarningCount > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Information);

            MessageBox.Show(
                sb.ToString(),
                "CHECK DRAWING BOM",
                MessageBoxButtons.OK,
                icon);
        }

        private static void LogDebug(string message)
        {
            Debug.WriteLine($"[CHECK DRAWING BOM] {message}");
        }

        private static void ShowWarning(string message)
        {
            LogDebug($"[WARNING] {message}");
            MessageBox.Show(
                message,
                "CHECK DRAWING",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        #endregion

        #region Internal Scan Models

        private sealed class NoteDiagnosticInfo
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public string ViewName { get; set; }
            public string SheetName { get; set; }
            public string Type { get; set; }
            public string DisplayedText { get; set; }
            public string RawText { get; set; }
            public string Source { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
        }

        private sealed class TableDiagnosticInfo
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public string ViewName { get; set; }
            public string SheetName { get; set; }
            public int RowCount { get; set; }
            public int ColumnCount { get; set; }
            public List<TableCellDiagnosticInfo> Cells { get; } = new List<TableCellDiagnosticInfo>();
        }

        private sealed class TableCellDiagnosticInfo
        {
            public int Row { get; set; }
            public int Column { get; set; }
            public string DisplayedText { get; set; }
        }

        #endregion
    }
}
