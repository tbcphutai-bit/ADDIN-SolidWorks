using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    // Pure layout policy. Identity resolution, one-to-one coverage, suppression,
    // feature errors and body geometry are checked separately by the caller.
    public static class FeatureTreeLayout50
    {
        public static void Verify(MirrorV7ModelGraph source, MirrorV7ModelGraph target,
            IDictionary<int, MirrorV7FeatureNode> mapped, IDictionary<int, int> externalProfiles,
            IDictionary<int, string> replacementTargetNames = null,
            IDictionary<int, int> verifiedNativeProfileOwners70 = null,
            IDictionary<int, string[]> requiredNativeFlangeParents70 = null)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            if (replacementTargetNames != null)
                foreach (var entry in replacementTargetNames)
                {
                    var original = source.Nodes.Single(n => n.TreeOrder == entry.Key);
                    MirrorV7FeatureNode output;
                    if (!mapped.TryGetValue(entry.Key, out output))
                        throw new InvalidOperationException("TREE70 renamed source identity is unmapped.");
                    bool profile = original.Role == MirrorV7FeatureRole.Sketch && output.Role == MirrorV7FeatureRole.Sketch;
                    bool flatBend = QualifiedFlatBendName70(source, target, mapped, original, output);
                    if ((!profile && !flatBend) || string.IsNullOrWhiteSpace(entry.Value) ||
                        original.TypeName != output.TypeName || output.Name != entry.Value ||
                        names.ContainsKey(original.Name) || names.Values.Contains(entry.Value) ||
                        target.Nodes.Count(n => n.Name == entry.Value) != 1)
                        throw new InvalidOperationException("TREE51 invalid profile name mapping: " + original.Name);
                    names.Add(original.Name, entry.Value);
                }
            var allowed = new HashSet<int>();
            foreach (var group in externalProfiles.GroupBy(p => p.Value))
            {
                var owner = source.Nodes.Single(n => n.TreeOrder == group.Key);
                MirrorV7FeatureNode outputOwner;
                if (!mapped.TryGetValue(owner.TreeOrder, out outputOwner))
                    throw new InvalidOperationException("TREE50 flange owner is unmapped.");
                int[] profiles = group.Select(p => p.Key).OrderBy(x => x).ToArray();
                var discardedConstraints70 = new HashSet<string>(StringComparer.Ordinal);
                if (owner.TypeName != "EdgeFlange" || outputOwner.TypeName != owner.TypeName ||
                    owner.Depth != outputOwner.Depth || owner.IsSuppressed != outputOwner.IsSuppressed ||
                    outputOwner.TreeOrder != owner.TreeOrder + profiles.Length ||
                    !SameFlangeParents70(source, owner, outputOwner, profiles, names,
                        verifiedNativeProfileOwners70, requiredNativeFlangeParents70, discardedConstraints70))
                    throw new InvalidOperationException("TREE50 flange layout/parents differ: " + owner.Name);
                var targetSlots = new HashSet<int>();
                for (int i = 0; i < profiles.Length; i++)
                {
                    var original = source.Nodes.Single(n => n.TreeOrder == profiles[i]);
                    MirrorV7FeatureNode output;
                    int verifiedOwner70;
                    bool nativeOwner70 = verifiedNativeProfileOwners70 != null &&
                        verifiedNativeProfileOwners70.TryGetValue(profiles[i], out verifiedOwner70) && verifiedOwner70 == owner.TreeOrder;
                    if (!mapped.TryGetValue(profiles[i], out output) ||
                        original.TreeOrder != owner.TreeOrder + i + 1 ||
                        original.Role != MirrorV7FeatureRole.Sketch || output.Role != MirrorV7FeatureRole.Sketch ||
                        original.TypeName != output.TypeName || TargetName(original.Name, names) != output.Name ||
                        original.IsSuppressed != output.IsSuppressed ||
                        original.Depth != owner.Depth + 1 || output.Depth != owner.Depth ||
                        output.TreeOrder < owner.TreeOrder || output.TreeOrder >= outputOwner.TreeOrder ||
                        !targetSlots.Add(output.TreeOrder) ||
                        (!nativeOwner70 && (!original.ChildFeatureNames.Contains(owner.Name) ||
                            !output.ChildFeatureNames.Contains(outputOwner.Name) ||
                            !outputOwner.ParentFeatureNames.Contains(output.Name))) ||
                        !SameOrOwnerRoutedParents70(original.ParentFeatureNames, output.ParentFeatureNames,
                            outputOwner.ParentFeatureNames, names, nativeOwner70, discardedConstraints70) ||
                        !SameNames(original.ChildFeatureNames, output.ChildFeatureNames, names))
                        throw new InvalidOperationException("TREE50 replacement profile layout/dependencies differ: " + original.Name);
                    allowed.Add(original.TreeOrder);
                }
                allowed.Add(owner.TreeOrder);
            }
            var sourceCache = FlatSketchCacheNodes71(source);
            var targetCache = FlatSketchCacheNodes71(target);
            foreach (var original in source.Nodes.Where(n => n.Role != MirrorV7FeatureRole.System))
            {
                MirrorV7FeatureNode output;
                if (!mapped.TryGetValue(original.TreeOrder, out output))
                {
                    // Only a regenerated flat-sketch cache entry may be absent.
                    if (sourceCache.Contains(original.TreeOrder)) continue;
                    throw new InvalidOperationException("TREE50 missing mapping: " + original.Name);
                }
                if (sourceCache.Contains(original.TreeOrder) != targetCache.Contains(output.TreeOrder))
                    throw new InvalidOperationException("TREE71 flat-sketch cache entry mapped to a design feature: " + original.Name);
                if (allowed.Contains(original.TreeOrder)) continue;
                if (HistorySlot71(original, sourceCache) == HistorySlot71(output, targetCache) && original.Depth == output.Depth) continue;
                if (GeneratedBendCrossedSystemFolder(source, target, mapped, original, output, names, sourceCache, targetCache)) continue;
                throw new InvalidOperationException("TREE50 unexpected layout change: " + original.Name +
                    " sourceOrder=" + original.TreeOrder + " targetOrder=" + output.TreeOrder +
                    " sourceDepth=" + original.Depth + " targetDepth=" + output.Depth);
            }
        }

        // Sketches inside a FlatPattern's system FoldUpDownSketchFolder are a
        // cache that SOLIDWORKS regenerates when the pattern is flattened: a
        // plain flatten/refold of an untouched source Part drops stale entries
        // (native probe FlatChildrenProbe71). They are not design history.
        // Qualified structurally: sketch, no consumers, no parent other than
        // the owning FlatPattern.
        public static HashSet<int> FlatSketchCacheNodes71(MirrorV7ModelGraph graph)
        {
            var result = new HashSet<int>();
            foreach (var node in graph.Nodes)
            {
                // Also outside the source's design history: the fixed reflected
                // surface this run inserted in front of an in-context Offset
                // Surface (user decision A1). Exactly those nodes, by the names
                // the insertion returned; they exist on the output side only.
                if (node.Depth == 0 && node.TypeName == "RefSurface" &&
                    RollbackReplayEngineV7.FrozenSurfaceImports71.Contains(node.Name))
                {
                    result.Add(node.TreeOrder);
                    continue;
                }
                if (node.Role != MirrorV7FeatureRole.Sketch || node.TypeName != "ProfileFeature" ||
                    node.ChildFeatureNames.Count != 0) continue;
                var folder = Owner(graph, node);
                var flat = folder == null ? null : Owner(graph, folder);
                if (folder == null || flat == null || folder.TypeName != "FoldUpDownSketchFolder" ||
                    folder.Role != MirrorV7FeatureRole.System || flat.TypeName != "FlatPattern" ||
                    node.Depth != folder.Depth + 1 || folder.Depth != flat.Depth + 1 ||
                    node.ParentFeatureNames.Any(n => n != flat.Name)) continue;
                result.Add(node.TreeOrder);
            }
            return result;
        }

        // Tree position with regenerated cache entries removed, so a dropped
        // cache sketch does not shift every later feature's history slot.
        private static int HistorySlot71(MirrorV7FeatureNode node, HashSet<int> cache)
        {
            return node.TreeOrder - cache.Count(order => order < node.TreeOrder);
        }

        // Native bend sub-features observed (captured source/target graphs) to
        // be the single parent of a FlatPattern-generated UiBend: OneBend under
        // an EdgeFlange and SketchBend under a base flange. This is an explicit
        // list, not a "*Bend*" wildcard; any other producer type keeps failing
        // until a captured native graph proves the same one-to-one relation.
        private static readonly string[] FlatBendProducerTypes71 = { "OneBend", "SketchBend" };

        private static bool QualifiedFlatBendName70(MirrorV7ModelGraph source, MirrorV7ModelGraph target,
            IDictionary<int, MirrorV7FeatureNode> mapped, MirrorV7FeatureNode original, MirrorV7FeatureNode output)
        {
            return QualifiedFlatBendProducer71(source, target, mapped, original, output) != null;
        }

        // Returns the mapped TARGET bend producer of a generated flat bend, or
        // null when identity is not proven: same mapped FlatPattern owner, one
        // producer on each side, producer mapped with equal type/role/depth/
        // suppression and the same mapped owning feature.
        private static MirrorV7FeatureNode QualifiedFlatBendProducer71(MirrorV7ModelGraph source, MirrorV7ModelGraph target,
            IDictionary<int, MirrorV7FeatureNode> mapped, MirrorV7FeatureNode original, MirrorV7FeatureNode output)
        {
            if (original.TypeName != "UiBend" || output.TypeName != "UiBend" ||
                original.Role != MirrorV7FeatureRole.SheetMetal || output.Role != original.Role ||
                original.Depth == 0 || output.Depth != original.Depth || output.IsSuppressed != original.IsSuppressed)
                return null;
            var owner = Owner(source, original);
            var targetOwner = Owner(target, output);
            MirrorV7FeatureNode resolvedOwner;
            if (owner == null || targetOwner == null || owner.TypeName != "FlatPattern" ||
                targetOwner.TypeName != "FlatPattern" || !mapped.TryGetValue(owner.TreeOrder, out resolvedOwner) ||
                resolvedOwner.TreeOrder != targetOwner.TreeOrder) return null;
            var bendParents = source.Nodes.Where(n => FlatBendProducerTypes71.Contains(n.TypeName) &&
                original.ParentFeatureNames.Contains(n.Name)).ToArray();
            MirrorV7FeatureNode resolvedBend;
            if (bendParents.Length != 1 || !mapped.TryGetValue(bendParents[0].TreeOrder, out resolvedBend) ||
                resolvedBend.TypeName != bendParents[0].TypeName || resolvedBend.Role != bendParents[0].Role ||
                resolvedBend.Depth != bendParents[0].Depth || resolvedBend.IsSuppressed != bendParents[0].IsSuppressed ||
                !output.ParentFeatureNames.Contains(resolvedBend.Name) ||
                original.ParentFeatureNames.Count != output.ParentFeatureNames.Count ||
                target.Nodes.Count(n => FlatBendProducerTypes71.Contains(n.TypeName) &&
                    output.ParentFeatureNames.Contains(n.Name)) != 1) return null;
            var bendOwner = Owner(source, bendParents[0]);
            var targetBendOwner = Owner(target, resolvedBend);
            if ((bendOwner == null) != (targetBendOwner == null)) return null;
            MirrorV7FeatureNode resolvedBendOwner;
            if (bendOwner != null && (!mapped.TryGetValue(bendOwner.TreeOrder, out resolvedBendOwner) ||
                resolvedBendOwner.TreeOrder != targetBendOwner.TreeOrder ||
                resolvedBendOwner.TypeName != bendOwner.TypeName)) return null;
            return resolvedBend;
        }

        private static MirrorV7FeatureNode Owner(MirrorV7ModelGraph graph, MirrorV7FeatureNode node)
        {
            return graph.Nodes.Take(node.TreeOrder).LastOrDefault(n => n.Depth < node.Depth);
        }

        private static bool SameOrOwnerRoutedParents70(IEnumerable<string> sourceParents,
            IEnumerable<string> targetParents, IEnumerable<string> targetOwnerParents,
            IDictionary<string, string> names, bool nativeOwnerVerified, ISet<string> discardedConstraints)
        {
            var expected = new HashSet<string>(sourceParents.Select(n => TargetName(n, names)), StringComparer.Ordinal);
            var actual = new HashSet<string>(targetParents, StringComparer.Ordinal);
            if (expected.SetEquals(actual)) return true;
            // Rebuilt free-coordinate profiles do not retain the original
            // sketch's external constraints. A missing profile parent is safe
            // only when the SAME flange still depends on it, or the source
            // adapter qualified it as a non-hinge sketch constraint. New or
            // lost mandatory native hinge dependencies still fail.
            return nativeOwnerVerified && actual.IsSubsetOf(expected) &&
                expected.Except(actual).All(n => new HashSet<string>(targetOwnerParents, StringComparer.Ordinal).Contains(n) ||
                    discardedConstraints.Contains(n));
        }

        private static bool SameFlangeParents70(MirrorV7ModelGraph source, MirrorV7FeatureNode owner,
            MirrorV7FeatureNode outputOwner, int[] profiles, IDictionary<string, string> names,
            IDictionary<int, int> nativeOwners, IDictionary<int, string[]> nativeHinges, ISet<string> discarded)
        {
            var expected = new HashSet<string>(owner.ParentFeatureNames.Select(n => TargetName(n, names)), StringComparer.Ordinal);
            var actual = new HashSet<string>(outputOwner.ParentFeatureNames, StringComparer.Ordinal);
            string[] required = null;
            bool qualified = nativeHinges != null && nativeHinges.TryGetValue(owner.TreeOrder, out required);
            if (qualified && (required == null || required.Length == 0 || required.Any(n => !actual.Contains(n)))) return false;
            if (expected.SetEquals(actual)) return true;
            if (!qualified || !actual.IsSubsetOf(expected) || nativeOwners == null || profiles.Any(id =>
            {
                int parent; return !nativeOwners.TryGetValue(id, out parent) || parent != owner.TreeOrder;
            })) return false;
            var sketchParents = new HashSet<string>(profiles.SelectMany(id => source.Nodes.Single(n => n.TreeOrder == id)
                .ParentFeatureNames).Select(n => TargetName(n, names)), StringComparer.Ordinal);
            var removed = expected.Except(actual).ToArray();
            // Removal is limited to old SKETCH dependencies of a qualified
            // blind native flange. The reflected hinge's captured mandatory
            // face owners must remain. Surface/vertex termination adapters
            // cannot obtain this permission from labels or empty parent lists.
            if (removed.Any(n => !sketchParents.Contains(n) || required.Contains(n))) return false;
            foreach (string n in removed) discarded.Add(n);
            return true;
        }

        private static bool GeneratedBendCrossedSystemFolder(MirrorV7ModelGraph source,
            MirrorV7ModelGraph target, IDictionary<int, MirrorV7FeatureNode> mapped,
            MirrorV7FeatureNode original, MirrorV7FeatureNode output, IDictionary<string, string> names,
            HashSet<int> sourceCache, HashSet<int> targetCache)
        {
            if (original.TypeName != "UiBend" || output.TypeName != "UiBend" ||
                original.Depth == 0 || original.Depth != output.Depth || TargetName(original.Name, names) != output.Name ||
                !SameNames(original.ParentFeatureNames, output.ParentFeatureNames, names) ||
                !QualifiedFlatBendName70(source, target, mapped, original, output)) return false;
            var owner = source.Nodes.Take(original.TreeOrder).LastOrDefault(n => n.Depth < original.Depth);
            var outputOwner = target.Nodes.Take(output.TreeOrder).LastOrDefault(n => n.Depth < output.Depth);
            MirrorV7FeatureNode resolvedOwner;
            if (owner == null || outputOwner == null || owner.TypeName != "FlatPattern" ||
                outputOwner.TypeName != owner.TypeName || !mapped.TryGetValue(owner.TreeOrder, out resolvedOwner) ||
                resolvedOwner.TreeOrder != outputOwner.TreeOrder) return false;
            var siblings = Descendants(source, owner).Where(n => n.Role != MirrorV7FeatureRole.System &&
                !sourceCache.Contains(n.TreeOrder)).ToList();
            var outputSiblings = Descendants(target, outputOwner).Where(n => n.Role != MirrorV7FeatureRole.System &&
                !targetCache.Contains(n.TreeOrder)).ToList();
            if (siblings.Count != outputSiblings.Count) return false;
            var usedSlots = new HashSet<int>();
            var usedProducers = new HashSet<int>();
            for (int i = 0; i < siblings.Count; i++)
            {
                MirrorV7FeatureNode sibling;
                if (!mapped.TryGetValue(siblings[i].TreeOrder, out sibling) ||
                    !outputSiblings.Any(n => n.TreeOrder == sibling.TreeOrder) || sibling.Depth != siblings[i].Depth ||
                    !usedSlots.Add(sibling.TreeOrder)) return false;
                if (siblings[i].TypeName == "UiBend")
                {
                    // FlatPattern's regenerated UiBend display order is not
                    // native design-history order. Qualify every generated
                    // sibling by its unique mapped bend producer and the SAME
                    // FlatPattern owner; user-created siblings cannot move.
                    if (TargetName(siblings[i].Name, names) != sibling.Name || siblings[i].IsSuppressed != sibling.IsSuppressed ||
                        !SameNames(siblings[i].ParentFeatureNames, sibling.ParentFeatureNames, names)) return false;
                    // One generated flat bend per producer, counted over ALL
                    // siblings: a permutation must not hide a lost or doubled
                    // bend. Only an unmoved, unrenamed sibling may lack one.
                    var producer = QualifiedFlatBendProducer71(source, target, mapped, siblings[i], sibling);
                    if (producer == null
                        ? siblings[i].TreeOrder != sibling.TreeOrder || siblings[i].Name != sibling.Name
                        : !usedProducers.Add(producer.TreeOrder)) return false;
                }
                else if (HistorySlot71(siblings[i], sourceCache) != HistorySlot71(sibling, targetCache)) return false;
            }
            return true;
        }

        private static IEnumerable<MirrorV7FeatureNode> Descendants(MirrorV7ModelGraph graph, MirrorV7FeatureNode owner)
        {
            return graph.Nodes.Skip(owner.TreeOrder + 1).TakeWhile(n => n.Depth > owner.Depth);
        }

        private static string TargetName(string sourceName, IDictionary<string, string> names)
        {
            string targetName;
            return names.TryGetValue(sourceName, out targetName) ? targetName : sourceName;
        }

        private static bool SameNames(IEnumerable<string> a, IEnumerable<string> b,
            IDictionary<string, string> names)
        {
            return new HashSet<string>(a.Select(n => TargetName(n, names)), StringComparer.Ordinal).SetEquals(b);
        }
    }
}
