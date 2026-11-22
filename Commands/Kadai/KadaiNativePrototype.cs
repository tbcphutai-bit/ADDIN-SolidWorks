using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.Kadai
{
    // Writes new native documents into a separate package.
    internal sealed class KadaiNativePrototype
    {
        readonly ISldWorks app;
        private readonly string materialDatabasePath;
        private readonly string materialName;
        private readonly string bendTablePath;
        private readonly string partTemplate;
        private readonly string drawingTemplate;

        internal KadaiNativePrototype(ISldWorks app, string materialDatabasePath = null,
            string materialName = null, string bendTablePath = null, string partTemplate = null,
            string drawingTemplate = null)
        {
            this.app = app;
            this.partTemplate = partTemplate;
            this.drawingTemplate = drawingTemplate;
            if (materialDatabasePath == null && materialName == null && bendTablePath == null) return;
            Require(!string.IsNullOrWhiteSpace(materialName), "Specify the native material name.");
            Require(File.Exists(materialDatabasePath) && string.Equals(Path.GetExtension(materialDatabasePath),
                ".sldmat", StringComparison.OrdinalIgnoreCase), "Select an existing SolidWorks material database (.sldmat).");
            Require(File.Exists(bendTablePath), "Select an existing Sheet Metal bend table.");
            this.materialDatabasePath = Path.GetFullPath(materialDatabasePath);
            this.materialName = materialName;
            this.bendTablePath = Path.GetFullPath(bendTablePath);
        }
        static object[] Items(object value) { return value as object[] ?? new object[0]; }
        static double Dot(double[] a, double[] b) { return a.Zip(b, (x, y) => x * y).Sum(); }
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        Feature SketchFeature(ModelDoc2 doc, Sketch sketch)
        {
            for (Feature f = doc.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
                if (f.GetTypeName2() == "ProfileFeature" && app.IsSame(f.GetSpecificFeature2(), sketch) == 1) return f;
            throw new InvalidOperationException("Cannot resolve the new sketch by COM identity.");
        }
        private ModelDoc2 WritePart(KadaiProfile3D profile, double bottomMm, string path, List<KadaiJointCut> cuts, bool prototype)
        {
            Require(!File.Exists(path), "Refusing to overwrite " + path);
            var local = KadaiJointGeometry.LocalPoints(profile, bottomMm);
            string template = partTemplate ?? app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
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
            var allowance = doc.FeatureManager.CreateCustomBendAllowance();
            allowance.Type = (int)swBendAllowanceTypes_e.swBendAllowanceKFactor;
            allowance.KFactor = .5;
            var flange = doc.FeatureManager.InsertSheetMetalBaseFlange2(profile.ThicknessMeters, false,
                .0001, 0, 0, false, 0, 0, 0, allowance, true, 0, 0, 0, .5, true, false, true, true);
            Require(flange != null, "Native Sheet Metal Base Flange failed.");
            flange.Name = "Kadai_BaseFlange_" + profile.RibId;
            if (materialDatabasePath != null) {
                doc.ClearSelection2(true);
                SetBodyBendTable(doc);
                ((PartDoc)doc).SetMaterialPropertyName2(doc.ConfigurationManager.ActiveConfiguration.Name,
                    materialDatabasePath, materialName);
            }
            Feature cut = null;
            if (cuts.Count > 0)
            {
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
            cut = doc.FeatureManager.FeatureCut4(false, false, false, 1, 1, 0, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, false, false, false, 0, 0, false, true);
            Require(cut != null, "Part-level Cut-Extrude failed."); cut.Name = "Kadai_JointCut_" + profile.RibId;
            }
            doc.EditRebuild3();
            Require(doc.Extension.NeedsRebuild2 == 0, "Part requires a rebuild.");
            var bodies = Items(((PartDoc)doc).GetBodies2(0, false)).OfType<Body2>().ToList();
            Require(bodies.Count == 1 && bodies[0].IsSheetMetal(), "Expected one native Sheet Metal body.");
            bool warning;
            if (cut != null) Require(cut.GetErrorCode2(out warning) == 0 && !warning, "Cut rebuild error/warning.");
            Feature flat = null;
            for (Feature f = doc.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
                if (f.GetTypeName2() == "FlatPattern") flat = f;
            Require(flat != null, "Flat Pattern missing.");
            Require(flat.SetSuppression2(1, 1, null), "Cannot unsuppress Flat Pattern."); doc.EditRebuild3();
            Require(flat.GetErrorCode2(out warning) == 0 && !warning, "Flat Pattern failed.");
            Require(flat.SetSuppression2(0, 1, null), "Cannot restore folded Part."); doc.EditRebuild3();
            VerifyManufacturingSettings(doc, profile.ThicknessMeters);
            doc.Extension.CustomPropertyManager[""].Add3("Kadai_RibId", 30, profile.RibId, 2);
            if (prototype) doc.Extension.CustomPropertyManager[""].Add3("Kadai_Status", 30, "PROTOTYPE - NOT FOR MANUFACTURE", 2);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            int error = 0, saveWarning = 0;
            Require(doc.Extension.SaveAs(path, 0, 1, null, ref error, ref saveWarning) && error == 0 && saveWarning == 0,
                "Saving prototype failed: " + error + "/" + saveWarning);
            return doc;
            } catch { try { app.CloseDoc(doc.GetTitle()); } catch { } throw; }
        }

        private static Feature FindSheetMetalFeature(ModelDoc2 doc)
        {
            for (Feature feature = doc.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                if (feature.GetTypeName2() == "SheetMetal") return feature;
            throw new InvalidOperationException("Native Sheet Metal feature missing.");
        }

        private void SetBodyBendTable(ModelDoc2 doc)
        {
            var feature = FindSheetMetalFeature(doc);
            var settings = feature.GetDefinition() as SheetMetalFeatureData;
            Require(settings != null && settings.AccessSelections(doc, null), "Cannot edit native Sheet Metal settings.");
            bool committed = false;
            try
            {
                int status = settings.SetOverrideDefaultParameter2(
                    (int)swSheetMetalOverrideDefaultParameters_e.swSheetMetalOverrideDefaultParameters_BendAllowance, true);
                Require(status == 0, "Cannot override native body bend allowance: " + status);
                // SW 2024 SP5 accepts the company .xlsx through the body feature's direct
                // properties. Passing ICustomBendAllowance into Base Flange drops the file.
                settings.BendAllowanceType = (int)swBendAllowanceTypes_e.swBendAllowanceBendTable;
                settings.BendTableFile = bendTablePath;
                committed = feature.ModifyDefinition(settings, doc, null);
                Require(committed, "SolidWorks rejected the selected Sheet Metal bend table.");
            }
            finally { if (!committed) settings.ReleaseSelectionAccess(); }
        }

        private void VerifyManufacturingSettings(ModelDoc2 doc, double expectedThickness)
        {
            if (materialDatabasePath == null) return;
            string configuration = doc.ConfigurationManager.ActiveConfiguration.Name;
            string database;
            string material = ((PartDoc)doc).GetMaterialPropertyName2(configuration, out database);
            Require(string.Equals(materialName, material, StringComparison.Ordinal)
                && string.Equals(Path.GetFileNameWithoutExtension(materialDatabasePath),
                    Path.GetFileNameWithoutExtension(database), StringComparison.OrdinalIgnoreCase),
                "SolidWorks did not retain the selected custom material. Read " + database + " / " + material);

            var settings = FindSheetMetalFeature(doc).GetDefinition() as SheetMetalFeatureData;
            Require(settings != null && Math.Abs(settings.Thickness - expectedThickness) <= 1e-8,
                "SolidWorks did not retain the specified Sheet Metal thickness.");
            var allowance = settings.GetCustomBendAllowance();
            Require(allowance != null && allowance.Type == (int)swBendAllowanceTypes_e.swBendAllowanceBendTable
                && string.Equals(allowance.BendTableFile, bendTablePath, StringComparison.OrdinalIgnoreCase),
                "SolidWorks did not retain the selected bend table. Read " + allowance?.Type + " / " + allowance?.BendTableFile);
        }

        internal void WriteNewPackage(KadaiPlan plan, List<KadaiProfile3D> profiles, string directory,
            double jointHeightMm, double jointDepthClearanceMm, bool export, string assemblyTemplate, Action<string> progress)
        {
            // Validate the entire grid before opening or writing any output document.
            var cuts = KadaiJointGeometry.Build(plan, profiles, jointHeightMm, jointDepthClearanceMm);
            WritePackage(plan, profiles, cuts, directory, export, assemblyTemplate, progress, null, null);
        }

        internal void WriteLayoutPackage(KadaiPreviewBackend.SlottedLayout layout, string directory,
            string assemblyTemplate, double[] placement, Action<string> progress, KadaiApplyOptions options)
        {
            string name = Path.GetFileName(Path.GetFullPath(directory));
            WritePackage(layout.Plan, layout.Blanks, layout.Joints, directory, true, assemblyTemplate, progress, name, placement, options);
        }

        private static string PartName(string ribId, string packageName)
        {
            return packageName == null ? ribId : packageName + "-" + ribId;
        }

        private void WritePackage(KadaiPlan plan, List<KadaiProfile3D> profiles,
            Dictionary<string, List<KadaiJointCut>> cuts, string directory, bool export, string assemblyTemplate,
            Action<string> progress, string packageName, double[] placement, KadaiApplyOptions options = null)
        {
            directory = Path.GetFullPath(directory);
            Require(!Directory.Exists(directory) && !File.Exists(directory), "A new output folder is required.");
            Directory.CreateDirectory(directory);
            bool prototype = packageName == null;
            string incomplete = Path.Combine(directory, "INCOMPLETE.txt");
            File.WriteAllText(incomplete, "Kadai generation has not completed.");
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
            string assemblyPath = AssemblyPath(directory, packageName);
            ModelDoc2 assemblyDoc = null;
            var bom = new List<string> { "RibId,Part,Quantity,Status" };
            string step = "Tạo Assembly";
            Action<string> markStep = message =>
            {
                step = message;
                File.WriteAllText(incomplete, step);
            };
            try
            {
                markStep(step);
                assemblyDoc = StartAssembly(assemblyPath, assemblyTemplate);
                foreach (var profile in profiles)
                {
                    string name = options == null ? PartName(profile.RibId, packageName) : options.PartName(profile.RibId);
                    string partPath = Path.Combine(directory, name + ".SLDPRT");
                    markStep("Tạo Part " + name);
                    progress?.Invoke(step);
                    var part = WritePart(profile, plan.Settings.BottomMm, partPath, cuts[profile.RibId], prototype);
                    try
                    {
                        if (export)
                        {
                            markStep("Xuất DXF " + name);
                            string dxfPath = Path.Combine(directory, name + ".dxf");
                            if (options == null) ExportFlatDxf(part, dxfPath);
                            else WriteRibDxf(part, dxfPath, profile.ThicknessMeters);
                        }
                        // Insert before the Drawing can close or replace the Part's open document.
                        // AddComponent5 requires the Part to remain loaded.
                        markStep("Chèn vào Assembly " + name);
                        bom.Add(InsertOpenPart(assemblyDoc, part, partPath, profile, plan.Settings.BottomMm,
                            placement, prototype));
                        if (export)
                        {
                            string drawingPath = Path.Combine(directory, name + ".SLDDRW");
                            CreateFlatDrawing(partPath, drawingPath, prototype, detail => markStep(detail + " " + name));
                            markStep("Đóng Drawing " + name);
                            app.CloseDoc(drawingPath);
                        }
                    }
                    finally { app.CloseDoc(partPath); }
                }
                markStep("Kiểm tra và lưu Assembly");
                progress?.Invoke(step);
                SaveAssembly(assemblyDoc, assemblyPath, profiles.Count, bom);
            }
            catch (Exception ex)
            {
                var failure = new InvalidOperationException(step + " — " + ex.Message, ex);
                try { File.WriteAllText(incomplete, failure.ToString()); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw failure;
            }
            finally { if (assemblyDoc != null) CloseAssembly(assemblyDoc); }
            File.WriteAllText(Path.Combine(directory, "PASS.txt"), prototype
                ? "Native prototype only; production gate closed." : "Kadai package completed.");
            File.Delete(incomplete);
        }
        internal void ExportFlatDxf(ModelDoc2 doc, string path)
        {
            Require(!File.Exists(path), "Refusing to overwrite DXF.");
            doc.ClearSelection2(true);
            Require(((PartDoc)doc).ExportToDWG2(path, doc.GetPathName(), 1, true,
                null, false, false, 1, null), "Flat Pattern DXF export failed.");
            Require(File.Exists(path) && new FileInfo(path).Length > 0, "Missing DXF output.");
        }

        private static void WriteRibDxf(ModelDoc2 doc, string path, double thickness)
        {
            // Kadai ribs are unbent single sheets. The two broad faces of the generated
            // Part have the same perimeter, including the actual Part-level slots.
            var body = Items(((PartDoc)doc).GetBodies2(0, false)).OfType<Body2>().Single();
            var face = Items(body.GetFaces()).OfType<Face2>()
                .Where(f => f.Normal is double[] normal && Math.Abs(normal[2]) > .999999)
                .OrderByDescending(f => f.GetArea()).FirstOrDefault();
            Require(face != null, "Không tìm được mặt phẳng của thanh tấm để xuất DXF.");
            var edges = Items(face.GetEdges()).OfType<Edge>().ToArray();
            var segments = new List<double[][]>(edges.Length);
            foreach (var edge in edges)
            {
                var curve = edge.GetCurve() as Curve;
                var start = edge.GetStartVertex() as Vertex;
                var end = edge.GetEndVertex() as Vertex;
                Require(curve != null && curve.IsLine() && start != null && end != null,
                    "Biên thanh có đoạn không phải đường thẳng.");
                segments.Add(new[] { (double[])start.GetPoint(), (double[])end.GetPoint() });
            }
            Require(segments.Count >= 3, "Biên DXF của thanh không đủ cạnh.");
            var points = segments.SelectMany(pair => pair).ToArray();
            double z = points[0][2];
            Require(Items(body.GetVertices()).OfType<Vertex>().Select(v => (double[])v.GetPoint()).All(p =>
            {
                double distance = Math.Abs(p[2] - z);
                return distance < 1e-8 || Math.Abs(distance - thickness) < 1e-8;
            }), "DXF Kadai yêu cầu thanh tấm phẳng có một chiều dày.");

            double minX = points.Min(p => p[0]);
            double maxY = points.Max(p => p[1]);
            using (var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write), Encoding.ASCII))
            {
                writer.WriteLine("0\r\nSECTION\r\n2\r\nHEADER\r\n9\r\n$ACADVER\r\n1\r\nAC1009\r\n9\r\n$INSUNITS\r\n70\r\n4\r\n0\r\nENDSEC");
                writer.WriteLine("0\r\nSECTION\r\n2\r\nENTITIES");
                foreach (var segment in segments)
                {
                    writer.WriteLine("0\r\nLINE\r\n8\r\n0");
                    for (int i = 0; i < segment.Length; i++)
                    {
                        var point = segment[i];
                        writer.WriteLine(10 + i);
                        writer.WriteLine(((point[0] - minX) * 1000).ToString("R", CultureInfo.InvariantCulture));
                        writer.WriteLine(20 + i);
                        writer.WriteLine(((maxY - point[1]) * 1000).ToString("R", CultureInfo.InvariantCulture));
                        writer.WriteLine(30 + i);
                        writer.WriteLine("0");
                    }
                }
                writer.WriteLine("0\r\nENDSEC\r\n0\r\nEOF");
            }
        }
        internal void CreateAssembly(List<KadaiProfile3D> profiles, string directory, double bottomMm, string explicitTemplate = null,
            string packageName = null, double[] placement = null)
        {
            string path = AssemblyPath(directory, packageName);
            var doc = StartAssembly(path, explicitTemplate);
            var openedParts = new List<string>();
            try
            {
                var bom = new List<string> { "RibId,Part,Quantity,Status" };
                foreach (var profile in profiles)
                {
                    string partPath = Path.Combine(directory, PartName(profile.RibId, packageName) + ".SLDPRT");
                    int error = 0, warning = 0;
                    var part = app.GetOpenDocumentByName(partPath) as ModelDoc2;
                    if (part == null)
                    {
                        part = app.OpenDoc6(partPath, 1, 3, "", ref error, ref warning) as ModelDoc2;
                        Require(part != null, "Cannot load prototype Part for Assembly: " + partPath + " error/warning=" + error + "/" + warning);
                        if (string.Equals(part.GetPathName(), partPath, StringComparison.OrdinalIgnoreCase))
                            openedParts.Add(partPath);
                    }
                    bom.Add(InsertOpenPart(doc, part, partPath, profile, bottomMm, placement, packageName == null));
                }
                SaveAssembly(doc, path, profiles.Count, bom);
            }
            finally
            {
                CloseAssembly(doc);
                foreach (string partPath in openedParts) app.CloseDoc(partPath);
            }
        }

        private static string AssemblyPath(string directory, string packageName)
        {
            return Path.Combine(directory, (packageName ?? "Kadai-Prototype") + ".SLDASM");
        }

        private ModelDoc2 StartAssembly(string path, string explicitTemplate)
        {
            Require(!File.Exists(path), "Refusing to overwrite Assembly.");
            string template = explicitTemplate ?? app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);
            Require(File.Exists(template), "Missing Assembly template.");
            var doc = app.NewDocument(template, 0, 0, 0) as ModelDoc2;
            Require(doc != null, "Cannot create Assembly.");
            return doc;
        }

        private string InsertOpenPart(ModelDoc2 doc, ModelDoc2 part, string partPath, KadaiProfile3D profile,
            double bottomMm, double[] placement, bool prototype)
        {
            Require(string.Equals(part.GetPathName(), partPath, StringComparison.OrdinalIgnoreCase), "Part filename conflict with another open document.");
            VerifyManufacturingSettings(part, profile.ThicknessMeters);
            var body = Items(((PartDoc)part).GetBodies2(0, false)).OfType<Body2>().Single();
            var box = (double[])body.GetBodyBox();
            double[] a = profile.AlongDirection, h = profile.HeightDirection;
            var n = new[] { a[1] * h[2] - a[2] * h[1], a[2] * h[0] - a[0] * h[2], a[0] * h[1] - a[1] * h[0] };
            var origin = (double[])profile.PointsWorldMeters[0].Clone();
            double localHeight = Dot(origin, h) - bottomMm * .001;
            for (int k = 0; k < 3; k++) origin[k] -= h[k] * localHeight + n[k] * (box[2] + box[5]) / 2;
            if (placement != null)
            {
                a = KadaiPreviewBackend.IntoPanelFrame(a, placement, null, false);
                h = KadaiPreviewBackend.IntoPanelFrame(h, placement, null, false);
                n = KadaiPreviewBackend.IntoPanelFrame(n, placement, null, false);
                origin = KadaiPreviewBackend.IntoPanelFrame(origin, placement, null, true);
            }
            var data = a.Concat(h).Concat(n).Concat(origin).Concat(new[] { 1d, 0, 0, 0 }).ToArray();
            var transform = (MathTransform)((MathUtility)app.GetMathUtility()).CreateTransform(data);
            ActivateAssembly(doc);
            var assembly = (AssemblyDoc)doc;
            var component = assembly.AddComponent5(partPath, 0, "", false, "", 0, 0, 0);
            Require(component != null, "Cannot insert rib component: " + profile.RibId);
            component.Transform2 = transform;
            var actual = (double[])component.Transform2.ArrayData;
            Require(Enumerable.Range(0, 13).All(i => Math.Abs(actual[i] - data[i]) < 1e-8), "Assembly transform differs from source frame.");
            Require(component.Select4(false, null, false), "Cannot select rib component: " + profile.RibId);
            assembly.FixComponent();
            doc.ClearSelection2(true);
            Require(component.IsFixed(), "Cannot fix rib component: " + profile.RibId);
            string partFile = Path.GetFileName(partPath);
            if (partFile.Contains(",")) partFile = "\"" + partFile + "\"";
            return profile.RibId + "," + partFile + ",1," + (prototype ? "PROTOTYPE" : "KADAI");
        }

        private void ActivateAssembly(ModelDoc2 doc)
        {
            int error = 0;
            var active = app.ActivateDoc3(doc.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error) as ModelDoc2;
            Require(active != null && app.IsSame(active, doc) == 1, "Cannot activate the generated Assembly: " + error);
        }

        private void SaveAssembly(ModelDoc2 doc, string path, int componentCount, List<string> bom)
        {
            ActivateAssembly(doc);
            var assembly = (AssemblyDoc)doc;
            doc.EditRebuild3(); doc.ShowNamedView2("*Isometric", 7); doc.ViewZoomtofit2();
            Require(Items(assembly.GetComponents(false)).Length == componentCount, "Unexpected Assembly component count.");
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
            File.WriteAllLines(Path.Combine(Path.GetDirectoryName(path), "BOM.csv"), bom);
        }

        private void CloseAssembly(ModelDoc2 doc)
        {
            string savedPath = doc.GetPathName();
            app.CloseDoc(string.IsNullOrEmpty(savedPath) ? doc.GetTitle() : savedPath);
        }
        internal ModelDoc2 CreateFlatDrawing(ModelDoc2 part, string path, bool prototype = true, Action<string> markStep = null)
        {
            return CreateFlatDrawing(part.GetPathName(), path, prototype, markStep);
        }

        private ModelDoc2 CreateFlatDrawing(string partPath, string path, bool prototype, Action<string> markStep)
        {
            Require(!File.Exists(path), "Refusing to overwrite Drawing.");
            string template = drawingTemplate ?? app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
            Require(File.Exists(template), "Missing Drawing template: " + template);
            markStep?.Invoke("Mở template Drawing");
            var drawing = app.NewDocument(template, 0, 0, 0) as ModelDoc2;
            Require(drawing != null, "Drawing creation failed.");
            try {
            drawing.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swUnitsLinear, (int)swLengthUnit_e.swMM);
            markStep?.Invoke("Tạo hình chiếu Flat Pattern");
            var view = ((DrawingDoc)drawing).CreateFlatPatternViewFromModelView3(partPath, "", .15, .15, 0, false, false);
            Require(view != null, "Flat drawing view failed.");
            if (drawingTemplate != null) FitFlatViewToTemplate(drawing, view, markStep);
            else
            {
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
            var note = drawing.InsertNote(prototype ? "KADAI PROTOTYPE - NOT FOR MANUFACTURE. Flat view 1:1, mm."
                : "KADAI - Flat view 1:1, mm.") as Note;
            if (note != null) ((Annotation)note.GetAnnotation()).SetPosition2(.05, .02, 0);
            }
            markStep?.Invoke("Lưu Drawing");
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

        private static void FitFlatViewToTemplate(ModelDoc2 drawing, View view, Action<string> markStep)
        {
            markStep?.Invoke("Đo khổ giấy và hình chiếu");
            var sheet = (Sheet)((DrawingDoc)drawing).GetCurrentSheet();
            double width = 0, height = 0;
            sheet.GetSize(ref width, ref height);
            Require(width > 0 && height > 0, "Khổ giấy của template bản vẽ không hợp lệ.");

            view.UseSheetScale = 1;
            drawing.EditRebuild3();
            var bounds = (double[])view.GetOutline();
            double viewWidth = bounds[2] - bounds[0], viewHeight = bounds[3] - bounds[1];
            Require(viewWidth > 0 && viewHeight > 0, "Không đo được hình trải phẳng.");
            // Reserve space for the template border and title block.
            double fit = Math.Min(width * .80 / viewWidth, height * .57 / viewHeight);
            if (fit < 1)
            {
                double targetScale = view.ScaleDecimal * fit;
                Require(targetScale > 0 && !double.IsInfinity(targetScale), "Tỉ lệ hình chiếu không hợp lệ.");
                markStep?.Invoke("Điều chỉnh tỉ lệ hình chiếu");
                view.UseSheetScale = 0;
                view.ScaleDecimal = 1 / Math.Ceiling(1 / targetScale);
                drawing.EditRebuild3();
                bounds = (double[])view.GetOutline();
            }
            markStep?.Invoke("Đặt vị trí hình chiếu trên giấy");
            view.PositionLocked = false;
            var position = (double[])view.Position;
            position[0] += width * .5 - (bounds[0] + bounds[2]) / 2;
            position[1] += height * .60 - (bounds[1] + bounds[3]) / 2;
            view.Position = position;
            drawing.EditRebuild3();
            bounds = (double[])view.GetOutline();
            Require(bounds[0] >= 0 && bounds[1] >= 0 && bounds[2] <= width && bounds[3] <= height,
                "Hình trải phẳng vượt khổ giấy của template đã chọn.");
        }
    }
}
