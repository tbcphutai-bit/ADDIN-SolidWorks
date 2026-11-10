using System;
using System.Globalization;

namespace ADDIN.Commands.CircleFaceTubeLayout
{
    // SolidWorks API coordinates and all solver tolerances are in meters.
    internal struct Point3
    {
        public readonly double X, Y, Z;
        public Point3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Point3 Read(object value, int index = 0)
        {
            var a = value as double[];
            if (a == null || a.Length < index + 3)
                throw new InvalidOperationException("Missing 3D coordinates from SolidWorks.");
            var point = new Point3(a[index], a[index + 1], a[index + 2]);
            if (double.IsNaN(point.Squared) || double.IsInfinity(point.Squared))
                throw new InvalidOperationException("Non-finite 3D coordinates.");
            return point;
        }

        public static Point3 operator +(Point3 a, Point3 b) => new Point3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Point3 operator -(Point3 a, Point3 b) => new Point3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Point3 operator *(Point3 a, double k) => new Point3(a.X * k, a.Y * k, a.Z * k);
        public double Dot(Point3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public Point3 Cross(Point3 b) => new Point3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Squared => Dot(this);
        public double Length => Math.Sqrt(Squared);
        public Point3 Unit()
        {
            double length = Length;
            if (length < 1e-12) throw new InvalidOperationException("Degenerate 3D direction.");
            return this * (1 / length);
        }

        public double[] Array() => new[] { X, Y, Z };
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:F9},{1:F9},{2:F9})", X, Y, Z);
    }

    internal struct TubeAxis
    {
        public readonly Point3 A, B, Direction;
        public readonly double Length;

        public TubeAxis(Point3 a, Point3 b)
        {
            A = a;
            B = b;
            Length = (b - a).Length;
            Direction = (b - a).Unit();
        }

        public TubeAxis StartSection(double length) => new TubeAxis(A, A + Direction * Math.Min(length, Length));

        // Support of a flat-capped cylinder in a direction, including both end disks.
        public Point3 Support(Point3 direction, double radius)
        {
            double axial = direction.Dot(Direction);
            var radial = direction - Direction * axial;
            double radialLength = radial.Length;
            return (axial > 0 ? B : A) + radial * (radialLength > 1e-14 ? radius / radialLength : 0);
        }
    }

    internal struct Gap
    {
        public readonly double Lower, Upper;
        public Gap(double lower, double upper) { Lower = lower; Upper = upper; }
    }

    internal static class TubeDistance
    {
        public const double Resolution = 0.0000002;

        // GJK distance on the difference of two convex envelopes. The current simplex
        // supplies an upper bound; the supporting plane supplies a lower bound.
        public static Gap Between(TubeAxis a, TubeAxis b, double radius)
        {
            var direction = a.A - b.A;
            if (direction.Squared < 1e-24) direction = new Point3(1, 0, 0);
            var simplex = new Point3[4];
            simplex[0] = a.Support(direction, radius) - b.Support(direction * -1, radius);
            int count = 1;
            double lower = 0;
            double upper = simplex[0].Length;
            var closest = simplex[0];
            for (int i = 0; i < 96; i++)
            {
                if (upper <= Resolution) return new Gap(0, upper);
                var normal = closest * (1 / upper);
                var support = a.Support(normal * -1, radius) - b.Support(normal, radius);
                lower = Math.Max(lower, normal.Dot(support));
                if (upper - lower <= Resolution) return new Gap(Math.Min(lower, upper), upper);
                if (count == 4) break;
                simplex[count++] = support;
                closest = Reduce(simplex, ref count);
                upper = closest.Length;
            }

            // A loose bound can still prove separation. Callers must never interpret
            // an uncertain lower bound of zero as a clear pair.
            return new Gap(Math.Min(lower, upper), upper);
        }

        private static Point3 Reduce(Point3[] simplex, ref int count)
        {
            int bestMask = 1;
            var closest = simplex[0];
            var points = new Point3[4];
            for (int mask = 2; mask < (1 << count); mask++)
            {
                int n = 0;
                for (int j = 0; j < count; j++)
                    if ((mask & (1 << j)) != 0) points[n++] = simplex[j];

                Point3 candidate;
                if (Project(points, n, out candidate) && candidate.Squared < closest.Squared)
                {
                    closest = candidate;
                    bestMask = mask;
                }
            }
            int retained = 0;
            for (int j = 0; j < count; j++)
                if ((bestMask & (1 << j)) != 0) simplex[retained++] = simplex[j];
            count = retained;
            return closest;
        }

        private static bool Project(Point3[] p, int count, out Point3 point)
        {
            point = p[0];
            if (count == 1) return true;
            var u = p[1] - p[0];
            if (u.Squared < 1e-24) return false;
            double a = -u.Dot(p[0]) / u.Squared, b = 0, c = 0;
            if (count >= 3)
            {
                var v = p[2] - p[0];
                var cross = u.Cross(v);
                double determinant = cross.Squared;
                if (determinant < 1e-20 * u.Squared * v.Squared || determinant < 1e-40) return false;
                a = (p[0] * -1).Cross(v).Dot(cross) / determinant;
                b = u.Cross(p[0] * -1).Dot(cross) / determinant;
                if (count == 4)
                {
                    var w = p[3] - p[0];
                    double volume = cross.Dot(w);
                    if (Math.Abs(volume) < 1e-12 * Math.Sqrt(determinant) * w.Length || Math.Abs(volume) < 1e-24)
                        return false;
                    a = -p[0].Dot(v.Cross(w)) / volume;
                    b = -u.Dot(p[0].Cross(w)) / volume;
                    c = -cross.Dot(p[0]) / volume;
                }
            }
            if (a < 0 || b < 0 || c < 0 || a + b + c > 1) return false;
            point = p[0] * (1 - a - b - c) + p[1] * a + p[2] * b + p[3] * c;
            return true;
        }
    }
}
