using System;
using System.Collections.Generic;

namespace ADDIN.Commands
{
    internal static class BomOccurrenceCounter
    {
        internal sealed class Total<T>
        {
            internal T Component;
            internal int Count;
        }

        internal static List<Total<T>> Count<T>(IEnumerable<T> roots,
            Func<T, IEnumerable<T>> children, Func<T, bool> included,
            Func<T, bool> seed, Func<T, string> key, Func<bool> cancelled)
        {
            var totals = new List<Total<T>>();
            var byKey = new Dictionary<string, Total<T>>(StringComparer.OrdinalIgnoreCase);
            Walk(roots, false, children, included, seed, key, cancelled, totals, byKey);
            return totals;
        }

        private static void Walk<T>(IEnumerable<T> nodes, bool insideSeed,
            Func<T, IEnumerable<T>> children, Func<T, bool> included,
            Func<T, bool> seed, Func<T, string> key, Func<bool> cancelled,
            List<Total<T>> totals, Dictionary<string, Total<T>> byKey)
        {
            if (nodes == null) return;
            foreach (T node in nodes)
            {
                if (cancelled()) throw new OperationCanceledException();
                if (!included(node)) continue;
                string identity = insideSeed ? key(node) : null;
                if (!string.IsNullOrEmpty(identity))
                {
                    Total<T> total;
                    if (!byKey.TryGetValue(identity, out total))
                    {
                        total = new Total<T> { Component = node };
                        byKey.Add(identity, total);
                        totals.Add(total);
                    }
                    total.Count = checked(total.Count + 1);
                }
                // Visit every occurrence. A global visited-file set would lose
                // repeated parents and all repeated descendants of those parents.
                Walk(children(node), insideSeed || seed(node), children, included,
                    seed, key, cancelled, totals, byKey);
            }
        }
    }
}
