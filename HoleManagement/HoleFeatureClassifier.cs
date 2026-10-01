using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace ADDIN.HoleManagement
{
    internal enum HoleFeatureKind { Unsupported, Wizard, Simple, CutCandidate, Pattern }

    internal static class HoleFeatureClassifier
    {
        public static long Identity(Feature feature)
        {
            IntPtr pointer = Marshal.GetIUnknownForObject(feature);
            try { return pointer.ToInt64(); }
            finally { Marshal.Release(pointer); }
        }

        public static HoleFeatureKind Classify(Feature feature)
        {
            if (feature == null) return HoleFeatureKind.Unsupported;
            try
            {
                object definition = feature.GetDefinition();
                if (IsPatternDefinition(definition)) return HoleFeatureKind.Pattern;
                if (definition is WizardHoleFeatureData2) return HoleFeatureKind.Wizard;
                if (definition is SimpleHoleFeatureData2) return HoleFeatureKind.Simple;
                string type = feature.GetTypeName2() ?? "";
                if (string.Equals(type, "Hole", StringComparison.OrdinalIgnoreCase)) return HoleFeatureKind.Simple;
                if (string.Equals(type, "Cut", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "CutThin", StringComparison.OrdinalIgnoreCase)) return HoleFeatureKind.CutCandidate;
                Debug.WriteLine("[HOLE SCAN] Unsupported feature type=" + type + ", name=" + feature.Name);
            }
            catch (Exception ex) { Debug.WriteLine("[HOLE SCAN] Classification failed: " + ex.Message); }
            return HoleFeatureKind.Unsupported;
        }

        private static bool IsPatternDefinition(object definition)
        {
            return definition is LinearPatternFeatureData ||
                   definition is CurveDrivenPatternFeatureData ||
                   definition is CircularPatternFeatureData ||
                   definition is FillPatternFeatureData ||
                   definition is TablePatternFeatureData;
        }

        public static List<Feature> GetPatternSeeds(Feature feature)
        {
            var result = new List<Feature>();
            try
            {
                object definition = feature.GetDefinition();
                object value = null;
                var linear = definition as LinearPatternFeatureData;
                var curve = definition as CurveDrivenPatternFeatureData;
                var circular = definition as CircularPatternFeatureData;
                var fill = definition as FillPatternFeatureData;
                var table = definition as TablePatternFeatureData;
                if (linear != null) value = linear.PatternFeatureArray;
                else if (curve != null) value = curve.PatternFeatureArray;
                else if (circular != null) value = circular.PatternFeatureArray;
                else if (fill != null) value = fill.PatternFeatureArray;
                else if (table != null) value = table.PatternFeatureArray;
                var array = value as Array;
                if (array != null)
                    foreach (object item in array)
                        if (item is Feature) result.Add((Feature)item);
            }
            catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Pattern seeds failed: " + ex.Message); }
            return result;
        }

        public static int GetPatternInstances(Feature feature)
        {
            try
            {
                object definition = feature.GetDefinition();
                var linear = definition as LinearPatternFeatureData;
                if (linear != null)
                    return Math.Max(1, TwoDirection(linear.D1TotalInstances, linear.D2TotalInstances,
                        linear.D2PatternSeedOnly) - linear.GetSkippedItemCount());
                var curve = definition as CurveDrivenPatternFeatureData;
                if (curve != null)
                    return Math.Max(1, TwoDirection(curve.D1InstanceCount, curve.D2InstanceCount,
                        curve.D2PatternSeedOnly) - curve.GetSkippedItemCount());
                var circular = definition as CircularPatternFeatureData;
                if (circular != null)
                    return Math.Max(1, (circular.TotalInstances2 > 0 ? circular.TotalInstances2 : circular.TotalInstances)
                        - circular.GetSkippedItemCount());
                var fill = definition as FillPatternFeatureData;
                if (fill != null)
                {
                    if (fill.NoOfInstances <= 0)
                        Debug.WriteLine("[HOLE FAMILY] FillPattern NoOfInstances unavailable: " + feature.Name);
                    var skipped = fill.SkippedItemArray as Array;
                    return Math.Max(1, fill.NoOfInstances - (skipped == null ? 0 : skipped.Length));
                }
                var table = definition as TablePatternFeatureData;
                if (table != null) return Math.Max(1, table.GetPointCount() - table.GetSkippedItemCount());
            }
            catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Pattern count failed: " + ex.Message); }
            return 1;
        }

        private static int TwoDirection(int first, int second, bool seedOnly)
        {
            first = Math.Max(1, first);
            second = Math.Max(1, second);
            return seedOnly ? first + second - 1 : first * second;
        }
    }
}
