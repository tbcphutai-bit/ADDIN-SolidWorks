// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using SolidWorks.Interop.sldworks;
namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Entry points for the "View -> DXF" button (ExportDrawingViewToDXF):
    /// sketches drawn directly on the selected Drawing View (or on the sheet,
    /// inside the view) whose layer name contains MARK/mark are kept and
    /// exported on the LASER_MARK layer instead of being cleaned away with
    /// the rest of the view sketch.
    ///
    /// It follows the Flatten -> DXF option "Sketch MARK on Drawing View"
    /// step by step: the sketches are READ from the source Drawing before the
    /// view is cleaned, recreated (arcs checked by reading them back) on the
    /// private marker layer of the temporary Drawing after the pasted view
    /// was found again by name and cleaned, saved with the layer preferences
    /// applied, and the finalizer then moves the marker layer to LASER_MARK.
    /// </summary>
    internal partial class DxfOutPut
    {
        /// <summary>
        /// Reads the MARK sketches of <paramref name="view"/> from the SOURCE
        /// drawing. Must be called before the view sketch is cleaned. Only
        /// reads; never modifies the drawing.
        /// </summary>
        internal static DrawingViewMarkSketchCollection ReadMarkSketchesOfView(
            ModelDoc2 drawingModel,
            IView view,
            string label)
        {
            var properties = new DXFPropforExport
            {
                FileName = label,
                PartNumber = SafeViewName(view)
            };
            return CollectDrawingViewMarkSketches(
                drawingModel,
                view,
                properties);
        }

        internal sealed class ViewMarkExportState
        {
            public DrawingViewMarkSketchCollection Marks { get; set; }
            public DxfManufacturingLayerSettings Settings { get; set; }
            public int Created { get; set; }

            /// <summary>Cạnh Cut-Extrude MARK dựng lên marker layer (tuỳ chọn «nhận Cut-Extrude MARK»).</summary>
            public int CutExtrudeCreated { get; set; }

            /// <summary>Vòng trong của lỗ MARK cắt xéo đã bỏ (đọc từ kết quả finalizer).</summary>
            public int ErasedMarkRims { get; set; }

            /// <summary>Có hình nào trên marker layer phải giữ khi lưu DXF.</summary>
            public bool AnyCreated => Created + CutExtrudeCreated > 0;

            public MarkerArcStats ArcStats { get; set; }

            /// <summary>Private layer the sketches were created on.</summary>
            public string MarkerLayerName { get; set; }

            /// <summary>Entities found in the saved DXF on the marker/LASER_MARK layer.</summary>
            public int DxfMarkEntities { get; set; }

            /// <summary>Entities on LASER_MARK after the final clean-up.</summary>
            public int FinalLaserEntities { get; set; }

            /// <summary>Which save attempt put the sketches in the DXF (0 = none).</summary>
            public int SaveAttempt { get; set; }

            public List<string> Notes { get; } = new List<string>();
        }

        /// <summary>
        /// Recreates the collected MARK sketches on the pasted view of the
        /// temporary drawing. Returns the number of sketches created (0 when
        /// there are none or the layers could not be prepared).
        /// </summary>
        internal static int AddMarkSketchesToTemporaryView(
            IModelDoc2 model,
            IView view,
            ViewMarkExportState state,
            string label)
        {
            if (model == null || view == null || state?.Marks == null ||
                state.Marks.Items.Count == 0)
                return 0;

            DxfManufacturingLayerSettings settings =
                DxfManufacturingLayerSettings.Normalize(state.Settings);
            state.Settings = settings;

            string viewName = SafeViewName(view);
            var properties = new DXFPropforExport
            {
                FileName = label,
                PartNumber = viewName,
                KeepDrawingViewMarkSketches = true,
                DrawingViewMarkSketches = state.Marks
            };
            state.ArcStats = properties.MarkerArcStats;

            LayerMgr layerManager = null;
            IView freshView = null;
            try
            {
                IDrawingDoc drawing = model as IDrawingDoc;
                layerManager = model.IGetLayerManager();
                if (drawing == null || layerManager == null)
                {
                    state.Notes.Add("không đọc được layer manager của Drawing tạm");
                    return 0;
                }

                // Same layers as Flatten -> DXF: the configured LASER_MARK
                // layer, the private marker layer the sketches are drawn on,
                // and the discard layer for wrong arcs.
                int laserColor = LayerToSolidWorksColor(settings.LaserMark);
                bool laserReady = EnsureTemporaryDrawingLayer(
                    drawing,
                    layerManager,
                    settings.LaserMark.Name,
                    "Laser marking geometry",
                    laserColor);
                bool markerReady = EnsureTemporaryDrawingLayer(
                    drawing,
                    layerManager,
                    DxfLayerFinalizer.CutExtrudeMarkerLayerName,
                    "Temporary LASER_MARK marker geometry",
                    laserColor);
                if (!laserReady || !markerReady)
                {
                    string failed = !laserReady
                        ? settings.LaserMark.Name
                        : DxfLayerFinalizer.CutExtrudeMarkerLayerName;
                    state.Notes.Add("không tạo được layer " + failed + " trên Drawing tạm");
                    ErrorEvents.LogError(
                        $"File:{label}-{viewName}: " +
                        "LỖI Sketch MARK: không tạo được layer " + failed +
                        " trên Drawing tạm.");
                    return 0;
                }
                if (EnsureTemporaryDrawingLayer(
                        drawing,
                        layerManager,
                        DxfLayerFinalizer.MarkDiscardLayerName,
                        "Temporary discarded marker geometry",
                        laserColor))
                {
                    properties.MarkDiscardLayerName =
                        DxfLayerFinalizer.MarkDiscardLayerName;
                }

                // The pasted view can be stale after the rebuild/clean-up
                // (Flatten -> DXF finds it again by name for that reason).
                freshView = FindViewByName(model as ModelDoc2, viewName);
                IView target = freshView ?? view;

                int created = CreateDrawingViewMarkSketchGeometry(
                    model,
                    target,
                    DxfLayerFinalizer.CutExtrudeMarkerLayerName,
                    properties);
                if (created == 0 && freshView != null)
                {
                    // Retry with the view the caller holds.
                    created = CreateDrawingViewMarkSketchGeometry(
                        model,
                        view,
                        DxfLayerFinalizer.CutExtrudeMarkerLayerName,
                        properties);
                }
                ReportMarkerArcStats(properties);

                try { model.ForceRebuild3(false); }
                catch (Exception ex)
                {
                    DxfTrace.Debug("View2DXF rebuild after MARK sketches failed: " + ex.Message);
                }

                state.Created = created;
                state.MarkerLayerName = created > 0
                    ? DxfLayerFinalizer.CutExtrudeMarkerLayerName
                    : null;
                if (created == 0)
                {
                    state.Notes.Add(
                        "SolidWorks không dựng lại được sketch nào trên Drawing tạm " +
                        "(xem Error Log)");
                }
                return created;
            }
            catch (Exception ex)
            {
                state.Notes.Add("lỗi khi dựng lại sketch: " + ex.Message);
                ErrorEvents.LogError(
                    $"File:{label}-{viewName}: " +
                    "LỖI Sketch MARK: không giữ lại được sketch MARK trên " +
                    "Drawing tạm: " + ex.Message);
                return 0;
            }
            finally
            {
                if (freshView != null && !ReferenceEquals(freshView, view))
                    SafeReleaseComObject(freshView);
                SafeReleaseComObject(layerManager);
            }
        }

        /// <summary>
        /// View -> DXF: cùng quy ước Cut-Extrude MARK với Flatten -> DXF (khi bật tuỳ chọn «nhận
        /// Cut-Extrude MARK»): cạnh hiển thị của feature tên có MARK được vẽ lại lên marker layer →
        /// LASER_MARK, bản model bên dưới bị bỏ; lỗ MARK cắt xéo 2 vòng chỉ giữ vòng ngoài cùng.
        /// Gọi SAU khi view tạm đã được dọn sketch. Trả số cạnh dựng được.
        /// </summary>
        internal static int AddCutExtrudeMarksToTemporaryView(
            IModelDoc2 model,
            IView view,
            ViewMarkExportState state,
            string label)
        {
            if (model == null || view == null || state == null) return 0;
            DxfManufacturingLayerSettings settings = DxfManufacturingLayerSettings.Normalize(state.Settings);
            state.Settings = settings;
            string viewName = SafeViewName(view);
            var properties = new DXFPropforExport { FileName = label, PartNumber = viewName };

            LayerMgr layerManager = null;
            IView freshView = null;
            try
            {
                IDrawingDoc drawing = model as IDrawingDoc;
                layerManager = model.IGetLayerManager();
                if (drawing == null || layerManager == null) return 0;

                int laserColor = LayerToSolidWorksColor(settings.LaserMark);
                bool laserReady = EnsureTemporaryDrawingLayer(drawing, layerManager, settings.LaserMark.Name, "Laser marking geometry", laserColor);
                bool markerReady = EnsureTemporaryDrawingLayer(drawing, layerManager, DxfLayerFinalizer.CutExtrudeMarkerLayerName,
                    "Temporary LASER_MARK marker geometry", laserColor);
                if (!laserReady || !markerReady)
                {
                    state.Notes.Add("không tạo được layer cho Cut-Extrude MARK trên Drawing tạm");
                    return 0;
                }
                if (EnsureTemporaryDrawingLayer(drawing, layerManager, DxfLayerFinalizer.MarkDiscardLayerName,
                        "Temporary discarded marker geometry", laserColor))
                    properties.MarkDiscardLayerName = DxfLayerFinalizer.MarkDiscardLayerName;
                bool eraseReady = EnsureTemporaryDrawingLayer(drawing, layerManager, DxfLayerFinalizer.MarkEraseLayerName,
                    "Temporary erased MARK rim geometry", laserColor);

                freshView = FindViewByName(model as ModelDoc2, viewName);
                int created = AssignCutExtrudeLaserMarkLayer(
                    model,
                    freshView ?? view,
                    DxfLayerFinalizer.CutExtrudeMarkerLayerName,
                    properties,
                    eraseReady ? DxfLayerFinalizer.MarkEraseLayerName : null,
                    text => state.Notes.Add(text));
                ReportMarkerArcStats(properties);
                try { model.ForceRebuild3(false); } catch { }

                state.CutExtrudeCreated = created;
                if (created > 0) state.MarkerLayerName = DxfLayerFinalizer.CutExtrudeMarkerLayerName;
                if (properties.LaserMarkMissingMarkerCount > 0)
                {
                    state.Notes.Add(properties.LaserMarkMissingMarkerCount +
                                    " cạnh Cut-Extrude MARK không dựng được marker (" +
                                    properties.LaserMarkMissingMarkerDetail + ") — xem Error Log");
                }
                return created;
            }
            catch (Exception ex)
            {
                state.Notes.Add("lỗi khi nhận Cut-Extrude MARK: " + ex.Message);
                ErrorEvents.LogError($"File:{label}-{viewName}: LỖI Cut-Extrude MARK (View→DXF): " + ex.Message);
                return 0;
            }
            finally
            {
                if (freshView != null && !ReferenceEquals(freshView, view))
                    SafeReleaseComObject(freshView);
                SafeReleaseComObject(layerManager);
            }
        }

        /// <summary>
        /// Makes SOLIDWORKS write the layer names of the temporary drawing
        /// into the DXF while it is saved; every user preference is restored
        /// on Dispose. <paramref name="withXToolProMap"/> applies the same
        /// DXF map Flatten -> DXF uses (it also moves the sketch-line
        /// category to LASER_MARK); without it only the layer names are used.
        /// </summary>
        internal static IDisposable BeginSketchLayerExportScope(bool withXToolProMap)
        {
            return DxfLayerExportPreferenceScope.Begin(
                withXToolProMap ? ResolveDxfLayerMapPath() : null);
        }

        /// <summary>
        /// Text for the message shown after View -> DXF; also written to the
        /// debug log so a missing sketch can always be explained.
        /// </summary>
        internal static string DescribeViewMarkExport(ViewMarkExportState state)
        {
            DrawingViewMarkSketchCollection marks = state?.Marks;
            if (marks == null) return "";

            var text = new StringBuilder();
            if (state.CutExtrudeCreated > 0)
            {
                text.Append(
                    "Cut-Extrude MARK: " + state.CutExtrudeCreated + " cạnh → layer " +
                    (state.Settings ?? DxfManufacturingLayerSettings.CreateDefault()).LaserMark.Name +
                    (state.ErasedMarkRims > 0 ? " (lỗ cắt xéo: bỏ " + state.ErasedMarkRims + " vòng trong, giữ vòng ngoài)" : "") +
                    ". ");
                if (marks.Items.Count == 0)
                {
                    foreach (string note in state.Notes)
                        text.Append(" " + note + ".");
                    return Finish(text);
                }
            }
            if (marks.Items.Count == 0)
            {
                text.Append(
                    "Không tìm thấy sketch nào có layer chứa MARK/mark trên view. ");
                text.Append("Đọc được " + marks.ViewSegmentTotal + " đoạn sketch trong view");
                if (marks.ViewLayerCounts.Count > 0)
                    text.Append(" (layer: " + FormatLayerCounts(marks.ViewLayerCounts) + ")");
                text.Append(" và " + marks.SheetSegmentTotal + " đoạn sketch trên sheet");
                if (marks.SheetLayerCounts.Count > 0)
                    text.Append(" (layer: " + FormatLayerCounts(marks.SheetLayerCounts) + ")");
                text.Append(". ");
                if (marks.OutsideOutlineSkipped > 0)
                {
                    text.Append(
                        marks.OutsideOutlineSkipped +
                        " đoạn MARK trên sheet nằm ngoài khung view nên bị bỏ. ");
                }
                if (marks.ConstructionSkipped > 0)
                {
                    text.Append(
                        marks.ConstructionSkipped +
                        " đoạn MARK là đường construction nên bị bỏ. ");
                }
                if (marks.ViewSegmentTotal == 0 && marks.SheetSegmentTotal == 0)
                {
                    text.Append(
                        "Nếu sketch đã được vẽ: chọn đúng view chứa sketch và " +
                        "lưu bản vẽ trước khi xuất.");
                }
                foreach (string note in state.Notes)
                    text.Append(" " + note + ".");
                return Finish(text);
            }

            string layerName =
                (state.Settings ?? DxfManufacturingLayerSettings.CreateDefault())
                .LaserMark.Name;
            text.Append(
                "Sketch MARK: đọc " + marks.Items.Count + " (layer nguồn: " +
                string.Join(", ", marks.MarkLayers) + "), dựng lại " +
                state.Created + ", trong DXF có " + state.FinalLaserEntities +
                " thực thể trên layer " + layerName + ".");
            if (state.Created < marks.Items.Count)
            {
                text.Append(
                    " CẢNH BÁO: " + (marks.Items.Count - state.Created) +
                    " sketch không dựng lại được - xem Error Log.");
            }
            if (state.Created > 0 && state.FinalLaserEntities == 0)
            {
                text.Append(
                    " CẢNH BÁO: SolidWorks không ghi sketch MARK vào DXF " +
                    "(đã thử " + Math.Max(1, state.SaveAttempt) + " cách lưu).");
            }
            else if (state.SaveAttempt == 2)
            {
                text.Append(
                    " (lưu theo map DXF của Flatten→DXF nên các sketch của " +
                    "model đang hiển thị trên view cũng nằm ở layer " + layerName + ".)");
            }
            if (state.ArcStats != null && state.ArcStats.HasFallback)
            {
                text.Append(
                    " Một số cung/tròn được dựng bằng cách khác hoặc polyline " +
                    "vì SolidWorks dựng cung sai.");
            }
            foreach (string note in state.Notes)
                text.Append(" " + note + ".");
            return Finish(text);
        }

        /// <summary>
        /// The collected sketches as plain data (source sheet coordinates) for
        /// <see cref="DxfLayerFinalizer.InjectSheetMarks"/>.
        /// </summary>
        internal static List<DxfInjectMark> ToInjectMarks(
            DrawingViewMarkSketchCollection marks)
        {
            List<bool> fromView;
            return ToInjectMarks(marks, out fromView);
        }

        /// <summary>
        /// Same, with per mark whether it was drawn in the view sketch (and so
        /// may have been converted and exported with the view geometry).
        /// </summary>
        internal static List<DxfInjectMark> ToInjectMarks(
            DrawingViewMarkSketchCollection marks,
            out List<bool> fromView)
        {
            var result = new List<DxfInjectMark>();
            fromView = new List<bool>();
            if (marks == null) return result;
            foreach (DrawingViewMarkSketch item in marks.Items)
            {
                if (item.SheetPoints.Count < 2) continue;
                var mark = new DxfInjectMark { Kind = item.Kind };
                mark.Points.AddRange(item.SheetPoints);
                result.Add(mark);
                fromView.Add(item.FromView);
            }
            return result;
        }

        private static string Finish(StringBuilder text)
        {
            string result = text.ToString().Trim();
            try { ErrorEvents.LogDebug("[View2DXF][MARK] " + result); }
            catch { }
            return result;
        }

        private static string FormatLayerCounts(Dictionary<string, int> counts)
        {
            return string.Join(", ", counts
                .OrderByDescending(pair => pair.Value)
                .Take(8)
                .Select(pair => pair.Key + " ×" + pair.Value));
        }

        private static string SafeViewName(IView view)
        {
            try { return view?.GetName2() ?? ""; }
            catch { return ""; }
        }
    }
}
