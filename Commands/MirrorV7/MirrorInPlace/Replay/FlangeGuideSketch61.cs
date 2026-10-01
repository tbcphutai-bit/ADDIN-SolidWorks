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
        private sealed class FlangeGuide61
        {
            internal string Name;
            internal FlangeGuideFrame61 Frame;
            internal List<SketchPrimitiveSnapshot58> Profile;
            internal List<double[]> PlanePoints;
        }

        private sealed class GuidePrimitive61
        {
            internal string Id;
            internal SketchPrimitiveSnapshot58 Expected;
            internal int ProfileIndex;
        }

        private static IEnumerable<double[]> GuidePoints61(IEnumerable<SketchPrimitiveSnapshot58> profile)
        {
            return profile.SelectMany(p => new[] { p.StartModel, p.EndModel, p.CenterModel, p.MiddleModel })
                .Where(p => p != null);
        }

        private static SketchPrimitiveSnapshot58 ReflectGuidePrimitive61(
            MirrorInPlaceExecutionContextV7 context, SketchPrimitiveSnapshot58 source)
        {
            Func<double[], double[]> reflect = p => p == null ? null : context.Reflection.ReflectPoint(p);
            return new SketchPrimitiveSnapshot58 { Type = source.Type, Construction = source.Construction,
                Circle = source.Circle, Length = source.Length, StartModel = reflect(source.StartModel),
                EndModel = reflect(source.EndModel), CenterModel = reflect(source.CenterModel),
                MiddleModel = reflect(source.MiddleModel) };
        }

        private static FlangeGuide61 CreateFlangeGuide61(MirrorInPlaceExecutionContextV7 context,
            string featureName, FlangePair33 pair)
        {
            var model = context.WorkingDocument;
            if (model.SketchManager.ActiveSketch != null)
                throw new InvalidOperationException("FLANGE61 guide creation requires a closed sketch.");
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, featureName))
                throw new InvalidOperationException("FLANGE61 cannot position guide before flange.");
            var expected = pair.Profile.Select(p => ReflectGuidePrimitive61(context, p)).ToList();
            // Validate all numeric geometry before inserting anything into staging.
            FlangeGuideFrame61.FromPoints(pair.Start, pair.End, GuidePoints61(expected).ToList(), pair.ReflectedProfileNormal);
            string name = "Mirror3DGuide61_" + Guid.NewGuid().ToString("N");
            model.ClearSelection2(true);
            model.SketchManager.Insert3DSketch(false);
            var sketch = model.SketchManager.ActiveSketch;
            if (sketch == null || !sketch.Is3D())
                throw new InvalidOperationException("FLANGE61 native 3D sketch was not created.");
            var records = new List<GuidePrimitive61>();
            bool oldAdd = model.SketchManager.AddToDB;
            bool oldSolve = sketch.GetAutomaticSolve();
            bool oldRelations = context.SwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations);
            bool oldInference = context.SwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference);
            try
            {
                // Name immediately so even a partially created guide can be inspected.
                var owner = FindSketchOwner28(model, sketch);
                owner.Name = name;
                if (owner.Name != name) throw new InvalidOperationException("FLANGE61 cannot name calibration sketch.");
                MirrorV7Diagnostics.Log("[FLANGE61][GUIDE_CREATED] feature=" + featureName +
                    " sourceProfile=" + pair.SketchName + " guide=" + name);
                context.SwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, false);
                context.SwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, false);
                sketch.SetAutomaticSolve(false);
                model.SketchManager.AddToDB = true;
                for (int i = 0; i < expected.Count; i++)
                {
                    var p = expected[i];
                    if (!p.Circle) records.Add(DrawGuidePrimitive61(context, sketch, p, i));
                    else
                    {
                        // A tilted 3D circle is represented by two exact semicircles;
                        // the final native 2D profile is still one analytic circle.
                        double[] radial = FlangeGuideFrame61.Subtract(p.StartModel, p.CenterModel);
                        double[] quarter = FlangeGuideFrame61.Cross(FlangeGuideFrame61.Unit(pair.ReflectedProfileNormal), radial);
                        double[] opposite = FlangeGuideFrame61.Subtract(p.CenterModel, radial);
                        Func<double[], double[], double[], SketchPrimitiveSnapshot58> half = (a, b, m) =>
                            new SketchPrimitiveSnapshot58 { Type = p.Type, Construction = p.Construction,
                                Length = p.Length / 2, StartModel = a, EndModel = b, CenterModel = p.CenterModel, MiddleModel = m };
                        records.Add(DrawGuidePrimitive61(context, sketch, half(p.StartModel, opposite,
                            Enumerable.Range(0, 3).Select(k => p.CenterModel[k] + quarter[k]).ToArray()), i));
                        records.Add(DrawGuidePrimitive61(context, sketch, half(opposite, p.StartModel,
                            FlangeGuideFrame61.Subtract(p.CenterModel, quarter)), i));
                    }
                }
                records.Add(DrawGuidePrimitive61(context, sketch, new SketchPrimitiveSnapshot58 {
                    Type = (int)swSketchSegments_e.swSketchLINE, Construction = true,
                    StartModel = pair.Start, EndModel = pair.End, Length = Distance(pair.Start, pair.End) }, -1));
            }
            finally
            {
                try { model.SketchManager.AddToDB = oldAdd; }
                finally
                {
                    try { sketch.SetAutomaticSolve(oldSolve); }
                    finally
                    {
                        try { context.SwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, oldRelations); }
                        finally
                        {
                            try { context.SwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, oldInference); }
                            finally
                            {
                                if (model.SketchManager.ActiveSketch != null) model.SketchManager.Insert3DSketch(false);
                                model.ClearSelection2(true);
                            }
                        }
                    }
                }
            }
            // Reacquire after closing the command. Do not retain native segment
            // pointers across exit/rebuild/rollback operations.
            var liveFeature = ((PartDoc)model).FeatureByName(name) as Feature;
            var liveSketch = liveFeature == null ? null : liveFeature.GetSpecificFeature2() as Sketch;
            if (liveSketch == null) throw new InvalidOperationException("FLANGE61 guide disappeared after closing sketch.");
            var liveSegments = (liveSketch.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>().ToList();
            if (liveSegments.Count != records.Count)
                throw new InvalidOperationException("FLANGE61 guide segment count changed after sketch exit.");
            var actual = records.Select(r => ReadGuidePrimitive61(context, liveSketch,
                liveSegments.Single(s => SegmentKey45(s) == r.Id), r.Expected)).ToList();
            var profile = new List<SketchPrimitiveSnapshot58>();
            for (int i = 0; i < expected.Count; i++)
            {
                var indices = Enumerable.Range(0, records.Count).Where(k => records[k].ProfileIndex == i).ToArray();
                var p = actual[indices[0]];
                if (expected[i].Circle)
                    p = new SketchPrimitiveSnapshot58 { Type = p.Type, Construction = p.Construction, Circle = true,
                        Length = indices.Sum(k => actual[k].Length), StartModel = p.StartModel, CenterModel = p.CenterModel };
                profile.Add(p);
            }
            var hinge = actual[records.FindIndex(r => r.ProfileIndex == -1)];
            // Include circle quarter points too: never derive a plane from the
            // centre and a single radius, which are collinear.
            var framePoints = GuidePoints61(actual.Take(actual.Count - 1)).ToList();
            var frame = FlangeGuideFrame61.FromPoints(hinge.StartModel, hinge.EndModel, framePoints, pair.ReflectedProfileNormal);
            MirrorV7Diagnostics.Log("[FLANGE61][GUIDE_PASS] guide=" + name + " primitives=" + profile.Count +
                " hinge=" + string.Join(",", frame.Hinge) + " outward=" + string.Join(",", frame.Outward) +
                " normal=" + string.Join(",", frame.Normal) + " planeResidual_m=" + frame.PlaneResidual +
                " hingePlaneOffset_m=" + frame.HingePlaneOffset + " reflectionApplied=ONCE");
            return new FlangeGuide61 { Name = name, Frame = frame, Profile = profile, PlanePoints = framePoints };
        }

        private static GuidePrimitive61 DrawGuidePrimitive61(MirrorInPlaceExecutionContextV7 context,
            Sketch sketch, SketchPrimitiveSnapshot58 p, int index)
        {
            var manager = context.WorkingDocument.SketchManager;
            var math = (IMathUtility)context.SwApp.GetMathUtility();
            var frame = (MathTransform)sketch.ModelToSketchTransform;
            Func<double[], double[]> local = v => SketchMutationMathV7.Transform(math, frame, v);
            double[] a = local(p.StartModel), b = local(p.EndModel);
            SketchSegment segment;
            if (p.Type == (int)swSketchSegments_e.swSketchLINE)
                segment = manager.CreateLine(a[0], a[1], a[2], b[0], b[1], b[2]);
            else if (p.Type == (int)swSketchSegments_e.swSketchARC && !p.Circle)
            {
                double[] m = local(p.MiddleModel);
                segment = manager.Create3PointArc(a[0], a[1], a[2], b[0], b[1], b[2], m[0], m[1], m[2]);
            }
            else throw new InvalidOperationException("FLANGE61 unsupported guide primitive: " + p.Type);
            if (segment == null) throw new InvalidOperationException("FLANGE61 3D primitive creation failed at " + index);
            segment.ConstructionGeometry = p.Construction;
            // Native return values, not API invocation success, prove the position.
            ReadGuidePrimitive61(context, sketch, segment, p);
            return new GuidePrimitive61 { Id = SegmentKey45(segment), Expected = p, ProfileIndex = index };
        }

        private static SketchPrimitiveSnapshot58 ReadGuidePrimitive61(MirrorInPlaceExecutionContextV7 context,
            Sketch sketch, SketchSegment segment, SketchPrimitiveSnapshot58 expected)
        {
            var math = (IMathUtility)context.SwApp.GetMathUtility();
            var inverse = (MathTransform)((MathTransform)sketch.ModelToSketchTransform).Inverse();
            Func<SketchPoint, double[]> position = p => p == null ? null :
                SketchMutationMathV7.Transform(math, inverse, new[] { p.X, p.Y, p.Z });
            var actual = new SketchPrimitiveSnapshot58 { Type = segment.GetType(),
                Construction = segment.ConstructionGeometry, Length = segment.GetLength() };
            if (actual.Type == (int)swSketchSegments_e.swSketchLINE)
            {
                var line = (ISketchLine)segment;
                actual.StartModel = position(line.GetStartPoint2() as SketchPoint);
                actual.EndModel = position(line.GetEndPoint2() as SketchPoint);
            }
            else if (actual.Type == (int)swSketchSegments_e.swSketchARC)
            {
                var arc = (ISketchArc)segment;
                actual.StartModel = position(arc.GetStartPoint2() as SketchPoint);
                actual.EndModel = position(arc.GetEndPoint2() as SketchPoint);
                actual.CenterModel = position(arc.GetCenterPoint2() as SketchPoint);
                actual.MiddleModel = CurveSamples45(context, sketch, segment)[16];
            }
            else throw new InvalidOperationException("FLANGE61 native guide type changed.");
            double direct = Math.Max(Distance(actual.StartModel, expected.StartModel), Distance(actual.EndModel, expected.EndModel));
            double reversed = Math.Max(Distance(actual.StartModel, expected.EndModel), Distance(actual.EndModel, expected.StartModel));
            if (reversed < direct)
            { var swap = actual.StartModel; actual.StartModel = actual.EndModel; actual.EndModel = swap; }
            double centerResidual = expected.CenterModel == null ? 0 :
                actual.CenterModel == null ? double.PositiveInfinity : Distance(actual.CenterModel, expected.CenterModel);
            double middleResidual = expected.MiddleModel == null ? 0 :
                actual.MiddleModel == null ? double.PositiveInfinity : Distance(actual.MiddleModel, expected.MiddleModel);
            if (actual.Type != expected.Type || actual.Construction != expected.Construction ||
                Math.Min(direct, reversed) > 1e-7 || Math.Abs(actual.Length - expected.Length) > 1e-7 ||
                centerResidual > 1e-7 || middleResidual > 1e-7)
                throw new InvalidOperationException("FLANGE61 native 3D primitive differs from reflected source: type=" + actual.Type +
                    " endpoints_m=" + Math.Min(direct, reversed) + " lengthDelta_m=" + (actual.Length - expected.Length) +
                    " center_m=" + centerResidual + " midpoint_m=" + middleResidual);
            return actual;
        }

        private static void RemoveFlangeGuides61(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayCheckpointV7 item, IList<FlangeGuide61> guides)
        {
            var model = context.WorkingDocument;
            foreach (var guide in guides)
            {
                var feature = ((PartDoc)model).FeatureByName(guide.Name) as Feature;
                if (feature == null) throw new InvalidOperationException("FLANGE61 calibration guide disappeared before cleanup.");
                // Numeric redraw creates no guide references. Refuse cascading deletion.
                if ((feature.GetChildren() as object[] ?? new object[0]).OfType<Feature>().Any())
                    throw new InvalidOperationException("FLANGE61 guide acquired dependencies; refuse deletion: " + guide.Name);
                DeleteTemporaryFlangeProfile60(model, feature);
                MirrorV7Diagnostics.Log("[FLANGE61][GUIDE_REMOVED] guide=" + guide.Name);
            }
            MoveRollbackAfter(model, item.Feature.Feature);
            model.EditRebuild3();
            EnsureFeatureHasNoError(item.Feature.Feature, "FLANGE61 cleanup rebuild");
            BaseSketchMutationEngineV7.VerifyCurrentSolids(context, item.ReflectedBodyOracle);
            MirrorV7Diagnostics.Log("[FLANGE61][CLEANUP_PASS] feature=" + item.Feature.Name +
                " guides=" + guides.Count + " bodyUnchanged=True");
        }

        private static void RequireCommonFlangeAngle61(IList<double> angles)
        {
            if (angles.Count == 0 || angles.Any(a => Math.Abs(Math.Cos(a) - Math.Cos(angles[0])) > 1e-8 ||
                Math.Abs(Math.Sin(a) - Math.Sin(angles[0])) > 1e-8))
                throw new InvalidOperationException("FLANGE61 calibrated profile planes require different native bend angles; no angle is averaged/guessed.");
        }

        private static void VerifyFinalFlangeProfiles61(MirrorInPlaceExecutionContextV7 context,
            IList<string> names, IList<FlangeGuide61> guides)
        {
            if (names.Count != guides.Count) throw new InvalidOperationException("FLANGE61 profile/guide correspondence lost.");
            var math = (IMathUtility)context.SwApp.GetMathUtility();
            for (int i = 0; i < names.Count; i++)
            {
                var feature = ((PartDoc)context.WorkingDocument).FeatureByName(names[i]) as Feature;
                var sketch = feature == null ? null : feature.GetSpecificFeature2() as Sketch;
                if (sketch == null) throw new InvalidOperationException("FLANGE61 final native profile missing: " + names[i]);
                var frame = (MathTransform)sketch.ModelToSketchTransform;
                var heights = guides[i].PlanePoints.Select(p => SketchMutationMathV7.Transform(math, frame, p)[2]).ToList();
                if (heights.Count == 0 || heights.Max() - heights.Min() > 1e-7)
                    throw new InvalidOperationException("FLANGE61 profile plane changed after native ModifyDefinition: " + names[i]);
                VerifyProfileAgainstGuide61(context, names[i], guides[i], heights.Average());
            }
            MirrorV7Diagnostics.Log("[FLANGE61][FINAL_PROFILE_PASS] profiles=" + names.Count + " phase=AFTER_NATIVE_REBUILD");
        }

        // CreateFeature may replace the input sketches with different native identities,
        // and apply its length dimension to their geometry. Resolve the actual drivers
        // after committing native options; never infer them from sketch labels/order.
        private static void ReacquireAndRedrawNativeFlangeProfiles64(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayCheckpointV7 item, FlangeRecipe33 recipe, IList<string> names, IList<FlangeGuide61> guides)
        {
            var model = context.WorkingDocument;
            if (model.SketchManager.ActiveSketch != null || names.Count != recipe.Pairs.Count ||
                guides.Count != recipe.Pairs.Count)
                throw new InvalidOperationException("FLANGE64 native profile recovery requires closed, paired sketches.");
            var layout = FeatureTreeScannerV7.Scan(model);
            var profiles = layout.Nodes.Where(n => n.Role == MirrorV7FeatureRole.Sketch).Select(n => n.Feature).Where(f =>
            {
                var sketch = f.GetSpecificFeature2() as Sketch;
                var owner = sketch == null ? null : f.GetOwnerFeature() as Feature;
                return sketch != null && !sketch.Is3D() && owner != null && SameCom28(owner, item.Feature.Feature);
            }).ToList();
            if (profiles.Count != recipe.Pairs.Count)
                throw new InvalidOperationException("FLANGE64 regenerated driver count differs from captured edge/profile pairs.");
            var math = (IMathUtility)context.SwApp.GetMathUtility();
            var sketches = profiles.Select(f => (Sketch)f.GetSpecificFeature2()).ToList();
            var primitives = sketches.Select(SketchOperationsHelper.CapturePristineSketchPrimitives58).ToList();
            var references = profiles.Select((f, i) => CaptureProfileReferences34(sketches[i], f.Name)).ToList();
            var candidates = new List<int[]>();
            for (int i = 0; i < recipe.Pairs.Count; i++)
            {
                var pair = recipe.Pairs[i];
                int[] byReference = Enumerable.Range(0, profiles.Count).Where(j => references[j].Any(r =>
                    BipartiteEquivalenceMatcherV7.SameEdgeSamples(r, pair.Edge, 1e-7))).ToArray();
                int[] byHinge = Enumerable.Range(0, profiles.Count).Where(j => primitives[j].Any(p =>
                    !p.Construction && p.Type == (int)swSketchSegments_e.swSketchLINE &&
                    p.StartModel != null && p.EndModel != null &&
                    FlangeGeometry33.OverlapsHinge(p.StartModel, p.EndModel, pair.Start, pair.End, 1e-7))).ToArray();
                int[] byFrame = Enumerable.Range(0, profiles.Count).Where(j =>
                    Math.Abs(Dot60(ProfileAxis60(math, sketches[j].ModelToSketchTransform as MathTransform, 0),
                        guides[i].Frame.Hinge)) >= 1 - 1e-8).ToArray();
                candidates.Add(byReference.Length > 0 ? byReference : byHinge.Length > 0 ? byHinge : byFrame);
            }
            int[] assignment = FlangeGeometry33.UniqueAssignment(candidates, profiles.Count);
            if (assignment == null)
                throw new InvalidOperationException("FLANGE64 native driver/hinge correspondence missing or ambiguous; no profile is chosen by name/order.");
            // Freeze only persistent identity, not live Sketch/Feature COM handles.
            // Exiting the first profile may invalidate the next cached sketch even
            // though its native feature remains in the model. Resolve each edit fresh.
            var driverReferences65 = assignment.Select(index => PersistentReferenceServiceV7.Capture(model,
                profiles[index], item.Feature.Name, "FLANGE_NATIVE_DRIVER_TRANSACTION65")).ToArray();
            if (driverReferences65.Any(reference => reference == null) ||
                driverReferences65.Select(reference => Convert.ToBase64String(reference.Data)).Distinct().Count() != assignment.Length)
                throw new InvalidOperationException("FLANGE65 native driver identities are unavailable or duplicated; no profile edit started.");
            var plane = new ADDIN.Commands.PlaneData { Origin = context.Reflection.Origin, Normal = context.Reflection.Normal };
            for (int i = 0; i < assignment.Length; i++)
            {
                // No native flange/body regeneration while its drivers are replaced.
                if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, item.Feature.Name))
                    throw new InvalidOperationException("FLANGE65 cannot roll back the owner before editing its native driver.");
                MirrorV7Diagnostics.Log("[FLANGE65][ROLLBACK_BEFORE_OWNER] feature=" + item.Feature.Name + " hinge=" + i);
                var profile = ResolveNativeFlangeDriver65(context, item, driverReferences65[i]);
                var sketch = profile.GetSpecificFeature2() as Sketch;
                if (sketch == null) throw new InvalidOperationException("FLANGE65 resolved driver is not a sketch.");
                string previous = names[i];
                names[i] = profile.Name;
                double offset;
                model.ClearSelection2(true);
                if (!profile.Select2(false, 0)) throw new InvalidOperationException("FLANGE64 native driver selection failed.");
                MirrorV7Diagnostics.Log("[FLANGE65][EDIT_BEGIN] profile=" + names[i] + " hinge=" + i);
                model.EditSketch();
                try
                {
                    var editing65 = model.SketchManager.ActiveSketch;
                    if (editing65 == null || !SameCom28(editing65, sketch))
                        throw new InvalidOperationException("FLANGE64 edit entered an unrelated sketch.");
                    var frame = editing65.ModelToSketchTransform as MathTransform;
                    var heights = guides[i].PlanePoints.Select(p => SketchMutationMathV7.Transform(math, frame, p)[2]).ToList();
                    if (heights.Count == 0 || heights.Max() - heights.Min() > 1e-7)
                        throw new InvalidOperationException("FLANGE64 native support plane is tilted from the reflected 3D guide.");
                    offset = heights.Average();
                    PrepareRecreatedFlangeProfile66(context, item, driverReferences65[i], editing65);
                    SketchOperationsHelper.RecreateMirroredPrimitives58(model, editing65, guides[i].Profile,
                        plane, true, offset, alreadyReflected: true, deferOwnerRebuild65: true);
                }
                finally
                {
                    if (model.SketchManager.ActiveSketch != null) model.SketchManager.InsertSketch(false);
                    model.ClearSelection2(true);
                }
                var committed65 = ResolveNativeFlangeDriver65(context, item, driverReferences65[i]);
                names[i] = committed65.Name;
                RequireHealthyNativeFlange66(committed65, "PROFILE_EXIT");
                VerifyProfileAgainstGuide61(context, names[i], guides[i], offset);
                MirrorV7Diagnostics.Log("[FLANGE64][NATIVE_DRIVER_REDRAW] source=" + recipe.Pairs[i].SketchName +
                    " consumedInput=" + previous + " actual=" + names[i] + " hinge=" + i +
                    " method=UNIQUE_NATIVE_HINGE_BINDING_AND_3D_GUIDE bodyVerificationPending=True");
            }
            MirrorV7Diagnostics.Log("[FLANGE65][OWNER_COMMIT_BEGIN] feature=" + item.Feature.Name +
                " profiles=" + names.Count + " activeSketch=False");
            item.Feature.Feature = ((PartDoc)model).FeatureByName(item.Feature.Name) as Feature ??
                throw new InvalidOperationException("FLANGE65 native owner disappeared before commit.");
            MoveRollbackAfter(model, item.Feature.Feature);
            // Moving the bar may already rebuild the owner. A second rebuild,
            // body read or direction attempt is not safe after a native error.
            RequireHealthyNativeFlange66(item.Feature.Feature, "OWNER_ROLLFORWARD");
            model.EditRebuild3();
            RequireHealthyNativeFlange66(item.Feature.Feature, "OWNER_REBUILD");
            for (int i = 0; i < names.Count; i++)
                names[i] = ResolveNativeFlangeDriver65(context, item, driverReferences65[i]).Name;
            MirrorV7Diagnostics.Log("[FLANGE65][OWNER_COMMIT_END] feature=" + item.Feature.Name +
                " bodyVerificationPending=True");
        }

        // These sketches were just created in staging, not copied user sketches.
        // Their auto-generated dimensions/USEEDGE relations refer to the segments
        // that are about to be removed. Deleting those segments first left error
        // 51 (swSketchErrorExtRefFail) in the SW2024 runtime. Remove the disposable
        // constraints first; attachment remains the typed native edge/profile pair.
        private static void PrepareRecreatedFlangeProfile66(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayCheckpointV7 item, PersistReferenceV7 reference, Sketch editing)
        {
            var model = context.WorkingDocument;
            var profile = ResolveNativeFlangeDriver65(context, item, reference);
            if (!item.WasNativeReplacement || editing == null ||
                !SameCom28(model.SketchManager.ActiveSketch, editing) ||
                !SameCom28(profile.GetSpecificFeature2(), editing))
                throw new System.IO.InvalidDataException("FLANGE66 constraint cleanup is restricted to a newly recreated, active native driver.");
            var manager = editing.RelationManager;
            int before = manager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll);
            MirrorV7Diagnostics.Log("[FLANGE66][DETACH_BEGIN] profile=" + profile.Name + " relations=" + before);
            if (before > 0 && !manager.DeleteAllRelations())
                throw new System.IO.InvalidDataException("FLANGE66 could not remove disposable native profile relations; segments retained.");
            if (manager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll) != 0)
                throw new System.IO.InvalidDataException("FLANGE66 native profile relations remain; segments retained.");

            // Logical relation deletion does not guarantee dimension deletion.
            // Resolve the feature/first dimension afresh after every deletion;
            // never retain annotation handles across another native deletion.
            var removed = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                profile = ResolveNativeFlangeDriver65(context, item, reference);
                var display = profile.GetFirstDisplayDimension() as DisplayDimension;
                if (display == null) break;
                var dimension = display.GetDimension2(0) as Dimension;
                var annotation = display.GetAnnotation() as Annotation;
                if (dimension == null || annotation == null || !removed.Add(dimension.FullName))
                    throw new System.IO.InvalidDataException("FLANGE66 disposable dimension identity is missing/repeated; no segment deletion attempted.");
                model.ClearSelection2(true);
                if (!annotation.Select3(false, null) || !model.Extension.DeleteSelection2(0))
                    throw new System.IO.InvalidDataException("FLANGE66 could not delete a disposable profile dimension; segments retained.");
                model.ClearSelection2(true);
            }
            MirrorV7Diagnostics.Log("[FLANGE66][DETACH_END] profile=" + profile.Name +
                " relationsAfter=0 dimensionsRemoved=" + removed.Count + " sourceConstraintsUntouched=True");
        }

        private static void RequireHealthyNativeFlange66(Feature feature, string stage)
        {
            if (feature == null)
                throw new System.IO.InvalidDataException("FLANGE66 native feature missing at " + stage + "; no further native operation attempted.");
            bool warning;
            int error = feature.GetErrorCode2(out warning);
            if (error == 0 || warning) return;
            string message = "FLANGE66 native error at " + stage + " feature=" + feature.Name +
                " errorCode=" + error + " errorName=" + ((swFeatureError_e)error) +
                "; remaining direction candidates, rebuilds and body reads not attempted.";
            MirrorV7Diagnostics.Log("[FLANGE66][NATIVE_STOP] " + message);
            // Not a geometric mismatch. Do not let the direction search catch it.
            throw new System.IO.InvalidDataException(message);
        }

        internal static bool IsUnsafeNativeFailure66(Exception failure)
        {
            // ExecuteAll can wrap a native error in an InvalidOperationException.
            // Cleanup must inspect the cause, not just the outer exception type.
            for (var cause = failure; cause != null; cause = cause.InnerException)
                if (cause is System.IO.InvalidDataException ||
                    cause is System.Runtime.InteropServices.COMException ||
                    cause is AccessViolationException ||
                    cause is System.Runtime.InteropServices.SEHException) return true;
            return false;
        }

        private static Feature ResolveNativeFlangeDriver65(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayCheckpointV7 item, PersistReferenceV7 reference)
        {
            int state;
            var feature = PersistentReferenceServiceV7.Resolve(context.WorkingDocument, reference, out state) as Feature;
            var sketch = feature == null ? null : feature.GetSpecificFeature2() as Sketch;
            var owner = feature == null ? null : feature.GetOwnerFeature() as Feature;
            var liveOwner = ((PartDoc)context.WorkingDocument).FeatureByName(item.Feature.Name) as Feature;
            if (state != 0 || sketch == null || sketch.Is3D() || owner == null || liveOwner == null ||
                !SameCom28(owner, liveOwner))
                throw new InvalidOperationException("FLANGE65 native driver identity/owner no longer resolves; state=" + state +
                    "; no stale sketch is reused.");
            return feature;
        }

        private static void VerifyProfileAgainstGuide61(MirrorInPlaceExecutionContextV7 context,
            string profileName, FlangeGuide61 guide, double normalOffset)
        {
            var feature = ((PartDoc)context.WorkingDocument).FeatureByName(profileName) as Feature;
            var sketch = feature == null ? null : feature.GetSpecificFeature2() as Sketch;
            if (sketch == null) throw new InvalidOperationException("FLANGE61 final 2D sketch disappeared.");
            var math = (IMathUtility)context.SwApp.GetMathUtility();
            var nativeNormal = ProfileAxis60(math, (MathTransform)sketch.ModelToSketchTransform, 2);
            Func<double[], double[]> shift = p => p == null ? null :
                Enumerable.Range(0, 3).Select(k => p[k] - normalOffset * nativeNormal[k]).ToArray();
            var expected = guide.Profile.Select(p => new SketchPrimitiveSnapshot58 {
                Type = p.Type, Construction = p.Construction, Circle = p.Circle, Length = p.Length,
                StartModel = shift(p.StartModel), EndModel = shift(p.EndModel),
                CenterModel = shift(p.CenterModel), MiddleModel = shift(p.MiddleModel) }).ToList();
            var actual = SketchOperationsHelper.CapturePristineSketchPrimitives58(sketch);
            if (expected.Count != actual.Count || !BipartiteEquivalenceMatcherV7.Match(expected, actual,
                SameGuidePrimitive61).HasPerfectMatching)
                throw new InvalidOperationException("FLANGE61 final 2D sketch does not reproduce the 3D guide geometry.");
            double[] nativeY = ProfileAxis60(math, (MathTransform)sketch.ModelToSketchTransform, 1);
            double directedSide = Dot60(nativeY, guide.Frame.Outward);
            // Local axes are a coordinate convention, not the drawn side. A
            // -Y frame with negative local profile coordinates can reproduce
            // exactly the same world-space guide. The primitive comparison
            // above proves the actual side; the body oracle remains mandatory.
            // Never use a basis-sign check instead of either geometry test.
            MirrorV7Diagnostics.Log("[FLANGE61][PROFILE_2D_PASS] profile=" + profileName + " guide=" + guide.Name +
                " primitives=" + actual.Count + " normalTranslation_m=" + normalOffset +
                " drawingSideDotY=" + directedSide + " tolerance_m=1E-7" +
                " method=GUIDE_MODEL_TO_NATIVE_SKETCH basisSignIsNotGeometry=True bodyVerificationPending=True");
        }

        private static bool SameGuidePrimitive61(SketchPrimitiveSnapshot58 a, SketchPrimitiveSnapshot58 b)
        {
            if (a.Type != b.Type || a.Circle != b.Circle || a.Construction != b.Construction ||
                Math.Abs(a.Length - b.Length) > 1e-7) return false;
            Func<double[], double[], bool> same = (p, q) => p == null || q == null ? p == q : Distance(p, q) <= 1e-7;
            if (!same(a.CenterModel, b.CenterModel) || !same(a.MiddleModel, b.MiddleModel)) return false;
            if (a.Circle) return true; // centre + circumference prove radius, independent of seam.
            return (same(a.StartModel, b.StartModel) && same(a.EndModel, b.EndModel)) ||
                (same(a.StartModel, b.EndModel) && same(a.EndModel, b.StartModel));
        }
    }
}
