using System;

namespace ADDIN.Commands.AutoFrameJoint
{
    /// <summary>
    /// Pure, testable THREE-DIMENSIONAL math: closest points on two estimated
    /// Frame axes, finite extents, acute angle and theoretical bisectors.
    /// Coordinates are in assembly meters. No COM, no body edits and no cuts.
    /// </summary>
    internal static class AutoFrameJointGeometryMath
    {
        internal struct V
        {
            internal readonly double X, Y, Z;
            internal V(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static V operator +(V a, V b)
            { return new V(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
            public static V operator -(V a, V b)
            { return new V(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
            public static V operator *(V a, double t)
            { return new V(a.X * t, a.Y * t, a.Z * t); }
            public static V operator /(V a, double t)
            { return new V(a.X / t, a.Y / t, a.Z / t); }
            internal static double Dot(V a, V b)
            { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
            internal double LengthSquared { get { return Dot(this, this); } }
            internal double Length { get { return Math.Sqrt(LengthSquared); } }
            internal bool Finite { get { return IsFinite(X) && IsFinite(Y) && IsFinite(Z); } }
            internal V Unit { get { return this / Length; } }
            internal static bool IsFinite(double x)
            { return !double.IsNaN(x) && !double.IsInfinity(x); }
        }

        internal sealed class Axis
        {
            // Axis origin is the centered sample-points centroid in WORLD coordinates.
            internal V Origin, Direction;
            internal double MinT, MaxT; // signed meters relative to Origin
        }

        internal sealed class Result
        {
            internal bool Valid, NearParallel, InfiniteNearestWithinFinite;
            internal string Status, Reason;
            internal double AngleDegrees, InfiniteAxisGapMm, FiniteAxisGapMm;
            internal double ParameterA, ParameterB;
            internal double InfiniteParameterA, InfiniteParameterB;
            internal V NearestA, NearestB, Midpoint, Bisector1, Bisector2;
        }

        private const double ParallelDenominator = 1e-10; // computational only
        private const double ParameterEpsilon = 1e-9;     // meters, NOT shop tolerance

        internal static Result Solve(Axis a, Axis b)
        {
            var r = new Result { Valid = false, Status = "UNAVAILABLE" };
            if (!ValidAxis(a) || !ValidAxis(b))
            {
                r.Reason = "Invalid or unnormalized input axis / finite bounds.";
                return r;
            }

            V u = a.Direction, v = b.Direction;
            V w = a.Origin - b.Origin;
            double dot = Clamp(V.Dot(u, v), -1.0, 1.0);
            double absDot = Math.Abs(dot);
            r.AngleDegrees = Math.Acos(absDot) * 180.0 / Math.PI;

            // The two axis directions are undirected. Reversing v does not
            // change the line but makes theoretical bisectors repeatable.
            V signedV = dot >= 0.0 ? v : v * -1.0;
            double d = V.Dot(u, w);
            double e = V.Dot(v, w);
            double denominator = 1.0 - dot * dot;
            r.NearParallel = denominator <= ParallelDenominator;

            double infS = 0.0, infT = 0.0;
            if (!r.NearParallel)
            {
                infS = (dot * e - d) / denominator;
                infT = (e - dot * d) / denominator;
                V delta = (a.Origin + u * infS) - (b.Origin + v * infT);
                r.InfiniteAxisGapMm = delta.Length * 1000.0;
                r.InfiniteNearestWithinFinite =
                    Between(infS, a.MinT, a.MaxT) &&
                    Between(infT, b.MinT, b.MaxT);
                r.Bisector1 = (u + signedV).Unit;
                r.Bisector2 = (u - signedV).Unit;
            }
            else
            {
                // Infinite parallel axes have no unique crossing parameter.
                // Report their perpendicular separation, no bisector plane.
                V perpendicular = w - u * V.Dot(w, u);
                r.InfiniteAxisGapMm = perpendicular.Length * 1000.0;
                r.InfiniteNearestWithinFinite = false;
            }

            r.InfiniteParameterA = infS;
            r.InfiniteParameterB = infT;

            // Compute the ACTUAL closest pair of points on the two FINITE
            // estimated axis segments. An infinite-line crossing outside a
            // member is not a physical joint location. Use boundary candidates
            // to solve the convex constrained two-variable distance problem.
            double bestDistanceSquared = double.PositiveInfinity;
            double bestS = 0.0, bestT = 0.0;
            if (!r.NearParallel && r.InfiniteNearestWithinFinite)
                Consider(a, b, infS, infT, ref bestDistanceSquared,
                         ref bestS, ref bestT);

            double[] sBounds = { a.MinT, a.MaxT };
            foreach (double s in sBounds)
            {
                V pointA = a.Origin + u * s;
                double t = Clamp(V.Dot(pointA - b.Origin, v),
                                 b.MinT, b.MaxT);
                Consider(a, b, s, t, ref bestDistanceSquared, ref bestS, ref bestT);
            }
            double[] tBounds = { b.MinT, b.MaxT };
            foreach (double t in tBounds)
            {
                V pointB = b.Origin + v * t;
                double s = Clamp(V.Dot(pointB - a.Origin, u),
                                 a.MinT, a.MaxT);
                Consider(a, b, s, t, ref bestDistanceSquared, ref bestS, ref bestT);
            }

            if (double.IsInfinity(bestDistanceSquared) ||
                !V.IsFinite(bestDistanceSquared))
            {
                r.Reason = "Could not find finite centerline nearest points.";
                return r;
            }

            r.ParameterA = bestS;
            r.ParameterB = bestT;
            r.NearestA = a.Origin + u * bestS;
            r.NearestB = b.Origin + v * bestT;
            r.Midpoint = (r.NearestA + r.NearestB) * 0.5;
            r.FiniteAxisGapMm = Math.Sqrt(bestDistanceSquared) * 1000.0;
            r.Valid = r.Midpoint.Finite &&
                      V.IsFinite(r.FiniteAxisGapMm);
            if (!r.Valid)
            {
                r.Status = "UNAVAILABLE";
                r.Reason = "Nonfinite joint coordinate.";
            }
            else if (r.NearParallel)
            {
                r.Status = "PARALLEL_OR_NEAR_PARALLEL";
                r.Reason = "No unique axis crossing or miter bisector.";
            }
            else if (!r.InfiniteNearestWithinFinite)
            {
                r.Status = "OUTSIDE_ONE_OR_BOTH_FRAME_SPANS";
                r.Reason = "Nearest infinite axes meet outside at least one estimated member span.";
            }
            else
            {
                r.Status = "FINITE_AXES_NEAREST";
                r.Reason = "Nearest estimated axes inside both members; does NOT prove solid collision.";
            }

            return r;
        }

        private static bool ValidAxis(Axis a)
        {
            if (a == null || !a.Origin.Finite || !a.Direction.Finite ||
                !V.IsFinite(a.MinT) || !V.IsFinite(a.MaxT) ||
                a.MaxT - a.MinT <= ParameterEpsilon)
                return false;
            double length2 = a.Direction.LengthSquared;
            return V.IsFinite(length2) && Math.Abs(length2 - 1.0) <= 0.000001;
        }

        private static void Consider(Axis a, Axis b, double s, double t,
                                     ref double distance2, ref double bestS,
                                     ref double bestT)
        {
            if (!V.IsFinite(s) || !V.IsFinite(t)) return;
            V delta = (a.Origin + a.Direction * s) -
                      (b.Origin + b.Direction * t);
            double current = delta.LengthSquared;
            if (V.IsFinite(current) && current < distance2)
            {
                distance2 = current;
                bestS = s;
                bestT = t;
            }
        }

        private static bool Between(double x, double min, double max)
        { return x >= min - ParameterEpsilon && x <= max + ParameterEpsilon; }

        private static double Clamp(double x, double min, double max)
        { return Math.Max(min, Math.Min(max, x)); }
    }
}
