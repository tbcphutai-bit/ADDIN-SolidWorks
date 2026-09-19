using System.Collections.Generic;

namespace ADDIN.Commands.MirrorV7
{
    public sealed class SketchMirrorPlanItemV7
    {
        public string FeatureName { get; set; }
        public int TreeOrder { get; set; }
        public bool Is3D { get; set; }
        public bool IsSuppressed { get; set; }
        public bool IsDerived { get; set; }
        public bool IsShared { get; set; }
        public bool OwnershipUnresolved { get; set; }
        public int PointCount { get; set; }
        public int SegmentCount { get; set; }
        public int RelationCount { get; set; }
        public bool HasNonLineGeometry { get; set; }
        public bool HasFixedEntity { get; set; }
        public int MissingModelCoordinates { get; set; }
        public int SlotCount { get; set; }
        public int ExternalRelationCount { get; set; }
        public int DimensionCount { get; set; }
        public int MissingDimensionReferences { get; set; }
        public bool HasBendLine { get; set; }
        public bool PlaneTestCompleted { get; set; }
        public double MaximumTargetPlaneDistance { get; set; }
        public List<string> Blockers { get; private set; }
        public List<string> RequiredHandlers { get; private set; }
        public bool MutationReady { get { return false; } }
        public SketchMirrorPlanItemV7() { Blockers = new List<string>(); RequiredHandlers = new List<string>(); }
    }

    public sealed class SketchMirrorPlanV7
    {
        public CanonicalPartMirrorPlaneV7 Plane { get; set; }
        public List<SketchMirrorPlanItemV7> Items { get; private set; }
        public bool MutationEnabled { get { return false; } }
        public SketchMirrorPlanV7() { Items = new List<SketchMirrorPlanItemV7>(); }
    }
}
