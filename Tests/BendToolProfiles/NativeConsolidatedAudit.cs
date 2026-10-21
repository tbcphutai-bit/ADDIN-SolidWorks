using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;

internal static partial class NativeAssemblyBatchValidation
{
    private static int InprocessAddinMain(string[] args)
    {
        if(args.Length!=5&&args.Length!=6)return 2;
        string dll=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]),bridge=Path.GetFullPath(args[2]),fixture=Path.GetFullPath(args[3]);
        if(Directory.Exists(output))throw new IOException("Fresh private output required");Directory.CreateDirectory(output);
        const string id="{6AFCF01D-40B5-46EE-8C93-86B42821DF67}",keyPath=@"Software\Classes\CLSID\"+id;
        const string progid="CheckChan.PrivateAcceptanceHost",progKey=@"Software\Classes\CheckChan.PrivateAcceptanceHost";
        var report=new Dictionary<string,object>();ISldWorks app=null;ITaskpaneView bridgeView=null;object activation=null;int code=1;bool created=false,createdProg=false;
        try
        {
            var cases=args.Length==6?CheckChan.NativeAcceptance.RepresentativeAcceptanceInput.Read(args[5]):null;
            string comControl=CheckChan.NativeAcceptance.RepresentativeAcceptanceInput.ComControlMode(System.Environment.GetEnvironmentVariable("ADDIN_ACCEPTANCE_COM_CONTROL"));
            if(comControl!=null&&cases==null)throw new InvalidDataException("COM controls require the protected native manifest");
            int start=CheckChan.NativeAcceptance.RepresentativeAcceptanceInput.StartIndexAfterXl02(System.Environment.GetEnvironmentVariable("ADDIN_ACCEPTANCE_PASSED_XL02_AUDIT"),dll);
            int requested=System.Environment.GetEnvironmentVariable("ADDIN_ACCEPTANCE_CLOSE_REPRO")=="1"?1:cases==null?1:cases.Cases.Length-start;
            if(comControl!=null){start=comControl=="A"?2:1;requested=comControl=="A"?1:2;report["ComControl"]=comControl;}
            report["StartCaseIndex"]=start;
            report["RequestedCases"]=requested;
            if(cases!=null)report["RepresentativeCases"]=cases.Cases.Select(c=>new {c.CaseId,c.Path,Execution="NOT_RUN"}).ToArray();
            using(var existing=Registry.CurrentUser.OpenSubKey(keyPath))if(existing!=null)throw new InvalidOperationException("Refuse to replace any existing COM registration");
            using(var existing=Registry.CurrentUser.OpenSubKey(progKey))if(existing!=null)throw new InvalidOperationException("Refuse to replace any existing ProgID registration");
            var name=AssemblyName.GetAssemblyName(bridge);
            using(var key=Registry.CurrentUser.CreateSubKey(keyPath))
            {
                created=true;key.SetValue("","Private Check Chấn acceptance test");
                using(var server=key.CreateSubKey("InprocServer32"))
                {server.SetValue("","mscoree.dll");server.SetValue("ThreadingModel","Both");server.SetValue("Class","CheckChan.NativeAcceptance.AcceptanceAddin");server.SetValue("Assembly",name.FullName);server.SetValue("RuntimeVersion","v4.0.30319");server.SetValue("CodeBase",new Uri(bridge).AbsoluteUri);}
                using(var p=key.CreateSubKey("ProgId"))p.SetValue("",progid);
            }
            using(var key=Registry.CurrentUser.CreateSubKey(progKey))
            {createdProg=true;using(var c=key.CreateSubKey("CLSID"))c.SetValue("",id);}
            System.Environment.SetEnvironmentVariable("ADDIN_ACCEPTANCE_HOST_DIRECTORY",output);
            System.Environment.SetEnvironmentVariable("ADDIN_ACCEPTANCE_TARGET_DLL",dll);
            System.Environment.SetEnvironmentVariable("ADDIN_ACCEPTANCE_FIXTURE",fixture);
            System.Environment.SetEnvironmentVariable("ADDIN_ACCEPTANCE_CASES",args.Length==6?Path.GetFullPath(args[5]):null);
            app=NativeTestProcess.StartOwned(output);NativeTestProcess.KeepOwnedBackgroundSession(app);report["OwnedPid"]=app.GetProcessID();
            if(args[4]=="unload")
            {
                var isolation=new List<object>();using(var addins=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SolidWorks\Addins"))
                foreach(string guid in addins.GetSubKeyNames())
                {
                    using(var registration=Registry.ClassesRoot.OpenSubKey(@"CLSID\"+guid+@"\InprocServer32"))
                    {
                        string path=Convert.ToString(registration?.GetValue("CodeBase"));if(path.StartsWith("file:",StringComparison.OrdinalIgnoreCase))path=new Uri(path).LocalPath;
                        if(string.IsNullOrWhiteSpace(path))path=Convert.ToString(registration?.GetValue(""));
                        if(!Path.IsPathRooted(path)||!File.Exists(path))continue;
                        isolation.Add(new{Guid=guid,Path=path,Result=app.UnloadAddIn(path)});
                    }
                }
                report["SessionOnlyUnload"]=isolation;File.WriteAllText(Path.Combine(output,"PRIVATE-isolation.json"),Json.Serialize(isolation));
            }
            report["LoadBridgeResult"]=app.LoadAddIn(bridge);File.WriteAllText(Path.Combine(output,"PRIVATE-host-progress.json"),Json.Serialize(report));
            string events=Path.Combine(output,"PRIVATE-inprocess-events.json");var watch=Stopwatch.StartNew();Dictionary<string,object> state=null;
            if(!File.Exists(events))
            {
                // LoadAddIn alone is not proof of managed activation. This
                // explicit test-control bridge invokes the real target methods
                // in-process without changing its installed registration.
                report["ActivationScope"]="ExplicitTaskPaneBridge_DirectTargetLifecycle_NoRegisteredAddinCookie";
                bridgeView=app.CreateTaskpaneView2("","PRIVATE CHECK CHAN ACCEPTANCE");activation=bridgeView.AddControl(progid,"");
                if(activation==null)throw new InvalidOperationException("INPROCESS_BRIDGE_ACTIVATION_FAILED");
                report["BridgeConnectReturned"]=activation.GetType().InvokeMember("ConnectToSW",BindingFlags.InvokeMethod,null,activation,new object[]{app,0});
            }
            // Same 60 s diagnostic limit per case, not a larger solver budget.
            while(watch.ElapsedMilliseconds<65000*requested)
            {
                if(File.Exists(events))
                {
                    try{state=Json.Deserialize<Dictionary<string,object>>(CheckChan.NativeAcceptance.AcceptanceEventFile.Read(events));}catch(IOException){}
                    if(state!=null&&(Convert.ToString(state["Stage"])=="COMMAND_COMPLETED"||Convert.ToString(state["Stage"])=="FAIL"))break;
                }
                // A dead owned server cannot complete the remaining cases.
                // Preserve its last event/exit code instead of waiting out
                // every NOT RUN case or looking for another ROT session.
                int? earlyExit=NativeTestProcess.WaitForOwnedExit(0);
                if(earlyExit.HasValue){report["OwnedExitBeforeCompletion"]=earlyExit;break;}
                NativeTestProcess.PumpMessages();Thread.Sleep(20);
            }
            report["InprocessResult"]=state;
            if(state!=null&&state.ContainsKey("RepresentativeCases"))report["RepresentativeCases"]=state["RepresentativeCases"];
            bool function=state!=null&&Convert.ToString(state["Stage"])=="COMMAND_COMPLETED"&&Convert.ToInt32(state["Pid"])==app.GetProcessID()&&Convert.ToString(state["DllSha256"])==Sha(dll).Replace("-","").ToLowerInvariant();
            if(comControl=="A")function=state!=null&&Convert.ToString(state["Stage"])=="COMMAND_COMPLETED"&&Convert.ToInt32(state["Pid"])==app.GetProcessID()&&
                state.ContainsKey("NoProductControl")&&Equals(state["NoProductControl"],true)&&state.ContainsKey("ProductAssemblyLoaded")&&Equals(state["ProductAssemblyLoaded"],false);
            report["FunctionalResult"]=function?"PASS":"FAIL";code=function?0:1;
            report["UnloadBridgeResult"]=app.UnloadAddIn(bridge);
            if(activation!=null)report["BridgeDisconnectReturned"]=activation.GetType().InvokeMember("DisconnectFromSW",BindingFlags.InvokeMethod,null,activation,new object[0]);
            if(bridgeView!=null){bridgeView.DeleteView();bridgeView=null;}activation=null;
            if(File.Exists(events))
            {
                var disconnected=Json.Deserialize<Dictionary<string,object>>(CheckChan.NativeAcceptance.AcceptanceEventFile.Read(events));report["DisconnectResult"]=disconnected;
                bool detach=comControl=="A"?disconnected.ContainsKey("NoProductControl")&&Equals(disconnected["NoProductControl"],true)&&
                    disconnected.ContainsKey("ProductAssemblyLoaded")&&Equals(disconnected["ProductAssemblyLoaded"],false):
                    disconnected.ContainsKey("TargetDisconnectReturned")&&Equals(disconnected["TargetDisconnectReturned"],true);
                if(!detach||Convert.ToString(disconnected["Stage"])!="TARGET_DISCONNECTED")
                {report["FunctionalResult"]="FAIL";code=1;}
            }
            else {report["FunctionalResult"]="FAIL";code=1;}
            NativeTestProcess.RecordLoadedBinaries(output,"after-inprocess-command");
        }
        catch(Exception ex){code=1;report["FunctionalResult"]="FAIL";report["Error"]=ex.ToString();}
        finally
        {
            if(app!=null)try{NativeTestProcess.RestoreCommandScope(app);app.ExitApp();}catch(Exception ex){report["ShutdownError"]=ex.ToString();}
            int? exit=NativeTestProcess.WaitForOwnedExit(10000);report["ShutdownExitCode"]=exit;report["ShutdownResult"]=exit==0&&!report.ContainsKey("ShutdownError")?"PASS":exit.HasValue?"FAIL":"NOT_CONFIRMED";
            if(created)
            {
                using(var server=Registry.CurrentUser.OpenSubKey(keyPath+@"\InprocServer32"))
                    if(Convert.ToString(server?.GetValue("CodeBase"))!=new Uri(bridge).AbsoluteUri)throw new InvalidOperationException("Refuse registry cleanup after unexpected identity change");
                Registry.CurrentUser.DeleteSubKeyTree(keyPath);report["PrivateRegistrationRemoved"]=true;
            }
            if(createdProg)
            {using(var key=Registry.CurrentUser.OpenSubKey(progKey+@"\CLSID"))if(Convert.ToString(key?.GetValue(""))!=id)throw new InvalidOperationException("Private ProgID changed unexpectedly");Registry.CurrentUser.DeleteSubKeyTree(progKey);}
            report["Result"]=code==0&&Equals(report["ShutdownResult"],"PASS")?"PASS":"FAIL";File.WriteAllText(Path.Combine(output,"PRIVATE-inprocess-audit.json"),Json.Serialize(report));if(!Equals(report["Result"],"PASS"))code=1;
        }
        Console.WriteLine("INPROCESS-ADDIN functional="+report["FunctionalResult"]+" shutdown="+report["ShutdownResult"]);return code;
    }
}
