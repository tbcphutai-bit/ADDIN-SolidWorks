using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ADDIN.Commands;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendPlanning;
using ADDIN.Commands.BendTools;
using ADDIN.UI;

// Only the source-result type is needed at this managed UI boundary. The
// native harness loads the product's real provider and Excel reader instead.
namespace ADDIN.Commands { internal sealed class BendToolCatalogReadResult { } }

internal static class BendAssemblyUiTests
{
    private static int assertions, groups;
    private static readonly CollisionPairKind[] Pairs = { CollisionPairKind.WorkpiecePunch, CollisionPairKind.WorkpieceHolder };
    private static void Check(bool ok, string message) { assertions++; if (!ok) throw new Exception(message); }
    private static void Test(string name, Action action) { action(); groups++; Console.WriteLine("PASS " + name); }
    private static AssemblyOccurrence Occ() => new AssemblyOccurrence("ROOT/MEMBER", "ROOT", "MEMBER.SLDPRT", "S", "Default",
        new double[] { 1,0,0,0,1,0,0,0,1,0,0,0,1 }, AssemblyOccurrenceKind.SheetMetal, true);
    private static AxialPairResult Static(CollisionPairKind pair, CollisionStatus status, params string[] reasons) =>
        new AxialPairResult(new AxialCollisionQuery(null, null, pair), status, reasons, 0, 0, 0);
    private static VolumeCandidateCollisionResult Geometry(int id, CollisionStatus punch = CollisionStatus.UNKNOWN) =>
        new VolumeCandidateCollisionResult(CompanyToolIds.All[0], id, Static(Pairs[0], punch), Static(Pairs[1], CollisionStatus.UNKNOWN), true);
    private static ProcessPairResult Motion(BendProcessPhase phase, CollisionPairKind pair, CollisionStatus status = CollisionStatus.CLEAR,
        string segment = null, Func<string> invalid = null, string reason = null) => new ProcessPairResult(pair, segment ?? phase.ToString(), phase,
            status, reason == null ? new string[0] : new[] { reason }, invalid ?? (() => null), null, 0, 0, 0, 0, 0);
    private static ProcessCandidateResult Process(int id, IEnumerable<ProcessPairResult> pairs = null) =>
        new ProcessCandidateResult(CompanyToolIds.All[0], id, pairs ?? Enum.GetValues(typeof(BendProcessPhase)).Cast<BendProcessPhase>()
            .SelectMany(p => Pairs.Select(pair => Motion(p, pair))), true);
    private static AssemblyPartResult Part(int count, IEnumerable<VolumeCandidateCollisionResult> statics = null,
        IEnumerable<ProcessCandidateResult> processes = null, Func<string> invalid = null) =>
        new AssemblyPartResult("S", new[] { Occ() }, null, statics, processes, count, true, invalid ?? (() => null));
    private static AssemblyBatchSnapshot Snapshot(AssemblyPartResult part, Func<string> invalid = null, bool incomplete = false)
    {
        var occs = new List<AssemblyOccurrence> { Occ() };
        if (incomplete) occs.Add(new AssemblyOccurrence("ROOT/LW", "ROOT", "LW", "LW", "Default", null,
            AssemblyOccurrenceKind.Lightweight, false, assemblyBranch: true));
        var request = new AssemblyBatchRequest("FIXTURE", "Default", AssemblyCheckScope.EntireAssembly, new ADDIN.Commands.BendPlacement.PlacementLease(),
            new ToolProfileLibrarySnapshot { Tools = new CompanyToolProfile[0] },
            new[] { new AssemblySourceInput("S", null, new[] { Occ() }) }, occs, captureMs: 200);
        return new AssemblyBatchSnapshot(request, new CollisionBatchRunState(), new[] { part }, 300, 0, 0, 0, 0, 0, 2,
            new long[8], 0, 0, null, invalid ?? (() => null), 0, 0);
    }
    private static BatchToolCandidateViewModel ConvertTool(IEnumerable<ProcessPairResult> pairs) =>
        BatchViewModelConverter.PartsFromSnapshot(Snapshot(Part(1, new[] { Geometry(42) }, new[] { Process(42, pairs) })))
            .Single().Bends.Single().ToolCandidates[0];
    private static void ConverterTests()
    {
        Test("native nonconsecutive FeatureIds and 18 independent slots; no angle/radius inference", () =>
        {
            var vm = BatchViewModelConverter.PartsFromSnapshot(Snapshot(Part(3, new[] { Geometry(7), Geometry(42), Geometry(314) }))).Single();
            Check(vm.Bends.Select(b => b.FeatureId).SequenceEqual(new[] { 7,42,314 }), "Native IDs retained");
            foreach (var bend in vm.Bends)
            {
                Check(bend.ToolCandidates.Count == 18, "All 18 candidates per real bend");
                Check(bend.ToolCandidates.All(t => t.FeatureId == bend.FeatureId), "Candidate binds to real FeatureId");
                Check(!bend.AngleDeg.HasValue && !bend.RadiusMm.HasValue && bend.AngleDisplay == "Chưa có dữ liệu" &&
                    bend.RadiusDisplay == "Chưa có dữ liệu", "Missing dimensions explicit");
                Check(bend.ToolCandidates.All(t => t.GeometricStatus == CollisionStatus.UNKNOWN), "Missing evidence stays UNKNOWN");
            }
        });
        Test("zero bends and missing candidate inventory never create fictional bends", () =>
        {
            Check(BatchViewModelConverter.PartsFromSnapshot(Snapshot(Part(0))).Single().Bends.Count == 0, "Zero stays zero");
            Check(BatchViewModelConverter.PartsFromSnapshot(Snapshot(Part(0, new[] { Geometry(42) }))).Single().Bends.Count == 0, "Contradictory candidates do not override zero count");
            var missing = BatchViewModelConverter.PartsFromSnapshot(Snapshot(Part(3))).Single();
            Check(missing.Bends.Count == 0 && missing.Reason.Contains("FeatureId"), "No guessed IDs");
            Check(BatchViewModelConverter.PartsFromSnapshot(null).Count == 0, "Null snapshot");
        });
        Test("phase/pair/segment binding differs from aggregate Punch/Holder", () =>
        {
            var rows = Enum.GetValues(typeof(BendProcessPhase)).Cast<BendProcessPhase>().SelectMany(p => Pairs.Select(pair =>
                Motion(p, pair, p == BendProcessPhase.DuringBend && pair == Pairs[1] ? CollisionStatus.COLLISION : CollisionStatus.CLEAR))).ToList();
            rows.Add(Motion(BendProcessPhase.DuringBend, Pairs[0], segment: "DURING_SECOND"));
            rows.Add(Motion(BendProcessPhase.DuringBend, Pairs[1], segment: "DURING_SECOND"));
            var vm = ConvertTool(rows);
            Check(vm.MotionPreBend == CollisionStatus.CLEAR && vm.MotionDuringBend == CollisionStatus.COLLISION &&
                vm.MotionPostBend == CollisionStatus.CLEAR, "Before/after cannot inherit aggregate holder collision");
            Check(vm.MotionSegments.Count == 12 && vm.MotionDetail.Contains("DURING_SECOND"), "Every pair/segment retained");
            Check(vm.GeometricStatus == CollisionStatus.UNKNOWN && vm.ProcessStatus == CollisionStatus.COLLISION, "Static and process separate");
            Check(vm.ManufacturingStatus == "CHƯA XÁC MINH", "Manufacturing not certified");
            var part = BatchViewModelConverter.PartsFromSnapshot(Snapshot(Part(1, processes: new[] { Process(42,rows) }))).Single();
            Check(part.Status == CollisionStatus.UNKNOWN && part.ProcessStatus == CollisionStatus.COLLISION, "Part geometry cannot inherit process verdict");
        });
        Test("missing/duplicate/mismatched phase pairs prevent CLEAR; no aggregate fallback", () =>
        {
            var rows = Enum.GetValues(typeof(BendProcessPhase)).Cast<BendProcessPhase>().SelectMany(p => Pairs.Select(pair => Motion(p,pair))).ToList();
            Check(ConvertTool(rows).ProcessStatus == CollisionStatus.CLEAR, "Full declared pairs can display CLEAR");
            rows.RemoveAll(r => r.Phase == BendProcessPhase.RetractUnload && r.Pair == Pairs[1]);
            var missing = ConvertTool(rows);
            Check(missing.MotionPostBend == CollisionStatus.UNKNOWN && missing.ProcessStatus == CollisionStatus.UNKNOWN, "Missing unload holder");
            rows.Add(Motion(BendProcessPhase.RetractUnload, Pairs[1], segment: "UNMATCHED"));
            Check(ConvertTool(rows).MotionPostBend == CollisionStatus.UNKNOWN, "Segment IDs must agree");
            rows.Add(Motion(BendProcessPhase.PreBend, Pairs[0]));
            Check(ConvertTool(rows).MotionPreBend == CollisionStatus.UNKNOWN, "Duplicate phase/pair/segment is ambiguous");
            var duplicate = BatchViewModelConverter.PartsFromSnapshot(Snapshot(Part(1, new[] { Geometry(42), Geometry(42) }))).Single().Bends.Single();
            Check(duplicate.ToolCandidates[0].CombinedReason == "AMBIGUOUS_CANDIDATE_BINDING", "Duplicate candidate fail closed");
        });
        Test("one colliding tool is not a whole-Part manufacturing verdict", () =>
        {
            var vm = BatchViewModelConverter.PartsFromSnapshot(Snapshot(Part(1, new[] { Geometry(42, CollisionStatus.COLLISION) }))).Single();
            Check(vm.StatusDisplay == "Có dao COLLISION*" && vm.ManufacturingStatus == "CHƯA XÁC MINH", "Candidate conflict label");
            Check(vm.Bends[0].ToolCandidates.Skip(1).All(t => t.GeometricStatus == CollisionStatus.UNKNOWN), "Other tools unaffected");
        });
        Test("invalidated source removes results and phase evidence expires", () =>
        {
            string invalid = null;
            var snap = Snapshot(Part(1, new[] { Geometry(42, CollisionStatus.COLLISION) }), () => invalid);
            Check(BatchViewModelConverter.PartsFromSnapshot(snap).Count == 1, "Initially current");
            invalid = "STALE_SOURCE";
            Check(BatchViewModelConverter.PartsFromSnapshot(snap).Count == 0, "No stale CAD results");
            Check(BatchViewModelConverter.FromSnapshot(snap).ProgressPercent < 100, "No stale success");
            var rows = new[] { Motion(BendProcessPhase.PreBend, Pairs[0], invalid: () => invalid), Motion(BendProcessPhase.PreBend, Pairs[1]) };
            Check(ConvertTool(rows).MotionPreBend == CollisionStatus.UNKNOWN, "Expired pair UNKNOWN");
        });
        Test("completion/progress distinguish wall/native/worker and incomplete/budget", () =>
        {
            var summary = BatchViewModelConverter.FromSnapshot(Snapshot(Part(0)), 2.5);
            Check(summary.ElapsedSeconds == 2.5 && summary.NativeCaptureSeconds == .2 && summary.WorkerSeconds == .3, "Separate timing clocks");
            Check(summary.CompletionConfirmed && summary.ProgressPercent == 100, "Only backend completion confirms 100");
            Check(BatchViewModelConverter.FromSnapshot(Snapshot(Part(0), incomplete: true)).ProgressPercent < 100, "Incomplete inventory");
            Check(BatchViewModelConverter.FromSnapshot(Snapshot(Part(3))).ProgressPercent < 100, "Missing native bend IDs cannot confirm completion");
            var budget = new[] { Motion(BendProcessPhase.PreBend, Pairs[0], CollisionStatus.UNKNOWN, reason: "PROCESS_TIME_BUDGET_EXCEEDED") };
            Check(BatchViewModelConverter.FromSnapshot(Snapshot(Part(1, processes: new[] { Process(42,budget) }))).ProgressPercent < 100, "Budget is not completion");
        });
    }
    private sealed class FakeOperation : IBendAssemblyBatchOperation
    {
        internal readonly TaskCompletionSource<AssemblyBatchSnapshot> Done = new TaskCompletionSource<AssemblyBatchSnapshot>();
        internal int Pumps, Cancellations;
        internal bool InsidePump;
        internal Action PumpAction;
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        public Task<AssemblyBatchSnapshot> Completion => Done.Task;
        public void Pump()
        {
            Check(Thread.CurrentThread.ManagedThreadId == thread, "Pump remains STA"); Pumps++; InsidePump=true;
            try { PumpAction?.Invoke(); } finally { InsidePump=false; }
        }
        public void Cancel() { Cancellations++; }
    }
    private sealed class FakeBackend : IBendAssemblyBatchBackend
    {
        internal FakeOperation Operation;
        internal IProgress<AssemblyBatchProgress> Progress;
        internal AssemblyCheckScope Scope;
        internal int Captures, Invalidations, Disposals;
        internal bool RejectBegin;
        public async Task<BendToolCatalogReadResult> ReadMinimumAsync(CancellationToken token)
        { Operation = null; await Task.Delay(10,token).ConfigureAwait(false); return null; }
        public IBendAssemblyBatchOperation BeginAssemblyBatch(AssemblyCheckScope scope, BendToolCatalogReadResult minimum,
            CancellationToken token, IProgress<AssemblyBatchProgress> progress)
        { if(RejectBegin)throw new InvalidOperationException("No selected components"); Scope = scope; Progress = progress; return Operation = new FakeOperation(); }
        public void CaptureSelection() { Captures++; }
        public void Invalidate() { Invalidations++; }
        public void Dispose() { Check(Operation?.InsidePump!=true,"Never dispose native owner during reentrant Pump"); Disposals++; }
    }
    private static Control Control(Form form, string name) => form.Controls.Find(name, true).Single();
    private static void Click(Form form, string name) => ((Button)Control(form,name)).PerformClick();
    private static int RowCount(Form form) => ((DataGridView)Control(form,"dgvParts")).Rows.Count;
    private static void Spin(Func<bool> until)
    {
        var clock = Stopwatch.StartNew();
        while (!until() && clock.ElapsedMilliseconds < 10000) { Application.DoEvents(); Thread.Sleep(5); }
        Check(until(), "Message loop timeout");
    }
    private static void FormTests()
    {
        Test("Form converts a guarded snapshot once and reuses its Part models for summary", () =>
        {
            int reads=0;
            using(var form=new BendAssemblyBatchForm())
            {
                form.Show();
                form.BindSnapshot(Snapshot(Part(1,new[]{Geometry(42)}),()=>{reads++;return null;}));
                Check(reads<=8,"One validation/conversion pass, no repeated full projection");
                Check(RowCount(form)==1&&((DataGridView)Control(form,"dgvTools")).Rows.Count==18,"The same converted data feeds visible Part/bend/tools");
                Check(form.LastBindingTimings!=null&&form.LastBindingTimings.ViewModelMilliseconds>=0&&
                    form.LastBindingTimings.GridRenderingMilliseconds>=0,"Actual conversion and rendering are timed separately");
                form.Close();
            }
        });
        Test("slow native capture yields between timer turns", () =>
        {
            using(var form=new BendAssemblyBatchForm())
            {
                var backend=new FakeBackend();
                using(var controller=new BendAssemblyBatchController(form,backend))
                {
                    form.Show();Click(form,"btnRunBatch");Spin(()=>backend.Operation!=null);
                    backend.Operation.PumpAction=()=>Thread.Sleep(80);
                    Spin(()=>backend.Operation.Pumps>=2);
                    backend.Operation.PumpAction=null;
                    backend.Operation.Done.SetResult(Snapshot(Part(0)));Spin(()=>!controller.Running);form.Close();
                }
            }
        });
        Test("Form live/preview/run/progress/cancel/stale/refresh/close lifecycle on STA", () =>
        {
            using (var form = new BendAssemblyBatchForm())
            {
                var backend = new FakeBackend();
                using (var controller = new BendAssemblyBatchController(form, backend))
                {
                    form.Show(); Application.DoEvents();
                    Check(!form.IsPreview && RowCount(form) == 0, "Default is empty LIVE");
                    Click(form,"btnToggleMockup"); Check(form.IsPreview && RowCount(form)>0, "Explicit Preview");
                    var ambient = SynchronizationContext.Current;
                    SynchronizationContext.SetSynchronizationContext(null);
                    try { Click(form,"btnRunBatch"); }
                    finally { SynchronizationContext.SetSynchronizationContext(ambient); }
                    Spin(() => backend.Operation != null);
                    Check(!form.IsPreview && RowCount(form)==0 && controller.Running, "Live clears all samples before backend completion");
                    backend.Progress.Report(new AssemblyBatchProgress(1,1,3,54,null));
                    Application.DoEvents(); Check(Control(form,"lblProgressPercent").Text != "100%", "Provisional progress capped");
                    Click(form,"btnCancel"); Check(controller.Running && !Control(form,"btnCancel").Enabled, "Cancel waits for backend acknowledgement");
                    backend.Operation.Done.SetResult(Snapshot(Part(0))); Spin(() => !controller.Running);
                    Check(RowCount(form)==0 && Control(form,"lblProgressPercent").Text != "100%", "Cancelled snapshot not published");
                    form.SetScope(AssemblyCheckScope.SelectedComponents); Check(backend.Captures==1,"Explicit selected scope capture");
                    Click(form,"btnRunBatch");
                    Spin(() => backend.Operation != null);
                    Check(backend.Scope==AssemblyCheckScope.SelectedComponents,"Selected scope reaches backend");
                    string stale = null;
                    backend.Operation.Done.SetResult(Snapshot(Part(1,new[] { Geometry(42) }), () => stale)); Spin(() => !controller.Running);
                    Check(RowCount(form)==1 && Control(form,"lblProgressPercent").Text=="100%", "Backend completion publishes live inventory");
                    Check(((DataGridView)Control(form,"dgvTools")).Rows.Count==18, "Initial row automatically opens real bend detail");
                    Check(((DataGridView)Control(form,"dgvParts")).Columns[1].HeaderText=="Configuration", "Designer schema retained");
                    stale="STALE_SOURCE"; Spin(() => RowCount(form)==0);
                    Click(form,"btnRefresh"); Check(controller.Running && backend.Captures==1,"Refresh retains captured selection");
                    Spin(() => backend.Operation != null);
                    var lastProgress=backend.Progress;
                    form.Close(); Check(!form.IsDisposed && controller.Running,"Close waits at safe native boundary");
                    backend.Operation.Done.SetResult(Snapshot(Part(0))); Spin(() => form.IsDisposed);
                    Check(backend.Disposals==1,"Backend disposed exactly once");
                    lastProgress.Report(new AssemblyBatchProgress(1,1,0,0,null)); Application.DoEvents();
                }
            }
        });
        Test("reentrant close and backend rejection cannot report success or dispose during capture", () =>
        {
            using(var form=new BendAssemblyBatchForm())
            {
                var backend=new FakeBackend {RejectBegin=true};
                using(var controller=new BendAssemblyBatchController(form,backend))
                {
                    form.Show();UiStart();
                    void UiStart() { Click(form,"btnRunBatch"); }
                    Spin(()=>!controller.Running);
                    Check(RowCount(form)==0&&Control(form,"lblProgressPercent").Text!="100%"&&Control(form,"btnRunBatch").Enabled,"Rejected begin is not success");
                    backend.RejectBegin=false;UiStart();Spin(()=>backend.Operation!=null);
                    backend.Operation.PumpAction=()=>
                    {
                        backend.Operation.PumpAction=null;
                        form.Close();Check(!form.IsDisposed,"Reentrant close is deferred");
                        backend.Operation.Done.SetResult(Snapshot(Part(0)));
                    };
                    Spin(()=>form.IsDisposed);Check(backend.Disposals==1,"Safe deferred disposal");
                }
            }
        });
    }
    private static void Render(string output)
    {
        Directory.CreateDirectory(output);
        foreach (float factor in new[] { 1f,1.25f,1.5f })
        foreach (var size in new[] { new Size(1024,680), new Size(1264,821), new Size(1920,1080) })
        using (var form = new BendAssemblyBatchForm())
        {
            form.Show(); form.ClientSize=size; form.Scale(new SizeF(factor,factor)); Application.DoEvents();
            form.BindSnapshot(Snapshot(Part(3,new[] { Geometry(7),Geometry(42),Geometry(314) })), 1);
            Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width,form.Height))
            { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(output,"form-"+size.Width+"-scale-"+(int)(factor*100)+".png")); }
            Console.WriteLine("LAYOUT_SCALE " + factor + " deviceDpi=" + form.DeviceDpi + " client=" + form.ClientSize);
            Check(Control(form,"btnRunBatch").Visible && Control(form,"dgvParts").Width>0 && Control(form,"dgvTools").Height>0,"Usable scaled controls");
            var tabs=(TabControl)Control(form,"checkChanDetailTabs");
            for(int tab=0;tab<tabs.TabPages.Count;tab++)
            {
                tabs.SelectedIndex=tab; Application.DoEvents();
                Check(tabs.SelectedTab.ClientSize.Width>500 && tabs.SelectedTab.ClientSize.Height>100,"Each functional tab has usable scrollable viewport");
            }
            form.Close();
        }
    }
    private static void UnifiedNavigationTests()
    {
        Test("unified: imported signed simulation reaches existing 18-tool timeline with explicit evidence label",()=>{
            var imported=ToolProfileTests.ImportedUiFixture();
            var raw=new AssemblyPartResult("IMPORTED",new[]{Occ()},null,new[]{Geometry(7),Geometry(42)},null,2,true,()=>null,imported.Item1);
            using(var form=new BendAssemblyBatchForm())
            {
                form.Show();form.BindSnapshot(Snapshot(raw));((TabControl)Control(form,"checkChanDetailTabs")).SelectedIndex=1;Application.DoEvents();
                Check(((DataGridView)Control(form,"sequenceTools")).Rows.Count==18&&((DataGridView)Control(form,"sequenceOptions")).Rows.Count==2,"File-import planner is rendered, not placeholder rows");
                Check(Control(form,"sequenceSummary").Text.StartsWith("SIMULATION")&&Control(form,"sequenceEvidence").Text.Contains("SimulationOnly"),"Simulation and reviewed evidence are direct labels");
                imported.Item2.Lease.Invalidate();form.ClearLive("STALE_PROCESS_ARTIFACT");Check(((DataGridView)Control(form,"sequenceOptions")).Rows.Count==0,"Stale imported proof is removed");form.Close();
            }
        });
        Test("unified: real numeric planner -> 18 tools -> sequence -> native timeline -> independent phase witnesses", () => {
            var fixture = ToolProfileTests.SequenceUiFixture();
            var raw = new AssemblyPartResult("S", new[] { Occ() }, null, new[] { Geometry(7), Geometry(42) }, null, 2, true, () => null, fixture.Item1);
            using (var form = new BendAssemblyBatchForm())
            {
                form.Show(); form.BindSnapshot(Snapshot(raw));
                var tabs = (TabControl)Control(form, "checkChanDetailTabs"); tabs.SelectedIndex = 1; Application.DoEvents();
                var tools = (DataGridView)Control(form, "sequenceTools"); var options = (DataGridView)Control(form, "sequenceOptions");
                var timeline = (DataGridView)Control(form, "sequenceTimeline"); var phases = (DataGridView)Control(form, "sequencePhases");
                Check(tools.Rows.Count == 18 && options.Rows.Count == 2, "Actual planner results bound lazily for one tool");
                Check(ReferenceEquals(options.CurrentRow.Tag,fixture.Item1.PreferredCheckedSequence)&&
                    ((BendSequenceResult)options.CurrentRow.Tag).ToolTypeCount==1&&((BendSequenceResult)options.CurrentRow.Tag).ToolChangeCount==0,
                    "Default selection prefers a proven one-tool route with zero changes; simulation label remains");
                tools.CurrentCell=tools.Rows[0].Cells[0];Application.DoEvents();
                Check(options.Rows.Cast<DataGridViewRow>().Any(r => Convert.ToString(r.Cells[1].Value) == "COLLISION") &&
                    options.Rows.Cast<DataGridViewRow>().Any(r => Convert.ToString(r.Cells[1].Value) == "CLEAR"), "Different orders and actual numeric statuses visible");
                var clear = options.Rows.Cast<DataGridViewRow>().First(r => Convert.ToString(r.Cells[1].Value) == "CLEAR");
                options.CurrentCell = clear.Cells[0];
                Check(timeline.Rows.Count == 2 && Convert.ToInt32(timeline.Rows[0].Cells[1].Value) == 42 && phases.Rows.Count == 10, "Native timeline + five Punch/Holder phase pairs");
                tabs.SelectedIndex = 0;
                var pills = Control(form, "pnlBendPills").Controls.OfType<Button>().ToArray(); pills.Last().PerformClick();
                var nativeTools = (DataGridView)Control(form, "dgvTools"); nativeTools.CurrentCell = nativeTools.Rows[5].Cells[0];
                string tool = Convert.ToString(nativeTools.Rows[5].Cells[0].Value); form.BindSnapshot(Snapshot(raw));
                Check(Control(form, "lblDetailBendTitle").Text.Contains("42") && Control(form, "lblCalloutTitle").Text.Contains(tool), "Part/Bend/Tool selection survives current identity refresh");
                fixture.Item2.Lease.Invalidate(); form.ClearLive("STALE"); tabs.SelectedIndex = 1;
                Check(options.Rows.Count == 0 && phases.Rows.Count == 0 && !tools.Rows.Cast<DataGridViewRow>().Any(r => Convert.ToString(r.Cells[3].Value) == "Có chứng minh*"), "Stale proof withdrawn");
                form.Close();
            }
        });
        Test("unified: bounded large-Part grid and zero-bend Part mode", () => {
            using (var form = new BendAssemblyBatchForm())
            {
                form.Show(); form.SetPartMode();
                Check(Control(form, "btnScopeEntire").Text == "Part hiện tại" && !Control(form, "btnScopeSelected").Visible, "Single Part uses same window");
                var parts = Enumerable.Range(0, 2000).Select(i => new BatchPartViewModel { SourceKey = "SOURCE" + i, PartName = "SAMPLE " + i,
                    Configuration = "Default", Quantity = 1, Reason = "Missing data", Status = CollisionStatus.UNKNOWN }).ToList();
                typeof(BendAssemblyBatchForm).GetField("allParts",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(form,parts);
                typeof(BendAssemblyBatchForm).GetMethod("ApplyPartFilters",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(form,null);
                Check(RowCount(form) == 250, "2000 Parts do not populate 2000 display rows"); Click(form, "partsPageNext");
                Check(Convert.ToString(((DataGridView)Control(form, "dgvParts")).Rows[0].Cells[0].Value) == "SAMPLE 250", "Paging retains stable Part records");
                ((TextBox)Control(form,"txtSearch")).Text = "SAMPLE 1999";
                Check(RowCount(form) == 1 && ((DataGridView)Control(form,"dgvTools")).Rows.Count == 0, "Search spans full dataset; zero bends invent nothing");
                form.Close();
            }
        });
        Test("unified: Check Min projection keeps native evaluator status and dimensions separate from Max", () => {
            var metadata = new[] { new AssemblyBendMetadata(42, "PERSISTENT", "Native bend", 90, .1, "BelowMinimum", "B 10 < 17.1", "Existing evaluator result",
                new[] { new AssemblyBendMinimumSide("Z", "B", 10, 17.1), new AssemblyBendMinimumSide("L", "A", 20, 12.5) }) };
            var raw = new AssemblyPartResult("S",new[]{Occ()},null,new[]{Geometry(42)},null,1,true,()=>null,null,metadata);
            using(var form = new BendAssemblyBatchForm())
            {
                form.Show(); form.BindSnapshot(Snapshot(raw));
                var grid = (DataGridView)Control(form,"batchMinimumEvidence");
                Check(grid.Rows.Count==2 && Convert.ToString(grid.Rows[0].Cells[1].Value)=="NG Min" && Convert.ToDouble(grid.Rows[0].Cells[4].Value)==10,
                    "Exact Min result/outer dimension projected without recalculation");
                Check(((DataGridView)Control(form,"dgvTools")).Rows.Cast<DataGridViewRow>().All(r=>Convert.ToString(r.Cells[4].Value).Contains("UNKNOWN")),
                    "Min NG cannot invent a Max manufacturing verdict");
                var unsupported = new AssemblyPartResult("S",new[]{Occ()},"MISSING_NATIVE_MAX_CAPTURE",null,null,0,false,()=>null,null,metadata);
                form.BindSnapshot(Snapshot(unsupported));Check(grid.Rows.Count==2,"Optional Max capture failure does not hide existing Check Min dimensions");
                form.ClearLive("STALE"); Check(grid.Rows.Count==0,"Withdraw stale Min display alongside managed Max display"); form.Close();
            }
        });
    }
    private static void RenderPlanner(string output)
    {
        Directory.CreateDirectory(output);var fixture=ToolProfileTests.SequenceUiFixture(6);var planner=fixture.Item1;
        var raw=new AssemblyPartResult("S",new[]{Occ()},null,fixture.Item2.Bends.Select(b=>Geometry(b.FeatureId)),null,6,true,()=>null,planner);
        using(var form=new BendAssemblyBatchForm("CONTROLLED SIMULATION","Synthetic"))
        {
            form.Show();var watch=Stopwatch.StartNew();form.BindSnapshot(Snapshot(raw));
            var tabs=(TabControl)Control(form,"checkChanDetailTabs");tabs.SelectedIndex=1;Application.DoEvents();form.Update();
            double visible=watch.Elapsed.TotalMilliseconds;var options=(DataGridView)Control(form,"sequenceOptions");
            var clear=options.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>Convert.ToString(r.Cells[1].Value)=="CLEAR");
            if(clear!=null)options.CurrentCell=clear.Cells[0];
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"simulation-sequence.png"));}
            File.WriteAllText(Path.Combine(output,"planner-ui-timing.json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{
                SimulationOnly=true,Bends=6,Tools=planner.Tools.Count,PlannerMilliseconds=planner.ElapsedMilliseconds,RenderToVisibleMilliseconds=visible,
                NativeCaptureMilliseconds=0,KernelQueries=planner.KernelQueries,DeviceDpi=form.DeviceDpi}));
            Console.WriteLine("PLANNER_UI 18x6 numeric; planner ms="+planner.ElapsedMilliseconds+" render ms="+visible);form.Close();
        }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length==2&&args[0]=="--benchmark-ui")return ToolProfileTests.UiReadBenchmark(args[1]);
            ConverterTests(); FormTests(); UnifiedNavigationTests();
            if(args.Length==2 && args[0]=="--render") { Render(args[1]); RenderPlanner(args[1]); }
            Console.WriteLine("RESULT " + groups + " groups; " + assertions + " assertions; 0 failures"); return 0;
        }
        catch(Exception ex) { Console.WriteLine("FAIL " + ex); return 1; }
    }
}
