using System;

namespace ADDIN.AutoFrameJointResearch
{
    // Regression tests for numeric validation. Kept separate from Copilot's
    // locally edited FrameJointGeometryTests.cs to avoid overwriting its work.
    internal static class FrameJointNumericTests
    {
        private static int passed;

        private static void MustReject(JointInputs input, string label)
        {
            JointResult r=JointGeometry.Analyze(input);
            if (r.Status!=JointSolveStatus.InvalidInput ||
                r.Winner!=FrameWinner.Undetermined ||
                r.CanAutomaticallyChooseCut)
                throw new InvalidOperationException("FAIL "+label+" got "+r.Status+"/"+r.Winner);
            passed++;
            Console.WriteLine("PASS "+label);
        }

        private static JointInputs Good()
        {
            return new JointInputs {
                A=new FrameSpan("A", new V3(-1000,0,0), new V3(1000,0,0)),
                B=new FrameSpan("B", new V3(0,-500,0), new V3(0,500,0)),
                PanelNormal=new V3(0,0,1),
                LengthTieToleranceMm=0.5,       // Fixture only, not a production tolerance.
                PositionToleranceMm=0.01,
                AngleParallelToleranceDeg=0.1,
                Kcc26003Scope=RuleApplicability.Applicable
            };
        }

        public static int Run()
        {
            passed=0;
            JointInputs x=Good();
            x.LengthTieToleranceMm=double.NaN;
            MustReject(x, "NaN length tie tolerance rejected");

            x=Good(); x.LengthTieToleranceMm=double.PositiveInfinity;
            MustReject(x, "positive infinity length tolerance rejected");

            x=Good(); x.LengthTieToleranceMm=double.NegativeInfinity;
            MustReject(x, "negative infinity length tolerance rejected");

            x=Good(); x.PositionToleranceMm=double.PositiveInfinity;
            MustReject(x, "positive infinity position tolerance rejected");

            x=Good(); x.PositionToleranceMm=double.NaN;
            MustReject(x, "NaN position tolerance rejected");

            x=Good(); x.AngleParallelToleranceDeg=double.NaN;
            MustReject(x, "NaN angle tolerance rejected");

            x=Good(); x.AngleParallelToleranceDeg=double.NegativeInfinity;
            MustReject(x, "negative infinity angle tolerance rejected");

            x=Good(); x.PanelNormal=new V3(double.NaN,0,1);
            MustReject(x, "NaN panel normal rejected");

            x=Good(); x.PanelNormal=new V3(0,double.PositiveInfinity,1);
            MustReject(x, "infinite panel normal rejected");

            x=Good(); x.A=new FrameSpan("A",new V3(double.NaN,0,0),new V3(1000,0,0));
            MustReject(x, "NaN frame start rejected");

            x=Good(); x.B=new FrameSpan("B",new V3(0,-500,0),new V3(0,double.PositiveInfinity,0));
            MustReject(x, "infinite frame endpoint rejected");

            x=Good(); x.A=new FrameSpan("A",new V3(-1e308,0,0),new V3(1e308,0,0));
            MustReject(x, "overflowing frame length rejected");

            MustReject(null, "null input rejected");

            x=Good();
            JointResult valid=JointGeometry.Analyze(x);
            if(valid.Status!=JointSolveStatus.Analyzed || valid.Winner!=FrameWinner.B ||
               valid.CanAutomaticallyChooseCut)
                throw new InvalidOperationException("FAIL valid geometry regression");
            passed++;
            Console.WriteLine("PASS valid geometry remains analyzed");
            Console.WriteLine("NUMERIC GUARD PASS: "+passed);
            return passed;
        }
    }
}
