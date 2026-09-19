using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
namespace ADDIN.Commands.MirrorV7
{
    public sealed class MirrorV7Context
    {
        public ISldWorks SwApp { get; set; }
        public ModelDoc2 PartDoc { get; set; }
        public MirrorV7ModelGraph Graph { get; set; }
        public PartReflectionTransformV7 Reflection { get; set; }
        public bool AuditOnly { get; set; }
        public SketchMirrorPlanV7 SketchMirrorPlan { get; set; }
        public ModelDoc2 WorkingDocument { get; set; }
        public MirrorV7ModelGraph WorkingGraph { get; set; }
        public string WorkingPath { get; set; }
        public string WorkingRunId { get; set; }
        public MirrorSourceBaselineV7 SourceBaseline { get; set; }
        public CanonicalPartMirrorPlaneV7? SelectedPlane { get; set; }

        /// <summary>Phase 5A read-only sketch snapshots, in Feature Tree order.</summary>
        public List<SketchSnapshotV7> SketchSnapshots { get; private set; }

        public MirrorV7Context()
        {
            AuditOnly = true;
            SketchSnapshots = new List<SketchSnapshotV7>();
        }
    }
}
