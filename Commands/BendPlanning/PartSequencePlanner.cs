using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlanning
{
    internal sealed class PartSequencePlanner : IDisposable
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, CachedStep> cache = new Dictionary<string, CachedStep>(StringComparer.Ordinal);
        private readonly Queue<string> fifo = new Queue<string>();
        private readonly int maximumCacheEntries;
        private CancellationTokenSource active;
        private PlacementLease runLease;
        private bool disposed;
        internal long CacheBytes { get { lock(gate) return cache.Values.Sum(c => c.Bytes); } }
        private sealed class Node
        {
            internal SequenceSheetState State;
            internal string Setup, PunchSource, HolderSource;
            internal List<SequenceStepResult> Steps;
            internal BendProcessPlan Previous;
        }
        private sealed class CachedStep
        {
            internal SequenceTransition Transition;
            internal ProcessPairResult[] Pairs;
            internal long Bytes { get; }
            internal CachedStep(SequenceStepResult step, long libraryBytes)
            {
                Transition = step.Transition;
                // Freeze numeric facts, not old job/cancellation callbacks.
                Pairs = step.Pairs.Select(p => Copy(p, () => null, Transition.Plan)).ToArray();
                var plan = Transition.Plan;
                Bytes = 2048 + libraryBytes + Pairs.Sum(p => 512L + p.Reasons.Sum(s => 32L + s.Length * 2L)) +
                    plan.Binding.Model.Bodies.Sum(b => 1024L + b.Edges.Count * 512L + b.Faces.Sum(f => 512L + f.Edges.Count * 16L) +
                        (b.EnvelopeTopology?.Faces.Sum(f => 512L + f.Loops.Sum(l => 128L + l.Edges.Count * 64L)) ?? 0)) +
                    plan.Segments.Sum(s => 2048L + s.Parts.Sum(p => 2048L + p.Source.Certificate.Intervals.Count * 128L +
                        p.Source.ObservedSlices.Sum(slice => (slice.Material?.Contours.Sum(c => c.Boundary.Count) ?? 0) * 512L)) +
                        new[] { s.Punch, s.Holder }.Sum(t => 1024L + (t?.Source?.Material?.Contours.Sum(c => c.Boundary.Count) ?? 0) * 512L));
            }
            internal SequenceStepResult Restore(Func<string> invalid) => new SequenceStepResult(Transition,
                Pairs.Select(p => Copy(p, invalid, Transition.Plan)));
            private static ProcessPairResult Copy(ProcessPairResult p, Func<string> invalid, BendProcessPlan plan) =>
                new ProcessPairResult(p.Pair, p.SegmentId, p.Phase, p.Status, p.Reasons, invalid, p.Witness,
                    p.IntervalsChecked, p.Refinements, p.KernelQueries, p.ExpectedContactIntervals, p.ElapsedMilliseconds, plan, p.ToleranceMm);
        }
        internal PartSequencePlanner(int maximumCacheEntries = 512)
        {
            if (maximumCacheEntries < 0 || maximumCacheEntries > 8192) throw new ArgumentOutOfRangeException(nameof(maximumCacheEntries));
            this.maximumCacheEntries = maximumCacheEntries;
        }
        internal Task<PartPlanningResult> RunAsync(string source, PlacementModelCapture model, ToolProfileLibrarySnapshot library,
            PartSequenceSpace space = null, SequencePlannerOptions options = null, CancellationToken token = default(CancellationToken),
            IProgress<SequencePlanningProgress> progress = null, Func<string> externalInvalid = null, Func<string> sharedInvalid = null, Action externalConsume = null)
        {
            CancellationTokenSource cts; PlacementLease lease;
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(PartSequencePlanner));
                active?.Cancel(); runLease?.Invalidate(); runLease = lease = new PlacementLease();
                active = cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            }
            return Task.Run(() => {
                try { return Run(source, model, library, space, options, cts.Token, progress,
                    () => !lease.Current ? "SUPERSEDED_SEQUENCE_SEARCH" : externalInvalid?.Invoke(),
                    () => !lease.Current ? "SUPERSEDED_SEQUENCE_SEARCH" : sharedInvalid?.Invoke() ?? externalInvalid?.Invoke(), externalConsume); }
                finally { lock (gate) if (ReferenceEquals(active, cts)) active = null; cts.Dispose(); }
            });
        }
        // Synchronous entry is for managed batch workers, never a COM callback.
        internal PartPlanningResult Run(string source, PlacementModelCapture model, ToolProfileLibrarySnapshot library,
            PartSequenceSpace space = null, SequencePlannerOptions options = null, CancellationToken token = default(CancellationToken),
            IProgress<SequencePlanningProgress> progress = null, Func<string> externalInvalid = null, Func<string> sharedInvalid = null, Action externalConsume = null)
        {
            options = options ?? new SequencePlannerOptions(); var watch = Stopwatch.StartNew();
            int nodes = 0, queries = 0, hits = 0, misses = 0, stored = 0; string stop = null;
            long retainedBytes = 0;
            long lastProgress = -100; string lastProgressTool = null;
            Func<bool, string> check = full => {
                try { return (full ? externalInvalid?.Invoke() : sharedInvalid?.Invoke() ?? externalInvalid?.Invoke()) ?? (token.IsCancellationRequested ? "CANCELLED_SEQUENCE_SEARCH" :
                    model?.Lease.Current == false ? "STALE_SEQUENCE_MODEL" : library != null && !AssemblyCacheKeys.CatalogCurrent(library) ?
                    "STALE_SEQUENCE_TOOL_LIBRARY" : full ? space?.InvalidReason : space?.SharedInvalidReason); }
                catch { return "SEQUENCE_FRESHNESS_GUARD_FAILED"; }
            };
            Func<string> current = () => check(true);
            var displayGuard = new AssemblyResultReadGuard(current, () => check(false));
            Func<string> heldCurrent = displayGuard.Check;
            Func<bool> proceed = () => {
                string stale = check(false);
                if (stale != null) stop = stale;
                else if (watch.ElapsedMilliseconds >= options.Milliseconds) stop = "SEQUENCE_TIME_BUDGET_EXCEEDED";
                else if (queries >= options.MaximumKernelQueries) stop = "SEQUENCE_QUERY_BUDGET_EXCEEDED";
                else if (retainedBytes >= options.MaximumRetainedBytes) stop = "SEQUENCE_MEMORY_BUDGET_EXCEEDED";
                return stop == null;
            };
            var tools = new List<SequenceToolResult>();
            long libraryBytes = library?.Tools?.Sum(t => 2048L + (t.Parts?.Sum(p => (p.Source?.ByteCount ?? 0) +
                (p.Entities?.Sum(e => 1024L + (e.Curves?.Count ?? 0) * 512L) ?? 0)) ?? 0)) ?? 0;
            string missing = !options.Valid ? "INVALID_SEQUENCE_SEARCH_POLICY" : string.IsNullOrWhiteSpace(source) ? "MISSING_PART_SOURCE_IDENTITY" : model == null ? "MISSING_NATIVE_MODEL" :
                model.Bends.Count == 0 ? "NO_BENDS_TO_PLAN" : space == null ? "MISSING_PROCESS_STATE_GRAPH_AND_KINEMATIC_SOURCE" :
                !ReferenceEquals(space.Model, model) ? "SEQUENCE_MODEL_IDENTITY_MISMATCH" : space.InputReasons.FirstOrDefault() ?? space.Reasons.FirstOrDefault();
            string scopeKey = ToolEvidenceCanonical.Hash("SequenceNumericCacheV1", source, AssemblyCacheKeys.TokenOf(model),
                AssemblyCacheKeys.TokenOf(space), space?.PayloadSha256, AssemblyCacheKeys.Catalog(library), options.Canonical);
            var byState = space?.Transitions.GroupBy(t => t.BeforeStateId).ToDictionary(g => g.Key,
                g => g.OrderBy(t => t.FeatureId).ThenBy(t => t.Id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            var states = space?.States.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (var tool in CompanyToolIds.All)
            {
                var paths = new List<BendSequenceResult>(); var reasons = new List<string>(); bool unknownFrontier = false;
                var coverage = SequenceSearchCoverage.MissingEvidence;
                if (missing != null) reasons.Add(missing);
                else if (proceed())
                {
                    var stack = new Stack<Node>();
                    stack.Push(new Node { State = states[space.InitialStateId], Steps = new List<SequenceStepResult>() });
                    while (stack.Count > 0 && proceed())
                    {
                        if (current() != null) { stop = current(); break; }
                        if (nodes >= options.MaximumNodes) { stop = "SEQUENCE_NODE_BUDGET_EXCEEDED"; break; }
                        if (stored >= options.MaximumResults) { stop = "SEQUENCE_RESULT_MEMORY_BUDGET_EXCEEDED"; break; }
                        var node = stack.Pop(); nodes++;
                        if (node.State.CompletedFeatureIds.Count == model.Bends.Count)
                        {
                            paths.Add(new BendSequenceResult(tool, node.Setup, node.Steps, true, space.SimulationOnly, heldCurrent)); stored++; continue;
                        }
                        SequenceTransition[] edges;
                        if (!byState.TryGetValue(node.State.Id, out edges)) edges = new SequenceTransition[0];
                        edges = edges.Where(t => t.ToolId == tool).ToArray();
                        if (edges.Length == 0)
                        {
                            unknownFrontier = true; reasons.Add("MISSING_SUCCESSOR_STATE_OR_PROCESS_PLAN");
                            paths.Add(new BendSequenceResult(tool, node.Setup, node.Steps, false, space.SimulationOnly, heldCurrent)); stored++; continue;
                        }
                        // Reverse push preserves stable native-ID-first DFS.
                        for (int i = edges.Length - 1; i >= 0 && proceed(); i--)
                        {
                            if (stored >= options.MaximumResults) { stop = "SEQUENCE_RESULT_MEMORY_BUDGET_EXCEEDED"; break; }
                            var edge = edges[i];
                            string punch = edge.Plan.Segments.First().Punch?.AssemblyCanonical;
                            string holder = edge.Plan.Segments.First().Holder?.AssemblyCanonical;
                            if (node.Setup != null && (node.Setup != edge.SetupIdentitySha256 || node.PunchSource != punch || node.HolderSource != holder))
                            { unknownFrontier = true; reasons.Add("INCONSISTENT_WHOLE_PART_PUNCH_HOLDER_SETUP"); continue; }
                            if (node.Previous != null && new[] { CollisionPairKind.WorkpiecePunch, CollisionPairKind.WorkpieceHolder }.Any(pair =>
                                node.Previous.Segments.Last().Tool(pair) == null || edge.Plan.Segments.First().Tool(pair) == null ||
                                !BendProcessPlan.Joins(node.Previous.Segments.Last().Tool(pair).Motion, edge.Plan.Segments.First().Tool(pair).Motion)))
                            { unknownFrontier = true; reasons.Add("UNBOUND_INTER_BEND_TOOL_MOTION"); continue; }
                            var key = ToolEvidenceCanonical.Hash(scopeKey, edge.Canonical);
                            CachedStep cached; SequenceStepResult step = null;
                            lock (gate) { if (cache.TryGetValue(key, out cached) && edge.Plan.InvalidatedReason == null) step = cached.Restore(heldCurrent); }
                            if (step != null) hits++;
                            else
                            {
                                misses++; var pairs = new List<ProcessPairResult>();
                                foreach (var segment in edge.Plan.Segments)
                                    foreach (CollisionPairKind pair in Enum.GetValues(typeof(CollisionPairKind)))
                                    {
                                        if (!proceed()) break;
                                        var result = ProcessCollisionEngine.Evaluate(edge.Plan, segment, pair, options.Policy, token,
                                            () => { proceed(); return check(false) ?? stop; },
                                            () => { queries++; externalConsume?.Invoke(); if (!proceed()) throw new CollisionAbort(stop); });
                                        // Rebind held proof to source/run freshness, not elapsed CPU budget.
                                        pairs.Add(new ProcessPairResult(result.Pair, result.SegmentId, result.Phase, result.Status, result.Reasons,
                                            heldCurrent, result.Witness, result.IntervalsChecked, result.Refinements, result.KernelQueries,
                                            result.ExpectedContactIntervals, result.ElapsedMilliseconds, edge.Plan, result.ToleranceMm));
                                    }
                                step = new SequenceStepResult(edge, pairs);
                                if (stop == null && current() == null && maximumCacheEntries > 0)
                                {
                                    lock (gate)
                                    {
                                        if (!cache.ContainsKey(key)) { cache[key] = new CachedStep(step, libraryBytes); fifo.Enqueue(key); }
                                        while (cache.Count > maximumCacheEntries || cache.Values.Sum(c => c.Bytes) > Math.Min(16777216, options.MaximumRetainedBytes / 2))
                                            cache.Remove(fifo.Dequeue());
                                    }
                                }
                            }
                            var next = new List<SequenceStepResult>(node.Steps) { step };
                            // Conservative allocation accounting, including
                            // discarded alternatives; never an exact CLR heap claim.
                            retainedBytes += 256 + next.Count * 16L + step.Pairs.Count * 1024L + step.Reasons.Sum(r => 64L + r.Length * 2L);
                            if (step.Status == CollisionStatus.COLLISION)
                            {
                                paths.Add(new BendSequenceResult(tool, edge.SetupIdentitySha256, next, false, space.SimulationOnly, heldCurrent)); stored++;
                            }
                            else
                            {
                                if (stack.Count >= options.MaximumFrontier) { stop = "SEQUENCE_FRONTIER_MEMORY_BUDGET_EXCEEDED"; break; }
                                stack.Push(new Node { State = states[edge.AfterStateId], Steps = next, Setup = edge.SetupIdentitySha256,
                                    PunchSource = punch, HolderSource = holder, Previous = edge.Plan });
                            }
                        }
                        if (lastProgressTool != tool || watch.ElapsedMilliseconds - lastProgress >= 100)
                        {
                            lastProgress = watch.ElapsedMilliseconds; lastProgressTool = tool;
                            try { progress?.Report(new SequencePlanningProgress(tool, nodes, queries, watch.Elapsed.TotalMilliseconds)); } catch { reasons.Add("SEQUENCE_PROGRESS_OBSERVER_FAILED"); }
                        }
                    }
                    coverage = current() != null ? SequenceSearchCoverage.Interrupted : stop != null ? SequenceSearchCoverage.Bounded :
                        space.DeclaredAlternativesComplete && !unknownFrontier ? SequenceSearchCoverage.ExhaustiveDeclaredSpace : SequenceSearchCoverage.MissingEvidence;
                    if (!space.DeclaredAlternativesComplete) reasons.Add("DECLARED_SEQUENCE_ALTERNATIVES_NOT_COMPLETE");
                    if (paths.Count == 0) reasons.Add("NO_PROVEN_SEQUENCE_IN_SUPPLIED_SPACE");
                }
                if (stop != null) { reasons.Add(stop); if (missing == null && coverage == SequenceSearchCoverage.MissingEvidence) coverage = SequenceSearchCoverage.Bounded; }
                if (current() != null) coverage = SequenceSearchCoverage.Interrupted;
                tools.Add(new SequenceToolResult(tool, paths, coverage, reasons, heldCurrent));
            }
            if (current() != null) stop = current();
            long bytes; lock (gate) bytes = cache.Values.Sum(c => c.Bytes);
            return new PartPlanningResult(source, model, space, tools, heldCurrent, nodes, queries, hits, misses, bytes, watch.Elapsed.TotalMilliseconds, stop, displayGuard, options);
        }
        public void Dispose()
        { lock (gate) { disposed = true; active?.Cancel(); runLease?.Invalidate(); cache.Clear(); fifo.Clear(); } }
    }
}
