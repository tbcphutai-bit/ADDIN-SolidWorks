using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands
{
    internal sealed class BendMinimumCylinderBand
    {
        internal double Radius, Min, Max;
        internal bool Sense;
        internal static List<double> PairedThicknesses(IList<BendMinimumCylinderBand> bands)
        {
            var result=new List<double>();
            for(int i=0;i<bands.Count;i++)for(int j=i+1;j<bands.Count;j++) {
                var a=bands[i];var b=bands[j];double thickness=Math.Abs(a.Radius-b.Radius);
                if(a.Sense==b.Sense || thickness<=0.0000001 || Math.Min(a.Max,b.Max)-Math.Max(a.Min,b.Min)<=0.0000001)continue;
                if(!result.Any(t=>Math.Abs(t-thickness)<=0.0000001))result.Add(thickness);
            }
            return result;
        }
    }

    // Only managed points survive a Boolean section. Never keep or reuse a
    // temporary native body consumed by Operations2. One cache lives for exactly
    // one model/configuration/update-stamp scan and is cleared on Dispose.
    internal sealed class BendMinimumSectionCache
    {
        internal sealed class Entry
        {
            internal object Body;
            internal BendVector Axis, Origin, X, Y;
            internal double Position;
            internal string Error;
            internal readonly List<SectionProfilePlanner.Curve> Curves=new List<SectionProfilePlanner.Curve>();
        }
        private readonly List<Entry> entries=new List<Entry>();
        internal int KernelSections { get; private set; }
        internal int CacheHits { get; private set; }
        internal Entry Find(object body,BendVector axis,double position)
        {
            var found=entries.FirstOrDefault(e=>ReferenceEquals(e.Body,body) && (e.Axis-axis).Length<=1e-12 && Math.Abs(e.Position-position)<=1e-12);
            if(found!=null)CacheHits++;
            return found;
        }
        internal void Add(Entry value)
        {
            KernelSections++;
            if(entries.Count>=256)entries.RemoveAt(0);
            entries.Add(value);
        }
        internal static List<SectionProfilePlanner.Curve> InFrame(Entry source,BendVector origin,BendVector x,BendVector y)
        {
            Func<SectionProfilePlanner.Point,SectionProfilePlanner.Point> project=p=> {
                var world=source.Origin+source.X*p.X+source.Y*p.Y;
                return new SectionProfilePlanner.Point(BendVector.Dot(world-origin,x),BendVector.Dot(world-origin,y));
            };
            return source.Curves.Select(c=>new SectionProfilePlanner.Curve {
                A=project(c.A),B=project(c.B),Center=c.IsArc?project(c.Center):new SectionProfilePlanner.Point(),
                IsArc=c.IsArc,SweepAngleRadians=c.SweepAngleRadians,Source=new object()
            }).ToList();
        }
        internal void Clear(){entries.Clear();}
    }

    internal sealed class BendMinimumSweepGeometry
    {
        internal sealed class EdgeData
        {
            internal int Kind;
            internal BendVector A,B,Center,Normal,Zero,Quarter;
            internal double Radius,UMin,UMax;
            internal bool AnalyticCircle;
        }
        internal sealed class FaceData
        {
            internal int Kind;
            internal BendVector Direction;
            internal double[] Box;
        }
        internal readonly List<EdgeData> Edges=new List<EdgeData>();
        internal readonly List<FaceData> Faces=new List<FaceData>();
    }

    internal static partial class BendMinimumSolidReader
    {
        private static BendMinimumSweepGeometry ReadSweepGeometry(Body2 body,object[] faces)
        {
            var result=new BendMinimumSweepGeometry();
            var edges=body.GetEdges() as object[];
            if(edges==null)throw new InvalidOperationException("Không đọc được cạnh để chọn mặt cắt.");
            foreach(var item in edges) {
                var value=new BendMinimumSweepGeometry.EdgeData();
                try {
                    var edge=item as Edge;var curve=edge?.GetCurve() as Curve;var data=edge?.GetCurveParams3() as CurveParamData;
                    if(curve!=null&&data!=null) {
                        value.A=BendVector.From((double[])data.StartPoint);value.B=BendVector.From((double[])data.EndPoint);
                        value.Kind=SourceEdgeKind(edge,out value.Center,out value.Normal,out value.Radius);
                        if(value.Kind==2) {
                            value.AnalyticCircle=curve.IsCircle();value.UMin=data.UMinValue;value.UMax=data.UMaxValue;
                            if(value.AnalyticCircle) {
                                value.Zero=BendVector.From((double[])curve.Evaluate2(0,0))-value.Center;
                                value.Quarter=BendVector.From((double[])curve.Evaluate2(Math.PI/2,0))-value.Center;
                            }
                        }
                    }
                } catch(COMException) {value.Kind=0;}
                result.Edges.Add(value);
            }
            foreach(var item in faces) {
                var value=new BendMinimumSweepGeometry.FaceData();
                try {
                    var face=item as Face2;var surface=face?.GetSurface() as Surface;
                    value.Box=face?.GetBox() as double[];
                    if(surface!=null&&surface.IsPlane()){value.Kind=1;value.Direction=BendVector.From((double[])surface.PlaneParams).Unit;}
                    else if(surface!=null&&surface.IsCylinder()){value.Kind=2;var p=(double[])surface.CylinderParams;value.Direction=new BendVector(p[3],p[4],p[5]).Unit;}
                } catch(COMException) {value.Kind=0;}
                result.Faces.Add(value);
            }
            return result;
        }
        // Endpoints/extrema divide the sweep into bands. A single midpoint is
        // sufficient only when every intersecting surface AND trim edge is
        // invariant along the bend axis. Variable/unknown bands retain all three
        // samples and can never turn a sampled pass into a certified full pass.
        internal static void BuildCuts(BendMinimumSite site,BendMinimumSweepGeometry geometry)
        {
            var values=new List<double>{site.Min,site.Max};
            var varying=new List<Tuple<double,double>>();
            Action<double,double> vary=(a,b)=>varying.Add(Tuple.Create(Math.Min(a,b),Math.Max(a,b)));
            foreach(var edge in geometry.Edges) {
                double a=BendVector.Dot(edge.A,site.Axis),b=BendVector.Dot(edge.B,site.Axis);values.Add(a);values.Add(b);
                if(edge.Kind==1) {
                    if((edge.B-edge.A).Length>Tolerance) {
                        var direction=(edge.B-edge.A).Unit;
                        if(Math.Abs(BendVector.Dot(direction,site.Axis))>1e-10 && BendVector.Cross(direction,site.Axis).Length>1e-10)vary(a,b);
                    }
                } else if(edge.Kind==2) {
                    double alignment=Math.Abs(BendVector.Dot(edge.Normal,site.Axis));
                    if(BendVector.Cross(edge.Normal,site.Axis).Length>1e-10) {
                        double center=BendVector.Dot(edge.Center,site.Axis),extent=edge.Radius*Math.Sqrt(Math.Max(0,1-alignment*alignment));
                        vary(center-extent,center+extent);
                        if(edge.AnalyticCircle) {
                            double phase=Math.Atan2(BendVector.Dot(edge.Quarter,site.Axis),BendVector.Dot(edge.Zero,site.Axis));
                            for(int k=-4;k<=4;k++) {
                                double u=phase+k*Math.PI;
                                if(u>=edge.UMin-Tolerance&&u<=edge.UMax+Tolerance)
                                    values.Add(BendVector.Dot(edge.Center+edge.Zero*Math.Cos(u)+edge.Quarter*Math.Sin(u),site.Axis));
                            }
                        } else {values.Add(center-extent);values.Add(center+extent);}
                    }
                } else vary(site.Min,site.Max);
            }
            foreach(var face in geometry.Faces) {
                double alignment=Math.Abs(BendVector.Dot(face.Direction,site.Axis));
                bool parallel=BendVector.Cross(face.Direction,site.Axis).Length<=1e-10;
                bool invariant=face.Kind==1?(alignment<=1e-10||parallel):face.Kind==2&&parallel;
                if(invariant)continue;
                if(face.Box==null||face.Box.Length<6){vary(site.Min,site.Max);continue;}
                var projections=new List<double>();
                for(int i=0;i<8;i++)projections.Add(BendVector.Dot(new BendVector(face.Box[(i&1)==0?0:3],face.Box[(i&2)==0?1:4],face.Box[(i&4)==0?2:5]),site.Axis));
                vary(projections.Min(),projections.Max());
            }
            var ordered=values.Where(v=>v>=site.Min-Tolerance&&v<=site.Max+Tolerance).OrderBy(v=>v).ToList();
            var events=new List<double>();foreach(double v in ordered)if(events.Count==0||Math.Abs(v-events.Last())>Tolerance)events.Add(Math.Max(site.Min,Math.Min(site.Max,v)));
            site.Cuts.Clear();bool uniform=true;
            for(int i=0;i+1<events.Count;i++) {
                double a=events[i],b=events[i+1],length=b-a;if(length<=Tolerance)continue;
                bool variable=varying.Any(range=>range.Item1<b-1e-12&&range.Item2>a+1e-12);
                if(variable){double margin=Math.Min(0.000001,length*.001);site.Cuts.Add(a+margin);uniform=false;}
                site.Cuts.Add((a+b)/2);
                if(variable){double margin=Math.Min(0.000001,length*.001);site.Cuts.Add(b-margin);}
            }
            site.UniformSweep=uniform;
            site.CoverageNote=uniform?"Mặt cắt chấn dạng đùn thẳng; đã kiểm tra các khoảng dọc đường chấn.":"Có cạnh nghiêng, lỗ/cung theo hướng khác hoặc spline; các mặt cắt lấy mẫu chưa chứng minh toàn bộ đường chấn.";
            if(site.Cuts.Count>900){site.Cuts.Clear();site.UniformSweep=false;site.CoverageNote="Có quá nhiều khoảng mặt cắt (trên 900). Cần chia vùng kiểm tra; không kết luận đạt.";}
        }
        internal static BendMinimumSample ReadCachedSection(ISldWorks app,BendMinimumSite site,double position,BendMinimumSectionCache cache)
        {
            var entry=cache.Find(site.Body,site.Axis,position);
            var origin=site.Root+site.Axis*position;var x=site.Reference.Unit;var y=BendVector.Cross(site.Axis,x).Unit;
            if(entry==null) {
                entry=new BendMinimumSectionCache.Entry{Body=site.Body,Axis=site.Axis,Position=position,Origin=origin,X=x,Y=y};
                var boundary=new List<SectionProfilePlanner.Curve>();
                var result=ReadSection(app,site,position,boundary);
                entry.Curves.AddRange(boundary.Select(c=>new SectionProfilePlanner.Curve {
                    A=c.A,B=c.B,Center=c.Center,IsArc=c.IsArc,SweepAngleRadians=c.SweepAngleRadians,Source=new object()
                }));
                if(entry.Curves.Count==0)entry.Error=result.Error;
                cache.Add(entry);return result;
            }
            var sample=new BendMinimumSample{PositionMm=(position-site.Min)*1000};
            if(entry.Error!=null){sample.Error=entry.Error;return sample;}
            var curves=BendMinimumSectionCache.InFrame(entry,origin,x,y);var center=new SectionProfilePlanner.Point(0,0);
            var seed=curves.FirstOrDefault(c=>c.IsArc&&(c.Center-center).Length<=Tolerance*3&&site.Radii.Any(r=>Math.Abs(r-(c.A-c.Center).Length)<=Tolerance*3));
            if(seed==null){sample.Error="Không tìm thấy cung của đúng đường chấn trong mặt cắt; không chọn biên dạng khác.";return sample;}
            List<BendMinimumSection> sections;string reason;
            bool measured=BendMinimumSectionReader.TryMeasureAll(curves,seed.Source,out sections,out reason);sample.Error=reason;
            if(measured){sample.Sections.AddRange(sections);sample.Section=sections[0];sample.ThicknessMm=sections[0].ThicknessMm;}
            return sample;
        }
    }
}
