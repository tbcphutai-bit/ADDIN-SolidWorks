using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ADDIN.Commands.DxfChecking;

internal static class DrawingDxfTests
{
    static int passed, failed;
    static readonly DrawingDxfCheckOptions Options = new DrawingDxfCheckOptions();
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Case(string name, Action body)
    { try { body(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); } }
    static DxfEdge L(double x1,double y1,double x2,double y2) { return DxfGeometry.Line(new DxfPoint(x1,y1),new DxfPoint(x2,y2)); }
    static DxfEdge C(double x,double y,double r) { return DxfGeometry.Circle(new DxfPoint(x,y),r,0,DxfGeometry.Tau); }
    static List<DxfEdge> Rectangle(double x, double y, double w, double h)
    { return new List<DxfEdge>{L(x,y,x+w,y),L(x+w,y,x+w,y+h),L(x+w,y+h,x,y+h),L(x,y+h,x,y)}; }
    static List<DxfEdge> Drawing()
    { var e=Rectangle(0,0,1200,450); for(int i=0;i<10;i++)e.Add(C(100+i*90,50+i*25,4));return e; }
    static List<DxfEdge> Move(IEnumerable<DxfEdge> edges, double x, double y, double scale=1)
    {
        var result=new List<DxfEdge>();
        foreach(var e in edges)
        {
            var shift=new DxfPoint(x,y);
            if(e.Type=="LINE")result.Add(DxfGeometry.Line(e.Start*scale+shift,e.End*scale+shift));
            else result.Add(DxfGeometry.Circle(e.Center*scale+shift,e.Radius*scale,e.StartAngle,e.Sweep));
        }
        return result;
    }
    static DxfCheckResult Check(IEnumerable<DxfEdge> drawing,IEnumerable<DxfEdge> dxf) { return ContourMatcher.Check(drawing,dxf,Options); }
    static void Main(string[] args)
    {
        string root=args[0]; Directory.CreateDirectory(root);
        Case("1 identical DXF",()=>{var r=Check(Drawing(),Drawing());Assert(r.Passed&&r.MatchedInsideContours==10,"expected PASS/10 holes");});
        Case("2 half scale remains untouched",()=>{var dxf=Move(Drawing(),300,100,.5);var before=dxf[0].Length;var r=Check(Drawing(),dxf);Assert(!r.Passed&&!r.ScalePassed&&Math.Abs(r.EstimatedScale-.5)<1e-6,"scale fail");Assert(dxf[0].Length==before,"DXF modified");});
        Case("3 different origin +500,+300",()=>{var r=Check(Drawing(),Move(Drawing(),500,300));Assert(r.Passed&&Math.Abs(r.Translation.OffsetX+500)<1e-8&&Math.Abs(r.Translation.OffsetY+300)<1e-8,"translation");});
        Case("4 missing hole",()=>{var e=Drawing();e.RemoveAt(8);var r=Check(Drawing(),e);Assert(!r.Passed&&r.Mismatches.Any(m=>m.Type=="Missing inside contour")&&r.MatchedInsideContours==9,"missing hole");});
        Case("5 extra hole",()=>{var e=Drawing();e.Add(C(1050,350,4));var r=Check(Drawing(),e);Assert(!r.Passed&&r.Mismatches.Any(m=>m.Type=="Extra DXF contour"),"extra hole");});
        Case("6 moved hole 0.8mm",()=>{var e=Drawing();e[4]=C(100.8,50,4);var r=Check(Drawing(),e);Assert(!r.Passed&&Math.Abs(r.MaximumDeviation-.8)<1e-6&&r.MatchedInsideContours==9,"moved hole");});
        Case("7 diameter 8 vs 8.5",()=>{var e=Drawing();e[4]=C(100,50,4.25);var r=Check(Drawing(),e);Assert(!r.Passed&&Math.Abs(r.MaximumDeviation-.5)<1e-8,"diameter fail");});
        Case("8 internal cutout altered despite same count",()=>{var a=Drawing();a.AddRange(Rectangle(1000,300,40,15));var b=Drawing();b.AddRange(Rectangle(1000,300,40,16));var r=Check(a,b);Assert(!r.Passed&&r.DrawingInsideContourCount==r.DxfInsideContourCount&&r.FailedInsideContours==1,"cutout");});
        Case("9 removed leftmost notch does not shift alignment",()=>{
            var a=new List<DxfEdge>{L(0,0,1200,0),L(1200,0,1200,450),L(1200,450,0,450),L(0,450,0,250),L(0,250,-30,250),L(-30,250,-30,200),L(-30,200,0,200),L(0,200,0,0)};
            a.Add(C(100,50,4));a.Add(C(800,100,4));var b=Rectangle(0,0,1200,450);b.Add(C(100,50,4));b.Add(C(800,100,4));
            var r=Check(a,Move(b,500,300));Assert(r.ScalePassed&&!r.OuterContourPassed&&r.MatchedInsideContours==2&&Math.Abs(r.Translation.OffsetX+500)<1e-8,"notch/alignment");});
        Case("reversed lines and reordered holes",()=>{var e=Drawing().Select(x=>x.Type=="LINE"?DxfGeometry.Line(x.End,x.Start):x).Reverse().ToList();Assert(Check(Drawing(),e).Passed,"reverse/order");});
        Case("all DXF lines split, no holes available for alignment",()=>{var a=Rectangle(0,0,1200,450);var b=new List<DxfEdge>();foreach(var e in a){b.Add(DxfGeometry.Line(e.Start,e.At(.5)));b.Add(DxfGeometry.Line(e.At(.5),e.End));}Assert(Check(a,Move(b,500,300)).Passed,"split representation");});
        Case("diameter tolerance is diameter, not radius",()=>{var e=Drawing();e[4]=C(100,50,4.15);Assert(!Check(Drawing(),e).Passed,"0.3mm diameter should fail");});
        Case("combined center and radius deviation exceeds tolerance",()=>{var e=Drawing();e[4]=C(100.15,50,4.075);Assert(!Check(Drawing(),e).Passed,"combined radial geometry deviation .225mm");});
        Case("configurable tolerance",()=>{var e=Drawing();e[4]=C(100.8,50,4);Assert(ContourMatcher.Check(Drawing(),e,new DrawingDxfCheckOptions{ToleranceMm=1}).Passed,"1mm tolerance");});
        Case("small permitted deviations",()=>{var e=Drawing();e[4]=C(100.035,50,4);Assert(Check(Drawing(),e).Passed,".035 should pass");});
        Case("open DXF never passes",()=>{var e=Drawing();e.Add(L(15,15,16,16));Assert(!Check(Drawing(),e).Passed,"open extra");});
        Case("duplicate DXF geometry never passes",()=>{var e=Drawing();e.Add(L(0,0,1200,0));Assert(!Check(Drawing(),e).Passed,"duplicate");});
        Case("disconnected extra outline never passes",()=>{var e=Drawing();e.AddRange(Rectangle(1300,0,100,100));Assert(!Check(Drawing(),e).Passed,"disconnected");});
        Case("anisotropic stretch never passes",()=>{var e=Drawing();for(int i=0;i<4;i++){var old=e[i];e[i]=DxfGeometry.Line(new DxfPoint(old.Start.X*.9,old.Start.Y),new DxfPoint(old.End.X*.9,old.End.Y));}Assert(!Check(Drawing(),e).Passed,"stretch");});
        Case("rotation never corrected",()=>{var e=Drawing().Select(x=>x.Type=="LINE"?DxfGeometry.Line(new DxfPoint(-x.Start.Y,x.Start.X),new DxfPoint(-x.End.Y,x.End.X)):C(-x.Center.Y,x.Center.X,x.Radius));Assert(!Check(Drawing(),e).Passed,"rotation");});
        Case("mirror never corrected",()=>{var e=Drawing().Select(x=>x.Type=="LINE"?DxfGeometry.Line(new DxfPoint(-x.Start.X,x.Start.Y),new DxfPoint(-x.End.X,x.End.Y)):C(-x.Center.X,x.Center.Y,x.Radius));Assert(!Check(Drawing(),e).Passed,"mirror");});
        Case("closed arc contour and reverse traversal",()=>{
            var a=Drawing();a.Add(DxfGeometry.Circle(new DxfPoint(1000,350),10,0,Math.PI));a.Add(L(990,350,1010,350));
            var b=Drawing();b.Add(DxfGeometry.Circle(new DxfPoint(1000,350),10,Math.PI,-Math.PI));b.Add(L(1010,350,990,350));Assert(Check(a,b).Passed,"arc reverse");});
        Case("arc with wrong sweep fails",()=>{
            var a=Drawing();a.Add(DxfGeometry.Circle(new DxfPoint(1000,350),10,0,Math.PI));a.Add(L(990,350,1010,350));
            var b=Drawing();b.Add(DxfGeometry.Circle(new DxfPoint(1000,350),10,0,-Math.PI));b.Add(L(990,350,1010,350));Assert(!Check(a,b).Passed,"arc sweep");});
        Case("Drawing scale 1:2 buffer normalized to 1:1",()=>{var b=BufferRectangle(.5);var e=DrawingViewCurveBuffer.Read(b,.5);Assert(Check(Rectangle(0,0,1200,450),e).Passed,"view scale");});
        Case("Drawing scale 2:1 buffer normalized to 1:1",()=>{var e=DrawingViewCurveBuffer.Read(BufferRectangle(2),2);Assert(Check(Rectangle(0,0,1200,450),e).Passed,"view scale2");});
        Case("invalid truncated buffer rejected",()=>{bool rejected=false;try{DrawingViewCurveBuffer.Read(new double[]{0,0,7},1);}catch(InvalidDataException){rejected=true;}Assert(rejected,"buffer");});
        Case("SolidWorks zero padding after final complete record",()=>{var b=BufferRectangle(1).Concat(new double[714]).ToArray();Assert(Check(Rectangle(0,0,1200,450),DrawingViewCurveBuffer.Read(b,1)).Passed,"zero-filled allocation tail");});
        Case("empty record before more geometry remains rejected",()=>{var b=BufferRectangle(1).Concat(new double[9]).Concat(BufferRectangle(1)).ToArray();bool rejected=false;try{DrawingViewCurveBuffer.Read(b,1);}catch(InvalidDataException){rejected=true;}Assert(rejected,"interior empty record");});
        Case("all-zero buffer cannot pass",()=>{bool rejected=false;try{DrawingViewCurveBuffer.Read(new double[714],1);}catch(InvalidDataException){rejected=true;}Assert(rejected,"empty buffer");});
        Case("circle buffer uses exact diameter",()=>{var b=new List<double>{1,12,.1,.05,0,.104,.05,0,.104,.05,0,0,0,1,0,0,0,0,0,0,3,.104,.05,0,.096,.05,0,.104,.05,0};var e=DrawingViewCurveBuffer.Read(b.ToArray(),1);Assert(e[0].Type=="CIRCLE"&&Math.Abs(e[0].Radius-4)<1e-8,"circle");});
        Case("tilted Drawing arc projects to exact ellipse",()=>{
            double angle=Math.PI/30,c=Math.Cos(angle),s=Math.Sin(angle);var b=new List<double>{1,12,0,0,0,0,.005,0,0,.005,0,s,0,c,0,0,0,0,0,0,9};
            for(int i=0;i<=8;i++){double t=i*Math.PI/4;b.AddRange(new[]{-.005*c*Math.Sin(t),.005*Math.Cos(t),.005*s*Math.Sin(t)});}
            var e=DrawingViewCurveBuffer.Read(b.ToArray(),1);Assert(e[0].Type=="ELLIPSE"&&e[0].Closed&&Math.Abs(e[0].Bounds.Width-10*c)<1e-6&&Math.Abs(e[0].Bounds.Height-10)<1e-6,"projected equation");});
        Case("end-on thickness spline numeric noise is discarded in reference",()=>{
            var b=BufferRectangle(1).ToList();b.AddRange(new double[]{3,15,1,0,3,2,4,.02,.01,0,.020000003,.01,.004,0,0,1,1,0,0,0,0,0,0,2,.02,.01,0,.020000003,.01,.004});
            Assert(Check(Rectangle(0,0,1200,450),DrawingViewCurveBuffer.Read(b.ToArray(),1)).Passed,"point projection");});
        Case("single closed spline need not have explicit Closed flag",()=>{
            var e=new DxfEdge{Type="SPLINE",Evaluate=t=>new DxfPoint(10*Math.Cos(t*DxfGeometry.Tau),10*Math.Sin(t*DxfGeometry.Tau)),Derivative=t=>new DxfPoint(-10*DxfGeometry.Tau*Math.Sin(t*DxfGeometry.Tau),10*DxfGeometry.Tau*Math.Cos(t*DxfGeometry.Tau))};
            e.Start=e.At(0);e.End=e.At(1);e.Length=DxfGeometry.Length(e);e.Bounds=DxfBox.From(DxfGeometry.Sample(e,.002));var g=GeometryLoopBuilder.Build(new[]{e});Assert(g.Outer!=null&&g.Unclosed.Count==0,"single-edge loop");});
        Case("degenerate tiny DXF edge returns FAIL without topology exception",()=>{var e=Drawing();e.Add(L(20,20,20.000003,20));var r=Check(Drawing(),e);Assert(!r.Passed&&r.Mismatches.Any(m=>m.Type=="Open DXF geometry"),"degenerate DXF preserved and flagged");});
        Case("self-crossing zero-area DXF contour remains a reported failure",()=>{var e=Drawing();e.AddRange(new[]{L(20,20,30,30),L(30,30,20,30),L(20,30,30,20),L(30,20,20,20)});Assert(!Check(Drawing(),e).Passed,"self-crossing contour");});
        Case("spline segment index agrees with exhaustive geometric distance",()=>{
            var random=new Random(4114);var segments=new List<DxfPoint[]>();for(int i=0;i<500;i++)segments.Add(new[]{new DxfPoint(random.NextDouble()*3000,random.NextDouble()*200),new DxfPoint(random.NextDouble()*3000,random.NextDouble()*200)});
            segments.Add(new[]{new DxfPoint(0,0),new DxfPoint(0,0)});var index=new SegmentSpatialIndex(segments);
            for(int i=0;i<100;i++){var p=new DxfPoint(random.NextDouble()*4000-500,random.NextDouble()*500-150);double expected=segments.Min(s=>DxfGeometry.DistanceSegment(p,s[0],s[1]));Assert(Math.Abs(p.Distance(index.Nearest(p))-expected)<1e-9,"nearest segment distance");}});
        Case("Drawing spline equation read and sampled in mm",()=>{
            var b=new List<double>{3,20,2,0,3,3,6,0,0,0,.02,.01,0,.04,0,0,0,0,0,1,1,1,0,0,0,0,0,0,5};
            for(int i=0;i<=4;i++){double t=i/4.0;b.AddRange(new[]{.04*t,.02*t*(1-t),0});}
            var e=DrawingViewCurveBuffer.Read(b.ToArray(),1);Assert(e[0].Type=="SPLINE"&&e[0].At(.5).Distance(new DxfPoint(20,5))<1e-8,"spline equation");});
        Case("Drawing ellipse equation read in mm",()=>{
            var b=new List<double>{2,17,0,0,0,1,0,0,0,1,0,.02,.01,.02,0,0,.02,0,0,0,0,0,0,0,0,9};
            for(int i=0;i<=8;i++){double t=i*Math.PI/4;b.AddRange(new[]{.02*Math.Cos(t),.01*Math.Sin(t),0});}
            var e=DrawingViewCurveBuffer.Read(b.ToArray(),1);Assert(e[0].Type=="ELLIPSE"&&e[0].Closed&&Math.Abs(e[0].Bounds.Width-40)<1e-6,"ellipse equation");});
        Case("parser LINE+CIRCLE preserves DXF bytes and mm dimensions",()=>{
            string path=Path.Combine(root,"profile.dxf");File.WriteAllText(path,FileText(Drawing()));string before=Hash(path);int ignored;var e=new DxfGeometryReader().Read(path,"",out ignored);Assert(Check(Drawing(),e).Passed&&before==Hash(path),"reader/unchanged");});
        Case("parser LWPOLYLINE bulge and model-space filtering",()=>{
            string path=Path.Combine(root,"poly.dxf");string data="0\nLWPOLYLINE\n8\n0\n90\n4\n70\n1\n10\n0\n20\n0\n10\n1200\n20\n0\n10\n1200\n20\n450\n10\n0\n20\n450\n0\nLINE\n67\n1\n10\n0\n20\n0\n11\n10\n21\n10\n";File.WriteAllText(path,Wrap(data));int ignored;var e=new DxfGeometryReader().Read(path,"",out ignored);Assert(Check(Rectangle(0,0,1200,450),e).Passed,"polyline");});
        Case("non-mm DXF rejected instead of scaled",()=>{string p=Path.Combine(root,"inches.dxf");File.WriteAllText(p,Wrap("0\nCIRCLE\n10\n0\n20\n0\n40\n4\n").Replace("$INSUNITS\n70\n4","$INSUNITS\n70\n1"));int ignored;bool rejected=false;try{new DxfGeometryReader().Read(p,"",out ignored);}catch(InvalidDataException){rejected=true;}Assert(rejected,"units");});
        Case("ignored bend layer is explicit and configurable",()=>{string p=Path.Combine(root,"bend.dxf");File.WriteAllText(p,FileText(Drawing()).Replace("0\nENDSEC\n0\nEOF","0\nLINE\n8\nBEND\n10\n0\n20\n0\n11\n1200\n21\n450\n0\nENDSEC\n0\nEOF"));int ignored;var reader=new DxfGeometryReader();Assert(Check(Drawing(),reader.Read(p,"BEND",out ignored)).Passed&&ignored==1,"bend ignore");Assert(!Check(Drawing(),reader.Read(p,"",out ignored)).Passed,"bend included");});
        Case("spline geometry comparison does not use control count",()=>{
            var a=Drawing();var spline=new DxfEdge{Type="SPLINE",Evaluate=t=>new DxfPoint(1000+40*t,300+10*Math.Sin(Math.PI*t)),Derivative=t=>new DxfPoint(40,10*Math.PI*Math.Cos(Math.PI*t))};spline.Start=spline.At(0);spline.End=spline.At(1);spline.Length=DxfGeometry.Length(spline);spline.Bounds=DxfBox.From(DxfGeometry.Sample(spline,.002));a.Add(spline);a.Add(L(1040,300,1000,300));
            var b=Drawing();var points=DxfGeometry.Sample(spline,.001);for(int i=1;i<points.Count;i++)b.Add(DxfGeometry.Line(points[i-1],points[i]));b.Add(L(1040,300,1000,300));Assert(Check(a,b).Passed,"spline tessellation");});
        Case("all holes shifted cannot vote the outer origin away",()=>{
            var drawing=Drawing();var dxf=Move(drawing,500,300);
            for(int i=4;i<dxf.Count;i++) dxf[i]=C(dxf[i].Center.X+30,dxf[i].Center.Y,dxf[i].Radius);
            var r=Check(drawing,dxf);Assert(r.OuterContourPassed && !r.GeometryPassed && r.FailedInsideContours==10,"must separate outer and holes");
            Assert(Math.Abs(r.Translation.OffsetX+500)<1e-8 && Math.Abs(r.Translation.OffsetY+300)<1e-8 && Math.Abs(r.MaximumDeviation-30)<1e-6,"outer origin must stay fixed");});
        Case("duplicate outer edge is a cut warning with geometry PASS",()=>{
            var dxf=Drawing();dxf.Add(L(0,0,1200,0));var r=Check(Drawing(),dxf);
            Assert(r.GeometryPassed && !r.CutQualityPassed && r.Status=="WARN" && !r.Passed,"warn is never a clean PASS");
            Assert(r.Mismatches.Any(m=>m.CutQualityIssue && m.Type=="Duplicate DXF geometry"),"duplicate evidence");});
        Case("duplicate circle is preserved as cut warning",()=>{
            var dxf=Drawing();dxf.Add(C(100,50,4));var r=Check(Drawing(),dxf);
            Assert(r.GeometryPassed && r.MatchedInsideContours==10 && r.Status=="WARN","deduplicate comparison only");Assert(dxf.Count==15,"source list changed");});
        Case("extra open cut is separately WARN",()=>{
            var dxf=Drawing();dxf.Add(L(15,15,16,16));var r=Check(Drawing(),dxf);
            Assert(r.GeometryPassed && !r.CutQualityPassed && r.Status=="WARN","cut vs geometry status");});
        Case("missing outer edge is still geometry FAIL",()=>{
            var dxf=Drawing();dxf.RemoveAt(0);var r=Check(Drawing(),dxf);
            Assert(!r.GeometryPassed && r.Status=="FAIL","must not WARN missing contour");});
        Case("reference duplicate projection within numeric resolution is counted",()=>{
            var b=BufferRectangle(1).ToList();b.AddRange(new[]{0.0,0,0,0,0,0,0,0,2,0,.000000003,0,1.2,.000000003,0});
            DrawingNormalizationReport normalized;var e=DrawingViewCurveBuffer.Read(b.ToArray(),1,out normalized);
            Assert(Check(Rectangle(0,0,1200,450),e).Passed && normalized.DuplicateEdges==1 && normalized.AcceptedEdges==4,"near-duplicate reference");});
        Case("reference projected thickness below join resolution is counted",()=>{
            var b=BufferRectangle(1).ToList();b.AddRange(new[]{0.0,0,0,0,0,0,0,0,2,.02,.01,0,.02000044,.01,0});
            DrawingNormalizationReport normalized;var e=DrawingViewCurveBuffer.Read(b.ToArray(),1,out normalized);
            Assert(Check(Rectangle(0,0,1200,450),e).Passed && normalized.ShortThicknessLines==1,"tiny projection");});
        Case("the same tiny edge in DXF remains a quality warning",()=>{
            var dxf=Drawing();dxf.Add(L(20,10,20.00044,10));var r=Check(Drawing(),dxf);
            Assert(!r.Passed && r.Status=="WARN" && r.Mismatches.Any(m=>m.CutQualityIssue),"never normalize source cut quality away");});
        Case("indexed polygon containment matches exhaustive boundary and ray tests",()=>{
            var polygon=new List<DxfPoint>{new DxfPoint(0,0),new DxfPoint(1200,0),new DxfPoint(1200,450),new DxfPoint(600,450),new DxfPoint(600,300),new DxfPoint(0,300)};
            var dense=new List<DxfPoint>();for(int j=0;j<polygon.Count;j++){var p=polygon[j];var q=polygon[(j+1)%polygon.Count];for(int k=0;k<300;k++)dense.Add(p+(q-p)*(k/300.0));}
            var index=new PolygonContainmentIndex(dense,.001);var random=new Random(4114);
            Func<DxfPoint,bool> exhaustive=p=>{bool inside=false;for(int i=0,j=dense.Count-1;i<dense.Count;j=i++){
                var x=dense[j];var y=dense[i];if(DxfGeometry.DistanceSegment(p,x,y)<=.001)return true;
                if((x.Y>p.Y)!=(y.Y>p.Y) && p.X<(y.X-x.X)*(p.Y-x.Y)/(y.Y-x.Y)+x.X)inside=!inside;}return inside;};
            var points=Enumerable.Range(0,1000).Select(i=>new DxfPoint(random.NextDouble()*1400-100,random.NextDouble()*650-100)).ToList();
            foreach(var p in polygon)points.AddRange(new[]{p,p+new DxfPoint(.0005,0),p-new DxfPoint(.0005,0),p+new DxfPoint(0,.0005),p-new DxfPoint(0,.0015)});
            foreach(var p in points)Assert(index.Contains(p)==exhaustive(p),"containment changed at "+p.X+","+p.Y);});
        Case("file fingerprints support an open writer and detect saved-file changes",()=>{
            string path=Path.Combine(root,"open-file.dxf");File.WriteAllText(path,"original");
            var method=typeof(DrawingComparisonSource).GetMethod("Hash",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            using(var writer=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite))
            {
                string hash=(string)method.Invoke(null,new object[]{path});Assert(!string.IsNullOrEmpty(hash),"open-file hash");
                var source=new DrawingComparisonSource{DrawingPath="",DxfPath=path,DxfSha256=hash};source.VerifySavedFiles();
                writer.Position=0;writer.WriteByte((byte)'X');writer.Flush();
                bool rejected=false;try{source.VerifySavedFiles();}catch(InvalidDataException){rejected=true;}Assert(rejected,"saved file change must reject result");
            }});
        Console.WriteLine("RESULT "+passed+" passed; "+failed+" failed"); Environment.ExitCode=failed==0?0:1;
    }
    static double[] BufferRectangle(double scale)
    {
        var b=new List<double>();foreach(var e in Rectangle(0,0,1200,450)){b.AddRange(new double[]{0,0,0,0,0,0,0,0,2,e.Start.X*scale/1000,e.Start.Y*scale/1000,0,e.End.X*scale/1000,e.End.Y*scale/1000,0});}return b.ToArray();
    }
    static string Hash(string p) { using(var h=SHA256.Create())return Convert.ToBase64String(h.ComputeHash(File.ReadAllBytes(p))); }
    static string Wrap(string e){return "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n"+e+"0\nENDSEC\n0\nEOF\n";}
    static string FileText(IEnumerable<DxfEdge> edges)
    {
        var s=new System.Text.StringBuilder();foreach(var e in edges){s.Append("0\n"+e.Type+"\n8\n0\n");if(e.Type=="LINE")s.Append(string.Format(CultureInfo.InvariantCulture,"10\n{0:R}\n20\n{1:R}\n11\n{2:R}\n21\n{3:R}\n",e.Start.X,e.Start.Y,e.End.X,e.End.Y));else s.Append(string.Format(CultureInfo.InvariantCulture,"10\n{0:R}\n20\n{1:R}\n40\n{2:R}\n",e.Center.X,e.Center.Y,e.Radius));}return Wrap(s.ToString());
    }
}
