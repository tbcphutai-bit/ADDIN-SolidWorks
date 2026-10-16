using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
        private readonly List<SkippedPatternHole> skippedPatternHoles = new List<SkippedPatternHole>();

        private sealed class SkippedPatternHole
        {
            public HoleAxis Seed;
            public HoleAxis Expected;
            public string FeatureName;
            public int InstanceIndex;
            public double PitchM;
        }

        // Read selections before collecting ANY final body/face handles. AccessSelections
        // temporarily rolls back the feature; ReleaseSelectionAccess restores it. Keep
        // only managed coordinates across that boundary, never historical COM topology.
        private void ReadSkippedPatternHoles(ModelDoc2 topDocument, object[] components)
        {
            skippedPatternHoles.Clear();
            foreach (Component2 component in components.OfType<Component2>())
            {
                ThrowIfCanceled();
                if (!IsUsableComponent(component)) continue;
                ModelDoc2 part = component.GetModelDoc2() as ModelDoc2;
                if (part == null || part.GetType() != (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocPART) continue;
                Configuration configuration = part.ConfigurationManager.ActiveConfiguration;
                if (configuration == null || !string.Equals(configuration.Name, SafeReferencedConfiguration(component), StringComparison.OrdinalIgnoreCase)) continue;
                MathTransform transform = component.Transform2;
                if (transform == null) continue;
                var features = new List<Feature>();
                for (Feature feature = part.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                    if (feature.GetTypeName2() == "LPattern" && !feature.IsSuppressed()) features.Add(feature);
                foreach (Feature feature in features)
                {
                    ThrowIfCanceled();
                    LinearPatternFeatureData pattern = feature.GetDefinition() as LinearPatternFeatureData;
                    if (pattern == null || pattern.BodyPattern || pattern.GetSkippedItemCount() == 0) continue;
                    string featureName = feature.Name;
                    int[] indices = ToObjectArray(pattern.SkippedItemArray).Select(Convert.ToInt32).Distinct().ToArray();
                    double pitch = pattern.D1Spacing;
                    bool accessed = false;
                    try
                    {
                        accessed = pattern.AccessSelections(topDocument, component);
                        if (!accessed) { Debug.WriteLine("[CHECK HOLE ASSY] SKIPPED_PATTERN unavailable: " + featureName); continue; }
                        var faces = ToObjectArray(pattern.PatternFaceArray).OfType<Face2>().ToList();
                        foreach (Feature seedFeature in ToObjectArray(pattern.PatternFeatureArray).OfType<Feature>())
                            faces.AddRange(ToObjectArray(seedFeature.GetFaces()).OfType<Face2>());
                        foreach (Face2 face in faces)
                        {
                            HoleAxis seed;
                            if (!TryReadHoleCylinder(face, transform, SafeComponentName(component), SafeComponentPath(component), "", out seed)
                                || seed.IsSlotEnd) continue;
                            foreach (int index in indices)
                            {
                                // Use the API's instance index directly. Adding one here
                                // would put the X on the neighbouring (existing) hole.
                                MathTransform instanceTransform = pattern.GetTransform(index);
                                MathTransform combined = instanceTransform == null ? null : instanceTransform.Multiply(transform) as MathTransform;
                                HoleAxis expected;
                                if (combined == null || !TryReadHoleCylinder(face, combined, seed.ComponentOccurrence, seed.ComponentPath, "", out expected)) continue;
                                skippedPatternHoles.Add(new SkippedPatternHole { Seed = seed, Expected = expected,
                                    FeatureName = featureName, InstanceIndex = index, PitchM = pitch });
                                Debug.WriteLine("[CHECK HOLE ASSY] SKIPPED_PATTERN seed=" + FormatPointMm(seed.Center)
                                    + ", expected=" + FormatPointMm(expected.Center) + ", feature=" + featureName + ", index=" + index);
                            }
                        }
                    }
                    finally
                    {
                        // Do not swallow restoration failure and continue with a rolled-back model.
                        if (accessed) pattern.ReleaseSelectionAccess();
                    }
                }
            }
        }

        private int AppendSkippedPatternIssues(List<HoleAxis> holes, List<PatternIssue> issues, HashSet<string> keys)
        {
            int added = 0;
            foreach (SkippedPatternHole skipped in skippedPatternHoles)
            {
                ThrowIfCanceled();
                HoleAxis seed = holes.FirstOrDefault(h => h.ComponentOccurrence == skipped.Seed.ComponentOccurrence
                    && h.Source != HoleSource.RecoveredDeformed && AxisAngleDeg(h.Direction, skipped.Seed.Direction) <= SameHoleAngleToleranceDeg
                    && Math.Abs(h.RadiusM - skipped.Seed.RadiusM) <= SameHolePositionToleranceM
                    && Distance(h.Center, skipped.Seed.Center) <= PositionToleranceM);
                if (seed == null)
                {
                    Debug.WriteLine("[CHECK HOLE ASSY] SKIPPED_PATTERN unavailable: seed not present in final body, feature=" + skipped.FeatureName);
                    continue;
                }
                HoleAxis expected = skipped.Expected.Clone();
                expected.BodyName = seed.BodyName;
                double[] surface = LocateMissingHoleSurface(expected, expected, expected.Center);
                if (!IsPoint(surface)) continue;
                if (AddSkippedPatternIssue(skipped, expected, surface, issues, keys)) added++;
            }
            return added;
        }

        private static bool AddSkippedPatternIssue(SkippedPatternHole skipped, HoleAxis expected, double[] surface,
            List<PatternIssue> issues, HashSet<string> keys)
        {
            if (!IsPoint(surface) || !IsDirection(expected.Direction)) return false;
            double[] normal = Scale(expected.Direction, Dot(Subtract(surface, expected.Center), expected.Direction) < 0 ? -1 : 1);
            PatternIssue existing = issues.FirstOrDefault(x => x.IsMissingHole && x.SecondComponent == expected.ComponentOccurrence
                && IsPoint(x.ExpectedCenter) && IsDirection(x.AnchorDirection)
                && AxisAngleDeg(x.AnchorDirection, expected.Direction) <= SameHoleAngleToleranceDeg
                && Length(Subtract(Subtract(x.ExpectedCenter, surface), Scale(expected.Direction, Dot(Subtract(x.ExpectedCenter, surface), expected.Direction)))) <= SameHolePositionToleranceM
                && Math.Abs(Dot(Subtract(x.ExpectedCenter, surface), expected.Direction)) <= expected.AxialLength + SlotPairToleranceM);
            if (existing != null)
            {
                existing.SkippedPatternName = skipped.FeatureName;
                existing.SkippedInstanceIndex = skipped.InstanceIndex;
                return false;
            }
            string key = "MISSING|" + MissingBodyKey(expected.ComponentOccurrence, expected.BodyName)
                + "|" + QuantizePoint(surface, SameHolePositionToleranceM);
            if (!keys.Add(key)) return false;
            issues.Add(new PatternIssue { IsMissingHole = true, Kind = "NG THIEU LO", FirstComponent = expected.ComponentOccurrence,
                SecondComponent = expected.ComponentOccurrence, ExpectedHole = expected, ExpectedCenter = ClonePoint(surface),
                AnchorDirection = ClonePoint(expected.Direction), DisplayNormal = normal, ExpectedPitchM = skipped.PitchM,
                SkippedPatternName = skipped.FeatureName, SkippedInstanceIndex = skipped.InstanceIndex });
            Debug.WriteLine("[CHECK HOLE ASSY] MISSING_HOLE missingOn=" + expected.ComponentOccurrence + ", center=" + FormatPointMm(surface)
                + ", pattern=" + skipped.FeatureName + ", skippedIndex=" + skipped.InstanceIndex + ", evidence=SKIPPED_HOLE_INSTANCE_AND_TARGET_MATERIAL");
            return true;
        }
    }
}
