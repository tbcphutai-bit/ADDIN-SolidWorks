using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

internal static partial class NativeAssemblyBatchValidation
{
    private static Control UiControl(Form form,string name) => form.Controls.Find(name,true).Single();
    private static void UiClick(Form form,string name) => ((Button)UiControl(form,name)).PerformClick();
    private static void UiSpin(Func<bool> done)
    {
        var clock=Stopwatch.StartNew();
        while(!done()&&clock.ElapsedMilliseconds<300000){NativeTestProcess.PumpMessages();Thread.Sleep(5);}
        Check(done(),"UI timer/await completion timed out");
    }
    private static object[] UiParts(Form form) => ((DataGridView)UiControl(form,"dgvParts")).Rows.Cast<DataGridViewRow>().Select(r=>r.Tag).ToArray();
    private static void UiCheckBindings(Form form,object snapshot)
    {
        CheckCompanyCadPairs(snapshot);
        var actual=UiParts(form);var expected=Items(Get(snapshot,"Parts"));
        Check(actual.Length==expected.Length,"UI has exactly the current backend analysis rows");
        foreach(var part in actual)
        {
            var raw=Get(part,"RawPartResult");int count=(int)Get(raw,"BendCount");
            var bends=Items(Get(part,"Bends"));
            var ids=Items(Get(raw,"StaticCandidates")).Concat(Items(Get(raw,"ProcessCandidates")))
                .Select(c=>(int)Get(c,"FeatureId")).Distinct().OrderBy(x=>x).ToArray();
            Check(bends.Select(b=>(int)Get(b,"FeatureId")).SequenceEqual(count==0?new int[0]:ids),"UI IDs match actual native backend FeatureIds");
            foreach(var bend in bends)
            {
                var metadata=Items(Get(raw,"BendMetadata")).SingleOrDefault(m=>(int)Get(m,"FeatureId")== (int)Get(bend,"FeatureId"));
                Check(Equals(Get(bend,"AngleDeg"),metadata==null?null:Get(metadata,"AngleDeg"))&&
                    Equals(Get(bend,"RadiusMm"),metadata==null?null:Get(metadata,"RadiusMm")),"UI dimensions match actual captured native metadata, never guessed");
                var tools=Items(Get(bend,"ToolCandidates"));Check(tools.Length==18,"18 live tool slots per native bend");
                if(System.Environment.GetEnvironmentVariable("ADDIN_BEND_USE_MASTER_PAIRS")=="1")
                    Check(tools.All(t=>Get(t,"CadAssembly")!=null&&Equals(Get(Get(t,"CadAssembly"),"Id"),Get(t,"ToolId"))&&
                        (bool)Get(Get(Get(t,"CadAssembly"),"CadPair"),"Matched")),"Live native candidates retain their fixed CAD pair and ToolId");
                Check(tools.All(t=>Get(t,"GeometricStatus").ToString()=="UNKNOWN"&&Get(t,"ProcessStatus").ToString()=="UNKNOWN"),
                    "No setup/process evidence means UNKNOWN, never CLEAR");
            }
        }
        // Exercise the actual visible Part -> Bend -> 18-tool selection path,
        // not only the detached converter objects.
        var grid=(DataGridView)UiControl(form,"dgvParts");
        foreach(DataGridViewRow row in grid.Rows)
        {
            grid.CurrentCell=row.Cells[0];row.Selected=true;
            var pills=UiControl(form,"pnlBendPills").Controls.OfType<Button>().ToArray();
            foreach(var pill in pills)
            {
                pill.PerformClick();var toolGrid=(DataGridView)UiControl(form,"dgvTools");
                Check(toolGrid.Rows.Count==18&&toolGrid.Rows.Cast<DataGridViewRow>().All(t=>(int)Get(t.Tag,"FeatureId")==
                    (int)Get(pill.Tag,"FeatureId")),"Visible bend grid binds all 18 candidates to its native FeatureId");
            }
            if(pills.Length==0)Check(((DataGridView)UiControl(form,"dgvTools")).Rows.Count==0,"Zero-bend visible Part has no fictional tools");
        }
        Check(UiControl(form,"lblDisclaimer").Text.Contains("CHƯA XÁC MINH"),"Live manufacturing disclaimer");
    }
    private static void CheckCompanyCadPairs(object snapshot)
    {
        if(System.Environment.GetEnvironmentVariable("ADDIN_BEND_USE_MASTER_PAIRS")!="1")return;
        var pairs=Items(Get(snapshot,"ToolPairs"));
        Check(pairs.Length==18&&pairs.All(p=>(bool)Get(p,"MappingIdentified")&&(bool)Get(Get(p,"CadPair"),"Matched")),"Native backend uses all 18 company CAD pairs");
        Check(pairs.All(p=>!(bool)Get(p,"ClampVerified")&&Get(Get(p,"SeatingEvidence"),"TipPointMm")==null),"Native CAD reference does not certify datum/seating");
        Check(pairs.Select(p=>Get(Get(p,"CadPair"),"PunchInsert")).Distinct().Count()==18&&pairs.Select(p=>Get(Get(p,"CadPair"),"HolderInsert")).Distinct().Count()==18,
            "Native source has 18 unique Punch and 18 unique Holder instances");
    }
    private static void ExerciseNativeUi(ISldWorks app,ModelDoc2 root,object source,string output,Component2 selected,
        Dictionary<string,object> report,string label)
    {
        Activate(app,root);root.ClearSelection2(true);
        string state=Json.Serialize(State(root));
        var form=(Form)Activator.CreateInstance(product.GetType("ADDIN.UI.BendAssemblyBatchForm",true),All,null,
            new object[]{root.GetTitle(),root.ConfigurationManager.ActiveConfiguration.Name},null);
        var settings=New("BendToolStorageSettings",Path.Combine(output,"ui-private-settings.json"));
        Call(settings,"SavePath",Get(Get(source,"Snapshot"),"SourcePath"),CancellationToken.None);
        var provider=New("BendToolCatalogProvider",settings);
        var backend=Activator.CreateInstance(product.GetType("ADDIN.UI.BendAssemblyBatchLiveBackend",true),All,null,new[]{app,root,provider},null);
        var controller=Activator.CreateInstance(product.GetType("ADDIN.UI.BendAssemblyBatchController",true),All,null,new[]{form,backend},null);
        try
        {
            form.Show();Application.DoEvents();Check(UiParts(form).Length==0&&!(bool)Get(form,"IsPreview"),"Native Form starts empty LIVE");
            UiClick(form,"btnToggleMockup");Check((bool)Get(form,"IsPreview"),"Preview explicit before actual run");
            UiClick(form,"btnRunBatch");Check(!(bool)Get(form,"IsPreview")&&UiParts(form).Length==0,"Native Run removes preview data immediately");
            UiSpin(()=>!(bool)Get(controller,"Running"));
            var cold=Get(controller,"displayed");Check(cold!=null&&(bool)Get(cold,"Current"),"Actual Form/Begin/Pump/Completion produced current snapshot: "+UiControl(form,"lblProgressTitle").Text);
            UiCheckBindings(form,cold);
            report["UiTiming-"+label+"-cold"]=UiTimings(controller);
            var counts=Items(Get(cold,"Parts")).Select(p=>(int)Get(p,"BendCount")).Distinct().OrderBy(x=>x).ToArray();
            report["UiInventory-"+label]=Items(Get(cold,"Parts")).Select(p=>new {BendCount=Get(p,"BendCount"),Reason=Get(p,"Reason")}).ToArray();
            report["UiNativeCapture-"+label]=Items(Get(Get(backend,"session"),"Items")).Select(i=>new {Done=Get(i,"Done"),Error=Get(i,"Error"),
                PlacementError=Get(i,"PlacementDiagnosticError"),NativeRows=Items(Get(i,"Rows")).Length}).ToArray();
            if(label=="zero-three-six")Check(counts.SequenceEqual(new[]{0,3,6}),"Actual Part inventories include 0,3,6 bends; observed "+string.Join(",",counts));
            Check(((DataGridView)UiControl(form,"dgvParts")).Columns[1].HeaderText=="Configuration","BOM styling cannot overwrite live schema");
            using(var bitmap=new Bitmap(form.Width,form.Height))
            {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"PRIVATE-live-"+label+".png"));}
            UiClick(form,"btnRunBatch");UiSpin(()=>!(bool)Get(controller,"Running"));var warm=Get(controller,"displayed");
            Console.WriteLine("UI_WARM "+Json.Serialize(new {Label=label,Current=warm!=null&&(bool)Get(warm,"Current"),
                Captures=warm==null?null:Get(warm,"Captures"),Hits=warm==null?null:Get(warm,"CaptureHits"),Message=UiControl(form,"lblProgressTitle").Text}));
            Check(warm!=null&&(bool)Get(warm,"Current"),"Actual UI warm run current: "+UiControl(form,"lblProgressTitle").Text);
            UiCheckBindings(form,warm);
            report["UiTiming-"+label+"-warm"]=UiTimings(controller);
            Check(!(bool)Get(cold,"Current"),"Superseded generation invalidated");
            root.ClearSelection2(true);Call(form,"SetScope",Enum.Parse(T("BendAssembly.AssemblyCheckScope"),"SelectedComponents"));
            UiClick(form,"btnRunBatch");UiSpin(()=>!(bool)Get(controller,"Running"));
            Check(Get(controller,"displayed")==null&&UiParts(form).Length==0&&UiControl(form,"lblProgressPercent").Text!="100%",
                "Empty Selected scope is rejected, never changed to Entire or success");
            root.ClearSelection2(true);Check(selected.Select4(false,null,false),"Select actual component for UI scope");
            Call(form,"SetScope",Enum.Parse(T("BendAssembly.AssemblyCheckScope"),"SelectedComponents"));
            UiClick(form,"btnRunBatch");UiSpin(()=>!(bool)Get(controller,"Running"));var subset=Get(controller,"displayed");
            Check(subset!=null&&(int)Get(subset,"LeafOccurrences")==1,"Actual UI Selected scope is one occurrence");UiCheckBindings(form,subset);
            using(var bitmap=new Bitmap(form.Width,form.Height))
            {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"PRIVATE-live-selected-"+label+".png"));}
            root.ClearSelection2(true);UiClick(form,"btnRefresh");UiSpin(()=>!(bool)Get(controller,"Running"));
            Check(Get(controller,"displayed")!=null&&(int)Get(Get(controller,"displayed"),"LeafOccurrences")==1,"Refresh retains saved selected scope after selection changes");
            UiClick(form,"btnRunBatch");UiClick(form,"btnCancel");Check((bool)Get(controller,"Running"),"Native Cancel waits for completion");
            UiSpin(()=>!(bool)Get(controller,"Running"));Check(UiParts(form).Length==0&&UiControl(form,"lblProgressPercent").Text!="100%","No canceled CAD results or false completion");
            UiClick(form,"btnRunBatch");UiSpin(()=>!(bool)Get(controller,"Running"));Check(UiParts(form).Length>0,"Restart after cancel");
            var selectedModel=(ModelDoc2)selected.GetModelDoc2();selectedModel.ForceRebuild3(false);
            UiSpin(()=>UiParts(form).Length==0);
            Check(UiControl(form,"lblProgressTitle").Text.Contains("cũ"),"Native Part rebuild withdraws held Live results");
            Activate(app,root);root.ForceRebuild3(false);
            Check(root.Extension.NeedsRebuild2==0,"Rebuild Assembly after the edited child before recapture");
            UiClick(form,"btnRefresh");UiSpin(()=>!(bool)Get(controller,"Running"));
            Check(Get(controller,"displayed")!=null,"Refresh recaptures rebuilt Part: "+UiControl(form,"lblProgressTitle").Text);
            var old=selected.Transform2;var values=((double[])old.ArrayData).ToArray();values[9]+=.001;
            selected.Transform2=(MathTransform)((MathUtility)app.GetMathUtility()).CreateTransform(values);
            UiSpin(()=>UiParts(form).Length==0);
            Check(UiControl(form,"lblProgressTitle").Text.Contains("cũ"),"Native pose change withdraws stale UI");
            selected.Transform2=old;root.ForceRebuild3(false);
            UiClick(form,"btnRefresh");UiSpin(()=>!(bool)Get(controller,"Running"));Check(Get(controller,"displayed")!=null,
                "Refresh after stale pose: "+UiControl(form,"lblProgressTitle").Text);
            UiClick(form,"btnRunBatch");form.Close();Check(!form.IsDisposed,"Window close deferred while backend runs");
            UiSpin(()=>form.IsDisposed);
            report["Ui-"+label]=new {Entire=true,Selected=true,PreviewSeparated=true,Cold=true,WarmCaptureHits=Get(warm,"CaptureHits"),
                Cancel=true,StalePart=true,StalePose=true,Refresh=true,CloseDuringRun=true,BendCounts=counts,
                WorkerComQueries=T("BendEnvelope.SheetNativeTopologyCapture").GetProperty("WrongThreadCount",All).GetValue(null,null)};
        }
        finally{((IDisposable)controller).Dispose();form.Dispose();}
    }
    private static object UiTimings(object controller)
    {
        var timings=Get(controller,"LastRunTimings");
        Check(timings!=null,"Run records real conversion/render timings");
        return timings.GetType().GetProperties(All).ToDictionary(p=>p.Name,p=>p.GetValue(timings,null));
    }
    private static void ExerciseNativeCaptureClose(ISldWorks app,ModelDoc2 root,Component2 selected,object source,string output,Dictionary<string,object> report)
    {
        Activate(app,root);root.ClearSelection2(true);Check(selected.Select4(false,null,false),"Select cold native capture close fixture");
        var form=(Form)Activator.CreateInstance(product.GetType("ADDIN.UI.BendAssemblyBatchForm",true),All,null,new object[]{root.GetTitle(),root.ConfigurationManager.ActiveConfiguration.Name},null);
        var settings=New("BendToolStorageSettings",Path.Combine(output,"ui-close-settings.json"));
        Call(settings,"SavePath",Get(Get(source,"Snapshot"),"SourcePath"),CancellationToken.None);
        var backend=Activator.CreateInstance(product.GetType("ADDIN.UI.BendAssemblyBatchLiveBackend",true),All,null,new[]{app,root,New("BendToolCatalogProvider",settings)},null);
        var controller=Activator.CreateInstance(product.GetType("ADDIN.UI.BendAssemblyBatchController",true),All,null,new[]{form,backend},null);
        try
        {
            form.Show();Call(form,"SetScope",Enum.Parse(T("BendAssembly.AssemblyCheckScope"),"SelectedComponents"));
            bool deferred=false;
            // This observer reads managed fields only. It never calls COM or
            // controls from the worker, except the thread-safe BeginInvoke.
            // Observe the explicit atomic-capture flag. The source index now
            // advances after its asynchronous provider read, so index-1/Done
            // no longer proves whether native Capture is active. No owner,
            // item or COM object is inspected on this observer thread.
            var posted=Task.Run(()=> {
                var clock=Stopwatch.StartNew();
                while(clock.ElapsedMilliseconds<30000)
                {
                    var wrapper=Get(controller,"operation");
                    if(wrapper!=null&&(bool)Get(controller,"pumping"))
                    {
                        var native=Get(wrapper,"operation");
                        if((bool)Get(native,"NativeCaptureActive"))
                        {
                            form.BeginInvoke(new Action(()=>{form.Close();deferred=!form.IsDisposed;}));return true;
                        }
                    }
                    Thread.Sleep(1);
                }
                return false;
            });
            UiClick(form,"btnRunBatch");
            UiSpin(()=>form.IsDisposed||posted.IsCompleted&&!posted.GetAwaiter().GetResult());
            Check(posted.GetAwaiter().GetResult()&&deferred&&form.IsDisposed,"Actual native capture close is posted, deferred and acknowledged safely");
            report["CloseDuringActualNativeCapture"]="PASS";
        }
        finally{((IDisposable)controller).Dispose();form.Dispose();}
    }
    private static IEnumerable<Control> UiControls(Control parent)
    {foreach(Control child in parent.Controls){yield return child;foreach(var nested in UiControls(child))yield return nested;}}
    private static void ExerciseLauncher(ISldWorks app,ModelDoc2 root,Dictionary<string,object> report)
    {
        Activate(app,root);root.ClearSelection2(true);
        using(var owner=(Form)Activator.CreateInstance(product.GetType("ADDIN.UI.BendCheckForm",true),All,null,new object[]{app,root,null},null))
        {
            owner.Show();var launch=UiControls(owner).OfType<Button>().Single(b=>b.Text=="Check chấn");launch.PerformClick();
            var form=(Form)Get(owner,"batchForm");var controller=Get(owner,"batchController");
            Check(form!=null&&controller!=null,"Assembly -> Check Chan -> Assembly Batch launches the live controller");
            UiClick(form,"btnRunBatch");UiSpin(()=>!(bool)Get(controller,"Running"));
            var result=Get(controller,"displayed");Check(result!=null&&(bool)Get(result,"Current"),"Launcher completes real native Run");
            UiCheckBindings(form,result);report["LauncherTimings"]=UiTimings(controller);
            form.Close();Check(Get(owner,"batchForm")==null,"Launcher releases closed child");owner.Close();
            report["LauncherWorkflow"]="PASS";
        }
    }
    private static string generatedSeedDimension;
    private static ModelDoc2 UiSheet(ISldWorks app,string template,string path,int bends)
        => UiSheet(app,template,path,bends,false);
    private static ModelDoc2 UiSheet(ISldWorks app,string template,string path,int bends,bool dimensioned)
    {
        var doc=(ModelDoc2)app.NewDocument(template,0,0,0);Check(doc!=null,"Create private sheet fixture");
        Check(Plane(doc).Select2(false,0),"Select native sheet plane");doc.SketchManager.InsertSketch(true);
        // Controlled test CAD only: request native perpendicular bend corners.
        // Never trust this input count; independently check the captured count.
        if(bends!=0&&bends!=3&&bends!=6)throw new ArgumentOutOfRangeException(nameof(bends));
        var xy=bends==0?new double[]{0,0,.05,0}:bends==3?new double[]{0,0,.05,0,.05,.03,.08,.03,.08,.06}:
            new double[]{0,0,.05,0,.05,.03,.08,.03,.08,.06,.11,.06,.11,.09,.14,.09};
        SketchSegment firstLine=null;
        for(int i=0;i+3<xy.Length;i+=2)
        {var line=doc.SketchManager.CreateLine(xy[i],xy[i+1],0,xy[i+2],xy[i+3],0) as SketchSegment;if(firstLine==null)firstLine=line;}
        if(dimensioned)
        {
            doc.ClearSelection2(true);Check(firstLine!=null&&firstLine.Select4(false,null),"Select fresh seed sketch segment");
            var display=doc.AddDimension2(.025,-.01,0) as DisplayDimension;
            Check(display!=null,"Explicit driving length on controlled seed only");
            generatedSeedDimension=string.Join("@",((Dimension)display.GetDimension2(0)).FullName.Split('@').Take(2));
            doc.ClearSelection2(true);
        }
        doc.SketchManager.InsertSketch(true);Feature profile=null;
        for(var f=(Feature)doc.FirstFeature();f!=null;f=(Feature)f.GetNextFeature())if(f.GetTypeName2()=="ProfileFeature")profile=f;
        Check(profile!=null&&profile.Select2(false,0),"Select generated profile");
        Check(doc.FeatureManager.InsertSheetMetalBaseFlange2(.002,false,.001,.06,.01,false,0,0,1,null,false,2,.0001,.0001,.5,true,false,true,true)!=null,"Native base flange");
        doc.ConfigurationManager.ActiveConfiguration.Name="Default";
        doc.ForceRebuild3(false);SaveAs(doc,path);return doc;
    }
    // --ui dll output(new) six-bend-seed assembly-template min-xlsx cut-plane-seed
    private static int UiMain(string[] args)
    {
        if(args.Length!=6){Console.WriteLine("--ui dll output(new) six-bend-seed assembly-template min-xlsx cut-plane-seed");return 2;}
        ISldWorks app=null;var disposable=new List<IDisposable>();var report=new Dictionary<string,object>();
        string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
        try
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            product=Assembly.LoadFrom(args[0]);string seedHash=Sha(args[2]),edgeHash=Sha(args[5]);
            if(System.Environment.GetEnvironmentVariable("ADDIN_NATIVE_CAPTURE_DIAGNOSTICS")=="1")
            {
                captureDiagnosticsDirectory=output;int staThread=Thread.CurrentThread.ManagedThreadId;
                AppDomain.CurrentDomain.FirstChanceException+=(sender,eventArgs)=> {
                    if(Thread.CurrentThread.ManagedThreadId==staThread&&eventArgs.Exception is KeyNotFoundException)
                        File.AppendAllText(Path.Combine(output,"PRIVATE-capture-exceptions.txt"),eventArgs.Exception+System.Environment.NewLine);
                };
            }
            app=NativeTestProcess.StartOwned(output);app.Visible=false;app.UserControl=false;
            string partTemplate=Path.Combine(Path.GetDirectoryName(args[3]),"部品.PRTDOT");
            bool closeOnly=System.Environment.GetEnvironmentVariable("ADDIN_NATIVE_UI_CLOSE_ONLY")=="1";
            var zero=closeOnly?null:UiSheet(app,partTemplate,Path.Combine(output,"ZERO.SLDPRT"),0);
            var three=UiSheet(app,partTemplate,Path.Combine(output,"GENERATED-02.SLDPRT"),3);
            if(!closeOnly){string twinPath=Path.Combine(output,"GENERATED-03.SLDPRT");File.Copy(three.GetPathName(),twinPath,false);Open(app,twinPath);}
            string sixPath=Path.Combine(output,"SIX.SLDPRT");ModelDoc2 six=null;
            bool generatedSix=System.Environment.GetEnvironmentVariable("ADDIN_NATIVE_UI_GENERATED_SIX")=="1";
            if(!closeOnly)
            {
                if(generatedSix)six=UiSheet(app,partTemplate,sixPath,6);
                else {File.Copy(args[2],sixPath,false);File.SetAttributes(sixPath,FileAttributes.Normal);six=Open(app,sixPath);}
            }
            report["UiSixSource"]=closeOnly?"NOT_REQUESTED_IN_CAPTURE_CLOSE_SCOPE":generatedSix?"GENERATED_NATIVE_TEST_CAD":"COPIED_NATIVE_SEED";
            var root=(ModelDoc2)app.NewDocument(args[3],0,0,0);Check(root!=null,"New private UI assembly");
            var assembly=(AssemblyDoc)root;Component2 selected=null,closeSelected=null;int n=0;
            foreach(var doc in new[]{zero,three,six}.Where(d=>d!=null))
            {
                var c=assembly.AddComponent5(doc.GetPathName(),0,"",true,doc.ConfigurationManager.ActiveConfiguration.Name,n++*.2,0,0);
                Check(c!=null,"Insert actual sheet fixture");if(doc==six)selected=c;if(doc==three)closeSelected=c;
            }
            root.ForceRebuild3(false);SaveAs(root,Path.Combine(output,"UI-TEST.SLDASM"));Activate(app,root);
            var source=Source(args[4],output);
            if(closeOnly)
            {
                ExerciseNativeCaptureClose(app,root,closeSelected,source,output,report);
                report["Scope"]="ACTUAL_NATIVE_CAPTURE_CLOSE_ONLY";
            }
            else
            {
            ExerciseLauncher(app,root,report);
            ExerciseNativeUi(app,root,source,output,selected,report,"zero-three-six");
            ExerciseNativeCaptureClose(app,root,closeSelected,source,output,report);
            System.Environment.SetEnvironmentVariable("ADDIN_ASSEMBLY_UI_TEST","1");
            // The existing cut fixture uses its template plane and requires a
            // seed intersecting that plane. Copy the caller's matching fixture;
            // never change an engine verdict to accommodate the generated profile.
            string edgeSheet=Path.Combine(output,"EDGE-SEED.SLDPRT"),edgeTwin=Path.Combine(output,"EDGE-TWIN.SLDPRT");
            File.Copy(args[5],edgeSheet,false);File.Copy(args[5],edgeTwin,false);
            File.SetAttributes(edgeSheet,FileAttributes.Normal);File.SetAttributes(edgeTwin,FileAttributes.Normal);
            // As in the 120/25 fixture, ordinary mixed-source probes require
            // context-independent definitions. The caller's seed can contain
            // live external sketch references. Break those on these disposable
            // copies only; retaining them would correctly make every ordinary
            // occurrence ineligible before mixed-instance isolation is tested.
            var prepared=new List<object>();
            foreach(string fixturePath in new[]{edgeSheet,edgeTwin})
            {
                Check(Path.GetFullPath(fixturePath).StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"External-reference preparation is confined to owned fixture copies");
                var fixture=Open(app,fixturePath);int before=fixture.Extension.ListExternalFileReferencesCount();
                object models,components,features,types,status,entities,featCom,configOptions,configNames;
                fixture.Extension.ListExternalFileReferences2(out models,out components,out features,out types,out status,out entities,out featCom,out configOptions,out configNames);
                if(before>0)fixture.Extension.BreakAllExternalFileReferences2(true);
                fixture.ForceRebuild3(false);Save(fixture);
                prepared.Add(new{File=Path.GetFileName(fixturePath),ExternalBefore=before,StatusBefore=status});
            }
            report["PrivateEdgeSourcePreparation"]=prepared;Activate(app,root);
            EdgeFixtures(app,root,args[3],output,disposable,report,source,edgeSheet,edgeTwin);
            }
            Check(Sha(args[2])==seedHash,"Original seed bytes unchanged");
            Check(Sha(args[5])==edgeHash,"Original cut-plane seed bytes unchanged");
            report["Result"]="PASS";report["Assertions"]=assertions;
            File.WriteAllText(Path.Combine(output,"PRIVATE-native-ui-result.json"),Json.Serialize(report));Console.WriteLine(Json.Serialize(report));return 0;
        }
        catch(Exception ex){report["Result"]="FAIL";report["Assertions"]=assertions;report["Error"]=ex.ToString();File.WriteAllText(Path.Combine(output,"PRIVATE-native-ui-result.json"),Json.Serialize(report));Console.WriteLine(ex);return 1;}
        finally{foreach(var d in disposable)d.Dispose();if(app!=null){try{NativeTestProcess.RestoreCommandScope(app);app.CloseAllDocuments(true);app.ExitApp();}catch{}}}
    }
}
