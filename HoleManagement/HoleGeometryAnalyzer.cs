using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.HoleManagement
{
    internal sealed class HoleGeometry
    {
        public int Count;
        public double? DiameterMm;
        public double? SlotLengthMm;
    }

    internal static class HoleGeometryAnalyzer
    {
        public static HoleGeometry Analyze(Feature feature, HoleFeatureKind kind)
        {
            if (kind == HoleFeatureKind.Simple)
            {
                try
                {
                    var data = feature.GetDefinition() as SimpleHoleFeatureData2;
                    if (data != null && data.Diameter > 0)
                        return new HoleGeometry { Count = 1, DiameterMm = data.Diameter * 1000.0 };
                }
                catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Simple hole data failed: " + ex.Message); }
            }
            if (kind == HoleFeatureKind.Wizard)
            {
                try
                {
                    var data = feature.GetDefinition() as WizardHoleFeatureData2;
                    if (data != null)
                    {
                        double diameter = data.HoleDiameter > 0 ? data.HoleDiameter : data.Diameter;
                        int points = data.GetSketchPointCount();
                        double slotLength = 0;
                        string wizardType = Enum.GetName(typeof(swWzdHoleTypes_e), data.Type) ?? "";
                        if (wizardType.IndexOf("Slot", StringComparison.OrdinalIgnoreCase) >= 0)
                            try { slotLength = data.Length; }
                            catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Wizard slot length unavailable: " + ex.Message); }
                        return new HoleGeometry { Count = Math.Max(1, points),
                            DiameterMm = diameter > 0 ? (double?)(diameter * 1000.0) : null,
                            SlotLengthMm = diameter > 0 && slotLength > diameter
                                ? (double?)(slotLength * 1000.0) : null };
                    }
                }
                catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Wizard geometry failed: " + ex.Message); }
            }

            if (kind != HoleFeatureKind.CutCandidate && kind != HoleFeatureKind.Simple &&
                kind != HoleFeatureKind.Wizard) return null;
            var spans = new Dictionary<string, double>(StringComparer.Ordinal);
            var radii = new Dictionary<string, double>(StringComparer.Ordinal);
            var cylinders = new Dictionary<string, double[]>(StringComparer.Ordinal);
            try
            {
                var faces = feature.GetFaces() as Array;
                if (faces != null)
                    foreach (object item in faces)
                    {
                        var face = item as Face2;
                        var surface = face == null ? null : face.GetSurface() as Surface;
                        if (surface == null || !surface.IsCylinder()) continue;
                        var cylinder = surface.CylinderParams as double[];
                        var uv = face.GetUVBounds() as double[];
                        if (cylinder == null || cylinder.Length < 7 || uv == null || uv.Length < 4) continue;
                        double angle = Math.Abs(uv[1] - uv[0]);
                        if (angle < 0.01) continue;
                        double x = cylinder[0], y = cylinder[1], z = cylinder[2];
                        double dx = cylinder[3], dy = cylinder[4], dz = cylinder[5];
                        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (length < 1e-9 || cylinder[6] <= 0) continue;
                        dx /= length; dy /= length; dz /= length;
                        if (dx < -1e-9 || (Math.Abs(dx) < 1e-9 && dy < -1e-9) ||
                            (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9 && dz < 0))
                        { dx = -dx; dy = -dy; dz = -dz; }
                        double along = x * dx + y * dy + z * dz;
                        string key = Round(dx) + "/" + Round(dy) + "/" + Round(dz) + "/" +
                            Round(x - along * dx) + "/" + Round(y - along * dy) + "/" + Round(z - along * dz);
                        double diameterMm = 2000.0 * cylinder[6];
                        string circleKey = key + "/" + Round(cylinder[6]);
                        double old;
                        spans.TryGetValue(circleKey, out old);
                        spans[circleKey] = old + angle;
                        radii[circleKey] = diameterMm;
                        cylinders[circleKey] = new[] { x - along * dx, y - along * dy,
                            z - along * dz, dx, dy, dz, cylinder[6] };
                    }
            }
            catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Cylinder analysis failed: " + ex.Message); }
            var axes = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var entry in spans)
            {
                // A split circular wall may have several faces; slot ends remain partial circles.
                if (entry.Value < 5.9) continue;
                int lastSlash = entry.Key.LastIndexOf('/');
                string axisKey = entry.Key.Substring(0, lastSlash);
                double old;
                double diameter = radii[entry.Key];
                if (!axes.TryGetValue(axisKey, out old) || diameter < old) axes[axisKey] = diameter;
            }
            // SketchSlot keeps the original pairing when several slots are close together.
            // Pair cylindrical ends only for slots drawn from ordinary arcs and lines.
            var slots = ReadSketchSlots(feature);
            var partial = new List<string>();
            foreach (var entry in spans)
                if (entry.Value >= 2.7 && entry.Value <= 3.6) partial.Add(entry.Key);
            var used = new HashSet<string>(StringComparer.Ordinal);
            if (slots.Count == 0) foreach (string firstKey in partial)
            {
                if (used.Contains(firstKey)) continue;
                double[] firstCylinder = cylinders[firstKey];
                string bestKey = null;
                double bestDistance = double.MaxValue;
                foreach (string secondKey in partial)
                {
                    if (secondKey == firstKey || used.Contains(secondKey)) continue;
                    double[] secondCylinder = cylinders[secondKey];
                    if (Math.Abs(firstCylinder[6] - secondCylinder[6]) > 0.00005 ||
                        firstCylinder[3] * secondCylinder[3] + firstCylinder[4] * secondCylinder[4] +
                        firstCylinder[5] * secondCylinder[5] < 0.999) continue;
                    double distance = Math.Sqrt(Math.Pow(firstCylinder[0] - secondCylinder[0], 2) +
                        Math.Pow(firstCylinder[1] - secondCylinder[1], 2) +
                        Math.Pow(firstCylinder[2] - secondCylinder[2], 2));
                    if (distance < 0.00001 || distance >= bestDistance) continue;
                    bestDistance = distance;
                    bestKey = secondKey;
                }
                if (bestKey == null) continue;
                used.Add(firstKey);
                used.Add(bestKey);
                slots.Add(new HoleGeometry { Count = 1, DiameterMm = 2000.0 * firstCylinder[6],
                    SlotLengthMm = 1000.0 * (bestDistance + 2.0 * firstCylinder[6]) });
            }
            if (slots.Count > 0)
            {
                if (axes.Count > 0 || slots.Exists(slot =>
                    Math.Abs(slot.DiameterMm.Value - slots[0].DiameterMm.Value) > 0.05 ||
                    Math.Abs(slot.SlotLengthMm.Value - slots[0].SlotLengthMm.Value) > 0.05))
                {
                    Debug.WriteLine("[HOLE FAMILY] Mixed hole/slot sizes in one feature: " + feature.Name);
                    return null;
                }
                return new HoleGeometry { Count = slots.Count, DiameterMm = slots[0].DiameterMm,
                    SlotLengthMm = slots[0].SlotLengthMm };
            }
            if (axes.Count == 0) return null;
            double first = double.MaxValue;
            foreach (double value in axes.Values) first = Math.Min(first, value);
            return new HoleGeometry { Count = axes.Count, DiameterMm = first };
        }

        private static List<HoleGeometry> ReadSketchSlots(Feature feature)
        {
            var result = new List<HoleGeometry>();
            try
            {
                for (Feature sub = feature.GetFirstSubFeature() as Feature; sub != null;
                    sub = sub.GetNextSubFeature() as Feature)
                {
                    Sketch sketch = sub.GetSpecificFeature2() as Sketch;
                    if (sketch == null) continue;
                    Array items = sketch.GetSketchSlots() as Array;
                    if (items == null) continue;
                    foreach (object item in items)
                    {
                        SketchSlot slot = item as SketchSlot;
                        if (slot == null || slot.CreationType !=
                            (int)swSketchSlotCreationType_e.swSketchSlotCreationType_line || slot.Width <= 0)
                            continue;
                        double length = slot.Length + (slot.LengthType ==
                            (int)swSketchSlotLengthType_e.swSketchSlotLengthType_CenterCenter ? slot.Width : 0);
                        if (length <= slot.Width) continue;
                        result.Add(new HoleGeometry { Count = 1, DiameterMm = slot.Width * 1000.0,
                            SlotLengthMm = length * 1000.0 });
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Sketch slot analysis failed: " + ex.Message); }
            return result;
        }

        // A face pattern can select only one hole from a Cut-Extrude that created
        // several holes. Count the selected cylindrical walls, not every hole in
        // the owning feature.
        public static int CountSelectedFaces(IEnumerable<Face2> faces, HoleGeometry root)
        {
            if (root == null) return 0;
            var spans = new Dictionary<string, double>(StringComparer.Ordinal);
            var cylinders = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (Face2 face in faces)
            {
                try
                {
                    Surface surface = face.GetSurface() as Surface;
                    if (surface == null || !surface.IsCylinder()) continue;
                    double[] cylinder = surface.CylinderParams as double[];
                    double[] uv = face.GetUVBounds() as double[];
                    if (cylinder == null || cylinder.Length < 7 || uv == null || uv.Length < 4 ||
                        cylinder[6] <= 0) continue;
                    double dx = cylinder[3], dy = cylinder[4], dz = cylinder[5];
                    double norm = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (norm < 1e-9) continue;
                    dx /= norm; dy /= norm; dz /= norm;
                    if (dx < -1e-9 || (Math.Abs(dx) < 1e-9 && dy < -1e-9) ||
                        (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9 && dz < 0))
                    { dx = -dx; dy = -dy; dz = -dz; }
                    double along = cylinder[0] * dx + cylinder[1] * dy + cylinder[2] * dz;
                    double x = cylinder[0] - along * dx;
                    double y = cylinder[1] - along * dy;
                    double z = cylinder[2] - along * dz;
                    string key = Round(dx) + "/" + Round(dy) + "/" + Round(dz) + "/" +
                        Round(x) + "/" + Round(y) + "/" + Round(z) + "/" + Round(cylinder[6]);
                    double old;
                    spans.TryGetValue(key, out old);
                    spans[key] = old + Math.Abs(uv[1] - uv[0]);
                    cylinders[key] = new[] { x, y, z, dx, dy, dz, cylinder[6] };
                }
                catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Selected face skipped: " + ex.Message); }
            }
            if (!root.SlotLengthMm.HasValue)
            {
                var fullAxes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in spans)
                    if (entry.Value >= 5.9)
                        fullAxes.Add(entry.Key.Substring(0, entry.Key.LastIndexOf('/')));
                return Math.Min(root.Count, fullAxes.Count);
            }
            double expected = (root.SlotLengthMm.Value - root.DiameterMm.GetValueOrDefault()) / 1000.0;
            if (expected <= 0) return 0;
            var partial = new List<string>();
            foreach (var entry in spans)
                if (entry.Value >= 2.7 && entry.Value <= 3.6) partial.Add(entry.Key);
            var used = new HashSet<string>(StringComparer.Ordinal);
            int count = 0;
            foreach (string firstKey in partial)
            {
                if (used.Contains(firstKey)) continue;
                double[] first = cylinders[firstKey];
                foreach (string secondKey in partial)
                {
                    if (secondKey == firstKey || used.Contains(secondKey)) continue;
                    double[] second = cylinders[secondKey];
                    if (Math.Abs(first[6] - second[6]) > 0.00005 ||
                        first[3] * second[3] + first[4] * second[4] + first[5] * second[5] < 0.999)
                        continue;
                    double distance = Math.Sqrt(Math.Pow(first[0] - second[0], 2) +
                        Math.Pow(first[1] - second[1], 2) + Math.Pow(first[2] - second[2], 2));
                    if (Math.Abs(distance - expected) > 0.00005) continue;
                    used.Add(firstKey);
                    used.Add(secondKey);
                    count++;
                    break;
                }
            }
            return Math.Min(root.Count, count);
        }

        private static string Round(double value)
        {
            return Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
