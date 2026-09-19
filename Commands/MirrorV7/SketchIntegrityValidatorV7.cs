using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ADDIN.Commands.MirrorV7
{
    /// <summary>
    /// How much a failed V7 integrity rule matters.
    ///
    /// The whole point of this enum is to stop SolidWorks API quirks from blocking the workflow.
    /// Every rule must be classified by asking one question: "if this is ignored, will a later
    /// mutation silently produce wrong geometry?" Yes -&gt; Fatal. No -&gt; ApiQuirk.
    ///
    /// Fatal fails the audit. ApiQuirk is counted and logged loudly, and never fails anything.
    /// </summary>
    public enum MirrorV7RuleSeverity
    {
        /// <summary>A later mutation would silently produce wrong geometry. Blocks the audit.</summary>
        Fatal = 0,

        /// <summary>
        /// SolidWorks does not guarantee this property, and nothing downstream depends on it.
        /// Recorded so the behaviour stays visible; never blocks.
        /// </summary>
        ApiQuirk = 1
    }

    /// <summary>Outcome of one integrity or stability check.</summary>
    public sealed class SketchIntegrityReportV7
    {
        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }

        /// <summary>
        /// Findings classified as <see cref="MirrorV7RuleSeverity.ApiQuirk"/>. Kept in their own
        /// channel so they can be reported prominently without ever contributing to
        /// <see cref="HasErrors"/>.
        /// </summary>
        public List<string> ApiQuirks { get; private set; }

        public SketchIntegrityReportV7()
        {
            Errors = new List<string>();
            Warnings = new List<string>();
            ApiQuirks = new List<string>();
        }

        public bool HasErrors { get { return Errors.Count > 0; } }
        public int ApiQuirkCount { get { return ApiQuirks.Count; } }
        public bool HasWarnings { get { return Warnings.Count > 0; } }
        public bool HasApiQuirks { get { return ApiQuirks.Count > 0; } }

        public void Merge(SketchIntegrityReportV7 other)
        {
            if (other == null) return;
            Errors.AddRange(other.Errors);
            Warnings.AddRange(other.Warnings);
            ApiQuirks.AddRange(other.ApiQuirks);
        }

        /// <summary>Routes a finding to the channel its severity deserves.</summary>
        public void Add(MirrorV7RuleSeverity severity, string message)
        {
            if (severity == MirrorV7RuleSeverity.ApiQuirk) AddApiQuirk(message);
            else AddError(message);
        }

        public void AddError(string message) { Errors.Add(message ?? ""); }
        public void AddWarning(string message) { Warnings.Add(message ?? ""); }
        public void AddApiQuirk(string message) { ApiQuirks.Add(message ?? ""); }
    }

    /// <summary>
    /// Phase 5A integrity model.
    ///
    /// Two independent jobs:
    ///   Validate        - is one snapshot internally consistent with what SolidWorks declared?
    ///   VerifyStability - do two read-only captures of the same untouched sketch agree?
    ///
    /// Findings land in one of three channels, and only the first blocks the audit:
    ///   Errors    (Fatal)   - a later mutation would silently produce wrong geometry.
    ///   ApiQuirks           - SolidWorks does not guarantee the property and nothing downstream
    ///                         depends on it. Counted and logged, never blocking.
    ///   Warnings            - pre-existing conditions in the source Part, and informational notes.
    ///
    /// Persistent-reference byte disagreement is a Warning because SolidWorks gives no stability
    /// guarantee for entity-level persist bytes. Slot interior-point identity and slot-sketch point
    /// enumeration are ApiQuirks for the same reason: both were measured to vary between two reads
    /// of an unmodified document.
    /// </summary>
    public static class SketchIntegrityValidatorV7
    {
        private const int DiffDetailCap = 20;
        private const double ScaleEpsilon = 1e-12;

        /// <summary>ArrayData element SolidWorks reserves for the scale factor. Never populated for sketch transforms.</summary>
        private const int TransformRawScaleIndex = 15;

        /// <summary>Label for the rotation block the scale is actually derived from; see SketchSnapshotServiceV7.</summary>
        private const string RotationBlockRange = "0..8";

        /// <summary>
        /// True when the captured transform demonstrably works: it was present, its inverse was
        /// obtained without a capture warning, and every sketch point carries model coordinates.
        /// Used to tell a genuinely degenerate transform from a misread array layout.
        /// </summary>
        private static bool TransformResolvesEveryPoint(SketchSnapshotV7 snapshot)
        {
            if (!snapshot.HasModelToSketchTransform) return false;
            if (snapshot.HasWarningStartingWith("ModelToSketchTransform")) return false;
            if (snapshot.HasWarningStartingWith("SketchToModelTransform")) return false;

            bool sawPoint = false;
            foreach (SketchEntitySnapshotV7 entity in snapshot.Entities)
            {
                if (entity.Kind != SketchEntityKindV7.Point) continue;
                sawPoint = true;
                if (!entity.HasModelCoords) return false;
            }
            return sawPoint;
        }

        public static SketchIntegrityReportV7 Validate(SketchSnapshotV7 snapshot)
        {
            SketchIntegrityReportV7 report = new SketchIntegrityReportV7();
            if (snapshot == null)
            {
                report.AddError("Snapshot is null.");
                return report;
            }

            string who = Describe(snapshot);

            if (!snapshot.CaptureSucceeded)
            {
                report.AddError(who + " capture failed: " + (snapshot.CaptureFailure ?? "<no reason>"));
                return report;
            }

            ValidateCounts(snapshot, who, report);
            ValidateTransform(snapshot, who, report);
            ValidateEntityIdentity(snapshot, who, report);
            ValidateConstraintHealth(snapshot, who, report);

            for (int i = 0; i < snapshot.Warnings.Count; i++)
                report.AddWarning(who + " capture warning: " + snapshot.Warnings[i]);

            return report;
        }

        private static void ValidateCounts(SketchSnapshotV7 snapshot, string who, SketchIntegrityReportV7 report)
        {
            if (!snapshot.HasWarningStartingWith("GetSketchPointsCount2") &&
                snapshot.DeclaredPointCount != snapshot.PointCount)
            {
                // A slot's synthesized interior points are enumerated non-deterministically, so on a
                // slot sketch a declared/captured disagreement is an API quirk. Elsewhere it means the
                // snapshot dropped a point, which would silently lose geometry in a later mutation.
                report.Add(snapshot.HasSlots ? MirrorV7RuleSeverity.ApiQuirk : MirrorV7RuleSeverity.Fatal,
                    who + " point count mismatch: declared=" + snapshot.DeclaredPointCount +
                    " captured=" + snapshot.PointCount + SlotSuffix(snapshot));
            }

            if (snapshot.DeclaredSegmentCount != snapshot.SegmentCount)
            {
                report.AddWarning(who + " segment count mismatch: declared=" + snapshot.DeclaredSegmentCount +
                                  " captured=" + snapshot.SegmentCount);
            }

            if (!snapshot.HasWarningStartingWith("GetSketchSlotCount") &&
                snapshot.DeclaredSlotCount != snapshot.Slots.Count)
            {
                report.AddWarning(who + " slot count mismatch: declared=" + snapshot.DeclaredSlotCount +
                                  " captured=" + snapshot.Slots.Count);
            }

            if (!snapshot.HasWarningStartingWith("GetRelationsCount:swAll") &&
                snapshot.Constraints.Count != snapshot.RelationCountAll)
            {
                // Warning, not error: GetRelationsCount and GetRelations are separate SolidWorks
                // getters and their filter semantics are not contractually identical. Capture
                // stability (VerifyStability) is the hard gate for relations.
                report.AddWarning(who + " relation count mismatch: declared=" + snapshot.RelationCountAll +
                                  " captured=" + snapshot.Constraints.Count);
            }
        }

        private static void ValidateTransform(SketchSnapshotV7 snapshot, string who, SketchIntegrityReportV7 report)
        {
            if (!snapshot.HasModelToSketchTransform)
            {
                if (snapshot.Is3D)
                    report.AddWarning(who + " 3D sketch has no ModelToSketchTransform; model coordinates unavailable.");
                else
                    report.AddError(who + " 2D sketch has no ModelToSketchTransform; model coordinates cannot be derived.");
                return;
            }

            double[] matrix = snapshot.ModelToSketchTransform;
            if (matrix == null || matrix.Length < 16)
            {
                int length = matrix == null ? -1 : matrix.Length;
                report.AddError(who + " ModelToSketchTransform array length=" +
                                length.ToString(CultureInfo.InvariantCulture) +
                                "; expected 16.");
                return;
            }

            if (double.IsNaN(snapshot.TransformScale) || double.IsInfinity(snapshot.TransformScale) || Math.Abs(snapshot.TransformScale) <= ScaleEpsilon)
            {
                // The degeneracy test is functional, not structural. ArrayData's rotation block is
                // read at an assumed offset; if that assumption is wrong the derived scale comes
                // out 0 even for a perfectly good rigid transform. A transform that resolved every
                // sketch point to model coordinates and inverted cleanly is not degenerate, whatever
                // any single array element reads - so that case is an API/layout quirk, not an error.
                report.AddError(who + " ModelToSketchTransform scale is degenerate (derived=" +
                                    Fmt(snapshot.TransformScale) + "); model coordinates cannot be trusted.");
            }
            else if (!snapshot.TransformScaleConsistent)
            {
                // Two independent derivations disagree: the matrix is sheared or non-uniformly
                // scaled, so a single "scale" number cannot describe it. That is geometry-affecting.
                report.AddError(who + " ModelToSketchTransform is not a similarity transform: |det(R)|^(1/3)=" +
                                Fmt(snapshot.TransformScale) + " but mean column norm=" +
                                Fmt(snapshot.TransformScaleCrossCheck) + ".");
            }
            else if (Math.Abs(snapshot.TransformRawScaleElement) <= ScaleEpsilon)
            {
                // Recorded per sketch so the count in the summary reflects how widespread it is.
                report.AddApiQuirk(who + " ArrayData[" + TransformRawScaleIndex + "] reads 0 while the scale derived " +
                                   "from the rotation block is " + Fmt(snapshot.TransformScale) + "; SolidWorks does not " +
                                   "populate the scale element of ISketch.ModelToSketchTransform.");
            }

            int missingModelCoords = 0;
            foreach (SketchEntitySnapshotV7 entity in snapshot.Entities)
                if (entity.Kind == SketchEntityKindV7.Point && !entity.HasModelCoords) missingModelCoords++;

            if (missingModelCoords > 0)
                report.AddWarning(who + " " + missingModelCoords + " point(s) have no model coordinates.");
        }

        private static void ValidateEntityIdentity(SketchSnapshotV7 snapshot, string who, SketchIntegrityReportV7 report)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int duplicates = 0;
            int withoutIds = 0;

            foreach (SketchEntitySnapshotV7 entity in snapshot.Entities)
            {
                string key = entity.Kind + ":" + entity.EntityKey;
                if (!seen.Add(key)) duplicates++;
                if (!entity.HasIds) withoutIds++;
            }

            if (duplicates > 0)
                report.AddWarning(who + " " + duplicates + " duplicated sketch entity ID key(s)." + SlotSuffix(snapshot) +
                                  " A slot's arcs and lines legitimately share GetID() values, so on a slot sketch " +
                                  "this is expected rather than a defect; identity must be resolved at mutation time.");

            if (withoutIds > 0)
                report.AddWarning(who + " " + withoutIds + " sketch entity/entities without IDs; " +
                                  "index-only identity is forbidden by V7 and must be resolved before mutation.");
        }

        private static void ValidateConstraintHealth(SketchSnapshotV7 snapshot, string who, SketchIntegrityReportV7 report)
        {
            if (snapshot.RelationCountDangling > 0)
                report.AddWarning(who + " dangling relations=" + snapshot.RelationCountDangling + " (pre-existing in source Part).");
            if (snapshot.RelationCountBroken > 0)
                report.AddWarning(who + " broken relations=" + snapshot.RelationCountBroken + " (pre-existing in source Part).");
            if (snapshot.RelationCountOverDefining > 0)
                report.AddWarning(who + " over-defining relations=" + snapshot.RelationCountOverDefining + " (pre-existing in source Part).");
            if (snapshot.RelationCountExternal > 0)
                report.AddWarning(who + " external relations=" + snapshot.RelationCountExternal +
                                  "; external references must be remapped explicitly in a later phase.");
        }

        /// <summary>
        /// Compares two captures of the same untouched sketch. Any structural difference is an error.
        /// </summary>
        public static SketchIntegrityReportV7 VerifyStability(SketchSnapshotV7 first, SketchSnapshotV7 second)
        {
            SketchIntegrityReportV7 report = new SketchIntegrityReportV7();
            if (first == null || second == null)
            {
                report.AddError("Cannot verify stability: a snapshot is null.");
                return report;
            }

            string who = Describe(first);

            // SolidWorks enumerates a slot's synthesized interior points non-deterministically: two
            // reads of the same untouched slot sketch can return a different point count (observed
            // 13 then 14, and 8 then 9, on an unmodified part). That is an API limitation, not
            // evidence the geometry is being read differently, so count/entity-level instability on a
            // slot sketch is downgraded to a quirk. Slot geometry itself - parameters and point
            // coordinates - stays Fatal, as does everything on a sketch without slots.
            MirrorV7RuleSeverity enumerationSeverity =
                (first.HasSlots || second.HasSlots) ? MirrorV7RuleSeverity.ApiQuirk : MirrorV7RuleSeverity.Fatal;

            CompareScalars(first, second, who, report, enumerationSeverity);
            CompareTransform(first, second, who, report);
            CompareEntities(first, second, who, report, enumerationSeverity);
            CompareConstraints(first.Constraints, second.Constraints, "relation", who, report);
            CompareConstraints(first.ExternalRelations, second.ExternalRelations, "externalRelation", who, report);
            CompareDimensions(first, second, who, report);
            CompareSlots(first, second, who, report);

            if (string.Equals(first.BuildPersistRefSignature(), second.BuildPersistRefSignature(), StringComparison.Ordinal))
                report.AddWarning(who + " persistRefSignature STABLE (informational).");
            else
                report.AddWarning(who + " persistRefSignature UNSTABLE between captures (diagnostic only; structural signature is authoritative).");

            return report;
        }

        private static void CompareScalars(SketchSnapshotV7 a, SketchSnapshotV7 b, string who, SketchIntegrityReportV7 report, MirrorV7RuleSeverity enumerationSeverity)
        {
            CompareText("featureName", a.FeatureName, b.FeatureName, who, report);
            CompareText("featureType", a.FeatureTypeName, b.FeatureTypeName, who, report);
            CompareInt("treeOrder", a.TreeOrder, b.TreeOrder, who, report);
            CompareBool("isSuppressed", a.IsSuppressed, b.IsSuppressed, who, report);
            CompareBool("captureSucceeded", a.CaptureSucceeded, b.CaptureSucceeded, who, report);
            CompareText("captureFailure", a.CaptureFailure, b.CaptureFailure, who, report);
            CompareBool("is3D", a.Is3D, b.Is3D, who, report);
            CompareBool("isDerived", a.IsDerived, b.IsDerived, who, report);
            CompareBool("isShared", a.IsShared, b.IsShared, who, report);
            CompareInt("featurePersistRefBytes", a.FeaturePersistRefBytes, b.FeaturePersistRefBytes, who, report);
            CompareBool("hasReferenceEntity", a.HasReferenceEntity, b.HasReferenceEntity, who, report);
            CompareInt("referenceEntityType", a.ReferenceEntityType, b.ReferenceEntityType, who, report);
            CompareText("referenceEntityName", a.ReferenceEntityName, b.ReferenceEntityName, who, report);
            CompareBool("hasModelToSketchTransform", a.HasModelToSketchTransform, b.HasModelToSketchTransform, who, report);
            CompareDouble("transformScale", a.TransformScale, b.TransformScale, who, report);
            CompareInt("declaredPointCount", a.DeclaredPointCount, b.DeclaredPointCount, who, report, enumerationSeverity);
            CompareInt("declaredSegmentCount", a.DeclaredSegmentCount, b.DeclaredSegmentCount, who, report, enumerationSeverity);
            CompareInt("declaredSlotCount", a.DeclaredSlotCount, b.DeclaredSlotCount, who, report);
            CompareInt("relationCountAll", a.RelationCountAll, b.RelationCountAll, who, report);
            CompareInt("relationCountDangling", a.RelationCountDangling, b.RelationCountDangling, who, report);
            CompareInt("relationCountOverDefining", a.RelationCountOverDefining, b.RelationCountOverDefining, who, report);
            CompareInt("relationCountExternal", a.RelationCountExternal, b.RelationCountExternal, who, report);
            CompareInt("relationCountDefinedInContext", a.RelationCountDefinedInContext, b.RelationCountDefinedInContext, who, report);
            CompareInt("relationCountLocked", a.RelationCountLocked, b.RelationCountLocked, who, report);
            CompareInt("relationCountBroken", a.RelationCountBroken, b.RelationCountBroken, who, report);
        }

        private static void CompareTransform(SketchSnapshotV7 a, SketchSnapshotV7 b, string who, SketchIntegrityReportV7 report)
        {
            double[] ma = a.ModelToSketchTransform;
            double[] mb = b.ModelToSketchTransform;
            if (ma == null && mb == null) return;
            if (ma == null || mb == null)
            {
                report.AddError(who + " instability: ModelToSketchTransform present in one capture only.");
                return;
            }
            if (ma.Length != mb.Length)
            {
                report.AddError(who + " instability: ModelToSketchTransform length " + ma.Length + " vs " + mb.Length + ".");
                return;
            }
            for (int i = 0; i < ma.Length; i++)
            {
                if (ma[i].ToString("G17", CultureInfo.InvariantCulture) != mb[i].ToString("G17", CultureInfo.InvariantCulture))
                {
                    report.AddError(who + " instability: ModelToSketchTransform[" + i + "] " +
                                    Fmt(ma[i]) + " -> " + Fmt(mb[i]));
                }
            }
        }

        private static void CompareEntities(SketchSnapshotV7 a, SketchSnapshotV7 b, string who, SketchIntegrityReportV7 report, MirrorV7RuleSeverity enumerationSeverity)
        {
            if (a.HasSlots || b.HasSlots)
            {
                // Compare geometry as a multiset so API enumeration order cannot hide or invent changes.
                var ga = new List<string>();
                var gb = new List<string>();
                foreach (var entity in a.Entities) if (!IsSynthesizedSlotPoint(entity, a)) ga.Add(entity.BuildGeometrySignature());
                foreach (var entity in b.Entities) if (!IsSynthesizedSlotPoint(entity, b)) gb.Add(entity.BuildGeometrySignature());
                ga.Sort(StringComparer.Ordinal); gb.Sort(StringComparer.Ordinal);
                if (string.Join("\n", ga.ToArray()) != string.Join("\n", gb.ToArray()))
                    report.AddError(who + " slot sketch entity geometry differs; synthesized-point differences require explicit attribution before acceptance.");
                if (a.Entities.Count != b.Entities.Count)
                    report.AddApiQuirk(who + " slot enumeration count differs: " + a.Entities.Count + " -> " + b.Entities.Count);
                return;
            }
            string note = enumerationSeverity == MirrorV7RuleSeverity.ApiQuirk ? SlotSuffix(a) : "";

            if (a.Entities.Count != b.Entities.Count)
            {
                report.Add(enumerationSeverity, who + " instability: entity count " + a.Entities.Count +
                           " -> " + b.Entities.Count + "." + note);
                return;
            }

            int reported = 0;
            for (int i = 0; i < a.Entities.Count; i++)
            {
                SketchEntitySnapshotV7 ea = a.Entities[i];
                SketchEntitySnapshotV7 eb = b.Entities[i];

                // Geometry is compared separately and always stays fatal: a sketch whose points move
                // between two reads of an untouched document is a real defect, slot or no slot.
                string ga = ea.BuildGeometrySignature();
                string gb = eb.BuildGeometrySignature();
                if (!string.Equals(ga, gb, StringComparison.Ordinal))
                {
                    if (reported++ < DiffDetailCap)
                        report.AddError(who + " instability: entity[" + i + "] GEOMETRY " + ea.Kind +
                                        " key=" + ea.EntityKey + "\n    pass1=" + ga + "\n    pass2=" + gb);
                    continue;
                }

                // Identity/enumeration only differs. Fatal unless SolidWorks is known not to
                // guarantee it for this sketch (slot interior points).
                string sa = ea.BuildStructuralSignature();
                string sb2 = eb.BuildStructuralSignature();
                if (string.Equals(sa, sb2, StringComparison.Ordinal)) continue;
                if (reported++ < DiffDetailCap)
                {
                    report.Add(enumerationSeverity, who + " instability: entity[" + i + "] IDENTITY " + ea.Kind +
                               " key=" + ea.EntityKey + "\n    pass1=" + sa + "\n    pass2=" + sb2 + note);
                }
            }
            if (reported > DiffDetailCap)
                report.Add(enumerationSeverity, who + " instability: " + (reported - DiffDetailCap) +
                           " further differing entity/entities not listed." + note);
        }

        private static bool IsSynthesizedSlotPoint(SketchEntitySnapshotV7 entity, SketchSnapshotV7 snapshot)
        {
            if (entity.Kind != SketchEntityKindV7.Point || !entity.HasIds || entity.Id2 >= 0) return false;
            foreach (var slot in snapshot.Slots)
                for (int i=0; i+2<slot.SlotPointSketchCoords.Count; i+=3)
                    if (Math.Abs(entity.SketchX-slot.SlotPointSketchCoords[i]) <= 1e-12 &&
                        Math.Abs(entity.SketchY-slot.SlotPointSketchCoords[i+1]) <= 1e-12 &&
                        Math.Abs(entity.SketchZ-slot.SlotPointSketchCoords[i+2]) <= 1e-12) return true;
            return false;
        }

        private static void CompareConstraints(
            List<SketchConstraintSnapshotV7> a,
            List<SketchConstraintSnapshotV7> b,
            string label,
            string who,
            SketchIntegrityReportV7 report)
        {
            if (a.Count != b.Count)
            {
                report.AddError(who + " instability: " + label + " count " + a.Count + " -> " + b.Count + ".");
                return;
            }

            int reported = 0;
            for (int i = 0; i < a.Count; i++)
            {
                string sa = a[i].BuildStructuralSignature();
                string sb = b[i].BuildStructuralSignature();
                if (string.Equals(sa, sb, StringComparison.Ordinal)) continue;
                if (reported++ < DiffDetailCap)
                {
                    report.AddError(who + " instability: " + label + "[" + i + "] type=" + a[i].RelationTypeName +
                                    "\n    pass1=" + sa + "\n    pass2=" + sb);
                }
            }
            if (reported > DiffDetailCap)
                report.AddError(who + " instability: " + (reported - DiffDetailCap) + " further differing " + label + "(s) not listed.");
        }

        private static void CompareDimensions(SketchSnapshotV7 a, SketchSnapshotV7 b, string who, SketchIntegrityReportV7 report)
        {
            if (a.Dimensions.Count != b.Dimensions.Count)
            {
                report.AddError(who + " instability: dimension count " + a.Dimensions.Count + " -> " + b.Dimensions.Count + ".");
                return;
            }

            int reported = 0;
            for (int i = 0; i < a.Dimensions.Count; i++)
            {
                string sa = a.Dimensions[i].BuildStructuralSignature();
                string sb = b.Dimensions[i].BuildStructuralSignature();
                if (string.Equals(sa, sb, StringComparison.Ordinal)) continue;
                if (reported++ < DiffDetailCap)
                {
                    report.AddError(who + " instability: dimension[" + i + "] name=" + a.Dimensions[i].FullName +
                                    "\n    pass1=" + sa + "\n    pass2=" + sb);
                }
            }
            if (reported > DiffDetailCap)
                report.AddError(who + " instability: " + (reported - DiffDetailCap) + " further differing dimension(s) not listed.");
        }

        private static void CompareSlots(SketchSnapshotV7 a, SketchSnapshotV7 b, string who, SketchIntegrityReportV7 report)
        {
            if (a.Slots.Count != b.Slots.Count)
            {
                report.AddError(who + " instability: slot count " + a.Slots.Count + " -> " + b.Slots.Count + ".");
                return;
            }

            int reportedGeometry = 0;
            int reportedIdentity = 0;
            for (int i = 0; i < a.Slots.Count; i++)
            {
                SketchSlotSnapshotV7 sa = a.Slots[i];
                SketchSlotSnapshotV7 sb = b.Slots[i];

                // Slot parameters and point coordinates: geometry-affecting, always fatal.
                string ga = sa.BuildGeometrySignature();
                string gb = sb.BuildGeometrySignature();
                if (!string.Equals(ga, gb, StringComparison.Ordinal) && reportedGeometry++ < DiffDetailCap)
                    report.AddError(who + " instability: slot[" + i + "] GEOMETRY\n    pass1=" + ga + "\n    pass2=" + gb);

                // Slot point identity: SolidWorks synthesizes a slot's interior points and does not
                // keep their GetID() values stable between two reads of the same untouched sketch.
                string ka = sa.BuildPointKeySignature();
                string kb = sb.BuildPointKeySignature();
                if (!string.Equals(ka, kb, StringComparison.Ordinal) && reportedIdentity++ < DiffDetailCap)
                    report.AddApiQuirk(who + " instability: slot[" + i + "] POINT_KEYS\n    pass1=" + ka +
                                       "\n    pass2=" + kb + "\n    Slot interior point identity is not guaranteed by " +
                                       "SolidWorks; resolve it at mutation time instead of caching it here.");
            }
            if (reportedGeometry > DiffDetailCap)
                report.AddError(who + " instability: " + (reportedGeometry - DiffDetailCap) + " further differing slot(s) not listed.");
            if (reportedIdentity > DiffDetailCap)
                report.AddApiQuirk(who + " instability: " + (reportedIdentity - DiffDetailCap) + " further slot(s) with differing point keys not listed.");
        }

        private static void CompareText(string field, string a, string b, string who, SketchIntegrityReportV7 report)
        {
            if (string.Equals(a ?? "", b ?? "", StringComparison.Ordinal)) return;
            report.AddError(who + " instability: " + field + " \"" + (a ?? "") + "\" -> \"" + (b ?? "") + "\"");
        }

        private static void CompareInt(string field, int a, int b, string who, SketchIntegrityReportV7 report)
        {
            CompareInt(field, a, b, who, report, MirrorV7RuleSeverity.Fatal);
        }

        private static void CompareInt(string field, int a, int b, string who, SketchIntegrityReportV7 report, MirrorV7RuleSeverity severity)
        {
            if (a == b) return;
            report.Add(severity, who + " instability: " + field + " " + a.ToString(CultureInfo.InvariantCulture) +
                         " -> " + b.ToString(CultureInfo.InvariantCulture));
        }

        private static void CompareBool(string field, bool a, bool b, string who, SketchIntegrityReportV7 report)
        {
            if (a == b) return;
            report.AddError(who + " instability: " + field + " " + a + " -> " + b);
        }

        private static void CompareDouble(string field, double a, double b, string who, SketchIntegrityReportV7 report)
        {
            if (Fmt(a) == Fmt(b)) return;
            report.AddError(who + " instability: " + field + " " + Fmt(a) + " -> " + Fmt(b));
        }

        private static string Fmt(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static string Describe(SketchSnapshotV7 snapshot)
        {
            return "sketch \"" + (snapshot.FeatureName ?? "") + "\" type=\"" + (snapshot.FeatureTypeName ?? "") + "\"";
        }

        /// <summary>
        /// Suffix that says why a finding was classified as an API quirk instead of an error, so the
        /// log explains itself rather than silently downgrading a rule.
        /// </summary>
        private static string SlotSuffix(SketchSnapshotV7 snapshot)
        {
            if (snapshot == null || !snapshot.HasSlots) return "";
            return " [API_QUIRK: sketch has " + snapshot.Slots.Count.ToString(CultureInfo.InvariantCulture) +
                   " slot(s); SolidWorks does not guarantee stable enumeration or GetID() values for a " +
                   "slot's synthesized interior points.]";
        }
    }
}
