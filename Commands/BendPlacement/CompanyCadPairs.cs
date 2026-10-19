using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlacement
{
    // Version 2 is an authored, source-pinned presentation-slot association.
    // It is deliberately NOT ToolHolderMapping/ToolPairSeating evidence.
    internal sealed class CadPresentationPairRecord
    {
        public string SlotId { get; set; }
        public string AssociationBasis { get; set; }
        public string EvidenceReference { get; set; }
        public string FrameHandle { get; set; }
        public string FrameLayer { get; set; }
        public CadPresentationRoleRecord Punch { get; set; }
        public CadPresentationRoleRecord Holder { get; set; }
    }
    internal sealed class CadPresentationRoleRecord
    {
        public string RootLayer { get; set; }
        public string[] Layers { get; set; }
        public int EntityCount { get; set; }
    }
    internal sealed class CadPairBinding
    {
        public string ToolId { get; }
        public string SlotId { get; }
        public string SourceSha256 { get; }
        public string ManifestSha256 { get; }
        public string PunchBlock { get; }
        public string PunchInsert { get; }
        public string HolderBlock { get; }
        public string HolderInsert { get; }
        public string PunchRootLayer { get; }
        public string HolderRootLayer { get; }
        public IReadOnlyList<string> PunchLayers { get; }
        public IReadOnlyList<string> HolderLayers { get; }
        public string EvidenceReference { get; }
        public bool Matched { get; }
        public bool ReferenceOnly => true;
        public string CoordinateSystem => "DXF_PRESENTATION_WCS_ONLY";
        public string Status => Matched ? "CAD_REFERENCE" : "UNKNOWN";
        public IReadOnlyList<string> Reasons { get; }
        internal CadPairBinding(string id,string sha,string manifest,MasterToolSelection definition,bool matched,IEnumerable<string> reasons)
        {
            ToolId=id;SourceSha256=sha;ManifestSha256=manifest;Matched=matched;
            SlotId=definition?.PresentationPair?.SlotId;EvidenceReference=definition?.PresentationPair?.EvidenceReference;
            PunchBlock=definition?.PunchBlock;PunchInsert=definition?.PunchRootInsert;
            HolderBlock=definition?.HolderBlock;HolderInsert=definition?.HolderRootInsert;
            PunchRootLayer=definition?.PresentationPair?.Punch?.RootLayer;HolderRootLayer=definition?.PresentationPair?.Holder?.RootLayer;
            PunchLayers=PlacementLists.Copy(definition?.PresentationPair?.Punch?.Layers);
            HolderLayers=PlacementLists.Copy(definition?.PresentationPair?.Holder?.Layers);
            Reasons=PlacementLists.Reasons(reasons);
        }
        internal static CadPairBinding Validate(string id,string sha,string manifest,MasterToolSelection definition,ToolDxfDocument doc,
            IReadOnlyList<ToolDxfEntity> punch,IReadOnlyList<ToolDxfEntity> holder,bool identity)
        {
            var reasons=new List<string>();var pair=definition.PresentationPair;
            if(pair==null||string.IsNullOrWhiteSpace(pair.SlotId)||pair.AssociationBasis!="CompanyAuthorizedDrawingSlot"||string.IsNullOrWhiteSpace(pair.EvidenceReference))
                reasons.Add("MISSING_CAD_SLOT_ASSOCIATION");
            if(!identity)reasons.Add("UNVERIFIED_TOOL_IDENTITY");
            // This company catalog encodes the Punch's ID in its TOOL_* block
            // name. A correct label elsewhere on the sheet must not validate
            // a different complete Punch/Holder/cell triplet under that ID.
            if(!string.Equals(definition.PunchBlock,"TOOL_"+id.Replace('-','_'),StringComparison.OrdinalIgnoreCase))
                reasons.Add("CAD_PUNCH_BLOCK_TOOL_ID_MISMATCH");
            ValidateRole(doc,definition.PunchRootInsert,definition.PunchBlock,pair?.Punch,punch,"PUNCH",reasons);
            ValidateRole(doc,definition.HolderRootInsert,definition.HolderBlock,pair?.Holder,holder,"HOLDER",reasons);
            ValidatePresentationFrame(doc,definition,pair,reasons);
            if(string.Equals(definition.PunchRootInsert,definition.HolderRootInsert,StringComparison.OrdinalIgnoreCase))reasons.Add("CAD_PAIR_REUSES_ROOT_INSERT");
            return new CadPairBinding(id,sha,manifest,definition,reasons.Count==0,reasons);
        }
        private static void ValidateRole(ToolDxfDocument doc,string handle,string block,CadPresentationRoleRecord record,
            IReadOnlyList<ToolDxfEntity> selection,string role,List<string> reasons)
        {
            var roots=doc.RootInserts.Where(i=>string.Equals(i.Handle,handle,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(record==null||record.Layers==null||record.Layers.Length==0||record.Layers.Any(string.IsNullOrWhiteSpace)||
                record.Layers.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=record.Layers.Length||record.EntityCount<=0||
                roots.Length!=1||roots[0].Rows!=1||roots[0].Columns!=1||
                !string.Equals(roots[0].Block,block,StringComparison.OrdinalIgnoreCase)||
                !string.Equals(roots[0].Layer,record.RootLayer,StringComparison.OrdinalIgnoreCase))
            {reasons.Add("CAD_"+role+"_ROOT_IDENTITY_MISMATCH");return;}
            var all=doc.Entities.Where(e=>!e.Annotation&&!e.PaperSpace&&e.InsertHandles.Count>0&&
                string.Equals(e.InsertHandles[0],handle,StringComparison.OrdinalIgnoreCase)).ToArray();
            // Every non-annotation entity of this instance is retained. Missing
            // a difficult SPLINE, reference line or open contour is not allowed.
            var keys=new HashSet<string>(all.Select(Key),StringComparer.Ordinal);
            if(all.Length!=record.EntityCount||selection.Count!=all.Length||keys.Count!=all.Length||!keys.SetEquals(selection.Select(Key))||
                !new HashSet<string>(all.Select(e=>e.Layer),StringComparer.OrdinalIgnoreCase).SetEquals(record.Layers))
                reasons.Add("CAD_"+role+"_INCOMPLETE_GEOMETRY_OR_LAYER_MISMATCH");
        }
        private static string Key(ToolDxfEntity e)=>e.InstancePath+"|"+e.SourceRecord+"|"+e.Handle;
        private static void ValidatePresentationFrame(ToolDxfDocument doc,MasterToolSelection definition,CadPresentationPairRecord pair,List<string> reasons)
        {
            var frames=doc.Entities.Where(e=>e.InsertHandles.Count==0&&e.Handle==pair?.FrameHandle&&e.Layer==pair?.FrameLayer&&!e.PaperSpace).ToArray();
            if(frames.Length!=1||frames[0].Type!="LWPOLYLINE"||frames[0].SourceClosed!=true||frames[0].Curves.Count!=4||
                frames[0].Curves.Any(c=>c.Kind!="LINE"||c.Start.X!=c.End.X&&c.Start.Y!=c.End.Y)||!ToolRegionValidator.Validate(frames).IsValidRegion)
            {reasons.Add("CAD_SLOT_FRAME_MISSING_OR_UNSUPPORTED");return;}
            // This is ONLY a check against the explicitly drawn rectangular
            // presentation cell, using source INSERT positions. It does not
            // derive geometry origins, seating, relative poses or tool lengths.
            var points=frames[0].Curves.Select(c=>c.Start).ToArray();
            double left=points.Min(p=>p.X),right=points.Max(p=>p.X),bottom=points.Min(p=>p.Y),top=points.Max(p=>p.Y);
            foreach(var role in new[]{new {Name="PUNCH",Handle=definition.PunchRootInsert},new {Name="HOLDER",Handle=definition.HolderRootInsert}})
            {
                var roots=doc.RootInserts.Where(i=>string.Equals(i.Handle,role.Handle,StringComparison.OrdinalIgnoreCase)).ToArray();
                if(roots.Length!=1)continue;
                var p=roots[0].PresentationAnchorMm;
                if(p.X<left||p.X>right||p.Y<bottom||p.Y>top)reasons.Add("CAD_"+role.Name+"_OUTSIDE_FIXED_SLOT");
            }
        }
    }
}
