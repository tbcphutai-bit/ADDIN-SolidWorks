using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    internal sealed class DependencyJournal64
    {
        internal readonly Dictionary<int, DependencyNode64> Nodes = new Dictionary<int, DependencyNode64>();
        internal readonly Dictionary<int, FeatureReplayCheckpointV7> Recipes = new Dictionary<int, FeatureReplayCheckpointV7>();
        internal readonly HashSet<int> Pending = new HashSet<int>();
    }

    public static partial class RollbackReplayEngineV7
    {
        internal sealed class MoveFaceRecipe64
        {
            internal int MoveType, EndCondition;
            internal bool Reverse;
            internal double Distance, Angle, Offset;
            internal double[] Translation, Direction;
            internal Func<ModelDoc2, object>[] Faces;
            internal Func<ModelDoc2, object> DirectionReference, EndReference, FromReference;
        }

        private static void CaptureDependencyGraph64(MirrorInPlaceExecutionContextV7 context)
        {
            var journal = new DependencyJournal64();
            string[] configs = context.WorkingDocument.GetConfigurationNames() as string[];
            if (configs == null || configs.Length == 0)
                throw new InvalidOperationException("DEPENDENCY64 configurations unavailable.");
            var external = new Dictionary<string, int>(StringComparer.Ordinal);
            Func<Feature, int> parentId = null;
            parentId = parent =>
            {
                var matches = context.SourceGraph.Nodes.Where(n => SameCom28(n.Feature, parent)).ToArray();
                if (matches.Length == 0)
                    matches = context.SourceGraph.Nodes.Where(n => n.Name == parent.Name &&
                        n.TypeName == FeatureTypeHelperV7.GetEffectiveType(parent)).ToArray();
                if (matches.Length == 1) return matches[0].TreeOrder;
                if (matches.Length > 1) throw new InvalidOperationException("DEPENDENCY64 ambiguous parent identity: " + parent.Name);
                // Native generated features can depend on hidden helper features
                // outside FirstFeature/subfeature traversal. Keep these vertices
                // explicitly; do not drop dependency edges or alter the scanner.
                string key = parent.Name + "\n" + FeatureTypeHelperV7.GetEffectiveType(parent);
                int id;
                if (external.TryGetValue(key, out id)) return id;
                if (external.Count >= 10000) throw new InvalidOperationException("DEPENDENCY64 hidden-parent safety limit exceeded.");
                id = -1 - external.Count;
                external.Add(key, id); // Register BEFORE recursion, including cycles.
                var hidden = new DependencyNode64 { Id = id, Depth = -1, Name = parent.Name,
                    Type = FeatureTypeHelperV7.GetEffectiveType(parent), Parents = new int[0], Recoverable = false };
                journal.Nodes.Add(id, hidden);
                hidden.Parents = (parent.GetParents() as object[] ?? new object[0]).OfType<Feature>().Select(parentId).Distinct().ToArray();
                MirrorV7Diagnostics.Log("[DEPENDENCY64][HIDDEN_PARENT] id=" + id + " name=" + hidden.Name +
                    " type=" + hidden.Type + " deletionRecovery=False");
                return id;
            };
            foreach (var source in context.SourceGraph.Nodes)
            {
                var parents = new List<int>();
                foreach (Feature parent in (source.Feature.GetParents() as object[] ?? new object[0]).OfType<Feature>())
                {
                    parents.Add(parentId(parent));
                }
                var states = source.Feature.IsSuppressed2((int)swInConfigurationOpts_e.swSpecifyConfiguration, configs) as Array;
                if (states == null || states.Length != configs.Length)
                    throw new InvalidOperationException("DEPENDENCY64 suppression snapshot unavailable: " + source.Name);
                var suppression = new Dictionary<string, bool>(StringComparer.Ordinal);
                for (int i = 0; i < configs.Length; i++) suppression.Add(configs[i], (bool)states.GetValue(i));
                journal.Nodes.Add(source.TreeOrder, new DependencyNode64 { Id = source.TreeOrder,
                    Depth = source.Depth, Name = source.Name, Type = source.TypeName,
                    Parents = parents.Distinct().ToArray(), Suppression = suppression });
            }
            context.Dependencies64 = journal;
            MirrorV7Diagnostics.Log("[DEPENDENCY64][GRAPH_CAPTURED] nodes=" + journal.Nodes.Count +
                " configurations=" + configs.Length + " identity=SOURCE_GRAPH_ID filenameRules=False");
        }

        private static Func<ModelDoc2, object> CaptureMoveReference64(MirrorInPlaceExecutionContextV7 context,
            object value, string feature, string property)
        {
            if (value == null) return null;
            if (value is Face2)
            {
                var binding = CaptureFaceBinding35((Face2)value, context.Reflection);
                return model => FindFaceByBinding35(model, binding, feature, property);
            }
            if (value is Edge)
            {
                var points = SampleEdge((Edge)value).Select(context.Reflection.ReflectPoint).ToArray();
                return model => FindEdge(model, points);
            }
            if (value is Vertex)
            {
                var point = context.Reflection.ReflectPoint((double[])((Vertex)value).GetPoint());
                return model => FindVertex(model, point);
            }
            if (value is Feature)
            {
                var source = (Feature)value;
                var node = context.SourceGraph.Nodes.Single(n => n.Name == source.Name &&
                    n.TypeName == FeatureTypeHelperV7.GetEffectiveType(source));
                int id = node.TreeOrder;
                return model => ResolveDependencyFeature64(context, id);
            }
            throw new InvalidOperationException("MOVEFACE64 unsupported reference type at " + feature + "." + property);
        }

        private static MoveFaceRecipe64 CaptureMoveFace64(MirrorInPlaceExecutionContextV7 context, Feature feature)
        {
            var data = (IMoveFaceFeatureData)feature.GetDefinition();
            if (!data.AccessSelections(context.WorkingDocument, null))
                throw new InvalidOperationException("MOVEFACE64 selections unavailable; suppressed commands are not blindly unsuppressed.");
            try
            {
                var faces = data.Faces as Array;
                if (faces == null || faces.Length == 0 || faces.Length != data.GetFacesCount() ||
                    faces.Cast<object>().Any(f => !(f is Face2)))
                    throw new InvalidOperationException("MOVEFACE64 face recipe incomplete.");
                int directionType, fromType;
                object direction = data.GetDirectionReference(out directionType), from;
                data.GetFromEntity(out from, out fromType);
                var recipe = new MoveFaceRecipe64 { MoveType = data.MoveType, EndCondition = data.EndCondition,
                    Reverse = data.ReverseDirection, Distance = data.Distance, Angle = data.Angle, Offset = data.OffsetDistance,
                    Faces = faces.Cast<object>().Select(f => CaptureMoveReference64(context, f, feature.Name, "Faces")).ToArray(),
                    DirectionReference = CaptureMoveReference64(context, direction, feature.Name, "Direction"),
                    EndReference = CaptureMoveReference64(context, data.GetEndConditionEntity(), feature.Name, "End"),
                    FromReference = CaptureMoveReference64(context, from, feature.Name, "From") };
                if (recipe.MoveType == (int)swMoveFaceType_e.swMoveFaceTypeTranslate)
                {
                    if (direction != null) recipe.Direction = context.Reflection.ReflectVector(DirectionVector64(context, direction));
                    else
                    {
                        var translation = data.TriadTranslationParameters as double[];
                        if (recipe.EndCondition != (int)swEndConditions_e.swEndCondBlind || translation == null ||
                            translation.Length != 3 || translation.All(v => Math.Abs(v) < 1e-14))
                            throw new InvalidOperationException("MOVEFACE64 translated direction/triad unavailable.");
                        recipe.Translation = context.Reflection.ReflectVector(translation);
                    }
                    if (recipe.EndCondition != (int)swEndConditions_e.swEndCondBlind && recipe.EndReference == null)
                        throw new InvalidOperationException("MOVEFACE64 non-blind end reference unavailable.");
                }
                else if (recipe.MoveType == (int)swMoveFaceType_e.swMoveFaceTypeRotate)
                {
                    if (direction == null)
                        throw new InvalidOperationException("MOVEFACE64 triad-only Euler rotation requires a verified rotation adapter; guessing disabled.");
                    // Rotation axes are axial vectors: under reflection omega'=-M omega.
                    recipe.Direction = context.Reflection.ReflectVector(DirectionVector64(context, direction)).Select(v => -v).ToArray();
                }
                else if (recipe.MoveType != (int)swMoveFaceType_e.swMoveFaceTypeOffset)
                    throw new InvalidOperationException("MOVEFACE64 unsupported move type=" + recipe.MoveType);
                MirrorV7Diagnostics.Log("[MOVEFACE64][RECIPE_CAPTURED] feature=" + feature.Name + " type=" + recipe.MoveType +
                    " faces=" + recipe.Faces.Length + " suppressed=" + feature.IsSuppressed() + " retainedComFaces=False");
                return recipe;
            }
            finally { data.ReleaseSelectionAccess(); }
        }

        private static double[] DirectionVector64(MirrorInPlaceExecutionContextV7 context, object reference)
        {
            var face = reference as Face2;
            if (face != null)
            {
                var normal = face.Normal as double[];
                if (normal != null && normal.Length == 3 && normal.Sum(v => v * v) > .5) return normal;
            }
            var edge = reference as Edge;
            if (edge != null && ((Curve)edge.GetCurve()).IsLine())
            {
                var line = ((Curve)edge.GetCurve()).LineParams as double[];
                if (line != null && line.Length == 6) return line.Skip(3).ToArray();
            }
            var feature = reference as Feature;
            if (feature != null)
            {
                var axis = feature.GetSpecificFeature2() as RefAxis;
                if (axis != null)
                {
                    var p = axis.GetRefAxisParams() as double[];
                    if (p != null && p.Length == 6) return Enumerable.Range(0, 3).Select(i => p[i + 3] - p[i]).ToArray();
                }
                var plane = feature.GetSpecificFeature2() as RefPlane;
                if (plane != null)
                {
                    var math = (IMathUtility)context.SwApp.GetMathUtility();
                    var z = (MathVector)math.CreateVector(new[] { 0.0, 0.0, 1.0 });
                    return ((MathVector)z.MultiplyTransform(plane.Transform)).ArrayData as double[];
                }
            }
            throw new InvalidOperationException("MOVEFACE64 cannot establish native direction reference.");
        }

        private static Feature ResolveDependencyFeature64(MirrorInPlaceExecutionContextV7 context, int id)
        {
            var node = context.Dependencies64.Nodes[id];
            var live = ((PartDoc)context.WorkingDocument).FeatureByName(node.Name) as Feature;
            if (live == null || FeatureTypeHelperV7.GetEffectiveType(live) != node.Type)
                throw new InvalidOperationException("DEPENDENCY64 live source identity unavailable: id=" + id + " name=" + node.Name);
            return live;
        }

        // Called only after DeleteSelection2, before any CreateFeature or rebuild.
        // A loss must be in the parent's dependency closure AND have a complete
        // native recipe. Otherwise the caller undoes exactly the deletion.
        private static bool QueueDependencyLoss64(MirrorInPlaceExecutionContextV7 context, FeatureReplayCheckpointV7 parent,
            IList<Tuple<string, string>> retained, IList<Tuple<string, string>> remaining, out string reason)
        {
            reason = null;
            var journal = context.Dependencies64;
            if (journal == null) { reason = "dependency snapshot unavailable"; return false; }
            var lost = new List<int>();
            int cursor = 0;
            foreach (var entry in retained)
            {
                if (cursor < remaining.Count && entry.Equals(remaining[cursor])) { cursor++; continue; }
                var matches = journal.Nodes.Values.Where(n => n.Depth == 0 && n.Name == entry.Item1 && n.Type == entry.Item2).ToArray();
                if (matches.Length != 1) { reason = "lost identity missing/ambiguous: " + entry.Item1; return false; }
                lost.Add(matches[0].Id);
            }
            if (cursor != remaining.Count) { reason = "unexpected insertion/reordering"; return false; }
            try
            {
                int[] planned = DependencyScope64.ValidateLoss(journal.Nodes.Values, parent.Feature.TreeOrder, lost);
                if (planned.Any(id => !journal.Recipes.ContainsKey(id) || journal.Pending.Contains(id)))
                    throw new InvalidOperationException("dependent recipe missing or already pending");
                foreach (int id in planned) journal.Pending.Add(id);
                MirrorV7Diagnostics.Log("[DEPENDENCY64][DEFERRED] parentId=" + parent.Feature.TreeOrder +
                    " lostIds=" + string.Join(",", planned) + " restore=ORIGINAL_HISTORY_SLOT outputPublished=False");
                return true;
            }
            catch (InvalidOperationException ex) { reason = ex.Message; return false; }
        }

        internal static FeatureReplayCheckpointV7 RestoreDeferredFeature64(MirrorInPlaceExecutionContextV7 context, string name)
        {
            if (context == null || context.Dependencies64 == null) return null;
            var journal = context.Dependencies64;
            var candidates = journal.Nodes.Values.Where(n => n.Depth == 0 && n.Name == name && journal.Pending.Contains(n.Id)).ToArray();
            if (candidates.Length == 0) return null;
            if (candidates.Length != 1) throw new InvalidOperationException("DEPENDENCY64 ambiguous replay slot: " + name);
            var node = candidates[0];
            foreach (int parent in node.Parents)
            {
                if (journal.Pending.Contains(parent)) throw new InvalidOperationException("DEPENDENCY64 parent not restored before child.");
                ResolveDependencyFeature64(context, parent);
            }
            var predecessor = journal.Nodes.Values.Where(n => n.Depth == 0 && n.Id < node.Id)
                .OrderByDescending(n => n.Id).FirstOrDefault();
            if (predecessor == null) throw new InvalidOperationException("DEPENDENCY64 original predecessor unavailable.");
            MoveRollbackAfter(context.WorkingDocument, ResolveDependencyFeature64(context, predecessor.Id));
            context.WorkingDocument.EditRebuild3();
            var item = journal.Recipes[node.Id];
            CreateMoveFace64(context, item);
            journal.Pending.Remove(node.Id);
            MirrorV7Diagnostics.Log("[DEPENDENCY64][RESTORED] sourceId=" + node.Id + " feature=" + name +
                " slot=ORIGINAL native=True pending=" + journal.Pending.Count);
            return item;
        }

        private static void RestoreDeferredThrough64(MirrorInPlaceExecutionContextV7 context, int order)
        {
            if (context.Dependencies64 == null) return;
            foreach (int id in context.Dependencies64.Pending.Where(i => i <= order).OrderBy(i => i).ToArray())
                RestoreDeferredFeature64(context, context.Dependencies64.Nodes[id].Name);
        }

        internal static void AssertDependencyReplayComplete64(MirrorInPlaceExecutionContextV7 context)
        {
            if (context == null || context.Dependencies64 == null) return;
            if (context.Dependencies64.Pending.Count != 0)
                throw new InvalidOperationException("DEPENDENCY64 output blocked: pending native commands=" +
                    string.Join(",", context.Dependencies64.Pending.OrderBy(i => i)));
            foreach (var item in context.Dependencies64.Recipes.Values.Where(i => i.WasNativeReplacement))
            {
                var node = context.Dependencies64.Nodes[item.Feature.TreeOrder];
                var live = ResolveDependencyFeature64(context, node.Id);
                var configs = node.Suppression.Keys.ToArray();
                var states = live.IsSuppressed2((int)swInConfigurationOpts_e.swSpecifyConfiguration, configs) as Array;
                if (states == null || states.Length != configs.Length ||
                    configs.Where((c, i) => node.Suppression[c] != (bool)states.GetValue(i)).Any())
                    throw new InvalidOperationException("DEPENDENCY64 configuration suppression changed: " + node.Name);
                var expectedParents = new HashSet<string>(node.Parents.Select(id =>
                {
                    string replacementName;
                    return context.ReplacementTargetNames.TryGetValue(id, out replacementName) ? replacementName :
                        context.Dependencies64.Nodes[id].Name;
                }), StringComparer.Ordinal);
                var actualParents = new HashSet<string>((live.GetParents() as object[] ?? new object[0]).OfType<Feature>().Select(f => f.Name), StringComparer.Ordinal);
                if (!expectedParents.SetEquals(actualParents))
                    throw new InvalidOperationException("DEPENDENCY64 restored parent graph differs: " + node.Name +
                        " expected=" + string.Join("|", expectedParents) + " actual=" + string.Join("|", actualParents));
            }
            MirrorV7Diagnostics.Log("[DEPENDENCY64][COMPLETE] pending=0 configurationSuppression=PASS");
        }

        private static bool MoveFaceReverse64(MirrorInPlaceExecutionContextV7 context, MoveFaceRecipe64 recipe, object direction)
        {
            if (recipe.Direction == null) return recipe.Reverse;
            var actual = DirectionVector64(context, direction);
            double norm = Math.Sqrt(actual.Sum(v => v * v) * recipe.Direction.Sum(v => v * v));
            if (norm < 1e-15) throw new InvalidOperationException("MOVEFACE64 direction has zero length.");
            double dot = Enumerable.Range(0, 3).Sum(i => actual[i] * recipe.Direction[i]) / norm;
            if (Math.Abs(dot) < .999999) throw new InvalidOperationException("MOVEFACE64 direction is not the reflected source vector.");
            return recipe.Reverse ^ (dot < 0);
        }

        private static void SelectMoveReference64(ModelDoc2 model, object value, int mark)
        {
            bool selected = false;
            var feature = value as Feature;
            if (feature != null) selected = feature.Select2(true, mark);
            else
            {
                var entity = value as Entity;
                if (entity != null)
                {
                    var selection = (SelectData)((SelectionMgr)model.SelectionManager).CreateSelectData();
                    selection.Mark = mark; selected = entity.Select4(true, selection);
                }
            }
            if (!selected) throw new InvalidOperationException("MOVEFACE64 selection failed for mark=" + mark);
        }

        private static void RestoreMoveSuppression64(MirrorInPlaceExecutionContextV7 context, FeatureReplayCheckpointV7 item)
        {
            var states = context.Dependencies64.Nodes[item.Feature.TreeOrder].Suppression;
            foreach (var group in states.GroupBy(p => p.Value))
                if (!item.Feature.Feature.SetSuppression2((int)(group.Key ? swFeatureSuppressionAction_e.swSuppressFeature :
                    swFeatureSuppressionAction_e.swUnSuppressFeature), (int)swInConfigurationOpts_e.swSpecifyConfiguration,
                    group.Select(p => p.Key).ToArray()))
                    throw new InvalidOperationException("MOVEFACE64 cannot restore source configuration suppression.");
        }

        private static void CreateMoveFace64(MirrorInPlaceExecutionContextV7 context, FeatureReplayCheckpointV7 item)
        {
            var model = context.WorkingDocument;
            var recipe = item.MoveFace64;
            if (recipe == null) throw new InvalidOperationException("MOVEFACE64 complete native recipe required.");
            model.ClearSelection2(true);
            try
            {
                // Fresh numeric mapping at the child's BEFORE checkpoint. No COM
                // face pointer from capture/deletion is reused here.
                foreach (var face in recipe.Faces) SelectMoveReference64(model, face(model), 1);
                object direction = recipe.DirectionReference == null ? null : recipe.DirectionReference(model);
                if (direction != null) SelectMoveReference64(model, direction,
                    recipe.MoveType == (int)swMoveFaceType_e.swMoveFaceTypeRotate ? 4 : 2);
                if (recipe.EndReference != null) SelectMoveReference64(model, recipe.EndReference(model), 8);
                bool reverse = MoveFaceReverse64(context, recipe, direction);
                var created = model.FeatureManager.InsertMoveFace3(recipe.MoveType, reverse, recipe.Angle,
                    recipe.Translation == null ? recipe.Distance : 0, recipe.Translation, null, recipe.EndCondition, recipe.Offset);
                if (created == null) throw new InvalidOperationException("MOVEFACE64 native InsertMoveFace3 returned null.");
                created.Name = item.Feature.Name;
                if (created.Name != item.Feature.Name || created.GetTypeName2() != item.Feature.TypeName)
                    throw new InvalidOperationException("MOVEFACE64 recreated native identity changed.");
                item.Feature.Feature = created;
                if (recipe.FromReference != null)
                {
                    var data = (IMoveFaceFeatureData)created.GetDefinition();
                    bool accessed = data.AccessSelections(model, null);
                    if (!accessed) throw new InvalidOperationException("MOVEFACE64 cannot access recreated from-reference.");
                    try
                    {
                        data.SetFromEntity(recipe.FromReference(model));
                        if (!created.ModifyDefinition(data, model, null)) throw new InvalidOperationException("MOVEFACE64 from-reference modify failed.");
                        accessed = false;
                    }
                    finally { if (accessed) data.ReleaseSelectionAccess(); }
                }
                RestoreMoveSuppression64(context, item);
                MoveRollbackAfter(model, created);
                model.EditRebuild3();
                if (!item.Feature.IsSuppressed) EnsureFeatureHasNoError(created, "MOVEFACE64 created feature");
                var read = (IMoveFaceFeatureData)created.GetDefinition();
                if (read.GetFacesCount() != recipe.Faces.Length || read.MoveType != recipe.MoveType || (recipe.MoveType == (int)swMoveFaceType_e.swMoveFaceTypeTranslate && read.EndCondition != recipe.EndCondition) ||
                    (recipe.Translation == null && !SameScalarOption63(read.Distance, recipe.Distance)) ||
                    !SameScalarOption63(read.Angle, recipe.Angle) || read.ReverseDirection != reverse ||
                    (recipe.EndCondition == (int)swEndConditions_e.swEndCondOffsetFromSurface && !SameScalarOption63(read.OffsetDistance, recipe.Offset)))
                    throw new InvalidOperationException("MOVEFACE64 native option readback mismatch.");
                BaseSketchMutationEngineV7.VerifyCurrentSolids(context, item.ReflectedBodyOracle);
                item.WasNativeReplacement = true;
                item.Result = new MirrorV7FeatureResult { FeatureName = item.Feature.Name, FeatureType = item.Feature.TypeName,
                    Status = MirrorV7ReplayStatus.ExactReplay, Message = "MOVEFACE64 native recovery at original history slot; options/suppression/checkpoint PASS." };
            }
            finally { model.ClearSelection2(true); }
        }

        private static void RebindMoveFace64(MirrorInPlaceExecutionContextV7 context, FeatureReplayCheckpointV7 item)
        {
            var model = context.WorkingDocument;
            var recipe = item.MoveFace64;
            var feature = item.Feature.Feature;
            var data = (IMoveFaceFeatureData)feature.GetDefinition();
            bool accessed = data.AccessSelections(model, null);
            if (!accessed) throw new InvalidOperationException("MOVEFACE64 cannot access native selections.");
            try
            {
                data.Faces = recipe.Faces.Select(f => (Face2)f(model)).ToArray();
                object direction = recipe.DirectionReference == null ? null : recipe.DirectionReference(model);
                if (direction != null && !data.SetDirectionReference(direction))
                    throw new InvalidOperationException("MOVEFACE64 direction reference rejected.");
                data.ReverseDirection = MoveFaceReverse64(context, recipe, direction);
                if (recipe.Translation != null) data.TriadTranslationParameters = recipe.Translation;
                if (recipe.EndReference != null) data.SetEndConditionEntity(recipe.EndReference(model));
                if (recipe.FromReference != null) data.SetFromEntity(recipe.FromReference(model));
                if (!feature.ModifyDefinition(data, model, null)) throw new InvalidOperationException("MOVEFACE64 ModifyDefinition failed.");
                accessed = false;
                MoveRollbackAfter(model, feature); model.EditRebuild3();
                EnsureFeatureHasNoError(feature, "MOVEFACE64 native rebound feature");
                BaseSketchMutationEngineV7.VerifyCurrentSolids(context, item.ReflectedBodyOracle);
                item.Result = new MirrorV7FeatureResult { FeatureName = item.Feature.Name, FeatureType = item.Feature.TypeName,
                    Status = MirrorV7ReplayStatus.ExactReplay, Message = "MOVEFACE64 typed reference rebind; reflected checkpoint PASS." };
            }
            finally { if (accessed) data.ReleaseSelectionAccess(); }
        }
    }
}
