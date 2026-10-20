// Test-only bridge. A unique temporary COM class activates inside the owned
// SOLIDWORKS process; production registrations and startup flags are untouched.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;

namespace CheckChan.NativeAcceptance
{
    [ComVisible(true),Guid("6AFCF01D-40B5-46EE-8C93-86B42821DF67"),ProgId("CheckChan.PrivateAcceptanceHost"),ClassInterface(ClassInterfaceType.AutoDual)]
    public sealed class AcceptanceAddin:UserControl,ISwAddin
    {
        private ISldWorks app;private object target;private Assembly assembly;private System.Windows.Forms.Timer timer;
        private string root,fixture,before;private TraceListener listener;private readonly Stopwatch clock=new Stopwatch();
        private Form window;private bool started;private readonly Dictionary<string,object> state=new Dictionary<string,object>();
        private static object Field(object x,string name)=>x.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(x);
        private static object Property(object x,string name)=>x.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(x);
        private static string Sha(string path){using(var h=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        private void Write(string stage)
        {
            state["Stage"]=stage;state["Pid"]=Process.GetCurrentProcess().Id;state["Apartment"]=Thread.CurrentThread.GetApartmentState().ToString();
            File.WriteAllText(Path.Combine(root,"PRIVATE-inprocess-events.json"),new JavaScriptSerializer().Serialize(state));
        }
        public bool ConnectToSW(object host,int cookie)
        {
            // An external IDispatch call may enter the CCW on an RPC MTA.
            // Dispatch to the already native-hosted control's owner; never
            // create an arbitrary STA thread or touch the host COM on the MTA.
            if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA||InvokeRequired)
            {
                if(!IsHandleCreated)throw new InvalidOperationException("HOST_CONTROL_STA_HANDLE_REQUIRED");
                BeginInvoke(new Action(()=>ConnectOwned(host,cookie)));return true;
            }
            return ConnectOwned(host,cookie);
        }
        private bool ConnectOwned(object host,int cookie)
        {
            root=System.Environment.GetEnvironmentVariable("ADDIN_ACCEPTANCE_HOST_DIRECTORY");
            if(string.IsNullOrWhiteSpace(root)||!Directory.Exists(root))return false;
            try
            {
                app=(ISldWorks)host;var owner=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"ownership.json")));
                int pid=Process.GetCurrentProcess().Id;
                if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA||pid!=Convert.ToInt32(owner["Pid"])||app.GetProcessID()!=pid)
                    throw new InvalidOperationException("OWNED_INPROCESS_STA_REQUIRED");
                Write("BRIDGE_CONNECT");
                listener=new TextWriterTraceListener(Path.Combine(root,"PRIVATE-target-trace.txt"));Trace.Listeners.Add(listener);Trace.AutoFlush=true;
                string dll=System.Environment.GetEnvironmentVariable("ADDIN_ACCEPTANCE_TARGET_DLL");fixture=System.Environment.GetEnvironmentVariable("ADDIN_ACCEPTANCE_FIXTURE");
                if(!Path.IsPathRooted(dll)||!File.Exists(dll)||!Path.IsPathRooted(fixture)||!File.Exists(fixture))throw new InvalidOperationException("EXPLICIT_PRIVATE_INPUTS_REQUIRED");
                assembly=Assembly.LoadFrom(dll);state["RequestedPath"]=dll;state["LoadedPath"]=assembly.Location;state["DllSha256"]=Sha(assembly.Location);state["Mvid"]=assembly.ManifestModule.ModuleVersionId;
                if(!string.Equals(dll,assembly.Location,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("TARGET_DLL_IDENTITY_MISMATCH");
                target=Activator.CreateInstance(assembly.GetType("ADDIN.SwAddin",true));Write("BEFORE_TARGET_CONNECT");
                state["TargetConnectReturned"]=target.GetType().GetMethod("ConnectToSW").Invoke(target,new[]{host,(object)cookie});
                state["TaskPaneControlType"]=Field(target,"uiControl")?.GetType().AssemblyQualifiedName;
                if(!Equals(state["TargetConnectReturned"],true)||Field(target,"uiControl")==null)throw new InvalidOperationException("TARGET_CONNECT_OR_TASKPANE_NOT_PROVEN");
                Write("TARGET_CONNECTED");
                timer=new System.Windows.Forms.Timer {Interval=100};timer.Tick+=Tick;clock.Start();timer.Start();return true;
            }
            catch(Exception ex){state["Error"]=ex.ToString();Write("FAIL");return false;}
        }
        private void Tick(object sender,EventArgs e)
        {
            // Native calls may pump host messages. Do not re-enter before the
            // first OpenDoc6/Click has produced a window and a controller.
            timer.Stop();
            try
            {
                if(clock.ElapsedMilliseconds>60000)throw new TimeoutException("Original 60s command diagnostic deadline expired");
                if(!started)
                {
                    started=true;before=Sha(fixture);int error=0,warning=0;
                    var model=app.OpenDoc6(fixture,1,3,"",ref error,ref warning) as ModelDoc2;
                    state["OpenError"]=error;state["OpenWarning"]=warning;if(model==null)throw new InvalidOperationException("READ_ONLY_FIXTURE_OPEN_FAILED");
                    var control=Field(target,"uiControl") as Control;var button=Field(control,"btnCheckBendMinimum") as Control;
                    if(button==null)throw new InvalidOperationException("EXISTING_COMMAND_BUTTON_MISSING");
                    // Invoke the actual existing Click handler on the host STA.
                    typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(button,new object[]{EventArgs.Empty});
                    window=Field(control,"bendCheckWindow") as Form;if(window==null||window.IsDisposed)throw new InvalidOperationException("EXISTING_CHECK_CHAN_COMMAND_DID_NOT_OPEN");
                    state["CommandWindowOpened"]=true;state["CommandWindowAssembly"]=window.GetType().Assembly.Location;
                    var run=window.Controls.Find("btnRunBatch",true).Single() as Button;run.PerformClick();Write("COMMAND_RUNNING");timer.Start();return;
                }
                if((bool)Property(window,"IsRunning")){timer.Start();return;}
                var parts=Field(window,"allParts") as IList;
                if(parts==null||parts.Count==0)throw new InvalidOperationException("COMMAND_FINISHED_WITHOUT_PART_RESULTS");
                int bends=0,candidates=0;var features=new List<int>();foreach(object part in parts)foreach(object bend in (IEnumerable)Property(part,"Bends"))
                {
                    var tools=((IEnumerable)Property(bend,"ToolCandidates")).Cast<object>().ToArray();
                    if(tools.Length!=18||tools.Select(t=>Convert.ToString(Property(t,"ToolId"))).Distinct(StringComparer.Ordinal).Count()!=18)
                        throw new InvalidOperationException("EXACTLY_18_UNIQUE_CANDIDATES_PER_NATIVE_BEND_REQUIRED");
                    bends++;candidates+=tools.Length;features.Add(Convert.ToInt32(Property(bend,"FeatureId")));
                }
                state["Parts"]=parts.Count;state["Bends"]=bends;state["Candidates"]=candidates;state["NativeFeatureIds"]=features;state["FixtureUnchanged"]=Sha(fixture)==before;
                if(bends==0||candidates!=18*bends||!Equals(state["FixtureUnchanged"],true))throw new InvalidOperationException("COMMAND_RESULT_OR_ORIGINAL_BYTE_INVARIANT_FAILED");
                timer.Stop();window.Close();state["FunctionalResult"]="PASS";Write("COMMAND_COMPLETED");
            }
            catch(Exception ex){timer.Stop();state["FunctionalResult"]="FAIL";state["Error"]=ex.ToString();Write("FAIL");}
        }
        public bool DisconnectFromSW()
        {
            if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA||InvokeRequired)
            {if(!IsHandleCreated)throw new InvalidOperationException("HOST_CONTROL_STA_HANDLE_REQUIRED");return (bool)Invoke(new Func<bool>(DisconnectOwned));}
            return DisconnectOwned();
        }
        private bool DisconnectOwned()
        {
            try
            {
                timer?.Stop();timer?.Dispose();window?.Close();Write("BEFORE_TARGET_DISCONNECT");
                if(target!=null)
                {
                    var control=Field(target,"uiControl") as Control;
                    state["TargetDisconnectReturned"]=target.GetType().GetMethod("DisconnectFromSW").Invoke(target,new object[0]);
                    state["TargetAppAndPaneDetached"]=Field(target,"swApp")==null&&Field(target,"uiControl")==null&&Field(target,"swTaskPane")==null&&
                        target.GetType().GetField("InstanceSwApp",BindingFlags.Static|BindingFlags.Public).GetValue(null)==null&&control?.IsDisposed==true;
                    state["BorrowedHostStillUsable"]=app.GetProcessID()==Process.GetCurrentProcess().Id;
                    if(!Equals(state["TargetAppAndPaneDetached"],true)||!Equals(state["BorrowedHostStillUsable"],true))throw new InvalidOperationException("TARGET_DETACH_OR_BORROWED_HOST_CONTRACT_FAILED");
                }
                Write("TARGET_DISCONNECTED");return true;
            }
            catch(Exception ex){state["DisconnectError"]=ex.ToString();Write("DISCONNECT_FAILED");return false;}
            finally{if(listener!=null){Trace.Listeners.Remove(listener);listener.Dispose();}target=null;app=null;}
        }
    }
}
