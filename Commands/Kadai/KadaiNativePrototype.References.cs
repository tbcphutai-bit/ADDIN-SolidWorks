using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.Kadai
{
    internal sealed partial class KadaiNativePrototype
    {
        private const string PanelSurfaceKey = "Panel";
        private const int IntersectionCurveCommand = 306; // swCommands_IntersectionCurve
        private const double DepthClearanceMeters = .0001;
        private const double SketchTolerance = .0000001;

        private static void SaveBlankPart(ModelDoc2 part, string path)
        {
            int error = 0, warning = 0;
            Require(part.Extension.SaveAs(path, 0, 1, null, ref error, ref warning) && error == 0 && warning == 0,
                "Không lưu được Part để tạo tham chiếu trong Assembly: " + error + "/" + warning);
        }

        private Dictionary<string, Feature> AddLinkedFaces(ModelDoc2 doc, ModelDoc2 part, Component2 component,
            KadaiProfile3D profile, List<KadaiJointCut> cuts, KadaiPanelSource panel, bool opensAtBottom,
            Dictionary<string, Component2> components, Dictionary<string, List<KadaiJointCut>> allCuts)
        {
            var surfaces = new Dictionary<string, Feature>();
            if (profile.HasPanelContact || (!opensAtBottom && cuts.Count > 0))
            {
                var assembly = (AssemblyDoc)doc;
                Require(component.Select4(false, null, false), "Không chọn được thanh để Edit Part.");
                int info = 0;
                try
                {
                    Require(assembly.EditPart2(true, false, ref info) == 0
                        && app.IsSame(assembly.GetEditTarget(), part) == 1, "Không Edit Part được trong Assembly: " + info);
                    if (profile.HasPanelContact)
                    {
                        var face = panel.PanelPart.Extension.GetObjectByPersistReference3(
                            Convert.FromBase64String(panel.PersistentReferenceEncoded), out int error) as Face2;
                        Require(face != null && error == 0, "Mất tham chiếu đến mặt panel đã chọn.");
                        surfaces.Add(PanelSurfaceKey, InsertLinkedSurface(doc, part, panel.PanelComponent, face, 0,
                            "Kadai_PanelFace_" + profile.RibId));
                    }
                    if (!opensAtBottom)
                    {
                        foreach (var slot in cuts)
                        {
                            var mate = components[slot.MatingRibId];
                            var mateSlot = allCuts[slot.MatingRibId].Single(item => item.MatingRibId == profile.RibId);
                            var mateDoc = (PartDoc)mate.GetModelDoc2();
                            var seat = FindSlotSeat(mateDoc, mateSlot);
                            surfaces.Add(slot.SlotId, InsertLinkedSurface(doc, part, mate, seat, DepthClearanceMeters,
                                "Kadai_Seat_" + slot.MatingRibId));
                        }
                    }
                    Require(component.ListExternalFileReferencesCount() >= surfaces.Count,
                        "SolidWorks chưa giữ đầy đủ tham chiếu cho " + profile.RibId + ".");
                }
                finally
                {
                    doc.ClearSelection2(true);
                    assembly.EditAssembly();
                }
            }
            int activationError = 0;
            var active = app.ActivateDoc3(part.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activationError);
            Require(active != null && app.IsSame(active, part) == 1, "Không mở được Part để tạo sketch: " + activationError);
            return surfaces;
        }

        private static Feature InsertLinkedSurface(ModelDoc2 assembly, ModelDoc2 part, Component2 source, Face2 face,
            double offset, string name)
        {
            var old = new HashSet<string>(OffsetSurfaces(part).Select(feature => feature.Name));
            assembly.ClearSelection2(true);
            var contextFace = source.GetCorrespondingEntity(face) as Entity;
            Require(contextFace != null && contextFace.Select4(false, null), "Không chọn được mặt tham chiếu " + name + ".");
            bool reverse = offset > 0 && ((double[])face.Normal)[1] > 0;
            assembly.InsertOffsetSurface(offset, reverse);
            var created = OffsetSurfaces(part).SingleOrDefault(feature => !old.Contains(feature.Name));
            Require(created != null && Items(created.GetFaces()).Length == 1,
                "Không tạo được mặt tham chiếu " + name + ".");
            created.Name = name;
            return created;
        }

        private static IEnumerable<Feature> OffsetSurfaces(ModelDoc2 part)
        {
            for (var feature = part.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                if (feature.GetDefinition() is SurfaceOffsetFeatureData) yield return feature;
        }

        private static Face2 FindSlotSeat(PartDoc part, KadaiJointCut slot)
        {
            Require(part != null, "Không nạp được thanh đối ứng " + slot.SlotId + ".");
            var body = Items(part.GetBodies2((int)swBodyType_e.swSolidBody, false)).OfType<Body2>().Single();
            var seat = Items(body.GetFaces()).OfType<Face2>().SingleOrDefault(face =>
            {
                if (!(face.Normal is double[] normal) || normal[1] > -.999999) return false;
                var box = (double[])face.GetBox();
                const double tolerance = .000025;
                return Math.Abs(box[1] - slot.Top) <= tolerance && Math.Abs(box[4] - slot.Top) <= tolerance
                    && Math.Abs(box[0] - slot.Left) <= tolerance && Math.Abs(box[3] - slot.Right) <= tolerance;
            });
            Require(seat != null, "Không tìm được mặt đáy rãnh đối ứng " + slot.SlotId + ".");
            return seat;
        }

        private SketchSegment[] Intersection(ModelDoc2 doc, Sketch sketch, Feature surface)
        {
            var old = new HashSet<string>(Items(sketch.GetSketchSegments()).OfType<SketchSegment>()
                .Select(segment => string.Join(",", (int[])segment.GetID())));
            var face = Items(surface.GetFaces()).OfType<Face2>().Single();
            doc.ClearSelection2(true);
            Require(((Entity)face).Select4(false, null) && app.RunCommand(IntersectionCurveCommand, ""),
                "Không chạy được 交線カーブ trên " + surface.Name + ".");
            var result = Items(sketch.GetSketchSegments()).OfType<SketchSegment>()
                .Where(segment => !old.Contains(string.Join(",", (int[])segment.GetID()))).ToArray();
            Require(result.Length > 0 && result.All(segment => segment.GetRelationsCount() > 0),
                "交線カーブ không tạo được giao tuyến có tham chiếu từ " + surface.Name + ".");
            doc.ClearSelection2(true);
            return result;
        }

        private double[] DrawLinkedPanelProfile(ModelDoc2 doc, Sketch sketch, Feature surface, double[][] local)
        {
            var curves = Intersection(doc, sketch, surface);
            var points = curves.Select(Endpoints).OrderBy(edge => edge[0].X).ToArray();
            for (int i = 1; i < points.Length; i++)
                Require(Math.Abs(points[i][0].X - points[i - 1][1].X) <= SketchTolerance
                    && Math.Abs(points[i][0].Y - points[i - 1][1].Y) <= SketchTolerance,
                    "Mặt panel cho nhiều đường giao rời. Chọn một mặt liên tục.");
            var first = points[0][0];
            var last = points[points.Length - 1][1];
            Require(last.X - first.X > SketchTolerance, "Đường giao panel bị đứng hoặc quá ngắn.");
            double width = local[local.Length - 2][0];
            // Extend the original intersection, then trim the blank at the two base edges.
            // This preserves the native relation even if the panel extends past the base.
            double left = Math.Min(first.X, 0) - .005, right = Math.Max(last.X, width) + .005;
            var lines = DrawChain(doc, new[] { first.X, first.Y }, new[] { left, first.Y }, new[] { left, 0d },
                new[] { right, 0d }, new[] { right, last.Y }, new[] { last.X, last.Y });
            Link(doc, first, Start(lines[0]));
            Link(doc, last, End(lines[4]));
            Constrain(doc, lines[0], "sgHORIZONTAL");
            Constrain(doc, lines[1], "sgVERTICAL");
            Constrain(doc, lines[2], "sgFIXED");
            Constrain(doc, lines[3], "sgVERTICAL");
            Constrain(doc, lines[4], "sgHORIZONTAL");
            return new[] { left, right, width };
        }

        private void TrimLinkedProfile(ModelDoc2 doc, Feature plane, string id, double[][] local, double[] bounds)
        {
            doc.ClearSelection2(true);
            Require(plane.Select2(false, 0), "Không chọn được mặt phẳng cắt theo base.");
            var manager = doc.SketchManager;
            manager.InsertSketch(true);
            var sketch = manager.ActiveSketch;
            try
            {
                double upper = 2 * local.Max(point => point[1]) + .01;
                DrawPolygon(doc, Rectangle(bounds[0] - .01, -.01, 0, upper));
                DrawPolygon(doc, Rectangle(bounds[2], -.01, bounds[1] + .01, upper));
            }
            finally { manager.InsertSketch(true); }
            Require(SketchFeature(doc, sketch).Select2(false, 0), "Không chọn được sketch mép base.");
            CutThroughSheet(doc, "Kadai_TrimToBase_" + id);
        }

        private static Feature CutThroughSheet(ModelDoc2 doc, string name)
        {
            var cut = doc.FeatureManager.FeatureCut4(false, false, false, 1, 1, 0, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, false, false, false, 0, 0, false, true);
            Require(cut != null && cut.GetErrorCode2(out bool warning) == 0 && !warning,
                "Không tạo được Normal Cut " + name + ".");
            cut.Name = name;
            return cut;
        }

        private void DrawLinkedSlots(ModelDoc2 doc, Sketch sketch, List<KadaiJointCut> cuts, bool opensAtBottom,
            Dictionary<string, Feature> surfaces)
        {
            if (!opensAtBottom)
            {
                foreach (var slot in cuts) DrawMatingSlot(doc, sketch, slot, surfaces[slot.SlotId]);
                return;
            }
            var manager = doc.SketchManager;
            manager.AddToDB = true;
            SketchPoint datum;
            try { datum = manager.CreatePoint(0, 0, 0); }
            finally { manager.AddToDB = false; }
            doc.ClearSelection2(true);
            Require(datum != null && datum.Select4(false, null), "Không tạo được mốc dim rãnh từ mặt đế.");
            doc.SketchAddConstraints("sgFIXED");
            int preference = (int)swUserPreferenceToggle_e.swInputDimValOnCreate;
            bool original = app.GetUserPreferenceToggle(preference);
            try
            {
                app.SetUserPreferenceToggle(preference, false);
                foreach (var slot in cuts)
                {
                    var corners = Rectangle(slot.Left, slot.Bottom, slot.Right, slot.Top);
                    var lines = DrawChain(doc, corners.Concat(corners.Take(1)).ToArray());
                    Link(doc, End(lines[3]), Start(lines[0]));
                    Constrain(doc, lines[0], "sgFIXED");
                    Constrain(doc, lines[1], "sgVERTICAL");
                    Constrain(doc, lines[2], "sgHORIZONTAL");
                    Constrain(doc, lines[3], "sgVERTICAL");
                    doc.ClearSelection2(true);
                    Require(Start(lines[2]).Select4(false, null) && datum.Select4(true, null),
                        "Không chọn được hai mốc DIM cho " + slot.SlotId + ".");
                    var dimension = (doc.AddVerticalDimension2(slot.Right + .015, slot.Top / 2, 0) as DisplayDimension)?.GetDimension2(0);
                    Require(dimension != null && Math.Abs(dimension.SystemValue - slot.Top) < SketchTolerance,
                        "SolidWorks không giữ DIM điều khiển rãnh " + slot.SlotId + ".");
                }
            }
            finally
            {
                app.SetUserPreferenceToggle(preference, original);
                doc.ClearSelection2(true);
            }
        }

        private void DrawMatingSlot(ModelDoc2 doc, Sketch sketch, KadaiJointCut slot, Feature surface)
        {
            var segment = Intersection(doc, sketch, surface).Single();
            var points = Endpoints(segment);
            var left = points[0];
            var right = points[1];
            Require(Math.Abs(left.Y - slot.Bottom) < .000025 && Math.Abs(right.Y - slot.Bottom) < .000025
                && left.X - slot.Left > SketchTolerance && slot.Right - right.X > SketchTolerance,
                "Giao tuyến rãnh " + slot.SlotId + " lệch cao độ hoặc bề rộng so với thanh đối ứng.");
            var lines = DrawChain(doc, new[] { left.X, left.Y }, new[] { slot.Left, left.Y },
                new[] { slot.Left, slot.Top }, new[] { slot.Right, slot.Top }, new[] { slot.Right, right.Y },
                new[] { right.X, right.Y });
            Link(doc, left, Start(lines[0]));
            Link(doc, right, End(lines[4]));
            Constrain(doc, lines[0], "sgHORIZONTAL");
            Constrain(doc, lines[1], "sgVERTICAL");
            Constrain(doc, lines[2], "sgFIXED");
            Constrain(doc, lines[3], "sgVERTICAL");
            Constrain(doc, lines[4], "sgHORIZONTAL");
        }

        private static SketchPoint[] Endpoints(SketchSegment segment)
        {
            SketchPoint[] points;
            if (segment is SketchLine line) points = new[] { Start(line), End(line) };
            else if (segment is SketchArc arc) points = new[] { (SketchPoint)arc.GetStartPoint2(), (SketchPoint)arc.GetEndPoint2() };
            else if (segment is SketchSpline spline)
            {
                var fit = Items(spline.GetPoints2()).OfType<SketchPoint>().ToArray();
                Require(fit.Length >= 2, "Spline giao tuyến thiếu điểm đầu hoặc cuối.");
                points = new[] { fit[0], fit[fit.Length - 1] };
            }
            else throw new InvalidOperationException("Kiểu giao tuyến chưa được hỗ trợ: " + segment.GetType());
            return points.OrderBy(point => point.X).ToArray();
        }

        private static void DrawPolygon(ModelDoc2 doc, double[][] points)
        {
            var manager = doc.SketchManager;
            manager.AddToDB = true;
            try
            {
                for (int i = 0; i < points.Length; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % points.Length];
                    Require(manager.CreateLine(a[0], a[1], 0, b[0], b[1], 0) != null, "Không tạo được cạnh sketch Kadai.");
                }
            }
            finally { manager.AddToDB = false; }
        }

        private static double[][] Rectangle(double left, double bottom, double right, double top) =>
            new[] { new[] { left, bottom }, new[] { right, bottom }, new[] { right, top }, new[] { left, top } };

        private static SketchLine[] DrawChain(ModelDoc2 doc, params double[][] points)
        {
            var manager = doc.SketchManager;
            var lines = new SketchLine[points.Length - 1];
            manager.AddToDB = true;
            try
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    lines[i] = manager.CreateLine(points[i][0], points[i][1], 0, points[i + 1][0], points[i + 1][1], 0) as SketchLine;
                    Require(lines[i] != null, "Không tạo được cạnh khép giao tuyến.");
                }
            }
            finally { manager.AddToDB = false; }
            for (int i = 1; i < lines.Length; i++) Link(doc, End(lines[i - 1]), Start(lines[i]));
            return lines;
        }

        private static SketchPoint Start(SketchLine line) => (SketchPoint)line.GetStartPoint2();
        private static SketchPoint End(SketchLine line) => (SketchPoint)line.GetEndPoint2();

        private static void Link(ModelDoc2 doc, SketchPoint a, SketchPoint b)
        {
            doc.ClearSelection2(true);
            Require(a.Select4(false, null) && b.Select4(true, null), "Không chọn được hai đầu mút sketch.");
            doc.SketchAddConstraints("sgMERGEPOINTS");
        }

        private static void Constrain(ModelDoc2 doc, SketchLine line, string constraint)
        {
            doc.ClearSelection2(true);
            Require(((SketchSegment)line).Select4(false, null), "Không chọn được cạnh để ràng buộc sketch.");
            doc.SketchAddConstraints(constraint);
        }
    }
}
