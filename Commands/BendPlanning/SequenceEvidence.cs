using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlanning
{
    // A finite, explicitly supplied process-state graph. Never manufactured
    // from finished CAD, bend names, a bounding box, or sampled sections.
    internal sealed class SequenceSheetState
    {
        public string Id { get; }
        public string GeometryPoseSha256 { get; }
        public IReadOnlyList<int> CompletedFeatureIds { get; }
        internal SequenceSheetState(string id, string geometryPose, IEnumerable<int> completed)
        {
            Id = id; GeometryPoseSha256 = geometryPose;
            CompletedFeatureIds = PlacementLists.Copy((completed ?? new int[0]).OrderBy(x => x));
            if (string.IsNullOrWhiteSpace(id) || !ToolDatumEvidence.IsSha256(geometryPose) ||
                CompletedFeatureIds.Distinct().Count() != CompletedFeatureIds.Count) throw new ArgumentException("Invalid sequence state");
        }
        internal string Canonical => ToolEvidenceCanonical.Hash(Id, GeometryPoseSha256,
            string.Join(",", CompletedFeatureIds));
    }

    internal sealed class SequenceTransition
    {
        public string Id { get; }
        public string BeforeStateId { get; }
        public string AfterStateId { get; }
        public string SetupIdentitySha256 { get; }
        public BendProcessPlan Plan { get; }
        public string ToolId => Plan.ToolId;
        public int FeatureId => Plan.Binding.FeatureId;
        internal SequenceTransition(string id, string before, string after, string setup, BendProcessPlan plan)
        {
            Id = id; BeforeStateId = before; AfterStateId = after; SetupIdentitySha256 = setup;
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (string.IsNullOrWhiteSpace(id) || !ToolDatumEvidence.IsSha256(setup)) throw new ArgumentException("Invalid sequence transition");
        }
        internal string Canonical => ToolEvidenceCanonical.Hash(Id, BeforeStateId, AfterStateId, SetupIdentitySha256, Plan.PayloadSha256);
    }

    internal sealed class PartSequenceSpace
    {
        private readonly ToolReviewValidation review;
        private readonly PlacementLease lease = new PlacementLease();
        public PlacementModelCapture Model { get; }
        public string SourceSha256 { get; }
        public string Revision { get; }
        public string InitialStateId { get; }
        public IReadOnlyList<SequenceSheetState> States { get; }
        public IReadOnlyList<SequenceTransition> Transitions { get; }
        public string PayloadSha256 { get; }
        public string NativeGeometrySha256 { get; }
        public bool DeclaredAlternativesComplete { get; }
        public bool SimulationOnly { get; }
        public IReadOnlyList<string> Reasons { get; }
        internal void Invalidate() => lease.Invalidate();
        internal string SharedInvalidReason => !lease.Current ? "STALE_SEQUENCE_SOURCE" : !Model.Lease.Current ? "STALE_SEQUENCE_MODEL" : review?.Status == ToolReviewStatus.Stale ? "STALE_SEQUENCE_REVIEW" : null;
        internal string InvalidReason => SharedInvalidReason ?? (Transitions.Any(t => t.Plan.InvalidatedReason != null) ? "STALE_SEQUENCE_PROCESS_OR_SETUP" : null);

        private PartSequenceSpace(PlacementModelCapture model, string source, string revision, string initial,
            IEnumerable<SequenceSheetState> states, IEnumerable<SequenceTransition> transitions, bool complete,
            bool controlled, ToolReviewVerifier verifier, ToolReviewProof proof, string root, CancellationToken token)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model)); SourceSha256 = source; Revision = revision; InitialStateId = initial;
            States = PlacementLists.Copy(states); Transitions = PlacementLists.Copy(transitions); DeclaredAlternativesComplete = complete;
            if (States.Count > 100000 || Transitions.Count > 100000 || States.Any(s => s == null) || Transitions.Any(t => t == null))
                throw new ArgumentException("Invalid or oversized sequence evidence graph");
            var byState = States.ToLookup(s => s.Id, StringComparer.Ordinal);
            Func<string, SequenceSheetState> state = id => id == null || byState[id].Count() != 1 ? null : byState[id].First();
            var reasons = new List<string>();
            if (!ToolDatumEvidence.IsSha256(source) || string.IsNullOrWhiteSpace(revision)) reasons.Add("MISSING_SEQUENCE_SOURCE_REVISION");
            if (States.Count == 0 || States.Select(s => s.Id).Distinct().Count() != States.Count ||
                Transitions.Select(t => t.Id).Distinct().Count() != Transitions.Count) reasons.Add("AMBIGUOUS_SEQUENCE_STATE_IDENTITY");
            var known = model.Bends.Select(b => b.FeatureId).ToArray();
            if (known.Distinct().Count() != known.Length) reasons.Add("AMBIGUOUS_NATIVE_FEATURE_ID");
            if (!controlled && !model.SimulationOnly && model.Bends.Any(b => string.IsNullOrWhiteSpace(b.PersistentId))) reasons.Add("MISSING_NATIVE_PERSISTENT_FEATURE_IDENTITY");
            var start = state(initial);
            if (start == null || start.CompletedFeatureIds.Count != 0) reasons.Add("MISSING_UNBENT_INITIAL_STATE");
            if (States.Any(s => s.CompletedFeatureIds.Any(f => !known.Contains(f)))) reasons.Add("UNKNOWN_NATIVE_FEATURE_ID");
            foreach (var t in Transitions)
            {
                var a = state(t.BeforeStateId); var b = state(t.AfterStateId);
                if (!ReferenceEquals(t.Plan.Binding.Model, model) || !known.Contains(t.FeatureId)) reasons.Add("SEQUENCE_MODEL_OR_FEATURE_MISMATCH");
                if (a == null || b == null || a.CompletedFeatureIds.Contains(t.FeatureId) ||
                    !a.CompletedFeatureIds.Concat(new[] { t.FeatureId }).OrderBy(x => x).SequenceEqual(b.CompletedFeatureIds))
                    reasons.Add("INVALID_SEQUENCE_COMPLETED_FEATURE_TRANSITION");
                else if (a.GeometryPoseSha256 != Boundary(t.Plan, false) || b.GeometryPoseSha256 != Boundary(t.Plan, true))
                    reasons.Add("UNBOUND_SEQUENCE_STATE_GEOMETRY_OR_FRAME");
            }
            NativeGeometrySha256 = ToolEvidenceCanonical.Hash("SequenceTargetNativeGeometryV1", model.CompleteBodyCoverage.ToString(),
                ToolEvidenceCanonical.Hash(model.Bodies.Select(b => ToolEvidenceCanonical.Hash(b.Id, b.EnvelopeTopology?.SourceSha256,
                    ToolEvidenceCanonical.Hash(b.Edges.Select(e => ToolEvidenceCanonical.Hash(e.Id.ToString(), e.Kind.ToString(), EnvelopeBinding.Vector(e.A),
                        EnvelopeBinding.Vector(e.B), e.Arc == null ? null : ToolEvidenceCanonical.Hash(EnvelopeBinding.Vector(e.Arc.CenterMm),
                            EnvelopeBinding.Vector(e.Arc.MidMm), ToolEvidenceCanonical.Number(e.Arc.RadiusMm), e.Arc.FullCircle.ToString()))).ToArray()),
                    ToolEvidenceCanonical.Hash(b.Faces.Select(f => ToolEvidenceCanonical.Hash(f.Id.ToString(), f.Kind.ToString(), EnvelopeBinding.Vector(f.Normal),
                        EnvelopeBinding.Vector(f.PointMm), EnvelopeBinding.Vector(f.Axis), string.Join(",", f.Edges))).ToArray()))).ToArray()));
            PayloadSha256 = ToolEvidenceCanonical.Hash("PartSequenceSpaceV1", model.SnapshotId, model.Configuration, NativeGeometrySha256,
                model.UpdateStamp.ToString(System.Globalization.CultureInfo.InvariantCulture), source, revision, initial, complete.ToString(),
                ToolEvidenceCanonical.Hash(model.Bends.Select(b => ToolEvidenceCanonical.Hash(b.FeatureId.ToString(), b.PersistentId)).ToArray()),
                ToolEvidenceCanonical.Hash(States.Select(s => s.Canonical).OrderBy(s => s, StringComparer.Ordinal).ToArray()),
                ToolEvidenceCanonical.Hash(Transitions.Select(s => s.Canonical).OrderBy(s => s, StringComparer.Ordinal).ToArray()));
            if (!controlled)
            {
                review = verifier?.Verify("PartSequenceSpace", PayloadSha256, proof, root, token);
                if (review?.RecordVerified != true) reasons.Add("UNVERIFIED_SEQUENCE_COMPLETENESS_REGISTRATION_AND_SETUP");
            }
            SimulationOnly = controlled || model.SimulationOnly || review?.SimulationOnly == true || Transitions.Any(t => t.Plan.SimulationOnly);
            Reasons = PlacementLists.Reasons(reasons);
        }
        internal static PartSequenceSpace Controlled(PlacementModelCapture model, string source, string revision, string initial,
            IEnumerable<SequenceSheetState> states, IEnumerable<SequenceTransition> transitions, bool complete = true) =>
            new PartSequenceSpace(model, source, revision, initial, states, transitions, complete, true, null, null, null, CancellationToken.None);
        internal static PartSequenceSpace Reviewed(PlacementModelCapture model, string source, string revision, string initial,
            IEnumerable<SequenceSheetState> states, IEnumerable<SequenceTransition> transitions, bool complete,
            ToolReviewVerifier verifier, ToolReviewProof proof, string root, CancellationToken token) =>
            new PartSequenceSpace(model, source, revision, initial, states, transitions, complete, false, verifier, proof, root, token);

        // Exact source+pose equality in a common registered frame, including
        // panel coverage and uncertainty. Different tessellations/frames are
        // deliberately UNKNOWN, even when they might describe equal shapes.
        internal static string Boundary(BendProcessPlan plan, bool end)
        {
            var segment = end ? plan.Segments.Last() : plan.Segments.First(); double u = end ? 1 : 0;
            return ToolEvidenceCanonical.Hash("WholeSheetBoundaryMmV1", plan.Binding.FrameId,
                ToolEvidenceCanonical.Hash(segment.Parts.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => {
                    var m = p.Motion.At(u);
                    return ToolEvidenceCanonical.Hash(p.Id, p.Source.ShapeSha256, p.Fidelity.ToString(), p.Role.ToString(),
                        ToolEvidenceCanonical.Hash(new[] { m.A, m.B, m.C, m.D, m.TxMm, m.TyMm, p.Motion.Axial(u),
                            p.Motion.PositionErrorMm, p.Motion.AngleErrorRadians, p.Motion.AxialErrorMm }.Select(ToolEvidenceCanonical.Number).ToArray()));
                }).ToArray()));
        }
    }
}
