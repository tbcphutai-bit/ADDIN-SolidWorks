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
    // Local operational diagnostics: no CAD coordinates or arbitrary environment dump.
    // Kept with the library boundary so the managed/UI test projects use the same logger.
    internal static class BendCheckRunDiagnostics
    {
        private static readonly object Gate=new object();
        private static readonly Dictionary<string,string> LastReportedState=new Dictionary<string,string>(StringComparer.Ordinal);
        private static readonly int Pid=System.Diagnostics.Process.GetCurrentProcess().Id;
        internal static string LogPath => Environment.GetEnvironmentVariable("ADDIN_BEND_DIAGNOSTIC_FILE") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ADDIN","Logs","CheckChan-"+Pid+".jsonl");
        internal static void Write(string stage,object data=null,Exception error=null,CancellationToken token=default(CancellationToken))
        {
            try
            {
                string line=new JavaScriptSerializer().Serialize(new {Utc=DateTimeOffset.UtcNow.ToString("o"),Pid,
                    Thread=Thread.CurrentThread.ManagedThreadId,Apartment=Thread.CurrentThread.GetApartmentState().ToString(),
                    Mvid=typeof(BendCheckRunDiagnostics).Assembly.ManifestModule.ModuleVersionId.ToString(),Stage=stage,
                    Cancelled=token.IsCancellationRequested,Data=data,
                    Exception=error==null?null:new {Type=error.GetType().FullName,error.Message,Stack=error.ToString()}});
                System.Diagnostics.Trace.WriteLine("[CHECK CHAN] "+line);
                lock(Gate)
                {
                    string path=LogPath;
                    if(!Path.IsPathRooted(path))return;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if(!File.Exists(path)||new FileInfo(path).Length<8*1024*1024)File.AppendAllText(path,line+Environment.NewLine,Encoding.UTF8);
                }
            }
            catch(Exception diagnostic){System.Diagnostics.Trace.WriteLine("[CHECK CHAN] Log unavailable: "+diagnostic.GetType().Name);}
        }
        // Transition-only source diagnostics: new errors still retain exact stack.
        internal static void WriteChanged(string stage,object data=null,Exception error=null,CancellationToken token=default(CancellationToken))
        {
            string signature;
            try { signature=new JavaScriptSerializer().Serialize(data)+"|"+(error?.ToString()??""); }
            catch { signature=(data?.ToString()??"")+"|"+(error?.ToString()??""); }
            lock(Gate)
            {
                string previous;
                if(LastReportedState.TryGetValue(stage,out previous)&&previous==signature)return;
                LastReportedState[stage]=signature;
            }
            Write(stage,data,error,token);
        }
        internal static object Overrides() => new {
            Directory=Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_DIRECTORY"),
            Manifest=Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_MANIFEST"),
            Datum=Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DATUM_EVIDENCE"),
            Placement=Environment.GetEnvironmentVariable("ADDIN_BEND_PLACEMENT_CONFIG"),
            MasterMode=Environment.GetEnvironmentVariable("ADDIN_BEND_USE_MASTER_PAIRS")};
    }

    // One read transaction records the bytes actually consumed by every nested
    // managed reader. No COM object, geometry inference or machine settings enter it.
    internal sealed class ToolSourceReadScope : IDisposable
    {
        [ThreadStatic] private static ToolSourceReadScope active;
        private readonly ToolSourceReadScope parent;
        private readonly Dictionary<string,string> files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        private bool inconsistent;
        private readonly string environment=ToolProfileSourceSettings.OverrideStamp();
        private readonly CancellationToken cancellation;
        internal string ConfigurationKey;
        internal ToolSourceReadScope(CancellationToken cancellation=default(CancellationToken)) {this.cancellation=cancellation; parent=active; active=this; }
        internal static void Attempt(string path)
        {
            if(active==null||string.IsNullOrWhiteSpace(path))return;
            string full=Path.GetFullPath(path);
            if(!active.files.ContainsKey(full))active.files.Add(full,File.Exists(full)?"UNREAD":"MISSING");
        }
        // A direct open distinguishes a genuinely absent file from an
        // unavailable one. File.Exists is only a metadata probe; false can
        // also mean access was denied and must not certify source absence.
        internal static void Missing(string path)
        {
            if(active==null||string.IsNullOrWhiteSpace(path))return;
            string full=Path.GetFullPath(path),prior;
            if(active.files.TryGetValue(full,out prior)&&prior!="MISSING"&&prior!="UNREAD")active.inconsistent=true;
            active.files[full]="MISSING";
        }
        internal static void Unavailable(string path)
        {
            if(active==null||string.IsNullOrWhiteSpace(path))return;
            active.files[Path.GetFullPath(path)]="UNREAD";
        }
        internal static void Observed(string path,byte[] bytes)
        {
            if(active==null||string.IsNullOrWhiteSpace(path))return;
            string full=Path.GetFullPath(path),sha=ToolProfileLibraryProvider.Hash(bytes),prior;
            if(active.files.TryGetValue(full,out prior)&&prior!="UNREAD"&&prior!="MISSING"&&prior!=sha)active.inconsistent=true;
            active.files[full]=sha;
        }
        internal ToolSourceIdentity Seal(CancellationToken token)
        {
            var identity=new ToolSourceIdentity(ConfigurationKey,files,inconsistent,environment);
            try {if(!identity.Validate(token))identity.Invalidate();return identity;}
            catch {identity.Invalidate();throw;}
        }
        internal ToolSourceIdentity Seal() => Seal(cancellation);
        internal static ToolSourceReadScope Active => active;
        public void Dispose() { active=parent; }
    }
    internal sealed class ToolSourceIdentity
    {
        private readonly IReadOnlyDictionary<string,string> files;
        private readonly List<FileSystemWatcher> watchers=new List<FileSystemWatcher>();
        private readonly object gate=new object();
        private readonly string environment;
        private int current=1;
        internal string ConfigurationKey { get; }
        internal string Fingerprint { get; }
        internal bool Current => Volatile.Read(ref current)==1;
        internal ToolSourceIdentity(string key,IDictionary<string,string> reads,bool inconsistent,string environment=null)
        {
            ConfigurationKey=key;
            files=new Dictionary<string,string>(reads,StringComparer.OrdinalIgnoreCase);
            this.environment=environment??ToolProfileSourceSettings.OverrideStamp();
            Fingerprint=ToolProfileLibraryProvider.Hash(Encoding.UTF8.GetBytes("EffectiveToolSourceV1\n"+key+"\n"+this.environment+"\n"+
                string.Join("\n",files.OrderBy(f=>f.Key,StringComparer.OrdinalIgnoreCase).Select(f=>f.Key+":"+f.Value))));
            if(inconsistent||files.Values.Any(v=>v=="UNREAD")){current=0;return;}
            // Watchers only revoke a lease. Hashing/decoding remains on managed
            // workers; callbacks never touch WinForms, SOLIDWORKS, or RCWs.
            try
            {
                lock(gate) foreach(string folder in files.Keys.Select(ExistingParent).Where(d=>d!=null).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var watcher=new FileSystemWatcher(folder) { IncludeSubdirectories=true,
                        NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.Size|NotifyFilters.FileName|NotifyFilters.DirectoryName };
                    watcher.Changed+=Changed;watcher.Created+=Changed;watcher.Deleted+=Changed;
                    watcher.Renamed+=(s,e)=>{if(Affects(e.OldFullPath)||Affects(e.FullPath))Invalidate();};
                    watcher.Error+=(s,e)=>Invalidate();
                    watchers.Add(watcher);watcher.EnableRaisingEvents=true;
                }
            }
            catch(IOException){Invalidate();}
            catch(UnauthorizedAccessException){Invalidate();}
            catch(ArgumentException){Invalidate();}
        }
        private static string ExistingParent(string path)
        {
            string folder=Path.GetDirectoryName(path);
            while(!string.IsNullOrEmpty(folder)&&!Directory.Exists(folder))folder=Path.GetDirectoryName(folder);
            return folder;
        }
        private bool Affects(string path) => files.Keys.Any(f=>string.Equals(f,path,StringComparison.OrdinalIgnoreCase)||
            f.StartsWith(path.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
        private void Changed(object sender,FileSystemEventArgs e) { if(Affects(e.FullPath))Invalidate(); }
        internal bool Validate(CancellationToken token)
        {
            if(!Current||environment!=ToolProfileSourceSettings.OverrideStamp()) { Invalidate();return false; }
            foreach(var file in files)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if(file.Value=="MISSING")
                    {
                        // A false metadata probe cannot distinguish missing
                        // from unreadable. Only these open errors prove absence.
                        try { using(var probe=new FileStream(file.Key,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) { } }
                        catch(FileNotFoundException) { continue; }
                        catch(DirectoryNotFoundException) { continue; }
                        Invalidate();return false;
                    }
                    // Hash bytes, not timestamps: same-size/same-timestamp edits
                    // must invalidate manifest, workbook and review evidence too.
                    using(var stream=new FileStream(file.Key,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                    using(var hash=SHA256.Create())
                    {
                        if(stream.Length>64*1024*1024) { Invalidate();return false; }
                        var buffer=new byte[65536];int count;long total=0,length=stream.Length;
                        while((count=stream.Read(buffer,0,buffer.Length))>0)
                        {
                            token.ThrowIfCancellationRequested();total+=count;
                            if(total>64*1024*1024){Invalidate();return false;}
                            hash.TransformBlock(buffer,0,count,buffer,0);
                        }
                        hash.TransformFinalBlock(new byte[0],0,0);
                        if(stream.Length!=length||total!=length){Invalidate();return false;}
                        string observed=BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant();
                        if(observed!=file.Value) { Invalidate();return false; }
                    }
                }
                catch(IOException){Invalidate();return false;}
                catch(UnauthorizedAccessException){Invalidate();return false;}
            }
            if(environment!=ToolProfileSourceSettings.OverrideStamp())Invalidate();
            return Current;
        }
        internal void Invalidate()
        {
            Interlocked.Exchange(ref current,0);
            lock(gate) { foreach(var watcher in watchers)watcher.Dispose();watchers.Clear(); }
        }
    }

    internal sealed class ToolProfileSourceConfiguration
    {
        public int Version { get; set; } = 1;
        public string DirectoryPath { get; set; }
        public string ManifestPath { get; set; }
        public string DatumEvidencePath { get; set; }
        public string PlacementConfigurationPath { get; set; }
        public bool UseMasterPairsAsLibrary { get; set; }
    }
    internal sealed class ToolProfileSourceSettings
    {
        private readonly string path;
        internal static string DefaultSettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ADDIN","bend-tool-dxf-source.json");
        internal ToolProfileSourceSettings() : this(DefaultSettingsPath) { }
        internal ToolProfileSourceSettings(string path) { this.path=Path.GetFullPath(path); }

        internal static readonly string[] OverrideNames={"ADDIN_BEND_TOOL_DXF_DIRECTORY","ADDIN_BEND_TOOL_DXF_MANIFEST",
            "ADDIN_BEND_TOOL_DATUM_EVIDENCE","ADDIN_BEND_PLACEMENT_CONFIG","ADDIN_BEND_USE_MASTER_PAIRS"};
        internal static string OverrideStamp() => new JavaScriptSerializer().Serialize(OverrideNames.Select(n=>Environment.GetEnvironmentVariable(n)).ToArray());

        // Read only file metadata and process overrides (no source/CAD file read).
        internal static string QuickConfigurationStamp()
        {
            try
            {
                var file=new FileInfo(DefaultSettingsPath);
                string local=file.Exists ? file.LastWriteTimeUtc.Ticks+":"+file.Length : "MISSING";
                return local+"|"+Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_DIRECTORY")+"|"+
                    Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_MANIFEST")+"|"+
                    Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DATUM_EVIDENCE")+"|"+
                    Environment.GetEnvironmentVariable("ADDIN_BEND_PLACEMENT_CONFIG")+"|"+
                    Environment.GetEnvironmentVariable("ADDIN_BEND_USE_MASTER_PAIRS");
            }
            catch(Exception ex) { return "CONFIG_PROBE_FAILED:"+ex.GetType().Name; }
        }

        // Only called after the operator previews and explicitly approves.
        // No workbook, DXF, original manifest or other source is modified.
        internal void SaveApprovedSelection(ToolProfileSourceConfiguration selected,ToolProfileLibrarySnapshot preview,CancellationToken token=default(CancellationToken))
        {
            token.ThrowIfCancellationRequested();
            if(selected==null||selected.Version!=1)throw new InvalidDataException("Invalid source selection.");
            if(OverrideNames.Any(n=>Environment.GetEnvironmentVariable(n)!=null))
                throw new InvalidOperationException("An ADDIN tool-source override is active; remove it explicitly before saving local settings.");
            if(!selected.UseMasterPairsAsLibrary)
            {
                if(string.IsNullOrWhiteSpace(selected.DirectoryPath)||!Path.IsPathRooted(selected.DirectoryPath)||
                   !Directory.Exists(selected.DirectoryPath))throw new InvalidDataException("Choose an existing absolute DXF folder.");
                if(!string.IsNullOrWhiteSpace(selected.ManifestPath))
                {
                    string manifest=Path.IsPathRooted(selected.ManifestPath) ? selected.ManifestPath :
                        Path.Combine(selected.DirectoryPath,selected.ManifestPath);
                    if(!File.Exists(manifest))throw new FileNotFoundException("Selected DXF manifest is missing.",manifest);
                }
            }
            if(!string.IsNullOrWhiteSpace(selected.PlacementConfigurationPath))
            {
                string placement=Path.IsPathRooted(selected.PlacementConfigurationPath) ?
                    selected.PlacementConfigurationPath :
                    Path.Combine(string.IsNullOrWhiteSpace(selected.DirectoryPath)?Path.GetDirectoryName(path):selected.DirectoryPath,
                        selected.PlacementConfigurationPath);
                if(string.IsNullOrWhiteSpace(placement)||!File.Exists(placement))
                    throw new FileNotFoundException("Selected placement configuration is missing.",placement);
            }
            if(selected.UseMasterPairsAsLibrary&&string.IsNullOrWhiteSpace(selected.PlacementConfigurationPath))
                throw new InvalidDataException("Fixed CAD pairs require an explicit placement configuration.");
            if(preview==null||preview.EffectiveSources==null||preview.SelectionProblem(selected)!=null||
                preview.EffectiveSources.ConfigurationKey!=ToolProfileLibraryProvider.Hash(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(selected)))||
                !preview.EffectiveSources.Validate(token))
                throw new InvalidDataException("SOURCE_PREVIEW_STALE_OR_NOT_VALIDATED");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(selected),new UTF8Encoding(false));
                token.ThrowIfCancellationRequested();
                if(File.Exists(path))File.Replace(temporary,path,path+".bak",true);
                else File.Move(temporary,path);
            }
            finally { if(File.Exists(temporary))File.Delete(temporary); }
        }
        internal ToolProfileSourceConfiguration Load()
        {
            BendCheckRunDiagnostics.WriteChanged("ToolSourceSettings.Load.begin",new {SettingsPath=path,FileExists=File.Exists(path),
                LocalApplicationData=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Overrides=BendCheckRunDiagnostics.Overrides()});
            try
            {
                string readState;
                var loaded=LoadCore(out readState);
                BendCheckRunDiagnostics.WriteChanged("ToolSourceSettings.Load.end",new {SettingsPath=path,FileExists=File.Exists(path),
                    SourceState=readState,loaded.Version,loaded.DirectoryPath,loaded.ManifestPath,loaded.DatumEvidencePath,loaded.PlacementConfigurationPath,loaded.UseMasterPairsAsLibrary});
                return loaded;
            }
            catch(Exception ex)
            {
                string code=ex is UnauthorizedAccessException||ex is System.Security.SecurityException?"SOURCE_SETTINGS_ACCESS_DENIED":
                    ex is InvalidDataException||ex is ArgumentException||ex is FormatException?"SOURCE_SETTINGS_INVALID":
                    ex is IOException?"SOURCE_SETTINGS_IO_FAILED":"SOURCE_SETTINGS_FAILED";
                BendCheckRunDiagnostics.WriteChanged("ToolSourceSettings.Load.error",new {SettingsPath=path,SourceState=code,
                    FileExistsProbe=File.Exists(path),HResult=ex.HResult},ex);
                // Preserve the original exception and stack in diagnostics and
                // InnerException while exposing a stable actionable UI reason.
                throw new InvalidDataException(code+": "+path,ex);
            }
        }
        private ToolProfileSourceConfiguration LoadCore(out string readState)
        {
            ToolSourceReadScope.Attempt(path);
            byte[] bytes=null;
            try
            {
                // Open directly. File.Exists(false) can also mean access/IO
                // failure; it is never the gate for approved settings bytes.
                bytes=BendPlacement.PlacementSourceIo.Read(path,65536,CancellationToken.None);
                readState="SETTINGS_FILE_READ";
            }
            catch(FileNotFoundException) { ToolSourceReadScope.Missing(path);readState="SETTINGS_FILE_MISSING"; }
            catch(DirectoryNotFoundException) { ToolSourceReadScope.Missing(path);readState="SETTINGS_DIRECTORY_MISSING"; }
            catch { ToolSourceReadScope.Unavailable(path);throw; }
            var result=new ToolProfileSourceConfiguration();
            if(bytes!=null)
            {
                result=new JavaScriptSerializer().Deserialize<ToolProfileSourceConfiguration>(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
                if(result==null||result.Version!=1)throw new InvalidDataException("Unsupported DXF source settings version.");
            }
            // Each explicitly supplied override takes precedence independently;
            // an absent override must never clear an approved local path/mode.
            string directory=Environment.GetEnvironmentVariable(OverrideNames[0]);
            string manifest=Environment.GetEnvironmentVariable(OverrideNames[1]);
            string datum=Environment.GetEnvironmentVariable(OverrideNames[2]);
            string placement=Environment.GetEnvironmentVariable(OverrideNames[3]);
            string master=Environment.GetEnvironmentVariable(OverrideNames[4]);
            if(!string.IsNullOrWhiteSpace(directory))result.DirectoryPath=directory;
            if(!string.IsNullOrWhiteSpace(manifest))result.ManifestPath=manifest;
            if(!string.IsNullOrWhiteSpace(datum))result.DatumEvidencePath=datum;
            if(!string.IsNullOrWhiteSpace(placement))result.PlacementConfigurationPath=placement;
            if(master!=null)
            {
                if(master!="0"&&master!="1")throw new InvalidDataException("ADDIN_BEND_USE_MASTER_PAIRS must be 0 or 1.");
                result.UseMasterPairsAsLibrary=master=="1";
            }
            if(result.UseMasterPairsAsLibrary&&!string.IsNullOrWhiteSpace(result.PlacementConfigurationPath)&&
                !Path.IsPathRooted(Environment.ExpandEnvironmentVariables(result.PlacementConfigurationPath)))
                result.PlacementConfigurationPath=BendPlacement.PlacementSourceIo.Resolve(
                    string.IsNullOrWhiteSpace(result.DirectoryPath)?Path.GetDirectoryName(path):result.DirectoryPath,result.PlacementConfigurationPath);
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

    internal sealed class ToolProfileLibraryProvider : IDisposable
    {
        private readonly Func<ToolProfileSourceConfiguration> configuration;
        private readonly Dictionary<string,ToolDxfDocument> documents=new Dictionary<string,ToolDxfDocument>(StringComparer.OrdinalIgnoreCase);
        private readonly object gate=new object();
        private ToolProfileLibrarySnapshot previous;
        private bool disposed;
        internal void Invalidate() { lock(gate)previous?.Invalidate(); }
        public void Dispose() { lock(gate){disposed=true;previous?.Invalidate();documents.Clear();} }
        internal ToolProfileLibraryProvider() : this(new ToolProfileSourceSettings().Load) { }
        internal ToolProfileLibraryProvider(Func<ToolProfileSourceConfiguration> configuration) { this.configuration=configuration; }
        internal static string Hash(byte[] bytes)
        { using (var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        private static string HashText(string text) => Hash(Encoding.UTF8.GetBytes(text));

        internal ToolProfileLibrarySnapshot Read(CancellationToken cancellation)
        {
            lock (gate)
            using(var reads=new ToolSourceReadScope(cancellation))
            {
                if(disposed)throw new ObjectDisposedException(nameof(ToolProfileLibraryProvider));
                BendCheckRunDiagnostics.WriteChanged("ToolLibrary.Read.begin",token:cancellation);
                bool fixedCadMode=previous?.UsesFixedCadPairs==true;
                try
                {
                    cancellation.ThrowIfCancellationRequested(); var config=configuration();
                    reads.ConfigurationKey=HashText(new JavaScriptSerializer().Serialize(config));
                    fixedCadMode=config?.UseMasterPairsAsLibrary==true;
                    if(previous?.Current==true&&previous.EffectiveSources?.ConfigurationKey==reads.ConfigurationKey&&
                        previous.EffectiveSources.Validate(cancellation))return previous;
                    if (config==null || config.Version!=1) throw new InvalidDataException("Unsupported DXF source settings version.");
                    if(config.UseMasterPairsAsLibrary)return Publish(FixedCadMode(ReadMasterPairs(config,cancellation)));
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
                    var evidence=ToolDatumEvidence.Load(config.DatumEvidencePath,cancellation,root:root);
                    foreach (var tool in tools)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        tool.Datums=ToolDatumEvidence.Bind(tool,evidence,cancellation);
                        tool.Validation.DatumEvidence=tool.Datums;
                    }
                    var placement=BendPlacement.BendPlacementSources.Read(config.PlacementConfigurationPath,cancellation,root);
                    configSha=HashText(configSha+"\n"+ToolGeometryTolerance.Version+"\n"+evidence.Fingerprint+"\n"+placement.Fingerprint);
                    var provenance=sources.Values.Select(d=>d.Source).OrderBy(s=>s.Path,StringComparer.OrdinalIgnoreCase).ToList();
                    string fingerprint=HashText(configSha+"\n"+string.Join("\n",provenance.Select(p=>p.Path+":"+p.Sha256+":"+p.Error)));
                    return Publish(new ToolProfileLibrarySnapshot { ConfigurationSha256=configSha,Fingerprint=fingerprint,Tools=tools.AsReadOnly(),Sources=provenance.AsReadOnly(),PlacementSources=placement });
                }
                catch (OperationCanceledException ex) { BendCheckRunDiagnostics.WriteChanged("ToolLibrary.Read.cancelled",error:ex,token:cancellation);throw; }
                catch (Exception ex) { BendCheckRunDiagnostics.WriteChanged("ToolLibrary.Read.error",error:ex,token:cancellation);var failure=Failure(ex.Message);return Publish(fixedCadMode?FixedCadMode(failure):failure); }
            }
        }
        private static ToolProfileLibrarySnapshot FixedCadMode(ToolProfileLibrarySnapshot snapshot)
        {
            snapshot.UsesFixedCadPairs=true;
            snapshot.Fingerprint=HashText("FixedCompanyCadPairsModeV2\n"+snapshot.Fingerprint);
            return snapshot;
        }
        private ToolProfileLibrarySnapshot Publish(ToolProfileLibrarySnapshot snapshot)
        {
            BendCheckRunDiagnostics.WriteChanged("ToolLibrary.Read.end",new {snapshot.Error,snapshot.UsesFixedCadPairs,
                SourceState=snapshot.Error=="DXF source directory is not configured."?"MISSING_SOURCE_CONFIGURATION":
                    snapshot.Error!=null?"INVALID_OR_UNAVAILABLE_SOURCE":"SOURCE_READ_COMPLETED",
                ToolCount=snapshot.Tools.Count,DataCount=snapshot.Tools.Count(t=>t.Validation.PunchGeometryReadComplete),
                MatchedPairs=snapshot.PlacementSources.Tools.Count(t=>t.CadPair?.Matched==true),
                ClampVerified=snapshot.PlacementSources.Tools.Count(t=>t.ClampVerified),snapshot.ConfigurationSha256,
                PlacementReasons=snapshot.PlacementSources.Reasons});
            snapshot.EffectiveSources=ToolSourceReadScope.Active?.Seal();
            snapshot.Fingerprint=HashText(snapshot.Fingerprint+"\n"+snapshot.EffectiveSources?.Fingerprint);
            if(snapshot.EffectiveSources?.Current==false)
            {
                // An unreadable source is reportable as health UNKNOWN, never
                // as usable partial geometry. Keep inventory display possible.
                string fingerprint=snapshot.Fingerprint;
                string error=snapshot.Error??"SOURCE_CHANGED_OR_UNREADABLE_DURING_READ";
                bool fixedMode=snapshot.UsesFixedCadPairs;
                snapshot.Invalidate();
                snapshot=Failure(error);snapshot.UsesFixedCadPairs=fixedMode;snapshot.Fingerprint=fingerprint;
            }
            // A stable failure contains zero geometry/certificates. It may be
            // shared across the publication audit so the recognized bends can
            // be shown with the actual source error instead of disappearing.
            if(previous?.Current==true&&previous.Error!=null&&snapshot.Error!=null&&previous.Fingerprint==snapshot.Fingerprint&&
                previous.Tools.All(t=>!t.Validation.PunchGeometryReadComplete)&&snapshot.Tools.All(t=>!t.Validation.PunchGeometryReadComplete))
            {snapshot.Invalidate();return previous;}
            previous?.Invalidate(); previous=snapshot; return snapshot;
        }
        private static ToolProfileLibrarySnapshot ReadMasterPairs(ToolProfileSourceConfiguration config,CancellationToken token)
        {
            if(string.IsNullOrWhiteSpace(config.PlacementConfigurationPath))return Failure("Company CAD pairs require an explicit placement configuration.");
            string root=string.IsNullOrWhiteSpace(config.DirectoryPath)?Path.GetDirectoryName(ToolProfileSourceSettings.DefaultSettingsPath):
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(config.DirectoryPath));
            string placementPath=BendPlacement.PlacementSourceIo.Resolve(root,config.PlacementConfigurationPath);
            var placement=BendPlacement.BendPlacementSources.Read(placementPath,token);
            var tools=new List<CompanyToolProfile>();
            foreach(string id in CompanyToolIds.All)
            {
                token.ThrowIfCancellationRequested();var pair=placement.Tools.Single(t=>t.Id==id);var parts=new List<ToolGeometryPart>();
                if(pair.Punch!=null)parts.Add(new ToolGeometryPart {Role=ToolGeometryRole.CompanyPunchProfile,Source=pair.Source,Entities=pair.Punch.SourceEntities,Region=pair.Punch.Region});
                if(pair.Holder!=null)parts.Add(new ToolGeometryPart {Role=ToolGeometryRole.HolderClampReference,Source=pair.Source,Entities=pair.Holder.SourceEntities,Region=pair.Holder.Region});
                bool read=pair.CadPair?.Matched==true&&pair.MappingIdentified;
                var validation=new ToolProfileValidationResult {SourceFilesExist=pair.Source?.FileExists==true,PunchGeometryReadComplete=read,
                    PunchEntitiesSupported=pair.Punch?.Supported==true,PunchClosed=read&&pair.Punch.ObservedClosed,
                    Region=pair.Punch?.Region??ToolRegionValidator.Validate(null),Reasons=pair.Reasons,DatumEvidence=pair.MasterDatums};
                tools.Add(new CompanyToolProfile {Id=id,Parts=parts.AsReadOnly(),Validation=validation,Datums=pair.MasterDatums});
            }
            var sources=placement.Tools.Select(t=>t.Source).Where(p=>p!=null).GroupBy(p=>p.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToArray();
            string configSha=HashText("FixedCompanyCadPairsV2\n"+placementPath+"\n"+ToolGeometryTolerance.Version+"\n"+placement.Fingerprint);
            return new ToolProfileLibrarySnapshot {ConfigurationSha256=configSha,Fingerprint=configSha,Tools=tools.AsReadOnly(),
                Sources=Array.AsReadOnly(sources),PlacementSources=placement};
        }
        internal static ToolProfileLibrarySnapshot Failure(string reason)
        {
            var snapshot=new ToolProfileLibrarySnapshot { Error=reason,Fingerprint=HashText("error:"+reason),Sources=Array.AsReadOnly(new ToolProfileProvenance[0]),
                Tools=CompanyToolIds.All.Select(id=>new CompanyToolProfile { Id=id,Parts=Array.AsReadOnly(new ToolGeometryPart[0]),
                    Validation=new ToolProfileValidationResult { Reasons=Array.AsReadOnly(new[] {reason,"Tool-tip datum/frame and mounting geometry have not been verified."}) } }).ToList().AsReadOnly() };
            foreach (var tool in snapshot.Tools)
            {
                tool.Datums=ToolDatumEvidence.Bind(tool,new ToolDatumEvidenceDocument());
                tool.Validation.DatumEvidence=tool.Datums;
            }
            return snapshot;
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
            foreach (var part in parts.Where(p=>p.Role==ToolGeometryRole.CompanyPunchProfile || p.Role==ToolGeometryRole.HolderClampReference || p.Role==ToolGeometryRole.ManufacturerReference))
                if (part.Source.Error==null) part.Region=ToolRegionValidator.Validate(part.Entities,cancellation:cancellation);
            var punch=parts.Where(p=>p.Role==ToolGeometryRole.CompanyPunchProfile).SelectMany(p=>p.Entities).ToList();
            bool supported=punch.Count>0 && punch.All(e=>e.UnsupportedReason==null && e.Curves.Count>0);
            bool closed=complete && ToolProfileTopology.Closed(punch);
            if (!closed) reasons.Add("Punch is open/incomplete; no closure or region was invented.");
            reasons.Add("Tool-tip origin and coordinate frame are unverified; source coordinates were not recentered.");
            reasons.Add("Holder/clamp fit is unverified; reference drawings are not verified mounting CAD.");
            var punchParts=parts.Where(p=>p.Role==ToolGeometryRole.CompanyPunchProfile).ToArray();
            var region=complete ? punchParts.Length==1 ? punchParts[0].Region : ToolRegionValidator.Validate(punch,cancellation:cancellation) : new ToolRegionValidation {
                Status=ToolRegionStatus.NotEvaluated,DistanceToleranceMm=new ToolGeometryTolerance().DistanceMm,
                Issues=Array.AsReadOnly(new[] {"IncompletePunchSelection"}) };
            return new CompanyToolProfile { Id=id,Parts=parts.AsReadOnly(),Validation=new ToolProfileValidationResult {
                SourceFilesExist=files,PunchGeometryReadComplete=complete,PunchEntitiesSupported=supported,PunchClosed=closed,Region=region,
                Reasons=reasons.Distinct().ToList().AsReadOnly() } };
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
            ToolSourceReadScope.Attempt(path);
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
                var bytes=buffer.ToArray();ToolSourceReadScope.Observed(path,bytes);return bytes;
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
