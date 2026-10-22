using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlanning
{
    internal enum SequenceSearchCoverage { ExhaustiveDeclaredSpace, Bounded, Interrupted, MissingEvidence }
    internal enum SequenceToolDecision { ProvenInCheckedScope, NeedsEvidence, RejectedInDeclaredConditions }
    internal enum SequenceEvidenceLevel { Missing, SimulationOnly, ReviewedSourceGraph }
    internal sealed class SequencePlannerOptions
    {
        public int Milliseconds { get; }
        public int MaximumNodes { get; }
        public int MaximumResults { get; }
        public int MaximumFrontier { get; }
        public int MaximumKernelQueries { get; }
        public long MaximumRetainedBytes { get; }
        public ProcessCollisionPolicy Policy { get; }
        internal SequencePlannerOptions(int milliseconds = 30000, int nodes = 5000, int results = 128,
            int frontier = 1024, int queries = 100000, ProcessCollisionPolicy policy = null, long retainedBytes = 33554432)
        { Milliseconds = milliseconds; MaximumNodes = nodes; MaximumResults = results; MaximumFrontier = frontier;
            MaximumKernelQueries = queries; Policy = policy ?? new ProcessCollisionPolicy(); MaximumRetainedBytes = retainedBytes; }
        internal bool Valid => Milliseconds > 0 && Milliseconds <= 120000 && MaximumNodes > 0 && MaximumNodes <= 1000000 &&
            MaximumResults > 0 && MaximumResults <= 4096 && MaximumFrontier > 0 && MaximumFrontier <= 100000 &&
            MaximumKernelQueries > 0 && MaximumKernelQueries <= 1000000 && MaximumRetainedBytes >= 16384 && MaximumRetainedBytes <= 268435456 && Policy.Valid;
        internal string Canonical => ToolEvidenceCanonical.Hash("SequencePlannerPolicyV1", ProcessCollisionPolicy.Version,
            Milliseconds.ToString(), MaximumNodes.ToString(), MaximumResults.ToString(), MaximumFrontier.ToString(), MaximumKernelQueries.ToString(), MaximumRetainedBytes.ToString(),
            ToolEvidenceCanonical.Hash(new[] { (double)Policy.MaximumIntervals, Policy.MaximumDepth, Policy.MaximumMilliseconds,
                Policy.Axial.MaximumCells, Policy.Axial.MaximumMilliseconds, Policy.Axial.CrossSection.ToleranceMm,
                Policy.Axial.CrossSection.MaximumWork, Policy.Axial.CrossSection.MaximumMilliseconds,
                Policy.Axial.CrossSection.MaximumSubdivisionDepth }.Select(ToolEvidenceCanonical.Number).ToArray()));
    }
    internal sealed class SequenceStepResult
    {
        public SequenceTransition Transition { get; }
        public IReadOnlyList<ProcessPairResult> Pairs { get; }
        public int FeatureId => Transition.FeatureId;
        public CollisionStatus Punch => Combine(CollisionPairKind.WorkpiecePunch);
        public CollisionStatus Holder => Combine(CollisionPairKind.WorkpieceHolder);
        public CollisionStatus Status => Punch == CollisionStatus.COLLISION || Holder == CollisionStatus.COLLISION ? CollisionStatus.COLLISION :
            Punch == CollisionStatus.CLEAR && Holder == CollisionStatus.CLEAR ? CollisionStatus.CLEAR : CollisionStatus.UNKNOWN;
        public IReadOnlyList<string> Reasons => PlacementLists.Reasons(Pairs.SelectMany(p => p.Reasons));
        internal SequenceStepResult(SequenceTransition transition, IEnumerable<ProcessPairResult> pairs)
        { Transition = transition; Pairs = PlacementLists.Copy(pairs); }
        private CollisionStatus Combine(CollisionPairKind pair)
        {
            var poses=Transition.Plan.Segments.Select(s=>s.Tool(pair)).ToArray();
            if(poses.Length==0||poses.Any(p=>p==null)||poses.Select(p=>p.AssemblyCanonical).Distinct().Count()!=1||
                Enumerable.Range(1,poses.Length-1).Any(i=>!BendProcessPlan.Joins(poses[i-1].Motion,poses[i].Motion)))return CollisionStatus.UNKNOWN;
            var rows = Pairs.Where(p => p.Pair == pair).ToArray();
            if (rows.Any(p => p.ProcessPayloadSha256 != Transition.Plan.PayloadSha256 || p.ModelSnapshotId != Transition.Plan.Binding.ModelSnapshotId ||
                p.FrameId != Transition.Plan.Binding.FrameId || !Transition.Plan.Segments.Any(s => s.Id == p.SegmentId && s.Phase == p.Phase)))
                return CollisionStatus.UNKNOWN;
            // A branch is pruned ONLY by a current, matching interior witness.
            if (rows.Any(ValidCollision)) return CollisionStatus.COLLISION;
            return Transition.Plan.Segments.Count > 0 && Enum.GetValues(typeof(BendProcessPhase)).Cast<BendProcessPhase>()
                .All(phase => rows.Any(r => r.Phase == phase)) && rows.Length == Transition.Plan.Segments.Count &&
                rows.Select(r => r.SegmentId).Distinct().Count() == rows.Length && rows.All(r => r.Status == CollisionStatus.CLEAR) ?
                CollisionStatus.CLEAR : CollisionStatus.UNKNOWN;
        }
        internal static bool ValidCollision(ProcessPairResult p) => p.Status == CollisionStatus.COLLISION && p.Current &&
            p.Witness?.ProcessPayloadSha256 == p.ProcessPayloadSha256 && p.Witness?.Parameter >= 0 && p.Witness.Parameter <= 1 &&
            p.Witness?.Geometry?.ObservedSliceOnly == false && p.Witness.Geometry.AxialOccupancyMarginLowerMm > 0 &&
            p.Witness.Geometry.CrossSectionInteriorMarginLowerMm > p.ToleranceMm;
    }
    internal sealed class BendSequenceResult
    {
        private readonly Func<string> invalid;
        public string ToolId { get; }
        public string SetupIdentitySha256 { get; }
        public IReadOnlyList<SequenceStepResult> Steps { get; }
        public bool ReachesAllBends { get; }
        public bool SimulationOnly { get; }
        public int ToolTypeCount => Steps.Select(s=>s.Transition.ToolId).Distinct(StringComparer.Ordinal).Count();
        public int ToolChangeCount => Enumerable.Range(1,Math.Max(0,Steps.Count-1)).Count(i=>
            Steps[i-1].Transition.ToolId!=Steps[i].Transition.ToolId ||
            Steps[i-1].Transition.SetupIdentitySha256!=Steps[i].Transition.SetupIdentitySha256);
        public CollisionStatus Status => invalid() != null ? CollisionStatus.UNKNOWN : Steps.Any(s => s.Status == CollisionStatus.COLLISION) ?
            CollisionStatus.COLLISION : ReachesAllBends && Steps.Count > 0 && Steps.All(s => s.Status == CollisionStatus.CLEAR) ? CollisionStatus.CLEAR : CollisionStatus.UNKNOWN;
        public string Reason => invalid() ?? (Status == CollisionStatus.COLLISION ? "WITNESSED_SEQUENCE_BRANCH_COLLISION" :
            Status == CollisionStatus.CLEAR ? "WHOLE_SUPPLIED_SEQUENCE_CLEAR_IN_CHECKED_SCOPE" : "INCOMPLETE_OR_UNKNOWN_SEQUENCE");
        internal BendSequenceResult(string tool, string setup, IEnumerable<SequenceStepResult> steps, bool all, bool simulation, Func<string> invalid)
        { ToolId = tool; SetupIdentitySha256 = setup; Steps = PlacementLists.Copy(steps); ReachesAllBends = all; SimulationOnly = simulation; this.invalid = invalid; }
    }
    internal sealed class SequenceToolResult
    {
        private readonly Func<string> invalid;
        private readonly SequenceSearchCoverage coverage;
        private readonly IReadOnlyList<string> reasons;
        public string ToolId { get; }
        public IReadOnlyList<BendSequenceResult> Sequences { get; }
        public IReadOnlyList<string> Reasons => PlacementLists.Reasons(reasons.Concat(invalid() == null ? new string[0] : new[] { invalid() }));
        public SequenceSearchCoverage Coverage => invalid() == null ? coverage : SequenceSearchCoverage.Interrupted;
        public SequenceToolDecision Decision => invalid() != null ? SequenceToolDecision.NeedsEvidence : Sequences.Any(s => s.Status == CollisionStatus.CLEAR) ?
            SequenceToolDecision.ProvenInCheckedScope : coverage == SequenceSearchCoverage.ExhaustiveDeclaredSpace && Sequences.Count > 0 &&
            Sequences.All(s => s.Status == CollisionStatus.COLLISION) ? SequenceToolDecision.RejectedInDeclaredConditions : SequenceToolDecision.NeedsEvidence;
        internal SequenceToolResult(string tool, IEnumerable<BendSequenceResult> sequences, SequenceSearchCoverage coverage, IEnumerable<string> reasons, Func<string> invalid)
        { ToolId = tool; Sequences = PlacementLists.Copy(sequences); this.coverage = coverage; this.reasons = PlacementLists.Reasons(reasons); this.invalid = invalid; }
    }
    internal sealed class PartPlanningResult
    {
        private readonly Func<string> invalid;
        private readonly AssemblyResultReadGuard displayGuard;
        public string SourceKey { get; }
        public string ModelSnapshotId { get; }
        public string Configuration { get; }
        public string Revision { get; }
        public string InputSha256 { get; }
        public SequencePlannerOptions Budget { get; }
        public string SolverPolicyVersion => ProcessCollisionPolicy.Version;
        public string CacheProvenance => "WholeSourceRevisionAndTransitionNumericFactsV1";
        public bool Current => invalid() == null;
        public string InvalidReason => invalid();
        public bool SimulationOnly { get; }
        private readonly SequenceEvidenceLevel evidence;
        public SequenceEvidenceLevel EvidenceLevel => Current ? evidence : SequenceEvidenceLevel.Missing;
        public string ManufacturingStatus => "CHUA_XAC_MINH";
        public string WinnerToolId => null;
        public IReadOnlyList<SequenceToolResult> Tools { get; }
        public IReadOnlyList<string> CommonToolIds => PlacementLists.Copy(Current ? Tools.Where(t => t.Decision == SequenceToolDecision.ProvenInCheckedScope).Select(t => t.ToolId) : new string[0]);
        // Preference within proven supplied process paths, never manufacturing
        // approval. UNKNOWN/rejected paths are not eligible for preference.
        // The present solver proves single-setup paths only: it must not invent
        // an unprovided inter-tool changeover movement to produce a fallback.
        public BendSequenceResult PreferredCheckedSequence => !Current ? null : Tools.SelectMany(t=>t.Sequences)
            .Where(s=>s.ReachesAllBends&&s.Status==CollisionStatus.CLEAR)
            .OrderBy(s=>s.ToolChangeCount).ThenBy(s=>s.ToolTypeCount)
            .ThenBy(s=>s.ToolId,StringComparer.Ordinal)
            .ThenBy(s=>string.Join(",",s.Steps.Select(step=>step.FeatureId)),StringComparer.Ordinal).FirstOrDefault();
        public int VisitedNodes { get; }
        public int KernelQueries { get; }
        public int CacheHits { get; }
        public int CacheMisses { get; }
        public long RetainedCacheBytes { get; }
        public double ElapsedMilliseconds { get; }
        public string StopReason { get; }
        internal PartPlanningResult(string source, PlacementModelCapture model, PartSequenceSpace space, IEnumerable<SequenceToolResult> tools,
            Func<string> invalid, int nodes, int queries, int hits, int misses, long bytes, double ms, string stop, AssemblyResultReadGuard displayGuard = null, SequencePlannerOptions budget = null)
        {
            SourceKey = source; ModelSnapshotId = model?.SnapshotId; Configuration = model?.Configuration;
            Revision = space?.Revision; InputSha256 = space?.PayloadSha256; SimulationOnly = space?.SimulationOnly == true || model?.SimulationOnly == true;
            evidence = space == null || space.Reasons.Count > 0 ? SequenceEvidenceLevel.Missing : space.SimulationOnly ? SequenceEvidenceLevel.SimulationOnly : SequenceEvidenceLevel.ReviewedSourceGraph;
            Tools = PlacementLists.Copy(tools); this.invalid = invalid; VisitedNodes = nodes; KernelQueries = queries; CacheHits = hits;
            CacheMisses = misses; RetainedCacheBytes = bytes; ElapsedMilliseconds = ms; StopReason = stop; this.displayGuard = displayGuard; Budget = budget;
        }
        // A synchronous display projection only. Strict held getters and other
        // threads retain full audits. The consumer must discard all rendered
        // facts when an entry/exit audit fails; never a numeric certificate.
        internal bool ReadDisplay(Action copy)
        {
            if (displayGuard == null) { if (!Current) return false; copy(); return Current; }
            if (displayGuard.ActiveOnCurrentThread) { copy(); return true; }
            double milliseconds;
            return displayGuard.Copy(() => { copy(); return new object(); }, out milliseconds) != null;
        }
    }
    internal sealed class SequencePlanningProgress
    {
        public string ToolId { get; }
        public int VisitedNodes { get; }
        public int KernelQueries { get; }
        public double ElapsedMilliseconds { get; }
        internal SequencePlanningProgress(string tool, int nodes, int queries, double ms)
        { ToolId = tool; VisitedNodes = nodes; KernelQueries = queries; ElapsedMilliseconds = ms; }
    }
}
