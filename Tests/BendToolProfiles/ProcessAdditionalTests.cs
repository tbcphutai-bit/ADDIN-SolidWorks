using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private static BendProcessPlan TwoBendFixture(bool outerFirst)
    {
        var m=CollisionModel(Rect("ANCHOR",0,0,1,1),2);var b=m.Bends[0];
        double right=Math.PI/2;var segments=new List<BendMotionSegment>();
        for(int stage=0;stage<3;stage++)
        {
            ProcessMotionLaw middle,outer,secondZone;
            if(!outerFirst)
            {
                middle=stage==0?new ProcessMotionLaw(0,right):new ProcessMotionLaw(right,right);
                outer=stage==0?new ProcessMotionLaw(0,right):new ProcessMotionLaw(right,stage==1?Math.PI:Math.PI,pivot:P(5,0),
                    startTranslation:P(-5,5),endTranslation:P(-5,5));
                if(stage==2)outer=new ProcessMotionLaw(Math.PI,Math.PI,pivot:P(5,0),startTranslation:P(-5,5),endTranslation:P(-5,5));
                secondZone=middle;
            }
            else
            {
                middle=stage==0?new ProcessMotionLaw():stage==1?new ProcessMotionLaw(0,right):new ProcessMotionLaw(right,right);
                outer=stage==0?new ProcessMotionLaw(0,right,pivot:P(5,0)):stage==1?
                    new ProcessMotionLaw(right,Math.PI,pivot:P(5,5),startTranslation:P(-5,-5),endTranslation:P(-5,-5)):
                    new ProcessMotionLaw(Math.PI,Math.PI,pivot:P(5,5),startTranslation:P(-5,-5),endTranslation:P(-5,-5));
                secondZone=middle;
            }
            var parts=new[]{ProcessPanel(m,b,"BASE",Material(Rect("BASE",-8,-.2,7,.4)),new ProcessMotionLaw()),
                ProcessPanel(m,b,"MIDDLE",Material(Rect("MIDDLE",1,-.2,4,.4)),middle),
                ProcessPanel(m,b,"OUTER",Material(Rect("OUTER",5,-.2,4,.4)),outer),
                ProcessPanel(m,b,"JOINT1",Material(Disk("JOINT1",0,0,1.1)),new ProcessMotionLaw(),ProcessGeometryFidelity.CertifiedEnclosureOnly),
                ProcessPanel(m,b,"JOINT2",Material(Disk("JOINT2",5,0,.5)),secondZone,ProcessGeometryFidelity.CertifiedEnclosureOnly)};
            double p=8/Math.Sqrt(2);var punch=ProcessTool(b,Material(Disk("PUNCH",p,p,.1)));
            segments.Add(new BendMotionSegment("STAGE"+stage,stage==2?BendProcessPhase.FormingEnd:BendProcessPhase.DuringBend,parts,punch,
                ProcessTool(b,Material(Rect("HOLDER",25,25,1,1))),ProcessHash("TWO_EXPLICIT_JOINT_AXES_AND_ORDER")));
        }
        return BendProcessPlan.Controlled(segments[0].Parts[0].Source.Binding,CompanyToolIds.All[0],ProcessHash(outerFirst?"ORDER_B_A":"ORDER_A_B"),segments);
    }
    private static void ProcessAdditionalTests()
    {
        Test("process: native transverse-cylinder source is reused for a declared rigid state law",()=> {
            var model=NativeBoreModel();var sheet=CompileNative(model);var bend=model.Bends[0];
            var part=new ProcessSheetPart("DECLARED_IN_PROCESS_BODY",sheet,new ProcessMotionLaw(0,.1));
            var tool=ProcessTool(bend,Material(Rect("IN_BORE",4.9,-.05,.2,.1)),lo:-.1,hi:.1);
            var s=new BendMotionSegment("REGISTERED_RIGID_MOTION",BendProcessPhase.ApproachLoad,new[]{part},tool,
                ProcessTool(bend,Material(Rect("HOLDER",30,30,1,1))),ProcessHash("CONTROLLED_SOURCE_ROLE_AND_MOTION"));
            var plan=BendProcessPlan.Controlled(sheet.Binding,CompanyToolIds.All[0],ProcessHash("EXPLICIT_NOT_INFERRED_NATIVE_LAW"),new[]{s});
            ProcessExpect(ProcessQuery(plan,s),CollisionStatus.CLEAR);
            Check(sheet.Certificate.Method=="CompleteOrthogonalPlaneCylinderSectionFamilyV1","Phase 3D.1 complete analytic source proof retained");
        });
        Test("process: two-bend L/Z surrogate changes outcome under explicit alternate ordering",()=> {
            var a=TwoBendFixture(false);var b=TwoBendFixture(true);
            Check(a.InputReasons.Count==0&&b.InputReasons.Count==0,"Both declared kinematic paths have joined source states: "+string.Join(",",a.InputReasons.Concat(b.InputReasons)));
            ProcessExpect(ProcessQuery(a,a.Segments[0]),CollisionStatus.COLLISION);
            foreach(var s in b.Segments)ProcessExpect(ProcessQuery(b,s),CollisionStatus.CLEAR);
            ProcessExpect(ProcessQuery(a,a.Segments.Last()),CollisionStatus.CLEAR);
            foreach(var plan in new[]{a,b})foreach(var s in plan.Segments)ProcessExpect(ProcessQuery(plan,s,CollisionPairKind.WorkpieceHolder),CollisionStatus.CLEAR);
            var pa=a.Segments.Last().Parts.Single(p=>p.Id=="OUTER").Motion.At(1).Apply(P(9,0));
            var pb=b.Segments.Last().Parts.Single(p=>p.Id=="OUTER").Motion.At(1).Apply(P(9,0));Point(pa,-4,5);Point(pb,-4,5);
            Check(a.ToolId==b.ToolId,"Same tool; no sequence ranking or winner selection");
        });
        Test("process: ARC tangency and retained NURBS bands cannot become CLEAR by sampling",()=> {
            var m=CollisionModel(Rect("ANCHOR",0,0,1,1));var b=m.Bends[0];
            var part=ProcessPanel(m,b,"ARC_PANEL",Material(Disk("ROUND",0,0,1)),new ProcessMotionLaw());
            foreach(double gap in new[]{0.0,2e-5})
            {
                var tool=ProcessTool(b,Material(Disk("PUNCH",2+gap,0,1)));
                var s=new BendMotionSegment("ARC_CONTACT",BendProcessPhase.DuringBend,new[]{part},tool,tool,ProcessHash("ARC"));
                var p=BendProcessPlan.Controlled(part.Source.Binding,CompanyToolIds.All[0],ProcessHash("PATH"),new[]{s});
                ProcessExpect(ProcessQuery(p,s),gap==0?CollisionStatus.UNKNOWN:CollisionStatus.CLEAR);
            }
            // This uses the existing real NURBS subdivision/band backend.
            using(var f=Reviews())
            {
                var spline=new ToolDxfEntity {Type="SPLINE",Spline=Spline(2,new[]{P(0,0),P(2,3),P(4,0)},new[]{0.0,0,0,1,1,1}),
                    Curves=new ToolCurve[0],UnsupportedReason="Spline backend required"};
                var punchSelection=HolderSelection(Square());var holderSelection=HolderSelection(spline,Entity(new[]{Segment(4,0,0,0)}));
                var definition=MaterialDefinition(punchSelection,holderSelection,"Solid","Solid");definition.Contours.RemoveAt(1);definition.Contours[0].EntityKeys=holderSelection.EntityKeys.ToArray();
                definition.Review=f.Sign("HolderMaterialRule",HolderObstacleSet.Payload(definition));var holderSet=ReadMaterial(f,punchSelection,holderSelection,definition);
                Check(holderSet.GeometrySupportedForCollision&&holderSet.RequiresConservativeBoundaryBand,"Existing rational SPLINE backend used");
                var holderMaterial=EnvelopeMaterial(MaterialFromHolder(holderSet),b.Frame.FrameId);
                Check(holderMaterial.Contours.Any(c=>c.UncertaintyMm>0),"Backend SPLINE uncertainty is present");
                var far=ProcessPanel(m,b,"FAR_PANEL",Material(Rect("PANEL",100,100,1,1)),new ProcessMotionLaw(0,.1));
                var holder=new ProcessToolPose(AxialToolOccupancy.Controlled(holderMaterial,new[]{new AxialInterval(-4,4)}),new ProcessMotionLaw(),ProcessHash("NURBS_SETUP"));
                var s=new BendMotionSegment("NURBS",BendProcessPhase.DuringBend,new[]{far},holder,holder,ProcessHash("PATH"));
                var p=BendProcessPlan.Controlled(far.Source.Binding,CompanyToolIds.All[0],ProcessHash("PATH"),new[]{s});ProcessExpect(ProcessQuery(p,s,CollisionPairKind.WorkpieceHolder),CollisionStatus.CLEAR);
                var near=ProcessPanel(m,b,"NEAR_SPLINE",Material(Rect("PANEL",1.9,1.5001,.2,.2)),new ProcessMotionLaw());
                s=new BendMotionSegment("NEAR_NURBS",BendProcessPhase.DuringBend,new[]{near},holder,holder,ProcessHash("PATH"));
                p=BendProcessPlan.Controlled(near.Source.Binding,CompanyToolIds.All[0],ProcessHash("PATH"),new[]{s});ProcessExpect(ProcessQuery(p,s,CollisionPairKind.WorkpieceHolder),CollisionStatus.UNKNOWN);
                var uncertainMaterial=new CollisionMaterial2D(b.Frame.FrameId,new[]{new CollisionComponent2D(Rect("BOUNDED_SPLINE_SURROGATE",1.001,0,1,1,.005))},"EXPLICIT_UNCERTAIN_SOURCE",true);
                var closePart=ProcessPanel(m,b,"CLOSE",Material(Rect("PANEL",0,0,1,1)),new ProcessMotionLaw());
                holder=new ProcessToolPose(AxialToolOccupancy.Controlled(uncertainMaterial,new[]{new AxialInterval(-4,4)}),new ProcessMotionLaw(),ProcessHash("BAND"));
                s=new BendMotionSegment("UNCERTAIN",BendProcessPhase.DuringBend,new[]{closePart},holder,holder,ProcessHash("PATH"));
                p=BendProcessPlan.Controlled(closePart.Source.Binding,CompanyToolIds.All[0],ProcessHash("PATH"),new[]{s});ProcessExpect(ProcessQuery(p,s),CollisionStatus.UNKNOWN);
            }
        });
        Test("process: wrong work frame observed-final role and discontinuous joins are UNKNOWN",()=> {
            var p=OneMotion(Material(Rect("FAR",20,20,1,1)),out var s,out _);
            var wrong=new ProcessToolPose(AxialToolOccupancy.Controlled(EnvelopeMaterial(Material(Rect("WRONG",20,20,1,1)),"UNREGISTERED"),new[]{new AxialInterval(-4,4)}),new ProcessMotionLaw(),ProcessHash("BAD_FRAME"));
            var seg=new BendMotionSegment("WRONG",s.Phase,s.Parts,wrong,s.Holder,s.ConstraintsSourceSha256);
            var bad=BendProcessPlan.Controlled(p.Binding,p.ToolId,p.SourceSha256,new[]{seg});ProcessExpect(ProcessQuery(bad,seg),CollisionStatus.UNKNOWN);
            var final=new ProcessSheetPart("FINAL_CAD",s.Parts[0].Source,new ProcessMotionLaw(),ProcessGeometryRole.FinalModelObserved);
            seg=new BendMotionSegment("FINAL",s.Phase,new[]{final},s.Punch,s.Holder,s.ConstraintsSourceSha256);
            bad=BendProcessPlan.Controlled(p.Binding,p.ToolId,p.SourceSha256,new[]{seg});ProcessExpect(ProcessQuery(bad,seg),CollisionStatus.UNKNOWN);
            bad=BendProcessPlan.Controlled(p.Binding,p.ToolId,p.SourceSha256,new[]{s,new BendMotionSegment("TELEPORT",BendProcessPhase.FormingEnd,
                s.Parts,s.Punch,s.Holder,s.ConstraintsSourceSha256)});ProcessExpect(ProcessQuery(bad,s),CollisionStatus.UNKNOWN);
            Check(bad.InputReasons.Contains("UNBOUND_PROCESS_STATE_TRANSITION"),"No unseen panel teleport between stage endpoints");
            p=OneMotion(Material(Rect("FAR",20,20,1,1)),out s,out _,a1:0);
            var changed=ProcessTool(s.Parts[0].Source.Binding.Model.Bends[0],Material(Rect("FAR",20,20,1,1)),lo:8,hi:10);
            var after=new BendMotionSegment("CHANGED_LENGTH",BendProcessPhase.FormingEnd,s.Parts,changed,s.Holder,s.ConstraintsSourceSha256);
            bad=BendProcessPlan.Controlled(p.Binding,p.ToolId,p.SourceSha256,new[]{s,after});
            ProcessExpect(ProcessQuery(bad,after),CollisionStatus.UNKNOWN);
            ProcessExpect(ProcessQuery(bad,after,CollisionPairKind.WorkpieceHolder),CollisionStatus.CLEAR);
            Check(ProcessQuery(bad,after).Reasons.Contains("UNKNOWN_MACHINE_MOTION_OR_TOOL_STATE_TRANSITION"),"Changing finite tool occupancy cannot be an unexamined state transition");
        });
        Test("process: tool axial gaps remain gaps and independent stale holder review is gated",()=> {
            var p=OneMotion(Material(Rect("HIT",4,-.1,.3,.2)),out var s,out _,a1:0);
            var gapTool=new ProcessToolPose(AxialToolOccupancy.Controlled(s.Punch.Source.Material,new[]{new AxialInterval(-10,-7),new AxialInterval(7,10)}),new ProcessMotionLaw(),ProcessHash("TWO_SEGMENTS"));
            var seg=new BendMotionSegment("GAPS",s.Phase,s.Parts,gapTool,gapTool,s.ConstraintsSourceSha256);
            p=BendProcessPlan.Controlled(p.Binding,p.ToolId,p.SourceSha256,new[]{seg});ProcessExpect(ProcessQuery(p,seg),CollisionStatus.CLEAR);
            bool current=true;var occupancy=new AxialToolOccupancy(s.Holder.Source.Material,s.Holder.Source.Segments,0,ProcessHash("HOLDER_REVIEW"),true,()=>current,null);
            var holder=new ProcessToolPose(occupancy,new ProcessMotionLaw(),ProcessHash("HOLDER"));
            seg=new BendMotionSegment("STALE_HOLDER",s.Phase,s.Parts,gapTool,holder,s.ConstraintsSourceSha256);p=BendProcessPlan.Controlled(p.Binding,p.ToolId,p.SourceSha256,new[]{seg});
            var r=ProcessQuery(p,seg,CollisionPairKind.WorkpieceHolder);ProcessExpect(r,CollisionStatus.CLEAR);current=false;ProcessExpect(r,CollisionStatus.UNKNOWN);
        });
        Test("process: coordinates do not verify a process review or physical clamp",()=> {
            var p=OneMotion(Material(Rect("FAR",20,20,1,1)),out var s,out _);
            var library=new ToolProfileLibrarySnapshot {Tools=new CompanyToolProfile[0]};
            var unreviewed=BendProcessPlan.Reviewed(p.Binding,p.ToolId,p.SourceSha256,p.Segments,library,null,null,null,CancellationToken.None);
            ProcessExpect(ProcessQuery(unreviewed,s),CollisionStatus.UNKNOWN);
            Check(unreviewed.InputReasons.Contains("UNVERIFIED_PROCESS_SOURCE_AND_CONSTRAINTS")&&unreviewed.InputReasons.Contains("UNVERIFIED_PHYSICAL_PUNCH_HOLDER_PAIR"),"Independent evidence contract, no verified-by-coordinates shortcut");
        });
        Test("process worker: supersession disposal cancellation budgets and callbacks preserve all rows",()=> {
            var model=CollisionModel(Rect("ANCHOR",0,0,1,1),3);var library=new ToolProfileLibrarySnapshot {Tools=new CompanyToolProfile[0]};
            var empty=new ProcessBatchRequest(model,library);
            using(var worker=new ProcessBatchWorker())using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
            {
                var first=worker.RunAsync(empty,new ProcessBatchOptions(workers:1),progress:new DirectProgress(r=>{if(r.Completed==1){entered.Set();release.Wait(10000);}}));
                Check(entered.Wait(10000),"First managed worker reached barrier");var second=worker.RunAsync(empty);
                release.Set();var old=first.GetAwaiter().GetResult();var latest=second.GetAwaiter().GetResult();
                Check(!old.Current&&latest.Current&&ReferenceEquals(worker.Published,latest),"Old generation cannot overwrite publication");
                worker.Dispose();Check(!latest.Current&&latest.Candidates.All(c=>c.ProcessResult==CollisionStatus.UNKNOWN),"Dispose invalidates held certificates");
            }
            foreach(var options in new[]{new ProcessBatchOptions(workers:0),new ProcessBatchOptions(milliseconds:1),new ProcessBatchOptions(kernelQueries:1)})
            {
                var result=ProcessBatchWorker.RunSynchronously(empty,options,new CancellationToken(true));
                Check(result.Candidates.Count==54&&result.Candidates.All(c=>c.ProcessResult==CollisionStatus.UNKNOWN),"No missing rows on abort/policy failure");
            }
            var callback=ProcessBatchWorker.RunSynchronously(empty,progress:new DirectProgress(r=>{throw new InvalidOperationException("listener");}));
            Check(callback.Current&&callback.ProgressDiagnostic=="PROGRESS_CALLBACK_FAILED:InvalidOperationException","Listener exception cannot corrupt worker");
            var plan=OneMotion(Material(Rect("FAR",20,20,1,1)),out _,out var captured);
            var budgeted=ProcessBatchWorker.RunSynchronously(new ProcessBatchRequest(captured,library,new[]{plan}),new ProcessBatchOptions(kernelQueries:1));
            Check(!budgeted.Current&&budgeted.Candidates.Count==18&&budgeted.Candidates.All(c=>c.ProcessResult==CollisionStatus.UNKNOWN),"Real numeric query exhaustion invalidates held partial results");
            Check(budgeted.Candidates.SelectMany(c=>c.PairSegments).Any(r=>r.Reasons.Contains("PROCESS_JOB_QUERY_BUDGET_EXCEEDED")),"Specific query exhaustion reason retained");
        });
    }
}
