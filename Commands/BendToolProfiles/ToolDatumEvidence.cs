using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace ADDIN.Commands.BendTools
{
    // VerifiedRecord means an explicit external review attestation with matching
    // source/selection/artifact hashes. It is NOT a physical inspection by this
    // program, a clamp-fit certificate, or a manufacturing PASS.
    internal enum DatumEvidenceStatus { Missing, Unverified, VerifiedRecord, Stale, Invalid, Unsupported }

    internal sealed class ToolLocalCoordinateFrame
    {
        public ToolPoint OriginMm { get; internal set; }
        public ToolPoint XAxis { get; internal set; }
        public ToolPoint YAxis { get; internal set; }
    }
    internal sealed class ToolDatumSet
    {
        private int stale;
        private DatumEvidenceStatus tipStatus, mountStatus, directionStatus, frameStatus, status;
        private DatumEvidenceStatus Current(DatumEvidenceStatus value) => Volatile.Read(ref stale) == 0 ? value : DatumEvidenceStatus.Stale;
        internal void Invalidate() { Interlocked.Exchange(ref stale, 1); }
        public ToolPoint? TipPointMm { get; internal set; }
        public ToolPoint? MountDatumMm { get; internal set; }
        public ToolPoint? WorkingDirection { get; internal set; }
        public ToolLocalCoordinateFrame LocalCoordinateFrame { get; internal set; }
        public DatumEvidenceStatus TipEvidenceStatus { get => Current(tipStatus); internal set => tipStatus = value; }
        public DatumEvidenceStatus MountEvidenceStatus { get => Current(mountStatus); internal set => mountStatus = value; }
        public DatumEvidenceStatus DirectionEvidenceStatus { get => Current(directionStatus); internal set => directionStatus = value; }
        public DatumEvidenceStatus FrameEvidenceStatus { get => Current(frameStatus); internal set => frameStatus = value; }
        public DatumEvidenceStatus Status { get => Current(status); internal set => status = value; }
        public string SourceSha256 { get; internal set; }
        public string GeometrySha256 { get; internal set; }
        public string EvidenceFileSha256 { get; internal set; }
        public IReadOnlyList<string> Reasons { get; internal set; }
        public bool ClampVerified => false;
    }

    // Companion DTOs only. Nullable coordinates distinguish absent values from
    // a supplied zero. All coordinates refer to the unchanged source-mm frame.
    internal sealed class ToolDatumEvidenceFile
    {
        public int Version { get; set; }
        public List<ToolDatumRecord> Tools { get; set; }
    }
    internal sealed class ToolDatumRecord
    {
        public string Id { get; set; }
        public string SourceSha256 { get; set; }
        public string GeometrySha256 { get; set; }
        public ToolPointDatumRecord TipPointMm { get; set; }
        public ToolPointDatumRecord MountDatumMm { get; set; }
        public ToolDirectionDatumRecord WorkingDirection { get; set; }
        public ToolFrameDatumRecord LocalCoordinateFrame { get; set; }
    }
    internal sealed class ToolVectorRecord
    {
        public double? X { get; set; }
        public double? Y { get; set; }
    }
    internal sealed class ToolPointDatumRecord
    {
        public double? X { get; set; }
        public double? Y { get; set; }
        public ToolDatumApproval Evidence { get; set; }
    }
    internal sealed class ToolDirectionDatumRecord
    {
        public double? X { get; set; }
        public double? Y { get; set; }
        public ToolDatumApproval Evidence { get; set; }
    }
    internal sealed class ToolFrameDatumRecord
    {
        public ToolVectorRecord OriginMm { get; set; }
        public ToolVectorRecord XAxis { get; set; }
        public ToolVectorRecord YAxis { get; set; }
        public ToolDatumApproval Evidence { get; set; }
    }
    internal sealed class ToolDatumApproval
    {
        public string Approval { get; set; }
        public string Method { get; set; }
        public string VerifiedBy { get; set; }
        public string VerifiedAtUtc { get; set; }
        public string ValidUntilUtc { get; set; }
        public string ArtifactFile { get; set; }
        public string ArtifactSha256 { get; set; }
        public string DatumPayloadSha256 { get; set; }
    }

    internal sealed class ToolDatumEvidenceDocument
    {
        internal readonly Dictionary<string, ToolDatumRecord> Records = new Dictionary<string, ToolDatumRecord>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<ToolDatumApproval, DatumEvidenceStatus> Approvals = new Dictionary<ToolDatumApproval, DatumEvidenceStatus>();
        internal string Error, FileSha256, Fingerprint;
    }

    internal static class ToolDatumEvidence
    {
        private static readonly Regex Sha = new Regex("\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant);
        private static readonly Regex Zone = new Regex("(Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant);
        internal static bool IsSha256(string value) => value != null && Sha.IsMatch(value);
        private static string Hash(string text) => ToolProfileLibraryProvider.Hash(Encoding.UTF8.GetBytes(text));
        internal static string GeometryHash(CompanyToolProfile tool, CancellationToken cancellation = default(CancellationToken))
        {
            // Includes exact selected instances, transforms (already reflected
            // in curves), flags and source bytes. A manifest selecting another
            // outline in the SAME DXF cannot reuse the former datum evidence.
            var selected = tool.Parts.Where(p => p.Role == ToolGeometryRole.CompanyPunchProfile)
                .SelectMany(p => p.Entities.Select(e => new { SourceSha256 = p.Source.Sha256, Entity = e }))
                .OrderBy(p => p.SourceSha256, StringComparer.Ordinal).ThenBy(p => p.Entity.InstancePath, StringComparer.Ordinal).ThenBy(p => p.Entity.SourceRecord);
            // Stream the canonical binding instead of allocating another full
            // coordinate JSON. Even an over-budget region keeps source identity
            // without turning one large profile into a whole-library failure.
            using (var hash = SHA256.Create())
            using (var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                {
                    Write(writer, "PunchBindingV1"); Write(writer, tool.Id);
                    foreach (var item in selected)
                    {
                        cancellation.ThrowIfCancellationRequested(); var e = item.Entity;
                        Write(writer, "ENTITY"); Write(writer, item.SourceSha256); Write(writer, e.Type); Write(writer, e.Handle);
                        Write(writer, e.SourceRecord.ToString(CultureInfo.InvariantCulture)); Write(writer, e.InstancePath);
                        Write(writer, e.SourceClosed?.ToString()); Write(writer, e.UnsupportedReason);
                        Write(writer, e.Transform == null ? "NoTransform" : "Transform");
                        if (e.Transform != null) foreach (double n in new[] { e.Transform.A, e.Transform.B, e.Transform.C, e.Transform.D, e.Transform.Tx, e.Transform.Ty }) Number(writer, n);
                        int index = 0;
                        foreach (var curve in e.Curves ?? new ToolCurve[0])
                        {
                            if ((index++ & 127) == 0) cancellation.ThrowIfCancellationRequested();
                            if (curve == null) { Write(writer, "NullCurve"); continue; }
                            Write(writer, "CURVE"); Write(writer, curve.Kind); Write(writer, curve.SourceSegment.ToString(CultureInfo.InvariantCulture));
                            foreach (double n in new[] { curve.Start.X, curve.Start.Y, curve.End.X, curve.End.Y, curve.Center.X, curve.Center.Y, curve.RadiusMm, curve.SweepRadians }) Number(writer, n);
                        }
                        Write(writer, "END_ENTITY");
                    }
                }
                stream.FlushFinalBlock(); return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
            }
        }
        private static void Write(StreamWriter writer, string value)
        { writer.Write(value == null ? "-1:" : value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value); }
        private static void Number(StreamWriter writer, double value) => Write(writer, value.ToString("R", CultureInfo.InvariantCulture));

        internal static ToolDatumEvidenceDocument Load(string path, CancellationToken cancellation, DateTimeOffset? clock = null, string root = null)
        {
            var result = new ToolDatumEvidenceDocument();
            if (string.IsNullOrWhiteSpace(path)) { result.Fingerprint = Hash("NoDatumEvidenceConfigured"); return result; }
            var fingerprint = new List<string> { path };
            try
            {
                path = Environment.ExpandEnvironmentVariables(path);
                path = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root ?? Directory.GetCurrentDirectory(), path));
                fingerprint.Add(path);
                byte[] bytes = Read(path, 1024 * 1024, cancellation);
                result.FileSha256 = ToolProfileLibraryProvider.Hash(bytes); fingerprint.Add(result.FileSha256);
                var file = new JavaScriptSerializer().Deserialize<ToolDatumEvidenceFile>(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
                if (file == null || file.Version != 1 || file.Tools == null || file.Tools.Count > CompanyToolIds.All.Count ||
                    file.Tools.Any(t => t == null || !CompanyToolIds.All.Contains(t.Id, StringComparer.OrdinalIgnoreCase)) ||
                    file.Tools.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                    throw new InvalidDataException("Invalid datum evidence document/version/tool IDs.");
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));
                var artifacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in file.Tools)
                {
                    cancellation.ThrowIfCancellationRequested(); result.Records.Add(record.Id, record);
                    foreach (var approval in new[] { record.TipPointMm?.Evidence, record.MountDatumMm?.Evidence,
                        record.WorkingDirection?.Evidence, record.LocalCoordinateFrame?.Evidence }.Where(a => a != null))
                    {
                        var status = CheckApproval(approval, directory, artifacts, fingerprint, cancellation, clock ?? DateTimeOffset.UtcNow);
                        result.Approvals[approval] = status; fingerprint.Add(status.ToString());
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { result.Error = ex.GetType().Name + ": datum evidence could not be read/validated."; result.Records.Clear(); fingerprint.Add(result.Error); }
            result.Fingerprint = Hash(string.Join("\n", fingerprint)); return result;
        }
        private static DatumEvidenceStatus CheckApproval(ToolDatumApproval approval, string directory, Dictionary<string, string> artifacts,
            List<string> fingerprint, CancellationToken cancellation, DateTimeOffset now)
        {
            if (approval.Approval == null || approval.Approval == "Unverified") return DatumEvidenceStatus.Unverified;
            DateTimeOffset reviewed, until;
            if (approval.Approval != "Verified" || !new[] { "MeasuredTool", "ApprovedDrawing", "ApprovedInspection" }.Contains(approval.Method) ||
                string.IsNullOrWhiteSpace(approval.VerifiedBy) || !Date(approval.VerifiedAtUtc, out reviewed) || reviewed > now.AddMinutes(5) ||
                !IsSha256(approval.ArtifactSha256) || string.IsNullOrWhiteSpace(approval.ArtifactFile)) return DatumEvidenceStatus.Invalid;
            if (approval.ValidUntilUtc != null && (!Date(approval.ValidUntilUtc, out until) || until <= reviewed)) return DatumEvidenceStatus.Invalid;
            if (approval.ValidUntilUtc != null && Date(approval.ValidUntilUtc, out until) && now >= until) return DatumEvidenceStatus.Stale;
            try
            {
                // Evidence artifacts stay beside/in the private evidence tree;
                // no URLs, execution, CAD exports or traversal reads are made.
                if (Path.IsPathRooted(approval.ArtifactFile)) return DatumEvidenceStatus.Invalid;
                string path = Path.GetFullPath(Path.Combine(directory, approval.ArtifactFile));
                if (!path.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return DatumEvidenceStatus.Invalid;
                string sha;
                if (!artifacts.TryGetValue(path, out sha))
                {
                    if (artifacts.Count >= 72) return DatumEvidenceStatus.Unsupported;
                    sha = ToolProfileLibraryProvider.Hash(Read(path, 8 * 1024 * 1024, cancellation)); artifacts.Add(path, sha);
                }
                fingerprint.Add(path + ":" + sha);
                return string.Equals(sha, approval.ArtifactSha256, StringComparison.OrdinalIgnoreCase) ? DatumEvidenceStatus.VerifiedRecord : DatumEvidenceStatus.Stale;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { fingerprint.Add("ArtifactFailure:" + ex.GetType().Name); return DatumEvidenceStatus.Invalid; }
        }
        private static bool Date(string text, out DateTimeOffset date)
        {
            date = default(DateTimeOffset);
            return text != null && Zone.IsMatch(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
        private static byte[] Read(string path, int limit, CancellationToken cancellation)
        {
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var buffer = new MemoryStream())
            {
                long length = stream.Length;
                if (length > limit) throw new InvalidDataException("Evidence read limit exceeded.");
                var chunk = new byte[65536]; int count;
                while ((count = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested(); buffer.Write(chunk, 0, count);
                    if (buffer.Length > limit) throw new InvalidDataException("Evidence read limit exceeded.");
                }
                if (length != buffer.Length || stream.Length != length || stamp != File.GetLastWriteTimeUtc(path)) throw new IOException("Evidence changed during read.");
                return buffer.ToArray();
            }
        }

        internal static ToolDatumSet Bind(CompanyToolProfile tool, ToolDatumEvidenceDocument document, CancellationToken cancellation = default(CancellationToken))
        {
            var punch = tool.Parts.Where(p => p.Role == ToolGeometryRole.CompanyPunchProfile).ToArray();
            string[] hashes = punch.Select(p => p.Source.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var result = new ToolDatumSet { SourceSha256 = hashes.Length == 1 ? hashes[0] : null, GeometrySha256 = GeometryHash(tool, cancellation), EvidenceFileSha256 = document.FileSha256 };
            var reasons = new List<string>();
            ToolDatumRecord record;
            if (document.Error != null) { reasons.Add(document.Error); return All(result, DatumEvidenceStatus.Invalid, reasons); }
            if (!document.Records.TryGetValue(tool.Id, out record))
            { reasons.Add("No datum record supplied; tip/mount/direction/frame are not inferred from geometry."); return All(result, DatumEvidenceStatus.Missing, reasons); }
            if (!IsSha256(record.SourceSha256) || !IsSha256(record.GeometrySha256))
            { reasons.Add("Datum evidence requires source and selected-geometry SHA256 bindings."); return All(result, DatumEvidenceStatus.Invalid, reasons); }
            if (hashes.Length > 1)
            { reasons.Add("Multi-source datum bindings are not supported by evidence V1."); return All(result, DatumEvidenceStatus.Unsupported, reasons); }
            if (!tool.Validation.PunchGeometryReadComplete || !string.Equals(record.SourceSha256, result.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(record.GeometrySha256, result.GeometrySha256, StringComparison.OrdinalIgnoreCase))
            { reasons.Add("Datum source/selected-geometry binding is stale or geometry is incomplete."); return All(result, DatumEvidenceStatus.Stale, reasons); }
            result.TipEvidenceStatus = PointStatus(record, record.TipPointMm, "TipPointMm", document, out var tip); result.TipPointMm = tip;
            result.MountEvidenceStatus = PointStatus(record, record.MountDatumMm, "MountDatumMm", document, out var mount); result.MountDatumMm = mount;
            if (record.WorkingDirection == null) result.DirectionEvidenceStatus = DatumEvidenceStatus.Missing;
            else
            {
                result.WorkingDirection = Vector(record.WorkingDirection.X, record.WorkingDirection.Y, true);
                result.DirectionEvidenceStatus = result.WorkingDirection.HasValue ? Approval(record.WorkingDirection.Evidence, document,
                    PointPayloadHash(record, "WorkingDirection", record.WorkingDirection.X, record.WorkingDirection.Y)) : DatumEvidenceStatus.Invalid;
            }
            var f = record.LocalCoordinateFrame;
            if (f == null) result.FrameEvidenceStatus = DatumEvidenceStatus.Missing;
            else
            {
                ToolPoint? origin = Vector(f.OriginMm?.X, f.OriginMm?.Y, false), x = Vector(f.XAxis?.X, f.XAxis?.Y, true), y = Vector(f.YAxis?.X, f.YAxis?.Y, true);
                if (!origin.HasValue || !x.HasValue || !y.HasValue || Math.Abs(x.Value.X * y.Value.X + x.Value.Y * y.Value.Y) > 1e-10 ||
                    Math.Abs(x.Value.X * y.Value.Y - x.Value.Y * y.Value.X - 1) > 1e-10) result.FrameEvidenceStatus = DatumEvidenceStatus.Invalid;
                else
                {
                    result.LocalCoordinateFrame = new ToolLocalCoordinateFrame { OriginMm = origin.Value, XAxis = x.Value, YAxis = y.Value };
                    result.FrameEvidenceStatus = Approval(f.Evidence, document, FramePayloadHash(record));
                }
            }
            var statuses = new[] { result.TipEvidenceStatus, result.MountEvidenceStatus, result.DirectionEvidenceStatus, result.FrameEvidenceStatus };
            result.Status = statuses.Contains(DatumEvidenceStatus.Invalid) ? DatumEvidenceStatus.Invalid : statuses.Contains(DatumEvidenceStatus.Stale) ? DatumEvidenceStatus.Stale :
                statuses.Contains(DatumEvidenceStatus.Unsupported) ? DatumEvidenceStatus.Unsupported : statuses.All(s => s == DatumEvidenceStatus.VerifiedRecord) ? DatumEvidenceStatus.VerifiedRecord :
                statuses.All(s => s == DatumEvidenceStatus.Missing) ? DatumEvidenceStatus.Missing : DatumEvidenceStatus.Unverified;
            reasons.Add("Tip, mounting point, working direction and local frame have independent evidence states; no clamp fit is verified.");
            reasons.Add("Tip=" + result.TipEvidenceStatus + "; Mount=" + result.MountEvidenceStatus + "; Direction=" + result.DirectionEvidenceStatus + "; Frame=" + result.FrameEvidenceStatus);
            result.Reasons = reasons.AsReadOnly(); return result;
        }
        private static ToolDatumSet All(ToolDatumSet result, DatumEvidenceStatus status, List<string> reasons)
        {
            result.TipEvidenceStatus = result.MountEvidenceStatus = result.DirectionEvidenceStatus = result.FrameEvidenceStatus = result.Status = status;
            result.Reasons = reasons.AsReadOnly(); return result;
        }
        private static DatumEvidenceStatus PointStatus(ToolDatumRecord owner, ToolPointDatumRecord record, string kind, ToolDatumEvidenceDocument document, out ToolPoint? point)
        {
            point = record == null ? null : Vector(record.X, record.Y, false);
            return record == null ? DatumEvidenceStatus.Missing : !point.HasValue ? DatumEvidenceStatus.Invalid :
                Approval(record.Evidence, document, PointPayloadHash(owner, kind, record.X, record.Y));
        }
        private static DatumEvidenceStatus Approval(ToolDatumApproval approval, ToolDatumEvidenceDocument document, string payloadHash)
        {
            if (approval == null) return DatumEvidenceStatus.Unverified;
            if (!document.Approvals.TryGetValue(approval, out var status)) return DatumEvidenceStatus.Invalid;
            if (status == DatumEvidenceStatus.VerifiedRecord)
            {
                if (!IsSha256(approval.DatumPayloadSha256)) return DatumEvidenceStatus.Invalid;
                if (!string.Equals(approval.DatumPayloadSha256, payloadHash, StringComparison.OrdinalIgnoreCase)) return DatumEvidenceStatus.Stale;
            }
            return status;
        }
        internal static string PointPayloadHash(ToolDatumRecord owner, string kind, double? x, double? y) => Hash(new JavaScriptSerializer().Serialize(new {
            Contract = "ToolDatumPayloadMmV1", owner.Id, owner.SourceSha256, owner.GeometrySha256, Kind = kind, X = x, Y = y }));
        internal static string FramePayloadHash(ToolDatumRecord owner) => Hash(new JavaScriptSerializer().Serialize(new {
            Contract = "ToolDatumPayloadMmV1", owner.Id, owner.SourceSha256, owner.GeometrySha256, Kind = "LocalCoordinateFrame",
            owner.LocalCoordinateFrame.OriginMm, owner.LocalCoordinateFrame.XAxis, owner.LocalCoordinateFrame.YAxis }));
        private static ToolPoint? Vector(double? x, double? y, bool unit)
        {
            if (!x.HasValue || !y.HasValue || !ToolPoint.Finite(x.Value) || !ToolPoint.Finite(y.Value) || Math.Abs(x.Value) > 10000000 || Math.Abs(y.Value) > 10000000) return null;
            if (unit && Math.Abs(x.Value * x.Value + y.Value * y.Value - 1) > 1e-10) return null;
            return new ToolPoint(x.Value, y.Value); // no normalization or coordinate repair
        }
    }
}
