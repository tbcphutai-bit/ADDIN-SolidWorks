using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private static string ProcessHash(string value) => ToolEvidenceCanonical.Hash("CONTROLLED_PROCESS_NOT_COMPANY_APPROVAL", value);
    private static ProcessToolPose ProcessTool(PlacementBendSnapshot bend, CollisionMaterial2D shape, ProcessMotionLaw motion = null,
        double lo = -4, double hi = 4, string source = "SOFTWARE_SETUP") => new ProcessToolPose(AxialToolOccupancy.Controlled(
            EnvelopeMaterial(shape, bend.Frame.FrameId), new[] { new AxialInterval(lo, hi) }), motion ?? new ProcessMotionLaw(), ProcessHash(source));
    private static ProcessSheetPart ProcessPanel(PlacementModelCapture model, PlacementBendSnapshot bend, string id, CollisionMaterial2D shape,
        ProcessMotionLaw law, ProcessGeometryFidelity fidelity = ProcessGeometryFidelity.ExactGeometricSurrogate) => ProcessSheetPart.ExplicitPrism(id,
            model, bend, EnvelopeMaterial(shape, bend.Frame.FrameId), new AxialInterval(-5, 5), law, fidelity);
    private static ProcessSheetPart[] MovingPanels(PlacementModelCapture model, PlacementBendSnapshot bend, ProcessMotionLaw law,
        ProcessGeometryFidelity zone = ProcessGeometryFidelity.CertifiedEnclosureOnly) => new[] {
        ProcessPanel(model,bend,"FIXED",Material(Rect("FIXED",-8,-.25,7,.5)),new ProcessMotionLaw()),
        ProcessPanel(model,bend,"MOVING",Material(Rect("MOVING",1,-.25,7,.5)),law),
        // Superset of a declared joint-zone surrogate constrained within this
        // disk throughout rotation. No plastic mechanics is claimed. It can
        // prove separation, but cannot itself supply an interference witness.
        ProcessPanel(model,bend,"BOUNDED_JOINT",Material(Disk("ZONE",0,0,1.1)),new ProcessMotionLaw(),zone) };
    private static BendProcessPlan OneMotion(CollisionMaterial2D obstacle, out BendMotionSegment segment, out PlacementModelCapture model,
        double a0 = 0, double a1 = Math.PI / 2, CollisionMaterial2D holder = null, ProcessMotionLaw panelLaw = null,
        ProcessGeometryFidelity zone = ProcessGeometryFidelity.CertifiedEnclosureOnly)
    {
        model = CollisionModel(Rect("ANCHOR",0,0,1,1)); var bend = model.Bends[0];
        var parts = MovingPanels(model, bend, panelLaw ?? new ProcessMotionLaw(a0, a1), zone);
        segment = new BendMotionSegment("FORM", BendProcessPhase.DuringBend, parts, ProcessTool(bend, obstacle),
            ProcessTool(bend, holder ?? Material(Rect("HOLDER",30,30,1,1))), ProcessHash("HINGE_CONSTRAINTS"));
        return BendProcessPlan.Controlled(parts[0].Source.Binding, CompanyToolIds.All[0], ProcessHash("EXPLICIT_PANEL_MOTION"), new[] { segment });
    }
    private static ProcessPairResult ProcessQuery(BendProcessPlan p, BendMotionSegment s, CollisionPairKind pair = CollisionPairKind.WorkpiecePunch,
        ProcessCollisionPolicy policy = null, CancellationToken token = default(CancellationToken), Action consume = null) =>
        ProcessCollisionEngine.Evaluate(p, s, pair, policy, token, consume: consume);
    private static void ProcessExpect(ProcessPairResult r, CollisionStatus status)
    {
        Check(r.Status == status, "Process " + r.Status + " expected " + status + ": " + string.Join(",", r.Reasons));
        Check(status != CollisionStatus.COLLISION || r.Witness?.Geometry?.CrossSectionInteriorMarginLowerMm > 0,
            "Only actual source states, never sweep overlap, prove unwanted collision");
        Check(status != CollisionStatus.UNKNOWN || r.Witness == null, "Unknown has no usable witness");
    }
    private static void ProcessTests()
    {
        ProcessPerformanceTests();
        Test("process: real two-panel hinge has a certified whole angular CLEAR", () => {
            var plan = OneMotion(Material(Disk("TOOL",12,12,.4)),out var s,out var model);
            ProcessExpect(ProcessQuery(plan,s),CollisionStatus.CLEAR);
            var state = plan.State(s,.37);
            Check(state.Sheet.Count==3&&state.Parameter==.37&&state.SimulationOnly&&state.Current&&state.ProcessPayloadSha256==plan.PayloadSha256,
                "State owns complete source/config/frame/fidelity/finite-tool provenance");
            Check(state.Sheet.All(g=>g.Binding.ModelSnapshotId==model.SnapshotId)&&state.Holder.Segments.Count==1,"No implicit infinite tools");
        });
        Test("process: sparse endpoints AND midpoint miss an interior moving-panel collision", () => {
            double angle=.29;var plan=OneMotion(Material(Disk("TOOL",5*Math.Cos(angle),5*Math.Sin(angle),.08)),out var s,out _);
            foreach(double u in new[]{0.0,.5,1.0})
                VolumeExpect(AxialCollisionEngine.Evaluate(new AxialCollisionQuery(s.Parts[1].At(u),s.Punch.At(u),CollisionPairKind.WorkpiecePunch)),CollisionStatus.CLEAR);
            var r=ProcessQuery(plan,s);ProcessExpect(r,CollisionStatus.COLLISION);
            Check(r.Witness.Parameter>0&&r.Witness.Parameter<.5&&r.Refinements>0,"Real interior-angle witness after adaptive interval refinement");
            ProcessExpect(ProcessQuery(plan,s,CollisionPairKind.WorkpieceHolder),CollisionStatus.CLEAR);
        });
        Test("process: independent Punch and Holder produce all four geometric combinations", () => {
            var hit=Material(Rect("HIT",4,-.1,.3,.2));var clear=Material(Rect("CLEAR",20,20,1,1));
            foreach(bool p in new[]{false,true})foreach(bool h in new[]{false,true})
            {
                var plan=OneMotion(p?hit:clear,out var s,out _,holder:h?hit:clear);
                ProcessExpect(ProcessQuery(plan,s),p?CollisionStatus.COLLISION:CollisionStatus.CLEAR);
                ProcessExpect(ProcessQuery(plan,s,CollisionPairKind.WorkpieceHolder),h?CollisionStatus.COLLISION:CollisionStatus.CLEAR);
            }
        });
        Test("process: bounded joint separation works; missing or interfering enclosure stays unknown", () => {
            var plan=OneMotion(Material(Rect("CLEAR",20,20,1,1)),out var s,out _,zone:ProcessGeometryFidelity.MissingBendZoneBound);
            var missing=ProcessQuery(plan,s);ProcessExpect(missing,CollisionStatus.UNKNOWN);Check(missing.Reasons.Contains("UNKNOWN_BEND_ZONE_ENCLOSURE"),"No deformation inferred");
            plan=OneMotion(Material(Disk("ZONE_PROBE",0,0,.1)),out s,out _,a1:0);
            ProcessExpect(ProcessQuery(plan,s),CollisionStatus.UNKNOWN);
        });
        Test("process: complete load pre forming end unload runs without cloning final CAD", () => {
            var model=CollisionModel(Rect("ANCHOR",0,0,1,1));var bend=model.Bends[0];var segments=new List<BendMotionSegment>();
            foreach(BendProcessPhase phase in Enum.GetValues(typeof(BendProcessPhase)))
            {
                double start=phase<BendProcessPhase.DuringBend?0:phase==BendProcessPhase.DuringBend?0:Math.PI/2;
                double end=phase<=BendProcessPhase.PreBend?0:Math.PI/2;
                var parts=MovingPanels(model,bend,new ProcessMotionLaw(start,end));
                if(phase==BendProcessPhase.ApproachLoad||phase==BendProcessPhase.RetractUnload)
                {
                    var from=phase==BendProcessPhase.ApproachLoad?P(0,-12):P(0,0);var to=phase==BendProcessPhase.ApproachLoad?P(0,0):P(0,12);
                    parts=parts.Select(p=>ProcessPanel(model,bend,p.Id,p.ExactPrismMaterial,new ProcessMotionLaw(start,end,startTranslation:from,endTranslation:to),p.Fidelity)).ToArray();
                }
                segments.Add(new BendMotionSegment(phase.ToString(),phase,parts,ProcessTool(bend,Material(Disk("PUNCH",30,30,1))),
                    ProcessTool(bend,Material(Disk("HOLDER",40,40,1))),ProcessHash("EXPLICIT_COMPLETE_PATH")));
            }
            // Fixed/zone angles must stay zero; only the designated moving
            // panel rotates. Loading and retracting use the same final poses.
            var fixedSegments=new List<BendMotionSegment>();
            foreach(var s in segments)
            {
                var parts=s.Parts.Select(p=>p.Id=="MOVING"?p:ProcessPanel(model,bend,p.Id,p.ExactPrismMaterial,
                    new ProcessMotionLaw(startTranslation:p.Motion.StartTranslationMm,endTranslation:p.Motion.EndTranslationMm),p.Fidelity)).ToArray();
                fixedSegments.Add(new BendMotionSegment(s.Id,s.Phase,parts,s.Punch,s.Holder,s.ConstraintsSourceSha256));
            }
            var plan=BendProcessPlan.Controlled(fixedSegments[0].Parts[0].Source.Binding,CompanyToolIds.All[0],ProcessHash("COMPLETE"),fixedSegments);
            Check(plan.InputReasons.Count==0,string.Join(",",plan.InputReasons));
            foreach(var s in plan.Segments)foreach(CollisionPairKind pair in Enum.GetValues(typeof(CollisionPairKind)))ProcessExpect(ProcessQuery(plan,s,pair),CollisionStatus.CLEAR);
            var row=new ProcessCandidateResult(plan.ToolId,bend.FeatureId,plan.Segments.SelectMany(s=>new[]{ProcessQuery(plan,s),ProcessQuery(plan,s,CollisionPairKind.WorkpieceHolder)}),true);
            Check(row.ProcessResult==CollisionStatus.CLEAR&&row.UnknownStates.Count==0&&row.ManufacturingStatus=="CHUA_XAC_MINH"&&row.WinnerToolId==null,"Modeled path proof never becomes manufacturing PASS");
        });
        Test("process: approach and retract collisions persist despite a clear forming end", () => {
            var model=CollisionModel(Rect("ANCHOR",0,0,1,1));var bend=model.Bends[0];var shape=Material(Rect("PANEL",0,0,1,1));
            foreach(var phase in new[]{BendProcessPhase.ApproachLoad,BendProcessPhase.RetractUnload})
            {
                var part=ProcessPanel(model,bend,"PANEL",shape,new ProcessMotionLaw(startTranslation:P(-4,0),endTranslation:P(4,0)));
                var s=new BendMotionSegment("LOAD_OR_UNLOAD",phase,new[]{part},ProcessTool(bend,Material(Rect("PUNCH",.2,.2,.3,.3))),ProcessTool(bend,Material(Rect("HOLDER",20,20,1,1))),ProcessHash("TRANSLATION"));
                var plan=BendProcessPlan.Controlled(part.Source.Binding,CompanyToolIds.All[0],ProcessHash("PATH"),new[]{s});
                foreach(double u in new[]{0.0,1.0})VolumeExpect(AxialCollisionEngine.Evaluate(new AxialCollisionQuery(part.At(u),s.Punch.At(u),CollisionPairKind.WorkpiecePunch)),CollisionStatus.CLEAR);
                ProcessExpect(ProcessQuery(plan,s),CollisionStatus.COLLISION);
            }
        });
        Test("process: explicit surface contact is allowed only at its verified planar zone", () => {
            var model=CollisionModel(Rect("ANCHOR",0,0,1,1));var b=model.Bends[0];var part=ProcessPanel(model,b,"SHEET",Material(Rect("SHEET",0,0,4,1)),new ProcessMotionLaw());
            var punch=ProcessTool(b,Material(Rect("PUNCH",1,-2,2,2)));var holder=ProcessTool(b,Material(Rect("HOLDER",20,20,1,1)));
            Func<PlanarFormingContact[],BendProcessPhase,BendProcessPlan> planFor=(contacts,phase)=>BendProcessPlan.Controlled(part.Source.Binding,CompanyToolIds.All[0],ProcessHash("CONTACT_PLAN"),
                new[]{new BendMotionSegment("CONTACT",phase,new[]{part},punch,holder,ProcessHash("NON_PENETRATING_SUPPORT_CONSTRAINT"),contacts)});
            var zone=new PlanarFormingContact(part.Id,part.Source.GeometrySha256,punch.SetupSourceSha256,ProcessHash("CONTACT_EVIDENCE"),BendProcessPhase.FormingEnd,false,true,0,0,4,new AxialInterval(-4,4));
            var p=planFor(new[]{zone},BendProcessPhase.FormingEnd);var r=ProcessQuery(p,p.Segments[0]);ProcessExpect(r,CollisionStatus.CLEAR);
            Check(r.ExpectedContact&&r.Reasons.Contains("EXPECTED_CONTACT"),"Expected surface contact is diagnostic, no penetrations ignored");
            foreach(var zones in new[]{new PlanarFormingContact[0],new[]{new PlanarFormingContact(part.Id,part.Source.GeometrySha256,punch.SetupSourceSha256,ProcessHash("CONTACT_EVIDENCE"),
                BendProcessPhase.FormingEnd,false,true,0,1,2,new AxialInterval(-4,4))}})
            { p=planFor(zones,BendProcessPhase.FormingEnd);ProcessExpect(ProcessQuery(p,p.Segments[0]),CollisionStatus.UNKNOWN); }
            p=planFor(new[]{zone},BendProcessPhase.PreBend);ProcessExpect(ProcessQuery(p,p.Segments[0]),CollisionStatus.UNKNOWN);
            var penetrating=ProcessTool(b,Material(Rect("PUNCH",1,-2,2,2.2)));
            var s=new BendMotionSegment("OUTSIDE_CONTACT",BendProcessPhase.FormingEnd,new[]{part},penetrating,punch,ProcessHash("CONSTRAINT"),new[]{zone});
            p=BendProcessPlan.Controlled(part.Source.Binding,CompanyToolIds.All[0],ProcessHash("PATH"),new[]{s});
            ProcessExpect(ProcessQuery(p,s),CollisionStatus.COLLISION);ProcessExpect(ProcessQuery(p,s,CollisionPairKind.WorkpieceHolder),CollisionStatus.UNKNOWN);
            Check(!ProcessQuery(p,s,CollisionPairKind.WorkpieceHolder).ExpectedContact,"Holder surface contact is never exempted");
        });
        Test("process: finite axial gaps moving ends and cap contact retain uncertainty", () => {
            var p=OneMotion(Material(Rect("PUNCH",4,-.1,.3,.2)),out var s,out _,a1:0);
            var far=new ProcessToolPose(AxialToolOccupancy.Controlled(s.Punch.Source.Material,new[]{new AxialInterval(8,10)}),new ProcessMotionLaw(),ProcessHash("FAR"));
            var seg=new BendMotionSegment("AXIAL_GAP",s.Phase,s.Parts,far,s.Holder,s.ConstraintsSourceSha256);
            p=BendProcessPlan.Controlled(p.Binding,p.ToolId,p.SourceSha256,new[]{seg});ProcessExpect(ProcessQuery(p,seg),CollisionStatus.CLEAR);
            var moving=new ProcessToolPose(far.Source,new ProcessMotionLaw(startAxial:0,endAxial:-12),far.SetupSourceSha256);
            seg=new BendMotionSegment("AXIAL_MOVE",s.Phase,s.Parts,moving,s.Holder,s.ConstraintsSourceSha256);
            p=BendProcessPlan.Controlled(p.Binding,p.ToolId,p.SourceSha256,new[]{seg});ProcessExpect(ProcessQuery(p,seg),CollisionStatus.COLLISION);
            var cap=new ProcessToolPose(AxialToolOccupancy.Controlled(s.Punch.Source.Material,new[]{new AxialInterval(5,7)}),new ProcessMotionLaw(),ProcessHash("CAP"));
            seg=new BendMotionSegment("CAP",s.Phase,s.Parts,cap,s.Holder,s.ConstraintsSourceSha256);p=BendProcessPlan.Controlled(p.Binding,p.ToolId,p.SourceSha256,new[]{seg});
            ProcessExpect(ProcessQuery(p,seg),CollisionStatus.UNKNOWN);
        });
        Test("process: uncertainty cannot create CLEAR; extra material cannot hide a witnessed hit", () => {
            var a=Material(Rect("PUNCH",8.001,-.2,.3,.4));var p=OneMotion(a,out var s,out _,a1:0);
            ProcessExpect(ProcessQuery(p,s),CollisionStatus.CLEAR);
            p=OneMotion(a,out s,out _,panelLaw:new ProcessMotionLaw(positionError:.005));ProcessExpect(ProcessQuery(p,s),CollisionStatus.UNKNOWN);
            double angle=.29;var hit=Disk("HIT",5*Math.Cos(angle),5*Math.Sin(angle),.1);
            foreach(var obstacle in new[]{Material(hit),MaterialSet(new CollisionComponent2D(hit),new CollisionComponent2D(Rect("ADDED",30,30,1,1)))})
            { p=OneMotion(obstacle,out s,out _);ProcessExpect(ProcessQuery(p,s),CollisionStatus.COLLISION); }
        });
        Test("process: model process and tool leases invalidate held certificates", () => {
            var p=OneMotion(Material(Rect("CLEAR",20,20,1,1)),out var s,out var model);var r=ProcessQuery(p,s);ProcessExpect(r,CollisionStatus.CLEAR);
            model.Lease.Invalidate();ProcessExpect(r,CollisionStatus.UNKNOWN);
            p=OneMotion(Material(Rect("CLEAR",20,20,1,1)),out s,out _);r=ProcessQuery(p,s);p.Invalidate();ProcessExpect(r,CollisionStatus.UNKNOWN);
        });
        Test("process: cancellation and interval time budgets cannot publish partial CLEAR", () => {
            var p=OneMotion(Material(Disk("TOOL",5*Math.Cos(.29),5*Math.Sin(.29),.08)),out var s,out _);
            ProcessExpect(ProcessQuery(p,s,token:new CancellationToken(true)),CollisionStatus.UNKNOWN);
            var limited=ProcessQuery(p,s,policy:new ProcessCollisionPolicy(intervals:1));ProcessExpect(limited,CollisionStatus.UNKNOWN);
            Check(limited.Reasons.Contains("PROCESS_INTERVAL_BUDGET_EXCEEDED"),"No sampling fallback at resource exhaustion");
            var timed=ProcessQuery(p,s,policy:new ProcessCollisionPolicy(milliseconds:5),consume:()=>Thread.Sleep(10));ProcessExpect(timed,CollisionStatus.UNKNOWN);
            Check(timed.Reasons.Contains("PROCESS_TIME_BUDGET_EXCEEDED"),"Time guard runs before and after actual kernel calls");
        });
        ProcessAdditionalTests();
        ProcessWorkerTests();
    }
    private static void ProcessWorkerTests()
    {
        Test("process worker: all 18 x 3/6 bends remain deterministic independent pairs",()=> {
            foreach(int n in new[]{3,6})
            {
                var model=CollisionModel(Rect("ANCHOR",0,0,1,1),n);var plans=new List<BendProcessPlan>();
                foreach(var b in model.Bends)for(int i=0;i<18;i++)
                {
                    var parts=MovingPanels(model,b,new ProcessMotionLaw(0,Math.PI/2));
                    var hit=Material(Disk("TOOL",5*Math.Cos(.29),5*Math.Sin(.29),.08));var clear=Material(Rect("FAR",30,30,1,1));
                    var segment=new BendMotionSegment("FORM",BendProcessPhase.DuringBend,parts,ProcessTool(b,i%3==0?hit:clear),
                        ProcessTool(b,i%3==1?hit:clear),ProcessHash("ALL_18_CONTROLLED"));
                    plans.Add(BendProcessPlan.Controlled(parts[0].Source.Binding,CompanyToolIds.All[i],ProcessHash("CONTROLLED_MOTION"),new[]{segment}));
                }
                var library=new ToolProfileLibrarySnapshot {Tools=new CompanyToolProfile[0],PlacementSources=PlacementSourceSnapshot.Missing("TEST_ONLY")};
                var request=new ProcessBatchRequest(model,library,plans);var reports=new List<CollisionBatchProgress>();
                using(var worker=new ProcessBatchWorker())
                {
                    int caller=Thread.CurrentThread.ManagedThreadId;var clock=Stopwatch.StartNew();
                    var task=worker.RunAsync(request,new ProcessBatchOptions(workers:2,milliseconds:120000),progress:new DirectProgress(reports.Add));double returned=clock.Elapsed.TotalMilliseconds;
                    var result=task.GetAwaiter().GetResult();Check(result.Current&&ReferenceEquals(result,worker.Published),"Atomic managed publication");
                    Check(result.Candidates.Count==18*n&&result.MaximumConcurrency<=2&&result.WorkerThreadId!=caller,"Bounded managed worker, complete slots");
                    Check(reports.Select(r=>r.Completed).SequenceEqual(Enumerable.Range(1,18*n)),"Monotone progress");
                    foreach(var row in result.Candidates)
                    {
                        Check(row.PairSegments.Count==10&&row.UnknownPhases.Count==4&&row.ManufacturingStatus=="CHUA_XAC_MINH"&&row.WinnerToolId==null,"Missing load/pre/end/unload retained");
                        int index=CompanyToolIds.All.ToList().IndexOf(row.ToolId);
                        var form=row.PairSegments.Where(r=>r.Phase==BendProcessPhase.DuringBend).ToArray();
                        Check(form[0].Status==(index%3==0?CollisionStatus.COLLISION:CollisionStatus.CLEAR)&&form[1].Status==(index%3==1?CollisionStatus.COLLISION:CollisionStatus.CLEAR),"Punch/Holder independent nontrivial outcomes");
                    }
                    var serial=ProcessBatchWorker.RunSynchronously(request,new ProcessBatchOptions(workers:1,milliseconds:120000));
                    Check(serial.Candidates.SelectMany(r=>r.PairSegments.Select(q=>q.Status)).SequenceEqual(result.Candidates.SelectMany(r=>r.PairSegments.Select(q=>q.Status))),
                        "Parallel scheduling cannot change geometric results; serial="+serial.WorkerMilliseconds+"ms; reasons="+
                        string.Join(",",serial.Candidates.SelectMany(c=>c.PairSegments.Where(r=>r.Status==CollisionStatus.UNKNOWN&&r.Phase==BendProcessPhase.DuringBend).SelectMany(r=>r.Reasons))));
                    Console.WriteLine("PROCESS_PERF "+Json.Serialize(new {Bends=n,Rows=result.Candidates.Count,CallerReturnMs=returned,result.WorkerMilliseconds,
                        SerialMilliseconds=serial.WorkerMilliseconds,result.KernelQueries,result.IntervalRefinements,result.MaximumConcurrency}));
                }
            }
        });
        Test("process worker: final geometry alone yields UNKNOWN all required phases",()=> {
            var model=CollisionModel(Rect("SHEET",0,0,1,1),6);var library=new ToolProfileLibrarySnapshot {Tools=new CompanyToolProfile[0]};
            var result=ProcessBatchWorker.RunSynchronously(new ProcessBatchRequest(model,library,observedFinalGeometry:BatchEnvelopes(model)));
            Check(result.Candidates.Count==108&&result.Candidates.All(r=>r.ProcessResult==CollisionStatus.UNKNOWN&&r.UnknownPhases.Count==5),"No final-state cloning");
            Check(result.KernelQueries==0&&result.Candidates.All(r=>r.PairSegments.All(p=>p.Reasons.Contains("NO_STATE_INFERENCE_FROM_FINAL_MODEL"))),"Explicit missing process reasons");
        });
    }
    private static int ProcessOracle(string[] args)
    {
        if(args.Length!=2)return 2;Directory.CreateDirectory(args[1]);var cases=new List<object>();var rng=new Random(210041);
        for(int i=0;i<182;i++)
        {
            double radius=i>=180?.1:.02+rng.NextDouble()*.35;
            double angle=i>=180?(i==180?-Math.Asin((.25+radius)/5):Math.PI/2+Math.Asin((.25+radius)/5)):
                -.3+rng.NextDouble()*(Math.PI/2+.6);
            var p=OneMotion(Material(Disk("ORACLE_TOOL",5*Math.Cos(angle),5*Math.Sin(angle),radius)),out var s,out _);
            var r=ProcessQuery(p,s);cases.Add(new {Index=i,Angle=angle,Radius=radius,Status=r.Status.ToString(),
                Witness=r.Witness==null?null:new {r.Witness.Parameter,r.Witness.Geometry.PointMm.X,r.Witness.Geometry.PointMm.Y,r.Witness.Geometry.AxialMm},r.Refinements,r.KernelQueries});
        }
        File.WriteAllText(Path.Combine(args[1],"process-oracle.json"),Json.Serialize(new {Bar=new[]{1.0,8.0,-.25,.25},CenterRadius=5,StartAngle=0,EndAngle=Math.PI/2,Cases=cases}));
        Console.WriteLine("PROCESS_ORACLE_EXPORTED "+cases.Count);return 0;
    }
}
