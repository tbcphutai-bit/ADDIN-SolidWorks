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
        internal DependencyJournal64 Dependencies64 { get; set; }
        // Only explicitly recreated native features enter this run-scoped ledger.
        // Source persistent references remain untouched for source-to-output reporting.
        public Dictionary<int, PersistReferenceV7> ReplacementReferences { get; private set; }
        // Only explicitly registered replacement sketches may use a generated name.
        public Dictionary<int, string> ReplacementTargetNames { get; private set; }
        // Source profile order -> source native Edge Flange order. The replacement
        // sketch is a top-level driver inserted immediately before its flange.
        public Dictionary<int, int> ExternalFlangeProfiles { get; private set; }
        // Explicit regenerated OneBend -> EdgeFlange and UiBend -> OneBend relationships.
        public Dictionary<int, int> GeneratedBendOwners { get; private set; }
        public MirrorInPlaceExecutionContextV7()
        {
            ReplacementReferences = new Dictionary<int, PersistReferenceV7>();
            ReplacementTargetNames = new Dictionary<int, string>();
            ExternalFlangeProfiles = new Dictionary<int, int>();
            GeneratedBendOwners = new Dictionary<int, int>();
        }
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
