// Compile with Commands/Kadai/*.cs and SW2024 interop. Output only to a NEW local folder.
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using ADDIN.Commands.Kadai;

public static class KadaiCadTests
{
    static object[] Items(object value) { return value as object[] ?? new object[0]; }
    static string Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 == args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Missing value for " + name);
        return args[index + 1];
    }
    [STAThread] public static int Main(string[] args)
    {
        try {
            if (args.Length < 2 || (Directory.Exists(args[1]) && !args.Contains("--assemble-only"))) throw new ArgumentException("Provide copied panel Part and a NEW output directory.");
            Directory.CreateDirectory(args[1]);
            var sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
            return RunOnApplication(sw, args);
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    public static int RunOnApplication(ISldWorks sw, string[] args)
    {
        try {
            string output = Path.GetFullPath(args[1]);
            int templateIndex = Array.IndexOf(args, "--assembly-template");
            string template = templateIndex >= 0 ? args[templateIndex + 1] : null;
            string materialDatabase = Option(args, "--material-db");
            string material = Option(args, "--material");
            string bendTable = Option(args, "--bend-table");
            int countIndex = Array.IndexOf(args, "--counts");
            int marginIndex = Array.IndexOf(args, "--margins");
            var request = new KadaiRequest { LongRibCount = countIndex >= 0 ? int.Parse(args[countIndex + 1]) : 3,
                CrossRibCount = countIndex >= 0 ? int.Parse(args[countIndex + 2]) : 1,
                OriginUmm = 0, OriginVmm = -1430, LengthXmm = 2250, LengthZmm = 1450,
                LongEndMarginMm = 50, CrossEndMarginMm = 150.9 };
            if (marginIndex >= 0) {
                var invariant = System.Globalization.CultureInfo.InvariantCulture;
                request.LongEndMarginMm = double.Parse(args[marginIndex + 1], invariant);
                request.CrossEndMarginMm = double.Parse(args[marginIndex + 2], invariant);
            }
            if (!args.Contains("--assemble-only") && File.Exists(Path.Combine(output, "PASS.txt")))
                throw new InvalidOperationException("Choose a fresh run directory.");
            Directory.CreateDirectory(output);
            if (args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
                var cached = new JavaScriptSerializer().Deserialize<List<KadaiProfile3D>>(File.ReadAllText(args[0]));
                var cachedWriter = new KadaiNativePrototype(sw, materialDatabase, material, bendTable);
                if (args.Contains("--assemble-only"))
                    cachedWriter.CreateAssembly(cached, output, request.BottomMm, template);
                else
                    cachedWriter.WriteNewPackage(KadaiPlanner.Build(request), cached, Path.Combine(output, "package"),
                        100, .1, args.Contains("--export"), template, Console.WriteLine);
                Console.WriteLine("PASS prototype assembly and BOM");
                File.WriteAllText(Path.Combine(output, "PASS.txt"), "Native prototype only; production gate closed.");
                return 0;
            }
            string input = Path.GetFullPath(args[0]);
            if (!Path.GetFileName(input).StartsWith("KADAI_TEST__", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use a copied CAD fixture with the KADAI_TEST__ prefix.");
            int e = 0, w = 0;
            var doc = sw.GetOpenDocumentByName(input) as ModelDoc2;
            bool openedHere = doc == null;
            if (openedHere) doc = sw.OpenDoc6(input, 1, 3, "", ref e, ref w) as ModelDoc2;
            if (doc == null || !string.Equals(doc.GetPathName(), input, StringComparison.OrdinalIgnoreCase)) throw new Exception("Wrong panel Part.");
            try {
            sw.ActivateDoc3(doc.GetTitle(), false, 0, ref e);
            // Test fixture selection: largest single face of the lowest surface body.
            var panel = Items(((PartDoc)doc).GetBodies2(1, false)).OfType<Body2>()
                .OrderBy(b => ((double[])b.GetBodyBox())[1]).First();
            var face = Items(panel.GetFaces()).OfType<Face2>().OrderByDescending(f => f.GetArea()).First();
            doc.ClearSelection2(true); ((Entity)face).Select4(false, null);
            bool dirty = doc.GetSaveFlag(); int stamp = doc.GetUpdateStamp(), features = doc.GetFeatureCount();
            using (var backend = new KadaiPreviewBackend(sw)) {
                var source = backend.CaptureSource(sw, doc);
                var plan = KadaiPlanner.Build(request);
                var profiles = backend.CalculateProfiles(plan, source);
                File.WriteAllText(Path.Combine(args[1], "profiles.json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(profiles));
                backend.ShowTemporaryPreview(doc, plan, profiles); Console.WriteLine("PREVIEW bodies=" + backend.VisibleBodyCount);
                doc.ShowNamedView2("*Isometric", 7); doc.ViewZoomtofit2();
                if (args.Contains("--hold")) { Console.WriteLine("Preview held until release-preview.txt exists.");
                    while (!File.Exists(Path.Combine(args[1], "release-preview.txt"))) System.Threading.Thread.Sleep(250); }
                backend.ClearTemporaryPreview(doc);
                if (dirty != doc.GetSaveFlag() || stamp != doc.GetUpdateStamp() || features != doc.GetFeatureCount())
                    throw new Exception("Preview changed source state.");
                Console.WriteLine("PASS preview/cancel source dirty/stamp/features unchanged");
                if (args.Contains("--matrix")) foreach (var counts in new[] { new[] { 7, 4 }, new[] { 10, 6 }, new[] { 7, 4 }, new[] { 5, 2 } }) {
                    var nextRequest = request.Snapshot();
                    nextRequest.LongRibCount = counts[0]; nextRequest.CrossRibCount = counts[1];
                    var nextPlan = KadaiPlanner.Build(nextRequest);
                    var nextProfiles = backend.CalculateProfiles(nextPlan, source);
                    backend.ShowTemporaryPreview(doc, nextPlan, nextProfiles);
                    if (backend.VisibleBodyCount != counts[0] + counts[1]) throw new Exception("Wrong temporary body count.");
                    backend.ClearTemporaryPreview(doc);
                    if (dirty != doc.GetSaveFlag() || stamp != doc.GetUpdateStamp() || features != doc.GetFeatureCount()) throw new Exception("Matrix preview changed source.");
                    Console.WriteLine("PASS real panel preview " + counts[0] + "x" + counts[1] + " / cancel unchanged");
                }
                if (args.Contains("--parts")) {
                    var writer = new KadaiNativePrototype(sw, materialDatabase, material, bendTable);
                    writer.WriteNewPackage(plan, profiles, Path.Combine(output, "package"), 100, .1,
                        args.Contains("--export"), template, Console.WriteLine);
                    if (dirty != doc.GetSaveFlag() || stamp != doc.GetUpdateStamp() || features != doc.GetFeatureCount())
                        throw new Exception("Prototype package changed the source.");
                    Console.WriteLine("PASS native assembly component count and final interference check");
                }
            }
            File.WriteAllText(Path.Combine(args[1], "PASS.txt"), "SW " + sw.RevisionNumber() + "\r\nPreview/cancel verified; prototype only.\r\n");
            return 0;
            } finally { if (openedHere) sw.CloseDoc(input); }
        } catch (Exception e) { Console.Error.WriteLine(e);
            if (args.Length > 1 && Directory.Exists(args[1])) File.WriteAllText(Path.Combine(args[1], "FAIL.txt"), e.ToString());
            return 1; }
    }
}
