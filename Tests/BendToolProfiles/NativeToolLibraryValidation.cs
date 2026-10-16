using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
        string oldDirectory=Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_DIRECTORY"), oldManifest=Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_MANIFEST");
        try
        {
            product=Assembly.LoadFrom(Path.GetFullPath(args[0])); output=Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            string original=Path.GetFullPath(args[2]), hash=Sha(original), copy=Path.Combine(output,"PRIVATE-library-test.SLDPRT");
            File.Copy(original,copy,true); File.SetAttributes(copy,FileAttributes.Normal);
            Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_DIRECTORY",Path.GetFullPath(args[3]));
            Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_MANIFEST",null);
            var before=Process.GetProcessesByName("SLDWORKS").Select(p=>p.Id).ToArray();
            app=(ISldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application.32")); int pid=app.GetProcessID();
            owned=!before.Contains(pid); Check(owned,"New owned API process required"); app.Visible=false; app.UserControl=false;
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
                report["initial"]=Snapshot(form,model,true);
                string min=Json.Serialize(report["initial"]); string minText=Get(item,"MinimumText").ToString();
                Action<string> source=directory=> {Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_DIRECTORY",directory); Call(worker,"Refresh");};
                string missing=Path.Combine(output,"missing-company-source"); source(missing);
                Until(()=> {Call(form,"Tick"); return count()==0 && !ReferenceEquals(library(),initial);},"Missing directory yields explicit unverified snapshot");
                Check(!(bool)Get(initial,"Current"),"Old source snapshot invalidated");
                Check(Get(item,"Error")==null && (bool)Get(item,"Current") && Get(item,"MinimumText").ToString()==minText,"Missing DXFs do not damage Min");
                Check(ReferenceEquals(item,Items(Get(session,"Items")).Single()),"No second COM capture on source update");
                Check((int)Get(Get(item,"Context"),"GeometryReadCount")==1,"One model geometry capture");
                Check(Json.Serialize(Snapshot(form,model,true))==min,"Min/planning unchanged by missing source");
                Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_MANIFEST",Path.Combine(output,"missing-manifest.json")); source(args[3]);
                Until(()=> {Call(form,"Tick"); return Get(library(),"Error")!=null && Get(library(),"Error").ToString().Contains("manifest");},"Bad manifest reports separate library error");
                Check(Get(item,"Error")==null && Json.Serialize(Snapshot(form,model,true))==min,"Bad manifest does not block Check Min");
                Environment.SetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_MANIFEST",null); source(args[3]);
                Until(()=> {Call(form,"Tick"); return count()==18;},"Library recovers after source restore");
                var listener=new FailingLibraryListener(); Debug.Listeners.Add(listener);
                try
                {
                    source(missing);
                    Until(()=> {Call(form,"Tick"); return count()==0;},"Diagnostic failure injection update");
                    Check(listener.Failures==1 && Convert.ToString(Get(session,"ToolLibraryDiagnosticError")).Contains("Injected"),"Library diagnostic failure isolated");
                    Check(Get(item,"Error")==null && Get(item,"MinimumText").ToString()==minText,"Diagnostic listener cannot change Min");
                }
                finally {Debug.Listeners.Remove(listener);}
                source(args[3]); Until(()=> {Call(form,"Tick"); return count()==18;},"Final library restore");
                Check(model.GetUpdateStamp()==stamp && model.GetSaveFlag()==dirty && model.ConfigurationManager.ActiveConfiguration.Name==config,"Library I/O preserves model stamp/config/dirty");
                report["libraryDebug"]=Json.DeserializeObject((string)Call(library(),"ToDebugJson"));
                model.ForceRebuild3(false); Call(form,"Tick");
                Check(!(bool)Get(item,"Current") && Get(item,"ToolProfiles")==null,"Stale model no longer exposes a tool snapshot link");
                Until(()=>Scanned(form) && !ReferenceEquals(item,Items(Get(session,"Items")).Single()),"Existing rebuild auto-rescan");
                Check(Items(Get(Items(Get(session,"Items")).Single(),"Rows")).Length==initialBends,"Rebuild retains actual sample bend count");
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
        }
        Console.WriteLine("Library native checks: "+checks+"; passed: "+report.ContainsKey("passed")); return report.ContainsKey("passed")?0:1;
    }
}
