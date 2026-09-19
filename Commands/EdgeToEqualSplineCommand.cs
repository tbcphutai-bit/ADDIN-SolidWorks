using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    /// <summary>
    /// AUTO EQUAL SPLINE
    /// V37.5 MOVE: reusable topology reserve + finer TARGET morph staging; no SketchSpline property writes.
    ///
    /// Workflow:
    /// 1) User selects ONE surface/model Edge in a Part.
    /// 2) Command reads the real 3D Edge curve.
    /// 3) Command automatically finds a point count that satisfies the tolerance.
    /// 4) Fit points are ALWAYS equally spaced by true Edge arc length.
    /// 5) Command creates a 3D Sketch Spline through those points.
    ///
    /// Important:
    /// - Point spacing is NOT equal curve parameter spacing.
    /// - Actual spline tolerance is 0.15–0.50 mm, scaled by edge length.
    /// - If a 3D Sketch is already active, the spline is created there.
    /// - If no sketch is active, the command creates a 3D Sketch automatically.
    /// </summary>
    internal sealed class EdgeToEqualSplineCommand
    {
        private const double DefaultToleranceMm = 0.05;
        private const int MinimumPointCount = 4;
        private const int MaximumPointCount = 32;

        // V37.4: a SAME 3D SketchSpline that is expected to MOVE again must keep
        // some topology in reserve. Runtime logs showed a complex Edge needed
        // 12 points for the first verified CREATE pass, while a later 4-point
        // SAME spline missed a complex Edge by ~50.8 mm even though every fit
        // point reached its target. We cannot safely grow SAME spline topology
        // again because InsertPoint previously changed native parameterization /
        // produced loops. Therefore CREATE starts at 12 and MOVE never reduces
        // below 12 when that topology is already available. Older splines with
        // fewer points are preserved at their current count; they are never grown.
        private const int ReusableSplineReservePointCount = 12;

        // V36.4 CREATE MODE:
        //
        // V37.4 CREATE starts from the reusable topology reserve (12 points),
        // then increases N by exactly one. The FIRST native SketchSpline that
        // passes both normal and dense bidirectional spline<->Edge deviation
        // scans is accepted. The absolute API minimum remains 4, but CREATE no
        // longer collapses reusable 3D spline topology to that floor.
        //
        // 32 is a safety cap, not a target. Most Edges should stop far below it.
        private const int CreateMaximumPointCount = 32;

        // V33: large 3D fit-point moves can converge over several native
        // rebuilds. V31 moved index 0 about 5 mm after one rebuild.
        private const int MoveNativeSolverMaximumPasses = 32;
        private const double MoveNativeSolverMinimumProgress = 1.0e-7;
        private const int MoveNativeSolverStallPassLimit = 3;
        private const int CreateComplexitySampleCount = 24;
        private const double CreateTargetTurnPerSpanDeg = 30.0;

        // Accuracy is measured on the final SketchSpline itself, not on the
        // straight helper chords.
        private const double CreateBaseSplineToleranceMm = 0.15;
        private const double CreateRelativeSplineToleranceRatio = 0.00020;
        private const double CreateMaximumSplineToleranceMm = 0.50;
        private const double MoveRelativeSplineToleranceRatio = 0.00025;

        // V35.7: after the first successful reduction, only continue reducing
        // when the accepted native spline has comfortable tolerance headroom.
        // This prevents probing one point too far and then trying to rebuild
        // topology with InsertPoint, which runtime V35.6 proved can create loops.
        private const double MoveReduceContinueHeadroomRatio = 0.60;
        private const int MoveReduceUndoMaximumSteps = 8;

        // V36.6: minimizing fit-point count must not spend the entire broad MOVE
        // tolerance when the current SAME spline is already much more accurate.
        // Keep at least 0.05 mm budget, or allow at most +25% over the current
        // native deviation; never exceed the normal MOVE tolerance.
        private const double MoveReduceAbsoluteDeviationFloorMm = 0.05;
        private const double MoveReduceMaximumDeviationGrowthRatio = 1.25;

        // V36.7: a newly-created temporary spline is NOT a valid predictor for
        // the SAME existing SketchSpline after DeletePoint. Runtime V36.6 proved
        // N=7 temporary spline error was ~0.546 mm while the SAME spline at N=7
        // had already measured ~0.014 mm. Use a conservative, non-destructive
        // growth prediction from the SAME spline's accepted deviations instead.
        private const double MoveReducePredictionOrder = 4.0;
        private const double MoveReducePredictionSafetyFactor = 1.60;
        private const double MoveReduceObservedGrowthSafetyFactor = 1.10;

        // V37.3: large 3D MOVE is performed as a coherent morph.  The command
        // never writes any SketchSpline property (no tangent/handle/control/
        // construction/style/parameter setter).  Only the existing fit-point
        // XYZ positions are moved through ModelDocExtension.MoveOrCopy.
        //
        // The old point configuration is first mapped near the new Edge by a
        // best-fit 3D similarity transform (rotation + uniform scale +
        // translation), then morphed to the equal-arc targets in bounded
        // stages.  Point identity/order is frozen from the original spline so
        // a fit point can never swap index with its neighbour during MOVE.
        // Each stage rebuilds only once after ALL points are moved.
        private const int MoveMorphMinimumStagesPerLeg = 3;
        private const int MoveMorphMaximumStagesPerLeg = 18;
        private const double MoveMorphMaximumStepToMeanChordRatio = 0.35;

        // V37.5: runtime V37.4 failed only on the TARGET leg when the leg used
        // 3 coarse stages: t=0.333 passed, t=0.667 caused EditRebuild3=false.
        // Keep PREALIGN economical, but make the final approach to the Edge much
        // finer. This changes only fit-point XYZ travel; no SketchSpline property
        // is written and SAME spline identity/topology are still required.
        private const int MoveMorphMinimumTargetStages = 8;
        private const double MoveMorphMaximumTargetStepToMeanChordRatio = 0.14;
        private const double MoveMorphMaximumPointReadbackErrorM = 5.0e-5;
        private const int MoveMorphSanitySamples = 96;
        private const double MoveMorphMaximumLengthRatioFloor = 1.75;
        private const double MoveMorphLengthRatioGrowthLimit = 1.50;
        private const double MoveMorphBacktrackFloor = 0.08;
        private const double MoveMorphBacktrackGrowthAllowance = 0.06;

        // V37.2: V37.1 direct adaptive trials are disabled in the active MOVE
        // route because runtime proved that moving a fit point away and back is
        // not a reversible probe of a native SketchSpline.

        // V37.1: when all SAME-spline fit points reach the selected Edge but
        // the native spline between them still exceeds tolerance, do not add
        // points immediately.  First re-distribute the EXISTING interior fit
        // points along the Edge with a bounded coordinate search.  Endpoints
        // stay fixed, point order stays monotonic, and SAME SketchSpline
        // identity/topology are preserved.
        private const double MoveAdaptiveOptimizeTriggerRatio = 0.35;
        private const int MoveAdaptiveMaximumSweeps = 4;
        private const int MoveAdaptiveMaximumEvaluations = 48;
        private const int MoveAdaptiveTrialValidationSamples = 96;
        private const double MoveAdaptiveInitialStepSegments = 0.35;
        private const double MoveAdaptiveMinimumGapSegments = 0.12;
        private const double MoveAdaptiveImprovementEpsilonMm = 0.001;

        // After the LAST native Equal command, leave SOLIDWORKS alone long enough
        // for its sketch solver to settle, then clear only the selection. V36.6
        // left the final two helper lines selected; the Make Hole selection watcher
        // then fired continuously until SOLIDWORKS became unresponsive/crashed.
        private const int DeferredFinalSelectionCleanupMs = 1200;

        // V36.5: Equal-readiness must be checked BEFORE DeletePoint because a
        // rejected Equal candidate must never force a destructive rollback.
        // Compare normalized chord spread (max-min)/meanChord, not raw spread:
        // raw spread naturally grows when N is reduced because every chord is
        // longer.  A 5% growth limit on the normalized value keeps the Equal
        // solver stable without falsely rejecting a lighter topology.  Very
        // small spreads are allowed a 0.5% floor so near-straight Edges are not
        // rejected by numerical noise.
        private const double MoveReduceMaximumRelativeChordSpreadGrowthRatio = 1.05;
        private const double MoveReduceRelativeChordSpreadFloor = 0.005;
        private const int DeferredEqualPairDelayMs = 350;

        // V36.1: before native Equal is applied, MOVE redistributes the SAME
        // spline fit points to an equal-CHORD solution on the selected Edge.
        // Equal therefore becomes a confirming native relation instead of
        // having to drag points a visible distance after geometry is finished.
        private const int MoveEqualChordRelaxationPasses = 64;
        private const int MoveEqualChordBisectionIterations = 28;
        private const double MoveEqualChordAbsoluteSpreadToleranceM = 1.0e-8;
        private const double MoveEqualChordRelativeSpreadTolerance = 1.0e-8;

        // V36.4 CREATE: test from the absolute minimum point count upward.
        // A candidate that passes the normal scan must also pass a denser
        // second verification before it can be accepted.  This catches
        // narrow local overshoot on strongly curved / twisted 3D Edges
        // without adding points unless the ACTUAL native SketchSpline needs them.
        private const int CreateSplineValidationSamples = 240;
        private const int CreateDenseValidationMinimumSamples = 768;
        private const int CreateDenseValidationSamplesPerPoint = 64;
        private const int CreateDenseValidationMaximumSamples = 2048;
        private const int CreateEdgeSnapshotSamples = 2048;
        private const int ArcLengthSolveIterations = 36;

        // V35 MOVE engine:
        //
        // Preserve the SAME SketchSpline entity.  MOVE no longer uses
        // sgMERGEPOINTS because runtime V34.1 proved the command can return
        // normally while the fit point remains unmoved.
        //
        // V35.4 deletes one-hop blocking construction geometry before MOVE.
        // It still does not trust relation-command return values.  It first
        // proves a real native MoveOrCopy on ONE existing fit point by XYZ
        // readback, then moves the current topology, and only afterwards
        // searches for the smallest point count that still passes the actual
        // native SketchSpline-to-Edge tolerance.
        private const int MoveControlFitMinimumSamples = 96;
        private const int MoveControlFitSamplesPerControl = 12;
        private const double MoveControlFitEndpointWeight = 10000.0;
        private const double MoveControlFitRegularizationRatio = 1.0e-10;

        // SOLIDWORKS swCommands_e.swCommands_Add_Constraint_Samelen
        // "Sketch > Add Relations > Equal"
        private const int EqualRelationCommandId = 1721;


        private static readonly double[] DeviationSamples =
        {
            0.20, 0.40, 0.60, 0.80
        };

        private readonly ISldWorks swApp;

        // CREATE-mode Equal is executed only after the AUTO SPLINE command
        // has completely returned to the SOLIDWORKS message loop.
        // Keeping timers in a static list prevents premature GC.
        private static readonly List<System.Windows.Forms.Timer> DeferredEqualTimers =
            new List<System.Windows.Forms.Timer>();

        private static readonly List<System.Windows.Forms.Timer> CompletionNoticeTimers =
            new List<System.Windows.Forms.Timer>();

        private const int DeferredEqualDelayMs = 350;
        private const int CompletionNoticeDelayMs = 200;

        public EdgeToEqualSplineCommand(ISldWorks app)
        {
            swApp = app;
        }

        private static bool commandRunning;
        public void Run(IWin32Window owner)
        {
            if (commandRunning) return;
            commandRunning = true;
            try { RunCore(owner); }
            finally { commandRunning = false; }
        }

        private void RunCore(IWin32Window owner)
        {
            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] build=20260916-v37.5-finer-target-morph-stages");

            ModelDoc2 model =
                swApp?.ActiveDoc as ModelDoc2;

            if (model != null)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] activeDoc=\"" +
                    SafeGetTitle(model) +
                    "\" docType=" +
                    model.GetType());
            }

            if (model == null)
            {
                ShowMessage(
                    "Không có file SolidWorks đang mở.",
                    swMessageBoxIcon_e.swMbWarning);

                return;
            }

            if (model.GetType() !=
                (int)swDocumentTypes_e.swDocPART)
            {
                ShowMessage(
                    "Lệnh AUTO SPLINE chỉ dùng trong Part.",
                    swMessageBoxIcon_e.swMbWarning);

                return;
            }

            AutoSplineSelection selection =
                ReadAutoSplineSelection(model);

            if (selection.Edge == null ||
                !string.IsNullOrWhiteSpace(
                    selection.Error))
            {
                ShowMessage(
                    string.IsNullOrWhiteSpace(
                        selection.Error)
                        ? "Hãy chọn một Edge Surface/Body."
                        : selection.Error,
                    swMessageBoxIcon_e.swMbInformation);

                return;
            }

            try
            {
                if (selection.MoveSketch != null &&
                    selection.MoveSpline != null)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MODE=MOVE_REATTACH " +
                        "trigger=EXPLICIT_SPLINE_PLUS_EDGE");

                    MoveExistingSplineToEdge(
                        model,
                        selection.MoveSketch,
                        selection.MoveSketchFeature,
                        selection.MoveSpline,
                        selection.Edge,
                        DefaultToleranceMm);
                }
                else
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MODE=CREATE " +
                        "trigger=EDGE_WITHOUT_SPLINE");

                    Execute(
                        model,
                        selection.Edge,
                        DefaultToleranceMm);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] FATAL: " +
                    ex);

                ShowMessage(
                    "AUTO SPLINE thất bại.\n" +
                    ex.Message,
                    swMessageBoxIcon_e.swMbStop);
            }
        }

        private void Execute(ModelDoc2 model, Edge edge, double toleranceMm)
        {
            Curve curve = edge?.GetCurve() as Curve;
            CurveParamData parameters = null;

            try
            {
                parameters = edge?.GetCurveParams3();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[EDGE EQUAL SPLINE] GetCurveParams3 failed: " + ex.Message);
            }

            if (curve == null || parameters == null)
                throw new InvalidOperationException("Không đọc được Curve / parameter của Edge.");

            double u0 = Math.Min(parameters.UMinValue, parameters.UMaxValue);
            double u1 = Math.Max(parameters.UMinValue, parameters.UMaxValue);

            if (Math.Abs(u1 - u0) < 1.0e-12)
                throw new InvalidOperationException("Miền parameter của Edge không hợp lệ.");

            double totalLength = GetCurveLength(curve, u0, u1);
            if (!IsFinite(totalLength) || totalLength <= 1.0e-9)
                throw new InvalidOperationException("Không đọc được chiều dài Edge.");

            double edgeLengthMm =
                totalLength * 1000.0;

            CreatePointPlan createPlan =
                DetermineCreatePointPlan(
                    edge,
                    curve,
                    u0,
                    u1,
                    totalLength);

            double actualSplineToleranceMm =
                GetCreateActualSplineToleranceMm(
                    edgeLengthMm);

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] CREATE START PLAN " +
                "edgeLengthMm=" +
                edgeLengthMm.ToString("0.###", CultureInfo.InvariantCulture) +
                ", tangentSamples=" +
                createPlan.TangentSampleCount +
                ", totalTurnDeg=" +
                createPlan.TotalTurnDeg.ToString("0.###", CultureInfo.InvariantCulture) +
                ", startPoints=" +
                createPlan.PointCount +
                ", maxPoints=" +
                CreateMaximumPointCount +
                ", actualSplineToleranceMm=" +
                actualSplineToleranceMm.ToString("0.######", CultureInfo.InvariantCulture));

            byte[] createEdgeReference = GeometryCall("IModelDocExtension.GetPersistReference3(create)",
                () => model.Extension.GetPersistReference3(edge) as byte[]);
            if (createEdgeReference == null || createEdgeReference.Length == 0)
                throw new InvalidOperationException("Không lưu được reference của Edge.");
            var createSnapshot = BuildCurvePolylineByArcLength(edge, curve, u0, u1, totalLength, CreateEdgeSnapshotSamples);
            var createCandidates = new Dictionary<int, SamplingResult>();
            for (int n = MinimumPointCount; n <= CreateMaximumPointCount; n++)
                createCandidates.Add(n, EvaluateSampling(edge, curve, u0, u1, totalLength, n - 1, double.MaxValue));
            bool created3DSketch = false;
            Sketch activeSketch = model.SketchManager.ActiveSketch as Sketch;

            if (activeSketch != null)
            {
                bool is3D = TryIs3DSketch(activeSketch);
                if (!is3D)
                {
                    throw new InvalidOperationException(
                        "Hiện tại đang edit Sketch 2D.\n" +
                        "Hãy thoát Sketch 2D rồi chạy lại, hoặc edit một 3D Sketch.");
                }
            }
            else
            {
                model.ClearSelection2(true);
                model.SketchManager.Insert3DSketch(true);
                activeSketch = model.SketchManager.ActiveSketch as Sketch;
                if (activeSketch == null)
                    throw new InvalidOperationException("SolidWorks không mở được 3D Sketch.");

                created3DSketch = true;
            }

            bool deferredCreateEqualScheduled =
                false;

            try
            {
                // Snapshot points existing before AUTO SPLINE.
                HashSet<string> pointIdsBefore =
                    CaptureSketchPointIds(
                        activeSketch);

                SamplingResult sampling =
                    null;

                object splineObject =
                    null;

                SketchSegment acceptedSplineSegment =
                    null;

                double acceptedActualDeviation =
                    double.MaxValue;

                int startPointCount =
                    Math.Max(
                        MinimumPointCount,
                        ReusableSplineReservePointCount);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] CREATE V37.4 REUSABLE MIN-POINT SEARCH BEGIN " +
                    "reserveFloor=" + ReusableSplineReservePointCount +
                    ", absoluteMinimum=" + MinimumPointCount +
                    ", startPoints=" + startPointCount +
                    ", maxPoints=" + CreateMaximumPointCount +
                    ", fastSamples=" + CreateSplineValidationSamples +
                    ", edgeSnapshotSamples=" + CreateEdgeSnapshotSamples);

                // V37.4: start at the reusable reserve floor rather than the
                // absolute mathematical minimum. The FIRST candidate that
                // survives both native deviation scans is the minimum verified
                // N that also retains enough topology for later SAME-spline MOVE.
                for (int candidatePointCount =
                         startPointCount;
                     candidatePointCount <=
                         CreateMaximumPointCount;
                     candidatePointCount++)
                {
                    SamplingResult candidateSampling =
                        createCandidates[candidatePointCount];

                    if (candidateSampling == null ||
                        candidateSampling.Points == null ||
                        candidateSampling.Points.Count !=
                            candidatePointCount)
                    {
                        throw new InvalidOperationException(
                            "Không tạo được equal-arc sampling cho candidate spline.");
                    }

                    double[] pointData =
                        FlattenPoints(
                            candidateSampling.Points);

                    object candidateSplineObject =
                        CreateSplineCompat(
                            model.SketchManager,
                            pointData);

                    SketchSegment candidateSplineSegment =
                        candidateSplineObject
                        as SketchSegment;

                    if (candidateSplineObject == null ||
                        candidateSplineSegment == null)
                    {
                        throw new InvalidOperationException(
                            "SolidWorks không tạo được candidate Spline.");
                    }

                    // Stage 1: inexpensive native-curve scan.
                    double fastActualDeviation =
                        MeasureActualSplineToEdgeDeviation(
                            null, null, 0, 0, 0,
                            candidateSplineSegment,
                            CreateSplineValidationSamples, createSnapshot);

                    bool fastPass =
                        IsFinite(fastActualDeviation) &&
                        fastActualDeviation * 1000.0 <=
                            actualSplineToleranceMm + 1.0e-9;

                    // Stage 2: ONLY a fast-pass candidate gets a dense scan.
                    // This keeps the search efficient while preventing a small,
                    // highly twisted region from slipping between sparse samples.
                    int denseValidationSamples = 0;
                    double denseActualDeviation = fastActualDeviation;
                    bool densePass = false;

                    if (fastPass)
                    {
                        denseValidationSamples =
                            GetCreateDenseValidationSampleCount(
                                candidatePointCount);

                        denseActualDeviation =
                            MeasureActualSplineToEdgeDeviation(
                                null, null, 0, 0, 0,
                                candidateSplineSegment,
                                denseValidationSamples, createSnapshot);

                        densePass =
                            IsFinite(denseActualDeviation) &&
                            denseActualDeviation * 1000.0 <=
                                actualSplineToleranceMm + 1.0e-9;
                    }

                    bool accepted =
                        fastPass && densePass;

                    double actualDeviation =
                        fastPass
                            ? denseActualDeviation
                            : fastActualDeviation;

                    bool atHardCap =
                        candidatePointCount ==
                        CreateMaximumPointCount;

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] CREATE V36.4 MIN-POINT TEST " +
                        "points=" + candidatePointCount +
                        ", fastDeviationMm=" +
                        (fastActualDeviation * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", fastPass=" + fastPass +
                        ", denseSamples=" + denseValidationSamples +
                        ", denseDeviationMm=" +
                        (denseActualDeviation * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", densePass=" + densePass +
                        ", toleranceMm=" +
                        actualSplineToleranceMm
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", pass=" + accepted +
                        ", hardCap=" + atHardCap);

                    if (accepted)
                    {
                        sampling =
                            candidateSampling;

                        splineObject =
                            candidateSplineObject;

                        acceptedSplineSegment =
                            candidateSplineSegment;

                        acceptedActualDeviation =
                            actualDeviation;

                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] CREATE V36.4 FIRST VERIFIED PASS " +
                            "minimumPoints=" + candidatePointCount +
                            ", denseDeviationMm=" +
                            (actualDeviation * 1000.0)
                                .ToString("0.######", CultureInfo.InvariantCulture) +
                            ", toleranceMm=" +
                            actualSplineToleranceMm
                                .ToString("0.######", CultureInfo.InvariantCulture));

                        break;
                    }

                    if (atHardCap)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] CREATE V36.4 HARD CAP FAIL " +
                            "maxPoints=" + CreateMaximumPointCount +
                            ", deviationMm=" +
                            (actualDeviation * 1000.0)
                                .ToString("0.######", CultureInfo.InvariantCulture) +
                            ", toleranceMm=" +
                            actualSplineToleranceMm
                                .ToString("0.######", CultureInfo.InvariantCulture));
                    }

                    if (!DeleteSingleSketchSegment(
                            model,
                            candidateSplineSegment))
                    {
                        throw new InvalidOperationException(
                            "Không xóa được candidate spline chưa đạt accuracy.");
                    }
                }

                if (sampling == null ||
                    splineObject == null ||
                    acceptedSplineSegment == null)
                {
                    throw new InvalidOperationException(
                        "Không tìm được spline đạt tolerance với tối đa " + CreateMaximumPointCount + " fit point.");
                }

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] CREATE V37.4 REUSABLE MIN-POINT RESULT " +
                    "minimumVerifiedPoints=" +
                    sampling.Points.Count +
                    ", reserveFloor=" + ReusableSplineReservePointCount +
                    ", reserveSatisfied=" +
                    (sampling.Points.Count >= ReusableSplineReservePointCount) +
                    ", actualDeviationMm=" +
                    (acceptedActualDeviation * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", toleranceMm=" +
                    actualSplineToleranceMm
                        .ToString("0.######", CultureInfo.InvariantCulture));

                // Read only the FINAL accepted spline fit points.
                List<SketchPoint> fitPoints =
                    FindOrderedNewFitPoints(
                        activeSketch,
                        pointIdsBefore,
                        sampling.Points);

                if (fitPoints.Count !=
                    sampling.Points.Count)
                {
                    throw new InvalidOperationException(
                        "Không nhận đủ fit point của final spline. " +
                        "Expected=" +
                        sampling.Points.Count +
                        ", Found=" +
                        fitPoints.Count);
                }

                edge = ReacquireTargetEdge(model, createEdgeReference);
                EqualRelationResult relationResult =
                    BuildEqualSpacingRelations(
                        swApp,
                        model,
                        activeSketch,
                        edge,
                        fitPoints,
                        false);

                // V16.2:
                // DO NOT run native Equal 1721 inside this CREATE call stack.
                // Geometry + helper relations must finish first. Equal is
                // scheduled on a WinForms timer after control returns to the
                // SOLIDWORKS message loop, which is much closer to the user's
                // successful manual Equal workflow.
                Feature createSketchFeature =
                    FindOwningFeatureForSketch(
                        model,
                        activeSketch);

                deferredCreateEqualScheduled =
                    ScheduleDeferredCreateEqual(
                        model,
                        activeSketch,
                        createSketchFeature,
                        relationResult.CreatedConstructionLines);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] CREATE EQUAL DEFERRED scheduled=" +
                    deferredCreateEqualScheduled +
                    ", delayMs=" +
                    DeferredEqualDelayMs +
                    ", helperLines=" +
                    relationResult.ConstructionLineCount);

                SpacingStats spacing =
                    MeasurePointSpacing(
                        fitPoints);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] RELATION RESULT " +
                    "pointOnEdge=" + relationResult.PointOnEdgeCount +
                    "/" + fitPoints.Count +
                    ", endpoint=" + relationResult.EndpointCoincidentCount +
                    "/2" +
                    ", constructionLines=" + relationResult.ConstructionLineCount +
                    ", equal=DEFERRED" +
                    ", deferredScheduled=" + deferredCreateEqualScheduled);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] FINAL CREATE GEOMETRY " +
                    "points=" +
                    fitPoints.Count +
                    ", actualSplineDeviationMm=" +
                    (acceptedActualDeviation * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", chordMinMm=" +
                    (spacing.Min * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", chordMaxMm=" +
                    (spacing.Max * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", chordDeltaMm=" +
                    ((spacing.Max - spacing.Min) * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture));

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] CLEAN TYPED COM PASS");

                if (deferredCreateEqualScheduled)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] CREATE RETURN BEFORE EQUAL " +
                        "noFurtherSolidWorksApiCalls=True");
                }
                else
                {
                    model.ClearSelection2(true);
                    model.GraphicsRedraw2();

                    ShowMessage(
                        "Đã tạo 3D Spline + helper line, nhưng Equal tự động đang tắt do đường gọi native từng gây crash.\n" +
                        "Bạn có thể Equal thủ công.",
                        swMessageBoxIcon_e.swMbWarning);
                }
            }
            finally
            {
                if (deferredCreateEqualScheduled)
                {
                    // Keep this exact 3D sketch active until the deferred Equal
                    // callback runs. The callback itself will not touch
                    // SOLIDWORKS after RunCommand(1721) returns.
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] CREATE FINALLY DEFERRED-SKIP " +
                        "created3DSketch=" +
                        created3DSketch);
                }
                else
                {
                    if (created3DSketch &&
                        model.SketchManager.ActiveSketch != null)
                    {
                        try
                        {
                            model.SketchManager.Insert3DSketch(true);
                        }
                        catch
                        {
                        }
                    }

                    model.ClearSelection2(true);
                    model.GraphicsRedraw2();
                }
            }
        }

        private CreatePointPlan DetermineCreatePointPlan(
            Edge edge,
            Curve curve,
            double u0,
            double u1,
            double totalLength)
        {
            CreatePointPlan plan =
                new CreatePointPlan
                {
                    PointCount =
                        MinimumPointCount
                };

            if (edge == null ||
                curve == null ||
                totalLength <= 1.0e-12)
            {
                return plan;
            }

            List<double[]> tangents =
                new List<double[]>();

            for (int i = 0;
                 i <= CreateComplexitySampleCount;
                 i++)
            {
                double targetLength =
                    totalLength *
                    i /
                    CreateComplexitySampleCount;

                double parameter =
                    FindParameterAtArcLength(
                        curve,
                        u0,
                        u1,
                        totalLength,
                        targetLength);

                double[] tangent =
                    EvaluateEdgeTangent(
                        edge,
                        parameter);

                if (IsVector3(
                        tangent))
                {
                    tangents.Add(
                        tangent);
                }
            }

            plan.TangentSampleCount =
                tangents.Count;

            // If tangent evaluation is unavailable, do NOT fall back to the
            // old chord-error logic. Keep the spline light.
            if (tangents.Count < 2)
            {
                plan.PointCount =
                    Math.Min(
                        6,
                        CreateMaximumPointCount);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] CREATE COMPLEXITY " +
                    "tangentUnavailable=True" +
                    ", fallbackPoints=" +
                    plan.PointCount);

                return plan;
            }

            double totalTurnDeg =
                0.0;

            double maxLocalTurnDeg =
                0.0;

            for (int i = 1;
                 i < tangents.Count;
                 i++)
            {
                double angleDeg =
                    AngleBetweenVectorsDeg(
                        tangents[i - 1],
                        tangents[i]);

                totalTurnDeg +=
                    angleDeg;

                if (angleDeg >
                    maxLocalTurnDeg)
                {
                    maxLocalTurnDeg =
                        angleDeg;
                }
            }

            // Start estimate only. V19 validates the real SketchSpline
            // afterwards and increases N only if its measured error requires it.
            int requiredSegments =
                Math.Max(
                    MinimumPointCount - 1,
                    (int)Math.Ceiling(
                        totalTurnDeg /
                        CreateTargetTurnPerSpanDeg));

            int pointCount =
                requiredSegments + 1;

            pointCount =
                Math.Max(
                    MinimumPointCount,
                    Math.Min(
                        CreateMaximumPointCount,
                        pointCount));

            plan.PointCount =
                pointCount;

            plan.TotalTurnDeg =
                totalTurnDeg;

            plan.MaxLocalTurnDeg =
                maxLocalTurnDeg;

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] CREATE COMPLEXITY " +
                "tangentSamples=" +
                tangents.Count +
                ", totalTurnDeg=" +
                totalTurnDeg.ToString("0.###", CultureInfo.InvariantCulture) +
                ", maxLocalTurnDeg=" +
                maxLocalTurnDeg.ToString("0.###", CultureInfo.InvariantCulture) +
                ", chosenPoints=" +
                pointCount);

            return plan;
        }

        private static double[] EvaluateEdgeTangent(
            Edge edge,
            double parameter)
        {
            if (edge == null)
                return null;

            try
            {
                double[] values =
                    edge.Evaluate2(
                        parameter,
                        1)
                    as double[];

                if (values == null ||
                    values.Length < 6)
                {
                    return null;
                }

                double x =
                    values[3];

                double y =
                    values[4];

                double z =
                    values[5];

                double length =
                    Math.Sqrt(
                        x * x +
                        y * y +
                        z * z);

                if (length <=
                    1.0e-12)
                {
                    return null;
                }

                return
                    new[]
                    {
                        x / length,
                        y / length,
                        z / length
                    };
            }
            catch
            {
                return null;
            }
        }

        private static bool IsVector3(
            double[] vector)
        {
            return vector != null &&
                   vector.Length >= 3 &&
                   !double.IsNaN(vector[0]) &&
                   !double.IsNaN(vector[1]) &&
                   !double.IsNaN(vector[2]);
        }

        private static double AngleBetweenVectorsDeg(
            double[] a,
            double[] b)
        {
            if (!IsVector3(a) ||
                !IsVector3(b))
            {
                return 0.0;
            }

            double dot =
                a[0] * b[0] +
                a[1] * b[1] +
                a[2] * b[2];

            dot =
                Math.Max(
                    -1.0,
                    Math.Min(
                        1.0,
                        dot));

            return Math.Acos(dot) *
                   180.0 /
                   Math.PI;
        }

        private static int GetCreateDenseValidationSampleCount(
            int pointCount)
        {
            int byPoint =
                Math.Max(
                    1,
                    pointCount) *
                CreateDenseValidationSamplesPerPoint;

            return
                Math.Max(
                    CreateDenseValidationMinimumSamples,
                    Math.Min(
                        CreateDenseValidationMaximumSamples,
                        byPoint));
        }

        private double ProbeTemporarySplineDeviationForTargets(
            ModelDoc2 model,
            List<double[]> targetPoints,
            List<double[]> targetSnapshot,
            out bool cleanupOk)
        {
            cleanupOk = false;

            if (model == null ||
                targetPoints == null ||
                targetPoints.Count < MinimumPointCount ||
                targetSnapshot == null ||
                targetSnapshot.Count < 2)
            {
                return double.MaxValue;
            }

            SketchSegment temporarySegment = null;

            try
            {
                double[] pointData =
                    FlattenPoints(
                        targetPoints);

                object temporaryObject =
                    CreateSplineCompat(
                        model.SketchManager,
                        pointData);

                temporarySegment =
                    temporaryObject as SketchSegment;

                if (temporarySegment == null)
                    return double.MaxValue;

                int denseSamples =
                    GetCreateDenseValidationSampleCount(
                        targetPoints.Count);

                return MeasureActualSplineToEdgeDeviation(
                    null, null, 0, 0, 0,
                    temporarySegment,
                    denseSamples,
                    targetSnapshot);
            }
            finally
            {
                if (temporarySegment != null)
                {
                    cleanupOk =
                        DeleteSingleSketchSegment(
                            model,
                            temporarySegment);
                }
            }
        }

        private static double GetCreateActualSplineToleranceMm(
            double edgeLengthMm)
        {
            double relative =
                edgeLengthMm *
                CreateRelativeSplineToleranceRatio;

            double tolerance =
                Math.Max(
                    CreateBaseSplineToleranceMm,
                    relative);

            return Math.Min(
                CreateMaximumSplineToleranceMm,
                tolerance);
        }

        private double MeasureActualSplineToEdgeDeviation(
            Edge edge,
            Curve edgeCurve,
            double edgeU0,
            double edgeU1,
            double edgeLength,
            SketchSegment splineSegment,
            int sampleCount,
            List<double[]> edgeSnapshot = null)
        {
            if ((edgeSnapshot == null && (edge == null || edgeCurve == null)) ||
                splineSegment == null)
            {
                return double.MaxValue;
            }

            Curve splineCurve =
                null;

            try
            {
                splineCurve =
                    splineSegment.GetCurve()
                    as Curve;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] ACTUAL SPLINE GetCurve failed: " +
                    ex.Message);
            }

            if (splineCurve == null)
                return double.MaxValue;

            double splineU0 =
                0.0;

            double splineU1 =
                0.0;

            bool splineClosed =
                false;

            bool splinePeriodic =
                false;

            bool gotSplineParams =
                false;

            try
            {
                gotSplineParams =
                    splineCurve.GetEndParams(
                        out splineU0,
                        out splineU1,
                        out splineClosed,
                        out splinePeriodic);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] ACTUAL SPLINE GetEndParams failed: " +
                    ex.Message);
            }

            if (!gotSplineParams)
                return double.MaxValue;

            if (splineU1 <
                splineU0)
            {
                double swap =
                    splineU0;

                splineU0 =
                    splineU1;

                splineU1 =
                    swap;
            }

            double splineLength =
                GetCurveLength(
                    splineCurve,
                    splineU0,
                    splineU1);

            if (!IsFinite(
                    splineLength) ||
                splineLength <=
                    1.0e-12)
            {
                return double.MaxValue;
            }

            sampleCount =
                Math.Max(
                    64,
                    sampleCount);

            List<double[]> edgePolyline = edgeSnapshot ??
                BuildCurvePolylineByArcLength(
                    edge,
                    edgeCurve,
                    edgeU0,
                    edgeU1,
                    edgeLength,
                    sampleCount);

            List<double[]> splinePolyline =
                BuildCurvePolylineByArcLength(
                    null,
                    splineCurve,
                    splineU0,
                    splineU1,
                    splineLength,
                    sampleCount);

            if (edgePolyline.Count <
                    2 ||
                splinePolyline.Count <
                    2)
            {
                return double.MaxValue;
            }

            double edgeToSpline =
                GetMaximumPointToPolylineDistance(
                    edgePolyline,
                    splinePolyline);

            double splineToEdge =
                GetMaximumPointToPolylineDistance(
                    splinePolyline,
                    edgePolyline);

            double result =
                Math.Max(
                    edgeToSpline,
                    splineToEdge);

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] ACTUAL SPLINE DEVIATION " +
                "edgeToSplineMm=" +
                (edgeToSpline * 1000.0)
                    .ToString("0.######", CultureInfo.InvariantCulture) +
                ", splineToEdgeMm=" +
                (splineToEdge * 1000.0)
                    .ToString("0.######", CultureInfo.InvariantCulture) +
                ", maxMm=" +
                (result * 1000.0)
                    .ToString("0.######", CultureInfo.InvariantCulture));

            return result;
        }

        private List<double[]> BuildCurvePolylineByArcLength(
            Edge edge,
            Curve curve,
            double u0,
            double u1,
            double totalLength,
            int sampleCount)
        {
            List<double[]> result =
                new List<double[]>();

            if (curve == null ||
                totalLength <=
                    1.0e-12)
            {
                return result;
            }

            for (int i = 0;
                 i <= sampleCount;
                 i++)
            {
                double targetLength =
                    totalLength *
                    i /
                    sampleCount;

                double parameter =
                    FindParameterAtArcLength(
                        curve,
                        u0,
                        u1,
                        totalLength,
                        targetLength);

                double[] point =
                    edge != null
                        ? EvaluateEdgePoint(
                            edge,
                            parameter)
                        : EvaluateCurvePoint(
                            curve,
                            parameter);

                if (IsPoint(
                        point))
                {
                    result.Add(
                        point);
                }
            }

            return result;
        }

        private static double GetMaximumPointToPolylineDistance(
            List<double[]> sourcePoints,
            List<double[]> targetPolyline)
        {
            if (sourcePoints == null ||
                targetPolyline == null ||
                targetPolyline.Count < 2)
            {
                return double.MaxValue;
            }

            double maximum =
                0.0;

            foreach (double[] sourcePoint in
                     sourcePoints)
            {
                double minimum =
                    double.MaxValue;

                for (int i = 0;
                     i < targetPolyline.Count - 1;
                     i++)
                {
                    double distance =
                        DistancePointToSegment3D(
                            sourcePoint,
                            targetPolyline[i],
                            targetPolyline[i + 1]);

                    if (distance <
                        minimum)
                    {
                        minimum =
                            distance;
                    }
                }

                if (minimum >
                    maximum)
                {
                    maximum =
                        minimum;
                }
            }

            return maximum;
        }

        private static bool DeleteSingleSketchSegment(
            ModelDoc2 model,
            SketchSegment segment)
        {
            if (model == null ||
                segment == null)
            {
                return false;
            }

            model.ClearSelection2(true);

            bool selected =
                false;

            try
            {
                selected =
                    segment.Select4(
                        false,
                        null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] CANDIDATE DELETE select failed: " +
                    ex.Message);
            }

            if (!selected)
                return false;

            ModelDocExtension extension =
                model.Extension
                as ModelDocExtension;

            bool deleted =
                false;

            if (extension != null)
            {
                try
                {
                    deleted =
                        extension.DeleteSelection2(
                            0);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] CANDIDATE DELETE DeleteSelection2 failed: " +
                        ex.Message);
                }
            }

            if (!deleted)
            {
                try
                {
                    model.EditDelete();
                    deleted =
                        true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] CANDIDATE DELETE EditDelete failed: " +
                        ex.Message);
                }
            }

            model.ClearSelection2(true);

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] CANDIDATE DELETE result=" +
                deleted);

            return deleted;
        }

        private static Sketch GetSketchFromFeature(
            Feature sketchFeature)
        {
            if (sketchFeature == null)
                return null;

            try
            {
                return
                    sketchFeature.GetSpecificFeature2()
                    as Sketch;
            }
            catch
            {
                return null;
            }
        }

        private static Sketch ExitSame3DSketchForControlFit(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName)
        {
            if (model == null ||
                owningSketchFeature == null)
            {
                throw new InvalidOperationException(
                    "Thiếu SAME 3D Sketch để control-fit.");
            }

            Sketch active =
                model.SketchManager.ActiveSketch
                as Sketch;

            if (active != null)
            {
                Feature activeFeature =
                    FindOwningFeatureForSketch(
                        model,
                        active);

                if (activeFeature == null ||
                    !IsSameComObject(
                        activeFeature,
                        owningSketchFeature))
                {
                    throw new InvalidOperationException(
                        "Đang Edit nhầm Sketch trước control-fit.");
                }

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE CONTROL-FIT EXIT SKETCH BEGIN");

                model.SketchManager.Insert3DSketch(
                    true);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE CONTROL-FIT EXIT SKETCH END");
            }

            if (model.SketchManager.ActiveSketch != null)
            {
                throw new InvalidOperationException(
                    "Không thoát được SAME 3D Sketch trước control-fit.");
            }

            bool rebuilt =
                model.EditRebuild3();

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE CONTROL-FIT PRE-REBUILD " +
                "result=" +
                rebuilt +
                ", sketch=\"" +
                owningSketchName +
                "\"");

            if (!rebuilt) throw new InvalidOperationException("EditRebuild3 returned false before control-fit.");
            Sketch sketch =
                GetSketchFromFeature(
                    owningSketchFeature);

            if (sketch == null ||
                !TryIs3DSketch(
                    sketch))
            {
                throw new InvalidOperationException(
                    "Không lấy lại được SAME 3D Sketch sau khi đóng.");
            }

            return sketch;
        }

        private static int SafeGetSplinePointCount(
            SketchSpline spline)
        {
            if (spline == null)
                return 0;

            try
            {
                return
                    spline.GetPointCount();
            }
            catch
            {
                return 0;
            }
        }

        private static List<double[]> SnapshotSketchPointCoordinates(
            List<SketchPoint> points)
        {
            List<double[]> result =
                new List<double[]>();

            if (points == null)
                return result;

            foreach (SketchPoint point in
                     points)
            {
                if (point == null)
                    continue;

                try
                {
                    result.Add(
                        new double[]
                        {
                            point.X,
                            point.Y,
                            point.Z
                        });
                }
                catch
                {
                }
            }

            return result;
        }

        private static bool ShouldReverseTargetForSameSpline(
            List<double[]> originalFitPointSnapshot,
            Edge targetEdge,
            double targetU0,
            double targetU1)
        {
            if (originalFitPointSnapshot == null ||
                originalFitPointSnapshot.Count < 2 ||
                targetEdge == null)
            {
                return false;
            }

            double[] oldStart =
                originalFitPointSnapshot[0];

            double[] oldEnd =
                originalFitPointSnapshot[
                    originalFitPointSnapshot.Count - 1];

            double[] targetStart =
                EvaluateEdgePoint(
                    targetEdge,
                    targetU0);

            double[] targetEnd =
                EvaluateEdgePoint(
                    targetEdge,
                    targetU1);

            if (!IsPoint(
                    oldStart) ||
                !IsPoint(
                    oldEnd) ||
                !IsPoint(
                    targetStart) ||
                !IsPoint(
                    targetEnd))
            {
                return false;
            }

            double forward =
                Distance(
                    oldStart,
                    targetStart) +
                Distance(
                    oldEnd,
                    targetEnd);

            double reverse =
                Distance(
                    oldStart,
                    targetEnd) +
                Distance(
                    oldEnd,
                    targetStart);

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE TARGET ORIENTATION " +
                "forwardEndpointTravelMm=" +
                (forward * 1000.0)
                    .ToString(
                        "0.######",
                        CultureInfo.InvariantCulture) +
                ", reverseEndpointTravelMm=" +
                (reverse * 1000.0)
                    .ToString(
                        "0.######",
                        CultureInfo.InvariantCulture));

            return
                reverse <
                forward;
        }

        private static List<double[]> CopyOrReversePointList(
            List<double[]> source,
            bool reverse)
        {
            List<double[]> result =
                new List<double[]>();

            if (source == null)
                return result;

            for (int i = 0;
                 i < source.Count;
                 i++)
            {
                int sourceIndex =
                    reverse
                        ? source.Count - 1 - i
                        : i;

                double[] point =
                    source[sourceIndex];

                if (!IsPoint(
                        point))
                {
                    return new List<double[]>();
                }

                result.Add(
                    new double[]
                    {
                        point[0],
                        point[1],
                        point[2]
                    });
            }

            return result;
        }

        private static bool IsEdgeTopologicallyClosed(
            Edge edge)
        {
            if (edge == null)
                return false;

            Vertex start =
                null;

            Vertex end =
                null;

            try
            {
                start =
                    edge.GetStartVertex()
                    as Vertex;

                end =
                    edge.GetEndVertex()
                    as Vertex;
            }
            catch
            {
            }

            // A full periodic edge such as a circle can have no distinct
            // start/end vertex.
            if (start == null &&
                end == null)
            {
                return true;
            }

            if (start == null ||
                end == null)
            {
                return false;
            }

            if (IsSameComObject(
                    start,
                    end))
            {
                return true;
            }

            try
            {
                double[] a =
                    start.GetPoint()
                    as double[];

                double[] b =
                    end.GetPoint()
                    as double[];

                return
                    IsPoint(a) &&
                    IsPoint(b) &&
                    Distance(a, b) <=
                        1.0e-9;
            }
            catch
            {
                return false;
            }
        }

        // LEGACY V34.1 ONLY.
        // V35 MOVE does NOT call this sgMERGEPOINTS route anymore.
        // Kept temporarily so the rest of the historical class can compile
        // unchanged while the new point-on-edge MOVE engine is validated.
        private AnchorFitResult FitSameSplineByFixedAnchors(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName,
            string expectedSplineKey,
            List<double[]> targetPoints)
        {
            AnchorFitResult result =
                new AnchorFitResult();

            result.MaxErrorBeforeCleanup =
                double.MaxValue;

            result.MaxErrorAfterCleanup =
                double.MaxValue;

            if (model == null ||
                owningSketchFeature == null ||
                string.IsNullOrWhiteSpace(
                    expectedSplineKey) ||
                targetPoints == null ||
                targetPoints.Count <
                    MinimumPointCount)
            {
                result.Error =
                    "Thiếu dữ liệu native-merge fit.";

                return result;
            }

            SketchPoint pendingAnchor =
                null;

            SketchPoint pendingSplinePoint =
                null;

            double maximumBeforeCleanup =
                0.0;

            double maximumAfterCleanup =
                0.0;

            try
            {
                for (int i = 0;
                     i < targetPoints.Count;
                     i++)
                {
                    double[] target =
                        targetPoints[i];

                    if (target == null ||
                        target.Length < 3)
                    {
                        result.Error =
                            "Target point không hợp lệ tại index=" +
                            i +
                            ".";

                        return result;
                    }

                    Sketch sketch =
                        EnsureSame3DSketchEditing(
                            model,
                            ReacquireSame3DSketchForReadback(
                                model,
                                owningSketchFeature,
                                owningSketchName,
                                "native merge pre-create " + i),
                            owningSketchFeature,
                            owningSketchName);

                    SketchSegment splineSegment =
                        ReacquireSketchSegmentByKey(
                            sketch,
                            expectedSplineKey);

                    SketchSpline spline =
                        splineSegment
                        as SketchSpline;

                    if (spline == null)
                    {
                        result.Error =
                            "Mất SAME spline trước native merge index=" +
                            i +
                            ".";

                        return result;
                    }

                    string keyBefore =
                        GetSketchSegmentKey(
                            splineSegment);

                    if (!string.Equals(
                            expectedSplineKey,
                            keyBefore,
                            StringComparison.Ordinal))
                    {
                        result.Error =
                            "Spline key sai trước native merge index=" +
                            i +
                            ".";

                        return result;
                    }

                    List<SketchPoint> fitPoints =
                        GetSplineFitPoints(
                            spline);

                    if (fitPoints.Count !=
                        targetPoints.Count)
                    {
                        result.Error =
                            "Fit-point count không khớp trước native merge. " +
                            fitPoints.Count +
                            "/" +
                            targetPoints.Count +
                            ".";

                        return result;
                    }

                    pendingSplinePoint =
                        fitPoints[i];

                    pendingAnchor =
                        model.SketchManager.CreatePoint(
                            target[0],
                            target[1],
                            target[2]);

                    if (pendingAnchor == null)
                    {
                        result.Error =
                            "Không tạo được temporary native-merge anchor index=" +
                            i +
                            ".";

                        return result;
                    }

                    bool fixedOk =
                        AddFixedSketchPoint(
                            model,
                            pendingAnchor);

                    if (!fixedOk)
                    {
                        result.Error =
                            "Không FIX được temporary native-merge anchor index=" +
                            i +
                            ".";

                        return result;
                    }

                    double anchorErrorBefore =
                        DistanceSketchPointToArray(
                            pendingAnchor,
                            target);

                    if (!IsFinite(
                            anchorErrorBefore) ||
                        anchorErrorBefore >
                            5.0e-6)
                    {
                        result.Error =
                            "Native-merge anchor không nằm đúng target trước merge. " +
                            "index=" +
                            i +
                            ", errorMm=" +
                            (anchorErrorBefore * 1000.0)
                                .ToString(
                                    "0.######",
                                    CultureInfo.InvariantCulture);

                        return result;
                    }

                    string mergeError =
                        "";

                    bool mergeCalled =
                        AddNativeMergePoints(
                            model,
                            pendingSplinePoint,
                            pendingAnchor,
                            out mergeError);

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE NATIVE MERGE PRECOMMIT " +
                        "index=" +
                        i +
                        ", called=" +
                        mergeCalled +
                        ", error=" +
                        mergeError);

                    if (!mergeCalled)
                    {
                        result.Error =
                            "sgMERGEPOINTS call thất bại tại index=" +
                            i +
                            ". " +
                            mergeError;

                        return result;
                    }

                    bool rebuildOk =
                        false;

                    try
                    {
                        rebuildOk =
                            model.EditRebuild3();
                    }
                    catch (Exception ex)
                    {
                        result.Error =
                            "EditRebuild3 sau sgMERGEPOINTS exception index=" +
                            i +
                            ": " +
                            ex.Message;

                        return result;
                    }

                    if (!rebuildOk)
                    {
                        result.Error =
                            "EditRebuild3 sau sgMERGEPOINTS returned false index=" +
                            i +
                            ".";

                        return result;
                    }

                    sketch =
                        ReacquireSame3DSketchForReadback(
                            model,
                            owningSketchFeature,
                            owningSketchName,
                            "native merge post-rebuild " + i);

                    splineSegment =
                        ReacquireSketchSegmentByKey(
                            sketch,
                            expectedSplineKey);

                    spline =
                        splineSegment
                        as SketchSpline;

                    if (spline == null)
                    {
                        result.Error =
                            "Mất SAME spline sau sgMERGEPOINTS index=" +
                            i +
                            ".";

                        return result;
                    }

                    string keyAfterMerge =
                        GetSketchSegmentKey(
                            splineSegment);

                    if (!string.Equals(
                            expectedSplineKey,
                            keyAfterMerge,
                            StringComparison.Ordinal))
                    {
                        result.Error =
                            "SAME spline key đổi sau sgMERGEPOINTS index=" +
                            i +
                            ".";

                        return result;
                    }

                    fitPoints =
                        GetSplineFitPoints(
                            spline);

                    if (fitPoints.Count !=
                        targetPoints.Count)
                    {
                        result.Error =
                            "sgMERGEPOINTS làm đổi spline fit-point count. " +
                            fitPoints.Count +
                            "/" +
                            targetPoints.Count +
                            ".";

                        return result;
                    }

                    SketchPoint currentSplinePoint =
                        fitPoints[i];

                    double fitTargetErrorBeforeCleanup =
                        DistanceSketchPointToArray(
                            currentSplinePoint,
                            target);

                    double anchorTargetErrorAfterMerge =
                        DistanceSketchPointToArray(
                            pendingAnchor,
                            target);

                    bool samePointAfterMerge =
                        IsSameSketchPointIdentity(
                            currentSplinePoint,
                            pendingAnchor);

                    bool mergeRelationFound =
                        HasSketchRelationBetweenPointPair(
                            sketch,
                            currentSplinePoint,
                            pendingAnchor,
                            (int)swConstraintType_e
                                .swConstraintType_MERGEPOINTS);

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE NATIVE MERGE RESULT " +
                        "index=" +
                        i +
                        ", sameSplineKey=True" +
                        ", samePointAfterMerge=" +
                        samePointAfterMerge +
                        ", mergeRelationFound=" +
                        mergeRelationFound +
                        ", anchorTargetErrorMm=" +
                        (anchorTargetErrorAfterMerge * 1000.0)
                            .ToString(
                                "0.######",
                                CultureInfo.InvariantCulture) +
                        ", fitTargetErrorMm=" +
                        (fitTargetErrorBeforeCleanup * 1000.0)
                            .ToString(
                                "0.######",
                                CultureInfo.InvariantCulture));

                    if (!IsFinite(
                            fitTargetErrorBeforeCleanup) ||
                        fitTargetErrorBeforeCleanup >
                            5.0e-6)
                    {
                        result.Error =
                            "Native sgMERGEPOINTS không đưa SAME spline fit point " +
                            "tới target. index=" +
                            i +
                            ", fitTargetErrorMm=" +
                            (fitTargetErrorBeforeCleanup * 1000.0)
                                .ToString(
                                    "0.######",
                                    CultureInfo.InvariantCulture);

                        return result;
                    }

                    maximumBeforeCleanup =
                        Math.Max(
                            maximumBeforeCleanup,
                            fitTargetErrorBeforeCleanup);

                    // Re-enter SAME old sketch before removing only the
                    // temporary FIX/MERGE network. Never delete the spline
                    // fit point, even when the temporary anchor was consumed
                    // by a native merge.
                    sketch =
                        EnsureSame3DSketchEditing(
                            model,
                            sketch,
                            owningSketchFeature,
                            owningSketchName);

                    splineSegment =
                        ReacquireSketchSegmentByKey(
                            sketch,
                            expectedSplineKey);

                    spline =
                        splineSegment
                        as SketchSpline;

                    if (spline == null)
                    {
                        result.Error =
                            "Mất SAME spline trước native-merge cleanup index=" +
                            i +
                            ".";

                        return result;
                    }

                    fitPoints =
                        GetSplineFitPoints(
                            spline);

                    if (fitPoints.Count !=
                        targetPoints.Count)
                    {
                        result.Error =
                            "Fit-point count đổi trước native-merge cleanup.";

                        return result;
                    }

                    currentSplinePoint =
                        fitPoints[i];

                    NativeMergeCleanupResult cleanup =
                        CleanupNativeMergeAnchor(
                            model,
                            sketch,
                            currentSplinePoint,
                            pendingAnchor);

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE NATIVE MERGE CLEANUP " +
                        "index=" +
                        i +
                        ", relationDeleted=" +
                        cleanup.RelationsDeleted +
                        ", anchorConsumed=" +
                        cleanup.AnchorConsumedBySplinePoint +
                        ", anchorDeleted=" +
                        cleanup.AnchorDeleted +
                        ", success=" +
                        cleanup.Success +
                        ", error=" +
                        (cleanup.Error ?? ""));

                    if (!cleanup.Success)
                    {
                        result.Error =
                            "Native-merge cleanup thất bại index=" +
                            i +
                            ". " +
                            cleanup.Error;

                        return result;
                    }

                    pendingAnchor =
                        null;

                    pendingSplinePoint =
                        null;

                    result.DeletedAnchorCount++;

                    bool cleanupRebuild =
                        false;

                    try
                    {
                        cleanupRebuild =
                            model.EditRebuild3();
                    }
                    catch (Exception ex)
                    {
                        result.Error =
                            "Rebuild sau native-merge cleanup exception index=" +
                            i +
                            ": " +
                            ex.Message;

                        return result;
                    }

                    if (!cleanupRebuild)
                    {
                        result.Error =
                            "Rebuild sau native-merge cleanup returned false index=" +
                            i +
                            ".";

                        return result;
                    }

                    sketch =
                        ReacquireSame3DSketchForReadback(
                            model,
                            owningSketchFeature,
                            owningSketchName,
                            "native merge post-cleanup " + i);

                    splineSegment =
                        ReacquireSketchSegmentByKey(
                            sketch,
                            expectedSplineKey);

                    spline =
                        splineSegment
                        as SketchSpline;

                    if (spline == null)
                    {
                        result.Error =
                            "Mất SAME spline sau native-merge cleanup index=" +
                            i +
                            ".";

                        return result;
                    }

                    string keyAfterCleanup =
                        GetSketchSegmentKey(
                            splineSegment);

                    if (!string.Equals(
                            expectedSplineKey,
                            keyAfterCleanup,
                            StringComparison.Ordinal))
                    {
                        result.Error =
                            "SAME spline key đổi sau native-merge cleanup index=" +
                            i +
                            ".";

                        return result;
                    }

                    fitPoints =
                        GetSplineFitPoints(
                            spline);

                    if (fitPoints.Count !=
                        targetPoints.Count)
                    {
                        result.Error =
                            "Native-merge cleanup làm đổi fit-point count.";

                        return result;
                    }

                    double fitTargetErrorAfterCleanup =
                        DistanceSketchPointToArray(
                            fitPoints[i],
                            target);

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE NATIVE MERGE POST-CLEANUP " +
                        "index=" +
                        i +
                        ", sameSplineKey=True" +
                        ", fitTargetErrorMm=" +
                        (fitTargetErrorAfterCleanup * 1000.0)
                            .ToString(
                                "0.######",
                                CultureInfo.InvariantCulture));

                    if (!IsFinite(
                            fitTargetErrorAfterCleanup) ||
                        fitTargetErrorAfterCleanup >
                            1.0e-5)
                    {
                        result.Error =
                            "Fit point rời target sau native-merge cleanup. " +
                            "index=" +
                            i +
                            ", errorMm=" +
                            (fitTargetErrorAfterCleanup * 1000.0)
                                .ToString(
                                    "0.######",
                                    CultureInfo.InvariantCulture);

                        return result;
                    }

                    maximumAfterCleanup =
                        Math.Max(
                            maximumAfterCleanup,
                            fitTargetErrorAfterCleanup);

                    // Re-enter SAME sketch for the next point.
                    if (i <
                        targetPoints.Count - 1)
                    {
                        EnsureSame3DSketchEditing(
                            model,
                            sketch,
                            owningSketchFeature,
                            owningSketchName);
                    }

                    // Keep the old result field name for caller compatibility.
                    // It now counts successful native point merges.
                    result.CoincidentRelationCount++;
                }

                result.MaxErrorBeforeCleanup =
                    maximumBeforeCleanup;

                result.MaxErrorAfterCleanup =
                    maximumAfterCleanup;

                result.Success =
                    result.CoincidentRelationCount ==
                    targetPoints.Count;

                if (!result.Success)
                {
                    result.Error =
                        "Native merge hoàn tất không đủ point. " +
                        result.CoincidentRelationCount +
                        "/" +
                        targetPoints.Count +
                        ".";
                }

                return result;
            }
            finally
            {
                // Best-effort cleanup only for a still-pending temporary
                // anchor. This guard never intentionally deletes a spline
                // fit point.
                if (pendingAnchor != null)
                {
                    try
                    {
                        Sketch cleanupSketch =
                            EnsureSame3DSketchEditing(
                                model,
                                ReacquireSame3DSketchForReadback(
                                    model,
                                    owningSketchFeature,
                                    owningSketchName,
                                    "native merge finally cleanup"),
                                owningSketchFeature,
                                owningSketchName);

                        SketchPoint safeSplinePoint =
                            pendingSplinePoint;

                        SketchSegment safeSegment =
                            ReacquireSketchSegmentByKey(
                                cleanupSketch,
                                expectedSplineKey);

                        SketchSpline safeSpline =
                            safeSegment
                            as SketchSpline;

                        if (safeSpline != null)
                        {
                            List<SketchPoint> safePoints =
                                GetSplineFitPoints(
                                    safeSpline);

                            if (safePoints.Count ==
                                targetPoints.Count)
                            {
                                int nearestIndex =
                                    FindNearestSketchPointIndex(
                                        safePoints,
                                        pendingAnchor);

                                if (nearestIndex >= 0)
                                {
                                    safeSplinePoint =
                                        safePoints[
                                            nearestIndex];
                                }
                            }
                        }

                        NativeMergeCleanupResult finalCleanup =
                            CleanupNativeMergeAnchor(
                                model,
                                cleanupSketch,
                                safeSplinePoint,
                                pendingAnchor);

                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE NATIVE MERGE FINALLY CLEANUP " +
                            "success=" +
                            finalCleanup.Success +
                            ", relationsDeleted=" +
                            finalCleanup.RelationsDeleted +
                            ", anchorConsumed=" +
                            finalCleanup.AnchorConsumedBySplinePoint +
                            ", anchorDeleted=" +
                            finalCleanup.AnchorDeleted +
                            ", error=" +
                            (finalCleanup.Error ?? ""));

                        try
                        {
                            model.EditRebuild3();
                        }
                        catch
                        {
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE NATIVE MERGE FINALLY CLEANUP exception=" +
                            ex.Message);
                    }
                }

                try
                {
                    model.ClearSelection2(
                        true);
                }
                catch
                {
                }
            }
        }

        private static bool AddFixedSketchPoint(
            ModelDoc2 model,
            SketchPoint point)
        {
            if (model == null ||
                point == null)
            {
                return false;
            }

            model.ClearSelection2(
                true);

            bool selected =
                false;

            try
            {
                selected =
                    point.Select4(
                        false,
                        null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] ANCHOR FIX select failed: " +
                    ex.Message);
            }

            if (!selected)
            {
                model.ClearSelection2(
                    true);

                return false;
            }

            try
            {
                model.SketchAddConstraints(
                    "sgFIXED");

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] ANCHOR FIX failed: " +
                    ex.Message);

                return false;
            }
            finally
            {
                model.ClearSelection2(
                    true);
            }
        }

        private static bool AddNativeMergePoints(
            ModelDoc2 model,
            SketchPoint splinePoint,
            SketchPoint anchorPoint,
            out string error)
        {
            error =
                "";

            if (model == null ||
                splinePoint == null ||
                anchorPoint == null)
            {
                error =
                    "Thiếu spline point hoặc anchor point.";

                return false;
            }

            try
            {
                model.ClearSelection2(
                    true);

                bool firstSelected =
                    splinePoint.Select4(
                        false,
                        null);

                bool secondSelected =
                    anchorPoint.Select4(
                        true,
                        null);

                if (!firstSelected ||
                    !secondSelected)
                {
                    error =
                        "Select4 failed. first=" +
                        firstSelected +
                        ", second=" +
                        secondSelected;

                    return false;
                }

                // Intentionally use the native sketch command token rather
                // than ISketchRelationManager.AddRelation(type 42).
                //
                // V32 proved GetAllowedRelations rejects MERGEPOINTS for this
                // spline fit point. SketchAddConstraints("sgMERGEPOINTS")
                // goes through SOLIDWORKS' native merge operation instead.
                model.SketchAddConstraints(
                    "sgMERGEPOINTS");

                return true;
            }
            catch (Exception ex)
            {
                error =
                    ex.Message;

                return false;
            }
            finally
            {
                try
                {
                    model.ClearSelection2(
                        true);
                }
                catch
                {
                }
            }
        }

        private static NativeMergeCleanupResult CleanupNativeMergeAnchor(
            ModelDoc2 model,
            Sketch sketch,
            SketchPoint splinePoint,
            SketchPoint anchorPoint)
        {
            NativeMergeCleanupResult result =
                new NativeMergeCleanupResult();

            if (model == null ||
                sketch == null ||
                anchorPoint == null)
            {
                result.Error =
                    "Thiếu dữ liệu cleanup.";

                return result;
            }

            bool samePoint =
                splinePoint != null &&
                IsSameSketchPointIdentity(
                    splinePoint,
                    anchorPoint);

            result.AnchorConsumedBySplinePoint =
                samePoint;

            ISketchRelationManager manager =
                null;

            try
            {
                manager =
                    sketch.RelationManager;
            }
            catch (Exception ex)
            {
                result.Error =
                    "Không lấy được RelationManager: " +
                    ex.Message;

                return result;
            }

            if (manager == null)
            {
                result.Error =
                    "RelationManager null.";

                return result;
            }

            object[] relations =
                null;

            try
            {
                relations =
                    manager.GetRelations(
                        (int)swSketchRelationFilterType_e
                            .swAll)
                    as object[];
            }
            catch (Exception ex)
            {
                result.Error =
                    "GetRelations failed: " +
                    ex.Message;

                return result;
            }

            if (relations != null)
            {
                foreach (object raw in relations)
                {
                    SketchRelation relation =
                        UnwrapDispatchObject(
                            raw)
                        as SketchRelation;

                    if (relation == null)
                        continue;

                    int relationType =
                        -1;

                    try
                    {
                        relationType =
                            relation.GetRelationType();
                    }
                    catch
                    {
                        continue;
                    }

                    bool temporaryType =
                        relationType ==
                            (int)swConstraintType_e
                                .swConstraintType_FIXED ||
                        relationType ==
                            (int)swConstraintType_e
                                .swConstraintType_MERGEPOINTS;

                    if (!temporaryType)
                        continue;

                    object[] entities =
                        GetRelationDefinitionEntities(
                            relation);

                    if (entities == null)
                    {
                        entities =
                            GetRelationEntities(
                                relation);
                    }

                    bool touchesTemporaryPoint =
                        RelationEntitiesContainSketchPoint(
                            entities,
                            anchorPoint);

                    bool touchesSplinePoint =
                        splinePoint != null &&
                        RelationEntitiesContainSketchPoint(
                            entities,
                            splinePoint);

                    if (!touchesTemporaryPoint &&
                        !touchesSplinePoint)
                    {
                        continue;
                    }

                    bool deleted =
                        false;

                    try
                    {
                        deleted =
                            manager.DeleteRelation(
                                relation);
                    }
                    catch
                    {
                        deleted =
                            false;
                    }

                    if (deleted)
                    {
                        result.RelationsDeleted++;
                    }
                }
            }

            if (!samePoint)
            {
                model.ClearSelection2(
                    true);

                bool selected =
                    false;

                try
                {
                    selected =
                        anchorPoint.Select4(
                            false,
                            null);
                }
                catch
                {
                    selected =
                        false;
                }

                if (selected)
                {
                    bool deleted =
                        false;

                    ModelDocExtension extension =
                        model.Extension
                        as ModelDocExtension;

                    if (extension != null)
                    {
                        try
                        {
                            deleted =
                                extension.DeleteSelection2(
                                    0);
                        }
                        catch
                        {
                            deleted =
                                false;
                        }
                    }

                    if (!deleted)
                    {
                        try
                        {
                            model.EditDelete();

                            deleted =
                                true;
                        }
                        catch
                        {
                            deleted =
                                false;
                        }
                    }

                    result.AnchorDeleted =
                        deleted;
                }

                if (!result.AnchorDeleted)
                {
                    result.Error =
                        "Temporary anchor không bị consume và không xóa được.";

                    try
                    {
                        model.ClearSelection2(
                            true);
                    }
                    catch
                    {
                    }

                    return result;
                }
            }
            else
            {
                // The temporary point was absorbed into the spline fit point.
                // Never delete it as sketch geometry.
                result.AnchorDeleted =
                    false;
            }

            try
            {
                model.ClearSelection2(
                    true);
            }
            catch
            {
            }

            result.Success =
                true;

            return result;
        }

        private static bool RelationEntitiesContainSketchPoint(
            object[] entities,
            SketchPoint point)
        {
            if (entities == null ||
                point == null)
            {
                return false;
            }

            foreach (object raw in entities)
            {
                SketchPoint entityPoint =
                    UnwrapDispatchObject(
                        raw)
                    as SketchPoint;

                if (entityPoint != null &&
                    IsSameSketchPointIdentity(
                        entityPoint,
                        point))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSketchRelationBetweenPointPair(
            Sketch sketch,
            SketchPoint first,
            SketchPoint second,
            int relationType)
        {
            if (sketch == null ||
                first == null ||
                second == null)
            {
                return false;
            }

            ISketchRelationManager manager =
                null;

            try
            {
                manager =
                    sketch.RelationManager;
            }
            catch
            {
                return false;
            }

            if (manager == null)
                return false;

            return
                FindVerifiedPointPairRelation(
                    manager,
                    first,
                    second,
                    relationType) !=
                null;
        }

        private static int FindNearestSketchPointIndex(
            List<SketchPoint> points,
            SketchPoint referencePoint)
        {
            if (points == null ||
                points.Count == 0 ||
                referencePoint == null)
            {
                return -1;
            }

            int bestIndex =
                -1;

            double bestDistance =
                double.MaxValue;

            for (int i = 0;
                 i < points.Count;
                 i++)
            {
                double distance =
                    DistanceSketchPoints(
                        points[i],
                        referencePoint);

                if (distance <
                    bestDistance)
                {
                    bestDistance =
                        distance;

                    bestIndex =
                        i;
                }
            }

            return bestIndex;
        }

        private static double[] EvaluateRationalBSplineBasis(
            double parameter,
            int order,
            int controlCount,
            double[] knots,
            double[] rationalWeights,
            bool forceLastControl)
        {
            if (order < 2 ||
                controlCount < order ||
                knots == null ||
                rationalWeights == null ||
                rationalWeights.Length <
                    controlCount ||
                knots.Length <
                    controlCount +
                    order)
            {
                return null;
            }

            if (forceLastControl)
            {
                double[] endpoint =
                    new double[
                        controlCount];

                endpoint[
                    controlCount - 1] =
                    1.0;

                return endpoint;
            }

            int degree =
                order - 1;

            int baseCount =
                knots.Length - 1;

            double[] basis =
                new double[
                    baseCount];

            for (int i = 0;
                 i < baseCount;
                 i++)
            {
                if (parameter >=
                        knots[i] &&
                    parameter <
                        knots[i + 1])
                {
                    basis[i] =
                        1.0;
                }
            }

            for (int level = 1;
                 level <= degree;
                 level++)
            {
                double[] next =
                    new double[
                        basis.Length - 1];

                for (int i = 0;
                     i < next.Length;
                     i++)
                {
                    double left =
                        0.0;

                    double right =
                        0.0;

                    double leftDenominator =
                        knots[
                            i + level] -
                        knots[i];

                    if (Math.Abs(
                            leftDenominator) >
                        1.0e-15)
                    {
                        left =
                            (parameter -
                             knots[i]) /
                            leftDenominator *
                            basis[i];
                    }

                    double rightDenominator =
                        knots[
                            i + level + 1] -
                        knots[
                            i + 1];

                    if (Math.Abs(
                            rightDenominator) >
                        1.0e-15)
                    {
                        right =
                            (knots[
                                 i + level + 1] -
                             parameter) /
                            rightDenominator *
                            basis[
                                i + 1];
                    }

                    next[i] =
                        left +
                        right;
                }

                basis =
                    next;
            }

            if (basis.Length <
                controlCount)
            {
                return null;
            }

            double[] rational =
                new double[
                    controlCount];

            double denominator =
                0.0;

            for (int i = 0;
                 i < controlCount;
                 i++)
            {
                rational[i] =
                    basis[i] *
                    rationalWeights[i];

                denominator +=
                    rational[i];
            }

            if (Math.Abs(
                    denominator) <=
                1.0e-15)
            {
                return null;
            }

            for (int i = 0;
                 i < controlCount;
                 i++)
            {
                rational[i] /=
                    denominator;
            }

            return rational;
        }

        private static double[] SolveDenseLinearSystem(
            double[,] sourceMatrix,
            double[] sourceRhs)
        {
            if (sourceMatrix == null ||
                sourceRhs == null)
            {
                return null;
            }

            int n =
                sourceRhs.Length;

            if (sourceMatrix.GetLength(0) !=
                    n ||
                sourceMatrix.GetLength(1) !=
                    n)
            {
                return null;
            }

            double[,] matrix =
                new double[
                    n,
                    n];

            double[] rhs =
                new double[
                    n];

            for (int row = 0;
                 row < n;
                 row++)
            {
                rhs[row] =
                    sourceRhs[row];

                for (int column = 0;
                     column < n;
                     column++)
                {
                    matrix[
                        row,
                        column] =
                        sourceMatrix[
                            row,
                            column];
                }
            }

            for (int pivotIndex = 0;
                 pivotIndex < n;
                 pivotIndex++)
            {
                int bestRow =
                    pivotIndex;

                double bestMagnitude =
                    Math.Abs(
                        matrix[
                            pivotIndex,
                            pivotIndex]);

                for (int row =
                         pivotIndex + 1;
                     row < n;
                     row++)
                {
                    double magnitude =
                        Math.Abs(
                            matrix[
                                row,
                                pivotIndex]);

                    if (magnitude >
                        bestMagnitude)
                    {
                        bestMagnitude =
                            magnitude;

                        bestRow =
                            row;
                    }
                }

                if (!IsFinite(
                        bestMagnitude) ||
                    bestMagnitude <=
                        1.0e-18)
                {
                    return null;
                }

                if (bestRow !=
                    pivotIndex)
                {
                    for (int column =
                             pivotIndex;
                         column < n;
                         column++)
                    {
                        double swap =
                            matrix[
                                pivotIndex,
                                column];

                        matrix[
                            pivotIndex,
                            column] =
                            matrix[
                                bestRow,
                                column];

                        matrix[
                            bestRow,
                            column] =
                            swap;
                    }

                    double rhsSwap =
                        rhs[pivotIndex];

                    rhs[pivotIndex] =
                        rhs[bestRow];

                    rhs[bestRow] =
                        rhsSwap;
                }

                double pivot =
                    matrix[
                        pivotIndex,
                        pivotIndex];

                for (int row =
                         pivotIndex + 1;
                     row < n;
                     row++)
                {
                    double factor =
                        matrix[
                            row,
                            pivotIndex] /
                        pivot;

                    if (Math.Abs(
                            factor) <=
                        1.0e-30)
                    {
                        continue;
                    }

                    matrix[
                        row,
                        pivotIndex] =
                        0.0;

                    for (int column =
                             pivotIndex + 1;
                         column < n;
                         column++)
                    {
                        matrix[
                            row,
                            column] -=
                            factor *
                            matrix[
                                pivotIndex,
                                column];
                    }

                    rhs[row] -=
                        factor *
                        rhs[pivotIndex];
                }
            }

            double[] solution =
                new double[
                    n];

            for (int row = n - 1;
                 row >= 0;
                 row--)
            {
                double value =
                    rhs[row];

                for (int column =
                         row + 1;
                     column < n;
                     column++)
                {
                    value -=
                        matrix[
                            row,
                            column] *
                        solution[column];
                }

                double diagonal =
                    matrix[
                        row,
                        row];

                if (Math.Abs(
                        diagonal) <=
                    1.0e-18)
                {
                    return null;
                }

                solution[row] =
                    value /
                    diagonal;

                if (!IsFinite(
                        solution[row]))
                {
                    return null;
                }
            }

            return solution;
        }

        private SamplingResult FindMinimumEqualSpacingSampling(
            Edge edge,
            Curve curve,
            double u0,
            double u1,
            double totalLength,
            double tolerance,
            int maximumPointCount)
        {
            maximumPointCount =
                Math.Max(
                    MinimumPointCount,
                    maximumPointCount);

            int minSegments =
                MinimumPointCount - 1;

            int maxSegments =
                maximumPointCount - 1;

            int previousFailedSegments = minSegments - 1;
            int currentSegments = minSegments;
            SamplingResult firstPass = null;

            while (true)
            {
                SamplingResult result = EvaluateSampling(
                    edge, curve, u0, u1, totalLength, currentSegments, tolerance);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] test segments=" + currentSegments +
                    ", points=" + (currentSegments + 1) +
                    ", maxDevMm=" +
                    (result.MaximumDeviation * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", pass=" + result.MeetsTolerance);

                if (result.MeetsTolerance)
                {
                    firstPass = result;
                    break;
                }

                previousFailedSegments = currentSegments;

                if (currentSegments >= maxSegments)
                    return result;

                currentSegments = Math.Min(maxSegments, Math.Max(currentSegments + 1, currentSegments * 2));
            }

            // Refine to the smallest equal-spacing segment count that passes.
            int low = Math.Max(minSegments, previousFailedSegments + 1);
            int high = firstPass.SegmentCount;
            SamplingResult best = firstPass;

            while (low <= high)
            {
                int mid = low + (high - low) / 2;

                SamplingResult candidate = EvaluateSampling(
                    edge, curve, u0, u1, totalLength, mid, tolerance);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] refine segments=" + mid +
                    ", maxDevMm=" +
                    (candidate.MaximumDeviation * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", pass=" + candidate.MeetsTolerance);

                if (candidate.MeetsTolerance)
                {
                    best = candidate;
                    high = mid - 1;
                }
                else
                {
                    low = mid + 1;
                }
            }

            return best;
        }

        private SamplingResult EvaluateSampling(
            Edge edge,
            Curve curve,
            double u0,
            double u1,
            double totalLength,
            int segmentCount,
            double tolerance)
        {
            segmentCount = Math.Max(1, segmentCount);
            double equalArcStep = totalLength / segmentCount;

            var points = new List<double[]>(segmentCount + 1);
            var parameters = new List<double>(segmentCount + 1);

            for (int i = 0; i <= segmentCount; i++)
            {
                double targetLength =
                    i == 0 ? 0.0 :
                    i == segmentCount ? totalLength :
                    equalArcStep * i;

                double parameter = FindParameterAtArcLength(
                    curve, u0, u1, totalLength, targetLength);

                double[] point = EvaluateEdgePoint(edge, parameter);
                if (!IsPoint(point))
                    throw new InvalidOperationException("Không evaluate được point trên Edge.");

                parameters.Add(parameter);
                points.Add(point);
            }

            // Conservative geometry check:
            // compare real Edge points to the chord between adjacent equal-arc points.
            // This selects a practical point count before CreateSpline.
            double maxDeviation = 0.0;

            for (int segment = 0; segment < segmentCount; segment++)
            {
                double startLength = equalArcStep * segment;
                double endLength =
                    segment == segmentCount - 1
                        ? totalLength
                        : equalArcStep * (segment + 1);

                double segmentArcLength = endLength - startLength;
                double[] a = points[segment];
                double[] b = points[segment + 1];

                foreach (double fraction in DeviationSamples)
                {
                    double targetLength = startLength + segmentArcLength * fraction;
                    double parameter = FindParameterAtArcLength(
                        curve, u0, u1, totalLength, targetLength);

                    double[] realPoint = EvaluateEdgePoint(edge, parameter);
                    if (!IsPoint(realPoint))
                        continue;

                    double deviation = DistancePointToSegment3D(realPoint, a, b);
                    if (deviation > maxDeviation)
                        maxDeviation = deviation;
                }
            }

            return new SamplingResult
            {
                SegmentCount = segmentCount,
                Points = points,
                Parameters = parameters,
                MaximumDeviation = maxDeviation,
                MeetsTolerance = maxDeviation <= tolerance + 1.0e-12
            };
        }

        // ------------------------------------------------------------
        // V36.1 MOVE EQUAL-CHORD PRE-SOLVER
        //
        // Equal arc-length and Equal helper-line length are not the same on
        // a curved Edge.  If native Equal is added only after equal-ARC
        // targets are frozen, SOLIDWORKS must redistribute the fit points at
        // the same time it is solving the point-on-Edge/end-point network.
        // That is what produced the small relation warnings seen in V36.0.
        //
        // Use the already valid equal-arc sampling only as an INITIAL GUESS.
        // Then relax the interior curve parameters until every straight chord
        // between consecutive points has the same length.  End parameters are
        // never moved, so the first/last point remain at the Edge vertices.
        //
        // The returned XYZ values are evaluated on the REAL Edge curve, not
        // on a polyline approximation.  Native Equal is still applied later;
        // this just gives the native solver a near-zero-residual starting
        // state so Equal does not need to fight the other relations.
        // ------------------------------------------------------------
        private static List<double[]> BuildEqualChordTargetsFromSampling(
            Curve curve,
            SamplingResult seed,
            out double minimumChord,
            out double maximumChord,
            out int relaxationPasses)
        {
            minimumChord =
                double.MaxValue;

            maximumChord =
                0.0;

            relaxationPasses =
                0;

            List<double[]> empty =
                new List<double[]>();

            if (curve == null ||
                seed == null ||
                seed.Parameters == null ||
                seed.Parameters.Count < 2)
            {
                return empty;
            }

            int pointCount =
                seed.Parameters.Count;

            List<double> parameters =
                new List<double>(
                    pointCount);

            for (int i = 0;
                 i < pointCount;
                 i++)
            {
                parameters.Add(
                    seed.Parameters[i]);
            }

            // Keep the parameter order strictly increasing.  EvaluateSampling
            // already returns ordered values, but normalize defensively.
            if (parameters[parameters.Count - 1] <
                parameters[0])
            {
                parameters.Reverse();
            }

            double domain =
                Math.Abs(
                    parameters[parameters.Count - 1] -
                    parameters[0]);

            if (!IsFinite(domain) ||
                domain <= 1.0e-14)
            {
                return empty;
            }

            double parameterGap =
                Math.Max(
                    domain * 1.0e-12,
                    1.0e-14);

            // A 2-point chain is already trivially Equal.
            if (pointCount > 2)
            {
                for (int pass = 0;
                     pass < MoveEqualChordRelaxationPasses;
                     pass++)
                {
                    // Forward Gauss-Seidel sweep.  For each interior point,
                    // solve |Pi-1,Pi| = |Pi,Pi+1| with its two neighbours
                    // temporarily fixed.
                    for (int i = 1;
                         i < pointCount - 1;
                         i++)
                    {
                        RelaxEqualChordParameter(
                            curve,
                            parameters,
                            i,
                            parameterGap);
                    }

                    // Reverse sweep reduces directional bias and converges the
                    // whole chain much faster on strongly varying curvature.
                    for (int i = pointCount - 2;
                         i >= 1;
                         i--)
                    {
                        RelaxEqualChordParameter(
                            curve,
                            parameters,
                            i,
                            parameterGap);
                    }

                    List<double[]> passPoints =
                        EvaluateCurvePointsAtParameters(
                            curve,
                            parameters);

                    double passMin;
                    double passMax;

                    MeasureChordRange(
                        passPoints,
                        out passMin,
                        out passMax);

                    relaxationPasses =
                        pass + 1;

                    if (!IsFinite(passMin) ||
                        !IsFinite(passMax))
                    {
                        break;
                    }

                    double average =
                        0.5 *
                        (passMin + passMax);

                    double spread =
                        passMax - passMin;

                    double allowedSpread =
                        Math.Max(
                            MoveEqualChordAbsoluteSpreadToleranceM,
                            Math.Abs(average) *
                                MoveEqualChordRelativeSpreadTolerance);

                    if (spread <=
                        allowedSpread)
                    {
                        break;
                    }
                }
            }

            List<double[]> result =
                EvaluateCurvePointsAtParameters(
                    curve,
                    parameters);

            MeasureChordRange(
                result,
                out minimumChord,
                out maximumChord);

            return result;
        }

        private static void RelaxEqualChordParameter(
            Curve curve,
            List<double> parameters,
            int index,
            double parameterGap)
        {
            if (curve == null ||
                parameters == null ||
                index <= 0 ||
                index >= parameters.Count - 1)
            {
                return;
            }

            double low =
                parameters[index - 1] +
                parameterGap;

            double high =
                parameters[index + 1] -
                parameterGap;

            if (!(high > low))
                return;

            double[] leftPoint =
                EvaluateCurvePoint(
                    curve,
                    parameters[index - 1]);

            double[] rightPoint =
                EvaluateCurvePoint(
                    curve,
                    parameters[index + 1]);

            if (!IsPoint(leftPoint) ||
                !IsPoint(rightPoint))
            {
                return;
            }

            for (int iteration = 0;
                 iteration < MoveEqualChordBisectionIterations;
                 iteration++)
            {
                double mid =
                    0.5 *
                    (low + high);

                double[] midPoint =
                    EvaluateCurvePoint(
                        curve,
                        mid);

                if (!IsPoint(midPoint))
                    break;

                double leftChord =
                    DistanceBetweenArrays(
                        leftPoint,
                        midPoint);

                double rightChord =
                    DistanceBetweenArrays(
                        midPoint,
                        rightPoint);

                if (!IsFinite(leftChord) ||
                    !IsFinite(rightChord))
                {
                    break;
                }

                // Moving toward the previous point shortens the left chord and
                // lengthens the right chord.
                if (leftChord >
                    rightChord)
                {
                    high = mid;
                }
                else
                {
                    low = mid;
                }
            }

            parameters[index] =
                0.5 *
                (low + high);
        }

        private static List<double[]> EvaluateCurvePointsAtParameters(
            Curve curve,
            List<double> parameters)
        {
            List<double[]> result =
                new List<double[]>();

            if (curve == null ||
                parameters == null)
            {
                return result;
            }

            foreach (double parameter in
                     parameters)
            {
                double[] point =
                    EvaluateCurvePoint(
                        curve,
                        parameter);

                if (!IsPoint(point))
                {
                    return new List<double[]>();
                }

                result.Add(
                    point);
            }

            return result;
        }

        private static void MeasureChordRange(
            List<double[]> points,
            out double minimumChord,
            out double maximumChord)
        {
            minimumChord =
                double.MaxValue;

            maximumChord =
                0.0;

            if (points == null ||
                points.Count < 2)
            {
                return;
            }

            for (int i = 0;
                 i < points.Count - 1;
                 i++)
            {
                double length =
                    DistanceBetweenArrays(
                        points[i],
                        points[i + 1]);

                if (!IsFinite(length))
                    continue;

                minimumChord =
                    Math.Min(
                        minimumChord,
                        length);

                maximumChord =
                    Math.Max(
                        maximumChord,
                        length);
            }
        }

        private static double GetRelativeChordSpread(
            double minimumChord,
            double maximumChord)
        {
            if (!IsFinite(minimumChord) ||
                !IsFinite(maximumChord) ||
                minimumChord <= 0.0 ||
                maximumChord <= 0.0 ||
                maximumChord < minimumChord)
            {
                return double.MaxValue;
            }

            double meanChord =
                0.5 * (minimumChord + maximumChord);

            if (meanChord <= 1.0e-12)
            {
                return double.MaxValue;
            }

            return
                (maximumChord - minimumChord) /
                meanChord;
        }

        private static double DistanceBetweenArrays(
            double[] first,
            double[] second)
        {
            if (!IsPoint(first) ||
                !IsPoint(second))
            {
                return double.MaxValue;
            }

            double dx =
                first[0] - second[0];

            double dy =
                first[1] - second[1];

            double dz =
                first[2] - second[2];

            return Math.Sqrt(
                dx * dx +
                dy * dy +
                dz * dz);
        }

        /// <summary>
        /// Equal arc-length point solver.
        /// Do NOT replace this with equal-U spacing.
        /// </summary>
        private double FindParameterAtArcLength(
            Curve curve,
            double u0,
            double u1,
            double totalLength,
            double targetLength)
        {
            if (targetLength <= 0.0)
                return u0;
            if (targetLength >= totalLength)
                return u1;

            double low = u0;
            double high = u1;

            for (int i = 0; i < ArcLengthSolveIterations; i++)
            {
                double mid = 0.5 * (low + high);
                double length = GetCurveLength(curve, u0, mid);

                if (length < targetLength)
                    low = mid;
                else
                    high = mid;
            }

            return 0.5 * (low + high);
        }

        private static double GetCurveLength(Curve curve, double start, double end)
        {
            if (curve == null) throw new InvalidOperationException("Curve null.");
            double low = Math.Min(start, end), high = Math.Max(start, end);
            double length = GeometryCall("ICurve.GetLength3", () => Math.Abs(curve.GetLength3(low, high)));
            if (!IsFinite(length) || (high > low && length <= 0.0))
                throw new InvalidOperationException("ICurve.GetLength3: invalid length.");
            return length;
        }

        private static T GeometryCall<T>(string api, Func<T> action)
        {
            try { return action(); }
            catch (COMException ex)
            {
                // Do not query COM again while reporting a failed native call.
                string message = api + " HRESULT=0x" + ex.ErrorCode.ToString("X8") + " " + ex.Message;
                Debug.WriteLine("[EDGE EQUAL SPLINE] COM STOP " + message);
                throw new InvalidOperationException(message, ex);
            }
        }

        private static double[] EvaluateEdgePoint(Edge edge, double parameter)
        {
            double[] values = GeometryCall("IEdge.Evaluate2", () => edge.Evaluate2(parameter, 0) as double[]);
            if (!IsPoint(values)) throw new InvalidOperationException("IEdge.Evaluate2: invalid XYZ.");
            return new[] { values[0], values[1], values[2] };
        }

        private static Edge ReacquireTargetEdge(ModelDoc2 model, byte[] reference)
        {
            int status = -1;
            Edge edge = GeometryCall("IModelDocExtension.GetObjectByPersistReference3(target)",
                () => model.Extension.GetObjectByPersistReference3(reference, out status) as Edge);
            if (edge == null || status != 0)
                throw new InvalidOperationException("Target Edge persistent reference unavailable; status=" + status);
            return edge;
        }

        private static double[] InterpolateArcSnapshot(List<double[]> points, double fraction)
        {
            double index = Math.Max(0.0, Math.Min(1.0, fraction)) * (points.Count - 1);
            int i = Math.Min(points.Count - 2, (int)index);
            double t = index - i;
            return new[] { points[i][0] * (1-t) + points[i+1][0] * t,
                points[i][1] * (1-t) + points[i+1][1] * t,
                points[i][2] * (1-t) + points[i+1][2] * t };
        }

        private static HashSet<string> CaptureSketchPointIds(
            Sketch sketch)
        {
            HashSet<string> ids =
                new HashSet<string>(
                    StringComparer.Ordinal);

            if (sketch == null)
                return ids;

            object[] objects = null;

            try
            {
                objects =
                    sketch.GetSketchPoints2()
                    as object[];
            }
            catch
            {
            }

            if (objects == null)
                return ids;

            foreach (object obj in objects)
            {
                SketchPoint point =
                    obj as SketchPoint;

                if (point == null)
                    continue;

                string key =
                    GetSketchPointKey(point);

                if (!string.IsNullOrWhiteSpace(key))
                    ids.Add(key);
            }

            return ids;
        }

        private static List<SketchPoint> FindOrderedNewFitPoints(
            Sketch sketch,
            HashSet<string> pointIdsBefore,
            List<double[]> targetPoints)
        {
            List<SketchPoint> newPoints =
                new List<SketchPoint>();

            if (sketch == null ||
                targetPoints == null)
            {
                return newPoints;
            }

            object[] objects = null;

            try
            {
                objects =
                    sketch.GetSketchPoints2()
                    as object[];
            }
            catch
            {
            }

            if (objects == null)
                return newPoints;

            foreach (object obj in objects)
            {
                SketchPoint point =
                    obj as SketchPoint;

                if (point == null)
                    continue;

                string key =
                    GetSketchPointKey(point);

                if (pointIdsBefore != null &&
                    pointIdsBefore.Contains(key))
                {
                    continue;
                }

                newPoints.Add(point);
            }

            // GetSketchPoints2 không bảo đảm trả đúng thứ tự spline.
            // Match từng point mới với tọa độ sampling gần nhất.
            List<SketchPoint> ordered =
                new List<SketchPoint>();

            HashSet<int> used =
                new HashSet<int>();

            foreach (double[] target in targetPoints)
            {
                int bestIndex =
                    -1;

                double bestDistance =
                    double.MaxValue;

                for (int i = 0;
                     i < newPoints.Count;
                     i++)
                {
                    if (used.Contains(i))
                        continue;

                    SketchPoint point =
                        newPoints[i];

                    double dx =
                        point.X - target[0];

                    double dy =
                        point.Y - target[1];

                    double dz =
                        point.Z - target[2];

                    double distance =
                        Math.Sqrt(
                            dx * dx +
                            dy * dy +
                            dz * dz);

                    if (distance < bestDistance)
                    {
                        bestDistance =
                            distance;

                        bestIndex =
                            i;
                    }
                }

                if (bestIndex >= 0 &&
                    bestDistance <= 0.001)
                {
                    used.Add(bestIndex);
                    ordered.Add(newPoints[bestIndex]);
                }
            }

            return ordered;
        }

        private static EqualRelationResult BuildEqualSpacingRelations(
            ISldWorks app,
            ModelDoc2 model,
            Sketch activeSketch,
            Edge edge,
            List<SketchPoint> fitPoints,
            bool allowUiEqual = true)
        {
            EqualRelationResult result =
                new EqualRelationResult();

            if (model == null ||
                activeSketch == null ||
                edge == null ||
                fitPoints == null ||
                fitPoints.Count < 2)
            {
                return result;
            }

            // ------------------------------------------------------------
            // 1. Mỗi fit point phải nằm trên Edge gốc.
            //    Đây là relation hình học, KHÔNG phải FIX.
            // ------------------------------------------------------------
            foreach (SketchPoint point in fitPoints)
            {
                model.ClearSelection2(true);

                bool pointSelected =
                    SelectComEntity(
                        point,
                        false);

                bool edgeSelected =
                    SelectComEntity(
                        edge,
                        true);

                if (!pointSelected ||
                    !edgeSelected)
                {
                    continue;
                }

                try
                {
                    model.SketchAddConstraints(
                        "sgCOINCIDENT");

                    result.PointOnEdgeCount++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "point-edge coincident failed: " +
                        ex.Message);
                }
            }

            // ------------------------------------------------------------
            // 2. Neo point đầu/cuối vào Vertex đầu/cuối của Edge.
            //    Nếu không có bước này, cả hệ Equal vẫn có thể trượt dọc Edge.
            // ------------------------------------------------------------
            Vertex startVertex =
                null;

            Vertex endVertex =
                null;

            try
            {
                startVertex =
                    edge.GetStartVertex()
                    as Vertex;

                endVertex =
                    edge.GetEndVertex()
                    as Vertex;
            }
            catch
            {
            }

            if (startVertex != null &&
                endVertex != null)
            {
                SketchPoint first =
                    fitPoints[0];

                SketchPoint last =
                    fitPoints[
                        fitPoints.Count - 1];

                double firstToStart =
                    DistanceSketchPointToVertex(
                        first,
                        startVertex);

                double firstToEnd =
                    DistanceSketchPointToVertex(
                        first,
                        endVertex);

                if (firstToStart <= firstToEnd)
                {
                    if (AddCoincident(
                            model,
                            first,
                            startVertex))
                    {
                        result.EndpointCoincidentCount++;
                    }

                    if (AddCoincident(
                            model,
                            last,
                            endVertex))
                    {
                        result.EndpointCoincidentCount++;
                    }
                }
                else
                {
                    if (AddCoincident(
                            model,
                            first,
                            endVertex))
                    {
                        result.EndpointCoincidentCount++;
                    }

                    if (AddCoincident(
                            model,
                            last,
                            startVertex))
                    {
                        result.EndpointCoincidentCount++;
                    }
                }
            }

            // ------------------------------------------------------------
            // 3. Nối từng cặp fit point bằng construction line.
            //    Equal relation trong SolidWorks áp lên ENTITY length,
            //    nên phải có line để tạo dấu "=" như user yêu cầu.
            // ------------------------------------------------------------
            List<SketchSegment> constructionLines =
                new List<SketchSegment>();

            for (int i = 0;
                 i < fitPoints.Count - 1;
                 i++)
            {
                SketchPoint a =
                    fitPoints[i];

                SketchPoint b =
                    fitPoints[i + 1];

                SketchSegment line =
                    null;

                try
                {
                    line =
                        model.SketchManager.CreateLine(
                            a.X,
                            a.Y,
                            a.Z,
                            b.X,
                            b.Y,
                            b.Z)
                        as SketchSegment;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "Create construction line failed i=" +
                        i +
                        ": " +
                        ex.Message);
                }

                if (line == null)
                    continue;

                try
                {
                    line.ConstructionGeometry =
                        true;
                }
                catch
                {
                }

                constructionLines.Add(line);

                // Explicitly tie line endpoints to spline fit points.
                // Strongly typed: no dynamic COM dispatch.
                try
                {
                    SketchLine sketchLine =
                        line as SketchLine;

                    SketchPoint lineStart =
                        sketchLine != null
                            ? sketchLine.GetStartPoint2() as SketchPoint
                            : null;

                    SketchPoint lineEnd =
                        sketchLine != null
                            ? sketchLine.GetEndPoint2() as SketchPoint
                            : null;

                    if (lineStart != null)
                    {
                        AddCoincident(
                            model,
                            lineStart,
                            a);
                    }

                    if (lineEnd != null)
                    {
                        AddCoincident(
                            model,
                            lineEnd,
                            b);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "line-point coincident failed i=" +
                        i +
                        ": " +
                        ex.Message);
                }
            }

            result.ConstructionLineCount =
                constructionLines.Count;

            result.CreatedConstructionLines =
                constructionLines;

            // ------------------------------------------------------------
            // 4. REAL EQUAL-LENGTH RELATION.
            //
            // V4/V5 used:
            //     model.SketchAddConstraints("sgEQUAL")
            //
            // SketchAddConstraints is void, so "no exception" does NOT prove
            // a relation was actually created. In a 3D sketch SolidWorks can
            // silently ignore the request.
            //
            // Use the native SketchRelationManager instead. Equal length is
            // swConstraintType_SAMELENGTH (14).
            // ------------------------------------------------------------
            if (allowUiEqual &&
                constructionLines.Count >= 2)
            {
                ISketchRelationManager relationManager =
                    null;

                try
                {
                    relationManager =
                        activeSketch.RelationManager;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "RelationManager access failed: " +
                        ex.Message);
                }

                if (relationManager != null)
                {
                    int sameLengthType =
                        (int)swConstraintType_e
                            .swConstraintType_SAMELENGTH;

                    object[] firstPair =
                        new object[]
                        {
                            constructionLines[0],
                            constructionLines[1]
                        };

                    bool sameLengthAllowed =
                        IsConstraintTypeAllowed(
                            relationManager,
                            firstPair,
                            sameLengthType);

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "SAMELENGTH allowed(firstPair)=" +
                        sameLengthAllowed +
                        " type=" +
                        sameLengthType);

                    // Preferred path:
                    // create ONE native relation containing ALL construction
                    // lines, equivalent to selecting every line in the UI and
                    // pressing Equal.
                    object[] allLines =
                        new object[
                            constructionLines.Count];

                    for (int i = 0;
                         i < constructionLines.Count;
                         i++)
                    {
                        allLines[i] =
                            constructionLines[i];
                    }

                    SketchRelation equalRelation =
                        null;

                    try
                    {
                        equalRelation =
                            relationManager.AddRelation(
                                allLines,
                                sameLengthType);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] " +
                            "AddRelation(SAMELENGTH all) failed: " +
                            ex.Message);
                    }

                    if (equalRelation != null)
                    {
                        int actualType =
                            -1;

                        try
                        {
                            actualType =
                                equalRelation
                                    .GetRelationType();
                        }
                        catch
                        {
                        }

                        result.EqualRelationObjectCount =
                            1;

                        result.EqualConstrainedLineCount =
                            constructionLines.Count;

                        result.EqualVerified =
                            actualType ==
                            sameLengthType;

                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] " +
                            "REAL EQUAL relation created " +
                            "mode=ALL_LINES " +
                            "relationType=" +
                            actualType +
                            " expected=" +
                            sameLengthType);
                    }
                    else
                    {
                        // Fallback:
                        // explicitly create SAMELENGTH master-to-each-line.
                        SketchSegment master =
                            constructionLines[0];

                        int createdRelations =
                            0;

                        HashSet<int> constrainedIndexes =
                            new HashSet<int>();

                        constrainedIndexes.Add(0);

                        for (int i = 1;
                             i < constructionLines.Count;
                             i++)
                        {
                            object[] pair =
                                new object[]
                                {
                                    master,
                                    constructionLines[i]
                                };

                            SketchRelation pairRelation =
                                null;

                            try
                            {
                                pairRelation =
                                    relationManager.AddRelation(
                                        pair,
                                        sameLengthType);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine(
                                    "[EDGE EQUAL SPLINE] " +
                                    "AddRelation(SAMELENGTH pair) " +
                                    "failed i=" +
                                    i +
                                    ": " +
                                    ex.Message);
                            }

                            if (pairRelation == null)
                                continue;

                            int actualType =
                                -1;

                            try
                            {
                                actualType =
                                    pairRelation
                                        .GetRelationType();
                            }
                            catch
                            {
                            }

                            if (actualType ==
                                sameLengthType)
                            {
                                createdRelations++;
                                constrainedIndexes.Add(i);
                            }
                        }

                        result.EqualRelationObjectCount =
                            createdRelations;

                        result.EqualConstrainedLineCount =
                            constrainedIndexes.Count;

                        result.EqualVerified =
                            constrainedIndexes.Count ==
                            constructionLines.Count;

                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] " +
                            "REAL EQUAL relation fallback " +
                            "mode=PAIRWISE " +
                            "relationObjects=" +
                            createdRelations +
                            ", constrainedLines=" +
                            constrainedIndexes.Count +
                            "/" +
                            constructionLines.Count);
                    }
                }
            }

            // ------------------------------------------------------------
            // 5. UI-COMMAND FALLBACK FOR A REAL "=" RELATION.
            //
            // Some SOLIDWORKS versions reject AddRelation(SAMELENGTH) through
            // ISketchRelationManager even though the normal UI command may
            // still be enabled for the selected lines.
            //
            // Command 1721 = Sketch > Add Relations > Equal.
            // We verify success by counting REAL relation objects of type 14
            // before and after RunCommand.
            // ------------------------------------------------------------
            if (allowUiEqual &&
                !result.EqualVerified &&
                constructionLines.Count >= 2)
            {
                UiEqualAttempt uiAttempt =
                    TryCreateEqualRelationWithUiCommand(
                        app,
                        model,
                        activeSketch,
                        constructionLines);

                result.UiEqualCommandEnabled =
                    uiAttempt.CommandEnabled;

                result.UiEqualCommandRan =
                    uiAttempt.CommandRan;

                result.UiEqualRelationsAdded =
                    uiAttempt.RelationsAdded;

                if (uiAttempt.Verified)
                {
                    result.EqualVerified =
                        true;

                    result.EqualRelationObjectCount =
                        uiAttempt.RelationsAdded;

                    result.EqualConstrainedLineCount =
                        constructionLines.Count;

                    result.EqualFallbackMode =
                        "UI_EQUAL_RELATION";

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "REAL EQUAL VERIFIED via UI command " +
                        "relationsAdded=" +
                        uiAttempt.RelationsAdded);
                }
            }

            // ------------------------------------------------------------
            // 6. LAST RESORT FOR 3D SKETCHES:
            //    dimension/equation network.
            //
            // If SOLIDWORKS itself reports that Equal is not available for
            // this 3D entity set, a native "=" relation cannot be forced.
            // In that case create:
            //
            //   L1 = reference dimension
            //   L2..Ln = driving dimensions
            //   equation Di = D1
            //
            // This imposes the same mathematical requirement (N-1 equal
            // length equations) without using FIX. It is intentionally kept
            // separate from EqualVerified because it is NOT the "=" glyph.
            // ------------------------------------------------------------
            if (allowUiEqual &&
                !result.EqualVerified &&
                constructionLines.Count >= 2)
            {
                EqualDimensionFallback dimensionFallback =
                    ApplyEqualLengthDimensionFallback(
                        model,
                        activeSketch,
                        constructionLines);

                result.DimensionCount =
                    dimensionFallback.DimensionCount;

                result.LinkedEquationCount =
                    dimensionFallback.LinkedEquationCount;

                result.FixedValueDimensionCount =
                    dimensionFallback.FixedValueDimensionCount;

                result.FallbackVerified =
                    dimensionFallback.Verified;

                result.EqualFallbackMode =
                    dimensionFallback.Mode;

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "EQUAL DIM FALLBACK " +
                    "mode=" +
                    dimensionFallback.Mode +
                    ", dimensions=" +
                    dimensionFallback.DimensionCount +
                    ", equations=" +
                    dimensionFallback.LinkedEquationCount +
                    ", fixedValueDims=" +
                    dimensionFallback.FixedValueDimensionCount +
                    ", verified=" +
                    dimensionFallback.Verified);
            }

            // Compatibility metric:
            // N equal lines mathematically represent N-1 independent
            // equal-length equations.
            result.EqualRelationCount =
                Math.Max(
                    0,
                    result.EqualConstrainedLineCount - 1);

            if (!result.UiEqualCommandRan)
            {
                model.ClearSelection2(true);
            }
            else
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "POST UI EQUAL selection clear deferred");
            }

            return result;
        }

        private static void ScheduleManagedCompletionNotice(
            string message)
        {
            if (string.IsNullOrWhiteSpace(
                    message))
            {
                return;
            }

            System.Windows.Forms.Timer timer =
                new System.Windows.Forms.Timer();

            timer.Interval =
                CompletionNoticeDelayMs;

            EventHandler handler =
                null;

            handler =
                delegate (object sender, EventArgs args)
                {
                    timer.Stop();
                    timer.Tick -= handler;

                    lock (CompletionNoticeTimers)
                    {
                        CompletionNoticeTimers.Remove(
                            timer);
                    }

                    timer.Dispose();

                    try
                    {
                        MessageBox.Show(
                            message,
                            "AUTO SPLINE",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] COMPLETION NOTICE SHOWN");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] COMPLETION NOTICE ERROR: " +
                            ex.Message);
                    }
                };

            timer.Tick +=
                handler;

            lock (CompletionNoticeTimers)
            {
                CompletionNoticeTimers.Add(
                    timer);
            }

            timer.Start();

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] COMPLETION NOTICE SCHEDULED");
        }

        private static void ScheduleDeferredFinalSelectionCleanup(
            ModelDoc2 model)
        {
            if (model == null)
                return;

            System.Windows.Forms.Timer cleanupTimer =
                new System.Windows.Forms.Timer();

            cleanupTimer.Interval =
                DeferredFinalSelectionCleanupMs;

            EventHandler cleanupHandler = null;

            cleanupHandler =
                delegate (object sender, EventArgs args)
                {
                    // Make the timer one-shot BEFORE touching SOLIDWORKS.
                    cleanupTimer.Stop();
                    cleanupTimer.Tick -= cleanupHandler;

                    lock (DeferredEqualTimers)
                    {
                        DeferredEqualTimers.Remove(cleanupTimer);
                    }

                    cleanupTimer.Dispose();

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V36.7 FINAL SELECTION CLEANUP BEGIN " +
                        "delayMs=" + DeferredFinalSelectionCleanupMs);

                    try
                    {
                        // Do not rebuild, scan relations, redraw, or leave/re-enter
                        // the sketch here.  The only purpose is to release the two
                        // helper lines selected by the final RunCommand(1721).
                        model.ClearSelection2(true);

                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V36.7 FINAL SELECTION CLEANUP END " +
                            "cleared=True, noRebuild=True, noRelationScan=True");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V36.7 FINAL SELECTION CLEANUP ERROR " +
                            ex.Message);
                    }
                };

            cleanupTimer.Tick += cleanupHandler;

            lock (DeferredEqualTimers)
            {
                DeferredEqualTimers.Add(cleanupTimer);
            }

            cleanupTimer.Start();

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V36.7 FINAL SELECTION CLEANUP SCHEDULED " +
                "delayMs=" + DeferredFinalSelectionCleanupMs);
        }

        private bool ScheduleDeferredMoveEqual(
            ModelDoc2 model,
            Sketch sketch,
            Feature sketchFeature,
            string sketchName,
            List<SketchSegment> helperLines,
            int oldPointCount,
            int finalPointCount,
            double finalDeviation,
            double toleranceMm)
        {
            if (model == null ||
                sketchFeature == null ||
                helperLines == null ||
                helperLines.Count < 2)
            {
                return false;
            }

            List<string> helperKeys =
                new List<string>();

            for (int i = 0;
                 i < helperLines.Count;
                 i++)
            {
                string key =
                    GetSketchSegmentKey(
                        helperLines[i]);

                if (string.IsNullOrEmpty(key))
                    return false;

                helperKeys.Add(key);
            }

            // V36.3:
            // Apply Equal incrementally with a stable master line:
            // L0=L1, L0=L2, ... Each pair runs in its own message-loop tick.
            int pairIndex =
                1;

            int successfulPairs =
                0;

            System.Windows.Forms.Timer timer =
                new System.Windows.Forms.Timer();

            timer.Interval =
                DeferredEqualDelayMs;

            EventHandler handler =
                null;

            handler =
                delegate (object sender, EventArgs args)
                {
                    timer.Stop();

                    bool keepTimer =
                        false;

                    try
                    {
                        if (pairIndex >= helperKeys.Count)
                        {
                            Debug.WriteLine(
                                "[EDGE EQUAL SPLINE] MOVE V36.7 PAIRWISE EQUAL COMPLETE " +
                                "pairs=" + successfulPairs +
                                "/" + (helperKeys.Count - 1) +
                                ", noCompletionPopup=True" +
                                ", finalSelectionCleanupDelayMs=" +
                                DeferredFinalSelectionCleanupMs);

                            ScheduleDeferredFinalSelectionCleanup(model);
                            return;
                        }

                        Sketch deferredSketch =
                            ReacquireSame3DSketchForReadback(
                                model,
                                sketchFeature,
                                sketchName,
                                "V36.3 pairwise Equal " + pairIndex);

                        deferredSketch =
                            EnsureSame3DSketchEditing(
                                model,
                                deferredSketch,
                                sketchFeature,
                                sketchName);

                        SketchSegment master =
                            ReacquireSketchSegmentByKey(
                                deferredSketch,
                                helperKeys[0]);

                        SketchSegment slave =
                            ReacquireSketchSegmentByKey(
                                deferredSketch,
                                helperKeys[pairIndex]);

                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V36.3 PAIRWISE EQUAL BEGIN " +
                            "pair=0-" + pairIndex +
                            ", master=" + (master != null) +
                            ", slave=" + (slave != null) +
                            ", delayMs=" +
                            (pairIndex == 1
                                ? DeferredEqualDelayMs
                                : DeferredEqualPairDelayMs));

                        if (master == null ||
                            slave == null)
                        {
                            Debug.WriteLine(
                                "[EDGE EQUAL SPLINE] MOVE V36.3 PAIRWISE EQUAL ABORT " +
                                "pair=0-" + pairIndex +
                                ", reason=helper-reacquire-failed");

                            ScheduleManagedCompletionNotice(
                                "AUTO SPLINE MOVE đã hoàn tất hình học, nhưng Equal dừng ở pair 0-" +
                                pairIndex + " vì không reacquire được helper line.");

                            return;
                        }

                        List<SketchSegment> pair =
                            new List<SketchSegment>
                            {
                                master,
                                slave
                            };

                        UiEqualAttempt equalAttempt =
                            TryCreateEqualRelationWithUiCommand(
                                swApp,
                                model,
                                deferredSketch,
                                pair);

                        bool pairRan =
                            equalAttempt.CommandEnabled &&
                            equalAttempt.CommandRan &&
                            equalAttempt.Verified;

                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V36.3 PAIRWISE EQUAL RETURN " +
                            "pair=0-" + pairIndex +
                            ", enabled=" + equalAttempt.CommandEnabled +
                            ", ran=" + equalAttempt.CommandRan +
                            ", assumedRelationsAdded=" +
                            equalAttempt.RelationsAdded +
                            ", verification=" +
                            (equalAttempt.Verified
                                ? "DEFERRED_NO_NATIVE_READBACK"
                                : "NOT_APPLIED"));

                        // No SOLIDWORKS API below this point in this tick.
                        if (!pairRan)
                        {
                            ScheduleManagedCompletionNotice(
                                "AUTO SPLINE MOVE đã hoàn tất hình học, nhưng Equal dừng ở pair 0-" +
                                pairIndex + ".");

                            return;
                        }

                        successfulPairs++;
                        pairIndex++;

                        if (pairIndex >= helperKeys.Count)
                        {
                            Debug.WriteLine(
                                "[EDGE EQUAL SPLINE] MOVE V36.7 PAIRWISE EQUAL COMPLETE " +
                                "pairs=" + successfulPairs +
                                "/" + (helperKeys.Count - 1) +
                                ", noCompletionPopup=True" +
                                ", finalSelectionCleanupDelayMs=" +
                                DeferredFinalSelectionCleanupMs);

                            // V36.6 intentionally left the final Equal pair selected.
                            // Runtime then showed a continuous external selection-event
                            // storm from MAKE HOLE.  Do one delayed selection clear only;
                            // still no rebuild/relation scan immediately after Equal.
                            ScheduleDeferredFinalSelectionCleanup(model);

                            keepTimer = false;
                            return;
                        }

                        timer.Interval =
                            DeferredEqualPairDelayMs;

                        keepTimer =
                            true;

                        timer.Start();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V36.3 PAIRWISE EQUAL ERROR " +
                            "pair=0-" + pairIndex +
                            ", " + ex.Message);
                    }
                    finally
                    {
                        if (!keepTimer)
                        {
                            timer.Tick -=
                                handler;

                            lock (DeferredEqualTimers)
                            {
                                DeferredEqualTimers.Remove(
                                    timer);
                            }

                            timer.Dispose();
                        }
                    }
                };

            timer.Tick +=
                handler;

            lock (DeferredEqualTimers)
            {
                DeferredEqualTimers.Add(
                    timer);
            }

            timer.Start();

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V36.3 PAIRWISE EQUAL SCHEDULED " +
                "initialDelayMs=" + DeferredEqualDelayMs +
                ", pairDelayMs=" + DeferredEqualPairDelayMs +
                ", helpers=" + helperKeys.Count +
                ", expectedPairs=" + (helperKeys.Count - 1));

            return true;
        }

        private bool ScheduleDeferredCreateEqual(ModelDoc2 model, Sketch sketch,
            Feature sketchFeature, List<SketchSegment> helperLines)
        {
            // Native command 1721 has crashed even when deferred. Until a safe
            // supported route is runtime-verified, leave the chain for manual Equal.
            Debug.WriteLine("[EDGE EQUAL SPLINE] CREATE EQUAL NOT_APPLIED native route disabled pending runtime validation");
            return false;
        }

        private static UiEqualAttempt TryCreateEqualRelationWithUiCommand(
            ISldWorks app,
            ModelDoc2 model,
            Sketch activeSketch,
            List<SketchSegment> constructionLines)
        {
            UiEqualAttempt result =
                new UiEqualAttempt();

            if (app == null ||
                model == null ||
                activeSketch == null ||
                constructionLines == null ||
                constructionLines.Count < 2)
            {
                return result;
            }

            // V36.3: avoid RelationManager scanning immediately before
            // the native UI Equal command.
            model.ClearSelection2(true);

            int selectedCount =
                0;

            for (int i = 0;
                 i < constructionLines.Count;
                 i++)
            {
                SketchSegment line =
                    constructionLines[i];

                if (line == null)
                    continue;

                try
                {
                    bool selected =
                        line.Select4(
                            selectedCount > 0,
                            null);

                    if (selected)
                        selectedCount++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "UI Equal select line failed i=" +
                        i +
                        ": " +
                        ex.Message);
                }
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] " +
                "UI EQUAL selectedLines=" +
                selectedCount +
                "/" +
                constructionLines.Count);

            if (selectedCount !=
                constructionLines.Count)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "UI EQUAL ABORT: incomplete line selection.");

                model.ClearSelection2(true);

                return result;
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] " +
                "UI EQUAL PRECHECK commandId=1721");

            try
            {
                result.CommandEnabled =
                    app.IsCommandEnabled(
                        EqualRelationCommandId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "IsCommandEnabled(1721) failed: " +
                    ex.Message);
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] " +
                "UI EQUAL PRECHECK enabled=" +
                result.CommandEnabled);

            if (!result.CommandEnabled)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "UI EQUAL ABORT: command disabled.");

                model.ClearSelection2(true);

                return result;
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] " +
                "UI EQUAL RUN BEGIN commandId=1721");

            try
            {
                result.CommandRan =
                    model.Extension.RunCommand(
                        EqualRelationCommandId,
                        "");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "RunCommand Equal(1721) failed: " +
                    ex.Message);
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] " +
                "UI EQUAL RUN END ran=" +
                result.CommandRan);

            // ------------------------------------------------------------
            // V14.1 NATIVE-CRASH GUARD
            //
            // The V14 runtime log proves RunCommand(1721) returned TRUE.
            // SOLIDWORKS then terminated before the next verification log.
            // The next calls in V14 were:
            //
            //     EditRebuild3()
            //     RelationManager.GetRelations(...)
            //
            // Do NOT touch the solver/relation manager immediately after the
            // native Equal UI command.  Treat a successful, enabled command
            // with a complete line selection as success for this transaction.
            //
            // A later user action/rebuild can let SOLIDWORKS settle naturally.
            // ------------------------------------------------------------
            if (result.CommandEnabled &&
                result.CommandRan &&
                selectedCount ==
                    constructionLines.Count)
            {
                result.RelationsAdded =
                    Math.Max(
                        1,
                        constructionLines.Count - 1);

                result.Verified =
                    true;

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "UI EQUAL SAFE RETURN " +
                    "commandId=1721" +
                    ", enabled=True" +
                    ", ran=True" +
                    ", selected=" +
                    selectedCount +
                    "/" +
                    constructionLines.Count +
                    ", assumedRelationsAdded=" +
                    result.RelationsAdded +
                    ", verification=DEFERRED_NO_RELATION_SCAN");

                // Intentionally DO NOT:
                // - EditRebuild3()
                // - RelationManager.GetRelations(...)
                // - ClearSelection2()
                // here.  All three are avoided in the immediate post-command
                // window because the solver can still be completing native
                // Equal processing.
                return result;
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] " +
                "UI EQUAL SAFE RETURN failed " +
                "enabled=" +
                result.CommandEnabled +
                ", ran=" +
                result.CommandRan +
                ", selected=" +
                selectedCount +
                "/" +
                constructionLines.Count);

            return result;
        }

        private static int CountRelationsByType(
            ISketchRelationManager relationManager,
            int relationType)
        {
            if (relationManager == null)
                return 0;

            object[] relations =
                null;

            try
            {
                relations =
                    relationManager
                        .GetRelations(
                            (int)swSketchRelationFilterType_e
                                .swAll)
                    as object[];
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "GetRelations(swAll) failed: " +
                    ex.Message);

                return 0;
            }

            if (relations == null)
                return 0;

            int count =
                0;

            foreach (object obj in relations)
            {
                SketchRelation relation =
                    obj as SketchRelation;

                if (relation == null)
                    continue;

                try
                {
                    if (relation.GetRelationType() ==
                        relationType)
                    {
                        count++;
                    }
                }
                catch
                {
                }
            }

            return count;
        }

        private static EqualDimensionFallback ApplyEqualLengthDimensionFallback(
            ModelDoc2 model,
            Sketch activeSketch,
            List<SketchSegment> constructionLines)
        {
            EqualDimensionFallback result =
                new EqualDimensionFallback
                {
                    Mode =
                        "NONE"
                };

            if (model == null ||
                activeSketch == null ||
                constructionLines == null ||
                constructionLines.Count < 2)
            {
                return result;
            }

            List<Dimension> dimensions =
                new List<Dimension>();

            List<DisplayDimension> displayDimensions =
                new List<DisplayDimension>();

            for (int i = 0;
                 i < constructionLines.Count;
                 i++)
            {
                SketchSegment segment =
                    constructionLines[i];

                if (segment == null)
                    continue;

                model.ClearSelection2(true);

                bool selected =
                    false;

                try
                {
                    selected =
                        segment.Select4(
                            false,
                            null);
                }
                catch
                {
                }

                if (!selected)
                    continue;

                double[] position =
                    GetDimensionPositionForLine(
                        segment,
                        i);

                DisplayDimension display =
                    null;

                try
                {
                    display =
                        model.AddDimension2(
                            position[0],
                            position[1],
                            position[2])
                        as DisplayDimension;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "AddDimension2 equal fallback failed i=" +
                        i +
                        ": " +
                        ex.Message);
                }

                if (display == null)
                    continue;

                Dimension dimension =
                    null;

                try
                {
                    dimension =
                        display.GetDimension2(
                            0)
                        as Dimension;
                }
                catch
                {
                }

                if (dimension == null)
                    continue;

                displayDimensions.Add(
                    display);

                dimensions.Add(
                    dimension);
            }

            result.DimensionCount =
                dimensions.Count;

            if (dimensions.Count !=
                constructionLines.Count)
            {
                result.Mode =
                    "DIMENSION_CREATE_INCOMPLETE";

                model.ClearSelection2(true);

                return result;
            }

            // L1 is measurement only.  This preserves exactly N-1 independent
            // constraints, the same DOF count as Equal on N lines.
            try
            {
                dimensions[0].DrivenState =
                    (int)swDimensionDrivenState_e
                        .swDimensionDriven;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "master dimension -> driven failed: " +
                    ex.Message);
            }

            string masterReference =
                GetEquationDimensionReference(
                    displayDimensions[0],
                    dimensions[0]);

            double masterValue =
                GetDimensionSystemValue(
                    dimensions[0]);

            EquationMgr equationManager =
                null;

            try
            {
                equationManager =
                    model.GetEquationMgr()
                    as EquationMgr;
            }
            catch
            {
            }

            bool allLinked =
                !string.IsNullOrWhiteSpace(
                    masterReference) &&
                equationManager != null;

            if (allLinked)
            {
                for (int i = 1;
                     i < dimensions.Count;
                     i++)
                {
                    Dimension child =
                        dimensions[i];

                    try
                    {
                        child.DrivenState =
                            (int)swDimensionDrivenState_e
                                .swDimensionDriving;
                    }
                    catch
                    {
                    }

                    string childReference =
                        GetEquationDimensionReference(
                            displayDimensions[i],
                            child);

                    if (string.IsNullOrWhiteSpace(
                            childReference))
                    {
                        allLinked =
                            false;

                        break;
                    }

                    string equation =
                        "\"" +
                        childReference +
                        "\" = \"" +
                        masterReference +
                        "\"";

                    int equationIndex =
                        -1;

                    try
                    {
                        equationIndex =
                            equationManager.Add2(
                                -1,
                                equation,
                                false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] " +
                            "Equation Add2 failed i=" +
                            i +
                            ": " +
                            ex.Message);
                    }

                    if (equationIndex < 0)
                    {
                        allLinked =
                            false;

                        break;
                    }

                    result.LinkedEquationCount++;
                }
            }

            if (allLinked &&
                result.LinkedEquationCount ==
                dimensions.Count - 1)
            {
                try
                {
                    equationManager.EvaluateAll();
                }
                catch
                {
                }

                try
                {
                    model.EditRebuild3();
                }
                catch
                {
                }

                result.Mode =
                    "LINKED_DIMENSIONS";

                result.Verified =
                    true;

                model.ClearSelection2(true);

                return result;
            }

            // Equation manager can reject Add2 on some multi-configuration
            // models.  Do not leave the child dimensions unconstrained.
            // Use the current master length as a fixed driving value for the
            // other N-1 lines.  This still avoids FIX and uses exactly N-1
            // length constraints.
            result.LinkedEquationCount =
                0;

            for (int i = 1;
                 i < dimensions.Count;
                 i++)
            {
                try
                {
                    dimensions[i].DrivenState =
                        (int)swDimensionDrivenState_e
                            .swDimensionDriving;

                    dimensions[i].SystemValue =
                        masterValue;

                    result.FixedValueDimensionCount++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "fixed-value length dim failed i=" +
                        i +
                        ": " +
                        ex.Message);
                }
            }

            try
            {
                model.EditRebuild3();
            }
            catch
            {
            }

            result.Mode =
                "EQUAL_BY_LENGTH_DIMENSIONS";

            result.Verified =
                result.FixedValueDimensionCount ==
                dimensions.Count - 1;

            model.ClearSelection2(true);

            return result;
        }

        private static double[] GetDimensionPositionForLine(
            SketchSegment segment,
            int index)
        {
            double[] fallback =
                new double[]
                {
                    0.0,
                    0.0,
                    0.0
                };

            SketchLine line =
                segment as SketchLine;

            if (line == null)
                return fallback;

            try
            {
                SketchPoint start =
                    line.GetStartPoint2()
                    as SketchPoint;

                SketchPoint end =
                    line.GetEndPoint2()
                    as SketchPoint;

                if (start == null ||
                    end == null)
                {
                    return fallback;
                }

                double mx =
                    0.5 *
                    (start.X + end.X);

                double my =
                    0.5 *
                    (start.Y + end.Y);

                double mz =
                    0.5 *
                    (start.Z + end.Z);

                // Display placement only; it does not affect the dimension value.
                // Stagger a little to prevent every dimension from stacking.
                double lane =
                    0.004 +
                    0.001 *
                    (index % 3);

                return
                    new double[]
                    {
                        mx + lane,
                        my + lane,
                        mz + lane
                    };
            }
            catch
            {
                return fallback;
            }
        }

        private static string GetEquationDimensionReference(
            DisplayDimension display,
            Dimension dimension)
        {
            if (display != null)
            {
                try
                {
                    string selectionName =
                        display.GetNameForSelection();

                    if (!string.IsNullOrWhiteSpace(
                            selectionName))
                    {
                        return selectionName;
                    }
                }
                catch
                {
                }
            }

            if (dimension != null)
            {
                try
                {
                    string fullName =
                        dimension.FullName;

                    if (!string.IsNullOrWhiteSpace(
                            fullName))
                    {
                        int firstAt =
                            fullName.IndexOf(
                                '@');

                        if (firstAt > 0)
                        {
                            int secondAt =
                                fullName.IndexOf(
                                    '@',
                                    firstAt + 1);

                            if (secondAt > firstAt)
                            {
                                return
                                    fullName.Substring(
                                        0,
                                        secondAt);
                            }
                        }

                        return fullName;
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private static double GetDimensionSystemValue(
            Dimension dimension)
        {
            if (dimension == null)
                return 0.0;

            try
            {
                return
                    dimension.SystemValue;
            }
            catch
            {
                return 0.0;
            }
        }

        private static bool IsConstraintTypeAllowed(
            ISketchRelationManager relationManager,
            object[] entities,
            int targetConstraintType)
        {
            if (relationManager == null ||
                entities == null ||
                entities.Length == 0)
            {
                return false;
            }

            object allowedObject =
                null;

            try
            {
                allowedObject =
                    relationManager
                        .GetAllowedRelations(
                            entities);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "GetAllowedRelations failed: " +
                    ex.Message);

                return false;
            }

            if (allowedObject == null)
                return false;

            int[] allowedInts =
                allowedObject as int[];

            if (allowedInts != null)
            {
                foreach (int value in allowedInts)
                {
                    if (value ==
                        targetConstraintType)
                    {
                        return true;
                    }
                }

                return false;
            }

            object[] allowedObjects =
                allowedObject as object[];

            if (allowedObjects != null)
            {
                foreach (object value in allowedObjects)
                {
                    try
                    {
                        if (Convert.ToInt32(
                                value,
                                CultureInfo.InvariantCulture) ==
                            targetConstraintType)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private static VerifiedRelationResult TryAddVerifiedCoincidentRelation(
            ISketchRelationManager relationManager,
            SketchPoint first,
            SketchPoint second,
            int coincidentType)
        {
            VerifiedRelationResult result =
                new VerifiedRelationResult();

            result.RelationType =
                -1;

            result.EntityCount =
                0;

            if (relationManager == null ||
                first == null ||
                second == null)
            {
                result.Error =
                    "Thiếu RelationManager/SketchPoint.";

                return result;
            }

            // Use DispatchWrapper[] deliberately. It is the COM-safe array
            // form for entity arrays passed to sketch-relation APIs.
            DispatchWrapper[] wrappedEntities =
                new DispatchWrapper[]
                {
                    new DispatchWrapper(first),
                    new DispatchWrapper(second)
                };

            object allowedObject =
                null;

            try
            {
                allowedObject =
                    relationManager.GetAllowedRelations(
                        wrappedEntities);
            }
            catch (Exception ex)
            {
                result.Error =
                    "GetAllowedRelations exception: " +
                    ex.Message;

                return result;
            }

            result.Allowed =
                ContainsConstraintType(
                    allowedObject,
                    coincidentType);

            if (!result.Allowed)
            {
                result.Error =
                    "COINCIDENT not present in GetAllowedRelations.";

                return result;
            }

            SketchRelation addedRelation =
                null;

            try
            {
                addedRelation =
                    relationManager.AddRelation(
                        wrappedEntities,
                        coincidentType);
            }
            catch (COMException ex)
            {
                result.Error =
                    "AddRelation COM HRESULT=0x" +
                    ex.ErrorCode.ToString("X8") +
                    " " +
                    ex.Message;

                return result;
            }
            catch (Exception ex)
            {
                result.Error =
                    "AddRelation exception: " +
                    ex.Message;

                return result;
            }

            result.AddReturnedRelation =
                addedRelation != null;

            SketchRelation verifiedRelation =
                null;

            if (addedRelation != null &&
                VerifyRelationContainsExactPointPair(
                    addedRelation,
                    first,
                    second,
                    coincidentType,
                    out int returnedType,
                    out int returnedEntityCount))
            {
                verifiedRelation =
                    addedRelation;

                result.RelationType =
                    returnedType;

                result.EntityCount =
                    returnedEntityCount;
            }

            // Defensive readback from RelationManager. This distinguishes
            // "AddRelation returned null" from "relation exists natively".
            SketchRelation managerRelation =
                FindVerifiedPointPairRelation(
                    relationManager,
                    first,
                    second,
                    coincidentType);

            result.ManagerFoundRelation =
                managerRelation != null;

            if (verifiedRelation == null &&
                managerRelation != null)
            {
                verifiedRelation =
                    managerRelation;

                try
                {
                    result.RelationType =
                        managerRelation.GetRelationType();
                }
                catch
                {
                    result.RelationType =
                        -1;
                }

                object[] entities =
                    GetRelationEntities(
                        managerRelation);

                result.EntityCount =
                    entities == null
                        ? 0
                        : entities.Length;
            }

            result.Verified =
                verifiedRelation != null;

            if (!result.Verified &&
                string.IsNullOrWhiteSpace(
                    result.Error))
            {
                result.Error =
                    "Relation object not verified after AddRelation.";
            }

            return result;
        }

        private static bool ContainsConstraintType(
            object allowedObject,
            int targetConstraintType)
        {
            if (allowedObject == null)
                return false;

            int[] ints =
                allowedObject as int[];

            if (ints != null)
            {
                foreach (int value in ints)
                {
                    if (value ==
                        targetConstraintType)
                    {
                        return true;
                    }
                }

                return false;
            }

            Array values =
                allowedObject as Array;

            if (values == null)
                return false;

            foreach (object rawValue in values)
            {
                try
                {
                    if (Convert.ToInt32(
                            rawValue,
                            CultureInfo.InvariantCulture) ==
                        targetConstraintType)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private static int CountVerifiedPointPairRelations(
            ISketchRelationManager relationManager,
            List<SketchPoint> firstPoints,
            List<SketchPoint> secondPoints,
            int relationType)
        {
            if (relationManager == null ||
                firstPoints == null ||
                secondPoints == null ||
                firstPoints.Count == 0 ||
                firstPoints.Count != secondPoints.Count)
            {
                return 0;
            }

            object[] relations =
                null;

            try
            {
                relations =
                    relationManager.GetRelations(
                        (int)swSketchRelationFilterType_e
                            .swAll)
                    as object[];
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] POSTCOMMIT GetRelations failed: " +
                    ex.Message);

                return 0;
            }

            if (relations == null)
                return 0;

            bool[] matched =
                new bool[
                    firstPoints.Count];

            int verified =
                0;

            foreach (object raw in relations)
            {
                SketchRelation relation =
                    UnwrapDispatchObject(
                        raw)
                    as SketchRelation;

                if (relation == null)
                    continue;

                for (int i = 0;
                     i < firstPoints.Count;
                     i++)
                {
                    if (matched[i])
                        continue;

                    if (VerifyRelationContainsExactPointPair(
                            relation,
                            firstPoints[i],
                            secondPoints[i],
                            relationType,
                            out int actualType,
                            out int entityCount))
                    {
                        matched[i] =
                            true;

                        verified++;

                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] POSTCOMMIT RELATION VERIFIED " +
                            "index=" +
                            i +
                            ", relationType=" +
                            actualType +
                            ", entityCount=" +
                            entityCount);

                        break;
                    }
                }
            }

            return verified;
        }

        private static object[] GetRelationDefinitionEntities(
            SketchRelation relation)
        {
            if (relation == null)
                return null;

            try
            {
                return
                    relation.GetDefinitionEntities2()
                    as object[];
            }
            catch
            {
                return null;
            }
        }

        private static SketchRelation FindVerifiedPointPairRelation(
            ISketchRelationManager relationManager,
            SketchPoint first,
            SketchPoint second,
            int relationType)
        {
            if (relationManager == null ||
                first == null ||
                second == null)
            {
                return null;
            }

            object[] relations =
                null;

            try
            {
                relations =
                    relationManager.GetRelations(
                        (int)swSketchRelationFilterType_e
                            .swAll)
                    as object[];
            }
            catch
            {
                return null;
            }

            if (relations == null)
                return null;

            foreach (object raw in relations)
            {
                SketchRelation relation =
                    UnwrapDispatchObject(raw)
                    as SketchRelation;

                if (relation == null)
                    continue;

                if (VerifyRelationContainsExactPointPair(
                        relation,
                        first,
                        second,
                        relationType,
                        out _,
                        out _))
                {
                    return relation;
                }
            }

            return null;
        }

        private static bool VerifyRelationContainsExactPointPair(
            SketchRelation relation,
            SketchPoint first,
            SketchPoint second,
            int expectedRelationType,
            out int actualRelationType,
            out int entityCount)
        {
            actualRelationType =
                -1;

            entityCount =
                0;

            if (relation == null ||
                first == null ||
                second == null)
            {
                return false;
            }

            try
            {
                actualRelationType =
                    relation.GetRelationType();
            }
            catch
            {
                return false;
            }

            if (actualRelationType !=
                expectedRelationType)
            {
                return false;
            }

            object[] entities =
                GetRelationDefinitionEntities(
                    relation);

            if (entities == null)
            {
                entities =
                    GetRelationEntities(
                        relation);
            }

            if (entities == null)
                return false;

            entityCount =
                entities.Length;

            bool hasFirst =
                false;

            bool hasSecond =
                false;

            foreach (object rawEntity in entities)
            {
                object entity =
                    UnwrapDispatchObject(
                        rawEntity);

                SketchPoint entityPoint =
                    entity as SketchPoint;

                if (!hasFirst &&
                    IsSameSketchPointIdentity(
                        entityPoint,
                        first))
                {
                    hasFirst =
                        true;
                }

                if (!hasSecond &&
                    IsSameSketchPointIdentity(
                        entityPoint,
                        second))
                {
                    hasSecond =
                        true;
                }
            }

            return
                hasFirst &&
                hasSecond;
        }

        private static bool IsSameSketchPointIdentity(
            SketchPoint first,
            SketchPoint second)
        {
            if (first == null ||
                second == null)
            {
                return false;
            }

            if (IsSameComObject(
                    first,
                    second))
            {
                return true;
            }

            string firstKey =
                GetSketchPointKey(
                    first);

            string secondKey =
                GetSketchPointKey(
                    second);

            return
                !string.IsNullOrWhiteSpace(
                    firstKey) &&
                string.Equals(
                    firstKey,
                    secondKey,
                    StringComparison.Ordinal);
        }

        private static object UnwrapDispatchObject(
            object value)
        {
            DispatchWrapper wrapper =
                value as DispatchWrapper;

            if (wrapper != null)
            {
                try
                {
                    return wrapper.WrappedObject;
                }
                catch
                {
                    return null;
                }
            }

            return value;
        }

        private static bool AddCoincident(
            ModelDoc2 model,
            object first,
            object second)
        {
            if (model == null ||
                first == null ||
                second == null)
            {
                return false;
            }

            model.ClearSelection2(true);

            bool firstSelected =
                SelectComEntity(
                    first,
                    false);

            bool secondSelected =
                SelectComEntity(
                    second,
                    true);

            if (!firstSelected ||
                !secondSelected)
            {
                return false;
            }

            try
            {
                model.SketchAddConstraints(
                    "sgCOINCIDENT");

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool SelectComEntity(
            object entity,
            bool append)
        {
            if (entity == null)
                return false;

            try
            {
                SketchPoint sketchPoint =
                    entity as SketchPoint;

                if (sketchPoint != null)
                {
                    return
                        sketchPoint.Select4(
                            append,
                            null);
                }

                SketchSegment sketchSegment =
                    entity as SketchSegment;

                if (sketchSegment != null)
                {
                    return
                        sketchSegment.Select4(
                            append,
                            null);
                }

                // Edge / Vertex / Face from the active document expose IEntity.
                Entity modelEntity =
                    entity as Entity;

                if (modelEntity != null)
                {
                    return
                        modelEntity.Select4(
                            append,
                            null);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "SelectComEntity typed failed: " +
                    ex.Message);
            }

            return false;
        }

        private static double DistanceSketchPointToVertex(
            SketchPoint point,
            Vertex vertex)
        {
            if (point == null ||
                vertex == null)
            {
                return double.MaxValue;
            }

            try
            {
                double[] vertexPoint =
                    vertex.GetPoint()
                    as double[];

                if (vertexPoint == null ||
                    vertexPoint.Length < 3)
                {
                    return double.MaxValue;
                }

                double dx =
                    point.X - vertexPoint[0];

                double dy =
                    point.Y - vertexPoint[1];

                double dz =
                    point.Z - vertexPoint[2];

                return
                    Math.Sqrt(
                        dx * dx +
                        dy * dy +
                        dz * dz);
            }
            catch
            {
                return double.MaxValue;
            }
        }

        private static string GetSketchPointKey(
            SketchPoint point)
        {
            if (point == null)
                return "";

            try
            {
                int[] id =
                    point.GetID()
                    as int[];

                if (id != null &&
                    id.Length >= 2)
                {
                    return
                        id[0].ToString(
                            CultureInfo.InvariantCulture)
                        + ":"
                        + id[1].ToString(
                            CultureInfo.InvariantCulture);
                }
            }
            catch
            {
            }

            return
                point.X.ToString(
                    "R",
                    CultureInfo.InvariantCulture)
                + ":"
                + point.Y.ToString(
                    "R",
                    CultureInfo.InvariantCulture)
                + ":"
                + point.Z.ToString(
                    "R",
                    CultureInfo.InvariantCulture);
        }

        private static SpacingStats MeasurePointSpacing(
            List<SketchPoint> points)
        {
            SpacingStats result =
                new SpacingStats
                {
                    Min =
                        double.MaxValue,

                    Max =
                        0.0
                };

            if (points == null ||
                points.Count < 2)
            {
                result.Min =
                    0.0;

                return result;
            }

            for (int i = 0;
                 i < points.Count - 1;
                 i++)
            {
                SketchPoint a =
                    points[i];

                SketchPoint b =
                    points[i + 1];

                double dx =
                    b.X - a.X;

                double dy =
                    b.Y - a.Y;

                double dz =
                    b.Z - a.Z;

                double distance =
                    Math.Sqrt(
                        dx * dx +
                        dy * dy +
                        dz * dz);

                result.Min =
                    Math.Min(
                        result.Min,
                        distance);

                result.Max =
                    Math.Max(
                        result.Max,
                        distance);
            }

            if (result.Min ==
                double.MaxValue)
            {
                result.Min =
                    0.0;
            }

            return result;
        }


        private void MoveExistingSplineToEdge(
            ModelDoc2 model,
            Sketch targetSketch,
            Feature targetSketchFeature,
            SketchSpline selectedSpline,
            Edge targetEdge,
            double toleranceMm)
        {
            if (model == null ||
                targetEdge == null)
            {
                throw new InvalidOperationException(
                    "Thiếu document hoặc Edge đích.");
            }

            // ============================================================
            // V35.3 - SAME 3D SKETCH + SAME SketchSpline DIRECT RETARGET
            //
            // HARD RULES:
            // - Keep the SAME existing 3D Sketch feature.
            // - Keep the SAME existing SketchSpline entity.
            // - NEVER call CreateSpline* in MOVE.
            // - NEVER delete / replace the selected SketchSpline.
            // - Point count may change only by SketchSpline.InsertPoint /
            //   SketchSpline.DeletePoint on that SAME spline.
            // - The selected Edge determines the required point count.
            // - The final ACTUAL SketchSpline <-> Edge deviation must pass.
            //
            // IMPORTANT CHANGE FROM V34.1:
            // - sgMERGEPOINTS is NOT used for MOVE anymore.
            // - Runtime proved that SketchAddConstraints("sgMERGEPOINTS")
            //   can return normally while the spline fit point does not move.
            //
            // V35.3 direct route:
            // 1) release the old AUTO SPLINE relation network;
            // 2) keep the SAME spline and SAME 3D Sketch;
            // 3) NEVER add sgMERGEPOINTS / sgCOINCIDENT in MOVE;
            // 4) prove native MoveOrCopy on ONE existing fit point first;
            // 5) only after that proof, move the CURRENT topology to the Edge;
            // 6) validate the real SAME SketchSpline against the Edge;
            // 7) then adapt point count one point at a time and keep the
            //    smallest passing topology;
            // 8) create disposable helper chords only after geometry passes.
            // ============================================================

            if (selectedSpline == null)
            {
                throw new InvalidOperationException(
                    "MOVE yêu cầu chọn chính spline hiện hữu.");
            }

            Sketch activeSketch =
                model.SketchManager.ActiveSketch
                as Sketch;

            if (activeSketch == null ||
                !TryIs3DSketch(
                    activeSketch))
            {
                throw new InvalidOperationException(
                    "MOVE yêu cầu đang Edit 3D Sketch chứa spline đã chọn.\n\n" +
                    "Ctrl-select đúng 2 đối tượng:\n" +
                    "1) spline hiện hữu\n" +
                    "2) Edge Surface mới");
            }

            targetSketch =
                activeSketch;

            if (!SketchContainsSpline(
                    targetSketch,
                    selectedSpline))
            {
                throw new InvalidOperationException(
                    "Spline đã chọn không thuộc 3D Sketch đang Edit.");
            }

            Feature owningSketchFeature =
                targetSketchFeature;

            if (owningSketchFeature == null)
            {
                owningSketchFeature =
                    FindOwningFeatureForSketch(
                        model,
                        targetSketch);
            }

            if (owningSketchFeature == null)
            {
                throw new InvalidOperationException(
                    "Không xác định được Feature của 3D Sketch đang Edit.");
            }

            string owningSketchName =
                SafeFeatureName(
                    owningSketchFeature);

            AutoSplineGeometry geometry =
                BuildMoveGeometryForSelectedSpline(
                    targetSketch,
                    selectedSpline);

            if (geometry == null ||
                geometry.Spline == null ||
                geometry.FitPoints == null ||
                geometry.FitPoints.Count <
                    MinimumPointCount)
            {
                throw new InvalidOperationException(
                    "Spline đã chọn không có đủ fit point để MOVE.");
            }

            SketchSpline originalSpline =
                selectedSpline;

            SketchSegment originalSplineSegment =
                originalSpline as SketchSegment;

            if (originalSplineSegment == null)
            {
                throw new InvalidOperationException(
                    "Không lấy được SketchSegment của spline hiện tại.");
            }

            string splineKeyBefore =
                GetSketchSegmentKey(
                    originalSplineSegment);

            int oldPointCount =
                geometry.FitPoints.Count;

            int oldHelperLineCount =
                geometry.ConstructionLines == null
                    ? 0
                    : geometry.ConstructionLines.Count;

            if (oldPointCount >
                CreateMaximumPointCount)
            {
                throw new InvalidOperationException(
                    "Spline hiện hữu có " +
                    oldPointCount +
                    " fit point, vượt giới hạn an toàn " +
                    CreateMaximumPointCount +
                    ".");
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V35 BEGIN " +
                "sketch=\"" +
                owningSketchName +
                "\", splineKey=" +
                splineKeyBefore +
                ", oldPoints=" +
                oldPointCount +
                ", oldHelperLines=" +
                oldHelperLineCount);

            // ------------------------------------------------------------
            // Read and pre-sample the NEW Edge.
            // ------------------------------------------------------------
            Curve targetCurve =
                targetEdge.GetCurve()
                as Curve;

            CurveParamData targetParams =
                null;

            try
            {
                targetParams =
                    targetEdge.GetCurveParams3();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE GetCurveParams3 failed: " +
                    ex.Message);
            }

            if (targetCurve == null ||
                targetParams == null)
            {
                throw new InvalidOperationException(
                    "Không đọc được Curve của Edge mới.");
            }

            double targetU0 =
                Math.Min(
                    targetParams.UMinValue,
                    targetParams.UMaxValue);

            double targetU1 =
                Math.Max(
                    targetParams.UMinValue,
                    targetParams.UMaxValue);

            if (Math.Abs(
                    targetU1 -
                    targetU0) <
                1.0e-12)
            {
                throw new InvalidOperationException(
                    "Miền parameter của Edge mới không hợp lệ.");
            }

            double targetLength =
                GetCurveLength(
                    targetCurve,
                    targetU0,
                    targetU1);

            if (!IsFinite(
                    targetLength) ||
                targetLength <=
                    1.0e-9)
            {
                throw new InvalidOperationException(
                    "Chiều dài Edge mới không hợp lệ.");
            }

            double targetLengthMm =
                targetLength *
                1000.0;

            CreatePointPlan movePlan =
                DetermineCreatePointPlan(
                    targetEdge,
                    targetCurve,
                    targetU0,
                    targetU1,
                    targetLength);

            double createActualSplineToleranceMm =
                GetCreateActualSplineToleranceMm(
                    targetLengthMm);

            // V35.6 MOVE-specific tolerance.  MOVE must preserve the SAME
            // native SketchSpline, so we do not grow fit-point topology with
            // InsertPoint merely to chase a tiny residual.  A slightly more
            // permissive relative tolerance (still capped at 0.50 mm) keeps
            // the existing entity stable while remaining visually coincident
            // with the selected Edge.
            double moveActualSplineToleranceMm =
                Math.Min(
                    CreateMaximumSplineToleranceMm,
                    Math.Max(
                        createActualSplineToleranceMm,
                        targetLengthMm * MoveRelativeSplineToleranceRatio));

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V35.6 TOLERANCE " +
                "createToleranceMm=" +
                createActualSplineToleranceMm.ToString(
                    "0.######", CultureInfo.InvariantCulture) +
                ", moveToleranceMm=" +
                moveActualSplineToleranceMm.ToString(
                    "0.######", CultureInfo.InvariantCulture) +
                ", capMm=" +
                CreateMaximumSplineToleranceMm.ToString(
                    "0.######", CultureInfo.InvariantCulture));

            bool targetEdgeClosed =
                IsEdgeTopologicallyClosed(
                    targetEdge);

            int expectedEndpointRelations =
                targetEdgeClosed
                    ? 0
                    : 2;

            List<double[]> originalFitPointSnapshot =
                SnapshotSketchPointCoordinates(
                    geometry.FitPoints);

            // V37.3: freeze fit-point identity in the ORIGINAL native spline
            // order.  We do not re-sort points by proximity to the target Edge
            // during the large 3D move because that can swap point identity and
            // force the native spline to backtrack/loop.
            List<string> originalFitPointKeys =
                new List<string>();

            foreach (SketchPoint fitPoint in geometry.FitPoints)
            {
                originalFitPointKeys.Add(
                    GetSketchPointKey(fitPoint));
            }

            bool reverseTarget =
                ShouldReverseTargetForSameSpline(
                    originalFitPointSnapshot,
                    targetEdge,
                    targetU0,
                    targetU1);

            int recommendedPointCount =
                Math.Max(
                    MinimumPointCount,
                    Math.Min(
                        CreateMaximumPointCount,
                        movePlan.PointCount));

            byte[] targetReference =
                GeometryCall(
                    "IModelDocExtension.GetPersistReference3(target)",
                    () =>
                        model.Extension
                            .GetPersistReference3(
                                targetEdge)
                        as byte[]);

            if (targetReference == null ||
                targetReference.Length == 0)
            {
                throw new InvalidOperationException(
                    "Không lưu được persistent reference của Edge mới.");
            }

            List<double[]> targetSnapshot =
                BuildCurvePolylineByArcLength(
                    targetEdge,
                    targetCurve,
                    targetU0,
                    targetU1,
                    targetLength,
                    2048);

            if (targetSnapshot == null ||
                targetSnapshot.Count < 2)
            {
                throw new InvalidOperationException(
                    "Không pre-sample được Edge mới.");
            }

            Dictionary<int, SamplingResult> targetCandidates =
                new Dictionary<int, SamplingResult>();

            for (int n =
                     MinimumPointCount;
                 n <=
                     CreateMaximumPointCount;
                 n++)
            {
                targetCandidates[n] =
                    EvaluateSampling(
                        targetEdge,
                        targetCurve,
                        targetU0,
                        targetU1,
                        targetLength,
                        n - 1,
                        double.MaxValue);
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V35 TARGET PLAN " +
                "edgeLengthMm=" +
                targetLengthMm.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture) +
                ", oldPoints=" +
                oldPointCount +
                ", recommendedPoints=" +
                recommendedPointCount +
                ", maxPoints=" +
                CreateMaximumPointCount +
                ", reverseTarget=" +
                reverseTarget +
                ", closed=" +
                targetEdgeClosed +
                ", toleranceMm=" +
                moveActualSplineToleranceMm.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture));

            try
            {
                // --------------------------------------------------------
                // V35.3 STEP 1 - UNLOCK ONLY.
                //
                // Do not change point count yet.  The old topology is the
                // safest topology with which to prove a real native move.
                // --------------------------------------------------------
                InPlaceRelationReleaseResult release =
                    ReleaseAutoSplineRelationsForInPlaceRetarget(
                        targetSketch,
                        geometry.FitPoints,
                        geometry.ConstructionLines,
                        originalSpline);

                int remainingSplineRelations =
                    CountSketchRelationsTouchingSpline(
                        targetSketch,
                        originalSpline);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V35.3 UNLOCK " +
                    "relationsDeleted=" + release.RelationsDeleted +
                    ", fitPointRelations=" + release.FitPointRelationsDeleted +
                    ", splineRelations=" + release.SplineRelationsDeleted +
                    ", helperRelations=" + release.HelperRelationsDeleted +
                    ", equalRelations=" + release.EqualRelationsDeleted +
                    ", remainingSplineRelations=" + remainingSplineRelations);

                if (remainingSplineRelations > 0)
                {
                    throw new InvalidOperationException(
                        "MOVE dừng an toàn: SAME spline vẫn còn " +
                        remainingSplineRelations +
                        " relation trực tiếp sau unlock.");
                }

                // --------------------------------------------------------
                // V37.0: SELECTIVE BLOCKER CLEANUP.
                //
                // User workflow:
                // - KEEP straight construction center LINE geometry so it can be
                //   constrained manually after MOVE.
                // - DELETE construction center SPLINE geometry because runtime
                //   showed that a connected construction spline can keep one or
                //   more SAME-spline fit points from reaching their new targets.
                // - Do not recreate deleted center splines afterwards.
                //
                // Relation unlock above already removes the old relation network.
                // This step changes only blocker GEOMETRY and never deletes the
                // selected SAME SketchSpline itself or the recognized helper chain.
                // --------------------------------------------------------
                SelectiveBlockingConstructionCleanupResult blockerCleanup =
                    DeleteOnlyBlockingConstructionSplinesForMove(
                        model,
                        targetSketch,
                        release.BlockingConstructionSegmentKeys);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V37.0 BLOCKER CLEANUP " +
                    "discovered=" + blockerCleanup.Discovered +
                    ", deletedCenterSplines=" + blockerCleanup.DeletedConstructionSplines +
                    ", preservedStraightLines=" + blockerCleanup.PreservedStraightLines +
                    ", preservedOtherConstruction=" + blockerCleanup.PreservedOtherConstruction +
                    ", missing=" + blockerCleanup.Missing +
                    ", straightCenterLineKept=True");

                int deletedOldHelpers =
                    DeleteAllDisposableHelperLines(
                        model,
                        targetSketch,
                        geometry.ConstructionLines);

                if (deletedOldHelpers != oldHelperLineCount)
                {
                    throw new InvalidOperationException(
                        "Không xóa đủ helper line cũ. " +
                        deletedOldHelpers + "/" + oldHelperLineCount);
                }

                targetSketch =
                    EnsureSame3DSketchEditing(
                        model,
                        targetSketch,
                        owningSketchFeature,
                        owningSketchName);

                originalSplineSegment =
                    ReacquireSketchSegmentByKey(
                        targetSketch,
                        splineKeyBefore);

                originalSpline =
                    originalSplineSegment as SketchSpline;

                if (originalSpline == null)
                {
                    throw new InvalidOperationException(
                        "Mất SAME spline ngay sau unlock.");
                }

                VerifySameSplineIdentityOrThrow(
                    originalSpline,
                    selectedSpline,
                    splineKeyBefore,
                    "V35.3 after unlock");

                int currentPointCount =
                    SafeGetSplinePointCount(
                        originalSpline);

                if (currentPointCount != oldPointCount)
                {
                    throw new InvalidOperationException(
                        "Point count đổi trong bước unlock. " +
                        oldPointCount + " -> " + currentPointCount);
                }

                SamplingResult currentSampling =
                    targetCandidates[currentPointCount];

                // V36.2: keep the proven equal-ARC targets for SAME-spline MOVE.
                // V36.1 proved that forcing exact equal-chord targets before the
                // existing native spline has settled can make the SAME spline
                // overshoot the Edge badly even though every fit point is on it.
                List<double[]> currentTargets =
                    CopyOrReversePointList(
                        currentSampling.Points,
                        reverseTarget);

                double currentChordMin;
                double currentChordMax;
                MeasureChordRange(
                    currentTargets,
                    out currentChordMin,
                    out currentChordMax);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V36.2 EQUAL-ARC TARGET " +
                    "points=" + currentPointCount +
                    ", chordMinMm=" +
                    (currentChordMin * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", chordMaxMm=" +
                    (currentChordMax * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", spreadMm=" +
                    ((currentChordMax - currentChordMin) * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture));

                // --------------------------------------------------------
                // V37.3 STEP 2/3 - COHERENT PRE-ALIGN + STAGED MORPH.
                //
                // Do NOT probe one isolated point first. A one-point jump can
                // already make a 3D native SketchSpline flip its internal shape.
                // Instead keep the original fit-point identity/order frozen,
                // pre-align the complete old point cloud near the new Edge, then
                // move ALL fit points by small coherent stages.
                //
                // IMPORTANT: this route DOES NOT SET ANY SketchSpline PROPERTY.
                // It only moves existing SketchPoint XYZ positions.
                // --------------------------------------------------------
                CoherentMorphResult morphResult =
                    MoveSameSplineByCoherentMorph(
                        model,
                        owningSketchFeature,
                        owningSketchName,
                        splineKeyBefore,
                        selectedSpline,
                        originalFitPointKeys,
                        originalFitPointSnapshot,
                        currentTargets);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V37.5 COHERENT MORPH RESULT " +
                    "success=" + morphResult.Success +
                    ", sameSpline=" + morphResult.SameSpline +
                    ", stages=" + morphResult.CompletedStages +
                    "/" + morphResult.TotalStages +
                    ", moveCalls=" + morphResult.MoveCallCount +
                    ", maxPointErrorMm=" +
                    (morphResult.MaximumPointError * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", twistGuard=" + morphResult.TwistGuardPassed +
                    ", propertyWrites=NONE" +
                    ", error=" + (morphResult.Error ?? ""));

                if (!morphResult.Success)
                {
                    throw new InvalidOperationException(
                        "Không MOVE được SAME spline bằng coherent staged morph. " +
                        (morphResult.Error ?? ""));
                }

                DirectSplineCandidateResult currentCandidate =
                    EvaluateDirectSameSplineCandidate(
                        model,
                        owningSketchFeature,
                        owningSketchName,
                        splineKeyBefore,
                        selectedSpline,
                        currentTargets,
                        targetSnapshot,
                        reverseTarget,
                        moveActualSplineToleranceMm);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V35.3 CURRENT-TOPOLOGY TEST " +
                    "points=" + currentPointCount +
                    ", maxTargetMm=" +
                    (currentCandidate.MaxTargetError * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", actualDeviationMm=" +
                    (currentCandidate.ActualSplineDeviation * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", toleranceMm=" +
                    moveActualSplineToleranceMm
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", pass=" + currentCandidate.Success);

                // --------------------------------------------------------
                // V37.2 STEP 4A - DO NOT EXPERIMENT ON THE SAME NATIVE SPLINE.
                //
                // Runtime V37.1 proved that repeatedly moving one fit point,
                // measuring, and moving it back is NOT a reversible probe for a
                // native SketchSpline.  Even with the same XYZ fit-point targets,
                // SolidWorks can keep/recompute internal tangent/control state, so
                // later rebuilds can be worse than the state that was measured as
                // "best".  This caused long run times and visible loops.
                //
                // Therefore V37.2 keeps the stable equal-arc state intact.  If the
                // CURRENT topology does not pass, we fail cleanly instead of
                // destructively optimizing the SAME spline.  At that point we also
                // log the exact SketchSpline interop members available in the
                // user's SolidWorks version so the next route can use a real native
                // Relax/Reset API if one exists, rather than guessing method names.
                // --------------------------------------------------------
                double currentDeviationBeforeAdaptive =
                    currentCandidate.ActualSplineDeviation;

                if (!currentCandidate.Success)
                {
                    double failureRatio =
                        moveActualSplineToleranceMm > 1.0e-12 &&
                        IsFinite(currentCandidate.ActualSplineDeviation)
                            ? (currentCandidate.ActualSplineDeviation * 1000.0) /
                              moveActualSplineToleranceMm
                            : double.MaxValue;

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V37.3 POST-MORPH ADAPTIVE SKIPPED " +
                        "points=" + currentPointCount +
                        ", actualDeviationMm=" +
                        (currentCandidate.ActualSplineDeviation * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", toleranceMm=" +
                        moveActualSplineToleranceMm
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", ratio=" +
                        failureRatio.ToString("0.###", CultureInfo.InvariantCulture) +
                        ", reason=NO_DESTRUCTIVE_TRIALS_AFTER_COHERENT_MORPH");

                    LogSketchSplineNativeAdjustmentApiSurface();
                }

                // --------------------------------------------------------
                // V36.8 STEP 4B - GEOMETRY-ONLY SAFE APPROXIMATE MINIMUM-POINT SEARCH.
                //
                // IMPORTANT:
                // - NEVER grow topology during MOVE.  Runtime V35.4/V35.5
                //   proved that InsertPoint + redistributing all fit points can
                //   make the SAME spline oscillate even though every fit point
                //   is exactly on the Edge.
                // - The CURRENT topology must pass in the stable equal-arc state.
                // - Reduce only ONE point at a time.
                // - Never rollback a failed reduction with InsertPoint.
                // - After the first accepted reduction, require strong tolerance
                //   headroom before trying another destructive delete. This
                //   intentionally targets the smallest SAFE approximate N.
                // - If an unexpected candidate still fails, use native Undo to
                //   restore the exact pre-delete SAME spline state.
                // --------------------------------------------------------
                if (!currentCandidate.Success)
                {
                    string reserveExplanation =
                        currentPointCount < ReusableSplineReservePointCount
                            ? " Topology hiện tại thấp hơn reusable reserve " +
                              ReusableSplineReservePointCount +
                              " point; V37.4 không InsertPoint để tránh làm đổi native parameterization của SAME spline."
                            : "";

                    throw new InvalidOperationException(
                        "SAME spline đã MOVE đúng fit point nhưng topology hiện tại " +
                        currentPointCount +
                        " point vẫn lệch Edge ở trạng thái equal-arc an toàn: " +
                        (currentCandidate.ActualSplineDeviation * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        " mm > MOVE tolerance " +
                        moveActualSplineToleranceMm
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        " mm. Không InsertPoint vì phải giữ SAME spline an toàn." +
                        reserveExplanation);
                }

                int bestPointCount =
                    currentPointCount;

                List<double[]> bestTargets =
                    currentTargets;

                double bestActualDeviation =
                    currentCandidate.ActualSplineDeviation;

                // V37.4 reusable topology floor. Never delete below 7 when the
                // incoming SAME spline already has at least 12 fit points. If an
                // older spline already has fewer than 7, keep its existing count;
                // this command still refuses to grow topology with InsertPoint.
                int reusableReductionFloor =
                    Math.Min(
                        currentPointCount,
                        Math.Max(
                            MinimumPointCount,
                            ReusableSplineReservePointCount));

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V37.4 REUSABLE TOPOLOGY FLOOR " +
                    "currentN=" + currentPointCount +
                    ", reserveFloor=" + ReusableSplineReservePointCount +
                    ", effectiveFloor=" + reusableReductionFloor +
                    ", canReduce=" +
                    (currentPointCount > reusableReductionFloor) +
                    ", canGrowSafely=False");

                double reduceGeometryToleranceMm =
                    Math.Min(
                        moveActualSplineToleranceMm,
                        Math.Max(
                            MoveReduceAbsoluteDeviationFloorMm,
                            currentDeviationBeforeAdaptive * 1000.0 *
                            MoveReduceMaximumDeviationGrowthRatio));

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V36.8 REDUCE ACCURACY BUDGET " +
                    "currentDeviationMm=" +
                    (bestActualDeviation * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", reduceToleranceMm=" +
                    reduceGeometryToleranceMm
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", normalMoveToleranceMm=" +
                    moveActualSplineToleranceMm
                        .ToString("0.######", CultureInfo.InvariantCulture));

                double bestChordMin;
                double bestChordMax;

                MeasureChordRange(
                    bestTargets,
                    out bestChordMin,
                    out bestChordMax);

                double bestChordSpread =
                    Math.Max(
                        0.0,
                        bestChordMax - bestChordMin);

                double bestRelativeChordSpread =
                    GetRelativeChordSpread(
                        bestChordMin,
                        bestChordMax);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V36.8 CHORD DIAGNOSTIC BASE " +
                    "points=" + bestPointCount +
                    ", chordSpreadMm=" +
                    (bestChordSpread * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", relativeSpreadPct=" +
                    (bestRelativeChordSpread * 100.0)
                        .ToString("0.######", CultureInfo.InvariantCulture));

                bool reducedAtLeastOnce =
                    false;

                double previousAcceptedDeviation =
                    double.NaN;

                int previousAcceptedPointCount =
                    0;

                for (int n = currentPointCount - 1;
                     n >= reusableReductionFloor;
                     n--)
                {
                    // ----------------------------------------------------
                    // V35.7 NON-DESTRUCTIVE PRE-FLIGHT.
                    //
                    // The FIRST one-point reduction is allowed so an old,
                    // unnecessarily dense spline can become lighter.
                    // After one accepted reduction, do NOT probe another
                    // topology unless the current accepted spline has real
                    // tolerance headroom.  V35.6 proved that probing a FAIL
                    // and then InsertPoint-ing the deleted point can destroy
                    // the SAME native spline parameterization and create a
                    // self-crossing loop.
                    // ----------------------------------------------------
                    if (reducedAtLeastOnce)
                    {
                        double headroomRatio =
                            reduceGeometryToleranceMm > 1.0e-12
                                ? (bestActualDeviation * 1000.0) /
                                  reduceGeometryToleranceMm
                                : double.MaxValue;

                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V36.8 REDUCE PREFLIGHT " +
                            "bestN=" + bestPointCount +
                            ", candidateN=" + n +
                            ", actualDeviationMm=" +
                            (bestActualDeviation * 1000.0)
                                .ToString("0.######", CultureInfo.InvariantCulture) +
                            ", toleranceMm=" +
                            reduceGeometryToleranceMm
                                .ToString("0.######", CultureInfo.InvariantCulture) +
                            ", headroomRatio=" +
                            headroomRatio.ToString("0.######", CultureInfo.InvariantCulture) +
                            ", continueLimit=" +
                            MoveReduceContinueHeadroomRatio.ToString("0.######", CultureInfo.InvariantCulture));

                        if (!IsFinite(headroomRatio) ||
                            headroomRatio > MoveReduceContinueHeadroomRatio)
                        {
                            Debug.WriteLine(
                                "[EDGE EQUAL SPLINE] MOVE V36.8 REDUCE STOP " +
                                "reason=insufficient-headroom-before-destructive-test" +
                                ", bestN=" + bestPointCount +
                                ", nextN=" + n);
                            break;
                        }
                    }

                    // ----------------------------------------------------
                    // V36.8 GEOMETRY-ONLY TARGET PREP.
                    //
                    // Equal is no longer part of the automatic MOVE workflow, so
                    // chord spread must NEVER reject a lower point count.  The
                    // only acceptance criterion is the measured native SAME
                    // SketchSpline <-> Edge deviation after the one-point reduce.
                    //
                    // We still compute chord statistics for debug only.
                    // ----------------------------------------------------
                    List<double[]> preflightTargets =
                        CopyOrReversePointList(
                            targetCandidates[n].Points,
                            reverseTarget);

                    double preflightChordMin;
                    double preflightChordMax;
                    MeasureChordRange(
                        preflightTargets,
                        out preflightChordMin,
                        out preflightChordMax);

                    double preflightChordSpread =
                        Math.Max(
                            0.0,
                            preflightChordMax - preflightChordMin);

                    double preflightRelativeChordSpread =
                        GetRelativeChordSpread(
                            preflightChordMin,
                            preflightChordMax);

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V36.8 GEOMETRY-ONLY TARGET " +
                        "candidateN=" + n +
                        ", chordSpreadMm=" +
                        (preflightChordSpread * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", relativeSpreadPct=" +
                        (preflightRelativeChordSpread * 100.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", equalGate=False" +
                        ", targetSpacing=TRUE_EDGE_EQUAL_ARC");

                    targetSketch =
                        EnsureSame3DSketchEditing(
                            model,
                            ReacquireSame3DSketchForReadback(
                                model,
                                owningSketchFeature,
                                owningSketchName,
                                "V35.7 reduce N=" + n),
                            owningSketchFeature,
                            owningSketchName);

                    originalSplineSegment =
                        ReacquireSketchSegmentByKey(
                            targetSketch,
                            splineKeyBefore);

                    originalSpline =
                        originalSplineSegment as SketchSpline;

                    if (originalSpline == null)
                    {
                        throw new InvalidOperationException(
                            "Mất SAME spline trước reduce N=" + n + ".");
                    }

                    VerifySameSplineIdentityOrThrow(
                        originalSpline,
                        selectedSpline,
                        splineKeyBefore,
                        "V35.7 before reduce N=" + n);

                    List<SketchPoint> acceptedPoints =
                        OrderSplineFitPointsAlongTargetEdge(
                            GetSplineFitPoints(originalSpline),
                            targetSnapshot,
                            reverseTarget);

                    if (acceptedPoints.Count != bestPointCount)
                    {
                        throw new InvalidOperationException(
                            "Point count trước reduce không khớp best topology. " +
                            acceptedPoints.Count + "/" + bestPointCount);
                    }

                    // V35.7 deletes the point that causes the smallest total
                    // re-indexing travel when N points become N-1 equal-arc
                    // points.  The old local-chord heuristic repeatedly chose
                    // index 1, concentrating knot/topology changes at one end.
                    int deleteIndex =
                        FindMinimumRedistributionDeleteIndex(
                            acceptedPoints.Count);

                    if (deleteIndex <= 0 ||
                        deleteIndex >= acceptedPoints.Count - 1)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V36.8 REDUCE STOP " +
                            "reason=no-interior-point, bestN=" + bestPointCount);
                        break;
                    }

                    SketchPoint pointToDelete =
                        acceptedPoints[deleteIndex];

                    bool deleted = false;

                    try
                    {
                        deleted =
                            originalSpline.DeletePoint(
                                pointToDelete);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V35.7 REDUCE DeletePoint exception " +
                            "targetN=" + n + ", " + ex.Message);
                    }

                    if (!deleted)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V36.8 REDUCE STOP " +
                            "reason=delete-failed, targetN=" + n);
                        break;
                    }

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V36.8 REDUCE DELETE " +
                        "fromN=" + bestPointCount +
                        ", targetN=" + n +
                        ", deleteIndex=" + deleteIndex +
                        ", strategy=MIN_REINDEX_TRAVEL");

                    VerifySameSplineIdentityOrThrow(
                        originalSpline,
                        selectedSpline,
                        splineKeyBefore,
                        "V35.7 after delete N=" + n);

                    bool reduceRebuildOk =
                        TryRebuildForMove(
                            model,
                            "V35.7 after reduce N=" + n);

                    List<double[]> targets =
                        preflightTargets;

                    double candidateChordMin =
                        preflightChordMin;

                    double candidateChordMax =
                        preflightChordMax;

                    double candidateChordSpread =
                        preflightChordSpread;

                    double candidateRelativeChordSpread =
                        preflightRelativeChordSpread;

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V36.8 EQUAL-ARC TARGET " +
                        "points=" + n +
                        ", chordMinMm=" +
                        (candidateChordMin * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", chordMaxMm=" +
                        (candidateChordMax * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", spreadMm=" +
                        (candidateChordSpread * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", relativeSpreadPct=" +
                        (candidateRelativeChordSpread * 100.0)
                            .ToString("0.######", CultureInfo.InvariantCulture));

                    DirectSplineMoveResult move =
                        new DirectSplineMoveResult
                        {
                            MaximumTargetError = double.MaxValue
                        };

                    DirectSplineCandidateResult candidate =
                        new DirectSplineCandidateResult
                        {
                            MaxTargetError = double.MaxValue,
                            ActualSplineDeviation = double.MaxValue,
                            Success = false
                        };

                    if (reduceRebuildOk)
                    {
                        move =
                            MoveSameSplineFitPointsByMoveOrCopyVerified(
                                model,
                                owningSketchFeature,
                                owningSketchName,
                                splineKeyBefore,
                                selectedSpline,
                                targets,
                                targetSnapshot,
                                reverseTarget,
                                8);

                        if (move.Success)
                        {
                            candidate =
                                EvaluateDirectSameSplineCandidate(
                                    model,
                                    owningSketchFeature,
                                    owningSketchName,
                                    splineKeyBefore,
                                    selectedSpline,
                                    targets,
                                    targetSnapshot,
                                    reverseTarget,
                                    reduceGeometryToleranceMm);
                        }
                    }

                    // V37.2: do NOT run V37.1 adaptive trials on a reduced
                    // topology either.  A passing reduced topology is already a
                    // valid candidate; preserve its native state exactly as-is.

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V36.8 MIN-POINT TEST " +
                        "points=" + n +
                        ", recommendedSeed=" + recommendedPointCount +
                        ", rebuild=" + reduceRebuildOk +
                        ", moved=" + move.Success +
                        ", maxTargetMm=" +
                        (candidate.MaxTargetError * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", actualDeviationMm=" +
                        (candidate.ActualSplineDeviation * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", toleranceMm=" +
                        reduceGeometryToleranceMm
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", pass=" + candidate.Success);

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V36.8 MINPOINT VERIFY " +
                        "candidateN=" + n +
                        ", geometryPass=" + candidate.Success +
                        ", equalGate=False" +
                        ", relativeSpreadPct=" +
                        (candidateRelativeChordSpread * 100.0)
                            .ToString("0.######", CultureInfo.InvariantCulture));

                    if (reduceRebuildOk &&
                        move.Success &&
                        candidate.Success)
                    {
                        previousAcceptedDeviation =
                            bestActualDeviation;
                        previousAcceptedPointCount =
                            bestPointCount;

                        bestPointCount = n;
                        bestTargets = targets;
                        bestActualDeviation =
                            candidate.ActualSplineDeviation;
                        bestChordSpread =
                            candidateChordSpread;
                        bestRelativeChordSpread =
                            candidateRelativeChordSpread;
                        reducedAtLeastOnce = true;
                        continue;
                    }

                    // ----------------------------------------------------
                    // V36.5 TRUE GEOMETRY/NATIVE FAIL ROLLBACK:
                    // Equal-readiness can no longer reach this path because it
                    // is checked before DeletePoint.  This fallback is now only
                    // for an unexpected rebuild/move/native-geometry failure.
                    // NEVER use SketchSpline.InsertPoint here.
                    // Native runtime V35.6 showed that InsertPoint can restore
                    // only the COUNT while changing spline parameterization,
                    // producing the visible loop/self-crossing geometry.
                    // Instead ask SOLIDWORKS native Undo to restore the exact
                    // pre-delete state, then verify SAME spline + point count.
                    // ----------------------------------------------------
                    string undoError;
                    bool undoRestored =
                        TryUndoSameSplineBackToPointCount(
                            model,
                            owningSketchFeature,
                            owningSketchName,
                            splineKeyBefore,
                            selectedSpline,
                            bestPointCount,
                            MoveReduceUndoMaximumSteps,
                            out undoError);

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V36.5 NATIVE-UNDO RESULT " +
                        "restoreN=" + bestPointCount +
                        ", success=" + undoRestored +
                        ", error=" + (undoError ?? ""));

                    if (!undoRestored)
                    {
                        throw new InvalidOperationException(
                            "Candidate N=" + n +
                            " fail và native Undo không restore được topology N=" +
                            bestPointCount + ". " + (undoError ?? ""));
                    }

                    DirectSplineMoveResult rollbackMove =
                        MoveSameSplineFitPointsByMoveOrCopyVerified(
                            model,
                            owningSketchFeature,
                            owningSketchName,
                            splineKeyBefore,
                            selectedSpline,
                            bestTargets,
                            targetSnapshot,
                            reverseTarget,
                            8);

                    if (!rollbackMove.Success)
                    {
                        throw new InvalidOperationException(
                            "Native Undo đã restore count nhưng không refit lại được " +
                            "best topology N=" + bestPointCount + ". " +
                            (rollbackMove.Error ?? ""));
                    }

                    DirectSplineCandidateResult rollbackCandidate =
                        EvaluateDirectSameSplineCandidate(
                            model,
                            owningSketchFeature,
                            owningSketchName,
                            splineKeyBefore,
                            selectedSpline,
                            bestTargets,
                            targetSnapshot,
                            reverseTarget,
                            reduceGeometryToleranceMm);

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V36.6 ROLLBACK VERIFY " +
                        "bestN=" + bestPointCount +
                        ", actualDeviationMm=" +
                        (rollbackCandidate.ActualSplineDeviation * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", pass=" + rollbackCandidate.Success);

                    if (!rollbackCandidate.Success)
                    {
                        throw new InvalidOperationException(
                            "Native Undo restore N=" + bestPointCount +
                            " nhưng geometry không còn PASS tolerance.");
                    }

                    break;
                }

                if (bestPointCount <= reusableReductionFloor)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V37.4 REDUCE STOP " +
                        "reason=reusable-topology-floor" +
                        ", bestN=" + bestPointCount +
                        ", reserveFloor=" + ReusableSplineReservePointCount +
                        ", effectiveFloor=" + reusableReductionFloor);
                }

                // At this point the SAME spline is already on the smallest
                // accepted topology allowed by the reusable reserve policy.
                // No generic restore/grow is allowed.
                List<double[]> finalTargets =
                    bestTargets;

                DirectSplineCandidateResult finalCandidate =
                    EvaluateDirectSameSplineCandidate(
                        model,
                        owningSketchFeature,
                        owningSketchName,
                        splineKeyBefore,
                        selectedSpline,
                        finalTargets,
                        targetSnapshot,
                        reverseTarget,
                        reduceGeometryToleranceMm);

                if (!finalCandidate.Success)
                {
                    throw new InvalidOperationException(
                        "V35.6 final topology không PASS. N=" +
                        bestPointCount +
                        ", deviationMm=" +
                        (finalCandidate.ActualSplineDeviation * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture));
                }

                // --------------------------------------------------------
                // V36.8 STEP 6 - FINAL MANUAL-EQUAL READY STATE.
                //
                // Keep the proven SAME SketchSpline and recreate only the useful
                // relation/helper network:
                //   - fit point -> selected Edge
                //   - first/last fit point -> Edge vertices
                //   - construction helper chain between the SAME fit points
                //
                // DO NOT apply Equal automatically.  The user will select the
                // helper chain and add Equal manually in SOLIDWORKS.
                // The preserved one-hop center line is also left in the sketch.
                // --------------------------------------------------------
                targetSketch =
                    EnsureSame3DSketchEditing(
                        model,
                        ReacquireSame3DSketchForReadback(
                            model,
                            owningSketchFeature,
                            owningSketchName,
                            "V36.8 final manual-equal relation rebuild"),
                        owningSketchFeature,
                        owningSketchName);

                originalSplineSegment =
                    ReacquireSketchSegmentByKey(
                        targetSketch,
                        splineKeyBefore);

                originalSpline =
                    originalSplineSegment as SketchSpline;

                if (originalSpline == null)
                {
                    throw new InvalidOperationException(
                        "Mất SAME spline trước final manual-equal rebuild.");
                }

                VerifySameSplineIdentityOrThrow(
                    originalSpline,
                    selectedSpline,
                    splineKeyBefore,
                    "V36.8 final manual-equal relation rebuild");

                List<SketchPoint> finalFitPoints =
                    OrderSplineFitPointsAlongTargetEdge(
                        GetSplineFitPoints(
                            originalSpline),
                        targetSnapshot,
                        reverseTarget);

                Edge finalTargetEdge =
                    ReacquireTargetEdge(
                        model,
                        targetReference);

                EqualRelationResult finalRelationResult =
                    BuildEqualSpacingRelations(
                        swApp,
                        model,
                        targetSketch,
                        finalTargetEdge,
                        finalFitPoints,
                        false);

                List<SketchSegment> finalHelpers =
                    finalRelationResult.CreatedConstructionLines ??
                    new List<SketchSegment>();

                if (finalHelpers.Count != bestPointCount - 1)
                {
                    throw new InvalidOperationException(
                        "Không tạo đủ helper line cuối cho manual Equal. " +
                        finalHelpers.Count + "/" +
                        (bestPointCount - 1));
                }

                double finalChordMin;
                double finalChordMax;

                MeasureChordRange(
                    finalTargets,
                    out finalChordMin,
                    out finalChordMax);

                // V37.0: no automatic Equal and no centerline recreation.
                // Straight construction center lines were preserved in-place;
                // only connected construction center SPLINES were removed.

                // Clear the command's selection once, synchronously. No timer,
                // no RunCommand(1721), no post-command relation scan.
                model.ClearSelection2(true);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V37.0 FINAL MANUAL-EQUAL READY " +
                    "sameSpline=True" +
                    ", points=" + oldPointCount + "->" + bestPointCount +
                    ", actualDeviationMm=" +
                    (finalCandidate.ActualSplineDeviation * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", reduceToleranceMm=" +
                    reduceGeometryToleranceMm
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", normalMoveToleranceMm=" +
                    moveActualSplineToleranceMm
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", pointOnEdge=" +
                    finalRelationResult.PointOnEdgeCount +
                    "/" + finalFitPoints.Count +
                    ", endpoint=" +
                    finalRelationResult.EndpointCoincidentCount +
                    "/" + expectedEndpointRelations +
                    ", helperLines=" + finalHelpers.Count +
                    ", chordSpreadMm=" +
                    ((finalChordMax - finalChordMin) * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", equalApplied=False" +
                    ", deletedCenterSplines=" + blockerCleanup.DeletedConstructionSplines +
                    ", preservedStraightCenterLines=" + blockerCleanup.PreservedStraightLines +
                    ", noDeferredEqual=True" +
                    ", reusableReserveFloor=" + ReusableSplineReservePointCount +
                    ", reusableReserveSatisfied=" +
                    (bestPointCount >= ReusableSplineReservePointCount));

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V37.0 DONE " +
                    "manualEqualRequired=True" +
                    ", sameSplinePreserved=True" +
                    ", straightCenterLinePreserved=True" +
                    ", constructionCenterSplinesDeleted=" +
                    blockerCleanup.DeletedConstructionSplines);

                // V37.0.4: final native SOLIDWORKS notification only. At this point all SolidWorks
                // geometry/relation work is already complete. Do not schedule
                // a timer or run any more geometry/relation operations after
                // the notification; simply tell the user that MOVE finished.
                try
                {
                    string completionMessage =
                        "Hoàn thành MOVE spline." + System.Environment.NewLine +
                        System.Environment.NewLine +
                        "Fit point: " +
                        oldPointCount + " -> " + bestPointCount +
                        System.Environment.NewLine +
                        "Reusable reserve: >= " +
                        ReusableSplineReservePointCount + " point" +
                        System.Environment.NewLine +
                        "Sai lệch max: " +
                        (finalCandidate.ActualSplineDeviation * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        " mm" +
                        System.Environment.NewLine +
                        "Center spline đã xóa: " +
                        blockerCleanup.DeletedConstructionSplines +
                        System.Environment.NewLine +
                        "Center line thẳng giữ lại: " +
                        blockerCleanup.PreservedStraightLines +
                        System.Environment.NewLine +
                        "Equal: chưa áp dụng - thực hiện thủ công.";

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V37.2 COMPLETION MESSAGE BEGIN via=SolidWorks.SendMsgToUser2");

                    // Use SOLIDWORKS' own message box instead of a WinForms modal dialog.
                    // The previous managed MessageBox returned normally, but SOLIDWORKS
                    // exited immediately after the command unwound from that modal UI.
                    // Keeping the notification native to SOLIDWORKS avoids mixing modal
                    // WinForms lifetime with the active SOLIDWORKS command stack.
                    ShowMessage(
                        completionMessage,
                        swMessageBoxIcon_e.swMbInformation);

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V37.2 COMPLETION MESSAGE SHOWN via=SolidWorks.SendMsgToUser2");
                }
                catch (Exception popupError)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V37.2 COMPLETION MESSAGE ERROR: " +
                        popupError.Message);
                }
            }
            catch (Exception moveError)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V35.3 STOP key=" +
                    splineKeyBefore +
                    "; SAME spline requested=True; " +
                    moveError);

                throw;
            }
        }

        private DirectMoveProbeResult ProbeSameSplineFitPointMoveOrCopy(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName,
            string expectedSplineKey,
            SketchSpline selectedSpline,
            int expectedPointCount,
            int pointIndex,
            double[] target)
        {
            DirectMoveProbeResult result =
                new DirectMoveProbeResult
                {
                    BeforeTargetError = double.MaxValue,
                    AfterTargetError = double.MaxValue,
                    RestoreError = double.MaxValue
                };

            if (model == null ||
                owningSketchFeature == null ||
                string.IsNullOrWhiteSpace(expectedSplineKey) ||
                selectedSpline == null ||
                expectedPointCount < MinimumPointCount ||
                pointIndex < 0 ||
                pointIndex >= expectedPointCount ||
                !IsPoint(target))
            {
                result.Error = "Thiếu dữ liệu route probe.";
                return result;
            }

            ModelDocExtension extension =
                model.Extension as ModelDocExtension;

            if (extension == null)
            {
                result.Error = "ModelDocExtension null.";
                return result;
            }

            Sketch sketch =
                EnsureSame3DSketchEditing(
                    model,
                    ReacquireSame3DSketchForReadback(
                        model,
                        owningSketchFeature,
                        owningSketchName,
                        "V35.3 route probe begin"),
                    owningSketchFeature,
                    owningSketchName);

            SketchSegment segment =
                ReacquireSketchSegmentByKey(
                    sketch,
                    expectedSplineKey);

            SketchSpline spline =
                segment as SketchSpline;

            if (spline == null)
            {
                result.Error = "Mất SAME spline trước route probe.";
                return result;
            }

            List<SketchPoint> points =
                GetSplineFitPoints(spline);

            if (points.Count != expectedPointCount)
            {
                result.Error =
                    "Point count route probe sai: " +
                    points.Count + "/" + expectedPointCount;
                return result;
            }

            SketchPoint point =
                points[pointIndex];

            double[] before =
                new double[]
                {
                    point.X,
                    point.Y,
                    point.Z
                };

            result.BeforeTargetError =
                Distance(before, target);

            model.ClearSelection2(true);

            try
            {
                result.Selected =
                    point.Select4(false, null);
            }
            catch (Exception ex)
            {
                result.Error =
                    "Select4 probe exception: " + ex.Message;
                return result;
            }

            if (!result.Selected)
            {
                result.Error = "Select4 probe returned false.";
                return result;
            }

            try
            {
                extension.MoveOrCopy(
                    false,
                    0,
                    true,
                    before[0],
                    before[1],
                    before[2],
                    target[0],
                    target[1],
                    target[2]);

                result.CallReturned = true;
            }
            catch (Exception ex)
            {
                result.Error =
                    "MoveOrCopy probe exception: " + ex.Message;
            }
            finally
            {
                try
                {
                    model.ClearSelection2(true);
                }
                catch
                {
                }
            }

            if (!result.CallReturned)
                return result;

            result.RebuildOk =
                TryRebuildForMove(
                    model,
                    "V35.3 route probe");

            sketch =
                ReacquireSame3DSketchForReadback(
                    model,
                    owningSketchFeature,
                    owningSketchName,
                    "V35.3 route probe readback");

            segment =
                ReacquireSketchSegmentByKey(
                    sketch,
                    expectedSplineKey);

            spline =
                segment as SketchSpline;

            if (spline == null)
            {
                result.Error = "Mất SAME spline sau route probe.";
                return result;
            }

            string keyAfter =
                GetSketchSegmentKey(segment);

            result.SameSpline =
                string.Equals(
                    expectedSplineKey,
                    keyAfter,
                    StringComparison.Ordinal) ||
                IsSameComObject(
                    spline,
                    selectedSpline);

            points =
                GetSplineFitPoints(spline);

            if (points.Count != expectedPointCount)
            {
                result.Error =
                    "Route probe làm đổi point count.";
                return result;
            }

            SketchPoint afterPoint =
                points[pointIndex];

            double[] after =
                new double[]
                {
                    afterPoint.X,
                    afterPoint.Y,
                    afterPoint.Z
                };

            result.ActualMovement =
                Distance(before, after);

            result.AfterTargetError =
                Distance(after, target);

            // A route is only accepted if native readback proves the point is
            // actually at the requested target.  Return values alone are never
            // accepted as proof.
            result.Success =
                result.RebuildOk &&
                result.SameSpline &&
                IsFinite(result.AfterTargetError) &&
                result.AfterTargetError <= 5.0e-5 &&
                IsFinite(result.ActualMovement) &&
                (result.BeforeTargetError <= 5.0e-5 ||
                 result.ActualMovement > 1.0e-6);

            if (result.Success)
            {
                result.RestoreError = 0.0;
                return result;
            }

            // If the probe partially moved the point but did not reach target,
            // restore that one point before returning failure.  This keeps the
            // diagnostic non-destructive whenever MoveOrCopy is reversible.
            if (IsFinite(result.ActualMovement) &&
                result.ActualMovement > 1.0e-7)
            {
                result.RestoreAttempted = true;

                sketch =
                    EnsureSame3DSketchEditing(
                        model,
                        sketch,
                        owningSketchFeature,
                        owningSketchName);

                segment =
                    ReacquireSketchSegmentByKey(
                        sketch,
                        expectedSplineKey);

                spline =
                    segment as SketchSpline;

                if (spline != null)
                {
                    points =
                        GetSplineFitPoints(spline);

                    if (points.Count == expectedPointCount)
                    {
                        SketchPoint restorePoint =
                            points[pointIndex];

                        double rx = restorePoint.X;
                        double ry = restorePoint.Y;
                        double rz = restorePoint.Z;

                        model.ClearSelection2(true);

                        bool restoreSelected = false;

                        try
                        {
                            restoreSelected =
                                restorePoint.Select4(false, null);
                        }
                        catch
                        {
                        }

                        if (restoreSelected)
                        {
                            try
                            {
                                extension.MoveOrCopy(
                                    false,
                                    0,
                                    true,
                                    rx,
                                    ry,
                                    rz,
                                    before[0],
                                    before[1],
                                    before[2]);
                            }
                            catch
                            {
                            }

                            model.ClearSelection2(true);
                            TryRebuildForMove(
                                model,
                                "V35.3 route probe restore");

                            Sketch readbackSketch =
                                ReacquireSame3DSketchForReadback(
                                    model,
                                    owningSketchFeature,
                                    owningSketchName,
                                    "V35.3 route probe restore readback");

                            SketchSegment readbackSegment =
                                ReacquireSketchSegmentByKey(
                                    readbackSketch,
                                    expectedSplineKey);

                            SketchSpline readbackSpline =
                                readbackSegment as SketchSpline;

                            if (readbackSpline != null)
                            {
                                List<SketchPoint> readbackPoints =
                                    GetSplineFitPoints(readbackSpline);

                                if (readbackPoints.Count == expectedPointCount)
                                {
                                    result.RestoreError =
                                        DistanceSketchPointToArray(
                                            readbackPoints[pointIndex],
                                            before);
                                }
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(result.Error))
            {
                result.Error =
                    "MoveOrCopy call có chạy nhưng native fit point không tới target.";
            }

            return result;
        }


        private CoherentMorphResult MoveSameSplineByCoherentMorph(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName,
            string expectedSplineKey,
            SketchSpline selectedSpline,
            List<string> stablePointKeys,
            List<double[]> originalPoints,
            List<double[]> finalTargets)
        {
            CoherentMorphResult result =
                new CoherentMorphResult
                {
                    MaximumPointError = double.MaxValue,
                    TwistGuardPassed = true
                };

            if (model == null ||
                owningSketchFeature == null ||
                selectedSpline == null ||
                string.IsNullOrWhiteSpace(expectedSplineKey) ||
                stablePointKeys == null ||
                originalPoints == null ||
                finalTargets == null ||
                originalPoints.Count < MinimumPointCount ||
                originalPoints.Count != finalTargets.Count ||
                stablePointKeys.Count != originalPoints.Count)
            {
                result.Error = "Thiếu dữ liệu coherent morph.";
                return result;
            }

            result.TotalStages = 0;

            // Read-only baseline sanity. No SketchSpline property is modified.
            MorphSanity baselineSanity =
                MeasureSplineMorphSanity(
                    model,
                    owningSketchFeature,
                    owningSketchName,
                    expectedSplineKey,
                    selectedSpline,
                    originalPoints);

            List<double[]> prealignedTargets =
                BuildBestFitSimilarityTargets(
                    originalPoints,
                    finalTargets);

            if (prealignedTargets.Count != finalTargets.Count)
            {
                result.Error = "Không tính được best-fit 3D pre-align.";
                return result;
            }

            double meanChord =
                GetMeanPolylineChordLength(finalTargets);

            int prealignStages =
                DetermineMorphStageCount(
                    originalPoints,
                    prealignedTargets,
                    meanChord,
                    MoveMorphMaximumStepToMeanChordRatio);

            int rawFinalStages =
                DetermineMorphStageCount(
                    prealignedTargets,
                    finalTargets,
                    meanChord,
                    MoveMorphMaximumTargetStepToMeanChordRatio);

            int finalStages =
                rawFinalStages <= 0
                    ? 0
                    : Math.Min(
                        MoveMorphMaximumStagesPerLeg,
                        Math.Max(
                            MoveMorphMinimumTargetStages,
                            rawFinalStages));

            result.TotalStages =
                prealignStages + finalStages;

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V37.5 COHERENT MORPH BEGIN " +
                "points=" + originalPoints.Count +
                ", prealignStages=" + prealignStages +
                ", finalStages=" + finalStages +
                ", targetStageFloor=" + MoveMorphMinimumTargetStages +
                ", targetStepRatio=" +
                MoveMorphMaximumTargetStepToMeanChordRatio.ToString(
                    "0.###", CultureInfo.InvariantCulture) +
                ", baselineLengthRatio=" +
                baselineSanity.LengthToPointPolylineRatio.ToString(
                    "0.######", CultureInfo.InvariantCulture) +
                ", baselineBacktrack=" +
                baselineSanity.MaximumBacktrackFraction.ToString(
                    "0.######", CultureInfo.InvariantCulture) +
                ", propertyWrites=NONE");

            List<double[]> legStart =
                ClonePointList(originalPoints);

            int globalStage = 0;

            for (int leg = 0; leg < 2; leg++)
            {
                List<double[]> legEnd =
                    leg == 0
                        ? prealignedTargets
                        : finalTargets;

                int stageCount =
                    leg == 0
                        ? prealignStages
                        : finalStages;

                for (int stage = 1;
                     stage <= stageCount;
                     stage++)
                {
                    double t =
                        stage / (double)stageCount;

                    List<double[]> stageTargets =
                        InterpolatePointLists(
                            legStart,
                            legEnd,
                            t);

                    globalStage++;

                    StagePointMoveResult stageMove =
                        MoveStableSameSplinePointsOneStage(
                            model,
                            owningSketchFeature,
                            owningSketchName,
                            expectedSplineKey,
                            selectedSpline,
                            stablePointKeys,
                            stageTargets,
                            "V37.3 morph stage " + globalStage);

                    result.MoveCallCount +=
                        stageMove.MoveCallCount;

                    result.MaximumPointError =
                        stageMove.MaximumPointError;

                    if (!stageMove.Success)
                    {
                        result.Error =
                            "Stage " + globalStage +
                            " không đạt XYZ target: " +
                            (stageMove.Error ?? "");

                        return result;
                    }

                    MorphSanity sanity =
                        MeasureSplineMorphSanity(
                            model,
                            owningSketchFeature,
                            owningSketchName,
                            expectedSplineKey,
                            selectedSpline,
                            stageTargets);

                    bool lengthOk =
                        sanity.Valid &&
                        sanity.LengthToPointPolylineRatio <=
                            Math.Max(
                                MoveMorphMaximumLengthRatioFloor,
                                baselineSanity.Valid
                                    ? baselineSanity.LengthToPointPolylineRatio *
                                      MoveMorphLengthRatioGrowthLimit
                                    : MoveMorphMaximumLengthRatioFloor);

                    bool backtrackOk =
                        sanity.Valid &&
                        sanity.MaximumBacktrackFraction <=
                            Math.Max(
                                MoveMorphBacktrackFloor,
                                baselineSanity.Valid
                                    ? baselineSanity.MaximumBacktrackFraction +
                                      MoveMorphBacktrackGrowthAllowance
                                    : MoveMorphBacktrackFloor);

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V37.5 MORPH STAGE " +
                        "stage=" + globalStage +
                        "/" + result.TotalStages +
                        ", leg=" + (leg == 0 ? "PREALIGN" : "TARGET") +
                        ", t=" + t.ToString("0.###", CultureInfo.InvariantCulture) +
                        ", maxPointErrorMm=" +
                        (stageMove.MaximumPointError * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture) +
                        ", splineLengthRatio=" +
                        sanity.LengthToPointPolylineRatio.ToString(
                            "0.######", CultureInfo.InvariantCulture) +
                        ", backtrack=" +
                        sanity.MaximumBacktrackFraction.ToString(
                            "0.######", CultureInfo.InvariantCulture) +
                        ", lengthOk=" + lengthOk +
                        ", backtrackOk=" + backtrackOk +
                        ", propertyWrites=NONE");

                    if (!lengthOk || !backtrackOk)
                    {
                        result.TwistGuardPassed = false;
                        result.Error =
                            "TWIST GUARD dừng ở stage " +
                            globalStage +
                            ": splineLengthRatio=" +
                            sanity.LengthToPointPolylineRatio.ToString(
                                "0.######", CultureInfo.InvariantCulture) +
                            ", backtrack=" +
                            sanity.MaximumBacktrackFraction.ToString(
                                "0.######", CultureInfo.InvariantCulture) +
                            ". Không tiếp tục làm biến dạng SAME spline.";

                        return result;
                    }

                    result.CompletedStages =
                        globalStage;
                }

                legStart =
                    ClonePointList(legEnd);
            }

            // Final identity + target readback in the ORIGINAL point order.
            Sketch finalSketch =
                ReacquireSame3DSketchForReadback(
                    model,
                    owningSketchFeature,
                    owningSketchName,
                    "V37.3 coherent morph final");

            SketchSegment finalSegment =
                ReacquireSketchSegmentByKey(
                    finalSketch,
                    expectedSplineKey);

            SketchSpline finalSpline =
                finalSegment as SketchSpline;

            result.SameSpline =
                finalSpline != null &&
                (string.Equals(
                     expectedSplineKey,
                     GetSketchSegmentKey(finalSegment),
                     StringComparison.Ordinal) ||
                 IsSameComObject(finalSpline, selectedSpline));

            if (!result.SameSpline)
            {
                result.Error = "Mất SAME SketchSpline sau coherent morph.";
                return result;
            }

            List<SketchPoint> finalPoints =
                GetSplineFitPointsInStableOrder(
                    finalSpline,
                    stablePointKeys);

            if (finalPoints.Count != finalTargets.Count)
            {
                result.Error = "Point count đổi sau coherent morph.";
                return result;
            }

            result.MaximumPointError =
                GetMaximumPointTargetDistance(
                    finalPoints,
                    finalTargets);

            result.Success =
                result.TwistGuardPassed &&
                result.SameSpline &&
                IsFinite(result.MaximumPointError) &&
                result.MaximumPointError <=
                    MoveMorphMaximumPointReadbackErrorM;

            if (!result.Success &&
                string.IsNullOrWhiteSpace(result.Error))
            {
                result.Error =
                    "Final fit-point error=" +
                    (result.MaximumPointError * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    " mm.";
            }

            return result;
        }

        private StagePointMoveResult MoveStableSameSplinePointsOneStage(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName,
            string expectedSplineKey,
            SketchSpline selectedSpline,
            List<string> stablePointKeys,
            List<double[]> stageTargets,
            string stageName)
        {
            StagePointMoveResult result =
                new StagePointMoveResult
                {
                    MaximumPointError = double.MaxValue
                };

            ModelDocExtension extension =
                model == null
                    ? null
                    : model.Extension as ModelDocExtension;

            if (extension == null ||
                owningSketchFeature == null ||
                selectedSpline == null ||
                stablePointKeys == null ||
                stageTargets == null ||
                stablePointKeys.Count != stageTargets.Count)
            {
                result.Error = "Thiếu dữ liệu stage move.";
                return result;
            }

            Sketch sketch =
                EnsureSame3DSketchEditing(
                    model,
                    ReacquireSame3DSketchForReadback(
                        model,
                        owningSketchFeature,
                        owningSketchName,
                        stageName + " begin"),
                    owningSketchFeature,
                    owningSketchName);

            SketchSegment segment =
                ReacquireSketchSegmentByKey(
                    sketch,
                    expectedSplineKey);

            SketchSpline spline =
                segment as SketchSpline;

            if (spline == null ||
                (!string.Equals(
                    expectedSplineKey,
                    GetSketchSegmentKey(segment),
                    StringComparison.Ordinal) &&
                 !IsSameComObject(spline, selectedSpline)))
            {
                result.Error = "Mất SAME spline trước stage move.";
                return result;
            }

            List<SketchPoint> points =
                GetSplineFitPointsInStableOrder(
                    spline,
                    stablePointKeys);

            if (points.Count != stageTargets.Count)
            {
                result.Error =
                    "Stable point order không khớp: " +
                    points.Count + "/" + stageTargets.Count;
                return result;
            }

            // Move interior points first, then endpoints.  The stage displacement
            // is small and all points are rebuilt together only once afterwards.
            // No SketchSpline property setter is called here.
            List<int> order =
                new List<int>();

            int centerLeft =
                (points.Count - 1) / 2;

            int centerRight =
                centerLeft + 1;

            for (int offset = 0;
                 order.Count < points.Count;
                 offset++)
            {
                int a =
                    centerLeft - offset;

                int b =
                    centerRight + offset;

                if (a >= 0 &&
                    !order.Contains(a))
                {
                    order.Add(a);
                }

                if (b < points.Count &&
                    !order.Contains(b))
                {
                    order.Add(b);
                }
            }

            foreach (int index in order)
            {
                SketchPoint point =
                    points[index];

                double[] target =
                    stageTargets[index];

                if (point == null ||
                    !IsPoint(target))
                {
                    result.Error = "Point/target null trong stage.";
                    return result;
                }

                double beforeError =
                    DistanceSketchPointToArray(
                        point,
                        target);

                if (IsFinite(beforeError) &&
                    beforeError <=
                        MoveMorphMaximumPointReadbackErrorM)
                {
                    continue;
                }

                double bx = point.X;
                double by = point.Y;
                double bz = point.Z;

                model.ClearSelection2(true);

                bool selected = false;

                try
                {
                    selected =
                        point.Select4(false, null);
                }
                catch
                {
                }

                if (!selected)
                {
                    result.Error =
                        "Không select được fit point index=" + index;
                    return result;
                }

                try
                {
                    extension.MoveOrCopy(
                        false,
                        0,
                        true,
                        bx,
                        by,
                        bz,
                        target[0],
                        target[1],
                        target[2]);

                    result.MoveCallCount++;
                }
                catch (Exception ex)
                {
                    result.Error =
                        "MoveOrCopy stage exception index=" +
                        index + ": " + ex.Message;

                    return result;
                }
            }

            model.ClearSelection2(true);

            if (!TryRebuildForMove(
                    model,
                    stageName))
            {
                result.Error = "EditRebuild3=false ở " + stageName;
                return result;
            }

            sketch =
                ReacquireSame3DSketchForReadback(
                    model,
                    owningSketchFeature,
                    owningSketchName,
                    stageName + " readback");

            segment =
                ReacquireSketchSegmentByKey(
                    sketch,
                    expectedSplineKey);

            spline =
                segment as SketchSpline;

            if (spline == null ||
                (!string.Equals(
                    expectedSplineKey,
                    GetSketchSegmentKey(segment),
                    StringComparison.Ordinal) &&
                 !IsSameComObject(spline, selectedSpline)))
            {
                result.Error = "Mất SAME spline sau " + stageName;
                return result;
            }

            points =
                GetSplineFitPointsInStableOrder(
                    spline,
                    stablePointKeys);

            if (points.Count != stageTargets.Count)
            {
                result.Error = "Point count đổi sau " + stageName;
                return result;
            }

            result.MaximumPointError =
                GetMaximumPointTargetDistance(
                    points,
                    stageTargets);

            result.Success =
                IsFinite(result.MaximumPointError) &&
                result.MaximumPointError <=
                    MoveMorphMaximumPointReadbackErrorM;

            if (!result.Success)
            {
                result.Error =
                    "maxPointErrorMm=" +
                    (result.MaximumPointError * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture);
            }

            return result;
        }

        private static List<SketchPoint> GetSplineFitPointsInStableOrder(
            SketchSpline spline,
            List<string> stablePointKeys)
        {
            List<SketchPoint> raw =
                GetSplineFitPoints(spline);

            if (stablePointKeys == null ||
                stablePointKeys.Count == 0 ||
                raw.Count != stablePointKeys.Count)
            {
                return raw;
            }

            Dictionary<string, SketchPoint> byKey =
                new Dictionary<string, SketchPoint>(
                    StringComparer.Ordinal);

            foreach (SketchPoint point in raw)
            {
                string key =
                    GetSketchPointKey(point);

                if (!string.IsNullOrWhiteSpace(key) &&
                    !byKey.ContainsKey(key))
                {
                    byKey.Add(key, point);
                }
            }

            List<SketchPoint> ordered =
                new List<SketchPoint>();

            foreach (string key in stablePointKeys)
            {
                SketchPoint point;

                if (!byKey.TryGetValue(
                        key,
                        out point))
                {
                    // GetPoints2 order is the same order used to create the
                    // original key list.  If a SOLIDWORKS version regenerates
                    // point IDs after rebuild, prefer this native order rather
                    // than re-sorting against the target Edge.
                    return raw;
                }

                ordered.Add(point);
            }

            return ordered;
        }

        private static List<double[]> BuildBestFitSimilarityTargets(
            List<double[]> source,
            List<double[]> target)
        {
            List<double[]> result =
                new List<double[]>();

            if (source == null ||
                target == null ||
                source.Count != target.Count ||
                source.Count < 2)
            {
                return result;
            }

            double[] cs = new double[3];
            double[] ct = new double[3];

            for (int i = 0; i < source.Count; i++)
            {
                if (!IsPoint(source[i]) ||
                    !IsPoint(target[i]))
                {
                    return new List<double[]>();
                }

                for (int k = 0; k < 3; k++)
                {
                    cs[k] += source[i][k];
                    ct[k] += target[i][k];
                }
            }

            for (int k = 0; k < 3; k++)
            {
                cs[k] /= source.Count;
                ct[k] /= source.Count;
            }

            double sxx = 0, sxy = 0, sxz = 0;
            double syx = 0, syy = 0, syz = 0;
            double szx = 0, szy = 0, szz = 0;
            double sourceEnergy = 0.0;

            for (int i = 0; i < source.Count; i++)
            {
                double px = source[i][0] - cs[0];
                double py = source[i][1] - cs[1];
                double pz = source[i][2] - cs[2];

                double qx = target[i][0] - ct[0];
                double qy = target[i][1] - ct[1];
                double qz = target[i][2] - ct[2];

                sxx += px * qx;
                sxy += px * qy;
                sxz += px * qz;
                syx += py * qx;
                syy += py * qy;
                syz += py * qz;
                szx += pz * qx;
                szy += pz * qy;
                szz += pz * qz;

                sourceEnergy +=
                    px * px + py * py + pz * pz;
            }

            double[,] n =
            {
                { sxx + syy + szz, syz - szy,       szx - sxz,       sxy - syx },
                { syz - szy,       sxx - syy - szz, sxy + syx,       szx + sxz },
                { szx - sxz,       sxy + syx,      -sxx + syy - szz, syz + szy },
                { sxy - syx,       szx + sxz,       syz + szy,      -sxx - syy + szz }
            };

            // Power iteration must target the LARGEST ALGEBRAIC
            // eigenvalue of Horn's symmetric 4x4 matrix.  The raw matrix can
            // have +/- eigenvalues with the same magnitude, so plain power
            // iteration may oscillate or converge to the wrong quaternion.
            // Add a positive diagonal shift; eigenvectors are unchanged while
            // every shifted eigenvalue becomes positive.
            double spectralShift = 1.0;

            for (int r = 0; r < 4; r++)
            {
                double rowAbs = 0.0;

                for (int c = 0; c < 4; c++)
                {
                    rowAbs += Math.Abs(n[r, c]);
                }

                spectralShift =
                    Math.Max(spectralShift, rowAbs + 1.0);
            }

            double[] q =
                { 1.0, 0.0, 0.0, 0.0 };

            for (int iter = 0; iter < 64; iter++)
            {
                double[] next = new double[4];

                for (int r = 0; r < 4; r++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        double value =
                            n[r, c] +
                            (r == c ? spectralShift : 0.0);

                        next[r] += value * q[c];
                    }
                }

                double norm =
                    Math.Sqrt(
                        next[0] * next[0] +
                        next[1] * next[1] +
                        next[2] * next[2] +
                        next[3] * next[3]);

                if (!IsFinite(norm) ||
                    norm <= 1.0e-20)
                {
                    q = new[] { 1.0, 0.0, 0.0, 0.0 };
                    break;
                }

                for (int k = 0; k < 4; k++)
                {
                    q[k] = next[k] / norm;
                }
            }

            double w = q[0];
            double x = q[1];
            double y = q[2];
            double z = q[3];

            double[,] rmat =
            {
                { 1 - 2 * (y * y + z * z), 2 * (x * y - z * w),     2 * (x * z + y * w) },
                { 2 * (x * y + z * w),     1 - 2 * (x * x + z * z), 2 * (y * z - x * w) },
                { 2 * (x * z - y * w),     2 * (y * z + x * w),     1 - 2 * (x * x + y * y) }
            };

            double numerator = 0.0;

            for (int i = 0; i < source.Count; i++)
            {
                double px = source[i][0] - cs[0];
                double py = source[i][1] - cs[1];
                double pz = source[i][2] - cs[2];

                double rx = rmat[0, 0] * px + rmat[0, 1] * py + rmat[0, 2] * pz;
                double ry = rmat[1, 0] * px + rmat[1, 1] * py + rmat[1, 2] * pz;
                double rz = rmat[2, 0] * px + rmat[2, 1] * py + rmat[2, 2] * pz;

                double qx = target[i][0] - ct[0];
                double qy = target[i][1] - ct[1];
                double qz = target[i][2] - ct[2];

                numerator +=
                    qx * rx + qy * ry + qz * rz;
            }

            double scale =
                sourceEnergy <= 1.0e-20
                    ? 1.0
                    : numerator / sourceEnergy;

            if (!IsFinite(scale) ||
                scale <= 1.0e-6)
            {
                scale = 1.0;
            }

            scale =
                Math.Max(
                    0.05,
                    Math.Min(20.0, scale));

            for (int i = 0; i < source.Count; i++)
            {
                double px = source[i][0] - cs[0];
                double py = source[i][1] - cs[1];
                double pz = source[i][2] - cs[2];

                double rx = rmat[0, 0] * px + rmat[0, 1] * py + rmat[0, 2] * pz;
                double ry = rmat[1, 0] * px + rmat[1, 1] * py + rmat[1, 2] * pz;
                double rz = rmat[2, 0] * px + rmat[2, 1] * py + rmat[2, 2] * pz;

                result.Add(
                    new[]
                    {
                        ct[0] + scale * rx,
                        ct[1] + scale * ry,
                        ct[2] + scale * rz
                    });
            }

            return result;
        }

        private static List<double[]> InterpolatePointLists(
            List<double[]> start,
            List<double[]> end,
            double t)
        {
            List<double[]> result =
                new List<double[]>();

            if (start == null ||
                end == null ||
                start.Count != end.Count)
            {
                return result;
            }

            t =
                Math.Max(0.0, Math.Min(1.0, t));

            for (int i = 0; i < start.Count; i++)
            {
                if (!IsPoint(start[i]) ||
                    !IsPoint(end[i]))
                {
                    return new List<double[]>();
                }

                result.Add(
                    new[]
                    {
                        start[i][0] + t * (end[i][0] - start[i][0]),
                        start[i][1] + t * (end[i][1] - start[i][1]),
                        start[i][2] + t * (end[i][2] - start[i][2])
                    });
            }

            return result;
        }

        private static int DetermineMorphStageCount(
            List<double[]> start,
            List<double[]> end,
            double meanChord,
            double maximumStepToMeanChordRatio)
        {
            double maximumMove = 0.0;

            if (start != null &&
                end != null &&
                start.Count == end.Count)
            {
                for (int i = 0; i < start.Count; i++)
                {
                    double d =
                        DistanceBetweenArrays(
                            start[i],
                            end[i]);

                    if (IsFinite(d) &&
                        d > maximumMove)
                    {
                        maximumMove = d;
                    }
                }
            }

            if (maximumMove <=
                MoveMorphMaximumPointReadbackErrorM)
            {
                return 0;
            }

            double safeStepRatio =
                IsFinite(maximumStepToMeanChordRatio) &&
                maximumStepToMeanChordRatio > 1.0e-6
                    ? maximumStepToMeanChordRatio
                    : MoveMorphMaximumStepToMeanChordRatio;

            double allowedStep =
                IsFinite(meanChord) &&
                meanChord > 1.0e-9
                    ? meanChord * safeStepRatio
                    : 0.025;

            allowedStep =
                Math.Max(0.005, allowedStep);

            int stages =
                (int)Math.Ceiling(
                    maximumMove /
                    allowedStep);

            return Math.Max(
                MoveMorphMinimumStagesPerLeg,
                Math.Min(
                    MoveMorphMaximumStagesPerLeg,
                    stages));
        }

        private static double GetMeanPolylineChordLength(
            List<double[]> points)
        {
            if (points == null ||
                points.Count < 2)
            {
                return 0.0;
            }

            double sum = 0.0;
            int count = 0;

            for (int i = 0; i < points.Count - 1; i++)
            {
                double d =
                    DistanceBetweenArrays(
                        points[i],
                        points[i + 1]);

                if (IsFinite(d) && d > 0.0)
                {
                    sum += d;
                    count++;
                }
            }

            return count == 0
                ? 0.0
                : sum / count;
        }

        private MorphSanity MeasureSplineMorphSanity(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName,
            string expectedSplineKey,
            SketchSpline selectedSpline,
            List<double[]> expectedPointPolyline)
        {
            MorphSanity result =
                new MorphSanity();

            if (model == null ||
                owningSketchFeature == null ||
                selectedSpline == null ||
                expectedPointPolyline == null ||
                expectedPointPolyline.Count < 2)
            {
                return result;
            }

            Sketch sketch =
                ReacquireSame3DSketchForReadback(
                    model,
                    owningSketchFeature,
                    owningSketchName,
                    "V37.3 morph sanity");

            SketchSegment segment =
                ReacquireSketchSegmentByKey(
                    sketch,
                    expectedSplineKey);

            SketchSpline spline =
                segment as SketchSpline;

            if (spline == null ||
                (!string.Equals(
                    expectedSplineKey,
                    GetSketchSegmentKey(segment),
                    StringComparison.Ordinal) &&
                 !IsSameComObject(spline, selectedSpline)))
            {
                return result;
            }

            Curve curve =
                null;

            try
            {
                curve =
                    segment.GetCurve()
                    as Curve;
            }
            catch
            {
            }

            if (curve == null)
                return result;

            double u0 = 0.0;
            double u1 = 0.0;
            bool closed = false;
            bool periodic = false;
            bool got = false;

            try
            {
                got =
                    curve.GetEndParams(
                        out u0,
                        out u1,
                        out closed,
                        out periodic);
            }
            catch
            {
            }

            if (!got)
                return result;

            if (u1 < u0)
            {
                double swap = u0;
                u0 = u1;
                u1 = swap;
            }

            double splineLength =
                GetCurveLength(
                    curve,
                    u0,
                    u1);

            double pointPolylineLength = 0.0;

            for (int i = 0;
                 i < expectedPointPolyline.Count - 1;
                 i++)
            {
                double d =
                    DistanceBetweenArrays(
                        expectedPointPolyline[i],
                        expectedPointPolyline[i + 1]);

                if (!IsFinite(d))
                    return result;

                pointPolylineLength += d;
            }

            if (!IsFinite(splineLength) ||
                splineLength <= 1.0e-12 ||
                pointPolylineLength <= 1.0e-12)
            {
                return result;
            }

            List<double[]> samples =
                BuildCurvePolylineByArcLength(
                    null,
                    curve,
                    u0,
                    u1,
                    splineLength,
                    MoveMorphSanitySamples);

            if (samples.Count < 2)
                return result;

            double forwardEndpointCost =
                DistanceBetweenArrays(
                    samples[0],
                    expectedPointPolyline[0]) +
                DistanceBetweenArrays(
                    samples[samples.Count - 1],
                    expectedPointPolyline[expectedPointPolyline.Count - 1]);

            double reverseEndpointCost =
                DistanceBetweenArrays(
                    samples[0],
                    expectedPointPolyline[expectedPointPolyline.Count - 1]) +
                DistanceBetweenArrays(
                    samples[samples.Count - 1],
                    expectedPointPolyline[0]);

            if (reverseEndpointCost < forwardEndpointCost)
            {
                samples.Reverse();
            }

            double previousProgress =
                -double.MaxValue;

            double maximumBacktrack =
                0.0;

            foreach (double[] sample in samples)
            {
                double progress =
                    GetPointPolylineProgress(
                        sample,
                        expectedPointPolyline);

                if (!IsFinite(progress))
                    return result;

                if (previousProgress > -double.MaxValue / 2.0 &&
                    progress < previousProgress)
                {
                    maximumBacktrack =
                        Math.Max(
                            maximumBacktrack,
                            previousProgress - progress);
                }

                previousProgress =
                    Math.Max(previousProgress, progress);
            }

            result.LengthToPointPolylineRatio =
                splineLength /
                pointPolylineLength;

            result.MaximumBacktrackFraction =
                expectedPointPolyline.Count <= 1
                    ? 0.0
                    : maximumBacktrack /
                      (expectedPointPolyline.Count - 1);

            result.Valid =
                IsFinite(result.LengthToPointPolylineRatio) &&
                IsFinite(result.MaximumBacktrackFraction);

            return result;
        }

        private static double GetPointPolylineProgress(
            double[] point,
            List<double[]> polyline)
        {
            if (!IsPoint(point) ||
                polyline == null ||
                polyline.Count < 2)
            {
                return double.MaxValue;
            }

            double bestD2 =
                double.MaxValue;

            double bestProgress =
                0.0;

            for (int i = 0;
                 i < polyline.Count - 1;
                 i++)
            {
                double[] a = polyline[i];
                double[] b = polyline[i + 1];

                if (!IsPoint(a) || !IsPoint(b))
                    continue;

                double abx = b[0] - a[0];
                double aby = b[1] - a[1];
                double abz = b[2] - a[2];

                double apx = point[0] - a[0];
                double apy = point[1] - a[1];
                double apz = point[2] - a[2];

                double denom =
                    abx * abx + aby * aby + abz * abz;

                double t =
                    denom <= 1.0e-30
                        ? 0.0
                        : (apx * abx + apy * aby + apz * abz) /
                          denom;

                t =
                    Math.Max(0.0, Math.Min(1.0, t));

                double qx = a[0] + t * abx;
                double qy = a[1] + t * aby;
                double qz = a[2] + t * abz;

                double dx = point[0] - qx;
                double dy = point[1] - qy;
                double dz = point[2] - qz;

                double d2 =
                    dx * dx + dy * dy + dz * dz;

                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    bestProgress = i + t;
                }
            }

            return bestProgress;
        }

        private DirectSplineMoveResult MoveSameSplineFitPointsByMoveOrCopyVerified(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName,
            string expectedSplineKey,
            SketchSpline selectedSpline,
            List<double[]> targets,
            List<double[]> targetSnapshot,
            bool reverseTarget,
            int maximumPasses)
        {
            DirectSplineMoveResult result =
                new DirectSplineMoveResult
                {
                    MaximumTargetError = double.MaxValue
                };

            if (model == null ||
                owningSketchFeature == null ||
                string.IsNullOrWhiteSpace(expectedSplineKey) ||
                selectedSpline == null ||
                targets == null ||
                targetSnapshot == null ||
                targetSnapshot.Count < 2 ||
                targets.Count < MinimumPointCount)
            {
                result.Error = "Thiếu dữ liệu direct fit.";
                return result;
            }

            ModelDocExtension extension =
                model.Extension as ModelDocExtension;

            if (extension == null)
            {
                result.Error = "ModelDocExtension null.";
                return result;
            }

            maximumPasses =
                Math.Max(1, Math.Min(12, maximumPasses));

            double bestError =
                double.MaxValue;

            int stallCount = 0;

            for (int pass = 0;
                 pass < maximumPasses;
                 pass++)
            {
                Sketch sketch =
                    EnsureSame3DSketchEditing(
                        model,
                        ReacquireSame3DSketchForReadback(
                            model,
                            owningSketchFeature,
                            owningSketchName,
                            "V35.3 direct move pass " + pass),
                        owningSketchFeature,
                        owningSketchName);

                SketchSegment segment =
                    ReacquireSketchSegmentByKey(
                        sketch,
                        expectedSplineKey);

                SketchSpline spline =
                    segment as SketchSpline;

                if (spline == null)
                {
                    result.Error = "Mất SAME spline ở direct move pass=" + pass;
                    return result;
                }

                if (!string.Equals(
                        expectedSplineKey,
                        GetSketchSegmentKey(segment),
                        StringComparison.Ordinal) &&
                    !IsSameComObject(spline, selectedSpline))
                {
                    result.Error = "Identity SAME spline đổi ở direct move.";
                    return result;
                }

                List<SketchPoint> points =
                    OrderSplineFitPointsAlongTargetEdge(
                        GetSplineFitPoints(spline),
                        targetSnapshot,
                        reverseTarget);

                if (points.Count != targets.Count)
                {
                    result.Error =
                        "Point count direct move sai: " +
                        points.Count + "/" + targets.Count;
                    return result;
                }

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V35.5 POINT ORDER " +
                    "pass=" + pass +
                    ", count=" + points.Count +
                    ", reverseTarget=" + reverseTarget +
                    ", orderedBy=TARGET_EDGE_ARC");

                // Move endpoints first, then interior fit points.
                List<int> order =
                    new List<int>();

                order.Add(0);
                order.Add(points.Count - 1);

                for (int i = 1;
                     i < points.Count - 1;
                     i++)
                {
                    order.Add(i);
                }

                int callsThisPass = 0;

                foreach (int index in order)
                {
                    SketchPoint point =
                        points[index];

                    double[] target =
                        targets[index];

                    if (point == null ||
                        !IsPoint(target))
                    {
                        continue;
                    }

                    double beforeError =
                        DistanceSketchPointToArray(
                            point,
                            target);

                    if (IsFinite(beforeError) &&
                        beforeError <= 5.0e-5)
                    {
                        continue;
                    }

                    double bx = point.X;
                    double by = point.Y;
                    double bz = point.Z;

                    model.ClearSelection2(true);

                    bool selected = false;

                    try
                    {
                        selected =
                            point.Select4(false, null);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V35.3 DIRECT " +
                            "pass=" + pass +
                            ", point=" + index +
                            ", select exception=" + ex.Message);
                    }

                    if (!selected)
                        continue;

                    try
                    {
                        extension.MoveOrCopy(
                            false,
                            0,
                            true,
                            bx,
                            by,
                            bz,
                            target[0],
                            target[1],
                            target[2]);

                        callsThisPass++;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V35.3 DIRECT " +
                            "pass=" + pass +
                            ", point=" + index +
                            ", MoveOrCopy exception=" + ex.Message);
                    }
                }

                model.ClearSelection2(true);

                bool rebuildOk =
                    TryRebuildForMove(
                        model,
                        "V35.3 direct move pass " + pass);

                if (!rebuildOk)
                {
                    result.Error =
                        "EditRebuild3 false ở direct move pass=" + pass;
                    return result;
                }

                sketch =
                    ReacquireSame3DSketchForReadback(
                        model,
                        owningSketchFeature,
                        owningSketchName,
                        "V35.3 direct move readback " + pass);

                segment =
                    ReacquireSketchSegmentByKey(
                        sketch,
                        expectedSplineKey);

                spline =
                    segment as SketchSpline;

                if (spline == null)
                {
                    result.Error =
                        "Mất SAME spline sau direct move pass=" + pass;
                    return result;
                }

                points =
                    OrderSplineFitPointsAlongTargetEdge(
                        GetSplineFitPoints(spline),
                        targetSnapshot,
                        reverseTarget);

                if (points.Count != targets.Count)
                {
                    result.Error =
                        "Point count đổi sau direct move pass=" + pass;
                    return result;
                }

                double maximumError =
                    GetMaximumPointTargetDistance(
                        points,
                        targets);

                result.MaximumTargetError =
                    maximumError;

                result.Passes =
                    pass + 1;

                result.MoveCallCount +=
                    callsThisPass;

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V35.3 DIRECT RESULT " +
                    "pass=" + pass +
                    ", calls=" + callsThisPass +
                    ", maxTargetErrorMm=" +
                    (maximumError * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture));

                if (IsFinite(maximumError) &&
                    maximumError <= 5.0e-5)
                {
                    result.Success = true;
                    return result;
                }

                double progress =
                    bestError - maximumError;

                if (maximumError < bestError)
                {
                    bestError = maximumError;
                }

                if (!IsFinite(progress) ||
                    progress < 1.0e-7)
                {
                    stallCount++;
                }
                else
                {
                    stallCount = 0;
                }

                if (stallCount >= 2)
                {
                    result.Error =
                        "Direct MoveOrCopy stalled; maxTargetErrorMm=" +
                        (maximumError * 1000.0)
                            .ToString("0.######", CultureInfo.InvariantCulture);
                    return result;
                }
            }

            result.Error =
                "Direct MoveOrCopy hết pass nhưng chưa tới target.";

            return result;
        }


        private static List<SketchPoint> OrderSplineFitPointsAlongTargetEdge(
            List<SketchPoint> points,
            List<double[]> targetSnapshot,
            bool reverseTarget)
        {
            List<SketchPoint> ordered =
                new List<SketchPoint>();

            if (points == null)
                return ordered;

            if (targetSnapshot == null ||
                targetSnapshot.Count < 2)
            {
                ordered.AddRange(points);
                return ordered;
            }

            List<KeyValuePair<double, SketchPoint>> scored =
                new List<KeyValuePair<double, SketchPoint>>();

            foreach (SketchPoint point in points)
            {
                if (point == null)
                    continue;

                double bestDistanceSquared =
                    double.MaxValue;

                double bestProgress =
                    0.0;

                for (int i = 0;
                     i < targetSnapshot.Count - 1;
                     i++)
                {
                    double[] a =
                        targetSnapshot[i];

                    double[] b =
                        targetSnapshot[i + 1];

                    if (!IsPoint(a) ||
                        !IsPoint(b))
                    {
                        continue;
                    }

                    double abx =
                        b[0] - a[0];

                    double aby =
                        b[1] - a[1];

                    double abz =
                        b[2] - a[2];

                    double apx =
                        point.X - a[0];

                    double apy =
                        point.Y - a[1];

                    double apz =
                        point.Z - a[2];

                    double denom =
                        abx * abx +
                        aby * aby +
                        abz * abz;

                    double t =
                        denom <= 1.0e-30
                            ? 0.0
                            : (apx * abx +
                               apy * aby +
                               apz * abz) /
                              denom;

                    t =
                        Math.Max(
                            0.0,
                            Math.Min(
                                1.0,
                                t));

                    double qx =
                        a[0] +
                        t * abx;

                    double qy =
                        a[1] +
                        t * aby;

                    double qz =
                        a[2] +
                        t * abz;

                    double dx =
                        point.X - qx;

                    double dy =
                        point.Y - qy;

                    double dz =
                        point.Z - qz;

                    double d2 =
                        dx * dx +
                        dy * dy +
                        dz * dz;

                    if (d2 <
                        bestDistanceSquared)
                    {
                        bestDistanceSquared =
                            d2;

                        bestProgress =
                            i + t;
                    }
                }

                double score =
                    reverseTarget
                        ? -bestProgress
                        : bestProgress;

                scored.Add(
                    new KeyValuePair<double, SketchPoint>(
                        score,
                        point));
            }

            scored.Sort(
                (a, b) =>
                    a.Key.CompareTo(
                        b.Key));

            foreach (KeyValuePair<double, SketchPoint> item in
                     scored)
            {
                ordered.Add(
                    item.Value);
            }

            return ordered;
        }

        private AdaptiveSameSplineTargetResult OptimizeSameSplineFitPointDistributionOnEdge(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName,
            string expectedSplineKey,
            SketchSpline selectedSpline,
            List<double[]> startingTargets,
            List<double[]> targetSnapshot,
            bool reverseTarget,
            double toleranceMm)
        {
            AdaptiveSameSplineTargetResult result =
                new AdaptiveSameSplineTargetResult
                {
                    Candidate =
                        new DirectSplineCandidateResult
                        {
                            MaxTargetError = double.MaxValue,
                            ActualSplineDeviation = double.MaxValue,
                            Success = false
                        }
                };

            if (model == null ||
                owningSketchFeature == null ||
                selectedSpline == null ||
                startingTargets == null ||
                startingTargets.Count < MinimumPointCount ||
                targetSnapshot == null ||
                targetSnapshot.Count < 2 ||
                toleranceMm <= 0.0)
            {
                result.Error = "Thiếu dữ liệu adaptive distribution.";
                return result;
            }

            int pointCount =
                startingTargets.Count;

            List<double[]> bestTargets =
                ClonePointList(
                    startingTargets);

            List<double> fractions =
                InferDirectedTargetFractions(
                    bestTargets,
                    targetSnapshot,
                    reverseTarget);

            if (fractions.Count != pointCount)
            {
                fractions =
                    BuildUniformDirectedFractions(
                        pointCount);
            }

            DirectSplineCandidateResult bestCandidate =
                EvaluateDirectSameSplineCandidate(
                    model,
                    owningSketchFeature,
                    owningSketchName,
                    expectedSplineKey,
                    selectedSpline,
                    bestTargets,
                    targetSnapshot,
                    reverseTarget,
                    toleranceMm,
                    CreateSplineValidationSamples);

            result.Attempted = true;
            result.BeforeDeviation =
                bestCandidate.ActualSplineDeviation;

            double uniformSegment =
                1.0 / Math.Max(1, pointCount - 1);

            double step =
                uniformSegment *
                MoveAdaptiveInitialStepSegments;

            double minimumGap =
                uniformSegment *
                MoveAdaptiveMinimumGapSegments;

            double improvementEpsilon =
                MoveAdaptiveImprovementEpsilonMm /
                1000.0;

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V37.1 ADAPTIVE BEGIN " +
                "points=" + pointCount +
                ", beforeDeviationMm=" +
                (bestCandidate.ActualSplineDeviation * 1000.0)
                    .ToString("0.######", CultureInfo.InvariantCulture) +
                ", toleranceMm=" +
                toleranceMm.ToString("0.######", CultureInfo.InvariantCulture) +
                ", stepFraction=" +
                step.ToString("0.########", CultureInfo.InvariantCulture) +
                ", minGapFraction=" +
                minimumGap.ToString("0.########", CultureInfo.InvariantCulture));

            for (int sweep = 0;
                 sweep < MoveAdaptiveMaximumSweeps &&
                 result.Evaluations < MoveAdaptiveMaximumEvaluations;
                 sweep++)
            {
                bool sweepImproved = false;

                for (int index = 1;
                     index < pointCount - 1 &&
                     result.Evaluations < MoveAdaptiveMaximumEvaluations;
                     index++)
                {
                    List<double> baseFractions =
                        new List<double>(
                            fractions);

                    List<double[]> baseTargets =
                        ClonePointList(
                            bestTargets);

                    DirectSplineCandidateResult localBestCandidate =
                        bestCandidate;

                    List<double> localBestFractions =
                        baseFractions;

                    List<double[]> localBestTargets =
                        baseTargets;

                    double lower =
                        baseFractions[index - 1] +
                        minimumGap;

                    double upper =
                        baseFractions[index + 1] -
                        minimumGap;

                    if (upper <= lower + 1.0e-10)
                        continue;

                    double center =
                        baseFractions[index];

                    double[] trials =
                    {
                        Math.Max(lower, center - step),
                        Math.Min(upper, center + step)
                    };

                    for (int trialIndex = 0;
                         trialIndex < trials.Length &&
                         result.Evaluations < MoveAdaptiveMaximumEvaluations;
                         trialIndex++)
                    {
                        double trialFraction =
                            trials[trialIndex];

                        if (Math.Abs(trialFraction - center) < 1.0e-10)
                            continue;

                        List<double> trialFractions =
                            new List<double>(
                                baseFractions);

                        trialFractions[index] =
                            trialFraction;

                        List<double[]> trialTargets =
                            ClonePointList(
                                baseTargets);

                        trialTargets[index] =
                            InterpolateArcSnapshot(
                                targetSnapshot,
                                reverseTarget
                                    ? 1.0 - trialFraction
                                    : trialFraction);

                        DirectSplineMoveResult trialMove =
                            MoveSameSplineFitPointsByMoveOrCopyVerified(
                                model,
                                owningSketchFeature,
                                owningSketchName,
                                expectedSplineKey,
                                selectedSpline,
                                trialTargets,
                                targetSnapshot,
                                reverseTarget,
                                6);

                        result.Evaluations++;

                        if (!trialMove.Success)
                        {
                            Debug.WriteLine(
                                "[EDGE EQUAL SPLINE] MOVE V37.1 ADAPTIVE TRIAL " +
                                "sweep=" + sweep +
                                ", point=" + index +
                                ", fraction=" +
                                trialFraction.ToString("0.########", CultureInfo.InvariantCulture) +
                                ", moved=False" +
                                ", error=" + (trialMove.Error ?? ""));
                            continue;
                        }

                        DirectSplineCandidateResult trialCandidate =
                            EvaluateDirectSameSplineCandidate(
                                model,
                                owningSketchFeature,
                                owningSketchName,
                                expectedSplineKey,
                                selectedSpline,
                                trialTargets,
                                targetSnapshot,
                                reverseTarget,
                                toleranceMm,
                                MoveAdaptiveTrialValidationSamples);

                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V37.1 ADAPTIVE TRIAL " +
                            "sweep=" + sweep +
                            ", point=" + index +
                            ", fraction=" +
                            trialFraction.ToString("0.########", CultureInfo.InvariantCulture) +
                            ", deviationMm=" +
                            (trialCandidate.ActualSplineDeviation * 1000.0)
                                .ToString("0.######", CultureInfo.InvariantCulture) +
                            ", localBestMm=" +
                            (localBestCandidate.ActualSplineDeviation * 1000.0)
                                .ToString("0.######", CultureInfo.InvariantCulture));

                        if (IsFinite(trialCandidate.ActualSplineDeviation) &&
                            (!IsFinite(localBestCandidate.ActualSplineDeviation) ||
                             trialCandidate.ActualSplineDeviation + improvementEpsilon <
                                localBestCandidate.ActualSplineDeviation))
                        {
                            localBestCandidate =
                                trialCandidate;

                            localBestFractions =
                                trialFractions;

                            localBestTargets =
                                trialTargets;
                        }
                    }

                    // Put native geometry on the best state found for this
                    // coordinate before continuing to the next fit point.
                    DirectSplineMoveResult settleMove =
                        MoveSameSplineFitPointsByMoveOrCopyVerified(
                            model,
                            owningSketchFeature,
                            owningSketchName,
                            expectedSplineKey,
                            selectedSpline,
                            localBestTargets,
                            targetSnapshot,
                            reverseTarget,
                            6);

                    if (!settleMove.Success)
                    {
                        result.Error =
                            "Không settle được adaptive best target ở point=" +
                            index + ". " + (settleMove.Error ?? "");
                        break;
                    }

                    if (IsFinite(localBestCandidate.ActualSplineDeviation) &&
                        (!IsFinite(bestCandidate.ActualSplineDeviation) ||
                         localBestCandidate.ActualSplineDeviation + improvementEpsilon <
                            bestCandidate.ActualSplineDeviation))
                    {
                        fractions =
                            localBestFractions;

                        bestTargets =
                            localBestTargets;

                        bestCandidate =
                            localBestCandidate;

                        sweepImproved = true;
                        result.Improved = true;
                    }
                }

                // Dense/native verification on the settled best state.  Trial
                // scans are intentionally lighter for speed; acceptance always
                // uses the normal dense MOVE validation sample count.
                DirectSplineMoveResult sweepSettle =
                    MoveSameSplineFitPointsByMoveOrCopyVerified(
                        model,
                        owningSketchFeature,
                        owningSketchName,
                        expectedSplineKey,
                        selectedSpline,
                        bestTargets,
                        targetSnapshot,
                        reverseTarget,
                        6);

                if (!sweepSettle.Success)
                {
                    result.Error =
                        "Adaptive sweep settle failed. " +
                        (sweepSettle.Error ?? "");
                    break;
                }

                bestCandidate =
                    EvaluateDirectSameSplineCandidate(
                        model,
                        owningSketchFeature,
                        owningSketchName,
                        expectedSplineKey,
                        selectedSpline,
                        bestTargets,
                        targetSnapshot,
                        reverseTarget,
                        toleranceMm,
                        CreateSplineValidationSamples);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V37.1 ADAPTIVE SWEEP RESULT " +
                    "sweep=" + sweep +
                    ", improved=" + sweepImproved +
                    ", evaluations=" + result.Evaluations +
                    ", deviationMm=" +
                    (bestCandidate.ActualSplineDeviation * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", pass=" + bestCandidate.Success);

                if (bestCandidate.Success)
                    break;

                step *=
                    sweepImproved
                        ? 0.70
                        : 0.50;

                if (step < uniformSegment * 0.015)
                    break;
            }

            // Final state is always the best target set discovered, never the
            // last trial.  This guarantees the caller sees a monotonic no-worse
            // SAME-spline state even when tolerance could not be reached.
            DirectSplineMoveResult finalMove =
                MoveSameSplineFitPointsByMoveOrCopyVerified(
                    model,
                    owningSketchFeature,
                    owningSketchName,
                    expectedSplineKey,
                    selectedSpline,
                    bestTargets,
                    targetSnapshot,
                    reverseTarget,
                    8);

            if (finalMove.Success)
            {
                bestCandidate =
                    EvaluateDirectSameSplineCandidate(
                        model,
                        owningSketchFeature,
                        owningSketchName,
                        expectedSplineKey,
                        selectedSpline,
                        bestTargets,
                        targetSnapshot,
                        reverseTarget,
                        toleranceMm,
                        CreateSplineValidationSamples);
            }

            result.Targets =
                bestTargets;

            result.Fractions =
                fractions;

            result.Candidate =
                bestCandidate;

            result.AfterDeviation =
                bestCandidate.ActualSplineDeviation;

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V37.1 ADAPTIVE END " +
                "points=" + pointCount +
                ", evaluations=" + result.Evaluations +
                ", improved=" + result.Improved +
                ", beforeDeviationMm=" +
                (result.BeforeDeviation * 1000.0)
                    .ToString("0.######", CultureInfo.InvariantCulture) +
                ", afterDeviationMm=" +
                (result.AfterDeviation * 1000.0)
                    .ToString("0.######", CultureInfo.InvariantCulture) +
                ", pass=" + bestCandidate.Success);

            return result;
        }

        private static void LogSketchSplineNativeAdjustmentApiSurface()
        {
            try
            {
                System.Reflection.MethodInfo[] methods =
                    typeof(SketchSpline).GetMethods();

                SortedSet<string> interesting =
                    new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (System.Reflection.MethodInfo method in methods)
                {
                    if (method == null || string.IsNullOrEmpty(method.Name))
                        continue;

                    string name =
                        method.Name;

                    string lower =
                        name.ToLowerInvariant();

                    if (lower.Contains("relax") ||
                        lower.Contains("reset") ||
                        lower.Contains("handle") ||
                        lower.Contains("control") ||
                        lower.Contains("param"))
                    {
                        string signature =
                            name + "(";

                        System.Reflection.ParameterInfo[] parameters =
                            method.GetParameters();

                        for (int i = 0; i < parameters.Length; i++)
                        {
                            if (i > 0)
                                signature += ", ";

                            string typeName =
                                parameters[i].ParameterType != null
                                    ? parameters[i].ParameterType.Name
                                    : "?";

                            signature +=
                                typeName + " " + parameters[i].Name;
                        }

                        signature += ")";
                        interesting.Add(signature);
                    }
                }

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V37.2 SKETCHSPLINE API PROBE count=" +
                    interesting.Count);

                foreach (string signature in interesting)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V37.2 SKETCHSPLINE API MEMBER " +
                        signature);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V37.2 SKETCHSPLINE API PROBE ERROR: " +
                    ex.Message);
            }
        }

        private static List<double[]> ClonePointList(
            List<double[]> points)
        {
            List<double[]> result =
                new List<double[]>();

            if (points == null)
                return result;

            foreach (double[] point in points)
            {
                if (!IsPoint(point))
                {
                    result.Add(null);
                    continue;
                }

                result.Add(
                    new[]
                    {
                        point[0],
                        point[1],
                        point[2]
                    });
            }

            return result;
        }

        private static List<double> BuildUniformDirectedFractions(
            int pointCount)
        {
            List<double> result =
                new List<double>();

            if (pointCount < 2)
                return result;

            for (int i = 0;
                 i < pointCount;
                 i++)
            {
                result.Add(
                    (double)i /
                    (pointCount - 1));
            }

            return result;
        }

        private static List<double> InferDirectedTargetFractions(
            List<double[]> targets,
            List<double[]> targetSnapshot,
            bool reverseTarget)
        {
            List<double> result =
                new List<double>();

            if (targets == null ||
                targetSnapshot == null ||
                targetSnapshot.Count < 2)
            {
                return result;
            }

            foreach (double[] target in targets)
            {
                double forward =
                    GetClosestPolylineProgressFraction(
                        target,
                        targetSnapshot);

                if (!IsFinite(forward))
                    return new List<double>();

                result.Add(
                    reverseTarget
                        ? 1.0 - forward
                        : forward);
            }

            if (result.Count != targets.Count)
                return new List<double>();

            result[0] = 0.0;
            result[result.Count - 1] = 1.0;

            for (int i = 1;
                 i < result.Count;
                 i++)
            {
                if (!IsFinite(result[i]) ||
                    result[i] <= result[i - 1] + 1.0e-8)
                {
                    return BuildUniformDirectedFractions(
                        targets.Count);
                }
            }

            return result;
        }

        private static double GetClosestPolylineProgressFraction(
            double[] point,
            List<double[]> polyline)
        {
            if (!IsPoint(point) ||
                polyline == null ||
                polyline.Count < 2)
            {
                return double.NaN;
            }

            double bestDistanceSquared =
                double.MaxValue;

            double bestProgress =
                0.0;

            for (int i = 0;
                 i < polyline.Count - 1;
                 i++)
            {
                double[] a =
                    polyline[i];

                double[] b =
                    polyline[i + 1];

                if (!IsPoint(a) ||
                    !IsPoint(b))
                {
                    continue;
                }

                double abx = b[0] - a[0];
                double aby = b[1] - a[1];
                double abz = b[2] - a[2];

                double apx = point[0] - a[0];
                double apy = point[1] - a[1];
                double apz = point[2] - a[2];

                double denom =
                    abx * abx +
                    aby * aby +
                    abz * abz;

                double t =
                    denom <= 1.0e-30
                        ? 0.0
                        : (apx * abx +
                           apy * aby +
                           apz * abz) /
                          denom;

                t =
                    Math.Max(
                        0.0,
                        Math.Min(
                            1.0,
                            t));

                double qx = a[0] + t * abx;
                double qy = a[1] + t * aby;
                double qz = a[2] + t * abz;

                double dx = point[0] - qx;
                double dy = point[1] - qy;
                double dz = point[2] - qz;

                double d2 =
                    dx * dx +
                    dy * dy +
                    dz * dz;

                if (d2 < bestDistanceSquared)
                {
                    bestDistanceSquared = d2;
                    bestProgress = i + t;
                }
            }

            return
                bestProgress /
                Math.Max(1, polyline.Count - 1);
        }

        private DirectSplineCandidateResult EvaluateDirectSameSplineCandidate(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName,
            string expectedSplineKey,
            SketchSpline selectedSpline,
            List<double[]> targets,
            List<double[]> targetSnapshot,
            bool reverseTarget,
            double toleranceMm,
            int validationSampleCount = CreateSplineValidationSamples)
        {
            DirectSplineCandidateResult result =
                new DirectSplineCandidateResult
                {
                    MaxTargetError = double.MaxValue,
                    ActualSplineDeviation = double.MaxValue
                };

            if (model == null ||
                owningSketchFeature == null ||
                selectedSpline == null ||
                targets == null ||
                targetSnapshot == null)
            {
                result.Error = "Thiếu dữ liệu candidate verify.";
                return result;
            }

            Sketch sketch =
                ReacquireSame3DSketchForReadback(
                    model,
                    owningSketchFeature,
                    owningSketchName,
                    "V35.3 candidate verify");

            SketchSegment segment =
                ReacquireSketchSegmentByKey(
                    sketch,
                    expectedSplineKey);

            SketchSpline spline =
                segment as SketchSpline;

            if (spline == null)
            {
                result.Error = "Mất SAME spline khi verify direct candidate.";
                return result;
            }

            result.SameSpline =
                string.Equals(
                    expectedSplineKey,
                    GetSketchSegmentKey(segment),
                    StringComparison.Ordinal) ||
                IsSameComObject(
                    spline,
                    selectedSpline);

            List<SketchPoint> points =
                OrderSplineFitPointsAlongTargetEdge(
                    GetSplineFitPoints(spline),
                    targetSnapshot,
                    reverseTarget);

            result.PointCount =
                points.Count;

            if (points.Count != targets.Count)
            {
                result.Error =
                    "Point count verify sai: " +
                    points.Count + "/" + targets.Count;
                return result;
            }

            result.MaxTargetError =
                GetMaximumPointTargetDistance(
                    points,
                    targets);

            result.ActualSplineDeviation =
                MeasureActualSplineToEdgeDeviation(
                    null,
                    null,
                    0,
                    0,
                    0,
                    segment,
                    Math.Max(64, validationSampleCount),
                    targetSnapshot);

            result.Success =
                result.SameSpline &&
                IsFinite(result.MaxTargetError) &&
                result.MaxTargetError <= 5.0e-5 &&
                IsFinite(result.ActualSplineDeviation) &&
                result.ActualSplineDeviation * 1000.0 <=
                    toleranceMm + 1.0e-9;

            if (!result.Success)
            {
                result.Error =
                    "maxTargetMm=" +
                    (result.MaxTargetError * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", deviationMm=" +
                    (result.ActualSplineDeviation * 1000.0)
                        .ToString("0.######", CultureInfo.InvariantCulture) +
                    ", toleranceMm=" +
                    toleranceMm.ToString("0.######", CultureInfo.InvariantCulture);
            }

            return result;
        }

        private SameSplineEdgeFitResult FitSameSplineToEdgeByCoincidentSolver(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName,
            SketchSpline selectedSpline,
            string expectedSplineKey,
            byte[] targetEdgeReference,
            bool targetEdgeClosed,
            int expectedEndpointRelations,
            List<double[]> targetPoints,
            List<double[]> targetSnapshot,
            double toleranceMm)
        {
            SameSplineEdgeFitResult result =
                new SameSplineEdgeFitResult
                {
                    MaxPointToEdgeError =
                        double.MaxValue,

                    MaxTargetError =
                        double.MaxValue,

                    EndpointVertexError =
                        double.MaxValue,

                    ActualSplineDeviation =
                        double.MaxValue
                };

            if (model == null ||
                owningSketchFeature == null ||
                selectedSpline == null ||
                string.IsNullOrWhiteSpace(
                    expectedSplineKey) ||
                targetEdgeReference == null ||
                targetEdgeReference.Length == 0 ||
                targetPoints == null ||
                targetPoints.Count <
                    MinimumPointCount ||
                targetSnapshot == null ||
                targetSnapshot.Count < 2)
            {
                result.Error =
                    "Thiếu dữ liệu V35 point-on-edge solver.";

                return result;
            }

            Sketch sketch =
                EnsureSame3DSketchEditing(
                    model,
                    ReacquireSame3DSketchForReadback(
                        model,
                        owningSketchFeature,
                        owningSketchName,
                        "V35 candidate begin"),
                    owningSketchFeature,
                    owningSketchName);

            SketchSegment splineSegment =
                ReacquireSketchSegmentByKey(
                    sketch,
                    expectedSplineKey);

            SketchSpline spline =
                splineSegment
                as SketchSpline;

            if (spline == null)
            {
                result.Error =
                    "Không reacquire được SAME spline cho V35 candidate.";

                return result;
            }

            List<SketchPoint> fitPoints =
                GetSplineFitPoints(
                    spline);

            if (fitPoints.Count !=
                targetPoints.Count)
            {
                result.Error =
                    "Fit-point count không khớp target. " +
                    fitPoints.Count +
                    "/" +
                    targetPoints.Count;

                return result;
            }

            Edge targetEdge =
                ReacquireTargetEdge(
                    model,
                    targetEdgeReference);

            if (targetEdge == null)
            {
                result.Error =
                    "Không reacquire được Edge mới.";

                return result;
            }

            // ------------------------------------------------------------
            // 1) Supported relation route: fit point directly on Edge.
            // ------------------------------------------------------------
            result.PointOnEdgeCallCount =
                AddPointOnEdgeRelations(
                    model,
                    targetEdge,
                    fitPoints);

            result.EndpointRelationCallCount =
                targetEdgeClosed
                    ? 0
                    : AddEndpointRelations(
                        model,
                        targetEdge,
                        fitPoints);

            TryRebuildForMove(
                model,
                "V35 after point-on-edge");

            // ------------------------------------------------------------
            // 2) With the point-on-edge relation already active, nudge the
            //    SAME fit points toward equal-arc target XYZ.  Failure of a
            //    single MoveOrCopy is NOT treated as proof of failure; the
            //    relation solver + helper equality gets the final say.
            // ------------------------------------------------------------
            result.NudgeMaximumTargetError =
                NudgeConstrainedSplineFitPointsTowardTargets(
                    model,
                    owningSketchFeature,
                    owningSketchName,
                    expectedSplineKey,
                    targetPoints);

            sketch =
                EnsureSame3DSketchEditing(
                    model,
                    ReacquireSame3DSketchForReadback(
                        model,
                        owningSketchFeature,
                        owningSketchName,
                        "V35 after target nudge"),
                    owningSketchFeature,
                    owningSketchName);

            splineSegment =
                ReacquireSketchSegmentByKey(
                    sketch,
                    expectedSplineKey);

            spline =
                splineSegment
                as SketchSpline;

            if (spline == null)
            {
                result.Error =
                    "Mất SAME spline sau V35 target nudge.";

                return result;
            }

            fitPoints =
                GetSplineFitPoints(
                    spline);

            if (fitPoints.Count !=
                targetPoints.Count)
            {
                result.Error =
                    "Point count đổi sau V35 target nudge.";

                return result;
            }

            // ------------------------------------------------------------
            // 3) Helper chain belongs to the SAME fit points.
            // ------------------------------------------------------------
            result.HelperLines =
                CreateConstructionChainOnly(
                    model,
                    fitPoints);

            if (result.HelperLines.Count !=
                fitPoints.Count - 1)
            {
                result.Error =
                    "Không tạo đủ helper line cho V35 candidate. " +
                    result.HelperLines.Count +
                    "/" +
                    (fitPoints.Count - 1);

                return result;
            }

            int equalConstrainedLineCount =
                0;

            result.EqualVerified =
                TryApplyEqualLengthRelationsManagerOnly(
                    sketch,
                    result.HelperLines,
                    out equalConstrainedLineCount);

            result.EqualConstrainedLineCount =
                equalConstrainedLineCount;

            // A few rebuild passes help large 3D changes settle without using
            // sgMERGEPOINTS or the UI Equal command.
            for (int pass = 0;
                 pass < 4;
                 pass++)
            {
                if (!TryRebuildForMove(
                        model,
                        "V35 settle pass " + pass))
                {
                    break;
                }
            }

            sketch =
                ExitSame3DSketchForControlFit(
                    model,
                    owningSketchFeature,
                    owningSketchName);

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V35 candidate curve readback forced.");

            splineSegment =
                ReacquireSketchSegmentByKey(
                    sketch,
                    expectedSplineKey);

            spline =
                splineSegment
                as SketchSpline;

            if (spline == null)
            {
                result.Error =
                    "Mất SAME spline khi verify V35 candidate.";

                return result;
            }

            fitPoints =
                GetSplineFitPoints(
                    spline);

            if (fitPoints.Count !=
                targetPoints.Count)
            {
                result.Error =
                    "Point count đổi khi verify V35 candidate.";

                return result;
            }

            string keyAfter =
                GetSketchSegmentKey(
                    splineSegment);

            result.SameSplineKey =
                string.Equals(
                    expectedSplineKey,
                    keyAfter,
                    StringComparison.Ordinal);

            result.SameSplineCom =
                IsSameComObject(
                    spline,
                    selectedSpline);

            targetEdge =
                ReacquireTargetEdge(
                    model,
                    targetEdgeReference);

            result.MaxPointToEdgeError =
                GetMaximumPointDistanceToEdge(
                    targetEdge,
                    fitPoints);

            result.MaxTargetError =
                GetMaximumPointTargetDistance(
                    fitPoints,
                    targetPoints);

            result.EndpointVertexError =
                targetEdgeClosed
                    ? 0.0
                    : GetEndpointVertexPairError(
                        targetEdge,
                        fitPoints);

            result.ActualSplineDeviation =
                MeasureActualSplineToEdgeDeviation(
                    null,
                    null,
                    0,
                    0,
                    0,
                    splineSegment,
                    CreateSplineValidationSamples,
                    targetSnapshot);

            bool endpointOk =
                targetEdgeClosed ||
                (IsFinite(
                     result.EndpointVertexError) &&
                 result.EndpointVertexError <=
                     1.0e-5);

            result.Success =
                (result.SameSplineKey ||
                 result.SameSplineCom) &&
                fitPoints.Count ==
                    targetPoints.Count &&
                IsFinite(
                    result.MaxPointToEdgeError) &&
                result.MaxPointToEdgeError <=
                    1.0e-5 &&
                endpointOk &&
                IsFinite(
                    result.ActualSplineDeviation) &&
                result.ActualSplineDeviation *
                    1000.0 <=
                    toleranceMm +
                        1.0e-9;

            if (!result.Success)
            {
                result.Error =
                    "candidate chưa đạt: pointEdgeMm=" +
                    (result.MaxPointToEdgeError *
                        1000.0)
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture) +
                    ", endpointMm=" +
                    (result.EndpointVertexError *
                        1000.0)
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture) +
                    ", splineDeviationMm=" +
                    (result.ActualSplineDeviation *
                        1000.0)
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture) +
                    ", toleranceMm=" +
                    toleranceMm.ToString(
                        "0.######",
                        CultureInfo.InvariantCulture) +
                    ", relationCalls=" +
                    result.PointOnEdgeCallCount +
                    "/" +
                    targetPoints.Count +
                    ", endpointCalls=" +
                    result.EndpointRelationCallCount +
                    "/" +
                    expectedEndpointRelations;
            }

            return result;
        }

        private static bool TryRebuildForMove(
            ModelDoc2 model,
            string stage)
        {
            if (model == null)
                return false;

            try
            {
                bool ok =
                    model.EditRebuild3();

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V35 REBUILD " +
                    "stage=\"" +
                    stage +
                    "\", result=" +
                    ok);

                return ok;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V35 REBUILD ERROR " +
                    "stage=\"" +
                    stage +
                    "\": " +
                    ex.Message);

                return false;
            }
        }

        private double NudgeConstrainedSplineFitPointsTowardTargets(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName,
            string expectedSplineKey,
            List<double[]> targets)
        {
            if (model == null ||
                owningSketchFeature == null ||
                string.IsNullOrWhiteSpace(
                    expectedSplineKey) ||
                targets == null ||
                targets.Count <
                    MinimumPointCount)
            {
                return double.MaxValue;
            }

            ModelDocExtension extension =
                model.Extension
                as ModelDocExtension;

            if (extension == null)
                return double.MaxValue;

            double bestMaximumError =
                double.MaxValue;

            int stalledPasses =
                0;

            // Keep this bounded.  This is only a solver hint; success is
            // decided later by actual spline-to-edge deviation.
            int maximumPasses =
                Math.Min(
                    10,
                    MoveNativeSolverMaximumPasses);

            for (int pass = 0;
                 pass < maximumPasses;
                 pass++)
            {
                Sketch sketch =
                    EnsureSame3DSketchEditing(
                        model,
                        ReacquireSame3DSketchForReadback(
                            model,
                            owningSketchFeature,
                            owningSketchName,
                            "V35 nudge pass " + pass),
                        owningSketchFeature,
                        owningSketchName);

                SketchSegment segment =
                    ReacquireSketchSegmentByKey(
                        sketch,
                        expectedSplineKey);

                SketchSpline spline =
                    segment
                    as SketchSpline;

                if (spline == null)
                    return double.MaxValue;

                List<SketchPoint> points =
                    GetSplineFitPoints(
                        spline);

                if (points.Count !=
                    targets.Count)
                {
                    return double.MaxValue;
                }

                // Endpoints are already vertex-coincident.  Move only the
                // interior points along their valid point-on-edge locus.
                for (int i = 1;
                     i < points.Count - 1;
                     i++)
                {
                    SketchPoint point =
                        points[i];

                    double[] target =
                        targets[i];

                    if (point == null ||
                        !IsPoint(
                            target))
                    {
                        continue;
                    }

                    double baseX =
                        point.X;

                    double baseY =
                        point.Y;

                    double baseZ =
                        point.Z;

                    model.ClearSelection2(
                        true);

                    bool selected =
                        false;

                    try
                    {
                        selected =
                            point.Select4(
                                false,
                                null);
                    }
                    catch
                    {
                    }

                    if (!selected)
                        continue;

                    try
                    {
                        extension.MoveOrCopy(
                            false,
                            0,
                            true,
                            baseX,
                            baseY,
                            baseZ,
                            target[0],
                            target[1],
                            target[2]);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V35 NUDGE " +
                            "pass=" +
                            pass +
                            ", point=" +
                            i +
                            ", MoveOrCopy exception=" +
                            ex.Message);
                    }
                }

                model.ClearSelection2(
                    true);

                TryRebuildForMove(
                    model,
                    "V35 nudge pass " + pass);

                sketch =
                    ReacquireSame3DSketchForReadback(
                        model,
                        owningSketchFeature,
                        owningSketchName,
                        "V35 nudge readback " + pass);

                segment =
                    ReacquireSketchSegmentByKey(
                        sketch,
                        expectedSplineKey);

                spline =
                    segment
                    as SketchSpline;

                if (spline == null)
                    return double.MaxValue;

                points =
                    GetSplineFitPoints(
                        spline);

                double maximumError =
                    GetMaximumPointTargetDistance(
                        points,
                        targets);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V35 NUDGE RESULT " +
                    "pass=" +
                    pass +
                    ", maxTargetErrorMm=" +
                    (maximumError *
                        1000.0)
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture));

                if (!IsFinite(
                        maximumError))
                {
                    return double.MaxValue;
                }

                if (maximumError <=
                    5.0e-5)
                {
                    return maximumError;
                }

                double progress =
                    bestMaximumError -
                    maximumError;

                if (maximumError <
                    bestMaximumError)
                {
                    bestMaximumError =
                        maximumError;
                }

                if (!IsFinite(
                        progress) ||
                    progress <
                        MoveNativeSolverMinimumProgress)
                {
                    stalledPasses++;
                }
                else
                {
                    stalledPasses =
                        0;
                }

                if (stalledPasses >=
                    MoveNativeSolverStallPassLimit)
                {
                    break;
                }
            }

            return bestMaximumError;
        }

        private static bool TryApplyEqualLengthNativeSketchPairwise(
            ModelDoc2 model,
            Sketch sketch,
            List<SketchSegment> constructionLines,
            out int constrainedLineCount,
            out int relationsAdded)
        {
            constrainedLineCount =
                0;

            relationsAdded =
                0;

            if (model == null ||
                sketch == null ||
                constructionLines == null ||
                constructionLines.Count < 2)
            {
                return false;
            }

            ISketchRelationManager manager =
                null;

            try
            {
                manager =
                    sketch.RelationManager;
            }
            catch
            {
            }

            if (manager == null)
                return false;

            int sameLengthType =
                (int)swConstraintType_e
                    .swConstraintType_SAMELENGTH;

            int beforeRelationCount =
                CountRelationsByType(
                    manager,
                    sameLengthType);

            // Reproduce the stable N-1 Equal network:
            // line[0]=line[1], line[1]=line[2], ...
            // One native sgEQUAL call is made for each adjacent pair.
            for (int i = 1;
                 i < constructionLines.Count;
                 i++)
            {
                SketchSegment first =
                    constructionLines[i - 1];

                SketchSegment second =
                    constructionLines[i];

                if (first == null ||
                    second == null)
                {
                    continue;
                }

                if (FindEqualRelationForSegmentPair(
                        manager,
                        first,
                        second,
                        sameLengthType) != null)
                {
                    continue;
                }

                model.ClearSelection2(
                    true);

                bool firstSelected =
                    false;

                bool secondSelected =
                    false;

                try
                {
                    firstSelected =
                        first.Select4(
                            false,
                            null);

                    secondSelected =
                        second.Select4(
                            true,
                            null);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V35.9 sgEQUAL SELECT " +
                        "pair=" + (i - 1) + "-" + i +
                        ", exception=" + ex.Message);
                }

                bool callCompleted =
                    false;

                if (firstSelected &&
                    secondSelected)
                {
                    try
                    {
                        model.SketchAddConstraints(
                            "sgEQUAL");

                        callCompleted =
                            true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V35.9 sgEQUAL CALL " +
                            "pair=" + (i - 1) + "-" + i +
                            ", exception=" + ex.Message);
                    }
                }

                SketchRelation verified =
                    FindEqualRelationForSegmentPair(
                        manager,
                        first,
                        second,
                        sameLengthType);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V35.9 sgEQUAL PAIR " +
                    "pair=" + (i - 1) + "-" + i +
                    ", selected=" + firstSelected + "/" + secondSelected +
                    ", callCompleted=" + callCompleted +
                    ", verified=" + (verified != null));
            }

            model.ClearSelection2(
                true);

            int afterRelationCount =
                CountRelationsByType(
                    manager,
                    sameLengthType);

            relationsAdded =
                Math.Max(
                    0,
                    afterRelationCount - beforeRelationCount);

            constrainedLineCount =
                CountEqualConstrainedLines(
                    manager,
                    constructionLines,
                    sameLengthType);

            bool verifiedAll =
                constrainedLineCount ==
                constructionLines.Count;

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V35.9 sgEQUAL RESULT " +
                "relationsBefore=" + beforeRelationCount +
                ", relationsAfter=" + afterRelationCount +
                ", relationsAdded=" + relationsAdded +
                ", constrained=" + constrainedLineCount +
                "/" + constructionLines.Count +
                ", verified=" + verifiedAll);

            return verifiedAll;
        }

        private static SketchRelation FindEqualRelationForSegmentPair(
            ISketchRelationManager manager,
            SketchSegment first,
            SketchSegment second,
            int sameLengthType)
        {
            if (manager == null ||
                first == null ||
                second == null)
            {
                return null;
            }

            string firstKey =
                GetSketchSegmentKey(
                    first);

            string secondKey =
                GetSketchSegmentKey(
                    second);

            object[] relations =
                null;

            try
            {
                relations =
                    manager.GetRelations(
                        (int)swSketchRelationFilterType_e.swAll)
                    as object[];
            }
            catch
            {
                return null;
            }

            if (relations == null)
                return null;

            foreach (object obj in relations)
            {
                SketchRelation relation =
                    obj as SketchRelation;

                if (relation == null)
                    continue;

                int relationType =
                    -1;

                try
                {
                    relationType =
                        relation.GetRelationType();
                }
                catch
                {
                }

                if (relationType !=
                    sameLengthType)
                {
                    continue;
                }

                object[] entities =
                    GetRelationEntities(
                        relation);

                if (entities == null)
                    continue;

                bool hasFirst =
                    false;

                bool hasSecond =
                    false;

                foreach (object entity in entities)
                {
                    SketchSegment segment =
                        entity as SketchSegment;

                    if (segment == null)
                        continue;

                    string key =
                        GetSketchSegmentKey(
                            segment);

                    if (string.Equals(
                            key,
                            firstKey,
                            StringComparison.Ordinal))
                    {
                        hasFirst =
                            true;
                    }

                    if (string.Equals(
                            key,
                            secondKey,
                            StringComparison.Ordinal))
                    {
                        hasSecond =
                            true;
                    }
                }

                if (hasFirst &&
                    hasSecond)
                {
                    return relation;
                }
            }

            return null;
        }

        private static int CountEqualConstrainedLines(
            ISketchRelationManager manager,
            List<SketchSegment> constructionLines,
            int sameLengthType)
        {
            if (manager == null ||
                constructionLines == null ||
                constructionLines.Count == 0)
            {
                return 0;
            }

            HashSet<string> targetKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (SketchSegment line in constructionLines)
            {
                if (line == null)
                    continue;

                targetKeys.Add(
                    GetSketchSegmentKey(
                        line));
            }

            HashSet<string> constrainedKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            object[] relations =
                null;

            try
            {
                relations =
                    manager.GetRelations(
                        (int)swSketchRelationFilterType_e.swAll)
                    as object[];
            }
            catch
            {
                return 0;
            }

            if (relations == null)
                return 0;

            foreach (object obj in relations)
            {
                SketchRelation relation =
                    obj as SketchRelation;

                if (relation == null)
                    continue;

                int relationType =
                    -1;

                try
                {
                    relationType =
                        relation.GetRelationType();
                }
                catch
                {
                }

                if (relationType !=
                    sameLengthType)
                {
                    continue;
                }

                object[] entities =
                    GetRelationEntities(
                        relation);

                if (entities == null)
                    continue;

                foreach (object entity in entities)
                {
                    SketchSegment segment =
                        entity as SketchSegment;

                    if (segment == null)
                        continue;

                    string key =
                        GetSketchSegmentKey(
                            segment);

                    if (targetKeys.Contains(
                            key))
                    {
                        constrainedKeys.Add(
                            key);
                    }
                }
            }

            return constrainedKeys.Count;
        }

        private static int DeleteEqualRelationsTouchingLines(
            Sketch sketch,
            List<SketchSegment> constructionLines)
        {
            if (sketch == null ||
                constructionLines == null ||
                constructionLines.Count == 0)
            {
                return 0;
            }

            ISketchRelationManager manager =
                null;

            try
            {
                manager =
                    sketch.RelationManager;
            }
            catch
            {
            }

            if (manager == null)
                return 0;

            int sameLengthType =
                (int)swConstraintType_e
                    .swConstraintType_SAMELENGTH;

            HashSet<string> lineKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (SketchSegment line in constructionLines)
            {
                if (line != null)
                {
                    lineKeys.Add(
                        GetSketchSegmentKey(
                            line));
                }
            }

            object[] relations =
                null;

            try
            {
                relations =
                    manager.GetRelations(
                        (int)swSketchRelationFilterType_e.swAll)
                    as object[];
            }
            catch
            {
                return 0;
            }

            if (relations == null)
                return 0;

            List<SketchRelation> toDelete =
                new List<SketchRelation>();

            foreach (object obj in relations)
            {
                SketchRelation relation =
                    obj as SketchRelation;

                if (relation == null)
                    continue;

                int relationType =
                    -1;

                try
                {
                    relationType =
                        relation.GetRelationType();
                }
                catch
                {
                }

                if (relationType !=
                    sameLengthType)
                {
                    continue;
                }

                object[] entities =
                    GetRelationEntities(
                        relation);

                bool touches =
                    false;

                if (entities != null)
                {
                    foreach (object entity in entities)
                    {
                        SketchSegment segment =
                            entity as SketchSegment;

                        if (segment != null &&
                            lineKeys.Contains(
                                GetSketchSegmentKey(
                                    segment)))
                        {
                            touches =
                                true;
                            break;
                        }
                    }
                }

                if (touches)
                {
                    toDelete.Add(
                        relation);
                }
            }

            int deleted =
                0;

            foreach (SketchRelation relation in toDelete)
            {
                try
                {
                    if (manager.DeleteRelation(
                            relation))
                    {
                        deleted++;
                    }
                }
                catch
                {
                }
            }

            return deleted;
        }

        private static bool TryApplyEqualLengthRelationsManagerOnly(
            Sketch sketch,
            List<SketchSegment> constructionLines,
            out int constrainedLineCount)
        {
            constrainedLineCount =
                0;

            if (sketch == null ||
                constructionLines == null ||
                constructionLines.Count < 2)
            {
                return false;
            }

            ISketchRelationManager manager =
                null;

            try
            {
                manager =
                    sketch.RelationManager;
            }
            catch
            {
            }

            if (manager == null)
                return false;

            int sameLengthType =
                (int)swConstraintType_e
                    .swConstraintType_SAMELENGTH;

            object[] allLines =
                new object[
                    constructionLines.Count];

            for (int i = 0;
                 i < constructionLines.Count;
                 i++)
            {
                allLines[i] =
                    constructionLines[i];
            }

            try
            {
                SketchRelation relation =
                    manager.AddRelation(
                        allLines,
                        sameLengthType);

                if (relation != null)
                {
                    int actualType =
                        -1;

                    try
                    {
                        actualType =
                            relation.GetRelationType();
                    }
                    catch
                    {
                    }

                    if (actualType ==
                        sameLengthType)
                    {
                        constrainedLineCount =
                            constructionLines.Count;

                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V35 EQUAL " +
                            "mode=ALL_LINES, verified=True");

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V35 EQUAL all-lines failed: " +
                    ex.Message);
            }

            // Pairwise fallback through RelationManager only.
            SketchSegment master =
                constructionLines[0];

            HashSet<int> constrained =
                new HashSet<int>();

            constrained.Add(
                0);

            for (int i = 1;
                 i < constructionLines.Count;
                 i++)
            {
                try
                {
                    SketchRelation relation =
                        manager.AddRelation(
                            new object[]
                            {
                                master,
                                constructionLines[i]
                            },
                            sameLengthType);

                    if (relation == null)
                        continue;

                    int actualType =
                        -1;

                    try
                    {
                        actualType =
                            relation.GetRelationType();
                    }
                    catch
                    {
                    }

                    if (actualType ==
                        sameLengthType)
                    {
                        constrained.Add(
                            i);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V35 EQUAL pair failed i=" +
                        i +
                        ": " +
                        ex.Message);
                }
            }

            constrainedLineCount =
                constrained.Count;

            bool verified =
                constrainedLineCount ==
                constructionLines.Count;

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V35 EQUAL " +
                "mode=PAIRWISE, constrained=" +
                constrainedLineCount +
                "/" +
                constructionLines.Count +
                ", verified=" +
                verified);

            return verified;
        }

        private void CleanupSameSplineMoveCandidate(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName,
            string expectedSplineKey,
            List<SketchSegment> helperLines)
        {
            if (model == null ||
                owningSketchFeature == null ||
                string.IsNullOrWhiteSpace(
                    expectedSplineKey))
            {
                return;
            }

            Sketch sketch =
                null;

            SketchSpline spline =
                null;

            try
            {
                sketch =
                    EnsureSame3DSketchEditing(
                        model,
                        ReacquireSame3DSketchForReadback(
                            model,
                            owningSketchFeature,
                            owningSketchName,
                            "V35 candidate cleanup"),
                        owningSketchFeature,
                        owningSketchName);

                SketchSegment segment =
                    ReacquireSketchSegmentByKey(
                        sketch,
                        expectedSplineKey);

                spline =
                    segment
                    as SketchSpline;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V35 CANDIDATE CLEANUP reacquire failed: " +
                    ex.Message);

                return;
            }

            if (sketch == null ||
                spline == null)
            {
                return;
            }

            List<SketchPoint> fitPoints =
                GetSplineFitPoints(
                    spline);

            List<SketchSegment> lines =
                helperLines ??
                new List<SketchSegment>();

            InPlaceRelationReleaseResult release =
                ReleaseAutoSplineRelationsForInPlaceRetarget(
                    sketch,
                    fitPoints,
                    lines,
                    spline);

            int deleted =
                DeleteAllDisposableHelperLines(
                    model,
                    sketch,
                    lines);

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V35 CANDIDATE CLEANUP " +
                "relationsDeleted=" +
                release.RelationsDeleted +
                ", helpersDeleted=" +
                deleted +
                "/" +
                lines.Count);
        }

        private static double GetEndpointVertexPairError(
            Edge edge,
            List<SketchPoint> fitPoints)
        {
            if (edge == null ||
                fitPoints == null ||
                fitPoints.Count < 2)
            {
                return double.MaxValue;
            }

            Vertex start =
                null;

            Vertex end =
                null;

            try
            {
                start =
                    edge.GetStartVertex()
                    as Vertex;

                end =
                    edge.GetEndVertex()
                    as Vertex;
            }
            catch
            {
            }

            if (start == null ||
                end == null)
            {
                return double.MaxValue;
            }

            SketchPoint first =
                fitPoints[0];

            SketchPoint last =
                fitPoints[
                    fitPoints.Count - 1];

            double direct =
                Math.Max(
                    DistanceSketchPointToVertex(
                        first,
                        start),
                    DistanceSketchPointToVertex(
                        last,
                        end));

            double reverse =
                Math.Max(
                    DistanceSketchPointToVertex(
                        first,
                        end),
                    DistanceSketchPointToVertex(
                        last,
                        start));

            return
                Math.Min(
                    direct,
                    reverse);
        }

        private InPlaceRelationReleaseResult ReleaseAutoSplineRelationsForInPlaceRetarget(
            Sketch sketch,
            List<SketchPoint> fitPoints,
            List<SketchSegment> helperLines,
            SketchSpline spline)
        {
            InPlaceRelationReleaseResult result =
                new InPlaceRelationReleaseResult();

            if (sketch == null)
                return result;

            ISketchRelationManager manager =
                null;

            try
            {
                manager =
                    sketch.RelationManager;
            }
            catch
            {
            }

            if (manager == null)
                return result;

            object[] relations =
                null;

            try
            {
                relations =
                    manager.GetRelations(
                        (int)swSketchRelationFilterType_e
                            .swAll)
                    as object[];
            }
            catch
            {
            }

            if (relations == null)
                return result;

            // ------------------------------------------------------------
            // V35.2 SAFE UNLOCK
            //
            // V35.1 tried to recursively walk a COM relation graph.  The real
            // runtime crashed with AccessViolation before the first unlock log.
            // Do NOT recursively traverse relation-definition COM objects.
            //
            // This implementation uses only the same GetRelationEntities route
            // that was stable in V35, plus ONE safe extra step:
            //
            //   selected spline network
            //          |
            //      relation
            //          |
            //   construction spline/segment
            //
            // If such construction geometry is discovered, only its FIXED
            // relation is additionally released.  No sketch entity is deleted.
            // ------------------------------------------------------------

            HashSet<string> fitPointKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            if (fitPoints != null)
            {
                foreach (SketchPoint point in
                         fitPoints)
                {
                    if (point == null)
                        continue;

                    string key =
                        GetSketchPointKey(
                            point);

                    if (!string.IsNullOrWhiteSpace(
                            key))
                    {
                        fitPointKeys.Add(
                            key);
                    }
                }
            }

            HashSet<string> helperLineKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            HashSet<string> helperEndpointKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            if (helperLines != null)
            {
                foreach (SketchSegment segment in
                         helperLines)
                {
                    if (segment == null)
                        continue;

                    string segmentKey =
                        GetSketchSegmentKey(
                            segment);

                    if (!string.IsNullOrWhiteSpace(
                            segmentKey))
                    {
                        helperLineKeys.Add(
                            segmentKey);
                    }

                    SketchLine line =
                        segment as SketchLine;

                    if (line == null)
                        continue;

                    try
                    {
                        SketchPoint a =
                            line.GetStartPoint2()
                            as SketchPoint;

                        SketchPoint b =
                            line.GetEndPoint2()
                            as SketchPoint;

                        if (a != null)
                        {
                            string key =
                                GetSketchPointKey(
                                    a);

                            if (!string.IsNullOrWhiteSpace(
                                    key))
                            {
                                helperEndpointKeys.Add(
                                    key);
                            }
                        }

                        if (b != null)
                        {
                            string key =
                                GetSketchPointKey(
                                    b);

                            if (!string.IsNullOrWhiteSpace(
                                    key))
                            {
                                helperEndpointKeys.Add(
                                    key);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            SketchSegment splineSegment =
                spline as SketchSegment;

            string splineKey =
                splineSegment == null
                    ? ""
                    : GetSketchSegmentKey(
                        splineSegment);

            HashSet<string> connectedConstructionKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            // PASS A: discover only construction geometry that participates in
            // a relation already touching the selected spline/helper network.
            foreach (object obj in
                     relations)
            {
                SketchRelation relation =
                    obj as SketchRelation;

                if (relation == null)
                    continue;

                object[] entities =
                    GetRelationEntities(
                        relation);

                if (entities == null)
                    continue;

                bool touchesMoveNetwork =
                    false;

                foreach (object entity in
                         entities)
                {
                    SketchPoint point =
                        entity as SketchPoint;

                    if (point != null)
                    {
                        string pointKey =
                            GetSketchPointKey(
                                point);

                        if (fitPointKeys.Contains(
                                pointKey) ||
                            helperEndpointKeys.Contains(
                                pointKey))
                        {
                            touchesMoveNetwork =
                                true;
                        }
                    }

                    SketchSegment segment =
                        entity as SketchSegment;

                    if (segment == null)
                        continue;

                    string segmentKey =
                        GetSketchSegmentKey(
                            segment);

                    if (helperLineKeys.Contains(
                            segmentKey) ||
                        (!string.IsNullOrWhiteSpace(
                             splineKey) &&
                         string.Equals(
                             splineKey,
                             segmentKey,
                             StringComparison.Ordinal)))
                    {
                        touchesMoveNetwork =
                            true;
                    }
                }

                if (!touchesMoveNetwork)
                    continue;

                foreach (object entity in
                         entities)
                {
                    SketchSegment segment =
                        entity as SketchSegment;

                    if (segment == null)
                        continue;

                    string segmentKey =
                        GetSketchSegmentKey(
                            segment);

                    if (string.IsNullOrWhiteSpace(
                            segmentKey) ||
                        string.Equals(
                            segmentKey,
                            splineKey,
                            StringComparison.Ordinal) ||
                        helperLineKeys.Contains(
                            segmentKey))
                    {
                        continue;
                    }

                    bool construction =
                        false;

                    try
                    {
                        construction =
                            segment.ConstructionGeometry;
                    }
                    catch
                    {
                    }

                    if (construction)
                    {
                        connectedConstructionKeys.Add(
                            segmentKey);
                    }
                }
            }

            // V37.0 additional safe geometry pass: a construction SketchSpline
            // may share a native fit point with the MOVE spline even when no
            // explicit relation object exposes that connection.  Detect only
            // construction SPLINES sharing an existing fit/helper endpoint ID.
            // Straight SketchLine center lines are deliberately not added here.
            object[] allSegmentsForSplineBlockers = null;
            try
            {
                allSegmentsForSplineBlockers = sketch.GetSketchSegments() as object[];
            }
            catch
            {
            }

            if (allSegmentsForSplineBlockers != null)
            {
                foreach (object raw in allSegmentsForSplineBlockers)
                {
                    SketchSpline constructionSpline = raw as SketchSpline;
                    SketchSegment segment = raw as SketchSegment;

                    if (constructionSpline == null || segment == null)
                        continue;

                    string segmentKey = GetSketchSegmentKey(segment);
                    if (string.IsNullOrWhiteSpace(segmentKey) ||
                        string.Equals(segmentKey, splineKey, StringComparison.Ordinal) ||
                        helperLineKeys.Contains(segmentKey))
                    {
                        continue;
                    }

                    bool construction = false;
                    try
                    {
                        construction = segment.ConstructionGeometry;
                    }
                    catch
                    {
                    }

                    if (!construction)
                        continue;

                    List<SketchPoint> blockerFitPoints =
                        GetSplineFitPoints(constructionSpline);

                    bool sharesMovePoint = false;
                    foreach (SketchPoint blockerPoint in blockerFitPoints)
                    {
                        string pointKey = GetSketchPointKey(blockerPoint);
                        if (fitPointKeys.Contains(pointKey) ||
                            helperEndpointKeys.Contains(pointKey))
                        {
                            sharesMovePoint = true;
                            break;
                        }
                    }

                    if (sharesMovePoint)
                    {
                        connectedConstructionKeys.Add(segmentKey);
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] MOVE V37.0 SHARED-POINT CENTER-SPLINE " +
                            "key=" + segmentKey + ", action=MARK_FOR_DELETE");
                    }
                }
            }

            // Collect point IDs from only those one-hop construction segments.
            // This lets us recognize a FIX relation even when SOLIDWORKS
            // exposes the fixed relation through one of its fit points.
            HashSet<string> connectedConstructionPointKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            if (connectedConstructionKeys.Count >
                0)
            {
                object[] sketchSegments =
                    null;

                try
                {
                    sketchSegments =
                        sketch.GetSketchSegments()
                        as object[];
                }
                catch
                {
                }

                if (sketchSegments != null)
                {
                    foreach (object rawSegment in
                             sketchSegments)
                    {
                        SketchSegment segment =
                            rawSegment as SketchSegment;

                        if (segment == null)
                            continue;

                        string segmentKey =
                            GetSketchSegmentKey(
                                segment);

                        if (!connectedConstructionKeys.Contains(
                                segmentKey))
                        {
                            continue;
                        }

                        SketchLine line =
                            segment as SketchLine;

                        if (line != null)
                        {
                            try
                            {
                                SketchPoint a =
                                    line.GetStartPoint2()
                                    as SketchPoint;

                                SketchPoint b =
                                    line.GetEndPoint2()
                                    as SketchPoint;

                                if (a != null)
                                {
                                    string key =
                                        GetSketchPointKey(
                                            a);

                                    if (!string.IsNullOrWhiteSpace(
                                            key))
                                    {
                                        connectedConstructionPointKeys.Add(
                                            key);
                                    }
                                }

                                if (b != null)
                                {
                                    string key =
                                        GetSketchPointKey(
                                            b);

                                    if (!string.IsNullOrWhiteSpace(
                                            key))
                                    {
                                        connectedConstructionPointKeys.Add(
                                            key);
                                    }
                                }
                            }
                            catch
                            {
                            }

                            continue;
                        }

                        SketchSpline constructionSpline =
                            segment as SketchSpline;

                        if (constructionSpline != null)
                        {
                            List<SketchPoint> constructionPoints =
                                GetSplineFitPoints(
                                    constructionSpline);

                            foreach (SketchPoint point in
                                     constructionPoints)
                            {
                                if (point == null)
                                    continue;

                                string key =
                                    GetSketchPointKey(
                                        point);

                                if (!string.IsNullOrWhiteSpace(
                                        key))
                                {
                                    connectedConstructionPointKeys.Add(
                                        key);
                                }
                            }
                        }
                    }
                }
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V35.2 SAFE UNLOCK DISCOVERY " +
                "fitPoints=" +
                fitPointKeys.Count +
                ", helperLines=" +
                helperLineKeys.Count +
                ", connectedConstruction=" +
                connectedConstructionKeys.Count +
                ", connectedConstructionPoints=" +
                connectedConstructionPointKeys.Count);

            int fixedConstructionDeleted =
                0;

            // PASS B: delete the original MOVE network exactly as V35 did.
            // Additionally delete FIXED only on the one-hop construction
            // geometry discovered above.
            foreach (object obj in
                     relations)
            {
                SketchRelation relation =
                    obj as SketchRelation;

                if (relation == null)
                    continue;

                object[] entities =
                    GetRelationEntities(
                        relation);

                bool touchesFitPoint =
                    false;

                bool touchesHelper =
                    false;

                bool touchesSpline =
                    false;

                bool touchesConnectedConstruction =
                    false;

                if (entities != null)
                {
                    foreach (object entity in
                             entities)
                    {
                        SketchPoint point =
                            entity as SketchPoint;

                        if (point != null)
                        {
                            string pointKey =
                                GetSketchPointKey(
                                    point);

                            if (fitPointKeys.Contains(
                                    pointKey))
                            {
                                touchesFitPoint =
                                    true;
                            }

                            if (helperEndpointKeys.Contains(
                                    pointKey))
                            {
                                touchesHelper =
                                    true;
                            }

                            if (connectedConstructionPointKeys.Contains(
                                    pointKey))
                            {
                                touchesConnectedConstruction =
                                    true;
                            }
                        }

                        SketchSegment segment =
                            entity as SketchSegment;

                        if (segment != null)
                        {
                            string segmentKey =
                                GetSketchSegmentKey(
                                    segment);

                            if (helperLineKeys.Contains(
                                    segmentKey))
                            {
                                touchesHelper =
                                    true;
                            }

                            if (!string.IsNullOrWhiteSpace(
                                    splineKey) &&
                                string.Equals(
                                    splineKey,
                                    segmentKey,
                                    StringComparison.Ordinal))
                            {
                                touchesSpline =
                                    true;
                            }

                            if (connectedConstructionKeys.Contains(
                                    segmentKey))
                            {
                                touchesConnectedConstruction =
                                    true;
                            }
                        }
                    }
                }

                int relationType =
                    -1;

                try
                {
                    relationType =
                        relation.GetRelationType();
                }
                catch
                {
                }

                bool equalRelation =
                    relationType ==
                    (int)swConstraintType_e
                        .swConstraintType_SAMELENGTH;

                bool fixedRelation =
                    relationType ==
                    (int)swConstraintType_e
                        .swConstraintType_FIXED;

                bool touchesOriginalMoveNetwork =
                    touchesFitPoint ||
                    touchesHelper ||
                    touchesSpline ||
                    (equalRelation &&
                     touchesHelper);

                bool deleteBlockingFixedConstruction =
                    fixedRelation &&
                    touchesConnectedConstruction;

                bool shouldDelete =
                    touchesOriginalMoveNetwork ||
                    deleteBlockingFixedConstruction;

                if (!shouldDelete)
                    continue;

                bool deleted =
                    false;

                try
                {
                    deleted =
                        manager.DeleteRelation(
                            relation);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "MOVE relation delete failed: " +
                        ex.Message);
                }

                if (!deleted)
                    continue;

                result.RelationsDeleted++;

                if (equalRelation)
                {
                    result.EqualRelationsDeleted++;
                }

                if (touchesFitPoint)
                {
                    result.FitPointRelationsDeleted++;
                }

                if (touchesSpline)
                {
                    result.SplineRelationsDeleted++;
                }

                if (touchesHelper)
                {
                    result.HelperRelationsDeleted++;
                }

                if (deleteBlockingFixedConstruction)
                {
                    fixedConstructionDeleted++;
                }
            }

            result.BlockingConstructionSegmentKeys.Clear();

            foreach (string blockerKey in
                     connectedConstructionKeys)
            {
                if (!string.IsNullOrWhiteSpace(
                        blockerKey))
                {
                    result.BlockingConstructionSegmentKeys.Add(
                        blockerKey);
                }
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V35.4 SAFE UNLOCK RESULT " +
                "relationsDeleted=" +
                result.RelationsDeleted +
                ", fixedConstructionDeleted=" +
                fixedConstructionDeleted +
                ", connectedConstruction=" +
                connectedConstructionKeys.Count +
                ", blockerKeys=" +
                result.BlockingConstructionSegmentKeys.Count);

            return result;
        }

        private static int CountSketchRelationsTouchingSpline(
            Sketch sketch,
            SketchSpline spline)
        {
            if (sketch == null ||
                spline == null)
            {
                return 0;
            }

            ISketchRelationManager manager =
                null;

            try
            {
                manager =
                    sketch.RelationManager;
            }
            catch
            {
                return 0;
            }

            if (manager == null)
                return 0;

            object[] relations =
                null;

            try
            {
                relations =
                    manager.GetRelations(
                        (int)swSketchRelationFilterType_e
                            .swAll)
                    as object[];
            }
            catch
            {
                return 0;
            }

            if (relations == null)
                return 0;

            SketchSegment splineSegment =
                spline as SketchSegment;

            string splineKey =
                splineSegment == null
                    ? ""
                    : GetSketchSegmentKey(
                        splineSegment);

            int count =
                0;

            foreach (object obj in
                     relations)
            {
                SketchRelation relation =
                    obj as SketchRelation;

                if (relation == null)
                    continue;

                object[] entities =
                    GetRelationEntities(
                        relation);

                if (entities == null)
                    continue;

                bool touches =
                    false;

                foreach (object entity in
                         entities)
                {
                    SketchSpline entitySpline =
                        entity as SketchSpline;

                    if (entitySpline != null &&
                        IsSameComObject(
                            entitySpline,
                            spline))
                    {
                        touches =
                            true;

                        break;
                    }

                    SketchSegment segment =
                        entity as SketchSegment;

                    if (segment != null &&
                        !string.IsNullOrWhiteSpace(
                            splineKey))
                    {
                        string segmentKey =
                            GetSketchSegmentKey(
                                segment);

                        if (string.Equals(
                                splineKey,
                                segmentKey,
                                StringComparison.Ordinal))
                        {
                            touches =
                                true;

                            break;
                        }
                    }
                }

                if (touches)
                {
                    count++;
                }
            }

            return count;
        }


        private List<SketchPoint> AdjustSplinePointCountInPlaceForMove(
            SketchSpline spline,
            int targetCount,
            List<double[]> targetSnapshot,
            bool reverseTarget)
        {
            if (spline == null)
            {
                throw new InvalidOperationException(
                    "Spline null khi adaptive MOVE point-count.");
            }

            targetCount =
                Math.Max(
                    MinimumPointCount,
                    Math.Min(
                        MaximumPointCount,
                        targetCount));

            int guard =
                0;

            List<SketchPoint> points =
                OrderSplineFitPointsAlongTargetEdge(
                    GetSplineFitPoints(spline),
                    targetSnapshot,
                    reverseTarget);

            while (points.Count <
                   targetCount)
            {
                guard++;

                if (guard >
                    MaximumPointCount * 3)
                {
                    throw new InvalidOperationException(
                        "MOVE InsertPoint guard exceeded.");
                }

                bool inserted =
                    InsertOnePointOnSameSpline(
                        spline,
                        targetCount);

                if (!inserted)
                {
                    throw new InvalidOperationException(
                        "MOVE SketchSpline.InsertPoint thất bại ở " +
                        points.Count +
                        " → " +
                        (points.Count + 1) +
                        " point.");
                }

                List<SketchPoint> refreshed =
                    OrderSplineFitPointsAlongTargetEdge(
                        GetSplineFitPoints(spline),
                        targetSnapshot,
                        reverseTarget);

                if (refreshed.Count !=
                    points.Count + 1)
                {
                    throw new InvalidOperationException(
                        "MOVE InsertPoint không tăng point-count đúng 1.");
                }

                points =
                    refreshed;
            }

            while (points.Count >
                   targetCount)
            {
                guard++;

                if (guard >
                    MaximumPointCount * 6)
                {
                    throw new InvalidOperationException(
                        "MOVE DeletePoint guard exceeded.");
                }

                int deleteIndex =
                    FindLeastImportantInteriorPointIndex(
                        points);

                if (deleteIndex <= 0 ||
                    deleteIndex >=
                        points.Count - 1)
                {
                    throw new InvalidOperationException(
                        "MOVE không tìm được interior spline point để xóa.");
                }

                SketchPoint deletePoint =
                    points[deleteIndex];

                bool deleted =
                    false;

                try
                {
                    deleted =
                        spline.DeletePoint(
                            deletePoint);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE DeletePoint exception: " +
                        ex.Message);
                }

                if (!deleted)
                {
                    throw new InvalidOperationException(
                        "MOVE SketchSpline.DeletePoint thất bại tại ordered index " +
                        deleteIndex +
                        ".");
                }

                List<SketchPoint> refreshed =
                    OrderSplineFitPointsAlongTargetEdge(
                        GetSplineFitPoints(spline),
                        targetSnapshot,
                        reverseTarget);

                if (refreshed.Count !=
                    points.Count - 1)
                {
                    throw new InvalidOperationException(
                        "MOVE DeletePoint không giảm point-count đúng 1.");
                }

                points =
                    refreshed;
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V35.5 TOPOLOGY ORDERED " +
                "targetN=" + targetCount +
                ", actualN=" + points.Count +
                ", order=TARGET_EDGE_ARC");

            return points;
        }

        private List<SketchPoint> AdjustSplinePointCountInPlace(
            SketchSpline spline,
            int targetCount)
        {
            if (spline == null)
            {
                throw new InvalidOperationException(
                    "Spline null khi adaptive point-count.");
            }

            targetCount =
                Math.Max(
                    MinimumPointCount,
                    Math.Min(
                        MaximumPointCount,
                        targetCount));

            int guard =
                0;

            List<SketchPoint> points =
                GetSplineFitPoints(
                    spline);

            while (points.Count <
                   targetCount)
            {
                guard++;

                if (guard >
                    MaximumPointCount * 3)
                {
                    throw new InvalidOperationException(
                        "InsertPoint guard exceeded.");
                }

                bool inserted =
                    InsertOnePointOnSameSpline(
                        spline,
                        targetCount);

                if (!inserted)
                {
                    throw new InvalidOperationException(
                        "SketchSpline.InsertPoint thất bại ở " +
                        points.Count +
                        " → " +
                        (points.Count + 1) +
                        " point.");
                }

                List<SketchPoint> refreshed =
                    GetSplineFitPoints(
                        spline);

                if (refreshed.Count !=
                    points.Count + 1)
                {
                    throw new InvalidOperationException(
                        "InsertPoint không tăng point-count đúng 1.");
                }

                points =
                    refreshed;
            }

            while (points.Count >
                   targetCount)
            {
                guard++;

                if (guard >
                    MaximumPointCount * 6)
                {
                    throw new InvalidOperationException(
                        "DeletePoint guard exceeded.");
                }

                int deleteIndex =
                    FindLeastImportantInteriorPointIndex(
                        points);

                if (deleteIndex <= 0 ||
                    deleteIndex >=
                        points.Count - 1)
                {
                    throw new InvalidOperationException(
                        "Không tìm được interior spline point để xóa.");
                }

                SketchPoint deletePoint =
                    points[deleteIndex];

                bool deleted =
                    false;

                try
                {
                    deleted =
                        spline.DeletePoint(
                            deletePoint);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "DeletePoint exception: " +
                        ex.Message);
                }

                if (!deleted)
                {
                    throw new InvalidOperationException(
                        "SketchSpline.DeletePoint thất bại tại index " +
                        deleteIndex +
                        ".");
                }

                List<SketchPoint> refreshed =
                    GetSplineFitPoints(
                        spline);

                if (refreshed.Count !=
                    points.Count - 1)
                {
                    throw new InvalidOperationException(
                        "DeletePoint không giảm point-count đúng 1.");
                }

                points =
                    refreshed;
            }

            return points;
        }

        private bool InsertOnePointOnSameSpline(
            SketchSpline spline,
            int targetCount)
        {
            SketchSegment segment =
                spline as SketchSegment;

            if (segment == null)
                return false;

            Curve curve =
                null;

            try
            {
                curve =
                    segment.GetCurve()
                    as Curve;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "Spline.GetCurve failed: " +
                    ex.Message);
            }

            if (curve == null)
                return false;

            double u0 =
                0.0;

            double u1 =
                0.0;

            bool isClosed =
                false;

            bool isPeriodic =
                false;

            bool gotEndParams =
                false;

            try
            {
                gotEndParams =
                    curve.GetEndParams(
                        out u0,
                        out u1,
                        out isClosed,
                        out isPeriodic);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "Spline curve GetEndParams failed: " +
                    ex.Message);
            }

            if (!gotEndParams)
                return false;

            if (u1 <
                u0)
            {
                double swap =
                    u0;

                u0 =
                    u1;

                u1 =
                    swap;
            }

            double totalLength =
                GetCurveLength(
                    curve,
                    u0,
                    u1);

            if (!IsFinite(
                    totalLength) ||
                totalLength <=
                    1.0e-12)
            {
                return false;
            }

            List<SketchPoint> existing =
                GetSplineFitPoints(
                    spline);

            int candidateDivisions =
                Math.Max(
                    64,
                    targetCount * 6);

            double bestDistance =
                -1.0;

            double[] bestPoint =
                null;

            for (int i = 1;
                 i < candidateDivisions;
                 i++)
            {
                double targetLength =
                    totalLength *
                    i /
                    candidateDivisions;

                double parameter =
                    FindParameterAtArcLength(
                        curve,
                        u0,
                        u1,
                        totalLength,
                        targetLength);

                double[] candidate =
                    EvaluateCurvePoint(
                        curve,
                        parameter);

                if (!IsPoint(
                        candidate))
                {
                    continue;
                }

                double minDistance =
                    double.MaxValue;

                foreach (SketchPoint point in
                         existing)
                {
                    double distance =
                        DistancePointToSketchPoint(
                            candidate,
                            point);

                    if (distance <
                        minDistance)
                    {
                        minDistance =
                            distance;
                    }
                }

                if (minDistance >
                    bestDistance)
                {
                    bestDistance =
                        minDistance;

                    bestPoint =
                        candidate;
                }
            }

            if (!IsPoint(
                    bestPoint))
            {
                return false;
            }

            try
            {
                return
                    spline.InsertPoint(
                        bestPoint[0],
                        bestPoint[1],
                        bestPoint[2]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "InsertPoint exception: " +
                    ex.Message);

                return false;
            }
        }

        private static double[] EvaluateCurvePoint(Curve curve, double parameter)
        {
            if (curve == null) throw new InvalidOperationException("Spline Curve null.");
            double[] values = GeometryCall("ICurve.Evaluate2", () => curve.Evaluate2(parameter, 0) as double[]);
            if (!IsPoint(values)) throw new InvalidOperationException("ICurve.Evaluate2: invalid XYZ.");
            return new[] { values[0], values[1], values[2] };
        }

        private static double DistancePointToSketchPoint(
            double[] point,
            SketchPoint sketchPoint)
        {
            if (!IsPoint(
                    point) ||
                sketchPoint == null)
            {
                return double.MaxValue;
            }

            double dx =
                point[0] -
                sketchPoint.X;

            double dy =
                point[1] -
                sketchPoint.Y;

            double dz =
                point[2] -
                sketchPoint.Z;

            return
                Math.Sqrt(
                    dx * dx +
                    dy * dy +
                    dz * dz);
        }

        private static int FindMinimumRedistributionDeleteIndex(
            int currentCount)
        {
            if (currentCount <= MinimumPointCount)
                return -1;

            int nextCount =
                currentCount - 1;

            int bestIndex =
                -1;

            double bestCost =
                double.MaxValue;

            // Existing points are already ordered along the target Edge.
            // Pick the deleted identity that minimizes total normalized arc
            // travel when the surviving identities are re-mapped from the
            // N-point equal-arc grid to the (N-1)-point equal-arc grid.
            for (int deleteIndex = 1;
                 deleteIndex < currentCount - 1;
                 deleteIndex++)
            {
                double cost = 0.0;
                int newIndex = 0;

                for (int oldIndex = 0;
                     oldIndex < currentCount;
                     oldIndex++)
                {
                    if (oldIndex == deleteIndex)
                        continue;

                    double oldT =
                        currentCount > 1
                            ? (double)oldIndex / (currentCount - 1)
                            : 0.0;

                    double newT =
                        nextCount > 1
                            ? (double)newIndex / (nextCount - 1)
                            : 0.0;

                    double delta =
                        oldT - newT;

                    cost +=
                        delta * delta;

                    newIndex++;
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestIndex = deleteIndex;
                }
            }

            return bestIndex;
        }

        private static bool TryUndoSameSplineBackToPointCount(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName,
            string splineKey,
            SketchSpline selectedSpline,
            int desiredPointCount,
            int maximumUndoSteps,
            out string error)
        {
            error = null;

            if (model == null ||
                owningSketchFeature == null ||
                string.IsNullOrWhiteSpace(splineKey) ||
                selectedSpline == null ||
                desiredPointCount < MinimumPointCount)
            {
                error = "invalid undo arguments";
                return false;
            }

            maximumUndoSteps =
                Math.Max(1, maximumUndoSteps);

            int previousCount =
                -1;

            int unchangedCountSteps =
                0;

            for (int step = 1;
                 step <= maximumUndoSteps;
                 step++)
            {
                try
                {
                    model.EditUndo2(1);
                }
                catch (Exception ex)
                {
                    error =
                        "EditUndo2 exception step=" + step + ": " + ex.Message;
                    return false;
                }

                Sketch sketch =
                    ReacquireSame3DSketchForReadback(
                        model,
                        owningSketchFeature,
                        owningSketchName,
                        "V36.5 native undo step " + step);

                if (sketch == null)
                {
                    continue;
                }

                SketchSegment segment =
                    ReacquireSketchSegmentByKey(
                        sketch,
                        splineKey);

                SketchSpline spline =
                    segment as SketchSpline;

                if (spline == null)
                {
                    continue;
                }

                bool sameKey =
                    string.Equals(
                        GetSketchSegmentKey(segment),
                        splineKey,
                        StringComparison.Ordinal);

                bool sameCom =
                    IsSameComObject(
                        spline,
                        selectedSpline);

                int count =
                    SafeGetSplinePointCount(
                        spline);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V36.5 NATIVE-UNDO STEP " +
                    "step=" + step +
                    ", count=" + count +
                    ", desired=" + desiredPointCount +
                    ", sameKey=" + sameKey +
                    ", sameCom=" + sameCom);

                if (sameKey &&
                    sameCom &&
                    count == desiredPointCount)
                {
                    return true;
                }

                if (count == previousCount)
                {
                    unchangedCountSteps++;
                }
                else
                {
                    previousCount = count;
                    unchangedCountSteps = 0;
                }

                if (unchangedCountSteps >= 5)
                {
                    error =
                        "Undo made no point-count progress for 6 consecutive steps; " +
                        "current=" + count +
                        ", desired=" + desiredPointCount;
                    return false;
                }
            }

            error =
                "Undo exhausted before SAME spline returned to pointCount=" +
                desiredPointCount;
            return false;
        }

        private static int FindLeastImportantInteriorPointIndex(
            List<SketchPoint> points)
        {
            if (points == null ||
                points.Count <=
                    MinimumPointCount)
            {
                return -1;
            }

            int bestIndex =
                -1;

            double bestScore =
                double.MaxValue;

            for (int i = 1;
                 i < points.Count - 1;
                 i++)
            {
                double[] p =
                    new double[]
                    {
                        points[i].X,
                        points[i].Y,
                        points[i].Z
                    };

                double[] a =
                    new double[]
                    {
                        points[i - 1].X,
                        points[i - 1].Y,
                        points[i - 1].Z
                    };

                double[] b =
                    new double[]
                    {
                        points[i + 1].X,
                        points[i + 1].Y,
                        points[i + 1].Z
                    };

                double score =
                    DistancePointToSegment3D(
                        p,
                        a,
                        b);

                if (score <
                    bestScore)
                {
                    bestScore =
                        score;

                    bestIndex =
                        i;
                }
            }

            return bestIndex;
        }

        private static List<double[]> ChooseTargetOrientationForMinimumTravel(
            List<SketchPoint> fitPoints,
            List<double[]> targets)
        {
            List<double[]> result =
                new List<double[]>();

            if (fitPoints == null ||
                targets == null ||
                fitPoints.Count !=
                    targets.Count)
            {
                return result;
            }

            double forward =
                0.0;

            double reverse =
                0.0;

            for (int i = 0;
                 i < fitPoints.Count;
                 i++)
            {
                SketchPoint point =
                    fitPoints[i];

                forward +=
                    DistanceSketchPointToArray(
                        point,
                        targets[i]);

                reverse +=
                    DistanceSketchPointToArray(
                        point,
                        targets[
                            targets.Count -
                            1 -
                            i]);
            }

            bool useReverse =
                reverse <
                forward;

            for (int i = 0;
                 i < targets.Count;
                 i++)
            {
                double[] source =
                    useReverse
                        ? targets[
                            targets.Count -
                            1 -
                            i]
                        : targets[i];

                result.Add(
                    new double[]
                    {
                        source[0],
                        source[1],
                        source[2]
                    });
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE ORIENTATION " +
                "forwardTravelMm=" +
                (forward * 1000.0)
                    .ToString(
                        "0.######",
                        CultureInfo.InvariantCulture) +
                ", reverseTravelMm=" +
                (reverse * 1000.0)
                    .ToString(
                        "0.######",
                        CultureInfo.InvariantCulture) +
                ", reversed=" +
                useReverse);

            return result;
        }

        private static double DistanceSketchPointToArray(
            SketchPoint point,
            double[] target)
        {
            if (point == null ||
                !IsPoint(
                    target))
            {
                return double.MaxValue;
            }

            double dx =
                point.X -
                target[0];

            double dy =
                point.Y -
                target[1];

            double dz =
                point.Z -
                target[2];

            return
                Math.Sqrt(
                    dx * dx +
                    dy * dy +
                    dz * dz);
        }

        private static InPlacePointMoveResult MoveSplineFitPointsInPlace(
            ModelDoc2 model,
            List<SketchPoint> fitPoints,
            List<double[]> targets)
        {
            InPlacePointMoveResult result =
                new InPlacePointMoveResult();

            if (model == null ||
                fitPoints == null ||
                targets == null ||
                fitPoints.Count !=
                    targets.Count)
            {
                return result;
            }

            ModelDocExtension extension =
                model.Extension
                as ModelDocExtension;

            if (extension == null)
                return result;

            // Endpoints first, then interior points.
            List<int> order =
                new List<int>();

            if (fitPoints.Count > 0)
                order.Add(0);

            if (fitPoints.Count > 1)
                order.Add(
                    fitPoints.Count - 1);

            for (int i = 1;
                 i < fitPoints.Count - 1;
                 i++)
            {
                order.Add(i);
            }

            double allowedError =
                5.0e-6; // 0.005 mm

            foreach (int index in
                     order)
            {
                SketchPoint point =
                    fitPoints[index];

                double[] target =
                    targets[index];

                if (point == null ||
                    !IsPoint(
                        target))
                {
                    return result;
                }

                double baseX =
                    point.X;

                double baseY =
                    point.Y;

                double baseZ =
                    point.Z;

                model.ClearSelection2(true);

                bool selected =
                    false;

                try
                {
                    selected =
                        point.Select4(
                            false,
                            null);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "Move fit point select failed i=" +
                        index +
                        ": " +
                        ex.Message);
                }

                if (!selected)
                    return result;

                try
                {
                    extension.MoveOrCopy(
                        false,
                        0,
                        true,
                        baseX,
                        baseY,
                        baseZ,
                        target[0],
                        target[1],
                        target[2]);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "MoveOrCopy fit point failed i=" +
                        index +
                        ": " +
                        ex.Message);

                    return result;
                }

                double error =
                    DistanceSketchPointToArray(
                        point,
                        target);

                result.MaximumError =
                    Math.Max(
                        result.MaximumError,
                        error);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE POINT[" +
                    index +
                    "] errorMm=" +
                    (error * 1000.0)
                        .ToString(
                            "0.######",
                            CultureInfo.InvariantCulture));

                if (!IsFinite(
                        error) ||
                    error >
                        allowedError)
                {
                    // Do not use SketchPoint.SetCoords as a fallback here.
                    // Runtime V20.1 showed SetCoords returned true while the
                    // spline fit point still remained ~38 mm from target.
                    // That return value is therefore not a reliable movement
                    // confirmation for this 3D spline case.
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE POINT[" +
                        index +
                        "] FAILED AFTER SOLVER REFRESH " +
                        "errorMm=" +
                        (error * 1000.0)
                            .ToString(
                                "0.######",
                                CultureInfo.InvariantCulture));

                    return result;
                }

                result.MovedCount++;
            }

            model.ClearSelection2(true);

            result.Success =
                result.MovedCount ==
                fitPoints.Count;

            return result;
        }

        private static HelperChainAdaptResult AdaptConstructionChainInPlace(
            ModelDoc2 model,
            Sketch activeSketch,
            List<SketchSegment> existingLines,
            List<SketchPoint> fitPoints)
        {
            HelperChainAdaptResult result =
                new HelperChainAdaptResult();

            if (model == null ||
                activeSketch == null ||
                fitPoints == null ||
                fitPoints.Count < 2)
            {
                return result;
            }

            int required =
                fitPoints.Count - 1;

            List<string> candidateKeys =
                new List<string>();

            if (existingLines != null)
            {
                foreach (SketchSegment line in
                         existingLines)
                {
                    if (line == null)
                        continue;

                    string key =
                        GetSketchSegmentKey(
                            line);

                    if (!string.IsNullOrWhiteSpace(
                            key))
                    {
                        candidateKeys.Add(
                            key);
                    }
                }
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] HELPER ADAPT BEGIN " +
                "candidates=" +
                candidateKeys.Count +
                ", required=" +
                required);

            List<SketchSegment> assignedLines =
                new List<SketchSegment>();

            HashSet<string> usedKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            HashSet<string> rejectedKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            // ------------------------------------------------------------
            // Map OLD helper entities to NEW spline intervals dynamically.
            //
            // We do NOT assume old line[i] still belongs to new point[i].
            // Point-count adaptation may remove/insert an interior spline
            // point, so the old helper topology can shift.
            //
            // Every target interval tries unused old helper lines until one
            // can be manually dragged into place.  A stubborn line is simply
            // rejected and another old helper line is tried.
            // ------------------------------------------------------------
            for (int interval = 0;
                 interval < required;
                 interval++)
            {
                SketchPoint targetA =
                    fitPoints[interval];

                SketchPoint targetB =
                    fitPoints[interval + 1];

                SketchSegment assigned =
                    null;

                string assignedKey =
                    "";

                foreach (string key in
                         candidateKeys)
                {
                    if (usedKeys.Contains(
                            key) ||
                        rejectedKeys.Contains(
                            key))
                    {
                        continue;
                    }

                    SketchSegment candidate =
                        ReacquireSketchSegmentByKey(
                            activeSketch,
                            key);

                    if (candidate == null)
                    {
                        rejectedKeys.Add(
                            key);

                        continue;
                    }

                    bool retargeted =
                        TryRetargetHelperLineByManualDrag(
                            model,
                            activeSketch,
                            candidate,
                            targetA,
                            targetB,
                            interval);

                    if (!retargeted)
                    {
                        rejectedKeys.Add(
                            key);

                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] HELPER CANDIDATE REJECT " +
                            "interval=" +
                            interval +
                            ", key=" +
                            key);

                        continue;
                    }

                    assigned =
                        ReacquireSketchSegmentByKey(
                            activeSketch,
                            key);

                    if (assigned == null)
                    {
                        rejectedKeys.Add(
                            key);

                        continue;
                    }

                    assignedKey =
                        key;

                    break;
                }

                // If all reusable old helpers are exhausted, create ONLY
                // the missing helper line.  The loft-driving spline is still
                // the original entity and is never recreated.
                if (assigned == null)
                {
                    SketchSegment created =
                        null;

                    try
                    {
                        created =
                            model.SketchManager.CreateLine(
                                targetA.X,
                                targetA.Y,
                                targetA.Z,
                                targetB.X,
                                targetB.Y,
                                targetB.Z)
                            as SketchSegment;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] HELPER CREATE failed " +
                            "interval=" +
                            interval +
                            ", ex=" +
                            ex.Message);
                    }

                    if (created == null)
                    {
                        result.Lines =
                            assignedLines;

                        return result;
                    }

                    try
                    {
                        created.ConstructionGeometry =
                            true;
                    }
                    catch
                    {
                    }

                    assigned =
                        created;

                    assignedKey =
                        GetSketchSegmentKey(
                            created);

                    result.CreatedCount++;

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] HELPER CREATED FALLBACK " +
                        "interval=" +
                        interval +
                        ", key=" +
                        assignedKey);
                }
                else
                {
                    result.ReusedCount++;
                }

                if (!string.IsNullOrWhiteSpace(
                        assignedKey))
                {
                    usedKeys.Add(
                        assignedKey);
                }

                // Reacquire line and endpoints one more time before relations.
                SketchSegment freshAssigned =
                    string.IsNullOrWhiteSpace(
                        assignedKey)
                        ? assigned
                        : ReacquireSketchSegmentByKey(
                            activeSketch,
                            assignedKey);

                if (freshAssigned == null)
                {
                    result.Lines =
                        assignedLines;

                    return result;
                }

                SketchLine sketchLine =
                    freshAssigned
                    as SketchLine;

                if (sketchLine == null)
                {
                    result.Lines =
                        assignedLines;

                    return result;
                }

                SketchPoint startPoint =
                    null;

                SketchPoint endPoint =
                    null;

                try
                {
                    startPoint =
                        sketchLine.GetStartPoint2()
                        as SketchPoint;

                    endPoint =
                        sketchLine.GetEndPoint2()
                        as SketchPoint;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] HELPER endpoint reacquire failed " +
                        "interval=" +
                        interval +
                        ", ex=" +
                        ex.Message);
                }

                if (startPoint == null ||
                    endPoint == null)
                {
                    result.Lines =
                        assignedLines;

                    return result;
                }

                // Determine which endpoint now corresponds to target A/B.
                // TryRetargetHelperLineByManualDrag can use either orientation.
                double direct =
                    DistanceBetweenSketchPoints(
                        startPoint,
                        targetA) +
                    DistanceBetweenSketchPoints(
                        endPoint,
                        targetB);

                double reverse =
                    DistanceBetweenSketchPoints(
                        startPoint,
                        targetB) +
                    DistanceBetweenSketchPoints(
                        endPoint,
                        targetA);

                SketchPoint endpointForA =
                    direct <= reverse
                        ? startPoint
                        : endPoint;

                SketchPoint endpointForB =
                    direct <= reverse
                        ? endPoint
                        : startPoint;

                bool coincidentA =
                    AddCoincident(
                        model,
                        endpointForA,
                        targetA);

                bool coincidentB =
                    AddCoincident(
                        model,
                        endpointForB,
                        targetB);

                if (!coincidentA ||
                    !coincidentB)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] HELPER COINCIDENT failed " +
                        "interval=" +
                        interval +
                        ", A=" +
                        coincidentA +
                        ", B=" +
                        coincidentB);

                    result.Lines =
                        assignedLines;

                    return result;
                }

                assignedLines.Add(
                    freshAssigned);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] HELPER ASSIGNED " +
                    "interval=" +
                    interval +
                    ", key=" +
                    assignedKey);
            }

            // ------------------------------------------------------------
            // Delete every OLD helper not used in the final N-1 chain.
            // This includes rejected/stubborn candidates and surplus lines.
            // Cleanup failure is non-fatal; such a line remains an orphan and
            // is excluded from Equal.
            // ------------------------------------------------------------
            foreach (string key in
                     candidateKeys)
            {
                if (usedKeys.Contains(
                        key))
                {
                    continue;
                }

                SketchSegment extra =
                    ReacquireSketchSegmentByKey(
                        activeSketch,
                        key);

                if (extra == null)
                {
                    result.DeletedCount++;

                    continue;
                }

                bool deleted =
                    TryDeleteSketchSegmentSafely(
                        model,
                        activeSketch,
                        extra,
                        key);

                if (deleted)
                {
                    result.DeletedCount++;
                }
                else
                {
                    result.DeleteFailedCount++;

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] HELPER UNUSED KEEP ORPHAN " +
                        "key=" +
                        key);
                }
            }

            // ------------------------------------------------------------
            // Reacquire only the assigned chain after cleanup.
            // ------------------------------------------------------------
            List<SketchSegment> finalLines =
                new List<SketchSegment>();

            foreach (SketchSegment line in
                     assignedLines)
            {
                if (line == null)
                    continue;

                string key =
                    GetSketchSegmentKey(
                        line);

                SketchSegment fresh =
                    ReacquireSketchSegmentByKey(
                        activeSketch,
                        key);

                if (fresh != null)
                {
                    finalLines.Add(
                        fresh);
                }
            }

            result.Lines =
                finalLines;

            result.Success =
                finalLines.Count ==
                required;

            model.ClearSelection2(true);

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] HELPER ADAPT END " +
                "success=" +
                result.Success +
                ", assigned=" +
                finalLines.Count +
                ", required=" +
                required +
                ", reused=" +
                result.ReusedCount +
                ", created=" +
                result.CreatedCount +
                ", deleted=" +
                result.DeletedCount +
                ", deleteFailed=" +
                result.DeleteFailedCount +
                ", rejected=" +
                rejectedKeys.Count);

            return result;
        }

        private static bool TryRetargetHelperLineByManualDrag(
            ModelDoc2 model,
            Sketch activeSketch,
            SketchSegment segment,
            SketchPoint targetA,
            SketchPoint targetB,
            int interval)
        {
            if (model == null ||
                activeSketch == null ||
                segment == null ||
                targetA == null ||
                targetB == null)
            {
                return false;
            }

            string key =
                GetSketchSegmentKey(
                    segment);

            SketchSegment fresh =
                ReacquireSketchSegmentByKey(
                    activeSketch,
                    key);

            SketchLine line =
                fresh as SketchLine;

            if (line == null)
                return false;

            SketchPoint start =
                null;

            SketchPoint end =
                null;

            try
            {
                start =
                    line.GetStartPoint2()
                    as SketchPoint;

                end =
                    line.GetEndPoint2()
                    as SketchPoint;
            }
            catch
            {
            }

            if (start == null ||
                end == null)
            {
                return false;
            }

            double directTravel =
                DistanceBetweenSketchPoints(
                    start,
                    targetA) +
                DistanceBetweenSketchPoints(
                    end,
                    targetB);

            double reverseTravel =
                DistanceBetweenSketchPoints(
                    start,
                    targetB) +
                DistanceBetweenSketchPoints(
                    end,
                    targetA);

            bool reverse =
                reverseTravel <
                directTravel;

            SketchPoint firstPoint =
                reverse
                    ? end
                    : start;

            SketchPoint firstTarget =
                targetA;

            SketchPoint secondTarget =
                targetB;

            bool firstMoved =
                MoveSketchPointByManualDrag(
                    model,
                    firstPoint,
                    firstTarget);

            if (!firstMoved)
            {
                return false;
            }

            // Moving one endpoint can regenerate the SketchLine RCW/endpoints.
            // Reacquire the SAME line before moving the second endpoint.
            fresh =
                ReacquireSketchSegmentByKey(
                    activeSketch,
                    key);

            line =
                fresh as SketchLine;

            if (line == null)
                return false;

            try
            {
                start =
                    line.GetStartPoint2()
                    as SketchPoint;

                end =
                    line.GetEndPoint2()
                    as SketchPoint;
            }
            catch
            {
                return false;
            }

            if (start == null ||
                end == null)
            {
                return false;
            }

            // After the first move, select the endpoint farther from targetA
            // as the second endpoint.  This avoids relying on stale start/end
            // orientation after SOLIDWORKS regenerates the line.
            double startToA =
                DistanceBetweenSketchPoints(
                    start,
                    targetA);

            double endToA =
                DistanceBetweenSketchPoints(
                    end,
                    targetA);

            SketchPoint secondPoint =
                startToA >
                endToA
                    ? start
                    : end;

            bool secondMoved =
                MoveSketchPointByManualDrag(
                    model,
                    secondPoint,
                    secondTarget);

            if (!secondMoved)
            {
                return false;
            }

            // Final geometry verification, independent of COM return values.
            fresh =
                ReacquireSketchSegmentByKey(
                    activeSketch,
                    key);

            line =
                fresh as SketchLine;

            if (line == null)
                return false;

            try
            {
                start =
                    line.GetStartPoint2()
                    as SketchPoint;

                end =
                    line.GetEndPoint2()
                    as SketchPoint;
            }
            catch
            {
                return false;
            }

            if (start == null ||
                end == null)
            {
                return false;
            }

            double finalDirect =
                Math.Max(
                    DistanceBetweenSketchPoints(
                        start,
                        targetA),
                    DistanceBetweenSketchPoints(
                        end,
                        targetB));

            double finalReverse =
                Math.Max(
                    DistanceBetweenSketchPoints(
                        start,
                        targetB),
                    DistanceBetweenSketchPoints(
                        end,
                        targetA));

            double finalError =
                Math.Min(
                    finalDirect,
                    finalReverse);

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] HELPER DRAG " +
                "interval=" +
                interval +
                ", key=" +
                key +
                ", reversed=" +
                reverse +
                ", errorMm=" +
                (finalError * 1000.0)
                    .ToString(
                        "0.######",
                        CultureInfo.InvariantCulture));

            return
                IsFinite(
                    finalError) &&
                finalError <=
                    5.0e-6;
        }

        private static bool MoveSketchPointByManualDrag(
            ModelDoc2 model,
            SketchPoint point,
            SketchPoint target)
        {
            if (model == null ||
                point == null ||
                target == null)
            {
                return false;
            }

            const double allowedError =
                5.0e-6;

            double beforeError =
                DistanceBetweenSketchPoints(
                    point,
                    target);

            if (IsFinite(
                    beforeError) &&
                beforeError <=
                    allowedError)
            {
                return true;
            }

            ModelDocExtension extension =
                model.Extension
                as ModelDocExtension;

            if (extension == null)
                return false;

            double baseX =
                point.X;

            double baseY =
                point.Y;

            double baseZ =
                point.Z;

            model.ClearSelection2(true);

            bool selected =
                false;

            try
            {
                selected =
                    point.Select4(
                        false,
                        null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] HELPER DRAG select exception: " +
                    ex.Message);
            }

            if (!selected)
            {
                return false;
            }

            try
            {
                extension.MoveOrCopy(
                    false,
                    0,
                    true,
                    baseX,
                    baseY,
                    baseZ,
                    target.X,
                    target.Y,
                    target.Z);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] HELPER DRAG MoveOrCopy exception: " +
                    ex.Message);
            }

            model.ClearSelection2(true);

            double finalError =
                DistanceBetweenSketchPoints(
                    point,
                    target);

            return
                IsFinite(
                    finalError) &&
                finalError <=
                    allowedError;
        }

        private static double DistanceBetweenSketchPoints(
            SketchPoint a,
            SketchPoint b)
        {
            if (a == null ||
                b == null)
            {
                return double.MaxValue;
            }

            try
            {
                double dx =
                    a.X -
                    b.X;

                double dy =
                    a.Y -
                    b.Y;

                double dz =
                    a.Z -
                    b.Z;

                return
                    Math.Sqrt(
                        dx * dx +
                        dy * dy +
                        dz * dz);
            }
            catch
            {
                return double.MaxValue;
            }
        }

        private static List<string> CaptureSketchSegmentKeys(
            List<SketchSegment> segments)
        {
            List<string> result =
                new List<string>();

            if (segments == null)
                return result;

            foreach (SketchSegment segment in
                     segments)
            {
                if (segment == null)
                    continue;

                string key =
                    GetSketchSegmentKey(
                        segment);

                if (!string.IsNullOrWhiteSpace(
                        key))
                {
                    result.Add(
                        key);
                }
            }

            return result;
        }

        private static List<SketchSegment> ReacquireSketchSegmentsByKeys(
            Sketch sketch,
            List<string> keys)
        {
            List<SketchSegment> result =
                new List<SketchSegment>();

            if (sketch == null ||
                keys == null)
            {
                return result;
            }

            foreach (string key in keys)
            {
                SketchSegment segment =
                    ReacquireSketchSegmentByKey(
                        sketch,
                        key);

                if (segment != null)
                {
                    result.Add(
                        segment);
                }
            }

            return result;
        }

        private static SketchSegment ReacquireSketchSegmentByKey(
            Sketch sketch,
            string key)
        {
            if (sketch == null ||
                string.IsNullOrWhiteSpace(
                    key))
            {
                return null;
            }

            object[] segments =
                null;

            try
            {
                segments =
                    sketch.GetSketchSegments()
                    as object[];
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] REACQUIRE SEGMENTS failed: " +
                    ex.Message);
            }

            if (segments == null)
                return null;

            foreach (object obj in
                     segments)
            {
                SketchSegment segment =
                    obj as SketchSegment;

                if (segment == null)
                    continue;

                string candidateKey =
                    GetSketchSegmentKey(
                        segment);

                if (string.Equals(
                        candidateKey,
                        key,
                        StringComparison.Ordinal))
                {
                    return segment;
                }
            }

            return null;
        }

        private static bool TryDeleteSketchSegmentSafely(
            ModelDoc2 model,
            Sketch sketch,
            SketchSegment segment,
            string segmentKey)
        {
            if (model == null ||
                sketch == null ||
                segment == null)
            {
                return false;
            }

            // Always reacquire one more time immediately before selection.
            SketchSegment fresh =
                ReacquireSketchSegmentByKey(
                    sketch,
                    segmentKey);

            if (fresh == null)
                return true;

            model.ClearSelection2(true);

            bool selected =
                false;

            try
            {
                selected =
                    fresh.Select4(
                        false,
                        null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] HELPER DELETE select failed " +
                    "key=" +
                    segmentKey +
                    ", ex=" +
                    ex.Message);
            }

            if (!selected)
            {
                model.ClearSelection2(true);

                return false;
            }

            bool deleted =
                false;

            ModelDocExtension extension =
                model.Extension
                as ModelDocExtension;

            if (extension != null)
            {
                try
                {
                    // 0 = no child/absorbed feature flags.
                    // We are deleting only the selected SketchSegment.
                    deleted =
                        extension.DeleteSelection2(
                            0);

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] HELPER DELETE DeleteSelection2 " +
                        "key=" +
                        segmentKey +
                        ", returned=" +
                        deleted);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] HELPER DELETE DeleteSelection2 failed " +
                        "key=" +
                        segmentKey +
                        ", ex=" +
                        ex.Message);
                }
            }

            if (!deleted)
            {
                // Fallback for SOLIDWORKS versions where DeleteSelection2 does
                // not remove a selected sketch segment while the sketch is active.
                try
                {
                    model.EditDelete();

                    deleted =
                        true;

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] HELPER DELETE EditDelete fallback " +
                        "key=" +
                        segmentKey);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] HELPER DELETE EditDelete failed " +
                        "key=" +
                        segmentKey +
                        ", ex=" +
                        ex.Message);
                }
            }

            model.ClearSelection2(true);

            // Verify by ID, not by the stale RCW.
            SketchSegment stillThere =
                ReacquireSketchSegmentByKey(
                    sketch,
                    segmentKey);

            bool verified =
                stillThere == null;

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] HELPER DELETE VERIFY " +
                "key=" +
                segmentKey +
                ", deletedCall=" +
                deleted +
                ", verifiedGone=" +
                verified);

            return verified;
        }

        private static bool MoveFreeSketchPointTo(
            SketchPoint point,
            SketchPoint target)
        {
            if (point == null ||
                target == null)
            {
                return false;
            }

            const double tolerance =
                5.0e-6;

            try
            {
                // A helper endpoint can already be the SAME topological point
                // as the spline fit point. In that case SOLIDWORKS can return
                // false from SetCoords because there is nothing to move or the
                // point is already controlled by shared sketch topology.
                double beforeDx =
                    point.X -
                    target.X;

                double beforeDy =
                    point.Y -
                    target.Y;

                double beforeDz =
                    point.Z -
                    target.Z;

                double beforeError =
                    Math.Sqrt(
                        beforeDx * beforeDx +
                        beforeDy * beforeDy +
                        beforeDz * beforeDz);

                if (beforeError <=
                    tolerance)
                {
                    return true;
                }

                bool moved =
                    point.SetCoords(
                        target.X,
                        target.Y,
                        target.Z);

                // Do not trust only the SetCoords return value.
                // Verify the actual final geometry instead.
                double dx =
                    point.X -
                    target.X;

                double dy =
                    point.Y -
                    target.Y;

                double dz =
                    point.Z -
                    target.Z;

                double error =
                    Math.Sqrt(
                        dx * dx +
                        dy * dy +
                        dz * dz);

                if (!moved)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] HELPER POINT SetCoords returned false " +
                        "but finalErrorMm=" +
                        (error * 1000.0)
                            .ToString(
                                "0.######",
                                CultureInfo.InvariantCulture));
                }

                return
                    error <=
                    tolerance;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] HELPER POINT move exception: " +
                    ex.Message);

                // One last geometry check. Some SOLIDWORKS COM calls throw
                // even though the shared endpoint has already followed the
                // spline point to the requested position.
                try
                {
                    double dx =
                        point.X -
                        target.X;

                    double dy =
                        point.Y -
                        target.Y;

                    double dz =
                        point.Z -
                        target.Z;

                    double error =
                        Math.Sqrt(
                            dx * dx +
                            dy * dy +
                            dz * dz);

                    return
                        error <=
                        tolerance;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static int AddPointOnEdgeRelations(
            ModelDoc2 model,
            Edge edge,
            List<SketchPoint> fitPoints)
        {
            if (model == null ||
                edge == null ||
                fitPoints == null)
            {
                return 0;
            }

            int count =
                0;

            foreach (SketchPoint point in
                     fitPoints)
            {
                model.ClearSelection2(true);

                if (!SelectComEntity(
                        point,
                        false) ||
                    !SelectComEntity(
                        edge,
                        true))
                {
                    continue;
                }

                try
                {
                    model.SketchAddConstraints(
                        "sgCOINCIDENT");

                    count++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "point-on-edge failed: " +
                        ex.Message);
                }
            }

            model.ClearSelection2(true);

            return count;
        }

        private static int AddEndpointRelations(
            ModelDoc2 model,
            Edge edge,
            List<SketchPoint> fitPoints)
        {
            if (model == null ||
                edge == null ||
                fitPoints == null ||
                fitPoints.Count < 2)
            {
                return 0;
            }

            Vertex startVertex =
                null;

            Vertex endVertex =
                null;

            try
            {
                startVertex =
                    edge.GetStartVertex()
                    as Vertex;

                endVertex =
                    edge.GetEndVertex()
                    as Vertex;
            }
            catch
            {
            }

            if (startVertex == null ||
                endVertex == null)
            {
                return 0;
            }

            SketchPoint first =
                fitPoints[0];

            SketchPoint last =
                fitPoints[
                    fitPoints.Count - 1];

            double firstToStart =
                DistanceSketchPointToVertex(
                    first,
                    startVertex);

            double firstToEnd =
                DistanceSketchPointToVertex(
                    first,
                    endVertex);

            int count =
                0;

            if (firstToStart <=
                firstToEnd)
            {
                if (AddCoincident(
                        model,
                        first,
                        startVertex))
                {
                    count++;
                }

                if (AddCoincident(
                        model,
                        last,
                        endVertex))
                {
                    count++;
                }
            }
            else
            {
                if (AddCoincident(
                        model,
                        first,
                        endVertex))
                {
                    count++;
                }

                if (AddCoincident(
                        model,
                        last,
                        startVertex))
                {
                    count++;
                }
            }

            return count;
        }

        private static double GetMaximumPointDistanceToEdge(
            Edge edge,
            List<SketchPoint> points)
        {
            if (edge == null ||
                points == null ||
                points.Count == 0)
            {
                return double.MaxValue;
            }

            double maximum =
                0.0;

            foreach (SketchPoint point in
                     points)
            {
                if (point == null)
                    return double.MaxValue;

                double[] closest =
                    null;

                try
                {
                    closest =
                        edge.GetClosestPointOn(
                            point.X,
                            point.Y,
                            point.Z)
                        as double[];
                }
                catch
                {
                }

                if (closest == null ||
                    closest.Length < 3)
                {
                    return double.MaxValue;
                }

                double dx =
                    point.X -
                    closest[0];

                double dy =
                    point.Y -
                    closest[1];

                double dz =
                    point.Z -
                    closest[2];

                double distance =
                    Math.Sqrt(
                        dx * dx +
                        dy * dy +
                        dz * dz);

                maximum =
                    Math.Max(
                        maximum,
                        distance);
            }

            return maximum;
        }

        private static void VerifySameSplineIdentityOrThrow(
            SketchSpline candidateSpline,
            SketchSpline originalSpline,
            string expectedKey,
            string stage)
        {
            if (candidateSpline == null ||
                originalSpline == null)
            {
                throw new InvalidOperationException(
                    "Spline bị mất tại " +
                    stage +
                    ".");
            }

            SketchSegment segment =
                candidateSpline as SketchSegment;

            if (segment == null)
            {
                throw new InvalidOperationException(
                    "Không cast được spline segment tại " +
                    stage +
                    ".");
            }

            string actualKey =
                GetSketchSegmentKey(
                    segment);

            bool sameKey =
                string.Equals(
                    expectedKey,
                    actualKey,
                    StringComparison.Ordinal);

            bool sameCom =
                IsSameComObject(
                    candidateSpline,
                    originalSpline);

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] SAME SPLINE CHECK " +
                "stage=\"" +
                stage +
                "\"" +
                ", sameKey=" +
                sameKey +
                ", sameCom=" +
                sameCom +
                ", beforeKey=" +
                expectedKey +
                ", afterKey=" +
                actualKey);

            if (!sameKey &&
                !sameCom)
            {
                throw new InvalidOperationException(
                    "Spline entity thực sự thay đổi tại " +
                    stage +
                    ". Before=" +
                    expectedKey +
                    ", After=" +
                    actualKey +
                    ".");
            }
        }

        private static List<PreservedConstructionLineSnapshot> CaptureBlockingConstructionLinesForTemporaryDetach(
            Sketch activeSketch,
            List<string> blockerSegmentKeys)
        {
            List<PreservedConstructionLineSnapshot> result =
                new List<PreservedConstructionLineSnapshot>();

            if (activeSketch == null ||
                blockerSegmentKeys == null ||
                blockerSegmentKeys.Count == 0)
            {
                return result;
            }

            HashSet<string> uniqueKeys =
                new HashSet<string>(
                    blockerSegmentKeys,
                    StringComparer.Ordinal);

            foreach (string key in uniqueKeys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                SketchSegment segment =
                    ReacquireSketchSegmentByKey(
                        activeSketch,
                        key);

                if (segment == null)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V36.9 CENTERLINE SNAPSHOT missing key=" +
                        key);
                    continue;
                }

                bool construction = false;
                try
                {
                    construction = segment.ConstructionGeometry;
                }
                catch
                {
                }

                SketchLine line =
                    segment as SketchLine;

                if (!construction || line == null)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V36.9 CENTERLINE SNAPSHOT unsupported key=" +
                        key +
                        ", construction=" + construction +
                        ", runtime=" + ((object)segment).GetType().FullName);
                    continue;
                }

                SketchPoint start = null;
                SketchPoint end = null;

                try
                {
                    start = line.GetStartPoint2() as SketchPoint;
                    end = line.GetEndPoint2() as SketchPoint;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V36.9 CENTERLINE SNAPSHOT endpoint read failed key=" +
                        key + ": " + ex.Message);
                }

                if (start == null || end == null)
                    continue;

                PreservedConstructionLineSnapshot snapshot =
                    new PreservedConstructionLineSnapshot
                    {
                        OriginalKey = key,
                        StartX = start.X,
                        StartY = start.Y,
                        StartZ = start.Z,
                        EndX = end.X,
                        EndY = end.Y,
                        EndZ = end.Z
                    };

                result.Add(snapshot);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V36.9 CENTERLINE SNAPSHOT " +
                    "key=" + key +
                    ", startMm=(" +
                    (snapshot.StartX * 1000.0).ToString("0.######", CultureInfo.InvariantCulture) + "," +
                    (snapshot.StartY * 1000.0).ToString("0.######", CultureInfo.InvariantCulture) + "," +
                    (snapshot.StartZ * 1000.0).ToString("0.######", CultureInfo.InvariantCulture) + ")" +
                    ", endMm=(" +
                    (snapshot.EndX * 1000.0).ToString("0.######", CultureInfo.InvariantCulture) + "," +
                    (snapshot.EndY * 1000.0).ToString("0.######", CultureInfo.InvariantCulture) + "," +
                    (snapshot.EndZ * 1000.0).ToString("0.######", CultureInfo.InvariantCulture) + ")");
            }

            return result;
        }

        private static int RestoreTemporarilyDetachedConstructionLines(
            ModelDoc2 model,
            List<PreservedConstructionLineSnapshot> snapshots)
        {
            if (model == null ||
                snapshots == null ||
                snapshots.Count == 0)
            {
                return 0;
            }

            int restored = 0;

            foreach (PreservedConstructionLineSnapshot snapshot in snapshots)
            {
                if (snapshot == null)
                    continue;

                SketchSegment created = null;

                try
                {
                    created =
                        model.SketchManager.CreateLine(
                            snapshot.StartX,
                            snapshot.StartY,
                            snapshot.StartZ,
                            snapshot.EndX,
                            snapshot.EndY,
                            snapshot.EndZ)
                        as SketchSegment;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V36.9 CENTERLINE RESTORE CreateLine failed key=" +
                        snapshot.OriginalKey + ": " + ex.Message);
                }

                if (created == null)
                    continue;

                try
                {
                    created.ConstructionGeometry = true;
                }
                catch
                {
                }

                restored++;

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V36.9 CENTERLINE RESTORE " +
                    "originalKey=" + snapshot.OriginalKey +
                    ", restoredIndex=" + restored +
                    ", relationsAdded=0");
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V36.9 CENTERLINE RESTORE RESULT " +
                "restored=" + restored +
                "/" + snapshots.Count +
                ", manualRelationsRequired=True");

            return restored;
        }

        private static SelectiveBlockingConstructionCleanupResult
            DeleteOnlyBlockingConstructionSplinesForMove(
                ModelDoc2 model,
                Sketch activeSketch,
                List<string> blockerSegmentKeys)
        {
            SelectiveBlockingConstructionCleanupResult result =
                new SelectiveBlockingConstructionCleanupResult();

            if (model == null ||
                activeSketch == null ||
                blockerSegmentKeys == null ||
                blockerSegmentKeys.Count == 0)
            {
                return result;
            }

            HashSet<string> uniqueKeys =
                new HashSet<string>(
                    blockerSegmentKeys,
                    StringComparer.Ordinal);

            result.Discovered = uniqueKeys.Count;

            foreach (string key in uniqueKeys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                SketchSegment blocker =
                    ReacquireSketchSegmentByKey(
                        activeSketch,
                        key);

                if (blocker == null)
                {
                    result.Missing++;
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V37.0 BLOCKER CLASSIFY " +
                        "key=" + key +
                        ", type=MISSING, action=SKIP");
                    continue;
                }

                bool construction = false;
                try
                {
                    construction = blocker.ConstructionGeometry;
                }
                catch
                {
                }

                if (!construction)
                {
                    result.PreservedOtherConstruction++;
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V37.0 BLOCKER CLASSIFY " +
                        "key=" + key +
                        ", construction=False, action=PRESERVE");
                    continue;
                }

                // Keep straight construction center lines exactly as requested.
                // Their old relations were already released by the unlock pass.
                SketchLine straightLine = blocker as SketchLine;
                if (straightLine != null)
                {
                    result.PreservedStraightLines++;
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V37.0 BLOCKER CLASSIFY " +
                        "key=" + key +
                        ", type=SketchLine, construction=True" +
                        ", action=PRESERVE_STRAIGHT_CENTERLINE");
                    continue;
                }

                // Delete only construction splines.  Never delete the selected
                // SAME spline because ReleaseAutoSplineRelations... explicitly
                // excludes its segment key from blockerSegmentKeys.
                SketchSpline centerSpline = blocker as SketchSpline;
                if (centerSpline == null)
                {
                    result.PreservedOtherConstruction++;
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V37.0 BLOCKER CLASSIFY " +
                        "key=" + key +
                        ", type=OTHER_CONSTRUCTION, action=PRESERVE");
                    continue;
                }

                model.ClearSelection2(true);

                bool selected = false;
                try
                {
                    selected = blocker.Select4(false, null);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V37.0 CENTER-SPLINE select failed " +
                        "key=" + key + ": " + ex.Message);
                }

                bool deleted = false;
                if (selected)
                {
                    try
                    {
                        deleted = model.Extension.DeleteSelection2(0);
                    }
                    catch
                    {
                    }

                    if (!deleted)
                    {
                        try
                        {
                            // ModelDoc2.EditDelete() is void in this interop version.
                            // Verify deletion by reacquiring the segment key instead of
                            // treating the API call as a Boolean result.
                            model.EditDelete();

                            SketchSegment afterDelete =
                                ReacquireSketchSegmentByKey(
                                    activeSketch,
                                    key);

                            deleted = afterDelete == null;
                        }
                        catch
                        {
                        }
                    }
                }

                model.ClearSelection2(true);

                if (deleted)
                    result.DeletedConstructionSplines++;

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE V37.0 BLOCKER CLASSIFY " +
                    "key=" + key +
                    ", type=SketchSpline, construction=True" +
                    ", action=DELETE_CENTER_SPLINE" +
                    ", selected=" + selected +
                    ", deleted=" + deleted);
            }

            model.ClearSelection2(true);

            return result;
        }

        private static int DeleteBlockingConstructionSegmentsForMove(
            ModelDoc2 model,
            Sketch activeSketch,
            List<string> blockerSegmentKeys)
        {
            if (model == null ||
                activeSketch == null ||
                blockerSegmentKeys == null ||
                blockerSegmentKeys.Count == 0)
            {
                return 0;
            }

            model.ClearSelection2(true);

            int selected =
                0;

            HashSet<string> uniqueKeys =
                new HashSet<string>(
                    blockerSegmentKeys,
                    StringComparer.Ordinal);

            foreach (string key in
                     uniqueKeys)
            {
                if (string.IsNullOrWhiteSpace(
                        key))
                {
                    continue;
                }

                SketchSegment blocker =
                    ReacquireSketchSegmentByKey(
                        activeSketch,
                        key);

                if (blocker == null)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V35.4 BLOCKER missing key=" +
                        key);

                    continue;
                }

                bool construction =
                    false;

                try
                {
                    construction =
                        blocker.ConstructionGeometry;
                }
                catch
                {
                }

                if (!construction)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V35.4 BLOCKER skip non-construction key=" +
                        key);

                    continue;
                }

                bool selectedNow =
                    false;

                try
                {
                    selectedNow =
                        blocker.Select4(
                            selected > 0,
                            null);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V35.4 BLOCKER select failed key=" +
                        key +
                        ": " +
                        ex.Message);
                }

                if (selectedNow)
                {
                    selected++;
                }
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V35.4 BLOCKER SELECT " +
                "selected=" +
                selected +
                "/" +
                uniqueKeys.Count);

            if (selected !=
                uniqueKeys.Count)
            {
                model.ClearSelection2(true);
                return 0;
            }

            ModelDocExtension extension =
                model.Extension
                as ModelDocExtension;

            bool deleted =
                false;

            if (extension != null)
            {
                try
                {
                    deleted =
                        extension.DeleteSelection2(
                            0);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V35.4 BLOCKER DeleteSelection2 failed: " +
                        ex.Message);
                }
            }

            if (!deleted)
            {
                try
                {
                    model.EditDelete();
                    deleted =
                        true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] MOVE V35.4 BLOCKER EditDelete fallback failed: " +
                        ex.Message);
                }
            }

            model.ClearSelection2(true);

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE V35.4 BLOCKER DELETE RESULT deleted=" +
                deleted +
                ", count=" +
                selected);

            return
                deleted
                    ? selected
                    : 0;
        }

        private static int DeleteAllDisposableHelperLines(
            ModelDoc2 model,
            Sketch activeSketch,
            List<SketchSegment> helperLines)
        {
            if (model == null ||
                activeSketch == null ||
                helperLines == null ||
                helperLines.Count == 0)
            {
                return 0;
            }

            model.ClearSelection2(true);

            int selected =
                0;

            foreach (SketchSegment helper in
                     helperLines)
            {
                if (helper == null)
                    continue;

                try
                {
                    if (helper.Select4(
                            selected > 0,
                            null))
                    {
                        selected++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] DELETE HELPER select failed: " +
                        ex.Message);
                }
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] DELETE HELPERS selected=" +
                selected +
                "/" +
                helperLines.Count);

            if (selected !=
                helperLines.Count)
            {
                model.ClearSelection2(true);

                return 0;
            }

            ModelDocExtension extension =
                model.Extension
                as ModelDocExtension;

            bool deleted =
                false;

            if (extension != null)
            {
                try
                {
                    // 0 = delete only the selected sketch entities.
                    deleted =
                        extension.DeleteSelection2(
                            0);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] DELETE HELPERS " +
                        "DeleteSelection2 failed: " +
                        ex.Message);
                }
            }

            if (!deleted)
            {
                try
                {
                    model.EditDelete();

                    deleted =
                        true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] DELETE HELPERS " +
                        "EditDelete fallback failed: " +
                        ex.Message);
                }
            }

            model.ClearSelection2(true);

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] DELETE HELPERS result=" +
                deleted);

            return
                deleted
                    ? selected
                    : 0;
        }

        private static Sketch RefreshSame3DSketchForMove(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName)
        {
            if (model == null ||
                owningSketchFeature == null)
            {
                throw new InvalidOperationException(
                    "Thiếu SAME 3D Sketch feature để refresh solver.");
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE SOLVER REFRESH BEGIN " +
                "sketch=\"" +
                owningSketchName +
                "\"");

            Sketch activeSketch =
                model.SketchManager.ActiveSketch
                as Sketch;

            if (activeSketch != null)
            {
                if (!TryIs3DSketch(
                        activeSketch))
                {
                    throw new InvalidOperationException(
                        "Active sketch không phải 3D Sketch khi refresh MOVE.");
                }

                Feature activeFeature =
                    FindOwningFeatureForSketch(
                        model,
                        activeSketch);

                if (activeFeature == null ||
                    !IsSameComObject(
                        activeFeature,
                        owningSketchFeature))
                {
                    throw new InvalidOperationException(
                        "Active sketch không phải SAME 3D Sketch khi refresh MOVE.");
                }

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE SOLVER REFRESH exit-same-sketch BEGIN");

                model.SketchManager.Insert3DSketch(
                    true);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE SOLVER REFRESH exit-same-sketch END");

                if (model.SketchManager.ActiveSketch != null)
                {
                    throw new InvalidOperationException(
                        "Không thoát được SAME 3D Sketch khi refresh MOVE.");
                }
            }

            // Commit deleted relations/helper geometry while safely outside
            // sketch edit mode. This is intentionally BEFORE any new Equal
            // command and therefore is unrelated to the old post-Equal crash.
            bool rebuildOk =
                false;

            try
            {
                rebuildOk =
                    model.EditRebuild3();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "EditRebuild3 thất bại khi refresh MOVE solver.",
                    ex);
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE SOLVER REFRESH rebuild=" +
                rebuildOk);

            model.ClearSelection2(
                true);

            bool selected =
                false;

            try
            {
                selected =
                    owningSketchFeature.Select2(
                        false,
                        0);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Không select lại SAME 3D Sketch sau refresh.",
                    ex);
            }

            if (!selected)
            {
                throw new InvalidOperationException(
                    "Không select lại được SAME 3D Sketch \"" +
                    owningSketchName +
                    "\" sau refresh.");
            }

            try
            {
                model.EditSketch();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Không re-enter SAME 3D Sketch sau refresh.",
                    ex);
            }

            Sketch reentered =
                model.SketchManager.ActiveSketch
                as Sketch;

            if (reentered == null ||
                !TryIs3DSketch(
                    reentered))
            {
                throw new InvalidOperationException(
                    "Không trở lại Edit 3D Sketch sau solver refresh.");
            }

            Feature reenteredFeature =
                FindOwningFeatureForSketch(
                    model,
                    reentered);

            if (reenteredFeature == null ||
                !IsSameComObject(
                    reenteredFeature,
                    owningSketchFeature))
            {
                throw new InvalidOperationException(
                    "Re-enter nhầm Sketch sau solver refresh.");
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE SOLVER REFRESH REENTER PASS");

            return reentered;
        }

        private static Sketch ReacquireSame3DSketchForReadback(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName,
            string stage)
        {
            if (model == null ||
                owningSketchFeature == null)
            {
                throw new InvalidOperationException(
                    "Thiếu SAME 3D Sketch feature tại " +
                    stage +
                    ".");
            }

            Sketch sketch =
                GetSketchFromFeature(
                    owningSketchFeature);

            if (sketch == null)
            {
                throw new InvalidOperationException(
                    "Không reacquire được SAME 3D Sketch \"" +
                    owningSketchName +
                    "\" tại " +
                    stage +
                    ".");
            }

            if (!TryIs3DSketch(
                    sketch))
            {
                throw new InvalidOperationException(
                    "Feature cũ không còn là 3D Sketch tại " +
                    stage +
                    ".");
            }

            Feature readbackFeature =
                FindOwningFeatureForSketch(
                    model,
                    sketch);

            if (readbackFeature == null ||
                !IsSameComObject(
                    readbackFeature,
                    owningSketchFeature))
            {
                throw new InvalidOperationException(
                    "Reacquire nhầm 3D Sketch feature tại " +
                    stage +
                    ".");
            }

            Sketch active =
                model.SketchManager.ActiveSketch
                as Sketch;

            bool sameSketchIsActive =
                false;

            if (active != null)
            {
                Feature activeFeature =
                    FindOwningFeatureForSketch(
                        model,
                        active);

                sameSketchIsActive =
                    activeFeature != null &&
                    IsSameComObject(
                        activeFeature,
                        owningSketchFeature);
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] SAME SKETCH READBACK " +
                "stage=\"" +
                stage +
                "\"" +
                ", feature=\"" +
                owningSketchName +
                "\"" +
                ", activeSketchPresent=" +
                (active != null) +
                ", sameSketchIsActive=" +
                sameSketchIsActive +
                ", reacquired=True");

            return sketch;
        }

        private static Sketch EnsureSame3DSketchEditing(
            ModelDoc2 model,
            Sketch expectedSketch,
            Feature owningSketchFeature,
            string owningSketchName)
        {
            if (model == null ||
                owningSketchFeature == null)
            {
                throw new InvalidOperationException(
                    "Thiếu SAME 3D Sketch feature.");
            }

            Sketch active =
                model.SketchManager.ActiveSketch
                as Sketch;

            if (active != null)
            {
                if (!TryIs3DSketch(
                        active))
                {
                    throw new InvalidOperationException(
                        "Active sketch hiện tại không phải 3D Sketch.");
                }

                Feature activeFeature =
                    FindOwningFeatureForSketch(
                        model,
                        active);

                if (activeFeature != null &&
                    IsSameComObject(
                        activeFeature,
                        owningSketchFeature))
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "SAME SKETCH already active before fresh relation rebuild.");

                    return active;
                }

                throw new InvalidOperationException(
                    "Đang Edit một Sketch khác, không phải SAME 3D Sketch.");
            }

            // SOLIDWORKS may have left edit mode after heavy geometry changes.
            // Re-enter the existing feature directly. Never Insert3DSketch.
            model.ClearSelection2(true);

            bool selected =
                false;

            try
            {
                selected =
                    owningSketchFeature.Select2(
                        false,
                        0);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Không select lại được SAME 3D Sketch feature.",
                    ex);
            }

            if (!selected)
            {
                throw new InvalidOperationException(
                    "Không select lại được SAME 3D Sketch \"" +
                    owningSketchName +
                    "\".");
            }

            try
            {
                model.EditSketch();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Không re-enter được SAME 3D Sketch \"" +
                    owningSketchName +
                    "\".",
                    ex);
            }

            Sketch reentered =
                model.SketchManager.ActiveSketch
                as Sketch;

            if (reentered == null ||
                !TryIs3DSketch(
                    reentered))
            {
                throw new InvalidOperationException(
                    "Không trở lại môi trường Edit 3D Sketch.");
            }

            Feature reenteredFeature =
                FindOwningFeatureForSketch(
                    model,
                    reentered);

            if (reenteredFeature == null ||
                !IsSameComObject(
                    reenteredFeature,
                    owningSketchFeature))
            {
                throw new InvalidOperationException(
                    "Re-enter vào nhầm 3D Sketch feature.");
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] " +
                "SAME SKETCH reentered before fresh relation rebuild.");

            return reentered;
        }

        private static Sketch RestartSame3DSketchForEqual(
            ModelDoc2 model,
            Feature owningSketchFeature,
            string owningSketchName)
        {
            if (model == null ||
                owningSketchFeature == null)
            {
                throw new InvalidOperationException(
                    "Không có 3D Sketch feature để stabilize Equal.");
            }

            Sketch activeSketch =
                model.SketchManager.ActiveSketch
                as Sketch;

            // ------------------------------------------------------------
            // V10.5:
            // SOLIDWORKS can automatically leave Edit 3D Sketch after the
            // new external Edge relations / rebuild.
            //
            // That is NOT a failure.  Two valid states are accepted here:
            //
            //   A) SAME 3D sketch is still active
            //      -> explicitly exit it, then re-enter the same feature.
            //
            //   B) ActiveSketch is already null
            //      -> DO NOT call Insert3DSketch(true), because that could
            //         create a new 3D sketch.
            //      -> directly re-enter the existing owning feature.
            // ------------------------------------------------------------
            if (activeSketch != null)
            {
                if (!TryIs3DSketch(
                        activeSketch))
                {
                    throw new InvalidOperationException(
                        "Đang Edit một Sketch khác, không phải 3D Sketch cũ.");
                }

                Feature activeFeature =
                    FindOwningFeatureForSketch(
                        model,
                        activeSketch);

                if (activeFeature == null ||
                    !IsSameComObject(
                        activeFeature,
                        owningSketchFeature))
                {
                    throw new InvalidOperationException(
                        "Active 3D Sketch không phải SAME sketch feature cần stabilize.");
                }

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "MOVE EQUAL STABILIZE state=ACTIVE_SAME_SKETCH " +
                    "sketch=\"" +
                    owningSketchName +
                    "\"");

                // With the SAME active 3D sketch, this toggles EXIT only.
                try
                {
                    model.SketchManager.Insert3DSketch(
                        true);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Không thoát được SAME 3D Sketch để stabilize Equal.",
                        ex);
                }

                if (model.SketchManager.ActiveSketch != null)
                {
                    throw new InvalidOperationException(
                        "SolidWorks vẫn còn ở Edit Sketch sau lệnh exit.");
                }
            }
            else
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "MOVE EQUAL STABILIZE state=ALREADY_OUTSIDE_EDIT " +
                    "sketch=\"" +
                    owningSketchName +
                    "\"");

                // IMPORTANT:
                // Do not call Insert3DSketch here.
                // ActiveSketch == null means Insert3DSketch could CREATE
                // a new 3D sketch, which is forbidden by the in-place design.
            }

            try
            {
                model.GraphicsRedraw2();
                System.Windows.Forms.Application.DoEvents();
            }
            catch
            {
            }

            model.ClearSelection2(true);

            bool selected =
                false;

            try
            {
                selected =
                    owningSketchFeature.Select2(
                        false,
                        0);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Không select lại được SAME 3D Sketch feature.",
                    ex);
            }

            if (!selected)
            {
                throw new InvalidOperationException(
                    "Không select lại được SAME 3D Sketch \"" +
                    owningSketchName +
                    "\".");
            }

            try
            {
                model.EditSketch();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Không re-enter được SAME 3D Sketch \"" +
                    owningSketchName +
                    "\".",
                    ex);
            }

            Sketch reenteredSketch =
                model.SketchManager.ActiveSketch
                as Sketch;

            if (reenteredSketch == null ||
                !TryIs3DSketch(
                    reenteredSketch))
            {
                throw new InvalidOperationException(
                    "Re-enter không trở lại môi trường 3D Sketch.");
            }

            Feature featureAfter =
                FindOwningFeatureForSketch(
                    model,
                    reenteredSketch);

            if (featureAfter == null ||
                !IsSameComObject(
                    featureAfter,
                    owningSketchFeature))
            {
                throw new InvalidOperationException(
                    "Re-enter vào nhầm Sketch feature.");
            }

            try
            {
                model.GraphicsRedraw2();
                System.Windows.Forms.Application.DoEvents();
            }
            catch
            {
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] " +
                "MOVE EQUAL STABILIZE reentered SAME sketch=\"" +
                owningSketchName +
                "\"");

            return reenteredSketch;
        }

        private static bool TryRestoreOldAutoSplineGeometry(
            ISldWorks app,
            ModelDoc2 model,
            Sketch activeSketch,
            List<double[]> oldCoordinates)
        {
            if (model == null ||
                activeSketch == null ||
                oldCoordinates == null ||
                oldCoordinates.Count <
                    MinimumPointCount)
            {
                return false;
            }

            try
            {
                // Remove any partial replacement geometry first.
                AutoSplineGeometry partial =
                    TryGetAutoSplineGeometry(
                        activeSketch);

                if (partial != null)
                {
                    DeleteAutoSplineGeometry(
                        model,
                        partial);
                }

                HashSet<string> before =
                    CaptureSketchPointIds(
                        activeSketch);

                double[] oldData =
                    FlattenPoints(
                        oldCoordinates);

                object restoredSpline =
                    CreateSplineCompat(
                        model.SketchManager,
                        oldData);

                if (restoredSpline == null)
                    return false;

                List<SketchPoint> restoredPoints =
                    FindOrderedNewFitPoints(
                        activeSketch,
                        before,
                        oldCoordinates);

                if (restoredPoints.Count !=
                    oldCoordinates.Count)
                {
                    return false;
                }

                // Restore internal equal-spacing geometry only.
                // External reference to the previous Edge is intentionally
                // not guessed here.
                List<SketchSegment> lines =
                    CreateConstructionChainOnly(
                        model,
                        restoredPoints);

                bool equalRestored =
                    false;

                if (lines.Count ==
                    restoredPoints.Count - 1)
                {
                    UiEqualAttempt equalAttempt =
                        TryCreateEqualRelationWithUiCommand(
                            app,
                            model,
                            activeSketch,
                            lines);

                    equalRestored =
                        equalAttempt.Verified;
                }

                try
                {
                    model.EditRebuild3();
                }
                catch
                {
                }

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE RESTORE OLD " +
                    "points=" +
                    restoredPoints.Count +
                    ", lines=" +
                    lines.Count +
                    ", equalRestored=" +
                    equalRestored);

                return
                    restoredPoints.Count ==
                    oldCoordinates.Count;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "Restore old geometry failed: " +
                    ex.Message);

                return false;
            }
        }

        private static List<SketchSegment> CreateConstructionChainOnly(
            ModelDoc2 model,
            List<SketchPoint> fitPoints)
        {
            List<SketchSegment> result =
                new List<SketchSegment>();

            if (model == null ||
                fitPoints == null ||
                fitPoints.Count < 2)
            {
                return result;
            }

            for (int i = 0;
                 i < fitPoints.Count - 1;
                 i++)
            {
                SketchPoint a =
                    fitPoints[i];

                SketchPoint b =
                    fitPoints[i + 1];

                if (a == null ||
                    b == null)
                {
                    continue;
                }

                SketchSegment line =
                    null;

                try
                {
                    line =
                        model.SketchManager.CreateLine(
                            a.X,
                            a.Y,
                            a.Z,
                            b.X,
                            b.Y,
                            b.Z)
                        as SketchSegment;
                }
                catch
                {
                }

                if (line == null)
                    continue;

                try
                {
                    line.ConstructionGeometry =
                        true;
                }
                catch
                {
                }

                SketchLine sketchLine =
                    line as SketchLine;

                if (sketchLine != null)
                {
                    SketchPoint lineStart =
                        null;

                    SketchPoint lineEnd =
                        null;

                    try
                    {
                        lineStart =
                            sketchLine.GetStartPoint2()
                            as SketchPoint;

                        lineEnd =
                            sketchLine.GetEndPoint2()
                            as SketchPoint;
                    }
                    catch
                    {
                    }

                    if (lineStart != null)
                    {
                        AddCoincident(
                            model,
                            lineStart,
                            a);
                    }

                    if (lineEnd != null)
                    {
                        AddCoincident(
                            model,
                            lineEnd,
                            b);
                    }
                }

                result.Add(
                    line);
            }

            return result;
        }

        private static bool DeleteAutoSplineGeometry(
            ModelDoc2 model,
            AutoSplineGeometry geometry)
        {
            if (model == null ||
                geometry == null ||
                geometry.Spline == null ||
                geometry.ConstructionLines == null)
            {
                return false;
            }

            SketchSegment splineSegment =
                geometry.Spline as SketchSegment;

            if (splineSegment == null)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "DELETE OLD failed: spline is not SketchSegment.");

                return false;
            }

            int expectedSelectionCount =
                1 +
                geometry.ConstructionLines.Count;

            model.ClearSelection2(true);

            int selectedCount =
                0;

            try
            {
                if (splineSegment.Select4(
                        false,
                        null))
                {
                    selectedCount++;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "DELETE OLD select spline failed: " +
                    ex.Message);
            }

            foreach (SketchSegment line in
                     geometry.ConstructionLines)
            {
                if (line == null)
                    continue;

                try
                {
                    if (line.Select4(
                            selectedCount > 0,
                            null))
                    {
                        selectedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "DELETE OLD select line failed: " +
                        ex.Message);
                }
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] DELETE OLD selection " +
                "selected=" +
                selectedCount +
                ", expected=" +
                expectedSelectionCount);

            // Do not call EditDelete on a partial selection.
            // This protects unrelated geometry in the same 3D sketch.
            if (selectedCount !=
                expectedSelectionCount)
            {
                model.ClearSelection2(true);

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "DELETE OLD aborted: incomplete selection.");

                return false;
            }

            try
            {
                model.EditDelete();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "DELETE OLD EditDelete failed: " +
                    ex.Message);

                model.ClearSelection2(true);

                return false;
            }

            model.ClearSelection2(true);

            // IMPORTANT: do NOT rebuild while the sketch is temporarily
            // empty.  On this SOLIDWORKS version a rebuild can terminate
            // sketch-edit mode.  The MOVE workflow immediately re-enters the
            // same sketch feature if EditDelete itself already ended editing.
            return true;
        }

        private static int CountSplineSegments(
            Sketch sketch)
        {
            if (sketch == null)
                return -1;

            object[] segments =
                null;

            try
            {
                segments =
                    sketch.GetSketchSegments()
                    as object[];
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "CountSplineSegments failed: " +
                    ex.Message);

                return -1;
            }

            if (segments == null)
                return 0;

            int count =
                0;

            foreach (object obj in segments)
            {
                SketchSpline spline =
                    obj as SketchSpline;

                if (spline != null)
                    count++;
            }

            return count;
        }

        private static AutoSplineGeometry TryGetMoveSplineGeometry(
            Sketch sketch)
        {
            if (sketch == null)
                return null;

            object[] segments =
                null;

            try
            {
                segments =
                    sketch.GetSketchSegments()
                    as object[];
            }
            catch
            {
            }

            if (segments == null)
                return null;

            List<SketchSpline> splines =
                new List<SketchSpline>();

            List<SketchSegment> constructionCandidates =
                new List<SketchSegment>();

            foreach (object obj in segments)
            {
                SketchSpline spline =
                    obj as SketchSpline;

                if (spline != null)
                {
                    splines.Add(
                        spline);

                    continue;
                }

                SketchSegment segment =
                    obj as SketchSegment;

                SketchLine line =
                    obj as SketchLine;

                if (segment == null ||
                    line == null)
                {
                    continue;
                }

                bool construction =
                    false;

                try
                {
                    construction =
                        segment.ConstructionGeometry;
                }
                catch
                {
                }

                if (construction)
                {
                    constructionCandidates.Add(
                        segment);
                }
            }

            if (splines.Count == 0)
                return null;

            // Prefer the spline that has the strongest old AUTO helper-chain
            // match. If helpers were already removed by V12, prefer the spline
            // with the most fit points. This permits repeated MOVE operations
            // while still preserving the SAME spline entity.
            AutoSplineGeometry best =
                null;

            int bestMatchedLines =
                -1;

            int bestPointCount =
                -1;

            foreach (SketchSpline spline in splines)
            {
                List<SketchPoint> points =
                    GetSplineFitPoints(
                        spline);

                if (points.Count <
                    MinimumPointCount)
                {
                    continue;
                }

                List<SketchSegment> lines =
                    FindOrderedConstructionLines(
                        points,
                        constructionCandidates);

                if (best == null ||
                    lines.Count >
                        bestMatchedLines ||
                    (lines.Count ==
                        bestMatchedLines &&
                     points.Count >
                        bestPointCount))
                {
                    bestMatchedLines =
                        lines.Count;

                    bestPointCount =
                        points.Count;

                    best =
                        new AutoSplineGeometry
                        {
                            Spline =
                                spline,

                            FitPoints =
                                points,

                            ConstructionLines =
                                lines
                        };
                }
            }

            if (best != null)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] MOVE GEOMETRY DETECT " +
                    "points=" +
                    best.FitPoints.Count +
                    ", helperLines=" +
                    best.ConstructionLines.Count +
                    ", strictHelperChain=False");
            }

            return best;
        }

        private static AutoSplineGeometry TryGetAutoSplineGeometry(
            Sketch sketch)
        {
            if (sketch == null)
                return null;

            object[] segments =
                null;

            try
            {
                segments =
                    sketch.GetSketchSegments()
                    as object[];
            }
            catch
            {
            }

            if (segments == null)
                return null;

            List<SketchSpline> splines =
                new List<SketchSpline>();

            List<SketchSegment> constructionCandidates =
                new List<SketchSegment>();

            foreach (object obj in segments)
            {
                SketchSpline spline =
                    obj as SketchSpline;

                if (spline != null)
                {
                    splines.Add(spline);
                    continue;
                }

                SketchSegment segment =
                    obj as SketchSegment;

                SketchLine line =
                    obj as SketchLine;

                if (segment != null &&
                    line != null)
                {
                    bool isConstruction =
                        false;

                    try
                    {
                        isConstruction =
                            segment.ConstructionGeometry;
                    }
                    catch
                    {
                    }

                    if (isConstruction)
                    {
                        constructionCandidates.Add(
                            segment);
                    }
                }
            }

            // AUTO SPLINE sketch is expected to contain one generated spline.
            // If multiple splines exist, select the one that has the strongest
            // construction-line chain match.
            AutoSplineGeometry best =
                null;

            int bestMatchedLines =
                -1;

            foreach (SketchSpline spline in splines)
            {
                List<SketchPoint> points =
                    GetSplineFitPoints(
                        spline);

                if (points.Count <
                    MinimumPointCount)
                {
                    continue;
                }

                List<SketchSegment> orderedLines =
                    FindOrderedConstructionLines(
                        points,
                        constructionCandidates);

                if (orderedLines.Count >
                    bestMatchedLines)
                {
                    bestMatchedLines =
                        orderedLines.Count;

                    best =
                        new AutoSplineGeometry
                        {
                            Spline =
                                spline,

                            FitPoints =
                                points,

                            ConstructionLines =
                                orderedLines
                        };
                }
            }

            if (best == null)
                return null;

            if (best.ConstructionLines.Count !=
                best.FitPoints.Count - 1)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "AUTO geometry chain incomplete " +
                    "points=" +
                    best.FitPoints.Count +
                    ", lines=" +
                    best.ConstructionLines.Count);

                return null;
            }

            return best;
        }

        private static List<SketchPoint> GetSplineFitPoints(
            SketchSpline spline)
        {
            List<SketchPoint> result =
                new List<SketchPoint>();

            if (spline == null)
                return result;

            object[] points =
                null;

            try
            {
                points =
                    spline.GetPoints2()
                    as object[];
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "SketchSpline.GetPoints2 failed: " +
                    ex.Message);
            }

            if (points == null)
                return result;

            foreach (object obj in points)
            {
                SketchPoint point =
                    obj as SketchPoint;

                if (point != null)
                    result.Add(point);
            }

            return result;
        }

        private static List<SketchSegment> FindOrderedConstructionLines(
            List<SketchPoint> fitPoints,
            List<SketchSegment> candidates)
        {
            List<SketchSegment> result =
                new List<SketchSegment>();

            if (fitPoints == null ||
                candidates == null ||
                fitPoints.Count < 2)
            {
                return result;
            }

            HashSet<string> used =
                new HashSet<string>(
                    StringComparer.Ordinal);

            for (int i = 0;
                 i < fitPoints.Count - 1;
                 i++)
            {
                SketchPoint a =
                    fitPoints[i];

                SketchPoint b =
                    fitPoints[i + 1];

                SketchSegment match =
                    null;

                foreach (SketchSegment candidate in candidates)
                {
                    if (candidate == null)
                        continue;

                    string key =
                        GetSketchSegmentKey(
                            candidate);

                    if (used.Contains(key))
                        continue;

                    SketchLine line =
                        candidate as SketchLine;

                    if (line != null &&
                        LineConnectsPoints(
                            line,
                            a,
                            b,
                            1.0e-6))
                    {
                        match =
                            candidate;

                        used.Add(key);

                        break;
                    }
                }

                if (match == null)
                    break;

                result.Add(match);
            }

            return result;
        }

        private static bool LineConnectsPoints(
            SketchLine line,
            SketchPoint a,
            SketchPoint b,
            double tolerance)
        {
            if (line == null ||
                a == null ||
                b == null)
            {
                return false;
            }

            try
            {
                SketchPoint start =
                    line.GetStartPoint2()
                    as SketchPoint;

                SketchPoint end =
                    line.GetEndPoint2()
                    as SketchPoint;

                if (start == null ||
                    end == null)
                {
                    return false;
                }

                double direct =
                    DistanceSketchPoints(
                        start,
                        a) +
                    DistanceSketchPoints(
                        end,
                        b);

                double reverse =
                    DistanceSketchPoints(
                        start,
                        b) +
                    DistanceSketchPoints(
                        end,
                        a);

                return
                    Math.Min(
                        direct,
                        reverse) <=
                    tolerance * 2.0;
            }
            catch
            {
                return false;
            }
        }

        private static double DistanceSketchPoints(
            SketchPoint a,
            SketchPoint b)
        {
            if (a == null ||
                b == null)
            {
                return double.MaxValue;
            }

            double dx =
                a.X - b.X;

            double dy =
                a.Y - b.Y;

            double dz =
                a.Z - b.Z;

            return
                Math.Sqrt(
                    dx * dx +
                    dy * dy +
                    dz * dz);
        }

        private static string GetSketchSegmentKey(
            SketchSegment segment)
        {
            if (segment == null)
                return "";

            try
            {
                int[] id =
                    segment.GetID()
                    as int[];

                if (id != null &&
                    id.Length >= 2)
                {
                    // All entities compared here are construction lines,
                    // so the segment ID pair is sufficient.  Do not call
                    // ISketchSegment.GetType() here because SOLIDWORKS uses
                    // that name for the sketch-segment enum, not System.Type.
                    return
                        "SEG:" +
                        id[0].ToString(
                            CultureInfo.InvariantCulture) +
                        ":" +
                        id[1].ToString(
                            CultureInfo.InvariantCulture);
                }
            }
            catch
            {
            }

            return
                segment.GetHashCode()
                    .ToString(
                        CultureInfo.InvariantCulture);
        }

        private static List<double[]> SnapshotPointCoordinates(
            List<SketchPoint> points)
        {
            List<double[]> result =
                new List<double[]>();

            if (points == null)
                return result;

            foreach (SketchPoint point in points)
            {
                result.Add(
                    new double[]
                    {
                        point.X,
                        point.Y,
                        point.Z
                    });
            }

            return result;
        }

        private static bool MoveFitPoints(
            List<SketchPoint> fitPoints,
            List<double[]> targetPoints)
        {
            if (fitPoints == null ||
                targetPoints == null ||
                fitPoints.Count !=
                targetPoints.Count)
            {
                return false;
            }

            bool allMoved =
                true;

            for (int i = 0;
                 i < fitPoints.Count;
                 i++)
            {
                SketchPoint point =
                    fitPoints[i];

                double[] target =
                    targetPoints[i];

                if (point == null ||
                    !IsPoint(target))
                {
                    allMoved =
                        false;

                    continue;
                }

                bool moved =
                    false;

                try
                {
                    moved =
                        point.SetCoords(
                            target[0],
                            target[1],
                            target[2]);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "SetCoords failed i=" +
                        i +
                        ": " +
                        ex.Message);
                }

                if (!moved)
                    allMoved = false;
            }

            return allMoved;
        }

        private static RelationBreakResult BreakMoveBlockingRelations(
            Sketch sketch,
            List<SketchPoint> fitPoints,
            List<SketchSegment> constructionLines)
        {
            RelationBreakResult result =
                new RelationBreakResult();

            if (sketch == null)
                return result;

            ISketchRelationManager manager =
                null;

            try
            {
                manager =
                    sketch.RelationManager;
            }
            catch
            {
            }

            if (manager == null)
                return result;

            object[] relations =
                null;

            try
            {
                relations =
                    manager.GetRelations(
                        (int)swSketchRelationFilterType_e
                            .swAll)
                    as object[];
            }
            catch
            {
            }

            if (relations == null)
                return result;

            HashSet<string> fitPointKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (SketchPoint point in fitPoints)
            {
                fitPointKeys.Add(
                    GetSketchPointKey(
                        point));
            }

            HashSet<string> lineKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (SketchSegment line in constructionLines)
            {
                lineKeys.Add(
                    GetSketchSegmentKey(
                        line));
            }

            foreach (object obj in relations)
            {
                SketchRelation relation =
                    obj as SketchRelation;

                if (relation == null)
                    continue;

                int relationType =
                    -1;

                try
                {
                    relationType =
                        relation.GetRelationType();
                }
                catch
                {
                }

                object[] entities =
                    GetRelationEntities(
                        relation);

                bool touchesFitPoint =
                    false;

                bool touchesOurLine =
                    false;

                bool hasExternalModelEntity =
                    false;

                if (entities != null)
                {
                    foreach (object entity in entities)
                    {
                        SketchPoint point =
                            entity as SketchPoint;

                        if (point != null &&
                            fitPointKeys.Contains(
                                GetSketchPointKey(
                                    point)))
                        {
                            touchesFitPoint =
                                true;
                        }

                        SketchSegment segment =
                            entity as SketchSegment;

                        if (segment != null &&
                            lineKeys.Contains(
                                GetSketchSegmentKey(
                                    segment)))
                        {
                            touchesOurLine =
                                true;
                        }

                        if (entity is Edge ||
                            entity is Vertex)
                        {
                            hasExternalModelEntity =
                                true;
                        }
                    }
                }

                bool equalRelation =
                    relationType ==
                    (int)swConstraintType_e
                        .swConstraintType_SAMELENGTH &&
                    touchesOurLine;

                bool externalRelation =
                    touchesFitPoint &&
                    hasExternalModelEntity;

                bool brokenPointRelation =
                    touchesFitPoint &&
                    (entities == null ||
                     entities.Length < 2);

                if (!equalRelation &&
                    !externalRelation &&
                    !brokenPointRelation)
                {
                    continue;
                }

                bool deleted =
                    false;

                try
                {
                    deleted =
                        manager.DeleteRelation(
                            relation);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "DeleteRelation failed: " +
                        ex.Message);
                }

                if (!deleted)
                    continue;

                if (equalRelation)
                {
                    result.EqualRelationsDeleted++;
                }
                else if (externalRelation)
                {
                    result.ExternalRelationsDeleted++;
                }
                else
                {
                    result.BrokenRelationsDeleted++;
                }
            }

            return result;
        }

        private static object[] GetRelationEntities(
            SketchRelation relation)
        {
            if (relation == null)
                return null;

            try
            {
                return
                    relation.GetEntities()
                    as object[];
            }
            catch
            {
                return null;
            }
        }

        private static ExternalReferenceSnapshot CaptureExternalReferences(
            Sketch sketch,
            List<SketchPoint> fitPoints)
        {
            ExternalReferenceSnapshot result =
                new ExternalReferenceSnapshot();

            if (sketch == null ||
                fitPoints == null)
            {
                return result;
            }

            ISketchRelationManager manager =
                null;

            try
            {
                manager =
                    sketch.RelationManager;
            }
            catch
            {
            }

            if (manager == null)
                return result;

            object[] relations =
                null;

            try
            {
                relations =
                    manager.GetRelations(
                        (int)swSketchRelationFilterType_e
                            .swAll)
                    as object[];
            }
            catch
            {
            }

            if (relations == null)
                return result;

            HashSet<string> fitKeys =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (SketchPoint point in fitPoints)
            {
                fitKeys.Add(
                    GetSketchPointKey(
                        point));
            }

            foreach (object obj in relations)
            {
                SketchRelation relation =
                    obj as SketchRelation;

                if (relation == null)
                    continue;

                object[] entities =
                    GetRelationEntities(
                        relation);

                if (entities == null)
                    continue;

                bool hasFitPoint =
                    false;

                foreach (object entity in entities)
                {
                    SketchPoint point =
                        entity as SketchPoint;

                    if (point != null &&
                        fitKeys.Contains(
                            GetSketchPointKey(
                                point)))
                    {
                        hasFitPoint =
                            true;

                        break;
                    }
                }

                if (!hasFitPoint)
                    continue;

                foreach (object entity in entities)
                {
                    Edge edge =
                        entity as Edge;

                    if (edge != null &&
                        result.Edge == null)
                    {
                        result.Edge =
                            edge;
                    }

                    Vertex vertex =
                        entity as Vertex;

                    if (vertex != null)
                    {
                        if (result.StartVertex == null)
                        {
                            result.StartVertex =
                                vertex;
                        }
                        else if (result.EndVertex == null)
                        {
                            result.EndVertex =
                                vertex;
                        }
                    }
                }
            }

            return result;
        }

        private static ExistingRelationResult ApplyRelationsToExistingGeometry(
            ISldWorks app,
            ModelDoc2 model,
            Sketch sketch,
            Edge edge,
            List<SketchPoint> fitPoints,
            List<SketchSegment> constructionLines)
        {
            ExistingRelationResult result =
                new ExistingRelationResult();

            if (app == null ||
                model == null ||
                sketch == null ||
                edge == null ||
                fitPoints == null ||
                constructionLines == null)
            {
                return result;
            }

            foreach (SketchPoint point in fitPoints)
            {
                model.ClearSelection2(true);

                bool pointSelected =
                    SelectComEntity(
                        point,
                        false);

                bool edgeSelected =
                    SelectComEntity(
                        edge,
                        true);

                if (!pointSelected ||
                    !edgeSelected)
                {
                    continue;
                }

                try
                {
                    model.SketchAddConstraints(
                        "sgCOINCIDENT");

                    result.PointOnEdgeCount++;
                }
                catch
                {
                }
            }

            Vertex startVertex =
                null;

            Vertex endVertex =
                null;

            try
            {
                startVertex =
                    edge.GetStartVertex()
                    as Vertex;

                endVertex =
                    edge.GetEndVertex()
                    as Vertex;
            }
            catch
            {
            }

            if (startVertex != null &&
                endVertex != null &&
                fitPoints.Count >= 2)
            {
                SketchPoint first =
                    fitPoints[0];

                SketchPoint last =
                    fitPoints[
                        fitPoints.Count - 1];

                double firstToStart =
                    DistanceSketchPointToVertex(
                        first,
                        startVertex);

                double firstToEnd =
                    DistanceSketchPointToVertex(
                        first,
                        endVertex);

                if (firstToStart <=
                    firstToEnd)
                {
                    if (AddCoincident(
                            model,
                            first,
                            startVertex))
                    {
                        result.EndpointCoincidentCount++;
                    }

                    if (AddCoincident(
                            model,
                            last,
                            endVertex))
                    {
                        result.EndpointCoincidentCount++;
                    }
                }
                else
                {
                    if (AddCoincident(
                            model,
                            first,
                            endVertex))
                    {
                        result.EndpointCoincidentCount++;
                    }

                    if (AddCoincident(
                            model,
                            last,
                            startVertex))
                    {
                        result.EndpointCoincidentCount++;
                    }
                }
            }

            ISketchRelationManager relationManager =
                null;

            try
            {
                relationManager =
                    sketch.RelationManager;
            }
            catch
            {
            }

            int sameLengthType =
                (int)swConstraintType_e
                    .swConstraintType_SAMELENGTH;

            result.EqualRelationsBefore =
                CountRelationsByType(
                    relationManager,
                    sameLengthType);

            UiEqualAttempt equalAttempt =
                TryCreateEqualRelationWithUiCommand(
                    app,
                    model,
                    sketch,
                    constructionLines);

            result.EqualRelationsAfter =
                CountRelationsByType(
                    relationManager,
                    sameLengthType);

            result.EqualVerified =
                equalAttempt.Verified ||
                result.EqualRelationsAfter >=
                Math.Max(
                    1,
                    constructionLines.Count - 1);

            if (!result.EqualVerified)
            {
                EqualDimensionFallback fallback =
                    ApplyEqualLengthDimensionFallback(
                        model,
                        sketch,
                        constructionLines);

                result.FallbackMode =
                    fallback.Mode;

                result.FallbackVerified =
                    fallback.Verified;
            }

            model.ClearSelection2(true);

            try
            {
                model.EditRebuild3();
            }
            catch
            {
            }

            return result;
        }

        private static double GetMaximumPointTargetDistance(
            List<SketchPoint> fitPoints,
            List<double[]> targetPoints)
        {
            if (fitPoints == null ||
                targetPoints == null ||
                fitPoints.Count !=
                targetPoints.Count)
            {
                return double.MaxValue;
            }

            double max =
                0.0;

            for (int i = 0;
                 i < fitPoints.Count;
                 i++)
            {
                SketchPoint point =
                    fitPoints[i];

                double[] target =
                    targetPoints[i];

                double dx =
                    point.X - target[0];

                double dy =
                    point.Y - target[1];

                double dz =
                    point.Z - target[2];

                double distance =
                    Math.Sqrt(
                        dx * dx +
                        dy * dy +
                        dz * dz);

                max =
                    Math.Max(
                        max,
                        distance);
            }

            return max;
        }

        private static void TryRollbackMove(
            ISldWorks app,
            ModelDoc2 model,
            Sketch sketch,
            AutoSplineGeometry geometry,
            List<double[]> originalCoordinates,
            ExternalReferenceSnapshot oldReferences)
        {
            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE ROLLBACK BEGIN");

            if (model == null ||
                sketch == null ||
                geometry == null)
            {
                return;
            }

            try
            {
                BreakMoveBlockingRelations(
                    sketch,
                    geometry.FitPoints,
                    geometry.ConstructionLines);
            }
            catch
            {
            }

            try
            {
                MoveFitPoints(
                    geometry.FitPoints,
                    originalCoordinates);
            }
            catch
            {
            }

            if (oldReferences != null &&
                oldReferences.Edge != null)
            {
                foreach (SketchPoint point in geometry.FitPoints)
                {
                    model.ClearSelection2(true);

                    if (SelectComEntity(
                            point,
                            false) &&
                        SelectComEntity(
                            oldReferences.Edge,
                            true))
                    {
                        try
                        {
                            model.SketchAddConstraints(
                                "sgCOINCIDENT");
                        }
                        catch
                        {
                        }
                    }
                }
            }

            if (oldReferences != null &&
                oldReferences.StartVertex != null &&
                oldReferences.EndVertex != null &&
                geometry.FitPoints.Count >= 2)
            {
                AddCoincident(
                    model,
                    geometry.FitPoints[0],
                    oldReferences.StartVertex);

                AddCoincident(
                    model,
                    geometry.FitPoints[
                        geometry.FitPoints.Count - 1],
                    oldReferences.EndVertex);
            }

            try
            {
                TryCreateEqualRelationWithUiCommand(
                    app,
                    model,
                    sketch,
                    geometry.ConstructionLines);
            }
            catch
            {
            }

            try
            {
                model.EditRebuild3();
            }
            catch
            {
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] MOVE ROLLBACK END");
        }

        private static AutoSplineSelection ReadAutoSplineSelection(
            ModelDoc2 model)
        {
            AutoSplineSelection result =
                new AutoSplineSelection();

            if (model == null)
            {
                result.Error =
                    "Không có document.";

                return result;
            }

            SelectionMgr manager =
                model.SelectionManager
                as SelectionMgr;

            if (manager == null)
            {
                result.Error =
                    "Không đọc được Selection Manager.";

                return result;
            }

            int count =
                0;

            try
            {
                count =
                    manager.GetSelectedObjectCount2(
                        -1);
            }
            catch
            {
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] selectionCount=" +
                count);

            Edge selectedEdge =
                null;

            int edgeCount =
                0;

            SketchSpline selectedSpline =
                null;

            int splineCount =
                0;

            for (int i = 1;
                 i <= count;
                 i++)
            {
                object selectedObject =
                    null;

                int selectionType =
                    -1;

                try
                {
                    selectionType =
                        manager.GetSelectedObjectType3(
                            i,
                            -1);
                }
                catch
                {
                }

                try
                {
                    selectedObject =
                        manager.GetSelectedObject6(
                            i,
                            -1);
                }
                catch
                {
                }

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] SELECT[" +
                    i +
                    "] typeId=" +
                    selectionType +
                    " typeName=" +
                    GetSelectionTypeName(
                        selectionType) +
                    " runtime=" +
                    (selectedObject == null
                        ? "NULL"
                        : selectedObject
                            .GetType()
                            .FullName));

                Edge edge =
                    selectedObject as Edge;

                if (edge != null)
                {
                    edgeCount++;

                    if (selectedEdge == null)
                    {
                        selectedEdge =
                            edge;
                    }

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "EDGE ACCEPTED index=" +
                        i +
                        " via=direct-com-cast");

                    continue;
                }

                SketchSpline spline =
                    ResolveSelectedSketchSpline(
                        model,
                        selectedObject);

                if (spline != null)
                {
                    splineCount++;

                    if (selectedSpline == null)
                    {
                        selectedSpline =
                            spline;
                    }

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "SPLINE ACCEPTED index=" +
                        i +
                        ", key=" +
                        GetSketchSegmentKey(
                            spline as SketchSegment));

                    continue;
                }
            }

            result.Edge =
                selectedEdge;

            if (selectedEdge == null)
            {
                result.Error =
                    "Hãy chọn Edge Surface/Body.\\n\\n" +
                    "CREATE: chọn 1 Edge.\\n" +
                    "MOVE: Ctrl-select đúng 2 đối tượng: spline hiện hữu + Edge mới.";

                return result;
            }

            if (edgeCount != 1)
            {
                result.Error =
                    "AUTO SPLINE chỉ nhận đúng 1 Edge đích.";

                return result;
            }

            // ------------------------------------------------------------
            // EXPLICIT MOVE TRIGGER:
            // ONLY exactly two selected objects:
            //     1 SketchSpline + 1 Edge
            //
            // Merely editing a 3D Sketch is NOT a MOVE trigger anymore.
            // If no spline is selected, Edge-only always means CREATE.
            // ------------------------------------------------------------
            if (selectedSpline == null)
            {
                if (splineCount != 0)
                {
                    result.Error =
                        "Không xác định được spline đã chọn.";

                    return result;
                }

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "SELECTION MODE=CREATE reason=NO_SPLINE_SELECTED");

                return result;
            }

            if (count != 2 ||
                splineCount != 1)
            {
                result.Error =
                    "MOVE yêu cầu Ctrl-select đúng 2 đối tượng:\\n" +
                    "1) 1 spline hiện hữu\\n" +
                    "2) 1 Edge Surface mới\\n\\n" +
                    "Nếu chỉ muốn CREATE, hãy bỏ chọn spline và chỉ chọn Edge.";

                return result;
            }

            Sketch activeSketch =
                model.SketchManager.ActiveSketch
                as Sketch;

            if (activeSketch == null ||
                !TryIs3DSketch(
                    activeSketch))
            {
                result.Error =
                    "Đã chọn spline + Edge nên AUTO SPLINE hiểu là MOVE.\\n\\n" +
                    "Hãy Edit 3D Sketch chứa spline đó trước khi MOVE.";

                return result;
            }

            if (!SketchContainsSpline(
                    activeSketch,
                    selectedSpline))
            {
                result.Error =
                    "Spline đã chọn không thuộc 3D Sketch đang Edit.";

                return result;
            }

            Feature sketchFeature =
                FindOwningFeatureForSketch(
                    model,
                    activeSketch);

            if (sketchFeature == null)
            {
                result.Error =
                    "Không xác định được Feature của 3D Sketch chứa spline.";

                return result;
            }

            result.MoveSketch =
                activeSketch;

            result.MoveSketchFeature =
                sketchFeature;

            result.MoveSpline =
                selectedSpline;

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] " +
                "SELECTION MODE=MOVE reason=EXPLICIT_SPLINE_PLUS_EDGE" +
                ", splineKey=" +
                GetSketchSegmentKey(
                    selectedSpline as SketchSegment));

            return result;
        }

        private static SketchSpline ResolveSelectedSketchSpline(
            ModelDoc2 model,
            object selectedObject)
        {
            if (selectedObject == null)
                return null;

            SketchSpline direct =
                selectedObject as SketchSpline;

            if (direct != null)
                return direct;

            SketchSegment selectedSegment =
                selectedObject as SketchSegment;

            if (selectedSegment == null ||
                model == null)
            {
                return null;
            }

            Sketch activeSketch =
                model.SketchManager.ActiveSketch
                as Sketch;

            if (activeSketch == null ||
                !TryIs3DSketch(
                    activeSketch))
            {
                return null;
            }

            object[] segments =
                null;

            try
            {
                segments =
                    activeSketch.GetSketchSegments()
                    as object[];
            }
            catch
            {
            }

            if (segments == null)
                return null;

            string selectedKey =
                GetSketchSegmentKey(
                    selectedSegment);

            foreach (object obj in segments)
            {
                SketchSpline candidate =
                    obj as SketchSpline;

                if (candidate == null)
                    continue;

                if (IsSameComObject(
                        selectedObject,
                        candidate))
                {
                    return candidate;
                }

                SketchSegment candidateSegment =
                    candidate as SketchSegment;

                if (candidateSegment == null)
                    continue;

                string candidateKey =
                    GetSketchSegmentKey(
                        candidateSegment);

                if (!string.IsNullOrWhiteSpace(
                        selectedKey) &&
                    string.Equals(
                        selectedKey,
                        candidateKey,
                        StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool SketchContainsSpline(
            Sketch sketch,
            SketchSpline spline)
        {
            if (sketch == null ||
                spline == null)
            {
                return false;
            }

            object[] segments =
                null;

            try
            {
                segments =
                    sketch.GetSketchSegments()
                    as object[];
            }
            catch
            {
            }

            if (segments == null)
                return false;

            SketchSegment sourceSegment =
                spline as SketchSegment;

            string sourceKey =
                GetSketchSegmentKey(
                    sourceSegment);

            foreach (object obj in segments)
            {
                SketchSpline candidate =
                    obj as SketchSpline;

                if (candidate == null)
                    continue;

                if (IsSameComObject(
                        candidate,
                        spline))
                {
                    return true;
                }

                SketchSegment candidateSegment =
                    candidate as SketchSegment;

                string candidateKey =
                    GetSketchSegmentKey(
                        candidateSegment);

                if (!string.IsNullOrWhiteSpace(
                        sourceKey) &&
                    string.Equals(
                        sourceKey,
                        candidateKey,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static AutoSplineGeometry BuildMoveGeometryForSelectedSpline(
            Sketch sketch,
            SketchSpline selectedSpline)
        {
            if (sketch == null ||
                selectedSpline == null)
            {
                return null;
            }

            if (!SketchContainsSpline(
                    sketch,
                    selectedSpline))
            {
                return null;
            }

            List<SketchPoint> fitPoints =
                GetSplineFitPoints(
                    selectedSpline);

            if (fitPoints.Count <
                MinimumPointCount)
            {
                return null;
            }

            List<SketchSegment> constructionCandidates =
                new List<SketchSegment>();

            object[] segments =
                null;

            try
            {
                segments =
                    sketch.GetSketchSegments()
                    as object[];
            }
            catch
            {
            }

            if (segments != null)
            {
                foreach (object obj in segments)
                {
                    SketchSegment segment =
                        obj as SketchSegment;

                    SketchLine line =
                        obj as SketchLine;

                    if (segment == null ||
                        line == null)
                    {
                        continue;
                    }

                    bool construction =
                        false;

                    try
                    {
                        construction =
                            segment.ConstructionGeometry;
                    }
                    catch
                    {
                    }

                    if (construction)
                    {
                        constructionCandidates.Add(
                            segment);
                    }
                }
            }

            List<SketchSegment> helperLines =
                FindOrderedConstructionLines(
                    fitPoints,
                    constructionCandidates);

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] " +
                "SELECTED SPLINE GEOMETRY points=" +
                fitPoints.Count +
                ", matchedHelperLines=" +
                helperLines.Count +
                ", splineKey=" +
                GetSketchSegmentKey(
                    selectedSpline as SketchSegment));

            return
                new AutoSplineGeometry
                {
                    Spline =
                        selectedSpline,

                    FitPoints =
                        fitPoints,

                    ConstructionLines =
                        helperLines
                };
        }

        private static Feature FindOwningFeatureForSketch(
            ModelDoc2 model,
            Sketch targetSketch)
        {
            if (model == null ||
                targetSketch == null)
            {
                return null;
            }

            Feature feature =
                null;

            try
            {
                feature =
                    model.FirstFeature()
                    as Feature;
            }
            catch
            {
            }

            while (feature != null)
            {
                Feature found =
                    FindOwningFeatureForSketchRecursive(
                        feature,
                        targetSketch);

                if (found != null)
                    return found;

                try
                {
                    feature =
                        feature.GetNextFeature()
                        as Feature;
                }
                catch
                {
                    feature =
                        null;
                }
            }

            return null;
        }

        private static Feature FindOwningFeatureForSketchRecursive(
            Feature feature,
            Sketch targetSketch)
        {
            if (feature == null ||
                targetSketch == null)
            {
                return null;
            }

            try
            {
                Sketch candidate =
                    feature.GetSpecificFeature2()
                    as Sketch;

                if (candidate != null &&
                    IsSameComObject(
                        candidate,
                        targetSketch))
                {
                    return feature;
                }
            }
            catch
            {
            }

            Feature subFeature =
                null;

            try
            {
                subFeature =
                    feature.GetFirstSubFeature()
                    as Feature;
            }
            catch
            {
            }

            while (subFeature != null)
            {
                Feature found =
                    FindOwningFeatureForSketchRecursive(
                        subFeature,
                        targetSketch);

                if (found != null)
                    return found;

                try
                {
                    subFeature =
                        subFeature.GetNextSubFeature()
                        as Feature;
                }
                catch
                {
                    subFeature =
                        null;
                }
            }

            return null;
        }

        private static bool IsSameComObject(
            object first,
            object second)
        {
            if (first == null ||
                second == null)
            {
                return false;
            }

            if (ReferenceEquals(
                    first,
                    second))
            {
                return true;
            }

            IntPtr firstUnknown =
                IntPtr.Zero;

            IntPtr secondUnknown =
                IntPtr.Zero;

            try
            {
                firstUnknown =
                    Marshal.GetIUnknownForObject(
                        first);

                secondUnknown =
                    Marshal.GetIUnknownForObject(
                        second);

                return
                    firstUnknown ==
                    secondUnknown;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (firstUnknown !=
                    IntPtr.Zero)
                {
                    Marshal.Release(
                        firstUnknown);
                }

                if (secondUnknown !=
                    IntPtr.Zero)
                {
                    Marshal.Release(
                        secondUnknown);
                }
            }
        }

        private static string SafeFeatureName(
            Feature feature)
        {
            if (feature == null)
                return "";

            try
            {
                return
                    feature.Name
                    ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static object CreateSplineCompat(SketchManager sketchManager, double[] points)
        {
            if (sketchManager == null || points == null || points.Length < 12)
                return null;

            // SolidWorks API:
            // SketchSegment CreateSpline(object PointData)
            // SketchSegment CreateSpline2(object PointData, bool Simp)
            try
            {
                return sketchManager.CreateSpline(points);
            }
            catch (Exception ex1)
            {
                Debug.WriteLine("[EDGE EQUAL SPLINE] CreateSpline failed: " + ex1.Message);

                try
                {
                    return sketchManager.CreateSpline2(points, false);
                }
                catch (Exception ex2)
                {
                    Debug.WriteLine("[EDGE EQUAL SPLINE] CreateSpline2 failed: " + ex2.Message);
                    return null;
                }
            }
        }

        private static bool TryIs3DSketch(
            Sketch sketch)
        {
            if (sketch == null)
                return false;

            try
            {
                return sketch.Is3D();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "Sketch.Is3D failed: " +
                    ex.Message);

                return false;
            }
        }

        private static bool TryGetSelectedEdge(
            ModelDoc2 model,
            out Edge edge,
            out string error)
        {
            edge = null;
            error = "";

            SelectionMgr selectionManager =
                model?.SelectionManager as SelectionMgr;

            if (selectionManager == null)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] selectionManager=NULL");

                error =
                    "Không đọc được Selection Manager.";
                return false;
            }

            int count = 0;

            try
            {
                count =
                    selectionManager
                        .GetSelectedObjectCount2(-1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "GetSelectedObjectCount2 failed: " +
                    ex.Message);
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] selectionCount=" +
                count);

            if (count <= 0)
            {
                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] " +
                    "NO SELECTION — stopped before curve read.");

                error =
                    "SolidWorks không còn selection khi AUTO SPLINE chạy.\n\n" +
                    "Hãy click trực tiếp 1 Edge của surface rồi bấm AUTO SPLINE.";
                return false;
            }

            for (int i = 1;
                 i <= count;
                 i++)
            {
                int selectionType =
                    -1;

                object selectedObject =
                    null;

                try
                {
                    selectionType =
                        selectionManager
                            .GetSelectedObjectType3(
                                i,
                                -1);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "GetSelectedObjectType3[" +
                        i +
                        "] failed: " +
                        ex.Message);
                }

                try
                {
                    selectedObject =
                        selectionManager
                            .GetSelectedObject6(
                                i,
                                -1);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "GetSelectedObject6[" +
                        i +
                        "] failed: " +
                        ex.Message);
                }

                string runtimeType =
                    selectedObject == null
                        ? "NULL"
                        : selectedObject
                            .GetType()
                            .FullName;

                Debug.WriteLine(
                    "[EDGE EQUAL SPLINE] SELECT[" +
                    i +
                    "] typeId=" +
                    selectionType +
                    " typeName=" +
                    GetSelectionTypeName(
                        selectionType) +
                    " runtime=" +
                    runtimeType);

                // Quan trọng:
                // thử cast COM object trực tiếp sang Edge trước.
                // Không phụ thuộc hoàn toàn vào typeId do SolidWorks trả về.
                Edge directEdge =
                    selectedObject as Edge;

                if (directEdge != null)
                {
                    edge =
                        directEdge;

                    Debug.WriteLine(
                        "[EDGE EQUAL SPLINE] " +
                        "EDGE ACCEPTED index=" +
                        i +
                        " via=direct-com-cast");

                    return true;
                }

                // Fallback chuẩn nếu SolidWorks báo đúng swSelEDGES.
                if (selectionType ==
                    (int)swSelectType_e.swSelEDGES)
                {
                    try
                    {
                        edge =
                            selectionManager
                                .GetSelectedObject6(
                                    i,
                                    -1)
                            as Edge;
                    }
                    catch
                    {
                        edge =
                            null;
                    }

                    if (edge != null)
                    {
                        Debug.WriteLine(
                            "[EDGE EQUAL SPLINE] " +
                            "EDGE ACCEPTED index=" +
                            i +
                            " via=swSelEDGES");

                        return true;
                    }
                }
            }

            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] " +
                "NO EDGE FOUND in current selection.");

            error =
                "Selection hiện tại không phải model Edge.\n\n" +
                "Hãy click đúng đường biên Edge của surface/body,\n" +
                "không chọn Face, Sketch Segment hoặc Point.";

            return false;
        }

        private static string GetSelectionTypeName(
            int type)
        {
            try
            {
                if (Enum.IsDefined(
                        typeof(swSelectType_e),
                        type))
                {
                    return
                        ((swSelectType_e)type)
                        .ToString();
                }
            }
            catch
            {
            }

            return "UNKNOWN";
        }

        private static string SafeGetTitle(
            ModelDoc2 model)
        {
            if (model == null)
                return "";

            try
            {
                return
                    model.GetTitle()
                    ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static double Distance(
            double[] a,
            double[] b)
        {
            if (!IsPoint(a) ||
                !IsPoint(b))
            {
                return double.MaxValue;
            }

            double dx =
                a[0] -
                b[0];

            double dy =
                a[1] -
                b[1];

            double dz =
                a[2] -
                b[2];

            return
                Math.Sqrt(
                    dx * dx +
                    dy * dy +
                    dz * dz);
        }

        private static double DistancePointToSegment3D(
            double[] point,
            double[] a,
            double[] b)
        {
            if (!IsPoint(point) || !IsPoint(a) || !IsPoint(b))
                return double.MaxValue;

            double abx = b[0] - a[0];
            double aby = b[1] - a[1];
            double abz = b[2] - a[2];

            double apx = point[0] - a[0];
            double apy = point[1] - a[1];
            double apz = point[2] - a[2];

            double denom = abx * abx + aby * aby + abz * abz;

            if (denom <= 1.0e-24)
                return Math.Sqrt(apx * apx + apy * apy + apz * apz);

            double t = (apx * abx + apy * aby + apz * abz) / denom;
            t = Math.Max(0.0, Math.Min(1.0, t));

            double cx = a[0] + t * abx;
            double cy = a[1] + t * aby;
            double cz = a[2] + t * abz;

            double dx = point[0] - cx;
            double dy = point[1] - cy;
            double dz = point[2] - cz;

            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static double[] FlattenPoints(List<double[]> points)
        {
            if (points == null || points.Count == 0)
                return new double[0];

            double[] data = new double[points.Count * 3];
            int index = 0;

            foreach (double[] point in points)
            {
                if (!IsPoint(point))
                    throw new InvalidOperationException("Point data không hợp lệ.");

                data[index++] = point[0];
                data[index++] = point[1];
                data[index++] = point[2];
            }

            return data;
        }

        private static bool IsPoint(double[] p)
        {
            return p != null &&
                   p.Length >= 3 &&
                   IsFinite(p[0]) &&
                   IsFinite(p[1]) &&
                   IsFinite(p[2]);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void DebugSampling(
            SamplingResult sampling,
            double totalLength,
            double toleranceMm)
        {
            double stepMm =
                totalLength / Math.Max(1, sampling.SegmentCount) * 1000.0;

            Debug.WriteLine("[EDGE EQUAL SPLINE] RESULT");
            Debug.WriteLine(
                "[EDGE EQUAL SPLINE] points=" + sampling.Points.Count +
                ", segments=" + sampling.SegmentCount +
                ", equalArcStepMm=" +
                stepMm.ToString("0.######", CultureInfo.InvariantCulture) +
                ", maxDeviationMm=" +
                (sampling.MaximumDeviation * 1000.0)
                    .ToString("0.######", CultureInfo.InvariantCulture) +
                ", toleranceMm=" +
                toleranceMm.ToString("0.######", CultureInfo.InvariantCulture));
        }

        private void ShowMessage(string text, swMessageBoxIcon_e icon)
        {
            try
            {
                swApp.SendMsgToUser2(
                    text,
                    (int)icon,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
            catch
            {
                MessageBox.Show(
                    text,
                    "AUTO EQUAL SPLINE",
                    MessageBoxButtons.OK,
                    icon == swMessageBoxIcon_e.swMbStop
                        ? MessageBoxIcon.Error
                        : icon == swMessageBoxIcon_e.swMbWarning
                            ? MessageBoxIcon.Warning
                            : MessageBoxIcon.Information);
            }
        }

        private sealed class SelectiveBlockingConstructionCleanupResult
        {
            public int Discovered;
            public int DeletedConstructionSplines;
            public int PreservedStraightLines;
            public int PreservedOtherConstruction;
            public int Missing;
        }

        private sealed class PreservedConstructionLineSnapshot
        {
            public string OriginalKey { get; set; }
            public double StartX { get; set; }
            public double StartY { get; set; }
            public double StartZ { get; set; }
            public double EndX { get; set; }
            public double EndY { get; set; }
            public double EndZ { get; set; }
        }

        private sealed class AutoSplineSelection
        {
            public Edge Edge { get; set; }

            public Sketch MoveSketch { get; set; }

            public Feature MoveSketchFeature { get; set; }

            public SketchSpline MoveSpline { get; set; }

            public string Error { get; set; } =
                "";
        }

        private sealed class AutoSplineGeometry
        {
            public SketchSpline Spline { get; set; }

            public List<SketchPoint> FitPoints { get; set; } =
                new List<SketchPoint>();

            public List<SketchSegment> ConstructionLines { get; set; } =
                new List<SketchSegment>();
        }

        private sealed class RelationBreakResult
        {
            public int ExternalRelationsDeleted { get; set; }

            public int EqualRelationsDeleted { get; set; }

            public int BrokenRelationsDeleted { get; set; }
        }

        private sealed class ExternalReferenceSnapshot
        {
            public Edge Edge { get; set; }

            public Vertex StartVertex { get; set; }

            public Vertex EndVertex { get; set; }
        }

        private sealed class ExistingRelationResult
        {
            public int PointOnEdgeCount { get; set; }

            public int EndpointCoincidentCount { get; set; }

            public int EqualRelationsBefore { get; set; }

            public int EqualRelationsAfter { get; set; }

            public bool EqualVerified { get; set; }

            public string FallbackMode { get; set; } =
                "NONE";

            public bool FallbackVerified { get; set; }
        }

        private sealed class DirectMoveProbeResult
        {
            public bool Success { get; set; }
            public bool Selected { get; set; }
            public bool CallReturned { get; set; }
            public bool RebuildOk { get; set; }
            public bool SameSpline { get; set; }
            public bool RestoreAttempted { get; set; }
            public double BeforeTargetError { get; set; }
            public double ActualMovement { get; set; }
            public double AfterTargetError { get; set; }
            public double RestoreError { get; set; }
            public string Error { get; set; } = "";
        }

        private sealed class AdaptiveSameSplineTargetResult
        {
            public bool Attempted { get; set; }
            public bool Improved { get; set; }
            public int Evaluations { get; set; }
            public double BeforeDeviation { get; set; } = double.MaxValue;
            public double AfterDeviation { get; set; } = double.MaxValue;
            public List<double[]> Targets { get; set; } = new List<double[]>();
            public List<double> Fractions { get; set; } = new List<double>();
            public DirectSplineCandidateResult Candidate { get; set; } =
                new DirectSplineCandidateResult();
            public string Error { get; set; } = "";
        }


        private sealed class CoherentMorphResult
        {
            public bool Success { get; set; }
            public bool SameSpline { get; set; }
            public bool TwistGuardPassed { get; set; }
            public int TotalStages { get; set; }
            public int CompletedStages { get; set; }
            public int MoveCallCount { get; set; }
            public double MaximumPointError { get; set; }
            public string Error { get; set; } = "";
        }

        private sealed class StagePointMoveResult
        {
            public bool Success { get; set; }
            public int MoveCallCount { get; set; }
            public double MaximumPointError { get; set; }
            public string Error { get; set; } = "";
        }

        private sealed class MorphSanity
        {
            public bool Valid { get; set; }
            public double LengthToPointPolylineRatio { get; set; } = double.MaxValue;
            public double MaximumBacktrackFraction { get; set; } = double.MaxValue;
        }

        private sealed class DirectSplineMoveResult
        {
            public bool Success { get; set; }
            public int Passes { get; set; }
            public int MoveCallCount { get; set; }
            public double MaximumTargetError { get; set; }
            public string Error { get; set; } = "";
        }

        private sealed class DirectSplineCandidateResult
        {
            public bool Success { get; set; }
            public bool SameSpline { get; set; }
            public int PointCount { get; set; }
            public double MaxTargetError { get; set; }
            public double ActualSplineDeviation { get; set; }
            public string Error { get; set; } = "";
        }

        private sealed class InPlaceRelationReleaseResult
        {
            public int RelationsDeleted { get; set; }

            public int FitPointRelationsDeleted { get; set; }

            public int SplineRelationsDeleted { get; set; }

            public int HelperRelationsDeleted { get; set; }

            public int EqualRelationsDeleted { get; set; }

            // V35.4: one-hop construction segments that are connected to the
            // selected MOVE network but are neither the SAME spline nor the
            // recognized disposable helper chain.  The caller deletes these
            // actual construction entities before attempting any fit-point move.
            public List<string> BlockingConstructionSegmentKeys { get; } =
                new List<string>();
        }

        private sealed class InPlacePointMoveResult
        {
            public bool Success { get; set; }

            public int MovedCount { get; set; }

            public double MaximumError { get; set; }
        }

        private sealed class HelperChainAdaptResult
        {
            public bool Success { get; set; }

            public int ReusedCount { get; set; }

            public int CreatedCount { get; set; }

            public int DeletedCount { get; set; }

            public int DeleteFailedCount { get; set; }

            public List<SketchSegment> Lines { get; set; } =
                new List<SketchSegment>();
        }

        private sealed class EqualRelationResult
        {
            public int PointOnEdgeCount { get; set; }

            public int EndpointCoincidentCount { get; set; }

            public int ConstructionLineCount { get; set; }

            public List<SketchSegment> CreatedConstructionLines { get; set; } =
                new List<SketchSegment>();

            public int EqualRelationObjectCount { get; set; }

            public int EqualConstrainedLineCount { get; set; }

            public bool EqualVerified { get; set; }

            public int EqualRelationCount { get; set; }

            public bool UiEqualCommandEnabled { get; set; }

            public bool UiEqualCommandRan { get; set; }

            public int UiEqualRelationsAdded { get; set; }

            public string EqualFallbackMode { get; set; } = "NONE";

            public bool FallbackVerified { get; set; }

            public int DimensionCount { get; set; }

            public int LinkedEquationCount { get; set; }

            public int FixedValueDimensionCount { get; set; }
        }

        private sealed class UiEqualAttempt
        {
            public bool CommandEnabled { get; set; }

            public bool CommandRan { get; set; }

            public int RelationsAdded { get; set; }

            public bool Verified { get; set; }
        }

        private sealed class EqualDimensionFallback
        {
            public string Mode { get; set; }

            public int DimensionCount { get; set; }

            public int LinkedEquationCount { get; set; }

            public int FixedValueDimensionCount { get; set; }

            public bool Verified { get; set; }
        }

        private sealed class SpacingStats
        {
            public double Min { get; set; }

            public double Max { get; set; }
        }

        private sealed class VerifiedRelationResult
        {
            public bool Allowed { get; set; }

            public bool AddReturnedRelation { get; set; }

            public bool ManagerFoundRelation { get; set; }

            public bool Verified { get; set; }

            public int RelationType { get; set; }

            public int EntityCount { get; set; }

            public string Error { get; set; }
        }

        private sealed class NativeMergeCleanupResult
        {
            public bool Success { get; set; }

            public string Error { get; set; }

            public int RelationsDeleted { get; set; }

            public bool AnchorConsumedBySplinePoint { get; set; }

            public bool AnchorDeleted { get; set; }
        }

        private sealed class SameSplineEdgeFitResult
        {
            public bool Success { get; set; }

            public string Error { get; set; } =
                "";

            public int PointOnEdgeCallCount { get; set; }

            public int EndpointRelationCallCount { get; set; }

            public List<SketchSegment> HelperLines { get; set; } =
                new List<SketchSegment>();

            public bool EqualVerified { get; set; }

            public int EqualConstrainedLineCount { get; set; }

            public bool SameSplineKey { get; set; }

            public bool SameSplineCom { get; set; }

            public double NudgeMaximumTargetError { get; set; }

            public double MaxPointToEdgeError { get; set; }

            public double MaxTargetError { get; set; }

            public double EndpointVertexError { get; set; }

            public double ActualSplineDeviation { get; set; }
        }

        private sealed class AnchorFitResult
        {
            public bool Success { get; set; }

            public string Error { get; set; }

            public int FitPointCount { get; set; }

            public int CoincidentAllowedCount { get; set; }

            public int CoincidentAddReturnCount { get; set; }

            public int CoincidentRelationCount { get; set; }

            public int DeletedAnchorCount { get; set; }

            public double MaxErrorBeforeCleanup { get; set; }

            public double MaxErrorAfterCleanup { get; set; }
        }

        private sealed class CreatePointPlan
        {
            public int PointCount { get; set; }

            public int TangentSampleCount { get; set; }

            public double TotalTurnDeg { get; set; }

            public double MaxLocalTurnDeg { get; set; }
        }

        private sealed class SamplingResult
        {
            public int SegmentCount { get; set; }
            public List<double[]> Points { get; set; }
            public List<double> Parameters { get; set; }
            public double MaximumDeviation { get; set; }
            public bool MeetsTolerance { get; set; }
        }
    }
}
