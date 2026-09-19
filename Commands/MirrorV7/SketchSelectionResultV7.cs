using System;

namespace ADDIN.Commands.MirrorV7
{
    public enum SketchSelectionStatusV7 { Selected, NoEligibleSketch, UserCancelled }

    public sealed class SketchSelectionResultV7
    {
        public SketchSelectionStatusV7 Status { get; private set; }
        public MirrorV7FeatureNode Node { get; private set; }
        private SketchSelectionResultV7(SketchSelectionStatusV7 status, MirrorV7FeatureNode node) { Status = status; Node = node; }
        public static SketchSelectionResultV7 Selected(MirrorV7FeatureNode node) { if (node == null) throw new ArgumentNullException("node"); return new SketchSelectionResultV7(SketchSelectionStatusV7.Selected, node); }
        public static SketchSelectionResultV7 NoEligible() { return new SketchSelectionResultV7(SketchSelectionStatusV7.NoEligibleSketch, null); }
        public static SketchSelectionResultV7 Cancelled() { return new SketchSelectionResultV7(SketchSelectionStatusV7.UserCancelled, null); }
    }
}
