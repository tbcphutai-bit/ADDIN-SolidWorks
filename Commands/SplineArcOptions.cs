namespace ADDIN.Commands
{
    internal sealed class SplineArcOptions
    {
        public bool AutomaticStep { get; set; } = true;

        public int ManualSegmentCount { get; set; } = 4;

        public double MaximumDeviationMm { get; set; } = 0.10;

        public bool SplitWhenOverTolerance { get; set; } = true;

        public bool AddRadiusDimensions { get; set; } = true;

        public bool AddStepDimensions { get; set; } = true;

        public double MaximumRadiusMm { get; set; } = 300;
    }
}
