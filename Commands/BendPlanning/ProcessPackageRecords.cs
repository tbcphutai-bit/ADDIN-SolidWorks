using System.Collections.Generic;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;

namespace ADDIN.Commands.BendPlanning
{
    // External JSON records only. Never expose these mutable objects as proofs.
    // Units are mm/radians; all mandatory numeric values are nullable so an
    // absent value cannot silently become zero or a valid identity transform.
    internal sealed class ProcessPackageRecord
    {
        public int Version { get; set; }
        public string Revision { get; set; }
        public string Configuration { get; set; }
        public string ModelSnapshotId { get; set; }
        public int? ModelUpdateStamp { get; set; }
        public string NativeGeometrySha256 { get; set; }
        public List<ProcessNativeBendRecord> Bends { get; set; }
        public List<ProcessArtifactRecord> Artifacts { get; set; }
        public ProcessRegistrationRecord Registration { get; set; }
        public List<ProcessPrismRecord> Geometry { get; set; }
        public List<ProcessStateRecord> States { get; set; }
        public List<ProcessTransitionRecord> Transitions { get; set; }
        public string InitialStateId { get; set; }
        public string SpecificationArtifactId { get; set; }
        public bool? DeclaredAlternativesComplete { get; set; }
        public ToolReviewProof GraphReview { get; set; }
        public ToolReviewProof PartLocalUseReview { get; set; }
    }
    internal sealed class ProcessNativeBendRecord
    {
        public int? FeatureId { get; set; }
        public string PersistentId { get; set; }
        public string NativeFrameId { get; set; }
    }
    internal sealed class ProcessArtifactRecord
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public string Sha256 { get; set; }
        public string Role { get; set; }
    }
    internal sealed class ProcessRegistrationRecord
    {
        public string FrameId { get; set; }
        public double[] OriginWorldMm { get; set; }
        public double[] X { get; set; }
        public double[] Y { get; set; }
        public double[] BendAxis { get; set; }
        public double? ErrorMm { get; set; }
        public string SourceArtifactId { get; set; }
        public ToolReviewProof Review { get; set; }
    }
    internal sealed class ProcessCurveRecord
    {
        public string Kind { get; set; }
        public double[] StartMm { get; set; }
        public double[] EndMm { get; set; }
        public double[] CenterMm { get; set; }
        public double? RadiusMm { get; set; }
        public double? SweepRadians { get; set; }
    }
    internal sealed class ProcessContourRecord
    {
        public string Id { get; set; }
        public double? UncertaintyMm { get; set; }
        public List<ProcessCurveRecord> Curves { get; set; }
    }
    internal sealed class ProcessMaterialRecord
    {
        public List<ProcessContourRecord> Contours { get; set; }
        public List<ProcessMaterialRegionRecord> Regions { get; set; }
    }
    internal sealed class ProcessMaterialRegionRecord
    {
        public string OuterId { get; set; }
        public string[] HoleIds { get; set; }
    }
    internal sealed class ProcessPrismRecord
    {
        public string Id { get; set; }
        public string SourceArtifactId { get; set; }
        public string SectionContract { get; set; }
        public double? StartMm { get; set; }
        public double? EndMm { get; set; }
        public double? ToleranceMm { get; set; }
        public ProcessMaterialRecord Material { get; set; }
    }
    internal sealed class ProcessStateRecord
    {
        public string Id { get; set; }
        public string GeometryPoseSha256 { get; set; }
        public int[] CompletedFeatureIds { get; set; }
    }
    internal sealed class ProcessMotionRecord
    {
        public double[] PivotMm { get; set; }
        public double[] StartTranslationMm { get; set; }
        public double[] EndTranslationMm { get; set; }
        public double? StartAngleRadians { get; set; }
        public double? EndAngleRadians { get; set; }
        public double? StartAxialMm { get; set; }
        public double? EndAxialMm { get; set; }
        public double? PositionErrorMm { get; set; }
        public double? AngleErrorRadians { get; set; }
        public double? AxialErrorMm { get; set; }
    }
    internal sealed class ProcessPanelRecord
    {
        public string Id { get; set; }
        public string GeometryId { get; set; }
        public string Role { get; set; }
        public string Fidelity { get; set; }
        public ProcessMotionRecord Motion { get; set; }
    }
    internal sealed class ProcessToolRecord
    {
        public CadReferenceSimulationRecord CadReferenceSimulation { get; set; }
        public ToolAxialSetupRecord AxialSetup { get; set; }
        public ProcessMotionRecord Motion { get; set; }
        // Explicitly reviewed software fixtures only. This path is forbidden
        // for external physical authority, even on a native captured Part.
        public ProcessPrismRecord SimulationGeometry { get; set; }
        public ToolReviewProof SimulationReview { get; set; }
    }
    internal sealed class ProcessSegmentRecord
    {
        public string Id { get; set; }
        public string Phase { get; set; }
        public string ConstraintsArtifactId { get; set; }
        public List<ProcessPanelRecord> Panels { get; set; }
        public ProcessToolRecord Punch { get; set; }
        public ProcessToolRecord Holder { get; set; }
        public List<ProcessContactRecord> Contacts { get; set; }
    }
    internal sealed class ProcessContactRecord
    {
        public string PartId { get; set; }
        public string SheetGeometrySha256 { get; set; }
        public string ToolSetupSha256 { get; set; }
        public string EvidenceArtifactId { get; set; }
        public bool? NormalIsX { get; set; }
        public bool? SheetPositiveSide { get; set; }
        public double? PlaneMm { get; set; }
        public double? TangentMinMm { get; set; }
        public double? TangentMaxMm { get; set; }
        public double? AxialStartMm { get; set; }
        public double? AxialEndMm { get; set; }
    }
    internal sealed class ProcessTransitionRecord
    {
        public string Id { get; set; }
        public string ToolId { get; set; }
        public int? FeatureId { get; set; }
        public string PersistentId { get; set; }
        public string BeforeStateId { get; set; }
        public string AfterStateId { get; set; }
        public string SetupArtifactId { get; set; }
        public string ProcessArtifactId { get; set; }
        public List<ProcessSegmentRecord> Segments { get; set; }
        public ToolReviewProof Review { get; set; }
    }
}
