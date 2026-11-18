// Test-only COM add-in. Uses the exact SolidWorks instance supplied by ConnectToSW.
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;

[ComVisible(true), Guid("E41111BF-226C-454D-B9DF-DC4F46128663"), ClassInterface(ClassInterfaceType.None)]
public sealed class KadaiCadPackageHost : ISwAddin
{
    private ISldWorks app;
    private Timer timer;
    private string root;

    public bool ConnectToSW(object value, int cookie)
    {
        root = Path.GetDirectoryName(typeof(KadaiCadPackageHost).Assembly.Location);
        app = (ISldWorks)value;
        try
        {
            if (app.GetProcessID() != int.Parse(File.ReadAllText(Path.Combine(root, "expected-pid.txt"))))
                throw new InvalidOperationException("Refused to attach to a non-test SolidWorks process.");
            timer = new Timer { Interval = 500 };
            timer.Tick += RunRequest;
            timer.Start();
            File.WriteAllText(Path.Combine(root, "ready.txt"), app.GetProcessID().ToString());
            return true;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(root, "host-error.txt"), ex.ToString());
            return false;
        }
    }

    private void RunRequest(object sender, EventArgs e)
    {
        string request = Path.Combine(root, "request.txt");
        if (!File.Exists(request)) return;
        timer.Stop();
        string result = Path.Combine(root, "result.txt");
        try
        {
            string[] args = File.ReadAllLines(request);
            File.Move(request, Path.Combine(root, "request-" + DateTime.UtcNow.Ticks + ".txt"));
            if (args.Length < 2) throw new ArgumentException("Specify copied Part and new output folder.");
            var documents = (app.GetDocuments() as object[] ?? new object[0]).OfType<ModelDoc2>();
            if (documents.Any(doc => !string.Equals(doc.GetPathName(), args[0], StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The test session contains an unrelated document.");
            File.WriteAllText(result, "RUNNING");
            var stdout = Console.Out;
            var stderr = Console.Error;
            int exitCode;
            using (var log = new StreamWriter(Path.Combine(root, "run.log")) { AutoFlush = true })
            {
                try
                {
                    Console.SetOut(log);
                    Console.SetError(log);
                    exitCode = KadaiCadTests.RunOnApplication(app, args);
                }
                finally { Console.SetOut(stdout); Console.SetError(stderr); }
            }
            File.WriteAllText(result, "EXIT " + exitCode);
        }
        catch (Exception ex) { File.WriteAllText(result, ex.ToString()); }
        finally { timer.Start(); }
    }

    public bool DisconnectFromSW()
    {
        timer?.Dispose();
        timer = null;
        app = null;
        return true;
    }
}
