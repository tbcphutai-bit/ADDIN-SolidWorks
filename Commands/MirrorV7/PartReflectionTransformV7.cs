using System;
namespace ADDIN.Commands.MirrorV7
{
    public sealed class PartReflectionTransformV7
    {
        private const double Tolerance=1e-9; private readonly double[] origin,normal;
        public double[] Origin { get { return Copy(origin); } } public double[] Normal { get { return Copy(normal); } }
        private PartReflectionTransformV7(double[] o,double[] n){origin=Copy(o);normal=Normalize(n);if(Math.Abs(origin[0])>Tolerance||Math.Abs(origin[1])>Tolerance||Math.Abs(origin[2])>Tolerance)throw new InvalidOperationException("V7 Part reflection plane must pass through Part Origin.");}
        public static PartReflectionTransformV7 CreateCanonical(CanonicalPartMirrorPlaneV7 p){return new PartReflectionTransformV7(new[]{0.0,0,0},CanonicalPartMirrorPlaneSelectorV7.GetNormal(p));}
        public static PartReflectionTransformV7 CreateOriginAnchored(double[] n){return new PartReflectionTransformV7(new[]{0.0,0,0},n);}
        public double[] ReflectPoint(double[] p){Validate(p,"point");double d=(p[0]-origin[0])*normal[0]+(p[1]-origin[1])*normal[1]+(p[2]-origin[2])*normal[2];return new[]{p[0]-2*d*normal[0],p[1]-2*d*normal[1],p[2]-2*d*normal[2]};}
        public double[] ReflectVector(double[] v){Validate(v,"vector");double d=v[0]*normal[0]+v[1]*normal[1]+v[2]*normal[2];return new[]{v[0]-2*d*normal[0],v[1]-2*d*normal[1],v[2]-2*d*normal[2]};}
        public double[] ReflectNormal(double[] n){return ReflectVector(n);}
        public MirrorV7Plane ReflectPlane(MirrorV7Plane p){if(p==null)throw new ArgumentNullException("sourcePlane");return new MirrorV7Plane(ReflectPoint(p.Origin),ReflectNormal(p.Normal));}
        public double DistanceToMirrorPlane(double[] p){Validate(p,"point");return (p[0]-origin[0])*normal[0]+(p[1]-origin[1])*normal[1]+(p[2]-origin[2])*normal[2];}
        public bool IsPointOnMirrorPlane(double[] p){return Math.Abs(DistanceToMirrorPlane(p))<=Tolerance;}
        public double[] GetLinearMatrix3x3(){double x=normal[0],y=normal[1],z=normal[2];return new[]{1-2*x*x,-2*x*y,-2*x*z,-2*y*x,1-2*y*y,-2*y*z,-2*z*x,-2*z*y,1-2*z*z};}
        private static double[] Normalize(double[] v){Validate(v,"normal");double l=Math.Sqrt(v[0]*v[0]+v[1]*v[1]+v[2]*v[2]);if(l<=1e-12)throw new ArgumentException("Reflection normal cannot be zero.");return new[]{v[0]/l,v[1]/l,v[2]/l};}
        private static void Validate(double[] v,string n){if(v==null||v.Length<3)throw new ArgumentException("Expected XYZ array.",n);}
        private static double[] Copy(double[] v){Validate(v,"value");return new[]{v[0],v[1],v[2]};}
    }
}
