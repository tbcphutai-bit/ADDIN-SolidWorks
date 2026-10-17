using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlacement
{
    internal sealed class PlacementArc3D
    {
        public PlacementVector3 CenterMm { get; }
        public PlacementVector3 MidMm { get; }
        public double RadiusMm { get; }
        public bool FullCircle { get; }
        internal PlacementArc3D(PlacementVector3 center,PlacementVector3 middle,double radius,bool full)
        {CenterMm=center;MidMm=middle;RadiusMm=radius;FullCircle=full;}
    }
    internal sealed class PlacementEdgeSnapshot
    {
        public int Id { get; }
        public int Kind { get; }
        public PlacementVector3 A { get; }
        public PlacementVector3 B { get; }
        public PlacementArc3D Arc { get; }
        public IReadOnlyList<int> Faces { get; }
        internal PlacementEdgeSnapshot(int id,int kind,PlacementVector3 a,PlacementVector3 b,IEnumerable<int> faces,PlacementArc3D arc=null)
        {Id=id;Kind=kind;A=a;B=b;Faces=PlacementLists.Copy(faces);Arc=arc;}
    }
    internal sealed class PlacementFaceSnapshot
    {
        public int Id { get; }
        public int Kind { get; }
        public PlacementVector3 Normal { get; }
        public PlacementVector3 PointMm { get; }
        public PlacementVector3 Axis { get; }
        public IReadOnlyList<int> Edges { get; }
        internal PlacementFaceSnapshot(int id,int kind,PlacementVector3 normal,PlacementVector3 point,PlacementVector3 axis,IEnumerable<int> edges)
        {Id=id;Kind=kind;Normal=normal;PointMm=point;Axis=axis;Edges=PlacementLists.Copy(edges);}
    }
    internal sealed class PlacementBodySnapshot
    {
        public string Id { get; }
        public IReadOnlyList<PlacementFaceSnapshot> Faces { get; }
        public IReadOnlyList<PlacementEdgeSnapshot> Edges { get; }
        public ADDIN.Commands.BendEnvelope.NativeEnvelopeBody EnvelopeTopology { get; }
        internal PlacementBodySnapshot(string id,IEnumerable<PlacementFaceSnapshot> faces,IEnumerable<PlacementEdgeSnapshot> edges)
            : this(id,faces,edges,null) { }
        internal PlacementBodySnapshot(string id,IEnumerable<PlacementFaceSnapshot> faces,IEnumerable<PlacementEdgeSnapshot> edges,ADDIN.Commands.BendEnvelope.NativeEnvelopeBody topology)
        {Id=id;Faces=PlacementLists.Copy(faces);Edges=PlacementLists.Copy(edges);EnvelopeTopology=topology;}
    }
    internal sealed class PlacementBendSnapshot
    {
        public int FeatureId { get; }
        public string BodyId { get; }
        public BendWorkFrame Frame { get; }
        public double? ThicknessMm { get; }
        public double BendMinMm { get; }
        public double BendMaxMm { get; }
        internal PlacementBendSnapshot(int id,string body,BendWorkFrame frame,double? thickness,double min,double max)
        {FeatureId=id;BodyId=body;Frame=frame;ThicknessMm=thickness;BendMinMm=min;BendMaxMm=max;}
    }
    internal sealed class PlacementModelCapture
    {
        public PlacementLease Lease { get; } = new PlacementLease();
        public string SnapshotId { get; }
        public int UpdateStamp { get; }
        public string Configuration { get; }
        public IReadOnlyList<PlacementBodySnapshot> Bodies { get; }
        public IReadOnlyList<PlacementBendSnapshot> Bends { get; }
        public bool CompleteBodyCoverage { get; }
        public bool SimulationOnly { get; }
        public IReadOnlyList<SheetSection2D> Sections { get; }
        internal PlacementModelCapture(string key,int stamp,string configuration,IEnumerable<PlacementBodySnapshot> bodies,IEnumerable<PlacementBendSnapshot> bends,bool complete,bool simulation=false)
        {
            SnapshotId=key;UpdateStamp=stamp;Configuration=configuration;Bodies=PlacementLists.Copy(bodies);Bends=PlacementLists.Copy(bends);CompleteBodyCoverage=complete;SimulationOnly=simulation;
            Sections=PlacementLists.Copy(Bends.Select(b=>BendSheetSection.Read(this,b)));
        }
    }
    internal static class BendSheetSection
    {
        private const double Tol=1e-5;
        internal static SheetSection2D Read(PlacementModelCapture model,PlacementBendSnapshot bend)
        {
            var reasons=new List<string>();var curves=new List<ToolCurve>();
            try
            {
                if(!model.CompleteBodyCoverage||model.Bodies.Count!=1)throw new InvalidDataException("INCOMPLETE_OR_MULTIBODY_ENVELOPE");
                var body=model.Bodies.Single();var frame=bend.Frame;
                if(frame==null||body.Id!=bend.BodyId||!bend.ThicknessMm.HasValue||bend.ThicknessMm<=0)throw new InvalidDataException("MISSING_BEND_FRAME_OR_THICKNESS");
                if(body.Edges.Count==0||body.Faces.Count==0)throw new InvalidDataException("INCOMPLETE_BODY_TOPOLOGY");
                var z=body.Edges.SelectMany(e=>new[]{frame.AxialDistance(e.A),frame.AxialDistance(e.B)}).ToArray();
                double min=z.Min(),max=z.Max();if(max-min<=Tol)throw new InvalidDataException("DEGENERATE_AXIAL_SPAN");
                if(bend.BendMinMm<min-Tol||bend.BendMaxMm>max+Tol||bend.BendMaxMm-bend.BendMinMm<=Tol||0<min-Tol||0>max+Tol)
                    throw new InvalidDataException("BEND_OUTSIDE_UNIFORM_BODY_SPAN");
                var lowerCaps=new HashSet<int>();var upperCaps=new HashSet<int>();
                foreach(var face in body.Faces)
                {
                    if(face.Kind==1)
                    {
                        double axial=Math.Abs(PlacementVector3.Dot(face.Normal,frame.BendAxis));
                        if(axial<1e-8)continue;
                        if(Math.Abs(axial-1)>1e-8)throw new InvalidDataException("NON_EXTRUDED_PLANAR_FACE");
                        double value=frame.AxialDistance(face.PointMm);
                        if(Math.Abs(value-min)<=Tol)lowerCaps.Add(face.Id);else if(Math.Abs(value-max)<=Tol)upperCaps.Add(face.Id);
                        else throw new InvalidDataException("INTERNAL_TRIM_OR_TAB");
                    }
                    else if(face.Kind==2)
                    {if(Math.Abs(Math.Abs(PlacementVector3.Dot(face.Axis,frame.BendAxis))-1)>1e-8)throw new InvalidDataException("CROSS_AXIS_CYLINDER_OR_HOLE");}
                    else throw new InvalidDataException("UNSUPPORTED_BODY_SURFACE");
                }
                if(lowerCaps.Count==0||upperCaps.Count==0)throw new InvalidDataException("MISSING_END_CAP_TOPOLOGY");
                foreach(var e in body.Edges)
                {
                    double a=frame.AxialDistance(e.A),b=frame.AxialDistance(e.B);
                    bool low=Math.Abs(a-min)<=Tol&&Math.Abs(b-min)<=Tol,high=Math.Abs(a-max)<=Tol&&Math.Abs(b-max)<=Tol;
                    bool axial=e.Kind==1&&((Math.Abs(a-min)<=Tol&&Math.Abs(b-max)<=Tol)||(Math.Abs(b-min)<=Tol&&Math.Abs(a-max)<=Tol))&&ToolPoint.Distance(frame.Project(e.A),frame.Project(e.B))<=Tol;
                    if(!low&&!high&&!axial)throw new InvalidDataException("AXIAL_VARIATION_TAB_HOLE_OR_RELIEF");
                    if(e.Kind!=1&&e.Kind!=2)throw new InvalidDataException("UNSUPPORTED_TRIM_CURVE");
                    if(e.Kind==2&&(e.Arc==null||!low&&!high||Math.Abs(frame.AxialDistance(e.Arc.MidMm)-(low?min:max))>Tol||
                        Math.Abs(frame.AxialDistance(e.Arc.CenterMm)-(low?min:max))>Tol))throw new InvalidDataException("MISSING_OR_NONPLANAR_ARC_EVIDENCE");
                    // Internal seams between coplanar cap faces are not a body
                    // boundary. Every selected edge still belongs to the full
                    // actual cap topology, including holes and return walls.
                    if(low&&e.Faces.Count(f=>lowerCaps.Contains(f))==1)curves.Add(Project(e,frame));
                }
                if(curves.Count==0)throw new InvalidDataException("MISSING_CAP_BOUNDARY");
                var entity=new ToolDxfEntity {Type="SOLID_SECTION",Curves=curves.AsReadOnly()};
                var region=ToolRegionValidator.Validate(new[]{entity});
                if(!region.IsValidRegion)throw new InvalidDataException("UNSUPPORTED_SECTION_REGION");
                reasons.Add("FINAL_MODEL_ONLY");reasons.Add("UNKNOWN_BEFORE_DURING_AFTER_STATES");
                return new SheetSection2D(bend.FeatureId,frame,bend.ThicknessMm,model.SimulationOnly?SheetCoverage.SimulationOnly:SheetCoverage.CompleteUniformExtrusion,curves,region,reasons);
            }
            catch(InvalidDataException ex)
            {
                reasons.Add("UNSUPPORTED_2D_ENVELOPE");reasons.Add(ex.Message);
                // A failed proof does not expose a partial silhouette as full
                // collision geometry. Source model data remains available.
                return new SheetSection2D(bend.FeatureId,bend.Frame,bend.ThicknessMm,SheetCoverage.Unsupported2DEnvelope,null,ToolRegionValidator.Validate(null),reasons);
            }
        }
        internal static ToolCurve Project(PlacementEdgeSnapshot edge,BendWorkFrame frame)
        {
            var a=frame.Project(edge.A);var b=frame.Project(edge.B);
            if(edge.Kind==1)return ToolCurve.Line(a,b,new ToolTransform());
            var c=frame.Project(edge.Arc.CenterMm);var middle=frame.Project(edge.Arc.MidMm);
            double start=Math.Atan2(a.Y-c.Y,a.X-c.X),end=Math.Atan2(b.Y-c.Y,b.X-c.X),mid=Math.Atan2(middle.Y-c.Y,middle.X-c.X);
            double sweep=Positive(end-start);
            if(edge.Arc.FullCircle)sweep=2*Math.PI;
            else if(sweep<1e-12)throw new InvalidDataException("AMBIGUOUS_ARC_SWEEP");
            else if(Positive(mid-start)>sweep+1e-10)sweep-=2*Math.PI;
            if(Math.Abs(ToolPoint.Distance(a,c)-edge.Arc.RadiusMm)>Tol||Math.Abs(ToolPoint.Distance(b,c)-edge.Arc.RadiusMm)>Tol||
                Math.Abs(ToolPoint.Distance(middle,c)-edge.Arc.RadiusMm)>Tol)throw new InvalidDataException("INCONSISTENT_NATIVE_ARC");
            return ToolCurve.Arc(a,b,c,edge.Arc.RadiusMm,sweep,new ToolTransform());
        }
        private static double Positive(double angle){angle%=2*Math.PI;return angle<0?angle+2*Math.PI:angle;}
    }
}
