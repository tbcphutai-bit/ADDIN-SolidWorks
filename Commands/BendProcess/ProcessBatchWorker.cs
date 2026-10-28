using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendProcess
{
    internal sealed class ProcessBatchRequest
    {
        public PlacementModelCapture Model { get; }
        public ToolProfileLibrarySnapshot Library { get; }
        // Observed finished geometry is retained as provenance/regression
        // data; it is NEVER converted into Before/During/After states.
        public IReadOnlyList<SheetEnvelopeCapture> ObservedFinalGeometry { get; }
        public IReadOnlyList<BendProcessPlan> Plans { get; }
        internal Func<string,int,string> CadAdmission {get;set;}
        private readonly Dictionary<string, IReadOnlyList<BendProcessPlan>> planIndex;
        private static readonly IReadOnlyList<BendProcessPlan> NoPlans=PlacementLists.Copy<BendProcessPlan>(null);
        internal ProcessBatchRequest(PlacementModelCapture model, ToolProfileLibrarySnapshot library,
            IEnumerable<BendProcessPlan> plans = null, IEnumerable<SheetEnvelopeCapture> observedFinalGeometry = null)
        { Model = model ?? throw new ArgumentNullException(nameof(model)); Library = library ?? throw new ArgumentNullException(nameof(library));
            Plans = PlacementLists.Copy(plans); ObservedFinalGeometry = PlacementLists.Copy(observedFinalGeometry);
            planIndex = Plans.GroupBy(p => PlanKey(p.ToolId, p.Binding.FeatureId)).ToDictionary(g => g.Key, g => PlacementLists.Copy(g), StringComparer.Ordinal); }
        private static string PlanKey(string tool, int feature) => tool + "\n" + feature.ToString(System.Globalization.CultureInfo.InvariantCulture);
        internal IReadOnlyList<BendProcessPlan> FindPlans(string tool, int feature)
        { IReadOnlyList<BendProcessPlan> values; return planIndex.TryGetValue(PlanKey(tool, feature), out values) ? values : NoPlans; }
        internal string SharedInvalidReason => !Model.Lease.Current ? "STALE_MODEL_OR_CONFIGURATION" : !Library.Current || Library.PlacementSources?.Current == false ?
            "STALE_TOOL_LIBRARY" : ObservedFinalGeometry.Any(e => !ReferenceEquals(e.Binding.Model, Model) || !e.Current) ?
            "STALE_OR_MISMATCHED_OBSERVED_GEOMETRY" : null;
        internal string InvalidReason { get { using(ProcessPerformanceScope.Measure("Freshness.FullRequest"))
            return SharedInvalidReason ?? (Plans.Any(p => p.InvalidatedReason != null) ? "STALE_PROCESS_OR_SETUP" : null); } }
    }
    internal sealed class ProcessCandidateResult
    {
        public string ToolId { get; }
        public int FeatureId { get; }
        public IReadOnlyList<ProcessPairResult> PairSegments { get; }
        public bool SimulationOnly { get; }
        public string ManufacturingStatus => "CHUA_XAC_MINH";
        public string WinnerToolId => null;
        public IReadOnlyList<BendProcessPhase> UnknownPhases => PlacementLists.Copy(Enum.GetValues(typeof(BendProcessPhase)).Cast<BendProcessPhase>()
            .Where(p => !PairSegments.Any(r => r.Phase == p) || PairSegments.Any(r => r.Phase == p && r.Status == CollisionStatus.UNKNOWN)));
        public CollisionStatus PunchResult => Combine(PairSegments.Where(r => r.Pair == CollisionPairKind.WorkpiecePunch));
        public CollisionStatus HolderResult => Combine(PairSegments.Where(r => r.Pair == CollisionPairKind.WorkpieceHolder));
        public CollisionStatus ProcessResult => BendStateCollisionResult.Combine(PunchResult, HolderResult);
        public IReadOnlyList<BendStateCoverage> UnknownStates => PlacementLists.Copy(new[] {
            UnknownPhases.Any(p => p == BendProcessPhase.ApproachLoad || p == BendProcessPhase.PreBend) ? (BendStateCoverage?)BendStateCoverage.UnknownBefore : null,
            UnknownPhases.Contains(BendProcessPhase.DuringBend) ? (BendStateCoverage?)BendStateCoverage.UnknownDuring : null,
            UnknownPhases.Any(p => p == BendProcessPhase.FormingEnd || p == BendProcessPhase.RetractUnload) ? (BendStateCoverage?)BendStateCoverage.UnknownAfter : null
        }.Where(x => x.HasValue).Select(x => x.Value));
        internal ProcessCandidateResult(string tool, int feature, IEnumerable<ProcessPairResult> results, bool simulation)
        { ToolId = tool; FeatureId = feature; PairSegments = PlacementLists.Copy(results); SimulationOnly = simulation; }
        private static CollisionStatus Combine(IEnumerable<ProcessPairResult> source)
        {
            var r = source.ToArray();
            if (r.Any(x => x.Status == CollisionStatus.COLLISION)) return CollisionStatus.COLLISION;
            return Enum.GetValues(typeof(BendProcessPhase)).Cast<BendProcessPhase>().All(p => r.Any(x => x.Phase == p) &&
                r.Where(x => x.Phase == p).All(x => x.Status == CollisionStatus.CLEAR)) ? CollisionStatus.CLEAR : CollisionStatus.UNKNOWN;
        }
    }
    internal sealed class ProcessBatchOptions
    {
        public int Workers { get; }
        public int Milliseconds { get; }
        public int KernelQueries { get; }
        public ProcessCollisionPolicy PairPolicy { get; }
        internal Func<string> ExternalInvalid { get; private set; }
        internal Action ExternalConsume { get; private set; }
        internal Action<ProcessPerformanceSample> Performance { get; private set; }
        internal ProcessBatchOptions(int workers = 2, int milliseconds = 30000, int kernelQueries = 100000, ProcessCollisionPolicy pairPolicy = null)
        { Workers = workers; Milliseconds = milliseconds; KernelQueries = kernelQueries; PairPolicy = pairPolicy ?? new ProcessCollisionPolicy(); }
        internal ProcessBatchOptions WithAssemblyGuard(Func<string> invalid, Action consume) => new ProcessBatchOptions(Workers,
            Milliseconds, KernelQueries, PairPolicy) { ExternalInvalid = invalid, ExternalConsume = consume, Performance = Performance };
        internal ProcessBatchOptions WithPerformance(Action<ProcessPerformanceSample> sink) => new ProcessBatchOptions(Workers,
            Milliseconds, KernelQueries, PairPolicy) { ExternalInvalid=ExternalInvalid, ExternalConsume=ExternalConsume, Performance=sink };
        internal bool Valid => Workers >= 1 && Workers <= 4 && Milliseconds > 0 && Milliseconds <= 120000 && KernelQueries > 0 && KernelQueries <= 1000000 && PairPolicy.Valid;
    }
    internal sealed class ProcessBatchSnapshot
    {
        private readonly ProcessBatchRequest request;
        internal CollisionBatchRunState RunState { get; }
        public bool Current => request.InvalidReason == null && RunState.Reason == null;
        public IReadOnlyList<ProcessCandidateResult> Candidates { get; }
        public double WorkerMilliseconds { get; }
        public int KernelQueries { get; }
        public int IntervalRefinements => Candidates.Sum(c => c.PairSegments.Sum(r => r.Refinements));
        public int WorkerThreadId { get; }
        public int MaximumConcurrency { get; }
        public string ProgressDiagnostic { get; }
        public int FullAudits { get; }
        public int SharedGuardChecks { get; }
        public double FreshnessAuditMilliseconds { get; }
        internal ProcessBatchSnapshot(ProcessBatchRequest request, CollisionBatchRunState state, IEnumerable<ProcessCandidateResult> rows,
            double ms, int calls, int thread, int concurrency, string diagnostic,int fullAudits=0,int sharedChecks=0,double auditMs=0)
        { this.request = request; RunState = state; Candidates = PlacementLists.Copy(rows); WorkerMilliseconds = ms;
            KernelQueries = calls; WorkerThreadId = thread; MaximumConcurrency = concurrency; ProgressDiagnostic = diagnostic;
            FullAudits=fullAudits;SharedGuardChecks=sharedChecks;FreshnessAuditMilliseconds=auditMs; }
    }
    internal sealed class ProcessBatchWorker : IDisposable
    {
        private readonly object gate = new object();
        private CancellationTokenSource active;
        private CollisionBatchRunState state;
        private ProcessBatchSnapshot published;
        private int generation;
        private bool disposed;
        public ProcessBatchSnapshot Published => Volatile.Read(ref published);
        internal Task<ProcessBatchSnapshot> RunAsync(ProcessBatchRequest request, ProcessBatchOptions options = null,
            CancellationToken token = default(CancellationToken), IProgress<CollisionBatchProgress> progress = null)
        {
            CancellationTokenSource source; CollisionBatchRunState runState; int version;
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(ProcessBatchWorker));
                state?.Stop("SUPERSEDED_PROCESS_BATCH"); active?.Cancel(); published?.RunState.Stop("SUPERSEDED_PROCESS_BATCH");
                source = CancellationTokenSource.CreateLinkedTokenSource(token); active = source; runState = state = new CollisionBatchRunState(); version = ++generation;
            }
            // Owned/awaitable task, only immutable managed DTOs cross boundary.
            return Task.Run(() => {
                try
                {
                    var answer = Execute(request, options ?? new ProcessBatchOptions(), source.Token, progress, runState);
                    lock (gate) if (!disposed && generation == version) Interlocked.Exchange(ref published, answer);
                    return answer;
                }
                finally { lock (gate) if (ReferenceEquals(active, source)) active = null; source.Dispose(); }
            });
        }
        internal static ProcessBatchSnapshot RunSynchronously(ProcessBatchRequest request, ProcessBatchOptions options = null,
            CancellationToken token = default(CancellationToken), IProgress<CollisionBatchProgress> progress = null) =>
            Execute(request, options ?? new ProcessBatchOptions(), token, progress, new CollisionBatchRunState());
        private static ProcessBatchSnapshot Execute(ProcessBatchRequest request, ProcessBatchOptions options, CancellationToken token,
            IProgress<CollisionBatchProgress> progress, CollisionBatchRunState runState)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var clock = Stopwatch.StartNew(); int calls = 0, next = -1, done = 0, executing = 1, running = 0, maximum = 0;
            int fullAudits=0,sharedChecks=0;long auditTicks=0;
            int thread = Thread.CurrentThread.ManagedThreadId; string diagnostic = null; var progressGate = new object();
            Func<string> invalid = () => {
                Interlocked.Increment(ref sharedChecks);
                string external = options.ExternalInvalid?.Invoke(); if (external != null) runState.Stop(external);
                // Per-query guards validate shared leases here and the active
                // plan in ProcessCollisionEngine. Other plans cannot publish
                // partial proof: full audits run at candidate boundaries and
                // publication, and held snapshots still use InvalidReason.
                string shared=request.SharedInvalidReason;if(shared!=null)runState.Stop(shared);
                if (token.IsCancellationRequested) runState.Stop("CANCELLED");
                if (Volatile.Read(ref executing) != 0 && clock.ElapsedMilliseconds >= options.Milliseconds) runState.Stop("PROCESS_JOB_TIME_BUDGET_EXCEEDED");
                return runState.Reason;
            };
            Func<string> fullInvalid = () => {
                long start=Stopwatch.GetTimestamp();Interlocked.Increment(ref fullAudits);
                string reason=request.InvalidReason;if(reason!=null)runState.Stop(reason);
                Interlocked.Add(ref auditTicks,Stopwatch.GetTimestamp()-start);
                return invalid();
            };
            Action consume = () => {
                options.ExternalConsume?.Invoke();
                if (Interlocked.Increment(ref calls) > options.KernelQueries) runState.Stop("PROCESS_JOB_QUERY_BUDGET_EXCEEDED");
                var reason = invalid(); if (reason != null) throw new CollisionAbort(reason);
            };
            if (!options.Valid) runState.Stop("INVALID_PROCESS_BATCH_POLICY");
            var jobs = CompanyToolIds.All.SelectMany(id => request.Model.Bends.Select(b => new { Tool = id, Bend = b })).ToArray();
            var output = new ProcessCandidateResult[jobs.Length];
            Action work = () => {
                while (true)
                {
                    int index = Interlocked.Increment(ref next); if (index >= jobs.Length) return;
                    var job = jobs[index]; int current = Interlocked.Increment(ref running), old;
                    do { old = Volatile.Read(ref maximum); if (old >= current) break; } while (Interlocked.CompareExchange(ref maximum, current, old) != old);
                    var results = new List<ProcessPairResult>(); bool simulation = request.Model.SimulationOnly;
                    Func<CollisionPairKind, BendProcessPhase, IEnumerable<string>, ProcessPairResult> unknown = (pair, phase, reasons) =>
                        new ProcessPairResult(pair, "MISSING:" + phase, phase, CollisionStatus.UNKNOWN, reasons, invalid, null, 0, 0, 0, 0, 0);
                    try
                    {
                        var plans = request.FindPlans(job.Tool, job.Bend.FeatureId);
                        var plan = plans.Count == 1 ? plans[0] : null;
                        string missing = request.CadAdmission?.Invoke(job.Tool,job.Bend.FeatureId) ?? (plans.Count > 1 ? "DUPLICATE_PROCESS_PLAN" : plan == null ? "UNKNOWN_PROCESS_GEOMETRY" :
                            !ReferenceEquals(plan.Binding.Model, request.Model) ? "MISMATCHED_PROCESS_MODEL" : fullInvalid());
                        if (missing == null)
                        {
                            simulation |= plan.SimulationOnly;
                            foreach (var segment in plan.Segments) foreach (CollisionPairKind pair in Enum.GetValues(typeof(CollisionPairKind)))
                                results.Add(ProcessCollisionEngine.Evaluate(plan, segment, pair, options.PairPolicy, token, invalid, consume, options.Performance));
                        }
                        foreach (BendProcessPhase phase in Enum.GetValues(typeof(BendProcessPhase)))
                            foreach (CollisionPairKind pair in Enum.GetValues(typeof(CollisionPairKind)))
                                if (!results.Any(r => r.Phase == phase && r.Pair == pair)) results.Add(unknown(pair, phase,
                                    new[] { missing ?? "MISSING_REQUIRED_PROCESS_PHASE", "UNKNOWN_MACHINE_MOTION", "NO_STATE_INFERENCE_FROM_FINAL_MODEL" }));
                    }
                    catch (Exception ex)
                    {
                        runState.Stop("PROCESS_BATCH_FAILURE:" + ex.GetType().Name); results.Clear();
                        foreach (BendProcessPhase phase in Enum.GetValues(typeof(BendProcessPhase))) foreach (CollisionPairKind pair in Enum.GetValues(typeof(CollisionPairKind)))
                            results.Add(unknown(pair, phase, new[] { runState.Reason }));
                    }
                    finally { Interlocked.Decrement(ref running); }
                    fullInvalid();
                    output[index] = new ProcessCandidateResult(job.Tool, job.Bend.FeatureId, results, simulation);
                    lock (progressGate)
                    {
                        int completed = ++done;
                        try { progress?.Report(new CollisionBatchProgress(completed, jobs.Length, job.Tool, job.Bend.FeatureId, index)); }
                        catch (Exception ex) { diagnostic = "PROGRESS_CALLBACK_FAILED:" + ex.GetType().Name; }
                    }
                }
            };
            using (token.Register(() => runState.Stop("CANCELLED")))
            {
                if (options.Workers == 1 || !options.Valid) work();
                else Task.WaitAll(Enumerable.Range(0, options.Workers).Select(i => Task.Run(work)).ToArray());
                fullInvalid();
            }
            Volatile.Write(ref executing, 0); clock.Stop();
            var ordered = request.Model.Bends.SelectMany(b => CompanyToolIds.All.Select(id => output.Single(r => r.FeatureId == b.FeatureId && r.ToolId == id)));
            return new ProcessBatchSnapshot(request, runState, ordered, clock.Elapsed.TotalMilliseconds, calls, thread, maximum, diagnostic,
                fullAudits,sharedChecks,auditTicks*1000.0/Stopwatch.Frequency);
        }
        public void Dispose()
        {
            lock (gate) { if (disposed) return; disposed = true; generation++; state?.Stop("PROCESS_BATCH_DISPOSED");
                published?.RunState.Stop("PROCESS_BATCH_DISPOSED"); active?.Cancel(); }
        }
    }
}
