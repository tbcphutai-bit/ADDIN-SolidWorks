using System;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.MirrorV7
{
    /// <summary>Validate SOLIDWORKS' actual affine map, not merely the supplied array.</summary>
    public static class ReflectionApiTransformV7
    {
        public static MathTransform Create(IMathUtility math, double[] householderData)
        {
            if (math == null) throw new ArgumentNullException("math");
            if (householderData == null || householderData.Length != 16)
                throw new ArgumentException("Expected 16 transform entries.");
            for (int i = 0; i < 16; i++)
                if (double.IsNaN(householderData[i]) || double.IsInfinity(householderData[i]))
                    throw new ArgumentException("Non-finite transform entry.");
            double[] expected = (double[])householderData.Clone();
            if (expected[12] != 1) throw new ArgumentException("Expected unit-scale reflection.");
            var direct = math.CreateTransform(expected) as MathTransform;
            if (Matches(math, direct, expected)) return direct;

            // R = (-R) * (-1). Some API paths normalize rotation axes; retain a proper
            // rotation and encode handedness in the scale. Never assume this is accepted.
            double[] alternative = (double[])expected.Clone();
            for (int i = 0; i < 9; i++) alternative[i] = -alternative[i];
            alternative[12] = -1;
            var signedScale = math.CreateTransform(alternative) as MathTransform;
            if (Matches(math, signedScale, expected))
            {
                MirrorV7Diagnostics.Log("[REFLECTION_API] representation=PROPER_ROTATION_NEGATIVE_SCALE basisVerified=True");
                return signedScale;
            }
            throw new InvalidOperationException("SOLIDWORKS transform does not implement requested reflection; refusing geometry mutation.");
        }

        private static bool Matches(IMathUtility math, MathTransform transform, double[] expected)
        {
            if (transform == null) return false;
            double[][] probes = { new double[3], new[] { 1.0, 0, 0 },
                new[] { 0.0, 1, 0 }, new[] { 0.0, 0, 1 }, new[] { .137, -.293, .419 } };
            foreach (double[] p in probes)
            {
                var point = math.CreatePoint(p) as MathPoint;
                var transformed = point == null ? null : point.MultiplyTransform(transform) as MathPoint;
                var actual = transformed == null ? null : transformed.ArrayData as double[];
                if (actual == null || actual.Length < 3) return false;
                for (int axis = 0; axis < 3; axis++)
                {
                    double wanted = p[0] * expected[axis] + p[1] * expected[3 + axis] +
                        p[2] * expected[6 + axis] + expected[9 + axis];
                    if (double.IsNaN(actual[axis]) || double.IsInfinity(actual[axis]) ||
                        Math.Abs(actual[axis] - wanted) > 1e-10 * Math.Max(1, Math.Abs(wanted))) return false;
                }
            }
            return true;
        }
    }
}
