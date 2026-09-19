using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    public sealed class FeatureReplayCheckpointV7
    {
        public MirrorV7FeatureNode Feature { get; internal set; }
        public List<Body2> ReflectedBodyOracle { get; internal set; }
        public MirrorV7FeatureResult Result { get; internal set; }
        internal Action<object, ModelDoc2> Rebind { get; set; }
        internal string BindingError27 { get; set; }
        internal Dictionary<string, object> Options27 { get; set; }
        internal BaseSketchMutationEngineV7.DrivingSnapshot Snapshot { get; set; }
    }

    public static class RollbackReplayEngineV7
    {
        internal static Dictionary<string, FeatureReplayCheckpointV7> CaptureMappedHandlers(
            MirrorInPlaceExecutionContextV7 context)
        {
            var result = new Dictionary<string, FeatureReplayCheckpointV7>();
            var graph = FeatureTreeScannerV7.Scan(context.WorkingDocument);
            try
            {
                foreach (var node in graph.Nodes.Where(n => n.Depth == 0 && !n.IsSuppressed &&
                    DefinitionType(n.Feature.GetDefinition()) != null).OrderBy(n => n.TreeOrder))
                {
                    MoveRollbackAfter(context.WorkingDocument, node.Feature);
                    context.WorkingDocument.EditRebuild3();
                    EnsureFeatureHasNoError(node.Feature, "Original mapped checkpoint");
                    var item = new FeatureReplayCheckpointV7 { Feature = node,
                        ReflectedBodyOracle = BaseSketchMutationEngineV7.ReflectCurrentSolids(context) };
                    item.Options27 = ReadOptions27(node.Feature.GetDefinition());
                    try { item.Rebind = CaptureBindings(context, node.Feature); }
                    catch (InvalidOperationException ex)
                    {
                        // Inherited native references may already rebuild correctly. Never guess
                        // missing mappings: accept only the full checkpoint oracle in that case.
                        item.BindingError27 = ex.Message;
                        MirrorV7Diagnostics.Log("[FEATURE27][LIMITED_MAPPING] feature=" + node.Name + " reason=" + ex.Message);
                    }
                    result.Add(node.Name, item);
                    MirrorV7Diagnostics.Log("[MAPPED12][CAPTURE] feature=" + node.Name);
                }
            }
            finally
            {
                if (!context.WorkingDocument.FeatureManager.EditRollback(
                    (int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, ""))
                    throw new InvalidOperationException("Cannot restore rollback after mapped capture.");
                context.WorkingDocument.EditRebuild3();
            }
            return result;
        }

        internal static void ReplayMappedHandler(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayCheckpointV7 item)
        {
            ExecuteMappedDefinition(context, item);
        }
        public static List<FeatureReplayCheckpointV7> CaptureAll(MirrorInPlaceExecutionContextV7 context)
        {
            var result = new List<FeatureReplayCheckpointV7>();
            foreach (var node in context.WorkingGraph.Nodes.Where(x => x.Depth == 0 && !x.IsSuppressed &&
                x.TreeOrder > context.BasePrescription.TreeOrder && AltersSolidBody(x)).OrderBy(x => x.TreeOrder))
            {
                MoveRollbackAfter(context.WorkingDocument, node.Feature);
                context.WorkingDocument.EditRebuild3();
                EnsureFeatureHasNoError(node.Feature, "Source checkpoint cannot rebuild: " + node.Name);
                var item = new FeatureReplayCheckpointV7 { Feature = node,
                    ReflectedBodyOracle = BaseSketchMutationEngineV7.ReflectCurrentSolids(context) };
                item.Rebind = CaptureBindings(context, node.Feature);
                if (node.Feature.GetDefinition() is IExtrudeFeatureData2)
                    item.Snapshot = BaseSketchMutationEngineV7.CaptureDrivingSnapshot(context, node);
                result.Add(item);
                MirrorV7Diagnostics.Log("[INPLACE][SEQUENCE_CAPTURE] order=" + node.TreeOrder +
                    " feature=" + node.Name + " type=" + node.TypeName);
            }
            return result;
        }

        public static FeatureReplayJournalV7 ExecuteAll(MirrorInPlaceExecutionContextV7 context,
            IList<FeatureReplayCheckpointV7> sequence)
        {
            var journal = new FeatureReplayJournalV7();
            foreach (var item in sequence)
            {
                try
                {
                    MoveRollbackAfter(context.WorkingDocument, item.Feature.Feature);
                    context.WorkingDocument.EditRebuild3();
                    bool inherited = false;
                    try
                    {
                        BaseSketchMutationEngineV7.VerifyCurrentSolids(context, item.ReflectedBodyOracle);
                        EnsureFeatureHasNoError(item.Feature.Feature, "Inherited feature error");
                        inherited = true;
                    }
                    catch (InvalidOperationException) { }
                    if (inherited)
                        item.Result = new MirrorV7FeatureResult { FeatureName = item.Feature.Name,
                            FeatureType = item.Feature.TypeName, Status = MirrorV7ReplayStatus.RecoveredEquivalent,
                            Message = "Individual reflected body oracle passed after inherited rebuild." };
                    else if (item.Feature.Feature.GetDefinition() is IExtrudeFeatureData2)
                        ExecuteCheckpoint(context, item);
                    else
                        ExecuteMappedDefinition(context, item);
                    if (item.Result == null || !item.Result.Success)
                        throw new InvalidOperationException(item.Result == null ? "No handler result" : item.Result.Message);
                    journal.Add(item.Result);
                    MirrorV7Diagnostics.Log("[INPLACE][SEQUENCE_PASS] feature=" + item.Feature.Name +
                        " order=" + item.Feature.TreeOrder + " strategy=" + item.Result.Message);
                }
                catch (Exception ex)
                {
                    MirrorV7Diagnostics.Log("[INPLACE][SEQUENCE_FAIL] feature=" + item.Feature.Name +
                        " type=" + item.Feature.TypeName + " passed=" + journal.Entries.Count +
                        " total=" + sequence.Count + " reason=" + ex.Message);
                    throw new InvalidOperationException("Replay failed at '" + item.Feature.Name + "' [" +
                        item.Feature.TypeName + "] after " + journal.Entries.Count + "/" + sequence.Count +
                        " features: " + ex.Message, ex);
                }
            }
            return journal;
        }

        private static Dictionary<string, object> ReadOptions27(object data)
        {
            var result = new Dictionary<string, object>();
            Type type = DefinitionType(data);
            if (type == null) return result;
            foreach (var property in type.GetProperties().Where(p => p.CanRead && p.CanWrite &&
                p.GetIndexParameters().Length == 0 && (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum)))
            {
                // Only these orientation options are explored by the bounded candidate solver.
                if (new[] { "ReverseOffset", "ReversePositionOffset", "D1ReverseDirection", "D2ReverseDirection", "ReverseDirection" }.Contains(property.Name)) continue;
                result.Add(property.Name, property.GetValue(data, null));
            }
            return result;
        }

        private static void VerifyOptions27(FeatureReplayCheckpointV7 item)
        {
            if (item.Options27 == null) return;
            var actual = ReadOptions27(item.Feature.Feature.GetDefinition());
            foreach (var pair in item.Options27)
                if (!actual.ContainsKey(pair.Key) || !object.Equals(pair.Value, actual[pair.Key]))
                    throw new InvalidOperationException("FEATURE27 source option changed: " + pair.Key);
            MirrorV7Diagnostics.Log("[FEATURE27][OPTIONS_PASS] feature=" + item.Feature.Name + " preserved=" + actual.Count);
        }

        private static Type DefinitionType(object data)
        {
            if (data is IEdgeFlangeFeatureData) return typeof(IEdgeFlangeFeatureData);
            if (data is IChamferFeatureData2) return typeof(IChamferFeatureData2);
            if (data is ILinearPatternFeatureData) return typeof(ILinearPatternFeatureData);
            if (data is ICircularPatternFeatureData) return typeof(ICircularPatternFeatureData);
            if (data is ICurveDrivenPatternFeatureData) return typeof(ICurveDrivenPatternFeatureData);
            return null;
        }

        private static bool AccessDefinition(object data, ModelDoc2 model)
        {
            if (data is IEdgeFlangeFeatureData) return ((IEdgeFlangeFeatureData)data).AccessSelections(model, null);
            if (data is IChamferFeatureData2) return ((IChamferFeatureData2)data).AccessSelections(model, null);
            if (data is ILinearPatternFeatureData) return ((ILinearPatternFeatureData)data).AccessSelections(model, null);
            if (data is ICircularPatternFeatureData) return ((ICircularPatternFeatureData)data).AccessSelections(model, null);
            if (data is ICurveDrivenPatternFeatureData) return ((ICurveDrivenPatternFeatureData)data).AccessSelections(model, null);
            throw new InvalidOperationException("Unsupported definition access interface.");
        }

        private static void ReleaseDefinition(object data)
        {
            if (data is IEdgeFlangeFeatureData) ((IEdgeFlangeFeatureData)data).ReleaseSelectionAccess();
            else if (data is IChamferFeatureData2) ((IChamferFeatureData2)data).ReleaseSelectionAccess();
            else if (data is ILinearPatternFeatureData) ((ILinearPatternFeatureData)data).ReleaseSelectionAccess();
            else if (data is ICircularPatternFeatureData) ((ICircularPatternFeatureData)data).ReleaseSelectionAccess();
            else if (data is ICurveDrivenPatternFeatureData) ((ICurveDrivenPatternFeatureData)data).ReleaseSelectionAccess();
        }

        // Capture numeric geometry while references still belong to the unmodified staging body.
        // Never retain a source Edge COM pointer for use after changing the base topology.
        private static Action<object, ModelDoc2> CaptureBindings(MirrorInPlaceExecutionContextV7 context, Feature feature)
        {
            object data = feature.GetDefinition();
            Type type = DefinitionType(data);
            if (type == null) return null;
            if (!AccessDefinition(data, context.WorkingDocument))
                throw new InvalidOperationException("Cannot capture selections: " + feature.Name);
            var setters = new List<Action<object, ModelDoc2>>();
            try
            {
                string[] names = data is IEdgeFlangeFeatureData
                    ? new[] { "Edges", "OffsetReference", "PositionOffsetReference", "AngleReference" }
                    : data is IChamferFeatureData2 ? new[] { "Edges", "Faces", "Loops", "Vertex" }
                    : data is ILinearPatternFeatureData ? new[] { "D1Axis", "D2Axis", "D1EndReference", "D2EndReference", "D1EndSeedReference", "D2EndSeedReference", "PatternFeatureArray", "PatternFaceArray", "PatternBodyArray" }
                    : data is ICircularPatternFeatureData ? new[] { "Axis", "PatternFeatureArray", "PatternFaceArray", "PatternBodyArray" }
                    : new[] { "D1Direction", "D2Direction", "D1FaceNormal", "PatternFeatureArray", "PatternFaceArray", "PatternBodyArray" };
                foreach (string name in names)
                {
                    if (data is ICurveDrivenPatternFeatureData && name == "D2Direction" &&
                        !((ICurveDrivenPatternFeatureData)data).Dir2Specified) continue;
                    var pattern = data as ICurveDrivenPatternFeatureData;
                    if (pattern != null &&
                        ((name == "PatternFeatureArray" && pattern.GetPatternFeatureCount() == 0) ||
                         (name == "PatternFaceArray" && pattern.GetPatternFaceCount() == 0) ||
                         (name == "PatternBodyArray" && pattern.GetPatternBodyCount() == 0))) continue;
                    var property = type.GetProperty(name);
                    if (property == null || !property.CanRead || !property.CanWrite)
                        throw new InvalidOperationException("Mapping property unavailable: " + type.Name + "." + name);
                    object raw = property.GetValue(data, null);
                    if (raw == null) continue;
                    bool array = raw is Array;
                    object[] values = array ? ((Array)raw).Cast<object>().ToArray() : new[] { raw };
                    if (values.Length == 0) continue;
                    var maps = new List<Func<ModelDoc2, object>>();
                    foreach (object value in values)
                    {
                        Edge edge = value as Edge;
                        if (edge != null)
                        {
                            double[][] points = SampleEdge(edge).Select(context.Reflection.ReflectPoint).ToArray();
                            maps.Add(model => FindEdge(model, points));
                        }
                        else if (value is Face2)
                        {
                            Face2 face = (Face2)value;
                            double area = face.GetArea();
                            double[][][] boundaries = ((object[])face.GetEdges() ?? new object[0]).Cast<Edge>()
                                .Select(e => SampleEdge(e).Select(context.Reflection.ReflectPoint).ToArray()).ToArray();
                            maps.Add(model => FindFace(model, area, boundaries));
                        }
                        else if (value is Vertex)
                        {
                            double[] point = context.Reflection.ReflectPoint((double[])((Vertex)value).GetPoint());
                            maps.Add(model => FindVertex(model, point));
                        }
                        else if (value is Feature)
                        {
                            string nameOfFeature = ((Feature)value).Name;
                            string typeOfFeature = ((Feature)value).GetTypeName2();
                            maps.Add(model =>
                            {
                                var mapped = ((PartDoc)model).FeatureByName(nameOfFeature) as Feature;
                                if (mapped == null || mapped.GetTypeName2() != typeOfFeature)
                                    throw new InvalidOperationException("FEATURE27 missing/type-changed reference: " + nameOfFeature);
                                return mapped;
                            });
                        }
                        else
                        {
                            string kind = value is SketchSegment ? "SketchSegment" :
                                value is SketchPoint ? "SketchPoint" : value is Sketch ? "Sketch" :
                                value is Body2 ? "Body2" : value.GetType().FullName;
                            string unsupported = "[PREFLIGHT13] feature=" + feature.Name +
                                " property=" + name + " referenceType=" + kind +
                                ": reference mapper not implemented; no base mutation performed.";
                            MirrorV7Diagnostics.Log(unsupported);
                            throw new InvalidOperationException(unsupported);
                        }
                    }
                    setters.Add((definition, model) => property.SetValue(definition,
                        array ? (object)maps.Select(map => map(model)).ToArray() : maps[0](model), null));
                }
            }
            finally { ReleaseDefinition(data); }
            return (definition, model) => { foreach (var setter in setters) setter(definition, model); };
        }

        private static double[][] SampleEdge(Edge edge)
        {
            return ADDIN.Helpers.SketchOperationsHelper.SampleEdge24(edge);
        }

        private static Edge FindEdge(ModelDoc2 model, double[][] expected)
        {
            var matches = new List<Edge>();
            foreach (Body2 body in ((object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0]))
                foreach (Edge edge in ((object[])body.GetEdges() ?? new object[0]))
                {
                    double[][] actual = SampleEdge(edge);
                    bool same = Enumerable.Range(0, expected.Length).All(i => Distance(expected[i], actual[i]) < 1e-7);
                    bool reversed = Enumerable.Range(0, expected.Length).All(i => Distance(expected[i], actual[actual.Length - 1 - i]) < 1e-7);
                    if (same || reversed) matches.Add(edge);
                }
            if (matches.Count != 1) throw new InvalidOperationException("Reflected edge mapping is not unique; matches=" + matches.Count);
            return matches[0];
        }

        private static double Distance(double[] a, double[] b)
        {
            return Math.Sqrt(Enumerable.Range(0, 3).Sum(i => (a[i] - b[i]) * (a[i] - b[i])));
        }

        private static object FindVertex(ModelDoc2 model, double[] expected)
        {
            var matches = new List<Vertex>();
            foreach (Body2 body in ((object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0]))
                foreach (Vertex vertex in ((object[])body.GetVertices() ?? new object[0]))
                    if (Distance(expected, (double[])vertex.GetPoint()) < 1e-7) matches.Add(vertex);
            if (matches.Count != 1) throw new InvalidOperationException("Reflected vertex is ambiguous or missing: " + matches.Count);
            return matches[0];
        }

        internal static object FindFace(ModelDoc2 model, double area, double[][][] boundaries)
        {
            if (boundaries.Length == 0) throw new InvalidOperationException("Face without boundary needs a surface-specific mapper.");
            var matches = new List<Face2>();
            foreach (Body2 body in ((object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0]))
                foreach (Face2 face in ((object[])body.GetFaces() ?? new object[0]))
                {
                    if (Math.Abs(face.GetArea() - area) > Math.Max(1e-12, area * 1e-6)) continue;
                    var edges = ((object[])face.GetEdges() ?? new object[0]).Cast<Edge>().Select(SampleEdge).ToList();
                    if (edges.Count != boundaries.Length) continue;
                    if (BipartiteEquivalenceMatcherV7.Match(boundaries.ToList(), edges, (a, b) =>
                        Enumerable.Range(0, a.Length).All(i => Distance(a[i], b[i]) < 1e-7) ||
                        Enumerable.Range(0, a.Length).All(i => Distance(a[i], b[b.Length - 1 - i]) < 1e-7)).HasPerfectMatching)
                        matches.Add(face);
                }
            if (matches.Count != 1) throw new InvalidOperationException("Reflected face is ambiguous or missing: " + matches.Count);
            return matches[0];
        }

        private static void ExecuteMappedDefinition(MirrorInPlaceExecutionContextV7 context, FeatureReplayCheckpointV7 item)
        {
            Feature feature = item.Feature.Feature;
            MoveRollbackAfter(context.WorkingDocument, feature);
            context.WorkingDocument.EditRebuild3();
            try
            {
                EnsureFeatureHasNoError(feature, "FEATURE27 inherited rebuild");
                VerifyOptions27(item);
                BaseSketchMutationEngineV7.VerifyCurrentSolids(context, item.ReflectedBodyOracle);
                item.Result = new MirrorV7FeatureResult { FeatureName = item.Feature.Name,
                    FeatureType = item.Feature.TypeName, Status = MirrorV7ReplayStatus.RecoveredEquivalent,
                    Message = "FEATURE27 inherited native definition; checkpoint geometry PASS." };
                MirrorV7Diagnostics.Log("[FEATURE27][PASS] feature=" + feature.Name + " strategy=INHERITED_ORACLE");
                return;
            }
            catch (InvalidOperationException ex)
            {
                MirrorV7Diagnostics.Log("[FEATURE27][REBIND_REQUIRED] feature=" + feature.Name + " reason=" + ex.Message);
            }
            if (item.BindingError27 != null)
                throw new InvalidOperationException("FEATURE27 unsupported reference/options at " + feature.Name + ": " + item.BindingError27);
            object initial = feature.GetDefinition();
            Type type = DefinitionType(initial);
            if (type == null) throw new InvalidOperationException("Mutation handler unavailable for " + item.Feature.TypeName);
            string[] flags = initial is IEdgeFlangeFeatureData ? new[] { "ReverseOffset", "ReversePositionOffset" }
                : initial is IChamferFeatureData2 ? new string[0]
                : initial is ICircularPatternFeatureData ? new[] { "ReverseDirection" }
                : initial is ICurveDrivenPatternFeatureData && !((ICurveDrivenPatternFeatureData)initial).Dir2Specified
                    ? new[] { "D1ReverseDirection" } : new[] { "D1ReverseDirection", "D2ReverseDirection" };
            bool[] baseline = flags.Select(n => (bool)type.GetProperty(n).GetValue(initial, null)).ToArray();
            for (int mask = 0; mask < (1 << flags.Length); mask++)
            {
                object data = feature.GetDefinition();
                bool opened = AccessDefinition(data, context.WorkingDocument);
                MirrorV7Diagnostics.Log("[MAPPED12][ACCESS] feature=" + feature.Name + " mask=" + mask + " opened=" + opened);
                if (!opened) throw new InvalidOperationException("Cannot access definition of " + feature.Name);
                try
                {
                    if (item.Rebind != null) item.Rebind(data, context.WorkingDocument);
                    for (int i = 0; i < flags.Length; i++)
                        type.GetProperty(flags[i]).SetValue(data, baseline[i] ^ ((mask & (1 << i)) != 0), null);
                    bool modified = feature.ModifyDefinition(data, context.WorkingDocument, null);
                    MirrorV7Diagnostics.Log("[MAPPED12][MODIFY] feature=" + feature.Name + " mask=" + mask + " success=" + modified);
                    if (!modified) continue;
                    opened = false;
                    MoveRollbackAfter(context.WorkingDocument, feature);
                    context.WorkingDocument.EditRebuild3();
                    try
                    {
                        EnsureFeatureHasNoError(feature, "Mapped feature rebuild error");
                        VerifyOptions27(item);
                        BaseSketchMutationEngineV7.VerifyCurrentSolids(context, item.ReflectedBodyOracle);
                        item.Result = new MirrorV7FeatureResult { FeatureName = item.Feature.Name,
                            FeatureType = item.Feature.TypeName, Status = MirrorV7ReplayStatus.ExactReplay,
                            Message = "Mapped references and options mask=" + mask + "; per-feature oracle PASS." };
                        MirrorV7Diagnostics.Log("[FEATURE27][PASS] feature=" + feature.Name + " strategy=REBOUND mask=" + mask);
                        return;
                    }
                    catch (InvalidOperationException ex)
                    {
                        MirrorV7Diagnostics.Log("[INPLACE][MAPPED_CANDIDATE_REJECTED] feature=" + feature.Name +
                            " mask=" + mask + " reason=" + ex.Message);
                    }
                }
                finally { if (opened) ReleaseDefinition(data); }
            }
            throw new InvalidOperationException("No mapped definition candidate matches reflected checkpoint: " + feature.Name);
        }

        public static FeatureReplayCheckpointV7 CaptureFirstTopLevelBodyFeatureOracle(
            MirrorInPlaceExecutionContextV7 context)
        {
            if (context == null || context.WorkingGraph == null || context.BasePrescription == null)
                throw new ArgumentNullException("context");
            MirrorV7FeatureNode target = context.WorkingGraph.Nodes
                .Where(x => x != null && x.Depth == 0 && !x.IsSuppressed &&
                    x.TreeOrder > context.BasePrescription.TreeOrder && AltersSolidBody(x))
                .OrderBy(x => x.TreeOrder).FirstOrDefault();
            if (target == null) return null;

            FeatureManager manager = context.WorkingDocument.FeatureManager;
            if (manager == null || !manager.EditRollback(
                (int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, target.Name))
                throw new InvalidOperationException("Cannot capture replay oracle after feature: " + target.Name);
            if (!context.WorkingDocument.EditRebuild3())
                throw new InvalidOperationException("Cannot rebuild while capturing replay oracle: " + target.Name);
            List<Body2> oracle = BaseSketchMutationEngineV7.ReflectCurrentSolids(context);
            MirrorV7Diagnostics.Log("[INPLACE][REPLAY_ORACLE_CAPTURED] feature=\"" + target.Name +
                "\" type=\"" + target.TypeName + "\" treeOrder=" + target.TreeOrder +
                " bodies=" + oracle.Count);
            return new FeatureReplayCheckpointV7
            {
                Feature = target,
                ReflectedBodyOracle = oracle
            };
        }

        public static void ExecuteCheckpoint(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayCheckpointV7 checkpoint)
        {
            if (checkpoint == null) return;
            MirrorV7FeatureNode feature = checkpoint.Feature;
            string t = Normalize(feature.TypeName);
            if (!(feature.Feature.GetDefinition() is IExtrudeFeatureData2))
            {
                checkpoint.Result = new MirrorV7FeatureResult
                {
                    FeatureName = feature.Name,
                    FeatureType = feature.TypeName,
                    Status = MirrorV7ReplayStatus.NotProcessed,
                    Message = "First downstream body feature is not an Extrude Cut; checkpoint stopped without changing it."
                };
                MirrorV7Diagnostics.Log("[INPLACE][REPLAY_CHECKPOINT_STOP] feature=\"" + feature.Name +
                    "\" type=\"" + feature.TypeName + "\" reason=HandlerNotEnabled");
                return;
            }

            FeatureManager manager = context.WorkingDocument.FeatureManager;
            try
            {
                if (manager == null || !manager.EditRollback(
                    (int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, feature.Name))
                    throw new InvalidOperationException("Cannot move rollback bar before replay feature.");

                ContourSelectionSnapshotV7 contourSelection = CaptureContourSelection(
                    context.WorkingDocument, feature.Feature);
                string sketchName;
                int moving;
                DrivingSketchReflectionV7 sketchReflection;
                string sketchStrategy = checkpoint.Snapshot == null
                    ? BaseSketchMutationEngineV7.ReflectDrivingSketchForFeature(
                        context, feature, out sketchName, out moving, out sketchReflection)
                    : BaseSketchMutationEngineV7.ApplyDrivingSnapshot(context, feature, checkpoint.Snapshot,
                        out sketchName, out moving, out sketchReflection);
                if (!manager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, feature.Name))
                    throw new InvalidOperationException("Cannot move rollback bar after replay feature.");
                bool initialRebuildSucceeded = context.WorkingDocument.EditRebuild3();
                MirrorV7Diagnostics.Log("[INPLACE][CUT_REBUILD_INITIAL] feature=\"" + feature.Name +
                    "\" success=" + initialRebuildSucceeded + " " + FeatureError(feature.Feature));
                if (sketchReflection.ReflectedSegments.Count != 0)
                {
                    RebindExtrudeContours(context.WorkingDocument, feature.Feature,
                        contourSelection, sketchReflection);
                    MoveRollbackAfter(context.WorkingDocument, feature.Feature);
                    initialRebuildSucceeded = context.WorkingDocument.EditRebuild3();
                    MirrorV7Diagnostics.Log("[INPLACE][CUT_REBUILD_AFTER_CONTOUR] feature=\"" + feature.Name +
                        "\" success=" + initialRebuildSucceeded + " " + FeatureError(feature.Feature));
                }

                string optionStrategy = "OriginalExtrudeOptions";
                try
                {
                    BaseSketchMutationEngineV7.VerifyCurrentSolids(context, checkpoint.ReflectedBodyOracle);
                    EnsureFeatureHasNoError(feature.Feature, "Cut body matched the oracle but the feature reports an error.");
                    if (!initialRebuildSucceeded)
                        optionStrategy = "OriginalExtrudeOptionsBodyOracleOverride";
                }
                catch (InvalidOperationException firstError)
                {
                    if (!TryAdjustExtrudeCutOptions(context, feature.Feature, checkpoint.ReflectedBodyOracle))
                        throw new InvalidOperationException("Reflected Cut sketch rebuilt, but no supported direction/side combination matched its body oracle. " +
                            firstError.Message, firstError);
                    optionStrategy = "OracleSelectedExtrudeOptions";
                }

                int saveErrors = 0, saveWarnings = 0;
                if (!context.WorkingDocument.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    ref saveErrors, ref saveWarnings) || saveErrors != 0)
                    throw new InvalidOperationException("Cannot save replay checkpoint. errors=" + saveErrors +
                        " warnings=" + saveWarnings);
                checkpoint.Result = new MirrorV7FeatureResult
                {
                    FeatureName = feature.Name,
                    FeatureType = feature.TypeName,
                    Status = MirrorV7ReplayStatus.ExactReplay,
                    Message = "Sequential Cut checkpoint passed. sketch=" + sketchName +
                        ", movingPoints=" + moving + ", sketchStrategy=" + sketchStrategy +
                        ", optionStrategy=" + optionStrategy
                };
                MirrorV7Diagnostics.Log("[INPLACE][REPLAY_CHECKPOINT_PASS] feature=\"" + feature.Name +
                    "\" type=\"" + feature.TypeName + "\" sketch=\"" + sketchName +
                    "\" moving=" + moving + " sketchStrategy=" + sketchStrategy +
                    " optionStrategy=" + optionStrategy + " geometryOracle=True saved=True");
            }
            catch (Exception ex)
            {
                checkpoint.Result = new MirrorV7FeatureResult
                {
                    FeatureName = feature.Name,
                    FeatureType = feature.TypeName,
                    Status = MirrorV7ReplayStatus.Failed,
                    Message = ex.Message
                };
                MirrorV7Diagnostics.LogException("REPLAY_CHECKPOINT", ex);
            }
        }

        public static FeatureReplayJournalV7 BuildPreparationJournal(
            MirrorInPlaceExecutionContextV7 context, FeatureDispatcherV7 dispatcher)
        {
            if (context == null || context.WorkingGraph == null) throw new ArgumentNullException("context");
            if (dispatcher == null) throw new ArgumentNullException("dispatcher");
            FeatureReplayJournalV7 journal = new FeatureReplayJournalV7();
            foreach (MirrorV7FeatureNode feature in context.WorkingGraph.Nodes)
            {
                if (context.ReplayCheckpoint != null && context.ReplayCheckpoint.Result != null &&
                    context.ReplayCheckpoint.Feature.TreeOrder == feature.TreeOrder)
                {
                    journal.Add(context.ReplayCheckpoint.Result);
                    continue;
                }
                IFeatureHandlerV7 handler = dispatcher.Resolve(feature);
                journal.Add(handler == null
                    ? new MirrorV7FeatureResult
                    {
                        FeatureName = feature.Name,
                        FeatureType = feature.TypeName,
                        Status = MirrorV7ReplayStatus.Unsupported,
                        Message = "No in-place handler registered."
                    }
                    : handler.Mutate(context, feature));
            }
            return journal;
        }

        private static bool AltersSolidBody(MirrorV7FeatureNode feature)
        {
            return feature.Role == MirrorV7FeatureRole.BodyFeature ||
                   feature.Role == MirrorV7FeatureRole.Pattern ||
                   feature.Role == MirrorV7FeatureRole.SheetMetal;
        }

        private sealed class ContourSelectionSnapshotV7
        {
            public bool SelectAll { get; set; }
            public List<List<SketchSegment>> SelectedContours { get; private set; }
            public ContourSelectionSnapshotV7() { SelectedContours = new List<List<SketchSegment>>(); }
        }

        private static ContourSelectionSnapshotV7 CaptureContourSelection(ModelDoc2 model, Feature feature)
        {
            IExtrudeFeatureData2 data = feature.GetDefinition() as IExtrudeFeatureData2;
            if (data == null) throw new InvalidOperationException("Extrude Cut definition is unavailable before contour capture.");
            bool accessed = false;
            try
            {
                accessed = data.AccessSelections(model, null);
                if (!accessed) throw new InvalidOperationException("Cannot access Extrude Cut selections before contour capture.");
                object raw = data.Contours;
                ContourSelectionSnapshotV7 result = new ContourSelectionSnapshotV7 { SelectAll = raw == null };
                if (raw == null) return result;
                object[] contours = raw as object[] ?? new object[] { raw };
                foreach (object item in contours)
                {
                    SketchContour contour = item as SketchContour;
                    if (contour == null) throw new InvalidOperationException("Unexpected Extrude Cut contour object.");
                    object[] segmentObjects = contour.GetSketchSegments() as object[];
                    List<SketchSegment> segments = segmentObjects == null ? new List<SketchSegment>() : segmentObjects
                        .Select(x => x as SketchSegment).Where(x => x != null).ToList();
                    if (segments.Count == 0) throw new InvalidOperationException("Selected Extrude Cut contour has no sketch segments.");
                    result.SelectedContours.Add(segments);
                }
                return result;
            }
            finally
            {
                if (accessed) try { data.ReleaseSelectionAccess(); } catch { }
            }
        }

        private static void RebindExtrudeContours(ModelDoc2 model, Feature feature,
            ContourSelectionSnapshotV7 selection, DrivingSketchReflectionV7 reflection)
        {
            object[] rawContours = reflection.Sketch.GetSketchContours() as object[];
            List<SketchContour> available = rawContours == null ? new List<SketchContour>() : rawContours
                .Select(x => x as SketchContour).Where(x => x != null).ToList();
            if (available.Count == 0)
                throw new InvalidOperationException("Reflected driving sketch produced no selectable contours.");

            List<SketchContour> selected = new List<SketchContour>();
            if (selection.SelectAll)
            {
                selected.AddRange(available);
            }
            else
            {
                foreach (List<SketchSegment> originalContour in selection.SelectedContours)
                {
                    List<int> expected = originalContour.Select(x => IndexOfSegment(reflection.SourceSegments, x))
                        .OrderBy(x => x).ToList();
                    if (expected.Any(x => x < 0))
                        throw new InvalidOperationException("An original selected contour is not part of the reflected profile.");
                    SketchContour match = available.FirstOrDefault(x => ContourUsesReflectedIndices(
                        x, reflection.ReflectedSegments, expected));
                    if (match == null)
                        throw new InvalidOperationException("Cannot map an original selected contour to reflected sketch geometry.");
                    selected.Add(match);
                }
            }

            IExtrudeFeatureData2 data = feature.GetDefinition() as IExtrudeFeatureData2;
            if (data == null) throw new InvalidOperationException("Extrude Cut definition is unavailable during contour rebind.");
            bool accessed = false;
            try
            {
                accessed = data.AccessSelections(model, null);
                if (!accessed) throw new InvalidOperationException("Cannot access Extrude Cut selections during contour rebind.");
                data.Contours = selected.ToArray();
                if (!feature.ModifyDefinition(data, model, null))
                    throw new InvalidOperationException("Extrude Cut rejected reflected contour selections.");
                accessed = false;
                MirrorV7Diagnostics.Log("[INPLACE][CUT_CONTOUR_REBOUND] feature=\"" + feature.Name +
                    "\" selectedContours=" + selected.Count + " availableContours=" + available.Count);
            }
            finally
            {
                if (accessed) try { data.ReleaseSelectionAccess(); } catch { }
            }
        }

        private static bool ContourUsesReflectedIndices(SketchContour contour,
            IList<SketchSegment> reflected, IList<int> expected)
        {
            object[] raw = contour.GetSketchSegments() as object[];
            if (raw == null) return false;
            List<int> actual = raw.Select(x => x as SketchSegment).Where(x => x != null)
                .Select(x => IndexOfSegment(reflected, x)).OrderBy(x => x).ToList();
            return actual.Count == expected.Count && actual.SequenceEqual(expected);
        }

        private static int IndexOfSegment(IList<SketchSegment> segments, SketchSegment candidate)
        {
            for (int i = 0; i < segments.Count; i++)
                if (SingleSketchTargetBuilderV7.SameComObject(segments[i], candidate)) return i;
            return -1;
        }

        private static bool TryAdjustExtrudeCutOptions(MirrorInPlaceExecutionContextV7 context,
            Feature feature, List<Body2> oracle)
        {
            IExtrudeFeatureData2 initial = feature.GetDefinition() as IExtrudeFeatureData2;
            if (initial == null) return false;
            bool reverse = initial.ReverseDirection;
            bool flip = initial.FlipSideToCut;
            bool fromReverse = initial.FromOffsetReverse;
            int[] candidates = Enumerable.Range(1, 7).OrderBy(BitCount).ToArray();
            foreach (int mask in candidates)
            {
                IExtrudeFeatureData2 data = feature.GetDefinition() as IExtrudeFeatureData2;
                if (data == null) continue;
                bool accessed = false;
                try
                {
                    accessed = data.AccessSelections(context.WorkingDocument, null);
                    if (!accessed) continue;
                    data.ReverseDirection = reverse ^ ((mask & 1) != 0);
                    data.FlipSideToCut = flip ^ ((mask & 2) != 0);
                    data.FromOffsetReverse = fromReverse ^ ((mask & 4) != 0);
                    if (!feature.ModifyDefinition(data, context.WorkingDocument, null))
                    {
                        MirrorV7Diagnostics.Log("[INPLACE][CUT_OPTION_CANDIDATE] feature=\"" + feature.Name +
                            "\" mask=" + mask + " modify=False " + FeatureError(feature));
                        continue;
                    }
                    accessed = false;
                    MoveRollbackAfter(context.WorkingDocument, feature);
                    bool rebuilt = context.WorkingDocument.EditRebuild3();
                    MirrorV7Diagnostics.Log("[INPLACE][CUT_OPTION_CANDIDATE] feature=\"" + feature.Name +
                        "\" mask=" + mask + " modify=True rebuild=" + rebuilt + " " + FeatureError(feature));
                    try
                    {
                        BaseSketchMutationEngineV7.VerifyCurrentSolids(context, oracle);
                        EnsureFeatureHasNoError(feature, "Cut candidate body matched the oracle but the feature reports an error.");
                        MirrorV7Diagnostics.Log("[INPLACE][CUT_OPTION_PASS] feature=\"" + feature.Name +
                            "\" mask=" + mask + " reverseDirection=" + data.ReverseDirection +
                            " flipSideToCut=" + data.FlipSideToCut +
                            " fromOffsetReverse=" + data.FromOffsetReverse +
                            " rebuild=" + rebuilt + " oracleOverride=" + (!rebuilt));
                        return true;
                    }
                    catch (InvalidOperationException oracleError)
                    {
                        MirrorV7Diagnostics.Log("[INPLACE][CUT_OPTION_ORACLE_REJECTED] feature=\"" + feature.Name +
                            "\" mask=" + mask + " message=" + oracleError.Message);
                    }
                }
                catch (Exception ex)
                {
                    MirrorV7Diagnostics.Log("[INPLACE][CUT_OPTION_REJECTED] feature=\"" + feature.Name +
                        "\" mask=" + mask + " message=" + ex.Message);
                }
                finally
                {
                    if (accessed) try { data.ReleaseSelectionAccess(); } catch { }
                }
            }
            try
            {
                RestoreExtrudeCutOptions(context, feature, reverse, flip, fromReverse);
            }
            catch (Exception restoreError)
            {
                // The staging document is discarded on failure. Preserve the diagnostic
                // cause from the option search instead of masking it with restore failure.
                MirrorV7Diagnostics.Log("[INPLACE][CUT_OPTION_RESTORE_FAILED] feature=\"" + feature.Name +
                    "\" message=" + restoreError.Message + " " + FeatureError(feature));
            }
            return false;
        }

        private static string FeatureError(Feature feature)
        {
            if (feature == null) return "featureError=<null>";
            try
            {
                bool warning;
                int code = feature.GetErrorCode2(out warning);
                return "featureError=" + code + " warning=" + warning + " editStatus=" + feature.GetEditStatus();
            }
            catch (Exception ex)
            {
                return "featureErrorUnavailable=\"" + ex.Message + "\"";
            }
        }

        private static void EnsureFeatureHasNoError(Feature feature, string message)
        {
            if (feature == null) throw new InvalidOperationException(message + " feature=<null>");
            bool warning;
            int code = feature.GetErrorCode2(out warning);
            if (code != 0 && !warning)
                throw new InvalidOperationException(message + " errorCode=" + code);
        }

        private static void RestoreExtrudeCutOptions(MirrorInPlaceExecutionContextV7 context,
            Feature feature, bool reverse, bool flip, bool fromReverse)
        {
            IExtrudeFeatureData2 data = feature.GetDefinition() as IExtrudeFeatureData2;
            if (data == null) throw new InvalidOperationException("Cannot restore Extrude Cut options.");
            bool accessed = false;
            try
            {
                accessed = data.AccessSelections(context.WorkingDocument, null);
                if (!accessed) throw new InvalidOperationException("Cannot access Extrude Cut selections while restoring options.");
                data.ReverseDirection = reverse;
                data.FlipSideToCut = flip;
                data.FromOffsetReverse = fromReverse;
                if (!feature.ModifyDefinition(data, context.WorkingDocument, null))
                    throw new InvalidOperationException("ModifyDefinition rejected original Extrude Cut options.");
                accessed = false;
                MoveRollbackAfter(context.WorkingDocument, feature);
                if (!context.WorkingDocument.EditRebuild3())
                    throw new InvalidOperationException("Extrude Cut failed to rebuild after restoring options.");
            }
            finally
            {
                if (accessed) try { data.ReleaseSelectionAccess(); } catch { }
            }
        }

        private static void MoveRollbackAfter(ModelDoc2 model, Feature feature)
        {
            FeatureManager manager = model == null ? null : model.FeatureManager;
            if (manager == null || feature == null || !manager.EditRollback(
                (int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, feature.Name))
                throw new InvalidOperationException("Cannot restore rollback checkpoint after feature definition mutation: " +
                    (feature == null ? "<null>" : feature.Name));
        }

        private static int BitCount(int value)
        {
            int count = 0;
            while (value != 0) { count += value & 1; value >>= 1; }
            return count;
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
