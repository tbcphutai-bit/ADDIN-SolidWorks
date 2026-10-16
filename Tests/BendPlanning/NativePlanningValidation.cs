using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

// Local-only evidence contains model identifiers and measurements. Do not upload
// its output or the input Part to a public repository.
internal static partial class NativePlanningValidation
{
    private sealed class FailingPlanningListener : TraceListener
    {
        public int Failures;
        public override void Write(string message) { }
        public override void WriteLine(string message)
        {
            if ((message ?? "").StartsWith("[BEND PLAN DRAFT]", StringComparison.Ordinal))
            { Failures++; throw new InvalidOperationException("Injected planning diagnostic failure"); }
        }
    }
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    private static Assembly product;
    private static int checks;
    private static string output;
    private static object Get(object x, string name) { var f = x.GetType().GetField(name, All); return f != null ? f.GetValue(x) : x.GetType().GetProperty(name, All).GetValue(x, null); }
    private static void Set(object x, string name, object value) { x.GetType().GetField(name, All).SetValue(x, value); }
    private static object Call(object x, string name, params object[] args)
    { try { return x.GetType().GetMethods(All).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(x, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
    private static object New(string type, params object[] args) { return Activator.CreateInstance(product.GetType("ADDIN." + type, true), All, null, args, null); }
    private static object[] Items(object x) { return ((IEnumerable)x).Cast<object>().ToArray(); }
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static string Sha(string path) { using (var h = SHA256.Create()) using (var s = File.OpenRead(path)) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", ""); }
    private static void Pump(int ms) { var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(15); } }
    private static void Until(Func<bool> done, string message, int limit = 60000)
    { var watch = Stopwatch.StartNew(); while (!done() && watch.ElapsedMilliseconds < limit) { Application.DoEvents(); Thread.Sleep(20); } Check(done(), message); }
    private static bool Scanned(Form form)
    { var items = Items(Get(Get(form, "session"), "Items")); return items.Length > 0 && items.All(i => (bool)Get(i, "Done")) && !(bool)Get(form, "pendingStart") && !(bool)Get(form, "scanning"); }
    private static Form OpenCheck(ISldWorks app, ModelDoc2 model, string excel)
    {
        var form = (Form)New("UI.BendCheckForm", app, model, null);
        // Change only this test window's provider; preserve the user's Settings.
        var settings = New("Commands.BendToolStorageSettings", Path.Combine(output, "settings.json"));
        Call(settings, "SavePath", excel, CancellationToken.None);
        Set(form, "provider", New("Commands.BendToolCatalogProvider", settings));
        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-3000, -3000);
        form.Show(); Until(() => Scanned(form), "Existing Check chan form completes: " + ((Label)Get(form, "state")).Text);
        return form;
    }
    private static bool Suppressed(Feature feature)
    {
        var states = feature.IsSuppressed2(1, null);
        return states is bool ? (bool)states : states is Array && Convert.ToBoolean(((Array)states).GetValue(0));
    }
    private static void Walk(Feature feature, Dictionary<int, Feature> features)
    {
        if (Suppressed(feature) || features.ContainsKey(feature.GetID())) return;
        features.Add(feature.GetID(), feature);
        for (var child = feature.GetFirstSubFeature() as Feature; child != null; child = child.GetNextSubFeature() as Feature) Walk(child, features);
    }
    private static object NativeInventory(ModelDoc2 model, out Dictionary<int, Feature> physical, out double[] thicknesses)
    {
        var features = new Dictionary<int, Feature>();
        for (var f = model.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature) Walk(f, features);
        thicknesses = features.Values.Where(f => f.GetTypeName2() == "SheetMetal").Select(f => f.GetDefinition() as ISheetMetalFeatureData)
            .Where(d => d != null && d.Thickness > 0).Select(d => Math.Round(d.Thickness * 1000, 3)).Distinct().ToArray();
        // Independent witness: actual native cylindrical faces owned by a bend,
        // plus the native definition (not CaptureForCheck or the panel graph).
        var owners = new HashSet<int>();
        foreach (Body2 body in ((PartDoc)model).GetBodies2(0, false) as object[] ?? new object[0])
            foreach (Face2 face in body.GetFaces() as object[] ?? new object[0])
            {
                var surface = face.GetSurface() as Surface;
                if (surface != null && surface.IsCylinder()) { var owner = face.GetFeature() as Feature; if (owner != null) owners.Add(owner.GetID()); }
            }
        physical = features.Where(pair => owners.Contains(pair.Key) &&
            (pair.Value.GetDefinition() is IOneBendFeatureData || pair.Value.GetDefinition() is ISketchedBendFeatureData || pair.Value.GetDefinition() is IEdgeFlangeFeatureData)).ToDictionary(p => p.Key, p => p.Value);
        return physical.Values.Select(f => {
            var definition = f.GetDefinition();
            var one = definition as IOneBendFeatureData; var sketch = definition as ISketchedBendFeatureData; var flange = definition as IEdgeFlangeFeatureData;
            double angle = one != null ? one.BendAngle : sketch != null ? sketch.BendAngle : flange.BendAngle;
            double radius = one != null ? one.BendRadius : sketch != null ? sketch.BendRadius : flange.BendRadius;
            var persistent = model.Extension.GetPersistReference3(f) as byte[];
            return new { id = f.GetID(), name = f.Name, type = f.GetTypeName2(), angle = Math.Abs(angle * 180 / Math.PI), radius = Math.Abs(radius * 1000), persistent = persistent != null && persistent.Length > 0 };
        }).ToArray();
    }
    private static object Snapshot(Form form, ModelDoc2 model, bool expectPlanning)
    {
        var item = Items(Get(Get(form, "session"), "Items")).Single();
        Check(Get(item, "Error") == null, "Capture error: " + Get(item, "Error"));
        Check((bool)Get(item, "Current"), "Item current");
        Dictionary<int, Feature> physical; double[] thicknesses; var native = NativeInventory(model, out physical, out thicknesses);
        var rows = Items(Get(item, "Rows")); Check(rows.Length == physical.Count, "Native face-owning bends match rows: " + physical.Count + "/" + rows.Length);
        Check(thicknesses.Length == 1, "One native Sheet Metal thickness in test fixture");
        foreach (var row in rows)
        {
            int id = (int)Get(row, "FeatureId"); Check(physical.ContainsKey(id), "Native owner ID matches");
            var feature = physical[id]; var definition = feature.GetDefinition();
            var one = definition as IOneBendFeatureData; var sketch = definition as ISketchedBendFeatureData; var flange = definition as IEdgeFlangeFeatureData;
            double angle = one != null ? one.BendAngle : sketch != null ? sketch.BendAngle : flange.BendAngle;
            double radius = one != null ? one.BendRadius : sketch != null ? sketch.BendRadius : flange.BendRadius;
            Check(Get(row, "FeatureName").ToString() == feature.Name.Normalize(System.Text.NormalizationForm.FormKC), "Feature name matches native");
            Check(Math.Abs(Convert.ToDouble(Get(row, "BendAngleDeg")) - Math.Abs(angle * 180 / Math.PI)) < 1e-7, "Angle matches native");
            Check(Math.Abs(Convert.ToDouble(Get(row, "BendRadiusMm")) - Math.Abs(radius * 1000)) < 1e-7, "R matches native");
            Check(!string.IsNullOrEmpty((string)Get(row, "PersistentId")), "Persistent ID captured");
            Check((string)Get(row, "PersistentId") == Convert.ToBase64String((byte[])model.Extension.GetPersistReference3(feature)), "Persistent ID equals native reference");
            Check(Math.Abs(Convert.ToDouble(Get(row, "ModelThicknessMm")) - thicknesses[0]) < 1e-7, "Thickness matches native Sheet Metal definition");
            Check(Get(row, "MinimumContext") != null, "Existing Min context retained");
        }
        var minimum = rows.Select(row => {
            var context = Get(row, "MinimumContext"); var evaluation = Call(context, "Evaluate", row);
            var sides = Items(Get(evaluation, "Sides")).Select(s => {
                var measure = Get(s, "Measure"); return new { shape = Get(measure, "Shape"), dimension = Get(measure, "Dimension"), outsideMm = Get(measure, "ValueMm"), thicknessMm = Get(measure, "ThicknessMm"), limitMm = Get(s, "LimitMm"), status = Get(Get(s, "Check"), "Status").ToString() };
            }).ToArray();
            return new { id = Get(row, "FeatureId"), name = Get(row, "FeatureName"), angle = Get(row, "BendAngleDeg"), radius = Get(row, "BendRadiusMm"), thickness = Get(row, "ModelThicknessMm"), persistent = Get(row, "PersistentId"), mark = Get(row, "Kind"), status = Get(evaluation, "Status").ToString(), sides };
        }).ToArray();
        object draftJson = null;
        if (expectPlanning)
        {
            var draft = Get(item, "PlanningDraft"); Check(draft != null, "Planning captured within existing form");
            string encoded = (string)Call(draft, "ToJson"); draftJson = Json.DeserializeObject(encoded);
            var bends = Items(Get(draft, "Bends")); var orders = Items(Get(draft, "CandidateOrders"));
            Check(bends.Length == rows.Length, "Planning bend count"); Check(bends.Select(b => Get(b, "Id")).Distinct().Count() == rows.Length, "Unique bend IDs");
            foreach (var bend in bends)
            {
                var row = rows.Single(r => (int)Get(r, "FeatureId") == (int)Get(bend, "FeatureId"));
                foreach (string field in new[] { "FeatureName", "BendAngleDeg", "BendRadiusMm", "ModelThicknessMm", "Kind" })
                {
                    string target = field == "BendAngleDeg" ? "AngleDeg" : field == "BendRadiusMm" ? "RadiusMm" : field == "ModelThicknessMm" ? "ThicknessMm" : field == "Kind" ? "Mark" : field;
                    Check(Equals(Get(row, field), Get(bend, target)), "Planning preserves " + field);
                }
                Check(Get(bend, "CheckMinStatus").ToString() == Get(Call(Get(row, "MinimumContext"), "Evaluate", row), "Status").ToString(), "Planning Min status matches existing evaluator");
            }
            foreach (var order in orders)
            {
                Check(Get(order, "Status").ToString() == "CHUA_XAC_MINH", "Every order unverified");
                Check(((string[])Get(order, "BendIds")).OrderBy(x => x).SequenceEqual(bends.Select(b => Get(b, "Id").ToString()).OrderBy(x => x)), "Order contains every bend once");
            }
            if (rows.Length == 2) Check(orders.Length == 2 && ((string[])Get(orders[0], "BendIds")).Reverse().SequenceEqual((string[])Get(orders[1], "BendIds")), "Two exact reverse permutations");
        }
        return new { nativeCount = physical.Count, rowCount = rows.Length, native, minimum, draft = draftJson, min = Get(item, "MinimumText"), contextReads = Get(Get(item, "Context"), "GeometryReadCount"), uiColumns = ((DataGridView)Get(form, "parts")).Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText).ToArray() };
    }
    #if !SYNTHETIC
    [STAThread]
    private static int Main(string[] args)
    {
        ISldWorks app = null; bool owned = false; string originalHash = null;
        var report = new Dictionary<string, object>();
        try
        {
            product = Assembly.LoadFrom(Path.GetFullPath(args[0])); output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            string original = Path.GetFullPath(args[2]); originalHash = Sha(original);
            string copy = Path.Combine(output, "PRIVATE-TEST.SLDPRT"); File.Copy(original, copy, true); File.SetAttributes(copy, FileAttributes.Normal);
            var before = Process.GetProcessesByName("SLDWORKS").Select(p => p.Id).ToArray();
            app = (ISldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application.32"));
            int pid = app.GetProcessID(); owned = !before.Contains(pid); Check(owned, "New owned SW process required");
            app.Visible = false; app.UserControl = false;
            File.WriteAllText(Path.Combine(output, "ownership.json"), Json.Serialize(new { pid, owned, startUtc = Process.GetProcessById(pid).StartTime.ToUniversalTime().ToString("o") }));
            Console.WriteLine("Owned SOLIDWORKS process " + pid);
            int errors = 0, warnings = 0; var model = app.OpenDoc6(copy, 1, 1, "", ref errors, ref warnings) as ModelDoc2;
            Check(model != null, "Open test copy " + errors); report["swRevision"] = app.RevisionNumber();
            string config = model.ConfigurationManager.ActiveConfiguration.Name; int stamp = model.GetUpdateStamp(); bool dirty = model.GetSaveFlag();
            report["bendState"] = model.GetBendState();
            bool hasFlatPattern = false;
            for (var feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                if (feature.GetTypeName2() == "FlatPattern") hasFlatPattern = true;
            Check(hasFlatPattern, "Native Part retains Flat-Pattern in Feature Tree");
            bool planning = args.Length > 4 && args[4] != "main";
            var messages = new StringWriter(); var listener = new TextWriterTraceListener(messages); Debug.Listeners.Add(listener);
            try
            {
                using (var form = OpenCheck(app, model, args[3]))
                {
                    report["initial"] = Snapshot(form, model, planning);
                    Check(model.GetUpdateStamp() == stamp && model.GetSaveFlag() == dirty && model.ConfigurationManager.ActiveConfiguration.Name == config, "Scan preserves model stamp/config/dirty");
                    if (args.Length > 5 && args[5] == "changes")
                    {
                        var session = Get(form, "session"); var oldItem = Items(Get(session, "Items")).Single();
                        var oldDraft = planning ? Get(oldItem, "PlanningDraft") : null;
                        model.ForceRebuild3(false); Call(form, "Tick");
                        Check(!(bool)Get(oldItem, "Current"), "Rebuild invalidates old item");
                        if (planning) Check(Get(oldItem, "PlanningDraft") == null, "Rebuild removes old planning draft");
                        Until(() => Scanned(form) && !ReferenceEquals(oldItem, Items(Get(session, "Items")).Single()), "Rebuild auto-rescan");
                        report["afterRebuild"] = Snapshot(form, model, planning);
                        if (planning) Check(!ReferenceEquals(oldDraft, Get(Items(Get(session, "Items")).Single(), "PlanningDraft")), "Rescan creates new draft");
                        string alternative = ((string[])model.GetConfigurationNames()).FirstOrDefault(c => c != config);
                        Check(alternative != null, "Alternate configuration exists for freshness test");
                        oldItem = Items(Get(session, "Items")).Single();
                        Check(model.ShowConfiguration2(alternative), "Switch config on copy only"); Call(form, "Tick");
                        Check(!(bool)Get(oldItem, "Current"), "Configuration invalidates old item");
                        if (planning) Check(Get(oldItem, "PlanningDraft") == null, "Configuration removes planning draft");
                        Check(model.ShowConfiguration2(config), "Return folded config on copy only"); model.ForceRebuild3(false);
                        Until(() => Scanned(form) && !ReferenceEquals(oldItem, Items(Get(session, "Items")).Single()), "Return config auto-rescan");
                        report["afterConfiguration"] = Snapshot(form, model, planning);
                        Feature sketchFeature = null; Dimension dimension = null;
                        var sketchCandidates = new Dictionary<int, Feature>();
                        for (var feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature) Walk(feature, sketchCandidates);
                        foreach (var feature in sketchCandidates.Values)
                        {
                            if (feature.GetTypeName2() != "ProfileFeature" || Suppressed(feature)) continue;
                            for (var display = feature.GetFirstDisplayDimension() as DisplayDimension; display != null; display = display.GetNext5() as DisplayDimension)
                            {
                                var candidate = display.GetDimension2(0) as Dimension;
                                if (candidate != null && new[] { 2, 11, 12 }.Contains(candidate.GetType()) && candidate.SystemValue > .001)
                                { sketchFeature = feature; dimension = candidate; break; }
                            }
                            if (dimension != null) break;
                        }
                        if (sketchFeature == null) sketchFeature = sketchCandidates.Values.FirstOrDefault(f => f.GetTypeName2() == "ProfileFeature" && f.GetSpecificFeature2() is Sketch);
                        Check(sketchFeature != null, "Native sketch available for edit test");
                        oldItem = Items(Get(session, "Items")).Single();
                        Check(sketchFeature.Select2(false, 0), "Select sketch on private copy"); model.EditSketch();
                        Check(model.GetActiveSketch2() != null, "Entered sketch edit on copy"); Call(form, "Tick");
                        Check(!(bool)Get(oldItem, "Current"), "Sketch invalidates old item");
                        if (planning) Check(Get(oldItem, "PlanningDraft") == null, "Sketch removes planning draft immediately");
                        double previous = dimension == null ? 0 : dimension.SystemValue;
                        SketchSegment added = null;
                        if (dimension != null)
                        {
                            Check(dimension.SetSystemValue3(previous + .0001, 1, null) == 0, "Change sketch dimension on private copy");
                            report["sketchEdit"] = "Changed a native driving linear dimension on the private copy";
                        }
                        else
                        {
                            // Some DXF-derived native sketches have no driving
                            // dimensions. Make a genuine harmless sketch edit.
                            added = model.SketchManager.CreateLine(.123, .234, 0, .124, .234, 0) as SketchSegment;
                            Check(added != null, "Create construction segment on private sketch"); added.ConstructionGeometry = true;
                            report["sketchEdit"] = "Added construction geometry; native sketch exposes no driving linear dimension";
                        }
                        model.SketchManager.InsertSketch(true); model.ForceRebuild3(false);
                        Check(model.GetActiveSketch2() == null, "Exited sketch on copy");
                        Check(dimension != null ? Math.Abs(dimension.SystemValue - previous - .0001) < 1e-9 : added.ConstructionGeometry, "Sketch actually changed");
                        Until(() => Scanned(form) && !ReferenceEquals(oldItem, Items(Get(session, "Items")).Single()), "Sketch rebuild auto-rescan");
                        report["afterSketch"] = Snapshot(form, model, planning);
                        if (planning)
                        {
                            oldItem = Items(Get(session, "Items")).Single(); Call(Get(oldItem, "Context"), "Invalidate");
                            Check(Get(oldItem, "PlanningDraft") == null, "Shared context invalidation hides draft before next poll");
                            Call(form, "RequestScan"); Until(() => Scanned(form), "Rescan shared invalidation");
                            var failing = new FailingPlanningListener(); Debug.Listeners.Add(failing);
                            try
                            {
                                Call(form, "RequestScan"); Until(() => Scanned(form), "Scan survives diagnostic failure");
                                var item = Items(Get(session, "Items")).Single();
                                Check(failing.Failures == 1, "Planning diagnostic failure injected exactly once");
                                Check(Get(item, "Error") == null, "Planning diagnostic failure does not damage Min");
                                var draft = Get(item, "PlanningDraft"); Check(draft != null, "Draft retained after logging failure");
                                Check(Get(draft, "DiagnosticError").ToString().Contains("Injected"), "Planning diagnostic error retained separately");
                                report["diagnosticIsolation"] = true;
                            }
                            finally { Debug.Listeners.Remove(failing); }
                        }
                    }
                    form.Close();
                }
            }
            finally { Debug.Listeners.Remove(listener); listener.Flush(); File.WriteAllText(Path.Combine(output, "PRIVATE-debug-output.txt"), messages.ToString()); listener.Dispose(); }
            if (planning)
            {
                var lines = messages.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(line => line.StartsWith("[BEND PLAN DRAFT] ", StringComparison.Ordinal)).ToArray();
                Check(lines.Length > 0, "Actual debug output contains planning JSON");
                foreach (string line in lines) Check(((Dictionary<string, object>)Json.DeserializeObject(line.Substring("[BEND PLAN DRAFT] ".Length)))["Status"].ToString() == "CHUA_XAC_MINH", "Actual emitted debug JSON parses and stays unverified");
                report["debugJsonRecords"] = lines.Length;
            }
            Check(Sha(original) == originalHash, "Original hash unchanged"); report["originalUnchanged"] = true; report["passed"] = true;
        }
        catch (Exception ex) { report["error"] = ex.ToString(); Console.WriteLine(ex); }
        finally
        {
            report["assertions"] = checks;
            if (output != null) File.WriteAllText(Path.Combine(output, "PRIVATE-native-report.json"), Json.Serialize(report));
            if (owned && app != null)
            {
                try { foreach (ModelDoc2 doc in app.GetDocuments() as object[] ?? new object[0]) if (doc.GetPathName().StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) app.CloseDoc(doc.GetTitle()); app.ExitApp(); }
                catch (Exception ex) { Console.WriteLine("Cleanup: " + ex.Message); }
            }
        }
        Console.WriteLine("Checks: " + checks + "; passed: " + report.ContainsKey("passed"));
        return report.ContainsKey("passed") ? 0 : 1;
    }
    #endif
}
