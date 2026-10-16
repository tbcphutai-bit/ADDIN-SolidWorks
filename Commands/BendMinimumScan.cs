using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal struct BendVector
    {
        internal double X, Y, Z;
        internal BendVector(double x, double y, double z) { X = x; Y = y; Z = z; }
        internal static BendVector From(double[] p) => new BendVector(p[0], p[1], p[2]);
        public static BendVector operator +(BendVector a, BendVector b) => new BendVector(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static BendVector operator -(BendVector a, BendVector b) => new BendVector(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static BendVector operator *(BendVector a, double n) => new BendVector(a.X*n,a.Y*n,a.Z*n);
        internal double Length => Math.Sqrt(Dot(this,this));
        internal BendVector Unit => this * (1.0/Length);
        internal double[] Array => new[] { X,Y,Z };
        internal static double Dot(BendVector a,BendVector b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        internal static BendVector Cross(BendVector a,BendVector b) => new BendVector(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    }

    internal sealed class BendMinimumSite
    {
        internal Body2 Body;
        internal string BodyName, Name, MaterialName, MaterialCode;
        internal BendMinimumMaterialInfo MaterialInfo;
        internal BendMinimumGrooveInfo Groove;
        internal BendVector Axis, Root, Reference;
        internal double Min, Max;
        internal readonly List<Face2> Faces = new List<Face2>();
        internal readonly List<double> Radii = new List<double>();
        internal readonly List<double> Cuts = new List<double>();
        internal readonly List<BendMinimumCylinderBand> Bands = new List<BendMinimumCylinderBand>();
        internal bool UniformSweep;
        internal string CoverageNote;
    }
    internal sealed class BendMinimumSample
    {
        internal double PositionMm;
        internal BendMinimumSection Section;
        internal string Error;
        internal double ThicknessMm;
        internal readonly List<BendMinimumSection> Sections = new List<BendMinimumSection>();
        internal readonly List<BendMinimumLocalCheck> Checks = new List<BendMinimumLocalCheck>();
    }
    internal sealed class BendMinimumLocalCheck
    {
        internal BendMinimumSection Section;
        internal BendMinimumCheckResult Result;
    }
    internal sealed class BendMinimumScanRow
    {
        internal string BodyName, Name, MaterialName, MaterialCode, CoverageNote;
        internal BendMinimumMaterialInfo MaterialInfo;
        internal BendMinimumGrooveInfo Groove;
        internal BendMinimumSite Site;
        internal readonly List<BendMinimumSample> Samples = new List<BendMinimumSample>();
        internal BendMinimumSample Representative;
        internal BendMinimumCheckResult Evaluation;
        internal string Reason;
        internal BendMinimumStatus Status;
        internal void Evaluate(BendToolCatalogSnapshot catalog, string overrideMaterial)
        {
            string material = string.IsNullOrEmpty(overrideMaterial) ? MaterialCode : overrideMaterial;
            MaterialName = MaterialName ?? "";
            Representative = null; Evaluation = null; Status = BendMinimumStatus.InsufficientData;
            foreach (var sample in Samples) sample.Checks.Clear();
            // User-defined minimum-check policy: assigned V/C grooves are OK,
            // regardless of profile, material, nominal thickness or Excel limits.
            // Keep this distinct from a dimension measured against the table.
            if (Groove != null && Groove.State != BendMinimumGrooveState.None)
            {
                Representative = Samples.FirstOrDefault(s => s.Section != null) ?? Samples.FirstOrDefault();
                Status = Groove.State == BendMinimumGrooveState.Grooved ? BendMinimumStatus.GroovedAccepted : BendMinimumStatus.InsufficientData;
                Reason = Groove.State == BendMinimumGrooveState.Grooved
                    ? "OK — cạnh bào " + Groove.Kind + "; theo quy tắc đã chốt, không so giới hạn kích thước bẻ tối thiểu."
                    : "Chưa xác định được cạnh bào hay chấn thường: " + Groove.Note;
                Evaluation = new BendMinimumCheckResult(Status, Reason); return;
            }
            if (catalog == null) { Reason = "Nguồn Excel chưa hợp lệ; chưa kiểm tra được cạnh chấn thường."; return; }
            if (Samples.Count == 0) { Reason = CoverageNote ?? "Không tìm thấy mặt cắt chấn để kiểm tra."; return; }
            int worst = -1;
            foreach (var sample in Samples)
            {
                var sections = sample.Sections.Count > 0 ? sample.Sections : sample.Section == null ? new List<BendMinimumSection>() : new List<BendMinimumSection> { sample.Section };
                foreach (var section in sections)
                {
                    var current = catalog.CheckMinimum(material, sample.ThicknessMm, section.Shape,
                        section.Shape == "L" ? (double?)section.A : null, section.Shape == "Z" ? (double?)section.B : null,
                        section.Dimension == "C" ? (double?)section.C : null, 90);
                    sample.Checks.Add(new BendMinimumLocalCheck { Section = section, Result = current });
                    int rank = Rank(current.Status);
                    if (rank > worst)
                    { worst = rank; sample.Section = section; Representative = sample; Evaluation = current; Status = current.Status;
                        Reason = current.Message + (string.IsNullOrEmpty(section.Context) ? "" : " " + section.Context + "."); }
                }
                if (sections.Count == 0 || !string.IsNullOrEmpty(sample.Error))
                {
                    var current = new BendMinimumCheckResult(BendMinimumStatus.InsufficientData, sample.Error ?? "Không nhận dạng được mặt cắt.");
                    int rank = Rank(current.Status);
                    if (rank > worst) { worst = rank; Representative = sample; Evaluation = current; Status = current.Status; Reason = current.Message; }
                }
            }
            if (Site != null && !Site.UniformSweep && Status == BendMinimumStatus.MeetsMinimum)
            { Status = BendMinimumStatus.InsufficientData; Reason = "Các mặt cắt đã đo đạt tối thiểu; chưa kết luận toàn đường chấn. " + CoverageNote; }
            if (string.IsNullOrEmpty(material) && Representative != null && Representative.Section != null && Status != BendMinimumStatus.BelowMinimum)
            { Status = BendMinimumStatus.InsufficientData; Reason = "Chưa xác định được nhóm vật liệu trong Excel. Chọn AL/CU/BS/RBS/SUS/ST để kiểm tra."; }
        }
        private static int Rank(BendMinimumStatus status) => status == BendMinimumStatus.BelowMinimum ? 3
            : status == BendMinimumStatus.InsufficientData ? 2 : status == BendMinimumStatus.Conditional ? 1 : 0;
    }

    internal static class BendMinimumMaterial
    {
        // Only exact workbook codes are automatic. Grade/category mappings require
        // an explicit selection in the command and never change CAD properties.
        internal static string Code(string text)
        {
            string value=(text ?? "").Trim().ToUpperInvariant();
            return new[] { "AL","CU","BS","RBS","SUS","ST" }.Contains(value) ? value : null;
        }
        internal static string Read(ModelDoc2 model, Body2 body, string configuration)
        { string database; return Read(model, body, configuration, out database); }
        internal static string Read(ModelDoc2 model, Body2 body, string configuration, out string database)
        {
            database="";
            try { string value=body.GetMaterialPropertyName(configuration,out database); if (!string.IsNullOrWhiteSpace(value)) return value.Trim(); } catch(COMException) { }
            try { string value=((IPartDoc)model).GetMaterialPropertyName2(configuration,out database); if (!string.IsNullOrWhiteSpace(value)) return value.Trim(); } catch(COMException) { }
            database="";
            foreach(string config in new[]{configuration,""})
            foreach(string property in new[]{"材質","Material","MATERIAL"})
            {
                try {
                    var manager=model.Extension.get_CustomPropertyManager(config);
                    string raw,resolved;bool wasResolved,linked;
                    manager.Get6(property,false,out raw,out resolved,out wasResolved,out linked);
                    string value=string.IsNullOrWhiteSpace(resolved)?raw:resolved;
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                } catch(COMException) { }
            }
            return "";
        }
    }

    internal sealed class BendMinimumScanSession : IDisposable
    {
        private readonly ISldWorks app;
        internal readonly ModelDoc2 Model;
        internal readonly string Configuration, Title;
        internal readonly int UpdateStamp;
        internal readonly List<BendMinimumScanRow> Rows = new List<BendMinimumScanRow>();
        private readonly List<double> nominalThicknesses = new List<double>();
        private readonly BendMinimumMaterialLibraries materialLibraries;
        private readonly BendMinimumGrooveAssignments grooveAssignments;
        private readonly Queue<Body2> bodies = new Queue<Body2>();
        private readonly Queue<BendMinimumSite> sites = new Queue<BendMinimumSite>();
        private readonly BendMinimumSectionCache sectionCache = new BendMinimumSectionCache();
        private BendMinimumScanRow current;
        private int cutIndex, bodyIndex;
        internal int CompletedSections { get; private set; }
        internal int KernelSections => sectionCache.KernelSections;
        internal int SectionCacheHits => sectionCache.CacheHits;
        internal double DiscoveryMilliseconds { get; private set; }
        internal double SectionMilliseconds { get; private set; }
        internal bool Complete { get; private set; }
        internal string Progress => "Đang quét: " + Rows.Count + " vị trí, " + CompletedSections + " mặt cắt.";

        internal BendMinimumScanSession(ISldWorks application, ModelDoc2 model)
        {
            app=application; Model=model;
            if (model==null || model.GetType()!=(int)swDocumentTypes_e.swDocPART) throw new InvalidOperationException("Mở Part 3D ở trạng thái đã chấn rồi chạy lệnh.");
            Configuration=model.ConfigurationManager.ActiveConfiguration.Name;
            Title=model.GetTitle(); UpdateStamp=model.GetUpdateStamp();
            string materialLocations="";
            try { materialLocations=app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases); } catch(COMException) { }
            materialLibraries=new BendMinimumMaterialLibraries((materialLocations ?? "").Split(new[]{';','|','\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim().Trim('"')));
            grooveAssignments=new BendMinimumGrooveAssignments(model);
            var solids=((IPartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody,false) as object[];
            if (solids==null || solids.Length==0) throw new InvalidOperationException("Part hiện tại không có solid body để quét.");
            foreach(var item in solids) if(item is Body2 body) bodies.Enqueue(body);
            foreach(var feature in Features(model))
            {
                try {
                    if(IsSuppressed(feature))continue;
                    string type=feature.GetTypeName2();
                    if(new[]{"LoftedBend","FreeformBend","RuledBend","UiFreeformBend","ToroidalBend","SweptFlange","Hem"}.Contains(type))
                        Rows.Add(new BendMinimumScanRow{BodyName="Feature",Name=feature.Name,Groove=grooveAssignments.Read(feature),CoverageNote="Feature "+type+" cần kiểm tra riêng; bảng hiện tại mô tả L/U/Z 90 độ."});
                    if(type!="SheetMetal")continue;
                    var data=feature.GetDefinition() as ISheetMetalFeatureData;
                    if(data!=null && data.Thickness>0 && !nominalThicknesses.Any(t=>Math.Abs(t-data.Thickness*1000)<0.0000001)) nominalThicknesses.Add(data.Thickness*1000);
                } catch(COMException) { }
            }
        }
        internal bool IsCurrent()
        {
            try { return app.ActiveDoc!=null && app.IsSame(app.ActiveDoc,Model)==(int)swObjectEquality.swObjectSame
                && Model.ConfigurationManager.ActiveConfiguration.Name==Configuration && Model.GetUpdateStamp()==UpdateStamp; }
            catch(COMException) { return false; }
            catch(InvalidComObjectException) { return false; }
        }
        // One native body discovery or one kernel section per UI tick. All COM
        // geometry stays on SOLIDWORKS' STA; only Excel reading runs in Task.Run.
        internal void Step()
        {
            if(Complete) return;
            if(!IsCurrent()) throw new InvalidOperationException("Model/configuration đã thay đổi. Bấm Quét lại để lấy hình học hiện tại.");
            if(current!=null)
            {
                double position=current.Site.Cuts[cutIndex++];
                var clock=Stopwatch.StartNew();
                var sample=BendMinimumSolidReader.ReadCachedSection(app,current.Site,position,sectionCache);
                SectionMilliseconds+=clock.Elapsed.TotalMilliseconds;
                if(sample.Section!=null) {
                    var matches=nominalThicknesses.Where(t=>Math.Abs(t-sample.Section.ThicknessMm)<=0.00001).ToList();
                    sample.ThicknessMm=matches.Count==1?matches[0]:sample.Section.ThicknessMm;
                }
                current.Samples.Add(sample);CompletedSections++;
                if(cutIndex>=current.Site.Cuts.Count) current=null;
                return;
            }
            if(sites.Count>0)
            {
                var site=sites.Dequeue();
                current=new BendMinimumScanRow{Site=site,BodyName=site.BodyName,Name=site.Name,MaterialName=site.MaterialName,MaterialCode=site.MaterialCode,MaterialInfo=site.MaterialInfo,Groove=site.Groove,CoverageNote=site.CoverageNote};
                Rows.Add(current);cutIndex=0;
                if(site.Cuts.Count==0) current=null;
                return;
            }
            if(bodies.Count>0)
            {
                var body=bodies.Dequeue();bodyIndex++;
                string name;
                try{name=body.Name;}catch(COMException){name="Body "+bodyIndex;}
                try {
                    var clock=Stopwatch.StartNew();
                    var found=BendMinimumSolidReader.FindSites(body,name);
                    DiscoveryMilliseconds+=clock.Elapsed.TotalMilliseconds;
                    if(found.Count==0) Rows.Add(new BendMinimumScanRow{BodyName=name,Name="Chưa có vị trí chấn",CoverageNote="Không tìm thấy mặt chấn trụ nối hai mặt phẳng trong body/configuration này; hình học phẳng, côn hoặc chấn phức tạp cần xác minh."});
                    else {
                        var materialInfo=materialLibraries.Read(app,Model,body,Configuration);
                        foreach(var site in found) {
                        site.MaterialInfo=materialInfo;site.MaterialName=materialInfo.Name;site.MaterialCode=materialInfo.Code;
                        site.Groove=grooveAssignments.Read(site);
                        sites.Enqueue(site);
                    }}
                }catch(Exception ex){Rows.Add(new BendMinimumScanRow{BodyName=name,Name="Chưa quét được body",CoverageNote=ex.Message});}
                return;
            }
            Complete=true;
        }
        internal void Evaluate(BendToolCatalogSnapshot source,string material) { foreach(var row in Rows) row.Evaluate(source,material); }
        public void Dispose(){bodies.Clear();sites.Clear();sectionCache.Clear();current=null;}
        private static bool IsSuppressed(Feature feature)
        {
            var states=feature.IsSuppressed2((int)swInConfigurationOpts_e.swThisConfiguration,null) as bool[];
            return states!=null && states.Length>0 && states[0];
        }
        private static IEnumerable<Feature> Features(ModelDoc2 model)
        {
            var seen=new HashSet<string>(StringComparer.Ordinal);
            var stack=new Stack<Feature>();var root=model.FirstFeature() as Feature;
            while(root!=null){stack.Push(root);root=root.GetNextFeature() as Feature;}
            int count=0;
            while(stack.Count>0){var feature=stack.Pop();if(++count>10000)yield break;
                string key=feature.Name+"/"+feature.GetTypeName2();if(!seen.Add(key))continue;
                yield return feature;var child=feature.GetFirstSubFeature() as Feature;
                while(child!=null){stack.Push(child);child=child.GetNextSubFeature() as Feature;}
            }
        }
    }

    internal static partial class BendMinimumSolidReader
    {
        private const double Tolerance=0.0000001;
        private sealed class Cylinder
        { internal Face2 Face;internal BendVector Root,Axis,Reference;internal double Radius,Min,Max;internal bool Sense; }
        internal static List<BendMinimumSite> FindSites(Body2 body,string name)
        {
            var result=new List<BendMinimumSite>();
            var faces=body.GetFaces() as object[];
            if(faces==null)throw new InvalidOperationException("Không đọc được các mặt của body.");
            if(faces.Length>12000)throw new InvalidOperationException("Body quá nhiều mặt cho một lần quét; cần chia body trước khi kiểm tra.");
            foreach(var item in faces)
            {
                var face=item as Face2;if(face==null)continue;
                var cylinder=ReadCylinder(face);if(cylinder==null)continue;
                var site=result.FirstOrDefault(s=>Math.Abs(BendVector.Dot(s.Axis,cylinder.Axis))>.999999
                    && (s.Root-cylinder.Root).Length<=Tolerance && BendVector.Dot(s.Reference,cylinder.Reference)>.999999
                    && cylinder.Min<=s.Max+Tolerance && cylinder.Max>=s.Min-Tolerance);
                if(site==null){site=new BendMinimumSite{Body=body,BodyName=name,Axis=cylinder.Axis,Root=cylinder.Root,Reference=cylinder.Reference,Min=cylinder.Min,Max=cylinder.Max};result.Add(site);}
                site.Faces.Add(face);site.Radii.Add(cylinder.Radius);site.Min=Math.Min(site.Min,cylinder.Min);site.Max=Math.Max(site.Max,cylinder.Max);
                site.Bands.Add(new BendMinimumCylinderBand{Radius=cylinder.Radius,Min=cylinder.Min,Max=cylinder.Max,Sense=cylinder.Sense});
            }
            var gauges=result.SelectMany(s=>BendMinimumCylinderBand.PairedThicknesses(s.Bands)).ToList();
            // A through-thickness rounded cut wall is not a bend. An unpaired
            // cylinder that cannot be proved to be such a wall stays Unknown.
            result=result.Where(s=>BendMinimumCylinderBand.PairedThicknesses(s.Bands).Count>0 || !IsThroughThicknessWall(s,gauges)).ToList();
            var geometry=ReadSweepGeometry(body,faces);
            result=result.OrderBy(s=>s.Root.X).ThenBy(s=>s.Root.Y).ThenBy(s=>s.Root.Z).ThenBy(s=>s.Min).ToList();
            for(int i=0;i<result.Count;i++) {
                var site=result[i];site.Name="Chấn "+(i+1);
                if(BendMinimumCylinderBand.PairedThicknesses(site.Bands).Count==0) {
                    site.UniformSweep=false;site.CoverageNote="Cung chưa có cặp mặt trong/ngoài của đường chấn; cần xác minh hình học, không tự coi là đạt.";
                } else BuildCuts(site,geometry);
            }
            return result;
        }
        private static Cylinder ReadCylinder(Face2 face)
        {
            var surface=face.GetSurface() as Surface;if(surface==null || !surface.IsCylinder())return null;
            var values=surface.CylinderParams as double[];if(values==null || values.Length<7 || values[6]<=0)return null;
            var axis=BendVector.From(values.Skip(3).Take(3).ToArray()).Unit;
            double major=Math.Abs(axis.X)>=Math.Abs(axis.Y)&&Math.Abs(axis.X)>=Math.Abs(axis.Z)?axis.X:Math.Abs(axis.Y)>=Math.Abs(axis.Z)?axis.Y:axis.Z;
            if(major<0)axis=axis*-1;
            var origin=BendVector.From(values);var root=origin-axis*BendVector.Dot(origin,axis);
            var normals=new List<BendVector>();var axial=new List<double>();var radial=new BendVector(0,0,0);
            int tangentEdges=0;
            var edges=face.GetEdges() as object[];if(edges==null)return null;
            foreach(var item in edges)
            {
                var edge=item as Edge;var curve=edge?.GetCurve() as Curve;if(curve==null)continue;
                var data=edge.GetCurveParams3() as CurveParamData;if(data==null)continue;
                var start=BendVector.From((double[])data.StartPoint);var end=BendVector.From((double[])data.EndPoint);
                if((end-start).Length<=Tolerance || Math.Abs(BendVector.Dot((end-start).Unit,axis))<.999999)continue;
                var adjacent=edge.GetTwoAdjacentFaces2() as object[];if(adjacent==null)continue;
                foreach(var other in adjacent){var plane=(other as Face2)?.GetSurface() as Surface;
                    if(plane==null || !plane.IsPlane())continue;
                    var p=plane.PlaneParams as double[];if(p==null || p.Length<6)continue;
                    var normal=BendVector.From(p).Unit;
                    if(Math.Abs(BendVector.Dot(normal,axis))>0.000001)continue;
                    // A cylinder/plane tangent intersection is an exact generator,
                    // even if an imported/modeler edge is stored as a B-spline.
                    if(Math.Abs(Math.Abs(BendVector.Dot(root-BendVector.From(p.Skip(3).Take(3).ToArray()),normal))-values[6])>Tolerance)continue;
                    if(!normals.Any(n=>Math.Abs(BendVector.Dot(n,normal))>.999999))normals.Add(normal);
                    axial.Add(BendVector.Dot(start,axis));axial.Add(BendVector.Dot(end,axis));
                    var direction=start-root-axis*BendVector.Dot(start,axis);
                    if(direction.Length>Tolerance)radial+=direction.Unit;
                    tangentEdges++;
                }
            }
            // A hole has circular end edges and no two tangent planar skins.
            if(normals.Count<1 || normals.Count>2 || tangentEdges<2 || axial.Count<2 || axial.Max()-axial.Min()<=Tolerance)return null;
            return new Cylinder{Face=face,Root=root,Axis=axis,Reference=radial.Length>Tolerance?radial.Unit:normals[0],Radius=values[6],Min=axial.Min(),Max=axial.Max(),Sense=face.FaceInSurfaceSense()};
        }
        private static bool IsThroughThicknessWall(BendMinimumSite site,List<double> gauges)
        {
            // A sharp inner bend can have only an outer cylinder R=t. Keep it
            // Unknown rather than confusing it with a through-thickness cut.
            if(!gauges.Any(t=>Math.Abs(t-(site.Max-site.Min))<=Tolerance && site.Radii.All(r=>r>t+Tolerance)))return false;
            var planes=new List<double>();
            foreach(var face in site.Faces) {
                var feature=face.GetFeature() as Feature;
                if(feature!=null && new[]{"EdgeFlange","OneBend","SMBend","SketchBend","Hem","SMMiteredFlange"}.Contains(feature.GetTypeName2()))return false;
                foreach(var item in face.GetEdges() as object[] ?? new object[0]) {
                    var adjacent=(item as Edge)?.GetTwoAdjacentFaces2() as object[];
                    foreach(var other in adjacent ?? new object[0]) {
                        var surface=(other as Face2)?.GetSurface() as Surface;if(surface==null||!surface.IsPlane())continue;
                        var p=surface.PlaneParams as double[];if(p==null||p.Length<6)continue;
                        if(Math.Abs(BendVector.Dot(BendVector.From(p).Unit,site.Axis))>.999999)
                            planes.Add(BendVector.Dot(new BendVector(p[3],p[4],p[5]),site.Axis));
                    }
                }
            }
            return planes.Any(p=>Math.Abs(p-site.Min)<=Tolerance)&&planes.Any(p=>Math.Abs(p-site.Max)<=Tolerance);
        }
        internal static BendMinimumSample ReadSection(ISldWorks app,BendMinimumSite site,double position,List<SectionProfilePlanner.Curve> diagnosticBoundary=null)
        {
            var result=new BendMinimumSample{PositionMm=(position-site.Min)*1000};
            Body2 solid=null,sheet=null;Surface plane=null;object[] sectionBodies=null;
            try {
                var modeler=app.GetModeler() as Modeler;
                var origin=site.Root+site.Axis*position;
                var x=site.Reference.Unit;var y=BendVector.Cross(site.Axis,x).Unit;
                plane=modeler.CreatePlanarSurface2(origin.Array,site.Axis.Array,x.Array) as Surface;
                var box=site.Body.GetBodyBox() as double[];
                if(box==null || plane==null)throw new InvalidOperationException("Không tạo được mặt phẳng cắt tạm.");
                double size=Math.Max(.01,(new BendVector(box[3]-box[0],box[4]-box[1],box[5]-box[2])).Length*2);
                sheet=modeler.CreateSheetFromSurface(plane,new[]{-size,size,-size,size}) as Body2;
                solid=site.Body.Copy() as Body2;
                if(sheet==null||solid==null)throw new InvalidOperationException("Không tạo được bản sao body để đọc mặt cắt.");
                int error;
                sectionBodies=sheet.Operations2((int)swBodyOperationType_e.SWBODYINTERSECT,solid,out error) as object[];
                if(error!=0||sectionBodies==null||sectionBodies.Length==0)throw new InvalidOperationException("Không đọc được mặt cắt tại vị trí này (kernel "+error+").");
                var curves=new List<SectionProfilePlanner.Curve>();
                foreach(var item in sectionBodies){var body=item as Body2;if(body==null)continue;var edges=body.GetEdges() as object[];
                    if(edges==null)continue;
                    foreach(var edgeItem in edges){var curve=ReadCurve(edgeItem as Edge,origin,x,y);if(curve==null)throw new InvalidOperationException("Mặt cắt có spline/ellipse hoặc cạnh chưa hỗ trợ.");curves.Add(curve);}
                }
                diagnosticBoundary?.AddRange(curves);
                var center=new SectionProfilePlanner.Point(0,0);
                var seed=curves.FirstOrDefault(c=>c.IsArc && (c.Center-center).Length<=Tolerance*3
                    && site.Radii.Any(r=>Math.Abs(r-(c.A-c.Center).Length)<=Tolerance*3));
                if(seed==null)throw new InvalidOperationException("Không tìm thấy cung của đúng đường chấn trong mặt cắt; không chọn biên dạng khác.");
                List<BendMinimumSection> sections;string reason;
                bool measured=BendMinimumSectionReader.TryMeasureAll(curves,seed.Source,out sections,out reason);
                result.Error=reason;
                if(measured) {result.Sections.AddRange(sections);result.Section=sections[0];result.ThicknessMm=sections[0].ThicknessMm;}
            }catch(Exception ex){result.Error=ex.Message;}
            finally {
                if(sectionBodies!=null)foreach(var body in sectionBodies)ReleaseTemporary(body);
                ReleaseTemporary(solid);ReleaseTemporary(sheet);ReleaseTemporary(plane);
            }
            return result;
        }
        private static SectionProfilePlanner.Curve ReadCurve(Edge edge,BendVector origin,BendVector x,BendVector y)
        {
            if(edge==null)return null;var curve=edge.GetCurve() as Curve;var data=edge.GetCurveParams3() as CurveParamData;
            if(curve==null||data==null||(!curve.IsLine()&&!curve.IsCircle()))return null;
            Func<BendVector,SectionProfilePlanner.Point> project=p=>new SectionProfilePlanner.Point(BendVector.Dot(p-origin,x),BendVector.Dot(p-origin,y));
            // Trimmed edge endpoints are authoritative. Evaluating the untrimmed
            // curve can drift from the topological vertex after a Boolean cut.
            var a=BendVector.From((double[])data.StartPoint);var b=BendVector.From((double[])data.EndPoint);
            var result=new SectionProfilePlanner.Curve{A=project(a),B=project(b),Source=new object()};
            if(curve.IsCircle()) {
                var p=curve.CircleParams as double[];if(p==null||p.Length<7)return null;
                result.IsArc=true;result.Center=project(BendVector.From(p));
                double firstParameter,lastParameter;bool closed,periodic;
                // GetCurve can return a reparameterized trimmed curve. Its own
                // bounds, not the edge's underlying-curve bounds, locate the arc.
                if(!curve.GetEndParams(out firstParameter,out lastParameter,out closed,out periodic))return null;
                while(lastParameter<firstParameter)lastParameter+=2*Math.PI;
                var middle=project(BendVector.From((double[])curve.Evaluate2((firstParameter+lastParameter)/2,0)));
                double first=Math.Atan2(result.A.Y-result.Center.Y,result.A.X-result.Center.X);
                double mid=Math.Atan2(middle.Y-result.Center.Y,middle.X-result.Center.X);
                double end=Math.Atan2(result.B.Y-result.Center.Y,result.B.X-result.Center.X);
                double sweep=Normalize(end-first);double halfway=Normalize(mid-first);
                result.SweepAngleRadians=halfway<=sweep+0.000001?sweep:sweep-2*Math.PI;
                if((result.A-result.B).Length<=Tolerance)result.SweepAngleRadians=2*Math.PI;
            }
            return result;
        }
        private static double Normalize(double angle)=>(angle%(2*Math.PI)+2*Math.PI)%(2*Math.PI);
        private static int SourceEdgeKind(Edge edge,out BendVector center,out BendVector normal,out double radius)
        {
            center=new BendVector();normal=new BendVector();radius=0;
            var curve=edge.GetCurve() as Curve;
            if(curve.IsLine())return 1;
            if(curve.IsCircle()){var p=(double[])curve.CircleParams;center=BendVector.From(p);normal=BendVector.From(p.Skip(3).Take(3).ToArray()).Unit;radius=p[6];return 2;}
            var adjacent=edge.GetTwoAdjacentFaces2() as object[];
            if(adjacent==null||adjacent.Length!=2)return 0;
            var first=(adjacent[0] as Face2)?.GetSurface() as Surface;var second=(adjacent[1] as Face2)?.GetSurface() as Surface;
            if(first==null||second==null)return 0;
            if(first.IsPlane()&&second.IsPlane()){
                var n1=BendVector.From((double[])first.PlaneParams).Unit;var n2=BendVector.From((double[])second.PlaneParams).Unit;
                return BendVector.Cross(n1,n2).Length>0.000001?1:0;
            }
            Surface plane=first.IsPlane()?first:second.IsPlane()?second:null;
            Surface cylinder=first.IsCylinder()?first:second.IsCylinder()?second:null;
            if(plane==null||cylinder==null)return 0;
            var pp=(double[])plane.PlaneParams;var cp=(double[])cylinder.CylinderParams;
            var n=BendVector.From(pp).Unit;var root=BendVector.From(cp);var axis=BendVector.From(cp.Skip(3).Take(3).ToArray()).Unit;
            double alignment=BendVector.Dot(n,axis);
            if(Math.Abs(alignment)>.999999){center=root+axis*(BendVector.Dot(BendVector.From(pp.Skip(3).Take(3).ToArray())-root,n)/alignment);normal=axis;radius=cp[6];return 2;}
            if(Math.Abs(alignment)<0.000001&&Math.Abs(Math.Abs(BendVector.Dot(root-BendVector.From(pp.Skip(3).Take(3).ToArray()),n))-cp[6])<=Tolerance)return 1;
            return 0;
        }
        private static void ReleaseTemporary(object value){if(value!=null&&Marshal.IsComObject(value))try{Marshal.ReleaseComObject(value);}catch(InvalidComObjectException){} }
    }
}
