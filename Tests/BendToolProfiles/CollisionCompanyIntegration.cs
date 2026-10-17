using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private static int CollisionCompanyIntegration(string[] args)
    {
        try
        {
            string config=Path.GetFullPath(args[1]),directory=Path.GetFullPath(args[2]),output=Path.GetFullPath(args[3]);Directory.CreateDirectory(output);
            var provider=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration {DirectoryPath=directory,PlacementConfigurationPath=config});
            var library=provider.Read(CancellationToken.None);Check(library.Error==null&&library.PlacementSources.Tools.Count==18,"Actual 18 Master slots");
            Check(library.Tools.Count(t=>t.Validation.PunchGeometryReadComplete)==18,"Actual 18 DIM profiles");
            Check(library.PlacementSources.Tools.Count(t=>t.Punch?.Region.IsValidRegion==true)==17,"KIK source remains open");
            Check(library.PlacementSources.Tools.All(t=>!t.PhysicalPairVerified&&!t.ClampVerified),"No company physical approval fabricated");
            var runs=new List<object>();
            foreach(int bends in new[]{3,6})
            {
                // Real company tool sources; explicitly SYNTHETIC workpiece.
                // Actual Part coverage is independently tested by the native harness.
                var model=CollisionModel(Rect("SYNTHETIC_SHEET",0,0,10,10),bends);
                var draft=BendPlacementBuilder.Build(model,library);var rows=draft.CollisionResults;
                Check(rows.Count==18*bends,"No missing error/tool rows");Check(rows.All(r=>r.CombinedResult==CollisionStatus.UNKNOWN),"No approved actual setup/contact/motion, no false CLEAR");
                Check(rows.All(r=>r.PunchResult.Status==CollisionStatus.UNKNOWN&&r.HolderResult.Status==CollisionStatus.UNKNOWN),"Independent unknown results include reasons");
                Check(rows.All(r=>r.PunchResult.Witness==null&&r.HolderResult.CertifiedClearanceLowerMm==null),"No fictitious geometric evidence");
                Check(rows.All(r=>r.States.Count==4&&r.AllRequiredStatesResult==CollisionStatus.UNKNOWN),"Before/during/after remain explicit unknowns");
                Check(draft.WinnerToolId==null&&draft.Status=="CHUA_XAC_MINH","No winner or manufacturing feasibility");
                Check(rows.Where(r=>r.ToolId=="KIK-7181328001").All(r=>r.PunchResult.Reasons.Contains("OPEN_UNSUPPORTED_OR_INVALID_PUNCH_REGION")),"Source-open KIK diagnosed");
                foreach(string id in new[]{"AMD-STD-004","AMD-STD-008","AMD-STD-117","AMD-STD-452"})
                    Check(rows.Where(r=>r.ToolId==id).All(r=>r.HolderResult.Reasons.Contains("MAPPING_UNRESOLVED_OR_UNREVIEWED")),"Missing holder mapping stays visible");
                runs.Add(new {Bends=bends,Rows=rows.Count,Clear=rows.Count(r=>r.CombinedResult==CollisionStatus.CLEAR),Collision=rows.Count(r=>r.CombinedResult==CollisionStatus.COLLISION),
                    Unknown=rows.Count(r=>r.CombinedResult==CollisionStatus.UNKNOWN),PerTool=CompanyToolIds.All.Select(id=>new {ToolId=id,
                        Rows=rows.Count(r=>r.ToolId==id),Punch=rows.First(r=>r.ToolId==id).PunchResult.Status.ToString(),Holder=rows.First(r=>r.ToolId==id).HolderResult.Status.ToString(),
                        Reasons=rows.Where(r=>r.ToolId==id).SelectMany(r=>r.Reasons).Distinct().OrderBy(s=>s).ToArray()})});
            }
            File.WriteAllText(Path.Combine(output,"PRIVATE-collision-company.json"),Json.Serialize(new {Passed=true,Assertions=assertions,WorkpieceFixture="CONTROLLED_SYNTHETIC_NOT_COMPANY_PART",Runs=runs}));
            Console.WriteLine("COLLISION COMPANY PASS; assertions="+assertions+"; 54/108 rows UNKNOWN; 18 actual tools, no fabricated approvals.");return 0;
        }
        catch(Exception ex){Console.WriteLine("COLLISION COMPANY FAIL: "+ex.Message);return 1;}
    }

    // Controlled software fixtures only. This mode never reads company CAD or
    // approvals and the optional Python oracle is not a runtime dependency.
    private static int CollisionOracleFixtures(string[] args)
    {
        try
        {
            var rows=new List<object>();var counts=new Dictionary<string,int>();int number=0;
            Action<string,CollisionMaterial2D,CollisionMaterial2D> add=(name,a,b)=>
            {
                Check(a.Valid&&b.Valid,"Valid oracle source "+name);var r=Query(a,b);
                string status=r.Status.ToString();if(!counts.ContainsKey(status))counts[status]=0;counts[status]++;
                rows.Add(new {Id=++number,Name=name,Workpiece=a,Obstacle=b,Status=status,r.ToleranceMm,r.NumericalGuardMm,r.CertifiedClearanceLowerMm,
                    r.NominalBoundaryDistanceUpperMm,r.Witness,r.WorkUnits,r.Reasons});
            };
            var random=new Random(1200037);
            for(int i=0;i<180;i++)
            {
                double x=random.NextDouble()*30-15,y=random.NextDouble()*24-12,size=.3+random.NextDouble()*6,band=i%4==0?.003:0;
                CollisionMaterial2D a;
                if(i%3==0)a=Material(Polygon("U",new[]{P(0,0),P(10,0),P(10,10),P(9,10),P(9,1),P(1,1),P(1,10),P(0,10)}));
                else if(i%3==1)a=MaterialSet(new CollisionComponent2D(Rect("OUT",-8,-8,16,16),new[]{Disk("HOLE",0,0,4)}),new CollisionComponent2D(Rect("ISLAND",-1,-1,2,2)));
                else a=i%6==2?Material(ArcCap("PARTIAL_ARC",5,i%4==0?3*Math.PI/2:-Math.PI/2)):
                    Material(Disk("CIRCLE",0,0,5,clockwise:(i&1)==0));
                var b=i%2==0?Material(Rect("RECT",x,y,size,size*.7,band)):Material(Disk("ARC",x,y,size,band,clockwise:(i&1)==1));
                add("base-"+i,a,b);
                double angle=random.NextDouble()*2*Math.PI;var transform=new PlacementTransform2D(Math.Cos(angle),Math.Sin(angle),-Math.Sin(angle),Math.Cos(angle),30,-21);
                add("rotate-"+i,Move(a,transform),Move(b,transform));
                add("mirror-"+i,Move(a,new PlacementTransform2D(-1,0,0,1,11,7,true)),Move(b,new PlacementTransform2D(-1,0,0,1,11,7,true)));
            }
            foreach(double gap in new[]{-.01,-.00005,0,.000005,.00002,.002,.005,.02})
                add("uncertainty-"+gap,Material(Rect("SHEET",10+gap,3,1,1)),Material(Rect("TOOL",0,0,10,10,.001)));
            add("per-component-holes",Material(Rect("SHEET",6,3,1,1)),MaterialSet(new CollisionComponent2D(Rect("OUT",0,0,10,10),new[]{Rect("HOLE",2,2,6,6)}),new CollisionComponent2D(Rect("SECOND",5,0,10,10))));
            var spline=ToolNurbsGeometry.Approximate(Spline(2,new[]{P(0,0),P(5,10),P(10,0)},new[]{0.0,0,0,1,1,1}));
            Check(spline.Supported,"Controlled rational/Bezier source");
            var curved=Material(new CollisionContour2D("BOUNDED_SPLINE",spline.Chords.Select(c=>c.Chord).Concat(new[]{Segment(10,0,0,0)}),spline.MaximumDeviationMm));
            add("spline-separated",Material(Rect("SHEET",4,6,1,1)),curved);
            add("spline-overlap",Material(Rect("SHEET",4,1,1,1)),curved);
            add("spline-band-only",Material(Rect("SHEET",4.9999,5.00005,.0002,.1)),curved);
            string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"SYNTHETIC-collision-oracle.json"),Json.Serialize(new {FixtureKind="CONTROLLED_SYNTHETIC_NO_COMPANY_DATA",Policy=CollisionPolicy2D.Version,Counts=counts,Cases=rows}));
            Console.WriteLine("SYNTHETIC ORACLE CAPTURE: "+Json.Serialize(counts)+"; cases="+rows.Count);return 0;
        }
        catch(Exception ex){Console.WriteLine("ORACLE CAPTURE FAIL: "+ex.Message);return 1;}
    }
}
