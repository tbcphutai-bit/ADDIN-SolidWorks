using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;

internal static partial class ToolProfileTests
{
    // An EXACT orthogonal polyhedron fixture, not a tessellated approximation.
    // Cell union defines the source solid. Internal faces are removed, shared
    // analytic LINE edges and oriented native-style coedges are retained.
    private static PlacementModelCapture PolycellModel(double[] xs,double[] ys,double[] zs,
        Func<int,int,int,bool> occupied,BendWorkFrame frame=null)
    {
        frame=frame??BendWorkFrame.Observed("SYNTHETIC_SECTION",new PlacementVector3(),new PlacementVector3(0,0,1));
        var vertices=new Dictionary<string,PlacementVector3>();var edgeMap=new Dictionary<string,int>();
        var endpoints=new List<Tuple<string,string>>();var adjacency=new List<List<int>>();var faces=new List<NativeEnvelopeFace>();
        Func<int,int,int,string> vertex=(x,y,z)=> {
            string key=x+","+y+","+z;if(!vertices.ContainsKey(key))vertices[key]=frame.World(P(xs[x],ys[y]),zs[z]);return key;
        };
        Func<int,int,int,bool> solid=(x,y,z)=>x>=0&&y>=0&&z>=0&&x<xs.Length-1&&y<ys.Length-1&&z<zs.Length-1&&occupied(x,y,z);
        Action<string[],PlacementVector3> face=(points,normal)=> {
            int id=faces.Count;var loop=new List<NativeLoopEdge>();
            for(int k=0;k<4;k++)
            {
                string a=points[k],b=points[(k+1)%4],key=string.CompareOrdinal(a,b)<0?a+"|"+b:b+"|"+a;int edge;
                if(!edgeMap.TryGetValue(key,out edge)) {edge=endpoints.Count;edgeMap[key]=edge;endpoints.Add(Tuple.Create(a,b));adjacency.Add(new List<int>());}
                adjacency[edge].Add(id);loop.Add(new NativeLoopEdge(edge,endpoints[edge].Item1==a));
            }
            faces.Add(new NativeEnvelopeFace(id,1,normal,vertices[points[0]],new PlacementVector3(),0,false,new[]{new NativeFaceLoop(true,false,loop)}));
        };
        for(int x=0;x<xs.Length-1;x++)for(int y=0;y<ys.Length-1;y++)for(int z=0;z<zs.Length-1;z++)if(solid(x,y,z))
        {
            if(!solid(x-1,y,z))face(new[]{vertex(x,y,z),vertex(x,y,z+1),vertex(x,y+1,z+1),vertex(x,y+1,z)},frame.X*(-1));
            if(!solid(x+1,y,z))face(new[]{vertex(x+1,y,z),vertex(x+1,y+1,z),vertex(x+1,y+1,z+1),vertex(x+1,y,z+1)},frame.X);
            if(!solid(x,y-1,z))face(new[]{vertex(x,y,z),vertex(x+1,y,z),vertex(x+1,y,z+1),vertex(x,y,z+1)},frame.Y*(-1));
            if(!solid(x,y+1,z))face(new[]{vertex(x,y+1,z),vertex(x,y+1,z+1),vertex(x+1,y+1,z+1),vertex(x+1,y+1,z)},frame.Y);
            if(!solid(x,y,z-1))face(new[]{vertex(x,y,z),vertex(x,y+1,z),vertex(x+1,y+1,z),vertex(x+1,y,z)},frame.BendAxis*(-1));
            if(!solid(x,y,z+1))face(new[]{vertex(x,y,z+1),vertex(x+1,y,z+1),vertex(x+1,y+1,z+1),vertex(x,y+1,z+1)},frame.BendAxis);
        }
        var edges=endpoints.Select((p,i)=>new PlacementEdgeSnapshot(i,1,vertices[p.Item1],vertices[p.Item2],adjacency[i])).ToArray();
        var shell=new NativeEnvelopeBody("BODY",true,faces.Count,edges.Length,edges,faces);
        return NativeFixtureModel(shell,frame);
    }
    private static PlacementModelCapture NativeFixtureModel(NativeEnvelopeBody shell,BendWorkFrame frame)
    {
        var faces=shell.Faces.Select(f=>new PlacementFaceSnapshot(f.Id,f.Kind,f.SurfaceNormal,f.PointMm,f.Axis,f.Loops.SelectMany(l=>l.Edges).Select(e=>e.EdgeId).Distinct()));
        var body=new PlacementBodySnapshot(shell.BodyId,faces,shell.Edges,shell);
        double lo=shell.Edges.Min(e=>Math.Min(frame.AxialDistance(e.A),frame.AxialDistance(e.B)));
        double hi=shell.Edges.Max(e=>Math.Max(frame.AxialDistance(e.A),frame.AxialDistance(e.B)));
        return new PlacementModelCapture("CONTROLLED_NATIVE_STYLE_SHELL",123,"TEST",new[]{body},new[]{new PlacementBendSnapshot(1,body.Id,frame,1,lo,hi)},true,true);
    }
    private static SheetEnvelopeCapture CompileNative(PlacementModelCapture model)=>PrismaticEnvelopeCompiler.Read(model,model.Bends[0],CancellationToken.None);
    private static void NativeEnvelopeTests()
    {
        Test("native capture: curve Sense and coedge Sense are separate orientations",()=> {
            var a=new PlacementVector3(1,2,3);var b=new PlacementVector3(4,5,6);
            var forward=NativeEnvelopeEdgePolicy.EdgeEndpoints(a,b,true);var reverse=NativeEnvelopeEdgePolicy.EdgeEndpoints(b,a,false);
            Check((forward.Item1-reverse.Item1).Length==0&&(forward.Item2-reverse.Item2).Length==0,"Reverse curve parameterization preserves the same topological IEdge endpoints");
        });
        Test("native envelope proof: complete cap/side topology proves a rectangular through-hole",()=> {
            var model=PolycellModel(new[]{0.0,2,8,10},new[]{0.0,2,8,10},new[]{-5.0,5},(x,y,z)=>x!=1||y!=1);
            Check(model.Sections[0].Coverage==SheetCoverage.Unsupported2DEnvelope,"Baseline uniform single-loop proof cannot fill the hole");
            var sheet=CompileNative(model);Check(sheet.Certificate.Tier==EnvelopeCoverageTier.AxialSlicesWithCertifiedCoverage,"Complete shell establishes the missing topology");
            VolumeExpect(VolumeQuery(sheet,Material(Rect("T",4,4,1,1)),-4,4),CollisionStatus.CLEAR);
            VolumeExpect(VolumeQuery(sheet,Material(Rect("T",.5,.5,.5,.5)),-4,4),CollisionStatus.COLLISION);
            Check(sheet.Slice(0).Material.Components.Single().Holes.Count==1,"Native orientation assigns the void, not largest-outline filling");
        });
        Test("native envelope proof: exact polyhedral tab hidden between end sections is detected",()=> {
            var model=PolycellModel(new[]{0.0,1,4},new[]{0.0,1},new[]{-10.0,-.1,.1,10},(x,y,z)=>x==0||z==1);
            var sheet=CompileNative(model);Check(sheet.Certificate.ContinuousCoverageProven,"Every axial topology event belongs to the proof");
            Check(model.Sections[0].Coverage==SheetCoverage.Unsupported2DEnvelope,"Legacy INTERNAL_TRIM_OR_TAB remains protected");
            VolumeExpect(VolumeQuery(sheet,Material(Rect("T",3,.2,.5,.5))),CollisionStatus.COLLISION);
            VolumeExpect(VolumeQuery(sheet,Material(Rect("T",6,.2,.5,.5))),CollisionStatus.CLEAR);
        });
        Test("native envelope proof: shallow pocket relief and independent analytic cell reference",()=> {
            var xs=new[]{0.0,2,8,10};var ys=new[]{0.0,2,8,10};var zs=new[]{-5.0,-2,2,5};
            var model=PolycellModel(xs,ys,zs,(x,y,z)=>!(x==1&&y==1&&z==1));var sheet=CompileNative(model);
            Check(sheet.Certificate.ContinuousCoverageProven,"Closed source faces prove the local pocket");
            VolumeExpect(VolumeQuery(sheet,Material(Rect("T",4,4,1,1)),-1,1),CollisionStatus.CLEAR);
            VolumeExpect(VolumeQuery(sheet,Material(Rect("T",4,4,1,1)),3,4),CollisionStatus.COLLISION);
            var random=new Random(140052);
            for(int i=0;i<24;i++)
            {
                int x=random.Next(3),y=random.Next(3),z=random.Next(3);double px=(xs[x]+xs[x+1])/2,py=(ys[y]+ys[y+1])/2,pz=(zs[z]+zs[z+1])/2;
                VolumeExpect(VolumeQuery(sheet,Material(Rect("T",px-.01,py-.01,.02,.02)),pz-.01,pz+.01),x==1&&y==1&&z==1?CollisionStatus.CLEAR:CollisionStatus.COLLISION);
            }
        });
        Test("native envelope proof: rotated translated and mirrored geometry respects a right-handed frame",()=> {
            double angle=.731;var x=new PlacementVector3(Math.Cos(angle),Math.Sin(angle),0);var y=new PlacementVector3(-Math.Sin(angle),Math.Cos(angle),0);
            foreach(var frame in new[]{new BendWorkFrame("SYNTHETIC_SECTION",new PlacementVector3(31,-27,9),x,y,new PlacementVector3(0,0,1)),
                new BendWorkFrame("SYNTHETIC_SECTION",new PlacementVector3(31,-27,9),x*(-1),y,new PlacementVector3(0,0,-1))})
            {
                var model=PolycellModel(new[]{0.0,1,4},new[]{0.0,1},new[]{-10.0,-.1,.1,10},(ix,iy,iz)=>ix==0||iz==1,frame);
                var sheet=CompileNative(model);Check(sheet.Certificate.ContinuousCoverageProven,"Transform preserves native proof");
                VolumeExpect(VolumeQuery(sheet,Material(Rect("T",3,.2,.5,.5))),CollisionStatus.COLLISION);
            }
        });
        Test("native envelope proof: incomplete misoriented unsupported surfaces and multibody scope stay UNKNOWN",()=> {
            var model=PolycellModel(new[]{0.0,1,4},new[]{0.0,1},new[]{-10.0,-.1,.1,10},(x,y,z)=>x==0||z==1);
            var source=model.Bodies[0].EnvelopeTopology;var frame=model.Bends[0].Frame;
            var incomplete=new NativeEnvelopeBody(source.BodyId,true,source.NativeFaceCount,source.NativeEdgeCount+1,source.Edges,source.Faces);
            Check(CompileNative(NativeFixtureModel(incomplete,frame)).Certificate.Tier==EnvelopeCoverageTier.UnknownCoverage,"Incomplete count cannot certify");
            var first=source.Faces[0];var flipped=new NativeEnvelopeFace(first.Id,first.Kind,first.SurfaceNormal,first.PointMm,first.Axis,0,false,
                first.Loops.Select(l=>new NativeFaceLoop(l.IsOuter,l.Singular,l.Edges.Select((e,i)=>new NativeLoopEdge(e.EdgeId,i==0?!e.Forward:e.Forward)))));
            var bad=new NativeEnvelopeBody(source.BodyId,true,source.NativeFaceCount,source.NativeEdgeCount,source.Edges,new[]{flipped}.Concat(source.Faces.Skip(1)));
            Check(CompileNative(NativeFixtureModel(bad,frame)).Certificate.Tier==EnvelopeCoverageTier.UnknownCoverage,"Coedge orientation is evidence, not decoration");
            var cap=source.Faces.First(f=>Math.Abs(PlacementVector3.Dot(f.OutwardNormal,frame.BendAxis))>.5);
            var invertedCap=new NativeEnvelopeFace(cap.Id,cap.Kind,cap.SurfaceNormal,cap.PointMm,cap.Axis,0,true,cap.Loops);
            bad=new NativeEnvelopeBody(source.BodyId,true,source.NativeFaceCount,source.NativeEdgeCount,source.Edges,source.Faces.Select(f=>f.Id==cap.Id?invertedCap:f));
            Check(CompileNative(NativeFixtureModel(bad,frame)).Reasons.Contains("INCONSISTENT_NATIVE_CAP_LOOP_ORIENTATION"),"A flipped face sense cannot retain a cap material proof");
            var splineFace=new NativeEnvelopeFace(first.Id,0,first.SurfaceNormal,first.PointMm,first.Axis,0,false,first.Loops);
            bad=new NativeEnvelopeBody(source.BodyId,true,source.NativeFaceCount,source.NativeEdgeCount,source.Edges,new[]{splineFace}.Concat(source.Faces.Skip(1)));
            Check(CompileNative(NativeFixtureModel(bad,frame)).Reasons.Contains("UNSUPPORTED_NATIVE_SURFACE"),"B-surface cannot be approximated into CLEAR");
            var multi=new PlacementModelCapture("MULTI",123,"TEST",new[]{model.Bodies[0],model.Bodies[0]},model.Bends,true,true);
            Check(CompileNative(multi).Reasons.Contains("AMBIGUOUS_MULTIBODY_OR_INCOMPLETE_SCOPE"),"No implicit union of other bodies");
        });
    }
}
