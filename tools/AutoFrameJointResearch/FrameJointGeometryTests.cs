using System;

namespace ADDIN.AutoFrameJointResearch
{
    internal static class FrameJointGeometryTests
    {
        private static int count;
        private static void Check(bool ok, string name)
        {
            count++;
            if (!ok) throw new Exception("FAIL " + name);
            Console.WriteLine("PASS " + name);
        }
        private static void Near(double actual, double expected, double delta, string name)
        {
            Check(Math.Abs(actual-expected)<=delta,
                name+" (actual="+actual+" expected="+expected+")");
        }
        private static JointInputs Sample()
        {
            return new JointInputs {
                A=new FrameSpan("A",new V3(-1000,0,0),new V3(1000,0,0)),
                B=new FrameSpan("B",new V3(0,-500,0),new V3(0,500,0)),
                PanelNormal=new V3(0,0,1),
                LengthTieToleranceMm=0.5,        // TEST FIXTURE ONLY; not production rule.
                PositionToleranceMm=0.01,       // TEST FIXTURE ONLY; not production rule.
                AngleParallelToleranceDeg=0.1,  // TEST FIXTURE ONLY; not production rule.
                Kcc26003Scope=RuleApplicability.Applicable
            };
        }
        private static V3 RotateZ90(V3 v) { return new V3(-v.Y,v.X,v.Z); }
        public static int Run()
        {
            count=0;
            var a=Sample();
            var r=JointGeometry.Analyze(a);
            Check(r.Status==JointSolveStatus.Analyzed,"90deg valid");
            Check(r.RoleA==FrameLengthRole.Long && r.RoleB==FrameLengthRole.Short,"long/short");
            Check(r.Winner==FrameWinner.B && r.WinnerSource==WinnerEvidence.Kcc26003,"KCC short wins when applicable");
            Near(r.AxisAcuteAngleDeg,90,1e-7,"90deg angle");
            Near(r.AxisIntersection.Norm,0,1e-7,"90deg intersection");
            Check(!r.CanAutomaticallyChooseCut,"no auto-cut in Phase1");

            a.DrawingOverride=FrameWinner.A;
            r=JointGeometry.Analyze(a);
            Check(r.Winner==FrameWinner.A && r.WinnerSource==WinnerEvidence.DrawingOverride,"drawing wins over KCC");

            a.DrawingOverride=FrameWinner.Undetermined;
            a.Kcc26003Scope=RuleApplicability.Unknown;
            r=JointGeometry.Analyze(a);
            Check(r.Winner==FrameWinner.Undetermined,"unknown scope => undetermined");

            a.Kcc26003Scope=RuleApplicability.Applicable;
            a.B=new FrameSpan("B",new V3(0,-1000,0),new V3(0,1000,0));
            r=JointGeometry.Analyze(a);
            Check(r.RoleA==FrameLengthRole.EqualOrAmbiguous && r.Winner==FrameWinner.Undetermined,"equal length unresolved");
            a.SquareConvention=FrameWinner.A;
            r=JointGeometry.Analyze(a);
            Check(r.Winner==FrameWinner.A && r.WinnerSource==WinnerEvidence.SquareConvention,"square convention");

            a=Sample();
            a.A=new FrameSpan("A",RotateZ90(a.A.Start),RotateZ90(a.A.End));
            a.B=new FrameSpan("B",RotateZ90(a.B.Start),RotateZ90(a.B.End));
            r=JointGeometry.Analyze(a);
            Check(r.Status==JointSolveStatus.Analyzed && r.Winner==FrameWinner.B,"rotation invariant");

            a=Sample();
            double rad=30*Math.PI/180;
            V3 dir=new V3(Math.Cos(rad),Math.Sin(rad),0);
            a.B=new FrameSpan("B",dir*(-500),dir*500);
            r=JointGeometry.Analyze(a);
            Near(r.AxisAcuteAngleDeg,30,1e-7,"30deg axis angle");
            Check(r.Status==JointSolveStatus.Analyzed,"30deg valid");

            a=Sample();
            a.B=new FrameSpan("B",new V3(-500,20,0),new V3(500,20,0));
            r=JointGeometry.Analyze(a);
            Check(r.Status==JointSolveStatus.ParallelAxes,"parallel safely rejected");

            a=Sample();
            a.B=new FrameSpan("B",new V3(0,-500,2),new V3(0,500,2));
            r=JointGeometry.Analyze(a);
            Check(r.Status==JointSolveStatus.NonPlanar,"noncoplanar safely rejected");

            a=Sample();
            a.A=new FrameSpan("A",new V3(1000,1000,0),new V3(2000,1000,0));
            r=JointGeometry.Analyze(a);
            Check(r.Status==JointSolveStatus.OutsideSegments,"outside finite spans safely rejected");

            a=Sample();
            a.PositionToleranceMm=0;
            r=JointGeometry.Analyze(a);
            Check(r.Status==JointSolveStatus.InvalidInput,"invalid tolerance safely rejected");

            Console.WriteLine("TOTAL PASS: "+count);
            return count;
        }
    }
}
