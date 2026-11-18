// Small typed STA client for local validation; no Save operation is exposed.
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
public static class KadaiSwControl
{
    [STAThread] public static int Main(string[] args)
    {
        try {
            var sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
            if (args[0] == "identity" || args[0] == "exit-isolated") {
                int expectedPid;
                if (args.Length != 2 || !int.TryParse(args[1], out expectedPid) || sw.GetProcessID() != expectedPid)
                    throw new InvalidOperationException("SolidWorks PID does not match the explicitly designated session.");
                var documents = sw.GetDocuments() as object[] ?? new object[0];
                Console.WriteLine("PID=" + sw.GetProcessID() + "; UserControl=" + sw.UserControl + "; Documents=" + documents.Length);
                if (args[0] == "exit-isolated") {
                    if (documents.Length != 0) throw new InvalidOperationException("Refusing to exit a session with open documents.");
                    sw.ExitApp();
                }
                return 0;
            }
            if (args[0] == "load") { Console.WriteLine("LoadAddIn=" + sw.LoadAddIn(args[1])); return 0; }
            if (args[0] == "unload") { Console.WriteLine("UnloadAddIn=" + sw.UnloadAddIn(args[1])); return 0; }
            if (args[0] == "state") {
                foreach (var d in (sw.GetDocuments() as object[] ?? new object[0]).OfType<ModelDoc2>())
                    Console.WriteLine(d.GetType() + " | " + d.GetTitle() + " | " + d.GetPathName());
                return 0;
            }
            if (args[0] == "close-test-package") {
                string root = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)).StartsWith("native-prototype-", StringComparison.Ordinal))
                    throw new Exception("Cleanup only accepts a native-prototype-* package directory.");
                var documents = (sw.GetDocuments() as object[] ?? new object[0]).OfType<ModelDoc2>()
                    .Where(d => d.GetPathName().StartsWith(root, StringComparison.OrdinalIgnoreCase)).OrderByDescending(d => d.GetType()).ToList();
                foreach (var d in documents) { Console.WriteLine("Close test copy " + d.GetPathName()); sw.CloseDoc(d.GetPathName()); }
                // Also close referenced/hidden models by their unambiguous full path.
                foreach (string extension in new[] { "*.SLDDRW", "*.SLDASM", "*.SLDPRT" })
                    foreach (string path in Directory.GetFiles(root, extension)) sw.CloseDoc(path);
                return 0;
            }
            if (args[0] == "prepare") {
                foreach (string p in Directory.GetFiles(args[1], "KADAI_TEST__*.SLDASM")) {
                    var deps = sw.GetDocumentDependencies2(p, false, true, false) as string[] ?? new string[0];
                    for (int i = 1; i < deps.Length; i += 2) {
                        string replacement = Path.Combine(args[1], "KADAI_TEST__" + Path.GetFileName(deps[i]));
                        if (File.Exists(replacement)) Console.WriteLine(Path.GetFileName(p) + " relink " + Path.GetFileName(deps[i]) + "=" + sw.ReplaceReferencedDocument(p, deps[i], replacement));
                    }
                }
                return 0;
            }
            int error = 0, warning = 0;
            int type = args[1].EndsWith("SLDASM", StringComparison.OrdinalIgnoreCase) ? 2 : args[1].EndsWith("SLDDRW", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
            var doc = sw.GetOpenDocumentByName(args[1]) as ModelDoc2;
            if (doc == null) doc = sw.OpenDoc6(args[1], type, 3, "", ref error, ref warning) as ModelDoc2;
            if (doc == null) throw new Exception("Open failed " + error + "/" + warning);
            sw.ActivateDoc3(doc.GetTitle(), false, 0, ref error);
            Console.WriteLine("Active=" + doc.GetPathName() + " dirty=" + doc.GetSaveFlag());
            if (args.Length > 2) {
                Feature f = null; for (Feature candidate = doc.FirstFeature() as Feature; candidate != null; candidate = candidate.GetNextFeature() as Feature) if (candidate.Name == args[2]) { f = candidate; break; }
                if (f == null || !f.Select2(false, 0)) throw new Exception("Feature not found.");
            }
            doc.ViewZoomtofit2(); return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}

