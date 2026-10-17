using System;
using System.IO;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlacement
{
    internal static class NativeEdgeParameterPolicy
    {
        // These are EDGE parameters, including negative parameter space for
        // reverse-sense edges. Pass the result to IEdge.Evaluate2, not ICurve.
        internal static double Midpoint(double min,double max)
        {
            if(!ToolPoint.Finite(min)||!ToolPoint.Finite(max)||max<=min)throw new InvalidDataException("Invalid native edge parameter interval.");
            double middle=min/2+max/2;
            if(!ToolPoint.Finite(middle))throw new InvalidDataException("Invalid native midpoint parameter.");
            return middle;
        }
        internal static bool EvaluationSucceeded(double[] evaluation)=>evaluation!=null&&evaluation.Length>=4&&
            (BitConverter.DoubleToInt64Bits(evaluation[3])&0xffffffffL)==1;
    }
    internal struct PlacementVector3
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        internal PlacementVector3(double x, double y, double z)
        {
            if (!ToolPoint.Finite(x) || !ToolPoint.Finite(y) || !ToolPoint.Finite(z)) throw new InvalidDataException("Non-finite placement vector.");
            X=x; Y=y; Z=z;
        }
        public static PlacementVector3 operator +(PlacementVector3 a, PlacementVector3 b) => new PlacementVector3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static PlacementVector3 operator -(PlacementVector3 a, PlacementVector3 b) => new PlacementVector3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static PlacementVector3 operator *(PlacementVector3 a, double scale) => new PlacementVector3(a.X*scale,a.Y*scale,a.Z*scale);
        internal double Length => Math.Sqrt(Dot(this,this));
        internal PlacementVector3 Unit => Length>1e-12 ? this*(1/Length) : throw new InvalidDataException("Zero direction.");
        internal static double Dot(PlacementVector3 a, PlacementVector3 b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        internal static PlacementVector3 Cross(PlacementVector3 a, PlacementVector3 b) => new PlacementVector3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        internal static PlacementVector3 Metres(double x,double y,double z) => new PlacementVector3(x*1000,y*1000,z*1000);
    }

    // Lengths are mm. Columns are orthonormal; determinant is exactly +/-1
    // within 1e-10. Mirroring requires explicit permission, never a guess.
    internal sealed class PlacementTransform2D
    {
        public double A { get; }
        public double B { get; }
        public double C { get; }
        public double D { get; }
        public double TxMm { get; }
        public double TyMm { get; }
        public double Determinant => A*D-B*C;
        public bool Mirrored => Determinant<0;
        internal PlacementTransform2D(double a=1,double b=0,double c=0,double d=1,double txMm=0,double tyMm=0,bool allowMirror=false)
        {
            if (!ToolPoint.Finite(txMm)||!ToolPoint.Finite(tyMm)||!ToolPoint.Finite(a)||!ToolPoint.Finite(b)||!ToolPoint.Finite(c)||!ToolPoint.Finite(d)||
                Math.Abs(a*a+b*b-1)>1e-10||Math.Abs(c*c+d*d-1)>1e-10||Math.Abs(a*c+b*d)>1e-10||
                Math.Abs(Math.Abs(a*d-b*c)-1)>1e-10||!allowMirror&&a*d-b*c<0) throw new InvalidDataException("Invalid rigid 2D placement/mirror permission.");
            A=a;B=b;C=c;D=d;TxMm=txMm;TyMm=tyMm;
        }
        internal ToolPoint Apply(ToolPoint p) => new ToolPoint(A*p.X+C*p.Y+TxMm,B*p.X+D*p.Y+TyMm);
        internal ToolPoint Direction(ToolPoint p) => new ToolPoint(A*p.X+C*p.Y,B*p.X+D*p.Y);
        // parent.Then(local) maps p -> parent(local(p)).
        internal PlacementTransform2D Then(PlacementTransform2D local) => new PlacementTransform2D(
            A*local.A+C*local.B,B*local.A+D*local.B,A*local.C+C*local.D,B*local.C+D*local.D,
            A*local.TxMm+C*local.TyMm+TxMm,B*local.TxMm+D*local.TyMm+TyMm,true);
        internal PlacementTransform2D Inverse() => new PlacementTransform2D(A,C,B,D,-A*TxMm-B*TyMm,-C*TxMm-D*TyMm,true);
        internal ToolCurve Apply(ToolCurve curve)
        {
            var m=new ToolTransform(A,B,C,D,TxMm,TyMm);
            return curve.Kind=="LINE" ? ToolCurve.Line(curve.Start,curve.End,m,curve.SourceSegment) :
                ToolCurve.Arc(curve.Start,curve.End,curve.Center,curve.RadiusMm,curve.SweepRadians,m,curve.SourceSegment);
        }
        internal static PlacementTransform2D Align(ToolPoint sourceTip,ToolPoint sourceWorkingDirection,ToolPoint targetContact,ToolPoint targetApproach,bool mirror=false)
        {
            Unit(sourceWorkingDirection);Unit(targetApproach);
            double sx=sourceWorkingDirection.X*(mirror?-1:1),sy=sourceWorkingDirection.Y;
            double a=Math.Atan2(targetApproach.Y,targetApproach.X)-Math.Atan2(sy,sx),c=Math.Cos(a),s=Math.Sin(a),sign=mirror?-1:1;
            double aa=c*sign,bb=s*sign,cc=-s,dd=c;
            return new PlacementTransform2D(aa,bb,cc,dd,targetContact.X-aa*sourceTip.X-cc*sourceTip.Y,targetContact.Y-bb*sourceTip.X-dd*sourceTip.Y,mirror);
        }
        internal static void Unit(ToolPoint p)
        { if (Math.Abs(p.X*p.X+p.Y*p.Y-1)>1e-10) throw new InvalidDataException("Placement direction must be supplied as a unit vector."); }
    }

    internal sealed class BendWorkFrame
    {
        public PlacementVector3 OriginMm { get; }
        public PlacementVector3 X { get; }
        public PlacementVector3 Y { get; }
        public PlacementVector3 BendAxis { get; }
        public string FrameId { get; }
        // A computational section frame does not identify the machine's punch
        // approach or a manufacturing bend direction in an unknown sequence.
        public bool MachineApproachVerified => false;
        internal BendWorkFrame(string id,PlacementVector3 origin,PlacementVector3 x,PlacementVector3 y,PlacementVector3 axis)
        {
            if (Math.Abs(x.Length-1)>1e-10||Math.Abs(y.Length-1)>1e-10||Math.Abs(axis.Length-1)>1e-10||
                Math.Abs(PlacementVector3.Dot(x,y))>1e-10||Math.Abs(PlacementVector3.Dot(x,axis))>1e-10||
                Math.Abs(PlacementVector3.Dot(y,axis))>1e-10||Math.Abs(PlacementVector3.Dot(PlacementVector3.Cross(x,y),axis)-1)>1e-10)
                throw new InvalidDataException("Invalid right-handed section frame.");
            FrameId=id;OriginMm=origin;X=x;Y=y;BendAxis=axis;
        }
        internal static BendWorkFrame Observed(string id,PlacementVector3 origin,PlacementVector3 axis)
        {
            axis=axis.Unit;var basis=Math.Abs(axis.X)<0.8?new PlacementVector3(1,0,0):new PlacementVector3(0,1,0);
            var x=(basis-axis*PlacementVector3.Dot(basis,axis)).Unit;
            return new BendWorkFrame(id,origin,x,PlacementVector3.Cross(axis,x).Unit,axis);
        }
        internal ToolPoint Project(PlacementVector3 worldMm) => new ToolPoint(PlacementVector3.Dot(worldMm-OriginMm,X),PlacementVector3.Dot(worldMm-OriginMm,Y));
        internal double AxialDistance(PlacementVector3 worldMm) => PlacementVector3.Dot(worldMm-OriginMm,BendAxis);
        internal PlacementVector3 World(ToolPoint sectionPoint,double axialMm=0) => OriginMm+X*sectionPoint.X+Y*sectionPoint.Y+BendAxis*axialMm;
    }
}
