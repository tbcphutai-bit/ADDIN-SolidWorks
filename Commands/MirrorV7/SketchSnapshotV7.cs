using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ADDIN.Commands.MirrorV7
{
    /// <summary>Distinguishes the two read-only sketch entity families captured by Phase 5A.</summary>
    public enum SketchEntityKindV7
    {
        Point = 0,
        Segment = 1
    }

    /// <summary>
    /// Read-only capture of one sketch point or one sketch segment.
    /// Phase 5A never writes through this type; it is a snapshot only.
    /// </summary>
    public sealed class SketchEntitySnapshotV7
    {
        public SketchEntityKindV7 Kind { get; set; }
        public int Index { get; set; }

        /// <summary>Values returned by SketchPoint.GetID()/SketchSegment.GetID(). Zero when unavailable.</summary>
        public int Id1 { get; set; }
        public int Id2 { get; set; }
        public bool HasIds { get; set; }

        // ---- point data (sketch space and model space) ----
        public double SketchX { get; set; }
        public double SketchY { get; set; }
        public double SketchZ { get; set; }
        public double ModelX { get; set; }
        public double ModelY { get; set; }
        public double ModelZ { get; set; }
        public bool HasModelCoords { get; set; }
        public int PointType { get; set; }

        // ---- segment data ----
        /// <summary>SketchSegment.GetType(); maps to swSketchSegments_e.</summary>
        public int SegmentType { get; set; }
        public string SegmentName { get; set; }
        public int SegmentStatus { get; set; }
        public double Length { get; set; }
        public bool IsBendLine { get; set; }
        public bool BelongsToSlot { get; set; }

        // ---- shared ----
        public bool IsConstruction { get; set; }
        public int PersistRefBytes { get; set; }
        public string PersistRefSignature { get; set; }

        /// <summary>Stable per-entity identity key used by integrity validation and future remapping.</summary>
        public string EntityKey
        {
            get { return (Kind == SketchEntityKindV7.Point ? "P" : "S") + ":" + Id1 + "|" + Id2; }
        }

        public void AppendStructuralSignature(StringBuilder sb)
        {
            AppendIdentitySignature(sb);
            AppendGeometrySignature(sb);
            sb.Append(PersistRefBytes.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append('|');
        }

        /// <summary>
        /// Identity and enumeration only: the position in the returned array plus the GetID() pair.
        /// SolidWorks guarantees none of these for a slot's synthesized interior points, so a
        /// difference confined to this part is an API quirk rather than a geometry defect.
        /// </summary>
        public void AppendIdentitySignature(StringBuilder sb)
        {
            sb.Append(Kind == SketchEntityKindV7.Point ? "P" : "S").Append(';');
            sb.Append(Index.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(Id1.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(Id2.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(HasIds ? "1" : "0").Append(';');
        }

        /// <summary>
        /// Geometry and type only - no identity, no persist-ref byte count. A difference here means
        /// the same sketch is being read with different geometry, which is always geometry-affecting
        /// and stays fatal even on a slot sketch.
        /// </summary>
        public void AppendGeometrySignature(StringBuilder sb)
        {
            if (Kind == SketchEntityKindV7.Point)
            {
                Num(sb, SketchX); Num(sb, SketchY); Num(sb, SketchZ);
                Num(sb, ModelX); Num(sb, ModelY); Num(sb, ModelZ);
                sb.Append(HasModelCoords ? "1" : "0").Append(';');
                sb.Append(PointType.ToString(CultureInfo.InvariantCulture)).Append(';');
            }
            else
            {
                sb.Append(SegmentType.ToString(CultureInfo.InvariantCulture)).Append(';');
                sb.Append(SegmentName ?? "").Append(';');
                sb.Append(SegmentStatus.ToString(CultureInfo.InvariantCulture)).Append(';');
                Num(sb, Length);
                sb.Append(IsBendLine ? "1" : "0").Append(';');
                sb.Append(BelongsToSlot ? "1" : "0").Append(';');
            }
            sb.Append(IsConstruction ? "1" : "0").Append(';');
        }

        public string BuildGeometrySignature()
        {
            StringBuilder sb = new StringBuilder();
            AppendGeometrySignature(sb);
            return sb.ToString();
        }

        public string BuildIdentitySignature()
        {
            StringBuilder sb = new StringBuilder();
            AppendIdentitySignature(sb);
            return sb.ToString();
        }

        public string BuildStructuralSignature()
        {
            StringBuilder sb = new StringBuilder();
            AppendStructuralSignature(sb);
            return sb.ToString();
        }

        public void AppendPersistRefSignature(StringBuilder sb)
        {
            sb.Append(EntityKey).Append('=').Append(PersistRefSignature ?? "<none>").Append('|');
        }

        private static void Num(StringBuilder sb, double value)
        {
            sb.Append(value.ToString("G17", CultureInfo.InvariantCulture)).Append(';');
        }
    }

    /// <summary>
    /// Read-only capture of one sketch relation/constraint.
    /// The V7 core never calls DeleteAllRelations(); this record exists so that
    /// preservation can be proved rather than assumed.
    /// </summary>
    public sealed class SketchConstraintSnapshotV7
    {
        public int Index { get; set; }

        /// <summary>SketchRelation.GetRelationType(); maps to swConstraintType_e.</summary>
        public int RelationType { get; set; }
        public string RelationTypeName { get; set; }

        /// <summary>The swSketchRelationFilterType_e bucket this relation was read from.</summary>
        public int FilterSource { get; set; }

        public int EntityCount { get; set; }

        /// <summary>Per-entity swSketchRelationEntityTypes_e values.</summary>
        public List<int> EntityTypes { get; private set; }

        /// <summary>
        /// Best-effort identity of each participating entity. Sketch points/segments resolve to
        /// their V7 entity key; anything else resolves to its COM type name (plus feature name).
        /// </summary>
        public List<string> EntityKeys { get; private set; }

        public SketchConstraintSnapshotV7()
        {
            EntityTypes = new List<int>();
            EntityKeys = new List<string>();
        }

        public void AppendStructuralSignature(StringBuilder sb)
        {
            sb.Append(Index.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(RelationType.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(FilterSource.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(EntityCount.ToString(CultureInfo.InvariantCulture)).Append(';');
            for (int i = 0; i < EntityTypes.Count; i++)
                sb.Append(EntityTypes[i].ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(';');
            for (int i = 0; i < EntityKeys.Count; i++)
                sb.Append(EntityKeys[i] ?? "").Append(',');
            sb.Append('|');
        }

        public string BuildStructuralSignature()
        {
            StringBuilder sb = new StringBuilder();
            AppendStructuralSignature(sb);
            return sb.ToString();
        }
    }

    /// <summary>Read-only capture of one sketch dimension.</summary>
    public sealed class SketchDimensionSnapshotV7
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string FullName { get; set; }
        public double Value { get; set; }
        public double SystemValue { get; set; }

        /// <summary>DisplayDimension.Type2.</summary>
        public int DisplayType { get; set; }

        public int DrivenState { get; set; }
        public bool IsReference { get; set; }
        public bool IsDangling { get; set; }
        public int PersistRefBytes { get; set; }
        public string PersistRefSignature { get; set; }

        public void AppendStructuralSignature(StringBuilder sb)
        {
            sb.Append(Index.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(Name ?? "").Append(';');
            sb.Append(FullName ?? "").Append(';');
            Num(sb, Value); Num(sb, SystemValue);
            sb.Append(DisplayType.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(DrivenState.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(IsReference ? "1" : "0").Append(';');
            sb.Append(IsDangling ? "1" : "0").Append(';');
            sb.Append(PersistRefBytes.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append('|');
        }

        public string BuildStructuralSignature()
        {
            StringBuilder sb = new StringBuilder();
            AppendStructuralSignature(sb);
            return sb.ToString();
        }

        public void AppendPersistRefSignature(StringBuilder sb)
        {
            sb.Append(FullName ?? Name ?? ("#" + Index.ToString(CultureInfo.InvariantCulture)))
              .Append('=').Append(PersistRefSignature ?? "<none>").Append('|');
        }

        private static void Num(StringBuilder sb, double value)
        {
            sb.Append(value.ToString("G17", CultureInfo.InvariantCulture)).Append(';');
        }
    }

    /// <summary>Read-only capture of one sketch slot.</summary>
    public sealed class SketchSlotSnapshotV7
    {
        public int Index { get; set; }
        public int CreationType { get; set; }
        public int LengthType { get; set; }
        public double Length { get; set; }
        public double Width { get; set; }
        public int CenterArcDirection { get; set; }
        public List<string> SlotPointKeys { get; private set; }
        public List<double> SlotPointSketchCoords { get; private set; }

        public SketchSlotSnapshotV7()
        {
            SlotPointKeys = new List<string>();
            SlotPointSketchCoords = new List<double>();
        }

        public void AppendStructuralSignature(StringBuilder sb)
        {
            AppendGeometrySignature(sb);
            AppendPointKeySignature(sb);
        }

        /// <summary>
        /// Everything about the slot that SolidWorks reports deterministically: its parameters
        /// and the sketch-space coordinates of its points. Instability here means the geometry
        /// itself is being read differently between passes, which is geometry-affecting.
        /// </summary>
        public void AppendGeometrySignature(StringBuilder sb)
        {
            sb.Append(Index.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(CreationType.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(LengthType.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(Length.ToString("G17", CultureInfo.InvariantCulture)).Append(';');
            sb.Append(Width.ToString("G17", CultureInfo.InvariantCulture)).Append(';');
            sb.Append(CenterArcDirection.ToString(CultureInfo.InvariantCulture)).Append(';');
            for (int i = 0; i < SlotPointSketchCoords.Count; i++)
                sb.Append(SlotPointSketchCoords[i].ToString("G17", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(';');
        }

        /// <summary>
        /// Slot point identity keys only. Kept separate because a slot's interior points are
        /// synthesized by SolidWorks: its centre point carries no usable second ID and its
        /// GetID() values shift between two reads of the same untouched sketch. Identity
        /// instability here is an API quirk, not a geometry defect - the coordinates above are
        /// the authoritative record.
        /// </summary>
        public void AppendPointKeySignature(StringBuilder sb)
        {
            for (int i = 0; i < SlotPointKeys.Count; i++) sb.Append(SlotPointKeys[i] ?? "").Append(',');
            sb.Append('|');
        }

        public string BuildGeometrySignature()
        {
            StringBuilder sb = new StringBuilder();
            AppendGeometrySignature(sb);
            return sb.ToString();
        }

        public string BuildPointKeySignature()
        {
            StringBuilder sb = new StringBuilder();
            AppendPointKeySignature(sb);
            return sb.ToString();
        }

        public string BuildStructuralSignature()
        {
            StringBuilder sb = new StringBuilder();
            AppendStructuralSignature(sb);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Complete read-only snapshot of one Sketch / 3D Sketch feature.
    /// Produced twice per sketch by Phase 5A so that capture stability can be proved.
    /// </summary>
    public sealed class SketchSnapshotV7
    {
        public string FeatureName { get; set; }
        public string FeatureTypeName { get; set; }
        public int TreeOrder { get; set; }
        public bool IsSuppressed { get; set; }

        public bool CaptureSucceeded { get; set; }
        public string CaptureFailure { get; set; }

        public bool Is3D { get; set; }
        public bool IsDerived { get; set; }
        public bool IsShared { get; set; }

        /// <summary>Persistent reference already captured for this feature by Phase 4.</summary>
        public int FeaturePersistRefBytes { get; set; }
        public string FeaturePersistRefSignature { get; set; }

        // ---- sketch plane / reference ----
        public bool HasReferenceEntity { get; set; }
        /// <summary>swSelectType_e value returned through Sketch.GetReferenceEntity.</summary>
        public int ReferenceEntityType { get; set; }
        public string ReferenceEntityName { get; set; }

        // ---- ModelToSketchTransform ----
        public bool HasModelToSketchTransform { get; set; }

        /// <summary>
        /// Authoritative uniform scale of the transform, derived from the 3x3 rotation block
        /// (ArrayData[0..8]) rather than read out of ArrayData[15].
        ///
        /// SolidWorks does not populate ArrayData[15] for ISketch.ModelToSketchTransform - it
        /// reads 0 for every sketch, including sketches whose transform is provably a rigid
        /// motion with scale 1. Deriving the scale from the rotation block makes the degeneracy
        /// check independent of that unpopulated element.
        /// </summary>
        public double TransformScale { get; set; }

        /// <summary>
        /// Scale derived a second, independent way (mean norm of the three rotation columns).
        /// Compared against <see cref="TransformScale"/> (|det|^(1/3)) to detect a matrix that
        /// is not a similarity transform - which would be a genuine, geometry-affecting defect.
        /// </summary>
        public double TransformScaleCrossCheck { get; set; }

        /// <summary>True when the two independent scale derivations agree.</summary>
        public bool TransformScaleConsistent { get; set; }

        /// <summary>
        /// Raw ArrayData[15]. Diagnostic only - never used as evidence. Retained so the log can
        /// show that SolidWorks leaves it at 0 while the derived scale is 1.
        /// </summary>
        public double TransformRawScaleElement { get; set; }

        private double[] modelToSketchTransform;
        public double[] ModelToSketchTransform
        {
            get { return modelToSketchTransform == null ? null : (double[])modelToSketchTransform.Clone(); }
            set { modelToSketchTransform = value == null ? null : (double[])value.Clone(); }
        }

        // ---- declared counts reported directly by SolidWorks ----
        public int DeclaredPointCount { get; set; }
        public int DeclaredSegmentCount { get; set; }
        public int DeclaredSlotCount { get; set; }

        public List<SketchEntitySnapshotV7> Entities { get; private set; }
        public List<SketchConstraintSnapshotV7> Constraints { get; private set; }
        public List<SketchConstraintSnapshotV7> ExternalRelations { get; private set; }
        public List<SketchDimensionSnapshotV7> Dimensions { get; private set; }
        public List<SketchSlotSnapshotV7> Slots { get; private set; }

        // ---- relation bucket counts (swSketchRelationFilterType_e) ----
        public int RelationCountAll { get; set; }
        public int RelationCountDangling { get; set; }
        public int RelationCountOverDefining { get; set; }
        public int RelationCountExternal { get; set; }
        public int RelationCountDefinedInContext { get; set; }
        public int RelationCountLocked { get; set; }
        public int RelationCountBroken { get; set; }

        // ---- capture warnings that are not integrity errors ----
        public List<string> Warnings { get; private set; }

        /// <summary>
        /// True when the named SolidWorks getter failed during capture. Integrity checks use
        /// this so that a missing declared count is reported as a capture warning instead of
        /// being misread as a count mismatch.
        /// </summary>
        public bool HasWarningStartingWith(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return false;
            for (int i = 0; i < Warnings.Count; i++)
            {
                string warning = Warnings[i];
                if (warning != null && warning.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public SketchSnapshotV7()
        {
            Entities = new List<SketchEntitySnapshotV7>();
            Constraints = new List<SketchConstraintSnapshotV7>();
            ExternalRelations = new List<SketchConstraintSnapshotV7>();
            Dimensions = new List<SketchDimensionSnapshotV7>();
            Slots = new List<SketchSlotSnapshotV7>();
            Warnings = new List<string>();
        }

        /// <summary>
        /// True when this sketch contains at least one slot. Slot sketches get a relaxed
        /// stability policy: SolidWorks enumerates a slot's synthesized interior points
        /// non-deterministically (GetSketchPointsCount2 disagrees with itself between two reads
        /// of the same untouched sketch), so identity-level instability there is an API quirk
        /// rather than evidence that the geometry is being read differently.
        /// </summary>
        public bool HasSlots
        {
            get { return Slots.Count > 0 || DeclaredSlotCount > 0; }
        }

        public int PointCount
        {
            get
            {
                int n = 0;
                foreach (SketchEntitySnapshotV7 e in Entities) if (e.Kind == SketchEntityKindV7.Point) n++;
                return n;
            }
        }

        public int SegmentCount
        {
            get
            {
                int n = 0;
                foreach (SketchEntitySnapshotV7 e in Entities) if (e.Kind == SketchEntityKindV7.Segment) n++;
                return n;
            }
        }

        public int ConstructionSegmentCount
        {
            get
            {
                int n = 0;
                foreach (SketchEntitySnapshotV7 e in Entities)
                    if (e.Kind == SketchEntityKindV7.Segment && e.IsConstruction) n++;
                return n;
            }
        }

        public SketchEntitySnapshotV7 FindEntity(SketchEntityKindV7 kind, string entityKey)
        {
            if (string.IsNullOrEmpty(entityKey)) return null;
            foreach (SketchEntitySnapshotV7 e in Entities)
                if (e.Kind == kind && string.Equals(e.EntityKey, entityKey, StringComparison.Ordinal)) return e;
            return null;
        }

        /// <summary>
        /// Deterministic signature of everything the V7 integrity model treats as authoritative.
        /// Two captures of an unmodified sketch must produce the identical string.
        /// </summary>
        public string BuildStructuralSignature()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(FeatureName ?? "").Append(';');
            sb.Append(FeatureTypeName ?? "").Append(';');
            sb.Append(TreeOrder.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(IsSuppressed ? "1" : "0").Append(';');
            sb.Append(CaptureSucceeded ? "1" : "0").Append(';');
            sb.Append(CaptureFailure ?? "").Append(';');
            sb.Append(Is3D ? "1" : "0").Append(';');
            sb.Append(IsDerived ? "1" : "0").Append(';');
            sb.Append(IsShared ? "1" : "0").Append(';');
            sb.Append(FeaturePersistRefBytes.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(HasReferenceEntity ? "1" : "0").Append(';');
            sb.Append(ReferenceEntityType.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(ReferenceEntityName ?? "").Append(';');
            sb.Append(HasModelToSketchTransform ? "1" : "0").Append(';');
            sb.Append(TransformScale.ToString("G17", CultureInfo.InvariantCulture)).Append(';');
            if (modelToSketchTransform != null)
                for (int i = 0; i < modelToSketchTransform.Length; i++)
                    sb.Append(modelToSketchTransform[i].ToString("G17", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(';');
            sb.Append(DeclaredPointCount.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(DeclaredSegmentCount.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(DeclaredSlotCount.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(RelationCountAll.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(RelationCountDangling.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(RelationCountOverDefining.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(RelationCountExternal.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(RelationCountDefinedInContext.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(RelationCountLocked.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(RelationCountBroken.ToString(CultureInfo.InvariantCulture)).Append(';');
            foreach (SketchEntitySnapshotV7 e in Entities) e.AppendStructuralSignature(sb);
            sb.Append('#');
            foreach (SketchConstraintSnapshotV7 c in Constraints) c.AppendStructuralSignature(sb);
            sb.Append('#');
            foreach (SketchConstraintSnapshotV7 c in ExternalRelations) c.AppendStructuralSignature(sb);
            sb.Append('#');
            foreach (SketchDimensionSnapshotV7 d in Dimensions) d.AppendStructuralSignature(sb);
            sb.Append('#');
            foreach (SketchSlotSnapshotV7 s in Slots) s.AppendStructuralSignature(sb);
            return sb.ToString();
        }

        /// <summary>
        /// Signature of persistent-reference bytes only. Reported as a diagnostic by Phase 5A
        /// because SolidWorks does not guarantee byte stability for entity-level persist refs;
        /// structural stability is the hard gate.
        /// </summary>
        public string BuildPersistRefSignature()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(FeatureName ?? "").Append('=').Append(FeaturePersistRefSignature ?? "<none>").Append('#');
            foreach (SketchEntitySnapshotV7 e in Entities) e.AppendPersistRefSignature(sb);
            sb.Append('#');
            foreach (SketchDimensionSnapshotV7 d in Dimensions) d.AppendPersistRefSignature(sb);
            return sb.ToString();
        }
    }
}
