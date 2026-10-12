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

    // A suppressed command is not evidence that its native recipe can be
    // regenerated in the current configuration. Choose only a configuration
    // in which the SOURCE command was active and its checkpoint was captured.
    // Names are opaque configuration identities, not "flat"/Part-name rules.
    internal static class DependencyConfiguration70
    {
        internal static string[] ActiveSourceConfigurations(IDictionary<string, bool> suppression)
        {
            if (suppression == null || suppression.Count == 0 || suppression.Keys.Any(string.IsNullOrEmpty))
                throw new InvalidOperationException("DEPENDENCY70 source configuration mask unavailable.");
            return suppression.Where(p => !p.Value).Select(p => p.Key).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }

        internal static string SelectReconstruction(IDictionary<string, bool> suppression,
            IEnumerable<string> verified, string current)
        {
            var active = ActiveSourceConfigurations(suppression);
            var captured = new HashSet<string>(verified ?? new string[0], StringComparer.Ordinal);
            if (string.IsNullOrEmpty(current) || !suppression.ContainsKey(current))
                throw new InvalidOperationException("DEPENDENCY70 current configuration not in source snapshot.");
            if (active.Length == 0 || active.Any(c => !captured.Contains(c)))
                throw new InvalidOperationException("DEPENDENCY70 no complete source-active reconstruction evidence; native insertion disabled.");
            return active.Contains(current, StringComparer.Ordinal) ? current : active[0];
        }
    }

    internal static class DependencyHistory70
    {
        internal static bool IsOriginalSourceOrder(IEnumerable<int> survivingSourceOrder, IEnumerable<int> actualSourceOrder)
        {
            if (survivingSourceOrder == null || actualSourceOrder == null)
                throw new InvalidOperationException("DEPENDENCY70 native history snapshot unavailable.");
            var expected = survivingSourceOrder.ToArray();
            var actual = actualSourceOrder.ToArray();
            if (expected.Length == 0 || expected.Distinct().Count() != expected.Length ||
                actual.Length != expected.Length || actual.Distinct().Count() != actual.Length ||
                !new HashSet<int>(expected).SetEquals(actual))
                throw new InvalidOperationException("DEPENDENCY70 native history identities missing/duplicated/unexpected.");
            return expected.SequenceEqual(actual);
        }
    }
}
