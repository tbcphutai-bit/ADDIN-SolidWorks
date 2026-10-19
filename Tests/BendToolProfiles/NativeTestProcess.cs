using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using Environment=System.Environment;

// Test infrastructure only. COM activation may return an already running user
// instance. Start our own executable and bind ONLY its exact ROT PID instead.
internal static class NativeTestProcess
{
    [DllImport("ole32.dll")] private static extern int CreateBindCtx(uint reserved,out IBindCtx context);
    internal static ISldWorks StartOwned(string output)
    {
        string executable=Environment.GetEnvironmentVariable("ADDIN_NATIVE_SOLIDWORKS_EXE")??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SOLIDWORKS Corp","SOLIDWORKS","SLDWORKS.exe");
        if(!File.Exists(executable))throw new FileNotFoundException("Set ADDIN_NATIVE_SOLIDWORKS_EXE to the installed executable");
        var process=Process.Start(new ProcessStartInfo(executable){UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden,
            RedirectStandardOutput=true,RedirectStandardError=true});
        // Drain owned server diagnostics without inheriting the harness pipes.
        // A lingering server must not make a finished test look still running.
        process.OutputDataReceived+=(s,e)=>{};process.ErrorDataReceived+=(s,e)=>{};
        process.BeginOutputReadLine();process.BeginErrorReadLine();
        DateTime start=process.StartTime.ToUniversalTime();Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"ownership.json"),new JavaScriptSerializer().Serialize(new {Owned=true,Pid=process.Id,StartUtc=start.ToString("o")}));
        var clock=Stopwatch.StartNew();
        while(clock.ElapsedMilliseconds<60000&&!process.HasExited)
        {
            var app=Find(process.Id);
            if(app!=null&&app.GetProcessID()==process.Id&&process.StartTime.ToUniversalTime()==start)return app;
            Application.DoEvents();Thread.Sleep(50);
        }
        throw new InvalidOperationException("Owned SOLIDWORKS PID did not register; no fallback to another session");
    }
    private static ISldWorks Find(int pid)
    {
        IBindCtx context=null;IRunningObjectTable table=null;IEnumMoniker enumeration=null;
        try
        {
            Marshal.ThrowExceptionForHR(CreateBindCtx(0,out context));context.GetRunningObjectTable(out table);table.EnumRunning(out enumeration);
            var next=new IMoniker[1];string expected="SolidWorks_PID_"+pid;
            while(enumeration.Next(1,next,IntPtr.Zero)==0)
            {
                try
                {
                    string name;next[0].GetDisplayName(context,null,out name);
                    if(name.IndexOf(expected,StringComparison.OrdinalIgnoreCase)<0)continue;
                    object value;table.GetObject(next[0],out value);var app=value as ISldWorks;
                    if(app!=null&&app.GetProcessID()==pid)return app;
                }
                finally{Marshal.ReleaseComObject(next[0]);}
            }
        }
        catch(COMException){ /* Registration can race startup. Retry same PID. */ }
        finally
        {if(enumeration!=null)Marshal.ReleaseComObject(enumeration);if(table!=null)Marshal.ReleaseComObject(table);if(context!=null)Marshal.ReleaseComObject(context);}
        return null;
    }
}
