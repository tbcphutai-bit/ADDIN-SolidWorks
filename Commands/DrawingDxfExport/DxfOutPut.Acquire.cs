// Adapted from user-supplied DXF.zip; see SOURCE.md.
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Lấy hình thô cho DXF theo phương án 06/10/2026 (docs/dxf-export-phan-tich-2026-10-06.md):
    /// BẢN SAO file bản vẽ mở ẩn → giữ một view → đọc «ý nghĩa» từ model
    /// (cạnh MARK, đường uốn, vòng xoá, sketch MARK) thành <see cref="DxfSemanticPlan"/>
    /// → ẩn chú thích, component → CUT → SaveAs DXF. Không clipboard, không
    /// sketch tạm, không map, không đụng tài liệu của người dùng.
    /// </summary>
    internal partial class DxfOutPut
    {
        /// <summary>Bản sao bản vẽ đang mở ẩn; Dispose đóng và xoá file tạm.</summary>
        internal sealed class DrawingCopy : IDisposable
        {
            public ISldWorks App;
            public IModelDoc2 Model;
            public string Path;
            public string Directory;

            public void Dispose()
            {
                IModelDoc2 model = Model;
                Model = null;
                if (model != null)
                {
                    try { model.ClearSelection2(true); } catch { }
                    ModelDoc2 modelDoc = model as ModelDoc2;
                    CloseTemporaryDrawing(ref modelDoc);
                }
                try
                {
                    if (!string.IsNullOrEmpty(Directory) && System.IO.Directory.Exists(Directory))
                        System.IO.Directory.Delete(Directory, true);
                }
                catch (Exception ex)
                {
                    DxfTrace.Debug("Cannot delete DXF drawing copy: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Chép Drawing đã lưu vào %TEMP%; dùng SaveAs Copy khi có thay đổi
        /// chưa lưu hoặc chưa đọc được trạng thái để mang theo sketch MARK.
        /// </summary>
        internal static DrawingCopy OpenDrawingCopy(ISldWorks app, IModelDoc2 sourceDrawing, string sourcePath)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Không thấy bản vẽ.", sourcePath);

            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TAI_TOOL", "DxfExport", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            // SolidWorks từ chối mở tài liệu TRÙNG TIÊU ĐỀ với tài liệu đang mở
            // (swFileWithSameTitleAlreadyOpen = 65536): bản sao phải mang tên khác.
            string copyPath = System.IO.Path.Combine(
                directory,
                System.IO.Path.GetFileNameWithoutExtension(sourcePath) + "~tai-dxf" + Guid.NewGuid().ToString("N").Substring(0, 8) + System.IO.Path.GetExtension(sourcePath));
            var copy = new DrawingCopy { App = app, Path = copyPath, Directory = directory };

            DxfTrace.Info("bản vẽ gốc: " + DxfTrace.DescribeFile(sourcePath));
            bool? dirty = null;
            try { if (sourceDrawing != null) dirty = sourceDrawing.GetSaveFlag(); } catch { }
            if (dirty == true) DxfTrace.Info("bản vẽ gốc có thay đổi CHƯA LƯU — bản sao SaveAs mang theo chúng.");

            bool savedCopy = false;
            if (dirty == false || sourceDrawing == null)
            {
                try {
                    File.Copy(sourcePath, copyPath, true);
                    File.SetAttributes(copyPath, FileAttributes.Normal);
                }
                catch { copy.Dispose(); throw; }
                savedCopy = true;
                DxfTrace.Info("Drawing không có thay đổi chưa lưu: chép trực tiếp bản sao trên đĩa.");
            }
            else
            {
                try
                {
                    int e = 0, w = 0;
                    bool api = sourceDrawing.Extension.SaveAs3(copyPath,
                        (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                        (int)(swSaveAsOptions_e.swSaveAsOptions_Silent | swSaveAsOptions_e.swSaveAsOptions_Copy),
                        null, null, ref e, ref w);
                    savedCopy = api && e == 0 && File.Exists(copyPath) && new FileInfo(copyPath).Length > 0;
                    DxfTrace.Info("SaveAs3(Copy) bản vẽ → " + copyPath + ": api=" + api + ", " + DescribeSaveResult(e, w) +
                                  ", file " + (File.Exists(copyPath) ? "có" : "KHÔNG có"));
                }
                catch (Exception ex)
                {
                    DxfTrace.Error("SaveAs3(Copy) bản vẽ ném lỗi", ex);
                }
            }
            if (!savedCopy)
            {
                copy.Dispose();
                throw new InvalidOperationException("Không tạo được bản sao Drawing có thay đổi chưa lưu. Hãy lưu Drawing rồi xuất lại.");
            }

            // Mở HIỆN: bản vẽ mở ẩn (DocumentVisible=false) không có đồ hoạ nên
            // GetVisibleEntities2 trả rỗng và SaveAs DXF báo lỗi (đo 06/10/2026).
            int errors = 0, warnings = 0;
            var openClock = Stopwatch.StartNew();
            copy.Model = app.OpenDoc6(copyPath, (int)swDocumentTypes_e.swDocDRAWING,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings) as IModelDoc2;
            DxfTrace.Info("OpenDoc6 bản sao: " + (copy.Model != null ? "OK" : "NULL") + ", " + DescribeLoadResult(errors, warnings) +
                          ", " + openClock.ElapsedMilliseconds + " ms");
            if (copy.Model == null)
            {
                copy.Dispose();
                throw new InvalidOperationException("Không mở được bản sao bản vẽ (" + DescribeLoadResult(errors, warnings) + ").");
            }
            return copy;
        }

        /// <summary>
        /// Giữ sheet và cây feature cập nhật trên bản sao để người dùng theo dõi
        /// khi dọn bản vẽ và SaveAs DXF. Chỉ tác động bản sao tạm, không đổi
        /// trạng thái đồ họa của Drawing nguồn. Màu model được xử lý riêng.
        /// </summary>
        internal static void EnableRedraw(DrawingCopy copy)
        {
            if (copy?.Model == null) return;
            try
            {
                if (copy.Model.ActiveView is IModelView view) view.EnableGraphicsUpdate = true;
                IFeatureManager tree = copy.Model.FeatureManager;
                tree.EnableFeatureTree = true;
                tree.EnableFeatureTreeWindow = true;
                copy.Model.GraphicsRedraw2();
                DxfTrace.Info("bản sao: giữ bật đồ họa sheet và cây feature trong lúc xuất DXF.");
            }
            catch (Exception ex) { DxfTrace.Error("bật đồ họa trên bản sao", ex); }
        }

        /// <summary>
        /// Kế hoạch ngữ nghĩa phải mô tả đúng view mà SolidWorks vừa lưu. View đổi ở giữa (bản vẽ cũ hơn
        /// Part, chỉ được cập nhật bởi lần dựng lại trong SaveRawDxf) thì nét mới không có quyết định
        /// layer: dòng phải thành WARN, không được qua như OK.
        /// </summary>
        internal static void WarnWhenViewChangedSincePlan(IModelDoc2 copy, string viewName, DxfSemanticPlan plan)
        {
            if (copy == null || plan == null || plan.ViewEdgeCount < 0) return;
            try
            {
                IView view = FindViewByName(copy, viewName);
                if (view == null) return;
                int now = CollectVisibleEdges(view).Count;
                DxfTrace.Value("cạnh của view: lúc đọc / sau khi lưu DXF", plan.ViewEdgeCount + " / " + now);
                if (now == plan.ViewEdgeCount) return;
                DxfExportNotices.Warning("Xuất DXF",
                    "View trải phẳng đổi trong lúc xuất: lúc đọc có " + plan.ViewEdgeCount + " cạnh, lúc lưu DXF có " + now +
                    " cạnh. Bản vẽ chưa cập nhật theo Part nên layer của nét mới có thể sai (lỗ MARK có thể nằm ở CUT). " +
                    "Mở bản vẽ cho nó cập nhật, lưu lại rồi xuất lại dòng này.");
            }
            catch (Exception ex) { DxfTrace.Debug("Cannot compare the view with the plan: " + ex.Message); }
        }

        /// <summary>
        /// Trên bản sao: kích hoạt sheet chứa view, xoá mọi sheet khác, xoá mọi
        /// view khác (view phụ thuộc trước). <paramref name="keepAncestors"/>:
        /// giữ cả chuỗi view cha (view cắt/chi tiết không tồn tại thiếu cha).
        /// Trả về view (đọc lại sau khi dựng lại).
        /// </summary>
        internal static IView KeepOnlyView(IModelDoc2 copy, string sheetName, string viewName, bool keepAncestors, DXFPropforExport properties)
        {
            var drawing = copy as IDrawingDoc;
            if (drawing == null) throw new InvalidOperationException("Bản sao không phải bản vẽ.");

            DxfTrace.Info("giữ view '" + viewName + "' trên sheet '" + sheetName + "' (giữ view cha: " + keepAncestors + ")");
            if (!string.IsNullOrEmpty(sheetName))
            {
                try { DxfTrace.Value("ActivateSheet(" + sheetName + ")", drawing.ActivateSheet(sheetName)); }
                catch (Exception ex) { DxfTrace.Error("ActivateSheet('" + sheetName + "') ném lỗi", ex); }
                string[] sheets = drawing.GetSheetNames() as string[];
                if (sheets != null)
                {
                    DxfTrace.Value("sheet trên bản sao", string.Join(" | ", sheets));
                    foreach (string other in sheets)
                    {
                        if (string.Equals(other, sheetName, StringComparison.Ordinal)) continue;
                        copy.ClearSelection2(true);
                        bool selected = copy.Extension.SelectByID2(other, "SHEET", 0, 0, 0, false, 0, null, 0);
                        bool removed = selected && copy.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                        if (!removed)
                            throw new InvalidOperationException("Không xoá được sheet '" + other + "' trên bản sao Drawing.");
                    }
                    copy.ClearSelection2(true);
                    try { drawing.ActivateSheet(sheetName); } catch (Exception ex) { DxfTrace.Error("ActivateSheet lần 2 ném lỗi", ex); }
                }
            }

            if (FindViewByName(copy, viewName) == null)
                throw new InvalidOperationException("Bản sao không có view '" + viewName + "'.");

            List<DrawingViewDependency> views = GetDrawingViewDependencies(copy);
            DxfTrace.Value("view trên sheet (tên←cha)", string.Join(" | ", views.Select(v =>
                v.Name + (string.IsNullOrEmpty(v.BaseViewName) ? "" : "←" + v.BaseViewName))));
            var keep = new HashSet<string>(StringComparer.Ordinal) { viewName };
            if (keepAncestors)
            {
                var byName = views.ToDictionary(v => v.Name, StringComparer.Ordinal);
                string parent = byName.TryGetValue(viewName, out DrawingViewDependency self) ? self.BaseViewName : null;
                while (!string.IsNullOrEmpty(parent) && keep.Add(parent))
                    parent = byName.TryGetValue(parent, out DrawingViewDependency p) ? p.BaseViewName : null;
            }

            // Xoá lá trước: độ sâu phụ thuộc lớn → nhỏ.
            var doomed = views
                .Where(v => !keep.Contains(v.Name))
                .Select(v => new { View = v, Depth = DependencyDepthFromAnyRoot(v, views) })
                .OrderByDescending(x => x.Depth)
                .Select(x => x.View.Name)
                .ToList();
            foreach (string name in doomed)
            {
                if (FindViewByName(copy, name) == null) continue;   // đã đi theo cha
                copy.ClearSelection2(true);
                if (!copy.Extension.SelectByID2(name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0))
                    throw new InvalidOperationException("Không chọn được view '" + name + "' để xoá trên bản sao.");
                bool deleted = copy.Extension.DeleteSelection2(
                    (int)swDeleteSelectionOptions_e.swDelete_Children | (int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                copy.ClearSelection2(true);
                if (!deleted || FindViewByName(copy, name) != null)
                    throw new InvalidOperationException("SolidWorks không xoá được view '" + name + "' trên bản sao.");
            }
            copy.ForceRebuild3(false);

            IView view = FindViewByName(copy, viewName);
            if (view == null)
                throw new InvalidOperationException("View '" + viewName + "' mất sau khi dọn bản sao.");
            DxfTrace.Info("đã xoá " + doomed.Count + " view khác" + (doomed.Count > 0 ? " (" + string.Join(", ", doomed) + ")" : "") +
                          "; view giữ lại: " + DescribeView(view));
            if (doomed.Count > 0)
            {
                DxfExportNotices.Info("Xuất DXF",
                    "Bỏ " + doomed.Count + " view khác trên bản sao bản vẽ (bản gốc không đổi).");
            }
            return view;
        }

        private static IView FindViewByName(IModelDoc2 drawingModel, string viewName) =>
            FindViewByName(drawingModel as ModelDoc2, viewName);

        private static List<DrawingViewDependency> GetDrawingViewDependencies(IModelDoc2 drawingModel) =>
            GetDrawingViewDependencies(drawingModel as ModelDoc2);

        private static int DependencyDepthFromAnyRoot(DrawingViewDependency view, IList<DrawingViewDependency> views)
        {
            var byName = views.ToDictionary(v => v.Name, StringComparer.Ordinal);
            int depth = 0;
            string parent = view.BaseViewName;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(parent) && visited.Add(parent) && byName.TryGetValue(parent, out DrawingViewDependency p))
            {
                depth++;
                parent = p.BaseViewName;
            }
            return depth;
        }

        /// <summary>
        /// Ẩn mọi thứ không phải hình cắt (chú thích, kích thước, sketch, hộp bao,
        /// dấu tâm, đường ngắt, mặt cắt/chi tiết con), component → CUT, text PART_INFO.
        /// Gọi SAU khi đã đọc kế hoạch ngữ nghĩa (đường uốn đọc trước khi tắt hiển thị).
        /// </summary>
        internal static void PrepareViewForRawExport(
            IModelDoc2 copy,
            IView view,
            DXFPropforExport properties,
            DxfManufacturingLayerSettings layerSettings,
            bool partInfoText)
        {
            DxfManufacturingLayerSettings settings = DxfManufacturingLayerSettings.Normalize(layerSettings);
            var drawing = (IDrawingDoc)copy;
            string name = view.GetName2();

            // Khung tên / khổ giấy (sheet format) cũng là hình trong DXF: 7/7 part
            // thật bị từ chối vì DXF 280×196 mm là khung A4 (06/10). Bỏ format.
            RemoveSheetFormat(drawing);
            // DXF thô phải ở mm GIẤY (tỉ lệ view) như kế hoạch ngữ nghĩa giả định. Hai cài đặt của
            // CHÍNH bản vẽ (template người dùng) đổi điều đó: đơn vị không phải mm, và «Scale output 1:1»
            // (swDxfOutputNoScale = 1: SolidWorks chia theo tỉ lệ sheet). Đặt lại trên BẢN SAO.
            TracedStep("UnitSetup (mm)", () => DxfModelAccess.UnitSetup(copy));
            try
            {
                int noScale = copy.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swDxfOutputNoScale);
                if (noScale != 0)
                {
                    copy.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swDxfOutputNoScale, 0);
                    ErrorEvents.LogDebug("[RAW] bản vẽ bật «Scale output 1:1» (swDxfOutputNoScale=" + noScale + ") — tắt trên bản sao.");
                }
            }
            catch (Exception ex) { DxfTrace.Error("swDxfOutputNoScale trên bản sao", ex); }
            TracedStep("ẩn hiển thị (sketch/bend/chú thích)", () => SwDisplayHiden(copy, 0, 0));
            TracedStep("xoá annotation", () => Deleteannotations(copy, view));
            TracedStep("xoá kích thước", () => RemoveDimensions(copy, view));
            TracedStep("xoá block sketch", () => DeleteSketchBlockInstances(copy, view));
            TracedStep("xoá đường ngắt", () => DeleteBreakView(copy, view));
            TracedStep("xoá vòng chi tiết", () => DeleteDetailView(copy, view));
            TracedStep("xoá đường cắt", () => DeleteSectionView(copy, view));
            TracedStep("xoá sketch view", () => DeleteSketch(copy, view));
            TracedStep("xoá đối tượng sketch", () => Deletesketchentities(copy, view));
            TracedStep("ẩn hộp bao", () => BoudingBox(copy, view));
            TracedStep("xoá dấu tâm", () => CenterMark(copy, view));
            copy.ClearSelection2(true);

            ILayerMgr layerManager = copy.IGetLayerManager();
            if (layerManager != null)
            {
                if (!EnsureTemporaryDrawingLayer(drawing, layerManager, settings.Cut.Name, "Flat-pattern cutting geometry", LayerToSolidWorksColor(settings.Cut)))
                    DxfTrace.Warn("không tạo được layer '" + settings.Cut.Name + "' trên bản sao.");
                if (partInfoText &&
                    !EnsureTemporaryDrawingLayer(drawing, layerManager, settings.PartInfo.Name, "Part information text", LayerToSolidWorksColor(settings.PartInfo)))
                    DxfTrace.Warn("không tạo được layer '" + settings.PartInfo.Name + "' trên bản sao.");
            }
            else
            {
                DxfTrace.Warn("bản sao không có LayerMgr — không tạo được layer CUT/PART_INFO.");
            }
            IView fresh = FindViewByName(copy, name) ?? view;
            if (!TryAssignDrawingComponentLayer(fresh, settings.Cut.Name))
                DxfTrace.Warn("không gán được component của view vào layer '" + settings.Cut.Name + "'.");
            if (partInfoText)
                TracedStep("ghi chữ PART_INFO", () => AddNoQtyTxt(copy, properties, layerSettings));
            // Không ForceRebuild3 ở đây: SaveRawDxf (luôn gọi ngay sau) dựng lại trước khi
            // lưu — hai lần dựng lại liền nhau trên cùng bản sao chỉ tốn thời gian.
        }

        /// <summary>Bỏ sheet format (khung, khung tên) của sheet hiện hành trên bản sao; giữ khổ và tỉ lệ.</summary>
        internal static void RemoveSheetFormat(IDrawingDoc drawing)
        {
            try
            {
                ISheet sheet = drawing.GetCurrentSheet() as ISheet;
                if (sheet == null) return;
                double[] p = sheet.GetProperties() as double[];
                if (p == null || p.Length < 7) return;
                sheet.SetProperties2((int)p[0], (int)swDwgTemplates_e.swDwgTemplateNone, p[2], p[3], p[4] != 0, p[5], p[6], true);
            }
            catch (Exception ex)
            {
                DxfTrace.Debug("Cannot remove sheet format: " + ex.Message);
            }
        }

        /// <summary>
        /// Đọc «ý nghĩa» của hình từ model qua phép chiếu của chính view (mm giấy ở
        /// tỉ lệ view): mọi cạnh nhìn thấy, cạnh MARK, vòng xoá, miệng lỗ côn/bậc,
        /// đường uốn, sketch MARK người dùng vẽ.
        /// </summary>
        internal static DxfSemanticPlan BuildSemanticPlan(
            IView view,
            DXFPropforExport properties,
            bool laserMarks,
            bool featureMarks,
            bool bendLines,
            DrawingViewMarkSketchCollection userMarks,
            bool cropToView,
            Action<string> reportHidden = null)
        {
            string logPrefix = $"File:{properties?.FileName}-{properties?.PartNumber}: ";
            var plan = new DxfSemanticPlan { CropToView = cropToView };
            var clock = Stopwatch.StartNew();
            var timing = new System.Text.StringBuilder();
            Action<string> lap = name =>
            {
                timing.Append(name).Append('=').Append((clock.ElapsedMilliseconds / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append("s ");
                clock.Restart();
            };
            _phaseTiming = timing;
            _phaseClock = clock;
            try { plan.ViewScale = view.ScaleDecimal; } catch { plan.ViewScale = 1; }
            MathUtility mathUtility = DxfExportContext.App?.GetMathUtility() as MathUtility;
            MathTransform modelToView = view.ModelToViewTransform;
            if (mathUtility == null || modelToView == null)
                throw new InvalidOperationException("Không đọc được phép chiếu của view.");
            _projection = FastTransform.From(modelToView, mathUtility);
            _componentProjection = new Dictionary<Component2, FastTransform>();

            // 1. Mọi cạnh nhìn thấy (đọc MỘT lần; quét MARK và lỗ côn/bậc dùng lại).
            List<VisibleEdgeReference> visible = CollectVisibleEdges(view);
            plan.ViewEdgeCount = visible.Count;
            lap("collect");
            // Đo chi phí MỘT lệnh COM nhẹ (GetID) để biết phần «ngoài tiến trình» chiếm bao nhiêu.
            if (visible.Count > 0)
            {
                var probe = Stopwatch.StartNew();
                int probeCalls = 0;
                try { for (; probeCalls < 20; probeCalls++) visible[0].Edge.GetID(); } catch (COMException) { }
                if (probeCalls > 0)
                    timing.Append("com-call=").Append((probe.ElapsedMilliseconds / (double)probeCalls).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append("ms ");
                clock.Restart();
            }
            var sampled = new Dictionary<VisibleEdgeReference, DxfSemanticItem>();
            foreach (VisibleEdgeReference reference in visible)
            {
                DxfSemanticItem item = ProjectEdge(reference, modelToView, mathUtility, "edge");
                if (item == null) continue;
                sampled[reference] = item;
                plan.VisibleEdges.Add(item);
            }

            lap("visible");
            // 2. Cạnh MARK / vòng xoá / miệng lỗ côn-bậc.
            // Cạnh đã nhận là MARK (kể cả vòng bị bỏ) — bước «mặt tách» dưới đây không được đụng tới.
            var markReferences = new HashSet<VisibleEdgeReference>();
            if (laserMarks && featureMarks)
            {
                var recognizer = new LaserMarkFeatureRecognizer();
                MarkEdgeScan scan = ScanMarkEdges(view, recognizer, logPrefix, visible);
                markReferences.UnionWith(scan.Marked);
                ISketch viewSketch = view.IGetSketch();
                MathTransform modelToSketch = viewSketch?.ModelToSketchTransform;
                HashSet<int> droppedRims = modelToSketch == null
                    ? new HashSet<int>()
                    : SelectDroppedMarkRims(scan.Marked, modelToView, modelToSketch, mathUtility, logPrefix);
                // v150: ô/rãnh MARK xuyên cắt xéo — chỉ giữ miệng lỗ mặt gần (vành xa, cạnh tường-tường → Erase).
                HashSet<int> droppedFar = SelectDroppedFarMarkEdges(scan.Marked, modelToView, mathUtility, plan.ViewScale, logPrefix);
                for (int i = 0; i < scan.Marked.Count; i++)
                {
                    VisibleEdgeReference reference = scan.Marked[i];
                    DxfSemanticItem item = sampled.TryGetValue(reference, out DxfSemanticItem existing)
                        ? Clone(existing, reference.MarkSource)
                        : ProjectEdge(reference, modelToView, mathUtility, reference.MarkSource);
                    if (item == null) continue;
                    (droppedRims.Contains(i) || droppedFar.Contains(i) ? plan.Erase : plan.Marks).Add(item);
                }
                int partSketchSegments = ProjectPartSketchMarks(
                    view, recognizer, modelToView, mathUtility,
                    plan.UserMarks, scan.VisibleMarkFeatureNames, scan.RecognizedDescriptions);
                if (scan.RecognizedDescriptions.Count > 0)
                {
                    DxfExportNotices.Info("LASER_MARK",
                        "Nhận dạng " + (scan.Marked.Count + partSketchSegments) + " cạnh/nét MARK → LASER_MARK: " +
                        string.Join("; ", scan.RecognizedDescriptions.Select(p => p.Key + " ×" + p.Value)) + "." +
                        (scan.RecoveredEdgeCount > 0 ? " (" + scan.RecoveredEdgeCount + " cạnh nhận lại bằng đối chiếu hình học.)" : ""));
                }
                if (scan.OutlineDropped > 0)
                {
                    DxfExportNotices.Warning("LASER_MARK",
                        scan.OutlineDropped + " cạnh biên ngoài của chi tiết bị nhận nhầm là MARK — giữ ở CUT (biên ngoài luôn phải cắt).");
                }
                if (droppedRims.Count > 0)
                {
                    DxfExportNotices.Info("LASER_MARK",
                        "Lỗ MARK cắt xéo có 2 vòng: giữ vòng ngoài cùng (miệng lỗ), bỏ " + droppedRims.Count + " vòng trong khỏi DXF.");
                }
                if (droppedFar.Count > 0)
                {
                    DxfExportNotices.Info("LASER_MARK",
                        "Ô/rãnh MARK xuyên tấm cắt xéo: chỉ khắc miệng lỗ mặt gần, bỏ " + droppedFar.Count +
                        " cạnh mặt xa / trong lòng lỗ khỏi DXF (tránh hai đường khắc gần trùng làm hở góc).");
                }
                lap("marks");
                ReportMarkFeaturesWithoutVisibleEdges(view, recognizer, scan.VisibleMarkFeatureNames, logPrefix, scan.Unmarked, reportHidden);
                lap("hidden");
            }

            // Cạnh nằm GIỮA hai mặt cùng hướng về người nhìn (hai đường tiếp tuyến vùng
            // uốn khi Flat-Pattern không gộp mặt, đường tách mặt): không phải biên cắt —
            // cạnh biên luôn có một mặt kề là mặt bề dày. Không áp cho cạnh MARK (plan.Marks giữ BẢN SAO
            // của mục cạnh nên phải so theo cạnh view, không theo mục — trước 07/10 phép so không bao giờ khớp).
            foreach (VisibleEdgeReference reference in visible)
            {
                if (!sampled.TryGetValue(reference, out DxfSemanticItem edgeItem)) continue;
                if (markReferences.Contains(reference)) continue;
                if (!IsEdgeBetweenFacingFaces(reference, modelToView, mathUtility)) continue;
                plan.Erase.Add(Clone(edgeItem, "face-split"));
                plan.FaceSplitEdges++;
            }
            lap("face-split");
            if (plan.FaceSplitEdges > 0)
            {
                DxfExportNotices.Info("CUT",
                    "Bỏ " + plan.FaceSplitEdges + " cạnh nằm giữa hai mặt cùng hướng nhìn (vùng uốn / mặt tách) — không phải biên cắt.");
            }

            // Miệng lỗ côn/bậc: khắc khi xuất laser mark, không thì bỏ (chỉ cắt lỗ xuyên).
            List<StepHoleMouth> mouths = FindStepHoleMouths(view, logPrefix, visible);
            if (mouths.Count > 0)
            {
                foreach (StepHoleMouth mouth in mouths)
                {
                    // «mouth:» — bộ đặt layer không xoá lỗ xuyên bên trong miệng lỗ côn/bậc.
                    DxfSemanticItem item = sampled.TryGetValue(mouth.Edge, out DxfSemanticItem existing)
                        ? Clone(existing, "mouth:" + mouth.Kind)
                        : ProjectEdge(mouth.Edge, modelToView, mathUtility, "mouth:" + mouth.Kind);
                    if (item == null) continue;
                    (laserMarks ? plan.Marks : plan.Erase).Add(item);
                }
                string summary = string.Join("; ", mouths
                    .GroupBy(m => m.Kind + " Ø" + StepMm(m.OuterRadius * 2) + " → lỗ xuyên Ø" + StepMm(m.InnerRadius * 2))
                    .Select(g => g.Key + " ×" + g.Count()));
                DxfExportNotices.Info("CUT", laserMarks
                    ? "Lỗ côn/lỗ bậc: chỉ cắt lỗ xuyên, vòng miệng được KHẮC lên LASER_MARK để đánh dấu chỗ gia công côn/bậc (" + mouths.Count + " vòng: " + summary + ")."
                    : "Lỗ côn/lỗ bậc: chỉ cắt lỗ xuyên, bỏ " + mouths.Count + " vòng miệng khỏi DXF (" + summary + ") — tắt Laser marks nên không khắc.");
            }

            lap("step-holes");
            // 3. Đường uốn.
            if (bendLines)
            {
                object[] bends = null;
                try { bends = view.GetBendLines() as object[]; } catch (COMException) { }
                int read = 0;
                foreach (object value in bends ?? new object[0])
                {
                    DxfSemanticItem item = ProjectBendLine(value as ISketchSegment, modelToView, mathUtility);
                    if (item == null) continue;
                    plan.Bends.Add(item);
                    read++;
                }
                if (properties != null) properties.ExpectedBendLineCount = bends?.Length ?? 0;
                if (bends != null && read < bends.Length)
                {
                    DxfExportNotices.Warning("BEND",
                        (bends.Length - read) + "/" + bends.Length + " đường uốn không đọc được toạ độ — kiểm tra layer BEND trong DXF.");
                }
            }

            // 4. Sketch MARK người dùng vẽ trên view.
            if (laserMarks && userMarks != null)
            {
                foreach (DrawingViewMarkSketch sketch in userMarks.Items)
                {
                    if (sketch.ModelPoints.Count < 2) continue;
                    var item = new DxfSemanticItem { Kind = sketch.Kind, Source = sketch.LayerName };
                    foreach (double[] point in sketch.ModelPoints)
                    {
                        double[] p = ProjectPoint(point, null, modelToView, mathUtility);
                        if (p == null) { item = null; break; }
                        item.Points.Add(p);
                    }
                    if (item != null) plan.UserMarks.Add(item);
                }
            }
            lap("bends+user");
            _phaseTiming = null;
            _phaseClock = null;
            ErrorEvents.LogDebug(logPrefix + "[PLAN] visible=" + plan.VisibleEdges.Count + " marks=" + plan.Marks.Count +
                                 " erase=" + plan.Erase.Count + " bends=" + plan.Bends.Count + " user=" + plan.UserMarks.Count +
                                 " face-split=" + plan.FaceSplitEdges + " | " + timing);
            return plan;
        }

        /// <summary>
        /// Cạnh nằm trong LÒNG tấm: một mặt kề là mặt tấm (phẳng, pháp tuyến ∥ hướng
        /// nhìn) và mặt kia hoặc cũng là mặt tấm (mặt tách), hoặc là mặt VÙNG UỐN —
        /// trên view trải phẳng SolidWorks vẫn báo mặt ấy là trụ (feature BaseBend)
        /// với trục nằm TRONG mặt tấm (đo 06/10: m02). Lỗ (trụ có trục ∥ hướng nhìn)
        /// và cạnh biên (mặt bề dày, pháp tuyến ⟂ hướng nhìn) không thuộc đây.
        /// </summary>
        private static bool IsEdgeBetweenFacingFaces(VisibleEdgeReference reference, MathTransform modelToView, MathUtility mathUtility)
        {
            try
            {
                object[] faces = reference?.Edge?.GetTwoAdjacentFaces2() as object[];
                if (faces == null || faces.Length < 2) return false;
                int sheetFaces = 0, bendFaces = 0;
                foreach (IFace2 face in faces.OfType<IFace2>())
                {
                    if (!(face.GetSurface() is ISurface surface)) return false;
                    if (surface.IsPlane())
                    {
                        double[] normal = face.Normal as double[];
                        double? z = ViewZ(normal, reference.Component, modelToView, mathUtility);
                        if (z == null) return false;
                        if (Math.Abs(z.Value) >= 0.99) sheetFaces++;
                        else return false;                       // mặt bề dày → cạnh biên
                        continue;
                    }
                    if (surface.IsCylinder() && surface.CylinderParams is double[] c && c.Length >= 7)
                    {
                        double? z = ViewZ(new[] { c[3], c[4], c[5] }, reference.Component, modelToView, mathUtility);
                        if (z == null || Math.Abs(z.Value) > 0.1) return false;   // lỗ: trục ∥ hướng nhìn
                        bendFaces++;
                        continue;
                    }
                    return false;
                }
                return sheetFaces == 2 || (sheetFaces == 1 && bendFaces == 1);
            }
            catch (COMException)
            {
                return false;
            }
        }

        /// <summary>Thành phần theo hướng nhìn (đã chuẩn hoá) của một vectơ model; null khi không đọc được.</summary>
        private static double? ViewZ(double[] vector, Component2 component, MathTransform modelToView, MathUtility mathUtility)
        {
            if (vector == null || vector.Length < 3) return null;
            FastTransform fastVector = ProjectionFor(component, modelToView, mathUtility);
            if (fastVector != null)
            {
                double[] r = fastVector.Vector(vector);
                double len = Math.Sqrt(r[0] * r[0] + r[1] * r[1] + r[2] * r[2]);
                return len < 1e-12 ? (double?)null : r[2] / len;
            }
            MathVector mv = mathUtility.CreateVector(vector) as MathVector;
            if (mv == null) return null;
            if (component != null)
            {
                MathTransform ct = component.Transform2;
                if (ct != null) mv = mv.MultiplyTransform(ct) as MathVector;
            }
            mv = mv?.MultiplyTransform(modelToView) as MathVector;
            if (!(mv?.ArrayData is double[] v) || v.Length < 3) return null;
            double length = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            return length < 1e-12 ? (double?)null : v[2] / length;
        }

        private static DxfSemanticItem Clone(DxfSemanticItem source, string label)
        {
            var item = new DxfSemanticItem { Kind = source.Kind, Source = label ?? source.Source };
            foreach (double[] p in source.Points) item.Points.Add(new[] { p[0], p[1], 0.0 });
            return item;
        }

        private static DxfSemanticItem ProjectEdge(VisibleEdgeReference reference, MathTransform modelToView, MathUtility mathUtility, string label)
        {
            if (reference?.Edge == null) return null;
            var item = new DxfSemanticItem { Kind = "POLYLINE", Source = label ?? "" };
            foreach (double[] point in SampleEdge3D(reference.Edge))
            {
                double[] p = ProjectPoint(point, reference.Component, modelToView, mathUtility);
                if (p != null) item.Points.Add(p);
            }
            return item.Points.Count >= 1 ? item : null;
        }

        /// <summary>Hai đầu đường uốn (sketch của Flat-Pattern) → mm giấy.</summary>
        private static DxfSemanticItem ProjectBendLine(ISketchSegment segment, MathTransform modelToView, MathUtility mathUtility)
        {
            if (!(segment is ISketchLine line)) return null;
            try
            {
                ISketch sketch = segment.GetSketch() as ISketch;
                MathTransform sketchToModel = sketch?.ModelToSketchTransform?.Inverse() as MathTransform;
                var a = line.GetStartPoint2() as ISketchPoint;
                var b = line.GetEndPoint2() as ISketchPoint;
                if (a == null || b == null) return null;
                var item = new DxfSemanticItem { Kind = "LINE", Source = "bend" };
                foreach (ISketchPoint sp in new[] { a, b })
                {
                    double[] model = { sp.X, sp.Y, sp.Z };
                    if (sketchToModel != null)
                    {
                        var mp = mathUtility.CreatePoint(model) as MathPoint;
                        model = (mp?.MultiplyTransform(sketchToModel) as MathPoint)?.ArrayData as double[] ?? model;
                    }
                    double[] p = ProjectPoint(model, null, modelToView, mathUtility);
                    if (p == null) return null;
                    item.Points.Add(p);
                }
                return item;
            }
            catch (COMException ex)
            {
                DxfTrace.Debug("Cannot read bend line: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Chiếu các nét của feature Sketch 2D có tên chứa MARK trong Part lên view → UserMarks (LASER_MARK).
        /// </summary>
        private static int ProjectPartSketchMarks(
            IView view,
            LaserMarkFeatureRecognizer recognizer,
            MathTransform modelToView,
            MathUtility mathUtility,
            IList<DxfSemanticItem> targetMarks,
            ICollection<string> visibleMarkFeatureNames,
            IDictionary<string, int> recognizedDescriptions)
        {
            ModelDoc2 part = view?.ReferencedDocument as ModelDoc2;
            if (part == null || part.GetType() != (int)swDocumentTypes_e.swDocPART) return 0;
            string configuration = "";
            try { configuration = view.ReferencedConfiguration ?? ""; } catch (COMException) { }

            int totalAdded = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (Feature feature = part.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
            {
                string name = "";
                try { name = feature.Name ?? ""; } catch (COMException) { }
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                if (!recognizer.TryResolve(feature, out LaserMarkFeatureRecognizer.Resolution mark)) continue;
                if (mark.SourceKind != LaserMarkFeatureKind.Sketch2D) continue;
                if (!string.Equals(mark.SourceName, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsFeatureSuppressedIn(feature, configuration)) continue;

                ISketch sketch = null;
                MathTransform modelToSketch = null;
                MathTransform sketchToModel = null;
                try
                {
                    sketch = feature.GetSpecificFeature2() as ISketch;
                    if (sketch == null) continue;
                    modelToSketch = sketch.ModelToSketchTransform;
                    sketchToModel = modelToSketch?.Inverse() as MathTransform;
                    if (sketchToModel == null) continue;

                    int featureAdded = 0;
                    foreach (ISketchSegment segment in (sketch.GetSketchSegments() as object[] ?? new object[0]).OfType<ISketchSegment>())
                    {
                        bool construction = false;
                        try { construction = segment.ConstructionGeometry; } catch (COMException) { }
                        if (construction) continue;

                        if (!TryReadMarkSketchSegment(
                                segment, modelToSketch, null, mathUtility,
                                out string kind, out List<double[]> sketchPoints, out _))
                            continue;

                        var item = new DxfSemanticItem { Kind = kind, Source = name };
                        foreach (double[] sp in sketchPoints)
                        {
                            double[] modelPoint = ApplyTransform(sp, sketchToModel, mathUtility);
                            double[] viewPoint = ProjectPoint(modelPoint, null, modelToView, mathUtility);
                            if (viewPoint == null) { item = null; break; }
                            item.Points.Add(viewPoint);
                        }
                        if (item != null && item.Points.Count >= 2)
                        {
                            targetMarks?.Add(item);
                            featureAdded++;
                            totalAdded++;
                        }
                    }
                    if (featureAdded > 0)
                    {
                        visibleMarkFeatureNames?.Add(mark.SourceName);
                        visibleMarkFeatureNames?.Add(mark.MarkedName);
                        if (recognizedDescriptions != null)
                        {
                            recognizedDescriptions[mark.Description] =
                                (recognizedDescriptions.TryGetValue(mark.Description, out int n) ? n : 0) + featureAdded;
                        }
                    }
                }
                catch (COMException ex)
                {
                    DxfTrace.Debug("Cannot project Part 2D sketch MARK '" + name + "': " + ex.Message);
                }
                finally
                {
                    SafeReleaseComObject(sketchToModel);
                    SafeReleaseComObject(modelToSketch);
                }
            }
            return totalAdded;
        }

        [ThreadStatic] private static FastTransform _projection;
        [ThreadStatic] private static Dictionary<Component2, FastTransform> _componentProjection;

        /// <summary>
        /// Phép chiếu trong C# cho cạnh của <paramref name="component"/> (null = part): ghép
        /// Transform2 của component với ModelToViewTransform, mỗi phép tự kiểm bằng COM một lần
        /// rồi cache theo component. Trả null khi không dựng được → người gọi dùng đường COM.
        /// </summary>
        private static FastTransform ProjectionFor(Component2 component, MathTransform modelToView, MathUtility mathUtility)
        {
            if (_projection == null || !ReferenceEquals(_projection.Source, modelToView)) return null;
            if (component == null) return _projection;
            if (_componentProjection == null) _componentProjection = new Dictionary<Component2, FastTransform>();
            if (_componentProjection.TryGetValue(component, out FastTransform cached)) return cached;
            FastTransform result = null;
            try
            {
                MathTransform componentTransform = component.Transform2;
                if (componentTransform == null)
                {
                    result = _projection;
                }
                else
                {
                    FastTransform inner = FastTransform.From(componentTransform, mathUtility);
                    if (inner != null) result = FastTransform.Compose(inner, _projection);
                }
            }
            catch (COMException) { result = null; }
            _componentProjection[component] = result;
            return result;
        }
        [ThreadStatic] private static System.Text.StringBuilder _phaseTiming;
        [ThreadStatic] private static Stopwatch _phaseClock;

        /// <summary>Ghi mốc thời gian con vào dòng [PLAN] (chỉ khi BuildSemanticPlan đang chạy).</summary>
        internal static void Lap(string name)
        {
            if (_phaseTiming == null || _phaseClock == null) return;
            _phaseTiming.Append(name).Append('=')
                .Append((_phaseClock.ElapsedMilliseconds / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                .Append("s ");
            _phaseClock.Restart();
        }

        /// <summary>
        /// Ma trận 4×4 của SolidWorks đọc về C# để nhân điểm/vectơ không qua COM.
        /// Quy ước nhân (hàng × ma trận hay ma trận × cột) tự kiểm bằng một điểm
        /// qua COM lúc dựng — không đoán.
        /// </summary>
        internal sealed class FastTransform
        {
            private readonly double[] _m;      // 16 số của ArrayData
            private readonly bool _rowVector;  // true: x' = x·m0 + y·m3 + z·m6
            public MathTransform Source { get; }

            private FastTransform(MathTransform source, double[] m, bool rowVector)
            {
                Source = source;
                _m = m;
                _rowVector = rowVector;
            }

            public static FastTransform From(MathTransform transform, MathUtility mathUtility)
            {
                if (!(transform?.ArrayData is double[] m) || m.Length < 13) return null;
                double[] probe = { 0.123, -0.456, 0.789 };
                double[] expected = null;
                try
                {
                    expected = ((mathUtility.CreatePoint(probe) as MathPoint)?.MultiplyTransform(transform) as MathPoint)?.ArrayData as double[];
                }
                catch (COMException) { }
                if (expected == null) return null;
                foreach (bool rowVector in new[] { true, false })
                {
                    var candidate = new FastTransform(transform, m, rowVector);
                    double[] got = candidate.Point(probe);
                    if (Math.Abs(got[0] - expected[0]) < 1e-9 && Math.Abs(got[1] - expected[1]) < 1e-9 && Math.Abs(got[2] - expected[2]) < 1e-9)
                        return candidate;
                }
                return null;
            }

            public double[] Point(double[] p)
            {
                if (_first != null) return _second.Point(_first.Point(p));
                double[] r = Vector(p);
                double s = _m[12];
                return new[] { r[0] * s + _m[9], r[1] * s + _m[10], r[2] * s + _m[11] };
            }

            private readonly FastTransform _first, _second;

            private FastTransform(FastTransform first, FastTransform second)
            {
                _first = first;
                _second = second;
                Source = second.Source;
            }

            /// <summary>Áp <paramref name="first"/> rồi <paramref name="second"/>; Source = của second.</summary>
            public static FastTransform Compose(FastTransform first, FastTransform second)
            {
                if (first == null || second == null) return null;
                return new FastTransform(first, second);
            }

            /// <summary>Chỉ phần xoay (vectơ phương/pháp tuyến).</summary>
            public double[] Vector(double[] v)
            {
                if (_first != null) return _second.Vector(_first.Vector(v));
                return _rowVector
                    ? new[]
                    {
                        v[0] * _m[0] + v[1] * _m[3] + v[2] * _m[6],
                        v[0] * _m[1] + v[1] * _m[4] + v[2] * _m[7],
                        v[0] * _m[2] + v[1] * _m[5] + v[2] * _m[8]
                    }
                    : new[]
                    {
                        v[0] * _m[0] + v[1] * _m[1] + v[2] * _m[2],
                        v[0] * _m[3] + v[1] * _m[4] + v[2] * _m[5],
                        v[0] * _m[6] + v[1] * _m[7] + v[2] * _m[8]
                    };
            }
        }

        /// <summary>Điểm model (của component nếu có) → mm giấy theo phép chiếu của view.</summary>
        internal static double[] ProjectPoint(double[] point, Component2 component, MathTransform modelToView, MathUtility mathUtility)
        {
            if (point == null || point.Length < 3) return null;
            FastTransform fast = ProjectionFor(component, modelToView, mathUtility);
            if (fast != null)
            {
                double[] a = fast.Point(point);
                if (double.IsNaN(a[0]) || double.IsNaN(a[1])) return null;
                return new[] { a[0] * 1000.0, a[1] * 1000.0, 0.0 };
            }
            try
            {
                var mp = mathUtility.CreatePoint(point) as MathPoint;
                if (mp == null) return null;
                if (component != null)
                {
                    MathTransform ct = component.Transform2;
                    if (ct != null) mp = mp.MultiplyTransform(ct) as MathPoint;
                }
                mp = mp?.MultiplyTransform(modelToView) as MathPoint;
                if (!(mp?.ArrayData is double[] a) || a.Length < 2) return null;
                if (double.IsNaN(a[0]) || double.IsNaN(a[1])) return null;
                return new[] { a[0] * 1000.0, a[1] * 1000.0, 0.0 };
            }
            catch (COMException)
            {
                return null;
            }
        }

        /// <summary>SaveAs DXF/DWG bản sao: dùng layer SolidWorks, KHÔNG map; trả lại cài đặt sau.</summary>
        internal static bool SaveRawDxf(IModelDoc2 copy, string path, out int errors, out int warnings, bool? splinesAsSplines = null)
        {
            errors = 0;
            warnings = 0;
            using (DxfLayerExportPreferenceScope.Begin(null, splinesAsSplines))
            {
                try { copy.ForceRebuild3(false); } catch (Exception ex) { DxfTrace.Error("ForceRebuild3 trước SaveAs", ex); }
                bool saved = false;
                for (int attempt = 1; attempt <= 2 && !saved; attempt++)
                {
                    errors = 0;
                    warnings = 0;
                    var clock = Stopwatch.StartNew();
                    try
                    {
                        saved = copy.Extension.SaveAs(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                            (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
                    }
                    catch (COMException ex)
                    {
                        DxfTrace.Error("SaveAs " + System.IO.Path.GetExtension(path) + " lần " + attempt + " ném lỗi", ex);
                    }
                    bool usable = HasUsableExportFile(saved, errors, path);
                    DxfTrace.Info("SaveAs thô lần " + attempt + ": api=" + saved + ", " + DescribeSaveResult(errors, warnings) +
                                  ", " + clock.ElapsedMilliseconds + " ms, file: " + DxfTrace.DescribeFile(path) + (usable ? "" : " → KHÔNG DÙNG ĐƯỢC"));
                    if (usable) return true;
                    // SW đôi khi báo lỗi 1 (generic) rồi lưu được ngay lần sau (m04 06/10).
                    saved = false;
                    TryDeleteFile(path);
                    try { copy.ForceRebuild3(true); } catch (Exception ex) { DxfTrace.Error("ForceRebuild3(true) giữa hai lần SaveAs", ex); }
                }
                errors = 0;
                warnings = 0;
                saved = copy.SaveAs4(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
                bool ok = HasUsableExportFile(saved, errors, path);
                DxfTrace.Info("SaveAs4 (dự phòng): api=" + saved + ", " + DescribeSaveResult(errors, warnings) +
                              ", file: " + DxfTrace.DescribeFile(path) + (ok ? "" : " → THẤT BẠI"));
                return ok;
            }
        }

        /// <summary>
        /// Mọi view trải phẳng trên MỌI sheet của bản vẽ, kèm các tiêu chí để
        /// <see cref="FlatPatternViewSelector"/> chọn: đúng part, đang hiện, khung so với khổ giấy, cấu hình.
        /// Chỉ đọc — không kích hoạt sheet, không sửa bản vẽ.
        /// </summary>
        internal static List<FlatViewCandidate> CollectFlatViewCandidates(
            IModelDoc2 drawingModel, string partPath, string expectedConfiguration)
        {
            var list = new List<FlatViewCandidate>();
            var drawing = drawingModel as IDrawingDoc;
            if (drawing == null) return list;
            string[] sheets = drawing.GetSheetNames() as string[] ?? new string[0];
            int order = 0;
            foreach (string sheetName in sheets)
            {
                ISheet sheet = null;
                try { sheet = drawing.get_Sheet(sheetName) as ISheet; } catch (COMException) { }
                if (sheet == null) continue;
                double[] sp = null;
                try { sp = sheet.GetProperties() as double[]; } catch (COMException) { }
                double width = sp != null && sp.Length > 6 ? sp[5] : 0;
                double height = sp != null && sp.Length > 6 ? sp[6] : 0;
                object[] views = null;
                try { views = sheet.GetViews() as object[]; } catch (COMException) { }
                foreach (IView view in (views ?? new object[0]).OfType<IView>())
                {
                    bool flat = false;
                    try { flat = view.IsFlatPatternView(); } catch (COMException) { }
                    if (!flat) continue;
                    var candidate = new FlatViewCandidate { SheetName = sheetName, Order = order++ };
                    try { candidate.ViewName = view.GetName2() ?? ""; } catch (COMException) { }
                    try { candidate.Visible = view.GetVisible(); } catch (COMException) { }
                    double[] outline = null;
                    try { outline = view.GetOutline() as double[]; } catch (COMException) { }
                    if (outline != null && outline.Length >= 4 && width > 0 && height > 0)
                    {
                        double cx = (outline[0] + outline[2]) / 2, cy = (outline[1] + outline[3]) / 2;
                        candidate.CenterInsideSheet = cx >= 0 && cx <= width && cy >= 0 && cy <= height;
                        candidate.FullyInsideSheet = outline[0] >= 0 && outline[1] >= 0 && outline[2] <= width && outline[3] <= height;
                        candidate.PaperArea = Math.Abs((outline[2] - outline[0]) * (outline[3] - outline[1]));
                    }
                    string referenced = "";
                    try { referenced = view.GetReferencedModelName() ?? ""; } catch (COMException) { }
                    IModelDoc2 referencedModel = null;
                    try {
                        referencedModel = view.ReferencedDocument as IModelDoc2;
                        string canonical = referencedModel?.GetPathName();
                        if (!string.IsNullOrWhiteSpace(canonical)) referenced = canonical;
                        else if (!string.IsNullOrWhiteSpace(referenced) && !System.IO.Path.IsPathRooted(referenced))
                            referenced = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(drawingModel.GetPathName()), referenced);
                    }
                    catch (COMException) { }
                    if (!string.IsNullOrWhiteSpace(partPath) && !string.IsNullOrWhiteSpace(referenced))
                    {
                        bool samePath = false;
                        try
                        {
                            samePath = string.Equals(System.IO.Path.GetFullPath(referenced), System.IO.Path.GetFullPath(partPath), StringComparison.OrdinalIgnoreCase);
                        }
                        catch (Exception) { }
                        candidate.ReferencesPart = samePath;
                    }
                    try { candidate.ReferencedConfiguration = view.ReferencedConfiguration ?? ""; } catch (COMException) { }
                    if (!string.IsNullOrWhiteSpace(expectedConfiguration))
                    {
                        string rc = candidate.ReferencedConfiguration;
                        // Cấu hình trải phẳng SolidWorks tạo: «<cấu hình>SM-FLAT-PATTERN» (con của cấu hình ấy).
                        candidate.ConfigurationMatches =
                            DxfSourceIdentity.ConfigurationMatches(rc, expectedConfiguration);
                        if (candidate.ConfigurationMatches == false && referencedModel != null) {
                            try {
                                var configuration = referencedModel.GetConfigurationByName(rc) as IConfiguration;
                                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                while (configuration != null && visited.Add(configuration.Name)) {
                                    if (string.Equals(configuration.Name, expectedConfiguration, StringComparison.OrdinalIgnoreCase)) {
                                        candidate.ConfigurationMatches = true; break;
                                    }
                                    configuration = configuration.GetParent() as IConfiguration;
                                }
                            }
                            catch (COMException) { }
                        }
                    }
                    list.Add(candidate);
                }
            }
            DxfTrace.Info("view trải phẳng tìm thấy: " + list.Count + " trên " + sheets.Length + " sheet (" + string.Join(" | ", sheets) + ")");
            foreach (FlatViewCandidate c in list)
            {
                DxfTrace.Info("  ứng viên '" + c.SheetName + "/" + c.ViewName + "': đúngPart=" + c.ReferencesPart +
                              " hiện=" + c.Visible + " tâmTrongKhổ=" + c.CenterInsideSheet + " trọnTrongKhổ=" + c.FullyInsideSheet +
                              " cấuHình='" + c.ReferencedConfiguration + "' khớpCấuHình=" + c.ConfigurationMatches +
                              " diệnTích=" + c.PaperArea.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
            }
            return list;
        }

        /// <summary>View tên <paramref name="viewName"/> trên sheet <paramref name="sheetName"/> (không kích hoạt sheet).</summary>
        internal static IView FindViewOnSheet(IModelDoc2 drawingModel, string sheetName, string viewName)
        {
            var drawing = drawingModel as IDrawingDoc;
            if (drawing == null || string.IsNullOrEmpty(viewName)) return null;
            try
            {
                ISheet sheet = string.IsNullOrEmpty(sheetName) ? drawing.GetCurrentSheet() as ISheet : drawing.get_Sheet(sheetName) as ISheet;
                object[] views = sheet?.GetViews() as object[];
                return (views ?? new object[0]).OfType<IView>().FirstOrDefault(v => string.Equals(v.GetName2(), viewName, StringComparison.Ordinal));
            }
            catch (COMException)
            {
                return null;
            }
        }

        /// <summary>Tên sheet chứa view (view thuộc sheet đang kích hoạt khi gọi).</summary>
        internal static string SheetNameOf(IModelDoc2 drawingModel, IView view)
        {
            try
            {
                var drawing = (IDrawingDoc)drawingModel;
                string current = (drawing.GetCurrentSheet() as ISheet)?.GetName();
                string[] names = drawing.GetSheetNames() as string[];
                if (names == null || names.Length <= 1) return current;
                string viewName = view.GetName2();
                foreach (string sheetName in names)
                {
                    IView first = drawing.get_Sheet(sheetName)?.GetViews() is object[] vs && vs.Length > 0 ? vs[0] as IView : null;
                    object[] views = drawing.get_Sheet(sheetName)?.GetViews() as object[];
                    if (views != null && views.OfType<IView>().Any(v => v.GetName2() == viewName)) return sheetName;
                }
                return current;
            }
            catch
            {
                return null;
            }
        }
    }
}
