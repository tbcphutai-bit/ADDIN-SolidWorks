using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    // Immutable-after-capture model coordinates (metres). The graph never needs
    // Boolean sections, drawing views, unfolded lengths or a world bounding box.
    internal sealed class BendPanelFace
    {
        internal int Id, Kind, Owner;
        internal bool Sense;
        internal BendVector Normal, Point, Axis;
        internal double Radius;
        internal readonly List<int> Edges=new List<int>(), OuterEdges=new List<int>();
    }
    internal sealed class BendPanelEdge
    {
        internal int Id, Kind;
        internal BendVector A,B;
        internal BendPlacement.PlacementArc3D PlacementArc;
        internal readonly List<int> Faces=new List<int>();
    }
    internal sealed class BendPanelBody
    {
        internal string Name,MaterialName,MaterialCode;
        internal Body2 NativeBody;
        internal readonly List<BendPanelFace> Faces=new List<BendPanelFace>();
        internal readonly List<BendPanelEdge> Edges=new List<BendPanelEdge>();
        internal readonly Dictionary<int,Face2> NativeFaces=new Dictionary<int,Face2>();
    }
    internal static class BendPanelGeometryReader
    {
        private static long Key(object value)
        {var key=Marshal.GetIUnknownForObject(value);try{return key.ToInt64();}finally{Marshal.Release(key);}}
        internal static List<BendPanelBody> Read(ISldWorks app,ModelDoc2 model,string config,IList<VCutPropertySetupCommand.BendRow> rows)
        {
            var result=new List<BendPanelBody>();
            var bodies=((IPartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody,false) as object[];
            if(bodies==null||bodies.Length==0)throw new InvalidOperationException("Part không có solid body đã chấn.");
            string locations=app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases)??"";
            var materials=new BendMinimumMaterialLibraries(locations.Split(new[]{';','|','\r','\n'},StringSplitOptions.RemoveEmptyEntries));
            var ids=new HashSet<int>(rows.Select(r=>r.FeatureId));
            var featureFaces=new Dictionary<long,HashSet<int>>();
            // The bend list is the one already loaded by Set Bend Line. Do not
            // traverse/reconstruct a second, potentially differently ordered list.
            foreach(var row in rows) {
                var faces=row.Feature?.GetFaces() as object[];
                foreach(var face in faces??new object[0]) {
                    long key=Key(face);HashSet<int> owners;
                    if(!featureFaces.TryGetValue(key,out owners))featureFaces.Add(key,owners=new HashSet<int>());
                    owners.Add(row.FeatureId);
                }
            }
            foreach(var body in bodies.Cast<Body2>()) {
                var nativeFaces=(body.GetFaces() as object[])?.Cast<Face2>().ToArray();
                var nativeEdges=(body.GetEdges() as object[])?.Cast<Edge>().ToArray();
                if(nativeFaces==null||nativeEdges==null||nativeFaces.Length>12000)throw new InvalidOperationException("Không đọc được topology body hoặc số mặt vượt giới hạn kiểm tra.");
                var faceIds=nativeFaces.Select((f,i)=>new{key=Key(f),id=i}).ToDictionary(p=>p.key,p=>p.id);
                var edgeIds=nativeEdges.Select((e,i)=>new{key=Key(e),id=i}).ToDictionary(p=>p.key,p=>p.id);
                var data=new BendPanelBody{Name=body.Name,NativeBody=body};var material=materials.Read(app,model,body,config);
                data.MaterialName=material.Name;data.MaterialCode=material.Code;
                for(int i=0;i<nativeEdges.Length;i++) {
                    var edge=nativeEdges[i];var curve=edge.GetCurve() as Curve;var p=edge.GetCurveParams3() as CurveParamData;
                    if(curve==null||p==null)throw new InvalidOperationException("Không đọc được cạnh topology của body.");
                    var value=new BendPanelEdge{Id=i,Kind=curve.IsLine()?1:curve.IsCircle()?2:0,A=BendVector.From((double[])p.StartPoint),B=BendVector.From((double[])p.EndPoint)};
                    if(value.Kind==2)value.PlacementArc=BendPlacement.BendPlacementNativeCapture.TryReadArc(edge,curve,p);
                    foreach(var face in edge.GetTwoAdjacentFaces2() as object[]??new object[0]) {
                        int index;if(face!=null&&faceIds.TryGetValue(Key(face),out index))value.Faces.Add(index);
                    }
                    data.Edges.Add(value);
                }
                for(int i=0;i<nativeFaces.Length;i++) {
                    var face=nativeFaces[i];var surface=face.GetSurface() as Surface;var feature=face.GetFeature() as Feature;
                    var value=new BendPanelFace{Id=i,Owner=feature?.GetID()??0,Sense=face.FaceInSurfaceSense()};
                    if(!ids.Contains(value.Owner)) {
                        HashSet<int> owners;
                        if(featureFaces.TryGetValue(Key(face),out owners)&&owners.Count==1)value.Owner=owners.Single();
                    }
                    if(surface!=null&&surface.IsPlane()) {
                        var p=(double[])surface.PlaneParams;value.Kind=1;value.Normal=BendVector.From(p).Unit;value.Point=new BendVector(p[3],p[4],p[5]);
                    } else if(surface!=null&&surface.IsCylinder()) {
                        var p=(double[])surface.CylinderParams;value.Kind=2;value.Point=BendVector.From(p);value.Axis=new BendVector(p[3],p[4],p[5]).Unit;value.Radius=p[6];
                    }
                    foreach(var edge in face.GetEdges() as object[]??new object[0])value.Edges.Add(edgeIds[Key(edge)]);
                    foreach(var loop in (face.GetLoops() as object[]??new object[0]).Cast<Loop2>())
                        if(loop.IsOuter())foreach(var edge in loop.GetEdges() as object[]??new object[0])value.OuterEdges.Add(edgeIds[Key(edge)]);
                    data.Faces.Add(value);data.NativeFaces.Add(i,face);
                }
                result.Add(data);
            }
            return result;
        }
    }
}
