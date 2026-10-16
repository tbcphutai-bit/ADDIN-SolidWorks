using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

internal static partial class NativePlanningValidation
{
    // Based on the official native base flange API example. The coordinates are
    // artificial test sections; no customer CAD is embedded in this test.
    // https://help.solidworks.com/2019/english/api/sldworksapi/insert_sheet_metal_base_flange_example_csharp.htm
    private static ModelDoc2 MakeSynthetic(ISldWorks app, string template, string name, double[] xy)
    {
        var model = app.NewDocument(template, 0, 0, 0) as ModelDoc2; Check(model != null, "New synthetic " + name);
        Feature plane = null;
        for (var f = model.FirstFeature() as Feature; f != null && plane == null; f = f.GetNextFeature() as Feature)
            if (f.GetTypeName2() == "RefPlane") plane = f;
        Check(plane != null && plane.Select2(false, 0), "Select synthetic sketch plane");
        model.SketchManager.InsertSketch(true); model.SketchManager.AddToDB = true;
        for (int i = 0; i + 3 < xy.Length; i += 2)
            Check(model.SketchManager.CreateLine(xy[i], xy[i + 1], 0, xy[i + 2], xy[i + 3], 0) != null, "Synthetic profile segment");
        model.SketchManager.AddToDB = false; model.SketchManager.InsertSketch(true);
        Feature sketchFeature = null;
        for (var f = model.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
            if (f.GetTypeName2() == "ProfileFeature") sketchFeature = f;
        Check(sketchFeature != null && sketchFeature.Select2(false, 0), "Select native profile for base flange");
        var flange = model.FeatureManager.InsertSheetMetalBaseFlange2(.002, false, .001, .06, .01, false, 0, 0, 1, null,
            false, 2, .0001, .0001, .5, true, false, true, true);
        Check(flange != null, "Native Sheet Metal base flange " + name); model.ForceRebuild3(false);
        Dictionary<int, Feature> physical; double[] thicknesses; NativeInventory(model, out physical, out thicknesses);
        Check(physical.Count > 0, "Synthetic folded model contains native cylindrical bends");
        int errors = 0, warnings = 0;
        Check(model.SaveAs4(Path.Combine(output, name + ".SLDPRT"), 0, 1, ref errors, ref warnings), "Save only generated fixture " + errors);
        return model;
    }

    #if SYNTHETIC
    [STAThread]
    private static int Main(string[] args)
    {
        ISldWorks app = null; bool owned = false; var report = new Dictionary<string, object>();
        try
        {
            product = Assembly.LoadFrom(Path.GetFullPath(args[0])); output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            var before = Process.GetProcessesByName("SLDWORKS").Select(p => p.Id).ToArray();
            app = (ISldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application.32"));
            int pid = app.GetProcessID(); owned = !before.Contains(pid); Check(owned, "New owned SW process required");
            app.Visible = false; app.UserControl = false;
            File.WriteAllText(Path.Combine(output, "ownership.json"), Json.Serialize(new { pid, owned, startUtc = Process.GetProcessById(pid).StartTime.ToUniversalTime().ToString("o") }));
            Console.WriteLine("Owned synthetic SOLIDWORKS process " + pid);
            string[] names = { "Synthetic-L", "Synthetic-U", "Synthetic-Z" };
            double[][] sections = { new double[] { 0, 0, .04, 0, .04, .025 }, new double[] { 0, .025, 0, 0, .04, 0, .04, .025 }, new double[] { 0, 0, .04, 0, .04, .025, .08, .025 } };
            for (int n = 0; n < names.Length; n++)
            {
                var model = MakeSynthetic(app, args[3], names[n], sections[n]);
                using (var form = OpenCheck(app, model, args[2]))
                {
                    report[names[n]] = Snapshot(form, model, true);
                    var item = Items(Get(Get(form, "session"), "Items")).Single();
                    Check(Items(Get(item, "Rows")).Length == (n == 0 ? 1 : 2), "Expected synthetic native bend count");
                    var geometry = (IDictionary)Get(Get(item, "Context"), "Geometry");
                    var shapes = geometry.Values.Cast<object>().SelectMany(g => Items(Get(g, "Measures"))).Select(m => Convert.ToString(Get(m, "Shape"))).ToArray();
                    Check(shapes.Contains(n == 0 ? "L" : n == 1 ? "C" : "Z"), "Existing Min graph recognizes intended artificial section");
                    form.Close();
                }
                app.CloseDoc(model.GetTitle()); Console.WriteLine(names[n] + " passed");
            }
            report["passed"] = true;
        }
        catch (Exception ex) { report["error"] = ex.ToString(); Console.WriteLine(ex); }
        finally
        {
            report["assertions"] = checks;
            if (output != null) File.WriteAllText(Path.Combine(output, "synthetic-report.json"), Json.Serialize(report));
            if (owned && app != null) { try { foreach (ModelDoc2 doc in app.GetDocuments() as object[] ?? new object[0]) if (doc.GetPathName().StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) app.CloseDoc(doc.GetTitle()); app.ExitApp(); } catch { } }
        }
        Console.WriteLine("Checks: " + checks + "; passed: " + report.ContainsKey("passed"));
        return report.ContainsKey("passed") ? 0 : 1;
    }
    #endif
}
