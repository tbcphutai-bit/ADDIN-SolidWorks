using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendPlanning;

namespace ADDIN.UI
{
    // Authoring/recovery only. None of these objects is an evidence verifier.
    internal sealed class HolderReviewAssignment
    {
        public string EntityKey {get;set;}
        public string Role {get;set;}
        public string ContourId {get;set;}
        public string ParentId {get;set;}
    }
    internal sealed class HolderReviewRecovery
    {
        public string Format {get;set;} = "UnverifiedHolderEditorRecoveryV1";
        public string SourcePin {get;set;}
        public string NativeContext {get;set;}
        public string Text {get;set;}
        public string ImportRoot {get;set;}
        public List<HolderReviewAssignment> Assignments {get;set;}
        public bool GridEdited {get;set;}
        public bool TextEdited {get;set;}
    }
    internal static class HolderReviewEditorData
    {
        private static readonly string[] MaterialRoles={"Solid","Hole","Island"};
        private static readonly string[] ExclusionRoles={"Decorative","Centerline","Reference","Annotation"};
        private static JavaScriptSerializer Json()=>new JavaScriptSerializer {MaxJsonLength=8*1024*1024,RecursionLimit=64};
        internal static string SourcePin(HolderMaterialReviewDraft d)=>ToolEvidenceCanonical.Hash("HolderEditorSourceV1",d.ToolId,d.MasterSha256,d.ManifestSha256,d.BindingSha256);
        internal static string NativeContext(SheetEnvelopeCapture s)=>s==null?"NO_NATIVE_CONTEXT":s.Binding.Canonical;
        internal static string ContextKey(HolderMaterialReviewDraft d,SheetEnvelopeCapture s)=>ToolEvidenceCanonical.Hash(SourcePin(d),NativeContext(s));
        internal static List<HolderReviewAssignment> ReadAssignments(HolderMaterialReviewDraft draft,ToolEvidenceReviewProposal p)
        {
            // Existing source/child-record identity checks, without changing
            // signature status or assuming that a coordinate is approved.
            p=ToolEvidenceReviewWorkspace.Read(ToolEvidenceReviewWorkspace.Serialize(p),draft);
            ToolEvidenceReviewWorkspace.ValidateRecordIdentities(p,draft);
            var rows=draft.Rows.ToDictionary(r=>r.EntityKey,r=>new HolderReviewAssignment {EntityKey=r.EntityKey,Role="NeedsReview"},StringComparer.Ordinal);
            var used=new HashSet<string>(StringComparer.Ordinal);
            var contours=p.Material?.Contours??new List<HolderContourDefinition>();
            if(contours.Count>128||contours.Any(c=>c==null||string.IsNullOrWhiteSpace(c.Id)||!MaterialRoles.Contains(c.MaterialKind)||c.EntityKeys==null||c.EntityKeys.Length==0)||
                contours.Select(c=>c.Id).Distinct(StringComparer.Ordinal).Count()!=contours.Count)throw new InvalidDataException("INVALID_EDITOR_CONTOURS");
            var ids=contours.ToDictionary(c=>c.Id,StringComparer.Ordinal);
            foreach(var c in contours)
            {
                if(c.MaterialKind=="Solid"&&!string.IsNullOrWhiteSpace(c.ParentId)||c.MaterialKind!="Solid"&&string.IsNullOrWhiteSpace(c.ParentId))throw new InvalidDataException("INVALID_EDITOR_PARENT");
                if(!string.IsNullOrWhiteSpace(c.ParentId))
                {
                    HolderContourDefinition parent;
                    if(!ids.TryGetValue(c.ParentId,out parent)||c.MaterialKind=="Hole"&&parent.MaterialKind=="Hole"||c.MaterialKind=="Island"&&parent.MaterialKind!="Hole")throw new InvalidDataException("INVALID_EDITOR_PARENT");
                }
                var ancestry=new HashSet<string>(StringComparer.Ordinal);var node=c;
                while(node!=null)
                {
                    if(!ancestry.Add(node.Id))throw new InvalidDataException("CYCLIC_EDITOR_PARENT");
                    if(string.IsNullOrWhiteSpace(node.ParentId)){node=null;continue;}
                    if(!ids.TryGetValue(node.ParentId,out node))throw new InvalidDataException("INVALID_EDITOR_PARENT");
                }
                foreach(string key in c.EntityKeys)
                {
                    if(key==null||!rows.ContainsKey(key)||!used.Add(key))throw new InvalidDataException("FOREIGN_OR_DUPLICATE_EDITOR_ENTITY");
                    rows[key]=new HolderReviewAssignment {EntityKey=key,Role=c.MaterialKind,ContourId=c.Id,ParentId=c.ParentId};
                }
            }
            foreach(var x in p.Material?.Excluded??new List<HolderExcludedEntity>())
            {
                if(x==null||x.EntityKey==null||!rows.ContainsKey(x.EntityKey)||!used.Add(x.EntityKey)||!ExclusionRoles.Contains(x.Role))throw new InvalidDataException("FOREIGN_OR_DUPLICATE_EDITOR_ENTITY");
                rows[x.EntityKey]=new HolderReviewAssignment {EntityKey=x.EntityKey,Role=x.Role};
            }
            return draft.Rows.Select(r=>rows[r.EntityKey]).ToList();
        }
        internal static HolderMaterialDefinition Material(HolderMaterialReviewDraft draft,IEnumerable<HolderReviewAssignment> input,string combination)
        {
            var rows=input.ToArray();
            if(rows.Any(r=>r==null)||rows.Length!=draft.Rows.Count||rows.Select(r=>r.EntityKey).Distinct(StringComparer.Ordinal).Count()!=rows.Length||
                rows.Any(r=>!MaterialRoles.Contains(r.Role)&&!ExclusionRoles.Contains(r.Role)))throw new InvalidDataException("INCOMPLETE_EDITOR_ASSIGNMENTS");
            var contours=rows.Where(r=>MaterialRoles.Contains(r.Role)).GroupBy(r=>r.ContourId,StringComparer.Ordinal).Select(g=>{
                if(string.IsNullOrWhiteSpace(g.Key)||g.Select(r=>r.Role).Distinct().Count()!=1||g.Select(r=>r.ParentId).Distinct().Count()!=1)throw new InvalidDataException("INCONSISTENT_EDITOR_CONTOUR");
                return new HolderContourDefinition {Id=g.Key,MaterialKind=g.First().Role,ParentId=g.First().ParentId,EntityKeys=g.Select(r=>r.EntityKey).ToArray()};}).ToArray();
            var material=HolderMaterialReviewDraftBuilder.PrepareUnsigned(draft,contours,rows.Where(r=>ExclusionRoles.Contains(r.Role)).Select(r=>new HolderExcludedEntity {EntityKey=r.EntityKey,Role=r.Role}),combination);
            var proposal=ToolEvidenceReviewWorkspace.Create(draft);proposal.Material=material;ReadAssignments(draft,proposal);return material;
        }
        internal static bool HasAttestation(ToolEvidenceReviewProposal p)=>p!=null&&(p.Material?.Review!=null||p.Mapping?.Review!=null||p.Seating?.Review!=null||p.Seating?.PhysicalInspectionConfirmed==true||
            p.Datum?.TipPointMm?.Evidence!=null||p.Datum?.MountDatumMm?.Evidence!=null||p.Datum?.WorkingDirection?.Evidence!=null||p.Datum?.LocalCoordinateFrame?.Evidence!=null||
            p.Axial?.Any(r=>r?.Review!=null)==true||p.Simulation?.Any(r=>r?.Review!=null)==true);
        internal static ToolEvidenceReviewProposal ForkUnsigned(HolderMaterialReviewDraft draft,ToolEvidenceReviewProposal original)
        {
            var p=ToolEvidenceReviewWorkspace.Read(ToolEvidenceReviewWorkspace.Serialize(original),draft);
            if(p.Material!=null)p.Material.Review=null;if(p.Mapping!=null)p.Mapping.Review=null;
            if(p.Seating!=null){p.Seating.Review=null;p.Seating.PhysicalInspectionConfirmed=false;}
            if(p.Datum?.TipPointMm!=null)p.Datum.TipPointMm.Evidence=null;if(p.Datum?.MountDatumMm!=null)p.Datum.MountDatumMm.Evidence=null;
            if(p.Datum?.WorkingDirection!=null)p.Datum.WorkingDirection.Evidence=null;if(p.Datum?.LocalCoordinateFrame!=null)p.Datum.LocalCoordinateFrame.Evidence=null;
            foreach(var r in p.Axial??new List<ToolAxialSetupRecord>())if(r!=null)r.Review=null;
            foreach(var r in p.Simulation??new List<CadReferenceSimulationRecord>())if(r!=null)r.Review=null;
            return p;
        }
        internal static string SaveRecovery(string root,HolderReviewRecovery state)
        {
            if(!Path.IsPathRooted(root))throw new InvalidDataException("ABSOLUTE_PRIVATE_RECOVERY_DIRECTORY_REQUIRED");
            Directory.CreateDirectory(root);string text=Json().Serialize(state);
            if(System.Text.Encoding.UTF8.GetByteCount(text)>8*1024*1024)throw new InvalidDataException("EDITOR_RECOVERY_SIZE_LIMIT");
            string path=Path.Combine(root,"holder-draft-"+Guid.NewGuid().ToString("N")+".json");
            using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false)))writer.Write(text);
            return path;
        }
        internal static HolderReviewRecovery ReadRecovery(string text,HolderMaterialReviewDraft current)
        {
            if(text==null||text.Length>8*1024*1024)throw new InvalidDataException("EDITOR_RECOVERY_SIZE_LIMIT");
            var r=Json().Deserialize<HolderReviewRecovery>(text);
            if(r?.Format!="UnverifiedHolderEditorRecoveryV1"||r.SourcePin!=SourcePin(current)||r.Text==null||r.Assignments==null||r.Assignments.Count!=current.Rows.Count||
                r.Assignments.Any(a=>a==null||a.EntityKey==null||a.Role!="NeedsReview"&&!MaterialRoles.Contains(a.Role)&&!ExclusionRoles.Contains(a.Role))||!new HashSet<string>(current.Rows.Select(x=>x.EntityKey),StringComparer.Ordinal).SetEquals(r.Assignments.Select(a=>a.EntityKey))||
                r.Assignments.Select(a=>a.EntityKey).Distinct(StringComparer.Ordinal).Count()!=r.Assignments.Count)throw new InvalidDataException("STALE_OR_INVALID_EDITOR_RECOVERY");
            return r;
        }
    }
}
