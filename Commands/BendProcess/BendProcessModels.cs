using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendProcess
{
    internal enum BendProcessPhase { ApproachLoad, PreBend, DuringBend, FormingEnd, RetractUnload }

    internal sealed class BendMotionSegment
    {
        public string Id { get; }
        public BendProcessPhase Phase { get; }
        public IReadOnlyList<ProcessSheetPart> Parts { get; }
        public ProcessToolPose Punch { get; }
        public ProcessToolPose Holder { get; }
        public IReadOnlyList<PlanarFormingContact> Contacts { get; }
        public string ConstraintsSourceSha256 { get; }
        public string ParameterContract => "NormalizedAffineProgression0To1;AnglesRadians;LengthsMm";
        internal BendMotionSegment(string id, BendProcessPhase phase, IEnumerable<ProcessSheetPart> parts,
            ProcessToolPose punch, ProcessToolPose holder, string constraintsSourceSha256, IEnumerable<PlanarFormingContact> contacts = null)
        { Id = id; Phase = phase; Parts = PlacementLists.Copy(parts); Punch = punch; Holder = holder;
            ConstraintsSourceSha256 = constraintsSourceSha256; Contacts = PlacementLists.Copy(contacts); }
        internal ProcessToolPose Tool(CollisionPairKind pair) => pair == CollisionPairKind.WorkpiecePunch ? Punch : Holder;
        internal string Canonical => ToolEvidenceCanonical.Hash("BendMotionSegmentV1", Id, Phase.ToString(), ConstraintsSourceSha256,
            ToolEvidenceCanonical.Hash(Parts.Select(p => p.Canonical).ToArray()), Punch?.Canonical, Holder?.Canonical,
            ToolEvidenceCanonical.Hash(Contacts.Select(c => c.Canonical).ToArray()));
    }

    internal sealed class BendStateSnapshot
    {
        private readonly BendProcessPlan plan;
        public string SegmentId { get; }
        public BendProcessPhase Phase { get; }
        public double Parameter { get; }
        public int FeatureId => plan.Binding.FeatureId;
        public string ModelSnapshotId => plan.Binding.ModelSnapshotId;
        public string Configuration => plan.Binding.Configuration;
        public int UpdateStamp => plan.Binding.UpdateStamp;
        public string ProcessSourceSha256 => plan.SourceSha256;
        public string ProcessPayloadSha256 => plan.PayloadSha256;
        public string FrameId => plan.Binding.FrameId;
        public IReadOnlyList<SheetEnvelopeCapture> Sheet { get; }
        public AxialToolOccupancy Punch { get; }
        public AxialToolOccupancy Holder { get; }
        public bool SimulationOnly => plan.SimulationOnly;
        public bool Current => plan.InvalidatedReason == null;
        public IReadOnlyList<ProcessGeometryFidelity> Fidelity { get; }
        public IReadOnlyList<ProcessGeometryRole> SourceRoles { get; }
        public IReadOnlyList<ProcessMotionLaw> PanelLaws { get; }
        public IReadOnlyList<PlanarFormingContact> IntendedContacts { get; }
        public string ConstraintsSourceSha256 { get; }
        public string PunchSetupSha256 { get; }
        public string HolderSetupSha256 { get; }
        internal BendStateSnapshot(BendProcessPlan plan, BendMotionSegment segment, double u)
        {
            if (!ToolPoint.Finite(u) || u < 0 || u > 1) throw new ArgumentOutOfRangeException(nameof(u));
            this.plan = plan; SegmentId = segment.Id; Phase = segment.Phase; Parameter = u;
            Sheet = PlacementLists.Copy(segment.Parts.Select(p => p.At(u))); Punch = segment.Punch?.At(u); Holder = segment.Holder?.At(u);
            Fidelity = PlacementLists.Copy(segment.Parts.Select(p => p.Fidelity));
            SourceRoles = PlacementLists.Copy(segment.Parts.Select(p => p.Role)); PanelLaws = PlacementLists.Copy(segment.Parts.Select(p => p.Motion));
            IntendedContacts = segment.Contacts; ConstraintsSourceSha256 = segment.ConstraintsSourceSha256;
            PunchSetupSha256 = segment.Punch?.SetupSourceSha256; HolderSetupSha256 = segment.Holder?.SetupSourceSha256;
        }
    }

    internal sealed class BendProcessPlan
    {
        private readonly PlacementLease lease = new PlacementLease();
        private readonly ToolProfileLibrarySnapshot library;
        private readonly ToolReviewValidation review;
        public EnvelopeBinding Binding { get; }
        public string ToolId { get; }
        public string SourceSha256 { get; }
        public string PayloadSha256 { get; }
        public IReadOnlyList<BendMotionSegment> Segments { get; }
        public bool SimulationOnly { get; }
        public IReadOnlyList<string> InputReasons { get; }
        internal string InvalidatedReason => !lease.Current ? "STALE_PROCESS_SOURCE" : !Binding.Current ? "STALE_MODEL_OR_CONFIGURATION" :
            library != null && (!library.Current || library.PlacementSources?.Current == false) ? "STALE_TOOL_LIBRARY" :
            review?.Status == ToolReviewStatus.Stale ? "STALE_PROCESS_REVIEW" :
            Segments.SelectMany(s => new[] { s.Punch, s.Holder }).Any(t => t?.Current == false) ? "STALE_TOOL_SETUP_OR_HOLDER_EVIDENCE" :
            Segments.SelectMany(s => s.Parts).Any(p => !p.Current) ? "STALE_PROCESS_GEOMETRY" : null;
        public void Invalidate() => lease.Invalidate();
        internal BendStateSnapshot State(BendMotionSegment segment, double u)
        { if (!Segments.Contains(segment)) throw new ArgumentException("State must belong to the bound process"); return new BendStateSnapshot(this, segment, u); }
        private BendProcessPlan(EnvelopeBinding binding, string toolId, string sourceSha256, IEnumerable<BendMotionSegment> segments,
            bool controlled, ToolProfileLibrarySnapshot library, ToolReviewVerifier verifier, ToolReviewProof proof, string evidenceRoot, CancellationToken token)
        {
            Binding = binding ?? throw new ArgumentNullException(nameof(binding)); ToolId = toolId; SourceSha256 = sourceSha256;
            Segments = PlacementLists.Copy(segments); this.library = library;
            var reasons = new List<string>();
            if (!CompanyToolIds.All.Contains(toolId) || !ToolDatumEvidence.IsSha256(sourceSha256)) reasons.Add("MISSING_PROCESS_SOURCE_OR_TOOL_BINDING");
            if (Segments.Count == 0 || Segments.Count > 64 || Segments.Any(s => s == null)) throw new ArgumentException("Process needs 1..64 explicit segments");
            if (Segments.Select(s => s.Id).Distinct().Count() != Segments.Count || Segments.Any(s => string.IsNullOrWhiteSpace(s.Id) ||
                !Enum.IsDefined(typeof(BendProcessPhase), s.Phase) || !ToolDatumEvidence.IsSha256(s.ConstraintsSourceSha256))) reasons.Add("UNKNOWN_MACHINE_MOTION");
            if (Segments.Any(s => s.Parts.Count == 0 || s.Parts.Count > 32 || s.Parts.Select(p => p.Id).Distinct().Count() != s.Parts.Count)) reasons.Add("UNKNOWN_PROCESS_GEOMETRY");
            foreach (var part in Segments.SelectMany(s => s.Parts))
            {
                if (string.IsNullOrWhiteSpace(part.Id) || part.Role == ProcessGeometryRole.FinalModelObserved ||
                    !Enum.IsDefined(typeof(ProcessGeometryRole), part.Role) || !Enum.IsDefined(typeof(ProcessGeometryFidelity), part.Fidelity)) reasons.Add("UNKNOWN_PROCESS_GEOMETRY");
                if (part.Fidelity == ProcessGeometryFidelity.MissingBendZoneBound) reasons.Add("UNKNOWN_BEND_ZONE_ENCLOSURE");
                if (!ReferenceEquals(part.Source.Binding.Model, binding.Model) || part.Source.Binding.FeatureId != binding.FeatureId ||
                    part.Source.Binding.FrameId != binding.FrameId) reasons.Add("PROCESS_FRAME_OR_MODEL_BINDING_MISMATCH");
                if (!part.Motion.Valid) reasons.Add("UNKNOWN_MACHINE_MOTION");
                if (!part.Source.Certificate.ContinuousCoverageProven) reasons.Add("UNKNOWN_PROCESS_GEOMETRY");
            }
            // Geometry and tool poses must join exactly in supplied order;
            // an unexamined teleport/change of panel geometry is not a path.
            for (int i = 1; i < Segments.Count; i++)
            {
                var a = Segments[i - 1]; var b = Segments[i];
                if (b.Phase < a.Phase) reasons.Add("UNKNOWN_MACHINE_MOTION_PHASE_ORDER");
                if (!a.Parts.Select(p => p.Id).OrderBy(x => x).SequenceEqual(b.Parts.Select(p => p.Id).OrderBy(x => x)))
                    reasons.Add("UNBOUND_PROCESS_STATE_TRANSITION");
                else foreach (var p in a.Parts)
                {
                    var q = b.Parts.Single(x => x.Id == p.Id);
                    if (p.Source.GeometrySha256 != q.Source.GeometrySha256 || !Joins(p.Motion, q.Motion)) reasons.Add("UNBOUND_PROCESS_STATE_TRANSITION");
                }
            }
            PayloadSha256 = ToolEvidenceCanonical.Hash("BendProcessPlanV1", binding.Canonical, toolId, sourceSha256,
                ToolEvidenceCanonical.Hash(Segments.Select(s => s.Canonical).ToArray()));
            if (!controlled)
            {
                review = verifier?.Verify("BendProcessPlan", PayloadSha256, proof, evidenceRoot, token);
                if (review?.RecordVerified != true) reasons.Add("UNVERIFIED_PROCESS_SOURCE_AND_CONSTRAINTS");
                if (library?.PlacementSources?.Tools.SingleOrDefault(t => t.Id == toolId)?.PhysicalPairVerified != true)
                    reasons.Add("UNVERIFIED_PHYSICAL_PUNCH_HOLDER_PAIR");
                if (Segments.SelectMany(s => new[] { s.Punch, s.Holder }).Any(t => t?.Source != null && !t.Source.SimulationOnly &&
                    (t.SetupEvidence == null || t.SetupEvidence.ToolId != toolId || t.SetupEvidence.FeatureId != binding.FeatureId)))
                    reasons.Add("UNVERIFIED_PROCESS_AXIAL_SETUP_BINDING");
            }
            SimulationOnly = controlled || binding.Model.SimulationOnly || review?.SimulationOnly == true || Segments.Any(s =>
                s.Parts.Any(p => p.Source.SimulationOnly) || s.Punch?.Source?.SimulationOnly == true || s.Holder?.Source?.SimulationOnly == true);
            InputReasons = PlacementLists.Reasons(reasons);
        }
        internal static BendProcessPlan Controlled(EnvelopeBinding binding, string tool, string processSourceSha256, IEnumerable<BendMotionSegment> segments) =>
            new BendProcessPlan(binding, tool, processSourceSha256, segments, true, null, null, null, null, CancellationToken.None);
        // Supplying coordinates or a final CAD body cannot verify this path.
        // External review covers ALL source hashes, laws, errors and contacts.
        internal static BendProcessPlan Reviewed(EnvelopeBinding binding, string tool, string source, IEnumerable<BendMotionSegment> segments,
            ToolProfileLibrarySnapshot library, ToolReviewVerifier verifier, ToolReviewProof proof, string root, CancellationToken token) =>
            new BendProcessPlan(binding, tool, source, segments, false, library, verifier, proof, root, token);
        internal static bool Joins(ProcessMotionLaw a, ProcessMotionLaw b)
        {
            var x = a.At(1); var y = b.At(0);
            // Matrices agree exactly. A <=1e-9 mm arithmetic join residual is
            // inside the >=1e-8 mm pose guard on BOTH neighboring intervals.
            // Larger/unaccounted registration jumps remain unsupported.
            return x.A == y.A && x.B == y.B && x.C == y.C && x.D == y.D &&
                Math.Abs(x.TxMm - y.TxMm) <= 1e-9 && Math.Abs(x.TyMm - y.TyMm) <= 1e-9 && Math.Abs(a.Axial(1) - b.Axial(0)) <= 1e-9;
        }
    }
}
