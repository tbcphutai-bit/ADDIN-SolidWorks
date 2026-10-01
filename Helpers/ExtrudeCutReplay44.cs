using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands;
using ADDIN.Commands.MirrorV7;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Helpers
{
    public sealed class ExtrudeCutRecipe44
    {
        public double[] SketchNormal;
        public int FromType;
        public double FromOffset;
        public bool NormalCut, OptimizeGeometry, FeatureScope, AutoSelect;
        public ExtrudeDirectionAudit50 FromReference, DirectionReference1, DirectionReference2;
        public double[] SourceDirection;
        public byte[][] ScopeBodies;
        public DirectionOptions44 D1, D2;
        public bool MissingSurfaceReference54;
        public bool Applied;
    }
    public sealed class DirectionOptions44
    {
        public bool Draft, DraftOutward, ReverseOffset, TranslateSurface;
        public double DraftAngle;
    }

    // Pure geometry: independent of file names, feature names and the sketch baseline.
    public static class CutOrientation44
    {
        public static double[] Unit(double[] v)
        {
            if (v == null || v.Length < 3) throw new InvalidOperationException("CUT44: missing direction vector.");
            double n = Math.Sqrt(v[0]*v[0] + v[1]*v[1] + v[2]*v[2]);
            if (double.IsNaN(n) || double.IsInfinity(n) || n < 1e-12)
                throw new InvalidOperationException("CUT44: degenerate direction vector.");
            return new[] { v[0]/n, v[1]/n, v[2]/n };
        }
        public static double[] Reflect(double[] v, double[] planeNormal)
        {
            double[] n = Unit(planeNormal), d = Unit(v);
            double dot = d[0]*n[0] + d[1]*n[1] + d[2]*n[2];
            return new[] { d[0]-2*dot*n[0], d[1]-2*dot*n[1], d[2]-2*dot*n[2] };
        }
        public static bool Opposed(double[] expected, double[] actual)
        {
            double[] a = Unit(expected), b = Unit(actual);
            double dot = a[0]*b[0] + a[1]*b[1] + a[2]*b[2];
            if (Math.Abs(dot) < 1.0-1e-8)
                throw new InvalidOperationException("CUT44: mapped direction is not parallel to reflected source. dot=" + dot.ToString("R"));
            return dot < 0;
        }
    }

    public static class CutBlindFallback54
    {
        public static bool CanQualify(int fromType, bool bothDirections, int endCondition,
            bool endReferencePresent)
        {
            return fromType == (int)swExtrudeFrom_e.swExtrudeFrom_SketchPlane &&
                !bothDirections &&
                endCondition == (int)swEndConditions_e.swEndCondUpToSurface &&
                !endReferencePresent;
        }

        public static double[] SignedSpanFromReflectedVertices(double[] origin, double[] normal,
            IEnumerable<double[]> vertices)
        {
            if (origin == null || normal == null || vertices == null ||
                origin.Length < 3 || normal.Length < 3)
                throw new InvalidOperationException("CUT54: sketch plane or removed vertices are unavailable.");
            double[] unit = CutOrientation44.Unit(normal);
            double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
            int count = 0;
            foreach (double[] point in vertices)
            {
                if (point == null || point.Length < 3 || point.Take(3).Any(value =>
                    double.IsNaN(value) || double.IsInfinity(value)))
                    throw new InvalidOperationException("CUT54: removed-body vertex is non-finite.");
                double signedDistance = (point[0] - origin[0]) * unit[0] +
                    (point[1] - origin[1]) * unit[1] +
                    (point[2] - origin[2]) * unit[2];
                minimum = Math.Min(minimum, signedDistance);
                maximum = Math.Max(maximum, signedDistance);
                count++;
            }
            if (count == 0 || double.IsNaN(minimum) || double.IsInfinity(minimum) ||
                double.IsNaN(maximum) || double.IsInfinity(maximum))
                throw new InvalidOperationException("CUT54: no finite removed-body vertex span.");
            return new[] { minimum, maximum };
        }

        public static double ExactDepthFromSignedSpan(double[] span)
        {
            if (span == null || span.Length < 2 ||
                span.Any(value => double.IsNaN(value) || double.IsInfinity(value)) ||
                span[0] > span[1])
                throw new InvalidOperationException("CUT54: signed removed-body span is invalid.");
            // A single-direction Blind cut cannot reproduce removed material on
            // both sides of the sketch plane. A geometric oracle remains required.
            if (span[0] < -1e-7 && span[1] > 1e-7)
                throw new InvalidOperationException("CUT54: removed material crosses both sides of the sketch plane.");
            double depth = Math.Max(Math.Abs(span[0]), Math.Abs(span[1]));
            if (depth <= 1e-9)
                throw new InvalidOperationException("CUT54: removed-body depth is too small to replay safely.");
            return depth;
        }
    }

    public static partial class SketchOperationsHelper
    {
        private static double[] SketchNormal44(Sketch sketch)
        {
            if (sketch == null) throw new InvalidOperationException("CUT44: driving sketch missing.");
            var math = (IMathUtility)SwAddin.InstanceSwApp.GetMathUtility();
            var vector = (MathVector)math.CreateVector(new[] { 0.0, 0.0, 1.0 });
            return CutOrientation44.Unit(((MathVector)vector.MultiplyTransform(
                sketch.ModelToSketchTransform.IInverse())).ArrayData as double[]);
        }

        private static ExtrudeDirectionAudit50 CaptureReference44(ModelDoc2 model, object entity, int type)
        {
            var r = new ExtrudeDirectionAudit50 { ReferencePresent = entity != null,
                ReferenceEntityKind = EndReferenceKind50(entity), ReferenceSelectionType = type };
            if (entity == null) return r;
            var vertex = entity as Vertex;
            var point = entity as SketchPoint;
            var edge = entity as Edge;
            var face = entity as Face2;
            if (vertex != null) r.ReferencePoint = vertex.GetPoint() as double[];
            else if (point != null) r.ReferencePoint = SketchPointWorld50(point);
            else if (edge != null) r.ReferenceEdgeSamples = SampleEdge24(edge);
            else if (face != null) r.ReferenceFace = CaptureFaceSupport24(face);
            else throw new InvalidOperationException("CUT44: unsupported reference kind=" + r.ReferenceEntityKind +
                "; no silent substitution of the original option.");
            return r;
        }

        private static double[] DirectionVector44(object first, object second, double[] defaultNormal)
        {
            if (first == null && second == null) return defaultNormal;
            Func<object, double[]> point = obj => obj is Vertex ? ((Vertex)obj).GetPoint() as double[] :
                obj is SketchPoint ? SketchPointWorld50((SketchPoint)obj) : null;
            if (second != null)
            {
                double[] a = point(first), b = point(second);
                if (a == null || b == null) throw new InvalidOperationException("CUT44: two-point direction requires points/vertices.");
                return CutOrientation44.Unit(new[] { b[0]-a[0], b[1]-a[1], b[2]-a[2] });
            }
            var edge = first as Edge;
            if (edge != null && ((Curve)edge.GetCurve()).IsLine())
            {
                double[] a = ((Vertex)edge.GetStartVertex()).GetPoint() as double[];
                double[] b = ((Vertex)edge.GetEndVertex()).GetPoint() as double[];
                return CutOrientation44.Unit(new[] { b[0]-a[0], b[1]-a[1], b[2]-a[2] });
            }
            var face = first as Face2;
            if (face != null && ((Surface)face.GetSurface()).IsPlane())
                return CutOrientation44.Unit(face.Normal as double[]);
            throw new InvalidOperationException("CUT44: explicit direction has no supported geometric vector; replay stopped.");
        }

        private static DirectionOptions44 CaptureDirectionOptions44(IExtrudeFeatureData2 d, bool forward, int end)
        {
            var value = new DirectionOptions44 { Draft = d.GetDraftWhileExtruding(forward) };
            if (value.Draft) { value.DraftAngle = d.GetDraftAngle(forward); value.DraftOutward = d.GetDraftOutward(forward); }
            if (end == (int)swEndConditions_e.swEndCondOffsetFromSurface)
            { value.ReverseOffset = d.GetReverseOffset(forward); value.TranslateSurface = d.GetTranslateSurface(forward); }
            return value;
        }

        private static bool NeedsEndReference44(int end)
        {
            return end == (int)swEndConditions_e.swEndCondUpToVertex ||
                end == (int)swEndConditions_e.swEndCondUpToSurface ||
                end == (int)swEndConditions_e.swEndCondOffsetFromSurface ||
                end == (int)swEndConditions_e.swEndCondUpToBody ||
                end == (int)swEndConditions_e.swEndCondUpToSelection;
        }

        private static ExtrudeCutRecipe44 CaptureCutRecipe44(ModelDoc2 model, IExtrudeFeatureData2 d,
            Sketch sketch, CutAuditSnapshot21 audit)
        {
            if (d.IsThinFeature())
                throw new InvalidOperationException("CUT44: thin-wall side mapping is not implemented; source option is not replaced.");
            if (audit.D1_50 == null || audit.D1_50.CaptureError != null ||
                (audit.BothDirections50 && (audit.D2_50 == null || audit.D2_50.CaptureError != null)))
                throw new InvalidOperationException("CUT44: active direction options were not completely captured.");
            bool missingSurface54 = CutBlindFallback54.CanQualify(d.FromType,
                audit.BothDirections50, audit.D1_50.EndCondition, audit.D1_50.ReferencePresent);
            foreach (var direction in audit.BothDirections50 ? new[] { audit.D1_50, audit.D2_50 } : new[] { audit.D1_50 })
            {
                if (!Enum.IsDefined(typeof(swEndConditions_e), direction.EndCondition) ||
                    (NeedsEndReference44(direction.EndCondition) && !direction.ReferencePresent &&
                        !(missingSurface54 && direction.Forward)))
                    throw new InvalidOperationException("CUT44: unknown end condition or required reference is missing: " + direction.EndConditionName);
                // The legacy audit tolerates missing depth; a replay recipe cannot.
                if (direction.EndCondition == (int)swEndConditions_e.swEndCondBlind ||
                    direction.EndCondition == (int)swEndConditions_e.swEndCondMidPlane ||
                    direction.EndCondition == (int)swEndConditions_e.swEndCondOffsetFromSurface)
                {
                    direction.Depth = d.GetDepth(direction.Forward);
                    if (double.IsNaN(direction.Depth) || double.IsInfinity(direction.Depth))
                        throw new InvalidOperationException("CUT44: non-finite extrusion depth.");
                }
            }
            var r = new ExtrudeCutRecipe44 { SketchNormal = SketchNormal44(sketch), FromType = d.FromType,
                MissingSurfaceReference54 = missingSurface54,
                NormalCut = d.NormalCut, OptimizeGeometry = d.OptimizeGeometry,
                FeatureScope = d.FeatureScope, AutoSelect = d.AutoSelect };
            if (r.FromType == (int)swExtrudeFrom_e.swExtrudeFrom_Offset) r.FromOffset = d.FromOffsetDistance;
            object from; int fromType;
            if (r.FromType == (int)swExtrudeFrom_e.swExtrudeFrom_SurfaceFacePlane ||
                r.FromType == (int)swExtrudeFrom_e.swExtrudeFrom_Vertex)
            { d.GetFromEntity(out from, out fromType); r.FromReference = CaptureReference44(model, from, fromType);
              if (!r.FromReference.ReferencePresent) throw new InvalidOperationException("CUT44: From reference missing."); }
            object dir1, dir2; int type1, type2;
            int directionCount = d.GetDirectionReference(out dir1, out type1, out dir2, out type2);
            // SOLIDWORKS can return a non-positive count for the implicit sketch-normal
            // direction. The out-entities are authoritative: no entities means no
            // reference to reparent, while a positive count without its entities is
            // genuinely incomplete and must fail closed.
            CreateMirrorPartPackage.LogDebug("[CUT44][DIRECTION_REFERENCE_CAPTURE] count=" + directionCount +
                " ref1=" + EndReferenceKind50(dir1) + " type1=" + type1 +
                " ref2=" + EndReferenceKind50(dir2) + " type2=" + type2);
            if (directionCount > 2 ||
                (dir1 == null && dir2 != null) ||
                (directionCount > 0 && dir1 == null) ||
                (directionCount == 1 && dir2 != null) ||
                (directionCount == 2 && dir2 == null) ||
                (directionCount <= 0 && (dir1 != null || dir2 != null)))
                throw new InvalidOperationException("CUT44: inconsistent extrusion direction reference count=" +
                    directionCount + " ref1=" + EndReferenceKind50(dir1) +
                    " ref2=" + EndReferenceKind50(dir2) + ".");
            if (missingSurface54 && (dir1 != null || dir2 != null))
                throw new InvalidOperationException("CUT54: missing surface fallback requires the implicit sketch-normal direction.");
            r.DirectionReference1 = CaptureReference44(model, dir1, type1);
            r.DirectionReference2 = CaptureReference44(model, dir2, type2);
            r.SourceDirection = DirectionVector44(dir1, dir2, r.SketchNormal);
            r.D1 = CaptureDirectionOptions44(d, true, audit.D1_50.EndCondition);
            if (audit.BothDirections50) r.D2 = CaptureDirectionOptions44(d, false, audit.D2_50.EndCondition);
            if (r.FeatureScope && !r.AutoSelect)
            {
                Array bodies = d.FeatureScopeBodies as Array;
                if (bodies == null || bodies.Length == 0) throw new InvalidOperationException("CUT44: explicit feature scope is empty.");
                r.ScopeBodies = bodies.Cast<object>().Select(body => model.Extension.GetPersistReference3(body) as byte[]).ToArray();
                if (r.ScopeBodies.Any(id => id == null || id.Length == 0))
                    throw new InvalidOperationException("CUT44: body scope identity unavailable.");
            }
            CreateMirrorPartPackage.LogDebug("[CUT44][CAPTURE] from=" + r.FromType + " D1=" + audit.D1_50.EndConditionName +
                " D2Enabled=" + audit.BothDirections50 + " normalCut=" + r.NormalCut +
                " explicitScope=" + (r.ScopeBodies != null) +
                " sourceScopeCount=" + (r.ScopeBodies == null ? 0 : r.ScopeBodies.Length));
            if (missingSurface54)
                CreateMirrorPartPackage.LogDebug("[CUT54][SOURCE_REFERENCE_MISSING] end=UpToSurface selectionType=" +
                    audit.D1_50.ReferenceSelectionType + " action=GEOMETRY_GATED_BLIND_CANDIDATES sourceOptionPreservedInAudit=True");
            return r;
        }

        private static object ResolveReference44(ModelDoc2 model, ExtrudeDirectionAudit50 reference, PlaneData plane)
        {
            if (reference == null || !reference.ReferencePresent) return null;
            if (reference.ReferenceEntityKind == "Body" && reference.PersistentReference != null)
            {
                int state;
                object body = model.Extension.GetObjectByPersistReference3(reference.PersistentReference, out state);
                if (!(body is Body2) || state != 0) throw new InvalidOperationException("CUT44: end body identity mapping failed.");
                return body;
            }
            return ResolveEndReference50(model, reference, plane);
        }

        private static void CheckReadback44<T>(List<string> differences, string name, T expected, Func<T> read)
        {
            try
            {
                T actual = read();
                if (!EqualityComparer<T>.Default.Equals(expected, actual))
                    differences.Add(name + " expected=" + expected + " actual=" + actual);
            }
            catch (Exception ex) { differences.Add(name + " readError=" + ex.GetType().Name + ":" + ex.Message); }
        }

        private static void CheckReadbackDouble44(List<string> differences, string name,
            double expected, Func<double> read, double tolerance)
        {
            try
            {
                double actual = read();
                if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(actual - expected) > tolerance)
                    differences.Add(name + " expected=" + expected.ToString("R") + " actual=" + actual.ToString("R"));
            }
            catch (Exception ex) { differences.Add(name + " readError=" + ex.GetType().Name + ":" + ex.Message); }
        }

        private static void CheckDirectionReadback44(List<string> differences, IExtrudeFeatureData2 read,
            ExtrudeDirectionAudit50 source, DirectionOptions44 options, double? fallbackDepth54 = null)
        {
            bool forward = source.Forward;
            string prefix = forward ? "D1." : "D2.";
            int expectedEnd = fallbackDepth54.HasValue ? (int)swEndConditions_e.swEndCondBlind : source.EndCondition;
            CheckReadback44(differences, prefix + "EndCondition", expectedEnd, () => read.GetEndCondition(forward));
            CheckReadback44(differences, prefix + "Draft", options.Draft, () => read.GetDraftWhileExtruding(forward));
            if (fallbackDepth54.HasValue)
                CheckReadbackDouble44(differences, prefix + "FallbackDepth54", fallbackDepth54.Value,
                    () => read.GetDepth(forward), 1e-9);
            else if (source.EndCondition == (int)swEndConditions_e.swEndCondBlind ||
                source.EndCondition == (int)swEndConditions_e.swEndCondMidPlane ||
                source.EndCondition == (int)swEndConditions_e.swEndCondOffsetFromSurface)
                CheckReadbackDouble44(differences, prefix + "Depth", source.Depth, () => read.GetDepth(forward), 1e-9);
            if (options.Draft)
            {
                CheckReadbackDouble44(differences, prefix + "DraftAngle", options.DraftAngle,
                    () => read.GetDraftAngle(forward), 1e-10);
                CheckReadback44(differences, prefix + "DraftOutward", options.DraftOutward,
                    () => read.GetDraftOutward(forward));
            }
            if (source.EndCondition == (int)swEndConditions_e.swEndCondOffsetFromSurface)
            {
                CheckReadback44(differences, prefix + "ReverseOffset", options.ReverseOffset,
                    () => read.GetReverseOffset(forward));
                CheckReadback44(differences, prefix + "TranslateSurface", options.TranslateSurface,
                    () => read.GetTranslateSurface(forward));
            }
        }

        private static void ApplyDirection44(ModelDoc2 model, IExtrudeFeatureData2 d,
            ExtrudeDirectionAudit50 source, DirectionOptions44 options, PlaneData plane,
            double? fallbackDepth54 = null)
        {
            bool forward = source.Forward;
            d.SetEndCondition(forward, fallbackDepth54.HasValue
                ? (int)swEndConditions_e.swEndCondBlind : source.EndCondition);
            if (fallbackDepth54.HasValue)
                d.SetDepth(forward, fallbackDepth54.Value);
            else if (source.EndCondition == (int)swEndConditions_e.swEndCondBlind ||
                source.EndCondition == (int)swEndConditions_e.swEndCondMidPlane ||
                source.EndCondition == (int)swEndConditions_e.swEndCondOffsetFromSurface)
                d.SetDepth(forward, source.Depth);
            if (!fallbackDepth54.HasValue && NeedsEndReference44(source.EndCondition))
            {
                object target = ResolveReference44(model, source, plane);
                if (target == null) throw new InvalidOperationException("CUT44: mapped end reference is missing.");
                d.SetEndConditionReference(forward, target);
                int type; object actual = d.GetEndConditionReference(forward, out type);
                if (type != source.ReferenceSelectionType || !SingleSketchTargetBuilderV7.SameComObject(target, actual))
                    throw new InvalidOperationException("CUT44: end reference identity/type readback differs.");
                CreateMirrorPartPackage.LogDebug("[CUT44][END_BOUND] direction=" + (forward ? "D1" : "D2") +
                    " end=" + source.EndConditionName + " kind=" + source.ReferenceEntityKind + " identity=True");
            }
            if (source.EndCondition == (int)swEndConditions_e.swEndCondOffsetFromSurface)
            { d.SetReverseOffset(forward, options.ReverseOffset); d.SetTranslateSurface(forward, options.TranslateSurface); }
            d.SetDraftWhileExtruding(forward, options.Draft);
            if (options.Draft) { d.SetDraftAngle(forward, options.DraftAngle); d.SetDraftOutward(forward, options.DraftOutward); }
        }

        private static object[] ResolveScopeBodies56(ModelDoc2 model, Feature feature,
            CutAuditSnapshot21 audit, byte[][] scopeReferences)
        {
            var resolved = new object[scopeReferences.Length];
            string failure = null;
            for (int i = 0; i < scopeReferences.Length; i++)
            {
                int state = -1;
                object body = null;
                try { body = model.Extension.GetObjectByPersistReference3(scopeReferences[i], out state); }
                catch (Exception ex) { failure = "index=" + i + " exception=" + ex.GetType().Name; }
                if (failure == null && (!(body is Body2) || state != 0))
                    failure = "index=" + i + " state=" + state + " type=" +
                        (body == null ? "null" : body.GetType().Name);
                if (failure != null) break;
                resolved[i] = body;
            }
            if (failure == null) return resolved;

            // Persistent body IDs can become obsolete after upstream in-place
            // feature replay. With exactly one explicitly scoped source body and
            // one live solid, the mapping is unique, provided the reflected
            // before-cut body was already proven equal by CUT23. Never broaden
            // a subset scope on a multi-body part.
            object[] live = ((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[]
                ?? new object[0];
            bool unique = audit != null && audit.BeforeChainVerified23 &&
                scopeReferences.Length == 1 && live.Length == 1 && live[0] is Body2;
            CreateMirrorPartPackage.LogDebug("[SCOPE56][RESOLVE] feature=" + feature.Name +
                " persistent=" + failure + " sourceScopeCount=" + scopeReferences.Length +
                " liveSolidCount=" + live.Length + " cut23Verified=" +
                (audit != null && audit.BeforeChainVerified23) +
                " result=" + (unique ? "UNIQUE_SINGLE_SOLID" : "FAIL_CLOSED"));
            if (!unique)
                throw new InvalidOperationException("CUT44: body scope mapping failed; " + failure +
                    "; sourceScopeCount=" + scopeReferences.Length + " liveSolidCount=" + live.Length +
                    "; not expanding to all bodies.");
            return new[] { live[0] };
        }

        public static void ApplyReflectedCut44(ModelDoc2 model, Feature feature, Feature profile,
            CutAuditSnapshot21 audit, PlaneData plane, double? fallbackDepth54 = null,
            bool? reverseOverride54 = null)
        {
            if (audit == null || audit.Recipe44 == null || !string.IsNullOrEmpty(audit.CaptureError))
                throw new InvalidOperationException("CUT44: complete source recipe required. " + (audit == null ? "" : audit.CaptureError));
            var recipe = audit.Recipe44;
            recipe.Applied = false;
            if (recipe.MissingSurfaceReference54 != fallbackDepth54.HasValue ||
                (fallbackDepth54.HasValue && (double.IsNaN(fallbackDepth54.Value) ||
                    double.IsInfinity(fallbackDepth54.Value) || fallbackDepth54.Value <= 0.0 ||
                    !reverseOverride54.HasValue)))
                throw new InvalidOperationException("CUT54: missing surface reference needs a finite, verified candidate depth and direction.");
            if (model.SketchManager.ActiveSketch != null)
                throw new InvalidOperationException("CUT44: finish sketch mutation before editing feature definition.");
            double[] targetNormal = SketchNormal44(profile.GetSpecificFeature2() as Sketch);
            bool normalOpposed = CutOrientation44.Opposed(CutOrientation44.Reflect(recipe.SketchNormal, plane.Normal), targetNormal);
            bool access = false, committed = false;
            IExtrudeFeatureData2 d = feature.GetDefinition() as IExtrudeFeatureData2;
            if (d == null) throw new InvalidOperationException("CUT44: extrusion definition unavailable.");
            try
            {
                access = d.AccessSelections(model, null);
                if (!access) throw new InvalidOperationException("CUT44: AccessSelections failed.");
                object dir1 = ResolveReference44(model, recipe.DirectionReference1, plane);
                object dir2 = ResolveReference44(model, recipe.DirectionReference2, plane);
                double[] targetDirection = DirectionVector44(dir1, dir2, targetNormal);
                bool directionOpposed = CutOrientation44.Opposed(CutOrientation44.Reflect(recipe.SourceDirection, plane.Normal), targetDirection);
                bool reverse = reverseOverride54 ?? (audit.ReverseDirection50 ^ directionOpposed);
                // det(local 2D reflection) = -dot(targetNormal, reflected sourceNormal).
                bool flip = audit.FlipSideToCut50 ^ (audit.ProfileKind50 == "OPEN" && !normalOpposed);
                if (dir1 != null || dir2 != null) d.SetDirectionReference(dir1, dir2);
                d.ReverseDirection = reverse;
                d.FlipSideToCut = flip;
                d.BothDirections = audit.BothDirections50;
                d.FromType = recipe.FromType;
                if (recipe.FromReference != null) d.SetFromEntity(ResolveReference44(model, recipe.FromReference, plane));
                if (recipe.FromType == (int)swExtrudeFrom_e.swExtrudeFrom_Offset)
                { d.FromOffsetDistance = recipe.FromOffset; d.FromOffsetReverse = audit.FromOffsetReverse50 ^ normalOpposed; }
                d.NormalCut = recipe.NormalCut;
                d.OptimizeGeometry = recipe.OptimizeGeometry;
                // LinkToThickness applies to an extruded boss, not a cut. Some cut
                // definitions report True before edit but SolidWorks normalizes it to
                // False on ModifyDefinition; it is not a cut option to replay.
                d.FeatureScope = recipe.FeatureScope;
                d.AutoSelect = recipe.AutoSelect;
                if (recipe.ScopeBodies != null)
                {
                    object[] bodies = ResolveScopeBodies56(model, feature, audit, recipe.ScopeBodies);
                    d.FeatureScopeBodies = bodies;
                }
                ApplyDirection44(model, d, audit.D1_50, recipe.D1, plane, fallbackDepth54);
                if (audit.BothDirections50) ApplyDirection44(model, d, audit.D2_50, recipe.D2, plane);
                CreateMirrorPartPackage.LogDebug("[CUT44][ATOMIC_APPLY] feature=" + feature.Name + " reverse=" + reverse +
                    " flip=" + flip + " normalOpposed=" + normalOpposed + " directionOpposed=" + directionOpposed +
                    " endConditionPreserved=" + !fallbackDepth54.HasValue +
                    (fallbackDepth54.HasValue ? " fallback=UpToSurfaceToBlind depth_m=" + fallbackDepth54.Value.ToString("R") : ""));
                committed = feature.ModifyDefinition(d, model, null);
                if (!committed) throw new InvalidOperationException("CUT44: atomic ModifyDefinition rejected; no ThroughAll fallback. feature=" + feature.Name);
                var read = feature.GetDefinition() as IExtrudeFeatureData2;
                if (read == null) throw new InvalidOperationException("CUT44: committed definition unavailable.");
                var differences = new List<string>();
                CheckReadback44(differences, "ReverseDirection", reverse, () => read.ReverseDirection);
                CheckReadback44(differences, "FlipSideToCut", flip, () => read.FlipSideToCut);
                CheckReadback44(differences, "BothDirections", audit.BothDirections50, () => read.BothDirections);
                CheckReadback44(differences, "FromType", recipe.FromType, () => read.FromType);
                CheckReadback44(differences, "NormalCut", recipe.NormalCut, () => read.NormalCut);
                CheckReadback44(differences, "OptimizeGeometry", recipe.OptimizeGeometry, () => read.OptimizeGeometry);
                CheckReadback44(differences, "FeatureScope", recipe.FeatureScope, () => read.FeatureScope);
                CheckReadback44(differences, "AutoSelect", recipe.AutoSelect, () => read.AutoSelect);
                CheckDirectionReadback44(differences, read, audit.D1_50, recipe.D1, fallbackDepth54);
                if (audit.BothDirections50) CheckDirectionReadback44(differences, read, audit.D2_50, recipe.D2);
                if (recipe.FromType == (int)swExtrudeFrom_e.swExtrudeFrom_Offset)
                {
                    CheckReadbackDouble44(differences, "FromOffsetDistance", recipe.FromOffset,
                        () => read.FromOffsetDistance, 1e-9);
                    CheckReadback44(differences, "FromOffsetReverse", audit.FromOffsetReverse50 ^ normalOpposed,
                        () => read.FromOffsetReverse);
                }
                CreateMirrorPartPackage.LogDebug("[CUT44][READBACK_ALL] feature=" + feature.Name +
                    " result=" + (differences.Count == 0 ? "PASS" : "FAIL") +
                    " mismatchCount=" + differences.Count +
                    (differences.Count == 0 ? "" : " differences=[" + string.Join("; ", differences) + "]"));
                if (differences.Count != 0)
                    throw new InvalidOperationException("CUT44: committed option mismatch: " + string.Join("; ", differences));
                recipe.Applied = true;
                CreateMirrorPartPackage.LogDebug("[CUT44][COMMIT_PASS] feature=" + feature.Name + " geometryOracleStillRequired=True");
            }
            finally { if (access && !committed) d.ReleaseSelectionAccess(); }
        }

        // The source API occasionally omits an Up-to-Surface target even while the
        // native feature rebuilds. A finite Blind cut is permitted only on the
        // disposable staging Part and only when its entire after-cut body is the
        // exact reflection of the source checkpoint. No feature-name rule is used.
        public static void ApplyReflectedCutWithEvidence54(ISldWorks app, ModelDoc2 model,
            Feature feature, Feature profile, CutAuditSnapshot21 audit, PlaneData plane,
            FeatureBodyState source)
        {
            if (audit == null || audit.Recipe44 == null || !audit.Recipe44.MissingSurfaceReference54)
                throw new InvalidOperationException("CUT54: fallback was not qualified by source capture.");
            if (source == null || source.BeforeBody == null || source.AfterBody == null ||
                source.RemovedBodies == null || source.RemovedBodies.Count == 0 ||
                BodyOperationsHelper.SumBodyVolumes(source.RemovedBodies) <= 0.0)
                throw new InvalidOperationException("CUT54: source before/after/removed geometry is incomplete.");
            var sketch = profile.GetSpecificFeature2() as Sketch;
            double[] origin, normal;
            CaptureSketchPlane50(sketch, out origin, out normal);
            if (origin == null || normal == null || origin.Length < 3 || normal.Length < 3)
                throw new InvalidOperationException("CUT54: reflected sketch plane is unavailable.");
            var reflectedVertices = new List<double[]>();
            foreach (Body2 removed in source.RemovedBodies)
            {
                object[] vertices = removed.GetVertices() as object[];
                if (vertices == null || vertices.Length == 0)
                    throw new InvalidOperationException("CUT54: removed-body vertices are unavailable; bounding-box corners are not a valid cut depth.");
                foreach (Vertex vertex in vertices)
                {
                    double[] point = vertex.GetPoint() as double[];
                    if (point == null || point.Length < 3)
                        throw new InvalidOperationException("CUT54: removed-body vertex coordinates unavailable.");
                    double[] reflected = BodyOperationsHelper.ReflectPointAcrossPlane(point, plane);
                    if (reflected == null || reflected.Length < 3)
                        throw new InvalidOperationException("CUT54: removed-body reflection failed.");
                    reflectedVertices.Add(reflected);
                }
            }
            double[] signedSpan = CutBlindFallback54.SignedSpanFromReflectedVertices(
                origin, normal, reflectedVertices);
            double depth = CutBlindFallback54.ExactDepthFromSignedSpan(signedSpan);
            int expectedMaterialSide = signedSpan[1] >= Math.Abs(signedSpan[0]) ? 1 : -1;
            double sheetThickness = double.NaN;
            for (Feature current = model.FirstFeature() as Feature; current != null;
                current = current.GetNextFeature() as Feature)
            {
                if (!string.Equals(current.GetTypeName2(), "SheetMetal", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    SheetMetalFeatureData sheet = current.GetDefinition() as SheetMetalFeatureData;
                    if (sheet != null && sheet.Thickness > 0.0)
                    {
                        sheetThickness = sheet.Thickness;
                        break;
                    }
                }
                catch { /* Diagnostic only; geometry proof below remains mandatory. */ }
            }
            CreateMirrorPartPackage.LogDebug("[CUT55][MATERIAL_DIRECTION] feature=" + feature.Name +
                " supportOrigin_m=" + string.Join(",", origin.Take(3).Select(value => value.ToString("R"))) +
                " supportNormal=" + string.Join(",", normal.Take(3).Select(value => value.ToString("R"))) +
                " removedSignedMin_m=" + signedSpan[0].ToString("R") +
                " removedSignedMax_m=" + signedSpan[1].ToString("R") +
                " expectedMaterialSide=" + (expectedMaterialSide > 0 ? "+SKETCH_NORMAL" : "-SKETCH_NORMAL") +
                " measuredCutDepth_m=" + depth.ToString("R") +
                " sheetThickness_m=" + (double.IsNaN(sheetThickness) ? "UNAVAILABLE" : sheetThickness.ToString("R")) +
                " depthIsSheetThickness=False vertexCount=" + reflectedVertices.Count);
            BodyTransformResult expected = BodyOperationsHelper.MirrorBodyStrict(app, source.AfterBody, plane);
            if (expected == null || !expected.Success || expected.Body == null)
                throw new InvalidOperationException("CUT54: reflected source after-body unavailable.");
            BodyTransformResult expectedBefore = BodyOperationsHelper.MirrorBodyStrict(app, source.BeforeBody, plane);
            if (expectedBefore == null || !expectedBefore.Success || expectedBefore.Body == null)
                throw new InvalidOperationException("CUT54: reflected source before-body unavailable.");

            bool normalOpposed = CutOrientation44.Opposed(
                CutOrientation44.Reflect(audit.Recipe44.SketchNormal, plane.Normal), normal);
            bool firstReverse = audit.ReverseDirection50 ^ normalOpposed;
            string lastFailure = "No candidate was evaluated.";
            for (int candidate = 0; candidate < 2; candidate++)
            {
                bool reverse = candidate == 0 ? firstReverse : !firstReverse;
                string label = feature.Name + "_CUT54_CANDIDATE_" + candidate;
                try
                {
                    CreateMirrorPartPackage.LogDebug("[CUT54][TRY] feature=" + feature.Name +
                        " candidate=" + candidate + " reverse=" + reverse +
                        " depth_m=" + depth.ToString("R") + " sourceEnd=UpToSurface appliedEnd=Blind");
                    ApplyReflectedCut44(model, feature, profile, audit, plane, depth, reverse);
                    model.ForceRebuild3(false);
                    bool warning;
                    int errorCode = feature.GetErrorCode2(out warning);
                    if (errorCode != 0 && !warning)
                        throw new InvalidOperationException("rebuild error=" + errorCode);
                    string captureError;
                    Body2 actual = BodyOperationsHelper.GetSolidBodyCopyStrict(model, out captureError);
                    if (actual == null)
                        throw new InvalidOperationException("after-body unavailable: " + captureError);
                    BodyBooleanResult missing = BodyOperationsHelper.BooleanCutStrict(
                        expected.Body, actual, label + "_MISSING");
                    BodyBooleanResult extra = BodyOperationsHelper.BooleanCutStrict(
                        actual, expected.Body, label + "_EXTRA");
                    BodyBooleanResult removedActual = BodyOperationsHelper.BooleanCutStrict(
                        expectedBefore.Body, actual, label + "_ACTUAL_REMOVED");
                    double removedVolume = double.NaN;
                    double[] removedCentroid = null;
                    bool measuredRemoval = removedActual.Success &&
                        BodyOperationsHelper.TryGetBodiesVolumeCentroid(
                            removedActual.Bodies, out removedVolume, out removedCentroid);
                    if (!measuredRemoval)
                    {
                        removedVolume = removedActual.Success
                            ? BodyOperationsHelper.SumBodyVolumes(removedActual.Bodies) : double.NaN;
                        removedCentroid = null;
                    }
                    double actualSignedCentroid = removedCentroid == null ? double.NaN :
                        (removedCentroid[0] - origin[0]) * normal[0] +
                        (removedCentroid[1] - origin[1]) * normal[1] +
                        (removedCentroid[2] - origin[2]) * normal[2];
                    bool cutsExpectedMaterialSide = removedVolume > 0.0 &&
                        !double.IsNaN(actualSignedCentroid) &&
                        actualSignedCentroid * expectedMaterialSide > 1e-9;
                    bool exact = missing.Success && extra.Success && missing.Bodies != null &&
                        extra.Bodies != null && removedActual.Success &&
                        missing.Bodies.Count == 0 && extra.Bodies.Count == 0 && removedVolume > 0.0;
                    CreateMirrorPartPackage.LogDebug("[CUT54][GEOMETRY] feature=" + feature.Name +
                        " candidate=" + candidate + " reverse=" + reverse +
                        " missingBoolean=" + missing.Success + " missingCount=" +
                        (missing.Bodies == null ? -1 : missing.Bodies.Count) +
                        " extraBoolean=" + extra.Success + " extraCount=" +
                        (extra.Bodies == null ? -1 : extra.Bodies.Count) +
                        " removedActual_m3=" + removedVolume.ToString("R") +
                        " cutsMaterial=" + (removedVolume > 0.0) +
                        " actualRemovedSignedCentroid_m=" + actualSignedCentroid.ToString("R") +
                        " cutsExpectedMaterialSide=" + cutsExpectedMaterialSide +
                        " result=" + (exact ? "PASS" : "FAIL"));
                    if (exact)
                    {
                        audit.Recipe44.Applied = true;
                        CreateMirrorPartPackage.LogDebug("[CUT54][ACCEPT] feature=" + feature.Name +
                            " originalEnd=UpToSurface outputEnd=Blind oracle=EXACT_AFTER_BODY");
                        return;
                    }
                    lastFailure = "after-body Boolean difference remains";
                    // When the first candidate demonstrably cuts material on the
                    // expected side, reversing cannot repair a depth/profile mismatch and may make
                    // SolidWorks reject a second ModifyDefinition on this feature.
                    if (candidate == 0 && cutsExpectedMaterialSide)
                    {
                        CreateMirrorPartPackage.LogDebug("[CUT55][DIRECTION_DECISION] feature=" +
                            feature.Name + " action=KEEP_MATERIAL_SIDE reverseTrialSkipped=True" +
                            " reason=firstCandidateRemovedMaterialOnExpectedSideButGeometryDiffers");
                        break;
                    }
                }
                catch (Exception error)
                {
                    lastFailure = error.GetType().Name + ": " + error.Message;
                    CreateMirrorPartPackage.LogDebug("[CUT54][REJECT] feature=" + feature.Name +
                        " candidate=" + candidate + " reason=" + lastFailure);
                }
                audit.Recipe44.Applied = false;
            }
            throw new InvalidOperationException("CUT54: no finite-depth direction reproduces the reflected source cut. " + lastFailure);
        }
    }
}
