using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public class TaoDimKegaki
    {
        private class BendInfo
        {
            public object Geometry;
            // Persistent reference dung de lay lai dung entity sau khi Drawing View refresh/rebuild.
            // Neu SolidWorks khong ho tro persist ref cho entity nay thi se tu dong fallback
            // sang tim lai theo hinh hoc, khong lam thay doi logic DIM.
            public bool IsEdge;
            public double AngleGroup;
            public double SortKey;
            public double MidX;
            public double MidY;
            public double NormalX;
            public double NormalY;
            public bool IsBoundingBox;
            public object StartVertex;
            public object EndVertex;
            public object StartPoint;
            public object EndPoint;
            public double StartX;
            public double StartY;
            public double EndX;
            public double EndY;
            public double Length;
        }

        private string diagnosticPath;
        private int diagnosticSequence;

        private void BeginDiagnostics()
        {
            diagnosticSequence = 0;
            diagnosticPath = null;
            try
            {
                string folder = Path.Combine(Path.GetTempPath(), "ADDIN", "DimKegaki");
                Directory.CreateDirectory(folder);
                diagnosticPath = Path.Combine(folder, "DimKegaki_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N") + ".log");
            }
            catch (Exception ex) { Trace.WriteLine("[DIM KEGAKI] Cannot initialize file log: " + ex); }
            LogDiagnostic("BEGIN build=kegaki-direct-view-bbox-20261001 assembly=" + typeof(TaoDimKegaki).Assembly.Location
                + " mvid=" + typeof(TaoDimKegaki).Module.ModuleVersionId + " log=" + diagnosticPath);
            LogDiagnostic("Coordinates/distances below are drawing-sheet metres, AFTER view transform; cutoff=0.001 sheet m.");
        }

        private void LogDiagnostic(string message)
        {
            try
            {
                string line = "[DIM KEGAKI] " + DateTime.Now.ToString("O") + " #" + (++diagnosticSequence) + " " + message;
                Trace.WriteLine(line);
                if (diagnosticPath != null) File.AppendAllText(diagnosticPath, line + System.Environment.NewLine);
            }
            catch (Exception) { /* Diagnostics must never interrupt dimension creation. */ }
        }

        private string Describe(BendInfo item)
        {
            if (item == null) return "null";
            return string.Format(CultureInfo.InvariantCulture,
                "{0} angle={1:F3} start=({2:G9},{3:G9}) end=({4:G9},{5:G9}) mid=({6:G9},{7:G9}) length={8:G9} sort={9:G9}",
                item.IsEdge ? "EDGE" : item.IsBoundingBox ? "BBOX" : "BEND",
                item.AngleGroup, item.StartX, item.StartY, item.EndX, item.EndY,
                item.MidX, item.MidY, item.Length, item.SortKey);
        }

        private void LogGeometry(string label, List<BendInfo> items)
        {
            LogDiagnostic(label + " count=" + items.Count);
            for (int i = 0; i < items.Count; i++) LogDiagnostic(label + "[" + i + "] " + Describe(items[i]));
        }

        private readonly ISldWorks swApp;
        private const double ParallelAngleTolerance = 10.0;

        public TaoDimKegaki(ISldWorks app)
        {
            swApp = app;
        }

        public void GenerateKegakiDimensions()
        {
            BeginDiagnostics();
            string currentStep = "[00] ActiveDoc";
            LogDiagnostic(currentStep);
            ModelDoc2 model = null;
            bool undoStarted = false;

            try
            {
                model = swApp?.ActiveDoc as ModelDoc2;
                if (model == null ||
                    model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                {
                    MessageBox.Show("Chi dung trong moi truong Drawing.", "dim kegaki", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                currentStep = "[01] Lay Drawing View";
                LogDiagnostic(currentStep);
                SelectionMgr selMgr = model.SelectionManager as SelectionMgr;
                SolidWorks.Interop.sldworks.View view = GetSelectedDrawingView(selMgr);

                if (view == null)
                {
                    MessageBox.Show("Vui long chon 1 Drawing View truoc.", "dim kegaki", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Giu lai ten View de co the lay lai COM object moi sau cac thao tac
                // co kha nang lam SolidWorks refresh/rebuild Drawing View.
                string selectedViewName = SafeGetViewName(view);
                LogDiagnostic("VIEW name=" + selectedViewName + " scale=" + view.ScaleDecimal.ToString("G9", CultureInfo.InvariantCulture));

                currentStep = "[02] Bat dau Undo";
                LogDiagnostic(currentStep);
                model.Extension.StartRecordingUndoObject();
                undoStarted = true;

                currentStep = "[03] Xoa DIM cu";
                LogDiagnostic(currentStep);
                DeleteAllDimensionsInView(model, view);

                // Xoa annotation co the lam View refresh. Lay lai View theo ten de tranh
                // tiep tuc dung COM proxy cu.
                DrawingDoc drawing = model as DrawingDoc;
                view = ReacquireViewByName(drawing, selectedViewName) ?? view;

                currentStep = "[04] Hien Bend-Line va BBox";
                LogDiagnostic(currentStep);
                // ShowSketchFromTree co ClearSelection/UnblankSketch va co the lam drawing
                // refresh. Hien tat ca sketch truoc, sau do moi lay geometry.
                ShowSketchFromTree(model, "ﾍﾞﾝﾄﾞ-ﾗｲﾝ", "ベンド-ライン", "Bend-Line");
                Feature boundingBoxFeature =
                    ShowSketchFromTree(model, "境界ﾎﾞｯｸｽ", "境界ボックス", "Bounding-Box");

                // Dua Drawing ve trang thai rebuild on dinh TRUOC KHI lay Bend-Line.
                // Muc dich la de DIM bam vao entity cua lan rebuild hien tai, thay vi bam vao
                // SketchSegment tam vua bi SolidWorks thay the. Khong thay doi hinh hoc/logic DIM.
                currentStep = "[05] Rebuild View truoc khi lay reference";
                LogDiagnostic(currentStep);
                try
                {
                    model.ForceRebuild3(false);
                }
                catch (COMException)
                {
                    // Neu SolidWorks dang ban, tiep tuc bang view hien tai; cac ham select
                    // phia duoi van co co che reacquire entity.
                }

                // Rebuild co the thay COM proxy cua View/Feature, nen bat buoc lay lai.
                view = ReacquireViewByName(drawing, selectedViewName) ?? view;
                LogViewBoundingBoxes(view);
                boundingBoxFeature = ResolveBoundingBoxFeature(model, view);

                currentStep = "[06] Lay Transform";
                LogDiagnostic(currentStep);
                MathUtility mathUtil = swApp.IGetMathUtility();
                MathTransform viewTransform = view.ModelToViewTransform;
                if (mathUtil == null || viewTransform == null)
                    return;

                currentStep = "[07] Lay Bend-Line va BBox";
                LogDiagnostic(currentStep);
                List<BendInfo> bends = new List<BendInfo>();
                AddBendLines(view.GetBendLines(), mathUtil, viewTransform, false, bends);

                List<BendInfo> outerEdges = GetOuterVisibleEdges(view, mathUtil, viewTransform);
                LogGeometry("BENDS", bends);
                LogGeometry("OUTER_EDGES", outerEdges);
                bends.AddRange(outerEdges);

                List<BendInfo> boundingBoxLines = new List<BendInfo>();
                if (boundingBoxFeature != null)
                {
                    Sketch boundingBoxSketch = boundingBoxFeature.GetSpecificFeature2() as Sketch;
                    if (boundingBoxSketch != null)
                    {
                        LogDiagnostic("BBOX sketch found; reading segments");
                        AddSketchSegments(boundingBoxSketch.GetSketchSegments(), mathUtil, viewTransform, true, boundingBoxLines);
                    }
                    else
                    {
                        LogDiagnostic("BBOX feature found but GetSpecificFeature2 is not a Sketch: "
                            + boundingBoxFeature.Name + " type=" + boundingBoxFeature.GetTypeName2());
                    }
                }

                LogGeometry("BBOX_SKETCH_FEATURE", boundingBoxLines);
                List<BendInfo> viewBoundingBoxLines = GetSelectableViewBoundingBoxLines(model, view);
                if (viewBoundingBoxLines.Count >= 4)
                    boundingBoxLines = viewBoundingBoxLines;
                LogGeometry("BBOX_SELECTED_FOR_DIM", boundingBoxLines);
                currentStep = "[08] Tao SelectData";
                LogDiagnostic(currentStep);
                // SelectionMgr/SelectData cung lay lai sau cac thao tac refresh o tren.
                selMgr = model.SelectionManager as SelectionMgr;
                SelectData selectData = selMgr?.CreateSelectData() as SelectData;
                if (selectData == null)
                    return;

                selectData.View = view;
                model.ClearSelection2(true);

                // Khong ActivateSheet lai chinh current sheet o day.
                // ActivateSheet sau khi da lay GetBendLines co the invalidate SketchSegment.
                if (!HasRealBendLine(bends))
                {
                    LogDiagnostic("BRANCH no real bend: overall W/L only");
                    List<BendInfo> overallLines = boundingBoxLines.Count > 0
                        ? boundingBoxLines
                        : outerEdges;

                    double overallMinX;
                    double overallMaxX;
                    double overallMinY;
                    double overallMaxY;

                    if (!TryGetBounds(overallLines, out overallMinX, out overallMaxX, out overallMinY, out overallMaxY))
                        return;

                    currentStep = "[09] Tao DIM Overall";
                LogDiagnostic(currentStep);
                    int overallCount = CreateOverallDimensions(
                        model,
                        overallLines,
                        view,
                        selectData,
                        overallMinX,
                        overallMaxX,
                        overallMinY,
                        overallMaxY,
                        boundingBoxLines.Count > 0 ? outerEdges : null);

                    model.ClearSelection2(true);
                    model.GraphicsRedraw2();
                    MessageBox.Show(
                        "Hoan tat! Da tao " + overallCount + " kich thuoc W,L.",
                        "dim kegaki",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                if (bends.Count < 2)
                    return;

                currentStep = "[10] Sap xep";
                LogDiagnostic(currentStep);
                bends.Sort(CompareBends);
                List<BendInfo> chainLines = new List<BendInfo>();
                foreach (BendInfo bend in bends)
                {
                    if (!bend.IsEdge && !bend.IsBoundingBox)
                        chainLines.Add(bend);
                }

                chainLines.Sort(CompareBends);
                LogGeometry("CHAIN", chainLines);
                LogDiagnostic("ACTIVE PASSES: bend chain, overall, outer edge-to-bend. Flap/transition passes are not called.");

                double minX = double.MaxValue;
                double maxX = double.MinValue;
                double minY = double.MaxValue;
                double maxY = double.MinValue;

                foreach (BendInfo bend in bends)
                {
                    minX = Math.Min(minX, bend.MidX);
                    maxX = Math.Max(maxX, bend.MidX);
                    minY = Math.Min(minY, bend.MidY);
                    maxY = Math.Max(maxY, bend.MidY);
                }

                double centerX = (minX + maxX) / 2.0;
                double centerY = (minY + maxY) / 2.0;

                currentStep = "[11] Tao DIM";
                LogDiagnostic(currentStep);
                List<string> createdDistanceKeys = new List<string>();
                int dimensionCount = CreateDimensions(
                    model,
                    chainLines,
                    selectData,
                    minY,
                    maxY,
                    centerX,
                    centerY,
                    view,
                    createdDistanceKeys);

                LogDiagnostic("CHAIN created=" + dimensionCount);
                dimensionCount += CreateOverallDimensions(
                    model,
                    boundingBoxLines.Count > 0 ? boundingBoxLines : outerEdges,
                    view,
                    selectData,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    boundingBoxLines.Count > 0 ? outerEdges : null);

                LogDiagnostic("CHAIN+OVERALL created=" + dimensionCount);
                dimensionCount += CreateSingleBendEdgePointDimensions(
                    model,
                    chainLines,
                    outerEdges,
                    view,
                    selectData,
                    centerX,
                    centerY,
                    createdDistanceKeys);

                model.ClearSelection2(true);
                model.GraphicsRedraw2();
                LogDiagnostic("TOTAL created=" + dimensionCount);
                MessageBox.Show(
                    "Hoan tat! Da tao " + dimensionCount + " kich thuoc chuan Form.",
                    "dim kegaki",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (COMException ex)
            {
                LogDiagnostic("ERROR step=" + currentStep + " " + ex);
                MessageBox.Show(
                    "Loi COM tai buoc: " + currentStep + System.Environment.NewLine +
                    "HRESULT: 0x" + ex.ErrorCode.ToString("X8") + System.Environment.NewLine +
                    ex.Message,
                    "dim kegaki",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                LogDiagnostic("ERROR step=" + currentStep + " " + ex);
                MessageBox.Show(
                    "Loi tai buoc: " + currentStep + System.Environment.NewLine + ex.Message,
                    "dim kegaki",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                LogDiagnostic("END lastStep=" + currentStep);
                if (undoStarted && model != null)
                {
                    try
                    {
                        model.Extension.FinishRecordingUndoObject("dim kegaki");
                    }
                    catch (COMException)
                    {
                        // Neu document/view vua bi SolidWorks rebuild/disconnect thi khong de
                        // cleanup Undo che mat loi goc.
                    }
                }
            }
        }

        private void AutoArrangeDimensionsInView(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view)
        {
            Array annotations = view.GetAnnotations() as Array;
            if (annotations == null)
                return;

            model.ClearSelection2(true);
            bool append = false;

            foreach (object item in annotations)
            {
                Annotation annotation = item as Annotation;
                if (annotation == null ||
                    annotation.GetType() != (int)swAnnotationType_e.swDisplayDimension)
                    continue;

                if (annotation.Select3(append, null))
                    append = true;
            }

            if (!append)
                return;

            model.Extension.AlignDimensions(
                (int)swAlignDimensionType_e.swAlignDimensionType_AutoArrange,
                0.01);
        }

        private string SafeGetViewName(SolidWorks.Interop.sldworks.View view)
        {
            if (view == null)
                return null;

            try
            {
                return view.GetName2();
            }
            catch (COMException)
            {
                return null;
            }
        }

        private SolidWorks.Interop.sldworks.View ReacquireViewByName(
            DrawingDoc drawing,
            string viewName)
        {
            if (drawing == null || string.IsNullOrEmpty(viewName))
                return null;

            try
            {
                SolidWorks.Interop.sldworks.View current =
                    drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;

                while (current != null)
                {
                    string currentName = SafeGetViewName(current);
                    if (string.Equals(currentName, viewName, StringComparison.OrdinalIgnoreCase))
                        return current;

                    current = current.GetNextView() as SolidWorks.Interop.sldworks.View;
                }
            }
            catch (COMException)
            {
            }

            return null;
        }

        private SolidWorks.Interop.sldworks.View GetSelectedDrawingView(SelectionMgr selMgr)
        {
            if (selMgr == null)
                return null;

            int count = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                SolidWorks.Interop.sldworks.View view =
                    selMgr.GetSelectedObject6(i, -1) as SolidWorks.Interop.sldworks.View;
                if (view != null)
                    return view;

                view = selMgr.GetSelectedObjectsDrawingView2(i, -1);
                if (view != null)
                    return view;
            }

            return null;
        }

        private void DeleteAllDimensionsInView(
            ModelDoc2 drawingModel,
            SolidWorks.Interop.sldworks.View view)
        {
            if (drawingModel == null || view == null)
                return;

            Array annotations;
            try
            {
                annotations = view.GetAnnotations() as Array;
            }
            catch (COMException)
            {
                return;
            }

            if (annotations == null)
                return;

            // QUAN TRONG: khong EditDelete() ngay trong luc dang foreach GetAnnotations().
            // Xoa tung annotation co the lam SolidWorks rebuild collection va lam cac
            // COM proxy con lai bi RPC_E_DISCONNECTED.
            drawingModel.ClearSelection2(true);
            bool hasSelection = false;

            foreach (object item in annotations)
            {
                Annotation annotation = item as Annotation;
                if (annotation == null)
                    continue;

                try
                {
                    if (annotation.GetType() != (int)swAnnotationType_e.swDisplayDimension)
                        continue;

                    if (IsHoleRelatedDimension(annotation))
                        continue;

                    if (annotation.Select3(hasSelection, null))
                        hasSelection = true;
                }
                catch (COMException)
                {
                    // Annotation da bi SolidWorks invalidate trong luc refresh.
                    // Bo qua object nay, khong lam thay doi logic chon DIM can xoa.
                    continue;
                }
            }

            if (hasSelection)
            {
                try
                {
                    // Xoa 1 lan sau khi da ket thuc viec duyet collection.
                    drawingModel.EditDelete();
                }
                catch (COMException)
                {
                    // De caller tiep tuc va lay lai View/geometry moi.
                }
            }

            drawingModel.ClearSelection2(true);
        }

        private bool IsHoleRelatedDimension(Annotation annotation)
        {
            DisplayDimension displayDimension =
                annotation.GetSpecificAnnotation() as DisplayDimension;
            if (displayDimension == null)
            { LogDiagnostic("ADD_DIM FAILED returned null; registered key remains reserved"); return false; }

            int dimensionType = displayDimension.GetType();
            LogDiagnostic("ADD_DIM returned type=" + dimensionType);
            if (dimensionType == (int)swDimensionType_e.swDiameterDimension ||
                dimensionType == (int)swDimensionType_e.swRadialDimension)
                return true;

            string prefix = displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix);
            string callout = displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutAbove);
            string suffix = displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix);
            string allText = (prefix ?? "") + (callout ?? "") + (suffix ?? "");

            if (HasAttachedCircularEntity(annotation))
                return true;

            return allText.Contains("Ø") ||
                allText.Contains("Φ") ||
                allText.StartsWith("R", StringComparison.OrdinalIgnoreCase);
        }

        private bool HasAttachedCircularEntity(Annotation annotation)
        {
            object[] entities = TryGetAttachedEntities(annotation);
            if (entities == null)
                return false;

            foreach (object entity in entities)
            {
                if (IsCircularEntity(entity))
                    return true;
            }

            return false;
        }

        private object[] TryGetAttachedEntities(Annotation annotation)
        {
            try
            {
                return ((dynamic)annotation).GetAttachedEntities3() as object[];
            }
            catch
            {
            }

            try
            {
                return ((dynamic)annotation).GetAttachedEntities2() as object[];
            }
            catch
            {
            }

            try
            {
                return ((dynamic)annotation).GetAttachedEntities() as object[];
            }
            catch
            {
                return null;
            }
        }

        private bool IsCircularEntity(object entity)
        {
            Edge edge = entity as Edge;
            if (edge != null)
                return IsCircularCurve(edge.GetCurve() as Curve);

            SketchSegment segment = entity as SketchSegment;
            if (segment != null)
                return IsCircularCurve(segment.GetCurve() as Curve);

            return IsCircularCurve(entity as Curve);
        }

        private bool IsCircularCurve(Curve curve)
        {
            if (curve == null)
                return false;

            try
            {
                if (((dynamic)curve).IsCircle())
                    return true;
            }
            catch
            {
            }

            try
            {
                if (((dynamic)curve).IsArc())
                    return true;
            }
            catch
            {
            }

            return false;
        }

        private bool HasRealBendLine(List<BendInfo> bends)
        {
            foreach (BendInfo bend in bends)
            {
                if (!bend.IsEdge && !bend.IsBoundingBox)
                    return true;
            }

            return false;
        }

        private bool TryGetBounds(
            List<BendInfo> lines,
            out double minX,
            out double maxX,
            out double minY,
            out double maxY)
        {
            minX = double.MaxValue;
            maxX = double.MinValue;
            minY = double.MaxValue;
            maxY = double.MinValue;

            if (lines == null || lines.Count == 0)
                return false;

            foreach (BendInfo line in lines)
            {
                minX = Math.Min(minX, line.MidX);
                maxX = Math.Max(maxX, line.MidX);
                minY = Math.Min(minY, line.MidY);
                maxY = Math.Max(maxY, line.MidY);
            }

            return minX < double.MaxValue &&
                maxX > double.MinValue &&
                minY < double.MaxValue &&
                maxY > double.MinValue;
        }

        private void AddBendLines(
            object bendLines,
            MathUtility mathUtil,
            MathTransform viewTransform,
            bool isBoundingBox,
            List<BendInfo> bends)
        {
            Array items = bendLines as Array;
            if (items == null)
                return;

            foreach (object item in items)
            {
                SketchSegment segment = item as SketchSegment;
                if (segment != null)
                    AddSegment(segment, mathUtil, viewTransform, isBoundingBox, bends);
            }
        }

        private void AddSketchSegments(
            object sketchSegments,
            MathUtility mathUtil,
            MathTransform viewTransform,
            bool isBoundingBox,
            List<BendInfo> bends)
        {
            Array items = sketchSegments as Array;
            if (items == null)
                return;

            foreach (object item in items)
            {
                SketchSegment segment = item as SketchSegment;
                if (segment != null && segment.GetType() == 0)
                    AddSegment(segment, mathUtil, viewTransform, isBoundingBox, bends);
            }
        }

        private void AddSegment(
            SketchSegment segment,
            MathUtility mathUtil,
            MathTransform viewTransform,
            bool isBoundingBox,
            List<BendInfo> bends)
        {
            SketchLine line = segment as SketchLine;
            Sketch sketch = segment.GetSketch();
            if (line == null || sketch == null)
                return;

            MathTransform sketchTransform = sketch.ModelToSketchTransform?.Inverse() as MathTransform;
            SketchPoint start = line.GetStartPoint2() as SketchPoint;
            SketchPoint end = line.GetEndPoint2() as SketchPoint;
            if (sketchTransform == null || start == null || end == null)
                return;

            double[] p1 = TransformPoint(mathUtil, sketchTransform, viewTransform, start.X, start.Y, start.Z);
            double[] p2 = TransformPoint(mathUtil, sketchTransform, viewTransform, end.X, end.Y, end.Z);
            if (p1 == null || p2 == null)
                return;

            double dx = p2[0] - p1[0];
            double dy = p2[1] - p1[1];
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.001)
            { LogDiagnostic("SKIP sketch segment length <= 0.001 sheet m: " + length); return; }

            double angle = Math.Atan2(dy, dx);
            if (angle < 0)
                angle += Math.PI;
            if (angle >= Math.PI)
                angle -= Math.PI;

            double midX = (p1[0] + p2[0]) / 2.0;
            double midY = (p1[1] + p2[1]) / 2.0;
            double normalX = -Math.Sin(angle);
            double normalY = Math.Cos(angle);

            BendInfo info = new BendInfo
            {
                Geometry = segment,
                IsEdge = false,
                AngleGroup = Math.Round(angle * 180.0 / Math.PI, 1),
                SortKey = midX * normalX + midY * normalY,
                MidX = midX,
                MidY = midY,
                NormalX = normalX,
                NormalY = normalY,
                IsBoundingBox = isBoundingBox,
                StartPoint = start,
                EndPoint = end,
                StartX = p1[0],
                StartY = p1[1],
                EndX = p2[0],
                EndY = p2[1],
                Length = length
            };

            bends.Add(info);
        }

        private int CreateDimensions(
            ModelDoc2 model,
            List<BendInfo> bends,
            SelectData selectData,
            double minY,
            double maxY,
            double centerX,
            double centerY,
            SolidWorks.Interop.sldworks.View view,
            List<string> createdDistanceKeys)
        {
            int dimensionCount = 0;
            int groupStart = 0;

            for (int i = 0; i < bends.Count; i++)
            {
                bool isGroupEnd =
                    i == bends.Count - 1 ||
                    GetUndirectedAngleDifference(bends[i + 1].AngleGroup, bends[i].AngleGroup) > 0.1;

                if (!isGroupEnd)
                    continue;

                // Moi group chi gom Bend-Line that. Sap xep lai tren truc normal chuan
                // de tao chain lien tuc, khong phu thuoc X/Y tuyet doi cua Drawing Sheet.
                List<BendInfo> group = new List<BendInfo>();
                for (int k = groupStart; k <= i; k++)
                {
                    if (!bends[k].IsBoundingBox && !bends[k].IsEdge)
                        group.Add(bends[k]);
                }

                LogGeometry("ANGLE_GROUP", group);
                if (group.Count >= 2)
                {
                    double tangentX;
                    double tangentY;
                    double normalX;
                    double normalY;
                    GetCanonicalAxes(
                        group[0].AngleGroup,
                        out tangentX,
                        out tangentY,
                        out normalX,
                        out normalY);

                    group.Sort(delegate (BendInfo a, BendInfo b)
                    {
                        double pa = ProjectPoint(a.MidX, a.MidY, normalX, normalY);
                        double pb = ProjectPoint(b.MidX, b.MidY, normalX, normalY);
                        return pa.CompareTo(pb);
                    });

                    // Mot chain chi dung MOT vi tri theo truc tangent.
                    // Vi tri nay duoc tinh tu chinh cac Bend-Line trong group,
                    // nen khi keo/rotate Drawing View, logic chain van giu nguyen.
                    double chainT = GetChainTangentCoordinate(
                        group,
                        group[0].AngleGroup,
                        tangentX,
                        tangentY);

                    for (int k = 0; k < group.Count - 1; k++)
                    {
                        BendInfo first = group[k];
                        BendInfo second = group[k + 1];

                        double n1 = ProjectPoint(first.MidX, first.MidY, normalX, normalY);
                        double n2 = ProjectPoint(second.MidX, second.MidY, normalX, normalY);
                        double distance = Math.Abs(n2 - n1);
                        LogDiagnostic("CHAIN_PAIR distance=" + distance.ToString("G9", CultureInfo.InvariantCulture) + " A=" + Describe(first) + " B=" + Describe(second));
                        if (distance <= 0.001)
                        { LogDiagnostic("SKIP chain distance <= 0.001 sheet m"); continue; }

                        // Tat ca DIM trong group nam tren cung 1 chain line.
                        double chainN = (n1 + n2) / 2.0;
                        double dimensionX = tangentX * chainT + normalX * chainN;
                        double dimensionY = tangentY * chainT + normalY * chainN;

                        // Kiem tra cap hinh hoc; chi ghi nhan sau khi DIM tao thanh cong.
                        if (!TryRegisterProjectedPair(
                            createdDistanceKeys,
                            first.AngleGroup,
                            n1,
                            n2))
                            continue;

                        model.ClearSelection2(true);
                        if (!SelectGeometry(view, first, false, selectData) ||
                            !SelectGeometry(view, second, true, selectData))
                        {
                            model.ClearSelection2(true);
                            continue;
                        }

                        if (AddLinearDimensionOnly(model, dimensionX, dimensionY))
                        {
                            CommitProjectedPair(createdDistanceKeys, first.AngleGroup, n1, n2);
                            dimensionCount++;
                        }
                    }
                }

                groupStart = i + 1;
            }

            return dimensionCount;
        }

        private List<BendInfo> GetOuterVisibleEdges(
            SolidWorks.Interop.sldworks.View view,
            MathUtility mathUtil,
            MathTransform viewTransform)
        {
            List<BendInfo> visibleLines = new List<BendInfo>();
            Array components = view.GetVisibleComponents() as Array;
            if (components == null)
                return visibleLines;

            foreach (object item in components)
            {
                Component2 component = item as Component2;
                if (component == null)
                    continue;

                Array edges = view.GetVisibleEntities2(
                    component,
                    (int)swViewEntityType_e.swViewEntityType_Edge) as Array;

                if (edges == null)
                    continue;

                foreach (object edgeItem in edges)
                {
                    Edge edge = edgeItem as Edge;
                    Curve curve = edge?.GetCurve() as Curve;
                    if (curve == null || !curve.IsLine())
                        continue;

                    BendInfo info = CreateEdgeInfo(edge, curve, mathUtil, viewTransform);
                    if (info != null)
                        visibleLines.Add(info);
                }
            }

            List<BendInfo> uniqueEdges = new List<BendInfo>();
            foreach (BendInfo line in visibleLines)
                AddUniqueEdge(uniqueEdges, line);

            return uniqueEdges;
        }

        private BendInfo CreateEdgeInfo(
            Edge edge,
            Curve curve,
            MathUtility mathUtil,
            MathTransform viewTransform)
        {
            double startParam;
            double endParam;
            bool isClosed;
            bool isPeriodic;
            if (!curve.GetEndParams(out startParam, out endParam, out isClosed, out isPeriodic))
                return null;

            double[] p1Model = curve.Evaluate(startParam) as double[];
            double[] p2Model = curve.Evaluate(endParam) as double[];
            if (p1Model == null || p2Model == null ||
                p1Model.Length < 3 || p2Model.Length < 3)
                return null;

            double[] p1 = TransformPoint(mathUtil, viewTransform, p1Model[0], p1Model[1], p1Model[2]);
            double[] p2 = TransformPoint(mathUtil, viewTransform, p2Model[0], p2Model[1], p2Model[2]);
            if (p1 == null || p2 == null)
                return null;

            double dx = p2[0] - p1[0];
            double dy = p2[1] - p1[1];
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.001)
            { LogDiagnostic("SKIP edge length <= 0.001 sheet m: " + length); return null; }

            double angle = Math.Atan2(dy, dx);
            if (angle < 0)
                angle += Math.PI;
            if (angle >= Math.PI)
                angle -= Math.PI;

            double midX = (p1[0] + p2[0]) / 2.0;
            double midY = (p1[1] + p2[1]) / 2.0;
            double normalX = -Math.Sin(angle);
            double normalY = Math.Cos(angle);

            return new BendInfo
            {
                Geometry = edge,
                IsEdge = true,
                AngleGroup = Math.Round(angle * 180.0 / Math.PI, 1),
                SortKey = midX * normalX + midY * normalY,
                MidX = midX,
                MidY = midY,
                NormalX = normalX,
                NormalY = normalY,
                IsBoundingBox = true,
                StartVertex = edge.GetStartVertex(),
                EndVertex = edge.GetEndVertex(),
                StartX = p1[0],
                StartY = p1[1],
                EndX = p2[0],
                EndY = p2[1],
                Length = length
            };
        }

        private void AddUniqueEdge(List<BendInfo> edges, BendInfo candidate)
        {
            if (candidate == null)
                return;

            foreach (BendInfo edge in edges)
            {
                if (ReferenceEquals(edge.Geometry, candidate.Geometry))
                    return;

                // Hai doan cung duong thang nhung cach nhau qua ranh/cutout
                // la hai edge rieng; chi bo trung khi hai dau trung nhau.
                bool sameEndpoints =
                    (GetDistance(edge.StartX, edge.StartY, candidate.StartX, candidate.StartY) <= 0.000001 &&
                     GetDistance(edge.EndX, edge.EndY, candidate.EndX, candidate.EndY) <= 0.000001) ||
                    (GetDistance(edge.StartX, edge.StartY, candidate.EndX, candidate.EndY) <= 0.000001 &&
                     GetDistance(edge.EndX, edge.EndY, candidate.StartX, candidate.StartY) <= 0.000001);
                if (sameEndpoints)
                {
                    LogDiagnostic("EDGE_MERGED collinear kept=" + Describe(edge) + " dropped=" + Describe(candidate));
                    return;
                }
            }

            edges.Add(candidate);
        }

        private int CreateOverallDimensions(
            ModelDoc2 model,
            List<BendInfo> primaryLines,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            double minX,
            double maxX,
            double minY,
            double maxY,
            List<BendInfo> fallbackEdges)
        {
            BendInfo left = null;
            BendInfo right = null;
            BendInfo bottom = null;
            BendInfo top = null;
            FindOverallSides(primaryLines, out left, out right, out bottom, out top);

            int count = 0;
            bool horizontalCreated = TryCreateHorizontalOverall(
                model, view, selectData, left, right, minX, maxX, minY);
            if (!horizontalCreated && fallbackEdges != null)
            {
                LogDiagnostic("OVERALL_HORIZONTAL bbox failed; fallback visible edges");
                BendInfo fallbackLeft, fallbackRight, fallbackBottom, fallbackTop;
                FindOverallSides(fallbackEdges,
                    out fallbackLeft, out fallbackRight, out fallbackBottom, out fallbackTop);
                horizontalCreated = TryCreateHorizontalOverall(
                    model, view, selectData, fallbackLeft, fallbackRight, minX, maxX, minY);
            }
            if (horizontalCreated) count++;

            bool verticalCreated = TryCreateVerticalOverall(
                model, view, selectData, primaryLines, bottom, top, maxX, minY, maxY);
            if (!verticalCreated && fallbackEdges != null)
            {
                LogDiagnostic("OVERALL_VERTICAL bbox failed; fallback visible edges");
                BendInfo fallbackLeft, fallbackRight, fallbackBottom, fallbackTop;
                FindOverallSides(fallbackEdges,
                    out fallbackLeft, out fallbackRight, out fallbackBottom, out fallbackTop);
                verticalCreated = TryCreateVerticalOverall(
                    model, view, selectData, fallbackEdges,
                    fallbackBottom, fallbackTop, maxX, minY, maxY);
            }
            if (verticalCreated) count++;

            return count;
        }

        private static void FindOverallSides(
            List<BendInfo> lines,
            out BendInfo left, out BendInfo right,
            out BendInfo bottom, out BendInfo top)
        {
            left = right = bottom = top = null;
            if (lines == null) return;
            foreach (BendInfo edge in lines)
            {
                if (Math.Abs(edge.NormalX) > Math.Abs(edge.NormalY))
                {
                    if (left == null || edge.MidX < left.MidX) left = edge;
                    if (right == null || edge.MidX > right.MidX) right = edge;
                }
                else
                {
                    if (bottom == null || edge.MidY < bottom.MidY) bottom = edge;
                    if (top == null || edge.MidY > top.MidY) top = edge;
                }
            }
        }

        private bool TryCreateHorizontalOverall(
            ModelDoc2 model, SolidWorks.Interop.sldworks.View view,
            SelectData selectData, BendInfo left, BendInfo right,
            double minX, double maxX, double minY)
        {
            if (left == null || right == null || ReferenceEquals(left, right))
                return false;
            model.ClearSelection2(true);
            return SelectGeometry(view, left, false, selectData) &&
                SelectGeometry(view, right, true, selectData) &&
                AddLinearDimensionOnly(model, (minX + maxX) / 2.0, minY - 0.025);
        }

        private bool TryCreateVerticalOverall(
            ModelDoc2 model, SolidWorks.Interop.sldworks.View view,
            SelectData selectData, List<BendInfo> lines,
            BendInfo bottom, BendInfo top,
            double maxX, double minY, double maxY)
        {
            if (bottom == null || top == null || ReferenceEquals(bottom, top))
                return false;

            // A sloped visible edge pair would create an angular dimension.
            // The actual bounding-box sketch has parallel sides and is selected directly.
            bool useExtremeVertices = bottom.IsEdge && top.IsEdge &&
                GetUndirectedAngleDifference(bottom.AngleGroup, top.AngleGroup) > 0.1;
            LogDiagnostic("OVERALL_VERTICAL bottom=" + Describe(bottom) + " top=" + Describe(top)
                + " useExtremeVertices=" + useExtremeVertices);
            if (useExtremeVertices)
                return TryCreateVerticalOverallFromVertices(
                    model, view, lines, maxX, minY, maxY);

            model.ClearSelection2(true);
            return SelectGeometry(view, bottom, false, selectData) &&
                SelectGeometry(view, top, true, selectData) &&
                AddLinearDimensionOnly(model, maxX + 0.025, (minY + maxY) / 2.0);
        }

        private bool TryCreateVerticalOverallFromVertices(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            List<BendInfo> edges,
            double maxX,
            double minY,
            double maxY)
        {
            object bottomVertex = null;
            object topVertex = null;
            double bottomY = double.MaxValue;
            double topY = double.MinValue;
            double bottomX = double.MinValue;
            double topX = double.MinValue;

            foreach (BendInfo edge in edges)
            {
                if (!edge.IsEdge)
                    continue;

                ConsiderVerticalExtreme(edge.StartVertex, edge.StartX, edge.StartY,
                    ref bottomVertex, ref bottomX, ref bottomY,
                    ref topVertex, ref topX, ref topY);
                ConsiderVerticalExtreme(edge.EndVertex, edge.EndX, edge.EndY,
                    ref bottomVertex, ref bottomX, ref bottomY,
                    ref topVertex, ref topX, ref topY);
            }

            if (bottomVertex == null || topVertex == null || topY - bottomY <= 0.000001)
            {
                LogDiagnostic("OVERALL_VERTICAL vertex extrema unavailable");
                return false;
            }

            LogDiagnostic("OVERALL_VERTICAL extrema bottom=(" + bottomX + "," + bottomY
                + ") top=(" + topX + "," + topY + ") projectedSheetHeight=" + (topY - bottomY));
            model.ClearSelection2(true);
            bool bottomSelected = view.SelectEntity(bottomVertex, false);
            bool topSelected = bottomSelected && view.SelectEntity(topVertex, true);
            LogDiagnostic("OVERALL_VERTICAL select bottom=" + bottomSelected + " top=" + topSelected);
            if (!topSelected)
            {
                model.ClearSelection2(true);
                return false;
            }

            DisplayDimension dimension = model.AddVerticalDimension2(
                maxX + 0.025, (minY + maxY) / 2.0, 0) as DisplayDimension;
            if (dimension == null)
            {
                LogDiagnostic("OVERALL_VERTICAL AddVerticalDimension2 returned null");
                model.ClearSelection2(true);
                return false;
            }

            LogDiagnostic("OVERALL_VERTICAL created type=" + dimension.GetType()
                + " expectedModelMm=" + ((topY - bottomY) * 1000.0 / view.ScaleDecimal));
            return true;
        }

        private static void ConsiderVerticalExtreme(
            object vertex, double x, double y,
            ref object bottomVertex, ref double bottomX, ref double bottomY,
            ref object topVertex, ref double topX, ref double topY)
        {
            if (vertex == null)
                return;

            // Prefer the right-hand corner when both ends share the same Y.
            if (y < bottomY - 0.000001 ||
                (Math.Abs(y - bottomY) <= 0.000001 && x > bottomX))
            {
                bottomVertex = vertex;
                bottomX = x;
                bottomY = y;
            }

            if (y > topY + 0.000001 ||
                (Math.Abs(y - topY) <= 0.000001 && x > topX))
            {
                topVertex = vertex;
                topX = x;
                topY = y;
            }
        }

        private int CreateOuterOrthogonalDimensions(
            ModelDoc2 model,
            List<BendInfo> bends,
            List<BendInfo> outerEdges,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            double centerX,
            double centerY)
        {
            int count = 0;
            List<string> processedPairs = new List<string>();

            foreach (BendInfo bend in bends)
            {
                if (bend.IsBoundingBox ||
                    bend.IsEdge ||
                    IsDiagonal(bend) ||
                    CountRealBendsAtAngle(bends, bend.AngleGroup) == 1 ||
                    HasOuterParallelRealBend(bend, bends, centerX, centerY))
                    continue;

                BendInfo edge = FindOutermostParallelLine(bend, outerEdges, centerX, centerY, false);
                if (edge == null)
                    continue;

                string pairKey = GetPairKey(edge, bend);
                if (processedPairs.Contains(pairKey))
                    continue;

                processedPairs.Add(pairKey);

                double measureX = (edge.MidX + bend.MidX) / 2.0;
                double measureY = (edge.MidY + bend.MidY) / 2.0;
                double direction = GetOutwardDirection(edge, centerX, centerY);
                double offset = 0.01;

                model.ClearSelection2(true);
                if (!SelectGeometry(view, edge, false, selectData) ||
                    !SelectGeometry(view, bend, true, selectData))
                    continue;

                if (model.AddDimension2(
                    measureX + direction * edge.NormalX * offset,
                    measureY + direction * edge.NormalY * offset,
                    0) != null)
                    count++;
            }

            return count;
        }

        private bool HasOuterParallelRealBend(
            BendInfo bend,
            List<BendInfo> bends,
            double centerX,
            double centerY)
        {
            double bendSide =
                (bend.MidX - centerX) * bend.NormalX +
                (bend.MidY - centerY) * bend.NormalY;

            foreach (BendInfo candidate in bends)
            {
                if (ReferenceEquals(candidate, bend) ||
                    candidate.IsBoundingBox ||
                    candidate.IsEdge ||
                    GetUndirectedAngleDifference(candidate.AngleGroup, bend.AngleGroup) > ParallelAngleTolerance)
                    continue;

                if (!HasTangentOverlap(candidate, bend, bend.NormalY, -bend.NormalX))
                    continue;

                double candidateSide =
                    (candidate.MidX - centerX) * bend.NormalX +
                    (candidate.MidY - centerY) * bend.NormalY;

                if (bendSide >= 0 && candidateSide > bendSide + 0.001)
                    return true;

                if (bendSide < 0 && candidateSide < bendSide - 0.001)
                    return true;
            }

            return false;
        }

        private bool HasOuterParallelRealBend(
            BendInfo bend,
            List<BendInfo> bends,
            double centerX,
            double centerY,
            double direction)
        {
            double bendSide =
                (bend.MidX - centerX) * bend.NormalX +
                (bend.MidY - centerY) * bend.NormalY;

            foreach (BendInfo candidate in bends)
            {
                if (ReferenceEquals(candidate, bend) ||
                    candidate.IsBoundingBox ||
                    candidate.IsEdge ||
                    GetUndirectedAngleDifference(candidate.AngleGroup, bend.AngleGroup) > ParallelAngleTolerance)
                    continue;

                if (!HasTangentOverlap(candidate, bend, bend.NormalY, -bend.NormalX))
                    continue;

                double candidateSide =
                    (candidate.MidX - centerX) * bend.NormalX +
                    (candidate.MidY - centerY) * bend.NormalY;

                if ((candidateSide - bendSide) * direction > 0.001)
                    return true;
            }

            return false;
        }

        private int CreateSingleBendEdgePointDimensions(
            ModelDoc2 model,
            List<BendInfo> bends,
            List<BendInfo> outerEdges,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            double centerX,
            double centerY,
            List<string> createdDistanceKeys)
        {
            int count = 0;
            foreach (BendInfo bend in bends)
            {
                if (bend.IsBoundingBox ||
                    bend.IsEdge)
                    continue;

                for (int side = -1; side <= 1; side += 2)
                {
                    double direction = side;
                    LogDiagnostic("SIDE direction=" + direction + " " + Describe(bend));
                    if (HasOuterParallelRealBend(bend, bends, centerX, centerY, direction))
                    { LogDiagnostic("SKIP another parallel bend is farther outward"); continue; }

                    BendInfo outerEdge = FindOutermostLineByBendDirection(
                        bend,
                        outerEdges,
                        centerX,
                        centerY,
                        direction);

                    LogDiagnostic("CHOSEN_OUTER " + Describe(outerEdge));
                    if (outerEdge == null)
                    { LogDiagnostic("SKIP no eligible outer edge"); continue; }

                    if (GetUndirectedAngleDifference(outerEdge.AngleGroup, bend.AngleGroup) > ParallelAngleTolerance)
                        continue;

                    double tangentX;
                    double tangentY;
                    double normalX;
                    double normalY;
                    GetCanonicalAxes(
                        bend.AngleGroup,
                        out tangentX,
                        out tangentY,
                        out normalX,
                        out normalY);

                    // Vi tri dim nam trong doan overlap cua chinh cap edge/bend.
                    // ChainT toan cuc co the dat DIM vao mot khu vuc khac.
                    double chainT = GetPairTangentCoordinate(
                        bend, outerEdge, tangentX, tangentY);

                    int created = CreateEdgeToBendLineDimension(
                        model,
                        view,
                        selectData,
                        outerEdge,
                        bend,
                        chainT,
                        tangentX,
                        tangentY,
                        normalX,
                        normalY,
                        createdDistanceKeys);

                    LogDiagnostic("EDGE_BEND created=" + created + " bend=" + Describe(bend)
                        + " edge=" + Describe(outerEdge));
                    if (created > 0)
                        count += created;
                }
            }

            return count;
        }

        private bool HasMatchingParallelBendChainDistance(
            BendInfo bend,
            BendInfo edge,
            List<BendInfo> bends)
        {
            List<BendInfo> group = new List<BendInfo>();
            foreach (BendInfo item in bends)
            {
                if (!item.IsBoundingBox &&
                    !item.IsEdge &&
                    GetUndirectedAngleDifference(item.AngleGroup, bend.AngleGroup) <= ParallelAngleTolerance)
                    group.Add(item);
            }

            if (group.Count < 2)
                return false;

            group.Sort(CompareBends);

            double targetDistance = Math.Abs(
                (edge.MidX - bend.MidX) * bend.NormalX +
                (edge.MidY - bend.MidY) * bend.NormalY);
            for (int i = 0; i < group.Count - 1; i++)
            {
                double chainDistance = Math.Abs(
                    (group[i + 1].MidX - group[i].MidX) * group[i].NormalX +
                    (group[i + 1].MidY - group[i].MidY) * group[i].NormalY);
                if (Math.Abs(chainDistance - targetDistance) <= 0.002)
                    return true;
            }

            return false;
        }

        private int CreateEdgeToBendPointDimensionIfUnique(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            BendInfo edge,
            SketchPoint point,
            double pointX,
            double pointY,
            double centerX,
            double centerY,
            List<string> createdDistanceKeys)
        {
            double distance = Math.Abs(
                (pointX - edge.MidX) * edge.NormalX +
                (pointY - edge.MidY) * edge.NormalY);

            if (!TryRegisterDimensionDistance(
                createdDistanceKeys,
                edge.AngleGroup,
                distance,
                (edge.MidX + pointX) / 2.0,
                (edge.MidY + pointY) / 2.0,
                false))
                return 0;

            return CreateEdgeToBendPointDimension(
                model,
                view,
                selectData,
                edge,
                point,
                pointX,
                pointY,
                centerX,
                centerY);
        }

        private int CreateEdgeToBendLineDimension(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            BendInfo edge,
            BendInfo bend,
            double chainT,
            double tangentX,
            double tangentY,
            double normalX,
            double normalY,
            List<string> createdDistanceKeys)
        {
            double edgeN = ProjectPoint(edge.MidX, edge.MidY, normalX, normalY);
            double bendN = ProjectPoint(bend.MidX, bend.MidY, normalX, normalY);
            double distance = Math.Abs(edgeN - bendN);
            if (distance <= 0.001)
            { LogDiagnostic("SKIP edge/bend distance <= 0.001 sheet m: " + distance); return 0; }

            // Edge -> Bend dau/cuoi nam tren dung chain line cua group Bend-Line.
            double chainN = (edgeN + bendN) / 2.0;
            double dimensionX = tangentX * chainT + normalX * chainN;
            double dimensionY = tangentY * chainT + normalY * chainN;

            if (!TryRegisterProjectedPair(
                createdDistanceKeys,
                bend.AngleGroup,
                edgeN,
                bendN))
                return 0;

            model.ClearSelection2(true);
            if (!SelectGeometry(view, edge, false, selectData) ||
                !SelectGeometry(view, bend, true, selectData))
                return 0;

            if (!AddLinearDimensionOnly(model, dimensionX, dimensionY))
                return 0;

            CommitProjectedPair(createdDistanceKeys, bend.AngleGroup, edgeN, bendN);
            return 1;
        }

        private BendInfo FindOutermostLineByBendDirection(
            BendInfo bend,
            List<BendInfo> lines,
            double centerX,
            double centerY,
            double direction)
        {
            BendInfo outermost = null;
            double bestScore = 0;
            double bendSide =
                (bend.MidX - centerX) * bend.NormalX +
                (bend.MidY - centerY) * bend.NormalY;
            double tangentX = bend.NormalY;
            double tangentY = -bend.NormalX;

            foreach (BendInfo line in lines)
            {
                // Chi xet canh song song voi bend line ngay tu dau.
                // Neu loc sau khi da chon outermost, canh ngang/cheo co the
                // chiem vi tri ung vien va lam bo qua bend line doc.
                if (GetUndirectedAngleDifference(line.AngleGroup, bend.AngleGroup) > ParallelAngleTolerance)
                    continue;

                if (!HasTangentOverlap(line, bend, tangentX, tangentY))
                { LogDiagnostic("OUTER_REJECT no tangent overlap " + Describe(line)); continue; }

                double lineSide =
                    (line.MidX - centerX) * bend.NormalX +
                    (line.MidY - centerY) * bend.NormalY;
                double score = (lineSide - bendSide) * direction;

                LogDiagnostic("OUTER_CANDIDATE score=" + score + " best=" + bestScore + " " + Describe(line));
                if (score <= 0.001 || score <= bestScore)
                { LogDiagnostic("OUTER_REJECT score <= cutoff or not farther than best"); continue; }

                outermost = line;
                bestScore = score;
            }

            return outermost;
        }

        private double GetClosestAlongDistance(
            BendInfo line,
            BendInfo bend,
            double tangentX,
            double tangentY)
        {
            double best = Math.Abs(
                (line.MidX - bend.MidX) * tangentX +
                (line.MidY - bend.MidY) * tangentY);

            best = Math.Min(
                best,
                Math.Abs((line.StartX - bend.StartX) * tangentX + (line.StartY - bend.StartY) * tangentY));
            best = Math.Min(
                best,
                Math.Abs((line.StartX - bend.EndX) * tangentX + (line.StartY - bend.EndY) * tangentY));
            best = Math.Min(
                best,
                Math.Abs((line.EndX - bend.StartX) * tangentX + (line.EndY - bend.StartY) * tangentY));
            best = Math.Min(
                best,
                Math.Abs((line.EndX - bend.EndX) * tangentX + (line.EndY - bend.EndY) * tangentY));

            return best;
        }

        private bool HasTangentOverlap(BendInfo first, BendInfo second, double tangentX, double tangentY)
        {
            double a1 = ProjectPoint(first.StartX, first.StartY, tangentX, tangentY);
            double a2 = ProjectPoint(first.EndX, first.EndY, tangentX, tangentY);
            double b1 = ProjectPoint(second.StartX, second.StartY, tangentX, tangentY);
            double b2 = ProjectPoint(second.EndX, second.EndY, tangentX, tangentY);
            return Math.Min(Math.Max(a1, a2), Math.Max(b1, b2)) -
                Math.Max(Math.Min(a1, a2), Math.Min(b1, b2)) > 0.000001;
        }

        private double GetPairTangentCoordinate(BendInfo bend, BendInfo edge, double tangentX, double tangentY)
        {
            double a1 = ProjectPoint(bend.StartX, bend.StartY, tangentX, tangentY);
            double a2 = ProjectPoint(bend.EndX, bend.EndY, tangentX, tangentY);
            double b1 = ProjectPoint(edge.StartX, edge.StartY, tangentX, tangentY);
            double b2 = ProjectPoint(edge.EndX, edge.EndY, tangentX, tangentY);
            return (Math.Max(Math.Min(a1, a2), Math.Min(b1, b2)) +
                Math.Min(Math.Max(a1, a2), Math.Max(b1, b2))) / 2.0;
        }

        private int CreateEdgeToBendPointDimension(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            BendInfo edge,
            SketchPoint point,
            double pointX,
            double pointY,
            double centerX,
            double centerY)
        {
            if (point == null)
                return 0;

            double direction = GetOutwardDirection(edge, centerX, centerY);
            double offset = 0.02;
            double dimensionX =
                (edge.MidX + pointX) / 2.0 +
                direction * edge.NormalX * offset;
            double dimensionY =
                (edge.MidY + pointY) / 2.0 +
                direction * edge.NormalY * offset;

            model.ClearSelection2(true);
            if (!SelectGeometry(view, edge, false, selectData) ||
                !point.Select4(true, selectData))
                return 0;

            return AddLinearDimensionOnly(model, dimensionX, dimensionY) ? 1 : 0;
        }

        private int CreateOuterFlapDimensions(
            ModelDoc2 model,
            List<BendInfo> bends,
            List<BendInfo> outerEdges,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            double centerX,
            double centerY)
        {
            int count = 0;
            List<BendInfo> processedEdges = new List<BendInfo>();

            foreach (BendInfo edge in outerEdges)
            {
                if (!IsDiagonalEdge(edge) || IsDuplicateEdge(edge, processedEdges))
                    continue;

                BendInfo nearestBend = FindNearestParallelRealBend(edge, bends);
                if (nearestBend == null)
                    continue;

                processedEdges.Add(edge);

                double measureX = (edge.MidX + nearestBend.MidX) / 2.0;
                double measureY = (edge.MidY + nearestBend.MidY) / 2.0;
                double direction = GetOutwardDirection(edge, centerX, centerY);
                double offset = 0.02;

                model.ClearSelection2(true);
                if (!SelectGeometry(view, edge, false, selectData) ||
                    !SelectGeometry(view, nearestBend, true, selectData))
                    continue;

                if (AddLinearDimensionOnly(
                    model,
                    measureX + direction * edge.NormalX * offset,
                    measureY + direction * edge.NormalY * offset))
                    count++;
            }

            return count;
        }

        private int CreateDiagonalTransitionDimensions(
            ModelDoc2 model,
            List<BendInfo> bends,
            List<BendInfo> outerEdges,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            double centerX,
            double centerY)
        {
            int count = 0;
            List<double> processedAngles = new List<double>();

            foreach (BendInfo bend in bends)
            {
                if (bend.IsBoundingBox ||
                    bend.IsEdge ||
                    !IsDiagonal(bend) ||
                    ContainsAngle(processedAngles, bend.AngleGroup) ||
                    CountRealBendsAtAngle(bends, bend.AngleGroup) != 1)
                    continue;

                BendInfo outerEdge = FindOutermostParallelLine(bend, outerEdges, centerX, centerY, true);
                BendInfo innerBend = FindNearestNonParallelRealBend(bend, bends);
                if (outerEdge == null || innerBend == null)
                    continue;

                processedAngles.Add(bend.AngleGroup);

                count += CreateLineToPointDimension(
                    model,
                    view,
                    selectData,
                    bend,
                    bend.StartX,
                    bend.StartY,
                    FindNearestPoint(bend.StartX, bend.StartY, innerBend),
                    centerX,
                    centerY);

                count += CreateLineToPointDimension(
                    model,
                    view,
                    selectData,
                    bend,
                    bend.EndX,
                    bend.EndY,
                    FindNearestPoint(bend.EndX, bend.EndY, innerBend),
                    centerX,
                    centerY);
            }

            return count;
        }

        private int CountRealBendsAtAngle(List<BendInfo> bends, double angle)
        {
            int count = 0;

            foreach (BendInfo bend in bends)
            {
                if (!bend.IsBoundingBox &&
                    !bend.IsEdge &&
                    GetUndirectedAngleDifference(bend.AngleGroup, angle) <= ParallelAngleTolerance)
                    count++;
            }

            return count;
        }

        private bool ContainsAngle(List<double> angles, double angle)
        {
            foreach (double item in angles)
            {
                if (Math.Abs(item - angle) <= ParallelAngleTolerance)
                    return true;
            }

            return false;
        }

        private string GetPairKey(BendInfo edge, BendInfo bend)
        {
            return Math.Round(edge.AngleGroup, 1) + "|" +
                Math.Round(edge.SortKey, 4) + "|" +
                Math.Round(bend.SortKey, 4);
        }

        private BendInfo FindNearestNonParallelRealBend(BendInfo line, List<BendInfo> bends)
        {
            BendInfo nearest = null;
            double nearestDistance = double.MaxValue;

            foreach (BendInfo bend in bends)
            {
                if (bend.IsBoundingBox ||
                    bend.IsEdge ||
                    GetUndirectedAngleDifference(line.AngleGroup, bend.AngleGroup) <= ParallelAngleTolerance)
                    continue;

                double dx = bend.MidX - line.MidX;
                double dy = bend.MidY - line.MidY;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < nearestDistance)
                {
                    nearest = bend;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private PointInfo FindNearestPoint(double x, double y, BendInfo bend)
        {
            double startDistance = GetDistance(x, y, bend.StartX, bend.StartY);
            double endDistance = GetDistance(x, y, bend.EndX, bend.EndY);

            if (startDistance <= endDistance)
            {
                return new PointInfo
                {
                    Point = bend.StartPoint as SketchPoint,
                    X = bend.StartX,
                    Y = bend.StartY
                };
            }

            return new PointInfo
            {
                Point = bend.EndPoint as SketchPoint,
                X = bend.EndX,
                Y = bend.EndY
            };
        }

        private int CreateLineToPointDimension(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            BendInfo line,
            double linePointX,
            double linePointY,
            PointInfo point,
            double centerX,
            double centerY)
        {
            if (point == null || point.Point == null)
                return 0;

            double direction = GetOutwardDirection(line, centerX, centerY);
            double offset = 0.02;
            double dimensionX =
                (linePointX + point.X) / 2.0 +
                direction * line.NormalX * offset;
            double dimensionY =
                (linePointY + point.Y) / 2.0 +
                direction * line.NormalY * offset;

            model.ClearSelection2(true);
            if (!SelectGeometry(view, line, false, selectData) ||
                !point.Point.Select4(true, selectData))
                return 0;

            return AddLinearDimensionOnly(model, dimensionX, dimensionY) ? 1 : 0;
        }

        private bool AddLinearDimensionOnly(ModelDoc2 model, double x, double y)
        {
            LogDiagnostic("ADD_DIM position=(" + x + "," + y + ")");
            DisplayDimension displayDimension = model.AddDimension2(x, y, 0) as DisplayDimension;
            if (displayDimension == null)
            { LogDiagnostic("ADD_DIM FAILED returned null; registered key remains reserved"); return false; }

            int dimensionType = displayDimension.GetType();
            LogDiagnostic("ADD_DIM returned type=" + dimensionType);
            if (dimensionType != (int)swDimensionType_e.swAngularDimension)
                return true;

            LogDiagnostic("ADD_DIM REJECT angular dimension; deleting");
            Annotation annotation = displayDimension.GetAnnotation() as Annotation;
            if (annotation != null && annotation.Select3(false, null))
                model.EditDelete();

            model.ClearSelection2(true);
            return false;
        }

        private double GetDistance(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void GetCanonicalAxes(
            double angleDegrees,
            out double tangentX,
            out double tangentY,
            out double normalX,
            out double normalY)
        {
            double angle = angleDegrees % 180.0;
            if (angle < 0)
                angle += 180.0;

            // Line la vo huong: 179.9 do va -0.1 do phai cho cung mot he truc.
            if (angle >= 90.0)
                angle -= 180.0;

            double radians = angle * Math.PI / 180.0;
            tangentX = Math.Cos(radians);
            tangentY = Math.Sin(radians);
            normalX = -tangentY;
            normalY = tangentX;
        }

        private double ProjectPoint(
            double x,
            double y,
            double axisX,
            double axisY)
        {
            return x * axisX + y * axisY;
        }

        private double GetChainTangentCoordinate(
            List<BendInfo> bends,
            double angleGroup,
            double tangentX,
            double tangentY)
        {
            double sum = 0.0;
            int count = 0;

            foreach (BendInfo bend in bends)
            {
                if (bend == null || bend.IsBoundingBox || bend.IsEdge)
                    continue;

                if (GetUndirectedAngleDifference(bend.AngleGroup, angleGroup) > 0.1)
                    continue;

                sum += ProjectPoint(bend.MidX, bend.MidY, tangentX, tangentY);
                count++;
            }

            return count > 0 ? sum / count : 0.0;
        }

        private bool TryRegisterProjectedPair(
            List<string> createdKeys,
            double angle,
            double projectionA,
            double projectionB)
        {
            double minProjection = Math.Min(projectionA, projectionB);
            double maxProjection = Math.Max(projectionA, projectionB);

            string key =
                "PAIR:" +
                Math.Round(GetAngleSortKey(angle), 1).ToString("0.0") +
                ":" +
                Math.Round(minProjection * 1000.0, 2).ToString("0.00") +
                ":" +
                Math.Round(maxProjection * 1000.0, 2).ToString("0.00");

            if (createdKeys.Contains(key))
            { LogDiagnostic("SKIP duplicate key=" + key); return false; }

            LogDiagnostic("CANDIDATE key=" + key);
            return true;
        }

        private void CommitProjectedPair(List<string> createdKeys, double angle, double projectionA, double projectionB)
        {
            string key = "PAIR:" +
                Math.Round(GetAngleSortKey(angle), 1).ToString("0.0") + ":" +
                Math.Round(Math.Min(projectionA, projectionB) * 1000.0, 2).ToString("0.00") + ":" +
                Math.Round(Math.Max(projectionA, projectionB) * 1000.0, 2).ToString("0.00");
            createdKeys.Add(key);
            LogDiagnostic("COMMIT key=" + key);
        }

        private bool TryRegisterDimensionDistance(
            List<string> createdKeys,
            double angle,
            double distance,
            double positionX,
            double positionY,
            bool collapseCenterAxis)
        {
            string key =
                Math.Round(angle, 1).ToString("0.0") +
                ":" +
                Math.Round(distance * 1000.0, 1).ToString("0.0");

            if (collapseCenterAxis)
            {
                key += ":CENTER";
            }
            else
            {
                key +=
                    ":" +
                    Math.Round(positionX * 1000.0 / 20.0).ToString("0") +
                    ":" +
                    Math.Round(positionY * 1000.0 / 20.0).ToString("0");
            }

            if (createdKeys.Contains(key))
            { LogDiagnostic("SKIP duplicate key=" + key); return false; }

            LogDiagnostic("REGISTER before creation key=" + key);
            createdKeys.Add(key);
            return true;
        }

        private bool IsCenterAxisDimension(
            double dimensionX,
            double dimensionY,
            double centerX,
            double centerY)
        {
            return Math.Abs(dimensionX - centerX) <= 0.003 ||
                Math.Abs(dimensionY - centerY) <= 0.003;
        }

        private BendInfo FindNearestParallelLine(
            BendInfo bend,
            List<BendInfo> lines,
            bool diagonalOnly)
        {
            BendInfo nearest = null;
            double nearestDistance = double.MaxValue;

            foreach (BendInfo line in lines)
            {
                if ((diagonalOnly && !IsDiagonalEdge(line)) ||
                    GetUndirectedAngleDifference(line.AngleGroup, bend.AngleGroup) > ParallelAngleTolerance)
                    continue;

                double distance = Math.Abs(
                    (line.MidX - bend.MidX) * bend.NormalX +
                    (line.MidY - bend.MidY) * bend.NormalY);
                if (distance > 0.001 && distance < nearestDistance)
                {
                    nearest = line;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private BendInfo FindNearestParallelRealBend(BendInfo edge, List<BendInfo> bends)
        {
            BendInfo nearest = null;
            double nearestDistance = double.MaxValue;

            foreach (BendInfo bend in bends)
            {
                if (!bend.IsBoundingBox &&
                    !bend.IsEdge &&
                    GetUndirectedAngleDifference(edge.AngleGroup, bend.AngleGroup) <= ParallelAngleTolerance)
                {
                    double distance = Math.Abs(
                (edge.MidX - bend.MidX) * bend.NormalX +
                (edge.MidY - bend.MidY) * bend.NormalY);
                    if (distance > 0.001 && distance < nearestDistance)
                    {
                        nearest = bend;
                        nearestDistance = distance;
                    }
                }
            }

            return nearest;
        }

        private BendInfo FindOutermostParallelLine(
            BendInfo bend,
            List<BendInfo> lines,
            double centerX,
            double centerY,
            bool diagonalOnly)
        {
            BendInfo outermost = null;
            double bestScore = 0;
            double bendSide =
                (bend.MidX - centerX) * bend.NormalX +
                (bend.MidY - centerY) * bend.NormalY;
            double direction = bendSide >= 0 ? 1.0 : -1.0;

            foreach (BendInfo line in lines)
            {
                if (diagonalOnly && !IsDiagonalEdge(line))
                    continue;

                double angleDiff = GetUndirectedAngleDifference(line.AngleGroup, bend.AngleGroup);
                if (angleDiff > ParallelAngleTolerance)
                    continue;

                double lineSide =
                    (line.MidX - centerX) * bend.NormalX +
                    (line.MidY - centerY) * bend.NormalY;
                double score = (lineSide - bendSide) * direction;

                if (score <= 0.001)
                    continue;

                if (score <= bestScore)
                    continue;

                outermost = line;
                bestScore = score;
            }

            return outermost;
        }

        private bool IsDuplicateEdge(BendInfo edge, List<BendInfo> processedEdges)
        {
            foreach (BendInfo processed in processedEdges)
            {
                if (GetUndirectedAngleDifference(processed.AngleGroup, edge.AngleGroup) <= 0.1 &&
                    Math.Abs(processed.SortKey - edge.SortKey) <= 0.001)
                    return true;
            }

            return false;
        }

        private bool IsDiagonalEdge(BendInfo edge)
        {
            return edge.IsEdge && IsDiagonal(edge);
        }

        private bool IsDiagonal(BendInfo line)
        {
            return Math.Abs(line.NormalX) > 0.15 &&
                Math.Abs(line.NormalY) > 0.15;
        }

        private double GetOutwardDirection(BendInfo edge, double centerX, double centerY)
        {
            double fromCenterX = edge.MidX - centerX;
            double fromCenterY = edge.MidY - centerY;

            return fromCenterX * edge.NormalX + fromCenterY * edge.NormalY >= 0
                ? 1.0
                : -1.0;
        }

        private bool SelectGeometry(
            SolidWorks.Interop.sldworks.View view,
            BendInfo bend,
            bool append,
            SelectData selectData)
        {
            if (view == null || bend == null)
                return false;

            try
            {
                // Edge cua model van giu nguyen cach select cu.
                if (bend.IsEdge)
                {
                    bool selected = view.SelectEntity(bend.Geometry, append);
                    LogDiagnostic("SELECT edge result=" + selected + " append=" + append + " " + Describe(bend));
                    return selected;
                }

                if (selectData == null)
                    return false;

                // Luon gan lai context View ngay truoc moi lan select. SelectData la COM object
                // va context cua no co the bi SolidWorks thay doi sau ClearSelection/rebuild.
                selectData.View = view;

                SketchSegment segment;

                if (!bend.IsBoundingBox)
                {
                    // Bend-Line: luon reacquire entity hien tai tu view.GetBendLines()
                    // va match bang hinh hoc; khong giu COM SketchSegment cu qua rebuild.
                    segment = ResolveCurrentBendSegment(view, bend);
                }
                else
                {
                    // Bounding-box sketch khong phai bend line auto-generated; giu logic cu.
                    segment = bend.Geometry as SketchSegment;
                }

                bool selectedSegment = segment != null && segment.Select4(append, selectData);
                LogDiagnostic("SELECT sketch result=" + selectedSegment + " resolved=" + (segment != null) + " append=" + append + " " + Describe(bend));
                return selectedSegment;
            }
            catch (COMException)
            {
                // Neu COM object vua bi invalidate dung luc select, thu reacquire Bend-Line
                // mot lan cuoi. Khong thay doi cap entity hay vi tri DIM.
                if (bend.IsEdge || bend.IsBoundingBox || selectData == null)
                    return false;

                SketchSegment refreshed = FindCurrentBendSegment(view, bend);
                if (refreshed == null)
                    return false;

                bend.Geometry = refreshed;

                try
                {
                    selectData.View = view;
                    bool retried = refreshed.Select4(append, selectData);
                    LogDiagnostic("SELECT retry result=" + retried + " " + Describe(bend));
                    return retried;
                }
                catch (COMException)
                {
                    return false;
                }
            }
        }

        private SketchSegment ResolveCurrentBendSegment(
            SolidWorks.Interop.sldworks.View view,
            BendInfo target)
        {
            if (view == null || target == null)
                return null;

            // Bend-Line trong Drawing co the bi SolidWorks tao lai sau rebuild.
            // Khong dung dynamic persistent-reference nua vi mot so version khong expose
            // cac method do cho Drawing SketchSegment va gay RuntimeBinderException.
            // Moi lan select, lay lai Bend-Line hien tai va match bang hinh hoc.
            SketchSegment refreshed = FindCurrentBendSegment(view, target);
            if (refreshed != null)
                target.Geometry = refreshed;

            return refreshed;
        }

        private BendInfo CreateTemporaryBendInfo(
            SolidWorks.Interop.sldworks.View view,
            SketchSegment segment)
        {
            SketchLine line = segment as SketchLine;
            if (view == null || line == null)
                return null;

            MathUtility mathUtil = swApp.IGetMathUtility();
            MathTransform viewTransform = view.ModelToViewTransform;
            Sketch sketch = segment.GetSketch();
            MathTransform sketchTransform = sketch?.ModelToSketchTransform?.Inverse() as MathTransform;
            SketchPoint start = line.GetStartPoint2() as SketchPoint;
            SketchPoint end = line.GetEndPoint2() as SketchPoint;

            if (mathUtil == null || viewTransform == null || sketchTransform == null ||
                start == null || end == null)
                return null;

            double[] p1 = TransformPoint(
                mathUtil, sketchTransform, viewTransform,
                start.X, start.Y, start.Z);
            double[] p2 = TransformPoint(
                mathUtil, sketchTransform, viewTransform,
                end.X, end.Y, end.Z);

            if (p1 == null || p2 == null)
                return null;

            double dx = p2[0] - p1[0];
            double dy = p2[1] - p1[1];
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.001)
            { LogDiagnostic("SKIP edge length <= 0.001 sheet m: " + length); return null; }

            double angle = Math.Atan2(dy, dx);
            if (angle < 0)
                angle += Math.PI;
            if (angle >= Math.PI)
                angle -= Math.PI;

            double midX = (p1[0] + p2[0]) / 2.0;
            double midY = (p1[1] + p2[1]) / 2.0;
            double normalX = -Math.Sin(angle);
            double normalY = Math.Cos(angle);

            return new BendInfo
            {
                Geometry = segment,
                IsEdge = false,
                AngleGroup = Math.Round(angle * 180.0 / Math.PI, 1),
                SortKey = midX * normalX + midY * normalY,
                MidX = midX,
                MidY = midY,
                NormalX = normalX,
                NormalY = normalY,
                IsBoundingBox = false,
                StartPoint = start,
                EndPoint = end,
                StartX = p1[0],
                StartY = p1[1],
                EndX = p2[0],
                EndY = p2[1],
                Length = length
            };
        }

        private SketchSegment FindCurrentBendSegment(
            SolidWorks.Interop.sldworks.View view,
            BendInfo target)
        {
            if (view == null || target == null)
                return null;

            try
            {
                Array items = view.GetBendLines() as Array;
                if (items == null)
                    return null;

                SketchSegment best = null;
                double bestScore = double.MaxValue;

                foreach (object item in items)
                {
                    SketchSegment segment = item as SketchSegment;
                    if (segment == null)
                        continue;

                    BendInfo probe = CreateTemporaryBendInfo(view, segment);
                    if (probe == null)
                        continue;

                    // QUAN TRONG: line la entity VO HUONG. 0 do va 180 do la cung huong.
                    // Truoc day Math.Abs(Angle1-Angle2) lam bend ngang 179.9 do
                    // khong match voi bend 0.1 do, nen SelectGeometry fail va DIM doc khong tao.
                    double angleDiff = GetUndirectedAngleDifference(
                        probe.AngleGroup,
                        target.AngleGroup);

                    if (angleDiff > ParallelAngleTolerance)
                        continue;

                    double midDx = probe.MidX - target.MidX;
                    double midDy = probe.MidY - target.MidY;
                    double midDistance = Math.Sqrt(midDx * midDx + midDy * midDy);
                    double lengthDiff = Math.Abs(probe.Length - target.Length);

                    // So sanh endpoint khong phu thuoc thu tu Start/End.
                    double sameOrder =
                        GetDistance(probe.StartX, probe.StartY, target.StartX, target.StartY) +
                        GetDistance(probe.EndX, probe.EndY, target.EndX, target.EndY);
                    double reverseOrder =
                        GetDistance(probe.StartX, probe.StartY, target.EndX, target.EndY) +
                        GetDistance(probe.EndX, probe.EndY, target.StartX, target.StartY);
                    double endpointDistance = Math.Min(sameOrder, reverseOrder);

                    // Khong dung SortKey de match vi normal co the doi dau khi 0/180 do.
                    double score =
                        midDistance +
                        lengthDiff +
                        endpointDistance * 0.25 +
                        angleDiff * 0.00001;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = segment;
                    }
                }

                // Cho phep sai so hinh hoc nho sau rebuild, nhung van du chat de
                // khong nham sang bend line khac.
                return bestScore <= 0.008 ? best : null;
            }
            catch (COMException)
            {
                return null;
            }
        }

        private double GetUndirectedAngleDifference(double angleA, double angleB)
        {
            double diff = Math.Abs(angleA - angleB) % 180.0;
            if (diff > 90.0)
                diff = 180.0 - diff;
            return diff;
        }

        private class PointInfo
        {
            public SketchPoint Point;
            public double X;
            public double Y;
        }

        private Feature FindFeatureFromTree(ModelDoc2 model, params string[] names)
        {
            if (model == null)
                return null;

            try
            {
                TreeControlItem root = model.FeatureManager.GetFeatureTreeRootItem2(1);
                TreeControlItem hit = FindTreeItemByText(root, names);
                LogDiagnostic("BBOX drawing tree hit=" + (hit == null ? "none" : hit.Text)
                    + " objectType=" + (hit == null || hit.Object == null ? "null" : hit.Object.GetType().FullName));
                return hit?.Object as Feature;
            }
            catch (COMException)
            {
                return null;
            }
        }

        private void LogViewBoundingBoxes(SolidWorks.Interop.sldworks.View view)
        {
            if (view == null)
            {
                LogDiagnostic("VIEW_BBOX view=null");
                return;
            }

            // The view outline and a flat-pattern boundary-box sketch are view data,
            // not necessarily named Feature objects in either document's feature tree.
            try
            {
                double[] outline = view.GetOutline() as double[];
                if (outline != null && outline.Length >= 4)
                {
                    LogDiagnostic("VIEW_OUTLINE sheetM min=("
                        + outline[0].ToString("G17", CultureInfo.InvariantCulture) + ","
                        + outline[1].ToString("G17", CultureInfo.InvariantCulture) + ") max=("
                        + outline[2].ToString("G17", CultureInfo.InvariantCulture) + ","
                        + outline[3].ToString("G17", CultureInfo.InvariantCulture) + ")");
                }
                else
                {
                    LogDiagnostic("VIEW_OUTLINE unavailable");
                }
            }
            catch (Exception ex)
            {
                LogDiagnostic("VIEW_OUTLINE error: " + ex.Message);
            }

            try
            {
                DisplayData display = view.GetSMBoundaryBoxDisplayData2() as DisplayData;
                LogDiagnostic("VIEW_SM_BOUNDARY_BOX lines="
                    + (display == null ? "null" : display.GetLineCount().ToString(CultureInfo.InvariantCulture)));
            }
            catch (Exception ex)
            {
                LogDiagnostic("VIEW_SM_BOUNDARY_BOX error: " + ex.Message);
            }
        }

        private List<BendInfo> GetSelectableViewBoundingBoxLines(
            ModelDoc2 model, SolidWorks.Interop.sldworks.View view)
        {
            List<BendInfo> lines = new List<BendInfo>();
            if (model == null || view == null)
                return lines;

            try
            {
                DisplayData display = view.GetSMBoundaryBoxDisplayData2() as DisplayData;
                int count = display == null ? 0 : display.GetLineCount();
                LogDiagnostic("VIEW_BBOX_DIM displayLines=" + count);
                if (count < 4)
                    return lines;
                double[] outline = view.GetOutline() as double[];

                for (int i = 0; i < count; i++)
                {
                    // DisplayData line format: color, line type, two reserved values,
                    // followed by start XYZ and end XYZ in drawing-sheet coordinates.
                    double[] data = display.GetLineAtIndex2(i) as double[];
                    if (data == null || data.Length < 10)
                    {
                        LogDiagnostic("VIEW_BBOX_DIM line[" + i + "] data unavailable");
                        lines.Clear();
                        break;
                    }

                    BendInfo line = CreateDisplayBoundingBoxLine(data);
                    if (line == null)
                    {
                        LogDiagnostic("VIEW_BBOX_DIM line[" + i + "] degenerate");
                        lines.Clear();
                        break;
                    }
                    if (outline != null && outline.Length >= 4 &&
                        !IsLineInsideViewOutline(line, outline))
                    {
                        LogDiagnostic("VIEW_BBOX_DIM line[" + i + "] outside view outline; coordinate frame mismatch");
                        lines.Clear();
                        break;
                    }

                    LogDiagnostic("VIEW_BBOX_DIM display[" + i + "] " + Describe(line));
                    SketchSegment segment = SelectVisibleBoundingBoxSegment(model, line, data[6], data[9]);
                    if (segment == null)
                    {
                        LogDiagnostic("VIEW_BBOX_DIM line[" + i + "] not selectable as boundary-box sketch segment");
                        lines.Clear();
                        break;
                    }

                    line.Geometry = segment;
                    lines.Add(line);
                }
            }
            catch (Exception ex)
            {
                LogDiagnostic("VIEW_BBOX_DIM error: " + ex.Message);
                lines.Clear();
            }
            finally
            {
                model.ClearSelection2(true);
            }

            LogDiagnostic("VIEW_BBOX_DIM selectableLines=" + lines.Count);
            return lines;
        }

        private static BendInfo CreateDisplayBoundingBoxLine(double[] data)
        {
            double x1 = data[4];
            double y1 = data[5];
            double x2 = data[7];
            double y2 = data[8];
            if (double.IsNaN(x1) || double.IsNaN(y1) ||
                double.IsNaN(x2) || double.IsNaN(y2) ||
                double.IsInfinity(x1) || double.IsInfinity(y1) ||
                double.IsInfinity(x2) || double.IsInfinity(y2))
                return null;
            double dx = x2 - x1;
            double dy = y2 - y1;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.001)
                return null;

            double angle = Math.Atan2(dy, dx);
            if (angle < 0) angle += Math.PI;
            if (angle >= Math.PI) angle -= Math.PI;
            double midX = (x1 + x2) / 2.0;
            double midY = (y1 + y2) / 2.0;
            double normalX = -Math.Sin(angle);
            double normalY = Math.Cos(angle);
            return new BendInfo
            {
                IsBoundingBox = true,
                AngleGroup = Math.Round(angle * 180.0 / Math.PI, 1),
                SortKey = midX * normalX + midY * normalY,
                MidX = midX,
                MidY = midY,
                NormalX = normalX,
                NormalY = normalY,
                StartX = x1,
                StartY = y1,
                EndX = x2,
                EndY = y2,
                Length = length
            };
        }

        private static bool IsLineInsideViewOutline(BendInfo line, double[] outline)
        {
            const double tolerance = 0.001;
            return line.StartX >= outline[0] - tolerance &&
                line.StartX <= outline[2] + tolerance &&
                line.EndX >= outline[0] - tolerance &&
                line.EndX <= outline[2] + tolerance &&
                line.StartY >= outline[1] - tolerance &&
                line.StartY <= outline[3] + tolerance &&
                line.EndY >= outline[1] - tolerance &&
                line.EndY <= outline[3] + tolerance;
        }

        private SketchSegment SelectVisibleBoundingBoxSegment(
            ModelDoc2 model, BendInfo line, double startZ, double endZ)
        {
            SelectionMgr selection = model.SelectionManager as SelectionMgr;
            if (selection == null)
                return null;

            string[] types = { "EXTSKETCHSEGMENT", "SKETCHSEGMENT" };
            double[] fractions = { 0.5, 0.25, 0.75 };
            foreach (string type in types)
            {
                foreach (double fraction in fractions)
                {
                    model.ClearSelection2(true);
                    double x = line.StartX + (line.EndX - line.StartX) * fraction;
                    double y = line.StartY + (line.EndY - line.StartY) * fraction;
                    double z = startZ + (endZ - startZ) * fraction;
                    try
                    {
                        if (!model.Extension.SelectByID2("", type, x, y, z, false, 0, null, 0))
                            continue;

                        SketchSegment segment = selection.GetSelectedObject6(1, -1) as SketchSegment;
                        Sketch sketch = segment?.GetSketch() as Sketch;
                        if (sketch != null && sketch.IsBoundaryBoxSketch())
                        {
                            LogDiagnostic("VIEW_BBOX_DIM selected type=" + type + " fraction=" + fraction);
                            return segment;
                        }
                    }
                    catch (COMException ex)
                    {
                        LogDiagnostic("VIEW_BBOX_DIM selection error: " + ex.Message);
                    }
                }
            }

            return null;
        }

        private Feature ResolveBoundingBoxFeature(ModelDoc2 drawing, SolidWorks.Interop.sldworks.View view)
        {
            string[] names = { "境界ﾎﾞｯｸｽ", "境界ボックス", "Bounding-Box" };
            Feature drawingFeature = FindFeatureFromTree(drawing, names);
            LogDiagnostic("BBOX lookup drawing tree: " + (drawingFeature == null ? "none" : drawingFeature.Name));
            try
            {
                if (drawingFeature != null && drawingFeature.GetSpecificFeature2() is Sketch)
                    return drawingFeature;
            }
            catch (COMException ex)
            {
                LogDiagnostic("BBOX drawing feature is stale: " + ex.Message);
            }

            ModelDoc2 referencedModel = null;
            try { referencedModel = view.ReferencedDocument as ModelDoc2; }
            catch (COMException ex) { LogDiagnostic("BBOX referenced document error: " + ex.Message); }

            if (referencedModel == null)
            {
                LogDiagnostic("BBOX referenced document unavailable");
                return drawingFeature;
            }

            try
            {
                LogDiagnostic("BBOX referenced model: " + referencedModel.GetTitle());
                int visited = 0;
                Feature referencedFeature = FindNamedSketchFeature(
                    referencedModel.FirstFeature() as Feature, names, 0, ref visited);
                LogDiagnostic("BBOX lookup referenced model: "
                    + (referencedFeature == null ? "none" : referencedFeature.Name)
                    + " featuresVisited=" + visited);
                return referencedFeature ?? drawingFeature;
            }
            catch (COMException ex)
            {
                LogDiagnostic("BBOX referenced-model search error: " + ex.Message);
                return drawingFeature;
            }
        }

        private Feature FindNamedSketchFeature(
            Feature first, string[] names, int depth, ref int visited)
        {
            if (depth > 24)
                return null;

            for (Feature feature = first; feature != null && visited < 10000;
                feature = depth == 0 ? feature.GetNextFeature() as Feature : feature.GetNextSubFeature() as Feature)
            {
                visited++;
                string featureName = feature.Name ?? string.Empty;
                string featureType = feature.GetTypeName2() ?? string.Empty;
                if (visited <= 200 && (featureType.IndexOf("Sketch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    featureType.IndexOf("ProfileFeature", StringComparison.OrdinalIgnoreCase) >= 0))
                    LogDiagnostic("BBOX model sketch candidate name=" + featureName + " type=" + featureType);
                bool nameMatches = false;
                foreach (string name in names)
                {
                    if (featureName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        nameMatches = true;
                        break;
                    }
                }

                if (nameMatches)
                {
                    Sketch sketch = feature.GetSpecificFeature2() as Sketch;
                    LogDiagnostic("BBOX referenced candidate name=" + featureName
                        + " type=" + featureType + " isSketch=" + (sketch != null));
                    if (sketch != null)
                        return feature;
                }

                Feature child = feature.GetFirstSubFeature() as Feature;
                if (child == null)
                    continue;

                Feature match = FindNamedSketchFeature(child, names, depth + 1, ref visited);
                if (match != null)
                    return match;
            }

            return null;
        }

        private Feature ShowSketchFromTree(ModelDoc2 model, params string[] names)
        {
            TreeControlItem root = model.FeatureManager.GetFeatureTreeRootItem2(1);
            TreeControlItem hit = FindTreeItemByText(root, names);
            Feature feature = hit?.Object as Feature;
            if (feature == null)
                return null;

            model.ClearSelection2(true);
            if (feature.Select2(false, 0))
                model.UnblankSketch();

            return feature;
        }

        private TreeControlItem FindTreeItemByText(TreeControlItem node, string[] names)
        {
            if (node == null)
                return null;

            foreach (string name in names)
            {
                if (!string.IsNullOrEmpty(node.Text) &&
                    node.Text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return node;
            }

            TreeControlItem child = node.GetFirstChild();
            while (child != null)
            {
                TreeControlItem hit = FindTreeItemByText(child, names);
                if (hit != null)
                    return hit;

                child = child.GetNext();
            }

            return null;
        }

        private double[] TransformPoint(
            MathUtility mathUtil,
            MathTransform sketchTransform,
            MathTransform viewTransform,
            double x,
            double y,
            double z)
        {
            MathPoint point = mathUtil.CreatePoint(new[] { x, y, z }) as MathPoint;
            point = point?.MultiplyTransform(sketchTransform) as MathPoint;
            point = point?.MultiplyTransform(viewTransform) as MathPoint;
            return point?.ArrayData as double[];
        }

        private double[] TransformPoint(
            MathUtility mathUtil,
            MathTransform transform,
            double x,
            double y,
            double z)
        {
            MathPoint point = mathUtil.CreatePoint(new[] { x, y, z }) as MathPoint;
            point = point?.MultiplyTransform(transform) as MathPoint;
            return point?.ArrayData as double[];
        }

        private int CompareBends(BendInfo left, BendInfo right)
        {
            double leftAngle = GetAngleSortKey(left.AngleGroup);
            double rightAngle = GetAngleSortKey(right.AngleGroup);

            int angleCompare = leftAngle.CompareTo(rightAngle);
            if (angleCompare != 0)
                return angleCompare;

            // Sort theo projection tren normal cua left thay vi SortKey cua tung line.
            // Nhu vay endpoint dao chieu (0/180 do) khong lam thu tu bi lat.
            double leftProjection = left.MidX * left.NormalX + left.MidY * left.NormalY;
            double rightProjection = right.MidX * left.NormalX + right.MidY * left.NormalY;
            return leftProjection.CompareTo(rightProjection);
        }

        private double GetAngleSortKey(double angleDegrees)
        {
            double angle = angleDegrees % 180.0;
            if (angle < 0)
                angle += 180.0;

            // Dat seam tai 157.5 do de 0/180, 90 va 135 do deu nam trong
            // vung lien tuc - phu hop drawing sheet-metal H/V/45/135 thong dung.
            if (angle >= 157.5)
                angle -= 180.0;

            return Math.Round(angle, 1);
        }

    }
}
