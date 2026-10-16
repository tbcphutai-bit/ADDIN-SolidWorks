using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ADDIN.Commands.DrawingDxfExporting;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    // Sets or removes the MARK token in the name of cut features. The DXF export
    // already reads that token (LaserMarkFeatureRecognizer); nothing else is
    // stored in the Part.
    internal sealed class MarkFeatureCommand
    {
        internal const string Suffix = "-mark";
        private static readonly Regex Token = new Regex("[-_ ‐-—－]*(mark|ｍａｒｋ|ＭＡＲＫ)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.RightToLeft);
        private readonly ISldWorks app;

        internal MarkFeatureCommand(ISldWorks application) { app = application; }

        internal sealed class Target
        {
            internal string Name = "", NewName, Note = "";
            internal string Line { get { return NewName == null ? Name + "  (" + Note + ")" : Name + "  →  " + NewName; } }
        }

        internal static LaserMarkFeatureKind KindOf(Feature feature)
        {
            if (feature == null) return LaserMarkFeatureKind.None;
            string type = "", legacy = "", name = "";
            try { type = feature.GetTypeName2() ?? ""; } catch (COMException) { }
            try { legacy = feature.GetTypeName() ?? ""; } catch (COMException) { }
            try { name = feature.Name ?? ""; } catch (COMException) { }
            return LaserMarkFeatureRecognizer.Classify(type, legacy, name);
        }

        // Cut or 2D sketch features behind a picked object: a feature, a face of
        // the cut, one of its edges, a 2D sketch, or a 2D sketch segment/point.
        // A pattern resolves to its seed, because the export marks every pattern
        // whose seed carries the token.
        internal List<Feature> Resolve(object pick)
        {
            var found = new List<Feature>();
            try
            {
                var feature = pick as Feature;
                if (feature != null) { AddFeature(feature, null, found); return found; }
                var face = pick as IFace2;
                if (face != null) { AddFace(face, found); return found; }
                var edge = pick as IEdge;
                if (edge != null)
                {
                    foreach (IFace2 adjacent in (edge.GetTwoAdjacentFaces2() as object[] ?? new object[0]).OfType<IFace2>())
                        AddFace(adjacent, found);
                    return found;
                }
                var segment = pick as ISketchSegment;
                if (segment != null)
                {
                    Feature sketchFeature = FindSketchFeature(segment.GetSketch() as ISketch);
                    if (sketchFeature != null) AddFeature(sketchFeature, null, found);
                    return found;
                }
                var point = pick as ISketchPoint;
                if (point != null)
                {
                    Feature sketchFeature = FindSketchFeature(point.GetSketch() as ISketch);
                    if (sketchFeature != null) AddFeature(sketchFeature, null, found);
                    return found;
                }
                var sketch = pick as ISketch;
                if (sketch != null)
                {
                    Feature sketchFeature = FindSketchFeature(sketch);
                    if (sketchFeature != null) AddFeature(sketchFeature, null, found);
                    return found;
                }
            }
            catch (COMException) { }
            catch (InvalidComObjectException) { }
            return found;
        }

        internal bool CanResolve(object pick) { return Resolve(pick).Count > 0; }

        private Feature FindSketchFeature(ISketch sketch)
        {
            if (sketch == null) return null;
            var direct = sketch as Feature;
            if (direct != null) return direct;
            ModelDoc2 model = null;
            try { model = app?.IActiveDoc2 ?? app?.ActiveDoc as ModelDoc2; } catch (COMException) { }
            if (model == null) return null;
            for (var feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
            {
                if (MatchesSketch(feature, sketch)) return feature;
                for (var sub = feature.GetFirstSubFeature() as Feature; sub != null; sub = sub.GetNextSubFeature() as Feature)
                    if (MatchesSketch(sub, sketch)) return sub;
            }
            return null;
        }

        private bool MatchesSketch(Feature feature, ISketch sketch)
        {
            if (feature == null || sketch == null) return false;
            try
            {
                var specific = feature.GetSpecificFeature2() as ISketch;
                if (specific == null) return false;
                if (ReferenceEquals(specific, sketch)) return true;
                return app != null && app.IsSame(specific, sketch) == (int)swObjectEquality.swObjectSame;
            }
            catch (COMException) { return false; }
        }

        private static void AddFace(IFace2 face, List<Feature> found)
        {
            var owner = face.GetFeature() as Feature;
            if (owner == null) return;
            Feature seed = null;
            if (KindOf(owner) == LaserMarkFeatureKind.Pattern)
            {
                try { seed = face.GetPatternSeedFeature() as Feature; } catch (COMException) { }
            }
            AddFeature(owner, seed, found);
        }

        private static void AddFeature(Feature feature, Feature knownSeed, List<Feature> found)
        {
            LaserMarkFeatureKind kind = KindOf(feature);
            if (kind == LaserMarkFeatureKind.None) return;
            if (kind == LaserMarkFeatureKind.Sketch2D)
            {
                Feature owner = null;
                try { owner = feature.GetOwnerFeature() as Feature; } catch (COMException) { }
                if (owner != null)
                {
                    string ownerType = "";
                    try { ownerType = owner.GetTypeName2() ?? ""; } catch (COMException) { }
                    if (!string.Equals(ownerType, "FlatPattern", StringComparison.OrdinalIgnoreCase))
                    {
                        LaserMarkFeatureKind ownerKind = KindOf(owner);
                        if (ownerKind != LaserMarkFeatureKind.None &&
                            ownerKind != LaserMarkFeatureKind.Sketch2D &&
                            !LaserMarkFeatureRecognizer.HasMarkToken(feature.Name ?? ""))
                        {
                            AddFeature(owner, null, found);
                            return;
                        }
                        if (!LaserMarkFeatureRecognizer.HasMarkToken(feature.Name ?? ""))
                            return;
                    }
                }
                AddUnique(feature, found);
                return;
            }
            if (kind == LaserMarkFeatureKind.Pattern)
            {
                var seeds = new List<Feature>();
                if (knownSeed != null && KindOf(knownSeed) != LaserMarkFeatureKind.None && KindOf(knownSeed) != LaserMarkFeatureKind.Pattern)
                    seeds.Add(knownSeed);
                else seeds.AddRange(PatternSeeds(feature).Where(s => KindOf(s) != LaserMarkFeatureKind.None && KindOf(s) != LaserMarkFeatureKind.Pattern));
                if (seeds.Count > 0) { foreach (Feature seed in seeds) AddUnique(seed, found); return; }
            }
            AddUnique(feature, found);
        }

        private static void AddUnique(Feature feature, List<Feature> found)
        {
            string name = feature.Name ?? "";
            if (!found.Any(f => string.Equals(f.Name, name, StringComparison.Ordinal))) found.Add(feature);
        }

        private static IEnumerable<Feature> PatternSeeds(Feature pattern)
        {
            var seeds = new List<Feature>();
            try
            {
                object definition = pattern.GetDefinition();
                if (definition != null)
                {
                    object value = definition.GetType().InvokeMember("PatternFeatureArray", BindingFlags.GetProperty, null, definition, null);
                    if (value is object[]) seeds.AddRange(((object[])value).OfType<Feature>());
                    else if (value is Feature) seeds.Add((Feature)value);
                }
            }
            catch (Exception) { }
            if (seeds.Count == 0)
            {
                try { seeds.AddRange((pattern.GetParents() as object[] ?? new object[0]).OfType<Feature>()); }
                catch (COMException) { }
            }
            return seeds;
        }

        internal static string WithMark(string name) { return (name ?? "").TrimEnd() + Suffix; }

        internal static string WithoutMark(string name)
        {
            string text = name ?? "";
            Match match = Token.Match(text);
            if (!match.Success) return text;
            return (text.Substring(0, match.Index) + text.Substring(match.Index + match.Length)).Trim(' ', '-', '_');
        }

        // What the command would rename; NewName stays null when nothing changes.
        internal List<Target> Plan(ModelDoc2 model, IEnumerable<object> picks, bool set)
        {
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("Hãy mở Part.");
            var features = new List<Feature>();
            foreach (object pick in picks ?? new object[0])
            {
                List<Feature> resolved = Resolve(pick);
                if (resolved.Count == 0)
                    throw new InvalidOperationException("Đối tượng đã chọn không thuộc feature cắt / lỗ / sketch 2D nào. Miệng lỗ vát (Chamfer) tự thành nét khắc, không cần đặt MARK.");
                foreach (Feature feature in resolved) AddUnique(feature, features);
            }
            var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                occupied.Add(feature.Name ?? "");
            var plan = new List<Target>();
            foreach (Feature feature in features)
            {
                var target = new Target { Name = feature.Name ?? "" };
                bool marked = LaserMarkFeatureRecognizer.HasMarkToken(target.Name);
                if (set && marked) target.Note = "đã có MARK";
                else if (!set && !marked) target.Note = "không có MARK";
                else
                {
                    string wanted = set ? WithMark(target.Name) : WithoutMark(target.Name);
                    if (wanted.Length == 0) wanted = KindOf(feature) == LaserMarkFeatureKind.Sketch2D ? "Sketch" : "Cut";
                    string unique = wanted;
                    for (int index = 2; occupied.Contains(unique) && !string.Equals(unique, target.Name, StringComparison.OrdinalIgnoreCase); index++)
                        unique = set ? WithoutMark(wanted) + "-" + index + Suffix : wanted + "-" + index;
                    target.NewName = unique;
                    occupied.Add(unique);
                }
                plan.Add(target);
            }
            return plan;
        }

        internal int Apply(ModelDoc2 model, IEnumerable<KeyValuePair<string, string>> renames)
        {
            var part = model as PartDoc;
            if (part == null) throw new InvalidOperationException("Hãy mở Part.");
            int changed = 0;
            foreach (KeyValuePair<string, string> rename in renames ?? new KeyValuePair<string, string>[0])
            {
                var feature = FindFeatureByName(model, part, rename.Key);
                if (feature == null) throw new InvalidOperationException("Không còn thấy feature " + rename.Key + ". Hãy mở lại lệnh.");
                feature.Name = rename.Value;
                changed++;
            }
            if (changed > 0)
            {
                model.SetSaveFlag();
                model.GraphicsRedraw2();
            }
            return changed;
        }

        private static Feature FindFeatureByName(ModelDoc2 model, PartDoc part, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var direct = part.FeatureByName(name) as Feature;
            if (direct != null) return direct;
            for (var feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
            {
                if (string.Equals(feature.Name, name, StringComparison.OrdinalIgnoreCase)) return feature;
                for (var sub = feature.GetFirstSubFeature() as Feature; sub != null; sub = sub.GetNextSubFeature() as Feature)
                    if (string.Equals(sub.Name, name, StringComparison.OrdinalIgnoreCase)) return sub;
            }
            return null;
        }
    }
}
