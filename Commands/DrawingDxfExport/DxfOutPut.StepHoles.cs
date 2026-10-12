// Adapted from user-supplied DXF.zip; see SOURCE.md.
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Countersunk / counterbored holes in the flat pattern. The FlatPattern
    /// view shows the MOUTH of the hole (Φ20 countersink rim) as well as the
    /// through bore (Φ12): both used to go to CUT, so the laser cut a Φ20
    /// hole. The laser only cuts the through bore; the countersink /
    /// counterbore is machined afterwards. The mouth rim is ENGRAVED
    /// (LASER_MARK, workshop rule: mark where to countersink) - or, without
    /// laser marks, drawn on the erase layer. Either way the finalizer
    /// removes its CUT copy.
    /// </summary>
    internal partial class DxfOutPut
    {
        private const double StepHoleToleranceMeters = 1e-5;   // 0.01 mm

        internal sealed class StepHoleMouth
        {
            public VisibleEdgeReference Edge;
            public double OuterRadius;
            public double InnerRadius;
            public string Kind = "";
            public double[] Centre;
            public double[] Axis;
        }

        /// <summary>
        /// Draws every visible countersink / counterbore mouth rim of the
        /// FlatPattern view on <paramref name="layerName"/>: the LASER_MARK
        /// marker layer (<paramref name="engrave"/>) or the erase layer.
        /// Returns the number of rims drawn (their CUT copy leaves the DXF).
        /// </summary>
        private static int HandleStepHoleMouths(
            IModelDoc2 model,
            IView view,
            string layerName,
            bool engrave,
            DXFPropforExport properties)
        {
            string eraseLayerName = layerName;
            if (model == null || view == null || string.IsNullOrWhiteSpace(eraseLayerName))
                return 0;
            string logPrefix = $"File:{properties?.FileName}-{properties?.PartNumber}: ";
            var mouths = new List<StepHoleMouth>();
            try
            {
                List<VisibleEdgeReference> visible = CollectVisibleEdges(view);
                if (visible.Count == 0) return 0;
                var recognizer = new LaserMarkFeatureRecognizer();

                // Visible full circles: the through bore must be on the view.
                var circles = new List<KeyValuePair<VisibleEdgeReference, double[]>>();
                foreach (VisibleEdgeReference v in visible)
                {
                    double[] circle = CircleOf(v?.Edge);
                    if (circle != null) circles.Add(new KeyValuePair<VisibleEdgeReference, double[]>(v, circle));
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (KeyValuePair<VisibleEdgeReference, double[]> candidate in circles)
                {
                    StepHoleMouth mouth;
                    if (!TryGetStepHoleMouth(candidate.Key, candidate.Value, out mouth)) continue;
                    // A MARK feature's own rim is handled by the LASER_MARK pass.
                    if (recognizer.TryResolve(candidate.Key.Edge, out _)) continue;

                    bool boreVisible = circles.Any(other =>
                        !ReferenceEquals(other.Key, candidate.Key) &&
                        ReferenceEquals(other.Key.Component, candidate.Key.Component) &&
                        Math.Abs(other.Value[6] - mouth.InnerRadius) <= StepHoleToleranceMeters &&
                        RadialOffset(other.Value, mouth.Centre, mouth.Axis) <= StepHoleToleranceMeters);
                    if (!boreVisible) continue;

                    string key = (candidate.Key.Component == null ? "P" : "C" + candidate.Key.Component.GetHashCode()) + "|" +
                                 string.Join(",", mouth.Centre.Select(x => Math.Round(x * 1e5).ToString(CultureInfo.InvariantCulture))) +
                                 "|" + Math.Round(mouth.OuterRadius * 1e5).ToString(CultureInfo.InvariantCulture);
                    if (!seen.Add(key)) continue;
                    mouths.Add(mouth);
                }
                if (mouths.Count == 0) return 0;
            }
            catch (Exception ex)
            {
                ErrorEvents.LogDebug(logPrefix + "[STEP_HOLE] lỗi khi dò lỗ côn/bậc: " + ex.Message);
                return 0;
            }

            int drawn = DrawEdgeMarkers(model, view, mouths.Select(m => m.Edge).ToList(), eraseLayerName, properties, logPrefix);
            string summary = string.Join("; ", mouths
                .GroupBy(m => m.Kind + " Ø" + StepMm(m.OuterRadius * 2) + " → lỗ xuyên Ø" + StepMm(m.InnerRadius * 2))
                .Select(g => g.Key + " ×" + g.Count()));
            ErrorEvents.LogDebug(logPrefix + "[STEP_HOLE] " + mouths.Count + " miệng lỗ côn/bậc, đã " +
                                 (engrave ? "khắc (LASER_MARK) " : "bỏ ") + drawn + " vòng miệng thay vì cắt: " + summary);
            if (drawn > 0)
            {
                DxfExportNotices.Info(
                    "CUT",
                    engrave
                        ? "Lỗ côn/lỗ bậc: chỉ cắt lỗ xuyên, vòng miệng được KHẮC lên LASER_MARK để đánh dấu chỗ gia công côn/bậc (" +
                          drawn + " vòng: " + summary + ")."
                        : "Lỗ côn/lỗ bậc: chỉ cắt lỗ xuyên, bỏ " + drawn + " vòng miệng khỏi DXF (" + summary +
                          ") - tắt Laser marks nên không khắc. Phần côn/bậc gia công sau khi cắt.");
            }
            if (drawn < mouths.Count)
            {
                ErrorEvents.LogError(logPrefix + "CẢNH BÁO CUT: " + (mouths.Count - drawn) +
                                     " vòng miệng lỗ côn/bậc không chuyển được khỏi CUT (" +
                                     summary + ") - máy sẽ cắt thủng theo vòng lớn. Kiểm tra lại DXF.");
            }
            return drawn;
        }

        private static string StepMm(double meters) =>
            (meters * 1000).ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>[cx,cy,cz, ax,ay,az, r] of a FULL circular edge, else null.</summary>
        private static double[] CircleOf(IEdge edge)
        {
            if (edge == null) return null;
            ICurve curve = null;
            try
            {
                curve = edge.GetCurve() as ICurve;
                if (curve == null || curve.IsLine() || !curve.IsCircle()) return null;
                if (!(curve.CircleParams is double[] c) || c.Length < 7 || !(c[6] > 0)) return null;
                if (edge.GetCurveParams2() is double[] p && p.Length >= 6)
                {
                    double gap = Math.Sqrt(Math.Pow(p[0] - p[3], 2) + Math.Pow(p[1] - p[4], 2) + Math.Pow(p[2] - p[5], 2));
                    if (gap > StepHoleToleranceMeters) return null;   // an arc, not a whole rim
                }
                return c;
            }
            catch
            {
                return null;
            }
            finally
            {
                SafeReleaseComObject(curve);
            }
        }

        /// <summary>Distance from circle centre to the line (centre, axis).</summary>
        private static double RadialOffset(double[] circle, double[] centre, double[] axis)
        {
            double dx = circle[0] - centre[0], dy = circle[1] - centre[1], dz = circle[2] - centre[2];
            double along = dx * axis[0] + dy * axis[1] + dz * axis[2];
            double rx = dx - along * axis[0], ry = dy - along * axis[1], rz = dz - along * axis[2];
            return Math.Sqrt(rx * rx + ry * ry + rz * rz);
        }

        private static double[] UnitAxis(double[] circle)
        {
            double l = Math.Sqrt(circle[3] * circle[3] + circle[4] * circle[4] + circle[5] * circle[5]);
            return l < 1e-12 ? new[] { 0.0, 0, 1 } : new[] { circle[3] / l, circle[4] / l, circle[5] / l };
        }

        /// <summary>
        /// The edge is the mouth of a stepped hole: it joins a planar sheet
        /// face to a CONE that narrows into the material (countersink), or to
        /// a short CYLINDER that ends on an annular step face (counterbore).
        /// </summary>
        private static bool TryGetStepHoleMouth(VisibleEdgeReference reference, double[] circle, out StepHoleMouth mouth)
        {
            mouth = null;
            IEdge edge = reference?.Edge;
            if (edge == null || circle == null) return false;
            object[] faces = null;
            try
            {
                faces = edge.GetTwoAdjacentFaces2() as object[];
                if (faces == null || faces.Length != 2) return false;
                IFace2 a = faces[0] as IFace2, b = faces[1] as IFace2;
                if (a == null || b == null) return false;
                IFace2 wall = IsPlanarFace(a) ? b : IsPlanarFace(b) ? a : null;
                if (wall == null || (IsPlanarFace(a) && IsPlanarFace(b))) return false;

                double radius = circle[6];
                double[] axis = UnitAxis(circle);
                double[] centre = { circle[0], circle[1], circle[2] };

                // A HOLE mouth lies on an INNER loop of the sheet face. The
                // outline of a round part (a Ø20 disc with a hole) is the
                // OUTER loop: never a countersink / counterbore.
                IFace2 sheet = IsPlanarFace(a) ? a : b;
                if (!LiesOnInnerLoop(sheet, centre, axis, radius)) return false;

                Surface surface = wall.GetSurface() as Surface;
                if (surface == null) return false;

                if (surface.IsCone())
                {
                    // Other rim of the cone, smaller = the cone narrows into the part.
                    double inner = OtherRimRadii(wall, centre, axis, radius)
                        .Where(r => r < radius - StepHoleToleranceMeters)
                        .DefaultIfEmpty(0)
                        .Max();
                    if (!(inner > 0)) return false;
                    mouth = new StepHoleMouth
                    {
                        Edge = reference, OuterRadius = radius, InnerRadius = inner,
                        Kind = "lỗ côn (countersink)", Centre = centre, Axis = axis
                    };
                    return true;
                }

                if (surface.IsCylinder())
                {
                    // Counterbore: the cylinder's other rim lies on an annular
                    // planar step face (its OUTER loop is that rim) with a
                    // smaller concentric hole inside.
                    foreach (object edgeObject in wall.GetEdges() as object[] ?? new object[0])
                    {
                        IEdge other = edgeObject as IEdge;
                        double[] otherCircle = CircleOf(other);
                        if (otherCircle == null ||
                            Math.Abs(otherCircle[6] - radius) > StepHoleToleranceMeters ||
                            RadialOffset(otherCircle, centre, axis) > StepHoleToleranceMeters)
                            continue;
                        double offset = Math.Abs((otherCircle[0] - centre[0]) * axis[0] +
                                                 (otherCircle[1] - centre[1]) * axis[1] +
                                                 (otherCircle[2] - centre[2]) * axis[2]);
                        if (offset < StepHoleToleranceMeters) continue;   // the same rim
                        foreach (object stepObject in other.GetTwoAdjacentFaces2() as object[] ?? new object[0])
                        {
                            IFace2 step = stepObject as IFace2;
                            if (step == null || !IsPlanarFace(step)) continue;
                            double inner = AnnularStepInnerRadius(step, centre, axis, radius);
                            if (!(inner > 0)) continue;
                            mouth = new StepHoleMouth
                            {
                                Edge = reference, OuterRadius = radius, InnerRadius = inner,
                                Kind = "lỗ bậc (counterbore)", Centre = centre, Axis = axis
                            };
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (COMException)
            {
                return false;
            }
        }

        /// <summary>True when the circle (centre, radius) is an INNER loop of the planar face.</summary>
        private static bool LiesOnInnerLoop(IFace2 face, double[] centre, double[] axis, double radius)
        {
            foreach (object loopObject in face.GetLoops() as object[] ?? new object[0])
            {
                ILoop2 loop = loopObject as ILoop2;
                if (loop == null) continue;
                List<double[]> loopCircles = (loop.GetEdges() as object[] ?? new object[0])
                    .Select(e => CircleOf(e as IEdge))
                    .ToList();
                if (loopCircles.Count == 0 || loopCircles.Any(c => c == null)) continue;
                if (!loopCircles.All(c => Math.Abs(c[6] - radius) <= StepHoleToleranceMeters &&
                                          RadialOffset(c, centre, axis) <= StepHoleToleranceMeters))
                    continue;
                return !loop.IsOuter();
            }
            return false;
        }

        private static IEnumerable<double> OtherRimRadii(IFace2 wall, double[] centre, double[] axis, double radius)
        {
            foreach (object edgeObject in wall.GetEdges() as object[] ?? new object[0])
            {
                double[] circle = CircleOf(edgeObject as IEdge);
                if (circle == null || RadialOffset(circle, centre, axis) > StepHoleToleranceMeters) continue;
                if (Math.Abs(circle[6] - radius) <= StepHoleToleranceMeters) continue;
                yield return circle[6];
            }
        }

        /// <summary>
        /// Planar face whose OUTER loop is a single circle of
        /// <paramref name="outerRadius"/> and which has a smaller concentric
        /// circle inside: the step of a counterbore. Returns that inner radius.
        /// </summary>
        private static double AnnularStepInnerRadius(IFace2 face, double[] centre, double[] axis, double outerRadius)
        {
            bool outerOk = false;
            double inner = 0;
            foreach (object loopObject in face.GetLoops() as object[] ?? new object[0])
            {
                ILoop2 loop = loopObject as ILoop2;
                if (loop == null) continue;
                List<double[]> loopCircles = (loop.GetEdges() as object[] ?? new object[0])
                    .Select(e => CircleOf(e as IEdge))
                    .ToList();
                if (loopCircles.Count == 0 || loopCircles.Any(c => c == null)) continue;
                if (loopCircles.Any(c => RadialOffset(c, centre, axis) > StepHoleToleranceMeters)) continue;
                if (loop.IsOuter())
                    outerOk = loopCircles.All(c => Math.Abs(c[6] - outerRadius) <= StepHoleToleranceMeters);
                else
                    inner = Math.Max(inner, loopCircles.Max(c => c[6]));
            }
            return outerOk && inner < outerRadius - StepHoleToleranceMeters ? inner : 0;
        }

        /// <summary>Mirrors model edges into the view sketch on a layer (same path as the MARK markers).</summary>
        private static int DrawEdgeMarkers(
            IModelDoc2 model,
            IView view,
            List<VisibleEdgeReference> edges,
            string layerName,
            DXFPropforExport properties,
            string logPrefix)
        {
            var markerContext = new MarkerCreationContext(model, view, properties);
            IDrawingDoc drawing = model as IDrawingDoc;
            ISketch viewSketch = null;
            SketchManager sketchManager = null;
            MathUtility mathUtility = null;
            MathTransform modelToView = null;
            MathTransform modelToSketch = null;
            bool oldAddToDb = false;
            int created = 0;
            try
            {
                if (drawing == null) return 0;
                drawing.ActivateView(view.GetName2());
                viewSketch = view.IGetSketch();
                sketchManager = model.SketchManager as SketchManager;
                mathUtility = DxfExportContext.App?.GetMathUtility() as MathUtility;
                modelToView = view.ModelToViewTransform;
                modelToSketch = viewSketch?.ModelToSketchTransform;
                if (viewSketch == null || sketchManager == null || mathUtility == null ||
                    modelToView == null || modelToSketch == null)
                    return 0;
                oldAddToDb = sketchManager.AddToDB;
                sketchManager.AddToDB = true;
                foreach (VisibleEdgeReference reference in edges)
                {
                    string failure;
                    try
                    {
                        created += CreateMarkerForEdge(reference?.Edge, reference?.Component, sketchManager,
                            modelToView, modelToSketch, mathUtility, layerName, markerContext, out failure);
                    }
                    catch (Exception ex)
                    {
                        failure = ex.GetType().Name + ": " + ex.Message;
                    }
                    if (!string.IsNullOrWhiteSpace(failure))
                        ErrorEvents.LogDebug(logPrefix + "[STEP_HOLE] không vẽ được vòng xoá: " + failure);
                }
                return created;
            }
            catch (COMException ex)
            {
                DxfTrace.Debug("Cannot draw step-hole erase markers: " + ex.Message);
                return created;
            }
            finally
            {
                if (sketchManager != null)
                {
                    try { sketchManager.AddToDB = oldAddToDb; } catch { }
                }
                try { model.ClearSelection2(true); } catch { }
                SafeReleaseComObject(viewSketch);
                SafeReleaseComObject(sketchManager);
                SafeReleaseComObject(mathUtility);
                SafeReleaseComObject(modelToView);
                SafeReleaseComObject(modelToSketch);
            }
        }

        // ───────────────────── ordinary holes recut by a MARK feature ─────────────────────

        /// <summary>
        /// Part view: on the flat pattern the wall of an ORDINARY hole can
        /// belong to a MARK feature (SOLIDWORKS hands the seed holes cut
        /// between Unfold and Fold to the first cut of that group, or the
        /// MARK sketch also has the rivet circles). The hole then became
        /// LASER_MARK (engraved, never cut). Rules, in this order:
        ///  1. the Ø6 hole of a MARK feature is a mark;
        ///  2. next to its Ø6 holes, the holes of another size of the same
        ///     MARK feature are ordinary holes: kept on CUT;
        ///  3. a hole of a size the MARK feature draws itself (sketch circle,
        ///     Hole Wizard diameter) is a mark;
        ///  4. otherwise kept on CUT when, in the open (folded) configuration,
        ///     that MARK feature has no hole of this radius while ordinary
        ///     features do.
        /// Returns the number of edges removed from <paramref name="markedEdges"/>.
        /// </summary>
        private static int DropOrdinaryHolesRecutByMark(
            IView view,
            List<VisibleEdgeReference> markedEdges,
            LaserMarkFeatureRecognizer recognizer,
            string logPrefix)
        {
            if (markedEdges == null || markedEdges.Count == 0) return 0;
            // Borrowed RCW shared with SolidWorks: never release it here.
            if (!(view?.ReferencedDocument is PartDoc part)) return 0;
            var model = part as IModelDoc2;
            if (model == null) return 0;

            var markRadii = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            var ownRadii = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            List<double> ordinaryRadii = null;
            var dropped = new List<VisibleEdgeReference>();
            var detail = new Dictionary<string, int>(StringComparer.Ordinal);
            const double tolerance = LaserMarkFeatureRecognizer.HoleRadiusToleranceMeters;   // rolled sheets measure rims slightly long
            try
            {
                // Full circles each MARK feature shows on this view.
                var shown = new Dictionary<string, List<double[]>>(StringComparer.OrdinalIgnoreCase);
                var circles = new List<KeyValuePair<VisibleEdgeReference, double[]>>();
                foreach (VisibleEdgeReference reference in markedEdges)
                {
                    double[] circle = CircleOf(reference?.Edge);
                    string source = reference?.MarkSource ?? "";
                    if (circle == null || source.Length == 0) continue;
                    circles.Add(new KeyValuePair<VisibleEdgeReference, double[]>(reference, circle));
                    if (!shown.TryGetValue(source, out List<double[]> list)) shown[source] = list = new List<double[]>();
                    list.Add(circle);
                }

                foreach (KeyValuePair<VisibleEdgeReference, double[]> pair in circles)
                {
                    VisibleEdgeReference reference = pair.Key;
                    double[] circle = pair.Value;
                    string source = reference.MarkSource;
                    double radius = circle[6];

                    if (LaserMarkFeatureRecognizer.IsMarkHoleRadius(radius)) continue;
                    List<double[]> sameFeature = shown[source];
                    // A second rim around a Ø6 hole (cut at an angle) is the same hole, not another size.
                    bool rimOfMarkHole = sameFeature.Any(other =>
                        LaserMarkFeatureRecognizer.IsMarkHoleRadius(other[6]) &&
                        Math.Sqrt(Math.Pow(other[0] - circle[0], 2) + Math.Pow(other[1] - circle[1], 2) +
                                  Math.Pow(other[2] - circle[2], 2)) < radius);
                    if (!rimOfMarkHole && LaserMarkFeatureRecognizer.OnlyMarkSizeIsMark(sameFeature.Select(c => c[6])))
                    {
                        dropped.Add(reference);
                        string mixedKey = source + " Ø" + StepMm(radius * 2) + " (cùng feature có lỗ mark Ø" +
                                          StepMm(LaserMarkFeatureRecognizer.MarkHoleRadiusMeters * 2) + ")";
                        detail[mixedKey] = (detail.TryGetValue(mixedKey, out int m) ? m : 0) + 1;
                        continue;
                    }

                    if (!ownRadii.TryGetValue(source, out List<double> own))
                        ownRadii[source] = own = OwnHoleRadii(part, source, recognizer);
                    if (own.Any(r => Math.Abs(r - radius) <= tolerance)) continue;

                    if (!markRadii.TryGetValue(source, out List<double> radii))
                    {
                        radii = new List<double>();
                        Feature feature = null;
                        try { feature = part.FeatureByName(source) as Feature; } catch { }
                        if (feature != null)
                            radii = HoleRadiiOfFaces((feature.GetFaces() as object[] ?? new object[0]).OfType<IFace2>());
                        markRadii[source] = radii;
                    }
                    // No hole of the MARK feature in the open configuration:
                    // nothing to compare with, keep the mark.
                    if (radii.Count == 0) continue;
                    if (radii.Any(r => Math.Abs(r - radius) <= tolerance)) continue;

                    if (ordinaryRadii == null)
                    {
                        ordinaryRadii = new List<double>();
                        foreach (object bodyObject in part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[] ?? new object[0])
                        {
                            foreach (IFace2 face in ((bodyObject as Body2)?.GetFaces() as object[] ?? new object[0]).OfType<IFace2>())
                            {
                                if (IsPlanarFace(face)) continue;
                                Feature owner = null;
                                try { owner = face.GetFeature() as Feature; } catch { }
                                if (owner == null || recognizer.TryResolve(owner, out _)) continue;
                                ordinaryRadii.AddRange(HoleRadiiOfFaces(new[] { face }));
                            }
                        }
                    }
                    if (!ordinaryRadii.Any(r => Math.Abs(r - radius) <= tolerance)) continue;

                    dropped.Add(reference);
                    string key = source + " Ø" + StepMm(radius * 2) + " (MARK chỉ có Ø" +
                                 string.Join("/", radii.Select(r => StepMm(r * 2)).Distinct()) + ")";
                    detail[key] = (detail.TryGetValue(key, out int n) ? n : 0) + 1;
                }
            }
            catch (Exception ex)
            {
                ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][RECUT-ORDINARY] lỗi: " + ex.Message);
                return 0;
            }

            if (dropped.Count == 0) return 0;
            markedEdges.RemoveAll(dropped.Contains);
            string text = string.Join("; ", detail.Select(p => p.Key + " ×" + p.Value));
            ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][RECUT-ORDINARY] giữ " + dropped.Count +
                                 " vòng lỗ thường trên CUT (lỗ thường thuộc feature MARK ở cấu hình trải phẳng): " + text);
            DxfExportNotices.Info(
                "LASER_MARK",
                dropped.Count + " vòng lỗ thường thuộc feature MARK ở cấu hình trải phẳng - giữ ở CUT (không khắc): " + text + ".");
            return dropped.Count;
        }

        /// <summary>
        /// Hole radii the MARK feature draws itself: the full circles of its
        /// sketches and the diameters of a Hole Wizard hole; for a pattern,
        /// those of its MARK seed. Unlike the walls of the folded body they
        /// do not change when the sheet is rolled or twisted.
        /// </summary>
        private static List<double> OwnHoleRadii(PartDoc part, string source, LaserMarkFeatureRecognizer recognizer)
        {
            var radii = new List<double>();
            try
            {
                Feature feature = part.FeatureByName(source) as Feature;
                if (feature != null && recognizer.TryResolve(feature, out LaserMarkFeatureRecognizer.Resolution mark) &&
                    !string.Equals(mark.MarkedName, source, StringComparison.OrdinalIgnoreCase))
                    feature = part.FeatureByName(mark.MarkedName) as Feature;
                if (feature == null) return radii;

                for (Feature sub = feature.GetFirstSubFeature() as Feature; sub != null; sub = sub.GetNextSubFeature() as Feature)
                {
                    if (!(sub.GetSpecificFeature2() is ISketch sketch)) continue;
                    foreach (object segment in sketch.GetSketchSegments() as object[] ?? new object[0])
                    {
                        if (segment is ISketchArc arc && !((ISketchSegment)segment).ConstructionGeometry && arc.IsCircle() == 1)
                            radii.Add(arc.GetRadius());
                    }
                }
                if (feature.GetDefinition() is IWizardHoleFeatureData2 wizard)
                {
                    if (wizard.HoleDiameter > 0) radii.Add(wizard.HoleDiameter / 2);
                    if (wizard.ThruHoleDiameter > 0) radii.Add(wizard.ThruHoleDiameter / 2);
                }
            }
            catch (Exception ex)
            {
                ErrorEvents.LogDebug("[LASER_MARK][RECUT-ORDINARY] không đọc được cỡ lỗ của " + source + ": " + ex.Message);
            }
            return radii;
        }
    }
}
