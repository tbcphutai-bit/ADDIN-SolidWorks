using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using SolidWorks.Interop.sldworks;

namespace ADDIN.HoleManagement
{
    internal sealed class HoleGeometry
    {
        public int Count;
        public double? DiameterMm;
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
                        return new HoleGeometry { Count = Math.Max(1, points),
                            DiameterMm = diameter > 0 ? (double?)(diameter * 1000.0) : null };
                    }
                }
                catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Wizard geometry failed: " + ex.Message); }
            }

            if (kind != HoleFeatureKind.CutCandidate && kind != HoleFeatureKind.Simple &&
                kind != HoleFeatureKind.Wizard) return null;
            var spans = new Dictionary<string, double>(StringComparer.Ordinal);
            var radii = new Dictionary<string, double>(StringComparer.Ordinal);
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
            if (axes.Count == 0) return null;
            double first = double.MaxValue;
            foreach (double value in axes.Values) first = Math.Min(first, value);
            return new HoleGeometry { Count = axes.Count, DiameterMm = first };
        }

        private static string Round(double value)
        {
            return Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
