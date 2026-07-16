using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public class DimKichThuocLo
    {
        private const string CenterMarkNamePrefix = "ADDIN_DIM_HOLE_CENTER_";

        private class LineInfo
        {
            public Edge Edge;
            public double StartX;
            public double StartY;
            public double EndX;
            public double EndY;
            public double MidX;
            public double MidY;
            public double Length;
            public double NormalX;
            public double NormalY;
        }

        private class HoleInfo
        {
            public Edge Edge;
            public Edge SecondEdge;
            public double CenterX;
            public double CenterY;
            public double Radius;
            public double ModelRadius;
            public bool IsSlot;
            public double SlotLength;
            public double SlotAxisX;
            public double SlotAxisY;
            public double FirstArcCenterX;
            public double FirstArcCenterY;
            public double SecondArcCenterX;
            public double SecondArcCenterY;
        }

        private class HoleGroup
        {
            public List<HoleInfo> Holes = new List<HoleInfo>();
            public HoleInfo Representative;
            public Feature SeedFeature;
            public int PatternCount;
        }

        private class PatternSeedInfo
        {
            public Feature SeedFeature;
            public int PatternCount;
            public double CenterX;
            public double CenterY;
            public double Radius;
            public bool IsSlot;
            public double SlotLength;
        }

        private class ViewBounds
        {
            public double MinX;
            public double MinY;
            public double MaxX;
            public double MaxY;
        }

        private class CalloutLengthInfo
        {
            public CalloutVariable Variable;
            public double Length;
            public string VariableName;
            public string UserName;
        }

        private class HoleLayout
        {
            public HoleGroup Group;
            public LineInfo Contour;
            public bool Above;
            public int Lane;
            public int PositionIndex;
            public double CalloutX;
            public double CalloutY;
        }

        private readonly ISldWorks swApp;

        public DimKichThuocLo(ISldWorks app)
        {
            swApp = app;
        }

        public void GenerateHoleDimensions()
        {
            ModelDoc2 model = null;
            bool undoStarted = false;

            try
            {
                model = swApp?.ActiveDoc as ModelDoc2;
                if (model == null ||
                    model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                {
                    MessageBox.Show("Chi dung trong moi truong Drawing.", "Dim kich thuoc lo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                SelectionMgr selMgr = model.SelectionManager as SelectionMgr;
                SolidWorks.Interop.sldworks.View view = GetSelectedDrawingView(selMgr);
                if (view == null)
                {
                    MessageBox.Show("Vui long chon 1 Drawing View truoc.", "Dim kich thuoc lo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                MathUtility mathUtil = swApp?.IGetMathUtility();
                MathTransform viewTransform = view.ModelToViewTransform;
                if (mathUtil == null || viewTransform == null)
                    return;

                DebugFeatureTreeFromView(view);

                model.Extension.StartRecordingUndoObject();
                undoStarted = true;

                DeleteHoleDimensionsInView(model, view);

                List<LineInfo> contours = CollectVisibleLineEdges(view, mathUtil, viewTransform);
                List<HoleInfo> holes = CollectVisibleHoles(view, mathUtil, viewTransform);
                List<HoleGroup> holeGroups = GroupPatternHoles(holes);
                List<PatternSeedInfo> patternSeeds = CollectPatternSeedInfo(view, mathUtil, viewTransform);
                ApplyPatternSeedsToGroups(holeGroups, patternSeeds);
                ViewBounds bounds = GetViewBounds(view, holes);
                Debug.WriteLine("[DIM HOLE] contours=" + contours.Count + ", holes=" + holes.Count + ", groups=" + holeGroups.Count);
                DebugHoleGroups(holeGroups);
                if (holes.Count == 0)
                {
                    MessageBox.Show("Khong tim thay lo trong drawing view da chon.", "Dim kich thuoc lo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                List<HoleLayout> layouts = BuildHoleLayouts(holeGroups, contours, bounds);
                int diameterCount = 0;
                int positionCount = 0;
                int pitchCount = 0;
                for (int groupIndex = 0; groupIndex < layouts.Count; groupIndex++)
                {
                    HoleLayout layout = layouts[groupIndex];
                    HoleGroup group = layout.Group;
                    HoleInfo hole = group.Representative;
                    if (hole == null)
                        continue;

                    if (CreateHoleDiameterDimension(model, view, hole, layout))
                        diameterCount++;

                    if (layout.Contour != null &&
                        CreateHolePositionDimension(model, view, hole, layout))
                        positionCount++;

                    if (CreatePatternPitchDimension(model, view, layout, bounds, holeGroups))
                        pitchCount++;
                }

                model.ClearSelection2(true);
                model.GraphicsRedraw2();

                MessageBox.Show(
                    "Hoan tat! Tim thay " + holes.Count + " lo, gom thanh " + holeGroups.Count + " cum lo." + System.Environment.NewLine +
                    "Da tao " + diameterCount + " kich thuoc duong kinh, " +
                    positionCount + " kich thuoc vi tri va " +
                    pitchCount + " kich thuoc buoc pattern.",
                    "Dim kich thuoc lo",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Loi khi dim kich thuoc lo:" + System.Environment.NewLine + ex.Message,
                    "Dim kich thuoc lo",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                if (undoStarted && model != null)
                    model.Extension.FinishRecordingUndoObject("Dim kich thuoc lo");
            }
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

        private void DeleteHoleDimensionsInView(ModelDoc2 drawingModel, SolidWorks.Interop.sldworks.View view)
        {
            Array annotations = view.GetAnnotations() as Array;
            if (annotations == null)
                return;

            foreach (object item in annotations)
            {
                Annotation annotation = item as Annotation;
                if (annotation == null)
                    continue;

                if (!IsManagedHoleCenterMark(annotation) && !IsHoleRelatedDimension(annotation))
                    continue;

                drawingModel.ClearSelection2(true);
                if (annotation.Select3(false, null))
                    drawingModel.EditDelete();
            }

            drawingModel.ClearSelection2(true);
        }

        private bool IsManagedHoleCenterMark(Annotation annotation)
        {
            try
            {
                string name = annotation?.GetName() ?? "";
                return name.StartsWith(CenterMarkNamePrefix, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool IsHoleRelatedDimension(Annotation annotation)
        {
            DisplayDimension displayDimension =
                annotation.GetSpecificAnnotation() as DisplayDimension;
            if (displayDimension == null)
                return HasAttachedCircularEntity(annotation) || LooksLikeHoleCalloutText(annotation);

            int dimensionType = displayDimension.GetType();
            if (dimensionType == (int)swDimensionType_e.swDiameterDimension ||
                dimensionType == (int)swDimensionType_e.swRadialDimension)
                return true;

            return HasAttachedCircularEntity(annotation);
        }

        private bool LooksLikeHoleCalloutText(Annotation annotation)
        {
            string text = GetAnnotationText(annotation);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.ToUpperInvariant();
            return text.Contains("%%C") ||
                text.Contains("<MOD-DIAM>") ||
                text.Contains("DIA") ||
                text.Contains("HOLE") ||
                text.Contains("DRILL") ||
                text.Contains(((char)0x2300).ToString());
        }

        private string GetAnnotationText(Annotation annotation)
        {
            try
            {
                Note note = annotation.GetSpecificAnnotation() as Note;
                if (note != null)
                    return note.GetText();
            }
            catch
            {
            }

            try
            {
                DisplayDimension displayDimension = annotation.GetSpecificAnnotation() as DisplayDimension;
                if (displayDimension != null)
                {
                    return (displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? "") +
                        (displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutAbove) ?? "") +
                        (displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix) ?? "");
                }
            }
            catch
            {
            }

            return string.Empty;
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

        private List<LineInfo> CollectVisibleLineEdges(
            SolidWorks.Interop.sldworks.View view,
            MathUtility mathUtil,
            MathTransform viewTransform)
        {
            List<LineInfo> lines = new List<LineInfo>();
            Array components = view.GetVisibleComponents() as Array;
            if (components == null)
                return lines;

            foreach (object item in components)
            {
                Component2 component = item as Component2;
                if (component == null)
                    continue;

                foreach (Edge edge in GetVisibleEdges(view, component))
                {
                    Curve curve = edge?.GetCurve() as Curve;
                    if (curve == null || !curve.IsLine())
                        continue;

                    LineInfo line = CreateLineInfo(edge, curve, mathUtil, viewTransform);
                    if (line != null)
                        AddUniqueLine(lines, line);
                }
            }

            return lines;
        }

        private List<HoleInfo> CollectVisibleHoles(
            SolidWorks.Interop.sldworks.View view,
            MathUtility mathUtil,
            MathTransform viewTransform)
        {
            List<HoleInfo> circles = new List<HoleInfo>();
            List<HoleInfo> arcs = new List<HoleInfo>();
            Array components = view.GetVisibleComponents() as Array;
            if (components == null)
                return circles;

            foreach (object item in components)
            {
                Component2 component = item as Component2;
                if (component == null)
                    continue;

                foreach (Edge edge in GetVisibleEdges(view, component))
                {
                    Curve curve = edge?.GetCurve() as Curve;
                    if (!IsCircularCurve(curve))
                        continue;

                    HoleInfo hole = CreateHoleInfo(edge, curve, mathUtil, viewTransform);
                    if (hole == null)
                        continue;

                    if (IsFullCircle(curve))
                        AddUniqueHole(circles, hole);
                    else
                        AddUniqueHole(arcs, hole);
                }
            }

            List<HoleInfo> result = new List<HoleInfo>();
            foreach (HoleInfo circle in circles)
                AddUniqueHole(result, circle);

            AddSlotHolesFromArcs(result, arcs);
            Debug.WriteLine("[DIM HOLE] circleEdges=" + circles.Count + ", arcEdges=" + arcs.Count + ", result=" + result.Count);
            return result;
        }

        private IEnumerable<Edge> GetVisibleEdges(SolidWorks.Interop.sldworks.View view, Component2 component)
        {
            int[] entityTypes =
            {
                (int)swViewEntityType_e.swViewEntityType_Edge,
                (int)swViewEntityType_e.swViewEntityType_SilhouetteEdge
            };

            foreach (int entityType in entityTypes)
            {
                Array edges = view.GetVisibleEntities2(component, entityType) as Array;
                if (edges == null)
                    continue;

                foreach (object edgeItem in edges)
                {
                    Edge edge = edgeItem as Edge;
                    if (edge != null)
                        yield return edge;
                }
            }
        }

        private LineInfo CreateLineInfo(
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
            if (!IsPoint(p1Model) || !IsPoint(p2Model))
                return null;

            double[] p1 = TransformPoint(mathUtil, viewTransform, p1Model);
            double[] p2 = TransformPoint(mathUtil, viewTransform, p2Model);
            if (!IsPoint(p1) || !IsPoint(p2))
                return null;

            double dx = p2[0] - p1[0];
            double dy = p2[1] - p1[1];
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.0005)
                return null;

            return new LineInfo
            {
                Edge = edge,
                StartX = p1[0],
                StartY = p1[1],
                EndX = p2[0],
                EndY = p2[1],
                MidX = (p1[0] + p2[0]) / 2.0,
                MidY = (p1[1] + p2[1]) / 2.0,
                Length = length,
                NormalX = -dy / length,
                NormalY = dx / length
            };
        }

        private HoleInfo CreateHoleInfo(
            Edge edge,
            Curve curve,
            MathUtility mathUtil,
            MathTransform viewTransform)
        {
            double[] centerModel;
            double radiusModel;
            if (!TryGetCircleData(curve, out centerModel, out radiusModel))
                return null;

            double[] center = TransformPoint(mathUtil, viewTransform, centerModel);
            if (!IsPoint(center))
                return null;

            double radiusView = EstimateRadiusInView(edge, curve, mathUtil, viewTransform, center, radiusModel);
            if (radiusModel < 0.0001 || radiusModel > 0.2)
                return null;

            if (radiusView <= 0)
                return null;

            return new HoleInfo
            {
                Edge = edge,
                CenterX = center[0],
                CenterY = center[1],
                Radius = radiusView,
                ModelRadius = radiusModel,
                IsSlot = false,
                FirstArcCenterX = center[0],
                FirstArcCenterY = center[1],
                SecondArcCenterX = center[0],
                SecondArcCenterY = center[1]
            };
        }

        private double EstimateRadiusInView(
            Edge edge,
            Curve curve,
            MathUtility mathUtil,
            MathTransform viewTransform,
            double[] center,
            double radiusModel)
        {
            double[] pModel = null;
            try
            {
                CurveParamData paramData = edge.GetCurveParams3();
                pModel = paramData?.StartPoint as double[];
            }
            catch
            {
            }

            if (!IsPoint(pModel))
            {
                double startParam;
                double endParam;
                bool isClosed;
                bool isPeriodic;
                if (curve.GetEndParams(out startParam, out endParam, out isClosed, out isPeriodic))
                    pModel = curve.Evaluate(startParam) as double[];
            }

            double[] p = TransformPoint(mathUtil, viewTransform, pModel);
            if (IsPoint(p))
            {
                double dx = p[0] - center[0];
                double dy = p[1] - center[1];
                double radius = Math.Sqrt(dx * dx + dy * dy);
                if (radius > 0)
                    return radius;
            }

            return Math.Abs(radiusModel);
        }

        private void AddSlotHolesFromArcs(List<HoleInfo> result, List<HoleInfo> arcs)
        {
            bool[] used = new bool[arcs.Count];
            for (int i = 0; i < arcs.Count; i++)
            {
                if (used[i])
                    continue;

                int pairIndex = -1;
                double bestDistance = double.MaxValue;
                for (int j = i + 1; j < arcs.Count; j++)
                {
                    if (used[j])
                        continue;

                    double radius = Math.Max(arcs[i].Radius, arcs[j].Radius);
                    double radiusDiff = Math.Abs(arcs[i].Radius - arcs[j].Radius);
                    if (radius <= 0 || radiusDiff > radius * 0.2)
                        continue;

                    double distance = Distance(arcs[i].CenterX, arcs[i].CenterY, arcs[j].CenterX, arcs[j].CenterY);
                    if (distance < radius * 1.5 || distance > radius * 20.0)
                        continue;

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        pairIndex = j;
                    }
                }

                if (pairIndex < 0)
                    continue;

                used[i] = true;
                used[pairIndex] = true;

                double axisX = arcs[pairIndex].CenterX - arcs[i].CenterX;
                double axisY = arcs[pairIndex].CenterY - arcs[i].CenterY;
                double axisLength = Math.Sqrt(axisX * axisX + axisY * axisY);
                if (axisLength > 1e-9)
                {
                    axisX /= axisLength;
                    axisY /= axisLength;
                }

                HoleInfo slot = new HoleInfo
                {
                    Edge = arcs[i].Edge,
                    SecondEdge = arcs[pairIndex].Edge,
                    CenterX = (arcs[i].CenterX + arcs[pairIndex].CenterX) / 2.0,
                    CenterY = (arcs[i].CenterY + arcs[pairIndex].CenterY) / 2.0,
                    Radius = (arcs[i].Radius + arcs[pairIndex].Radius) / 2.0,
                    ModelRadius = (arcs[i].ModelRadius + arcs[pairIndex].ModelRadius) / 2.0,
                    IsSlot = true,
                    SlotLength = bestDistance + (arcs[i].Radius + arcs[pairIndex].Radius),
                    SlotAxisX = axisX,
                    SlotAxisY = axisY,
                    FirstArcCenterX = arcs[i].CenterX,
                    FirstArcCenterY = arcs[i].CenterY,
                    SecondArcCenterX = arcs[pairIndex].CenterX,
                    SecondArcCenterY = arcs[pairIndex].CenterY
                };

                AddUniqueHole(result, slot);
            }
        }

        private List<HoleGroup> GroupPatternHoles(List<HoleInfo> holes)
        {
            List<HoleGroup> groups = new List<HoleGroup>();
            List<HoleInfo> remaining = new List<HoleInfo>();
            foreach (HoleInfo hole in holes)
            {
                if (hole != null && hole.Edge != null)
                    remaining.Add(hole);
            }

            while (remaining.Count > 0)
            {
                HoleInfo seed = remaining[0];
                List<HoleInfo> sameSize = new List<HoleInfo>();
                foreach (HoleInfo hole in remaining)
                {
                    if (AreSameHoleSize(seed, hole))
                        sameSize.Add(hole);
                }

                List<HoleInfo> bestGroup = FindBestPatternLine(seed, sameSize);
                if (bestGroup.Count == 0)
                    bestGroup.Add(seed);

                foreach (HoleInfo hole in bestGroup)
                    remaining.Remove(hole);

                HoleGroup group = new HoleGroup();
                group.Holes.AddRange(bestGroup);
                group.Representative = ChooseFeatureHole(bestGroup);
                groups.Add(group);
            }

            return groups;
        }

        private List<HoleInfo> FindBestPatternLine(HoleInfo seed, List<HoleInfo> candidates)
        {
            List<HoleInfo> row = new List<HoleInfo>();
            List<HoleInfo> column = new List<HoleInfo>();
            double tolerance = GetPatternLineTolerance(seed);

            foreach (HoleInfo hole in candidates)
            {
                if (Math.Abs(hole.CenterY - seed.CenterY) <= tolerance)
                    row.Add(hole);

                if (Math.Abs(hole.CenterX - seed.CenterX) <= tolerance)
                    column.Add(hole);
            }

            if (row.Count >= column.Count)
                return row;

            return column;
        }

        private HoleInfo ChooseFeatureHole(List<HoleInfo> holes)
        {
            if (holes == null || holes.Count == 0)
                return null;

            if (holes.Count == 1)
                return holes[0];

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            foreach (HoleInfo hole in holes)
            {
                minX = Math.Min(minX, hole.CenterX);
                minY = Math.Min(minY, hole.CenterY);
                maxX = Math.Max(maxX, hole.CenterX);
                maxY = Math.Max(maxY, hole.CenterY);
            }

            double centerX = (minX + maxX) / 2.0;
            double centerY = (minY + maxY) / 2.0;
            HoleInfo best = holes[0];
            double bestDistance = double.MaxValue;

            foreach (HoleInfo hole in holes)
            {
                double distance = Distance(hole.CenterX, hole.CenterY, centerX, centerY);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = hole;
                }
            }

            return best;
        }

        private bool AreSameHoleSize(HoleInfo a, HoleInfo b)
        {
            if (a == null || b == null)
                return false;

            if (a.IsSlot != b.IsSlot)
                return false;

            double radiusA = GetComparableRadius(a);
            double radiusB = GetComparableRadius(b);
            double radius = Math.Max(radiusA, radiusB);
            if (radius <= 0)
                return false;

            return Math.Abs(radiusA - radiusB) <= Math.Max(0.00002, radius * 0.08);
        }

        private double GetComparableRadius(HoleInfo hole)
        {
            if (hole == null)
                return 0;

            if (hole.ModelRadius > 0)
                return Math.Abs(hole.ModelRadius);

            return Math.Abs(hole.Radius);
        }

        private double GetPatternLineTolerance(HoleInfo hole)
        {
            double radius = Math.Abs(hole?.Radius ?? 0);
            return Math.Max(0.0015, radius * 2.5);
        }

        private void DebugHoleGroups(List<HoleGroup> groups)
        {
            if (groups == null)
                return;

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                HoleGroup group = groups[groupIndex];
                if (group == null || group.Holes == null)
                    continue;

                Debug.WriteLine("[DIM HOLE FEATURE] group=" + (groupIndex + 1) +
                    ", count=" + group.Holes.Count +
                    ", rep=" + DescribeHoleFeature(group.Representative));

                for (int holeIndex = 0; holeIndex < group.Holes.Count; holeIndex++)
                {
                    HoleInfo hole = group.Holes[holeIndex];
                    string role = ReferenceEquals(hole, group.Representative) ? "REP" : "PATTERN-CANDIDATE";
                    Debug.WriteLine("[DIM HOLE FEATURE]   " + role +
                        ", index=" + (holeIndex + 1) +
                        ", " + DescribeHoleFeature(hole));
                }
            }
        }

        private string DescribeHoleFeature(HoleInfo hole)
        {
            if (hole == null)
                return "hole=null";

            Feature feature = TryGetFeatureFromHole(hole);
            string featureName = SafeFeatureName(feature);
            string featureType = SafeFeatureTypeName(feature);
            bool isPattern = IsPatternFeature(feature);

            return "diaMm=" + (GetComparableRadius(hole) * 2000.0).ToString("0.###") +
                ", x=" + (hole.CenterX * 1000.0).ToString("0.###") +
                ", y=" + (hole.CenterY * 1000.0).ToString("0.###") +
                ", slot=" + hole.IsSlot +
                ", feature=" + featureName +
                ", type=" + featureType +
                ", pattern=" + isPattern;
        }

        private Feature TryGetFeatureFromHole(HoleInfo hole)
        {
            if (hole == null)
                return null;

            return TryGetFeatureFromEdge(hole.Edge);
        }

        private Feature TryGetFeatureFromEdge(Edge edge)
        {
            if (edge == null)
                return null;

            try
            {
                Feature feature = ((dynamic)edge).GetFeature() as Feature;
                if (feature != null)
                    return feature;
            }
            catch
            {
            }

            try
            {
                Entity entity = edge as Entity;
                Feature feature = ((dynamic)entity).GetFeature() as Feature;
                if (feature != null)
                    return feature;
            }
            catch
            {
            }

            try
            {
                Face2 face = ((dynamic)edge).GetFace() as Face2;
                Feature feature = ((dynamic)face).GetFeature() as Feature;
                if (feature != null)
                    return feature;
            }
            catch
            {
            }

            return null;
        }

        private bool IsPatternFeature(Feature feature)
        {
            string name = SafeFeatureName(feature);
            string type = SafeFeatureTypeName(feature);
            return name.IndexOf("Pattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Pattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string SafeFeatureName(Feature feature)
        {
            if (feature == null)
                return "null";

            try
            {
                return feature.Name ?? "";
            }
            catch
            {
                return "";
            }
        }

        private string SafeFeatureTypeName(Feature feature)
        {
            if (feature == null)
                return "null";

            try
            {
                string typeName = feature.GetTypeName2();
                if (!string.IsNullOrWhiteSpace(typeName))
                    return typeName;
            }
            catch
            {
            }

            try
            {
                return feature.GetTypeName() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private void DebugFeatureTreeFromView(SolidWorks.Interop.sldworks.View view)
        {
            try
            {
                Debug.WriteLine("[DIM HOLE TREE] begin");

                HashSet<string> loggedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Array components = view.GetVisibleComponents() as Array;
                if (components != null)
                {
                    foreach (object item in components)
                    {
                        Component2 component = item as Component2;
                        if (component == null)
                            continue;

                        ModelDoc2 componentModel = null;
                        try { componentModel = component.GetModelDoc2() as ModelDoc2; } catch { }
                        string path = SafeComponentPath(component, componentModel);
                        string config = SafeComponentConfig(component);
                        string key = path + "|" + config;
                        if (loggedModels.Contains(key))
                            continue;

                        loggedModels.Add(key);
                        Debug.WriteLine("[DIM HOLE TREE] component=" + SafeComponentName(component) +
                            ", path=" + path +
                            ", config=" + config +
                            ", modelLoaded=" + (componentModel != null));

                        DebugFeatureTree(componentModel, path, config);
                    }
                }

                if (loggedModels.Count == 0)
                {
                    ModelDoc2 referencedModel = null;
                    try { referencedModel = view.ReferencedDocument as ModelDoc2; } catch { }
                    string path = SafeModelPath(referencedModel);
                    string config = "";
                    try { config = view.ReferencedConfiguration ?? ""; } catch { }
                    Debug.WriteLine("[DIM HOLE TREE] fallback referenced model path=" + path + ", config=" + config + ", modelLoaded=" + (referencedModel != null));
                    DebugFeatureTree(referencedModel, path, config);
                }

                Debug.WriteLine("[DIM HOLE TREE] end");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM HOLE TREE] failed: " + ex.Message);
            }
        }

        private void DebugFeatureTree(ModelDoc2 model, string path, string config)
        {
            if (model == null)
            {
                Debug.WriteLine("[DIM HOLE TREE] model null. path=" + (path ?? "") + ", config=" + (config ?? ""));
                return;
            }

            int index = 0;
            int logged = 0;
            for (Feature feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
            {
                index++;
                string name = SafeFeatureName(feature);
                string type = SafeFeatureTypeName(feature);
                bool likely = IsLikelyHoleOrPatternFeature(feature);

                if (likely)
                {
                    logged++;
                    Debug.WriteLine("[DIM HOLE TREE] feature#" + index +
                        ", name=" + name +
                        ", type=" + type +
                        ", suppressed=" + IsFeatureSuppressed(feature) +
                        ", kind=" + FeatureKindText(feature) +
                        ", patternInfo=" + TryDescribePatternFeature(feature, model));
                    DebugSubFeatures(feature, index);
                }

                if (index >= 500)
                {
                    Debug.WriteLine("[DIM HOLE TREE] stop after 500 features to avoid long scan.");
                    break;
                }
            }

            Debug.WriteLine("[DIM HOLE TREE] model summary path=" + SafeModelPath(model) +
                ", config=" + (config ?? "") +
                ", featuresScanned=" + index +
                ", likelyLogged=" + logged);
        }

        private void DebugSubFeatures(Feature feature, int parentIndex)
        {
            try
            {
                int subIndex = 0;
                for (Feature subFeature = feature.GetFirstSubFeature() as Feature;
                    subFeature != null;
                    subFeature = subFeature.GetNextSubFeature() as Feature)
                {
                    subIndex++;
                    Debug.WriteLine("[DIM HOLE TREE]   sub#" + parentIndex + "." + subIndex +
                        ", name=" + SafeFeatureName(subFeature) +
                        ", type=" + SafeFeatureTypeName(subFeature) +
                        ", suppressed=" + IsFeatureSuppressed(subFeature));

                    if (subIndex >= 10)
                    {
                        Debug.WriteLine("[DIM HOLE TREE]   sub stop after 10.");
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        private bool IsLikelyHoleOrPatternFeature(Feature feature)
        {
            string name = SafeFeatureName(feature);
            string type = SafeFeatureTypeName(feature);
            return name.IndexOf("Hole", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Extrude", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Pattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Sketch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("押し出し", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("カット", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Hole", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Extrude", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Pattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Sketch", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string FeatureKindText(Feature feature)
        {
            if (feature == null)
                return "NULL";

            if (IsPatternFeature(feature))
                return "PATTERN";

            string name = SafeFeatureName(feature);
            string type = SafeFeatureTypeName(feature);
            if (name.IndexOf("Hole", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Hole", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Extrude", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Extrude", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("押し出し", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("カット", StringComparison.OrdinalIgnoreCase) >= 0)
                return "SEED-CANDIDATE";

            return "OTHER";
        }

        private bool IsFeatureSuppressed(Feature feature)
        {
            try
            {
                return feature == null || feature.IsSuppressed();
            }
            catch
            {
                return false;
            }
        }

        private string TryDescribePatternFeature(Feature feature, ModelDoc2 model)
        {
            if (!IsPatternFeature(feature))
                return "";

            try
            {
                object definition = feature.GetDefinition();
                if (definition == null)
                    return "definition=null";

                ICurveDrivenPatternFeatureData curveData = definition as ICurveDrivenPatternFeatureData;
                if (curveData != null)
                    return DescribeCurvePatternData(curveData, model);

                List<string> parts = new List<string>();
                parts.Add("defType=" + definition.GetType().Name);
                TryAddDynamicValue(parts, definition, "D1TotalInstances");
                TryAddDynamicValue(parts, definition, "D1InstanceCount");
                TryAddDynamicValue(parts, definition, "D2TotalInstances");
                TryAddDynamicValue(parts, definition, "D2InstanceCount");
                TryAddDynamicFeatureArray(parts, definition, "PatternFeatureArray");
                TryAddDynamicFeatureArray(parts, definition, "SeedFeatureArray");
                return string.Join("; ", parts.ToArray());
            }
            catch (Exception ex)
            {
                return "patternInfoError=" + ex.Message;
            }
        }

        private string DescribeCurvePatternData(ICurveDrivenPatternFeatureData data, ModelDoc2 model)
        {
            List<string> parts = new List<string>();
            parts.Add("defType=ICurveDrivenPatternFeatureData");

            bool accessGranted = false;
            try
            {
                if (model != null)
                    accessGranted = data.AccessSelections(model, null);

                parts.Add("accessSelections=" + accessGranted);
                parts.Add("D1InstanceCount=" + data.D1InstanceCount);
                parts.Add("D2InstanceCount=" + data.D2InstanceCount);
                parts.Add("featureCount=" + data.GetPatternFeatureCount());
                parts.Add("patternElement=" + data.PatternElement);
                parts.Add("geometryPattern=" + data.GeometryPattern);

                string seedText = DescribePatternFeatureArray(data.PatternFeatureArray);
                parts.Add("seedFeatures=" + seedText);
            }
            catch (Exception ex)
            {
                parts.Add("typedReadError=" + ex.Message);
            }
            finally
            {
                if (accessGranted)
                {
                    try { data.ReleaseSelectionAccess(); }
                    catch { }
                }
            }

            return string.Join("; ", parts.ToArray());
        }

        private string DescribePatternFeatureArray(object value)
        {
            if (value == null)
                return "null";

            object[] values = value as object[];
            if (values == null)
            {
                Feature singleFeature = value as Feature;
                if (singleFeature != null)
                    return "[" + SafeFeatureName(singleFeature) + "/" + SafeFeatureTypeName(singleFeature) + "]";
                return "unreadable:" + value.GetType().Name;
            }

            List<string> names = new List<string>();
            foreach (object item in values)
            {
                Feature seedFeature = item as Feature;
                if (seedFeature != null)
                    names.Add(SafeFeatureName(seedFeature) + "/" + SafeFeatureTypeName(seedFeature));
                else if (item != null)
                    names.Add("unknown:" + item.GetType().Name);
            }

            return "[" + string.Join(", ", names.ToArray()) + "]";
        }

        private void TryAddDynamicValue(List<string> parts, object target, string propertyName)
        {
            try
            {
                object value = target.GetType().GetProperty(propertyName)?.GetValue(target, null);
                if (value != null)
                    parts.Add(propertyName + "=" + value);
            }
            catch
            {
            }
        }

        private void TryAddDynamicFeatureArray(List<string> parts, object target, string propertyName)
        {
            try
            {
                object value = target.GetType().GetProperty(propertyName)?.GetValue(target, null);
                object[] values = value as object[];
                if (values == null)
                    return;

                List<string> names = new List<string>();
                foreach (object item in values)
                {
                    Feature feature = item as Feature;
                    if (feature != null)
                        names.Add(SafeFeatureName(feature) + "/" + SafeFeatureTypeName(feature));
                }

                if (names.Count > 0)
                    parts.Add(propertyName + "=[" + string.Join(", ", names.ToArray()) + "]");
            }
            catch
            {
            }
        }

        private string SafeComponentName(Component2 component)
        {
            try { return component?.Name2 ?? ""; }
            catch { return ""; }
        }

        private string SafeComponentPath(Component2 component, ModelDoc2 model)
        {
            try
            {
                string path = component?.GetPathName();
                if (!string.IsNullOrWhiteSpace(path))
                    return path;
            }
            catch
            {
            }

            return SafeModelPath(model);
        }

        private string SafeComponentConfig(Component2 component)
        {
            try { return component?.ReferencedConfiguration ?? ""; }
            catch { return ""; }
        }

        private string SafeModelPath(ModelDoc2 model)
        {
            try { return model?.GetPathName() ?? ""; }
            catch { return ""; }
        }

        private List<PatternSeedInfo> CollectPatternSeedInfo(
            SolidWorks.Interop.sldworks.View view,
            MathUtility mathUtil,
            MathTransform viewTransform)
        {
            List<PatternSeedInfo> result = new List<PatternSeedInfo>();
            Array components = view?.GetVisibleComponents() as Array;
            if (components == null)
                return result;

            HashSet<string> scannedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in components)
            {
                Component2 component = item as Component2;
                ModelDoc2 componentModel = component?.GetModelDoc2() as ModelDoc2;
                if (componentModel == null)
                    continue;

                string modelKey = SafeModelPath(componentModel) + "|" + SafeComponentConfig(component);
                if (!scannedModels.Add(modelKey))
                    continue;

                for (Feature feature = componentModel.FirstFeature() as Feature;
                    feature != null;
                    feature = feature.GetNextFeature() as Feature)
                {
                    if (!string.Equals(SafeFeatureTypeName(feature), "CurvePattern", StringComparison.OrdinalIgnoreCase))
                        continue;

                    ICurveDrivenPatternFeatureData data = feature.GetDefinition() as ICurveDrivenPatternFeatureData;
                    if (data == null)
                        continue;

                    bool accessGranted = false;
                    try
                    {
                        accessGranted = data.AccessSelections(componentModel, null);
                        int patternCount = Math.Max(1, data.D1InstanceCount);
                        object[] seedFeatures = data.PatternFeatureArray as object[];
                        if (seedFeatures == null)
                            continue;

                        foreach (object seedItem in seedFeatures)
                        {
                            Feature seedFeature = seedItem as Feature;
                            PatternSeedInfo seed = CreatePatternSeedInfo(
                                seedFeature,
                                patternCount,
                                mathUtil,
                                viewTransform);
                            if (seed != null)
                                AddUniquePatternSeed(result, seed);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DIM HOLE SEED] read failed: " + ex.Message);
                    }
                    finally
                    {
                        if (accessGranted)
                        {
                            try { data.ReleaseSelectionAccess(); }
                            catch { }
                        }
                    }
                }
            }

            Debug.WriteLine("[DIM HOLE SEED] targets=" + result.Count);
            return result;
        }

        private PatternSeedInfo CreatePatternSeedInfo(
            Feature seedFeature,
            int patternCount,
            MathUtility mathUtil,
            MathTransform viewTransform)
        {
            if (seedFeature == null)
                return null;

            List<double[]> cylinderCenters = new List<double[]>();
            List<double> cylinderRadii = new List<double>();
            Array faces = seedFeature.GetFaces() as Array;
            if (faces == null)
                return null;

            foreach (object faceItem in faces)
            {
                Face2 face = faceItem as Face2;
                Surface surface = face?.GetSurface() as Surface;
                if (surface == null || !surface.IsCylinder())
                    continue;

                double[] cylinder = ConvertToDoubleArray(surface.CylinderParams);
                if (cylinder == null || cylinder.Length < 7)
                    continue;

                double[] center = TransformPoint(
                    mathUtil,
                    viewTransform,
                    new[] { cylinder[0], cylinder[1], cylinder[2] });
                double radius = Math.Abs(cylinder[6]);
                if (!IsPoint(center) || radius <= 0)
                    continue;

                bool duplicate = false;
                for (int i = 0; i < cylinderCenters.Count; i++)
                {
                    if (Distance(center[0], center[1], cylinderCenters[i][0], cylinderCenters[i][1]) < 0.00002 &&
                        Math.Abs(radius - cylinderRadii[i]) < 0.00002)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    cylinderCenters.Add(center);
                    cylinderRadii.Add(radius);
                }
            }

            if (cylinderCenters.Count == 0)
            {
                Debug.WriteLine("[DIM HOLE SEED] no cylinder face. feature=" + SafeFeatureName(seedFeature));
                return null;
            }

            int first = 0;
            int second = -1;
            double bestPairDistance = double.MaxValue;
            for (int i = 0; i < cylinderCenters.Count; i++)
            {
                for (int j = i + 1; j < cylinderCenters.Count; j++)
                {
                    double radius = Math.Max(cylinderRadii[i], cylinderRadii[j]);
                    if (Math.Abs(cylinderRadii[i] - cylinderRadii[j]) > Math.Max(0.00002, radius * 0.1))
                        continue;

                    double distance = Distance(
                        cylinderCenters[i][0], cylinderCenters[i][1],
                        cylinderCenters[j][0], cylinderCenters[j][1]);
                    if (distance <= radius * 1.2 || distance >= radius * 30.0)
                        continue;

                    if (distance < bestPairDistance)
                    {
                        bestPairDistance = distance;
                        first = i;
                        second = j;
                    }
                }
            }

            PatternSeedInfo result = new PatternSeedInfo
            {
                SeedFeature = seedFeature,
                PatternCount = patternCount,
                CenterX = cylinderCenters[first][0],
                CenterY = cylinderCenters[first][1],
                Radius = cylinderRadii[first],
                IsSlot = second >= 0
            };

            if (second >= 0)
            {
                result.CenterX = (cylinderCenters[first][0] + cylinderCenters[second][0]) / 2.0;
                result.CenterY = (cylinderCenters[first][1] + cylinderCenters[second][1]) / 2.0;
                result.Radius = (cylinderRadii[first] + cylinderRadii[second]) / 2.0;
                result.SlotLength = bestPairDistance + result.Radius * 2.0;
            }

            Debug.WriteLine("[DIM HOLE SEED] feature=" + SafeFeatureName(seedFeature) +
                ", count=" + patternCount +
                ", diaMm=" + (result.Radius * 2000.0).ToString("0.###") +
                ", slot=" + result.IsSlot +
                ", centerMm=(" + (result.CenterX * 1000.0).ToString("0.###") +
                "," + (result.CenterY * 1000.0).ToString("0.###") + ")");
            return result;
        }

        private double[] ConvertToDoubleArray(object raw)
        {
            double[] values = raw as double[];
            if (values != null)
                return values;

            object[] objects = raw as object[];
            if (objects == null)
                return null;

            values = new double[objects.Length];
            for (int i = 0; i < objects.Length; i++)
                values[i] = Convert.ToDouble(objects[i]);
            return values;
        }

        private void AddUniquePatternSeed(List<PatternSeedInfo> seeds, PatternSeedInfo candidate)
        {
            foreach (PatternSeedInfo seed in seeds)
            {
                if (ReferenceEquals(seed.SeedFeature, candidate.SeedFeature) ||
                    (Distance(seed.CenterX, seed.CenterY, candidate.CenterX, candidate.CenterY) < 0.00002 &&
                    Math.Abs(seed.Radius - candidate.Radius) < 0.00002))
                    return;
            }

            seeds.Add(candidate);
        }

        private void ApplyPatternSeedsToGroups(List<HoleGroup> groups, List<PatternSeedInfo> seeds)
        {
            if (groups == null || seeds == null)
                return;

            HashSet<HoleGroup> assigned = new HashSet<HoleGroup>();
            foreach (PatternSeedInfo seed in seeds)
            {
                HoleGroup bestGroup = null;
                HoleInfo bestHole = null;
                double bestScore = double.MaxValue;

                foreach (HoleGroup group in groups)
                {
                    if (group == null || assigned.Contains(group))
                        continue;

                    foreach (HoleInfo hole in group.Holes)
                    {
                        double radius = GetComparableRadius(hole);
                        double radiusError = Math.Abs(radius - seed.Radius);
                        if (radiusError > Math.Max(0.00005, seed.Radius * 0.15))
                            continue;

                        double slotPenalty = hole.IsSlot == seed.IsSlot ? 0.0 : 0.05;
                        double score = Distance(hole.CenterX, hole.CenterY, seed.CenterX, seed.CenterY) + slotPenalty;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestGroup = group;
                            bestHole = hole;
                        }
                    }
                }

                if (bestGroup == null || bestHole == null)
                    continue;

                bestGroup.Representative = bestHole;
                bestGroup.SeedFeature = seed.SeedFeature;
                bestGroup.PatternCount = seed.PatternCount;
                assigned.Add(bestGroup);
                Debug.WriteLine("[DIM HOLE SEED] mapped feature=" + SafeFeatureName(seed.SeedFeature) +
                    " -> diaMm=" + (GetComparableRadius(bestHole) * 2000.0).ToString("0.###") +
                    ", slot=" + bestHole.IsSlot +
                    ", distanceMm=" + (bestScore * 1000.0).ToString("0.###"));
            }
        }

        private ViewBounds GetViewBounds(SolidWorks.Interop.sldworks.View view, List<HoleInfo> holes)
        {
            try
            {
                double[] outline = view?.GetOutline() as double[];
                if (outline != null && outline.Length >= 4)
                {
                    return new ViewBounds
                    {
                        MinX = outline[0],
                        MinY = outline[1],
                        MaxX = outline[2],
                        MaxY = outline[3]
                    };
                }
            }
            catch
            {
            }

            ViewBounds bounds = new ViewBounds
            {
                MinX = double.MaxValue,
                MinY = double.MaxValue,
                MaxX = double.MinValue,
                MaxY = double.MinValue
            };
            foreach (HoleInfo hole in holes)
            {
                bounds.MinX = Math.Min(bounds.MinX, hole.CenterX);
                bounds.MinY = Math.Min(bounds.MinY, hole.CenterY);
                bounds.MaxX = Math.Max(bounds.MaxX, hole.CenterX);
                bounds.MaxY = Math.Max(bounds.MaxY, hole.CenterY);
            }
            return bounds;
        }

        private List<HoleLayout> BuildHoleLayouts(
            List<HoleGroup> groups,
            List<LineInfo> contours,
            ViewBounds bounds)
        {
            List<HoleLayout> layouts = new List<HoleLayout>();
            double middleY = (bounds.MinY + bounds.MaxY) / 2.0;

            foreach (HoleGroup group in groups)
            {
                HoleInfo hole = group?.Representative;
                if (hole == null)
                    continue;

                LineInfo contour = FindOutermostContour(hole, contours, bounds);
                bool above = contour != null
                    ? contour.MidY >= middleY
                    : hole.CenterY >= middleY;
                layouts.Add(new HoleLayout
                {
                    Group = group,
                    Contour = contour,
                    Above = above
                });
            }

            layouts.Sort(delegate (HoleLayout a, HoleLayout b)
            {
                if (a.Above != b.Above)
                    return a.Above ? -1 : 1;
                return a.Group.Representative.CenterX.CompareTo(b.Group.Representative.CenterX);
            });

            double previousTopX = double.MinValue;
            double previousBottomX = double.MinValue;
            int topLane = 0;
            int bottomLane = 0;
            int topPosition = 0;
            int bottomPosition = 0;
            double minCalloutX = bounds.MinX + 0.005;
            double maxCalloutX = bounds.MaxX - 0.005;

            foreach (HoleLayout layout in layouts)
            {
                HoleInfo hole = layout.Group.Representative;
                double previousX = layout.Above ? previousTopX : previousBottomX;
                int lane = layout.Above ? topLane : bottomLane;

                if (previousX != double.MinValue && Math.Abs(hole.CenterX - previousX) < 0.045)
                    lane = (lane + 1) % 3;
                else
                    lane = 0;

                layout.Lane = lane;
                layout.PositionIndex = layout.Above ? topPosition++ : bottomPosition++;
                double direction = hole.IsSlot
                    ? (layout.PositionIndex % 2 == 0 ? -1.0 : 1.0)
                    : 1.0;
                double horizontalOffset = hole.IsSlot ? 0.026 : 0.018;
                horizontalOffset += lane * 0.006;
                layout.CalloutX = Math.Max(
                    minCalloutX,
                    Math.Min(maxCalloutX, hole.CenterX + direction * horizontalOffset));

                double contourY = layout.Contour != null
                    ? layout.Contour.MidY
                    : hole.CenterY;
                double verticalOffset = 0.007 + lane * 0.005;
                layout.CalloutY = contourY + (layout.Above ? verticalOffset : -verticalOffset);

                if (layout.Above)
                {
                    previousTopX = hole.CenterX;
                    topLane = lane;
                }
                else
                {
                    previousBottomX = hole.CenterX;
                    bottomLane = lane;
                }

                Debug.WriteLine("[DIM HOLE LAYOUT] diaMm=" +
                    (GetComparableRadius(hole) * 2000.0).ToString("0.###") +
                    ", slot=" + hole.IsSlot +
                    ", side=" + (layout.Above ? "TOP" : "BOTTOM") +
                    ", lane=" + lane +
                    ", calloutMm=(" + (layout.CalloutX * 1000.0).ToString("0.###") +
                    "," + (layout.CalloutY * 1000.0).ToString("0.###") + ")");
            }

            return layouts;
        }

        private LineInfo FindOutermostContour(
            HoleInfo hole,
            List<LineInfo> contours,
            ViewBounds bounds)
        {
            if (hole == null || contours == null || bounds == null)
                return null;

            double normalX;
            double normalY;
            double tangentX;
            double tangentY;
            GetOutwardFrame(hole, bounds, out normalX, out normalY, out tangentX, out tangentY);

            LineInfo outermost = null;
            double outermostProjection = double.MinValue;
            double outermostTangentialGap = double.MaxValue;

            foreach (LineInfo contour in contours)
            {
                if (contour == null || contour.Edge == null)
                    continue;

                double dx = contour.EndX - contour.StartX;
                double dy = contour.EndY - contour.StartY;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= 1e-9)
                    continue;

                double parallelScore = Math.Abs(
                    (dx / length) * tangentX +
                    (dy / length) * tangentY);
                if (parallelScore < 0.94)
                    continue;

                double closestX;
                double closestY;
                ClosestPointOnSegment(
                    hole.CenterX,
                    hole.CenterY,
                    contour.StartX,
                    contour.StartY,
                    contour.EndX,
                    contour.EndY,
                    out closestX,
                    out closestY);

                double deltaX = closestX - hole.CenterX;
                double deltaY = closestY - hole.CenterY;
                double outwardProjection = deltaX * normalX + deltaY * normalY;
                double tangentialGap = Math.Abs(deltaX * tangentX + deltaY * tangentY);
                double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

                if (hole.IsSlot && distance < hole.Radius * 2.2)
                    continue;
                if (outwardProjection <= 0.0001)
                    continue;
                if (tangentialGap > 0.020)
                    continue;

                if (outwardProjection > outermostProjection + 1e-6 ||
                    (Math.Abs(outwardProjection - outermostProjection) <= 1e-6 &&
                     tangentialGap < outermostTangentialGap))
                {
                    outermost = contour;
                    outermostProjection = outwardProjection;
                    outermostTangentialGap = tangentialGap;
                }
            }

            if (outermost != null)
            {
                Debug.WriteLine("[DIM HOLE OUTER CONTOUR] slot=" + hole.IsSlot +
                    ", holeMm=(" + (hole.CenterX * 1000.0).ToString("0.###") +
                    "," + (hole.CenterY * 1000.0).ToString("0.###") + ")" +
                    ", normal=(" + normalX.ToString("0.###") +
                    "," + normalY.ToString("0.###") + ")" +
                    ", projectionMm=" + (outermostProjection * 1000.0).ToString("0.###"));
                return outermost;
            }

            LineInfo parallel = FindNearestContourCore(hole, contours, true);
            if (parallel != null)
                return parallel;

            return FindNearestContourCore(hole, contours, false);
        }

        private void GetOutwardFrame(
            HoleInfo hole,
            ViewBounds bounds,
            out double normalX,
            out double normalY,
            out double tangentX,
            out double tangentY)
        {
            double centerX = (bounds.MinX + bounds.MaxX) / 2.0;
            double centerY = (bounds.MinY + bounds.MaxY) / 2.0;

            if (hole.IsSlot &&
                Math.Sqrt(hole.SlotAxisX * hole.SlotAxisX + hole.SlotAxisY * hole.SlotAxisY) > 1e-9)
            {
                double axisLength = Math.Sqrt(
                    hole.SlotAxisX * hole.SlotAxisX +
                    hole.SlotAxisY * hole.SlotAxisY);
                tangentX = hole.SlotAxisX / axisLength;
                tangentY = hole.SlotAxisY / axisLength;
                normalX = -tangentY;
                normalY = tangentX;

                double side =
                    (hole.CenterX - centerX) * normalX +
                    (hole.CenterY - centerY) * normalY;
                if (side < 0)
                {
                    normalX = -normalX;
                    normalY = -normalY;
                }
                return;
            }

            double top = Math.Abs(bounds.MaxY - hole.CenterY);
            double bottom = Math.Abs(hole.CenterY - bounds.MinY);
            double left = Math.Abs(hole.CenterX - bounds.MinX);
            double right = Math.Abs(bounds.MaxX - hole.CenterX);
            double nearest = Math.Min(Math.Min(top, bottom), Math.Min(left, right));

            if (nearest == top)
            {
                normalX = 0;
                normalY = 1;
                tangentX = 1;
                tangentY = 0;
            }
            else if (nearest == bottom)
            {
                normalX = 0;
                normalY = -1;
                tangentX = 1;
                tangentY = 0;
            }
            else if (nearest == left)
            {
                normalX = -1;
                normalY = 0;
                tangentX = 0;
                tangentY = 1;
            }
            else
            {
                normalX = 1;
                normalY = 0;
                tangentX = 0;
                tangentY = 1;
            }
        }

        private LineInfo FindNearestContourCore(
            HoleInfo hole,
            List<LineInfo> contours,
            bool requireParallelToSlot)
        {
            LineInfo nearest = null;
            double nearestDistance = double.MaxValue;

            foreach (LineInfo contour in contours)
            {
                if (contour == null || contour.Edge == null)
                    continue;

                if (requireParallelToSlot)
                {
                    if (!hole.IsSlot)
                        continue;

                    double dx = contour.EndX - contour.StartX;
                    double dy = contour.EndY - contour.StartY;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    double axisLength = Math.Sqrt(
                        hole.SlotAxisX * hole.SlotAxisX +
                        hole.SlotAxisY * hole.SlotAxisY);
                    if (length <= 1e-9 || axisLength <= 1e-9)
                        continue;

                    double parallelScore = Math.Abs(
                        (dx / length) * (hole.SlotAxisX / axisLength) +
                        (dy / length) * (hole.SlotAxisY / axisLength));
                    if (parallelScore < 0.94)
                        continue;
                }

                double distance = DistancePointToSegment(
                    hole.CenterX,
                    hole.CenterY,
                    contour.StartX,
                    contour.StartY,
                    contour.EndX,
                    contour.EndY);

                if (hole.IsSlot && distance < hole.Radius * 2.2)
                    continue;

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = contour;
                }
            }

            if (nearest != null)
            {
                Debug.WriteLine("[DIM HOLE CONTOUR] slot=" + hole.IsSlot +
                    ", parallel=" + requireParallelToSlot +
                    ", holeMm=(" + (hole.CenterX * 1000.0).ToString("0.###") +
                    "," + (hole.CenterY * 1000.0).ToString("0.###") + ")" +
                    ", contourMm=(" + (nearest.StartX * 1000.0).ToString("0.###") +
                    "," + (nearest.StartY * 1000.0).ToString("0.###") + ")->(" +
                    (nearest.EndX * 1000.0).ToString("0.###") +
                    "," + (nearest.EndY * 1000.0).ToString("0.###") + ")" +
                    ", distanceMm=" + (nearestDistance * 1000.0).ToString("0.###"));
            }

            return nearest;
        }

        private bool CreateHoleDiameterDimension(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            HoleInfo hole,
            HoleLayout layout)
        {
            if (hole?.Edge == null)
                return false;

            bool previousValue;
            bool shouldRestore = TrySetInputDimensionOnCreate(model, false, out previousValue);
            try
            {
                model.ClearSelection2(true);
                if (!view.SelectEntity(hole.Edge, false))
                    return false;

                double x = layout.CalloutX;
                double y = layout.CalloutY;

                DrawingDoc drawing = model as DrawingDoc;
                object result = null;
                if (drawing != null)
                {
                    try
                    {
                        result = drawing.AddHoleCallout2(x, y, 0);
                        if (hole.IsSlot)
                            TrySimplifySlotHoleCallout(result as DisplayDimension, hole);
                    }
                    catch
                    {
                        result = null;
                    }
                }

                if (result == null)
                    result = model.AddDiameterDimension2(x, y, 0);

                return result != null;
            }
            finally
            {
                RestoreInputDimensionOnCreate(model, shouldRestore, previousValue);
                model.ClearSelection2(true);
            }
        }

        private bool TrySimplifySlotHoleCallout(DisplayDimension displayDimension, HoleInfo hole)
        {
            if (displayDimension == null || hole == null || !hole.IsSlot)
                return false;

            try
            {
                if (!displayDimension.IsHoleCallout())
                    return false;

                object[] rawVariables = displayDimension.GetHoleCalloutVariables() as object[];
                if (rawVariables == null || rawVariables.Length == 0)
                    return false;

                List<CalloutLengthInfo> lengths = new List<CalloutLengthInfo>();
                foreach (object item in rawVariables)
                {
                    CalloutVariable variable = item as CalloutVariable;
                    CalloutLengthVariable lengthVariable = item as CalloutLengthVariable;
                    if (variable == null || lengthVariable == null)
                        continue;

                    CalloutLengthInfo info = new CalloutLengthInfo
                    {
                        Variable = variable,
                        Length = Math.Abs(lengthVariable.Length),
                        VariableName = variable.VariableName ?? "",
                        UserName = variable.UserReadableVariableName ?? ""
                    };
                    lengths.Add(info);
                    Debug.WriteLine("[DIM HOLE CALLOUT] variable=" + info.VariableName +
                        ", user=" + info.UserName +
                        ", lengthMm=" + (info.Length * 1000.0).ToString("0.###"));
                }

                if (lengths.Count < 2)
                    return false;

                double targetWidth = Math.Abs(hole.ModelRadius) * 2.0;
                double viewToModelScale = hole.Radius > 0
                    ? Math.Abs(hole.ModelRadius / hole.Radius)
                    : 1.0;
                double targetLength = Math.Abs(hole.SlotLength) * viewToModelScale;

                CalloutLengthInfo width = FindClosestCalloutLength(lengths, targetWidth, null);
                CalloutLengthInfo length = FindClosestCalloutLength(lengths, targetLength, width);
                if (width == null || length == null ||
                    string.IsNullOrWhiteSpace(width.VariableName) ||
                    string.IsNullOrWhiteSpace(length.VariableName))
                    return false;

                double widthError = Math.Abs(width.Length - targetWidth);
                double lengthError = Math.Abs(length.Length - targetLength);
                double widthTolerance = Math.Max(0.0002, targetWidth * 0.08);
                double lengthTolerance = Math.Max(0.0003, targetLength * 0.08);
                if (widthError > widthTolerance || lengthError > lengthTolerance)
                {
                    Debug.WriteLine("[DIM HOLE CALLOUT] keep original. widthErrorMm=" +
                        (widthError * 1000.0).ToString("0.###") +
                        ", lengthErrorMm=" + (lengthError * 1000.0).ToString("0.###"));
                    return false;
                }

                string linkedText = FormatCalloutVariableToken(width.VariableName) +
                    " X " +
                    FormatCalloutVariableToken(length.VariableName);
                displayDimension.SetText(
                    (int)swDimensionTextParts_e.swDimensionTextAll,
                    linkedText);

                Debug.WriteLine("[DIM HOLE CALLOUT] linked slot text=" + linkedText +
                    ", target=" + (targetWidth * 1000.0).ToString("0.###") +
                    "x" + (targetLength * 1000.0).ToString("0.###"));
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM HOLE CALLOUT] simplify failed: " + ex.Message);
                return false;
            }
        }

        private string FormatCalloutVariableToken(string variableName)
        {
            string name = (variableName ?? "").Trim();
            if (name.Length == 0)
                return "";

            if (name.StartsWith("<", StringComparison.Ordinal) &&
                name.EndsWith(">", StringComparison.Ordinal))
                return name;

            return "<" + name.Trim('<', '>') + ">";
        }

        private CalloutLengthInfo FindClosestCalloutLength(
            List<CalloutLengthInfo> values,
            double target,
            CalloutLengthInfo excluded)
        {
            CalloutLengthInfo best = null;
            double bestError = double.MaxValue;
            foreach (CalloutLengthInfo value in values)
            {
                if (value == null || ReferenceEquals(value, excluded))
                    continue;

                double error = Math.Abs(value.Length - target);
                if (error < bestError)
                {
                    bestError = error;
                    best = value;
                }
            }
            return best;
        }

        private bool CreateHolePositionDimension(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            HoleInfo hole,
            HoleLayout layout)
        {
            LineInfo contour = layout?.Contour;
            if (hole?.Edge == null || contour?.Edge == null)
                return false;

            bool previousValue;
            bool shouldRestore = TrySetInputDimensionOnCreate(model, false, out previousValue);
            try
            {
                model.ClearSelection2(true);
                if (!view.SelectEntity(hole.Edge, false))
                    return false;

                if (!view.SelectEntity(contour.Edge, true))
                    return false;

                double closestX;
                double closestY;
                ClosestPointOnSegment(
                    hole.CenterX,
                    hole.CenterY,
                    contour.StartX,
                    contour.StartY,
                    contour.EndX,
                    contour.EndY,
                    out closestX,
                    out closestY);

                double laneOffset = 0.004 + layout.Lane * 0.003;
                double x;
                double y;

                object result;
                double contourDx = Math.Abs(contour.EndX - contour.StartX);
                double contourDy = Math.Abs(contour.EndY - contour.StartY);
                if (contourDx >= contourDy * 2.0)
                {
                    double side = layout.CalloutX <= hole.CenterX ? 1.0 : -1.0;
                    x = hole.CenterX + side * laneOffset;
                    y = (hole.CenterY + closestY) / 2.0;
                    result = model.AddVerticalDimension2(x, y, 0);
                }
                else if (contourDy >= contourDx * 2.0)
                {
                    double side = layout.CalloutY <= hole.CenterY ? 1.0 : -1.0;
                    x = (hole.CenterX + closestX) / 2.0;
                    y = hole.CenterY + side * laneOffset;
                    result = model.AddHorizontalDimension2(x, y, 0);
                }
                else
                {
                    double sign =
                        ((hole.CenterX - contour.MidX) * contour.NormalX +
                        (hole.CenterY - contour.MidY) * contour.NormalY) >= 0
                            ? 1.0
                            : -1.0;
                    x = (hole.CenterX + closestX) / 2.0 + contour.NormalX * sign * laneOffset;
                    y = (hole.CenterY + closestY) / 2.0 + contour.NormalY * sign * laneOffset;
                    result = model.AddDimension2(x, y, 0);
                }

                return result != null;
            }
            finally
            {
                RestoreInputDimensionOnCreate(model, shouldRestore, previousValue);
                model.ClearSelection2(true);
            }
        }

        private bool CreatePatternPitchDimension(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            HoleLayout layout,
            ViewBounds bounds,
            List<HoleGroup> allGroups)
        {
            HoleGroup seedGroup = layout?.Group;
            HoleInfo seed = seedGroup?.Representative;
            if (seed == null || seed.Edge == null ||
                seedGroup.SeedFeature == null ||
                seedGroup.PatternCount <= 1 ||
                allGroups == null)
                return false;

            HoleInfo nearestPattern = FindNearestPatternHole(seedGroup, allGroups);
            if (nearestPattern == null || nearestPattern.Edge == null)
                return false;

            Edge seedEdge;
            Edge patternEdge;
            if (!TryGetMatchingPitchEdges(seed, nearestPattern, out seedEdge, out patternEdge))
                return false;

            bool previousValue;
            bool shouldRestore = TrySetInputDimensionOnCreate(model, false, out previousValue);
            try
            {
                model.ClearSelection2(true);
                if (!view.SelectEntity(seedEdge, false))
                    return false;
                if (!view.SelectEntity(patternEdge, true))
                    return false;

                double dx = nearestPattern.CenterX - seed.CenterX;
                double dy = nearestPattern.CenterY - seed.CenterY;
                double midpointX = (seed.CenterX + nearestPattern.CenterX) / 2.0;
                double midpointY = (seed.CenterY + nearestPattern.CenterY) / 2.0;
                double offset = 0.005;
                object result;

                if (Math.Abs(dx) >= Math.Abs(dy) * 2.0)
                {
                    double contourY = layout.Contour != null
                        ? layout.Contour.MidY
                        : (layout.Above ? bounds.MaxY : bounds.MinY);
                    double y = contourY + (layout.Above ? offset : -offset);
                    result = model.AddHorizontalDimension2(midpointX, y, 0);
                }
                else if (Math.Abs(dy) >= Math.Abs(dx) * 2.0)
                {
                    double middleX = (bounds.MinX + bounds.MaxX) / 2.0;
                    bool right = seed.CenterX >= middleX;
                    double contourX = layout.Contour != null
                        ? layout.Contour.MidX
                        : (right ? bounds.MaxX : bounds.MinX);
                    double x = contourX + (right ? offset : -offset);
                    result = model.AddVerticalDimension2(x, midpointY, 0);
                }
                else
                {
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    if (length <= 1e-9)
                        return false;

                    double normalX = -dy / length;
                    double normalY = dx / length;
                    double viewCenterX = (bounds.MinX + bounds.MaxX) / 2.0;
                    double viewCenterY = (bounds.MinY + bounds.MaxY) / 2.0;
                    double side =
                        (midpointX - viewCenterX) * normalX +
                        (midpointY - viewCenterY) * normalY;
                    if (side < 0)
                    {
                        normalX = -normalX;
                        normalY = -normalY;
                    }

                    result = model.AddDimension2(
                        midpointX + normalX * offset,
                        midpointY + normalY * offset,
                        0);
                }

                if (result != null)
                {
                    Debug.WriteLine("[DIM HOLE PITCH] feature=" + SafeFeatureName(seedGroup.SeedFeature) +
                        ", patternCount=" + seedGroup.PatternCount +
                        ", seedMm=(" + (seed.CenterX * 1000.0).ToString("0.###") +
                        "," + (seed.CenterY * 1000.0).ToString("0.###") + ")" +
                        ", patternMm=(" + (nearestPattern.CenterX * 1000.0).ToString("0.###") +
                        "," + (nearestPattern.CenterY * 1000.0).ToString("0.###") + ")");
                }
                return result != null;
            }
            finally
            {
                RestoreInputDimensionOnCreate(model, shouldRestore, previousValue);
                model.ClearSelection2(true);
            }
        }

        private HoleInfo FindNearestPatternHole(HoleGroup seedGroup, List<HoleGroup> allGroups)
        {
            HoleInfo seed = seedGroup?.Representative;
            if (seed == null)
                return null;

            HoleInfo nearest = null;
            double nearestDistance = double.MaxValue;
            double seedRadius = GetComparableRadius(seed);
            double alignmentTolerance = Math.Max(0.0015, Math.Abs(seed.Radius) * 2.5);

            foreach (HoleGroup group in allGroups)
            {
                if (group == null || group.Holes == null)
                    continue;

                if (!ReferenceEquals(group, seedGroup) && group.SeedFeature != null)
                    continue;

                foreach (HoleInfo candidate in group.Holes)
                {
                    if (candidate == null || candidate.Edge == null || ReferenceEquals(candidate, seed))
                        continue;

                    double distance = Distance(
                        seed.CenterX,
                        seed.CenterY,
                        candidate.CenterX,
                        candidate.CenterY);
                    if (distance <= 0.0001)
                        continue;

                    double candidateRadius = GetComparableRadius(candidate);
                    double radiusTolerance = Math.Max(0.00005, seedRadius * 0.15);
                    if (Math.Abs(candidateRadius - seedRadius) > radiusTolerance)
                        continue;

                    bool sameRow = Math.Abs(candidate.CenterY - seed.CenterY) <= alignmentTolerance;
                    bool sameColumn = Math.Abs(candidate.CenterX - seed.CenterX) <= alignmentTolerance;
                    if (!sameRow && !sameColumn)
                        continue;

                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = candidate;
                    }
                }
            }

            return nearest;
        }

        private bool TryGetMatchingPitchEdges(
            HoleInfo seed,
            HoleInfo pattern,
            out Edge seedEdge,
            out Edge patternEdge)
        {
            seedEdge = null;
            patternEdge = null;
            if (seed == null || pattern == null)
                return false;

            Edge[] seedEdges = seed.IsSlot && seed.SecondEdge != null
                ? new[] { seed.Edge, seed.SecondEdge }
                : new[] { seed.Edge };
            Edge[] patternEdges = pattern.IsSlot && pattern.SecondEdge != null
                ? new[] { pattern.Edge, pattern.SecondEdge }
                : new[] { pattern.Edge };

            double[] seedX = seed.IsSlot && seed.SecondEdge != null
                ? new[] { seed.FirstArcCenterX, seed.SecondArcCenterX }
                : new[] { seed.CenterX };
            double[] seedY = seed.IsSlot && seed.SecondEdge != null
                ? new[] { seed.FirstArcCenterY, seed.SecondArcCenterY }
                : new[] { seed.CenterY };
            double[] patternX = pattern.IsSlot && pattern.SecondEdge != null
                ? new[] { pattern.FirstArcCenterX, pattern.SecondArcCenterX }
                : new[] { pattern.CenterX };
            double[] patternY = pattern.IsSlot && pattern.SecondEdge != null
                ? new[] { pattern.FirstArcCenterY, pattern.SecondArcCenterY }
                : new[] { pattern.CenterY };

            double targetDx = pattern.CenterX - seed.CenterX;
            double targetDy = pattern.CenterY - seed.CenterY;
            double bestError = double.MaxValue;

            for (int i = 0; i < seedEdges.Length; i++)
            {
                for (int j = 0; j < patternEdges.Length; j++)
                {
                    if (seedEdges[i] == null || patternEdges[j] == null)
                        continue;

                    double candidateDx = patternX[j] - seedX[i];
                    double candidateDy = patternY[j] - seedY[i];
                    double error = Distance(candidateDx, candidateDy, targetDx, targetDy);
                    if (error < bestError)
                    {
                        bestError = error;
                        seedEdge = seedEdges[i];
                        patternEdge = patternEdges[j];
                    }
                }
            }

            return seedEdge != null && patternEdge != null;
        }

        private void ClosestPointOnSegment(
            double px,
            double py,
            double ax,
            double ay,
            double bx,
            double by,
            out double x,
            out double y)
        {
            double dx = bx - ax;
            double dy = by - ay;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0)
            {
                x = ax;
                y = ay;
                return;
            }

            double t = ((px - ax) * dx + (py - ay) * dy) / lengthSquared;
            t = Math.Max(0.0, Math.Min(1.0, t));
            x = ax + t * dx;
            y = ay + t * dy;
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

        private bool IsFullCircle(Curve curve)
        {
            if (curve == null)
                return false;

            try
            {
                double startParam;
                double endParam;
                bool isClosed;
                bool isPeriodic;
                if (curve.GetEndParams(out startParam, out endParam, out isClosed, out isPeriodic))
                    return isClosed;
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

        private void AddUniqueLine(List<LineInfo> lines, LineInfo candidate)
        {
            foreach (LineInfo line in lines)
            {
                if (ReferenceEquals(line.Edge, candidate.Edge))
                    return;

                if (Distance(line.MidX, line.MidY, candidate.MidX, candidate.MidY) < 0.00001 &&
                    Math.Abs(line.Length - candidate.Length) < 0.00001)
                    return;
            }

            lines.Add(candidate);
        }

        private void AddUniqueHole(List<HoleInfo> holes, HoleInfo candidate)
        {
            foreach (HoleInfo hole in holes)
            {
                if (ReferenceEquals(hole.Edge, candidate.Edge))
                    return;

                if (Distance(hole.CenterX, hole.CenterY, candidate.CenterX, candidate.CenterY) < Math.Max(0.00005, candidate.Radius * 0.2) &&
                    Math.Abs(hole.Radius - candidate.Radius) < Math.Max(0.00002, candidate.Radius * 0.2))
                    return;
            }

            holes.Add(candidate);
        }

        private double DistancePointToSegment(
            double px,
            double py,
            double ax,
            double ay,
            double bx,
            double by)
        {
            double dx = bx - ax;
            double dy = by - ay;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0)
                return Distance(px, py, ax, ay);

            double t = ((px - ax) * dx + (py - ay) * dy) / lengthSquared;
            t = Math.Max(0.0, Math.Min(1.0, t));

            double x = ax + t * dx;
            double y = ay + t * dy;
            return Distance(px, py, x, y);
        }

        private double Distance(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private bool IsPoint(double[] point)
        {
            return point != null && point.Length >= 3;
        }

        private double[] TransformPoint(MathUtility mathUtil, MathTransform transform, double[] point)
        {
            if (mathUtil == null || transform == null || !IsPoint(point))
                return null;

            MathPoint mathPoint = mathUtil.CreatePoint(new[] { point[0], point[1], point[2] }) as MathPoint;
            mathPoint = mathPoint?.MultiplyTransform(transform) as MathPoint;
            return mathPoint?.ArrayData as double[];
        }

        private bool TrySetInputDimensionOnCreate(ModelDoc2 model, bool enabled, out bool previousValue)
        {
            previousValue = false;
            try
            {
                if (swApp != null)
                {
                    previousValue = swApp.GetUserPreferenceToggle(10);
                    swApp.SetUserPreferenceToggle(10, enabled);
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if (model == null)
                    return false;

                previousValue = model.GetUserPreferenceToggle(10);
                model.SetUserPreferenceToggle(10, enabled);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RestoreInputDimensionOnCreate(ModelDoc2 model, bool shouldRestore, bool previousValue)
        {
            if (!shouldRestore)
                return;

            try
            {
                if (swApp != null)
                {
                    swApp.SetUserPreferenceToggle(10, previousValue);
                    return;
                }
            }
            catch
            {
            }

            try
            {
                model?.SetUserPreferenceToggle(10, previousValue);
            }
            catch
            {
            }
        }
    }
}
