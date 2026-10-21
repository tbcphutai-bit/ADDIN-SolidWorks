using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlacement
{
    // Observations only. Source flags, layers, area and connectivity NEVER
    // assign material roles. No CAD entities or closure edges are generated.
    internal sealed class HolderSourceRoleRow
    {
        public string EntityKey { get; }
        public string Handle { get; }
        public string InstancePath { get; }
        public IReadOnlyList<string> Blocks { get; }
        public IReadOnlyList<string> InsertHandles { get; }
        public string Layer { get; }
        public string SourceType { get; }
        public string SourceLineType { get; }
        public bool? SourceClosed { get; }
        public bool GeometrySupported { get; }
        public string UnsupportedReason { get; }
        public double BoundaryApproximationErrorMm { get; }
        public string ProposedRole => "NeedsReview";
        public string ReviewedRole { get; }
        // Original analytic curves / NURBS retained for the private viewer.
        internal ToolDxfEntity Source { get; }
        internal HolderEntityGeometry Geometry { get; }
        internal HolderSourceRoleRow(ToolDxfEntity source, HolderEntityGeometry geometry,string reviewedRole)
        {
            Source=source; Geometry=geometry; EntityKey=geometry.EntityKey;
            Handle=source.Handle; InstancePath=source.InstancePath;
            Blocks=PlacementLists.Copy(source.Blocks); InsertHandles=PlacementLists.Copy(source.InsertHandles);
            Layer=geometry.Layer; SourceType=geometry.SourceType; SourceLineType=geometry.SourceLineType;
            SourceClosed=geometry.SourceClosed; GeometrySupported=geometry.Supported;
            UnsupportedReason=geometry.Reason; BoundaryApproximationErrorMm=geometry.ErrorBoundMm;
            ReviewedRole=reviewedRole;
        }
    }
    internal sealed class HolderMaterialReviewDraft
    {
        private readonly Func<bool> current;
        public string ToolId { get; }
        public string MasterSha256 { get; }
        public string ManifestSha256 { get; }
        public string BindingSha256 { get; }
        public string SourceSlot { get; }
        public bool FixedPairMatched { get; }
        public bool Current => current();
        public string Status => !Current ? "SOURCE_STALE" : !FixedPairMatched ? "SOURCE_UNMATCHED" :
            MaterialReviewed ? "MATERIAL_REVIEWED" : "MATERIAL_ROLE_PENDING";
        public bool MaterialReviewed => Current && Source.HolderObstacles?.GeometrySupportedForCollision == true;
        public int ConnectedComponents => Topology?.ConnectedComponents ?? 0;
        public int ClosedSimpleComponents => Topology?.ClosedSimpleComponents ?? 0;
        public int OpenOrBranchedComponents => Topology?.OpenOrBranchedComponents ?? 0;
        public IReadOnlyList<HolderSourceRoleRow> Rows { get; }
        private readonly IReadOnlyList<string> reasons;
        public IReadOnlyList<string> MissingReasons => PlacementLists.Reasons(reasons.Concat(Current ? new string[0] : new[]{"SOURCE_STALE"}));
        internal ToolAssemblyProfile Source { get; }
        internal HolderTopologyInventory Topology { get; }
        internal HolderMaterialReviewDraft(ToolAssemblyProfile source, Func<bool> current, bool matched,
            string binding, HolderTopologyInventory topology, IEnumerable<HolderSourceRoleRow> rows, IEnumerable<string> reasons)
        {
            Source=source; this.current=current; ToolId=source.Id; MasterSha256=source.MasterSha256;
            ManifestSha256=source.ManifestSha256; SourceSlot=source.CadPair?.SlotId;
            FixedPairMatched=matched; BindingSha256=binding; Topology=topology;
            Rows=PlacementLists.Copy(rows); this.reasons=PlacementLists.Reasons(reasons);
        }
    }
    internal static class HolderMaterialReviewDraftBuilder
    {
        internal static HolderMaterialReviewDraft Create(ToolAssemblyProfile source, CancellationToken token, Func<bool> current=null)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));
            token.ThrowIfCancellationRequested(); var missing=new List<string>();
            bool matched=source.CadPair?.Matched==true && source.MappingIdentified;
            if(!matched)missing.Add("MISSING_OR_STALE_FIXED_CAD_PAIR");
            if(source.Punch==null)missing.Add("MISSING_PUNCH_SELECTION");
            if(source.Holder==null)missing.Add("MISSING_HOLDER_SELECTION");
            if(source.Punch!=null && (!source.Punch.ObservedClosed || !source.Punch.Region.IsValidRegion))
                missing.Add("OPEN_OR_INVALID_PUNCH_MATERIAL");
            // Even unmatched selections are inventoried: never hide the source
            // items that prevented promotion. The binding is unusable for export.
            var inventory=source.HolderObstacles?.Inventory ?? PlacementLists.Copy(source.Holder?.SourceEntities.Select(e=>new HolderEntityGeometry(e,token)));
            var entities=source.Holder?.SourceEntities ?? new ToolDxfEntity[0];
            var rows=entities.Select(e=> {string role=null;
                if(source.HolderObstacles?.GeometrySupportedForCollision==true)source.HolderObstacles.ReviewedRoles.TryGetValue(HolderEntityGeometry.Key(e),out role);
                return new HolderSourceRoleRow(e,inventory.Single(g=>g.EntityKey==HolderEntityGeometry.Key(e)),role);}).ToArray();
            if(rows.Length==0)missing.Add("EMPTY_HOLDER_SOURCE");
            if(rows.Any(e=>!e.GeometrySupported))missing.Add("UNSUPPORTED_HOLDER_ENTITIES_NEED_REVIEW");
            if(source.HolderObstacles?.GeometrySupportedForCollision!=true)missing.Add("HOLDER_MATERIAL_SOLID_HOLE_REFERENCE_ROLES_UNREVIEWED");
            if(!source.SeatingEvidence.CoordinateRecordReady)missing.Add("PHYSICAL_SEATING_DATUM_UNVERIFIED");
            var topology=source.HolderObstacles?.Topology ?? new HolderTopologyInventory(inventory,token);
            string binding=matched ? HolderObstacleSet.Binding(source.Id,source.Punch,source.Holder,source.ManifestSha256) : null;
            return new HolderMaterialReviewDraft(source,()=> (current?.Invoke() ?? true) && source.MasterDatums?.Status!=DatumEvidenceStatus.Stale &&
                source.MappingReview.Status!=ToolReviewStatus.Stale && source.HolderObstacles?.Current!=false,
                matched,binding,topology,rows,missing);
        }
        internal static HolderMaterialDefinition PrepareUnsigned(HolderMaterialReviewDraft draft,
            IEnumerable<HolderContourDefinition> contours, IEnumerable<HolderExcludedEntity> excluded, string combination)
        {
            if(draft==null)throw new ArgumentNullException(nameof(draft));
            if(!draft.Current)throw new InvalidOperationException("SOURCE_STALE");
            if(!draft.FixedPairMatched || string.IsNullOrWhiteSpace(draft.BindingSha256))throw new InvalidOperationException("MISSING_FIXED_PAIR_BINDING");
            var c=(contours ?? Enumerable.Empty<HolderContourDefinition>()).ToArray();
            var x=(excluded ?? Enumerable.Empty<HolderExcludedEntity>()).ToArray();
            if(c.Length<1 || c.Length>128 || c.Any(v=>v==null || string.IsNullOrWhiteSpace(v.Id) || v.EntityKeys==null || v.EntityKeys.Length==0 ||
                !new[]{"Solid","Hole","Island"}.Contains(v.MaterialKind)) || c.Select(v=>v.Id).Distinct(StringComparer.Ordinal).Count()!=c.Length)
                throw new InvalidOperationException("REVIEWED_CONTOURS_REQUIRED");
            if(x.Any(v=>v==null || string.IsNullOrWhiteSpace(v.EntityKey) || !new[]{"Decorative","Centerline","Reference","Annotation"}.Contains(v.Role)))
                throw new InvalidOperationException("EXPLICIT_EXCLUSION_ROLE_REQUIRED");
            if(combination!=null && combination!="DisjointMaterialHierarchyV1" && combination!="UnionOfComponentMaterialsV1")
                throw new InvalidOperationException("INVALID_REVIEWED_COMBINATION_RULE");
            var assigned=c.SelectMany(v=>v.EntityKeys).Concat(x.Select(v=>v.EntityKey)).ToArray();
            if(assigned.Any(string.IsNullOrWhiteSpace) || assigned.Distinct(StringComparer.Ordinal).Count()!=assigned.Length ||
                assigned.Length!=draft.Rows.Count || !new HashSet<string>(draft.Rows.Select(v=>v.EntityKey),StringComparer.Ordinal).SetEquals(assigned))
                throw new InvalidOperationException("INCOMPLETE_OR_DUPLICATE_ENTITY_ROLES");
            // Clone all arrays: edits to the unsigned UI draft cannot alter the
            // canonical payload returned for independent review.
            return new HolderMaterialDefinition {ToolId=draft.ToolId,BindingSha256=draft.BindingSha256,Review=null,CombinationRule=combination,
                Contours=c.Select(v=>new HolderContourDefinition {Id=v.Id,ParentId=v.ParentId,MaterialKind=v.MaterialKind,EntityKeys=v.EntityKeys.ToArray()}).ToList(),
                Excluded=x.Select(v=>new HolderExcludedEntity {EntityKey=v.EntityKey,Role=v.Role}).ToList()};
        }
    }
}
