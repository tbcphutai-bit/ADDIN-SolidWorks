using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NativeMatrix;
using SolidWorks.Interop.sldworks;

internal static partial class NativeAssemblyBatchValidation
{
    // --excel-matrix product.dll NEW-output part-template min-xlsx reviewed-sidecar
    // No generated CAD, section sidecars or private CSV/Excel is checked in.
    private static int ExcelMatrixMain(string[] args)
    {
        if(args.Length!=5&&args.Length!=6){Console.WriteLine("--excel-matrix dll NEW-output part-template min-xlsx reviewed-sidecar.json [comma-separated-case-ids]");return 2;}
        string output=Path.GetFullPath(args[1]);
        if(Directory.Exists(output)||File.Exists(output))throw new IOException("New fixture output directory required");
        Directory.CreateDirectory(output);ISldWorks app=null;int code=1;
        var rows=new List<Dictionary<string,object>>();var report=new Dictionary<string,object>();
        try
        {
            // Validate ALL sources/rows/interpretations before creating native CAD.
            var input=MatrixInput.Read(args[4]);
            var selected=input.Cases;
            if(args.Length==6)
            {
                var ids=args[5].Split(',');Check(ids.Length>0&&ids.Distinct(StringComparer.Ordinal).Count()==ids.Length&&ids.All(id=>input.Cases.Any(c=>c.CaseId==id)),"Explicit case filter is unique and source-bound");
                selected=input.Cases.Where(c=>ids.Contains(c.CaseId)).ToArray();
            }
            report["Purpose"]=input.Specification.Purpose;report["SourceCases"]=input.Cases.Length;report["RequestedCases"]=selected.Length;
            report["SidecarSha256"]=input.SidecarSha256;
            report["SourceHashes"]=input.Specification.Sources.Select(s=>new{s.Role,s.Sha256}).ToArray();
            report["ExcelLengthPolicy"]="REFERENCE_ONLY_NOT_CERTIFIED_TOOL_CAPACITY";
            report["MeasurementToleranceMm"]=MatrixInput.MeasurementToleranceMm;
            var protectedPaths=input.Specification.Sources.Select(s=>s.Path).Concat(input.Specification.Materials.Select(m=>m.Database)).Concat(new[]{args[2],args[3],args[4]}).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p=>p,MatrixInput.Hash,StringComparer.OrdinalIgnoreCase);
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Check(Thread.CurrentThread.GetApartmentState()==ApartmentState.STA,"Native fixture generator/capture is STA");
            product=Assembly.LoadFrom(args[0]);
            Check(System.Environment.GetEnvironmentVariable("ADDIN_BEND_USE_MASTER_PAIRS")=="1","Explicit company Master-pair mode required");
            app=NativeTestProcess.StartOwned(output);app.Visible=false;app.UserControl=false;
            foreach(var item in selected)
            {
                var row=new Dictionary<string,object> {{"CaseId",item.CaseId},{"CsvRecord",item.CsvRecord},{"Shape",item.Shape},
                    {"RequestedAxialLengthMm",item.AxialLengthMm},{"RequestedW",item.W},{"RequestedL",item.L},{"RequestedThicknessMm",item.ThicknessMm},
                    {"MaterialGroup",item.MaterialGroup},{"ReferenceAxialLengthMm",item.ReferenceAxialLengthMm},{"LengthReferenceRelation",item.LengthReferenceRelation},
                    {"SourceInterpretation",item.Section.Evidence},
                    {"Execution","NOT_RUN"},{"ManufacturingStatus","CHUA_XAC_MINH"}};
                rows.Add(row);ModelDoc2 part=null;Form form=null;IDisposable controllerDisposable=null;
                try
                {
                    input.CheckSourcesCurrent();var creation=Stopwatch.StartNew();string path=Path.Combine(output,item.CaseId+".SLDPRT");
                    part=MatrixSheet(app,args[2],path,item);row["FixturePath"]=path;row["GenerationMilliseconds"]=creation.Elapsed.TotalMilliseconds;
                    Activate(app,part);
                    MatrixSettleGeneratedFixture(part);
                    var measured=MatrixNativeGeometry(part,item);
                    row["NativeGeometry"]=measured;
                    MatrixAuditGeometry(measured,item);
                    // Measure audit also checks physical bend owners, angle,
                    // radius and thickness without reading Check Min's graph.
                    string state=Json.Serialize(State(part)),sha=MatrixInput.Hash(path);
                    form=(Form)Activator.CreateInstance(product.GetType("ADDIN.UI.BendAssemblyBatchForm",true),All,null,
                        new object[]{part.GetTitle(),part.ConfigurationManager.ActiveConfiguration.Name},null);
                    var settings=New("BendToolStorageSettings",Path.Combine(output,"settings-"+item.CaseId+".json"));
                    Call(settings,"SavePath",args[3],CancellationToken.None);
                    var backend=Activator.CreateInstance(product.GetType("ADDIN.UI.BendAssemblyBatchLiveBackend",true),All,null,new[]{app,part,New("BendToolCatalogProvider",settings)},null);
                    var controller=Activator.CreateInstance(product.GetType("ADDIN.UI.BendAssemblyBatchController",true),All,null,new[]{form,backend},null);
                    controllerDisposable=(IDisposable)controller;
                    Call(form,"SetPartMode");form.Show();
                    var cold=MatrixRun(form,controller,item);CheckCompanyCadPairs(cold);UiCheckBindings(form,cold);CheckMinimumProjection(backend,cold);
                    var coldPart=Items(Get(cold,"Parts")).Single();
                    var nativeIds=(int[])measured["FeatureIds"];
                    Check(Items(Get(coldPart,"BendMetadata")).Select(m=>(int)Get(m,"FeatureId")).OrderBy(id=>id).SequenceEqual(nativeIds),"Captured native FeatureIds equal independent cylindrical-face owners");
                    var geometry=(System.Collections.IDictionary)Get(Get(Items(Get(Get(backend,"session"),"Items")).Single(),"Context"),"Geometry");
                    Check(geometry.Count>0&&geometry.Values.Cast<object>().All(g=>Convert.ToString(Get(g,"MaterialCode"))==item.MaterialGroup),"Native material classification matches CSV group");
                    var coldResult=MatrixResults(cold,coldPart);string coldFacts=Json.Serialize(coldResult);
                    File.WriteAllText(Path.Combine(output,"PRIVATE-cold-"+item.CaseId+".json"),coldFacts);
                    row["Cold"]=coldResult;row["ColdTiming"]=UiTimings(controller);
                    row["ColdMetrics"]=MatrixMetrics(cold);
                    var warm=MatrixRun(form,controller,item);CheckMinimumProjection(backend,warm);UiCheckBindings(form,warm);
                    var warmResult=MatrixResults(warm,Items(Get(warm,"Parts")).Single());
                    File.WriteAllText(Path.Combine(output,"PRIVATE-warm-"+item.CaseId+".json"),Json.Serialize(warmResult));
                    row["Warm"]=warmResult;
                    row["WarmTiming"]=UiTimings(controller);row["WarmMetrics"]=MatrixMetrics(warm);
                    Check(MatrixInput.SameJsonFacts(Json.Serialize(warmResult),coldFacts),"Cold/warm status, reasons, witness, tolerance, Min and planner decisions are identical");
                    Check((int)Get(warm,"Captures")==0&&(int)Get(warm,"CaptureHits")==1,"Warm check reuses one actual native capture");
                    Check(Json.Serialize(State(part))==state&&MatrixInput.Hash(path)==sha,"Check Chấn did not modify generated Part");
                    if(ReferenceEquals(item,selected[0]))
                    {
                        part.ForceRebuild3(false);UiSpin(()=>Get(controller,"displayed")==null);
                        UiClick(form,"btnRefresh");UiSpin(()=>!(bool)Get(controller,"Running"));
                        var refreshed=Get(controller,"displayed");Check(refreshed!=null&&(bool)Get(refreshed,"Current"),"Rebuild withdraws held result; Refresh recaptures actual native Part");
                        CheckMinimumProjection(backend,refreshed);row["RebuildRefresh"]="PASS";
                        Check(Items(Get(Items(Get(refreshed,"Parts")).Single(),"BendMetadata")).Select(m=>(int)Get(m,"FeatureId")).OrderBy(id=>id).SequenceEqual(nativeIds),"Native identities survive rebuild/Refresh");
                    }
                    input.CheckSourcesCurrent();row["Execution"]="PASS";
                }
                catch(Exception ex){row["Execution"]="FAIL";row["Error"]=ex.ToString();Console.WriteLine("FAIL "+item.CaseId+": "+ex.Message);}
                finally
                {
                    controllerDisposable?.Dispose();form?.Dispose();
                    if(part!=null)try{app.CloseDoc(part.GetTitle());}catch(Exception ex){row["CloseError"]=ex.ToString();row["Execution"]="FAIL";}
                    report["Cases"]=rows;File.WriteAllText(Path.Combine(output,"PRIVATE-native-matrix.json"),Json.Serialize(report));
                    Console.WriteLine("CASE "+item.CaseId+" "+row["Execution"]);
                }
            }
            input.CheckSourcesCurrent();
            var wrongThread=T("BendEnvelope.SheetNativeTopologyCapture").GetProperty("WrongThreadCount",All).GetValue(null,null);
            Check(Convert.ToInt32(wrongThread)==0,"No SOLIDWORKS topology capture from worker");report["WrongThreadCaptureCount"]=wrongThread;
            Check(protectedPaths.All(p=>File.Exists(p.Key)&&MatrixInput.Hash(p.Key)==p.Value),"Original sources, material libraries, template, Min workbook and sidecar bytes unchanged");
            report["ProtectedSourcesUnchanged"]=true;
            report["FunctionalResult"]=rows.All(r=>Equals(r["Execution"],"PASS"))?"PASS":"FAIL";
            code=Equals(report["FunctionalResult"],"PASS")?0:1;
        }
        catch(Exception ex){report["FunctionalResult"]="FAIL";report["Error"]=ex.ToString();Console.WriteLine(ex);}
        finally
        {
            report["Assertions"]=assertions;report["Cases"]=rows;
            report["PassCount"]=rows.Count(r=>Equals(r["Execution"],"PASS"));report["FailCount"]=rows.Count(r=>Equals(r["Execution"],"FAIL"));
            report["NotRunCount"]=report.ContainsKey("RequestedCases")?(int)report["RequestedCases"]-rows.Count(r=>!Equals(r["Execution"],"NOT_RUN")):0;
            if(app!=null)try{NativeTestProcess.RestoreCommandScope(app);app.ExitApp();}catch(Exception ex){report["ShutdownError"]=ex.ToString();}
            int? exit=NativeTestProcess.WaitForOwnedExit(10000);report["ShutdownExitCode"]=exit;
            report["ShutdownResult"]=app==null?"NOT_RUN":exit==0&&!report.ContainsKey("ShutdownError")?"PASS":exit.HasValue?"FAIL":"NOT_CONFIRMED";
            report["Result"]=code==0&&Equals(report["ShutdownResult"],"PASS")?"PASS":"FAIL";
            if(!Equals(report["Result"],"PASS"))code=1;
            File.WriteAllText(Path.Combine(output,"PRIVATE-native-matrix.json"),Json.Serialize(report));
        }
        return code;
    }
    private static object MatrixRun(Form form,object controller,MatrixCase item)
    {
        UiClick(form,"btnRunBatch");UiSpin(()=>!(bool)Get(controller,"Running"));var snapshot=Get(controller,"displayed");
        Check(snapshot!=null&&(bool)Get(snapshot,"Current"),"Real UI run current: "+UiControl(form,"lblProgressTitle").Text);
        var parts=Items(Get(snapshot,"Parts"));Check(parts.Length==1&&(int)Get(parts[0],"BendCount")==item.ExpectedBends,"Native bend count matches source interpretation");
        return snapshot;
    }
    private static void MatrixSettleGeneratedFixture(ModelDoc2 part)
    {
        // Creating a new sketch/material/configuration posts native rebuild
        // notifications. Finish fixture preparation BEFORE observing the live
        // session's revision. Never retry or suppress a stale completed check.
        var total=Stopwatch.StartNew();var stable=Stopwatch.StartNew();int stamp=part.GetUpdateStamp();
        while(total.ElapsedMilliseconds<5000&&stable.ElapsedMilliseconds<250)
        {
            NativeTestProcess.PumpMessages();Thread.Sleep(5);int next=part.GetUpdateStamp();
            if(next!=stamp||part.Extension.NeedsRebuild2!=0){stamp=next;stable.Restart();}
        }
        Check(stable.ElapsedMilliseconds>=250&&part.Extension.NeedsRebuild2==0,"New native fixture reaches a stable rebuilt revision before capture");
        if(part.GetSaveFlag())Save(part);
    }
    private static object MatrixMetrics(object snapshot)=>new {Captures=Get(snapshot,"Captures"),CaptureHits=Get(snapshot,"CaptureHits"),
        CaptureMilliseconds=Get(snapshot,"CaptureMilliseconds"),KernelQueries=Get(snapshot,"KernelQueries"),PeakWorkers=Get(snapshot,"PeakWorkers"),
        StaticHits=Get(snapshot,"StaticHits"),StaticMisses=Get(snapshot,"StaticMisses"),ProcessHits=Get(snapshot,"ProcessHits"),ProcessMisses=Get(snapshot,"ProcessMisses")};
    private static object MatrixResults(object snapshot,object part)
    {
        var metadata=Items(Get(part,"BendMetadata")).OrderBy(m=>(int)Get(m,"FeatureId")).ToArray();
        var statics=Items(Get(part,"StaticCandidates")).OrderBy(c=>(int)Get(c,"FeatureId")).ThenBy(c=>Convert.ToString(Get(c,"ToolId")),StringComparer.Ordinal).ToArray();
        var processes=Items(Get(part,"ProcessCandidates")).OrderBy(c=>(int)Get(c,"FeatureId")).ThenBy(c=>Convert.ToString(Get(c,"ToolId")),StringComparer.Ordinal).ToArray();
        foreach(var m in metadata)
        {
            int id=(int)Get(m,"FeatureId");var s=statics.Where(c=>(int)Get(c,"FeatureId")==id).ToArray();var p=processes.Where(c=>(int)Get(c,"FeatureId")==id).ToArray();
            Check(s.Length==18&&p.Length==18&&s.Select(c=>Get(c,"ToolId")).Distinct().Count()==18&&p.Select(c=>Get(c,"ToolId")).Distinct().Count()==18,"Each real bend retains 18 independent Punch/Holder and process candidates");
        }
        Func<object,object[]> reasons=p=>Items(Get(p,"Reasons")).OrderBy(r=>Convert.ToString(r),StringComparer.Ordinal).ToArray();
        Func<object,object> pair=p=>new {Status=Convert.ToString(Get(p,"Status")),Reasons=reasons(p),
            CertifiedClearanceLowerMm=Get(p,"CertifiedClearanceLowerMm"),Witness=Get(p,"Witness")};
        var planning=Get(part,"Planning");Check(planning!=null&&Items(Get(planning,"Tools")).Length==18,"Phase 4B evaluates all 18 tool candidates");
        return new {
            BendCount=Get(part,"BendCount"),ManufacturingStatus=Get(part,"ManufacturingStatus"),WinnerToolId=Get(part,"WinnerToolId"),
            Minimum=metadata.Select(m=>new {FeatureId=Get(m,"FeatureId"),PersistentId=Get(m,"PersistentId"),Name=Get(m,"Name"),
                Status=Convert.ToString(Get(m,"MinimumStatus")),Summary=Get(m,"MinimumSummary"),Details=Get(m,"MinimumDetails"),Sides=Get(m,"Sides")}).ToArray(),
            Static=statics.Select(c=>new {FeatureId=Get(c,"FeatureId"),ToolId=Get(c,"ToolId"),Punch=pair(Get(c,"PunchResult")),Holder=pair(Get(c,"HolderResult"))}).ToArray(),
            Process=processes.Select(c=>new {FeatureId=Get(c,"FeatureId"),ToolId=Get(c,"ToolId"),Status=Convert.ToString(Get(c,"ProcessResult")),
                Pairs=Items(Get(c,"PairSegments")).OrderBy(p=>Convert.ToString(Get(p,"Phase")),StringComparer.Ordinal).ThenBy(p=>Convert.ToString(Get(p,"Pair")),StringComparer.Ordinal).ThenBy(p=>Convert.ToString(Get(p,"SegmentId")),StringComparer.Ordinal)
                    .Select(p=>new {Pair=Convert.ToString(Get(p,"Pair")),Phase=Convert.ToString(Get(p,"Phase")),SegmentId=Get(p,"SegmentId"),
                    Status=Convert.ToString(Get(p,"Status")),Reasons=reasons(p),Witness=Get(p,"Witness"),ToleranceMm=Get(p,"ToleranceMm")}).ToArray()}).ToArray(),
            Planning=new {ManufacturingStatus=Get(planning,"ManufacturingStatus"),WinnerToolId=Get(planning,"WinnerToolId"),CommonToolIds=Get(planning,"CommonToolIds"),
                Tools=Items(Get(planning,"Tools")).OrderBy(t=>Convert.ToString(Get(t,"ToolId")),StringComparer.Ordinal).Select(t=>new {ToolId=Get(t,"ToolId"),Decision=Convert.ToString(Get(t,"Decision")),Reasons=reasons(t),Sequences=Get(t,"Sequences")}).ToArray()}};
    }
    private static ModelDoc2 MatrixSheet(ISldWorks app,string template,string path,MatrixCase item)
    {
        var doc=(ModelDoc2)app.NewDocument(template,0,0,0);Check(doc!=null,"New native sheet-metal fixture");
        Check(Plane(doc).Select2(false,0),"Select private native fixture plane");doc.SketchManager.InsertSketch(true);
        foreach(var pair in item.Section.SketchVerticesMm.Zip(item.Section.SketchVerticesMm.Skip(1),(a,b)=>new[]{a,b}))
            Check(doc.SketchManager.CreateLine(pair[0][0]/1000,pair[0][1]/1000,0,pair[1][0]/1000,pair[1][1]/1000,0)!=null,"Create explicit source-interpreted native sketch segment");
        doc.SketchManager.InsertSketch(true);Feature profile=null;
        for(var f=(Feature)doc.FirstFeature();f!=null;f=(Feature)f.GetNextFeature())if(f.GetTypeName2()=="ProfileFeature")profile=f;
        Check(profile!=null&&profile.Select2(false,0),"Select actual generated profile");
        Check(doc.FeatureManager.InsertSheetMetalBaseFlange2(item.ThicknessMm/1000,false,item.Section.InsideRadiusMm/1000,item.AxialLengthMm/1000,0,false,0,0,1,null,false,2,.0001,.0001,.5,true,false,true,true)!=null,"Native base flange succeeds");
        doc.ConfigurationManager.ActiveConfiguration.Name="Default";
        ((PartDoc)doc).SetMaterialPropertyName2("Default",item.Material.Database,item.Material.Name);
        string database;Check(((PartDoc)doc).GetMaterialPropertyName2("Default",out database)==item.Material.Name,"Actual native material assignment read back");
        doc.ForceRebuild3(false);Check(doc.Extension.NeedsRebuild2==0,"Private sheet rebuild complete");SaveAs(doc,path);return doc;
    }
    private static Dictionary<string,object> MatrixNativeGeometry(ModelDoc2 doc,MatrixCase item)
    {
        var bodies=((PartDoc)doc).GetBodies2(0,false) as object[]??new object[0];Check(bodies.Length==1,"One native solid body");var body=(Body2)bodies[0];
        var bounds=new double[6];for(int k=0;k<3;k++)foreach(int sign in new[]{-1,1})
        {
            var d=new double[3];d[k]=sign;double x,y,z;
            Check(body.GetExtremePoint(d[0],d[1],d[2],out x,out y,out z),"Exact B-rep directional extreme");bounds[k+(sign>0?3:0)]=new[]{x,y,z}[k]*1000;
        }
        double w=bounds[3]-bounds[0],l=bounds[4]-bounds[1],length=bounds[5]-bounds[2];
        var features=new Dictionary<int,Feature>();Action<Feature> walk=null;walk=f=>{if(features.ContainsKey(f.GetID()))return;features.Add(f.GetID(),f);for(var c=f.GetFirstSubFeature() as Feature;c!=null;c=c.GetNextSubFeature() as Feature)walk(c);};
        for(var f=doc.FirstFeature() as Feature;f!=null;f=f.GetNextFeature() as Feature)walk(f);
        var thickness=features.Values.Where(f=>f.GetTypeName2()=="SheetMetal").Select(f=>f.GetDefinition() as ISheetMetalFeatureData).Where(d=>d!=null).Select(d=>d.Thickness*1000).Distinct().ToArray();
        var ids=new HashSet<int>();var lengths=new List<double>();
        foreach(Face2 face in body.GetFaces() as object[]??new object[0])
        {
            var surface=face.GetSurface() as Surface;if(surface==null||!surface.IsCylinder())continue;
            var owner=face.GetFeature() as Feature;Check(owner!=null,"Native cylindrical face has a real feature owner");
            var definition=owner.GetDefinition();if(!(definition is IOneBendFeatureData)&&!(definition is ISketchedBendFeatureData)&&!(definition is IEdgeFlangeFeatureData))continue;
            ids.Add(owner.GetID());var cylinder=(double[])surface.CylinderParams;
            var one=definition as IOneBendFeatureData;var sketch=definition as ISketchedBendFeatureData;var flange=definition as IEdgeFlangeFeatureData;
            double angle=one!=null?one.BendAngle:sketch!=null?sketch.BendAngle:flange.BendAngle;
            double radius=one!=null?one.BendRadius:sketch!=null?sketch.BendRadius:flange.BendRadius;
            Check(Math.Abs(Math.Abs(angle)-Math.PI/2)<1e-8&&Math.Abs(radius*1000-item.Section.InsideRadiusMm)<=MatrixInput.MeasurementToleranceMm,"Independent native bend angle and inside R match fixture intent");
            Check(Math.Abs(cylinder[3])<1e-9&&Math.Abs(cylinder[4])<1e-9&&Math.Abs(Math.Abs(cylinder[5])-1)<1e-9,"Generated bend axis is template Z; never infer from largest bounding extent");
            var vertices=(face.GetEdges() as object[]??new object[0]).Cast<Edge>().SelectMany(e=>new[]{e.GetStartVertex() as Vertex,e.GetEndVertex() as Vertex}).Where(v=>v!=null).Select(v=>(double[])v.GetPoint()).ToArray();
            Check(vertices.Length>0,"Finite native bend-face vertices available");lengths.Add((vertices.Max(v=>v[2])-vertices.Min(v=>v[2]))*1000);
        }
        var audit=new Dictionary<string,object> {{"W",w},{"L",l},{"AxialLengthMm",length},{"BoundsMm",bounds},{"ThicknessesMm",thickness},
            {"FeatureIds",ids.OrderBy(id=>id).ToArray()},{"BendFaceAxialLengthsMm",lengths}};
        foreach(int id in ids){int error;string persistent=Convert.ToBase64String((byte[])doc.Extension.GetPersistReference3(features[id]));Check(doc.Extension.GetObjectByPersistReference3(Convert.FromBase64String(persistent),out error)!=null&&error==0,"Actual native bend persistent identity resolves");}
        return audit;
    }
    private static void MatrixAuditGeometry(Dictionary<string,object> audit,MatrixCase item)
    {
        // Report measured facts even on a failed assertion. Never silently
        // rescale or retry the input sketch until an assertion passes.
        Action<bool,string> exact=(ok,label)=>Check(ok,label+"; observed "+Json.Serialize(audit));
        exact(Math.Abs((double)audit["W"]-item.W)<=MatrixInput.MeasurementToleranceMm,"Actual outer W matches CSV");
        exact(Math.Abs((double)audit["L"]-item.L)<=MatrixInput.MeasurementToleranceMm,"Actual outer L matches CSV");
        exact(Math.Abs((double)audit["AxialLengthMm"]-item.AxialLengthMm)<=MatrixInput.MeasurementToleranceMm&&
            ((List<double>)audit["BendFaceAxialLengthsMm"]).All(n=>Math.Abs(n-item.AxialLengthMm)<=MatrixInput.MeasurementToleranceMm),"Actual axial bend lengths match CSV");
        var thickness=(double[])audit["ThicknessesMm"];
        exact(thickness.Length==1&&Math.Abs(thickness[0]-item.ThicknessMm)<=MatrixInput.MeasurementToleranceMm,"Native thickness matches CSV");
        exact(((int[])audit["FeatureIds"]).Length==item.ExpectedBends,"Physical native bend owner count matches source");
    }
}
