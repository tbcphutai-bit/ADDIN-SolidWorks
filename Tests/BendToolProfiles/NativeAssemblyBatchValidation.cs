using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Owns a NEW API process and private generated fixtures. Arguments contain all
// local CAD paths; no company path/coordinate/hash is embedded or exported.
internal static partial class NativeAssemblyBatchValidation
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=int.MaxValue};
    private static Assembly product;
    private static int assertions;
    private static string captureDiagnosticsDirectory;
    private static object Get(object x,string name) { var f=x.GetType().GetField(name,All);return f!=null?f.GetValue(x):x.GetType().GetProperty(name,All).GetValue(x,null); }
    private static object Call(object x,string name,params object[] args)
    {try{return x.GetType().GetMethods(All).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(x,args);}catch(TargetInvocationException ex){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException??ex).Throw();throw;}}
    private static Type T(string name)=>product.GetType("ADDIN.Commands."+name,true);
    private static object New(string name,params object[] args)=>Activator.CreateInstance(T(name),All,null,args,null);
    private static object[] Items(object x)=>((IEnumerable)x).Cast<object>().ToArray();
    private static void Check(bool valid,string reason) {assertions++;if(!valid)throw new Exception(reason);}
    private static void Save(ModelDoc2 doc) {int e=0,w=0;Check(doc.Save3(1,ref e,ref w),"Save private fixture "+e);}
    private static void SaveAs(ModelDoc2 doc,string path) {int e=0,w=0;Check(doc.SaveAs4(path,0,1,ref e,ref w),"SaveAs private fixture "+e);}
    private static void Activate(ISldWorks app,ModelDoc2 doc) {int e=0;app.ActivateDoc3(doc.GetTitle(),false,0,ref e);Check(app.IsSame(app.ActiveDoc,doc)==1,"Activate owned fixture "+e);}
    private static string Sha(string path) {using(var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s));}
    private static object State(ModelDoc2 doc)=>new {Dirty=doc.GetSaveFlag(),Stamp=doc.GetUpdateStamp(),Config=doc.ConfigurationManager.ActiveConfiguration.Name};
    private static ModelDoc2 Open(ISldWorks app,string path,int type=1) {int e=0,w=0;var m=app.OpenDoc6(path,type,1,"",ref e,ref w) as ModelDoc2;Check(m!=null,"Open private fixture "+e);return m;}
    private static object Session(ISldWorks app,ModelDoc2 host)=>New("BendCheckSession",app,host,null);
    private static object Source(string excel,string output)
    {var settings=New("BendToolStorageSettings",Path.Combine(output,"settings.json"));return New("BendToolCatalogReadResult",Call(settings,"SavePath",excel,CancellationToken.None),null);}
    private static object Options(int workers=2,bool cache=true,bool dedup=true)=>New("BendAssembly.AssemblyBatchOptions",workers,300000,1000000,cache,dedup,null,null);
    private static object Run(object session,object source,bool selected,object options,out double wall,out double sta)
    {
        var clock=Stopwatch.StartNew();var op=Call(session,"BeginAssemblyBatch",Enum.Parse(T("BendAssembly.AssemblyCheckScope"),selected?"SelectedComponents":"EntireAssembly"),source,options,CancellationToken.None,null,null);
        Console.WriteLine("INVENTORY "+Json.Serialize(new {Items=Items(Get(session,"Items")).Length,
            Reasons=Items(Get(session,"InventoryOccurrences")).GroupBy(o=>Get(o,"Kind")+":"+Get(o,"Reason")).Select(g=>new {Reason=g.Key,Count=g.Count()})}));
        var task=(Task)Get(op,"Completion");int turns=0,lastIndex=-1;
        while(!task.IsCompleted && clock.ElapsedMilliseconds<600000)
        {
            int index=(int)Get(op,"index");
            if(captureDiagnosticsDirectory!=null && index!=lastIndex)
            {
                var pending=Items(Get(session,"Items"));
                File.AppendAllText(Path.Combine(captureDiagnosticsDirectory,"PRIVATE-capture-progress.jsonl"),Json.Serialize(new {
                    Stage="before-pump",Index=index,Total=pending.Length,ElapsedMilliseconds=clock.Elapsed.TotalMilliseconds,
                    Source=index<pending.Length?Path.GetFileName(Convert.ToString(Get(pending[index],"Path"))):null,
                    Configuration=index<pending.Length?Get(pending[index],"Configuration"):null})+System.Environment.NewLine);
                lastIndex=index;
            }
            Call(op,"Pump",1);turns++;
            int after=(int)Get(op,"index");
            if(captureDiagnosticsDirectory!=null && after!=index)
            {
                var item=Items(Get(session,"Items"))[after-1];
                File.AppendAllText(Path.Combine(captureDiagnosticsDirectory,"PRIVATE-capture-progress.jsonl"),Json.Serialize(new {
                    Stage="after-capture",Index=after,Captures=Get(op,"captures"),Hits=Get(op,"hits"),ElapsedMilliseconds=clock.Elapsed.TotalMilliseconds,
                    Error=Get(item,"Error"),PlacementError=Get(item,"PlacementDiagnosticError"),HasNativeCapture=Get(item,"PlacementCapture")!=null})+System.Environment.NewLine);
            }
            Application.DoEvents();Thread.Sleep(1);
        }
        Check(task.IsCompleted,"Native batch timeout: "+Get(op,"Diagnostic"));task.GetAwaiter().GetResult();var result=Get(op,"Result");
        wall=clock.Elapsed.TotalMilliseconds;sta=(double)Get(op,"MaximumStaCaptureMilliseconds");
        if(!(bool)Get(result,"Current"))
        {
            var changes=new List<object>();var observed=Get(op,"revisions") as IDictionary;
            if(observed!=null)foreach(DictionaryEntry entry in observed)
            {
                string path=(string)entry.Key;bool exists=File.Exists(path);
                bool same=exists&&string.Equals(Sha(path).Replace("-",""),Convert.ToString(entry.Value),StringComparison.OrdinalIgnoreCase);
                if(!same)changes.Add(new {Name=Path.GetFileName(path),Exists=exists,Changed=true});
            }
            Console.WriteLine("NATIVE_FRESHNESS_DIAGNOSTIC "+Json.Serialize(changes));
            Console.WriteLine("NATIVE_INCOMPLETE_CAPTURE "+Json.Serialize(new {Index=Get(op,"index"),Captures=Get(op,"captures"),Hits=Get(op,"hits"),
                CaptureMilliseconds=Get(result,"CaptureMilliseconds"),MaximumStaCaptureMilliseconds=Get(op,"MaximumStaCaptureMilliseconds"),Diagnostic=Get(op,"Diagnostic")}));
        }
        Check((bool)Get(result,"Current"),"Native batch stale: "+Get(result,"InvalidReason")+" / "+Get(op,"Diagnostic"));
        Check(turns>0,"STA capture progresses via message-loop slices");return result;
    }
    private static string Rows(IEnumerable<object> parts)
    {
        var rows=new List<string>();
        foreach(var part in parts)
        {
            string statics=string.Join("|",Items(Get(part,"StaticCandidates")).Select(r=>Get(r,"ToolId")+":"+Get(r,"FeatureId")+":"+Pair(Get(r,"PunchResult"))+":"+Pair(Get(r,"HolderResult"))));
            string processes=string.Join("|",Items(Get(part,"ProcessCandidates")).Select(r=>Get(r,"ToolId")+":"+Get(r,"FeatureId")+":"+
                string.Join(";",Items(Get(r,"PairSegments")).Select(q=>Get(q,"Pair")+":"+Get(q,"Phase")+":"+Get(q,"Status")+":"+string.Join(",",Items(Get(q,"Reasons")))+":"+(Get(q,"Witness")==null?"-":"WITNESS")))));
            foreach(var occurrence in Items(Get(part,"Occurrences")))rows.Add(Get(occurrence,"Id")+":"+statics+"/"+processes);
        }
        return string.Join("\n",rows.OrderBy(s=>s,StringComparer.Ordinal));
    }
    private static string Pair(object p)=>Get(p,"Status")+":"+string.Join(",",Items(Get(p,"Reasons")))+":"+Get(p,"CertifiedClearanceLowerMm")+":"+(Get(p,"Witness")==null?"-":"WITNESS");
    private static object Metrics(string name,object r,double wall,double sta)=>new {Name=name,WallMs=wall,MaximumStaCaptureMs=sta,
        Components=Get(r,"LeafOccurrences"),UniqueParts=Get(r,"UniqueSourceConfigurations"),Jobs=Get(r,"AnalysisJobs"),Captures=Get(r,"Captures"),CaptureHits=Get(r,"CaptureHits"),
        InventoryMs=Get(r,"InventoryMilliseconds"),CaptureMs=Get(r,"CaptureMilliseconds"),EnvelopeMs=Get(r,"EnvelopeMilliseconds"),
        StaticMs=Get(r,"StaticMilliseconds"),ProcessMs=Get(r,"ProcessMilliseconds"),KernelMs=Get(r,"NumericKernelMilliseconds"),Queries=Get(r,"KernelQueries"),
        PrecheckMs=Get(r,"PrecheckMilliseconds"),PrecheckOnlyJobs=Get(r,"FastPrecheckOnlyJobs"),ResolveCount=0,ResolveMilliseconds=0,
        CatalogHits=Get(r,"CatalogHits"),CatalogMisses=Get(r,"CatalogMisses"),
        GeometryHits=Get(r,"GeometryHits"),GeometryMisses=Get(r,"GeometryMisses"),StaticHits=Get(r,"StaticHits"),StaticMisses=Get(r,"StaticMisses"),
        ProcessHits=Get(r,"ProcessHits"),ProcessMisses=Get(r,"ProcessMisses"),CacheBytes=Get(r,"CacheBytes"),PeakManagedBytes=Get(r,"PeakManagedBytes"),PeakWorkers=Get(r,"PeakWorkers")};

    [STAThread] private static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="--shutdown-audit") return ShutdownAuditMain(args.Skip(1).ToArray());
        if(args.Length>0 && args[0]=="--inprocess-addin") return InprocessAddinMain(args.Skip(1).ToArray());
        if(args.Length==1 && args[0]=="--representative-input-tests") return CheckChan.NativeAcceptance.RepresentativeAcceptanceInput.Tests();
        if(args.Length==1 && args[0]=="--acceptance-event-tests") return CheckChan.NativeAcceptance.AcceptanceEventFile.Tests();
        if(args.Length==1 && args[0]=="--com-control-tests") return CheckChan.NativeAcceptance.RepresentativeAcceptanceInput.ComControlTests();
        if(args.Length==3 && args[0]=="--prior-xl02-gate-tests") return CheckChan.NativeAcceptance.RepresentativeAcceptanceInput.GateTests(args[1],args[2]);
        if(args.Length>0 && args[0]=="--excel-existing") return ExcelMatrixMain(args.Skip(1).ToArray(),true);
        if(args.Length>0 && args[0]=="--excel-matrix") return ExcelMatrixMain(args.Skip(1).ToArray());
        if(args.Length>0 && args[0]=="--create-seed") return SeedMain(args.Skip(1).ToArray());
        if(args.Length>0 && args[0]=="--part-ui") return UnifiedPartMain(args.Skip(1).ToArray());
        if(args.Length>0 && args[0]=="--lifecycle") return LifecycleMain(args.Skip(1).ToArray());
        bool ui=args.Length>0&&args[0]=="--ui";
        int code=ui?UiMain(args.Skip(1).ToArray()):AssemblyMain(args);
        int? exit=NativeTestProcess.WaitForOwnedExit(10000);
        string folder=ui?(args.Length>2?args[2]:null):(args.Length>1?args[1]:null);
        if(folder!=null&&Directory.Exists(folder))File.WriteAllText(Path.Combine(folder,"native-server-shutdown.json"),Json.Serialize(new{
            FunctionalExitCode=code,ShutdownExitCode=exit,ShutdownResult=exit==0?"PASS":exit.HasValue?"FAIL":"NOT_CONFIRMED"}));
        return code!=0?code:exit==0?0:1;
    }
    private static int AssemblyMain(string[] args)
    {
        // dll, output(new), sheet-metal seed, seed length dimension, assembly
        // template, min workbook. Source files are only copied/read, never saved.
        if(args.Length!=6) {Console.WriteLine("dll output seed dimension assembly-template min-xlsx");return 2;}
        ISldWorks app=null;bool owned=false;var disposable=new List<IDisposable>();var report=new Dictionary<string,object>();
        var fixtureSources=new Dictionary<string,string>();
        try
        {
            product=Assembly.LoadFrom(args[0]);string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
            if(System.Environment.GetEnvironmentVariable("ADDIN_NATIVE_CAPTURE_DIAGNOSTICS")=="1")
            {
                captureDiagnosticsDirectory=output;
                int staThread=Thread.CurrentThread.ManagedThreadId;
                AppDomain.CurrentDomain.FirstChanceException+=(sender,eventArgs)=> {
                    if(Thread.CurrentThread.ManagedThreadId==staThread&&eventArgs.Exception is KeyNotFoundException)
                        File.AppendAllText(Path.Combine(output,"PRIVATE-capture-exceptions.txt"),eventArgs.Exception+System.Environment.NewLine);
                };
            }
            string seedHash=Sha(args[2]);
            app=NativeTestProcess.StartOwned(output);owned=true;Check(owned,"Own explicit native test process");NativeTestProcess.KeepOwnedBackgroundSession(app);
            File.WriteAllText(Path.Combine(output,"ownership.json"),Json.Serialize(new {Owned=owned,Pid=app.GetProcessID(),StartUtc=Process.GetProcessById(app.GetProcessID()).StartTime.ToUniversalTime().ToString("o")}));
            string rootPath=Path.Combine(output,"GENERATED-120.SLDASM");
            string existing=System.Environment.GetEnvironmentVariable("ADDIN_NATIVE_FIXTURE_SOURCE");
            if(!string.IsNullOrWhiteSpace(existing))
            {
                existing=Path.GetFullPath(existing);
                Check(!string.Equals(existing,output,StringComparison.OrdinalIgnoreCase)&&!File.Exists(rootPath),"Clone fixture into a new private output only");
                var parts=Directory.GetFiles(existing,"GENERATED-*.SLDPRT");
                var assemblies=new[]{"SUB-0.SLDASM","SUB-1.SLDASM","GENERATED-120.SLDASM"}.Select(n=>Path.Combine(existing,n)).ToArray();
                Check(parts.Length==23&&assemblies.All(File.Exists),"Reuse requires the complete real 120/25 fixture");
                foreach(var path in parts.Concat(assemblies)){fixtureSources.Add(path,Sha(path));File.Copy(path,Path.Combine(output,Path.GetFileName(path)),false);}
                // Closed-document replacement writes only the copied Assembly.
                // Native path assertions below reject an unresolved old link.
                foreach(var assemblyPath in assemblies)foreach(var sourceReference in parts.Concat(assemblies.Take(2)))
                    app.ReplaceReferencedDocument(Path.Combine(output,Path.GetFileName(assemblyPath)),sourceReference,Path.Combine(output,Path.GetFileName(sourceReference)));
                report["ReusedBaselineFixture"]=true;
            }
            if(!File.Exists(rootPath))
            {
            var keys=new List<Tuple<string,string>>();var docs=new List<ModelDoc2>();
            for(int i=0;i<23;i++)
            {
                string path=Path.Combine(output,"GENERATED-"+i.ToString("00")+".SLDPRT");File.Copy(args[2],path,false);File.SetAttributes(path,FileAttributes.Normal);
                var doc=Open(app,path);var d=doc.Parameter(args[3]) as Dimension;Check(d!=null,"Seed parameter exists");double start=d.SystemValue;
                d.SetSystemValue3(start+i*.0003,1,null);doc.ForceRebuild3(false);Save(doc);keys.Add(Tuple.Create(path,doc.ConfigurationManager.ActiveConfiguration.Name));
                if(i<2)
                {
                    Check(doc.AddConfiguration3("BATCH_ALT","Private fixture","",0)!=null,"Create alternate source configuration");doc.ShowConfiguration2("BATCH_ALT");
                    d=(Dimension)doc.Parameter(args[3]);d.SetSystemValue3(start+.009+i*.0003,1,null);doc.ForceRebuild3(false);Save(doc);
                    keys.Add(Tuple.Create(path,"BATCH_ALT"));doc.ShowConfiguration2("Default");doc.ForceRebuild3(false);Save(doc);
                }
                docs.Add(doc);
            }
            Check(keys.Count==25,"25 native source/config identities");var subPaths=new List<string>();
            for(int branch=0;branch<2;branch++)
            {
                var sub=app.NewDocument(args[4],0,0,0) as ModelDoc2;Check(sub!=null,"Create nested subassembly");var a=(AssemblyDoc)sub;
                int count=0;for(int i=0;i<25;i++)for(int j=0;j<(i<20?(branch==0?3:2):2);j++)
                {var c=a.AddComponent5(keys[i].Item1,0,"",true,keys[i].Item2,count*.02,branch*.1,0);Check(c!=null,"Insert private member");c.ReferencedConfiguration=keys[i].Item2;count++;}
                Check(count==(branch==0?70:50),"Nested occurrence distribution");sub.ForceRebuild3(false);string path=Path.Combine(output,"SUB-"+branch+".SLDASM");SaveAs(sub,path);subPaths.Add(path);
            }
            var fixtureRoot=app.NewDocument(args[4],0,0,0) as ModelDoc2;Check(fixtureRoot!=null,"Create batch Assembly");var fixtureAssembly=(AssemblyDoc)fixtureRoot;
            foreach(string path in subPaths)Check(fixtureAssembly.AddComponent5(path,0,"",true,"Default",0,0,0)!=null,"Insert nested branch");
            fixtureRoot.ForceRebuild3(false);SaveAs(fixtureRoot,rootPath);
            foreach(var doc in (app.GetDocuments() as object[]??new object[0]).OfType<ModelDoc2>().ToArray())app.CloseDoc(doc.GetTitle());
            }
            var root=Open(app,rootPath,2);var assembly=(AssemblyDoc)root;Activate(app,root);
            // The fixture explicitly starts resolved. Backend never requests bulk
            // resolution; any required setup belongs to fixture creation only.
            assembly.ResolveAllLightWeightComponents(false);
            Check((assembly.GetComponents(false) as object[]??new object[0]).OfType<Component2>().All(c=>
                Path.GetFullPath(c.GetPathName()).StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)),
                "All fixture references are inside the current private output before any fixture saves");
            foreach(var fixturePart in (app.GetDocuments() as object[]).OfType<ModelDoc2>().Where(d=>d.GetType()==1 && d.GetPathName().StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
            {
                if(fixturePart.Extension.ListExternalFileReferencesCount()>0)fixturePart.Extension.BreakAllExternalFileReferences2(true);
                fixturePart.ShowConfiguration2("Default");fixturePart.ForceRebuild3(false);Save(fixturePart);
            }
            root.ForceRebuild3(false);Save(root);Activate(app,root);
            foreach(var fixturePart in (app.GetDocuments() as object[]).OfType<ModelDoc2>().Where(d=>d.GetType()==1 && d.GetPathName().StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
                if(fixturePart.GetSaveFlag())Save(fixturePart);
            Save(root);
            var leaves=(assembly.GetComponents(false) as object[]??new object[0]).OfType<Component2>().Where(c=>string.Equals(Path.GetExtension(c.GetPathName()),".SLDPRT",StringComparison.OrdinalIgnoreCase)).ToArray();
            Check(leaves.Length==120,"Actual native fixture has 120 leaf occurrences");
            if(System.Environment.GetEnvironmentVariable("ADDIN_ASSEMBLY_EDGE_ONLY")=="1")
            {EdgeFixtures(app,root,args[4],output,disposable,report,Source(args[5],output));report["Result"]="EDGE_ONLY_PASS";report["Assertions"]=assertions;File.WriteAllText(Path.Combine(output,"assembly-edge-result.json"),Json.Serialize(report));Console.WriteLine(Json.Serialize(report));return 0;}
            var files=Directory.GetFiles(output,"*.SLDPRT").Concat(Directory.GetFiles(output,"*.SLDASM")).ToDictionary(p=>p,Sha);
            if(System.Environment.GetEnvironmentVariable("ADDIN_NATIVE_INVENTORY_ONLY")=="1")
            {
                report["Scope"]="NATIVE_INVENTORY_ONLY_NOT_FULL_BATCH";
                var inventorySession = Session(app, root); disposable.Add((IDisposable)inventorySession);
                Call(inventorySession,"BuildInventory",false);
                Check(Items(Get(inventorySession,"Items")).Length==25,"Inventory-only actual 25 source/config groups");
                Check(Items(Get(inventorySession,"InventoryOccurrences")).Count(o=>!(bool)Get(o,"IsAssemblyBranch"))==120,"Inventory-only actual 120 leaves");
                foreach(var original in fixtureSources)Check(Sha(original.Key)==original.Value,"Inventory-only fixture source unchanged");
                report["Scope"]="NATIVE_INVENTORY_ONLY_NOT_FULL_BATCH";report["Result"]="PASS";report["Assertions"]=assertions;
                File.WriteAllText(Path.Combine(output,"assembly-inventory-result.json"),Json.Serialize(report));Console.WriteLine(Json.Serialize(report));return 0;
            }
            var beforeDocs=(app.GetDocuments() as object[]).OfType<ModelDoc2>().Where(d=>files.ContainsKey(d.GetPathName())).ToDictionary(d=>d.GetPathName(),d=>Json.Serialize(State(d)));
            var source=Source(args[5],output);root.ClearSelection2(true);var session=Session(app,root);disposable.Add((IDisposable)session);
            object bodyInfo;var nativeBodies=leaves[0].GetBodies3(0,out bodyInfo) as object[];var firstModel=(ModelDoc2)leaves[0].GetModelDoc2();
            object refModels,refComponents,refFeatures,refTypes,refStatus,refEntities,refFeatCom,refConfigOptions,refConfigNames;
            firstModel.Extension.ListExternalFileReferences2(out refModels,out refComponents,out refFeatures,out refTypes,out refStatus,out refEntities,out refFeatCom,out refConfigOptions,out refConfigNames);
            Console.WriteLine("NATIVE_METADATA "+Json.Serialize(new {BodyCount=nativeBodies?.Length,Info=bodyInfo,Sheet=(nativeBodies?.FirstOrDefault() as Body2)?.IsSheetMetal(),
                Suppression=leaves[0].GetSuppression2(),Solving=leaves[0].Solving,Virtual=leaves[0].IsVirtual,Mirrored=leaves[0].IsMirrored(),External=firstModel.Extension.ListExternalFileReferencesCount(),ReferenceTypes=refTypes,ReferenceStatus=refStatus,Dirty=firstModel.GetSaveFlag(),Transform=(double[])leaves[0].GetTotalTransform(false).ArrayData}));
            if(System.Environment.GetEnvironmentVariable("ADDIN_ASSEMBLY_INVENTORY_ONLY")=="1")
            {Call(session,"BuildInventory",false);Console.WriteLine("INVENTORY_ONLY "+Json.Serialize(Items(Get(session,"InventoryOccurrences")).GroupBy(o=>Get(o,"Kind")+":"+Get(o,"Reason")).Select(g=>new {Reason=g.Key,Count=g.Count()})));return 0;}
            double wall,sta;var cold=Run(session,source,false,Options(4),out wall,out sta);var metrics=new List<object>{Metrics("cold-all",cold,wall,sta)};report["Performance"]=metrics;
            CheckCompanyCadPairs(cold);
            var items=Items(Get(session,"Items"));Check(items.Length==25,"Inventory groups 120 members into 25 source/config captures");
            report["CaptureInventory"]=items.Select(i=>new {File=Path.GetFileName(Convert.ToString(Get(i,"Path"))),
                Configuration=Get(i,"Configuration"),Done=Get(i,"Done"),Error=Get(i,"Error"),Reason=Get(i,"AssemblyBatchReason"),
                Reads=Get(i,"Context")==null?(object)null:Get(Get(i,"Context"),"GeometryReadCount")}).ToArray();
            report["BatchPartReasons"]=Get(cold,"ReasonCounts");
            Check((int)Get(cold,"Captures")==25&&items.All(i=>(int)Get(Get(i,"Context"),"GeometryReadCount")==1),"Exactly one real Min geometry read per unique source");
            Check(items.All(i=>Get(i,"Error")==null),"Every eligible native Part captured successfully");
            Check((int)Get(cold,"KernelQueries")==0,"Unverified company process is UNKNOWN, not numeric benchmark");
            Check((int)Get(session,"SelectiveResolveCount")==0,"Read-only native check performs zero resolves");
            string expected=Rows(Items(Get(cold,"Parts")));int append=(int)T("BendEnvelope.SheetNativeTopologyCapture").GetProperty("BodyCaptureCount",All).GetValue(null,null);
            var warm=Run(session,source,false,Options(4),out wall,out sta);metrics.Add(Metrics("warm-all",warm,wall,sta));
            Check((int)Get(warm,"Captures")==0&&(int)Get(warm,"CaptureHits")==25,"Warm native check reuses captures");
            Check(append==(int)T("BendEnvelope.SheetNativeTopologyCapture").GetProperty("BodyCaptureCount",All).GetValue(null,null),"Warm batch does not append B-rep again");
            Check(expected==Rows(Items(Get(warm,"Parts"))),"Cold/warm native statuses and exact reasons equivalent");
            // Independent per-instance legacy engines, using the same proven
            // native local geometry. This isolates collision/backend optimization
            // from the pre-existing native grouping optimization.
            var reference=new List<object>();var clock=Stopwatch.StartNew();
            foreach(var item in items)
            {
                var model=Get(item,"PlacementCapture");var library=Get(session,"ToolLibrary");
                foreach(var occurrence in Items(Get(item,"AssemblyOccurrences")))
                {
                    var staticRequest=New("BendEnvelope.CollisionBatchRequest",model,library,null,null);
                    var staticResult=CallStatic("BendEnvelope.CollisionBatchWorker","RunSynchronously",staticRequest,New("BendEnvelope.CollisionBatchOptions",1,120000,1000000,null),CancellationToken.None,null);
                    var processRequest=New("BendProcess.ProcessBatchRequest",model,library,null,null);
                    var processResult=CallStatic("BendProcess.ProcessBatchWorker","RunSynchronously",processRequest,New("BendProcess.ProcessBatchOptions",1,120000,1000000,null),CancellationToken.None,null);
                    var occurrenceArray=Array.CreateInstance(T("BendAssembly.AssemblyOccurrence"),1);occurrenceArray.SetValue(occurrence,0);
                    reference.Add(New("BendAssembly.AssemblyPartResult","REFERENCE",occurrenceArray,null,Get(staticResult,"Candidates"),Get(processResult,"Candidates"),Items(Get(model,"Bends")).Length,false,(Func<string>)(()=>null)));
                }
            }
            report["IndependentPerInstanceWorkerMilliseconds"]=clock.Elapsed.TotalMilliseconds;
            Check(expected==Rows(reference),"All 120 instances equal independent single-Part static + process engines");
            var allSnapshot=warm;root.ClearSelection2(true);foreach(var c in leaves.Take(10))Check(c.Select4(true,null,false),"Select fixture component");
            var selected=Session(app,root);disposable.Add((IDisposable)selected);var subset=Run(selected,source,true,Options(2),out wall,out sta);metrics.Add(Metrics("selected-10",subset,wall,sta));
            Check((int)Get(subset,"LeafOccurrences")==10&&Items(Get(subset,"Parts")).Sum(p=>(int)Get(p,"Quantity"))==10,"Selected scope keeps exactly 10 leaf instances");
            root.ClearSelection2(true);var parents=(assembly.GetComponents(true) as object[]).OfType<Component2>().ToArray();Check(parents[0].Select4(false,null,false),"Select parent subassembly");
            var parentSession=Session(app,root);disposable.Add((IDisposable)parentSession);var nested=Run(parentSession,source,true,Options(2),out wall,out sta);
            Check(new[]{50,70}.Contains((int)Get(nested,"LeafOccurrences")),"Selected subassembly recursively includes its descendants once");
            foreach(var p in files)Check(Sha(p.Key)==p.Value,"Private fixture bytes unchanged by check");
            foreach(var doc in (app.GetDocuments() as object[]).OfType<ModelDoc2>().Where(d=>beforeDocs.ContainsKey(d.GetPathName())))
                Check(Json.Serialize(State(doc))==beforeDocs[doc.GetPathName()],"Source dirty/stamp/config unchanged by backend");
            Check(Sha(args[2])==seedHash,"Original seed bytes unchanged");
            Check((int)T("BendEnvelope.SheetNativeTopologyCapture").GetProperty("WrongThreadCount",All).GetValue(null,null)==0,"No native capture on worker");
            if(System.Environment.GetEnvironmentVariable("ADDIN_NATIVE_UI_120")=="1")
                ExerciseLargeNativeUi(app,root,report,output);
            report["CoreReadOnlyResult"]="PASS";report["CoreAssertions"]=assertions;
            File.WriteAllText(Path.Combine(output,"assembly-core-result.json"),Json.Serialize(report));
            using(var cancel=new CancellationTokenSource())
            {
                var operation=Call(session,"BeginAssemblyBatch",Enum.Parse(T("BendAssembly.AssemblyCheckScope"),"EntireAssembly"),source,Options(4),cancel.Token,null,null);
                cancel.Cancel();var canceled=FinishOperation(operation);
                Check(!(bool)Get(canceled,"Current")&&Items(Get(canceled,"Parts")).All(p=>Items(Get(p,"ProcessCandidates")).Length==18*(int)Get(p,"BendCount")),"Native cancellation preserves known slots without current success");
            }
            // Restart after cancellation revalidates native inputs but does not
            // repeat still-current Min/COM geometry just to change generation.
            var restarted=Run(session,source,false,Options(2),out wall,out sta);
            Check((int)Get(restarted,"Captures")==0&&(int)Get(restarted,"CaptureHits")==25,"Restart retains only revalidated native geometry");
            string privateWorkbook=Path.Combine(output,"PRIVATE-stale-workbook.xlsx");File.Copy(args[5],privateWorkbook,true);var staleSource=Source(privateWorkbook,output);
            var staleOperation=Call(session,"BeginAssemblyBatch",Enum.Parse(T("BendAssembly.AssemblyCheckScope"),"EntireAssembly"),staleSource,Options(2),CancellationToken.None,null,null);
            var captureWait=Stopwatch.StartNew();while((int)Get(staleOperation,"index")==0&&captureWait.ElapsedMilliseconds<30000){Call(staleOperation,"Pump",1);Application.DoEvents();Thread.Sleep(1);}
            Check((int)Get(staleOperation,"index")>0,"Source revision observed before private mutation");
            using(var changed=new FileStream(privateWorkbook,FileMode.Append,FileAccess.Write,FileShare.Read))changed.WriteByte(13);
            var stale=FinishOperation(staleOperation);Check(!(bool)Get(stale,"Current"),"Workbook byte change invalidates batch before publication");
            foreach(var item in Items(Get(session,"Items")))if(Get(item,"Context")!=null)Call(Get(item,"Context"),"SetSource",source);
            report["NativeLifecycle"]=new {Cancellation=true,RestartCaptureHits=Get(restarted,"CaptureHits"),ChangedWorkbookBeforePublication=true};
            // Explicit mutable-fixture edge tests occur AFTER read-only proof.
            var edge=leaves.First();int oldState=edge.GetSuppression2(),stamp=root.GetUpdateStamp();bool dirty=root.GetSaveFlag();
            int set=edge.SetSuppression2(1),light=edge.GetSuppression2();root.ClearSelection2(true);edge.Select4(false,null,false);
            var lw=Session(app,root);disposable.Add((IDisposable)lw);Call(lw,"BuildInventory",true);
            var lwRows=Items(Get(lw,"InventoryOccurrences"));Check(lwRows.Length==1&&Get(lwRows[0],"Kind").ToString()=="Lightweight","Actual lightweight metadata without GetModelDoc fallback");
            Check(edge.GetSuppression2()==light&&(int)Get(lw,"SelectiveResolveCount")==0,"Read-only LW scan preserves state");
            var lwBatch=Run(lw,source,true,Options(2),out wall,out sta);
            Check((int)Get(lwBatch,"Captures")==0&&Items(Get(lwBatch,"Parts")).Length==1&&
                Get(Items(Get(lwBatch,"Parts"))[0],"GeometricStatus").ToString()=="UNKNOWN"&&edge.GetSuppression2()==light,
                "Actual selected lightweight batch stays UNKNOWN without capture or automatic resolution");
            int restore=edge.SetSuppression2(oldState);report["LightweightProbe"]=new {SetReturn=set,Observed=light,RestoreReturn=restore,Restored=edge.GetSuppression2()==oldState,
                StampBefore=stamp,StampAfter=root.GetUpdateStamp(),DirtyBefore=dirty,DirtyAfter=root.GetSaveFlag(),BackendResolveCount=0};
            edge.SetSuppression2(0);root.ClearSelection2(true);edge.Select4(false,null,false);var suppressed=Session(app,root);disposable.Add((IDisposable)suppressed);Call(suppressed,"BuildInventory",true);
            Check((int)Get(suppressed,"SuppressedCount")==1,"Suppressed selected component accounted without body traversal");
            edge.SetSuppression2(oldState);
            EdgeFixtures(app,root,args[4],output,disposable,report,source);
            Check(fixtureSources.All(p=>Sha(p.Key)==p.Value),"Baseline fixture source bytes unchanged");
            report["BaselineFixtureSourceUnchanged"]=true;
            report["Assertions"]=assertions;report["Result"]="PASS";report["CompanyNumericBenchmark"]=false;
            File.WriteAllText(Path.Combine(output,"assembly-batch-result.json"),Json.Serialize(report));Console.WriteLine(Json.Serialize(report));return 0;
        }
        catch(Exception ex){report["Result"]="FAIL";report["Assertions"]=assertions;report["Error"]=ex.ToString();if(args.Length>1)File.WriteAllText(Path.Combine(args[1],System.Environment.GetEnvironmentVariable("ADDIN_ASSEMBLY_EDGE_ONLY")=="1"?"assembly-edge-result.json":"assembly-batch-result.json"),Json.Serialize(report));Console.WriteLine(ex);return 1;}
        finally {foreach(var d in disposable)d.Dispose();if(app!=null&&owned){try{NativeTestProcess.RestoreCommandScope(app);app.CloseAllDocuments(true);app.ExitApp();}catch{}}}
    }
    private static object CallStatic(string type,string method,params object[] args)
    {try{return T(type).GetMethods(All).Single(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(null,args);}catch(TargetInvocationException ex){throw ex.InnerException??ex;}}
    private static object FinishOperation(object operation)
    {
        var task=(Task)Get(operation,"Completion");var clock=Stopwatch.StartNew();
        while(!task.IsCompleted&&clock.ElapsedMilliseconds<120000){Call(operation,"Pump",1);Application.DoEvents();Thread.Sleep(1);}
        Check(task.IsCompleted,"Lifecycle test operation completed");task.GetAwaiter().GetResult();return Get(operation,"Result");
    }
}
