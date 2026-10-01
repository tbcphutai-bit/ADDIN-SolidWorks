using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    // Pure policy: IDs are frozen source graph orders, never Part names, edge
    // indexes or localized feature labels. Native adapters supply capabilities.
    internal sealed class DependencyNode64
    {
        internal int Id, Depth;
        internal string Name, Type, CaptureError;
        internal int[] Parents;
        internal bool Recoverable;
        internal Dictionary<string, bool> Suppression;
    }

    internal static class DependencyScope64
    {
        internal static HashSet<int> Descendants(IEnumerable<DependencyNode64> nodes, int root)
        {
            var all = nodes.ToArray();
            if (all.Count(n => n.Id == root) != 1 || all.Select(n => n.Id).Distinct().Count() != all.Length)
                throw new InvalidOperationException("DEPENDENCY64 source identity is not unique.");
            var affected = new HashSet<int> { root };
            bool changed;
            do
            {
                changed = false;
                foreach (var node in all)
                    if (!affected.Contains(node.Id) && node.Parents.Any(affected.Contains))
                    { affected.Add(node.Id); changed = true; }
            } while (changed);
            affected.Remove(root);
            return affected;
        }

        internal static int[] ValidateLoss(IEnumerable<DependencyNode64> nodes, int root, IEnumerable<int> lost)
        {
            var all = nodes.ToDictionary(n => n.Id);
            var descendants = Descendants(all.Values, root);
            var missing = lost.ToArray();
            if (missing.Distinct().Count() != missing.Length)
                throw new InvalidOperationException("DEPENDENCY64 duplicate lost identity.");
            foreach (int id in missing)
            {
                DependencyNode64 node;
                if (!all.TryGetValue(id, out node) || !descendants.Contains(id) || id <= root ||
                    node.Depth != 0 || !node.Recoverable || node.CaptureError != null)
                    throw new InvalidOperationException("DEPENDENCY64 unplanned/unrecoverable deletion: sourceId=" + id);
            }
            return missing.OrderBy(id => id).ToArray();
        }
    }
}
