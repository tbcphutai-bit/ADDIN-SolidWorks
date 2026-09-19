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
