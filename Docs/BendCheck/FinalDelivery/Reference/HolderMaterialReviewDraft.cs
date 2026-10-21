// DESIGN REFERENCE ONLY; NOT included in ADDIN.csproj.
// Copy into Commands/BendPlacement after Codex review, add to project, add tests.
// C# 7.3 / .NET Framework 4.8. This code NEVER certifies material, closes CAD,
// infers a tip/seating coordinate, or creates/signs a ToolReviewProof.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ADDIN.Commands.BendPlacement
{
    internal sealed class HolderSourceRoleRow
    {
        public string EntityKey { get; private set; }
        public string Layer { get; private set; }
        public string SourceType { get; private set; }
        public string SourceLineType { get; private set; }
        public bool? SourceClosed { get; private set; }
        public bool GeometrySupported { get; private set; }
        public string UnsupportedReason { get; private set; }
        public double BoundaryApproximationErrorMm { get; private set; }
        // Never fill this from layer, area, winding or topology:
        public string ReviewedMaterialRole { get { return "NEEDS_INDEPENDENT_REVIEW"; } }

        internal HolderSourceRoleRow(HolderEntityGeometry e)
        {
            EntityKey = e.EntityKey;
            Layer = e.Layer;
            SourceType = e.SourceType;
            SourceLineType = e.SourceLineType;
            SourceClosed = e.SourceClosed;
            GeometrySupported = e.Supported;
            UnsupportedReason = e.Reason;
            BoundaryApproximationErrorMm = e.ErrorBoundMm;
        }
    }

    internal sealed class HolderMaterialReviewDraft
    {
        public string ToolId { get; private set; }
        public string MasterSha256 { get; private set; }
        public string ManifestSha256 { get; private set; }
        public string BindingSha256 { get; private set; }
        public string SourceSlot { get; private set; }
        public bool FixedPairMatched { get; private set; }
        public int ConnectedComponents { get; private set; }
        public int ClosedSimpleComponents { get; private set; }
        public int OpenOrBranchedComponents { get; private set; }
        public IReadOnlyList<HolderSourceRoleRow> Rows { get; private set; }
        public IReadOnlyList<string> MissingReasons { get; private set; }
        // Explicit: inventory is NOT a manufacturing approval.
        public string Status { get { return "DRAFT_UNREVIEWED"; } }

        internal HolderMaterialReviewDraft(
            ToolAssemblyProfile source, bool matched, string binding,
            HolderTopologyInventory topology, HolderSourceRoleRow[] rows,
            IEnumerable<string> reasons)
        {
            ToolId = source.Id;
            MasterSha256 = source.MasterSha256;
            ManifestSha256 = source.ManifestSha256;
            BindingSha256 = binding;
            SourceSlot = source.CadPair == null ? null : source.CadPair.SlotId;
            FixedPairMatched = matched;
            ConnectedComponents = topology == null ? 0 : topology.ConnectedComponents;
            ClosedSimpleComponents = topology == null ? 0 : topology.ClosedSimpleComponents;
            OpenOrBranchedComponents = topology == null ? 0 : topology.OpenOrBranchedComponents;
            Rows = Array.AsReadOnly(rows);
            MissingReasons = Array.AsReadOnly(reasons.Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }
    }

    internal static class HolderMaterialReviewDraftBuilder
    {
        // Only managed, read-only source data. Caller verifies owning library
        // snapshot.Current immediately before presentation/export.
        internal static HolderMaterialReviewDraft Create(
            ToolAssemblyProfile source, CancellationToken token)
        {
            if (source == null) throw new ArgumentNullException("source");
            token.ThrowIfCancellationRequested();
            var missing = new List<string>();
            bool matched = source.CadPair != null && source.CadPair.Matched &&
                source.MappingIdentified;
            if (!matched) missing.Add("MISSING_OR_STALE_FIXED_CAD_PAIR");
            if (source.Punch == null) missing.Add("MISSING_PUNCH_SELECTION");
            if (source.Holder == null) missing.Add("MISSING_HOLDER_SELECTION");
            if (source.Punch != null &&
                (!source.Punch.ObservedClosed || !source.Punch.Region.IsValidRegion))
                missing.Add("OPEN_OR_INVALID_PUNCH_MATERIAL");
            if (!matched || source.Punch == null || source.Holder == null)
                return new HolderMaterialReviewDraft(source, false, null, null,
                    new HolderSourceRoleRow[0], missing);

            var inventory = source.Holder.SourceEntities
                .Select(e => new HolderEntityGeometry(e, token)).ToArray();
            token.ThrowIfCancellationRequested();
            if (inventory.Length == 0) missing.Add("EMPTY_HOLDER_SOURCE");
            if (inventory.Any(e => !e.Supported))
                missing.Add("UNSUPPORTED_HOLDER_ENTITIES_NEED_REVIEW");
            missing.Add("HOLDER_MATERIAL_SOLID_HOLE_REFERENCE_ROLES_UNREVIEWED");
            missing.Add("PHYSICAL_SEATING_DATUM_AXIAL_LENGTH_UNVERIFIED");

            var topology = new HolderTopologyInventory(inventory, token);
            string binding = HolderObstacleSet.Binding(source.Id, source.Punch,
                source.Holder, source.ManifestSha256);
            return new HolderMaterialReviewDraft(source, true, binding, topology,
                inventory.Select(x => new HolderSourceRoleRow(x)).ToArray(), missing);
        }

        // The reviewer explicitly authors contours and exclusions.
        // Returns an UNSIGNED holder rule for HolderObstacleSet.Payload(...) and
        // an independent external authority. This method never calls Verify or
        // sets Review. A missing/duplicate key is rejected, not silently filled.
        internal static HolderMaterialDefinition PrepareUnsigned(
            HolderMaterialReviewDraft draft,
            IEnumerable<HolderContourDefinition> reviewedContours,
            IEnumerable<HolderExcludedEntity> reviewedExcluded,
            string reviewedCombinationRule)
        {
            if (draft == null) throw new ArgumentNullException("draft");
            if (!draft.FixedPairMatched || String.IsNullOrWhiteSpace(draft.BindingSha256))
                throw new InvalidOperationException("MISSING_FIXED_PAIR_BINDING");

            var contours = (reviewedContours ?? Enumerable.Empty<HolderContourDefinition>()).ToList();
            var excluded = (reviewedExcluded ?? Enumerable.Empty<HolderExcludedEntity>()).ToList();
            if (contours.Count < 1 || contours.Count > 128 ||
                contours.Any(c => c == null || String.IsNullOrWhiteSpace(c.Id) ||
                    c.EntityKeys == null || c.EntityKeys.Length == 0 ||
                    !new[] { "Solid", "Hole", "Island" }.Contains(c.MaterialKind)))
                throw new InvalidOperationException("REVIEWED_CONTOURS_REQUIRED");

            if (excluded.Any(e => e == null || String.IsNullOrWhiteSpace(e.EntityKey) ||
                !new[] { "Decorative", "Centerline", "Reference", "Annotation" }.Contains(e.Role)))
                throw new InvalidOperationException("EXPLICIT_EXCLUSION_ROLE_REQUIRED");

            if (reviewedCombinationRule != null &&
                reviewedCombinationRule != "DisjointMaterialHierarchyV1" &&
                reviewedCombinationRule != "UnionOfComponentMaterialsV1")
                throw new InvalidOperationException("INVALID_REVIEWED_COMBINATION_RULE");

            var expected = draft.Rows.Select(e => e.EntityKey).ToArray();
            var assigned = contours.SelectMany(c => c.EntityKeys)
                .Concat(excluded.Select(e => e.EntityKey)).ToArray();
            if (assigned.Any(String.IsNullOrWhiteSpace) ||
                assigned.Length != expected.Length ||
                assigned.Distinct(StringComparer.Ordinal).Count() != assigned.Length ||
                !new HashSet<string>(expected, StringComparer.Ordinal).SetEquals(assigned))
                throw new InvalidOperationException("INCOMPLETE_OR_DUPLICATE_ENTITY_ROLES");

            // A signature cannot be produced inside the application.
            // The existing HolderObstacleSet.Read validates contours, holes,
            // nesting, source binding and external review before any collision.
            return new HolderMaterialDefinition
            {
                ToolId = draft.ToolId,
                BindingSha256 = draft.BindingSha256,
                Contours = contours,
                Excluded = excluded,
                CombinationRule = reviewedCombinationRule,
                Review = null
            };
        }
    }
}
