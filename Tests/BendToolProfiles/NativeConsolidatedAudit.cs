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
        if(args.Length!=5)return 2;
        string dll=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]),bridge=Path.GetFullPath(args[2]),fixture=Path.GetFullPath(args[3]);
        if(Directory.Exists(output))throw new IOException("Fresh private output required");Directory.CreateDirectory(output);
        const string id="{6AFCF01D-40B5-46EE-8C93-86B42821DF67}",keyPath=@"Software\Classes\CLSID\"+id;
        const string progid="CheckChan.PrivateAcceptanceHost",progKey=@"Software\Classes\CheckChan.PrivateAcceptanceHost";
        var report=new Dictionary<string,object>();ISldWorks app=null;ITaskpaneView bridgeView=null;object activation=null;int code=1;bool created=false,createdProg=false;
        try
        {
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
            while(watch.ElapsedMilliseconds<65000)
            {
                if(File.Exists(events))
                {
                    try{state=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(events));}catch(IOException){}
                    if(state!=null&&(Convert.ToString(state["Stage"])=="COMMAND_COMPLETED"||Convert.ToString(state["Stage"])=="FAIL"))break;
                }
                NativeTestProcess.PumpMessages();Thread.Sleep(20);
            }
            report["InprocessResult"]=state;
            bool function=state!=null&&Convert.ToString(state["Stage"])=="COMMAND_COMPLETED"&&Convert.ToInt32(state["Pid"])==app.GetProcessID()&&Convert.ToString(state["DllSha256"])==Sha(dll).Replace("-","").ToLowerInvariant();
            report["FunctionalResult"]=function?"PASS":"FAIL";code=function?0:1;
            report["UnloadBridgeResult"]=app.UnloadAddIn(bridge);
            if(activation!=null)report["BridgeDisconnectReturned"]=activation.GetType().InvokeMember("DisconnectFromSW",BindingFlags.InvokeMethod,null,activation,new object[0]);
            if(bridgeView!=null){bridgeView.DeleteView();bridgeView=null;}activation=null;
            if(File.Exists(events))
            {
                var disconnected=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(events));report["DisconnectResult"]=disconnected;
                if(!disconnected.ContainsKey("TargetDisconnectReturned")||!Equals(disconnected["TargetDisconnectReturned"],true)||Convert.ToString(disconnected["Stage"])!="TARGET_DISCONNECTED")
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
