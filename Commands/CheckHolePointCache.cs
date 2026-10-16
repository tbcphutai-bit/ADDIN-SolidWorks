using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ADDIN.Commands
{
    // Scoped to one body scan. Exact parameter bits preserve the existing samples.
    internal sealed class CheckHolePointCache
    {
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) { return ReferenceEquals(x, y); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }

        private readonly Dictionary<object, Dictionary<long, double[]>> points =
            new Dictionary<object, Dictionary<long, double[]>>(new ReferenceComparer());
        public long ApiCalls { get; private set; }
        public long Hits { get; private set; }

        public double[] Evaluate(object edge, double parameter, Func<object, double, double[]> evaluate)
        {
            if (edge == null || evaluate == null || double.IsNaN(parameter) || double.IsInfinity(parameter))
                return null;
            long key = BitConverter.DoubleToInt64Bits(parameter);
            Dictionary<long, double[]> edgePoints;
            double[] cached;
            if (points.TryGetValue(edge, out edgePoints) && edgePoints.TryGetValue(key, out cached))
            {
                Hits++;
                return (double[])cached.Clone();
            }
            ApiCalls++;
            double[] value = evaluate(edge, parameter);
            if (value == null || value.Length != 3 || Array.Exists(value, x => double.IsNaN(x) || double.IsInfinity(x)))
                return null; // A transient API failure must be retried, not cached.
            if (edgePoints == null)
            {
                edgePoints = new Dictionary<long, double[]>();
                points.Add(edge, edgePoints);
            }
            edgePoints[key] = (double[])value.Clone();
            return value;
        }
    }

    internal static class CheckHoleBodyCachePolicy
    {
        public static string Key(string path, string configuration, int index, string body,
            string occurrence, bool knownNormalBody)
        {
            string key = (path ?? "") + "|" + (configuration ?? "") + "|" + index + "|" + (body ?? "");
            // User/unknown bodies and virtual parts must not share topology between occurrences.
            return knownNormalBody && !string.IsNullOrWhiteSpace(path)
                ? key : key + "|OCCURRENCE=" + (occurrence ?? "");
        }
    }
}
