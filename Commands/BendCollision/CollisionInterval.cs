using System;
using System.Linq;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendCollision
{
    // Binary64 arithmetic widened after EVERY operation. Curve-distance lower
    // certificates use these enclosures, not an analytic intersection guess.
    internal struct CollisionInterval
    {
        internal readonly double Lo,Hi;
        internal CollisionInterval(double lo,double hi)
        { if(!ToolPoint.Finite(lo)||!ToolPoint.Finite(hi)||lo>hi)throw new CollisionAbort("UNRESOLVED_NUMERICAL_INTERVAL");Lo=lo;Hi=hi; }
        internal static CollisionInterval P(double n)=>new CollisionInterval(n,n);
        internal static double Up(double n)
        { if(!ToolPoint.Finite(n))throw new CollisionAbort("UNRESOLVED_NUMERICAL_INTERVAL");return n==0?double.Epsilon:BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(n)+(n>0?1:-1)); }
        internal static double Down(double n)=>-Up(-n);
        public static CollisionInterval operator +(CollisionInterval a,CollisionInterval b)=>new CollisionInterval(Down(a.Lo+b.Lo),Up(a.Hi+b.Hi));
        public static CollisionInterval operator -(CollisionInterval a,CollisionInterval b)=>new CollisionInterval(Down(a.Lo-b.Hi),Up(a.Hi-b.Lo));
        public static CollisionInterval operator *(CollisionInterval a,CollisionInterval b)
        { var v=new[]{a.Lo*b.Lo,a.Lo*b.Hi,a.Hi*b.Lo,a.Hi*b.Hi};return new CollisionInterval(Down(v.Min()),Up(v.Max())); }
        public static CollisionInterval operator /(CollisionInterval a,CollisionInterval b)
        { if(b.Lo<=0&&b.Hi>=0)throw new CollisionAbort("UNRESOLVED_NUMERICAL_DENOMINATOR");var v=new[]{a.Lo/b.Lo,a.Lo/b.Hi,a.Hi/b.Lo,a.Hi/b.Hi};return new CollisionInterval(Down(v.Min()),Up(v.Max())); }
        internal CollisionInterval Square()=>Lo<=0&&Hi>=0?new CollisionInterval(0,Up(Math.Max(Lo*Lo,Hi*Hi))):
            new CollisionInterval(Down(Math.Min(Lo*Lo,Hi*Hi)),Up(Math.Max(Lo*Lo,Hi*Hi)));
        internal CollisionInterval Sqrt()=>new CollisionInterval(Math.Max(0,Down(Math.Sqrt(Math.Max(0,Lo)))),Up(Math.Sqrt(Math.Max(0,Hi))));
        internal static CollisionInterval Abs(CollisionInterval x)=>x.Lo>=0?x:x.Hi<=0?new CollisionInterval(-x.Hi,-x.Lo):new CollisionInterval(0,Math.Max(-x.Lo,x.Hi));
        internal static void SinCos(CollisionInterval angle,out CollisionInterval sin,out CollisionInterval cos)
        {
            if(Math.Max(Math.Abs(angle.Lo),Math.Abs(angle.Hi))>7)throw new CollisionAbort("UNSUPPORTED_TRIG_INTERVAL_DOMAIN");
            var square=angle.Square();var st=angle;var ct=P(1);sin=st;cos=ct;
            for(int k=1;k<=32;k++)
            {
                st=(P(0)-st)*square/P((2*k)*(2*k+1));sin=sin+st;
                ct=(P(0)-ct)*square/P((2*k-1)*(2*k));cos=cos+ct;
            }
            // Lagrange remainder at |angle|<=7 is below 1e-30 for these
            // degrees; 1e-24 is an explicit outward upper allowance.
            sin=sin+new CollisionInterval(-1e-24,1e-24);cos=cos+new CollisionInterval(-1e-24,1e-24);
        }
    }
}
