using System;
using System.Reflection;
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
    private static AssemblyBatchSnapshot Snapshot(AssemblyPartResult part, Func<string> invalid = null, bool incomplete = false,ToolProfileLibrarySnapshot library=null)
    {
        var occs = new List<AssemblyOccurrence> { Occ() };
        if (incomplete) occs.Add(new AssemblyOccurrence("ROOT/LW", "ROOT", "LW", "LW", "Default", null,
            AssemblyOccurrenceKind.Lightweight, false, assemblyBranch: true));
        var request = new AssemblyBatchRequest("FIXTURE", "Default", AssemblyCheckScope.EntireAssembly, new ADDIN.Commands.BendPlacement.PlacementLease(),
            library??new ToolProfileLibrarySnapshot { Tools = new CompanyToolProfile[0] },
            new[] { new AssemblySourceInput("S", null, new[] { Occ() }) }, occs, captureMs: 200);
        return new AssemblyBatchSnapshot(request, new CollisionBatchRunState(), new[] { part }, 300, 0, 0, 0, 0, 0, 2,
            new long[8], 0, 0, null, invalid ?? (() => null), 0, 0);
    }
    private static BatchToolCandidateViewModel ConvertTool(IEnumerable<ProcessPairResult> pairs) =>
        BatchViewModelConverter.PartsFromSnapshot(Snapshot(Part(1, new[] { Geometry(42) }, new[] { Process(42, pairs) })))
            .Single().Bends.Single().ToolCandidates[0];
    private static void ConverterTests()
    {
        Test("editor: host close and repeated Dispose preserve drafts and cancel preparation once",()=>{
            string root=Path.Combine(Path.GetTempPath(),"ADDIN-EditorDispose-"+Guid.NewGuid().ToString("N"));
            using(var host=new Form())
            using(var pane=new BendToolEvidenceControl(root))
            {
                host.Controls.Add(pane);host.Show();pane.ShowTool(ToolProfileTests.CadPairUiLibrary().PlacementSources.Tools[0],null,()=>true);
                pane.ProposalText="{ unfinished private input";host.Close();
                Check(pane.IsDisposed&&File.Exists(pane.LastRecoveryPath),"Host close first saves incomplete authoring input");
                pane.Dispose();pane.Dispose();Check(pane.IsDisposed,"Repeated cleanup has no ObjectDisposedException from cancellation source");
            }
        });
        Test("editor: import edit export round-trip restores every role and parent; bad input is atomic",()=>{
            var source=ToolProfileTests.CadPairUiLibrary().PlacementSources.Tools[0];bool current=true;
            using(var pane=new BendToolEvidenceControl(Path.Combine(Path.GetTempPath(),"ADDIN-EditorRoundtrip-"+Guid.NewGuid().ToString("N"))))
            {
                pane.ShowTool(source,null,()=>current);var d=pane.Draft;var keys=d.Rows.Select(r=>r.EntityKey).ToArray();
                var p=ADDIN.Commands.BendPlacement.ToolEvidenceReviewWorkspace.Create(d);
                p.Material.Contours=new List<ADDIN.Commands.BendPlacement.HolderContourDefinition> {
                    new ADDIN.Commands.BendPlacement.HolderContourDefinition {Id="outside",MaterialKind="Solid",EntityKeys=new[]{keys[0]}},
                    new ADDIN.Commands.BendPlacement.HolderContourDefinition {Id="inside",MaterialKind="Hole",ParentId="outside",EntityKeys=new[]{keys[1]}},
                    new ADDIN.Commands.BendPlacement.HolderContourDefinition {Id="island",MaterialKind="Island",ParentId="inside",EntityKeys=new[]{keys[2]}}};
                p.Material.Excluded=new List<ADDIN.Commands.BendPlacement.HolderExcludedEntity> {new ADDIN.Commands.BendPlacement.HolderExcludedEntity {EntityKey=keys[3],Role="Annotation"}};
                pane.ImportProposal(ADDIN.Commands.BendPlacement.ToolEvidenceReviewWorkspace.Serialize(p),null);
                var grid=(DataGridView)pane.Controls.Find("holderRoleEntities",true).Single();
                Check(Convert.ToString(grid.Rows[1].Cells[5].Value)=="Hole"&&Convert.ToString(grid.Rows[1].Cells[6].Value)=="inside"&&Convert.ToString(grid.Rows[2].Cells[7].Value)=="inside","Actual imported assignments, IDs and parent are in grid");
                grid.Rows[1].Cells[6].Value="edited-hole";grid.Rows[2].Cells[7].Value="edited-hole";
                var edited=pane.PrepareUnsignedExport();Check(edited.Material.Contours.Single(c=>c.Id=="island").ParentId=="edited-hole"&&edited.Material.Excluded.Single().Role=="Annotation","Edited imported table exports losslessly");
                var changedText=ADDIN.Commands.BendPlacement.ToolEvidenceReviewWorkspace.Read(ADDIN.Commands.BendPlacement.ToolEvidenceReviewWorkspace.Serialize(edited),d);
                changedText.Material.Excluded.Single().Role="Reference";pane.ProposalText=ADDIN.Commands.BendPlacement.ToolEvidenceReviewWorkspace.Serialize(changedText);
                grid.Rows[3].Cells[5].Value="Decorative";pane.ConfirmAction=m=>false;
                bool conflict=false;try{pane.PrepareUnsignedExport();}catch(InvalidDataException){conflict=true;}
                Check(conflict&&pane.ProposalText.Contains("Reference")&&Convert.ToString(grid.Rows[3].Cells[5].Value)=="Decorative","Conflicting grid/document edits cannot silently discard either buffer");
                pane.ConfirmAction=m=>true;var chosen=pane.PrepareUnsignedExport();
                Check(chosen.Material.Excluded.Single().Role=="Decorative"&&File.Exists(pane.LastRecoveryPath),"Explicit choice archives both buffers before applying table roles");
                pane.ImportProposal(ADDIN.Commands.BendPlacement.ToolEvidenceReviewWorkspace.Serialize(edited),null);
                pane.ProposalText=ADDIN.Commands.BendPlacement.ToolEvidenceReviewWorkspace.Serialize(changedText);
                Check(pane.PrepareUnsignedExport().Material.Excluded.Single().Role=="Reference"&&Convert.ToString(grid.Rows[3].Cells[5].Value)=="Reference","Direct document edit exports and refreshes the role grid without using stale rows");
                pane.ImportProposal(ADDIN.Commands.BendPlacement.ToolEvidenceReviewWorkspace.Serialize(edited),null);
                Check(Convert.ToString(grid.Rows[1].Cells[6].Value)=="edited-hole"&&Convert.ToString(grid.Rows[2].Cells[7].Value)=="edited-hole","Second import is a true round-trip");
                string before=pane.ProposalText;p.MasterSha256=new string('a',64);bool rejected=false;
                try{pane.ImportProposal(ADDIN.Commands.BendPlacement.ToolEvidenceReviewWorkspace.Serialize(p),null);}catch(InvalidDataException){rejected=true;}
                Check(rejected&&pane.ProposalText==before&&Convert.ToString(grid.Rows[1].Cells[6].Value)=="edited-hole","Wrong revision cannot silently replace editor state");
                current=false;rejected=false;try{pane.PrepareUnsignedExport();}catch(InvalidDataException){rejected=true;}Check(rejected,"Stale editor cannot export native-bound data");
            }
        });
        Test("editor: unsaved grid and partial text survive tool/Part/bend changes in private source-bound recovery",()=>{
            string root=Path.Combine(Path.GetTempPath(),"ADDIN-EditorRecovery-"+Guid.NewGuid().ToString("N"));var library=ToolProfileTests.CadPairUiLibrary();var sheets=ToolProfileTests.EditorContextUiFixtures();
            using(var pane=new BendToolEvidenceControl(root))
            {
                pane.ShowTool(library.PlacementSources.Tools[0],sheets[0],()=>true);var grid=(DataGridView)pane.Controls.Find("holderRoleEntities",true).Single();
                grid.Rows[0].Cells[5].Value="Reference";pane.ProposalText="{ incomplete editing buffer";
                pane.ShowTool(library.PlacementSources.Tools[1],sheets[0],()=>true);
                Check(File.Exists(pane.LastRecoveryPath)&&Directory.GetFiles(root).Length==1,"Unsaved grid and incomplete JSON are privately archived before switching");
                Check(grid.Rows.Cast<DataGridViewRow>().All(r=>Convert.ToString(r.Cells[5].Value)=="NeedsReview"),"Other ToolId does not inherit role edits");
                pane.ShowTool(library.PlacementSources.Tools[0],sheets[1],()=>true);
                Check(grid.Rows.Cast<DataGridViewRow>().All(r=>Convert.ToString(r.Cells[5].Value)=="NeedsReview"),"Other native bend does not inherit context");
                pane.ShowTool(library.PlacementSources.Tools[0],sheets[0],()=>true);
                Check(pane.ProposalText=="{ incomplete editing buffer"&&Convert.ToString(grid.Rows[0].Cells[5].Value)=="Reference","Exact context restores both uncommitted buffers without parsing away incomplete text");
                pane.ImportProposal(File.ReadAllText(pane.LastRecoveryPath),null);
                Check(pane.ProposalText=="{ incomplete editing buffer"&&Convert.ToString(grid.Rows[0].Cells[5].Value)=="Reference","Explicit recovery import preserves incomplete text as non-evidence");
            }
        });
        Test("editor: bulk roles require confirmation; groups do not infer material; approved input needs unsigned fork",()=>{
            var source=ToolProfileTests.CadPairUiLibrary().PlacementSources.Tools[0];
            using(var host=new Form())
            using(var pane=new BendToolEvidenceControl(Path.Combine(Path.GetTempPath(),"ADDIN-EditorBulk-"+Guid.NewGuid().ToString("N"))))
            {
                host.ClientSize=new Size(1000,780);host.Controls.Add(pane);host.Show();Application.DoEvents();
                Check(Convert.ToString(((ComboBox)pane.Controls.Find("holderBulkRole",true).Single()).SelectedValue)=="","Binding never preselects Solid on behalf of the reviewer");
                Check(ToolReferenceCanvas.ProposalColor("NeedsReview")!=ToolReferenceCanvas.ProposalColor("Solid"),"Unassigned CAD cannot appear as proposed solid material");
                pane.ShowTool(source,null,()=>true);var grid=(DataGridView)pane.Controls.Find("holderRoleEntities",true).Single();
                ((Button)pane.Controls.Find("selectHolderConnectedGroup",true).Single()).PerformClick();Check(grid.SelectedRows.Count>0&&grid.Rows.Cast<DataGridViewRow>().All(r=>Convert.ToString(r.Cells[5].Value)=="NeedsReview"),"Connected selection cannot classify material");
                foreach(DataGridViewRow row in grid.Rows)row.Selected=true;int confirmations=0;pane.ConfirmAction=m=>{confirmations++;return false;};
                Check(!pane.ApplySelectionRoles("Reference",null,null)&&grid.Rows.Cast<DataGridViewRow>().All(r=>Convert.ToString(r.Cells[5].Value)=="NeedsReview"),"Denied confirmation changes nothing");
                pane.ConfirmAction=m=>{confirmations++;return true;};Check(pane.ApplySelectionRoles("Reference",null,null)&&confirmations==2,"Explicit mass classification confirmed exactly once per attempt");
                var outer=grid.Rows.Cast<DataGridViewRow>().Single(r=>Convert.ToString(r.Cells[0].Value)=="H");outer.Cells[5].Value="Solid";outer.Cells[6].Value="source-outer";
                var p=pane.PrepareUnsignedExport();Check(p.Material.Excluded.Count==grid.Rows.Count-1&&p.Material.Review==null,"Whole entity assignment is complete and unsigned");
                p.Material.Review=new ADDIN.Commands.BendPlacement.ToolReviewProof();pane.ImportProposal(ADDIN.Commands.BendPlacement.ToolEvidenceReviewWorkspace.Serialize(p),null);
                Check(grid.Columns[5].ReadOnly,"Imported attestations never silently stripped for editing");
                bool rejected=false;try{pane.ApplyRoles();}catch(InvalidDataException){rejected=true;}Check(rejected,"Approved-input fork is explicit, not implicit on Apply");
                ((Button)pane.Controls.Find("forkToolProposal",true).Single()).PerformClick();Check(!grid.Columns[5].ReadOnly&&pane.PrepareUnsignedExport().Material.Review==null&&p.Material.Review!=null,"Confirmed fork leaves original signed input intact");
                Check(source.HolderObstacles.Topology.SourceEntityGroups.SelectMany(g=>g).Distinct().Count()==source.Holder.EntityKeys.Count,"Selection groups preserve all source entities");
            }
        });
        Test("final evidence UX: source preview, explicit unsigned roles and stale export barrier",()=>{
            bool current=true;var source=ToolProfileTests.CadPairUiLibrary().PlacementSources.Tools[0];
            using(var pane=new BendToolEvidenceControl(Path.Combine(Path.GetTempPath(),"ADDIN-EditorTest-"+Guid.NewGuid().ToString("N"))))
            {
                pane.ShowTool(source,null,()=>current);
                Check(pane.Draft.Rows.Count==source.Holder.SourceEntities.Count&&!pane.Draft.MaterialReviewed,"Every original entity visible without material approval");
                var grid=(DataGridView)pane.Controls.Find("holderRoleEntities",true).Single();
                foreach(DataGridViewRow row in grid.Rows){bool solid=Convert.ToString(row.Cells[0].Value)=="H";
                    row.Cells[5].Value=solid?"Solid":"Reference";if(solid)row.Cells[6].Value="REVIEWER_SELECTED_CONTOUR";}
                pane.ApplyRoles();
                var p=ADDIN.Commands.BendPlacement.ToolEvidenceReviewWorkspace.Read(pane.ProposalText,pane.Draft);
                Check(p.Material.Review==null&&p.Material.Contours.Count==1&&!source.ClampVerified,"Unsigned proposal is not installed evidence");
                Check(p.Seating.TipPointMm.X==null&&p.Seating.HolderSourceToPunchSource.A==null,"No guessed pose from viewer fit");
                current=false;bool refused=false;try{pane.ApplyRoles();}catch(InvalidOperationException){refused=true;}catch(InvalidDataException){refused=true;}
                Check(refused,"Source revocation blocks held editor");
                pane.ShowTool(null,null,null);Check(pane.Draft==null&&grid.Rows.Count==0&&pane.ProposalText.Length==0,"Refresh clears source-bound review pane");
            }
        });
        Test("18 fixed CAD pair identities reach every UI bend without promotion",()=>{
            var library=ToolProfileTests.CadPairUiLibrary();var snapshot=Snapshot(Part(3,new[]{Geometry(7),Geometry(42),Geometry(314)}),library:library);
            var part=BatchViewModelConverter.PartsFromSnapshot(snapshot).Single();
            foreach(var bend in part.Bends){Check(bend.ToolCandidates.Count==18,"18 fixed pair candidates");
                Check(bend.ToolCandidates.All(t=>t.CadAssembly?.CadPair?.Matched==true&&t.CadAssembly.Id==t.ToolId&&!t.CadAssembly.ClampVerified),"Source identities stay with their ToolId");}
            using(var form=new BendAssemblyBatchForm()){form.Show();form.BindSnapshot(snapshot);Application.DoEvents();
                Check(Control(form,"lblCalloutHolder").Text.Contains("CAD tham khảo"),"Reference status visible");
                var evidence=(DataGridView)typeof(BendAssemblyBatchForm).GetField("phaseEvidence",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                Check(evidence.Rows.Cast<DataGridViewRow>().Any(r=>Convert.ToString(r.Cells[0].Value)=="CAD / Đầu kẹp chày"),"Exact Holder identity visible in existing evidence area");form.Close();}
        });
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
            Check(vm.StatusDisplay == "Có phương án va chạm*" && vm.ManufacturingStatus == "CHƯA XÁC MINH", "Candidate conflict label");
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
        Test("Vietnamese vocabulary preserves codes, phase meaning and missing clearance",()=>{
            foreach(var value in Enum.GetValues(typeof(CollisionStatus)).Cast<CollisionStatus>())
                Check(BendCheckDisplayText.Collision(value.ToString()).OriginalCode==value.ToString(),"Raw collision enum unchanged");
            foreach(var value in Enum.GetValues(typeof(BendProcessPhase)).Cast<BendProcessPhase>())
                Check(BendCheckDisplayText.ProcessPhase(value.ToString()).OriginalCode==value.ToString()&&!BendCheckDisplayText.Phase(value).Contains(value.ToString()),"Five phases localized without changing identity");
            Check(BendCheckDisplayText.CheckMin("BelowMinimum").Caption=="Cạnh chấn không đạt tối thiểu","Real Min NG remains NG");
            Check(BendCheckDisplayText.Collision("UNRECOGNIZED").Caption.StartsWith("Trạng thái chưa được hỗ trợ"),"Unknown code cannot become CLEAR");
            Check(BendCheckDisplayText.Clearance(null)=="Chưa tính được"&&BendCheckDisplayText.Clearance(0)=="0 mm","Missing clearance differs from measured zero");
            var unmapped=BendCheckDisplayText.Reason("A_NEW_PROVEN_CLEAR_REASON");
            Check(!unmapped.Caption.Contains("Chưa đủ dữ liệu")&&unmapped.OriginalCode=="A_NEW_PROVEN_CLEAR_REASON"&&unmapped.Explanation.Contains("không"),"Untranslated reason remains neutral and retains exact code");
            Check(BendCheckDisplayText.Reasons("TOOL_AXIAL_EXTENT_UNVERIFIED",true)=="TOOL_AXIAL_EXTENT_UNVERIFIED"&&
                BendCheckDisplayText.Reasons("TOOL_AXIAL_EXTENT_UNVERIFIED").Contains("chiều dài"),"Exact raw recovery plus useful explanation");
            Check(!BendCheckDisplayText.Reasons("CERTIFIED_WHOLE_SEGMENT_SEPARATED").Contains("Chưa đủ dữ liệu")&&
                BendCheckDisplayText.Reasons("WHOLE_SUPPLIED_SEQUENCE_CLEAR_IN_CHECKED_SCOPE").Contains("phạm vi"),"Proven numeric evidence has an accurate scope explanation");
        });
        Test("Vietnamese live view keeps 18 UNKNOWN candidates and technical recovery without rebinding review",()=>{
            var numeric=new VolumeCandidateCollisionResult(CompanyToolIds.All[0],42,
                Static(Pairs[0],CollisionStatus.UNKNOWN,"TOOL_AXIAL_EXTENT_UNVERIFIED"),
                Static(Pairs[1],CollisionStatus.UNKNOWN,"HOLDER_MATERIAL_ROLES_UNREVIEWED"),true);
            using(var form=new BendAssemblyBatchForm())
            {
                form.Show();form.BindSnapshot(Snapshot(Part(1,new[]{numeric})));
                var grid=(DataGridView)Control(form,"dgvTools");
                Check(grid.Rows.Count==18&&grid.Rows.Cast<DataGridViewRow>().All(r=>((BatchToolCandidateViewModel)r.Tag).GeometricStatus==CollisionStatus.UNKNOWN),"No numeric work cannot become success");
                var parts=(DataGridView)Control(form,"dgvParts");
                var part=(BatchPartViewModel)parts.Rows[0].Tag;
                string common=BendCheckDisplayText.PartReason(part);
                Check(common.StartsWith("18 bộ dao chưa có kết luận")&&common.Contains("chiều dài"),"Common missing input summarized once at Part level");
                var tips=(ToolTip)typeof(BendAssemblyBatchForm).GetField("toolTips",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                string punchTip=tips.GetToolTip(Control(form,"lblCalloutPunch")),holderTip=tips.GetToolTip(Control(form,"lblCalloutHolder"));
                Check(punchTip.Contains("Chưa tính được")&&holderTip.Contains("Chưa tính được")&&!punchTip.Contains("≥  mm")&&!holderTip.Contains("0 mm"),"Actual callouts never invent null clearance");
                Check(Control(form,"txtDiagnostics").Text.Contains("Chưa thực hiện phép tính va chạm"),"Zero queries are visible");
                string nativeSource=((BatchToolCandidateViewModel)grid.Rows[0].Tag).ToolId;
                Check(Convert.ToString(grid.Rows[0].Cells[7].Value).Contains("chiều dài"),"Missing axial input explained");
                ((CheckBox)Control(form,"checkChanTechnical")).Checked=true;
                Check(Convert.ToString(grid.Rows[0].Cells[7].Value).Contains("TOOL_AXIAL_EXTENT_UNVERIFIED")&&
                    ((BatchToolCandidateViewModel)grid.Rows[0].Tag).ToolId==nativeSource&&((BatchToolCandidateViewModel)grid.Rows[0].Tag).FeatureId==42,"Technical view recovers exact code without changing native identity");
                form.LoadMockupData();Check(Control(form,"badgeConcept").Text=="DỮ LIỆU MINH HỌA","Synthetic illustration cannot masquerade as live CAD");
                form.ClearLive("STALE");Check(RowCount(form)==0,"Stale results withdrawn regardless of display language");
                form.Close();
            }
        });
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
                    Check(((DataGridView)Control(form,"dgvParts")).Columns[1].HeaderText=="Cấu hình", "Localized header retains the same configuration column");
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
            var scope=Control(form,"btnScopeEntire");
            Check(scope.Width>=TextRenderer.MeasureText(scope.Text,scope.Font).Width+scope.Padding.Horizontal,"Full Vietnamese scope caption fits");
            Check(Control(form,"badgeConcept").Width>=TextRenderer.MeasureText(Control(form,"badgeConcept").Text,Control(form,"badgeConcept").Font).Width,"Evidence source badge fits");
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
                ((CheckBox)Control(form,"checkChanTechnical")).Checked=true; Check(Control(form,"sequenceSummary").Text.StartsWith("Dữ liệu mô phỏng")&&Control(form,"sequenceEvidence").Text.Contains("SimulationOnly"),"Simulation and reviewed evidence are direct labels");
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
                Check(options.Rows.Cast<DataGridViewRow>().Any(r => Convert.ToString(r.Cells[1].Value) == BendCheckDisplayText.Collision(CollisionStatus.COLLISION)) &&
                    options.Rows.Cast<DataGridViewRow>().Any(r => Convert.ToString(r.Cells[1].Value) == BendCheckDisplayText.Collision(CollisionStatus.CLEAR)), "Different orders and actual numeric statuses visible");
                var clear = options.Rows.Cast<DataGridViewRow>().First(r => Convert.ToString(r.Cells[1].Value) == BendCheckDisplayText.Collision(CollisionStatus.CLEAR));
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
                Check(Control(form, "btnScopeEntire").Text == "Chi tiết đang mở" && !Control(form, "btnScopeSelected").Visible, "Single Part uses same window");
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
                Check(grid.Rows.Count==2 && Convert.ToString(grid.Rows[0].Cells[1].Value)=="Không đạt tối thiểu" && Convert.ToDouble(grid.Rows[0].Cells[4].Value)==10,
                    "Exact Min result/outer dimension projected without recalculation");
                Check(((DataGridView)Control(form,"dgvTools")).Rows.Cast<DataGridViewRow>().All(r=>Convert.ToString(r.Cells[4].Value).Contains("Chưa đủ dữ liệu")),
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
            var clear=options.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>Convert.ToString(r.Cells[1].Value)==BendCheckDisplayText.Collision(CollisionStatus.CLEAR));
            if(clear!=null)options.CurrentCell=clear.Cells[0];
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"simulation-sequence.png"));}
            File.WriteAllText(Path.Combine(output,"planner-ui-timing.json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{
                SimulationOnly=true,Bends=6,Tools=planner.Tools.Count,PlannerMilliseconds=planner.ElapsedMilliseconds,RenderToVisibleMilliseconds=visible,
                NativeCaptureMilliseconds=0,KernelQueries=planner.KernelQueries,DeviceDpi=form.DeviceDpi}));
            Console.WriteLine("PLANNER_UI 18x6 numeric; planner ms="+planner.ElapsedMilliseconds+" render ms="+visible);form.Close();
        }
    }
    private static void RenderHolder(string output)
    {
        using(var host=new Form())
        using(var pane=new BendToolEvidenceControl(Path.Combine(output,"PRIVATE-editor-recovery")))
        {
            host.ClientSize=new Size(1024,680);host.Controls.Add(pane);host.Show();
            pane.ShowTool(ToolProfileTests.CadPairUiLibrary().PlacementSources.Tools[0],null,()=>true);Application.DoEvents();
            var grid=(DataGridView)pane.Controls.Find("holderRoleEntities",true).Single();
            Check(grid.ClientSize.Width>300&&grid.ClientSize.Height>100,"Holder roles and original CAD have usable 1024x680 viewport");
            Check(pane.Controls.Find("applySelectedHolderRoles",true).Single().Visible&&pane.Controls.Find("saveHolderEditorDraft",true).Single().Visible,"Bulk confirmation and private recovery actions remain visible");
            using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"holder-editor-controlled-1024.png"));}
            host.Close();
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
            if(args.Length==2 && args[0]=="--render") { Render(args[1]); RenderPlanner(args[1]); RenderHolder(args[1]); }
            Console.WriteLine("RESULT " + groups + " groups; " + assertions + " assertions; 0 failures"); return 0;
        }
        catch(Exception ex) { Console.WriteLine("FAIL " + ex); return 1; }
    }
}
