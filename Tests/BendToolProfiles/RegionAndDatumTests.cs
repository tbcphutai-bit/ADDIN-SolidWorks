using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private static ToolPoint P(double x, double y) => new ToolPoint(x, y);
    private static ToolCurve Segment(double x, double y, double ex, double ey) => ToolCurve.Line(P(x, y), P(ex, ey), new ToolTransform());
    private static ToolCurve Circular(double x, double y, double r, double start, double sweep) => ToolCurve.Arc(
        P(x + r * Math.Cos(start), y + r * Math.Sin(start)), P(x + r * Math.Cos(start + sweep), y + r * Math.Sin(start + sweep)),
        P(x, y), r, sweep, new ToolTransform());
    private static ToolDxfEntity Entity(IEnumerable<ToolCurve> curves, bool? closed = null) => new ToolDxfEntity {
        Type="SYNTHETIC", Handle="TEST", InstancePath="TEST", Curves=curves.ToArray(), SourceClosed=closed };
    private static ToolDxfEntity Polygon(params ToolPoint[] p) => Entity(p.Select((a,i)=>ToolCurve.Line(a,p[(i+1)%p.Length],new ToolTransform())),true);
    private static ToolDxfEntity Square(double x=0,double y=0,double size=10) => Polygon(P(x,y),P(x+size,y),P(x+size,y+size),P(x,y+size));
    private static ToolDxfEntity Circle(double x=0,double y=0,double radius=1) => Entity(new[] {Circular(x,y,radius,0,2*Math.PI)},true);
    private static ToolRegionValidation Region(params ToolDxfEntity[] e) => ToolRegionValidator.Validate(e);
    private static void Status(ToolRegionValidation r, ToolRegionStatus expected, string issue=null)
    { Check(r.Status==expected,"Region "+r.Status+" expected "+expected+"; "+string.Join(",",r.Issues)); if(issue!=null) Check(r.Issues.Contains(issue),"Issue "+issue); }
    private static CompanyToolProfile SyntheticTool(params ToolDxfEntity[] e)
    {
        return new CompanyToolProfile {Id=CompanyToolIds.All[0], Parts=new[] {new ToolGeometryPart {Role=ToolGeometryRole.CompanyPunchProfile,
            Source=new ToolProfileProvenance {Sha256=ToolProfileLibraryProvider.Hash(Encoding.UTF8.GetBytes("synthetic source bytes")),FileExists=true},Entities=e}},
            Validation=new ToolProfileValidationResult {PunchGeometryReadComplete=true,PunchEntitiesSupported=true,PunchClosed=ToolProfileTopology.Closed(e),Region=Region(e),Reasons=new string[0]} };
    }
    private static ToolDatumRecord DatumRecord(CompanyToolProfile tool)
    {
        var hashes=tool.Parts.Where(p=>p.Role==ToolGeometryRole.CompanyPunchProfile).Select(p=>p.Source.Sha256).Distinct().ToArray();
        return new ToolDatumRecord {Id=tool.Id,SourceSha256=hashes[0],GeometrySha256=ToolDatumEvidence.GeometryHash(tool),
            TipPointMm=new ToolPointDatumRecord {X=1,Y=2},MountDatumMm=new ToolPointDatumRecord {X=6,Y=12},
            WorkingDirection=new ToolDirectionDatumRecord {X=0,Y=-1}, LocalCoordinateFrame=new ToolFrameDatumRecord {
                OriginMm=new ToolVectorRecord {X=3,Y=4}, XAxis=new ToolVectorRecord {X=1,Y=0},YAxis=new ToolVectorRecord {X=0,Y=1}}};
    }
    private static readonly DateTimeOffset EvidenceClock = DateTimeOffset.Parse("2026-10-17T00:00:00Z");
    private static ToolDatumApproval Approve(string payload) => new ToolDatumApproval { Approval="Verified",Method="MeasuredTool",VerifiedBy="SYNTHETIC TEST REVIEWER",
        VerifiedAtUtc="2026-10-10T12:00:00Z",ArtifactFile="inspection.txt",ArtifactSha256=ToolProfileLibraryProvider.Hash(Encoding.UTF8.GetBytes("synthetic reviewed evidence")),DatumPayloadSha256=payload};
    private static void ApproveAll(ToolDatumRecord r)
    {
        r.TipPointMm.Evidence=Approve(ToolDatumEvidence.PointPayloadHash(r,"TipPointMm",r.TipPointMm.X,r.TipPointMm.Y));
        r.MountDatumMm.Evidence=Approve(ToolDatumEvidence.PointPayloadHash(r,"MountDatumMm",r.MountDatumMm.X,r.MountDatumMm.Y));
        r.WorkingDirection.Evidence=Approve(ToolDatumEvidence.PointPayloadHash(r,"WorkingDirection",r.WorkingDirection.X,r.WorkingDirection.Y));
        r.LocalCoordinateFrame.Evidence=Approve(ToolDatumEvidence.FramePayloadHash(r));
    }
    private static string WriteEvidence(string folder, params ToolDatumRecord[] r)
    {
        Save(folder,"datums.json",Json.Serialize(new ToolDatumEvidenceFile {Version=1,Tools=r.ToList()}));
        return System.IO.Path.Combine(folder,"datums.json");
    }
    private static ToolDatumSet Bound(CompanyToolProfile tool, ToolDatumRecord r, Action<string> alter=null)
    {
        string folder=Folder(); Save(folder,"inspection.txt","synthetic reviewed evidence"); string path=WriteEvidence(folder,r); alter?.Invoke(folder);
        return ToolDatumEvidence.Bind(tool,ToolDatumEvidence.Load(path,CancellationToken.None,EvidenceClock));
    }

    private static void RegionAndDatumTests()
    {
        Test("region: simple polygon, area and source closure are separate",()=> {
            var e=Square(); var r=Region(e); Status(r,ToolRegionStatus.ValidSingleLoop); Near(r.AreaMm2.Value,100,"Area");
            Check(r.LoopCount==1 && ToolProfileTopology.Closed(new[] {e}),"Observed loop"); Near(r.DistanceToleranceMm,1e-5,"mm tolerance");
        });
        Test("region: unordered reversed LINE entities form one observed loop",()=> {
            var lines=new[] {Segment(0,0,10,0),Segment(10,10,10,0),Segment(0,10,10,10),Segment(0,0,0,10)};
            var e=lines.Reverse().Select(c=>Entity(new[] {c})).ToArray(); var r=Region(e); Status(r,ToolRegionStatus.ValidSingleLoop); Near(r.AreaMm2.Value,100,"Area independent of order");
        });
        Test("region: concave and reversed polygon areas",()=> {
            var p=new[] {P(0,0),P(8,0),P(8,2),P(3,2),P(3,7),P(0,7)};
            foreach(var q in new[] {p,p.Reverse().ToArray()}) {var r=Region(Polygon(q));Status(r,ToolRegionStatus.ValidSingleLoop);Near(r.AreaMm2.Value,31,"Concave area");}
        });
        Test("region: translated coordinates retain area without recentering source",()=> {
            var e=Square(1000000,-1000000);var r=Region(e);Status(r,ToolRegionStatus.ValidSingleLoop);Near(r.AreaMm2.Value,100,"Stable area");Point(e.Curves[0].Start,1000000,-1000000);
        });
        Test("region: CIRCLE remains one analytic curve",()=> {
            var e=Circle(12,-4,3);var r=Region(e);Status(r,ToolRegionStatus.ValidSingleLoop);Near(r.AreaMm2.Value,Math.PI*9,"Circle integral");Check(e.Curves.Count==1,"No tessellation");
        });
        Test("region: adjacent tangent LINE/ARC capsule is valid",()=> {
            var e=Entity(new[] {Segment(0,0,10,0),Circular(10,2,2,-Math.PI/2,Math.PI),Segment(10,4,0,4),Circular(0,2,2,Math.PI/2,Math.PI)},true);
            var r=Region(e);Status(r,ToolRegionStatus.ValidSingleLoop);Near(r.AreaMm2.Value,40+4*Math.PI,"Analytic capsule area");
        });
        Test("region: adjacent co-circular quarters are not overlapping",()=> {
            var e=Entity(Enumerable.Range(0,4).Select(i=>Circular(0,0,2,i*Math.PI/2,Math.PI/2)),true);
            var r=Region(e);Status(r,ToolRegionStatus.ValidSingleLoop);Near(r.AreaMm2.Value,4*Math.PI,"Four arcs");
        });
        Test("region: signed major ARC and chord integral",()=> {
            foreach(double sweep in new[] {1.5*Math.PI,-1.5*Math.PI}) {
                var c=Circular(5,7,3,0,sweep);var r=Region(Entity(new[] {c,ToolCurve.Line(c.End,c.Start,new ToolTransform())},true));
                Status(r,ToolRegionStatus.ValidSingleLoop);Near(r.AreaMm2.Value,0.5*9*(1.5*Math.PI+1),"Major segment area");}
        });
        Test("region: a two-arc lens allows its two shared endpoints",()=> {
            double a=Math.PI/3; var e=Entity(new[] {Circular(0,0,2,-a,2*a),Circular(2,0,2,2*a,2*a)},true);
            var r=Region(e);Status(r,ToolRegionStatus.ValidSingleLoop);Near(r.AreaMm2.Value,8*Math.PI/3-2*Math.Sqrt(3),"Lens area");
        });
        Test("region: closed bowtie is not a valid region",()=> {
            var e=Polygon(P(0,0),P(10,10),P(0,10),P(10,0));Check(ToolProfileTopology.Closed(new[] {e}),"Legacy closure observes only degree");
            var r=Region(e);Status(r,ToolRegionStatus.InvalidBoundary,"SelfIntersectionOrContact");Check(r.Issues.Contains("ZeroOrUnresolvedArea"),"Zero algebraic area recorded");
        });
        Test("region: nonzero-area self crossing cannot pass",()=> {
            var e=Polygon(P(0,0),P(10,8),P(0,8),P(7,0));Status(Region(e),ToolRegionStatus.InvalidBoundary,"SelfIntersectionOrContact");
        });
        Test("region: multiple disjoint loops remain unsupported",()=> {
            var e=new[] {Square(),Square(30,0)};Check(ToolProfileTopology.Closed(e),"Both source loops closed");var r=Region(e);
            Status(r,ToolRegionStatus.Unsupported,"DisjointLoopsUnsupported");Check(r.LoopCount==2 && !r.AreaMm2.HasValue,"No union region invented");
        });
        Test("region: nested and disjoint loop sets report both unsupported cases",()=> {
            var r=Region(Square(),Circle(5,5,1),Circle(30,0,2));Status(r,ToolRegionStatus.Unsupported,"NestedLoopsUnsupported");
            Check(r.Issues.Contains("DisjointLoopsUnsupported") && r.LoopCount==3,"Mixed holes and islands");
        });
        Test("region: nested line and arc loops require a fill rule",()=> {
            foreach(var outer in new[] {Square(),Polygon(P(0,10),P(10,10),P(10,0),P(0,0))}) {
                var r=Region(outer,Circle(5,5,1));Status(r,ToolRegionStatus.Unsupported,"NestedLoopsUnsupported");Check(r.LoopCount==2,"Hole not filled");}
            Status(Region(Circle(0,0,10),Circle(0,0,2)),ToolRegionStatus.Unsupported,"NestedLoopsUnsupported");
        });
        Test("region: non-adjacent tangent circles and internal tangent circles invalidate",()=> {
            Status(Region(Circle(),Circle(2,0)),ToolRegionStatus.InvalidBoundary,"SelfIntersectionOrContact");
            Status(Region(Circle(0,0,3),Circle(2,0,1)),ToolRegionStatus.InvalidBoundary,"SelfIntersectionOrContact");
        });
        Test("region: crossing arcs and line/arc intersections invalidate",()=> {
            Status(Region(Circle(),Circle(1,0)),ToolRegionStatus.InvalidBoundary,"SelfIntersectionOrContact");
            Status(Region(Square(),Circle(5,0,2)),ToolRegionStatus.InvalidBoundary,"SelfIntersectionOrContact");
        });
        Test("region: non-adjacent line/arc tangency invalidates",()=> {
            Status(Region(Square(),Circle(5,-1,1)),ToolRegionStatus.InvalidBoundary,"SelfIntersectionOrContact");
        });
        Test("region: coincident and oppositely oriented circles overlap",()=> {
            Status(Region(Circle(),Entity(new[] {Circular(0,0,1,0,-2*Math.PI)},true)),ToolRegionStatus.InvalidBoundary,"OverlappingCurves");
        });
        Test("region: overlapping co-circular arcs across angular seam",()=> {
            var a=Circular(0,0,5,350*Math.PI/180,40*Math.PI/180);var b=Circular(0,0,5,10*Math.PI/180,-40*Math.PI/180);
            Status(Region(Entity(new[] {a,b})),ToolRegionStatus.InvalidBoundary,"OverlappingCurves");
        });
        Test("region: duplicate and partially retraced lines invalidate",()=> {
            var e=Square(); Status(Region(e,Entity(new[] {Segment(10,0,0,0)})),ToolRegionStatus.InvalidBoundary,"OverlappingCurves");
            Status(Region(Polygon(P(0,0),P(10,0),P(5,0),P(5,10),P(0,10))),ToolRegionStatus.InvalidBoundary,"OverlappingCurves");
        });
        Test("region: zero-length edge and tiny arc invalidate",()=> {
            Status(Region(Polygon(P(0,0),P(10,0),P(10,0),P(0,10))),ToolRegionStatus.InvalidBoundary,"DegenerateEdge");
            Status(Region(Circle(0,0,1e-6)),ToolRegionStatus.InvalidBoundary,"DegenerateArc");
        });
        Test("region: zero area and unresolved thin area invalidate",()=> {
            Status(Region(Polygon(P(0,0),P(10,0),P(20,0))),ToolRegionStatus.InvalidBoundary,"ZeroOrUnresolvedArea");
            var e=Polygon(P(0,0),P(10,0),P(10,1.5e-5),P(0,1.5e-5));Status(Region(e),ToolRegionStatus.InvalidBoundary,"ZeroOrUnresolvedArea");
        });
        Test("region: T-junction and shared four-way vertex invalidate",()=> {
            Status(Region(Square(),Entity(new[] {Segment(10,0,20,0)})),ToolRegionStatus.InvalidBoundary,"NonManifoldVertex");
            Status(Region(Square(),Square(10,10)),ToolRegionStatus.InvalidBoundary,"NonManifoldVertex");
        });
        Test("region: explicit open flag is never repaired despite closed endpoints",()=> {
            var e=Square();e.SourceClosed=false;var before=Json.Serialize(e);Status(Region(e),ToolRegionStatus.OpenBoundary,"ExplicitSourceOpen");Check(Json.Serialize(e)==before,"Source unchanged");
        });
        Test("region: joint gaps within policy are observed and not welded",()=> {
            double gap=0.5e-5;var e=Entity(new[] {Segment(0,0,10,0),Segment(10+gap,0,10,10),Segment(10,10,0,10),Segment(0,10,0,0)});
            string before=Json.Serialize(e);var r=Region(e);Status(r,ToolRegionStatus.ValidSingleLoop);Near(r.MaximumJointGapMm,gap,"Observed gap");Check(Json.Serialize(e)==before,"No repair");
            Check(!ToolProfileTopology.Closed(new[] {e}),"Region tolerance independent of legacy endpoint tolerance");
        });
        Test("region: gaps outside policy remain open",()=> {
            double gap=1.1e-5;var e=Entity(new[] {Segment(0,0,10,0),Segment(10+gap,0,10,10),Segment(10,10,0,10),Segment(0,10,0,0)});
            Status(Region(e),ToolRegionStatus.OpenBoundary,"UnconnectedBoundary");
        });
        Test("region: tolerance chains do not depend on endpoint insertion order",()=> {
            var e=Entity(new[] {Segment(0,0,10,10),Segment(0.75e-5,0,20,10),Segment(1.5e-5,0,30,10)});
            Status(Region(e),ToolRegionStatus.InvalidBoundary,"AmbiguousToleranceCluster");
            e.Curves=e.Curves.Reverse().ToArray();Status(Region(e),ToolRegionStatus.InvalidBoundary,"AmbiguousToleranceCluster");
        });
        Test("region: near tangency within tolerance is unresolved contact",()=> {
            Status(Region(Circle(),Circle(2+0.5e-5,0)),ToolRegionStatus.InvalidBoundary,"SelfIntersectionOrContact");
            Status(Region(Circle(),Circle(2+2e-5,0)),ToolRegionStatus.Unsupported,"DisjointLoopsUnsupported");
        });
        Test("region: unsupported data, curve budget and numeric domain are explicit",()=> {
            var e=Square();e.UnsupportedReason="Synthetic unsupported";Status(Region(e),ToolRegionStatus.Unsupported,"UnsupportedOrIncompleteEntity");
            Status(Region(Square(1e8,0)),ToolRegionStatus.Unsupported,"NumericDomainExceeded");
            Status(Region(Entity(Enumerable.Repeat(Segment(0,0,1,0),4097))),ToolRegionStatus.Unsupported,"CurveBudgetExceeded");
            Status(Region(),ToolRegionStatus.NotEvaluated,"NoBoundary");
        });
        Test("region: inconsistent and multi-turn arcs are not accepted as circles",()=> {
            var bad=ToolCurve.Arc(P(1,0),P(0,2),P(0,0),1,Math.PI/2,new ToolTransform());
            Status(Region(Entity(new[] {bad})),ToolRegionStatus.InvalidBoundary,"InconsistentArc");
            Status(Region(Entity(new[] {Circular(0,0,2,0,4*Math.PI)})),ToolRegionStatus.Unsupported,"MultiTurnArcUnsupported");
        });
        Test("region: near line/arc tangency and tiny co-linear overlap are conservative",()=> {
            Status(Region(Square(),Circle(5,-1-0.5e-5,1)),ToolRegionStatus.InvalidBoundary,"SelfIntersectionOrContact");
            Status(Region(Polygon(P(0,0),P(10,0),P(10-1e-6,0),P(10,10),P(0,10))),ToolRegionStatus.InvalidBoundary);
        });
        Test("region: a reference can have valid area without verified mounting",()=> {
            string folder=Folder();Save(folder,"source.dxf",Dxf(Poly()+Poly(handle:"H")));var punch=Selection();punch.Handles=new[] {"P"};
            var holder=Selection("VerifiedHolderGeometry");holder.Handles=new[] {"H"};WriteManifest(folder,Manifest(punch,holder));
            var tool=First(Provider(folder,"manifest.json").Read(CancellationToken.None));
            Check(tool.Parts.Single(p=>p.Role==ToolGeometryRole.HolderClampReference).Region.IsValidRegion && !tool.Validation.ClampVerified,"Region is not clamp fit");
        });
        Test("region: invalid tolerance policy and cancellation are rejected",()=> {
            bool bad=false,cancelled=false;try {new ToolGeometryTolerance(0);}catch(ArgumentOutOfRangeException){bad=true;}
            using(var c=new CancellationTokenSource()){c.Cancel();try {ToolRegionValidator.Validate(new[] {Square()},cancellation:c.Token);}catch(OperationCanceledException){cancelled=true;}}
            Check(bad && cancelled,"No invalid policy or ignored cancellation");
        });
        Test("region: seeded convex polygons agree with independent shoelace area",()=> {
            var random=new Random(81273);
            for(int trial=0;trial<250;trial++) {
                int n=3+random.Next(28);double radius=1+random.NextDouble()*99,ox=random.NextDouble()*10000,oy=random.NextDouble()*10000;
                var angles=Enumerable.Range(0,n).Select(i=>(i+0.3*random.NextDouble())*2*Math.PI/n).ToArray();
                var p=angles.Select(a=>P(ox+radius*Math.Cos(a),oy+radius*Math.Sin(a))).ToArray();
                double area=0;for(int i=1;i+1<p.Length;i++) area+=(p[i].X-p[0].X)*(p[i+1].Y-p[0].Y)-(p[i].Y-p[0].Y)*(p[i+1].X-p[0].X);
                if(trial%2==0) p=p.Reverse().ToArray();var r=Region(Polygon(p));Status(r,ToolRegionStatus.ValidSingleLoop);Near(r.AreaMm2.Value,Math.Abs(area)/2,"Independent area");
            }
        });
        Test("region: rotated mirrored analytic capsules preserve validity and area",()=> {
            var random=new Random(4682);var original=new[] {Segment(0,0,10,0),Circular(10,2,2,-Math.PI/2,Math.PI),Segment(10,4,0,4),Circular(0,2,2,Math.PI/2,Math.PI)};
            for(int i=0;i<100;i++) {
                double a=random.NextDouble()*2*Math.PI,s=0.1+random.NextDouble()*5,sign=i%2==0?-1:1;
                var m=new ToolTransform(s*Math.Cos(a)*sign,s*Math.Sin(a)*sign,-s*Math.Sin(a),s*Math.Cos(a),random.NextDouble()*10000,random.NextDouble()*10000);
                var curves=original.Select(c=>c.Kind=="LINE"?ToolCurve.Line(c.Start,c.End,m):ToolCurve.Arc(c.Start,c.End,c.Center,c.RadiusMm,c.SweepRadians,m));
                var r=Region(Entity(curves,true));Status(r,ToolRegionStatus.ValidSingleLoop);Near(r.AreaMm2.Value,(40+4*Math.PI)*s*s,"Transformed analytic area");
            }
        });

        Test("datums: missing evidence never infers a tip or mounting point",()=> {
            var tool=SyntheticTool(Square());var d=ToolDatumEvidence.Bind(tool,ToolDatumEvidence.Load(null,CancellationToken.None));
            Check(d.Status==DatumEvidenceStatus.Missing && !d.TipPointMm.HasValue && !d.MountDatumMm.HasValue && d.LocalCoordinateFrame==null,"No heuristic datums");Check(!d.ClampVerified,"No physical fit");
        });
        Test("datums: coordinates alone are unverified",()=> {
            var tool=SyntheticTool(Square());var d=Bound(tool,DatumRecord(tool));Check(d.Status==DatumEvidenceStatus.Unverified,"Coordinates are not evidence");
            Check(d.TipEvidenceStatus==DatumEvidenceStatus.Unverified && d.MountEvidenceStatus==DatumEvidenceStatus.Unverified,"Separate points");Point(d.TipPointMm.Value,1,2);Point(d.MountDatumMm.Value,6,12);
        });
        Test("datums: explicit attestation validates only its record, never clamp or chanz feasibility",()=> {
            var tool=SyntheticTool(Square());var r=DatumRecord(tool);ApproveAll(r);var d=Bound(tool,r);
            Check(d.Status==DatumEvidenceStatus.VerifiedRecord && d.TipEvidenceStatus==DatumEvidenceStatus.VerifiedRecord && d.FrameEvidenceStatus==DatumEvidenceStatus.VerifiedRecord,"Reviewed bound record");
            Check(!d.ClampVerified && !tool.Validation.ToolTipOriginVerified && !tool.Validation.CoordinateFrameVerified && !tool.Validation.ClampVerified && tool.Validation.Status=="CHUA_XAC_MINH","No physical/manufacturing PASS");
        });
        Test("datums: tip and mounting evidence are independent",()=> {
            var tool=SyntheticTool(Square());var r=DatumRecord(tool);r.TipPointMm.Evidence=Approve(ToolDatumEvidence.PointPayloadHash(r,"TipPointMm",1,2));
            var d=Bound(tool,r);Check(d.TipEvidenceStatus==DatumEvidenceStatus.VerifiedRecord && d.MountEvidenceStatus==DatumEvidenceStatus.Unverified && d.Status==DatumEvidenceStatus.Unverified,"Tip proof does not prove mounting");
        });
        Test("datums: missing or mismatched source and selection hashes fail closed",()=> {
            var tool=SyntheticTool(Square());var r=DatumRecord(tool);r.SourceSha256="bad";Check(Bound(tool,r).Status==DatumEvidenceStatus.Invalid,"Malformed source binding");
            r=DatumRecord(tool);r.SourceSha256=new string('a',64);Check(Bound(tool,r).Status==DatumEvidenceStatus.Stale,"Wrong source binding");
            r=DatumRecord(tool);r.GeometrySha256=new string('b',64);Check(Bound(tool,r).Status==DatumEvidenceStatus.Stale,"Same file different selection binding");
            r=DatumRecord(tool);tool.Validation.PunchGeometryReadComplete=false;Check(Bound(tool,r).Status==DatumEvidenceStatus.Stale,"Incomplete source cannot attest datums");
        });
        Test("datums: editing coordinates without renewing approval makes payload stale",()=> {
            var tool=SyntheticTool(Square());var r=DatumRecord(tool);ApproveAll(r);r.TipPointMm.X=7;
            var d=Bound(tool,r);Check(d.TipEvidenceStatus==DatumEvidenceStatus.Stale && d.MountEvidenceStatus==DatumEvidenceStatus.VerifiedRecord && d.Status==DatumEvidenceStatus.Stale,"Payload bound independently");
        });
        Test("datums: repinning the source cannot reuse an old approval payload",()=> {
            var tool=SyntheticTool(Square());var r=DatumRecord(tool);ApproveAll(r);tool.Parts[0].Source.Sha256=new string('d',64);
            r.SourceSha256=tool.Parts[0].Source.Sha256;r.GeometrySha256=ToolDatumEvidence.GeometryHash(tool);
            Check(Bound(tool,r).Status==DatumEvidenceStatus.Stale,"Review binds source/selection hashes as well as values");
        });
        Test("datums: changed or missing evidence artifact cannot remain verified",()=> {
            var tool=SyntheticTool(Square());var r=DatumRecord(tool);ApproveAll(r);
            Check(Bound(tool,r,folder=>Save(folder,"inspection.txt","changed reviewed evidence")).Status==DatumEvidenceStatus.Stale,"Artifact content hash");
            Check(Bound(tool,r,folder=>System.IO.File.Delete(System.IO.Path.Combine(folder,"inspection.txt"))).Status==DatumEvidenceStatus.Invalid,"Artifact missing");
        });
        Test("datums: verification requires reviewer, method, timestamp and payload binding",()=> {
            var tool=SyntheticTool(Square());foreach(string field in new[] {"reviewer","method","date","future","payload","approval"}) {
                var r=DatumRecord(tool);ApproveAll(r);var a=r.TipPointMm.Evidence;
                if(field=="reviewer")a.VerifiedBy=null;if(field=="method")a.Method="LowestPoint";if(field=="date")a.VerifiedAtUtc="2026-10-10";
                if(field=="future")a.VerifiedAtUtc="2030-01-01T00:00:00Z";if(field=="payload")a.DatumPayloadSha256=null;if(field=="approval")a.Approval="yes";
                Check(Bound(tool,r).TipEvidenceStatus==DatumEvidenceStatus.Invalid,"Required explicit evidence: "+field);}
        });
        Test("datums: expired approval is stale, malformed expiry invalid",()=> {
            var tool=SyntheticTool(Square());var r=DatumRecord(tool);ApproveAll(r);r.TipPointMm.Evidence.ValidUntilUtc="2026-10-15T00:00:00Z";
            Check(Bound(tool,r).TipEvidenceStatus==DatumEvidenceStatus.Stale,"Expired evidence");r.TipPointMm.Evidence.ValidUntilUtc="2026-10-01T00:00:00Z";
            Check(Bound(tool,r).TipEvidenceStatus==DatumEvidenceStatus.Invalid,"Expiry precedes review");
        });
        Test("datums: expiry changes the fingerprint without a file edit",()=> {
            var tool=SyntheticTool(Square());var r=DatumRecord(tool);ApproveAll(r);r.TipPointMm.Evidence.ValidUntilUtc="2026-10-17T01:00:00Z";
            string folder=Folder();Save(folder,"inspection.txt","synthetic reviewed evidence");string path=WriteEvidence(folder,r);
            var before=ToolDatumEvidence.Load(path,CancellationToken.None,EvidenceClock);var after=ToolDatumEvidence.Load(path,CancellationToken.None,EvidenceClock.AddHours(2));
            Check(ToolDatumEvidence.Bind(tool,before).TipEvidenceStatus==DatumEvidenceStatus.VerifiedRecord && ToolDatumEvidence.Bind(tool,after).TipEvidenceStatus==DatumEvidenceStatus.Stale,"Clock-based expiry");
            Check(before.Fingerprint!=after.Fingerprint,"Expiry invalidates snapshot cache even with unchanged bytes");
        });
        Test("datums: unit directions and right-handed orthonormal frame are mandatory",()=> {
            var tool=SyntheticTool(Square());var r=DatumRecord(tool);r.WorkingDirection.X=2;Check(Bound(tool,r).DirectionEvidenceStatus==DatumEvidenceStatus.Invalid,"No auto normalization");
            r=DatumRecord(tool);r.LocalCoordinateFrame.YAxis.Y=-1;Check(Bound(tool,r).FrameEvidenceStatus==DatumEvidenceStatus.Invalid,"No reflected frame guessed");
            r=DatumRecord(tool);r.LocalCoordinateFrame.XAxis.Y=1;Check(Bound(tool,r).FrameEvidenceStatus==DatumEvidenceStatus.Invalid,"Invalid frame axes");
            r=DatumRecord(tool);r.LocalCoordinateFrame.OriginMm=null;Check(Bound(tool,r).FrameEvidenceStatus==DatumEvidenceStatus.Invalid,"Origin required");
        });
        Test("datums: zero is supplied data but absent and nonfinite coordinates are invalid",()=> {
            var tool=SyntheticTool(Square());var r=DatumRecord(tool);r.TipPointMm.X=0;r.TipPointMm.Y=0;
            Check(Bound(tool,r).TipPointMm.HasValue,"Explicit zero point");r.TipPointMm.X=null;Check(Bound(tool,r).TipEvidenceStatus==DatumEvidenceStatus.Invalid,"Absent X");
            r=DatumRecord(tool);r.TipPointMm.X=double.NaN;var doc=new ToolDatumEvidenceDocument();doc.Records.Add(tool.Id,r);
            Check(ToolDatumEvidence.Bind(tool,doc).TipEvidenceStatus==DatumEvidenceStatus.Invalid,"Nonfinite coordinate");
        });
        Test("datums: evidence path traversal and ambiguous IDs are rejected",()=> {
            var tool=SyntheticTool(Square());var r=DatumRecord(tool);ApproveAll(r);r.TipPointMm.Evidence.ArtifactFile="../inspection.txt";
            Check(Bound(tool,r).TipEvidenceStatus==DatumEvidenceStatus.Invalid,"No artifact traversal");
            string path=WriteEvidence(Folder(),r,r);Check(ToolDatumEvidence.Bind(tool,ToolDatumEvidence.Load(path,CancellationToken.None)).Status==DatumEvidenceStatus.Invalid,"Duplicate IDs invalid");
        });
        Test("datums: multiple source files require an explicitly unsupported richer binding",()=> {
            var tool=SyntheticTool(Square());var r=DatumRecord(tool);tool.Parts=tool.Parts.Concat(new[] {new ToolGeometryPart {Role=ToolGeometryRole.CompanyPunchProfile,
                Source=new ToolProfileProvenance {Sha256=new string('c',64)},Entities=new[] {Circle(30,0)}}}).ToArray();
            Check(Bound(tool,r).Status==DatumEvidenceStatus.Unsupported,"No composite source masquerading as one file SHA");
        });
        Test("datums: streamed geometry binding is deterministic, sensitive and cancellable",()=> {
            var tool=SyntheticTool(Square());string first=ToolDatumEvidence.GeometryHash(tool);Check(first==ToolDatumEvidence.GeometryHash(tool),"Deterministic source/geometry identity");
            tool.Parts[0].Entities[0].Curves=Square(size:11).Curves;Check(first!=ToolDatumEvidence.GeometryHash(tool),"Actual analytic curves bind independently of source metadata");
            var large=SyntheticTool(Entity(Enumerable.Repeat(Segment(0,0,1,0),4097)));Check(ToolDatumEvidence.IsSha256(ToolDatumEvidence.GeometryHash(large)),"Unsupported-size region still has bounded streaming identity");
            bool cancelled=false;using(var c=new CancellationTokenSource()){c.Cancel();try {ToolDatumEvidence.GeometryHash(tool,c.Token);}catch(OperationCanceledException){cancelled=true;}}
            Check(cancelled,"Hash cancellation");
        });
        Test("datums: debug summary excludes values, local frame and private review metadata",()=> {
            var tool=SyntheticTool(Square());var r=DatumRecord(tool);ApproveAll(r);tool.Datums=Bound(tool,r);tool.Validation.DatumEvidence=tool.Datums;
            var snapshot=new ToolProfileLibrarySnapshot {Tools=new[] {tool},Sources=tool.Parts.Select(p=>p.Source).ToArray()};string json=snapshot.ToDebugJson();
            Check(!json.Contains("TipPointMm") && !json.Contains("XAxis") && !json.Contains("AreaMm2") && !json.Contains("VerifiedBy") && json.Contains("VerifiedRecord"),"Safe aggregate debug contract");
        });
        Test("datums: configured evidence errors are isolated from readable geometry",()=> {
            string folder=Folder();Save(folder,"amd-std-003_DIM.dxf",Dxf(Poly()));Save(folder,"datums.json","{broken");
            var provider=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration {DirectoryPath=folder,DatumEvidencePath="datums.json"});
            var tool=First(provider.Read(CancellationToken.None));Check(tool.Validation.PunchGeometryReadComplete && tool.Validation.Region.IsValidRegion && tool.Datums.Status==DatumEvidenceStatus.Invalid,"Malformed datum file does not erase CAD");
            var invalid=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration {DirectoryPath=folder,DatumEvidencePath="bad\0path"});
            tool=First(invalid.Read(CancellationToken.None));Check(tool.Validation.PunchGeometryReadComplete && tool.Datums.Status==DatumEvidenceStatus.Invalid,"Invalid path isolated");
        });
        Test("datums: cancellation and missing evidence invalidate without a last-good record",()=> {
            var tool=SyntheticTool(Square());string folder=Folder();Save(folder,"inspection.txt","synthetic reviewed evidence");var r=DatumRecord(tool);ApproveAll(r);string path=WriteEvidence(folder,r);
            Check(ToolDatumEvidence.Bind(tool,ToolDatumEvidence.Load(path,CancellationToken.None,EvidenceClock)).Status==DatumEvidenceStatus.VerifiedRecord,"Initial approval");
            System.IO.File.Delete(path);Check(ToolDatumEvidence.Bind(tool,ToolDatumEvidence.Load(path,CancellationToken.None,EvidenceClock)).Status==DatumEvidenceStatus.Invalid,"No last-good evidence on deletion");
            bool cancelled=false;using(var c=new CancellationTokenSource()){c.Cancel();WriteEvidence(folder,r);try {ToolDatumEvidence.Load(path,c.Token);}catch(OperationCanceledException){cancelled=true;}}
            Check(cancelled,"Cancellation propagated");
        });
        Test("datums: source/evidence edits invalidate old snapshots without timestamp guessing",()=> {
            string folder=Folder();Save(folder,"amd-std-003_DIM.dxf",Dxf(Poly()));var config=new ToolProfileSourceConfiguration {DirectoryPath=folder,DatumEvidencePath="datums.json"};
            var provider=new ToolProfileLibraryProvider(()=>config);var initial=provider.Read(CancellationToken.None);var r=DatumRecord(First(initial));ApproveAll(r);Save(folder,"inspection.txt","synthetic reviewed evidence");WriteEvidence(folder,r);
            var reviewed=provider.Read(CancellationToken.None);Check(!initial.Current && First(reviewed).Datums.Status==DatumEvidenceStatus.VerifiedRecord,"Evidence publication invalidates previous snapshot");
            string artifact=System.IO.Path.Combine(folder,"inspection.txt");var time=System.IO.File.GetLastWriteTimeUtc(artifact);Save(folder,"inspection.txt","Synthetic reviewed evidence");System.IO.File.SetLastWriteTimeUtc(artifact,time);
            var stale=provider.Read(CancellationToken.None);Check(!reviewed.Current && First(stale).Datums.Status==DatumEvidenceStatus.Stale,"Artifact hash changes with same size/time");
            Check(First(reviewed).Datums.Status==DatumEvidenceStatus.Stale && First(reviewed).Datums.TipEvidenceStatus==DatumEvidenceStatus.Stale &&
                First(reviewed).Validation.DatumEvidenceStatus==DatumEvidenceStatus.Stale,"Held old datum objects cannot keep a verified status");
            Save(folder,"inspection.txt","synthetic reviewed evidence");var recovered=provider.Read(CancellationToken.None);Check(!stale.Current && First(recovered).Datums.Status==DatumEvidenceStatus.VerifiedRecord,"Evidence recovers");
            string cad=System.IO.Path.Combine(folder,"amd-std-003_DIM.dxf");time=System.IO.File.GetLastWriteTimeUtc(cad);string data=System.IO.File.ReadAllText(cad).Replace("10\n4\n","10\n5\n");Save(folder,"amd-std-003_DIM.dxf",data);System.IO.File.SetLastWriteTimeUtc(cad,time);
            var changed=provider.Read(CancellationToken.None);Check(!recovered.Current && First(changed).Datums.Status==DatumEvidenceStatus.Stale && First(changed).Validation.PunchGeometryReadComplete,"CAD SHA expires datum evidence only");
            Check(First(changed).Validation.Status=="CHUA_XAC_MINH" && !First(changed).Validation.ClampVerified,"No manufacturing result changes");
        });
        Test("datums: manifest outline selection change expires evidence despite same source hash",()=> {
            string folder=Folder();Save(folder,"source.dxf",Dxf(Poly(handle:"P")+Poly(handle:"Q")));var s=Selection();s.Handles=new[] {"P"};WriteManifest(folder,Manifest(s));
            var provider=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration {DirectoryPath=folder,ManifestPath="manifest.json",DatumEvidencePath="datums.json"});
            var a=provider.Read(CancellationToken.None);var r=DatumRecord(First(a));ApproveAll(r);Save(folder,"inspection.txt","synthetic reviewed evidence");WriteEvidence(folder,r);a=provider.Read(CancellationToken.None);
            Check(First(a).Datums.Status==DatumEvidenceStatus.VerifiedRecord,"Initial exact selection");s.Handles=new[] {"Q"};WriteManifest(folder,Manifest(s));var b=provider.Read(CancellationToken.None);
            Check(!a.Current && First(b).Datums.Status==DatumEvidenceStatus.Stale && First(a).Datums.SourceSha256==First(b).Datums.SourceSha256,"Selection hash differs while file hash is unchanged");
        });
    }
}
