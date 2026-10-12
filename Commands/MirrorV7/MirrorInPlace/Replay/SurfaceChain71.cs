using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    // In-context Offset Surface (its source face belongs to ANOTHER Part of an
    // assembly). Opened on its own, the Part only holds a frozen copy of that
    // surface, so it cannot be re-attached to a reflected face: there is none
    // in this document. The user chose to replace its source by a fixed
    // surface at the reflected position:
    //   - a fixed (imported) surface, the reflected result moved back by the
    //     offset distance, is inserted just before the Offset Surface;
    //   - the Offset Surface is re-attached to it with its own distance, which
    //     also removes the external reference;
    //   - the surface features that follow (offset of a surface, extend,
    //     delete body, cut with surface) lose their references when their
    //     parent is regenerated, so each is re-attached to the entity found at
    //     the reflected position of the one it used in the source.
    public static partial class RollbackReplayEngineV7
    {
        private sealed class SurfaceRecipe71
        {
            internal Body2 Frozen; internal double SourceDistance, Area; internal double[][] Points;
            internal readonly List<Action<object, ModelDoc2>> Setters = new List<Action<object, ModelDoc2>>();
        }
        private static readonly Dictionary<string, SurfaceRecipe71> surfaceChain71 =
            new Dictionary<string, SurfaceRecipe71>(StringComparer.Ordinal);
        // Names of the fixed surfaces this run inserted; the tree gates accept exactly these extra nodes.
        internal static readonly List<string> FrozenSurfaceImports71 = new List<string>();

        private static readonly Dictionary<string, string[]> surfaceChainProperties71 = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "OffsetRefSurface", new[] { "Entities" } },
            { "ExtendRefSurface", new[] { "Items", "Face", "Vertex", "PropagatingEdges" } },
            { "DeleteBody", new[] { "Bodies" } }
        };

        private static Type SurfaceChainType71(object data)
        {
            return data is ISurfaceOffsetFeatureData ? typeof(ISurfaceOffsetFeatureData)
                : data is ISurfaceExtendFeatureData ? typeof(ISurfaceExtendFeatureData)
                : data is IDeleteBodyFeatureData ? typeof(IDeleteBodyFeatureData) : null;
        }
        private static bool SurfaceChainAccess71(object data, ModelDoc2 model)
        {
            var offset = data as ISurfaceOffsetFeatureData; if (offset != null) return offset.AccessSelections(model, null);
            var extend = data as ISurfaceExtendFeatureData; if (extend != null) return extend.AccessSelections(model, null);
            var delete = data as IDeleteBodyFeatureData; return delete != null && delete.AccessSelections(model, null);
        }
        private static void SurfaceChainRelease71(object data)
        {
            var offset = data as ISurfaceOffsetFeatureData; if (offset != null) { offset.ReleaseSelectionAccess(); return; }
            var extend = data as ISurfaceExtendFeatureData; if (extend != null) { extend.ReleaseSelectionAccess(); return; }
            var delete = data as IDeleteBodyFeatureData; if (delete != null) delete.ReleaseSelectionAccess();
        }

        private static IEnumerable<Body2> ChainBodies71(ModelDoc2 model)
        {
            var part = (PartDoc)model;
            return ((part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[]) ?? new object[0])
                .Concat((part.GetBodies2((int)swBodyType_e.swSheetBody, false) as object[]) ?? new object[0]).Cast<Body2>()
                .Where(b => !FixedImportBody71(b));
        }

        // The fixed surface and the zero-distance Offset Surface re-attached
        // to it coincide exactly. References always belong on the features of
        // the original tree, so the bodies of the inserted fixed surfaces are
        // never candidates.
        private static bool FixedImportBody71(Body2 body)
        {
            var face = body.GetFirstFace() as Face2;
            var owner = face == null ? null : face.GetFeature() as Feature;
            return owner != null && FrozenSurfaceImports71.Contains(owner.Name);
        }
        // In-context Offset Surface name -> name of the fixed surface that replaces it.
        private static readonly Dictionary<string, string> replacedRoots71 = new Dictionary<string, string>(StringComparer.Ordinal);

        private static double[] ReflectBox71(MirrorInPlaceExecutionContextV7 context, double[] box)
        {
            double[] a = context.Reflection.ReflectPoint(new[] { box[0], box[1], box[2] });
            double[] b = context.Reflection.ReflectPoint(new[] { box[3], box[4], box[5] });
            return new[] { Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2]),
                Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Max(a[2], b[2]) };
        }
        private static bool SameBox71(double[] actual, double[] expected)
        {
            return actual != null && actual.Length >= 6 && Enumerable.Range(0, 6).All(i => Math.Abs(actual[i] - expected[i]) <= 1e-6);
        }
        private static double BodyArea71(Body2 body)
        {
            return ((body.GetFaces() as object[]) ?? new object[0]).Cast<Face2>().Sum(f => f.GetArea());
        }
        private static T Unique71<T>(List<T> matches, string what) where T : class
        {
            if (matches.Count != 1) throw new InvalidOperationException("SURFCHAIN71 reflected " + what + " is not unique; matches=" + matches.Count);
            return matches[0];
        }

        // Maps one source reference to the function that finds its counterpart
        // at the reflected position. null: this kind of reference is not handled.
        private static Func<ModelDoc2, object> ChainMap71(MirrorInPlaceExecutionContextV7 context, object value)
        {
            var face = value as Face2;
            if (face != null)
            {
                int surface = ((Surface)face.GetSurface()).Identity(); double area = face.GetArea();
                double[] raw = (double[])face.GetBox();
                double[] box = ReflectBox71(context, raw);
                // A point of the source face, reflected: the re-extended surface can
                // merge what were two faces in the source (corner topology), so a
                // face that no longer exists on its own is found as the face of the
                // same surface type that contains this point.
                var inside = face.GetClosestPointOn((raw[0] + raw[3]) / 2, (raw[1] + raw[4]) / 2, (raw[2] + raw[5]) / 2) as double[];
                double[] witness = inside == null || inside.Length < 3 ? null : context.Reflection.ReflectPoint(new[] { inside[0], inside[1], inside[2] });
                return model =>
                {
                    var all = ChainBodies71(model).SelectMany(b => ((b.GetFaces() as object[]) ?? new object[0]).Cast<Face2>()).ToList();
                    var exact = all.Where(f => ((Surface)f.GetSurface()).Identity() == surface && SameBox71(f.GetBox() as double[], box) &&
                        Math.Abs(f.GetArea() - area) <= Math.Max(1e-12, area * 1e-4)).ToList();
                    if (exact.Count == 1 || witness == null) return Unique71(exact, "face");
                    var containing = all.Where(f =>
                    {
                        if (((Surface)f.GetSurface()).Identity() != surface) return false;
                        var near = f.GetClosestPointOn(witness[0], witness[1], witness[2]) as double[];
                        return near != null && near.Length >= 3 && Math.Abs(near[0] - witness[0]) <= 5e-6 &&
                            Math.Abs(near[1] - witness[1]) <= 5e-6 && Math.Abs(near[2] - witness[2]) <= 5e-6;
                    }).ToList();
                    MirrorV7Diagnostics.Log("[SURFCHAIN71][FACE_BY_WITNESS] exact=" + exact.Count + " containing=" + containing.Count);
                    return Unique71(containing, "face");
                };
            }
            var edge = value as Edge;
            if (edge != null)
            {
                double[][] points = SampleEdge(edge).Select(context.Reflection.ReflectPoint).ToArray();
                return model => Unique71(ChainBodies71(model).SelectMany(b => ((b.GetEdges() as object[]) ?? new object[0]).Cast<Edge>())
                    .Where(e =>
                    {
                        double[][] actual = SampleEdge(e);
                        if (actual.Length != points.Length) return false;
                        Func<bool, bool> same = reversed => Enumerable.Range(0, points.Length).All(i =>
                        {
                            double[] p = points[i], q = actual[reversed ? points.Length - 1 - i : i];
                            return Math.Abs(p[0] - q[0]) <= 1e-6 && Math.Abs(p[1] - q[1]) <= 1e-6 && Math.Abs(p[2] - q[2]) <= 1e-6;
                        });
                        return same(false) || same(true);
                    }).ToList(), "edge");
            }
            var vertex = value as Vertex;
            if (vertex != null)
            {
                double[] point = context.Reflection.ReflectPoint((double[])vertex.GetPoint());
                return model => Unique71(ChainBodies71(model).SelectMany(b => ((b.GetVertices() as object[]) ?? new object[0]).Cast<Vertex>())
                    .Where(v => { var q = (double[])v.GetPoint(); return Math.Abs(q[0] - point[0]) <= 1e-6 && Math.Abs(q[1] - point[1]) <= 1e-6 && Math.Abs(q[2] - point[2]) <= 1e-6; })
                    .ToList(), "vertex");
            }
            var body = value as Body2;
            if (body != null) return ChainBodyMap71(context, body);
            var reference = value as Feature;
            if (reference != null)
            {
                string name = reference.Name, type = reference.GetTypeName2();
                return model =>
                {
                    string replacement;
                    if (replacedRoots71.TryGetValue(name, out replacement))
                    {
                        var fixedSurface = ((PartDoc)model).FeatureByName(replacement) as Feature;
                        if (fixedSurface == null) throw new InvalidOperationException("SURFCHAIN71 fixed surface missing: " + replacement);
                        return fixedSurface;
                    }
                    var mapped = ((PartDoc)model).FeatureByName(name) as Feature;
                    if (mapped == null || mapped.GetTypeName2() != type) throw new InvalidOperationException("SURFCHAIN71 referenced feature missing: " + name);
                    return mapped;
                };
            }
            return null;
        }

        internal static Func<ModelDoc2, object> ChainBodyMap71(MirrorInPlaceExecutionContextV7 context, Body2 body)
        {
            int type = body.GetType(), faces = body.GetFaceCount(); double area = BodyArea71(body);
            double[] box = ReflectBox71(context, (double[])body.GetBodyBox());
            double[][] points = ((body.GetVertices() as object[]) ?? new object[0]).Cast<Vertex>()
                .Select(v => context.Reflection.ReflectPoint((double[])v.GetPoint())).ToArray();
            return model =>
            {
                // A re-extended surface can differ from the source in corner
                // topology (face/vertex count), and a body box is not tight, so a
                // body with vertices is recognised by area and by every reflected
                // source vertex (corner merges aside) having a vertex of it within 5e-6 m.
                var found = Unique71(ChainBodies71(model).Where(b =>
                {
                    if (b.GetType() != type || Math.Abs(BodyArea71(b) - area) > Math.Max(1e-12, area * 1e-4)) return false;
                    if (points.Length == 0) return b.GetFaceCount() == faces && SameBox71(b.GetBodyBox() as double[], box);
                    var own = ((b.GetVertices() as object[]) ?? new object[0]).Cast<Vertex>().Select(v => (double[])v.GetPoint()).ToList();
                    // Corner vertices of an extension may be merged differently;
                    // three quarters of the reflected source vertices must be met.
                    return own.Count > 0 && 4 * points.Count(p => own.Any(q =>
                        Math.Abs(p[0] - q[0]) <= 5e-6 && Math.Abs(p[1] - q[1]) <= 5e-6 && Math.Abs(p[2] - q[2]) <= 5e-6)) >= 3 * points.Length;
                }).ToList(), "body");
                // Diagnostic: how exactly the found body sits on the reflected source body.
                var actual = ((found.GetVertices() as object[]) ?? new object[0]).Cast<Vertex>().Select(v => (double[])v.GetPoint()).ToList();
                double farthest = points.Length == 0 || actual.Count == 0 ? double.NaN : points.Max(p => actual.Min(q =>
                    Math.Sqrt((p[0] - q[0]) * (p[0] - q[0]) + (p[1] - q[1]) * (p[1] - q[1]) + (p[2] - q[2]) * (p[2] - q[2]))));
                MirrorV7Diagnostics.Log("[SURFCHAIN71][BODY_MATCH] type=" + type + " faces=" + faces + " vertices=" + points.Length + "/" + actual.Count +
                    " farthestVertex_m=" + farthest.ToString("E3") + " areaDelta_m2=" + (BodyArea71(found) - area).ToString("E3"));
                return found;
            };
        }

        // Names of auxiliary surface features this run suppressed in the result.
        internal static readonly HashSet<string> SuppressedAuxiliary71 = new HashSet<string>(StringComparer.Ordinal);

        // User decision: an auxiliary surface feature that lost its reference
        // and cannot be re-attached (natively: Untrim Surface after the last
        // Fold, fed by a knit of solid faces) is SUPPRESSED in the mirrored
        // Part, together with what depends on it and with the surface features
        // that only feed it, instead of blocking the Part or staying in error.
        // Refused when any feature that changes the solid depends on it.
        internal static bool TrySuppressAuxiliary71(ModelDoc2 model, Feature failed, ICollection<string> solidFeatureNames, ICollection<string> inheritedNames)
        {
            if (failed == null) return false;
            var part = (PartDoc)model;
            var descendants = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<Feature>();
            queue.Enqueue(failed);
            while (queue.Count > 0)
                foreach (Feature child in ((queue.Dequeue().GetChildren() as object[]) ?? new object[0]).Cast<Feature>())
                    if (child.Name != failed.Name && descendants.Add(child.Name)) queue.Enqueue(child);
            string blocker = descendants.FirstOrDefault(solidFeatureNames.Contains);
            if (blocker != null)
            {
                MirrorV7Diagnostics.Log("[AUX71][SUPPRESS_REFUSED] feature=" + failed.Name + " solidDependent=" + blocker);
                return false;
            }
            var group = new List<string> { failed.Name };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (string name in group.ToList())
                {
                    var member = part.FeatureByName(name) as Feature;
                    foreach (Feature parent in ((member == null ? null : member.GetParents() as object[]) ?? new object[0]).Cast<Feature>())
                    {
                        if (group.Contains(parent.Name) || !inheritedNames.Contains(parent.Name)) continue;
                        var children = ((parent.GetChildren() as object[]) ?? new object[0]).Cast<Feature>().Select(f => f.Name).ToList();
                        if (children.Count == 0 || children.Any(n => !group.Contains(n) && !descendants.Contains(n))) continue;
                        group.Add(parent.Name);
                        grew = true;
                    }
                }
            }
            foreach (string name in group)
            {
                var member = part.FeatureByName(name) as Feature;
                if (member == null || member.IsSuppressed()) continue;
                if (!member.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature, (int)swInConfigurationOpts_e.swAllConfiguration, null))
                    throw new InvalidOperationException("AUX71 cannot suppress " + name);
            }
            foreach (string name in group.Concat(descendants))
            {
                var member = part.FeatureByName(name) as Feature;
                if (member != null && member.IsSuppressed()) SuppressedAuxiliary71.Add(name);
            }
            MirrorV7Diagnostics.Log("[AUX71][SUPPRESSED] failed=" + failed.Name + " group=" + string.Join(",", group) +
                " suppressedNow=" + string.Join(",", SuppressedAuxiliary71));
            return true;
        }

        // Second tier, used when the surface chain cannot be re-attached (a
        // surface feature without an editable definition such as Untrim, or an
        // extension whose reflected topology cannot be reproduced). The
        // surface features are then left exactly as in the source, and each
        // Cut With Surface gets a fixed copy of the reflected surface it cut
        // with in the source, inserted just before it. The cut then rebuilds
        // without error and on the mirror image; only the link between the
        // surface features and the cut is given up.
        internal static bool FixedCutToolMode71;
        // An in-context Offset Surface was found in the Part of this run.
        internal static bool SurfaceChainSeen71;
        private static readonly Dictionary<string, Body2> cutTools71 = new Dictionary<string, Body2>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> cutToolImports71 = new Dictionary<string, string>(StringComparer.Ordinal);

        private static MathTransform ReflectionTransform71(MirrorInPlaceExecutionContextV7 context)
        {
            var math = (MathUtility)context.SwApp.GetMathUtility();
            double[] o = context.Reflection.Origin, n = context.Reflection.Normal;
            double d = 2 * (n[0] * o[0] + n[1] * o[1] + n[2] * o[2]);
            return (MathTransform)math.CreateTransform(new[]
            {
                1 - 2 * n[0] * n[0], -2 * n[0] * n[1], -2 * n[0] * n[2],
                -2 * n[1] * n[0], 1 - 2 * n[1] * n[1], -2 * n[1] * n[2],
                -2 * n[2] * n[0], -2 * n[2] * n[1], 1 - 2 * n[2] * n[2],
                d * n[0], d * n[1], d * n[2], 1.0, 0, 0, 0
            });
        }

        internal static bool HasFixedCutTool71(string featureName)
        {
            return FixedCutToolMode71 && cutTools71.ContainsKey(featureName);
        }

        private static void CaptureFixedCutTools71(MirrorInPlaceExecutionContextV7 context, MirrorV7ModelGraph graph)
        {
            ModelDoc2 model = context.WorkingDocument;
            MathTransform reflection = ReflectionTransform71(context);
            bool chain = false;
            foreach (var node in graph.Nodes.Where(n => n.Depth == 0 && !n.IsSuppressed).OrderBy(n => n.TreeOrder))
            {
                if (node.TypeName == "OffsetRefSurface" && node.Feature.ListExternalFileReferencesCount() > 0) { chain = true; SurfaceChainSeen71 = true; }
                if (!chain || node.TypeName != "SurfCut") continue;
                MoveRollbackAfter(model, node.Feature);
                model.EditRebuild3();
                var data = node.Feature.GetDefinition() as ISurfaceCutFeatureData;
                if (data == null || !data.AccessSelections(model, null)) continue;
                Body2 tool = null;
                try
                {
                    object raw = data.SurfaceForCut;
                    tool = raw as Body2;
                    var owner = raw as Feature;
                    if (tool == null && owner != null)
                    {
                        object[] faces = owner.GetFaces() as object[] ?? new object[0];
                        if (faces.Length > 0) tool = ((Face2)faces[0]).GetBody() as Body2;
                    }
                    if (tool != null && tool.GetType() == (int)swBodyType_e.swSheetBody)
                    {
                        var copy = (Body2)tool.Copy();
                        if (copy != null && copy.ApplyTransform(reflection)) cutTools71[node.Name] = copy;
                    }
                }
                finally { data.ReleaseSelectionAccess(); }
                MirrorV7Diagnostics.Log("[SURFCHAIN71][CAPTURE_CUT_TOOL] feature=" + node.Name + " captured=" + cutTools71.ContainsKey(node.Name));
            }
        }

        // Fixed-tool tier: called once the rollback bar is just after a Cut With
        // Surface, before its mapped replay. Inserts the fixed reflected tool
        // surface in front of the cut.
        internal static void PrepareFixedCutTool71(ModelDoc2 model, Feature feature)
        {
            Body2 tool;
            if (!FixedCutToolMode71 || feature == null || !cutTools71.TryGetValue(feature.Name, out tool) || cutToolImports71.ContainsKey(feature.Name)) return;
            string name = feature.Name;
            var part = (PartDoc)model;
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, name))
                throw new InvalidOperationException("SURFCHAIN71 cannot roll back before " + name);
            var imported = part.CreateFeatureFromBody3((Body2)tool.Copy(), false, (int)swCreateFeatureBodyOpts_e.swCreateFeatureBodyCheck) as Feature;
            if (imported == null) throw new InvalidOperationException("SURFCHAIN71 cannot insert the fixed cutting surface before " + name);
            cutToolImports71[name] = imported.Name;
            FrozenSurfaceImports71.Add(imported.Name);
            ADDIN.Helpers.NativeBodyCoincidence67.SurfaceChainResidualAllowed71 = true;
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, name))
                throw new InvalidOperationException("SURFCHAIN71 cannot roll forward after " + name);
            MirrorV7Diagnostics.Log("[SURFCHAIN71][FIXED_CUT_TOOL] cut=" + name + " inserted=" + imported.Name);
        }

        internal static object FixedCutToolBody71(ModelDoc2 model, string cutName)
        {
            string importName;
            var imported = cutToolImports71.TryGetValue(cutName, out importName) ? ((PartDoc)model).FeatureByName(importName) as Feature : null;
            object[] faces = imported == null ? null : imported.GetFaces() as object[];
            if (faces == null || faces.Length == 0) throw new InvalidOperationException("SURFCHAIN71 fixed cutting surface missing for " + cutName);
            return ((Face2)faces[0]).GetBody();
        }

        // Fixed-tool tier: a Delete Body of the source also removes the fixed
        // cutting surfaces inserted so far, so no extra surface body stays behind.
        private static Feature AppendFixedToolsToDelete71(ModelDoc2 model, Feature feature)
        {
            if (feature == null || feature.GetTypeName2() != "DeleteBody" || FrozenSurfaceImports71.Count == 0) return feature;
            string name = feature.Name;
            var part = (PartDoc)model;
            var extra = new List<object>();
            foreach (string importName in FrozenSurfaceImports71)
            {
                var imported = part.FeatureByName(importName) as Feature;
                object[] faces = imported == null ? null : imported.GetFaces() as object[];
                if (faces != null && faces.Length > 0) extra.Add(((Face2)faces[0]).GetBody());
            }
            var data = feature.GetDefinition() as IDeleteBodyFeatureData;
            if (extra.Count == 0 || data == null || data.KeepBodies || !data.AccessSelections(model, null)) return feature;
            bool modified = false;
            try
            {
                object[] current = data.Bodies as object[] ?? new object[0];
                var all = current.ToList();
                foreach (object body in extra) if (!all.Any(b => SameCom28(b, body))) all.Add(body);
                if (all.Count != current.Length)
                {
                    data.Bodies = all.Select(b => new DispatchWrapper(b)).ToArray();
                    modified = feature.ModifyDefinition(data, model, null);
                }
            }
            finally { if (!modified) data.ReleaseSelectionAccess(); }
            feature = part.FeatureByName(name) as Feature;
            bool warning;
            int error = feature.GetErrorCode2(out warning);
            MirrorV7Diagnostics.Log("[SURFCHAIN71][DELETE_FIXED_TOOLS] feature=" + name + " added=" + extra.Count + " modified=" + modified + " error=" + error);
            if (error != 0 && !warning) throw new InvalidOperationException("SURFCHAIN71 Delete Body failed after adding the fixed cutting surfaces: " + name);
            return feature;
        }

        // A fixed surface this run inserted that no Delete Body of the Part removes
        // would be a visible surface body the source does not show: it is hidden.
        // Appearance only; the surface stays in the tree as the reference it is.
        internal static void HideLeftoverFixedSurfaces71(ModelDoc2 model)
        {
            if (FrozenSurfaceImports71.Count == 0) return;
            int hidden = 0;
            foreach (Body2 body in (((PartDoc)model).GetBodies2((int)swBodyType_e.swSheetBody, false) as object[] ?? new object[0]).Cast<Body2>())
            {
                object[] faces = body.GetFaces() as object[];
                var owner = faces == null || faces.Length == 0 ? null : ((Face2)faces[0]).GetFeature() as Feature;
                if (owner == null || !FrozenSurfaceImports71.Contains(owner.Name) || !body.Visible) continue;
                body.HideBody(true);
                hidden++;
            }
            MirrorV7Diagnostics.Log("[SURFCHAIN71][HIDE_LEFTOVER_FIXED] fixedSurfaces=" + FrozenSurfaceImports71.Count + " hidden=" + hidden);
        }

        internal static void CaptureSurfaceChain71(MirrorInPlaceExecutionContextV7 context, MirrorV7ModelGraph graph)
        {
            surfaceChain71.Clear();
            FrozenSurfaceImports71.Clear();
            replacedRoots71.Clear();
            cutTools71.Clear();
            cutToolImports71.Clear();
            SuppressedAuxiliary71.Clear();
            SurfaceChainSeen71 = false;
            ADDIN.Helpers.NativeBodyCoincidence67.SurfaceChainResidualAllowed71 = false;
            ADDIN.Helpers.NativeBodyCoincidence67.LargestAcceptedDeviation71 = 0;
            if (FixedCutToolMode71) { CaptureFixedCutTools71(context, graph); return; }
            ModelDoc2 model = context.WorkingDocument;
            var math = (MathUtility)context.SwApp.GetMathUtility();
            bool chain = false;
            foreach (var node in graph.Nodes.Where(n => n.Depth == 0 && !n.IsSuppressed).OrderBy(n => n.TreeOrder))
            {
                string[] properties;
                if (!surfaceChainProperties71.TryGetValue(node.TypeName, out properties)) continue;
                bool root = node.TypeName == "OffsetRefSurface" && node.Feature.ListExternalFileReferencesCount() > 0;
                if (!root && !chain) continue;
                MoveRollbackAfter(model, node.Feature);
                model.EditRebuild3();
                var recipe = new SurfaceRecipe71();
                if (root)
                {
                    object[] faces = node.Feature.GetFaces() as object[] ?? new object[0];
                    if (faces.Length == 0) throw new InvalidOperationException("SURFCHAIN71 in-context Offset Surface has no surface to copy: " + node.Name);
                    var source = (Body2)((Face2)faces[0]).GetBody();
                    if (faces.Cast<Face2>().Any(f => !SameCom28(f.GetBody(), source)))
                        throw new InvalidOperationException("SURFCHAIN71 in-context Offset Surface spans several bodies: " + node.Name);
                    double[] o = context.Reflection.Origin, n = context.Reflection.Normal;
                    double d = 2 * (n[0] * o[0] + n[1] * o[1] + n[2] * o[2]);
                    var reflection = (MathTransform)math.CreateTransform(new[]
                    {
                        1 - 2 * n[0] * n[0], -2 * n[0] * n[1], -2 * n[0] * n[2],
                        -2 * n[1] * n[0], 1 - 2 * n[1] * n[1], -2 * n[1] * n[2],
                        -2 * n[2] * n[0], -2 * n[2] * n[1], 1 - 2 * n[2] * n[2],
                        d * n[0], d * n[1], d * n[2], 1.0, 0, 0, 0
                    });
                    recipe.Frozen = (Body2)source.Copy();
                    if (recipe.Frozen == null || !recipe.Frozen.ApplyTransform(reflection))
                        throw new InvalidOperationException("SURFCHAIN71 cannot reflect the frozen surface of " + node.Name);
                    // No Negate(): natively the reflected sheet already offsets to
                    // the mirrored side; negating it sent the next offset the wrong way.
                    recipe.SourceDistance = ((ISurfaceOffsetFeatureData)node.Feature.GetDefinition()).Distance;
                    recipe.Points = ((recipe.Frozen.GetVertices() as object[]) ?? new object[0]).Cast<Vertex>().Select(v => (double[])v.GetPoint()).ToArray();
                    recipe.Area = BodyArea71(recipe.Frozen);
                    chain = true;
                    SurfaceChainSeen71 = true;
                    surfaceChain71[node.Name] = recipe;
                    MirrorV7Diagnostics.Log("[SURFCHAIN71][CAPTURE_ROOT] feature=" + node.Name + " externalReferences=" +
                        node.Feature.ListExternalFileReferencesCount() + " sourceDistance_m=" + recipe.SourceDistance.ToString("R") + " faces=" + faces.Length);
                    continue;
                }
                // Reflected result of this feature in the source, for the replay diagnostic.
                object[] resultFaces = node.Feature.GetFaces() as object[] ?? new object[0];
                if (resultFaces.Length > 0)
                {
                    var resultBody = (Body2)((Face2)resultFaces[0]).GetBody();
                    recipe.Points = ((resultBody.GetVertices() as object[]) ?? new object[0]).Cast<Vertex>()
                        .Select(v => context.Reflection.ReflectPoint((double[])v.GetPoint())).ToArray();
                    recipe.Area = BodyArea71(resultBody);
                }
                object data = node.Feature.GetDefinition();
                Type type = SurfaceChainType71(data);
                if (type == null || !SurfaceChainAccess71(data, model)) { MirrorV7Diagnostics.Log("[SURFCHAIN71][CAPTURE] feature=" + node.Name + " definition=UNAVAILABLE"); continue; }
                string summary = "";
                try
                {
                    foreach (string name in properties)
                    {
                        var property = type.GetProperty(name);
                        object raw = property.GetValue(data, null);
                        if (raw == null) continue;
                        bool array = raw is Array;
                        object[] values = array ? ((Array)raw).Cast<object>().ToArray() : new[] { raw };
                        if (values.Length == 0) continue;
                        var maps = values.Select(v => v == null ? null : ChainMap71(context, v)).ToList();
                        summary += " " + name + "=" + values.Length;
                        if (maps.Any(m => m == null))
                            throw new InvalidOperationException("SURFCHAIN71 reference kind not supported: " + node.Name + "." + name);
                        string featureName = node.Name;
                        recipe.Setters.Add((definition, target) =>
                        {
                            object[] mapped = maps.Select(map => map(target)).ToArray();
                            // Two source faces can now be one merged face: list it once.
                            var distinct = new List<object>();
                            foreach (object item in mapped) if (!distinct.Any(d => SameCom28(d, item))) distinct.Add(item);
                            mapped = distinct.ToArray();
                            property.SetValue(definition, array ? (object)mapped.Select(m => new DispatchWrapper(m)).ToArray() : mapped[0], null);
                            MirrorV7Diagnostics.Log("[SURFCHAIN71][MAP_PROPERTY] feature=" + featureName + " property=" + name + " count=" + mapped.Length);
                        });
                    }
                }
                finally { SurfaceChainRelease71(data); }
                if (recipe.Setters.Count > 0) surfaceChain71[node.Name] = recipe;
                MirrorV7Diagnostics.Log("[SURFCHAIN71][CAPTURE] feature=" + node.Name + " type=" + node.TypeName + " properties:" + summary);
            }
        }

        private static bool SameFrozenResult71(Feature feature, SurfaceRecipe71 recipe)
        {
            object[] faces = feature.GetFaces() as object[] ?? new object[0];
            if (faces.Length == 0) return false;
            var body = (Body2)((Face2)faces[0]).GetBody();
            var points = ((body.GetVertices() as object[]) ?? new object[0]).Cast<Vertex>().Select(v => (double[])v.GetPoint()).ToList();
            if (points.Count != recipe.Points.Length || Math.Abs(BodyArea71(body) - recipe.Area) > Math.Max(1e-12, recipe.Area * 1e-4)) return false;
            return recipe.Points.All(p => points.Any(q => Math.Abs(p[0] - q[0]) <= 1e-7 && Math.Abs(p[1] - q[1]) <= 1e-7 && Math.Abs(p[2] - q[2]) <= 1e-7));
        }

        // The in-context Offset Surface keeps its own distance and options. Its
        // lost source face is replaced by a fixed surface: the reflected result
        // moved back by that distance. Which side "back" is, is not assumed:
        // both are tried and the one whose offset lands on the reflected result
        // (vertices within 1e-7 m, same area) is kept.
        private static Feature ReattachFrozenRoot71(ModelDoc2 model, string name, SurfaceRecipe71 recipe)
        {
            var part = (PartDoc)model;
            var candidates = new List<Body2>();
            if (Math.Abs(recipe.SourceDistance) <= 1e-12) candidates.Add((Body2)recipe.Frozen.Copy());
            else foreach (bool flip in new[] { false, true })
            {
                var moved = ((Body2)recipe.Frozen.Copy()).MakeOffset(recipe.SourceDistance, flip) as Body2;
                if (moved != null) candidates.Add(moved);
            }
            var rejected = new List<string>();
            for (int attempt = 0; attempt < candidates.Count; attempt++)
            {
                if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, name))
                    throw new InvalidOperationException("SURFCHAIN71 cannot roll back before " + name);
                var imported = part.CreateFeatureFromBody3(candidates[attempt], false, (int)swCreateFeatureBodyOpts_e.swCreateFeatureBodyCheck) as Feature;
                if (imported == null) { MirrorV7Diagnostics.Log("[SURFCHAIN71][FIXED_SURFACE] attempt=" + attempt + " insert=FAILED"); continue; }
                string importedName = imported.Name;
                if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, name))
                    throw new InvalidOperationException("SURFCHAIN71 cannot roll forward after " + name);
                imported = part.FeatureByName(importedName) as Feature;
                object[] localFaces = imported == null ? null : imported.GetFaces() as object[];
                var feature = part.FeatureByName(name) as Feature;
                var data = feature == null ? null : feature.GetDefinition() as ISurfaceOffsetFeatureData;
                if (localFaces == null || localFaces.Length == 0 || data == null || !data.AccessSelections(model, null))
                    throw new InvalidOperationException("SURFCHAIN71 cannot access " + name);
                bool modified = false;
                try
                {
                    data.Entities = localFaces.Select(f => new DispatchWrapper(f)).ToArray();
                    modified = feature.ModifyDefinition(data, model, null);
                }
                finally { if (!modified) data.ReleaseSelectionAccess(); }
                feature = part.FeatureByName(name) as Feature;
                bool warning;
                int error = feature.GetErrorCode2(out warning);
                bool same = modified && error == 0 && SameFrozenResult71(feature, recipe);
                MirrorV7Diagnostics.Log("[SURFCHAIN71][FIXED_SURFACE] feature=" + name + " attempt=" + attempt + " inserted=" + importedName +
                    " distance_m=" + recipe.SourceDistance.ToString("R") + " modified=" + modified + " error=" + error +
                    " landsOnReflectedResult=" + same + " externalReferences=" + feature.ListExternalFileReferencesCount());
                if (same)
                {
                    // A rejected fixed surface is removed only now: deleting it
                    // while the Offset Surface still pointed at it made the
                    // feature definition inaccessible natively.
                    foreach (string rejectedName in rejected)
                    {
                        var stale = part.FeatureByName(rejectedName) as Feature;
                        model.ClearSelection2(true);
                        if (stale == null || !stale.Select2(false, 0) || !model.Extension.DeleteSelection2(0))
                            throw new InvalidOperationException("SURFCHAIN71 cannot remove a rejected fixed surface: " + rejectedName);
                    }
                    model.ClearSelection2(true);
                    FrozenSurfaceImports71.Add(importedName);
                    ADDIN.Helpers.NativeBodyCoincidence67.SurfaceChainResidualAllowed71 = true;
                    return part.FeatureByName(name) as Feature;
                }
                rejected.Add(importedName);
            }
            throw new InvalidOperationException("SURFCHAIN71 no fixed source surface reproduces the reflected result of " + name);
        }

        private static int ResultVertexCount71(Feature feature)
        {
            object[] faces = feature.GetFaces() as object[] ?? new object[0];
            return faces.Length == 0 ? -1 : ((Body2)((Face2)faces[0]).GetBody()).GetVertexCount();
        }

        // Surface-Extend builds the corner between two extended edges from the
        // order of its edges. On the reflected surface the source order left
        // one corner unmerged (one vertex more than the reflected source
        // surface, 12 against 11 natively), which later cut a micrometre
        // sliver off the solid. The same edges are therefore tried in their
        // other orders until the extended surface has the source's topology.
        // Nothing is assumed: the order is kept only if the vertex count matches.
        private static Feature MatchExtendCorners71(ModelDoc2 model, Feature feature, SurfaceRecipe71 recipe)
        {
            if (recipe.Points == null || feature.GetTypeName2() != "ExtendRefSurface" || ResultVertexCount71(feature) == recipe.Points.Length) return feature;
            string name = feature.Name;
            var part = (PartDoc)model;
            var first = feature.GetDefinition() as ISurfaceExtendFeatureData;
            if (first == null || !first.AccessSelections(model, null)) return feature;
            object[] items;
            try { items = first.Items as object[] ?? new object[0]; }
            finally { first.ReleaseSelectionAccess(); }
            if (items.Length < 2 || items.Length > 4) return feature;
            var orders = new List<int[]>();
            Action<List<int>> permute = null;
            permute = prefix =>
            {
                if (prefix.Count == items.Length) { orders.Add(prefix.ToArray()); return; }
                for (int i = 0; i < items.Length; i++) if (!prefix.Contains(i)) { var next = new List<int>(prefix) { i }; permute(next); }
            };
            permute(new List<int>());
            Func<int[], int> apply = order =>
            {
                var data = feature.GetDefinition() as ISurfaceExtendFeatureData;
                if (data == null || !data.AccessSelections(model, null)) return -1;
                bool modified = false;
                try
                {
                    data.Items = order.Select(i => new DispatchWrapper(items[i])).ToArray();
                    modified = feature.ModifyDefinition(data, model, null);
                }
                finally { if (!modified) data.ReleaseSelectionAccess(); }
                feature = part.FeatureByName(name) as Feature;
                bool warning;
                int count = ResultVertexCount71(feature), error = feature.GetErrorCode2(out warning);
                MirrorV7Diagnostics.Log("[SURFCHAIN71][EXTEND_ORDER] feature=" + name + " order=" + string.Join(",", order) + " modified=" + modified +
                    " error=" + error + " vertices=" + count + " source=" + recipe.Points.Length);
                return modified && error == 0 ? count : -1;
            };
            // No order reproduced the source topology natively (10 or 12 against
            // 11). The order with the most merged corners (fewest vertices) is
            // then kept: an unmerged corner is what cut the sliver.
            int[] best = orders[0]; int bestCount = ResultVertexCount71(feature);
            foreach (int[] order in orders.Skip(1))
            {
                int count = apply(order);
                if (count == recipe.Points.Length) return feature;
                if (count > 0 && count < bestCount) { best = order; bestCount = count; }
            }
            apply(best);
            return feature;
        }
        // Called for an inherited (no solid change) feature once the rollback
        // bar is just after it. Returns the live feature object.
        internal static Feature ReplaySurfaceChain71(ModelDoc2 model, Feature feature)
        {
            SurfaceRecipe71 recipe;
            if (FixedCutToolMode71) return AppendFixedToolsToDelete71(model, feature);
            if (feature == null || !surfaceChain71.TryGetValue(feature.Name, out recipe)) return feature;
            string name = feature.Name;
            var part = (PartDoc)model;
            if (recipe.Frozen != null) return ReattachFrozenRoot71(model, name, recipe);
            feature = part.FeatureByName(name) as Feature;
            object data = feature == null ? null : feature.GetDefinition();
            if (data == null || !SurfaceChainAccess71(data, model)) throw new InvalidOperationException("SURFCHAIN71 cannot access " + name);
            bool modified = false;
            try
            {
                foreach (var setter in recipe.Setters) setter(data, model);
                modified = feature.ModifyDefinition(data, model, null);
            }
            finally { if (!modified) SurfaceChainRelease71(data); }
            feature = part.FeatureByName(name) as Feature;
            if (modified) feature = MatchExtendCorners71(model, feature, recipe);
            bool warning;
            int error = feature.GetErrorCode2(out warning);
            string placement = "";
            object[] resultFaces = feature.GetFaces() as object[] ?? new object[0];
            if (recipe.Points != null && resultFaces.Length > 0)
            {
                var body = (Body2)((Face2)resultFaces[0]).GetBody();
                var actual = ((body.GetVertices() as object[]) ?? new object[0]).Cast<Vertex>().Select(v => (double[])v.GetPoint()).ToList();
                double farthest = recipe.Points.Length == 0 || actual.Count == 0 ? double.NaN : recipe.Points.Max(p => actual.Min(q =>
                    Math.Sqrt((p[0] - q[0]) * (p[0] - q[0]) + (p[1] - q[1]) * (p[1] - q[1]) + (p[2] - q[2]) * (p[2] - q[2]))));
                placement = " resultVertices=" + recipe.Points.Length + "/" + actual.Count + " farthestVertex_m=" + farthest.ToString("E3") +
                    " areaDelta_m2=" + (BodyArea71(body) - recipe.Area).ToString("E3");
            }
            MirrorV7Diagnostics.Log("[SURFCHAIN71][REATTACH] feature=" + name + " modified=" + modified + " error=" + error + placement);
            if (!modified || (error != 0 && !warning))
                throw new InvalidOperationException("SURFCHAIN71 could not re-attach " + name + " at the reflected position. error=" + error);
            return AppendFixedToolsToDelete71(model, feature);
        }
    }
}