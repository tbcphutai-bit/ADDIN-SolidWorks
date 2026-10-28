using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendEnvelope
{
    internal sealed class CollisionBatchRequest
    {
        public PlacementModelCapture Model { get; }
        public ToolProfileLibrarySnapshot Library { get; }
        public IReadOnlyList<SheetEnvelopeCapture> Envelopes { get; }
        public IReadOnlyList<ToolAxialSetupEvidence> Setups { get; }
        internal Func<string,int,string> CadAdmission {get;set;}
        private readonly Dictionary<string, IReadOnlyList<ToolAxialSetupEvidence>> setupIndex;
        private static readonly IReadOnlyList<ToolAxialSetupEvidence> NoSetups=PlacementLists.Copy<ToolAxialSetupEvidence>(null);
        private readonly HashSet<string> simulationTools;
        internal CollisionBatchRequest(PlacementModelCapture model, ToolProfileLibrarySnapshot library,
            IEnumerable<SheetEnvelopeCapture> envelopes = null, IEnumerable<ToolAxialSetupEvidence> setups = null)
        { Model = model ?? throw new ArgumentNullException(nameof(model)); Library = library ?? throw new ArgumentNullException(nameof(library));
            Envelopes = PlacementLists.Copy(envelopes); Setups = PlacementLists.Copy(setups);
            setupIndex = Setups.GroupBy(s => SetupKey(s.ToolId, s.FeatureId, s.Pair)).ToDictionary(g => g.Key, g => PlacementLists.Copy(g), StringComparer.Ordinal);
            simulationTools = new HashSet<string>(Setups.Where(s => s.Occupancy.SimulationOnly).Select(s => s.ToolId), StringComparer.Ordinal); }
        private static string SetupKey(string tool, int feature, CollisionPairKind pair) => tool + "\n" + feature.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + pair;
        internal IReadOnlyList<ToolAxialSetupEvidence> FindSetups(string tool, int feature, CollisionPairKind pair)
        { IReadOnlyList<ToolAxialSetupEvidence> values; return setupIndex.TryGetValue(SetupKey(tool, feature, pair), out values) ? values : NoSetups; }
        internal bool ToolSimulationOnly(string tool) => simulationTools.Contains(tool);
        internal string InvalidReason => !Model.Lease.Current ? "STALE_MODEL" : !Library.Current || Library.PlacementSources?.Current == false ? "STALE_TOOL_SOURCE" :
            Setups.Any(s => s.Occupancy.Current == false) ? "STALE_SETUP_OR_REVIEW_EVIDENCE" : null;
    }
    internal sealed class CollisionBatchOptions
    {
        public int MaximumWorkers { get; }
        public int MaximumMilliseconds { get; }
        public int MaximumKernelQueries { get; }
        public AxialCollisionPolicy PairPolicy { get; }
        internal Func<string> ExternalInvalid { get; private set; }
        internal Action ExternalConsume { get; private set; }
        internal CollisionBatchOptions(int workers = 2, int milliseconds = 15000, int kernelQueries = 20000, AxialCollisionPolicy pairPolicy = null)
        { MaximumWorkers = workers; MaximumMilliseconds = milliseconds; MaximumKernelQueries = kernelQueries; PairPolicy = pairPolicy ?? new AxialCollisionPolicy(); }
        internal CollisionBatchOptions WithAssemblyGuard(Func<string> invalid, Action consume) => new CollisionBatchOptions(MaximumWorkers,
            MaximumMilliseconds, MaximumKernelQueries, PairPolicy) { ExternalInvalid = invalid, ExternalConsume = consume };
        internal bool Valid => MaximumWorkers >= 1 && MaximumWorkers <= 4 && MaximumMilliseconds > 0 && MaximumMilliseconds <= 120000 &&
            MaximumKernelQueries > 0 && MaximumKernelQueries <= 1000000 && PairPolicy.Valid;
    }
    internal sealed class CollisionBatchProgress
    {
        public int Completed { get; }
        public int Total { get; }
        public string ToolId { get; }
        public int FeatureId { get; }
        public int WorkerThreadId { get; }
        public int DispatchIndex { get; }
        internal CollisionBatchProgress(int done, int total, string tool, int feature, int dispatchIndex)
        { Completed = done; Total = total; ToolId = tool; FeatureId = feature; DispatchIndex = dispatchIndex; WorkerThreadId = Thread.CurrentThread.ManagedThreadId; }
    }
    internal sealed class VolumeCandidateCollisionResult
    {
        public string ToolId { get; }
        public int FeatureId { get; }
        public AxialPairResult PunchResult { get; }
        public AxialPairResult HolderResult { get; }
        public CollisionStatus CombinedResult => BendStateCollisionResult.Combine(PunchResult.Status, HolderResult.Status);
        public CollisionStatus AllRequiredStatesResult => CombinedResult == CollisionStatus.COLLISION ? CollisionStatus.COLLISION : CollisionStatus.UNKNOWN;
        public string ManufacturingStatus => "CHUA_XAC_MINH";
        public string WinnerToolId => null;
        public IReadOnlyList<BendStateCoverage> UnknownStates { get; } = PlacementLists.Copy(new[] {
            BendStateCoverage.UnknownBefore, BendStateCoverage.UnknownDuring, BendStateCoverage.UnknownAfter });
        public bool SimulationOnly { get; }
        public IReadOnlyList<string> Reasons => PlacementLists.Reasons(PunchResult.Reasons.Concat(HolderResult.Reasons));
        internal VolumeCandidateCollisionResult(string id, int feature, AxialPairResult punch, AxialPairResult holder, bool simulation)
        { ToolId = id; FeatureId = feature; PunchResult = punch; HolderResult = holder; SimulationOnly = simulation; }
    }
    internal sealed class CollisionBatchRunState
    {
        private string stopped;
        public string Reason => Volatile.Read(ref stopped);
        internal void Stop(string reason) { Interlocked.CompareExchange(ref stopped, reason, null); }
    }
    internal sealed class CollisionBatchSnapshot
    {
        private readonly CollisionBatchRequest request;
        internal CollisionBatchRunState RunState { get; }
        public IReadOnlyList<VolumeCandidateCollisionResult> Candidates { get; }
        public IReadOnlyList<SheetEnvelopeCapture> Envelopes { get; }
        public bool Current => request.InvalidReason == null && RunState.Reason == null;
        public string InvalidReason => request.InvalidReason ?? RunState.Reason;
        public double ElapsedMilliseconds { get; }
        public int KernelQueries { get; }
        public int MaximumObservedConcurrency { get; }
        public int PreparationThreadId { get; }
        public string ProgressDiagnostic { get; }
        internal CollisionBatchSnapshot(CollisionBatchRequest request, CollisionBatchRunState state, IEnumerable<VolumeCandidateCollisionResult> candidates,
            IEnumerable<SheetEnvelopeCapture> envelopes, double elapsed, int queries, int concurrency, int threadId, string progressDiagnostic)
        { this.request = request; RunState = state; Candidates = PlacementLists.Copy(candidates); Envelopes = PlacementLists.Copy(envelopes);
            ElapsedMilliseconds = elapsed; KernelQueries = queries; MaximumObservedConcurrency = concurrency; PreparationThreadId = threadId; ProgressDiagnostic = progressDiagnostic; }
    }
    internal sealed class CollisionBatchWorker : IDisposable
    {
        private readonly object gate = new object();
        private CancellationTokenSource active;
        private CollisionBatchRunState activeState;
        private CollisionBatchSnapshot published;
        private int generation;
        private bool disposed;
        public CollisionBatchSnapshot Published => Volatile.Read(ref published);
        internal Task<CollisionBatchSnapshot> RunAsync(CollisionBatchRequest request, CollisionBatchOptions options = null,
            CancellationToken token = default(CancellationToken), IProgress<CollisionBatchProgress> progress = null)
        {
            CancellationTokenSource source; CollisionBatchRunState state; int run;
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(CollisionBatchWorker));
                activeState?.Stop("SUPERSEDED_BATCH"); active?.Cancel(); published?.RunState.Stop("SUPERSEDED_BATCH");
                source = CancellationTokenSource.CreateLinkedTokenSource(token); state = new CollisionBatchRunState();
                active = source; activeState = state; run = ++generation;
            }
            // The returned task is owned and awaited. No fire-and-forget UI
            // callback, COM access, or partial result publication occurs.
            return Task.Run(() => {
                try
                {
                    var result = Execute(request, options ?? new CollisionBatchOptions(), source.Token, progress, state);
                    lock (gate) if (!disposed && run == generation) Interlocked.Exchange(ref published, result);
                    return result;
                }
                finally { lock (gate) if (ReferenceEquals(active, source)) { active = null; activeState = null; } source.Dispose(); }
            });
        }
        internal static CollisionBatchSnapshot RunSynchronously(CollisionBatchRequest request, CollisionBatchOptions options = null,
            CancellationToken token = default(CancellationToken), IProgress<CollisionBatchProgress> progress = null) =>
            Execute(request, options ?? new CollisionBatchOptions(), token, progress, new CollisionBatchRunState());
        private static CollisionBatchSnapshot Execute(CollisionBatchRequest request, CollisionBatchOptions options, CancellationToken token,
            IProgress<CollisionBatchProgress> progress, CollisionBatchRunState state)
        {
            var watch = Stopwatch.StartNew(); int preparationThread = Thread.CurrentThread.ManagedThreadId;
            int calls = 0, done = 0, next = -1, concurrency = 0, maximumConcurrency = 0, executing = 1; string diagnostic = null;
            var progressGate = new object();
            var envelopes = new Dictionary<int, SheetEnvelopeCapture>();
            Func<string> invalid = () => {
                string external = options.ExternalInvalid?.Invoke(); if (external != null) state.Stop(external);
                if (request.InvalidReason != null) state.Stop(request.InvalidReason);
                if (token.IsCancellationRequested) state.Stop("CANCELLED");
                if (Volatile.Read(ref executing) != 0 && watch.ElapsedMilliseconds >= options.MaximumMilliseconds) state.Stop("JOB_TIME_BUDGET_EXCEEDED");
                return state.Reason;
            };
            Action consume = () => {
                options.ExternalConsume?.Invoke();
                if (Interlocked.Increment(ref calls) > options.MaximumKernelQueries) state.Stop("JOB_QUERY_BUDGET_EXCEEDED");
                string reason = invalid(); if (reason != null) throw new CollisionAbort(reason);
            };
            // Preserve 18 slots even if preparation, coverage, or budget fails.
            if (!options.Valid) state.Stop("INVALID_BATCH_POLICY");
            if (invalid() == null)
                foreach (var bend in request.Model.Bends)
                {
                    try
                    {
                        if (invalid() != null) break;
                        var supplied = request.Envelopes.Where(e => e.Binding.FeatureId == bend.FeatureId).ToArray();
                        if (supplied.Length > 1 || supplied.Any(e => !ReferenceEquals(e.Binding.Model, request.Model)))
                            throw new InvalidOperationException("MISMATCHED_OR_DUPLICATE_ENVELOPE");
                        envelopes[bend.FeatureId] = supplied.Length == 1 ? supplied[0] : PrismaticEnvelopeCompiler.Read(request.Model, bend, token);
                    }
                    catch (OperationCanceledException) { state.Stop("CANCELLED"); break; }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is System.IO.InvalidDataException || ex is ArgumentException)
                    { state.Stop("ENVELOPE_PREPARATION_FAILED"); }
                }
            if (invalid() == null)
                try
                {
                    var prepared = BendPlacementBuilder.PrepareForAsync(request.Model, request.Library);
                    if (prepared.Candidates.Count != 18 * request.Model.Bends.Count)
                        throw new InvalidOperationException("INCOMPLETE_PLACEMENT_CANDIDATE_SET");
                }
                catch (Exception ex) { state.Stop("PLACEMENT_PREPARATION_FAILED:" + ex.GetType().Name); }
            // Derive slots from the immutable bend inventory, even when a
            // malformed library prevents legacy placement preparation.
            var jobs = CompanyToolIds.All.SelectMany(id => request.Model.Bends.Select(b => new { Id = id, Bend = b })).ToArray();
            var output = new VolumeCandidateCollisionResult[jobs.Length];
            Action work = () => {
                while (true)
                {
                    int index = Interlocked.Increment(ref next); if (index >= jobs.Length) return;
                    var job = jobs[index]; int running = Interlocked.Increment(ref concurrency);
                    int old; do { old = Volatile.Read(ref maximumConcurrency); if (old >= running) break; } while (Interlocked.CompareExchange(ref maximumConcurrency, running, old) != old);
                    try
                    {
                        SheetEnvelopeCapture envelope; envelopes.TryGetValue(job.Bend.FeatureId, out envelope);
                        Func<CollisionPairKind, AxialPairResult> evaluate = pair => {
                            string admission=request.CadAdmission?.Invoke(job.Id,job.Bend.FeatureId);
                            if(admission!=null)return new AxialPairResult(new AxialCollisionQuery(envelope,null,pair,options.PairPolicy,invalid),
                                CollisionStatus.UNKNOWN,new[]{admission},0,0,0);
                            var evidence = request.FindSetups(job.Id, job.Bend.FeatureId, pair);
                            AxialToolOccupancy occupancy = evidence.Count == 1 && envelope != null && evidence[0].AppliesTo(envelope, request.Library) ? evidence[0].Occupancy :
                                new AxialToolOccupancy(null, null, 0, "UNVERIFIED_SETUP", false, null, new[] {
                                    evidence.Count > 1 ? "DUPLICATE_AXIAL_SETUP" : "TOOL_AXIAL_EXTENT_UNVERIFIED" });
                            return AxialCollisionEngine.Evaluate(new AxialCollisionQuery(envelope, occupancy, pair, options.PairPolicy, invalid, consume), token);
                        };
                        var punch = evaluate(CollisionPairKind.WorkpiecePunch); var holder = evaluate(CollisionPairKind.WorkpieceHolder);
                        output[index] = new VolumeCandidateCollisionResult(job.Id, job.Bend.FeatureId, punch, holder,
                            request.Model.SimulationOnly || envelope?.SimulationOnly == true || request.ToolSimulationOnly(job.Id));
                    }
                    catch (Exception ex)
                    {
                        state.Stop("COLLISION_BATCH_FAILURE:" + ex.GetType().Name);
                        var punchQuery = new AxialCollisionQuery(null, null, CollisionPairKind.WorkpiecePunch, invalid: invalid);
                        var holderQuery = new AxialCollisionQuery(null, null, CollisionPairKind.WorkpieceHolder, invalid: invalid);
                        output[index] = new VolumeCandidateCollisionResult(job.Id, job.Bend.FeatureId,
                            AxialCollisionEngine.Evaluate(punchQuery), AxialCollisionEngine.Evaluate(holderQuery), request.Model.SimulationOnly);
                    }
                    finally { Interlocked.Decrement(ref concurrency); }
                    lock (progressGate)
                    {
                        int completed = ++done;
                        if (progress != null) try { progress.Report(new CollisionBatchProgress(completed, jobs.Length, job.Id, job.Bend.FeatureId, index)); }
                            catch (Exception ex) { diagnostic = "PROGRESS_CALLBACK_FAILED:" + ex.GetType().Name; }
                    }
                }
            };
            using (token.Register(() => state.Stop("CANCELLED")))
            {
                if (options.MaximumWorkers == 1 || !options.Valid) work();
                else Task.WaitAll(Enumerable.Range(0, options.MaximumWorkers).Select(i => Task.Run(work)).ToArray());
                invalid();
            }
            Volatile.Write(ref executing, 0); watch.Stop();
            // Stable public order remains bend-major, independent of scheduling.
            var ordered = request.Model.Bends.SelectMany(b => CompanyToolIds.All.Select(id => output.Single(r => r.FeatureId == b.FeatureId && r.ToolId == id))).ToArray();
            return new CollisionBatchSnapshot(request, state, ordered, request.Model.Bends.Where(b => envelopes.ContainsKey(b.FeatureId)).Select(b => envelopes[b.FeatureId]),
                watch.Elapsed.TotalMilliseconds, calls, maximumConcurrency, preparationThread, diagnostic);
        }
        public void Dispose()
        {
            lock (gate)
            { if (disposed) return; disposed = true; generation++; activeState?.Stop("BATCH_DISPOSED"); published?.RunState.Stop("BATCH_DISPOSED"); active?.Cancel(); }
        }
    }
}
