using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Tools.BendToolAudit
{
    internal sealed class AuditFileLedger
    {
        private readonly Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string Hash(string path) => !File.Exists(path) ? "MISSING" : PlacementSourceIo.Hash(PlacementSourceIo.Read(path, 256 * 1024 * 1024, CancellationToken.None));
        internal void Watch(string path)
        { if (!string.IsNullOrWhiteSpace(path)) { path = Path.GetFullPath(path); if (!hashes.ContainsKey(path)) hashes.Add(path, Hash(path)); } }
        internal bool Current => hashes.All(p => Hash(p.Key) == p.Value);
        internal string Fingerprint => ToolEvidenceCanonical.Hash(hashes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => ToolEvidenceCanonical.Hash(p.Key, p.Value)).ToArray());
        internal void JsonArtifacts(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            Watch(path); if (!File.Exists(path)) return;
            try
            {
                var value = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.DeserializeObject(File.ReadAllText(path));
                Artifacts(value, Path.GetDirectoryName(Path.GetFullPath(path)));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            { } // The contract reader supplies the invalid-document diagnostic.
        }
        private void Artifacts(object value, string root)
        {
            if (value is IDictionary<string, object> d)
            {
                foreach (var p in d)
                {
                    if ((p.Key == "ArtifactPath" || p.Key == "ArtifactFile") && p.Value is string s && !string.IsNullOrWhiteSpace(s))
                    {
                        var path = PlacementSourceIo.Resolve(root, s);
                        if (path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Watch(path);
                    }
                    else Artifacts(p.Value, root);
                }
            }
            else if (value is object[] array) foreach (var x in array) Artifacts(x, root);
        }
    }
    internal static class Program
    {
        internal static void PrivateOutput(string output, IEnumerable<string> sourceDirectories)
        {
            output = Path.GetFullPath(output);
            foreach (var source in sourceDirectories.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                string dir = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
                if (output.Equals(dir, StringComparison.OrdinalIgnoreCase) || output.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("OUTPUT_INSIDE_CAD_OR_WORKBOOK_DIRECTORY");
            }
            for (var d = new DirectoryInfo(output); d != null; d = d.Parent)
            {
                if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("OUTPUT_REPARSE_POINT_REJECTED");
                if (File.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git", "HEAD")))
                    throw new InvalidDataException("PRIVATE_OUTPUT_MUST_BE_OUTSIDE_GIT_CHECKOUT");
            }
            if (File.Exists(output) || Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new InvalidDataException("OUTPUT_MUST_BE_NEW_OR_EMPTY_NO_OVERWRITE");
        }
        private static Dictionary<string, string> Options(string[] args)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (args.Length % 2 != 0) throw new InvalidDataException("OPTIONS_REQUIRE_NAME_VALUE_PAIRS");
            var known = new[] { "--placement", "--dim-directory", "--dim-manifest", "--intake", "--authorities", "--output", "--code-revision" };
            for (int i = 0; i < args.Length; i += 2)
            { if (!known.Contains(args[i]) || d.ContainsKey(args[i])) throw new InvalidDataException("UNKNOWN_OR_DUPLICATE_OPTION"); d.Add(args[i], args[i + 1]); }
            foreach (string key in new[] { "--placement", "--dim-directory", "--output" }) if (!d.ContainsKey(key)) throw new InvalidDataException("MISSING_OPTION:" + key);
            return d;
        }
        internal static void ValidateIntake(AuditIntake p)
        {
            if (p == null || p.Version != 1 || p.SourceRevisions == null || p.LimitApplications == null || p.AxialSetups == null || p.ExpectedRevisions == null ||
                p.SourceRevisions.Count > 20 || p.LimitApplications.Count > 512 || p.AxialSetups.Count > 9216 || p.ExpectedRevisions.Count > 20)
                throw new InvalidDataException("INVALID_INTAKE_VERSION_OR_BUDGET");
            var keys = new[] { "Master", "Workbook" }.Concat(CompanyToolIds.All.Select(id => "DIM:" + id)).ToArray();
            if (p.SourceRevisions.Any(s => s == null || !keys.Contains(s.Key)) || p.SourceRevisions.Select(s => s.Key).Distinct().Count() != p.SourceRevisions.Count ||
                p.ExpectedRevisions.Keys.Any(k => !keys.Contains(k)) ||
                p.LimitApplications.Any(s => s == null || !CompanyToolIds.All.Contains(s.ToolId)) ||
                p.AxialSetups.Any(s => s == null || !CompanyToolIds.All.Contains(s.ToolId) || !new[] { "WorkpiecePunch", "WorkpieceHolder" }.Contains(s.Pair)))
                throw new InvalidDataException("UNKNOWN_OR_DUPLICATE_INTAKE_IDENTITY");
        }
        private static int Main(string[] args) => Run(args);
        internal static int Run(string[] args)
        {
            if (args.Length == 0 || args.Length == 1 && args[0] == "--help")
            {
                Console.WriteLine("Read-only saved-source audit; no SOLIDWORKS required.\nBendToolAudit.exe --placement CONFIG --dim-directory DIM_DIR --output NEW_PRIVATE_DIR [--dim-manifest FILE] [--intake FILE] [--authorities ADMIN_TRUST_FILE] [--code-revision COMMIT]\nExit 0: all 18 ready for a saved-context static check; 2: report produced with blockers; 1: invalid invocation/output or audit failure. No production signing.");
                return 0;
            }
            try
            {
                var o = Options(args); string Get(string k) => o.TryGetValue(k, out var v) ? v : null;
                string configPath = Path.GetFullPath(o["--placement"]), configRoot = Path.GetDirectoryName(configPath), directory = Path.GetFullPath(o["--dim-directory"]), output = Path.GetFullPath(o["--output"]);
                var ledger = new AuditFileLedger(); ledger.JsonArtifacts(configPath);
                var c = PlacementSourceIo.Json<BendPlacementConfiguration>(PlacementSourceIo.Read(configPath, 65536, CancellationToken.None));
                if (c?.Version != 1) throw new InvalidDataException("INVALID_PLACEMENT_CONFIGURATION");
                string Resolve(string path) => PlacementSourceIo.Resolve(configRoot, path);
                var master = Resolve(c.MasterPath); var workbook = Resolve(c.LimitWorkbookPath);
                PrivateOutput(output, new[] { directory, Path.GetDirectoryName(master), Path.GetDirectoryName(workbook) });
                string configuredAuthority = Resolve(c.ReviewAuthoritiesPath), authority = Get("--authorities");
                if (!string.IsNullOrWhiteSpace(authority)) authority = Path.GetFullPath(authority);
                if (!string.Equals(configuredAuthority, authority, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("EXPLICIT_AUTHORITY_MUST_MATCH_ADMINISTERED_PLACEMENT_CONFIGURATION");
                foreach (var path in new[] { master, workbook, Resolve(c.MasterManifestPath), Resolve(c.LimitManifestPath), authority }) ledger.Watch(path);
                foreach (var path in new[] { Resolve(c.MasterDatumEvidencePath), Resolve(c.HolderEvidencePath), Resolve(c.PairSeatingEvidencePath), Get("--intake") }) ledger.JsonArtifacts(path);
                ledger.Watch(Get("--dim-manifest"));
                var provider = new ToolProfileLibraryProvider(() => new ToolProfileSourceConfiguration { DirectoryPath = directory,
                    ManifestPath = Get("--dim-manifest"), PlacementConfigurationPath = configPath });
                var library = provider.Read(CancellationToken.None);
                var intake = new AuditIntake(); string intakeError = null; string evidenceRoot = configRoot;
                if (Get("--intake") != null)
                {
                    evidenceRoot = Path.GetDirectoryName(Path.GetFullPath(Get("--intake")));
                    try { intake = PlacementSourceIo.Json<AuditIntake>(PlacementSourceIo.Read(Get("--intake"), 2 * 1024 * 1024, CancellationToken.None)); ValidateIntake(intake); }
                    catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidDataException || ex is InvalidOperationException)
                    { intake = new AuditIntake(); intakeError = "INVALID_INTAKE_PACKET"; }
                }
                ledger.Watch(PlacementSourceIo.Resolve(evidenceRoot, intake.ModelCapturePath)); ledger.Watch(PlacementSourceIo.Resolve(evidenceRoot, intake.ModelSourcePath));
                var verifier = ToolReviewVerifier.Load(authority, CancellationToken.None);
                var facts = new List<AuditSourceFact>();
                string Observed(string path) => string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : PlacementSourceIo.Hash(PlacementSourceIo.Read(path, 32 * 1024 * 1024, CancellationToken.None));
                facts.Add(new AuditSourceFact { Key = "Master", Path = master, SourceSha256 = Observed(master), SelectionManifestSha256 = Observed(Resolve(c.MasterManifestPath)) });
                facts.Add(new AuditSourceFact { Key = "Workbook", Path = workbook, SourceSha256 = Observed(workbook), SelectionManifestSha256 = Observed(Resolve(c.LimitManifestPath)) });
                foreach (var t in library.Tools)
                {
                    var parts = t.Parts.Where(p => p.Role == ToolGeometryRole.CompanyPunchProfile).ToArray();
                    foreach (var p in t.Parts) ledger.Watch(p.Source.Path);
                    if (parts.Select(p => p.Source.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                        facts.Add(new AuditSourceFact { Key = "DIM:" + t.Id, Path = parts[0].Source.Path, SourceSha256 = parts[0].Source.Sha256,
                            SelectionManifestSha256 = Get("--dim-manifest") == null ? PlacementSourceIo.Hash("CompanyDimensionedV1") : Observed(Get("--dim-manifest")) });
                }
                var context = OfflineModelReader.Read(intake, evidenceRoot, verifier, CancellationToken.None);
                var rows = ToolDataAudit.Evaluate(library, intake, facts, verifier, evidenceRoot, context);
                if (!ReferenceEquals(library, provider.Read(CancellationToken.None)) || !ledger.Current)
                    throw new InvalidDataException("INPUT_CHANGED_DURING_AUDIT_RERUN_REQUIRED");
                if (intakeError != null) foreach (var row in rows)
                { row.Readiness = "UNKNOWN"; row.Issues.Add(new AuditIssue { Code = "STALE_OR_INVALID_EVIDENCE", Field = "Intake", Diagnostic = intakeError, RequiredEvidence = ToolDataAudit.Actions["STALE_OR_INVALID_EVIDENCE"] }); }
                MasterAssemblyManifest manifest = null;
                try { manifest = PlacementSourceIo.Json<MasterAssemblyManifest>(PlacementSourceIo.Read(Resolve(c.MasterManifestPath), 1024 * 1024, CancellationToken.None)); }
                catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidOperationException) { }
                Directory.CreateDirectory(output);
                AuditExport.Report(output, rows, Get("--code-revision"), ledger.Fingerprint);
                AuditExport.Templates(output, library, facts, manifest);
                // Console exposes only counts/codes; detailed diagnostics and
                // source/selection bindings remain in the private report.
                Console.WriteLine("AUDIT_RECORDED tools=" + rows.Count + " parsed=" + rows.Count(r => r.PunchRead == "PARSED") +
                    " holder_selected=" + rows.Count(r => r.HolderSelection == "REFERENCE_SELECTED") + " physical_attested=" + rows.Count(r => r.PhysicalPair == "ATTESTED") +
                    " production_ready=" + rows.Count(r => r.Readiness == "READY_PRODUCTION_STATIC_CHECK"));
                return rows.All(r => r.Readiness == "READY_PRODUCTION_STATIC_CHECK") ? 0 : 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("AUDIT_FAILED " + (ex is InvalidDataException ? ex.Message : ex.GetType().Name)); return 1;
            }
        }
    }
}
