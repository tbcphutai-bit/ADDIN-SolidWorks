using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private static CollisionMaterial2D EnvelopeMaterial(CollisionMaterial2D source, string frame = "SYNTHETIC_SECTION") =>
        new CollisionMaterial2D(frame, source.Components, "CONTROLLED_SYNTHETIC_SECTION", true);
    private static SheetEnvelopeCapture Volume(params AxialSolidPrimitive[] parts)
    {
        var model = CollisionModel(Rect("ANCHOR",0,0,1,1));
        return AnalyticSheetEnvelope.Controlled(model,model.Bends[0],parts);
    }
    private static AxialPrismaticSweep Prism(CollisionMaterial2D material, double lo=-10, double hi=10, ToolPoint velocity=default(ToolPoint)) =>
        new AxialPrismaticSweep(new AxialInterval(lo,hi),EnvelopeMaterial(material),velocity);
    private static AxialPairResult VolumeQuery(SheetEnvelopeCapture sheet, CollisionMaterial2D tool, double lo=-9, double hi=9,
        AxialCollisionPolicy policy=null, CancellationToken token=default(CancellationToken)) => AxialCollisionEngine.Evaluate(new AxialCollisionQuery(sheet,
            AxialToolOccupancy.Controlled(EnvelopeMaterial(tool,sheet.Binding.FrameId),new[]{new AxialInterval(lo,hi)}),CollisionPairKind.WorkpiecePunch,policy),token);
    private static void VolumeExpect(AxialPairResult r,CollisionStatus expected)
    {
        Check(r.Status==expected,"Axial "+r.Status+" expected "+expected+": "+string.Join(",",r.Reasons));
        if(expected==CollisionStatus.COLLISION)Check(r.Witness!=null&&r.Witness.CrossSectionInteriorMarginLowerMm>0&&r.Witness.AxialOccupancyMarginLowerMm>0,"Source-bound slice/material witness with axial overlap; no unproven 3D ball radius");
        if(expected==CollisionStatus.CLEAR)Check(r.CertifiedClearanceLowerMm>0||r.Reasons.Contains("CERTIFIED_ALL_AXIAL_INTERVALS_SEPARATED_OR_VOID"),"Interval coverage proof, never sample-only absence");
        if(expected==CollisionStatus.UNKNOWN)Check(r.CertifiedClearanceLowerMm==null&&r.Witness==null,"No usable geometric certificate on UNKNOWN");
    }
    private static void EnvelopeTests()
    {
        Test("envelope: exact prismatic L U Z keep Phase 3C geometric outcomes",()=> {
            var shapes=new[]{Material(Rect("SOLID",0,0,5,5)),Material(Polygon("L",new[]{P(0,0),P(5,0),P(5,1),P(1,1),P(1,5),P(0,5)})),
                Material(Polygon("U",new[]{P(0,0),P(6,0),P(6,6),P(5,6),P(5,1),P(1,1),P(1,6),P(0,6)})),
                Material(Polygon("Z",new[]{P(0,0),P(5,0),P(5,4),P(9,4),P(9,5),P(4,5),P(4,1),P(0,1)}))};
            foreach(var shape in shapes)foreach(var tool in new[]{Material(Rect("TOOL",.2,.2,.3,.3)),Material(Rect("TOOL",12,12,1,1))})
                VolumeExpect(VolumeQuery(Volume(Prism(shape)),tool),Query(shape,tool).Status);
        });
        Test("envelope: through hole and nested island retain solid/void roles",()=> {
            var outer=Rect("OUT",-5,-5,10,10);var hole=Disk("VOID",0,0,2);
            var sheet=Volume(Prism(Material(outer,hole)));var probe=Material(Rect("TOOL",-.2,-.2,.4,.4));
            VolumeExpect(VolumeQuery(sheet,probe),CollisionStatus.CLEAR);
            VolumeExpect(VolumeQuery(sheet,Material(Rect("TOOL",3,3,.5,.5))),CollisionStatus.COLLISION);
            var island=MaterialSet(new CollisionComponent2D(outer,new[]{hole}),new CollisionComponent2D(Rect("ISLAND",-.5,-.5,1,1)));
            VolumeExpect(VolumeQuery(Volume(Prism(island)),probe),CollisionStatus.COLLISION);
        });
        Test("envelope: a local tab between two CLEAR samples is a real collision",()=> {
            var sheet=Volume(Prism(Material(Rect("BASE",0,0,1,1))),Prism(Material(Rect("TAB",0,0,4,1)),-.1,.1));
            var tool=Material(Rect("TOOL",3,.2,.5,.5));
            foreach(double z in new[]{-9.0,9.0})Expect(Query(sheet.Slice(z).Material,EnvelopeMaterial(tool)),CollisionStatus.CLEAR);
            VolumeExpect(VolumeQuery(sheet.ObservationsOnly(new[]{-9.0,9.0}),tool),CollisionStatus.UNKNOWN);
            VolumeExpect(VolumeQuery(sheet,tool),CollisionStatus.COLLISION);
            Check(sheet.Certificate.ContinuousCoverageProven&&sheet.Certificate.Intervals.Count==3,"Complete source events, including a thin middle slab");
        });
        Test("envelope: exact observed slice may prove collision but never global CLEAR",()=> {
            var sheet=Volume(Prism(Material(Rect("BASE",0,0,4,4))));var only=sheet.ObservationsOnly(new[]{0.0});
            VolumeExpect(VolumeQuery(only,Material(Rect("TOOL",1,1,1,1))),CollisionStatus.COLLISION);
            VolumeExpect(VolumeQuery(only,Material(Rect("TOOL",8,8,1,1))),CollisionStatus.UNKNOWN);
            VolumeExpect(VolumeQuery(sheet.ObservationsOnly(new[]{-10.0}),Material(Rect("TOOL",1,1,1,1)),-11,-9),CollisionStatus.UNKNOWN);
        });
        Test("envelope: shallow pocket and staggered material depend on actual tool axial occupancy",()=> {
            var full=Material(Rect("BASE",0,0,10,10));var pocket=Material(Rect("OUT",0,0,10,10),Rect("POCKET",2,2,6,6));
            var sheet=Volume(Prism(full,-10,-2),Prism(pocket,-2,2),Prism(full,2,10));
            var tool=Material(Rect("TOOL",4,4,1,1));
            VolumeExpect(VolumeQuery(sheet,tool,-1,1),CollisionStatus.CLEAR);
            VolumeExpect(VolumeQuery(sheet,tool,3,4),CollisionStatus.COLLISION);
            VolumeExpect(VolumeQuery(sheet,tool,-4,4),CollisionStatus.COLLISION);
        });
        Test("envelope: slot relief notch and trimmed outlines are exact material not healed curves",()=> {
            var slotted=MaterialSet(new CollisionComponent2D(Rect("LOW",0,0,10,2)),new CollisionComponent2D(Rect("HIGH",0,4,10,2)));
            var notch=Material(Polygon("NOTCH",new[]{P(0,0),P(10,0),P(10,2),P(3,2),P(3,4),P(10,4),P(10,6),P(0,6)}));
            foreach(var shape in new[]{slotted,notch})
            {
                var sheet=Volume(Prism(shape));VolumeExpect(VolumeQuery(sheet,Material(Rect("TOOL",5,2.5,1,1))),CollisionStatus.CLEAR);
                VolumeExpect(VolumeQuery(sheet,Material(Rect("TOOL",5,1,1,.5))),CollisionStatus.COLLISION);
            }
        });
        Test("envelope: off-axis cylindrical bore uses a continuous sqrt bound",()=> {
            var sheet=Volume(new CrossAxisBoreBox(new AxialInterval(-4,4),-4,4,-4,4,0,0,2));
            var tool=Material(Rect("TOOL",-.1,-.1,.2,.2));
            VolumeExpect(VolumeQuery(sheet,tool,-.5,.5),CollisionStatus.CLEAR);
            VolumeExpect(VolumeQuery(sheet,tool,3,3.5),CollisionStatus.COLLISION);
            VolumeExpect(VolumeQuery(sheet,tool,-3.5,3.5),CollisionStatus.COLLISION);
            Check(sheet.Certificate.Method=="CompleteAnalyticSolidPartitionV1","Analytic volume, not voxel/sample proof");
        });
        Test("envelope: 3D clearance includes material just outside the tool axial range",()=> {
            var sheet=Volume(new CrossAxisBoreBox(new AxialInterval(-4,4),-4,4,-4,4,0,0,2));
            var r=VolumeQuery(sheet,Material(Rect("TOOL",-.1,.8,.2,.1)),.8,.9);VolumeExpect(r,CollisionStatus.CLEAR);
            double exactDistance=2-Math.Sqrt(.9*.9+.9*.9);
            Check(r.CertifiedClearanceLowerMm<=exactDistance,"An XY slice gap cannot overstate the true radial solid distance");
        });
        Test("envelope: continuous translated ARC/holes use a full displacement disk",()=> {
            var shape=Material(Disk("OUT",0,0,3),Disk("VOID",0,0,1));
            var sheet=Volume(Prism(shape,-2,2,P(.5,.2)));
            VolumeExpect(VolumeQuery(sheet,Material(Rect("TOOL",12,12,1,1)),-1,1),CollisionStatus.CLEAR);
            VolumeExpect(VolumeQuery(sheet,Material(Rect("TOOL",2,.1,.3,.3)),-1,1),CollisionStatus.COLLISION);
            // Unproven sample-only paths still cannot turn an empty center
            // section inside the moving hole into a whole-volume CLEAR.
            VolumeExpect(VolumeQuery(sheet.ObservationsOnly(new[]{0.0}),Material(Rect("TOOL",-.1,-.1,.2,.2)),-1,1),CollisionStatus.UNKNOWN);
        });
        Test("envelope: axial separation cap contact and tiny gaps have distinct outcomes",()=> {
            var sheet=Volume(Prism(Material(Rect("SHEET",0,0,4,4)),-1,2));var tool=Material(Rect("TOOL",1,1,1,1));
            VolumeExpect(VolumeQuery(sheet,tool,4,5),CollisionStatus.CLEAR);
            VolumeExpect(VolumeQuery(sheet,tool,2,3),CollisionStatus.UNKNOWN);
            VolumeExpect(VolumeQuery(sheet,tool,2+1e-6,3),CollisionStatus.UNKNOWN);
            VolumeExpect(VolumeQuery(sheet,tool,2+2e-5,3),CollisionStatus.CLEAR);
        });
        Test("envelope: missing tool/holder length frame and coverage cannot become CLEAR",()=> {
            var sheet=Volume(Prism(Material(Rect("SHEET",0,0,1,1))));var material=EnvelopeMaterial(Material(Rect("TOOL",8,8,1,1)));
            VolumeExpect(AxialCollisionEngine.Evaluate(new AxialCollisionQuery(sheet,AxialToolOccupancy.Controlled(material,new AxialInterval[0]),CollisionPairKind.WorkpieceHolder)),CollisionStatus.UNKNOWN);
            VolumeExpect(AxialCollisionEngine.Evaluate(new AxialCollisionQuery(sheet,AxialToolOccupancy.Controlled(EnvelopeMaterial(material,"WRONG_FRAME"),new[]{new AxialInterval(-1,1)}),CollisionPairKind.WorkpiecePunch)),CollisionStatus.UNKNOWN);
            var badSource=Volume(new AxialPrismaticSweep(new AxialInterval(-1,1),EnvelopeMaterial(material,"WRONG_SOURCE_FRAME")));
            Check(!badSource.Certificate.ContinuousCoverageProven&&badSource.Reasons.Contains("FRAME_MISMATCH"),"A source section cannot be silently relabelled into the bend frame");
        });
        Test("envelope: stale sources cancel cell time and work budgets hide certificates",()=> {
            var sheet=Volume(Prism(Material(Rect("SHEET",0,0,1,1))));var tool=Material(Rect("TOOL",8,8,1,1));
            var clear=VolumeQuery(sheet,tool);VolumeExpect(clear,CollisionStatus.CLEAR);sheet.Binding.Model.Lease.Invalidate();VolumeExpect(clear,CollisionStatus.UNKNOWN);
            sheet=Volume(Prism(Material(Rect("SHEET",0,0,1,1))));
            VolumeExpect(VolumeQuery(sheet,tool,token:new CancellationToken(true)),CollisionStatus.UNKNOWN);
            var split=Volume(Prism(Material(Rect("A",0,0,1,1)),-10,0),Prism(Material(Rect("B",0,0,1,1)),0,10));
            var limited=VolumeQuery(split,tool,policy:new AxialCollisionPolicy(maximumCells:1));VolumeExpect(limited,CollisionStatus.UNKNOWN);
            Check(limited.Reasons.Contains("AXIAL_CELL_BUDGET_EXCEEDED"),"Global interval budget, not silently partial CLEAR");
            var occupancy=AxialToolOccupancy.Controlled(EnvelopeMaterial(Material(Rect("HIT",.2,.2,.2,.2))),new[]{new AxialInterval(-9,9)});
            foreach(var source in new[]{sheet,sheet.ObservationsOnly(new[]{0.0})})
            {
                var timed=AxialCollisionEngine.Evaluate(new AxialCollisionQuery(source,occupancy,CollisionPairKind.WorkpiecePunch,
                    new AxialCollisionPolicy(maximumMilliseconds:5),consumeKernelQuery:()=>Thread.Sleep(15)));
                VolumeExpect(timed,CollisionStatus.UNKNOWN);
                Check(timed.Reasons.Contains("AXIAL_TIME_BUDGET_EXCEEDED"),"Slice-interior work cannot publish a collision after exhausting the pair time budget");
            }
        });
        Test("envelope: independent analytic bore solid reference checks real 3D points",()=> {
            var sheet=Volume(new CrossAxisBoreBox(new AxialInterval(-4,4),-4,4,-4,4,0,0,2));var random=new Random(140031);
            for(int i=0;i<60;i++)
            {
                double y=random.NextDouble()*7-3.5,z=random.NextDouble()*7-3.5;
                double radial=Math.Sqrt(y*y+z*z);if(Math.Abs(radial-2)<.1)continue;
                var r=VolumeQuery(sheet,Material(Rect("T",-.01,y-.01,.02,.02)),z-.01,z+.01);
                VolumeExpect(r,radial>2?CollisionStatus.COLLISION:CollisionStatus.CLEAR);
            }
        });
        NativeEnvelopeTests();
        EnvelopeWorkerTests();
    }
}
