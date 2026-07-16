using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public class LenhDimCanhSongSong
    {
        private readonly ISldWorks swApp;

        private const double OrthoTolMm = 0.5;
        private const double DimOffsetMm = 10.0;
        private const double MinGapMm = 1.0;
        private const double MaxGapMm = 300.0;
        private const double MinOverlapMm = 2.0;

        private const double ParallelAngleTolDeg = 3.0;
        private const double AngleMateTolMm = 4.0;

        private double viewScale = 1.0;

        public LenhDimCanhSongSong(ISldWorks app)
        {
            swApp = app;
        }

        public void Run()
        {
            Debug.WriteLine("[DIM MAT CAT] build=20260716-keep-outer-sharp-v8");
            ModelDoc2 model = swApp?.ActiveDoc as ModelDoc2;
            if (model == null)
            {
                Msg("Khong co file nao dang mo.", swMessageBoxIcon_e.swMbWarning);
                return;
            }

            if (model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                Msg("Lenh nay chi dung trong Drawing.", swMessageBoxIcon_e.swMbWarning);
                return;
            }

            EnableNativeVirtualSharpDisplay(model);

            SelectionMgr selMgr = model.SelectionManager as SelectionMgr;
            int selCount = selMgr?.GetSelectedObjectCount2(-1) ?? 0;
            if (selMgr == null || selCount < 1)
            {
                EnableEdgeSelectionFilter();
                Msg("Hay chon 1 canh trong Drawing View, roi bam lai nut dim mat cat.", swMessageBoxIcon_e.swMbInformation);
                return;
            }

            DisableEdgeSelectionFilter();

            SolidWorks.Interop.sldworks.View view = GetSelectedDrawingView(selMgr, selCount);
            if (view == null)
            {
                Msg("Khong lay duoc Drawing View cua canh dang chon.", swMessageBoxIcon_e.swMbWarning);
                return;
            }

            // Luu canh nguoi dung chon truoc khi ActivateView vi SolidWorks co the lam mat selection.
            Edge selectedInputEdge = GetSelectedEdgeFromSelection(selMgr, selCount);

            viewScale = view.ScaleDecimal;
            if (viewScale <= 0)
                viewScale = 1.0;

            DrawingDoc draw = model as DrawingDoc;
            try
            {
                draw?.ActivateView(view.Name);
            }
            catch
            {
            }

            MathUtility mathUtil = swApp.GetMathUtility() as MathUtility;
            MathTransform viewTransform = view.ModelToViewTransform as MathTransform;
            SelectData selectData = selMgr.CreateSelectData() as SelectData;
            if (mathUtil == null || viewTransform == null || selectData == null)
            {
                Msg("Khong lay duoc du lieu Drawing View.", swMessageBoxIcon_e.swMbStop);
                return;
            }

            selectData.View = view;

            List<EdgeInfo> edges = CollectVisibleLineEdges(view, mathUtil, viewTransform);
            List<ArcInfo> arcs = CollectVisibleArcEdges(view, mathUtil, viewTransform);
            double materialThicknessMm = EstimateMaterialThicknessMm(edges);
            List<ArcInfo> usableArcs = FilterUsableFilletArcs(arcs, materialThicknessMm);
            ArcInfo selectedArc = GetSelectedArcInfo(selectedInputEdge, arcs, mathUtil, viewTransform);

            Debug.WriteLine("[DIM MAT CAT] view=" + SafeViewName(view)
                + ", visibleLineEdges=" + edges.Count
                + ", visibleArcs=" + arcs.Count
                + ", thicknessMm=" + materialThicknessMm.ToString("0.###")
                + ", usableArcs=" + usableArcs.Count);

            List<ArcInfo> curvedProfileArcs = GetCurvedProfileReferenceArcs(
                arcs,
                edges,
                materialThicknessMm,
                selectedArc);
            if (curvedProfileArcs.Count > 0)
            {
                ArcInfo selectedArcGeometry = selectedArc;
                DeletePreviousDimensionArtifacts(model, view);
                edges = CollectVisibleLineEdges(view, mathUtil, viewTransform);
                arcs = CollectVisibleArcEdges(view, mathUtil, viewTransform);
                materialThicknessMm = EstimateMaterialThicknessMm(edges);
                selectedArc = FindMatchingArcGeometry(arcs, selectedArcGeometry);

                int curvedCount = AddCurvedProfileDimensions(
                    model,
                    view,
                    selectData,
                    edges,
                    arcs,
                    materialThicknessMm,
                    selectedArc);

                model.ClearSelection2(true);
                model.EditRebuild3();
                Msg(curvedCount > 0
                    ? "Da tao " + curvedCount + " dim mat cat co cung."
                    : "Da nhan dang mat cat co cung nhung chua tao duoc kich thuoc.",
                    curvedCount > 0 ? swMessageBoxIcon_e.swMbInformation : swMessageBoxIcon_e.swMbWarning);
                return;
            }

            if (edges.Count == 0)
            {
                Msg("Khong lay duoc canh thang trong view.", swMessageBoxIcon_e.swMbStop);
                return;
            }

            EdgeInfo selectedInfo = GetSelectedEdgeInfo(selMgr, edges, mathUtil, viewTransform);
            Debug.WriteLine("[DIM MAT CAT] selectedInfo direct=" + (selectedInfo != null));

            double nearestDistanceSheetMm;
            EdgeInfo nearestInfo = FindNearestEdgeFromSelectionPoint(selMgr, edges, out nearestDistanceSheetMm);
            bool nearestIsReliable = nearestInfo != null && nearestDistanceSheetMm <= GetReliableSelectionDistanceMm();
            // A real selected edge is authoritative.  The nearest-click
            // fallback can land on the parallel edge across the material
            // thickness and must never switch an explicitly selected outer
            // contour to the inner contour (or vice versa).
            if (selectedInfo == null && nearestIsReliable)
            {
                selectedInfo = nearestInfo;
                Debug.WriteLine("[DIM MAT CAT] selectedInfo from reliable nearest=True, distanceSheetMm="
                    + nearestDistanceSheetMm.ToString("0.###"));
            }
            else if (selectedInfo == null)
            {
                selectedInfo = nearestInfo;
            }

            Debug.WriteLine("[DIM MAT CAT] selectedInfo nearest=" + (nearestInfo != null)
                + ", selected=" + EdgeSummary(selectedInfo));

            if (selectedInfo == null && IsDrawingViewSelection(selMgr))
            {
                DeletePreviousDimensionArtifacts(model, view);
                edges = CollectVisibleLineEdges(view, mathUtil, viewTransform);
                arcs = CollectVisibleArcEdges(view, mathUtil, viewTransform);
                int count = AddSectionViewDimensions(model, view, selectData, edges, arcs);
                model.ClearSelection2(true);
                model.EditRebuild3();
                Msg(count > 0
                    ? "Da tao " + count + " dim theo contour phu bi lien tuc."
                    : "Khong tim duoc contour phu bi lien tuc trong Drawing View.",
                    count > 0 ? swMessageBoxIcon_e.swMbInformation : swMessageBoxIcon_e.swMbWarning);
                return;
            }
            else if (selectedInfo == null)
            {
                Msg("Khong xac dinh duoc canh dang chon. Hay zoom lon va chon sat canh can dim.", swMessageBoxIcon_e.swMbWarning);
                return;
            }

            // Cleanup rebuilds the drawing and may invalidate the Edge COM
            // references collected above.  Preserve only the geometry key,
            // then collect fresh entities before creating new dimensions.
            EdgeInfo selectedGeometry = selectedInfo;
            DeletePreviousDimensionArtifacts(model, view);
            edges = CollectVisibleLineEdges(view, mathUtil, viewTransform);
            arcs = CollectVisibleArcEdges(view, mathUtil, viewTransform);
            selectedInfo = FindMatchingEdgeGeometry(edges, selectedGeometry);
            if (selectedInfo == null)
            {
                Msg("Khong tim lai duoc canh da chon sau khi xoa dim cu.", swMessageBoxIcon_e.swMbWarning);
                return;
            }

            Debug.WriteLine("[DIM MAT CAT] refreshed geometry after cleanup. edges="
                + edges.Count + ", arcs=" + arcs.Count
                + ", selected=" + EdgeSummary(selectedInfo));

            // Always build the contour first.  Whether a segment looks
            // horizontal/vertical relative to the drawing sheet must not
            // choose the dimension engine.  Each connected joint decides
            // locally whether it is a 90-degree edge case or needs a virtual
            // sharp.
            int angledProfileCount = AddAngledOuterProfileDimensions(
                model,
                view,
                selectData,
                edges,
                arcs,
                selectedInfo);
            if (angledProfileCount > 0)
            {
                model.ClearSelection2(true);
                model.EditRebuild3();
                Msg("Da tao " + angledProfileCount
                    + " dim mat cat theo contour ngoai (co canh nghieng).",
                    swMessageBoxIcon_e.swMbInformation);
                return;
            }

            int seededCount = AddSeededSectionDimensions(model, selectData, edges, selectedInfo);

            // The selected-edge workflow previously stopped after creating the
            // horizontal/vertical profile dimensions.  Although the angular
            // dimension helpers existed, they were only called when the whole
            // Drawing View was selected.  Add the angle explicitly when the
            // seed is inclined so angled section profiles work in both modes.
            int angularCount = IsMeaningfullyAngled(selectedInfo)
                ? AddSelectedAngleDimension(model, selectData, edges, selectedInfo)
                : 0;

            seededCount += angularCount;
            Debug.WriteLine("[DIM MAT CAT] selected angled edge=" + IsMeaningfullyAngled(selectedInfo)
                + ", selected angular dimensions=" + angularCount
                + ", selected total dimensions=" + seededCount);

            if (seededCount > 0)
            {
                model.ClearSelection2(true);
                model.EditRebuild3();
                Msg("Da tao " + seededCount + " dim mat cat theo canh chon"
                    + (angularCount > 0 ? " (co dim goc)." : "."),
                    swMessageBoxIcon_e.swMbInformation);
                return;
            }

            EdgeInfo pair = FindBestParallelPair(edges, selectedInfo);
            Debug.WriteLine("[DIM MAT CAT] pair=" + (pair != null));
            if (pair == null)
            {
                Msg("Khong tim duoc canh song song phu hop.", swMessageBoxIcon_e.swMbWarning);
                return;
            }

            bool ok = AddParallelDimension(model, view, selectData, selectedInfo, pair);
            model.ClearSelection2(true);
            model.EditRebuild3();

            Msg(ok
                ? "Da dim giua canh chon va canh song song gan nhat."
                : "Tim duoc canh song song nhung tao dim that bai.",
                ok ? swMessageBoxIcon_e.swMbInformation : swMessageBoxIcon_e.swMbStop);
        }

        private SolidWorks.Interop.sldworks.View GetSelectedDrawingView(SelectionMgr selMgr, int selCount)
        {
            for (int i = 1; i <= selCount; i++)
            {
                try
                {
                    SolidWorks.Interop.sldworks.View view = selMgr.GetSelectedObject6(i, -1) as SolidWorks.Interop.sldworks.View;
                    if (view != null)
                        return view;
                }
                catch
                {
                }

                try
                {
                    SolidWorks.Interop.sldworks.View view = selMgr.GetSelectedObjectsDrawingView2(i, -1) as SolidWorks.Interop.sldworks.View;
                    if (view != null)
                        return view;
                }
                catch
                {
                }
            }

            return null;
        }

        private EdgeInfo GetSelectedEdgeInfo(SelectionMgr selMgr, List<EdgeInfo> edges, MathUtility mathUtil, MathTransform viewTransform)
        {
            object selectedObj = null;
            int selectedType = -1;
            try
            {
                selectedObj = selMgr.GetSelectedObject6(1, -1);
                selectedType = selMgr.GetSelectedObjectType3(1, -1);
            }
            catch
            {
            }

            Debug.WriteLine("[DIM MAT CAT] selectedType=" + selectedType
                + ", selectedObject=" + (selectedObj == null ? "null" : selectedObj.GetType().FullName));

            Edge selectedEdge = selectedObj as Edge;
            if (selectedEdge == null)
                return null;

            EdgeInfo selectedGeometry = MakeEdgeInfo(selectedEdge, mathUtil, viewTransform);
            if (selectedGeometry == null)
                return null;

            foreach (EdgeInfo info in edges)
            {
                if (IsSameEdgeGeometry(info, selectedGeometry))
                    return info;
            }

            return null;
        }

        private ArcInfo GetSelectedArcInfo(
            Edge selectedEdge,
            List<ArcInfo> arcs,
            MathUtility mathUtil,
            MathTransform viewTransform)
        {
            if (selectedEdge == null || arcs == null)
                return null;

            ArcInfo selectedGeometry = MakeArcInfo(selectedEdge, mathUtil, viewTransform);
            if (selectedGeometry == null)
                return null;

            ArcInfo match = FindMatchingArcGeometry(arcs, selectedGeometry);
            Debug.WriteLine("[DIM MAT CAT CUNG] selected arc="
                + (match == null ? "none" : "R" + match.RadiusMm.ToString("0.###")));
            return match;
        }

        private Edge GetSelectedEdgeFromSelection(SelectionMgr selMgr, int selCount)
        {
            if (selMgr == null || selCount < 1)
                return null;

            for (int i = 1; i <= selCount; i++)
            {
                try
                {
                    object selectedObject = selMgr.GetSelectedObject6(i, -1);
                    int selectedType = selMgr.GetSelectedObjectType3(i, -1);
                    Debug.WriteLine("[DIM MAT CAT CUNG] input selection index=" + i
                        + ", type=" + selectedType
                        + ", object=" + (selectedObject == null ? "null" : selectedObject.GetType().FullName));

                    Edge selectedEdge = selectedObject as Edge;
                    if (selectedEdge != null)
                        return selectedEdge;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[DIM MAT CAT CUNG] read input selection failed: " + ex.Message);
                }
            }

            return null;
        }

        private ArcInfo FindMatchingArcGeometry(List<ArcInfo> arcs, ArcInfo geometry)
        {
            if (arcs == null || geometry == null)
                return null;

            ArcInfo best = null;
            double bestScore = double.MaxValue;
            double positionTol = MmToViewM(0.08);
            foreach (ArcInfo arc in arcs)
            {
                if (arc == null)
                    continue;

                double centerDistance = Distance2D(
                    arc.CenterX,
                    arc.CenterY,
                    geometry.CenterX,
                    geometry.CenterY);
                if (centerDistance > positionTol)
                    continue;

                double radiusError = Math.Abs(arc.RadiusMm - geometry.RadiusMm);
                if (radiusError > 0.08)
                    continue;

                double directEndpointError = Distance2D(
                    arc.StartX, arc.StartY,
                    geometry.StartX, geometry.StartY)
                    + Distance2D(
                        arc.EndX, arc.EndY,
                        geometry.EndX, geometry.EndY);
                double reverseEndpointError = Distance2D(
                    arc.StartX, arc.StartY,
                    geometry.EndX, geometry.EndY)
                    + Distance2D(
                        arc.EndX, arc.EndY,
                        geometry.StartX, geometry.StartY);
                double endpointError = Math.Min(directEndpointError, reverseEndpointError);
                if (endpointError > positionTol * 2.0)
                    continue;

                double arcLengthError = Math.Abs(arc.ArcLengthMm - geometry.ArcLengthMm);
                if (arcLengthError > 0.1)
                    continue;

                double score = centerDistance
                    + endpointError
                    + MmToViewM(radiusError + arcLengthError);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = arc;
                }
            }

            return best;
        }

        private EdgeInfo FindMatchingEdgeGeometry(
            List<EdgeInfo> edges,
            EdgeInfo geometry)
        {
            if (edges == null || geometry == null)
                return null;

            foreach (EdgeInfo edge in edges)
            {
                if (IsSameEdgeGeometry(edge, geometry))
                    return edge;
            }

            // A rebuild can move an endpoint by a very small drawing-space
            // tolerance.  Use midpoint/direction/length only as a controlled
            // fallback, never the old COM reference.
            EdgeInfo best = null;
            double bestScore = double.MaxValue;
            double positionTol = MmToViewM(0.05);
            foreach (EdgeInfo edge in edges)
            {
                double parallelDot = Math.Abs(
                    edge.DirX * geometry.DirX + edge.DirY * geometry.DirY);
                if (parallelDot < Math.Cos(0.5 * Math.PI / 180.0))
                    continue;

                double midpointDistance = Distance2D(
                    edge.MidX, edge.MidY,
                    geometry.MidX, geometry.MidY);
                if (midpointDistance > positionTol)
                    continue;

                double lengthError = Math.Abs(edge.LengthMm - geometry.LengthMm);
                if (lengthError > 0.05)
                    continue;

                double score = midpointDistance + MmToViewM(lengthError);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = edge;
                }
            }

            return best;
        }

        private List<EdgeInfo> CollectVisibleLineEdges(SolidWorks.Interop.sldworks.View view, MathUtility mathUtil, MathTransform viewTransform)
        {
            List<EdgeInfo> result = new List<EdgeInfo>();
            Array components = view.GetVisibleComponents() as Array;
            if (components == null)
                return result;

            foreach (object item in components)
            {
                Component2 component = item as Component2;
                if (component == null)
                    continue;

                Array visibleEdges = view.GetVisibleEntities2(component, (int)swViewEntityType_e.swViewEntityType_Edge) as Array;
                if (visibleEdges == null)
                    continue;

                foreach (object edgeItem in visibleEdges)
                {
                    Edge edge = edgeItem as Edge;
                    Curve curve = edge?.GetCurve() as Curve;
                    if (curve == null || !curve.IsLine())
                        continue;

                    EdgeInfo info = MakeEdgeInfo(edge, mathUtil, viewTransform);
                    if (info != null)
                        AddUniqueEdge(result, info);
                }
            }

            return result;
        }

        private List<ArcInfo> CollectVisibleArcEdges(SolidWorks.Interop.sldworks.View view, MathUtility mathUtil, MathTransform viewTransform)
        {
            List<ArcInfo> result = new List<ArcInfo>();
            Array components = view.GetVisibleComponents() as Array;
            if (components == null)
                return result;

            foreach (object item in components)
            {
                Component2 component = item as Component2;
                if (component == null)
                    continue;

                Array visibleEdges = view.GetVisibleEntities2(component, (int)swViewEntityType_e.swViewEntityType_Edge) as Array;
                if (visibleEdges == null)
                    continue;

                foreach (object edgeItem in visibleEdges)
                {
                    Edge edge = edgeItem as Edge;
                    Curve curve = edge?.GetCurve() as Curve;
                    if (!IsCircularCurve(curve))
                        continue;

                    ArcInfo info = MakeArcInfo(edge, mathUtil, viewTransform);
                    if (info != null)
                        AddUniqueArc(result, info);
                }
            }

            return result;
        }

        private ArcInfo MakeArcInfo(Edge edge, MathUtility mathUtil, MathTransform viewTransform)
        {
            Curve curve = edge?.GetCurve() as Curve;
            if (!IsCircularCurve(curve))
                return null;

            double[] p1Model = null;
            double[] p2Model = null;
            double uMin = 0.0;
            double uMax = 0.0;
            bool hasCurveRange = false;
            try
            {
                CurveParamData edgeParams = edge.GetCurveParams3();
                if (edgeParams != null)
                {
                    p1Model = edgeParams.StartPoint as double[];
                    p2Model = edgeParams.EndPoint as double[];
                    uMin = edgeParams.UMinValue;
                    uMax = edgeParams.UMaxValue;
                    hasCurveRange = true;
                }
            }
            catch
            {
            }

            if (!IsModelPoint(p1Model) || !IsModelPoint(p2Model))
            {
                Vertex startVertex = edge.GetStartVertex() as Vertex;
                Vertex endVertex = edge.GetEndVertex() as Vertex;
                p1Model = startVertex?.GetPoint() as double[];
                p2Model = endVertex?.GetPoint() as double[];
            }

            double[] centerModel;
            double radiusModel;
            if (!TryGetCircleData(curve, out centerModel, out radiusModel))
                return null;

            double[] center = TransformPoint(mathUtil, viewTransform, centerModel);
            double[] p1 = TransformPoint(mathUtil, viewTransform, p1Model);
            double[] p2 = TransformPoint(mathUtil, viewTransform, p2Model);
            if (center == null || p1 == null || p2 == null)
                return null;

            double radiusView = Math.Sqrt((p1[0] - center[0]) * (p1[0] - center[0]) + (p1[1] - center[1]) * (p1[1] - center[1]));
            if (radiusView <= 0 && radiusModel > 0)
                radiusView = radiusModel * viewScale;

            double arcLengthModel = 0.0;
            double[] mid = null;
            if (hasCurveRange)
            {
                try
                {
                    arcLengthModel = curve.GetLength3(uMin, uMax);
                }
                catch
                {
                    try
                    {
                        arcLengthModel = curve.GetLength2(uMin, uMax);
                    }
                    catch
                    {
                    }
                }

                try
                {
                    double[] midModel = edge.Evaluate2((uMin + uMax) / 2.0, 0) as double[];
                    mid = TransformPoint(mathUtil, viewTransform, midModel);
                }
                catch
                {
                }
            }

            if (arcLengthModel <= 0.0)
            {
                double chordView = Math.Sqrt(
                    (p2[0] - p1[0]) * (p2[0] - p1[0])
                    + (p2[1] - p1[1]) * (p2[1] - p1[1]));
                double ratio = radiusView > 0.0
                    ? Math.Max(-1.0, Math.Min(1.0, chordView / (2.0 * radiusView)))
                    : 0.0;
                double sweep = radiusView > 0.0 ? 2.0 * Math.Asin(ratio) : 0.0;
                arcLengthModel = radiusModel * sweep;
            }

            if (mid == null)
            {
                double vx = ((p1[0] + p2[0]) / 2.0) - center[0];
                double vy = ((p1[1] + p2[1]) / 2.0) - center[1];
                double vLen = Math.Sqrt(vx * vx + vy * vy);
                if (vLen > 0.0000001)
                {
                    mid = new[]
                    {
                        center[0] + vx / vLen * radiusView,
                        center[1] + vy / vLen * radiusView,
                        0.0
                    };
                }
                else
                {
                    mid = new[] { (p1[0] + p2[0]) / 2.0, (p1[1] + p2[1]) / 2.0, 0.0 };
                }
            }

            return new ArcInfo
            {
                Edge = edge,
                CenterX = center[0],
                CenterY = center[1],
                StartX = p1[0],
                StartY = p1[1],
                EndX = p2[0],
                EndY = p2[1],
                MidX = mid[0],
                MidY = mid[1],
                RadiusMm = radiusView * 1000.0 / viewScale,
                ArcLengthMm = arcLengthModel * 1000.0,
                SweepAngleRad = radiusModel > 0.0 ? arcLengthModel / radiusModel : 0.0
            };
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

        private bool TryGetCircleData(Curve curve, out double[] center, out double radius)
        {
            center = null;
            radius = 0;

            object raw = null;
            try
            {
                raw = ((dynamic)curve).CircleParams;
            }
            catch
            {
            }

            if (raw == null)
            {
                try
                {
                    raw = ((dynamic)curve).GetCircleParams();
                }
                catch
                {
                }
            }

            double[] values = raw as double[];
            if (values == null)
            {
                object[] objects = raw as object[];
                if (objects != null)
                {
                    values = new double[objects.Length];
                    for (int i = 0; i < objects.Length; i++)
                        values[i] = Convert.ToDouble(objects[i]);
                }
            }

            if (values == null || values.Length < 7)
                return false;

            center = new[] { values[0], values[1], values[2] };
            radius = Math.Abs(values[6]);
            return radius > 0;
        }

        private double EstimateMaterialThicknessMm(List<EdgeInfo> edges)
        {
            if (edges == null || edges.Count == 0)
                return 0.0;

            List<double> candidates = new List<double>();

            // 1. L?y c?c c?nh ng?n nghi l? ?? d?y t?m
            foreach (EdgeInfo edge in edges)
            {
                if (edge == null)
                    continue;

                if (edge.LengthMm >= 0.8 && edge.LengthMm <= 8.0)
                    candidates.Add(edge.LengthMm);
            }

            // 2. L?y kho?ng c?ch gi?a c?c c?p c?nh song song nghi l? ?? d?y
            for (int i = 0; i < edges.Count; i++)
            {
                for (int j = i + 1; j < edges.Count; j++)
                {
                    EdgeInfo a = edges[i];
                    EdgeInfo b = edges[j];

                    if (a == null || b == null)
                        continue;

                    double gapViewM;
                    double overlapViewM;
                    if (!TryGetParallelMetrics(a, b, out gapViewM, out overlapViewM))
                        continue;

                    double gapMm = gapViewM * 1000.0 / viewScale;
                    if (gapMm < 0.8 || gapMm > 8.0)
                        continue;

                    double overlapMm = overlapViewM * 1000.0 / viewScale;
                    if (overlapMm < Math.Max(0.8, gapMm * 0.35))
                        continue;

                    candidates.Add(gapMm);
                }
            }

            double result = PickDominantSmallMeasureMm(candidates);

            Debug.WriteLine("[DIM MAT CAT] estimated thickness improved = "
                + result.ToString("0.###")
                + "mm, candidates="
                + candidates.Count);

            return result;
        }

        private double PickDominantSmallMeasureMm(List<double> values)
        {
            if (values == null || values.Count == 0)
                return 0.0;

            values.Sort();

            const double groupTolMm = 0.35;

            int bestCount = 0;
            double bestAverage = 0.0;

            int i = 0;
            while (i < values.Count)
            {
                int j = i;
                double sum = 0.0;
                int count = 0;

                while (j < values.Count && Math.Abs(values[j] - values[i]) <= groupTolMm)
                {
                    sum += values[j];
                    count++;
                    j++;
                }

                double average = sum / Math.Max(1, count);

                if (count > bestCount || (count == bestCount && (bestAverage <= 0.0 || average < bestAverage)))
                {
                    bestCount = count;
                    bestAverage = average;
                }

                i = j;
            }

            return bestAverage;
        }

        private List<ArcInfo> FilterUsableFilletArcs(List<ArcInfo> arcs, double materialThicknessMm)
        {
            List<ArcInfo> result = new List<ArcInfo>();
            double tolMm = Math.Max(0.06, materialThicknessMm * 0.04);
            double thicknessPlusBendMm = materialThicknessMm > 0 ? materialThicknessMm + 0.1 : 0.0;

            foreach (ArcInfo arc in arcs)
            {
                bool radiusMatchesThickness = materialThicknessMm > 0
                    && Math.Abs(arc.RadiusMm - materialThicknessMm) <= tolMm;
                bool diameterMatchesThickness = materialThicknessMm > 0
                    && Math.Abs(arc.RadiusMm * 2.0 - materialThicknessMm) <= tolMm;
                bool radiusMatchesThicknessPlusBend = thicknessPlusBendMm > 0
                    && Math.Abs(arc.RadiusMm - thicknessPlusBendMm) <= tolMm;
                bool diameterMatchesThicknessPlusBend = thicknessPlusBendMm > 0
                    && Math.Abs(arc.RadiusMm * 2.0 - thicknessPlusBendMm) <= tolMm;

                if (radiusMatchesThickness || diameterMatchesThickness || radiusMatchesThicknessPlusBend || diameterMatchesThicknessPlusBend)
                {
                    Debug.WriteLine("[DIM MAT CAT] skip thickness arc radiusMm=" + arc.RadiusMm.ToString("0.###")
                        + ", thicknessMm=" + materialThicknessMm.ToString("0.###")
                        + ", thicknessPlus0.1Mm=" + thicknessPlusBendMm.ToString("0.###"));
                    continue;
                }

                result.Add(arc);
                Debug.WriteLine("[DIM MAT CAT] usable arc radiusMm=" + arc.RadiusMm.ToString("0.###")
                    + ", center=(" + MToMm(arc.CenterX).ToString("0.###")
                    + "," + MToMm(arc.CenterY).ToString("0.###") + ")");
            }

            return result;
        }

        private List<ArcInfo> GetCurvedProfileReferenceArcs(
            List<ArcInfo> arcs,
            List<EdgeInfo> edges,
            double materialThicknessMm,
            ArcInfo selectedArc)
        {
            List<ArcInfo> candidates = new List<ArcInfo>();
            if (arcs == null || arcs.Count == 0)
                return candidates;

            List<ArcInfo> usable = FilterUsableFilletArcs(arcs, materialThicknessMm);
            double largeRadiusMm = Math.Max(20.0, materialThicknessMm * 8.0);
            double bendRadiusMm = materialThicknessMm > 0.0 ? materialThicknessMm + 0.2 : 0.0;
            double bendRadiusTolMm = Math.Max(0.08, materialThicknessMm * 0.06);

            foreach (ArcInfo arc in usable)
            {
                if (arc == null || arc.Edge == null || IsFullCircleArc(arc))
                    continue;

                bool isLargeProfileArc = arc.RadiusMm >= largeRadiusMm
                    && arc.ArcLengthMm >= 5.0;
                bool matchesThicknessPlus02 = bendRadiusMm > 0.0
                    && Math.Abs(arc.RadiusMm - bendRadiusMm) <= bendRadiusTolMm;
                bool isConnectedBendArc = arc.RadiusMm >= Math.Max(5.0, materialThicknessMm * 3.0)
                    && arc.ArcLengthMm >= 5.0
                    && CountConnectedLongLineEnds(arc, edges, materialThicknessMm) >= 2;

                if (isLargeProfileArc || matchesThicknessPlus02 || isConnectedBendArc)
                    candidates.Add(arc);
            }

            ArcInfo selectedCandidate = FindMatchingArcGeometry(candidates, selectedArc);
            if (selectedCandidate != null)
            {
                List<ArcInfo> selectedSide = new List<ArcInfo>();
                double centerTolMm = Math.Max(0.4, materialThicknessMm * 0.3);
                foreach (ArcInfo arc in candidates)
                {
                    double centerGapMm = Distance2D(
                        arc.CenterX,
                        arc.CenterY,
                        selectedCandidate.CenterX,
                        selectedCandidate.CenterY) * 1000.0 / viewScale;
                    if (centerGapMm <= centerTolMm
                        && Math.Abs(arc.RadiusMm - selectedCandidate.RadiusMm) <= 0.15)
                        selectedSide.Add(arc);
                }

                Debug.WriteLine("[DIM MAT CAT CUNG] selected contour R"
                    + selectedCandidate.RadiusMm.ToString("0.###")
                    + ", segments=" + selectedSide.Count);
                return selectedSide;
            }

            List<ArcInfo> innerContourArcs = SelectInnerContourArcs(candidates, materialThicknessMm);
            Debug.WriteLine("[DIM MAT CAT CUNG] candidates=" + candidates.Count
                + ", selectedInner=" + innerContourArcs.Count
                + ", thicknessMm=" + materialThicknessMm.ToString("0.###"));
            return innerContourArcs;
        }

        private bool IsFullCircleArc(ArcInfo arc)
        {
            if (arc == null || arc.RadiusMm <= 0.0)
                return true;

            double endGapMm = Distance2D(
                arc.StartX,
                arc.StartY,
                arc.EndX,
                arc.EndY) * 1000.0 / viewScale;
            double circumferenceMm = 2.0 * Math.PI * arc.RadiusMm;
            bool almostClosed = endGapMm <= Math.Max(0.05, arc.RadiusMm * 0.002);
            bool almostFullSweep = circumferenceMm > 0.0
                && arc.ArcLengthMm / circumferenceMm >= 0.95;
            return almostClosed || almostFullSweep || arc.SweepAngleRad >= Math.PI * 1.9;
        }

        private int CountConnectedLongLineEnds(
            ArcInfo arc,
            List<EdgeInfo> edges,
            double materialThicknessMm)
        {
            if (arc == null || edges == null)
                return 0;

            double minLineLengthMm = Math.Max(8.0, materialThicknessMm * 4.0);
            double tol = MmToViewM(Math.Max(0.25, Math.Min(0.8, materialThicknessMm * 0.3)));
            bool startConnected = false;
            bool endConnected = false;

            foreach (EdgeInfo edge in edges)
            {
                if (edge == null || edge.LengthMm < minLineLengthMm)
                    continue;

                if (DistanceToEdgeEndpoint(arc.StartX, arc.StartY, edge) <= tol)
                    startConnected = true;
                if (DistanceToEdgeEndpoint(arc.EndX, arc.EndY, edge) <= tol)
                    endConnected = true;
            }

            return (startConnected ? 1 : 0) + (endConnected ? 1 : 0);
        }

        private double DistanceToEdgeEndpoint(double x, double y, EdgeInfo edge)
        {
            if (edge == null)
                return double.MaxValue;

            return Math.Min(
                Distance2D(x, y, edge.X1, edge.Y1),
                Distance2D(x, y, edge.X2, edge.Y2));
        }

        private List<ArcInfo> SelectInnerContourArcs(
            List<ArcInfo> candidates,
            double materialThicknessMm)
        {
            List<ArcInfo> result = new List<ArcInfo>();
            if (candidates == null)
                return result;

            double maxRadiusGapMm = Math.Max(3.0, materialThicknessMm * 2.0 + 0.5);
            foreach (ArcInfo arc in candidates)
            {
                bool hasSmallerParallelArc = false;
                foreach (ArcInfo other in candidates)
                {
                    if (other == null || ReferenceEquals(other, arc))
                        continue;

                    double radiusGapMm = arc.RadiusMm - other.RadiusMm;
                    if (radiusGapMm <= 0.08 || radiusGapMm > maxRadiusGapMm)
                        continue;

                    if (AreParallelArcSegments(arc, other, materialThicknessMm))
                    {
                        hasSmallerParallelArc = true;
                        break;
                    }
                }

                if (!hasSmallerParallelArc)
                    result.Add(arc);
            }

            return result;
        }

        private bool AreParallelArcSegments(
            ArcInfo first,
            ArcInfo second,
            double materialThicknessMm)
        {
            if (first == null || second == null)
                return false;

            double centerGapMm = Distance2D(
                first.CenterX,
                first.CenterY,
                second.CenterX,
                second.CenterY) * 1000.0 / viewScale;
            if (centerGapMm > Math.Max(0.4, materialThicknessMm * 0.3))
                return false;

            if (Math.Abs(first.SweepAngleRad - second.SweepAngleRad) > 0.04)
                return false;

            bool sameDirection = ArcRadialDirectionMatches(
                first.CenterX, first.CenterY, first.StartX, first.StartY,
                second.CenterX, second.CenterY, second.StartX, second.StartY)
                && ArcRadialDirectionMatches(
                    first.CenterX, first.CenterY, first.EndX, first.EndY,
                    second.CenterX, second.CenterY, second.EndX, second.EndY);
            bool reverseDirection = ArcRadialDirectionMatches(
                first.CenterX, first.CenterY, first.StartX, first.StartY,
                second.CenterX, second.CenterY, second.EndX, second.EndY)
                && ArcRadialDirectionMatches(
                    first.CenterX, first.CenterY, first.EndX, first.EndY,
                    second.CenterX, second.CenterY, second.StartX, second.StartY);
            return sameDirection || reverseDirection;
        }

        private bool ArcRadialDirectionMatches(
            double centerX1,
            double centerY1,
            double pointX1,
            double pointY1,
            double centerX2,
            double centerY2,
            double pointX2,
            double pointY2)
        {
            double dx1 = pointX1 - centerX1;
            double dy1 = pointY1 - centerY1;
            double dx2 = pointX2 - centerX2;
            double dy2 = pointY2 - centerY2;
            double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
            double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
            if (len1 <= 0.0000001 || len2 <= 0.0000001)
                return false;

            double dot = (dx1 * dx2 + dy1 * dy2) / (len1 * len2);
            return dot >= 0.9995;
        }

        private int AddCurvedProfileDimensions(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            List<EdgeInfo> edges,
            List<ArcInfo> arcs,
            double materialThicknessMm,
            ArcInfo selectedArc)
        {
            List<ArcInfo> referenceArcs = GetCurvedProfileReferenceArcs(
                arcs,
                edges,
                materialThicknessMm,
                selectedArc);
            if (referenceArcs.Count == 0)
                return 0;

            double centerX;
            double centerY;
            GetCurvedProfileBoundsCenter(edges, referenceArcs, out centerX, out centerY);

            int count = 0;
            List<ArcInfo> radiusGroups = new List<ArcInfo>();
            foreach (ArcInfo arc in referenceArcs)
            {
                bool radiusAlreadyAdded = false;
                foreach (ArcInfo done in radiusGroups)
                {
                    double centerGapMm = Distance2D(
                        arc.CenterX,
                        arc.CenterY,
                        done.CenterX,
                        done.CenterY) * 1000.0 / viewScale;
                    if (centerGapMm <= Math.Max(0.4, materialThicknessMm * 0.3)
                        && Math.Abs(arc.RadiusMm - done.RadiusMm) <= 0.15)
                    {
                        radiusAlreadyAdded = true;
                        break;
                    }
                }

                if (!radiusAlreadyAdded)
                {
                    count += AddRadiusDimensionForArc(
                        model,
                        selectData,
                        arc,
                        centerX,
                        centerY,
                        18.0);
                    radiusGroups.Add(arc);
                }

                if (ShouldAddArcLengthDimension(arc, materialThicknessMm))
                {
                    count += AddArcLengthDimensionForArc(
                        model,
                        selectData,
                        arc,
                        centerX,
                        centerY,
                        10.0 + count * 2.5);
                }
            }

            List<EdgeInfo> connectedLines = GetConnectedCurvedProfileLines(
                referenceArcs,
                arcs,
                edges,
                materialThicknessMm);

            int connectedLineDimensionCount = 0;
            if (connectedLines.Count >= 2)
            {
                connectedLineDimensionCount = AddOrthogonalCurvedChainDimensions(
                    model,
                    selectData,
                    connectedLines,
                    centerX,
                    centerY);

                // Dung lai toan bo logic canh thang cu. Cung bo nho chi noi chuoi,
                // khong tao dimension rieng. Goc 90 dung envelope; goc khac 90
                // dung virtual sharp va dimension goc.
                if (connectedLineDimensionCount == 0)
                {
                    connectedLineDimensionCount = AddAngledOuterProfileDimensions(
                        model,
                        view,
                    selectData,
                    edges,
                    arcs,
                    connectedLines[0],
                    true);
                }

                count += connectedLineDimensionCount;
                Debug.WriteLine("[DIM MAT CAT CUNG] straight-chain legacy dimensions="
                    + connectedLineDimensionCount);
            }

            if (connectedLineDimensionCount == 0)
            {
                HashSet<Edge> dimensionedLines = new HashSet<Edge>();
                foreach (EdgeInfo edge in connectedLines)
                {
                    DimensionPlacement placement = GetOuterPlacement(edge, centerX, centerY);
                    count += AddEdgeLengthDimension(
                        model,
                        selectData,
                        edge,
                        placement,
                        DimOffsetMm,
                        dimensionedLines);
                }
            }

            Debug.WriteLine("[DIM MAT CAT CUNG] referenceArcs=" + referenceArcs.Count
                + ", connectedLines=" + connectedLines.Count
                + ", dimensions=" + count);
            return count;
        }

        private int AddOrthogonalCurvedChainDimensions(
            ModelDoc2 model,
            SelectData selectData,
            List<EdgeInfo> connectedLines,
            double centerX,
            double centerY)
        {
            if (model == null || selectData == null
                || connectedLines == null || connectedLines.Count != 2)
                return 0;

            EdgeInfo first = connectedLines[0];
            EdgeInfo second = connectedLines[1];
            if (first == null || second == null || first.Edge == null || second.Edge == null)
                return 0;

            double dot = Math.Abs(first.DirX * second.DirX + first.DirY * second.DirY);
            dot = Math.Max(0.0, Math.Min(1.0, dot));
            double angleDeg = Math.Acos(dot) * 180.0 / Math.PI;
            const double exactRightAngleEpsilonDeg = 0.000001;
            if (Math.Abs(90.0 - angleDeg) > exactRightAngleEpsilonDeg)
                return 0;

            double jointX;
            double jointY;
            if (!TryIntersectLines2D(first, second, out jointX, out jointY))
                return 0;

            double firstExtension = DistancePointToSegment(
                jointX, jointY, first.X1, first.Y1, first.X2, first.Y2);
            double secondExtension = DistancePointToSegment(
                jointX, jointY, second.X1, second.Y1, second.X2, second.Y2);
            if (firstExtension > MmToViewM(20.0) || secondExtension > MmToViewM(20.0))
                return 0;

            int count = 0;
            count += AddOrthogonalLineFromJointDimension(
                model,
                selectData,
                first,
                second,
                jointX,
                jointY,
                centerX,
                centerY);
            count += AddOrthogonalLineFromJointDimension(
                model,
                selectData,
                second,
                first,
                jointX,
                jointY,
                centerX,
                centerY);

            Debug.WriteLine("[DIM MAT CAT CUNG] orthogonal curved chain dimensions="
                + count + ", angle=" + angleDeg.ToString("0.######"));
            return count == 2 ? count : 0;
        }

        private int AddOrthogonalLineFromJointDimension(
            ModelDoc2 model,
            SelectData selectData,
            EdgeInfo line,
            EdgeInfo jointBoundary,
            double jointX,
            double jointY,
            double centerX,
            double centerY)
        {
            if (line == null || line.Edge == null
                || jointBoundary == null || jointBoundary.Edge == null)
                return 0;

            double d1 = Distance2D(jointX, jointY, line.X1, line.Y1);
            double d2 = Distance2D(jointX, jointY, line.X2, line.Y2);
            bool useStart = d1 >= d2;
            Vertex farVertex = useStart
                ? line.Edge.GetStartVertex() as Vertex
                : line.Edge.GetEndVertex() as Vertex;
            if (farVertex == null)
                return 0;

            double farX = useStart ? line.X1 : line.X2;
            double farY = useStart ? line.Y1 : line.Y2;
            int added = AddReferenceToReferenceDimension(
                model,
                selectData,
                jointBoundary.Edge,
                farVertex,
                line,
                jointX,
                jointY,
                farX,
                farY,
                centerX,
                centerY,
                DimOffsetMm);

            Debug.WriteLine("[DIM MAT CAT CUNG] orthogonal envelope target="
                + (Distance2D(jointX, jointY, farX, farY) * 1000.0 / viewScale).ToString("0.###")
                + ", physical=" + line.LengthMm.ToString("0.###")
                + ", added=" + added);
            return added;
        }

        private bool ShouldAddArcLengthDimension(ArcInfo arc, double materialThicknessMm)
        {
            if (arc == null || arc.ArcLengthMm <= 0.0)
                return false;

            double bendRadiusMm = materialThicknessMm > 0.0 ? materialThicknessMm + 0.2 : 0.0;
            double bendRadiusTolMm = Math.Max(0.08, materialThicknessMm * 0.06);
            bool matchesThicknessPlus02 = bendRadiusMm > 0.0
                && Math.Abs(arc.RadiusMm - bendRadiusMm) <= bendRadiusTolMm;
            bool isLargeArc = arc.RadiusMm >= Math.Max(20.0, materialThicknessMm * 8.0);
            return matchesThicknessPlus02 || isLargeArc;
        }

        private int AddRadiusDimensionForArc(
            ModelDoc2 model,
            SelectData selectData,
            ArcInfo arc,
            double profileCenterX,
            double profileCenterY,
            double offsetMm)
        {
            if (model == null || arc == null || arc.Edge == null)
                return 0;

            double x;
            double y;
            GetArcDimensionPosition(
                arc,
                profileCenterX,
                profileCenterY,
                offsetMm,
                out x,
                out y);

            model.ClearSelection2(true);
            if (!SelectEdge(arc.Edge, false, selectData))
                return 0;

            DisplayDimension dimension = model.AddRadialDimension2(x, y, 0) as DisplayDimension;
            if (dimension == null)
                return 0;

            ApplyCompactRadiusStyle(model, dimension);

            Debug.WriteLine("[DIM MAT CAT CUNG] added radius R" + arc.RadiusMm.ToString("0.###"));
            return 1;
        }

        private void ApplyCompactRadiusStyle(ModelDoc2 model, DisplayDimension dimension)
        {
            if (dimension == null)
                return;

            try
            {
                dimension.ShortenedRadius = true;
                bool bentLengthSet = dimension.SetBentLeaderLength(false, MmToM(5.0));
                Debug.WriteLine("[DIM MAT CAT CUNG] compact radius. shortened="
                    + dimension.ShortenedRadius
                    + ", bentLengthSet=" + bentLengthSet
                    + ", bentLengthMm=" + (dimension.GetBentLeaderLength() * 1000.0).ToString("0.###"));
                model?.GraphicsRedraw2();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT CUNG] compact radius failed: " + ex.Message);
            }
        }

        private int AddArcLengthDimensionForArc(
            ModelDoc2 model,
            SelectData selectData,
            ArcInfo arc,
            double profileCenterX,
            double profileCenterY,
            double offsetMm)
        {
            if (model == null || arc == null || arc.Edge == null)
                return 0;

            double x;
            double y;
            GetArcDimensionPosition(
                arc,
                profileCenterX,
                profileCenterY,
                offsetMm,
                out x,
                out y);

            // SolidWorks tao arc-length bang cach chon cung, sau do chon hai dau cung.
            // AddSpecificDimension khong ho tro swArcLengthDimension va se tao nham Radial.
            DisplayDimension dimension = TryAddArcLengthByReferences(
                model,
                selectData,
                arc,
                x,
                y,
                true);

            if (dimension == null)
            {
                dimension = TryAddArcLengthByReferences(
                    model,
                    selectData,
                    arc,
                    x,
                    y,
                    false);
            }

            if (dimension == null)
            {
                model.ClearSelection2(true);
                if (SelectEdge(arc.Edge, false, selectData))
                {
                    try
                    {
                        dimension = model.Extension.AddPathLengthDim(x, y, 0) as DisplayDimension;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DIM MAT CAT CUNG] AddPathLengthDim failed: " + ex.Message);
                    }
                }
            }

            if (dimension == null)
            {
                Debug.WriteLine("[DIM MAT CAT CUNG] arc length create failed, expectedMm="
                    + arc.ArcLengthMm.ToString("0.###"));
                return 0;
            }

            if (dimension.GetType() != (int)swDimensionType_e.swArcLengthDimension)
            {
                Annotation annotation = dimension.GetAnnotation() as Annotation;
                if (annotation != null && annotation.Select3(false, null))
                    model.EditDelete();
                Debug.WriteLine("[DIM MAT CAT CUNG] rejected non arc-length dimension type="
                    + dimension.GetType());
                return 0;
            }

            ApplyRadialArcLengthLeader(model, dimension);

            Debug.WriteLine("[DIM MAT CAT CUNG] added arc length="
                + arc.ArcLengthMm.ToString("0.###"));
            return 1;
        }

        private void ApplyRadialArcLengthLeader(ModelDoc2 model, DisplayDimension dimension)
        {
            if (dimension == null)
                return;

            try
            {
                int status = dimension.SetArcLengthLeader(
                    false,
                    (int)swArcLengthLeaderType_e.swArcLengthLeaderRadial);
                Debug.WriteLine("[DIM MAT CAT CUNG] radial arc leader status=" + status);
                model?.GraphicsRedraw2();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT CUNG] set radial arc leader failed: " + ex.Message);
            }
        }

        private DisplayDimension TryAddArcLengthByReferences(
            ModelDoc2 model,
            SelectData selectData,
            ArcInfo arc,
            double x,
            double y,
            bool arcFirst)
        {
            Vertex startVertex = null;
            Vertex endVertex = null;
            try
            {
                startVertex = arc.Edge.GetStartVertex() as Vertex;
                endVertex = arc.Edge.GetEndVertex() as Vertex;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT CUNG] read arc vertices failed: " + ex.Message);
            }

            if (startVertex == null || endVertex == null)
            {
                Debug.WriteLine("[DIM MAT CAT CUNG] arc vertices unavailable");
                return null;
            }

            model.ClearSelection2(true);
            bool selected;
            if (arcFirst)
            {
                selected = SelectEdge(arc.Edge, false, selectData)
                    && SelectReference(startVertex, true, selectData)
                    && SelectReference(endVertex, true, selectData);
            }
            else
            {
                selected = SelectReference(startVertex, false, selectData)
                    && SelectReference(endVertex, true, selectData)
                    && SelectEdge(arc.Edge, true, selectData);
            }

            if (!selected)
            {
                Debug.WriteLine("[DIM MAT CAT CUNG] select arc endpoints failed, arcFirst=" + arcFirst);
                model.ClearSelection2(true);
                return null;
            }

            DisplayDimension dimension = null;
            try
            {
                dimension = model.AddDimension2(x, y, 0) as DisplayDimension;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT CUNG] AddDimension2 arc length failed: " + ex.Message);
            }

            if (dimension == null)
            {
                Debug.WriteLine("[DIM MAT CAT CUNG] AddDimension2 returned null, arcFirst=" + arcFirst);
                return null;
            }

            int dimensionType = dimension.GetType();
            if (dimensionType == (int)swDimensionType_e.swArcLengthDimension)
            {
                Debug.WriteLine("[DIM MAT CAT CUNG] native arc length created, arcFirst=" + arcFirst);
                return dimension;
            }

            DeleteDisplayDimension(model, dimension);
            Debug.WriteLine("[DIM MAT CAT CUNG] native arc selection returned wrong type="
                + dimensionType + ", arcFirst=" + arcFirst);
            return null;
        }

        private void DeleteDisplayDimension(ModelDoc2 model, DisplayDimension dimension)
        {
            if (model == null || dimension == null)
                return;

            try
            {
                model.ClearSelection2(true);
                Annotation annotation = dimension.GetAnnotation() as Annotation;
                if (annotation != null && annotation.Select3(false, null))
                    model.EditDelete();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT CUNG] delete invalid dimension failed: " + ex.Message);
            }
        }

        private void GetArcDimensionPosition(
            ArcInfo arc,
            double profileCenterX,
            double profileCenterY,
            double offsetMm,
            out double x,
            out double y)
        {
            double dx = arc.MidX - profileCenterX;
            double dy = arc.MidY - profileCenterY;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len <= 0.0000001)
            {
                dx = arc.MidX - arc.CenterX;
                dy = arc.MidY - arc.CenterY;
                len = Math.Sqrt(dx * dx + dy * dy);
            }

            if (len <= 0.0000001)
            {
                dx = 0.0;
                dy = -1.0;
                len = 1.0;
            }

            x = arc.MidX + dx / len * MmToM(offsetMm);
            y = arc.MidY + dy / len * MmToM(offsetMm);
        }

        private void GetCurvedProfileBoundsCenter(
            List<EdgeInfo> edges,
            List<ArcInfo> arcs,
            out double centerX,
            out double centerY)
        {
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            if (edges != null)
            {
                foreach (EdgeInfo edge in edges)
                {
                    if (edge == null)
                        continue;
                    minX = Math.Min(minX, edge.MinX);
                    maxX = Math.Max(maxX, edge.MaxX);
                    minY = Math.Min(minY, edge.MinY);
                    maxY = Math.Max(maxY, edge.MaxY);
                }
            }

            if (arcs != null)
            {
                foreach (ArcInfo arc in arcs)
                {
                    if (arc == null)
                        continue;
                    minX = Math.Min(minX, Math.Min(arc.StartX, Math.Min(arc.EndX, arc.MidX)));
                    maxX = Math.Max(maxX, Math.Max(arc.StartX, Math.Max(arc.EndX, arc.MidX)));
                    minY = Math.Min(minY, Math.Min(arc.StartY, Math.Min(arc.EndY, arc.MidY)));
                    maxY = Math.Max(maxY, Math.Max(arc.StartY, Math.Max(arc.EndY, arc.MidY)));
                }
            }

            if (minX == double.MaxValue)
            {
                centerX = 0.0;
                centerY = 0.0;
                return;
            }

            centerX = (minX + maxX) / 2.0;
            centerY = (minY + maxY) / 2.0;
        }

        private List<EdgeInfo> GetConnectedCurvedProfileLines(
            List<ArcInfo> referenceArcs,
            List<ArcInfo> allArcs,
            List<EdgeInfo> edges,
            double materialThicknessMm)
        {
            List<EdgeInfo> result = new List<EdgeInfo>();
            if (referenceArcs == null || referenceArcs.Count == 0 || edges == null)
                return result;

            double joinTol = MmToViewM(Math.Max(0.2, Math.Min(0.7, materialThicknessMm * 0.3)));
            double minLineLengthMm = Math.Max(4.0, materialThicknessMm * 2.2);
            List<double[]> frontier = new List<double[]>();
            HashSet<ArcInfo> visitedArcs = new HashSet<ArcInfo>();
            HashSet<EdgeInfo> visitedLines = new HashSet<EdgeInfo>();

            foreach (ArcInfo arc in referenceArcs)
            {
                visitedArcs.Add(arc);
                AddFrontierPoint(frontier, arc.StartX, arc.StartY, joinTol);
                AddFrontierPoint(frontier, arc.EndX, arc.EndY, joinTol);
            }

            bool changed = true;
            int guard = 0;
            while (changed && guard++ < 100)
            {
                changed = false;

                if (allArcs != null)
                {
                    foreach (ArcInfo arc in allArcs)
                    {
                        if (arc == null || visitedArcs.Contains(arc) || IsFullCircleArc(arc))
                            continue;

                        bool startNear = IsFrontierPointNear(frontier, arc.StartX, arc.StartY, joinTol);
                        bool endNear = IsFrontierPointNear(frontier, arc.EndX, arc.EndY, joinTol);
                        if (!startNear && !endNear)
                            continue;

                        visitedArcs.Add(arc);
                        AddFrontierPoint(frontier, arc.StartX, arc.StartY, joinTol);
                        AddFrontierPoint(frontier, arc.EndX, arc.EndY, joinTol);
                        changed = true;
                    }
                }

                foreach (EdgeInfo edge in edges)
                {
                    if (edge == null || visitedLines.Contains(edge) || edge.LengthMm < minLineLengthMm)
                        continue;

                    bool startNear = IsFrontierPointNear(frontier, edge.X1, edge.Y1, joinTol);
                    bool endNear = IsFrontierPointNear(frontier, edge.X2, edge.Y2, joinTol);
                    if (!startNear && !endNear)
                        continue;

                    visitedLines.Add(edge);
                    result.Add(edge);
                    AddFrontierPoint(frontier, edge.X1, edge.Y1, joinTol);
                    AddFrontierPoint(frontier, edge.X2, edge.Y2, joinTol);
                    changed = true;
                }
            }

            return RemoveParallelDuplicateProfileLines(result, materialThicknessMm);
        }

        private List<EdgeInfo> RemoveParallelDuplicateProfileLines(
            List<EdgeInfo> lines,
            double materialThicknessMm)
        {
            List<EdgeInfo> result = new List<EdgeInfo>();
            if (lines == null)
                return result;

            foreach (EdgeInfo edge in lines)
            {
                bool duplicate = false;
                foreach (EdgeInfo kept in result)
                {
                    double gapViewM;
                    double overlapViewM;
                    if (!TryGetParallelMetrics(edge, kept, out gapViewM, out overlapViewM))
                        continue;

                    double gapMm = gapViewM * 1000.0 / viewScale;
                    double overlapMm = overlapViewM * 1000.0 / viewScale;
                    if (gapMm <= Math.Max(0.5, materialThicknessMm * 1.5)
                        && overlapMm >= Math.Min(edge.LengthMm, kept.LengthMm) * 0.65)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                    result.Add(edge);
            }

            return result;
        }

        private void AddFrontierPoint(
            List<double[]> points,
            double x,
            double y,
            double tolerance)
        {
            if (IsFrontierPointNear(points, x, y, tolerance))
                return;
            points.Add(new[] { x, y });
        }

        private bool IsFrontierPointNear(
            List<double[]> points,
            double x,
            double y,
            double tolerance)
        {
            if (points == null)
                return false;
            foreach (double[] point in points)
            {
                if (point != null && point.Length >= 2
                    && Distance2D(point[0], point[1], x, y) <= tolerance)
                    return true;
            }
            return false;
        }

        private EdgeInfo MakeEdgeInfo(Edge edge, MathUtility mathUtil, MathTransform viewTransform)
        {
            Curve curve = edge?.GetCurve() as Curve;
            if (curve == null || !curve.IsLine())
                return null;

            double[] p1Model = null;
            double[] p2Model = null;
            try
            {
                CurveParamData edgeParams = edge.GetCurveParams3();
                if (edgeParams != null)
                {
                    p1Model = edgeParams.StartPoint as double[];
                    p2Model = edgeParams.EndPoint as double[];
                }
            }
            catch
            {
            }

            if (!IsModelPoint(p1Model) || !IsModelPoint(p2Model))
            {
                Vertex startVertex = edge.GetStartVertex() as Vertex;
                Vertex endVertex = edge.GetEndVertex() as Vertex;
                p1Model = startVertex?.GetPoint() as double[];
                p2Model = endVertex?.GetPoint() as double[];
            }

            if (!IsModelPoint(p1Model) || !IsModelPoint(p2Model))
                return null;

            double[] p1 = TransformPoint(mathUtil, viewTransform, p1Model);
            double[] p2 = TransformPoint(mathUtil, viewTransform, p2Model);
            if (p1 == null || p2 == null)
                return null;

            double dx = p2[0] - p1[0];
            double dy = p2[1] - p1[1];
            double absDx = Math.Abs(dx);
            double absDy = Math.Abs(dy);
            double tol = MmToM(OrthoTolMm);

            bool isHorizontal = absDy <= tol && absDx > tol;
            bool isVertical = absDx <= tol && absDy > tol;

            double lengthView = Math.Sqrt(dx * dx + dy * dy);
            if (lengthView <= tol)
                return null;

            double dirX = dx / lengthView;
            double dirY = dy / lengthView;

            // Normalize direction so comparison is stable
            if (dirX < 0 || (Math.Abs(dirX) < 0.000001 && dirY < 0))
            {
                dirX = -dirX;
                dirY = -dirY;
            }

            double normX = -dirY;
            double normY = dirX;

            return new EdgeInfo
            {
                Edge = edge,

                X1 = p1[0],
                Y1 = p1[1],
                X2 = p2[0],
                Y2 = p2[1],

                MinX = Math.Min(p1[0], p2[0]),
                MaxX = Math.Max(p1[0], p2[0]),
                MinY = Math.Min(p1[1], p2[1]),
                MaxY = Math.Max(p1[1], p2[1]),

                MidX = (p1[0] + p2[0]) / 2.0,
                MidY = (p1[1] + p2[1]) / 2.0,

                DirX = dirX,
                DirY = dirY,
                NormX = normX,
                NormY = normY,

                IsHorizontal = isHorizontal,
                IsVertical = isVertical,
                IsAngled = !isHorizontal && !isVertical,

                LengthMm = lengthView * 1000.0 / viewScale
            };
        }

        private double[] TransformPoint(MathUtility mathUtil, MathTransform viewTransform, double[] modelPoint)
        {
            if (mathUtil == null || viewTransform == null || modelPoint == null || modelPoint.Length < 3)
                return null;

            MathPoint point = mathUtil.CreatePoint(new[] { modelPoint[0], modelPoint[1], modelPoint[2] }) as MathPoint;
            point = point?.MultiplyTransform(viewTransform) as MathPoint;
            double[] data = point?.ArrayData as double[];
            if (data == null || data.Length < 2)
                return null;

            return data;
        }

        private bool IsModelPoint(double[] point)
        {
            return point != null && point.Length >= 3;
        }

        private EdgeInfo FindBestParallelPair(List<EdgeInfo> edges, EdgeInfo selected)
        {
            EdgeInfo best = null;
            double bestScore = double.MaxValue;

            foreach (EdgeInfo candidate in edges)
            {
                if (candidate == selected)
                    continue;

                if (selected.IsVertical && candidate.IsVertical)
                {
                    double gapMm = Math.Abs(candidate.MidX - selected.MidX) * 1000.0 / viewScale;
                    if (gapMm < MinGapMm || gapMm > MaxGapMm)
                        continue;

                    double overlapMm = OverlapLength(selected.MinY, selected.MaxY, candidate.MinY, candidate.MaxY) * 1000.0 / viewScale;
                    if (overlapMm < MinOverlapMm)
                        continue;

                    double score = gapMm - overlapMm * 0.01;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
                else if (selected.IsHorizontal && candidate.IsHorizontal)
                {
                    double gapMm = Math.Abs(candidate.MidY - selected.MidY) * 1000.0 / viewScale;
                    if (gapMm < MinGapMm || gapMm > MaxGapMm)
                        continue;

                    double overlapMm = OverlapLength(selected.MinX, selected.MaxX, candidate.MinX, candidate.MaxX) * 1000.0 / viewScale;
                    if (overlapMm < MinOverlapMm)
                        continue;

                    double score = gapMm - overlapMm * 0.01;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }

            return best;
        }

        private bool FindBestParallelPairInView(List<EdgeInfo> edges, out EdgeInfo first, out EdgeInfo second)
        {
            first = null;
            second = null;
            double bestScore = double.MinValue;

            if (!FindBestParallelPairInView(edges, true, out first, out second, out bestScore))
                FindBestParallelPairInView(edges, false, out first, out second, out bestScore);

            Debug.WriteLine("[DIM MAT CAT] best view pair=" + (first != null && second != null)
                + ", score=" + (bestScore == double.MinValue ? "n/a" : bestScore.ToString("0.###"))
                + ", horizontal=" + (first != null && first.IsHorizontal));

            return first != null && second != null;
        }

        private bool FindBestParallelPairInView(List<EdgeInfo> edges, bool horizontalOnly, out EdgeInfo first, out EdgeInfo second, out double bestScore)
        {
            first = null;
            second = null;
            bestScore = double.MinValue;

            for (int i = 0; i < edges.Count; i++)
            {
                for (int j = i + 1; j < edges.Count; j++)
                {
                    EdgeInfo a = edges[i];
                    EdgeInfo b = edges[j];
                    double score;

                    if (horizontalOnly && (!a.IsHorizontal || !b.IsHorizontal))
                        continue;
                    if (!horizontalOnly && (!a.IsVertical || !b.IsVertical))
                        continue;

                    if (!TryScoreParallelPair(a, b, out score))
                        continue;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        first = a;
                        second = b;
                    }
                }
            }

            return first != null && second != null;
        }

        private bool TryScoreParallelPair(EdgeInfo a, EdgeInfo b, out double score)
        {
            score = double.MaxValue;

            if (a.IsVertical && b.IsVertical)
            {
                double gapMm = Math.Abs(b.MidX - a.MidX) * 1000.0 / viewScale;
                if (gapMm < MinGapMm || gapMm > MaxGapMm)
                    return false;

                double overlapMm = OverlapLength(a.MinY, a.MaxY, b.MinY, b.MaxY) * 1000.0 / viewScale;
                if (overlapMm < MinOverlapMm)
                    return false;

                score = gapMm + overlapMm * 0.01;
                return true;
            }

            if (a.IsHorizontal && b.IsHorizontal)
            {
                double gapMm = Math.Abs(b.MidY - a.MidY) * 1000.0 / viewScale;
                if (gapMm < MinGapMm || gapMm > MaxGapMm)
                    return false;

                double overlapMm = OverlapLength(a.MinX, a.MaxX, b.MinX, b.MaxX) * 1000.0 / viewScale;
                if (overlapMm < MinOverlapMm)
                    return false;

                score = gapMm + overlapMm * 0.01;
                return true;
            }

            return false;
        }

        private bool AreParallelEdges(EdgeInfo a, EdgeInfo b)
        {
            if (a == null || b == null)
                return false;

            double cross = Math.Abs(a.DirX * b.DirY - a.DirY * b.DirX);
            double tol = Math.Sin(ParallelAngleTolDeg * Math.PI / 180.0);

            return cross <= tol;
        }

        private bool IsMeaningfullyAngled(EdgeInfo edge)
        {
            if (edge == null || !edge.IsAngled || edge.LengthMm < 5.0)
                return false;

            double angleDeg = Math.Atan2(Math.Abs(edge.DirY), Math.Abs(edge.DirX))
                * 180.0 / Math.PI;
            double deviationFromAxisDeg = Math.Min(angleDeg, Math.Abs(90.0 - angleDeg));

            // Ignore small projection errors and short chamfer/thickness edges.
            // The special profile path is only for a clearly inclined flange.
            return deviationFromAxisDeg >= 5.0;
        }

        private bool HasMeaningfulAngledEdge(List<EdgeInfo> edges)
        {
            if (edges == null)
                return false;

            foreach (EdgeInfo edge in edges)
            {
                if (IsMeaningfullyAngled(edge))
                    return true;
            }

            return false;
        }

        private bool TryGetParallelMetrics(
            EdgeInfo a,
            EdgeInfo b,
            out double gapViewM,
            out double overlapViewM)
        {
            gapViewM = 0.0;
            overlapViewM = 0.0;

            if (!AreParallelEdges(a, b))
                return false;

            gapViewM = Math.Abs(
                (b.MidX - a.MidX) * a.NormX
                + (b.MidY - a.MidY) * a.NormY);

            double a1 = a.X1 * a.DirX + a.Y1 * a.DirY;
            double a2 = a.X2 * a.DirX + a.Y2 * a.DirY;
            double b1 = b.X1 * a.DirX + b.Y1 * a.DirY;
            double b2 = b.X2 * a.DirX + b.Y2 * a.DirY;

            overlapViewM = OverlapLength(
                Math.Min(a1, a2),
                Math.Max(a1, a2),
                Math.Min(b1, b2),
                Math.Max(b1, b2));

            return true;
        }

        private bool TryIntersectLines2D(
            EdgeInfo a,
            EdgeInfo b,
            out double x,
            out double y)
        {
            x = 0.0;
            y = 0.0;

            if (a == null || b == null)
                return false;

            double rX = a.X2 - a.X1;
            double rY = a.Y2 - a.Y1;
            double sX = b.X2 - b.X1;
            double sY = b.Y2 - b.Y1;

            double denom = rX * sY - rY * sX;
            if (Math.Abs(denom) < 0.0000000001)
                return false;

            double qpx = b.X1 - a.X1;
            double qpy = b.Y1 - a.Y1;

            double t = (qpx * sY - qpy * sX) / denom;

            x = a.X1 + t * rX;
            y = a.Y1 + t * rY;

            return true;
        }

        private double GetEdgeToEdgeNearDistance(EdgeInfo a, EdgeInfo b)
        {
            if (a == null || b == null)
                return double.MaxValue;

            double d1 = DistancePointToSegment(a.X1, a.Y1, b.X1, b.Y1, b.X2, b.Y2);
            double d2 = DistancePointToSegment(a.X2, a.Y2, b.X1, b.Y1, b.X2, b.Y2);
            double d3 = DistancePointToSegment(b.X1, b.Y1, a.X1, a.Y1, a.X2, a.Y2);
            double d4 = DistancePointToSegment(b.X2, b.Y2, a.X1, a.Y1, a.X2, a.Y2);

            return Math.Min(Math.Min(d1, d2), Math.Min(d3, d4));
        }

        private EdgeInfo FindAngleMateForEdge(List<EdgeInfo> edges, EdgeInfo angledEdge)
        {
            if (edges == null || angledEdge == null)
                return null;

            EdgeInfo best = null;
            double bestScore = double.MaxValue;
            double tol = MmToViewM(AngleMateTolMm);

            foreach (EdgeInfo candidate in edges)
            {
                if (candidate == null || candidate == angledEdge)
                    continue;

                if (AreParallelEdges(angledEdge, candidate))
                    continue;

                double nearDistance = GetEdgeToEdgeNearDistance(angledEdge, candidate);

                double ix;
                double iy;
                bool hasIntersection = TryIntersectLines2D(angledEdge, candidate, out ix, out iy);

                double intersectionPenalty = 0.0;
                if (hasIntersection)
                {
                    double dA = DistancePointToSegment(ix, iy, angledEdge.X1, angledEdge.Y1, angledEdge.X2, angledEdge.Y2);
                    double dB = DistancePointToSegment(ix, iy, candidate.X1, candidate.Y1, candidate.X2, candidate.Y2);
                    intersectionPenalty = dA + dB;
                }

                double score = nearDistance + intersectionPenalty * 0.35;

                if (nearDistance <= tol || intersectionPenalty <= tol * 2.0)
                {
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }

            Debug.WriteLine("[DIM MAT CAT] angle mate for "
                + EdgeSummary(angledEdge)
                + " = "
                + EdgeSummary(best));

            return best;
        }

        private bool AddParallelDimension(ModelDoc2 model, SolidWorks.Interop.sldworks.View view, SelectData selectData, EdgeInfo selected, EdgeInfo pair)
        {
            model.ClearSelection2(true);
            if (!SelectEdge(selected.Edge, false, selectData) || !SelectEdge(pair.Edge, true, selectData))
                return false;

            double x;
            double y;
            if (selected.IsVertical && pair.IsVertical)
            {
                x = (selected.MidX + pair.MidX) / 2.0;
                y = Math.Max(selected.MaxY, pair.MaxY) + MmToM(DimOffsetMm);
            }
            else
            {
                x = Math.Max(selected.MaxX, pair.MaxX) + MmToM(DimOffsetMm);
                y = (selected.MidY + pair.MidY) / 2.0;
            }

            DisplayDimension displayDimension = model.AddDimension2(x, y, 0) as DisplayDimension;
            if (displayDimension == null)
                return false;

            int dimensionType = displayDimension.GetType();
            if (dimensionType != (int)swDimensionType_e.swAngularDimension)
                return true;

            Annotation annotation = displayDimension.GetAnnotation() as Annotation;
            if (annotation != null && annotation.Select3(false, null))
                model.EditDelete();

            return false;
        }

        private int AddAngularDimensionsInView(
            ModelDoc2 model,
            SelectData selectData,
            List<EdgeInfo> edges,
            HashSet<string> dimensionedPairs)
        {
            if (model == null || selectData == null || edges == null)
                return 0;

            int count = 0;

            foreach (EdgeInfo edge in edges)
            {
                if (edge == null || !edge.IsAngled)
                    continue;

                EdgeInfo mate = FindAngleMateForEdge(edges, edge);
                if (mate == null)
                    continue;

                count += AddAngularDimension(
                    model,
                    selectData,
                    edge,
                    mate,
                    DimOffsetMm,
                    dimensionedPairs);
            }

            Debug.WriteLine("[DIM MAT CAT] angular dimensions in view=" + count);

            return count;
        }

        private int AddAngularDimension(
            ModelDoc2 model,
            SelectData selectData,
            EdgeInfo first,
            EdgeInfo second,
            double offsetMm,
            HashSet<string> dimensionedPairs)
        {
            if (model == null || first == null || second == null)
                return 0;

            if (first.Edge == null || second.Edge == null)
                return 0;

            string key = MakePairKey(first, second);
            if (dimensionedPairs != null && dimensionedPairs.Contains(key))
                return 0;

            model.ClearSelection2(true);

            if (!SelectEdge(first.Edge, false, selectData) ||
                !SelectEdge(second.Edge, true, selectData))
                return 0;

            double x;
            double y;

            double ix;
            double iy;
            if (TryIntersectLines2D(first, second, out ix, out iy))
            {
                double directionX = ((first.MidX + second.MidX) / 2.0) - ix;
                double directionY = ((first.MidY + second.MidY) / 2.0) - iy;
                double len = Math.Sqrt(directionX * directionX + directionY * directionY);

                if (len <= 0.0000001)
                {
                    directionX = 1.0;
                    directionY = -1.0;
                    len = Math.Sqrt(2.0);
                }

                directionX /= len;
                directionY /= len;

                x = ix + directionX * MmToM(offsetMm * 1.8);
                y = iy + directionY * MmToM(offsetMm * 1.8);
            }
            else
            {
                x = (first.MidX + second.MidX) / 2.0 + MmToM(offsetMm);
                y = (first.MidY + second.MidY) / 2.0 - MmToM(offsetMm);
            }

            DisplayDimension displayDimension = model.AddDimension2(x, y, 0) as DisplayDimension;
            if (displayDimension == null)
                return 0;

            int dimensionType = displayDimension.GetType();

            // This function only keeps angular dimensions.
            // If SolidWorks creates a linear dimension here, delete it.
            if (dimensionType != (int)swDimensionType_e.swAngularDimension)
            {
                Annotation annotation = displayDimension.GetAnnotation() as Annotation;
                if (annotation != null && annotation.Select3(false, null))
                    model.EditDelete();

                return 0;
            }

            if (dimensionedPairs != null)
                dimensionedPairs.Add(key);

            Debug.WriteLine("[DIM MAT CAT] added angular dimension. first="
                + EdgeSummary(first)
                + ", second="
                + EdgeSummary(second));

            return 1;
        }

        private int AddSelectedAngleDimension(
            ModelDoc2 model,
            SelectData selectData,
            List<EdgeInfo> edges,
            EdgeInfo selected)
        {
            if (selected == null || edges == null)
                return 0;

            EdgeInfo mate = FindAngleMateForEdge(edges, selected);
            if (mate == null)
            {
                Debug.WriteLine("[DIM MAT CAT] no angle mate found for selected edge="
                    + EdgeSummary(selected));
                return 0;
            }

            HashSet<string> dimensionedPairs = new HashSet<string>();

            return AddAngularDimension(
                model,
                selectData,
                selected,
                mate,
                DimOffsetMm,
                dimensionedPairs);
        }

        private void DeletePreviousDimensionArtifacts(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view)
        {
            int dimensionCount = DeleteDisplayDimensionsInView(model, view);
            int pointCount = DeleteSketchPointsInView(model, view);
            model?.ClearSelection2(true);
            model?.EditRebuild3();

            Debug.WriteLine("[DIM MAT CAT] cleared previous artifacts. dimensions="
                + dimensionCount + ", sketchPoints=" + pointCount);
        }

        private int DeleteSketchPointsInView(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view)
        {
            if (model == null || view == null)
                return 0;

            Sketch sketch = null;
            try
            {
                sketch = view.GetSketch() as Sketch;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT] get view sketch for cleanup failed: " + ex.Message);
            }

            if (sketch == null)
                return 0;

            // Snapshot first because deleting a virtual sharp mutates the
            // sketch point collection immediately.
            List<SketchPoint> points = GetUserSketchPoints(sketch);
            int count = 0;
            foreach (SketchPoint point in points)
            {
                if (point == null)
                    continue;

                try
                {
                    model.SetPickMode();
                    model.ClearSelection2(true);
                    if (!SelectReference(point, false, null))
                        continue;

                    model.EditDelete();
                    count++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[DIM MAT CAT] delete view sketch point failed: " + ex.Message);
                }
            }

            model.SetPickMode();
            model.ClearSelection2(true);
            Debug.WriteLine("[DIM MAT CAT] deleted view sketch points=" + count);
            return count;
        }

        private int DeleteDisplayDimensionsInView(ModelDoc2 model, SolidWorks.Interop.sldworks.View view)
        {
            if (model == null || view == null)
                return 0;

            Array annotations = null;
            try
            {
                annotations = view.GetAnnotations() as Array;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT] get view annotations failed: " + ex.Message);
            }

            if (annotations == null)
                return 0;

            int count = 0;
            foreach (object item in annotations)
            {
                Annotation annotation = item as Annotation;
                if (annotation == null || annotation.GetType() != (int)swAnnotationType_e.swDisplayDimension)
                    continue;

                try
                {
                    model.ClearSelection2(true);
                    if (!annotation.Select3(false, null))
                        continue;

                    model.EditDelete();
                    count++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[DIM MAT CAT] delete view dimension failed: " + ex.Message);
                }
            }

            model.ClearSelection2(true);
            Debug.WriteLine("[DIM MAT CAT] deleted view dimensions=" + count);
            return count;
        }

        private void EnableNativeVirtualSharpDisplay(ModelDoc2 model)
        {
            try
            {
                model.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayVirtualSharps, true);
                model.SetUserPreferenceIntegerValue(
                    (int)swUserPreferenceIntegerValue_e.swDetailingVirtualSharpStyle,
                    (int)swDetailingVirtualSharp_e.swDetailingVirtualSharpStar);
                Debug.WriteLine("[DIM MAT CAT] native virtual sharp display enabled, style=Star");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT] native virtual sharp display enable failed: " + ex.Message);
            }
        }

        private int AddAngledOuterProfileDimensions(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            List<EdgeInfo> edges,
            List<ArcInfo> arcs,
            EdgeInfo selectedContourSeed,
            bool allowTwoEdgeContour = false)
        {
            if (model == null || view == null || selectData == null || edges == null || edges.Count == 0)
                return 0;

            List<EdgeInfo> contourEdges = GetSelectedContourCandidateEdges(
                edges, arcs, selectedContourSeed, allowTwoEdgeContour);
            if (contourEdges == null || contourEdges.Count < 2)
                return 0;

            List<OuterProfileJoint> joints = BuildOuterProfileJoints(
                contourEdges,
                arcs,
                selectedContourSeed);
            if (joints.Count == 0)
            {
                Debug.WriteLine("[DIM MAT CAT] angled outer profile: no connected outer-edge joints found");
                return 0;
            }

            RefineTerminalNonOrthogonalJoints(
                joints,
                contourEdges,
                edges,
                selectedContourSeed);

            double centerX;
            double centerY;
            GetEdgeBoundsCenter(contourEdges, out centerX, out centerY);

            int count = 0;
            HashSet<Edge> dimensionedEdges = new HashSet<Edge>();
            HashSet<string> dimensionedPairs = new HashSet<string>();

            foreach (OuterProfileJoint joint in joints)
            {
                joint.UseVirtualSharp = ShouldAddProfileAngle(joint.First, joint.Second);
                if (joint.UseVirtualSharp)
                {
                    joint.Sharp = CreateNativeVirtualSharp(
                        model,
                        view,
                        selectData,
                        joint.First,
                        joint.Second);
                }
                else
                {
                    Debug.WriteLine("[DIM MAT CAT] orthogonal joint uses legacy edge dimension. first="
                        + EdgeSummary(joint.First)
                        + ", second=" + EdgeSummary(joint.Second));
                }
            }

            foreach (EdgeInfo edge in contourEdges)
            {
                OuterProfileJoint startJoint = FindJointForEdgeSlot(joints, edge, 0);
                OuterProfileJoint endJoint = FindJointForEdgeSlot(joints, edge, 1);
                int edgeDimensionCount = 0;
                bool hasAcuteJoint = IsAcuteProfileJoint(startJoint)
                    || IsAcuteProfileJoint(endJoint);

                if (edgeDimensionCount == 0
                    && startJoint?.Sharp != null
                    && endJoint?.Sharp != null)
                {
                    if (IsObtuseProfileJoint(startJoint)
                        && IsObtuseProfileJoint(endJoint)
                        && IsDominantProfileEdge(edge, contourEdges))
                    {
                        edgeDimensionCount = AddPhysicalProfileEnvelopeDimension(
                            model,
                            selectData,
                            edge,
                            startJoint,
                            endJoint,
                            joints,
                            centerX,
                            centerY,
                            DimOffsetMm);

                        if (edgeDimensionCount > 0)
                        {
                            Debug.WriteLine("[DIM MAT CAT] obtuse dominant edge uses physical envelope. edge="
                                + EdgeSummary(edge));
                        }
                    }

                    if (edgeDimensionCount == 0)
                    {
                    OuterProfileJoint boundaryJoint = startJoint.DimensionBoundaryEdge != null
                        ? startJoint
                        : (endJoint.DimensionBoundaryEdge != null ? endJoint : null);
                    OuterProfileJoint pointJoint = boundaryJoint == startJoint
                        ? endJoint
                        : startJoint;

                    if (edge.IsAngled && boundaryJoint != null && pointJoint?.Sharp != null)
                    {
                        double boundaryX = boundaryJoint.X;
                        double boundaryY = boundaryJoint.Y;
                        TryIntersectLines2D(
                            edge,
                            boundaryJoint.DimensionBoundaryEdge,
                            out boundaryX,
                            out boundaryY);

                        edgeDimensionCount = AddProjectedReferenceDimension(
                            model,
                            selectData,
                            pointJoint.Sharp.Point,
                            boundaryJoint.DimensionBoundaryEdge.Edge,
                            pointJoint.X,
                            pointJoint.Y,
                            boundaryX,
                            boundaryY,
                            centerX,
                            centerY,
                            DimOffsetMm);

                        Debug.WriteLine("[DIM MAT CAT] added point-to-contour projected dim. pointJoint=("
                            + MToMm(pointJoint.X).ToString("0.###")
                            + "," + MToMm(pointJoint.Y).ToString("0.###") + ")"
                            + ", boundary=" + EdgeSummary(boundaryJoint.DimensionBoundaryEdge)
                            + ", boundaryPoint=(" + MToMm(boundaryX).ToString("0.###")
                            + "," + MToMm(boundaryY).ToString("0.###") + ")");
                    }
                    else
                    {
                        edgeDimensionCount = edge.IsAngled && hasAcuteJoint
                        ? AddProjectedReferenceDimension(
                            model,
                            selectData,
                            startJoint.Sharp.Point,
                            endJoint.Sharp.Point,
                            startJoint.X,
                            startJoint.Y,
                            endJoint.X,
                            endJoint.Y,
                            centerX,
                            centerY,
                            DimOffsetMm)
                        : AddReferenceToReferenceDimension(
                            model,
                            selectData,
                            startJoint.Sharp.Point,
                            endJoint.Sharp.Point,
                            edge,
                            startJoint.X,
                            startJoint.Y,
                            endJoint.X,
                            endJoint.Y,
                            centerX,
                            centerY,
                            DimOffsetMm);
                    }
                    }
                }
                else if (edgeDimensionCount == 0)
                {
                    VirtualSharpReference oneSharp = startJoint?.Sharp ?? endJoint?.Sharp;
                    if (oneSharp != null)
                    {
                        OuterProfileJoint sharpJoint = startJoint?.Sharp != null
                            ? startJoint
                            : endJoint;
                        OuterProfileJoint otherJoint = startJoint?.Sharp != null
                            ? endJoint
                            : startJoint;

                        if (otherJoint != null && !otherJoint.UseVirtualSharp)
                        {
                            EdgeInfo perpendicularEdge = GetOtherJointEdge(otherJoint, edge);
                            if (perpendicularEdge != null)
                            {
                                edgeDimensionCount = edge.IsAngled && IsAcuteProfileJoint(sharpJoint)
                                    ? AddProjectedReferenceDimension(
                                        model,
                                        selectData,
                                        oneSharp.Point,
                                        perpendicularEdge.Edge,
                                        oneSharp.X,
                                        oneSharp.Y,
                                        otherJoint.X,
                                        otherJoint.Y,
                                        centerX,
                                        centerY,
                                        DimOffsetMm)
                                    : AddReferenceToReferenceDimension(
                                        model,
                                        selectData,
                                        oneSharp.Point,
                                        perpendicularEdge.Edge,
                                        edge,
                                        oneSharp.X,
                                        oneSharp.Y,
                                        otherJoint.X,
                                        otherJoint.Y,
                                        centerX,
                                        centerY,
                                        DimOffsetMm);
                            }
                        }
                        else if (otherJoint == null)
                        {
                            bool useProjected = IsAcuteProfileJoint(sharpJoint);

                            // In the Drawing-View workflow there is no
                            // selected seed.  Use the short thickness edge at
                            // the terminal's free contour slot as the envelope
                            // boundary.  This is deliberately attempted only
                            // for the one-sharp/one-free-end case and falls
                            // back to the previous vertex behavior below.
                            if (edgeDimensionCount == 0 && selectedContourSeed == null)
                            {
                                EdgeInfo freeBoundary;
                                double boundaryX;
                                double boundaryY;
                                if (TryFindTerminalFreeBoundaryEdge(
                                    edge,
                                    sharpJoint,
                                    edges,
                                    out freeBoundary,
                                    out boundaryX,
                                    out boundaryY))
                                {
                                    edgeDimensionCount = AddVirtualSharpToFreeBoundaryDimension(
                                        model,
                                        selectData,
                                        oneSharp,
                                        edge,
                                        freeBoundary,
                                        boundaryX,
                                        boundaryY,
                                        useProjected,
                                        centerX,
                                        centerY,
                                        DimOffsetMm);

                                    if (edgeDimensionCount > 0)
                                    {
                                        Debug.WriteLine("[DIM MAT CAT] drawing-view terminal envelope boundary used. terminal="
                                            + EdgeSummary(edge)
                                            + ", boundary=" + EdgeSummary(freeBoundary)
                                            + ", boundaryPoint=("
                                            + MToMm(boundaryX).ToString("0.###")
                                            + "," + MToMm(boundaryY).ToString("0.###") + ")");
                                    }
                                }
                            }

                            // Only the explicit selected-edge workflow may
                            // override the terminal envelope side.  When the
                            // acute-joint refinement used a parallel mate only
                            // to create the locally correct virtual sharp, the
                            // original contour edge is retained in
                            // DimensionBoundaryEdge.  Use its free endpoint so
                            // the overall dimension remains on the side chosen
                            // by the user.  View-selection and all previous
                            // workflows continue through the existing edge.
                            EdgeInfo selectedSideTerminal = selectedContourSeed != null
                                && sharpJoint.DimensionBoundaryEdge != null
                                ? sharpJoint.DimensionBoundaryEdge
                                : edge;

                            if (edgeDimensionCount == 0)
                            {
                                edgeDimensionCount = AddVirtualSharpToFreeEndDimension(
                                    model,
                                    selectData,
                                    oneSharp,
                                    selectedSideTerminal,
                                    useProjected,
                                    centerX,
                                    centerY,
                                    DimOffsetMm);
                            }

                            if (edgeDimensionCount > 0
                                && selectedSideTerminal != edge)
                            {
                                Debug.WriteLine("[DIM MAT CAT] selected-side terminal envelope used. contourSeed="
                                    + EdgeSummary(selectedContourSeed)
                                    + ", virtualEdge=" + EdgeSummary(edge)
                                    + ", envelopeEdge=" + EdgeSummary(selectedSideTerminal));
                            }

                            // Preserve the old behavior as a safe fallback if
                            // the selected-side reference cannot be dimensioned.
                            if (edgeDimensionCount == 0 && selectedSideTerminal != edge)
                            {
                                edgeDimensionCount = AddVirtualSharpToFreeEndDimension(
                                    model,
                                    selectData,
                                    oneSharp,
                                    edge,
                                    useProjected,
                                    centerX,
                                    centerY,
                                    DimOffsetMm);
                            }
                        }
                    }
                }

                if (edgeDimensionCount == 0
                    && (startJoint == null || !startJoint.UseVirtualSharp)
                    && (endJoint == null || !endJoint.UseVirtualSharp))
                {
                    DimensionPlacement placement = GetOuterPlacement(edge, centerX, centerY);
                    double offsetMm = GetOuterOffsetMm(edge, placement, centerX, centerY);
                    OuterProfileJoint terminalJoint = (startJoint == null) != (endJoint == null)
                        ? (startJoint ?? endJoint)
                        : null;
                    EdgeInfo preferredJointBoundary = terminalJoint != null
                        ? GetOtherJointEdge(terminalJoint, edge)
                        : null;
                    edgeDimensionCount = AddPairAroundEdge(
                        model,
                        selectData,
                        edge,
                        placement,
                        offsetMm,
                        edges,
                        dimensionedPairs,
                        preferredJointBoundary);
                }

                if (edgeDimensionCount == 0)
                {
                    edgeDimensionCount = AddOuterAlignedEdgeDimension(
                        model,
                        selectData,
                        edge,
                        centerX,
                        centerY,
                        DimOffsetMm,
                        dimensionedEdges);
                }

                count += edgeDimensionCount;
            }

            foreach (OuterProfileJoint joint in joints)
            {
                if (!joint.UseVirtualSharp)
                    continue;

                count += AddAngularDimension(
                    model,
                    selectData,
                    joint.First,
                    joint.Second,
                    DimOffsetMm,
                    dimensionedPairs);
            }

            int nativeSharpCount = 0;
            foreach (OuterProfileJoint joint in joints)
            {
                if (joint.Sharp != null)
                    nativeSharpCount++;
            }

            Debug.WriteLine("[DIM MAT CAT] angled outer contour dims=" + count
                + ", contourEdges=" + contourEdges.Count
                + ", joints=" + joints.Count
                + ", nativeVirtualSharps=" + nativeSharpCount);

            return count;
        }

        private List<OuterProfileJoint> BuildOuterProfileJoints(
            List<EdgeInfo> contourEdges,
            List<ArcInfo> arcs,
            EdgeInfo selectedSeed)
        {
            List<OuterProfileJoint> candidates = new List<OuterProfileJoint>();
            if (contourEdges == null)
                return candidates;

            double endpointTol = MmToViewM(20.0);

            for (int i = 0; i < contourEdges.Count; i++)
            {
                EdgeInfo first = contourEdges[i];
                if (first == null)
                    continue;

                for (int j = i + 1; j < contourEdges.Count; j++)
                {
                    EdgeInfo second = contourEdges[j];
                    if (second == null || AreParallelEdges(first, second))
                        continue;

                    double x;
                    double y;
                    if (!TryIntersectLines2D(first, second, out x, out y))
                        continue;

                    double firstStart = Distance2D(x, y, first.X1, first.Y1);
                    double firstEnd = Distance2D(x, y, first.X2, first.Y2);
                    double secondStart = Distance2D(x, y, second.X1, second.Y1);
                    double secondEnd = Distance2D(x, y, second.X2, second.Y2);

                    int firstSlot = firstStart <= firstEnd ? 0 : 1;
                    int secondSlot = secondStart <= secondEnd ? 0 : 1;
                    double firstEndpointDistance = Math.Min(firstStart, firstEnd);
                    double secondEndpointDistance = Math.Min(secondStart, secondEnd);

                    if (firstEndpointDistance > endpointTol || secondEndpointDistance > endpointTol)
                        continue;

                    candidates.Add(new OuterProfileJoint
                    {
                        First = first,
                        Second = second,
                        FirstSlot = firstSlot,
                        SecondSlot = secondSlot,
                        X = x,
                        Y = y,
                        Score = firstEndpointDistance + secondEndpointDistance,
                        HasArcSupport = HasSupportingFilletArc(first, second, arcs)
                    });
                }
            }

            // A real bend arc is the strongest evidence that two straight
            // edges belong to the same physical contour.  Prefer it before
            // endpoint distance so a close inner edge cannot steal the slot.
            candidates.Sort((a, b) =>
            {
                int arcCompare = b.HasArcSupport.CompareTo(a.HasArcSupport);
                return arcCompare != 0 ? arcCompare : a.Score.CompareTo(b.Score);
            });

            List<OuterProfileJoint> result = new List<OuterProfileJoint>();
            HashSet<string> usedSlots = new HashSet<string>();
            foreach (OuterProfileJoint candidate in candidates)
            {
                string firstKey = EdgeGeometryKey(candidate.First) + "#" + candidate.FirstSlot;
                string secondKey = EdgeGeometryKey(candidate.Second) + "#" + candidate.SecondSlot;
                if (usedSlots.Contains(firstKey) || usedSlots.Contains(secondKey))
                    continue;

                usedSlots.Add(firstKey);
                usedSlots.Add(secondKey);
                result.Add(candidate);

                Debug.WriteLine("[DIM MAT CAT] outer contour joint first="
                    + EdgeSummary(candidate.First) + "[" + candidate.FirstSlot + "]"
                    + ", second=" + EdgeSummary(candidate.Second) + "[" + candidate.SecondSlot + "]"
                    + ", point=(" + MToMm(candidate.X).ToString("0.###")
                    + "," + MToMm(candidate.Y).ToString("0.###") + ")"
                    + ", arcSupport=" + candidate.HasArcSupport
                    + ", scoreMm=" + (candidate.Score * 1000.0 / viewScale).ToString("0.###"));
            }

            return KeepContinuousContourComponent(
                result,
                contourEdges,
                selectedSeed);
        }

        private bool HasSupportingFilletArc(
            EdgeInfo first,
            EdgeInfo second,
            List<ArcInfo> arcs)
        {
            if (first == null || second == null || arcs == null || arcs.Count == 0)
                return false;

            double endpointTol = MmToViewM(1.5);
            foreach (ArcInfo arc in arcs)
            {
                if (arc == null)
                    continue;

                double startToFirst = DistancePointToSegment(
                    arc.StartX, arc.StartY,
                    first.X1, first.Y1, first.X2, first.Y2);
                double endToFirst = DistancePointToSegment(
                    arc.EndX, arc.EndY,
                    first.X1, first.Y1, first.X2, first.Y2);
                double startToSecond = DistancePointToSegment(
                    arc.StartX, arc.StartY,
                    second.X1, second.Y1, second.X2, second.Y2);
                double endToSecond = DistancePointToSegment(
                    arc.EndX, arc.EndY,
                    second.X1, second.Y1, second.X2, second.Y2);

                bool forward = startToFirst <= endpointTol
                    && endToSecond <= endpointTol;
                bool reverse = endToFirst <= endpointTol
                    && startToSecond <= endpointTol;
                if (forward || reverse)
                    return true;
            }

            return false;
        }

        private List<OuterProfileJoint> KeepContinuousContourComponent(
            List<OuterProfileJoint> joints,
            List<EdgeInfo> contourEdges,
            EdgeInfo selectedSeed)
        {
            if (joints == null || joints.Count == 0 || contourEdges == null)
                return joints ?? new List<OuterProfileJoint>();

            List<List<EdgeInfo>> components = new List<List<EdgeInfo>>();
            HashSet<EdgeInfo> visited = new HashSet<EdgeInfo>();
            foreach (OuterProfileJoint joint in joints)
            {
                foreach (EdgeInfo start in new[] { joint.First, joint.Second })
                {
                    if (start == null || visited.Contains(start))
                        continue;

                    List<EdgeInfo> component = new List<EdgeInfo>();
                    Queue<EdgeInfo> queue = new Queue<EdgeInfo>();
                    queue.Enqueue(start);
                    visited.Add(start);

                    while (queue.Count > 0)
                    {
                        EdgeInfo current = queue.Dequeue();
                        component.Add(current);
                        foreach (OuterProfileJoint connection in joints)
                        {
                            EdgeInfo next = null;
                            if (connection.First == current)
                                next = connection.Second;
                            else if (connection.Second == current)
                                next = connection.First;

                            if (next != null && visited.Add(next))
                                queue.Enqueue(next);
                        }
                    }

                    components.Add(component);
                }
            }

            if (components.Count <= 1)
                return joints;

            double centerX;
            double centerY;
            GetEdgeBoundsCenter(contourEdges, out centerX, out centerY);

            List<EdgeInfo> chosen = null;
            if (selectedSeed != null)
            {
                foreach (List<EdgeInfo> component in components)
                {
                    if (component.Contains(selectedSeed))
                    {
                        chosen = component;
                        break;
                    }
                }
            }

            if (chosen == null)
            {
                double bestScore = double.MinValue;
                foreach (List<EdgeInfo> component in components)
                {
                    double totalLengthMm = 0.0;
                    double totalOuterScore = 0.0;
                    foreach (EdgeInfo edge in component)
                    {
                        totalLengthMm += edge.LengthMm;
                        totalOuterScore += OuterScore(edge, centerX, centerY);
                    }

                    // Edge count is the primary criterion. Length and the
                    // envelope score only resolve ties between two surfaces.
                    double componentScore = component.Count * 1000000.0
                        + totalLengthMm * 100.0
                        + totalOuterScore;
                    if (componentScore > bestScore)
                    {
                        bestScore = componentScore;
                        chosen = component;
                    }
                }
            }

            if (chosen == null)
                return joints;

            HashSet<EdgeInfo> chosenSet = new HashSet<EdgeInfo>(chosen);
            contourEdges.RemoveAll(edge => !chosenSet.Contains(edge));
            List<OuterProfileJoint> filtered = joints.FindAll(joint =>
                chosenSet.Contains(joint.First) && chosenSet.Contains(joint.Second));

            Debug.WriteLine("[DIM MAT CAT] locked continuous contour component. components="
                + components.Count
                + ", selectedSeed=" + EdgeSummary(selectedSeed)
                + ", edges=" + contourEdges.Count
                + ", joints=" + filtered.Count);
            return filtered;
        }

        private OuterProfileJoint FindJointForEdgeSlot(
            List<OuterProfileJoint> joints,
            EdgeInfo edge,
            int slot)
        {
            if (joints == null || edge == null)
                return null;

            foreach (OuterProfileJoint joint in joints)
            {
                if (joint.First == edge && joint.FirstSlot == slot)
                    return joint;
                if (joint.Second == edge && joint.SecondSlot == slot)
                    return joint;
            }

            return null;
        }

        private void RefineTerminalNonOrthogonalJoints(
            List<OuterProfileJoint> joints,
            List<EdgeInfo> contourEdges,
            List<EdgeInfo> allEdges,
            EdgeInfo selectedSeed)
        {
            if (joints == null || contourEdges == null || allEdges == null)
                return;

            Dictionary<EdgeInfo, int> jointCounts = new Dictionary<EdgeInfo, int>();
            foreach (OuterProfileJoint joint in joints)
            {
                if (!jointCounts.ContainsKey(joint.First))
                    jointCounts[joint.First] = 0;
                if (!jointCounts.ContainsKey(joint.Second))
                    jointCounts[joint.Second] = 0;
                jointCounts[joint.First]++;
                jointCounts[joint.Second]++;
            }

            double thicknessMm = EstimateMaterialThicknessMm(allEdges);
            double profileCenterX;
            double profileCenterY;
            GetEdgeBoundsCenter(allEdges, out profileCenterX, out profileCenterY);
            foreach (OuterProfileJoint joint in joints)
            {
                if (!ShouldAddProfileAngle(joint.First, joint.Second))
                    continue;

                // For an inward/obtuse return, the geometrically nearest
                // parallel mate is the opposite (inner) contour.  Never
                // switch contour sides in that case.
                if (GetProfileJointIncludedAngleDegrees(joint) >= 90.0)
                    continue;

                EdgeInfo terminal = null;
                EdgeInfo neighbor = null;
                bool terminalIsFirst = false;
                if (jointCounts[joint.First] == 1 && jointCounts[joint.Second] > 1)
                {
                    terminal = joint.First;
                    neighbor = joint.Second;
                    terminalIsFirst = true;
                }
                else if (jointCounts[joint.Second] == 1 && jointCounts[joint.First] > 1)
                {
                    terminal = joint.Second;
                    neighbor = joint.First;
                }

                // An explicitly selected terminal edge is authoritative.
                if (terminal == null || terminal == selectedSeed)
                    continue;

                // contourEdges da duoc loc ve phia phu bi. Neu terminal hien tai
                // khong co mot canh song song nam xa tam hon thi no da la canh
                // ngoai dung. Khong doi sang canh song song ben trong chi vi diem
                // tiep tuyen cua cung bo gan hon giao diem ly thuyet. Voi bien dang
                // canh thang, virtual sharp phai la giao cua hai duong thang keo dai.
                if (!HasOuterParallelMate(
                    terminal,
                    allEdges,
                    thicknessMm,
                    profileCenterX,
                    profileCenterY))
                {
                    Debug.WriteLine("[DIM MAT CAT] keep outer terminal joint. terminal="
                        + EdgeSummary(terminal)
                        + ", neighbor=" + EdgeSummary(neighbor)
                        + ", point=(" + MToMm(joint.X).ToString("0.###")
                        + "," + MToMm(joint.Y).ToString("0.###") + ")");
                    continue;
                }

                EdgeInfo alternate = FindParallelMateAtThickness(
                    terminal, allEdges, thicknessMm);
                if (alternate == null)
                    continue;

                int alternateSlot;
                int neighborSlot;
                double x;
                double y;
                double score;
                if (!TryGetProfileJointGeometry(
                    alternate,
                    neighbor,
                    out alternateSlot,
                    out neighborSlot,
                    out x,
                    out y,
                    out score))
                    continue;

                if (score >= joint.Score - MmToViewM(0.05))
                    continue;

                int contourIndex = contourEdges.IndexOf(terminal);
                if (contourIndex >= 0)
                    contourEdges[contourIndex] = alternate;

                // Keep the originally chosen contour edge as the linear
                // dimension boundary.  The alternate is only used to obtain
                // the locally correct virtual sharp for the terminal lip.
                joint.DimensionBoundaryEdge = terminal;

                if (terminalIsFirst)
                {
                    joint.First = alternate;
                    joint.FirstSlot = alternateSlot;
                    joint.SecondSlot = neighborSlot;
                }
                else
                {
                    joint.Second = alternate;
                    joint.SecondSlot = alternateSlot;
                    joint.FirstSlot = neighborSlot;
                }
                joint.X = x;
                joint.Y = y;
                joint.Score = score;

                Debug.WriteLine("[DIM MAT CAT] refined terminal contour joint. from="
                    + EdgeSummary(terminal)
                    + ", to=" + EdgeSummary(alternate)
                    + ", neighbor=" + EdgeSummary(neighbor)
                    + ", point=(" + MToMm(x).ToString("0.###")
                    + "," + MToMm(y).ToString("0.###") + ")"
                    + ", scoreMm=" + (score * 1000.0 / viewScale).ToString("0.###"));
            }
        }

        private EdgeInfo FindParallelMateAtThickness(
            EdgeInfo edge,
            List<EdgeInfo> allEdges,
            double thicknessMm)
        {
            if (edge == null || allEdges == null || thicknessMm <= 0.0)
                return null;

            double thicknessViewM = MmToViewM(thicknessMm);
            double tolerance = MmToViewM(Math.Max(0.25, thicknessMm * 0.25));
            double minOverlap = MmToViewM(Math.Max(1.0, thicknessMm * 0.5));
            EdgeInfo best = null;
            double bestScore = double.MaxValue;

            foreach (EdgeInfo candidate in allEdges)
            {
                if (candidate == null || candidate == edge)
                    continue;

                double gap;
                double overlap;
                if (!TryGetParallelMetrics(edge, candidate, out gap, out overlap)
                    || Math.Abs(gap - thicknessViewM) > tolerance
                    || overlap < minOverlap)
                    continue;

                double score = Math.Abs(gap - thicknessViewM)
                    + MmToViewM(Math.Abs(candidate.LengthMm - edge.LengthMm));
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private bool TryGetProfileJointGeometry(
            EdgeInfo first,
            EdgeInfo second,
            out int firstSlot,
            out int secondSlot,
            out double x,
            out double y,
            out double score)
        {
            firstSlot = 0;
            secondSlot = 0;
            x = 0.0;
            y = 0.0;
            score = double.MaxValue;
            if (first == null || second == null || AreParallelEdges(first, second)
                || !TryIntersectLines2D(first, second, out x, out y))
                return false;

            double firstStart = Distance2D(x, y, first.X1, first.Y1);
            double firstEnd = Distance2D(x, y, first.X2, first.Y2);
            double secondStart = Distance2D(x, y, second.X1, second.Y1);
            double secondEnd = Distance2D(x, y, second.X2, second.Y2);
            firstSlot = firstStart <= firstEnd ? 0 : 1;
            secondSlot = secondStart <= secondEnd ? 0 : 1;
            double firstDistance = Math.Min(firstStart, firstEnd);
            double secondDistance = Math.Min(secondStart, secondEnd);
            if (firstDistance > MmToViewM(20.0) || secondDistance > MmToViewM(20.0))
                return false;

            score = firstDistance + secondDistance;
            return true;
        }

        private EdgeInfo GetOtherJointEdge(OuterProfileJoint joint, EdgeInfo edge)
        {
            if (joint == null || edge == null)
                return null;

            if (joint.First == edge)
                return joint.Second;
            if (joint.Second == edge)
                return joint.First;

            return null;
        }

        private int AddInnerTerminalDimension(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            EdgeInfo outerTerminal,
            OuterProfileJoint outerJoint,
            List<EdgeInfo> allEdges,
            double thicknessMm,
            double centerX,
            double centerY,
            double offsetMm)
        {
            if (outerTerminal == null || outerJoint == null || allEdges == null
                || thicknessMm <= 0.0)
                return 0;

            EdgeInfo outerNeighbor = GetOtherJointEdge(outerJoint, outerTerminal);
            if (outerNeighbor == null)
                return 0;

            EdgeInfo innerTerminal = FindInnerParallelMate(
                outerTerminal, allEdges, thicknessMm, centerX, centerY);
            EdgeInfo innerNeighbor = FindInnerParallelMate(
                outerNeighbor, allEdges, thicknessMm, centerX, centerY);
            if (innerTerminal == null || innerNeighbor == null)
            {
                Debug.WriteLine("[DIM MAT CAT] inner terminal references not found. terminal="
                    + EdgeSummary(outerTerminal) + ", neighbor=" + EdgeSummary(outerNeighbor));
                return 0;
            }

            VirtualSharpReference innerSharp = CreateNativeVirtualSharp(
                model,
                view,
                selectData,
                innerTerminal,
                innerNeighbor);
            if (innerSharp == null)
                return 0;

            bool useProjected = innerTerminal.IsAngled
                && IsAcuteProfileJoint(outerJoint);
            int count = AddVirtualSharpToFreeEndDimension(
                model,
                selectData,
                innerSharp,
                innerTerminal,
                useProjected,
                centerX,
                centerY,
                offsetMm);

            Debug.WriteLine("[DIM MAT CAT] inner terminal dimension created=" + (count > 0)
                + ", outer=" + EdgeSummary(outerTerminal)
                + ", inner=" + EdgeSummary(innerTerminal)
                + ", innerNeighbor=" + EdgeSummary(innerNeighbor));
            return count;
        }

        private EdgeInfo FindInnerParallelMate(
            EdgeInfo outer,
            List<EdgeInfo> allEdges,
            double thicknessMm,
            double centerX,
            double centerY)
        {
            if (outer == null || allEdges == null || thicknessMm <= 0.0)
                return null;

            double thicknessViewM = MmToViewM(thicknessMm);
            double thicknessTolM = MmToViewM(Math.Max(0.25, thicknessMm * 0.25));
            double minOverlapM = MmToViewM(Math.Max(1.0, thicknessMm * 0.5));
            double outerScore = OuterScore(outer, centerX, centerY);
            EdgeInfo best = null;
            double bestScore = double.MaxValue;

            foreach (EdgeInfo candidate in allEdges)
            {
                if (candidate == null || candidate == outer)
                    continue;

                double gap;
                double overlap;
                if (!TryGetParallelMetrics(outer, candidate, out gap, out overlap)
                    || Math.Abs(gap - thicknessViewM) > thicknessTolM
                    || overlap < minOverlapM)
                    continue;

                double candidateOuterScore = OuterScore(candidate, centerX, centerY);
                if (candidateOuterScore >= outerScore - MmToViewM(0.05))
                    continue;

                double gapErrorMm = Math.Abs(gap - thicknessViewM) * 1000.0 / viewScale;
                double lengthErrorMm = Math.Abs(candidate.LengthMm - outer.LengthMm);
                double score = gapErrorMm + lengthErrorMm * 0.05;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private bool ShouldAddProfileAngle(EdgeInfo first, EdgeInfo second)
        {
            if (first == null || second == null || AreParallelEdges(first, second))
                return false;

            double dot = Math.Abs(first.DirX * second.DirX + first.DirY * second.DirY);
            dot = Math.Max(0.0, Math.Min(1.0, dot));
            double acuteAngleDeg = Math.Acos(dot) * 180.0 / Math.PI;

            // Preserve the exact design angle.  The tiny epsilon only absorbs
            // floating-point noise from the API; it is not a design-angle
            // tolerance and does not round 89.999 or 90.001 degrees to 90.
            const double exactRightAngleEpsilonDeg = 0.000001;
            return acuteAngleDeg >= 5.0
                && Math.Abs(90.0 - acuteAngleDeg) > exactRightAngleEpsilonDeg;
        }

        private bool IsAcuteProfileJoint(OuterProfileJoint joint)
        {
            if (joint == null || !joint.UseVirtualSharp
                || joint.First == null || joint.Second == null)
                return false;

            double includedAngleDeg = GetProfileJointIncludedAngleDegrees(joint);
            if (double.IsNaN(includedAngleDeg))
                return false;

            Debug.WriteLine("[DIM MAT CAT] joint included angle="
                + includedAngleDeg.ToString("0.###")
                + ", acute=" + (includedAngleDeg < 90.0));
            return includedAngleDeg < 90.0;
        }

        private bool IsObtuseProfileJoint(OuterProfileJoint joint)
        {
            if (joint == null || !joint.UseVirtualSharp
                || joint.First == null || joint.Second == null)
                return false;

            double includedAngleDeg = GetProfileJointIncludedAngleDegrees(joint);
            return !double.IsNaN(includedAngleDeg) && includedAngleDeg > 90.0;
        }

        private bool IsDominantProfileEdge(EdgeInfo edge, List<EdgeInfo> contourEdges)
        {
            if (edge == null || contourEdges == null || contourEdges.Count == 0)
                return false;

            double longestLengthMm = 0.0;
            foreach (EdgeInfo candidate in contourEdges)
            {
                if (candidate != null && candidate.LengthMm > longestLengthMm)
                    longestLengthMm = candidate.LengthMm;
            }

            // This special envelope rule is intentionally limited to the
            // longest web.  Terminal and intermediate edges retain the prior
            // joint-specific logic.
            return longestLengthMm > 0.0
                && edge.LengthMm >= longestLengthMm - 0.01;
        }

        private int AddPhysicalProfileEnvelopeDimension(
            ModelDoc2 model,
            SelectData selectData,
            EdgeInfo directionEdge,
            OuterProfileJoint startJoint,
            OuterProfileJoint endJoint,
            List<OuterProfileJoint> allJoints,
            double centerX,
            double centerY,
            double offsetMm)
        {
            if (model == null || directionEdge == null || allJoints == null)
                return 0;

            Vertex startVertex = directionEdge.Edge?.GetStartVertex() as Vertex;
            Vertex endVertex = directionEdge.Edge?.GetEndVertex() as Vertex;
            if (startVertex == null || endVertex == null)
                return 0;

            object startReference = startVertex;
            object endReference = endVertex;
            double startX = directionEdge.X1;
            double startY = directionEdge.Y1;
            double endX = directionEdge.X2;
            double endY = directionEdge.Y2;
            // The dominant web dimension is defined by the two virtual
            // contour intersections.  Selecting a physical edge vertex here
            // shortens the result to the straight segment (401.8 in the
            // reported case).  Use the native SketchPoint at both ends so the
            // dimension remains associative with the contour virtual sharps.
            bool startUsesSharp = startJoint?.Sharp?.Point != null;
            bool endUsesSharp = endJoint?.Sharp?.Point != null;

            if (startUsesSharp)
            {
                startReference = startJoint.Sharp.Point;
                startX = startJoint.X;
                startY = startJoint.Y;
            }
            if (endUsesSharp)
            {
                endReference = endJoint.Sharp.Point;
                endX = endJoint.X;
                endY = endJoint.Y;
            }

            double startProjection = startX * directionEdge.DirX + startY * directionEdge.DirY;
            double endProjection = endX * directionEdge.DirX + endY * directionEdge.DirY;
            if (Math.Abs(endProjection - startProjection) <= MmToViewM(1.0))
                return 0;

            double spanMm = Math.Abs(endProjection - startProjection) * 1000.0 / viewScale;
            Debug.WriteLine("[DIM MAT CAT] terminal-sharp dominant span="
                + spanMm.ToString("0.###")
                + ", startSharp=" + startUsesSharp
                + ", endSharp=" + endUsesSharp
                + ", start=(" + MToMm(startX).ToString("0.###")
                + "," + MToMm(startY).ToString("0.###") + ")"
                + ", end=(" + MToMm(endX).ToString("0.###")
                + "," + MToMm(endY).ToString("0.###") + ")");

            if (directionEdge.IsHorizontal || directionEdge.IsVertical)
            {
                return AddProjectedReferenceDimension(
                    model,
                    selectData,
                    startReference,
                    endReference,
                    startX,
                    startY,
                    endX,
                    endY,
                    centerX,
                    centerY,
                    offsetMm);
            }

            return AddReferenceToReferenceDimension(
                model,
                selectData,
                startReference,
                endReference,
                directionEdge,
                startX,
                startY,
                endX,
                endY,
                centerX,
                centerY,
                offsetMm);
        }

        private bool JointConnectsToTerminalEdge(
            OuterProfileJoint joint,
            EdgeInfo directionEdge,
            List<OuterProfileJoint> allJoints)
        {
            EdgeInfo otherEdge = GetOtherJointEdge(joint, directionEdge);
            if (otherEdge == null || allJoints == null)
                return false;

            int count = 0;
            foreach (OuterProfileJoint candidate in allJoints)
            {
                if (candidate != null
                    && (candidate.First == otherEdge || candidate.Second == otherEdge))
                    count++;
            }
            return count == 1;
        }

        private double GetProfileJointIncludedAngleDegrees(OuterProfileJoint joint)
        {
            if (joint == null || joint.First == null || joint.Second == null)
                return double.NaN;

            // Measure the included angle using rays from the virtual
            // intersection toward the physical portions of both edges.  Do
            // not use Abs(dot) here: that would make an obtuse joint look
            // identical to its acute supplementary angle.
            double firstFarX = joint.FirstSlot == 0 ? joint.First.X2 : joint.First.X1;
            double firstFarY = joint.FirstSlot == 0 ? joint.First.Y2 : joint.First.Y1;
            double secondFarX = joint.SecondSlot == 0 ? joint.Second.X2 : joint.Second.X1;
            double secondFarY = joint.SecondSlot == 0 ? joint.Second.Y2 : joint.Second.Y1;

            double firstX = firstFarX - joint.X;
            double firstY = firstFarY - joint.Y;
            double secondX = secondFarX - joint.X;
            double secondY = secondFarY - joint.Y;
            double firstLength = Math.Sqrt(firstX * firstX + firstY * firstY);
            double secondLength = Math.Sqrt(secondX * secondX + secondY * secondY);
            if (firstLength <= 1e-9 || secondLength <= 1e-9)
                return double.NaN;

            double dot = (firstX * secondX + firstY * secondY)
                / (firstLength * secondLength);
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            double includedAngleDeg = Math.Acos(dot) * 180.0 / Math.PI;
            return includedAngleDeg;
        }

        private VirtualSharpReference CreateNativeVirtualSharp(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            SelectData selectData,
            EdgeInfo first,
            EdgeInfo second)
        {
            if (model == null || view == null || selectData == null || first == null || second == null)
                return null;

            double x;
            double y;
            if (!TryIntersectLines2D(first, second, out x, out y))
                return null;

            Sketch viewSketch = null;
            try
            {
                viewSketch = view.GetSketch() as Sketch;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT] get view sketch for virtual sharp failed: " + ex.Message);
            }

            if (viewSketch == null)
                return null;

            List<SketchPoint> before = GetUserSketchPoints(viewSketch);
            SketchPoint selectedPoint = null;

            try
            {
                model.SetPickMode();
                model.ClearSelection2(true);

                if (!SelectEdge(first.Edge, false, selectData)
                    || !SelectEdge(second.Edge, true, selectData))
                    return null;

                // Native SOLIDWORKS Point command. With two lines preselected
                // this creates the same associative virtual sharp as
                // Ctrl-selecting the lines and clicking Sketch Point.
                bool commandResult = swApp.RunCommand(72, string.Empty);

                SelectionMgr selectionMgr = model.SelectionManager as SelectionMgr;
                int selectedCount = selectionMgr?.GetSelectedObjectCount2(-1) ?? 0;
                for (int index = 1; index <= selectedCount; index++)
                {
                    SketchPoint point = selectionMgr.GetSelectedObject6(index, -1) as SketchPoint;
                    if (point != null)
                    {
                        selectedPoint = point;
                        break;
                    }
                }

                model.SetPickMode();

                List<SketchPoint> after = GetUserSketchPoints(viewSketch);
                SketchPoint createdPoint = FindNewSketchPoint(before, after) ?? selectedPoint;

                Debug.WriteLine("[DIM MAT CAT] native virtual sharp command=" + commandResult
                    + ", beforePoints=" + before.Count
                    + ", afterPoints=" + after.Count
                    + ", created=" + (createdPoint != null)
                    + ", expected=(" + MToMm(x).ToString("0.###")
                    + "," + MToMm(y).ToString("0.###") + ")");

                if (createdPoint == null)
                    return null;

                return new VirtualSharpReference
                {
                    Point = createdPoint,
                    X = x,
                    Y = y,
                    First = first,
                    Second = second
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT] native virtual sharp failed: " + ex.Message);
                try
                {
                    model.SetPickMode();
                }
                catch
                {
                }
                return null;
            }
        }

        private List<SketchPoint> GetUserSketchPoints(Sketch sketch)
        {
            List<SketchPoint> result = new List<SketchPoint>();
            if (sketch == null)
                return result;

            try
            {
                Array points = sketch.GetSketchPoints2() as Array;
                if (points == null)
                    return result;

                foreach (object item in points)
                {
                    SketchPoint point = item as SketchPoint;
                    if (point != null)
                        result.Add(point);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT] get view sketch points failed: " + ex.Message);
            }

            return result;
        }

        private SketchPoint FindNewSketchPoint(List<SketchPoint> before, List<SketchPoint> after)
        {
            if (after == null || after.Count == 0)
                return null;

            if (before == null || after.Count > before.Count)
                return after[after.Count - 1];

            foreach (SketchPoint candidate in after)
            {
                bool existed = false;
                foreach (SketchPoint oldPoint in before)
                {
                    if (ReferenceEquals(candidate, oldPoint) || candidate.Equals(oldPoint))
                    {
                        existed = true;
                        break;
                    }
                }

                if (!existed)
                    return candidate;
            }

            return null;
        }

        private bool TryFindTerminalFreeBoundaryEdge(
            EdgeInfo terminal,
            OuterProfileJoint occupiedJoint,
            List<EdgeInfo> allEdges,
            out EdgeInfo boundary,
            out double boundaryX,
            out double boundaryY)
        {
            boundary = null;
            boundaryX = 0.0;
            boundaryY = 0.0;
            if (terminal == null || occupiedJoint == null || allEdges == null)
                return false;

            int occupiedSlot;
            if (occupiedJoint.First == terminal)
                occupiedSlot = occupiedJoint.FirstSlot;
            else if (occupiedJoint.Second == terminal)
                occupiedSlot = occupiedJoint.SecondSlot;
            else
                return false;

            int freeSlot = occupiedSlot == 0 ? 1 : 0;
            double freeX = freeSlot == 0 ? terminal.X1 : terminal.X2;
            double freeY = freeSlot == 0 ? terminal.Y1 : terminal.Y2;
            double thicknessMm = EstimateMaterialThicknessMm(allEdges);
            if (thicknessMm <= 0.0)
                return false;

            double endpointTol = MmToViewM(1.0);
            double lengthTolMm = Math.Max(0.35, thicknessMm * 0.25);
            double perpendicularDotTol = Math.Sin(0.5 * Math.PI / 180.0);
            double bestScore = double.MaxValue;

            foreach (EdgeInfo candidate in allEdges)
            {
                if (candidate == null || candidate == terminal)
                    continue;

                if (Math.Abs(candidate.LengthMm - thicknessMm) > lengthTolMm)
                    continue;

                double dot = Math.Abs(terminal.DirX * candidate.DirX
                    + terminal.DirY * candidate.DirY);
                if (dot > perpendicularDotTol)
                    continue;

                double x;
                double y;
                if (!TryIntersectLines2D(terminal, candidate, out x, out y))
                    continue;

                double freeDistance = Distance2D(x, y, freeX, freeY);
                double candidateExtension = DistancePointToSegment(
                    x, y,
                    candidate.X1, candidate.Y1,
                    candidate.X2, candidate.Y2);
                if (freeDistance > endpointTol || candidateExtension > endpointTol)
                    continue;

                double score = freeDistance
                    + candidateExtension
                    + MmToViewM(Math.Abs(candidate.LengthMm - thicknessMm));
                if (score < bestScore)
                {
                    bestScore = score;
                    boundary = candidate;
                    boundaryX = x;
                    boundaryY = y;
                }
            }

            return boundary != null;
        }

        private int AddVirtualSharpToFreeBoundaryDimension(
            ModelDoc2 model,
            SelectData selectData,
            VirtualSharpReference sharp,
            EdgeInfo terminal,
            EdgeInfo boundary,
            double boundaryX,
            double boundaryY,
            bool useProjected,
            double centerX,
            double centerY,
            double offsetMm)
        {
            if (sharp == null || sharp.Point == null
                || terminal == null || boundary == null || boundary.Edge == null)
                return 0;

            if (!terminal.IsAngled || useProjected)
            {
                return AddProjectedReferenceDimension(
                    model,
                    selectData,
                    sharp.Point,
                    boundary.Edge,
                    sharp.X,
                    sharp.Y,
                    boundaryX,
                    boundaryY,
                    centerX,
                    centerY,
                    offsetMm,
                    true);
            }

            return AddReferenceToReferenceDimension(
                model,
                selectData,
                sharp.Point,
                boundary.Edge,
                terminal,
                sharp.X,
                sharp.Y,
                boundaryX,
                boundaryY,
                centerX,
                centerY,
                offsetMm,
                true);
        }

        private int AddVirtualSharpToFreeEndDimension(
            ModelDoc2 model,
            SelectData selectData,
            VirtualSharpReference sharp,
            EdgeInfo edge,
            bool useProjected,
            double centerX,
            double centerY,
            double offsetMm)
        {
            if (sharp == null || sharp.Point == null || edge == null || edge.Edge == null)
                return 0;

            double d1 = Distance2D(sharp.X, sharp.Y, edge.X1, edge.Y1);
            double d2 = Distance2D(sharp.X, sharp.Y, edge.X2, edge.Y2);
            bool useStart = d1 >= d2;

            Vertex endVertex = useStart
                ? edge.Edge.GetStartVertex() as Vertex
                : edge.Edge.GetEndVertex() as Vertex;
            if (endVertex == null)
                return 0;

            double endX = useStart ? edge.X1 : edge.X2;
            double endY = useStart ? edge.Y1 : edge.Y2;

            // Keep inclined terminal flanges consistent with the envelope
            // dimensioning used between two virtual sharps.  The dominant
            // X/Y projection is the manufacturing "overall" size, not the
            // true length measured along the inclined edge.
            if (!edge.IsAngled || useProjected)
            {
                return AddProjectedReferenceDimension(
                    model,
                    selectData,
                    sharp.Point,
                    endVertex,
                    sharp.X,
                    sharp.Y,
                    endX,
                    endY,
                    centerX,
                    centerY,
                    offsetMm,
                    true);
            }

            return AddReferenceToReferenceDimension(
                model,
                selectData,
                sharp.Point,
                endVertex,
                edge,
                sharp.X,
                sharp.Y,
                endX,
                endY,
                centerX,
                centerY,
                offsetMm,
                true);
        }

        private int AddProjectedReferenceDimension(
            ModelDoc2 model,
            SelectData selectData,
            object firstReference,
            object secondReference,
            double x1,
            double y1,
            double x2,
            double y2,
            double centerX,
            double centerY,
            double offsetMm,
            bool placeTowardCenter = false)
        {
            if (model == null || firstReference == null || secondReference == null)
                return 0;

            model.SetPickMode();
            model.ClearSelection2(true);
            if (!SelectReference(firstReference, false, selectData)
                || !SelectReference(secondReference, true, selectData))
                return 0;

            double dx = Math.Abs(x2 - x1);
            double dy = Math.Abs(y2 - y1);
            double offset = MmToM(offsetMm);
            double x;
            double y;
            DisplayDimension displayDimension;

            if (dx >= dy)
            {
                x = (x1 + x2) / 2.0;
                bool placePositiveY = (y1 + y2) / 2.0 >= centerY;
                if (placeTowardCenter)
                    placePositiveY = !placePositiveY;
                y = placePositiveY
                    ? Math.Max(y1, y2) + offset
                    : Math.Min(y1, y2) - offset;
                displayDimension = model.AddHorizontalDimension2(x, y, 0) as DisplayDimension;
            }
            else
            {
                bool placePositiveX = (x1 + x2) / 2.0 >= centerX;
                if (placeTowardCenter)
                    placePositiveX = !placePositiveX;
                x = placePositiveX
                    ? Math.Max(x1, x2) + offset
                    : Math.Min(x1, x2) - offset;
                y = (y1 + y2) / 2.0;
                displayDimension = model.AddVerticalDimension2(x, y, 0) as DisplayDimension;
            }

            if (displayDimension == null)
                return 0;

            double valueMm = (dx >= dy ? dx : dy) * 1000.0 / viewScale;
            Debug.WriteLine("[DIM MAT CAT] added projected virtual-reference dim value="
                + valueMm.ToString("0.###")
                + ", direction=" + (dx >= dy ? "Horizontal" : "Vertical"));
            return 1;
        }

        private int AddReferenceToReferenceDimension(
            ModelDoc2 model,
            SelectData selectData,
            object firstReference,
            object secondReference,
            EdgeInfo directionEdge,
            double x1,
            double y1,
            double x2,
            double y2,
            double centerX,
            double centerY,
            double offsetMm,
            bool placeTowardCenter = false)
        {
            if (model == null || firstReference == null || secondReference == null || directionEdge == null)
                return 0;

            model.SetPickMode();
            model.ClearSelection2(true);
            if (!SelectReference(firstReference, false, selectData)
                || !SelectReference(secondReference, true, selectData))
                return 0;

            double outwardSign = (((x1 + x2) / 2.0 - centerX) * directionEdge.NormX
                + ((y1 + y2) / 2.0 - centerY) * directionEdge.NormY) >= 0.0
                ? 1.0
                : -1.0;
            if (placeTowardCenter)
                outwardSign = -outwardSign;
            double offset = MmToM(offsetMm);
            double x = (x1 + x2) / 2.0 + directionEdge.NormX * outwardSign * offset;
            double y = (y1 + y2) / 2.0 + directionEdge.NormY * outwardSign * offset;

            DisplayDimension displayDimension = model.AddDimension2(x, y, 0) as DisplayDimension;
            if (displayDimension == null)
                return 0;

            if (displayDimension.GetType() == (int)swDimensionType_e.swAngularDimension)
            {
                Annotation annotation = displayDimension.GetAnnotation() as Annotation;
                if (annotation != null && annotation.Select3(false, null))
                    model.EditDelete();
                return 0;
            }

            double valueMm = Distance2D(x1, y1, x2, y2) * 1000.0 / viewScale;
            Debug.WriteLine("[DIM MAT CAT] added virtual-reference dim value="
                + valueMm.ToString("0.###")
                + ", direction=" + EdgeSummary(directionEdge));
            return 1;
        }

        private bool SelectReference(object reference, bool append, SelectData selectData)
        {
            if (reference == null)
                return false;

            Entity entity = reference as Entity;
            if (entity != null)
                return entity.Select4(append, selectData);

            try
            {
                return ((dynamic)reference).Select4(append, selectData);
            }
            catch
            {
                try
                {
                    return ((dynamic)reference).Select(append);
                }
                catch
                {
                    return false;
                }
            }
        }

        private double Distance2D(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void FindAngledMatesAtReferenceEnds(
            List<EdgeInfo> contourEdges,
            EdgeInfo reference,
            out EdgeInfo positive,
            out EdgeInfo negative,
            out double positiveScore,
            out double negativeScore)
        {
            positive = null;
            negative = null;
            positiveScore = double.MaxValue;
            negativeScore = double.MaxValue;

            if (contourEdges == null || reference == null)
                return;

            double extensionTol = MmToViewM(20.0);

            foreach (EdgeInfo candidate in contourEdges)
            {
                if (candidate == null
                    || !IsMeaningfullyAngled(candidate)
                    || candidate == reference
                    || candidate.LengthMm < Math.Max(5.0, reference.LengthMm * 0.15))
                    continue;

                if (AreParallelEdges(reference, candidate))
                    continue;

                double ix;
                double iy;
                if (!TryIntersectLines2D(reference, candidate, out ix, out iy))
                    continue;

                double referenceExtension = DistancePointToSegment(
                    ix,
                    iy,
                    reference.X1,
                    reference.Y1,
                    reference.X2,
                    reference.Y2);
                double candidateExtension = DistancePointToSegment(
                    ix,
                    iy,
                    candidate.X1,
                    candidate.Y1,
                    candidate.X2,
                    candidate.Y2);

                if (referenceExtension > extensionTol || candidateExtension > extensionTol)
                    continue;

                double score = referenceExtension + candidateExtension;
                double alongReference = (ix - reference.MidX) * reference.DirX
                    + (iy - reference.MidY) * reference.DirY;

                if (alongReference >= 0.0)
                {
                    if (score < positiveScore)
                    {
                        positiveScore = score;
                        positive = candidate;
                    }
                }
                else if (score < negativeScore)
                {
                    negativeScore = score;
                    negative = candidate;
                }
            }
        }

        private int AddOuterAlignedEdgeDimension(
            ModelDoc2 model,
            SelectData selectData,
            EdgeInfo edge,
            double centerX,
            double centerY,
            double offsetMm,
            HashSet<Edge> dimensionedEdges,
            bool placeTowardCenter = false)
        {
            if (model == null || edge == null || edge.Edge == null || dimensionedEdges == null)
                return 0;

            if (dimensionedEdges.Contains(edge.Edge))
                return 0;

            model.ClearSelection2(true);
            if (!SelectEdge(edge.Edge, false, selectData))
                return 0;

            double outwardSign = ((edge.MidX - centerX) * edge.NormX
                + (edge.MidY - centerY) * edge.NormY) >= 0.0
                ? 1.0
                : -1.0;
            if (placeTowardCenter)
                outwardSign = -outwardSign;
            double offset = MmToM(offsetMm);
            double x = edge.MidX + edge.NormX * outwardSign * offset;
            double y = edge.MidY + edge.NormY * outwardSign * offset;

            DisplayDimension displayDimension = model.AddDimension2(x, y, 0) as DisplayDimension;
            if (displayDimension == null)
                return 0;

            int dimensionType = displayDimension.GetType();
            if (dimensionType == (int)swDimensionType_e.swAngularDimension)
            {
                Annotation annotation = displayDimension.GetAnnotation() as Annotation;
                if (annotation != null && annotation.Select3(false, null))
                    model.EditDelete();
                return 0;
            }

            dimensionedEdges.Add(edge.Edge);
            Debug.WriteLine("[DIM MAT CAT] added outer aligned edge dim length="
                + edge.LengthMm.ToString("0.###")
                + ", edge=" + EdgeSummary(edge)
                + ", position=(" + MToMm(x).ToString("0.###")
                + "," + MToMm(y).ToString("0.###") + ")");
            return 1;
        }

        private int AddSectionViewDimensions(ModelDoc2 model, SolidWorks.Interop.sldworks.View view, SelectData selectData, List<EdgeInfo> edges, List<ArcInfo> arcs)
        {
            int angledProfileCount = AddAngledOuterProfileDimensions(
                model,
                view,
                selectData,
                edges,
                arcs,
                null);
            if (angledProfileCount > 0)
                return angledProfileCount;

            List<EdgeInfo> contourEdges = GetOuterContourCandidateEdges(edges);
            if (contourEdges.Count == 0)
                return 0;

            List<VirtualCornerInfo> virtualCorners = BuildVirtualCorners(contourEdges, arcs);

            int count = 0;
            HashSet<string> dimensionedPairs = new HashSet<string>();
            double centerX;
            double centerY;
            GetEdgeBoundsCenter(contourEdges, out centerX, out centerY);

            contourEdges.Sort((a, b) =>
            {
                int axisCompare = b.IsHorizontal.CompareTo(a.IsHorizontal);
                if (axisCompare != 0)
                    return axisCompare;
                if (a.IsHorizontal)
                    return b.MidY.CompareTo(a.MidY);
                return a.MidX.CompareTo(b.MidX);
            });

            Debug.WriteLine("[DIM MAT CAT] native virtual sharp dimension is not available in current API path; fallback to clean edge-pair dims.");

            foreach (EdgeInfo edge in contourEdges)
            {
                if (edge.IsAngled)
                    continue;

                DimensionPlacement placement = GetOuterPlacement(edge, centerX, centerY);
                double offsetMm = GetOuterOffsetMm(edge, placement, centerX, centerY);

                count += AddPairAroundEdge(
                    model,
                    selectData,
                    edge,
                    placement,
                    offsetMm,
                    edges,
                    dimensionedPairs);
            }

            count += AddAngularDimensionsInView(
                model,
                selectData,
                contourEdges,
                dimensionedPairs);

            Debug.WriteLine("[DIM MAT CAT] outer contour candidates=" + contourEdges.Count
                + ", virtualCorners=" + virtualCorners.Count
                + ", section dims created=" + count);

            return count;
        }

        private List<VirtualCornerInfo> BuildVirtualCorners(List<EdgeInfo> contourEdges, List<ArcInfo> arcs)
        {
            List<VirtualCornerInfo> result = new List<VirtualCornerInfo>();
            if (contourEdges == null || arcs == null)
                return result;

            foreach (ArcInfo arc in arcs)
            {
                EdgeInfo startLine = FindLineNearPoint(contourEdges, arc.StartX, arc.StartY);
                EdgeInfo endLine = FindLineNearPoint(contourEdges, arc.EndX, arc.EndY);
                if (startLine == null || endLine == null || startLine == endLine)
                    continue;

                if (startLine.IsHorizontal == endLine.IsHorizontal)
                    continue;

                double x;
                double y;
                if (!TryIntersectOrthogonalLines(startLine, endLine, out x, out y))
                    continue;

                VirtualCornerInfo corner = new VirtualCornerInfo
                {
                    X = x,
                    Y = y,
                    Arc = arc,
                    First = startLine,
                    Second = endLine
                };
                result.Add(corner);

                Debug.WriteLine("[DIM MAT CAT] virtual corner arcR=" + arc.RadiusMm.ToString("0.###")
                    + ", point=(" + MToMm(x).ToString("0.###")
                    + "," + MToMm(y).ToString("0.###") + ")"
                    + ", first=" + EdgeSummary(startLine)
                    + ", second=" + EdgeSummary(endLine));
            }

            return result;
        }

        private EdgeInfo FindLineNearPoint(List<EdgeInfo> edges, double x, double y)
        {
            EdgeInfo best = null;
            double bestDist = double.MaxValue;
            double tol = MmToViewM(3.0);

            foreach (EdgeInfo edge in edges)
            {
                double dist = DistancePointToSegment(x, y, edge.X1, edge.Y1, edge.X2, edge.Y2);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = edge;
                }
            }

            return bestDist <= tol ? best : null;
        }

        private bool TryIntersectOrthogonalLines(EdgeInfo a, EdgeInfo b, out double x, out double y)
        {
            x = 0;
            y = 0;

            if (a.IsHorizontal && b.IsVertical)
            {
                x = b.MidX;
                y = a.MidY;
                return true;
            }

            if (a.IsVertical && b.IsHorizontal)
            {
                x = a.MidX;
                y = b.MidY;
                return true;
            }

            return false;
        }

        private List<EdgeInfo> GetSelectedContourCandidateEdges(
            List<EdgeInfo> edges,
            List<ArcInfo> arcs,
            EdgeInfo selectedSeed,
            bool allowTwoEdgeContour = false)
        {
            if (selectedSeed != null)
            {
                List<EdgeInfo> connectedSelection = BuildArcConnectedContourFromSeed(
                    edges, arcs, selectedSeed);
                int requiredEdgeCount = allowTwoEdgeContour ? 2 : 3;
                if (connectedSelection.Count >= requiredEdgeCount)
                {
                    Debug.WriteLine("[DIM MAT CAT] contour side=arc-connected selected seed, edges="
                        + connectedSelection.Count + ", selectedSeed=" + EdgeSummary(selectedSeed));
                    return connectedSelection;
                }

                Debug.WriteLine("[DIM MAT CAT] selected-seed arc contour incomplete; use parallel-side fallback. mapped="
                    + connectedSelection.Count + ", selectedSeed=" + EdgeSummary(selectedSeed));
            }

            List<EdgeInfo> outer = GetOuterContourCandidateEdges(edges);
            if (selectedSeed == null)
            {
                List<EdgeInfo> bestConnectedOuter = null;
                int bestOuterOverlap = -1;
                double bestOuterScore = double.MinValue;
                HashSet<string> checkedComponents = new HashSet<string>();
                double autoCenterX;
                double autoCenterY;
                GetEdgeBoundsCenter(edges, out autoCenterX, out autoCenterY);

                foreach (EdgeInfo outerSeed in outer)
                {
                    List<EdgeInfo> component = BuildArcConnectedContourFromSeed(
                        edges, arcs, outerSeed);
                    if (component.Count < 3)
                        continue;

                    List<string> keys = new List<string>();
                    foreach (EdgeInfo componentEdge in component)
                        keys.Add(EdgeGeometryKey(componentEdge));
                    keys.Sort();
                    string componentKey = string.Join("|", keys.ToArray());
                    if (!checkedComponents.Add(componentKey))
                        continue;

                    int outerOverlap = 0;
                    double outerScore = 0.0;
                    foreach (EdgeInfo componentEdge in component)
                    {
                        if (outer.Contains(componentEdge))
                            outerOverlap++;
                        outerScore += OuterScore(componentEdge, autoCenterX, autoCenterY);
                    }

                    if (outerOverlap > bestOuterOverlap
                        || (outerOverlap == bestOuterOverlap && outerScore > bestOuterScore))
                    {
                        bestConnectedOuter = component;
                        bestOuterOverlap = outerOverlap;
                        bestOuterScore = outerScore;
                    }
                }

                if (bestConnectedOuter != null)
                {
                    Debug.WriteLine("[DIM MAT CAT] contour side=auto arc-connected outer, edges="
                        + bestConnectedOuter.Count + ", outerOverlap=" + bestOuterOverlap);
                    return bestConnectedOuter;
                }
            }

            if (selectedSeed == null || outer.Contains(selectedSeed))
            {
                Debug.WriteLine("[DIM MAT CAT] contour side=outer, selectedSeed="
                    + EdgeSummary(selectedSeed));
                return outer;
            }

            double thicknessMm = EstimateMaterialThicknessMm(edges);
            double centerX;
            double centerY;
            GetEdgeBoundsCenter(edges, out centerX, out centerY);

            EdgeInfo selectedOuterMate = null;
            foreach (EdgeInfo outerEdge in outer)
            {
                EdgeInfo innerMate = FindInnerParallelMate(
                    outerEdge, edges, thicknessMm, centerX, centerY);
                if (innerMate == selectedSeed)
                {
                    selectedOuterMate = outerEdge;
                    break;
                }
            }

            if (selectedOuterMate == null)
            {
                Debug.WriteLine("[DIM MAT CAT] selected edge is not on a longitudinal contour; fallback outer. selectedSeed="
                    + EdgeSummary(selectedSeed));
                return outer;
            }

            List<EdgeInfo> selectedSide = new List<EdgeInfo>();
            foreach (EdgeInfo outerEdge in outer)
            {
                EdgeInfo innerMate = FindInnerParallelMate(
                    outerEdge, edges, thicknessMm, centerX, centerY);
                if (innerMate != null && !selectedSide.Contains(innerMate))
                    selectedSide.Add(innerMate);
            }

            if (selectedSide.Count < 3 || !selectedSide.Contains(selectedSeed))
            {
                Debug.WriteLine("[DIM MAT CAT] inner contour mapping incomplete; fallback outer. mapped="
                    + selectedSide.Count + ", selectedSeed=" + EdgeSummary(selectedSeed));
                return outer;
            }

            Debug.WriteLine("[DIM MAT CAT] contour side=selected-inner, edges="
                + selectedSide.Count + ", selectedSeed=" + EdgeSummary(selectedSeed));
            return selectedSide;
        }

        private List<EdgeInfo> BuildArcConnectedContourFromSeed(
            List<EdgeInfo> edges,
            List<ArcInfo> arcs,
            EdgeInfo selectedSeed)
        {
            List<EdgeInfo> result = new List<EdgeInfo>();
            if (edges == null || arcs == null || selectedSeed == null)
                return result;

            double thicknessMm = EstimateMaterialThicknessMm(edges);
            double minLengthMm = Math.Max(3.0, thicknessMm + 0.8);
            List<EdgeInfo> eligible = new List<EdgeInfo>();
            foreach (EdgeInfo edge in edges)
            {
                if (edge != null && edge.LengthMm >= minLengthMm)
                    eligible.Add(edge);
            }

            EdgeInfo freshSeed = eligible.Contains(selectedSeed)
                ? selectedSeed
                : FindMatchingEdgeGeometry(eligible, selectedSeed);
            if (freshSeed == null)
                return result;

            Queue<EdgeInfo> pending = new Queue<EdgeInfo>();
            HashSet<EdgeInfo> visited = new HashSet<EdgeInfo>();
            pending.Enqueue(freshSeed);
            visited.Add(freshSeed);

            while (pending.Count > 0)
            {
                EdgeInfo current = pending.Dequeue();
                result.Add(current);

                foreach (EdgeInfo candidate in eligible)
                {
                    if (candidate == null || visited.Contains(candidate)
                        || AreParallelEdges(current, candidate))
                        continue;

                    if (!HasSupportingFilletArc(current, candidate, arcs))
                        continue;

                    visited.Add(candidate);
                    pending.Enqueue(candidate);
                }
            }

            return result;
        }

        private List<EdgeInfo> GetOuterContourCandidateEdges(List<EdgeInfo> edges)
        {
            List<EdgeInfo> result = new List<EdgeInfo>();
            double thicknessMm = EstimateMaterialThicknessMm(edges);
            double minLengthMm = Math.Max(3.0, thicknessMm + 0.8);
            double centerX;
            double centerY;
            GetEdgeBoundsCenter(edges, out centerX, out centerY);

            foreach (EdgeInfo edge in edges)
            {
                if (edge.LengthMm < minLengthMm)
                    continue;

                if (HasOuterParallelMate(edge, edges, thicknessMm, centerX, centerY))
                {
                    Debug.WriteLine("[DIM MAT CAT] skip inner contour edge=" + EdgeSummary(edge));
                    continue;
                }

                result.Add(edge);
                Debug.WriteLine("[DIM MAT CAT] outer contour edge=" + EdgeSummary(edge)
                    + ", mid=(" + MToMm(edge.MidX).ToString("0.###")
                    + "," + MToMm(edge.MidY).ToString("0.###") + ")");
            }

            Debug.WriteLine("[DIM MAT CAT] outer contour minLengthMm=" + minLengthMm.ToString("0.###")
                + ", candidates=" + result.Count);
            return result;
        }

        private bool HasOuterParallelMate(EdgeInfo edge, List<EdgeInfo> edges, double thicknessMm, double centerX, double centerY)
        {
            if (thicknessMm <= 0)
                return false;

            double thicknessViewM = MmToViewM(thicknessMm);
            double thicknessTolM = MmToViewM(Math.Max(0.25, thicknessMm * 0.25));
            double minOverlapM = MmToViewM(Math.Max(1.0, thicknessMm * 0.5));

            foreach (EdgeInfo other in edges)
            {
                if (other == edge)
                    continue;

                double gap;
                double overlap;
                if (!TryGetParallelMetrics(edge, other, out gap, out overlap))
                    continue;

                if (Math.Abs(gap - thicknessViewM) > thicknessTolM)
                    continue;

                if (overlap < minOverlapM)
                    continue;

                double edgeOuterScore = OuterScore(edge, centerX, centerY);
                double otherOuterScore = OuterScore(other, centerX, centerY);
                if (otherOuterScore > edgeOuterScore + MmToViewM(0.05))
                    return true;
            }

            return false;
        }

        private DimensionPlacement GetOuterPlacement(EdgeInfo edge, double centerX, double centerY)
        {
            if (edge.IsHorizontal)
                return edge.MidY >= centerY ? DimensionPlacement.Above : DimensionPlacement.Below;

            if (edge.MidX < centerX)
                return DimensionPlacement.Left;

            if (edge.MidY < centerY)
                return DimensionPlacement.Right;

            return DimensionPlacement.Left;
        }

        private double GetOuterOffsetMm(EdgeInfo edge, DimensionPlacement placement, double centerX, double centerY)
        {
            return DimOffsetMm;
        }

        private bool FindSectionProfileEdges(
            List<EdgeInfo> edges,
            out EdgeInfo topFlange,
            out EdgeInfo bottomFlange,
            out EdgeInfo mainWeb,
            out EdgeInfo topLip,
            out EdgeInfo bottomLip)
        {
            topFlange = null;
            bottomFlange = null;
            mainWeb = null;
            topLip = null;
            bottomLip = null;

            if (edges == null || edges.Count == 0)
                return false;

            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;
            foreach (EdgeInfo edge in edges)
            {
                minX = Math.Min(minX, edge.MinX);
                maxX = Math.Max(maxX, edge.MaxX);
                minY = Math.Min(minY, edge.MinY);
                maxY = Math.Max(maxY, edge.MaxY);
            }

            double centerX = (minX + maxX) / 2.0;
            double centerY = (minY + maxY) / 2.0;

            foreach (EdgeInfo edge in edges)
            {
                if (edge.IsHorizontal && edge.LengthMm >= 5.0)
                {
                    if (edge.MidY >= centerY)
                        topFlange = PickLonger(topFlange, edge);
                    else
                        bottomFlange = PickLonger(bottomFlange, edge);
                }

                if (edge.IsVertical && edge.LengthMm >= 5.0)
                    mainWeb = PickLonger(mainWeb, edge);
            }

            foreach (EdgeInfo edge in edges)
            {
                if (!edge.IsVertical || edge.LengthMm < 5.0 || (mainWeb != null && IsSameEdgeGeometry(edge, mainWeb)))
                    continue;

                bool isRightSide = edge.MidX >= centerX;
                if (!isRightSide)
                    continue;

                if (edge.MidY >= centerY)
                    topLip = PickLonger(topLip, edge);
                else
                    bottomLip = PickLonger(bottomLip, edge);
            }

            Debug.WriteLine("[DIM MAT CAT] section picks topFlange=" + EdgeSummary(topFlange)
                + ", topLip=" + EdgeSummary(topLip)
                + ", mainWeb=" + EdgeSummary(mainWeb)
                + ", bottomFlange=" + EdgeSummary(bottomFlange)
                + ", bottomLip=" + EdgeSummary(bottomLip));

            return topFlange != null || bottomFlange != null || mainWeb != null || topLip != null || bottomLip != null;
        }

        private EdgeInfo PickLonger(EdgeInfo current, EdgeInfo candidate)
        {
            if (candidate == null)
                return current;
            if (current == null || candidate.LengthMm > current.LengthMm)
                return candidate;
            return current;
        }

        private string EdgeSummary(EdgeInfo edge)
        {
            if (edge == null)
                return "null";

            string type = edge.IsHorizontal ? "H" : (edge.IsVertical ? "V" : "A");
            return type + ":" + edge.LengthMm.ToString("0.###");
        }

        private int AddSeededSectionDimensions(ModelDoc2 model, SelectData selectData, List<EdgeInfo> edges, EdgeInfo seed)
        {
            EdgeInfo topFlange;
            EdgeInfo bottomFlange;
            EdgeInfo mainWeb;
            EdgeInfo topLip;
            EdgeInfo bottomLip;

            if (seed == null || !FindSectionProfileEdges(edges, out topFlange, out bottomFlange, out mainWeb, out topLip, out bottomLip))
                return 0;

            double centerX;
            double centerY;
            GetEdgeBoundsCenter(edges, out centerX, out centerY);
            ApplySeedToProfile(seed, centerX, centerY, ref topFlange, ref bottomFlange, ref mainWeb, ref topLip, ref bottomLip);

            DimensionPlacement topFlangePlacement;
            DimensionPlacement topLipPlacement;
            DimensionPlacement mainWebPlacement;
            DimensionPlacement bottomFlangePlacement;
            DimensionPlacement bottomLipPlacement;
            GetPlacementsFromSeed(
                seed,
                centerX,
                centerY,
                out topFlangePlacement,
                out topLipPlacement,
                out mainWebPlacement,
                out bottomFlangePlacement,
                out bottomLipPlacement);

            int count = 0;
            HashSet<string> dimensionedPairs = new HashSet<string>();

            count += AddPairAroundEdge(model, selectData, topFlange, topFlangePlacement, DimOffsetMm, edges, dimensionedPairs);
            count += AddPairAroundEdge(model, selectData, topLip, topLipPlacement, DimOffsetMm, edges, dimensionedPairs);
            count += AddPairAroundEdge(model, selectData, mainWeb, mainWebPlacement, DimOffsetMm, edges, dimensionedPairs);
            count += AddPairAroundEdge(model, selectData, bottomFlange, bottomFlangePlacement, DimOffsetMm, edges, dimensionedPairs);
            count += AddPairAroundEdge(model, selectData, bottomLip, bottomLipPlacement, DimOffsetMm, edges, dimensionedPairs);

            Debug.WriteLine("[DIM MAT CAT] seeded section dims created=" + count
                + ", seed=" + EdgeSummary(seed)
                + ", placements="
                + topFlangePlacement + "/"
                + topLipPlacement + "/"
                + mainWebPlacement + "/"
                + bottomFlangePlacement + "/"
                + bottomLipPlacement);
            return count;
        }

        private void GetPlacementsFromSeed(
            EdgeInfo seed,
            double centerX,
            double centerY,
            out DimensionPlacement topFlangePlacement,
            out DimensionPlacement topLipPlacement,
            out DimensionPlacement mainWebPlacement,
            out DimensionPlacement bottomFlangePlacement,
            out DimensionPlacement bottomLipPlacement)
        {
            topFlangePlacement = DimensionPlacement.Above;
            topLipPlacement = DimensionPlacement.Left;
            mainWebPlacement = DimensionPlacement.Left;
            bottomFlangePlacement = DimensionPlacement.Below;
            bottomLipPlacement = DimensionPlacement.Right;

            if (seed == null)
                return;

            if (seed.IsVertical)
            {
                DimensionPlacement verticalPlacement = seed.MidX < centerX
                    ? DimensionPlacement.Left
                    : DimensionPlacement.Right;

                topLipPlacement = verticalPlacement;
                mainWebPlacement = verticalPlacement;
                bottomLipPlacement = verticalPlacement;
                return;
            }

            if (seed.IsHorizontal)
            {
                DimensionPlacement horizontalPlacement = seed.MidY >= centerY
                    ? DimensionPlacement.Above
                    : DimensionPlacement.Below;

                topFlangePlacement = horizontalPlacement;
                bottomFlangePlacement = horizontalPlacement;
            }
        }

        private void ApplySeedToProfile(
            EdgeInfo seed,
            double centerX,
            double centerY,
            ref EdgeInfo topFlange,
            ref EdgeInfo bottomFlange,
            ref EdgeInfo mainWeb,
            ref EdgeInfo topLip,
            ref EdgeInfo bottomLip)
        {
            if (seed.IsHorizontal)
            {
                if (seed.MidY >= centerY)
                    topFlange = seed;
                else
                    bottomFlange = seed;
                return;
            }

            if (!seed.IsVertical)
                return;

            if (seed.MidX < centerX)
            {
                mainWeb = seed;
                return;
            }

            if (seed.MidY >= centerY)
                topLip = seed;
            else
                bottomLip = seed;
        }

        private int AddPairAroundEdge(
            ModelDoc2 model,
            SelectData selectData,
            EdgeInfo reference,
            DimensionPlacement placement,
            double offsetMm,
            List<EdgeInfo> edges,
            HashSet<string> dimensionedPairs,
            EdgeInfo preferredJointBoundary = null)
        {
            if (reference == null || edges == null || edges.Count == 0)
                return 0;

            EdgeInfo first = null;
            EdgeInfo second = null;

            bool found = false;

            // A terminal 90-degree contour joint is authoritative regardless
            // of how the reference edge is oriented on the drawing sheet.
            // Anchor that occupied end first, then let the general local-axis
            // search find the thickness edge at the opposite/free endpoint.
            // If this strict anchored search fails, preserve the old H/V path
            // below as an unchanged fallback.
            if (preferredJointBoundary != null)
            {
                found = TryFindPerpendicularPairForAngledEdge(
                    reference,
                    edges,
                    preferredJointBoundary,
                    out first,
                    out second);

                if (found)
                {
                    Debug.WriteLine("[DIM MAT CAT] terminal 90-degree pair locked by contour slot. reference="
                        + EdgeSummary(reference)
                        + ", jointBoundary=" + EdgeSummary(preferredJointBoundary)
                        + ", freeBoundary="
                        + EdgeSummary(first == preferredJointBoundary ? second : first));
                }
            }

            if (!found && reference.IsHorizontal)
            {
                found = TryFindOuterVerticalPairForHorizontal(
                    reference, edges, out first, out second);
            }
            else if (!found && reference.IsVertical)
            {
                found = TryFindOuterHorizontalPairForVertical(
                    reference, edges, out first, out second);
            }
            else if (!found)
            {
                found = TryFindPerpendicularPairForAngledEdge(
                    reference,
                    edges,
                    preferredJointBoundary,
                    out first,
                    out second);
            }

            if (!found)
            {
                Debug.WriteLine("[DIM MAT CAT] pair not found for reference=" + EdgeSummary(reference));
                return 0;
            }

            return AddParallelEdgeDistanceDimension(
                model,
                selectData,
                first,
                second,
                placement,
                offsetMm,
                dimensionedPairs);
        }

        private bool TryFindPerpendicularPairForAngledEdge(
            EdgeInfo reference,
            List<EdgeInfo> edges,
            EdgeInfo preferredJointBoundary,
            out EdgeInfo negativeEnd,
            out EdgeInfo positiveEnd)
        {
            negativeEnd = null;
            positiveEnd = null;
            if (reference == null || edges == null)
                return false;

            double bestNegativeScore = double.MaxValue;
            double bestPositiveScore = double.MaxValue;
            double joinTol = MmToViewM(5.0);
            double perpendicularDotTol = Math.Sin(5.0 * Math.PI / 180.0);

            if (preferredJointBoundary != null && preferredJointBoundary != reference)
            {
                double preferredDot = Math.Abs(reference.DirX * preferredJointBoundary.DirX
                    + reference.DirY * preferredJointBoundary.DirY);
                double preferredX;
                double preferredY;
                if (preferredDot <= perpendicularDotTol
                    && TryIntersectLines2D(reference, preferredJointBoundary, out preferredX, out preferredY))
                {
                    double preferredProjection = (preferredX - reference.MidX) * reference.DirX
                        + (preferredY - reference.MidY) * reference.DirY;
                    if (preferredProjection < 0.0)
                    {
                        negativeEnd = preferredJointBoundary;
                        bestNegativeScore = -1.0;
                    }
                    else
                    {
                        positiveEnd = preferredJointBoundary;
                        bestPositiveScore = -1.0;
                    }

                    Debug.WriteLine("[DIM MAT CAT] anchored angled 90-degree boundary to contour joint. boundary="
                        + EdgeSummary(preferredJointBoundary));
                }
            }

            foreach (EdgeInfo candidate in edges)
            {
                if (candidate == null || candidate == reference
                    || candidate == preferredJointBoundary)
                    continue;

                double dot = Math.Abs(reference.DirX * candidate.DirX
                    + reference.DirY * candidate.DirY);
                if (dot > perpendicularDotTol)
                    continue;

                double x;
                double y;
                if (!TryIntersectLines2D(reference, candidate, out x, out y))
                    continue;

                double referenceExtension = DistancePointToSegment(
                    x, y, reference.X1, reference.Y1, reference.X2, reference.Y2);
                double candidateExtension = DistancePointToSegment(
                    x, y, candidate.X1, candidate.Y1, candidate.X2, candidate.Y2);
                if (referenceExtension > joinTol || candidateExtension > joinTol)
                    continue;

                double projection = (x - reference.MidX) * reference.DirX
                    + (y - reference.MidY) * reference.DirY;
                double score = referenceExtension + candidateExtension;
                if (projection < 0.0)
                {
                    if (score < bestNegativeScore)
                    {
                        bestNegativeScore = score;
                        negativeEnd = candidate;
                    }
                }
                else if (score < bestPositiveScore)
                {
                    bestPositiveScore = score;
                    positiveEnd = candidate;
                }
            }

            if (negativeEnd == null || positiveEnd == null
                || IsSameEdgeGeometry(negativeEnd, positiveEnd)
                || !AreParallelEdges(negativeEnd, positiveEnd))
                return false;

            // For an internal exact-90-degree segment, the first pair found
            // can lie on the two inner faces.  Keep the old edge-edge dim, but
            // move each boundary to its thickness mate only when that produces
            // a larger physical span along the reference edge.  A terminal
            // joint with an explicit contour boundary stays locked to the
            // previous branch above.
            if (preferredJointBoundary == null)
            {
                ExpandPerpendicularPairToPhysicalCoverage(
                    reference,
                    edges,
                    ref negativeEnd,
                    ref positiveEnd);
            }

            double valueMm = Math.Abs(
                (positiveEnd.MidX - negativeEnd.MidX) * negativeEnd.NormX
                + (positiveEnd.MidY - negativeEnd.MidY) * negativeEnd.NormY)
                * 1000.0 / viewScale;
            Debug.WriteLine("[DIM MAT CAT] angled 90-degree legacy pair found. reference="
                + EdgeSummary(reference)
                + ", first=" + EdgeSummary(negativeEnd)
                + ", second=" + EdgeSummary(positiveEnd)
                + ", value=" + valueMm.ToString("0.###"));
            return true;
        }

        private void ExpandPerpendicularPairToPhysicalCoverage(
            EdgeInfo reference,
            List<EdgeInfo> allEdges,
            ref EdgeInfo negativeEnd,
            ref EdgeInfo positiveEnd)
        {
            if (reference == null || allEdges == null
                || negativeEnd == null || positiveEnd == null)
                return;

            double thicknessMm = EstimateMaterialThicknessMm(allEdges);
            if (thicknessMm <= 0.0)
                return;

            EdgeInfo negativeMate = FindParallelMateAtThickness(
                negativeEnd, allEdges, thicknessMm);
            EdgeInfo positiveMate = FindParallelMateAtThickness(
                positiveEnd, allEdges, thicknessMm);

            List<EdgeInfo> negativeCandidates = new List<EdgeInfo> { negativeEnd };
            List<EdgeInfo> positiveCandidates = new List<EdgeInfo> { positiveEnd };
            if (negativeMate != null)
                negativeCandidates.Add(negativeMate);
            if (positiveMate != null)
                positiveCandidates.Add(positiveMate);

            EdgeInfo bestNegative = negativeEnd;
            EdgeInfo bestPositive = positiveEnd;
            double bestSpan = GetBoundaryPairSpanAlongReference(
                reference, negativeEnd, positiveEnd);

            foreach (EdgeInfo negativeCandidate in negativeCandidates)
            {
                foreach (EdgeInfo positiveCandidate in positiveCandidates)
                {
                    if (negativeCandidate == null || positiveCandidate == null
                        || IsSameEdgeGeometry(negativeCandidate, positiveCandidate)
                        || !AreParallelEdges(negativeCandidate, positiveCandidate))
                        continue;

                    double span = GetBoundaryPairSpanAlongReference(
                        reference, negativeCandidate, positiveCandidate);
                    if (span > bestSpan + MmToViewM(0.05))
                    {
                        bestSpan = span;
                        bestNegative = negativeCandidate;
                        bestPositive = positiveCandidate;
                    }
                }
            }

            if (!IsSameEdgeGeometry(bestNegative, negativeEnd)
                || !IsSameEdgeGeometry(bestPositive, positiveEnd))
            {
                Debug.WriteLine("[DIM MAT CAT] expanded 90-degree pair to physical coverage. reference="
                    + EdgeSummary(reference)
                    + ", first=" + EdgeSummary(bestNegative)
                    + ", second=" + EdgeSummary(bestPositive)
                    + ", spanMm=" + (bestSpan * 1000.0 / viewScale).ToString("0.###"));
                negativeEnd = bestNegative;
                positiveEnd = bestPositive;
            }
        }

        private double GetBoundaryPairSpanAlongReference(
            EdgeInfo reference,
            EdgeInfo firstBoundary,
            EdgeInfo secondBoundary)
        {
            double firstX;
            double firstY;
            double secondX;
            double secondY;
            if (reference == null || firstBoundary == null || secondBoundary == null
                || !TryIntersectLines2D(reference, firstBoundary, out firstX, out firstY)
                || !TryIntersectLines2D(reference, secondBoundary, out secondX, out secondY))
                return 0.0;

            double firstProjection = firstX * reference.DirX + firstY * reference.DirY;
            double secondProjection = secondX * reference.DirX + secondY * reference.DirY;
            return Math.Abs(secondProjection - firstProjection);
        }

        private bool TryFindOuterVerticalPairForHorizontal(
            EdgeInfo horizontal,
            List<EdgeInfo> edges,
            out EdgeInfo left,
            out EdgeInfo right)
        {
            left = null;
            right = null;

            if (horizontal == null || edges == null)
                return false;

            EdgeInfo rawLeft = null;
            EdgeInfo rawRight = null;

            double bestLeftGap = double.MaxValue;
            double bestRightGap = double.MaxValue;

            // ??y l? dung sai theo k?ch th??c model, c? scale theo Drawing View.
            double joinTol = MmToViewM(18.0);

            foreach (EdgeInfo candidate in edges)
            {
                if (candidate == null || !candidate.IsVertical)
                    continue;

                // C?nh d?c ph?i c? v? tr? t??ng quan v?i c?nh ngang.
                double yGap = DistanceToRange(horizontal.MidY, candidate.MinY, candidate.MaxY);
                if (yGap > joinTol)
                    continue;

                if (candidate.MidX <= horizontal.MidX)
                {
                    double gap = Math.Abs(candidate.MidX - horizontal.MinX);
                    if (gap < bestLeftGap)
                    {
                        bestLeftGap = gap;
                        rawLeft = candidate;
                    }
                }
                else
                {
                    double gap = Math.Abs(candidate.MidX - horizontal.MaxX);
                    if (gap < bestRightGap)
                    {
                        bestRightGap = gap;
                        rawRight = candidate;
                    }
                }
            }

            if (rawLeft == null || rawRight == null)
            {
                Debug.WriteLine("[DIM MAT CAT] raw vertical pair failed for H=" + EdgeSummary(horizontal));
                return false;
            }

            double centerX;
            double centerY;
            GetEdgeBoundsCenter(edges, out centerX, out centerY);

            left = ExpandToOuterParallelMate(rawLeft, edges, centerX, centerY);
            right = ExpandToOuterParallelMate(rawRight, edges, centerX, centerY);

            if (left == null || right == null)
                return false;

            if (left.MidX > right.MidX)
            {
                EdgeInfo temp = left;
                left = right;
                right = temp;
            }

            if (IsSameEdgeGeometry(left, right))
                return false;

            double valueMm = Math.Abs(right.MidX - left.MidX) * 1000.0 / viewScale;

            Debug.WriteLine("[DIM MAT CAT] outer V pair for H="
                + EdgeSummary(horizontal)
                + ", rawLeft=" + EdgeSummary(rawLeft)
                + ", rawRight=" + EdgeSummary(rawRight)
                + ", outerLeft=" + EdgeSummary(left)
                + ", outerRight=" + EdgeSummary(right)
                + ", value=" + valueMm.ToString("0.###"));

            return true;
        }

        private bool TryFindOuterHorizontalPairForVertical(
            EdgeInfo vertical,
            List<EdgeInfo> edges,
            out EdgeInfo bottom,
            out EdgeInfo top)
        {
            bottom = null;
            top = null;

            if (vertical == null || edges == null)
                return false;

            EdgeInfo rawBottom = null;
            EdgeInfo rawTop = null;

            double bestBottomGap = double.MaxValue;
            double bestTopGap = double.MaxValue;

            // ??y l? dung sai theo k?ch th??c model, c? scale theo Drawing View.
            double joinTol = MmToViewM(18.0);

            foreach (EdgeInfo candidate in edges)
            {
                if (candidate == null || !candidate.IsHorizontal)
                    continue;

                // C?nh ngang ph?i c? v? tr? t??ng quan v?i c?nh d?c.
                double xGap = DistanceToRange(vertical.MidX, candidate.MinX, candidate.MaxX);
                if (xGap > joinTol)
                    continue;

                if (candidate.MidY <= vertical.MidY)
                {
                    double gap = Math.Abs(candidate.MidY - vertical.MinY);
                    if (gap < bestBottomGap)
                    {
                        bestBottomGap = gap;
                        rawBottom = candidate;
                    }
                }
                else
                {
                    double gap = Math.Abs(candidate.MidY - vertical.MaxY);
                    if (gap < bestTopGap)
                    {
                        bestTopGap = gap;
                        rawTop = candidate;
                    }
                }
            }

            if (rawBottom == null || rawTop == null)
            {
                Debug.WriteLine("[DIM MAT CAT] raw horizontal pair failed for V=" + EdgeSummary(vertical));
                return false;
            }

            double centerX;
            double centerY;
            GetEdgeBoundsCenter(edges, out centerX, out centerY);

            bottom = ExpandToOuterParallelMate(rawBottom, edges, centerX, centerY);
            top = ExpandToOuterParallelMate(rawTop, edges, centerX, centerY);

            if (bottom == null || top == null)
                return false;

            if (bottom.MidY > top.MidY)
            {
                EdgeInfo temp = bottom;
                bottom = top;
                top = temp;
            }

            if (IsSameEdgeGeometry(bottom, top))
                return false;

            double valueMm = Math.Abs(top.MidY - bottom.MidY) * 1000.0 / viewScale;

            Debug.WriteLine("[DIM MAT CAT] outer H pair for V="
                + EdgeSummary(vertical)
                + ", rawBottom=" + EdgeSummary(rawBottom)
                + ", rawTop=" + EdgeSummary(rawTop)
                + ", outerBottom=" + EdgeSummary(bottom)
                + ", outerTop=" + EdgeSummary(top)
                + ", value=" + valueMm.ToString("0.###"));

            return true;
        }

        private EdgeInfo ExpandToOuterParallelMate(
            EdgeInfo edge,
            List<EdgeInfo> edges,
            double centerX,
            double centerY)
        {
            if (edge == null || edges == null || edges.Count == 0)
                return edge;

            double thicknessMm = EstimateMaterialThicknessMm(edges);

            if (thicknessMm <= 0.001)
                return edge;

            double thicknessView = MmToViewM(thicknessMm);
            double thicknessTol = MmToViewM(Math.Max(0.35, thicknessMm * 0.35));
            double minOverlap = MmToViewM(Math.Max(0.8, thicknessMm * 0.35));

            EdgeInfo outer = edge;
            double bestOuterScore = OuterScore(edge, centerX, centerY);

            foreach (EdgeInfo candidate in edges)
            {
                if (candidate == null || candidate == edge)
                    continue;

                if (edge.IsVertical && candidate.IsVertical)
                {
                    double gap = Math.Abs(candidate.MidX - edge.MidX);

                    if (Math.Abs(gap - thicknessView) > thicknessTol)
                        continue;

                    double overlap = OverlapLength(edge.MinY, edge.MaxY, candidate.MinY, candidate.MaxY);

                    if (overlap < minOverlap)
                        continue;

                    double candidateScore = OuterScore(candidate, centerX, centerY);

                    if (candidateScore > bestOuterScore + MmToViewM(0.05))
                    {
                        outer = candidate;
                        bestOuterScore = candidateScore;
                    }
                }
                else if (edge.IsHorizontal && candidate.IsHorizontal)
                {
                    double gap = Math.Abs(candidate.MidY - edge.MidY);

                    if (Math.Abs(gap - thicknessView) > thicknessTol)
                        continue;

                    double overlap = OverlapLength(edge.MinX, edge.MaxX, candidate.MinX, candidate.MaxX);

                    if (overlap < minOverlap)
                        continue;

                    double candidateScore = OuterScore(candidate, centerX, centerY);

                    if (candidateScore > bestOuterScore + MmToViewM(0.05))
                    {
                        outer = candidate;
                        bestOuterScore = candidateScore;
                    }
                }
            }

            if (!IsSameEdgeGeometry(edge, outer))
            {
                Debug.WriteLine("[DIM MAT CAT] expand inner edge to outer. from="
                    + EdgeSummary(edge)
                    + ", to="
                    + EdgeSummary(outer)
                    + ", thicknessMm="
                    + thicknessMm.ToString("0.###"));
            }

            return outer;
        }

        private double OuterScore(EdgeInfo edge, double centerX, double centerY)
        {
            if (edge == null)
                return 0.0;

            return Math.Abs(
                (edge.MidX - centerX) * edge.NormX
                + (edge.MidY - centerY) * edge.NormY);
        }

        private bool TryFindVerticalPairForHorizontal(EdgeInfo horizontal, List<EdgeInfo> edges, out EdgeInfo left, out EdgeInfo right)
        {
            left = null;
            right = null;
            double bestLeftGap = double.MaxValue;
            double bestRightGap = double.MaxValue;
            double joinTol = MmToM(12.0);

            foreach (EdgeInfo candidate in edges)
            {
                if (!candidate.IsVertical)
                    continue;

                double yGap = DistanceToRange(horizontal.MidY, candidate.MinY, candidate.MaxY);
                if (yGap > joinTol)
                    continue;

                if (candidate.MidX <= horizontal.MidX)
                {
                    double gap = Math.Abs(candidate.MidX - horizontal.MinX);
                    if (gap < bestLeftGap)
                    {
                        bestLeftGap = gap;
                        left = candidate;
                    }
                }
                else
                {
                    double gap = Math.Abs(candidate.MidX - horizontal.MaxX);
                    if (gap < bestRightGap)
                    {
                        bestRightGap = gap;
                        right = candidate;
                    }
                }
            }

            return left != null && right != null;
        }

        private bool TryFindHorizontalPairForVertical(EdgeInfo vertical, List<EdgeInfo> edges, out EdgeInfo bottom, out EdgeInfo top)
        {
            bottom = null;
            top = null;
            double bestBottomGap = double.MaxValue;
            double bestTopGap = double.MaxValue;
            double joinTol = MmToM(12.0);

            foreach (EdgeInfo candidate in edges)
            {
                if (!candidate.IsHorizontal)
                    continue;

                double xGap = DistanceToRange(vertical.MidX, candidate.MinX, candidate.MaxX);
                if (xGap > joinTol)
                    continue;

                if (candidate.MidY <= vertical.MidY)
                {
                    double gap = Math.Abs(candidate.MidY - vertical.MinY);
                    if (gap < bestBottomGap)
                    {
                        bestBottomGap = gap;
                        bottom = candidate;
                    }
                }
                else
                {
                    double gap = Math.Abs(candidate.MidY - vertical.MaxY);
                    if (gap < bestTopGap)
                    {
                        bestTopGap = gap;
                        top = candidate;
                    }
                }
            }

            return bottom != null && top != null;
        }

        private int AddParallelEdgeDistanceDimension(ModelDoc2 model, SelectData selectData, EdgeInfo first, EdgeInfo second, DimensionPlacement placement, double offsetMm, HashSet<string> dimensionedPairs)
        {
            if (first == null || second == null || first.Edge == null || second.Edge == null)
                return 0;

            string key = MakePairKey(first, second);
            if (dimensionedPairs.Contains(key))
                return 0;

            model.ClearSelection2(true);
            if (!SelectEdge(first.Edge, false, selectData) || !SelectEdge(second.Edge, true, selectData))
                return 0;

            double offset = MmToM(offsetMm);
            double x;
            double y;

            if (first.IsVertical && second.IsVertical)
            {
                x = (first.MidX + second.MidX) / 2.0;
                if (placement == DimensionPlacement.Below)
                    y = Math.Min(first.MinY, second.MinY) - offset;
                else
                    y = Math.Max(first.MaxY, second.MaxY) + offset;
            }
            else if (first.IsHorizontal && second.IsHorizontal)
            {
                y = (first.MidY + second.MidY) / 2.0;
                if (placement == DimensionPlacement.Right)
                    x = Math.Max(first.MaxX, second.MaxX) + offset;
                else
                    x = Math.Min(first.MinX, second.MinX) - offset;
            }
            else
            {
                // General 90-degree section logic for an inclined flange:
                // the two selected boundary edges are parallel but not tied
                // to the drawing X/Y axes.  Let SOLIDWORKS create their true
                // perpendicular distance and only control the text side.
                x = (first.MidX + second.MidX) / 2.0;
                y = (first.MidY + second.MidY) / 2.0;
                if (placement == DimensionPlacement.Left)
                    x = Math.Min(first.MinX, second.MinX) - offset;
                else if (placement == DimensionPlacement.Right)
                    x = Math.Max(first.MaxX, second.MaxX) + offset;
                else if (placement == DimensionPlacement.Above)
                    y = Math.Max(first.MaxY, second.MaxY) + offset;
                else
                    y = Math.Min(first.MinY, second.MinY) - offset;
            }

            DisplayDimension displayDimension = model.AddDimension2(x, y, 0) as DisplayDimension;
            if (displayDimension == null)
                return 0;

            int dimensionType = displayDimension.GetType();
            if (dimensionType == (int)swDimensionType_e.swAngularDimension)
            {
                Annotation annotation = displayDimension.GetAnnotation() as Annotation;
                if (annotation != null && annotation.Select3(false, null))
                    model.EditDelete();
                return 0;
            }

            dimensionedPairs.Add(key);
            double valueMm;
            if (first.IsVertical)
                valueMm = Math.Abs(second.MidX - first.MidX) * 1000.0 / viewScale;
            else if (first.IsHorizontal)
                valueMm = Math.Abs(second.MidY - first.MidY) * 1000.0 / viewScale;
            else
                valueMm = Math.Abs(
                    (second.MidX - first.MidX) * first.NormX
                    + (second.MidY - first.MidY) * first.NormY)
                    * 1000.0 / viewScale;
            Debug.WriteLine("[DIM MAT CAT] added pair dim value=" + valueMm.ToString("0.###")
                + ", parametric=True"
                + ", first=" + EdgeSummary(first)
                + ", second=" + EdgeSummary(second)
                + ", placement=" + placement
                + ", offsetMm=" + offsetMm.ToString("0.#"));
            return 1;
        }

        private string MakePairKey(EdgeInfo first, EdgeInfo second)
        {
            string a = EdgeGeometryKey(first);
            string b = EdgeGeometryKey(second);
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }

        private string EdgeGeometryKey(EdgeInfo edge)
        {
            return edge.X1.ToString("0.000000") + "," + edge.Y1.ToString("0.000000") + ","
                + edge.X2.ToString("0.000000") + "," + edge.Y2.ToString("0.000000");
        }

        private void GetEdgeBoundsCenter(List<EdgeInfo> edges, out double centerX, out double centerY)
        {
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            foreach (EdgeInfo edge in edges)
            {
                minX = Math.Min(minX, edge.MinX);
                maxX = Math.Max(maxX, edge.MaxX);
                minY = Math.Min(minY, edge.MinY);
                maxY = Math.Max(maxY, edge.MaxY);
            }

            centerX = (minX + maxX) / 2.0;
            centerY = (minY + maxY) / 2.0;
        }

        private int AddEdgeLengthDimension(ModelDoc2 model, SelectData selectData, EdgeInfo edge, DimensionPlacement placement, double offsetMm, HashSet<Edge> dimensioned)
        {
            if (edge == null || edge.Edge == null || dimensioned.Contains(edge.Edge))
                return 0;

            model.ClearSelection2(true);
            if (!SelectEdge(edge.Edge, false, selectData))
                return 0;

            double offset = MmToM(offsetMm);
            double x = edge.MidX;
            double y = edge.MidY;

            if (placement == DimensionPlacement.Above)
                y = edge.MaxY + offset;
            else if (placement == DimensionPlacement.Below)
                y = edge.MinY - offset;
            else if (placement == DimensionPlacement.Left)
                x = edge.MinX - offset;
            else if (placement == DimensionPlacement.Right)
                x = edge.MaxX + offset;

            DisplayDimension displayDimension = model.AddDimension2(x, y, 0) as DisplayDimension;
            if (displayDimension == null)
                return 0;

            int dimensionType = displayDimension.GetType();
            if (dimensionType == (int)swDimensionType_e.swAngularDimension)
            {
                Annotation annotation = displayDimension.GetAnnotation() as Annotation;
                if (annotation != null && annotation.Select3(false, null))
                    model.EditDelete();
                return 0;
            }

            dimensioned.Add(edge.Edge);
            Debug.WriteLine("[DIM MAT CAT] added edge dim length=" + edge.LengthMm.ToString("0.###")
                + ", placement=" + placement
                + ", offsetMm=" + offsetMm.ToString("0.#"));
            return 1;
        }

        private bool TryGetVirtualDimensionLine(EdgeInfo edge, List<EdgeInfo> edges, out double x1, out double y1, out double x2, out double y2)
        {
            x1 = edge.X1;
            y1 = edge.Y1;
            x2 = edge.X2;
            y2 = edge.Y2;

            if (edge.IsHorizontal)
            {
                double leftX;
                double rightX;
                if (!TryExtendHorizontal(edge, edges, out leftX, out rightX))
                    return false;

                x1 = leftX;
                y1 = edge.MidY;
                x2 = rightX;
                y2 = edge.MidY;
                return Math.Abs(x2 - x1) > MmToM(MinGapMm);
            }

            if (edge.IsVertical)
            {
                double bottomY;
                double topY;
                if (!TryExtendVertical(edge, edges, out bottomY, out topY))
                    return false;

                x1 = edge.MidX;
                y1 = bottomY;
                x2 = edge.MidX;
                y2 = topY;
                return Math.Abs(y2 - y1) > MmToM(MinGapMm);
            }

            return false;
        }

        private bool TryExtendHorizontal(EdgeInfo horizontal, List<EdgeInfo> edges, out double leftX, out double rightX)
        {
            leftX = horizontal.MinX;
            rightX = horizontal.MaxX;

            bool foundLeft = false;
            bool foundRight = false;
            double bestLeftGap = double.MaxValue;
            double bestRightGap = double.MaxValue;
            double joinTol = MmToM(12.0);

            foreach (EdgeInfo candidate in edges)
            {
                if (!candidate.IsVertical)
                    continue;

                double yGap = DistanceToRange(horizontal.MidY, candidate.MinY, candidate.MaxY);
                if (yGap > joinTol)
                    continue;

                if (candidate.MidX <= horizontal.MidX)
                {
                    double gap = Math.Abs(candidate.MidX - horizontal.MinX);
                    if (gap < bestLeftGap)
                    {
                        bestLeftGap = gap;
                        leftX = candidate.MidX;
                        foundLeft = true;
                    }
                }
                else
                {
                    double gap = Math.Abs(candidate.MidX - horizontal.MaxX);
                    if (gap < bestRightGap)
                    {
                        bestRightGap = gap;
                        rightX = candidate.MidX;
                        foundRight = true;
                    }
                }
            }

            return foundLeft || foundRight;
        }

        private bool TryExtendVertical(EdgeInfo vertical, List<EdgeInfo> edges, out double bottomY, out double topY)
        {
            bottomY = vertical.MinY;
            topY = vertical.MaxY;

            bool foundBottom = false;
            bool foundTop = false;
            double bestBottomGap = double.MaxValue;
            double bestTopGap = double.MaxValue;
            double joinTol = MmToM(12.0);

            foreach (EdgeInfo candidate in edges)
            {
                if (!candidate.IsHorizontal)
                    continue;

                double xGap = DistanceToRange(vertical.MidX, candidate.MinX, candidate.MaxX);
                if (xGap > joinTol)
                    continue;

                if (candidate.MidY <= vertical.MidY)
                {
                    double gap = Math.Abs(candidate.MidY - vertical.MinY);
                    if (gap < bestBottomGap)
                    {
                        bestBottomGap = gap;
                        bottomY = candidate.MidY;
                        foundBottom = true;
                    }
                }
                else
                {
                    double gap = Math.Abs(candidate.MidY - vertical.MaxY);
                    if (gap < bestTopGap)
                    {
                        bestTopGap = gap;
                        topY = candidate.MidY;
                        foundTop = true;
                    }
                }
            }

            return foundBottom || foundTop;
        }

        private double DistanceToRange(double value, double min, double max)
        {
            if (value < min)
                return min - value;
            if (value > max)
                return value - max;
            return 0.0;
        }

        private bool SelectEdge(Edge edge, bool append, SelectData selectData)
        {
            Entity entity = edge as Entity;
            if (entity != null)
                return entity.Select4(append, selectData);

            try
            {
                return ((dynamic)edge).Select(append);
            }
            catch
            {
                return false;
            }
        }

        private EdgeInfo FindNearestEdgeFromSelectionPoint(SelectionMgr selMgr, List<EdgeInfo> edges)
        {
            double distanceSheetMm;
            return FindNearestEdgeFromSelectionPoint(selMgr, edges, out distanceSheetMm);
        }

        private EdgeInfo FindNearestEdgeFromSelectionPoint(SelectionMgr selMgr, List<EdgeInfo> edges, out double distanceSheetMm)
        {
            distanceSheetMm = double.MaxValue;
            double clickX;
            double clickY;
            if (!TryGetSelectionPoint(selMgr, out clickX, out clickY))
            {
                Debug.WriteLine("[DIM MAT CAT] nearest clickPoint=null");
                return null;
            }

            EdgeInfo best = null;
            double bestDist = double.MaxValue;
            foreach (EdgeInfo edge in edges)
            {
                double dist = DistancePointToSegment(clickX, clickY, edge.X1, edge.Y1, edge.X2, edge.Y2);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = edge;
                }
            }

            double distSheetMm = bestDist * 1000.0;
            distanceSheetMm = distSheetMm;
            double distModelMm = viewScale > 0 ? distSheetMm / viewScale : distSheetMm;
            double maxSheetDistMm = Math.Max(6.0, 30.0 * viewScale);
            Debug.WriteLine("[DIM MAT CAT] nearest clickPoint=("
                + MToMm(clickX).ToString("0.###")
                + "," + MToMm(clickY).ToString("0.###")
                + "), nearestEdge=" + EdgeSummary(best)
                + ", distSheetMm=" + distSheetMm.ToString("0.###")
                + ", distModelMm=" + distModelMm.ToString("0.###")
                + ", maxSheetDistMm=" + maxSheetDistMm.ToString("0.###"));

            return best != null && distSheetMm <= maxSheetDistMm ? best : null;
        }

        private double GetReliableSelectionDistanceMm()
        {
            return Math.Max(1.0, 6.0 * Math.Max(0.1, viewScale));
        }

        private bool TryGetSelectionPoint(SelectionMgr selMgr, out double x, out double y)
        {
            x = 0;
            y = 0;

            foreach (int mark in new[] { -1, 0 })
            {
                try
                {
                    object raw = selMgr.GetSelectionPoint2(1, mark);
                    double[] point = raw as double[];
                    if (point != null && point.Length >= 2)
                    {
                        x = point[0];
                        y = point[1];
                        return true;
                    }

                    object[] values = raw as object[];
                    if (values != null && values.Length >= 2)
                    {
                        x = Convert.ToDouble(values[0]);
                        y = Convert.ToDouble(values[1]);
                        return true;
                    }
                }
                catch
                {
                }
            }

            foreach (int mark in new[] { -1, 0 })
            {
                try
                {
                    object raw = ((dynamic)selMgr).GetSelectionPointInSketchSpace2(1, mark);
                    double[] point = raw as double[];
                    if (point != null && point.Length >= 2)
                    {
                        x = point[0];
                        y = point[1];
                        return true;
                    }

                    object[] values = raw as object[];
                    if (values != null && values.Length >= 2)
                    {
                        x = Convert.ToDouble(values[0]);
                        y = Convert.ToDouble(values[1]);
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private bool IsDrawingViewSelection(SelectionMgr selMgr)
        {
            try
            {
                return selMgr.GetSelectedObjectType3(1, -1) == (int)swSelectType_e.swSelDRAWINGVIEWS;
            }
            catch
            {
                return false;
            }
        }

        private void AddUniqueEdge(List<EdgeInfo> edges, EdgeInfo edge)
        {
            foreach (EdgeInfo existing in edges)
            {
                if (IsSameEdgeGeometry(existing, edge))
                    return;
            }

            edges.Add(edge);
        }

        private void AddUniqueArc(List<ArcInfo> arcs, ArcInfo arc)
        {
            const double tol = 0.000001;

            foreach (ArcInfo existing in arcs)
            {
                bool same =
                    Math.Abs(existing.CenterX - arc.CenterX) <= tol &&
                    Math.Abs(existing.CenterY - arc.CenterY) <= tol &&
                    Math.Abs(existing.StartX - arc.StartX) <= tol &&
                    Math.Abs(existing.StartY - arc.StartY) <= tol &&
                    Math.Abs(existing.EndX - arc.EndX) <= tol &&
                    Math.Abs(existing.EndY - arc.EndY) <= tol;

                bool sameReverse =
                    Math.Abs(existing.CenterX - arc.CenterX) <= tol &&
                    Math.Abs(existing.CenterY - arc.CenterY) <= tol &&
                    Math.Abs(existing.StartX - arc.EndX) <= tol &&
                    Math.Abs(existing.StartY - arc.EndY) <= tol &&
                    Math.Abs(existing.EndX - arc.StartX) <= tol &&
                    Math.Abs(existing.EndY - arc.StartY) <= tol;

                if (same || sameReverse)
                    return;
            }

            arcs.Add(arc);
        }

        private bool IsSameEdgeGeometry(EdgeInfo a, EdgeInfo b)
        {
            const double tol = 0.000001;

            bool sameNormal =
                Math.Abs(a.X1 - b.X1) <= tol &&
                Math.Abs(a.Y1 - b.Y1) <= tol &&
                Math.Abs(a.X2 - b.X2) <= tol &&
                Math.Abs(a.Y2 - b.Y2) <= tol;

            bool sameReverse =
                Math.Abs(a.X1 - b.X2) <= tol &&
                Math.Abs(a.Y1 - b.Y2) <= tol &&
                Math.Abs(a.X2 - b.X1) <= tol &&
                Math.Abs(a.Y2 - b.Y1) <= tol;

            return sameNormal || sameReverse;
        }

        private double DistancePointToSegment(double px, double py, double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            if (Math.Abs(dx) < 1e-12 && Math.Abs(dy) < 1e-12)
            {
                double ddx = px - x1;
                double ddy = py - y1;
                return Math.Sqrt(ddx * ddx + ddy * ddy);
            }

            double t = ((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0, Math.Min(1, t));
            double cx = x1 + t * dx;
            double cy = y1 + t * dy;
            double rx = px - cx;
            double ry = py - cy;
            return Math.Sqrt(rx * rx + ry * ry);
        }

        private double OverlapLength(double a1, double a2, double b1, double b2)
        {
            double lo = Math.Max(a1, b1);
            double hi = Math.Min(a2, b2);
            return hi > lo ? hi - lo : 0.0;
        }

        private void EnableEdgeSelectionFilter()
        {
            try
            {
                // A section-view outline can otherwise win the pick before
                // the visible model edge underneath it.  Keep Drawing View
                // disabled while asking for the contour seed.
                swApp.SetSelectionFilter((int)swSelectType_e.swSelDRAWINGVIEWS, false);
                swApp.SetSelectionFilter((int)swSelectType_e.swSelEDGES, true);
                Debug.WriteLine("[DIM MAT CAT] selection filter: edges=True, drawingViews=False");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT] enable edge selection filter failed: " + ex.Message);
            }
        }

        private void DisableEdgeSelectionFilter()
        {
            try
            {
                swApp.SetSelectionFilter((int)swSelectType_e.swSelEDGES, false);
                swApp.SetSelectionFilter((int)swSelectType_e.swSelDRAWINGVIEWS, false);
            }
            catch
            {
            }
        }

        private double MmToM(double mm)
        {
            return mm / 1000.0;
        }

        private double MmToViewM(double mm)
        {
            return mm * viewScale / 1000.0;
        }

        private double MToMm(double value)
        {
            return value * 1000.0 / viewScale;
        }

        private void Msg(string text, swMessageBoxIcon_e icon)
        {
            swApp.SendMsgToUser2(text, (int)icon, (int)swMessageBoxBtn_e.swMbOk);
        }

        private string SafeViewName(SolidWorks.Interop.sldworks.View view)
        {
            try
            {
                return view?.Name ?? "";
            }
            catch
            {
                return "";
            }
        }

        private enum DimensionPlacement
        {
            Above,
            Below,
            Left,
            Right
        }

        private class EdgeInfo
        {
            public Edge Edge;

            public double X1;
            public double Y1;
            public double X2;
            public double Y2;

            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;

            public double MidX;
            public double MidY;

            public double DirX;
            public double DirY;
            public double NormX;
            public double NormY;

            public bool IsHorizontal;
            public bool IsVertical;
            public bool IsAngled;

            public double LengthMm;
        }

        private class ArcInfo
        {
            public Edge Edge;
            public double CenterX;
            public double CenterY;
            public double StartX;
            public double StartY;
            public double EndX;
            public double EndY;
            public double MidX;
            public double MidY;
            public double RadiusMm;
            public double ArcLengthMm;
            public double SweepAngleRad;
        }

        private class VirtualCornerInfo
        {
            public double X;
            public double Y;
            public ArcInfo Arc;
            public EdgeInfo First;
            public EdgeInfo Second;
        }

        private class VirtualSharpReference
        {
            public SketchPoint Point;
            public double X;
            public double Y;
            public EdgeInfo First;
            public EdgeInfo Second;
        }

        private class OuterProfileJoint
        {
            public EdgeInfo First;
            public EdgeInfo Second;
            public int FirstSlot;
            public int SecondSlot;
            public double X;
            public double Y;
            public double Score;
            public bool HasArcSupport;
            public bool UseVirtualSharp;
            public VirtualSharpReference Sharp;
            public EdgeInfo DimensionBoundaryEdge;
        }
    }
}
