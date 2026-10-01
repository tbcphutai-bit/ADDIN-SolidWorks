using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        public int PhysicalHoleCount;
    }

    internal sealed class HoleFamilyResolver
    {
        private readonly ModelDoc2 model;
        private readonly Dictionary<long, Feature> features = new Dictionary<long, Feature>();
        private readonly Dictionary<long, List<long>> edges = new Dictionary<long, List<long>>();
        private readonly Dictionary<long, List<long>> patternSeeds = new Dictionary<long, List<long>>();
        private readonly Dictionary<long, HoleFeatureKind> kinds = new Dictionary<long, HoleFeatureKind>();
        private readonly List<HoleFamily> families = new List<HoleFamily>();
        private readonly Dictionary<long, HoleFamily> byFeature = new Dictionary<long, HoleFamily>();

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
                    if (!features.ContainsKey(id)) features.Add(id, feature);
                }
                catch (Exception ex) { Debug.WriteLine("[HOLE SCAN] Feature skipped: " + ex.Message); }
            }

            foreach (var pair in features)
            {
                long id = pair.Key;
                var kind = HoleFeatureClassifier.Classify(pair.Value);
                kinds[id] = kind;
                if (kind != HoleFeatureKind.Pattern) continue;
                var seeds = new List<long>();
                foreach (Feature seed in HoleFeatureClassifier.GetPatternSeeds(pair.Value))
                {
                    long seedId = HoleFeatureClassifier.Identity(seed);
                    if (!features.ContainsKey(seedId) || seeds.Contains(seedId)) continue;
                    seeds.Add(seedId);
                    Link(id, seedId);
                }
                patternSeeds[id] = seeds;
            }

            var metadataGroups = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in features)
            {
                HoleMetadata metadata = HoleMetadataService.Read(model, pair.Value);
                if (metadata == null || string.IsNullOrWhiteSpace(metadata.FamilyId)) continue;
                long first;
                if (metadataGroups.TryGetValue(metadata.FamilyId, out first)) Link(first, pair.Key);
                else metadataGroups[metadata.FamilyId] = pair.Key;
            }

            var visited = new HashSet<long>();
            foreach (long id in features.Keys)
            {
                if (!visited.Add(id)) continue;
                var family = new HoleFamily();
                var queue = new Queue<long>();
                queue.Enqueue(id);
                while (queue.Count > 0)
                {
                    long current = queue.Dequeue();
                    Feature feature = features[current];
                    family.Features.Add(feature);
                    byFeature[current] = family;
                    if (kinds[current] == HoleFeatureKind.Pattern) family.Patterns.Add(feature);
                    else family.Roots.Add(feature);
                    List<long> neighbours;
                    if (edges.TryGetValue(current, out neighbours))
                        foreach (long next in neighbours)
                            if (visited.Add(next)) queue.Enqueue(next);
                }
                Populate(family);
                if (family.PhysicalHoleCount > 0) families.Add(family);
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

        private void Link(long first, long second)
        {
            List<long> list;
            if (!edges.TryGetValue(first, out list)) edges[first] = list = new List<long>();
            if (!list.Contains(second)) list.Add(second);
            if (!edges.TryGetValue(second, out list)) edges[second] = list = new List<long>();
            if (!list.Contains(first)) list.Add(first);
        }

        private void Populate(HoleFamily family)
        {
            int directCount = 0;
            foreach (Feature feature in family.Roots)
            {
                long id = HoleFeatureClassifier.Identity(feature);
                HoleMetadata metadata = HoleMetadataService.Read(model, feature);
                HoleGeometry geometry = HoleGeometryAnalyzer.Analyze(feature, kinds[id]);
                if (metadata != null)
                {
                    if (family.FamilyId == null) family.FamilyId = metadata.FamilyId;
                    if (family.Label == null) family.Label = metadata.Label;
                    if (!family.DiameterMm.HasValue) family.DiameterMm = metadata.DiameterMm;
                }
                if (!family.DiameterMm.HasValue && geometry != null) family.DiameterMm = geometry.DiameterMm;
                int count = geometry == null ? 0 : geometry.Count;
                if (count == 0 && (metadata != null ||
                    (kinds[id] != HoleFeatureKind.Unsupported && TryLegacyDiameter(feature.Name).HasValue))) count = 1;
                directCount += count;
                if (!family.DiameterMm.HasValue) family.DiameterMm = TryLegacyDiameter(feature.Name);
                Debug.WriteLine("[HOLE FAMILY] Root=" + feature.Name + ", Type=" + feature.GetTypeName2() +
                    ", Diameter=" + family.DiameterMm + ", Count=" + count);
            }

            foreach (Feature pattern in family.Patterns)
            {
                HoleMetadata metadata = HoleMetadataService.Read(model, pattern);
                if (metadata == null) continue;
                if (family.FamilyId == null) family.FamilyId = metadata.FamilyId;
                if (family.Label == null) family.Label = metadata.Label;
                if (!family.DiameterMm.HasValue) family.DiameterMm = metadata.DiameterMm;
            }
            if (directCount == 0) return;

            int countTotal = directCount;
            foreach (Feature pattern in family.Patterns)
            {
                long id = HoleFeatureClassifier.Identity(pattern);
                List<long> seeds;
                if (!patternSeeds.TryGetValue(id, out seeds) || seeds.Count == 0) continue;
                int input = 0;
                foreach (long seed in seeds)
                    input += CountOutput(seed, new HashSet<long>());
                int instances = HoleFeatureClassifier.GetPatternInstances(pattern);
                countTotal += Math.Max(0, instances - 1) * input;
                Debug.WriteLine("[HOLE FAMILY] Pattern=" + pattern.Name + ", Instances=" + instances +
                    ", Input=" + input);
            }
            family.PhysicalHoleCount = countTotal;
            Debug.WriteLine("[HOLE FAMILY] FamilyId=" + family.FamilyId + ", Label=" + family.Label +
                ", Quantity=" + countTotal);
        }

        private int CountOutput(long id, HashSet<long> visiting)
        {
            if (!visiting.Add(id)) return 0;
            try
            {
                Feature feature;
                if (!features.TryGetValue(id, out feature)) return 0;
                if (kinds[id] != HoleFeatureKind.Pattern)
                {
                    HoleGeometry geometry = HoleGeometryAnalyzer.Analyze(feature, kinds[id]);
                    if (geometry != null) return geometry.Count;
                    return HoleMetadataService.Read(model, feature) != null ||
                        (kinds[id] != HoleFeatureKind.Unsupported && TryLegacyDiameter(feature.Name).HasValue) ? 1 : 0;
                }
                List<long> seeds;
                if (!patternSeeds.TryGetValue(id, out seeds)) return 0;
                int input = 0;
                foreach (long seed in seeds) input += CountOutput(seed, visiting);
                return input * HoleFeatureClassifier.GetPatternInstances(feature);
            }
            finally { visiting.Remove(id); }
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
