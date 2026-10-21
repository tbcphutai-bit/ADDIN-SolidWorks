using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlanning
{
    internal sealed class ProcessProviderConfiguration
    {
        public int Version { get; set; }
        public string TrustedAuthoritiesPath { get; set; }
        public string AuthoritySha256 { get; set; }
        public List<ProcessProviderSource> Sources { get; set; }
        public List<ProcessProviderSource> FinalStaticSources { get; set; }
    }
    internal sealed class ProcessProviderSource
    {
        public string NativePath { get; set; }
        public string Configuration { get; set; }
        public string PackagePath { get; set; }
        public string PackageSha256 { get; set; }
        public string ExpectedRevision { get; set; }
    }
    // Owns managed DTOs and immutable file observations only. The owning STA
    // periodically requests managed verification and a publication barrier.
    internal sealed partial class ProcessProgramProvider : IDisposable
    {
        private sealed class Entry
        {
            internal string Key;
            internal string Scope, ConfigurationPath, ConfigurationSha;
            internal ProcessProgramImport Result;
            internal Dictionary<string,string> Files;
        }
        private readonly object gate=new object();
        private readonly List<Entry> entries=new List<Entry>();
        private readonly Func<string> configuration;
        private readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        private Task audit;
        private DateTime next=DateTime.MinValue;
        private bool disposed;
        internal long Hits { get; private set; }
        internal long Misses { get; private set; }
        internal ProcessProgramProvider(Func<string> configuration=null)
        { this.configuration=configuration??(()=>Environment.GetEnvironmentVariable("ADDIN_BEND_PROCESS_CONFIG")??Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ADDIN","BendTools","process-provider.json")); }
        internal Task<ProcessProgramImport> ReadAsync(string source,string nativePath,PlacementModelCapture model,ToolProfileLibrarySnapshot library,CancellationToken token) =>
            Task.Run(()=>Read(source,nativePath,model,library,token),token);
        internal ProcessProgramImport Read(string source,string nativePath,PlacementModelCapture model,ToolProfileLibrarySnapshot library,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(model==null)throw new ArgumentNullException(nameof(model));
            try
            {
                string path=configuration();
                string scope=ToolEvidenceCanonical.Hash(source,AssemblyCacheKeys.TokenOf(model));
                if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))
                {lock(gate)foreach(var old in entries.Where(e=>e.Scope==scope))old.Result.Invalidate();return ProcessProgramImport.Missing(model,"PROCESS_PROVIDER_NOT_CONFIGURED");}
                path=Path.GetFullPath(path);var bytes=PlacementSourceIo.Read(path,65536,token);string root=Path.GetDirectoryName(path);
                string configSha=PlacementSourceIo.Hash(bytes);
                lock(gate)foreach(var old in entries.Where(e=>e.Scope==scope&&(e.ConfigurationPath!=path||e.ConfigurationSha!=configSha)))old.Result.Invalidate();
                var config=PlacementSourceIo.Json<ProcessProviderConfiguration>(bytes);
                if(config==null||config.Version!=1||config.Sources==null||config.Sources.Count>1024||config.Sources.Any(s=>s==null))throw new InvalidDataException("INVALID_PROCESS_PROVIDER_CONFIGURATION");
                var matches=config.Sources.Where(s=>!string.IsNullOrWhiteSpace(s.NativePath)&&
                    string.Equals(PlacementSourceIo.Resolve(root,s.NativePath),Path.GetFullPath(nativePath),StringComparison.OrdinalIgnoreCase)&&s.Configuration==model.Configuration).ToArray();
                if(matches.Length!=1)return ProcessProgramImport.Missing(model,matches.Length==0?"PROCESS_PACKAGE_NOT_MAPPED_FOR_PART_CONFIGURATION":"AMBIGUOUS_PROCESS_PROVIDER_SOURCE");
                var match=matches[0];
                if(!ToolDatumEvidence.IsSha256(match.PackageSha256)||string.IsNullOrWhiteSpace(match.ExpectedRevision))throw new InvalidDataException("UNPINNED_PROCESS_PACKAGE_OR_REVISION");
                string package=PlacementSourceIo.Resolve(root,match.PackagePath);string key=ToolEvidenceCanonical.Hash(source,AssemblyCacheKeys.TokenOf(model),
                    AssemblyCacheKeys.TokenOf(library),PlacementSourceIo.Hash(bytes),package,match.PackageSha256,match.ExpectedRevision);
                Entry existing;lock(gate){if(disposed)throw new ObjectDisposedException(nameof(ProcessProgramProvider));existing=entries.FirstOrDefault(e=>e.Key==key);}
                if(existing!=null&&existing.Result.Current&&FilesCurrent(existing.Files,token))
                {lock(gate)Hits++;return existing.Result;}
                existing?.Result.Invalidate();
                string authorities=PlacementSourceIo.Resolve(root,config.TrustedAuthoritiesPath);
                if(!ToolDatumEvidence.IsSha256(config.AuthoritySha256)||string.IsNullOrWhiteSpace(authorities))throw new InvalidDataException("UNPINNED_PROCESS_REVIEW_AUTHORITIES");
                var authorityBytes=PlacementSourceIo.Read(authorities,131072,token);string authoritySha=PlacementSourceIo.Hash(authorityBytes);
                if(!string.Equals(authoritySha,config.AuthoritySha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("STALE_PROCESS_REVIEW_AUTHORITY_CONFIGURATION");
                // Authorities are supplied by trusted local configuration, not
                // by the package being approved; importing its own public key
                // must never confer engineering authority.
                var verifier=ToolReviewVerifier.Load(authorities,token);
                var result=ProcessPackageImporter.Read(package,source,model,library,verifier,token);
                if(!string.Equals(match.PackageSha256,result.PackageSha256,StringComparison.OrdinalIgnoreCase)||match.ExpectedRevision!=result.Revision)
                {result.Invalidate();return ProcessProgramImport.Missing(model,"STALE_PINNED_PROCESS_PACKAGE_OR_REVISION");}
                var files=result.Files.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);
                files[path]=PlacementSourceIo.Hash(bytes);files[authorities]=authoritySha;
                if(!FilesCurrent(files,token)){result.Invalidate();return ProcessProgramImport.Missing(model,"PROCESS_SOURCES_CHANGED_DURING_IMPORT");}
                // A failed read can have only a partial file observation set.
                // Do not retain it: a subsequently supplied missing artifact
                // must be re-read even when the package pin itself is unchanged.
                if(result.Sequence.InputReasons.Count>0)return result;
                lock(gate)
                {
                    if(disposed){result.Invalidate();throw new ObjectDisposedException(nameof(ProcessProgramProvider));}
                    foreach(var old in entries.Where(e=>e.Key==key).ToArray()){old.Result.Invalidate();entries.Remove(old);}
                    entries.Add(new Entry{Key=key,Scope=scope,ConfigurationPath=path,ConfigurationSha=configSha,Result=result,Files=files});Misses++;
                    while(entries.Count>128){entries[0].Result.Invalidate();entries.RemoveAt(0);}
                }
                return result;
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex) when(ex is InvalidDataException||ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is InvalidOperationException||ex is FormatException)
            {return ProcessProgramImport.Missing(model,ex is InvalidDataException?ex.Message:"PROCESS_PROVIDER_READ_FAILED:"+ex.GetType().Name);}
        }
        private static bool FilesCurrent(IDictionary<string,string> files,CancellationToken token)
        {
            try{return files.All(p=>PlacementSourceIo.Hash(PlacementSourceIo.Read(p.Key,4*1024*1024,token))==p.Value);}
            catch(OperationCanceledException){throw;}catch{return false;}
        }
        private void Audit(CancellationToken token)
        {
            Entry[] snapshot;lock(gate)snapshot=entries.ToArray();
            string configured;
            try {configured=configuration();configured=string.IsNullOrWhiteSpace(configured)?null:Path.GetFullPath(configured);}
            catch(Exception ex) when(ex is ArgumentException||ex is IOException||ex is UnauthorizedAccessException||ex is InvalidOperationException)
            {foreach(var entry in snapshot)entry.Result.Invalidate();AuditFinalStatics(null,token);return;}
            AuditFinalStatics(configured,token);
            foreach(var entry in snapshot)
            {token.ThrowIfCancellationRequested();if(entry.Result.Current&&(configured==null||
                !string.Equals(configured,entry.ConfigurationPath,StringComparison.OrdinalIgnoreCase)||!FilesCurrent(entry.Files,token)))entry.Result.Invalidate();}
        }
        internal Task VerifyForPublicationAsync()
        {var token=lifetime.Token;return Task.Run(()=>Audit(token),token);}
        internal void Poll()
        {
            lock(gate)
            {
                if(disposed)return;
                if(audit?.IsCompleted==true){var ignored=audit.Exception;audit=null;next=DateTime.UtcNow.AddSeconds(2);}
                if(audit==null&&DateTime.UtcNow>=next)audit=VerifyForPublicationAsync();
            }
        }
        public void Dispose()
        {
            lock(gate){if(disposed)return;disposed=true;lifetime.Cancel();foreach(var entry in entries)entry.Result.Invalidate();entries.Clear();
                foreach(var entry in finalStatics)entry.Invalidate();finalStatics.Clear();
                if(audit==null||audit.IsCompleted)lifetime.Dispose();else audit.ContinueWith(t=>{var ignored=t.Exception;lifetime.Dispose();},TaskScheduler.Default);}
        }
    }
}
