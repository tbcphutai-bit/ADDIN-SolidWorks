// Adapted from user-supplied DXF.zip; see SOURCE.md.
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
namespace ADDIN.Commands.DrawingDxfExporting
{
    internal partial class DxfOutPut
    {
        private const string ExportStagePrefix = "XToolPro_DxfStage_";
        private const string DxfLayerMapFileName =
            "XToolProLaserMark.slddxfmap";

        public static bool Export(BomDxfPart item, string saveRoot, int bendLine, int lazerMark,
            string extension, bool recognizeCutExtrudeAsLaserMark = false, bool keepDrawingViewMarkSketches = false,
            IDictionary<string, string> batchOutputPaths = null)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ComponentPath))
                throw new ArgumentException("Đường dẫn Part đang trống.");
            var properties = new DXFPropforExport {
                compPath = item.ComponentPath, PartPath = item.ComponentPath,
                DrwPath = item.DrawingPath, FileName = item.ComponentPath,
                SourceConfiguration = item.ConfigurationName,
                KeepDrawingViewMarkSketches = keepDrawingViewMarkSketches && lazerMark == 1,
                ExportSplinesAsSplines = item.ExportSplinesAsSplines,
                BatchOutputPaths = batchOutputPaths
            };
            InitializePropertiesFromItem(item, null, properties);
            OpenDocumentSnapshot before = CaptureOpenDocumentSnapshot();
            string activeTitle = DxfExportContext.App.IActiveDoc2?.GetTitle() ?? "";
            try {
                using (DxfTrace.Block("BOM " + item.PartNumber + " · " + item.ComponentPath))
                    return ExportDrawingView(properties, saveRoot, bendLine, lazerMark, extension, recognizeCutExtrudeAsLaserMark);
            }
            catch (Exception ex) {
                DxfTrace.Error("Xuất DXF thất bại", ex);
                ErrorEvents.LogError(ex.Message);
                return false;
            }
            finally { RestoreDocumentStateAfterExport(before, activeTitle); }
        }

        private static void InitializePropertiesFromItem(
            BomDxfPart item,
            string orderNumber,
            DXFPropforExport properties)
        {
            properties.OrderNumber = (orderNumber ?? "").Trim();
            properties.PartNumber = item.PartNumber ?? "";
            properties.QTY = item.PartQty > 0
                ? item.PartQty.ToString()
                : "";
            properties.UnitName = item.UnitName ?? "";
            properties.Thickness = item.Thickness > 0
                ? item.Thickness.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                : "";
            properties.material = item.PMaterial ?? "";
            properties.DxfSaveFileName = "";
        }

        private static bool TrySetDxfFileName(
            DXFPropforExport properties)
        {
            if (properties == null) return false;
            if (string.IsNullOrWhiteSpace(properties.OrderNumber))
            {
                ErrorEvents.LogError(
                    $"File:{properties.FileName}-{properties.PartNumber}: " +
                    "Không thể tạo tên DXF vì custom property 手配番号 đang trống.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(properties.PartNumber))
            {
                ErrorEvents.LogError(
                    $"File:{properties.FileName}: " +
                    "Không thể tạo tên DXF vì custom property 部品番号 đang trống.");
                return false;
            }

            properties.DxfSaveFileName = BuildDxfFileName(
                properties.OrderNumber,
                properties.PartNumber,
                properties.PartPath);
            return true;
        }

        private static string BuildDxfFileName(
            string orderNumber,
            string partNumber,
            string componentPath)
        {
            string safeOrderNumber =
                SanitizeFileNamePart(orderNumber);
            string safePartNumber =
                SanitizeFileNamePart(partNumber);

            if (string.IsNullOrWhiteSpace(safePartNumber))
            {
                safePartNumber =
                    Path.GetFileNameWithoutExtension(componentPath ?? "");
            }
            safePartNumber = SanitizeFileNamePart(safePartNumber);
            if (string.IsNullOrWhiteSpace(safePartNumber))
                safePartNumber = "Part";

            return safeOrderNumber + "-" + safePartNumber;
        }

        private static string SanitizeFileNamePart(string value)
        {
            string result = (value ?? "").Trim();
            foreach (char invalidCharacter in
                Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalidCharacter, '_');
            }

            result = result.Trim().TrimEnd('.', ' ');
            string stem = result.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                 stem[3] >= '1' && stem[3] <= '9')) result = "_" + result;
            return result;
        }

        private static bool ExportDrawingView(DXFPropforExport properties, string saveRoot,
            int bendLine, int lazerMark, string extension, bool recognizeCutExtrudeAsLaserMark)
        {
            // A Drawing that is not open in SOLIDWORKS is opened ONCE, as the disposable copy the
            // export works on anyway: the view, the DXF identity and the MARK sketches are read from
            // that copy before it is trimmed. Opening the source first and the copy after it cost
            // about one extra Drawing load per row (measured 09/10/2026: 2.47 s -> 1.39 s per row).
            // A Drawing the user has open keeps the old path, which carries unsaved changes.
            // TAI_DXF_OPEN_SOURCE=1 forces the old path for comparison.
            if (DxfExportContext.App.IGetOpenDocumentByName2(properties.DrwPath) == null &&
                System.Environment.GetEnvironmentVariable("TAI_DXF_OPEN_SOURCE") != "1")
            {
                // The copy lives in %TEMP%, away from its Part: opened on its own it binds to the
                // path stored in the Drawing (wherever the project was last saved), not to the Part
                // of the BOM row. With that Part already in memory the copy binds to it. A Part
                // loaded here is closed again by RestoreDocumentStateAfterExport.
                PreloadPart(properties.PartPath);
                DrawingCopy opened = OpenDrawingCopy(DxfExportContext.App, null, properties.DrwPath);
                try {
                    return ExportFromDrawing(opened.Model as ModelDoc2, opened, properties, saveRoot,
                        bendLine, lazerMark, extension, recognizeCutExtrudeAsLaserMark);
                }
                finally { opened.Dispose(); }
            }

            using (DocumentLease lease = DocumentLease.Open(properties.DrwPath)) {
                ModelDoc2 drawing = lease.Document;
                if (drawing == null || drawing.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                    throw new InvalidOperationException("Không mở được Drawing: " + properties.DrwPath);
                string originalSheet = (drawing as IDrawingDoc)?.GetCurrentSheet() is ISheet sheet ? sheet.GetName() : "";
                try {
                    return ExportFromDrawing(drawing, null, properties, saveRoot,
                        bendLine, lazerMark, extension, recognizeCutExtrudeAsLaserMark);
                }
                finally {
                    if (!string.IsNullOrWhiteSpace(originalSheet)) (drawing as IDrawingDoc)?.ActivateSheet(originalSheet);
                }
            }
        }

        /// <summary>
        /// A Part that is in memory with a flat-pattern configuration ALREADY rebuilt in this session gets
        /// all its configurations rebuilt before its Drawing copy opens. Measured 11/10/2026 on 4119 with
        /// the BOM assembly loaded: opening the Drawing of 4119-04-M3A-S03-2 makes SOLIDWORKS rebuild the
        /// flat-pattern configuration of its twin 4119-04-M2A-S03-2 too, in another state (flat view
        /// turned 3.5°, the hole re-drilled in the flat pattern no longer round); the twin's Drawing then
        /// shows that state and its MARK hole is lost. A rebuild of the Part itself gives the saved state
        /// back. A configuration still waiting for its rebuild is left alone: SOLIDWORKS builds it right
        /// when the Drawing opens. TAI_DXF_NO_PART_REFRESH=1 switches this off (for comparison).
        /// </summary>
        private static void RebuildFlatConfigurationRebuiltElsewhere(ModelDoc2 part)
        {
            if (part == null || System.Environment.GetEnvironmentVariable("TAI_DXF_NO_PART_REFRESH") == "1") return;
            try
            {
                string active = (part.GetActiveConfiguration() as Configuration)?.Name ?? "";
                foreach (string name in part.GetConfigurationNames() as string[] ?? new string[0])
                {
                    if (string.Equals(name, active, StringComparison.OrdinalIgnoreCase) ||
                        !name.EndsWith("SM-FLAT-PATTERN", StringComparison.OrdinalIgnoreCase)) continue;
                    Configuration configuration = part.GetConfigurationByName(name) as Configuration;
                    if (configuration == null || configuration.NeedsRebuild) continue;
                    var clock = Stopwatch.StartNew();
                    bool rebuilt = part.Extension.ForceRebuildAll();
                    DxfTrace.Info("Part đang mở có cấu hình trải '" + name + "' đã được dựng lại trong phiên này: dựng lại mọi cấu hình của Part = " +
                                  rebuilt + ", " + clock.ElapsedMilliseconds + " ms");
                    return;
                }
            }
            catch (Exception ex) { DxfTrace.Error("dựng lại cấu hình trải của Part trước khi mở bản sao bản vẽ", ex); }
        }

        /// <summary>Loads the Part without a window when it is not in memory yet.</summary>
        private static void PreloadPart(string partPath)
        {
            ISldWorks app = DxfExportContext.App;
            if (string.IsNullOrWhiteSpace(partPath) || !File.Exists(partPath)) return;
            ModelDoc2 loaded = app.IGetOpenDocumentByName2(partPath) as ModelDoc2;
            if (loaded != null)
            {
                RebuildFlatConfigurationRebuiltElsewhere(loaded);
                return;
            }
            int errors = 0, warnings = 0;
            var clock = Stopwatch.StartNew();
            app.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
            try
            {
                ModelDoc2 part = app.OpenDoc6(partPath, (int)swDocumentTypes_e.swDocPART,
                    (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_ReadOnly),
                    "", ref errors, ref warnings) as ModelDoc2;
                DxfTrace.Info("OpenDoc6 Part của dòng BOM (nạp trước bản sao): " + (part != null ? "OK" : "NULL") + ", " +
                              DescribeLoadResult(errors, warnings) + ", " + clock.ElapsedMilliseconds + " ms");
            }
            finally { app.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART); }
        }

        /// <summary>
        /// Finds the Flat Pattern view on <paramref name="drawing"/>, reads the DXF identity and exports.
        /// <paramref name="openedCopy"/> is set when <paramref name="drawing"/> already is the disposable copy.
        /// </summary>
        private static bool ExportFromDrawing(ModelDoc2 drawing, DrawingCopy openedCopy, DXFPropforExport properties,
            string saveRoot, int bendLine, int lazerMark, string extension, bool recognizeCutExtrudeAsLaserMark)
        {
            string viewName = FindFlatPatternViewName(drawing, properties);
            if (string.IsNullOrWhiteSpace(viewName)) return false;
            if (!TryResolveExportIdentityFromDrawingView(drawing, viewName, properties, out string partPath)) return false;
            DxfExportNotices.SetPartNumber(properties.PartNumber);
            IView view = FindViewByName(drawing, viewName);
            try {
                if (view == null) throw new InvalidOperationException("Flat Pattern view không còn khả dụng.");
                return ExportFlatPatternView(drawing, view, viewName, properties, saveRoot,
                    bendLine, lazerMark, extension, recognizeCutExtrudeAsLaserMark, openedCopy);
            }
            finally { SafeReleaseComObject(view); }
        }
        private static bool TryResolveExportIdentityFromDrawingView(
            ModelDoc2 drawing,
            string viewName,
            DXFPropforExport properties,
            out string referencedPartPath)
        {
            referencedPartPath = "";
            IView view = null;
            ModelDoc2 referencedPart = null;
            DocumentLease referencedPartLease = null;
            try
            {
                view = FindViewByName(drawing, viewName);
                if (view == null)
                {
                    ErrorEvents.LogError(
                        $"File:{properties.FileName}-{properties.PartNumber}: " +
                        "Cannot reacquire FlatPattern View '" +
                        viewName + "' to read export properties.");
                    return false;
                }

                string referencedConfiguration =
                    view.ReferencedConfiguration ?? "";
                try
                {
                    referencedPartPath =
                        (view.GetReferencedModelName() ?? "").Trim();
                }
                catch (Exception ex)
                {
                    DxfTrace.Debug(
                        "Cannot read referenced model path from FlatPattern " +
                        "View '" + viewName + "': " + ex.Message);
                }

                try
                {
                    referencedPart =
                        view.ReferencedDocument as ModelDoc2;
                }
                catch (Exception ex)
                {
                    DxfTrace.Debug(
                        "Cannot read ReferencedDocument from FlatPattern " +
                        "View '" + viewName + "': " + ex.Message);
                }

                if (referencedPart == null &&
                    !string.IsNullOrWhiteSpace(referencedPartPath))
                {
                    referencedPartLease =
                        DocumentLease.Open(referencedPartPath);
                    referencedPart = referencedPartLease.Document;
                }

                if (referencedPart == null ||
                    referencedPart.GetType() !=
                    (int)swDocumentTypes_e.swDocPART)
                {
                    ErrorEvents.LogError(
                        $"File:{properties.FileName}-{properties.PartNumber}: " +
                        "Cannot open the Part referenced by FlatPattern View '" +
                        viewName + "'.");
                    return false;
                }

                string loadedPartPath =
                    (referencedPart.GetPathName() ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(loadedPartPath))
                {
                    // Prefer the canonical path reported by the loaded Part.
                    // GetReferencedModelName can be relative in older drawings.
                    referencedPartPath = loadedPartPath;
                }
                if (string.IsNullOrWhiteSpace(referencedPartPath))
                {
                    ErrorEvents.LogError(
                        $"File:{properties.FileName}-{properties.PartNumber}: " +
                        "The Part referenced by FlatPattern View '" +
                        viewName + "' has not been saved.");
                    return false;
                }

                properties.OrderNumber =
                    ReadReferencedCustomProperty(
                        referencedPart,
                        referencedConfiguration,
                        DxfPropertyNames.PropOrderNumber);
                properties.PartNumber =
                    ReadReferencedCustomProperty(
                        referencedPart,
                        referencedConfiguration,
                        DxfPropertyNames.PropPartNumber);
                properties.PartPath = referencedPartPath;

                if (string.IsNullOrWhiteSpace(properties.OrderNumber))
                {
                    ErrorEvents.LogError(
                        $"File:{properties.FileName}-{properties.PartNumber}: " +
                        "Cannot read custom property 手配番号 from the Part " +
                        "referenced by FlatPattern View '" + viewName + "'.");
                    return false;
                }
                if (string.IsNullOrWhiteSpace(properties.PartNumber))
                {
                    ErrorEvents.LogError(
                        $"File:{properties.FileName}: " +
                        "Cannot read custom property 部品番号 from the Part " +
                        "referenced by FlatPattern View '" + viewName + "'.");
                    return false;
                }

                DxfTrace.Debug(
                    $"DXF identity from FlatPattern View '{viewName}': " +
                    $"Part='{referencedPartPath}', " +
                    $"Configuration='{referencedConfiguration}', " +
                    $"手配番号='{properties.OrderNumber}', " +
                    $"部品番号='{properties.PartNumber}'.");
                return TrySetDxfFileName(properties);
            }
            catch (Exception ex)
            {
                ErrorEvents.LogError(
                    $"File:{properties.FileName}-{properties.PartNumber}: " +
                    "Cannot read DXF export properties from the Part " +
                    "referenced by FlatPattern View '" + viewName +
                    "': " + ex.Message);
                return false;
            }
            finally
            {
                if (referencedPartLease != null)
                    referencedPartLease.Dispose();
                // ReferencedDocument can share the same RCW with other
                // SolidWorks/add-in code. Do not ReleaseComObject on that
                // borrowed top-level document; an owned DocumentLease closes
                // and final-releases only documents opened by this operation.
                referencedPart = null;
                SafeReleaseComObject(view);
            }
        }

        private static string ReadReferencedCustomProperty(
            ModelDoc2 part,
            string referencedConfiguration,
            string propertyName)
        {
            if (part == null || string.IsNullOrWhiteSpace(propertyName))
                return "";

            var model = new DxfModelAccess(part);
            string value = "";
            if (!string.IsNullOrWhiteSpace(referencedConfiguration))
            {
                value = model.GetCustomProperty(
                    propertyName,
                    referencedConfiguration,
                    resolved: true);
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                value = model.GetCustomProperty(
                    propertyName,
                    resolved: true);
            }

            return (value ?? "").Trim();
        }

        private static bool IsViewInsideSheet(IView view, double sheetWidth, double sheetHeight)
        {
            if (view == null) return false;

            double[] outline = view.GetOutline() as double[];
            if (outline == null || outline.Length < 4) return false;
            double centerX = (outline[0] + outline[2]) / 2.0;
            double centerY = (outline[1] + outline[3]) / 2.0;
            if (centerX >= 0 && centerX <= sheetWidth && centerY >= 0 && centerY <= sheetHeight)
                return true;
            return false;
        }

        private static string FindFlatPatternViewName(
            ModelDoc2 drawingModel,
            DXFPropforExport properties)
        {
            // Luật chọn đo trên 35 bản vẽ thật của bộ 4115 (FlatPatternViewSelector): đúng part →
            // đang hiện → trong khổ giấy → đúng cấu hình → khung lớn nhất, trên MỌI sheet. View
            // «展開» ẩn ngoài khổ và view palette ẩn không bao giờ thắng view trải phẳng đang hiện.
            FlatViewChoice choice = FlatPatternViewSelector.ChooseForBom(
                CollectFlatViewCandidates(drawingModel, properties?.PartPath, properties?.SourceConfiguration));
            if (choice.Chosen == null || choice.Chosen.ReferencesPart == false || choice.Chosen.ConfigurationMatches == false)
            {
                ErrorEvents.LogError(
                    $"File:{properties?.FileName}-{properties?.PartNumber}: " +
                    "Không tìm thấy view Flat Pattern tham chiếu đúng Part/config của dòng BOM trên các sheet.");
                return null;
            }

            ErrorEvents.LogDebug(
                $"File:{properties?.FileName}-{properties?.PartNumber}: [FLAT_VIEW] " + choice.Reason);
            if (DxfExportContext.CurrentFile != null) {
                DxfExportContext.CurrentFile.SheetName = choice.Chosen.SheetName;
                DxfExportContext.CurrentFile.ViewName = choice.Chosen.ViewName;
            }
            foreach (string warning in choice.Warnings)
            {
                ErrorEvents.LogDebug(
                    $"File:{properties?.FileName}-{properties?.PartNumber}: " + warning);
                DxfExportNotices.Warning("View trải phẳng", warning);
            }

            // Các bước sau tìm view trên sheet ĐANG MỞ: view nằm ở sheet khác thì mở sheet đó.
            try
            {
                var drawing = drawingModel as IDrawingDoc;
                string current = (drawing?.GetCurrentSheet() as ISheet)?.GetName();
                if (drawing != null && !string.Equals(current, choice.Chosen.SheetName, StringComparison.Ordinal))
                    drawing.ActivateSheet(choice.Chosen.SheetName);
            }
            catch (COMException ex)
            {
                DxfTrace.Debug("Cannot activate sheet of FlatPattern view: " + ex.Message);
            }
            return choice.Chosen.ViewName;
        }

        private static bool ExportFlatPatternView(
            ModelDoc2 drawingModel,
            IView drawingView,
            string drawingViewName,
            DXFPropforExport properties,
            string saveRoot,
            int bendLine,
            int lazerMark,
            string extension,
            bool recognizeCutExtrudeAsLaserMark,
            DrawingCopy openedCopy = null)
        {
            if (string.IsNullOrWhiteSpace(saveRoot))
            {
                ErrorEvents.LogError(
                    $"File:{properties.FileName}-{properties.PartNumber}: " +
                    "Export directory is empty.");
                return false;
            }

            string saveDirectory = BuildOrderOutputDirectory(
                saveRoot,
                properties);
            string outputPath = ResolveBatchOutputConflict(
                Path.Combine(
                    saveDirectory,
                    properties.DxfSaveFileName + "." + extension),
                properties);
            string stagingPath = Path.Combine(
                saveDirectory,
                ExportStagePrefix +
                Guid.NewGuid().ToString("N") + "." + extension);
            try
            {
                Directory.CreateDirectory(saveDirectory);
                CleanupStagedExportFiles(saveDirectory);
            }
            catch (Exception ex)
            {
                ErrorEvents.LogError(
                    $"File:{properties.FileName}-{properties.PartNumber}: " +
                    "Cannot create export directory: " + ex.Message);
                return false;
            }

            IDrawingDoc drawing = drawingModel as IDrawingDoc;
            ModelDocExtension extensionApi = null;
            ModelDoc2 temporaryDrawing = null;
            DrawingCopy copy = null;
            DxfDrawingColorScope colorScope = null;
            DxfManufacturingLayerSettings layerSettings =
                DxfExportContext.LayerSettings;
            string exportPhase = "initialize";
            Action<string> step = name =>
            {
                exportPhase = name;
                DxfTrace.Phase(name);
                DxfExportContext.ReportPhase(name);
            };
            DxfTrace.Value("file ra", outputPath);
            try
            {
                // Cơ chế 06/10/2026 (docs ITool dxf-export-phan-tich-2026-10-06.md): bản sao file
                // bản vẽ → giữ view trải phẳng → đọc cạnh MARK / đường uốn / vòng xoá từ model →
                // SaveAs DXF thô → đặt layer BẰNG HÌNH HỌC có kiểm chứng. Không clipboard, không
                // bản vẽ tạm dán view, không sketch marker, không map .slddxfmap.
                step("read view identity");
                string sheetName = SheetNameOf(drawingModel, drawingView);
                // Cổng xác nhận view (P0, v150): ảnh tham chiếu đọc trên VIEW GỐC sau lần dựng lại cuối.
                ViewIdentityReference viewReference = extension == "dxf"
                    ? ReadViewIdentityReference(drawingModel, drawingView)
                    : null;
                ViewFrame copyFrame = null, savedFrame = null;

                DrawingViewMarkSketchCollection userMarks = null;
                if (properties.KeepDrawingViewMarkSketches)
                {
                    // Đọc trên bản vẽ GỐC (chỉ đọc, không sửa gì).
                    step("read MARK sketches on source Drawing View");
                    userMarks = CollectDrawingViewMarkSketches(
                        drawingModel,
                        drawingView,
                        properties);
                    properties.DrawingViewMarkSketches = userMarks;
                }

                step("open drawing copy");
                // Already open when the source Drawing was not: everything above was read from it.
                copy = openedCopy ?? OpenDrawingCopy(DxfExportContext.App, drawingModel, properties.DrwPath);
                step("keep flat-pattern view on copy");
                IView copyView = KeepOnlyView(copy.Model, sheetName, drawingViewName, false, properties);

                step("read model semantics");
                DxfSemanticPlan plan = BuildSemanticPlan(
                    copyView, properties,
                    lazerMark == 1, recognizeCutExtrudeAsLaserMark, bendLine == 1,
                    userMarks, cropToView: true);   // mọi thứ ngoài khung view (khung tên còn sót…) bị bỏ
                copyFrame = ViewFrame.Read(copyView);
                // Keep graphics updating while the user monitors cleanup and DXF saving.
                EnableRedraw(copy);

                step("disable model colors");
                colorScope = new DxfDrawingColorScope(DxfExportContext.App, copy.Model);
                step("prepare copy for raw export");
                PrepareViewForRawExport(copy.Model, copyView, properties, layerSettings, partInfoText: true);

                step("save raw " + extension.ToUpperInvariant());
                int saveErrors, saveWarnings;
                bool saved = SaveRawDxf(copy.Model, stagingPath, out saveErrors, out saveWarnings, properties.ExportSplinesAsSplines);
                if (viewReference != null)
                {
                    // SaveRawDxf dựng lại bản sao 1–2 lần: đọc lại hướng view SAU lần ấy (phản biện 08/10).
                    IView savedView = null;
                    try
                    {
                        savedView = FindViewByName(copy.Model, drawingViewName);
                        savedFrame = ViewFrame.Read(savedView);
                    }
                    catch (Exception ex) { DxfTrace.Debug("Cannot re-read copy view frame: " + ex.Message); }
                    finally { SafeReleaseComObject(savedView); }
                }
                if (saved) WarnWhenViewChangedSincePlan(copy.Model, drawingViewName, plan);
                step("close drawing copy");
                copy.Dispose();
                copy = null;

                if (!saved || !File.Exists(stagingPath) ||
                    new FileInfo(stagingPath).Length == 0)
                {
                    ErrorEvents.LogError(
                        $"File:{properties.FileName}-{properties.PartNumber}: " +
                        $"Cannot save {extension.ToUpperInvariant()} " +
                        $"({DescribeSaveResult(saveErrors, saveWarnings)}).");
                    return false;
                }

                if (extension == "dxf")
                {
                    DxfTrace.Info("DXF thô: " + DxfTrace.DescribeDxf(stagingPath));
                    // Diagnostics for tools\DxfExportSmokeTest: keep the DXF exactly as
                    // SOLIDWORKS wrote it plus the plan, so the layer placement can be
                    // replayed offline.
                    if (System.Environment.GetEnvironmentVariable(
                            "XTOOLPRO_DXF_KEEP_RAW") == "1")
                    {
                        try
                        {
                            File.Copy(
                                stagingPath,
                                Path.ChangeExtension(outputPath, ".raw.dxf"),
                                true);
                            plan.Save(Path.ChangeExtension(outputPath, ".plan.txt"));
                        }
                        catch (Exception ex)
                        {
                            DxfTrace.Debug("Cannot keep raw DXF: " + ex.Message);
                        }
                    }

                    step("place semantic layers");
                    DxfSemanticResult semantic = DxfLayerFinalizer.ApplySemanticPlan(
                        stagingPath,
                        plan,
                        layerSettings);
                    ErrorEvents.LogDebug(
                        $"File:{properties.FileName}-{properties.PartNumber}: " +
                        "[SEMANTIC] " + semantic.Summary);
                    if (!semantic.Verified)
                    {
                        // Hình DXF không khớp view → không bao giờ ghi file sai hướng/vị trí.
                        ErrorEvents.LogError(
                            $"File:{properties.FileName}-{properties.PartNumber}: " +
                            "LỖI DXF — không xuất: " + semantic.Message);
                        return false;
                    }
                    ReportSemantic(semantic, properties, bendLine == 1, lazerMark == 1);
                    DxfTrace.Info("DXF sau đặt layer: " + DxfTrace.DescribeDxf(stagingPath));

                    step("finalize DXF layers");
                    DxfLayerFinalizer.Result layerResult =
                        DxfLayerFinalizer.NormalizeManufacturingLayers(
                            stagingPath,
                            bendLine == 1,
                            lazerMark == 1,
                            layerSettings,
                            applyMarkRingInterior: false,
                            semanticLayers: true);
                    DxfTrace.Info(
                        $"File:{properties.FileName}-{properties.PartNumber}: [LAYERS] " +
                        layerResult.Summary);
                    ReportLayerResult(layerResult, bendLine == 1, lazerMark == 1);
                    layerResult.MarkRingSubstitutions.AddRange(semantic.MarkRingSubstitutions);
                    properties.MarkRingSubstitutions.Clear();
                    properties.MarkRingSubstitutions.AddRange(layerResult.MarkRingSubstitutions);
                    if (layerResult.MarkRingSubstitutions.Count > 0)
                    {
                        DxfExportNotices.Info(
                            "LASER_MARK",
                            "Lỗ đánh dấu bằng LASER_MARK: giữ vòng MARK ngoài cùng, bỏ " +
                            layerResult.MarkRingSubstitutions.Count + " lỗ xuyên đồng tâm và mọi đường bên trong (" +
                            string.Join("; ", layerResult.MarkRingSubstitutions
                                .GroupBy(pair => "Ø" + (pair.Key * 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                                                 " thay Ø" + (pair.Value * 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                                .Select(group => group.Key + " ×" + group.Count())) + ").");
                    }
                    ReportMissingLaserMarkers(
                        properties,
                        layerResult.ClosedMarkContourCount);

                    step("view identity gate (log only)");
                    try
                    {
                        ViewIdentityVerdict verdict = viewReference == null || viewReference.Paths.Count == 0
                            ? null
                            : EvaluateViewIdentity(viewReference.Paths, stagingPath, layerSettings, viewReference.ViewScale);
                        LogViewIdentity(properties, viewReference, copyFrame, savedFrame, verdict);
                    }
                    catch (Exception ex)
                    {
                        DxfTrace.Debug("View identity gate failed: " + ex.Message);
                    }
                }
                else
                {
                    ReportMissingLaserMarkers(properties, 0);
                }

                step("publish output file");
                PublishExportFile(stagingPath, outputPath);
                stagingPath = null;
                DxfExportNotices.SetOutputPath(outputPath);
                if (properties.BatchOutputPaths != null)
                    properties.BatchOutputPaths[outputPath] = properties.PartPath + "|" + properties.SourceConfiguration;
                if (!CanOpenExclusively(outputPath))
                    throw new IOException("File DXF vẫn đang bị khóa: " + outputPath);
                return true;
            }
            catch (OperationCanceledException) { DxfTrace.Info("Đã hủy trước khi phát hành DXF."); return false; }
            catch (Exception ex) {
                DxfTrace.Error("DXF: " + exportPhase, ex);
                ErrorEvents.LogError("Xuất DXF [" + exportPhase + "]: " + ex.Message);
                return false;
            }
            finally {
                SafeReleaseComObject(extensionApi);
                CloseTemporaryDrawing(ref temporaryDrawing);
                copy?.Dispose();
                colorScope?.Dispose();
                TryDeleteFile(stagingPath);
            }
        }

        private static void TryRebuildForComRetry(ModelDoc2 drawingModel)
        {
            try { drawingModel?.ForceRebuild3(false); }
            catch (Exception ex)
            {
                DxfTrace.Debug("DXF COM retry rebuild failed: " + ex.Message);
            }
        }

        /// <summary>
        /// SolidWorks uses the Windows Clipboard to transfer a Drawing View.
        /// Another application can replace that Clipboard while the temporary
        /// Drawing is being created. Detect that change and recopy the source
        /// view immediately before Paste so the export does not silently lose
        /// geometry. The operation is retried a small, bounded number of times
        /// to avoid hanging when a user is actively copying in another app.
        /// </summary>

        private static bool TryActivateDocument(ModelDoc2 document)
        {
            if (document == null || DxfExportContext.App == null)
                return false;

            string title = "";
            try { title = document.GetTitle() ?? ""; }
            catch { }
            if (string.IsNullOrWhiteSpace(title)) return false;

            try
            {
                int activationErrors = 0;
                DxfExportContext.App.ActivateDoc3(
                    title,
                    false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                    ref activationErrors);
                string activeTitle = "";
                try
                {
                    activeTitle =
                        DxfExportContext.App.IActiveDoc2?.GetTitle() ?? "";
                }
                catch { }

                return activationErrors == 0 ||
                    string.Equals(
                        activeTitle,
                        title,
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                DxfTrace.Debug(
                    "DXF clipboard retry could not activate '" +
                    title + "': " + ex.Message);
                return false;
            }
        }

        private static void SafeClearSelection(ModelDoc2 model)
        {
            try { model?.ClearSelection2(true); }
            catch (COMException ex)
            {
                DxfTrace.Debug(
                    "DXF ClearSelection2 ignored COM failure: " + ex.Message);
            }
        }

        private static void TrySetTemporaryDrawingNoScale(
            ModelDoc2 temporaryDrawing,
            DXFPropforExport properties)
        {
            try
            {
                temporaryDrawing?.SetUserPreferenceIntegerValue(
                    (int)swUserPreferenceIntegerValue_e.swDxfOutputNoScale,
                    0);
            }
            catch (COMException ex)
            {
                DxfTrace.Debug(
                    $"File:{properties?.FileName}-{properties?.PartNumber}: " +
                    "optional DXF no-scale preference was rejected; " +
                    "continuing SaveAs: " + ex.Message);
            }
        }


        private static void ReportLayerResult(
            DxfLayerFinalizer.Result layerResult,
            bool includeBendLines,
            bool includeLaserMarks)
        {
            if (layerResult == null) return;

            var parts = new List<string>();
            if (includeBendLines)
                parts.Add("BEND=" + layerResult.BendEntityCount);
            if (includeLaserMarks)
                parts.Add(
                    "LASER_MARK=" + layerResult.LaserMarkEntityCount +
                    " (LINE " + layerResult.LaserLineCount +
                    ", ARC " + layerResult.LaserArcCount +
                    ", CIRCLE " + layerResult.LaserCircleCount +
                    ", khác " + layerResult.LaserOtherCount + ")");
            if (layerResult.RemainingDefaultGeometryCount > 0)
                parts.Add("layer 0=" + layerResult.RemainingDefaultGeometryCount);
            if (parts.Count > 0)
                DxfExportNotices.Info("Layer", "Số đối tượng: " + string.Join(", ", parts));

            if (layerResult.OverlapRemovedCount > 0 ||
                layerResult.OverlapTrimmedCount > 0)
            {
                DxfExportNotices.Info(
                    "LASER_MARK",
                    "Chỉ giữ đường LASER_MARK: đã gỡ " +
                    layerResult.OverlapRemovedCount +
                    " đường CUT/BEND nằm trùng" +
                    (layerResult.OverlapTrimmedCount > 0
                        ? ", cắt ngắn " + layerResult.OverlapTrimmedCount +
                          " đường trùng một phần"
                        : "") + ".");
            }

            if (layerResult.DuplicateRemovedCount > 0)
            {
                DxfExportNotices.Info(
                    "Layer",
                    "Đã gỡ " + layerResult.DuplicateRemovedCount +
                    " nét trùng hoàn toàn với nét khác cùng layer (tránh cắt hai lần).");
            }

            if (layerResult.ClosedMarkContourCount > 0)
            {
                // A cut line became an engraved line without a MARK name of
                // its own: the row must not pass as OK.
                DxfExportNotices.Warning(
                    "LASER_MARK",
                    "CẢNH BÁO LASER_MARK: đã chuyển " + layerResult.ClosedMarkContourCount +
                    " đường CUT sang LASER_MARK vì chúng khép kín hoặc nối " +
                    "các đoạn LASER_MARK (cạnh cong/cung góc của rãnh/lỗ MARK) - " +
                    "kiểm tra DXF trước khi cắt.");
            }

            if (layerResult.OverlapPartialCount > 0)
            {
                DxfExportNotices.Warning(
                    "LASER_MARK",
                    "CẢNH BÁO LASER_MARK: " + layerResult.OverlapPartialCount +
                    " cung/đường cong CUT/BEND trùng một phần với LASER_MARK " +
                    "nên chưa gỡ - kiểm tra DXF trước khi cắt.");
            }
        }

        /// <summary>
        /// Keeps the first file when a later Part of the same batch resolves
        /// to the same DXF name (same 手配番号/部品番号). The later Part is
        /// saved next to it with its Part file name appended, and the panel
        /// shows both sources so the duplicate number can be corrected.
        /// </summary>
        private static string ResolveBatchOutputConflict(
            string outputPath,
            DXFPropforExport properties)
        {
            IDictionary<string, string> published = properties?.BatchOutputPaths;
            string firstSource;
            if (published == null ||
                !published.TryGetValue(outputPath, out firstSource))
                return outputPath;

            string directory = Path.GetDirectoryName(outputPath) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(outputPath);
            string extension = Path.GetExtension(outputPath);
            string sourceName = SanitizeFileNamePart(
                Path.GetFileNameWithoutExtension(
                    properties.PartPath ?? properties.FileName ?? ""));
            if (string.IsNullOrWhiteSpace(sourceName)) sourceName = "2";

            string candidate = Path.Combine(
                directory,
                baseName + " (" + sourceName + ")" + extension);
            for (int index = 2; published.ContainsKey(candidate); index++)
            {
                candidate = Path.Combine(
                    directory,
                    baseName + " (" + sourceName + "-" + index + ")" + extension);
            }

            DxfExportNotices.Warning(
                "Tên file",
                "CẢNH BÁO TRÙNG TÊN DXF: " + Path.GetFileName(outputPath) +
                " đã được xuất từ " + firstSource + " trong lần xuất này " +
                "(cùng 手配番号/部品番号). Không ghi đè - lưu thành " +
                Path.GetFileName(candidate) + ". Kiểm tra lại 部品番号.");
            return candidate;
        }

        private static void ReportMissingLaserMarkers(
            DXFPropforExport properties,
            int closedContourCount)
        {
            int missing = properties?.LaserMarkMissingMarkerCount ?? 0;
            if (missing <= 0) return;

            string detail = properties.LaserMarkMissingMarkerDetail ?? "";
            if (closedContourCount >= missing)
            {
                DxfExportNotices.Info(
                    "LASER_MARK",
                    missing + " cạnh MARK không tạo được marker trên Drawing " +
                    "tạm (" + detail + ") nhưng đã được chuyển sang " +
                    "LASER_MARK ở bước hậu kiểm DXF.");
                return;
            }

            // Edges without a marker stay on CUT, so the laser would cut
            // them through instead of marking them.
            ErrorEvents.LogError(
                $"File:{properties.FileName}-{properties.PartNumber}: " +
                "LỖI LASER_MARK: " + (missing - closedContourCount) +
                " cạnh feature MARK vẫn nằm ở layer CUT (" + detail +
                ") - kiểm tra DXF trước khi cắt.");
        }


        private static string TryBuildOutputPath(
            string saveRoot,
            DXFPropforExport properties,
            string extension)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(saveRoot) ||
                    properties == null ||
                    string.IsNullOrWhiteSpace(
                        properties.DxfSaveFileName) ||
                    string.IsNullOrWhiteSpace(
                        properties.OrderNumber))
                    return "";

                return Path.Combine(
                    BuildOrderOutputDirectory(
                        saveRoot,
                        properties),
                    properties.DxfSaveFileName + "." + extension);
            }
            catch
            {
                return "";
            }
        }

        private static string BuildOrderOutputDirectory(
            string saveRoot,
            DXFPropforExport properties)
        {
            string safeOrderNumber = SanitizeFileNamePart(
                properties?.OrderNumber);
            if (string.IsNullOrWhiteSpace(safeOrderNumber))
            {
                throw new InvalidOperationException(
                    "Cannot create the DXF folder because 手配番号 is empty.");
            }

            // A folder that already is the 手配番号 folder (...\4119\4119 for 4119) takes the files itself;
            // in any other folder the 手配番号 folder is used, and created when it is not there yet.
            string root = Path.GetFullPath((saveRoot ?? "").Trim());
            string leaf = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.Equals(leaf, safeOrderNumber, StringComparison.OrdinalIgnoreCase)
                ? root
                : Path.Combine(root, safeOrderNumber);
        }

        /// <summary>手配番号 of the Part as a folder name, "" when it has none.</summary>
        internal static string OrderFolderName(ModelDoc2 part, string configuration)
        {
            return part == null
                ? ""
                : SanitizeFileNamePart(ReadReferencedCustomProperty(part, configuration, DxfPropertyNames.PropOrderNumber));
        }


        /// <summary>
        /// Flip/Angle of the user's FlatPattern view, read before Copy so the
        /// temporary Drawing can be forced to the same orientation.
        /// </summary>
        private sealed class SourceViewOrientation
        {
            private const double AngleToleranceRad = 1e-6;

            public bool FlipView { get; private set; }
            public double Angle { get; private set; }

            /// <summary>
            /// The real on-sheet orientation (Flat-Pattern base × Flip × Angle).
            /// Angle/Flip alone are only a first guess; this frame decides.
            /// </summary>
            public ViewFrame Frame { get; private set; }

            public static SourceViewOrientation Capture(
                IView view,
                DXFPropforExport properties)
            {
                if (view == null) return null;
                try
                {
                    return new SourceViewOrientation
                    {
                        FlipView = view.FlipView,
                        Angle = view.Angle,
                        Frame = ViewFrame.Read(view)
                    };
                }
                catch (Exception ex)
                {
                    ErrorEvents.LogError(
                        $"File:{properties?.FileName}-{properties?.PartNumber}: " +
                        "Cannot read Flip/Angle of the source FlatPattern " +
                        "view: " + ex.Message);
                    return null;
                }
            }

            public static bool IsSameAngle(double first, double second)
            {
                return Math.Abs(Math.IEEERemainder(
                    first - second,
                    2 * Math.PI)) <= AngleToleranceRad;
            }

            public override string ToString()
            {
                return "FlipView=" + FlipView + ", Angle=" +
                    (Angle * 180.0 / Math.PI).ToString(
                        "0.###",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "°";
            }
        }

        /// <summary>
        /// Makes the pasted temporary view use the same Flip/Angle as the
        /// source view. Returns true when the view had to be changed (and the
        /// caller must reacquire it after the rebuild).
        /// </summary>
        private static bool MatchSourceViewOrientation(
            ModelDoc2 temporaryDrawing,
            IView pastedView,
            SourceViewOrientation source,
            DXFPropforExport properties)
        {
            if (pastedView == null || source == null) return false;

            string prefix =
                $"File:{properties?.FileName}-{properties?.PartNumber}: ";
            bool changed = false;
            if (pastedView.FlipView != source.FlipView)
            {
                pastedView.FlipView = source.FlipView;
                changed = true;
            }
            if (!SourceViewOrientation.IsSameAngle(
                    pastedView.Angle,
                    source.Angle))
            {
                pastedView.Angle = source.Angle;
                changed = true;
            }

            if (!changed)
            {
                DxfTrace.Debug(
                    prefix + "temporary DXF view keeps source orientation (" +
                    source + ").");
                return false;
            }

            temporaryDrawing?.ForceRebuild3(false);
            if (pastedView.FlipView != source.FlipView ||
                !SourceViewOrientation.IsSameAngle(
                    pastedView.Angle,
                    source.Angle))
            {
                // Exporting a mirrored/rotated DXF is worse than failing.
                throw new InvalidOperationException(
                    "Temporary DXF view orientation does not match the " +
                    "source FlatPattern view (" + source + ").");
            }

            DxfTrace.Debug(
                prefix + "restored source orientation on temporary DXF " +
                "view (" + source + ").");
            return true;
        }

        public static void ReleaseBatchMemory()
        {
            CleanupStaleTemporaryFiles();
            // Never force GC inside the SOLIDWORKS process. A collection can
            // finalize shared COM RCWs in an arbitrary order; the next API call
            // (often the completion dialog owner lookup) may then terminate
            // SOLIDWORKS even though every DXF was already saved. The runtime
            // will collect managed objects normally when memory pressure calls
            // for it, while owned documents are closed explicitly below.
        }

        private static void ReleaseExportItemMemory(
            bool includedFlattenAnalysis)
        {
            // Do not force even a Gen-0 collection here. COM finalizers can run
            // during that collection and release a wrapper still shared by a
            // SOLIDWORKS-owned document.
            DxfTrace.Debug(
                "DXF item cleanup complete; Check DXF=" +
                includedFlattenAnalysis + ".");
        }

        private static void CleanupStaleTemporaryFiles()
        {
            try
            {
                string tempDirectory = Path.GetTempPath();
                DateTime cutoff = DateTime.UtcNow.AddHours(-1);
                foreach (string file in Directory.EnumerateFiles(
                    tempDirectory,
                    "XToolPro_CKFlatten_*.dxf",
                    SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff)
                            File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        DxfTrace.Debug(
                            "Cannot delete stale XToolPro DXF temp file '" +
                            file + "': " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                DxfTrace.Debug(
                    "Cannot enumerate stale XToolPro DXF temp files: " +
                    ex.Message);
            }
        }

        private static void CleanupStagedExportFiles(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
                return;

            DateTime cutoff = DateTime.UtcNow.AddHours(-1);
            try
            {
                foreach (string file in Directory.EnumerateFiles(
                    directory,
                    ExportStagePrefix + "*.*",
                    SearchOption.TopDirectoryOnly))
                {
                    string extension =
                        Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                    if (extension != "dxf" && extension != "dwg") continue;

                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!name.StartsWith(
                        ExportStagePrefix,
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    string token =
                        name.Substring(ExportStagePrefix.Length);
                    Guid ignored;
                    if (!Guid.TryParseExact(token, "N", out ignored)) continue;

                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff)
                            File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        DxfTrace.Debug(
                            "Cannot delete stale staged DXF/DWG '" + file +
                            "': " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                DxfTrace.Debug(
                    "Cannot enumerate staged DXF/DWG files in '" +
                    directory + "': " + ex.Message);
            }
        }

        private static void ReadPartProperties(
            ModelDoc2 part,
            DXFPropforExport properties)
        {
            try
            {
                var model = new DxfModelAccess(part);
                properties.OrderNumber =
                    model.GetCustomProperty(
                        DxfPropertyNames.PropOrderNumber,
                        resolved: true) ?? "";
                properties.PartNumber =
                    model.GetCustomProperty(
                        DxfPropertyNames.PropPartNumber,
                        resolved: true) ?? "";
                properties.QTY =
                    model.GetCustomProperty(
                        DxfPropertyNames.PropQTY,
                        resolved: true) ?? "";
                properties.UnitName =
                    model.GetCustomProperty(
                        DxfPropertyNames.PropUnitName,
                        resolved: true) ?? "";
                properties.Thickness =
                    model.GetCustomProperty(
                        DxfPropertyNames.PropThickness,
                        resolved: true) ?? "";
                properties.material =
                    model.GetCustomProperty(
                        DxfPropertyNames.PropMaterial,
                        resolved: true) ?? "";

            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Cannot read part custom properties.",
                    ex);
            }
        }

        private static string GetReferencedConfiguration(
            ModelDoc2 drawing,
            string viewName)
        {
            IView view = FindViewByName(drawing, viewName);
            if (view == null) return "";
            try
            {
                return view.ReferencedConfiguration ?? "";
            }
            finally
            {
                SafeReleaseComObject(view);
            }
        }

        private static bool IsDrawingOlderThanPart(string drawingPath, string partPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(drawingPath) || string.IsNullOrWhiteSpace(partPath) ||
                    !File.Exists(drawingPath) || !File.Exists(partPath))
                    return false;
                return File.GetLastWriteTimeUtc(drawingPath) <
                       File.GetLastWriteTimeUtc(partPath).AddMinutes(-1);
            }
            catch
            {
                return false;
            }
        }




        private static string GetActiveConfigurationName(
            ModelDoc2 model)
        {
            ConfigurationManager manager = null;
            Configuration configuration = null;
            try
            {
                manager = model?.ConfigurationManager;
                configuration = manager?.ActiveConfiguration;
                return configuration?.Name ?? "";
            }
            catch
            {
                return "";
            }
            finally
            {
                SafeReleaseComObject(configuration);
                SafeReleaseComObject(manager);
            }
        }

        private static IView FindViewByName(
            ModelDoc2 drawingModel,
            string viewName)
        {
            if (drawingModel == null || string.IsNullOrWhiteSpace(viewName))
                return null;

            IDrawingDoc drawing = drawingModel as IDrawingDoc;
            IView current = drawing?.GetFirstView() as IView;
            while (current != null)
            {
                IView next = current.GetNextView() as IView;
                string currentName = current.GetName2();
                if (string.Equals(
                    currentName,
                    viewName,
                    StringComparison.Ordinal))
                {
                    SafeReleaseComObject(next);
                    return current;
                }

                SafeReleaseComObject(current);
                current = next;
            }

            return null;
        }

        private sealed class DrawingViewDependency
        {
            public string Name { get; set; }
            public string BaseViewName { get; set; }
            public bool IsFlatPattern { get; set; }
        }

        private static List<DrawingViewDependency> GetDrawingViewDependencies(
            ModelDoc2 drawingModel)
        {
            var dependencies = new List<DrawingViewDependency>();
            IDrawingDoc drawing = drawingModel as IDrawingDoc;
            IView current = drawing?.GetFirstView() as IView;
            bool isSheetView = true;
            while (current != null)
            {
                IView next = current.GetNextView() as IView;
                object baseViewObject = null;
                try
                {
                    // The first item is the sheet view and has no model reference.
                    if (!isSheetView)
                    {
                        string name = current.GetName2();
                        string baseViewName = null;
                        try
                        {
                            baseViewObject = current.GetBaseView();
                            IView baseView = baseViewObject as IView;
                            baseViewName = baseView?.GetName2();
                        }
                        catch (COMException)
                        {
                            // A standard/root view has no base view.
                        }

                        bool isFlatPattern = false;
                        try
                        {
                            isFlatPattern = current.IsFlatPatternView();
                        }
                        catch (COMException)
                        {
                        }

                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            dependencies.Add(new DrawingViewDependency
                            {
                                Name = name,
                                BaseViewName = baseViewName,
                                IsFlatPattern = isFlatPattern
                            });
                        }
                    }
                }
                finally
                {
                    isSheetView = false;
                    SafeReleaseComObject(baseViewObject);
                    SafeReleaseComObject(current);
                    current = next;
                }
            }

            return dependencies;
        }

        private static string FindPastedRootViewName(ModelDoc2 drawingModel)
        {
            List<DrawingViewDependency> views =
                GetDrawingViewDependencies(drawingModel);
            if (views.Count == 0) return null;

            var viewNames = new HashSet<string>(
                views.Select(item => item.Name),
                StringComparer.Ordinal);
            List<DrawingViewDependency> roots = views
                .Where(item =>
                    string.IsNullOrWhiteSpace(item.BaseViewName) ||
                    !viewNames.Contains(item.BaseViewName))
                .ToList();

            // The selected source is a FlatPattern view. Prefer that root when
            // the template happens to contain another independent drawing view.
            DrawingViewDependency root = roots
                .Where(item => item.IsFlatPattern)
                .OrderByDescending(
                    item => CountDescendants(item.Name, views))
                .FirstOrDefault();
            if (root == null)
            {
                root = roots
                    .OrderByDescending(
                        item => CountDescendants(item.Name, views))
                    .FirstOrDefault();
            }

            // Defensive fallback for malformed/cyclic view metadata.
            return root?.Name ??
                views.FirstOrDefault(item => item.IsFlatPattern)?.Name ??
                views[0].Name;
        }

        private static int CountDescendants(
            string rootName,
            IList<DrawingViewDependency> views)
        {
            return views.Count(
                item => GetDependencyDepth(item, rootName, views) > 0);
        }

        private static int GetDependencyDepth(
            DrawingViewDependency view,
            string rootName,
            IList<DrawingViewDependency> views)
        {
            if (view == null ||
                string.IsNullOrWhiteSpace(rootName) ||
                string.Equals(
                    view.Name,
                    rootName,
                    StringComparison.Ordinal))
                return 0;

            var byName = views.ToDictionary(
                item => item.Name,
                StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string parentName = view.BaseViewName;
            int depth = 1;
            while (!string.IsNullOrWhiteSpace(parentName) &&
                visited.Add(parentName))
            {
                if (string.Equals(
                    parentName,
                    rootName,
                    StringComparison.Ordinal))
                    return depth;

                if (!byName.TryGetValue(
                    parentName,
                    out DrawingViewDependency parent))
                    return 0;

                parentName = parent.BaseViewName;
                depth++;
            }

            return 0;
        }

        private static void DeletePastedDependentViews(
            ModelDoc2 drawingModel,
            string rootViewName,
            DXFPropforExport properties)
        {
            if (drawingModel == null ||
                string.IsNullOrWhiteSpace(rootViewName))
                throw new InvalidOperationException(
                    "Cannot identify the pasted root Drawing View.");

            List<DrawingViewDependency> views =
                GetDrawingViewDependencies(drawingModel);
            List<DrawingViewDependency> dependents = views
                .Select(item => new
                {
                    View = item,
                    Depth = GetDependencyDepth(
                        item,
                        rootViewName,
                        views)
                })
                .Where(item => item.Depth > 0)
                .OrderByDescending(item => item.Depth)
                .Select(item => item.View)
                .ToList();
            if (dependents.Count == 0) return;

            DxfTrace.Debug(
                $"File:{properties.FileName}-{properties.PartNumber}: " +
                $"temporary Drawing View '{rootViewName}' has " +
                $"{dependents.Count} dependent view(s); deleting leaf-first.");

            IDrawingDoc drawing = drawingModel as IDrawingDoc;
            ModelDocExtension extension = null;
            try
            {
                extension = drawingModel.Extension as ModelDocExtension;
                if (extension == null)
                    throw new InvalidOperationException(
                        "Cannot access temporary Drawing extension.");

                foreach (DrawingViewDependency dependent in dependents)
                {
                    drawingModel.ClearSelection2(true);
                    drawing?.ActivateView(rootViewName);

                    bool selected = extension.SelectByID2(
                        dependent.Name,
                        "DRAWINGVIEW",
                        0,
                        0,
                        0,
                        false,
                        0,
                        null,
                        0);
                    if (!selected)
                    {
                        throw new InvalidOperationException(
                            "Cannot select pasted dependent Drawing View '" +
                            dependent.Name + "'.");
                    }

                    int deleteOptions =
                        (int)swDeleteSelectionOptions_e.swDelete_Children |
                        (int)swDeleteSelectionOptions_e.swDelete_Absorbed;
                    bool deleted =
                        extension.DeleteSelection2(deleteOptions);
                    drawingModel.ClearSelection2(true);
                    drawingModel.ForceRebuild3(false);

                    if (!deleted ||
                        DrawingViewExists(
                            drawingModel,
                            dependent.Name))
                    {
                        throw new InvalidOperationException(
                            "SolidWorks cannot delete pasted dependent " +
                            "Drawing View '" + dependent.Name + "'.");
                    }
                }

                List<DrawingViewDependency> remaining =
                    GetDrawingViewDependencies(drawingModel);
                bool hasRemainingDependent = remaining.Any(
                    item => GetDependencyDepth(
                        item,
                        rootViewName,
                        remaining) > 0);
                if (hasRemainingDependent)
                {
                    throw new InvalidOperationException(
                        "Dependent Drawing Views remain after temporary cleanup.");
                }
            }
            finally
            {
                drawingModel.ClearSelection2(true);
                SafeReleaseComObject(extension);
            }
        }

        private static bool DrawingViewExists(
            ModelDoc2 drawingModel,
            string viewName)
        {
            IView view = FindViewByName(drawingModel, viewName);
            try
            {
                return view != null;
            }
            finally
            {
                SafeReleaseComObject(view);
            }
        }

        private static bool CanOpenExclusively(string filePath)
        {
            try
            {
                using (var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None))
                {
                    return stream.Length > 0;
                }
            }
            catch
            {
                return false;
            }
        }


        /// <summary>
        /// SOLIDWORKS DXF mapping is an application-wide preference. Apply the
        /// XToolPro map only while the disposable Drawing is saved, then restore
        /// every user preference on all exit paths. The map routes visible
        /// sketch lines to LASER_MARK. Some generated sketch-bend lines share
        /// that DXF entity class, so the finalizer gives explicit bend style
        /// and boundary-spanning bend geometry priority over that map result.
        /// </summary>
        private sealed class DxfLayerExportPreferenceScope : IDisposable
        {
            private ISldWorks _application;
            private bool _originalMapping;
            private bool _originalDontShowMap;
            private bool _originalUseSolidworksLayers;
            private string _originalMappingFiles;
            private bool _originalSplinesAsSplines;
            private bool? _splinesAsSplines;
            private int _originalMappingFileIndex;
            private readonly string _mapFilePath;
            private bool _captured;

            private DxfLayerExportPreferenceScope(string mapFilePath)
            {
                _mapFilePath = mapFilePath;
            }

            public static DxfLayerExportPreferenceScope Begin(
                string mapFilePath,
                bool? splinesAsSplines = null)
            {
                var scope = new DxfLayerExportPreferenceScope(mapFilePath);
                scope._splinesAsSplines = splinesAsSplines;
                scope.CaptureAndApply();
                return scope;
            }

            private void CaptureAndApply()
            {
                _application = DxfExportContext.App;
                if (_application == null) return;

                try
                {
                    _originalMapping =
                        _application.GetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e.swDxfMapping);
                    _originalDontShowMap =
                        _application.GetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e.swDXFDontShowMap);
                    _originalUseSolidworksLayers =
                        _application.GetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e
                                .swDxfUseSolidworksLayers);
                    _originalMappingFiles =
                        _application.GetUserPreferenceStringListValue(
                            (int)swUserPreferenceStringListValue_e
                                .swDxfMappingFiles);
                    _originalMappingFileIndex =
                        _application.GetUserPreferenceIntegerValue(
                            (int)swUserPreferenceIntegerValue_e
                                .swDxfMappingFileIndex);
                    _originalSplinesAsSplines =
                        _application.GetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e
                                .swDxfExportSplinesAsSplines);
                    _captured = true;

                    // The choice made per BOM row decides, not whatever this machine was left on.
                    if (_splinesAsSplines.HasValue)
                    {
                        _application.SetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e
                                .swDxfExportSplinesAsSplines,
                            _splinesAsSplines.Value);
                        DxfTrace.Info("Spline khi lưu DXF: " + (_splinesAsSplines.Value ? "SPLINE" : "POLYLINE") +
                                      " (máy đang đặt " + (_originalSplinesAsSplines ? "SPLINE" : "POLYLINE") + ").");
                    }

                    _application.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e
                            .swDxfUseSolidworksLayers,
                        true);

                    if (!string.IsNullOrWhiteSpace(_mapFilePath) &&
                        File.Exists(_mapFilePath))
                    {
                        _application.SetUserPreferenceStringListValue(
                            (int)swUserPreferenceStringListValue_e
                                .swDxfMappingFiles,
                            _mapFilePath);
                        _application.SetUserPreferenceIntegerValue(
                            (int)swUserPreferenceIntegerValue_e
                                .swDxfMappingFileIndex,
                            0);
                        _application.SetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e.swDXFDontShowMap,
                            true);
                        _application.SetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e.swDxfMapping,
                            true);
                    }
                    else
                    {
                        // Keep the export usable if a deployment is missing
                        // the optional map asset, and never fall back to a
                        // user's unrelated global DXF map.
                        _application.SetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e.swDxfMapping,
                            false);
                        DxfTrace.Debug(
                            "XToolPro DXF layer map was not found: " +
                            (_mapFilePath ?? "<null>"));
                    }
                }
                catch (Exception ex)
                {
                    DxfTrace.Debug(
                        "Cannot isolate DXF layer export preferences: " +
                        ex.Message);
                    Restore();
                }
            }

            public void Dispose()
            {
                Restore();
            }

            private void Restore()
            {
                ISldWorks application = _application;
                _application = null;
                if (!_captured || application == null) return;

                _captured = false;
                TryRestorePreference(
                    () => application.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swDxfMapping,
                        false),
                    "mapping guard");
                TryRestorePreference(
                    () => application.SetUserPreferenceStringListValue(
                        (int)swUserPreferenceStringListValue_e
                            .swDxfMappingFiles,
                        _originalMappingFiles ?? string.Empty),
                    "mapping file list");
                TryRestorePreference(
                    () => application.SetUserPreferenceIntegerValue(
                        (int)swUserPreferenceIntegerValue_e
                            .swDxfMappingFileIndex,
                        _originalMappingFileIndex),
                    "mapping file index");
                TryRestorePreference(
                    () => application.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swDXFDontShowMap,
                        _originalDontShowMap),
                    "mapping dialog visibility");
                TryRestorePreference(
                    () => application.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e
                            .swDxfUseSolidworksLayers,
                        _originalUseSolidworksLayers),
                    "SOLIDWORKS layer preservation");
                TryRestorePreference(
                    () => application.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swDxfMapping,
                        _originalMapping),
                    "mapping toggle");
                if (_splinesAsSplines.HasValue)
                    TryRestorePreference(
                        () => application.SetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e
                                .swDxfExportSplinesAsSplines,
                            _originalSplinesAsSplines),
                        "spline export mode");
            }

            private static void TryRestorePreference(
                Action restore,
                string preferenceName)
            {
                try
                {
                    restore();
                }
                catch (Exception ex)
                {
                    DxfTrace.Debug(
                        "Cannot restore DXF " + preferenceName + ": " +
                        ex.Message);
                }
            }
        }

        private static string ResolveDxfLayerMapPath()
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string assemblyDirectory = Path.GetDirectoryName(assemblyPath);
            return Path.Combine(
                assemblyDirectory ?? string.Empty,
                "swTemplete",
                "dxfTemp",
                DxfLayerMapFileName);
        }

        private static bool HasUsableExportFile(
            bool apiResult,
            int errors,
            string filePath)
        {
            if (!apiResult || errors != 0 ||
                string.IsNullOrWhiteSpace(filePath) ||
                !File.Exists(filePath))
                return false;

            try
            {
                return new FileInfo(filePath).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void PublishExportFile(
            string stagingPath,
            string outputPath)
        {
            if (string.IsNullOrWhiteSpace(stagingPath) ||
                !File.Exists(stagingPath))
                throw new FileNotFoundException(
                    "Staged DXF/DWG was not created.",
                    stagingPath);

            if (File.Exists(outputPath))
            {
                try
                {
                    File.Replace(stagingPath, outputPath, null);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                }
                catch (IOException)
                {
                    // Some network/OneDrive providers do not support Replace.
                    // Copy is the compatibility fallback after the staged file
                    // has already been fully written and closed.
                }

                File.Copy(stagingPath, outputPath, true);
                File.Delete(stagingPath);
                return;
            }

            File.Move(stagingPath, outputPath);
        }

        private static void TryDeleteFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            try
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
            catch (Exception ex)
            {
                DxfTrace.Debug(
                    "Cannot delete staged DXF/DWG '" + filePath +
                    "': " + ex.Message);
            }
        }

        private static void CloseTemporaryDrawing(ref ModelDoc2 drawing)
        {
            ModelDoc2 document = drawing;
            drawing = null;
            if (document == null) return;

            try
            {
                document.ClearSelection2(true);
            }
            catch
            {
            }

            try
            {
                DxfModelAccess.CloseFile(document);
            }
            catch (Exception ex)
            {
                DxfTrace.Debug(
                    "Cannot close temporary DXF drawing: " + ex.Message);
            }
            finally
            {
                SafeReleaseComObject(document);
            }
        }

        private static OpenDocumentSnapshot CaptureOpenDocumentSnapshot()
        {
            var snapshot = new OpenDocumentSnapshot();
            if (DxfExportContext.App == null)
                return snapshot;

            try
            {
                foreach (OpenDocumentState state in
                    EnumerateOpenDocumentStates())
                {
                    if (string.IsNullOrWhiteSpace(state.Identity))
                    {
                        DxfTrace.Debug(
                            "DXF export cannot identify an already-open " +
                            "SolidWorks document; automatic document cleanup " +
                            "is disabled for this item.");
                        return snapshot;
                    }

                    snapshot.Documents[state.Identity] = state;
                }

                snapshot.IsComplete = true;
            }
            catch (Exception ex)
            {
                DxfTrace.Debug(
                    "Cannot capture SolidWorks document state before DXF " +
                    "export: " + ex.Message);
            }

            return snapshot;
        }

        private static List<OpenDocumentState>
            EnumerateOpenDocumentStates()
        {
            var documents = new List<OpenDocumentState>();
            ModelDoc2 current =
                DxfExportContext.App?.GetFirstDocument() as ModelDoc2;
            int guard = 0;
            while (current != null && guard++ < 10000)
            {
                ModelDoc2 next = null;
                try
                {
                    next = current.GetNext() as ModelDoc2;
                    documents.Add(ReadOpenDocumentState(current));
                }
                finally
                {
                    // Top-level documents are borrowed from SolidWorks.
                    // Releasing their shared RCW while they remain open can
                    // invalidate references owned by the add-in/assembly.
                    current = next;
                }
            }

            if (guard >= 10000)
            {
                throw new InvalidOperationException(
                    "SolidWorks document enumeration did not terminate.");
            }

            return documents;
        }

        private static OpenDocumentState ReadOpenDocumentState(
            ModelDoc2 document)
        {
            string filePath = "";
            string title = "";
            bool visible = true;
            int documentType = 0;
            try { filePath = document?.GetPathName() ?? ""; } catch { }
            try { title = document?.GetTitle() ?? ""; } catch { }
            try { visible = document != null && document.Visible; } catch { }
            try { documentType = document?.GetType() ?? 0; } catch { }

            return new OpenDocumentState
            {
                Document = document,
                FilePath = filePath,
                Title = title,
                Visible = visible,
                DocumentType = documentType,
                Identity = BuildDocumentIdentity(filePath, title)
            };
        }

        private static string BuildDocumentIdentity(
            string filePath,
            string title)
        {
            string normalizedPath = (filePath ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(normalizedPath))
            {
                try
                {
                    normalizedPath = Path.GetFullPath(normalizedPath);
                }
                catch
                {
                }

                return "PATH|" + normalizedPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            }

            string normalizedTitle = (title ?? "").Trim();
            return string.IsNullOrWhiteSpace(normalizedTitle)
                ? ""
                : "TITLE|" + normalizedTitle;
        }

        private static void RestoreDocumentStateAfterExport(
            OpenDocumentSnapshot snapshot,
            string originalActiveTitle)
        {
            if (snapshot == null || !snapshot.IsComplete)
            {
                RestoreActiveDocument(originalActiveTitle);
                return;
            }

            try
            {
                List<OpenDocumentState> currentDocuments =
                    EnumerateOpenDocumentStates();
                List<OpenDocumentState> openedByExport =
                    currentDocuments
                        .Where(state =>
                            !string.IsNullOrWhiteSpace(state.Identity) &&
                            !snapshot.Documents.ContainsKey(state.Identity))
                        .OrderBy(GetDocumentCloseOrder)
                        .ToList();

                int closedCount = 0;
                foreach (OpenDocumentState state in openedByExport)
                {
                    if (CloseDocumentOpenedByExport(state))
                        closedCount++;
                }

                RestoreActiveDocument(originalActiveTitle);

                // A Part can already be loaded invisibly by the source
                // assembly. Check DXF activates it for configuration/Flatten
                // evaluation. Restore that hidden state instead of closing a
                // document that belonged to the assembly before the export.
                foreach (OpenDocumentState state in currentDocuments)
                {
                    if (!snapshot.Documents.TryGetValue(
                        state.Identity ?? "",
                        out OpenDocumentState original))
                    {
                        continue;
                    }

                    if (state.Document == null ||
                        state.Visible == original.Visible)
                    {
                        continue;
                    }

                    try
                    {
                        state.Document.Visible = original.Visible;
                    }
                    catch (Exception ex)
                    {
                        DxfTrace.Debug(
                            "Cannot restore visibility of SolidWorks document '" +
                            state.Title + "': " + ex.Message);
                    }
                }

                DxfTrace.Debug(
                    "DXF document cleanup: closed " + closedCount +
                    " document(s) opened by this item; restored " +
                    "pre-existing document visibility.");
            }
            catch (Exception ex)
            {
                DxfTrace.Debug(
                    "DXF document cleanup failed: " + ex);
                RestoreActiveDocument(originalActiveTitle);
            }
        }

        private static int GetDocumentCloseOrder(
            OpenDocumentState state)
        {
            if (state == null) return 3;
            if (state.DocumentType ==
                (int)swDocumentTypes_e.swDocDRAWING)
                return 0;
            if (state.DocumentType ==
                (int)swDocumentTypes_e.swDocASSEMBLY)
                return 1;
            if (state.DocumentType ==
                (int)swDocumentTypes_e.swDocPART)
                return 2;
            return 3;
        }

        private static bool CloseDocumentOpenedByExport(
            OpenDocumentState state)
        {
            if (state?.Document == null ||
                string.IsNullOrWhiteSpace(state.Title) ||
                DxfExportContext.App == null)
            {
                return false;
            }

            try
            {
                try { state.Document.ClearSelection2(true); } catch { }
                DxfExportContext.App.CloseDoc(state.Title);
                bool closed = !IsDocumentStillOpen(state);
                if (!closed)
                {
                    ErrorEvents.LogError(
                        "DXF cleanup: SolidWorks did not close document '" +
                        state.Title + "' opened by the export operation.");
                    return false;
                }

                SafeReleaseComObject(state.Document);
                state.Document = null;
                return true;
            }
            catch (Exception ex)
            {
                ErrorEvents.LogError(
                    "DXF cleanup: cannot close document '" +
                    state.Title + "': " + ex.Message);
                return false;
            }
        }

        private static bool IsDocumentStillOpen(
            OpenDocumentState state)
        {
            if (state == null || DxfExportContext.App == null)
                return false;

            if (!string.IsNullOrWhiteSpace(state.FilePath))
            {
                try
                {
                    return DxfExportContext.App.IGetOpenDocumentByName2(
                        state.FilePath) != null;
                }
                catch
                {
                }
            }

            try
            {
                return EnumerateOpenDocumentStates().Any(
                    document => string.Equals(
                        document.Identity,
                        state.Identity,
                        StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return true;
            }
        }

        private static void RestoreActiveDocument(string title)
        {
            if (DxfExportContext.App == null || string.IsNullOrWhiteSpace(title))
                return;

            try
            {
                int activationErrors = 0;
                DxfExportContext.App.ActivateDoc3(
                    title,
                    false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                    ref activationErrors);
                if (activationErrors != 0)
                {
                    DxfTrace.Debug(
                        "DXF export could not reactivate '" + title +
                        "', SolidWorks error=" + activationErrors);
                }
            }
            catch (Exception ex)
            {
                DxfTrace.Debug(
                    "DXF export could not restore active document: " +
                    ex.Message);
            }
        }

        private static void SafeReleaseComObject(object value)
        {
            if (value == null || !Marshal.IsComObject(value)) return;
            try
            {
                Marshal.ReleaseComObject(value);
            }
            catch
            {
            }
        }

        private sealed class DocumentLease : IDisposable
        {
            private bool _disposed;

            private DocumentLease(ModelDoc2 document, bool ownsDocument)
            {
                Document = document;
                OwnsDocument = ownsDocument;
            }

            public ModelDoc2 Document { get; private set; }
            public bool OwnsDocument { get; }

            public static DocumentLease Open(string filePath)
            {
                if (DxfExportContext.App == null ||
                    string.IsNullOrWhiteSpace(filePath))
                    return new DocumentLease(null, false);

                ModelDoc2 document =
                    DxfExportContext.App.IGetOpenDocumentByName2(filePath);
                bool ownsDocument = false;
                if (document == null && File.Exists(filePath))
                {
                    document = DxfModelAccess.OpenFile(filePath);
                    ownsDocument = document != null;
                }

                return new DocumentLease(document, ownsDocument);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                ModelDoc2 document = Document;
                Document = null;
                if (!OwnsDocument || document == null)
                    return;

                try
                {
                    document.ClearSelection2(true);
                }
                catch
                {
                }

                try
                {
                    DxfModelAccess.CloseFile(document);
                }
                catch (Exception ex)
                {
                    DxfTrace.Debug(
                        "Cannot close document opened by DXF export: " +
                        ex.Message);
                }
                finally
                {
                    SafeReleaseComObject(document);
                }
            }
        }

        private sealed class OpenDocumentSnapshot
        {
            public OpenDocumentSnapshot()
            {
                Documents =
                    new Dictionary<string, OpenDocumentState>(
                        StringComparer.OrdinalIgnoreCase);
            }

            public bool IsComplete { get; set; }
            public Dictionary<string, OpenDocumentState> Documents { get; }
        }

        private sealed class OpenDocumentState
        {
            public ModelDoc2 Document { get; set; }
            public string Identity { get; set; }
            public string FilePath { get; set; }
            public string Title { get; set; }
            public bool Visible { get; set; }
            public int DocumentType { get; set; }
        }

        private static void DeleteSketch(IModelDoc2 tempDraw, IView swView)
        {
            var sketch = (ISketch)swView.GetSketch();
            ISelectionMgr swSelMgr = tempDraw.SelectionManager as ISelectionMgr;
            SelectData swSelData = swSelMgr.CreateSelectData();
            swSelData.View = (View)swView;
            object[] skSeg = sketch.GetSketchSegments() as object[];
            tempDraw.ClearSelection2(true);
            if (skSeg != null)
            {
                foreach (ISketchSegment item in skSeg.Cast<ISketchSegment>())
                {
                    item.Select4(true, swSelData);
                }
                tempDraw.DeleteSelection(true);
            }

            // Sketch points of the view reach the DXF as small crosses on
            // layer SLD-0 (seen at bend-line ends): never part geometry.
            object[] points = sketch.GetSketchPoints2() as object[];
            if (points != null && points.Length > 0)
            {
                tempDraw.ClearSelection2(true);
                int selected = 0;
                foreach (ISketchPoint point in points.OfType<ISketchPoint>())
                {
                    if (point.Select4(true, swSelData)) selected++;
                }
                if (selected > 0) tempDraw.DeleteSelection(true);
                tempDraw.ClearSelection2(true);
            }
        }


        private static void DeleteDetailView(
            IModelDoc2 tempDraw,
            IView drawingView)
        {
            if (tempDraw == null || drawingView == null) return;

            IView activeView = null;
            ModelDocExtension extension = null;
            object[] detailCircles = null;
            try
            {
                tempDraw.ClearSelection2(true);
                IDrawingDoc drawing = tempDraw as IDrawingDoc;
                drawing?.ActivateView(drawingView.GetName2());
                activeView = drawing?.ActiveDrawingView as IView;
                if (activeView == null) return;

                extension = tempDraw.Extension as ModelDocExtension;
                detailCircles = activeView.GetDetailCircles() as object[];
                if (extension == null || detailCircles == null) return;

                foreach (object detailCircleObject in detailCircles)
                {
                    IDetailCircle detailCircle =
                        detailCircleObject as IDetailCircle;
                    try
                    {
                        if (detailCircle == null) continue;
                        detailCircle.GetLabelPosition(
                            out double xPosition,
                            out double yPosition);
                        bool selected = extension.SelectByID2(
                            detailCircle.GetName(),
                            "DETAILCIRCLE",
                            xPosition,
                            yPosition,
                            0,
                            false,
                            0,
                            null,
                            0);
                        if (!selected)
                        {
                            selected = extension.SelectByID2(
                                detailCircle.GetName(),
                                "詳細図",
                                xPosition,
                                yPosition,
                                0,
                                false,
                                0,
                                null,
                                0);
                        }

                        if (selected)
                        {
                            extension.DeleteSelection2(
                                (int)swDeleteSelectionOptions_e
                                    .swDelete_Absorbed);
                        }
                    }
                    finally
                    {
                        SafeReleaseComObject(detailCircleObject);
                        tempDraw.ClearSelection2(true);
                    }
                }
            }
            finally
            {
                tempDraw.ClearSelection2(true);
                SafeReleaseComObject(extension);
                SafeReleaseComObject(activeView);
            }
        }

        private static void DeleteSectionView(
            IModelDoc2 tempDraw,
            IView drawingView)
        {
            if (tempDraw == null || drawingView == null) return;

            IView activeView = null;
            ModelDocExtension extension = null;
            object[] sectionLines = null;
            try
            {
                tempDraw.ClearSelection2(true);
                IDrawingDoc drawing = tempDraw as IDrawingDoc;
                drawing?.ActivateView(drawingView.GetName2());
                activeView = drawing?.ActiveDrawingView as IView;
                if (activeView == null) return;

                extension = tempDraw.Extension as ModelDocExtension;
                sectionLines = activeView.GetSectionLines() as object[];
                double[] position = activeView.Position as double[];
                if (extension == null ||
                    sectionLines == null ||
                    position == null ||
                    position.Length < 2)
                    return;

                foreach (object sectionLineObject in sectionLines)
                {
                    DrSection sectionLine = sectionLineObject as DrSection;
                    try
                    {
                        if (sectionLine == null) continue;
                        bool selected = extension.SelectByID2(
                            sectionLine.GetName(),
                            "SECTIONLINE",
                            position[0],
                            position[1],
                            0,
                            false,
                            0,
                            null,
                            0);
                        if (!selected)
                        {
                            selected = extension.SelectByID2(
                                sectionLine.GetName(),
                                "断面線",
                                position[0],
                                position[1],
                                0,
                                false,
                                0,
                                null,
                                0);
                        }

                        if (selected)
                        {
                            extension.DeleteSelection2(
                                (int)swDeleteSelectionOptions_e
                                    .swDelete_Absorbed);
                        }
                    }
                    finally
                    {
                        SafeReleaseComObject(sectionLineObject);
                        tempDraw.ClearSelection2(true);
                    }
                }
            }
            finally
            {
                tempDraw.ClearSelection2(true);
                SafeReleaseComObject(extension);
                SafeReleaseComObject(activeView);
            }
        }
        private static void RemoveTables(IModelDoc2 model, IView view)
        {
            var swModelDocExt = (IModelDocExtension)model.Extension;
            object[] vTableAnns = view.GetTableAnnotations() as object[];
            model.ClearSelection2(true);
            if (vTableAnns != null && vTableAnns.Length > 0)
            {
                foreach (object tableObj in vTableAnns)
                {
                    TableAnnotation table = (TableAnnotation)tableObj;
                    // Select the table

                }
                model.DeleteSelection(true);
            }
        }
        private static void DeleteBreakView(IModelDoc2 tempDraw, IView swView)
        {
            SelectionMgr swSelMgr = (SelectionMgr)tempDraw.SelectionManager;
            SelectData swSelData = swSelMgr.CreateSelectData();
            swSelData.View = (View)swView;
            object[] swBreakLines = swView.GetBreakLines() as object[];

            tempDraw.ClearSelection2(true);
            if (swBreakLines != null)
            {
                foreach (IBreakLine item in swBreakLines)
                {
                    item.Select(true, swSelData);
                }
                tempDraw.DeleteSelection(true);
            }

        }


        private static void DeleteSketchBlockInstances(IModelDoc2 draw, IView swView)
        {
            var swSketchMgr = (ISketchManager)draw.SketchManager;
            draw.ClearSelection2(true);
            if (swSketchMgr.GetSketchBlockDefinitions() is object[] vblockdef && vblockdef.Length > 0)
            {
                for (int i = 0; i < vblockdef.Length; i++)
                {
                    var swBlockDef = (ISketchBlockDefinition)vblockdef.GetValue(i);
                    var swFeat = swBlockDef.GetFeature();
                    swFeat.Select2(true, -1);
                }
                draw.DeleteSelection(true);
            }

        }


        private static void RemoveDimensions(IModelDoc2 swDrawx, IView view)
        {
            var swDraw = (IDrawingDoc)swDrawx;
            var swSheet = (ISheet)swDraw.GetCurrentSheet();
            var drawingViewName = view.GetName2();
            var status = swDraw.ActivateView(drawingViewName);
            var swDispDims = new IDisplayDimension[0];
            var swView = view;
            var swDispDim = (IDisplayDimension)swView.GetFirstDisplayDimension5();
            while (swDispDim != null)
            {
                if (swDispDims.Length == 0)
                {
                    Array.Resize(ref swDispDims, 1);
                }
                else
                {
                    Array.Resize(ref swDispDims, swDispDims.Length + 1);
                }
                swDispDims[swDispDims.Length - 1] = swDispDim;
                swDispDim = (IDisplayDimension)swDispDim.GetNext5();
            }



            swDrawx.ClearSelection2(true);
            long selCount = swDrawx.Extension.MultiSelect2(swDispDims, false, null);

            if (selCount == swDispDims.Length)
            {
                swDrawx.DeleteSelection(true);
            }
            swDrawx.ClearSelection2(true);
        }
        private static void Deletesketchentities(IModelDoc2 model, IView swView)
        {
            // Delete sketch entities
            SelectionMgr swSelMgr = (SelectionMgr)model.SelectionManager;

            ISketch sketch = swView.IGetSketch();
            object[] vSketchTextSegments = sketch.GetSketchSegments() as object[];
            if (vSketchTextSegments != null)
            {
                foreach (object vSketchTextSegment in vSketchTextSegments)
                {
                    if (vSketchTextSegment is Entity swSketchText)
                    {
                        SelectData swSelData = swSelMgr.CreateSelectData();
                        swSketchText.Select4(false, swSelData);
                        model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                    }

                }
            }
        }

        private static void Deleteannotations(IModelDoc2 swModel, IView swView)
        {
            // Delete annotations
            object[] annotations = swView.GetAnnotations() as object[];
            if (annotations != null)
            {
                foreach (object annotationObj in annotations)
                {
                    Annotation annotation = (Annotation)annotationObj;
                    if (annotation != null)
                    {

                        // Select the annotation
                        bool isSelected = annotation.Select3(false, null);
                        if (isSelected)
                        {
                            // Delete the annotation
                            swModel.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                        }
                    }
                }
            }

        }

        private static void CenterMark(IModelDoc2 swModel, IView swView)
        {
            SelectionMgr swSelMgr = (SelectionMgr)swModel.SelectionManager;
            SelectData swSelData = (SelectData)swSelMgr.CreateSelectData();
            CenterMark swCtrMark = (CenterMark)swView.GetFirstCenterMark();
            while ((swCtrMark != null))
            {
                Annotation swAnn = (Annotation)swCtrMark.GetAnnotation();
                Debug.Print("    " + swAnn.GetName());
                // Select directly through annotation
                bool status = swAnn.Select3(true, swSelData);
                Debug.Assert(status);
                swCtrMark = (CenterMark)swCtrMark.GetNext();
            }
            ;
            swModel.DeleteSelection(false);
        }


        private static void FitSheetToView(ISheet sheet, IView swView)
        {
            var vViewOutline = swView.GetOutline() as double[];
            sheet.SetSize((int)swDwgPaperSizes_e.swDwgPapersUserDefined, vViewOutline[2] - vViewOutline[0], vViewOutline[3] - vViewOutline[1]);
            var vPos = (double[])swView.Position;
            vPos[0] -= vViewOutline[0];
            vPos[1] -= vViewOutline[1];
            swView.Position = vPos;
        }



        private static void BoudingBox(IModelDoc2 swModel, IView wView)
        {
            var swDraw = (IDrawingDoc)swModel;
            var drawingViewName = wView.GetName2();
            var status = swDraw.ActivateView(drawingViewName);
            var swView = (IView)swDraw.ActiveDrawingView;
            var refDoc = swView.ReferencedDocument;
            var pathDoc = refDoc.GetPathName();
            string bboxname = DxfModelAccess.GetBoundingBoxSketchName(refDoc);
            var name = ViewHelper.GetPartNameUltraSafe(DxfExportContext.App, swView);
            var FullSketchName = bboxname + "@" + name + "@" + drawingViewName;
            status = swModel.Extension.SelectByID2(FullSketchName, "SKETCH", 0, 0, 0, false, 0, null, 0);
            if (status)
            {
                swModel.BlankSketch();
            }
        }


        /// <summary>
        /// Creates and assigns the manufacturing layers on the disposable
        /// Drawing only. The source Drawing and Part are never changed.
        /// Assigning the root drawing component is O(1); bend lines are the
        /// only entities enumerated, and only when the user exports them.
        /// </summary>
        private static void ApplyTemporaryDrawingLayers(
            IModelDoc2 model,
            IView view,
            bool includeBendLines,
            bool includeLaserMarks,
            DXFPropforExport properties,
            DxfManufacturingLayerSettings layerSettings,
            bool recognizeCutExtrudeAsLaserMark)
        {
            if (model == null || view == null) return;

            DxfManufacturingLayerSettings settings =
                DxfManufacturingLayerSettings.Normalize(layerSettings);

            IDrawingDoc drawing = model as IDrawingDoc;
            LayerMgr layerManager = null;
            try
            {
                layerManager = model.IGetLayerManager();
                if (drawing == null || layerManager == null)
                {
                    DxfTrace.Debug(
                        "Temporary DXF Drawing does not expose its layer manager.");
                    return;
                }

                bool cutLayerReady = EnsureTemporaryDrawingLayer(
                    drawing,
                    layerManager,
                    settings.Cut.Name,
                    "Flat-pattern cutting geometry",
                    LayerToSolidWorksColor(settings.Cut));
                bool infoLayerReady = EnsureTemporaryDrawingLayer(
                    drawing,
                    layerManager,
                    settings.PartInfo.Name,
                    "Part information text",
                    LayerToSolidWorksColor(settings.PartInfo));
                bool bendLayerReady = !includeBendLines ||
                    EnsureTemporaryDrawingLayer(
                        drawing,
                        layerManager,
                        settings.Bend.Name,
                        "Sheet-metal bend lines",
                        LayerToSolidWorksColor(settings.Bend),
                        (int)swLineStyles_e.swLineCENTER);
                bool laserLayerReady = !includeLaserMarks ||
                    EnsureTemporaryDrawingLayer(
                        drawing,
                        layerManager,
                        settings.LaserMark.Name,
                        "Laser marking geometry",
                        LayerToSolidWorksColor(settings.LaserMark));
                bool keepDrawingSketchMarks = includeLaserMarks &&
                    properties?.DrawingViewMarkSketches?.Items.Count > 0;
                bool needMarkerLayer = includeLaserMarks &&
                    (recognizeCutExtrudeAsLaserMark || keepDrawingSketchMarks);
                bool cutExtrudeMarkerLayerReady =
                    !needMarkerLayer ||
                    EnsureTemporaryDrawingLayer(
                        drawing,
                        layerManager,
                        DxfLayerFinalizer.CutExtrudeMarkerLayerName,
                        "Temporary LASER_MARK marker geometry",
                        LayerToSolidWorksColor(settings.LaserMark));

                // Wrong marker arcs (read back and rejected) are parked here;
                // the DXF finalizer deletes everything on this layer.
                if (needMarkerLayer &&
                    EnsureTemporaryDrawingLayer(
                        drawing,
                        layerManager,
                        DxfLayerFinalizer.MarkDiscardLayerName,
                        "Temporary discarded marker geometry",
                        LayerToSolidWorksColor(settings.LaserMark)))
                {
                    if (properties != null)
                        properties.MarkDiscardLayerName =
                            DxfLayerFinalizer.MarkDiscardLayerName;
                }

                bool cutAssigned = cutLayerReady &&
                    TryAssignDrawingComponentLayer(
                    view,
                    settings.Cut.Name);

                // Countersunk / counterbored holes: the laser cuts only the
                // through bore. The mouth rim is engraved (LASER_MARK: the
                // workshop marks where to countersink) or, without laser
                // marks, erased. The finalizer removes its CUT copy.
                bool engraveMouths = includeLaserMarks && laserLayerReady &&
                    EnsureTemporaryDrawingLayer(
                        drawing,
                        layerManager,
                        DxfLayerFinalizer.CutExtrudeMarkerLayerName,
                        "Temporary LASER_MARK marker geometry",
                        LayerToSolidWorksColor(settings.LaserMark));
                if (cutLayerReady &&
                    (engraveMouths ||
                     EnsureTemporaryDrawingLayer(
                        drawing,
                        layerManager,
                        DxfLayerFinalizer.MarkEraseLayerName,
                        "Temporary erased geometry",
                        LayerToSolidWorksColor(settings.Cut))))
                {
                    HandleStepHoleMouths(
                        model,
                        view,
                        engraveMouths
                            ? DxfLayerFinalizer.CutExtrudeMarkerLayerName
                            : DxfLayerFinalizer.MarkEraseLayerName,
                        engraveMouths,
                        properties);
                }
                int cutExtrudeMarkCount = 0;
                if (includeLaserMarks &&
                    recognizeCutExtrudeAsLaserMark &&
                    laserLayerReady &&
                    cutExtrudeMarkerLayerReady)
                {
                    // Inner rims of a slanted MARK hole are drawn here; the
                    // finalizer deletes them together with their CUT copy.
                    bool eraseLayerReady = EnsureTemporaryDrawingLayer(
                        drawing,
                        layerManager,
                        DxfLayerFinalizer.MarkEraseLayerName,
                        "Temporary erased MARK rim geometry",
                        LayerToSolidWorksColor(settings.LaserMark));
                    cutExtrudeMarkCount = AssignCutExtrudeLaserMarkLayer(
                        model,
                        view,
                        DxfLayerFinalizer.CutExtrudeMarkerLayerName,
                        properties,
                        eraseLayerReady ? DxfLayerFinalizer.MarkEraseLayerName : null);
                }
                else if (includeLaserMarks &&
                         (!laserLayerReady ||
                          (needMarkerLayer &&
                           !cutExtrudeMarkerLayerReady)))
                {
                    ErrorEvents.LogError(
                        $"File:{properties?.FileName}-{properties?.PartNumber}: " +
                        "LỖI LASER_MARK: không tạo được layer " +
                        (!laserLayerReady
                            ? settings.LaserMark.Name
                            : DxfLayerFinalizer.CutExtrudeMarkerLayerName) +
                        " trên Drawing tạm; DXF có thể thiếu hoặc sai " +
                        "layer LASER_MARK.");
                }

                int drawingSketchMarkCount = 0;
                if (keepDrawingSketchMarks &&
                    laserLayerReady &&
                    cutExtrudeMarkerLayerReady)
                {
                    drawingSketchMarkCount = CreateDrawingViewMarkSketchGeometry(
                        model,
                        view,
                        DxfLayerFinalizer.CutExtrudeMarkerLayerName,
                        properties);
                }
                ReportMarkerArcStats(properties);
                int bendCount = 0;
                if (includeBendLines && bendLayerReady)
                {
                    bendCount = AssignBendLineLayer(
                        view,
                        settings.Bend);
                }

                DxfTrace.Debug(
                    $"File:{properties?.FileName}-{properties?.PartNumber}: " +
                    "temporary Drawing layers prepared; " +
                    $"{settings.Cut.Name}=" +
                    $"{(cutAssigned ? "assigned" : "unavailable")}, " +
                    $"{settings.Bend.Name}={bendCount}, " +
                    $"{settings.LaserMark.Name}=" +
                    $"{(laserLayerReady ? "ready" : "unavailable")}, " +
                    $"marked-Cut-Extrude={cutExtrudeMarkCount}, " +
                    $"drawing-sketch-MARK={drawingSketchMarkCount}, " +
                    $"{settings.PartInfo.Name}=" +
                    $"{(infoLayerReady ? "ready" : "unavailable")}.");
            }
            catch (Exception ex)
            {
                // Layer metadata must never stop a previously valid geometry
                // export. Keep the original SaveAs workflow as a safe fallback,
                // but make the problem visible in the Error Log.
                ErrorEvents.LogError(
                    $"File:{properties?.FileName}-{properties?.PartNumber}: " +
                    "cannot prepare temporary Drawing layers " +
                    "(CUT/BEND/LASER_MARK may be wrong): " + ex.Message);
            }
            finally
            {
                SafeReleaseComObject(layerManager);
            }
        }

        private static int LayerToSolidWorksColor(DxfLayerMapOption option)
        {
            int rgb = option?.TrueColorRgb ?? DxfPalette.Rgb(option?.Color ?? 7);
            return ((rgb >> 16) & 0xFF) | (rgb & 0xFF00) | ((rgb & 0xFF) << 16);
        }
        private static bool EnsureTemporaryDrawingLayer(
            IDrawingDoc drawing,
            ILayerMgr layerManager,
            string layerName,
            string description,
            int color = 0,
            int lineStyle = (int)swLineStyles_e.swLineCONTINUOUS)
        {
            object existingLayer = null;
            try
            {
                existingLayer = layerManager.GetLayer(layerName);
                if (existingLayer != null) return true;

                bool created = drawing.CreateLayer2(
                    layerName,
                    description,
                    color,
                    lineStyle,
                    (int)swLineWeights_e.swLW_NORMAL,
                    true,
                    true);
                if (created) return true;

                // Some templates create the layer even when CreateLayer2
                // reports false. Read it back before treating that as failure.
                existingLayer = layerManager.GetLayer(layerName);
                return existingLayer != null;
            }
            catch (COMException ex)
            {
                DxfTrace.Debug(
                    "Cannot create temporary Drawing layer '" + layerName +
                    "': " + ex.Message);
                return false;
            }
            finally
            {
                SafeReleaseComObject(existingLayer);
            }
        }

        private static bool TryAssignDrawingComponentLayer(
            IView view,
            string layerName)
        {
            DrawingComponent rootComponent = null;
            try
            {
                rootComponent = view.RootDrawingComponent;
                if (rootComponent == null) return false;

                rootComponent.Layer = layerName;
                rootComponent.LayerOverride = 0;
                return string.Equals(
                    rootComponent.Layer,
                    layerName,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (COMException ex)
            {
                DxfTrace.Debug(
                    "Cannot assign FlatPattern component to layer '" +
                    layerName + "': " + ex.Message);
                return false;
            }
            finally
            {
                SafeReleaseComObject(rootComponent);
            }
        }

        private static int AssignBendLineLayer(
            IView view,
            DxfLayerMapOption bendLayer)
        {
            string layerName = bendLayer.Name;
            object[] bendLines = null;
            int assignedCount = 0;
            try
            {
                bendLines = view.GetBendLines() as object[];
                if (bendLines == null) return 0;

                foreach (object bendLineObject in bendLines)
                {
                    ISketchSegment bendLine =
                        bendLineObject as ISketchSegment;
                    try
                    {
                        if (bendLine == null) continue;
                        bendLine.Layer = layerName;
                        bendLine.LayerOverride = 0;
                        bendLine.Style =
                            (int)swLineStyles_e.swLineCENTER;
                        assignedCount++;
                    }
                    catch (COMException ex)
                    {
                        DxfTrace.Debug(
                            "Cannot assign a bend line to layer '" +
                            layerName + "': " + ex.Message);
                    }
                    finally
                    {
                        SafeReleaseComObject(bendLineObject);
                    }
                }

                return assignedCount;
            }
            catch (COMException ex)
            {
                DxfTrace.Debug(
                    "Cannot enumerate FlatPattern bend lines: " + ex.Message);
                return assignedCount;
            }
        }

        internal sealed class VisibleEdgeReference
        {
            public IEdge Edge { get; set; }
            public Component2 Component { get; set; }

            /// <summary>Feature MARK sở hữu (Resolution.SourceName); "" khi chưa nhận.</summary>
            public string MarkSource { get; set; } = "";

            /// <summary>Cạnh miệng lỗ: một mặt kề không thuộc MARK (xem <see cref="MarkRimCandidate.IsMouth"/>).</summary>
            public bool IsMouth { get; set; } = true;

            /// <summary>Khoá các mặt thành CONG thuộc MARK kề cạnh này.</summary>
            public List<string> WallKeys { get; } = new List<string>();
        }

        /// <summary>
        /// Mirrors only the visible model edges that belong to a MARK feature
        /// into the private marker layer in the disposable Drawing. Model
        /// IEdge objects have no drawing-layer setter, so the marker is created
        /// as an ISketchSegment; the DXF finalizer later maps it to LASER_MARK
        /// and removes the CUT copy. Eligible features carry the explicit MARK
        /// token in their name (Cut-ExtrudeMARK01, Cut-Extrude-MARK-01,
        /// HoleWzd-MARK...) and are a Cut-Extrude, a Hole Wizard/Simple Hole,
        /// another material-removal cut, or a feature pattern/mirror of such a
        /// feature (see <see cref="LaserMarkFeatureRecognizer"/>). The source
        /// Part and source Drawing are never modified.
        /// </summary>
        private static int AssignCutExtrudeLaserMarkLayer(
            IModelDoc2 model,
            IView view,
            string layerName,
            DXFPropforExport properties,
            string eraseLayerName = null,
            Action<string> reportHidden = null)
        {
            if (model == null || view == null ||
                string.IsNullOrWhiteSpace(layerName))
                return 0;

            string logPrefix =
                $"File:{properties?.FileName}-{properties?.PartNumber}: ";
            var recognizer = new LaserMarkFeatureRecognizer();
            var visibleMarkFeatureNames =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var recognizedDescriptions =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var markerFailures =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int markedEdgeCount = 0;
            int markerCount = 0;
            int erasedRimCount = 0;
            int recoveredEdgeCount = 0;
            var unmarkedEdges = new List<VisibleEdgeReference>();
            try
            {
                List<VisibleEdgeReference> visibleEntityList = CollectVisibleEdges(view);

                // A model IEdge is not a drawing entity.  It can be selected
                // in a view, but SolidWorks has no Layer setter on IEdge and
                // SelectedEdgeProperties(string) only *names* an edge (it
                // never changes its drawing layer).  Keep the marked edges
                // and mirror their 2D geometry into the view sketch instead;
                // ISketchSegment.Layer is the supported per-entity API and
                // survives the temporary Drawing -> DXF export.
                List<VisibleEdgeReference> markedEdges =
                    new List<VisibleEdgeReference>();
                // A marked Cut-Extrude can expose more than one model edge
                // (for example the two sides of a groove).  Every visible
                // edge belonging to that MARK feature must be mirrored to
                // LASER_MARK.  Cut-Extrude features without MARK are never
                // included and therefore remain on CUT.
                HashSet<string> markedEdgeIds =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (VisibleEdgeReference visibleEntity in visibleEntityList)
                {
                    IEdge edge = visibleEntity?.Edge;
                    if (edge == null) continue;

                    try
                    {
                        string componentKey = "PART";
                        try
                        {
                            componentKey = visibleEntity.Component == null
                                ? "PART"
                                : (visibleEntity.Component.Name2 ??
                                   visibleEntity.Component.Name ?? "ASSEMBLY");
                        }
                        catch { }

                        if (!recognizer.TryResolve(
                                edge,
                                out LaserMarkFeatureRecognizer.Resolution mark))
                        {
                            unmarkedEdges.Add(visibleEntity);
                            continue;
                        }

                        // Both the owning pattern and its MARK seed count as
                        // visible for the hidden-mark report below.
                        visibleMarkFeatureNames.Add(mark.SourceName);
                        visibleMarkFeatureNames.Add(mark.MarkedName);

                        int solidWorksEdgeId = edge.GetID();
                        string edgeId = solidWorksEdgeId > 0
                            ? componentKey + "|ID|" + solidWorksEdgeId
                            // Some Drawing views expose temporary/projected
                            // edges with GetID()==0. Do not collapse all such
                            // edges into a single candidate.
                            : componentKey + "|RCW|" +
                              RuntimeHelpers.GetHashCode(edge);
                        // The Part fallback query can return the same edge as
                        // the component query. Do not create duplicate marker
                        // geometry for that edge.
                        if (markedEdgeIds.Add(edgeId))
                        {
                            DescribeMarkFaces(edge, recognizer, mark.SourceName, visibleEntity);
                            markedEdges.Add(visibleEntity);
                            string description = mark.Description;
                            recognizedDescriptions[description] =
                                recognizedDescriptions.TryGetValue(
                                    description,
                                    out int count)
                                    ? count + 1
                                    : 1;
                        }
                    }
                    catch (COMException ex)
                    {
                        DxfTrace.Debug("Cannot inspect MARK feature edge for " +
                            "LASER_MARK: " + ex.Message);
                    }
                }

                // A MARK feature whose edges were not attributed to it through
                // the face owner (flattened faces can report another owner)
                // is matched again by GEOMETRY: its own face edges against
                // the still-unmarked visible edges of the view.
                recoveredEdgeCount = RecoverMarkEdgesByGeometry(
                    view,
                    recognizer,
                    unmarkedEdges,
                    markedEdges,
                    visibleMarkFeatureNames,
                    recognizedDescriptions,
                    logPrefix);

                // Safety net: an edge of the part OUTLINE (outer loop of a
                // sheet face) is never engraved, or the part would not be cut
                // free. Seen on a lofted rolled sheet whose outline edges were
                // attributed to a MARK feature.
                int outlineDropped = DropOutlineEdges(markedEdges, recognizer, logPrefix);

                // An ordinary (rivet) hole cut AGAIN by a MARK feature in the
                // flat configuration is not a mark: keep it on CUT.
                DropOrdinaryHolesRecutByMark(view, markedEdges, recognizer, logPrefix);
                if (outlineDropped > 0)
                {
                    ErrorEvents.LogError(
                        logPrefix + "CẢNH BÁO LASER_MARK: " + outlineDropped +
                        " cạnh biên ngoài của chi tiết bị nhận nhầm là MARK — " +
                        "đã giữ ở layer CUT (biên ngoài luôn phải cắt).");
                }

                markedEdgeCount = markedEdges.Count;
                if (markedEdgeCount > 0)
                {
                    markerCount = CreateCutExtrudeLaserMarkSketchGeometry(
                        model,
                        view,
                        markedEdges,
                        layerName,
                        markerFailures,
                        properties,
                        eraseLayerName,
                        out erasedRimCount,
                        logPrefix);
                    // A dropped inner rim is not a missing marker: it was
                    // removed on purpose together with its CUT copy.
                    markedEdgeCount -= erasedRimCount;
                    DxfTrace.Debug(
                        "MARK feature edges mirrored to LASER_MARK sketch: " +
                        markedEdgeCount + " edge(s), " + markerCount +
                        " marker(s), " + erasedRimCount + " inner rim(s) erased.");
                }
            }
            catch (COMException ex)
            {
                ErrorEvents.LogError(
                    logPrefix +
                    "LỖI LASER_MARK: không gán được feature MARK " +
                    "sang LASER_MARK: " + ex.Message);
            }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
            }

            if (recognizedDescriptions.Count > 0)
            {
                ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][RECOGNIZED] " + markedEdgeCount + " cạnh: " +
                    string.Join("; ", recognizedDescriptions.Select(pair => pair.Key + " ×" + pair.Value)));
                DxfExportNotices.Info(
                    "LASER_MARK",
                    "Nhận dạng " + markedEdgeCount + " cạnh MARK → LASER_MARK: " +
                    string.Join("; ", recognizedDescriptions.Select(pair =>
                        pair.Key + " ×" + pair.Value)) + "." +
                    (recoveredEdgeCount > 0
                        ? " (" + recoveredEdgeCount + " cạnh nhận lại bằng đối chiếu hình học.)"
                        : ""));
            }
            if (erasedRimCount > 0)
            {
                DxfExportNotices.Info(
                    "LASER_MARK",
                    "Lỗ MARK cắt xéo có 2 vòng: giữ vòng ngoài cùng (miệng lỗ), bỏ " +
                    erasedRimCount + " vòng trong khỏi DXF.");
            }

            if (markerCount < markedEdgeCount && properties != null)
            {
                // Edges without a marker are still on CUT at this point. The
                // DXF finalizer can move such an edge when it closes a
                // LASER_MARK contour, so the verdict is reported after it.
                properties.LaserMarkMissingMarkerCount +=
                    markedEdgeCount - markerCount;
                properties.LaserMarkMissingMarkerDetail =
                    string.Join(", ", visibleMarkFeatureNames) +
                    (markerFailures.Count == 0
                        ? ""
                        : "; nguyên nhân: " + string.Join("; ",
                            markerFailures.Select(pair => pair.Key + " ×" + pair.Value)));
            }
            ReportMarkFeaturesWithoutVisibleEdges(
                view,
                recognizer,
                visibleMarkFeatureNames,
                logPrefix,
                unmarkedEdges,
                reportHidden);
            return markerCount;
        }

        /// <summary>
        /// Visible model edges of a Drawing View (every visible assembly
        /// component, or the Part itself queried with a null component).
        /// </summary>
        private static List<VisibleEdgeReference> CollectVisibleEdges(IView view)
        {
            object[] visibleComponents = null;
            try
            {
                visibleComponents =
                    view.GetVisibleComponents() as object[];
            }
            catch (COMException ex)
            {
                DxfTrace.Debug(
                    "Cannot enumerate Drawing View components: " +
                    ex.Message);
            }
            List<Component2> components = visibleComponents == null
                ? new List<Component2>()
                : visibleComponents
                    .OfType<Component2>()
                    .ToList();
            bool isPartView = components.Count == 0;

            DrawingComponent root = null;
            Component2 rootComponent = null;
            try
            {
                root = view.RootDrawingComponent;
                rootComponent = root?.Component;
            }
            catch (COMException ex)
            {
                DxfTrace.Debug(
                    "Cannot resolve Drawing View root component: " +
                    ex.Message);
            }
            if (!isPartView && rootComponent != null &&
                !components.Contains(rootComponent))
            {
                components.Add(rootComponent);
            }

            // Assembly views require a Component2. A Part Drawing (the
            // normal Flatten -> DXF case) has no visible component at all;
            // SolidWorks expects null in that case. The old implementation
            // returned here when component was null, so marked Cut-Extrude
            // edges in Part Drawings could never be classified. Enumerate
            // every visible assembly component so a mark is not missed just
            // because it belongs to a component other than the first one.
            List<VisibleEdgeReference> visibleEntityList =
                new List<VisibleEdgeReference>();
            foreach (Component2 component in components)
            {
                object[] componentEntities =
                    TryGetVisibleEdgeEntities(view, component);
                if (componentEntities == null) continue;
                foreach (object entity in componentEntities)
                {
                    IEdge edge = entity as IEdge;
                    if (edge != null)
                    {
                        visibleEntityList.Add(
                            new VisibleEdgeReference
                            {
                                Edge = edge,
                                Component = component
                            });
                    }
                }
            }

            // A Part Drawing must be queried with a null Component2. Do
            // this even when RootDrawingComponent happens to expose a
            // component RCW; that RCW can return only the outer contour
            // and omit the inner Cut-Extrude edges that represent holes.
            if (isPartView || visibleEntityList.Count == 0)
            {
                object[] partEntities =
                    TryGetVisibleEdgeEntities(view, null);
                if (partEntities != null)
                {
                    foreach (object entity in partEntities)
                    {
                        IEdge edge = entity as IEdge;
                        if (edge != null)
                        {
                            visibleEntityList.Add(
                                new VisibleEdgeReference
                                {
                                    Edge = edge,
                                    Component = null
                                });
                        }
                    }
                }
            }
            return visibleEntityList;
        }

        /// <summary>
        /// Removes from <paramref name="markedEdges"/> every edge that lies on
        /// the OUTER loop of an adjacent planar face which is not itself a
        /// MARK face (the sheet face): that edge is the part outline. Returns
        /// the number removed.
        /// </summary>
        private static int DropOutlineEdges(
            List<VisibleEdgeReference> markedEdges,
            LaserMarkFeatureRecognizer recognizer,
            string logPrefix)
        {
            if (markedEdges == null || markedEdges.Count == 0) return 0;
            var outline = new HashSet<string>(StringComparer.Ordinal);
            var checkedFaces = new HashSet<string>(StringComparer.Ordinal);
            var owners = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (VisibleEdgeReference reference in markedEdges)
            {
                foreach (IFace2 face in (reference?.Edge?.GetTwoAdjacentFaces2() as object[] ?? new object[0]).OfType<IFace2>())
                {
                    try
                    {
                        if (!IsPlanarFace(face) || recognizer.TryResolveFace(face, out _)) continue;
                        string faceKey = FaceKey(face);
                        if (faceKey.Length == 0 || !checkedFaces.Add(faceKey)) continue;
                        foreach (ILoop2 loop in (face.GetLoops() as object[] ?? new object[0]).OfType<ILoop2>())
                        {
                            if (!loop.IsOuter()) continue;
                            foreach (IEdge edge in (loop.GetEdges() as object[] ?? new object[0]).OfType<IEdge>())
                            {
                                string signature = EdgeSignature(edge);
                                if (signature.Length > 0) outline.Add(signature);
                            }
                        }
                    }
                    catch { }
                }
            }
            if (outline.Count == 0) return 0;

            int dropped = markedEdges.RemoveAll(reference =>
            {
                if (reference?.Edge == null) return false;
                if (!outline.Contains(EdgeSignature(reference.Edge))) return false;
                string source = string.IsNullOrEmpty(reference.MarkSource) ? "?" : reference.MarkSource;
                owners[source] = (owners.TryGetValue(source, out int n) ? n : 0) + 1;
                return true;
            });
            if (dropped > 0)
            {
                ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][OUTLINE] bỏ " + dropped +
                                     " cạnh biên ngoài khỏi MARK (nhận nhầm từ: " +
                                     string.Join(", ", owners.Select(pair => pair.Key + " ×" + pair.Value)) + ").");
            }
            return dropped;
        }

        /// <summary>
        /// The DXF follows the face shown by the source view, so a MARK
        /// feature that does not go through the sheet and lies on the
        /// hidden face has no visible edge. Without this check it would just
        /// be missing from the DXF. Log every unsuppressed MARK feature of the
        /// referenced Part that contributed no visible edge (after the
        /// geometric recovery), with a diagnostic line in the debug log that
        /// says why: how many faces the feature has in the open configuration
        /// and which features own the still-unmarked circular view edges.
        /// </summary>
        private static void ReportMarkFeaturesWithoutVisibleEdges(
            IView view,
            LaserMarkFeatureRecognizer recognizer,
            ICollection<string> visibleMarkFeatureNames,
            string logPrefix,
            IEnumerable<VisibleEdgeReference> unmarkedEdges = null,
            Action<string> reportHidden = null)
        {
            try
            {
                if (!(view?.ReferencedDocument is ModelDoc2 part) ||
                    part.GetType() != (int)swDocumentTypes_e.swDocPART)
                {
                    DxfTrace.Debug(
                        logPrefix + "LASER_MARK visibility check skipped: " +
                        "the view does not reference a Part.");
                    return;
                }

                var hidden = FindHiddenMarkFeatures(view, recognizer, visibleMarkFeatureNames);
                if (hidden.Count == 0) return;

                string diagnostics = DescribeHiddenMarkDiagnostics(hidden, unmarkedEdges);
                ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][HIDDEN] " + diagnostics);
                if (reportHidden != null)
                {
                    // View -> DXF: any view can be exported (a side view never
                    // shows the marks), so tell the user in the result message
                    // instead of the Error Log.
                    reportHidden(hidden.Count + " feature MARK không có cạnh nào trên view này (" +
                                 string.Join(", ", hidden.Select(h => h.Mark.Description)) + ")");
                    return;
                }
                // The name decides: a hole drilled again in the flat-pattern
                // environment is a mark only when THAT feature is named MARK.
                // When one is, it stands for the MARK hole it replaced.
                string recut = VisibleMarkAfterFlatPattern(part, visibleMarkFeatureNames);
                if (recut.Length > 0)
                {
                    DxfExportNotices.Info(
                        "LASER_MARK",
                        hidden.Count + " feature MARK của mô hình gập không còn cạnh trên hình trải (" +
                        string.Join(", ", hidden.Select(h => h.Mark.Description)) +
                        "); mark lấy theo feature ở môi trường trải: " + recut + ".");
                    return;
                }
                ErrorEvents.LogError(
                    logPrefix +
                    "CẢNH BÁO LASER_MARK: " + hidden.Count +
                    " feature MARK không có cạnh nào hiển thị trên " +
                    "view nên DXF sẽ THIẾU mark: " +
                    string.Join(", ", hidden.Select(h => h.Mark.Description)) +
                    " (đã thử cả đối chiếu hình học). Nếu lỗ đã được khoan lại " +
                    "ở môi trường trải thì đặt MARK trên lỗ khoan lại đó; nếu " +
                    "mark khắc không xuyên nằm ở mặt bị che thì kiểm tra mặt hiển thị " +
                    "(Flip view) trên Drawing gốc. Chẩn đoán: " + diagnostics);
                // A mark that is missing from the DXF must not pass as OK.
                BomDxfFileResult file = DxfExportContext.CurrentFile;
                if (file != null) file.WarningCount++;
            }
            catch (Exception ex)
            {
                ErrorEvents.LogError(
                    logPrefix +
                    "LỖI LASER_MARK: không kiểm tra được feature MARK " +
                    "bị che trên view: " + ex.Message);
            }
        }

        /// <summary>
        /// Names of the MARK features with visible edges that come after the
        /// Flat-Pattern feature in the tree (cut in the flat-pattern
        /// environment), comma separated; empty when there is none.
        /// </summary>
        private static string VisibleMarkAfterFlatPattern(
            ModelDoc2 part,
            ICollection<string> visibleMarkFeatureNames)
        {
            var names = new List<string>();
            bool afterFlatPattern = false;
            for (Feature feature = part.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
            {
                if (string.Equals(feature.GetTypeName2(), "FlatPattern", StringComparison.OrdinalIgnoreCase))
                    afterFlatPattern = true;
                else if (afterFlatPattern && visibleMarkFeatureNames.Contains(feature.Name ?? ""))
                    names.Add(feature.Name);
            }
            return string.Join(", ", names);
        }

        private static bool IsFeatureSuppressedIn(
            Feature feature,
            string configuration)
        {
            if (!string.IsNullOrWhiteSpace(configuration))
            {
                object states = feature.IsSuppressed2(
                    (int)swInConfigurationOpts_e.swSpecifyConfiguration,
                    new[] { configuration });
                if (states is bool[] boolStates && boolStates.Length > 0)
                    return boolStates[0];
                if (states is object[] objectStates &&
                    objectStates.Length > 0 &&
                    objectStates[0] is bool suppressed)
                    return suppressed;
            }
            return feature.IsSuppressed();
        }

        private static int CreateCutExtrudeLaserMarkSketchGeometry(
            IModelDoc2 model,
            IView view,
            IEnumerable<VisibleEdgeReference> markedEdges,
            string layerName,
            IDictionary<string, int> failures,
            DXFPropforExport properties,
            string eraseLayerName,
            out int erasedCount,
            string logPrefix)
        {
            erasedCount = 0;
            if (model == null || view == null ||
                string.IsNullOrWhiteSpace(layerName))
                return 0;

            var markerContext = new MarkerCreationContext(model, view, properties);
            IDrawingDoc drawing = model as IDrawingDoc;
            ISketch viewSketch = null;
            SketchManager sketchManager = null;
            MathUtility mathUtility = null;
            MathTransform modelToView = null;
            MathTransform modelToSketch = null;
            int createdCount = 0;
            bool oldAddToDb = false;
            try
            {
                if (drawing == null) return 0;
                drawing.ActivateView(view.GetName2());
                viewSketch = view.IGetSketch();
                sketchManager = model.SketchManager as SketchManager;
                mathUtility = DxfExportContext.App?.GetMathUtility() as MathUtility;
                modelToView = view.ModelToViewTransform;
                modelToSketch = viewSketch?.ModelToSketchTransform;
                if (viewSketch == null || sketchManager == null ||
                    mathUtility == null || modelToView == null ||
                    modelToSketch == null)
                    return 0;

                oldAddToDb = sketchManager.AddToDB;
                sketchManager.AddToDB = true;

                // Lỗ MARK cắt xéo có 2 vòng: vòng trong được vẽ lên layer XOÁ
                // (finalizer xoá cả nó lẫn bản CUT bên dưới), chỉ vòng ngoài
                // lên LASER_MARK. Không có layer xoá thì giữ cả hai như cũ —
                // khắc thừa một vòng còn hơn để vòng trong lại trên CUT.
                List<VisibleEdgeReference> edgeList =
                    (markedEdges ?? Enumerable.Empty<VisibleEdgeReference>()).ToList();
                HashSet<int> dropped = string.IsNullOrWhiteSpace(eraseLayerName)
                    ? new HashSet<int>()
                    : SelectDroppedMarkRims(edgeList, modelToView, modelToSketch, mathUtility, logPrefix);

                for (int edgeIndex = 0; edgeIndex < edgeList.Count; edgeIndex++)
                {
                    VisibleEdgeReference edgeReference = edgeList[edgeIndex];
                    string failure;
                    try
                    {
                        if (dropped.Contains(edgeIndex))
                        {
                            int erased = CreateMarkerForEdge(
                                edgeReference?.Edge,
                                edgeReference?.Component,
                                sketchManager,
                                modelToView,
                                modelToSketch,
                                mathUtility,
                                eraseLayerName,
                                markerContext,
                                out failure);
                            if (erased > 0)
                            {
                                erasedCount++;
                                continue;
                            }
                            ErrorEvents.LogDebug(
                                logPrefix + "[LASER_MARK][RIM] không vẽ được vòng xoá (" + failure +
                                ") — giữ vòng này trên LASER_MARK.");
                        }
                        createdCount += CreateMarkerForEdge(
                            edgeReference?.Edge,
                            edgeReference?.Component,
                            sketchManager,
                            modelToView,
                            modelToSketch,
                            mathUtility,
                            layerName,
                            markerContext,
                            out failure);
                    }
                    catch (Exception ex)
                    {
                        failure = ex.GetType().Name + ": " + ex.Message;
                        DxfTrace.Debug(
                            "Cannot mirror marked feature geometry: " +
                            ex.Message);
                    }

                    if (!string.IsNullOrWhiteSpace(failure))
                    {
                        try
                        {
                            ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][MARKER] không dựng được marker (" + failure +
                                "), cạnh dài " + (EdgeLength(edgeReference?.Edge) * 1000).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                                " mm, MARK=" + (edgeReference?.MarkSource ?? ""));
                        }
                        catch { }
                    }
                    if (!string.IsNullOrWhiteSpace(failure) && failures != null)
                    {
                        failures[failure] = failures.TryGetValue(failure, out int count)
                            ? count + 1
                            : 1;
                    }
                }
                return createdCount;
            }
            catch (COMException ex)
            {
                DxfTrace.Debug(
                    "Cannot create LASER_MARK view sketch: " + ex.Message);
                return createdCount;
            }
            finally
            {
                if (sketchManager != null)
                {
                    try { sketchManager.AddToDB = oldAddToDb; } catch { }
                }
                SafeReleaseComObject(viewSketch);
                SafeReleaseComObject(sketchManager);
                SafeReleaseComObject(mathUtility);
                SafeReleaseComObject(modelToView);
                SafeReleaseComObject(modelToSketch);
            }
        }

        // Model units are metres. 0.01 mm chord error keeps a tessellated
        // mark (text/spline/ellipse edge) inside the 0.02 mm overlap tolerance
        // used by DxfLayerFinalizer when it removes the CUT copy.
        private const double MarkerChordToleranceMeters = 1e-5;
        private const double MarkerTessellationLengthMeters = 1e-4;
        private const double EdgePointToleranceMeters = 1e-6;
        private const int MaxMarkerPolylinePoints = 5000;

        private static int CreateMarkerForEdge(
            IEdge edge,
            Component2 component,
            SketchManager sketchManager,
            MathTransform modelToView,
            MathTransform modelToSketch,
            MathUtility mathUtility,
            string layerName,
            MarkerCreationContext markerContext,
            out string failure)
        {
            failure = null;
            if (edge == null || sketchManager == null)
            {
                failure = "no edge";
                return 0;
            }

            ICurve curve = edge.GetCurve() as ICurve;
            if (curve == null)
            {
                failure = "no curve";
                return 0;
            }

            var segments = new List<ISketchSegment>();
            int coveredPieces = 0;
            try
            {
                double[] parameters = edge.GetCurveParams2() as double[];
                if (parameters == null || parameters.Length < 8)
                {
                    failure = "no curve parameters";
                    return 0;
                }

                double[] modelStart =
                    { parameters[0], parameters[1], parameters[2] };
                double[] modelEnd =
                    { parameters[3], parameters[4], parameters[5] };
                double[] start = TransformModelPoint(
                    modelStart, component, modelToView, modelToSketch, mathUtility);
                double[] end = TransformModelPoint(
                    modelEnd, component, modelToView, modelToSketch, mathUtility);
                if (!IsUsableSketchPoint(start) || !IsUsableSketchPoint(end))
                {
                    failure = "edge end point cannot be transformed";
                    return 0;
                }

                // An edge running through the sheet thickness (corner of a
                // MARK slot) is seen end-on: two distinct model points, one
                // point on the view. There is nothing to engrave and no CUT
                // copy of it in the DXF, so it is not a missing marker.
                if (Distance3D(modelStart, modelEnd) > 1e-6 &&
                    Distance2D(start, end) <= 1e-7 &&
                    IsSeenEndOn(curve, parameters, start, component,
                        modelToView, modelToSketch, mathUtility))
                {
                    return 1;
                }

                double[] circle = null;
                double[] center = null;
                double radius = 0;
                bool isCircle = !curve.IsLine() && curve.IsCircle();
                if (isCircle)
                {
                    circle = curve.CircleParams as double[];
                    if (circle != null && circle.Length >= 7)
                    {
                        center = TransformModelPoint(
                            new[] { circle[0], circle[1], circle[2] },
                            component, modelToView, modelToSketch, mathUtility);
                        radius = IsUsableSketchPoint(center)
                            ? Distance2D(center, start)
                            : 0;
                    }
                    // Without usable circle data the edge is sampled below.
                    isCircle = radius > 1e-9;
                }

                if (curve.IsLine())
                {
                    // The back-face copy of a through MARK edge (or a
                    // zero-length edge piece) maps onto a marker that is
                    // already in the sketch: SOLIDWORKS returns no new line,
                    // which used to be reported as a MARK edge left on CUT.
                    if (markerContext != null &&
                        markerContext.HasLineMarker(start, end))
                        return 1;

                    AddSegment(segments, sketchManager.CreateLine(
                        start[0], start[1], 0,
                        end[0], end[1], 0));
                    if (segments.Count > 0)
                        markerContext?.AddLineMarker(start, end);
                }
                else if (isCircle)
                {
                    double deltaU = Math.Abs(parameters[7] - parameters[6]);
                    if (Distance2D(start, end) <= 1e-7 ||
                        deltaU >= (2.0 * Math.PI - 1e-5))
                    {
                        // Read back after creation; a circle that SOLIDWORKS
                        // drew with another centre/radius is replaced by the
                        // sampled polyline below.
                        TryCreateVerifiedCircleMarker(
                            markerContext, sketchManager, center, radius, segments);
                    }
                    else
                    {
                        // A point that lies ON the edge fixes the arc side.
                        // The old start/end cross product picked the wrong
                        // side for arcs of 180° and more (slot ends), so the
                        // mark bulged the other way and did not cover CUT.
                        double[] modelMid = FindCircularEdgeMidPoint(
                            edge, circle, modelStart, modelEnd);
                        if (modelMid == null)
                        {
                            // GetClosestPointOn is not always answered for a
                            // drawing-view edge: take the curve at the middle
                            // of the edge's own parameter range instead.
                            modelMid = FindArcMidPointByParameter(
                                curve, circle, parameters, modelStart, modelEnd);
                        }
                        double[] mid = modelMid == null
                            ? null
                            : TransformModelPoint(
                                modelMid, component, modelToView,
                                modelToSketch, mathUtility);
                        if (IsUsableSketchPoint(mid))
                        {
                            // Built, read back and compared with the edge. A
                            // wrong arc is discarded and the next construction
                            // (or the sampled polyline below) takes over, so a
                            // MARK arc never stays on CUT.
                            TryCreateVerifiedArcMarker(
                                markerContext, sketchManager,
                                start, end, mid, segments);
                        }
                        // Without any point on the edge the side of the arc
                        // is unknown (a centre arc guessed from the end
                        // points is wrong for 180° and more): use the
                        // sampled polyline, which follows the real edge.
                    }
                }
                else
                {
                    // Spline (engraved text, edges of a rolled sheet after
                    // Flatten), ellipse and other curves: mirror a polyline
                    // sampled on the edge.
                    coveredPieces += AddPolylineSegments(
                        segments,
                        SampleCurvedEdge(edge, curve, parameters, modelStart, modelEnd),
                        component, sketchManager, modelToView,
                        modelToSketch, mathUtility, markerContext);
                }

                // SolidWorks can reject the exact sketch entity (for example a
                // 3-point arc of a huge-radius rolled-sheet edge, whose points
                // are almost collinear). Fall back to the sampled polyline
                // instead of leaving the edge on CUT.
                if (segments.Count == 0 && coveredPieces == 0 && !curve.IsLine())
                {
                    coveredPieces += AddPolylineSegments(
                        segments,
                        SampleCurvedEdge(edge, curve, parameters, modelStart, modelEnd),
                        component, sketchManager, modelToView,
                        modelToSketch, mathUtility, markerContext);
                }

                // Every piece of the sampled edge is already drawn (the
                // back-face copy of a through MARK edge): nothing is missing.
                if (segments.Count == 0 && coveredPieces > 0)
                    return 1;

                if (segments.Count == 0)
                {
                    failure = "curve type " + SafeCurveIdentity(curve);
                    return 0;
                }

                bool allAssigned = true;
                foreach (ISketchSegment segment in segments)
                {
                    segment.Layer = layerName;
                    segment.LayerOverride = 0;
                    allAssigned &= string.Equals(
                        segment.Layer,
                        layerName,
                        StringComparison.OrdinalIgnoreCase);
                }
                if (!allAssigned)
                    failure = "layer not assigned, curve type " + SafeCurveIdentity(curve);
                return allAssigned ? 1 : 0;
            }
            finally
            {
                foreach (ISketchSegment segment in segments)
                    SafeReleaseComObject(segment);
                SafeReleaseComObject(curve);
            }
        }

        private static void AddSegment(
            ICollection<ISketchSegment> segments,
            SketchSegment segment)
        {
            if (segment != null) segments.Add(segment);
        }

        private static string SafeCurveIdentity(ICurve curve)
        {
            try
            {
                int identity = curve.Identity();
                return Enum.IsDefined(typeof(swCurveTypes_e), identity)
                    ? ((swCurveTypes_e)identity).ToString()
                    : identity.ToString();
            }
            catch
            {
                return "?";
            }
        }

        /// <returns>Pieces not drawn because the same marker line already exists.</returns>
        private static int AddPolylineSegments(
            ICollection<ISketchSegment> segments,
            List<double[]> modelPoints,
            Component2 component,
            SketchManager sketchManager,
            MathTransform modelToView,
            MathTransform modelToSketch,
            MathUtility mathUtility,
            MarkerCreationContext markerContext = null)
        {
            if (modelPoints == null || modelPoints.Count < 2) return 0;

            var sketchPoints = new List<double[]>(modelPoints.Count);
            foreach (double[] modelPoint in modelPoints)
            {
                double[] point = TransformModelPoint(
                    modelPoint, component, modelToView,
                    modelToSketch, mathUtility);
                if (!IsUsableSketchPoint(point)) return 0;
                if (sketchPoints.Count > 0 &&
                    Distance2D(sketchPoints[sketchPoints.Count - 1], point) <= 1e-9)
                    continue;
                sketchPoints.Add(point);
            }

            int covered = 0;
            for (int index = 1; index < sketchPoints.Count; index++)
            {
                double[] from = sketchPoints[index - 1];
                double[] to = sketchPoints[index];
                if (markerContext != null && markerContext.HasLineMarker(from, to))
                {
                    covered++;
                    continue;
                }
                int before = segments.Count;
                AddSegment(segments, sketchManager.CreateLine(
                    from[0], from[1], 0,
                    to[0], to[1], 0));
                if (segments.Count > before) markerContext?.AddLineMarker(from, to);
            }
            return covered;
        }

        /// <summary>
        /// Points on a curved edge from its start to its end. GetTessPts is
        /// tried first; if the curve type does not support it (trimmed/SP
        /// curves of a flattened rolled sheet), the curve is evaluated at
        /// evenly spaced parameters. End points are snapped to the exact edge
        /// vertices so the marker joins its neighbouring markers.
        /// </summary>
        private static List<double[]> SampleCurvedEdge(
            IEdge edge,
            ICurve curve,
            double[] parameters,
            double[] modelStart,
            double[] modelEnd)
        {
            List<double[]> points =
                TessellateEdge(edge, curve, modelStart, modelEnd);
            if (points == null || points.Count < 2)
                points = EvaluateEdgeByParameter(curve, parameters, modelStart, modelEnd);
            if (points == null || points.Count < 2) return null;

            if (Distance3(points[0], modelStart) > Distance3(points[0], modelEnd))
                points.Reverse();
            points[0] = modelStart;
            points[points.Count - 1] = modelEnd;
            return points;
        }

        private static List<double[]> EvaluateEdgeByParameter(
            ICurve curve,
            double[] parameters,
            double[] modelStart,
            double[] modelEnd)
        {
            double t0 = parameters[6];
            double t1 = parameters[7];
            if (double.IsNaN(t0) || double.IsNaN(t1) ||
                Math.Abs(t1 - t0) <= 1e-12)
                return null;

            int count = 64;
            try
            {
                double length = curve.GetLength3(Math.Min(t0, t1), Math.Max(t0, t1));
                if (length > 0 && !double.IsInfinity(length))
                {
                    count = (int)Math.Ceiling(length / (2 * MarkerTessellationLengthMeters));
                }
            }
            catch
            {
            }
            count = Math.Max(8, Math.Min(MaxMarkerPolylinePoints, count));

            var points = new List<double[]>(count + 1);
            try
            {
                for (int index = 0; index <= count; index++)
                {
                    double parameter = t0 + ((t1 - t0) * index / count);
                    double[] value = curve.Evaluate2(parameter, 0) as double[];
                    if (value == null || value.Length < 3) return null;
                    points.Add(new[] { value[0], value[1], value[2] });
                }
            }
            catch (Exception ex)
            {
                DxfTrace.Debug("Cannot evaluate MARK edge: " + ex.Message);
                return null;
            }

            // The parameter range must really describe this edge.
            double startGap = Math.Min(
                Distance3(points[0], modelStart),
                Distance3(points[0], modelEnd));
            double endGap = Math.Min(
                Distance3(points[points.Count - 1], modelStart),
                Distance3(points[points.Count - 1], modelEnd));
            return startGap <= 1e-4 && endGap <= 1e-4 ? points : null;
        }

        private static double Distance3(double[] first, double[] second)
        {
            double dx = first[0] - second[0];
            double dy = first[1] - second[1];
            double dz = first[2] - second[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static double[] FindCircularEdgeMidPoint(
            IEdge edge,
            double[] circle,
            double[] start,
            double[] end)
        {
            double[] center = { circle[0], circle[1], circle[2] };
            double[] axis = { circle[3], circle[4], circle[5] };
            double radius = circle[6];
            double[] startDirection = Normalize3(Subtract3(start, center));
            double[] endDirection = Normalize3(Subtract3(end, center));
            if (startDirection == null || endDirection == null || radius <= 0)
                return null;

            double[] bisector = Normalize3(new[]
            {
                startDirection[0] + endDirection[0],
                startDirection[1] + endDirection[1],
                startDirection[2] + endDirection[2]
            });
            if (bisector == null)
            {
                // Exactly 180°: the candidates are perpendicular to the chord
                // in the circle plane.
                bisector = Normalize3(Cross3(axis, startDirection));
                if (bisector == null) return null;
            }

            double[] first =
            {
                center[0] + radius * bisector[0],
                center[1] + radius * bisector[1],
                center[2] + radius * bisector[2]
            };
            double[] second =
            {
                center[0] - radius * bisector[0],
                center[1] - radius * bisector[1],
                center[2] - radius * bisector[2]
            };
            double firstDistance = DistanceToEdge(edge, first);
            double secondDistance = DistanceToEdge(edge, second);
            if (double.IsInfinity(firstDistance) &&
                double.IsInfinity(secondDistance))
                return null;
            return firstDistance <= secondDistance ? first : second;
        }

        private static List<double[]> TessellateEdge(
            IEdge edge,
            ICurve curve,
            double[] start,
            double[] end)
        {
            List<double[]> points = TessellateCurve(curve, start, end);
            if (points == null || points.Count <= 2) return points;

            // For a closed curve GetTessPts follows the curve direction, which
            // may be the complement of this edge. Keep the run whose middle
            // point really lies on the edge.
            double limit = Math.Max(
                EdgePointToleranceMeters,
                10 * MarkerChordToleranceMeters);
            if (DistanceToEdge(edge, points[points.Count / 2]) <= limit)
                return points;

            List<double[]> reversed = TessellateCurve(curve, end, start);
            if (reversed != null && reversed.Count > 2 &&
                DistanceToEdge(edge, reversed[reversed.Count / 2]) <= limit)
            {
                reversed.Reverse();
                return reversed;
            }
            return points;
        }

        private static List<double[]> TessellateCurve(
            ICurve curve,
            double[] start,
            double[] end)
        {
            try
            {
                double[] raw = curve.GetTessPts(
                    MarkerChordToleranceMeters,
                    MarkerTessellationLengthMeters,
                    start,
                    end) as double[];
                if (raw == null || raw.Length < 6 ||
                    raw.Length / 3 > MaxMarkerPolylinePoints)
                    return null;

                var points = new List<double[]>(raw.Length / 3);
                for (int index = 0; index + 2 < raw.Length; index += 3)
                    points.Add(new[] { raw[index], raw[index + 1], raw[index + 2] });
                return points;
            }
            catch (Exception ex)
            {
                DxfTrace.Debug("Cannot tessellate MARK edge: " + ex.Message);
                return null;
            }
        }

        private static double DistanceToEdge(IEdge edge, double[] point)
        {
            try
            {
                double[] closest =
                    edge.GetClosestPointOn(point[0], point[1], point[2]) as double[];
                if (closest == null || closest.Length < 3)
                    return double.PositiveInfinity;
                double dx = closest[0] - point[0];
                double dy = closest[1] - point[1];
                double dz = closest[2] - point[2];
                return Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            catch
            {
                return double.PositiveInfinity;
            }
        }

        private static double[] Subtract3(double[] first, double[] second)
        {
            return new[]
            {
                first[0] - second[0],
                first[1] - second[1],
                first[2] - second[2]
            };
        }

        private static double[] Cross3(double[] first, double[] second)
        {
            return new[]
            {
                first[1] * second[2] - first[2] * second[1],
                first[2] * second[0] - first[0] * second[2],
                first[0] * second[1] - first[1] * second[0]
            };
        }

        private static double[] Normalize3(double[] vector)
        {
            double length = Math.Sqrt(
                vector[0] * vector[0] +
                vector[1] * vector[1] +
                vector[2] * vector[2]);
            if (length <= 1e-12) return null;
            return new[]
            {
                vector[0] / length,
                vector[1] / length,
                vector[2] / length
            };
        }

        private static double[] TransformModelPoint(
            double[] point,
            Component2 component,
            MathTransform modelToView,
            MathTransform modelToSketch,
            MathUtility mathUtility)
        {
            if (point == null || point.Length < 3 ||
                modelToView == null || modelToSketch == null ||
                mathUtility == null)
                return null;

            MathPoint mathPoint = mathUtility.CreatePoint(point) as MathPoint;
            if (mathPoint == null) return null;
            try
            {
                if (component != null)
                {
                    MathTransform componentTransform = component.Transform2;
                    if (componentTransform != null)
                        mathPoint = mathPoint.MultiplyTransform(
                            componentTransform) as MathPoint;
                    SafeReleaseComObject(componentTransform);
                }
                mathPoint = mathPoint.MultiplyTransform(modelToView) as MathPoint;
                mathPoint = mathPoint?.MultiplyTransform(modelToSketch) as MathPoint;
                return mathPoint?.ArrayData as double[];
            }
            finally
            {
                SafeReleaseComObject(mathPoint);
            }
        }

        private static bool IsUsableSketchPoint(double[] point)
        {
            return point != null && point.Length >= 2 &&
                !double.IsNaN(point[0]) && !double.IsNaN(point[1]) &&
                !double.IsInfinity(point[0]) && !double.IsInfinity(point[1]);
        }

        private static double Distance3D(double[] first, double[] second)
        {
            double dx = first[0] - second[0];
            double dy = first[1] - second[1];
            double dz = first[2] - second[2];
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        /// <summary>The middle of the edge projects onto its (coincident) end points too.</summary>
        private static bool IsSeenEndOn(
            ICurve curve,
            double[] parameters,
            double[] sketchStart,
            Component2 component,
            MathTransform modelToView,
            MathTransform modelToSketch,
            MathUtility mathUtility)
        {
            try
            {
                double middle = (parameters[6] + parameters[7]) / 2;
                if (!(curve.Evaluate2(middle, 0) is double[] point) || point.Length < 3) return false;
                double[] projected = TransformModelPoint(
                    new[] { point[0], point[1], point[2] },
                    component, modelToView, modelToSketch, mathUtility);
                return IsUsableSketchPoint(projected) &&
                       Distance2D(projected, sketchStart) <= 1e-6;
            }
            catch
            {
                return false;
            }
        }

        private static double Distance2D(double[] first, double[] second)
        {
            if (!IsUsableSketchPoint(first) || !IsUsableSketchPoint(second))
                return double.PositiveInfinity;
            double dx = first[0] - second[0];
            double dy = first[1] - second[1];
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static object[] TryGetVisibleEdgeEntities(
            IView view,
            Component2 component)
        {
            if (view == null) return null;
            try
            {
                object[] entities = view.GetVisibleEntities2(
                    component,
                    (int)swViewEntityType_e.swViewEntityType_Edge)
                    as object[];
                if (entities != null && entities.Length > 0)
                    return entities;

                // Older SolidWorks releases occasionally return an empty
                // SAFEARRAY from the *2 accessor even though the legacy
                // accessor still exposes the visible edges.
                return view.GetVisibleEntities(
                    component,
                    (int)swViewEntityType_e.swViewEntityType_Edge)
                    as object[];
            }
            catch (COMException ex)
            {
                DxfTrace.Debug(
                    "Cannot enumerate Drawing View edges for " +
                    (component == null ? "Part" : "Assembly") +
                    ": " + ex.Message);
                return null;
            }
        }

        private static void AddNoQtyTxt(
            IModelDoc2 model,
            DXFPropforExport dXFPropforExport,
            DxfManufacturingLayerSettings layerSettings)
        {
            IDrawingDoc draw = model as IDrawingDoc;
            if (draw == null) return;
            DxfManufacturingLayerSettings settings =
                DxfManufacturingLayerSettings.Normalize(layerSettings);
            string partInfoLayer = settings.PartInfo.Name;
            string partInfoFont = settings.PartInfoFontName;

            CreateTextOnLayer(
                draw,
                "PartNumber" + ": " + dXFPropforExport.PartNumber,
                -0.1,
                partInfoLayer,
                partInfoFont);
            CreateTextOnLayer(
                draw,
                "Material" + ": " + dXFPropforExport.material + " " +
                dXFPropforExport.Thickness + "t",
                -0.2,
                partInfoLayer,
                partInfoFont);
            CreateTextOnLayer(
                draw,
                "Quantity" + ": " + dXFPropforExport.QTY,
                -0.3,
                partInfoLayer,
                partInfoFont);
            CreateTextOnLayer(
                draw,
                "UnitName" + ": " + dXFPropforExport.UnitName,
                -0.4,
                partInfoLayer,
                partInfoFont);
        }

        private static void CreateTextOnLayer(
            IDrawingDoc drawing,
            string text,
            double y,
            string layerName,
            string fontName)
        {
            Note note = null;
            Annotation annotation = null;
            try
            {
                note = drawing.ICreateText2(text, 0, y, 0, 0.07, 0);
                annotation = note?.IGetAnnotation();
                if (annotation == null) return;

                annotation.Layer = layerName;
                annotation.LayerOverride = 0;
                ApplyAnnotationFont(annotation, fontName, layerName);
            }
            catch (COMException ex)
            {
                // Keep the text even when an older SOLIDWORKS version rejects
                // its layer metadata.
                DxfTrace.Debug(
                    "Cannot assign DXF information text to layer '" +
                    layerName + "': " + ex.Message);
            }
            finally
            {
                SafeReleaseComObject(annotation);
                SafeReleaseComObject(note);
            }
        }

        private static void ApplyAnnotationFont(
            Annotation annotation,
            string fontName,
            string layerName)
        {
            if (annotation == null || string.IsNullOrWhiteSpace(fontName))
                return;

            object formatObject = null;
            try
            {
                // Preserve height, width factor and spacing from ICreateText2;
                // only replace the installed Windows font family.
                formatObject = annotation.GetTextFormat(0);
                ITextFormat textFormat = formatObject as ITextFormat;
                if (textFormat == null) return;

                textFormat.TypeFaceName = fontName.Trim();
                if (!annotation.SetTextFormat(0, false, formatObject))
                {
                    DxfTrace.Debug(
                        "SOLIDWORKS rejected PART_INFO font '" +
                        fontName + "' on layer '" + layerName + "'.");
                }
            }
            catch (COMException ex)
            {
                // Font metadata is optional. Never stop a valid DXF geometry
                // export when a font disappears or a SOLIDWORKS version does
                // not accept the requested typeface.
                DxfTrace.Debug(
                    "Cannot apply PART_INFO font '" + fontName +
                    "' on layer '" + layerName + "': " + ex.Message);
            }
            finally
            {
                SafeReleaseComObject(formatObject);
            }
        }


        private static void SwDisplayHiden(IModelDoc2 draw, int bendINT, int sketchDisplayINT)
        {
            bool bend = false;
            bool sketchDisplay = false;
            if (bendINT == 1)
            {
                bend = true;
            }
            if (sketchDisplayINT == 1)
            {
                sketchDisplay = true;
            }
            draw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplaySketches, sketchDisplay);
            draw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swEdgesHighQualityDisplay, true);
            draw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayBendLines, bend);
            draw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayAllAnnotations, false);
            draw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDrawingDisplayViewBorders, false);
            draw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayDatums, false);
            draw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayNotes, false);
            draw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swViewDispGlobalBBox, false);
            draw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDrawingDisplayViewBorders, false);
            draw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayCurves, false);
        }


    }
}
