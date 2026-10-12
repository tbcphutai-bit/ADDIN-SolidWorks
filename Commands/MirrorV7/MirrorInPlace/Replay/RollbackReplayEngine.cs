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
        internal RollbackReplayEngineV7.MiterProfile71 MiterProfile71 { get; set; }
    }

    public static partial class RollbackReplayEngineV7
    {
        // Features whose driving profile this run drew again because its points could
        // not be moved; what SOLIDWORKS generates from that profile has a new identity.
        internal static readonly HashSet<string> RedrawnProfileOwners71 = new HashSet<string>(StringComparer.Ordinal);

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
            bool unsafeCapture69 = false;
            try
            {
                ADDIN.Helpers.NativeBodyCoincidence67.RefoldResidualAllowed71 = false;
                ADDIN.Helpers.NativeBodyCoincidence67.FoldReplayed71 = false;
                ADDIN.Helpers.NativeBodyCoincidence67.ResetSkinDeviations71();
                RedrawnProfileOwners71.Clear();
                CaptureRefAxes71(context, graph);
                Capture3DSketches71(context, graph);
                CaptureOffsetSurfaces71(context, graph);
                CaptureSurfaceChain71(context, graph);
                foreach (var node in graph.Nodes.Where(n => n.Depth == 0 && (!n.IsSuppressed || n.TypeName == "MoveFace") &&
                    DefinitionType(n.Feature.GetDefinition()) != null).OrderBy(n => n.TreeOrder))
                {
                    // The Lofted Bend that is the base feature is reflected by the base path (LOFT71), not replayed here.
                    if (node.Name == LoftedBendBaseName71) continue;
                    MoveRollbackAfter(context.WorkingDocument, node.Feature);
                    context.WorkingDocument.EditRebuild3();
                    // In front of the first solid of the tree (natively a surface Sweep
                    // ahead of a Lofted Bend base) there is no solid to take a
                    // checkpoint of, and nothing that is replayed: the base path deals
                    // with what the base refers to.
                    if ((((PartDoc)context.WorkingDocument).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0]).Length == 0)
                    {
                        MirrorV7Diagnostics.Log("[MAPPED12][CAPTURE_SKIPPED] feature=" + node.Name + " type=" + node.TypeName + " reason=NO_SOLID_BODY_YET");
                        continue;
                    }
                    if (!node.IsSuppressed) EnsureFeatureHasNoError(node.Feature, "Original mapped checkpoint");
                    var item = new FeatureReplayCheckpointV7 { Feature = node,
                        ReflectedBodyOracle = BaseSketchMutationEngineV7.ReflectCurrentSolids(context,
                            "FEATURE_CAPTURE/type=" + node.TypeName + "/order=" + node.TreeOrder + "/label=" + node.Name) };
                    item.Options27 = ReadOptions27(node.Feature.GetDefinition());
                    try
                    {
                        if (node.Feature.GetDefinition() is IMoveFaceFeatureData)
                        {
                            item.MoveFace64 = CaptureMoveFace64(context, node.Feature);
                            CaptureMoveConfigurations70(context, item);
                        }
                        else if (IsChiral45(node.Feature.GetDefinition()))
                            item.Chiral = CaptureChiral45(context, node.Feature);
                        else if (node.Feature.GetDefinition() is IChamferFeatureData2)
                            item.Chamfer = CaptureChamfer32(context, node.Feature);
                        else if (node.Feature.GetDefinition() is IEdgeFlangeFeatureData)
                        {
                            item.Flange = CaptureFlange33(context, node.Feature);
                            item.Rebind = CaptureBindings(context, node.Feature, true);
                        }
                        else if (node.Feature.GetDefinition() is IMiterFlangeFeatureData)
                        {
                            item.MiterProfile71 = CaptureMiterProfile71(node.Feature);
                            item.Rebind = CaptureBindings(context, node.Feature);
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
                    catch (ADDIN.Helpers.BodyValidationUnavailable69) { throw; }
                    catch (InvalidOperationException ex)
                    {
                        if (IsUnsafeNativeFailure66(ex)) throw;
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
                CapturePathSupportsAtSketch71(context);
            }
            catch (Exception ex)
            {
                unsafeCapture69 = IsUnsafeNativeFailure66(ex);
                throw;
            }
            finally
            {
                if (unsafeCapture69)
                    MirrorV7Diagnostics.Log("[BODY69][NATIVE_CAPTURE_STOP] rollbackSkipped=True rebuildSkipped=True furtherCadCalls=0");
                else
                {
                    if (!context.WorkingDocument.FeatureManager.EditRollback(
                        (int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, ""))
                        throw new InvalidOperationException("Cannot restore rollback after mapped capture.");
                    context.WorkingDocument.EditRebuild3();
                }
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
                        ReflectedBodyOracle = BaseSketchMutationEngineV7.ReflectCurrentSolids(context,
                            "SEQUENCE_CAPTURE/type=" + node.TypeName + "/order=" + node.TreeOrder + "/label=" + node.Name) };
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
                if (new[] { "ReverseOffset", "ReversePositionOffset", "D1ReverseDirection", "D2ReverseDirection", "ReverseDirection", "Flip" }.Contains(property.Name)) continue;
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
            // Miter Flange: the edges it runs along. Its profile is a sketch on a
            // plane normal to the first edge at its end, which follows that edge.
            if (data is IMiterFlangeFeatureData) return typeof(IMiterFlangeFeatureData);
            if (data is IChamferFeatureData2) return typeof(IChamferFeatureData2);
            if (data is ILinearPatternFeatureData) return typeof(ILinearPatternFeatureData);
            if (data is ICircularPatternFeatureData) return typeof(ICircularPatternFeatureData);
            if (data is ICurveDrivenPatternFeatureData) return typeof(ICurveDrivenPatternFeatureData);
            if (data is ISketchPatternFeatureData) return typeof(ISketchPatternFeatureData);
            // Fold / Unfold: fixed face + bends. Verified like every mapped
            // feature by the reflected whole-body checkpoint, never by a
            // folded-versus-flat Boolean.
            if (data is IFoldsFeatureData) return typeof(IFoldsFeatureData);
            // Cut With Surface: the cutting surface is an upstream surface
            // feature that follows the mirrored body by itself, so its
            // reference is kept; only the side (Flip) can need the other value.
            if (data is ISurfaceCutFeatureData) return typeof(ISurfaceCutFeatureData);
            return null;
        }

        private static bool AccessDefinition(object data, ModelDoc2 model)
        {
            if (data is IMoveFaceFeatureData) return ((IMoveFaceFeatureData)data).AccessSelections(model, null);
            if (data is ISweepFeatureData) return ((ISweepFeatureData)data).AccessSelections(model, null);
            if (data is ILoftFeatureData) return ((ILoftFeatureData)data).AccessSelections(model, null);
            if (data is ILoftedBendsFeatureData) return ((ILoftedBendsFeatureData)data).AccessSelections(model, null);
            if (data is IEdgeFlangeFeatureData) return ((IEdgeFlangeFeatureData)data).AccessSelections(model, null);
            if (data is IMiterFlangeFeatureData) return ((IMiterFlangeFeatureData)data).AccessSelections(model, null);
            if (data is IChamferFeatureData2) return ((IChamferFeatureData2)data).AccessSelections(model, null);
            if (data is ILinearPatternFeatureData) return ((ILinearPatternFeatureData)data).AccessSelections(model, null);
            if (data is ICircularPatternFeatureData) return ((ICircularPatternFeatureData)data).AccessSelections(model, null);
            if (data is ICurveDrivenPatternFeatureData) return ((ICurveDrivenPatternFeatureData)data).AccessSelections(model, null);
            if (data is ISketchPatternFeatureData) return ((ISketchPatternFeatureData)data).AccessSelections(model, null);
            if (data is IFoldsFeatureData) return ((IFoldsFeatureData)data).AccessSelections(model, null);
            if (data is ISurfaceCutFeatureData) return ((ISurfaceCutFeatureData)data).AccessSelections(model, null);
            throw new InvalidOperationException("Unsupported definition access interface.");
        }

        private static void ReleaseDefinition(object data)
        {
            if (data is IMoveFaceFeatureData) { ((IMoveFaceFeatureData)data).ReleaseSelectionAccess(); return; }
            if (data is ISweepFeatureData) { ((ISweepFeatureData)data).ReleaseSelectionAccess(); return; }
            if (data is ILoftFeatureData) { ((ILoftFeatureData)data).ReleaseSelectionAccess(); return; }
            if (data is ILoftedBendsFeatureData) { ((ILoftedBendsFeatureData)data).ReleaseSelectionAccess(); return; }
            if (data is IEdgeFlangeFeatureData) ((IEdgeFlangeFeatureData)data).ReleaseSelectionAccess();
            else if (data is IMiterFlangeFeatureData) ((IMiterFlangeFeatureData)data).ReleaseSelectionAccess();
            else if (data is IChamferFeatureData2) ((IChamferFeatureData2)data).ReleaseSelectionAccess();
            else if (data is ILinearPatternFeatureData) ((ILinearPatternFeatureData)data).ReleaseSelectionAccess();
            else if (data is ICircularPatternFeatureData) ((ICircularPatternFeatureData)data).ReleaseSelectionAccess();
            else if (data is ICurveDrivenPatternFeatureData) ((ICurveDrivenPatternFeatureData)data).ReleaseSelectionAccess();
            else if (data is ISketchPatternFeatureData) ((ISketchPatternFeatureData)data).ReleaseSelectionAccess();
            else if (data is IFoldsFeatureData) ((IFoldsFeatureData)data).ReleaseSelectionAccess();
            else if (data is ISurfaceCutFeatureData) ((ISurfaceCutFeatureData)data).ReleaseSelectionAccess();
        }

        // Capture numeric geometry while references still belong to the unmodified staging body.
        // Never retain a source Edge COM pointer for use after changing the base topology.
        private static Action<object, ModelDoc2> CaptureBindings(MirrorInPlaceExecutionContextV7 context, Feature feature,
            bool skipFlangeEdges = false, bool newPatternDefinition = false)
        {
            // The name is read once: the setters built here run after the model was
            // edited upstream, and the pointer captured now can be left behind by
            // then (natively a base profile partly drawn again: Feature.Name of the
            // captured Curve Driven Pattern failed with RPC_E_DISCONNECTED when its
            // references had to be mapped).
            string featureName71 = feature.Name;
            object data = feature.GetDefinition();
            Type type = DefinitionType(data);
            if (type == null) return null;
            if (!AccessDefinition(data, context.WorkingDocument))
                throw new InvalidOperationException("Cannot capture selections: " + featureName71);
            var setters = new List<Action<object, ModelDoc2>>();
            try
            {
                string[] names = data is IEdgeFlangeFeatureData
                    ? new[] { "Edges", "OffsetReference", "PositionOffsetReference", "AngleReference" }
                    : data is IChamferFeatureData2 ? new[] { "Edges", "Faces", "Loops", "Vertex" }
                    : data is ILinearPatternFeatureData ? new[] { "D1Axis", "D2Axis", "D1EndReference", "D2EndReference", "D1EndSeedReference", "D2EndSeedReference", "PatternFeatureArray", "PatternFaceArray", "PatternBodyArray" }
                    : data is ICircularPatternFeatureData ? new[] { "Axis", "PatternFeatureArray", "PatternFaceArray", "PatternBodyArray" }
                    : data is ISketchPatternFeatureData ? new[] { "Sketch", "PatternFeatureArray", "PatternFaceArray", "PatternBodyArray", "ReferencePoint" }
                    : data is IFoldsFeatureData ? new[] { "FixedFace", "Bends" }
                    : data is IMiterFlangeFeatureData ? new[] { "Edges" }
                    : data is ISurfaceCutFeatureData ? new[] { "SurfaceForCut" }
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
                            throw new InvalidOperationException("FEATURE29 non-feature pattern seed: " + featureName71);
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
                                MirrorV7Diagnostics.Log("[FEATURE39][FEATURE_SEEDS] feature=" + featureName71 +
                                    " count=" + seedNames.Length +
                                    " phase=" + (definition == null ? "preselect" : "freshDefinition"));
                                return;
                            }
                            var current = type.GetProperty(name).GetValue(definition, null) as Array;
                            if (current == null || current.Length != seedNames.Length)
                                throw new InvalidOperationException(
                                    (sketchPattern != null ? "SKETCH_PATTERN59 stale native feature references: " :
                                        "FEATURE36 curve pattern has stale native feature references: ") + featureName71 +
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
                                            "FEATURE36 curve pattern has stale native feature references: ") + featureName71 +
                                        " (FEATURE29 seed identity changed: " + seedNames[i] + ")");
                            }
                            MirrorV7Diagnostics.Log("[FEATURE29][SEED_IDENTITY_PASS] feature=" + featureName71 +
                                " count=" + seedNames.Length + " setter=SKIPPED");
                        });
                        continue;
                    }
                    var maps = new List<Func<ModelDoc2, object>>();
                    foreach (object value in values)
                    {
                        if (name == "SurfaceForCut" && HasFixedCutTool71(featureName71))
                        {
                            // Fixed-tool tier: the cut takes the fixed reflected surface inserted before it.
                            string cutName = featureName71;
                            maps.Add(model => FixedCutToolBody71(model, cutName));
                            continue;
                        }
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
                            maps.Add(model => FindFaceByBinding35(model, signature, featureName71, name));
                        }
                        else if (value is Vertex)
                        {
                            double[] point = context.Reflection.ReflectPoint((double[])((Vertex)value).GetPoint());
                            maps.Add(model => FindVertex(model, point));
                        }
                        else if (value is Body2)
                        {
                            // Cut With Surface: the tool surface body, found at its reflected place.
                            maps.Add(ChainBodyMap71(context, (Body2)value));
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
                                throw new InvalidOperationException("FEATURE28 segment identity unavailable: " + featureName71);
                            int segmentType = sourceSegment.GetType();
                            bool segmentConstruction = sourceSegment.ConstructionGeometry;
                            double[][] expected = SampleSketchSegment28(context.SwApp, sourceSegment)
                                .Select(context.Reflection.ReflectPoint).ToArray();
                            CapturePathSketch2D71(context, featureName71, ownerName, sourceSketch, segmentType, segmentConstruction, expected);
                            maps.Add(model => FindSketchSegment28(model, ownerName, segmentId,
                                segmentType, segmentConstruction, expected));
                        }
                        else
                        {
                            string kind = value is SketchSegment ? "SketchSegment" :
                                value is SketchPoint ? "SketchPoint" : value is Sketch ? "Sketch" :
                                value is Body2 ? "Body2" : value.GetType().FullName;
                            string unsupported = "[PREFLIGHT13] feature=" + featureName71 +
                                " property=" + name + " referenceType=" + kind +
                                ": reference mapper not implemented; no base mutation performed.";
                            MirrorV7Diagnostics.Log(unsupported);
                            throw new InvalidOperationException(unsupported);
                        }
                    }
                    setters.Add((definition, model) =>
                    {
                        MirrorV7Diagnostics.Log("[FEATURE35][MAP_PROPERTY] feature=" + featureName71 +
                            " property=" + name + " count=" + maps.Count);
                        object[] mapped = maps.Select(map => map(model)).ToArray();
                        MirrorV7Diagnostics.Log("[FEATURE35][MAPPED_ENTITIES] feature=" + featureName71 + " property=" + name + " " + string.Join(" ; ", mapped.Select(DescribeEntity71)));
                        if (newPatternDefinition && definition == null)
                        {
                            int mark = name == "Sketch" ? 64 : name == "ReferencePoint" ? 32 :
                                name == "D1Direction" ? 1 : name == "D2Direction" ? 2 :
                                name == "D1FaceNormal" ? 1024 : name == "PatternBodyArray" ? 256 :
                                name == "PatternFaceArray" ? 128 : 4;
                            foreach (object reference in mapped)
                                SelectCurvePatternReference37(model, reference, mark, featureName71, name);
                            MirrorV7Diagnostics.Log("[FEATURE37][PRESELECT] feature=" + featureName71 +
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
                            MirrorV7Diagnostics.Log("[FEATURE41][FACE_SEEDS] feature=" + featureName71 +
                                " count=" + mapped.Length + " phase=preselect-only mark=128");
                            return;
                        }
                        if (name == "Edges" && (definition is IChamferFeatureData2 || definition is IEdgeFlangeFeatureData))
                        {
                            object inherited = definition is IChamferFeatureData2
                                ? ((IChamferFeatureData2)definition).Edges
                                : ((IEdgeFlangeFeatureData)definition).Edges;
                            var current = inherited as Array;
                            bool identical = EdgeAttachmentsEquivalent31(current, mapped, featureName71);
                            if (identical)
                            {
                                MirrorV7Diagnostics.Log("[FEATURE30][EDGES_INHERITED] feature=" + featureName71 + " count=" + mapped.Length);
                                return;
                            }
                            if (definition is IEdgeFlangeFeatureData)
                                throw new InvalidOperationException("FEATURE30 flange attachment differs from reflected edges: " + featureName71 +
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
                            MirrorV7Diagnostics.Log("[FEATURE36][PATTERN_FACES] feature=" + featureName71 +
                                " mappedCount=" + mapped.Length +
                                " inheritedCount=" + (inherited == null ? -1 : inherited.Length) +
                                " equivalent=" + identical + " geometric=" + geometric + " setter=SKIPPED");
                            if (!identical)
                                throw new InvalidOperationException("FEATURE36 curve pattern has stale native face references: " +
                                    featureName71 + ". PatternFaceArray setter is unsafe; native pattern reconstruction is required.");
                            return;
                        }
                        // Fold/Unfold bends: the native setter faults on a plain
                        // object[] of RCWs; it needs IDispatch variants.
                        // Observed on IFoldsFeatureData.Bends and on
                        // ILinearPatternFeatureData.PatternFeatureArray.
                        object value71 = !array ? mapped[0] :
                            definition is IFoldsFeatureData || definition is ILinearPatternFeatureData ||
                            definition is ICircularPatternFeatureData || definition is IMiterFlangeFeatureData
                            ? (object)mapped.Select(m => new System.Runtime.InteropServices.DispatchWrapper(m)).ToArray()
                            : mapped;
                        try { property.SetValue(definition, value71, null); }
                        catch (TargetInvocationException ex)
                        {
                            throw new InvalidOperationException("FEATURE36 setter failed: " + featureName71 + "." + name +
                                " inner=" + (ex.InnerException == null ? "none" : ex.InnerException.GetType().FullName +
                                ": " + ex.InnerException.Message), ex);
                        }
                    });
                }
            }
            finally { ReleaseDefinition(data); }
            return (definition, model) => { foreach (var setter in setters) setter(definition, model); };
        }

        // The profile sketch of a Miter Flange as it is in the source: its points
        // and its lines and arcs, in model space.
        internal sealed class MiterProfile71
        {
            internal string SketchName;
            internal List<ADDIN.Helpers.SketchPointSnapshot> Points;
            internal List<ADDIN.Helpers.SketchPrimitiveSnapshot58> Primitives;
        }

        private static MiterProfile71 CaptureMiterProfile71(Feature feature)
        {
            for (var child = feature.GetFirstSubFeature() as Feature; child != null; child = child.GetNextSubFeature() as Feature)
            {
                var sketch = child.GetSpecificFeature2() as Sketch;
                if (sketch == null || sketch.Is3D() || child.GetTypeName2() != "ProfileFeature") continue;
                var profile = new MiterProfile71 { SketchName = child.Name,
                    Points = ADDIN.Helpers.SketchOperationsHelper.CapturePristineSketchPoints(sketch),
                    Primitives = ADDIN.Helpers.SketchOperationsHelper.CapturePristineSketchPrimitives58(sketch) };
                MirrorV7Diagnostics.Log("[MITER71][PROFILE_CAPTURE] feature=" + feature.Name + " sketch=" + child.Name + " points=" + profile.Points.Count +
                    " primitives=" + profile.Primitives.Count);
                return profile.Points.Count == 0 ? null : profile;
            }
            return null;
        }

        // True when the profile was edited; the tree is left rolled to just after the flange.
        private static bool ReflectMiterProfile71(MirrorInPlaceExecutionContextV7 context, FeatureReplayCheckpointV7 item, Feature feature)
        {
            var model = context.WorkingDocument;
            MiterProfile71 profile = item.MiterProfile71;
            var owner = ((PartDoc)model).FeatureByName(profile.SketchName) as Feature;
            if (owner == null) throw new InvalidOperationException("MITER71 profile sketch missing: " + profile.SketchName);
            var plane = new ADDIN.Commands.PlaneData { Origin = context.Reflection.Origin, Normal = context.Reflection.Normal };
            Sketch active = ADDIN.Helpers.SketchOperationsHelper.FreeSketchForMutation(model, owner, preserveConstraints: true);
            if (active == null) throw new InvalidOperationException("MITER71 cannot edit the profile sketch: " + profile.SketchName);
            bool drawnAgain = false;
            try { ADDIN.Helpers.SketchOperationsHelper.MutateSketchPoints(model, active, profile.Points, plane, null); }
            catch (ADDIN.Helpers.SketchPointMoveRejected58 rejected)
            {
                Sketch editing = model.SketchManager.ActiveSketch as Sketch;
                if (editing == null) throw;
                MirrorV7Diagnostics.Log("[MITER71][PROFILE_POINT_MOVE_REJECTED] sketch=" + profile.SketchName + " reason=" + rejected.Message +
                    " primitives=" + profile.Primitives.Count);
                ADDIN.Helpers.SketchOperationsHelper.RecreateMirroredPrimitives58(model, editing, profile.Primitives, plane);
                drawnAgain = true;
            }
            if (model.SketchManager.ActiveSketch != null) model.SketchManager.InsertSketch(true);
            model.ClearSelection2(true);
            feature = ((PartDoc)model).FeatureByName(item.Feature.Name) as Feature;
            if (feature == null) throw new InvalidOperationException("MITER71 flange lost after its profile was reflected: " + item.Feature.Name);
            item.Feature.Feature = feature;
            MoveRollbackAfter(model, feature);
            model.EditRebuild3();
            MirrorV7Diagnostics.Log("[MITER71][PROFILE_REFLECTED] feature=" + feature.Name + " sketch=" + profile.SketchName + " points=" + profile.Points.Count +
                " pointsMoved=" + !drawnAgain + " drawnAgain=" + drawnAgain);
            return true;
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
                (a, b) => BipartiteEquivalenceMatcherV7.SameEdgeSamples(a, b, MatchTolerance71));
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
            // The session this add-in runs in; the running-object table may hand out another SOLIDWORKS session.
            var app = (ADDIN.SwAddin.InstanceSwApp as ISldWorks) ?? System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
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

        // Standalone 3D sketches (for example a curve-pattern path) are not
        // driven by any replayed feature, so nothing moves them to the opposite
        // hand. Their points are captured, reflected, before any mutation and
        // applied on demand, once, when a consumer cannot find its reflected
        // path. The consumer's own body oracle then proves the result.
        private static readonly Dictionary<string, Dictionary<string, double[]>> sketches3d71 =
            new Dictionary<string, Dictionary<string, double[]>>(StringComparer.Ordinal);
        private static readonly HashSet<string> reflected3d71 = new HashSet<string>(StringComparer.Ordinal);
        // The same sketches as figures: per segment its type, key points (ends, arc
        // centre) and one point halfway along it, all reflected, and whether it is
        // construction geometry. Only figures of lines and arcs are recorded.
        private sealed class Segment3D71 { internal int Type; internal double[][] Key; internal double[] Halfway; internal bool Construction; internal double Length; }

        // The point halfway along an arc from its ends, centre and length. Not from the
        // parameters of its curve: natively their mean lay on the far side of the
        // circle, and the arc drawn through it was the 274 m remainder of a 2.7 m arc.
        private static double[] ArcHalfway71(double[] start, double[] centre, double[] end, double length)
        {
            double[] a = { start[0] - centre[0], start[1] - centre[1], start[2] - centre[2] }, b = { end[0] - centre[0], end[1] - centre[1], end[2] - centre[2] };
            double radius = Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
            double[] sum = { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
            double size = Math.Sqrt(sum[0] * sum[0] + sum[1] * sum[1] + sum[2] * sum[2]);
            if (radius <= 0 || size <= radius * 1e-6) throw new InvalidOperationException("SKETCH3D71 half-circle arc needs its own handler.");
            double cosine = Math.Max(-1, Math.Min(1, (a[0] * b[0] + a[1] * b[1] + a[2] * b[2]) / (radius * radius)));
            double minor = radius * Math.Acos(cosine), major = 2 * Math.PI * radius - minor;
            double sign = Math.Abs(length - minor) <= Math.Abs(length - major) ? 1 : -1;
            return new[] { centre[0] + sign * sum[0] / size * radius, centre[1] + sign * sum[1] / size * radius, centre[2] + sign * sum[2] / size * radius };
        }
        private static readonly Dictionary<string, List<Segment3D71>> figures3d71 =
            new Dictionary<string, List<Segment3D71>>(StringComparer.Ordinal);

        internal static void Capture3DSketches71(MirrorInPlaceExecutionContextV7 context, MirrorV7ModelGraph graph)
        {
            sketches3d71.Clear(); reflected3d71.Clear(); figures3d71.Clear(); ownMirrorChecked71.Clear(); pathSketches2d71.Clear();
            var math = context.SwApp.GetMathUtility() as IMathUtility;
            foreach (var node in graph.Nodes.Where(n => n.Depth == 0 && !n.IsSuppressed && n.TypeName == "3DProfileFeature"))
            {
                var sketch = node.Feature.GetSpecificFeature2() as Sketch;
                if (sketch == null || !sketch.Is3D() || math == null) continue;
                MathTransform toModel = sketch.ModelToSketchTransform == null ? null : sketch.ModelToSketchTransform.IInverse();
                var points = new Dictionary<string, double[]>(StringComparer.Ordinal);
                foreach (SketchPoint point in ADDIN.Helpers.SketchOperationsHelper.OwnedSketchPoints71(sketch))
                {
                    int[] id = point.GetID() as int[];
                    if (id == null || id.Length < 2) continue;
                    double[] model = { point.X, point.Y, point.Z };
                    if (toModel != null) model = (double[])((MathPoint)((MathPoint)math.CreatePoint(model)).MultiplyTransform(toModel)).ArrayData;
                    points[id[0] + ":" + id[1]] = context.Reflection.ReflectPoint(model);
                }
                if (points.Count > 0) sketches3d71[node.Name] = points;
                try
                {
                    var recorded = new List<Segment3D71>();
                    foreach (SketchSegment s in (sketch.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>())
                    {
                        var curve = s.GetCurve() as Curve;
                        double from, to; bool closed, periodic;
                        // A full circle has no ends to draw from; the curve's own "closed" flag is
                        // no use here, it is also set for the circle under an arc.
                        var arc = s as SketchArc;
                        if ((s.GetType() != (int)swSketchSegments_e.swSketchLINE && s.GetType() != (int)swSketchSegments_e.swSketchARC) ||
                            (arc != null && arc.IsCircle() == 1) ||
                            curve == null || !curve.GetEndParams(out from, out to, out closed, out periodic)) { recorded = null; break; }
                        double[][] key = SampleSketchSegment28(context.SwApp, s);
                        double[] halfway = arc != null ? ArcHalfway71(key[0], key[1], key[2], s.GetLength())
                            : new[] { (key[0][0] + key[1][0]) / 2, (key[0][1] + key[1][1]) / 2, (key[0][2] + key[1][2]) / 2 };
                        recorded.Add(new Segment3D71 { Type = s.GetType(), Construction = s.ConstructionGeometry, Length = s.GetLength(),
                            Key = key.Select(p => context.Reflection.ReflectPoint(p)).ToArray(), Halfway = context.Reflection.ReflectPoint(halfway) });
                    }
                    if (recorded != null && recorded.Count > 0) figures3d71[node.Name] = recorded;
                }
                catch (InvalidOperationException) { figures3d71.Remove(node.Name); }
                MirrorV7Diagnostics.Log("[SKETCH3D71][CAPTURE] sketch=" + node.Name + " points=" + points.Count);
            }
            // What each of those sketches is attached to, read with the model rolled
            // back to the sketch (an edge consumed further down is not readable from
            // the end of the tree).
            sketchLinks71.Clear();
            bool rolled = false;
            foreach (var node in graph.Nodes.Where(n => n.Depth == 0 && !n.IsSuppressed && n.TypeName == "3DProfileFeature" && sketches3d71.ContainsKey(n.Name)))
            {
                MoveRollbackAfter(context.WorkingDocument, node.Feature);
                rolled = true;
                var sketch = node.Feature.GetSpecificFeature2() as Sketch;
                if (sketch != null) CaptureSketchLink71(node.Name, sketch, p => context.Reflection.ReflectPoint(p));
            }
            if (rolled)
            {
                if (!context.WorkingDocument.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, ""))
                    throw new InvalidOperationException("Cannot restore rollback after 3D sketch capture.");
                context.WorkingDocument.EditRebuild3();
            }
        }

        // A figure that lies across the mirror plane can be its own mirror image with
        // its entities in exchanged roles. Natively: a pattern path of three arcs on
        // one circle, 2.9 m across, made by offsetting a bend edge; every point 2.9 m
        // from its own reflection and 5e-6 m from the reflection of its counterpart.
        // Such a figure cannot be moved point by point: an arc cannot take its own
        // reflection (it runs the other way round), and no point took even a 5e-6 m
        // move - trying left the figure 100 mm out of shape. Left as it stands it is
        // within micrometres of the mirror image as a set of curves, yet a Curve
        // Pattern on it failed in every direction, because the path arc then starts
        // at the far end from the seed. The mirror image of the figure is therefore
        // drawn: every line and arc again from the reflected source points, each arc
        // through its reflected start, halfway point and end, so it runs the way the
        // mirror image runs. Natively the pattern then rebuilt without error with
        // its own direction flag and its holes on the mirror image of the source
        // holes. The relations of the old entities go with them (the figure keeps
        // its shape and place, not its link to the edge it was offset from).
        // True: the sketch was of this kind and now is the mirror image.
        private static bool TryOwnMirrorImage71(ModelDoc2 model, ISldWorks app, ref Feature owner, ref Sketch sketch,
            Dictionary<string, double[]> targets)
        {
            List<Segment3D71> figure;
            if (owner == null || sketch == null || !figures3d71.TryGetValue(owner.Name, out figure)) return false;
            double limit = ADDIN.Helpers.NativeBodyCoincidence67.LocalDeviationLimit71;
            var here = ADDIN.Helpers.SketchOperationsHelper.OwnedSketchPoints71(sketch)
                .Select(p => Tuple.Create(p.GetID() as int[], new[] { p.X, p.Y, p.Z })).Where(p => p.Item1 != null && p.Item1.Length >= 2).ToList();
            double largestOwn = 0, largestCounterpart = 0;
            foreach (var point in here)
            {
                double[] own;
                if (!targets.TryGetValue(point.Item1[0] + ":" + point.Item1[1], out own)) return false;
                largestOwn = Math.Max(largestOwn, Distance(point.Item2, own));
                largestCounterpart = Math.Max(largestCounterpart, targets.Values.Min(t => Distance(point.Item2, t)));
            }
            // Farthest segment of the reflected source figure from the sketch; sameSense: also running the same way.
            // A segment is compared by its two ends and the point halfway along it, not by
            // its arc centre: the centre of a short arc of large radius is not carried by
            // three points (natively a 46.5 mm arc drawn again with ends and length exact
            // had its centre 2.9e-7 m away).
            Func<Sketch, bool, double> farthestSegment = (s, sameSense) =>
            {
                var now = (s.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>().Select(x =>
                {
                    double[][] key = SampleSketchSegment28(app, x);
                    double[] halfway = x is SketchArc && key.Length == 3 ? ArcHalfway71(key[0], key[1], key[2], x.GetLength())
                        : new[] { (key[0][0] + key[key.Length - 1][0]) / 2, (key[0][1] + key[key.Length - 1][1]) / 2, (key[0][2] + key[key.Length - 1][2]) / 2 };
                    return new Segment3D71 { Type = x.GetType(), Key = key, Halfway = halfway, Length = x.GetLength() };
                }).ToList();
                if (now.Count != figure.Count) return double.PositiveInfinity;
                double worst = 0;
                foreach (var wanted in figure)
                    worst = Math.Max(worst, now.Where(n => n.Type == wanted.Type && n.Key.Length == wanted.Key.Length).Select(n =>
                    {
                        double[] a = n.Key[0], b = n.Key[n.Key.Length - 1], wa = wanted.Key[0], wb = wanted.Key[wanted.Key.Length - 1];
                        double ends = Math.Max(Distance(a, wa), Distance(b, wb));
                        if (!sameSense) ends = Math.Min(ends, Math.Max(Distance(a, wb), Distance(b, wa)));
                        return Math.Max(Math.Max(ends, Distance(n.Halfway, wanted.Halfway)), Math.Abs(n.Length - wanted.Length));
                    }).DefaultIfEmpty(double.PositiveInfinity).Min());
                return worst;
            };
            double before = farthestSegment(sketch, false);
            if (here.Count == 0 || largestOwn <= limit || largestCounterpart > limit || before > limit) return false;
            string name = owner.Name;
            model.ClearSelection2(true);
            if (!owner.Select2(false, 0)) throw new InvalidOperationException("SKETCH3D71 cannot select " + name);
            model.EditSketch();
            Sketch active = model.SketchManager.ActiveSketch;
            if (active == null) throw new InvalidOperationException("SKETCH3D71 cannot edit " + name);
            bool addToDb = model.SketchManager.AddToDB;
            bool relations = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations);
            bool inference = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference);
            int drawn = 0;
            try
            {
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, false);
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, false);
                model.SketchManager.AddToDB = true;
                model.ClearSelection2(true);
                object[] existing = active.GetSketchSegments() as object[] ?? new object[0];
                foreach (SketchSegment segment in existing.Cast<SketchSegment>())
                    if (!segment.Select4(true, null)) throw new InvalidOperationException("SKETCH3D71 cannot select every segment of " + name);
                if (existing.Length > 0 && !model.Extension.DeleteSelection2(0))
                    throw new InvalidOperationException("SKETCH3D71 the segments of " + name + " could not be removed.");
                if ((active.GetSketchSegments() as object[] ?? new object[0]).Length != 0)
                    throw new InvalidOperationException("SKETCH3D71 segments remain in " + name + " after removal.");
                foreach (Segment3D71 wanted in figure)
                {
                    double[] a = wanted.Key[0], b = wanted.Key[wanted.Key.Length - 1], h = wanted.Halfway;
                    SketchSegment created = wanted.Type == (int)swSketchSegments_e.swSketchLINE
                        ? model.SketchManager.CreateLine(a[0], a[1], a[2], b[0], b[1], b[2]) as SketchSegment
                        : model.SketchManager.Create3PointArc(a[0], a[1], a[2], b[0], b[1], b[2], h[0], h[1], h[2]) as SketchSegment;
                    // Three points almost in a line are refused as an arc (natively an
                    // 11.6 mm arc of 44 m radius, 0.38 um off its chord). Such an arc is
                    // drawn from its centre, in the sense that gives the recorded length.
                    if (created == null && wanted.Type == (int)swSketchSegments_e.swSketchARC && wanted.Key.Length == 3)
                        foreach (short sense in new short[] { 1, -1 })
                        {
                            double[] c = wanted.Key[1];
                            created = model.SketchManager.CreateArc(c[0], c[1], c[2], a[0], a[1], a[2], b[0], b[1], b[2], sense) as SketchSegment;
                            if (created == null || Math.Abs(created.GetLength() - wanted.Length) <= 1e-7) { if (created != null) break; continue; }
                            model.ClearSelection2(true);
                            if (!created.Select4(false, null) || !model.Extension.DeleteSelection2(0))
                                throw new InvalidOperationException("SKETCH3D71 an arc of " + name + " drawn the other way round could not be removed.");
                            created = null;
                        }
                    if (created == null) throw new InvalidOperationException("SKETCH3D71 a segment of " + name + " could not be drawn again: type=" + wanted.Type +
                        " keyPoints=" + wanted.Key.Length + " length_mm=" + (wanted.Length * 1000).ToString("F6") + " drawnBefore=" + drawn +
                        " from_mm=" + string.Join(",", a.Select(v => (v * 1000).ToString("F6"))) + " to_mm=" + string.Join(",", b.Select(v => (v * 1000).ToString("F6"))) +
                        " halfway_mm=" + string.Join(",", h.Select(v => (v * 1000).ToString("F6"))));
                    // Same ends and centre are also the ends and centre of the remainder of the circle.
                    if (Math.Abs(created.GetLength() - wanted.Length) > 1e-7)
                        throw new InvalidOperationException("SKETCH3D71 a segment of " + name + " came out with another length: " +
                            created.GetLength().ToString("R") + " for " + wanted.Length.ToString("R"));
                    created.ConstructionGeometry = wanted.Construction;
                    drawn++;
                }
            }
            finally
            {
                model.SketchManager.AddToDB = addToDb;
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, relations);
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, inference);
                model.ClearSelection2(true);
                model.SketchManager.Insert3DSketch(true);
            }
            model.EditRebuild3();
            owner = ((PartDoc)model).FeatureByName(name) as Feature;
            sketch = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
            if (sketch == null) throw new InvalidOperationException("SKETCH3D71 sketch lost after it was drawn again: " + name);
            double after = farthestSegment(sketch, true);
            bool warning; int error = owner.GetErrorCode2(out warning);
            MirrorV7Diagnostics.Log("[SKETCH3D71][OWN_MIRROR_IMAGE] sketch=" + name + " points=" + here.Count + " segments=" + figure.Count +
                " farthestPointFromOwnReflection_m=" + largestOwn.ToString("E2") + " fromCounterpart_m=" + largestCounterpart.ToString("E2") +
                " figureBefore_m=" + before.ToString("E2") + " segmentsDrawnAgain=" + drawn + " figureAfter_m=" + after.ToString("E2") +
                " sketchError=" + error + (warning ? "w" : ""));
            if (after > 1e-7)
            {
                var drawnNow = (sketch.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>().ToList();
                for (int i = 0; i < figure.Count && i < drawnNow.Count; i++)
                {
                    double[][] got = SampleSketchSegment28(app, drawnNow[i]);
                    MirrorV7Diagnostics.Log("[SKETCH3D71][DRAWN_SEGMENT] index=" + i + " type=" + figure[i].Type + " length_mm=" + (figure[i].Length * 1000).ToString("F6") + "/" +
                        (drawnNow[i].GetLength() * 1000).ToString("F6") + " keyPointResiduals_m=" +
                        (got.Length == figure[i].Key.Length ? string.Join(",", Enumerable.Range(0, got.Length).Select(k => Distance(got[k], figure[i].Key[k]).ToString("E2"))) : "count"));
                }
            }
            if (after > 1e-7 || (error != 0 && !warning))
                throw new InvalidOperationException("SKETCH3D71 the sketch is not the mirror image of the source figure after it was drawn again: " + name +
                    " farthest_m=" + after.ToString("E2") + " error=" + error);
            return true;
        }

        // Before a feature is replayed, every such sketch above it is brought onto its
        // mirror image - outside any feature definition that is being edited, so the
        // model can be rebuilt and the children of the sketch (seed hole, pattern)
        // find it in place. Each sketch is looked at once.
        private static readonly HashSet<string> ownMirrorChecked71 = new HashSet<string>(StringComparer.Ordinal);

        internal static void PrepareOwnMirrorSketches71(ISldWorks app, ModelDoc2 model, string beforeFeature)
        {
            if (figures3d71.Count == 0) return;
            var pending = new List<string>();
            for (var f = model.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
            {
                if (f.Name == beforeFeature) break;
                if (f.GetTypeName2() == "3DProfileFeature" && !f.IsSuppressed() && figures3d71.ContainsKey(f.Name) &&
                    !reflected3d71.Contains(f.Name) && ownMirrorChecked71.Add(f.Name)) pending.Add(f.Name);
            }
            foreach (string name in pending)
            {
                Dictionary<string, double[]> targets;
                if (!sketches3d71.TryGetValue(name, out targets)) continue;
                if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, name))
                    throw new InvalidOperationException("SKETCH3D71 cannot roll back after " + name);
                model.EditRebuild3();
                var owner = ((PartDoc)model).FeatureByName(name) as Feature;
                var sketch = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
                if (TryOwnMirrorImage71(model, app, ref owner, ref sketch, targets)) reflected3d71.Add(name);
            }
            // The feature about to be replayed finds the model where the one before it left it.
            if (pending.Count > 0 && model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, beforeFeature))
                model.EditRebuild3();
        }

        // A stand-alone 2D sketch that a feature uses as its path (natively the line
        // a Curve Driven Pattern follows, drawn on the sheet face). It is the driving
        // sketch of no feature, so no replay reflects it. It is reflected just before
        // the feature that uses it - its support first, then its points - unless the
        // path already lies where its mirror image has to be.
        private sealed class PathSketch2D71
        {
            internal string Consumer; internal int SegmentType; internal bool Construction, Done; internal double[][] Expected;
            internal List<ADDIN.Helpers.SketchPointSnapshot> Points; internal ADDIN.Helpers.SketchSupportSnapshot20 Support;
            internal List<ADDIN.Helpers.SketchPrimitiveSnapshot58> Primitives;
            // The support as it is just after the sketch itself, before any later
            // feature has changed that face (natively a hole made in it between
            // the sketch and the pattern: the face then has one more edge and
            // 4.7e-4 less area). Used when the sketch is needed ahead of its turn.
            internal ADDIN.Helpers.SketchSupportSnapshot20 SupportAtSketch;
        }

        // The fixed face of every Flat Pattern, reflected. A Flat Pattern keeps its
        // fixed face by identity. When what generates that face is drawn again
        // upstream (natively a line of the base profile) SOLIDWORKS takes another
        // face, and the mirrored Part then unfolds from the wrong end: natively
        // its flat state lay 3.1 m from the mirror image of the source's flat
        // state, and with FixedFace2 set to the face at the reflection of the
        // source's fixed face it lay on it exactly.
        private static readonly Dictionary<string, FlangeSupport33> flatFixedFaces71 =
            new Dictionary<string, FlangeSupport33>(StringComparer.Ordinal);
        private static readonly HashSet<string> flatFixedDone71 = new HashSet<string>(StringComparer.Ordinal);
        // The same for a Flat Pattern whose fixed entity is an edge: its reflected samples.
        private static readonly Dictionary<string, double[][]> flatFixedEdges71 =
            new Dictionary<string, double[][]>(StringComparer.Ordinal);

        private static void CaptureFlatFixedFaces71(MirrorInPlaceExecutionContextV7 context)
        {
            flatFixedFaces71.Clear(); flatFixedDone71.Clear(); flatFixedEdges71.Clear();
            var model = context.WorkingDocument;
            var names = new List<string>();
            for (var f = model.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
                if (f.GetTypeName2() == "FlatPattern") names.Add(f.Name);
            foreach (string name in names)
            {
                var flat = ((PartDoc)model).FeatureByName(name) as Feature;
                var data = flat == null ? null : flat.GetDefinition() as IFlatPatternFeatureData;
                if (data == null || !data.AccessSelections(model, null))
                {
                    MirrorV7Diagnostics.Log("[FLAT71][FIXED_FACE_CAPTURE] flat=" + name + " captured=False reason=SELECTIONS_NOT_ACCESSED");
                    continue;
                }
                try
                {
                    object fixedEntity = data.FixedFace2;
                    var face = fixedEntity as Face2;
                    // A rolled sheet unfolds from a straight edge, not from a face
                    // (natively: a 2.9 m sheet of 44 m radius, whose mirrored Part
                    // unfolded from the edge at the other end).
                    var fixedEdge = face == null ? fixedEntity as Edge : null;
                    if (fixedEdge != null)
                    {
                        flatFixedEdges71[name] = SampleEdge(fixedEdge).Select(context.Reflection.ReflectPoint).ToArray();
                        MirrorV7Diagnostics.Log("[FLAT71][FIXED_FACE_CAPTURE] flat=" + name + " fixedEntity=EDGE samples=" + flatFixedEdges71[name].Length);
                        continue;
                    }
                    object[] edges = face == null ? null : face.GetEdges() as object[];
                    if (edges == null || edges.Length == 0)
                    {
                        MirrorV7Diagnostics.Log("[FLAT71][FIXED_FACE_CAPTURE] flat=" + name + " captured=False reason=" +
                            (fixedEntity == null ? "NO_FIXED_ENTITY" : face == null ? "FIXED_ENTITY_IS_NEITHER_FACE_NOR_EDGE" : "FACE_WITHOUT_EDGES"));
                        continue;
                    }
                    flatFixedFaces71[name] = new FlangeSupport33 { Area = face.GetArea(),
                        Boundaries = edges.Cast<Edge>().Select(e => SampleEdge(e).Select(context.Reflection.ReflectPoint).ToArray()).ToArray() };
                    MirrorV7Diagnostics.Log("[FLAT71][FIXED_FACE_CAPTURE] flat=" + name + " area_mm2=" + (face.GetArea() * 1e6).ToString("F3") + " edges=" + edges.Length);
                }
                catch (InvalidOperationException ex)
                {
                    MirrorV7Diagnostics.Log("[FLAT71][FIXED_FACE_CAPTURE] flat=" + name + " captured=False reason=" + ex.Message);
                }
                finally { data.ReleaseSelectionAccess(); }
            }
        }

        // Before 'beforeFeature' is replayed (null: at the end), every Flat Pattern
        // in front of it that has not been looked at yet: when its fixed face is
        // not the face at the reflection of the source's fixed face, it is given
        // that face. A reflected face that is not found is not replaced by a
        // guess: the Flat Pattern is left as it is, and the checks that follow
        // decide as they did before.
        internal static void PrepareFlatFixedFaces71(ModelDoc2 model, string beforeFeature)
        {
            if (model == null || flatFixedFaces71.Count + flatFixedEdges71.Count == flatFixedDone71.Count) return;
            var due = new List<string>();
            for (var f = model.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
            {
                // The Flat Pattern itself, when it is the step about to be replayed, is due as well.
                if ((flatFixedFaces71.ContainsKey(f.Name) || flatFixedEdges71.ContainsKey(f.Name)) && !flatFixedDone71.Contains(f.Name)) due.Add(f.Name);
                if (beforeFeature != null && f.Name == beforeFeature) break;
            }
            if (due.Count == 0) return;
            foreach (string name in due)
            {
                flatFixedDone71.Add(name);
                var flat = ((PartDoc)model).FeatureByName(name) as Feature;
                var data = flat == null ? null : flat.GetDefinition() as IFlatPatternFeatureData;
                if (data == null || !data.AccessSelections(model, null))
                {
                    MirrorV7Diagnostics.Log("[FLAT71][FIXED_FACE] flat=" + name + " result=SELECTIONS_NOT_ACCESSED leftAsItIs=True");
                    continue;
                }
                bool modified = false; string outcome;
                try
                {
                    object wanted = null; string notFound = null;
                    double[][] edgeSamples;
                    bool isEdge = flatFixedEdges71.TryGetValue(name, out edgeSamples);
                    try
                    {
                        if (isEdge) wanted = FindEdge(model, edgeSamples);
                        else { FlangeSupport33 recipe = flatFixedFaces71[name]; wanted = FindFace(model, recipe.Area, recipe.Boundaries); }
                    }
                    catch (InvalidOperationException ex) { notFound = ex.Message; }
                    object current = data.FixedFace2;
                    string kind = isEdge ? "EDGE" : "FACE";
                    if (wanted == null) outcome = "REFLECTED_" + kind + "_NOT_FOUND leftAsItIs=True reason=" + (notFound ?? "none");
                    else if (current != null && SameCom28(current, wanted)) outcome = "ALREADY_THE_REFLECTED_" + kind;
                    else
                    {
                        data.FixedFace2 = wanted;
                        modified = flat.ModifyDefinition(data, model, null);
                        outcome = modified ? "SET_TO_THE_REFLECTED_" + kind : "MODIFY_REFUSED leftAsItIs=True";
                    }
                }
                finally { if (!modified) data.ReleaseSelectionAccess(); }
                MirrorV7Diagnostics.Log("[FLAT71][FIXED_FACE] flat=" + name + " dueBefore=" + (beforeFeature ?? "END") + " result=" + outcome);
            }
            // The feature about to be replayed finds the model where the one before it left it.
            if (beforeFeature != null && model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, beforeFeature))
                model.EditRebuild3();
        }

        // What exists in another configuration only. Natively 11 of 60 sample
        // Parts carry features that are suppressed in the configuration the Part
        // is mirrored in and active in another one - nearly always the derived
        // flat-pattern configuration: holes, cuts, a Boss or a Move Face made on
        // the flat state. Nothing of the replay sees them, so for every such
        // configuration the solid of the source is recorded, reflected, before
        // anything is changed, and the mirrored Part is held against it at the
        // end. A Part is not published with a configuration that is not the
        // mirror image of the source's.
        private sealed class OtherConfiguration71
        {
            internal string Name; internal List<string> Features; internal List<Body2> Oracle;
            // Filled Surfaces that exist in this configuration only: the edges
            // each one is bounded by, reflected. Natively such a surface (made
            // over a hole of the flat state, to get its centre) lost the edge of
            // the seed hole in the mirrored Part and stopped with error 51.
            internal readonly Dictionary<string, double[][][]> FillBoundaries = new Dictionary<string, double[][][]>(StringComparer.Ordinal);
        }

        private static void CaptureFillBoundaries71(MirrorInPlaceExecutionContextV7 context, OtherConfiguration71 entry)
        {
            var model = context.WorkingDocument;
            foreach (string name in entry.Features)
            {
                var feature = ((PartDoc)model).FeatureByName(name) as Feature;
                var data = feature == null ? null : feature.GetDefinition() as IFillSurfaceFeatureData;
                if (data == null || !data.AccessSelections(model, null)) continue;
                try
                {
                    object curvature;
                    var boundary = (data.GetPatchBoundary(out curvature) as Array ?? new object[0]).Cast<object>().ToList();
                    if (boundary.Count == 0 || boundary.Any(b => !(b is Edge))) continue;
                    entry.FillBoundaries[name] = boundary.Cast<Edge>().Select(e => SampleEdge(e).Select(context.Reflection.ReflectPoint).ToArray()).ToArray();
                }
                catch (InvalidOperationException ex)
                {
                    MirrorV7Diagnostics.Log("[FILL71][BOUNDARY_CAPTURE] configuration=" + entry.Name + " feature=" + name + " captured=False reason=" + ex.Message);
                }
                finally { data.ReleaseSelectionAccess(); }
            }
            if (entry.FillBoundaries.Count > 0)
                MirrorV7Diagnostics.Log("[FILL71][BOUNDARY_CAPTURE] configuration=" + entry.Name + " filledSurfaces=" + entry.FillBoundaries.Count +
                    " edges=" + entry.FillBoundaries.Values.Sum(b => b.Length));
        }

        // The one edge of the solid that is a closed curve on which every recorded
        // point of a closed loop lies within the limit, and which is as long as the
        // loop (the polygon through the recorded points is a few per cent shorter
        // than the curve). Null when there is none or more than one.
        private static Edge FindClosedEdgeWithin71(ModelDoc2 model, double[][] samples, double limit, out double deviation)
        {
            deviation = double.PositiveInfinity;
            if (samples == null || samples.Length < 4 || Distance(samples[0], samples[samples.Length - 1]) > limit) return null;
            double polygon = Enumerable.Range(1, samples.Length - 1).Sum(i => Distance(samples[i - 1], samples[i]));
            Edge best = null; int within = 0;
            foreach (Body2 body in ((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0])
                foreach (Edge edge in body.GetEdges() as object[] ?? new object[0])
                {
                    double worst = 0;
                    foreach (double[] p in samples)
                    {
                        var near = edge.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                        worst = near == null || near.Length < 3 ? double.PositiveInfinity : Math.Max(worst, Distance(p, new[] { near[0], near[1], near[2] }));
                        if (worst > limit) break;
                    }
                    if (worst > limit) continue;
                    var curve = edge.GetCurve() as Curve; var range = edge.GetCurveParams2() as double[];
                    if (curve == null || range == null || range.Length < 8) continue;
                    double length = curve.GetLength3(range[6], range[7]);
                    if (length < polygon - 8 * limit || length > polygon * 1.06 + 8 * limit) continue;
                    within++;
                    if (worst < deviation) { deviation = worst; best = edge; }
                }
            return within == 1 ? best : null;
        }

        // A Filled Surface of this configuration that does not rebuild is given
        // the edges found at the reflections of the edges it was bounded by.
        private static void RestoreFillBoundaries71(ModelDoc2 model, OtherConfiguration71 entry)
        {
            int repaired = 0;
            foreach (var recorded in entry.FillBoundaries)
            {
                var feature = ((PartDoc)model).FeatureByName(recorded.Key) as Feature;
                if (feature == null) continue;
                bool warning; int error = feature.GetErrorCode2(out warning);
                if (error == 0 || warning) continue;
                var data = feature.GetDefinition() as IFillSurfaceFeatureData;
                if (data == null || !data.AccessSelections(model, null)) continue;
                bool modified = false, set = false; string missing = null; int found = 0; double farthest = 0;
                try
                {
                    var edges = new List<Edge>();
                    try
                    {
                        foreach (double[][] samples in recorded.Value)
                        {
                            Edge edge;
                            try { edge = FindEdge(model, samples); }
                            catch (InvalidOperationException)
                            {
                                // The flat state of the mirrored Part is the mirror image of the
                                // source's only within the deviation the mirror accepts (natively
                                // hole edges of an unfolded lofted sheet 0.03 mm off): the one
                                // closed edge every recorded point lies on within that deviation.
                                double deviation;
                                edge = FindClosedEdgeWithin71(model, samples, ADDIN.Helpers.NativeBodyCoincidence67.LocalDeviationLimit71, out deviation);
                                if (edge == null) throw;
                                farthest = Math.Max(farthest, deviation);
                            }
                            edges.Add(edge); found++;
                        }
                    }
                    catch (InvalidOperationException ex) { missing = ex.Message; }
                    if (missing == null)
                    {
                        set = data.SetPatchBoundary(edges.Select(e => new System.Runtime.InteropServices.DispatchWrapper(e)).ToArray());
                        modified = set && feature.ModifyDefinition(data, model, null);
                    }
                }
                finally { if (!modified) data.ReleaseSelectionAccess(); }
                MirrorV7Diagnostics.Log("[FILL71][BOUNDARY_GIVEN_BACK] configuration=" + entry.Name + " feature=" + recorded.Key + " errorBefore=" + error +
                    " reflectedEdgesFound=" + found + "/" + recorded.Value.Length + " boundarySet=" + set + " modified=" + modified +
                    " edgeDeviation_m=" + farthest.ToString("E2") + (missing == null ? "" : " reason=" + missing));
                if (modified) repaired++;
                if (modified && farthest > 1e-7)
                {
                    ADDIN.Helpers.NativeBodyCoincidence67.LargestAcceptedDeviation71 = Math.Max(ADDIN.Helpers.NativeBodyCoincidence67.LargestAcceptedDeviation71, farthest);
                    ADDIN.Helpers.NativeBodyCoincidence67.SurfaceChainResidualAllowed71 = true;
                }
            }
            if (repaired > 0)
            {
                model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
                model.EditRebuild3();
            }
        }
        private static readonly List<OtherConfiguration71> otherConfigurations71 = new List<OtherConfiguration71>();

        private static void CaptureOtherConfigurations71(MirrorInPlaceExecutionContextV7 context)
        {
            otherConfigurations71.Clear();
            var model = context.WorkingDocument;
            // Which features are suppressed in which configuration is taken from
            // the dependency journal this capture has already made: for a Part
            // that has nothing of the kind, not one more call goes to SOLIDWORKS
            // than before this check existed. (Natively a Part made in the context
            // of an assembly kept one external file reference in its result in
            // two runs with a first form of this capture and of its final check,
            // and in none of four runs without one or the other.)
            var journal = context.Dependencies64;
            var active = model.ConfigurationManager.ActiveConfiguration;
            string baseline = active == null ? null : active.Name;
            if (journal == null || baseline == null) return;
            var only = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var node in journal.Nodes.Values.Where(n => n.Depth == 0 && n.Suppression != null).OrderBy(n => n.Id))
            {
                if (node.Type == "FlatPattern" || node.Type == "FtrFolder") continue;
                bool suppressedHere;
                if (!node.Suppression.TryGetValue(baseline, out suppressedHere) || !suppressedHere) continue;
                foreach (var state in node.Suppression)
                {
                    if (state.Key == baseline || state.Value) continue;
                    List<string> list;
                    if (!only.TryGetValue(state.Key, out list)) only.Add(state.Key, list = new List<string>());
                    list.Add(node.Name);
                }
            }
            if (only.Count == 0) return;
            string[] names = only.Keys.ToArray();
            // The whole tree: the captures before this one leave the bar in front of
            // the Flat Pattern, and the other configuration would be recorded folded
            // (natively the mirrored flat state was then held against a folded solid).
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, ""))
                throw new InvalidOperationException("CONFIG71 cannot roll the tree forward to record the other configurations.");
            model.EditRebuild3();
            try
            {
                foreach (string name in names)
                {
                    bool shown = model.ShowConfiguration2(name);
                    if (!shown || model.ConfigurationManager.ActiveConfiguration.Name != name)
                        throw new InvalidOperationException("CONFIG71 configuration cannot be shown: " + name);
                    model.EditRebuild3();
                    var entry = new OtherConfiguration71 { Name = name, Features = only[name],
                        Oracle = BaseSketchMutationEngineV7.ReflectCurrentSolids(context, "OTHER_CONFIGURATION71/" + name) };
                    otherConfigurations71.Add(entry);
                    MirrorV7Diagnostics.Log("[CONFIG71][OTHER_CONFIGURATION_CAPTURED] configuration=" + name + " featuresOnlyThere=" + entry.Features.Count +
                        " solids=" + entry.Oracle.Count + " features=" + string.Join(",", entry.Features.Take(12)) + (entry.Features.Count > 12 ? ",..." : ""));
                    CaptureFillBoundaries71(context, entry);
                    model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
                    model.EditRebuild3();
                }
            }
            finally
            {
                if (model.ConfigurationManager.ActiveConfiguration.Name != baseline)
                {
                    bool back = model.ShowConfiguration2(baseline);
                    model.EditRebuild3();
                    if (!back || model.ConfigurationManager.ActiveConfiguration.Name != baseline)
                        throw new InvalidOperationException("CONFIG71 cannot return to the configuration the mirror works in: " + baseline);
                }
            }
        }

        // At the end, with the mirrored Part proved in the configuration it is
        // mirrored in: every recorded configuration is shown, what exists there
        // only must rebuild without error, and the solid must be the reflected
        // solid of the source in that configuration. The caller returns to the
        // working configuration.
        internal static bool HasOtherConfigurations71 { get { return otherConfigurations71.Count > 0; } }

        // What the mirror does when a configuration other than the one it works in
        // is not the mirror image of the source's: false = the Part is published and
        // the failure is reported (log, report file, message to the user); true = the
        // Part is not published. Until this check existed such a configuration was
        // never looked at, so "report" changes nothing that was published before,
        // and "refuse" would stop Parts that have always been published. Which of
        // the two it should be is the user's decision.
        internal static bool BlockOnOtherConfiguration71 = false;

        private static string FlatStates71(ModelDoc2 model)
        {
            var states = new List<string>();
            for (var f = model.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
                if (f.GetTypeName2() == "FlatPattern") states.Add(f.Name + ":" + (f.IsSuppressed() ? "suppressed" : "active"));
            return string.Join(",", states);
        }

        // At the end, with the mirrored Part proved in the configuration it is
        // mirrored in: every recorded configuration is shown, what exists there
        // only must rebuild without error, and the solid must be the reflected
        // solid of the source in that configuration. Returns what failed, one
        // line per configuration (empty: all are the mirror image). The caller
        // returns to the working configuration.
        internal static List<string> VerifyOtherConfigurations71(MirrorInPlaceExecutionContextV7 context, ModelDoc2 model)
        {
            var failures = new List<string>();
            foreach (OtherConfiguration71 entry in otherConfigurations71)
            {
                string failure = null;
                try
                {
                    bool shown = model.ShowConfiguration2(entry.Name);
                    if (!shown || model.ConfigurationManager.ActiveConfiguration.Name != entry.Name)
                        throw new InvalidOperationException("cau hinh khong mo duoc trong Part mirror");
                    model.EditRebuild3();
                    MirrorV7Diagnostics.Log("[CONFIG71][OTHER_CONFIGURATION_SHOWN] configuration=" + entry.Name + " flatPatterns=" + FlatStates71(model));
                    RestoreFillBoundaries71(model, entry);
                    foreach (string name in entry.Features)
                    {
                        var feature = ((PartDoc)model).FeatureByName(name) as Feature;
                        if (feature == null) throw new InvalidOperationException("thieu lenh '" + name + "'");
                        bool warning; int error = feature.GetErrorCode2(out warning);
                        if (error != 0 && !warning)
                            throw new InvalidOperationException("lenh chi co o cau hinh nay bi loi sau khi mirror: " + name + " (error " + error + ")");
                    }
                    try { BaseSketchMutationEngineV7.VerifyCurrentSolids(context, entry.Oracle); }
                    catch (InvalidOperationException ex)
                    {
                        throw new InvalidOperationException("than khong trung anh mirror cua Part goc (lenh chi co o cau hinh nay: " +
                            string.Join(", ", entry.Features.Take(6)) + (entry.Features.Count > 6 ? ", ..." : "") + "): " + ex.Message, ex);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    if (IsUnsafeNativeFailure66(ex)) throw;
                    failure = "cau hinh '" + entry.Name + "': " + ex.Message;
                }
                MirrorV7Diagnostics.Log("[CONFIG71][OTHER_CONFIGURATION] configuration=" + entry.Name + " featuresOnlyThere=" + entry.Features.Count +
                    " flatPatterns=" + FlatStates71(model) + " result=" + (failure == null ? "PASS rebuildErrors=0 solidIsReflectedSource=True" : "FAIL blocking=" + BlockOnOtherConfiguration71 + " reason=" + failure));
                if (failure == null) continue;
                if (BlockOnOtherConfiguration71) throw new InvalidOperationException("CONFIG71 " + failure);
                failures.Add(failure);
            }
            return failures;
        }
        // Runs after every mapped capture, with the model free to be rolled.
        private static void CapturePathSupportsAtSketch71(MirrorInPlaceExecutionContextV7 context)
        {
            CaptureFlatFixedFaces71(context);
            CaptureOtherConfigurations71(context);
            var model = context.WorkingDocument;
            foreach (var entry in pathSketches2d71)
            {
                try
                {
                    if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, entry.Key)) continue;
                    model.EditRebuild3();
                    var owner = ((PartDoc)model).FeatureByName(entry.Key) as Feature;
                    var sketch = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
                    if (sketch == null) continue;
                    entry.Value.SupportAtSketch = ADDIN.Helpers.SketchOperationsHelper.CaptureSupport20(sketch, model);
                    MirrorV7Diagnostics.Log("[SKETCH2D71][PATH_SUPPORT_AT_SKETCH] sketch=" + entry.Key + " support=" +
                        (entry.Value.SupportAtSketch == null ? "none" : entry.Value.SupportAtSketch.Kind.ToString()));
                }
                catch (InvalidOperationException ex)
                {
                    MirrorV7Diagnostics.Log("[SKETCH2D71][PATH_SUPPORT_AT_SKETCH] sketch=" + entry.Key + " captured=False reason=" + ex.Message);
                }
            }
        }

        // On demand, for a feature that does not rebuild while a path sketch it
        // refers to is still where the source had it (natively the second Hole
        // Wizard of a sheet: its position point lies on a point of the sketch
        // its own pattern follows, and that sketch sat on a face that had become
        // a new entity). Only the support of the sketch is given its reflected
        // face, in the state it was recorded in just after the sketch; the
        // sketch counts as done only when that alone makes its path the mirror
        // image. Nothing is moved or drawn here: reflecting such a sketch ahead
        // of its turn by its points left another Part's path 69 mm out. Returns
        // how many sketches became their mirror image; the tree is left rolled
        // to just after the feature that asked.
        internal static int ReflectPathParentsNow71(ISldWorks app, ModelDoc2 model, Feature user, PlaneData plane)
        {
            if (user == null) return 0;
            string userName = user.Name;
            var parents = new HashSet<string>(StringComparer.Ordinal);
            foreach (Feature parent in (user.GetParents() as object[] ?? new object[0]).OfType<Feature>()) parents.Add(parent.Name);
            var due = pathSketches2d71.Where(p => !p.Value.Done && p.Value.SupportAtSketch != null && parents.Contains(p.Key)).Select(p => p.Key).ToList();
            if (due.Count == 0) return 0;
            double limit = ADDIN.Helpers.NativeBodyCoincidence67.SurfaceChainResidualAllowed71
                ? ADDIN.Helpers.NativeBodyCoincidence67.LocalDeviationLimit71 : 1e-7;
            int reflected = 0;
            foreach (string name in due)
            {
                PathSketch2D71 path = pathSketches2d71[name];
                try
                {
                    if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, name)) continue;
                    model.EditRebuild3();
                    var owner = ((PartDoc)model).FeatureByName(name) as Feature;
                    var sketch = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
                    if (sketch == null) continue;
                    double before = PathResidual2D71(app, sketch, path);
                    ADDIN.Helpers.SketchOperationsHelper.EnsureReflectedSupport20(app, model, owner, path.Points, path.SupportAtSketch, plane);
                    model.ClearSelection2(true);
                    model.EditRebuild3();
                    owner = ((PartDoc)model).FeatureByName(name) as Feature;
                    sketch = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
                    double onSupport = sketch == null ? double.PositiveInfinity : PathResidual2D71(app, sketch, path);
                    bool done = onSupport <= limit;
                    if (done) { path.Done = true; reflected++; }
                    MirrorV7Diagnostics.Log("[SKETCH2D71][PATH_REFLECTED_FOR_EARLIER_FEATURE] sketch=" + name + " neededBy=" + userName + " recordedFor=" + path.Consumer +
                        " pathBefore_m=" + before.ToString("E2") + " onReflectedSupport_m=" + onSupport.ToString("E2") + " pointsMoved=False done=" + done);
                }
                catch (InvalidOperationException ex)
                {
                    MirrorV7Diagnostics.Log("[SKETCH2D71][PATH_REFLECTED_FOR_EARLIER_FEATURE] sketch=" + name + " neededBy=" + userName + " done=False reason=" + ex.Message);
                }
            }
            if (model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, userName)) model.EditRebuild3();
            return reflected;
        }
        private static readonly Dictionary<string, PathSketch2D71> pathSketches2d71 =
            new Dictionary<string, PathSketch2D71>(StringComparer.Ordinal);

        private static void CapturePathSketch2D71(MirrorInPlaceExecutionContextV7 context, string consumer, string ownerName, Sketch sketch,
            int segmentType, bool construction, double[][] expected)
        {
            if (sketch == null || sketch.Is3D() || pathSketches2d71.ContainsKey(ownerName)) return;
            var owner = ((PartDoc)context.WorkingDocument).FeatureByName(ownerName) as Feature;
            // A sketch absorbed by a feature is the business of that feature.
            if (owner == null || owner.GetOwnerFeature() != null) return;
            try
            {
                var path = new PathSketch2D71 { Consumer = consumer, SegmentType = segmentType, Construction = construction, Expected = expected,
                    Points = ADDIN.Helpers.SketchOperationsHelper.CapturePristineSketchPoints(sketch),
                    Primitives = ADDIN.Helpers.SketchOperationsHelper.CapturePristineSketchPrimitives58(sketch),
                    Support = ADDIN.Helpers.SketchOperationsHelper.CaptureSupport20(sketch, context.WorkingDocument) };
                pathSketches2d71[ownerName] = path;
                MirrorV7Diagnostics.Log("[SKETCH2D71][PATH_CAPTURE] sketch=" + ownerName + " consumer=" + consumer + " points=" + path.Points.Count +
                    " support=" + (path.Support == null ? "none" : path.Support.Kind));
            }
            catch (InvalidOperationException ex)
            {
                MirrorV7Diagnostics.Log("[SKETCH2D71][PATH_CAPTURE] sketch=" + ownerName + " consumer=" + consumer + " captured=False reason=" + ex.Message);
            }
        }

        // How far the nearest segment of the kind the feature uses is from the reflected source path.
        private static double PathResidual2D71(ISldWorks app, Sketch sketch, PathSketch2D71 path)
        {
            double nearest = double.PositiveInfinity;
            foreach (SketchSegment segment in sketch.GetSketchSegments() as object[] ?? new object[0])
            {
                if (segment.GetType() != path.SegmentType || segment.ConstructionGeometry != path.Construction) continue;
                double[][] actual = SampleSketchSegment28(app, segment);
                if (actual.Length != path.Expected.Length) continue;
                Func<bool, double> score = reverse => Enumerable.Range(0, actual.Length).Max(i =>
                    Distance(actual[reverse ? actual.Length - 1 - i : i], path.Expected[i]));
                nearest = Math.Min(nearest, Math.Min(score(false), score(true)));
            }
            return nearest;
        }

        internal static void PreparePathSketches2D71(ISldWorks app, ModelDoc2 model, string beforeFeature, PlaneData plane)
        {
            // Only before the feature the path was recorded for. Reflecting every
            // such sketch ahead of an earlier feature that refers to it as well (a
            // Hole Wizard on a point of the sketch its own pattern follows) was
            // tried and taken out: natively, just after the sketch itself, a path
            // held by dimensions to the sheet stayed 69 mm from its mirror image
            // whatever was done to its points, while before its pattern it is
            // within 2 mm by itself. A feature that needs it earlier asks for it
            // (ReflectPathParentsNow71), and gets only the support reflected.
            var due = pathSketches2d71.Where(p => !p.Value.Done && p.Value.Consumer == beforeFeature).Select(p => p.Key).ToList();
            if (due.Count == 0) return;
            // The state the path was recorded in: everything before the feature that uses it.
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, beforeFeature))
                throw new InvalidOperationException("SKETCH2D71 cannot roll back before " + beforeFeature);
            model.EditRebuild3();
            double limit = ADDIN.Helpers.NativeBodyCoincidence67.SurfaceChainResidualAllowed71
                ? ADDIN.Helpers.NativeBodyCoincidence67.LocalDeviationLimit71 : 1e-7;
            foreach (string name in due)
            {
                PathSketch2D71 path = pathSketches2d71[name];
                path.Done = true;
                var owner = ((PartDoc)model).FeatureByName(name) as Feature;
                var sketch = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
                if (sketch == null) throw new InvalidOperationException("SKETCH2D71 path sketch missing: " + name);
                double before = PathResidual2D71(app, sketch, path);
                if (before <= limit)
                {
                    MirrorV7Diagnostics.Log("[SKETCH2D71][PATH_IN_PLACE] sketch=" + name + " consumer=" + beforeFeature + " path_m=" + before.ToString("E2") + " untouched=True");
                    continue;
                }
                ADDIN.Helpers.SketchOperationsHelper.EnsureReflectedSupport20(app, model, owner, path.Points, path.Support, plane);
                // On its reflected support the sketch is solved again from its own
                // relations first: a path made with Offset Entities follows the
                // mirrored edge by itself, and its points cannot be placed by hand.
                model.ClearSelection2(true);
                model.EditRebuild3();
                owner = ((PartDoc)model).FeatureByName(name) as Feature;
                sketch = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
                if (sketch == null) throw new InvalidOperationException("SKETCH2D71 path sketch lost after its support was reflected: " + name);
                double onSupport = PathResidual2D71(app, sketch, path);
                bool moved = onSupport > limit, drawnAgain = false;
                if (moved)
                {
                    Sketch active = ADDIN.Helpers.SketchOperationsHelper.FreeSketchForMutation(model, owner, preserveConstraints: true) ?? sketch;
                    try { ADDIN.Helpers.SketchOperationsHelper.MutateSketchPoints(model, active, path.Points, plane, null); }
                    catch (ADDIN.Helpers.SketchPointMoveRejected58 rejected)
                    {
                        // What an Offset Entities relation holds cannot be placed by hand
                        // (natively the end of a line offset from a sheet edge). As for a
                        // cut profile, a path of lines and arcs is then drawn again at its
                        // mirror image: its shape and place are kept, its link to the edge
                        // it was offset from is not.
                        Sketch editing = model.SketchManager.ActiveSketch as Sketch;
                        if (editing == null) throw;
                        MirrorV7Diagnostics.Log("[SKETCH2D71][PATH_POINT_MOVE_REJECTED] sketch=" + name + " reason=" + rejected.Message +
                            " primitives=" + (path.Primitives == null ? 0 : path.Primitives.Count));
                        ADDIN.Helpers.SketchOperationsHelper.RecreateMirroredPrimitives58(model, editing, path.Primitives, plane);
                        if (model.SketchManager.ActiveSketch != null) model.SketchManager.InsertSketch(true);
                        drawnAgain = true;
                    }
                    model.ClearSelection2(true);
                    model.EditRebuild3();
                    owner = ((PartDoc)model).FeatureByName(name) as Feature;
                    sketch = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
                    if (sketch == null) throw new InvalidOperationException("SKETCH2D71 path sketch lost after it was reflected: " + name);
                }
                double after = moved ? PathResidual2D71(app, sketch, path) : onSupport;
                bool warning; int error = owner.GetErrorCode2(out warning);
                MirrorV7Diagnostics.Log("[SKETCH2D71][PATH_REFLECTED] sketch=" + name + " consumer=" + beforeFeature + " points=" + path.Points.Count +
                    " pathBefore_m=" + before.ToString("E2") + " onReflectedSupport_m=" + onSupport.ToString("E2") + " pointsMoved=" + (moved && !drawnAgain) +
                    " drawnAgain=" + drawnAgain +
                    " pathAfter_m=" + after.ToString("E2") + " sketchError=" + error + (warning ? "w" : ""));
                if (after > 1e-7)
                    throw new InvalidOperationException("SKETCH2D71 path sketch " + name + " did not become its mirror image: path_m=" + after.ToString("E2"));
            }
        }

        private static bool TryReflect3DSketch71(ModelDoc2 model, ISldWorks app, Feature owner, Sketch sketch)
        {
            Dictionary<string, double[]> targets;
            if (owner == null || !sketch.Is3D() || reflected3d71.Contains(owner.Name) ||
                !sketches3d71.TryGetValue(owner.Name, out targets)) return false;
            reflected3d71.Add(owner.Name);
            // A sketch attached to model edges follows them: it is re-attached to the
            // mirrored edges. Only when that is refused are its points moved as before.
            try { if (TryRelinkToMirroredModel71(model, owner.Name)) return true; }
            catch (InvalidOperationException ex)
            {
                if (IsUnsafeNativeFailure66(ex)) throw;
                MirrorV7Diagnostics.Log("[RELINK71][MIRRORED_MODEL_REFUSED] sketch=" + owner.Name + " reason=" + ex.Message);
                owner = ((PartDoc)model).FeatureByName(owner.Name) as Feature;
                sketch = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
                if (sketch == null) throw;
            }
            model.ClearSelection2(true);
            if (!owner.Select2(false, 0)) throw new InvalidOperationException("SKETCH3D71 cannot select " + owner.Name);
            model.EditSketch();
            Sketch active = model.SketchManager.ActiveSketch;
            if (active == null || !SameCom28(active, sketch))
                throw new InvalidOperationException("SKETCH3D71 expected 3D sketch is not active: " + owner.Name);
            var math = app.GetMathUtility() as IMathUtility;
            MathTransform toSketch = active.ModelToSketchTransform;
            var moves = new List<Tuple<SketchPoint, double[]>>();
            foreach (SketchPoint point in ADDIN.Helpers.SketchOperationsHelper.OwnedSketchPoints71(active))
            {
                int[] id = point.GetID() as int[];
                double[] target;
                if (id == null || id.Length < 2 || !targets.TryGetValue(id[0] + ":" + id[1], out target))
                    throw new InvalidOperationException("SKETCH3D71 point has no captured reflected position: " + owner.Name);
                if (toSketch != null) target = (double[])((MathPoint)((MathPoint)math.CreatePoint(target)).MultiplyTransform(toSketch)).ArrayData;
                moves.Add(Tuple.Create(point, target));
            }
            int suspended = 0;
            foreach (SketchRelation relation in (active.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll)
                as object[] ?? new object[0]).OfType<SketchRelation>())
            {
                if (relation.GetDisplayDimension() != null || relation.Suppressed) continue;
                relation.Suppressed = true;
                suspended++;
            }
            Func<Tuple<SketchPoint, double[]>, bool> reached = m => Math.Abs(m.Item1.X - m.Item2[0]) <= 1e-7 &&
                Math.Abs(m.Item1.Y - m.Item2[1]) <= 1e-7 && Math.Abs(m.Item1.Z - m.Item2[2]) <= 1e-7;
            for (int pass = 0; pass < 4 && moves.Any(m => !reached(m)); pass++)
                foreach (var move in moves.Where(m => !reached(m)).ToList())
                    move.Item1.SetCoords(move.Item2[0], move.Item2[1], move.Item2[2]);
            bool inSketch = moves.All(reached);
            model.SketchManager.Insert3DSketch(true);
            bool afterExit = moves.All(reached);
            MirrorV7Diagnostics.Log("[SKETCH3D71][REFLECT] sketch=" + owner.Name + " points=" + moves.Count +
                " relationsSuspended=" + suspended + " reachedInSketch=" + inSketch + " reachedAfterExit=" + afterExit);
            if (!inSketch || !afterExit)
                throw new InvalidOperationException("SKETCH3D71 points did not reach the reflected positions: " + owner.Name);
            return true;
        }

        private static SketchSegment FindSketchSegment28(ModelDoc2 model, string ownerName, int[] id,
            int segmentType, bool construction, double[][] expected)
        {
            var owner = ((PartDoc)model).FeatureByName(ownerName) as Feature;
            var sketch = owner == null ? null : owner.GetSpecificFeature2() as Sketch;
            if (sketch == null) throw new InvalidOperationException("FEATURE28 path sketch missing: " + ownerName);
            var matches = new List<Tuple<SketchSegment, double>>();
            int typeMatches = 0, sampleMatches = 0;
            // The session this add-in runs in; the running-object table may hand out another SOLIDWORKS session.
            var app = (ADDIN.SwAddin.InstanceSwApp as ISldWorks) ?? System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
            if (app == null) throw new InvalidOperationException("FEATURE28 SolidWorks instance unavailable.");
            var near71 = new List<Tuple<SketchSegment, double>>();
            double nearest71 = double.PositiveInfinity;
            for (int attempt71 = 0; attempt71 < 2; attempt71++)
            {
                matches.Clear(); near71.Clear(); typeMatches = 0; sampleMatches = 0; nearest71 = double.PositiveInfinity;
                foreach (SketchSegment segment in sketch.GetSketchSegments() as object[] ?? new object[0])
                {
                    if (segment.GetType() != segmentType || segment.ConstructionGeometry != construction) continue;
                    typeMatches++;
                    double[][] actual = SampleSketchSegment28(app, segment);
                    double[][] wanted = expected;
                    // A spline that was drawn again goes through more points than the
                    // source spline had (natively 65 against 2; its shape was measured
                    // when it was drawn). Two splines with different point counts are
                    // compared by their two ends.
                    if (actual.Length != wanted.Length && segmentType == (int)swSketchSegments_e.swSketchSPLINE &&
                        actual.Length >= 2 && wanted.Length >= 2)
                    {
                        actual = new[] { actual[0], actual[actual.Length - 1] };
                        wanted = new[] { wanted[0], wanted[wanted.Length - 1] };
                    }
                    if (actual.Length != wanted.Length) continue;
                    sampleMatches++;
                    Func<bool, double> score = reverse => Enumerable.Range(0, actual.Length).Max(i =>
                        Math.Sqrt(Enumerable.Range(0, 3).Sum(k => Math.Pow(
                            actual[reverse ? actual.Length - 1 - i : i][k] - wanted[i][k], 2))));
                    double error = Math.Min(score(false), score(true));
                    nearest71 = Math.Min(nearest71, error);
                    if (error <= 1e-7) matches.Add(Tuple.Create(segment, error));
                    else if (error <= ADDIN.Helpers.NativeBodyCoincidence67.LocalDeviationLimit71) near71.Add(Tuple.Create(segment, error));
                }
                if (matches.Count != 0 || !TryReflect3DSketch71(model, app, owner, sketch)) break;
                sketch = owner.GetSpecificFeature2() as Sketch;
            }
            // Accepted local-deviation class: a path that is a fixed copy of a model
            // edge (Offset Entities) stays where it was and is only nearly its own
            // mirror image (natively 2.6e-5 m on a 2.9 m flat-pattern edge). One such
            // segment inside the local deviation is the path; the body check after
            // the pattern measures the result.
            if (matches.Count == 0 && near71.Count == 1 && ADDIN.Helpers.NativeBodyCoincidence67.SurfaceChainResidualAllowed71)
            {
                matches.Add(near71[0]);
                MirrorV7Diagnostics.Log("[FEATURE71][PATH_WITHIN_LOCAL_DEVIATION] owner=" + ownerName + " residual_m=" + near71[0].Item2.ToString("E2") +
                    " limit_m=" + ADDIN.Helpers.NativeBodyCoincidence67.LocalDeviationLimit71.ToString("E0"));
            }
            if (matches.Count != 1)
                throw new InvalidOperationException("FEATURE28 reflected path match count=" + matches.Count +
                    " owner=" + ownerName + " sourceId=" + id[0] + "," + id[1] +
                    " type=" + segmentType + " construction=" + construction +
                    " typeCandidates=" + typeMatches + " sampleCandidates=" + sampleMatches +
                    " nearest_m=" + nearest71.ToString("E2") + " withinLocalDeviation=" + near71.Count +
                    " localDeviationClass=" + ADDIN.Helpers.NativeBodyCoincidence67.SurfaceChainResidualAllowed71);
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

        // Reference Axis: no body change, but its face/edge/vertex selections
        // die when an upstream feature (for example a pattern) is natively
        // recreated. The recipe is captured numerically on the untouched
        // staging body; the rebound axis must coincide with the reflected
        // source axis line.
        private sealed class RefAxisRecipe71
        {
            internal readonly List<Func<ModelDoc2, object>> Maps = new List<Func<ModelDoc2, object>>();
            internal double[] ReflectedA, ReflectedB;
        }
        private static readonly Dictionary<string, RefAxisRecipe71> refAxes71 = new Dictionary<string, RefAxisRecipe71>();

        // Diagnostic text for a mapped reference.
        private static string DescribeEntity71(object value)
        {
            Func<double[], string> mm = p => p == null ? "?" : string.Join(",", p.Take(3).Select(v => (v * 1000).ToString("F3")));
            var edge = value as Edge;
            if (edge != null)
            {
                var a = edge.GetStartVertex() as Vertex; var b = edge.GetEndVertex() as Vertex;
                return "Edge[" + mm(a == null ? null : (double[])a.GetPoint()) + " -> " + mm(b == null ? null : (double[])b.GetPoint()) + "]";
            }
            var face = value as Face2;
            if (face != null) return "Face[type=" + ((Surface)face.GetSurface()).Identity() + " area_mm2=" + (face.GetArea() * 1e6).ToString("F3") + " box=" + mm(((double[])face.GetBox()).Take(3).ToArray()) + ".." + mm(((double[])face.GetBox()).Skip(3).ToArray()) + "]";
            var feature = value as Feature;
            if (feature != null) return "Feature[" + feature.Name + "/" + feature.GetTypeName2() + "]";
            return value == null ? "null" : value is Vertex ? "Vertex[" + mm((double[])((Vertex)value).GetPoint()) + "]" : value is Body2 ? "Body" : "Other";
        }

        // Geometric matching tolerance. 1e-7 m everywhere, except in a Part that
        // carries the accepted micrometre sliver of a re-attached in-context
        // surface chain (user decision A1), whose entities sit up to ~3e-6 m off.
        private static double MatchTolerance71
        {
            get { return ADDIN.Helpers.NativeBodyCoincidence67.SurfaceChainResidualAllowed71 ? 5e-6 : 1e-7; }
        }

        // Second-chance distance for an edge lookup that found nothing at
        // MatchTolerance71 (see FindEdge): the vertex reach of the reduced body proof.
        private const double RefoldMatchTolerance71 = 5e-6;

        private static object FindFace71(ModelDoc2 model, int surface, double area, double[] box)
        {
            var matches = new List<Face2>();
            foreach (Body2 body in ((object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0]))
                foreach (Face2 face in ((object[])body.GetFaces() ?? new object[0]))
                {
                    double[] actual = face.GetBox() as double[];
                    if (actual != null && actual.Length >= 6 && ((Surface)face.GetSurface()).Identity() == surface &&
                        Enumerable.Range(0, 6).All(i => Math.Abs(actual[i] - box[i]) <= 1e-6) &&
                        Math.Abs(face.GetArea() - area) <= Math.Max(1e-12, area * 1e-4)) matches.Add(face);
                }
            if (matches.Count != 1) throw new InvalidOperationException("REFAXIS71 reflected face is not unique; matches=" + matches.Count);
            return matches[0];
        }

        // Offset Surface: a surface feature does not change the solid, so the
        // solid-body oracle cannot tell whether it sits on the reflected face.
        // Its source faces are therefore captured numerically and always
        // re-attached to the faces found at the reflected signature. Needed
        // in particular when the base profile was reflected through
        // counterpart targets: face identities then stay on their original
        // side while the geometry is the opposite hand.
        private static readonly Dictionary<string, List<Func<ModelDoc2, object>>> offsetSurfaces71 =
            new Dictionary<string, List<Func<ModelDoc2, object>>>(StringComparer.Ordinal);

        internal static void CaptureOffsetSurfaces71(MirrorInPlaceExecutionContextV7 context, MirrorV7ModelGraph graph)
        {
            offsetSurfaces71.Clear();
            ModelDoc2 model = context.WorkingDocument;
            foreach (var node in graph.Nodes.Where(n => n.Depth == 0 && !n.IsSuppressed && n.TypeName == "OffsetRefSurface"))
            {
                var data = node.Feature.GetDefinition() as ISurfaceOffsetFeatureData;
                if (data == null) continue;
                MoveRollbackAfter(model, node.Feature);
                model.EditRebuild3();
                if (!data.AccessSelections(model, null)) continue;
                var maps = new List<Func<ModelDoc2, object>>();
                bool supported = true;
                try
                {
                    // Entities can come back with null items; the typed Faces
                    // list is preferred when it is complete.
                    object[] entities = data.Faces as object[] ?? new object[0];
                    if (entities.Length == 0 || entities.Any(e => e == null))
                        entities = data.Entities as object[] ?? new object[0];
                    supported = entities.Length > 0;
                    string reason = supported ? "" : "NO_ENTITIES entitiesCount=" + data.GetEntitiesCount() + " facesCount=" + data.GetFacesCount();
                    foreach (object entity in entities)
                    {
                        var face = entity as Face2;
                        double[] box = face == null ? null : face.GetBox() as double[];
                        // Only faces of the solid can be located on the mirrored
                        // solid; a face of another surface body follows its owner.
                        var owner = face == null ? null : face.GetBody() as Body2;
                        if (box == null || box.Length < 6 || owner == null || owner.GetType() != (int)swBodyType_e.swSolidBody)
                        {
                            supported = false;
                            reason = "ENTITY kind=" + (face != null ? "Face" : entity is Body2 ? "Body" : entity is Feature ? "Feature" : entity == null ? "null" : "Other") +
                                " ownerBodyType=" + (owner == null ? "none" : owner.GetType().ToString());
                            break;
                        }
                        int surface = ((Surface)face.GetSurface()).Identity(); double area = face.GetArea();
                        double[] a = context.Reflection.ReflectPoint(new[] { box[0], box[1], box[2] });
                        double[] b = context.Reflection.ReflectPoint(new[] { box[3], box[4], box[5] });
                        double[] reflected = { Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2]),
                            Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Max(a[2], b[2]) };
                        maps.Add(m => FindFace71(m, surface, area, reflected));
                    }
                    if (!supported) MirrorV7Diagnostics.Log("[OFFSETSURF71][CAPTURE_DETAIL] feature=" + node.Name + " " + reason);
                }
                finally { data.ReleaseSelectionAccess(); }
                if (supported) offsetSurfaces71[node.Name] = maps;
                MirrorV7Diagnostics.Log("[OFFSETSURF71][CAPTURE] feature=" + node.Name + " faces=" + maps.Count +
                    " rebindAvailable=" + supported);
            }
        }

        // Re-attaches a captured Offset Surface to the reflected faces. No-op
        // for other features and for surfaces without a recipe.
        internal static void RebindOffsetSurface71(ModelDoc2 model, Feature feature)
        {
            List<Func<ModelDoc2, object>> maps;
            if (feature == null || feature.GetTypeName2() != "OffsetRefSurface" ||
                !offsetSurfaces71.TryGetValue(feature.Name, out maps)) return;
            var data = feature.GetDefinition() as ISurfaceOffsetFeatureData;
            if (data == null || !data.AccessSelections(model, null))
                throw new InvalidOperationException("OFFSETSURF71 cannot access " + feature.Name);
            bool modified = false, changed = false;
            try
            {
                object[] current = data.Entities as object[] ?? new object[0];
                object[] mapped = maps.Select(map => map(model)).ToArray();
                changed = current.Length != mapped.Length ||
                    Enumerable.Range(0, mapped.Length).Any(i => !SameCom28(current[i], mapped[i]));
                if (changed)
                {
                    data.Entities = mapped.Select(m => new System.Runtime.InteropServices.DispatchWrapper(m)).ToArray();
                    modified = feature.ModifyDefinition(data, model, null);
                }
            }
            finally { if (!modified) data.ReleaseSelectionAccess(); }
            bool warning;
            int error = feature.GetErrorCode2(out warning);
            MirrorV7Diagnostics.Log("[OFFSETSURF71][REBIND] feature=" + feature.Name + " faces=" + maps.Count +
                " referencesChanged=" + changed + " modified=" + modified + " error=" + error);
            if ((changed && !modified) || (error != 0 && !warning))
                throw new InvalidOperationException("OFFSETSURF71 could not re-attach " + feature.Name + " to the reflected faces. error=" + error);
        }

        internal static void CaptureRefAxes71(MirrorInPlaceExecutionContextV7 context, MirrorV7ModelGraph graph)
        {
            refAxes71.Clear();
            ModelDoc2 model = context.WorkingDocument;
            foreach (var node in graph.Nodes.Where(n => n.Depth == 0 && !n.IsSuppressed && n.TypeName == "RefAxis"))
            {
                var data = node.Feature.GetDefinition() as IRefAxisFeatureData;
                if (data == null) continue;
                MoveRollbackAfter(model, node.Feature);
                model.EditRebuild3();
                var axis = node.Feature.GetSpecificFeature2() as RefAxis;
                double[] line = axis == null ? null : axis.GetRefAxisParams() as double[];
                if (line == null || line.Length < 6 || !data.AccessSelections(model, null)) continue;
                var recipe = new RefAxisRecipe71
                {
                    ReflectedA = context.Reflection.ReflectPoint(new[] { line[0], line[1], line[2] }),
                    ReflectedB = context.Reflection.ReflectPoint(new[] { line[3], line[4], line[5] })
                };
                bool supported = true;
                try
                {
                    object types;
                    object[] selections = data.GetSelections(out types) as object[] ?? new object[0];
                    supported = selections.Length > 0;
                    foreach (object selection in selections)
                    {
                        var face = selection as Face2; var edge = selection as Edge; var vertex = selection as Vertex;
                        var reference = selection as Feature;
                        if (face != null)
                        {
                            int surface = ((Surface)face.GetSurface()).Identity(); double area = face.GetArea();
                            double[] box = face.GetBox() as double[];
                            double[] a = context.Reflection.ReflectPoint(new[] { box[0], box[1], box[2] });
                            double[] b = context.Reflection.ReflectPoint(new[] { box[3], box[4], box[5] });
                            double[] reflected = { Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2]),
                                Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Max(a[2], b[2]) };
                            recipe.Maps.Add(m => FindFace71(m, surface, area, reflected));
                        }
                        else if (edge != null)
                        {
                            double[][] samples = SampleEdge(edge).Select(context.Reflection.ReflectPoint).ToArray();
                            recipe.Maps.Add(m => FindEdge(m, samples));
                        }
                        else if (vertex != null)
                        {
                            double[] point = context.Reflection.ReflectPoint((double[])vertex.GetPoint());
                            recipe.Maps.Add(m => FindVertex(m, point));
                        }
                        else if (reference != null)
                        {
                            string name = reference.Name, type = reference.GetTypeName2();
                            recipe.Maps.Add(m =>
                            {
                                var mapped = ((PartDoc)m).FeatureByName(name) as Feature;
                                if (mapped == null || mapped.GetTypeName2() != type)
                                    throw new InvalidOperationException("REFAXIS71 reference feature missing: " + name);
                                return mapped;
                            });
                        }
                        else { supported = false; break; }
                    }
                }
                finally { data.ReleaseSelectionAccess(); }
                if (supported) refAxes71[node.Name] = recipe;
                MirrorV7Diagnostics.Log("[REFAXIS71][CAPTURE] feature=" + node.Name + " selections=" + recipe.Maps.Count +
                    " rebindAvailable=" + supported);
            }
        }

        // Returns true only when the failed axis was rebound, rebuilt without
        // error and lies on the reflected source axis line (1e-7 m).
        internal static bool TryRebindRefAxis71(ModelDoc2 model, Feature feature)
        {
            RefAxisRecipe71 recipe;
            if (feature == null || feature.GetTypeName2() != "RefAxis" || !refAxes71.TryGetValue(feature.Name, out recipe)) return false;
            var data = feature.GetDefinition() as IRefAxisFeatureData;
            if (data == null || !data.AccessSelections(model, null)) return false;
            bool modified = false;
            try
            {
                // A SAFEARRAY of entities must carry IDispatch variants; a plain
                // object[] of RCWs is rejected by SetSelections.
                var mapped = recipe.Maps.Select(map => new System.Runtime.InteropServices.DispatchWrapper(map(model))).ToArray();
                if (!data.SetSelections(mapped))
                    throw new InvalidOperationException("REFAXIS71 SetSelections rejected: " + feature.Name);
                modified = feature.ModifyDefinition(data, model, null);
            }
            finally { if (!modified) data.ReleaseSelectionAccess(); }
            bool warning;
            int error = feature.GetErrorCode2(out warning);
            var axis = feature.GetSpecificFeature2() as RefAxis;
            double[] line = axis == null ? null : axis.GetRefAxisParams() as double[];
            bool onLine = false;
            if (modified && (error == 0 || warning) && line != null && line.Length >= 6)
            {
                double[] d = { recipe.ReflectedB[0] - recipe.ReflectedA[0], recipe.ReflectedB[1] - recipe.ReflectedA[1], recipe.ReflectedB[2] - recipe.ReflectedA[2] };
                double length = Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
                Func<double[], double> offLine = p =>
                {
                    double[] v = { p[0] - recipe.ReflectedA[0], p[1] - recipe.ReflectedA[1], p[2] - recipe.ReflectedA[2] };
                    double[] c = { v[1] * d[2] - v[2] * d[1], v[2] * d[0] - v[0] * d[2], v[0] * d[1] - v[1] * d[0] };
                    return Math.Sqrt(c[0] * c[0] + c[1] * c[1] + c[2] * c[2]) / length;
                };
                onLine = length > 1e-9 && offLine(new[] { line[0], line[1], line[2] }) <= 1e-7 &&
                    offLine(new[] { line[3], line[4], line[5] }) <= 1e-7;
            }
            MirrorV7Diagnostics.Log("[REFAXIS71][REBIND] feature=" + feature.Name + " modified=" + modified + " error=" + error +
                " onReflectedSourceAxis=" + onLine + " result=" + (onLine ? "PASS" : "FAIL"));
            return onLine;
        }

        internal static Edge FindEdge(ModelDoc2 model, double[][] expected)
        {
            var matches = new List<Edge>();
            double closest71 = double.PositiveInfinity;
            double[][] closestSamples71 = null;
            // Edges a few micrometres away, for the message when nothing matches.
            var withinRefoldNoise71 = new List<Tuple<Edge, double>>();
            foreach (Body2 body in ((object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0]))
                foreach (Edge edge in ((object[])body.GetEdges() ?? new object[0]))
                {
                    double[][] actual = SampleEdge(edge);
                    if (actual == null || actual.Length != expected.Length) continue;
                    double residual71 = Math.Min(
                        Enumerable.Range(0, expected.Length).Max(i => Distance(expected[i], actual[i])),
                        Enumerable.Range(0, expected.Length).Max(i => Distance(expected[i], actual[actual.Length - 1 - i])));
                    if (residual71 < closest71) { closest71 = residual71; closestSamples71 = actual; }
                    if (residual71 <= RefoldMatchTolerance71) withinRefoldNoise71.Add(Tuple.Create(edge, residual71));
                    bool same = Enumerable.Range(0, expected.Length).All(i => Distance(expected[i], actual[i]) < MatchTolerance71);
                    bool reversed = Enumerable.Range(0, expected.Length).All(i => Distance(expected[i], actual[actual.Length - 1 - i]) < MatchTolerance71);
                    if (same || reversed) matches.Add(edge);
                }
            // A closed edge has no geometric start: its seam is wherever the
            // sketch circle's seam point happens to be, which reflection does
            // not have to preserve. When the ordered samples match nothing and
            // the expected loop is closed, match the curve itself: a closed
            // edge on which every expected sample lies within 1e-7.
            if (matches.Count == 0 && expected.Length > 2 && Distance(expected[0], expected[expected.Length - 1]) < MatchTolerance71)
            {
                foreach (Body2 body in ((object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0]))
                    foreach (Edge edge in ((object[])body.GetEdges() ?? new object[0]))
                    {
                        double[][] actual = SampleEdge(edge);
                        if (actual == null || actual.Length < 3 || Distance(actual[0], actual[actual.Length - 1]) >= MatchTolerance71) continue;
                        if (expected.All(p =>
                        {
                            var nearest = edge.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                            return nearest != null && nearest.Length >= 3 && Distance(p, nearest) < MatchTolerance71;
                        })) matches.Add(edge);
                    }
                MirrorV7Diagnostics.Log("[EDGE71][CLOSED_LOOP_MATCH] candidates=" + matches.Count +
                    " seamIndependent=True tolerance_m=1E-7");
            }
            // Same curve, other parameterisation: on a re-cut spline edge the
            // samples taken at equal parameter steps slid along the curve
            // (natively 1.95 mm) although both ends and the curve itself
            // coincided. An open edge is then matched by its two end points and
            // by every expected sample lying on it.
            if (matches.Count == 0 && expected.Length > 2 && Distance(expected[0], expected[expected.Length - 1]) >= MatchTolerance71)
            {
                foreach (Body2 body in ((object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0]))
                    foreach (Edge edge in ((object[])body.GetEdges() ?? new object[0]))
                    {
                        double[][] actual = SampleEdge(edge);
                        if (actual == null || actual.Length < 2) continue;
                        double[] a = actual[0], b = actual[actual.Length - 1], p0 = expected[0], p1 = expected[expected.Length - 1];
                        bool ends = (Distance(a, p0) < MatchTolerance71 && Distance(b, p1) < MatchTolerance71) ||
                            (Distance(a, p1) < MatchTolerance71 && Distance(b, p0) < MatchTolerance71);
                        if (ends && expected.All(p =>
                        {
                            var nearest = edge.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                            return nearest != null && nearest.Length >= 3 && Distance(p, nearest) < MatchTolerance71;
                        })) matches.Add(edge);
                    }
                MirrorV7Diagnostics.Log("[EDGE71][CURVE_MATCH] candidates=" + matches.Count + " parameterisationIndependent=True");
            }
            // A body accepted inside the local deviation (0.1 mm, every Part since
            // 2026-10-08) carries edges that are not on the reflected source to the
            // matching tolerance (natively a 2 mm thickness edge 3.7e-6 m away, and a
            // sliver's second edge 4.5e-6 m away). When nothing matches there, the
            // nearest edge within 5e-6 m is the reflected edge, unless two are equally
            // near. The distance is logged and counts toward the largest accepted
            // deviation of the run; the body check after the feature that uses the
            // edge judges the result.
            withinRefoldNoise71 = withinRefoldNoise71.OrderBy(t => t.Item2).ToList();
            if (matches.Count == 0 && withinRefoldNoise71.Count >= 1 &&
                (withinRefoldNoise71.Count == 1 || withinRefoldNoise71[1].Item2 - withinRefoldNoise71[0].Item2 > 1e-7))
            {
                matches.Add(withinRefoldNoise71[0].Item1);
                ADDIN.Helpers.NativeBodyCoincidence67.LargestAcceptedDeviation71 = Math.Max(
                    ADDIN.Helpers.NativeBodyCoincidence67.LargestAcceptedDeviation71, withinRefoldNoise71[0].Item2);
                MirrorV7Diagnostics.Log("[EDGE71][NEAREST_WITHIN_DEVIATION] residual_m=" + withinRefoldNoise71[0].Item2.ToString("E2") +
                    " limit_m=" + RefoldMatchTolerance71.ToString("E0") + " candidates=" + withinRefoldNoise71.Count +
                    (withinRefoldNoise71.Count > 1 ? " next_m=" + withinRefoldNoise71[1].Item2.ToString("E2") : "") + " exactMatches=0");
            }
            if (matches.Count != 1) throw new InvalidOperationException("Reflected edge mapping is not unique; matches=" + matches.Count +
                " within" + RefoldMatchTolerance71.ToString("E0") + "=" + withinRefoldNoise71.Count + "[" +
                string.Join(",", withinRefoldNoise71.Take(4).Select(t => t.Item2.ToString("E2"))) + "]" +
                " closestEdgeResidual_m=" + closest71.ToString("E3") + " from_mm=" + string.Join(",", expected[0].Select(v => (v * 1000).ToString("F3"))) +
                " to_mm=" + string.Join(",", expected[expected.Length - 1].Select(v => (v * 1000).ToString("F3"))) +
                " mid_mm=" + string.Join(",", expected[expected.Length / 2].Select(v => (v * 1000).ToString("F3"))) +
                (closestSamples71 == null ? "" : " closestFrom_mm=" + string.Join(",", closestSamples71[0].Select(v => (v * 1000).ToString("F3"))) +
                    " closestMid_mm=" + string.Join(",", closestSamples71[closestSamples71.Length / 2].Select(v => (v * 1000).ToString("F3"))) +
                    " closestTo_mm=" + string.Join(",", closestSamples71[closestSamples71.Length - 1].Select(v => (v * 1000).ToString("F3")))));
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
                    if (Distance(expected, (double[])vertex.GetPoint()) < MatchTolerance71) matches.Add(vertex);
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
                        Enumerable.Range(0, a.Length).All(i => Distance(a[i], b[i]) < MatchTolerance71) ||
                        Enumerable.Range(0, a.Length).All(i => Distance(a[i], b[b.Length - 1 - i]) < MatchTolerance71)).HasPerfectMatching)
                        matches.Add(face);
                }
            if (matches.Count == 0)
            {
                // A natively re-built spline face has its own edge parameterisation
                // and, in the accepted re-attachment class, an area that moved by
                // integration noise. The face is then found by where its boundary
                // lies: every recorded edge on one edge of the face (ends in either
                // order, every sample on the edge), one to one.
                double areaNoise = ADDIN.Helpers.NativeBodyCoincidence67.SurfaceChainResidualAllowed71 ? 5e-4 : 1e-6;
                double nearestArea = double.PositiveInfinity;
                foreach (Body2 body in ((object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0]))
                    foreach (Face2 face in ((object[])body.GetFaces() ?? new object[0]))
                    {
                        double off = Math.Abs(face.GetArea() - area) / Math.Max(1e-12, area);
                        nearestArea = Math.Min(nearestArea, off);
                        if (off > areaNoise) continue;
                        var faceEdges = ((object[])face.GetEdges() ?? new object[0]).ToList();
                        if (faceEdges.Count != boundaries.Length) continue;
                        if (BipartiteEquivalenceMatcherV7.Match<object>(boundaries.Cast<object>().ToList(), faceEdges,
                            (a, e) => EdgeResidual71((Edge)e, (double[][])a) < MatchTolerance71 ||
                                ClosedEdgeResidual71((Edge)e, (double[][])a) < MatchTolerance71).HasPerfectMatching)
                            matches.Add(face);
                    }
                MirrorV7Diagnostics.Log("[FACE71][CURVE_MATCH] area_mm2=" + (area * 1e6).ToString("F3") + " boundaries=" + boundaries.Length +
                    " nearestAreaRelative=" + nearestArea.ToString("E2") + " areaLimit=" + areaNoise.ToString("E0") +
                    " closedEdgesSeamIndependent=True candidates=" + matches.Count);
                if (matches.Count == 0)
                {
                    // The same face with one of its edges in more pieces (natively the
                    // large face of an Unfold whose body had 76 corners against 74: 38
                    // edges for 37 recorded, every recorded point on the face to 1e-13 m).
                    // Such a face has the recorded area, more edges than were recorded,
                    // and every recorded point of every recorded edge on one of them.
                    foreach (Body2 body in ((object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0]))
                        foreach (Face2 face in ((object[])body.GetFaces() ?? new object[0]))
                        {
                            if (Math.Abs(face.GetArea() - area) / Math.Max(1e-12, area) > areaNoise) continue;
                            var pieces = ((object[])face.GetEdges() ?? new object[0]).Cast<Edge>().ToList();
                            if (pieces.Count <= boundaries.Length) continue;
                            if (boundaries.All(b => b.All(p => pieces.Any(e =>
                                {
                                    var q = e.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                                    return q != null && q.Length >= 3 && Distance(p, new[] { q[0], q[1], q[2] }) < MatchTolerance71;
                                }))))
                                matches.Add(face);
                        }
                    MirrorV7Diagnostics.Log("[FACE71][EDGE_IN_PIECES] area_mm2=" + (area * 1e6).ToString("F3") + " recordedEdges=" + boundaries.Length +
                        " candidates=" + matches.Count);
                }
                // Nothing found: say for each face of that area how far every recorded edge is from its nearest edge.
                if (matches.Count == 0)
                    foreach (Body2 body in ((object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0]))
                        foreach (Face2 face in ((object[])body.GetFaces() ?? new object[0]))
                        {
                            if (Math.Abs(face.GetArea() - area) / Math.Max(1e-12, area) > areaNoise) continue;
                            var faceEdges = ((object[])face.GetEdges() ?? new object[0]).Cast<Edge>().ToList();
                            MirrorV7Diagnostics.Log("[FACE71][NEAR_AREA] edges=" + faceEdges.Count + " recorded=" + boundaries.Length + " nearestEdge_m=[" +
                                string.Join(" ", boundaries.Select(b => (Distance(b[0], b[b.Length - 1]) < MatchTolerance71 ? "closed:" : "open:") +
                                    faceEdges.Select(e => Math.Min(EdgeResidual71(e, b), ClosedEdgeResidual71(e, b))).DefaultIfEmpty(double.PositiveInfinity).Min().ToString("E2"))) +
                                "] recordedFrom_mm=[" + string.Join(" ", boundaries.Select(b => string.Join(",", b[0].Select(v => (v * 1000).ToString("F3"))))) + "]");
                        }
            }
            if (matches.Count != 1) throw new InvalidOperationException("Reflected face is ambiguous or missing: " + matches.Count);
            return matches[0];
        }

        // A closed edge has no geometric start (see FindEdge): its seam is where
        // SOLIDWORKS puts it, which reflection does not have to preserve (natively
        // a circle and an ellipse inside a planar face). A recorded closed loop and
        // a closed edge are the same curve when every recorded sample lies on the
        // edge and the edge is as long as the recorded loop.
        private static double ClosedEdgeResidual71(Edge edge, double[][] geometry)
        {
            if (geometry == null || geometry.Length < 4 || Distance(geometry[0], geometry[geometry.Length - 1]) >= MatchTolerance71)
                return double.PositiveInfinity;
            var range = edge.GetCurveParams2() as double[];
            if (range == null || range.Length < 8 ||
                Distance(new[] { range[0], range[1], range[2] }, new[] { range[3], range[4], range[5] }) >= MatchTolerance71)
                return double.PositiveInfinity;
            double worst = 0;
            foreach (double[] p in geometry)
            {
                var near = edge.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                if (near == null || near.Length < 3) return double.PositiveInfinity;
                worst = Math.Max(worst, Distance(p, new[] { near[0], near[1], near[2] }));
            }
            // Recorded length: the polygon through the samples is shorter than the
            // curve, never longer; an edge that only passes through them and is
            // shorter than that polygon is another curve.
            double polygon = Enumerable.Range(1, geometry.Length - 1).Sum(i => Distance(geometry[i - 1], geometry[i]));
            var curve = edge.GetCurve() as Curve;
            double length = curve == null ? 0 : Math.Abs(curve.GetLength3(range[6], range[7]));
            if (length < polygon - MatchTolerance71 || length > polygon * 1.2) return double.PositiveInfinity;
            return worst;
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
                if (!witnesses.Any(w => Distance(w, point) < MatchTolerance71)) witnesses.Add(point);
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
                            Distance(witness, new[] { closest[0], closest[1], closest[2] }) < MatchTolerance71) count++;
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
            // A deliberate edit upstream (a base profile drawn again, a fixed surface
            // inserted) can leave the captured pointer behind: natively Feature.Name
            // then failed with RPC_E_DISCONNECTED. The feature is taken by name.
            Feature feature = ((PartDoc)context.WorkingDocument).FeatureByName(item.Feature.Name) as Feature;
            if (feature == null) throw new InvalidOperationException("Mapped feature no longer exists: " + item.Feature.Name);
            item.Feature.Feature = feature;
            MoveRollbackAfter(context.WorkingDocument, feature);
            context.WorkingDocument.EditRebuild3();
            if (feature.GetDefinition() is IFoldsFeatureData)
                ADDIN.Helpers.NativeBodyCoincidence67.RefoldResidualAllowed71 = true;
            // Unfold has the same definition type; only the Fold opens the wider local limit.
            if (feature.GetTypeName2() == "Fold") ADDIN.Helpers.NativeBodyCoincidence67.FoldReplayed71 = true;
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
            // Miter Flange: its edges and the plane of its profile follow the
            // mirrored sheet, but the profile keeps its own coordinates in that
            // plane, and those are not the mirror image (natively the flange of
            // the mirrored Part went up and outward instead of down and inward).
            // The profile is put on the reflections of its source points, and the
            // checkpoint is asked again before any edge is touched.
            if (item.MiterProfile71 != null && ReflectMiterProfile71(context, item, feature))
            {
                feature = item.Feature.Feature;
                try
                {
                    EnsureFeatureHasNoError(feature, "MITER71 rebuild after the profile was reflected");
                    VerifyOptions27(item);
                    BaseSketchMutationEngineV7.VerifyCurrentSolids(context, item.ReflectedBodyOracle);
                    item.Result = new MirrorV7FeatureResult { FeatureName = item.Feature.Name,
                        FeatureType = item.Feature.TypeName, Status = MirrorV7ReplayStatus.ExactReplay,
                        Message = "MITER71 profile reflected; inherited edges; checkpoint geometry PASS." };
                    MirrorV7Diagnostics.Log("[FEATURE27][PASS] feature=" + feature.Name + " strategy=MITER_PROFILE_REFLECTED");
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    MirrorV7Diagnostics.Log("[MITER71][AFTER_PROFILE] feature=" + feature.Name + " reason=" + ex.Message);
                }
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
            string[] flags = initial is IChamferFeatureData2 || initial is IFoldsFeatureData || initial is IMiterFlangeFeatureData ? new string[0]
                : initial is ISurfaceCutFeatureData ? new[] { "Flip" }
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
