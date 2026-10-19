using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ADDIN.Commands;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;
using ADDIN.UI;

internal static partial class ToolProfileTests
{
    internal static int UiReadBenchmark(string output)
    {
        Directory.CreateDirectory(output);temporary=Path.Combine(output,"controlled-fixtures");Directory.CreateDirectory(temporary);
        var fixtures=new List<SeatingFixture>();var metrics=new List<object>();
        try
        {
            for(int i=0;i<18;i++)fixtures.Add(Seating(toolIndex:i));
            var library=new ToolProfileLibrarySnapshot {Tools=CompanyToolIds.All.Select(id=>new CompanyToolProfile {Id=id,Parts=new ToolGeometryPart[0],Validation=new ToolProfileValidationResult()}).ToArray(),
                PlacementSources=new PlacementSourceSnapshot("CONTROLLED_UI_BENCHMARK",fixtures.Select(f=>f.Assembly),null,new string[0])};
            foreach(var fixture in new[]{new{Name="18x3",Sources=1,Bends=3},new{Name="18x6",Sources=1,Bends=6},new{Name="120x25",Sources=25,Bends=1}})
            {
                var sources=new List<AssemblySourceInput>();
                for(int n=0;n<fixture.Sources;n++)
                {
                    var model=CollisionModel(Rect("CONTROLLED_UI_"+n,n*.01,0,1,1),fixture.Bends);var sheets=BatchEnvelopes(model);
                    var setup=fixtures.SelectMany((f,i)=>sheets.SelectMany(e=>new[]{AxialEvidence(f,e,library,CollisionPairKind.WorkpiecePunch,i%3==0?P(1,2):P(21,22)),
                        AxialEvidence(f,e,library,CollisionPairKind.WorkpieceHolder,i%3==1?P(1,2):P(21,22))})).ToArray();
                    int quantity=fixture.Sources==1?1:n<20?5:4;
                    sources.Add(LocalSource("UI_SOURCE_"+n,model,Enumerable.Range(0,quantity).Select(i=>Occurrence(n,i)),sheets,setup,AssemblyPlans(model,full:true,slow:fixture.Sources==1)));
                }
                using(var coordinator=new AssemblyBatchCoordinator())
                using(var form=new BendAssemblyBatchForm())
                {
                    form.Show();
                    var request=AssemblyRequest(sources,library);
                    foreach(string stage in new[]{"cold","warm"})
                    {
                        var clock=Stopwatch.StartNew();var task=coordinator.RunAsync(request,new AssemblyBatchOptions(4,milliseconds:1200000));
                        while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(1);}
                        var snapshot=task.GetAwaiter().GetResult();
                        form.BindSnapshot(snapshot,clock.Elapsed.TotalSeconds);form.RecordVisibleTime(clock.Elapsed.TotalMilliseconds);
                        var t=form.LastBindingTimings;
                        Check(t!=null&&snapshot.Current&&((DataGridView)form.Controls.Find("dgvParts",true).Single()).Rows.Count==sources.Count,"UI numeric rows are actually rendered");
                        metrics.Add(new {Name=fixture.Name+"-"+stage,t.NativeCaptureMilliseconds,t.WorkerMilliseconds,t.NumericSolverMilliseconds,
                            t.SnapshotValidationMilliseconds,t.SnapshotCopyMilliseconds,t.ViewModelMilliseconds,t.GridRenderingMilliseconds,t.RunToVisibleMilliseconds,
                            snapshot.KernelQueries,SimulationOnly=true});
                    }
                    form.Close();
                }
                if(fixture.Name=="18x6") CheckWorkerClose(sources,library);
            }
            File.WriteAllText(Path.Combine(output,"ui-performance.json"),Json.Serialize(new {Result="PASS",Performance=metrics}));
            Console.WriteLine("UI_BENCHMARK PASS "+metrics.Count+" measured cold/warm runs");return 0;
        }
        finally{foreach(var fixture in fixtures)fixture.Dispose();}
    }
    private sealed class NumericUiBackend : IBendAssemblyBatchBackend
    {
        private readonly AssemblyBatchRequest request;
        private readonly AssemblyBatchCoordinator coordinator=new AssemblyBatchCoordinator();
        internal Task<AssemblyBatchSnapshot> Worker;
        internal NumericUiBackend(AssemblyBatchRequest request){this.request=request;}
        public Task<BendToolCatalogReadResult> ReadMinimumAsync(CancellationToken token)=>Task.FromResult<BendToolCatalogReadResult>(null);
        public IBendAssemblyBatchOperation BeginAssemblyBatch(AssemblyCheckScope scope,BendToolCatalogReadResult minimum,CancellationToken token,IProgress<AssemblyBatchProgress> progress)
        {Worker=coordinator.RunAsync(request,new AssemblyBatchOptions(1,milliseconds:120000),token,progress);return new NumericUiOperation(Worker,coordinator);}
        public void CaptureSelection(){}
        public void Invalidate(){}
        public void Dispose()=>coordinator.Dispose();
    }
    private sealed class NumericUiOperation : IBendAssemblyBatchOperation
    {
        private readonly AssemblyBatchCoordinator coordinator;
        public Task<AssemblyBatchSnapshot> Completion{get;}
        internal NumericUiOperation(Task<AssemblyBatchSnapshot> task,AssemblyBatchCoordinator coordinator){Completion=task;this.coordinator=coordinator;}
        public void Pump(){}
        public void Cancel()=>coordinator.Cancel();
    }
    private static void CheckWorkerClose(IEnumerable<AssemblySourceInput> sources,ToolProfileLibrarySnapshot library)
    {
        using(var form=new BendAssemblyBatchForm())
        {
            var backend=new NumericUiBackend(AssemblyRequest(sources,library));
            using(var controller=new BendAssemblyBatchController(form,backend))
            {
                form.Show();((Button)form.Controls.Find("btnRunBatch",true).Single()).PerformClick();
                Check(backend.Worker!=null&&!backend.Worker.IsCompleted,"Actual numeric worker is executing when close is requested");
                form.Close();Check(!form.IsDisposed,"Form defers close during real collision worker");
                var clock=Stopwatch.StartNew();
                while(!form.IsDisposed&&clock.ElapsedMilliseconds<10000){Application.DoEvents();Thread.Sleep(2);}
                Check(form.IsDisposed&&backend.Worker.IsCompleted,"Cancel/close waits for actual numeric worker and safely disposes");
                Console.WriteLine("UI_REAL_NUMERIC_WORKER_CLOSE PASS");
            }
        }
    }
}
