using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
namespace ADDIN.Commands.MirrorV7
{
    public sealed class BodyMirrorVerificationResultV7
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool Success { get { return Errors.Count == 0; } }
    }
    public static class BodyMirrorVerifierV7
    {
        public sealed class Measurements
        {
            public double Volume, Area;
            public double[] Centroid, Box;
            public int Faces;
        }
        public static Measurements Measure(Body2 body)
        {
            if (body == null) throw new ArgumentNullException("body");
            var mass = body.GetMassProperties(1.0) as double[];
            var box = body.GetBodyBox() as double[];
            if (mass == null || mass.Length < 5 || box == null || box.Length != 6)
                throw new InvalidOperationException("Body measurements unavailable.");
            foreach (double value in mass) if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidOperationException("Invalid mass properties.");
            foreach (double value in box) if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidOperationException("Invalid bounding box.");
            if (mass[3] <= 0 || mass[4] <= 0) throw new InvalidOperationException("Expected a non-empty solid body.");
            return new Measurements { Volume = mass[3], Area = mass[4], Centroid = new[] { mass[0], mass[1], mass[2] }, Box = box, Faces = body.GetFaceCount() };
        }
        private static bool Near(double a, double b, double absolute)
        { return Math.Abs(a-b) <= absolute + 1e-6 * Math.Max(Math.Abs(a), Math.Abs(b)); }
        public static BodyMirrorVerificationResultV7 Verify(Body2 source, Body2 output, PartReflectionTransformV7 reflection)
        { return Compare(Measure(source), Measure(output), reflection); }
        public static BodyMirrorVerificationResultV7 Compare(Measurements source, Measurements output, PartReflectionTransformV7 reflection)
        {
            if (source == null || output == null || reflection == null) throw new ArgumentNullException();
            var result = new BodyMirrorVerificationResultV7();
            if (!Near(source.Volume, output.Volume, 1e-12)) result.Errors.Add("Volume mismatch.");
            if (!Near(source.Area, output.Area, 1e-10)) result.Errors.Add("Surface area mismatch.");
            var expected = reflection.ReflectPoint(source.Centroid);
            for (int i=0;i<3;i++) if (!Near(expected[i], output.Centroid[i], 1e-7)) result.Errors.Add("Centroid mismatch axis="+i);
            // GetBodyBox is approximate; transformed corners enclose the body but are not an exact bbox for oblique planes.
            var bounds = ReflectBox(source.Box, reflection);
            for (int i=0;i<3;i++)
                if (!Near(bounds[i], output.Box[i], 1e-6) || !Near(bounds[i+3], output.Box[i+3], 1e-6))
                    result.Warnings.Add("Approximate bounding box differs axis="+i+"; exact extrema verification required before output acceptance.");
            if (source.Faces != output.Faces) result.Warnings.Add("Face count differs.");
            return result;
        }
        private static double[] ReflectBox(double[] box, PartReflectionTransformV7 reflection)
        {
            var bounds = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
            for(int mask=0;mask<8;mask++)
            {
                var p = reflection.ReflectPoint(new[] { box[(mask&1)==0?0:3], box[(mask&2)==0?1:4], box[(mask&4)==0?2:5] });
                for(int i=0;i<3;i++) { bounds[i]=Math.Min(bounds[i],p[i]); bounds[i+3]=Math.Max(bounds[i+3],p[i]); }
            }
            return bounds;
        }
        public static void SelfTest(Body2 body)
        {
            var m = Measure(body);
            foreach(CanonicalPartMirrorPlaneV7 plane in Enum.GetValues(typeof(CanonicalPartMirrorPlaneV7)))
            {
                var r=PartReflectionTransformV7.CreateCanonical(plane);
                var once=new Measurements { Volume=m.Volume, Area=m.Area, Faces=m.Faces, Centroid=r.ReflectPoint(m.Centroid), Box=ReflectBox(m.Box,r) };
                var result=Compare(once,m,r);
                if(!result.Success || result.Warnings.Count>0) throw new InvalidOperationException("Body measurement double-reflection self-test failed.");
            }
            MirrorV7Diagnostics.Log("[BODY_VERIFY][SELF_TEST] PASS scope=MEASUREMENT_MATH_ONLY volume="+m.Volume+" area="+m.Area+" faces="+m.Faces);
        }
    }
}
