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
}
