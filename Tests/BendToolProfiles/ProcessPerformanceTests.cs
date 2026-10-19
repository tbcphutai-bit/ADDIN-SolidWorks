using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    // Same complete synthetic plan used by the slow Assembly benchmark. Direct
    // pair evaluation isolates its numeric semantics from a timed-out batch's
    // global UNKNOWN lease. Pair policies/tolerances/budgets are unchanged.
    private static int ProcessReference(string[] args)
    {
        if(args.Length!=2)return 2;Directory.CreateDirectory(args[1]);
        var clock=Stopwatch.StartNew();var rows=new List<object>();int calls=0,intervals=0,refinements=0;
        foreach(int bends in new[]{3,6})
        {
            var model=CollisionModel(Rect("CONTROLLED_REFERENCE",0,0,1,1),bends);
            foreach(var plan in AssemblyPlans(model,full:true,slow:true))
                foreach(var segment in plan.Segments)
                    foreach(CollisionPairKind pair in Enum.GetValues(typeof(CollisionPairKind)))
                    {
                        var r=ProcessCollisionEngine.Evaluate(plan,segment,pair);
                        if(r.Status==CollisionStatus.UNKNOWN)throw new Exception("Incomplete pair reference: "+string.Join(",",r.Reasons));
                        calls+=r.KernelQueries;intervals+=r.IntervalsChecked;refinements+=r.Refinements;
                        rows.Add(new {Bends=bends,plan.ToolId,plan.Binding.FeatureId,Segment=segment.Id,Pair=pair.ToString(),Phase=r.Phase.ToString(),
                            Status=r.Status.ToString(),Reasons=r.Reasons,r.ToleranceMm,r.ExpectedContact,r.ExpectedContactIntervals,
                            Witness=r.Witness==null?null:new {r.Witness.Parameter,r.Witness.PartId,r.Witness.Geometry.PointMm.X,r.Witness.Geometry.PointMm.Y,
                                r.Witness.Geometry.AxialMm,r.Witness.Geometry.CrossSectionInteriorMarginLowerMm,r.Witness.Geometry.AxialOccupancyMarginLowerMm,
                                r.Witness.Geometry.ObservedSliceOnly,r.Witness.Geometry.WorkpieceComponent,r.Witness.Geometry.ToolComponent}});
                    }
        }
        File.WriteAllText(Path.Combine(args[1],"numeric-reference.json"),Json.Serialize(rows));
        Console.WriteLine(Json.Serialize(new {Result="PASS",Pairs=rows.Count,KernelQueries=calls,Intervals=intervals,Refinements=refinements,
            Milliseconds=clock.Elapsed.TotalMilliseconds,Is64BitProcess=System.Environment.Is64BitProcess}));return 0;
    }
    private static void ProcessPerformanceTests()
    {
        Test("process worker: unrelated plan invalidation cannot publish a partial certificate",()=> {
            var model=CollisionModel(Rect("BODY",0,0,1,1),2);var plans=AssemblyPlans(model,full:true,slow:true);
            int queries=0;var request=new ProcessBatchRequest(model,new ToolProfileLibrarySnapshot {Tools=new CompanyToolProfile[0]},plans);
            var options=new ProcessBatchOptions(1,120000).WithAssemblyGuard(()=>null,()=>{if(Interlocked.Increment(ref queries)==1)plans.Last().Invalidate();});
            var result=ProcessBatchWorker.RunSynchronously(request,options);
            Check(queries>0&&!result.Current&&result.Candidates.Count==36&&result.Candidates.All(c=>c.ProcessResult==CollisionStatus.UNKNOWN),
                "Staleness in a different plan invalidates all held rows before publication");
            Check(result.FullAudits>0&&result.SharedGuardChecks>result.FullAudits&&
                result.Candidates.SelectMany(c=>c.PairSegments).Any(r=>r.Reasons.Contains("STALE_PROCESS_OR_SETUP")),"Full audit remains mandatory and reason-coded");
        });
        Test("process pruning: parent separation proof preserves an intermediate collision in another panel",()=> {
            var original=OneMotion(Material(Disk("HIT",5*Math.Cos(.29),5*Math.Sin(.29),.08)),out var segment,out var model);
            var far=ProcessPanel(model,model.Bends[0],"FAR_VARIABLE",Material(Rect("FAR",80,80,1,1)),new ProcessMotionLaw(0,.2));
            var combined=new BendMotionSegment("COMBINED",segment.Phase,new[]{far}.Concat(segment.Parts),segment.Punch,segment.Holder,segment.ConstraintsSourceSha256);
            var plan=BendProcessPlan.Controlled(original.Binding,original.ToolId,original.SourceSha256,new[]{combined});
            var samples=new List<ProcessPerformanceSample>();
            var result=ProcessCollisionEngine.Evaluate(plan,combined,CollisionPairKind.WorkpiecePunch,performance:s=>samples.Add(s));
            var reference=ProcessCollisionEngine.Evaluate(original,segment,CollisionPairKind.WorkpiecePunch);
            Check(result.Status==CollisionStatus.COLLISION&&result.Witness.Parameter==reference.Witness.Parameter&&
                ToolPoint.Distance(result.Witness.Geometry.PointMm,reference.Witness.Geometry.PointMm)==0,"Same real intermediate witness survives pruning");
            Check(samples.Single().Metrics["Motion.ParentSeparationReuses"].Count>0&&result.Refinements>0,"Whole parent interval certificates are actually reused");
            model.Lease.Invalidate();Check(result.Status==CollisionStatus.UNKNOWN&&result.Witness==null,"Reused separation cannot outlive source lease");
        });
        Test("process diagnostics: observer preserves witnesses and cannot corrupt proof",()=> {
            var plan=OneMotion(Material(Disk("HIT",5*Math.Cos(.29),5*Math.Sin(.29),.08)),out var segment,out _);
            var baseline=ProcessCollisionEngine.Evaluate(plan,segment,CollisionPairKind.WorkpiecePunch);
            var samples=new List<ProcessPerformanceSample>();
            var measured=ProcessCollisionEngine.Evaluate(plan,segment,CollisionPairKind.WorkpiecePunch,performance:s=>samples.Add(s));
            Check(measured.Status==baseline.Status&&measured.Reasons.SequenceEqual(baseline.Reasons)&&measured.Witness.Parameter==baseline.Witness.Parameter&&
                ToolPoint.Distance(measured.Witness.Geometry.PointMm,baseline.Witness.Geometry.PointMm)==0,"Diagnostics preserve exact witness and reasons");
            Check(samples.Count==1&&samples[0].Metrics["Axial.Query"].Count>0&&samples[0].Metrics["Motion.Refinements"].Count==measured.Refinements,
                "Real interval and query counters are observed");
            var throwing=ProcessCollisionEngine.Evaluate(plan,segment,CollisionPairKind.WorkpiecePunch,performance:s=>throw new Exception("observer"));
            Check(throwing.Status==baseline.Status&&throwing.Witness.Parameter==baseline.Witness.Parameter,"Observer failure is isolated");
        });
    }
}
