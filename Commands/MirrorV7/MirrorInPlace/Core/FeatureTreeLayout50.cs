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
            IDictionary<int, string> replacementTargetNames = null)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            if (replacementTargetNames != null)
                foreach (var entry in replacementTargetNames)
                {
                    var original = source.Nodes.Single(n => n.TreeOrder == entry.Key);
                    MirrorV7FeatureNode output;
                    if (original.Role != MirrorV7FeatureRole.Sketch || string.IsNullOrWhiteSpace(entry.Value) ||
                        !mapped.TryGetValue(entry.Key, out output) || output.Role != MirrorV7FeatureRole.Sketch ||
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
                if (owner.TypeName != "EdgeFlange" || outputOwner.TypeName != owner.TypeName ||
                    owner.Depth != outputOwner.Depth || owner.IsSuppressed != outputOwner.IsSuppressed ||
                    outputOwner.TreeOrder != owner.TreeOrder + profiles.Length ||
                    !SameNames(owner.ParentFeatureNames, outputOwner.ParentFeatureNames, names))
                    throw new InvalidOperationException("TREE50 flange layout/parents differ: " + owner.Name);
                var targetSlots = new HashSet<int>();
                for (int i = 0; i < profiles.Length; i++)
                {
                    var original = source.Nodes.Single(n => n.TreeOrder == profiles[i]);
                    MirrorV7FeatureNode output;
                    if (!mapped.TryGetValue(profiles[i], out output) ||
                        original.TreeOrder != owner.TreeOrder + i + 1 ||
                        original.Role != MirrorV7FeatureRole.Sketch || output.Role != MirrorV7FeatureRole.Sketch ||
                        original.TypeName != output.TypeName || TargetName(original.Name, names) != output.Name ||
                        original.IsSuppressed != output.IsSuppressed ||
                        original.Depth != owner.Depth + 1 || output.Depth != owner.Depth ||
                        output.TreeOrder < owner.TreeOrder || output.TreeOrder >= outputOwner.TreeOrder ||
                        !targetSlots.Add(output.TreeOrder) ||
                        !original.ChildFeatureNames.Contains(owner.Name) ||
                        !output.ChildFeatureNames.Contains(outputOwner.Name) ||
                        !outputOwner.ParentFeatureNames.Contains(output.Name) ||
                        !SameNames(original.ParentFeatureNames, output.ParentFeatureNames, names) ||
                        !SameNames(original.ChildFeatureNames, output.ChildFeatureNames, names))
                        throw new InvalidOperationException("TREE50 replacement profile layout/dependencies differ: " + original.Name);
                    allowed.Add(original.TreeOrder);
                }
                allowed.Add(owner.TreeOrder);
            }
            foreach (var original in source.Nodes.Where(n => n.Role != MirrorV7FeatureRole.System))
            {
                MirrorV7FeatureNode output;
                if (!mapped.TryGetValue(original.TreeOrder, out output))
                    throw new InvalidOperationException("TREE50 missing mapping: " + original.Name);
                if (allowed.Contains(original.TreeOrder)) continue;
                if (original.TreeOrder == output.TreeOrder && original.Depth == output.Depth) continue;
                if (GeneratedBendCrossedSystemFolder(source, target, mapped, original, output, names)) continue;
                throw new InvalidOperationException("TREE50 unexpected layout change: " + original.Name +
                    " sourceOrder=" + original.TreeOrder + " targetOrder=" + output.TreeOrder +
                    " sourceDepth=" + original.Depth + " targetDepth=" + output.Depth);
            }
        }

        private static bool GeneratedBendCrossedSystemFolder(MirrorV7ModelGraph source,
            MirrorV7ModelGraph target, IDictionary<int, MirrorV7FeatureNode> mapped,
            MirrorV7FeatureNode original, MirrorV7FeatureNode output, IDictionary<string, string> names)
        {
            if (original.TypeName != "UiBend" || output.TypeName != "UiBend" ||
                original.Depth == 0 || original.Depth != output.Depth || original.Name != output.Name ||
                !SameNames(original.ParentFeatureNames, output.ParentFeatureNames, names)) return false;
            var owner = source.Nodes.Take(original.TreeOrder).LastOrDefault(n => n.Depth < original.Depth);
            var outputOwner = target.Nodes.Take(output.TreeOrder).LastOrDefault(n => n.Depth < output.Depth);
            MirrorV7FeatureNode resolvedOwner;
            if (owner == null || outputOwner == null || owner.TypeName != "FlatPattern" ||
                outputOwner.TypeName != owner.TypeName || !mapped.TryGetValue(owner.TreeOrder, out resolvedOwner) ||
                resolvedOwner.TreeOrder != outputOwner.TreeOrder) return false;
            var siblings = Descendants(source, owner).Where(n => n.Role != MirrorV7FeatureRole.System).ToList();
            var outputSiblings = Descendants(target, outputOwner).Where(n => n.Role != MirrorV7FeatureRole.System).ToList();
            if (siblings.Count != outputSiblings.Count) return false;
            for (int i = 0; i < siblings.Count; i++)
            {
                MirrorV7FeatureNode sibling;
                if (!mapped.TryGetValue(siblings[i].TreeOrder, out sibling) ||
                    sibling.TreeOrder != outputSiblings[i].TreeOrder || sibling.Depth != siblings[i].Depth)
                    return false;
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
