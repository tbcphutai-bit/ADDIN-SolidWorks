using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    public sealed class MirrorInPlaceExecutionContextV7
    {
        public ISldWorks SwApp { get; set; }
        public ModelDoc2 SourceDocument { get; set; }
        public ModelDoc2 WorkingDocument { get; set; }
        public MirrorSourceBaselineV7 SourceBaseline { get; set; }
        public MirrorV7ModelGraph SourceGraph { get; set; }
        public MirrorV7ModelGraph WorkingGraph { get; set; }
        public PartReflectionTransformV7 Reflection { get; set; }
        public CanonicalPartMirrorPlaneV7 Plane { get; set; }
        public string WorkingPath { get; set; }
        public string RequestedOutputPath { get; set; }
        public BaseFeaturePrescriptionV7 BasePrescription { get; set; }
        public BaseMutationResultV7 BaseMutation { get; set; }
        public FeatureReplayCheckpointV7 ReplayCheckpoint { get; set; }
    }

    public sealed class MirrorInPlacePreparationResultV7
    {
        public string WorkingPath { get; internal set; }
        public string ReportPath { get; internal set; }
        public string RequestedOutputPath { get; internal set; }
        public int FeatureCount { get; internal set; }
        public int PlannedHandlerCount { get; internal set; }
        public int UnsupportedCount { get; internal set; }
        public bool SourceUnchanged { get; internal set; }
        public bool MutationExecuted { get; internal set; }
        public bool BaseGeometryVerified { get; internal set; }
        public bool OutputPublished { get; internal set; }
    }

    public sealed class FeatureReplayJournalV7
    {
        private readonly List<MirrorV7FeatureResult> entries = new List<MirrorV7FeatureResult>();
        public IList<MirrorV7FeatureResult> Entries { get { return entries.AsReadOnly(); } }
        public void Add(MirrorV7FeatureResult result)
        {
            if (result == null) throw new ArgumentNullException("result");
            entries.Add(result);
        }
    }
}
