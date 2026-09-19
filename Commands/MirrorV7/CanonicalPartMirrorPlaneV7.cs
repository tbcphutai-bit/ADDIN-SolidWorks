using System;
namespace ADDIN.Commands.MirrorV7
{
    public enum CanonicalPartMirrorPlaneV7 { YZ = 0, XZ = 1, XY = 2 }
    public static class CanonicalPartMirrorPlaneSelectorV7
    {
        public static CanonicalPartMirrorPlaneV7 FromLocalNormal(double[] normal)
        {
            ValidateVector(normal,"normal"); double x=Math.Abs(normal[0]), y=Math.Abs(normal[1]), z=Math.Abs(normal[2]);
            return x>=y&&x>=z?CanonicalPartMirrorPlaneV7.YZ:y>=x&&y>=z?CanonicalPartMirrorPlaneV7.XZ:CanonicalPartMirrorPlaneV7.XY;
        }
        public static double[] GetNormal(CanonicalPartMirrorPlaneV7 plane)
        {
            switch(plane){case CanonicalPartMirrorPlaneV7.YZ:return new[]{1.0,0,0};case CanonicalPartMirrorPlaneV7.XZ:return new[]{0.0,1,0};case CanonicalPartMirrorPlaneV7.XY:return new[]{0.0,0,1};default:throw new ArgumentOutOfRangeException("plane");}
        }
        private static void ValidateVector(double[] value,string name){if(value==null||value.Length<3)throw new ArgumentException("Vector must contain X, Y and Z.",name);if(value[0]*value[0]+value[1]*value[1]+value[2]*value[2]<=1e-24)throw new ArgumentException("Vector magnitude is zero.",name);}
    }
}
