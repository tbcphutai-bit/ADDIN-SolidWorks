using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.Kadai
{
    // Laboratory P3/P5 only. Never called by the production Apply button.
    // Creates a fresh package; neither overwrites existing parts nor recreates
    // user features. Exact-curve and regeneration gates remain outstanding.
    internal sealed class KadaiNativePrototype
    {
        readonly ISldWorks app;
        internal KadaiNativePrototype(ISldWorks app) { this.app = app; }
        static object[] Items(object value) { return value as object[] ?? new object[0]; }
        static double Dot(double[] a, double[] b) { return a.Zip(b, (x, y) => x * y).Sum(); }
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        Feature SketchFeature(ModelDoc2 doc, Sketch sketch)
        {
            for (Feature f = doc.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
                if (f.GetTypeName2() == "ProfileFeature" && app.IsSame(f.GetSpecificFeature2(), sketch) == 1) return f;
            throw new InvalidOperationException("Cannot resolve the new sketch by COM identity.");
        }
        private ModelDoc2 WritePart(KadaiProfile3D profile, double bottomMm, string path, List<KadaiJointCut> cuts)
        {
            Require(!File.Exists(path), "Refusing to overwrite " + path);
            var local = KadaiJointGeometry.LocalPoints(profile, bottomMm);
            string template = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            Require(File.Exists(template), "Missing native Part template: " + template);
            var doc = app.NewDocument(template, 0, 0, 0) as ModelDoc2;
            Require(doc != null, "Cannot create native Part.");
            try {
            doc.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swUnitsLinear, (int)swLengthUnit_e.swMM);
            Feature plane = null;
            for (Feature f = doc.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
                if (f.GetTypeName2() == "RefPlane") { plane = f; break; }
            Require(plane != null && plane.Select2(false, 0), "Cannot select native sketch plane.");
            var sketch = doc.SketchManager;
            sketch.InsertSketch(true);
            var profileSketchObject = sketch.ActiveSketch;
            try {
                sketch.AddToDB = true;
                for (int i = 0; i < local.Length; i++) {
                    var a = local[i]; var b = local[(i + 1) % local.Length];
                    Require(sketch.CreateLine(a[0], a[1], 0, b[0], b[1], 0) != null, "Profile edge failed.");
                }
            } finally { sketch.AddToDB = false; sketch.InsertSketch(true); }
            var baseSketch = SketchFeature(doc, profileSketchObject);
            Require(baseSketch != null && baseSketch.Select2(false, 0), "Profile sketch missing.");
            baseSketch.Name = "Kadai_Profile_" + profile.RibId;
            var allowance = doc.FeatureManager.CreateCustomBendAllowance(); allowance.Type = 2; allowance.KFactor = .5;
            var flange = doc.FeatureManager.InsertSheetMetalBaseFlange2(profile.ThicknessMeters, false,
                .0001, 0, 0, false, 0, 0, 0, allowance, true, 0, 0, 0, .5, true, false, true, true);
            Require(flange != null, "Native Sheet Metal Base Flange failed.");
            flange.Name = "Kadai_BaseFlange_" + profile.RibId;
            doc.ClearSelection2(true); Require(plane.Select2(false, 0), "Cannot select slot plane.");
            sketch.InsertSketch(true);
            var slotSketchObject = sketch.ActiveSketch;
            try {
                sketch.AddToDB = true;
                foreach (var slot in cuts) {
                    var corners = new[] { new[] { slot.Left, slot.Bottom }, new[] { slot.Right, slot.Bottom },
                        new[] { slot.Right, slot.Top }, new[] { slot.Left, slot.Top } };
                    for (int i = 0; i < 4; i++) {
                        var a = corners[i]; var b = corners[(i + 1) % 4];
                        Require(sketch.CreateLine(a[0], a[1], 0, b[0], b[1], 0) != null, "Slot sketch edge failed.");
                    }
                }
            } finally { sketch.AddToDB = false; sketch.InsertSketch(true); }
            var slotSketch = SketchFeature(doc, slotSketchObject);
            Require(slotSketch != null && slotSketch.Select2(false, 0), "Slot sketch missing.");
            slotSketch.Name = "Kadai_Slots_" + profile.RibId;
            // Through-all both directions, NormalCut=true, single native Part body.
            var cut = doc.FeatureManager.FeatureCut4(false, false, false, 1, 1, 0, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, false, false, false, 0, 0, false, true);
            Require(cut != null, "Part-level Cut-Extrude failed."); cut.Name = "Kadai_JointCut_" + profile.RibId;
            doc.EditRebuild3();
            Require(doc.Extension.NeedsRebuild2 == 0, "Part requires a rebuild.");
            var bodies = Items(((PartDoc)doc).GetBodies2(0, false)).OfType<Body2>().ToList();
            Require(bodies.Count == 1 && bodies[0].IsSheetMetal(), "Expected one native Sheet Metal body.");
            bool warning; Require(cut.GetErrorCode2(out warning) == 0 && !warning, "Cut rebuild error/warning.");
            Feature flat = null;
            for (Feature f = doc.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
                if (f.GetTypeName2() == "FlatPattern") flat = f;
            Require(flat != null, "Flat Pattern missing.");
            Require(flat.SetSuppression2(1, 1, null), "Cannot unsuppress Flat Pattern."); doc.EditRebuild3();
            Require(flat.GetErrorCode2(out warning) == 0 && !warning, "Flat Pattern failed.");
            Require(flat.SetSuppression2(0, 1, null), "Cannot restore folded Part."); doc.EditRebuild3();
            doc.Extension.CustomPropertyManager[""].Add3("Kadai_RibId", 30, profile.RibId, 2);
            doc.Extension.CustomPropertyManager[""].Add3("Kadai_Status", 30, "PROTOTYPE - NOT FOR MANUFACTURE", 2);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            int error = 0, saveWarning = 0;
            Require(doc.Extension.SaveAs(path, 0, 1, null, ref error, ref saveWarning) && error == 0 && saveWarning == 0,
                "Saving prototype failed: " + error + "/" + saveWarning);
            return doc;
            } catch { try { app.CloseDoc(doc.GetTitle()); } catch { } throw; }
        }

        internal void WriteNewPackage(KadaiPlan plan, List<KadaiProfile3D> profiles, string directory,
            double jointHeightMm, double jointDepthClearanceMm, bool export, string assemblyTemplate, Action<string> progress)
        {
            // Validate the entire grid before opening or writing any output document.
            var cuts = KadaiJointGeometry.Build(plan, profiles, jointHeightMm, jointDepthClearanceMm);
            directory = Path.GetFullPath(directory);
            Require(!Directory.Exists(directory) && !File.Exists(directory), "A new output folder is required.");
            Directory.CreateDirectory(directory);
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var rows = new List<string> { "SlotId,RibId,MatingRibId,LeftMm,RightMm,BottomMm,TopMm" };
            foreach (var profile in profiles)
                foreach (var cut in cuts[profile.RibId])
                    rows.Add(string.Join(",", cut.SlotId, profile.RibId, cut.MatingRibId,
                        (cut.Left * 1000).ToString("R", culture), (cut.Right * 1000).ToString("R", culture),
                        (cut.Bottom * 1000).ToString("R", culture), (cut.Top * 1000).ToString("R", culture)));
            File.WriteAllLines(Path.Combine(directory, "JointPlan.csv"), rows);
            File.WriteAllLines(Path.Combine(directory, "Stations.csv"),
                new[] { "RibId,StationKey" }.Concat(plan.LongRibs.Concat(plan.CrossRibs)
                    .Select(rib => rib.Id + "," + rib.StationKey)));
            foreach (var profile in profiles) {
                string partPath = Path.Combine(directory, profile.RibId + ".SLDPRT");
                progress?.Invoke("START " + profile.RibId);
                var part = WritePart(profile, plan.Settings.BottomMm, partPath, cuts[profile.RibId]);
                try {
                    if (export) {
                        ExportFlatDxf(part, Path.Combine(directory, profile.RibId + ".dxf"));
                        string drawingPath = Path.Combine(directory, profile.RibId + ".SLDDRW");
                        CreateFlatDrawing(part, drawingPath);
                        app.CloseDoc(drawingPath);
                    }
                    progress?.Invoke("PASS " + profile.RibId + " native SheetMetal/Cut/Flat" + (export ? "/Drawing/DXF" : ""));
                } finally { app.CloseDoc(partPath); }
            }
            CreateAssembly(profiles, directory, plan.Settings.BottomMm, assemblyTemplate);
            File.WriteAllText(Path.Combine(directory, "PASS.txt"), "Native prototype only; production gate closed.");
        }
        internal void ExportFlatDxf(ModelDoc2 doc, string path)
        {
            Require(!File.Exists(path), "Refusing to overwrite DXF.");
            doc.ClearSelection2(true);
            Require(((PartDoc)doc).ExportToDWG2(path, doc.GetPathName(), 1, true,
                null, false, false, 1, null), "Flat Pattern DXF export failed.");
            Require(File.Exists(path) && new FileInfo(path).Length > 0, "Missing DXF output.");
        }
        internal void CreateAssembly(List<KadaiProfile3D> profiles, string directory, double bottomMm, string explicitTemplate = null)
        {
            string path = Path.Combine(directory, "Kadai-Prototype.SLDASM");
            Require(!File.Exists(path), "Refusing to overwrite Assembly.");
            string template = explicitTemplate ?? app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);
            Require(File.Exists(template), "Missing Assembly template.");
            var doc = app.NewDocument(template, 0, 0, 0) as ModelDoc2;
            Require(doc != null, "Cannot create Assembly.");
            var openedParts = new List<string>();
            try {
            var assembly = (AssemblyDoc)doc;
            var bom = new List<string> { "RibId,Part,Quantity,Status" };
            foreach (var profile in profiles) {
                string partPath = Path.Combine(directory, profile.RibId + ".SLDPRT");
                int error = 0, warning = 0;
                var part = app.GetOpenDocumentByName(partPath) as ModelDoc2;
                bool openedHere = part == null;
                if (openedHere) part = app.OpenDoc6(partPath, 1, 3, "", ref error, ref warning) as ModelDoc2;
                Require(part != null, "Cannot load prototype Part for Assembly: " + partPath + " error/warning=" + error + "/" + warning);
                Require(string.Equals(part.GetPathName(), partPath, StringComparison.OrdinalIgnoreCase), "Part filename conflict with another open document.");
                if (openedHere) openedParts.Add(partPath);
                var body = Items(((PartDoc)part).GetBodies2(0, false)).OfType<Body2>().Single();
                var box = (double[])body.GetBodyBox();
                double[] a = profile.AlongDirection, h = profile.HeightDirection;
                var n = new[] { a[1] * h[2] - a[2] * h[1], a[2] * h[0] - a[0] * h[2], a[0] * h[1] - a[1] * h[0] };
                var origin = (double[])profile.PointsWorldMeters[0].Clone();
                double localHeight = Dot(origin, h) - bottomMm * .001;
                for (int k = 0; k < 3; k++) origin[k] -= h[k] * localHeight + n[k] * (box[2] + box[5]) / 2;
                var data = a.Concat(h).Concat(n).Concat(origin).Concat(new[] { 1d, 0, 0, 0 }).ToArray();
                var transform = (MathTransform)((MathUtility)app.GetMathUtility()).CreateTransform(data);
                app.ActivateDoc3(doc.GetTitle(), false, 0, ref error);
                var component = assembly.AddComponent5(partPath, 0, "", false, "", 0, 0, 0);
                Require(component != null, "Cannot insert rib component.");
                component.Transform2 = transform;
                var actual = (double[])component.Transform2.ArrayData;
                Require(Enumerable.Range(0, 13).All(i => Math.Abs(actual[i] - data[i]) < 1e-8), "Assembly transform differs from source frame.");
                component.Select4(false, null, false); assembly.FixComponent(); doc.ClearSelection2(true);
                bom.Add(profile.RibId + "," + Path.GetFileName(partPath) + ",1,PROTOTYPE");
            }
            doc.EditRebuild3(); doc.ShowNamedView2("*Isometric", 7); doc.ViewZoomtofit2();
            Require(Items(assembly.GetComponents(false)).Length == profiles.Count, "Unexpected Assembly component count.");
            Require(doc.Extension.NeedsRebuild2 == 0, "Assembly requires a rebuild.");
            var interference = assembly.InterferenceDetectionManager;
            Require(interference != null, "Cannot check native interference.");
            try {
                interference.TreatCoincidenceAsInterference = false;
                interference.IncludeMultibodyPartInterferences = false;
                var collisions = interference.GetInterferences();
                Require(collisions == null || (collisions is Array && ((Array)collisions).Length == 0),
                    "Native Assembly has overlapping rib volumes.");
            } finally { interference.Done(); }
            int saveError = 0, saveWarning = 0;
            Require(doc.Extension.SaveAs(path, 0, 1, null, ref saveError, ref saveWarning) && saveError == 0 && saveWarning == 0,
                "Assembly save failed: " + saveError + "/" + saveWarning);
            File.WriteAllLines(Path.Combine(directory, "BOM.csv"), bom);
            } finally {
                string savedPath = doc.GetPathName();
                app.CloseDoc(string.IsNullOrEmpty(savedPath) ? doc.GetTitle() : savedPath);
                foreach (string partPath in openedParts) app.CloseDoc(partPath);
            }
        }
        internal ModelDoc2 CreateFlatDrawing(ModelDoc2 part, string path)
        {
            Require(!File.Exists(path), "Refusing to overwrite Drawing.");
            string template = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
            Require(File.Exists(template), "Missing Drawing template: " + template);
            var drawing = app.NewDocument(template, 0, 0, 0) as ModelDoc2;
            Require(drawing != null, "Drawing creation failed.");
            try {
            drawing.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swUnitsLinear, (int)swLengthUnit_e.swMM);
            var view = ((DrawingDoc)drawing).CreateFlatPatternViewFromModelView3(part.GetPathName(), "", .15, .15, 0, false, false);
            Require(view != null, "Flat drawing view failed.");
            view.UseSheetScale = 0; view.ScaleDecimal = 1;
            drawing.EditRebuild3();
            var outline = (double[])view.GetOutline();
            double width = outline[2] - outline[0], height = outline[3] - outline[1];
            var sheet = (Sheet)((DrawingDoc)drawing).GetCurrentSheet();
            Require(sheet.SetSize((int)swDwgPaperSizes_e.swDwgPapersUserDefined, width + .1, height + .1), "Cannot size drawing sheet at 1:1.");
            drawing.EditRebuild3(); outline = (double[])view.GetOutline();
            view.PositionLocked = false;
            var position = (double[])view.Position;
            position[0] += .05 - outline[0]; position[1] += .05 - outline[1]; view.Position = position;
            drawing.EditRebuild3();
            outline = (double[])view.GetOutline();
            // GetOutline includes a zoom-dependent display border. Keep 20 mm
            // minimum physical sheet margin rather than comparing border equality.
            Require(outline[0] >= .02 && outline[1] >= .02 && outline[2] <= width + .08 && outline[3] <= height + .08
                && Math.Abs(view.ScaleDecimal - 1) < 1e-9,
                "Flat view falls outside the drawing sheet: " + string.Join(",", outline) + " expected width/height=" + width + "/" + height);
            var note = drawing.InsertNote("KADAI PROTOTYPE - NOT FOR MANUFACTURE. Flat view 1:1, mm.") as Note;
            if (note != null) ((Annotation)note.GetAnnotation()).SetPosition2(.05, .02, 0);
            drawing.ClearSelection2(true); drawing.ViewZoomtofit2();
            int error = 0, warning = 0;
            Require(drawing.Extension.SaveAs(path, 0, 1, null, ref error, ref warning) && error == 0 && warning == 0,
                "Drawing save failed: " + error + "/" + warning);
            return drawing;
            } catch {
                string savedPath = drawing.GetPathName();
                app.CloseDoc(string.IsNullOrEmpty(savedPath) ? drawing.GetTitle() : savedPath);
                throw;
            }
        }
    }
}
