using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

internal static partial class NativeAssemblyBatchValidation
{
    // No registration/deployment, registry edits, user process termination or
    // relaxed deadline. Run one variant per fresh owned PID.
    private static int ShutdownAuditMain(string[] args)
    {
        if(args.Length!=3)return 2;
        string dll=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]),variant=args[2];
        if(Directory.Exists(output))throw new IOException("Fresh audit output required");
        Directory.CreateDirectory(output);ISldWorks app=null;ITaskpaneView view=null;
        var report=new Dictionary<string,object> {{"Variant",variant},{"RequestedDllSha256",Sha(dll).Replace("-","").ToLowerInvariant()},{"RequestedDllPath",dll}};
        int code=1;
        try
        {
            app=NativeTestProcess.StartOwned(output);report["OwnedPid"]=app.GetProcessID();report["SolidWorksRevision"]=app.RevisionNumber();
            if(variant=="lastdoc-old"||variant=="lastdoc-controlled")
            {
                string fixture=System.Environment.GetEnvironmentVariable("ADDIN_NATIVE_CLOSE_PROBE_PART");
                if(string.IsNullOrWhiteSpace(fixture)||!Path.IsPathRooted(fixture)||!File.Exists(fixture))throw new InvalidOperationException("Explicit existing read-only fixture required");
                string before=Sha(fixture);int e=0,w=0;
                var part=app.OpenDoc6(fixture,1,3,"",ref e,ref w) as ModelDoc2;Check(part!=null,"Open existing controlled lifetime probe");
                if(variant=="lastdoc-controlled")NativeTestProcess.KeepOwnedBackgroundSession(app);else {app.Visible=false;app.UserControl=false;}
                report["DocumentsBeforeClose"]=app.GetDocumentCount();Check(app.GetDocumentCount()==1,"Last-document control is isolated");
                app.CloseDoc(part.GetTitle());part=null;
                var settle=System.Diagnostics.Stopwatch.StartNew();while(settle.ElapsedMilliseconds<1500){NativeTestProcess.PumpMessages();System.Threading.Thread.Sleep(5);}
                try
                {
                    var reopened=app.OpenDoc6(fixture,1,3,"",ref e,ref w) as ModelDoc2;
                    report["ReopenedSameOwnedPid"]=reopened!=null&&app.GetProcessID()==(int)report["OwnedPid"];
                    if(variant=="lastdoc-controlled")Check(reopened!=null&&app.UserControlBackground,"Controlled invisible host survives zero documents");
                }
                catch(COMException ex)
                {report["ReopenRpcHResult"]=ex.HResult;if(variant=="lastdoc-controlled")throw;report["OldLifetimeFailureReproduced"]=true;}
                Check(Sha(fixture)==before,"Probe leaves original fixture bytes unchanged");
            }
            else if(variant=="load-build")
            {
                string installed=System.Environment.GetEnvironmentVariable("ADDIN_NATIVE_INSTALLED_DLL");
                if(string.IsNullOrWhiteSpace(installed)||!Path.IsPathRooted(installed))throw new InvalidOperationException("Explicit installed path required for owned-session unload");
                report["UnloadInstalledResult"]=app.UnloadAddIn(installed);
                report["LoadBuildResult"]=app.LoadAddIn(dll);
                // A success code alone does not prove which managed assembly
                // COM activated. Record the physical module SHA separately.
                NativeTestProcess.RecordLoadedBinaries(output,"after-load");
                var observation=Json.DeserializeObject(File.ReadAllText(Path.Combine(output,"PRIVATE-loaded-binaries-after-load.json"))) as Dictionary<string,object>;
                report["RequestedModuleObserved"]=((object[])observation["Modules"]).Cast<Dictionary<string,object>>().Any(m=>
                    string.Equals(Convert.ToString(m["Path"]),dll,StringComparison.OrdinalIgnoreCase)&&Convert.ToString(m["Sha256"])==Convert.ToString(report["RequestedDllSha256"]));
                object addin=app.GetAddInObject("{D1C7F4B2-9A1E-4B5C-B234-A1B2C3D4E5F6}");
                try {report["InProcessIdentity"]=addin?.GetType().InvokeMember("GetBendCheckBinaryIdentity",BindingFlags.InvokeMethod,null,addin,new object[0]);}
                catch(Exception ex){report["InProcessIdentityUnavailable"]=ex.GetType().Name;}
                string fixture=System.Environment.GetEnvironmentVariable("ADDIN_NATIVE_CLOSE_PROBE_PART");
                if(!string.IsNullOrWhiteSpace(fixture))
                {
                    if(!Path.IsPathRooted(fixture)||!File.Exists(fixture))throw new InvalidOperationException("Explicit existing read-only A/B fixture required");
                    NativeTestProcess.KeepOwnedBackgroundSession(app);
                    string before=Sha(fixture);int error=0,warning=0;
                    var part=app.OpenDoc6(fixture,1,3,"",ref error,ref warning) as ModelDoc2;
                    Check(part!=null,"Owned A/B opens same existing Part");
                    report["FixtureFeatureCount"]=part.FeatureManager.GetFeatureCount(true);
                    report["FixtureSha256"]=before;report["FixtureWasDirty"]=part.GetSaveFlag();
                    app.CloseDoc(part.GetTitle());part=null;
                    Check(Sha(fixture)==before,"Owned A/B leaves original fixture bytes unchanged");
                    report["FixtureUnchanged"]=true;
                    // This checks load/open/close/lifetime. It does not claim
                    // that a command in the loaded add-in actually ran.
                    report["FixtureScope"]="ReadOnlyOpenClose_NotCommandExecution";
                }
            }
            else if(variant=="pane-release"||variant=="pane-no-release")
            {
                view=app.CreateTaskpaneView2("","OWNED LIFETIME TEST");
                Check(view!=null,"Owned native view created");view.DeleteView();
                if(variant=="pane-release")Marshal.ReleaseComObject(view);
                view=null;
            }
            else if(variant!="startup")throw new InvalidOperationException("Unknown controlled audit variant");
            report["FunctionalResult"]="PASS";code=0;
        }
        catch(Exception ex){report["FunctionalResult"]="FAIL";report["Error"]=ex.ToString();}
        finally
        {
            view=null;
            if(app!=null)try{NativeTestProcess.RestoreCommandScope(app);app.ExitApp();}catch(Exception ex){report["ShutdownError"]=ex.ToString();}
            int? exit=NativeTestProcess.WaitForOwnedExit(10000);report["ShutdownExitCode"]=exit;
            report["ShutdownResult"]=app==null?"NOT_RUN":exit==0&&!report.ContainsKey("ShutdownError")?"PASS":exit.HasValue?"FAIL":"NOT_CONFIRMED";
            report["Result"]=code==0&&Equals(report["ShutdownResult"],"PASS")?"PASS":"FAIL";
            if(!Equals(report["Result"],"PASS"))code=1;
            File.WriteAllText(Path.Combine(output,"PRIVATE-shutdown-audit.json"),Json.Serialize(report));
        }
        Console.WriteLine("SHUTDOWN "+variant+" functional="+report["FunctionalResult"]+" lifecycle="+report["ShutdownResult"]);return code;
    }
}
