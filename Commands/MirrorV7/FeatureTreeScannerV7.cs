using System;
using System.Collections.Generic;
using System.Text;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.MirrorV7
{
    /// <summary>Read-only FeatureManager scanner for V7 Phase 2.</summary>
    public static class FeatureTreeScannerV7
    {
        public static MirrorV7ModelGraph Scan(ModelDoc2 model)
        {
            if (model == null) throw new ArgumentNullException("model");
            MirrorV7Diagnostics.LogPhase("PHASE2", "Feature Tree scan started.");
            var graph = new MirrorV7ModelGraph();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int order = 0;
            Feature feature = model.FirstFeature() as Feature;
            while (feature != null)
            {
                ScanRecursive(feature, 0, graph, visited, ref order);
                feature = feature.GetNextFeature() as Feature;
            }
            LogGraph(graph);
            MirrorV7Diagnostics.LogPhase("PHASE2", "Feature Tree scan completed. nodeCount=" + graph.Count);
            return graph;
        }

        private static void ScanRecursive(Feature feature, int depth, MirrorV7ModelGraph graph, HashSet<string> visited, ref int order)
        {
            if (feature == null) return;
            string name = SafeName(feature), type = ResolveType(feature), key = BuildKey(feature, name, type);
            if (visited.Add(key))
            {
                var node = new MirrorV7FeatureNode { TreeOrder = order++, Depth = depth, Name = name, TypeName = type,
                    Feature = feature, IsSuppressed = SafeSuppressed(feature), Role = Classify(type) };
                CaptureParents(feature, node); CaptureChildren(feature, node); graph.Add(node);
            }
            Feature sub = null;
            try { sub = feature.GetFirstSubFeature() as Feature; } catch (Exception ex) { MirrorV7Diagnostics.Log("[PHASE2][SUBFEATURE_READ_WARNING] " + name + " :: " + ex.Message); }
            while (sub != null)
            {
                ScanRecursive(sub, depth + 1, graph, visited, ref order);
                Feature next = null;
                try { next = sub.GetNextSubFeature() as Feature; } catch (Exception ex) { MirrorV7Diagnostics.Log("[PHASE2][SUBFEATURE_NEXT_WARNING] " + name + " :: " + ex.Message); }
                sub = next;
            }
        }

        private static void CaptureParents(Feature feature, MirrorV7FeatureNode node)
        {
            try
            {
                object[] items = feature.GetParents() as object[];
                if (items == null) return;
                foreach (object item in items) { Feature parent = item as Feature; string name = SafeName(parent); if (parent != null && !string.IsNullOrWhiteSpace(name) && !node.ParentFeatureNames.Contains(name)) node.ParentFeatureNames.Add(name); }
            }
            catch (Exception ex) { MirrorV7Diagnostics.Log("[PHASE2][PARENT_READ_WARNING] " + node.Name + " :: " + ex.Message); }
        }
        private static void CaptureChildren(Feature feature, MirrorV7FeatureNode node)
        {
            try
            {
                object[] items = feature.GetChildren() as object[];
                if (items == null) return;
                foreach (object item in items) { Feature child = item as Feature; string name = SafeName(child); if (child != null && !string.IsNullOrWhiteSpace(name) && !node.ChildFeatureNames.Contains(name)) node.ChildFeatureNames.Add(name); }
            }
            catch (Exception ex) { MirrorV7Diagnostics.Log("[PHASE2][CHILD_READ_WARNING] " + node.Name + " :: " + ex.Message); }
        }
        private static string ResolveType(Feature feature)
        {
            return FeatureTypeHelperV7.GetEffectiveType(feature);
        }
        private static MirrorV7FeatureRole Classify(string type)
        {
            string t = (type ?? "").Trim().ToLowerInvariant();
            if (t.Contains("folder") || t == "detailcabinet" || t == "annotationviewfeat" || t == "ambientlight" || t == "directionlight" || t == "attribute" || t == "weldmentfeature" || t.Contains("history") || t.Contains("material") || t.Contains("sensor") || t.Contains("selectionset")) return MirrorV7FeatureRole.System;
            if (t == "originprofilefeature" || t.Contains("refplane") || t.Contains("refaxis") || t == "refpoint" || t.Contains("coordsys") || t.Contains("coordinate") || t == "origin") return MirrorV7FeatureRole.Reference;
            if (t == "profilefeature" || t == "3dprofilefeature") return MirrorV7FeatureRole.Sketch;
            if (t == "fillrefsurface" || t == "offsetrefsurface" || t.Contains("surface")) return MirrorV7FeatureRole.SurfaceFeature;
            if (t == "flatpattern" || t == "smmiteredflange" || t.Contains("sheetmetal") || t.Contains("baseflange") || t.Contains("smbaseflange") || t.Contains("edgeflange") || t.Contains("bend") || t.Contains("flattenbends") || t.Contains("processbends") || t.Contains("hem") || t.Contains("jog") || t.Contains("loftedbend")) return MirrorV7FeatureRole.SheetMetal;
            if (t == "curvepattern" || t.Contains("pattern") || t.Contains("lpattern") || t.Contains("cirpattern")) return MirrorV7FeatureRole.Pattern;
            if (t.Contains("helix") || t.Contains("spiral") || t.Contains("compositecurve") || t.Contains("projectedcurve") || t.Contains("curve")) return MirrorV7FeatureRole.Curve;
            if (t == "deletebody" || t == "moveface" || t == "movecopybody" || t == "movebody" || t.Contains("boss") || t == "cut" || t.Contains("extrusion") || t.Contains("extrude") || t.Contains("revolve") || t.Contains("sweep") || t.Contains("loft") || t.Contains("boundary") || t.Contains("fillet") || t.Contains("chamfer") || t.Contains("hole")) return MirrorV7FeatureRole.BodyFeature;
            return MirrorV7FeatureRole.Unknown;
        }
        private static string SafeName(Feature feature) { try { return feature == null ? "" : feature.Name ?? ""; } catch { return ""; } }
        private static bool SafeSuppressed(Feature feature) { try { return feature != null && feature.IsSuppressed(); } catch { return false; } }
        private static string BuildKey(Feature feature, string name, string type) { return !string.IsNullOrWhiteSpace(name) ? name + "|" + (type ?? "") : "<unnamed>|" + (type ?? "") + "|" + feature.GetHashCode(); }
        private static void LogGraph(MirrorV7ModelGraph graph)
        {
            MirrorV7Diagnostics.Log("======================================"); MirrorV7Diagnostics.Log("[PHASE2] MIRROR V7 FEATURE GRAPH"); MirrorV7Diagnostics.Log("======================================");
            var counts = new Dictionary<MirrorV7FeatureRole, int>();
            foreach (MirrorV7FeatureNode node in graph.Nodes)
            {
                int count; counts.TryGetValue(node.Role, out count); counts[node.Role] = count + 1;
                string indent = new string(' ', Math.Max(0, node.Depth * 2));
                MirrorV7Diagnostics.Log(string.Format("[PHASE2][NODE] {0}#{1} depth={2} name=\"{3}\" type=\"{4}\" role={5} suppressed={6}", indent, node.TreeOrder, node.Depth, node.Name, node.TypeName, node.Role, node.IsSuppressed));
                MirrorV7Diagnostics.Log(string.Format("[PHASE2][DEPS] name=\"{0}\" parents=[{1}] children=[{2}]", node.Name, string.Join(",", node.ParentFeatureNames), string.Join(",", node.ChildFeatureNames)));
            }
            MirrorV7Diagnostics.Log("---------- ROLE SUMMARY ----------");
            foreach (MirrorV7FeatureRole role in Enum.GetValues(typeof(MirrorV7FeatureRole))) { int count; counts.TryGetValue(role, out count); MirrorV7Diagnostics.Log("[PHASE2][SUMMARY] " + role + "=" + count); }
            MirrorV7Diagnostics.Log("[PHASE2][SUMMARY] TOTAL=" + graph.Count); MirrorV7Diagnostics.Log("======================================");
        }
    }
}
