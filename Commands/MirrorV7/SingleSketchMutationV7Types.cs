using System.Collections.Generic;

namespace ADDIN.Commands.MirrorV7
{
    public sealed class SketchPointTargetV7
    {
        public PersistReferenceV7 Reference { get; set; }
        public double[] BeforeSketch { get; set; }
        public double[] BeforeModel { get; set; }
        public double[] TargetSketch { get; set; }
        public double[] TargetModel { get; set; }
    }

    public sealed class SingleSketchMutationResultV7
    {
        public int PointCount { get; set; }
        public int MovingPointCount { get; set; }
        public double MaximumErrorMetres { get; set; }
        public bool PostEditVerified { get; set; }
        public bool PostRebuildVerified { get; set; }
        public bool SourceUnchanged { get; set; }
        public bool PartialMutation { get; set; }
        public bool SketchMutationVerified { get; set; }
        public bool FullPartMirrorVerified { get { return false; } }
        public List<string> Errors { get; private set; }
        public SingleSketchMutationResultV7() { Errors = new List<string>(); }
        public bool Success { get { return MovingPointCount > 0 && PostEditVerified && PostRebuildVerified && SourceUnchanged && Errors.Count == 0; } }
    }
}
