using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using static ADDIN.Commands.CircleFaceTubeLayout.TubeLayoutSolver;

namespace ADDIN.Commands.CircleFaceTubeLayout
{
    internal sealed class SwFaceLayout
    {
        private readonly Face2 face;
        private readonly Surface surface;
        private readonly Modeler modeler;
        private readonly Body2 ownerBody;
        private readonly double faceSense;
        private readonly Action<string> log;
        public readonly BoundaryRails Rails;

        private enum SheetCheck { Clear, Intersecting, Inconclusive }

        public SwFaceLayout(Face2 face, Modeler modeler, Action<string> log)
        {
            this.face = face;
            this.modeler = modeler;
            this.log = log;
            surface = (Surface)face.GetSurface();
            ownerBody = face.IGetBody();
            if (surface == null || ownerBody == null)
                throw new LayoutException("The selected face has no supported surface or body.");
            faceSense = face.FaceInSurfaceSense() ? -1 : 1;
            log(F("FACE id={0} surface_type={1} face_vs_surface_sign={2} body_type={3}",
                face.GetFaceId(), surface.Identity(), faceSense, ownerBody.GetType()));

            var loops = (face.GetLoops() as object[] ?? new object[0]).OfType<Loop2>().ToArray();
            foreach (Loop2 loop in loops) log(F("LOOP outer={0} coedges={1}", loop.IsOuter(), loop.GetCoEdgeCount()));
            var outer = loops.Where(l => l.IsOuter()).ToArray();
            if (outer.Length != 1)
                throw new LayoutException("Select a face with one identifiable exterior loop.");

            int count = outer[0].GetCoEdgeCount();
            var pieces = new List<BoundaryPiece>(count);
            CoEdge coedge = outer[0].IGetFirstCoEdge();
            for (int i = 0; i < count; i++)
            {
                if (coedge == null) throw new LayoutException("The exterior loop has a missing coedge.");
                pieces.Add(ReadPiece(coedge, i + 1));
                coedge = coedge.IGetNext();
            }
            Rails = BoundaryRails.Find(pieces, log);
            log(F("RAILS small_mm={0:F6} large_mm={1:F6} small_pieces={2} large_pieces={3}",
                Rails.Small.Length / Mm, Rails.Large.Length / Mm, Rails.Small.PieceCount, Rails.Large.PieceCount));
        }

        public TubeLayout Analyze(int? count, double diameter, double margin, bool flip)
        {
            double sign = faceSense * (flip ? -1 : 1);
            log(F("SETTINGS flip={0} selected_normal_sign={1} large_edge_margin_mm={2:F6} count={3}",
                flip, sign, margin / Mm, count?.ToString() ?? "auto"));
            RailStation Small(double s) => Station(Rails.Small.At(s), sign);
            RailStation Large(double s) => Station(Rails.Large.At(Rails.Large.Length - s), sign);
            var smallCache = CacheRail(Small, Rails.Small.Length, diameter, "small");
            var largeCache = CacheRail(Large, Rails.Large.Length, diameter, "large");

            // Shared outward lift keeps offsets coherent. Re-solve contact after each
            // lift. A bounded search deliberately rejects faces that require more.
            double lift = 0.01 * Mm;
            TubeLayout layout = null;
            int uncertain = 0;
            for (int pass = 0; pass < 9 && lift <= 2 * diameter; pass++)
            {
                if (count.HasValue)
                {
                    layout = Solve(smallCache, Rails.Small.Length, largeCache, Rails.Large.Length,
                        count.Value, diameter, margin, lift, log, verify: false);
                    VerifyExact(layout, Small, Large, Rails.Large.Length, diameter, margin, log);
                }
                else
                {
                    int limit = MaximumCount;
                    while (true)
                    {
                        layout = SolveAutomatic(smallCache, Rails.Small.Length, largeCache, Rails.Large.Length,
                            diameter, margin, lift, log, limit);
                        VerifyExact(layout, Small, Large, Rails.Large.Length, diameter, margin, log);
                        if (layout.Issue == null) break;
                        log(F("AUTO_COUNT exact_rejected={0} reason={1}", layout.Axes.Length, layout.Issue));
                        limit = layout.Axes.Length - 1;
                    }
                }
                int clashes = 0;
                uncertain = 0;
                layout.MinimumSheetEstimate = double.PositiveInfinity;
                for (int i = 0; i < layout.Axes.Length; i++)
                {
                    TubeAxis axis = layout.Axes[i];
                    double estimate = EstimateFaceClearance(axis, diameter / 2, sign);
                    layout.MinimumSheetEstimate = Math.Min(layout.MinimumSheetEstimate, estimate);
                    SheetCheck check = CheckSheet(axis, diameter / 2, i + 1);
                    if (check == SheetCheck.Intersecting) clashes++;
                    if (check == SheetCheck.Inconclusive) uncertain++;
                    log(F("SHEET axis={0} sampled_min_mm={1:F6} brep={2}", i + 1, estimate / Mm, check));
                }
                log(F("LIFT pass={0} offset_mm={1:F6} clashes={2} signed_sample_min_mm={3:F6} inconclusive={4}",
                    pass + 1, lift / Mm, clashes, layout.MinimumSheetEstimate / Mm, uncertain));
                if (clashes == 0 && uncertain == 0 && layout.MinimumSheetEstimate >= -0.02 * Mm) return layout;
                if (flip && ownerBody.GetType() == (int)swBodyType_e.swSolidBody && clashes > 0)
                {
                    // On a solid, the opposite normal points into material. A
                    // flipped solution is allowed only if the body check is clear.
                    log("FLIPPED_SOLID: selected side may be inside the sheet material.");
                }
                lift += Math.Max(Math.Max(0.05 * Mm, lift * 0.5), -layout.MinimumSheetEstimate + 0.02 * Mm);
            }
            layout.Issue = uncertain > 0
                ? "SolidWorks could not verify all tube-to-sheet checks within the allowed lift."
                : "The solver could not clear the sheet while staying close to the selected side.";
            return layout;
        }

        private Func<double, RailStation> CacheRail(Func<double, RailStation> exact, double length, double diameter, string name)
        {
            // The solver runs entirely on managed data. Only the chosen
            // stations are later evaluated against the exact CAD curves.
            int count = Math.Max(2, (int)Math.Ceiling(length / Math.Min(0.004, diameter / 3)));
            if (count > 5000) throw new LayoutException("The end rail is too long for this tube diameter.");
            var samples = new RailStation[count + 1];
            for (int i = 0; i <= count; i++) samples[i] = exact(length * i / count);
            log(F("RAIL_CACHE end={0} samples={1} step_mm={2:F6}", name, samples.Length, length / count / Mm));
            return station =>
            {
                double fraction = Math.Max(0, Math.Min(count, station / length * count));
                int i = Math.Min(count - 1, (int)fraction);
                double t = fraction - i;
                var a = samples[i];
                var b = samples[i + 1];
                return new RailStation(a.Point + (b.Point - a.Point) * t,
                    (a.Normal + (b.Normal - a.Normal) * t).Unit());
            };
        }

        private RailStation Station(Point3 point, double sign, double[] closest = null)
        {
            // XYZ evaluation can fail at a trimmed boundary even when its
            // parent surface is regular at the corresponding UV parameters.
            var xyz = surface.EvaluateAtPoint(point.X, point.Y, point.Z) as double[];
            Point3 normal;
            if (xyz != null && xyz.Length == 11 && TryReadNormal(xyz, 0, out normal))
                return new RailStation(point, normal * sign);

            closest = closest ?? face.GetClosestPointOn(point.X, point.Y, point.Z) as double[];
            if (closest != null && closest.Length == 5 &&
                (Point3.Read(closest) - point).Length <= 0.02 * Mm)
            {
                // Zero derivatives returns [point XYZ, surface normal XYZ].
                var uv = surface.Evaluate(closest[3], closest[4], 0, 0) as double[];
                if (uv != null && uv.Length == 6 &&
                    (Point3.Read(uv) - point).Length <= 0.02 * Mm &&
                    TryReadNormal(uv, 3, out normal))
                {
                    log(F("NORMAL_UV_FALLBACK point_m={0} u={1:R} v={2:R} normal={3}",
                        point, closest[3], closest[4], normal * sign));
                    return new RailStation(point, normal * sign);
                }
            }

            log(F("NORMAL_FAILED point_m={0} xyz_count={1} closest_count={2}",
                point, xyz?.Length ?? 0, closest?.Length ?? 0));
            throw new LayoutException("SolidWorks could not evaluate the selected face normal at a layout point.");
        }

        private static bool TryReadNormal(double[] values, int offset, out Point3 normal)
        {
            normal = new Point3(values[offset], values[offset + 1], values[offset + 2]);
            double length = normal.Length;
            if (!(length > 1e-12) || double.IsInfinity(length)) return false;
            normal = normal * (1 / length);
            return true;
        }

        private double EstimateFaceClearance(TubeAxis axis, double radius, double sign)
        {
            int divisions = Math.Max(16, Math.Min(128, (int)Math.Ceiling(axis.Length / radius)));
            double minimum = double.PositiveInfinity;
            for (int i = 0; i <= divisions; i++)
            {
                Point3 p = axis.A + axis.Direction * (axis.Length * i / divisions);
                var closest = face.GetClosestPointOn(p.X, p.Y, p.Z) as double[];
                Point3 q = Point3.Read(closest);
                Point3 n = Station(q, sign, closest).Normal;
                double along = n.Dot(axis.Direction);
                double radialExtent = radius * Math.Sqrt(Math.Max(0, 1 - along * along));
                minimum = Math.Min(minimum, (p - q).Dot(n) - radialExtent);
            }
            return minimum;
        }

        private SheetCheck CheckSheet(TubeAxis axis, double radius, int index)
        {
            Body2 target = null, tool = null;
            Array result = null;
            try
            {
                // Operations2 consumes its inputs. Only pass bodies owned by this
                // call, never the Part's actual body or a displayed preview body.
                target = ownerBody.GetType() == (int)swBodyType_e.swSolidBody
                    ? ownerBody.ICopy()
                    : modeler.CreateSheetFromFaces(new[] { face });
                var a = axis.A;
                var d = axis.Direction;
                tool = modeler.CreateBodyFromCyl(new[] { a.X, a.Y, a.Z, d.X, d.Y, d.Z, radius, axis.Length }) as Body2;
                if (target == null || tool == null)
                    throw new LayoutException("SolidWorks could not create temporary interference bodies.");
                if (HasFaults(target) || HasFaults(tool))
                    throw new LayoutException("The temporary sheet or tube body has geometric faults.");

                int error;
                result = target.Operations2((int)swBodyOperationType_e.SWBODYINTERSECT, tool, out error) as Array;
                log(F("BREP axis={0} result_count={1} error={2}", index, result?.Length ?? 0, error));
                if (result != null && result.Length > 0) return SheetCheck.Intersecting;
                switch ((swBodyOperationError_e)error)
                {
                    case swBodyOperationError_e.swBodyOperationNoError:
                    case swBodyOperationError_e.swBodyOperationDisjointBodies:
                    case swBodyOperationError_e.swBodyOperationNoIntersect:
                        return SheetCheck.Clear;
                    case swBodyOperationError_e.swBodyOperationUnknownError:
                    case swBodyOperationError_e.swBodyOperationNonManifold:
                    case swBodyOperationError_e.swBodyOperationPartialCoincidence:
                    case swBodyOperationError_e.swBodyOperationBooleanFail:
                        // Near tangency can make the Boolean indeterminate. Retry at
                        // the next lift without treating this attempt as separated.
                        return SheetCheck.Inconclusive;
                    default:
                        throw new LayoutException("Sheet interference check was inconclusive (SolidWorks error " + error + ").");
                }
            }
            finally
            {
                if (result != null)
                    foreach (object body in result) ReleaseTemporary(body);
                ReleaseTemporary(tool);
                ReleaseTemporary(target);
            }
        }

        private static bool HasFaults(Body2 body)
        {
            FaultEntity faults = body.Check3;
            try { return faults != null && faults.Count > 0; }
            finally { ReleaseTemporary(faults); }
        }

        internal static void ReleaseTemporary(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        private BoundaryPiece ReadPiece(CoEdge coedge, int index)
        {
            Edge edge = coedge.IGetEdge();
            Curve curve = (Curve)edge.GetCurve();
            CurveParamData range = edge.GetCurveParams3();
            bool same = coedge.GetSense();
            Point3 start = Point3.Read(same == range.Sense ? range.StartPoint : range.EndPoint);
            Point3 end = Point3.Read(same == range.Sense ? range.EndPoint : range.StartPoint);
            double a = range.UMinValue, b = range.UMaxValue;
            const double joinTolerance = 0.00002;
            bool TryDomain(double u0, double u1, out bool increasing)
            {
                Point3 low = Point3.Read(curve.Evaluate2(u0, 0)), high = Point3.Read(curve.Evaluate2(u1, 0));
                increasing = (low - start).Length + (high - end).Length < (high - start).Length + (low - end).Length;
                log(F("CURVE_DOMAIN coedge={0} sense={1}/{2} raw_u0={3:R} raw_u1={4:R} tried_u0={5:R} tried_u1={6:R} expected_start_m={7} expected_end_m={8} eval_low_m={9} eval_high_m={10}",
                    index, range.Sense, same, a, b, u0, u1, start, end, low, high));
                return ((increasing ? low : high) - start).Length <= joinTolerance &&
                    ((increasing ? high : low) - end).Length <= joinTolerance && u1 > u0;
            }

            double lo = Math.Min(a, b), hi = Math.Max(a, b);
            bool forward;
            if (!TryDomain(lo, hi, out forward))
            {
                // Some edge ranges are reported in reversed parameter space.
                // Accept a domain only when its evaluated endpoints agree.
                double opposite = -lo;
                lo = -hi;
                hi = opposite;
                if (!TryDomain(lo, hi, out forward))
                    throw new LayoutException("Coedge " + index + " does not match its evaluated curve endpoints.");
            }

            double length = curve.GetLength3(lo, hi);
            Point3 t0 = Point3.Read(curve.Evaluate2(forward ? lo : hi, 1), 3) * (forward ? 1 : -1);
            Point3 t1 = Point3.Read(curve.Evaluate2(forward ? hi : lo, 1), 3) * (forward ? 1 : -1);
            log(F("COEDGE index={0} type={1} curve_sense={2} coedge_sense={3} forward={4} u0={5:R} u1={6:R} start_m={7} end_m={8} length_mm={9:F6}",
                index, range.CurveType, range.Sense, same, forward, lo, hi, start, end, length / Mm));

            // Index the monotone length function to avoid a full domain bisection
            // for every trial station. Each returned station uses the true curve.
            const int intervals = 32;
            var distances = new double[intervals + 1];
            for (int i = 1; i < intervals; i++)
            {
                distances[i] = curve.GetLength3(lo, lo + (hi - lo) * i / intervals);
                if (!(distances[i] > distances[i - 1] && distances[i] < length))
                    throw new LayoutException("Coedge arc length is not monotone.");
            }
            distances[intervals] = length;
            Point3 At(double station)
            {
                double target = forward ? station : length - station;
                int pos = Array.BinarySearch(distances, target);
                if (pos >= 0) return Point3.Read(curve.Evaluate2(lo + (hi - lo) * pos / intervals, 0));
                pos = ~pos;
                double min = lo + (hi - lo) * (pos - 1) / intervals, max = lo + (hi - lo) * pos / intervals;
                double minLength = distances[pos - 1], maxLength = distances[pos];
                for (int i = 0; i < 28; i++)
                {
                    double fraction = (target - minLength) / (maxLength - minLength);
                    double u = min + (max - min) * (i == 0 ? fraction : Math.Max(0.2, Math.Min(0.8, fraction)));
                    double l = curve.GetLength3(lo, u);
                    if (Math.Abs(l - target) <= 1e-8) return Point3.Read(curve.Evaluate2(u, 0));
                    if (l < target) { min = u; minLength = l; } else { max = u; maxLength = l; }
                }
                throw new LayoutException("Arc-length inversion did not converge.");
            }

            return new BoundaryPiece(length, start, end, t0, t1, station =>
            {
                Point3 point = At(station);
                // Curve is acquired from the topology. Keep the owning coedge
                // and edge proxies rooted for the lifetime of the evaluator.
                GC.KeepAlive(edge);
                GC.KeepAlive(coedge);
                return point;
            });
        }
    }
}
