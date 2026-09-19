using System;
namespace ADDIN.Commands.MirrorV7
{
    public static class ReflectionMathSelfTestV7
    {
        private const double T=1e-9;
        public static void RunOrThrow(){YZ();XZ();XY();Double();OnPlane();Length();Arbitrary();Selector();MirrorV7Diagnostics.LogPhase("PHASE3","Reflection mathematics self-test PASS.");}
        private static void YZ(){AssertV(PartReflectionTransformV7.CreateCanonical(CanonicalPartMirrorPlaneV7.YZ).ReflectPoint(new[]{10.0,20,30}),new[]{-10.0,20,30},"YZ");}
        private static void XZ(){AssertV(PartReflectionTransformV7.CreateCanonical(CanonicalPartMirrorPlaneV7.XZ).ReflectPoint(new[]{10.0,20,30}),new[]{10.0,-20,30},"XZ");}
        private static void XY(){AssertV(PartReflectionTransformV7.CreateCanonical(CanonicalPartMirrorPlaneV7.XY).ReflectPoint(new[]{10.0,20,30}),new[]{10.0,20,-30},"XY");}
        private static void Double(){var t=PartReflectionTransformV7.CreateOriginAnchored(new[]{1.0,2,3});var p=new[]{4.25,-9.5,12.75};AssertV(t.ReflectPoint(t.ReflectPoint(p)),p,"Double reflection");}
        private static void OnPlane(){var t=PartReflectionTransformV7.CreateCanonical(CanonicalPartMirrorPlaneV7.YZ);var p=new[]{0.0,12,-7};if(!t.IsPointOnMirrorPlane(p))throw new InvalidOperationException("Point-on-plane failed");AssertV(t.ReflectPoint(p),p,"On plane");}
        private static void Length(){var t=PartReflectionTransformV7.CreateOriginAnchored(new[]{2.0,-1,4});var p=new[]{3.0,5,-7};AssertNearly(Len(p),Len(t.ReflectVector(p)),"Vector length");}
        private static void Arbitrary(){var t=PartReflectionTransformV7.CreateOriginAnchored(new[]{1.0,1,0});AssertV(t.ReflectPoint(new[]{5.0,-5,8}),new[]{5.0,-5,8},"Arbitrary plane");}
        private static void Selector(){if(CanonicalPartMirrorPlaneSelectorV7.FromLocalNormal(new[]{.95,.1,.2})!=CanonicalPartMirrorPlaneV7.YZ)throw new Exception("Selector X");if(CanonicalPartMirrorPlaneSelectorV7.FromLocalNormal(new[]{.1,-.98,.05})!=CanonicalPartMirrorPlaneV7.XZ)throw new Exception("Selector Y");if(CanonicalPartMirrorPlaneSelectorV7.FromLocalNormal(new[]{.1,.2,.99})!=CanonicalPartMirrorPlaneV7.XY)throw new Exception("Selector Z");}
        private static double Len(double[] v){return Math.Sqrt(v[0]*v[0]+v[1]*v[1]+v[2]*v[2]);}
        private static void AssertV(double[] a,double[] e,string n){for(int i=0;i<3;i++)AssertNearly(a[i],e[i],n+"["+i+"]");}
        private static void AssertNearly(double a,double e,string n){if(Math.Abs(a-e)>T)throw new InvalidOperationException(n+" failed; expected="+e+" actual="+a);}
    }
}
