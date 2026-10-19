using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using Environment=System.Environment;

internal static class ToolLibraryProgram
{
    [STAThread] private static int Main(string[] args) => NativePlanningValidation.RunToolLibrary(args);
}
internal static partial class NativePlanningValidation
{
    private sealed class FailingLibraryListener : TraceListener
    {
        internal int Failures;
        public override void Write(string message) { }
        public override void WriteLine(string message)
        { if (message.StartsWith("[BEND TOOL LIBRARY]",StringComparison.Ordinal)) { Failures++; throw new InvalidOperationException("Injected library diagnostic exception"); } }
    }
    internal static int RunToolLibrary(string[] args)
    {
        ISldWorks app=null; bool owned=false; var report=new Dictionary<string,object>();
        string oldDirectory=Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_DIRECTORY"), oldManifest=Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_MANIFEST"),
            oldEvidence=Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DATUM_EVIDENCE"),oldPlacement=Environment.GetEnvironmentVariable("ADDIN_BEND_PLACEMENT_CONFIG");
        try
        {
            product=Assembly.LoadFrom(Path.GetFullPath(args[0])); output=Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            bool debugDiagnostics=product.GetCustomAttribute<DebuggableAttribute>()?.IsJITTrackingEnabled==true;
            report["debugDiagnosticInjection"]=debugDiagnostics?"Enabled":"Debug.WriteLine omitted by Release compiler";
            string original=Path.GetFullPath(args[2]), hash=Sha(original), copy=Path.Combine(output,"PRIVATE-library-test.SLDPRT");
            File.Copy(original,copy,true); File.SetAttributes(copy,FileAttributes.Normal);
            Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_DIRECTORY",Path.GetFullPath(args[3]));
            Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_MANIFEST",null);
            Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DATUM_EVIDENCE",null);
            Environment.SetEnvironmentVariable("ADDIN_BEND_PLACEMENT_CONFIG",args.Length>5?Path.GetFullPath(args[5]):null);
            app=NativeTestProcess.StartOwned(output); int pid=app.GetProcessID();
            owned=true; Check(owned,"Explicit owned API process required"); app.Visible=false; app.UserControl=false;
            File.WriteAllText(Path.Combine(output,"ownership.json"),Json.Serialize(new {pid,owned,startUtc=Process.GetProcessById(pid).StartTime.ToUniversalTime().ToString("o")}));
            Console.WriteLine("Owned library test SW process "+pid);
            int errors=0,warnings=0; var model=app.OpenDoc6(copy,1,1,"",ref errors,ref warnings) as ModelDoc2; Check(model!=null,"Open private Part copy "+errors);
            int stamp=model.GetUpdateStamp(); bool dirty=model.GetSaveFlag(); string config=model.ConfigurationManager.ActiveConfiguration.Name;
            using (var form=OpenCheck(app,model,args[4]))
            {
                var session=Get(form,"session"); var worker=Get(session,"toolLibrary");
                Func<object> library=()=>Get(session,"ToolLibrary");
                Func<int> count=()=>Items(Get(library(),"Tools")).Count(t=>(bool)Get(Get(t,"Validation"),"PunchGeometryReadComplete"));
                Until(()=> {Call(form,"Tick"); return count()==18;},"Existing form receives 18-tool snapshot");
                var initial=library(); var item=Items(Get(session,"Items")).Single();
                int initialBends=Items(Get(item,"Rows")).Length;
                Check(ReferenceEquals(Get(item,"ToolProfiles"),initial),"Item uses session library snapshot");
                Check(Items(Get(initial,"Tools")).All(t=>!(bool)Get(Get(t,"Validation"),"ClampVerified")),"No clamp verification inferred");
                Check(Items(Get(initial,"Tools")).Count(t=>(bool)Get(Get(Get(t,"Validation"),"Region"),"IsValidRegion"))==17,"17 analytic regions, KIK remains open");
                Check(Items(Get(initial,"Tools")).All(t=>Get(Get(t,"Datums"),"Status").ToString()=="Missing" && Get(Get(t,"Datums"),"TipPointMm")==null),"No guessed datum evidence");
                report["initial"]=Snapshot(form,model,true);
                string min=NativeSnapshotJson.Canonical(report["initial"]); string minText=Get(item,"MinimumText").ToString();
                report["initialSerializedAtAssertion"]=Json.Serialize(report["initial"]);
                report["initialCanonicalAtAssertion"]=min;
                var placement=Get(item,"PlacementDraft");Check(placement!=null,"Read-only placement is captured by existing Check chan");
                Check(Items(Get(placement,"Sections")).Length==initialBends&&Items(Get(placement,"Candidates")).Length==18*initialBends,"Every actual bend has all 18 candidates");
                Check(Items(Get(placement,"Candidates")).All(c=>Get(c,"Status").ToString()=="CHUA_XAC_MINH"&&!(bool)Get(c,"ReadyForCollisionCheck")),"No automatic Check Max verdict");
                Check(Get(placement,"WinnerToolId")==null&&Get(item,"PlacementDiagnosticError")==null,"No winner or placement error");
                var collision=Items(Get(placement,"CollisionResults"));
                Check(collision.Length==18*initialBends,"Collision engine retains all 18 actual tool rows per bend");
                Check(collision.All(c=>Get(c,"PunchResult")!=null&&Get(c,"HolderResult")!=null&&Get(c,"CombinedResult").ToString()=="UNKNOWN"),
                    "Unapproved real setups cannot become CLEAR or COLLISION");
                Check(collision.All(c=>Items(Get(c,"States")).Length==4&&Get(c,"AllRequiredStatesResult").ToString()=="UNKNOWN"),
                    "Observed final pose never replaces unknown process states");
                Check(collision.All(c=>Items(Get(c,"Reasons")).Length>0&&Get(Get(c,"PunchResult"),"CertifiedClearanceLowerMm")==null&&Get(Get(c,"HolderResult"),"Witness")==null),
                    "Actual UNKNOWN results include reasons without fictitious certificates");
                report["collision"]=new {Bends=initialBends,Rows=collision.Length,Clear=0,Collision=0,Unknown=collision.Length};
                int arcReads=(int)product.GetType("ADDIN.Commands.BendPlacement.BendPlacementNativeCapture").GetProperty("ArcMetadataReadCount",All).GetValue(null,null);
                Check(arcReads>0,"Analytic circle metadata appended during original STA capture");
                var nativeEnvelopeType=product.GetType("ADDIN.Commands.BendEnvelope.SheetNativeTopologyCapture");
                int bodyCaptures=(int)nativeEnvelopeType.GetProperty("BodyCaptureCount",All).GetValue(null,null);
                Check(bodyCaptures>0,"One optional full topology append was read on STA");
                var managedCapture=Get(item,"PlacementCapture");
                product.GetType("ADDIN.Commands.BendPlacement.BendPlacementNativeCapture").GetMethod("Copy",All).Invoke(null,new[]{item});
                Check((int)nativeEnvelopeType.GetProperty("BodyCaptureCount",All).GetValue(null,null)==bodyCaptures,"Same snapshot reuses cached native topology append");
                var volumeRequest=New("Commands.BendEnvelope.CollisionBatchRequest",managedCapture,initial,null,null);
                object volumeWorker=New("Commands.BendEnvelope.CollisionBatchWorker");object volumeResult;
                try
                {
                    var task=(Task)Call(volumeWorker,"RunAsync",volumeRequest,null,CancellationToken.None,null);
                    Until(()=>task.IsCompleted,"Managed 18-tool envelope batch completes");task.GetAwaiter().GetResult();
                    volumeResult=task.GetType().GetProperty("Result").GetValue(task,null);
                    var volumeRows=Items(Get(volumeResult,"Candidates"));var envelopes=Items(Get(volumeResult,"Envelopes"));
                    Check(volumeRows.Length==18*initialBends&&volumeRows.All(r=>Get(r,"CombinedResult").ToString()=="UNKNOWN"),"Actual 3D tool extents remain unverified for all 18 slots");
                    Check(envelopes.Length==initialBends,"Coverage result exists for every actual bend");
                    Check((int)Get(volumeResult,"PreparationThreadId")!=Thread.CurrentThread.ManagedThreadId,"Managed geometry preparation runs off the STA caller");
                    Check((int)nativeEnvelopeType.GetProperty("WrongThreadCount",All).GetValue(null,null)==0,"No native topology capture runs on a worker");
                    report["envelope"]=new {Bends=initialBends,Rows=volumeRows.Length,BodyCaptures=bodyCaptures,
                        CaptureElapsedMs=nativeEnvelopeType.GetProperty("TotalCaptureMilliseconds",All).GetValue(null,null),
                        ElapsedMs=Get(volumeResult,"ElapsedMilliseconds"),CaptureHash=Items(Get(managedCapture,"Bodies")).Select(b=>Get(Get(b,"EnvelopeTopology"),"SourceSha256")).ToArray(),
                        Tiers=envelopes.Select(e=>new {Feature=Get(Get(e,"Binding"),"FeatureId"),Tier=Get(Get(e,"Certificate"),"Tier").ToString(),
                            Intervals=Items(Get(Get(e,"Certificate"),"Intervals")).Length,Reasons=Get(e,"Reasons")}).ToArray()};
                    Check((int)Get(Get(item,"Context"),"GeometryReadCount")==1,"Envelope batch reuses original Check Min capture");
                    var processRequest=New("Commands.BendProcess.ProcessBatchRequest",managedCapture,initial,null,
                        Typed("Commands.BendEnvelope.SheetEnvelopeCapture",envelopes));
                    using(var processWorker=(IDisposable)New("Commands.BendProcess.ProcessBatchWorker"))
                    {
                        var processTask=(Task)Call(processWorker,"RunAsync",processRequest,null,CancellationToken.None,null);
                        Until(()=>processTask.IsCompleted,"Managed 18-tool process batch completes");processTask.GetAwaiter().GetResult();
                        var processResult=processTask.GetType().GetProperty("Result").GetValue(processTask,null);
                        var processRows=Items(Get(processResult,"Candidates"));
                        Check(processRows.Length==18*initialBends&&processRows.All(r=>Get(r,"ProcessResult").ToString()=="UNKNOWN"),"Final native CAD cannot supply missing forming states");
                        Check(processRows.All(r=>Items(Get(r,"PairSegments")).Length==10&&Items(Get(r,"UnknownPhases")).Length==5),"Every real tool retains both pairs and five unresolved process phases");
                        Check((int)Get(processResult,"KernelQueries")==0,"No unverified real motion is silently sent to the numeric kernel");
                        Check((int)Get(processResult,"WorkerThreadId")!=Thread.CurrentThread.ManagedThreadId,"Process batch uses only managed worker data");
                        Check((int)Get(Get(item,"Context"),"GeometryReadCount")==1&&
                            (int)nativeEnvelopeType.GetProperty("BodyCaptureCount",All).GetValue(null,null)==bodyCaptures&&
                            (int)nativeEnvelopeType.GetProperty("WrongThreadCount",All).GetValue(null,null)==0,"Process batch adds zero native reads/worker COM calls");
                        report["process"]=new {Rows=processRows.Length,Unknown=processRows.Length,
                            PairPhaseResults=processRows.Sum(r=>Items(Get(r,"PairSegments")).Length),
                            WorkerMs=Get(processResult,"WorkerMilliseconds"),KernelQueries=Get(processResult,"KernelQueries")};
                    }
                    if(args.Skip(6).Contains("--native-family-reference"))
                        report["nativeFamilyReference"]=NativeFamilyReference(model,managedCapture,envelopes);
                }
                finally {((IDisposable)volumeWorker).Dispose();}
                report["placement"]=Json.DeserializeObject((string)product.GetType("ADDIN.Commands.BendPlacement.BendPlacementBuilder").GetMethod("Diagnostic",All).Invoke(null,new[]{placement}));
                report["sections"]=Items(Get(placement,"Sections")).Select(s=>new {Coverage=Get(s,"Coverage").ToString(),Reasons=Get(s,"Reasons"),BoundaryCount=Items(Get(s,"Boundary")).Length}).ToArray();
                if(args.Skip(6).Contains("--capture-private"))
                    File.WriteAllText(Path.Combine(output,"PRIVATE-managed-capture.json"),Json.Serialize(Get(item,"PlacementCapture")));
                if(args.Skip(6).Contains("--require-uniform-sections"))
                    Check(Items(Get(placement,"Sections")).All(s=>Get(s,"Coverage").ToString()=="CompleteUniformExtrusion"&&Items(Get(s,"Boundary")).Length>0),
                        "Actual uniform Part retains the full closed material section for every bend");
                if(args.Length>5)
                {
                    Check(Items(Get(Get(initial,"PlacementSources"),"Tools")).Count(t=>Get(t,"Punch")!=null)==18,"Actual Master mappings reach production pipeline");
                    string good=Environment.GetEnvironmentVariable("ADDIN_BEND_PLACEMENT_CONFIG");
                    Environment.SetEnvironmentVariable("ADDIN_BEND_PLACEMENT_CONFIG",Path.Combine(output,"missing-placement-config.json"));Call(worker,"Refresh");
                    Until(()=>{Call(form,"Tick");return !ReferenceEquals(library(),initial)&&Get(item,"PlacementDraft")!=null;},"Placement config change creates safe unknown draft");
                    Check(!(bool)Get(placement,"Current")&&!(bool)Get(Get(initial,"PlacementSources"),"Current"),"Held placement/source invalidate on configuration edit");
                    Check(Items(Get(Get(item,"PlacementDraft"),"Candidates")).Length==18*initialBends,"Missing configuration retains 18 slots per actual bend");
                    Check(NativeSnapshotJson.Canonical(Snapshot(form,model,true))==min,"Placement-source failure does not change Min or Planning");
                    Environment.SetEnvironmentVariable("ADDIN_BEND_PLACEMENT_CONFIG",good);Call(worker,"Refresh");
                    Until(()=>{Call(form,"Tick");return Items(Get(Get(library(),"PlacementSources"),"Tools")).All(t=>Get(t,"Punch")!=null);},"Master/Excel source restores independently");
                    // Exercise new optional review I/O through the real file
                    // worker/session, without sending a COM object to it.
                    var privateConfig=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(good));
                    foreach(string key in privateConfig.Keys.Where(k=>k.EndsWith("Path",StringComparison.Ordinal)).ToArray())
                    {
                        string value=Convert.ToString(privateConfig[key]);
                        if(!string.IsNullOrWhiteSpace(value))privateConfig[key]=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(good),value));
                    }
                    string malformed=Path.Combine(output,"PRIVATE-malformed-holder-review.json"),reviewConfig=Path.Combine(output,"PRIVATE-holder-source-config.json");
                    File.WriteAllText(malformed,"{broken");
                    foreach(string key in new[]{"HolderEvidencePath","PairSeatingEvidencePath","ReviewAuthoritiesPath"})privateConfig[key]=malformed;
                    File.WriteAllText(reviewConfig,Json.Serialize(privateConfig));
                    var beforeHolderEdit=library();Environment.SetEnvironmentVariable("ADDIN_BEND_PLACEMENT_CONFIG",reviewConfig);Call(worker,"Refresh");
                    Until(()=>{Call(form,"Tick");return !ReferenceEquals(library(),beforeHolderEdit)&&Items(Get(Get(library(),"PlacementSources"),"Reasons")).Any(r=>r.ToString()=="INVALID_HOLDER_EVIDENCE_SOURCE");},"Malformed holder/seating review is isolated in native pipeline");
                    Check(Items(Get(Get(library(),"PlacementSources"),"Tools")).Count(t=>Get(t,"Punch")!=null)==18,"Optional review error preserves actual 18 source punches");
                    Check(NativeSnapshotJson.Canonical(Snapshot(form,model,true))==min&&ReferenceEquals(item,Items(Get(session,"Items")).Single()),"Holder review edit preserves Min/planning and capture object");
                    Check((int)Get(Get(item,"Context"),"GeometryReadCount")==1,"Holder review edit adds no COM geometry read");
                    Environment.SetEnvironmentVariable("ADDIN_BEND_PLACEMENT_CONFIG",good);Call(worker,"Refresh");
                    Until(()=>{Call(form,"Tick");return !Items(Get(Get(library(),"PlacementSources"),"Reasons")).Any(r=>r.ToString()=="INVALID_HOLDER_EVIDENCE_SOURCE");},"Optional review source recovery");
                }
                Action<string> source=directory=> {Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_DIRECTORY",directory); Call(worker,"Refresh");};
                string missing=Path.Combine(output,"missing-company-source"); source(missing);
                Until(()=> {Call(form,"Tick"); return count()==0 && !ReferenceEquals(library(),initial);},"Missing directory yields explicit unverified snapshot");
                Check(!(bool)Get(initial,"Current"),"Old source snapshot invalidated");
                Check(Get(item,"Error")==null && (bool)Get(item,"Current") && Get(item,"MinimumText").ToString()==minText,"Missing DXFs do not damage Min");
                Check(ReferenceEquals(item,Items(Get(session,"Items")).Single()),"No second COM capture on source update");
                Check((int)Get(Get(item,"Context"),"GeometryReadCount")==1,"One model geometry capture");
                Check(NativeSnapshotJson.Canonical(Snapshot(form,model,true))==min,"Min/planning unchanged by missing source");
                Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_MANIFEST",Path.Combine(output,"missing-manifest.json")); source(args[3]);
                Until(()=> {Call(form,"Tick"); return Get(library(),"Error")!=null && Get(library(),"Error").ToString().Contains("manifest");},"Bad manifest reports separate library error");
                var afterBadManifest=Snapshot(form,model,true);
                report["afterBadManifest"]=afterBadManifest;
                report["badManifestItemError"]=Get(item,"Error");
                report["badManifestItemSame"]=ReferenceEquals(item,Items(Get(session,"Items")).Single());
                string manifestSerialized=Json.Serialize(afterBadManifest);
                report["manifestSerializedAtAssertion"]=manifestSerialized;
                report["manifestCanonicalAtAssertion"]=NativeSnapshotJson.Canonical(afterBadManifest);
                Check(Get(item,"Error")==null && NativeSnapshotJson.Canonical(afterBadManifest)==min,"Bad manifest does not block Check Min");
                Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_MANIFEST",null); source(args[3]);
                Until(()=> {Call(form,"Tick"); return count()==18;},"Library recovers after source restore");
                var beforeEvidence=library(); string badEvidence=Path.Combine(output,"PRIVATE-malformed-datum.json");File.WriteAllText(badEvidence,"{broken");
                Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DATUM_EVIDENCE",badEvidence);Call(worker,"Refresh");
                Until(()=> {Call(form,"Tick");return Items(Get(library(),"Tools")).All(t=>Get(Get(t,"Datums"),"Status").ToString()=="Invalid");},"Malformed datum evidence is isolated");
                Check(!(bool)Get(beforeEvidence,"Current") && count()==18,"Evidence edit invalidates snapshot but preserves all CAD profiles");
                Check(ReferenceEquals(item,Items(Get(session,"Items")).Single()) && (int)Get(Get(item,"Context"),"GeometryReadCount")==1,"Evidence edit does not recapture COM geometry");
                Check(Get(item,"Error")==null && NativeSnapshotJson.Canonical(Snapshot(form,model,true))==min,"Invalid datums do not change Check Min/planning");
                Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DATUM_EVIDENCE",null);Call(worker,"Refresh");
                Until(()=> {Call(form,"Tick");return Items(Get(library(),"Tools")).All(t=>Get(Get(t,"Datums"),"Status").ToString()=="Missing");},"Datum configuration recovers");
                var listener=new FailingLibraryListener(); Debug.Listeners.Add(listener);
                try
                {
                    source(missing);
                    Until(()=> {Call(form,"Tick"); return count()==0;},"Diagnostic failure injection update");
                    Check(debugDiagnostics?listener.Failures==1&&Convert.ToString(Get(session,"ToolLibraryDiagnosticError")).Contains("Injected"):listener.Failures==0,
                        "Diagnostic failure isolated in Debug; diagnostic call omitted in Release");
                    Check(Get(item,"Error")==null && Get(item,"MinimumText").ToString()==minText,"Diagnostic listener cannot change Min");
                }
                finally {Debug.Listeners.Remove(listener);}
                source(args[3]); Until(()=> {Call(form,"Tick"); return count()==18;},"Final library restore");
                Check((int)product.GetType("ADDIN.Commands.BendPlacement.BendPlacementNativeCapture").GetProperty("ArcMetadataReadCount",All).GetValue(null,null)==arcReads,"Library/evidence edits do not call SOLIDWORKS arc capture again");
                Check((int)nativeEnvelopeType.GetProperty("BodyCaptureCount",All).GetValue(null,null)==bodyCaptures,"Source-only changes add no native B-rep traversal");
                Check((int)Get(Get(item,"Context"),"GeometryReadCount")==1,"All source-only changes reuse original model capture");
                Check(model.GetUpdateStamp()==stamp && model.GetSaveFlag()==dirty && model.ConfigurationManager.ActiveConfiguration.Name==config,"Library I/O preserves model stamp/config/dirty");
                report["libraryDebug"]=Json.DeserializeObject((string)Call(library(),"ToDebugJson"));
                var held=Get(item,"PlacementDraft");
                model.ForceRebuild3(false); Call(form,"Tick");
                Check(!(bool)Get(item,"Current") && Get(item,"ToolProfiles")==null,"Stale model no longer exposes a tool snapshot link");
                Check(Get(item,"PlacementDraft")==null&&!(bool)Get(held,"Current"),"Rebuild invalidates held placement and hides stale draft");
                Check(Items(Get(volumeResult,"Envelopes")).All(e=>!(bool)Get(Get(e,"Certificate"),"Current")),"Native rebuild invalidates held continuous-coverage certificates");
                Check(Items(Get(held,"CollisionResults")).All(c=>Get(Get(c,"PunchResult"),"Status").ToString()=="UNKNOWN"&&
                    Items(Get(Get(c,"PunchResult"),"Reasons")).Cast<string>().Contains("STALE_COLLISION_INPUT")),"Held collision evidence invalidates with model rebuild");
                Until(()=>Scanned(form) && !ReferenceEquals(item,Items(Get(session,"Items")).Single()),"Existing rebuild auto-rescan");
                Check(Items(Get(Items(Get(session,"Items")).Single(),"Rows")).Length==initialBends,"Rebuild retains actual sample bend count");
                var rebuilt=Items(Get(session,"Items")).Single();Check(Get(rebuilt,"PlacementDraft")!=null,"Placement regenerated after existing auto-rescan");
                Check(model.AddConfiguration3("PHASE3B_TEST_ONLY","private regression copy","",0)!=null,"Create private-copy configuration for stale test");
                model.ShowConfiguration2("PHASE3B_TEST_ONLY");
                Check(model.ConfigurationManager.ActiveConfiguration.Name=="PHASE3B_TEST_ONLY","Switch private-copy configuration");Call(form,"Tick");
                Check(Get(rebuilt,"PlacementDraft")==null,"Changed model configuration hides old placement");
                form.Close();
            }
            Check(Sha(original)==hash,"Original Part unchanged"); report["originalUnchanged"]=true; report["passed"]=true;
        }
        catch(Exception ex) { report["error"]=ex.ToString(); Console.WriteLine(ex); }
        finally
        {
            report["assertions"]=checks;
            if (output!=null) File.WriteAllText(Path.Combine(output,"PRIVATE-library-native.json"),Json.Serialize(report));
            if (owned && app!=null)
            { try {foreach(ModelDoc2 doc in app.GetDocuments() as object[]??new object[0]) if(doc.GetPathName().StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) app.CloseDoc(doc.GetTitle());app.ExitApp();}catch(Exception ex){Console.WriteLine("Cleanup: "+ex.Message);} }
            Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_DIRECTORY",oldDirectory); Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_MANIFEST",oldManifest);
            Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DATUM_EVIDENCE",oldEvidence);
            Environment.SetEnvironmentVariable("ADDIN_BEND_PLACEMENT_CONFIG",oldPlacement);
        }
        Console.WriteLine("Library native checks: "+checks+"; passed: "+report.ContainsKey("passed")); return report.ContainsKey("passed")?0:1;
    }
}
