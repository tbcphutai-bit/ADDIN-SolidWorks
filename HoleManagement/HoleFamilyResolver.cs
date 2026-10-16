using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;

namespace ADDIN.HoleManagement
{
    internal sealed class HoleFamily
    {
        public readonly List<Feature> Features = new List<Feature>();
        public readonly List<Feature> Roots = new List<Feature>();
        public readonly List<Feature> Patterns = new List<Feature>();
        public string FamilyId;
        public string Label;
        public double? DiameterMm;
        public double? SlotLengthMm;
        public string HoleKey;
        public int PhysicalHoleCount;
    }

    internal sealed class HoleFamilyResolver
    {
        private readonly ModelDoc2 model;
        private readonly Dictionary<long, Feature> features = new Dictionary<long, Feature>();
        private readonly Dictionary<long, HoleFeatureKind> kinds = new Dictionary<long, HoleFeatureKind>();
        private readonly Dictionary<long, List<long>> patternSeeds = new Dictionary<long, List<long>>();
        private readonly Dictionary<long, Dictionary<long, int>> patternFaceCounts =
            new Dictionary<long, Dictionary<long, int>>();
        private readonly Dictionary<long, int> rootCounts = new Dictionary<long, int>();
        private readonly Dictionary<long, HoleGeometry> rootGeometries = new Dictionary<long, HoleGeometry>();
        private readonly Dictionary<long, HoleFamily> byFeature = new Dictionary<long, HoleFamily>();
        private readonly Dictionary<long, HashSet<HoleFamily>> patternFamilies = new Dictionary<long, HashSet<HoleFamily>>();
        private readonly List<HoleFamily> families = new List<HoleFamily>();

        public HoleFamilyResolver(ModelDoc2 model)
        {
            this.model = model;
            for (Feature feature = model.FirstFeature() as Feature; feature != null;
                feature = feature.GetNextFeature() as Feature)
            {
                try
                {
                    if (feature.IsSuppressed()) continue;
                    long id = HoleFeatureClassifier.Identity(feature);
                    if (!features.ContainsKey(id))
                    {
                        features.Add(id, feature);
                        kinds.Add(id, HoleFeatureClassifier.Classify(feature));
                    }
                }
                catch (Exception ex) { Debug.WriteLine("[HOLE SCAN] Feature skipped: " + ex.Message); }
            }
            BuildRootFamilies();
            BuildPatternSources();
            foreach (var pair in features)
            {
                if (kinds[pair.Key] != HoleFeatureKind.Pattern) continue;
                HashSet<HoleFamily> owners = ResolvePatternFamilies(pair.Key, new HashSet<long>());
                foreach (HoleFamily family in owners) family.Patterns.Add(pair.Value);
                if (owners.Count == 1)
                {
                    HoleFamily family = null;
                    foreach (HoleFamily owner in owners) family = owner;
                    family.Features.Add(pair.Value);
                    byFeature[pair.Key] = family;
                    HoleMetadata metadata = HoleMetadataService.Read(model, pair.Value);
                    if (metadata != null)
                    {
                        if (family.FamilyId == null) family.FamilyId = metadata.FamilyId;
                        if (family.Label == null) family.Label = metadata.Label;
                    }
                }
                else if (owners.Count > 1)
                    Debug.WriteLine("[HOLE FAMILY] Shared pattern kept separate from hole labels: " + pair.Value.Name);
            }
            foreach (HoleFamily family in families)
            {
                foreach (Feature pattern in family.Patterns)
                {
                    long id = HoleFeatureClassifier.Identity(pattern);
                    List<long> seeds;
                    if (!patternSeeds.TryGetValue(id, out seeds)) continue;
                    int input = 0;
                    foreach (long seed in seeds)
                        input += CountPatternInput(id, seed, family, new HashSet<long>());
                    int instances = HoleFeatureClassifier.GetPatternInstances(pattern);
                    family.PhysicalHoleCount += Math.Max(0, instances - 1) * input;
                    Debug.WriteLine("[HOLE FAMILY] Pattern=" + pattern.Name + ", Instances=" + instances +
                        ", Input=" + input + ", HoleKey=" + family.HoleKey);
                }
                Debug.WriteLine("[HOLE FAMILY] HoleKey=" + family.HoleKey + ", Label=" + family.Label +
                    ", Quantity=" + family.PhysicalHoleCount);
            }
        }

        public IList<HoleFamily> Families { get { return families; } }

        public HoleFamily Find(Feature feature)
        {
            if (feature == null) return null;
            HoleFamily family;
            return byFeature.TryGetValue(HoleFeatureClassifier.Identity(feature), out family) &&
                family.PhysicalHoleCount > 0 ? family : null;
        }

        public int PatternFamilyCount(Feature feature)
        {
            if (feature == null) return 0;
            HashSet<HoleFamily> owners;
            return patternFamilies.TryGetValue(HoleFeatureClassifier.Identity(feature), out owners)
                ? owners.Count : 0;
        }

        private void BuildRootFamilies()
        {
            var byId = new Dictionary<string, HoleFamily>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in features)
            {
                long id = pair.Key;
                if (kinds[id] == HoleFeatureKind.Pattern) continue;
                Feature feature = pair.Value;
                HoleMetadata metadata = HoleMetadataService.Read(model, feature);
                HoleGeometry geometry = HoleGeometryAnalyzer.Analyze(feature, kinds[id]);
                int count = geometry == null ? 0 : geometry.Count;
                double slotWidth, slotLength;
                bool legacySlot = TryLegacySlot(feature.Name, out slotWidth, out slotLength);
                if (count == 0 && (metadata != null ||
                    (kinds[id] != HoleFeatureKind.Unsupported &&
                     (TryLegacyDiameter(feature.Name).HasValue || legacySlot)))) count = 1;
                if (count <= 0) continue;

                HoleFamily family = null;
                if (metadata != null && !string.IsNullOrWhiteSpace(metadata.FamilyId))
                    byId.TryGetValue(metadata.FamilyId, out family);
                if (family == null)
                {
                    family = new HoleFamily();
                    families.Add(family);
                    if (metadata != null && !string.IsNullOrWhiteSpace(metadata.FamilyId))
                        byId[metadata.FamilyId] = family;
                }
                family.Roots.Add(feature);
                family.Features.Add(feature);
                byFeature[id] = family;
                rootCounts[id] = count;
                rootGeometries[id] = geometry;
                family.PhysicalHoleCount += count;
                if (metadata != null)
                {
                    if (family.FamilyId == null) family.FamilyId = metadata.FamilyId;
                    if (family.Label == null) family.Label = metadata.Label;
                    if (!family.DiameterMm.HasValue) family.DiameterMm = metadata.DiameterMm;
                }
                if (geometry != null)
                {
                    if (!family.DiameterMm.HasValue) family.DiameterMm = geometry.DiameterMm;
                    if (!family.SlotLengthMm.HasValue) family.SlotLengthMm = geometry.SlotLengthMm;
                }
                if (legacySlot)
                {
                    if (!family.DiameterMm.HasValue) family.DiameterMm = slotWidth;
                    if (!family.SlotLengthMm.HasValue) family.SlotLengthMm = slotLength;
                }
                if (!family.DiameterMm.HasValue) family.DiameterMm = TryLegacyDiameter(feature.Name);
                Debug.WriteLine("[HOLE FAMILY] Root=" + feature.Name + ", Type=" + feature.GetTypeName2() +
                    ", Width=" + family.DiameterMm + ", SlotLength=" + family.SlotLengthMm + ", Count=" + count);
            }
            foreach (HoleFamily family in families)
                family.HoleKey = family.SlotLengthMm.HasValue && family.DiameterMm.HasValue
                    ? "SLOT " + Size(family.DiameterMm.Value) + "×" + Size(family.SlotLengthMm.Value)
                    : family.DiameterMm.HasValue ? "φ" + Size(family.DiameterMm.Value) : "?";
        }

        private void BuildPatternSources()
        {
            foreach (var pair in features)
            {
                if (kinds[pair.Key] != HoleFeatureKind.Pattern) continue;
                long patternId = pair.Key;
                patternSeeds[patternId] = new List<long>();
                HoleFeatureClassifier.VisitPatternSeeds(pair.Value, model, (featureItems, faceItems, bodyItems) =>
                {
                    var fullSources = new HashSet<long>();
                    var selectedFaces = new Dictionary<long, List<Face2>>();
                    foreach (object item in Items(featureItems))
                    {
                        Feature seed = item as Feature;
                        if (seed == null) continue;
                        long id = FindFeatureId(seed);
                        AddSource(patternId, id);
                        fullSources.Add(id);
                    }
                    foreach (object item in Items(faceItems))
                    {
                        Face2 face = item as Face2;
                        if (face == null) continue;
                        try
                        {
                            Feature owner = face.GetFeature() as Feature;
                            if (owner != null && HoleFeatureClassifier.Classify(owner) == HoleFeatureKind.Pattern)
                            {
                                Feature original = face.GetPatternSeedFeature() as Feature;
                                if (original == null) continue;
                                owner = original;
                            }
                            if (owner == null) continue;
                            long id = FindFeatureId(owner);
                            if (id == 0) continue;
                            List<Face2> list;
                            if (!selectedFaces.TryGetValue(id, out list))
                                selectedFaces[id] = list = new List<Face2>();
                            list.Add(face);
                        }
                        catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Face seed skipped: " + ex.Message); }
                    }
                    foreach (object item in Items(bodyItems))
                    {
                        Body2 body = item as Body2;
                        if (body == null) continue;
                        foreach (var root in rootCounts)
                            if (RootTouchesBody(features[root.Key], body))
                            {
                                AddSource(patternId, root.Key);
                                fullSources.Add(root.Key);
                            }
                    }
                    foreach (var selection in selectedFaces)
                    {
                        if (fullSources.Contains(selection.Key)) continue;
                        int total;
                        if (!rootCounts.TryGetValue(selection.Key, out total))
                        {
                            AddSource(patternId, selection.Key);
                            continue;
                        }
                        int selected = HoleGeometryAnalyzer.CountSelectedFaces(selection.Value,
                            rootGeometries[selection.Key]);
                        if (selected == 0 && total == 1) selected = 1;
                        if (selected == 0) continue;
                        AddSource(patternId, selection.Key);
                        Dictionary<long, int> counts;
                        if (!patternFaceCounts.TryGetValue(patternId, out counts))
                            patternFaceCounts[patternId] = counts = new Dictionary<long, int>();
                        counts[selection.Key] = selected;
                    }
                });
            }
        }

        private static IEnumerable<object> Items(object value)
        {
            Array array = value as Array;
            if (array != null) foreach (object item in array) yield return item;
        }

        private long FindFeatureId(Feature feature)
        {
            if (feature == null) return 0;
            try
            {
                long id = HoleFeatureClassifier.Identity(feature);
                if (features.ContainsKey(id)) return id;
                string name = feature.Name;
                foreach (var pair in features)
                    if (string.Equals(pair.Value.Name, name, StringComparison.Ordinal)) return pair.Key;
            }
            catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Seed feature lookup failed: " + ex.Message); }
            return 0;
        }

        private void AddSource(long patternId, long seedId)
        {
            if (seedId == 0 || seedId == patternId || !features.ContainsKey(seedId)) return;
            List<long> list = patternSeeds[patternId];
            if (!list.Contains(seedId)) list.Add(seedId);
        }

        private static bool RootTouchesBody(Feature feature, Body2 body)
        {
            try
            {
                foreach (object item in Items(feature.GetFaces()))
                {
                    Face2 face = item as Face2;
                    Body2 owner = face == null ? null : face.GetBody() as Body2;
                    if (owner != null && SameBody(owner, body)) return true;
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Body seed lookup failed: " + ex.Message); }
            return false;
        }

        private static bool SameBody(Body2 first, Body2 second)
        {
            IntPtr a = IntPtr.Zero, b = IntPtr.Zero;
            try
            {
                a = Marshal.GetIUnknownForObject(first);
                b = Marshal.GetIUnknownForObject(second);
                if (a == b) return true;
                string name = first.Name;
                return !string.IsNullOrWhiteSpace(name) &&
                    string.Equals(name, second.Name, StringComparison.Ordinal);
            }
            finally
            {
                if (a != IntPtr.Zero) Marshal.Release(a);
                if (b != IntPtr.Zero) Marshal.Release(b);
            }
        }

        private HashSet<HoleFamily> ResolvePatternFamilies(long id, HashSet<long> visiting)
        {
            HashSet<HoleFamily> result;
            if (patternFamilies.TryGetValue(id, out result)) return result;
            result = new HashSet<HoleFamily>();
            if (!visiting.Add(id)) return result;
            try
            {
                List<long> seeds;
                if (patternSeeds.TryGetValue(id, out seeds))
                    foreach (long seed in seeds)
                    {
                        HoleFamily family;
                        if (byFeature.TryGetValue(seed, out family)) result.Add(family);
                        else if (kinds[seed] == HoleFeatureKind.Pattern)
                            result.UnionWith(ResolvePatternFamilies(seed, visiting));
                    }
            }
            finally { visiting.Remove(id); }
            patternFamilies[id] = result;
            return result;
        }

        private int CountOutput(long id, HoleFamily family, HashSet<long> visiting)
        {
            if (!visiting.Add(id)) return 0;
            try
            {
                if (kinds[id] != HoleFeatureKind.Pattern)
                {
                    HoleFamily owner;
                    int count;
                    return byFeature.TryGetValue(id, out owner) && owner == family &&
                        rootCounts.TryGetValue(id, out count) ? count : 0;
                }
                List<long> seeds;
                if (!patternSeeds.TryGetValue(id, out seeds)) return 0;
                int input = 0;
                foreach (long seed in seeds) input += CountPatternInput(id, seed, family, visiting);
                return input * HoleFeatureClassifier.GetPatternInstances(features[id]);
            }
            finally { visiting.Remove(id); }
        }

        private int CountPatternInput(long patternId, long seedId, HoleFamily family,
            HashSet<long> visiting)
        {
            Dictionary<long, int> selected;
            int count;
            if (patternFaceCounts.TryGetValue(patternId, out selected) &&
                selected.TryGetValue(seedId, out count))
            {
                HoleFamily owner;
                return byFeature.TryGetValue(seedId, out owner) && owner == family ? count : 0;
            }
            return CountOutput(seedId, family, visiting);
        }

        private static string Size(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static bool TryLegacySlot(string name, out double width, out double length)
        {
            width = length = 0;
            Match match = Regex.Match(name ?? "", @"\bSLOT\s*(?<w>\d+(?:[.,]\d+)?)\s*[x×X]\s*(?<l>\d+(?:[.,]\d+)?)",
                RegexOptions.IgnoreCase);
            return match.Success &&
                double.TryParse(match.Groups["w"].Value.Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out width) &&
                double.TryParse(match.Groups["l"].Value.Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out length) && width > 0 && length > width;
        }

        public static double? TryLegacyDiameter(string name)
        {
            Match match = Regex.Match(name ?? "", "[φΦ⌀Øø]\\s*(?<size>\\d+(?:[.,]\\d+)?)");
            double value;
            return match.Success && double.TryParse(match.Groups["size"].Value.Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? (double?)value : null;
        }
    }
}
