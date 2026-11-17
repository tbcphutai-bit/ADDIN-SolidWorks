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
    [STAThread] public static int Main(string[] args)
    {
        try {
            if (args.Length < 2 || (Directory.Exists(args[1]) && !args.Contains("--assemble-only"))) throw new ArgumentException("Provide copied panel Part and a NEW output directory.");
            Directory.CreateDirectory(args[1]);
            var sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
            if (args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
                var cached = new JavaScriptSerializer().Deserialize<List<KadaiProfile3D>>(File.ReadAllText(args[0]));
                var settings = new KadaiRequest { LongRibCount = 3, CrossRibCount = 1, OriginVmm = -1430 };
                var cachedPlan = KadaiPlanner.Build(settings);
                var cachedWriter = new KadaiNativePrototype(sw);
                foreach (var profile in args.Contains("--assemble-only") ? new List<KadaiProfile3D>() : cached) {
                    var part = cachedWriter.WritePart(profile, cachedPlan, Path.Combine(args[1], profile.RibId + ".SLDPRT"), 100, .1);
                    Console.WriteLine("PASS native SheetMetal/Cut/Flat " + profile.RibId);
                    if (args.Contains("--export")) {
                        cachedWriter.ExportFlatDxf(part, Path.Combine(args[1], profile.RibId + ".dxf"));
                        Console.WriteLine("PASS DXF " + profile.RibId);
                        var drawing = cachedWriter.CreateFlatDrawing(part, Path.Combine(args[1], profile.RibId + ".SLDDRW"));
                        Console.WriteLine("PASS drawing " + profile.RibId); sw.CloseDoc(drawing.GetPathName());
                    }
                    sw.CloseDoc(part.GetPathName());
                }
                int templateIndex = Array.IndexOf(args, "--assembly-template");
                cachedWriter.CreateAssembly(cached, args[1], cachedPlan.Settings.BottomMm,
                    templateIndex >= 0 ? args[templateIndex + 1] : null);
                Console.WriteLine("PASS prototype assembly and BOM");
                File.WriteAllText(Path.Combine(args[1], "PASS.txt"), "Native prototype only; production gate closed.");
                return 0;
            }
            int e = 0, w = 0;
            var doc = sw.GetOpenDocumentByName(args[0]) as ModelDoc2;
            if (doc == null) doc = sw.OpenDoc6(args[0], 1, 3, "", ref e, ref w) as ModelDoc2;
            if (doc == null || !string.Equals(doc.GetPathName(), args[0], StringComparison.OrdinalIgnoreCase)) throw new Exception("Wrong panel Part.");
            sw.ActivateDoc3(doc.GetTitle(), false, 0, ref e);
            // Test fixture selection: largest single face of the lowest surface body.
            var panel = Items(((PartDoc)doc).GetBodies2(1, false)).OfType<Body2>()
                .OrderBy(b => ((double[])b.GetBodyBox())[1]).First();
            var face = Items(panel.GetFaces()).OfType<Face2>().OrderByDescending(f => f.GetArea()).First();
            doc.ClearSelection2(true); ((Entity)face).Select4(false, null);
            var request = new KadaiRequest { LongRibCount = 3, CrossRibCount = 1,
                OriginUmm = 0, OriginVmm = -1430, LengthXmm = 2250, LengthZmm = 1450,
                LongEndMarginMm = 50, CrossEndMarginMm = 150.9 };
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
                    request.LongRibCount = counts[0]; request.CrossRibCount = counts[1];
                    var nextPlan = KadaiPlanner.Build(request);
                    var nextProfiles = backend.CalculateProfiles(nextPlan, source);
                    backend.ShowTemporaryPreview(doc, nextPlan, nextProfiles);
                    if (backend.VisibleBodyCount != counts[0] + counts[1]) throw new Exception("Wrong temporary body count.");
                    backend.ClearTemporaryPreview(doc);
                    if (dirty != doc.GetSaveFlag() || stamp != doc.GetUpdateStamp() || features != doc.GetFeatureCount()) throw new Exception("Matrix preview changed source.");
                    Console.WriteLine("PASS real panel preview " + counts[0] + "x" + counts[1] + " / cancel unchanged");
                }
                if (args.Contains("--parts")) {
                    var writer = new KadaiNativePrototype(sw);
                    foreach (var profile in profiles) {
                        var part = writer.WritePart(profile, plan, Path.Combine(args[1], profile.RibId + ".SLDPRT"), 100, .1);
                        Console.WriteLine("PASS native SheetMetal/Cut/Flat " + profile.RibId);
                        if (args.Contains("--export")) {
                            writer.ExportFlatDxf(part, Path.Combine(args[1], profile.RibId + ".dxf"));
                            Console.WriteLine("PASS DXF " + profile.RibId);
                            var drawing = writer.CreateFlatDrawing(part, Path.Combine(args[1], profile.RibId + ".SLDDRW"));
                            Console.WriteLine("PASS drawing " + profile.RibId); sw.CloseDoc(drawing.GetPathName());
                        }
                        sw.CloseDoc(part.GetPathName());
                    }
                }
            }
            File.WriteAllText(Path.Combine(args[1], "PASS.txt"), "SW " + sw.RevisionNumber() + "\r\nPreview/cancel verified; prototype only.\r\n");
            return 0;
        } catch (Exception e) { Console.Error.WriteLine(e);
            if (args.Length > 1 && Directory.Exists(args[1])) File.WriteAllText(Path.Combine(args[1], "FAIL.txt"), e.ToString());
            return 1; }
    }
}
