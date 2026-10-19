using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendPlanning;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendAssembly
{
    internal enum AssemblyCheckScope { SelectedComponents, EntireAssembly }
    internal enum AssemblyOccurrenceKind { SheetMetal, NonSheetMetal, Suppressed, Lightweight, Unloaded, Subassembly, Unsupported }
    internal sealed class AssemblyOccurrence
    {
        public string Id { get; }
        public string ParentId { get; }
        public string DisplayPath { get; }
        public string SourceIdentity { get; }
        public string Configuration { get; }
        public string TransformSha256 { get; }
        public IReadOnlyList<double> Transform { get; }
        // Exact raw SOLIDWORKS MathTransform.ArrayData, presentation excluded.
        // Indices 9..11 are metres; local geometry DTOs use mm. This metadata
        // maps an occurrence for display; it is never a manufacturing work pose.
        public string TransformContract => "SolidWorksMathTransform/translation-metres/no-presentation";
        public AssemblyOccurrenceKind Kind { get; }
        public bool GeometryShareable { get; }
        public bool Mirrored { get; }
        public bool Modified { get; }
        public bool Virtual { get; }
        public bool Flexible { get; }
        public bool IsAssemblyBranch { get; }
        public string Reason { get; }
        internal bool RigidInstanceTransform => RigidTransform(Transform);
        internal static bool RigidTransform(IReadOnlyList<double> t)
        {
            if(t==null||t.Count<13||t.Any(v=>!ToolPoint.Finite(v))||Math.Abs(t[12]-1)>1e-10)return false;
            for(int a=0;a<3;a++)for(int b=a;b<3;b++)
            {double dot=0;for(int k=0;k<3;k++)dot+=t[a*3+k]*t[b*3+k];if(Math.Abs(dot-(a==b?1:0))>1e-10)return false;}
            double determinant=t[0]*(t[4]*t[8]-t[5]*t[7])-t[1]*(t[3]*t[8]-t[5]*t[6])+t[2]*(t[3]*t[7]-t[4]*t[6]);
            return Math.Abs(determinant-1)<=1e-10;
        }
        internal AssemblyOccurrence(string id, string parent, string display, string source, string config, IEnumerable<double> transform,
            AssemblyOccurrenceKind kind, bool shareable, bool mirrored = false, bool modified = false, bool virtualPart = false, bool flexible = false, string reason = null, bool assemblyBranch = false)
        {
            Id = id; ParentId = parent; DisplayPath = display; SourceIdentity = source; Configuration = config;
            Transform = PlacementLists.Copy(transform); Kind = kind; GeometryShareable = shareable; Mirrored = mirrored; Modified = modified; Virtual = virtualPart; Flexible = flexible; Reason = reason;
            IsAssemblyBranch=assemblyBranch||kind==AssemblyOccurrenceKind.Subassembly;
            TransformSha256 = ToolEvidenceCanonical.Hash(Transform.Select(ToolEvidenceCanonical.Number).ToArray());
        }
        internal string Canonical => ToolEvidenceCanonical.Hash(Id, ParentId, SourceIdentity, Configuration, TransformSha256, Kind.ToString(),
            GeometryShareable.ToString(), Mirrored.ToString(), Modified.ToString(), Virtual.ToString(), Flexible.ToString(), IsAssemblyBranch.ToString(),Reason);
    }
    // An unverified occurrence must never veto capture or collision checking
    // for its normal siblings in a legacy Check Min file/config group.
    internal static class AssemblyOccurrenceEligibility
    {
        internal static string Reason(AssemblyOccurrence occurrence)
        {
            if (occurrence == null) return "MISSING_ASSEMBLY_OCCURRENCE";
            if (!string.IsNullOrWhiteSpace(occurrence.Reason)) return occurrence.Reason;
            if (occurrence.Kind != AssemblyOccurrenceKind.SheetMetal)
                return "UNSUPPORTED_ASSEMBLY_OCCURRENCE:" + occurrence.Kind;
            if (occurrence.Modified) return "ASSEMBLY_MODIFIED_BODY_REQUIRES_INSTANCE_GEOMETRY";
            if (occurrence.Mirrored || !occurrence.RigidInstanceTransform) return "MIRRORED_OR_UNVERIFIED_INSTANCE_TRANSFORM";
            if (occurrence.Flexible) return "FLEXIBLE_ASSEMBLY_CONTEXT_UNVERIFIED";
            if (occurrence.Virtual) return "VIRTUAL_SOURCE_UNVERIFIED";
            if (!occurrence.GeometryShareable) return "INSTANCE_GEOMETRY_NOT_CERTIFIED";
            return null;
        }
        internal static string GroupReason(IEnumerable<AssemblyOccurrence> occurrences)
        {
            var reasons = occurrences.Select(Reason).ToArray();
            if (reasons.Length == 0) return "EMPTY_ASSEMBLY_GROUP";
            if (reasons.Any(r => r == null)) return null;
            return reasons.Distinct(StringComparer.Ordinal).Count() == 1 ? reasons[0] : "NO_CERTIFIED_ASSEMBLY_OCCURRENCE";
        }
        internal static IReadOnlyList<AssemblySourceInput> Partition(string sourceKey, PlacementModelCapture model,
            IEnumerable<AssemblyOccurrence> occurrences, IEnumerable<SheetEnvelopeCapture> envelopes,
            IEnumerable<ToolAxialSetupEvidence> setups, IEnumerable<BendProcessPlan> plans, AssemblyProgramUse programUse,
            string sourceReason, string conditionsRevision, PartSequenceSpace sequence = null, IEnumerable<AssemblyBendMetadata> metadata = null)
        {
            var all = occurrences.ToArray();
            if (all.Length == 0) throw new InvalidOperationException("EMPTY_ASSEMBLY_SOURCE_GROUP");
            var eligible = all.Where(o => Reason(o) == null).ToArray();
            var output = new List<AssemblySourceInput>();
            if (eligible.Length > 0)
                output.Add(new AssemblySourceInput(sourceKey, model, eligible, envelopes, setups, plans, programUse,
                    sourceReason, conditionsRevision, sequence, metadata));
            foreach (var occurrence in all)
            {
                string reason = Reason(occurrence);
                if (reason == null) continue;
                // Retain known bend IDs if available, but never transfer reviewed
                // machine/setup evidence or a reusable certificate to this instance.
                output.Add(new AssemblySourceInput(sourceKey, model, new[] { occurrence },
                    reason: reason, conditionsRevision: conditionsRevision));
            }
            return output;
        }
    }

    internal sealed class AssemblySourceInput
    {
        public string SourceKey { get; }
        public PlacementModelCapture Model { get; }
        public IReadOnlyList<AssemblyOccurrence> Occurrences { get; }
        public IReadOnlyList<SheetEnvelopeCapture> Envelopes { get; }
        public IReadOnlyList<ToolAxialSetupEvidence> Setups { get; }
        public IReadOnlyList<BendProcessPlan> Plans { get; }
        public PartSequenceSpace Sequence { get; }
        public IReadOnlyList<AssemblyBendMetadata> BendMetadata { get; }
        public AssemblyProgramUse ProgramUse { get; }
        public string Reason { get; }
        public string ConditionsRevision { get; }
        internal AssemblySourceInput(string sourceKey, PlacementModelCapture model, IEnumerable<AssemblyOccurrence> occurrences,
            IEnumerable<SheetEnvelopeCapture> envelopes, IEnumerable<ToolAxialSetupEvidence> setups, IEnumerable<BendProcessPlan> plans,
            AssemblyProgramUse use, string reason, string conditionsRevision)
            : this(sourceKey, model, occurrences, envelopes, setups, plans, use, reason, conditionsRevision, null) { }
        internal AssemblySourceInput(string sourceKey, PlacementModelCapture model, IEnumerable<AssemblyOccurrence> occurrences,
            IEnumerable<SheetEnvelopeCapture> envelopes, IEnumerable<ToolAxialSetupEvidence> setups, IEnumerable<BendProcessPlan> plans,
            AssemblyProgramUse use, string reason, string conditionsRevision, PartSequenceSpace sequence)
            : this(sourceKey, model, occurrences, envelopes, setups, plans, use, reason, conditionsRevision, sequence, null) { }
        public bool Current => Model?.Lease.Current != false && Setups.All(s => s.Occupancy.Current) && Plans.All(p => p.InvalidatedReason == null) && Sequence?.InvalidReason == null && ProgramUse.Current;
        internal AssemblySourceInput(string sourceKey, PlacementModelCapture model, IEnumerable<AssemblyOccurrence> occurrences,
            IEnumerable<SheetEnvelopeCapture> envelopes = null, IEnumerable<ToolAxialSetupEvidence> setups = null, IEnumerable<BendProcessPlan> plans = null,
            AssemblyProgramUse use = null, string reason = null, string conditionsRevision = "", PartSequenceSpace sequence = null, IEnumerable<AssemblyBendMetadata> metadata = null)
        {
            SourceKey = sourceKey; Model = model; Occurrences = PlacementLists.Copy(occurrences); Envelopes = PlacementLists.Copy(envelopes);
            Setups = PlacementLists.Copy(setups); Plans = PlacementLists.Copy(plans); Sequence = sequence; BendMetadata = PlacementLists.Copy(metadata);
            Reason = reason ?? (Model!=null&&Occurrences.Any(o=>!string.Equals(o.Configuration,Model.Configuration,StringComparison.OrdinalIgnoreCase))?
                "MISMATCHED_REFERENCED_CONFIGURATION":Occurrences.Select(o=>o.SourceIdentity).Distinct(StringComparer.Ordinal).Count()>1?"MIXED_SOURCE_IDENTITIES":null);
            ProgramUse = use ?? AssemblyProgramUse.PerOccurrence(); ConditionsRevision = conditionsRevision;
            if (string.IsNullOrWhiteSpace(sourceKey) || Occurrences.Count == 0) throw new ArgumentException("Source/occurrence proof required");
        }
    }
    internal sealed class AssemblyProgramUse
    {
        private readonly ToolReviewValidation review;
        private readonly bool controlled;
        public string PayloadSha256 { get; }
        public bool PartLocal { get; }
        public bool SimulationOnly => controlled || review?.SimulationOnly == true;
        public bool Current => review?.Status != ToolReviewStatus.Stale;
        public string SourceKey { get; }
        private AssemblyProgramUse(string source, string payload, bool controlled, ToolReviewValidation review, bool local)
        { SourceKey = source; PayloadSha256 = payload; this.controlled = controlled; this.review = review; PartLocal = local; }
        internal static AssemblyProgramUse PerOccurrence() => new AssemblyProgramUse(null, null, false, null, false);
        internal static string Payload(string source, string program) => ToolEvidenceCanonical.Hash("AssemblyPartLocalProgramUseV1", source, program,
            "LocalPartFrameIndependentOfRigidAssemblyPlacement;UnmodifiedSourcesOnly");
        // Explicit software fixture only, never an authority for company setup.
        internal static AssemblyProgramUse ControlledPartLocal(string source, string programDigest) =>
            new AssemblyProgramUse(source, Payload(source, programDigest), true, null, true);
        internal static AssemblyProgramUse Reviewed(string source, string programDigest, ToolReviewVerifier verifier, ToolReviewProof proof, string root, CancellationToken token)
        {
            string payload = Payload(source, programDigest); var v = verifier.Verify("AssemblyPartLocalProgramUse", payload, proof, root, token);
            return new AssemblyProgramUse(source, payload, false, v, v.RecordVerified);
        }
        internal bool CanShare(AssemblySourceInput source, string programDigest) => source.Reason==null && Current && PartLocal && SourceKey == source.SourceKey &&
            PayloadSha256 == Payload(source.SourceKey, programDigest) && source.Occurrences.All(o => AssemblyOccurrenceEligibility.Reason(o) == null);
    }
    internal sealed class AssemblyBatchRequest
    {
        public string AssemblySnapshotId { get; }
        public string Configuration { get; }
        public AssemblyCheckScope Scope { get; }
        public PlacementLease Lease { get; }
        public ToolProfileLibrarySnapshot Library { get; }
        public IReadOnlyList<AssemblySourceInput> Sources { get; }
        public IReadOnlyList<AssemblyOccurrence> Occurrences { get; }
        public double InventoryMilliseconds { get; }
        public double CaptureMilliseconds { get; }
        public int Captures { get; }
        public int CaptureHits { get; }
        internal AssemblyBatchRequest(string assembly, string config, AssemblyCheckScope scope, PlacementLease lease,
            ToolProfileLibrarySnapshot library, IEnumerable<AssemblySourceInput> sources, IEnumerable<AssemblyOccurrence> occurrences,
            double inventoryMs = 0, double captureMs = 0, int captures = 0, int captureHits = 0)
        { AssemblySnapshotId = assembly; Configuration = config; Scope = scope; Lease = lease; Library = library; Sources = PlacementLists.Copy(sources);
            Occurrences = PlacementLists.Copy(occurrences); InventoryMilliseconds = inventoryMs; CaptureMilliseconds = captureMs; Captures = captures; CaptureHits = captureHits; }
        internal string InvalidReason => !Lease.Current ? "STALE_ASSEMBLY_OR_SCOPE" : Library?.Current != true || Library.PlacementSources?.Current == false ?
            "STALE_TOOL_CATALOG_OR_REVIEW" : Sources.Any(s => !s.Current) ? "STALE_GEOMETRY_SETUP_OR_PROCESS" : null;
    }
    internal sealed class AssemblyPartResult
    {
        private readonly Func<string> invalid;
        public string SourceKey { get; }
        public string OccurrenceSetKey => ToolEvidenceCanonical.Hash(SourceKey,
            ToolEvidenceCanonical.Hash(Occurrences.Select(o => o.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray()));
        public IReadOnlyList<AssemblyOccurrence> Occurrences { get; }
        public int Quantity => Occurrences.Count;
        public string Reason { get; }
        public IReadOnlyList<VolumeCandidateCollisionResult> StaticCandidates { get; }
        public IReadOnlyList<ProcessCandidateResult> ProcessCandidates { get; }
        public int BendCount { get; }
        public bool SimulationOnly { get; }
        public bool Current => invalid() == null;
        public PartPlanningResult Planning { get; }
        public IReadOnlyList<AssemblyBendMetadata> BendMetadata { get; }
        public string ManufacturingStatus => "CHUA_XAC_MINH";
        public string WinnerToolId => null;
        public CollisionStatus GeometricStatus => !Current ? CollisionStatus.UNKNOWN :
            StaticCandidates.Any(r => r.CombinedResult == CollisionStatus.COLLISION) || ProcessCandidates.Any(r => r.ProcessResult == CollisionStatus.COLLISION) ? CollisionStatus.COLLISION :
            ProcessCandidates.Count > 0 && ProcessCandidates.All(r => r.ProcessResult == CollisionStatus.CLEAR) ? CollisionStatus.CLEAR : CollisionStatus.UNKNOWN;
        internal AssemblyPartResult(string source, IEnumerable<AssemblyOccurrence> occurrences, string reason, IEnumerable<VolumeCandidateCollisionResult> statics,
            IEnumerable<ProcessCandidateResult> processes, int bends, bool simulation, Func<string> invalid)
            : this(source, occurrences, reason, statics, processes, bends, simulation, invalid, null) { }
        internal AssemblyPartResult(string source, IEnumerable<AssemblyOccurrence> occurrences, string reason, IEnumerable<VolumeCandidateCollisionResult> statics,
            IEnumerable<ProcessCandidateResult> processes, int bends, bool simulation, Func<string> invalid, PartPlanningResult planning)
            : this(source, occurrences, reason, statics, processes, bends, simulation, invalid, planning, null) { }
        internal AssemblyPartResult(string source, IEnumerable<AssemblyOccurrence> occurrences, string reason, IEnumerable<VolumeCandidateCollisionResult> statics,
            IEnumerable<ProcessCandidateResult> processes, int bends, bool simulation, Func<string> invalid, PartPlanningResult planning = null, IEnumerable<AssemblyBendMetadata> metadata = null)
        { SourceKey = source; Occurrences = PlacementLists.Copy(occurrences); Reason = reason; StaticCandidates = PlacementLists.Copy(statics);
            ProcessCandidates = PlacementLists.Copy(processes); BendCount = bends; SimulationOnly = simulation; this.invalid = invalid; Planning = planning; BendMetadata = PlacementLists.Copy(metadata); }
    }
}
