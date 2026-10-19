using System;
using System.Globalization;

namespace ADDIN.AutoFrameJointResearch
{
    // Standalone geometry kernel; input units = millimetres.
    // This file intentionally has NO SOLIDWORKS API references and NO write actions.
    public struct V3
    {
        public readonly double X, Y, Z;
        public V3(double x, double y, double z) { X=x; Y=y; Z=z; }
        public static V3 operator +(V3 a,V3 b) { return new V3(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
        public static V3 operator -(V3 a,V3 b) { return new V3(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        public static V3 operator *(V3 a,double k) { return new V3(a.X*k,a.Y*k,a.Z*k); }
        public double Norm { get { return Math.Sqrt(X*X+Y*Y+Z*Z); } }
        public V3 Unit()
        {
            if (Norm < 1e-12) throw new ArgumentException("Zero-length vector.");
            return this*(1.0/Norm);
        }
        public static double Dot(V3 a,V3 b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
        public static V3 Cross(V3 a,V3 b) { return new V3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X); }
        public override string ToString() { return string.Format(CultureInfo.InvariantCulture,"({0:0.###},{1:0.###},{2:0.###})",X,Y,Z); }
    }

    public enum FrameLengthRole { Long, Short, EqualOrAmbiguous }
    public enum RuleApplicability { Applicable, NotApplicable, Unknown }
    public enum FrameWinner { A, B, Undetermined }
    public enum WinnerEvidence { DrawingOverride, Kcc26003, SquareConvention, Unresolved }
    public enum JointSolveStatus { Analyzed, ParallelAxes, NonPlanar, OutsideSegments, InvalidInput }

    public sealed class FrameSpan
    {
        public readonly string Id;
        public readonly V3 Start, End;
        public FrameSpan(string id, V3 start, V3 end)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Frame id required.");
            if ((end-start).Norm < 1e-9) throw new ArgumentException("Zero-length frame.");
            Id=id; Start=start; End=end;
        }
        public double LengthMm { get { return (End-Start).Norm; } }
        public V3 Axis { get { return (End-Start).Unit(); } }
    }

    public sealed class JointInputs
    {
        public FrameSpan A, B;
        public V3 PanelNormal;
        // Required explicitly for each analysis; do not hardcode production tolerances.
        public double LengthTieToleranceMm;
        public double PositionToleranceMm;
        public double AngleParallelToleranceDeg;
        public RuleApplicability Kcc26003Scope = RuleApplicability.Unknown;
        public FrameWinner DrawingOverride = FrameWinner.Undetermined;
        public FrameWinner SquareConvention = FrameWinner.Undetermined;
    }

    public sealed class JointResult
    {
        public JointSolveStatus Status;
        public string Reason;
        public double AxisAcuteAngleDeg;
        public double LengthAMm, LengthBMm;
        public FrameLengthRole RoleA, RoleB;
        public V3 AxisIntersection;
        public FrameWinner Winner;
        public WinnerEvidence WinnerSource;
        public bool CanAutomaticallyChooseCut; // ALWAYS false in Phase 1.
    }

    public static class JointGeometry
    {
        private static double Clamp(double x,double lo,double hi) { return Math.Max(lo,Math.Min(hi,x)); }

        // System.Double.IsFinite is not available in .NET Framework 4.8.
        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(V3 value)
        {
            return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
        }

        private static JointResult Invalid(string reason)
        {
            return new JointResult {
                Status=JointSolveStatus.InvalidInput,
                Reason=reason,
                Winner=FrameWinner.Undetermined,
                WinnerSource=WinnerEvidence.Unresolved,
                CanAutomaticallyChooseCut=false
            };
        }

        public static JointResult Analyze(JointInputs x)
        {
            if (x==null || x.A==null || x.B==null)
                return Invalid("Two frames required.");

            // Reject NaN/Infinity before comparisons and vector normalization.
            // Comparisons such as NaN <= 0 are FALSE and therefore insufficient.
            if (!IsFinite(x.PositionToleranceMm) ||
                !IsFinite(x.LengthTieToleranceMm) ||
                !IsFinite(x.AngleParallelToleranceDeg) ||
                x.PositionToleranceMm<=0 || x.LengthTieToleranceMm<0 ||
                x.AngleParallelToleranceDeg<=0 || x.AngleParallelToleranceDeg>=90)
                return Invalid("Non-finite or out-of-range tolerances.");

            if (!IsFinite(x.A.Start) || !IsFinite(x.A.End) ||
                !IsFinite(x.B.Start) || !IsFinite(x.B.End) ||
                !IsFinite(x.PanelNormal))
                return Invalid("Non-finite frame coordinates or panel normal.");

            double lengthA=x.A.LengthMm, lengthB=x.B.LengthMm;
            double normalLength=x.PanelNormal.Norm;
            if (!IsFinite(lengthA) || !IsFinite(lengthB) ||
                !IsFinite(normalLength) ||
                lengthA<1e-9 || lengthB<1e-9 || normalLength<1e-12)
                return Invalid("Degenerate or numerically overflowing geometry.");

            var n = x.PanelNormal.Unit();
            var da=x.A.Axis;
            var db=x.B.Axis;
            var result = new JointResult {
                LengthAMm=x.A.LengthMm,
                LengthBMm=x.B.LengthMm,
                CanAutomaticallyChooseCut=false,
                Winner=FrameWinner.Undetermined,
                WinnerSource=WinnerEvidence.Unresolved
            };

            var diff=result.LengthAMm-result.LengthBMm;
            result.RoleA=Math.Abs(diff)<=x.LengthTieToleranceMm ? FrameLengthRole.EqualOrAmbiguous :
                         diff>0 ? FrameLengthRole.Long : FrameLengthRole.Short;
            result.RoleB=Math.Abs(diff)<=x.LengthTieToleranceMm ? FrameLengthRole.EqualOrAmbiguous :
                         diff>0 ? FrameLengthRole.Short : FrameLengthRole.Long;

            // Winner is a design rule with scope and precedence, not a purely geometric result.
            if (x.DrawingOverride!=FrameWinner.Undetermined)
            {
                result.Winner=x.DrawingOverride;
                result.WinnerSource=WinnerEvidence.DrawingOverride;
            }
            else if (x.Kcc26003Scope==RuleApplicability.Applicable)
            {
                if (result.RoleA==FrameLengthRole.Short) result.Winner=FrameWinner.A;
                else if (result.RoleB==FrameLengthRole.Short) result.Winner=FrameWinner.B;
                else if (x.SquareConvention!=FrameWinner.Undetermined)
                {
                    result.Winner=x.SquareConvention;
                    result.WinnerSource=WinnerEvidence.SquareConvention;
                }
                if (result.WinnerSource==WinnerEvidence.Unresolved &&
                    result.Winner!=FrameWinner.Undetermined)
                    result.WinnerSource=WinnerEvidence.Kcc26003;
            }

            result.AxisAcuteAngleDeg = Math.Acos(Clamp(Math.Abs(V3.Dot(da,db)),0,1))*180/Math.PI;
            if (Math.Abs(V3.Dot(da,n))>1e-6 || Math.Abs(V3.Dot(db,n))>1e-6 ||
                Math.Abs(V3.Dot(x.B.Start-x.A.Start,n))>x.PositionToleranceMm)
            {
                result.Status=JointSolveStatus.NonPlanar;
                result.Reason="Axes are not in a single panel plane; manual 3D analysis required.";
                return result;
            }

            double determinant=V3.Dot(V3.Cross(da,db),n);
            if (Math.Abs(determinant)<=Math.Sin(x.AngleParallelToleranceDeg*Math.PI/180))
            {
                result.Status=JointSolveStatus.ParallelAxes;
                result.Reason="Axis intersection is ambiguous for parallel/near-parallel axes.";
                return result;
            }

            V3 delta=x.B.Start-x.A.Start;
            if (!IsFinite(delta))
                return Invalid("Coordinate difference overflow.");

            double t=V3.Dot(V3.Cross(delta,db),n)/determinant;
            double u=V3.Dot(V3.Cross(delta,da),n)/determinant;
            if (!IsFinite(t) || !IsFinite(u))
                return Invalid("Axis intersection calculation overflow.");

            V3 pa=x.A.Start+da*t, pb=x.B.Start+db*u;
            if (!IsFinite(pa) || !IsFinite(pb))
                return Invalid("Axis intersection coordinate overflow.");

            result.AxisIntersection=(pa+pb)*0.5;
            double pointGap=(pa-pb).Norm;
            if (!IsFinite(result.AxisIntersection) || !IsFinite(pointGap))
                return Invalid("Axis intersection numeric overflow.");

            if (pointGap>x.PositionToleranceMm)
            {
                result.Status=JointSolveStatus.NonPlanar;
                result.Reason="Computed axis points do not coincide.";
            }
            else if(t< -x.PositionToleranceMm || t>result.LengthAMm+x.PositionToleranceMm ||
                    u< -x.PositionToleranceMm || u>result.LengthBMm+x.PositionToleranceMm)
            {
                result.Status=JointSolveStatus.OutsideSegments;
                result.Reason="Infinite axes intersect outside the supplied finite spans.";
            }
            else
            {
                result.Status=JointSolveStatus.Analyzed;
                result.Reason="Axis geometry analyzed; NOT a validated end-cut.";
            }
            return result;
        }
    }
}
