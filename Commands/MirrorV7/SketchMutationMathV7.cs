using System;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.MirrorV7
{
    public static class SketchMutationMathV7
    {
        public const double PositionToleranceMetres = 1e-8;
        public static void Validate(double[] point)
        {
            if (point == null || point.Length < 3) throw new InvalidOperationException("Expected XYZ coordinates.");
            for (int i = 0; i < 3; i++) if (double.IsNaN(point[i]) || double.IsInfinity(point[i])) throw new InvalidOperationException("Non-finite coordinate.");
        }
        public static double Distance(double[] a, double[] b) { Validate(a); Validate(b); double x = a[0] - b[0], y = a[1] - b[1], z = a[2] - b[2]; return Math.Sqrt(x * x + y * y + z * z); }
        public static double[] Transform(IMathUtility math, MathTransform transform, double[] coordinates)
        {
            Validate(coordinates); if (math == null || transform == null) throw new InvalidOperationException("Coordinate transform is unavailable.");
            MathPoint point = math.CreatePoint(coordinates) as MathPoint; MathPoint transformed = point == null ? null : point.MultiplyTransform(transform) as MathPoint; double[] result = transformed == null ? null : transformed.ArrayData as double[]; Validate(result); return new[] { result[0], result[1], result[2] };
        }
    }
}
