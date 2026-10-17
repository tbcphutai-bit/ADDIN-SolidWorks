using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private sealed class DirectProgress : IProgress<CollisionBatchProgress>
    {
        private readonly Action<CollisionBatchProgress> action;
        internal DirectProgress(Action<CollisionBatchProgress> action) { this.action=action; }
        public void Report(CollisionBatchProgress value)=>action(value);
    }
    private static readonly List<object> envelopePerformance=new List<object>();
    private static SheetEnvelopeCapture[] BatchEnvelopes(PlacementModelCapture model) => model.Bends.Select(b=>
        AnalyticSheetEnvelope.Controlled(model,b,new AxialPrismaticSweep(new AxialInterval(-5,5),
            EnvelopeMaterial(Material(Rect("SHEET",1,.2,.5,.5)),b.Frame.FrameId)))).ToArray();
    private static ToolAxialSetupRecord AxialRecord(SeatingFixture f,SheetEnvelopeCapture envelope,CollisionPairKind pair,ToolPoint contact,
        double lo=-4,double hi=4)
    {
        return new ToolAxialSetupRecord {ToolId=f.Assembly.Id,FeatureId=envelope.Binding.FeatureId,Pair=pair.ToString(),
            ModelSnapshotId=envelope.Binding.ModelSnapshotId,ModelUpdateStamp=envelope.Binding.UpdateStamp,Configuration=envelope.Binding.Configuration,
            EnvelopeGeometrySha256=envelope.GeometrySha256,FrameId=envelope.Binding.FrameId,MasterSourceSha256=f.Assembly.MasterSha256,
            ProfileGeometrySha256=pair==CollisionPairKind.WorkpiecePunch?f.Assembly.Punch.GeometrySha256:f.Assembly.HolderObstacles.DefinitionSha256,
            SeatingPayloadSha256=f.Assembly.SeatingEvidence.PayloadSha256,AxialSectionContract="UniformProfileOverListedSegments",
            ToolAxialOriginMm=0,ToolAxisSign=1,AxialUncertaintyMm=0,Segments=new List<ToolAxialSegmentRecord>{new ToolAxialSegmentRecord {LocalStartMm=lo,LocalEndMm=hi}},
            ContactPointMm=new PairPointRecord {X=contact.X,Y=contact.Y},ApproachDirection=new PairPointRecord {X=0,Y=-1}};
    }
    private static ToolAxialSetupEvidence AxialEvidence(SeatingFixture f,SheetEnvelopeCapture envelope,ToolProfileLibrarySnapshot library,
        CollisionPairKind pair,ToolPoint contact,Action<ToolAxialSetupRecord> edit=null)
    {
        f.Reviews.Authority.Purposes=f.Reviews.Authority.Purposes.Concat(new[]{"PunchAxialSetup","HolderAxialSetup"}).Distinct().ToArray();f.Reviews.Reload();
        var record=AxialRecord(f,envelope,pair,contact);edit?.Invoke(record);
        try {record.Review=f.Reviews.Sign(pair==CollisionPairKind.WorkpiecePunch?"PunchAxialSetup":"HolderAxialSetup",ToolAxialSetupEvidence.Payload(record));}
        catch(InvalidDataException) { } // Explicitly malformed omission test, no guessed defaults.
        return ToolAxialSetupEvidence.Read(record,envelope,library,f.Reviews.Verifier,f.Reviews.Root,CancellationToken.None);
    }
    private static CollisionBatchRequest WorkerRequest(SeatingFixture f,int bends=3,ToolPoint? contact=null)
    {
        var model=CollisionModel(Rect("ANCHOR",0,0,1,1),bends);var envelopes=BatchEnvelopes(model);var library=CollisionLibrary(f);
        var setup=envelopes.SelectMany(e=>new[]{AxialEvidence(f,e,library,CollisionPairKind.WorkpiecePunch,contact??P(21,22)),
            AxialEvidence(f,e,library,CollisionPairKind.WorkpieceHolder,contact??P(21,22))}).ToArray();
        return new CollisionBatchRequest(model,library,envelopes,setup);
    }
    private static void EnvelopeWorkerTests()
    {
        Test("axial setup: signed lengths origin axis frame and profile are all required",()=> {
            using(var f=Seating())
            {
                var request=WorkerRequest(f,1);var sheet=request.Envelopes[0];var library=request.Library;
                var valid=AxialEvidence(f,sheet,library,CollisionPairKind.WorkpiecePunch,P(21,22));Check(valid.Occupancy.Verified,"Controlled reviewed axial setup");
                foreach(Action<ToolAxialSetupRecord> bad in new Action<ToolAxialSetupRecord>[] {r=>r.ToolAxialOriginMm=null,r=>r.ToolAxisSign=null,r=>r.AxialUncertaintyMm=null,
                    r=>r.Segments[0].LocalEndMm=null,r=>r.FrameId="WRONG",r=>r.ModelUpdateStamp++,r=>r.EnvelopeGeometrySha256=new string('f',64),
                    r=>r.ProfileGeometrySha256=new string('f',64),r=>r.AxialSectionContract="GuessedInfiniteExtrusion",r=>r.ToolAxisSign=0})
                    Check(!AxialEvidence(f,sheet,library,CollisionPairKind.WorkpiecePunch,P(21,22),bad).Occupancy.Verified,"Coordinates/signature alone cannot bypass exact source or extent contract");
                var reversed=AxialEvidence(f,sheet,library,CollisionPairKind.WorkpieceHolder,P(21,22),r=>{r.ToolAxisSign=-1;r.ToolAxialOriginMm=10;r.Segments[0].LocalStartMm=1;r.Segments[0].LocalEndMm=4;});
                Check(reversed.Occupancy.Verified&&reversed.Occupancy.Segments[0].StartMm==6&&reversed.Occupancy.Segments[0].EndMm==9,"Explicit origin and reversed local axis place a measured finite segment");
            }
        });
        Test("worker: all 18 pairs run on managed workers with nontrivial geometric outcomes",()=> {
            var fixtures=new List<SeatingFixture>();
            try
            {
                for(int i=0;i<18;i++)fixtures.Add(Seating(toolIndex:i));
                var library=new ToolProfileLibrarySnapshot {Tools=CompanyToolIds.All.Select(id=>new CompanyToolProfile {Id=id,Parts=new ToolGeometryPart[0],Validation=new ToolProfileValidationResult()}).ToArray(),
                    PlacementSources=new PlacementSourceSnapshot("CONTROLLED_ALL_18_AXIAL",fixtures.Select(f=>f.Assembly),null,new string[0])};
                var model=CollisionModel(Rect("ANCHOR",0,0,1,1),3);var envelopes=BatchEnvelopes(model);var setup=new List<ToolAxialSetupEvidence>();
                for(int i=0;i<18;i++)foreach(var envelope in envelopes)foreach(CollisionPairKind pair in Enum.GetValues(typeof(CollisionPairKind)))
                    setup.Add(AxialEvidence(fixtures[i],envelope,library,pair,i%3==0?P(1,2):i%3==1?P(-1,-3):P(21,22)));
                var request=new CollisionBatchRequest(model,library,envelopes,setup);var reports=new List<CollisionBatchProgress>();int caller=Thread.CurrentThread.ManagedThreadId;
                using(var worker=new CollisionBatchWorker())
                {
                    var start=Stopwatch.StartNew();var task=worker.RunAsync(request,new CollisionBatchOptions(workers:2),progress:new DirectProgress(p=>reports.Add(p)));
                    double returned=start.Elapsed.TotalMilliseconds;Check(returned<200,"Async entry does not compute geometry on caller");
                    var result=task.GetAwaiter().GetResult();Check(ReferenceEquals(worker.Published,result)&&result.Current,"Only the complete immutable result is published");
                    Check(result.Candidates.Count==54&&result.Candidates.Count(c=>c.CombinedResult==CollisionStatus.CLEAR)==18&&
                        result.Candidates.Count(c=>c.CombinedResult==CollisionStatus.COLLISION)==36,"Automatic 18-tool volume evaluation, not all UNKNOWN");
                    Check(result.Candidates.All(c=>c.SimulationOnly&&c.ManufacturingStatus=="CHUA_XAC_MINH"&&c.WinnerToolId==null&&c.UnknownStates.Count==3),"Software fixtures never become a feasible company setup/sequence");
                    Check(reports.Count==54&&reports.Select(p=>p.Completed).SequenceEqual(Enumerable.Range(1,54)),"Monotone complete progress");
                    Check(reports.All(p=>p.WorkerThreadId!=caller)&&result.PreparationThreadId!=caller&&result.MaximumObservedConcurrency<=2,"Managed preparation and bounded worker concurrency");
                    Check(reports.OrderBy(p=>p.DispatchIndex).Take(3).Select(p=>p.FeatureId).Distinct().Count()==3,"Fair dispatch visits each bend; parallel completions may arrive in another order");
                    envelopePerformance.Add(new {Fixture="ControlledAll18_3Bends",Rows=54,CallerReturnMs=returned,result.ElapsedMilliseconds,result.KernelQueries,result.MaximumObservedConcurrency});
                }
            }
            finally {foreach(var f in fixtures)f.Dispose();}
        });
        Test("worker: 3/6 bends retain 54/108 rows and independent missing holder extent",()=> {
            using(var f=Seating())foreach(int count in new[]{3,6})
            {
                var request=WorkerRequest(f,count,P(1,2));var onlyPunch=new CollisionBatchRequest(request.Model,request.Library,request.Envelopes,
                    request.Setups.Where(s=>s.Pair==CollisionPairKind.WorkpiecePunch));
                var result=CollisionBatchWorker.RunSynchronously(onlyPunch,new CollisionBatchOptions(workers:1));
                Check(result.Candidates.Count==18*count,"No mapping/tool slot is dropped");
                foreach(var first in result.Candidates.Where(c=>c.ToolId==f.Assembly.Id))
                    Check(first.PunchResult.Status==CollisionStatus.COLLISION&&first.HolderResult.Status==CollisionStatus.UNKNOWN&&first.CombinedResult==CollisionStatus.COLLISION,"A missing holder length cannot erase a punch collision");
                envelopePerformance.Add(new {Fixture="SingleApprovedPunch_"+count,Rows=18*count,result.ElapsedMilliseconds,result.KernelQueries});
            }
        });
        Test("worker: sync compatibility and async results agree",()=> {
            using(var f=Seating())using(var worker=new CollisionBatchWorker())
            {
                var request=WorkerRequest(f,3);var sync=CollisionBatchWorker.RunSynchronously(request,new CollisionBatchOptions(workers:1));
                var async=worker.RunAsync(request,new CollisionBatchOptions(workers:2)).GetAwaiter().GetResult();
                Check(sync.Candidates.Select(c=>c.CombinedResult).SequenceEqual(async.Candidates.Select(c=>c.CombinedResult)),"Deterministic stable public order and decisions");
                Check(sync.Candidates.All(c=>c.FeatureId==request.Model.Bends[(sync.Candidates.ToList().IndexOf(c))/18].FeatureId),"Bend-major output remains compatible");
            }
        });
        Test("worker: rebuild source mapping holder review and pose invalidation suppress held results",()=> {
            for(int scenario=0;scenario<6;scenario++)using(var f=Seating())using(var worker=new CollisionBatchWorker())
            {
                var request=WorkerRequest(f,3);int kind=scenario;
                var progress=new DirectProgress(p=>{if(p.Completed==1) {
                    if(kind==0)request.Model.Lease.Invalidate();else if(kind==1)request.Library.Invalidate();
                    else if(kind==2)f.Assembly.MappingReview.Invalidate();else if(kind==3)f.Assembly.HolderObstacles.Invalidate();else if(kind==4)f.Reviews.Now=f.Reviews.Now.AddDays(2);
                    else request.Setups[0].Review.Invalidate();
                }});
                var result=worker.RunAsync(request,new CollisionBatchOptions(workers:1),progress:progress).GetAwaiter().GetResult();
                Check(!result.Current&&result.Candidates.All(c=>c.CombinedResult==CollisionStatus.UNKNOWN&&c.PunchResult.CertifiedClearanceLowerMm==null&&c.HolderResult.Witness==null),"No stale geometric result survives scenario "+scenario);
                Check(result.Candidates.Count==54,"All rows remain visible after source invalidation");
            }
        });
        Test("worker: cancellation job budgets and bad policies preserve rows without CLEAR",()=> {
            using(var f=Seating())using(var cancel=new CancellationTokenSource())using(var worker=new CollisionBatchWorker())
            {
                var request=WorkerRequest(f,3);var result=worker.RunAsync(request,new CollisionBatchOptions(workers:1),cancel.Token,
                    new DirectProgress(p=>{if(p.Completed==1)cancel.Cancel();})).GetAwaiter().GetResult();
                Check(result.Candidates.Count==54&&!result.Current&&result.Candidates.All(c=>c.CombinedResult==CollisionStatus.UNKNOWN),"Cancellation suppresses a partially complete batch");
            }
            using(var f=Seating())
            {
                var request=WorkerRequest(f,3);
                foreach(var options in new[]{new CollisionBatchOptions(workers:1,kernelQueries:1),new CollisionBatchOptions(workers:0)})
                {
                    var result=CollisionBatchWorker.RunSynchronously(request,options);Check(!result.Current&&result.Candidates.Count==54&&result.Candidates.All(c=>c.CombinedResult==CollisionStatus.UNKNOWN),"Budget/policy failure cannot publish partial CLEAR");
                }
                var timed=CollisionBatchWorker.RunSynchronously(request,new CollisionBatchOptions(workers:1,milliseconds:2),progress:new DirectProgress(p=>Thread.Sleep(3)));
                Check(!timed.Current&&timed.InvalidReason=="JOB_TIME_BUDGET_EXCEEDED","Elapsed batch time is enforced");
                request.Library.Tools=request.Library.Tools.Concat(request.Library.Tools.Take(1)).ToArray();
                var malformed=CollisionBatchWorker.RunSynchronously(request,new CollisionBatchOptions(workers:1));
                Check(!malformed.Current&&malformed.InvalidReason.StartsWith("PLACEMENT_PREPARATION_FAILED:"),"Malformed placement input becomes a diagnostic instead of faulting the task");
                Check(malformed.Candidates.Count==54&&malformed.Candidates.All(c=>c.CombinedResult==CollisionStatus.UNKNOWN),"Preparation failure preserves all candidate slots without partial CLEAR");
            }
        });
        Test("worker: newer generation atomically supersedes an unfinished batch",()=> {
            using(var f=Seating())using(var worker=new CollisionBatchWorker())using(var started=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
            {
                var request=WorkerRequest(f,1);var old=worker.RunAsync(request,new CollisionBatchOptions(workers:1),progress:new DirectProgress(p=>{if(p.Completed==1){started.Set();release.Wait();}}));
                Check(started.Wait(10000),"First worker reaches a controlled progress boundary");Check(worker.Published==null,"No partial rows leak into Published");
                var current=worker.RunAsync(request,new CollisionBatchOptions(workers:1)).GetAwaiter().GetResult();release.Set();var superseded=old.GetAwaiter().GetResult();
                Check(ReferenceEquals(worker.Published,current)&&current.Current&&!superseded.Current,"Latest generation wins independently of completion order");
                Check(superseded.Candidates.All(c=>c.CombinedResult==CollisionStatus.UNKNOWN),"Old CLEAR or COLLISION becomes unusable");
            }
        });
        Test("worker: progress failures are isolated and finished results do not time-expire as jobs",()=> {
            using(var f=Seating())using(var worker=new CollisionBatchWorker())
            {
                var request=WorkerRequest(f,1);var result=worker.RunAsync(request,new CollisionBatchOptions(workers:1,milliseconds:200),
                    progress:new DirectProgress(p=>{throw new InvalidOperationException("Synthetic progress listener");})).GetAwaiter().GetResult();
                Check(result.Current&&result.ProgressDiagnostic!=null,"Consumer progress failure cannot erase valid managed geometry");
                Thread.Sleep(220);Check(result.Current&&result.Candidates.Any(c=>c.CombinedResult==CollisionStatus.CLEAR),"Stopped job clock cannot invalidate a completed certificate");
                request.Model.Lease.Invalidate();Check(!result.Current&&result.Candidates.All(c=>c.CombinedResult==CollisionStatus.UNKNOWN),"Source freshness remains live after completion");
            }
        });
        File.WriteAllText(Path.Combine(temporary,"SYNTHETIC-envelope-performance.json"),Json.Serialize(envelopePerformance));
        string qaOutput=Environment.GetEnvironmentVariable("ADDIN_ENVELOPE_TEST_OUTPUT");
        if(!string.IsNullOrWhiteSpace(qaOutput)) {Directory.CreateDirectory(qaOutput);File.WriteAllText(Path.Combine(qaOutput,"SYNTHETIC-envelope-performance.json"),Json.Serialize(envelopePerformance));}
    }
}
