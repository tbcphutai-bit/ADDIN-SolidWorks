using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlanning
{
    internal sealed class FinalStaticSetupDocument
    {
        public int Version {get;set;}
        public string Revision {get;set;}
        public List<ToolAxialSetupRecord> Records {get;set;}
    }
    internal sealed class FinalStaticSetupImport
    {
        private readonly PlacementLease lease=new PlacementLease();
        internal string Key,Scope,ConfigurationPath;
        internal Dictionary<string,string> Files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        internal IReadOnlyList<SheetEnvelopeCapture> Envelopes=PlacementLists.Copy<SheetEnvelopeCapture>(null);
        internal IReadOnlyList<ToolAxialSetupEvidence> Setups=PlacementLists.Copy<ToolAxialSetupEvidence>(null);
        internal IReadOnlyList<string> Reasons=PlacementLists.Copy<string>(null);
        internal string Revision;
        internal bool Current=>lease.Current&&Envelopes.All(e=>e.Current)&&Setups.All(s=>s.Occupancy.Current);
        internal bool FileLeaseCurrent=>lease.Current;
        internal void Invalidate()=>lease.Invalidate();
    }
    internal sealed partial class ProcessProgramProvider
    {
        private readonly List<FinalStaticSetupImport> finalStatics=new List<FinalStaticSetupImport>();
        internal long StaticHits {get;private set;}
        internal long StaticMisses {get;private set;}
        internal Task<FinalStaticSetupImport> ReadFinalStaticAsync(string source,string nativePath,PlacementModelCapture model,
            ToolProfileLibrarySnapshot library,CancellationToken token)=>Task.Run(()=>ReadFinalStatic(source,nativePath,model,library,token),token);
        internal FinalStaticSetupImport ReadFinalStatic(string source,string nativePath,PlacementModelCapture model,ToolProfileLibrarySnapshot library,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();if(model==null)throw new ArgumentNullException(nameof(model));
            var output=new FinalStaticSetupImport();string scope=ToolEvidenceCanonical.Hash(source,AssemblyCacheKeys.TokenOf(model));
            try
            {
                if(library==null||library.Error!=null||library.PlacementSources?.Current==false)throw new InvalidDataException("FINAL_STATIC_TOOL_LIBRARY_UNAVAILABLE");
                string path=configuration();
                if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))throw new InvalidDataException("FINAL_STATIC_PROVIDER_NOT_CONFIGURED");
                path=Path.GetFullPath(path);string root=Path.GetDirectoryName(path);var bytes=PlacementSourceIo.Read(path,65536,token);
                output.Files[path]=PlacementSourceIo.Hash(bytes);var config=PlacementSourceIo.Json<ProcessProviderConfiguration>(bytes);
                if(config?.Version!=1||(config.FinalStaticSources?.Count??0)>1024||config.FinalStaticSources?.Any(s=>s==null)==true)throw new InvalidDataException("INVALID_FINAL_STATIC_CONFIGURATION");
                var matches=(config.FinalStaticSources??new List<ProcessProviderSource>()).Where(s=>!string.IsNullOrWhiteSpace(s.NativePath)&&
                    string.Equals(PlacementSourceIo.Resolve(root,s.NativePath),Path.GetFullPath(nativePath),StringComparison.OrdinalIgnoreCase)&&s.Configuration==model.Configuration).ToArray();
                if(matches.Length!=1)throw new InvalidDataException(matches.Length==0?"FINAL_STATIC_NOT_MAPPED_FOR_NATIVE_PART":"AMBIGUOUS_FINAL_STATIC_SOURCE");
                var match=matches[0];
                if(!ToolDatumEvidence.IsSha256(match.PackageSha256)||string.IsNullOrWhiteSpace(match.ExpectedRevision))throw new InvalidDataException("UNPINNED_FINAL_STATIC_REVISION");
                string key=ToolEvidenceCanonical.Hash("FinalNativeStaticV1",scope,AssemblyCacheKeys.TokenOf(library),path,output.Files[path],match.PackageSha256,match.ExpectedRevision);
                lock(gate)
                {
                    if(disposed)throw new ObjectDisposedException(nameof(ProcessProgramProvider));
                    foreach(var old in finalStatics.Where(e=>e.Scope==scope&&e.Key!=key))old.Invalidate();
                    var held=finalStatics.SingleOrDefault(e=>e.Key==key&&e.Current);
                    if(held!=null&&FilesCurrent(held.Files,token)){StaticHits++;return held;}
                }
                string package=PlacementSourceIo.Resolve(root,match.PackagePath);var data=PlacementSourceIo.Read(package,4*1024*1024,token);
                output.Files[package]=PlacementSourceIo.Hash(data);
                var document=new System.Web.Script.Serialization.JavaScriptSerializer {MaxJsonLength=4*1024*1024}.Deserialize<FinalStaticSetupDocument>(System.Text.Encoding.UTF8.GetString(data).TrimStart('\uFEFF'));
                if(!string.Equals(output.Files[package],match.PackageSha256,StringComparison.OrdinalIgnoreCase)||document?.Version!=1||document.Revision!=match.ExpectedRevision||document.Records==null||document.Records.Count==0||document.Records.Count>4096||
                    document.Records.Any(r=>r==null||!CompanyToolIds.All.Contains(r.ToolId,StringComparer.Ordinal)||!model.Bends.Any(b=>b.FeatureId==r.FeatureId)||
                        r.Pair!="WorkpiecePunch"&&r.Pair!="WorkpieceHolder")||
                    document.Records.GroupBy(r=>r.ToolId+"|"+r.FeatureId+"|"+r.Pair,StringComparer.Ordinal).Any(g=>g.Count()!=1))
                    throw new InvalidDataException("STALE_OR_AMBIGUOUS_FINAL_STATIC_RECORDS");
                string authority=PlacementSourceIo.Resolve(root,config.TrustedAuthoritiesPath);
                if(!ToolDatumEvidence.IsSha256(config.AuthoritySha256)||string.IsNullOrWhiteSpace(authority))throw new InvalidDataException("UNPINNED_FINAL_STATIC_AUTHORITY");
                output.Files[authority]=PlacementSourceIo.Hash(PlacementSourceIo.Read(authority,131072,token));
                if(!string.Equals(output.Files[authority],config.AuthoritySha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("STALE_FINAL_STATIC_AUTHORITY");
                var verifier=ToolReviewVerifier.Load(authority,token);string evidenceRoot=Path.GetDirectoryName(package);
                foreach(var record in document.Records.Where(r=>r.Review!=null))
                {
                    string artifact=ProcessPackageImporter.SafePath(evidenceRoot,record.Review.ArtifactPath);
                    output.Files[artifact]=PlacementSourceIo.Hash(PlacementSourceIo.Read(artifact,4*1024*1024,token));
                }
                var envelopes=model.Bends.Where(b=>document.Records.Any(r=>r.FeatureId==b.FeatureId)).Select(b=>
                    PrismaticEnvelopeCompiler.Read(model,b,token).WithAdditionalFreshness(()=>output.FileLeaseCurrent)).ToArray();
                var setups=document.Records.Select(r=>ToolAxialSetupEvidence.Read(r,envelopes.Single(e=>e.Binding.FeatureId==r.FeatureId),library,verifier,evidenceRoot,token)).ToArray();
                if(!FilesCurrent(output.Files,token))throw new InvalidDataException("FINAL_STATIC_SOURCE_CHANGED_DURING_IMPORT");
                output.Envelopes=PlacementLists.Copy(envelopes);output.Setups=PlacementLists.Copy(setups);output.Key=key;output.Scope=scope;
                output.ConfigurationPath=path;output.Revision=ToolEvidenceCanonical.Hash(document.Revision,output.Files[package],config.AuthoritySha256);
                output.Reasons=PlacementLists.Reasons(setups.SelectMany(s=>s.Occupancy.Reasons));
                lock(gate)
                {
                    if(disposed){output.Invalidate();throw new ObjectDisposedException(nameof(ProcessProgramProvider));}
                    foreach(var old in finalStatics.Where(e=>e.Scope==scope).ToArray()){old.Invalidate();finalStatics.Remove(old);}
                    finalStatics.Add(output);StaticMisses++;
                    while(finalStatics.Count>128){finalStatics[0].Invalidate();finalStatics.RemoveAt(0);}
                }
                return output;
            }
            catch(OperationCanceledException){output.Invalidate();throw;}
            catch(Exception ex) when(ex is InvalidDataException||ex is IOException||ex is ArgumentException||ex is UnauthorizedAccessException||ex is InvalidOperationException||ex is FormatException)
            {
                lock(gate)foreach(var old in finalStatics.Where(e=>e.Scope==scope))old.Invalidate();
                output.Invalidate();output.Envelopes=PlacementLists.Copy<SheetEnvelopeCapture>(null);output.Setups=PlacementLists.Copy<ToolAxialSetupEvidence>(null);
                output.Reasons=PlacementLists.Reasons(new[]{ex is InvalidDataException?ex.Message:"FINAL_STATIC_READ_FAILED:"+ex.GetType().Name});return output;
            }
        }
        private void AuditFinalStatics(string configured,CancellationToken token)
        {
            FinalStaticSetupImport[] rows;lock(gate)rows=finalStatics.ToArray();
            foreach(var row in rows){token.ThrowIfCancellationRequested();if(configured==null||row.ConfigurationPath!=configured||!FilesCurrent(row.Files,token))row.Invalidate();}
        }
    }
}
