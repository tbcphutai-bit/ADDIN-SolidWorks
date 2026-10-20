using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlacement
{
    internal sealed class ToolHolderMappingRecord
    {
        public string ToolId { get; set; }
        public string BindingSha256 { get; set; }
        public string ExpectedLabel { get; set; }
        public string LabelHandle { get; set; }
        public string PunchRootInsert { get; set; }
        public string PunchBlock { get; set; }
        public string HolderRootInsert { get; set; }
        public string HolderBlock { get; set; }
        public string[] HolderEntityKeys { get; set; }
        public ToolReviewProof Review { get; set; }
        internal string Payload()=>ToolEvidenceCanonical.Hash("ToolHolderMappingV1",ToolId,BindingSha256,ExpectedLabel,LabelHandle,PunchRootInsert,PunchBlock,HolderRootInsert,HolderBlock,
            ToolEvidenceCanonical.Hash((HolderEntityKeys??new string[0]).OrderBy(k=>k,StringComparer.Ordinal).ToArray()));
    }
    internal sealed class HolderEvidenceDocument
    {
        public int Version { get; set; }
        public List<HolderMaterialDefinition> Materials { get; set; }
        public List<ToolHolderMappingRecord> Mappings { get; set; }
    }
    internal sealed class PairSeatingDocument
    {
        public int Version { get; set; }
        public List<ToolPairSeatingRecord> Pairs { get; set; }
    }
    internal sealed class ToolHolderEvidenceReader
    {
        private HolderEvidenceDocument holder;
        private PairSeatingDocument seating;
        private readonly ToolReviewVerifier verifier;
        private readonly string holderRoot,seatingRoot;
        private readonly string fileFingerprint;
        public string Fingerprint => ToolEvidenceCanonical.Hash(fileFingerprint,verifier.ObservationFingerprint);
        public string HolderError { get; }
        public string SeatingError { get; }
        internal ToolHolderEvidenceReader(string holderPath,string seatingPath,string authorities,CancellationToken token,string reviewWorkspacePath=null,string masterSha=null,string manifestSha=null)
        {
            verifier=ToolReviewVerifier.Load(authorities,token);string hsha=null,ssha=null;
            try
            {
                holderRoot=Root(holderPath);
                if(!string.IsNullOrWhiteSpace(holderPath))
                {
                    var bytes=PlacementSourceIo.Read(holderPath,1024*1024,token);hsha=PlacementSourceIo.Hash(bytes);holder=PlacementSourceIo.Json<HolderEvidenceDocument>(bytes);
                    if(holder==null||holder.Version!=1)throw new InvalidDataException("Invalid holder evidence version.");
                    if((holder.Materials?.Count??0)>18||(holder.Mappings?.Count??0)>18)throw new InvalidDataException("Holder evidence budget.");
                    ValidateIds(holder.Materials?.Select(m=>m?.ToolId));ValidateIds(holder.Mappings?.Select(m=>m?.ToolId));
                }
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex) when(ex is InvalidDataException||ex is IOException||ex is ArgumentException||ex is InvalidOperationException||ex is UnauthorizedAccessException){holder=null;HolderError="INVALID_HOLDER_EVIDENCE_SOURCE";}
            try
            {
                seatingRoot=Root(seatingPath);
                if(!string.IsNullOrWhiteSpace(seatingPath))
                {
                    var bytes=PlacementSourceIo.Read(seatingPath,1024*1024,token);ssha=PlacementSourceIo.Hash(bytes);seating=PlacementSourceIo.Json<PairSeatingDocument>(bytes);
                    if(seating==null||seating.Version!=1||(seating.Pairs?.Count??0)>18)throw new InvalidDataException("Invalid seating evidence version/budget.");
                    ValidateIds(seating.Pairs?.Select(p=>p?.ToolId));
                }
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex) when(ex is InvalidDataException||ex is IOException||ex is ArgumentException||ex is InvalidOperationException||ex is UnauthorizedAccessException){seating=null;SeatingError="INVALID_PAIR_SEATING_SOURCE";}
            string workspaceSha=null;
            if(!string.IsNullOrWhiteSpace(reviewWorkspacePath))
            {
                try
                {
                    if(!string.IsNullOrWhiteSpace(holderPath)||!string.IsNullOrWhiteSpace(seatingPath))throw new InvalidDataException("AMBIGUOUS_REVIEW_INPUTS");
                    var bytes=PlacementSourceIo.Read(reviewWorkspacePath,4*1024*1024,token);workspaceSha=PlacementSourceIo.Hash(bytes);
                    var proposals=ToolEvidenceReviewWorkspace.ReadDocument(bytes);
                    if(proposals.Any(p=>p.MasterSha256!=masterSha||p.ManifestSha256!=manifestSha||
                        p.Mapping?.ToolId!=p.ToolId||p.Material?.ToolId!=p.ToolId||p.Seating?.ToolId!=p.ToolId||
                        p.Material.BindingSha256!=p.BindingSha256||p.Mapping.BindingSha256!=p.BindingSha256||
                        p.Mapping.PunchRootInsert!=p.PunchInsert||p.Mapping.HolderRootInsert!=p.HolderInsert))
                        throw new InvalidDataException("STALE_REVIEW_WORKSPACE_SOURCE");
                    holderRoot=Root(reviewWorkspacePath);seatingRoot=holderRoot;
                    // These are still untrusted DTOs. Material/Mapping/Seating
                    // below MUST pass their existing independent verifiers.
                    holder=new HolderEvidenceDocument {Version=1,Materials=proposals.Select(p=>p.Material).ToList(),Mappings=proposals.Select(p=>p.Mapping).ToList()};
                    seating=new PairSeatingDocument {Version=1,Pairs=proposals.Select(p=>p.Seating).ToList()};
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex) when(ex is InvalidDataException||ex is IOException||ex is ArgumentException||ex is InvalidOperationException||ex is UnauthorizedAccessException)
                {holder=null;seating=null;HolderError="INVALID_REVIEW_WORKSPACE_SOURCE";SeatingError=HolderError;}
            }
            fileFingerprint=ToolEvidenceCanonical.Hash("HolderEvidenceFilesV2",hsha,ssha,workspaceSha,verifier.Fingerprint,HolderError,SeatingError);
        }
        private static string Root(string path)=>string.IsNullOrWhiteSpace(path)?Directory.GetCurrentDirectory():Path.GetDirectoryName(Path.GetFullPath(path));
        private static void ValidateIds(IEnumerable<string> ids)
        {
            var list=(ids??new string[0]).ToArray();if(list.Any(id=>!CompanyToolIds.All.Contains(id,StringComparer.Ordinal))||list.Distinct(StringComparer.Ordinal).Count()!=list.Length)
                throw new InvalidDataException("Invalid/duplicate tool evidence identity.");
        }
        internal HolderObstacleSet Material(string id,GeometrySelection2D punch,GeometrySelection2D h,string manifest,CancellationToken token)=>
            HolderObstacleSet.Read(id,punch,h,manifest,holder?.Materials?.SingleOrDefault(m=>m.ToolId==id),verifier,holderRoot,token);
        internal ToolReviewValidation Mapping(string id,MasterToolSelection definition,GeometrySelection2D punch,GeometrySelection2D h,string manifest,CancellationToken token)
        {
            var record=holder?.Mappings?.SingleOrDefault(m=>m.ToolId==id);
            if(record==null)return ToolReviewValidation.Missing(h==null?"MAPPING_UNRESOLVED":"MAPPING_SELECTION_UNREVIEWED");
            if(h==null||punch==null||record.BindingSha256!=HolderObstacleSet.Binding(id,punch,h,manifest)||record.ExpectedLabel!=definition.ExpectedLabel||record.LabelHandle!=definition.LabelHandle||
                record.PunchRootInsert!=definition.PunchRootInsert||record.PunchBlock!=definition.PunchBlock||record.HolderRootInsert!=definition.HolderRootInsert||record.HolderBlock!=definition.HolderBlock||
                record.HolderEntityKeys==null||!record.HolderEntityKeys.OrderBy(k=>k,StringComparer.Ordinal).SequenceEqual(h.EntityKeys.OrderBy(k=>k,StringComparer.Ordinal)))
                return new ToolReviewValidation(ToolReviewStatus.Stale,"STALE_OR_WRONG_MAPPING_EVIDENCE");
            return verifier.Verify("ToolHolderMapping",record.Payload(),record.Review,holderRoot,token);
        }
        internal ToolPairSeatingEvidence Seating(string id,string sha,string manifest,GeometrySelection2D punch,GeometrySelection2D h,HolderObstacleSet obstacle,ToolDatumSet datums,CancellationToken token)=>
            ToolPairSeatingEvidence.Read(id,sha,manifest,punch,h,obstacle,datums,seating?.Pairs?.SingleOrDefault(p=>p.ToolId==id),verifier,seatingRoot,token);
    }
}
