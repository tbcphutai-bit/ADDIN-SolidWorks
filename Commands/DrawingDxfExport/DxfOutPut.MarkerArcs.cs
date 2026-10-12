// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Marker arcs/circles for LASER_MARK.
    ///
    /// A MARK feature edge or a MARK Drawing View sketch can only reach the
    /// LASER_MARK layer through a sketch segment created on the temporary
    /// Drawing (model edges have no layer setter). Straight lines are
    /// unambiguous, but an arc created from three points can come back from
    /// SOLIDWORKS as the OTHER arc of the circle (wrong side), with swapped
    /// end points or with another radius; then the CUT copy is not covered
    /// and the arc stays on CUT although its feature/sketch is named MARK.
    ///
    /// Every marker arc is therefore read back and compared with the
    /// geometry it must have. The first construction that reads back
    /// correctly is kept; a wrong one is parked on a private layer that the
    /// DXF finalizer deletes; if none is right the caller falls back to an
    /// exact sampled polyline (still on LASER_MARK, still covering CUT).
    /// </summary>
    internal partial class DxfOutPut
    {
        internal sealed class MarkerArcStats
        {
            public int ArcRequests { get; set; }
            public int CircleRequests { get; set; }
            public int ArcPolylineFallbacks { get; set; }
            public int CirclePolylineFallbacks { get; set; }
            public int DiscardedAttempts { get; set; }
            public Dictionary<string, int> Variants { get; } =
                new Dictionary<string, int>(StringComparer.Ordinal);

            public void CountVariant(string variant)
            {
                Variants[variant] = Variants.TryGetValue(variant, out int count)
                    ? count + 1
                    : 1;
            }

            public bool HasFallback =>
                ArcPolylineFallbacks > 0 ||
                CirclePolylineFallbacks > 0 ||
                Variants.Keys.Any(name => name != FirstArcVariant &&
                                          name != CircleVariant);
        }

        private const string FirstArcVariant = "3-point(start,end,mid)";
        private const string CircleVariant = "circle";

        private sealed class MarkerCreationContext
        {
            public MarkerCreationContext(
                IModelDoc2 model,
                IView view,
                DXFPropforExport properties)
            {
                Model = model;
                View = view;
                DiscardLayerName = properties?.MarkDiscardLayerName;
                Stats = properties?.MarkerArcStats ?? new MarkerArcStats();
                Owner = $"File:{properties?.FileName}-{properties?.PartNumber}";
            }

            public IModelDoc2 Model { get; }
            public IView View { get; }
            public string DiscardLayerName { get; }
            public MarkerArcStats Stats { get; }
            public string Owner { get; }

            // Straight markers drawn in this view (sketch space, metres).
            private readonly List<double[]> _lineMarkers = new List<double[]>();
            private const double SameLineTolerance = 1e-7;

            public void AddLineMarker(double[] start, double[] end)
            {
                _lineMarkers.Add(new[] { start[0], start[1], end[0], end[1] });
            }

            /// <summary>
            /// True when a straight marker with the same end points (either
            /// direction) was already drawn, or the edge has no length.
            /// </summary>
            public bool HasLineMarker(double[] start, double[] end)
            {
                if (Math.Abs(start[0] - end[0]) <= 1e-9 &&
                    Math.Abs(start[1] - end[1]) <= 1e-9)
                    return true;
                foreach (double[] line in _lineMarkers)
                {
                    if ((Near(line[0], line[1], start) && Near(line[2], line[3], end)) ||
                        (Near(line[0], line[1], end) && Near(line[2], line[3], start)))
                        return true;
                }
                return false;
            }

            private static bool Near(double x, double y, double[] point)
            {
                return Math.Abs(x - point[0]) <= SameLineTolerance &&
                       Math.Abs(y - point[1]) <= SameLineTolerance;
            }
        }

        /// <summary>Geometry a marker arc must have, in sketch space.</summary>
        private sealed class ArcMarkerSpec
        {
            public double[] Start;
            public double[] End;
            public double[] Center;
            public double Radius;

            /// <summary>Direction of travel start -> (a point on the arc) -> end.</summary>
            public bool CounterClockwise;

            /// <summary>Angle swept from Start to End in that direction, (0, 2π).</summary>
            public double Sweep;
        }

        private static ArcMarkerSpec BuildArcMarkerSpec(
            double[] start,
            double[] end,
            double[] mid)
        {
            if (!IsUsableSketchPoint(start) ||
                !IsUsableSketchPoint(end) ||
                !IsUsableSketchPoint(mid))
                return null;

            double ax = start[0], ay = start[1];
            double bx = mid[0], by = mid[1];
            double cx = end[0], cy = end[1];
            double determinant =
                2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            double scale = Math.Max(
                1e-12,
                Math.Max(
                    Distance2D(start, end),
                    Math.Max(Distance2D(start, mid), Distance2D(mid, end))));
            // Nearly collinear: a 3-point arc would have a huge, unstable
            // radius. The caller samples the edge instead.
            if (Math.Abs(determinant) <= 1e-9 * scale * scale) return null;

            double a2 = ax * ax + ay * ay;
            double b2 = bx * bx + by * by;
            double c2 = cx * cx + cy * cy;
            double centerX =
                (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / determinant;
            double centerY =
                (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / determinant;
            var center = new[] { centerX, centerY, 0.0 };
            double radius = Distance2D(center, start);
            if (radius <= 1e-9 || double.IsNaN(radius) || double.IsInfinity(radius))
                return null;
            // Sagitta below ~1e-5 of the chord: a 3-point arc is numerically
            // meaningless (rolled-sheet edges), the sampled polyline is exact.
            if (radius > 1e4 * scale) return null;

            // Orientation of the triangle start-mid-end is the direction in
            // which the circle is traversed from start through mid to end.
            double orientation =
                (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            bool ccw = orientation > 0;

            double startAngle = Math.Atan2(ay - centerY, ax - centerX);
            double endAngle = Math.Atan2(cy - centerY, cx - centerX);
            double sweep = ccw
                ? NormalizePositiveAngle(endAngle - startAngle)
                : NormalizePositiveAngle(startAngle - endAngle);
            if (sweep >= 2 * Math.PI - 1e-9) return null;

            return new ArcMarkerSpec
            {
                Start = start,
                End = end,
                Center = center,
                Radius = radius,
                CounterClockwise = ccw,
                Sweep = sweep
            };
        }

        /// <summary>
        /// true = the segment is the requested arc, false = it is not,
        /// null = it could not be read back (then it is accepted as before).
        /// </summary>
        private static bool? VerifyArcSegment(
            ISketchSegment segment,
            ArcMarkerSpec spec)
        {
            ISketchArc arc = segment as ISketchArc;
            if (arc == null) return null;

            double[] center;
            double[] start;
            double[] end;
            try
            {
                center = ReadSketchPoint(arc.GetCenterPoint2());
                start = ReadSketchPoint(arc.GetStartPoint2());
                end = ReadSketchPoint(arc.GetEndPoint2());
            }
            catch
            {
                return null;
            }
            if (center == null || start == null || end == null) return null;

            double tolerance = Math.Max(5e-6, spec.Radius * 1e-4);
            if (Distance2D(center, spec.Center) > tolerance) return false;
            double radius = Distance2D(center, start);
            if (Math.Abs(radius - spec.Radius) > tolerance) return false;

            bool forward =
                Distance2D(start, spec.Start) <= tolerance &&
                Distance2D(end, spec.End) <= tolerance;
            bool swapped =
                Distance2D(start, spec.End) <= tolerance &&
                Distance2D(end, spec.Start) <= tolerance;
            if (!forward && !swapped) return false;

            // The two arcs between the same end points differ in length,
            // except for a half circle.
            double expectedLength = radius * spec.Sweep;
            double complementLength = radius * (2 * Math.PI - spec.Sweep);
            double length = 0;
            try { length = segment.GetLength(); } catch { }
            if (length > 0 &&
                Math.Abs(expectedLength - complementLength) >
                    1e-3 * Math.Max(expectedLength, complementLength))
            {
                double lengthTolerance =
                    2e-3 * Math.Max(expectedLength, complementLength);
                if (Math.Abs(length - expectedLength) <= lengthTolerance)
                    return true;
                if (Math.Abs(length - complementLength) <= lengthTolerance)
                    return false;
                // Neither: the length is in other units. Use the direction.
            }

            int rotation;
            try { rotation = arc.GetRotationDir(); }
            catch { return null; }
            if (rotation == 0) return null;

            // Direction of the created arc as seen from ITS start point.
            bool expectCounterClockwise =
                forward ? spec.CounterClockwise : !spec.CounterClockwise;
            return (rotation > 0) == expectCounterClockwise;
        }

        private static bool? VerifyCircleSegment(
            ISketchSegment segment,
            double[] center,
            double radius)
        {
            ISketchArc arc = segment as ISketchArc;
            if (arc == null) return null;

            try
            {
                double[] actualCenter = ReadSketchPoint(arc.GetCenterPoint2());
                double[] onCircle = ReadSketchPoint(arc.GetStartPoint2());
                if (actualCenter == null || onCircle == null) return null;

                double tolerance = Math.Max(5e-6, radius * 1e-4);
                return Distance2D(actualCenter, center) <= tolerance &&
                       Math.Abs(Distance2D(actualCenter, onCircle) - radius) <= tolerance;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Creates the arc start -> end that passes through
        /// <paramref name="mid"/> and keeps it only when it reads back as that
        /// arc. Returns false when no construction produced it (or the points
        /// are almost collinear); the caller then samples the edge.
        /// </summary>
        private static bool TryCreateVerifiedArcMarker(
            MarkerCreationContext context,
            SketchManager sketchManager,
            double[] start,
            double[] end,
            double[] mid,
            ICollection<ISketchSegment> segments)
        {
            context.Stats.ArcRequests++;
            ArcMarkerSpec spec = BuildArcMarkerSpec(start, end, mid);
            if (spec == null)
            {
                context.Stats.ArcPolylineFallbacks++;
                return false;
            }

            double centerX = spec.Center[0];
            double centerY = spec.Center[1];
            short direction = (short)(spec.CounterClockwise ? 1 : -1);
            var attempts = new List<KeyValuePair<string, Func<SketchSegment>>>
            {
                new KeyValuePair<string, Func<SketchSegment>>(
                    FirstArcVariant,
                    () => sketchManager.Create3PointArc(
                        start[0], start[1], 0,
                        end[0], end[1], 0,
                        mid[0], mid[1], 0)),
                new KeyValuePair<string, Func<SketchSegment>>(
                    "3-point(start,mid,end)",
                    () => sketchManager.Create3PointArc(
                        start[0], start[1], 0,
                        mid[0], mid[1], 0,
                        end[0], end[1], 0)),
                new KeyValuePair<string, Func<SketchSegment>>(
                    "centre-arc(direction)",
                    () => sketchManager.CreateArc(
                        centerX, centerY, 0,
                        start[0], start[1], 0,
                        end[0], end[1], 0,
                        direction)),
                new KeyValuePair<string, Func<SketchSegment>>(
                    "centre-arc(reversed)",
                    () => sketchManager.CreateArc(
                        centerX, centerY, 0,
                        start[0], start[1], 0,
                        end[0], end[1], 0,
                        (short)-direction))
            };

            foreach (KeyValuePair<string, Func<SketchSegment>> attempt in attempts)
            {
                SketchSegment created = null;
                try
                {
                    created = attempt.Value();
                }
                catch (Exception ex)
                {
                    DxfTrace.Debug(
                        "Marker arc construction '" + attempt.Key +
                        "' failed: " + ex.Message);
                }
                if (created == null) continue;

                bool? verdict = VerifyArcSegment(created, spec);
                if (verdict != false)
                {
                    segments.Add(created);
                    context.Stats.CountVariant(attempt.Key);
                    if (attempt.Key != FirstArcVariant)
                    {
                        LogMarkerFallback(
                            context,
                            "arc r=" + (spec.Radius * 1000).ToString("0.###") +
                            " mm was wrong with the standard construction; kept '" +
                            attempt.Key + "'.");
                    }
                    return true;
                }

                LogMarkerFallback(
                    context,
                    "construction '" + attempt.Key + "' read back as a " +
                    "different arc (r=" + (spec.Radius * 1000).ToString("0.###") +
                    " mm); discarded.");
                DiscardMarkerSegment(context, created);
                SafeReleaseComObject(created);
            }

            context.Stats.ArcPolylineFallbacks++;
            LogMarkerFallback(
                context,
                "no construction gave the requested arc (r=" +
                (spec.Radius * 1000).ToString("0.###") +
                " mm); using a sampled polyline.");
            return false;
        }

        private static bool TryCreateVerifiedCircleMarker(
            MarkerCreationContext context,
            SketchManager sketchManager,
            double[] center,
            double radius,
            ICollection<ISketchSegment> segments)
        {
            context.Stats.CircleRequests++;
            if (!IsUsableSketchPoint(center) || radius <= 1e-9)
            {
                context.Stats.CirclePolylineFallbacks++;
                return false;
            }

            SketchSegment created = null;
            try
            {
                created = sketchManager.CreateCircleByRadius(
                    center[0], center[1], 0, radius);
            }
            catch (Exception ex)
            {
                DxfTrace.Debug("Marker circle construction failed: " + ex.Message);
            }

            if (created != null)
            {
                bool? verdict = VerifyCircleSegment(created, center, radius);
                if (verdict != false)
                {
                    segments.Add(created);
                    context.Stats.CountVariant(CircleVariant);
                    return true;
                }

                LogMarkerFallback(
                    context,
                    "circle r=" + (radius * 1000).ToString("0.###") +
                    " mm read back with another centre/radius; discarded.");
                DiscardMarkerSegment(context, created);
                SafeReleaseComObject(created);
            }

            context.Stats.CirclePolylineFallbacks++;
            return false;
        }

        /// <summary>
        /// Takes a wrong marker segment out of the export: it is moved to the
        /// private discard layer (removed by the DXF finalizer) or, when that
        /// layer is unavailable, deleted from the view sketch.
        /// </summary>
        private static void DiscardMarkerSegment(
            MarkerCreationContext context,
            ISketchSegment segment)
        {
            if (segment == null) return;
            context.Stats.DiscardedAttempts++;

            if (!string.IsNullOrEmpty(context.DiscardLayerName))
            {
                try
                {
                    segment.Layer = context.DiscardLayerName;
                    segment.LayerOverride = 0;
                    if (string.Equals(
                            segment.Layer,
                            context.DiscardLayerName,
                            StringComparison.OrdinalIgnoreCase))
                        return;
                }
                catch (Exception ex)
                {
                    DxfTrace.Debug(
                        "Cannot move a wrong marker to the discard layer: " +
                        ex.Message);
                }
            }

            try
            {
                ISelectionMgr selectionManager =
                    context.Model?.SelectionManager as ISelectionMgr;
                SelectData selectData = selectionManager?.CreateSelectData();
                if (selectData != null && context.View != null)
                    selectData.View = (View)context.View;
                context.Model?.ClearSelection2(true);
                segment.Select4(false, selectData);
                context.Model?.Extension.DeleteSelection2(
                    (int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch (Exception ex)
            {
                ErrorEvents.LogError(
                    context.Owner + ": LỖI LASER_MARK: không gỡ được một " +
                    "cung marker sai khỏi Drawing tạm - DXF có thể có thêm " +
                    "một đường cong thừa: " + ex.Message);
            }
        }

        private static void LogMarkerFallback(
            MarkerCreationContext context,
            string message)
        {
            DxfTrace.Debug("[DXF][MARK_ARC] " + message);
            try
            {
                ErrorEvents.LogDebug(
                    "[DXF][MARK_ARC] " + context.Owner + ": " + message);
            }
            catch
            {
            }
        }

        /// <summary>
        /// One line in the export panel: how many MARK arcs/circles were
        /// requested and how they were built.
        /// </summary>
        private static void ReportMarkerArcStats(DXFPropforExport properties)
        {
            MarkerArcStats stats = properties?.MarkerArcStats;
            if (stats == null ||
                (stats.ArcRequests == 0 && stats.CircleRequests == 0))
                return;

            string text =
                "Cung/tròn MARK: " + stats.ArcRequests + " cung, " +
                stats.CircleRequests + " đường tròn";
            if (stats.Variants.Count > 0)
            {
                text += "; cách dựng: " + string.Join(", ",
                    stats.Variants.Select(pair => pair.Key + " ×" + pair.Value));
            }
            int polylines =
                stats.ArcPolylineFallbacks + stats.CirclePolylineFallbacks;
            if (polylines > 0)
            {
                text += "; " + polylines + " chuyển sang polyline lấy mẫu vì " +
                        "SolidWorks không dựng đúng cung/tròn";
            }
            if (stats.DiscardedAttempts > 0)
                text += "; đã loại " + stats.DiscardedAttempts + " cung dựng sai";
            text += ".";

            if (stats.HasFallback)
                DxfExportNotices.Warning("LASER_MARK", "CẢNH BÁO " + text);
            else
                DxfExportNotices.Info("LASER_MARK", text);
        }

        /// <summary>
        /// A point ON an arc edge, without relying on IEdge.GetClosestPointOn
        /// (which drawing-view edges do not always answer). The curve is
        /// evaluated half-way through the edge's own parameter range; the
        /// candidate whose swept angle equals that range is the edge (the
        /// other candidate is the antipode, i.e. the complementary arc).
        /// </summary>
        private static double[] FindArcMidPointByParameter(
            ICurve curve,
            double[] circle,
            double[] parameters,
            double[] modelStart,
            double[] modelEnd)
        {
            if (curve == null || circle == null || circle.Length < 7 ||
                parameters == null || parameters.Length < 8)
                return null;

            double delta = Math.Abs(parameters[7] - parameters[6]);
            if (double.IsNaN(delta) || delta <= 1e-9 ||
                delta >= 2 * Math.PI - 1e-9)
                return null;

            try
            {
                double[] value = curve.Evaluate2(
                    0.5 * (parameters[6] + parameters[7]), 0) as double[];
                if (value == null || value.Length < 3) return null;

                double[] center = { circle[0], circle[1], circle[2] };
                double[] axis = { circle[3], circle[4], circle[5] };
                double[] first = { value[0], value[1], value[2] };
                double[] second =
                {
                    2 * center[0] - first[0],
                    2 * center[1] - first[1],
                    2 * center[2] - first[2]
                };

                double firstSweep = SweepThrough(
                    center, axis, modelStart, first, modelEnd);
                double secondSweep = SweepThrough(
                    center, axis, modelStart, second, modelEnd);
                const double angleTolerance = 1e-4;
                bool firstMatches =
                    !double.IsNaN(firstSweep) &&
                    Math.Abs(firstSweep - delta) <= angleTolerance;
                bool secondMatches =
                    !double.IsNaN(secondSweep) &&
                    Math.Abs(secondSweep - delta) <= angleTolerance;
                if (firstMatches && !secondMatches) return first;
                if (secondMatches && !firstMatches) return second;
                // Half circle (or unreadable): both sides sweep the same
                // angle, so the parameter range itself decides.
                return firstMatches ? first : null;
            }
            catch (Exception ex)
            {
                DxfTrace.Debug("Cannot evaluate arc mid point: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Angle swept when travelling from <paramref name="start"/> to
        /// <paramref name="end"/> around <paramref name="axis"/> on the side
        /// of <paramref name="through"/>.
        /// </summary>
        private static double SweepThrough(
            double[] center,
            double[] axis,
            double[] start,
            double[] through,
            double[] end)
        {
            double[] startDirection = Normalize3(Subtract3(start, center));
            double[] throughDirection = Normalize3(Subtract3(through, center));
            double[] endDirection = Normalize3(Subtract3(end, center));
            double[] unitAxis = Normalize3(axis);
            if (startDirection == null || throughDirection == null ||
                endDirection == null || unitAxis == null)
                return double.NaN;

            double throughSign = Dot3(unitAxis, Cross3(startDirection, throughDirection));
            if (Math.Abs(throughSign) <= 1e-9) return double.NaN;

            double angle = Math.Atan2(
                Dot3(unitAxis, Cross3(startDirection, endDirection)),
                Dot3(startDirection, endDirection));
            if (throughSign > 0) return angle > 0 ? angle : angle + 2 * Math.PI;
            return angle < 0 ? -angle : 2 * Math.PI - angle;
        }

        private static double Dot3(double[] first, double[] second)
        {
            return first[0] * second[0] + first[1] * second[1] + first[2] * second[2];
        }

        /// <summary>Closed polyline approximating a circle, in sketch space.</summary>
        private static List<double[]> SampleCircle(double[] center, double radius)
        {
            var points = new List<double[]>(SketchCurveSamples + 1);
            for (int index = 0; index <= SketchCurveSamples; index++)
            {
                double angle = 2 * Math.PI * index / SketchCurveSamples;
                points.Add(new[]
                {
                    center[0] + radius * Math.Cos(angle),
                    center[1] + radius * Math.Sin(angle),
                    0.0
                });
            }
            points[points.Count - 1] = points[0];
            return points;
        }
    }
}
