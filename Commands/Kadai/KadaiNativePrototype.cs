using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.Kadai
{
    // Writes new native documents into a separate package.
    internal sealed partial class KadaiNativePrototype
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
        private ModelDoc2 WritePart(KadaiProfile3D profile, double bottomMm, string path, List<KadaiJointCut> cuts,
            bool prototype, int uraNormal, bool opensAtBottom, Func<ModelDoc2, Dictionary<string, Feature>> addReferences)
        {
            Require(!File.Exists(path), "Refusing to overwrite " + path);
            var local = KadaiJointGeometry.LocalPoints(profile, bottomMm);
            string template = partTemplate ?? app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            Require(File.Exists(template), "Missing native Part template: " + template);
            var doc = app.NewDocument(template, 0, 0, 0) as ModelDoc2;
            Require(doc != null, "Cannot create native Part.");
            try {
            doc.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swUnitsLinear, (int)swLengthUnit_e.swMM);
            var surfaces = addReferences?.Invoke(doc);
            Feature plane = null;
            for (Feature f = doc.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
                if (f.GetTypeName2() == "RefPlane") { plane = f; break; }
            Require(plane != null && plane.Select2(false, 0), "Cannot select native sketch plane.");
            var sketch = doc.SketchManager;
            sketch.InsertSketch(true);
            var profileSketchObject = sketch.ActiveSketch;
            double[] endTrim = null;
            try {
                if (surfaces != null && surfaces.TryGetValue(PanelSurfaceKey, out Feature panelSurface))
                    endTrim = DrawLinkedPanelProfile(doc, profileSketchObject, panelSurface, local);
                else
                    DrawPolygon(doc, local);
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
            if (endTrim != null)
                TrimLinkedProfile(doc, plane, profile.RibId, local, endTrim);
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
                if (surfaces != null)
                    DrawLinkedSlots(doc, slotSketchObject, cuts, opensAtBottom, surfaces);
                else
                    foreach (var slot in cuts) DrawPolygon(doc, Rectangle(slot.Left, slot.Bottom, slot.Right, slot.Top));
            } finally { sketch.AddToDB = false; sketch.InsertSketch(true); }
            var slotSketch = SketchFeature(doc, slotSketchObject);
            Require(slotSketch != null && slotSketch.Select2(false, 0), "Slot sketch missing.");
            slotSketch.Name = "Kadai_Slots_" + profile.RibId;
            // Through-all both directions, NormalCut=true, single native Part body.
            cut = CutThroughSheet(doc, "Kadai_JointCut_" + profile.RibId);
            }
            doc.EditRebuild3();
            Require(doc.Extension.NeedsRebuild2 == 0, "Part requires a rebuild.");
            var bodies = Items(((PartDoc)doc).GetBodies2(0, false)).OfType<Body2>().ToList();
            Require(bodies.Count == 1 && bodies[0].IsSheetMetal(), "Expected one native Sheet Metal body.");
            if (surfaces != null)
            {
                var box = (double[])bodies[0].GetBodyBox();
                Require(Math.Abs(box[2] + box[5] + profile.ThicknessMeters) < .00002,
                    "Độ dày Sheet Metal lệch khỏi mặt phẳng đã đặt thanh.");
                foreach (var surface in surfaces.Values)
                    ((Body2)((Face2)Items(surface.GetFaces()).Single()).GetBody()).HideBody(true);
            }
            bool warning;
            if (cut != null) Require(cut.GetErrorCode2(out warning) == 0 && !warning, "Cut rebuild error/warning.");
            var flat = FindFlatPattern(doc);
            Require(flat.SetSuppression2(1, 1, null), "Cannot unsuppress Flat Pattern."); doc.EditRebuild3();
            Require(flat.GetErrorCode2(out warning) == 0 && !warning, "Flat Pattern failed.");
            Require(flat.SetSuppression2(0, 1, null), "Cannot restore folded Part."); doc.EditRebuild3();
            VerifyManufacturingSettings(doc, profile.ThicknessMeters);
            if (uraNormal != 0) PaintUra(doc, uraNormal);
            doc.Extension.CustomPropertyManager[""].Add3("Kadai_RibId", 30, profile.RibId, 2);
            if (prototype) doc.Extension.CustomPropertyManager[""].Add3("Kadai_Status", 30, "PROTOTYPE - NOT FOR MANUFACTURE", 2);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            int error = 0, saveWarning = 0;
            bool saved = surfaces == null
                ? doc.Extension.SaveAs(path, 0, 1, null, ref error, ref saveWarning)
                : doc.Save3(1, ref error, ref saveWarning);
            Require(saved && error == 0 && saveWarning == 0,
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

        private static Feature FindFlatPattern(ModelDoc2 doc)
        {
            for (var feature = doc.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                if (feature.GetTypeName2() == "FlatPattern") return feature;
            throw new InvalidOperationException("Part chưa có feature Flat Pattern.");
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
            VerifyMaterial((PartDoc)doc, doc.ConfigurationManager.ActiveConfiguration.Name);

            var settings = FindSheetMetalFeature(doc).GetDefinition() as SheetMetalFeatureData;
            Require(settings != null && Math.Abs(settings.Thickness - expectedThickness) <= 1e-8,
                "SolidWorks did not retain the specified Sheet Metal thickness.");
            var allowance = settings.GetCustomBendAllowance();
            Require(allowance != null && allowance.Type == (int)swBendAllowanceTypes_e.swBendAllowanceBendTable
                && string.Equals(allowance.BendTableFile, bendTablePath, StringComparison.OrdinalIgnoreCase),
                "SolidWorks did not retain the selected bend table. Read " + allowance?.Type + " / " + allowance?.BendTableFile);
        }

        private void VerifyMaterial(PartDoc part, string configuration)
        {
            if (materialDatabasePath == null) return;
            string material = part.GetMaterialPropertyName2(configuration, out string database);
            Require(string.Equals(materialName, material, StringComparison.Ordinal)
                && string.Equals(Path.GetFileNameWithoutExtension(materialDatabasePath),
                    Path.GetFileNameWithoutExtension(database), StringComparison.OrdinalIgnoreCase),
                "SolidWorks không giữ vật liệu đã chọn trong cấu hình " + configuration + ". Đọc được " + database + " / " + material + ".");
        }

        internal void WriteNewPackage(KadaiPlan plan, List<KadaiProfile3D> profiles, string directory,
            double jointHeightMm, double jointDepthClearanceMm, bool export, string assemblyTemplate, Action<string> progress)
        {
            // Validate the entire grid before opening or writing any output document.
            var cuts = KadaiJointGeometry.Build(plan, profiles, jointHeightMm, jointDepthClearanceMm);
            WritePackage(plan, profiles, cuts, directory, export, assemblyTemplate, progress);
        }

        internal void InsertLayout(KadaiPreviewBackend.SlottedLayout layout, ModelDoc2 targetAssembly,
            string directory, KadaiPanelSource panel, Action<string> progress, KadaiApplyOptions options, Action<ModelDoc2, View> dimensionView)
        {
            Require(targetAssembly is AssemblyDoc, "Chọn Assembly đích để chèn thanh Kadai.");
            WritePackage(layout.Plan, layout.Blanks, layout.Joints, directory, true, null, progress,
                targetAssembly, panel, options, dimensionView);
        }

        private static string PartName(string ribId, string packageName)
        {
            return packageName == null ? ribId : packageName + "-" + ribId;
        }

        private void WritePackage(KadaiPlan plan, List<KadaiProfile3D> profiles,
            Dictionary<string, List<KadaiJointCut>> cuts, string directory, bool export, string assemblyTemplate,
            Action<string> progress, ModelDoc2 targetAssembly = null, KadaiPanelSource panel = null,
            KadaiApplyOptions options = null, Action<ModelDoc2, View> dimensionView = null)
        {
            directory = Path.GetFullPath(directory);
            Require(!Directory.Exists(directory) && !File.Exists(directory), "A new output folder is required.");
            Directory.CreateDirectory(directory);
            bool prototype = targetAssembly == null;
            if (!prototype)
            {
                Require(panel?.PanelComponent != null && !string.IsNullOrEmpty(targetAssembly.GetPathName()),
                    "Lưu Assembly trước để SolidWorks giữ tham chiếu mặt panel.");
                Require(!app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swExternalReferencesDisable)
                    && app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAllowCreationOfReferencesExternalToModel),
                    "Bật tạo External References trong System Options của SolidWorks để tạo thanh Kadai liên kết.");
                profiles = profiles.OrderByDescending(profile => plan.Settings.OpensAtBottom(profile.Direction)).ToList();
            }
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
            ModelDoc2 assemblyDoc = targetAssembly;
            var added = new List<Component2>(profiles.Count);
            var componentsById = new Dictionary<string, Component2>();
            var bom = new List<string> { "RibId,Part,Quantity,Status" };
            string complete = Path.Combine(directory, "PASS.txt");
            string step = prototype ? "Tạo Assembly" : "Kích hoạt Assembly đích";
            Action<string> markStep = message =>
            {
                step = message;
                File.WriteAllText(incomplete, step);
            };
            try
            {
                markStep(step);
                if (prototype) assemblyDoc = StartAssembly(AssemblyPath(directory, null), assemblyTemplate);
                else ActivateAssembly(assemblyDoc);
                foreach (var profile in profiles)
                {
                    string name = options == null ? profile.RibId : options.PartName(profile.RibId);
                    int uraNormal = options == null ? 0 :
                        (int)options.UraSide(profile.RibId) * Math.Sign(Dot(PartNormal(profile), profile.ExtrusionDirection));
                    string partPath = Path.Combine(directory, name + ".SLDPRT");
                    markStep("Tạo Part " + name);
                    progress?.Invoke(step);
                    bool opensAtBottom = plan.Settings.OpensAtBottom(profile.Direction);
                    Func<ModelDoc2, Dictionary<string, Feature>> addReferences = null;
                    if (!prototype)
                    {
                        addReferences = partDoc =>
                        {
                            SaveBlankPart(partDoc, partPath);
                            var component = InsertPartAtTransform(assemblyDoc, partPath, profile, plan.Settings.BottomMm,
                                panel.ComponentPlacement, -profile.ThicknessMeters * .5, added);
                            componentsById.Add(profile.RibId, component);
                            markStep("Gắn tham chiếu " + name);
                            return AddLinkedFaces(assemblyDoc, partDoc, component, profile, cuts[profile.RibId],
                                panel, opensAtBottom, componentsById, cuts);
                        };
                    }
                    var part = WritePart(profile, plan.Settings.BottomMm, partPath, cuts[profile.RibId], prototype, uraNormal,
                        opensAtBottom, addReferences);
                    try
                    {
                        if (prototype && export)
                        {
                            markStep("Xuất DXF " + name);
                            ExportFlatDxf(part, Path.Combine(directory, name + ".dxf"));
                        }
                        // Insert before the Drawing can close or replace the Part's open document.
                        // AddComponent5 requires the Part to remain loaded.
                        markStep("Chèn vào Assembly " + name);
                        if (prototype)
                            bom.Add(InsertOpenPart(assemblyDoc, part, partPath, profile, plan.Settings.BottomMm,
                                null, true, added));
                        else
                            bom.Add(BomRow(profile.RibId, partPath, false));
                        if (export)
                        {
                            string drawingPath = Path.Combine(directory, name + ".SLDDRW");
                            CreateFlatDrawing(partPath, drawingPath, prototype,
                                detail => markStep(detail + " " + name), part, uraNormal, dimensionView);
                            markStep("Đóng Drawing " + name);
                            app.CloseDoc(drawingPath);
                        }
                    }
                    finally { app.CloseDoc(partPath); }
                }
                markStep(prototype ? "Kiểm tra và lưu Assembly" : "Kiểm tra các thanh vừa chèn");
                progress?.Invoke(step);
                if (prototype) SaveAssembly(assemblyDoc, AssemblyPath(directory, null), profiles.Count, bom);
                else
                {
                    VerifyInsertedRibs(assemblyDoc, added, profiles, markStep);
                    File.WriteAllLines(Path.Combine(directory, "BOM.csv"), bom);
                }
                File.WriteAllText(complete, prototype ? "Native prototype only; production gate closed." : "Kadai components inserted.");
                File.Delete(incomplete);
            }
            catch (Exception ex)
            {
                var failure = new InvalidOperationException(step + " — " + ex.Message, ex);
                if (!prototype && added.Count > 0)
                {
                    try { RemoveInsertedRibs(assemblyDoc, added); }
                    catch (Exception rollback)
                    {
                        failure = new InvalidOperationException(failure.Message +
                            "\nKhông thể gỡ hết các thanh Kadai vừa chèn. Hãy kiểm tra Assembly trước khi lưu.",
                            new AggregateException(failure, rollback));
                    }
                }
                try
                {
                    File.WriteAllText(incomplete, failure.ToString());
                    File.Delete(complete);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw failure;
            }
            finally { if (prototype && assemblyDoc != null) CloseAssembly(assemblyDoc); }
        }
        internal void ExportFlatDxf(ModelDoc2 doc, string path)
        {
            Require(!File.Exists(path), "Refusing to overwrite DXF.");
            doc.ClearSelection2(true);
            Require(((PartDoc)doc).ExportToDWG2(path, doc.GetPathName(), 1, true,
                null, false, false, 1, null), "Flat Pattern DXF export failed.");
            Require(File.Exists(path) && new FileInfo(path).Length > 0, "Missing DXF output.");
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
            double bottomMm, double[] placement, bool prototype, List<Component2> added = null)
        {
            Require(string.Equals(part.GetPathName(), partPath, StringComparison.OrdinalIgnoreCase), "Part filename conflict with another open document.");
            VerifyManufacturingSettings(part, profile.ThicknessMeters);
            var body = Items(((PartDoc)part).GetBodies2(0, false)).OfType<Body2>().Single();
            var box = (double[])body.GetBodyBox();
            InsertPartAtTransform(doc, partPath, profile, bottomMm, placement, (box[2] + box[5]) / 2, added);
            return BomRow(profile.RibId, partPath, prototype);
        }

        private Component2 InsertPartAtTransform(ModelDoc2 doc, string partPath, KadaiProfile3D profile,
            double bottomMm, double[] placement, double centerZ, List<Component2> added)
        {
            double[] a = profile.AlongDirection, h = profile.HeightDirection;
            var n = PartNormal(profile);
            var origin = (double[])profile.PointsWorldMeters[0].Clone();
            double localHeight = Dot(origin, h) - bottomMm * .001;
            for (int k = 0; k < 3; k++) origin[k] -= h[k] * localHeight + n[k] * centerZ;
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
            added?.Add(component);
            component.Transform2 = transform;
            var actual = (double[])component.Transform2.ArrayData;
            Require(Enumerable.Range(0, 13).All(i => Math.Abs(actual[i] - data[i]) < 1e-8), "Assembly transform differs from source frame.");
            Require(component.Select4(false, null, false), "Cannot select rib component: " + profile.RibId);
            assembly.FixComponent();
            doc.ClearSelection2(true);
            Require(component.IsFixed(), "Cannot fix rib component: " + profile.RibId);
            return component;
        }

        private static string BomRow(string ribId, string partPath, bool prototype)
        {
            string partFile = Path.GetFileName(partPath);
            if (partFile.Contains(",")) partFile = "\"" + partFile + "\"";
            return ribId + "," + partFile + ",1," + (prototype ? "PROTOTYPE" : "KADAI");
        }

        private void ActivateAssembly(ModelDoc2 doc)
        {
            int error = 0;
            var active = app.ActivateDoc3(doc.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error) as ModelDoc2;
            Require(active != null && app.IsSame(active, doc) == 1, "Không kích hoạt được Assembly đích: " + error);
        }

        private static double[] PartNormal(KadaiProfile3D profile)
        {
            var a = profile.AlongDirection;
            var h = profile.HeightDirection;
            return new[] { a[1] * h[2] - a[2] * h[1], a[2] * h[0] - a[0] * h[2], a[0] * h[1] - a[1] * h[0] };
        }

        private static void PaintUra(ModelDoc2 part, int uraNormal)
        {
            var body = Items(((PartDoc)part).GetBodies2(0, false)).OfType<Body2>().Single();
            var face = Items(body.GetFaces()).OfType<Face2>().Single(candidate =>
                candidate.Normal is double[] normal && uraNormal * normal[2] > .999999);
            var pink = KadaiUraAppearance.MaterialValues();
            int configuration = (int)swInConfigurationOpts_e.swThisConfiguration;
            face.SetMaterialPropertyValues2(pink, configuration, null);
            var actual = face.GetMaterialPropertyValues2(configuration, null) as double[];
            Require(actual != null && actual.Length == pink.Length &&
                actual.Zip(pink, (a, b) => Math.Abs(a - b)).All(delta => delta < .005),
                "SolidWorks không giữ màu hồng của mặt ウラ.");
        }

        private void VerifyInsertedRibs(ModelDoc2 doc, List<Component2> added, List<KadaiProfile3D> profiles,
            Action<string> markStep)
        {
            ActivateAssembly(doc);
            doc.EditRebuild3();
            var assembly = (AssemblyDoc)doc;
            var paths = new HashSet<string>(Items(assembly.GetComponents(true)).OfType<Component2>()
                .Select(component => component.GetPathName()), StringComparer.OrdinalIgnoreCase);
            Require(added.Count == profiles.Count && added.All(component =>
                component.IsFixed() && paths.Contains(component.GetPathName())),
                "Thiếu thanh mới hoặc có thanh chưa được cố định trong Assembly.");
            markStep("Kiểm tra giao thể tích các thanh dọc và ngang");
            var longitudinal = new List<Body2>();
            var transverse = new List<Body2>();
            try
            {
                for (int i = 0; i < added.Count; i++)
                {
                    var component = added[i];
                    var part = component.GetModelDoc2() as PartDoc;
                    Require(part != null, "Không đọc được Part vừa chèn để kiểm tra giao thể tích.");
                    var modelBody = Items(part.GetBodies2((int)swBodyType_e.swSolidBody, false)).OfType<Body2>().Single();
                    var copy = modelBody.Copy() as Body2;
                    Require(copy != null, "Không sao chép được body để kiểm tra giao thể tích.");
                    var group = profiles[i].Direction == KadaiRibDirection.Longitudinal ? longitudinal : transverse;
                    group.Add(copy);
                    Require(copy.ApplyTransform(component.Transform2), "Không đưa được body vào hệ tọa độ Assembly.");
                }
                if (longitudinal.Count > 0 && transverse.Count > 0)
                {
                    var modeler = (Modeler)app.GetModeler();
                    object facesA = null, facesB = null, common = null;
                    Require(!modeler.CheckInterference3(longitudinal.ToArray(), transverse.ToArray(), 0, ref facesA, ref facesB, ref common),
                        "Các thanh Kadai vừa chèn bị giao thể tích.");
                }
            }
            finally
            {
                foreach (var copy in longitudinal.Concat(transverse)) Marshal.ReleaseComObject(copy);
            }
            doc.GraphicsRedraw2();
        }

        private void RemoveInsertedRibs(ModelDoc2 doc, List<Component2> added)
        {
            ActivateAssembly(doc);
            var assembly = (AssemblyDoc)doc;
            var paths = new HashSet<string>(added.Select(component => component.GetPathName()),
                StringComparer.OrdinalIgnoreCase);
            doc.ClearSelection2(true);
            try
            {
                foreach (var component in added)
                    Require(component.Select4(true, null, false), "Không chọn được thanh vừa chèn để hoàn tác.");
                Require(((SelectionMgr)doc.SelectionManager).GetSelectedObjectCount2(-1) == added.Count,
                    "Lựa chọn hoàn tác không khớp số thanh vừa chèn.");
                Require(assembly.DeleteSelections((int)swAssemblyDeleteOptions_e.swDelete_SelectedComponents),
                    "SolidWorks không gỡ được các thanh vừa chèn.");
                Require(!Items(assembly.GetComponents(true)).OfType<Component2>()
                    .Any(component => paths.Contains(component.GetPathName())),
                    "Assembly vẫn còn thanh Kadai chưa gỡ.");
                doc.EditRebuild3();
                doc.GraphicsRedraw2();
            }
            finally { doc.ClearSelection2(true); }
        }

        private void SaveAssembly(ModelDoc2 doc, string path, int componentCount, List<string> bom)
        {
            ActivateAssembly(doc);
            var assembly = (AssemblyDoc)doc;
            doc.EditRebuild3(); doc.ShowNamedView2("*Isometric", 7); doc.ViewZoomtofit2();
            Require(Items(assembly.GetComponents(false)).Length == componentCount, "Unexpected Assembly component count.");
            Require(doc.Extension.NeedsRebuild2 == 0, "Assembly requires a rebuild.");
            VerifyRibInterference(assembly);
            int saveError = 0, saveWarning = 0;
            Require(doc.Extension.SaveAs(path, 0, 1, null, ref saveError, ref saveWarning) && saveError == 0 && saveWarning == 0,
                "Assembly save failed: " + saveError + "/" + saveWarning);
            File.WriteAllLines(Path.Combine(Path.GetDirectoryName(path), "BOM.csv"), bom);
        }

        private static void VerifyRibInterference(AssemblyDoc assembly)
        {
            var interference = assembly.InterferenceDetectionManager;
            Require(interference != null, "Cannot check native interference.");
            try {
                interference.TreatCoincidenceAsInterference = false;
                interference.IncludeMultibodyPartInterferences = false;
                var collisions = interference.GetInterferences();
                Require(collisions == null || (collisions is Array && ((Array)collisions).Length == 0),
                    "Các thanh Kadai vừa chèn bị giao thể tích.");
            } finally { interference.Done(); }
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

        private ModelDoc2 CreateFlatDrawing(string partPath, string path, bool prototype, Action<string> markStep,
            ModelDoc2 part = null, int uraNormal = 0, Action<ModelDoc2, View> dimensionView = null)
        {
            Require(!File.Exists(path), "Refusing to overwrite Drawing.");
            string template = drawingTemplate ?? app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
            Require(File.Exists(template), "Missing Drawing template: " + template);
            markStep?.Invoke("Mở template Drawing");
            var drawing = app.NewDocument(template, 0, 0, 0) as ModelDoc2;
            Require(drawing != null, "Drawing creation failed.");
            try {
            drawing.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swUnitsLinear, (int)swLengthUnit_e.swMM);
            var drawingDoc = (DrawingDoc)drawing;
            var templateSlot = uraNormal != 0 ? TakeTemplateFlatSlot(drawing, drawingDoc) : null;
            markStep?.Invoke("Tạo hình chiếu Flat Pattern");
            var center = templateSlot?.Position;
            var view = drawingDoc.CreateFlatPatternViewFromModelView3(partPath, "", center?[0] ?? .15, center?[1] ?? .15, 0, false, false);
            Require(view != null, "Flat drawing view failed.");
            if (uraNormal != 0)
            {
                markStep?.Invoke("Gán vật liệu và tô mặt ウラ trong cấu hình 展開");
                PrepareFlatConfiguration(part, view.ReferencedConfiguration, uraNormal);
                int activationError = 0;
                app.ActivateDoc3(drawing.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activationError);
                markStep?.Invoke("Định hướng mặt 表");
                if (UraFacesViewer(view, uraNormal)) view.FlipView = !view.FlipView;
                drawing.EditRebuild3();
                Require(view.IsFlatPatternView() && !UraFacesViewer(view, uraNormal), "View 展開 chưa nhìn mặt 表.");
                if (templateSlot == null) SetJapaneseViewName(drawingDoc, view);
                else
                {
                    Require(view.SetName2(templateSlot.Name), "Không đặt được tên view số 13 theo template.");
                    view.UseSheetScale = 0;
                    view.ScaleDecimal = templateSlot.Scale;
                    ((Sheet)drawingDoc.GetCurrentSheet()).CustomPropertyView = view.GetName2();
                    drawing.EditRebuild3();
                }
            }
            if (drawingTemplate != null) FitFlatViewToTemplate(drawing, view, markStep, center);
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
            if (uraNormal != 0)
            {
                markStep?.Invoke("Hiện Bounding Box và chạy DIM Kegaki");
                drawing.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplaySketches, true);
                string viewName = view.GetName2();
                Require(dimensionView != null, "Chưa kết nối lệnh DIM Kegaki.");
                dimensionView(drawing, view);
                view = Items(((DrawingDoc)drawing).GetViews()).SelectMany(Items).OfType<View>()
                    .Single(candidate => candidate.GetName2() == viewName);
                var boundingBox = view.GetSMBoundaryBoxDisplayData2() as DisplayData;
                Require(boundingBox != null && boundingBox.GetLineCount() >= 4, "Bounding Box của view 展開 chưa hiện.");
                Require(view.GetDisplayDimensionCount() == 2, "DIM Kegaki chưa tạo đủ hai kích thước W/L.");
                AddOmoteMark(drawing, view);
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

        private sealed class TemplateFlatSlot
        {
            internal string Name;
            internal double[] Position;
            internal double Scale;
        }

        private static IEnumerable<View> ActiveModelViews(DrawingDoc drawing)
        {
            var sheetView = drawing.GetFirstView() as View;
            for (var view = sheetView?.GetNextView() as View; view != null; view = view.GetNextView() as View)
                yield return view;
        }

        private static TemplateFlatSlot TakeTemplateFlatSlot(ModelDoc2 doc, DrawingDoc drawing)
        {
            var views = ActiveModelViews(drawing).ToArray();
            var view13 = views.FirstOrDefault(view => IsEmptyTemplateView(view, "13"));
            if (view13 == null) return null;
            var view12 = views.FirstOrDefault(view => IsEmptyTemplateView(view, "12"));
            var slot = new TemplateFlatSlot
            {
                Name = view13.GetName2(), Position = (double[])view13.Position, Scale = view13.ScaleDecimal
            };
            var names = new[] { view12?.GetName2(), slot.Name }.Where(name => name != null).ToArray();
            foreach (var view in views)
            {
                if (names.Contains(view.GetName2())) continue;
                var parent = view.GetBaseView() as View;
                Require(parent == null || !names.Contains(parent.GetName2()),
                    "View mẫu 12 hoặc 13 đang có một hình chiếu phụ thuộc.");
            }

            // Filling template view 13 only creates an isometric view, even with the flat configuration.
            // Reuse its slot and name with a native Flat Pattern view so the drawing has a real boundary box.
            doc.ClearSelection2(true);
            foreach (string name in names)
                Require(doc.Extension.SelectByID2(name, "DRAWINGVIEW", 0, 0, 0, true, 0, null, 0),
                    "Không chọn được view mẫu " + name + ".");
            Require(doc.Extension.DeleteSelection2(0), "Không thay được view mẫu 12/13.");
            doc.ClearSelection2(true);
            Require(!ActiveModelViews(drawing).Any(view => names.Contains(view.GetName2())),
                "View mẫu 12/13 vẫn còn trên Drawing.");
            return slot;
        }

        private static bool IsEmptyTemplateView(View view, string number)
        {
            string name = view.GetName2().Normalize(NormalizationForm.FormKC).Replace(" ", "").Replace("-", "");
            return (name == "図面ビュー" + number || string.Equals(name, "DrawingView" + number, StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrEmpty(view.GetReferencedModelName());
        }

        private void PrepareFlatConfiguration(ModelDoc2 part, string flatConfiguration, int uraNormal)
        {
            var partDoc = (PartDoc)part;
            string folded = part.ConfigurationManager.ActiveConfiguration.Name;
            Require(!string.IsNullOrEmpty(flatConfiguration) && folded != flatConfiguration,
                "Drawing không tham chiếu cấu hình Flat Pattern riêng.");
            Require(part.ShowConfiguration2(flatConfiguration), "Không mở được cấu hình Flat Pattern của Part.");
            try
            {
                var configuration = part.ConfigurationManager.ActiveConfiguration;
                Require(configuration.Name == flatConfiguration && configuration.IsDerived(),
                    "View 展開 cần tham chiếu cấu hình phái sinh của Part.");
                var flat = FindFlatPattern(part);
                int thisConfiguration = (int)swInConfigurationOpts_e.swThisConfiguration;
                if (IsFlatSuppressed(flat, thisConfiguration))
                    Require(flat.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature, thisConfiguration, null),
                        "Không bật được feature Flat Pattern trong cấu hình 展開.");
                part.EditRebuild3();
                Require(!IsFlatSuppressed(flat, thisConfiguration) && flat.GetErrorCode2(out bool featureWarning) == 0 && !featureWarning,
                    "Cấu hình 展開 vẫn chưa mở phẳng được tấm.");
                // SolidWorks may copy the material override from the Part template into the flat configuration.
                if (materialDatabasePath != null)
                    partDoc.SetMaterialPropertyName2(flatConfiguration, materialDatabasePath, materialName);
                VerifyMaterial(partDoc, flatConfiguration);
                PaintUra(part, uraNormal);
            }
            finally { Require(part.ShowConfiguration2(folded), "Không khôi phục được cấu hình ban đầu của Part."); }
            // Creating or rebuilding the derived configuration can replace the folded face.
            PaintUra(part, uraNormal);
            VerifyMaterial(partDoc, folded);
            VerifyMaterial(partDoc, flatConfiguration);
            int error = 0, warning = 0;
            Require(part.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref error, ref warning) && error == 0,
                "Không lưu được vật liệu và màu mặt của Part: " + error + "/" + warning);
        }

        private static bool IsFlatSuppressed(Feature flat, int option)
        {
            var states = flat.IsSuppressed2(option, null) as Array;
            Require(states != null && states.Length == 1, "Không đọc được trạng thái Flat Pattern.");
            return Convert.ToBoolean(states.GetValue(states.GetLowerBound(0)));
        }

        private bool UraFacesViewer(View view, int uraNormal)
        {
            var vector = ((MathUtility)app.GetMathUtility()).CreateVector(new[] { 0d, 0, (double)uraNormal }) as MathVector;
            var inView = vector?.MultiplyTransform(view.ModelToViewTransform) as MathVector;
            var direction = inView?.ArrayData as double[];
            Require(direction != null && Math.Abs(direction[2]) > 1e-9 &&
                Math.Abs(direction[0]) + Math.Abs(direction[1]) < Math.Abs(direction[2]) * .001,
                "Không đọc được hướng mặt tấm của view 展開.");
            return direction[2] > 0;
        }

        private static void SetJapaneseViewName(DrawingDoc drawing, View view)
        {
            const string prefix = "図面ビュー";
            if (view.GetName2().StartsWith(prefix, StringComparison.Ordinal)) return;
            var usedNames = new HashSet<string>(Items(drawing.GetViews()).SelectMany(Items).OfType<View>()
                .Select(candidate => candidate.GetName2()), StringComparer.OrdinalIgnoreCase);
            int number = 1;
            while (usedNames.Contains(prefix + number)) number++;
            Require(view.SetName2(prefix + number), "Không đặt được tên 図面ビュー cho hình chiếu.");
        }

        private void AddOmoteMark(ModelDoc2 drawing, View view)
        {
            drawing.ClearSelection2(true);
            var bounds = (double[])view.GetOutline();
            double x = (bounds[0] + bounds[2]) / 2, y = bounds[3] + .005;
            var sketch = drawing.SketchManager;
            var blocks = Items(sketch.GetSketchBlockDefinitions()).OfType<SketchBlockDefinition>().ToArray();
            var omoteBlocks = blocks.Where(block => IsSideBlock(block, false)).ToArray();
            var uraBlocks = blocks.Where(block => IsSideBlock(block, true));
            foreach (var instance in uraBlocks.SelectMany(block => Items(block.GetInstances())).OfType<SketchBlockInstance>().ToArray())
            {
                Require(instance.Select(false, null) && drawing.Extension.DeleteSelection2(0),
                    "Không xóa được ký hiệu ウラ có sẵn trong Drawing.");
                drawing.ClearSelection2(true);
            }

            var annotations = Items(((DrawingDoc)drawing).GetViews()).SelectMany(Items).OfType<View>()
                .SelectMany(candidate => Items(candidate.GetNotes())).OfType<Note>().ToArray();
            var existingNote = annotations.FirstOrDefault(note => IsSideLabel(note.GetText(), false));
            foreach (var ura in annotations.Where(note => IsSideLabel(note.GetText(), true)))
            {
                var annotation = ura.GetAnnotation() as Annotation;
                Require(annotation != null && annotation.Select3(false, null) && drawing.Extension.DeleteSelection2(0),
                    "Không xóa được ghi chú ký hiệu ウラ trong Drawing.");
                drawing.ClearSelection2(true);
            }

            var omoteBlock = omoteBlocks.SelectMany(block => Items(block.GetInstances())).OfType<SketchBlockInstance>().FirstOrDefault();
            var point = ((MathUtility)app.GetMathUtility()).CreatePoint(new[] { x, y, 0d }) as MathPoint;
            if (omoteBlock != null)
            {
                omoteBlock.InstancePosition = point;
                return;
            }
            if (omoteBlocks.Length != 0)
            {
                Require(sketch.InsertSketchBlockInstance(omoteBlocks[0], point, 1, 0) != null,
                    "Không đặt được block 表 từ template.");
                return;
            }

            if (existingNote != null)
            {
                var annotation = existingNote.GetAnnotation() as Annotation;
                Require(annotation != null && annotation.SetPosition2(x, y, 0), "Không đặt được ký hiệu 表 của template.");
                return;
            }
            var fallbackNote = drawing.InsertNote("表") as Note;
            Require(fallbackNote != null && fallbackNote.SetBalloon((int)swBalloonStyle_e.swBS_Circular, (int)swBalloonFit_e.swBF_Tightest),
                "Không tạo được ký hiệu mặt 表.");
            var newAnnotation = fallbackNote.GetAnnotation() as Annotation;
            Require(newAnnotation != null && newAnnotation.SetPosition2(x, y, 0),
                "Không đặt được ký hiệu mặt 表 trên bản vẽ.");
        }

        private static bool IsSideBlock(SketchBlockDefinition block, bool ura)
        {
            var notes = Items(block.GetNotes()).OfType<Note>().ToArray();
            return notes.Length == 1 && IsSideLabel(notes[0].GetText(), ura);
        }

        private static bool IsSideLabel(string text, bool ura)
        {
            string value = (text ?? "").Normalize(NormalizationForm.FormKC).Trim();
            return ura ? value == "ウラ" || value == "裏" : value == "表" || value == "オモテ";
        }

        private static void FitFlatViewToTemplate(ModelDoc2 drawing, View view, Action<string> markStep, double[] center)
        {
            markStep?.Invoke("Đo khổ giấy và hình chiếu");
            var sheet = (Sheet)((DrawingDoc)drawing).GetCurrentSheet();
            double width = 0, height = 0;
            sheet.GetSize(ref width, ref height);
            Require(width > 0 && height > 0, "Khổ giấy của template bản vẽ không hợp lệ.");

            if (center == null) view.UseSheetScale = 1;
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
            double halfWidth = (bounds[2] - bounds[0]) / 2, halfHeight = (bounds[3] - bounds[1]) / 2;
            double centerX = center == null ? width * .5 : Math.Max(.01 + halfWidth, Math.Min(center[0], width - .01 - halfWidth));
            double centerY = center == null ? height * .60 : Math.Max(.025 + halfHeight, Math.Min(center[1], height - .025 - halfHeight));
            position[0] += centerX - (bounds[0] + bounds[2]) / 2;
            position[1] += centerY - (bounds[1] + bounds[3]) / 2;
            view.Position = position;
            drawing.EditRebuild3();
            bounds = (double[])view.GetOutline();
            Require(bounds[0] >= 0 && bounds[1] >= 0 && bounds[2] <= width && bounds[3] <= height,
                "Hình trải phẳng vượt khổ giấy của template đã chọn.");
        }
    }
}
