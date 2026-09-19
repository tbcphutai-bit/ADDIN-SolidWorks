using System;
namespace ADDIN.Commands.MirrorV7
{
    public sealed class MirrorV7Plane
    {
        private readonly double[] origin,normal;
        public double[] Origin { get { return new[]{origin[0],origin[1],origin[2]}; } }
        public double[] Normal { get { return new[]{normal[0],normal[1],normal[2]}; } }
        public MirrorV7Plane(double[] origin,double[] normal){Validate(origin,"origin");Validate(normal,"normal");this.origin=new[]{origin[0],origin[1],origin[2]};this.normal=Normalize(normal);}
        private static double[] Normalize(double[] v){double l=Math.Sqrt(v[0]*v[0]+v[1]*v[1]+v[2]*v[2]);if(l<=1e-12)throw new ArgumentException("Plane normal cannot be zero.");return new[]{v[0]/l,v[1]/l,v[2]/l};}
        private static void Validate(double[] v,string n){if(v==null||v.Length<3)throw new ArgumentException("Expected XYZ array.",n);}
    }
}
