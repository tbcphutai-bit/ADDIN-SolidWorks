using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    // No CAD calls: the native 3D sketch readback is the only geometry input.
    // The hinge need not lie on the profile plane (position offset/thickness).
    internal sealed class FlangeGuideFrame61
    {
        internal double[] Origin, Hinge, Outward, Normal;
        internal double PlaneResidual, HingePlaneOffset;

        internal static FlangeGuideFrame61 FromPoints(double[] start, double[] end,
            IList<double[]> points, double[] normalHint)
        {
            if (points == null || points.Count < 3)
                throw new InvalidOperationException("FLANGE61 guide has insufficient profile points.");
            foreach (var p in points.Concat(new[] { start, end, normalHint }))
                if (p == null || p.Length != 3 || p.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                    throw new InvalidOperationException("FLANGE61 guide contains invalid coordinates.");
            double[] origin = points[0];
            var longest = points.Select(p => Subtract(p, origin)).OrderByDescending(v => Dot(v, v)).First();
            var cross = points.Select(p => Cross(longest, Subtract(p, origin)))
                .OrderByDescending(v => Dot(v, v)).First();
            double[] planeNormal = Unit(cross);
            double[] hinge = Unit(Subtract(end, start));
            if (Math.Abs(Dot(planeNormal, Unit(normalHint))) < 1 - 1e-6 ||
                Math.Abs(Dot(planeNormal, hinge)) > 1e-6)
                throw new InvalidOperationException("FLANGE61 guide plane disagrees with source plane/hinge.");
            double residual = points.Max(p => Math.Abs(Dot(Subtract(p, origin), planeNormal)));
            if (residual > 1e-7)
                throw new InvalidOperationException("FLANGE61 3D profile is not planar; residual_m=" + residual);
            double offset = Dot(Subtract(start, origin), planeNormal);
            double[] projectedStart = Subtract(start, Scale(planeNormal, offset));
            // A real profile point supplies the drawing side, not a world-axis rule.
            double[] outward = points.Select(p => Subtract(p, projectedStart))
                .Select(v => Subtract(v, Scale(hinge, Dot(v, hinge))))
                .OrderByDescending(v => Dot(v, v)).First();
            outward = Unit(outward);
            return new FlangeGuideFrame61 { Origin = origin.ToArray(), Hinge = hinge,
                Outward = outward, Normal = Unit(Cross(hinge, outward)),
                PlaneResidual = residual, HingePlaneOffset = offset };
        }

        internal static double[] Subtract(double[] a, double[] b)
        { return Enumerable.Range(0, 3).Select(i => a[i] - b[i]).ToArray(); }
        internal static double[] Scale(double[] a, double s)
        { return a.Select(v => v * s).ToArray(); }
        internal static double Dot(double[] a, double[] b)
        { return Enumerable.Range(0, 3).Sum(i => a[i] * b[i]); }
        internal static double[] Cross(double[] a, double[] b)
        { return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] }; }
        internal static double[] Unit(double[] a)
        {
            if (a == null || a.Length != 3 || a.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                throw new InvalidOperationException("FLANGE62 invalid direction coordinates.");
            double length = Math.Sqrt(Dot(a, a));
            if (length < 1e-12) throw new InvalidOperationException("FLANGE61 degenerate calibration geometry.");
            return Scale(a, 1 / length);
        }

        // Unlike an unoriented plane, a drawing side must not match its negative.
        // Read native +Y at two angles; use its measured response around the real
        // hinge to solve +Y -> reflected profile outward (no +/- normal shortcut).
        internal static double SolveDirectedAngle62(double initialAngle, double step,
            double[] hinge, double[] firstY, double[] secondY, double[] outward,
            out double response, out double delta)
        {
            if (double.IsNaN(initialAngle) || double.IsInfinity(initialAngle) ||
                double.IsNaN(step) || double.IsInfinity(step) || step < 1e-5 || step > .05)
                throw new InvalidOperationException("FLANGE62 invalid calibration angle/step.");
            hinge = Unit(hinge);
            firstY = Unit(firstY);
            secondY = Unit(secondY);
            outward = Unit(outward);
            if (new[] { firstY, secondY, outward }.Any(v => Math.Abs(Dot(v, hinge)) > 1e-6))
                throw new InvalidOperationException("FLANGE62 drawing side is not perpendicular to hinge.");
            response = Math.Atan2(Dot(hinge, Cross(firstY, secondY)), Dot(firstY, secondY));
            if (Math.Abs(Math.Abs(response) - step) > step * .03)
                throw new InvalidOperationException("FLANGE62 native +Y does not follow a unit hinge rotation; response_rad=" + response);
            delta = Math.Atan2(Dot(hinge, Cross(firstY, outward)), Dot(firstY, outward));
            double value = initialAngle + delta * step / response;
            return (value % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
        }
    }

    // Numeric correspondence only, NOT a geometry acceptance test. Coordinates
    // are expressed in each measured profile's directed hinge/outward frame.
    // Normalization permits a native length dimension to regenerate a different
    // size. Every final point must still be read back at the exact guide position.
    internal sealed class FlangeIdentityPrimitive68
    {
        internal int Type;
        internal bool Construction, Circle;
        internal double[] Start, End, Center, Middle;
        internal string StartKey, EndKey, CenterKey;
    }

    internal sealed class FlangeIdentityMove68
    {
        internal string PointKey;
        internal int TargetPrimitive, TargetRole;
    }

    internal static class FlangeIdentityGeometry68
    {
        internal static List<FlangeIdentityMove68> Plan(IList<FlangeIdentityPrimitive68> native,
            IList<FlangeIdentityPrimitive68> target)
        {
            if (native == null || target == null || native.Count == 0 || native.Count != target.Count || native.Count > 256)
                throw new InvalidOperationException("FLANGE68 native/guide topology differs; no entity was deleted.");
            var left = Normalize(native);
            var right = Normalize(target);
            var candidates = Enumerable.Range(0, left.Count).Select(i => Enumerable.Range(0, right.Count)
                .Where(j => Compatible(left[i], right[j])).ToArray()).ToArray();
            int[] assignment = Match(candidates, -1, -1);
            if (assignment == null)
                throw new InvalidOperationException("FLANGE68 no native/guide primitive correspondence; no point was moved.");
            // A perfect matching alone is not a unique matching. Forbid each
            // chosen edge in turn; any alternative complete match is ambiguous.
            for (int i = 0; i < assignment.Length; i++)
                if (Match(candidates, i, assignment[i]) != null)
                    throw new InvalidOperationException("FLANGE68 ambiguous primitive correspondence; no name/order tie-break.");
            var moves = new Dictionary<string, FlangeIdentityMove68>(StringComparer.Ordinal);
            for (int i = 0; i < assignment.Length; i++)
            {
                int j = assignment[i];
                bool direct = Same(left[i].Start, right[j].Start) &&
                    (left[i].Circle || Same(left[i].End, right[j].End));
                bool reverse = !left[i].Circle && Same(left[i].Start, right[j].End) && Same(left[i].End, right[j].Start);
                if (direct == reverse)
                    throw new InvalidOperationException("FLANGE68 ambiguous endpoint direction; no point was moved.");
                // Shared-point consistency is checked in physical canonical
                // coordinates, NOT normalized bounds (which can hide a gap).
                Add(moves, native[i].StartKey, j, direct ? 0 : 1, target);
                if (!native[i].Circle) Add(moves, native[i].EndKey, j, direct ? 1 : 0, target);
                if (native[i].Type == 1) Add(moves, native[i].CenterKey, j, 2, target);
            }
            return moves.Values.ToList();
        }

        static void Add(Dictionary<string, FlangeIdentityMove68> moves, string key, int target, int role,
            IList<FlangeIdentityPrimitive68> geometry)
        {
            if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("FLANGE68 native point ID unavailable.");
            FlangeIdentityMove68 previous;
            if (moves.TryGetValue(key, out previous))
            {
                if (!Same(Point(geometry[previous.TargetPrimitive], previous.TargetRole), Point(geometry[target], role)))
                    throw new InvalidOperationException("FLANGE68 one shared native point has conflicting guide targets.");
                return;
            }
            moves.Add(key, new FlangeIdentityMove68 { PointKey = key, TargetPrimitive = target, TargetRole = role });
        }

        internal static double[] Point(FlangeIdentityPrimitive68 p, int role)
        {
            if (role == 0) return p.Start;
            if (role == 1) return p.End;
            if (role == 2) return p.Center;
            throw new InvalidOperationException("FLANGE68 invalid point role.");
        }

        static List<FlangeIdentityPrimitive68> Normalize(IList<FlangeIdentityPrimitive68> profile)
        {
            foreach (var p in profile)
            {
                if (p == null || (p.Type != 0 && p.Type != 1) || (p.Circle && p.Type != 1))
                    throw new InvalidOperationException("FLANGE68 unsupported native primitive; original IDs retained.");
                Validate(p.Start);
                if (!p.Circle) Validate(p.End);
                if (p.Type == 1) { Validate(p.Center); if (!p.Circle) Validate(p.Middle); }
            }
            var points = profile.SelectMany(p => new[] { p.Start, p.End, p.Center, p.Middle }).Where(p => p != null).ToList();
            double[] min = { points.Min(p => p[0]), points.Min(p => p[1]) };
            double[] size = { points.Max(p => p[0]) - min[0], points.Max(p => p[1]) - min[1] };
            if (size.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v <= 1e-9))
                throw new InvalidOperationException("FLANGE68 degenerate native profile bounds.");
            Func<double[], double[]> normalized = p => p == null ? null : new[] { (p[0] - min[0]) / size[0], (p[1] - min[1]) / size[1] };
            return profile.Select(p => new FlangeIdentityPrimitive68 { Type = p.Type, Construction = p.Construction,
                Circle = p.Circle, Start = normalized(p.Start), End = normalized(p.End), Center = normalized(p.Center),
                Middle = normalized(p.Middle) }).ToList();
        }

        static void Validate(double[] p)
        {
            if (p == null || p.Length != 2 || p.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                throw new InvalidOperationException("FLANGE68 invalid canonical coordinates.");
        }

        static bool Same(double[] a, double[] b)
        {
            if (a == null || b == null) return a == null && b == null;
            return Math.Abs(a[0] - b[0]) <= 1e-7 && Math.Abs(a[1] - b[1]) <= 1e-7;
        }

        static bool Compatible(FlangeIdentityPrimitive68 a, FlangeIdentityPrimitive68 b)
        {
            return a.Type == b.Type && a.Construction == b.Construction && a.Circle == b.Circle &&
                Same(a.Center, b.Center) && Same(a.Middle, b.Middle) &&
                ((Same(a.Start, b.Start) && (a.Circle || Same(a.End, b.End))) ||
                (!a.Circle && Same(a.Start, b.End) && Same(a.End, b.Start)));
        }

        static int[] Match(int[][] candidates, int excludedLeft, int excludedRight)
        {
            var rightToLeft = Enumerable.Repeat(-1, candidates.Length).ToArray();
            for (int i = 0; i < candidates.Length; i++)
                if (!Augment(i, candidates, rightToLeft, new bool[candidates.Length], excludedLeft, excludedRight)) return null;
            var result = new int[candidates.Length];
            for (int j = 0; j < rightToLeft.Length; j++) result[rightToLeft[j]] = j;
            return result;
        }

        static bool Augment(int left, int[][] candidates, int[] rightToLeft, bool[] visited, int excludedLeft, int excludedRight)
        {
            foreach (int right in candidates[left])
            {
                if (visited[right] || (left == excludedLeft && right == excludedRight)) continue;
                visited[right] = true;
                if (rightToLeft[right] < 0 || Augment(rightToLeft[right], candidates, rightToLeft, visited, excludedLeft, excludedRight))
                { rightToLeft[right] = left; return true; }
            }
            return false;
        }
    }
}
