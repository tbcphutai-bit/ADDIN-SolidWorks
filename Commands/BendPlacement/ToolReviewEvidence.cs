using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlacement
{
    internal enum ToolReviewStatus { Missing, Unreviewed, Invalid, Stale, VerifiedSimulation, VerifiedExternalRecord }
    internal sealed class ToolReviewProof
    {
        public string AuthorityKeyId { get; set; }
        public string ReviewerId { get; set; }
        public string ApprovedUtc { get; set; }
        public string ExpiresUtc { get; set; }
        public string ArtifactPath { get; set; }
        public string ArtifactSha256 { get; set; }
        public string PayloadSha256 { get; set; }
        public string SignatureBase64 { get; set; }
        public bool SimulationOnly { get; set; }
    }
    internal sealed class ToolReviewAuthority
    {
        public string KeyId { get; set; }
        public string ReviewerId { get; set; }
        public string[] Purposes { get; set; }
        public string RsaModulusBase64 { get; set; }
        public string RsaExponentBase64 { get; set; }
        public string ValidFromUtc { get; set; }
        public string ValidToUtc { get; set; }
        public bool SimulationOnly { get; set; }
        public bool MayAttestPhysicalInspection { get; set; }
    }
    internal sealed class ToolReviewAuthorities
    {
        public int Version { get; set; }
        public List<ToolReviewAuthority> Keys { get; set; }
    }
    internal sealed class ToolReviewValidation
    {
        private readonly PlacementLease lease=new PlacementLease();
        private readonly ToolReviewStatus observed;
        private readonly DateTime expiry;
        private readonly Func<DateTime> clock;
        public ToolReviewStatus Status => !lease.Current||((observed==ToolReviewStatus.VerifiedExternalRecord||observed==ToolReviewStatus.VerifiedSimulation)&&clock()>=expiry)?ToolReviewStatus.Stale:observed;
        public bool RecordVerified => Status==ToolReviewStatus.VerifiedExternalRecord||Status==ToolReviewStatus.VerifiedSimulation;
        public bool SimulationOnly => Status==ToolReviewStatus.VerifiedSimulation;
        public bool MayAttestPhysicalInspection { get; }
        public string Reason { get; }
        public string PayloadSha256 { get; }
        public string AuthorityConfigurationSha256 { get; }
        public string ReviewerId { get; }
        public string AuthorityKeyId { get; }
        public string ArtifactSha256 { get; }
        public string ApprovedUtc { get; }
        public string ExpiresUtc { get; }
        internal ToolReviewValidation(ToolReviewStatus status,string reason,string payload=null,string authoritySha=null,bool physical=false,DateTime expiry=default(DateTime),Func<DateTime> clock=null,ToolReviewProof proof=null)
        {observed=status;Reason=reason;PayloadSha256=payload;AuthorityConfigurationSha256=authoritySha;MayAttestPhysicalInspection=physical;this.expiry=expiry;this.clock=clock??(()=>DateTime.UtcNow);
            ReviewerId=proof?.ReviewerId;AuthorityKeyId=proof?.AuthorityKeyId;ArtifactSha256=proof?.ArtifactSha256;ApprovedUtc=proof?.ApprovedUtc;ExpiresUtc=proof?.ExpiresUtc;}
        internal void Invalidate()=>lease.Invalidate();
        internal static ToolReviewValidation Missing(string reason="MISSING_REVIEW_EVIDENCE")=>new ToolReviewValidation(ToolReviewStatus.Missing,reason);
    }
    internal sealed class ToolReviewVerifier
    {
        private readonly IReadOnlyList<ToolReviewAuthority> keys;
        private readonly Func<DateTime> clock;
        private readonly List<string> observations=new List<string>();
        public string Fingerprint { get; }
        internal string ObservationFingerprint => ToolEvidenceCanonical.Hash(observations.ToArray());
        public string Error { get; }
        private ToolReviewVerifier(IEnumerable<ToolReviewAuthority> keys,string fingerprint,string error,Func<DateTime> clock)
        {this.keys=PlacementLists.Copy(keys);Fingerprint=fingerprint;Error=error;this.clock=clock??(()=>DateTime.UtcNow);}
        internal static ToolReviewVerifier Load(string path,CancellationToken token,Func<DateTime> clock=null)
        {
            string sha=null;
            try
            {
                if(string.IsNullOrWhiteSpace(path))return new ToolReviewVerifier(null,"NoAuthorities","MISSING_REVIEW_AUTHORITY",clock);
                var bytes=PlacementSourceIo.Read(path,131072,token);sha=PlacementSourceIo.Hash(bytes);var doc=PlacementSourceIo.Json<ToolReviewAuthorities>(bytes);
                if(doc==null||doc.Version!=1||doc.Keys==null||doc.Keys.Count>64||doc.Keys.Any(k=>k==null||string.IsNullOrWhiteSpace(k.KeyId)||string.IsNullOrWhiteSpace(k.ReviewerId)||k.Purposes==null)||
                    doc.Keys.GroupBy(k=>k.KeyId,StringComparer.Ordinal).Any(g=>g.Count()>1))throw new InvalidDataException("Invalid authority configuration.");
                foreach(var k in doc.Keys)
                {
                    if(!Date(k.ValidFromUtc,out var from)||!Date(k.ValidToUtc,out var to)||to<=from)throw new InvalidDataException("Invalid authority validity.");
                    var modulus=Convert.FromBase64String(k.RsaModulusBase64);var exponent=Convert.FromBase64String(k.RsaExponentBase64);
                    if(modulus.Length<256||modulus.Length>1024||modulus[0]<128||exponent.Length==0||exponent.Length>8)throw new InvalidDataException("Unsupported authority RSA key.");
                    using(var rsa=RSA.Create())rsa.ImportParameters(new RSAParameters {Modulus=modulus,Exponent=exponent});
                }
                return new ToolReviewVerifier(doc.Keys,sha,null,clock);
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex) when(ex is InvalidDataException||ex is IOException||ex is ArgumentException||ex is InvalidOperationException||ex is CryptographicException||ex is FormatException||ex is UnauthorizedAccessException)
            {return new ToolReviewVerifier(null,sha??"InvalidAuthorities","INVALID_REVIEW_AUTHORITY",clock);}
        }
        internal ToolReviewValidation Verify(string purpose,string payload,ToolReviewProof proof,string root,CancellationToken token)
        {
            string artifactObservation=null;var result=VerifyCore(purpose,payload,proof,root,token,out artifactObservation);
            observations.Add(ToolEvidenceCanonical.Hash(purpose,payload,artifactObservation,result.Status.ToString(),result.Reason));return result;
        }
        private ToolReviewValidation VerifyCore(string purpose,string payload,ToolReviewProof proof,string root,CancellationToken token,out string artifactObservation)
        {
            artifactObservation="NotRead";
            if(proof==null)return ToolReviewValidation.Missing();
            if(Error!=null)return new ToolReviewValidation(ToolReviewStatus.Unreviewed,Error,payload,Fingerprint);
            try
            {
                if(!ToolDatumEvidence.IsSha256(proof.PayloadSha256)||!string.Equals(payload,proof.PayloadSha256,StringComparison.OrdinalIgnoreCase))return Result(ToolReviewStatus.Stale,"STALE_REVIEW_PAYLOAD",payload);
                var key=keys.SingleOrDefault(k=>k.KeyId==proof.AuthorityKeyId&&k.ReviewerId==proof.ReviewerId&&k.Purposes.Contains(purpose,StringComparer.Ordinal));
                if(key==null||key.SimulationOnly&&!proof.SimulationOnly)return Result(ToolReviewStatus.Invalid,"UNAUTHORIZED_REVIEWER_OR_PURPOSE",payload);
                if(!Date(proof.ApprovedUtc,out var approved)||!Date(proof.ExpiresUtc,out var expiry)||expiry<=approved||approved>clock().AddMinutes(5))return Result(ToolReviewStatus.Invalid,"INVALID_REVIEW_DATES",payload);
                Date(key.ValidFromUtc,out var keyFrom);Date(key.ValidToUtc,out var keyTo);
                if(approved<keyFrom||approved>=keyTo||clock()>=expiry||clock()>=keyTo)return Result(ToolReviewStatus.Stale,"EXPIRED_REVIEW_OR_AUTHORITY",payload);
                if(!ToolDatumEvidence.IsSha256(proof.ArtifactSha256)||string.IsNullOrWhiteSpace(proof.ArtifactPath))return Result(ToolReviewStatus.Invalid,"MISSING_REVIEW_ARTIFACT",payload);
                string full=PlacementSourceIo.Resolve(root,proof.ArtifactPath),basePath=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(!full.StartsWith(basePath,StringComparison.OrdinalIgnoreCase))return Result(ToolReviewStatus.Invalid,"REVIEW_ARTIFACT_OUTSIDE_EVIDENCE_ROOT",payload);
                var bytes=PlacementSourceIo.Read(full,8*1024*1024,token);
                artifactObservation=PlacementSourceIo.Hash(bytes);
                if(!string.Equals(artifactObservation,proof.ArtifactSha256,StringComparison.OrdinalIgnoreCase))return Result(ToolReviewStatus.Stale,"STALE_REVIEW_ARTIFACT",payload);
                using(var rsa=RSA.Create())
                {
                    rsa.ImportParameters(new RSAParameters {Modulus=Convert.FromBase64String(key.RsaModulusBase64),Exponent=Convert.FromBase64String(key.RsaExponentBase64)});
                    if(!rsa.VerifyData(Encoding.UTF8.GetBytes(SignatureContent(purpose,proof)),Convert.FromBase64String(proof.SignatureBase64),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))
                        return Result(ToolReviewStatus.Invalid,"INVALID_REVIEW_SIGNATURE",payload);
                }
                return new ToolReviewValidation(proof.SimulationOnly?ToolReviewStatus.VerifiedSimulation:ToolReviewStatus.VerifiedExternalRecord,"SIGNED_EXTERNAL_RECORD",payload,Fingerprint,
                    key.MayAttestPhysicalInspection&&!proof.SimulationOnly,expiry<keyTo?expiry:keyTo,clock,proof);
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex) when(ex is InvalidDataException||ex is IOException||ex is ArgumentException||ex is FormatException||ex is CryptographicException||ex is InvalidOperationException||ex is UnauthorizedAccessException)
            {return Result(ToolReviewStatus.Invalid,"INVALID_REVIEW_EVIDENCE",payload);}
        }
        private ToolReviewValidation Result(ToolReviewStatus status,string reason,string payload)=>new ToolReviewValidation(status,reason,payload,Fingerprint);
        internal static string SignatureContent(string purpose,ToolReviewProof p)=>"ToolReviewSignatureV1:"+ToolEvidenceCanonical.Hash(purpose,p.PayloadSha256?.ToLowerInvariant(),p.AuthorityKeyId,p.ReviewerId,p.ApprovedUtc,p.ExpiresUtc,p.ArtifactSha256?.ToLowerInvariant(),p.SimulationOnly?"SIMULATION_ONLY":"EXTERNAL");
        internal static bool Date(string value,out DateTime utc)
        {DateTimeOffset dt;bool valid=DateTimeOffset.TryParseExact(value,new[]{"o","yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'","yyyy-MM-dd'T'HH:mm:ss'Z'"},CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out dt)&&dt.Offset==TimeSpan.Zero;utc=valid?dt.UtcDateTime:DateTime.MinValue;return valid;}
    }
    internal static class ToolEvidenceCanonical
    {
        // Length-prefixed values prevent delimiter ambiguity. All coordinates
        // stay inside the private evidence binding; diagnostics expose hashes.
        internal static string Hash(params string[] values)=>PlacementSourceIo.Hash(string.Concat(values.Select(v=>v==null?"-1:":v.Length.ToString(CultureInfo.InvariantCulture)+":"+v)));
        internal static string Number(double value){if(!ToolPoint.Finite(value))throw new InvalidDataException("Non-finite evidence coordinate.");return value==0?"0":value.ToString("R",CultureInfo.InvariantCulture);}
        internal static string Point(ToolPoint p)=>Hash(Number(p.X),Number(p.Y));
        internal static string Transform(PlacementTransform2D t)=>t==null?null:Hash(Number(t.A),Number(t.B),Number(t.C),Number(t.D),Number(t.TxMm),Number(t.TyMm));
    }
}
