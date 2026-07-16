using System;
using System.Collections.Generic;
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
            string currentStep = "[00] Khoi tao";
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

                SelectionMgr selMgr = model.SelectionManager as SelectionMgr;
                SolidWorks.Interop.sldworks.View view = GetSelectedDrawingView(selMgr);

                if (view == null)
                {
                    MessageBox.Show("Vui long chon 1 Drawing View truoc.", "dim kegaki", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                model.Extension.StartRecordingUndoObject();
                undoStarted = true;

                DeleteAllDimensionsInView(model, view);

                MathUtility mathUtil = swApp.IGetMathUtility();
                MathTransform viewTransform = view.ModelToViewTransform;
                if (mathUtil == null || viewTransform == null)
                    return;

                currentStep = "[01] Lay duong chan va BBox";
                List<BendInfo> bends = new List<BendInfo>();

                ShowSketchFromTree(model, "ﾍﾞﾝﾄﾞ-ﾗｲﾝ", "ベンド-ライン", "Bend-Line");
                AddBendLines(view.GetBendLines(), mathUtil, viewTransform, false, bends);

                List<BendInfo> outerEdges = GetOuterVisibleEdges(view, mathUtil, viewTransform);
                bends.AddRange(outerEdges);

                Feature boundingBoxFeature =
                    ShowSketchFromTree(model, "境界ﾎﾞｯｸｽ", "境界ボックス", "Bounding-Box");

                List<BendInfo> boundingBoxLines = new List<BendInfo>();
                if (boundingBoxFeature != null)
                {
                    Sketch boundingBoxSketch = boundingBoxFeature.GetSpecificFeature2() as Sketch;
                    if (boundingBoxSketch != null)
                        AddSketchSegments(boundingBoxSketch.GetSketchSegments(), mathUtil, viewTransform, true, boundingBoxLines);
                }

                SelectData selectData = selMgr.CreateSelectData() as SelectData;
                if (selectData == null)
                    return;

                selectData.View = view;
                model.ClearSelection2(true);

                DrawingDoc drawing = model as DrawingDoc;
                Sheet sheet = drawing?.GetCurrentSheet() as Sheet;
                if (drawing != null && sheet != null)
                    drawing.ActivateSheet(sheet.GetName());

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

                currentStep = "[02] Sap xep";
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

                currentStep = "[03] Tao DIM";
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
                    model.Extension.FinishRecordingUndoObject("dim kegaki");
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
            Array annotations = view.GetAnnotations() as Array;
            if (annotations == null)
                return;

            foreach (object item in annotations)
            {
                Annotation annotation = item as Annotation;
                if (annotation == null ||
                    annotation.GetType() != (int)swAnnotationType_e.swDisplayDimension)
                    continue;

                if (IsHoleRelatedDimension(annotation))
                    continue;

                drawingModel.ClearSelection2(true);
                if (!annotation.Select3(false, null))
                    continue;

                drawingModel.EditDelete();
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

            bends.Add(new BendInfo
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
            });
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
            double[] shifts = { 0, 0.018, -0.018, 0.036, -0.036, 0.054, -0.054 };
            int dimensionCount = 0;
            int groupStart = 0;

            for (int i = 0; i < bends.Count; i++)
            {
                bool isGroupEnd =
                    i == bends.Count - 1 ||
                    Math.Abs(bends[i + 1].AngleGroup - bends[i].AngleGroup) > 0.1;

                if (!isGroupEnd)
                    continue;

                bool hasRealBend = false;
                for (int k = groupStart; k <= i; k++)
                {
                    if (!bends[k].IsBoundingBox)
                    {
                        hasRealBend = true;
                        break;
                    }
                }

                if (hasRealBend)
                {
                    double normalX = bends[groupStart].NormalX;
                    double normalY = bends[groupStart].NormalY;
                    bool isHorizontalDimension =
                        Math.Abs(normalX) >
                        Math.Abs(normalY);
                    bool isDiagonal =
                        Math.Abs(normalX) > 0.15 &&
                        Math.Abs(normalY) > 0.15;

                    int verticalStagger = 0;

                    for (int k = groupStart; k < i; k++)
                    {
                        if (bends[k].IsBoundingBox &&
                            bends[k + 1].IsBoundingBox)
                            continue;

                        if (Math.Abs(bends[k + 1].SortKey - bends[k].SortKey) <= 0.001)
                            continue;

                        double distance = Math.Abs(bends[k + 1].SortKey - bends[k].SortKey);
                        double measureX = (bends[k].MidX + bends[k + 1].MidX) / 2.0;
                        double measureY = (bends[k].MidY + bends[k + 1].MidY) / 2.0;
                        double dimensionX;
                        double dimensionY;

                        if (isDiagonal)
                        {
                            double fromCenterX = measureX - centerX;
                            double fromCenterY = measureY - centerY;
                            double direction =
                                fromCenterX * normalX + fromCenterY * normalY >= 0
                                    ? 1.0
                                    : -1.0;

                            double offset = 0.02;
                            dimensionX = measureX + direction * normalX * offset;
                            dimensionY = measureY + direction * normalY * offset;
                        }
                        else if (isHorizontalDimension)
                        {
                            dimensionX = measureX;
                            dimensionY = minY - 0.01;
                        }
                        else
                        {
                            dimensionX = centerX + shifts[verticalStagger % shifts.Length];
                            dimensionY = measureY;
                            verticalStagger++;
                        }

                        if (!TryRegisterDimensionDistance(
                            createdDistanceKeys,
                            bends[groupStart].AngleGroup,
                            distance,
                            dimensionX,
                            dimensionY,
                            IsCenterAxisDimension(dimensionX, dimensionY, centerX, centerY)))
                            continue;

                        model.ClearSelection2(true);
                        SelectGeometry(view, bends[k], false, selectData);
                        SelectGeometry(view, bends[k + 1], true, selectData);

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

                if (Math.Abs(edge.AngleGroup - candidate.AngleGroup) <= 0.1 &&
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
                    if (left == null || edge.SortKey < left.SortKey)
                        left = edge;
                    if (right == null || edge.SortKey > right.SortKey)
                        right = edge;
                }
                else
                {
                    if (bottom == null || edge.SortKey < bottom.SortKey)
                        bottom = edge;
                    if (top == null || edge.SortKey > top.SortKey)
                        top = edge;
                }
            }

            int count = 0;
            if (left != null && right != null)
            {
                model.ClearSelection2(true);
                SelectGeometry(view, left, false, selectData);
                SelectGeometry(view, right, true, selectData);
                if (AddLinearDimensionOnly(model, (minX + maxX) / 2.0, minY - 0.025))
                    count++;
            }

            if (bottom != null && top != null)
            {
                model.ClearSelection2(true);
                SelectGeometry(view, bottom, false, selectData);
                SelectGeometry(view, top, true, selectData);
                if (AddLinearDimensionOnly(model, maxX + 0.025, (minY + maxY) / 2.0))
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
                    Math.Abs(candidate.AngleGroup - bend.AngleGroup) > ParallelAngleTolerance)
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
                    Math.Abs(candidate.AngleGroup - bend.AngleGroup) > ParallelAngleTolerance)
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
                        Math.Round(bend.AngleGroup, 1).ToString("0.0") +
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

                    if (Math.Abs(outerEdge.AngleGroup - bend.AngleGroup) > ParallelAngleTolerance)
                        continue;

                    int created = CreateEdgeToBendLineDimension(
                        model,
                        view,
                        selectData,
                        outerEdge,
                        bend,
                        centerX,
                        centerY,
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
                    Math.Abs(item.AngleGroup - bend.AngleGroup) <= ParallelAngleTolerance)
                    group.Add(item);
            }

            if (group.Count < 2)
                return false;

            group.Sort(CompareBends);

            double targetDistance = Math.Abs(edge.SortKey - bend.SortKey);
            for (int i = 0; i < group.Count - 1; i++)
            {
                double chainDistance = Math.Abs(group[i + 1].SortKey - group[i].SortKey);
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
            double centerX,
            double centerY,
            List<string> createdDistanceKeys)
        {
            double measureX = (edge.MidX + bend.MidX) / 2.0;
            double measureY = (edge.MidY + bend.MidY) / 2.0;
            bool createsHorizontalDimension =
                Math.Abs(edge.NormalX) > Math.Abs(edge.NormalY);

            double dimensionX = createsHorizontalDimension ? measureX : centerX;
            double dimensionY = createsHorizontalDimension ? centerY : measureY;
            double distance = Math.Abs(edge.SortKey - bend.SortKey);

            if (!TryRegisterDimensionDistance(
                createdDistanceKeys,
                bend.AngleGroup,
                distance,
                dimensionX,
                dimensionY,
                IsCenterAxisDimension(dimensionX, dimensionY, centerX, centerY)))
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
                    Math.Abs(bend.AngleGroup - angle) <= ParallelAngleTolerance)
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
                    Math.Abs(line.AngleGroup - bend.AngleGroup) <= ParallelAngleTolerance)
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
                    Math.Abs(line.AngleGroup - bend.AngleGroup) > ParallelAngleTolerance)
                    continue;

                double distance = Math.Abs(line.SortKey - bend.SortKey);
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
                    Math.Abs(edge.AngleGroup - bend.AngleGroup) <= ParallelAngleTolerance)
                {
                    double distance = Math.Abs(edge.SortKey - bend.SortKey);
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

                double angleDiff = Math.Abs(line.AngleGroup - bend.AngleGroup);
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
                if (Math.Abs(processed.AngleGroup - edge.AngleGroup) <= 0.1 &&
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
            if (bend.IsEdge)
                return view.SelectEntity(bend.Geometry, append);

            SketchSegment segment = bend.Geometry as SketchSegment;
            return segment != null && segment.Select4(append, selectData);
        }

        private class PointInfo
        {
            public SketchPoint Point;
            public double X;
            public double Y;
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
            int angleCompare = left.AngleGroup.CompareTo(right.AngleGroup);
            if (angleCompare != 0)
                return angleCompare;

            return left.SortKey.CompareTo(right.SortKey);
        }

    }
}
