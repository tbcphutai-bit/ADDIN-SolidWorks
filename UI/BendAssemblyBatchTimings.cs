namespace ADDIN.UI
{
    // Durations are measured stages, never inferred by subtracting worker CPU
    // sums from wall time. Worker stages can overlap and exceed total wall.
    internal sealed class BendAssemblyBatchTimings
    {
        public double PlanningMilliseconds { get; set; }
        internal double NativeCaptureMilliseconds { get; set; }
        internal double WorkerMilliseconds { get; set; }
        internal double NumericSolverMilliseconds { get; set; }
        internal double SnapshotValidationMilliseconds { get; set; }
        internal double SnapshotCopyMilliseconds { get; set; }
        internal double ViewModelMilliseconds { get; set; }
        internal double GridRenderingMilliseconds { get; set; }
        internal double RunToVisibleMilliseconds { get; set; }
    }
}
