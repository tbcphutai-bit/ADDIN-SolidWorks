using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendPlanning;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private sealed class SequenceFixture
    {
        internal PlacementModelCapture Model;
        internal PartSequenceSpace Space;
        internal ToolProfileLibrarySnapshot Library = new ToolProfileLibrarySnapshot { Tools = new CompanyToolProfile[0] };
    }
    private static SequenceFixture SequenceExample(bool complete = true, bool changeSetup = false, bool wrongBoundary = false, bool missingPhase = false, int bends = 2, bool toolTeleport = false)
    {
        var f = new SequenceFixture(); var seed = CollisionModel(Rect("BASE", 0, 0, 1, 1));
        var frame = seed.Bends[0].Frame;
        int[] ids = new[] { 7, 42 }.Concat(Enumerable.Range(0, bends - 2).Select(i => 314 + i * 13)).ToArray();
        f.Model = new PlacementModelCapture("EXPLICIT_SEQUENCE_FIXTURE", 1, "SYNTHETIC", seed.Bodies,
            ids.Select(id => new PlacementBendSnapshot(id, seed.Bends[0].BodyId, frame, 1, -5, 5)), true, true);
        var edges = new List<SequenceTransition>(); var states = new Dictionary<string, SequenceSheetState>();
        var positions = new Dictionary<string, ToolPoint> { { "S", P(0, 0) }, { "E", P(4, 4) } };
        var completed = new Dictionary<string, int[]> { { "S", new int[0] }, { "E", ids } };
        var paths = new List<Tuple<string, string, int>>();
        foreach (bool reverse in new[] { false, true })
        {
            var order = reverse ? ids.Reverse().ToArray() : ids;
            for (int k = 0; k < bends; k++)
            {
                Func<int, string> state = n => n == 0 ? "S" : n == bends ? "E" : (reverse ? "B" : "A") + (bends == 2 ? "" : n.ToString());
                string before = state(k), after = state(k + 1);
                if (k + 1 < bends)
                { positions[after] = reverse ? P(4.0 * k / (bends - 1), 4) : P(4, 4.0 * k / (bends - 1)); completed[after] = order.Take(k + 1).ToArray(); }
                paths.Add(Tuple.Create(before, after, order[k]));
            }
        }
        foreach (var tool in CompanyToolIds.All)
        {
            int number = CompanyToolIds.All.ToList().IndexOf(tool);
            foreach (var path in paths)
            {
                var bend = f.Model.Bends.Single(b => b.FeatureId == path.Item3); var segments = new List<BendMotionSegment>();
                foreach (BendProcessPhase phase in Enum.GetValues(typeof(BendProcessPhase)))
                {
                    if (missingPhase && phase == BendProcessPhase.ApproachLoad) continue;
                    var start = phase <= BendProcessPhase.DuringBend ? positions[path.Item1] : positions[path.Item2];
                    var end = phase < BendProcessPhase.DuringBend ? positions[path.Item1] : positions[path.Item2];
                    var panel = ProcessPanel(f.Model, bend, "WHOLE_DECLARED_SHEET", Material(Rect("SHEET", 0, 0, 1, 1)),
                        new ProcessMotionLaw(startTranslation: start, endTranslation: end));
                    var obstacle = number == 0 ? Material(Disk("PUNCH", 2.5, .5, .2)) : number == 1 ?
                        Material(Disk("PUNCH", .5, .5, .2)) : Material(Disk("PUNCH", 20 + number, 20, .2));
                    var toolLaw = toolTeleport && path.Item1 != "S" ? new ProcessMotionLaw(startTranslation: P(3, 0), endTranslation: P(3, 0)) : new ProcessMotionLaw();
                    segments.Add(new BendMotionSegment(phase.ToString(), phase, new[] { panel }, ProcessTool(bend, obstacle, toolLaw),
                        ProcessTool(bend, Material(Rect("HOLDER", 50, 50, 1, 1))), ProcessHash("EXPLICIT_STATE_LAW")));
                }
                var plan = BendProcessPlan.Controlled(segments[0].Parts[0].Source.Binding, tool, ProcessHash(tool + path.Item1 + path.Item2), segments);
                if (!states.ContainsKey(path.Item1)) states[path.Item1] = new SequenceSheetState(path.Item1, PartSequenceSpace.Boundary(plan, false), completed[path.Item1]);
                if (!states.ContainsKey(path.Item2)) states[path.Item2] = new SequenceSheetState(path.Item2, PartSequenceSpace.Boundary(plan, true), completed[path.Item2]);
                edges.Add(new SequenceTransition(tool + path.Item1 + path.Item2, path.Item1, path.Item2,
                    ProcessHash("SAME_PHYSICAL_SETUP_" + tool + (changeSetup && path.Item1 != "S" ? "CHANGED" : "")), plan));
            }
        }
        if (wrongBoundary) states["A"] = new SequenceSheetState("A", ProcessHash("UNRELATED_GEOMETRY"), new[] { 7 });
        f.Space = PartSequenceSpace.Controlled(f.Model, ProcessHash("EXPLICIT_GRAPH_NOT_FINISHED_CAD"), "REV1", "S", states.Values, edges, complete);
        return f;
    }
    internal static Tuple<PartPlanningResult, PlacementModelCapture> SequenceUiFixture(int bends = 2)
    {
        var f = SequenceExample(bends: bends); using (var planner = new PartSequencePlanner())
            return Tuple.Create(planner.Run("PART", f.Model, f.Library, f.Space), f.Model);
    }
    private static int SequenceBenchmark(string output)
    {
        Directory.CreateDirectory(output); var records = new List<object>();
        foreach (int bends in new[] { 3, 6 })
        {
            var f = SequenceExample(complete: false, bends: bends);
            using (var planner = new PartSequencePlanner())
            {
                var cold = planner.Run("PART", f.Model, f.Library, f.Space, new SequencePlannerOptions(milliseconds: 120000));
                var warm = planner.Run("PART", f.Model, f.Library, f.Space, new SequencePlannerOptions(milliseconds: 120000));
                records.Add(new { Bends = bends, Tools = cold.Tools.Count, SimulationOnly = cold.SimulationOnly,
                    ColdMilliseconds = cold.ElapsedMilliseconds, WarmMilliseconds = warm.ElapsedMilliseconds,
                    ColdQueries = cold.KernelQueries, WarmQueries = warm.KernelQueries, Hits = warm.CacheHits, Misses = cold.CacheMisses,
                    Nodes = cold.VisitedNodes, RetainedBytes = warm.RetainedCacheBytes, CommonToolsInSuppliedSpace = cold.CommonToolIds.Count,
                    Stop = cold.StopReason, Coverage = cold.Tools.Select(t => t.Coverage.ToString()).Distinct().ToArray() });
                Check(cold.KernelQueries > 0 && warm.KernelQueries == 0 && cold.CommonToolIds.Count == warm.CommonToolIds.Count && cold.StopReason == null,
                    "Numeric planner benchmark cold/warm equivalent and inside budget");
            }
        }
        File.WriteAllText(Path.Combine(output, "sequence-benchmark.json"), Json.Serialize(records));
        Console.WriteLine(Json.Serialize(records)); return 0;
    }
    private static void SequencePlannerTests()
    {
        Test("sequence: 18 common-tool candidates with two genuinely different collision orders", () => {
            var f = SequenceExample(); Check(f.Space.Reasons.Count == 0, string.Join(",", f.Space.Reasons));
            using (var planner = new PartSequencePlanner())
            {
                var result = planner.Run("PART", f.Model, f.Library, f.Space);
                Check(result.Current && result.Tools.Count == 18 && result.KernelQueries > 0, "Real continuous collision queries for every company slot");
                var first = result.Tools[0];
                Check(first.Coverage == SequenceSearchCoverage.ExhaustiveDeclaredSpace && first.Sequences.Count == 2, "Two declared orders actually searched");
                Check(first.Sequences.Count(s => s.Status == CollisionStatus.COLLISION) == 1 && first.Sequences.Count(s => s.Status == CollisionStatus.CLEAR) == 1,
                    "Order 7/42 hits interior motion, 42/7 clears with identical tools");
                var hit = first.Sequences.Single(s => s.Status == CollisionStatus.COLLISION).Steps.Last().Pairs.First(p => p.Status == CollisionStatus.COLLISION);
                Check(hit.Witness.Parameter > 0 && hit.Witness.Parameter < 1 && hit.Phase == BendProcessPhase.DuringBend && SequenceStepResult.ValidCollision(hit), "Actual intermediate witness retained");
                Check(first.Sequences.Single(s => s.Status == CollisionStatus.CLEAR).Steps.Select(s => s.FeatureId).SequenceEqual(new[] { 42, 7 }), "Native IDs, never row numbers");
                Check(result.Tools[1].Decision == SequenceToolDecision.RejectedInDeclaredConditions && result.CommonToolIds.Count == 17, "One blocked tool does not reject Part");
                Check(result.Tools.SelectMany(t => t.Sequences).All(s => s.Steps.All(e => e.Transition.ToolId == s.ToolId)), "A sequence never mixes tools");
                Check(result.SimulationOnly && result.WinnerToolId == null && result.ManufacturingStatus == "CHUA_XAC_MINH", "No manufacturing winner from simulation");
            }
        });
        Test("sequence: warm certificates preserve exact pair reason witness and phase, zero repeat queries", () => {
            var f = SequenceExample(); using (var planner = new PartSequencePlanner())
            {
                var a = planner.Run("PART", f.Model, f.Library, f.Space); var b = planner.Run("PART", f.Model, f.Library, f.Space);
                Check(a.KernelQueries > 0 && b.KernelQueries == 0 && b.CacheHits > 0 && b.CacheMisses == 0, "Warm uses complete bound certificates");
                Func<PartPlanningResult, string> facts = r => string.Join("|", r.Tools.SelectMany(t => t.Sequences).SelectMany(s => s.Steps)
                    .SelectMany(s => s.Pairs).Select(p => p.Pair + ":" + p.Phase + ":" + p.Status + ":" + string.Join(",", p.Reasons) + ":" + p.Witness?.Parameter));
                Check(facts(a) == facts(b), "Cold/warm exact facts");
                var different = planner.Run("OTHER_OCCURRENCE_CONDITIONS", f.Model, f.Library, f.Space);
                Check(different.KernelQueries > 0 && different.CacheHits == 0, "Different source/occurrence is not a shared work pose");
                f.Space.Transitions[0].Plan.Invalidate();
                Check(!a.Current && a.CommonToolIds.Count == 0 && a.Tools.All(t => t.Decision == SequenceToolDecision.NeedsEvidence), "Held proof expires with any supplied process source");
            }
        });
        Test("sequence: missing process, unbound endpoints and changed setup remain UNKNOWN", () => {
            foreach (int kind in new[] { 0, 1, 2, 3 })
            {
                var f = SequenceExample(changeSetup: kind == 2, wrongBoundary: kind == 1, missingPhase: kind == 3);
                using (var planner = new PartSequencePlanner())
                {
                    var r = planner.Run("PART", f.Model, f.Library, kind == 0 ? null : f.Space);
                    Check(r.Tools.Count == 18 && r.CommonToolIds.Count == 0, "No best UNKNOWN is selected");
                    Check(r.Tools.All(t => t.Decision != SequenceToolDecision.ProvenInCheckedScope), "Missing evidence cannot become a common tool");
                }
            }
        });
        Test("sequence: bounded search, cancellation and stale model cannot manufacture exhaustive rejection", () => {
            var f = SequenceExample(); using (var planner = new PartSequencePlanner())
            {
                var r = planner.Run("PART", f.Model, f.Library, f.Space, new SequencePlannerOptions(nodes: 1));
                Check(r.StopReason == "SEQUENCE_NODE_BUDGET_EXCEEDED" && r.VisitedNodes == 1 && r.Tools.Count == 18, "Bounded lazy frontier; no factorial allocation");
                Check(r.Tools.All(t => t.Decision != SequenceToolDecision.RejectedInDeclaredConditions), "Incomplete search is not proof of no sequence");
                using (var cts = new CancellationTokenSource())
                { cts.Cancel(); var c = planner.Run("PART", f.Model, f.Library, f.Space, token: cts.Token); Check(!c.Current && c.KernelQueries == 0 && c.Tools.Count == 18, "Cancel preserves 18 unknown slots"); }
                f.Model.Lease.Invalidate(); Check(!r.Current && r.CommonToolIds.Count == 0, "Stale clears proof access");
            }
        });
        Test("sequence: incomplete declared space and unsigned review do not certify source", () => {
            var f = SequenceExample(false);
            var unsigned = PartSequenceSpace.Reviewed(f.Model, f.Space.SourceSha256, "REV", "S", f.Space.States, f.Space.Transitions,
                true, null, null, null, CancellationToken.None);
            using (var planner = new PartSequencePlanner())
            {
                var r = planner.Run("PART", f.Model, f.Library, unsigned);
                Check(r.CommonToolIds.Count == 0 && r.KernelQueries == 0, "Coordinates/hash are not review evidence");
                r = planner.Run("PART", f.Model, f.Library, f.Space);
                Check(r.Tools[1].Decision == SequenceToolDecision.NeedsEvidence && r.Tools[1].Coverage == SequenceSearchCoverage.MissingEvidence, "Pruned supplied routes cannot reject unexamined alternatives");
            }
        });
        Test("sequence: async supersession and invalidation withdraw held sequences", () => {
            var f = SequenceExample(); using (var planner = new PartSequencePlanner())
            {
                var a = planner.RunAsync("PART", f.Model, f.Library, f.Space).Result;
                var b = planner.RunAsync("PART", f.Model, f.Library, f.Space).Result;
                Check(!a.Current && a.CommonToolIds.Count == 0 && b.Current && b.CacheHits > 0, "New generation expires old display while restoring safe numeric facts");
                planner.Dispose(); Check(!b.Current, "Disposed planner cannot retain proof");
            }
        });
        Test("sequence: many native bends, finite alternatives, explicit memory and query limits", () => {
            var f = SequenceExample(complete: false, bends: 12);
            Check(f.Space.Transitions.Count == 18 * 24 && f.Space.States.Count == 24, "Explicit sparse graph, not 12! materialized permutations");
            using (var planner = new PartSequencePlanner())
            {
                var r = planner.Run("PART", f.Model, f.Library, f.Space, new SequencePlannerOptions(queries: 1));
                Check(r.StopReason == "SEQUENCE_QUERY_BUDGET_EXCEEDED" && r.Tools.Count == 18 && r.KernelQueries == 1 && r.CommonToolIds.Count == 0, "Query budget cannot create a winner");
                r = planner.Run("PART", f.Model, f.Library, f.Space, new SequencePlannerOptions(retainedBytes: 16384));
                Check(r.StopReason == "SEQUENCE_MEMORY_BUDGET_EXCEEDED" && r.Tools.Count == 18 && r.Tools.All(t => t.Decision != SequenceToolDecision.RejectedInDeclaredConditions), "Memory cutoff explicit, never exhaustive");
            }
        });
        Test("sequence: tool teleport and unwitnessed COLLISION cannot prune or certify a route", () => {
            var f = SequenceExample(toolTeleport: true);
            using (var planner = new PartSequencePlanner())
            {
                var r = planner.Run("PART", f.Model, f.Library, f.Space);
                Check(r.CommonToolIds.Count == 0 && r.Tools[0].Reasons.Contains("UNBOUND_INTER_BEND_TOOL_MOTION"), "No unexamined inter-step tool teleport");
                var t = f.Space.Transitions.First();
                var pairs = t.Plan.Segments.SelectMany(s => Enum.GetValues(typeof(CollisionPairKind)).Cast<CollisionPairKind>().Select(p =>
                    new ProcessPairResult(p, s.Id, s.Phase, s.Phase == BendProcessPhase.DuringBend && p == CollisionPairKind.WorkpiecePunch ? CollisionStatus.COLLISION : CollisionStatus.CLEAR,
                        new[] { "NO_WITNESS" }, () => null, null, 0, 0, 0, 0, 0, t.Plan)));
                Check(new SequenceStepResult(t, pairs).Status == CollisionStatus.UNKNOWN, "Collision string without an actual source-bound witness is not rejection evidence");
            }
        });
        Test("sequence: signed source review expiry and explicit source revision invalidation", () => {
            var f = SequenceExample(); using (var review = Reviews()) using (var planner = new PartSequencePlanner())
            {
                review.Authority.Purposes = review.Authority.Purposes.Concat(new[] { "PartSequenceSpace" }).ToArray(); review.Reload();
                Func<ToolReviewProof, PartSequenceSpace> make = proof => PartSequenceSpace.Reviewed(f.Model, f.Space.SourceSha256, "REV", "S", f.Space.States,
                    f.Space.Transitions, true, review.Verifier, proof, review.Root, CancellationToken.None);
                var definition = make(null); var signed = make(review.Sign("PartSequenceSpace", definition.PayloadSha256));
                Check(signed.Reasons.Count == 0, "Signed exact graph payload accepted as simulation evidence");
                var r = planner.Run("PART", f.Model, f.Library, signed); Check(r.CommonToolIds.Count == 17 && r.EvidenceLevel == SequenceEvidenceLevel.SimulationOnly, "External simulation review never becomes physical verification");
                review.Now = review.Now.AddDays(2); Check(!r.Current && r.CommonToolIds.Count == 0 && r.Tools.All(t => t.Decision == SequenceToolDecision.NeedsEvidence), "Review expiry withdraws held common-tool claims");
                r = planner.Run("PART", f.Model, f.Library, f.Space); f.Space.Invalidate();
                Check(!r.Current && r.InvalidReason == "STALE_SEQUENCE_SOURCE", "Source owner can explicitly invalidate revision without editing Part");
            }
        });
        Test("sequence: Assembly integration equals independent Part planner and isolates blocked siblings", () => {
            var f = SequenceExample();
            Func<int, AssemblyOccurrence> occ = i => new AssemblyOccurrence("ROOT/NORMAL" + i, "ROOT", "NORMAL" + i, "S", f.Model.Configuration,
                new double[] { 1,0,0,0,1,0,0,0,1,0,0,0,1 }, AssemblyOccurrenceKind.SheetMetal, true);
            var normal = occ(0); var twin = occ(1);
            var bad = new AssemblyOccurrence("ROOT/BAD", "ROOT", "BAD", normal.SourceIdentity, normal.Configuration, normal.Transform,
                AssemblyOccurrenceKind.SheetMetal, false, mirrored: true);
            var provisional = new AssemblySourceInput("S", f.Model, new[] { normal, twin }, sequence: f.Space);
            var use = AssemblyProgramUse.ControlledPartLocal("S", AssemblyCacheKeys.Program(provisional));
            var partition = AssemblyOccurrenceEligibility.Partition("S", f.Model, new[] { normal, twin, bad }, null, null, null, use, null, "", f.Space);
            using (var coordinator = new AssemblyBatchCoordinator()) using (var planner = new PartSequencePlanner())
            {
                var result = coordinator.RunAsync(AssemblyRequest(partition, f.Library), new AssemblyBatchOptions()).Result;
                var valid = result.Parts.Single(p => p.Reason == null); var invalid = result.Parts.Single(p => p.Reason != null);
                var direct = planner.Run("S", f.Model, f.Library, f.Space);
                Check(valid.Quantity == 2 && valid.Planning != null && valid.Planning.CommonToolIds.SequenceEqual(direct.CommonToolIds), "Eligible twins actually execute planner, independent Part agrees");
                Check(valid.Planning.Tools.SelectMany(t => t.Sequences).Select(s => s.Status).SequenceEqual(direct.Tools.SelectMany(t => t.Sequences).Select(s => s.Status)), "Exact branch statuses agree");
                Check(invalid.Quantity == 1 && invalid.Planning == null && invalid.ProcessCandidates.All(p => p.ProcessResult == CollisionStatus.UNKNOWN), "Mirrored sibling never inherits normal proof");
                Check(valid.StaticCandidates.Count == 36 && valid.ProcessCandidates.Count == 36 && result.KernelQueries > 0, "18 per native bend plus actual planner kernel accounting");
            }
        });
        Test("sequence: bounded display preserves facts, full exit rejection, exception cleanup and thread isolation", () => {
            var f=SequenceExample();using(var planner=new PartSequencePlanner())
            {
                var r=planner.Run("PART",f.Model,f.Library,f.Space);
                Func<string> facts=()=> {
                    var rows=new List<string>();
                    foreach(var tool in r.Tools)
                    {
                        rows.Add(tool.ToolId+":"+tool.Decision+":"+tool.Coverage);
                        foreach(var sequence in tool.Sequences)
                        { rows.Add(sequence.Status.ToString()); rows.AddRange(sequence.Steps.SelectMany(x=>x.Pairs).Select(p=>p.Status+":"+p.Witness?.Parameter)); }
                    }
                    return string.Join("|",rows);
                };
                string strict=facts(),bounded=null;Check(r.ReadDisplay(()=>bounded=facts())&&strict==bounded,"Strict and bracketed facts equal");
                bool threw=false;try{r.ReadDisplay(()=>{throw new InvalidOperationException("DISPLAY_OBSERVER");});}catch(InvalidOperationException){threw=true;}
                Check(threw&&r.Current,"Exception unwinds private read scope");
                bool otherThreadCurrent=true;
                bool accepted=r.ReadDisplay(()=>{f.Space.Transitions.Last().Plan.Invalidate();otherThreadCurrent=System.Threading.Tasks.Task.Run(()=>r.Current).Result;});
                Check(!accepted&&!otherThreadCurrent&&!r.Current&&r.CommonToolIds.Count==0,"Other threads stay strict and exit audit discards all stale display facts");
            }
        });
    }
}
