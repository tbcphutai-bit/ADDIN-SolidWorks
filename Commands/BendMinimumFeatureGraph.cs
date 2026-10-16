using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ADDIN.Commands
{
    internal sealed class BendMinimumMeasure
    {
        internal string Shape,Dimension,Side,Neighbor,Error;
        internal double ValueMm,ThicknessMm;
        internal BendVector Start,End;
        internal readonly List<int> Faces=new List<int>();
    }
    internal sealed class BendMinimumFeatureGeometry
    {
        internal int FeatureId;
        internal string Name,Error,MaterialCode;
        internal BendPanelBody Body;
        internal BendVector Axis,Root;
        internal double Min,Max;
        internal readonly List<BendMinimumMeasure> Measures=new List<BendMinimumMeasure>();
    }
    // A panel has two actual planar skins. Identity comes from connected face
    // groups, not merely coincident infinite planes or proximity of bend lines.
    internal sealed class BendGraphPanel
    {
        internal BendVector Normal;
        internal double First,Second;
        internal readonly HashSet<int> FirstFaces=new HashSet<int>(),SecondFaces=new HashSet<int>();
        internal readonly List<BendGraphNode> Bends=new List<BendGraphNode>();
    }
    internal sealed class BendGraphNode
    {
        internal int Id;
        internal string Name;
        internal BendVector Axis,Root;
        internal double Min,Max,Thickness;
        internal BendMinimumFeatureGeometry Result;
        internal readonly List<BendGraphPanel> Panels=new List<BendGraphPanel>();
    }
    internal static class BendMinimumFeatureGraph
    {
        private const double Tol=1e-8;
        private static bool Parallel(BendVector a,BendVector b)=>BendVector.Cross(a,b).Length<1e-8;
        private static BendVector Canonical(BendVector value) {
            var n=value.Unit;double major=Math.Abs(n.X)>=Math.Abs(n.Y)&&Math.Abs(n.X)>=Math.Abs(n.Z)?n.X:Math.Abs(n.Y)>=Math.Abs(n.Z)?n.Y:n.Z;
            return major<0?n*-1:n;
        }
        private static bool Straight(BendPanelEdge edge,BendPanelBody body) {
            if(edge.Kind==1)return true;if(edge.Kind==2||edge.Faces.Count!=2)return false;
            var a=body.Faces[edge.Faces[0]];var b=body.Faces[edge.Faces[1]];
            // Analytic plane/plane intersections can be stored as B-splines.
            return a.Kind==1&&b.Kind==1&&BendVector.Cross(a.Normal,b.Normal).Length>1e-8;
        }
        private static HashSet<int> ConnectedCoplanar(BendPanelBody body,BendPanelFace start) {
            var faces=new HashSet<int>();var pending=new Queue<int>();pending.Enqueue(start.Id);
            while(pending.Count>0) {
                int id=pending.Dequeue();if(!faces.Add(id))continue;
                foreach(int e in body.Faces[id].Edges)foreach(int other in body.Edges[e].Faces) {
                    var next=body.Faces[other];
                    if(next.Kind==1&&!faces.Contains(other)&&Parallel(next.Normal,start.Normal)&&Math.Abs(BendVector.Dot(next.Point-start.Point,start.Normal))<=Tol)pending.Enqueue(other);
                }
            }
            return faces;
        }
        private static List<BendPanelFace> Tangents(BendPanelBody body,BendPanelFace cylinder,BendVector axis,BendVector root) {
            var values=new List<BendPanelFace>();
            foreach(int e in cylinder.Edges) {
                var edge=body.Edges[e];var direction=edge.B-edge.A;
                if(direction.Length<=Tol||!Parallel(direction.Unit,axis))continue;
                foreach(int f in edge.Faces) {
                    var plane=body.Faces[f];
                    if(plane.Kind!=1||Math.Abs(BendVector.Dot(plane.Normal,axis))>1e-8)continue;
                    if(Math.Abs(Math.Abs(BendVector.Dot(root-plane.Point,plane.Normal))-cylinder.Radius)>Tol*3)continue;
                    if(!values.Any(p=>p.Id==plane.Id))values.Add(plane);
                }
            }
            return values;
        }
        private static BendGraphPanel Panel(BendPanelBody body,BendPanelFace a,BendPanelFace b,IList<BendGraphPanel> all) {
            var n=Canonical(a.Normal);double first=BendVector.Dot(a.Point,n),second=BendVector.Dot(b.Point,n);
            var firstFaces=ConnectedCoplanar(body,a);var secondFaces=ConnectedCoplanar(body,b);
            if(first>second){double swap=first;first=second;second=swap;var fs=firstFaces;firstFaces=secondFaces;secondFaces=fs;}
            var panel=all.FirstOrDefault(p=>Parallel(p.Normal,n)&&Math.Abs(p.First-first)<=Tol*3&&Math.Abs(p.Second-second)<=Tol*3
                &&(p.FirstFaces.Overlaps(firstFaces)||p.SecondFaces.Overlaps(secondFaces)));
            if(panel==null){panel=new BendGraphPanel{Normal=n,First=first,Second=second};all.Add(panel);}
            panel.FirstFaces.UnionWith(firstFaces);panel.SecondFaces.UnionWith(secondFaces);return panel;
        }
        internal static Dictionary<int,BendMinimumFeatureGeometry> Build(IList<BendPanelBody> bodies,IList<VCutPropertySetupCommand.BendRow> rows)
        {
            var results=rows.ToDictionary(r=>r.FeatureId,r=>new BendMinimumFeatureGeometry{FeatureId=r.FeatureId,Name=r.FeatureName,Error="Chưa ghép được feature với hai mặt trong/ngoài của nếp chấn ở cấu hình đã chấn."});
            foreach(var body in bodies) {
                var panels=new List<BendGraphPanel>();var nodes=new List<BendGraphNode>();
                foreach(var row in rows) {
                    var cylinders=body.Faces.Where(f=>f.Kind==2&&f.Owner==row.FeatureId).ToList();if(cylinders.Count==0)continue;
                    var result=results[row.FeatureId];
                    if(result.Body!=null){result.Error="Feature xuất hiện trên nhiều body; chưa xác định duy nhất nếp chấn.";continue;}
                    result.Body=body;result.MaterialCode=body.MaterialCode;
                    if(!row.BendAngleDeg.HasValue||Math.Abs(row.BendAngleDeg.Value-90)>.0001){result.Error="Bảng tối thiểu hiện tại chỉ kiểm chứng nếp chấn 90°.";continue;}
                    var axis=Canonical(cylinders[0].Axis);var root=cylinders[0].Point-axis*BendVector.Dot(cylinders[0].Point,axis);
                    if(cylinders.Any(c=>!Parallel(c.Axis,axis)||(c.Point-axis*BendVector.Dot(c.Point,axis)-root).Length>Tol*3)) {result.Error="Một feature chứa nhiều trục chấn; cần nhận dạng riêng từng nếp.";continue;}
                    var radii=cylinders.Select(c=>c.Radius).OrderBy(r=>r).ToList();double inner=radii.First(),outer=radii.Last(),thickness=outer-inner;
                    if(thickness<=Tol||radii.Any(r=>Math.Abs(r-inner)>Tol&&Math.Abs(r-outer)>Tol)) {result.Error="Chưa xác định duy nhất hai bán kính trong/ngoài và chiều dày.";continue;}
                    if(row.ModelThicknessMm.HasValue&&Math.Abs(thickness*1000-row.ModelThicknessMm.Value)>.00001) {result.Error="Chênh bán kính hai mặt chấn không khớp chiều dày model.";continue;}
                    var inside=cylinders.Where(c=>Math.Abs(c.Radius-inner)<=Tol).ToList();var outside=cylinders.Where(c=>Math.Abs(c.Radius-outer)<=Tol).ToList();
                    if(inside.Any(a=>outside.Any(b=>a.Sense==b.Sense))) {result.Error="Hai mặt trụ chưa xác nhận được pháp tuyến trong/ngoài.";continue;}
                    var axial=cylinders.SelectMany(c=>c.Edges).Distinct().SelectMany(e=>new[]{BendVector.Dot(body.Edges[e].A,axis),BendVector.Dot(body.Edges[e].B,axis)}).ToList();
                    var node=new BendGraphNode{Id=row.FeatureId,Name=row.FeatureName,Axis=axis,Root=root,Min=axial.Min(),Max=axial.Max(),Thickness=thickness,Result=result};
                    result.Axis=axis;result.Root=root;result.Min=node.Min;result.Max=node.Max;
                    var left=inside.SelectMany(c=>Tangents(body,c,axis,root)).GroupBy(f=>f.Id).Select(g=>g.First()).ToList();
                    var right=outside.SelectMany(c=>Tangents(body,c,axis,root)).GroupBy(f=>f.Id).Select(g=>g.First()).ToList();
                    foreach(var a in left)foreach(var b in right) {
                        if(!Parallel(a.Normal,b.Normal)||Math.Abs(Math.Abs(BendVector.Dot(a.Point-b.Point,a.Normal))-thickness)>Tol*3)continue;
                        var panel=Panel(body,a,b,panels);if(!node.Panels.Contains(panel))node.Panels.Add(panel);
                    }
                    if(node.Panels.Count!=2||Math.Abs(BendVector.Dot(node.Panels[0].Normal,node.Panels[1].Normal))>1e-8) {result.Error="Chưa ghép duy nhất hai cánh phẳng vuông góc của nếp chấn.";continue;}
                    result.Error=null;nodes.Add(node);foreach(var panel in node.Panels)panel.Bends.Add(node);
                }
                foreach(var node in nodes) {
                    for(int i=0;i<node.Panels.Count;i++) {
                        var panel=node.Panels[i];var side=new BendMinimumMeasure{Side=i==0?"Phía A":"Phía B",ThicknessMm=node.Thickness*1000};
                        node.Result.Measures.Add(side);
                        var neighbors=panel.Bends.Where(b=>b!=node&&Parallel(b.Axis,node.Axis)&&Math.Min(b.Max,node.Max)-Math.Max(b.Min,node.Min)>Tol).ToList();
                        if(panel.Bends.Any(b=>b!=node&&!Parallel(b.Axis,node.Axis))||HasUnmappedBoundary(body,panel,node.Axis)) {
                            side.Error="Cánh nối với nếp chưa nhận dạng đầy đủ hoặc theo phương khác; chưa chứng minh duy nhất cụm L/Z/C.";
                            continue;
                        }
                        // End reliefs legitimately give the two connected bends
                        // different axial lengths (as in B5). A trimmed end of a
                        // continuous web does not become a separate L flange.
                        if(neighbors.Count==1)MeasureBetween(node,panel,neighbors[0],side);
                        else if(neighbors.Count>1)side.Error="Cánh nối với nhiều nếp song song; chưa xác định duy nhất cụm L/Z/C.";
                        else if(panel.Bends.Any(b=>b!=node))side.Error="Cánh nối với nếp theo phương khác; chưa xác định duy nhất đầu cánh tự do.";
                        else MeasureFree(node,panel,body,side);
                    }
                }
            }
            return results;
        }
        private static bool HasUnmappedBoundary(BendPanelBody body,BendGraphPanel panel,BendVector axis)
        {
            var recognized=new HashSet<int>(panel.Bends.Select(b=>b.Id));
            foreach(int id in panel.FirstFaces.Concat(panel.SecondFaces))
                foreach(int e in body.Faces[id].OuterEdges)
                    foreach(int f in body.Edges[e].Faces) {
                        var face=body.Faces[f];
                        if(face.Kind==2&&Parallel(face.Axis,axis)&&!recognized.Contains(face.Owner))return true;
                    }
            return false;
        }
        private static BendGraphPanel Other(BendGraphNode node,BendGraphPanel panel)=>node.Panels.Single(p=>p!=panel);
        private static void MeasureBetween(BendGraphNode node,BendGraphPanel web,BendGraphNode neighbor,BendMinimumMeasure side)
        {
            side.Neighbor=neighbor.Name;var first=Other(node,web);var last=Other(neighbor,web);
            if(!Parallel(first.Normal,last.Normal)||Math.Abs(BendVector.Dot(first.Normal,web.Normal))>1e-8) {side.Error="Hai cánh đầu cụm chưa song song/vuông góc để xác định phủ bì B/C.";return;}
            double mid=(web.First+web.Second)/2;
            double turn1=BendVector.Dot(node.Root,web.Normal)-mid,turn2=BendVector.Dot(neighbor.Root,web.Normal)-mid;
            if(Math.Abs(turn1)<=Tol||Math.Abs(turn2)<=Tol){side.Error="Chưa xác định được phía tâm hai nếp so với cánh giữa.";return;}
            side.Shape=Math.Sign(turn1)==Math.Sign(turn2)?"C":"Z";side.Dimension=side.Shape=="Z"?"B":"C";
            var n=first.Normal;double sign=BendVector.Dot(n,last.Normal)>=0?1:-1;
            var positions=new[]{first.First,first.Second,last.First*sign,last.Second*sign};
            double low=positions.Min(),high=positions.Max();side.ValueMm=(high-low)*1000;
            var origin=(node.Root+neighbor.Root)*.5+node.Axis*((Math.Max(node.Min,neighbor.Min)+Math.Min(node.Max,neighbor.Max))/2);
            side.Start=origin+n*(low-BendVector.Dot(origin,n));side.End=origin+n*(high-BendVector.Dot(origin,n));
            AddSkinAt(first,low,side.Faces);AddSkinAt(first,high,side.Faces);AddSkinAt(last,low*sign,side.Faces);AddSkinAt(last,high*sign,side.Faces);
        }
        private static void AddSkinAt(BendGraphPanel panel,double offset,List<int> values)
        {
            if(Math.Abs(panel.First-offset)<=Tol*3)values.AddRange(panel.FirstFaces);
            if(Math.Abs(panel.Second-offset)<=Tol*3)values.AddRange(panel.SecondFaces);
        }
        private static void MeasureFree(BendGraphNode node,BendGraphPanel panel,BendPanelBody body,BendMinimumMeasure side)
        {
            var web=Other(node,panel);var direction=web.Normal;var caps=new List<Tuple<double,int>>();
            var capSpans=new List<Tuple<double,double>>();
            foreach(int id in panel.FirstFaces.Concat(panel.SecondFaces)) {
                var skin=body.Faces[id];
                foreach(int e in skin.OuterEdges) {
                    var edge=body.Edges[e];var along=edge.B-edge.A;
                    if(along.Length<=Tol||!Straight(edge,body)||!Parallel(along.Unit,node.Axis))continue;
                    foreach(int f in edge.Faces.Where(f=>f!=id)) {
                        var cap=body.Faces[f];if(cap.Kind!=1||!Parallel(cap.Normal,direction))continue;
                        var adjacent=new HashSet<int>(cap.Edges.SelectMany(n=>body.Edges[n].Faces));
                        if(!adjacent.Overlaps(panel.FirstFaces)||!adjacent.Overlaps(panel.SecondFaces))continue;
                        double value=BendVector.Dot(edge.A,direction);
                        if(!caps.Any(c=>Math.Abs(c.Item1-value)<=Tol*3))caps.Add(Tuple.Create(value,f));
                        double t1=BendVector.Dot(edge.A,node.Axis),t2=BendVector.Dot(edge.B,node.Axis);
                        capSpans.Add(Tuple.Create(Math.Min(t1,t2),Math.Max(t1,t2)));
                    }
                }
            }
            if(caps.Count!=1){side.Error=caps.Count==0?"Chưa chứng minh được đầu tự do phẳng của cánh L.":"Đầu tự do có nhiều mức phủ bì; chưa xác định duy nhất A.";return;}
            double covered=node.Min;
            foreach(var span in capSpans.OrderBy(s=>s.Item1)) {
                if(span.Item1>covered+Tol)break;
                covered=Math.Max(covered,span.Item2);
            }
            if(covered<node.Max-Tol){side.Error="Đầu tự do phẳng chỉ phủ một phần chiều dài; chưa chứng minh được A trên toàn bộ nếp chấn.";return;}
            side.Shape="L";side.Dimension="A";side.Neighbor="Đầu tự do";
            double capOffset=caps[0].Item1;
            double wall=Math.Abs(capOffset-web.First)>=Math.Abs(capOffset-web.Second)?web.First:web.Second;
            side.ValueMm=Math.Abs(capOffset-wall)*1000;
            var origin=node.Root+node.Axis*((node.Min+node.Max)/2);
            side.Start=origin+direction*(wall-BendVector.Dot(origin,direction));side.End=origin+direction*(capOffset-BendVector.Dot(origin,direction));
            AddSkinAt(web,wall,side.Faces);side.Faces.Add(caps[0].Item2);
        }
    }
}
