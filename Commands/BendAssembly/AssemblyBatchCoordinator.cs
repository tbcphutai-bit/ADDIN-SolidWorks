using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendPlanning;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendAssembly
{
    internal sealed class AssemblyBatchOptions
    {
        public int Workers { get; }
        public int Milliseconds { get; }
        public int KernelBudget { get; }
        public bool UseCache { get; }
        public bool Deduplicate { get; }
        public AxialCollisionPolicy StaticPolicy { get; }
        public ProcessCollisionPolicy ProcessPolicy { get; }
        internal Action<ProcessPerformanceSample> ProcessPerformance { get; private set; }
        internal AssemblyBatchOptions(int workers=2,int milliseconds=300000,int kernelBudget=1000000,bool cache=true,bool deduplicate=true,
            AxialCollisionPolicy staticPolicy=null,ProcessCollisionPolicy processPolicy=null)
        {Workers=workers;Milliseconds=milliseconds;KernelBudget=kernelBudget;UseCache=cache;Deduplicate=deduplicate;
            StaticPolicy=staticPolicy??new AxialCollisionPolicy();ProcessPolicy=processPolicy??new ProcessCollisionPolicy();}
        internal AssemblyBatchOptions WithProcessPerformance(Action<ProcessPerformanceSample> sink) => new AssemblyBatchOptions(
            Workers,Milliseconds,KernelBudget,UseCache,Deduplicate,StaticPolicy,ProcessPolicy) { ProcessPerformance=sink };
        internal bool Valid => Workers>=1&&Workers<=4&&Milliseconds>0&&Milliseconds<=1800000&&KernelBudget>0&&KernelBudget<=10000000&&StaticPolicy.Valid&&ProcessPolicy.Valid;
    }
    internal sealed class AssemblyBatchProgress
    {
        public int CompletedParts { get; }
        public int TotalParts { get; }
        public int CompletedBends { get; }
        public int Candidates { get; }
        public int WorkerThreadId { get; }
        public AssemblyPartResult ProvisionalPart { get; }
        public string PlanningToolId { get; }
        public int PlanningNodes { get; }
        public int PlanningQueries { get; }
        internal AssemblyBatchProgress(int done,int total,int bends,int candidates,AssemblyPartResult part)
            : this(done,total,bends,candidates,part,null) { }
        internal AssemblyBatchProgress(int done,int total,int bends,int candidates,AssemblyPartResult part,SequencePlanningProgress planning)
        {CompletedParts=done;TotalParts=total;CompletedBends=bends;Candidates=candidates;ProvisionalPart=part;WorkerThreadId=Thread.CurrentThread.ManagedThreadId;
            PlanningToolId=planning?.ToolId;PlanningNodes=planning?.VisitedNodes??0;PlanningQueries=planning?.KernelQueries??0;}
    }
    internal sealed class AssemblyBatchSnapshot
    {
        private readonly AssemblyBatchRequest request;
        private readonly Func<string> invalid;
        private readonly AssemblyResultReadGuard readGuard;
        internal CollisionBatchRunState State { get; }
        public bool Current => invalid()==null;
        public bool InventoryComplete => !Occurrences.Any(o=>o.IsAssemblyBranch&&o.Kind!=AssemblyOccurrenceKind.Suppressed&&
            (o.Kind!=AssemblyOccurrenceKind.Subassembly||o.Reason=="SUBASSEMBLY_CHILD_INVENTORY_UNVERIFIED"));
        public bool Complete => Current&&InventoryComplete;
        public string InvalidReason => invalid();
        public string AssemblySnapshotId => request.AssemblySnapshotId;
        public string Configuration => request.Configuration;
        public AssemblyCheckScope Scope => request.Scope;
        public IReadOnlyList<AssemblyPartResult> Parts { get; }
        public IReadOnlyList<AssemblyOccurrence> Occurrences => request.Occurrences;
        public int LeafOccurrences => Occurrences.Count(o=>!o.IsAssemblyBranch);
        public int UnresolvedBranches => Occurrences.Count(o=>o.IsAssemblyBranch&&o.Kind!=AssemblyOccurrenceKind.Subassembly&&o.Kind!=AssemblyOccurrenceKind.Suppressed||o.Reason=="SUBASSEMBLY_CHILD_INVENTORY_UNVERIFIED");
        public int EligibleOccurrences => Occurrences.Count(o=>o.Kind==AssemblyOccurrenceKind.SheetMetal);
        public int ComponentOccurrences => Occurrences.Count;
        public int SuppressedOccurrences => Occurrences.Count(o=>o.Kind==AssemblyOccurrenceKind.Suppressed);
        public int LightweightOccurrences => Occurrences.Count(o=>o.Kind==AssemblyOccurrenceKind.Lightweight);
        public int UnsupportedOccurrences => Occurrences.Count(o=>o.Kind==AssemblyOccurrenceKind.Unsupported||o.Kind==AssemblyOccurrenceKind.Unloaded);
        public IReadOnlyDictionary<string,int> ReasonCounts => Parts.Where(p=>p.Reason!=null).GroupBy(p=>p.Reason).ToDictionary(g=>g.Key,g=>g.Sum(p=>p.Quantity));
        public IReadOnlyDictionary<string,int> OccurrenceReasonCounts => Occurrences.Where(o=>o.Reason!=null).GroupBy(o=>o.Reason).ToDictionary(g=>g.Key,g=>g.Count());
        public int UniqueSourceConfigurations => request.Sources.Select(s=>s.SourceKey).Distinct().Count();
        public int AnalysisJobs => Parts.Count;
        public int Captures => request.Captures;
        public int CaptureHits => request.CaptureHits;
        public double InventoryMilliseconds => request.InventoryMilliseconds;
        public double CaptureMilliseconds => request.CaptureMilliseconds;
        public double WorkerMilliseconds { get; }
        public double EnvelopeMilliseconds { get; }
        public double StaticMilliseconds { get; }
        public double ProcessMilliseconds { get; }
        public double PlanningMilliseconds => Parts.Sum(p => p.Planning?.ElapsedMilliseconds ?? 0);
        public int PlanningCacheHits => Parts.Sum(p => p.Planning?.CacheHits ?? 0);
        public int PlanningCacheMisses => Parts.Sum(p => p.Planning?.CacheMisses ?? 0);
        public double NumericKernelMilliseconds { get; }
        public double PrecheckMilliseconds { get; }
        public int FastPrecheckOnlyJobs { get; }
        public int KernelQueries { get; }
        public int PeakWorkers { get; }
        public long GeometryHits { get; }
        public long GeometryMisses { get; }
        public long StaticHits { get; }
        public long StaticMisses { get; }
        public long ProcessHits { get; }
        public long ProcessMisses { get; }
        public long CatalogHits { get; }
        public long CatalogMisses { get; }
        public long CacheBytes { get; }
        public long PeakManagedBytes { get; }
        public string ProgressDiagnostic { get; }
        public int ProcessFullAudits { get; }
        public int ProcessSharedGuardChecks { get; }
        public double ProcessFreshnessAuditMilliseconds { get; }
        public string ManufacturingStatus => "CHUA_XAC_MINH";
        public string WinnerToolId => null;
        internal IReadOnlyList<ToolAssemblyProfile> ToolPairs => request.Library?.PlacementSources?.Tools??new ToolAssemblyProfile[0];
        // Read only inside the same audited AssemblyBatchReadSnapshot copy.
        internal ToolProfileLibrarySnapshot ToolCatalogForDisplay => request.Library;
        internal AssemblyBatchReadSnapshot ReadSnapshot()
        {
            var clock = Stopwatch.StartNew();
            double validationMs;
            var guard = readGuard ?? new AssemblyResultReadGuard(invalid, invalid);
            var read = guard.Copy(() => new AssemblyBatchReadSnapshot(this), out validationMs);
            if (read != null)
            {
                read.ValidationMilliseconds = validationMs;
                read.CopyMilliseconds = Math.Max(0, clock.Elapsed.TotalMilliseconds - validationMs);
            }
            return read;
        }
        internal AssemblyBatchSnapshot(AssemblyBatchRequest request,CollisionBatchRunState state,IEnumerable<AssemblyPartResult> parts,
            double workerMs,double envelopeMs,double staticMs,double processMs,double kernelMs,int queries,int peak,long[] deltas,long cacheBytes,long memory,string diagnostic,
            Func<string> invalid,double precheckMs,int fastJobs,int processAudits=0,int processSharedChecks=0,double processAuditMs=0,
            AssemblyResultReadGuard readGuard=null)
        {this.request=request;this.invalid=invalid;State=state;Parts=PlacementLists.Copy(parts);WorkerMilliseconds=workerMs;EnvelopeMilliseconds=envelopeMs;
            StaticMilliseconds=staticMs;ProcessMilliseconds=processMs;NumericKernelMilliseconds=kernelMs;KernelQueries=queries;PeakWorkers=peak;GeometryHits=deltas[0];GeometryMisses=deltas[1];
            StaticHits=deltas[2];StaticMisses=deltas[3];ProcessHits=deltas[4];ProcessMisses=deltas[5];CatalogHits=deltas[6];CatalogMisses=deltas[7];
            CacheBytes=cacheBytes;PeakManagedBytes=memory;ProgressDiagnostic=diagnostic;PrecheckMilliseconds=precheckMs;FastPrecheckOnlyJobs=fastJobs;
            ProcessFullAudits=processAudits;ProcessSharedGuardChecks=processSharedChecks;ProcessFreshnessAuditMilliseconds=processAuditMs;this.readGuard=readGuard;}
    }
    internal sealed class AssemblyBatchCoordinator : IDisposable
    {
        private sealed class PlannerProgress : IProgress<SequencePlanningProgress>
        { private readonly Action<SequencePlanningProgress> report; internal PlannerProgress(Action<SequencePlanningProgress> report){this.report=report;}
            public void Report(SequencePlanningProgress value)=>report(value); }
        private readonly AssemblyManagedCache<AssemblyGeometryBundle> geometry;
        private readonly AssemblyManagedCache<AssemblyStaticCertificate> statics;
        private readonly AssemblyManagedCache<AssemblyProcessCertificate> processes;
        private readonly AssemblyManagedCache<ToolProfileLibrarySnapshot> catalogs;
        private readonly object gate=new object();
        private readonly SemaphoreSlim workerSlots=new SemaphoreSlim(4,4);
        private readonly PartSequencePlanner planner = new PartSequencePlanner();
        private int totalActiveWorkers;
        private CancellationTokenSource active;
        private CollisionBatchRunState activeState;
        private AssemblyBatchSnapshot published;
        private int generation;
        private bool disposed;
        public AssemblyBatchSnapshot Published => Volatile.Read(ref published);
        internal AssemblyBatchCoordinator(int geometryEntries=128,int resultEntries=4096,long bytes=67108864)
        {geometry=new AssemblyManagedCache<AssemblyGeometryBundle>(geometryEntries,bytes/4);statics=new AssemblyManagedCache<AssemblyStaticCertificate>(resultEntries,bytes/4);
            processes=new AssemblyManagedCache<AssemblyProcessCertificate>(resultEntries,bytes/2);catalogs=new AssemblyManagedCache<ToolProfileLibrarySnapshot>(8,1048576);}
        internal Task<AssemblyBatchSnapshot> RunAsync(AssemblyBatchRequest request,AssemblyBatchOptions options=null,CancellationToken token=default(CancellationToken),
            IProgress<AssemblyBatchProgress> progress=null)
        {
            CancellationTokenSource cts;CollisionBatchRunState state;int run;
            lock(gate)
            {if(disposed)throw new ObjectDisposedException(nameof(AssemblyBatchCoordinator));activeState?.Stop("SUPERSEDED_ASSEMBLY_BATCH");active?.Cancel();
                published?.State.Stop("SUPERSEDED_ASSEMBLY_BATCH");active=cts=CancellationTokenSource.CreateLinkedTokenSource(token);activeState=state=new CollisionBatchRunState();run=++generation;}
            return Task.Run(()=> {
                try {var result=Execute(request,options??new AssemblyBatchOptions(),cts.Token,progress,state);
                    lock(gate)if(!disposed&&run==generation)Interlocked.Exchange(ref published,result);return result;}
                finally {lock(gate)if(ReferenceEquals(active,cts))active=null;cts.Dispose();}
            });
        }
        private AssemblyBatchSnapshot Execute(AssemblyBatchRequest request,AssemblyBatchOptions options,CancellationToken token,IProgress<AssemblyBatchProgress> progress,CollisionBatchRunState state)
        {
            if(request==null)throw new ArgumentNullException(nameof(request));var timer=Stopwatch.StartNew();int executing=1,calls=0,running=0,peak=0,next=-1,done=0,bends=0,candidates=0;
            double envelopeMs=0,staticMs=0,processMs=0,kernelMs=0,precheckMs=0;int fastJobs=0;long memory=GC.GetTotalMemory(false);string diagnostic=null;var progressGate=new object();
            int processAudits=0,processSharedChecks=0;double processAuditMs=0;
            long[] start={geometry.Hits,geometry.Misses,statics.Hits,statics.Misses,processes.Hits,processes.Misses,catalogs.Hits,catalogs.Misses};
            Func<string> invalid=()=> {
                using(ProcessPerformanceScope.Measure("Freshness.AssemblyShared"))
                {
                // Kernel callbacks inspect cheap shared leases. The existing
                // per-Part workers/queries still validate their own complete
                // plan/setup. Scanning every other Part's every segment at each
                // scalar kernel operation is quadratic Assembly work.
                if(!request.Lease.Current)state.Stop("STALE_ASSEMBLY_OR_SCOPE");
                if(request.Library?.Current!=true||request.Library.PlacementSources?.Current==false)state.Stop("STALE_TOOL_CATALOG_OR_REVIEW");
                if(request.Sources.Any(s=>s.Model?.Lease.Current==false||!s.ProgramUse.Current))state.Stop("STALE_GEOMETRY_SETUP_OR_PROCESS");
                if(token.IsCancellationRequested)state.Stop("CANCELLED");
                if(Volatile.Read(ref executing)!=0&&timer.ElapsedMilliseconds>=options.Milliseconds)state.Stop("ASSEMBLY_TIME_BUDGET_EXCEEDED");
                return state.Reason;
                }
            };
            Func<string> fullInvalid=()=> {
                if(request.InvalidReason!=null)state.Stop(request.InvalidReason);
                if(!AssemblyCacheKeys.CatalogCurrent(request.Library))state.Stop("STALE_TOOL_GEOMETRY_OR_REVIEW");
                return invalid();
            };
            var readGuard = new AssemblyResultReadGuard(fullInvalid, invalid);
            Action consume=()=> {if(Interlocked.Increment(ref calls)>options.KernelBudget)state.Stop("ASSEMBLY_KERNEL_BUDGET_EXCEEDED");
                var reason=invalid();if(reason!=null)throw new CollisionAbort(reason);};
            if(!options.Valid)state.Stop("INVALID_ASSEMBLY_BATCH_POLICY");
            var jobs=new List<Tuple<AssemblySourceInput,AssemblyOccurrence[]>>();
            // Native capture already partitions Min groups, but the managed API
            // is callable independently. Enforce the same admission boundary
            // here so a caller cannot bypass it with a mixed/unverified source.
            foreach(var source in request.Sources.SelectMany(s=>AssemblyOccurrenceEligibility.Partition(s.SourceKey,s.Model,
                s.Occurrences,s.Envelopes,s.Setups,s.Plans,s.ProgramUse,s.Reason,s.ConditionsRevision,s.Sequence,s.BendMetadata)))
            {
                bool share=options.Deduplicate&&source.ProgramUse.CanShare(source,AssemblyCacheKeys.Program(source));
                if(share)jobs.Add(Tuple.Create(source,source.Occurrences.ToArray()));
                else foreach(var occurrence in source.Occurrences)jobs.Add(Tuple.Create(source,new[]{occurrence}));
            }
            var output=new AssemblyPartResult[jobs.Count];string catalogKey=AssemblyCacheKeys.Catalog(request.Library),policyKey=AssemblyCacheKeys.Policy(options);
            bool catalogHit;
            catalogs.Get(catalogKey,()=>request.Library,AssemblyCacheKeys.CatalogCurrent,_=>1024,CancellationToken.None,out catalogHit);
            Action work=()=> {
                while(true)
                {
                    int index=Interlocked.Increment(ref next);if(index>=jobs.Count)return;
                    var source=jobs[index].Item1;var occurrences=jobs[index].Item2;
                    workerSlots.Wait();
                    Interlocked.Increment(ref running);
                    int count=Interlocked.Increment(ref totalActiveWorkers),old;do{old=Volatile.Read(ref peak);if(old>=count)break;}while(Interlocked.CompareExchange(ref peak,count,old)!=old);
                    string reason=source.Reason;
                    try
                    {
                        var precheck=Stopwatch.StartNew();string fail=fullInvalid();
                        bool needsGeometry=source.Setups.Count>0||source.Plans.Count>0||source.Envelopes.Count>0;
                        lock(progressGate) {precheckMs+=precheck.Elapsed.TotalMilliseconds;if(!needsGeometry)fastJobs++;}
                        if(source.Model==null||reason!=null||fail!=null||occurrences.Any(o=>o.Modified||o.Mirrored||o.Flexible||!o.RigidInstanceTransform||o.Kind!=AssemblyOccurrenceKind.SheetMetal))
                        {reason=reason??fail??"UNVERIFIED_INSTANCE_BODY_OR_CONTEXT";output[index]=Unknown(source,occurrences,reason,readGuard.Check);}
                        else
                        {
                            string geoKey=AssemblyCacheKeys.Geometry(source);
                            Func<AssemblyGeometryBundle> compile=()=> {
                                var clock=Stopwatch.StartNew();var sheets=new List<SheetEnvelopeCapture>();
                                foreach(var bend in source.Model.Bends)
                                {
                                    token.ThrowIfCancellationRequested();var supplied=source.Envelopes.Where(e=>e.Binding.FeatureId==bend.FeatureId).ToArray();
                                    if(supplied.Length>1||supplied.Any(e=>!ReferenceEquals(e.Binding.Model,source.Model)))throw new InvalidOperationException("MISMATCHED_OR_DUPLICATE_ENVELOPE");
                                    if(supplied.Length==1)sheets.Add(supplied[0]);
                                    else if(needsGeometry)sheets.Add(PrismaticEnvelopeCompiler.Read(source.Model,bend,token));
                                    else sheets.Add(new SheetEnvelopeCapture(new EnvelopeBinding(source.Model,bend,ToolEvidenceCanonical.Hash(geoKey,bend.FeatureId.ToString())),
                                        new AxialSolidPrimitive[0],EnvelopeCoverageTier.UnknownCoverage,"FastPrecheckNoVerifiedSetup",source.Model.SimulationOnly,1e-9,new[]{"NO_NUMERIC_QUERY_WITHOUT_SETUP"}));
                                }
                                lock(progressGate)envelopeMs+=clock.Elapsed.TotalMilliseconds;
                                return new AssemblyGeometryBundle(source,sheets);
                            };
                            bool hit;var bundle=options.UseCache?geometry.Get(geoKey+(needsGeometry?"/FULL":"/PRECHECK"),compile,b=>b.Current,b=>b.Bytes,token,out hit):compile();
                            string condition=source.ProgramUse.CanShare(source,AssemblyCacheKeys.Program(source))?source.ProgramUse.PayloadSha256:
                                ToolEvidenceCanonical.Hash(occurrences[0].Canonical,"INSTANCE_SPECIFIC_UNVERIFIED_PROGRAM");
                            string resultKey=ToolEvidenceCanonical.Hash(geoKey,catalogKey,policyKey,AssemblyCacheKeys.Program(source),AssemblyCacheKeys.EvidenceBindings(source),condition,source.ProgramUse.SimulationOnly.ToString());
                            var excel=source.Model.Bends.SelectMany(b=>CompanyToolIds.All.Select(id=>new {
                                Key=BendExcelApplicabilityAdapter.Key(id,b.FeatureId),
                                Value=BendExcelApplicabilityAdapter.Evaluate(source,bundle.Envelopes,request.Library,b.FeatureId,id)
                            })).ToDictionary(v=>v.Key,v=>v.Value,StringComparer.Ordinal);
                            // Explicit controlled simulations may exercise the numeric
                            // engines without Excel; their simulation flag is retained.
                            bool referenceSimulation=source.Model.SimulationOnly||source.ProgramUse.SimulationOnly;
                            Func<string,int,string> staticAdmission=(id,f)=>referenceSimulation?null:excel[BendExcelApplicabilityAdapter.Key(id,f)].AdmissionReason(false);
                            Func<string,int,string> processAdmission=(id,f)=>referenceSimulation?null:excel[BendExcelApplicabilityAdapter.Key(id,f)].AdmissionReason(true);
                            resultKey=ToolEvidenceCanonical.Hash("ExcelAdmissionV1",resultKey,
                                ToolEvidenceCanonical.Hash(excel.OrderBy(e=>e.Key,StringComparer.Ordinal).Select(e=>e.Key+"/"+e.Value.Applicability+"/"+e.Value.Evidence).ToArray()));
                            var sr=new CollisionBatchRequest(source.Model,request.Library,bundle.Envelopes,source.Setups) {CadAdmission=staticAdmission};
                            var pr=new ProcessBatchRequest(source.Model,request.Library,source.Plans,bundle.Envelopes) {CadAdmission=processAdmission};
                            Func<AssemblyStaticCertificate> checkStatic=()=> {
                                CollisionBatchSnapshot result;
                                using(new CollisionTimingScope(ms=>{lock(progressGate)kernelMs+=ms;}))
                                    result=CollisionBatchWorker.RunSynchronously(sr,new CollisionBatchOptions(1,120000,Math.Min(1000000,options.KernelBudget),options.StaticPolicy).WithAssemblyGuard(invalid,consume),token);
                                lock(progressGate)staticMs+=result.ElapsedMilliseconds;
                                return new AssemblyStaticCertificate(result.Candidates);
                            };
                            Func<AssemblyProcessCertificate> checkProcess=()=> {
                                ProcessBatchSnapshot result;
                                using(new CollisionTimingScope(ms=>{lock(progressGate)kernelMs+=ms;}))
                                    result=ProcessBatchWorker.RunSynchronously(pr,new ProcessBatchOptions(1,120000,Math.Min(1000000,options.KernelBudget),options.ProcessPolicy)
                                        .WithAssemblyGuard(invalid,consume).WithPerformance(options.ProcessPerformance),token);
                                lock(progressGate){processMs+=result.WorkerMilliseconds;processAudits+=result.FullAudits;
                                    processSharedChecks+=result.SharedGuardChecks;processAuditMs+=result.FreshnessAuditMilliseconds;}
                                return new AssemblyProcessCertificate(result.Candidates);
                            };
                            var sc=options.UseCache?statics.Get(resultKey,checkStatic,c=>source.Current&&AssemblyCacheKeys.CatalogCurrent(request.Library)&&c.Cacheable,c=>c.Bytes,token,out hit):checkStatic();
                            var pc=options.UseCache?processes.Get(resultKey,checkProcess,c=>source.Current&&AssemblyCacheKeys.CatalogCurrent(request.Library)&&c.Cacheable,c=>c.Bytes,token,out hit):checkProcess();
                            var restoredStatic=sc.Restore(sr,bundle.Envelopes,readGuard.Check);var restoredProcess=pc.Restore(pr,readGuard.Check);
                            if(source.ProgramUse.SimulationOnly)
                            {
                                restoredStatic=PlacementLists.Copy(restoredStatic.Select(r=>new VolumeCandidateCollisionResult(r.ToolId,r.FeatureId,r.PunchResult,r.HolderResult,true)));
                                restoredProcess=PlacementLists.Copy(restoredProcess.Select(r=>new ProcessCandidateResult(r.ToolId,r.FeatureId,r.PairSegments,true)));
                            }
                            PartPlanningResult planning;
                            using(new CollisionTimingScope(ms=>{lock(progressGate)kernelMs+=ms;}))
                                planning = planner.Run(ToolEvidenceCanonical.Hash(source.SourceKey, condition, source.ConditionsRevision),
                                    source.Model, request.Library, source.Sequence, token: token, externalInvalid: readGuard.Check,
                                    sharedInvalid: invalid, externalConsume: consume, cadAdmission: processAdmission, progress: progress==null?null:new PlannerProgress(p=>{
                                        lock(progressGate) progress.Report(new AssemblyBatchProgress(done,jobs.Count,bends,candidates,null,p)); }));
                            output[index]=new AssemblyPartResult(source.SourceKey,occurrences,null,restoredStatic,restoredProcess,source.Model.Bends.Count,
                                source.Model.SimulationOnly||source.ProgramUse.SimulationOnly,readGuard.Check,planning,source.BendMetadata,bundle.Envelopes) {
                                    ReadinessDiagnostics=source.ReadinessDiagnostics,NativeInventoryKnown=InventoryKnown(source),
                                    NativeFeatureIds=PlacementLists.Copy(source.Model.Bends.Select(b=>b.FeatureId)),
                                    ExcelResults=new System.Collections.ObjectModel.ReadOnlyDictionary<string,BendExcelCaseResult>(excel),
                                    DisplaySetups=source.Setups};
                        }
                    }
                    catch(OperationCanceledException){state.Stop("CANCELLED");output[index]=Unknown(source,occurrences,"CANCELLED",readGuard.Check);}
                    catch(Exception ex){reason="ASSEMBLY_PART_FAILURE:"+ex.GetType().Name;state.Stop(reason);output[index]=Unknown(source,occurrences,reason,readGuard.Check);}
                    finally{Interlocked.Decrement(ref running);Interlocked.Decrement(ref totalActiveWorkers);workerSlots.Release();}
                    lock(progressGate)
                    {
                        done++;bends+=output[index].BendCount;candidates+=output[index].StaticCandidates.Count;memory=Math.Max(memory,GC.GetTotalMemory(false));
                        try{progress?.Report(new AssemblyBatchProgress(done,jobs.Count,bends,candidates,output[index]));}catch(Exception ex){diagnostic="PROGRESS_CALLBACK_FAILED:"+ex.GetType().Name;}
                    }
                }
            };
            using(token.Register(()=>state.Stop("CANCELLED")))
            {if(options.Workers==1||!options.Valid)work();else Task.WaitAll(Enumerable.Range(0,options.Workers).Select(_=>Task.Run(work)).ToArray());fullInvalid();}
            Volatile.Write(ref executing,0);timer.Stop();
            long[] finish={geometry.Hits,geometry.Misses,statics.Hits,statics.Misses,processes.Hits,processes.Misses,catalogs.Hits,catalogs.Misses};
            return new AssemblyBatchSnapshot(request,state,output,timer.Elapsed.TotalMilliseconds,envelopeMs,staticMs,processMs,kernelMs,calls,peak,
                finish.Zip(start,(a,b)=>a-b).ToArray(),geometry.Bytes+statics.Bytes+processes.Bytes+catalogs.Bytes+planner.CacheBytes,memory,diagnostic,fullInvalid,precheckMs,fastJobs,
                processAudits,processSharedChecks,processAuditMs,readGuard);
        }
        private static AssemblyPartResult Unknown(AssemblySourceInput source,AssemblyOccurrence[] occurrences,string reason,Func<string> invalid)
        {
            var statics=new List<VolumeCandidateCollisionResult>();var processes=new List<ProcessCandidateResult>();
            if(source.Model!=null)foreach(var bend in source.Model.Bends)foreach(string id in CompanyToolIds.All)
            {
                Func<CollisionPairKind,AxialPairResult> pair=p=>new AxialPairResult(new AxialCollisionQuery(null,null,p,invalid:invalid),CollisionStatus.UNKNOWN,new[]{reason},0,0,0);
                statics.Add(new VolumeCandidateCollisionResult(id,bend.FeatureId,pair(CollisionPairKind.WorkpiecePunch),pair(CollisionPairKind.WorkpieceHolder),source.Model.SimulationOnly));
                processes.Add(new ProcessCandidateResult(id,bend.FeatureId,Enum.GetValues(typeof(BendProcessPhase)).Cast<BendProcessPhase>().SelectMany(phase=>
                    Enum.GetValues(typeof(CollisionPairKind)).Cast<CollisionPairKind>().Select(p=>new ProcessPairResult(p,"UNKNOWN:"+phase,phase,CollisionStatus.UNKNOWN,new[]{reason},invalid,null,0,0,0,0,0))),source.Model.SimulationOnly));
            }
            return new AssemblyPartResult(source.SourceKey,occurrences,reason,statics,processes,source.Model?.Bends.Count??0,source.Model?.SimulationOnly==true||source.ProgramUse.SimulationOnly,invalid,null,source.BendMetadata) {ReadinessDiagnostics=source.ReadinessDiagnostics,
                    NativeInventoryKnown=InventoryKnown(source),NativeFeatureIds=PlacementLists.Copy(source.Model?.Bends.Select(b=>b.FeatureId))};
        }
        private static bool InventoryKnown(AssemblySourceInput source) => source.Model!=null&&
            (source.Model.Bends.Count>0||source.Model.CompleteBodyCoverage)&&
            source.Model.Bends.All(b=>b.FeatureId>0)&&source.Model.Bends.Select(b=>b.FeatureId).Distinct().Count()==source.Model.Bends.Count&&
            source.BendMetadata.All(m=>source.Model.Bends.Any(b=>b.FeatureId==m.FeatureId));
        internal void Cancel()
        {lock(gate){activeState?.Stop("CANCELLED");published?.State.Stop("CANCELLED");active?.Cancel();statics.Clear();processes.Clear();}}
        public void Dispose()
        {lock(gate){if(disposed)return;disposed=true;generation++;activeState?.Stop("ASSEMBLY_COORDINATOR_DISPOSED");published?.State.Stop("ASSEMBLY_COORDINATOR_DISPOSED");active?.Cancel();
            geometry.Clear();statics.Clear();processes.Clear();catalogs.Clear();planner.Dispose();}}
    }
}
