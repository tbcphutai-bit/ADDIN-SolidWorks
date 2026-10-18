using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private sealed class AssemblyProgress : IProgress<AssemblyBatchProgress>
    {
        private readonly Action<AssemblyBatchProgress> action;
        internal AssemblyProgress(Action<AssemblyBatchProgress> action) { this.action=action; }
        public void Report(AssemblyBatchProgress value) => action(value);
    }
    private static AssemblyOccurrence Occurrence(int source,int instance,bool share=true,bool mirror=false,bool modified=false,bool virtualPart=false,
        bool flexible=false,AssemblyOccurrenceKind kind=AssemblyOccurrenceKind.SheetMetal,string config="Test") => new AssemblyOccurrence(
            "ROOT/SUB"+(instance%2)+"/INSTANCE"+source+"-"+instance,"ROOT/SUB"+(instance%2),"Part-1","SOURCE"+source,config,
            new double[]{1,0,0,0,1,0,0,0,1,instance*.1,0,0,1,0,0,0},kind,share,mirror,modified,virtualPart,flexible);
    private static AssemblySourceInput LocalSource(string key,PlacementModelCapture model,IEnumerable<AssemblyOccurrence> occurrences,
        IEnumerable<SheetEnvelopeCapture> sheets=null,IEnumerable<ToolAxialSetupEvidence> setups=null,IEnumerable<BendProcessPlan> plans=null,
        bool local=true,string conditions="CONTROLLED_REV_1")
    {
        var input=new AssemblySourceInput(key,model,occurrences,sheets,setups,plans,conditionsRevision:conditions);
        return local?new AssemblySourceInput(key,model,occurrences,sheets,setups,plans,
            AssemblyProgramUse.ControlledPartLocal(key,AssemblyCacheKeys.Program(input)),conditionsRevision:conditions):input;
    }
    private static AssemblyBatchRequest AssemblyRequest(IEnumerable<AssemblySourceInput> sources,ToolProfileLibrarySnapshot library=null,
        PlacementLease lease=null,AssemblyCheckScope scope=AssemblyCheckScope.EntireAssembly)
    {
        var a=sources.ToArray();return new AssemblyBatchRequest("CONTROLLED_ASSEMBLY","Default",scope,lease??new PlacementLease(),
            library??new ToolProfileLibrarySnapshot {Tools=new CompanyToolProfile[0]},a,a.SelectMany(s=>s.Occurrences));
    }
    private static BendProcessPlan[] AssemblyPlans(PlacementModelCapture model,bool full=false,bool slow=false,double shift=0)
    {
        return model.Bends.SelectMany(b=>Enumerable.Range(0,18).Select(i=> {
            var segments=new List<BendMotionSegment>();
            foreach(BendProcessPhase phase in Enum.GetValues(typeof(BendProcessPhase)))
            {
                if(!full&&phase!=BendProcessPhase.DuringBend)continue;
                double from=phase>=BendProcessPhase.FormingEnd?Math.PI/2:0,to=phase>=BendProcessPhase.DuringBend?Math.PI/2:0;
                var parts=MovingPanels(model,b,new ProcessMotionLaw(from,to));
                var hit=slow?Material(Disk("HIT",shift+5*Math.Cos(.29),5*Math.Sin(.29),.08)):Material(Rect("HIT",4+shift,-.1,.3,.2));
                var clear=Material(Rect("CLEAR",30+shift,30,1,1));
                segments.Add(new BendMotionSegment("EXPLICIT_"+phase,phase,parts,ProcessTool(b,i%3==0?hit:clear),
                    ProcessTool(b,i%3==1?hit:clear),ProcessHash("DECLARED_COMPLETE_PROGRAM")));
            }
            return BendProcessPlan.Controlled(segments[0].Parts[0].Source.Binding,CompanyToolIds.All[i],ProcessHash("PROGRAM_"+shift),segments);
        })).ToArray();
    }
    private static string ResultFingerprint(IEnumerable<AssemblyPartResult> parts)
    {
        // Per-instance comparison includes independent pair status, exact reason
        // sets, clearance, real witness coordinates and intermediate parameter.
        return string.Join("\n",parts.SelectMany(p=>p.Occurrences.Select(o=>o.Id+":"+string.Join("|",p.StaticCandidates.Select(c=>
            c.ToolId+":"+c.FeatureId+":"+StaticPairText(c.PunchResult)+":"+StaticPairText(c.HolderResult)))+"/"+
            string.Join("|",p.ProcessCandidates.Select(c=>c.ToolId+":"+c.FeatureId+":"+string.Join(";",c.PairSegments.Select(q=>
                q.Pair+":"+q.Phase+":"+q.Status+":"+string.Join(",",q.Reasons)+":"+(q.Witness==null?"-":
                    ToolEvidenceCanonical.Number(q.Witness.Parameter)+":"+ToolEvidenceCanonical.Point(q.Witness.Geometry.PointMm)+":"+ToolEvidenceCanonical.Number(q.Witness.Geometry.AxialMm))))))))
            .OrderBy(x=>x,StringComparer.Ordinal));
    }
    private static string StaticPairText(AxialPairResult p) => p.Status+":"+string.Join(",",p.Reasons)+":"+p.CertifiedClearanceLowerMm+":"+
        (p.Witness==null?"-":ToolEvidenceCanonical.Point(p.Witness.PointMm)+":"+ToolEvidenceCanonical.Number(p.Witness.AxialMm));
    private static void AssemblyBatchTests()
    {
        Test("assembly cache: bounded LRU, stale guards and exceptions cannot strand flights",()=> {
            var cache=new AssemblyManagedCache<string>(2,8);bool hit;int made=0;bool fresh=true;
            Func<string,string> get=k=>cache.Get(k,()=>{made++;return k;},_=>fresh,_=>4,CancellationToken.None,out hit);
            get("A");get("B");get("A");get("C");Check(made==3&&cache.Count==2&&cache.Bytes==8&&cache.Hits==1,"LRU budget/hits");
            get("B");Check(made==4,"Least recent key evicted");fresh=false;get("B");Check(cache.Count<=1,"Stale value removed, invalid result not cached");
            bool threw=false;try{cache.Get("FAIL",()=>"bad",_=>true,_=>throw new Exception(),CancellationToken.None,out hit);}catch{threw=true;}
            Check(threw,"Failure is visible");fresh=true;get("FAIL");Check(cache.Count<=2,"Faulted flight can retry");
        });
        Test("assembly cache: concurrent equal keys compile once and waiting cancellation is safe",()=> {
            var cache=new AssemblyManagedCache<string>(8,1024);int made=0;var started=new ManualResetEventSlim();var release=new ManualResetEventSlim();
            Func<string> get=()=> {bool hit;return cache.Get("ONE",()=>{Interlocked.Increment(ref made);started.Set();release.Wait();return "VALUE";},_=>true,_=>20,CancellationToken.None,out hit);};
            var tasks=Enumerable.Range(0,4).Select(_=>Task.Run(get)).ToArray();Check(started.Wait(1000),"Producer started");Thread.Sleep(40);release.Set();Task.WaitAll(tasks);
            Check(made==1&&tasks.All(t=>t.Result=="VALUE")&&cache.Hits==3&&cache.Waits>0,"Single-flight with three waiters");
            using(var cts=new CancellationTokenSource()){cts.Cancel();bool canceled=false;try{bool hit;cache.Get("X",()=>"X",_=>true,_=>1,cts.Token,out hit);}catch(OperationCanceledException){canceled=true;}Check(canceled,"Canceled access never creates a flight");}
        });
        Test("assembly: 120 occurrences / 25 unique, quantities and nested names survive",()=> {
            var inputs=Enumerable.Range(0,25).Select(i=>{var m=CollisionModel(Rect("BODY"+i,0,0,1,1));return LocalSource("SOURCE"+i,m,
                Enumerable.Range(0,i<20?5:4).Select(j=>Occurrence(i,j)));}).ToArray();
            using(var coordinator=new AssemblyBatchCoordinator())
            {
                var progress=new List<AssemblyBatchProgress>();var request=AssemblyRequest(inputs);
                var cold=coordinator.RunAsync(request,new AssemblyBatchOptions(workers:4),progress:new AssemblyProgress(progress.Add)).Result;
                Check(cold.Current&&cold.Parts.Count==25&&cold.LeafOccurrences==120&&cold.Parts.Sum(p=>p.Quantity)==120,"Verified local program groups all 120");
                Check(cold.GeometryMisses==25&&cold.GeometryHits==0&&cold.PeakWorkers<=4&&cold.KernelQueries==0,"One managed geometry per source; missing setup fast precheck has no kernel work");
                Check(progress.Select(p=>p.CompletedParts).SequenceEqual(Enumerable.Range(1,25))&&progress.Last().Candidates==450,"Stable monotone part progress");
                Check(cold.Parts.All(p=>p.StaticCandidates.Count==18&&p.ProcessCandidates.Count==18&&p.ProcessCandidates.All(c=>c.PairSegments.Count==10)),"All 18 slots and five independent phase pairs");
                string before=ResultFingerprint(cold.Parts);var warm=coordinator.RunAsync(request,new AssemblyBatchOptions(workers:2)).Result;
                Check(!cold.Current&&cold.Parts.All(p=>!p.Current),"Held old generation expires");
                Check(warm.Current&&warm.GeometryHits==25&&warm.StaticHits==25&&warm.ProcessHits==25&&warm.KernelQueries==0,"Warm managed caches restore under new generation");
                Check(before==ResultFingerprint(warm.Parts),"Warm exact statuses/reasons/witnesses and quantities");
                Check(warm.Parts.All(p=>p.SimulationOnly&&p.ManufacturingStatus=="CHUA_XAC_MINH"&&p.WinnerToolId==null),"Simulation cannot become manufacturing pass");
            }
        });
        Test("assembly: absent program proof isolates poses but reuses local geometry",()=> {
            var m=CollisionModel(Rect("BODY",0,0,1,1));var source=LocalSource("S",m,new[]{Occurrence(0,0),Occurrence(0,1)},local:false);
            using(var c=new AssemblyBatchCoordinator())
            {
                var r=c.RunAsync(AssemblyRequest(new[]{source}),new AssemblyBatchOptions(workers:2)).Result;
                Check(r.AnalysisJobs==2&&r.Parts.All(p=>p.Quantity==1)&&r.GeometryMisses==1&&r.GeometryHits==1,"Rigid Assembly transforms are not machine-pose evidence");
                Check(r.StaticMisses==2&&r.ProcessMisses==2,"No result sharing without program invariant");
            }
        });
        Test("assembly: unresolved subassembly is not an empty completed inventory",()=> {
            var branch=new AssemblyOccurrence("SUB",null,"Subassembly","SUBSOURCE","Test",Occurrence(0,0).Transform,
                AssemblyOccurrenceKind.Lightweight,false,reason:"LIGHTWEIGHT_SUBASSEMBLY_SCOPE_UNRESOLVED",assemblyBranch:true);
            var request=new AssemblyBatchRequest("ROOT","Default",AssemblyCheckScope.EntireAssembly,new PlacementLease(),
                new ToolProfileLibrarySnapshot {Tools=new CompanyToolProfile[0]},new AssemblySourceInput[0],new[]{branch});
            using(var c=new AssemblyBatchCoordinator())
            {var r=c.RunAsync(request).Result;Check(r.Current&&!r.Complete&&!r.InventoryComplete&&r.UnresolvedBranches==1&&r.LeafOccurrences==0,"Missing descendants cannot become a complete empty success");}
        });
        Test("assembly: malformed configuration source and nonrigid transform never reuse a Part",()=> {
            var m=CollisionModel(Rect("BODY",0,0,1,1));
            var a=LocalSource("S",m,new[]{Occurrence(0,0,config:"OTHER")});
            var b=LocalSource("S",m,new[]{Occurrence(0,0),Occurrence(1,1)});
            var transform=Occurrence(0,0).Transform.ToArray();transform[0]=-1;
            var reflected=new AssemblyOccurrence("REFLECTED",null,"Part-1","SOURCE0",m.Configuration,transform,AssemblyOccurrenceKind.SheetMetal,true);
            Check(!reflected.RigidInstanceTransform,"A shareable boolean cannot certify a reflection");
            var c=LocalSource("S",m,new[]{reflected});
            Check(a.Reason=="MISMATCHED_REFERENCED_CONFIGURATION"&&b.Reason=="MIXED_SOURCE_IDENTITIES"&&!c.ProgramUse.CanShare(c,AssemblyCacheKeys.Program(c)),"All identity constraints checked");
            using(var worker=new AssemblyBatchCoordinator())foreach(var s in new[]{a,b,c})
            {var r=worker.RunAsync(AssemblyRequest(new[]{s})).Result;Check(r.Parts.All(p=>p.GeometricStatus==CollisionStatus.UNKNOWN)&&r.GeometryMisses==0,"Malformed source does no numeric work");}
        });
        Test("assembly: mirrored cut flexible virtual and unsupported occurrences never inherit certification",()=> {
            foreach(var o in new[]{Occurrence(0,0,mirror:true),Occurrence(0,0,modified:true),Occurrence(0,0,flexible:true),
                Occurrence(0,0,virtualPart:true),Occurrence(0,0,kind:AssemblyOccurrenceKind.Lightweight),Occurrence(0,0,kind:AssemblyOccurrenceKind.NonSheetMetal)})
            {
                var m=CollisionModel(Rect("BODY",0,0,1,1));var source=LocalSource("S",m,new[]{o});
                Check(!source.ProgramUse.CanShare(source,AssemblyCacheKeys.Program(source)),"Unproven equivalence cannot share a result");
                using(var c=new AssemblyBatchCoordinator())
                {
                    var r=c.RunAsync(AssemblyRequest(new[]{source})).Result;
                    Check(r.Parts.Count==1&&r.Parts[0].StaticCandidates.Count==18&&r.Parts[0].ManufacturingStatus=="CHUA_XAC_MINH","Unsupported retains inventory");
                    if(!o.Virtual)Check(r.GeometryMisses==0&&r.Parts[0].GeometricStatus==CollisionStatus.UNKNOWN,"Unverified body does not read/compile source as instance");
                }
            }
        });
        Test("assembly: real static and motion computation matches independent per-instance baseline",()=> {
            using(var f=Seating())
            {
                var q=WorkerRequest(f,1,P(1,2));var plans=AssemblyPlans(q.Model);var os=new[]{Occurrence(0,0),Occurrence(0,1)};
                var source=LocalSource("S",q.Model,os,q.Envelopes,q.Setups,plans);
                var sr=CollisionBatchWorker.RunSynchronously(q,new CollisionBatchOptions(1,120000,1000000));
                var pr=ProcessBatchWorker.RunSynchronously(new ProcessBatchRequest(q.Model,q.Library,plans,q.Envelopes),new ProcessBatchOptions(1,120000,1000000));
                var baseline=os.Select(o=>new AssemblyPartResult("S",new[]{o},null,sr.Candidates,pr.Candidates,1,true,()=>null)).ToArray();
                using(var c=new AssemblyBatchCoordinator())
                {
                    var request=AssemblyRequest(new[]{source},q.Library);var cold=c.RunAsync(request,new AssemblyBatchOptions(2)).Result;
                    Check(cold.Current&&cold.KernelQueries>0&&cold.NumericKernelMilliseconds>0,"Actual Phase3C/4A math executed");
                    Check(ResultFingerprint(baseline)==ResultFingerprint(cold.Parts),"Every status, reason and numeric witness matches independent engines");
                    Check(cold.Parts[0].StaticCandidates.Any(x=>x.CombinedResult==CollisionStatus.COLLISION)&&cold.Parts[0].ProcessCandidates.Any(x=>x.ProcessResult==CollisionStatus.COLLISION),"Real witnessed static + intermediate collisions");
                    string fingerprint=ResultFingerprint(cold.Parts);var warm=c.RunAsync(request,new AssemblyBatchOptions(4)).Result;
                    Check(warm.KernelQueries==0&&warm.StaticHits==1&&warm.ProcessHits==1&&fingerprint==ResultFingerprint(warm.Parts),"Warm proof retains numeric witnesses without recomputation");
                }
            }
        });
        Test("assembly: different reviewed setup pose and process plan have distinct result keys",()=> {
            using(var f=Seating())
            {
                var q=WorkerRequest(f,1,P(1,2));var os=new[]{Occurrence(0,0)};
                var a=LocalSource("S",q.Model,os,q.Envelopes,q.Setups,AssemblyPlans(q.Model));
                var changed=q.Envelopes.SelectMany(e=>new[]{AxialEvidence(f,e,q.Library,CollisionPairKind.WorkpiecePunch,P(21,22)),AxialEvidence(f,e,q.Library,CollisionPairKind.WorkpieceHolder,P(21,22))});
                var b=LocalSource("S",q.Model,os,q.Envelopes,changed,AssemblyPlans(q.Model,shift:50));
                Check(AssemblyCacheKeys.Geometry(a)==AssemblyCacheKeys.Geometry(b)&&AssemblyCacheKeys.Program(a)!=AssemblyCacheKeys.Program(b),"Geometry invariant, two physical program conditions");
                using(var c=new AssemblyBatchCoordinator())
                {
                    var first=c.RunAsync(AssemblyRequest(new[]{a},q.Library)).Result;string old=ResultFingerprint(first.Parts);
                    var next=c.RunAsync(AssemblyRequest(new[]{b},q.Library)).Result;
                    Check(next.GeometryHits==1&&next.StaticMisses==1&&next.ProcessMisses==1&&next.KernelQueries>0,"Changed pose reruns both solvers");
                    Check(old!=ResultFingerprint(next.Parts),"Actual outcome changes with the declared pose");
                }
            }
        });
        Test("assembly: indexed duplicate setup and process plans keep UNKNOWN semantics",()=> {
            using(var f=Seating())
            {
                var q=WorkerRequest(f,1);var plan=AssemblyPlans(q.Model).First();var setups=q.Setups.Concat(new[]{q.Setups.First()});
                var sr=new CollisionBatchRequest(q.Model,q.Library,q.Envelopes,setups);var pr=new ProcessBatchRequest(q.Model,q.Library,new[]{plan,plan});
                Check(sr.FindSetups(plan.ToolId,plan.Binding.FeatureId,CollisionPairKind.WorkpiecePunch).Count==2&&pr.FindPlans(plan.ToolId,plan.Binding.FeatureId).Count==2,"Duplicates preserved in immutable indexes");
                var r=CollisionBatchWorker.RunSynchronously(sr,new CollisionBatchOptions(1));Check(r.Candidates.First().PunchResult.Reasons.Contains("DUPLICATE_AXIAL_SETUP"),"No arbitrary first winner");
                var p=ProcessBatchWorker.RunSynchronously(pr,new ProcessBatchOptions(1));Check(p.Candidates.First().PairSegments.All(x=>x.Reasons.Contains("DUPLICATE_PROCESS_PLAN")),"No duplicate plan accepted");
            }
        });
        Test("assembly: equal setup coordinates without equal verification cannot hit certified result",()=> {
            using(var f=Seating())
            {
                var q=WorkerRequest(f,1,P(1,2));var sheet=q.Envelopes.Single();
                var unreviewed=Enum.GetValues(typeof(CollisionPairKind)).Cast<CollisionPairKind>().Select(pair=>
                    ToolAxialSetupEvidence.Read(AxialRecord(f,sheet,pair,P(1,2)),sheet,q.Library,f.Reviews.Verifier,f.Reviews.Root,CancellationToken.None)).ToArray();
                var a=LocalSource("S",q.Model,new[]{Occurrence(0,0)},q.Envelopes,q.Setups);
                var b=LocalSource("S",q.Model,new[]{Occurrence(0,0)},q.Envelopes,unreviewed);
                Check(AssemblyCacheKeys.Program(a)==AssemblyCacheKeys.Program(b)&&AssemblyCacheKeys.EvidenceBindings(a)!=AssemblyCacheKeys.EvidenceBindings(b),"Payload equality never substitutes reviewer/object identity");
                using(var c=new AssemblyBatchCoordinator())
                {
                    var good=c.RunAsync(AssemblyRequest(new[]{a},q.Library)).Result;Check(good.Parts[0].StaticCandidates.First().CombinedResult==CollisionStatus.COLLISION,"Verified source witnesses overlap");
                    var bad=c.RunAsync(AssemblyRequest(new[]{b},q.Library)).Result;
                    Check(bad.StaticMisses==1&&bad.StaticHits==0&&bad.Parts[0].StaticCandidates.First().CombinedResult==CollisionStatus.UNKNOWN,"Unreviewed setup cannot inherit old geometric certification");
                }
            }
        });
        Test("assembly: different observed slice bindings cannot reuse a whole-volume hash",()=> {
            using(var f=Seating())
            {
                var q=WorkerRequest(f,1,P(1,2));var volume=q.Envelopes.Single();var sources=new List<AssemblySourceInput>();
                foreach(double station in new[]{-5.0,0.0})
                {
                    var observed=volume.ObservationsOnly(new[]{station});var setups=Enum.GetValues(typeof(CollisionPairKind)).Cast<CollisionPairKind>().Select(pair=>AxialEvidence(f,observed,q.Library,pair,P(1,2))).ToArray();
                    sources.Add(LocalSource("S",q.Model,new[]{Occurrence(0,0)},new[]{observed},setups));
                }
                Check(sources[0].Envelopes[0].GeometrySha256==sources[1].Envelopes[0].GeometrySha256,"Regression fixture exercises a real observed-volume hash alias");
                Check(AssemblyCacheKeys.Geometry(sources[0])!=AssemblyCacheKeys.Geometry(sources[1]),"Exact observed geometry and object binding included");
                using(var c=new AssemblyBatchCoordinator())
                {
                    var cap=c.RunAsync(AssemblyRequest(new[]{sources[0]},q.Library)).Result;Check(cap.Parts[0].StaticCandidates.First().CombinedResult==CollisionStatus.UNKNOWN,"Outside finite tool is not a global CLEAR proof");
                    var middle=c.RunAsync(AssemblyRequest(new[]{sources[1]},q.Library)).Result;
                    Check(middle.GeometryMisses==1&&middle.StaticMisses==1&&middle.Parts[0].StaticCandidates.First().CombinedResult==CollisionStatus.COLLISION,"Middle source slice gives its own real witness");
                }
            }
        });
        Test("assembly: cancellation source/tool expiry and global budgets preserve all known slots",()=> {
            foreach(string mode in new[]{"cancel","model","tool","assembly","budget","restart"})
            {
                var model=CollisionModel(Rect("BODY",0,0,1,1),3);var source=LocalSource("S",model,new[]{Occurrence(0,0)},plans:AssemblyPlans(model,slow:true));
                var lease=new PlacementLease();var lib=new ToolProfileLibrarySnapshot {Tools=new CompanyToolProfile[0]};var request=AssemblyRequest(new[]{source},lib,lease);
                using(var c=new AssemblyBatchCoordinator())using(var cancel=new CancellationTokenSource())
                {
                    if(mode=="cancel")cancel.Cancel();if(mode=="model")model.Lease.Invalidate();if(mode=="tool")lib.Invalidate();if(mode=="assembly")lease.Invalidate();
                    var task=c.RunAsync(request,new AssemblyBatchOptions(4,kernelBudget:mode=="budget"?1:1000000),cancel.Token);
                    if(mode=="restart")c.Cancel();var r=task.Result;
                    Check(!r.Current&&r.Parts.Count==1&&r.Parts[0].ProcessCandidates.Count==54&&r.Parts[0].StaticCandidates.Count==54,"Canceled/stale/budget rows complete but not current: "+mode);
                    Check(r.Parts.All(p=>p.GeometricStatus==CollisionStatus.UNKNOWN&&p.ProcessCandidates.All(x=>x.ProcessResult==CollisionStatus.UNKNOWN)),"No stale witnessed result remains usable: "+mode);
                }
            }
        });
        Test("assembly: held result invalidates immediately with source lease",()=> {
            var model=CollisionModel(Rect("BODY",0,0,1,1));var source=LocalSource("S",model,new[]{Occurrence(0,0)},plans:AssemblyPlans(model));
            using(var c=new AssemblyBatchCoordinator())
            {var r=c.RunAsync(AssemblyRequest(new[]{source})).Result;Check(r.Parts[0].GeometricStatus==CollisionStatus.COLLISION,"Witness first");model.Lease.Invalidate();Check(!r.Current&&r.Parts[0].ProcessCandidates.All(p=>p.ProcessResult==CollisionStatus.UNKNOWN),"Held rows dynamically expire");}
        });
        Test("assembly: serial/two/four worker results and bounded tiny caches stay deterministic",()=> {
            var model=CollisionModel(Rect("BODY",0,0,1,1));var sources=Enumerable.Range(0,4).Select(i=>LocalSource("S"+i,model,new[]{Occurrence(i,0)},plans:AssemblyPlans(model))).ToArray();
            string previous=null;
            foreach(int n in new[]{1,2,4})using(var c=new AssemblyBatchCoordinator(2,2,32768))
            {
                var r=c.RunAsync(AssemblyRequest(sources),new AssemblyBatchOptions(n)).Result;string actual=ResultFingerprint(r.Parts);
                Check(r.Current&&r.PeakWorkers<=n&&r.CacheBytes<=32768+1048576,"Single shared worker budget and bounded cache memory");
                Check(previous==null||actual==previous,"Deterministic stable results");previous=actual;
            }
        });
        Test("assembly: invalid policy and listener failure cannot fake success",()=> {
            var source=LocalSource("S",CollisionModel(Rect("BODY",0,0,1,1)),new[]{Occurrence(0,0)});
            using(var c=new AssemblyBatchCoordinator())
            {
                var bad=c.RunAsync(AssemblyRequest(new[]{source}),new AssemblyBatchOptions(5)).Result;Check(!bad.Current&&bad.InvalidReason=="INVALID_ASSEMBLY_BATCH_POLICY","Bad global budget rejected");
                var okay=c.RunAsync(AssemblyRequest(new[]{source}),progress:new AssemblyProgress(_=>throw new Exception())).Result;
                Check(okay.Current&&okay.ProgressDiagnostic.Contains("PROGRESS_CALLBACK_FAILED"),"Progress listener isolated from solver");
            }
        });
    }
    private static int AssemblyOnly(string[] args)
    {
        temporary=Path.Combine(Path.GetTempPath(),"ADDIN-AssemblyTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
        AssemblyBatchTests();Console.WriteLine("RESULT "+passed+" passed; "+failed+" failed; "+assertions+" assertions");return failed==0?0:1;
    }
}
