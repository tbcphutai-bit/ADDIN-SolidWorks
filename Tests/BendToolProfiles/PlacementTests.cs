using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private sealed class MasterFixture
    {
        internal string Root,Source,Manifest;internal MasterAssemblyManifest Mapping;
    }
    private static MasterFixture Master(int slots=18,string holderType="closed")
    {
        string folder=Folder();var definitions=new List<MasterToolSelection>();var blocks=new StringBuilder();var entities=new StringBuilder();
        for(int i=0;i<slots;i++)
        {
            string id=CompanyToolIds.All[i],name="PAIR_"+id,root="I"+i,label="L"+i;
            string punch=Poly(handle:"P"+i).Replace("8\n0\n","8\nPUNCH\n");
            string holder=Tags(0,"LWPOLYLINE",5,"H"+i,8,"CLAMP_REFERENCE",90,4,70,holderType=="open"?0:1,10,0,20,5,10,4,20,5,10,4,20,8,10,0,20,8);
            if(holderType=="spline")holder+=Tags(0,"SPLINE",5,"S"+i,8,"CLAMP_REFERENCE");
            if(holderType=="multiple")holder+=Poly(handle:"SECOND"+i).Replace("8\n0\n","8\nCLAMP_REFERENCE\n");
            blocks.Append(Block(name,punch+holder+Tags(0,"TEXT",5,"NOTE"+i,8,"LABEL",1,"TEST ANNOTATION")));
            entities.Append(Tags(0,"INSERT",5,root,8,"0",2,name,10,i*100,20,0));
            entities.Append(Tags(0,"TEXT",5,label,8,"LABEL",1,(i+1).ToString("00")+" | "+id));
            definitions.Add(new MasterToolSelection {Id=id,LabelHandle=label,ExpectedLabel=(i+1).ToString("00")+" | "+id,PunchRootInsert=root,PunchBlock=name,
                HolderRootInsert=root,HolderBlock=name,Punch=new ToolProfileSelection {Layers=new[]{"PUNCH"}},Holder=new ToolProfileSelection {Layers=new[]{"CLAMP_REFERENCE"}}});
        }
        string data=Dxf(entities.ToString(),blocks.ToString());Save(folder,"master.dxf",data);
        var manifest=new MasterAssemblyManifest {Version=1,MasterSha256=ToolProfileLibraryProvider.Hash(Encoding.UTF8.GetBytes(data)),Tools=definitions};
        Save(folder,"master.json",Json.Serialize(manifest));return new MasterFixture {Root=folder,Source=Path.Combine(folder,"master.dxf"),Manifest=Path.Combine(folder,"master.json"),Mapping=manifest};
    }
    private static BendMasterAssemblyReader.Result Read(MasterFixture f)=>BendMasterAssemblyReader.Read(f.Source,f.Manifest,null,CancellationToken.None);
    private static void Save(MasterFixture f)=>Save(f.Root,"master.json",Json.Serialize(f.Mapping));
    private static void MasterData(MasterFixture f,string data)
    {File.WriteAllText(f.Source,data,new UTF8Encoding(false));f.Mapping.MasterSha256=PlacementSourceIo.Hash(File.ReadAllBytes(f.Source));Save(f);}
    private static void ChangeWorkbook(string path,string entry,Func<string,string> edit)
    {
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite))using(var zip=new ZipArchive(stream,ZipArchiveMode.Update))
        {
            var old=zip.GetEntry(entry);string text;using(var reader=new StreamReader(old.Open()))text=reader.ReadToEnd();old.Delete();
            using(var writer=new StreamWriter(zip.CreateEntry(entry).Open(),new UTF8Encoding(false)))writer.Write(edit(text));
        }
    }

    // Synthetic B-rep extrusion for unit tests only. It represents the entire
    // material boundary (both skins, arcs, returns), not Min's dimension window.
    private static PlacementBodySnapshot Extrusion(IEnumerable<ToolCurve> boundary)
    {
        var c=boundary.ToArray();int n=c.Length;var vertices=new List<ToolPoint>();
        Func<ToolPoint,int> vertex=p=>{int i=vertices.FindIndex(q=>ToolPoint.Distance(p,q)<1e-8);if(i<0){i=vertices.Count;vertices.Add(p);}return i;};
        foreach(var e in c){vertex(e.Start);vertex(e.End);}
        var edges=new List<PlacementEdgeSnapshot>();var faces=new List<PlacementFaceSnapshot>();
        Func<ToolPoint,double,PlacementVector3> point=(p,z)=>new PlacementVector3(p.X,p.Y,z);
        for(int end=0;end<2;end++)for(int i=0;i<n;i++)
        {
            double z=end==0?-50:50;var edge=c[i];PlacementArc3D arc=null;
            if(edge.Kind=="ARC")
            {
                double a=Math.Atan2(edge.Start.Y-edge.Center.Y,edge.Start.X-edge.Center.X)+edge.SweepRadians/2;
                arc=new PlacementArc3D(point(edge.Center,z),new PlacementVector3(edge.Center.X+edge.RadiusMm*Math.Cos(a),edge.Center.Y+edge.RadiusMm*Math.Sin(a),z),edge.RadiusMm,Math.Abs(Math.Abs(edge.SweepRadians)-2*Math.PI)<1e-10);
            }
            edges.Add(new PlacementEdgeSnapshot(end*n+i,edge.Kind=="LINE"?1:2,point(edge.Start,z),point(edge.End,z),new[]{end,2+i},arc));
        }
        for(int i=0;i<vertices.Count;i++)edges.Add(new PlacementEdgeSnapshot(2*n+i,1,point(vertices[i],-50),point(vertices[i],50),
            c.Select((e,j)=>new {e,j}).Where(e=>vertex(e.e.Start)==i||vertex(e.e.End)==i).Select(e=>2+e.j)));
        faces.Add(new PlacementFaceSnapshot(0,1,new PlacementVector3(0,0,-1),new PlacementVector3(0,0,-50),new PlacementVector3(),Enumerable.Range(0,n)));
        faces.Add(new PlacementFaceSnapshot(1,1,new PlacementVector3(0,0,1),new PlacementVector3(0,0,50),new PlacementVector3(),Enumerable.Range(n,n)));
        for(int i=0;i<n;i++)
        {
            var e=c[i];var delta=new PlacementVector3(e.End.Y-e.Start.Y,e.Start.X-e.End.X,0);var normal=e.Kind=="LINE"?delta.Unit:new PlacementVector3();
            faces.Add(new PlacementFaceSnapshot(2+i,e.Kind=="LINE"?1:2,normal,point(e.Start,0),e.Kind=="ARC"?new PlacementVector3(0,0,1):new PlacementVector3(),
                new[]{i,n+i,2*n+vertex(e.Start),2*n+vertex(e.End)}));
        }
        return new PlacementBodySnapshot("SYNTHETIC_BODY",faces,edges);
    }
    private static PlacementModelCapture Model(PlacementBodySnapshot body,int bends=1,bool complete=true)
    {
        var frame=BendWorkFrame.Observed("SYNTHETIC_SECTION",new PlacementVector3(0,0,0),new PlacementVector3(0,0,1));
        return new PlacementModelCapture("SYNTHETIC_MODEL",123,"Test",new[]{body},Enumerable.Range(1,bends).Select(i=>new PlacementBendSnapshot(i,body.Id,frame,2,-40,40)),complete,true);
    }
    private static ToolProfileLibrarySnapshot Library(BendMasterAssemblyReader.Result master)
    {
        return new ToolProfileLibrarySnapshot {Tools=CompanyToolIds.All.Select(id=>new CompanyToolProfile {Id=id,Parts=new ToolGeometryPart[0],Validation=new ToolProfileValidationResult()}).ToArray(),
            PlacementSources=new PlacementSourceSnapshot(master.Fingerprint,master.Tools,null,master.Reasons)};
    }
    private static string Workbook(string folder)
    {
        string path=Path.Combine(folder,"SYNTHETIC.xlsx");
        using(var stream=new FileStream(path,FileMode.Create))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))
        {
            Action<string,string> add=(name,text)=>{using(var w=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false)))w.Write(text);};
            add("xl/workbook.xml","<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Limits' sheetId='1' r:id='r1'/></sheets></workbook>");
            add("xl/_rels/workbook.xml.rels","<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='r1' Target='worksheets/sheet1.xml'/></Relationships>");
            add("xl/sharedStrings.xml","<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><t>対応長さ： ４.2Ｍ</t><rPh sb='0' eb='1'><t>PHONETIC</t></rPh></si></sst>");
            add("xl/worksheets/sheet1.xml","<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheetData><row r='1'><c r='A1' t='s'><v>0</v></c><c r='B1'><v>80</v></c><c r='C1'><f>40*2</f><v>80</v></c><c r='D1'><v>0</v></c></row></sheetData><drawing r:id='d1'/></worksheet>");
            add("xl/worksheets/_rels/sheet1.xml.rels","<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='d1' Target='../drawings/drawing1.xml'/></Relationships>");
            add("xl/drawings/drawing1.xml","<xdr:wsDr xmlns:xdr='http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing' xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><xdr:twoCellAnchor><xdr:sp><xdr:nvSpPr><xdr:cNvPr id='7' name='SYNTHETIC'/></xdr:nvSpPr><xdr:txBody><a:p><a:r><a:t>105</a:t></a:r></a:p></xdr:txBody></xdr:sp></xdr:twoCellAnchor><xdr:twoCellAnchor><xdr:pic><xdr:nvPicPr><xdr:cNvPr id='8' name='SYNTHETIC_IMAGE'/></xdr:nvPicPr><xdr:blipFill><a:blip r:embed='im1'/></xdr:blipFill></xdr:pic></xdr:twoCellAnchor></xdr:wsDr>");
            add("xl/drawings/_rels/drawing1.xml.rels","<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='im1' Target='../media/test.bin'/></Relationships>");
            add("xl/media/test.bin","SYNTHETIC IMAGE HASH FIXTURE, NO CAD");
        }
        return path;
    }
    private static BendLimitManifest LimitMapping(string path)=>new BendLimitManifest {Version=1,WorkbookSha256=PlacementSourceIo.Hash(File.ReadAllBytes(path)),Rules=new List<BendLimitSelection> {
        new BendLimitSelection {Id="LENGTH",FamilyId="SYNTHETIC_FAMILY",Kind="MaxBendLength",Sheet="Limits",Locator="cell:A1",Unit="m",NumberPattern=@"\A対応長さ:\s*(?<value>[0-9.]+)M\z",ToolIds=new[]{CompanyToolIds.All[0]}},
        new BendLimitSelection {Id="EXAMPLE",FamilyId="SYNTHETIC_FAMILY",Kind="DeepBendClearanceExample",Sheet="Limits",Locator="cell:B1",Unit="mm",ToolIds=new[]{CompanyToolIds.All[0]}} }};
    private static BendLimitWorkbookReader.Result LimitRead(string path,BendLimitManifest mapping)
    {string manifest=Path.Combine(Path.GetDirectoryName(path),"limits.json");File.WriteAllText(manifest,Json.Serialize(mapping));return BendLimitWorkbookReader.Read(path,manifest,CancellationToken.None);}

    private static void PlacementTests()
    {
        Test("placement: native edge parameters retain reverse sense and require evaluation success",()=> {
            Near(NativeEdgeParameterPolicy.Midpoint(-2*Math.PI,-3*Math.PI/2),-7*Math.PI/4,"Negative edge parameter space retained");
            double success=BitConverter.Int64BitsToDouble(1);
            Check(NativeEdgeParameterPolicy.EvaluationSucceeded(new[]{1.0,2.0,3.0,success})&&!NativeEdgeParameterPolicy.EvaluationSucceeded(new[]{1.0,2.0,3.0,0.0}),"Packed native status is checked");
            Check(!NativeEdgeParameterPolicy.EvaluationSucceeded(new[]{1.0,2.0,3.0}),"Underlying curve output without edge status rejected");
            bool invalid=false;try{NativeEdgeParameterPolicy.Midpoint(1,-1);}catch(InvalidDataException){invalid=true;}Check(invalid,"Unordered interval refused");
        });
        Test("placement: mm/metres and complete 3D frame round trip",()=> {
            var origin=PlacementVector3.Metres(1,2,3);Near(origin.X,1000,"Metres->mm");
            var f=BendWorkFrame.Observed("TEST",origin,new PlacementVector3(1,2,3));var p=f.World(P(5,-7),9);
            Point(f.Project(p),5,-7);Near(f.AxialDistance(p),9,"Axial position");Check(!f.MachineApproachVerified,"Computational frame is not machine approach");
        });
        Test("placement: composition and inverse with different origins",()=> {
            var a=new PlacementTransform2D(0,1,-1,0,12,7);var b=new PlacementTransform2D(1,0,0,1,-3,11);var p=P(9,4);
            var expected=a.Apply(b.Apply(p));var result=a.Then(b).Apply(p);Point(result,expected.X,expected.Y);var inverse=a.Then(b).Inverse().Apply(result);Point(inverse,p.X,p.Y);
        });
        Test("placement: tip/mount remain distinct during approach alignment",()=> {
            var m=PlacementTransform2D.Align(P(3,4),P(0,-1),P(10,20),P(1,0));Point(m.Apply(P(3,4)),10,20);Point(m.Direction(P(0,-1)),1,0);
            Check(ToolPoint.Distance(m.Apply(P(3,14)),P(10,20))>9.99,"Mount is not replaced by tip");
        });
        Test("placement: explicit mirror reverses arc sweep and preserves radius",()=> {
            var m=new PlacementTransform2D(-1,0,0,1,8,9,true);var c=m.Apply(Circular(0,0,2,0,Math.PI/2));Near(c.RadiusMm,2,"Radius");Near(c.SweepRadians,-Math.PI/2,"Mirrored sweep");
            Point(m.Inverse().Apply(m.Apply(P(5,7))),5,7);
        });
        Test("placement: shear, scale, implicit reflection and wrong direction are refused",()=> {
            int errors=0;foreach(Action a in new Action[]{()=>new PlacementTransform2D(2,0,0,2),()=>new PlacementTransform2D(1,0,0.1,1),()=>new PlacementTransform2D(-1,0,0,1),
                ()=>PlacementTransform2D.Align(P(0,0),P(0,-2),P(0,0),P(1,0))})try{a();}catch(InvalidDataException){errors++;}
            Check(errors==4,"Rigid transforms and direction policy");
        });
        Test("master: exactly 18 keyed slots with separate punch and holder provenance",()=> {
            var f=Master();var r=Read(f);Check(r.Tools.Count==18&&r.Tools.All(t=>t.MappingIdentified&&t.Punch.Supported&&t.Holder.Supported),"18 exact identity mappings");
            Check(r.Tools.All(t=>t.Punch.FrameId==t.Holder.FrameId&&t.Punch.EntityKeys.Intersect(t.Holder.EntityKeys).Count()==0),"Same observed master frame, disjoint roles");
            Check(r.Tools.All(t=>!t.ClampVerified&&!t.PhysicalPairVerified&&t.Holder.ReferenceOnly&&t.MasterDatums.Status==DatumEvidenceStatus.Missing),"Reference is never mounted/verified");
        });
        Test("master: missing 17 entries remain explicit slots",()=> {
            var r=Read(Master(1));Check(r.Tools.Count==18&&r.Tools.Count(t=>t.MappingIdentified)==1&&r.Tools.Count(t=>t.Punch==null)==17,"No silent filtering");
        });
        Test("master: exact ID boundaries distinguish 004 from 0048",()=> {
            var f=Master(3);f.Mapping.Tools[1].ExpectedLabel=f.Mapping.Tools[2].ExpectedLabel;f.Mapping.Tools[1].LabelHandle=f.Mapping.Tools[2].LabelHandle;Save(f);
            Check(!Read(f).Tools.Single(t=>t.Id==CompanyToolIds.All[1]).MappingIdentified,"Substring is not identity");
        });
        Test("master: duplicate tool records and stale source hashes are rejected",()=> {
            var f=Master(1);f.Mapping.Tools.Add(f.Mapping.Tools[0]);Save(f);Check(Read(f).Tools.All(t=>!t.MappingIdentified),"Duplicate identity records");
            f=Master(1);f.Mapping.MasterSha256=new string('a',64);Save(f);Check(Read(f).Reasons.Contains("STALE_MASTER_MAPPING"),"Source hash pin");
        });
        Test("master: ambiguous and overlapping roles cannot become obstacles",()=> {
            var f=Master(1);f.Mapping.Tools[0].Holder=f.Mapping.Tools[0].Punch;Save(f);var t=Read(f).Tools[0];Check(!t.MappingIdentified&&t.Reasons.Contains("AMBIGUOUS_MASTER_ROLE"),"Punch/holder role overlap");
            f=Master(1);f.Mapping.Tools[0].HolderRootInsert="missing";Save(f);t=Read(f).Tools[0];Check(t.Holder==null&&t.Reasons.Contains("MISSING_HOLDER_GEOMETRY"),"Missing root never guessed");
        });
        Test("master: SPLINE, open holder and multiple loops remain visible and unsupported",()=> {
            var spline=Read(Master(1,"spline")).Tools[0];Check(!spline.Holder.Supported&&spline.Holder.UnsupportedTypes.Contains("SPLINE")&&spline.Reasons.Contains("UNSUPPORTED_HOLDER_GEOMETRY"),"Spline not silently dropped");
            var open=Read(Master(1,"open")).Tools[0];Check(!open.Holder.ObservedClosed&&!open.Holder.Region.IsValidRegion,"Open holder not repaired");
            var multiple=Read(Master(1,"multiple")).Tools[0];Check(!multiple.Holder.Region.IsValidRegion&&multiple.Reasons.Contains("UNVERIFIED_HOLDER_REGION"),"No union/bounding box obstacle");
        });
        Test("master: annotations cannot be selected as holder geometry",()=> {
            var f=Master(1);f.Mapping.Tools[0].Holder=new ToolProfileSelection {Layers=new[]{"LABEL"}};Save(f);var t=Read(f).Tools[0];
            Check(t.Holder==null&&t.Reasons.Contains("ANNOTATION_SELECTION_REJECTED"),"Label is decoration");
        });
        Test("master: nested reflected instances preserve selection and analytic source transforms",()=> {
            var f=Master(1);var m=f.Mapping.Tools[0];m.PunchBlock=m.HolderBlock="OUT";
            string inner=Poly(handle:"P0").Replace("8\n0\n","8\nPUNCH\n")+
                Poly(handle:"H0").Replace("8\n0\n","8\nCLAMP_REFERENCE\n");
            string data=Dxf(Tags(0,"INSERT",5,"I0",8,"0",2,"OUT",10,100,20,20,41,-1,42,1)+Tags(0,"TEXT",5,"L0",1,m.ExpectedLabel),
                Block("INNER",inner)+Block("OUT",Tags(0,"INSERT",5,"NEST",8,"0",2,"INNER",10,3,20,4)));
            MasterData(f,data);var t=Read(f).Tools[0];Check(t.MappingIdentified&&t.Punch.Supported&&t.Holder.Supported,"Nested keyed roles");
            Point(t.Punch.Curves[0].Start,97,24);Check(t.Punch.InstanceTransforms[0].Determinant<0&&t.Punch.EntityKeys[0].Contains("NEST"),"Reflected full instance path retained");
        });
        Test("master: duplicate and array INSERT roots cannot silently select a pair",()=> {
            foreach(bool array in new[]{false,true})
            {
                var f=Master(1);string data=File.ReadAllText(f.Source);string insert=Tags(0,"INSERT",5,"I0",8,"0",2,f.Mapping.Tools[0].PunchBlock,10,0,20,0);
                data=data.Replace(insert,insert+(array?Tags(70,2,71,1,44,50):insert));MasterData(f,data);var t=Read(f).Tools[0];
                Check(t.Punch==null&&t.Holder==null&&t.Reasons.Contains("AMBIGUOUS_PUNCH_INSTANCE"),"Ambiguous root stays empty");
            }
        });
        Test("section: L U Z and step include complete material boundary",()=> {
            var profiles=new[]{Polygon(P(0,0),P(20,0),P(20,2),P(2,2),P(2,20),P(0,20)),
                Polygon(P(0,0),P(20,0),P(20,20),P(18,20),P(18,2),P(2,2),P(2,20),P(0,20)),
                Polygon(P(0,0),P(20,0),P(20,12),P(40,12),P(40,14),P(18,14),P(18,2),P(0,2)),
                Polygon(P(0,0),P(20,0),P(20,12),P(40,12),P(40,24),P(38,24),P(38,14),P(18,14),P(18,2),P(0,2))};
            foreach(var p in profiles){var m=Model(Extrusion(p.Curves));var s=m.Sections.Single();Check(s.Coverage==SheetCoverage.SimulationOnly&&s.Region.IsValidRegion&&s.Boundary.Count==p.Curves.Count,"Whole closed silhouette");Near(s.ThicknessMm.Value,2,"Thickness");}
        });
        Test("section: both skins and actual inner/outer bend arcs survive",()=> {
            var c=new[]{Segment(0,-4,10,-4),Circular(10,0,4,-Math.PI/2,Math.PI/2),Segment(14,0,14,10),Segment(14,10,12,10),Segment(12,10,12,0),
                Circular(10,0,2,0,-Math.PI/2),Segment(10,-2,0,-2),Segment(0,-2,0,-4)};
            var s=Model(Extrusion(c)).Sections.Single();Check(s.Region.IsValidRegion&&s.Boundary.Count==8&&s.Boundary.Count(e=>e.Kind=="ARC")==2,"No Min window or centerline substitution");
            Check(s.Boundary.Where(e=>e.Kind=="ARC").Select(e=>e.RadiusMm).OrderBy(v=>v).SequenceEqual(new[]{2.0,4.0}),"Actual R retained");
        });
        Test("section: box/hole loops are explicitly unsupported",()=> {
            var s=Model(Extrusion(Square().Curves.Concat(Square(2,2,3).Curves))).Sections.Single();
            Check(s.Coverage==SheetCoverage.Unsupported2DEnvelope&&s.Reasons.Contains("UNSUPPORTED_SECTION_REGION")&&s.Boundary.Count==0,"No filled box/hole assumption");
        });
        Test("section: tabs, trim changes, cross-axis holes and missing bodies require 3D",()=> {
            var b=Extrusion(Square().Curves);var varying=new PlacementBodySnapshot(b.Id,b.Faces,b.Edges.Concat(new[]{new PlacementEdgeSnapshot(100,1,new PlacementVector3(0,0,0),new PlacementVector3(1,1,0),new int[0])}));
            Check(Model(varying).Sections[0].Coverage==SheetCoverage.Unsupported2DEnvelope,"Axial variation");
            var hole=new PlacementBodySnapshot(b.Id,b.Faces.Concat(new[]{new PlacementFaceSnapshot(100,2,new PlacementVector3(),new PlacementVector3(),new PlacementVector3(1,0,0),new int[0])}),b.Edges);
            Check(Model(hole).Sections[0].Reasons.Contains("CROSS_AXIS_CYLINDER_OR_HOLE"),"Cross-axis hole");Check(Model(b,complete:false).Sections[0].Coverage==SheetCoverage.Unsupported2DEnvelope,"Unassigned body coverage");
        });
        Test("section: no analytic arc metadata means unsupported, no chord replacement",()=> {
            var c=Circular(0,0,5,0,Math.PI);var b=Extrusion(new[]{c,ToolCurve.Line(c.End,c.Start,new ToolTransform())});
            var broken=b.Edges.Select(e=>new PlacementEdgeSnapshot(e.Id,e.Kind,e.A,e.B,e.Faces));
            Check(Model(new PlacementBodySnapshot(b.Id,b.Faces,broken)).Sections[0].Reasons.Contains("MISSING_OR_NONPLANAR_ARC_EVIDENCE"),"Missing arc evidence does not become a line");
        });
        Test("candidates: one explicit simulation and 17 missing still reports all 18",()=> {
            var library=Library(Read(Master(1)));var model=Model(Extrusion(Square().Curves));var frame=library.PlacementSources.Tools[0].Punch.FrameId;
            var sim=new PlacementSimulationRequest(CompanyToolIds.All[0],frame,P(0,0),P(0,-1),P(7,8),P(1,0));var d=BendPlacementBuilder.Build(model,library,simulation:sim);
            Check(d.Candidates.Count==18&&d.Candidates.Count(c=>c.PlacementGeometryComplete)==1,"All candidates, only one complete simulation");
            Check(d.Candidates.All(c=>!c.ReadyForCollisionCheck&&c.Status=="CHUA_XAC_MINH")&&d.WinnerToolId==null,"No manufacturing verdict/winner");
            var first=d.Candidates[0];Check(first.SimulationOnly,"Manual coordinate input marked simulation");Point(first.PunchPlacement.ToWorkFrame.Apply(P(0,0)),7,8);
            Check(first.BendStates.Count(s=>s.Section==null)==3,"Before/during/after remain unknown");
        });
        Test("candidates: 18 slots for every bend and no automatic datum placement",()=> {
            var library=Library(Read(Master()));var d=BendPlacementBuilder.Build(Model(Extrusion(Square().Curves),bends:5),library);
            Check(d.Candidates.Count==90&&d.Sections.Count==5,"Five bends x 18 tools");
            Check(d.Candidates.All(c=>c.PunchPlacement.ToWorkFrame==null&&c.Reasons.Contains("MISSING_DATUM_EVIDENCE")&&c.Reasons.Contains("UNMAPPED_LIMIT_RULE")),"No geometry-derived datums or family defaults");
        });
        Test("candidates: different DIM/master frames require explicit registration",()=> {
            var library=Library(Read(Master(1)));var model=Model(Extrusion(Square().Curves));string to=library.PlacementSources.Tools[0].Punch.FrameId;
            var sim=new PlacementSimulationRequest(CompanyToolIds.All[0],"OTHER_FRAME",P(2,3),P(0,-1),P(7,8),P(0,-1));
            var a=BendPlacementBuilder.Build(model,library,simulation:sim);Check(a.Candidates[0].PunchPlacement.ToWorkFrame==null&&a.Candidates[0].Reasons.Contains("UNVERIFIED_FRAME_REGISTRATION"),"No shared-origin assumption");
            var registration=new PlacementRegistration2D("OTHER_FRAME",to,new PlacementTransform2D(txMm:10,tyMm:20));
            sim=new PlacementSimulationRequest(CompanyToolIds.All[0],"OTHER_FRAME",P(2,3),P(0,-1),P(7,8),P(0,-1),registration:registration);
            var b=BendPlacementBuilder.Build(model,library,simulation:sim);Point(b.Candidates[0].PunchPlacement.ToWorkFrame.Apply(P(12,23)),7,8);Check(b.Candidates[0].SimulationOnly,"Registration remains simulation only");
        });
        Test("candidates: source/model/configuration invalidation hides readiness on held objects",()=> {
            var library=Library(Read(Master()));var model=Model(Extrusion(Square().Curves));var d=BendPlacementBuilder.Build(model,library);library.Invalidate();
            Check(!d.Current&&d.Candidates.All(c=>!c.Current&&c.Reasons.Contains("STALE_SOURCE")),"Library lease");
            library=Library(Read(Master()));d=BendPlacementBuilder.Build(model,library);model.Lease.Invalidate();Check(!d.Current,"Model stamp/config lease");
        });
        Test("candidates: diagnostic contains no model coordinates or transform matrices",()=> {
            var d=BendPlacementBuilder.Build(Model(Extrusion(Square().Curves)),Library(Read(Master())));string json=BendPlacementBuilder.Diagnostic(d);
            Check(!json.Contains("TxMm")&&!json.Contains("OriginMm")&&!json.Contains("Boundary")&&!json.Contains("RadiusMm")&&json.Contains("CandidateCount"),"Counts/reasons only");
        });
        Test("candidates: held draft checks datum freshness independently of geometry leases",()=> {
            var library=Library(Read(Master(1)));var draft=BendPlacementBuilder.Build(Model(Extrusion(Square().Curves)),library);
            library.PlacementSources.Tools[0].MasterDatums.Invalidate();var candidate=draft.Candidates[0];
            Check(draft.Current&&candidate.DatumStatus==DatumEvidenceStatus.Stale&&candidate.Reasons.Contains("STALE_DATUM_EVIDENCE"),"Datum stale does not masquerade as current evidence");
            Check(!candidate.ReadyForCollisionCheck&&candidate.Status=="CHUA_XAC_MINH","No stale evidence readiness");
        });

        Test("Excel-first: unsigned numeric case stays unresolved",()=> {
            var selection=new BendLimitSelection {MachineSystem="HDS",MaterialCode="ST",SectionKind="U",
                ThicknessMinMm=1,ThicknessMaxMm=3,AngleMinDeg=80,AngleMaxDeg=100,
                WMinMm=5,WMaxMm=90,LMinMm=5,LMaxMm=90};
            var conditions=new BendExcelCaseConditions(selection);
            Check(conditions.Complete,"Finite, bounded source conditions");
            var draft=new BendLimitRuleDraft("R1","SASH",BendLimitKind.MaxBendLength,new string('a',64),
                "金型限界表","cell:B3",new string('b',64),"mm",4200,new[]{CompanyToolIds.All[0]},new string[0],
                approvedConditions:conditions);
            var measured=new BendExcelObservation {ToolId=CompanyToolIds.All[0],MachineSystem="HDS",
                MaterialCode="ST",SectionKind="U",ThicknessMm=2,AngleDeg=90,Wmm=30,Lmm=35,
                BendLengthMm=4300,Current=true};
            Check(BendExcelCaseMatcher.Evaluate(draft,measured).Applicability==BendExcelCaseApplicability.MATCH_UNRESOLVED,
                "Unsigned Excel rule is NOT a certified MAX failure");
        });
        Test("Excel-first: bounded signed case only certifies bend-line length",()=> {
            var selection=new BendLimitSelection {MachineSystem="HDS",MaterialCode="ST",SectionKind="U",
                ThicknessMinMm=1,ThicknessMaxMm=3,AngleMinDeg=80,AngleMaxDeg=100,
                WMinMm=5,WMaxMm=90,LMinMm=5,LMaxMm=90};
            var conditions=new BendExcelCaseConditions(selection);
            // Managed fixture: trusted reviewer simulation for matcher logic.
            // Production approval is exclusively through ToolReviewVerifier.
            var review=new ToolReviewValidation(ToolReviewStatus.VerifiedExternalRecord,"SYNTHETIC_TEST");
            var rule=new BendLimitRuleDraft("R1","SASH",BendLimitKind.MaxBendLength,new string('a',64),
                "金型限界表","cell:B3",new string('b',64),"mm",4200,new[]{CompanyToolIds.All[0]},new string[0],review,conditions);
            var measured=new BendExcelObservation {ToolId=CompanyToolIds.All[0],MachineSystem="HDS",
                MaterialCode="ST",SectionKind="U",ThicknessMm=2,AngleDeg=90,Wmm=30,Lmm=35,
                BendLengthMm=4300,Current=true};
            var hit=BendExcelCaseMatcher.Evaluate(rule,measured);
            Check(hit.Applicability==BendExcelCaseApplicability.MATCH_CONFIRMED &&
                hit.ExceedsCertifiedLength==true,"4300 exceeds signed length 4200");
            measured.MaterialCode="SUS";
            Check(BendExcelCaseMatcher.Evaluate(rule,measured).Applicability==BendExcelCaseApplicability.NOT_APPLICABLE_CONFIRMED,
                "Complete verified source conditions can rule out a case");
            measured.MaterialCode="ST";measured.Wmm=null;
            Check(BendExcelCaseMatcher.Evaluate(rule,measured).Applicability==BendExcelCaseApplicability.MATCH_UNRESOLVED,
                "Unknown W dimension must never become negative match");
            review.Invalidate();
            measured.Wmm=30;
            Check(BendExcelCaseMatcher.Evaluate(rule,measured).Applicability==BendExcelCaseApplicability.MATCH_UNRESOLVED,
                "Expired/stale signed evidence blocks numeric decisions");
        });
        Test("limits: source cells, drawing IDs, media hash and phonetic text are read independently",()=> {
            string path=Workbook(Folder());var objects=BendLimitWorkbookReader.ReadObjects(File.ReadAllBytes(path),CancellationToken.None);
            Check(objects.Count==6&&objects.Single(o=>o.Locator=="cell:A1").Text=="対応長さ： ４.2Ｍ","Rich text excludes phonetics");
            Check(objects.Any(o=>o.Locator=="drawing:xl/drawings/drawing1.xml#7")&&objects.Any(o=>o.Kind=="Image"&&ToolDatumEvidence.IsSha256(o.Sha256)),"Stable drawing/media evidence");
        });
        Test("limits: length and deep examples retain distinct semantics and explicit units",()=> {
            string path=Workbook(Folder());var result=LimitRead(path,LimitMapping(path));Check(result.Rules.Count==2,"Two explicit rules");
            var length=result.Rules.Single(r=>r.Kind==BendLimitKind.MaxBendLength);Near(length.ValueMm.Value,4200,"Explicit m->mm");
            var example=result.Rules.Single(r=>r.Kind==BendLimitKind.DeepBendClearanceExample);Near(example.ValueMm.Value,80,"Example value read");
            Check(example.ReferenceOnly&&example.Reasons.Contains("REFERENCE_EXAMPLE_ONLY")&&!example.FamilyMappingVerified&&length.ToolIds.Count==1,"No per-tool clearance/length defaults");
        });
        Test("limits: image transcription and cached formulas cannot silently become numeric rules",()=> {
            string path=Workbook(Folder());var mapping=LimitMapping(path);mapping.Rules[0].Locator="cell:C1";mapping.Rules[0].NumberPattern=null;mapping.Rules[1].Locator="drawing:xl/drawings/drawing1.xml#8";
            var result=LimitRead(path,mapping);Check(result.Rules.All(r=>!r.ValueMm.HasValue)&&result.Rules.Any(r=>r.Reasons.Contains("UNVERIFIED_IMAGE_TRANSCRIPTION"))&&result.Rules.Any(r=>r.Reasons.Contains("UNVERIFIED_CACHED_FORMULA")),"No OCR/formula assumptions");
        });
        Test("limits: wrong workbook hash, missing locator, unknown tool family and invalid unit are visible",()=> {
            string path=Workbook(Folder());var mapping=LimitMapping(path);mapping.WorkbookSha256=new string('f',64);Check(LimitRead(path,mapping).Reasons.Contains("STALE_LIMIT_MAPPING"),"Workbook pin");
            mapping=LimitMapping(path);mapping.Rules[0].Locator="cell:ZZ999";mapping.Rules[1].ToolIds=new[]{"UNKNOWN"};Check(LimitRead(path,mapping).Rules.All(r=>r.Reasons.Contains("UNMAPPED_LIMIT_RULE")),"No fallback mappings");
            mapping=LimitMapping(path);mapping.Rules[0].Unit="unknown";Check(LimitRead(path,mapping).Rules[0].Reasons.Contains("UNMAPPED_LIMIT_VALUE_OR_UNIT"),"No guessed units");
        });
        Test("limits: zero remains zero, object hash changes do not reuse stale rule data",()=> {
            string path=Workbook(Folder());var mapping=LimitMapping(path);mapping.Rules[0].Locator="cell:D1";mapping.Rules[0].NumberPattern=null;mapping.Rules[0].Unit="mm";
            Near(LimitRead(path,mapping).Rules[0].ValueMm.Value,0,"Explicit zero");mapping.Rules[0].ExpectedObjectSha256=new string('a',64);
            var r=LimitRead(path,mapping).Rules[0];Check(!r.ValueMm.HasValue&&r.Reasons.Contains("STALE_LIMIT_OBJECT"),"Object provenance pin");
        });
        Test("limits: readable source fact is retained while tool applicability stays unmapped",()=> {
            string path=Workbook(Folder());var mapping=LimitMapping(path);mapping.Rules[0].ToolIds=new string[0];
            var r=LimitRead(path,mapping).Rules[0];Near(r.ValueMm.Value,4200,"Source fact independently transcribed");
            Check(r.Reasons.Contains("UNMAPPED_LIMIT_RULE")&&!r.FamilyMappingVerified,"No applicability guessed from source number");
        });
        Test("limits: boolean/error/date cell types never become numeric dimensions",()=> {
            foreach(string type in new[]{"b","e","d"})
            {
                string path=Workbook(Folder());ChangeWorkbook(path,"xl/worksheets/sheet1.xml",s=>s.Replace("<c r='B1'>","<c r='B1' t='"+type+"'>"));
                var r=LimitRead(path,LimitMapping(path)).Rules[1];Check(!r.ValueMm.HasValue&&r.Reasons.Contains("UNSUPPORTED_LIMIT_CELL_TYPE"),"Unsupported cell type");
            }
        });
        Test("limits: external drawing relationships are rejected without network access",()=> {
            string path=Workbook(Folder());ChangeWorkbook(path,"xl/drawings/_rels/drawing1.xml.rels",s=>s.Replace("Target='../media/test.bin'","Target='https://example.invalid/private.bin' TargetMode='External'"));
            Check(LimitRead(path,LimitMapping(path)).Reasons.Contains("MISSING_OR_INVALID_LIMIT_SOURCE"),"No external image fetch");
        });
        Test("sources: manifest/configuration edits invalidate held candidates without changing model capture",()=> {
            var f=Master(1);string workbook=Workbook(f.Root),limits=Path.Combine(f.Root,"limits.json"),config=Path.Combine(f.Root,"placement.json");
            File.WriteAllText(limits,Json.Serialize(LimitMapping(workbook)));
            var c=new BendPlacementConfiguration {Version=1,MasterPath="master.dxf",MasterManifestPath="master.json",LimitWorkbookPath=Path.GetFileName(workbook),LimitManifestPath="limits.json"};
            File.WriteAllText(config,Json.Serialize(c));
            var provider=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration {DirectoryPath=f.Root,PlacementConfigurationPath=config});
            var first=provider.Read(CancellationToken.None);var model=Model(Extrusion(Square().Curves));var draft=BendPlacementBuilder.Build(model,first);
            Check(draft.Current&&first.PlacementSources.Tools[0].Punch!=null,"Initial source");
            f.Mapping.Tools[0].HolderRootInsert="NO_SUCH_INSTANCE";Save(f);var second=provider.Read(CancellationToken.None);
            Check(!first.Current&&!draft.Current&&second.PlacementSources.Tools[0].Holder==null&&model.Lease.Current,"Manifest hash changes only library");
            var next=BendPlacementBuilder.Build(model,second);c.Version=999;File.WriteAllText(config,Json.Serialize(c));var third=provider.Read(CancellationToken.None);
            Check(!second.Current&&!next.Current&&third.PlacementSources.Tools.Count==18&&third.PlacementSources.Tools.All(t=>t.Punch==null),"Invalid config leaves explicit unknown slots");
        });
        Test("sources: cancellation propagates instead of publishing a fake missing library",()=> {
            var f=Master(1);var token=new CancellationToken(true);bool canceled=false;
            try{BendMasterAssemblyReader.Read(f.Source,f.Manifest,null,token);}catch(OperationCanceledException){canceled=true;}
            Check(canceled,"Cancellation is not missing source");
        });
        Test("sources: missing configuration creates 18 explicit unverified assemblies",()=> {
            var s=BendPlacementSources.Read(null,CancellationToken.None);Check(s.Tools.Count==18&&s.Tools.All(t=>!t.MappingIdentified)&&s.Status=="CHUA_XAC_MINH","No synthetic replacement for missing CAD");
        });
    }
}
