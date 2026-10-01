using System;
using System.Collections.Generic;

namespace ADDIN.Commands.MirrorV7
{
    public sealed class EquivalenceMatchResultV7
    {
        public bool HasPerfectMatching { get; internal set; }
        public int[] LeftToRight { get; internal set; }
        public int LeftWithMultipleCandidates { get; internal set; }
        public int LeftWithoutCandidates { get; internal set; }
    }

    public static class BipartiteEquivalenceMatcherV7
    {
        // Model-space samples; independent of COM identity and edge orientation.
        public static bool SameEdgeSamples(double[][] a, double[][] b, double tolerance)
        {
            if (a == null || b == null || a.Length < 2 || a.Length != b.Length ||
                double.IsNaN(tolerance) || double.IsInfinity(tolerance) || tolerance <= 0) return false;
            bool forward = true, reverse = true;
            for (int i = 0; i < a.Length; i++)
            {
                forward &= SampleDistanceWithin(a[i], b[i], tolerance);
                reverse &= SampleDistanceWithin(a[i], b[b.Length - 1 - i], tolerance);
            }
            return forward || reverse;
        }

        private static bool SampleDistanceWithin(double[] a, double[] b, double tolerance)
        {
            if (a == null || b == null || a.Length != 3 || b.Length != 3) return false;
            double squared = 0;
            for (int i = 0; i < 3; i++)
            {
                double d = a[i] - b[i];
                if (double.IsNaN(d) || double.IsInfinity(d)) return false;
                squared += d * d;
            }
            return squared <= tolerance * tolerance;
        }

        public static EquivalenceMatchResultV7 Match<T>(IList<T> left, IList<T> right, Func<T, T, bool> compatible)
        {
            if (left == null) throw new ArgumentNullException("left"); if (right == null) throw new ArgumentNullException("right"); if (compatible == null) throw new ArgumentNullException("compatible");
            EquivalenceMatchResultV7 result = new EquivalenceMatchResultV7 { LeftToRight = new int[left.Count] }; for (int i = 0; i < result.LeftToRight.Length; i++) result.LeftToRight[i] = -1;
            if (left.Count != right.Count) return result;
            List<int>[] edges = new List<int>[left.Count];
            for (int i = 0; i < left.Count; i++) { edges[i] = new List<int>(); for (int j = 0; j < right.Count; j++) if (compatible(left[i], right[j])) edges[i].Add(j); if (edges[i].Count == 0) result.LeftWithoutCandidates++; if (edges[i].Count > 1) result.LeftWithMultipleCandidates++; }
            if (result.LeftWithoutCandidates > 0) return result;
            int[] rightToLeft = new int[right.Count]; for (int j = 0; j < rightToLeft.Length; j++) rightToLeft[j] = -1;
            for (int i = 0; i < left.Count; i++) if (!TryAugment(i, edges, rightToLeft, new bool[right.Count])) return result;
            for (int j = 0; j < rightToLeft.Length; j++) if (rightToLeft[j] >= 0) result.LeftToRight[rightToLeft[j]] = j;
            result.HasPerfectMatching = true; return result;
        }
        private static bool TryAugment(int leftIndex, List<int>[] edges, int[] rightToLeft, bool[] visitedRight)
        {
            foreach (int rightIndex in edges[leftIndex]) { if (visitedRight[rightIndex]) continue; visitedRight[rightIndex] = true; int previousLeft = rightToLeft[rightIndex]; if (previousLeft < 0 || TryAugment(previousLeft, edges, rightToLeft, visitedRight)) { rightToLeft[rightIndex] = leftIndex; return true; } }
            return false;
        }
    }
}
