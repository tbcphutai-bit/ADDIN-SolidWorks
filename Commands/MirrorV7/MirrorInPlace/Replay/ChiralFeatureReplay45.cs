using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Helpers;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    public static partial class RollbackReplayEngineV7
    {
        // Numerical snapshots only: never retain selected COM entities across rollback.
        internal sealed class ChiralRecipe45
        {
            internal readonly List<ChiralSketch45> Sketches = new List<ChiralSketch45>();
            internal readonly List<HelixRecipe45> Helices = new List<HelixRecipe45>();
            internal short Twist;
            internal bool D1Reverse, D2Reverse;
            internal int Direction;
        }
        internal sealed class ChiralSketch45
        {
            internal string Name;
            internal bool Is3D;
            internal List<SketchPointSnapshot> Points;
            internal SketchSupportSnapshot20 Support;
            internal readonly Dictionary<string, double[][]> Curves = new Dictionary<string, double[][]>();
        }
        internal static bool IsChiral45(object data)
        {
            return data is ISweepFeatureData || data is ILoftFeatureData || data is ILoftedBendsFeatureData;
        }

        private static Dictionary<string, object> ReadChiralOptions45(object definition)
        {
            var o = new Dictionary<string, object>();
            var s = definition as ISweepFeatureData;
            if (s != null)
            {
                o["TwistControlType"] = s.TwistControlType; o["Direction"] = s.Direction;
                o["PathAlignmentType"] = s.PathAlignmentType; o["CircularProfile"] = s.CircularProfile;
                if (s.CircularProfile) o["CircularProfileDiameter"] = s.CircularProfileDiameter;
                o["StartTangencyType"] = s.StartTangencyType; o["EndTangencyType"] = s.EndTangencyType;
                o["MaintainTangency"] = s.MaintainTangency; o["AdvancedSmoothing"] = s.AdvancedSmoothing;
                o["AlignWithEndFaces"] = s.AlignWithEndFaces; o["MergeSmoothFaces"] = s.MergeSmoothFaces;
                o["Merge"] = s.Merge; o["FeatureScope"] = s.FeatureScope; o["AutoSelect"] = s.AutoSelect;
                o["ThinFeature"] = s.IsThinFeature();
                if (s.IsThinFeature())
                { o["ThinWallType"] = s.ThinWallType; o["Wall1"] = s.GetWallThickness(true); o["Wall2"] = s.GetWallThickness(false); }
                if (s.TwistControlType == 8 || s.TwistControlType == 9)
                {
                    o["TwistAngle"] = s.GetTwistAngle();
                    if (s.Direction == 1) o["D2TwistAngle"] = s.GetD2TwistAngle();
                }
                return o;
            }
            var l = definition as ILoftFeatureData;
            if (l != null)
            {
                o["Close"] = l.Close; o["MaintainTangency"] = l.MaintainTangency;
                o["AdvancedSmoothing"] = l.AdvancedSmoothing; o["Merge"] = l.Merge;
                o["FeatureScope"] = l.FeatureScope; o["AutoSelect"] = l.AutoSelect;
                o["GuideCurveInfluence"] = l.GuideCurveInfluence;
                o["StartTangencyType"] = l.StartTangencyType; o["EndTangencyType"] = l.EndTangencyType;
                o["ThinFeature"] = l.IsThinFeature();
                if (l.IsThinFeature())
                { o["ThinWallType"] = l.ThinWallType; o["Wall1"] = l.GetWallThickness(true); o["Wall2"] = l.GetWallThickness(false); }
                return o;
            }
            var b = (ILoftedBendsFeatureData)definition;
            o["Thickness"] = b.Thickness; o["SymmetricThickness"] = b.SymmetricThickness;
            o["FormedMethod"] = b.FormedMethod; o["ReferToEndPoint"] = b.ReferToEndPoint;
            o["BendLineControlOption"] = b.BendLineControlOption;
            return o;
        }

        private static ChiralRecipe45 CaptureChiral45(MirrorInPlaceExecutionContextV7 context, Feature feature)
        {
            var recipe = new ChiralRecipe45();
            var names = new List<string>();
            Action<object> add = null;
            add = value =>
            {
                if (value == null) return;
                var array = value as Array;
                if (array != null) { foreach (object element in array) add(element); return; }
                Sketch sketch = value as Sketch;
                var owner = value as Feature;
                if (owner != null && owner.GetDefinition() is IHelixFeatureData)
                {
                    if (!recipe.Helices.Any(h => h.Name == owner.Name)) recipe.Helices.Add(CaptureHelix45(context, owner));
                    return;
                }
                if (owner != null) sketch = owner.GetSpecificFeature2() as Sketch;
                var segment = value as SketchSegment;
                if (segment != null) sketch = segment.GetSketch() as Sketch;
                if (sketch == null)
                    throw new InvalidOperationException("CHIRAL45 unsupported profile/path/guide reference at " + feature.Name +
                        ": " + (owner == null ? value.GetType().Name : owner.GetTypeName2()) +
                        "; requires a dedicated curve/face reference mapper, not a direction guess.");
                if (owner == null) owner = FindSketchOwner28(context.WorkingDocument, sketch);
                if (!names.Contains(owner.Name)) names.Add(owner.Name);
            };
            object data = feature.GetDefinition();
            if (!AccessDefinition(data, context.WorkingDocument)) throw new InvalidOperationException("CHIRAL45 selection access failed.");
            try
            {
                var sweep = data as ISweepFeatureData;
                if (sweep != null)
                {
                    // These modes need explicit face/direction and body-scope mapping.
                    if (sweep.PathAlignmentType != 0 || sweep.StartTangencyType != 0 || sweep.EndTangencyType != 0 ||
                        sweep.IsThinFeature() || (sweep.FeatureScope && !sweep.AutoSelect))
                        throw new InvalidOperationException("CHIRAL45 sweep requires a direction/tangency/thin-wall/body-scope adapter.");
                    if (!sweep.CircularProfile) add(sweep.Profile);
                    add(sweep.Path);
                    if (sweep.GetGuideCurvesCount() > 0) add(sweep.GuideCurves);
                    recipe.Twist = sweep.TwistControlType; recipe.Direction = sweep.Direction;
                    if (recipe.Twist == 8 || recipe.Twist == 9)
                    {
                        recipe.D1Reverse = sweep.D1ReverseTwistDir;
                        if (recipe.Direction == 1) recipe.D2Reverse = sweep.D2ReverseTwistDir;
                    }
                }
                else if (data is ILoftFeatureData)
                {
                    var loft = (ILoftFeatureData)data;
                    if (loft.StartTangencyType != 0 || loft.EndTangencyType != 0 || loft.IsThinFeature() ||
                        (loft.FeatureScope && !loft.AutoSelect))
                        throw new InvalidOperationException("CHIRAL45 loft requires a tangency/thin-wall/body-scope adapter.");
                    add(loft.Profiles); add(loft.GuideCurves); add(loft.Centerline);
                }
                else
                {
                    var bends = (ILoftedBendsFeatureData)data;
                    // One-sided sheet thickness needs a profile-normal convention adapter.
                    if (!bends.SymmetricThickness)
                        throw new InvalidOperationException("CHIRAL45 one-sided lofted-bend thickness is not yet mapped.");
                    add(bends.Profiles);
                }
            }
            finally { ReleaseDefinition(data); }
            foreach (string name in names)
            {
                var owner = (Feature)((PartDoc)context.WorkingDocument).FeatureByName(name);
                var sketch = (Sketch)owner.GetSpecificFeature2();
                var snap = new ChiralSketch45 { Name = name, Is3D = sketch.Is3D(),
                    Points = SketchOperationsHelper.CapturePristineSketchPoints(sketch),
                    Support = sketch.Is3D() ? null : SketchOperationsHelper.CaptureSupport20(sketch, context.WorkingDocument) };
                if (snap.Points.Count == 0 || snap.Points.Any(p => !p.HasModelCoords))
                    throw new InvalidOperationException("CHIRAL45 missing absolute sketch coordinates: " + name);
                foreach (SketchSegment segment in sketch.GetSketchSegments() as object[] ?? new object[0])
                    snap.Curves.Add(SegmentKey45(segment), CurveSamples45(context, sketch, segment)
                        .Select(context.Reflection.ReflectPoint).ToArray());
                recipe.Sketches.Add(snap);
            }
            if (recipe.Sketches.Count == 0 && recipe.Helices.Count == 0) throw new InvalidOperationException("CHIRAL45 no editable driving sketches.");
            MirrorV7Diagnostics.Log("[CHIRAL45][CAPTURE] feature=" + feature.Name + " sketches=" + recipe.Sketches.Count);
            return recipe;
        }

        private static string SegmentKey45(SketchSegment segment)
        { return string.Join(":", (int[])segment.GetID()); }

        private static double[][] CurveSamples45(MirrorInPlaceExecutionContextV7 context, Sketch sketch, SketchSegment segment)
        {
            var curve = (Curve)segment.GetCurve();
            if (curve == null) throw new InvalidOperationException("CHIRAL45 unsupported sketch segment curve.");
            double start, end; bool closed, periodic;
            if (!curve.GetEndParams(out start, out end, out closed, out periodic))
                throw new InvalidOperationException("CHIRAL45 cannot evaluate sketch curve.");
            var math = (IMathUtility)context.SwApp.GetMathUtility();
            var frame = (MathTransform)((MathTransform)sketch.ModelToSketchTransform).Inverse();
            return Enumerable.Range(0, 33).Select(i =>
            {
                var point = (double[])curve.Evaluate2(start + (end - start) * i / 32.0, 0);
                return SketchMutationMathV7.Transform(math, frame, point.Take(3).ToArray());
            }).ToArray();
        }

        private static void VerifyChiralSketch45(MirrorInPlaceExecutionContextV7 context, ChiralSketch45 snap)
        {
            var feature = (Feature)((PartDoc)context.WorkingDocument).FeatureByName(snap.Name);
            var sketch = (Sketch)feature.GetSpecificFeature2();
            var segments = (sketch.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>().ToArray();
            if (segments.Length != snap.Curves.Count) throw new InvalidOperationException("CHIRAL45 curve count changed: " + snap.Name);
            foreach (var segment in segments)
            {
                double[][] expected;
                if (!snap.Curves.TryGetValue(SegmentKey45(segment), out expected))
                    throw new InvalidOperationException("CHIRAL45 curve identity changed: " + snap.Name);
                var actual = CurveSamples45(context, sketch, segment);
                // Ordered samples deliberately reject reversed path parameterization.
                if (actual.Where((p, i) => Distance(p, expected[i]) > 1e-7).Any())
                    throw new InvalidOperationException("CHIRAL45 curve/tangent geometry mismatch: " + snap.Name +
                        "; spline handles or path orientation require a dedicated adapter.");
            }
        }

        internal sealed class HelixRecipe45
        {
            internal string Name, SketchName;
            internal List<SketchPointSnapshot> Points;
            internal SketchSupportSnapshot20 Support;
            internal double[][][] Expected;
            internal double[] Axis, X, Y, Centre;
            internal bool Reverse, Clockwise;
            internal int Samples;
            internal Dictionary<string, object> Options;
        }
        private static Dictionary<string, object> HelixOptions45(IHelixFeatureData h)
        {
            var o = new Dictionary<string, object> { { "DefinedBy", h.DefinedBy }, { "Height", h.Height },
                { "Pitch", h.Pitch }, { "Revolution", h.Revolution }, { "Taper", h.Taper }, { "VariablePitch", h.VariablePitch } };
            if (h.Taper) { o["TaperAngle"] = h.TaperAngle; o["TaperOutward"] = h.TaperOutward; }
            return o;
        }
        private static double[][][] HelixSamples45(Feature feature, int samples)
        {
            var reference = feature.GetSpecificFeature2() as ReferenceCurve;
            var edges = reference == null ? null : reference.GetSegments() as object[];
            if (edges == null || edges.Length == 0) throw new InvalidOperationException("HELIX45 native curve is unavailable: " + feature.Name);
            return edges.Cast<Edge>().Select(edge =>
            {
                var bounds = edge.GetCurveParams3();
                var curve = (Curve)edge.GetCurve();
                return Enumerable.Range(0, samples).Select(i => ((double[])curve.Evaluate2(
                    bounds.UMinValue + (bounds.UMaxValue - bounds.UMinValue) * i / (samples - 1.0), 0)).Take(3).ToArray()).ToArray();
            }).ToArray();
        }
        private static HelixRecipe45 CaptureHelix45(MirrorInPlaceExecutionContextV7 context, Feature feature)
        {
            var h = (IHelixFeatureData)feature.GetDefinition();
            if (h.VariablePitch) throw new InvalidOperationException("HELIX45 variable pitch region mapping is not implemented: " + feature.Name);
            var sketches = new List<Feature>();
            for (var child = feature.GetFirstSubFeature() as Feature; child != null; child = child.GetNextSubFeature() as Feature)
                if (child.GetSpecificFeature2() is Sketch) sketches.Add(child);
            if (sketches.Count != 1) throw new InvalidOperationException("HELIX45 requires one native circle sketch.");
            var sketch = (Sketch)sketches[0].GetSpecificFeature2();
            var circles = (sketch.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>()
                .Where(s => !s.ConstructionGeometry).ToArray();
            if (sketch.Is3D() || circles.Length != 1 || !(circles[0] is SketchArc) || ((SketchArc)circles[0]).IsCircle() == 0)
                throw new InvalidOperationException("HELIX45 expected a single planar circle.");
            int samples = (int)Math.Ceiling(Math.Abs(h.Revolution) * 32) + 33;
            if (samples < 33 || samples > 32769) throw new InvalidOperationException("HELIX45 sample budget exceeded.");
            var math = (IMathUtility)context.SwApp.GetMathUtility();
            var frame = (MathTransform)((MathTransform)sketch.ModelToSketchTransform).Inverse();
            var origin = SketchMutationMathV7.Transform(math, frame, new double[] { 0, 0, 0 });
            Func<double[], double[]> direction = v => context.Reflection.ReflectVector(
                SketchMutationMathV7.Transform(math, frame, v).Select((n, i) => n - origin[i]).ToArray());
            var arc = (SketchArc)circles[0];
            var centre = (SketchPoint)arc.GetCenterPoint2();
            var recipe = new HelixRecipe45 { Name = feature.Name, SketchName = sketches[0].Name,
                Points = SketchOperationsHelper.CapturePristineSketchPoints(sketch),
                Support = SketchOperationsHelper.CaptureSupport20(sketch, context.WorkingDocument),
                Axis = direction((double[])arc.GetNormalVector()), X = direction(new double[] { 1, 0, 0 }),
                Y = direction(new double[] { 0, 1, 0 }),
                Centre = context.Reflection.ReflectPoint(SketchMutationMathV7.Transform(math, frame, new[] { centre.X, centre.Y, centre.Z })),
                Reverse = h.ReverseDirection, Clockwise = h.Clockwise, Samples = samples, Options = HelixOptions45(h),
                Expected = HelixSamples45(feature, samples).Select(edge => edge.Select(context.Reflection.ReflectPoint).ToArray()).ToArray() };
            MirrorV7Diagnostics.Log("[HELIX45][CAPTURE] feature=" + feature.Name + " samplesPerSegment=" + samples);
            return recipe;
        }
        private static void VerifyHelix45(MirrorInPlaceExecutionContextV7 context, HelixRecipe45 recipe)
        {
            var feature = (Feature)((PartDoc)context.WorkingDocument).FeatureByName(recipe.Name);
            var actual = HelixSamples45(feature, recipe.Samples);
            if (actual.Length != recipe.Expected.Length || actual.Where((edge, j) =>
                edge.Where((p, i) => Distance(p, recipe.Expected[j][i]) > 1e-7).Any()).Any())
                throw new InvalidOperationException("HELIX45 ordered curve geometry mismatch: " + recipe.Name);
            var options = HelixOptions45((IHelixFeatureData)feature.GetDefinition());
            foreach (var option in recipe.Options)
                if (!object.Equals(option.Value, options[option.Key])) throw new InvalidOperationException("HELIX45 option changed: " + option.Key);
        }
        private static void ReplayHelix45(MirrorInPlaceExecutionContextV7 context, HelixRecipe45 recipe)
        {
            try { VerifyHelix45(context, recipe); return; } catch (InvalidOperationException) { }
            var model = context.WorkingDocument;
            var feature = (Feature)((PartDoc)model).FeatureByName(recipe.Name);
            var owner = (Feature)((PartDoc)model).FeatureByName(recipe.SketchName);
            var plane = new ADDIN.Commands.PlaneData { Origin = context.Reflection.Origin, Normal = context.Reflection.Normal };
            SketchOperationsHelper.EnsureReflectedSupport20(context.SwApp, model, owner, recipe.Points, recipe.Support, plane);
            var sketch = SketchOperationsHelper.FreeSketchForMutation(model, owner, true);
            try { SketchOperationsHelper.MutateSketchPoints(model, sketch, recipe.Points, plane); }
            finally { if (model.SketchManager.ActiveSketch != null) model.SketchManager.InsertSketch(false); }
            MoveRollbackAfter(model, feature);
            model.EditRebuild3();
            sketch = (Sketch)owner.GetSpecificFeature2();
            var math = (IMathUtility)context.SwApp.GetMathUtility();
            var frame = (MathTransform)sketch.ModelToSketchTransform;
            var origin = SketchMutationMathV7.Transform(math, frame, new double[] { 0, 0, 0 });
            Func<double[], double[]> localVector = v => SketchMutationMathV7.Transform(math, frame, v)
                .Select((n, i) => n - origin[i]).ToArray();
            double[] x = localVector(recipe.X), y = localVector(recipe.Y), axis = localVector(recipe.Axis);
            if (Math.Abs(x[2]) > 1e-7 || Math.Abs(y[2]) > 1e-7)
                throw new InvalidOperationException("HELIX45 target circle plane is not reflected source plane.");
            var circle = (SketchArc)(sketch.GetSketchSegments() as object[]).Cast<SketchSegment>().Single(s => !s.ConstructionGeometry);
            double[] normal = (double[])circle.GetNormalVector();
            double axisDot = Enumerable.Range(0, 3).Sum(i => axis[i] * normal[i]);
            if (Math.Abs(axisDot) < 1 - 1e-6) throw new InvalidOperationException("HELIX45 ambiguous circle axis.");
            var h = (IHelixFeatureData)feature.GetDefinition();
            var current = HelixSamples45(feature, recipe.Samples)[0][0];
            var currentLocal = SketchMutationMathV7.Transform(math, frame, current);
            var centre = (SketchPoint)circle.GetCenterPoint2();
            var target = SketchMutationMathV7.Transform(math, frame, recipe.Expected[0][0]);
            // Calibrate the API's angular zero against its live first point rather than
            // assuming it equals sketch +X for every circle/reference-plane convention.
            double zero = Math.Atan2(currentLocal[1] - centre.Y, currentLocal[0] - centre.X) - h.StartingAngle;
            double angle = Math.Atan2(target[1] - centre.Y, target[0] - centre.X) - zero;
            h.StartingAngle = (angle % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
            h.ReverseDirection = recipe.Reverse ^ (axisDot < 0);
            h.Clockwise = recipe.Clockwise ^ (x[0] * y[1] - x[1] * y[0] < 0);
            if (!feature.ModifyDefinition(h, model, null)) throw new InvalidOperationException("HELIX45 native update rejected.");
            model.EditRebuild3();
            EnsureFeatureHasNoError(feature, "HELIX45 rebuild");
            VerifyHelix45(context, recipe);
            MirrorV7Diagnostics.Log("[HELIX45][PASS] feature=" + recipe.Name + " method=FRAME_AND_LIVE_ANGLE_CALIBRATION");
        }

        private static void ReplayChiral45(MirrorInPlaceExecutionContextV7 context, FeatureReplayCheckpointV7 item)
        {
            var model = context.WorkingDocument;
            var plane = new ADDIN.Commands.PlaneData { Origin = context.Reflection.Origin, Normal = context.Reflection.Normal };
            foreach (var snap in item.Chiral.Sketches)
            {
                // Shared profiles are reflected from their original snapshot, never toggled twice.
                try { VerifyChiralSketch45(context, snap); continue; }
                catch (InvalidOperationException) { }
                var owner = (Feature)((PartDoc)model).FeatureByName(snap.Name);
                if (snap.Is3D)
                    SketchOperationsHelper.Reflect3DSketch45(model, owner, snap.Points, plane);
                else
                {
                    SketchOperationsHelper.EnsureReflectedSupport20(context.SwApp, model, owner, snap.Points, snap.Support, plane);
                    var sketch = SketchOperationsHelper.FreeSketchForMutation(model, owner, true);
                    try { SketchOperationsHelper.MutateSketchPoints(model, sketch, snap.Points, plane); }
                    finally { if (model.SketchManager.ActiveSketch != null) model.SketchManager.InsertSketch(false); }
                }
                VerifyChiralSketch45(context, snap);
                MirrorV7Diagnostics.Log("[CHIRAL45][SKETCH_PASS] feature=" + item.Feature.Name + " sketch=" + snap.Name + " is3D=" + snap.Is3D);
            }
            foreach (var helix in item.Chiral.Helices) ReplayHelix45(context, helix);
            var feature = item.Feature.Feature;
            MoveRollbackAfter(model, feature);
            object data = feature.GetDefinition();
            bool opened = AccessDefinition(data, model);
            if (!opened) throw new InvalidOperationException("CHIRAL45 cannot access target definition.");
            try
            {
                var sweep = data as ISweepFeatureData;
                if (sweep != null && (item.Chiral.Twist == 8 || item.Chiral.Twist == 9))
                {
                    // Ordered path preserved above. A reflection reverses handedness exactly once.
                    sweep.D1ReverseTwistDir = !item.Chiral.D1Reverse;
                    if (item.Chiral.Direction == 1) sweep.D2ReverseTwistDir = !item.Chiral.D2Reverse;
                }
                // Profiles, paths, guide order and loft connector identities stay native.
                if (!feature.ModifyDefinition(data, model, null)) throw new InvalidOperationException("CHIRAL45 ModifyDefinition rejected.");
                opened = false;
            }
            finally { if (opened) ReleaseDefinition(data); }
            MoveRollbackAfter(model, feature);
            model.EditRebuild3();
            EnsureFeatureHasNoError(feature, "CHIRAL45 native rebuild");
            var sweepReadback45 = feature.GetDefinition() as ISweepFeatureData;
            if (sweepReadback45 != null && (item.Chiral.Twist == 8 || item.Chiral.Twist == 9))
            {
                if (sweepReadback45.D1ReverseTwistDir != !item.Chiral.D1Reverse ||
                    (item.Chiral.Direction == 1 && sweepReadback45.D2ReverseTwistDir != !item.Chiral.D2Reverse))
                    throw new InvalidOperationException("CHIRAL45 native twist direction readback mismatch.");
            }
            foreach (var snap in item.Chiral.Sketches) VerifyChiralSketch45(context, snap);
            foreach (var helix in item.Chiral.Helices) VerifyHelix45(context, helix);
            VerifyOptions27(item);
            BaseSketchMutationEngineV7.VerifyCurrentSolids(context, item.ReflectedBodyOracle);
            item.Result = new MirrorV7FeatureResult { FeatureName = item.Feature.Name, FeatureType = item.Feature.TypeName,
                Status = MirrorV7ReplayStatus.ExactReplay, Message = "CHIRAL45 native feature, ordered reflected sketches and solid oracle PASS." };
            MirrorV7Diagnostics.Log("[CHIRAL45][PASS] feature=" + feature.Name);
        }
    }
}

namespace ADDIN.Commands
{
    public sealed class ChiralFeatureMirrorHandler45 : IFeatureMirrorHandler
    {
        public bool CanHandle(PostBaseFeatureInfo info)
        { return info != null && info.Feature != null && MirrorV7.MirrorInPlace.RollbackReplayEngineV7.IsChiral45(info.Feature.GetDefinition()); }
        public FeatureReplayResult Replay(ISldWorks app, ModelDoc2 model, PostBaseFeatureInfo info,
            PlaneData plane, FeatureBodyState cache, string baseFeature, string baseSketch)
        {
            return new FeatureReplayResult { Success = false, FeatureName = info.Name,
                StatusCode = "CHIRAL45_MAPPED_CONTEXT_REQUIRED",
                Message = "Sweep/Loft requires captured source sketches and a reflected geometry oracle; legacy fallback is disabled." };
        }
    }
}

namespace ADDIN.Helpers
{
    public static partial class SketchOperationsHelper
    {
        internal static void Reflect3DSketch45(ModelDoc2 model, Feature owner,
            List<SketchPointSnapshot> source, ADDIN.Commands.PlaneData plane)
        {
            var sketch = owner.GetSpecificFeature2() as Sketch;
            if (sketch == null || !sketch.Is3D()) throw new InvalidOperationException("CHIRAL45 expected a 3D sketch.");
            var math = (IMathUtility)SwAddin.InstanceSwApp.GetMathUtility();
            var frame = (MathTransform)sketch.ModelToSketchTransform;
            var points = EnumerateSketchPoints28(sketch).Cast<SketchPoint>().ToArray();
            var targets = source.Select(p =>
            {
                var matches = points.Where(q => { var id = (int[])q.GetID(); return id[0] == p.Id1 && id[1] == p.Id2; }).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("CHIRAL45 ambiguous 3D point identity.");
                double[] position = { p.ModelX, p.ModelY, p.ModelZ };
                double dot = Enumerable.Range(0, 3).Sum(i => (position[i] - plane.Origin[i]) * plane.Normal[i]);
                var reflected = Enumerable.Range(0, 3).Select(i => position[i] - 2 * dot * plane.Normal[i]).ToArray();
                var local = ADDIN.Commands.MirrorV7.SketchMutationMathV7.Transform(math, frame, reflected);
                return Tuple.Create(matches[0], local);
            }).ToList();
            model.ClearSelection2(true);
            if (!owner.Select2(false, 0)) throw new InvalidOperationException("CHIRAL45 cannot select 3D sketch.");
            model.EditSketch();
            bool automatic = sketch.GetAutomaticSolve();
            try
            {
                if (!object.Equals(model.SketchManager.ActiveSketch, sketch))
                    throw new InvalidOperationException("CHIRAL45 expected 3D sketch is not active.");
                // User permits unconstrained output. Do not delete point/curve identities used by downstream features.
                var relations = sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as Array;
                if (relations != null) foreach (SketchRelation relation in relations) relation.Suppressed = true;
                for (var dd = owner.GetFirstDisplayDimension() as DisplayDimension; dd != null;
                    dd = owner.GetNextDisplayDimension(dd) as DisplayDimension)
                {
                    var dimension = dd.GetDimension2(0) as Dimension;
                    if (dimension != null && dimension.DrivenState == (int)swDimensionDrivenState_e.swDimensionDriving)
                        dimension.DrivenState = (int)swDimensionDrivenState_e.swDimensionDriven;
                }
                sketch.SetAutomaticSolve(false);
                foreach (var target in targets)
                    if (!target.Item1.SetCoords(target.Item2[0], target.Item2[1], target.Item2[2]))
                        throw new InvalidOperationException("CHIRAL45 3D SetCoords rejected.");
                foreach (var target in targets)
                    if (Math.Abs(target.Item1.X - target.Item2[0]) > 1e-7 || Math.Abs(target.Item1.Y - target.Item2[1]) > 1e-7 ||
                        Math.Abs(target.Item1.Z - target.Item2[2]) > 1e-7)
                        throw new InvalidOperationException("CHIRAL45 3D coordinate readback mismatch.");
            }
            finally
            {
                try { sketch.SetAutomaticSolve(automatic); }
                finally
                {
                    if (model.SketchManager.ActiveSketch != null) model.SketchManager.Insert3DSketch(false);
                    model.ClearSelection2(true);
                }
            }
        }
    }
}
