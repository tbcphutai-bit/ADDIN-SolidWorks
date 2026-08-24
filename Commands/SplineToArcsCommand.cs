using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SwView = SolidWorks.Interop.sldworks.View;

namespace ADDIN.Commands
{
    internal sealed class SplineToArcsCommand
    {
        private const double MinimumArcLengthMm = 2.0;
        private const int MaximumSplitDepth = 7;
        private readonly ISldWorks swApp;
        private SplineArcOptions currentOptions;

        public SplineToArcsCommand(ISldWorks app)
        {
            swApp = app;
        }

        public void Run(IWin32Window owner)
        {
            Debug.WriteLine("[SPLINE ARC] build=20260726-v6");

            ModelDoc2 model = swApp?.ActiveDoc as ModelDoc2;
            if (model == null)
            {
                ShowMessage("Không có file nào đang mở.", swMessageBoxIcon_e.swMbWarning);
                return;
            }

            if (model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                ShowMessage("Lệnh này chỉ dùng trong môi trường Drawing.", swMessageBoxIcon_e.swMbWarning);
                return;
            }

            Edge selectedEdge;
            SwView selectedView;
            double[] pickPoint;
            if (!TryGetSelectedCurve(model, out selectedEdge, out selectedView, out pickPoint))
            {
                ShowMessage(
                    "Hãy click chọn một cạnh spline trong Drawing View.\n"
                    + "Click gần đầu nào thì lệnh sẽ chạy từ đầu đó.",
                    swMessageBoxIcon_e.swMbInformation);
                return;
            }

            Curve curve = selectedEdge.GetCurve() as Curve;
            if (curve == null || IsLineOrCircle(curve))
            {
                ShowMessage(
                    "Cạnh đang chọn không phải spline/đường cong cần nội suy.",
                    swMessageBoxIcon_e.swMbWarning);
                return;
            }

            SplineArcOptions options;
            using (SplineArcOptionsDialog dialog = new SplineArcOptionsDialog())
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK)
                    return;
                options = dialog.Options;
            }

            try
            {
                currentOptions = options;
                Execute(model, selectedEdge, selectedView, pickPoint, options);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SPLINE ARC] fatal: " + ex);
                ShowMessage(
                    "Không tạo được chuỗi cung R.\n" + ex.Message,
                    swMessageBoxIcon_e.swMbStop);
            }
        }

        private void Execute(
            ModelDoc2 model,
            Edge edge,
            SwView view,
            double[] pickPoint,
            SplineArcOptions options)
        {
            DrawingDoc drawing = model as DrawingDoc;
            bool sketchWasActive = model.SketchManager.ActiveSketch != null;
            bool createdSketch = false;
            bool preferenceChanged = false;
            bool previousInputDimension = false;
            bool previousFocusLocked = false;
            bool focusLockChanged = false;

            try
            {
                model.ClearSelection2(true);
                try
                {
                    previousFocusLocked = view.FocusLocked;
                    view.FocusLocked = true;
                    focusLockChanged = true;
                }
                catch
                {
                }

                if (!sketchWasActive)
                {
                    if (drawing == null || !drawing.ActivateView(view.Name))
                        throw new InvalidOperationException("Không kích hoạt được Drawing View đã chọn.");

                    // IModelDoc2.InsertSketch2 is the drawing-compatible API.
                    // The selected drawing view is active, so the new 2D sketch
                    // belongs to that view and uses drawing-view coordinates.
                    model.InsertSketch2(true);
                    createdSketch = model.SketchManager.ActiveSketch != null;
                    if (!createdSketch)
                        throw new InvalidOperationException("Không mở được Sketch 2D trong Drawing View.");
                }

                SketchSegment referenceSpline;
                string convertError;
                if (!TryConvertEdgeToReferenceSpline(
                    model,
                    edge,
                    view,
                    out referenceSpline,
                    out convertError))
                {
                    throw new InvalidOperationException(convertError);
                }

                CurveContext context;
                string contextError;
                if (!TryBuildConvertedCurveContext(
                    referenceSpline,
                    view,
                    pickPoint,
                    out context,
                    out contextError))
                {
                    throw new InvalidOperationException(contextError);
                }

                // Even in automatic mode, keep 22 mm as a practical maximum
                // span. The tolerance can split it further, but it cannot create
                // one very long, almost-straight arc with an unusably large R.
                double manualStep =
                    context.TotalLength
                    / Math.Max(1, options.ManualSegmentCount);
                List<ParameterRange> initialRanges =
                    BuildInitialRanges(context, manualStep);
                if (initialRanges.Count == 0)
                    throw new InvalidOperationException("Không chia được spline sketch theo bước nội suy.");

                List<ArcDefinition> arcDefinitions =
                    new List<ArcDefinition>();
                foreach (ParameterRange range in initialRanges)
                {
                    ArcDefinition definition;
                    if (!TryFitArc(
                        context,
                        range.Start,
                        range.End,
                        out definition))
                    {
                        throw new InvalidOperationException(
                            "Khong fit duoc cung tron cho mot doan spline.");
                    }

                    if (!options.AutomaticStep
                        && definition.MaximumDeviationMm
                            > options.MaximumDeviationMm + 1e-9)
                    {
                        throw new InvalidOperationException(
                            "So doan muon chia hien tai chua dat sai so cho phep. Hay tang so doan.");
                    }

                    arcDefinitions.Add(definition);
                }

                if (arcDefinitions.Count == 0)
                    throw new InvalidOperationException("Không nội suy được cung tròn từ spline sketch.");

                List<CreatedArc> createdArcs = CreateArcs(model, arcDefinitions);
                if (createdArcs.Count == 0)
                    throw new InvalidOperationException("SolidWorks không tạo được cung sketch.");

                TryDeleteReferenceSpline(model, referenceSpline);

                RelationSummary relationSummary =
                    AddCoincidentRelations(
                        model,
                        createdArcs,
                        edge,
                        view);
                int expectedEdgeAnchorCount = createdArcs.Count * 3;
                int expectedChainCount = Math.Max(0, createdArcs.Count - 1);
                if (relationSummary.EdgeAnchorCount < expectedEdgeAnchorCount)
                {
                    throw new InvalidOperationException(
                        "Chua bat du diem vao canh Drawing View. Bat duoc "
                        + relationSummary.EdgeAnchorCount + "/" + expectedEdgeAnchorCount
                        + " diem neo; noi cung " + relationSummary.ChainCount + "/"
                        + expectedChainCount + ".");
                }

                int sameLengthRelationCount =
                    AddSameLengthRelations(
                        model,
                        createdArcs);

                model.ClearSelection2(true);
                Debug.WriteLine(
                    "[SPLINE ARC] DIMENSION PHASE START "
                    + "(active Drawing View sketch)");
                Debug.WriteLine(
                    "[SPLINE ARC] ActiveSketch before dimensions="
                    + (
                        model.SketchManager.ActiveSketch == null
                            ? "NULL"
                            : "ACTIVE"
                    ));
                Debug.WriteLine(
                    "[SPLINE ARC] Active Drawing View="
                    + view.Name);

                preferenceChanged = TrySetInputDimensionOnCreate(false, out previousInputDimension);
                double[] preferredNormal =
                    GetPreferredDimensionNormal(
                        model,
                        createdArcs,
                        context);
                int radiusDimensionCount = options.AddRadiusDimensions
                    ? AddRadiusDimensions(model, createdArcs, context, preferredNormal)
                    : 0;
                int stepDimensionCount = options.AddStepDimensions
                    ? AddArcLengthDimensions(model, createdArcs, context, preferredNormal)
                    : 0;

                model.ClearSelection2(true);
                model.GraphicsRedraw2();

                double worstDeviationMm = 0.0;
                foreach (ArcDefinition definition in arcDefinitions)
                    worstDeviationMm = Math.Max(worstDeviationMm, definition.MaximumDeviationMm);

                ShowMessage(
                    "Đã tạo " + createdArcs.Count + " cung R theo hướng đã chọn.\n"
                     + "Dim R: " + radiusDimensionCount
                     + " | Dim bước: " + stepDimensionCount
                     + " | Cùng chiều dài cung: " + sameLengthRelationCount
                     + " | Coincident: " + relationSummary.TotalCount
                     + "\nSai số lớn nhất: " + worstDeviationMm.ToString("0.###") + " mm.",
                    swMessageBoxIcon_e.swMbInformation);
            }
            finally
            {
                currentOptions = null;
                if (preferenceChanged)
                    TrySetInputDimensionOnCreate(previousInputDimension, out previousInputDimension);

                model.ClearSelection2(true);
                if (createdSketch && model.SketchManager.ActiveSketch != null)
                {
                    try
                    {
                        model.InsertSketch2(true);
                    }
                    catch
                    {
                    }
                }
                if (focusLockChanged)
                {
                    try
                    {
                        view.FocusLocked = previousFocusLocked;
                    }
                    catch
                    {
                    }
                }
                model.GraphicsRedraw2();
            }
        }

        private bool TryBuildCurveContext(
            Edge edge,
            SwView view,
            double[] pickPoint,
            out CurveContext context,
            out string error)
        {
            context = null;
            error = string.Empty;

            Curve curve = edge?.GetCurve() as Curve;
            CurveParamData parameters = null;
            try
            {
                parameters = edge?.GetCurveParams3();
            }
            catch
            {
            }

            if (curve == null || parameters == null)
            {
                error = "Không đọc được tham số của cạnh spline.";
                return false;
            }

            double uMin = parameters.UMinValue;
            double uMax = parameters.UMaxValue;
            if (Math.Abs(uMax - uMin) < 1e-12)
            {
                error = "Miền tham số của spline không hợp lệ.";
                return false;
            }

            MathUtility mathUtility = swApp.GetMathUtility() as MathUtility;
            MathTransform transform = view?.ModelToViewTransform as MathTransform;
            if (mathUtility == null || transform == null)
            {
                error = "Không đọc được hệ tọa độ của Drawing View.";
                return false;
            }

            double[] startDrawing = TransformPoint(
                mathUtility,
                transform,
                GetEdgePoint(edge, uMin));
            double[] endDrawing = TransformPoint(
                mathUtility,
                transform,
                GetEdgePoint(edge, uMax));
            double[] startSketch = ToViewSketchPoint(view, startDrawing);
            double[] endSketch = ToViewSketchPoint(view, endDrawing);
            if (!IsPoint(startSketch) || !IsPoint(endSketch))
            {
                error = "Không đọc được hai đầu spline.";
                return false;
            }

            double viewScale = view.ScaleDecimal;
            if (viewScale <= 0.0)
                viewScale = 1.0;

            bool forward = true;
            if (IsPoint(pickPoint))
            {
                double[] directionPickPoint =
                    ToViewSketchPoint(view, pickPoint);
                double distanceToStart =
                    Distance2D(directionPickPoint, startSketch);
                double distanceToEnd =
                    Distance2D(directionPickPoint, endSketch);
                forward = distanceToStart <= distanceToEnd;

                Debug.WriteLine(
                    "[SPLINE ARC] viewPosition="
                    + FormatPoint(GetViewPosition(view))
                    + ", pickSheet=" + FormatPoint(pickPoint)
                    + ", pickSketch=" + FormatPoint(directionPickPoint));
            }

            double totalLength;
            try
            {
                totalLength = curve.GetLength3(Math.Min(uMin, uMax), Math.Max(uMin, uMax));
            }
            catch
            {
                totalLength = curve.GetLength2(Math.Min(uMin, uMax), Math.Max(uMin, uMax));
            }

            if (totalLength <= 1e-8)
            {
                error = "Chiều dài spline bằng 0 hoặc không đọc được.";
                return false;
            }

            context = new CurveContext
            {
                Edge = edge,
                Curve = curve,
                View = view,
                MathUtility = mathUtility,
                Transform = transform,
                UMin = Math.Min(uMin, uMax),
                UMax = Math.Max(uMin, uMax),
                Forward = forward,
                TotalLength = totalLength,
                ViewScale = viewScale
            };

            Debug.WriteLine(
                "[SPLINE ARC] view=" + view.Name
                + ", lengthMm=" + (totalLength * 1000.0).ToString("0.###")
                + ", startSheet=" + FormatPoint(startDrawing)
                + ", endSheet=" + FormatPoint(endDrawing)
                + ", startSketch=" + FormatPoint(startSketch)
                + ", endSketch=" + FormatPoint(endSketch)
                + ", scale=" + viewScale.ToString("0.######")
                + ", direction=" + (forward ? "UMin->UMax" : "UMax->UMin"));
            return true;
        }

        private bool TryConvertEdgeToReferenceSpline(
            ModelDoc2 model,
            Edge edge,
            SwView view,
            out SketchSegment referenceSpline,
            out string error)
        {
            referenceSpline = null;
            error = string.Empty;

            Sketch activeSketch = model?.SketchManager?.ActiveSketch as Sketch;
            if (activeSketch == null)
            {
                error = "Khong co active sketch trong Drawing View.";
                return false;
            }

            List<string> beforeKeys = new List<string>();
            foreach (SketchSegment existingSegment in GetSketchSegments(activeSketch))
                beforeKeys.Add(GetSegmentKey(existingSegment));
            model.ClearSelection2(true);

            bool selected = false;
            try
            {
                Entity entity = edge as Entity;
                if (entity != null)
                {
                    SelectionMgr selectionManager =
                        model.SelectionManager as SelectionMgr;
                    SelectData selectData =
                        selectionManager?.CreateSelectData() as SelectData;
                    if (selectData != null)
                        selectData.View = view;
                    selected = entity.Select4(false, selectData);
                }
            }
            catch
            {
            }

            if (!selected)
            {
                error = "Khong select duoc spline da chon de Convert Entities.";
                return false;
            }

            bool converted = false;
            try
            {
                converted = model.SketchManager.SketchUseEdge3(false, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SPLINE ARC] SketchUseEdge3 failed: " + ex.Message);
            }
            finally
            {
                model.ClearSelection2(true);
            }

            if (!converted)
            {
                error = "Convert Entities khong tao duoc spline tham chieu.";
                return false;
            }

            activeSketch = model.SketchManager.ActiveSketch as Sketch;
            List<SketchSegment> afterSegments = GetSketchSegments(activeSketch);
            foreach (SketchSegment segment in afterSegments)
            {
                string key = GetSegmentKey(segment);
                if (beforeKeys.Contains(key))
                    continue;

                Curve curve = null;
                try
                {
                    curve = segment.GetCurve() as Curve;
                }
                catch
                {
                }

                if (curve == null || IsLineOrCircle(curve))
                    continue;

                try
                {
                    segment.ConstructionGeometry = true;
                }
                catch
                {
                }

                referenceSpline = segment;
                Debug.WriteLine(
                    "[SPLINE ARC] reference spline created, key=" + key);
                return true;
            }

            error = "Da Convert Entities nhung khong tim thay spline sketch moi.";
            return false;
        }

        private bool TryBuildConvertedCurveContext(
            SketchSegment referenceSpline,
            SwView view,
            double[] pickPoint,
            out CurveContext context,
            out string error)
        {
            context = null;
            error = string.Empty;

            Curve curve = null;
            try
            {
                curve = referenceSpline?.GetCurve() as Curve;
            }
            catch
            {
            }

            if (curve == null)
            {
                error = "Khong doc duoc curve tu spline sketch da convert.";
                return false;
            }

            double uMin;
            double uMax;
            bool isClosed;
            bool isPeriodic;
            try
            {
                curve.GetEndParams(out uMin, out uMax, out isClosed, out isPeriodic);
            }
            catch
            {
                error = "Khong doc duoc mien tham so cua spline sketch.";
                return false;
            }

            if (Math.Abs(uMax - uMin) < 1e-12)
            {
                error = "Spline sketch co mien tham so khong hop le.";
                return false;
            }

            MathUtility mathUtility = swApp.GetMathUtility() as MathUtility;
            if (mathUtility == null)
            {
                error = "Khong tao duoc MathUtility.";
                return false;
            }

            ModelDoc2 model = swApp?.ActiveDoc as ModelDoc2;
            Sketch activeSketch = model?.SketchManager?.ActiveSketch as Sketch;
            MathTransform modelToSketch =
                activeSketch?.ModelToSketchTransform as MathTransform;

            double[] rawStart = EvaluateCurvePoint(curve, uMin);
            double[] rawEnd = EvaluateCurvePoint(curve, uMax);
            double[] transformedStart = TransformPoint(mathUtility, modelToSketch, rawStart);
            double[] transformedEnd = TransformPoint(mathUtility, modelToSketch, rawEnd);

            SketchPoint startSketchPoint = GetArcStartPoint(referenceSpline);
            SketchPoint endSketchPoint = GetArcEndPoint(referenceSpline);
            double[] sketchStart = ToPoint(startSketchPoint);
            double[] sketchEnd = ToPoint(endSketchPoint);

            bool useTransform =
                IsPoint(transformedStart)
                && IsPoint(transformedEnd)
                && IsPoint(sketchStart)
                && IsPoint(sketchEnd)
                && Distance2D(transformedStart, sketchStart)
                    + Distance2D(transformedEnd, sketchEnd)
                    <= Distance2D(rawStart, sketchStart)
                    + Distance2D(rawEnd, sketchEnd);

            double[] startPoint = useTransform ? transformedStart : rawStart;
            double[] endPoint = useTransform ? transformedEnd : rawEnd;
            MathTransform activeCurveTransform = useTransform ? modelToSketch : null;

            if (!IsPoint(startPoint) || !IsPoint(endPoint))
            {
                error = "Khong xac dinh duoc diem dau/cuoi cua spline sketch.";
                return false;
            }

            bool forward = true;
            if (IsPoint(pickPoint))
            {
                double[] pickSketch = ToViewSketchPoint(view, pickPoint);
                if (IsPoint(pickSketch))
                {
                    double distanceToStart = Distance2D(pickSketch, startPoint);
                    double distanceToEnd = Distance2D(pickSketch, endPoint);
                    forward = distanceToStart <= distanceToEnd;
                }
            }

            double rawLength = GetLength(curve, Math.Min(uMin, uMax), Math.Max(uMin, uMax));
            double lengthScale = 1.0;
            if (IsPoint(rawStart) && IsPoint(rawEnd) && IsPoint(startPoint) && IsPoint(endPoint))
            {
                double rawChord = Distance2D(rawStart, rawEnd);
                double sketchChord = Distance2D(startPoint, endPoint);
                if (rawChord > 1e-12 && sketchChord > 1e-12)
                    lengthScale = sketchChord / rawChord;
            }
            if (lengthScale <= 1e-12 || double.IsNaN(lengthScale) || double.IsInfinity(lengthScale))
                lengthScale = 1.0;

            double totalLength = rawLength * lengthScale;
            if (totalLength <= 1e-8)
            {
                error = "Chieu dai spline sketch bang 0.";
                return false;
            }

            double viewScale = view.ScaleDecimal;
            if (viewScale <= 0.0)
                viewScale = 1.0;

            context = new CurveContext
            {
                Curve = curve,
                View = view,
                MathUtility = mathUtility,
                UMin = Math.Min(uMin, uMax),
                UMax = Math.Max(uMin, uMax),
                Forward = forward,
                TotalLength = totalLength,
                ViewScale = viewScale,
                ReferenceSegment = referenceSpline,
                CurveToSketchTransform = activeCurveTransform,
                CurveLengthScale = lengthScale,
                IsConvertedSketchCurve = true
            };

            Debug.WriteLine(
                "[SPLINE ARC] converted context, lengthMm="
                + (totalLength * 1000.0).ToString("0.###")
                + ", startSketch=" + FormatPoint(startPoint)
                + ", endSketch=" + FormatPoint(endPoint)
                + ", scaleFactor=" + lengthScale.ToString("0.######")
                + ", direction=" + (forward ? "UMin->UMax" : "UMax->UMin"));
            return true;
        }

        private List<ParameterRange> BuildInitialRanges(
            CurveContext context,
            double maximumStep)
        {
            List<ParameterRange> result = new List<ParameterRange>();
            if (context == null || maximumStep <= 0.0)
                return result;

            if (currentOptions != null && currentOptions.AutomaticStep)
            {
                int automaticSegmentCount = FindAutomaticSegmentCount(
                    context,
                    currentOptions.MaximumDeviationMm);
                if (automaticSegmentCount <= 0)
                    return result;

                for (int index = 0; index < automaticSegmentCount; index++)
                {
                    double startDistance =
                        context.TotalLength * index / automaticSegmentCount;
                    double endDistance =
                        context.TotalLength * (index + 1) / automaticSegmentCount;

                    double start = ParameterAtDistance(context, startDistance);
                    double end = ParameterAtDistance(context, endDistance);
                    if (Math.Abs(end - start) > 1e-12)
                        result.Add(new ParameterRange(start, end));
                }

                return result;
            }

            int segmentCount =
                currentOptions != null && currentOptions.ManualSegmentCount > 0
                    ? currentOptions.ManualSegmentCount
                    : Math.Max(1, (int)Math.Round(context.TotalLength / maximumStep));

            for (int index = 0; index < segmentCount; index++)
            {
                double startDistance =
                    context.TotalLength * index / segmentCount;
                double endDistance =
                    context.TotalLength * (index + 1) / segmentCount;

                double start = ParameterAtDistance(context, startDistance);
                double end = ParameterAtDistance(context, endDistance);
                if (Math.Abs(end - start) > 1e-12)
                    result.Add(new ParameterRange(start, end));
            }

            return result;
        }

        private int FindAutomaticSegmentCount(
            CurveContext context,
            double maximumDeviationMm)
        {
            for (int segmentCount = 1; segmentCount <= 200; segmentCount++)
            {
                bool valid = true;
                for (int index = 0; index < segmentCount; index++)
                {
                    double startDistance =
                        context.TotalLength * index / segmentCount;
                    double endDistance =
                        context.TotalLength * (index + 1) / segmentCount;
                    if (!IsDistanceRangeValid(
                        context,
                        startDistance,
                        endDistance,
                        maximumDeviationMm))
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid)
                    return segmentCount;
            }

            return 0;
        }

        private bool IsDistanceRangeValid(
            CurveContext context,
            double startDistance,
            double endDistance,
            double maximumDeviationMm)
        {
            if (endDistance <= startDistance + 1e-9)
                return false;

            double uStart = ParameterAtDistance(context, startDistance);
            double uEnd = ParameterAtDistance(context, endDistance);

            ArcDefinition definition;
            if (!TryFitArc(context, uStart, uEnd, out definition))
                return false;

            return definition.MaximumDeviationMm
                <= maximumDeviationMm + 1e-9;
        }

        private List<ParameterRange> BuildWholeCurveRange(CurveContext context)
        {
            List<ParameterRange> result = new List<ParameterRange>();
            if (context == null || context.TotalLength <= 1e-9)
                return result;

            double start = ParameterAtDistance(context, 0.0);
            double end = ParameterAtDistance(context, context.TotalLength);
            if (Math.Abs(end - start) > 1e-12)
                result.Add(new ParameterRange(start, end));
            return result;
        }

        private void AppendAdaptiveArcDefinitions(
            CurveContext context,
            double uStart,
            double uEnd,
            SplineArcOptions options,
            int depth,
            List<ArcDefinition> output)
        {
            double lengthMm = GetContextLength(context, uStart, uEnd) * 1000.0;
            ArcDefinition definition;
            if (!TryFitArc(context, uStart, uEnd, out definition))
            {
                if (lengthMm > MinimumArcLengthMm * 2.0
                    && depth < MaximumSplitDepth)
                {
                    double fallbackMid = ParameterBetween(
                        context,
                        uStart,
                        uEnd,
                        0.5);
                    if (Math.Abs(fallbackMid - uStart) > 1e-12
                        && Math.Abs(uEnd - fallbackMid) > 1e-12)
                    {
                        AppendAdaptiveArcDefinitions(
                            context,
                            uStart,
                            fallbackMid,
                            options,
                            depth + 1,
                            output);
                        AppendAdaptiveArcDefinitions(
                            context,
                            fallbackMid,
                            uEnd,
                            options,
                            depth + 1,
                            output);
                    }
                }
                return;
            }
            if (options.SplitWhenOverTolerance
                && definition.MaximumDeviationMm > options.MaximumDeviationMm
                && lengthMm > MinimumArcLengthMm * 2.0
                && depth < MaximumSplitDepth)
            {
                double uMid = ParameterBetween(context, uStart, uEnd, 0.5);
                if (Math.Abs(uMid - uStart) > 1e-12
                    && Math.Abs(uEnd - uMid) > 1e-12)
                {
                    AppendAdaptiveArcDefinitions(
                        context,
                        uStart,
                        uMid,
                        options,
                        depth + 1,
                        output);
                    AppendAdaptiveArcDefinitions(
                        context,
                        uMid,
                        uEnd,
                        options,
                        depth + 1,
                        output);
                    return;
                }
            }

            output.Add(definition);
        }

        private bool TryFitArc(
            CurveContext context,
            double uStart,
            double uEnd,
            out ArcDefinition definition)
        {
            definition = null;
            double uMid = ParameterBetween(context, uStart, uEnd, 0.5);
            double[] start = EvaluateSheetPoint(context, uStart);
            double[] mid = EvaluateSheetPoint(context, uMid);
            double[] end = EvaluateSheetPoint(context, uEnd);
            if (!IsPoint(start) || !IsPoint(mid) || !IsPoint(end))
                return false;

            double centerX;
            double centerY;
            double radius;
            if (!TryGetCircle(start, mid, end, out centerX, out centerY, out radius))
                return false;

            short direction = ArcContainsMidpointCounterClockwise(
                centerX,
                centerY,
                start,
                mid,
                end)
                ? (short)1
                : (short)-1;

            double maximumDeviationMm = 0.0;
            foreach (double ratio in new[]
            {
                0.0625, 0.125, 0.1875, 0.25, 0.3125, 0.375, 0.4375,
                0.5625, 0.625, 0.6875, 0.75, 0.8125, 0.875, 0.9375
            })
            {
                double sampleParameter = ParameterBetween(context, uStart, uEnd, ratio);
                double[] sample = EvaluateSheetPoint(context, sampleParameter);
                if (!IsPoint(sample))
                    continue;

                double sampleRadius = Math.Sqrt(
                    Square(sample[0] - centerX)
                    + Square(sample[1] - centerY));
                double deviationMm =
                    Math.Abs(sampleRadius - radius) * 1000.0;
                maximumDeviationMm = Math.Max(maximumDeviationMm, deviationMm);
            }

            definition = new ArcDefinition
            {
                UStart = uStart,
                UEnd = uEnd,
                Start = start,
                Mid = mid,
                End = end,
                CenterX = centerX,
                CenterY = centerY,
                Radius = radius,
                Direction = direction,
                MaximumDeviationMm = maximumDeviationMm
            };
            return true;
        }

        private List<CreatedArc> CreateArcs(
            ModelDoc2 model,
            List<ArcDefinition> definitions)
        {
            List<CreatedArc> result = new List<CreatedArc>();
            SketchManager sketchManager = model.SketchManager;
            bool oldAddToDb = sketchManager.AddToDB;
            bool oldDisplayWhenAdded = sketchManager.DisplayWhenAdded;

            try
            {
                sketchManager.AddToDB = true;
                sketchManager.DisplayWhenAdded = false;

                for (int i = 0; i < definitions.Count; i++)
                {
                    ArcDefinition definition = definitions[i];
                    Debug.WriteLine(
                        "[SPLINE ARC] create arc=" + (i + 1)
                        + ", center=" + FormatPoint(new[]
                        {
                            definition.CenterX,
                            definition.CenterY,
                            0.0
                        })
                        + ", start=" + FormatPoint(definition.Start)
                        + ", end=" + FormatPoint(definition.End)
                        + ", radiusSheetMm="
                        + (definition.Radius * 1000.0).ToString("0.###")
                        + ", deviationMm="
                        + definition.MaximumDeviationMm.ToString("0.###"));

                    // A center/start/end arc can choose the major branch when
                    // the drawing-view axes are mirrored. A three-point arc is
                    // unambiguous and follows the same workflow as the video.
                    SketchSegment segment = sketchManager.Create3PointArc(
                        definition.Start[0],
                        definition.Start[1],
                        0.0,
                        definition.End[0],
                        definition.End[1],
                        0.0,
                        definition.Mid[0],
                        definition.Mid[1],
                        0.0);
                    if (segment != null)
                    {
                        result.Add(new CreatedArc
                        {
                            Definition = definition,
                            Segment = segment
                        });
                    }
                    else
                    {
                        Debug.WriteLine(
                            "[SPLINE ARC] CreateArc returned null at arc="
                            + (i + 1));
                    }
                }
            }
            finally
            {
                sketchManager.DisplayWhenAdded = oldDisplayWhenAdded;
                sketchManager.AddToDB = oldAddToDb;
            }

            model.GraphicsRedraw2();
            return result;
        }

        private RelationSummary AddCoincidentRelations(
            ModelDoc2 model,
            List<CreatedArc> arcs,
            Edge edge,
            SwView view)
        {
            RelationSummary summary = new RelationSummary();
            if (model == null || arcs == null || arcs.Count == 0)
                return summary;

            for (int i = 0; i < arcs.Count; i++)
            {
                try
                {
                    int edgeAnchorCount = AddPointCoincidentRelations(
                        model,
                        edge,
                        view,
                        arcs[i]);
                    summary.EdgeAnchorCount += edgeAnchorCount;
                    Debug.WriteLine(
                        "[SPLINE ARC] edge anchors arc=" + (i + 1)
                        + " success=" + edgeAnchorCount + "/3");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[SPLINE ARC] midpoint-to-edge coincident failed at arc="
                        + (i + 1) + ": " + ex.Message);
                }
            }

            for (int i = 1; i < arcs.Count; i++)
            {
                try
                {
                    SketchPoint previousEnd = GetArcEndPoint(arcs[i - 1].Segment);
                    SketchPoint currentStart = GetArcStartPoint(arcs[i].Segment);
                    if (previousEnd == null || currentStart == null)
                        continue;

                    int chainCount =
                        AddCoincidentRelation(model, previousEnd, currentStart);
                    summary.ChainCount += chainCount;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[SPLINE ARC] coincident relation "
                        + i + " failed: " + ex.Message);
                }
            }

            model.ClearSelection2(true);
            summary.TotalCount =
                summary.EdgeAnchorCount + summary.ChainCount;
            Debug.WriteLine(
                "[SPLINE ARC] coincident relations total=" + summary.TotalCount
                + ", edgeAnchors=" + summary.EdgeAnchorCount
                + ", chain=" + summary.ChainCount
                + ", arcs=" + arcs.Count);
            return summary;
        }

        private int AddPointCoincidentRelations(
            ModelDoc2 model,
            Edge edge,
            SwView view,
            CreatedArc arc)
        {
            if (model == null || edge == null || view == null || arc?.Segment == null)
                return 0;

            int count = 0;

            try
            {
                SketchPoint startPoint = GetArcStartPoint(arc.Segment);
                SketchPoint endPoint = GetArcEndPoint(arc.Segment);

                count += AddSketchPointCoincidentToDrawingEdge(
                    model,
                    startPoint,
                    edge,
                    view,
                    arc.Definition?.Start,
                    arc.Definition);

                count += AddSegmentMidpointCoincidentToDrawingEdge(
                    model,
                    arc.Segment,
                    edge,
                    view,
                    arc.Definition?.Mid,
                    arc.Definition);

                count += AddSketchPointCoincidentToDrawingEdge(
                    model,
                    endPoint,
                    edge,
                    view,
                    arc.Definition?.End,
                    arc.Definition);

                return count;
            }
            catch
            {
                return count;
            }
        }

        private int AddSegmentMidpointCoincidentToDrawingEdge(
            ModelDoc2 model,
            SketchSegment segment,
            Edge edge,
            SwView view,
            double[] sketchPoint,
            ArcDefinition definition)
        {
            if (model == null || segment == null || edge == null || view == null)
                return 0;

            double[] sheetPoint = ToSheetPoint(view, sketchPoint);
            if (!IsPoint(sheetPoint))
                return 0;

            try
            {
                model.ClearSelection2(true);
                if (!SelectSketchSegment(segment, false))
                    return 0;

                model.SelectMidpoint();
                if (!SelectDrawingEdgeInView(model, edge, view, true))
                    return 0;

                model.SketchAddConstraints("sgCOINCIDENT");
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        private int AddSketchPointCoincidentToDrawingEdge(
            ModelDoc2 model,
            SketchPoint point,
            Edge edge,
            SwView view,
            double[] sketchPoint,
            ArcDefinition definition)
        {
            if (model == null || point == null || edge == null || view == null)
                return 0;

            double[] sheetPoint = ToSheetPoint(view, sketchPoint);
            if (!IsPoint(sheetPoint))
                return 0;

            try
            {
                model.ClearSelection2(true);
                if (!SelectSketchPoint(point, false))
                    return 0;

                if (!SelectDrawingEdgeInView(model, edge, view, true))
                    return 0;

                model.SketchAddConstraints("sgCOINCIDENT");
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        private int AddCoincidentRelation(
            ModelDoc2 model,
            object firstEntity,
            object secondEntity)
        {
            if (model == null || firstEntity == null || secondEntity == null)
                return 0;

            try
            {
                model.ClearSelection2(true);
                SketchPoint firstPoint = firstEntity as SketchPoint;
                SketchPoint secondPoint = secondEntity as SketchPoint;
                if (firstPoint == null || secondPoint == null)
                    return 0;

                if (!SelectSketchPoint(firstPoint, false)
                    || !SelectSketchPoint(secondPoint, true))
                {
                    return 0;
                }

                model.SketchAddConstraints("sgCOINCIDENT");
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        private static SketchPoint GetArcStartPoint(SketchSegment segment)
        {
            try
            {
                dynamic sketchArc = segment;
                return sketchArc.GetStartPoint2() as SketchPoint;
            }
            catch
            {
                return null;
            }
        }

        private static SketchPoint GetArcEndPoint(SketchSegment segment)
        {
            try
            {
                dynamic sketchArc = segment;
                return sketchArc.GetEndPoint2() as SketchPoint;
            }
            catch
            {
                return null;
            }
        }

        private static SketchPoint GetArcCenterPoint(SketchSegment segment)
        {
            try
            {
                dynamic sketchArc = segment;
                return sketchArc.GetCenterPoint2() as SketchPoint;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetActualArcGeometry(
            CreatedArc arc,
            out double[] center,
            out double[] midpoint,
            out double radius)
        {
            center = null;
            midpoint = null;
            radius = 0.0;
            if (arc?.Segment == null || arc.Definition == null)
                return false;

            double[] start =
                ToPoint(
                    GetArcStartPoint(
                        arc.Segment));
            double[] end =
                ToPoint(
                    GetArcEndPoint(
                        arc.Segment));
            center =
                ToPoint(
                    GetArcCenterPoint(
                        arc.Segment));
            if (!IsPoint(center))
            {
                center = new[]
                {
                    arc.Definition.CenterX,
                    arc.Definition.CenterY,
                    0.0
                };
            }

            if (!IsPoint(start)
                || !IsPoint(end)
                || !IsPoint(center))
            {
                return false;
            }

            radius = Distance2D(center, start);
            if (radius <= 1e-12)
                return false;

            double startX = (start[0] - center[0]) / radius;
            double startY = (start[1] - center[1]) / radius;
            double endX = (end[0] - center[0]) / radius;
            double endY = (end[1] - center[1]) / radius;
            double midpointDirectionX = startX + endX;
            double midpointDirectionY = startY + endY;
            if (Math.Sqrt(
                midpointDirectionX * midpointDirectionX
                + midpointDirectionY * midpointDirectionY) <= 1e-9)
            {
                midpointDirectionX =
                    arc.Definition.Mid[0] - center[0];
                midpointDirectionY =
                    arc.Definition.Mid[1] - center[1];
            }
            Normalize(
                ref midpointDirectionX,
                ref midpointDirectionY);

            double[] candidateA = new[]
            {
                center[0] + midpointDirectionX * radius,
                center[1] + midpointDirectionY * radius,
                0.0
            };
            double[] candidateB = new[]
            {
                center[0] - midpointDirectionX * radius,
                center[1] - midpointDirectionY * radius,
                0.0
            };
            midpoint =
                Distance2D(
                    candidateA,
                    arc.Definition.Mid)
                <= Distance2D(
                    candidateB,
                    arc.Definition.Mid)
                    ? candidateA
                    : candidateB;
            return true;
        }

        private List<DimensionPlacement> BuildPredictedArcLengthPlacements(
            ModelDoc2 model,
            List<CreatedArc> arcs,
            CurveContext context,
            double[] globalNormal)
        {
            List<DimensionPlacement> placements =
                new List<DimensionPlacement>();
            if (arcs == null)
                return placements;

            for (int i = 0; i < arcs.Count; i++)
            {
                ArcDefinition definition = arcs[i].Definition;
                if (definition == null
                    || !IsPoint(definition.Start)
                    || !IsPoint(definition.End)
                    || !IsPoint(definition.Mid))
                {
                    placements.Add(null);
                    continue;
                }

                double tangentX =
                    definition.End[0] - definition.Start[0];
                double tangentY =
                    definition.End[1] - definition.Start[1];
                Normalize(ref tangentX, ref tangentY);

                double normalAX = -tangentY;
                double normalAY = tangentX;
                double normalBX = tangentY;
                double normalBY = -tangentX;
                double offsetMillimetres =
                    6.0 + (i % 2) * 2.5;
                double centeredIndex =
                    i - (arcs.Count - 1) * 0.5;
                double tangentShiftMillimetres =
                    Math.Max(
                        -4.0,
                        Math.Min(
                            4.0,
                            centeredIndex * 0.8));
                double offsetSketch =
                    SheetMillimetresToSketchLength(
                        context,
                        offsetMillimetres);
                double tangentShiftSketch =
                    SheetMillimetresToSketchLength(
                        context,
                        tangentShiftMillimetres);

                double[] candidateA = new[]
                {
                    definition.Mid[0]
                        + normalAX * offsetSketch
                        + tangentX * tangentShiftSketch,
                    definition.Mid[1]
                        + normalAY * offsetSketch
                        + tangentY * tangentShiftSketch,
                    0.0
                };
                double[] candidateB = new[]
                {
                    definition.Mid[0]
                        + normalBX * offsetSketch
                        + tangentX * tangentShiftSketch,
                    definition.Mid[1]
                        + normalBY * offsetSketch
                        + tangentY * tangentShiftSketch,
                    0.0
                };

                double clearanceA =
                    ScoreDimensionCandidateClearance(
                        candidateA,
                        arcs,
                        i);
                double clearanceB =
                    ScoreDimensionCandidateClearance(
                        candidateB,
                        arcs,
                        i);
                double crowdingA =
                    MeasureSideCrowding(
                        model,
                        context,
                        definition,
                        new[] { normalAX, normalAY, 0.0 });
                double crowdingB =
                    MeasureSideCrowding(
                        model,
                        context,
                        definition,
                        new[] { normalBX, normalBY, 0.0 });
                double crowdingPenalty =
                    SheetMillimetresToSketchLength(
                        context,
                        1.0);
                double scoreA =
                    clearanceA - crowdingA * crowdingPenalty;
                double scoreB =
                    clearanceB - crowdingB * crowdingPenalty;

                bool positiveWins;
                if (Math.Abs(scoreA - scoreB) > 1e-9)
                {
                    positiveWins = scoreA > scoreB;
                }
                else
                {
                    double preferredDot =
                        IsPoint(globalNormal)
                            ? normalAX * globalNormal[0]
                                + normalAY * globalNormal[1]
                            : 1.0;
                    positiveWins = preferredDot >= 0.0;
                }

                placements.Add(
                    new DimensionPlacement
                    {
                        CandidateA = candidateA,
                        CandidateB = candidateB,
                        ClearanceA = clearanceA,
                        ClearanceB = clearanceB,
                        CrowdingA = crowdingA,
                        CrowdingB = crowdingB,
                        ScoreA = scoreA,
                        ScoreB = scoreB,
                        ChosenSign = positiveWins ? 1 : -1
                    });
            }

            return placements;
        }

        private static double[] ApplyRadiusDimensionSpacing(
            double[] initialPoint,
            double laneNormalX,
            double laneNormalY,
            double tangentX,
            double tangentY,
            List<double[]> occupiedPoints,
            CurveContext context)
        {
            if (!IsPoint(initialPoint))
                return initialPoint;

            double[] result =
                new[]
                {
                    initialPoint[0],
                    initialPoint[1],
                    0.0
                };
            if (occupiedPoints == null
                || occupiedPoints.Count == 0)
            {
                return result;
            }

            double minimumSpacing =
                SheetMillimetresToSketchLength(
                    context,
                    12.0);
            double laneStep =
                SheetMillimetresToSketchLength(
                    context,
                    4.0);
            double tangentStep =
                SheetMillimetresToSketchLength(
                    context,
                    1.5);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                double nearestDistance =
                    double.PositiveInfinity;
                for (int i = 0; i < occupiedPoints.Count; i++)
                {
                    if (!IsPoint(occupiedPoints[i]))
                        continue;

                    nearestDistance =
                        Math.Min(
                            nearestDistance,
                            Distance2D(
                                result,
                                occupiedPoints[i]));
                }

                if (nearestDistance >= minimumSpacing)
                    break;

                double tangentSign =
                    attempt % 2 == 0
                        ? 1.0
                        : -1.0;
                result[0] +=
                    laneNormalX * laneStep
                    + tangentX
                        * tangentStep
                        * tangentSign;
                result[1] +=
                    laneNormalY * laneStep
                    + tangentY
                        * tangentStep
                        * tangentSign;
            }

            return result;
        }

        private int AddRadiusDimensions(
            ModelDoc2 model,
            List<CreatedArc> arcs,
            CurveContext context,
            double[] globalNormal)
        {
            Debug.WriteLine(
                "[SPLINE ARC] AddRadiusDimensions entered.");

            int count = 0;

            for (int i = 0; i < arcs.Count; i++)
            {
                CreatedArc arc = arcs[i];
                ArcDefinition definition = arc.Definition;
                if (definition == null || !IsPoint(definition.Mid))
                    continue;

                double[] actualCenter;
                double[] actualMidpoint;
                double actualRadius;
                if (!TryGetActualArcGeometry(
                    arc,
                    out actualCenter,
                    out actualMidpoint,
                    out actualRadius))
                {
                    Debug.WriteLine(
                        "[SPLINE ARC] actual radius geometry unavailable at arc="
                        + (i + 1));
                    continue;
                }

                double radialX =
                    actualMidpoint[0] - actualCenter[0];
                double radialY =
                    actualMidpoint[1] - actualCenter[1];
                Normalize(ref radialX, ref radialY);
                double radialOffsetSketch =
                    SheetMillimetresToSketchLength(
                        context,
                        10.0);

                double[] textPointSketch = new[]
                {
                    actualMidpoint[0]
                        + radialX * radialOffsetSketch,
                    actualMidpoint[1]
                        + radialY * radialOffsetSketch,
                    0.0
                };
                if (!IsPoint(textPointSketch))
                    continue;

                double[] textPointSheet =
                    ToSheetPoint(
                        context.View,
                        textPointSketch);
                textPointSheet =
                    ClampDimensionPointToViewArea(
                        context.View,
                        textPointSheet,
                        25.0);
                if (!IsPoint(textPointSheet))
                    continue;

                try
                {
                    model.ClearSelection2(true);
                    if (!arc.Segment.Select4(false, null))
                    {
                        Debug.WriteLine(
                            "[SPLINE ARC] radius select failed at arc="
                            + (i + 1));
                        continue;
                    }

                    DisplayDimension displayDimension =
                        model.AddRadialDimension2(
                            textPointSketch[0],
                            textPointSketch[1],
                            0.0) as DisplayDimension;
                    if (displayDimension == null)
                    {
                        Debug.WriteLine(
                            "[SPLINE ARC] radius AddRadialDimension2 returned null at arc="
                            + (i + 1));
                        continue;
                    }

                    MakeDriven(displayDimension);
                    try
                    {
                        displayDimension.ShortenedRadius = true;
                        displayDimension.SetBentLeaderLength(false, 0.004);
                    }
                    catch
                    {
                    }
                    RepositionDimensionAnnotation(
                        displayDimension,
                        textPointSheet);
                    double viewScale = 1.0;
                    try
                    {
                        if (context != null
                            && context.View != null
                            && context.View.ScaleDecimal > 1e-12)
                        {
                            viewScale =
                                context.View.ScaleDecimal;
                        }
                    }
                    catch
                    {
                        viewScale = 1.0;
                    }
                    Debug.WriteLine(
                        "[SPLINE ARC] radius dim="
                        + (i + 1)
                        + ", actualRadiusSheetMm="
                        + (
                            actualRadius
                            * viewScale
                            * 1000.0
                        ).ToString("0.###")
                        + ", radialOffsetMm=10"
                        + ", midpointSketch="
                        + FormatPoint(actualMidpoint)
                        + ", textPointSketch="
                        + FormatPoint(textPointSketch)
                        + ", targetSheet="
                        + FormatPoint(textPointSheet));
                    count++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[SPLINE ARC] radius dim failed: " + ex.Message);
                }
                finally
                {
                    model.ClearSelection2(true);
                }
            }

            model.GraphicsRedraw2();
            model.ClearSelection2(true);
            return count;
        }

        private int AddArcLengthDimensions(
            ModelDoc2 model,
            List<CreatedArc> arcs,
            CurveContext context,
            double[] globalNormal)
        {
            Debug.WriteLine(
                "[SPLINE ARC] AddArcLengthDimensions entered.");

            int count = 0;
            List<DisplayDimension> createdDimensions =
                new List<DisplayDimension>();
            List<double[]> finalPositions =
                new List<double[]>();
            List<DimensionPlacement> placements =
                new List<DimensionPlacement>();

            for (int i = 0; i < arcs.Count; i++)
            {
                ArcDefinition definition = arcs[i].Definition;
                if (definition == null
                    || !IsPoint(definition.Start)
                    || !IsPoint(definition.End)
                    || !IsPoint(definition.Mid))
                {
                    placements.Add(null);
                    continue;
                }

                double tangentX =
                    definition.End[0] - definition.Start[0];
                double tangentY =
                    definition.End[1] - definition.Start[1];
                Normalize(ref tangentX, ref tangentY);

                double normalAX = -tangentY;
                double normalAY = tangentX;
                double normalBX = tangentY;
                double normalBY = -tangentX;
                double offsetMillimetres =
                    6.0 + (i % 2) * 2.5;
                double centeredIndex =
                    i - (arcs.Count - 1) * 0.5;
                double tangentShiftMillimetres =
                    Math.Max(
                        -4.0,
                        Math.Min(
                            4.0,
                            centeredIndex * 0.8));
                double offsetSketch =
                    SheetMillimetresToSketchLength(
                        context,
                        offsetMillimetres);
                double tangentShiftSketch =
                    SheetMillimetresToSketchLength(
                        context,
                        tangentShiftMillimetres);

                double[] candidateA = new[]
                {
                    definition.Mid[0]
                        + normalAX * offsetSketch
                        + tangentX * tangentShiftSketch,
                    definition.Mid[1]
                        + normalAY * offsetSketch
                        + tangentY * tangentShiftSketch,
                    0.0
                };
                double[] candidateB = new[]
                {
                    definition.Mid[0]
                        + normalBX * offsetSketch
                        + tangentX * tangentShiftSketch,
                    definition.Mid[1]
                        + normalBY * offsetSketch
                        + tangentY * tangentShiftSketch,
                    0.0
                };

                double clearanceA =
                    ScoreDimensionCandidateClearance(
                        candidateA,
                        arcs,
                        i);
                double clearanceB =
                    ScoreDimensionCandidateClearance(
                        candidateB,
                        arcs,
                        i);
                double crowdingA =
                    MeasureSideCrowding(
                        model,
                        context,
                        definition,
                        new[] { normalAX, normalAY, 0.0 });
                double crowdingB =
                    MeasureSideCrowding(
                        model,
                        context,
                        definition,
                        new[] { normalBX, normalBY, 0.0 });
                double crowdingPenalty =
                    SheetMillimetresToSketchLength(
                        context,
                        1.0);
                double scoreA =
                    clearanceA - crowdingA * crowdingPenalty;
                double scoreB =
                    clearanceB - crowdingB * crowdingPenalty;

                bool positiveWins;
                if (Math.Abs(scoreA - scoreB) > 1e-9)
                {
                    positiveWins = scoreA > scoreB;
                }
                else
                {
                    double preferredDot =
                        IsPoint(globalNormal)
                            ? normalAX * globalNormal[0]
                                + normalAY * globalNormal[1]
                            : 1.0;
                    positiveWins = preferredDot >= 0.0;
                }

                placements.Add(
                    new DimensionPlacement
                    {
                        CandidateA = candidateA,
                        CandidateB = candidateB,
                        ClearanceA = clearanceA,
                        ClearanceB = clearanceB,
                        CrowdingA = crowdingA,
                        CrowdingB = crowdingB,
                        ScoreA = scoreA,
                        ScoreB = scoreB,
                        ChosenSign = positiveWins ? 1 : -1
                    });
            }

            for (int i = 0; i < arcs.Count; i++)
            {
                ArcDefinition definition = arcs[i].Definition;
                DimensionPlacement placement =
                    i < placements.Count
                        ? placements[i]
                        : null;
                if (definition == null
                    || !IsPoint(definition.Mid)
                    || placement == null)
                    continue;

                SketchPoint startPoint = GetArcStartPoint(arcs[i].Segment);
                SketchPoint endPoint = GetArcEndPoint(arcs[i].Segment);
                if (startPoint == null || endPoint == null)
                {
                    Debug.WriteLine(
                        "[SPLINE ARC] arc-length endpoints unavailable at arc="
                        + (i + 1));
                    continue;
                }

                double[] textPointSketch =
                    placement.ChosenSign > 0
                        ? placement.CandidateA
                        : placement.CandidateB;
                if (!IsPoint(textPointSketch))
                    continue;

                double[] textPointSheet =
                    ToSheetPoint(
                        context.View,
                        textPointSketch);
                textPointSheet =
                    ClampDimensionPointToViewArea(
                        context.View,
                        textPointSheet,
                        25.0);
                if (!IsPoint(textPointSheet))
                    continue;

                try
                {
                    model.ClearSelection2(true);
                    if (!SelectSketchSegment(arcs[i].Segment, false)
                        || !startPoint.Select4(true, null)
                        || !endPoint.Select4(true, null))
                    {
                        Debug.WriteLine(
                            "[SPLINE ARC] arc-length selection failed at arc="
                            + (i + 1));
                        continue;
                    }

                    DisplayDimension displayDimension =
                        model.AddDimension2(
                            textPointSketch[0],
                            textPointSketch[1],
                            0.0) as DisplayDimension;
                    if (displayDimension == null)
                    {
                        Debug.WriteLine(
                            "[SPLINE ARC] arc-length AddDimension2 returned null at arc="
                            + (i + 1));
                        continue;
                    }

                    if (displayDimension.GetType()
                        != (int)swDimensionType_e.swArcLengthDimension)
                    {
                        Debug.WriteLine(
                            "[SPLINE ARC] wrong dimension type at arc="
                            + (i + 1)
                            + ", type="
                            + displayDimension.GetType());
                        DeleteDisplayDimension(
                            model,
                            displayDimension);
                        continue;
                    }

                    MakeDriven(displayDimension);
                    try
                    {
                        ApplyRadialArcLengthLeader(displayDimension);
                    }
                    catch
                    {
                    }
                    createdDimensions.Add(displayDimension);
                    finalPositions.Add(textPointSheet);
                    Debug.WriteLine(
                        "[SPLINE ARC] arc-length dim arc="
                        + (i + 1)
                        + ", candidateA="
                        + placement.ClearanceA.ToString("0.######")
                        + ", candidateB="
                        + placement.ClearanceB.ToString("0.######")
                        + ", crowding="
                        + placement.CrowdingA.ToString("0.###")
                        + "/"
                        + placement.CrowdingB.ToString("0.###")
                        + ", chosenSide="
                        + (placement.ChosenSign > 0 ? "+1" : "-1")
                        + ", textPointSketch="
                        + FormatPoint(textPointSketch)
                        + ", textPointSheet="
                        + FormatPoint(textPointSheet));
                    count++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[SPLINE ARC] arc-length dim failed: " + ex.Message);
                }
                finally
                {
                    model.ClearSelection2(true);
                }
            }

            // SolidWorks can move radial-style arc-length dimensions far away
            // while later dimensions are being created. Reapply only the text
            // positions once after the complete chain exists. Do not force
            // CenterText or OffsetText.
            model.GraphicsRedraw2();
            for (int i = 0;
                i < createdDimensions.Count && i < finalPositions.Count;
                i++)
            {
                RepositionDimensionAnnotation(
                    createdDimensions[i],
                    finalPositions[i]);
            }
            model.GraphicsRedraw2();
            for (int i = 0;
                i < createdDimensions.Count && i < finalPositions.Count;
                i++)
            {
                Debug.WriteLine(
                    "[SPLINE ARC] arc-length final text position dim="
                    + (i + 1)
                    + ", requestedSheet="
                    + FormatPoint(finalPositions[i])
                    + ", actualSheet="
                    + FormatPoint(
                        GetDimensionAnnotationPosition(
                            createdDimensions[i])));
            }

            model.ClearSelection2(true);
            return count;
        }

        private int AddSameLengthRelations(
            ModelDoc2 model,
            List<CreatedArc> arcs)
        {
            if (model == null || arcs == null || arcs.Count < 2)
                return 0;

            int count = 0;
            for (int i = 1; i < arcs.Count; i++)
            {
                try
                {
                    model.ClearSelection2(true);
                    if (!SelectSketchSegment(arcs[i - 1].Segment, false)
                        || !SelectSketchSegment(arcs[i].Segment, true))
                    {
                        continue;
                    }

                    model.SketchAddConstraints("sgSAMECURVELENGTH");
                    count++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[SPLINE ARC] same-length relation failed: " + ex.Message);
                }
            }

            model.ClearSelection2(true);
            return count;
        }

        private double[] GetPreferredDimensionNormal(
            ModelDoc2 model,
            List<CreatedArc> arcs,
            CurveContext context)
        {
            if (arcs == null || arcs.Count == 0 || context?.View == null)
                return null;

            ArcDefinition first = arcs[0].Definition;
            ArcDefinition last = arcs[arcs.Count - 1].Definition;
            ArcDefinition middle = arcs[arcs.Count / 2].Definition;
            if (first == null || last == null || middle == null)
                return null;

            double[] startPoint = first.Start;
            double[] endPoint = last.End;
            double[] middlePoint = middle.Mid;
            if (!IsPoint(startPoint) || !IsPoint(endPoint) || !IsPoint(middlePoint))
                return null;

            double tangentX = endPoint[0] - startPoint[0];
            double tangentY = endPoint[1] - startPoint[1];
            Normalize(ref tangentX, ref tangentY);

            double normalX = tangentY;
            double normalY = -tangentX;

            double[] positiveNormal = new[] { normalX, normalY, 0.0 };
            double[] negativeNormal = new[] { -normalX, -normalY, 0.0 };

            double positiveCrowding =
                MeasureSideCrowding(
                    model,
                    context,
                    middle,
                    positiveNormal);
            double negativeCrowding =
                MeasureSideCrowding(
                    model,
                    context,
                    middle,
                    negativeNormal);

            if (positiveCrowding + 1e-9 < negativeCrowding)
                return positiveNormal;

            if (negativeCrowding + 1e-9 < positiveCrowding)
                return negativeNormal;

            double[] viewCenter = GetViewCenterSketch(context.View);
            if (IsPoint(viewCenter))
            {
                double toMiddleX = middlePoint[0] - viewCenter[0];
                double toMiddleY = middlePoint[1] - viewCenter[1];
                if (toMiddleX * normalX + toMiddleY * normalY < 0.0)
                {
                    normalX = -normalX;
                    normalY = -normalY;
                }
            }

            return new[] { normalX, normalY, 0.0 };
        }

        private static double ScoreDimensionCandidateClearance(
            double[] pointSketch,
            List<CreatedArc> arcs,
            int currentArcIndex)
        {
            if (!IsPoint(pointSketch)
                || arcs == null
                || arcs.Count == 0)
            {
                return double.NegativeInfinity;
            }

            double minimumDistance = double.PositiveInfinity;
            for (int i = 0; i < arcs.Count; i++)
            {
                ArcDefinition definition = arcs[i]?.Definition;
                if (definition == null)
                    continue;

                if (IsPoint(definition.Start))
                {
                    minimumDistance =
                        Math.Min(
                            minimumDistance,
                            Distance2D(
                                pointSketch,
                                definition.Start));
                }

                if (IsPoint(definition.End))
                {
                    minimumDistance =
                        Math.Min(
                            minimumDistance,
                            Distance2D(
                                pointSketch,
                                definition.End));
                }

                if (i != currentArcIndex
                    && IsPoint(definition.Mid))
                {
                    minimumDistance =
                        Math.Min(
                            minimumDistance,
                            Distance2D(
                                pointSketch,
                                definition.Mid));
                }
            }

            return double.IsInfinity(minimumDistance)
                ? 0.0
                : minimumDistance;
        }

        private double MeasureSideCrowding(
            ModelDoc2 model,
            CurveContext context,
            ArcDefinition definition,
            double[] normal)
        {
            if (model == null
                || context?.View == null
                || definition == null
                || !IsPoint(definition.Mid)
                || !IsPoint(normal))
            {
                return double.PositiveInfinity;
            }

            double score = 0.0;
            double[] probeOffsetsMm = { 2.5, 4.0, 6.0 };
            SelectionMgr selectionManager =
                model.SelectionManager as SelectionMgr;

            for (int i = 0; i < probeOffsetsMm.Length; i++)
            {
                double offset =
                    SheetMillimetresToSketchLength(
                        context,
                        probeOffsetsMm[i]);
                double[] probeSketch = new[]
                {
                    definition.Mid[0] + normal[0] * offset,
                    definition.Mid[1] + normal[1] * offset,
                    0.0
                };

                double[] probeSheet =
                    ToSheetPoint(
                        context.View,
                        probeSketch);
                if (!IsPoint(probeSheet))
                    continue;

                model.ClearSelection2(true);
                if (!TrySelectDrawingEdgeByRay(
                    model,
                    probeSheet,
                    definition))
                {
                    continue;
                }

                Edge hitEdge =
                    selectionManager?.GetSelectedObject6(1, -1) as Edge;
                model.ClearSelection2(true);

                if (hitEdge == null)
                    continue;

                if (context.Edge != null && hitEdge == context.Edge)
                    continue;

                score += 10.0 - i;
            }

            return score;
        }

        private static void DeleteDisplayDimension(
            ModelDoc2 model,
            DisplayDimension displayDimension)
        {
            try
            {
                Annotation annotation =
                    displayDimension?.GetAnnotation() as Annotation;
                if (annotation != null && annotation.Select3(false, null))
                    model?.EditDelete();
            }
            catch
            {
            }
            finally
            {
                model?.ClearSelection2(true);
            }
        }

        private static void ApplyRadialArcLengthLeader(
            DisplayDimension dimension)
        {
            try
            {
                if (dimension == null)
                    return;

                int status =
                    dimension.SetArcLengthLeader(
                        false,
                        (int)swArcLengthLeaderType_e.swArcLengthLeaderRadial);
                Debug.WriteLine(
                    "[SPLINE ARC] arc-length leader radial status="
                    + status
                    + ", auto="
                    + dimension.GetAutoArcLengthLeader()
                    + ", actualType="
                    + dimension.GetArcLengthLeader());
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[SPLINE ARC] radial arc-length leader failed: "
                    + ex.Message);
            }
        }

        private static double[] ClampDimensionPointToViewArea(
            SwView view,
            double[] pointSheet,
            double marginMillimetres)
        {
            if (view == null || !IsPoint(pointSheet))
                return pointSheet;

            try
            {
                double[] outline =
                    view.GetOutline() as double[];
                if (outline == null || outline.Length < 4)
                    return pointSheet;

                double margin =
                    Math.Max(0.0, marginMillimetres)
                    / 1000.0;
                double minimumX = outline[0] - margin;
                double minimumY = outline[1] - margin;
                double maximumX = outline[2] + margin;
                double maximumY = outline[3] + margin;

                return new[]
                {
                    Math.Max(
                        minimumX,
                        Math.Min(maximumX, pointSheet[0])),
                    Math.Max(
                        minimumY,
                        Math.Min(maximumY, pointSheet[1])),
                    0.0
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[SPLINE ARC] clamp dimension point failed: "
                    + ex.Message);
                return pointSheet;
            }
        }

        private static void RepositionDimensionAnnotation(
            DisplayDimension displayDimension,
            double[] targetPointSheet)
        {
            if (displayDimension == null || !IsPoint(targetPointSheet))
                return;

            try
            {
                Annotation annotation =
                    displayDimension.GetAnnotation() as Annotation;
                if (annotation == null)
                {
                    Debug.WriteLine(
                        "[SPLINE ARC] dimension annotation unavailable.");
                    return;
                }

                bool moved =
                    annotation.SetPosition2(
                        targetPointSheet[0],
                        targetPointSheet[1],
                        0.0);
                Debug.WriteLine(
                    "[SPLINE ARC] annotation reposition="
                    + moved
                    + ", targetSheet="
                    + FormatPoint(targetPointSheet));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[SPLINE ARC] annotation reposition failed: "
                    + ex.Message);
            }
        }

        private static double[] GetDimensionAnnotationPosition(
            DisplayDimension displayDimension)
        {
            if (displayDimension == null)
                return null;

            try
            {
                Annotation annotation =
                    displayDimension.GetAnnotation() as Annotation;
                double[] position =
                    annotation?.GetPosition() as double[];
                if (position == null || position.Length < 3)
                    return null;

                return new[]
                {
                    position[0],
                    position[1],
                    position[2]
                };
            }
            catch
            {
                return null;
            }
        }

        private static void MakeDriven(DisplayDimension displayDimension)
        {
            try
            {
                Dimension dimension = displayDimension.GetDimension2(0);
                if (dimension != null)
                {
                    dimension.DrivenState =
                        (int)swDimensionDrivenState_e.swDimensionDriven;
                    dimension.ReadOnly = true;
                }
            }
            catch
            {
            }
        }

        private static double[] CopyNormalized(double[] vector)
        {
            if (!IsPoint(vector))
                return null;

            double x = vector[0];
            double y = vector[1];
            Normalize(ref x, ref y);
            return new[] { x, y, 0.0 };
        }

        private static bool SelectSketchPoint(SketchPoint point, bool append)
        {
            if (point == null)
                return false;

            try
            {
                return point.Select4(append, null);
            }
            catch
            {
                return false;
            }
        }

        private static bool SelectSketchSegment(SketchSegment segment, bool append)
        {
            if (segment == null)
                return false;

            try
            {
                return segment.Select4(append, null);
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySelectDrawingEdgeByRay(
            ModelDoc2 model,
            double[] sheetPoint,
            ArcDefinition definition)
        {
            if (model?.Extension == null || !IsPoint(sheetPoint))
                return false;

            double tolerance = 0.00075;
            if (definition != null)
                tolerance = Math.Max(
                    tolerance,
                    (definition.MaximumDeviationMm + 0.75) / 1000.0);

            try
            {
                return model.Extension.SelectByRay(
                    sheetPoint[0],
                    sheetPoint[1],
                    250.0,
                    0.0,
                    0.0,
                    -1.0,
                    tolerance,
                    (int)swSelectType_e.swSelEDGES,
                    true,
                    0,
                    0);
            }
            catch
            {
                return false;
            }
        }

        private static bool SelectDrawingEdgeInView(
            ModelDoc2 model,
            Edge edge,
            SwView view,
            bool append)
        {
            if (model == null || edge == null || view == null)
                return false;

            try
            {
                Entity entity = edge as Entity;
                if (entity == null)
                    return false;

                SelectionMgr selectionManager =
                    model.SelectionManager as SelectionMgr;
                SelectData selectData =
                    selectionManager?.CreateSelectData() as SelectData;
                if (selectData != null)
                    selectData.View = view;

                return entity.Select4(append, selectData);
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteReferenceSpline(
            ModelDoc2 model,
            SketchSegment referenceSpline)
        {
            if (model == null || referenceSpline == null)
                return;

            try
            {
                model.ClearSelection2(true);
                if (SelectSketchSegment(referenceSpline, false))
                    model.EditDelete();
            }
            catch
            {
            }
            finally
            {
                model.ClearSelection2(true);
            }
        }

        private double ParameterAtDistance(CurveContext context, double distance)
        {
            distance = Math.Max(0.0, Math.Min(context.TotalLength, distance));
            double low = context.UMin;
            double high = context.UMax;

            for (int i = 0; i < 55; i++)
            {
                double mid = (low + high) * 0.5;
                double currentDistance = context.Forward
                    ? GetContextLength(context, context.UMin, mid)
                    : GetContextLength(context, mid, context.UMax);

                if (currentDistance < distance)
                {
                    if (context.Forward)
                        low = mid;
                    else
                        high = mid;
                }
                else
                {
                    if (context.Forward)
                        high = mid;
                    else
                        low = mid;
                }
            }

            return context.Forward ? (low + high) * 0.5 : (low + high) * 0.5;
        }

        private double ParameterBetween(
            CurveContext context,
            double uStart,
            double uEnd,
            double ratio)
        {
            ratio = Math.Max(0.0, Math.Min(1.0, ratio));
            double totalLength = GetContextLength(context, uStart, uEnd);
            if (totalLength <= 1e-12)
                return (uStart + uEnd) * 0.5;

            bool increasing = uEnd >= uStart;
            double low = Math.Min(uStart, uEnd);
            double high = Math.Max(uStart, uEnd);
            double target = totalLength * ratio;

            for (int i = 0; i < 50; i++)
            {
                double mid = (low + high) * 0.5;
                double current = increasing
                    ? GetContextLength(context, uStart, mid)
                    : GetContextLength(context, mid, uStart);

                if (current < target)
                {
                    if (increasing)
                        low = mid;
                    else
                        high = mid;
                }
                else
                {
                    if (increasing)
                        high = mid;
                    else
                        low = mid;
                }
            }

            return (low + high) * 0.5;
        }

        private double[] EvaluateSheetPoint(CurveContext context, double parameter)
        {
            double[] rawPoint = EvaluateCurvePoint(context.Curve, parameter);
            if (!IsPoint(rawPoint))
                return null;

            if (context.IsConvertedSketchCurve)
            {
                if (context.CurveToSketchTransform != null)
                    return TransformPoint(
                        context.MathUtility,
                        context.CurveToSketchTransform,
                        rawPoint);
                return rawPoint;
            }

            double[] drawingPoint = TransformPoint(
                context.MathUtility,
                context.Transform,
                rawPoint);
            return ToViewSketchPoint(context.View, drawingPoint);
        }

        private static double[] GetEdgePoint(Edge edge, double parameter)
        {
            try
            {
                double[] values = edge.Evaluate2(parameter, 0) as double[];
                if (values != null && values.Length >= 3)
                    return new[] { values[0], values[1], values[2] };
            }
            catch
            {
            }
            return null;
        }

        private static double[] EvaluateCurvePoint(Curve curve, double parameter)
        {
            try
            {
                double[] values = curve.Evaluate2(parameter, 0) as double[];
                if (values != null && values.Length >= 3)
                    return new[] { values[0], values[1], values[2] };
            }
            catch
            {
            }

            return null;
        }

        private double GetContextLength(CurveContext context, double u1, double u2)
        {
            double scale =
                context != null && context.CurveLengthScale > 1e-12
                    ? context.CurveLengthScale
                    : 1.0;
            return GetLength(context.Curve, u1, u2) * scale;
        }

        private static double GetLength(Curve curve, double u1, double u2)
        {
            double start = Math.Min(u1, u2);
            double end = Math.Max(u1, u2);
            try
            {
                return curve.GetLength3(start, end);
            }
            catch
            {
                return curve.GetLength2(start, end);
            }
        }

        private static double[] TransformPoint(
            MathUtility utility,
            MathTransform transform,
            double[] point)
        {
            if (utility == null || transform == null || !IsPoint(point))
                return null;

            MathPoint mathPoint = utility.CreatePoint(point) as MathPoint;
            mathPoint = mathPoint?.MultiplyTransform(transform) as MathPoint;
            double[] result = mathPoint?.ArrayData as double[];
            if (result == null || result.Length < 3)
                return null;
            return new[] { result[0], result[1], result[2] };
        }

        private static double[] ToViewSketchPoint(
            SwView view,
            double[] drawingPoint)
        {
            if (view == null || !IsPoint(drawingPoint))
                return null;

            double[] viewPosition = GetViewPosition(view);
            if (!IsPoint(viewPosition))
                return null;

            double scale = view.ScaleDecimal;
            if (scale <= 1e-12)
                scale = 1.0;

            return new[]
            {
                (drawingPoint[0] - viewPosition[0]) / scale,
                (drawingPoint[1] - viewPosition[1]) / scale,
                0.0
            };
        }

        private static double[] ToSheetPoint(
            SwView view,
            double[] sketchPoint)
        {
            if (view == null || !IsPoint(sketchPoint))
                return null;

            double[] viewPosition = GetViewPosition(view);
            if (!IsPoint(viewPosition))
                return null;

            double scale = view.ScaleDecimal;
            if (scale <= 1e-12)
                scale = 1.0;

            return new[]
            {
                sketchPoint[0] * scale + viewPosition[0],
                sketchPoint[1] * scale + viewPosition[1],
                0.0
            };
        }

        private static double SheetMillimetresToSketchLength(
            CurveContext context,
            double sheetMillimetres)
        {
            double scale = 1.0;

            try
            {
                if (context != null
                    && context.View != null
                    && context.View.ScaleDecimal > 1e-12)
                {
                    scale = context.View.ScaleDecimal;
                }
            }
            catch
            {
                scale = 1.0;
            }

            return (sheetMillimetres / 1000.0) / scale;
        }

        private static double[] GetViewCenterSketch(SwView view)
        {
            if (view == null)
                return null;

            try
            {
                double[] outline = view.GetOutline() as double[];
                if (outline == null || outline.Length < 4)
                    return null;

                double[] centerSheet = new[]
                {
                    (outline[0] + outline[2]) * 0.5,
                    (outline[1] + outline[3]) * 0.5,
                    0.0
                };

                return ToViewSketchPoint(view, centerSheet);
            }
            catch
            {
                return null;
            }
        }

        private static double[] GetViewPosition(SwView view)
        {
            try
            {
                double[] position = view?.Position as double[];
                if (position != null && position.Length >= 2)
                {
                    return new[]
                    {
                        position[0],
                        position[1],
                        0.0
                    };
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool TryGetCircle(
            double[] p1,
            double[] p2,
            double[] p3,
            out double centerX,
            out double centerY,
            out double radius)
        {
            centerX = 0.0;
            centerY = 0.0;
            radius = 0.0;

            double determinant = 2.0 * (
                p1[0] * (p2[1] - p3[1])
                + p2[0] * (p3[1] - p1[1])
                + p3[0] * (p1[1] - p2[1]));
            double scale = Math.Max(
                Distance2D(p1, p2),
                Math.Max(Distance2D(p2, p3), Distance2D(p1, p3)));
            if (Math.Abs(determinant) < Math.Max(1e-16, scale * scale * 1e-8))
                return false;

            double p1Square = Square(p1[0]) + Square(p1[1]);
            double p2Square = Square(p2[0]) + Square(p2[1]);
            double p3Square = Square(p3[0]) + Square(p3[1]);

            centerX = (
                p1Square * (p2[1] - p3[1])
                + p2Square * (p3[1] - p1[1])
                + p3Square * (p1[1] - p2[1]))
                / determinant;
            centerY = (
                p1Square * (p3[0] - p2[0])
                + p2Square * (p1[0] - p3[0])
                + p3Square * (p2[0] - p1[0]))
                / determinant;
            radius = Math.Sqrt(
                Square(p1[0] - centerX)
                + Square(p1[1] - centerY));

            return radius > 1e-8
                && !double.IsNaN(radius)
                && !double.IsInfinity(radius);
        }

        private static bool ArcContainsMidpointCounterClockwise(
            double centerX,
            double centerY,
            double[] start,
            double[] mid,
            double[] end)
        {
            double startAngle = NormalizeAngle(
                Math.Atan2(start[1] - centerY, start[0] - centerX));
            double midAngle = NormalizeAngle(
                Math.Atan2(mid[1] - centerY, mid[0] - centerX));
            double endAngle = NormalizeAngle(
                Math.Atan2(end[1] - centerY, end[0] - centerX));

            double sweepToEnd = NormalizeAngle(endAngle - startAngle);
            double sweepToMid = NormalizeAngle(midAngle - startAngle);
            return sweepToMid <= sweepToEnd + 1e-8;
        }

        private static double NormalizeAngle(double angle)
        {
            double twoPi = Math.PI * 2.0;
            while (angle < 0.0)
                angle += twoPi;
            while (angle >= twoPi)
                angle -= twoPi;
            return angle;
        }

        private bool TryGetSelectedCurve(
            ModelDoc2 model,
            out Edge edge,
            out SwView view,
            out double[] pickPoint)
        {
            edge = null;
            view = null;
            pickPoint = null;

            SelectionMgr selectionManager = model.SelectionManager as SelectionMgr;
            int count = selectionManager?.GetSelectedObjectCount2(-1) ?? 0;
            for (int index = 1; index <= count; index++)
            {
                Edge candidate = selectionManager.GetSelectedObject6(index, -1) as Edge;
                if (candidate == null)
                    continue;

                SwView candidateView = null;
                try
                {
                    candidateView =
                        selectionManager.GetSelectedObjectsDrawingView2(index, -1)
                        as SwView;
                }
                catch
                {
                }

                if (candidateView == null)
                    continue;

                object rawPoint = null;
                try
                {
                    rawPoint = selectionManager.GetSelectionPoint2(index, -1);
                }
                catch
                {
                }

                double[] selectedPoint = rawPoint as double[];
                if (selectedPoint != null && selectedPoint.Length >= 2)
                {
                    pickPoint = new[]
                    {
                        selectedPoint[0],
                        selectedPoint[1],
                        selectedPoint.Length > 2 ? selectedPoint[2] : 0.0
                    };
                }

                edge = candidate;
                view = candidateView;
                return true;
            }

            return false;
        }

        private static bool IsLineOrCircle(Curve curve)
        {
            try
            {
                if (curve.IsLine())
                    return true;
            }
            catch
            {
            }

            try
            {
                if (curve.IsCircle())
                    return true;
            }
            catch
            {
            }

            return false;
        }

        private bool TrySetInputDimensionOnCreate(
            bool enabled,
            out bool previousValue)
        {
            previousValue = false;
            try
            {
                previousValue = swApp.GetUserPreferenceToggle(10);
                swApp.SetUserPreferenceToggle(10, enabled);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ShowMessage(string message, swMessageBoxIcon_e icon)
        {
            try
            {
                swApp.SendMsgToUser2(
                    message,
                    (int)icon,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
            catch
            {
                MessageBox.Show(message, "Spline → Cung R");
            }
        }

        private static bool IsPoint(double[] point)
        {
            return point != null
                && point.Length >= 3
                && !double.IsNaN(point[0])
                && !double.IsNaN(point[1]);
        }

        private static double Distance2D(double[] first, double[] second)
        {
            return Math.Sqrt(
                Square(first[0] - second[0])
                + Square(first[1] - second[1]));
        }

        private static string FormatPoint(double[] point)
        {
            if (!IsPoint(point))
                return "(null)";

            return "("
                + (point[0] * 1000.0).ToString("0.###")
                + ","
                + (point[1] * 1000.0).ToString("0.###")
                + ","
                + (point[2] * 1000.0).ToString("0.###")
                + ")mm";
        }

        private static double Square(double value)
        {
            return value * value;
        }

        private static void Normalize(ref double x, ref double y)
        {
            double length = Math.Sqrt(x * x + y * y);
            if (length <= 1e-12)
            {
                x = 1.0;
                y = 0.0;
                return;
            }
            x /= length;
            y /= length;
        }

        private static List<SketchSegment> GetSketchSegments(Sketch sketch)
        {
            List<SketchSegment> result = new List<SketchSegment>();
            object[] rawSegments = sketch?.GetSketchSegments() as object[];
            if (rawSegments == null)
                return result;

            for (int i = 0; i < rawSegments.Length; i++)
            {
                SketchSegment segment = rawSegments[i] as SketchSegment;
                if (segment != null)
                    result.Add(segment);
            }

            return result;
        }

        private static string GetSegmentKey(SketchSegment segment)
        {
            if (segment == null)
                return string.Empty;

            try
            {
                SketchPoint startPoint = GetArcStartPoint(segment);
                SketchPoint endPoint = GetArcEndPoint(segment);
                double[] start = ToPoint(startPoint);
                double[] end = ToPoint(endPoint);
                if (IsPoint(start) && IsPoint(end))
                {
                    return start[0].ToString("0.########")
                        + "|"
                        + start[1].ToString("0.########")
                        + "|"
                        + end[0].ToString("0.########")
                        + "|"
                        + end[1].ToString("0.########");
                }
            }
            catch
            {
            }

            return segment.GetHashCode().ToString();
        }

        private static double[] ToPoint(SketchPoint point)
        {
            if (point == null)
                return null;

            return new[] { point.X, point.Y, point.Z };
        }

        private sealed class CurveContext
        {
            public Edge Edge;
            public Curve Curve;
            public SwView View;
            public MathUtility MathUtility;
            public MathTransform Transform;
            public double UMin;
            public double UMax;
            public bool Forward;
            public double TotalLength;
            public double ViewScale;
            public SketchSegment ReferenceSegment;
            public MathTransform CurveToSketchTransform;
            public double CurveLengthScale;
            public bool IsConvertedSketchCurve;
        }

        private sealed class ParameterRange
        {
            public ParameterRange(double start, double end)
            {
                Start = start;
                End = end;
            }

            public double Start;
            public double End;
        }

        private sealed class ArcDefinition
        {
            public double UStart;
            public double UEnd;
            public double[] Start;
            public double[] Mid;
            public double[] End;
            public double CenterX;
            public double CenterY;
            public double Radius;
            public short Direction;
            public double MaximumDeviationMm;
        }

        private sealed class CreatedArc
        {
            public ArcDefinition Definition;
            public SketchSegment Segment;
        }

        private sealed class DimensionPlacement
        {
            public double[] CandidateA;
            public double[] CandidateB;
            public double ClearanceA;
            public double ClearanceB;
            public double CrowdingA;
            public double CrowdingB;
            public double ScoreA;
            public double ScoreB;
            public int ChosenSign;
        }

        private sealed class RelationSummary
        {
            public int EdgeAnchorCount;
            public int ChainCount;
            public int TotalCount;
        }
    }
}
