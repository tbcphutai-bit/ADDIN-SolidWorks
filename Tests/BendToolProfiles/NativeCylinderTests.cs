using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;

internal static partial class ToolProfileTests
{
    // Exact box minus a through-X cylinder. Full circular inner trim loops on
    // both planar caps and the inward cylindrical wall belong to the source.
    private static PlacementModelCapture NativeBoreModel(BendWorkFrame frame=null)
    {
        frame=frame??BendWorkFrame.Observed("SYNTHETIC_SECTION",new PlacementVector3(),new PlacementVector3(0,0,1));
        var box=PolycellModel(new[]{0.0,10},new[]{-4.0,4},new[]{-4.0,4},(x,y,z)=>true,frame).Bodies[0].EnvelopeTopology;
        var edges=box.Edges.ToList();var faces=box.Faces.ToList();var circles=new List<NativeCircleDefinition>();
        for(int cap=0;cap<2;cap++)
        {
            int id=edges.Count;var center=frame.World(P(cap*10,0),0);var start=center+frame.Y*2;
            edges.Add(new PlacementEdgeSnapshot(id,2,start,start,new[]{cap,6},new PlacementArc3D(center,center-frame.Y*2,2,true)));
            circles.Add(new NativeCircleDefinition(id,frame.X,true));var old=faces[cap];
            faces[cap]=new NativeEnvelopeFace(old.Id,old.Kind,old.SurfaceNormal,old.PointMm,old.Axis,old.RadiusMm,old.ReversedSurfaceSense,
                old.Loops.Concat(new[]{new NativeFaceLoop(false,false,new[]{new NativeLoopEdge(id,cap==0)})}));
        }
        faces.Add(new NativeEnvelopeFace(6,2,new PlacementVector3(),frame.World(P(0,0),0),frame.X,2,true,
            new[]{new NativeFaceLoop(true,false,new[]{new NativeLoopEdge(12,false)}),new NativeFaceLoop(false,false,new[]{new NativeLoopEdge(13,true)})}));
        return NativeFixtureModel(new NativeEnvelopeBody(box.BodyId,true,7,14,edges,faces,circles:circles),frame);
    }
    // Quarter-annulus extruded in X, the same native surface family as a bend
    // transverse to the inspected bend. No analytic-volume factory is used.
    private static PlacementModelCapture NativeQuarterBendModel(BendWorkFrame frame=null)
    {
        frame=frame??BendWorkFrame.Observed("SYNTHETIC_SECTION",new PlacementVector3(),new PlacementVector3(0,0,1));
        var yz=new[]{P(3,0),P(0,3),P(0,1),P(1,0)};
        Func<int,int,PlacementVector3> vertex=(cap,i)=>frame.World(P(cap*10,yz[i].X),yz[i].Y);
        var edges=new List<PlacementEdgeSnapshot>();var circles=new List<NativeCircleDefinition>();
        for(int cap=0;cap<2;cap++)for(int i=0;i<4;i++)
        {
            PlacementArc3D arc=null;
            if(i==0||i==2)
            {
                double r=i==0?3:1;var c=frame.World(P(cap*10,0),0);
                arc=new PlacementArc3D(c,c+(frame.Y+frame.BendAxis)*(r/Math.Sqrt(2)),r,false);
                circles.Add(new NativeCircleDefinition(cap*4+i,frame.X,i==0));
            }
            edges.Add(new PlacementEdgeSnapshot(cap*4+i,arc==null?1:2,vertex(cap,i),vertex(cap,(i+1)%4),new[]{cap,2+i},arc));
        }
        for(int i=0;i<4;i++)edges.Add(new PlacementEdgeSnapshot(8+i,1,vertex(0,i),vertex(1,i),new[]{2+i,2+(i+3)%4}));
        var faces=new List<NativeEnvelopeFace>();
        faces.Add(new NativeEnvelopeFace(0,1,frame.X*(-1),vertex(0,0),new PlacementVector3(),0,false,
            new[]{new NativeFaceLoop(true,false,new[]{3,2,1,0}.Select(i=>new NativeLoopEdge(i,false)))}));
        faces.Add(new NativeEnvelopeFace(1,1,frame.X,vertex(1,0),new PlacementVector3(),0,false,
            new[]{new NativeFaceLoop(true,false,Enumerable.Range(0,4).Select(i=>new NativeLoopEdge(4+i,true)))}));
        for(int i=0;i<4;i++)
        {
            bool cylinder=i==0||i==2;var normal=i==1?frame.Y*(-1):frame.BendAxis*(-1);
            faces.Add(new NativeEnvelopeFace(2+i,cylinder?2:1,cylinder?new PlacementVector3():normal,
                cylinder?frame.World(P(0,0),0):vertex(0,i),cylinder?frame.X:new PlacementVector3(),i==0?3:i==2?1:0,i==2,
                new[]{new NativeFaceLoop(true,false,new[]{new NativeLoopEdge(i,true),new NativeLoopEdge(8+(i+1)%4,true),new NativeLoopEdge(4+i,false),new NativeLoopEdge(8+i,false)})}));
        }
        return NativeFixtureModel(new NativeEnvelopeBody("BODY",true,6,12,edges,faces,circles:circles),frame);
    }
    private static void NativeCylinderTests()
    {
        Test("native nonprismatic: full-circle bore has exact cap holes and continuous sqrt coverage",()=> {
            var model=NativeBoreModel();Check(model.Sections[0].Coverage==SheetCoverage.Unsupported2DEnvelope,"Legacy uniform proof still rejects cross-axis material variation");
            var sheet=CompileNative(model);Check(sheet.Certificate.ContinuousCoverageProven,"Complete cross-axis cylinder source is certified: "+string.Join(",",sheet.Reasons));
            Check(sheet.Certificate.Method=="CompleteOrthogonalPlaneCylinderSectionFamilyV1","Native analytic source, not a supplied synthetic volume");
            VolumeExpect(VolumeQuery(sheet,Material(Rect("VOID",4,-.1,1,.2)),-.2,.2),CollisionStatus.CLEAR);
            VolumeExpect(VolumeQuery(sheet,Material(Rect("METAL",4,2.5,1,.2)),-.2,.2),CollisionStatus.COLLISION);
            VolumeExpect(VolumeQuery(sheet.ObservationsOnly(new[]{-.5,.5}),Material(Rect("VOID",4,-.1,1,.2)),-.2,.2),CollisionStatus.UNKNOWN);
        });
        Test("native nonprismatic: transverse quarter bend retains inner and outer analytic walls",()=> {
            var sheet=CompileNative(NativeQuarterBendModel());Check(sheet.Certificate.ContinuousCoverageProven,"Native bend-sector proof: "+string.Join(",",sheet.Reasons));
            VolumeExpect(VolumeQuery(sheet,Material(Rect("METAL",4,2,1,.2)),.2,.4),CollisionStatus.COLLISION);
            VolumeExpect(VolumeQuery(sheet,Material(Rect("VOID",4,.1,1,.1)),.1,.2),CollisionStatus.CLEAR);
            VolumeExpect(VolumeQuery(sheet,Material(Rect("OUTSIDE",4,3.3,1,.1)),.2,.4),CollisionStatus.CLEAR);
        });
        Test("native nonprismatic: interval enclosure contains every analytic intermediate section",()=> {
            foreach(var model in new[]{NativeBoreModel(),NativeQuarterBendModel()})
            {
                var sheet=CompileNative(model);Check(sheet.Certificate.ContinuousCoverageProven,"Native family ready");var random=new Random(160041);
                foreach(var cell in sheet.Certificate.Intervals)
                {
                    var inner=new AxialInterval(cell.StartMm+cell.LengthMm*.1,cell.EndMm-cell.LengthMm*.1);var enclosing=sheet.Enclose(inner);
                    foreach(double fraction in new[]{.13,.37,.61,.89})
                    {
                        double z=inner.StartMm+inner.LengthMm*fraction;var actual=sheet.Slice(z).Material;
                        foreach(var contour in actual.Contours)foreach(var edge in contour.Boundary)
                        {
                            var point=CollisionGeometry.Point(edge,.37);
                            var location=CollisionPointLocation.Locate(enclosing,point,new CollisionWork(new CollisionQuery2D(
                                CollisionPairKind.WorkpiecePunch,enclosing,enclosing,"ORACLE"),CancellationToken.None),1e-5);
                            Check(location.Location!=MaterialLocation.Outside,"No true intermediate boundary can lie outside the certified material superset");
                        }
                    }
                }
            }
        });
        Test("native nonprismatic: circle parameter reversal rigid transforms and reflected frames preserve material",()=> {
            double angle=.431;var x=new PlacementVector3(Math.Cos(angle),Math.Sin(angle),0);var y=new PlacementVector3(-Math.Sin(angle),Math.Cos(angle),0);
            foreach(var frame in new[]{new BendWorkFrame("SYNTHETIC_SECTION",new PlacementVector3(51,-37,12),x,y,new PlacementVector3(0,0,1)),
                new BendWorkFrame("SYNTHETIC_SECTION",new PlacementVector3(51,-37,12),x*(-1),y,new PlacementVector3(0,0,-1))})
            {
                var model=NativeBoreModel(frame);var source=model.Bodies[0].EnvelopeTopology;
                var equivalent=new NativeEnvelopeBody(source.BodyId,true,source.NativeFaceCount,source.NativeEdgeCount,source.Edges,source.Faces,
                    circles:source.Circles.Select(c=>new NativeCircleDefinition(c.EdgeId,c.Axis*(-1),!c.CurveAndEdgeSameSense)));
                var sheet=CompileNative(NativeFixtureModel(equivalent,frame));Check(sheet.Certificate.ContinuousCoverageProven,"Parameter orientation is separate from physical orientation");
                VolumeExpect(VolumeQuery(sheet,Material(Rect("VOID",4,-.1,1,.2)),-.2,.2),CollisionStatus.CLEAR);
            }
        });
        Test("native nonprismatic: missing circle evidence inverted face sense B-surface and multibody fail closed",()=> {
            var model=NativeBoreModel();var s=model.Bodies[0].EnvelopeTopology;var f=model.Bends[0].Frame;
            var noPlane=new NativeEnvelopeBody(s.BodyId,true,s.NativeFaceCount,s.NativeEdgeCount,s.Edges,s.Faces);
            Check(!CompileNative(NativeFixtureModel(noPlane,f)).Certificate.ContinuousCoverageProven,"Full circles do not define their plane from a repeated endpoint");
            foreach(int id in new[]{0,6})
            {
                var old=s.Faces[id];var flip=new NativeEnvelopeFace(old.Id,old.Kind,old.SurfaceNormal,old.PointMm,old.Axis,old.RadiusMm,!old.ReversedSurfaceSense,old.Loops);
                var bad=new NativeEnvelopeBody(s.BodyId,true,s.NativeFaceCount,s.NativeEdgeCount,s.Edges,s.Faces.Select(p=>p.Id==id?flip:p),circles:s.Circles);
                Check(!CompileNative(NativeFixtureModel(bad,f)).Certificate.ContinuousCoverageProven,"Face orientation must agree with coedges");
            }
            var face=s.Faces[6];var unsupported=new NativeEnvelopeFace(6,0,face.SurfaceNormal,face.PointMm,face.Axis,face.RadiusMm,face.ReversedSurfaceSense,face.Loops);
            var spline=new NativeEnvelopeBody(s.BodyId,true,s.NativeFaceCount,s.NativeEdgeCount,s.Edges,s.Faces.Take(6).Concat(new[]{unsupported}),circles:s.Circles);
            Check(!CompileNative(NativeFixtureModel(spline,f)).Certificate.ContinuousCoverageProven,"Unknown B-surface is not replaced by its boundary");
            var multi=new PlacementModelCapture("MULTI",123,"TEST",new[]{model.Bodies[0],model.Bodies[0]},model.Bends,true,true);
            Check(!CompileNative(multi).Certificate.ContinuousCoverageProven,"No implicit multibody scope");
        });
        Test("native nonprismatic: source error is amplified at a cylindrical tangency instead of producing a false witness",()=> {
            var sheet=CompileNative(NativeBoreModel());
            VolumeExpect(VolumeQuery(sheet,Material(Rect("NEAR_ROOT",4,.023,.001,.0002)),1.99989,1.99991),CollisionStatus.UNKNOWN);
            var clear=VolumeQuery(sheet,Material(Rect("VOID",4,-.1,1,.2)),-.2,.2);VolumeExpect(clear,CollisionStatus.CLEAR);
            sheet.Binding.Model.Lease.Invalidate();VolumeExpect(clear,CollisionStatus.UNKNOWN);
        });
        Test("native snapshot regression: object key order is ignored while candidate order and values are exact",()=> {
            var a=new Dictionary<string,object>{{"Index",1},{"BendIds",new[]{"A","B"}},{"Status","CHUA_XAC_MINH"}};
            var b=new Dictionary<string,object>{{"Status","CHUA_XAC_MINH"},{"BendIds",new[]{"A","B"}},{"Index",1}};
            Check(NativeSnapshotJson.Canonical(a)==NativeSnapshotJson.Canonical(b),"Observed bad-manifest false failure: only object property order differs");
            b["BendIds"]=new[]{"B","A"};Check(NativeSnapshotJson.Canonical(a)!=NativeSnapshotJson.Canonical(b),"Candidate sequence changes are still detected");
            b["BendIds"]=new[]{"A","B"};b["Index"]=2;Check(NativeSnapshotJson.Canonical(a)!=NativeSnapshotJson.Canonical(b),"Numeric changes are not rounded or ignored");
        });
    }
}
