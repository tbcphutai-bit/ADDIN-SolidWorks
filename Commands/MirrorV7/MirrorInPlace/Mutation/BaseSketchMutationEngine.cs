using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    public static class MirrorProfileGeometryV7
    {
        public static double CircleError(double centreDistance, double sourceRadius, double targetRadius)
        {
            if (double.IsNaN(centreDistance) || double.IsInfinity(centreDistance) || centreDistance < 0 ||
                double.IsNaN(sourceRadius) || double.IsInfinity(sourceRadius) || sourceRadius <= 0 ||
                double.IsNaN(targetRadius) || double.IsInfinity(targetRadius) || targetRadius <= 0)
                return double.MaxValue;
            return Math.Max(centreDistance, Math.Abs(sourceRadius - targetRadius));
        }
    }
    public sealed class BaseMutationResultV7
    {
        public string BaseFeatureName { get; internal set; }
        public string SketchFeatureName { get; internal set; }
        public int PointCount { get; internal set; }
        public int MovingPointCount { get; internal set; }
        public int SetCoordsRejectedCount { get; internal set; }
        public string Strategy { get; internal set; }
        public double MaximumPointErrorMetres { get; internal set; }
        public bool SketchVerified { get; internal set; }
        public bool BaseGeometryVerified { get; internal set; }
        public bool Saved { get; internal set; }
    }

    public sealed class DrivingSketchReflectionV7
    {
        public Sketch Sketch { get; internal set; }
        public List<SketchSegment> SourceSegments { get; internal set; }
        public List<SketchSegment> ReflectedSegments { get; internal set; }
    }

    public static class BaseSketchMutationEngineV7
    {
        private const double PointTolerance = 1e-7;

        internal sealed class DrivingSnapshot
        {
            internal Feature Feature;
            internal readonly List<SketchPointTargetV7> Points = new List<SketchPointTargetV7>();
            internal double[] Frame;
            internal double Area;
            internal double[][][] FaceBoundaries;
        }

        internal static DrivingSnapshot CaptureDrivingSnapshot(MirrorInPlaceExecutionContextV7 context,
            MirrorV7FeatureNode consumer)
        {
            var node = FindAbsorbedProfile(context.WorkingGraph, consumer);
            if (node == null) throw new InvalidOperationException("Driving sketch unavailable: " + consumer.Name);
            var sketch = node.Feature.GetSpecificFeature2() as Sketch;
            if (sketch == null || sketch.Is3D()) throw new InvalidOperationException("Driving sketch requires a 2D profile: " + node.Name);
            var frame = (MathTransform)sketch.ModelToSketchTransform;
            var inverse = frame.IInverse();
            var math = (IMathUtility)context.SwApp.GetMathUtility();
            var snapshot = new DrivingSnapshot { Feature = node.Feature, Frame = ((double[])frame.ArrayData).ToArray() };
            foreach (SketchPoint point in ((object[])sketch.GetSketchPoints2() ?? new object[0]))
            {
                double[] local = Point(point);
                double[] global = SketchMutationMathV7.Transform(math, inverse, local);
                var reference = PersistentReferenceServiceV7.Capture(context.WorkingDocument, point, node.Name, "SEQUENCE_POINT");
                if (reference == null) throw new InvalidOperationException("Cannot snapshot sketch point identity: " + node.Name);
                snapshot.Points.Add(new SketchPointTargetV7 { Reference = reference, BeforeSketch = local,
                    BeforeModel = global, TargetModel = context.Reflection.ReflectPoint(global) });
            }
            int kind = 0;
            Face2 face = sketch.GetReferenceEntity(ref kind) as Face2;
            if (face != null)
            {
                snapshot.Area = face.GetArea();
                snapshot.FaceBoundaries = ((object[])face.GetEdges() ?? new object[0]).Cast<Edge>().Select(edge =>
                {
                    var curve = (Curve)edge.GetCurve();
                    var parameters = (CurveParamData)edge.GetCurveParams3();
                    return Enumerable.Range(0, 9).Select(i => context.Reflection.ReflectPoint(
                        ((double[])curve.Evaluate2(parameters.UMinValue + (parameters.UMaxValue - parameters.UMinValue) * i / 8.0, 0)).Take(3).ToArray())).ToArray();
                }).ToArray();
            }
            return snapshot;
        }

        internal static string ApplyDrivingSnapshot(MirrorInPlaceExecutionContextV7 context,
            MirrorV7FeatureNode consumer, DrivingSnapshot snapshot, out string name, out int moving,
            out DrivingSketchReflectionV7 result)
        {
            var model = context.WorkingDocument;
            var sketch = (Sketch)snapshot.Feature.GetSpecificFeature2();
            var math = (IMathUtility)context.SwApp.GetMathUtility();
            var frame = (MathTransform)sketch.ModelToSketchTransform;
            name = snapshot.Feature.Name;
            bool outside = snapshot.Points.Any(p => Math.Abs(SketchMutationMathV7.Transform(math, frame, p.TargetModel)[2]) > PointTolerance);
            if (outside)
            {
                if (snapshot.FaceBoundaries == null)
                    throw new InvalidOperationException("Reflected sketch support needs a reference-plane handler: " + name);
                var support = (Face2)RollbackReplayEngineV7.FindFace(model, snapshot.Area, snapshot.FaceBoundaries);
                model.ClearSelection2(true);
                if (!snapshot.Feature.Select2(false, 0) || !((Entity)support).Select4(true, null) || !model.ChangeSketchPlane())
                    throw new InvalidOperationException("Cannot re-parent sketch to reflected support: " + name);
                sketch = (Sketch)snapshot.Feature.GetSpecificFeature2();
                frame = (MathTransform)sketch.ModelToSketchTransform;
                MirrorV7Diagnostics.Log("[INPLACE][SKETCH_SUPPORT_REBOUND] sketch=" + name);
            }
            foreach (var point in snapshot.Points)
            {
                point.TargetSketch = SketchMutationMathV7.Transform(math, frame, point.TargetModel);
                if (Math.Abs(point.TargetSketch[2]) > PointTolerance)
                    throw new InvalidOperationException("Mapped support does not contain reflected sketch: " + name);
            }
            var currentFrame = (double[])frame.ArrayData;
            bool originalFrame = currentFrame.Length == snapshot.Frame.Length &&
                currentFrame.Select((v, i) => Math.Abs(v - snapshot.Frame[i]) < 1e-10).All(x => x);
            bool originalPoints = snapshot.Points.All(p =>
            {
                int state;
                var point = PersistentReferenceServiceV7.Resolve(model, p.Reference, out state) as SketchPoint;
                return point != null && SketchMutationMathV7.Distance(Point(point), p.BeforeSketch) < PointTolerance;
            });
            if (originalFrame && originalPoints)
                return ReflectDrivingSketchForFeature(context, consumer, out name, out moving, out result);
            // The upstream body can move the support. Apply absolute source targets instead
            // of reflecting the already-moved profile a second time.
            result = new DrivingSketchReflectionV7 { Sketch = sketch,
                SourceSegments = ((object[])sketch.GetSketchSegments() ?? new object[0]).Cast<SketchSegment>().Where(s => !s.ConstructionGeometry).ToList(),
                ReflectedSegments = new List<SketchSegment>() };
            moving = 0;
            model.ClearSelection2(true);
            if (!snapshot.Feature.Select2(false, 0)) throw new InvalidOperationException("Cannot select driving sketch: " + name);
            model.EditSketch();
            try
            {
                foreach (var target in snapshot.Points)
                {
                    int state;
                    var point = PersistentReferenceServiceV7.Resolve(model, target.Reference, out state) as SketchPoint;
                    if (point == null) throw new InvalidOperationException("Driving point identity lost: " + name);
                    if (SketchMutationMathV7.Distance(Point(point), target.TargetSketch) <= PointTolerance) continue;
                    moving++;
                    if (!point.SetCoords(target.TargetSketch[0], target.TargetSketch[1], target.TargetSketch[2]))
                        throw new InvalidOperationException("Sketch constraints reject reflected coordinates: " + name);
                }
            }
            finally { model.SketchManager.InsertSketch(true); }
            SingleSketchMutationVerifierV7.VerifyPoints(model, sketch, math, snapshot.Points);
            return "AbsoluteSourceTargetsWithSupportMapping";
        }

        private sealed class SketchBaseline
        {
            public int Points, Segments, Relations, Dimensions;
            public List<double> SegmentLengths = new List<double>();
            public List<double> DimensionValues = new List<double>();
        }

        public static BaseMutationResultV7 Execute(MirrorInPlaceExecutionContextV7 context)
        {
            if (context == null || context.WorkingDocument == null || context.WorkingGraph == null ||
                context.BasePrescription == null) throw new ArgumentNullException("context");
            if (!context.BasePrescription.SupportedBaseType)
                throw new InvalidOperationException("Base feature is not enabled for mutation: " + context.BasePrescription.FeatureType);

            ModelDoc2 model = context.WorkingDocument;
            MirrorV7FeatureNode baseNode = context.WorkingGraph.FindByName(context.BasePrescription.FeatureName);
            if (baseNode == null) throw new InvalidOperationException("Cannot resolve Base Feature in staging graph.");
            MirrorV7FeatureNode sketchNode = FindAbsorbedProfile(context.WorkingGraph, baseNode);
            if (sketchNode == null) throw new InvalidOperationException("Cannot identify the absorbed Base Feature profile sketch.");
            Sketch sketch = sketchNode.Feature == null ? null : sketchNode.Feature.GetSpecificFeature2() as Sketch;
            if (sketch == null || sketch.Is3D()) throw new InvalidOperationException("Base Feature profile must be a 2D sketch.");

            FeatureManager manager = model.FeatureManager;
            if (manager == null) throw new InvalidOperationException("FeatureManager unavailable.");
            if (!manager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, baseNode.Name))
                throw new InvalidOperationException("Cannot move rollback bar after Base Feature.");
            if (!model.EditRebuild3()) throw new InvalidOperationException("Base Feature cannot rebuild before mutation.");
            List<Body2> oracle = ReflectCurrentSolids(context);

            if (!manager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, baseNode.Name))
                throw new InvalidOperationException("Cannot move rollback bar before Base Feature.");
            SketchBaseline baseline = CaptureBaseline(sketch, sketchNode.Feature);
            IMathUtility math = context.SwApp.GetMathUtility() as IMathUtility;
            List<SketchPointTargetV7> targets = SingleSketchTargetBuilderV7.Capture(
                model, sketch, math, context.Reflection, sketchNode.Name);
            int moving = targets.Count(x => SketchMutationMathV7.Distance(x.BeforeSketch, x.TargetSketch) >
                SketchMutationMathV7.PositionToleranceMetres);
            if (moving == 0)
                return CompleteInvariantBaseSketch(context, manager, baseNode, sketchNode, sketch,
                    baseline, targets, oracle);

            bool entered = false;
            bool oldAutoSolve = model.SketchManager.AutoSolve;
            bool oldAddToDb = model.SketchManager.AddToDB;
            int rejected = 0;
            string strategy = null;
            Exception frameFailure = null;
            MathTransform originalSketchFrame = sketch.ModelToSketchTransform as MathTransform;
            List<SketchSegment> reflectedProfile;
            if (TryCreateConstructionBackedReflectedProfile(model, sketchNode.Feature, sketch, math,
                context.Reflection, out reflectedProfile))
            {
                strategy = "ConstructionBackedExplicitGeometry";
            }
            int flippedAxis;
            if (strategy == null && TrySketchModifyFlip(model, sketchNode.Feature, sketch, math, targets, out flippedAxis))
                strategy = "SketchModifyFlipAxis" + flippedAxis;
            try
            {
                if (strategy == null)
                {
                    ApplyReflectedSketchFrame(sketch, math, context.Reflection, targets);
                    SingleSketchMutationVerifierV7.VerifyPoints(model, sketch, math, targets);
                    strategy = "ReflectedSketchCoordinateFrame";
                }
            }
            catch (Exception ex)
            {
                try { if (originalSketchFrame != null) sketch.ModelToSketchTransform = originalSketchFrame; } catch { }
                frameFailure = ex;
                MirrorV7Diagnostics.Log("[INPLACE][BASE_FRAME_REJECTED] sketch=\"" + sketchNode.Name +
                    "\" message=" + ex.Message + " fallback=BatchPointMutation");
            }

            if (strategy == null) try
            {
                model.ClearSelection2(true);
                if (!sketchNode.Feature.Select2(false, 0)) throw new InvalidOperationException("Cannot select Base Feature profile sketch.");
                model.EditSketch();
                entered = true;
                if (model.SketchManager.ActiveSketch == null ||
                    !SingleSketchTargetBuilderV7.SameComObject(model.SketchManager.ActiveSketch, sketch))
                    throw new InvalidOperationException("The wrong sketch entered edit mode.");
                model.SketchManager.AutoSolve = false;
                model.SketchManager.AddToDB = true;
                foreach (SketchPointTargetV7 target in targets)
                {
                    int state;
                    SketchPoint point = PersistentReferenceServiceV7.Resolve(model, target.Reference, out state) as SketchPoint;
                    if (point == null) throw new InvalidOperationException("Base sketch point identity lost before mutation. state=" + state);
                    if (!point.SetCoords(target.TargetSketch[0], target.TargetSketch[1], target.TargetSketch[2])) rejected++;
                }
                strategy = "BatchPointMutation";
            }
            catch (Exception pointFailure)
            {
                throw new InvalidOperationException("Both reflected sketch-frame and batch point mutation failed. frame=" +
                    (frameFailure == null ? "<none>" : frameFailure.Message) + " point=" + pointFailure.Message, pointFailure);
            }
            finally
            {
                try { model.SketchManager.AddToDB = oldAddToDb; } catch { }
                try { model.SketchManager.AutoSolve = oldAutoSolve; } catch { }
                if (entered) model.SketchManager.InsertSketch(true);
            }

            double maxError;
            if (strategy == "ConstructionBackedExplicitGeometry")
            {
                maxError = VerifyReflectedProfile(sketch, math, context.Reflection, reflectedProfile);
                VerifyConstructionBackedBaseline(sketch, sketchNode.Feature, baseline);
            }
            else
            {
                maxError = SingleSketchMutationVerifierV7.VerifyPoints(model, sketch, math, targets);
                VerifyBaseline(sketch, sketchNode.Feature, baseline);
            }
            if (!manager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, baseNode.Name))
                throw new InvalidOperationException("Cannot rebuild the reflected Base Feature.");
            if (!model.EditRebuild3()) throw new InvalidOperationException("Reflected Base Feature rebuild failed.");
            try
            {
                VerifyCurrentSolids(context, oracle);
            }
            catch (InvalidOperationException initialGeometryError)
            {
                if (!TryAdjustBaseFlangeDirections(context, baseNode.Feature, oracle))
                    throw new InvalidOperationException("Reflected sketch rebuilt, but no Base Flange direction/thickness-side combination matched the geometry oracle. " +
                        initialGeometryError.Message, initialGeometryError);
            }
            if (strategy == "ConstructionBackedExplicitGeometry")
            {
                maxError = Math.Max(maxError, VerifyReflectedProfile(sketch, math, context.Reflection, reflectedProfile));
                VerifyConstructionBackedBaseline(sketch, sketchNode.Feature, baseline);
            }
            else
            {
                maxError = Math.Max(maxError, SingleSketchMutationVerifierV7.VerifyPoints(model, sketch, math, targets));
                VerifyBaseline(sketch, sketchNode.Feature, baseline);
            }

            int saveErrors = 0, saveWarnings = 0;
            if (!model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref saveErrors, ref saveWarnings) || saveErrors != 0)
                throw new InvalidOperationException("Cannot save reflected staging Part. errors=" + saveErrors + " warnings=" + saveWarnings);
            MirrorV7Diagnostics.Log("[INPLACE][BASE_MUTATION_PASS] base=\"" + baseNode.Name +
                "\" sketch=\"" + sketchNode.Name + "\" points=" + targets.Count + " moving=" + moving +
                " strategy=" + strategy + " setCoordsRejected=" + rejected + " maxErrorSI=" + maxError +
                " geometryOracle=True saved=True");
            return new BaseMutationResultV7
            {
                BaseFeatureName = baseNode.Name,
                SketchFeatureName = sketchNode.Name,
                PointCount = targets.Count,
                MovingPointCount = moving,
                SetCoordsRejectedCount = rejected,
                Strategy = strategy,
                MaximumPointErrorMetres = maxError,
                SketchVerified = true,
                BaseGeometryVerified = true,
                Saved = true
            };
        }

        private static BaseMutationResultV7 CompleteInvariantBaseSketch(
            MirrorInPlaceExecutionContextV7 context,
            FeatureManager manager,
            MirrorV7FeatureNode baseNode,
            MirrorV7FeatureNode sketchNode,
            Sketch sketch,
            SketchBaseline baseline,
            List<SketchPointTargetV7> targets,
            List<Body2> oracle)
        {
            ModelDoc2 model = context.WorkingDocument;
            MirrorV7Diagnostics.Log("[INPLACE][BASE_SKETCH_INVARIANT] base=\"" + baseNode.Name +
                "\" sketch=\"" + sketchNode.Name +
                "\" action=ValidateBodyAndDirectionFlags");

            // A sketch that lies symmetrically about the selected plane is already its
            // own reflected profile.  This is not a failure: thickness side, offset and
            // extrusion direction still determine whether the resulting solid is the
            // required opposite-hand body.
            VerifyBaseline(sketch, sketchNode.Feature, baseline);
            if (!manager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, baseNode.Name))
                throw new InvalidOperationException("Cannot rebuild the invariant Base Feature.");
            if (!model.EditRebuild3())
                throw new InvalidOperationException("Invariant Base Feature rebuild failed.");

            string strategy = "InvariantSketchBodyOracle";
            try
            {
                VerifyCurrentSolids(context, oracle);
            }
            catch (InvalidOperationException initialGeometryError)
            {
                if (!TryAdjustBaseFlangeDirections(context, baseNode.Feature, oracle))
                    throw new InvalidOperationException(
                        "Base sketch is invariant, but no Base Flange direction/thickness-side combination matched the reflected geometry oracle. " +
                        initialGeometryError.Message, initialGeometryError);
                strategy = "InvariantSketchWithBaseDirectionMutation";
            }

            // The invariant path must never alter, duplicate or approximate sketch
            // entities.  It is accepted solely by exact sketch preservation plus the
            // reflected-body oracle.
            VerifyBaseline(sketch, sketchNode.Feature, baseline);
            int saveErrors = 0, saveWarnings = 0;
            if (!model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref saveErrors, ref saveWarnings) || saveErrors != 0)
                throw new InvalidOperationException("Cannot save reflected staging Part. errors=" + saveErrors +
                    " warnings=" + saveWarnings);

            MirrorV7Diagnostics.Log("[INPLACE][BASE_MUTATION_PASS] base=\"" + baseNode.Name +
                "\" sketch=\"" + sketchNode.Name + "\" points=" + targets.Count +
                " moving=0 strategy=" + strategy +
                " setCoordsRejected=0 maxErrorSI=0 geometryOracle=True saved=True");
            return new BaseMutationResultV7
            {
                BaseFeatureName = baseNode.Name,
                SketchFeatureName = sketchNode.Name,
                PointCount = targets.Count,
                MovingPointCount = 0,
                SetCoordsRejectedCount = 0,
                Strategy = strategy,
                MaximumPointErrorMetres = 0.0,
                SketchVerified = true,
                BaseGeometryVerified = true,
                Saved = true
            };
        }

        internal static string ReflectDrivingSketchForFeature(MirrorInPlaceExecutionContextV7 context,
            MirrorV7FeatureNode consumingFeature, out string sketchName, out int movingPointCount,
            out DrivingSketchReflectionV7 reflectionResult)
        {
            if (context == null || consumingFeature == null) throw new ArgumentNullException("context");
            MirrorV7FeatureNode sketchNode = FindAbsorbedProfile(context.WorkingGraph, consumingFeature);
            if (sketchNode == null)
                throw new InvalidOperationException("Cannot identify a direct parent sketch for feature '" +
                    consumingFeature.Name + "'.");
            Sketch sketch = sketchNode.Feature == null ? null : sketchNode.Feature.GetSpecificFeature2() as Sketch;
            if (sketch == null || sketch.Is3D())
                throw new InvalidOperationException("Driving profile must be a 2D sketch: " + sketchNode.Name);

            ModelDoc2 model = context.WorkingDocument;
            IMathUtility math = context.SwApp.GetMathUtility() as IMathUtility;
            SketchBaseline baseline = CaptureBaseline(sketch, sketchNode.Feature);
            object[] rawSegments = sketch.GetSketchSegments() as object[];
            List<SketchSegment> sourceSegments = rawSegments == null ? new List<SketchSegment>() : rawSegments
                .Select(x => x as SketchSegment).Where(x => x != null && !x.ConstructionGeometry).ToList();
            List<SketchPointTargetV7> targets = SingleSketchTargetBuilderV7.Capture(
                model, sketch, math, context.Reflection, sketchNode.Name);
            movingPointCount = targets.Count(x => SketchMutationMathV7.Distance(x.BeforeSketch, x.TargetSketch) >
                SketchMutationMathV7.PositionToleranceMetres);
            sketchName = sketchNode.Name;
            reflectionResult = new DrivingSketchReflectionV7
            {
                Sketch = sketch,
                SourceSegments = sourceSegments,
                ReflectedSegments = new List<SketchSegment>()
            };
            if (movingPointCount == 0)
            {
                VerifyBaseline(sketch, sketchNode.Feature, baseline);
                MirrorV7Diagnostics.Log("[INPLACE][DRIVING_SKETCH_PASS] consumer=\"" + consumingFeature.Name +
                    "\" sketch=\"" + sketchNode.Name +
                    "\" moving=0 strategy=InvariantDrivingSketch maxErrorSI=0");
                return "InvariantDrivingSketch";
            }

            int flippedAxis;
            if (TrySketchModifyFlip(model, sketchNode.Feature, sketch, math, targets, out flippedAxis))
            {
                VerifyBaseline(sketch, sketchNode.Feature, baseline);
                string flipStrategy = "SketchModifyFlipAxis" + flippedAxis;
                MirrorV7Diagnostics.Log("[INPLACE][DRIVING_SKETCH_PASS] consumer=\"" + consumingFeature.Name +
                    "\" sketch=\"" + sketchNode.Name + "\" moving=" + movingPointCount +
                    " strategy=" + flipStrategy + " maxErrorSI=0");
                return flipStrategy;
            }

            List<SketchSegment> reflected;
            if (!TryCreateConstructionBackedReflectedProfile(model, sketchNode.Feature, sketch, math,
                context.Reflection, out reflected))
                throw new InvalidOperationException("Driving sketch contains unsupported geometry or its reflected support plane is unresolved: " +
                    sketchNode.Name);
            reflectionResult.ReflectedSegments = reflected;
            double error = VerifyReflectedProfile(sketch, math, context.Reflection, reflected);
            VerifyConstructionBackedBaseline(sketch, sketchNode.Feature, baseline);
            MirrorV7Diagnostics.Log("[INPLACE][DRIVING_SKETCH_PASS] consumer=\"" + consumingFeature.Name +
                "\" sketch=\"" + sketchNode.Name + "\" moving=" + movingPointCount +
                " strategy=ConstructionBackedExplicitGeometry maxErrorSI=" + error);
            return "ConstructionBackedExplicitGeometry";
        }

        private static bool TryCreateConstructionBackedReflectedProfile(ModelDoc2 model, Feature sketchFeature,
            Sketch sketch, IMathUtility math, PartReflectionTransformV7 reflection,
            out List<SketchSegment> reflectedProfile)
        {
            reflectedProfile = new List<SketchSegment>();
            object[] raw = sketch.GetSketchSegments() as object[];
            List<SketchSegment> source = raw == null ? new List<SketchSegment>() : raw.Cast<object>()
                .Select(x => x as SketchSegment).Where(x => x != null && !x.ConstructionGeometry).ToList();
            if (source.Count == 0) return false;
            foreach (SketchSegment segment in source)
            {
                int type = segment.GetType();
                if (type != (int)swSketchSegments_e.swSketchLINE && type != (int)swSketchSegments_e.swSketchARC)
                {
                    MirrorV7Diagnostics.Log("[INPLACE][EXPLICIT_PROFILE_UNSUPPORTED_SEGMENT] sketch=\"" +
                        sketchFeature.Name + "\" segmentType=" + type);
                    return false;
                }
            }

            MathTransform modelToSketch = sketch.ModelToSketchTransform as MathTransform;
            MathTransform sketchToModel = modelToSketch == null ? null : modelToSketch.IInverse();
            if (sketchToModel == null) throw new InvalidOperationException("Base sketch transform unavailable.");
            double[] q0 = ReflectLocal(math, sketchToModel, modelToSketch, reflection, new double[] { 0, 0, 0 });
            double[] qx = ReflectLocal(math, sketchToModel, modelToSketch, reflection, new double[] { 1, 0, 0 });
            double[] qy = ReflectLocal(math, sketchToModel, modelToSketch, reflection, new double[] { 0, 1, 0 });
            if (Math.Abs(q0[2]) > PointTolerance || Math.Abs(qx[2]) > PointTolerance || Math.Abs(qy[2]) > PointTolerance)
            {
                MirrorV7Diagnostics.Log("[INPLACE][EXPLICIT_PROFILE_SUPPORT_REMAP_REQUIRED] sketch=\"" +
                    sketchFeature.Name + "\" q0z=" + q0[2] + " qxz=" + qx[2] + " qyz=" + qy[2]);
                return false; // Reflection moved this sketch onto another support plane.
            }
            double[] probe = Math.Abs(q0[0]) + Math.Abs(q0[1]) > 1e-9 ? new double[] { 0, 0, 0 } :
                (Math.Abs(qx[0] - 1) + Math.Abs(qx[1]) > 1e-9 ? new double[] { 1, 0, 0 } : new double[] { 0, 1, 0 });
            double[] mapped = probe[0] == 0 && probe[1] == 0 ? q0 : (probe[0] == 1 ? qx : qy);
            double dx = mapped[0] - probe[0], dy = mapped[1] - probe[1];
            double delta = Math.Sqrt(dx * dx + dy * dy);
            if (delta <= 1e-12)
            {
                MirrorV7Diagnostics.Log("[INPLACE][EXPLICIT_PROFILE_NO_AFFINE_CHANGE] sketch=\"" +
                    sketchFeature.Name + "\"");
                return false;
            }
            double mx = (probe[0] + mapped[0]) * 0.5, my = (probe[1] + mapped[1]) * 0.5;
            double ux = -dy / delta, uy = dx / delta;

            model.ClearSelection2(true);
            if (!sketchFeature.Select2(false, 0)) throw new InvalidOperationException("Cannot select Base sketch.");
            model.EditSketch();
            if (model.SketchManager.ActiveSketch == null ||
                !SingleSketchTargetBuilderV7.SameComObject(model.SketchManager.ActiveSketch, sketch))
                throw new InvalidOperationException("The wrong Base sketch entered edit mode.");

            bool oldAddToDb = model.SketchManager.AddToDB;
            bool oldAutoSolve = model.SketchManager.AutoSolve;
            SketchSegment axis = null;
            try
            {
                model.SketchManager.AddToDB = true;
                model.SketchManager.AutoSolve = false;
                axis = model.SketchManager.CreateCenterLine(mx - ux, my - uy, 0, mx + ux, my + uy, 0) as SketchSegment;
                if (axis == null) throw new InvalidOperationException("Cannot create reflection-axis construction line.");
                foreach (SketchSegment segment in source)
                {
                    SketchSegment created = CreateReflectedSegment(model.SketchManager, segment, math,
                        sketchToModel, modelToSketch, reflection);
                    if (created == null) throw new InvalidOperationException("Cannot create reflected Base profile segment.");
                    segment.ConstructionGeometry = true;
                    reflectedProfile.Add(created);
                }
            }
            finally
            {
                try { model.SketchManager.AddToDB = oldAddToDb; } catch { }
                try { model.SketchManager.AutoSolve = oldAutoSolve; } catch { }
            }

            ApplyConstraint(model, new SketchSegment[] { axis }, "sgFIXED");
            for (int i = 0; i < source.Count; i++)
                ApplyConstraint(model, new SketchSegment[] { source[i], reflectedProfile[i], axis }, "sgSYMMETRIC");
            model.SketchManager.InsertSketch(true);
            MirrorV7Diagnostics.Log("[INPLACE][EXPLICIT_PROFILE] sketch=\"" + sketchFeature.Name +
                "\" sourceSegments=" + source.Count + " reflectedSegments=" + reflectedProfile.Count +
                " axis=Fixed symmetryRelations=" + source.Count + " sketchMirrorApi=False");
            return true;
        }

        private static SketchSegment CreateReflectedSegment(SketchManager manager, SketchSegment source,
            IMathUtility math, MathTransform sketchToModel, MathTransform modelToSketch,
            PartReflectionTransformV7 reflection)
        {
            if (source.GetType() == (int)swSketchSegments_e.swSketchLINE)
            {
                ISketchLine line = source as ISketchLine;
                if (line == null) throw new InvalidOperationException("Line interface unavailable.");
                SketchPoint a = line.GetStartPoint2() as SketchPoint;
                SketchPoint b = line.GetEndPoint2() as SketchPoint;
                double[] ra = ReflectLocal(math, sketchToModel, modelToSketch, reflection, Point(a));
                double[] rb = ReflectLocal(math, sketchToModel, modelToSketch, reflection, Point(b));
                return manager.CreateLine(ra[0], ra[1], ra[2], rb[0], rb[1], rb[2]) as SketchSegment;
            }
            ISketchArc arc = source as ISketchArc;
            if (arc == null) throw new InvalidOperationException("Arc interface unavailable.");
            SketchPoint c = arc.GetCenterPoint2() as SketchPoint;
            SketchPoint a2 = arc.GetStartPoint2() as SketchPoint;
            SketchPoint b2 = arc.GetEndPoint2() as SketchPoint;
            double[] rc = ReflectLocal(math, sketchToModel, modelToSketch, reflection, Point(c));
            double[] ra2 = ReflectLocal(math, sketchToModel, modelToSketch, reflection, Point(a2));
            double[] rb2 = ReflectLocal(math, sketchToModel, modelToSketch, reflection, Point(b2));
            if (arc.IsCircle() != 0)
                return manager.CreateCircle(rc[0], rc[1], rc[2], ra2[0], ra2[1], ra2[2]) as SketchSegment;
            short direction = (short)(arc.GetRotationDir() >= 0 ? -1 : 1);
            return manager.CreateArc(rc[0], rc[1], rc[2], ra2[0], ra2[1], ra2[2],
                rb2[0], rb2[1], rb2[2], direction) as SketchSegment;
        }

        private static void ApplyConstraint(ModelDoc2 model, IList<SketchSegment> entities, string constraint)
        {
            model.ClearSelection2(true);
            SelectionMgr selection = model.SelectionManager as SelectionMgr;
            SelectData data = selection == null ? null : selection.CreateSelectData();
            if (data == null) throw new InvalidOperationException("Sketch selection data unavailable.");
            for (int i = 0; i < entities.Count; i++)
                if (entities[i] == null || !entities[i].Select4(i != 0, data))
                    throw new InvalidOperationException("Cannot select entity for " + constraint + ".");
            model.SketchAddConstraints(constraint);
        }

        private static double[] Point(SketchPoint point)
        {
            if (point == null) throw new InvalidOperationException("Sketch segment point unavailable.");
            return new double[] { point.X, point.Y, point.Z };
        }

        private static double[] ReflectLocal(IMathUtility math, MathTransform sketchToModel,
            MathTransform modelToSketch, PartReflectionTransformV7 reflection, double[] local)
        {
            double[] model = SketchMutationMathV7.Transform(math, sketchToModel, local);
            return SketchMutationMathV7.Transform(math, modelToSketch, reflection.ReflectPoint(model));
        }

        private static bool TrySketchModifyFlip(ModelDoc2 model, Feature sketchFeature, Sketch sketch,
            IMathUtility math, IList<SketchPointTargetV7> targets, out int acceptedAxis)
        {
            acceptedAxis = 0;
            for (int axis = 1; axis <= 2; axis++)
            {
                model.ClearSelection2(true);
                if (!sketchFeature.Select2(false, 0))
                    throw new InvalidOperationException("Cannot select Base sketch for SketchModifyFlip.");
                model.SketchModifyFlip(axis);
                try
                {
                    SingleSketchMutationVerifierV7.VerifyPoints(model, sketch, math, targets);
                    acceptedAxis = axis;
                    MirrorV7Diagnostics.Log("[INPLACE][SKETCH_MODIFY_FLIP_ACCEPTED] sketch=\"" +
                        sketchFeature.Name + "\" axis=" + axis);
                    return true;
                }
                catch (Exception candidateError)
                {
                    // SketchModifyFlip is an involution. Apply the same axis again and prove
                    // that the rejected candidate was fully reverted before trying another axis.
                    model.ClearSelection2(true);
                    if (!sketchFeature.Select2(false, 0))
                        throw new InvalidOperationException("Cannot reselect Base sketch to revert rejected flip.");
                    model.SketchModifyFlip(axis);
                    VerifyOriginalPointPositions(model, sketch, math, targets);
                    MirrorV7Diagnostics.Log("[INPLACE][SKETCH_MODIFY_FLIP_REJECTED] sketch=\"" +
                        sketchFeature.Name + "\" axis=" + axis + " message=" + candidateError.Message);
                }
            }
            return false;
        }

        private static void VerifyOriginalPointPositions(ModelDoc2 model, Sketch sketch, IMathUtility math,
            IList<SketchPointTargetV7> targets)
        {
            MathTransform modelToSketch = sketch.ModelToSketchTransform as MathTransform;
            MathTransform sketchToModel = modelToSketch == null ? null : modelToSketch.IInverse();
            if (sketchToModel == null) throw new InvalidOperationException("Sketch transform unavailable after flip reversal.");
            foreach (SketchPointTargetV7 target in targets)
            {
                int state;
                SketchPoint point = PersistentReferenceServiceV7.Resolve(model, target.Reference, out state) as SketchPoint;
                if (point == null) throw new InvalidOperationException("Sketch point lost while reversing candidate. state=" + state);
                double[] actual = SketchMutationMathV7.Transform(math, sketchToModel,
                    new double[] { point.X, point.Y, point.Z });
                if (SketchMutationMathV7.Distance(actual, target.BeforeModel) > PointTolerance)
                    throw new InvalidOperationException("Rejected SketchModifyFlip candidate could not be reverted exactly.");
            }
        }

        private static void ApplyReflectedSketchFrame(Sketch sketch, IMathUtility math,
            PartReflectionTransformV7 reflection, IList<SketchPointTargetV7> targets)
        {
            MathTransform originalModelToSketch = sketch.ModelToSketchTransform as MathTransform;
            MathTransform originalSketchToModel = originalModelToSketch == null ? null : originalModelToSketch.IInverse();
            if (originalSketchToModel == null) throw new InvalidOperationException("Original sketch transform unavailable.");
            // Derive the frame by transforming probe points. GetData/GetData2 return
            // different COM representations between SOLIDWORKS releases.
            double[] oldOrigin = SketchMutationMathV7.Transform(math, originalSketchToModel, new double[] { 0, 0, 0 });
            double[] oldPointX = SketchMutationMathV7.Transform(math, originalSketchToModel, new double[] { 1, 0, 0 });
            double[] oldPointY = SketchMutationMathV7.Transform(math, originalSketchToModel, new double[] { 0, 1, 0 });
            double[] oldX = Subtract(oldPointX, oldOrigin);
            double[] oldY = Subtract(oldPointY, oldOrigin);
            double scale = Length(oldX);
            if (scale <= 1e-12 || Math.Abs(Length(oldY) - scale) > 1e-9)
                throw new InvalidOperationException("Sketch frame scale is invalid.");
            double[] sketchNormal = Normalize(Cross(oldX, oldY));
            double[] mirrorNormal = Normalize(reflection.Normal);
            double[] axis = Cross(sketchNormal, mirrorNormal);
            double axisLengthSquared = Dot(axis, axis);
            if (axisLengthSquared <= 1e-18)
                throw new InvalidOperationException("Sketch plane is parallel to the reflection plane; support re-parenting is required.");
            axis = Normalize(axis);
            double planeDistance = Dot(sketchNormal, oldOrigin);
            double[] mirrorCrossAxis = Cross(mirrorNormal, Cross(sketchNormal, mirrorNormal));
            double[] axisPointData = new double[] {
                planeDistance * mirrorCrossAxis[0] / axisLengthSquared,
                planeDistance * mirrorCrossAxis[1] / axisLengthSquared,
                planeDistance * mirrorCrossAxis[2] / axisLengthSquared };
            MathPoint axisPoint = math.CreatePoint(axisPointData) as MathPoint;
            MathVector axisVector = math.CreateVector(axis) as MathVector;
            MathTransform rotation = math.CreateTransformRotateAxis(axisPoint, axisVector, Math.PI) as MathTransform;
            if (axisPoint == null || axisVector == null || rotation == null)
                throw new InvalidOperationException("Cannot build the equivalent 180-degree sketch-frame rotation.");
            MathTransform first = originalSketchToModel.Multiply(rotation) as MathTransform;
            MathTransform second = rotation.Multiply(originalSketchToModel) as MathTransform;
            MathTransform targetSketchToModel = MatchesTargets(math, first, targets) ? first :
                MatchesTargets(math, second, targets) ? second : null;
            if (targetSketchToModel == null)
                throw new InvalidOperationException("Reflected sketch-frame validation failed before assignment.");
            MathTransform targetModelToSketch = targetSketchToModel.IInverse();
            if (targetModelToSketch == null) throw new InvalidOperationException("Reflected sketch frame is not invertible.");
            try
            {
                sketch.ModelToSketchTransform = targetModelToSketch;
            }
            catch
            {
                try { sketch.ModelToSketchTransform = originalModelToSketch; } catch { }
                throw;
            }
        }

        private static bool MatchesTargets(IMathUtility math, MathTransform candidate,
            IList<SketchPointTargetV7> targets)
        {
            if (candidate == null) return false;
            foreach (SketchPointTargetV7 target in targets)
            {
                double[] projected = SketchMutationMathV7.Transform(math, candidate, target.BeforeSketch);
                if (SketchMutationMathV7.Distance(projected, target.TargetModel) > PointTolerance) return false;
            }
            return true;
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static double[] Subtract(double[] a, double[] b)
        {
            return new double[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }

        private static double Length(double[] value)
        {
            return Math.Sqrt(value[0] * value[0] + value[1] * value[1] + value[2] * value[2]);
        }

        private static double[] Cross(double[] a, double[] b)
        {
            return new double[] { a[1] * b[2] - a[2] * b[1],
                a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        private static double[] Normalize(double[] value)
        {
            double length = Math.Sqrt(value[0] * value[0] + value[1] * value[1] + value[2] * value[2]);
            if (length <= 1e-12) throw new InvalidOperationException("Reflected sketch axes are degenerate.");
            return new double[] { value[0] / length, value[1] / length, value[2] / length };
        }

        private static MirrorV7FeatureNode FindAbsorbedProfile(MirrorV7ModelGraph graph, MirrorV7FeatureNode baseNode)
        {
            return graph.Nodes.Where(n => n != null && n.Role == MirrorV7FeatureRole.Sketch && !n.IsSuppressed &&
                n.TreeOrder < baseNode.TreeOrder && baseNode.ParentFeatureNames.Any(p =>
                    string.Equals(p, n.Name, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(n => n.TreeOrder).FirstOrDefault();
        }

        private static SketchBaseline CaptureBaseline(Sketch sketch, Feature sketchFeature)
        {
            SketchBaseline result = new SketchBaseline();
            object[] points = sketch.GetSketchPoints2() as object[];
            object[] segments = sketch.GetSketchSegments() as object[];
            result.Points = points == null ? 0 : points.Length;
            result.Segments = segments == null ? 0 : segments.Length;
            result.Relations = sketch.RelationManager == null ? 0 :
                sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll);
            if (segments != null) foreach (object raw in segments)
            {
                ISketchSegment segment = raw as ISketchSegment;
                if (segment == null) throw new InvalidOperationException("Unexpected sketch segment object.");
                result.SegmentLengths.Add(segment.GetLength());
            }
            object current = sketchFeature.GetFirstDisplayDimension();
            int guard = 0;
            while (current != null && guard++ < 100000)
            {
                DisplayDimension display = current as DisplayDimension;
                if (display == null) throw new InvalidOperationException("Unexpected sketch display dimension object.");
                Dimension dimension = display.GetDimension2(0);
                if (dimension == null) throw new InvalidOperationException("Sketch dimension unavailable.");
                result.DimensionValues.Add(dimension.SystemValue);
                result.Dimensions++;
                current = sketchFeature.GetNextDisplayDimension(current);
            }
            return result;
        }

        private static void VerifyBaseline(Sketch sketch, Feature feature, SketchBaseline before)
        {
            SketchBaseline after = CaptureBaseline(sketch, feature);
            if (before.Points != after.Points || before.Segments != after.Segments ||
                before.Relations != after.Relations || before.Dimensions != after.Dimensions)
                throw new InvalidOperationException("Base sketch topology, relation or dimension count changed.");
            for (int i = 0; i < before.SegmentLengths.Count; i++)
                if (Math.Abs(before.SegmentLengths[i] - after.SegmentLengths[i]) > 1e-8)
                    throw new InvalidOperationException("Base sketch segment length changed at index " + i + ".");
            for (int i = 0; i < before.DimensionValues.Count; i++)
                if (Math.Abs(before.DimensionValues[i] - after.DimensionValues[i]) > 1e-10)
                    throw new InvalidOperationException("Base sketch driving dimension changed at index " + i + ".");
        }

        private static void VerifyConstructionBackedBaseline(Sketch sketch, Feature feature, SketchBaseline before)
        {
            SketchBaseline after = CaptureBaseline(sketch, feature);
            if (after.Dimensions != before.Dimensions)
                throw new InvalidOperationException("Construction-backed reflection changed the Base sketch dimension count.");
            if (after.Relations < before.Relations)
                throw new InvalidOperationException("Construction-backed reflection removed an existing Base sketch relation.");
            for (int i = 0; i < before.DimensionValues.Count; i++)
                if (Math.Abs(before.DimensionValues[i] - after.DimensionValues[i]) > 1e-10)
                    throw new InvalidOperationException("Construction-backed reflection changed driving dimension " + i + ".");
            object[] raw = sketch.GetSketchSegments() as object[];
            List<SketchSegment> construction = raw == null ? new List<SketchSegment>() : raw.Cast<object>()
                .Select(x => x as SketchSegment).Where(x => x != null && x.ConstructionGeometry).ToList();
            foreach (double expectedLength in before.SegmentLengths)
                if (!construction.Any(x => Math.Abs(x.GetLength() - expectedLength) <= 1e-8))
                    throw new InvalidOperationException("Original dimension-driving construction geometry was not preserved.");
        }

        private static double VerifyReflectedProfile(Sketch sketch, IMathUtility math,
            PartReflectionTransformV7 reflection, IList<SketchSegment> reflected)
        {
            MathTransform modelToSketch = sketch.ModelToSketchTransform as MathTransform;
            MathTransform sketchToModel = modelToSketch == null ? null : modelToSketch.IInverse();
            if (sketchToModel == null) throw new InvalidOperationException("Sketch transform unavailable during reflected-profile verification.");
            object[] raw = sketch.GetSketchSegments() as object[];
            List<SketchSegment> source = raw == null ? new List<SketchSegment>() : raw.Cast<object>()
                .Select(x => x as SketchSegment).Where(x => x != null && x.ConstructionGeometry).ToList();
            // The final construction entity is the fixed reflection axis. Match every reflected
            // profile entity to one of the remaining source entities by reflected geometry.
            double maximum = 0;
            foreach (SketchSegment target in reflected)
            {
                bool matched = false;
                double nearestError = double.MaxValue;
                foreach (SketchSegment candidate in source)
                {
                    if (candidate.GetType() != target.GetType()) continue;
                    double error = SegmentReflectionError(candidate, target, math, sketchToModel, reflection);
                    nearestError = Math.Min(nearestError, error);
                    if (error <= PointTolerance)
                    {
                        maximum = Math.Max(maximum, error);
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    var arc = target as ISketchArc;
                    throw new InvalidOperationException("Reflected profile does not match its construction driver: segmentType=" +
                        target.GetType() + " circle=" + (arc != null && arc.IsCircle() != 0) +
                        " nearestErrorSI=" + nearestError.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            return maximum;
        }

        private static double SegmentReflectionError(SketchSegment source, SketchSegment target,
            IMathUtility math, MathTransform sketchToModel, PartReflectionTransformV7 reflection)
        {
            if (source.GetType() == (int)swSketchSegments_e.swSketchLINE)
            {
                ISketchLine a = source as ISketchLine, b = target as ISketchLine;
                if (a == null || b == null) return double.MaxValue;
                double[] a0 = ToModel(math, sketchToModel, Point(a.GetStartPoint2() as SketchPoint));
                double[] a1 = ToModel(math, sketchToModel, Point(a.GetEndPoint2() as SketchPoint));
                double[] b0 = ToModel(math, sketchToModel, Point(b.GetStartPoint2() as SketchPoint));
                double[] b1 = ToModel(math, sketchToModel, Point(b.GetEndPoint2() as SketchPoint));
                double forward = Math.Max(SketchMutationMathV7.Distance(reflection.ReflectPoint(a0), b0),
                    SketchMutationMathV7.Distance(reflection.ReflectPoint(a1), b1));
                double reverse = Math.Max(SketchMutationMathV7.Distance(reflection.ReflectPoint(a0), b1),
                    SketchMutationMathV7.Distance(reflection.ReflectPoint(a1), b0));
                return Math.Min(forward, reverse);
            }
            ISketchArc aa = source as ISketchArc, bb = target as ISketchArc;
            if (aa == null || bb == null) return double.MaxValue;
            bool sourceCircle = aa.IsCircle() != 0, targetCircle = bb.IsCircle() != 0;
            if (sourceCircle != targetCircle) return double.MaxValue;
            double centre = SketchMutationMathV7.Distance(
                reflection.ReflectPoint(ToModel(math, sketchToModel, Point(aa.GetCenterPoint2() as SketchPoint))),
                ToModel(math, sketchToModel, Point(bb.GetCenterPoint2() as SketchPoint)));
            double radiusError = Math.Abs(aa.GetRadius() - bb.GetRadius());
            // A full circle has no geometric start/end vertex. SOLIDWORKS may move its
            // parameter seam after solving; comparing those points rejects the same circle.
            if (sourceCircle)
                return MirrorProfileGeometryV7.CircleError(centre, aa.GetRadius(), bb.GetRadius());
            double[] as0 = ToModel(math, sketchToModel, Point(aa.GetStartPoint2() as SketchPoint));
            double[] as1 = ToModel(math, sketchToModel, Point(aa.GetEndPoint2() as SketchPoint));
            double[] bs0 = ToModel(math, sketchToModel, Point(bb.GetStartPoint2() as SketchPoint));
            double[] bs1 = ToModel(math, sketchToModel, Point(bb.GetEndPoint2() as SketchPoint));
            double endpoints = Math.Min(
                Math.Max(SketchMutationMathV7.Distance(reflection.ReflectPoint(as0), bs0),
                    SketchMutationMathV7.Distance(reflection.ReflectPoint(as1), bs1)),
                Math.Max(SketchMutationMathV7.Distance(reflection.ReflectPoint(as0), bs1),
                    SketchMutationMathV7.Distance(reflection.ReflectPoint(as1), bs0)));
            double lengthError = Math.Abs(source.GetLength() - target.GetLength());
            double middleError = SketchMutationMathV7.Distance(
                reflection.ReflectPoint(ToModel(math, sketchToModel, ArcMiddle(aa, source.GetLength()))),
                ToModel(math, sketchToModel, ArcMiddle(bb, target.GetLength())));
            return new[] { centre, radiusError, endpoints, lengthError, middleError }.Max();
        }

        private static double[] ArcMiddle(ISketchArc arc, double length)
        {
            double[] centre = Point(arc.GetCenterPoint2() as SketchPoint);
            double[] start = Point(arc.GetStartPoint2() as SketchPoint);
            double radius = arc.GetRadius();
            if (radius <= 0) throw new InvalidOperationException("Invalid sketch arc radius.");
            double angle = length / radius * 0.5 * (arc.GetRotationDir() >= 0 ? 1 : -1);
            double x = start[0] - centre[0], y = start[1] - centre[1];
            return new[] { centre[0] + x * Math.Cos(angle) - y * Math.Sin(angle),
                centre[1] + x * Math.Sin(angle) + y * Math.Cos(angle), centre[2] };
        }

        private static double[] ToModel(IMathUtility math, MathTransform sketchToModel, double[] local)
        {
            return SketchMutationMathV7.Transform(math, sketchToModel, local);
        }

        private static bool TryAdjustBaseFlangeDirections(MirrorInPlaceExecutionContextV7 context,
            Feature feature, List<Body2> oracle)
        {
            IBaseFlangeFeatureData initial = feature.GetDefinition() as IBaseFlangeFeatureData;
            if (initial == null)
            {
                MirrorV7Diagnostics.Log("[INPLACE][BASE_DIRECTION_UNAVAILABLE] feature=\"" + feature.Name + "\" definition=\"null\"");
                return false;
            }
            bool reverseDirection, reverseThickness, reverseD1, reverseD2;
            try
            {
                reverseDirection = initial.ReverseDirection;
                reverseThickness = initial.ReverseThickness;
                reverseD1 = initial.D1ReverseOffset;
                reverseD2 = initial.D2ReverseOffset;
            }
            catch (Exception ex)
            {
                MirrorV7Diagnostics.Log("[INPLACE][BASE_DIRECTION_UNAVAILABLE] feature=\"" + feature.Name +
                    "\" readProperties=\"" + ex.Message + "\"");
                return false;
            }
            int[] candidates = Enumerable.Range(1, 15).OrderBy(BitCount).ToArray();
            foreach (int mask in candidates)
            {
                IBaseFlangeFeatureData data = feature.GetDefinition() as IBaseFlangeFeatureData;
                if (data == null) continue;
                bool accessed = false;
                try
                {
                    accessed = data.AccessSelections(context.WorkingDocument, null);
                    if (!accessed) continue;
                    data.ReverseDirection = reverseDirection ^ ((mask & 1) != 0);
                    data.ReverseThickness = reverseThickness ^ ((mask & 2) != 0);
                    data.D1ReverseOffset = reverseD1 ^ ((mask & 4) != 0);
                    data.D2ReverseOffset = reverseD2 ^ ((mask & 8) != 0);
                    if (!feature.ModifyDefinition(data, context.WorkingDocument, null)) continue;
                    accessed = false;
                    if (!context.WorkingDocument.EditRebuild3()) continue;
                    try
                    {
                        VerifyCurrentSolids(context, oracle);
                        MirrorV7Diagnostics.Log("[INPLACE][BASE_DIRECTION_PASS] feature=\"" + feature.Name +
                            "\" mask=" + mask + " reverseDirection=" + data.ReverseDirection +
                            " reverseThickness=" + data.ReverseThickness + " d1ReverseOffset=" + data.D1ReverseOffset +
                            " d2ReverseOffset=" + data.D2ReverseOffset);
                        return true;
                    }
                    catch (InvalidOperationException) { }
                }
                catch (Exception ex)
                {
                    MirrorV7Diagnostics.Log("[INPLACE][BASE_DIRECTION_REJECTED] feature=\"" + feature.Name +
                        "\" mask=" + mask + " message=" + ex.Message);
                }
                finally
                {
                    if (accessed) try { data.ReleaseSelectionAccess(); } catch { }
                }
            }
            RestoreBaseFlangeDirections(context, feature, reverseDirection, reverseThickness, reverseD1, reverseD2);
            return false;
        }

        private static void RestoreBaseFlangeDirections(MirrorInPlaceExecutionContextV7 context,
            Feature feature, bool reverseDirection, bool reverseThickness, bool reverseD1, bool reverseD2)
        {
            IBaseFlangeFeatureData data = feature.GetDefinition() as IBaseFlangeFeatureData;
            if (data == null)
                throw new InvalidOperationException("Cannot restore Base Flange definition after direction search.");
            bool accessed = false;
            try
            {
                accessed = data.AccessSelections(context.WorkingDocument, null);
                if (!accessed)
                    throw new InvalidOperationException("Cannot access Base Flange selections while restoring direction flags.");
                data.ReverseDirection = reverseDirection;
                data.ReverseThickness = reverseThickness;
                data.D1ReverseOffset = reverseD1;
                data.D2ReverseOffset = reverseD2;
                if (!feature.ModifyDefinition(data, context.WorkingDocument, null))
                    throw new InvalidOperationException("ModifyDefinition rejected the original Base Flange direction flags.");
                accessed = false;
                if (!context.WorkingDocument.EditRebuild3())
                    throw new InvalidOperationException("Base Flange failed to rebuild after restoring direction flags.");
                MirrorV7Diagnostics.Log("[INPLACE][BASE_DIRECTION_RESTORED] feature=\"" + feature.Name +
                    "\" reverseDirection=" + reverseDirection +
                    " reverseThickness=" + reverseThickness + " d1ReverseOffset=" + reverseD1 +
                    " d2ReverseOffset=" + reverseD2);
            }
            finally
            {
                if (accessed) try { data.ReleaseSelectionAccess(); } catch { }
            }
        }

        private static int BitCount(int value)
        {
            int count = 0;
            while (value != 0) { count += value & 1; value >>= 1; }
            return count;
        }

        internal static List<Body2> ReflectCurrentSolids(MirrorInPlaceExecutionContextV7 context)
        {
            List<Body2> source = Solids(context.WorkingDocument);
            double[] data = new double[16];
            Array.Copy(context.Reflection.GetLinearMatrix3x3(), data, 9);
            data[12] = 1;
            MathTransform transform = ReflectionApiTransformV7.Create(
                context.SwApp.GetMathUtility() as IMathUtility, data);
            List<Body2> result = new List<Body2>();
            foreach (Body2 body in source)
            {
                Body2 copy = body.Copy2(false) as Body2;
                if (copy == null || !copy.ApplyTransform(transform))
                    throw new InvalidOperationException("Cannot create Base Feature geometry oracle.");
                result.Add(copy);
            }
            return result;
        }

        internal static void VerifyCurrentSolids(MirrorInPlaceExecutionContextV7 context, List<Body2> oracle)
        {
            List<Body2> actual = Solids(context.WorkingDocument);
            if (oracle.Count != actual.Count) throw new InvalidOperationException("Checkpoint body count mismatch: expected=" + oracle.Count + " actual=" + actual.Count);
            EquivalenceMatchResultV7 match = BipartiteEquivalenceMatcherV7.Match(oracle, actual,
                delegate(Body2 expected, Body2 candidate)
                {
                    // expected is already reflected; compare it directly to candidate.
                    double[] a = expected.GetMassProperties(1.0) as double[];
                    double[] b = candidate.GetMassProperties(1.0) as double[];
                    if (a == null || b == null || a.Length < 5 || b.Length < 5 || a[3] <= 0 || b[3] <= 0 ||
                        a.Concat(b).Any(v => double.IsNaN(v) || double.IsInfinity(v))) return false;
                    bool massAgrees = Math.Abs(a[3] - b[3]) <= Math.Max(1e-15, Math.Abs(a[3]) * 1e-6);
                    for (int i = 0; i < 3; i++) massAgrees &= Math.Abs(a[i] - b[i]) <= PointTolerance;
                    if (!SameVertexSet(expected, candidate)) return false;
                    var left = BodyOperationsHelper.BooleanCutStrict(expected, candidate, "INPLACE_EXPECTED_MINUS_ACTUAL");
                    var right = BodyOperationsHelper.BooleanCutStrict(candidate, expected, "INPLACE_ACTUAL_MINUS_EXPECTED");
                    double tolerance = Math.Max(1e-15, Math.Abs(a[3]) * 1e-6);
                    if (!massAgrees)
                    {
                        // In the live bent-sheet regression, equal vertices and two
                        // EMPTY native cuts proved the same BRep, while GetMassProperties
                        // gave different numerical integrals for a BSpline bend face.
                        // Do not enlarge mass/position tolerances or accept a small
                        // residual in this branch. Require strictly empty differences.
                        bool exact = left.Success && right.Success && left.Bodies.Count == 0 && right.Bodies.Count == 0;
                        MirrorV7Diagnostics.Log("[BODY63][MASS_DISAGREEMENT] volumeDelta_m3=" + (b[3] - a[3]) +
                            " centroidDelta_m=" + string.Join(",", Enumerable.Range(0, 3).Select(i => b[i] - a[i])) +
                            " vertexSetEqual=True missingCount=" + left.Bodies.Count + " extraCount=" + right.Bodies.Count +
                            " booleanSuccess=" + left.Success + "," + right.Success +
                            " proof=EMPTY_BIDIRECTIONAL_BOOLEAN result=" + (exact ? "PASS" : "FAIL"));
                        return exact;
                    }
                    return left.Success && right.Success &&
                        left.Bodies.Sum(x => ((double[])x.GetMassProperties(1.0))[3]) <= tolerance &&
                        right.Bodies.Sum(x => ((double[])x.GetMassProperties(1.0))[3]) <= tolerance;
                });
            if (!match.HasPerfectMatching) throw new InvalidOperationException("Current checkpoint geometry does not match reflected oracle (mass, centroid, vertices or Boolean difference).");
        }

        private static bool SameVertexSet(Body2 left, Body2 right)
        {
            object[] la = left.GetVertices() as object[];
            object[] ra = right.GetVertices() as object[];
            List<double[]> a = la == null ? new List<double[]>() : la.Cast<Vertex>().Select(v => v.GetPoint() as double[]).ToList();
            List<double[]> b = ra == null ? new List<double[]>() : ra.Cast<Vertex>().Select(v => v.GetPoint() as double[]).ToList();
            if (a.Count == 0 || a.Count != b.Count) return false;
            return BipartiteEquivalenceMatcherV7.Match(a, b, delegate(double[] x, double[] y)
            {
                return x != null && y != null && x.Length >= 3 && y.Length >= 3 &&
                    Math.Abs(x[0] - y[0]) <= PointTolerance && Math.Abs(x[1] - y[1]) <= PointTolerance &&
                    Math.Abs(x[2] - y[2]) <= PointTolerance;
            }).HasPerfectMatching;
        }

        private static List<Body2> Solids(ModelDoc2 model)
        {
            PartDoc part = model as PartDoc;
            object[] raw = part == null ? null : part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (raw == null || raw.Length == 0) throw new InvalidOperationException("Base Feature produced no solid body.");
            return raw.Cast<Body2>().ToList();
        }
    }
}
