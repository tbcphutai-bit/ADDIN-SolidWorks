using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendEnvelope
{
    // Exact plane/cylinder patches, including cylinders perpendicular to the
    // bend axis. Between ALL analytic edge extrema the section is a continuous
    // family of LINE/ARC loops. Full-interval endpoint bounds enclose that
    // family's boundaries; the representative section alone proves nothing.
    internal sealed class OrthogonalNativeEnvelopeCompiler
    {
        private const double Joint=1e-10, Parallel=1e-12;
        // Includes native source allowance, frame arithmetic and accepted axis
        // alignment error under the explicit 1e6 mm coordinate bound. Root
        // sensitivity is propagated, not hidden in a constant XY chord band.
        private const double Allowance=4*PrismaticEnvelopeCompiler.SourceToleranceMm;
        private readonly EnvelopeBinding binding;
        private readonly NativeEnvelopeBody shell;
        private readonly Dictionary<int,PlacementEdgeSnapshot> edges;
        private readonly Dictionary<int,Circle> circles;
        private readonly Dictionary<int,Cylinder> cylinders=new Dictionary<int,Cylinder>();
        private readonly BendWorkFrame frame;

        private OrthogonalNativeEnvelopeCompiler(EnvelopeBinding binding,NativeEnvelopeBody shell)
        {
            this.binding=binding;this.shell=shell;frame=binding.WorkFrame;
            edges=shell.Edges.ToDictionary(e=>e.Id);
            circles=shell.Circles.ToDictionary(c=>c.EdgeId,c=>new Circle(edges[c.EdgeId],c));
        }
        private static InvalidDataException Unsupported(string reason,int id) => new InvalidDataException(reason+":"+id);
        internal static SheetEnvelopeCapture Read(EnvelopeBinding binding,NativeEnvelopeBody shell,CancellationToken token)
        {
            try
            {
                PrismaticEnvelopeCompiler.ValidateShell(shell,token);
                var proof=new OrthogonalNativeEnvelopeCompiler(binding,shell);
                var stations=proof.ValidateAndPartition(token);
                var primitives=new List<AxialSolidPrimitive>();
                for(int i=1;i<stations.Count;i++)
                {
                    token.ThrowIfCancellationRequested();
                    var interval=new AxialInterval(stations[i-1],stations[i]);
                    if(interval.LengthMm<4*Allowance)throw new InvalidDataException("UNRESOLVED_THIN_AXIAL_FEATURE");
                    var section=proof.Section(interval.MidMm);
                    if(section.Curves.Count==0)continue;
                    if(!section.Material.Valid)throw new InvalidDataException("UNSUPPORTED_SLICE_TOPOLOGY");
                    primitives.Add(new SliceFamily(proof,interval));
                }
                if(primitives.Count==0)throw new InvalidDataException("EMPTY_NATIVE_SECTION_FAMILY");
                return new SheetEnvelopeCapture(binding,primitives,EnvelopeCoverageTier.AxialSlicesWithCertifiedCoverage,
                    "CompleteOrthogonalPlaneCylinderSectionFamilyV1",binding.Model.SimulationOnly,
                    Allowance,new[]{"FINAL_MODEL_ONLY","ANALYTIC_EDGE_EXTREMA_AND_FULL_INTERVAL_BOUNDARY_BOUNDS"});
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex) when(ex is InvalidDataException||ex is InvalidOperationException||ex is ArgumentException||ex is CollisionAbort)
            {
                return new SheetEnvelopeCapture(binding,null,EnvelopeCoverageTier.UnknownCoverage,"NativeAnalyticFamilyProofUnavailable",
                    binding.Model.SimulationOnly,PrismaticEnvelopeCompiler.SourceToleranceMm,
                    new[]{"AXIAL_COVERAGE_UNKNOWN",ex is InvalidDataException?ex.Message:"INVALID_NATIVE_SECTION_FAMILY"});
            }
        }
        private List<double> ValidateAndPartition(CancellationToken token)
        {
            if(frame.OriginMm.Length>1000000)throw new InvalidDataException("UNSUPPORTED_NATIVE_FRAME_MAGNITUDE");
            if(shell.Edges.Any(e=>e.Kind!=1&&e.Kind!=2))throw new InvalidDataException("UNSUPPORTED_NATIVE_TRIM_CURVE");
            if(shell.Edges.Any(e=>e.Kind==2&&!circles.ContainsKey(e.Id)))throw new InvalidDataException("MISSING_NATIVE_CIRCLE_PLANE_AND_SENSE");
            var events=new List<double>();
            foreach(var edge in shell.Edges)
            {
                token.ThrowIfCancellationRequested();
                if(new[]{edge.A,edge.B}.Any(p=>p.Length>1000000))throw Unsupported("UNSUPPORTED_NATIVE_COORDINATE_MAGNITUDE_EDGE",edge.Id);
                events.Add(frame.AxialDistance(edge.A));events.Add(frame.AxialDistance(edge.B));
                if(edge.Kind==2)
                {
                    var circle=circles[edge.Id];double alignment=Math.Abs(PlacementVector3.Dot(circle.Axis,frame.BendAxis));
                    if(circle.Center.Length>1000000||edge.Arc.MidMm.Length>1000000)throw Unsupported("UNSUPPORTED_NATIVE_ARC_MAGNITUDE_EDGE",edge.Id);
                    if(alignment>Parallel&&Math.Abs(alignment-1)>Parallel)throw Unsupported("UNSUPPORTED_OBLIQUE_CIRCLE_EDGE",edge.Id);
                    events.AddRange(circle.Extrema(frame));
                }
            }
            foreach(var face in shell.Faces)
            {
                token.ThrowIfCancellationRequested();
                if(face.PointMm.Length>1000000)throw Unsupported("UNSUPPORTED_NATIVE_SURFACE_MAGNITUDE_FACE",face.Id);
                if(face.Kind==1)
                {
                    double alignment=Math.Abs(PlacementVector3.Dot(face.OutwardNormal,frame.BendAxis));
                    if(alignment>Parallel&&Math.Abs(alignment-1)>Parallel)throw Unsupported("UNSUPPORTED_SLOPED_NATIVE_FACE",face.Id);
                    ValidatePlane(face);
                }
                else if(face.Kind==2)
                {
                    double alignment=Math.Abs(PlacementVector3.Dot(face.Axis,frame.BendAxis));
                    if(alignment>Parallel&&Math.Abs(alignment-1)>Parallel)throw Unsupported("UNSUPPORTED_OBLIQUE_CYLINDER_FACE",face.Id);
                    cylinders.Add(face.Id,new Cylinder(face,edges,circles));
                }
                else throw Unsupported("UNSUPPORTED_NATIVE_SURFACE_FACE",face.Id);
            }
            var stations=new List<double>();
            foreach(double z in events.OrderBy(z=>z))if(stations.Count==0||z-stations.Last()>Joint)stations.Add(z);
            if(stations.Count<2||stations.Count>4096)throw new InvalidDataException("INVALID_AXIAL_PARTITION");
            return stations;
        }
        private void ValidatePlane(NativeEnvelopeFace face)
        {
            if(Math.Abs(face.OutwardNormal.Length-1)>Parallel)throw Unsupported("INVALID_NATIVE_PLANE_NORMAL_FACE",face.Id);
            var local=BendWorkFrame.Observed("NATIVE_FACE_PROOF",face.PointMm,face.OutwardNormal);
            var all=new List<ToolCurve>();
            foreach(var loop in face.Loops)
            {
                var curves=new List<ToolCurve>();
                foreach(var coedge in loop.Edges)
                {
                    var edge=edges[coedge.EdgeId];ToolCurve curve;
                    if(Math.Abs(PlacementVector3.Dot(edge.A-face.PointMm,face.OutwardNormal))>Joint||
                        Math.Abs(PlacementVector3.Dot(edge.B-face.PointMm,face.OutwardNormal))>Joint)
                        throw Unsupported("INCONSISTENT_NATIVE_PLANE_TRIM_FACE",face.Id);
                    if(edge.Kind==1)curve=ToolCurve.Line(local.Project(edge.A),local.Project(edge.B),new ToolTransform());
                    else
                    {
                        var circle=circles[edge.Id];
                        if(Math.Abs(Math.Abs(PlacementVector3.Dot(circle.Axis,face.OutwardNormal))-1)>Parallel||
                            Math.Abs(PlacementVector3.Dot(circle.Center-face.PointMm,face.OutwardNormal))>Joint)
                            throw Unsupported("INCONSISTENT_NATIVE_PLANAR_ARC_FACE",face.Id);
                        curve=circle.Project(local);
                    }
                    curves.Add(coedge.Forward?curve:Reverse(curve));
                }
                double area=TwiceArea(curves);
                if(area*(loop.IsOuter?1:-1)<=PrismaticEnvelopeCompiler.SourceToleranceMm*curves.Sum(CollisionGeometry.Length))
                    throw Unsupported("INCONSISTENT_NATIVE_PLANE_LOOP_SENSE_FACE",face.Id);
                all.AddRange(curves);
            }
            if(!PrismaticEnvelopeCompiler.OrientedMaterial(all,binding).Valid)throw Unsupported("INVALID_NATIVE_PLANAR_REGION_FACE",face.Id);
        }
        private SectionData Section(double z)
        {
            var curves=new List<ToolCurve>();var moving=new List<MovingPoint>();
            foreach(var face in shell.Faces)
            {
                if(face.Kind==1)
                {
                    if(Math.Abs(PlacementVector3.Dot(face.OutwardNormal,frame.BendAxis))>.5)continue;
                    var points=face.Loops.SelectMany(l=>l.Edges).Select(c=>c.EdgeId).Distinct().SelectMany(id=>Crossings(edges[id],z)).ToArray();
                    var direction=PlacementVector3.Cross(frame.BendAxis,face.OutwardNormal);
                    points=points.OrderBy(p=>PlacementVector3.Dot(p.Point,direction)).ToArray();
                    if((points.Length&1)!=0)throw Unsupported("UNRESOLVED_PLANAR_SECTION_FACE",face.Id);
                    for(int i=0;i<points.Length;i+=2)
                    {curves.Add(ToolCurve.Line(frame.Project(points[i].Point),frame.Project(points[i+1].Point),new ToolTransform()));moving.Add(points[i]);moving.Add(points[i+1]);}
                }
                else
                {
                    var cylinder=cylinders[face.Id];
                    if(Math.Abs(PlacementVector3.Dot(face.Axis,frame.BendAxis))>.5)
                    {
                        double a=frame.AxialDistance(cylinder.First.Center),b=frame.AxialDistance(cylinder.Second.Center);
                        if(z<=Math.Min(a,b)||z>=Math.Max(a,b))continue;
                        var curve=cylinder.First.Project(frame);bool ccw=!face.ReversedSurfaceSense;
                        curves.Add((curve.SweepRadians>0)==ccw?curve:Reverse(curve));
                    }
                    else foreach(var a in CircleCrossings(cylinder.First,z))
                    {
                        var target=a.Point+(cylinder.Second.Center-cylinder.First.Center);
                        var b=CircleCrossings(cylinder.Second,z).Where(p=>(p.Point-target).Length<=Joint).ToArray();
                        if(b.Length!=1)throw Unsupported("NONMATCHING_CYLINDER_SECTION_FACE",face.Id);
                        var radial=(a.Point-cylinder.First.Center).Unit*(face.ReversedSurfaceSense?-1:1);
                        var direction=PlacementVector3.Cross(frame.BendAxis,radial);
                        var curve=ToolCurve.Line(frame.Project(a.Point),frame.Project(b[0].Point),new ToolTransform());
                        curves.Add(PlacementVector3.Dot(b[0].Point-a.Point,direction)>0?curve:Reverse(curve));moving.Add(a);moving.Add(b[0]);
                    }
                }
            }
            return new SectionData(curves,curves.Count==0?null:PrismaticEnvelopeCompiler.OrientedMaterial(curves,binding),moving);
        }
        private IEnumerable<MovingPoint> Crossings(PlacementEdgeSnapshot edge,double z)
        {
            if(edge.Kind==2)return CircleCrossings(circles[edge.Id],z);
            double a=frame.AxialDistance(edge.A),b=frame.AxialDistance(edge.B);
            if(z<=Math.Min(a,b)||z>=Math.Max(a,b)||Math.Abs(a-b)<=Joint)return new MovingPoint[0];
            var direction=(edge.B-edge.A)*(1/(b-a));
            return new[]{new MovingPoint(edge.A+direction*(z-a),range=>
                BoundPoint(edge.A,direction,(new CollisionInterval(range.StartMm,range.EndMm)-CollisionInterval.P(a)+new CollisionInterval(-Allowance,Allowance)),null,CollisionInterval.P(0)))};
        }
        private IEnumerable<MovingPoint> CircleCrossings(Circle circle,double z)
        {
            if(Math.Abs(PlacementVector3.Dot(circle.Axis,frame.BendAxis))>.5)return new MovingPoint[0];
            var vertical=(frame.BendAxis-circle.Axis*PlacementVector3.Dot(circle.Axis,frame.BendAxis)).Unit;
            var horizontal=PlacementVector3.Cross(circle.Axis,vertical).Unit;double factor=PlacementVector3.Dot(vertical,frame.BendAxis);
            double delta=(z-frame.AxialDistance(circle.Center))/factor,square=circle.Radius*circle.Radius-delta*delta;
            if(square<=0)return new MovingPoint[0];
            var result=new List<MovingPoint>();
            foreach(int sign in new[]{-1,1})
            {
                var point=circle.Center+vertical*delta+horizontal*(sign*Math.Sqrt(square));
                if(!circle.Contains(point))continue;
                result.Add(new MovingPoint(point,range=> {
                    var d=(new CollisionInterval(range.StartMm,range.EndMm)-CollisionInterval.P(frame.AxialDistance(circle.Center))+
                        new CollisionInterval(-Allowance,Allowance))/CollisionInterval.P(factor);
                    var h=(new CollisionInterval(circle.Radius-Allowance,circle.Radius+Allowance).Square()-d.Square()).Sqrt()*CollisionInterval.P(sign);
                    return BoundPoint(circle.Center,vertical,d,horizontal,h);
                }));
            }
            return result;
        }
        private CollisionInterval[] BoundPoint(PlacementVector3 origin,PlacementVector3 a,CollisionInterval u,PlacementVector3? b,CollisionInterval v)
        {
            var offset=origin-frame.OriginMm;
            return new[]{frame.X,frame.Y}.Select(axis=>CollisionInterval.P(PlacementVector3.Dot(offset,axis))+
                CollisionInterval.P(PlacementVector3.Dot(a,axis))*u+CollisionInterval.P(b.HasValue?PlacementVector3.Dot(b.Value,axis):0)*v+
                new CollisionInterval(-Allowance,Allowance)).ToArray();
        }
        private sealed class SectionData
        {
            internal readonly List<ToolCurve> Curves;internal readonly CollisionMaterial2D Material;internal readonly List<MovingPoint> Moving;
            internal SectionData(List<ToolCurve> curves,CollisionMaterial2D material,List<MovingPoint> moving){Curves=curves;Material=material;Moving=moving;}
        }
        private sealed class MovingPoint
        {
            internal readonly PlacementVector3 Point;internal readonly Func<AxialInterval,CollisionInterval[]> Bound;
            internal MovingPoint(PlacementVector3 point,Func<AxialInterval,CollisionInterval[]> bound){Point=point;Bound=bound;}
        }
        private sealed class SliceFamily : AxialSolidPrimitive
        {
            private readonly OrthogonalNativeEnvelopeCompiler proof;
            public override bool Valid=>true;
            public override string RequiredFrameId=>proof.binding.FrameId;
            public override string Canonical=>ToolEvidenceCanonical.Hash("NativePlaneCylinderFamilyV1",proof.shell.SourceSha256,
                proof.binding.FrameId,ToolEvidenceCanonical.Number(Domain.StartMm),ToolEvidenceCanonical.Number(Domain.EndMm));
            internal SliceFamily(OrthogonalNativeEnvelopeCompiler proof,AxialInterval domain):base(domain){this.proof=proof;}
            internal override IEnumerable<CollisionComponent2D> Section(double z,string frame,string prefix)
            {
                var data=proof.Section(z);return data.Material==null?new CollisionComponent2D[0]:Move(data.Material,prefix,0,0,
                    ErrorBound(data,new AxialInterval(CollisionInterval.Down(z),CollisionInterval.Up(z))));
            }
            internal override IEnumerable<CollisionComponent2D> Enclose(AxialInterval range,string frame,string prefix)
            {
                var data=proof.Section(range.MidMm);if(data.Material==null)return new CollisionComponent2D[0];
                return Move(data.Material,prefix,0,0,ErrorBound(data,range));
            }
            private double ErrorBound(SectionData data,AxialInterval range)
            {
                double maximum=0;
                foreach(var point in data.Moving)
                {
                    var xy=proof.frame.Project(point.Point);var bound=point.Bound(range);
                    var dx=bound[0]-CollisionInterval.P(xy.X);var dy=bound[1]-CollisionInterval.P(xy.Y);
                    maximum=Math.Max(maximum,(dx.Square()+dy.Square()).Sqrt().Hi);
                }
                return CollisionInterval.Up(maximum+Allowance);
            }
        }
        private sealed class Circle
        {
            internal readonly PlacementEdgeSnapshot Edge;
            internal readonly PlacementVector3 Axis,Center,U,V;
            internal readonly double Radius,Sweep;
            internal Circle(PlacementEdgeSnapshot edge,NativeCircleDefinition definition)
            {
                Edge=edge;Axis=definition.Axis;Center=edge.Arc?.CenterMm??throw Unsupported("MISSING_NATIVE_ARC_EDGE",edge.Id);Radius=edge.Arc.RadiusMm;
                if(Math.Abs(Axis.Length-1)>Parallel||Radius<=Allowance||Radius>10000)throw Unsupported("INVALID_NATIVE_CIRCLE_EDGE",edge.Id);
                U=(edge.A-Center).Unit;V=PlacementVector3.Cross(Axis,U).Unit;
                if(new[]{edge.A,edge.B,edge.Arc.MidMm}.Any(p=>Math.Abs((p-Center).Length-Radius)>Joint||Math.Abs(PlacementVector3.Dot(p-Center,Axis))>Joint))
                    throw Unsupported("INCONSISTENT_NATIVE_CIRCLE_EDGE",edge.Id);
                double end=Angle(edge.B),mid=Angle(edge.Arc.MidMm);Sweep=edge.Arc.FullCircle?(definition.CurveAndEdgeSameSense?2:-2)*Math.PI:
                    (mid<=end+1e-12?end:end-2*Math.PI);
                if(Math.Abs(Sweep)<1e-12)throw Unsupported("AMBIGUOUS_NATIVE_ARC_EDGE",edge.Id);
            }
            private double Angle(PlacementVector3 p)=>Positive(Math.Atan2(PlacementVector3.Dot(p-Center,V),PlacementVector3.Dot(p-Center,U)));
            internal bool Contains(PlacementVector3 p)=>Edge.Arc.FullCircle||(Sweep>0?Angle(p):Positive(-Angle(p)))<=Math.Abs(Sweep)+1e-12;
            internal IEnumerable<double> Extrema(BendWorkFrame frame)
            {
                double angle=Math.Atan2(PlacementVector3.Dot(V,frame.BendAxis),PlacementVector3.Dot(U,frame.BendAxis));
                foreach(double t in new[]{angle,angle+Math.PI})
                {
                    var point=Center+(U*Math.Cos(t)+V*Math.Sin(t))*Radius;
                    if(Contains(point))yield return frame.AxialDistance(point);
                }
            }
            internal ToolCurve Project(BendWorkFrame frame)=>ToolCurve.Arc(frame.Project(Edge.A),frame.Project(Edge.B),frame.Project(Center),Radius,
                Sweep*(PlacementVector3.Dot(Axis,frame.BendAxis)>0?1:-1),new ToolTransform());
            internal PlacementVector3 MidTangent => PlacementVector3.Cross(Axis,(Edge.Arc.MidMm-Center).Unit)*(Sweep>0?1:-1);
        }
        private sealed class Cylinder
        {
            internal readonly Circle First,Second;
            internal Cylinder(NativeEnvelopeFace face,IDictionary<int,PlacementEdgeSnapshot> edges,IDictionary<int,Circle> circles)
            {
                var rim=face.Loops.SelectMany(l=>l.Edges).Select(c=>edges[c.EdgeId]).Distinct().ToArray();var arcs=rim.Where(e=>e.Kind==2).ToArray();
                if(arcs.Length!=2||rim.Length!=2&&rim.Length!=4||Math.Abs(face.Axis.Length-1)>Parallel)
                    throw Unsupported("UNSUPPORTED_CYLINDER_PATCH_TOPOLOGY_FACE",face.Id);
                First=circles[arcs[0].Id];Second=circles[arcs[1].Id];var delta=Second.Center-First.Center;
                Func<PlacementVector3,PlacementVector3> radial=p=>p-face.Axis*PlacementVector3.Dot(p,face.Axis);
                if(Math.Abs(First.Radius-face.RadiusMm)>Joint||Math.Abs(Second.Radius-face.RadiusMm)>Joint||
                    Math.Abs(Math.Abs(PlacementVector3.Dot(First.Axis,face.Axis))-1)>Parallel||Math.Abs(Math.Abs(PlacementVector3.Dot(Second.Axis,face.Axis))-1)>Parallel||
                    radial(delta).Length>Joint||radial(First.Center-face.PointMm).Length>Joint||Math.Abs(PlacementVector3.Dot(delta,face.Axis))<Joint||
                    First.Edge.Arc.FullCircle!=Second.Edge.Arc.FullCircle||Math.Abs(Math.Abs(First.Sweep)-Math.Abs(Second.Sweep))>1e-10||
                    !First.Edge.Arc.FullCircle&&(radial(First.Edge.Arc.MidMm-Second.Edge.Arc.MidMm).Length>Joint||
                    Math.Min(radial(First.Edge.A-Second.Edge.A).Length,radial(First.Edge.A-Second.Edge.B).Length)>Joint))
                    throw Unsupported("NONMATCHING_ANALYTIC_CYLINDER_RIMS_FACE",face.Id);
                foreach(var edge in rim.Where(e=>e.Kind==1))if(radial(edge.B-edge.A).Length>Joint)
                    throw Unsupported("UNSUPPORTED_CYLINDER_GENERATOR_FACE",face.Id);
                double middle=PlacementVector3.Dot((First.Center+Second.Center)*.5,face.Axis);
                foreach(var loop in face.Loops)foreach(var coedge in loop.Edges.Where(e=>circles.ContainsKey(e.EdgeId)))
                {
                    var circle=circles[coedge.EdgeId];var normal=(circle.Edge.Arc.MidMm-circle.Center).Unit*(face.ReversedSurfaceSense?-1:1);
                    var conormal=PlacementVector3.Cross(circle.MidTangent*(coedge.Forward?1:-1),normal);
                    if(PlacementVector3.Dot(conormal,face.Axis)*(PlacementVector3.Dot(circle.Center,face.Axis)-middle)<=0)
                        throw Unsupported("INCONSISTENT_CYLINDER_LOOP_SENSE_FACE",face.Id);
                }
            }
        }
        private static ToolCurve Reverse(ToolCurve c)=>c.Kind=="LINE"?ToolCurve.Line(c.End,c.Start,new ToolTransform()):ToolCurve.Arc(c.End,c.Start,c.Center,c.RadiusMm,-c.SweepRadians,new ToolTransform());
        private static double Positive(double a){a%=2*Math.PI;return a<0?a+2*Math.PI:a;}
        private static double TwiceArea(IReadOnlyList<ToolCurve> curves)
        {
            var o=curves[0].Start;
            return curves.Sum(e=>e.Kind=="LINE"?(e.Start.X-o.X)*(e.End.Y-o.Y)-(e.Start.Y-o.Y)*(e.End.X-o.X):
                (e.Center.X-o.X)*(e.End.Y-e.Start.Y)-(e.Center.Y-o.Y)*(e.End.X-e.Start.X)+e.RadiusMm*e.RadiusMm*e.SweepRadians);
        }
    }
}
