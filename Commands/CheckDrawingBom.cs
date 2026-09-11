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

    internal sealed class DrawingValueLocation
    {
        public double X { get; set; }
        public double Y { get; set; }
        public string ViewName { get; set; } = "";
        public string SheetName { get; set; } = "";
        public string Text { get; set; } = "";
    }

    internal sealed class DrawingDisplayedData
    {
        public Dictionary<string, DrawingValueLocation> ValueLocations
        { get; } = new Dictionary<string, DrawingValueLocation>();

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

        public bool HasDrawingLocation { get; set; }
        public double DrawingX { get; set; }
        public double DrawingY { get; set; }
        public string DrawingViewName { get; set; } = "";
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

        private sealed class NgNavigationTarget
        {
            public DrawingCheckItemResult Item { get; set; }
            public DrawingBomFieldResult Field { get; set; }
        }

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

                        LogDebug(
                            $"[DRAWING RESULT] component={itemResult.Component} " +
                            $"model={Path.GetFileName(itemResult.PartPath ?? "")} " +
                            $"drawing={Path.GetFileName(drawingPath)} status={itemResult.Status}");

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
                DrawingPath = !string.IsNullOrEmpty(drawingPath)
                    ? drawingPath
                    : (drawingModel != null ? drawingModel.GetPathName() : "")
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
            string sheetName = ResolveCurrentSheetName(drawing, activeSheet);

            LogDebug(
                $"[SHEET RESOLVE] sheet=\"{sheetName}\" " +
                $"filter={(string.IsNullOrWhiteSpace(sheetName) ? "ALL_VIEWS" : "CURRENT_SHEET")}");

            // 1. Quét Notes và Tables trên Sheet hiện tại.
            // Nếu SolidWorks trả tên sheet rỗng/space khi Drawing mở silent,
            // scanner sẽ quét toàn bộ view thay vì loại hết view.
            List<NoteDiagnosticInfo> notes = ScanCurrentSheetNotes(drawing, sheetName);
            List<TableDiagnosticInfo> tables = ScanCurrentSheetTables(drawing, sheetName, drawingModel);

            // 2. Trích xuất toàn bộ giá trị Displayed Text từ khung tên
            DrawingDisplayedData drawingData = ExtractAllTitleBlockValues(notes, tables, drawingModel);

            // 3. Xác định Drawing đang tham chiếu Part hay Assembly.
            //    Với Assembly Drawing, KHÔNG bắt buộc phải có 部品番号.
            ModelDoc2 referencedModel;
            string referencedConfiguration;
            GetPrimaryReferencedModel(
                drawing,
                out referencedModel,
                out referencedConfiguration);

            bool isAssemblyDrawing =
                IsAssemblyDocument(referencedModel) ||
                IsAssemblyModelPath(partPath) ||
                IsAssemblyModelPath(fileNameBom);

            if (isAssemblyDrawing)
            {
                return CheckAssemblyDrawing(
                    itemResult,
                    drawing,
                    drawingData,
                    notes,
                    tables,
                    referencedModel,
                    referencedConfiguration,
                    partPath,
                    fileNameBom);
            }

            // 4. PART DRAWING: giữ nguyên rule cũ, phải nhận diện được khung tên + 部品番号.
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

            // 5. Đọc các giá trị tương ứng từ BOM và Part Custom Properties
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

            string expectedDxf = "";
            string tehaiToUse = !string.IsNullOrWhiteSpace(bomTehaiNo)
                ? bomTehaiNo
                : drawingData.TehaiNo;

            if (!string.IsNullOrWhiteSpace(tehaiToUse) && !string.IsNullOrWhiteSpace(buhinNoBom))
                expectedDxf = $"{tehaiToUse} / {buhinNoBom}";
            else if (!string.IsNullOrWhiteSpace(buhinNoBom))
                expectedDxf = buhinNoBom;

            // 6. So sánh 12 trường Part hiện tại
            itemResult.Fields.Add(ComparePartNumber(drawingData.PartNumber, buhinNoBom, drawingData.PartNumberSource));
            itemResult.Fields.Add(CompareNumericField("W", drawingData.Width, bomW, drawingData.WidthSource));
            itemResult.Fields.Add(CompareNumericField("L", drawingData.Length, bomL, drawingData.LengthSource));
            itemResult.Fields.Add(CompareQuantityField(drawingData.Quantity, bomQty, drawingData.QuantitySource));
            itemResult.Fields.Add(CompareMaterialField(drawingData.Material, bomMaterial));
            itemResult.Fields.Add(CompareThicknessField(drawingData.Thickness, bomThickness));
            itemResult.Fields.Add(CompareGobanField(drawingData.Goban, bomGoban));
            itemResult.Fields.Add(CompareFileNameField(drawingData.PartFileName, componentName));
            itemResult.Fields.Add(CompareDxfField(drawingData.DxfFileName, expectedDxf));
            itemResult.Fields.Add(CompareOptionalStringField("品名", drawingData.ProductName, bomProductName));
            itemResult.Fields.Add(CompareOptionalStringField("現場名", drawingData.SiteName, bomSiteName));
            itemResult.Fields.Add(CompareOptionalStringField("工事番号", drawingData.JobNo, bomJobNo));

            // Gắn tọa độ cho các field Part nào đã bắt được Note thật trên Drawing.
            AttachPartDrawingLocations(itemResult, drawingData);

            FinalizeItemResult(itemResult, "BOM");
            return itemResult;
        }

        #endregion

        #region Title Block Exact Geometric & Property Recognition (3 Blocks)

        private static void SaveDrawingValueLocation(
            DrawingDisplayedData data,
            string fieldName,
            NoteDiagnosticInfo note)
        {
            if (data == null || note == null ||
                string.IsNullOrWhiteSpace(fieldName))
            {
                return;
            }

            data.ValueLocations[fieldName] =
                new DrawingValueLocation
                {
                    X = note.X,
                    Y = note.Y,
                    ViewName =
                        !string.IsNullOrWhiteSpace(note.ViewName)
                            ? note.ViewName
                            : (note.SheetName ?? ""),
                    SheetName = note.SheetName ?? "",
                    Text = note.DisplayedText ?? ""
                };
        }

        private DrawingDisplayedData ExtractAllTitleBlockValues(
            List<NoteDiagnosticInfo> notes,
            List<TableDiagnosticInfo> tables,
            ModelDoc2 drawingDoc)
        {
            DrawingDisplayedData data = new DrawingDisplayedData();

            // Assembly drawings can keep title-block notes in the sheet-format
            // annotation collection instead of the normal View.GetFirstNote chain.
            // Never return early here, otherwise property/table fallback and
            // diagnostics are skipped completely.
            if (notes == null)
                notes = new List<NoteDiagnosticInfo>();

            if (tables == null)
                tables = new List<TableDiagnosticInfo>();

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
                                SaveDrawingValueLocation(data, "部品番号", bestValueNote);
                                break;
                            case "Width":
                                data.Width = disp;
                                data.WidthRaw = raw;
                                data.WidthSource = src;
                                SaveDrawingValueLocation(data, "W", bestValueNote);
                                break;
                            case "Length":
                                data.Length = disp;
                                data.LengthRaw = raw;
                                data.LengthSource = src;
                                SaveDrawingValueLocation(data, "L", bestValueNote);
                                break;
                            case "Quantity":
                                data.Quantity = disp;
                                data.QuantityRaw = raw;
                                data.QuantitySource = src;
                                SaveDrawingValueLocation(data, "数量", bestValueNote);
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
                    if (string.IsNullOrWhiteSpace(data.JobNo))
                    {
                        data.JobNo = disp;
                        SaveDrawingValueLocation(data, "工事番号", note);
                    }
                }

                // 2. 現場名 (X ≈ 0.062, Y ≈ 0.192)
                if (raw.Contains("\"現場名\"") || raw.Contains("'現場名'") ||
                    (note.Y >= 0.188 && note.Y <= 0.195 && note.X >= 0.050 && note.X <= 0.100 && (disp.Contains("工事") || disp.Contains("AOYAMA"))))
                {
                    if (string.IsNullOrWhiteSpace(data.SiteName))
                    {
                        data.SiteName = disp;
                        SaveDrawingValueLocation(data, "現場名", note);
                    }
                }

                // 3. 品名 (X ≈ 0.062, Y ≈ 0.186)
                if (raw.Contains("\"品名\"") || raw.Contains("'品名'") ||
                    (note.Y >= 0.180 && note.Y <= 0.188 && note.X >= 0.050 && note.X <= 0.100 && (disp.Contains("ベンチ") || disp.Contains("外構") || disp.Contains("項目"))))
                {
                    if (string.IsNullOrWhiteSpace(data.ProductName))
                    {
                        data.ProductName = disp;
                        SaveDrawingValueLocation(data, "品名", note);
                    }
                }

                // 4. 材質 (X ≈ 0.110, Y ≈ 0.196)
                if (raw.Contains("\"材質\"") || raw.Contains("'材質'") ||
                    (note.Y >= 0.190 && note.X >= 0.095 && note.X <= 0.125 && (disp.StartsWith("SUS") || disp.StartsWith("SECC") || disp.StartsWith("SPCC") || disp.StartsWith("NSD"))))
                {
                    if (string.IsNullOrWhiteSpace(data.Material))
                    {
                        data.Material = disp;
                        SaveDrawingValueLocation(data, "材質", note);
                    }
                }

                // 5. 板厚 (X ≈ 0.140, Y ≈ 0.196, ví dụ: "2t" hoặc "1.6t")
                if (raw.Contains("\"板厚\"") || raw.Contains("'板厚'") ||
                    (note.Y >= 0.190 && note.X >= 0.130 && note.X <= 0.155 && Regex.IsMatch(disp, @"^\d+(\.\d+)?t?$", RegexOptions.IgnoreCase)))
                {
                    if (string.IsNullOrWhiteSpace(data.Thickness))
                    {
                        data.Thickness = disp;
                        SaveDrawingValueLocation(data, "板厚", note);
                    }
                }

                // 6. 手配番号 / No. (X ≈ 0.277, Y ≈ 0.197, ví dụ: "8198")
                if (raw.Contains("\"手配番号\"") || (note.Y >= 0.190 && note.X >= 0.260 && note.X <= 0.290 && Regex.IsMatch(disp, @"^\d+$")))
                {
                    if (string.IsNullOrWhiteSpace(data.TehaiNo))
                    {
                        data.TehaiNo = disp;
                        SaveDrawingValueLocation(data, "手配番号", note);
                    }
                }

                // 7. 仕上げ (X ≈ 0.110, Y ≈ 0.187, ví dụ: "粉体塗装")
                if (raw.Contains("\"仕上げ\"") || (note.Y >= 0.180 && note.Y <= 0.190 && note.X >= 0.095 && note.X <= 0.125))
                {
                    if (string.IsNullOrWhiteSpace(data.Finish))
                    {
                        data.Finish = disp;
                        SaveDrawingValueLocation(data, "仕上げ", note);
                    }
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
                if (string.IsNullOrWhiteSpace(data.Finish)) data.Finish = ResolvePropertyFromDrawingViews(drw, "仕上げ");
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

        private DrawingCheckItemResult CheckAssemblyDrawing(
            DrawingCheckItemResult itemResult,
            DrawingDoc drawing,
            DrawingDisplayedData drawingData,
            List<NoteDiagnosticInfo> notes,
            List<TableDiagnosticInfo> tables,
            ModelDoc2 referencedModel,
            string referencedConfiguration,
            string resolvedModelPath,
            string fileNameBom)
        {
            bool openedAssemblyByCommand = false;
            ModelDoc2 assemblyModel = referencedModel;
            string configurationName = referencedConfiguration ?? "";

            try
            {
                if (!IsAssemblyDocument(assemblyModel))
                {
                    string candidatePath = resolvedModelPath;
                    if (string.IsNullOrWhiteSpace(candidatePath) &&
                        Path.IsPathRooted(fileNameBom) &&
                        File.Exists(fileNameBom))
                    {
                        candidatePath = fileNameBom;
                    }

                    assemblyModel = OpenAssemblyDocumentSilentReadOnly(
                        candidatePath,
                        out openedAssemblyByCommand);
                }

                if (!IsAssemblyDocument(assemblyModel))
                {
                    itemResult.Status = DrawingBomCheckStatus.Warning;
                    itemResult.Note = "Không lấy được Assembly model được Drawing tham chiếu.";
                    itemResult.Fields.Add(new DrawingBomFieldResult
                    {
                        FieldName = "ASM:Model",
                        DrawingValue = Path.GetFileName(itemResult.DrawingPath ?? ""),
                        BomValue = string.IsNullOrWhiteSpace(resolvedModelPath)
                            ? "(Not Found)"
                            : resolvedModelPath,
                        Source = "ASSEMBLY_REFERENCE",
                        Status = DrawingBomCheckStatus.Warning,
                        Message = "Không mở/resolve được .SLDASM để kiểm tra Custom Property."
                    });
                    return itemResult;
                }

                if (string.IsNullOrWhiteSpace(itemResult.PartPath))
                {
                    try { itemResult.PartPath = assemblyModel.GetPathName() ?? ""; }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(configurationName))
                    configurationName = GetSafeActiveConfigurationName(assemblyModel);

                int matchedPropertyCount = AddAssemblyPropertyChecks(
                    itemResult,
                    drawingData,
                    notes,
                    tables,
                    assemblyModel,
                    configurationName);

                if (matchedPropertyCount == 0)
                {
                    itemResult.Status = DrawingBomCheckStatus.Warning;
                    itemResult.Note =
                        "Drawing không có $PRPSHEET phù hợp để đối chiếu với Assembly.";

                    itemResult.Fields.Add(new DrawingBomFieldResult
                    {
                        FieldName = "ASM:Properties",
                        DrawingValue = "-",
                        BomValue = "-",
                        Source = "ASSEMBLY_LINKED_PROPERTY",
                        Status = DrawingBomCheckStatus.Warning,
                        Message =
                            "Không tìm thấy linked note $PRPSHEET nào có thể map với Assembly."
                    });

                    return itemResult;
                }

                FinalizeItemResult(itemResult, "Assembly");

                LogDebug(
                    $"[ASM RESULT] model={Path.GetFileName(itemResult.PartPath ?? "")} " +
                    $"drawing={Path.GetFileName(itemResult.DrawingPath ?? "")} " +
                    $"config={configurationName} status={itemResult.Status} matched={matchedPropertyCount}");

                foreach (DrawingBomFieldResult field in itemResult.Fields)
                {
                    if (field == null ||
                        !field.FieldName.StartsWith("ASM:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    LogDebug(
                        $"[ASM FIELD] name=\"{field.FieldName}\" " +
                        $"drawing=\"{field.DrawingValue}\" " +
                        $"assembly=\"{field.BomValue}\" " +
                        $"status={field.Status} " +
                        $"hasLocation={field.HasDrawingLocation} " +
                        $"X={field.DrawingX:F6} Y={field.DrawingY:F6} " +
                        $"view=\"{field.DrawingViewName}\"");
                }

                return itemResult;
            }
            finally
            {
                if (openedAssemblyByCommand && assemblyModel != null && swApp != null)
                {
                    try { swApp.CloseDoc(assemblyModel.GetTitle()); }
                    catch { }
                }
            }
        }

        private static void LogAssemblyCustomProperties(
            ModelDoc2 assemblyModel,
            string configurationName)
        {
            if (assemblyModel == null)
                return;

            LogCustomPropertyManager(
                assemblyModel,
                configurationName ?? "",
                "CONFIG");

            if (!string.IsNullOrWhiteSpace(configurationName))
            {
                LogCustomPropertyManager(
                    assemblyModel,
                    "",
                    "DOCUMENT");
            }
        }

        private static void LogCustomPropertyManager(
            ModelDoc2 model,
            string configurationName,
            string scopeName)
        {
            try
            {
                CustomPropertyManager mgr =
                    model.Extension.get_CustomPropertyManager(
                        configurationName ?? "");

                if (mgr == null)
                    return;

                object namesObj = mgr.GetNames();
                Array names = namesObj as Array;

                int count = names != null ? names.Length : 0;

                LogDebug(
                    $"[ASM PROP SCOPE] scope={scopeName} " +
                    $"config=\"{configurationName}\" count={count}");

                if (names == null)
                    return;

                foreach (object nameObj in names)
                {
                    string propName =
                        Convert.ToString(nameObj ?? "").Trim();

                    if (string.IsNullOrWhiteSpace(propName))
                        continue;

                    string value = "";

                    try
                    {
                        string valOut;
                        string resolvedValOut;
                        bool wasResolved;
                        bool linkToProperty;

                        mgr.Get6(
                            propName,
                            false,
                            out valOut,
                            out resolvedValOut,
                            out wasResolved,
                            out linkToProperty);

                        value =
                            !string.IsNullOrWhiteSpace(resolvedValOut)
                                ? resolvedValOut
                                : (valOut ?? "");
                    }
                    catch
                    {
                    }

                    LogDebug(
                        $"[ASM PROP] scope={scopeName} " +
                        $"name=\"{propName}\" value=\"{value}\"");
                }
            }
            catch (Exception ex)
            {
                LogDebug(
                    $"[ASM PROP ERROR] scope={scopeName} " +
                    $"config=\"{configurationName}\" " +
                    $"message=\"{ex.Message}\"");
            }
        }

        private static NoteDiagnosticInfo FindVisibleTehaiNoNote(
            List<NoteDiagnosticInfo> notes)
        {
            if (notes == null)
                return null;

            NoteDiagnosticInfo best = null;
            double bestScore = double.MaxValue;

            // Tọa độ thực tế đã xác nhận từ các Drawing hiện tại:
            // X ~ 0.276, Y ~ 0.197.
            // Dùng vùng đủ rộng để không phụ thuộc tuyệt đối một template.
            const double targetX = 0.276;
            const double targetY = 0.197;

            foreach (NoteDiagnosticInfo note in notes)
            {
                if (note == null)
                    continue;

                string fourDigits =
                    ExtractFourDigitTehaiNo(
                        note.DisplayedText);

                if (string.IsNullOrWhiteSpace(fourDigits))
                    continue;

                // Chỉ ưu tiên vùng No. phía trên bên phải.
                bool inNoArea =
                    note.X >= 0.235 &&
                    note.X <= 0.315 &&
                    note.Y >= 0.175 &&
                    note.Y <= 0.220;

                if (!inNoArea)
                    continue;

                double dx = note.X - targetX;
                double dy = note.Y - targetY;
                double score = (dx * dx) + (dy * dy);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = note;
                }
            }

            // Fallback:
            // nếu template khác vị trí, tìm Note linked đúng "手配番号"
            // nhưng vẫn yêu cầu displayed value là đúng 4 chữ số.
            if (best == null)
            {
                foreach (NoteDiagnosticInfo note in notes)
                {
                    if (note == null)
                        continue;

                    string raw =
                        note.RawText ?? "";

                    if (raw.IndexOf(
                            "手配番号",
                            StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    string fourDigits =
                        ExtractFourDigitTehaiNo(
                            note.DisplayedText);

                    if (!string.IsNullOrWhiteSpace(fourDigits))
                    {
                        best = note;
                        break;
                    }
                }
            }

            return best;
        }

        private static string ExtractFourDigitTehaiNo(
            string displayedText)
        {
            string text =
                (displayedText ?? "").Trim();

            if (string.IsNullOrWhiteSpace(text))
                return "";

            // Trường hợp Note chỉ chứa "4993".
            Match exact =
                Regex.Match(
                    text,
                    @"^\s*(\d{4})\s*$");

            if (exact.Success)
                return exact.Groups[1].Value;

            // Trường hợp "No. 4993" nằm trong cùng một Note.
            Match withNo =
                Regex.Match(
                    text,
                    @"\bNo\.?\s*[:\-]?\s*(\d{4})\b",
                    RegexOptions.IgnoreCase);

            if (withNo.Success)
                return withNo.Groups[1].Value;

            return "";
        }

        private int AddAssemblyDrawingTypeCheck(
            DrawingCheckItemResult itemResult,
            DrawingDisplayedData drawingData,
            List<NoteDiagnosticInfo> notes,
            List<TableDiagnosticInfo> tables,
            ModelDoc2 assemblyModel)
        {
            if (itemResult == null ||
                drawingData == null ||
                assemblyModel == null)
            {
                return 0;
            }

            string assemblyName = "";

            try
            {
                assemblyName =
                    Path.GetFileNameWithoutExtension(
                        assemblyModel.GetPathName() ?? "");
            }
            catch
            {
            }

            string code =
                ExtractAssemblyDrawingTypeCode(
                    assemblyName);

            string expectedLabel =
                GetExpectedDrawingTypeLabel(
                    code);

            // Chỉ kiểm tra các mã đã xác nhận.
            if (string.IsNullOrWhiteSpace(expectedLabel))
                return 0;

            NoteDiagnosticInfo noteCandidate =
                FindDrawingTypeNote(
                    notes,
                    expectedLabel);

            if (noteCandidate != null)
            {
                string actualLabel =
                    ExtractDrawingTypeLabel(
                        noteCandidate.DisplayedText);

                if (string.IsNullOrWhiteSpace(actualLabel))
                {
                    actualLabel =
                        ExtractDrawingTypeLabel(
                            noteCandidate.RawText);
                }

                SaveDrawingValueLocation(
                    drawingData,
                    "図面区分",
                    noteCandidate);

                AddDrawingTypeResult(
                    itemResult,
                    code,
                    expectedLabel,
                    actualLabel,
                    "ASSEMBLY_DRAWING_TYPE_LABEL");

                LogDebug(
                    $"[DRAWING TYPE] code=\"{code}\" " +
                    $"expected=\"{expectedLabel}\" " +
                    $"drawing=\"{actualLabel}\" " +
                    $"source=NOTE " +
                    $"X={noteCandidate.X:F6} " +
                    $"Y={noteCandidate.Y:F6}");

                return 1;
            }

            TableDiagnosticInfo tableCandidate;
            TableCellDiagnosticInfo cellCandidate;

            if (FindDrawingTypeTableCell(
                    tables,
                    expectedLabel,
                    out tableCandidate,
                    out cellCandidate))
            {
                string actualLabel =
                    ExtractDrawingTypeLabel(
                        cellCandidate.DisplayedText);

                SaveDrawingTableValueLocation(
                    drawingData,
                    "図面区分",
                    tableCandidate,
                    cellCandidate);

                AddDrawingTypeResult(
                    itemResult,
                    code,
                    expectedLabel,
                    actualLabel,
                    "ASSEMBLY_DRAWING_TYPE_TABLE");

                LogDebug(
                    $"[DRAWING TYPE] code=\"{code}\" " +
                    $"expected=\"{expectedLabel}\" " +
                    $"drawing=\"{actualLabel}\" " +
                    $"source=TABLE " +
                    $"X={cellCandidate.X:F6} " +
                    $"Y={cellCandidate.Y:F6}");

                return 1;
            }

            // Không tìm thấy expected label.
            // Thử tìm label đối nghịch để có thể báo NG + có tọa độ.
            string oppositeLabel =
                string.Equals(
                    expectedLabel,
                    "出荷状態",
                    StringComparison.OrdinalIgnoreCase)
                    ? "サブユニット"
                    : "出荷状態";

            noteCandidate =
                FindDrawingTypeNote(
                    notes,
                    oppositeLabel);

            if (noteCandidate != null)
            {
                string actualLabel =
                    ExtractDrawingTypeLabel(
                        noteCandidate.DisplayedText);

                if (string.IsNullOrWhiteSpace(actualLabel))
                {
                    actualLabel =
                        ExtractDrawingTypeLabel(
                            noteCandidate.RawText);
                }

                SaveDrawingValueLocation(
                    drawingData,
                    "図面区分",
                    noteCandidate);

                AddDrawingTypeResult(
                    itemResult,
                    code,
                    expectedLabel,
                    actualLabel,
                    "ASSEMBLY_DRAWING_TYPE_LABEL");

                LogDebug(
                    $"[DRAWING TYPE] code=\"{code}\" " +
                    $"expected=\"{expectedLabel}\" " +
                    $"drawing=\"{actualLabel}\" " +
                    $"source=NOTE status=NG " +
                    $"X={noteCandidate.X:F6} " +
                    $"Y={noteCandidate.Y:F6}");

                return 1;
            }

            if (FindDrawingTypeTableCell(
                    tables,
                    oppositeLabel,
                    out tableCandidate,
                    out cellCandidate))
            {
                string actualLabel =
                    ExtractDrawingTypeLabel(
                        cellCandidate.DisplayedText);

                SaveDrawingTableValueLocation(
                    drawingData,
                    "図面区分",
                    tableCandidate,
                    cellCandidate);

                AddDrawingTypeResult(
                    itemResult,
                    code,
                    expectedLabel,
                    actualLabel,
                    "ASSEMBLY_DRAWING_TYPE_TABLE");

                LogDebug(
                    $"[DRAWING TYPE] code=\"{code}\" " +
                    $"expected=\"{expectedLabel}\" " +
                    $"drawing=\"{actualLabel}\" " +
                    $"source=TABLE status=NG " +
                    $"X={cellCandidate.X:F6} " +
                    $"Y={cellCandidate.Y:F6}");

                return 1;
            }

            // ---------------------------------------------------------
            // Fallback theo đúng SLOT của 図面区分.
            // Nếu người dùng đã sửa "出荷状態" thành text khác (ví dụ "a"),
            // ta vẫn phải bắt được giá trị thực tế + tọa độ để báo NG/zoom.
            // ---------------------------------------------------------
            NoteDiagnosticInfo slotNote =
                FindDrawingTypeSlotNote(notes);

            if (slotNote != null)
            {
                string actualSlotText =
                    CleanDrawingTypeSlotText(
                        slotNote.DisplayedText);

                if (string.IsNullOrWhiteSpace(actualSlotText))
                {
                    actualSlotText =
                        CleanDrawingTypeSlotText(
                            slotNote.RawText);
                }

                if (!string.IsNullOrWhiteSpace(actualSlotText))
                {
                    SaveDrawingValueLocation(
                        drawingData,
                        "図面区分",
                        slotNote);

                    AddDrawingTypeResult(
                        itemResult,
                        code,
                        expectedLabel,
                        actualSlotText,
                        "ASSEMBLY_DRAWING_TYPE_LABEL");

                    LogDebug(
                        $"[DRAWING TYPE] code=\"{code}\" " +
                        $"expected=\"{expectedLabel}\" " +
                        $"drawing=\"{actualSlotText}\" " +
                        $"source=SLOT_FALLBACK " +
                        $"X={slotNote.X:F6} " +
                        $"Y={slotNote.Y:F6}");

                    return 1;
                }
            }

            // Thật sự không tìm thấy slot.
            itemResult.Fields.Add(
                new DrawingBomFieldResult
                {
                    FieldName = "ASM:図面区分",
                    DrawingValue = "(Không tìm thấy)",
                    BomValue = expectedLabel,
                    Source = "ASSEMBLY_DRAWING_TYPE_LABEL",
                    Status = DrawingBomCheckStatus.NG,
                    Message =
                        $"Assembly code -{code}- yêu cầu Drawing label '{expectedLabel}' nhưng không tìm thấy."
                });

            LogDebug(
                $"[DRAWING TYPE] code=\"{code}\" " +
                $"expected=\"{expectedLabel}\" " +
                $"drawing=\"(NOT FOUND)\" status=NG");

            return 1;
        }

        private static void AddDrawingTypeResult(
            DrawingCheckItemResult itemResult,
            string code,
            string expectedLabel,
            string actualLabel,
            string source)
        {
            if (itemResult == null)
                return;

            string expected =
                NormalizeText(
                    expectedLabel);

            string actual =
                NormalizeText(
                    actualLabel);

            bool ok =
                !string.IsNullOrWhiteSpace(actual) &&
                string.Equals(
                    actual,
                    expected,
                    StringComparison.OrdinalIgnoreCase);

            itemResult.Fields.Add(
                new DrawingBomFieldResult
                {
                    FieldName = "ASM:図面区分",
                    DrawingValue =
                        string.IsNullOrWhiteSpace(actualLabel)
                            ? "(Trống)"
                            : actualLabel,
                    BomValue = expectedLabel,
                    Source = source,
                    Status =
                        ok
                            ? DrawingBomCheckStatus.OK
                            : DrawingBomCheckStatus.NG,
                    Message =
                        ok
                            ? ""
                            : $"Assembly code -{code}- phải là '{expectedLabel}' nhưng Drawing đang là '{actualLabel}'."
                });
        }

        private static string ExtractAssemblyDrawingTypeCode(
            string assemblyName)
        {
            string name =
                (assemblyName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name))
                return "";

            string[] parts =
                name.Split(
                    new[] { '-' },
                    StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
                return "";

            string code =
                (parts[1] ?? "").Trim();

            if (code == "02" ||
                code == "03")
            {
                return code;
            }

            return "";
        }

        private static string GetExpectedDrawingTypeLabel(
            string code)
        {
            if (code == "02")
                return "出荷状態";

            if (code == "03")
                return "サブユニット";

            return "";
        }

        private static string ExtractDrawingTypeLabel(
            string text)
        {
            string normalized =
                NormalizeText(
                    text ?? "");

            string compact =
                normalized
                .Replace(" ", "")
                .Replace("　", "");

            if (compact.IndexOf(
                    "出荷状態",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "出荷状態";
            }

            if (compact.IndexOf(
                    "サブユニット",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "サブユニット";
            }

            return "";
        }

        private static NoteDiagnosticInfo FindDrawingTypeSlotNote(
            List<NoteDiagnosticInfo> notes)
        {
            if (notes == null)
                return null;

            // Tọa độ slot đã xác nhận trên template hiện tại.
            const double targetX = 0.254899;
            const double targetY = 0.187265;

            NoteDiagnosticInfo best = null;
            double bestDistance2 = double.MaxValue;

            foreach (NoteDiagnosticInfo note in notes)
            {
                if (note == null)
                    continue;

                // Chỉ xét khu vực khung tên quanh 図番 / 出荷状態.
                if (note.X < 0.245 ||
                    note.X > 0.265 ||
                    note.Y < 0.180 ||
                    note.Y > 0.191)
                {
                    continue;
                }

                string text =
                    CleanDrawingTypeSlotText(
                        note.DisplayedText);

                if (string.IsNullOrWhiteSpace(text))
                {
                    text =
                        CleanDrawingTypeSlotText(
                            note.RawText);
                }

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                // Loại các note lân cận không phải slot.
                if (string.Equals(
                        text,
                        "No.",
                        StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("図 番") ||
                    text.Contains("図番"))
                {
                    continue;
                }

                if (Regex.IsMatch(text, @"^\d{4}$"))
                    continue;

                double dx = note.X - targetX;
                double dy = note.Y - targetY;
                double distance2 =
                    (dx * dx) +
                    (dy * dy);

                if (distance2 < bestDistance2)
                {
                    bestDistance2 =
                        distance2;

                    best = note;
                }
            }

            return best;
        }

        private static string CleanDrawingTypeSlotText(
            string text)
        {
            string value =
                (text ?? "")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            if (string.IsNullOrWhiteSpace(value))
                return "";

            value =
                Regex.Replace(
                    value,
                    @"<[^>]+>",
                    "");

            return
                NormalizeText(value)
                .Trim();
        }

        private static NoteDiagnosticInfo FindDrawingTypeNote(
            List<NoteDiagnosticInfo> notes,
            string wantedLabel)
        {
            if (notes == null ||
                string.IsNullOrWhiteSpace(wantedLabel))
            {
                return null;
            }

            NoteDiagnosticInfo best = null;
            double bestScore = double.MaxValue;

            // Khu vực title block ngay dưới No. ở góc phải trên.
            const double targetX = 0.270;
            const double targetY = 0.180;

            foreach (NoteDiagnosticInfo note in notes)
            {
                if (note == null)
                    continue;

                string label =
                    ExtractDrawingTypeLabel(
                        note.DisplayedText);

                if (string.IsNullOrWhiteSpace(label))
                {
                    label =
                        ExtractDrawingTypeLabel(
                            note.RawText);
                }

                if (!string.Equals(
                        label,
                        wantedLabel,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool inTitleArea =
                    note.X >= 0.220 &&
                    note.X <= 0.330 &&
                    note.Y >= 0.145 &&
                    note.Y <= 0.215;

                double dx =
                    note.X - targetX;

                double dy =
                    note.Y - targetY;

                double score =
                    (dx * dx) +
                    (dy * dy);

                // Ưu tiên label ở vùng title block,
                // nhưng vẫn có fallback nếu template khác.
                if (!inTitleArea)
                    score += 1.0;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = note;
                }
            }

            return best;
        }

        private static bool FindDrawingTypeTableCell(
            List<TableDiagnosticInfo> tables,
            string wantedLabel,
            out TableDiagnosticInfo bestTable,
            out TableCellDiagnosticInfo bestCell)
        {
            bestTable = null;
            bestCell = null;

            if (tables == null ||
                string.IsNullOrWhiteSpace(wantedLabel))
            {
                return false;
            }

            double bestScore = double.MaxValue;

            const double targetX = 0.270;
            const double targetY = 0.180;

            foreach (TableDiagnosticInfo table in tables)
            {
                if (table == null)
                    continue;

                foreach (TableCellDiagnosticInfo cell in table.Cells)
                {
                    if (cell == null)
                        continue;

                    string label =
                        ExtractDrawingTypeLabel(
                            cell.DisplayedText);

                    if (!string.Equals(
                            label,
                            wantedLabel,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool inTitleArea =
                        cell.X >= 0.220 &&
                        cell.X <= 0.330 &&
                        cell.Y >= 0.145 &&
                        cell.Y <= 0.215;

                    double dx =
                        cell.X - targetX;

                    double dy =
                        cell.Y - targetY;

                    double score =
                        (dx * dx) +
                        (dy * dy);

                    if (!inTitleArea)
                        score += 1.0;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestTable = table;
                        bestCell = cell;
                    }
                }
            }

            return
                bestTable != null &&
                bestCell != null;
        }

        private int AddAssemblyPropertyChecks(
            DrawingCheckItemResult itemResult,
            DrawingDisplayedData drawingData,
            List<NoteDiagnosticInfo> notes,
            List<TableDiagnosticInfo> tables,
            ModelDoc2 assemblyModel,
            string configurationName)
        {
            if (itemResult == null ||
                drawingData == null ||
                assemblyModel == null)
            {
                return 0;
            }

            int matchedPropertyCount = 0;

            HashSet<string> checkedLogicalNames =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            if (notes == null)
                notes = new List<NoteDiagnosticInfo>();

            // -------------------------------------------------------------
            // 手配番号:
            // Ưu tiên đúng số 4 chữ số đang HIỂN THỊ kế bên "No."
            // trên khung tên Drawing. Không dùng giá trị resolve từ model
            // nếu Drawing đang hiển thị một số khác.
            // -------------------------------------------------------------
            NoteDiagnosticInfo tehaiDisplayNote =
                FindVisibleTehaiNoNote(notes);

            if (tehaiDisplayNote != null)
            {
                string drawingTehaiNo =
                    ExtractFourDigitTehaiNo(
                        tehaiDisplayNote.DisplayedText);

                string assemblyTehaiNo =
                    GetModelCustomProperty(
                        assemblyModel,
                        configurationName,
                        "手配番号");

                if (!string.IsNullOrWhiteSpace(drawingTehaiNo) &&
                    !string.IsNullOrWhiteSpace(assemblyTehaiNo))
                {
                    drawingData.TehaiNo =
                        drawingTehaiNo;

                    SaveDrawingValueLocation(
                        drawingData,
                        "手配番号",
                        tehaiDisplayNote);

                    checkedLogicalNames.Add(
                        "手配番号");

                    matchedPropertyCount +=
                        AddAssemblyPropertyComparison(
                            itemResult,
                            "手配番号",
                            drawingTehaiNo,
                            assemblyTehaiNo,
                            "ASSEMBLY_TITLEBLOCK_NO");

                    LogDebug(
                        $"[TEHAI NO] drawing=\"{drawingTehaiNo}\" " +
                        $"assembly=\"{assemblyTehaiNo}\" " +
                        $"X={tehaiDisplayNote.X:F6} " +
                        $"Y={tehaiDisplayNote.Y:F6}");
                }
            }

            matchedPropertyCount +=
                AddAssemblyDrawingTypeCheck(
                    itemResult,
                    drawingData,
                    notes,
                    tables,
                    assemblyModel);

            foreach (NoteDiagnosticInfo note in notes)
            {
                if (note == null)
                    continue;

                string raw = note.RawText ?? "";

                if (string.IsNullOrWhiteSpace(raw) ||
                    !raw.Contains("$PRPSHEET"))
                {
                    continue;
                }

                Match match = Regex.Match(
                    raw,
                    @"\$PRPSHEET\s*:\s*""([^""]+)""",
                    RegexOptions.IgnoreCase);

                if (!match.Success)
                    continue;

                string propertyName =
                    (match.Groups[1].Value ?? "").Trim();

                if (string.IsNullOrWhiteSpace(propertyName))
                    continue;

                // ---------------------------------------------------------
                // SOLIDWORKS system property:
                // SW-ﾌｧｲﾙ名(File Name)
                // Drawing hiển thị:
                // Assem-ﾌｧｲﾙ名 : 4292-02-3FG-1
                // ---------------------------------------------------------
                if (IsSolidWorksFileNameProperty(propertyName))
                {
                    const string logicalName = "Assem-ﾌｧｲﾙ名";

                    if (!checkedLogicalNames.Add(logicalName))
                        continue;

                    string drawingFileName =
                        ExtractDisplayedAssemblyFileName(
                            note.DisplayedText);

                    string assemblyFileName = "";

                    try
                    {
                        assemblyFileName =
                            Path.GetFileNameWithoutExtension(
                                assemblyModel.GetPathName() ?? "");
                    }
                    catch
                    {
                    }

                    SaveDrawingValueLocation(
                        drawingData,
                        logicalName,
                        note);

                    matchedPropertyCount +=
                        AddAssemblyPropertyComparison(
                            itemResult,
                            logicalName,
                            drawingFileName,
                            assemblyFileName);

                    continue;
                }

                // ---------------------------------------------------------
                // Normal custom property.
                // Chỉ compare khi property đó thật sự tồn tại trong Assembly.
                // ---------------------------------------------------------
                string assemblyValue =
                    GetModelCustomProperty(
                        assemblyModel,
                        configurationName,
                        propertyName);

                if (string.IsNullOrWhiteSpace(assemblyValue))
                    continue;

                if (!checkedLogicalNames.Add(propertyName))
                    continue;

                string drawingValue =
                    (note.DisplayedText ?? "").Trim();

                SaveDrawingValueLocation(
                    drawingData,
                    propertyName,
                    note);

                matchedPropertyCount +=
                    AddAssemblyPropertyComparison(
                        itemResult,
                        propertyName,
                        drawingValue,
                        assemblyValue);
            }

            matchedPropertyCount += AddAssemblySummaryTableChecks(
                itemResult,
                drawingData,
                tables,
                assemblyModel,
                configurationName,
                checkedLogicalNames);

            AttachAssemblyDrawingLocations(
                itemResult,
                drawingData);

            return matchedPropertyCount;
        }

        private int AddAssemblySummaryTableChecks(
            DrawingCheckItemResult itemResult,
            DrawingDisplayedData drawingData,
            List<TableDiagnosticInfo> tables,
            ModelDoc2 assemblyModel,
            string configurationName,
            HashSet<string> checkedLogicalNames)
        {
            if (itemResult == null ||
                drawingData == null ||
                assemblyModel == null ||
                tables == null)
            {
                return 0;
            }

            TableDiagnosticInfo summaryTable =
                FindAssemblySummaryTable(tables);

            if (summaryTable == null)
                return 0;

            int added = 0;

            added += AddAssemblySummaryTableField(
                itemResult,
                drawingData,
                summaryTable,
                assemblyModel,
                configurationName,
                checkedLogicalNames,
                "合番");

            added += AddAssemblySummaryTableField(
                itemResult,
                drawingData,
                summaryTable,
                assemblyModel,
                configurationName,
                checkedLogicalNames,
                "数量");

            return added;
        }

        private int AddAssemblySummaryTableField(
            DrawingCheckItemResult itemResult,
            DrawingDisplayedData drawingData,
            TableDiagnosticInfo table,
            ModelDoc2 assemblyModel,
            string configurationName,
            HashSet<string> checkedLogicalNames,
            string logicalName)
        {
            if (table == null ||
                string.IsNullOrWhiteSpace(logicalName))
            {
                return 0;
            }

            if (checkedLogicalNames != null &&
                !checkedLogicalNames.Add(logicalName))
            {
                return 0;
            }

            int column =
                FindTableHeaderColumn(
                    table,
                    logicalName);

            if (column < 0)
                return 0;

            TableCellDiagnosticInfo valueCell =
                FindTableCell(
                    table,
                    1,
                    column);

            if (valueCell == null)
                return 0;

            string drawingValue =
                CleanTableCellText(
                    valueCell.DisplayedText);

            string assemblyValue =
                GetModelCustomProperty(
                    assemblyModel,
                    configurationName,
                    logicalName);

            if (string.IsNullOrWhiteSpace(assemblyValue))
                return 0;

            SaveDrawingTableValueLocation(
                drawingData,
                logicalName,
                table,
                valueCell);

            return AddAssemblyPropertyComparison(
                itemResult,
                logicalName,
                drawingValue,
                assemblyValue,
                "ASSEMBLY_TABLE_PROPERTY");
        }

        private static TableDiagnosticInfo FindAssemblySummaryTable(
            List<TableDiagnosticInfo> tables)
        {
            if (tables == null)
                return null;

            foreach (TableDiagnosticInfo table in tables)
            {
                if (table == null ||
                    table.RowCount < 2 ||
                    table.ColumnCount < 3)
                {
                    continue;
                }

                string h0 =
                    CleanTableCellText(
                        GetTableCellText(
                            table,
                            0,
                            0));

                string h1 =
                    CleanTableCellText(
                        GetTableCellText(
                            table,
                            0,
                            1));

                string h2 =
                    CleanTableCellText(
                        GetTableCellText(
                            table,
                            0,
                            2));

                if (h0.Contains("合番") &&
                    h1.Contains("重量") &&
                    h2.Contains("数量"))
                {
                    return table;
                }
            }

            return null;
        }

        private static int FindTableHeaderColumn(
            TableDiagnosticInfo table,
            string logicalName)
        {
            if (table == null ||
                string.IsNullOrWhiteSpace(logicalName))
            {
                return -1;
            }

            for (int c = 0; c < table.ColumnCount; c++)
            {
                string header =
                    CleanTableCellText(
                        GetTableCellText(
                            table,
                            0,
                            c));

                if (string.Equals(
                        NormalizeText(header),
                        NormalizeText(logicalName),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }

            return -1;
        }

        private static TableCellDiagnosticInfo FindTableCell(
            TableDiagnosticInfo table,
            int row,
            int column)
        {
            if (table == null)
                return null;

            foreach (TableCellDiagnosticInfo cell in table.Cells)
            {
                if (cell != null &&
                    cell.Row == row &&
                    cell.Column == column)
                {
                    return cell;
                }
            }

            return null;
        }

        private static string GetTableCellText(
            TableDiagnosticInfo table,
            int row,
            int column)
        {
            TableCellDiagnosticInfo cell =
                FindTableCell(
                    table,
                    row,
                    column);

            return cell != null
                ? (cell.DisplayedText ?? "")
                : "";
        }

        private static string CleanTableCellText(
            string text)
        {
            string value =
                text ?? "";

            value =
                Regex.Replace(
                    value,
                    @"<[^>]+>",
                    "");

            return
                NormalizeText(value)
                .Trim();
        }

        private static void SaveDrawingTableValueLocation(
            DrawingDisplayedData data,
            string fieldName,
            TableDiagnosticInfo table,
            TableCellDiagnosticInfo cell)
        {
            if (data == null ||
                table == null ||
                cell == null ||
                string.IsNullOrWhiteSpace(fieldName))
            {
                return;
            }

            data.ValueLocations[fieldName] =
                new DrawingValueLocation
                {
                    X = cell.X,
                    Y = cell.Y,
                    ViewName =
                        !string.IsNullOrWhiteSpace(table.ViewName)
                            ? table.ViewName
                            : (table.SheetName ?? ""),
                    SheetName = table.SheetName ?? "",
                    Text = cell.DisplayedText ?? ""
                };
        }

        private static bool IsSolidWorksFileNameProperty(
            string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                return false;

            string n =
                NormalizeText(propertyName)
                .Replace(" ", "");

            return
                n.IndexOf(
                    "SW-ﾌｧｲﾙ名",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf(
                    "SW-ファイル名",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf(
                    "SW-FileName",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExtractDisplayedAssemblyFileName(
            string displayedText)
        {
            string text =
                (displayedText ?? "").Trim();

            if (string.IsNullOrWhiteSpace(text))
                return "";

            int colonIndex = text.IndexOf(':');

            if (colonIndex >= 0 &&
                colonIndex + 1 < text.Length)
            {
                text =
                    text.Substring(
                        colonIndex + 1)
                    .Trim();
            }

            return
                Path.GetFileNameWithoutExtension(text);
        }

        private static string GetLocatedDrawingValue(
            DrawingDisplayedData drawingData,
            string logicalName,
            string fallbackValue)
        {
            if (drawingData == null ||
                string.IsNullOrWhiteSpace(logicalName))
            {
                return "";
            }

            DrawingValueLocation location;

            // For Assembly verification, a Drawing value is trusted only if
            // we actually found the corresponding Note on the Drawing.
            if (!drawingData.ValueLocations.TryGetValue(
                    logicalName,
                    out location) ||
                location == null)
            {
                return "";
            }

            string text =
                (location.Text ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(text))
                return text;

            return
                (fallbackValue ?? "").Trim();
        }

        private static int AddAssemblyPropertyComparison(
            DrawingCheckItemResult itemResult,
            string logicalName,
            string drawingValue,
            string assemblyValue,
            string source = "ASSEMBLY_LINKED_PROPERTY")
        {
            if (itemResult == null || string.IsNullOrWhiteSpace(logicalName))
                return 0;

            // Chỉ kiểm tra những property thực sự tồn tại trong Assembly.
            // Tránh tạo Warning giả cho các property tùy chọn không được công ty sử dụng.
            if (string.IsNullOrWhiteSpace(assemblyValue))
                return 0;

            string draw = (drawingValue ?? "").Trim();
            string asm = (assemblyValue ?? "").Trim();

            DrawingBomFieldResult field = new DrawingBomFieldResult
            {
                FieldName = "ASM:" + logicalName,
                DrawingValue = string.IsNullOrWhiteSpace(draw) ? "(Trống)" : draw,
                BomValue = asm,
                Source = source
            };

            if (string.IsNullOrWhiteSpace(draw))
            {
                field.Status = DrawingBomCheckStatus.Warning;
                field.Message = $"Drawing không đọc được {logicalName}, Assembly='{asm}'.";
            }
            else if (AreAssemblyValuesEquivalent(logicalName, draw, asm))
            {
                field.Status = DrawingBomCheckStatus.OK;
            }
            else
            {
                field.Status = DrawingBomCheckStatus.NG;
                field.Message = $"Drawing='{draw}' != Assembly='{asm}'";
            }

            itemResult.Fields.Add(field);
            return 1;
        }

        private static bool AreAssemblyValuesEquivalent(
            string logicalName,
            string drawingValue,
            string assemblyValue)
        {
            if (string.Equals(logicalName, "板厚", StringComparison.OrdinalIgnoreCase))
            {
                string d = Regex.Replace(drawingValue ?? "", @"[tTmM\s]", "").Trim();
                string a = Regex.Replace(assemblyValue ?? "", @"[tTmM\s]", "").Trim();

                double dv;
                double av;
                if (double.TryParse(d.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out dv) &&
                    double.TryParse(a.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out av))
                {
                    return Math.Abs(dv - av) <= NumericTolerance;
                }
            }

            string normDrawing = NormalizeText(drawingValue).TrimEnd('-').Trim();
            string normAssembly = NormalizeText(assemblyValue).TrimEnd('-').Trim();

            return string.Equals(
                normDrawing,
                normAssembly,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void AttachAssemblyDrawingLocations(
            DrawingCheckItemResult itemResult,
            DrawingDisplayedData drawingData)
        {
            if (itemResult == null || drawingData == null)
                return;

            foreach (DrawingBomFieldResult field in itemResult.Fields)
            {
                if (field == null ||
                    string.IsNullOrWhiteSpace(field.FieldName) ||
                    !field.FieldName.StartsWith("ASM:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string logicalName = field.FieldName.Substring(4);
                DrawingValueLocation location;

                if (drawingData.ValueLocations.TryGetValue(logicalName, out location))
                {
                    field.HasDrawingLocation = true;
                    field.DrawingX = location.X;
                    field.DrawingY = location.Y;
                    field.DrawingViewName = location.ViewName;
                }
            }
        }

        private static void AttachPartDrawingLocations(
            DrawingCheckItemResult itemResult,
            DrawingDisplayedData drawingData)
        {
            if (itemResult == null || drawingData == null)
                return;

            foreach (DrawingBomFieldResult field in itemResult.Fields)
            {
                if (field == null || string.IsNullOrWhiteSpace(field.FieldName))
                    continue;

                if (field.FieldName.StartsWith("ASM:", StringComparison.OrdinalIgnoreCase))
                    continue;

                DrawingValueLocation location;
                if (drawingData.ValueLocations.TryGetValue(field.FieldName, out location))
                {
                    field.HasDrawingLocation = true;
                    field.DrawingX = location.X;
                    field.DrawingY = location.Y;
                    field.DrawingViewName = location.ViewName;
                }
            }
        }

        private static void FinalizeItemResult(
            DrawingCheckItemResult itemResult,
            string comparisonLabel)
        {
            if (itemResult == null)
                return;

            bool hasNg = false;
            bool hasWarning = false;
            List<string> errorNotes = new List<string>();

            foreach (DrawingBomFieldResult field in itemResult.Fields)
            {
                if (field == null)
                    continue;

                if (field.Status == DrawingBomCheckStatus.NG)
                {
                    hasNg = true;
                    errorNotes.Add(
                        $"{field.FieldName}: {comparisonLabel}={field.BomValue} / Drawing={field.DrawingValue}");
                }
                else if (field.Status == DrawingBomCheckStatus.Warning)
                {
                    hasWarning = true;
                    if (!string.IsNullOrEmpty(field.Message))
                        errorNotes.Add($"{field.FieldName}: {field.Message}");
                }
            }

            if (hasNg)
                itemResult.Status = DrawingBomCheckStatus.NG;
            else if (hasWarning)
                itemResult.Status = DrawingBomCheckStatus.Warning;
            else
                itemResult.Status = DrawingBomCheckStatus.OK;

            itemResult.Note = string.Join(" | ", errorNotes);
        }

        private static bool IsAssemblyModelPath(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
                return false;

            return string.Equals(
                Path.GetExtension(pathOrName),
                ".SLDASM",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAssemblyDocument(ModelDoc2 model)
        {
            if (model == null)
                return false;

            try
            {
                return model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY;
            }
            catch
            {
                return false;
            }
        }

        private static void GetPrimaryReferencedModel(
            DrawingDoc drawing,
            out ModelDoc2 referencedModel,
            out string referencedConfiguration)
        {
            referencedModel = null;
            referencedConfiguration = "";

            if (drawing == null)
                return;

            ModelDoc2 firstReferencedModel = null;
            string firstReferencedConfiguration = "";

            try
            {
                SolidWorks.Interop.sldworks.View view =
                    drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;

                while (view != null)
                {
                    ModelDoc2 model = null;
                    try { model = view.ReferencedDocument as ModelDoc2; }
                    catch { }

                    if (model != null)
                    {
                        string cfg = "";
                        try { cfg = view.ReferencedConfiguration ?? ""; }
                        catch { }

                        if (firstReferencedModel == null)
                        {
                            firstReferencedModel = model;
                            firstReferencedConfiguration = cfg;
                        }

                        if (IsAssemblyDocument(model))
                        {
                            referencedModel = model;
                            referencedConfiguration = cfg;
                            return;
                        }
                    }

                    view = view.GetNextView() as SolidWorks.Interop.sldworks.View;
                }
            }
            catch { }

            referencedModel = firstReferencedModel;
            referencedConfiguration = firstReferencedConfiguration;
        }

        private ModelDoc2 OpenAssemblyDocumentSilentReadOnly(
            string assemblyPath,
            out bool openedByCommand)
        {
            openedByCommand = false;

            if (swApp == null ||
                string.IsNullOrWhiteSpace(assemblyPath) ||
                !File.Exists(assemblyPath) ||
                !IsAssemblyModelPath(assemblyPath))
            {
                return null;
            }

            ModelDoc2 alreadyOpen = swApp.GetOpenDocumentByName(assemblyPath) as ModelDoc2;
            if (alreadyOpen != null)
                return alreadyOpen;

            int errors = 0;
            int warnings = 0;
            int options = (int)(
                swOpenDocOptions_e.swOpenDocOptions_Silent |
                swOpenDocOptions_e.swOpenDocOptions_ReadOnly);

            ModelDoc2 opened = swApp.OpenDoc6(
                assemblyPath,
                (int)swDocumentTypes_e.swDocASSEMBLY,
                options,
                "",
                ref errors,
                ref warnings) as ModelDoc2;

            openedByCommand = opened != null;
            return opened;
        }

        private static string GetSafeActiveConfigurationName(ModelDoc2 model)
        {
            if (model == null)
                return "";

            try
            {
                ConfigurationManager mgr = model.ConfigurationManager;
                Configuration cfg = mgr != null ? mgr.ActiveConfiguration : null;
                return cfg != null ? (cfg.Name ?? "") : "";
            }
            catch
            {
                return "";
            }
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

                    // Resolve model path BEFORE returning the Drawing path.
                    // Old code returned .SLDDRW first, which left resolvedPartPath empty for Assembly rows.
                    if (string.IsNullOrWhiteSpace(resolvedPartPath))
                    {
                        string prtUpper = Path.Combine(dir, baseName + ".SLDPRT");
                        string prtLower = Path.Combine(dir, baseName + ".sldprt");
                        string asmUpper = Path.Combine(dir, baseName + ".SLDASM");
                        string asmLower = Path.Combine(dir, baseName + ".sldasm");

                        if (File.Exists(prtUpper))
                            resolvedPartPath = prtUpper;
                        else if (File.Exists(prtLower))
                            resolvedPartPath = prtLower;
                        else if (File.Exists(asmUpper))
                            resolvedPartPath = asmUpper;
                        else if (File.Exists(asmLower))
                            resolvedPartPath = asmLower;
                    }

                    string drwUpper = Path.Combine(dir, baseName + ".SLDDRW");
                    if (File.Exists(drwUpper)) return drwUpper;

                    string drwLower = Path.Combine(dir, baseName + ".slddrw");
                    if (File.Exists(drwLower)) return drwLower;
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

        private List<NoteDiagnosticInfo> ScanCurrentSheetNotes(
            DrawingDoc drawing,
            string targetSheetName)
        {
            List<NoteDiagnosticInfo> result =
                new List<NoteDiagnosticInfo>();

            if (drawing == null)
                return result;

            HashSet<string> seen =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            int noteCounter = 1;

            // PASS 1:
            // Normal notes owned by Drawing Views / Sheet View.
            try
            {
                SolidWorks.Interop.sldworks.View view =
                    drawing.GetFirstView()
                    as SolidWorks.Interop.sldworks.View;

                while (view != null)
                {
                    if (IsViewOnSheet(
                            view,
                            targetSheetName))
                    {
                        string viewName =
                            view.Name ?? "";

                        try
                        {
                            Note note =
                                view.GetFirstNote()
                                as Note;

                            while (note != null)
                            {
                                try
                                {
                                    Annotation ann =
                                        note.GetAnnotation()
                                        as Annotation;

                                    NoteDiagnosticInfo info =
                                        ExtractNoteInfo(
                                            note,
                                            ann,
                                            viewName,
                                            targetSheetName,
                                            noteCounter);

                                    if (TryAddUniqueNote(
                                            result,
                                            seen,
                                            info))
                                    {
                                        noteCounter++;
                                    }
                                }
                                catch
                                {
                                }

                                note =
                                    note.GetNext()
                                    as Note;
                            }
                        }
                        catch
                        {
                        }
                    }

                    view =
                        view.GetNextView()
                        as SolidWorks.Interop.sldworks.View;
                }
            }
            catch
            {
            }

            // PASS 2:
            // Sheet-format notes are not always returned by View.GetFirstNote().
            // Walk the current document annotation chain as a fallback.
            try
            {
                ModelDoc2 model =
                    drawing as ModelDoc2;

                if (model != null)
                {
                    Annotation ann =
                        model.GetFirstAnnotation2()
                        as Annotation;

                    while (ann != null)
                    {
                        try
                        {
                            Note note =
                                ann.GetSpecificAnnotation()
                                as Note;

                            if (note != null)
                            {
                                NoteDiagnosticInfo info =
                                    ExtractNoteInfo(
                                        note,
                                        ann,
                                        targetSheetName,
                                        targetSheetName,
                                        noteCounter);

                                if (TryAddUniqueNote(
                                        result,
                                        seen,
                                        info))
                                {
                                    noteCounter++;
                                }
                            }
                        }
                        catch
                        {
                        }

                        try
                        {
                            ann =
                                ann.GetNext3()
                                as Annotation;
                        }
                        catch
                        {
                            ann = null;
                        }
                    }
                }
            }
            catch
            {
            }

            LogDebug(
                $"[NOTE SCAN] sheet=\"" +
                $"{(string.IsNullOrWhiteSpace(targetSheetName) ? "<ALL_VIEWS>" : targetSheetName)}\" " +
                $"count={result.Count}");

            return result;
        }

        private static bool TryAddUniqueNote(
            List<NoteDiagnosticInfo> result,
            HashSet<string> seen,
            NoteDiagnosticInfo info)
        {
            if (result == null ||
                seen == null ||
                info == null)
            {
                return false;
            }

            string key =
                (info.Name ?? "") + "|" +
                (info.DisplayedText ?? "") + "|" +
                (info.RawText ?? "") + "|" +
                Math.Round(info.X, 7)
                    .ToString(
                        CultureInfo.InvariantCulture) + "|" +
                Math.Round(info.Y, 7)
                    .ToString(
                        CultureInfo.InvariantCulture);

            if (!seen.Add(key))
                return false;

            result.Add(info);
            return true;
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

        private static void LogTableDiagnostics(
            List<TableDiagnosticInfo> tables)
        {
            int count = tables != null ? tables.Count : 0;

            LogDebug(
                $"[TABLE SCAN] count={count}");

            if (tables == null)
                return;

            foreach (TableDiagnosticInfo table in tables)
            {
                if (table == null)
                    continue;

                LogDebug(
                    $"[TABLE] index={table.Index} " +
                    $"name=\"{table.Name}\" " +
                    $"view=\"{table.ViewName}\" " +
                    $"rows={table.RowCount} cols={table.ColumnCount} " +
                    $"X={table.X:F6} Y={table.Y:F6}");

                foreach (TableCellDiagnosticInfo cell in table.Cells)
                {
                    if (cell == null)
                        continue;

                    string text =
                        (cell.DisplayedText ?? "")
                        .Replace("\r", " ")
                        .Replace("\n", " ")
                        .Trim();

                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    LogDebug(
                        $"[TABLE CELL] table={table.Index} " +
                        $"r={cell.Row} c={cell.Column} " +
                        $"text=\"{text}\" " +
                        $"X={cell.X:F6} Y={cell.Y:F6}");
                }
            }
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

                double tableX = 0.0;
                double tableY = 0.0;

                try
                {
                    object posObj = ann != null ? ann.GetPosition() : null;
                    double[] pos = posObj as double[];

                    if (pos != null && pos.Length >= 2)
                    {
                        tableX = pos[0];
                        tableY = pos[1];
                    }
                }
                catch
                {
                }

                TableDiagnosticInfo tableInfo = new TableDiagnosticInfo
                {
                    Index = index,
                    Name = annName,
                    ViewName = viewName,
                    SheetName = sheetName,
                    RowCount = rowCount,
                    ColumnCount = colCount,
                    X = tableX,
                    Y = tableY
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

                        double cellX = tableX;
                        double cellY = tableY;

                        try
                        {
                            dynamic dynTable = table;

                            double xOffset = 0.0;
                            for (int cc = 0; cc < c; cc++)
                            {
                                xOffset += Convert.ToDouble(
                                    dynTable.GetColumnWidth(cc),
                                    CultureInfo.InvariantCulture);
                            }

                            double columnWidth =
                                Convert.ToDouble(
                                    dynTable.GetColumnWidth(c),
                                    CultureInfo.InvariantCulture);

                            double yOffset = 0.0;
                            for (int rr = 0; rr < r; rr++)
                            {
                                yOffset += Convert.ToDouble(
                                    dynTable.GetRowHeight(rr),
                                    CultureInfo.InvariantCulture);
                            }

                            double rowHeight =
                                Convert.ToDouble(
                                    dynTable.GetRowHeight(r),
                                    CultureInfo.InvariantCulture);

                            // Best effort: Annotation.GetPosition() is used as
                            // the table insertion point. For normal general tables
                            // this gives a useful center position per cell.
                            cellX =
                                tableX +
                                xOffset +
                                (columnWidth / 2.0);

                            cellY =
                                tableY -
                                yOffset -
                                (rowHeight / 2.0);
                        }
                        catch
                        {
                            // Fallback to table annotation position.
                            cellX = tableX;
                            cellY = tableY;
                        }

                        tableInfo.Cells.Add(new TableCellDiagnosticInfo
                        {
                            Row = r,
                            Column = c,
                            DisplayedText = displayText,
                            X = cellX,
                            Y = cellY
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

        private static string ResolveCurrentSheetName(
            DrawingDoc drawing,
            Sheet activeSheet)
        {
            string name = "";

            try
            {
                if (activeSheet != null)
                    name = (activeSheet.GetName() ?? "").Trim();
            }
            catch
            {
                name = "";
            }

            if (!string.IsNullOrWhiteSpace(name))
                return name;

            // Fallback 1: first Drawing View is normally the sheet view.
            try
            {
                SolidWorks.Interop.sldworks.View firstView =
                    drawing != null
                        ? drawing.GetFirstView() as SolidWorks.Interop.sldworks.View
                        : null;

                if (firstView != null)
                {
                    string viewName = (firstView.Name ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(viewName))
                        return viewName;
                }
            }
            catch
            {
            }

            // Fallback 2: DrawingDoc.GetSheetNames().
            try
            {
                object namesObj =
                    drawing != null
                        ? drawing.GetSheetNames()
                        : null;

                Array names = namesObj as Array;

                if (names != null && names.Length > 0)
                {
                    string first =
                        Convert.ToString(
                            names.GetValue(0) ?? "")
                        .Trim();

                    if (!string.IsNullOrWhiteSpace(first))
                        return first;
                }
            }
            catch
            {
            }

            // Empty is intentional:
            // IsViewOnSheet() treats it as "scan all views".
            return "";
        }

        private static bool IsViewOnSheet(
            SolidWorks.Interop.sldworks.View view,
            string targetSheetName)
        {
            if (view == null)
                return false;

            // Important for silent-open Assembly Drawings:
            // SolidWorks can return a blank/space current-sheet name.
            // In that case never reject every view.
            if (string.IsNullOrWhiteSpace(targetSheetName))
                return true;

            string target = targetSheetName.Trim();

            try
            {
                if (view.Type ==
                    (int)swDrawingViewTypes_e.swDrawingSheet)
                {
                    return string.Equals(
                        (view.Name ?? "").Trim(),
                        target,
                        StringComparison.OrdinalIgnoreCase);
                }

                Sheet sheet = view.Sheet as Sheet;

                if (sheet != null)
                {
                    string viewSheetName =
                        (sheet.GetName() ?? "").Trim();

                    return string.Equals(
                        viewSheetName,
                        target,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // If SolidWorks cannot report ownership, do not discard
                // a potentially valid note/table.
                return true;
            }

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

        private void ShowBatchSummaryDialog(DrawingBatchCheckResult result)
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

            if (result.NgCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Duyệt các lỗi NG trên Drawing?");

                DialogResult answer = MessageBox.Show(
                    sb.ToString(),
                    "CHECK DRAWING BOM",
                    MessageBoxButtons.YesNo,
                    icon);

                if (answer == DialogResult.Yes)
                {
                    NavigateThroughNgErrors(result);
                }
            }
            else
            {
                MessageBox.Show(
                    sb.ToString(),
                    "CHECK DRAWING BOM",
                    MessageBoxButtons.OK,
                    icon);
            }
        }

        private void NavigateThroughNgErrors(
            DrawingBatchCheckResult result)
        {
            if (result == null || swApp == null)
                return;

            List<NgNavigationTarget> targets =
                new List<NgNavigationTarget>();

            int totalNgFields = 0;

            foreach (DrawingCheckItemResult item in result.Items)
            {
                if (item == null ||
                    item.Status != DrawingBomCheckStatus.NG ||
                    string.IsNullOrWhiteSpace(item.DrawingPath))
                {
                    continue;
                }

                foreach (DrawingBomFieldResult field in item.Fields)
                {
                    if (field == null ||
                        field.Status != DrawingBomCheckStatus.NG)
                    {
                        continue;
                    }

                    totalNgFields++;

                    if (!field.HasDrawingLocation)
                        continue;

                    targets.Add(
                        new NgNavigationTarget
                        {
                            Item = item,
                            Field = field
                        });
                }
            }

            if (targets.Count == 0)
            {
                ShowWarning(
                    "Có kết quả NG nhưng không tìm thấy tọa độ Drawing để điều hướng.");
                return;
            }

            LogDebug(
                $"[NAVIGATE NG START] targets={targets.Count} " +
                $"totalNgFields={totalNgFields}");

            for (int i = 0; i < targets.Count; i++)
            {
                NgNavigationTarget target = targets[i];

                if (target == null ||
                    target.Item == null ||
                    target.Field == null)
                {
                    continue;
                }

                NavigateToDrawingField(
                    target.Item.DrawingPath,
                    target.Field);

                LogDebug(
                    $"[NAVIGATE NG INDEX] {i + 1}/{targets.Count} " +
                    $"drawing=\"{Path.GetFileName(target.Item.DrawingPath)}\" " +
                    $"field=\"{target.Field.FieldName}\"");

                if (i >= targets.Count - 1)
                {
                    MessageBox.Show(
                        BuildNgNavigationMessage(
                            target,
                            i + 1,
                            targets.Count,
                            false) +
                        "\n\nĐã xem hết lỗi NG có tọa độ.",
                        "CHECK DRAWING BOM - NG",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    LogDebug(
                        $"[NAVIGATE NG COMPLETE] viewed={targets.Count}");
                    break;
                }

                DialogResult next = MessageBox.Show(
                    BuildNgNavigationMessage(
                        target,
                        i + 1,
                        targets.Count,
                        true),
                    "CHECK DRAWING BOM - NG",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (next != DialogResult.Yes)
                {
                    LogDebug(
                        $"[NAVIGATE NG STOP] viewed={i + 1}/{targets.Count}");
                    break;
                }
            }
        }

        private static string BuildNgNavigationMessage(
            NgNavigationTarget target,
            int currentIndex,
            int totalCount,
            bool askNext)
        {
            if (target == null ||
                target.Item == null ||
                target.Field == null)
            {
                return "";
            }

            DrawingBomFieldResult field = target.Field;

            StringBuilder sb = new StringBuilder();

            sb.AppendLine(
                $"Lỗi NG {currentIndex} / {totalCount}");
            sb.AppendLine();
            sb.AppendLine(
                $"Drawing : {Path.GetFileName(target.Item.DrawingPath)}");
            sb.AppendLine(
                $"Field   : {field.FieldName}");
            sb.AppendLine(
                $"Drawing : {field.DrawingValue}");
            sb.AppendLine(
                $"Assembly: {field.BomValue}");

            if (askNext)
            {
                sb.AppendLine();
                sb.Append("Mở lỗi NG tiếp theo?");
            }

            return sb.ToString();
        }

        private void NavigateToDrawingField(
            string drawingPath,
            DrawingBomFieldResult field)
        {
            if (string.IsNullOrWhiteSpace(drawingPath) ||
                field == null)
            {
                return;
            }

            try
            {
                ModelDoc2 drawingModel =
                    swApp.GetOpenDocumentByName(drawingPath)
                    as ModelDoc2;

                if (drawingModel == null)
                {
                    int errors = 0;
                    int warnings = 0;

                    drawingModel = swApp.OpenDoc6(
                        drawingPath,
                        (int)swDocumentTypes_e.swDocDRAWING,
                        (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly,
                        "",
                        ref errors,
                        ref warnings)
                        as ModelDoc2;
                }

                if (drawingModel == null)
                {
                    ShowWarning(
                        "Không thể mở Drawing để điều hướng:\n" +
                        drawingPath);

                    return;
                }

                int activateErrors = 0;

                swApp.ActivateDoc3(
                    drawingModel.GetTitle(),
                    false,
                    0,
                    ref activateErrors);

                drawingModel.ClearSelection2(true);

                Annotation selectedAnnotation = null;

                if (!string.IsNullOrWhiteSpace(field.Source) &&
                    field.Source.StartsWith(
                        "ASSEMBLY_TABLE",
                        StringComparison.OrdinalIgnoreCase))
                {
                    selectedAnnotation =
                        FindNearestTableAnnotation(
                            drawingModel as DrawingDoc,
                            field.DrawingX,
                            field.DrawingY);
                }
                else
                {
                    selectedAnnotation =
                        FindNearestNoteAnnotation(
                            drawingModel,
                            field.DrawingX,
                            field.DrawingY);
                }

                bool selected = false;

                if (selectedAnnotation != null)
                {
                    try
                    {
                        selected =
                            selectedAnnotation.Select3(
                                false,
                                null);
                    }
                    catch
                    {
                        selected = false;
                    }
                }

                // Sheet-format title-block notes (e.g. No. / 手配番号)
                // may refuse selection while the Drawing is in normal sheet mode.
                // Enter sheet-format mode only as a selection fallback, zoom,
                // then immediately return to normal sheet mode.
                bool enteredSheetFormat = false;

                if (!selected &&
                    (
                        string.Equals(
                            field.Source,
                            "ASSEMBLY_TITLEBLOCK_NO",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            field.Source,
                            "ASSEMBLY_DRAWING_TYPE_LABEL",
                            StringComparison.OrdinalIgnoreCase)
                    ))
                {
                    DrawingDoc drawDoc =
                        drawingModel as DrawingDoc;

                    if (drawDoc != null)
                    {
                        try
                        {
                            drawDoc.EditTemplate();
                            enteredSheetFormat = true;

                            drawingModel.ClearSelection2(true);

                            selectedAnnotation =
                                FindNearestNoteAnnotationAllViews(
                                    drawDoc,
                                    drawingModel,
                                    field.DrawingX,
                                    field.DrawingY);

                            if (selectedAnnotation != null)
                            {
                                try
                                {
                                    selected =
                                        selectedAnnotation.Select3(
                                            false,
                                            null);
                                }
                                catch
                                {
                                    selected = false;
                                }
                            }

                            if (selected)
                            {
                                try
                                {
                                    drawingModel.ViewZoomToSelection();
                                }
                                catch
                                {
                                }
                            }
                        }
                        catch
                        {
                            selected = false;
                        }
                        finally
                        {
                            if (enteredSheetFormat)
                            {
                                try
                                {
                                    drawDoc.EditSheet();
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                }

                if (selected)
                {
                    try
                    {
                        drawingModel.ViewZoomToSelection();
                    }
                    catch
                    {
                        try
                        {
                            drawingModel.ViewZoomtofit2();
                        }
                        catch
                        {
                        }
                    }

                    LogDebug(
                        $"[NAVIGATE NG] field=\"{field.FieldName}\" " +
                        $"X={field.DrawingX:F6} Y={field.DrawingY:F6} " +
                        $"drawing=\"{Path.GetFileName(drawingPath)}\" " +
                        $"source=\"{field.Source}\" selected=True");
                }
                else
                {
                    try
                    {
                        drawingModel.ViewZoomtofit2();
                    }
                    catch
                    {
                    }

                    LogDebug(
                        $"[NAVIGATE NG] field=\"{field.FieldName}\" " +
                        $"X={field.DrawingX:F6} Y={field.DrawingY:F6} " +
                        $"drawing=\"{Path.GetFileName(drawingPath)}\" " +
                        $"source=\"{field.Source}\" selected=False");

                    ShowWarning(
                        "Đã mở Drawing nhưng chưa chọn được đối tượng lỗi.\n" +
                        $"Field: {field.FieldName}\n" +
                        $"X={field.DrawingX:F6}, Y={field.DrawingY:F6}");
                }
            }
            catch (Exception ex)
            {
                LogDebug(
                    $"[NAVIGATE NG ERROR] {ex.Message}");

                ShowWarning(
                    "Lỗi khi điều hướng tới vị trí NG:\n" +
                    ex.Message);
            }
        }

        private static Annotation FindNearestNoteAnnotationAllViews(
            DrawingDoc drawing,
            ModelDoc2 drawingModel,
            double targetX,
            double targetY)
        {
            Annotation best = null;
            double bestDistance2 = double.MaxValue;

            if (drawing != null)
            {
                try
                {
                    SolidWorks.Interop.sldworks.View view =
                        drawing.GetFirstView()
                        as SolidWorks.Interop.sldworks.View;

                    while (view != null)
                    {
                        try
                        {
                            Note note =
                                view.GetFirstNote()
                                as Note;

                            while (note != null)
                            {
                                try
                                {
                                    Annotation ann =
                                        note.GetAnnotation()
                                        as Annotation;

                                    if (ann != null)
                                    {
                                        object posObj =
                                            ann.GetPosition();

                                        double[] pos =
                                            posObj as double[];

                                        if (pos != null &&
                                            pos.Length >= 2)
                                        {
                                            double dx =
                                                pos[0] - targetX;

                                            double dy =
                                                pos[1] - targetY;

                                            double distance2 =
                                                (dx * dx) +
                                                (dy * dy);

                                            if (distance2 < bestDistance2)
                                            {
                                                bestDistance2 =
                                                    distance2;

                                                best = ann;
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                }

                                note =
                                    note.GetNext()
                                    as Note;
                            }
                        }
                        catch
                        {
                        }

                        view =
                            view.GetNextView()
                            as SolidWorks.Interop.sldworks.View;
                    }
                }
                catch
                {
                }
            }

            // Also include the document annotation chain because some
            // title-block notes are exposed only through this route.
            Annotation chainBest =
                FindNearestNoteAnnotation(
                    drawingModel,
                    targetX,
                    targetY);

            if (chainBest != null)
            {
                try
                {
                    double[] pos =
                        chainBest.GetPosition()
                        as double[];

                    if (pos != null &&
                        pos.Length >= 2)
                    {
                        double dx =
                            pos[0] - targetX;

                        double dy =
                            pos[1] - targetY;

                        double distance2 =
                            (dx * dx) +
                            (dy * dy);

                        if (distance2 < bestDistance2)
                        {
                            best = chainBest;
                        }
                    }
                }
                catch
                {
                }
            }

            return best;
        }

        private static Annotation FindNearestNoteAnnotation(
            ModelDoc2 drawingModel,
            double targetX,
            double targetY)
        {
            if (drawingModel == null)
                return null;

            Annotation best = null;
            double bestDistance2 = double.MaxValue;

            try
            {
                Annotation ann =
                    drawingModel.GetFirstAnnotation2()
                    as Annotation;

                while (ann != null)
                {
                    try
                    {
                        Note note =
                            ann.GetSpecificAnnotation()
                            as Note;

                        if (note != null)
                        {
                            object posObj =
                                ann.GetPosition();

                            double[] pos =
                                posObj as double[];

                            if (pos != null &&
                                pos.Length >= 2)
                            {
                                double dx =
                                    pos[0] - targetX;

                                double dy =
                                    pos[1] - targetY;

                                double distance2 =
                                    (dx * dx) +
                                    (dy * dy);

                                if (distance2 < bestDistance2)
                                {
                                    bestDistance2 =
                                        distance2;

                                    best = ann;
                                }
                            }
                        }
                    }
                    catch
                    {
                    }

                    ann =
                        ann.GetNext3()
                        as Annotation;
                }
            }
            catch
            {
            }

            return best;
        }

        private static Annotation FindNearestTableAnnotation(
            DrawingDoc drawing,
            double targetX,
            double targetY)
        {
            if (drawing == null)
                return null;

            Annotation best = null;
            double bestDistance2 = double.MaxValue;

            try
            {
                SolidWorks.Interop.sldworks.View view =
                    drawing.GetFirstView()
                    as SolidWorks.Interop.sldworks.View;

                while (view != null)
                {
                    object[] tableObjects = null;

                    try
                    {
                        tableObjects =
                            view.GetTableAnnotations()
                            as object[];
                    }
                    catch
                    {
                    }

                    if (tableObjects != null)
                    {
                        foreach (object obj in tableObjects)
                        {
                            ITableAnnotation table =
                                obj as ITableAnnotation;

                            if (table == null)
                                continue;

                            Annotation ann = null;

                            try
                            {
                                ann =
                                    table.GetAnnotation()
                                    as Annotation;
                            }
                            catch
                            {
                            }

                            if (ann == null)
                                continue;

                            double distance2 =
                                GetNearestTableCellDistance2(
                                    table,
                                    ann,
                                    targetX,
                                    targetY);

                            if (distance2 < bestDistance2)
                            {
                                bestDistance2 =
                                    distance2;

                                best = ann;
                            }
                        }
                    }

                    view =
                        view.GetNextView()
                        as SolidWorks.Interop.sldworks.View;
                }
            }
            catch
            {
            }

            return best;
        }

        private static double GetNearestTableCellDistance2(
            ITableAnnotation table,
            Annotation ann,
            double targetX,
            double targetY)
        {
            double tableX = 0.0;
            double tableY = 0.0;

            try
            {
                object posObj =
                    ann.GetPosition();

                double[] pos =
                    posObj as double[];

                if (pos != null &&
                    pos.Length >= 2)
                {
                    tableX = pos[0];
                    tableY = pos[1];
                }
            }
            catch
            {
            }

            double bestDistance2 =
                ((tableX - targetX) *
                 (tableX - targetX)) +
                ((tableY - targetY) *
                 (tableY - targetY));

            try
            {
                dynamic dynTable = table;

                int rowCount =
                    table.RowCount;

                int columnCount =
                    table.ColumnCount;

                double yOffset = 0.0;

                for (int r = 0; r < rowCount; r++)
                {
                    double rowHeight =
                        Convert.ToDouble(
                            dynTable.GetRowHeight(r),
                            CultureInfo.InvariantCulture);

                    double xOffset = 0.0;

                    for (int c = 0; c < columnCount; c++)
                    {
                        double columnWidth =
                            Convert.ToDouble(
                                dynTable.GetColumnWidth(c),
                                CultureInfo.InvariantCulture);

                        double cellX =
                            tableX +
                            xOffset +
                            (columnWidth / 2.0);

                        double cellY =
                            tableY -
                            yOffset -
                            (rowHeight / 2.0);

                        double dx =
                            cellX - targetX;

                        double dy =
                            cellY - targetY;

                        double distance2 =
                            (dx * dx) +
                            (dy * dy);

                        if (distance2 < bestDistance2)
                        {
                            bestDistance2 =
                                distance2;
                        }

                        xOffset +=
                            columnWidth;
                    }

                    yOffset +=
                        rowHeight;
                }
            }
            catch
            {
            }

            return bestDistance2;
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
            public double X { get; set; }
            public double Y { get; set; }
            public List<TableCellDiagnosticInfo> Cells { get; } = new List<TableCellDiagnosticInfo>();
        }

        private sealed class TableCellDiagnosticInfo
        {
            public int Row { get; set; }
            public int Column { get; set; }
            public string DisplayedText { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
        }

        #endregion
    }
}
