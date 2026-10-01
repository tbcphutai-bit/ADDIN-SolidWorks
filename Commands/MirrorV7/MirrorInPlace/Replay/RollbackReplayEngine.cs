using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    public sealed class FeatureReplayCheckpointV7
    {
        public MirrorV7FeatureNode Feature { get; internal set; }
        public List<Body2> ReflectedBodyOracle { get; internal set; }
        public MirrorV7FeatureResult Result { get; internal set; }
        public bool WasNativeReplacement { get; internal set; }
        internal Action<object, ModelDoc2> Rebind { get; set; }
        internal Action<object, ModelDoc2> CreatePatternBinding36 { get; set; }
        internal RollbackReplayEngineV7.CurvePatternRecipe36 Pattern36 { get; set; }
        internal RollbackReplayEngineV7.SketchPatternRecipe59 SketchPattern59 { get; set; }
        internal string BindingError27 { get; set; }
        internal Dictionary<string, object> Options27 { get; set; }
        internal BaseSketchMutationEngineV7.DrivingSnapshot Snapshot { get; set; }
        internal RollbackReplayEngineV7.ChamferRecipe32 Chamfer { get; set; }
        internal RollbackReplayEngineV7.FlangeRecipe33 Flange { get; set; }
        internal RollbackReplayEngineV7.ChiralRecipe45 Chiral { get; set; }
        internal RollbackReplayEngineV7.MoveFaceRecipe64 MoveFace64 { get; set; }
    }

    public static partial class RollbackReplayEngineV7
    {
        internal static Dictionary<string, FeatureReplayCheckpointV7> CaptureMappedHandlers(
            MirrorInPlaceExecutionContextV7 context)
        {
            var result = new Dictionary<string, FeatureReplayCheckpointV7>();
            var unsupportedChiral45 = new List<string>();
            var graph = FeatureTreeScannerV7.Scan(context.WorkingDocument);
            // Freeze the pre-mutation profile/owner layout for explicit replacement
            // registration. Standalone legacy replay uses its own execution context.
            if (context.SourceGraph == null) context.SourceGraph = graph;
            CaptureDependencyGraph64(context);
            try
            {
                foreach (var node in graph.Nodes.Where(n => n.Depth == 0 && (!n.IsSuppressed || n.TypeName == "MoveFace") &&
                    DefinitionType(n.Feature.GetDefinition()) != null).OrderBy(n => n.TreeOrder))
                {
                    MoveRollbackAfter(context.WorkingDocument, node.Feature);
                    context.WorkingDocument.EditRebuild3();
                    if (!node.IsSuppressed) EnsureFeatureHasNoError(node.Feature, "Original mapped checkpoint");
                    var item = new FeatureReplayCheckpointV7 { Feature = node,
                        ReflectedBodyOracle = BaseSketchMutationEngineV7.ReflectCurrentSolids(context) };
                    item.Options27 = ReadOptions27(node.Feature.GetDefinition());
                    try
                    {
                        if (node.Feature.GetDefinition() is IMoveFaceFeatureData)
                            item.MoveFace64 = CaptureMoveFace64(context, node.Feature);
                        else if (IsChiral45(node.Feature.GetDefinition()))
                            item.Chiral = CaptureChiral45(context, node.Feature);
                        else if (node.Feature.GetDefinition() is IChamferFeatureData2)
                            item.Chamfer = CaptureChamfer32(context, node.Feature);
                        else if (node.Feature.GetDefinition() is IEdgeFlangeFeatureData)
                        {
                            item.Flange = CaptureFlange33(context, node.Feature);
                            item.Rebind = CaptureBindings(context, node.Feature, true);
                        }
                        else
                        {
                            item.Rebind = CaptureBindings(context, node.Feature);
                            if (node.Feature.GetDefinition() is ICurveDrivenPatternFeatureData)
                            {
                                item.Pattern36 = CaptureCurvePattern36(node.Feature);
                                item.CreatePatternBinding36 = CaptureBindings(context, node.Feature, false, true);
                            }
                            else if (node.Feature.GetDefinition() is ISketchPatternFeatureData)
                            {
                                item.SketchPattern59 = CaptureSketchPattern59(node.Feature, context.WorkingDocument);
                                item.CreatePatternBinding36 = CaptureBindings(context, node.Feature, false, true);
                            }
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Inherited native references may already rebuild correctly. Never guess
                        // missing mappings: accept only the full checkpoint oracle in that case.
                        item.BindingError27 = ex.Message;
                        if (IsChiral45(node.Feature.GetDefinition()))
                            unsupportedChiral45.Add(node.Name + ": " + ex.Message);
                        MirrorV7Diagnostics.Log("[FEATURE27][LIMITED_MAPPING] feature=" + node.Name + " reason=" + ex.Message);
                    }
                    result.Add(node.Name, item);
                    if (item.MoveFace64 != null || node.TypeName == "MoveFace")
                    {
                        var dependency = context.Dependencies64.Nodes[node.TreeOrder];
                        dependency.CaptureError = item.BindingError27;
                        dependency.Recoverable = item.MoveFace64 != null && item.BindingError27 == null;
                        context.Dependencies64.Recipes.Add(node.TreeOrder, item);
                    }
                    MirrorV7Diagnostics.Log("[MAPPED12][CAPTURE] feature=" + node.Name);
                }
                if (unsupportedChiral45.Count > 0)
                    throw new InvalidOperationException("CHIRAL45 preflight stopped BEFORE base mutation:\n" +
                        string.Join("\n", unsupportedChiral45));
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
            var mapped = CaptureMappedHandlers(context);
            foreach (var node in context.WorkingGraph.Nodes.Where(x => x.Depth == 0 && !x.IsSuppressed &&
                x.TreeOrder > context.BasePrescription.TreeOrder && AltersSolidBody(x)).OrderBy(x => x.TreeOrder))
            {
                MoveRollbackAfter(context.WorkingDocument, node.Feature);
                context.WorkingDocument.EditRebuild3();
                EnsureFeatureHasNoError(node.Feature, "Source checkpoint cannot rebuild: " + node.Name);
                FeatureReplayCheckpointV7 item;
                if (!mapped.TryGetValue(node.Name, out item))
                {
                    item = new FeatureReplayCheckpointV7 { Feature = node,
                        ReflectedBodyOracle = BaseSketchMutationEngineV7.ReflectCurrentSolids(context) };
                    item.Options27 = ReadOptions27(node.Feature.GetDefinition());
                    item.Rebind = CaptureBindings(context, node.Feature);
                }
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
                    RestoreDeferredThrough64(context, item.Feature.TreeOrder);
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
            RestoreDeferredThrough64(context, int.MaxValue);
            AssertDependencyReplayComplete64(context);
            return journal;
        }

        private static Dictionary<string, object> ReadOptions27(object data)
        {
            if (data is IEdgeFlangeFeatureData) return ReadFlangeOptions45((IEdgeFlangeFeatureData)data);
            if (IsChiral45(data)) return ReadChiralOptions45(data);
            var result = new Dictionary<string, object>();
            Type type = DefinitionType(data);
            if (type == null) return result;
            foreach (var property in type.GetProperties().Where(p => p.CanRead && p.CanWrite &&
                p.GetIndexParameters().Length == 0 && (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum)))
            {
                // Only these orientation options are explored by the bounded candidate solver.
                if (new[] { "ReverseOffset", "ReversePositionOffset", "D1ReverseDirection", "D2ReverseDirection", "ReverseDirection" }.Contains(property.Name)) continue;
                object value = property.GetValue(data, null);
                // A reflected bend can use the complementary directed angle. Preserve
                // its geometric magnitude while allowing the handedness to change.
                if (data is IEdgeFlangeFeatureData && property.Name == "BendAngle")
                    value = Math.Round(Math.Cos((double)value), 12);
                result.Add(property.Name, value);
            }
            return result;
        }

        private static void VerifyOptions27(FeatureReplayCheckpointV7 item)
        {
            if (item.Options27 == null) return;
            var actual = ReadOptions27(item.Feature.Feature.GetDefinition());
            foreach (var pair in item.Options27)
            {
                object value;
                if (!actual.TryGetValue(pair.Key, out value) || !SameScalarOption63(pair.Value, value))
                    throw new InvalidOperationException("FEATURE27 source option changed: " + pair.Key +
                        " expected=" + FormatScalarOption63(pair.Value) + " actual=" + FormatScalarOption63(value));
                if (!object.Equals(pair.Value, value))
                    MirrorV7Diagnostics.Log("[FEATURE63][NUMERIC_READBACK] feature=" + item.Feature.Name +
                        " option=" + pair.Key + " expected=" + FormatScalarOption63(pair.Value) +
                        " actual=" + FormatScalarOption63(value) + " proof=SCALAR_ROUNDOFF_ONLY");
            }
            MirrorV7Diagnostics.Log("[FEATURE27][OPTIONS_PASS] feature=" + item.Feature.Name + " preserved=" + actual.Count);
        }

        internal static bool SameScalarOption63(object expected, object actual)
        {
            if (expected is double && actual is double)
            {
                double a = (double)expected, b = (double)actual;
                if (double.IsNaN(a) || double.IsNaN(b) || double.IsInfinity(a) || double.IsInfinity(b)) return false;
                // A live native flange rebuild read 0.034 m back as
                // 0.034000000000000537 m. Bitwise Equals is not an option audit.
                // This is 1 pm at ordinary CAD lengths, far tighter than the
                // unchanged 0.1 um geometric gate. Boolean/enumerated choices
                // remain exact, and full geometry still has to pass separately.
                return Math.Abs(a - b) <= 1e-12 * Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));
            }
            return object.Equals(expected, actual);
        }

        private static string FormatScalarOption63(object value)
        {
            if (value == null) return "<missing>";
            if (value is double) return ((double)value).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static Type DefinitionType(object data)
        {
            if (data is IMoveFaceFeatureData) return typeof(IMoveFaceFeatureData);
            if (data is ISweepFeatureData) return typeof(ISweepFeatureData);
            if (data is ILoftFeatureData) return typeof(ILoftFeatureData);
            if (data is ILoftedBendsFeatureData) return typeof(ILoftedBendsFeatureData);
            if (data is IEdgeFlangeFeatureData) return typeof(IEdgeFlangeFeatureData);
            if (data is IChamferFeatureData2) return typeof(IChamferFeatureData2);
            if (data is ILinearPatternFeatureData) return typeof(ILinearPatternFeatureData);
            if (data is ICircularPatternFeatureData) return typeof(ICircularPatternFeatureData);
            if (data is ICurveDrivenPatternFeatureData) return typeof(ICurveDrivenPatternFeatureData);
            if (data is ISketchPatternFeatureData) return typeof(ISketchPatternFeatureData);
            return null;
        }

        private static bool AccessDefinition(object data, ModelDoc2 model)
        {
            if (data is IMoveFaceFeatureData) return ((IMoveFaceFeatureData)data).AccessSelections(model, null);
            if (data is ISweepFeatureData) return ((ISweepFeatureData)data).AccessSelections(model, null);
            if (data is ILoftFeatureData) return ((ILoftFeatureData)data).AccessSelections(model, null);
            if (data is ILoftedBendsFeatureData) return ((ILoftedBendsFeatureData)data).AccessSelections(model, null);
            if (data is IEdgeFlangeFeatureData) return ((IEdgeFlangeFeatureData)data).AccessSelections(model, null);
            if (data is IChamferFeatureData2) return ((IChamferFeatureData2)data).AccessSelections(model, null);
            if (data is ILinearPatternFeatureData) return ((ILinearPatternFeatureData)data).AccessSelections(model, null);
            if (data is ICircularPatternFeatureData) return ((ICircularPatternFeatureData)data).AccessSelections(model, null);
            if (data is ICurveDrivenPatternFeatureData) return ((ICurveDrivenPatternFeatureData)data).AccessSelections(model, null);
            if (data is ISketchPatternFeatureData) return ((ISketchPatternFeatureData)data).AccessSelections(model, null);
            throw new InvalidOperationException("Unsupported definition access interface.");
        }

        private static void ReleaseDefinition(object data)
        {
            if (data is IMoveFaceFeatureData) { ((IMoveFaceFeatureData)data).ReleaseSelectionAccess(); return; }
            if (data is ISweepFeatureData) { ((ISweepFeatureData)data).ReleaseSelectionAccess(); return; }
            if (data is ILoftFeatureData) { ((ILoftFeatureData)data).ReleaseSelectionAccess(); return; }
            if (data is ILoftedBendsFeatureData) { ((ILoftedBendsFeatureData)data).ReleaseSelectionAccess(); return; }
            if (data is IEdgeFlangeFeatureData) ((IEdgeFlangeFeatureData)data).ReleaseSelectionAccess();
            else if (data is IChamferFeatureData2) ((IChamferFeatureData2)data).ReleaseSelectionAccess();
            else if (data is ILinearPatternFeatureData) ((ILinearPatternFeatureData)data).ReleaseSelectionAccess();
            else if (data is ICircularPatternFeatureData) ((ICircularPatternFeatureData)data).ReleaseSelectionAccess();
            else if (data is ICurveDrivenPatternFeatureData) ((ICurveDrivenPatternFeatureData)data).ReleaseSelectionAccess();
            else if (data is ISketchPatternFeatureData) ((ISketchPatternFeatureData)data).ReleaseSelectionAccess();
        }

        // Capture numeric geometry while references still belong to the unmodified staging body.
        // Never retain a source Edge COM pointer for use after changing the base topology.
        private static Action<object, ModelDoc2> CaptureBindings(MirrorInPlaceExecutionContextV7 context, Feature feature,
            bool skipFlangeEdges = false, bool newPatternDefinition = false)
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
                    : data is ISketchPatternFeatureData ? new[] { "Sketch", "PatternFeatureArray", "PatternFaceArray", "PatternBodyArray", "ReferencePoint" }
                    : newPatternDefinition
                        ? new[] { "PatternFeatureArray", "PatternFaceArray", "PatternBodyArray", "D1Direction", "D2Direction", "D1FaceNormal" }
                        : new[] { "D1Direction", "D2Direction", "D1FaceNormal", "PatternFeatureArray", "PatternFaceArray", "PatternBodyArray" };
                foreach (string name in names)
                {
                    var flange45 = data as IEdgeFlangeFeatureData;
                    if (flange45 != null &&
                        ((name == "OffsetReference" && (flange45.OffsetType == 1 || flange45.OffsetType == 5)) ||
                         (name == "PositionOffsetReference" && !flange45.UsePositionOffset))) continue;
                    if (skipFlangeEdges && data is IEdgeFlangeFeatureData && name == "Edges") continue;
                    if (data is ICurveDrivenPatternFeatureData && name == "D2Direction" &&
                        !((ICurveDrivenPatternFeatureData)data).Dir2Specified) continue;
                    var sketchPattern = data as ISketchPatternFeatureData;
                    if (sketchPattern != null && name == "ReferencePoint" &&
                        sketchPattern.GetReferencePointType() == -1) continue;
                    var pattern = data as ICurveDrivenPatternFeatureData;
                    if (pattern != null &&
                        ((name == "PatternFeatureArray" && pattern.GetPatternFeatureCount() == 0) ||
                         (name == "PatternFaceArray" && pattern.GetPatternFaceCount() == 0) ||
                         (name == "PatternBodyArray" && pattern.GetPatternBodyCount() == 0))) continue;
                    if (sketchPattern != null &&
                        ((name == "PatternFeatureArray" && sketchPattern.GetPatternFeatureCount() == 0) ||
                         (name == "PatternFaceArray" && sketchPattern.GetPatternFaceCount() == 0) ||
                         (name == "PatternBodyArray" && sketchPattern.GetPatternBodyCount() == 0))) continue;
                    var property = type.GetProperty(name);
                    if (property == null || !property.CanRead || !property.CanWrite)
                        throw new InvalidOperationException("Mapping property unavailable: " + type.Name + "." + name);
                    object raw = property.GetValue(data, null);
                    if (raw == null) continue;
                    bool array = raw is Array;
                    object[] values = array ? ((Array)raw).Cast<object>().ToArray() : new[] { raw };
                    if (values.Length == 0) continue;
                    if ((data is ICurveDrivenPatternFeatureData || sketchPattern != null) &&
                        name == "PatternFeatureArray")
                    {
                        // This staged document retains the original seed features. Reassigning the
                        // inherited COM feature array is unnecessary and can make SolidWorks read
                        // an invalid native selection. Verify identity instead of writing it.
                        var seeds = values.Select(value => value as Feature).ToArray();
                        if (seeds.Any(seed => seed == null))
                            throw new InvalidOperationException("FEATURE29 non-feature pattern seed: " + feature.Name);
                        var seedNames = seeds.Select(seed => seed.Name).ToArray();
                        var seedTypes = seeds.Select(seed => seed.GetTypeName2()).ToArray();
                        setters.Add((definition, model) =>
                        {
                            if (newPatternDefinition)
                            {
                                object[] mappedSeeds = new object[seedNames.Length];
                                for (int i = 0; i < seedNames.Length; i++)
                                {
                                    var liveSeed = ((PartDoc)model).FeatureByName(seedNames[i]) as Feature;
                                    if (liveSeed == null || liveSeed.GetTypeName2() != seedTypes[i])
                                        throw new InvalidOperationException("FEATURE36 pattern seed missing: " + seedNames[i]);
                                    mappedSeeds[i] = liveSeed;
                                    if (definition == null && !liveSeed.Select2(true, 4))
                                        throw new InvalidOperationException("FEATURE37 cannot preselect pattern feature seed: " + seedNames[i]);
                                }
                                if (definition != null && !(definition is ISketchPatternFeatureData))
                                    property.SetValue(definition, mappedSeeds, null);
                                MirrorV7Diagnostics.Log("[FEATURE39][FEATURE_SEEDS] feature=" + feature.Name +
                                    " count=" + seedNames.Length +
                                    " phase=" + (definition == null ? "preselect" : "freshDefinition"));
                                return;
                            }
                            var current = type.GetProperty(name).GetValue(definition, null) as Array;
                            if (current == null || current.Length != seedNames.Length)
                                throw new InvalidOperationException(
                                    (sketchPattern != null ? "SKETCH_PATTERN59 stale native feature references: " :
                                        "FEATURE36 curve pattern has stale native feature references: ") + feature.Name +
                                    " (FEATURE29 seed count expected=" + seedNames.Length +
                                    " actual=" + (current == null ? -1 : current.Length) + ")");
                            for (int i = 0; i < seedNames.Length; i++)
                            {
                                var inherited = current.GetValue(i) as Feature;
                                var live = ((PartDoc)model).FeatureByName(seedNames[i]) as Feature;
                                if (inherited == null || live == null ||
                                    inherited.Name != seedNames[i] || inherited.GetTypeName2() != seedTypes[i] ||
                                    live.GetTypeName2() != seedTypes[i] || !SameCom28(inherited, live))
                                    throw new InvalidOperationException(
                                        (sketchPattern != null ? "SKETCH_PATTERN59 stale native feature references: " :
                                            "FEATURE36 curve pattern has stale native feature references: ") + feature.Name +
                                        " (FEATURE29 seed identity changed: " + seedNames[i] + ")");
                            }
                            MirrorV7Diagnostics.Log("[FEATURE29][SEED_IDENTITY_PASS] feature=" + feature.Name +
                                " count=" + seedNames.Length + " setter=SKIPPED");
                        });
                        continue;
                    }
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
                            FaceBinding35 signature = CaptureFaceBinding35(face, context.Reflection);
                            maps.Add(model => FindFaceByBinding35(model, signature, feature.Name, name));
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
                        else if (value is Sketch)
                        {
                            string ownerName = FindSketchOwner28(context.WorkingDocument, (Sketch)value).Name;
                            maps.Add(model =>
                            {
                                var owner = ((PartDoc)model).FeatureByName(ownerName) as Feature;
                                var mapped = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
                                if (mapped == null)
                                    throw new InvalidOperationException("SKETCH_PATTERN59 driving sketch missing: " + ownerName);
                                return mapped;
                            });
                        }
                        else if (value is SketchPoint)
                        {
                            var sourcePoint = (SketchPoint)value;
                            string ownerName = FindSketchOwner28(context.WorkingDocument, sourcePoint.GetSketch()).Name;
                            double[] expected = context.Reflection.ReflectPoint(
                                SketchPointModelPosition59(context.SwApp, sourcePoint));
                            maps.Add(model => FindSketchPoint59(model, ownerName, expected));
                        }
                        else if (value is SketchSegment)
                        {
                            var sourceSegment = (SketchSegment)value;
                            var sourceSketch = sourceSegment.GetSketch();
                            string ownerName = FindSketchOwner28(context.WorkingDocument, sourceSketch).Name;
                            int[] segmentId = sourceSegment.GetID() as int[];
                            if (segmentId == null || segmentId.Length < 2)
                                throw new InvalidOperationException("FEATURE28 segment identity unavailable: " + feature.Name);
                            int segmentType = sourceSegment.GetType();
                            bool segmentConstruction = sourceSegment.ConstructionGeometry;
                            double[][] expected = SampleSketchSegment28(context.SwApp, sourceSegment)
                                .Select(context.Reflection.ReflectPoint).ToArray();
                            maps.Add(model => FindSketchSegment28(model, ownerName, segmentId,
                                segmentType, segmentConstruction, expected));
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
                    setters.Add((definition, model) =>
                    {
                        MirrorV7Diagnostics.Log("[FEATURE35][MAP_PROPERTY] feature=" + feature.Name +
                            " property=" + name + " count=" + maps.Count);
                        object[] mapped = maps.Select(map => map(model)).ToArray();
                        if (newPatternDefinition && definition == null)
                        {
                            int mark = name == "Sketch" ? 64 : name == "ReferencePoint" ? 32 :
                                name == "D1Direction" ? 1 : name == "D2Direction" ? 2 :
                                name == "D1FaceNormal" ? 1024 : name == "PatternBodyArray" ? 256 :
                                name == "PatternFaceArray" ? 128 : 4;
                            foreach (object reference in mapped)
                                SelectCurvePatternReference37(model, reference, mark, feature.Name, name);
                            MirrorV7Diagnostics.Log("[FEATURE37][PRESELECT] feature=" + feature.Name +
                                " property=" + name + " count=" + mapped.Length + " mark=" + mark);
                            return;
                        }
                        if (newPatternDefinition && definition is ISketchPatternFeatureData)
                        {
                            // Sketch-pattern creation consumes the marked selections.
                            // Do not write inherited COM references into a fresh definition.
                            return;
                        }
                        if (newPatternDefinition && definition is ICurveDrivenPatternFeatureData &&
                            (name == "D1Direction" || name == "D2Direction" || name == "D1FaceNormal"))
                        {
                            // These directions were marked before CreateDefinition. The API
                            // documents their properties primarily for editing an existing pattern.
                            return;
                        }
                        if (newPatternDefinition && definition is ICurveDrivenPatternFeatureData &&
                            name == "PatternFaceArray")
                        {
                            // Macro2.swp records face seeds with selection mark 128 before
                            // CreateDefinition and does not assign PatternFaceArray afterward.
                            // The fresh setter did not bind face seeds in the previous run.
                            MirrorV7Diagnostics.Log("[FEATURE41][FACE_SEEDS] feature=" + feature.Name +
                                " count=" + mapped.Length + " phase=preselect-only mark=128");
                            return;
                        }
                        if (name == "Edges" && (definition is IChamferFeatureData2 || definition is IEdgeFlangeFeatureData))
                        {
                            object inherited = definition is IChamferFeatureData2
                                ? ((IChamferFeatureData2)definition).Edges
                                : ((IEdgeFlangeFeatureData)definition).Edges;
                            var current = inherited as Array;
                            bool identical = EdgeAttachmentsEquivalent31(current, mapped, feature.Name);
                            if (identical)
                            {
                                MirrorV7Diagnostics.Log("[FEATURE30][EDGES_INHERITED] feature=" + feature.Name + " count=" + mapped.Length);
                                return;
                            }
                            if (definition is IEdgeFlangeFeatureData)
                                throw new InvalidOperationException("FEATURE30 flange attachment differs from reflected edges: " + feature.Name +
                                    ". Edges setter is unsupported; edge/profile reconstruction is required.");
                            throw new InvalidOperationException("CHAMFER32 requires native reconstruction; Edges setter disabled.");
                        }
                        if (name == "PatternFaceArray" && definition is ICurveDrivenPatternFeatureData && !newPatternDefinition)
                        {
                            // The copied pattern may already reference the live faces after the
                            // upstream native features have been replayed. Reassigning those
                            // same COM faces is unnecessary and can crash the native setter.
                            // Never silently keep a stale face list: the per-feature body oracle
                            // below must still pass before this checkpoint is accepted.
                            var inherited = property.GetValue(definition, null) as Array;
                            bool identical = inherited != null && inherited.Length == mapped.Length;
                            int geometric = 0;
                            if (identical)
                            {
                                for (int i = 0; i < mapped.Length; i++)
                                {
                                    if (!(inherited.GetValue(i) is Face2) ||
                                        !(mapped[i] is Face2) ||
                                        (!SameCom28(inherited.GetValue(i), mapped[i]) &&
                                         !LiveEquivalentFace36(model, (Face2)inherited.GetValue(i), (Face2)mapped[i])))
                                    {
                                        identical = false;
                                        break;
                                    }
                                    if (!SameCom28(inherited.GetValue(i), mapped[i])) geometric++;
                                }
                            }
                            MirrorV7Diagnostics.Log("[FEATURE36][PATTERN_FACES] feature=" + feature.Name +
                                " mappedCount=" + mapped.Length +
                                " inheritedCount=" + (inherited == null ? -1 : inherited.Length) +
                                " equivalent=" + identical + " geometric=" + geometric + " setter=SKIPPED");
                            if (!identical)
                                throw new InvalidOperationException("FEATURE36 curve pattern has stale native face references: " +
                                    feature.Name + ". PatternFaceArray setter is unsafe; native pattern reconstruction is required.");
                            return;
                        }
                        try { property.SetValue(definition, array ? (object)mapped : mapped[0], null); }
                        catch (TargetInvocationException ex)
                        {
                            throw new InvalidOperationException("FEATURE36 setter failed: " + feature.Name + "." + name +
                                " inner=" + (ex.InnerException == null ? "none" : ex.InnerException.GetType().FullName +
                                ": " + ex.InnerException.Message), ex);
                        }
                    });
                }
            }
            finally { ReleaseDefinition(data); }
            return (definition, model) => { foreach (var setter in setters) setter(definition, model); };
        }

        internal sealed class CurvePatternRecipe36
        {
            internal bool D1Reverse, D2Reverse, Dir2Specified;
            internal int FaceSeedCount, FeatureSeedCount, BodySeedCount;
            internal int[] SkippedItems;
        }

        internal sealed class SketchPatternRecipe59
        {
            internal int FeatureSeedCount, FaceSeedCount, BodySeedCount, ReferencePointType;
        }

        private static SketchPatternRecipe59 CaptureSketchPattern59(Feature feature, ModelDoc2 model)
        {
            var data = feature.GetDefinition() as ISketchPatternFeatureData;
            if (data == null || !data.AccessSelections(model, null))
                throw new InvalidOperationException("SKETCH_PATTERN59 cannot access source definition: " + feature.Name);
            try
            {
                var recipe = new SketchPatternRecipe59
                {
                    FeatureSeedCount = data.GetPatternFeatureCount(),
                    FaceSeedCount = data.GetPatternFaceCount(),
                    BodySeedCount = data.GetPatternBodyCount(),
                    ReferencePointType = data.GetReferencePointType()
                };
                if (data.Sketch == null ||
                    recipe.FeatureSeedCount + recipe.FaceSeedCount + recipe.BodySeedCount == 0 ||
                    (recipe.ReferencePointType != -1 && data.ReferencePoint == null))
                    throw new InvalidOperationException("SKETCH_PATTERN59 source sketch, seed or reference point is missing: " + feature.Name);
                return recipe;
            }
            finally { data.ReleaseSelectionAccess(); }
        }

        private static CurvePatternRecipe36 CaptureCurvePattern36(Feature feature)
        {
            var data = feature.GetDefinition() as ICurveDrivenPatternFeatureData;
            if (data == null) throw new InvalidOperationException("FEATURE36 curve pattern definition unavailable.");
            bool secondDirection = data.Dir2Specified;
            int skippedCount = data.GetSkippedItemCount();
            var skippedRaw = skippedCount == 0 ? null : data.SkippedItemArray as Array;
            if (skippedCount > 0 && (skippedRaw == null || skippedRaw.Length != skippedCount))
                throw new InvalidOperationException("FEATURE36 skipped item list unavailable: " + feature.Name);
            return new CurvePatternRecipe36
            {
                D1Reverse = data.D1ReverseDirection,
                D2Reverse = secondDirection && data.D2ReverseDirection,
                Dir2Specified = secondDirection,
                FaceSeedCount = data.GetPatternFaceCount(),
                FeatureSeedCount = data.GetPatternFeatureCount(),
                BodySeedCount = data.GetPatternBodyCount(),
                SkippedItems = skippedRaw == null ? new int[0] : skippedRaw.Cast<object>().Select(Convert.ToInt32).ToArray()
            };
        }

        private static void SelectCurvePatternReference37(ModelDoc2 model, object reference,
            int mark, string featureName, string propertyName)
        {
            bool selected;
            var seed = reference as Feature;
            if (seed != null) selected = seed.Select2(true, mark);
            else
            {
                var selection = model.SelectionManager as SelectionMgr;
                if (selection == null)
                    throw new InvalidOperationException("FEATURE37 selection manager unavailable.");
                SelectData selectData = selection.CreateSelectData();
                selectData.Mark = mark;
                var entity = reference as Entity;
                var segment = reference as SketchSegment;
                var point = reference as SketchPoint;
                var sketch = reference as Sketch;
                var body = reference as Body2;
                if (entity != null) selected = entity.Select4(true, selectData);
                else if (segment != null) selected = segment.Select4(true, selectData);
                else if (point != null) selected = point.Select4(true, selectData);
                else if (sketch != null) selected = FindSketchOwner28(model, sketch).Select2(true, mark);
                else if (body != null) selected = body.Select2(true, selectData);
                else throw new InvalidOperationException("FEATURE37 unsupported preselection: " +
                    featureName + "." + propertyName + " type=" + reference.GetType().FullName);
            }
            if (!selected)
                throw new InvalidOperationException("FEATURE37 preselection failed: " +
                    featureName + "." + propertyName + " mark=" + mark);
        }

        private static bool LiveEquivalentFace36(ModelDoc2 model, Face2 inherited, Face2 mapped)
        {
            if (inherited == null || mapped == null) return false;
            bool live = false;
            var bodies = ((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            foreach (Body2 body in bodies ?? new object[0])
            {
                foreach (Face2 face in (body.GetFaces() as object[]) ?? new object[0])
                {
                    if (SameCom28(face, inherited)) { live = true; break; }
                }
                if (live) break;
            }
            if (!live) return false;
            double area = mapped.GetArea();
            if (Math.Abs(inherited.GetArea() - area) > Math.Max(1e-12, area * 1e-6)) return false;
            var oldBox = inherited.GetBox() as double[];
            var newBox = mapped.GetBox() as double[];
            if (oldBox == null || newBox == null || oldBox.Length < 6 || newBox.Length < 6) return false;
            for (int i = 0; i < 6; i++)
                if (Math.Abs(oldBox[i] - newBox[i]) > 1e-7) return false;
            var oldNormal = inherited.Normal as double[];
            var newNormal = mapped.Normal as double[];
            if (oldNormal != null && newNormal != null && oldNormal.Length >= 3 && newNormal.Length >= 3)
            {
                double oldLength = Math.Sqrt(oldNormal.Take(3).Sum(v => v * v));
                double newLength = Math.Sqrt(newNormal.Take(3).Sum(v => v * v));
                if (oldLength > 0.5 && newLength > 0.5 &&
                    Enumerable.Range(0, 3).Sum(i => oldNormal[i] * newNormal[i]) / oldLength / newLength < 0.99999)
                    return false;
            }
            var oldOwner = inherited.GetFeature() as Feature;
            var newOwner = mapped.GetFeature() as Feature;
            return oldOwner != null && newOwner != null &&
                oldOwner.Name == newOwner.Name && oldOwner.GetTypeName2() == newOwner.GetTypeName2();
        }

        internal sealed class ChamferRecipe32
        {
            internal int Type, Options;
            internal double Width, OtherDistance, Angle;
            internal bool Flip;
            internal readonly List<double[][]> Edges = new List<double[][]>();
        }

        private static ChamferRecipe32 CaptureChamfer32(MirrorInPlaceExecutionContextV7 context, Feature feature)
        {
            var data = (IChamferFeatureData2)feature.GetDefinition();
            if (!data.AccessSelections(context.WorkingDocument, null))
                throw new InvalidOperationException("CHAMFER32 cannot capture selections: " + feature.Name);
            try
            {
                var recipe = new ChamferRecipe32 { Type = data.Type };
                if (recipe.Type != (int)swChamferType_e.swChamferAngleDistance &&
                    recipe.Type != (int)swChamferType_e.swChamferDistanceDistance &&
                    recipe.Type != (int)swChamferType_e.swChamferEqualDistance)
                    throw new InvalidOperationException("CHAMFER32 unsupported chamfer type=" + recipe.Type);
                var edges = data.Edges as Array;
                var faces = data.Faces as Array;
                if (edges == null || edges.Length == 0 || (faces != null && faces.Length != 0) || data.LoopCount != 0)
                    throw new InvalidOperationException("CHAMFER32 recreation currently requires explicit edge selections (not faces/loops/vertex).");
                recipe.Width = data.GetEdgeChamferDistance(0);
                recipe.OtherDistance = recipe.Type == (int)swChamferType_e.swChamferAngleDistance
                    ? 0 : data.GetEdgeChamferDistance(1);
                recipe.Angle = recipe.Type == (int)swChamferType_e.swChamferAngleDistance ? data.EdgeChamferAngle : 0;
                if (data.KeepFeatures) recipe.Options |= (int)swFeatureChamferOption_e.swFeatureChamferKeepFeature;
                if (data.TangentPropagation) recipe.Options |= (int)swFeatureChamferOption_e.swFeatureChamferTangentPropagation;
                bool? flip = null;
                foreach (object value in edges)
                {
                    var edge = value as Edge;
                    if (edge == null) throw new InvalidOperationException("CHAMFER32 invalid source edge.");
                    bool edgeFlip = data.GetIsFlipped(edge);
                    if (flip.HasValue && flip.Value != edgeFlip)
                        throw new InvalidOperationException("CHAMFER32 mixed per-edge flips require a separate reconstruction handler.");
                    flip = edgeFlip;
                    recipe.Edges.Add(SampleEdge(edge).Select(context.Reflection.ReflectPoint).ToArray());
                }
                recipe.Flip = flip.GetValueOrDefault();
                MirrorV7Diagnostics.Log("[CHAMFER32][CAPTURE] feature=" + feature.Name + " edges=" + recipe.Edges.Count +
                    " type=" + recipe.Type + " width_m=" + recipe.Width + " other_m=" + recipe.OtherDistance + " angle_rad=" + recipe.Angle);
                return recipe;
            }
            finally { data.ReleaseSelectionAccess(); }
        }

        private static void RecreateChamfer32(MirrorInPlaceExecutionContextV7 context, FeatureReplayCheckpointV7 item)
        {
            var model = context.WorkingDocument;
            var recipe = item.Chamfer;
            string originalName = item.Feature.Name;
            var old = item.Feature.Feature;
            // Feature names are not a safe key for a tree snapshot: SolidWorks can
            // expose repeated names while walking top-level history. Preserve the
            // ordered sequence and validate it after deleting only this feature.
            var retained = new List<Tuple<string, string>>();
            int predecessorIndex = -1;
            bool foundOld = false;
            for (var cursor = model.FirstFeature() as Feature; cursor != null; cursor = cursor.GetNextFeature() as Feature)
            {
                if (SameCom28(cursor, old))
                {
                    if (foundOld) throw new InvalidOperationException("CHAMFER32 source feature occurs twice in history.");
                    foundOld = true;
                    predecessorIndex = retained.Count - 1;
                }
                else retained.Add(Tuple.Create(cursor.Name, cursor.GetTypeName2()));
                if (retained.Count > 10000) throw new InvalidOperationException("CHAMFER32 feature-tree traversal exceeded safety limit.");
            }
            if (!foundOld || predecessorIndex < 0) throw new InvalidOperationException("CHAMFER32 previous feature unavailable.");
            // Prove all targets are uniquely available before deleting anything. Do not
            // carry those COM pointers across deletion or rebuild.
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, originalName))
                throw new InvalidOperationException("CHAMFER32 cannot roll back before source chamfer.");
            foreach (var points in recipe.Edges) FindEdge(model, points);
            MoveRollbackAfter(model, old);
            model.ClearSelection2(true);
            // No swDelete_Children or swDelete_Absorbed: remove only this feature on staging.
            if (!old.Select2(false, 0) || !model.Extension.DeleteSelection2(0))
                throw new InvalidOperationException("CHAMFER32 could not delete staging chamfer only.");
            model.ClearSelection2(true);
            int retainedIndex = 0;
            for (var live = model.FirstFeature() as Feature; live != null; live = live.GetNextFeature() as Feature)
            {
                if (retainedIndex >= retained.Count || live.Name != retained[retainedIndex].Item1 ||
                    live.GetTypeName2() != retained[retainedIndex].Item2)
                    throw new InvalidOperationException("CHAMFER32 deletion changed feature history at index " + retainedIndex);
                retainedIndex++;
            }
            if (retainedIndex != retained.Count)
                throw new InvalidOperationException("CHAMFER32 deletion changed feature count.");
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Feature predecessor = model.FirstFeature() as Feature;
                for (int index = 0; index < predecessorIndex && predecessor != null; index++)
                    predecessor = predecessor.GetNextFeature() as Feature;
                if (predecessor == null) throw new InvalidOperationException("CHAMFER32 predecessor lost.");
                if (predecessor.Name != retained[predecessorIndex].Item1 ||
                    predecessor.GetTypeName2() != retained[predecessorIndex].Item2)
                    throw new InvalidOperationException("CHAMFER32 predecessor identity changed.");
                MoveRollbackAfter(model, predecessor);
                model.EditRebuild3();
                model.ClearSelection2(true);
                foreach (var points in recipe.Edges)
                {
                    var edge = FindEdge(model, points);
                    if (!((Entity)edge).Select4(true, null))
                        throw new InvalidOperationException("CHAMFER32 cannot select reflected edge.");
                }
                int options = recipe.Options;
                if (recipe.Flip ^ (attempt == 1)) options |= (int)swFeatureChamferOption_e.swFeatureChamferFlipDirection;
                MirrorV7Diagnostics.Log("[CHAMFER32][CREATE] feature=" + originalName + " attempt=" + attempt + " edges=" + recipe.Edges.Count);
                Feature created = model.FeatureManager.InsertFeatureChamfer(options, recipe.Type,
                    recipe.Width, recipe.Angle, recipe.OtherDistance, 0, 0, 0);
                model.ClearSelection2(true);
                if (created == null)
                    throw new InvalidOperationException("CHAMFER32 InsertFeatureChamfer returned null; staging retained for diagnosis.");
                bool accepted = false;
                try
                {
                    MoveRollbackAfter(model, created);
                    model.EditRebuild3();
                    EnsureFeatureHasNoError(created, "CHAMFER32 created feature");
                    var readback = (IChamferFeatureData2)created.GetDefinition();
                    if (readback.Type != recipe.Type || Math.Abs(readback.GetEdgeChamferDistance(0) - recipe.Width) > 1e-10 ||
                        (recipe.Type == (int)swChamferType_e.swChamferAngleDistance
                            ? Math.Abs(readback.EdgeChamferAngle - recipe.Angle) > 1e-10
                            : Math.Abs(readback.GetEdgeChamferDistance(1) - recipe.OtherDistance) > 1e-10))
                        throw new InvalidOperationException("CHAMFER32 dimensions changed during creation.");
                    BaseSketchMutationEngineV7.VerifyCurrentSolids(context, item.ReflectedBodyOracle);
                    // Naming supports existing name-based mappers; it does not repair
                    // topology references by itself. Downstream replay/oracles still run.
                    created.Name = originalName;
                    if (created.Name != originalName) throw new InvalidOperationException("CHAMFER32 cannot restore feature name.");
                    // The checkpoint owns this graph node in both replay paths. The
                    // standalone legacy path deliberately has no WorkingGraph; its
                    // caller updates its own feature record from this checkpoint.
                    item.Feature.Feature = created;
                    item.Feature.PersistentReference = null;
                    VerifyOptions27(item);
                    item.Result = new MirrorV7FeatureResult { FeatureName = originalName, FeatureType = item.Feature.TypeName,
                        Status = MirrorV7ReplayStatus.ExactReplay, Message = "CHAMFER32 native recreation; checkpoint geometry PASS; downstream replay required." };
                    item.WasNativeReplacement = true;
                    accepted = true;
                    MirrorV7Diagnostics.Log("[CHAMFER32][PASS] feature=" + originalName + " native=True downstreamValidation=REQUIRED");
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    MirrorV7Diagnostics.Log("[CHAMFER32][REJECT] feature=" + originalName + " attempt=" + attempt + " reason=" + ex.Message);
                }
                finally
                {
                    if (!accepted)
                    {
                        model.ClearSelection2(true);
                        if (!created.Select2(false, 0) || !model.Extension.DeleteSelection2(0))
                            throw new InvalidOperationException("CHAMFER32 cannot remove rejected candidate; replay stopped.");
                        model.ClearSelection2(true);
                    }
                }
            }
            throw new InvalidOperationException("CHAMFER32 no native candidate matches reflected geometry; output not published.");
        }

        private static bool EdgeAttachmentsEquivalent31(Array inherited, object[] mapped, string featureName)
        {
            if (inherited == null || inherited.Length != mapped.Length)
            {
                MirrorV7Diagnostics.Log("[EDGE31][COUNT_MISMATCH] feature=" + featureName +
                    " inherited=" + (inherited == null ? -1 : inherited.Length) + " expected=" + mapped.Length);
                return false;
            }
            var current = inherited.Cast<object>().ToList();
            var identity = BipartiteEquivalenceMatcherV7.Match(current, mapped.ToList(), SameCom28);
            if (identity.HasPerfectMatching && identity.LeftWithMultipleCandidates == 0)
            {
                MirrorV7Diagnostics.Log("[EDGE31][IDENTITY_SET_PASS] feature=" + featureName + " count=" + mapped.Length);
                return true;
            }
            if (current.Any(e => !(e is Edge)) || mapped.Any(e => !(e is Edge)))
                throw new InvalidOperationException("EDGE31 non-edge attachment: " + featureName);
            var actualSamples = current.Cast<Edge>().Select(SampleEdge).ToList();
            var expectedSamples = mapped.Cast<Edge>().Select(SampleEdge).ToList();
            var geometry = BipartiteEquivalenceMatcherV7.Match(actualSamples, expectedSamples,
                (a, b) => BipartiteEquivalenceMatcherV7.SameEdgeSamples(a, b, 1e-7));
            bool pass = geometry.HasPerfectMatching && geometry.LeftWithMultipleCandidates == 0;
            MirrorV7Diagnostics.Log("[EDGE31][GEOMETRY_SET] feature=" + featureName + " count=" + mapped.Length +
                " unmatched=" + geometry.LeftWithoutCandidates + " ambiguous=" + geometry.LeftWithMultipleCandidates +
                " result=" + (pass ? "PASS" : "FAIL") + " tolerance_m=1E-7");
            return pass;
        }

        private static Feature FindSketchOwner28(ModelDoc2 model, Sketch sketch)
        {
            foreach (var node in FeatureTreeScannerV7.Scan(model).Nodes)
                if (node.Feature != null && SameCom28(node.Feature.GetSpecificFeature2() as Sketch, sketch)) return node.Feature;
            throw new InvalidOperationException("FEATURE28 sketch owner unavailable.");
        }

        private static bool SameCom28(object first, object second)
        {
            if (first == null || second == null) return false;
            IntPtr a = IntPtr.Zero, b = IntPtr.Zero;
            try
            {
                a = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(first);
                b = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(second);
                return a == b;
            }
            finally
            {
                if (a != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(a);
                if (b != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(b);
            }
        }

        private static double[][] SampleSketchSegment28(ISldWorks app, SketchSegment segment)
        {
            var local = new List<double[]>();
            Action<SketchPoint> add = p => { if (p != null) local.Add(new[] { p.X, p.Y, p.Z }); };
            var line = segment as SketchLine;
            if (line != null) { add(line.GetStartPoint2() as SketchPoint); add(line.GetEndPoint2() as SketchPoint); }
            else
            {
                var arc = segment as SketchArc;
                if (arc != null) { add(arc.GetStartPoint2() as SketchPoint); add(arc.GetCenterPoint2() as SketchPoint); add(arc.GetEndPoint2() as SketchPoint); }
                else
                {
                    var spline = segment as SketchSpline;
                    if (spline != null)
                        foreach (SketchPoint point in spline.GetPoints2() as object[] ?? new object[0]) add(point);
                }
            }
            if (local.Count < 2) throw new InvalidOperationException("FEATURE28 unsupported/empty path segment.");
            var inverse = segment.GetSketch().ModelToSketchTransform.IInverse();
            var math = app.GetMathUtility() as IMathUtility;
            return local.Select(p => ((MathPoint)((MathPoint)math.CreatePoint(p)).MultiplyTransform(inverse)).ArrayData as double[]).ToArray();
        }

        private static double[] SketchPointModelPosition59(ISldWorks app, SketchPoint point)
        {
            var math = app.GetMathUtility() as IMathUtility;
            var sketch = point.GetSketch();
            if (math == null || sketch == null)
                throw new InvalidOperationException("SKETCH_PATTERN59 point transform unavailable.");
            var transformed = (MathPoint)((MathPoint)math.CreatePoint(new[] { point.X, point.Y, point.Z }))
                .MultiplyTransform(sketch.ModelToSketchTransform.IInverse());
            return transformed.ArrayData as double[];
        }

        private static SketchPoint FindSketchPoint59(ModelDoc2 model, string ownerName, double[] expected)
        {
            var owner = ((PartDoc)model).FeatureByName(ownerName) as Feature;
            var sketch = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
            if (sketch == null) throw new InvalidOperationException("SKETCH_PATTERN59 point sketch missing: " + ownerName);
            var app = System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
            if (app == null) throw new InvalidOperationException("SKETCH_PATTERN59 SolidWorks instance unavailable.");
            var matches = new List<SketchPoint>();
            foreach (SketchPoint point in sketch.GetSketchPoints2() as object[] ?? new object[0])
                if (Distance(expected, SketchPointModelPosition59(app, point)) < 1e-7)
                    matches.Add(point);
            if (matches.Count != 1)
                throw new InvalidOperationException("SKETCH_PATTERN59 reflected point match count=" + matches.Count +
                    " sketch=" + ownerName);
            return matches[0];
        }

        private static SketchSegment FindSketchSegment28(ModelDoc2 model, string ownerName, int[] id,
            int segmentType, bool construction, double[][] expected)
        {
            var owner = ((PartDoc)model).FeatureByName(ownerName) as Feature;
            var sketch = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
            if (sketch == null) throw new InvalidOperationException("FEATURE28 path sketch missing: " + ownerName);
            var matches = new List<Tuple<SketchSegment, double>>();
            int typeMatches = 0, sampleMatches = 0;
            var app = System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
            if (app == null) throw new InvalidOperationException("FEATURE28 SolidWorks instance unavailable.");
            foreach (SketchSegment segment in sketch.GetSketchSegments() as object[] ?? new object[0])
            {
                if (segment.GetType() != segmentType || segment.ConstructionGeometry != construction) continue;
                typeMatches++;
                double[][] actual = SampleSketchSegment28(app, segment);
                if (actual.Length != expected.Length) continue;
                sampleMatches++;
                Func<bool, double> score = reverse => Enumerable.Range(0, actual.Length).Max(i =>
                    Math.Sqrt(Enumerable.Range(0, 3).Sum(k => Math.Pow(
                        actual[reverse ? actual.Length - 1 - i : i][k] - expected[i][k], 2))));
                double error = Math.Min(score(false), score(true));
                if (error <= 1e-7) matches.Add(Tuple.Create(segment, error));
            }
            if (matches.Count != 1)
                throw new InvalidOperationException("FEATURE28 reflected path match count=" + matches.Count +
                    " owner=" + ownerName + " sourceId=" + id[0] + "," + id[1] +
                    " type=" + segmentType + " construction=" + construction +
                    " typeCandidates=" + typeMatches + " sampleCandidates=" + sampleMatches);
            int[] mappedId = matches[0].Item1.GetID() as int[];
            MirrorV7Diagnostics.Log("[FEATURE28][PATH_PASS] owner=" + ownerName +
                " sourceId=" + id[0] + "," + id[1] +
                " mappedId=" + (mappedId == null ? "unavailable" : mappedId[0] + "," + mappedId[1]) +
                " type=" + segmentType + " residual_m=" + matches[0].Item2.ToString("R") +
                " strategy=UniqueReflectedGeometry");
            return matches[0].Item1;
        }

        private static double[][] SampleEdge(Edge edge)
        {
            return ADDIN.Helpers.SketchOperationsHelper.SampleEdge24(edge);
        }

        internal static Edge FindEdge(ModelDoc2 model, double[][] expected)
        {
            var matches = new List<Edge>();
            foreach (Body2 body in ((object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0]))
                foreach (Edge edge in ((object[])body.GetEdges() ?? new object[0]))
                {
                    double[][] actual = SampleEdge(edge);
                    if (actual == null || actual.Length != expected.Length) continue;
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

        internal static object FindVertex(ModelDoc2 model, double[] expected)
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

        private sealed class FaceBinding35
        {
            internal double Area;
            internal double[][][] Boundaries;
            internal double[][] Witnesses;
            internal double[] Normal;
            internal string OwnerName, OwnerType;
        }

        private static FaceBinding35 CaptureFaceBinding35(Face2 face, PartReflectionTransformV7 reflection)
        {
            var box = face.GetBox() as double[];
            if (box == null || box.Length < 6)
                throw new InvalidOperationException("FEATURE35 cannot sample source face box.");
            // Interior witnesses survive edge splits and retrimming after a native
            // Chamfer recreation, while exact edge loops may no longer be identical.
            double[][] fractions = { new[] { .5, .5, .5 }, new[] { .25, .5, .5 },
                new[] { .75, .5, .5 }, new[] { .5, .25, .5 }, new[] { .5, .75, .5 },
                new[] { .5, .5, .25 }, new[] { .5, .5, .75 } };
            var witnesses = new List<double[]>();
            foreach (var f in fractions)
            {
                var projected = face.GetClosestPointOn(
                    box[0] + f[0] * (box[3] - box[0]),
                    box[1] + f[1] * (box[4] - box[1]),
                    box[2] + f[2] * (box[5] - box[2])) as double[];
                if (projected == null || projected.Length < 3) continue;
                var point = reflection.ReflectPoint(new[] { projected[0], projected[1], projected[2] });
                if (!witnesses.Any(w => Distance(w, point) < 1e-7)) witnesses.Add(point);
            }
            if (witnesses.Count == 0)
                throw new InvalidOperationException("FEATURE35 source face has no geometric witnesses.");
            var originalNormal = face.Normal as double[];
            double[] normal = null;
            if (originalNormal != null && originalNormal.Length >= 3 &&
                originalNormal.Take(3).Sum(v => v * v) > 0.5)
            {
                var zero = reflection.ReflectPoint(new double[] { 0, 0, 0 });
                var reflected = reflection.ReflectPoint(new[] { originalNormal[0], originalNormal[1], originalNormal[2] });
                normal = Enumerable.Range(0, 3).Select(i => reflected[i] - zero[i]).ToArray();
            }
            return new FaceBinding35 { Area = face.GetArea(),
                Boundaries = ((object[])face.GetEdges() ?? new object[0]).Cast<Edge>()
                    .Select(e => SampleEdge(e).Select(reflection.ReflectPoint).ToArray()).ToArray(),
                Witnesses = witnesses.ToArray(), Normal = normal,
                OwnerName = (face.GetFeature() as Feature)?.Name,
                OwnerType = (face.GetFeature() as Feature)?.GetTypeName2() };
        }

        private static object FindFaceByBinding35(ModelDoc2 model, FaceBinding35 binding,
            string featureName, string propertyName)
        {
            try { return FindFace(model, binding.Area, binding.Boundaries); }
            catch (InvalidOperationException ex)
            {
                MirrorV7Diagnostics.Log("[FEATURE35][EXACT_FACE_MISS] feature=" + featureName +
                    " property=" + propertyName + " reason=" + ex.Message);
            }
            var scores = new List<Tuple<Face2, int>>();
            foreach (Body2 body in ((object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0]))
                foreach (Face2 face in ((object[])body.GetFaces() ?? new object[0]))
                {
                    if (binding.Normal != null)
                    {
                        var normal = face.Normal as double[];
                        if (normal == null || normal.Length < 3 ||
                            Enumerable.Range(0, 3).Sum(i => normal[i] * binding.Normal[i]) < 0.999)
                            continue;
                    }
                    int count = 0;
                    foreach (var witness in binding.Witnesses)
                    {
                        var closest = face.GetClosestPointOn(witness[0], witness[1], witness[2]) as double[];
                        if (closest != null && closest.Length >= 3 &&
                            Distance(witness, new[] { closest[0], closest[1], closest[2] }) < 1e-7) count++;
                    }
                    if (count > 0) scores.Add(Tuple.Create(face, count));
                }
            int best = scores.Count == 0 ? 0 : scores.Max(s => s.Item2);
            var matches = scores.Where(s => s.Item2 == best).ToList();
            if (matches.Count > 1 && binding.OwnerName != null)
            {
                var sameOwner = matches.Where(s =>
                {
                    var owner = s.Item1.GetFeature() as Feature;
                    return owner != null && owner.Name == binding.OwnerName &&
                        owner.GetTypeName2() == binding.OwnerType;
                }).ToList();
                if (sameOwner.Count > 0) matches = sameOwner;
            }
            if (matches.Count > 1)
            {
                double smallest = matches.Min(s => Math.Abs(s.Item1.GetArea() - binding.Area));
                double separation = Math.Max(1e-12, binding.Area * 1e-6);
                matches = matches.Where(s =>
                    Math.Abs(s.Item1.GetArea() - binding.Area) <= smallest + separation).ToList();
            }
            MirrorV7Diagnostics.Log("[FEATURE35][FACE_WITNESS] feature=" + featureName +
                " property=" + propertyName + " witnesses=" + binding.Witnesses.Length +
                " best=" + best + " tied=" + matches.Count + " nonzeroCandidates=" + scores.Count +
                " sourceOwner=" + binding.OwnerName + " sourceArea=" + binding.Area +
                " candidateAreas=" + string.Join("|", matches.Select(s => s.Item1.GetArea().ToString("G9"))));
            if (best < Math.Min(3, binding.Witnesses.Length) || matches.Count != 1)
                throw new InvalidOperationException("FEATURE35 reflected face lacks a unique geometric match: " +
                    featureName + "." + propertyName + " witnesses=" + best + "/" + binding.Witnesses.Length +
                    " tied=" + matches.Count);
            return matches[0].Item1;
        }

        private static void RecreateCurvePattern36(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayCheckpointV7 item)
        {
            if (item.Pattern36 == null || item.CreatePatternBinding36 == null || item.Options27 == null)
                throw new InvalidOperationException("FEATURE36 source pattern recipe is incomplete: " + item.Feature.Name);
            var model = context.WorkingDocument;
            var old = item.Feature.Feature;
            string originalName = item.Feature.Name;
            var retained = new List<Tuple<string, string>>();
            int predecessorIndex = -1;
            bool foundOld = false;
            for (var cursor = model.FirstFeature() as Feature; cursor != null; cursor = cursor.GetNextFeature() as Feature)
            {
                if (SameCom28(cursor, old))
                {
                    if (foundOld) throw new InvalidOperationException("FEATURE36 source pattern occurs twice in history.");
                    foundOld = true;
                    predecessorIndex = retained.Count - 1;
                }
                else retained.Add(Tuple.Create(cursor.Name, cursor.GetTypeName2()));
                if (retained.Count > 10000) throw new InvalidOperationException("FEATURE36 feature history exceeds safety limit.");
            }
            if (!foundOld || predecessorIndex < 0)
                throw new InvalidOperationException("FEATURE36 predecessor unavailable: " + originalName);
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, originalName))
                throw new InvalidOperationException("FEATURE36 cannot roll back before source pattern.");
            MoveRollbackAfter(model, old);
            model.ClearSelection2(true);
            if (!old.Select2(false, 0) || !model.Extension.DeleteSelection2(0))
                throw new InvalidOperationException("FEATURE36 cannot delete the staging pattern only.");
            model.ClearSelection2(true);
            int retainedIndex = 0;
            for (var live = model.FirstFeature() as Feature; live != null; live = live.GetNextFeature() as Feature)
            {
                if (retainedIndex >= retained.Count || live.Name != retained[retainedIndex].Item1 ||
                    live.GetTypeName2() != retained[retainedIndex].Item2)
                    throw new InvalidOperationException("FEATURE36 deletion changed feature history at " + retainedIndex);
                retainedIndex++;
            }
            if (retainedIndex != retained.Count)
                throw new InvalidOperationException("FEATURE36 deletion changed feature count.");

            int attempts = item.Pattern36.Dir2Specified ? 4 : 2;
            for (int mask = 0; mask < attempts; mask++)
            {
                Feature predecessor = model.FirstFeature() as Feature;
                for (int index = 0; index < predecessorIndex && predecessor != null; index++)
                    predecessor = predecessor.GetNextFeature() as Feature;
                if (predecessor == null || predecessor.Name != retained[predecessorIndex].Item1 ||
                    predecessor.GetTypeName2() != retained[predecessorIndex].Item2)
                    throw new InvalidOperationException("FEATURE36 predecessor changed before creation.");
                MoveRollbackAfter(model, predecessor);
                model.EditRebuild3();
                model.ClearSelection2(true);
                // Curve-pattern creation consumes marked selections at CreateDefinition.
                // The reference properties are for editing an existing pattern; setting
                // PatternFaceArray on the inherited definition can fault in native code.
                item.CreatePatternBinding36(null, model);
                var selection = model.SelectionManager as SelectionMgr;
                if (selection == null)
                    throw new InvalidOperationException("FEATURE37 selection manager unavailable before CreateDefinition.");
                int selectedFaces = selection.GetSelectedObjectCount2(128);
                int selectedFeatures = selection.GetSelectedObjectCount2(4);
                if (selectedFaces != item.Pattern36.FaceSeedCount ||
                    selectedFeatures != item.Pattern36.FeatureSeedCount ||
                    selection.GetSelectedObjectCount2(1) != 1 ||
                    (item.Pattern36.Dir2Specified && selection.GetSelectedObjectCount2(2) != 1))
                    throw new InvalidOperationException("FEATURE37 preselection count mismatch: " + originalName +
                        " faces(mark128)=" + selectedFaces + "/" + item.Pattern36.FaceSeedCount +
                        " features(mark4)=" + selectedFeatures + "/" + item.Pattern36.FeatureSeedCount +
                        " d1=" + selection.GetSelectedObjectCount2(1) +
                        " d2=" + selection.GetSelectedObjectCount2(2));
                MirrorV7Diagnostics.Log("[FEATURE37][PRECREATE] feature=" + originalName +
                    " faceSeeds(mark128)=" + selectedFaces + "/" + item.Pattern36.FaceSeedCount +
                    " featureSeeds(mark4)=" + selectedFeatures + "/" + item.Pattern36.FeatureSeedCount +
                    " d1=" + selection.GetSelectedObjectCount2(1));
                var data = model.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmCurvePattern)
                    as ICurveDrivenPatternFeatureData;
                if (data == null) throw new InvalidOperationException("FEATURE36 CreateDefinition returned null.");
                int sourceMode = item.Options27.ContainsKey("PatternElement")
                    ? Convert.ToInt32(item.Options27["PatternElement"]) : 0;
                int derivedMode = item.Pattern36.BodySeedCount > 0
                    ? (int)swPatternElementSelection_e.swBodiesToPattern
                    : item.Pattern36.FaceSeedCount + item.Pattern36.FeatureSeedCount > 0
                        ? (int)swPatternElementSelection_e.swFeatureFaces : 0;
                if (derivedMode == 0 ||
                    (item.Pattern36.BodySeedCount > 0 &&
                     item.Pattern36.FaceSeedCount + item.Pattern36.FeatureSeedCount > 0))
                    throw new InvalidOperationException("FEATURE40 source pattern has ambiguous seed mode: " + originalName);
                MirrorV7Diagnostics.Log("[FEATURE40][SEED_MODE] feature=" + originalName +
                    " source=" + sourceMode + " derived=" + derivedMode +
                    " faces=" + item.Pattern36.FaceSeedCount +
                    " features=" + item.Pattern36.FeatureSeedCount +
                    " bodies=" + item.Pattern36.BodySeedCount);
                // Set seed mode before references, then restore every other captured
                // primitive option. This is feature-class logic, not part-name logic.
                string[] modeOptions = { "BodyPattern", "PatternElement", "Dir2Specified" };
                foreach (string key in modeOptions)
                    if (item.Options27.ContainsKey(key))
                        SetCurvePatternOption36(data, key, key == "PatternElement"
                            ? (object)derivedMode : item.Options27[key]);
                if (!item.Options27.ContainsKey("PatternElement"))
                    SetCurvePatternOption36(data, "PatternElement", derivedMode);
                if (item.Pattern36.FaceSeedCount > 0 && data.BodyPattern)
                    throw new InvalidOperationException("FEATURE39 face-seed pattern unexpectedly uses body mode: " + originalName);
                // Unlike the inherited definition, this fresh data object may safely receive
                // only live reflected seed entities. Preselection alone did not bind face seeds.
                item.CreatePatternBinding36(data, model);
                MirrorV7Diagnostics.Log("[FEATURE39][FRESH_BINDING] feature=" + originalName +
                    " bodyPattern=" + data.BodyPattern + " patternElement=" + data.PatternElement +
                    " faces=" + data.GetPatternFaceCount() + "/" + item.Pattern36.FaceSeedCount +
                    " features=" + data.GetPatternFeatureCount() + "/" + item.Pattern36.FeatureSeedCount);
                foreach (var option in item.Options27.Where(o => !modeOptions.Contains(o.Key)))
                    SetCurvePatternOption36(data, option.Key, option.Value);
                if (item.Pattern36.SkippedItems.Length > 0)
                    data.SkippedItemArray = item.Pattern36.SkippedItems;
                data.D1ReverseDirection = item.Pattern36.D1Reverse ^ ((mask & 1) != 0);
                if (item.Pattern36.Dir2Specified)
                    data.D2ReverseDirection = item.Pattern36.D2Reverse ^ ((mask & 2) != 0);
                int boundFaces = data.GetPatternFaceCount();
                int boundFeatures = data.GetPatternFeatureCount();
                MirrorV7Diagnostics.Log("[FEATURE37][BINDING_READBACK] feature=" + originalName +
                    " faceSeeds=" + boundFaces + "/" + item.Pattern36.FaceSeedCount +
                    " featureSeeds=" + boundFeatures + "/" + item.Pattern36.FeatureSeedCount +
                    " bodySeeds=" + data.GetPatternBodyCount() + "/" + item.Pattern36.BodySeedCount);
                // On a newly created definition these getters can still report zero:
                // SolidWorks may materialize selected seeds only in CreateFeature.
                // The marked selection list above is validated before creation;
                // validate the actual feature's seed counts and geometry afterward.
                MirrorV7Diagnostics.Log("[FEATURE38][CREATE] feature=" + originalName + " mask=" + mask +
                    " selectedFaces=" + selectedFaces + " selectedFeatures=" + selectedFeatures +
                    " preCreateFaceCount=" + boundFaces);
                Feature created = model.FeatureManager.CreateFeature(data);
                model.ClearSelection2(true);
                if (created == null)
                    throw new InvalidOperationException("FEATURE38 CreateFeature returned null after marked preselection; " +
                        "staging retained for diagnosis.");
                bool accepted = false;
                try
                {
                    MoveRollbackAfter(model, created);
                    model.EditRebuild3();
                    EnsureFeatureHasNoError(created, "FEATURE36 created pattern");
                    var readback = ReadOptions27(created.GetDefinition());
                    foreach (var option in item.Options27)
                    {
                        if (option.Key == "PatternElement" && sourceMode == 0)
                            continue; // 0 is the API's error sentinel, not a usable seed mode.
                        if (!readback.ContainsKey(option.Key) || !object.Equals(option.Value, readback[option.Key]))
                            throw new InvalidOperationException("FEATURE36 option changed: " + option.Key);
                    }
                    var patternReadback = created.GetDefinition() as ICurveDrivenPatternFeatureData;
                    if (patternReadback == null ||
                        patternReadback.GetPatternFaceCount() != item.Pattern36.FaceSeedCount ||
                        patternReadback.GetPatternFeatureCount() != item.Pattern36.FeatureSeedCount ||
                        patternReadback.GetPatternBodyCount() != item.Pattern36.BodySeedCount)
                        throw new InvalidOperationException("FEATURE39 created pattern seed counts changed: faces=" +
                            (patternReadback == null ? -1 : patternReadback.GetPatternFaceCount()) + "/" +
                            item.Pattern36.FaceSeedCount + " features=" +
                            (patternReadback == null ? -1 : patternReadback.GetPatternFeatureCount()) + "/" +
                            item.Pattern36.FeatureSeedCount);
                    var skippedReadback = patternReadback == null || item.Pattern36.SkippedItems.Length == 0 ?
                        null : patternReadback.SkippedItemArray as Array;
                    var skippedValues = skippedReadback == null ? new int[0] :
                        skippedReadback.Cast<object>().Select(Convert.ToInt32).ToArray();
                    if (!item.Pattern36.SkippedItems.SequenceEqual(skippedValues))
                        throw new InvalidOperationException("FEATURE36 skipped items changed.");
                    BaseSketchMutationEngineV7.VerifyCurrentSolids(context, item.ReflectedBodyOracle);
                    created.Name = originalName;
                    if (created.Name != originalName)
                        throw new InvalidOperationException("FEATURE36 cannot restore feature name.");
                    item.Feature.Feature = created;
                    item.Feature.PersistentReference = null;
                    item.Result = new MirrorV7FeatureResult { FeatureName = originalName,
                        FeatureType = item.Feature.TypeName, Status = MirrorV7ReplayStatus.ExactReplay,
                        Message = "FEATURE36 native curve pattern recreation; checkpoint geometry PASS." };
                    item.WasNativeReplacement = true;
                    accepted = true;
                    MirrorV7Diagnostics.Log("[FEATURE36][PASS] feature=" + originalName + " mask=" + mask +
                        " downstreamValidation=REQUIRED");
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    MirrorV7Diagnostics.Log("[FEATURE36][REJECT] feature=" + originalName +
                        " mask=" + mask + " reason=" + ex.Message);
                    if (ex.Message.StartsWith("FEATURE39 created pattern seed counts changed:"))
                        throw;
                }
                finally
                {
                    if (!accepted)
                    {
                        model.ClearSelection2(true);
                        if (!created.Select2(false, 0) || !model.Extension.DeleteSelection2(0))
                            throw new InvalidOperationException("FEATURE36 cannot remove rejected staging candidate.");
                        model.ClearSelection2(true);
                    }
                }
            }
            throw new InvalidOperationException("FEATURE36 no native curve-pattern candidate matches reflected geometry: " + originalName);
        }

        private static void SetCurvePatternOption36(ICurveDrivenPatternFeatureData data, string name, object value)
        {
            var property = typeof(ICurveDrivenPatternFeatureData).GetProperty(name);
            if (property == null || !property.CanWrite)
                throw new InvalidOperationException("FEATURE36 option unavailable: " + name);
            try { property.SetValue(data, value, null); }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException("FEATURE36 cannot set option " + name +
                    ": " + (ex.InnerException == null ? ex.Message : ex.InnerException.Message), ex);
            }
        }

        private static void EnsureSketchPatternHasNoError59(Feature feature, string message)
        {
            if (feature == null) throw new InvalidOperationException(message + " feature=<null>");
            bool warning;
            int code = feature.GetErrorCode2(out warning);
            // swSketchErrorExtRefFail (51) has been observed with warning=true.
            // A nonzero native error is never proof of a usable sketch pattern.
            if (code != 0)
                throw new InvalidOperationException(message + " errorCode=" + code + " warning=" + warning);
        }

        private static void SetSketchPatternOption59(ISketchPatternFeatureData data, string name, object value)
        {
            var property = typeof(ISketchPatternFeatureData).GetProperty(name);
            if (property == null || !property.CanWrite)
                throw new InvalidOperationException("SKETCH_PATTERN59 option unavailable: " + name);
            try { property.SetValue(data, value, null); }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException("SKETCH_PATTERN59 cannot set " + name + ": " +
                    (ex.InnerException == null ? ex.Message : ex.InnerException.Message), ex);
            }
        }

        private static void RecreateSketchPattern59(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayCheckpointV7 item)
        {
            var recipe = item.SketchPattern59;
            if (recipe == null || item.CreatePatternBinding36 == null || item.Options27 == null)
                throw new InvalidOperationException("SKETCH_PATTERN59 source recipe is incomplete: " + item.Feature.Name);
            var model = context.WorkingDocument;
            var old = item.Feature.Feature;
            string originalName = item.Feature.Name;
            var retained = new List<Tuple<string, string>>();
            int predecessorIndex = -1;
            bool foundOld = false;
            for (var cursor = model.FirstFeature() as Feature; cursor != null; cursor = cursor.GetNextFeature() as Feature)
            {
                if (SameCom28(cursor, old))
                {
                    if (foundOld) throw new InvalidOperationException("SKETCH_PATTERN59 source occurs twice in history.");
                    foundOld = true;
                    predecessorIndex = retained.Count - 1;
                }
                else retained.Add(Tuple.Create(cursor.Name, cursor.GetTypeName2()));
                if (retained.Count > 10000)
                    throw new InvalidOperationException("SKETCH_PATTERN59 feature history exceeds safety limit.");
            }
            if (!foundOld || predecessorIndex < 0)
                throw new InvalidOperationException("SKETCH_PATTERN59 predecessor unavailable: " + originalName);
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, originalName))
                throw new InvalidOperationException("SKETCH_PATTERN59 cannot roll back before source pattern.");
            MoveRollbackAfter(model, old);
            model.ClearSelection2(true);
            if (!old.Select2(false, 0) || !model.Extension.DeleteSelection2(0))
                throw new InvalidOperationException("SKETCH_PATTERN59 cannot delete the staging pattern only.");
            model.ClearSelection2(true);
            int retainedIndex = 0;
            for (var live = model.FirstFeature() as Feature; live != null; live = live.GetNextFeature() as Feature)
            {
                if (retainedIndex >= retained.Count || live.Name != retained[retainedIndex].Item1 ||
                    live.GetTypeName2() != retained[retainedIndex].Item2)
                    throw new InvalidOperationException("SKETCH_PATTERN59 deletion changed feature history at " + retainedIndex);
                retainedIndex++;
            }
            if (retainedIndex != retained.Count)
                throw new InvalidOperationException("SKETCH_PATTERN59 deletion changed feature count.");

            Feature predecessor = model.FirstFeature() as Feature;
            for (int i = 0; i < predecessorIndex && predecessor != null; i++)
                predecessor = predecessor.GetNextFeature() as Feature;
            if (predecessor == null || predecessor.Name != retained[predecessorIndex].Item1 ||
                predecessor.GetTypeName2() != retained[predecessorIndex].Item2)
                throw new InvalidOperationException("SKETCH_PATTERN59 predecessor changed before creation.");
            MoveRollbackAfter(model, predecessor);
            model.EditRebuild3();
            model.ClearSelection2(true);
            item.CreatePatternBinding36(null, model);
            var selection = model.SelectionManager as SelectionMgr;
            if (selection == null ||
                selection.GetSelectedObjectCount2(4) != recipe.FeatureSeedCount ||
                selection.GetSelectedObjectCount2(128) != recipe.FaceSeedCount ||
                selection.GetSelectedObjectCount2(256) != recipe.BodySeedCount ||
                selection.GetSelectedObjectCount2(64) != 1 ||
                selection.GetSelectedObjectCount2(32) != (recipe.ReferencePointType == -1 ? 0 : 1))
                throw new InvalidOperationException("SKETCH_PATTERN59 marked reference counts differ from source: " +
                    originalName + " features=" + (selection == null ? -1 : selection.GetSelectedObjectCount2(4)) +
                    "/" + recipe.FeatureSeedCount + " faces=" +
                    (selection == null ? -1 : selection.GetSelectedObjectCount2(128)) + "/" + recipe.FaceSeedCount +
                    " bodies=" + (selection == null ? -1 : selection.GetSelectedObjectCount2(256)) +
                    "/" + recipe.BodySeedCount + " sketch=" +
                    (selection == null ? -1 : selection.GetSelectedObjectCount2(64)) + " point=" +
                    (selection == null ? -1 : selection.GetSelectedObjectCount2(32)));
            int derivedMode = recipe.BodySeedCount > 0
                ? (int)swPatternElementSelection_e.swBodiesToPattern
                : (int)swPatternElementSelection_e.swFeatureFaces;
            if (recipe.BodySeedCount > 0 && recipe.FeatureSeedCount + recipe.FaceSeedCount > 0)
                throw new InvalidOperationException("SKETCH_PATTERN59 mixed body/feature seed mode is ambiguous.");
            int sourceMode = item.Options27.ContainsKey("PatternElement")
                ? Convert.ToInt32(item.Options27["PatternElement"]) : 0;
            if (sourceMode != 0 && sourceMode != derivedMode)
                throw new InvalidOperationException("SKETCH_PATTERN59 source seed mode contradicts selected seed types.");
            var data = model.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmSketchPattern)
                as ISketchPatternFeatureData;
            if (data == null)
                throw new InvalidOperationException("SKETCH_PATTERN59 CreateDefinition returned null.");
            if (item.Options27.ContainsKey("BodyPattern"))
                SetSketchPatternOption59(data, "BodyPattern", item.Options27["BodyPattern"]);
            SetSketchPatternOption59(data, "PatternElement", derivedMode);
            foreach (var option in item.Options27.Where(o =>
                o.Key != "PatternElement" && o.Key != "BodyPattern"))
                SetSketchPatternOption59(data, option.Key, option.Value);
            MirrorV7Diagnostics.Log("[SKETCH_PATTERN59][CREATE] feature=" + originalName +
                " featureSeeds=" + recipe.FeatureSeedCount + " faceSeeds=" + recipe.FaceSeedCount +
                " bodySeeds=" + recipe.BodySeedCount + " referencePointType=" + recipe.ReferencePointType +
                " mode=" + derivedMode);
            Feature created = model.FeatureManager.CreateFeature(data);
            model.ClearSelection2(true);
            if (created == null)
                throw new InvalidOperationException("SKETCH_PATTERN59 CreateFeature returned null; staging retained for diagnosis.");
            bool accepted = false;
            try
            {
                MoveRollbackAfter(model, created);
                model.EditRebuild3();
                EnsureSketchPatternHasNoError59(created, "SKETCH_PATTERN59 created pattern");
                var actual = created.GetDefinition() as ISketchPatternFeatureData;
                if (actual == null || actual.GetPatternFeatureCount() != recipe.FeatureSeedCount ||
                    actual.GetPatternFaceCount() != recipe.FaceSeedCount ||
                    actual.GetPatternBodyCount() != recipe.BodySeedCount ||
                    actual.GetReferencePointType() != recipe.ReferencePointType)
                    throw new InvalidOperationException("SKETCH_PATTERN59 created reference counts/type changed.");
                var readback = ReadOptions27(actual);
                foreach (var option in item.Options27)
                {
                    if (option.Key == "PatternElement" && sourceMode == 0) continue;
                    if (!readback.ContainsKey(option.Key) || !object.Equals(option.Value, readback[option.Key]))
                        throw new InvalidOperationException("SKETCH_PATTERN59 option changed: " + option.Key);
                }
                BaseSketchMutationEngineV7.VerifyCurrentSolids(context, item.ReflectedBodyOracle);
                created.Name = originalName;
                if (created.Name != originalName)
                    throw new InvalidOperationException("SKETCH_PATTERN59 cannot restore feature name.");
                item.Feature.Feature = created;
                item.Feature.PersistentReference = null;
                item.WasNativeReplacement = true;
                item.Result = new MirrorV7FeatureResult { FeatureName = originalName,
                    FeatureType = item.Feature.TypeName, Status = MirrorV7ReplayStatus.ExactReplay,
                    Message = "SKETCH_PATTERN59 native pattern recreation; checkpoint geometry PASS." };
                accepted = true;
                MirrorV7Diagnostics.Log("[SKETCH_PATTERN59][PASS] feature=" + originalName +
                    " downstreamValidation=REQUIRED");
            }
            finally
            {
                if (!accepted)
                {
                    model.ClearSelection2(true);
                    if (!created.Select2(false, 0) || !model.Extension.DeleteSelection2(0))
                        throw new InvalidOperationException("SKETCH_PATTERN59 cannot remove rejected staging candidate.");
                    model.ClearSelection2(true);
                }
            }
        }

        private static void ExecuteMappedDefinition(MirrorInPlaceExecutionContextV7 context, FeatureReplayCheckpointV7 item)
        {
            Feature feature = item.Feature.Feature;
            MoveRollbackAfter(context.WorkingDocument, feature);
            context.WorkingDocument.EditRebuild3();
            try
            {
                EnsureFeatureHasNoError(feature, "FEATURE27 inherited rebuild");
                if (item.SketchPattern59 != null)
                    EnsureSketchPatternHasNoError59(feature, "SKETCH_PATTERN59 inherited rebuild");
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
            if (item.SketchPattern59 != null)
            {
                RecreateSketchPattern59(context, item);
                return;
            }
            if (item.Chamfer != null)
            {
                RecreateChamfer32(context, item);
                return;
            }
            if (item.MoveFace64 != null)
            {
                RebindMoveFace64(context, item);
                return;
            }
            if (item.Flange != null)
            {
                RebindFlange33(context, item);
                return;
            }
            if (item.Chiral != null)
            {
                ReplayChiral45(context, item);
                return;
            }
            object initial = feature.GetDefinition();
            if (initial is IEdgeFlangeFeatureData)
                throw new InvalidOperationException("FLANGE33 source edge/profile recipe unavailable; direction guessing disabled.");
            Type type = DefinitionType(initial);
            if (type == null) throw new InvalidOperationException("Mutation handler unavailable for " + item.Feature.TypeName);
            string[] flags = initial is IChamferFeatureData2 ? new string[0]
                : initial is ICircularPatternFeatureData ? new[] { "ReverseDirection" }
                : initial is ICurveDrivenPatternFeatureData && !((ICurveDrivenPatternFeatureData)initial).Dir2Specified
                    ? new[] { "D1ReverseDirection" } : new[] { "D1ReverseDirection", "D2ReverseDirection" };
            bool[] baseline = flags.Select(n => (bool)type.GetProperty(n).GetValue(initial, null)).ToArray();
            int directionCount = 1 << flags.Length;
            bool recreatePattern = false;
            for (int mask = 0; mask < directionCount; mask++)
            {
                object data = feature.GetDefinition();
                bool opened = AccessDefinition(data, context.WorkingDocument);
                MirrorV7Diagnostics.Log("[MAPPED12][ACCESS] feature=" + feature.Name + " mask=" + mask + " opened=" + opened);
                if (!opened) throw new InvalidOperationException("Cannot access definition of " + feature.Name);
                try
                {
                    try { if (item.Rebind != null) item.Rebind(data, context.WorkingDocument); }
                    catch (InvalidOperationException ex)
                    {
                        bool staleFace = ex.Message.StartsWith(
                            "FEATURE36 curve pattern has stale native face references:", StringComparison.Ordinal);
                        bool staleFeature = ex.Message.StartsWith(
                            "FEATURE36 curve pattern has stale native feature references:", StringComparison.Ordinal);
                        if (item.Pattern36 == null || item.CreatePatternBinding36 == null ||
                            (!staleFace && !staleFeature))
                            throw;
                        MirrorV7Diagnostics.Log("[FEATURE36][RECREATE_REQUIRED] feature=" + feature.Name +
                            " reference=" + (staleFeature ? "featureSeed" : "faceSeed") +
                            " reason=" + ex.Message);
                        recreatePattern = true;
                        break;
                    }
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
            if (recreatePattern)
            {
                RecreateCurvePattern36(context, item);
                return;
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
