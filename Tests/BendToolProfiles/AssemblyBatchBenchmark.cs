using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private static object BatchPerformance(string name,AssemblyBatchSnapshot r,double wall)=>new {
        Name=name,WallMilliseconds=wall,r.WorkerMilliseconds,r.InventoryMilliseconds,r.CaptureMilliseconds,r.Captures,r.CaptureHits,
        r.EnvelopeMilliseconds,r.StaticMilliseconds,r.ProcessMilliseconds,r.NumericKernelMilliseconds,r.KernelQueries,r.PeakWorkers,
        r.GeometryHits,r.GeometryMisses,r.StaticHits,r.StaticMisses,r.ProcessHits,r.ProcessMisses,r.CacheBytes,r.PeakManagedBytes,
        r.AnalysisJobs,r.UniqueSourceConfigurations,Instances=r.Parts.Sum(p=>p.Quantity),
        r.ProcessFullAudits,r.ProcessSharedGuardChecks,r.ProcessFreshnessAuditMilliseconds,
        StaticClear=r.Parts.Sum(p=>p.StaticCandidates.Count(c=>c.CombinedResult==CollisionStatus.CLEAR)),
        StaticCollision=r.Parts.Sum(p=>p.StaticCandidates.Count(c=>c.CombinedResult==CollisionStatus.COLLISION)),
        ProcessClear=r.Parts.Sum(p=>p.ProcessCandidates.Count(c=>c.ProcessResult==CollisionStatus.CLEAR)),
        ProcessCollision=r.Parts.Sum(p=>p.ProcessCandidates.Count(c=>c.ProcessResult==CollisionStatus.COLLISION))};
    private static int AssemblyBenchmark(string[] args)
    {
        if(args.Length<2||args.Length>3||args.Length==3&&!new[]{"Numeric18x3","Numeric18x6","Numeric120x25"}.Contains(args[2]))return 2;
        Directory.CreateDirectory(args[1]);temporary=Path.Combine(args[1],"controlled-proof-fixtures");Directory.CreateDirectory(temporary);
        var fixtures=new List<SeatingFixture>();var performance=new List<object>();
        var samples=new List<ProcessPerformanceSample>();var sampleGate=new object();
        Action<ProcessPerformanceSample> sink=System.Environment.GetEnvironmentVariable("ADDIN_PROCESS_PROFILE")=="1"?
            (Action<ProcessPerformanceSample>)(s=>{lock(sampleGate)samples.Add(s);}):null;
        Action<string> saveProfile=name=> {
            if(sink==null)return;
            lock(sampleGate)
            {
                var rows=samples.GroupBy(s=>new {s.Phase,s.Pair}).Select(g=>new {Phase=g.Key.Phase.ToString(),Pair=g.Key.Pair.ToString(),Segments=g.Count(),
                    Metrics=g.SelectMany(s=>s.Metrics).GroupBy(m=>m.Key).ToDictionary(v=>v.Key,v=>new {Count=v.Sum(x=>x.Value.Count),Milliseconds=v.Sum(x=>x.Value.Milliseconds)})}).ToArray();
                File.WriteAllText(Path.Combine(args[1],name+"-process-profile.json"),Json.Serialize(rows));samples.Clear();
            }
        };
        try
        {
            for(int i=0;i<18;i++)fixtures.Add(Seating(toolIndex:i));
            var library=new ToolProfileLibrarySnapshot {Tools=CompanyToolIds.All.Select(id=>new CompanyToolProfile {Id=id,Parts=new ToolGeometryPart[0],Validation=new ToolProfileValidationResult()}).ToArray(),
                PlacementSources=new PlacementSourceSnapshot("CONTROLLED_ALL_18_ASSEMBLY_BENCHMARK",fixtures.Select(f=>f.Assembly),null,new string[0])};
            foreach(var fixture in new[]{new {Name="Numeric18x3",Sources=1,Bends=3,Instances=1},new{Name="Numeric18x6",Sources=1,Bends=6,Instances=1},
                new{Name="Numeric120x25",Sources=25,Bends=1,Instances=120}}.Where(f=>args.Length==2||f.Name==args[2]))
            {
                var fixtureClock=Stopwatch.StartNew();double referenceComparisonMs=0,coldComparisonMs=0,warmComparisonMs=0;
                var sources=new List<AssemblySourceInput>();
                for(int n=0;n<fixture.Sources;n++)
                {
                    var model=CollisionModel(Rect("CONTROLLED_SOURCE_"+n,n*.01,0,1,1),fixture.Bends);var sheets=BatchEnvelopes(model);
                    var setup=fixtures.SelectMany((f,i)=>sheets.SelectMany(e=>new[]{AxialEvidence(f,e,library,CollisionPairKind.WorkpiecePunch,i%3==0?P(1,2):P(21,22)),
                        AxialEvidence(f,e,library,CollisionPairKind.WorkpieceHolder,i%3==1?P(1,2):P(21,22))})).ToArray();
                    int quantity=fixture.Sources==1?1:n<20?5:4;
                    sources.Add(LocalSource("CONTROLLED_SOURCE_"+n,model,Enumerable.Range(0,quantity).Select(i=>Occurrence(n,i)),sheets,setup,
                        AssemblyPlans(model,full:true,slow:fixture.Sources==1)));
                }
                var reference=new List<AssemblyPartResult>();int calls=0;var clock=Stopwatch.StartNew();
                foreach(var s in sources)foreach(var o in s.Occurrences)
                {
                    var sr=CollisionBatchWorker.RunSynchronously(new CollisionBatchRequest(s.Model,library,s.Envelopes,s.Setups),new CollisionBatchOptions(1,120000,1000000));
                    var pr=ProcessBatchWorker.RunSynchronously(new ProcessBatchRequest(s.Model,library,s.Plans,s.Envelopes),new ProcessBatchOptions(1,120000,1000000).WithPerformance(sink));
                    calls+=sr.KernelQueries+pr.KernelQueries;reference.Add(new AssemblyPartResult(s.SourceKey,new[]{o},null,sr.Candidates,pr.Candidates,s.Model.Bends.Count,true,()=>null));
                }
                double baselineMs=clock.Elapsed.TotalMilliseconds;clock.Restart();string expected=ResultFingerprint(reference);
                referenceComparisonMs=clock.Elapsed.TotalMilliseconds;
                saveProfile(fixture.Name+"-independent");
                var unknownReasons=reference.SelectMany(p=>p.StaticCandidates.SelectMany(c=>c.PunchResult.Reasons.Concat(c.HolderResult.Reasons))
                    .Concat(p.ProcessCandidates.SelectMany(c=>c.PairSegments.Where(q=>q.Status==CollisionStatus.UNKNOWN).SelectMany(q=>q.Reasons))))
                    .GroupBy(r=>r).ToDictionary(g=>g.Key,g=>g.Count());
                bool complete=reference.All(p=>p.StaticCandidates.All(c=>c.CombinedResult!=CollisionStatus.UNKNOWN)&&p.ProcessCandidates.All(c=>c.ProcessResult!=CollisionStatus.UNKNOWN));
                if(!complete)performance.Add(new {Name=fixture.Name+"-incomplete-reference",WallMilliseconds=baselineMs,KernelQueries=calls,Reasons=unknownReasons});
                Check(complete,"Controlled benchmark must do complete real numeric work");
                performance.Add(new {Name=fixture.Name+"-independent-sequential",WallMilliseconds=baselineMs,KernelQueries=calls,Instances=fixture.Instances,UniqueGeometry=fixture.Sources,MaximumWorkers=1});
                using(var c=new AssemblyBatchCoordinator())
                {
                    var request=AssemblyRequest(sources,library);clock.Restart();var cold=c.RunAsync(request,new AssemblyBatchOptions(4,milliseconds:1200000).WithProcessPerformance(sink)).Result;
                    double coldMs=clock.Elapsed.TotalMilliseconds;Check(cold.Current&&cold.KernelQueries>0,"Cold real solver work completes");
                    saveProfile(fixture.Name+"-cold");
                    clock.Restart();string coldFingerprint=ResultFingerprint(cold.Parts);coldComparisonMs=clock.Elapsed.TotalMilliseconds;
                    Check(coldFingerprint==expected,"Cold batch identical to independent per-instance solver, including witnesses/reasons");
                    performance.Add(BatchPerformance(fixture.Name+"-cold",cold,coldMs));
                    clock.Restart();var warm=c.RunAsync(request,new AssemblyBatchOptions(4,milliseconds:1200000)).Result;double warmMs=clock.Elapsed.TotalMilliseconds;
                    clock.Restart();string warmFingerprint=ResultFingerprint(warm.Parts);warmComparisonMs=clock.Elapsed.TotalMilliseconds;
                    Check(warm.Current&&warm.KernelQueries==0&&warmFingerprint==expected,"Warm cache exact proof reuse");
                    performance.Add(BatchPerformance(fixture.Name+"-warm",warm,warmMs));
                    performance.Add(new {Name=fixture.Name+"-measured-ratio",ColdSpeedup=baselineMs/coldMs,WarmSpeedup=baselineMs/warmMs});
                }
                performance.Add(new {Name=fixture.Name+"-validation-overhead",ReferenceFingerprintMilliseconds=referenceComparisonMs,
                    ColdFingerprintMilliseconds=coldComparisonMs,WarmFingerprintMilliseconds=warmComparisonMs,FixtureWallMilliseconds=fixtureClock.Elapsed.TotalMilliseconds});
                Console.WriteLine("ASSEMBLY_NUMERIC_BENCHMARK "+fixture.Name+" baselineMs="+baselineMs+" queries="+calls);
            }
            File.WriteAllText(Path.Combine(args[1],"assembly-numeric-performance.json"),Json.Serialize(new {Result="PASS",Assertions=assertions,SimulationOnly=true,
                Is64BitProcess=System.Environment.Is64BitProcess,ProcessorCount=System.Environment.ProcessorCount,Performance=performance}));return 0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(args[1],"assembly-numeric-performance.json"),Json.Serialize(new {Result="FAIL",Error=ex.ToString(),Performance=performance}));Console.WriteLine(ex);return 1;}
        finally{foreach(var f in fixtures)f.Dispose();}
    }
}
