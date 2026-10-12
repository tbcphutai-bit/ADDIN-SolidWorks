using System;
using System.Linq;
using ADDIN.Commands;
using ADDIN.UI;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN
{
    public partial class BomTaskPaneControl
    {
        private void FixViewScale()
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                if (drawingViewFitter == null) drawingViewFitter = new ChinhTiLeDrawingView(swApp);
                SolidWorks.Interop.sldworks.View view;
                if (drawingViewFitter.TryGetQuickView(out view))
                {
                    var proposal = drawingViewFitter.InspectNative(view);
                    drawingViewFitter.RunNative(view, proposal.SuggestedScale, true);
                    LoadWidthLengthFromSelectedView(true);
                }
                else ShowNativeToolPage(NativeToolKind.ViewScale);
            }
            catch (Exception ex) { NativeModelCommandError("FIX TỈ LỆ VIEW", ex); }
        }

        private void RenameSelectedHoles()
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                if (changeHoleNameCommand == null) changeHoleNameCommand = new ChangeHoleNameCommand(swApp);
                // Hole Type is already entered in the task pane; reuse the current feature selection.
                changeHoleNameCommand.Run(cboHoleType.Text, false);
            }
            catch (Exception ex) { NativeModelCommandError("CHANGE NAME HOLE", ex); }
        }

        private void RunSimpleDrawingDimensions(NativeToolKind kind)
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                if (drawingViewFitter == null) drawingViewFitter = new ChinhTiLeDrawingView(swApp);
                SolidWorks.Interop.sldworks.View view;
                bool ready = drawingViewFitter.TryGetQuickView(out view);
                if (ready && kind == NativeToolKind.SectionDimensions)
                {
                    var model = swApp.ActiveDoc as ModelDoc2;
                    var manager = model.SelectionManager as SelectionMgr;
                    ready = manager != null && manager.GetSelectedObjectCount2(-1) == 1 &&
                        manager.GetSelectedObject6(1, -1) is Edge;
                }
                if (!ready) { ShowNativeToolPage(kind); return; }
                if (kind == NativeToolKind.SectionDimensions)
                {
                    if (sectionEdgeDimensionCommand == null) sectionEdgeDimensionCommand = new LenhDimCanhSongSong(swApp);
                    sectionEdgeDimensionCommand.RunNative(10, false);
                }
                else
                {
                    if (drawingDimensionGenerator == null) drawingDimensionGenerator = new TaoDimKegaki(swApp);
                    drawingDimensionGenerator.RunNative(10, false);
                }
            }
            catch (Exception ex) { NativeModelCommandError(NativeToolPropertyManagerPage.Title(kind), ex); }
        }

        // DIM lỗ behaves like the other DIM commands: a View (or an edge / DIM
        // of it) selected beforehand runs at once with the standard 7 / 5 mm
        // placement; only an empty selection opens the page.
        private void RunHoleDimensions()
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                if (drawingViewFitter == null) drawingViewFitter = new ChinhTiLeDrawingView(swApp);
                SolidWorks.Interop.sldworks.View view;
                if (!drawingViewFitter.TryGetQuickView(out view)) { ShowNativeToolPage(NativeToolKind.HoleDimensions); return; }
                if (holeDimensionCommand == null) holeDimensionCommand = new DimKichThuocLo(swApp);
                // The engine reads the View from the selection, as it does after the page closes.
                var model = swApp.ActiveDoc as ModelDoc2;
                var manager = model.SelectionManager as SelectionMgr;
                model.ClearSelection2(true);
                if (model.Extension.MultiSelect2(new[] { new System.Runtime.InteropServices.DispatchWrapper(view) }, false, manager.CreateSelectData()) <= 0)
                    throw new InvalidOperationException("Không chọn lại được View đã chọn.");
                holeDimensionCommand.RunNative(7, 5);
            }
            catch (Exception ex) { NativeModelCommandError(NativeToolPropertyManagerPage.Title(NativeToolKind.HoleDimensions), ex); }
        }

        // Spline → cung R follows the same rule: a spline edge of a Drawing View selected
        // beforehand runs at once with the page's default options; pressing the button
        // with anything else selected, or nothing, opens the page.
        private void RunSplineArcs()
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                if (!QuickSplineEdgeSelected()) { ShowNativeToolPage(NativeToolKind.SplineArcs); return; }
                if (splineToArcsCommand == null) splineToArcsCommand = new SplineToArcsCommand(swApp);
                // The engine reads the edge and the click point from the selection, as it does after the page closes.
                splineToArcsCommand.RunNative(new NativeToolValues().ArcOptions());
            }
            catch (Exception ex) { NativeModelCommandError(NativeToolPropertyManagerPage.Title(NativeToolKind.SplineArcs), ex); }
        }

        // The checks the page makes on its own pick: one edge of a Drawing View that is neither a line nor a circle.
        private bool QuickSplineEdgeSelected()
        {
            var model = swApp.ActiveDoc as ModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING) return false;
            var manager = model.SelectionManager as SelectionMgr;
            if (manager == null || manager.GetSelectedObjectCount2(-1) != 1) return false;
            var edge = manager.GetSelectedObject6(1, -1) as Edge;
            if (edge == null || manager.GetSelectedObjectsDrawingView2(1, -1) == null) return false;
            var curve = edge.GetCurve() as Curve;
            return curve != null && !curve.IsLine() && !curve.IsCircle();
        }

        private void ShowNativeToolPage(NativeToolKind kind)
        {
            try {
                EnsureNativeModelCommandAvailable();
                var values = new NativeToolValues(); string[] presets = new string[0];
                Func<object[], NativeToolValues, NativeToolPreview> inspect = null;
                Action<NativeToolValues> execute;
                switch (kind) {
                    case NativeToolKind.SplineArcs:
                        if (splineToArcsCommand == null) splineToArcsCommand = new SplineToArcsCommand(swApp);
                        execute = v => splineToArcsCommand.RunNative(v.ArcOptions()); break;
                    case NativeToolKind.RepairSpline:
                        var repair = new Repair3DSplineCommand(swApp);
                        inspect = (p, v) => repair.InspectNative(p[0] as SketchSegment);
                        execute = v => repair.Run(this); break;
                    case NativeToolKind.HoleDimensions:
                        if (holeDimensionCommand == null) holeDimensionCommand = new DimKichThuocLo(swApp);
                        values.OffsetMm = 7; inspect = (p, v) => holeDimensionCommand.InspectNative(p[0] as SolidWorks.Interop.sldworks.View);
                        execute = v => holeDimensionCommand.RunNative(v.OffsetMm, v.LaneMm); break;
                    case NativeToolKind.SectionDimensions:
                        if (sectionEdgeDimensionCommand == null) sectionEdgeDimensionCommand = new LenhDimCanhSongSong(swApp);
                        execute = v => sectionEdgeDimensionCommand.RunNative(v.OffsetMm); break;
                    case NativeToolKind.KegakiDimensions:
                        if (drawingDimensionGenerator == null) drawingDimensionGenerator = new TaoDimKegaki(swApp);
                        execute = v => drawingDimensionGenerator.RunNative(v.OffsetMm); break;
                    case NativeToolKind.RenameHole:
                        if (changeHoleNameCommand == null) changeHoleNameCommand = new ChangeHoleNameCommand(swApp);
                        values.Text = cboHoleType.Text; presets = holeNamePresets.ToArray();
                        inspect = (p, v) => changeHoleNameCommand.InspectNative(p.Cast<Feature>());
                        execute = v => { changeHoleNameCommand.Run(v.Text); cboHoleType.Text = v.Text; }; break;
                    case NativeToolKind.Note:
                        EnsureNativeDrawingAnnotations();
                        values.Text = cboBendLine.Text; presets = Enumerable.Range(0, cboBendLine.ItemCount).Select(cboBendLine.GetItem).ToArray();
                        execute = v => drawingTextAnnotationCommands.InsertNativeNote(v.Text); break;
                    case NativeToolKind.DimensionText:
                        EnsureNativeDrawingAnnotations();
                        values.Text = cboSide.Text; presets = Enumerable.Range(0, cboSide.ItemCount).Select(cboSide.GetItem).ToArray();
                        execute = v => drawingTextAnnotationCommands.InsertNativeText(v.Text); break;
                    case NativeToolKind.Balloon:
                        EnsureNativeDrawingAnnotations();
                        values.Property = cboBalloonProperty.Text == "合番" ? "合番" : "部品番号";
                        inspect = (p, v) => drawingTextAnnotationCommands.InspectNativeBalloon(v.Property);
                        execute = v => drawingTextAnnotationCommands.InsertNativeBalloon(v.Property); break;
                    default:
                        if (drawingViewFitter == null) drawingViewFitter = new ChinhTiLeDrawingView(swApp);
                        inspect = (p, v) => drawingViewFitter.InspectNative(p[0] as SolidWorks.Interop.sldworks.View, v.Automatic ? (double?)null : v.Scale);
                        execute = v => { drawingViewFitter.RunNative(v.Scale, v.Center); LoadWidthLengthFromSelectedView(true); }; break;
                }
                OpenNativeModelCommand(closed => new NativeToolPropertyManagerPage(swApp, kind, values, presets, inspect, execute, closed,
                    work => BeginInvoke((Action)(() => { if (!IsDisposed && !Disposing && !solidWorksClosing) work(); }))));
            }
            catch (Exception ex) { NativeModelCommandError(NativeToolPropertyManagerPage.Title(kind), ex); }
        }
        private void RunDrawingAnnotation(string title, Action<LenhNoteTextBalloon> execute)
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                EnsureNativeDrawingAnnotations();
                // These commands already have their inputs in the task pane.
                execute(drawingTextAnnotationCommands);
            }
            catch (Exception ex) { NativeModelCommandError(title, ex); }
        }
        private void EnsureNativeDrawingAnnotations()
        { if (drawingTextAnnotationCommands == null) drawingTextAnnotationCommands = new LenhNoteTextBalloon(swApp, this, cboBendLine, cboSide, cboBalloonProperty); }
        private void ShowNativeRepairDimensions()
        {
            try {
                OpenNativeModelCommand(closed => new RepairDimensionsPropertyManagerPage(swApp,
                    view => RepairDanglingDimensions.ScanNative(swApp, view),
                    targets => RepairDanglingDimensions.RunNativeSelected(swApp, targets), closed));
            }
            catch (Exception ex) { NativeModelCommandError("REPAIR DIM", ex); }
        }
    }
}
