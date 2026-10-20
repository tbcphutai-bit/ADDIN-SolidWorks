using System;
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
                if (!drawingViewFitter.TryGetQuickView(out view))
                    throw new InvalidOperationException("Chọn một Drawing View hoặc đối tượng thuộc cùng một View rồi bấm Fix tỉ lệ.");
                var proposal = drawingViewFitter.InspectNative(view);
                drawingViewFitter.RunNative(view, proposal.SuggestedScale, true);
                LoadWidthLengthFromSelectedView(true);
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
                // Each engine reads its own preselection. A quick DIM command
                // never replaces a missing/invalid pick with a PropertyManager.
                if (kind == NativeToolKind.SectionDimensions)
                {
                    if (sectionEdgeDimensionCommand == null) sectionEdgeDimensionCommand = new LenhDimCanhSongSong(swApp);
                    sectionEdgeDimensionCommand.RunNative(10, false);
                }
                else if (kind == NativeToolKind.KegakiDimensions)
                {
                    if (drawingDimensionGenerator == null) drawingDimensionGenerator = new TaoDimKegaki(swApp);
                    drawingDimensionGenerator.RunNative(10, false);
                }
                else throw new InvalidOperationException("Lệnh DIM nhanh không hợp lệ.");
            }
            catch (Exception ex) { NativeModelCommandError(NativeToolPropertyManagerPage.Title(kind), ex); }
        }

        // Use the selected View with the standard 7 / 5 mm placement.
        // Invalid selections get guidance; they do not open another command page.
        private void RunHoleDimensions()
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                if (drawingViewFitter == null) drawingViewFitter = new ChinhTiLeDrawingView(swApp);
                SolidWorks.Interop.sldworks.View view;
                if (!drawingViewFitter.TryGetQuickView(out view))
                    throw new InvalidOperationException("Chọn một Drawing View hoặc đối tượng thuộc cùng một View rồi bấm DIM lỗ.");
                if (holeDimensionCommand == null) holeDimensionCommand = new DimKichThuocLo(swApp);
                // The engine reads the View from the selection, as it does after the page closes.
                var model = swApp.ActiveDoc as ModelDoc2;
                var manager = model.SelectionManager as SelectionMgr;
                model.ClearSelection2(true);
                if (model.Extension.MultiSelect2(new[] { new System.Runtime.InteropServices.DispatchWrapper(view) }, false, manager.CreateSelectData()) <= 0)
                    throw new InvalidOperationException("Không chọn lại được View đã chọn.");
                holeDimensionCommand.RunNative(7, 5, false);
            }
            catch (Exception ex) { NativeModelCommandError(NativeToolPropertyManagerPage.Title(NativeToolKind.HoleDimensions), ex); }
        }

        // Keep the spline edge, its Drawing View and the original click point.
        // The engine validates the pick and uses the standard fitting options.
        private void RunSplineArcs()
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                if (splineToArcsCommand == null) splineToArcsCommand = new SplineToArcsCommand(swApp);
                // The engine reads the edge and the click point from the selection, as it does after the page closes.
                splineToArcsCommand.RunNative(new NativeToolValues().ArcOptions(), false);
            }
            catch (Exception ex) { NativeModelCommandError(NativeToolPropertyManagerPage.Title(NativeToolKind.SplineArcs), ex); }
        }

        private void ShowNativeRepairSpline()
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                var repair = new Repair3DSplineCommand(swApp);
                OpenNativeModelCommand(closed => new NativeToolPropertyManagerPage(swApp,
                    NativeToolKind.RepairSpline, new NativeToolValues(), null,
                    (picks, values) => repair.InspectNative(picks[0] as SketchSegment),
                    values => repair.Run(this), closed,
                    work => BeginInvoke((Action)(() => { if (!IsDisposed && !Disposing && !solidWorksClosing) work(); }))));
            }
            catch (Exception ex) { NativeModelCommandError("REPAIR 3D SPLINE", ex); }
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
