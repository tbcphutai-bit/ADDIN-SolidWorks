// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.DrawingDxfExporting
{
    internal enum LaserMarkFeatureKind
    {
        None,
        CutExtrude,
        HoleWizard,
        OtherCut,
        Pattern
    }

    /// <summary>
    /// Decides whether a model edge/feature is laser-marking geometry.
    ///
    /// The rule is still the explicit MARK token in the feature name, but it is
    /// no longer limited to Cut-Extrude:
    ///  - Cut-Extrude, Hole Wizard / Advanced Hole / Simple Hole and the other
    ///    material-removal cuts (Revolve/Sweep/Loft cut...) named with MARK;
    ///  - any feature pattern (Linear/Circular/Mirror/Sketch/Curve/Table/Fill...)
    ///    that is itself named with MARK, or whose seed feature is a MARK
    ///    feature. Pattern instance faces belong to the pattern feature, which
    ///    is why patterned MARK holes were previously exported on CUT.
    ///
    /// Results are cached by feature name for one Drawing View export, and only
    /// names are kept so no SolidWorks RCW outlives the export.
    /// </summary>
    internal sealed class LaserMarkFeatureRecognizer
    {
        private const string MarkToken = "MARK";
        private const int MaxOwnerDepth = 5;
        private const int MaxPatternDepth = 4;

        internal sealed class Resolution
        {
            /// <summary>Feature that owns the face (pattern, hole, cut).</summary>
            public string SourceName { get; set; } = "";
            public LaserMarkFeatureKind SourceKind { get; set; }

            /// <summary>Feature whose name carries the MARK token.</summary>
            public string MarkedName { get; set; } = "";
            public LaserMarkFeatureKind MarkedKind { get; set; }

            public string Description
            {
                get
                {
                    string source = SourceName + " (" +
                        DescribeKind(SourceKind) + ")";
                    return string.Equals(
                            SourceName,
                            MarkedName,
                            StringComparison.OrdinalIgnoreCase)
                        ? source
                        : source + " ← " + MarkedName + " (" +
                          DescribeKind(MarkedKind) + ")";
                }
            }
        }

        private readonly Dictionary<string, Resolution> _cache =
            new Dictionary<string, Resolution>(StringComparer.Ordinal);

        public static string DescribeKind(LaserMarkFeatureKind kind)
        {
            switch (kind)
            {
                case LaserMarkFeatureKind.CutExtrude: return "Cut-Extrude";
                case LaserMarkFeatureKind.HoleWizard: return "Hole";
                case LaserMarkFeatureKind.OtherCut: return "Cut";
                case LaserMarkFeatureKind.Pattern: return "Pattern";
                default: return "Feature";
            }
        }

        public static bool HasMarkToken(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            string text = name.Trim();
            // Japanese IME input can produce full-width "ＭＡＲＫ" that looks
            // identical in the FeatureManager tree.
            try { text = text.Normalize(NormalizationForm.FormKC); }
            catch { }
            return text.IndexOf(
                MarkToken,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Workshop convention (10/10/2026): the feature name decides. When one
        // MARK feature shows holes of several sizes, its Ø6 holes are the
        // marks and the other sizes are ordinary holes that stay on CUT.
        public const double MarkHoleRadiusMeters = 0.003;
        public const double HoleRadiusToleranceMeters = 5e-5;   // 0.05 mm

        public static bool IsMarkHoleRadius(double radius)
        {
            return Math.Abs(radius - MarkHoleRadiusMeters) <= HoleRadiusToleranceMeters;
        }

        /// <summary>
        /// True when the holes of one MARK feature have the mark size and at
        /// least one other size: only the mark size is engraved.
        /// </summary>
        public static bool OnlyMarkSizeIsMark(IEnumerable<double> radiiOfOneFeature)
        {
            bool markSize = false, otherSize = false;
            foreach (double radius in radiiOfOneFeature)
            {
                if (IsMarkHoleRadius(radius)) markSize = true;
                else otherSize = true;
            }
            return markSize && otherSize;
        }

        public static LaserMarkFeatureKind Classify(
            string type,
            string legacyType,
            string name)
        {
            string compactType = Compact(type);
            string compactLegacyType = Compact(legacyType);
            string compactName = Compact(name);

            // Flat-Pattern (and its folders) owns every flattened face of a
            // sheet-metal body. It must never be treated as a feature pattern,
            // otherwise one MARK parent would move the whole outline.
            if (ContainsAny(compactType, compactLegacyType, "FLATPATTERN", "FOLDER"))
                return LaserMarkFeatureKind.None;

            if (IsAny(compactType, compactLegacyType, "CUT", "CUTTHIN", "ICE") ||
                StartsWithAny(compactType, compactLegacyType,
                    "CUTEXTRUDE", "EXTRUDECUT", "EXTRUDEDCUT") ||
                StartsWithAny(compactName, null,
                    "CUTEXTRUDE", "EXTRUDECUT", "EXTRUDEDCUT"))
                return LaserMarkFeatureKind.CutExtrude;

            // HoleWzd, AdvHoleWzd, HoleSimple/SimpleHole, HoleSeries...
            if (ContainsAny(compactType, compactLegacyType, "HOLE"))
                return LaserMarkFeatureKind.HoleWizard;

            // LPattern, CirPattern, MirrorPattern, SketchPattern,
            // CurvePattern, TablePattern, FillPattern, VarPattern,
            // DerivedLPattern, LocalLPattern...
            if (ContainsAny(compactType, compactLegacyType, "PATTERN"))
                return LaserMarkFeatureKind.Pattern;

            // RevCut, SweepCut, LoftCut, BoundaryCut, SurfCut, CutThicken...
            // (cut-list folders were excluded above).
            if (ContainsAny(compactType, compactLegacyType, "CUT") &&
                !ContainsAny(compactType, compactLegacyType, "LIST"))
                return LaserMarkFeatureKind.OtherCut;

            return LaserMarkFeatureKind.None;
        }

        public bool TryResolve(IEdge edge, out Resolution resolution)
        {
            resolution = null;
            if (edge == null) return false;

            object[] adjacentFaces = null;
            try { adjacentFaces = edge.GetTwoAdjacentFaces2() as object[]; }
            catch { }
            if (adjacentFaces == null) return false;

            foreach (object value in adjacentFaces)
            {
                if (TryResolveFace(value as IFace2, out resolution)) return true;
            }

            return false;
        }

        /// <summary>
        /// Mặt <paramref name="face"/> có thuộc một feature MARK không (chủ sở hữu, cha của nó, hoặc
        /// seed của pattern). Dùng riêng từng mặt để biết một cạnh MARK là MIỆNG lỗ (một mặt kề là mặt
        /// tấm) hay nằm trong lòng hốc khắc (cả hai mặt kề thuộc MARK).
        /// </summary>
        public bool TryResolveFace(IFace2 face, out Resolution resolution)
        {
            resolution = null;
            if (face == null) return false;

            Feature owner = null;
            try { owner = face.GetFeature() as Feature; } catch { }
            if (TryResolve(owner, out resolution)) return true;

            // Fallbacks for faces whose owner feature cannot expose its
            // pattern seeds (for example a FeatureData that needs
            // AccessSelections). Keep the owning pattern as the source so
            // the visibility report does not flag it as hidden.
            Feature seed = null;
            try { seed = face.GetPatternSeedFeature() as Feature; }
            catch { }
            if (TryResolveThroughSeed(owner, seed, out resolution))
                return true;

            try { seed = face.GetSeedFeature(); } catch { seed = null; }
            return TryResolveThroughSeed(owner, seed, out resolution);
        }

        /// <summary>Tên feature sở hữu mặt (để ghi chẩn đoán khi một MARK không nhận ra được).</summary>
        public static string OwnerName(IFace2 face)
        {
            try
            {
                if (!(face?.GetFeature() is Feature owner)) return "(không có feature)";
                string name = owner.Name ?? "";
                string type = "";
                try { type = owner.GetTypeName2() ?? ""; } catch { }
                return name + (type.Length > 0 ? " [" + type + "]" : "");
            }
            catch (Exception ex)
            {
                return "(lỗi: " + ex.Message + ")";
            }
        }

        public bool TryResolve(Feature feature, out Resolution resolution)
        {
            resolution = null;
            Feature candidate = feature;
            for (int depth = 0;
                 candidate != null && depth < MaxOwnerDepth;
                 depth++)
            {
                if (TryResolveSingle(
                        candidate,
                        0,
                        new HashSet<string>(StringComparer.Ordinal),
                        out resolution))
                    return true;

                try { candidate = candidate.GetOwnerFeature(); }
                catch { candidate = null; }
            }

            return false;
        }

        private bool TryResolveThroughSeed(
            Feature owner,
            Feature seed,
            out Resolution resolution)
        {
            resolution = null;
            if (seed == null || !TryResolve(seed, out Resolution seedResolution))
                return false;

            string ownerName = "";
            string ownerType = "";
            try { ownerName = owner?.Name ?? ""; } catch { }
            try { ownerType = owner?.GetTypeName2() ?? ""; } catch { }
            // Only a real pattern owner becomes the source. In the flattened
            // state the owner can be Flat-Pattern, which is not a MARK source.
            resolution =
                Classify(ownerType, "", ownerName) != LaserMarkFeatureKind.Pattern
                    ? seedResolution
                    : new Resolution
                    {
                        SourceName = ownerName,
                        SourceKind = LaserMarkFeatureKind.Pattern,
                        MarkedName = seedResolution.MarkedName,
                        MarkedKind = seedResolution.MarkedKind
                    };
            return true;
        }

        private bool TryResolveSingle(
            Feature feature,
            int patternDepth,
            HashSet<string> visiting,
            out Resolution resolution)
        {
            resolution = null;
            if (feature == null) return false;

            string type = "";
            string legacyType = "";
            string name = "";
            try { type = feature.GetTypeName2() ?? ""; } catch { }
            try { legacyType = feature.GetTypeName() ?? ""; } catch { }
            try { name = feature.Name ?? ""; } catch { }

            string key = name + "\u0001" + type;
            if (_cache.TryGetValue(key, out Resolution cached))
            {
                resolution = cached;
                return cached != null;
            }

            LaserMarkFeatureKind kind = Classify(type, legacyType, name);
            Resolution result = null;
            if (kind == LaserMarkFeatureKind.Pattern)
            {
                if (HasMarkToken(name))
                {
                    result = new Resolution
                    {
                        SourceName = name,
                        SourceKind = kind,
                        MarkedName = name,
                        MarkedKind = kind
                    };
                }
                else if (patternDepth < MaxPatternDepth && visiting.Add(key))
                {
                    foreach (Feature seed in GetPatternSeeds(feature))
                    {
                        if (!TryResolveSingle(
                                seed,
                                patternDepth + 1,
                                visiting,
                                out Resolution seedResolution))
                            continue;

                        result = new Resolution
                        {
                            SourceName = name,
                            SourceKind = kind,
                            MarkedName = seedResolution.MarkedName,
                            MarkedKind = seedResolution.MarkedKind
                        };
                        break;
                    }
                    visiting.Remove(key);
                }
            }
            else if (kind != LaserMarkFeatureKind.None && HasMarkToken(name))
            {
                result = new Resolution
                {
                    SourceName = name,
                    SourceKind = kind,
                    MarkedName = name,
                    MarkedKind = kind
                };
            }

            // A pattern still being expanded higher in the stack must not
            // cache a partial negative answer.
            if (result != null || !visiting.Contains(key))
                _cache[key] = result;
            resolution = result;
            return result != null;
        }

        /// <summary>
        /// Seed features of a feature pattern. PatternFeatureArray is exposed
        /// by every pattern FeatureData (Linear, Circular, Mirror, Sketch,
        /// Curve, Table, Fill...) and is read late-bound so one code path
        /// covers them all. If a SolidWorks build does not return it without
        /// AccessSelections, fall back to the feature parents; the caller still
        /// requires each parent to be a MARK cut/hole feature.
        /// </summary>
        private static List<Feature> GetPatternSeeds(Feature pattern)
        {
            var seeds = new List<Feature>();
            object definition = null;
            try { definition = pattern.GetDefinition(); }
            catch (Exception ex)
            {
                DxfTrace.Debug(
                    "LASER_MARK pattern definition unavailable: " + ex.Message);
            }

            if (definition != null)
            {
                try
                {
                    object value = definition.GetType().InvokeMember(
                        "PatternFeatureArray",
                        BindingFlags.GetProperty,
                        null,
                        definition,
                        null);
                    AddFeatures(value, seeds);
                }
                catch (Exception ex)
                {
                    DxfTrace.Debug(
                        "LASER_MARK PatternFeatureArray unavailable: " +
                        ex.Message);
                }
            }

            if (seeds.Count == 0)
            {
                try { AddFeatures(pattern.GetParents(), seeds); }
                catch { }
            }

            return seeds;
        }

        private static void AddFeatures(object value, ICollection<Feature> target)
        {
            if (value is object[] values)
            {
                foreach (object item in values)
                {
                    if (item is Feature feature) target.Add(feature);
                }
            }
            else if (value is Feature single)
            {
                target.Add(single);
            }
        }

        private static string Compact(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var builder = new StringBuilder(value.Length);
            foreach (char character in value.Trim())
            {
                if (character == '-' || character == '_' ||
                    character == ' ' ||
                    (character >= '\u2010' && character <= '\u2014'))
                    continue;
                builder.Append(char.ToUpperInvariant(character));
            }
            return builder.ToString();
        }

        private static bool IsAny(
            string first,
            string second,
            params string[] values)
        {
            foreach (string value in values)
            {
                if (first == value || second == value) return true;
            }
            return false;
        }

        private static bool StartsWithAny(
            string first,
            string second,
            params string[] values)
        {
            foreach (string value in values)
            {
                if ((!string.IsNullOrEmpty(first) &&
                     first.StartsWith(value, StringComparison.Ordinal)) ||
                    (!string.IsNullOrEmpty(second) &&
                     second.StartsWith(value, StringComparison.Ordinal)))
                    return true;
            }
            return false;
        }

        private static bool ContainsAny(
            string first,
            string second,
            params string[] values)
        {
            foreach (string value in values)
            {
                if ((!string.IsNullOrEmpty(first) &&
                     first.IndexOf(value, StringComparison.Ordinal) >= 0) ||
                    (!string.IsNullOrEmpty(second) &&
                     second.IndexOf(value, StringComparison.Ordinal) >= 0))
                    return true;
            }
            return false;
        }
    }
}
