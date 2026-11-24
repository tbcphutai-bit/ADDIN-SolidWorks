using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ADDIN.Commands.CircleFaceTubeLayout;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.Kadai
{
    internal sealed partial class KadaiNativePrototype
    {
        private const string TubeSurfacePrefix = "Kadai_Tube_Reference_";

        private void InsertTubes(ModelDoc2 assemblyDoc, string directory, KadaiPanelSource panel, KadaiTubeLayout tubes,
            KadaiApplyOptions options, List<Component2> added, List<Component2> tubeComponents, List<string> bom)
        {
            var assembly = (AssemblyDoc)assemblyDoc;
            var math = (MathUtility)app.GetMathUtility();
            for (int index = 0; index < tubes.Axes.Length; index++)
            {
                string id = KadaiTubeLayout.Id(index);
                string path = Path.Combine(directory, Path.GetFileName(directory) + "-" + id + ".SLDPRT");
                var axis = tubes.Axes[index];
                var part = app.NewDocument(options.PartTemplate, 0, 0, 0) as ModelDoc2;
                Require(part != null, "Không tạo được Part ống " + id + ".");
                try
                {
                    WriteTube(part, path, axis.Length, id, tubes.Settings, options);
                    Point3 IntoAssembly(Point3 p, bool point) =>
                        Point3.Read(KadaiPreviewBackend.IntoPanelFrame(p.Array(), panel.ComponentPlacement, null, point));
                    var z = IntoAssembly(axis.Direction, false);
                    var x = (Math.Abs(z.Z) < .9 ? new Point3(0, 0, 1) : new Point3(0, 1, 0)).Cross(z).Unit();
                    var y = z.Cross(x);
                    var origin = IntoAssembly(axis.A, true);
                    var data = x.Array().Concat(y.Array()).Concat(z.Array()).Concat(origin.Array())
                        .Concat(new[] { 1d, 0, 0, 0 }).ToArray();
                    ActivateAssembly(assemblyDoc);
                    var component = assembly.AddComponent5(path, 0, "", false, "", 0, 0, 0);
                    Require(component != null, "Không chèn được ống " + id + ".");
                    added.Add(component);
                    assemblyDoc.ClearSelection2(true);
                    if (component.IsFixed())
                    {
                        Require(component.Select4(false, null, false), "Không chọn được ống " + id + ".");
                        assembly.UnfixComponent();
                    }
                    component.Transform2 = (MathTransform)math.CreateTransform(data);
                    var actual = (double[])component.Transform2.ArrayData;
                    Require(Enumerable.Range(0, 13).All(i => Math.Abs(actual[i] - data[i]) < 1e-8),
                        "Vị trí ống lệch khỏi Preview " + id + ".");
                    assemblyDoc.ClearSelection2(true);
                    Require(component.Select4(false, null, false), "Không chọn được ống " + id + " để cố định.");
                    assembly.FixComponent();
                    Require(component.IsFixed(), "Không cố định được ống " + id + ".");
                    assemblyDoc.ClearSelection2(true);
                    tubeComponents.Add(component);
                    bom.Add(BomRow(id, path, false));
                }
                finally { app.CloseDoc(part.GetTitle()); }
            }
        }

        private void WriteTube(ModelDoc2 part, string path, double length, string id, KadaiTubeSettings settings,
            KadaiApplyOptions options)
        {
            Require(!File.Exists(path), "Tên ống bị trùng " + path);
            var profilePlane = FirstTubePlane(part);
            Require(profilePlane != null && profilePlane.Select2(false, 0), "Không chọn được mặt phẳng ống " + id + ".");
            var sketches = part.SketchManager;
            sketches.InsertSketch(true);
            var sketch = sketches.ActiveSketch;
            Require(sketch != null, "Không mở được sketch ống " + id + ".");
            try
            {
                WithDrivingDimensions(part, () =>
                {
                    var circle = sketches.CreateCircleByRadius(0, 0, 0, settings.Radius);
                    Require(circle != null && circle.Select4(false, null), "Không tạo được đường tròn ống " + id + ".");
                    var display = part.AddDiameterDimension2(settings.Diameter, settings.Diameter, 0) as DisplayDimension;
                    Require(display != null && Math.Abs(display.GetDimension2(0).SystemValue - settings.Diameter) < 1e-7,
                        "Không đặt được dim đường kính ống " + id + ".");
                });
            }
            finally
            {
                part.ClearSelection2(true);
                sketches.InsertSketch(true);
            }
            var feature = SketchFeature(part, sketch);
            feature.Name = "Kadai_Tube_Profile";
            Require(feature.Select2(false, 0), "Không chọn được sketch ống " + id + ".");
            var boss = part.FeatureManager.FeatureExtrusion3(true, false, false,
                (int)swEndConditions_e.swEndCondBlind, 0, length, 0, false, false, false, false, 0, 0,
                false, false, false, false, true, true, true, 0, 0, false);
            Require(boss != null, "Không tạo được Boss-Extrude ống " + id + ".");
            boss.Name = "Kadai_Tube_Length";
            var body = SolidTubeBody(part);
            var caps = Items(body.GetFaces()).OfType<Face2>().Where(face => ((Surface)face.GetSurface()).IsPlane()).ToArray();
            Require(caps.Length == 2, "Ống " + id + " không có đúng hai đầu phẳng.");
            part.ClearSelection2(true);
            var select = ((SelectionMgr)part.SelectionManager).CreateSelectData();
            select.Mark = 1;
            foreach (var cap in caps)
                Require(((Entity)cap).Select4(true, select), "Không chọn được đầu ống " + id + " để Shell.");
            part.InsertFeatureShell(settings.Wall, false);
            part.ClearSelection2(true);
            part.EditRebuild3();
            var shell = TubeFeatures(part).LastOrDefault(candidate => candidate.GetTypeName2() == "Shell");
            Require(shell?.GetDefinition() is ShellFeatureData data && Math.Abs(data.Thickness - settings.Wall) < 1e-7
                && shell.GetErrorCode2(out bool shellWarning) == 0 && !shellWarning, "Không tạo được Shell ống " + id + ".");
            shell.Name = "Kadai_Tube_Wall";
            body = SolidTubeBody(part);
            var radii = Items(body.GetFaces()).OfType<Face2>().Select(CylinderRadius).Where(radius => radius > 0).OrderBy(radius => radius).ToArray();
            var box = (double[])body.GetBodyBox();
            Require(radii.Length == 2 && Math.Abs(radii[0] - (settings.Radius - settings.Wall)) < 1e-7
                && Math.Abs(radii[1] - settings.Radius) < 1e-7 && Math.Abs(box[2]) < 1e-6 && Math.Abs(box[5] - length) < 1e-6,
                "Kích thước hoặc hướng ống khác Preview " + id + ".");

            var partDoc = (PartDoc)part;
            string configuration = part.ConfigurationManager.ActiveConfiguration.Name;
            partDoc.SetMaterialPropertyName2(configuration, options.TubeMaterialDatabase, options.TubeMaterialName);
            string material = partDoc.GetMaterialPropertyName2(configuration, out string database);
            Require(string.Equals(material, options.TubeMaterialName, StringComparison.Ordinal)
                && string.Equals(Path.GetFileNameWithoutExtension(database), Path.GetFileNameWithoutExtension(options.TubeMaterialDatabase),
                    StringComparison.OrdinalIgnoreCase), "SolidWorks đọc lại vật liệu ống " + id + " là " + database + " / " + material + ".");
            part.Extension.CustomPropertyManager[""].Add3("Kadai_TubeId", 30, id, 2);
            SaveBlankPart(part, path);
        }

        private static IEnumerable<Feature> TubeFeatures(ModelDoc2 model)
        {
            for (var feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                yield return feature;
        }

        private static Feature FirstTubePlane(ModelDoc2 model) => TubeFeatures(model).FirstOrDefault(feature => feature.GetTypeName2() == "RefPlane");

        private static Body2 SolidTubeBody(ModelDoc2 model) =>
            Items(((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false)).OfType<Body2>().Single();

        private static double CylinderRadius(Face2 face)
        {
            var surface = (Surface)face.GetSurface();
            return surface.IsCylinder() ? ((double[])surface.CylinderParams)[6] : 0;
        }

        private static Face2 FindTubeOuterFace(Component2 component, double radius)
        {
            var body = SolidTubeBody((ModelDoc2)component.GetModelDoc2());
            return Items(body.GetFaces()).OfType<Face2>().Single(face => Math.Abs(CylinderRadius(face) - radius) < 1e-7);
        }

        private List<Feature> CutTubeSeats(ModelDoc2 part, Feature plane, Dictionary<string, Feature> surfaces)
        {
            var cuts = new List<Feature>();
            foreach (var group in GroupTubeCutProfiles(surfaces))
            {
                string id = (cuts.Count + 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
                part.ClearSelection2(true);
                Require(plane.Select2(false, 0), "Không chọn được mặt sketch cắt " + id + ".");
                var manager = part.SketchManager;
                manager.InsertSketch(true);
                var sketch = manager.ActiveSketch;
                Require(sketch != null, "Không mở được sketch cắt " + id + ".");
                try
                {
                    Require(Intersections(part, sketch, group).Length == group.Count,
                        "Số giao tuyến ống trong sketch cắt " + id + " không khớp.");
                }
                finally { manager.InsertSketch(true); }
                var feature = SketchFeature(part, sketch);
                feature.Name = "Kadai_Tube_Intersection_" + id;
                Require(feature.Select2(false, 0), "Không chọn được giao tuyến ống " + id + ".");
                cuts.Add(CutThroughSheet(part, "Kadai_Tube_Seat_" + id));
            }
            return cuts;
        }

        private static List<List<Feature>> GroupTubeCutProfiles(Dictionary<string, Feature> surfaces)
        {
            var intervals = surfaces.Where(entry => entry.Key.StartsWith(TubeSurfacePrefix, StringComparison.Ordinal))
                .Select(entry =>
                {
                    var face = Items(entry.Value.GetFaces()).OfType<Face2>().Single();
                    var cylinder = (double[])((Surface)face.GetSurface()).CylinderParams;
                    double slope = cylinder[3] / cylinder[5];
                    double center = cylinder[0] - cylinder[2] * slope;
                    double halfWidth = cylinder[6] * Math.Sqrt(1 + slope * slope);
                    return (Surface: entry.Value, Left: center - halfWidth, Right: center + halfWidth);
                }).OrderBy(item => item.Left);
            var groups = new List<List<Feature>>();
            var lastRight = new List<double>();
            foreach (var item in intervals)
            {
                // Closed contours that overlap cannot share a normal-cut sketch.
                int index = lastRight.FindIndex(right => right + .00001 < item.Left);
                if (index < 0)
                {
                    groups.Add(new List<Feature> { item.Surface });
                    lastRight.Add(item.Right);
                }
                else
                {
                    groups[index].Add(item.Surface);
                    lastRight[index] = item.Right;
                }
            }
            return groups;
        }

        private void VerifyInsertedTubes(ModelDoc2 assembly, KadaiPanelSource panel, KadaiTubeLayout tubes,
            List<Component2> components, List<Component2> ribs)
        {
            Require(components.Count == tubes.Axes.Length && components.All(component => component.IsFixed()), "Assembly thiếu ống đã cố định.");
            ActivateAssembly(assembly);
            assembly.EditRebuild3();
            var modeler = (Modeler)app.GetModeler();
            var tubeBodies = new List<Body2>();
            var ribBodies = new List<Body2>();
            var panelBodies = new List<Body2>();
            try
            {
                foreach (var component in components)
                    tubeBodies.Add(CopyPlacedBody(SolidTubeBody((ModelDoc2)component.GetModelDoc2()), component));
                foreach (var component in ribs)
                    ribBodies.Add(CopyPlacedBody(SolidTubeBody((ModelDoc2)component.GetModelDoc2()), component));
                foreach (var body in Items(((PartDoc)panel.PanelPart).GetBodies2((int)swBodyType_e.swSolidBody, false)).OfType<Body2>())
                    panelBodies.Add(CopyPlacedBody(body, panel.PanelComponent));
                CheckTubeInterference(modeler, tubeBodies.ToArray(), ribBodies.Concat(panelBodies).ToArray(), "Ống giao với gân hoặc panel sau rebuild.");
                for (int i = 1; i < tubeBodies.Count; i++)
                    CheckTubeInterference(modeler, new[] { tubeBodies[i] }, tubeBodies.Take(i).ToArray(), "Các ống giao thể tích sau khi lưu Part.");
            }
            finally
            {
                foreach (var body in tubeBodies.Concat(ribBodies).Concat(panelBodies)) Marshal.ReleaseComObject(body);
            }
        }

        private static Body2 CopyPlacedBody(Body2 source, Component2 component)
        {
            var result = source.Copy() as Body2;
            Require(result != null, "Không sao chép được body để kiểm tra lắp.");
            if (!result.ApplyTransform(component.Transform2))
            {
                Marshal.ReleaseComObject(result);
                throw new InvalidOperationException("Không chuyển được body vào tọa độ Assembly.");
            }
            return result;
        }

        private static void CheckTubeInterference(Modeler modeler, Body2[] targets, Body2[] tools, string message)
        {
            object first = null, second = null, overlap = null;
            Require(!modeler.CheckInterference3(targets, tools, 0, ref first, ref second, ref overlap), message);
        }
    }
}
