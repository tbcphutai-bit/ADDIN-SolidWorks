// Optional diagnostic add-in. Never register the production ADDIN assembly for this test.
// Detaches only the existing MARK observer in one empty, explicitly designated test process.
// It does not edit DLLs, CAD, feature definitions or startup settings.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;

[ComVisible(true), Guid("498861B9-CC08-4EDC-8F89-AAB32448DE79"),
 ProgId("Kadai.MarkIsolationHost"), ClassInterface(ClassInterfaceType.None)]
public sealed class KadaiMarkIsolationHost : ISwAddin
{
    public bool ConnectToSW(object value, int cookie)
    {
        string root = Path.GetDirectoryName(typeof(KadaiMarkIsolationHost).Assembly.Location);
        string log = Path.Combine(root, "mark-isolation-result.txt");
        try {
            int expectedPid = int.Parse(File.ReadAllText(Path.Combine(root, "expected-pid.txt")).Trim());
            var app = (ISldWorks)value;
            if (Process.GetCurrentProcess().Id != expectedPid || app.GetProcessID() != expectedPid)
                throw new InvalidOperationException("Wrong test process.");
            if ((app.GetDocuments() as object[] ?? new object[0]).Length != 0)
                throw new InvalidOperationException("The test process must have no open documents.");
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "ADDIN");
            var type = assembly.GetType("ADDIN.Helpers.MarkHighlightManager", true);
            var current = type.GetField("current", BindingFlags.Static | BindingFlags.NonPublic);
            object observer = current.GetValue(null);
            if (observer == null) throw new InvalidOperationException("No existing MARK observer to isolate.");
            type.GetMethod("Detach", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(observer, null);
            if (current.GetValue(null) != null) throw new InvalidOperationException("MARK observer still attached.");
            File.WriteAllText(log, "PASS PID=" + expectedPid + "; MARK observer detached; assembly=" + assembly.Location);
            return true;
        } catch (Exception e) {
            File.WriteAllText(log, "FAIL " + e);
            return false;
        }
    }

    // The process is disposable. A fresh process restores the unmodified add-in's normal observer.
    public bool DisconnectFromSW() { return true; }
}
