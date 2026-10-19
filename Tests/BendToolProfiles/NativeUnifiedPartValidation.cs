using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

internal static partial class NativeAssemblyBatchValidation
{
    private static int SeedMain(string[] args)
    {
        if(args.Length!=2)return 2;string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);ISldWorks app=null;
        try
        {
            app=NativeTestProcess.StartOwned(output);app.Visible=false;app.UserControl=false;
            var doc=UiSheet(app,args[1],Path.Combine(output,"PARAMETRIC-THREE.SLDPRT"),3,true);
            var chosen=doc.Parameter(generatedSeedDimension) as Dimension;
            Check(chosen!=null,"Fresh native fixture has a real editable length parameter");
            string parameter=string.Join("@",chosen.FullName.Split('@').Take(2));double original=chosen.SystemValue;
            Check(chosen.SetSystemValue3(original+.0001,1,null)==0,"Fresh seed parameter changes actual native shape");doc.ForceRebuild3(false);
            Check(chosen.SetSystemValue3(original,1,null)==0,"Restore private seed parameter");doc.ForceRebuild3(false);Save(doc);
            File.WriteAllText(Path.Combine(output,"parametric-seed.json"),Json.Serialize(new{Result="PASS",Path=doc.GetPathName(),Parameter=parameter,Assertions=assertions}));return 0;
        }
        catch(Exception ex){Console.WriteLine(ex);return 1;}
        finally{if(app!=null)try{app.CloseAllDocuments(true);app.ExitApp();}catch{}}
    }
    // Real Part backend + actual WinForms controller. Source seed is only
    // copied; sketch/rebuild changes below apply to that disposable copy.
    private static int UnifiedPartMain(string[] args)
    {
        if (args.Length != 5) { Console.WriteLine("--part-ui dll new-output part-template six-bend-seed min-xlsx"); return 2; }
        ISldWorks app = null; var report = new Dictionary<string, object>();
        string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output); int code = 1;
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            product = Assembly.LoadFrom(args[0]); string hash = Sha(args[3]);
            report["Result"] = "RUNNING"; report["OriginalSeedSha256"] = hash;
            app = NativeTestProcess.StartOwned(output); app.Visible = false; app.UserControl = false;
            foreach (int bends in new[] { 0, 3, 6 })
            {
                ModelDoc2 part;
                if (bends == 6)
                {
                    string path = Path.Combine(output, "SIX.SLDPRT"); File.Copy(args[3], path, false); File.SetAttributes(path, FileAttributes.Normal);
                    part = Open(app, path);
                }
                else part = UiSheet(app, args[2], Path.Combine(output, "PART-" + bends + ".SLDPRT"), bends);
                Activate(app, part); string state = Json.Serialize(State(part));
                var form = (Form)Activator.CreateInstance(product.GetType("ADDIN.UI.BendAssemblyBatchForm", true), All, null,
                    new object[] { part.GetTitle(), part.ConfigurationManager.ActiveConfiguration.Name }, null);
                var settings = New("BendToolStorageSettings", Path.Combine(output, "settings-" + bends + ".json"));
                Call(settings, "SavePath", args[4], CancellationToken.None);
                var backend = Activator.CreateInstance(product.GetType("ADDIN.UI.BendAssemblyBatchLiveBackend", true), All, null,
                    new[] { app, part, New("BendToolCatalogProvider", settings) }, null);
                var controller = Activator.CreateInstance(product.GetType("ADDIN.UI.BendAssemblyBatchController", true), All, null, new[] { form, backend }, null);
                try
                {
                    Call(form, "SetPartMode"); form.Show(); UiClick(form, "btnRunBatch"); UiSpin(() => !(bool)Get(controller, "Running"));
                    var cold = Get(controller, "displayed");
                    Check(cold != null && (bool)Get(cold, "Current"), "Native Part cold result current: " + UiControl(form,"lblProgressTitle").Text);
                    var rows = Items(Get(cold, "Parts")); Check(rows.Length == 1 && (int)Get(rows[0], "BendCount") == bends, "Actual native Part " + bends + " bend inventory");
                    UiCheckBindings(form, cold);
                    CheckMinimumProjection(backend,cold);
                    Check(Json.Serialize(State(part)) == state, "Native Part capture leaves dirty/configuration/stamp unchanged");
                    var planning = Get(rows[0], "Planning");
                    if (bends > 0) Check(planning != null && Items(Get(planning, "Tools")).Length == 18 && Items(Get(planning, "CommonToolIds")).Length == 0,
                        "All 18 native planning candidates retain UNKNOWN without process evidence");
                    if (bends > 0)
                    {
                        var nativeBend = Items(Get(rows[0],"StaticCandidates")).First();
                        Check((bool)Call(backend,"Locate",cold,Get(rows[0],"OccurrenceSetKey"),Get(nativeBend,"FeatureId")),"Live highlight resolves persistent identity and actual bend face");
                        part.ClearSelection2(true);
                    }
                    report["Part" + bends + "Cold"] = UiTimings(controller);
                    UiClick(form, "btnRunBatch"); UiSpin(() => !(bool)Get(controller, "Running"));
                    var warm = Get(controller, "displayed"); Check(warm != null && (bool)Get(warm, "Current"), "Native Part warm current");
                    CheckMinimumProjection(backend,warm);
                    if (bends > 0) Check((int)Get(warm, "Captures") == 0 && (int)Get(warm, "CaptureHits") == 1, "Warm Part actually reuses one native capture");
                    report["Part" + bends + "Warm"] = UiTimings(controller);
                    if (bends == 3)
                    {
                        part.ForceRebuild3(false); UiSpin(() => Get(controller, "displayed") == null);
                        UiClick(form, "btnRefresh"); UiSpin(() => !(bool)Get(controller, "Running"));
                        var refreshed = Get(controller, "displayed");
                        Check(refreshed != null && (bool)Get(refreshed, "Current"), "Native Part rebuild/Refresh current: " + UiControl(form,"lblProgressTitle").Text);
                        UiCheckBindings(form, refreshed); report["Part3RebuildRefresh"] = "PASS";
                    }
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "PRIVATE-unified-part-" + bends + ".png")); }
                    report["Part" + bends + "Result"] = "PASS";
                }
                finally { ((IDisposable)controller).Dispose(); form.Dispose(); }
                File.WriteAllText(Path.Combine(output, "unified-part-result.json"), Json.Serialize(report));
                app.CloseDoc(part.GetTitle());
            }
            Check(Sha(args[3]) == hash, "Original six-bend seed bytes unchanged"); report["Result"] = "PASS"; code = 0;
        }
        catch (Exception ex) { report["Result"] = "FAIL"; report["Error"] = ex.ToString(); Console.WriteLine(ex); }
        finally
        {
            report["Assertions"] = assertions; report["FunctionalResult"] = report["Result"];
            if (app != null)
            {
                try { app.ExitApp(); } catch (Exception ex) { report["ShutdownError"] = ex.ToString(); }
                int? exit = NativeTestProcess.WaitForOwnedExit(10000);
                report["ShutdownExitCode"] = exit;
                report["ShutdownResult"] = exit == 0 && !report.ContainsKey("ShutdownError") ? "PASS" : exit.HasValue ? "FAIL" : "NOT_CONFIRMED";
                if (Convert.ToString(report["ShutdownResult"]) != "PASS") { report["Result"] = "FAIL"; code = 1; }
            }
            File.WriteAllText(Path.Combine(output, "unified-part-result.json"), Json.Serialize(report));
        }
        Console.WriteLine(Json.Serialize(report)); return code;
    }
    private static void CheckMinimumProjection(object backend,object snapshot)
    {
        var session=Get(backend,"session");var items=Items(Get(session,"Items"));
        foreach(var part in Items(Get(snapshot,"Parts")))
        {
            foreach(var metadata in Items(Get(part,"BendMetadata")))
            {
                int feature=(int)Get(metadata,"FeatureId");
                var matching=items.SelectMany(i=>Items(Get(i,"Rows"))).Where(r=>(int)Get(r,"FeatureId")==feature).ToArray();
                Check(matching.Length==1,"Min metadata uses exact native FeatureId in single Part");
                var context=Get(matching[0],"MinimumContext");var evaluated=Call(context,"Evaluate",matching[0]);
                Check(Convert.ToString(Get(evaluated,"Status"))==Convert.ToString(Get(metadata,"MinimumStatus"))&&
                    Convert.ToString(Get(evaluated,"Summary"))==Convert.ToString(Get(metadata,"MinimumSummary")),"Unified Min equals existing evaluator status and reason");
                var expected=Items(Get(evaluated,"Sides")).Where(s=>Get(s,"Measure")!=null).ToArray();var actual=Items(Get(metadata,"Sides"));
                Check(expected.Length==actual.Length,"Min side count unchanged");
                for(int side=0;side<expected.Length;side++)
                    Check(Equals(Get(Get(expected[side],"Measure"),"ValueMm"),Get(actual[side],"ActualMm"))&&Equals(Get(expected[side],"LimitMm"),Get(actual[side],"MinimumMm")),
                        "Min outer dimension and Excel limit unchanged");
            }
        }
    }
}
