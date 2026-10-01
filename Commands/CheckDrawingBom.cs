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

    internal sealed class ModelPropertyLinkAudit
    {
        public string PropertyName { get; set; } = "";
        public string RawExpression { get; set; } = "";
        public string ResolvedValue { get; set; } = "";
        public string Scope { get; set; } = "";
        public bool Exists { get; set; }
        public bool IsLinked { get; set; }
    }

    internal sealed class DrawingCheckItemResult
    {
        public int BomRowIndex { get; set; } = -1;
        public string BomItemNumber { get; set; } = "";
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
        // TABLE PARSING RULES (2026-09):
        // - Không dùng tọa độ để NHẬN DIỆN table/cell.
        // - Nhận diện bằng header + row + column + linked property.
        // - X/Y chỉ dùng sau cùng cho Navigate NG.
        // - Part Drawing hỗ trợ 2 dạng 合番/数量:
        //     A) 合番 | 数量 với nhiều data row.
        //     B) 合番 | BR-02:35枚.
        // - Assembly summary không hard-code row=1.
        // - 数量 của cả COMPONENT và UNIT: Drawing ↔ đúng dòng SolidWorks BOM.
        //   Không dùng Custom Property 数量/Qty của 3D Part/Assembly làm Expected.
        private readonly ISldWorks swApp;
        private readonly DataGridView bomGrid;

        private const double NumericTolerance = 0.01; // 0.01 mm tolerance cho W, L, 板厚
        private const double HeaderYBandTolerance = 0.010; // 10mm band độ chênh Y giữa các Header
        private const double ValueMaxYDistance = 0.025; // 25mm khoảng cách tối đa bên dưới Header

        // FRAME SCOPE V2: Note lọc theo vị trí Note; Table lọc theo table anchor.
        // Chỉ đọc dữ liệu nằm TRONG phạm vi Sheet hiện tại.
        // Không hard-code A4/A3/A2: width/height lấy trực tiếp từ ISheet.
        private const double DrawingFrameEpsilon = 0.0002; // 0.2 mm, chống sai số số thực ở biên

        private sealed class SheetFrameBounds
        {
            public bool IsValid { get; set; }
            public double MinX { get; set; }
            public double MinY { get; set; }
            public double MaxX { get; set; }
            public double MaxY { get; set; }
            public string Source { get; set; } = "";

            public bool Contains(double x, double y)
            {
                if (!IsValid) return true;
                return x >= MinX - DrawingFrameEpsilon &&
                       x <= MaxX + DrawingFrameEpsilon &&
                       y >= MinY - DrawingFrameEpsilon &&
                       y <= MaxY + DrawingFrameEpsilon;
            }
        }

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
                        BomItemNumber = GetBomNumberValues(row)?.ItemNumber ?? "",
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
            string bomQuantity = GetBomQuantityFromRow(bomRow);
            string componentName = Path.GetFileNameWithoutExtension(fileNameBom);
            if (string.IsNullOrWhiteSpace(componentName))
                componentName = fileNameBom;

            LogDebug(
                $"[BOM QTY ROW] row={((bomRow != null) ? bomRow.Index : -1)} " +
                $"component=\"{componentName}\" partNo=\"{buhinNoBom}\" quantity=\"{bomQuantity}\"");

            DrawingCheckItemResult itemResult = new DrawingCheckItemResult
            {
                BomRowIndex = bomRow != null ? bomRow.Index : -1,
                BomItemNumber = GetBomNumberValues(bomRow)?.ItemNumber ?? "",
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

            // Linked drawing tables need an active document window. Opening them
            // with DocumentVisible(false) leaves $PRPSHEET display evaluation empty.
            ActivateDrawingForDisplayRead(drawingModel, drawing);
            Sheet activeSheet = drawing.GetCurrentSheet() as Sheet;
            string sheetName = ResolveCurrentSheetName(drawing, activeSheet);

            LogDebug(
                $"[SHEET RESOLVE] sheet=\"{sheetName}\" " +
                $"filter={(string.IsNullOrWhiteSpace(sheetName) ? "ALL_VIEWS" : "CURRENT_SHEET")}");

            // 1. Refresh Drawing trước khi đọc Note/Table.
            // Batch mở Drawing bằng Silent + ReadOnly nên DisplayedText/Table có thể
            // vẫn giữ giá trị cache cũ nếu chưa rebuild. ForceRebuild3 chỉ rebuild
            // trong memory; command này không Save Drawing.
            RefreshDrawingBeforeCheck(
                drawingModel,
                drawing,
                sheetName);

            // 2. Chỉ quét Notes/Tables nằm TRONG phạm vi khung Sheet hiện tại.
            // Nếu SolidWorks trả tên sheet rỗng/space khi Drawing mở silent,
            // scanner vẫn quét view nhưng mọi candidate ngoài khung sẽ bị loại.
            SheetFrameBounds frameBounds = ResolveSheetFrameBounds(activeSheet);
            LogSheetFrameBounds(sheetName, frameBounds);

            List<NoteDiagnosticInfo> notes =
                ScanCurrentSheetNotes(drawing, sheetName, frameBounds);

            List<TableDiagnosticInfo> tables =
                ScanCurrentSheetTables(drawing, sheetName, drawingModel, frameBounds);

            // 3. Trích xuất toàn bộ giá trị Displayed Text từ khung tên
            DrawingDisplayedData drawingData = ExtractAllTitleBlockValues(notes, tables, drawingModel);

            // 4. Xác định Drawing đang tham chiếu Part hay Assembly.
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
                    fileNameBom,
                    bomQuantity);
            }

            BomNumberValues bomNumbers = GetBomNumberValues(bomRow);
            itemResult.Fields.Add(CompareBomNumbers(bomNumbers));
            DrawingBomFieldResult gobanTotalField = CompareBomGobanTotal(bomNumbers);
            itemResult.Fields.Add(gobanTotalField);
            LogDebug($"[BOM GOBAN TOTAL] component=\"{componentName}\" total=\"{gobanTotalField.DrawingValue}\" bomQty=\"{gobanTotalField.BomValue}\" status={gobanTotalField.Status}");

            // 5. PART DRAWING: giữ nguyên rule cũ, phải nhận diện được khung tên + 部品番号.
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

            // 6. Đọc các giá trị tương ứng từ BOM và Part Custom Properties.
            // QUAN TRỌNG: 数量 của COMPONENT lấy trực tiếp từ đúng dòng BOM
            // mà SOLIDWORKS đã list từ Assembly lớn. Không dùng Custom Property
            // 数量/Qty của 3D Part làm nguồn chuẩn số lượng.
            LogDebug(
                $"[PART QTY BOM] component=\"{componentName}\" drawing=\"{drawingData.Quantity}\" bom=\"{bomQuantity}\"");

            string bomMaterial = GetCellText(bomRow, 2);
            string bomThickness = GetCellText(bomRow, 3);
            string bomGoban = "";
            string bomW = "";
            string bomL = "";
            string bomJobNo = "";
            string bomTehaiNo = "";
            string bomSiteName = "";
            string bomProductName = "";

            Dictionary<string, ModelPropertyLinkAudit> propertyLinkAudits =
                new Dictionary<string, ModelPropertyLinkAudit>(StringComparer.OrdinalIgnoreCase);

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
                ref bomProductName,
                propertyLinkAudits);

            string expectedDxf = "";
            string tehaiToUse = !string.IsNullOrWhiteSpace(bomTehaiNo)
                ? bomTehaiNo
                : drawingData.TehaiNo;

            if (!string.IsNullOrWhiteSpace(tehaiToUse) && !string.IsNullOrWhiteSpace(buhinNoBom))
                expectedDxf = $"{tehaiToUse} / {buhinNoBom}";
            else if (!string.IsNullOrWhiteSpace(buhinNoBom))
                expectedDxf = buhinNoBom;

            // 7. So sánh 12 trường Part hiện tại
            itemResult.Fields.Add(ComparePartNumber(drawingData.PartNumber, buhinNoBom, drawingData.PartNumberSource));
            itemResult.Fields.Add(ApplyRequiredPropertyLinkAudit(
                CompareNumericField("W", drawingData.Width, bomW, drawingData.WidthSource),
                GetPropertyLinkAudit(propertyLinkAudits, "W")));
            itemResult.Fields.Add(ApplyRequiredPropertyLinkAudit(
                CompareNumericField("L", drawingData.Length, bomL, drawingData.LengthSource),
                GetPropertyLinkAudit(propertyLinkAudits, "L")));
            itemResult.Fields.Add(CompareQuantityField(
                drawingData.Quantity,
                bomQuantity,
                "BOM_QUANTITY"));
            itemResult.Fields.Add(ApplyStrictPropertyLinkAudit(
                CompareMaterialField(drawingData.Material, bomMaterial),
                GetPropertyLinkAudit(propertyLinkAudits, "材質")));
            itemResult.Fields.Add(ApplyRequiredPropertyLinkAudit(
                CompareThicknessField(drawingData.Thickness, bomThickness),
                GetPropertyLinkAudit(propertyLinkAudits, "板厚")));
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
            // CỤM 2: BẢNG 合番 CỦA DRAWING CHI TIẾT
            // =========================================================================
            // QUAN TRỌNG: parser này CHỈ dùng để đọc 合番.
            // Không được ghi đè data.Quantity từ bảng 合番, kể cả khi trong bảng
            // có số như G-5B:1, M-5B:1 hoặc BR-02:35枚.
            // 数量 của COMPONENT phải lấy đúng ô 数量 trong title block ở CỤM 1,
            // sau đó so với 数量 của đúng dòng Component trong SolidWorks BOM.
            ApplyPartGobanQuantityTableData(data, tables);

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
                data.DxfFileName = CombineDxfFragments(dxfJobNo, dxfPartNo);
            }
            else if (!string.IsNullOrWhiteSpace(dxfJobNo) && !string.IsNullOrWhiteSpace(data.PartNumber))
            {
                data.DxfFileName = CombineDxfFragments(
                    dxfJobNo, "/" + data.PartNumber.Trim());
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

            // Không fallback từ 3D để lấp Drawing Value còn thiếu.
            // Drawing Value phải thật sự đến từ Note/Table nằm trong khung bản vẽ.
            // Nếu không tìm thấy trong khung, field giữ trống để checker báo Warning/NG đúng bản chất.

            return data;
        }

        private sealed class PartGobanQuantityEntry
        {
            public string Goban { get; set; } = "";
            public string Quantity { get; set; } = "";
            // Text đúng như Drawing hiển thị. Với TYPE B, ví dụ "P2 : 100"
            // phải được giữ nguyên thay vì chỉ xuất phần mã "P2".
            public string DisplayText { get; set; } = "";
            public TableDiagnosticInfo Table { get; set; }
            public TableCellDiagnosticInfo GobanCell { get; set; }
            public TableCellDiagnosticInfo QuantityCell { get; set; }
        }

        private void ApplyPartGobanQuantityTableData(
            DrawingDisplayedData data,
            List<TableDiagnosticInfo> tables)
        {
            if (data == null || tables == null || tables.Count == 0)
                return;

            List<PartGobanQuantityEntry> entries =
                ReadPartGobanQuantityEntries(tables);

            if (entries.Count == 0)
                return;

            // Hợp nhất 合番 theo đúng thứ tự xuất hiện, KHÔNG làm mất 数量 đi kèm.
            // Ví dụ:
            //   TYPE A: G-5B | 1 ; M-5B | 1  ->  "G-5B : 1 / M-5B : 1"
            //   TYPE B: 合番 | P2 : 100         ->  "P2 : 100"
            // 数量 ở đây chỉ là phần của chuỗi 合番; 数量 chính của COMPONENT vẫn
            // được so sánh riêng Drawing ↔ 3D Custom Property.
            List<string> gobanDisplays = new List<string>();
            HashSet<string> seenGobanPairs =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            double quantityTotal = 0.0;
            bool hasNumericQuantity = false;
            bool allQuantitiesNumeric = true;

            foreach (PartGobanQuantityEntry entry in entries)
            {
                if (entry == null)
                    continue;

                string goban = CleanTableCellText(entry.Goban);
                string qtyText = CleanTableCellText(entry.Quantity);

                if (!string.IsNullOrWhiteSpace(goban))
                {
                    string pairKey =
                        NormalizeGobanToken(goban) + "|" + NormalizeQuantityToken(qtyText);

                    if (seenGobanPairs.Add(pairKey))
                    {
                        string display = CleanTableCellText(entry.DisplayText);
                        if (string.IsNullOrWhiteSpace(display))
                        {
                            display = string.IsNullOrWhiteSpace(qtyText)
                                ? goban
                                : goban + " : " + qtyText.Replace("枚", "").Trim();
                        }

                        gobanDisplays.Add(display);
                    }
                }

                double qty;
                if (TryParseQuantityNumber(qtyText, out qty))
                {
                    quantityTotal += qty;
                    hasNumericQuantity = true;
                }
                else if (!string.IsNullOrWhiteSpace(qtyText))
                {
                    allQuantitiesNumeric = false;
                }

                LogDebug(
                    $"[PART GOBAN TABLE] table={entry.Table?.Index ?? -1} " +
                    $"display=\"{entry.DisplayText}\" goban=\"{entry.Goban}\" qty=\"{entry.Quantity}\" " +
                    $"gobanRow={entry.GobanCell?.Row ?? -1} gobanCol={entry.GobanCell?.Column ?? -1} " +
                    $"qtyRow={entry.QuantityCell?.Row ?? -1} qtyCol={entry.QuantityCell?.Column ?? -1}");
            }

            if (gobanDisplays.Count > 0)
            {
                data.Goban = string.Join(" / ", gobanDisplays);
                LogDebug($"[PART GOBAN DISPLAY] drawing=\"{data.Goban}\"");

                PartGobanQuantityEntry firstGobanEntry =
                    entries.Find(e =>
                        e != null &&
                        e.GobanCell != null &&
                        !string.IsNullOrWhiteSpace(e.Goban));

                if (firstGobanEntry != null)
                {
                    SaveDrawingTableValueLocation(
                        data,
                        "合番",
                        firstGobanEntry.Table,
                        firstGobanEntry.GobanCell);
                }
            }

            // 数量 ở bảng 合番 chỉ là thông tin phụ của 合番, KHÔNG phải nguồn
            // so sánh 数量 của COMPONENT. Giữ lại debug để chẩn đoán nhưng không
            // ghi đè data.Quantity/data.QuantitySource/data.ValueLocations["数量"].
            if (hasNumericQuantity && allQuantitiesNumeric)
            {
                LogDebug(
                    $"[PART GOBAN TABLE INFO] goban=\"{data.Goban}\" " +
                    $"parsedQtyTotal=\"{FormatQuantityNumber(quantityTotal)}\" " +
                    $"drawingTitleQty=\"{data.Quantity}\" entries={entries.Count}");
            }
        }

        private static List<PartGobanQuantityEntry> ReadPartGobanQuantityEntries(
            List<TableDiagnosticInfo> tables)
        {
            List<PartGobanQuantityEntry> result =
                new List<PartGobanQuantityEntry>();

            if (tables == null)
                return result;

            HashSet<string> dedupe =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (TableDiagnosticInfo table in tables)
            {
                if (table == null || table.Cells == null || table.Cells.Count == 0)
                    continue;

                int entryCountBeforeTable = result.Count;
                bool foundTypeAInTable = false;

                // -------------------------------------------------------------
                // TYPE A
                //   合番 | 数量
                //   G-5B | 1
                //   M-5B | 1
                // Header có thể nằm ở bất kỳ row/column nào.
                // -------------------------------------------------------------
                for (int headerRow = 0; headerRow < table.RowCount; headerRow++)
                {
                    int gobanCol = FindHeaderColumnInRow(table, headerRow, "合番");
                    int qtyCol = FindHeaderColumnInRow(table, headerRow, "数量");

                    if (gobanCol < 0 || qtyCol < 0 || gobanCol == qtyCol)
                        continue;

                    bool foundData = false;

                    for (int r = headerRow + 1; r < table.RowCount; r++)
                    {
                        TableCellDiagnosticInfo gobanCell =
                            FindTableCell(table, r, gobanCol);
                        TableCellDiagnosticInfo qtyCell =
                            FindTableCell(table, r, qtyCol);

                        string goban =
                            CleanTableCellText(gobanCell?.DisplayedText ?? "");
                        string qty =
                            CleanTableCellText(qtyCell?.DisplayedText ?? "");

                        // Nếu đã vào vùng data mà gặp một row trống hoàn toàn,
                        // coi như block 合番 đã kết thúc để không ăn sang block khác.
                        if (string.IsNullOrWhiteSpace(goban) &&
                            string.IsNullOrWhiteSpace(qty))
                        {
                            if (foundData)
                                break;

                            continue;
                        }

                        // Bỏ qua row header lặp lại hoặc row không có 合番.
                        if (string.IsNullOrWhiteSpace(goban) ||
                            HeaderMatchesLogicalName(goban, "合番"))
                        {
                            continue;
                        }

                        AddPartGobanQuantityEntry(
                            result,
                            dedupe,
                            new PartGobanQuantityEntry
                            {
                                Goban = goban,
                                Quantity = qty,
                                DisplayText = string.IsNullOrWhiteSpace(qty)
                                    ? goban
                                    : goban + " : " + qty.Replace("枚", "").Trim(),
                                Table = table,
                                GobanCell = gobanCell,
                                QuantityCell = qtyCell
                            });

                        foundData = true;
                        foundTypeAInTable = true;
                    }

                    if (foundTypeAInTable)
                        break;
                }

                if (foundTypeAInTable)
                    continue;

                // -------------------------------------------------------------
                // TYPE B/C
                //   合番 | BR-02:35枚      (ngang, template 4185)
                //   合番                  (dọc, template 4120)
                //   M-4A:1
                // Có template đặt cả nhãn và giá trị trong cùng một cell.
                // -------------------------------------------------------------
                foreach (TableCellDiagnosticInfo headerCell in table.Cells)
                {
                    if (headerCell == null ||
                        !HeaderMatchesLogicalName(
                            CleanTableCellText(headerCell.DisplayedText),
                            "合番"))
                    {
                        continue;
                    }

                    string headerText =
                        CleanTableCellText(headerCell.DisplayedText);
                    string inlineValue = Regex.Replace(
                        headerText,
                        @"^.*?合番\s*[:：]?\s*",
                        "");

                    if (TryAddCombinedGobanEntries(
                            result, dedupe, table, headerCell, inlineValue))
                    {
                        continue;
                    }

                    bool foundValue = false;
                    for (int c = headerCell.Column + 1; c < table.ColumnCount; c++)
                    {
                        TableCellDiagnosticInfo valueCell =
                            FindTableCell(table, headerCell.Row, c);

                        string combined =
                            CleanTableCellText(valueCell?.DisplayedText ?? "");

                        if (!TryAddCombinedGobanEntries(
                                result, dedupe, table, valueCell, combined))
                        {
                            continue;
                        }

                        foundValue = true;
                        break;
                    }

                    if (foundValue)
                        continue;

                    // Bảng 合番 của template 4120 xếp nhãn và giá trị
                    // thành 2 hàng trong cùng một cột.
                    for (int r = headerCell.Row + 1; r < table.RowCount; r++)
                    {
                        TableCellDiagnosticInfo valueCell =
                            FindTableCell(table, r, headerCell.Column);
                        string combined =
                            CleanTableCellText(valueCell?.DisplayedText ?? "");

                        if (HeaderMatchesLogicalName(combined, "合番"))
                            break;

                        if (!TryAddCombinedGobanEntries(
                                result, dedupe, table, valueCell, combined))
                            continue;
                        break;
                    }
                }

                if (result.Count == entryCountBeforeTable &&
                    table.RowCount == 2 && table.ColumnCount == 1)
                {
                    TableCellDiagnosticInfo first = FindTableCell(table, 0, 0);
                    TableCellDiagnosticInfo second = FindTableCell(table, 1, 0);
                    LogDebug(
                        $"[PART GOBAN TABLE MISS] table={table.Index} " +
                        $"cell0=\"{first?.DisplayedText}\" " +
                        $"cell1=\"{second?.DisplayedText}\" " +
                        $"raw0=\"{first?.RawText}\" " +
                        $"raw1=\"{second?.RawText}\"");
                }
            }

            return result;
        }

        private static void AddPartGobanQuantityEntry(
            List<PartGobanQuantityEntry> result,
            HashSet<string> dedupe,
            PartGobanQuantityEntry entry)
        {
            if (result == null || dedupe == null || entry == null)
                return;

            string goban = CleanTableCellText(entry.Goban);
            string qty = CleanTableCellText(entry.Quantity);

            if (string.IsNullOrWhiteSpace(goban))
                return;

            string key =
                NormalizeGobanToken(goban) + "|" +
                NormalizeText(qty);

            if (!dedupe.Add(key))
                return;

            entry.Goban = goban;
            entry.Quantity = qty;
            entry.DisplayText = CleanTableCellText(entry.DisplayText);
            result.Add(entry);
        }

        private static bool TryAddCombinedGobanEntries(
            List<PartGobanQuantityEntry> result,
            HashSet<string> dedupe,
            TableDiagnosticInfo table,
            TableCellDiagnosticInfo cell,
            string text)
        {
            string value = CleanTableCellText(text);
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // A single cell can contain several pairs separated by spaces,
            // line breaks or slashes. Validate the whole cell before adding any.
            MatchCollection matches = Regex.Matches(
                value,
                @"\G[\s/／,，、;；]*(?<pair>(?<goban>[^:：/／,，、;；]+?)\s*[:：]\s*(?<qty>\d+(?:[\.,]\d+)?)\s*枚?)(?=$|[\s/／,，、;；])",
                RegexOptions.IgnoreCase);

            if (matches.Count == 0)
                return false;

            Match last = matches[matches.Count - 1];
            if (!string.IsNullOrWhiteSpace(value.Substring(last.Index + last.Length)))
                return false;

            foreach (Match match in matches)
            {
                if (string.IsNullOrWhiteSpace(match.Groups["goban"].Value))
                    return false;
            }

            foreach (Match match in matches)
            {
                AddPartGobanQuantityEntry(result, dedupe,
                    new PartGobanQuantityEntry
                    {
                        Goban = match.Groups["goban"].Value.Trim(),
                        Quantity = match.Groups["qty"].Value.Trim(),
                        DisplayText = match.Groups["pair"].Value.Trim(),
                        Table = table,
                        GobanCell = cell,
                        QuantityCell = cell
                    });
            }
            return true;
        }

        private static bool TryParseQuantityNumber(
            string text,
            out double value)
        {
            value = 0.0;

            string cleaned =
                CleanTableCellText(text)
                .Replace("枚", "")
                .Replace(",", ".")
                .Trim();

            return double.TryParse(
                cleaned,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static string FormatQuantityNumber(double value)
        {
            double rounded = Math.Round(value);
            if (Math.Abs(value - rounded) <= 0.0000001)
            {
                return ((long)rounded).ToString(CultureInfo.InvariantCulture);
            }

            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string NormalizeGobanToken(string text)
        {
            return NormalizeText(text ?? "")
                .Replace(" ", "")
                .Replace("　", "")
                .Replace("：", ":")
                .Trim();
        }

        private static string NormalizeQuantityToken(string text)
        {
            string cleaned = CleanTableCellText(text ?? "")
                .Replace("枚", "")
                .Replace("，", ".")
                .Replace(",", ".")
                .Trim();

            double numeric;
            if (double.TryParse(
                    cleaned,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out numeric))
            {
                return FormatQuantityNumber(numeric);
            }

            return NormalizeText(cleaned);
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

        private static BomNumberValues GetBomNumberValues(DataGridViewRow row)
        {
            return row != null && row.Cells.Count > 1
                ? row.Cells[1].Tag as BomNumberValues
                : null;
        }

        internal static DrawingBomFieldResult CompareBomNumbers(BomNumberValues values)
        {
            string itemNo = values?.ItemNumber ?? "";
            string partNo = values?.PartNumber ?? "";
            bool missing = string.IsNullOrWhiteSpace(itemNo) || string.IsNullOrWhiteSpace(partNo);
            bool equal = string.Equals(NormalizeText(itemNo), NormalizeText(partNo), StringComparison.OrdinalIgnoreCase);
            return new DrawingBomFieldResult
            {
                FieldName = "INo ↔ 部品番号 (BOM)",
                DrawingValue = itemNo,
                BomValue = partNo,
                Source = "BOM: INo / 部品番号 cùng dòng",
                Status = missing ? DrawingBomCheckStatus.Warning
                    : equal ? DrawingBomCheckStatus.OK : DrawingBomCheckStatus.NG,
                Message = missing ? "Không đọc được INo hoặc 部品番号 từ dòng BOM."
                    : equal ? "" : $"Khác biệt trên cùng dòng BOM: INo='{itemNo}' != 部品番号='{partNo}'"
            };
        }

        internal static DrawingBomFieldResult CompareBomGobanTotal(BomNumberValues values)
        {
            string goban = (values?.Goban ?? "").Normalize(System.Text.NormalizationForm.FormKC).Trim();
            string quantity = (values?.Quantity ?? "").Normalize(System.Text.NormalizationForm.FormKC).Replace("枚", "").Trim();
            var field = new DrawingBomFieldResult
            {
                FieldName = "合番合計 ↔ 数量 (BOM)",
                BomValue = values?.Quantity ?? "",
                Source = "BOM: tổng 合番 / 数量 cùng dòng",
                Status = DrawingBomCheckStatus.Warning,
                Message = "Không đọc đầy đủ được số lượng của từng 合番 hoặc 数量 trong dòng BOM."
            };
            decimal expected;
            if (string.IsNullOrWhiteSpace(goban) ||
                !decimal.TryParse(quantity.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out expected))
                return field;

            // Consume the complete cell, including multiline and slash-separated
            // entries. Repeated 合番 entries contribute separately to the total.
            MatchCollection entries = Regex.Matches(goban,
                @"\G[\s/,、;]*(?<goban>[^\s:/,、;]+)(?:\s*:\s*|\s+)(?<qty>[0-9]+(?:[.,][0-9]+)?)\s*枚?(?=$|[\s/,、;])");
            if (entries.Count == 0)
                return field;
            Match last = entries[entries.Count - 1];
            if (!Regex.IsMatch(goban.Substring(last.Index + last.Length), @"^[\s/,、;]*$"))
                return field;

            decimal total = 0;
            var terms = new List<string>();
            foreach (Match entry in entries)
            {
                decimal amount;
                if (!decimal.TryParse(entry.Groups["qty"].Value.Replace(',', '.'),
                    NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
                    return field;
                try { total += amount; }
                catch (OverflowException) { return field; }
                terms.Add(entry.Groups["goban"].Value + ": " + amount.ToString(CultureInfo.InvariantCulture));
            }
            field.DrawingValue = total.ToString(CultureInfo.InvariantCulture);
            field.Status = total == expected ? DrawingBomCheckStatus.OK : DrawingBomCheckStatus.NG;
            field.Message = string.Join(" + ", terms) + " = " + field.DrawingValue
                + "; 数量 BOM = " + quantity
                + (total == expected ? " — khớp." : " — không khớp.");
            return field;
        }

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

        private static ModelPropertyLinkAudit GetPropertyLinkAudit(
            Dictionary<string, ModelPropertyLinkAudit> audits,
            string propertyName)
        {
            ModelPropertyLinkAudit audit;
            return audits != null &&
                   audits.TryGetValue(propertyName, out audit)
                ? audit
                : null;
        }

        private static DrawingBomFieldResult ApplyRequiredPropertyLinkAudit(
            DrawingBomFieldResult field,
            ModelPropertyLinkAudit audit)
        {
            if (field == null || audit == null || !audit.Exists || audit.IsLinked)
                return field;

            string linkMessage =
                "Custom Property '" + audit.PropertyName +
                "' đang là giá trị nhập tay, không có liên kết. Cần kiểm tra trong file chi tiết.";

            if (field.Status == DrawingBomCheckStatus.OK)
                field.Status = DrawingBomCheckStatus.Warning;

            field.Source = "MODEL_PROPERTY_MANUAL";
            field.Message = string.IsNullOrWhiteSpace(field.Message)
                ? linkMessage
                : field.Message + " | " + linkMessage;

            LogDebug(
                "[PROPERTY LINK WARNING] property=\"" + audit.PropertyName +
                "\" scope=\"" + audit.Scope +
                "\" raw=\"" + audit.RawExpression + "\"");

            return field;
        }

        private static DrawingBomFieldResult ApplyStrictPropertyLinkAudit(
            DrawingBomFieldResult field,
            ModelPropertyLinkAudit audit)
        {
            if (field == null || audit == null || !audit.Exists || audit.IsLinked)
                return field;

            string linkMessage =
                "Custom Property '" + audit.PropertyName +
                "' bắt buộc phải LINK nhưng hiện đang là giá trị nhập tay / đã mất link.";

            field.Status = DrawingBomCheckStatus.NG;
            field.Source = "MODEL_PROPERTY_LINK_REQUIRED";
            field.Message = string.IsNullOrWhiteSpace(field.Message)
                ? linkMessage
                : field.Message + " | " + linkMessage;

            LogDebug(
                "[PROPERTY LINK NG] property=\"" + audit.PropertyName +
                "\" scope=\"" + audit.Scope +
                "\" raw=\"" + audit.RawExpression + "\"");

            return field;
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
                        Status = DrawingBomCheckStatus.OK,
                        Message = (fieldName == "W" || fieldName == "L")
                            ? "Giá trị 2D và 3D giống nhau."
                            : ""
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
                    Status = DrawingBomCheckStatus.OK,
                    Message = (fieldName == "W" || fieldName == "L")
                        ? "Giá trị 2D và 3D giống nhau."
                        : ""
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
                    Message = "Không có 数量 trong dòng tương ứng của SolidWorks BOM."
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
                            Message = $"Số lượng lẻ không khớp: Drawing={dValDouble} != BOM={bValDouble}"
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

            double dVal, bVal;
            if (TryParseThicknessMm(drawVal, out dVal) &&
                TryParseThicknessMm(bomVal, out bVal))
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

        private static bool TryParseThicknessMm(string text, out double value)
        {
            value = 0.0;
            string compact = Regex.Replace(text ?? "", @"\s+", "");
            Match match = Regex.Match(
                compact,
                @"^(?<value>\d+(?:[.,]\d+)?)(?:t|mm)?$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                // A material code may precede the final thickness token.
                // Reject text with more than one thickness token.
                match = Regex.Match(
                    compact,
                    @"^.+[-－−](?<value>\d+(?:[.,]\d+)?)t$",
                    RegexOptions.IgnoreCase);
                if (!match.Success ||
                    Regex.Matches(compact, @"\d+(?:[.,]\d+)?t", RegexOptions.IgnoreCase).Count != 1)
                    return false;
            }

            return double.TryParse(
                match.Groups["value"].Value.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
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

            // 1. So khớp trực tiếp / normalized text.
            if (string.Equals(nDraw, nBom, StringComparison.OrdinalIgnoreCase))
                return CreateGobanOk(drawVal, bomVal);

            string sDraw = NormalizeGobanToken(nDraw);
            string sBom = NormalizeGobanToken(nBom);
            if (string.Equals(sDraw, sBom, StringComparison.OrdinalIgnoreCase))
                return CreateGobanOk(drawVal, bomVal);

            // 2. Nếu 合番 có kèm 数量 (P2:100, G-5B:1 / M-5B:1...),
            //    bắt buộc so sánh CẢ mã + số lượng. Không được bỏ suffix 数量.
            Dictionary<string, string> drawPairs = ParseGobanQuantityPairs(drawVal);
            Dictionary<string, string> bomPairs = ParseGobanQuantityPairs(bomVal);

            if (drawPairs.Count > 0 || bomPairs.Count > 0)
            {
                if (drawPairs.Count == 0 || bomPairs.Count == 0)
                {
                    return new DrawingBomFieldResult
                    {
                        FieldName = "合番",
                        DrawingValue = drawVal,
                        BomValue = bomVal,
                        Status = DrawingBomCheckStatus.NG,
                        Message = $"合番 không khớp đầy đủ mã + 数量: Drawing='{drawVal}' != BOM='{bomVal}'"
                    };
                }

                if (GobanQuantityPairsEqual(drawPairs, bomPairs))
                {
                    return new DrawingBomFieldResult
                    {
                        FieldName = "合番",
                        DrawingValue = drawVal,
                        BomValue = bomVal,
                        Status = DrawingBomCheckStatus.OK,
                        Message = "合番 và số lượng đi kèm giống nhau."
                    };
                }

                return new DrawingBomFieldResult
                {
                    FieldName = "合番",
                    DrawingValue = drawVal,
                    BomValue = bomVal,
                    Status = DrawingBomCheckStatus.NG,
                    Message = $"合番 hoặc số lượng đi kèm không khớp: Drawing='{drawVal}' != BOM='{bomVal}'"
                };
            }

            // 3. Trường hợp chỉ có mã 合番, có thể nhiều mã, so sánh theo tập mã.
            HashSet<string> drawSet = ParseGobanSet(drawVal);
            HashSet<string> bomSet = ParseGobanSet(bomVal);

            if (drawSet.Count > 0 && drawSet.SetEquals(bomSet))
                return CreateGobanOk(drawVal, bomVal);

            // 4. Compatibility cũ chỉ áp dụng khi CẢ HAI phía KHÔNG chứa dấu ':' quantity.
            //    Tránh lỗi P2 được coi bằng P2:100.
            if (!ContainsGobanQuantitySyntax(drawVal) &&
                !ContainsGobanQuantitySyntax(bomVal))
            {
                string unitDraw = ExtractGobanUnit(sDraw);
                string unitBom = ExtractGobanUnit(sBom);
                if (!string.IsNullOrEmpty(unitDraw) &&
                    !string.IsNullOrEmpty(unitBom) &&
                    string.Equals(unitDraw, unitBom, StringComparison.OrdinalIgnoreCase))
                {
                    return CreateGobanOk(drawVal, bomVal);
                }
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

        private static DrawingBomFieldResult CreateGobanOk(string drawVal, string bomVal)
        {
            return new DrawingBomFieldResult
            {
                FieldName = "合番",
                DrawingValue = drawVal,
                BomValue = bomVal,
                Status = DrawingBomCheckStatus.OK,
                Message = "Giá trị 合番 giống nhau."
            };
        }

        private static Dictionary<string, string> ParseGobanQuantityPairs(string text)
        {
            Dictionary<string, string> result =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string value = NormalizeText(text ?? "");
            if (string.IsNullOrWhiteSpace(value))
                return result;

            MatchCollection matches = Regex.Matches(
                value,
                @"(?<goban>[^:：/／,，、;；\r\n]+?)\s*[:：]\s*(?<qty>\d+(?:[\.,]\d+)?)\s*枚?",
                RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                if (match == null || !match.Success)
                    continue;

                string goban = NormalizeGobanToken(match.Groups["goban"].Value);
                string qty = NormalizeQuantityToken(match.Groups["qty"].Value);

                // Khi các pair nối nhau chỉ bằng khoảng trắng (vd "I.J-5B:2 K-5B:1"),
                // regex có thể nhận phần đầu của match tiếp theo với leading spaces; Trim ở trên xử lý.
                if (string.IsNullOrWhiteSpace(goban) || string.IsNullOrWhiteSpace(qty))
                    continue;

                result[goban] = qty;
            }

            return result;
        }

        private static bool GobanQuantityPairsEqual(
            Dictionary<string, string> left,
            Dictionary<string, string> right)
        {
            if (left == null || right == null || left.Count != right.Count)
                return false;

            foreach (KeyValuePair<string, string> pair in left)
            {
                string rightQty;
                if (!right.TryGetValue(pair.Key, out rightQty))
                    return false;

                if (!string.Equals(
                        NormalizeQuantityToken(pair.Value),
                        NormalizeQuantityToken(rightQty),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsGobanQuantitySyntax(string text)
        {
            return Regex.IsMatch(
                NormalizeText(text ?? ""),
                @"[:：]\s*\d+(?:[\.,]\d+)?\s*枚?",
                RegexOptions.IgnoreCase);
        }

        private static HashSet<string> ParseGobanSet(string text)
        {
            HashSet<string> result =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string value = NormalizeText(text ?? "");
            if (string.IsNullOrWhiteSpace(value))
                return result;

            string[] parts = Regex.Split(
                value,
                @"\s*(?:,|，|、|;|；|/|／|\r?\n)\s*");

            foreach (string part in parts)
            {
                string token = NormalizeGobanToken(part);
                if (!string.IsNullOrWhiteSpace(token))
                    result.Add(token);
            }

            return result;
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
            string nDraw = NormalizeDxfName(drawVal);
            string nBom = NormalizeDxfName(bomVal);

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

        private static string NormalizeDxfName(string text)
        {
            string compact = Regex.Replace(NormalizeText(text), @"\s+", "")
                .Replace('／', '/');
            return Regex.Replace(compact, @"(?<=\d)[-－−](?=\d)", "/");
        }

        private static string CombineDxfFragments(string jobNo, string partNo)
        {
            string normalizedJobNo = NormalizeDxfName(jobNo);
            string normalizedPartNo = NormalizeDxfName(partNo);
            return normalizedPartNo.StartsWith("/", StringComparison.Ordinal) &&
                normalizedJobNo.EndsWith(
                    normalizedPartNo, StringComparison.OrdinalIgnoreCase)
                ? (jobNo ?? "").Trim()
                : $"{jobNo} {partNo}".Trim();
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
            string fileNameBom,
            string bomQuantity)
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
                    configurationName,
                    bomQuantity);

                ApplyAssemblyMassLinkAudit(
                    itemResult,
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

            // Compare bằng canonical label để các cách ghi tương đương
            // (ví dụ "Sub-ユニット図" và "サブユニット") đều được coi là OK.
            string expectedCanonical =
                ExtractDrawingTypeLabel(
                    expectedLabel);

            if (string.IsNullOrWhiteSpace(expectedCanonical))
            {
                expectedCanonical =
                    NormalizeText(
                        expectedLabel);
            }

            string actualCanonical =
                ExtractDrawingTypeLabel(
                    actualLabel);

            if (string.IsNullOrWhiteSpace(actualCanonical))
            {
                actualCanonical =
                    NormalizeText(
                        actualLabel);
            }

            bool ok =
                !string.IsNullOrWhiteSpace(actualCanonical) &&
                string.Equals(
                    actualCanonical,
                    expectedCanonical,
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

            // ---------------------------------------------------------
            // Drawing type -03-:
            // "Sub-ユニット図", "Sub-ユニット" và "サブユニット"
            // được coi là CÙNG một loại Drawing.
            // Canonical value dùng để compare = "サブユニット".
            // ---------------------------------------------------------
            if (compact.IndexOf(
                    "サブユニット",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "サブユニット";
            }

            bool containsEnglishSub =
                compact.IndexOf(
                    "Sub",
                    StringComparison.OrdinalIgnoreCase) >= 0;

            bool containsUnit =
                compact.IndexOf(
                    "ユニット",
                    StringComparison.OrdinalIgnoreCase) >= 0;

            if (containsEnglishSub && containsUnit)
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
            string configurationName,
            string bomQuantity)
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

                // 数量/Qty của UNIT không dùng Custom Property của 3D Assembly
                // làm Expected. Nguồn chuẩn là 数量 của đúng dòng UNIT trong
                // SolidWorks BOM. Bỏ qua linked note quantity ở vòng này để
                // summary-table check bên dưới xử lý bằng BOM_QUANTITY.
                if (IsBomQuantityPropertyName(propertyName))
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
                checkedLogicalNames,
                bomQuantity);

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
            HashSet<string> checkedLogicalNames,
            string bomQuantity)
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
            {
                int missing = 0;
                foreach (string name in new[] { "合番", "重量", "数量" })
                {
                    if (checkedLogicalNames != null && !checkedLogicalNames.Add(name))
                        continue;
                    itemResult.Fields.Add(new DrawingBomFieldResult
                    {
                        FieldName = "ASM:" + name,
                        DrawingValue = "(Không đọc được bảng UNIT)",
                        BomValue = name == "数量" ? (bomQuantity ?? "")
                            : GetModelCustomProperty(assemblyModel, configurationName, name),
                        Source = "ASSEMBLY_TABLE_MISSING",
                        Status = DrawingBomCheckStatus.Warning,
                        Message = "Không nhận diện được bảng tổng hợp UNIT; chưa thể xác nhận giá trị trên Drawing."
                    });
                    missing++;
                }
                return missing;
            }

            int added = 0;

            added += AddAssemblySummaryTableField(
                itemResult,
                drawingData,
                summaryTable,
                assemblyModel,
                configurationName,
                checkedLogicalNames,
                bomQuantity,
                "合番");

            added += AddAssemblySummaryTableField(
                itemResult,
                drawingData,
                summaryTable,
                assemblyModel,
                configurationName,
                checkedLogicalNames,
                bomQuantity,
                "重量");

            added += AddAssemblySummaryTableField(
                itemResult,
                drawingData,
                summaryTable,
                assemblyModel,
                configurationName,
                checkedLogicalNames,
                bomQuantity,
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
            string bomQuantity,
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

            int headerRow = FindAssemblySummaryHeaderRow(table);
            if (headerRow < 0)
                return 0;

            int column =
                FindTableHeaderColumn(
                    table,
                    logicalName,
                    headerRow);

            if (column < 0)
                return 0;

            string expectedGoban =
                GetModelCustomProperty(
                    assemblyModel,
                    configurationName,
                    "合番");

            int dataRow =
                FindAssemblySummaryDataRow(
                    table,
                    headerRow,
                    expectedGoban);

            // Preserve the check even when all linked display cells are blank.
            // The summary structure has already identified the header and columns.
            if (dataRow < 0 && headerRow + 1 < table.RowCount)
                dataRow = headerRow + 1;
            if (dataRow < 0)
            {
                itemResult.Fields.Add(new DrawingBomFieldResult
                {
                    FieldName = "ASM:" + logicalName,
                    DrawingValue = "(Không có dòng dữ liệu UNIT)",
                    BomValue = logicalName == "数量" ? (bomQuantity ?? "")
                        : GetModelCustomProperty(assemblyModel, configurationName, logicalName),
                    Source = "ASSEMBLY_TABLE_ROW_MISSING",
                    Status = DrawingBomCheckStatus.Warning,
                    Message = "Bảng UNIT có tiêu đề nhưng không có dòng dữ liệu đọc được."
                });
                return 1;
            }

            TableCellDiagnosticInfo valueCell =
                FindTableCell(
                    table,
                    dataRow,
                    column);

            if (valueCell == null)
                return 0;

            string drawingValue =
                CleanTableCellText(
                    valueCell.DisplayedText);

            bool isQuantity =
                string.Equals(logicalName, "数量", StringComparison.OrdinalIgnoreCase);

            string assemblyValue =
                isQuantity
                    ? (bomQuantity ?? "").Trim()
                    : GetModelCustomProperty(
                        assemblyModel,
                        configurationName,
                        logicalName);

            // Required summary fields must never disappear from the report.
            if (string.IsNullOrWhiteSpace(drawingValue) || string.IsNullOrWhiteSpace(assemblyValue)
                || IsIncompleteLinkedTableDisplay(valueCell.RawText, drawingValue))
            {
                itemResult.Fields.Add(new DrawingBomFieldResult
                {
                    FieldName = "ASM:" + logicalName,
                    DrawingValue = string.IsNullOrWhiteSpace(drawingValue)
                        ? "(Không đọc được giá trị hiển thị)" : drawingValue,
                    BomValue = assemblyValue,
                    Source = "ASSEMBLY_TABLE_VALUE_UNAVAILABLE",
                    Status = DrawingBomCheckStatus.Warning,
                    Message = "Thiếu giá trị hiển thị của ô bảng hoặc giá trị đối chiếu. "
                        + "Raw='" + valueCell.RawText + "'; API='" + valueCell.ApiDisplayedText
                        + "'; API hidden='" + valueCell.ApiDisplayedTextWithHidden + "'."
                });
                LogDebug($"[ASM TABLE UNAVAILABLE] field={logicalName} table={table.Index} row={dataRow} col={column} source={valueCell.ValueSource} raw={valueCell.RawText}");
                return 1;
            }

            LogDebug(
                $"[ASM TABLE FIELD] field=\"{logicalName}\" " +
                $"table={table.Index} headerRow={headerRow} row={dataRow} col={column} " +
                $"drawing=\"{drawingValue}\" expected=\"{assemblyValue}\" " +
                $"expectedSource=\"{(isQuantity ? "BOM_QUANTITY" : "ASSEMBLY_PROPERTY")}\" " +
                $"source=\"{valueCell.ValueSource}\" " +
                $"apiDisplayed=\"{valueCell.ApiDisplayedText}\" " +
                $"apiDisplayedHidden=\"{valueCell.ApiDisplayedTextWithHidden}\" " +
                $"raw=\"{valueCell.RawText}\" " +
                $"X={valueCell.X:F6} Y={valueCell.Y:F6}");

            // X/Y chỉ lưu sau khi đã xác định đúng table/row/column bằng cấu trúc.
            SaveDrawingTableValueLocation(
                drawingData,
                logicalName,
                table,
                valueCell);

            if (isQuantity)
            {
                DrawingBomFieldResult quantityField =
                    CompareQuantityField(
                        drawingValue,
                        assemblyValue,
                        "BOM_QUANTITY");

                quantityField.FieldName = "ASM:数量";
                itemResult.Fields.Add(quantityField);

                LogDebug(
                    $"[ASM QTY BOM] drawing=\"{drawingValue}\" bom=\"{assemblyValue}\" status={quantityField.Status}");

                return 1;
            }

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
                if (table == null || table.RowCount < 2)
                    continue;

                if (FindAssemblySummaryHeaderRow(table) >= 0)
                    return table;
            }

            return null;
        }

        private static int FindAssemblySummaryHeaderRow(
            TableDiagnosticInfo table)
        {
            if (table == null)
                return -1;

            for (int r = 0; r < table.RowCount; r++)
            {
                int gobanCol = FindHeaderColumnInRow(table, r, "合番");
                int qtyCol = FindHeaderColumnInRow(table, r, "数量");
                int weightCol = FindHeaderColumnInRow(table, r, "重量");

                if (gobanCol >= 0 && qtyCol >= 0 && weightCol >= 0)
                    return r;
            }

            return -1;
        }

        private static int FindAssemblySummaryDataRow(
            TableDiagnosticInfo table,
            int headerRow,
            string expectedGoban)
        {
            if (table == null || headerRow < 0)
                return -1;

            int gobanCol = FindTableHeaderColumn(table, "合番", headerRow);
            int qtyCol = FindTableHeaderColumn(table, "数量", headerRow);

            if (gobanCol < 0 && qtyCol < 0)
                return -1;

            string normalizedExpected = NormalizeGobanToken(expectedGoban);
            int firstDataRow = -1;

            for (int r = headerRow + 1; r < table.RowCount; r++)
            {
                string goban =
                    gobanCol >= 0
                        ? CleanTableCellText(GetTableCellText(table, r, gobanCol))
                        : "";

                string qty =
                    qtyCol >= 0
                        ? CleanTableCellText(GetTableCellText(table, r, qtyCol))
                        : "";

                TableCellDiagnosticInfo gobanCell = FindTableCell(table, r, gobanCol);
                TableCellDiagnosticInfo qtyCell = FindTableCell(table, r, qtyCol);
                bool hasLinkedData = ContainsTablePropertyExpression(gobanCell?.RawText)
                    || ContainsTablePropertyExpression(qtyCell?.RawText);
                if (string.IsNullOrWhiteSpace(goban) &&
                    string.IsNullOrWhiteSpace(qty) && !hasLinkedData)
                {
                    if (firstDataRow >= 0)
                        break;

                    continue;
                }

                if (firstDataRow < 0)
                    firstDataRow = r;

                if (!string.IsNullOrWhiteSpace(normalizedExpected) &&
                    string.Equals(
                        NormalizeGobanToken(goban),
                        normalizedExpected,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return r;
                }
            }

            return firstDataRow;
        }

        private static int FindTableHeaderColumn(
            TableDiagnosticInfo table,
            string logicalName,
            int headerRow)
        {
            return FindHeaderColumnInRow(
                table,
                headerRow,
                logicalName);
        }

        private static int FindHeaderColumnInRow(
            TableDiagnosticInfo table,
            int row,
            string logicalName)
        {
            if (table == null ||
                row < 0 ||
                row >= table.RowCount ||
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
                            row,
                            c));

                if (HeaderMatchesLogicalName(header, logicalName))
                    return c;
            }

            return -1;
        }

        private static bool HeaderMatchesLogicalName(
            string header,
            string logicalName)
        {
            string h = NormalizeText(header ?? "")
                .Replace(" ", "")
                .Replace("　", "")
                .Trim();

            string target = NormalizeText(logicalName ?? "")
                .Replace(" ", "")
                .Replace("　", "")
                .Trim();

            if (string.IsNullOrWhiteSpace(h) ||
                string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            if (string.Equals(
                    h,
                    target,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Một số template ghi "重量/ユニット" hoặc thêm đơn vị vào header.
            if ((target == "重量" || target == "数量" || target == "合番") &&
                h.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
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
                double thicknessDrawing;
                double thicknessAssembly;
                if (TryParseThicknessMm(drawingValue, out thicknessDrawing) &&
                    TryParseThicknessMm(assemblyValue, out thicknessAssembly))
                    return Math.Abs(thicknessDrawing - thicknessAssembly) <= NumericTolerance;
            }

            if (string.Equals(logicalName, "重量", StringComparison.OrdinalIgnoreCase))
                return AreDisplayedMassValuesEquivalent(drawingValue, assemblyValue);

            string normDrawing = NormalizeText(drawingValue).TrimEnd('-').Trim();
            string normAssembly = NormalizeText(assemblyValue).TrimEnd('-').Trim();

            return string.Equals(
                normDrawing,
                normAssembly,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyAssemblyMassLinkAudit(
            DrawingCheckItemResult itemResult,
            ModelDoc2 assemblyModel,
            string configurationName)
        {
            if (itemResult == null || assemblyModel == null)
                return;

            ModelPropertyLinkAudit audit = ReadModelPropertyLinkAudit(
                assemblyModel,
                configurationName,
                "重量");

            if (audit == null || !audit.Exists || audit.IsLinked)
                return;

            DrawingBomFieldResult massField = null;
            foreach (DrawingBomFieldResult field in itemResult.Fields)
            {
                if (field != null &&
                    string.Equals(field.FieldName, "ASM:重量", StringComparison.OrdinalIgnoreCase))
                {
                    massField = field;
                    break;
                }
            }

            if (massField == null)
            {
                massField = new DrawingBomFieldResult
                {
                    FieldName = "ASM:重量",
                    DrawingValue = "(Không đọc được từ Drawing)",
                    BomValue = string.IsNullOrWhiteSpace(audit.ResolvedValue)
                        ? audit.RawExpression
                        : audit.ResolvedValue,
                    Status = DrawingBomCheckStatus.OK
                };
                itemResult.Fields.Add(massField);
            }

            ApplyRequiredPropertyLinkAudit(massField, audit);
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
            ref string productName,
            Dictionary<string, ModelPropertyLinkAudit> propertyLinkAudits)
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

                                CaptureRequiredPropertyLinkAudits(
                                    compModel,
                                    cfg,
                                    propertyLinkAudits);

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

        private static void CaptureRequiredPropertyLinkAudits(
            ModelDoc2 model,
            string configurationName,
            Dictionary<string, ModelPropertyLinkAudit> audits)
        {
            if (model == null || audits == null)
                return;

            string[] requiredProperties = { "W", "L", "材質", "板厚" };
            foreach (string propertyName in requiredProperties)
            {
                ModelPropertyLinkAudit audit = ReadModelPropertyLinkAudit(
                    model,
                    configurationName,
                    propertyName);

                ModelPropertyLinkAudit current;
                bool hasCurrent = audits.TryGetValue(propertyName, out current);
                if (audit != null && audit.Exists &&
                    (!hasCurrent || (current.IsLinked && !audit.IsLinked)))
                {
                    audits[propertyName] = audit;
                }
            }
        }

        private static ModelPropertyLinkAudit ReadModelPropertyLinkAudit(
            ModelDoc2 model,
            string configurationName,
            string propertyName)
        {
            ModelPropertyLinkAudit audit = ReadModelPropertyLinkAuditAtScope(
                model,
                configurationName ?? "",
                propertyName);

            if (audit != null && audit.Exists)
                return audit;

            if (!string.IsNullOrWhiteSpace(configurationName))
                return ReadModelPropertyLinkAuditAtScope(model, "", propertyName);

            return audit;
        }

        private static ModelPropertyLinkAudit ReadModelPropertyLinkAuditAtScope(
            ModelDoc2 model,
            string scope,
            string propertyName)
        {
            ModelPropertyLinkAudit audit = new ModelPropertyLinkAudit
            {
                PropertyName = propertyName,
                Scope = string.IsNullOrWhiteSpace(scope) ? "DOCUMENT" : "CONFIG:" + scope
            };

            if (model == null || string.IsNullOrWhiteSpace(propertyName))
                return audit;

            try
            {
                CustomPropertyManager manager =
                    model.Extension.get_CustomPropertyManager(scope ?? "");

                string raw;
                string resolved;
                bool wasResolved;
                bool linkedToParent;
                int getResult = manager.Get6(
                    propertyName,
                    false,
                    out raw,
                    out resolved,
                    out wasResolved,
                    out linkedToParent);

                audit.RawExpression = raw ?? "";
                audit.ResolvedValue = resolved ?? "";
                audit.Exists = getResult ==
                    (int)swCustomInfoGetResult_e.swCustomInfoGetResult_ResolvedValue;

                if (!audit.Exists)
                {
                    audit.Exists =
                        !string.IsNullOrWhiteSpace(audit.RawExpression) ||
                        !string.IsNullOrWhiteSpace(audit.ResolvedValue);
                }

                audit.IsLinked =
                    audit.Exists &&
                    (linkedToParent ||
                     LooksLikePropertyExpression(audit.RawExpression));
            }
            catch (Exception ex)
            {
                LogDebug(
                    "[PROPERTY LINK AUDIT ERROR] property=\"" + propertyName +
                    "\" scope=\"" + audit.Scope + "\" error=" + ex.Message);
            }

            return audit;
        }

        private static bool LooksLikePropertyExpression(string rawValue)
        {
            string raw = (rawValue ?? "").Trim();
            if (raw.Length == 0)
                return false;

            return raw.IndexOf("$PRP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   raw.IndexOf("$PRPSHEET", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   raw.IndexOf("$PRPMODEL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   raw.IndexOf("SW-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (raw.IndexOf('@') >= 0 && raw.IndexOf('"') >= 0);
        }

        /// <summary>
        /// Helper đọc số lượng từ Custom Property của 3D model.
        /// LƯU Ý: CHECK DRAWING không còn dùng helper này làm nguồn chuẩn 数量.
        /// Nguồn chuẩn của 数量 cho cả COMPONENT và UNIT là SolidWorks BOM row.
        /// Giữ helper để tương thích với các logic khác nếu cần.
        /// </summary>
        private static string GetModelQuantityProperty(ModelDoc2 model, string configurationName)
        {
            if (model == null)
                return "";

            string quantity =
                GetModelCustomProperty(
                    model,
                    configurationName,
                    "数量");

            if (string.IsNullOrWhiteSpace(quantity))
            {
                quantity =
                    GetModelCustomProperty(
                        model,
                        configurationName,
                        "Qty");
            }

            return (quantity ?? "").Trim();
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

        private void ActivateDrawingForDisplayRead(ModelDoc2 model, DrawingDoc drawing)
        {
            if (swApp == null || model == null || drawing == null)
                return;
            try
            {
                try { model.Visible = true; } catch { }
                int errors = 0;
                ModelDoc2 activated = swApp.ActivateDoc3(model.GetTitle(), false, 0, ref errors) as ModelDoc2;
                LogDebug($"[DRAWING ACTIVATE] title={model.GetTitle()} success={activated != null} errors={errors}");
                Sheet sheet = drawing.GetCurrentSheet() as Sheet;
                string current = sheet != null ? (sheet.GetName() ?? "").Trim() : "";
                if (string.IsNullOrWhiteSpace(current))
                {
                    Array names = drawing.GetSheetNames() as Array;
                    if (names != null && names.Length > 0)
                    {
                        string first = Convert.ToString(names.GetValue(0));
                        if (!string.IsNullOrWhiteSpace(first))
                        {
                            bool switched = drawing.ActivateSheet(first);
                            LogDebug($"[DRAWING SHEET ACTIVATE] sheet={first} success={switched}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"[DRAWING ACTIVATE WARNING] message={ex.Message}");
            }
        }

        private void RefreshDrawingBeforeCheck(
            ModelDoc2 drawingModel,
            DrawingDoc drawing,
            string sheetName)
        {
            if (drawingModel == null)
                return;

            try
            {
                // Bảo đảm đúng sheet đang được kiểm tra là active trước rebuild.
                if (drawing != null &&
                    !string.IsNullOrWhiteSpace(sheetName))
                {
                    try
                    {
                        drawing.ActivateSheet(sheetName);
                    }
                    catch
                    {
                    }
                }

                drawingModel.ForceRebuild3(false);

                try
                {
                    drawingModel.GraphicsRedraw2();
                }
                catch
                {
                }

                LogDebug(
                    $"[DRAWING REFRESH] sheet=\"{sheetName}\" " +
                    "ForceRebuild3=DONE");
            }
            catch (Exception ex)
            {
                // Không dừng batch chỉ vì rebuild thất bại.
                // Scanner vẫn tiếp tục đọc dữ liệu hiện có và log sẽ cho biết.
                LogDebug(
                    $"[DRAWING REFRESH WARNING] sheet=\"{sheetName}\" " +
                    $"message=\"{ex.Message}\"");
            }
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
                // A visible document window is required for drawing-side linked
                // text evaluation. Silent still suppresses opening prompts.
                swApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocDRAWING);
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
            string targetSheetName,
            SheetFrameBounds frameBounds)
        {
            List<NoteDiagnosticInfo> result =
                new List<NoteDiagnosticInfo>();

            if (drawing == null)
                return result;

            HashSet<string> seen =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            int noteCounter = 1;
            int outsideFrameSkipped = 0;

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

                                    if (!IsNoteInsideFrame(info, frameBounds))
                                    {
                                        outsideFrameSkipped++;
                                    }
                                    else if (TryAddUniqueNote(
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

                                if (!IsNoteInsideFrame(info, frameBounds))
                                {
                                    outsideFrameSkipped++;
                                }
                                else if (TryAddUniqueNote(
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
                $"count={result.Count} outsideFrameSkipped={outsideFrameSkipped}");

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
            bool hasPosition = false;
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
                        hasPosition = true;
                    }
                }
                catch { }
            }

            // Không dùng dynamic IsBomBalloon()/IsBalloon() ở đây.
            // Một số phiên bản SOLIDWORKS Interop không expose các member này
            // qua runtime binder và tạo hàng loạt RuntimeBinderException trong Output.
            // Type chỉ dùng cho diagnostic, không ảnh hưởng logic kiểm tra.
            string objType = "Note";

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
                HasPosition = hasPosition,
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
            ModelDoc2 drawingModel,
            SheetFrameBounds frameBounds)
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
                                        TableDiagnosticInfo info = ExtractTableInfo(
                                            table,
                                            viewName,
                                            targetSheetName,
                                            tableCounter,
                                            drawingModel,
                                            frameBounds);
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

        private static string ResolveLinkedTableProperty(
            ModelDoc2 drawingModel,
            string rawExpression,
            string propertyName)
        {
            if (drawingModel == null ||
                string.IsNullOrWhiteSpace(propertyName))
            {
                return "";
            }

            string raw = rawExpression ?? "";

            // $PRP:"..." thường là property của chính Drawing document.
            // Thử Drawing trước, rồi mới fallback sang model được Drawing tham chiếu.
            if (raw.IndexOf("$PRP:", StringComparison.OrdinalIgnoreCase) >= 0 &&
                raw.IndexOf("$PRPSHEET", StringComparison.OrdinalIgnoreCase) < 0 &&
                raw.IndexOf("$PRPMODEL", StringComparison.OrdinalIgnoreCase) < 0)
            {
                string drawingValue =
                    GetModelCustomProperty(
                        drawingModel,
                        "",
                        propertyName);

                if (!string.IsNullOrWhiteSpace(drawingValue))
                    return drawingValue;
            }

            DrawingDoc drawing = drawingModel as DrawingDoc;
            if (drawing != null)
            {
                string referencedValue =
                    ResolvePropertyFromDrawingViews(
                        drawing,
                        propertyName);

                if (!string.IsNullOrWhiteSpace(referencedValue))
                    return referencedValue;
            }

            return "";
        }

        internal static string SelectDrawingTableText(
            string displayed, string displayedWithHidden, string raw, out string source)
        {
            if (!string.IsNullOrWhiteSpace(displayed) && !ContainsTablePropertyExpression(displayed))
            {
                source = "DISPLAYED_TEXT2";
                return displayed;
            }
            if (!string.IsNullOrWhiteSpace(displayedWithHidden) && !ContainsTablePropertyExpression(displayedWithHidden))
            {
                source = "DISPLAYED_TEXT2_INCLUDE_HIDDEN";
                return displayedWithHidden;
            }
            if (!string.IsNullOrWhiteSpace(raw) && !ContainsTablePropertyExpression(raw)
                && !raw.TrimStart().StartsWith("$", StringComparison.OrdinalIgnoreCase))
            {
                source = "RAW_STATIC_FALLBACK";
                return raw;
            }
            // A missing/unresolved display must remain missing, so the comparison
            // warns instead of comparing a model property against itself.
            source = "DRAWING_DISPLAY_UNAVAILABLE";
            return "";
        }

        internal static bool IsIncompleteLinkedTableDisplay(string raw, string display)
        {
            if (!ContainsTablePropertyExpression(raw))
                return false;
            // The linked value can vanish while a fixed suffix such as (kg)
            // survives. This is missing display evidence, not a mass mismatch.
            string literal = Regex.Replace(raw ?? "",
                @"\$PRP(?:SHEET|MODEL|VIEW)?\s*:\s*""[^""]+""", "", RegexOptions.IgnoreCase);
            return string.Equals(CleanTableCellText(display), CleanTableCellText(literal), StringComparison.Ordinal);
        }

        internal static bool AreDisplayedMassValuesEquivalent(string display, string expected)
        {
            const string pattern = @"^\s*(?<number>[+-]?\d+(?:[.,]\d+)?)\s*(?:\(\s*kg\s*\)|kg)?\s*$";
            Match d = Regex.Match(display ?? "", pattern, RegexOptions.IgnoreCase);
            Match e = Regex.Match(expected ?? "", pattern, RegexOptions.IgnoreCase);
            if (!d.Success || !e.Success)
                return false;
            decimal dv, ev;
            const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
            if (!decimal.TryParse(d.Groups["number"].Value.Replace(',', '.'), styles, CultureInfo.InvariantCulture, out dv)
                || !decimal.TryParse(e.Groups["number"].Value.Replace(',', '.'), styles, CultureInfo.InvariantCulture, out ev))
                return false;
            // Compare numeric values exactly; formatting and trailing zeros may differ.
            return dv == ev;
        }

        private static bool ContainsTablePropertyExpression(string text)
        {
            return Regex.IsMatch(text ?? "", @"\$PRP(?:SHEET|MODEL|VIEW)?\s*:", RegexOptions.IgnoreCase);
        }

        private TableDiagnosticInfo ExtractTableInfo(
            ITableAnnotation table,
            string viewName,
            string sheetName,
            int index,
            ModelDoc2 drawingModel,
            SheetFrameBounds frameBounds)
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
                bool tableHasPosition = false;

                try
                {
                    object posObj = ann != null ? ann.GetPosition() : null;
                    double[] pos = posObj as double[];

                    if (pos != null && pos.Length >= 2)
                    {
                        tableX = pos[0];
                        tableY = pos[1];
                        tableHasPosition = true;
                    }
                }
                catch
                {
                }

                // IMPORTANT — FRAME FILTER FOR TABLES:
                // Annotation.GetPosition() là ANCHOR của bảng, không bảo đảm là góc trái-trên.
                // Nếu tự cộng/trừ width/height để suy ra tâm từng cell, bảng neo BottomRight/TopRight
                // có thể bị tính X/Y ra ngoài Sheet dù bảng đang nằm hoàn toàn trong khung.
                // Vì vậy phạm vi Sheet của TABLE được quyết định bằng chính anchor của table.
                // X/Y của từng cell chỉ dùng cho navigation và cũng fallback về anchor,
                // KHÔNG dùng để quyết định cell có nằm trong khung hay không.
                if (frameBounds != null && frameBounds.IsValid)
                {
                    if (tableHasPosition)
                    {
                        bool tableAnchorInside =
                            frameBounds.Contains(tableX, tableY);

                        LogDebug(
                            $"[TABLE FRAME] table={index} " +
                            $"anchorX={tableX:F6} anchorY={tableY:F6} " +
                            $"inside={tableAnchorInside} rows={rowCount} cols={colCount}");

                        if (!tableAnchorInside)
                        {
                            LogDebug(
                                $"[TABLE FRAME FILTER] table={index} " +
                                "SKIP_TABLE reason=ANCHOR_OUTSIDE_SHEET");
                            return null;
                        }
                    }
                    else
                    {
                        // Không có position thì không được tự loại bảng, tránh false NG.
                        LogDebug(
                            $"[TABLE FRAME WARNING] table={index} " +
                            "không đọc được anchor; giữ bảng để tránh false NG.");
                    }
                }

                TableDiagnosticInfo tableInfo = new TableDiagnosticInfo
                {
                    Index = index,
                    Name = annName,
                    ViewName = viewName,
                    SheetName = sheetName,
                    RowCount = rowCount,
                    ColumnCount = colCount,
                    HasPosition = tableHasPosition,
                    X = tableX,
                    Y = tableY
                };

                for (int r = 0; r < rowCount; r++)
                {
                    for (int c = 0; c < colCount; c++)
                    {
                        // Drawing evidence must come from the cell display, independently
                        // of the model property used for the expected value.
                        string apiDisplayedText = "";
                        string apiDisplayedTextWithHidden = "";
                        string rawText = "";
                        try { apiDisplayedText = table.get_DisplayedText2(r, c, false) ?? ""; } catch { }
                        try { apiDisplayedTextWithHidden = table.get_DisplayedText2(r, c, true) ?? ""; } catch { }
                        try { rawText = table.get_Text(r, c) ?? ""; } catch { }

                        string valueSource;
                        string displayText = SelectDrawingTableText(
                            apiDisplayedText, apiDisplayedTextWithHidden, rawText, out valueSource);
                        if (valueSource == "DRAWING_DISPLAY_UNAVAILABLE")
                        {
                            // Older display accessor is a separate drawing-side fallback.
                            string legacyDisplay = "";
                            try { legacyDisplay = table.get_DisplayedText(r, c) ?? ""; } catch { }
                            if (!string.IsNullOrWhiteSpace(legacyDisplay)
                                && !ContainsTablePropertyExpression(legacyDisplay))
                            {
                                displayText = legacyDisplay;
                                valueSource = "DISPLAYED_TEXT_LEGACY";
                            }
                        }

                        // Không suy diễn vị trí cell từ table anchor.
                        // AnchorType của SolidWorks có thể là TopLeft/TopRight/BottomLeft/BottomRight;
                        // phép cộng/trừ width/height cũ làm sai tọa độ và loại nhầm toàn bộ table.
                        // Dùng anchor làm vị trí navigation ổn định cho mọi cell.
                        double cellX = tableX;
                        double cellY = tableY;

                        tableInfo.Cells.Add(new TableCellDiagnosticInfo
                        {
                            Row = r,
                            Column = c,
                            DisplayedText = displayText,
                            ApiDisplayedText = apiDisplayedText,
                            ApiDisplayedTextWithHidden = apiDisplayedTextWithHidden,
                            RawText = rawText,
                            ValueSource = valueSource,
                            X = cellX,
                            Y = cellY
                        });
                    }
                }

                if (frameBounds != null && frameBounds.IsValid)
                {
                    LogDebug(
                        $"[TABLE FRAME RESULT] table={index} " +
                        $"keptCells={tableInfo.Cells.Count} " +
                        $"anchorX={tableX:F6} anchorY={tableY:F6}");
                }

                return tableInfo;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsNoteInsideFrame(
            NoteDiagnosticInfo info,
            SheetFrameBounds frameBounds)
        {
            if (info == null)
                return false;

            if (frameBounds == null || !frameBounds.IsValid)
                return true;

            if (!info.HasPosition)
                return false;

            return frameBounds.Contains(info.X, info.Y);
        }

        private static SheetFrameBounds ResolveSheetFrameBounds(Sheet sheet)
        {
            SheetFrameBounds bounds = new SheetFrameBounds();
            if (sheet == null)
                return bounds;

            double width = 0.0;
            double height = 0.0;

            try
            {
                double w = 0.0;
                double h = 0.0;
                ((ISheet)sheet).GetSize(ref w, ref h);

                if (w > 0.0 && h > 0.0)
                {
                    width = w;
                    height = h;
                    bounds.Source = "ISheet.GetSize";
                }
            }
            catch
            {
            }

            if (width <= 0.0 || height <= 0.0)
            {
                object propsObj = null;
                try
                {
                    dynamic dynSheet = sheet;
                    propsObj = dynSheet.GetProperties2();
                    bounds.Source = "ISheet.GetProperties2";
                }
                catch
                {
                    try
                    {
                        dynamic dynSheet = sheet;
                        propsObj = dynSheet.GetProperties();
                        bounds.Source = "ISheet.GetProperties";
                    }
                    catch
                    {
                        propsObj = null;
                    }
                }

                try
                {
                    if (propsObj is double[] d && d.Length >= 7)
                    {
                        width = d[5];
                        height = d[6];
                    }
                    else if (propsObj is object[] o && o.Length >= 7)
                    {
                        width = Convert.ToDouble(o[5], CultureInfo.InvariantCulture);
                        height = Convert.ToDouble(o[6], CultureInfo.InvariantCulture);
                    }
                }
                catch
                {
                    width = 0.0;
                    height = 0.0;
                }
            }

            if (width > 0.0 && height > 0.0)
            {
                bounds.IsValid = true;
                bounds.MinX = 0.0;
                bounds.MinY = 0.0;
                bounds.MaxX = width;
                bounds.MaxY = height;
            }

            return bounds;
        }

        private static void LogSheetFrameBounds(
            string sheetName,
            SheetFrameBounds bounds)
        {
            if (bounds != null && bounds.IsValid)
            {
                LogDebug(
                    $"[SHEET FRAME] sheet=\"{sheetName}\" " +
                    $"X=[{bounds.MinX:F6},{bounds.MaxX:F6}] " +
                    $"Y=[{bounds.MinY:F6},{bounds.MaxY:F6}] " +
                    $"source={bounds.Source}");
            }
            else
            {
                LogDebug(
                    $"[SHEET FRAME WARNING] sheet=\"{sheetName}\" " +
                    "không đọc được kích thước Sheet; frame filter được bỏ qua để tránh false NG.");
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

        /// <summary>
        /// 数量 nguồn chuẩn cho cả COMPONENT và UNIT:
        /// lấy trực tiếp từ đúng dòng DataGridView đã được SolidWorks BOM list ra.
        /// Ưu tiên nhận diện cột bằng HeaderText; fallback index 4 để tương thích
        /// layout grid hiện tại: [0]=select, [1]=部品番号, [2]=材質,
        /// [3]=板厚, [4]=数量, [5]=file name.
        /// </summary>
        private static string GetBomQuantityFromRow(DataGridViewRow row)
        {
            if (row == null)
                return "";

            try
            {
                DataGridView grid = row.DataGridView;
                if (grid != null)
                {
                    foreach (DataGridViewColumn column in grid.Columns)
                    {
                        if (column == null)
                            continue;

                        string header =
                            NormalizeText(column.HeaderText ?? "")
                            .Replace(" ", "")
                            .Trim();

                        if (string.Equals(header, "数量", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(header, "Qty", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(header, "QTY", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(header, "Quantity", StringComparison.OrdinalIgnoreCase))
                        {
                            return GetCellText(row, column.Index);
                        }
                    }
                }
            }
            catch
            {
            }

            // Fallback cho layout BOM grid hiện tại.
            if (row.Cells != null && row.Cells.Count > 4)
                return GetCellText(row, 4);

            return "";
        }

        private static bool IsBomQuantityPropertyName(string propertyName)
        {
            string n =
                NormalizeText(propertyName ?? "")
                .Replace(" ", "")
                .Trim();

            return string.Equals(n, "数量", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(n, "Qty", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(n, "QTY", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(n, "Quantity", StringComparison.OrdinalIgnoreCase);
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

            MessageBox.Show(
                sb.ToString(),
                "CHECK DRAWING BOM",
                MessageBoxButtons.OK,
                icon);
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
            public bool HasPosition { get; set; }
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
            public bool HasPosition { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public List<TableCellDiagnosticInfo> Cells { get; } = new List<TableCellDiagnosticInfo>();
        }

        private sealed class TableCellDiagnosticInfo
        {
            public int Row { get; set; }
            public int Column { get; set; }

            // Effective value used by the checker.
            public string DisplayedText { get; set; }

            // Diagnostic values returned directly by the SOLIDWORKS table API.
            public string ApiDisplayedText { get; set; }
            public string ApiDisplayedTextWithHidden { get; set; }
            public string RawText { get; set; }
            public string ValueSource { get; set; }

            public double X { get; set; }
            public double Y { get; set; }
        }

        #endregion
    }
}
