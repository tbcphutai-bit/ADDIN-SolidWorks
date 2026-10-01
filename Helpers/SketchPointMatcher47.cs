using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands;
using ADDIN.Commands.MirrorV7;

namespace ADDIN.Helpers
{
    // Match owned sketch points after SolidWorks regenerates their IDs. No list-index
    // fallback: every accepted assignment has the same reflected target per live point.
    public static class SketchPointMatcher47
    {
        private const double Tolerance = 1e-7;

        public static int[] Resolve(IList<SketchPointSnapshot> source,
            IList<SketchPointSnapshot> live, PlaneData plane, out string evidence)
        {
            if (source == null || live == null || source.Count != live.Count || source.Count == 0)
                throw new InvalidOperationException("POINT47 source/live point count differs or is empty.");
            var byId = new int[live.Count];
            var usedIds = new HashSet<int>();
            bool allIds = true;
            for (int i = 0; i < live.Count; i++)
            {
                int candidate = -1;
                for (int j = 0; j < source.Count; j++)
                    if (source[j].Id1 == live[i].Id1 && source[j].Id2 == live[i].Id2)
                    { candidate = j; break; }
                byId[i] = candidate;
                if (candidate < 0 || !usedIds.Add(candidate)) allIds = false;
            }
            if (allIds) { evidence = "POINT_ID"; return byId; }

            bool modelReady = plane != null && source.All(p => p.HasModelCoords) &&
                live.All(p => p.HasModelCoords);
            var sourceTargets = modelReady ? source.Select(p => Reflect(Model(p), plane)).ToArray() : null;
            int[] matched = TryMode(source, live, sourceTargets,
                (i, j) => Near(Local(source[j]), Local(live[i])), "LOCAL_COORDS");
            if (matched != null) { evidence = "LOCAL_COORDS"; return matched; }

            if (modelReady)
            {
                int[] original = TryMode(source, live, sourceTargets,
                    (i, j) => Near(Model(source[j]), Model(live[i])), "MODEL_COORDS");
                int[] reflected = TryMode(source, live, sourceTargets,
                    (i, j) => Near(Reflect(Model(source[j]), plane), Model(live[i])), "REFLECTED_MODEL_COORDS");
                if (original != null && reflected != null && !SameTargets(original, reflected, sourceTargets))
                    throw new InvalidOperationException("POINT47 original/reflected model matches imply different targets.");
                matched = original ?? reflected;
                if (matched != null)
                { evidence = original != null ? "MODEL_COORDS" : "REFLECTED_MODEL_COORDS"; return matched; }
            }

            // Rigid changes of the sketch frame preserve all pairwise distances.
            // This tier rejects symmetries if they could send a live point to a
            // different reflected source location.
            double[][] aSignature = Signatures(source);
            double[][] bSignature = Signatures(live);
            matched = TryMode(source, live, sourceTargets,
                (i, j) => Near(aSignature[j], bSignature[i]), "DISTANCE_FINGERPRINT");
            if (matched != null)
            {
                for (int i = 0; i < live.Count; i++)
                    for (int j = 0; j < i; j++)
                        if (Math.Abs(Distance(Local(live[i]), Local(live[j])) -
                            Distance(Local(source[matched[i]]), Local(source[matched[j]]))) > Tolerance)
                            throw new InvalidOperationException("POINT47 fingerprint assignment is not rigid.");
                evidence = "DISTANCE_FINGERPRINT";
                return matched;
            }
            throw new InvalidOperationException("POINT47 point IDs changed and no unique geometry-preserving mapping exists; count=" + live.Count);
        }

        private static int[] TryMode(IList<SketchPointSnapshot> source,
            IList<SketchPointSnapshot> live, double[][] targets,
            Func<int, int, bool> compatible, string mode)
        {
            // Index values for distance signatures are positions in these lists.
            var left = Enumerable.Range(0, live.Count).ToArray();
            var right = Enumerable.Range(0, source.Count).ToArray();
            Func<int, int, bool> edge = compatible;
            var match = BipartiteEquivalenceMatcherV7.Match(left, right, edge);
            if (!match.HasPerfectMatching) return null;
            for (int i = 0; i < live.Count; i++)
                for (int j = 0; j < source.Count; j++)
                    if (edge(i, j) && j != match.LeftToRight[i] &&
                        !SameTarget(source[j], source[match.LeftToRight[i]], targets == null ? null : targets[j],
                            targets == null ? null : targets[match.LeftToRight[i]]))
                            throw new InvalidOperationException("POINT47 " + mode +
                                " has ambiguous source points with different reflected targets.");
            return match.LeftToRight;
        }

        private static double[][] Signatures(IList<SketchPointSnapshot> points)
        {
            var result = new double[points.Count][];
            for (int i = 0; i < points.Count; i++)
                result[i] = Enumerable.Range(0, points.Count)
                    .Select(j => Distance(Local(points[i]), Local(points[j]))).OrderBy(x => x).ToArray();
            return result;
        }
        private static bool SameTargets(int[] a, int[] b, double[][] target)
        {
            for (int i = 0; i < a.Length; i++)
                if (!Near(target[a[i]], target[b[i]])) return false;
            return true;
        }
        private static bool SameTarget(SketchPointSnapshot a, SketchPointSnapshot b, double[] targetA, double[] targetB)
        {
            if (targetA == null || targetB == null) return Near(Local(a), Local(b));
            return Near(targetA, targetB);
        }
        private static double[] Local(SketchPointSnapshot p) { return new[] { p.X, p.Y, 0.0 }; }
        private static double[] Model(SketchPointSnapshot p) { return new[] { p.ModelX, p.ModelY, p.ModelZ }; }
        private static double[] Reflect(double[] p, PlaneData plane)
        {
            double length = plane.Normal.Sum(x => x * x);
            if (length < 1e-20) throw new InvalidOperationException("POINT47 invalid mirror normal.");
            double dot = Enumerable.Range(0, 3).Sum(i => (p[i] - plane.Origin[i]) * plane.Normal[i]) / length;
            return Enumerable.Range(0, 3).Select(i => p[i] - 2.0 * dot * plane.Normal[i]).ToArray();
        }
        private static double Distance(double[] a, double[] b)
        { return Math.Sqrt(Enumerable.Range(0, 3).Sum(i => Math.Pow(a[i] - b[i], 2))); }
        private static bool Near(double[] a, double[] b) { return Distance(a, b) <= Tolerance; }
    }
}
