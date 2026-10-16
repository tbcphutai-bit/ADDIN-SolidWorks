using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ADDIN.Commands.BendTools
{
    internal sealed class ToolProfileSourceConfiguration
    {
        public int Version { get; set; } = 1;
        public string DirectoryPath { get; set; }
        public string ManifestPath { get; set; }
    }
    internal sealed class ToolProfileSourceSettings
    {
        private readonly string path;
        internal ToolProfileSourceSettings() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ADDIN","bend-tool-dxf-source.json")) { }
        internal ToolProfileSourceSettings(string path) { this.path=Path.GetFullPath(path); }
        internal ToolProfileSourceConfiguration Load()
        {
            string directory=Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(directory)) return new ToolProfileSourceConfiguration { DirectoryPath=directory,
                ManifestPath=Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_MANIFEST") };
            if (!File.Exists(path)) return new ToolProfileSourceConfiguration();
            if (new FileInfo(path).Length>65536) throw new InvalidDataException("DXF source settings exceed 64 KB.");
            var result=new JavaScriptSerializer().Deserialize<ToolProfileSourceConfiguration>(File.ReadAllText(path,Encoding.UTF8));
            if (result==null || result.Version!=1) throw new InvalidDataException("Unsupported DXF source settings version.");
            return result;
        }
    }

    // Optional private manifest; contains selections/evidence, never auto-created
    // beside company CAD. A future mounting-validation phase can extend it.
    internal sealed class ToolProfileManifest
    {
        public int Version { get; set; }
        public List<ToolProfileDefinition> Tools { get; set; }
    }
    internal sealed class ToolProfileDefinition
    {
        public string Id { get; set; }
        public List<ToolProfileSelection> Sources { get; set; }
    }
    internal sealed class ToolProfileSelection
    {
        public string File { get; set; }
        public string Role { get; set; }
        public string ExpectedSha256 { get; set; }
        public string[] Handles { get; set; }
        public string[] Layers { get; set; }
        public string[] Blocks { get; set; }
        public string[] InsertHandles { get; set; }
        public string[] EntityTypes { get; set; }
        public bool SinglePolyline { get; set; }
        internal bool HasSelector => new[] {Handles,Layers,Blocks,InsertHandles,EntityTypes}.Any(a=>a!=null && a.Length>0);
        private static bool Includes(string[] values, string value) => values==null || values.Length==0 || values.Contains(value,StringComparer.OrdinalIgnoreCase);
        internal bool Matches(ToolDxfEntity e) => Includes(Handles,e.Handle) && Includes(Layers,e.Layer) && Includes(EntityTypes,e.Type) &&
            (InsertHandles==null || InsertHandles.Length==0 || e.InsertHandles.Any(h=>InsertHandles.Contains(h,StringComparer.OrdinalIgnoreCase))) &&
            (Blocks==null || Blocks.Length==0 || e.Blocks.Any(b=>Blocks.Contains(b,StringComparer.OrdinalIgnoreCase)));
    }

    internal sealed class ToolProfileLibraryProvider
    {
        private readonly Func<ToolProfileSourceConfiguration> configuration;
        private readonly Dictionary<string,ToolDxfDocument> documents=new Dictionary<string,ToolDxfDocument>(StringComparer.OrdinalIgnoreCase);
        private readonly object gate=new object();
        private ToolProfileLibrarySnapshot previous;
        internal ToolProfileLibraryProvider() : this(new ToolProfileSourceSettings().Load) { }
        internal ToolProfileLibraryProvider(Func<ToolProfileSourceConfiguration> configuration) { this.configuration=configuration; }
        internal static string Hash(byte[] bytes)
        { using (var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        private static string HashText(string text) => Hash(Encoding.UTF8.GetBytes(text));

        internal ToolProfileLibrarySnapshot Read(CancellationToken cancellation)
        {
            lock (gate)
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested(); var config=configuration();
                    if (config==null || config.Version!=1) throw new InvalidDataException("Unsupported DXF source settings version.");
                    if (string.IsNullOrWhiteSpace(config.DirectoryPath)) return Publish(Failure("DXF source directory is not configured."));
                    string expanded=Environment.ExpandEnvironmentVariables(config.DirectoryPath);
                    if (!Path.IsPathRooted(expanded)) throw new InvalidDataException("DXF source directory must be an absolute path.");
                    string root=Path.GetFullPath(expanded);
                    string configSha=HashText(root+"\nCompanyDimensionedV1");
                    List<ToolProfileDefinition> definitions;
                    if (string.IsNullOrWhiteSpace(config.ManifestPath))
                    {
                        definitions=CompanyToolIds.All.Select(id=>new ToolProfileDefinition { Id=id, Sources=new List<ToolProfileSelection> {
                            new ToolProfileSelection { File=id.ToLowerInvariant()+"_DIM.dxf",Role="CompanyPunchProfile",SinglePolyline=true,EntityTypes=new[] {"LWPOLYLINE","POLYLINE"} } } }).ToList();
                    }
                    else
                    {
                        string manifestPath=Path.IsPathRooted(config.ManifestPath) ? config.ManifestPath : Path.Combine(root,config.ManifestPath);
                        var manifestBytes=ReadBytes(manifestPath,1024*1024,cancellation);
                        configSha=HashText(root+"\n"+Path.GetFullPath(manifestPath)+"\n"+Hash(manifestBytes));
                        var manifest=new JavaScriptSerializer().Deserialize<ToolProfileManifest>(Encoding.UTF8.GetString(manifestBytes).TrimStart('\uFEFF'));
                        if (manifest==null || manifest.Version!=1 || manifest.Tools==null) throw new InvalidDataException("Invalid DXF manifest/version.");
                        definitions=manifest.Tools;
                        if (definitions.Any(t=>t==null || !CompanyToolIds.All.Contains(t.Id,StringComparer.OrdinalIgnoreCase)) ||
                            definitions.GroupBy(t=>t.Id,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()!=1)) throw new InvalidDataException("Unknown/duplicate company tool ID in manifest.");
                    }
                    var sources=new Dictionary<string,ToolDxfDocument>(StringComparer.OrdinalIgnoreCase);
                    var tools=new List<CompanyToolProfile>();
                    foreach (string id in CompanyToolIds.All)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var definition=definitions.FirstOrDefault(t=>string.Equals(t.Id,id,StringComparison.OrdinalIgnoreCase));
                        tools.Add(ReadTool(id,definition,root,sources,cancellation));
                    }
                    var provenance=sources.Values.Select(d=>d.Source).OrderBy(s=>s.Path,StringComparer.OrdinalIgnoreCase).ToList();
                    string fingerprint=HashText(configSha+"\n"+string.Join("\n",provenance.Select(p=>p.Path+":"+p.Sha256+":"+p.Error)));
                    return Publish(new ToolProfileLibrarySnapshot { ConfigurationSha256=configSha,Fingerprint=fingerprint,Tools=tools.AsReadOnly(),Sources=provenance.AsReadOnly() });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { return Publish(Failure(ex.Message)); }
            }
        }
        private ToolProfileLibrarySnapshot Publish(ToolProfileLibrarySnapshot snapshot)
        {
            if (previous!=null && previous.Current && previous.Fingerprint==snapshot.Fingerprint) return previous;
            previous?.Invalidate(); previous=snapshot; return snapshot;
        }
        internal static ToolProfileLibrarySnapshot Failure(string reason)
        {
            return new ToolProfileLibrarySnapshot { Error=reason,Fingerprint=HashText("error:"+reason),Sources=Array.AsReadOnly(new ToolProfileProvenance[0]),
                Tools=CompanyToolIds.All.Select(id=>new CompanyToolProfile { Id=id,Parts=Array.AsReadOnly(new ToolGeometryPart[0]),
                    Validation=new ToolProfileValidationResult { Reasons=Array.AsReadOnly(new[] {reason,"Tool-tip datum/frame and mounting geometry have not been verified."}) } }).ToList().AsReadOnly() };
        }
        private CompanyToolProfile ReadTool(string id, ToolProfileDefinition definition, string root, Dictionary<string,ToolDxfDocument> sources, CancellationToken cancellation)
        {
            var reasons=new List<string>(); var parts=new List<ToolGeometryPart>(); bool files=true, complete=true, hasPunch=false;
            var selectedKeys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var overlaps=new HashSet<ToolDxfEntity>();
            if (definition?.Sources==null || definition.Sources.Count==0) { files=complete=false; reasons.Add("No source selection configured for this company tool ID."); }
            else foreach (var selection in definition.Sources)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if (selection==null || string.IsNullOrWhiteSpace(selection.File) || !selection.HasSelector) throw new InvalidDataException("Source requires an explicit file and entity selector.");
                    ToolGeometryRole role;
                    if (!Enum.GetNames(typeof(ToolGeometryRole)).Contains(selection.Role) || !Enum.TryParse(selection.Role,false,out role)) throw new InvalidDataException("Unknown geometry role.");
                    if (role==ToolGeometryRole.CompanyPunchProfile) hasPunch=true;
                    if (role==ToolGeometryRole.VerifiedHolderGeometry)
                    {
                        role=ToolGeometryRole.HolderClampReference;
                        reasons.Add("Requested verified-holder role retained as reference: no mounting verification is performed in Phase 2.");
                    }
                    string path=WithinRoot(root,selection.File); ToolDxfDocument doc;
                    if (!sources.TryGetValue(path,out doc)) { doc=Load(path,cancellation); sources.Add(path,doc); }
                    if (!doc.Source.FileExists) files=false;
                    if (doc.Source.Error!=null)
                    { reasons.Add(Path.GetFileName(path)+": "+doc.Source.Error); if (role==ToolGeometryRole.CompanyPunchProfile) complete=false; }
                    bool hashMatches=string.IsNullOrEmpty(selection.ExpectedSha256) || string.Equals(selection.ExpectedSha256,doc.Source.Sha256,StringComparison.OrdinalIgnoreCase);
                    if (!hashMatches) { complete=false; reasons.Add("Source SHA256 differs from manifest evidence."); }
                    var matches=doc.Entities.Where(e=>!e.PaperSpace && selection.Matches(e)).ToList();
                    bool unambiguous=!selection.SinglePolyline || matches.Count==1 && new[] {"LWPOLYLINE","POLYLINE"}.Contains(matches[0].Type);
                    if (matches.Count==0 || !unambiguous)
                    { if (role==ToolGeometryRole.CompanyPunchProfile) complete=false; reasons.Add("Source selector is empty/ambiguous; no profile chosen automatically."); }
                    var selected=unambiguous && hashMatches ? matches.Where(e=>!e.Annotation && !e.PaperSpace).ToList() : new List<ToolDxfEntity>();
                    foreach (var e in selected)
                    {
                        if (!selectedKeys.Add(path+"|"+e.InstancePath+"|"+e.SourceRecord)) { complete=false; overlaps.Add(e); reasons.Add("Overlapping source selectors; geometry roles are ambiguous."); }
                    }
                    if (role==ToolGeometryRole.CompanyPunchProfile && (selected.Count==0 || selected.Any(e=>e.UnsupportedReason!=null || e.Curves.Count==0) || matches.Count!=selected.Count)) complete=false;
                    foreach (var e in selected.Where(e=>e.UnsupportedReason!=null)) reasons.Add(e.Type+": "+e.UnsupportedReason);
                    foreach (var group in doc.Entities.GroupBy(e=>e.Annotation || e.PaperSpace ? ToolGeometryRole.Annotations : selected.Contains(e) ? role : ToolGeometryRole.UnclassifiedReference))
                        parts.Add(new ToolGeometryPart { Role=group.Key,Source=doc.Source,Entities=group.ToList().AsReadOnly() });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { files=complete=false; reasons.Add(ex.Message); }
            }
            if (overlaps.Count>0)
                parts=parts.SelectMany(p=>p.Entities.GroupBy(e=>overlaps.Contains(e) ? ToolGeometryRole.UnclassifiedReference : p.Role)
                    .Select(g=>new ToolGeometryPart { Role=g.Key,Source=p.Source,Entities=g.ToList().AsReadOnly() })).ToList();
            complete=complete && hasPunch;
            if (!hasPunch) reasons.Add("Company punch selection is missing.");
            var punch=parts.Where(p=>p.Role==ToolGeometryRole.CompanyPunchProfile).SelectMany(p=>p.Entities).ToList();
            bool supported=punch.Count>0 && punch.All(e=>e.UnsupportedReason==null && e.Curves.Count>0);
            bool closed=complete && ToolProfileTopology.Closed(punch);
            if (!closed) reasons.Add("Punch is open/incomplete; no closure or region was invented.");
            reasons.Add("Tool-tip origin and coordinate frame are unverified; source coordinates were not recentered.");
            reasons.Add("Holder/clamp fit is unverified; reference drawings are not verified mounting CAD.");
            return new CompanyToolProfile { Id=id,Parts=parts.AsReadOnly(),Validation=new ToolProfileValidationResult {
                SourceFilesExist=files,PunchGeometryReadComplete=complete,PunchEntitiesSupported=supported,PunchClosed=closed,Reasons=reasons.Distinct().ToList().AsReadOnly() } };
        }
        private ToolDxfDocument Load(string path, CancellationToken cancellation)
        {
            var provenance=new ToolProfileProvenance { Path=path,FileExists=File.Exists(path) };
            try
            {
                var bytes=ReadBytes(path,32*1024*1024,cancellation); provenance.FileExists=true; provenance.Sha256=Hash(bytes); provenance.ByteCount=bytes.Length;
                ToolDxfDocument cached;
                if (documents.TryGetValue(path,out cached) && cached.Source.Sha256==provenance.Sha256) return cached;
                var doc=ToolDxfReader.Read(bytes,provenance,cancellation);
                if (documents.Count>128) documents.Clear(); documents[path]=doc; return doc;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { provenance.Error=ex.Message; return new ToolDxfDocument { Source=provenance }; }
        }
        private static byte[] ReadBytes(string path, int limit, CancellationToken cancellation)
        {
            var stamp=File.GetLastWriteTimeUtc(path);
            using (var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using (var buffer=new MemoryStream())
            {
                long length=stream.Length;
                if (length>limit) throw new InvalidDataException("Source file exceeds read limit.");
                var chunk=new byte[65536]; int count;
                while ((count=stream.Read(chunk,0,chunk.Length))>0)
                {
                    cancellation.ThrowIfCancellationRequested(); buffer.Write(chunk,0,count);
                    if (buffer.Length>limit) throw new InvalidDataException("Source file exceeds read limit.");
                }
                if (length!=buffer.Length || stream.Length!=length || stamp!=File.GetLastWriteTimeUtc(path)) throw new IOException("Source changed while reading; retry after saving.");
                return buffer.ToArray();
            }
        }
        private static string WithinRoot(string root, string file)
        {
            if (!string.Equals(Path.GetExtension(file),".dxf",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Tool source must be a DXF file.");
            string prefix=root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string path=Path.GetFullPath(Path.Combine(root,file));
            if (!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Tool source must stay inside the configured read-only directory.");
            return path;
        }
    }
}
