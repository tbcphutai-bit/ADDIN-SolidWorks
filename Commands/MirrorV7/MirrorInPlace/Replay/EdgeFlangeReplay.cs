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
        internal sealed class FlangeRecipe33
        {
            internal double Angle;
            internal double PositionOffsetDistance;
            internal int OffsetType;
            internal bool ReverseOffset, ReversePositionOffset, UsePositionOffset, LockAngle;
            internal readonly List<FlangePair33> Pairs = new List<FlangePair33>();
            internal readonly List<FlangeBend52> Bends = new List<FlangeBend52>();
        }

        internal sealed class FlangeBend52
        {
            internal int SourceOrder;
            internal List<FlangeSupport33> Faces;
        }

        private static List<FlangeSupport33> CaptureBendFaces52(Feature feature, Func<double[], double[]> transform)
        {
            return (feature.GetFaces() as object[] ?? new object[0]).Cast<Face2>()
                .Select(face => new FlangeSupport33 { Area = face.GetArea(),
                    Boundaries = (face.GetEdges() as object[] ?? new object[0]).Cast<Edge>()
                        .Select(edge => SampleEdge(edge).Select(transform).ToArray()).ToArray() }).ToList();
        }

        private static bool SameBendFaces52(List<FlangeSupport33> expected, List<FlangeSupport33> actual)
        {
            return expected.Count > 0 && actual.Count > 0 &&
                BipartiteEquivalenceMatcherV7.Match(expected, actual, (a, b) =>
                    a.Boundaries.Length > 0 && b.Boundaries.Length > 0 &&
                    BipartiteEquivalenceMatcherV7.Match(a.Boundaries.ToList(), b.Boundaries.ToList(),
                        (x, y) => BipartiteEquivalenceMatcherV7.SameEdgeSamples(x, y, 1e-7)).HasPerfectMatching)
                    .HasPerfectMatching;
        }

        internal sealed class FlangePair33
        {
            internal string SketchName;
            internal double[][] Edge;
            internal double[] Start, End;
            internal double[] ReflectedProfileX;
            internal double[] ReflectedProfileNormal;
            internal List<SketchPrimitiveSnapshot58> Profile;
            internal readonly List<FlangeSupport33> Supports = new List<FlangeSupport33>();
        }

        internal sealed class FlangeSupport33
        {
            internal double Area;
            internal double[][][] Boundaries;
        }

        private static FlangeRecipe33 CaptureFlange33(MirrorInPlaceExecutionContextV7 context, Feature feature)
        {
            var sourceOwner52 = context.SourceGraph.Nodes.Single(n => SameCom28(n.Feature, feature));
            var bends52 = context.SourceGraph.Nodes.Skip(sourceOwner52.TreeOrder + 1)
                .TakeWhile(n => n.Depth > sourceOwner52.Depth)
                .Where(n => n.TypeName == "OneBend" && n.Depth == sourceOwner52.Depth + 1)
                .Select(n => new FlangeBend52 { SourceOrder = n.TreeOrder,
                    Faces = CaptureBendFaces52(n.Feature, context.Reflection.ReflectPoint) }).ToList();
            // Snapshot all absorbed profiles before AccessSelections changes rollback state.
            var profiles = new Dictionary<string, List<SketchPrimitiveSnapshot58>>();
            var visited = new HashSet<string>();
            CaptureFlangeProfiles33(feature, profiles, visited);
            var references = new Dictionary<string, List<double[][]>>();
            var frames = new Dictionary<string, MathTransform>();
            foreach (var name in profiles.Keys)
            {
                var owner = ((PartDoc)context.WorkingDocument).FeatureByName(name) as Feature;
                var sketch = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
                references[name] = CaptureProfileReferences34(sketch, name);
                frames[name] = sketch.ModelToSketchTransform as MathTransform;
                if (frames[name] == null)
                    throw new InvalidOperationException("FLANGE33 source profile has no sketch frame: " + name);
            }
            var data = (IEdgeFlangeFeatureData)feature.GetDefinition();
            if (!data.AccessSelections(context.WorkingDocument, null))
                throw new InvalidOperationException("FLANGE33 cannot access original attachments: " + feature.Name);
            try
            {
                var recipe = new FlangeRecipe33 { Angle = data.BendAngle,
                    PositionOffsetDistance = data.PositionOffsetDistance, OffsetType = data.OffsetType,
                    ReverseOffset = data.ReverseOffset,
                    ReversePositionOffset = data.ReversePositionOffset, LockAngle = data.LockAngle };
                recipe.UsePositionOffset = data.UsePositionOffset;
                recipe.Bends.AddRange(bends52);
                var edges = (data.Edges as Array ?? new object[0]).Cast<object>().Select(e => e as Edge).ToList();
                if (edges.Count == 0 || edges.Any(e => e == null))
                    throw new InvalidOperationException("FLANGE33 missing source attachments.");
                var names = profiles.Keys.ToList();
                var sourceSamples = edges.Select(SampleEdge).ToList();
                // Direct references and an overlapping hinge are strongest evidence.
                // Offset native flange profiles need not touch the hinge; their sketch
                // X axis is aligned with the selected straight edge. Use that frame
                // relationship only if it gives a unique global one-to-one mapping.
                var compatible = new List<int[]>();
                var math = (IMathUtility)context.SwApp.GetMathUtility();
                for (int i = 0; i < edges.Count; i++)
                {
                    var a = ((Vertex)edges[i].GetStartVertex()).GetPoint() as double[];
                    var b = ((Vertex)edges[i].GetEndVertex()).GetPoint() as double[];
                    if (a == null || b == null || Distance(a, b) < 1e-9 ||
                        sourceSamples[i].Any(p => !FlangeGeometry33.OnLine(p, a, b, 1e-7)))
                        throw new InvalidOperationException("FLANGE33 non-straight hinge requires a curved-edge handler.");
                    int[] byReference = Enumerable.Range(0, names.Count).Where(j => references[names[j]].Any(r =>
                        BipartiteEquivalenceMatcherV7.SameEdgeSamples(r, sourceSamples[i], 1e-7))).ToArray();
                    int[] byGeometry = Enumerable.Range(0, names.Count).Where(j => profiles[names[j]].Any(p =>
                        !p.Construction && p.Type == (int)swSketchSegments_e.swSketchLINE &&
                        FlangeGeometry33.OverlapsHinge(p.StartModel, p.EndModel, a, b, 1e-7))).ToArray();
                    double[] alignments = names.Select(name =>
                    {
                        var localA = SketchMutationMathV7.Transform(math, frames[name], a);
                        var localB = SketchMutationMathV7.Transform(math, frames[name], b);
                        return FlangeGeometry33.ProfileAxisAlignment(localA, localB);
                    }).ToArray();
                    int[] byFrame = Enumerable.Range(0, names.Count)
                        .Where(j => alignments[j] >= 0.995).ToArray();
                    compatible.Add(byReference.Length > 0 ? byReference :
                        byGeometry.Length > 0 ? byGeometry : byFrame);
                    MirrorV7Diagnostics.Log("[FLANGE34][PAIR_EVIDENCE] feature=" + feature.Name + " edge=" + i +
                        " start=" + string.Join(",", a) + " end=" + string.Join(",", b) +
                        " referenceProfiles=" + string.Join("|", byReference.Select(j => names[j])) +
                        " coincidentProfiles=" + string.Join("|", byGeometry.Select(j => names[j])) +
                        " frameProfiles=" + string.Join("|", byFrame.Select(j => names[j])) +
                        " frameAlignment=" + string.Join("|", alignments.Select(x => x.ToString("G9"))) +
                        " offsetType=" + data.OffsetType + " positionType=" + data.PositionType +
                        " positionOffset=" + data.PositionOffsetDistance);
                }
                int[] assignment = FlangeGeometry33.UniqueAssignment(compatible, names.Count);
                if (assignment == null)
                    throw new InvalidOperationException("FLANGE33 edge/profile pairing is missing or ambiguous: candidates=" +
                        string.Join(",", compatible.Select(c => c.Length)) + "; no profile is guessed.");
                for (int i = 0; i < edges.Count; i++)
                {
                    var edge = edges[i];
                    var pair = new FlangePair33 { SketchName = names[assignment[i]],
                        ReflectedProfileX = context.Reflection.ReflectVector(ProfileX45(math, frames[names[assignment[i]]])),
                        ReflectedProfileNormal = context.Reflection.ReflectVector(ProfileAxis60(math, frames[names[assignment[i]]], 2)),
                        Profile = profiles[names[assignment[i]]],
                        Edge = sourceSamples[i].Select(context.Reflection.ReflectPoint).ToArray(),
                        Start = context.Reflection.ReflectPoint((double[])((Vertex)edge.GetStartVertex()).GetPoint()),
                        End = context.Reflection.ReflectPoint((double[])((Vertex)edge.GetEndVertex()).GetPoint()) };
                    foreach (Face2 face in (edge.GetTwoAdjacentFaces2() as object[] ?? new object[0]))
                    {
                        if (face == null) throw new InvalidOperationException("FLANGE33 incomplete adjacent faces.");
                        pair.Supports.Add(new FlangeSupport33 { Area = face.GetArea(),
                            Boundaries = ((object[])face.GetEdges()).Cast<Edge>().Select(e =>
                                SampleEdge(e).Select(context.Reflection.ReflectPoint).ToArray()).ToArray() });
                    }
                    if (pair.Supports.Count != 2) throw new InvalidOperationException("FLANGE33 requires a solid hinge with two adjacent faces.");
                    recipe.Pairs.Add(pair);
                    MirrorV7Diagnostics.Log("[FLANGE33][PAIR] feature=" + feature.Name + " edge=" + i +
                        " sketch=" + pair.SketchName + " segments=" + pair.Profile.Count + " supports=2");
                }
                return recipe;
            }
            finally { data.ReleaseSelectionAccess(); }
        }

        private static void CaptureFlangeProfiles33(Feature owner,
            Dictionary<string, List<SketchPrimitiveSnapshot58>> result, HashSet<string> visited)
        {
            for (var child = owner.GetFirstSubFeature() as Feature; child != null; child = child.GetNextSubFeature() as Feature)
            {
                if (!visited.Add(child.Name)) continue;
                var sketch = child.GetSpecificFeature2() as Sketch;
                if (sketch != null && !sketch.Is3D())
                {
                    var primitives = SketchOperationsHelper.CapturePristineSketchPrimitives58(sketch);
                    if (primitives.Count > 0)
                    {
                        if (primitives.Any(p => p.StartModel == null ||
                            (p.Type != (int)swSketchSegments_e.swSketchLINE && p.Type != (int)swSketchSegments_e.swSketchARC) ||
                            (!p.Circle && p.EndModel == null) ||
                            (p.Type == (int)swSketchSegments_e.swSketchARC && (p.CenterModel == null || (!p.Circle && p.MiddleModel == null)))))
                            throw new InvalidOperationException("FLANGE33 incomplete or unsupported source profile: " + child.Name);
                        result.Add(child.Name, primitives);
                    }
                }
                CaptureFlangeProfiles33(child, result, visited);
            }
        }

        // Store numerical geometry, never a relation's COM entity across rollback.
        // Definition entities expose external references that GetEntities can represent
        // only as local proxy points. Direct edge evidence also works for offset profiles.
        private static List<double[][]> CaptureProfileReferences34(Sketch sketch, string name)
        {
            var result = new List<double[][]>();
            if (sketch == null) throw new InvalidOperationException("FLANGE34 source sketch disappeared: " + name);
            int kind = 0;
            object support = sketch.GetReferenceEntity(ref kind);
            var supportEdge = support as Edge;
            if (supportEdge != null) result.Add(SampleEdge(supportEdge));
            MirrorV7Diagnostics.Log("[FLANGE34][PROFILE_FRAME] sketch=" + name + " referenceKind=" + kind +
                " matrix=" + string.Join(",", (double[])((MathTransform)sketch.ModelToSketchTransform).ArrayData));
            var relations = sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as Array;
            if (relations != null)
                foreach (SketchRelation relation in relations)
                {
                    // Read both representations: neither is guaranteed to contain edges.
                    foreach (object raw in new[] { relation.GetEntities(), relation.GetDefinitionEntities2() })
                    {
                        var entities = raw as Array;
                        if (entities == null) continue;
                        foreach (object entity in entities)
                        {
                            var edge = entity as Edge;
                            if (edge != null)
                            {
                                var samples = SampleEdge(edge);
                                if (!result.Any(r => BipartiteEquivalenceMatcherV7.SameEdgeSamples(r, samples, 1e-7)))
                                    result.Add(samples);
                            }
                            MirrorV7Diagnostics.Log("[FLANGE34][RELATION_ENTITY] sketch=" + name +
                                " kind=" + (edge != null ? "Edge" : entity is SketchSegment ? "SketchSegmentProxy" :
                                entity is SketchPoint ? "SketchPointProxy" : entity is Face2 ? "Face" : entity == null ? "null" : entity.GetType().Name));
                        }
                    }
                }
            MirrorV7Diagnostics.Log("[FLANGE34][REFERENCES] sketch=" + name + " uniqueEdges=" + result.Count);
            return result;
        }

        private static Edge FindFlangeEdge33(ModelDoc2 model, FlangePair33 pair)
        {
            var edge = FindEdge(model, pair.Edge);
            var adjacent = (edge.GetTwoAdjacentFaces2() as object[] ?? new object[0]).ToList();
            var expected = pair.Supports.Select(s => (object)FindFace(model, s.Area, s.Boundaries)).ToList();
            var match = BipartiteEquivalenceMatcherV7.Match(adjacent, expected, SameCom28);
            if (!match.HasPerfectMatching || match.LeftWithMultipleCandidates != 0)
                throw new InvalidOperationException("FLANGE33 edge matches position but not reflected support faces.");
            return edge;
        }

        private static void RebindFlange33(MirrorInPlaceExecutionContextV7 context, FeatureReplayCheckpointV7 item)
        {
            var guides = new List<FlangeGuide61>();
            try { RebindFlangeWithGuides61(context, item, guides); }
            catch (Exception ex)
            {
                MirrorV7Diagnostics.Log("[FLANGE61][STOP] feature=" + item.Feature.Name +
                    " guidesCreated=" + string.Join(",", guides.Select(g => g.Name)) +
                    " reason=" + ex.Message + " sourceUntouched=True outputPublished=False");
                throw;
            }
        }

        private static void RebindFlangeWithGuides61(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayCheckpointV7 item, List<FlangeGuide61> guides)
        {
            var model = context.WorkingDocument;
            var recipe = item.Flange;
            var feature = item.Feature.Feature;
            var plane = new ADDIN.Commands.PlaneData { Origin = context.Reflection.Origin, Normal = context.Reflection.Normal };
            // Inspect attachments under AccessSelections, then release BEFORE creating sketches.
            var probe = (IEdgeFlangeFeatureData)feature.GetDefinition();
            if (!probe.AccessSelections(model, null)) throw new InvalidOperationException("FLANGE33 attachment access rejected.");
            bool inherited, recreate;
            var angles = new List<double>();
            try
            {
                var desired = recipe.Pairs.Select(p => FindFlangeEdge33(model, p)).ToArray();
                var current = probe.Edges as Array;
                recreate = current == null || current.Length != recipe.Pairs.Count || current.Cast<object>().Any(e => !(e is Edge));
                inherited = !recreate && EdgeAttachmentsEquivalent31(current, desired.Cast<object>().ToArray(), feature.Name);
                if (recreate)
                    MirrorV7Diagnostics.Log("[FLANGE63][DANGLING_ATTACHMENT] feature=" + feature.Name +
                        " expected=" + recipe.Pairs.Count + " live=" + (current == null ? -1 : current.Length) +
                        " nullOrInvalid=" + (current == null ? -1 : current.Cast<object>().Count(e => !(e is Edge))) +
                        " strategy=NATIVE_RECREATE_FROM_CAPTURED_RECIPE");
                if (!recreate && !inherited && current != null && current.Cast<object>().Any(e => desired.Any(d => SameCom28(e, d))))
                    throw new InvalidOperationException("FLANGE33 partially overlapping attachments require per-profile replacement; no duplicate edge is added.");
                for (int i = 0; i < desired.Length; i++)
                {
                    double[] liveStart = (double[])((Vertex)desired[i].GetStartVertex()).GetPoint();
                    double[] liveEnd = (double[])((Vertex)desired[i].GetEndVertex()).GetPoint();
                    bool reversed = Distance(liveStart, recipe.Pairs[i].End) < 1e-7 && Distance(liveEnd, recipe.Pairs[i].Start) < 1e-7;
                    if (!reversed && (Distance(liveStart, recipe.Pairs[i].Start) > 1e-7 || Distance(liveEnd, recipe.Pairs[i].End) > 1e-7))
                        throw new InvalidOperationException("FLANGE33 cannot determine directed hinge correspondence.");
                    // A BRep edge may reverse its parameterization after a rebuild.
                    // For an inherited native profile use its actual directed local X axis.
                    if (inherited)
                    {
                        var owner = (Feature)((PartDoc)model).FeatureByName(recipe.Pairs[i].SketchName);
                        var profile = owner.GetSpecificFeature2() as Sketch;
                        if (profile == null) throw new InvalidOperationException("FLANGE45 inherited profile disappeared.");
                        double[] axis = ProfileX45((IMathUtility)context.SwApp.GetMathUtility(),
                            (MathTransform)profile.ModelToSketchTransform);
                        double dot = Enumerable.Range(0, 3).Sum(k => axis[k] * recipe.Pairs[i].ReflectedProfileX[k]);
                        if (Math.Abs(dot) < 1 - 1e-6)
                            throw new InvalidOperationException("FLANGE45 native hinge axes are not parallel to reflected source; dot=" + dot);
                        reversed = dot < 0;
                        MirrorV7Diagnostics.Log("[FLANGE45][AXIS] sketch=" + owner.Name + " reflectedDot=" + dot);
                    }
                    angles.Add(FlangeGeometry33.ReflectedAngle(recipe.Angle, reversed));
                    MirrorV7Diagnostics.Log("[FLANGE33][DIRECTION] edge=" + i + " reversed=" + reversed +
                        " angle_rad=" + angles[i] + " method=REFLECTION_PARITY");
                }
                MirrorV7Diagnostics.Log("[FLANGE33][ANGLE_POLICY] feature=" + feature.Name +
                    " offsetType=" + recipe.OffsetType + " lockAngle=" + recipe.LockAngle +
                    " lockApplicable=" + (recipe.OffsetType == (int)swFlangeOffsetTypes_e.swFlangeOffsetUptoEdgeAndMerge) +
                    " sourceAngle=" + recipe.Angle + " reflectedAngle=" + angles[0]);
            }
            finally { probe.ReleaseSelectionAccess(); }

            // First establish the exact reflected position on staging. Native flange
            // profiles consume this sketch readback, never reflect the source twice.
            foreach (var pair in recipe.Pairs)
                guides.Add(CreateFlangeGuide61(context, feature.Name, pair));
            if (inherited)
            {
                for (int i = 0; i < recipe.Pairs.Count; i++)
                {
                    double calibrated = angles[i];
                    var temporary = CreateAlignedFlangeProfile60(context, feature.Name,
                        recipe.Pairs[i], guides[i], recipe.ReverseOffset, ref calibrated);
                    DeleteTemporaryFlangeProfile60(model, temporary);
                    angles[i] = calibrated;
                }
                RequireCommonFlangeAngle61(angles);
            }

            // Set the native bend orientation BEFORE reading its profile transform.
            // Reading the old frame and only changing BendAngle afterwards rotated the
            // newly reflected sketch a second time on non-right-angle bends.
            if (inherited)
            {
                var orient = (IEdgeFlangeFeatureData)feature.GetDefinition();
                bool access = orient.AccessSelections(model, null);
                if (!access) throw new InvalidOperationException("FLANGE45 cannot access orientation stage.");
                try
                {
                    if (item.Rebind != null) item.Rebind(orient, model);
                    bool lockApplicable = recipe.OffsetType == (int)swFlangeOffsetTypes_e.swFlangeOffsetUptoEdgeAndMerge;
                    if (lockApplicable && recipe.LockAngle) orient.LockAngle = false;
                    orient.BendAngle = angles[0];
                    orient.ReverseOffset = recipe.ReverseOffset;
                    if (orient.UsePositionOffset) orient.ReversePositionOffset = recipe.ReversePositionOffset;
                    if (lockApplicable) orient.LockAngle = recipe.LockAngle;
                    if (!feature.ModifyDefinition(orient, model, null))
                        throw new InvalidOperationException("FLANGE45 orientation stage rejected; source remains untouched.");
                    access = false;
                }
                finally { if (access) orient.ReleaseSelectionAccess(); }
                MoveRollbackAfter(model, feature);
                model.EditRebuild3();
            }

            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, feature.Name))
                throw new InvalidOperationException("FLANGE33 cannot position rollback before flange.");
            var sketchNames = new List<string>();
            for (int i = 0; i < recipe.Pairs.Count; i++)
            {
                var pair = recipe.Pairs[i];
                var guide = guides[i];
                double angle = angles[i];
                Feature profile = inherited ? ((PartDoc)model).FeatureByName(pair.SketchName) as Feature :
                    CreateAlignedFlangeProfile60(context, feature.Name, pair, guide,
                        recipe.ReverseOffset, ref angle);
                angles[i] = angle;
                if (profile == null) throw new InvalidOperationException("FLANGE33 cannot obtain native flange profile.");
                var sketch = profile.GetSpecificFeature2() as Sketch;
                if (sketch == null) throw new InvalidOperationException("FLANGE33 generated feature is not a sketch.");
                // Native flange sketches may be translated along their normal by
                // PositionOffset. That transform is read-only. Preserve every local
                // XY coordinate; accept a normal translation only if ALL reflected
                // profile points share it, then let the full-body oracle decide.
                var math = (IMathUtility)context.SwApp.GetMathUtility();
                var transform = (MathTransform)sketch.ModelToSketchTransform;
                var normalOffsets = new List<double>();
                foreach (var point in guide.PlanePoints)
                {
                    var local = SketchMutationMathV7.Transform(math, transform, point);
                    normalOffsets.Add(local[2]);
                }
                if (normalOffsets.Count == 0 || normalOffsets.Max() - normalOffsets.Min() > 1e-7)
                    throw new InvalidOperationException("FLANGE36 native sketch is tilted relative to reflected profile; range_m=" +
                        (normalOffsets.Count == 0 ? double.NaN : normalOffsets.Max() - normalOffsets.Min()));
                double normalOffset = normalOffsets.Average();
                MirrorV7Diagnostics.Log("[FLANGE36][NATIVE_FRAME] feature=" + feature.Name +
                    " sketch=" + profile.Name + " normalTranslation_m=" + normalOffset +
                    " variation_m=" + (normalOffsets.Max() - normalOffsets.Min()) +
                    " sourcePositionOffset_m=" + recipe.PositionOffsetDistance);
                model.ClearSelection2(true);
                if (!profile.Select2(false, 0)) throw new InvalidOperationException("FLANGE33 profile selection failed.");
                model.EditSketch();
                try { SketchOperationsHelper.RecreateMirroredPrimitives58(model, sketch, guide.Profile,
                    plane, !inherited, normalOffset, alreadyReflected: true); }
                finally
                {
                    if (model.SketchManager.ActiveSketch != null) model.SketchManager.InsertSketch(false);
                    model.ClearSelection2(true);
                }
                VerifyProfileAgainstGuide61(context, profile.Name, guide, normalOffset);
                sketchNames.Add(profile.Name);
                MirrorV7Diagnostics.Log("[FLANGE33][PROFILE_PASS] source=" + pair.SketchName + " target=" + profile.Name);
            }
            RequireCommonFlangeAngle61(angles);
            if (recreate)
                feature = RecreateDanglingFlange63(context, item, sketchNames, angles[0]);
            else
            {
            var data = (IEdgeFlangeFeatureData)feature.GetDefinition();
            bool opened = data.AccessSelections(model, null);
            if (!opened) throw new InvalidOperationException("FLANGE33 cannot reopen native definition.");
            try
            {
                // Keep the runtime array types, as in the official Create Edge Flange C# example.
                // Cast<object>().ToArray() creates a different COM array representation.
                Edge[] edges = recipe.Pairs.Select(p => FindFlangeEdge33(model, p)).ToArray();
                if (!inherited)
                {
                    var oldEdges = data.Edges as Array;
                    if (oldEdges == null || oldEdges.Length == 0) throw new InvalidOperationException("FLANGE33 existing edge list lost.");
                    Sketch[] sketches = sketchNames.Select(n => ((PartDoc)model).FeatureByName(n) as Feature)
                        .Select(f => f == null ? null : f.GetSpecificFeature2() as Sketch).ToArray();
                    if (sketches.Any(s => s == null)) throw new InvalidOperationException("FLANGE33 generated profile identity lost.");
                    Edge[] oldTyped = oldEdges.Cast<object>().Select(e => e as Edge).ToArray();
                    if (oldTyped.Any(e => e == null)) throw new InvalidOperationException("FLANGE46 old attachment is not an Edge.");
                    if (model.SketchManager.ActiveSketch != null)
                        throw new InvalidOperationException("FLANGE46 sketch edit must be closed before AddEdges.");
                    if (oldTyped.Any(e => edges.Any(n => SameCom28(e, n))))
                        throw new InvalidOperationException("FLANGE46 replacement overlaps existing attachments; old edges will not be removed.");
                    int add = AddFlangeEdges46(data, edges, sketches);
                    MirrorV7Diagnostics.Log("[FLANGE46][ADD_EDGES] code=" + add + " name=" +
                        ((swEdgeFlangeError_e)add) + " count=" + edges.Length + " edgeArray=" + edges.GetType().Name +
                        " sketchArray=" + sketches.GetType().Name);
                    if (add != 0) throw new InvalidOperationException("FLANGE46 AddEdges rejected typed pairing: " + (swEdgeFlangeError_e)add);
                    // Do not delete old references unless the native definition contains
                    // exactly the original edges plus the requested additions.
                    if (!EdgeAttachmentsEquivalent31(data.Edges as Array, oldTyped.Concat(edges).Cast<object>().ToArray(), feature.Name))
                        throw new InvalidOperationException("FLANGE46 AddEdges readback mismatch; old attachments retained.");
                    int remove = data.RemoveEdges(oldTyped);
                    MirrorV7Diagnostics.Log("[FLANGE33][REMOVE_OLD_EDGES] code=" + remove);
                    if (remove != 0) throw new InvalidOperationException("FLANGE33 RemoveEdges rejected old attachments: " + remove);
                }
                if (item.Rebind != null) item.Rebind(data, model);
                // LockAngle is meaningful only for Up To Edge and Merge. For that
                // mode SolidWorks requires a temporary unlock before BendAngle can
                // be assigned; restore the original option before ModifyDefinition.
                bool lockApplicable = recipe.OffsetType == (int)swFlangeOffsetTypes_e.swFlangeOffsetUptoEdgeAndMerge;
                if (lockApplicable && recipe.LockAngle) data.LockAngle = false;
                data.BendAngle = angles[0];
                data.ReverseOffset = recipe.ReverseOffset;
                if (data.UsePositionOffset) data.ReversePositionOffset = recipe.ReversePositionOffset;
                if (lockApplicable) data.LockAngle = recipe.LockAngle;
                if (!EdgeAttachmentsEquivalent31(data.Edges as Array, edges, feature.Name))
                    throw new InvalidOperationException("FLANGE33 attachment readback failed.");
                if (!feature.ModifyDefinition(data, model, null))
                    throw new InvalidOperationException("FLANGE33 ModifyDefinition failed; no alternative direction is guessed.");
                opened = false;
            }
            finally { if (opened) data.ReleaseSelectionAccess(); }
            }
            MoveRollbackAfter(model, feature);
            model.EditRebuild3();
            // A native sketch/reference error is not evidence of a wrong offset
            // flag. Preserve the failed state instead of applying more definitions.
            try { EnsureFeatureHasNoError(feature, "FLANGE62 first native rebuild"); }
            catch (InvalidOperationException)
            {
                MirrorV7Diagnostics.Log("[FLANGE62][NATIVE_REBUILD_STOP] feature=" + feature.Name +
                    " " + FeatureError(feature) + " directionRetries=0 bodyVerification=NOT_PASSED");
                foreach (string profileName in sketchNames)
                {
                    var nativeProfile = ((PartDoc)model).FeatureByName(profileName) as Feature;
                    MirrorV7Diagnostics.Log("[FLANGE62][PROFILE_ERROR] profile=" + profileName +
                        " " + FeatureError(nativeProfile));
                }
                LogFlangeBodyMetrics48(model, item.ReflectedBodyOracle, "DIRECTED_GUIDE_NATIVE_ERROR");
                throw;
            }
            // AddEdges/RemoveEdges consume the new native sketches first. On SW2024,
            // assigning ReverseOffset in that same transaction can read back correctly
            // while the regenerated profile still uses the opposite length direction.
            // Commit the native length/position options in a fresh definition before
            // judging the final frames. This is not a redraw of an attached sketch.
            ResolveFlangeDirections48(context, item, recipe, sketchNames, guides);
            VerifyFinalFlangeProfiles61(context, sketchNames, guides);
            RemoveFlangeGuides61(context, item, guides);
            VerifyOptions27(item);
            if (!inherited) RegisterFlangeProfiles50(context, item, recipe, sketchNames);
            RegisterFlangeBends52(context, item, recipe);
            item.Result = new MirrorV7FeatureResult { FeatureName = item.Feature.Name, FeatureType = item.Feature.TypeName,
                Status = MirrorV7ReplayStatus.ExactReplay, Message = "FLANGE33 reflected profiles and native attachment replacement; oracle PASS." };
            MirrorV7Diagnostics.Log("[FLANGE33][PASS] feature=" + feature.Name +
                " nativeIdentityPreserved=" + !item.WasNativeReplacement + " direction=ORACLE_VERIFIED");
        }

        private static Feature RecreateDanglingFlange63(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayCheckpointV7 item, IList<string> profiles, double angle)
        {
            var model = context.WorkingDocument;
            var recipe = item.Flange;
            var old = item.Feature.Feature;
            if (model.SketchManager.ActiveSketch != null || item.Options27 == null ||
                !string.IsNullOrEmpty(item.BindingError27))
                throw new InvalidOperationException("FLANGE63 recreation requires closed profiles and a complete captured recipe.");
            var definition = model.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmEdgeFlange) as IEdgeFlangeFeatureData;
            if (definition == null) throw new InvalidOperationException("FLANGE63 native CreateDefinition unavailable.");
            // Only scalar, documented options enter this fresh definition. Never use
            // the unsupported Edges setter or pass null handles to RemoveEdges.
            foreach (var option in item.Options27)
            {
                if (option.Key == "BendAngle") continue; // Audit stores cosine; creation needs radians.
                var property = typeof(IEdgeFlangeFeatureData).GetProperty(option.Key);
                if (property == null) continue; // Custom allowance fields are applied below.
                if (!property.CanWrite || !(property.PropertyType.IsPrimitive || property.PropertyType.IsEnum))
                    throw new InvalidOperationException("FLANGE63 unsupported scalar option: " + option.Key);
                property.SetValue(definition, option.Value, null);
            }
            if (!definition.UseDefaultBendAllowance)
            {
                var allowance = definition.GetCustomBendAllowance();
                if (allowance == null) throw new InvalidOperationException("FLANGE63 custom allowance unavailable.");
                allowance.Type = (int)item.Options27["AllowanceType"];
                foreach (var option in item.Options27)
                {
                    if (option.Key == "AllowanceType" || typeof(IEdgeFlangeFeatureData).GetProperty(option.Key) != null) continue;
                    var property = typeof(ICustomBendAllowance).GetProperty(option.Key);
                    if (property == null || !property.CanWrite)
                        throw new InvalidOperationException("FLANGE63 unsupported allowance option: " + option.Key);
                    property.SetValue(allowance, option.Value, null);
                }
                definition.SetCustomBendAllowance(allowance);
            }
            definition.BendAngle = angle;
            definition.ReverseOffset = recipe.ReverseOffset;
            if (recipe.UsePositionOffset) definition.ReversePositionOffset = recipe.ReversePositionOffset;

            // Do not request absorbed/child deletion. SW may still auto-delete
            // native dependents. Permit only a dependency-closure loss with a
            // pre-captured native recovery recipe; defer to its original slot.
            // Unexpected/unrecoverable loss still undoes this single deletion.
            var beforeDeletion = FlangeHistory63(model);
            var retained = new List<Tuple<string, string>>();
            var absorbed = new List<Feature>();
            CollectFlangeSubfeatures63(old, absorbed);
            for (var live = model.FirstFeature() as Feature; live != null; live = live.GetNextFeature() as Feature)
                if (!SameCom28(live, old) && !absorbed.Any(child => SameCom28(child, live)))
                    retained.Add(Tuple.Create(live.Name, live.GetTypeName2()));
            MoveRollbackAfter(model, old);
            model.ClearSelection2(true);
            if (!old.Select2(false, 0) || !model.Extension.DeleteSelection2(0))
                throw new InvalidOperationException("FLANGE63 cannot remove defective staging flange only.");
            model.ClearSelection2(true);
            var remaining = FlangeHistory63(model);
            string dependencyReason64 = null;
            if (!retained.SequenceEqual(remaining) &&
                !QueueDependencyLoss64(context, item, retained, remaining, out dependencyReason64))
            {
                string missing = string.Join("|", retained.Except(remaining).Select(p => p.Item1 + ":" + p.Item2));
                string unexpected = string.Join("|", remaining.Except(retained).Select(p => p.Item1 + ":" + p.Item2));
                // DeleteSelection2 is the only undoable operation since selection.
                // This exact one-step recovery was verified against both lookup
                // APIs and the ordered history on the live SW2024 staging copy.
                model.EditUndo2(1);
                var restored = FlangeHistory63(model);
                bool historyRestored = beforeDeletion.SequenceEqual(restored);
                var restoredFlange = ((PartDoc)model).FeatureByName(item.Feature.Name) as Feature;
                bool flangeRestored = restoredFlange != null && restoredFlange.GetTypeName2() == item.Feature.TypeName;
                if (historyRestored && flangeRestored) item.Feature.Feature = restoredFlange;
                MirrorV7Diagnostics.Log("[FLANGE63][DELETE_SCOPE_REJECTED] feature=" + item.Feature.Name +
                    " missing=" + missing + " unexpected=" + unexpected +
                    " dependencyReason=" + dependencyReason64 +
                    " undoHistoryRestored=" + historyRestored + " flangeRestored=" + flangeRestored +
                    " outputPublished=False sourceUntouched=True");
                throw new InvalidOperationException("FLANGE63 deletion changed retained feature history; expected=" +
                    retained.Count + " actual=" + remaining.Count + " missing=" +
                    missing + " unexpected=" + unexpected + " undoHistoryRestored=" + historyRestored +
                    " flangeRestored=" + flangeRestored + "; dependencyReason=" + dependencyReason64);
            }
            Edge[] edges = recipe.Pairs.Select(p => FindFlangeEdge33(model, p)).ToArray();
            Sketch[] sketches = profiles.Select(n => ((PartDoc)model).FeatureByName(n) as Feature)
                .Select(f => f == null ? null : f.GetSpecificFeature2() as Sketch).ToArray();
            int add = AddFlangeEdges46(definition, edges, sketches);
            if (add != (int)swEdgeFlangeError_e.swEdgeFlangeError_NoError)
                throw new InvalidOperationException("FLANGE63 fresh native attachment failed: " + (swEdgeFlangeError_e)add);
            // A CreateDefinition object is not an attached feature. Its Edges
            // getter returned null on SW2024 even after AddEdges returned NoError.
            // Audit the actual native feature's selections AFTER CreateFeature.
            if (item.Rebind != null) item.Rebind(definition, model);
            var created = model.FeatureManager.CreateFeature(definition) as Feature;
            if (created == null) throw new InvalidOperationException("FLANGE63 native CreateFeature rejected the captured recipe.");
            created.Name = item.Feature.Name;
            if (created.Name != item.Feature.Name || created.GetTypeName2() != item.Feature.TypeName)
                throw new InvalidOperationException("FLANGE63 recreated feature name/type changed.");
            item.Feature.Feature = created;
            item.WasNativeReplacement = true;
            var attached64 = created.GetDefinition() as IEdgeFlangeFeatureData;
            bool accessed64 = attached64 != null && attached64.AccessSelections(model, null);
            if (!accessed64) throw new InvalidOperationException("FLANGE64 recreated attachment selections unavailable.");
            try
            {
                var mapped64 = recipe.Pairs.Select(p => FindFlangeEdge33(model, p)).Cast<object>().ToArray();
                if (!EdgeAttachmentsEquivalent31(attached64.Edges as Array, mapped64, item.Feature.Name))
                    throw new InvalidOperationException("FLANGE64 recreated native edge pairing readback mismatch.");
            }
            finally { attached64.ReleaseSelectionAccess(); }
            MirrorV7Diagnostics.Log("[FLANGE63][NATIVE_RECREATED] feature=" + created.Name +
                " pairs=" + edges.Length + " downstreamTopLevelRetained=" + retained.Count +
                " mirrorBodyFeature=False bodyVerificationPending=True");
            return created;
        }

        private static void CollectFlangeSubfeatures63(Feature owner, IList<Feature> result)
        {
            for (var child = owner.GetFirstSubFeature() as Feature; child != null; child = child.GetNextSubFeature() as Feature)
            {
                if (result.Any(previous => SameCom28(previous, child))) continue;
                if (result.Count >= 10000) throw new InvalidOperationException("FLANGE63 subfeature safety limit exceeded.");
                result.Add(child);
                CollectFlangeSubfeatures63(child, result);
            }
        }

        private static List<Tuple<string, string>> FlangeHistory63(ModelDoc2 model)
        {
            var history = new List<Tuple<string, string>>();
            for (var feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
            {
                if (history.Count >= 10000) throw new InvalidOperationException("FLANGE63 history safety limit exceeded.");
                history.Add(Tuple.Create(feature.Name, feature.GetTypeName2()));
            }
            return history;
        }

        private static void RegisterFlangeBends52(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayCheckpointV7 item, FlangeRecipe33 recipe)
        {
            if (recipe.Bends.Count == 0) return;
            // Face area is numerically integrated for native BSpline bends. The
            // live regression has equal reflected boundaries but area readings
            // differing by up to 24 ppm. Prove the complete BRep BEFORE using
            // unique reflected boundary groups to identify individual bends.
            BaseSketchMutationEngineV7.VerifyCurrentSolids(context, item.ReflectedBodyOracle);
            var layout = FeatureTreeScannerV7.Scan(context.WorkingDocument);
            var owner = layout.Nodes.Single(n => SameCom28(n.Feature, item.Feature.Feature));
            var targets = layout.Nodes.Skip(owner.TreeOrder + 1).TakeWhile(n => n.Depth > owner.Depth)
                .Where(n => n.TypeName == "OneBend" && n.Depth == owner.Depth + 1).ToList();
            if (targets.Count != recipe.Bends.Count)
                throw new InvalidOperationException("FLANGE52 generated bend count changed: source=" +
                    recipe.Bends.Count + " target=" + targets.Count);
            var faces = targets.Select(n => CaptureBendFaces52(n.Feature, p => p)).ToList();
            var candidates = recipe.Bends.Select(b => Enumerable.Range(0, targets.Count)
                .Where(i => SameBendFaces52(b.Faces, faces[i])).ToArray()).ToList();
            var assignment = FlangeGeometry33.UniqueAssignment(candidates, targets.Count);
            if (assignment == null)
                throw new InvalidOperationException("FLANGE52 bend geometry mapping missing/ambiguous; candidates=" +
                    string.Join(",", candidates.Select(c => c.Length)) + " sourceFaces=" +
                    string.Join(",", recipe.Bends.Select(b => b.Faces.Count)));
            for (int i = 0; i < assignment.Length; i++)
            {
                var source = context.SourceGraph.Nodes.Single(n => n.TreeOrder == recipe.Bends[i].SourceOrder);
                var target = targets[assignment[i]];
                if (source.IsSuppressed != target.IsSuppressed ||
                    !(target.Feature.GetParents() as object[] ?? new object[0]).OfType<Feature>()
                        .Any(p => SameCom28(p, item.Feature.Feature)))
                    throw new InvalidOperationException("FLANGE52 bend metadata/owner mismatch: " + source.Name);
                if (source.Name != target.Feature.Name)
                {
                    // Native attachment replacement generates new bend labels.
                    // Preserve the source label only after unique geometric mapping,
                    // and never remove another feature to make its name available.
                    var collision = ((PartDoc)context.WorkingDocument).FeatureByName(source.Name) as Feature;
                    if (collision != null && !SameCom28(collision, target.Feature))
                        throw new InvalidOperationException("FLANGE63 source bend name is occupied: " + source.Name);
                    string generatedName = target.Feature.Name;
                    target.Feature.Name = source.Name;
                    if (target.Feature.Name != source.Name)
                        throw new InvalidOperationException("FLANGE63 cannot preserve native bend name: " + source.Name);
                    MirrorV7Diagnostics.Log("[FLANGE63][BEND_LABEL_RESTORED] generated=" + generatedName +
                        " source=" + source.Name + " evidence=UNIQUE_REFLECTED_BOUNDARIES_AND_BODY_PROOF");
                }
                var reference = PersistentReferenceServiceV7.Capture(context.WorkingDocument,
                    target.Feature, source.Name, "REGENERATED_FLANGE_BEND");
                int state;
                if (reference == null || !SameCom28(target.Feature,
                    PersistentReferenceServiceV7.Resolve(context.WorkingDocument, reference, out state)))
                    throw new InvalidOperationException("FLANGE52 cannot retain regenerated bend identity.");
                context.ReplacementReferences.Add(source.TreeOrder, reference);
                context.GeneratedBendOwners.Add(source.TreeOrder, item.Feature.TreeOrder);
                MirrorV7Diagnostics.Log("[FLANGE52][BEND_REGISTERED] source=" + source.Name +
                    " target=" + target.Feature.Name + " owner=" + owner.Name + " evidence=REFLECTED_FACE_SET");
            }
        }

        private static void RegisterFlangeProfiles50(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayCheckpointV7 item, FlangeRecipe33 recipe, IList<string> generatedNames)
        {
            if (recipe.Pairs.Count != generatedNames.Count)
                throw new InvalidOperationException("FLANGE50 profile registration count mismatch.");
            var model = context.WorkingDocument;
            var part = (PartDoc)model;
            var sourceOwner = context.SourceGraph.Nodes.Single(n => n.TreeOrder == item.Feature.TreeOrder);
            int end = context.SourceGraph.Nodes.Where(n => n.TreeOrder > sourceOwner.TreeOrder &&
                n.Depth <= sourceOwner.Depth).Select(n => n.TreeOrder).DefaultIfEmpty(int.MaxValue).Min();
            for (int i = 0; i < generatedNames.Count; i++)
            {
                var pair = recipe.Pairs[i];
                var source = context.SourceGraph.Nodes.Where(n =>
                    n.TreeOrder > sourceOwner.TreeOrder && n.TreeOrder < end &&
                    n.Depth == sourceOwner.Depth + 1 && n.Role == MirrorV7FeatureRole.Sketch &&
                    string.Equals(n.Name, pair.SketchName, StringComparison.Ordinal)).ToList();
                if (source.Count != 1 || context.ReplacementReferences.ContainsKey(source[0].TreeOrder))
                    throw new InvalidOperationException("FLANGE50 source profile is missing or ambiguous: " + pair.SketchName);
                var generated = part.FeatureByName(generatedNames[i]) as Feature;
                if (generated == null || !(generated.GetSpecificFeature2() is Sketch))
                    throw new InvalidOperationException("FLANGE50 generated profile disappeared: " + generatedNames[i]);
                object[] children = generated.GetChildren() as object[];
                bool directConsumer = children != null && children.OfType<Feature>().Any(c => SameCom28(c, item.Feature.Feature));
                var owner = generated.GetOwnerFeature() as Feature;
                bool absorbedByFlange = false;
                if (owner != null && SameCom28(owner, item.Feature.Feature))
                    for (var child = owner.GetFirstSubFeature() as Feature; child != null; child = child.GetNextSubFeature() as Feature)
                        if (SameCom28(child, generated)) { absorbedByFlange = true; break; }
                // A multiple-edge flange can report GetChildren/GetParents for
                // its first profile only. SW2024 exposes its other driver through
                // BOTH GetOwnerFeature and the flange's actual subfeature list.
                // Names alone, or a missing child list alone, prove nothing.
                if (!directConsumer && !absorbedByFlange)
                    throw new InvalidOperationException("FLANGE50 generated sketch does not drive its Edge Flange.");
                // Names are labels, not identity. Keep the native generated name;
                // never delete or rename an old sketch just to free its label.
                string targetName = generated.Name;
                if (string.IsNullOrWhiteSpace(targetName))
                    throw new InvalidOperationException("FLANGE51 generated profile has no name.");
                var reference = PersistentReferenceServiceV7.Capture(model, generated,
                    source[0].Name, "FLANGE_PROFILE_REPLACEMENT");
                int state;
                var resolved = reference == null ? null :
                    PersistentReferenceServiceV7.Resolve(model, reference, out state) as Feature;
                if (resolved == null || !SameCom28(resolved, generated))
                    throw new InvalidOperationException("FLANGE50 generated profile identity cannot be retained.");
                if (owner != null && !SameCom28(owner, item.Feature.Feature))
                    throw new InvalidOperationException("FLANGE50 generated profile has an unrelated owner.");
                // The scanner visits each identity once. A native profile can be
                // exposed in top-level history before its owning feature even if
                // GetOwnerFeature still returns that flange. Use the same layout
                // representation as the publication gate, not a null-owner guess.
                var layout = FeatureTreeScannerV7.Scan(model);
                var profileNode = layout.Nodes.Single(n => SameCom28(n.Feature, generated));
                var ownerNode = layout.Nodes.Single(n => SameCom28(n.Feature, item.Feature.Feature));
                bool external = profileNode.Depth == ownerNode.Depth && profileNode.TreeOrder < ownerNode.TreeOrder;
                if (!external && (owner == null || profileNode.Depth != ownerNode.Depth + 1))
                    throw new InvalidOperationException("FLANGE50 generated profile has an unsupported layout.");
                context.ReplacementReferences.Add(source[0].TreeOrder, reference);
                context.ReplacementTargetNames.Add(source[0].TreeOrder, targetName);
                if (external)
                    context.ExternalFlangeProfiles.Add(source[0].TreeOrder, sourceOwner.TreeOrder);
                MirrorV7Diagnostics.Log("[FLANGE51][PROFILE_REGISTERED] source=" + source[0].Name +
                    " target=" + generated.Name + " sourceOrder=" + source[0].TreeOrder +
                    " ownerOrder=" + sourceOwner.TreeOrder + " external=" + external +
                    " bindingEvidence=" + (directConsumer ? "DIRECT_CONSUMER" : "NATIVE_OWNER_AND_SUBFEATURE"));
            }
        }

        // These are the two native direction switches that may change meaning when
        // an edge is replaced after a reflection. Evaluate their finite state space
        // against the captured body oracle; never infer direction from a part name.
        private static void ResolveFlangeDirections48(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayCheckpointV7 item, FlangeRecipe33 recipe,
            IList<string> profileNames, IList<FlangeGuide61> guides)
        {
            var model = context.WorkingDocument;
            var feature = item.Feature.Feature;
            var candidates = new List<Tuple<bool, bool>>();
            candidates.Add(Tuple.Create(recipe.ReverseOffset, recipe.ReversePositionOffset));
            candidates.Add(Tuple.Create(!recipe.ReverseOffset, recipe.ReversePositionOffset));
            if (recipe.UsePositionOffset)
            {
                candidates.Add(Tuple.Create(recipe.ReverseOffset, !recipe.ReversePositionOffset));
                candidates.Add(Tuple.Create(!recipe.ReverseOffset, !recipe.ReversePositionOffset));
            }
            foreach (var candidate in candidates)
            {
                string state = "lengthReverse=" + candidate.Item1 +
                    " positionReverse=" + candidate.Item2;
                try
                {
                    feature = ((PartDoc)model).FeatureByName(item.Feature.Name) as Feature;
                    RequireHealthyNativeFlange66(feature, "DIRECTION_BEGIN");
                    item.Feature.Feature = feature;
                    // Even the original flags require this distinct transaction after
                    // binding: getter equality did not prove native frame equality in
                    // the live regression. Never change the bend angle to compensate
                    // for an unsettled length direction. Copied user constraints stay
                    // intact; only disposable recreated drivers have a cleanup step.
                    SetFlangeDirections48(context, feature, candidate.Item1, candidate.Item2);
                    feature = ((PartDoc)model).FeatureByName(item.Feature.Name) as Feature;
                    RequireHealthyNativeFlange66(feature, "DIRECTION_COMMIT");
                    item.Feature.Feature = feature;
                    MirrorV7Diagnostics.Log("[FLANGE63][ATTACHED_OPTIONS_COMMITTED] feature=" + feature.Name +
                        " " + state + " transaction=AFTER_PROFILE_BINDING");
                    // Once native references fail, changing more flags on this
                    // invalid state cannot prove a geometric direction choice.
                    bool warning;
                    int nativeError = feature.GetErrorCode2(out warning);
                    if (nativeError != 0 && !warning)
                        throw new System.IO.InvalidDataException("FLANGE62 direction candidate caused native rebuild error; " +
                            state + " errorCode=" + nativeError + "; remaining candidates not attempted.");
                    EnsureFeatureHasNoError(feature, "FLANGE48 native rebuild");
                    if (item.WasNativeReplacement)
                        ReacquireAndRedrawNativeFlangeProfiles64(context, item, recipe, profileNames, guides);
                    VerifyFinalFlangeProfiles61(context, profileNames, guides);
                    BaseSketchMutationEngineV7.VerifyCurrentSolids(context, item.ReflectedBodyOracle);
                    MirrorV7Diagnostics.Log("[FLANGE48][ORACLE] feature=" + feature.Name +
                        " " + state + " result=PASS");
                    // ModifyDefinition is not guaranteed to be idempotent for a
                    // regenerated native flange profile. Keep the proven state;
                    // reapplying it can change the body even with identical flags.
                    MirrorV7Diagnostics.Log("[FLANGE48][SELECT] feature=" + feature.Name +
                        " " + state + " tested=" + (candidates.IndexOf(candidate) + 1) +
                        " available=" + candidates.Count + " result=PASS");
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    MirrorV7Diagnostics.Log("[FLANGE48][ORACLE] feature=" + feature.Name +
                        " " + state + " result=FAIL reason=" + ex.Message);
                    LogFlangeBodyMetrics48(model, item.ReflectedBodyOracle, state);
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    MirrorV7Diagnostics.Log("[FLANGE48][ORACLE] feature=" + feature.Name +
                        " " + state + " result=API_REJECTED hresult=" + ex.HResult +
                        " reason=" + ex.Message);
                    throw new System.IO.InvalidDataException("FLANGE66 native COM transaction failed; no further direction candidate is safe.", ex);
                }
            }
            throw new InvalidOperationException("FLANGE48 no native direction matches the reflected body; tried=" +
                candidates.Count + ". Diagnostic staging is retained; no output is published.");
        }

        private static void LogFlangeBodyMetrics48(ModelDoc2 model, List<Body2> oracle, string state)
        {
            try
            {
                object[] raw = ((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                var actual = raw == null ? new Body2[0] : raw.Cast<Body2>().ToArray();
                MirrorV7Diagnostics.Log("[FLANGE48][BODY] " + state +
                    " expectedCount=" + oracle.Count + " actualCount=" + actual.Length);
                for (int i = 0; i < oracle.Count; i++)
                {
                    var m = BodyMirrorVerifierV7.Measure(oracle[i]);
                    MirrorV7Diagnostics.Log("[FLANGE48][EXPECTED] body=" + i +
                        " volume=" + m.Volume + " centroid=" + string.Join(",", m.Centroid) +
                        " box=" + string.Join(",", m.Box) + " faces=" + m.Faces);
                }
                for (int i = 0; i < actual.Length; i++)
                {
                    var m = BodyMirrorVerifierV7.Measure(actual[i]);
                    MirrorV7Diagnostics.Log("[FLANGE48][ACTUAL] body=" + i +
                        " volume=" + m.Volume + " centroid=" + string.Join(",", m.Centroid) +
                        " box=" + string.Join(",", m.Box) + " faces=" + m.Faces);
                }
            }
            catch (Exception ex)
            {
                MirrorV7Diagnostics.Log("[FLANGE48][BODY_UNAVAILABLE] " + state +
                    " reason=" + ex.Message);
            }
        }

        private static void SetFlangeDirections48(MirrorInPlaceExecutionContextV7 context,
            Feature feature, bool reverseOffset, bool reversePositionOffset)
        {
            var model = context.WorkingDocument;
            var data = (IEdgeFlangeFeatureData)feature.GetDefinition();
            bool opened = data.AccessSelections(model, null);
            if (!opened) throw new System.IO.InvalidDataException("FLANGE66 cannot access the native direction definition; remaining candidates not attempted.");
            try
            {
                data.ReverseOffset = reverseOffset;
                if (data.UsePositionOffset) data.ReversePositionOffset = reversePositionOffset;
                if (!feature.ModifyDefinition(data, model, null))
                    throw new System.IO.InvalidDataException("FLANGE66 native direction change rejected; remaining candidates not attempted.");
                opened = false;
            }
            finally { if (opened) data.ReleaseSelectionAccess(); }
            RequireHealthyNativeFlange66(feature, "DIRECTION_DEFINITION");
            MoveRollbackAfter(model, feature);
            RequireHealthyNativeFlange66(feature, "DIRECTION_ROLLFORWARD");
            // A length-direction change reorients absorbed sketch support planes.
            // Force a native feature regeneration, then reacquire frames by name.
            // The caller guarantees no sketch edit is active here.
            model.ForceRebuild3(false);
            RequireHealthyNativeFlange66(feature, "DIRECTION_REBUILD");
        }

        internal static int AddFlangeEdges46(IEdgeFlangeFeatureData data, Edge[] edges, Sketch[] sketches)
        {
            if (data == null || edges == null || sketches == null || edges.Length == 0 ||
                edges.Length != sketches.Length || edges.Any(e => e == null) || sketches.Any(s => s == null))
                throw new InvalidOperationException("FLANGE46 invalid edge/sketch pairing; native API was not called.");
            // Passing the arrays directly preserves Edge[] and Sketch[] at the COM boundary.
            return data.AddEdges(edges, sketches);
        }

        private static double[] ProfileX45(IMathUtility math, MathTransform modelToSketch)
        {
            return ProfileAxis60(math, modelToSketch, 0);
        }

        private static double[] ProfileAxis60(IMathUtility math, MathTransform modelToSketch, int index)
        {
            if (math == null || modelToSketch == null || index < 0 || index > 2)
                throw new InvalidOperationException("FLANGE60 sketch axis is unavailable.");
            var inverse = (MathTransform)modelToSketch.Inverse();
            double[] origin = SketchMutationMathV7.Transform(math, inverse, new double[] { 0, 0, 0 });
            double[] basis = { 0, 0, 0 };
            basis[index] = 1;
            double[] unit = SketchMutationMathV7.Transform(math, inverse, basis);
            var axis = Enumerable.Range(0, 3).Select(i => unit[i] - origin[i]).ToArray();
            double length = Math.Sqrt(axis.Sum(x => x * x));
            if (length < 1e-12) throw new InvalidOperationException("FLANGE45 invalid sketch axis.");
            return axis.Select(x => x / length).ToArray();
        }

        private static Feature CreateAlignedFlangeProfile60(MirrorInPlaceExecutionContextV7 context,
            string featureName, FlangePair33 pair, FlangeGuide61 guide, bool flip, ref double angle)
        {
            var model = context.WorkingDocument;
            var math = (IMathUtility)context.SwApp.GetMathUtility();
            const double epsilon = 0.01; // Native angle response is measured, not assumed.
            Feature first = InsertFlangeProfile60(model, featureName, pair, angle, flip);
            double firstRange = FlangeProfilePlaneRange60(math, first, guide);
            double[] firstY = FlangeProfileDrawingSide62(math, first, guide);
            double firstSide = Dot60(firstY, guide.Frame.Outward);
            if (firstRange <= 1e-7 && firstSide >= 1 - 1e-8)
            {
                MirrorV7Diagnostics.Log("[FLANGE62][DIRECTED_FRAME_PASS] feature=" + featureName +
                    " sketch=" + pair.SketchName + " range_m=" + firstRange +
                    " outwardDotY=" + firstSide + " angle=" + angle + " guide=" + guide.Name);
                return first;
            }
            DeleteTemporaryFlangeProfile60(model, first);

            double secondAngle = WrapAngle60(angle + epsilon);
            Feature second = InsertFlangeProfile60(model, featureName, pair, secondAngle, flip);
            double[] secondY = FlangeProfileDrawingSide62(math, second, guide);
            DeleteTemporaryFlangeProfile60(model, second);

            double response, directedDelta;
            double solved = FlangeGuideFrame61.SolveDirectedAngle62(angle, epsilon,
                guide.Frame.Hinge, firstY, secondY, guide.Frame.Outward, out response, out directedDelta);
            MirrorV7Diagnostics.Log("[FLANGE62][DIRECTED_SOLVE] feature=" + featureName +
                " sketch=" + pair.SketchName + " initialAngle=" + angle +
                " response_rad=" + response + " directedDelta_rad=" + directedDelta +
                " solvedAngle=" + solved + " firstRange_m=" + firstRange +
                " firstOutwardDotY=" + firstSide + " guide=" + guide.Name + " acceptsOppositeSide=False");
            Feature aligned = InsertFlangeProfile60(model, featureName, pair, solved, flip);
            double finalRange = FlangeProfilePlaneRange60(math, aligned, guide);
            double finalSide = Dot60(FlangeProfileDrawingSide62(math, aligned, guide), guide.Frame.Outward);
            if (finalRange > 1e-7 || finalSide < 1 - 1e-8)
            {
                DeleteTemporaryFlangeProfile60(model, aligned);
                throw new InvalidOperationException("FLANGE62 solved native frame differs from directed 3D guide: " +
                    featureName + " range_m=" + finalRange + " outwardDotY=" + finalSide);
            }
            angle = solved;
            MirrorV7Diagnostics.Log("[FLANGE62][DIRECTED_FRAME_PASS] feature=" + featureName +
                " sketch=" + pair.SketchName + " range_m=" + finalRange +
                " outwardDotY=" + finalSide + " angle=" + solved + " method=MEASURED_NATIVE_Y_TO_3D_GUIDE");
            return aligned;
        }

        private static double[] FlangeProfileDrawingSide62(IMathUtility math, Feature profile, FlangeGuide61 guide)
        {
            var sketch = profile.GetSpecificFeature2() as Sketch;
            if (sketch == null) throw new InvalidOperationException("FLANGE62 native profile disappeared.");
            var transform = sketch.ModelToSketchTransform as MathTransform;
            double[] nativeX = ProfileAxis60(math, transform, 0);
            if (Math.Abs(Dot60(nativeX, guide.Frame.Hinge)) < 1 - 1e-8)
                throw new InvalidOperationException("FLANGE62 native profile X does not follow the mapped hinge.");
            return ProfileAxis60(math, transform, 1);
        }

        private static Feature InsertFlangeProfile60(ModelDoc2 model, string featureName, FlangePair33 pair,
            double angle, bool flip)
        {
            if (model.SketchManager.ActiveSketch != null)
                throw new InvalidOperationException("FLANGE61 close sketch before native plane calibration.");
            if (!model.FeatureManager.EditRollback(
                (int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, featureName))
                throw new InvalidOperationException("FLANGE60 cannot position rollback before flange: " + featureName);
            var edge = FindFlangeEdge33(model, pair);
            var profile = model.InsertSketchForEdgeFlange(edge, angle, flip) as Feature;
            if (profile == null || !(profile.GetSpecificFeature2() is Sketch))
                throw new InvalidOperationException("FLANGE60 InsertSketchForEdgeFlange did not return a sketch.");
            string name = profile.Name;
            // Normalize actual command state before rollback/deletion/EditSketch.
            if (model.SketchManager.ActiveSketch != null)
            {
                if (!SameCom28(model.SketchManager.ActiveSketch, profile.GetSpecificFeature2()))
                    throw new InvalidOperationException("FLANGE61 API left an unexpected sketch active.");
                model.SketchManager.InsertSketch(false);
            }
            return ((PartDoc)model).FeatureByName(name) as Feature ??
                throw new InvalidOperationException("FLANGE61 native profile lost after sketch exit.");
        }

        private static void DeleteTemporaryFlangeProfile60(ModelDoc2 model, Feature profile)
        {
            if (model.SketchManager.ActiveSketch != null)
                throw new InvalidOperationException("FLANGE61 cannot delete a calibration sketch during sketch edit.");
            string name = profile.Name;
            model.ClearSelection2(true);
            if (!profile.Select2(false, 0) || !model.Extension.DeleteSelection2(0))
                throw new InvalidOperationException("FLANGE60 cannot remove unbound calibration sketch: " + name);
            model.ClearSelection2(true);
            if (((PartDoc)model).FeatureByName(name) != null)
                throw new InvalidOperationException("FLANGE60 calibration sketch remains in feature tree: " + name);
        }

        private static double FlangeProfilePlaneRange60(IMathUtility math, Feature profile, FlangeGuide61 guide)
        {
            var sketch = profile.GetSpecificFeature2() as Sketch;
            if (sketch == null) throw new InvalidOperationException("FLANGE60 native sketch disappeared.");
            var frame = sketch.ModelToSketchTransform as MathTransform;
            if (frame == null) throw new InvalidOperationException("FLANGE60 native sketch frame unavailable.");
            var heights = guide.PlanePoints
                .Select(p => SketchMutationMathV7.Transform(math, frame, p)[2]).ToList();
            if (heights.Count == 0)
                throw new InvalidOperationException("FLANGE60 source profile has no points.");
            return heights.Max() - heights.Min();
        }

        private static double Dot60(double[] a, double[] b)
        {
            return Enumerable.Range(0, 3).Sum(i => a[i] * b[i]);
        }

        private static double WrapAngle60(double value)
        {
            return (value % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
        }

        private static Dictionary<string, object> ReadFlangeOptions45(IEdgeFlangeFeatureData f)
        {
            // Audit active options only. Inactive native getters can change on rebuild.
            var o = new Dictionary<string, object>
            {
                { "BendAngle", Math.Round(Math.Cos(f.BendAngle), 12) },
                { "OffsetType", f.OffsetType }, { "PositionType", f.PositionType },
                { "UseDefaultBendRadius", f.UseDefaultBendRadius },
                { "UseDefaultBendAllowance", f.UseDefaultBendAllowance },
                { "UseDefaultBendRelief", f.UseDefaultBendRelief },
                { "UsePositionOffset", f.UsePositionOffset }, { "GapDistance", f.GapDistance },
                { "PerpendicularToFace", f.PerpendicularToFace },
                { "UsePositionTrimSideBends", f.UsePositionTrimSideBends }
            };
            if (!f.UseDefaultBendRadius) o["BendRadius"] = f.BendRadius;
            if (f.OffsetType == (int)swFlangeOffsetTypes_e.swFlangeOffsetBlind ||
                f.OffsetType == (int)swFlangeOffsetTypes_e.swFlangeOffsetMidPlane ||
                f.OffsetType == (int)swFlangeOffsetTypes_e.swFlangeOffsetFromSurface)
                o["OffsetDistance"] = f.OffsetDistance;
            if (f.OffsetType == (int)swFlangeOffsetTypes_e.swFlangeOffsetBlind ||
                f.OffsetType == (int)swFlangeOffsetTypes_e.swFlangeOffsetMidPlane)
                o["OffsetDimType"] = f.OffsetDimType;
            if (f.OffsetType == (int)swFlangeOffsetTypes_e.swFlangeOffsetUpToVertex)
                o["NormalToFlangePlane"] = f.NormalToFlangePlane;
            if (f.OffsetType == (int)swFlangeOffsetTypes_e.swFlangeOffsetUptoEdgeAndMerge)
                o["LockAngle"] = f.LockAngle;
            if (f.UsePositionOffset)
            { o["PositionOffsetType"] = f.PositionOffsetType; o["PositionOffsetDistance"] = f.PositionOffsetDistance; }
            if (!f.UseDefaultBendRelief)
            {
                o["AutoReliefType"] = f.AutoReliefType;
                if (f.AutoReliefType == (int)swSheetMetalReliefTypes_e.swSheetMetalReliefTear)
                    o["ReliefTearType"] = f.ReliefTearType;
                if (f.AutoReliefType == (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular ||
                    f.AutoReliefType == (int)swSheetMetalReliefTypes_e.swSheetMetalReliefObround)
                {
                    o["UseReliefRatio"] = f.UseReliefRatio;
                    if (f.UseReliefRatio) o["ReliefRatio"] = f.ReliefRatio;
                    else { o["ReliefWidth"] = f.ReliefWidth; o["ReliefDepth"] = f.ReliefDepth; }
                }
            }
            if (!f.UseDefaultBendAllowance)
            {
                var allowance = f.GetCustomBendAllowance();
                if (allowance == null) throw new InvalidOperationException("FLANGE45 missing custom bend allowance.");
                o["AllowanceType"] = allowance.Type;
                switch ((swBendAllowanceTypes_e)allowance.Type)
                {
                    case swBendAllowanceTypes_e.swBendAllowanceKFactor: o["KFactor"] = allowance.KFactor; break;
                    case swBendAllowanceTypes_e.swBendAllowanceDirect: o["BendAllowance"] = allowance.BendAllowance; break;
                    case swBendAllowanceTypes_e.swBendAllowanceDeduction: o["BendDeduction"] = allowance.BendDeduction; break;
                    case swBendAllowanceTypes_e.swBendAllowanceBendTable:
                    case swBendAllowanceTypes_e.swBendAllowanceBendCalculationTable: o["BendTableFile"] = allowance.BendTableFile; break;
                    default: throw new InvalidOperationException("FLANGE45 unsupported allowance type: " + allowance.Type);
                }
            }
            return o;
        }

    }

    public static class FlangeGeometry33
    {
        // Translation cancels out: offset sketch planes are accepted, but the
        // selected edge must follow the native profile's local X direction.
        public static double ProfileAxisAlignment(double[] localStart, double[] localEnd)
        {
            if (!Valid(localStart) || !Valid(localEnd)) return 0;
            double dx = localEnd[0] - localStart[0];
            double dy = localEnd[1] - localStart[1];
            double dz = localEnd[2] - localStart[2];
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return length > 1e-9 ? Math.Abs(dx) / length : 0;
        }

        // Return an assignment only when exactly one injective mapping exists.
        // Extra absorbed sketches are allowed; array/tree order never decides a tie.
        public static int[] UniqueAssignment(IList<int[]> candidates, int profileCount)
        {
            if (candidates == null || profileCount < candidates.Count ||
                candidates.Any(c => c == null || c.Length == 0 || c.Any(j => j < 0 || j >= profileCount)))
                return null;
            int[] current = Enumerable.Repeat(-1, candidates.Count).ToArray();
            int[] result = null;
            bool[] used = new bool[profileCount];
            int solutions = 0;
            Action<int> visit = null;
            visit = depth =>
            {
                if (solutions > 1) return;
                if (depth == candidates.Count)
                {
                    result = (int[])current.Clone();
                    solutions++;
                    return;
                }
                foreach (int j in candidates[depth].Distinct())
                {
                    if (used[j]) continue;
                    current[depth] = j;
                    used[j] = true;
                    visit(depth + 1);
                    used[j] = false;
                    if (solutions > 1) return;
                }
            };
            visit(0);
            return solutions == 1 ? result : null;
        }

        public static double ReflectedAngle(double source, bool reversed)
        {
            if (double.IsNaN(source) || double.IsInfinity(source)) throw new ArgumentException("Invalid angle.");
            // R Rot(t,a) R^-1 = Rot(Rt,-a); an antiparallel target hinge reverses sign again.
            double value = reversed ? source : -source;
            return (value % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
        }

        public static bool OnLine(double[] p, double[] a, double[] b, double tolerance)
        {
            if (!Valid(p) || !Valid(a) || !Valid(b) || tolerance <= 0 || double.IsNaN(tolerance) || double.IsInfinity(tolerance)) return false;
            double[] d = { b[0] - a[0], b[1] - a[1], b[2] - a[2] };
            double l2 = d.Sum(x => x * x);
            if (l2 <= tolerance * tolerance) return false;
            double t = Enumerable.Range(0, 3).Sum(i => (p[i] - a[i]) * d[i]) / l2;
            return Enumerable.Range(0, 3).Sum(i => Math.Pow(p[i] - a[i] - t * d[i], 2)) <= tolerance * tolerance;
        }

        public static bool OverlapsHinge(double[] p, double[] q, double[] a, double[] b, double tolerance)
        {
            if (!OnLine(p, a, b, tolerance) || !OnLine(q, a, b, tolerance)) return false;
            double[] d = { b[0] - a[0], b[1] - a[1], b[2] - a[2] };
            double l2 = d.Sum(x => x * x);
            double u = Enumerable.Range(0, 3).Sum(i => (p[i] - a[i]) * d[i]) / l2;
            double v = Enumerable.Range(0, 3).Sum(i => (q[i] - a[i]) * d[i]) / l2;
            return (Math.Min(1, Math.Max(u, v)) - Math.Max(0, Math.Min(u, v))) * Math.Sqrt(l2) > tolerance;
        }

        private static bool Valid(double[] p) { return p != null && p.Length == 3 && p.All(x => !double.IsNaN(x) && !double.IsInfinity(x)); }
    }
}
