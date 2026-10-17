using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlacement
{
    internal sealed class MasterAssemblyManifest
    {
        public int Version { get; set; }
        public string MasterSha256 { get; set; }
        public List<MasterToolSelection> Tools { get; set; }
    }
    internal sealed class MasterToolSelection
    {
        public string Id { get; set; }
        public string LabelHandle { get; set; }
        public string ExpectedLabel { get; set; }
        public string PunchRootInsert { get; set; }
        public string PunchBlock { get; set; }
        public ToolProfileSelection Punch { get; set; }
        public string HolderRootInsert { get; set; }
        public string HolderBlock { get; set; }
        public ToolProfileSelection Holder { get; set; }
    }
    internal static class PlacementSourceIo
    {
        internal static byte[] Read(string path,int limit,CancellationToken token)
        {
            DateTime stamp=File.GetLastWriteTimeUtc(path);
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var memory=new MemoryStream())
            {
                long length=file.Length;if(length>limit)throw new InvalidDataException("Placement source read limit exceeded.");
                byte[] chunk=new byte[65536];int n;
                while((n=file.Read(chunk,0,chunk.Length))>0){token.ThrowIfCancellationRequested();memory.Write(chunk,0,n);if(memory.Length>limit)throw new InvalidDataException("Placement source grew beyond read limit.");}
                if(length!=memory.Length||file.Length!=length||stamp!=File.GetLastWriteTimeUtc(path))throw new IOException("Placement source changed during read.");
                return memory.ToArray();
            }
        }
        internal static string Hash(byte[] bytes)=>ToolProfileLibraryProvider.Hash(bytes);
        internal static string Hash(string text)=>Hash(Encoding.UTF8.GetBytes(text??""));
        internal static T Json<T>(byte[] bytes)=>new JavaScriptSerializer {MaxJsonLength=2*1024*1024}.Deserialize<T>(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        internal static string Resolve(string root,string path)=>string.IsNullOrWhiteSpace(path)?null:Path.GetFullPath(Path.Combine(root,Environment.ExpandEnvironmentVariables(path)));
    }
    internal static class BendMasterAssemblyReader
    {
        internal sealed class Result
        {
            internal string Fingerprint;
            internal IReadOnlyList<ToolAssemblyProfile> Tools;
            internal IReadOnlyList<string> Reasons;
        }
        internal static Result Read(string source,string manifestPath,string datumPath,CancellationToken token)
        {
            string sha=null,manifestSha=null;var reasons=new List<string>();var profiles=new List<ToolAssemblyProfile>();
            try
            {
                byte[] bytes=PlacementSourceIo.Read(source,32*1024*1024,token);sha=PlacementSourceIo.Hash(bytes);
                byte[] mapping=PlacementSourceIo.Read(manifestPath,1024*1024,token);manifestSha=PlacementSourceIo.Hash(mapping);
                var manifest=PlacementSourceIo.Json<MasterAssemblyManifest>(mapping);
                if(manifest==null||manifest.Version!=1||manifest.Tools==null||manifest.Tools.Any(t=>t==null||!CompanyToolIds.All.Contains(t.Id,StringComparer.OrdinalIgnoreCase))||
                    manifest.Tools.GroupBy(t=>t.Id,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new InvalidDataException("Invalid master mapping/version/identity.");
                if(!ToolDatumEvidence.IsSha256(manifest.MasterSha256)||!string.Equals(sha,manifest.MasterSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("STALE_MASTER_MAPPING");
                var provenance=new ToolProfileProvenance {Path=source,Sha256=sha,FileExists=true,ByteCount=bytes.Length};
                var doc=ToolDxfReader.Read(bytes,provenance,token);
                if(doc.Source.Error!=null)throw new InvalidDataException("UNSUPPORTED_MASTER_SOURCE");
                var labels=Labels(bytes);var evidence=ToolDatumEvidence.Load(datumPath,token);
                foreach(string id in CompanyToolIds.All)
                {
                    token.ThrowIfCancellationRequested();var r=new List<string>();
                    var definition=manifest.Tools.SingleOrDefault(t=>string.Equals(t.Id,id,StringComparison.OrdinalIgnoreCase));
                    if(definition==null){profiles.Add(Missing(id,sha,manifestSha,"MISSING_MASTER_MAPPING"));continue;}
                    // Both the explicit source handle/text and stable ID are
                    // required. A drawing's location or INSERT order is unused.
                    bool identified=definition.ExpectedLabel!=null&&definition.ExpectedLabel.Split(new[]{' ','|',':',';','\t','#','(',')'},StringSplitOptions.RemoveEmptyEntries).Contains(id,StringComparer.OrdinalIgnoreCase)&&
                        labels.Count(l=>l.Item1==definition.LabelHandle&&l.Item2==definition.ExpectedLabel)==1;
                    if(!identified)r.Add("UNVERIFIED_TOOL_IDENTITY");
                    var punch=Select(doc,definition.Punch,definition.PunchRootInsert,definition.PunchBlock,"PUNCH",r);
                    var holder=Select(doc,definition.Holder,definition.HolderRootInsert,definition.HolderBlock,"HOLDER",r);
                    var temp=new CompanyToolProfile {Id=id,Parts=new[]{new ToolGeometryPart {Role=ToolGeometryRole.CompanyPunchProfile,Source=provenance,Entities=punch}},
                        Validation=new ToolProfileValidationResult {PunchGeometryReadComplete=identified&&punch.Count>0&&punch.All(e=>e.UnsupportedReason==null&&e.Curves.Count>0)}};
                    string geometryHash=ToolDatumEvidence.GeometryHash(temp,token);
                    var p=punch.Count==0?null:new GeometrySelection2D(sha,geometryHash,punch,false,token);
                    var h=holder.Count==0?null:new GeometrySelection2D(sha,null,holder,true,token);
                    var datums=ToolDatumEvidence.Bind(temp,evidence,token);
                    if(p!=null&&(!p.Region.IsValidRegion||!p.ObservedClosed||p.Region.MaximumJointGapMm>1e-10))r.Add("UNVERIFIED_PUNCH_REGION");
                    if(h!=null&&!h.Supported)r.Add("UNSUPPORTED_HOLDER_GEOMETRY");
                    else if(h!=null&&(!h.Region.IsValidRegion||!h.ObservedClosed||h.Region.MaximumJointGapMm>1e-10))r.Add("UNVERIFIED_HOLDER_REGION");
                    r.Add("UNVERIFIED_HOLDER");r.Add("UNVERIFIED_PAIR_PLACEMENT");
                    if(datums.Status!=DatumEvidenceStatus.VerifiedRecord)r.Add("MISSING_DATUM_EVIDENCE");
                    profiles.Add(new ToolAssemblyProfile(id,sha,manifestSha,p,h,datums,identified,r));
                }
                // A shared physical source entity cannot silently belong to two
                // tool slots or both roles. Reused block definitions are fine
                // when their actual INSERT instance keys differ.
                var assignments=profiles.SelectMany(p=>(p.Punch?.EntityKeys??new string[0]).Select(k=>new {p.Id,Key=k,Role="P"})
                    .Concat((p.Holder?.EntityKeys??new string[0]).Select(k=>new {p.Id,Key=k,Role="H"}))).ToArray();
                var overlaps=new HashSet<string>(assignments.GroupBy(a=>a.Key).Where(g=>g.Count()>1).SelectMany(g=>g.Select(a=>a.Id)),StringComparer.OrdinalIgnoreCase);
                profiles=profiles.Select(p=>overlaps.Contains(p.Id)?new ToolAssemblyProfile(p.Id,sha,manifestSha,p.Punch,p.Holder,p.MasterDatums,false,p.Reasons.Concat(new[]{"AMBIGUOUS_MASTER_ROLE"})):p).ToList();
                return new Result {Tools=PlacementLists.Copy(profiles),Reasons=PlacementLists.Reasons(reasons),Fingerprint=PlacementSourceIo.Hash(sha+manifestSha+evidence.Fingerprint)};
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex)
            {
                string reason=ex is InvalidDataException&&ex.Message=="STALE_MASTER_MAPPING"?"STALE_MASTER_MAPPING":"MISSING_OR_INVALID_MASTER_SOURCE";
                reasons.Add(reason);return new Result {Tools=PlacementLists.Copy(CompanyToolIds.All.Select(id=>Missing(id,sha,manifestSha,reason))),Reasons=PlacementLists.Reasons(reasons),
                    Fingerprint=PlacementSourceIo.Hash((source??"")+sha+manifestSha+reason+ex.GetType().Name)};
            }
        }
        private static ToolAssemblyProfile Missing(string id,string sha,string mapping,string reason)=>new ToolAssemblyProfile(id,sha,mapping,null,null,null,false,new[]{reason,"MISSING_DATUM_EVIDENCE","UNVERIFIED_HOLDER"});
        private static IReadOnlyList<ToolDxfEntity> Select(ToolDxfDocument doc,ToolProfileSelection selector,string root,string block,string role,List<string> reasons)
        {
            if(selector==null||!selector.HasSelector||string.IsNullOrWhiteSpace(root)||string.IsNullOrWhiteSpace(block))
            {reasons.Add("MISSING_"+role+"_SELECTION");return new ToolDxfEntity[0];}
            var matching=doc.Entities.Where(e=>selector.Matches(e)&&e.InsertHandles.Count>0&&string.Equals(e.InsertHandles[0],root,StringComparison.OrdinalIgnoreCase)&&
                e.Blocks.Count>0&&string.Equals(e.Blocks[0],block,StringComparison.OrdinalIgnoreCase)).ToArray();
            var selected=matching.Where(e=>!e.Annotation&&!e.PaperSpace&&!Decorative(e.Layer)).ToArray();
            if(matching.Length!=selected.Length)reasons.Add("ANNOTATION_SELECTION_REJECTED");
            if(selected.Length==0){reasons.Add("MISSING_"+role+"_GEOMETRY");return selected;}
            if(selected.Select(e=>e.InstancePath.Split('/').FirstOrDefault(s=>s.Length>0)).Distinct().Count()!=1||
                selected.GroupBy(e=>e.InstancePath+"|"+e.Handle).Any(g=>g.Count()>1))
            {reasons.Add("AMBIGUOUS_"+role+"_INSTANCE");return new ToolDxfEntity[0];}
            return selected;
        }
        private static bool Decorative(string layer)=>new[]{"FRAME","LABEL","DIM_LINES","CAUTION","ANNOTATION"}.Any(k=>
            string.Equals(layer,k,StringComparison.OrdinalIgnoreCase)||layer?.StartsWith(k+"_",StringComparison.OrdinalIgnoreCase)==true);
        private static List<Tuple<string,string>> Labels(byte[] bytes)
        {
            // Identity labels in the master contract are ASCII tool IDs. Decode
            // all bytes losslessly (not as commands) and read model-space TEXT
            // records only; never associate labels by coordinates.
            string text=Encoding.GetEncoding(28591).GetString(bytes);var result=new List<Tuple<string,string>>();
            using(var reader=new StringReader(text))
            {
                string code,value,type=null,handle=null,section=null,payload=null;bool paper=false,sectionRecord=false;
                Action flush=()=>{if(section=="ENTITIES"&&type=="TEXT"&&!paper&&handle!=null&&payload!=null)result.Add(Tuple.Create(handle,payload));};
                while((code=reader.ReadLine())!=null&&(value=reader.ReadLine())!=null)
                {
                    int n;if(!int.TryParse(code.Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out n))continue;
                    value=value.Trim();
                    if(n==0){flush();type=value;handle=payload=null;paper=false;sectionRecord=value=="SECTION";if(value=="ENDSEC")section=null;}
                    else if(n==2&&sectionRecord){section=value;sectionRecord=false;}
                    else if(n==5)handle=value;else if(n==1)payload=value;else if(n==67&&value=="1"||n==410&&!string.Equals(value,"Model",StringComparison.OrdinalIgnoreCase))paper=true;
                }
                flush();
            }
            return result;
        }
    }
}
