using System;
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

        private readonly ISldWorks swApp;
        private const double ParallelAngleTolerance = 10.0;

        public TaoDimKegaki(ISldWorks app)
        {
            swApp = app;
        }

        public void GenerateKegakiDimensions()
        {
            string currentStep = "[00] ActiveDoc";
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

                currentStep = "[02] Bat dau Undo";
                model.Extension.StartRecordingUndoObject();
                undoStarted = true;

                currentStep = "[03] Xoa DIM cu";
                DeleteAllDimensionsInView(model, view);

                // Xoa annotation co the lam View refresh. Lay lai View theo ten de tranh
                // tiep tuc dung COM proxy cu.
                DrawingDoc drawing = model as DrawingDoc;
                view = ReacquireViewByName(drawing, selectedViewName) ?? view;

                currentStep = "[04] Hien Bend-Line va BBox";
                // ShowSketchFromTree co ClearSelection/UnblankSketch va co the lam drawing
                // refresh. Hien tat ca sketch truoc, sau do moi lay geometry.
                ShowSketchFromTree(model, "ﾍﾞﾝﾄﾞ-ﾗｲﾝ", "ベンド-ライン", "Bend-Line");
                Feature boundingBoxFeature =
                    ShowSketchFromTree(model, "境界ﾎﾞｯｸｽ", "境界ボックス", "Bounding-Box");

                // Dua Drawing ve trang thai rebuild on dinh TRUOC KHI lay Bend-Line.
                // Muc dich la de DIM bam vao entity cua lan rebuild hien tai, thay vi bam vao
                // SketchSegment tam vua bi SolidWorks thay the. Khong thay doi hinh hoc/logic DIM.
                currentStep = "[05] Rebuild View truoc khi lay reference";
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
                boundingBoxFeature = FindFeatureFromTree(
                    model,
                    "境界ﾎﾞｯｸｽ", "境界ボックス", "Bounding-Box");

                currentStep = "[06] Lay Transform";
                MathUtility mathUtil = swApp.IGetMathUtility();
                MathTransform viewTransform = view.ModelToViewTransform;
                if (mathUtil == null || viewTransform == null)
                    return;

                currentStep = "[07] Lay Bend-Line va BBox";
                List<BendInfo> bends = new List<BendInfo>();
                AddBendLines(view.GetBendLines(), mathUtil, viewTransform, false, bends);

                List<BendInfo> outerEdges = GetOuterVisibleEdges(view, mathUtil, viewTransform);
                bends.AddRange(outerEdges);

                List<BendInfo> boundingBoxLines = new List<BendInfo>();
                if (boundingBoxFeature != null)
                {
                    Sketch boundingBoxSketch = boundingBoxFeature.GetSpecificFeature2() as Sketch;
                    if (boundingBoxSketch != null)
                        AddSketchSegments(boundingBoxSketch.GetSketchSegments(), mathUtil, viewTransform, true, boundingBoxLines);
                }

                currentStep = "[08] Tao SelectData";
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
                    int overallCount = CreateOverallDimensions(
                        model,
                        overallLines,
                        view,
                        selectData,
                        overallMinX,
                        overallMaxX,
                        overallMinY,
                        overallMaxY);

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
                bends.Sort(CompareBends);
                List<BendInfo> chainLines = new List<BendInfo>();
                foreach (BendInfo bend in bends)
                {
                    if (!bend.IsEdge && !bend.IsBoundingBox)
                        chainLines.Add(bend);
                }

                chainLines.Sort(CompareBends);

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

                dimensionCount += CreateOverallDimensions(
                    model,
                    boundingBoxLines.Count > 0 ? boundingBoxLines : outerEdges,
                    view,
                    selectData,
                    minX,
                    maxX,
                    minY,
                    maxY);

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
                MessageBox.Show(
                    "Hoan tat! Da tao " + dimensionCount + " kich thuoc chuan Form.",
                    "dim kegaki",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (COMException ex)
            {
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
                MessageBox.Show(
                    "Loi tai buoc: " + currentStep + System.Environment.NewLine + ex.Message,
                    "dim kegaki",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
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
                return false;

            int dimensionType = displayDimension.GetType();
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
                return;

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
                        if (distance <= 0.001)
                            continue;

                        // Tat ca DIM trong group nam tren cung 1 chain line.
                        double chainN = (n1 + n2) / 2.0;
                        double dimensionX = tangentX * chainT + normalX * chainN;
                        double dimensionY = tangentY * chainT + normalY * chainN;

                        // Dang ky theo CAP HINH HOC thay vi theo gia tri distance.
                        // Nhu vay 2 doan co cung gia tri (vd 27.4, 27.4) van duoc tao day du.
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
                            dimensionCount++;
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
                return null;

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

                if (GetUndirectedAngleDifference(edge.AngleGroup, candidate.AngleGroup) <= 0.1 &&
                    Math.Abs(edge.SortKey - candidate.SortKey) <= 0.000001)
                    return;
            }

            edges.Add(candidate);
        }

        private int CreateOverallDimensions(
            ModelDoc2 model,
            List<BendInfo> outerEdges,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            BendInfo left = null;
            BendInfo right = null;
            BendInfo bottom = null;
            BendInfo top = null;

            foreach (BendInfo edge in outerEdges)
            {
                bool createsHorizontalDimension =
                    Math.Abs(edge.NormalX) > Math.Abs(edge.NormalY);

                if (createsHorizontalDimension)
                {
                    if (left == null || edge.MidX < left.MidX)
                        left = edge;
                    if (right == null || edge.MidX > right.MidX)
                        right = edge;
                }
                else
                {
                    if (bottom == null || edge.MidY < bottom.MidY)
                        bottom = edge;
                    if (top == null || edge.MidY > top.MidY)
                        top = edge;
                }
            }

            int count = 0;
            if (left != null && right != null)
            {
                model.ClearSelection2(true);
                if (SelectGeometry(view, left, false, selectData) &&
                    SelectGeometry(view, right, true, selectData) &&
                    AddLinearDimensionOnly(model, (minX + maxX) / 2.0, minY - 0.025))
                    count++;
            }

            if (bottom != null && top != null)
            {
                model.ClearSelection2(true);
                if (SelectGeometry(view, bottom, false, selectData) &&
                    SelectGeometry(view, top, true, selectData) &&
                    AddLinearDimensionOnly(model, maxX + 0.025, (minY + maxY) / 2.0))
                    count++;
            }

            return count;
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
            List<string> processedSides = new List<string>();

            foreach (BendInfo bend in bends)
            {
                if (bend.IsBoundingBox ||
                    bend.IsEdge)
                    continue;

                for (int side = -1; side <= 1; side += 2)
                {
                    double direction = side;
                    if (HasOuterParallelRealBend(bend, bends, centerX, centerY, direction))
                        continue;

                    string sideKey =
                        Math.Round(GetAngleSortKey(bend.AngleGroup), 1).ToString("0.0") +
                        ":" +
                        (direction > 0 ? "P" : "N");
                    if (processedSides.Contains(sideKey))
                        continue;

                    BendInfo outerEdge = FindOutermostLineByBendDirection(
                        bend,
                        outerEdges,
                        centerX,
                        centerY,
                        direction);

                    if (outerEdge == null)
                        continue;

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

                    double chainT = GetChainTangentCoordinate(
                        bends,
                        bend.AngleGroup,
                        tangentX,
                        tangentY);

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

                    if (created > 0)
                    {
                        processedSides.Add(sideKey);
                        count += created;
                    }
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
                return 0;

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

            return AddLinearDimensionOnly(model, dimensionX, dimensionY) ? 1 : 0;
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
            double alongLimit = Math.Max(bend.Length * 2.5, 0.03);

            foreach (BendInfo line in lines)
            {
                // Chi xet canh song song voi bend line ngay tu dau.
                // Neu loc sau khi da chon outermost, canh ngang/cheo co the
                // chiem vi tri ung vien va lam bo qua bend line doc.
                if (GetUndirectedAngleDifference(line.AngleGroup, bend.AngleGroup) > ParallelAngleTolerance)
                    continue;

                double alongDistance = GetClosestAlongDistance(line, bend, tangentX, tangentY);
                if (alongDistance > alongLimit)
                    continue;

                double lineSide =
                    (line.MidX - centerX) * bend.NormalX +
                    (line.MidY - centerY) * bend.NormalY;
                double score = (lineSide - bendSide) * direction;

                if (score <= 0.001 || score <= bestScore)
                    continue;

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
            DisplayDimension displayDimension = model.AddDimension2(x, y, 0) as DisplayDimension;
            if (displayDimension == null)
                return false;

            int dimensionType = displayDimension.GetType();
            if (dimensionType != (int)swDimensionType_e.swAngularDimension)
                return true;

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
                return false;

            createdKeys.Add(key);
            return true;
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
                return false;

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
                    return view.SelectEntity(bend.Geometry, append);

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

                return segment != null && segment.Select4(append, selectData);
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
                    return refreshed.Select4(append, selectData);
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
                return null;

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
                return hit?.Object as Feature;
            }
            catch (COMException)
            {
                return null;
            }
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
