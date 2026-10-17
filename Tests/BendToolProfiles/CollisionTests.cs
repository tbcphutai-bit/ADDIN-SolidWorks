using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private static CollisionContour2D Rect(string id, double x, double y, double width, double height, double band=0) =>
        Polygon(id,new[]{P(x,y),P(x+width,y),P(x+width,y+height),P(x,y+height)},band);
    private static CollisionContour2D Polygon(string id, ToolPoint[] points, double band=0) => new CollisionContour2D(id,
        points.Select((p,i)=>ToolCurve.Line(p,points[(i+1)%points.Length],new ToolTransform())),band);
    private static CollisionContour2D Disk(string id, double x, double y, double radius, double band=0, bool clockwise=false) =>
        new CollisionContour2D(id,new[]{ToolCurve.Arc(P(x+radius,y),P(x+radius,y),P(x,y),radius,(clockwise?-1:1)*2*Math.PI,new ToolTransform())},band);
    private static CollisionContour2D ArcCap(string id,double radius,double sweep)
    {
        var start=P(radius,0);var end=P(radius*Math.Cos(sweep),radius*Math.Sin(sweep));
        return new CollisionContour2D(id,new[]{ToolCurve.Arc(start,end,P(0,0),radius,sweep,new ToolTransform()),ToolCurve.Line(end,start,new ToolTransform())});
    }
    private static CollisionMaterial2D Material(CollisionContour2D outer, params CollisionContour2D[] holes) =>
        MaterialSet(new CollisionComponent2D(outer,holes));
    private static CollisionMaterial2D MaterialSet(params CollisionComponent2D[] components) =>
        new CollisionMaterial2D("SYNTHETIC_WORK_MM",components,"CONTROLLED_SYNTHETIC_GEOMETRY",true);
    private static CollisionResult2D Query(CollisionMaterial2D a, CollisionMaterial2D b, CollisionPolicy2D policy=null, Func<bool> current=null,
        CancellationToken token=default(CancellationToken)) => CollisionEngine2D.Evaluate(
            new CollisionQuery2D(CollisionPairKind.WorkpiecePunch,a,b,"CONTROLLED_OBSERVED_POSE",policy,current),token);
    private static void Expect(CollisionResult2D r, CollisionStatus status)
    {
        Check(r.Status==status,"Collision result "+r.Status+" expected "+status+"; reasons="+string.Join(",",r.Reasons)+"; work="+r.WorkUnits);
        if(status==CollisionStatus.CLEAR) Check(r.CertifiedClearanceLowerMm>r.ToleranceMm&&r.Witness==null,"CLEAR has a positive certificate, not a collision witness");
        if(status==CollisionStatus.COLLISION) Check(r.Witness!=null&&r.Witness.InteriorMarginLowerMm>r.ToleranceMm&&r.CertifiedClearanceLowerMm==null,"COLLISION has a robust interior witness");
        if(status==CollisionStatus.UNKNOWN) Check(r.CertifiedClearanceLowerMm==null&&r.Reasons.Count>0,"UNKNOWN never exposes a usable clearance");
    }
    private static CollisionMaterial2D Move(CollisionMaterial2D source, PlacementTransform2D t)
    {
        Func<CollisionContour2D,CollisionContour2D> contour=c=>new CollisionContour2D(c.Id,c.Boundary.Select(t.Apply),c.UncertaintyMm);
        return MaterialSet(source.Components.Select(c=>new CollisionComponent2D(contour(c.Outer),c.Holes.Select(contour))).ToArray());
    }
    private static void CollisionTests()
    {
        Test("collision kernel: separated rectangles have a certified clearance",()=> {
            var r=Query(Material(Rect("A",0,0,2,2)),Material(Rect("B",5,0,2,2)));Expect(r,CollisionStatus.CLEAR);
            Check(r.CertifiedClearanceLowerMm<=3&&r.NominalBoundaryDistanceUpperMm>=3,"Conservative lower and nominal upper bracket 3 mm");
        });
        Test("collision kernel: overlap and full containment without crossing",()=> {
            var a=Material(Rect("A",0,0,10,10));Expect(Query(a,Material(Rect("B",8,3,4,4))),CollisionStatus.COLLISION);
            Expect(Query(a,Material(Rect("B",3,3,1,1))),CollisionStatus.COLLISION);
            Expect(Query(a,a),CollisionStatus.COLLISION);
        });
        Test("collision kernel: edge contact corner contact and near-threshold gap are UNKNOWN",()=> {
            var a=Material(Rect("A",0,0,1,1));
            foreach(var b in new[]{Rect("B",1,0,1,1),Rect("B",1,1,1,1),Rect("B",1+5e-6,0,1,1),Rect("B",1+1e-5,0,1,1)})Expect(Query(a,Material(b)),CollisionStatus.UNKNOWN);
            Expect(Query(a,Material(Rect("B",1+1.00001e-5,0,1,1))),CollisionStatus.UNKNOWN);
            Expect(Query(a,Material(Rect("B",1+2e-5,0,1,1))),CollisionStatus.CLEAR);
        });
        Test("collision kernel: circle ARC-ARC clear overlap containment and tangency",()=> {
            var a=Material(Disk("A",0,0,3));
            Expect(Query(a,Material(Disk("B",8,0,2))),CollisionStatus.CLEAR);
            Expect(Query(a,Material(Disk("B",4,0,2))),CollisionStatus.COLLISION);
            Expect(Query(a,Material(Disk("B",0,0,1))),CollisionStatus.COLLISION);
            Expect(Query(a,Material(Disk("B",5,0,2))),CollisionStatus.UNKNOWN);
        });
        Test("collision kernel: LINE-ARC mixed semicircle and CW/CCW source direction",()=> {
            var line=Segment(-3,0,3,0);var arc=ToolCurve.Arc(P(3,0),P(-3,0),P(0,0),3,Math.PI,new ToolTransform());
            var d=Material(new CollisionContour2D("D",new[]{line,arc}));
            Expect(Query(d,Material(Rect("B",-.5,1,1,1))),CollisionStatus.COLLISION);
            Expect(Query(d,Material(Rect("B",-.5,-2,1,1))),CollisionStatus.CLEAR);
            Expect(Query(d,Material(Rect("B",-1,3,2,1))),CollisionStatus.UNKNOWN);
            foreach(bool cw in new[]{false,true})Expect(Query(Material(Disk("C",0,0,3,clockwise:cw)),Material(Rect("B",-1,1,2,1))),CollisionStatus.COLLISION);
        });
        Test("collision kernel: minor and major arcs exclude the missing circular sector",()=> {
            var minor=Material(ArcCap("MINOR",3,Math.PI/2));var major=Material(ArcCap("MAJOR",3,3*Math.PI/2));
            Expect(Query(minor,Material(Rect("PROBE",1.9,1.9,.2,.2))),CollisionStatus.COLLISION);
            Expect(Query(minor,Material(Rect("PROBE",-.1,-.1,.2,.2))),CollisionStatus.CLEAR);
            Expect(Query(major,Material(Rect("PROBE",-.1,-.1,.2,.2))),CollisionStatus.COLLISION);
            Expect(Query(major,Material(Rect("PROBE",2.15,-1.25,.05,.05))),CollisionStatus.CLEAR);
            var reflection=new PlacementTransform2D(1,0,0,-1,0,0,true);
            Expect(Query(Move(minor,reflection),Material(Rect("PROBE",1.9,-2.1,.2,.2))),CollisionStatus.COLLISION);
            Expect(Query(Move(major,reflection),Material(Rect("PROBE",-.1,-.1,.2,.2))),CollisionStatus.COLLISION);
        });
        Test("collision kernel: hole excludes material and an explicit island restores it",()=> {
            var outer=Rect("OUT",0,0,20,20);var hole=Rect("HOLE",5,5,10,10);var probe=Material(Rect("P",8,8,1,1));
            Expect(Query(probe,Material(outer,hole)),CollisionStatus.CLEAR);
            var island=Rect("ISLAND",7,7,4,4);var set=MaterialSet(new CollisionComponent2D(outer,new[]{hole}),new CollisionComponent2D(island));
            Expect(Query(probe,set),CollisionStatus.COLLISION);
            Expect(Query(Material(Rect("P",14.5,8,1,1)),Material(outer,hole)),CollisionStatus.COLLISION);
        });
        Test("collision kernel: union subtracts holes per component, never globally",()=> {
            var one=new CollisionComponent2D(Rect("OUT",0,0,10,10),new[]{Rect("HOLE",2,2,6,6)});
            var two=new CollisionComponent2D(Rect("SECOND",5,0,10,10));var set=MaterialSet(one,two);
            Expect(Query(Material(Rect("P",3,3,1,1)),set),CollisionStatus.CLEAR);
            Expect(Query(Material(Rect("P",6,3,1,1)),set),CollisionStatus.COLLISION);
            Expect(Query(Material(Rect("P",30,3,1,1)),set),CollisionStatus.CLEAR);
        });
        Test("collision kernel: concave L U Z and deep return/body collisions",()=> {
            var l=Material(Polygon("L",new[]{P(0,0),P(8,0),P(8,1),P(1,1),P(1,8),P(0,8)}));
            var u=Material(Polygon("U",new[]{P(0,0),P(10,0),P(10,10),P(9,10),P(9,1),P(1,1),P(1,10),P(0,10)}));
            var z=Material(Polygon("Z",new[]{P(0,0),P(6,0),P(6,5),P(10,5),P(10,6),P(5,6),P(5,1),P(0,1)}));
            Expect(Query(l,Material(Rect("TOOL",2,2,2,2))),CollisionStatus.CLEAR);
            Expect(Query(u,Material(Rect("PUNCH",2,3,4,4))),CollisionStatus.CLEAR);
            Expect(Query(u,Material(Rect("PUNCH_BODY",8.5,6,2,2))),CollisionStatus.COLLISION);
            Expect(Query(z,Material(Rect("HOLDER",5.5,3,2,2))),CollisionStatus.COLLISION);
            Expect(Query(u,Material(Rect("HOLDER",3,10,3,1))),CollisionStatus.CLEAR);
        });
        Test("collision kernel: uncertainty band is retained for clear robust overlap and band-only contact",()=> {
            var obstacle=Material(Rect("BOUNDED",0,0,10,10,.01));
            Expect(Query(Material(Rect("P",11,3,1,1)),obstacle),CollisionStatus.CLEAR);
            Expect(Query(Material(Rect("P",5,3,1,1)),obstacle),CollisionStatus.COLLISION);
            Expect(Query(Material(Rect("P",10.005,3,1,1)),obstacle),CollisionStatus.UNKNOWN);
            Expect(Query(Material(Rect("P",9.995,3,.002,1)),obstacle),CollisionStatus.UNKNOWN);
        });
        Test("collision kernel: increasing uncertainty cannot turn non-CLEAR into CLEAR",()=> {
            foreach(double x in new[]{4.0,9.999,10.0,10.005})
            {
                bool nonclear=false;
                foreach(double error in new[]{0.0,.001,.01,.1,1})
                {
                    var result=Query(Material(Rect("P",x,3,1,1)),Material(Rect("T",0,0,10,10,error)));
                    if(nonclear)Check(result.Status!=CollisionStatus.CLEAR,"Uncertainty cannot prove new clearance");
                    nonclear|=result.Status!=CollisionStatus.CLEAR;
                }
            }
        });
        Test("collision kernel: rigid rotation and mirror preserve robust geometric decisions",()=> {
            var a=Material(Disk("A",0,0,3));var shapes=new[]{Material(Rect("B",2,-1,2,2)),Material(Rect("B",5,2,2,2)),Material(Rect("B",3,-1,2,2))};
            double angle=.731;var rotation=new PlacementTransform2D(Math.Cos(angle),Math.Sin(angle),-Math.Sin(angle),Math.Cos(angle),31,-27);
            var mirror=new PlacementTransform2D(-1,0,0,1,20,10,true);
            foreach(var b in shapes)foreach(var t in new[]{rotation,mirror})Check(Query(Move(a,t),Move(b,t)).Status==Query(a,b).Status,"Rigid invariance");
        });
        Test("collision kernel: adding an overlapping or disjoint obstacle cannot hide a witnessed collision",()=> {
            var p=Material(Rect("P",2,2,2,2));var solid=Rect("T",0,0,5,5);
            Expect(Query(p,Material(solid)),CollisionStatus.COLLISION);
            Expect(Query(p,MaterialSet(new CollisionComponent2D(solid),new CollisionComponent2D(Rect("ADD",3,0,5,5)))),CollisionStatus.COLLISION);
            Expect(Query(p,MaterialSet(new CollisionComponent2D(solid),new CollisionComponent2D(Rect("ADD",30,0,5,5)))),CollisionStatus.COLLISION);
        });
        Test("collision kernel: frame invalid topology empty envelope and unknown state fail closed",()=> {
            var a=Material(Rect("A",0,0,2,2));var shiftedFrame=new CollisionMaterial2D("WRONG_FRAME",a.Components,"SYNTHETIC",true);
            Expect(Query(a,shiftedFrame),CollisionStatus.UNKNOWN);
            Expect(Query(a,MaterialSet()),CollisionStatus.UNKNOWN);
            Expect(Query(a,Material(new CollisionContour2D("OPEN",new[]{Segment(5,0,7,0)}))),CollisionStatus.UNKNOWN);
            Expect(CollisionEngine2D.Evaluate(new CollisionQuery2D(CollisionPairKind.WorkpieceHolder,a,a,null)),CollisionStatus.UNKNOWN);
            Expect(Query(a,Material(Rect("BAD",1e8,0,2,2))),CollisionStatus.UNKNOWN);
        });
        Test("collision kernel: cancellation time work depth budgets and invalid policy cannot return CLEAR",()=> {
            var a=Material(Rect("A",0,0,2,2));var b=Material(Rect("B",1,1,2,2));
            Expect(Query(a,b,token:new CancellationToken(true)),CollisionStatus.UNKNOWN);
            var limited=Query(a,b,new CollisionPolicy2D(maximumWork:2));Expect(limited,CollisionStatus.UNKNOWN);Check(limited.Reasons.Contains("WORK_BUDGET_EXCEEDED"),"Explicit budget reason");
            var timed=Query(a,b,new CollisionPolicy2D(maximumMilliseconds:1),current:()=>{Thread.Sleep(4);return true;});
            Expect(timed,CollisionStatus.UNKNOWN);Check(timed.Reasons.Contains("TIME_BUDGET_EXCEEDED"),"Elapsed time cannot silently become CLEAR");
            Expect(Query(a,b,new CollisionPolicy2D(double.NaN)),CollisionStatus.UNKNOWN);
            var unresolved=Query(Material(Disk("ROUND",0,0,3)),Material(Disk("SEPARATED_DIAGONALLY",4,4,2)),new CollisionPolicy2D(maximumSubdivisionDepth:1));
            Expect(unresolved,CollisionStatus.UNKNOWN);Check(unresolved.Reasons.Contains("SUBDIVISION_OR_NUMERICAL_RESOLUTION_LIMIT"),"Explicit depth limit");
        });
        Test("collision kernel: managed worker consumes only immutable geometry",()=> {
            var a=Material(Rect("SHEET",0,0,2,2));var b=Material(Disk("TOOL",10,0,2));
            var r=System.Threading.Tasks.Task.Run(()=>Query(a,b)).GetAwaiter().GetResult();Expect(r,CollisionStatus.CLEAR);
        });
        Test("collision kernel: held CLEAR and COLLISION invalidate when source changes",()=> {
            bool current=true;var a=Material(Rect("A",0,0,2,2));
            var clear=Query(a,Material(Rect("B",5,0,2,2)),current:()=>current);var hit=Query(a,a,current:()=>current);
            Expect(clear,CollisionStatus.CLEAR);Expect(hit,CollisionStatus.COLLISION);current=false;
            Expect(clear,CollisionStatus.UNKNOWN);Expect(hit,CollisionStatus.UNKNOWN);Check(hit.Witness==null,"No stale witness");
            int reads=0;Expect(Query(a,a,current:()=>++reads<6),CollisionStatus.UNKNOWN);
        });
        Test("collision kernel: independent analytic rectangle and circle oracles",()=> {
            var random=new Random(120031);
            for(int i=0;i<100;i++)
            {
                double x=random.NextDouble()*20-10,y=random.NextDouble()*20-10,w=.2+random.NextDouble()*6,h=.2+random.NextDouble()*6;
                double gapX=Math.Max(0,Math.Max(-3-(x+w),x-3)),gapY=Math.Max(0,Math.Max(-3-(y+h),y-3));
                double depthX=Math.Min(3,x+w)-Math.Max(-3,x),depthY=Math.Min(3,y+h)-Math.Max(-3,y);
                var r=Query(Material(Rect("A",-3,-3,6,6)),Material(Rect("B",x,y,w,h)));
                if(gapX>1e-3 || gapY>1e-3) { Expect(r,CollisionStatus.CLEAR);Check(r.CertifiedClearanceLowerMm<=Math.Sqrt(gapX*gapX+gapY*gapY),"Rectangle certificate <= independent distance"); }
                else if(depthX>1e-3 && depthY>1e-3)Expect(r,CollisionStatus.COLLISION);
                double radius=.2+random.NextDouble()*4,centerDistance=Math.Sqrt(x*x+y*y);
                var circle=Query(Material(Disk("A",0,0,3)),Material(Disk("B",x,y,radius,clockwise:(i&1)!=0)));
                if(centerDistance>3+radius+.001) { Expect(circle,CollisionStatus.CLEAR);Check(circle.CertifiedClearanceLowerMm<=centerDistance-3-radius,"Circle certificate <= independent distance"); }
                else if(centerDistance<3+radius-.001)Expect(circle,CollisionStatus.COLLISION);
            }
        });
    }
}
