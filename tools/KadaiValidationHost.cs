// Optional isolated SW add-in for validation. Register ONLY this unique test
// assembly, never the candidate ADDIN assembly (which has production ProgIDs).
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Linq;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;

[ComVisible(true), Guid("C572CE87-5CF8-42AF-AB27-630804E55BA8"), ProgId("Kadai.ValidationHost"), ClassInterface(ClassInterfaceType.AutoDual)]
public sealed class KadaiValidationHost : ISwAddin
{
    ISldWorks app; Control control; TaskpaneView pane; Timer timer;
    string root;
    public bool ConnectToSW(object value, int cookie)
    {
        app = (ISldWorks)value; app.SetAddinCallbackInfo2(0, this, cookie);
        root = Path.GetDirectoryName(typeof(KadaiValidationHost).Assembly.Location);
        try {
            pane = app.CreateTaskpaneView2(Path.Combine(root, "icons20.png"), "Kadai Branch Verification");
            if (File.Exists(Path.Combine(root, "headless.flag"))) { control = new Panel(); control.CreateControl(); }
            else CreateCandidate(Path.Combine(root, "ADDIN.Kadai.BranchTest.dll"));
            timer = new Timer { Interval = 500 }; timer.Tick += Tick; timer.Start();
            File.WriteAllText(Path.Combine(root, "host-ready.txt"), "SW UI thread=" + System.Threading.Thread.CurrentThread.ManagedThreadId);
            return true;
        } catch (Exception e) { File.WriteAllText(Path.Combine(root, "host-error.txt"), e.ToString()); return false; }
    }
    void CreateCandidate(string path)
    {
        if (control != null) {
            var shutdown = control.GetType().GetMethod("ShutdownFromSolidWorks");
            if (shutdown != null) shutdown.Invoke(control, null); control.Dispose();
        }
        var candidate = Assembly.LoadFrom(path);
        candidate.GetType("ADDIN.SwAddin").GetField("InstanceSwApp").SetValue(null, app);
        control = (Control)Activator.CreateInstance(candidate.GetType("ADDIN.BomTaskPaneControl"));
        control.GetType().GetMethod("Init").Invoke(control, new object[] { app }); control.CreateControl();
        if (!pane.DisplayWindowFromHandlex64(control.Handle.ToInt64())) throw new Exception("Cannot host candidate task pane.");
        pane.ShowView();
    }
    void Tick(object sender, EventArgs args)
    {
        string request = Path.Combine(root, "request.json");
        if (!File.Exists(request)) return;
        timer.Stop();
        try {
            var job = new JavaScriptSerializer().Deserialize<Job>(File.ReadAllText(request));
            File.Move(request, Path.Combine(root, "request-" + DateTime.UtcNow.Ticks + ".json"));
            File.WriteAllText(Path.Combine(root, "result.txt"), job.Mode + " RUNNING " + DateTime.UtcNow.ToString("O"));
            int code;
            if (job.Mode == "audit") code = KadaiNativeAudit.Main(job.Arguments);
            else if (job.Mode == "cad") code = KadaiCadTests.Main(job.Arguments);
            else if (job.Mode == "run") {
                var engine = Assembly.LoadFrom(job.Arguments[0]);
                var log = new StringWriter(); var originalOut = Console.Out; var originalError = Console.Error;
                try { Console.SetOut(log); Console.SetError(log);
                    code = (int)engine.GetType(job.Arguments[1]).GetMethod("Main").Invoke(null, new object[] { job.Arguments.Skip(2).ToArray() });
                } finally { Console.SetOut(originalOut); Console.SetError(originalError);
                    File.WriteAllText(Path.Combine(root, "run-log.txt"), log.ToString()); }
            }
            else if (job.Mode == "switchui") { CreateCandidate(job.Arguments[0]); code = 0; }
            else if (job.Mode == "cycles") {
                var button = control.Controls.Find("btnKadai", true).Single() as Button;
                if (button == null) throw new Exception("Kadai button missing.");
                for (int i = 0; i < 35; i++) {
                    button.PerformClick();
                    var field = control.GetType().GetField("kadaiForm", BindingFlags.NonPublic | BindingFlags.Instance);
                    var form = field.GetValue(control) as Form;
                    if (form == null || !form.Visible) throw new Exception("Kadai launcher failed at cycle " + i);
                    form.Close(); if (!form.IsDisposed) throw new Exception("Kadai form not disposed.");
                }
                code = 0;
            } else throw new Exception("Unknown validation mode.");
            File.WriteAllText(Path.Combine(root, "result.txt"), job.Mode + " exit=" + code);
        } catch (Exception e) { File.WriteAllText(Path.Combine(root, "result.txt"), e.ToString()); }
        finally { timer.Start(); }
    }
    public bool DisconnectFromSW()
    {
        if (timer != null) { timer.Stop(); timer.Dispose(); }
        if (control != null) {
            var shutdown = control.GetType().GetMethod("ShutdownFromSolidWorks");
            if (shutdown != null) shutdown.Invoke(control, null);
            control.Dispose();
        }
        if (pane != null) pane.DeleteView(); app = null; return true;
    }
    public sealed class Job { public string Mode; public string[] Arguments; }
}
