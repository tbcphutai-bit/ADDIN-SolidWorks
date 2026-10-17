using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendTools;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.BendPlacement
{
    internal static class BendPlacementNativeCapture
    {
        private static int arcReads;
        internal static int ArcMetadataReadCount=>Volatile.Read(ref arcReads);
        // This optional append runs inside the existing STA edge capture while
        // Curve/CurveParamData are already available. Failures affect placement
        // only; the Min edge kind/endpoints/topology are left untouched.
        internal static PlacementArc3D TryReadArc(Edge edge,Curve curve,CurveParamData p)
        {
            try
            {
                Interlocked.Increment(ref arcReads);
                if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)return null;
                var data=curve.CircleParams as double[];if(data==null||data.Length<7)return null;
                // CurveParamData is edge-oriented. Reverse-sense edges can use
                // negative parameter space; evaluating that parameter on the
                // underlying ICurve can select the complementary circular arc.
                var middle=edge.Evaluate2(NativeEdgeParameterPolicy.Midpoint(p.UMinValue,p.UMaxValue),0) as double[];
                if(!NativeEdgeParameterPolicy.EvaluationSucceeded(middle))return null;
                return new PlacementArc3D(PlacementVector3.Metres(data[0],data[1],data[2]),PlacementVector3.Metres(middle[0],middle[1],middle[2]),
                    data[6]*1000,Math.Abs(Math.Abs(p.UMaxValue-p.UMinValue)-2*Math.PI)<1e-10);
            }
            catch{return null;}
        }
        private static PlacementVector3 Point(BendVector v)=>PlacementVector3.Metres(v.X,v.Y,v.Z);
        private static PlacementVector3 Direction(BendVector v)=>new PlacementVector3(v.X,v.Y,v.Z);
        internal static PlacementModelCapture Copy(BendCheckItem item)
        {
            if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)throw new InvalidOperationException("Placement capture adapter requires STA.");
            var geometry=item.Context?.Geometry;var nativeBodies=geometry==null?new BendPanelBody[0]:geometry.Values.Select(g=>g.Body).Where(b=>b!=null).Distinct().ToArray();
            var bodies=new List<PlacementBodySnapshot>();var ids=new Dictionary<BendPanelBody,string>();
            for(int i=0;i<nativeBodies.Length;i++)
            {
                var b=nativeBodies[i];string id="body:"+i;ids.Add(b,id);
                bodies.Add(new PlacementBodySnapshot(id,b.Faces.Select(f=>new PlacementFaceSnapshot(f.Id,f.Kind,Direction(f.Normal),Point(f.Point),Direction(f.Axis),f.Edges)),
                    b.Edges.Select(e=>new PlacementEdgeSnapshot(e.Id,e.Kind,Point(e.A),Point(e.B),e.Faces,e.PlacementArc))));
            }
            var bends=new List<PlacementBendSnapshot>();
            foreach(var row in item.Rows)
            {
                BendMinimumFeatureGeometry g=null;geometry?.TryGetValue(row.FeatureId,out g);BendWorkFrame frame=null;string bodyId=null;double min=0,max=0;
                if(g?.Body!=null)
                {
                    bodyId=ids[g.Body];
                    if(g.Axis.Length>0.999999&&g.Max>g.Min)
                    {
                        double middle=(g.Min+g.Max)/2;frame=BendWorkFrame.Observed("bend:"+row.FeatureId,Point(g.Root+g.Axis*middle),Direction(g.Axis));
                        min=(g.Min-middle)*1000;max=(g.Max-middle)*1000;
                    }
                }
                bends.Add(new PlacementBendSnapshot(row.FeatureId,bodyId,frame,row.ModelThicknessMm,min,max));
            }
            bool complete=item.Context!=null&&nativeBodies.Sum(b=>b.Faces.Count)==item.Context.FaceCount&&nativeBodies.Sum(b=>b.Edges.Count)==item.Context.EdgeCount;
            string key=PlacementSourceIo.Hash(item.Key+"\n"+item.Configuration+"\n"+item.Stamp);
            return new PlacementModelCapture(key,item.Stamp,item.Configuration,bodies,bends,complete);
        }
    }
}
