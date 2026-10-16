using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands
{
    internal sealed partial class AutoVCutSectionCommand
    {
        // Coordinates describe the unbroken profile at the old view scale.
        // Keep names rather than RCWs: rebuilding or unbreaking can replace them.
        private sealed class PartSectionMarkLayout
        {
            internal string Name;
            internal bool ProfileAnchor;
            internal double AnchorX, AnchorY, OutsideX, OutsideY;
            internal double PositionX, PositionY;
            internal double PaperDiameter, OldInstanceScale;
            internal string CaptureNotice;
        }

        private List<PartSectionMarkLayout> CapturePartSectionMarks(
            SolidWorks.Interop.sldworks.View view)
        {
            var layouts = new List<PartSectionMarkLayout>();
            try
            {
                double[] origin = view == null ? null : view.Position as double[];
                double viewScale = view == null ? 0 : view.ScaleDecimal;
                if (origin == null || origin.Length < 2 || !IsPartMarkNumber(viewScale))
                    return layouts;
                Sketch sketch = view.GetSketch() as Sketch;
                var profile = GetDisplayedSegments(view);
                foreach (SketchBlockInstance block in GetBlockInstances(sketch))
                {
                    try
                    {
                        if (!IsPartSectionGrooveMark(block)) continue;
                        double x, y;
                        if (!TryReadPartMarkSheetPosition(block, sketch, out x, out y))
                        {
                            Log("SECTION MARK CAPTURE unavailable position instance=" + block.Name);
                            continue;
                        }
                        var layout = new PartSectionMarkLayout {
                            Name = block.Name ?? "",
                            PositionX = (x - origin[0]) / viewScale,
                            PositionY = (y - origin[1]) / viewScale,
                            OldInstanceScale = block.Scale
                        };
                        double anchorX, anchorY, outsideX, outsideY;
                        layout.ProfileAnchor = TryFindLocalBendCorner(profile, x, y,
                            out anchorX, out anchorY, out outsideX, out outsideY);
                        if (layout.ProfileAnchor)
                        {
                            layout.AnchorX = (anchorX - origin[0]) / viewScale;
                            layout.AnchorY = (anchorY - origin[1]) / viewScale;
                            layout.OutsideX = outsideX;
                            layout.OutsideY = outsideY;
                        }
                        else layout.CaptureNotice = "không xác nhận được góc bẻ để giữ khoảng cách dấu";
                        double diameter;
                        if (TryReadPartMarkPaperDiameter(block, sketch, out diameter))
                            layout.PaperDiameter = diameter;
                        else layout.CaptureNotice = AppendPartMarkNotice(layout.CaptureNotice,
                            "không đo được đường kính dấu trên giấy");
                        layouts.Add(layout);
                        Log("SECTION MARK CAPTURE instance=" + layout.Name +
                            " corner=" + layout.ProfileAnchor +
                            " paperDiameter=" + Mm(layout.PaperDiameter) +
                            "mm instanceScale=" + F(layout.OldInstanceScale));
                    }
                    catch (Exception ex)
                    { Log("SECTION MARK CAPTURE instance error " + ex.Message); }
                }
            }
            catch (Exception ex) { Log("SECTION MARK CAPTURE unavailable " + ex.Message); }
            return layouts;
        }

        // Call after rescaling an unbroken view, before replanning its breaks.
        // This adjusts existing instances only; it must never add another mark.
        private string RestorePartSectionMarks(SolidWorks.Interop.sldworks.View view,
            List<PartSectionMarkLayout> layouts)
        {
            var notices = new List<string>();
            if (layouts == null || layouts.Count == 0) return "";
            string viewName = "Mặt cắt";
            try
            {
                if (view != null) viewName = view.GetName2();
                double[] origin = view == null ? null : view.Position as double[];
                double viewScale = view == null ? 0 : view.ScaleDecimal;
                if (origin == null || origin.Length < 2 || !IsPartMarkNumber(viewScale))
                    return viewName + ": chưa khôi phục được dấu vì không đọc được vị trí hoặc tỉ lệ mặt cắt.";
                Sketch sketch = view.GetSketch() as Sketch;
                var instances = GetBlockInstances(sketch);
                var profile = GetDisplayedSegments(view);
                MathUtility math = app == null ? null : app.IGetMathUtility();
                foreach (PartSectionMarkLayout layout in layouts)
                {
                    try
                    {
                        var matches = instances.Where(candidate => string.Equals(candidate.Name,
                            layout.Name, StringComparison.Ordinal)).ToList();
                        if (matches.Count != 1)
                        {
                            notices.Add(layout.Name + ": không xác định được dấu cũ; chưa thay đổi dấu.");
                            continue;
                        }
                        SketchBlockInstance block = matches[0];
                        string notice = layout.CaptureNotice ?? "";
                        double x = origin[0] + layout.PositionX * viewScale;
                        double y = origin[1] + layout.PositionY * viewScale;
                        if (layout.ProfileAnchor)
                        {
                            double anchorX = origin[0] + layout.AnchorX * viewScale;
                            double anchorY = origin[1] + layout.AnchorY * viewScale;
                            double foundX, foundY, foundOutsideX, foundOutsideY;
                            // The normalized anchor is already valid for a scale
                            // change. Prefer the actual corner when still close,
                            // without hopping to another neighboring bend.
                            if (TryFindLocalBendCorner(profile, anchorX, anchorY,
                                out foundX, out foundY, out foundOutsideX, out foundOutsideY) &&
                                Math.Sqrt(Math.Pow(foundX - anchorX, 2) +
                                    Math.Pow(foundY - anchorY, 2)) <= 0.002)
                            {
                                anchorX = foundX;
                                anchorY = foundY;
                            }
                            const double clearance = 0.008;
                            x = anchorX + clearance * layout.OutsideX;
                            y = anchorY + clearance * layout.OutsideY;
                        }
                        MathPoint position = ViewPointToSketchPoint(math, view, x, y, 0);
                        if (position != null) block.InstancePosition = position;
                        else notice = AppendPartMarkNotice(notice, "không đổi được vị trí dấu");

                        // Instance scale alone does not tell us whether the view
                        // scale was inherited by the drawing block. Measure its
                        // actual paper diameter before deciding on a correction.
                        if (IsPartMarkNumber(layout.PaperDiameter))
                        {
                            double diameter;
                            if (TryReadPartMarkPaperDiameter(block, sketch, out diameter))
                            {
                                double tolerance = Math.Max(0.000001, layout.PaperDiameter * 0.002);
                                if (Math.Abs(diameter - layout.PaperDiameter) > tolerance)
                                {
                                    double instanceScale = block.Scale;
                                    double desiredScale = instanceScale * layout.PaperDiameter / diameter;
                                    if (IsPartMarkNumber(instanceScale) && IsPartMarkNumber(desiredScale))
                                    {
                                        block.Scale = desiredScale;
                                        double verified;
                                        if (!TryReadPartMarkPaperDiameter(block, sketch, out verified) ||
                                            Math.Abs(verified - layout.PaperDiameter) > tolerance)
                                            notice = AppendPartMarkNotice(notice,
                                                "cần kiểm tra lại đường kính dấu sau khi đổi tỉ lệ");
                                    }
                                    else notice = AppendPartMarkNotice(notice, "tỉ lệ block không hợp lệ");
                                }
                            }
                            else notice = AppendPartMarkNotice(notice,
                                "không đo lại được đường kính; giữ tỉ lệ block hiện tại");
                        }
                        Log("SECTION MARK RESTORE instance=" + layout.Name +
                            " paperPosition=(" + Mm(x) + "," + Mm(y) +
                            ")mm profileAnchor=" + layout.ProfileAnchor +
                            " oldInstanceScale=" + F(layout.OldInstanceScale) +
                            " instanceScale=" + F(block.Scale));
                        if (notice.Length > 0) notices.Add(layout.Name + ": " + notice + ".");
                    }
                    catch (Exception ex)
                    { notices.Add(layout.Name + ": chưa khôi phục đủ dấu — " + ex.Message); }
                }
                if (GetBlockInstances(view.GetSketch() as Sketch).Count != instances.Count)
                    notices.Add("Số block của mặt cắt đã thay đổi; cần kiểm tra dấu.");
            }
            catch (Exception ex) { notices.Add("Chưa khôi phục đủ dấu: " + ex.Message); }
            return notices.Count == 0 ? "" : viewName + ": " + string.Join("\n", notices.Distinct());
        }

        private static bool IsPartSectionGrooveMark(SketchBlockInstance block)
        {
            string name = block == null ? "" : block.Name ?? "";
            if (name.IndexOf("プレーナー", StringComparison.Ordinal) >= 0) return true;
            SketchBlockDefinition definition = block == null ? null : block.Definition;
            string file = definition == null ? "" : Path.GetFileName(definition.FileName ?? "");
            return file.IndexOf("プレーナー", StringComparison.Ordinal) >= 0;
        }

        private bool TryReadPartMarkSheetPosition(SketchBlockInstance block, Sketch sketch,
            out double x, out double y)
        {
            x = y = 0;
            MathTransform sketchToSheet = sketch == null || sketch.ModelToSketchTransform == null
                ? null : sketch.ModelToSketchTransform.Inverse() as MathTransform;
            MathPoint point = block == null ? null : block.InstancePosition;
            point = point == null || sketchToSheet == null ? null :
                point.MultiplyTransform(sketchToSheet) as MathPoint;
            double[] coordinates = point == null ? null : point.ArrayData as double[];
            if (coordinates == null || coordinates.Length < 2 ||
                !IsFinitePartMarkCoordinate(coordinates[0]) ||
                !IsFinitePartMarkCoordinate(coordinates[1])) return false;
            x = coordinates[0]; y = coordinates[1];
            return true;
        }

        private bool TryReadPartMarkPaperDiameter(SketchBlockInstance block, Sketch hostSketch,
            out double diameter)
        {
            diameter = 0;
            if (block == null || hostSketch == null || app == null) return false;
            try
            {
                SketchBlockDefinition definition = block.Definition;
                Sketch definitionSketch = definition == null ? null : definition.GetSketch() as Sketch;
                Array segments = definitionSketch == null ? null : definitionSketch.GetSketchSegments() as Array;
                MathTransform blockToSketch = block.BlockToSketchTransform;
                MathTransform sketchToSheet = hostSketch.ModelToSketchTransform == null ? null :
                    hostSketch.ModelToSketchTransform.Inverse() as MathTransform;
                MathUtility math = app.IGetMathUtility();
                if (blockToSketch == null || sketchToSheet == null || math == null)
                    return false;
                // Imported SLD blocks can return no GetSketchSegments. The
                // documented definition GetArcs array still exposes the circles:
                // 16 doubles/arc, start at 6 and center at 12.
                // https://help.solidworks.com/2021/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchBlockDefinition~GetArcs.html
                Array arcs = definition.GetArcs() as Array;
                if (arcs != null && arcs.Length % 16 == 0)
                    for (int index = 0; index < arcs.Length; index += 16)
                    {
                        double cx = Convert.ToDouble(arcs.GetValue(index + 12));
                        double cy = Convert.ToDouble(arcs.GetValue(index + 13));
                        double cz = Convert.ToDouble(arcs.GetValue(index + 14));
                        double radius = Math.Sqrt(Math.Pow(Convert.ToDouble(arcs.GetValue(index + 6)) - cx, 2) +
                            Math.Pow(Convert.ToDouble(arcs.GetValue(index + 7)) - cy, 2) +
                            Math.Pow(Convert.ToDouble(arcs.GetValue(index + 8)) - cz, 2));
                        if (!IsPartMarkNumber(radius)) continue;
                        diameter = Math.Max(diameter, Math.Max(
                            PartMarkPaperDistance(TransformPartMarkCirclePoint(math, blockToSketch, sketchToSheet, cx + radius, cy, cz),
                                TransformPartMarkCirclePoint(math, blockToSketch, sketchToSheet, cx - radius, cy, cz)),
                            PartMarkPaperDistance(TransformPartMarkCirclePoint(math, blockToSketch, sketchToSheet, cx, cy + radius, cz),
                                TransformPartMarkCirclePoint(math, blockToSketch, sketchToSheet, cx, cy - radius, cz))));
                    }
                foreach (object item in segments ?? new object[0])
                {
                    SketchArc arc = item as SketchArc;
                    if (arc == null) continue;
                    SketchPoint center = arc.GetCenterPoint2() as SketchPoint;
                    double radius = arc.GetRadius();
                    if (center == null || !IsPartMarkNumber(radius)) continue;
                    double[] first = TransformPartMarkCirclePoint(math, blockToSketch, sketchToSheet,
                        center.X + radius, center.Y, center.Z);
                    double[] second = TransformPartMarkCirclePoint(math, blockToSketch, sketchToSheet,
                        center.X - radius, center.Y, center.Z);
                    double[] third = TransformPartMarkCirclePoint(math, blockToSketch, sketchToSheet,
                        center.X, center.Y + radius, center.Z);
                    double[] fourth = TransformPartMarkCirclePoint(math, blockToSketch, sketchToSheet,
                        center.X, center.Y - radius, center.Z);
                    diameter = Math.Max(diameter, Math.Max(PartMarkPaperDistance(first, second),
                        PartMarkPaperDistance(third, fourth)));
                }
                // The bundled ○ / ◎ / ● templates use note glyphs rather than
                // sketch circles. Preserve their nominal paper text height;
                // keeping this height also keeps the same glyph's diameter.
                Array notes = definition.GetNotes() as Array;
                foreach (Note note in notes == null ? Enumerable.Empty<Note>() : notes.OfType<Note>())
                {
                    var format = note.GetTextFormat() as TextFormat;
                    double height = format == null ? note.GetHeight() : format.CharHeight;
                    if (!IsPartMarkNumber(height)) continue;
                    double[] origin = TransformPartMarkCirclePoint(math, blockToSketch, sketchToSheet, 0, 0, 0);
                    diameter = Math.Max(diameter, Math.Max(
                        PartMarkPaperDistance(origin, TransformPartMarkCirclePoint(math, blockToSketch, sketchToSheet, height, 0, 0)),
                        PartMarkPaperDistance(origin, TransformPartMarkCirclePoint(math, blockToSketch, sketchToSheet, 0, height, 0))));
                }
                return IsPartMarkNumber(diameter);
            }
            catch { return false; }
        }

        private static double[] TransformPartMarkCirclePoint(MathUtility math,
            MathTransform blockToSketch, MathTransform sketchToSheet, double x, double y, double z)
        {
            MathPoint point = math.CreatePoint(new[] { x, y, z }) as MathPoint;
            point = point == null ? null : point.MultiplyTransform(blockToSketch) as MathPoint;
            point = point == null ? null : point.MultiplyTransform(sketchToSheet) as MathPoint;
            return point == null ? null : point.ArrayData as double[];
        }

        private static double PartMarkPaperDistance(double[] first, double[] second)
        {
            if (first == null || second == null || first.Length < 2 || second.Length < 2) return 0;
            double dx = first[0] - second[0], dy = first[1] - second[1];
            double distance = Math.Sqrt(dx * dx + dy * dy);
            return IsPartMarkNumber(distance) ? distance : 0;
        }

        private static bool IsPartMarkNumber(double value)
        { return value > 1e-12 && IsFinitePartMarkCoordinate(value); }
        private static bool IsFinitePartMarkCoordinate(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static string AppendPartMarkNotice(string current, string next)
        { return string.IsNullOrEmpty(current) ? next : current + "; " + next; }
    }
}
