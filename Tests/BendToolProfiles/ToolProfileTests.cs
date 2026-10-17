using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private static int passed, failed, assertions;
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer { MaxJsonLength=int.MaxValue };
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    private static void Near(double actual, double expected, string message) { Check(Math.Abs(actual-expected)<=1e-8*Math.Max(1,Math.Abs(expected)),message+": "+actual+" vs "+expected); }
    private static void Point(ToolPoint actual, double x, double y) { Near(actual.X,x,"X"); Near(actual.Y,y,"Y"); }
    private static void Test(string name, Action test)
    { try { test(); passed++; Console.WriteLine("PASS "+name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL "+name+": "+ex.Message); } }
    private static string Tags(params object[] items)
    {
        var text=new StringBuilder(); foreach (var item in items) text.Append(Convert.ToString(item,CultureInfo.InvariantCulture)).Append('\n'); return text.ToString();
    }
    private static string Dxf(string entities, string blocks="", int? units=4, string header="") =>
        Tags(0,"SECTION",2,"HEADER",9,"$ACADVER",1,"AC1027")+(units.HasValue ? Tags(9,"$INSUNITS",70,units.Value) : "")+header+
        Tags(0,"ENDSEC",0,"SECTION",2,"BLOCKS")+blocks+Tags(0,"ENDSEC",0,"SECTION",2,"ENTITIES")+entities+Tags(0,"ENDSEC",0,"EOF");
    private static string Line(double x=0,double y=0,double endX=1,double endY=0,string handle="L",string layer="0") => Tags(0,"LINE",5,handle,8,layer,10,x,20,y,11,endX,21,endY);
    private static string Arc(double x=0,double y=0,double radius=1,double start=0,double end=90) => Tags(0,"ARC",5,"A",8,"0",10,x,20,y,40,radius,50,start,51,end);
    private static string Poly(bool closed=true,double bulge=0,string handle="P") => Tags(0,"LWPOLYLINE",5,handle,8,"0",90,3,70,closed?1:0,10,0,20,0,42,bulge,10,2,20,2,10,4,20,0);
    private static string Block(string name,string entities,double x=0,double y=0,int flags=0) => Tags(0,"BLOCK",2,name,70,flags,10,x,20,y)+entities+Tags(0,"ENDBLK");
    private static string Insert(string name,double x=0,double y=0,double sx=1,double sy=1,double rotation=0) => Tags(0,"INSERT",5,"I",8,"PUNCH",2,name,10,x,20,y,41,sx,42,sy,50,rotation);
    private static ToolDxfDocument Parse(string text,bool bom=false)
    {
        var bytes=new UTF8Encoding(bom).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        return ToolDxfReader.Read(bytes,new ToolProfileProvenance { Path="synthetic.dxf",ByteCount=bytes.Length,Sha256=ToolProfileLibraryProvider.Hash(bytes) },CancellationToken.None);
    }
    private static ToolDxfEntity One(ToolDxfDocument doc)
    { Check(doc.Source.Error==null,"Document: "+doc.Source.Error); Check(doc.Entities.Count==1,"One source entity"); Check(doc.Entities[0].UnsupportedReason==null,"Entity: "+doc.Entities[0].UnsupportedReason); return doc.Entities[0]; }
    private static string temporary;
    private static string Folder()
    { string path=Path.Combine(temporary,Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static void Save(string folder,string name,string text) { File.WriteAllText(Path.Combine(folder,name),text,new UTF8Encoding(false)); }
    private static ToolProfileLibraryProvider Provider(string folder,string manifest=null) => new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration { DirectoryPath=folder,ManifestPath=manifest });
    private static CompanyToolProfile First(ToolProfileLibrarySnapshot snapshot) => snapshot.Tools[0];
    private static ToolProfileManifest Manifest(params ToolProfileSelection[] sources) => new ToolProfileManifest { Version=1,Tools=new List<ToolProfileDefinition> { new ToolProfileDefinition { Id=CompanyToolIds.All[0],Sources=sources.ToList() } } };
    private static void WriteManifest(string folder,ToolProfileManifest manifest) { Save(folder,"manifest.json",Json.Serialize(manifest)); }
    private static ToolProfileSelection Selection(string role="CompanyPunchProfile",string file="source.dxf",string[] types=null) => new ToolProfileSelection { File=file,Role=role,EntityTypes=types??new[] {"LWPOLYLINE"} };
    private static void Wait(Func<bool> done)
    { var watch=Stopwatch.StartNew(); while (!done() && watch.ElapsedMilliseconds<10000) Thread.Sleep(5); Check(done(),"Async completion"); }
    private sealed class BadListener : TraceListener
    {
        internal int Count;
        public override void Write(string message) { }
        public override void WriteLine(string message) { if (message.StartsWith("[BEND TOOL LIBRARY]",StringComparison.Ordinal)) { Count++; throw new InvalidOperationException("injected diagnostic exception"); } }
    }
    private static int Main(string[] args)
    {
        if (args.Length>0 && args[0]=="--company") return CompanyIntegration(args);
        if (args.Length>0 && args[0]=="--placement-company") return PlacementCompanyIntegration(args);
        if (args.Length>0 && args[0]=="--holder-company") return HolderCompanyIntegration(args);
        if (args.Length>0 && args[0]=="--collision-company") return CollisionCompanyIntegration(args);
        if (args.Length>0 && args[0]=="--collision-oracle") return CollisionOracleFixtures(args);
        if (args.Length>0 && args[0]=="--workbook-objects") return WorkbookObjectInventory(args);
        temporary=Path.Combine(Path.GetTempPath(),"ADDIN-ToolProfileTests-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        Test("LINE retains coordinates and INSBASE; no tip recentering",()=> {
            var doc=Parse(Dxf(Line(20,-5,30,-5),header:Tags(9,"$INSBASE",10,7,20,8,30,9)));
            var e=One(doc); Point(e.Curves[0].Start,20,-5); Point(e.Curves[0].End,30,-5); Near(doc.Source.InsBaseX,7,"INSBASE"); Near(doc.Source.InsBaseZ,9,"INSBASE Z"); Check(e.Handle=="L","Handle retained");
        });
        Test("ARC exact radius and wrapped CCW sweep",()=> {
            var c=One(Parse(Dxf(Arc(2,3,5,350,10)))).Curves[0]; Point(c.Center,2,3); Near(c.RadiusMm,5,"Radius"); Near(c.SweepRadians,20*Math.PI/180,"Sweep"); Point(c.Start,2+5*Math.Cos(350*Math.PI/180),3+5*Math.Sin(350*Math.PI/180));
        });
        Test("ARC negative-Z OCS reflects center and sweep",()=> {
            var c=One(Parse(Dxf(Arc(3,4,2)+Tags(210,0,220,0,230,-1)))).Curves[0]; Point(c.Center,-3,4); Point(c.Start,-5,4); Point(c.End,-3,6); Near(c.SweepRadians,-Math.PI/2,"Reflected sweep");
        });
        Test("CIRCLE stays an analytic full circle",()=> {
            var e=One(Parse(Dxf(Tags(0,"CIRCLE",10,2,20,3,40,4)))); Near(e.Curves[0].RadiusMm,4,"Circle radius"); Near(e.Curves[0].SweepRadians,2*Math.PI,"Circle sweep"); Check(ToolProfileTopology.Closed(new[] {e}),"Circle closed");
        });
        Test("LWPOLYLINE quarter-circle bulge and source metadata",()=> {
            var e=One(Parse(Dxf(Poly(false,Math.Tan(Math.PI/8))))); var c=e.Curves[0]; Point(c.Center,0,2); Near(c.RadiusMm,2,"Bulge radius"); Near(c.SweepRadians,Math.PI/2,"Bulge sweep"); Check(e.SourceVertices.Count==3 && e.SourceBulges.Count==3,"Original vertices/bulges retained");
        });
        Test("negative and major bulges remain analytic",()=> {
            foreach (double bulge in new[] {-1.0,2.0}) { var e=One(Parse(Dxf(Poly(false,bulge)))); Near(e.Curves[0].SweepRadians,4*Math.Atan(bulge),"Signed major sweep"); Check(e.Curves.Count==2,"No tessellation"); }
        });
        Test("open polyline last bulge never invents a closing edge",()=> {
            var text=Tags(0,"LWPOLYLINE",90,2,70,0,10,0,20,0,10,2,20,0,42,1);
            var e=One(Parse(Dxf(text))); Check(e.SourceClosed==false && e.Curves.Count==1 && e.Curves[0].Kind=="LINE","Open source kept"); Check(!ToolProfileTopology.Closed(new[] {e}),"No repaired region"); Near(e.SourceBulges[1],1,"Unused terminal bulge preserved");
        });
        Test("open flag remains open even with coincident first/last vertex",()=> {
            var e=One(Parse(Dxf(Tags(0,"LWPOLYLINE",90,3,70,0,10,0,20,0,10,2,20,0,10,0,20,0)))); Check(!ToolProfileTopology.Closed(new[] {e}),"Do not reinterpret source closure");
        });
        Test("closed polyline preserves exactly the declared closure segment",()=> {
            var e=One(Parse(Dxf(Poly()))); Check(e.SourceClosed==true && e.Curves.Count==3 && ToolProfileTopology.Closed(new[] {e}),"Source closure");
        });
        Test("POLYLINE 2D vertices and bulge",()=> {
            var e=One(Parse(Dxf(Tags(0,"POLYLINE",5,"OLD",70,0,0,"VERTEX",10,0,20,0,42,1,0,"VERTEX",10,2,20,0,0,"SEQEND")))); Point(e.Curves[0].Center,1,0); Near(e.Curves[0].RadiusMm,1,"R12 bulge"); Check(e.SourceClosed==false,"Original open flag");
        });
        Test("declared units convert geometry and radius to mm",()=> {
            foreach (var pair in new[] {Tuple.Create(1,25.4),Tuple.Create(4,1.0),Tuple.Create(5,10.0),Tuple.Create(6,1000.0)})
            { var c=One(Parse(Dxf(Arc(1,2,3),units:pair.Item1))).Curves[0]; Point(c.Center,pair.Item2,2*pair.Item2); Near(c.RadiusMm,3*pair.Item2,"Unit radius"); }
        });
        Test("missing and unitless units are unverified, never assumed mm",()=> {
            foreach (int? unit in new int?[] {null,0,99}) { var doc=Parse(Dxf(Line(),units:unit)); Check(doc.Source.Error.Contains("INSUNITS") && doc.Entities.Count==0,"No fabricated scale"); }
        });
        Test("INSERT block base, rotation, scale and inherited layer",()=> {
            var doc=Parse(Dxf(Insert("B",10,20,2,2,90),Block("B",Line(2,3,3,3)+Arc(2,3),2,3)));
            Check(doc.Source.Error==null && doc.Entities.Count==2,"Expanded source"); var line=doc.Entities[0]; Point(line.Curves[0].Start,10,20); Point(line.Curves[0].End,10,22); Check(line.Layer=="PUNCH" && line.SourceLayer=="0" && line.Blocks.Single()=="B" && line.InstancePath.Contains("I:B"),"Insert provenance"); var c=doc.Entities[1].Curves[0]; Point(c.Center,10,20); Near(c.RadiusMm,2,"Scaled arc");
        });
        Test("nested INSERT transforms compose without origin normalization",()=> {
            var doc=Parse(Dxf(Insert("OUT",10,20,1,1,90),Block("INNER",Line(1,0,2,0))+Block("OUT",Insert("INNER",3,4))));
            var e=One(doc); Point(e.Curves[0].Start,6,24); Point(e.Curves[0].End,6,25); Check(e.Blocks.Count==2,"Nested path");
        });
        Test("INSERT reflection preserves circular radius and reverses sweep",()=> {
            var c=One(Parse(Dxf(Insert("B",sx:-2,sy:2),Block("B",Arc())))).Curves[0]; Point(c.Start,-2,0); Point(c.End,0,2); Near(c.RadiusMm,2,"Mirror R"); Near(c.SweepRadians,-Math.PI/2,"Mirror direction");
        });
        Test("INSERT negative-Z OCS",()=> {
            var e=One(Parse(Dxf(Insert("B",3,4)+Tags(230,-1),Block("B",Line())))); Point(e.Curves[0].Start,-3,4); Point(e.Curves[0].End,-4,4);
        });
        Test("manifest distinguishes repeated block instances by INSERT handle",()=> {
            string folder=Folder(); Save(folder,"source.dxf",Dxf(Insert("B")+Insert("B",10,20).Replace("5\nI\n","5\nSECOND\n"),Block("B",Poly())));
            var selection=Selection(); selection.Blocks=new[] {"B"}; selection.InsertHandles=new[] {"SECOND"}; WriteManifest(folder,Manifest(selection));
            var t=First(Provider(folder,"manifest.json").Read(CancellationToken.None)); var punch=t.Parts.Single(p=>p.Role==ToolGeometryRole.CompanyPunchProfile);
            Check(t.Validation.PunchGeometryReadComplete && punch.Entities.Count==1 && punch.Entities[0].InsertHandles.Single()=="SECOND","One intended instance"); Point(punch.Entities[0].Curves[0].Start,10,20);
        });
        Test("INSERT arrays retain unscaled rotated row/column spacing",()=> {
            var doc=Parse(Dxf(Insert("B",sx:2,sy:2,rotation:90)+Tags(70,2,71,1,44,5),Block("B",Line())));
            Check(doc.Entities.Count==2,"Two instances"); Point(doc.Entities[1].Curves[0].Start,0,5); Point(doc.Entities[1].Curves[0].End,0,7); Check(doc.Entities[0].InstancePath!=doc.Entities[1].InstancePath,"Distinct instances");
        });
        Test("nonuniform INSERT supports LINE but refuses circular approximation",()=> {
            var doc=Parse(Dxf(Insert("B",sx:2,sy:3),Block("B",Line()+Arc()))); Check(doc.Entities[0].Curves.Count==1,"Affine line retained"); Point(doc.Entities[0].Curves[0].End,2,0); Check(doc.Entities[1].UnsupportedReason.Contains("ellipse") && doc.Entities[1].Curves.Count==0,"No fake circular R");
        });
        Test("missing/cyclic/xref INSERT stays unverified",()=> {
            foreach (string text in new[] {Dxf(Insert("NO")),Dxf(Insert("B"),Block("B",Insert("B"))),Dxf(Insert("B"),Block("B",Line(),flags:4))})
            { var doc=Parse(text); Check(doc.Entities.Any(e=>e.UnsupportedReason!=null),"Explicit INSERT error"); }
        });
        Test("tilted OCS and nonzero elevation are rejected without projection",()=> {
            foreach (string entity in new[] {Arc()+Tags(210,.1,230,1),Line()+Tags(30,1),Poly()+Tags(38,2)})
            { var doc=Parse(Dxf(entity)); Check(doc.Entities.Single().UnsupportedReason!=null && doc.Entities.Single().Curves.Count==0,"Unsupported 3D retained as error"); }
        });
        Test("unsupported SPLINE remains metadata, annotations have no region",()=> {
            var doc=Parse(Dxf(Tags(0,"SPLINE",5,"S",8,"REF")+Tags(0,"DIMENSION",5,"D",2,"DIMBLOCK",0,"TEXT",1,"note"),Block("DIMBLOCK",Poly())));
            Check(doc.Entities[0].UnsupportedReason.Contains("SPLINE"),"Unsupported type"); Check(doc.Entities.Skip(1).All(e=>e.Annotation && e.Curves.Count==0),"No dimension-block expansion");
        });
        Test("UTF8 BOM and Unicode layer/handle provenance",()=> {
            var e=One(Parse(Dxf(Line(layer:"金型")),true)); Check(e.Layer=="金型" && e.Handle=="L","Unicode layer");
        });
        Test("malformed/missing numbers and fitted/width polylines fail closed",()=> {
            var entities=new[] {Tags(0,"LINE",10,0,20,0,11,1),Arc().Replace("1\n50\n","NaN\n50\n"),Poly()+Tags(43,2),Tags(0,"POLYLINE",70,8,0,"VERTEX",10,0,20,0,0,"VERTEX",10,2,20,0,0,"SEQEND"),Poly().Replace("90\n3\n","90\n4\n")};
            foreach (string entity in entities) Check(Parse(Dxf(entity)).Entities.Single().UnsupportedReason!=null,"Malformed geometry not guessed");
            Check(Parse(Dxf(Tags(0,"POLYLINE",70,0,0,"VERTEX",10,0,20,0))).Source.Error!=null,"Missing SEQEND");
            Check(Parse(Dxf(Line()).Replace("0\nEOF\n","")).Source.Error!=null,"Missing EOF");
        });
        Test("18 IDs always have explicit source state; missing file has no geometry",()=> {
            var snapshot=Provider(Folder()).Read(CancellationToken.None); Check(snapshot.Tools.Count==18 && snapshot.Tools.All(t=>!t.Validation.SourceFilesExist && !t.Validation.PunchGeometryReadComplete && t.Parts.Count==0),"All missing slots explicit"); Check(snapshot.Tools.All(t=>t.Validation.Status=="CHUA_XAC_MINH"),"No inferred tool");
        });
        Test("DIM layout separates punch, annotations and unclassified reference LINE",()=> {
            string folder=Folder(); Save(folder,"amd-std-003_DIM.dxf",Dxf(Poly()+Line()+Tags(0,"TEXT",1,"note",0,"DIMENSION",5,"DIM")));
            var s=Provider(folder).Read(CancellationToken.None); var t=First(s); Check(t.Validation.PunchGeometryReadComplete && t.Validation.PunchClosed,"One source polyline read"); Check(t.Parts.Single(p=>p.Role==ToolGeometryRole.CompanyPunchProfile).Entities.Count==1,"Only profile selected"); Check(t.Parts.Any(p=>p.Role==ToolGeometryRole.Annotations) && t.Parts.Any(p=>p.Role==ToolGeometryRole.UnclassifiedReference),"Reference LINE excluded"); Check(!t.Validation.ToolTipOriginVerified && !t.Validation.CoordinateFrameVerified && !t.Validation.ClampVerified,"No inferred origin or fit"); Check(s.Tools.Skip(1).All(p=>!p.Validation.PunchGeometryReadComplete),"Other IDs not fabricated");
        });
        Test("ambiguous DIM polylines do not pick first or largest",()=> {
            string folder=Folder(); Save(folder,"amd-std-003_DIM.dxf",Dxf(Poly()+Poly(handle:"SECOND"))); var t=First(Provider(folder).Read(CancellationToken.None)); Check(!t.Validation.PunchGeometryReadComplete && t.Parts.All(p=>p.Role!=ToolGeometryRole.CompanyPunchProfile),"Ambiguity retained");
        });
        Test("paper layout polyline cannot become the company punch",()=> {
            string folder=Folder(); Save(folder,"amd-std-003_DIM.dxf",Dxf(Poly()+Poly(handle:"PAPER")+Tags(410,"Layout1")));
            var t=First(Provider(folder).Read(CancellationToken.None)); Check(t.Validation.PunchGeometryReadComplete,"Only model-space candidate selected");
            Check(t.Parts.Single(p=>p.Role==ToolGeometryRole.CompanyPunchProfile).Entities.Single().Handle=="P","Model-space profile");
            Check(t.Parts.Single(p=>p.Role==ToolGeometryRole.Annotations).Entities.Single().Handle=="PAPER","Paper content remains annotation");
        });
        Test("open profile is readable but never repaired or verified",()=> {
            string folder=Folder(); Save(folder,"amd-std-003_DIM.dxf",Dxf(Poly(false))); var t=First(Provider(folder).Read(CancellationToken.None)); Check(t.Validation.PunchGeometryReadComplete && !t.Validation.PunchClosed && t.Validation.Status=="CHUA_XAC_MINH","Read vs verification separate"); Check(t.Parts.Single().Entities[0].Curves.Count==2,"No closure inserted");
        });
        Test("manifest selects exact LINE/ARC geometry and separates holder reference",()=> {
            string folder=Folder(); Save(folder,"source.dxf",Dxf(Poly()+Tags(0,"TEXT",1,"label")+Line(layer:"HOLDER")));
            var punch=Selection(); var holder=Selection("VerifiedHolderGeometry",types:new[] {"LINE"}); WriteManifest(folder,Manifest(punch,holder));
            var t=First(Provider(folder,"manifest.json").Read(CancellationToken.None)); Check(t.Validation.PunchGeometryReadComplete,"Selected punch"); Check(t.Parts.Any(p=>p.Role==ToolGeometryRole.HolderClampReference) && t.Parts.All(p=>p.Role!=ToolGeometryRole.VerifiedHolderGeometry) && !t.Validation.ClampVerified,"Clamp never promoted without proof");
        });
        Test("annotation selector cannot turn DIMENSION into punch",()=> {
            string folder=Folder(); Save(folder,"source.dxf",Dxf(Tags(0,"DIMENSION",5,"D"))); WriteManifest(folder,Manifest(Selection(types:new[] {"DIMENSION"})));
            var t=First(Provider(folder,"manifest.json").Read(CancellationToken.None)); Check(!t.Validation.PunchGeometryReadComplete && t.Parts.All(p=>p.Role==ToolGeometryRole.Annotations),"Annotation remains decoration");
        });
        Test("manifest unsupported selected punch is explicit",()=> {
            string folder=Folder(); Save(folder,"source.dxf",Dxf(Tags(0,"SPLINE",5,"S"))); WriteManifest(folder,Manifest(Selection(types:new[] {"SPLINE"})));
            var t=First(Provider(folder,"manifest.json").Read(CancellationToken.None)); Check(!t.Validation.PunchGeometryReadComplete && t.Validation.Reasons.Any(r=>r.Contains("SPLINE")),"Missing support reason");
        });
        Test("overlapping selectors cannot assign a punch to holder too",()=> {
            string folder=Folder(); Save(folder,"source.dxf",Dxf(Poly())); WriteManifest(folder,Manifest(Selection(),Selection("HolderClampReference")));
            var t=First(Provider(folder,"manifest.json").Read(CancellationToken.None)); Check(!t.Validation.PunchGeometryReadComplete && t.Parts.All(p=>p.Role==ToolGeometryRole.UnclassifiedReference),"Ambiguous role downgraded");
        });
        Test("source hash changes invalidate even with identical length/mtime",()=> {
            string folder=Folder(), path=Path.Combine(folder,"amd-std-003_DIM.dxf"); Save(folder,Path.GetFileName(path),Dxf(Poly())); var stamp=File.GetLastWriteTimeUtc(path); var provider=Provider(folder); var first=provider.Read(CancellationToken.None); var repeat=provider.Read(CancellationToken.None); Check(ReferenceEquals(first,repeat),"Unchanged immutable snapshot cached");
            string text=File.ReadAllText(path).Replace("10\n4\n","10\n5\n"); File.WriteAllText(path,text,new UTF8Encoding(false)); File.SetLastWriteTimeUtc(path,stamp);
            var next=provider.Read(CancellationToken.None); Check(first.Fingerprint!=next.Fingerprint && !first.Current && next.Current,"SHA invalidation"); Check(first.Sources.Single(s=>s.Path==path).Sha256!=next.Sources.Single(s=>s.Path==path).Sha256,"Content hash not timestamp");
            File.Delete(path); var missing=provider.Read(CancellationToken.None); Check(!next.Current && !First(missing).Validation.SourceFilesExist && First(missing).Parts.Count==0,"No stale last-good geometry on deletion");
        });
        Test("manifest SHA and edits invalidate selections",()=> {
            string folder=Folder(); Save(folder,"source.dxf",Dxf(Poly())); var selection=Selection(); selection.ExpectedSha256="wrong"; WriteManifest(folder,Manifest(selection)); var provider=Provider(folder,"manifest.json"); var first=provider.Read(CancellationToken.None); Check(!First(first).Validation.PunchGeometryReadComplete,"Expected source hash enforced"); selection.ExpectedSha256=null; WriteManifest(folder,Manifest(selection)); var next=provider.Read(CancellationToken.None); Check(!first.Current && next.ConfigurationSha256!=first.ConfigurationSha256 && First(next).Validation.PunchGeometryReadComplete,"Manifest content invalidation");
        });
        Test("invalid manifest and traversal have no guessed fallback",()=> {
            string folder=Folder(); Save(folder,"source.dxf",Dxf(Poly())); var s=Selection(); s.File="../source.dxf"; WriteManifest(folder,Manifest(s)); Check(!First(Provider(folder,"manifest.json").Read(CancellationToken.None)).Validation.PunchGeometryReadComplete,"Traversal rejected"); Save(folder,"manifest.json","{ bad }"); Check(Provider(folder,"manifest.json").Read(CancellationToken.None).Tools.All(t=>!t.Validation.PunchGeometryReadComplete),"No DIM fallback after invalid manifest");
        });
        Test("unconfigured and failing settings remain independent unverified data",()=> {
            var p=new ToolProfileLibraryProvider(()=> { throw new IOException("settings failure"); }); var s=p.Read(CancellationToken.None); Check(s.Error.Contains("settings failure") && s.Tools.Count==18,"Settings failure snapshot");
            var missing=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration()).Read(CancellationToken.None); Check(missing.Error.Contains("not configured"),"No user-path guessing");
        });
        Test("separate debug summary excludes CAD coordinates",()=> {
            string folder=Folder(); Save(folder,"amd-std-003_DIM.dxf",Dxf(Poly())); string json=Provider(folder).Read(CancellationToken.None).ToDebugJson(); var decoded=(Dictionary<string,object>)Json.DeserializeObject(json); Check(decoded["Status"].ToString()=="CHUA_XAC_MINH" && !json.Contains("\"Start\"") && !json.Contains("\"Center\"") && !json.Contains("SourceVertices"),"Counts/provenance only");
        });
        Test("managed worker does not block caller; diagnostic exception stays isolated",()=> {
            string folder=Folder(); Save(folder,"amd-std-003_DIM.dxf",Dxf(Poly())); using (var release=new ManualResetEventSlim(false))
            {
                var provider=new ToolProfileLibraryProvider(()=> { release.Wait(); return new ToolProfileSourceConfiguration { DirectoryPath=folder }; });
                using (var session=new ToolProfileLibrarySession(provider))
                {
                    var watch=Stopwatch.StartNew(); session.Refresh(); session.Poll(); Check(watch.ElapsedMilliseconds<200,"Caller not blocked on I/O");
                    release.Set(); var listener=new BadListener(); Debug.Listeners.Add(listener);
                    try
                    {
                        Wait(()=> {session.Poll(); return session.Snapshot.Error==null;});
#if DEBUG
                        Check(listener.Count==1 && session.DiagnosticError.Contains("injected"),"Diagnostic fault observed separately");
#else
                        Check(listener.Count==0 && string.IsNullOrEmpty(session.DiagnosticError),"Release compiler omits Debug diagnostic callback");
#endif
                        Check(First(session.Snapshot).Validation.PunchGeometryReadComplete,"Valid data survives diagnostic listener");
                    }
                    finally { Debug.Listeners.Remove(listener); }
                }
            }
        });
        Test("cancellation is observed and not converted to fake missing data",()=> {
            using (var c=new CancellationTokenSource()) { c.Cancel(); bool cancelled=false; try { Provider(Folder()).Read(c.Token); } catch (OperationCanceledException) {cancelled=true;} Check(cancelled,"Cancellation propagated"); }
        });
        RegionAndDatumTests();
        PlacementTests();
        HolderTests();
        CollisionTests();
        CollisionPlacementTests();
        Console.WriteLine("RESULT "+passed+" passed; "+failed+" failed; "+assertions+" assertions; synthetic DXF only, no SOLIDWORKS API.");
        // Keep failures available for inspection; no recursive cleanup of a
        // computed directory and never any operation on a company source folder.
        Console.WriteLine("Synthetic test output: "+temporary); return failed==0 ? 0 : 1;
    }
}
