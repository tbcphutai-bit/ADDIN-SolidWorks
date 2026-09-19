using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ADDIN.Commands
{
    // Pure geometry: metres in the drawing plane. No SOLIDWORKS mutation here.
    // A sheet section is a closed boundary with two end caps and two matching
    // longitudinal paths. Shape names describe that topology, never select edges.
    internal static class SectionProfilePlanner
    {
        internal struct Point
        {
            public double X, Y;
            public Point(double x, double y) { X = x; Y = y; }
            public static Point operator +(Point a, Point b) { return new Point(a.X + b.X, a.Y + b.Y); }
            public static Point operator -(Point a, Point b) { return new Point(a.X - b.X, a.Y - b.Y); }
            public static Point operator *(Point a, double b) { return new Point(a.X * b, a.Y * b); }
            public double Length { get { return Math.Sqrt(Dot(this, this)); } }
            public Point Unit { get { return this * (1.0 / Length); } }
        }
        internal sealed class Curve
        {
            public Point A, B, Center;
            public bool IsArc;
            public double SweepAngleRadians;
            public object Source;
        }
        internal sealed class Reference
        {
            public Point Position;
            public Curve First, Second;
            public bool IsEnd;
            public bool IsTangent;
        }
        internal sealed class Span
        {
            public Curve First, Last;
            public Curve BendBefore;
            public Point Start, End;
            public Point Direction { get { return (End - Start).Unit; } }
        }
        internal sealed class Length
        {
            public Span Side;
            public Reference Start, End;
            public double Value;
            public string Rule;
        }
        internal sealed class Bend
        {
            public Curve First, Second;
            public Point Position, Bisector;
            public double AngleDegrees;
        }
        internal sealed class Plan
        {
            public CurvedSection Curved;
            public string Shape;
            public string SelectedContour;
            public double Thickness;
            public readonly List<Length> Lengths = new List<Length>();
            public readonly List<Bend> Bends = new List<Bend>();
            public readonly List<Curve> DimensionArcs = new List<Curve>();
            public readonly List<Curve> Boundary = new List<Curve>();
        }
        internal sealed class CurvedSection
        {
            // Legacy single-arc fields are kept for compatibility with the
            // existing single curved-web workflow.  V12 also stores arc data
            // per support so a curved skin may contain several real circular
            // arcs without collapsing them to one guessed "main" arc.
            public Curve Arc;
            public Point Start, End, Mid;
            public double ArcLength;
            public readonly List<Reference> Nodes = new List<Reference>();
            public readonly List<Curve> Supports = new List<Curve>();
            public readonly List<Point> SupportMids = new List<Point>();
            public readonly List<double> SupportArcLengths = new List<double>();
            public readonly List<double> Angles = new List<double>();
        }
        private sealed class Step
        {
            public Curve Curve;
            public Point Start, End;
        }
        private sealed class MissingBendCandidate
        {
            public Curve Curve;
            public bool UseA;
            public Point JoinPoint;
            public Point VirtualSharp;
            public double Gap;
            public double CurrentSetback;
            public double CandidateSetback;
            public double SymmetryError;
        }
        internal static double Dot(Point a, Point b) { return a.X * b.X + a.Y * b.Y; }
        private static double Cross(Point a, Point b) { return a.X * b.Y - a.Y * b.X; }
        private const double AngularTolerance = 0.1; // degrees; independent of view rotation/scale
        private static bool Parallel(Point a, Point b)
        {
            return Math.Abs(Cross(a.Unit, b.Unit)) <= Math.Sin(AngularTolerance * Math.PI / 180);
        }
        private static bool Perpendicular(Point a, Point b)
        {
            return Math.Abs(Dot(a.Unit, b.Unit)) <= Math.Sin(AngularTolerance * Math.PI / 180);
        }
        private static Point Intersection(Span first, Span second)
        {
            Point a = first.Direction, b = second.Direction;
            double determinant = Cross(a, b);
            if (Math.Abs(determinant) < 1e-8) throw new InvalidOperationException("Hai doan tai goc be song song.");
            return first.Start + a * (Cross(second.Start - first.Start, b) / determinant);
        }

        // Backward-compatible overload for callers that intentionally use one tolerance.
        internal static bool TryBuild(List<Curve> curves, object selectedSource, double tolerance,
            out Plan plan, out string reason)
        {
            return TryBuild(curves, selectedSource, tolerance, tolerance, out plan, out reason);
        }

        // geometryTolerance is the strict physical-geometry tolerance.
        // topologyRepairTolerance is used ONLY when no exact next curve exists.
        internal static bool TryBuild(List<Curve> curves, object selectedSource,
            double geometryTolerance, double topologyRepairTolerance,
            out Plan plan, out string reason)
        {
            plan = null;
            reason = "Khong tim duoc bien dang ton kin gom hai mat va hai mep dau.";
            if (curves == null || curves.Count == 0 || geometryTolerance <= 0 || topologyRepairTolerance <= 0)
                return false;
            if (selectedSource == null)
            {
                reason = "Chon mot canh hoac cung tren mat can DIM.";
                return false;
            }

            // SolidWorks can return the same projected section line twice with only
            // a few microns of drawing/model-space numerical separation. Remove only
            // near-identical LINE duplicates before tracing; never merge different
            // arcs or merely-near neighbouring contour edges.
            List<Curve> topology = RemoveNearDuplicateLines(curves, selectedSource, geometryTolerance);
            var seeds = topology.Where(c => ReferenceEquals(c.Source, selectedSource)).ToList();
            if (seeds.Count == 0)
            {
                reason = "Khong tim thay canh/cung click sau khi chuan hoa topology.";
                return false;
            }

            var visited = new HashSet<Curve>();
            var valid = new List<Plan>();
            foreach (Curve seed in seeds)
            {
                if (visited.Contains(seed)) continue;
                try
                {
                    List<Step> loop = TraceLoop(topology, seed, geometryTolerance, topologyRepairTolerance);
                    foreach (Step step in loop) visited.Add(step.Curve);
                    Plan candidate = BuildLoop(loop, selectedSource, geometryTolerance);
                    valid.Add(candidate);
                }
                catch (InvalidOperationException ex)
                {
                    reason = ex.Message;
                }
            }
            if (valid.Count != 1)
            {
                if (valid.Count > 1)
                    reason = "View co nhieu bien dang hop le. Hay chon mot canh cua chi tiet can DIM.";
                return false;
            }
            plan = valid[0];
            reason = null;
            return true;
        }

        private static List<Curve> RemoveNearDuplicateLines(List<Curve> curves,
            object selectedSource, double tolerance)
        {
            var result = new List<Curve>();
            foreach (Curve curve in curves)
            {
                if (curve == null)
                    continue;

                int duplicateIndex = -1;
                if (!curve.IsArc)
                {
                    for (int i = 0; i < result.Count; i++)
                    {
                        Curve kept = result[i];
                        if (kept == null || kept.IsArc)
                            continue;

                        bool sameDirection =
                            (kept.A - curve.A).Length <= tolerance &&
                            (kept.B - curve.B).Length <= tolerance;
                        bool reverseDirection =
                            (kept.A - curve.B).Length <= tolerance &&
                            (kept.B - curve.A).Length <= tolerance;

                        if (sameDirection || reverseDirection)
                        {
                            duplicateIndex = i;
                            break;
                        }
                    }
                }

                if (duplicateIndex < 0)
                {
                    result.Add(curve);
                    continue;
                }

                Curve previous = result[duplicateIndex];
                bool incomingIsSelected = ReferenceEquals(curve.Source, selectedSource);
                bool previousIsSelected = ReferenceEquals(previous.Source, selectedSource);

                // Keep the user's clicked source if it is one of the two copies so
                // seed locking remains authoritative.
                if (incomingIsSelected && !previousIsSelected)
                    result[duplicateIndex] = curve;

                Debug.WriteLine(
                    "[DIM MAT CAT DEDUPE] near-duplicate LINE removed"
                    + ", keptSelected=" + (incomingIsSelected || previousIsSelected)
                    + ", toleranceSheet=" + (tolerance * 1000.0).ToString("0.######") + " mm");
            }
            return result;
        }

        private static List<Step> TraceLoop(List<Curve> curves, Curve seed,
            double geometryTolerance, double topologyRepairTolerance)
        {
            var result = new List<Step>();
            var used = new HashSet<Curve>();
            Curve current = seed;
            Point start = seed.A, finish = seed.B;

            while (true)
            {
                if ((finish - start).Length <= geometryTolerance)
                    throw new InvalidOperationException("Canh qua ngan hoac duong tron kin: khong phai doan mat cat.");
                if (!used.Add(current))
                    throw new InvalidOperationException("Bien dang tu lap, khong phai chuoi ton don.");

                result.Add(new Step { Curve = current, Start = start, End = finish });

                // PASS 1: preserve the original exact topology whenever it exists.
                var exact = curves.Where(c => c != current &&
                    ((c.A - finish).Length <= geometryTolerance ||
                     (c.B - finish).Length <= geometryTolerance)).ToList();

                if (exact.Count > 1)
                {
                    Debug.WriteLine(
                        "[DIM MAT CAT TRACE] BRANCH exactCount=" + exact.Count
                        + ", finishSheet=(" + (finish.X * 1000.0).ToString("0.######")
                        + "," + (finish.Y * 1000.0).ToString("0.######") + ")mm");
                    throw new InvalidOperationException(
                        "Nhieu canh trung nut: bien dang re nhanh/chong hinh, can chon view mat cat ro hon.");
                }

                Curve candidate;
                bool useA;
                double joinGap;
                bool repaired = false;
                bool missingBend = false;

                if (exact.Count == 1)
                {
                    candidate = exact[0];
                    double da = (candidate.A - finish).Length;
                    double db = (candidate.B - finish).Length;
                    useA = da <= db;
                    joinGap = Math.Min(da, db);
                }
                else
                {
                    // PASS 2: tiny endpoint repair. This is deliberately limited to
                    // small projection omissions (for example a missing micro-arc).
                    // It must never be enlarged to bridge material thickness.
                    var ranked = curves
                        .Where(c => c != current)
                        .Select(c => new
                        {
                            Curve = c,
                            GapA = (c.A - finish).Length,
                            GapB = (c.B - finish).Length
                        })
                        .Select(x => new
                        {
                            x.Curve,
                            UseA = x.GapA <= x.GapB,
                            Gap = Math.Min(x.GapA, x.GapB)
                        })
                        .OrderBy(x => x.Gap)
                        .ToList();

                    if (ranked.Count > 0 && ranked[0].Gap <= topologyRepairTolerance)
                    {
                        double bestGap = ranked[0].Gap;
                        int tied = ranked.Count(x => Math.Abs(x.Gap - bestGap) <= geometryTolerance);
                        if (tied != 1)
                            throw new InvalidOperationException(
                                "Nhieu canh trung nut sau topology repair: co nhieu canh gan nhu nhau.");

                        candidate = ranked[0].Curve;
                        useA = ranked[0].UseA;
                        joinGap = ranked[0].Gap;
                        repaired = true;

                        Debug.WriteLine(
                            "[DIM MAT CAT TOPOLOGY REPAIR]"
                            + " gapSheet=" + (joinGap * 1000.0).ToString("0.######") + " mm"
                            + ", limitSheet=" + (topologyRepairTolerance * 1000.0).ToString("0.######") + " mm"
                            + ", nextType=" + (candidate.IsArc ? "ARC" : "LINE"));
                    }
                    else
                    {
                        // PASS 3: a normal bend arc can be absent from the Drawing
                        // API even though both trimmed tangent lines are visible.
                        // Do NOT connect by nearest distance. A missing bend is
                        // accepted only when exactly one LINE->LINE continuation
                        // forms a geometrically valid virtual sharp:
                        //   - the directions are not parallel/antiparallel;
                        //   - the current line reaches the sharp forward;
                        //   - the candidate line reaches the same sharp backward;
                        //   - the two tangent setbacks are symmetric (circular bend);
                        //   - the candidate endpoint is not already claimed by an
                        //     exact/tiny connection to another curve.
                        // This rejects the common false bridge across sheet thickness.
                        List<MissingBendCandidate> bendCandidates = FindMissingBendCandidates(
                            curves, used, current, start, finish, seed,
                            geometryTolerance, topologyRepairTolerance);

                        if (bendCandidates.Count == 1)
                        {
                            MissingBendCandidate bend = bendCandidates[0];
                            candidate = bend.Curve;
                            useA = bend.UseA;
                            joinGap = bend.Gap;
                            repaired = true;
                            missingBend = true;

                            Debug.WriteLine(
                                "[DIM MAT CAT MISSING BEND REPAIR]"
                                + " gapSheet=" + (bend.Gap * 1000.0).ToString("0.######") + " mm"
                                + ", currentSetbackSheet=" + (bend.CurrentSetback * 1000.0).ToString("0.######") + " mm"
                                + ", candidateSetbackSheet=" + (bend.CandidateSetback * 1000.0).ToString("0.######") + " mm"
                                + ", symmetryErrorSheet=" + (bend.SymmetryError * 1000.0).ToString("0.######") + " mm"
                                + ", virtualSharpSheet=(" + (bend.VirtualSharp.X * 1000.0).ToString("0.######")
                                + "," + (bend.VirtualSharp.Y * 1000.0).ToString("0.######") + ")mm");
                        }
                        else
                        {
                            double nearest = ranked.Count == 0 ? double.MaxValue : ranked[0].Gap;
                            if (bendCandidates.Count > 1)
                            {
                                Debug.WriteLine(
                                    "[DIM MAT CAT MISSING BEND] AMBIGUOUS count=" + bendCandidates.Count
                                    + ", finishSheet=(" + (finish.X * 1000.0).ToString("0.######")
                                    + "," + (finish.Y * 1000.0).ToString("0.######") + ")mm");
                                throw new InvalidOperationException(
                                    "Nhieu kha nang noi qua bend bi thieu; khong tu chon canh gan nhat.");
                            }

                            Debug.WriteLine(
                                "[DIM MAT CAT TRACE] OPEN"
                                + ", finishSheet=(" + (finish.X * 1000.0).ToString("0.######")
                                + "," + (finish.Y * 1000.0).ToString("0.######") + ")mm"
                                + ", nearestGapSheet="
                                + (nearest == double.MaxValue ? "NONE" : (nearest * 1000.0).ToString("0.######") + " mm")
                                + ", repairLimitSheet=" + (topologyRepairTolerance * 1000.0).ToString("0.######") + " mm");
                            throw new InvalidOperationException(
                                "Bien dang bi dut: thieu canh/cung trong view. Khong tim duoc virtual bend duy nhat hop le.");
                        }
                    }
                }

                if (candidate == seed)
                {
                    // Exact/tiny closure returns to seed.A. Missing-bend closure is
                    // allowed only because the virtual-sharp test above already
                    // proved one unique geometric continuation to that seed endpoint.
                    if (!missingBend)
                    {
                        double closeGap = (finish - seed.A).Length;
                        double closeTolerance = repaired ? topologyRepairTolerance : geometryTolerance;
                        if (closeGap > closeTolerance)
                            throw new InvalidOperationException("Bien dang khong khep dung dau.");
                    }
                    break;
                }

                start = useA ? candidate.A : candidate.B;
                finish = useA ? candidate.B : candidate.A;
                current = candidate;
            }
            return result;
        }

        private static List<MissingBendCandidate> FindMissingBendCandidates(
            List<Curve> curves, HashSet<Curve> used, Curve current,
            Point currentStart, Point finish, Curve seed,
            double geometryTolerance, double topologyRepairTolerance)
        {
            var result = new List<MissingBendCandidate>();
            if (current == null || current.IsArc)
                return result;

            Point currentVector = finish - currentStart;
            double currentLength = currentVector.Length;
            if (currentLength <= geometryTolerance)
                return result;
            Point currentDirection = currentVector * (1.0 / currentLength);

            foreach (Curve candidate in curves)
            {
                if (candidate == null || candidate == current || candidate.IsArc)
                    continue;
                if (used.Contains(candidate) && candidate != seed)
                    continue;

                TryAddMissingBendOrientation(result, curves, current, candidate,
                    true, currentDirection, currentLength, finish,
                    geometryTolerance, topologyRepairTolerance);
                TryAddMissingBendOrientation(result, curves, current, candidate,
                    false, currentDirection, currentLength, finish,
                    geometryTolerance, topologyRepairTolerance);
            }

            return result;
        }

        private static void TryAddMissingBendOrientation(
            List<MissingBendCandidate> result, List<Curve> curves,
            Curve current, Curve candidate, bool useA,
            Point currentDirection, double currentLength, Point finish,
            double geometryTolerance, double topologyRepairTolerance)
        {
            Point join = useA ? candidate.A : candidate.B;
            Point other = useA ? candidate.B : candidate.A;
            Point candidateVector = other - join;
            double candidateLength = candidateVector.Length;
            if (candidateLength <= geometryTolerance)
                return;

            Point candidateDirection = candidateVector * (1.0 / candidateLength);

            // Missing circular bends connect two different tangent directions.
            // Parallel and antiparallel lines are especially dangerous because the
            // neighbouring sheet-metal skin is normally separated by thickness.
            if (Parallel(currentDirection, candidateDirection))
                return;

            // Never steal an endpoint that already has a valid exact/tiny partner.
            // Such an endpoint belongs to an existing contour branch, not a missing
            // bend continuation from the current curve.
            if (EndpointHasSmallConnection(join, candidate, current, curves, topologyRepairTolerance))
                return;

            // Intersect the forward ray of the current line with the backward ray
            // of the candidate line. For a trimmed circular fillet both tangent
            // endpoints lie on opposite sides of the same virtual sharp.
            Point reverseCandidate = candidateDirection * -1.0;
            double determinant = Cross(currentDirection, reverseCandidate);
            if (Math.Abs(determinant) < 1e-10)
                return;

            Point delta = join - finish;
            double currentSetback = Cross(delta, reverseCandidate) / determinant;
            double candidateSetback = Cross(delta, currentDirection) / determinant;
            if (currentSetback <= geometryTolerance || candidateSetback <= geometryTolerance)
                return;

            // PASS 2 already handles micro-gaps. A missing normal bend should have
            // meaningful tangent setbacks on BOTH legs, otherwise this is more
            // likely a projection/noise mismatch than an omitted bend arc.
            if (currentSetback <= topologyRepairTolerance || candidateSetback <= topologyRepairTolerance)
                return;

            double maximumSetback = Math.Max(currentSetback, candidateSetback);
            double symmetryError = Math.Abs(currentSetback - candidateSetback);
            double symmetryTolerance = Math.Max(topologyRepairTolerance, maximumSetback * 0.20);
            if (symmetryError > symmetryTolerance)
                return;

            // Avoid virtual sharps that are many times farther away than both
            // visible tangent lines. This is scale-independent and only removes
            // clearly unrelated line intersections.
            double localLength = Math.Max(currentLength, candidateLength);
            if (maximumSetback > localLength * 2.0 + topologyRepairTolerance)
                return;

            Point sharp = finish + currentDirection * currentSetback;
            result.Add(new MissingBendCandidate
            {
                Curve = candidate,
                UseA = useA,
                JoinPoint = join,
                VirtualSharp = sharp,
                Gap = (join - finish).Length,
                CurrentSetback = currentSetback,
                CandidateSetback = candidateSetback,
                SymmetryError = symmetryError
            });
        }

        private static bool EndpointHasSmallConnection(Point point, Curve owner,
            Curve current, List<Curve> curves, double tolerance)
        {
            foreach (Curve other in curves)
            {
                if (other == null || other == owner || other == current)
                    continue;
                if ((other.A - point).Length <= tolerance ||
                    (other.B - point).Length <= tolerance)
                    return true;
            }
            return false;
        }

        private static Plan BuildLoop(List<Step> loop, object selectedSource, double tolerance)
        {
            var caps = new List<int>();
            for (int i = 0; i < loop.Count; i++)
            {
                Step p = loop[(i + loop.Count - 1) % loop.Count];
                Step c = loop[i];
                Step n = loop[(i + 1) % loop.Count];

                // The end cap itself is a straight thickness edge, but either skin
                // beside it may be LINE or ARC. Use the local traversal tangents so
                // a valid cap between two arcs is recognized correctly.
                if (c.Curve.IsArc) continue;

                Point pd = CurveTangent(p, true);
                Point nd = CurveTangent(n, false);
                Point cd = c.End - c.Start;
                if (Parallel(pd, nd) && Dot(pd, nd) < 0 && Perpendicular(pd, cd))
                    caps.Add(i);
            }
            // The web of a sharp U also connects two antiparallel flanges.
            // End caps must additionally be the minimum, equal thickness pair;
            // the subsequent two-skin validation checks that thickness everywhere.
            if (caps.Count > 0)
            {
                double minimum = caps.Min(i => (loop[i].End - loop[i].Start).Length);
                caps = caps.Where(i => Math.Abs((loop[i].End - loop[i].Start).Length - minimum)
                    <= Math.Max(tolerance * 3, minimum * .01)).ToList();
            }
            if (caps.Count != 2) throw new InvalidOperationException("Khong nhan dang duoc dung hai mep dau ton (L/Z/U/nhieu bac).");
            double thickness = (loop[caps[0]].End - loop[caps[0]].Start).Length;
            double otherThickness = (loop[caps[1]].End - loop[caps[1]].Start).Length;
            double thicknessTol = Math.Max(tolerance * 3, thickness * 0.01);
            if (Math.Abs(thickness - otherThickness) > thicknessTol)
                throw new InvalidOperationException("Chieu day hai dau khong dong nhat; khong tu suy doan bien dang ton.");
            var pathA = BetweenCaps(loop, caps[0], caps[1]);
            var pathB = BetweenCaps(loop, caps[1], caps[0]);
            pathB.Reverse();
            pathB = pathB.Select(s => new Step { Curve = s.Curve, Start = s.End, End = s.Start }).ToList();
            if (NeedsCurvedSection(pathA) || NeedsCurvedSection(pathB))
                return BuildCurvedSection(loop, pathA, pathB, selectedSource, thickness, tolerance,
                    loop[caps[0]].Curve, loop[caps[1]].Curve);
            List<Span> a = MakeSpans(pathA, tolerance), b = MakeSpans(pathB, tolerance);
            if (a.Count < 2 || a.Count != b.Count)
                throw new InvalidOperationException("Hai mat ton khong co chuoi doan tuong ung; co chamfer, nhanh phu hoac thieu canh.");
            var plan = new Plan { Thickness = thickness };
            plan.Boundary.AddRange(loop.Select(s => s.Curve));
            var refsA = MakeReferences(a, loop[caps[0]].Curve, loop[caps[1]].Curve);
            var refsB = MakeReferences(b, loop[caps[0]].Curve, loop[caps[1]].Curve);
            bool selectedOnA = selectedSource != null && pathA.Any(s => ReferenceEquals(s.Curve.Source, selectedSource));
            bool selectedOnB = selectedSource != null && pathB.Any(s => ReferenceEquals(s.Curve.Source, selectedSource));
            if (selectedSource != null && selectedOnA == selectedOnB)
                throw new InvalidOperationException("Hay chon canh doc cua mat trong hoac mat ngoai; khong chon mep day ton.");
            List<Span> selectedSpans = selectedOnB ? b : a;
            List<Reference> selectedRefs = selectedOnB ? refsB : refsA;
            plan.SelectedContour = "locked-to-clicked-edge";
            // Radius policy inherited from the reference macro, evaluated on
            // the INNER radius of each pair, so changing the seed skin cannot
            // reclassify the very same bend. tolerance is 0.01 model mm.
            bool[] tangentJoints = new bool[a.Count];
            for (int j = 1; j < a.Count; j++)
            {
                Curve ca = a[j].BendBefore, cb = b[j].BendBefore;
                double ra = ca == null ? 0 : (ca.A - ca.Center).Length;
                double rb = cb == null ? 0 : (cb.A - cb.Center).Length;
                bool explicitlyPickedArc = (ca != null && ReferenceEquals(ca.Source, selectedSource))
                    || (cb != null && ReferenceEquals(cb.Source, selectedSource));
                tangentJoints[j] = selectedSpans[j].BendBefore != null &&
                    (explicitlyPickedArc || Math.Min(ra, rb) > thickness + 10 * tolerance);
                if (tangentJoints[j]) plan.DimensionArcs.Add(selectedSpans[j].BendBefore);
            }
            var signs = new List<int>();
            for (int i = 0; i < a.Count; i++)
            {
                Point direction = a[i].Direction;
                double separation = Math.Abs(Cross(b[i].Start - a[i].Start, direction));
                if (!Parallel(direction, b[i].Direction) || Dot(direction, b[i].Direction) < 0 ||
                    Math.Abs(separation - thickness) > thicknessTol)
                    throw new InvalidOperationException("Cap mat ton khong song song/cung chieu day tai doan " + (i + 1) + ".");
                if (a[i].BendBefore != null && b[i].BendBefore != null)
                {
                    Curve arcA = a[i].BendBefore, arcB = b[i].BendBefore;
                    double radiusA = (arcA.A - arcA.Center).Length, radiusB = (arcB.A - arcB.Center).Length;
                    if ((arcA.Center - arcB.Center).Length > thicknessTol || Math.Abs(Math.Abs(radiusA - radiusB) - thickness) > thicknessTol)
                        throw new InvalidOperationException("Cap cung R trong/ngoai khong dong tam hoac sai chieu day.");
                }
                // Follow the picked skin, except for the local Z terminal-return
                // envelope below. Never maximize against unrelated profile edges.
                Span dimensionSpan = selectedSpans[i];
                direction = dimensionSpan.Direction;
                bool tangentStart = i > 0 && tangentJoints[i];
                bool tangentEnd = i + 1 < a.Count && tangentJoints[i + 1];
                Reference start = tangentStart
                    ? new Reference
                    {
                        Position = dimensionSpan.Start,
                        First = dimensionSpan.First,
                        Second = dimensionSpan.BendBefore,
                        IsTangent = true
                    } : selectedRefs[i];
                Reference end = tangentEnd
                    ? new Reference
                    {
                        Position = dimensionSpan.End,
                        First = dimensionSpan.Last,
                        Second = selectedSpans[i + 1].BendBefore,
                        IsTangent = true
                    } : selectedRefs[i + 1];
                bool reversal = i > 0 && i + 1 < a.Count &&
                    Cross(selectedSpans[i - 1].Direction, direction) * Cross(direction, selectedSpans[i + 1].Direction) < 0;
                bool terminalEnvelope = false;
                // A reversed step leading into a terminal return uses the farthest
                // face of that return along this span's measurement axis. Its own
                // tip dimension remains on the picked skin. Keep references local
                // to this length: changing selectedRefs would also alter the lip.
                // Only orthogonal, virtual-corner Z bends use this convention;
                // tangent R and oblique bends retain their existing measurement.
                if (reversal && !tangentStart && !tangentEnd &&
                    Perpendicular(selectedSpans[i - 1].Direction, direction) &&
                    Perpendicular(direction, selectedSpans[i + 1].Direction))
                {
                    List<Span> otherSpans = selectedOnB ? a : b;
                    if (i == 1)
                    {
                        Reference candidate = TerminalEnvelopeReference(dimensionSpan, otherSpans[i - 1]);
                        if (Dot(candidate.Position - start.Position, direction) < -tolerance)
                        { start = candidate; terminalEnvelope = true; }
                    }
                    if (i == a.Count - 2)
                    {
                        Reference candidate = TerminalEnvelopeReference(dimensionSpan, otherSpans[i + 1]);
                        if (Dot(candidate.Position - end.Position, direction) > tolerance)
                        { end = candidate; terminalEnvelope = true; }
                    }
                }
                double value = Dot(end.Position - start.Position, direction);
                if (value <= tolerance) throw new InvalidOperationException("Chieu dai phu bi khong hop le.");
                plan.Lengths.Add(new Length
                {
                    Side = dimensionSpan,
                    Start = start,
                    End = end,
                    Value = value,
                    Rule = (i == 0 || i == a.Count - 1 ? "terminal" : reversal ? "Z-reversal" : "intermediate")
                        + (terminalEnvelope ? "/terminal-return-envelope" : "")
                        + ": " + (tangentStart ? "tangent" : start.IsEnd ? "tip" : "virtual")
                        + " -> " + (tangentEnd ? "tangent" : end.IsEnd ? "tip" : "virtual")
                });
                if (i == a.Count - 1) continue;
                Point next = selectedSpans[i + 1].Direction;
                double angle = Math.Acos(Math.Max(-1, Math.Min(1, Dot(direction * -1, next)))) * 180 / Math.PI;
                signs.Add(Math.Sign(Cross(direction, next)));
                if (Math.Abs(angle - 90) > AngularTolerance)
                {
                    plan.Bends.Add(new Bend
                    {
                        First = dimensionSpan.Last,
                        Second = selectedSpans[i + 1].First,
                        Position = selectedRefs[i + 1].Position,
                        Bisector = (next - direction).Unit,
                        AngleDegrees = angle
                    });
                }
            }
            plan.Shape = a.Count == 2 ? "L" : a.Count == 3
                ? (signs[0] == signs[1] ? "U" : "Z")
                : signs.Distinct().Count() > 1 ? "Multi-bend / Z-step / lip" : "Multi-bend / bac / lip";
            plan.Shape += plan.Bends.Count == 0 ? " (90 deg)" : " (non-90 / mixed)";
            if (plan.DimensionArcs.Count > 0) plan.Shape += " + tangent R";
            return plan;
        }
        private static bool NeedsCurvedSection(List<Step> path)
        {
            return path.Where((s, i) => s.Curve.IsArc && (i == 0 || i == path.Count - 1 ||
                path[i - 1].Curve.IsArc || path[i + 1].Curve.IsArc ||
                !Parallel(CurveTangent(path[i - 1], true), CurveTangent(s, false)) ||
                !Parallel(CurveTangent(s, true), CurveTangent(path[i + 1], false)))).Any();
        }
        private static Point CurveTangent(Step s, bool end)
        {
            if (!s.Curve.IsArc) return (s.End - s.Start).Unit;
            Point r = (end ? s.End : s.Start) - s.Curve.Center;
            Point chord = s.End - s.Start;
            Point tangent = new Point(-r.Y, r.X).Unit;
            return Dot(tangent, chord) < 0 ? tangent * -1 : tangent;
        }
        private static bool IsMainCurvedArc(Step s, double thickness, double tolerance)
        {
            return s != null && s.Curve != null && s.Curve.IsArc &&
                (s.Start - s.Curve.Center).Length > 2 * thickness + 10 * tolerance;
        }
        private static bool ForwardTangent(Step first, Step second)
        {
            Point a = CurveTangent(first, true), b = CurveTangent(second, false);
            return Parallel(a, b) && Dot(a, b) > 0;
        }
        private static List<Step> CurvedSupports(List<Step> path, double thickness, double tolerance)
        {
            // V12: a curved web is a CHAIN, not a single privileged arc.
            // Large arcs are preserved in path order.  Small tangent corner
            // fillets may still be suppressed exactly as before so the planner
            // dimensions the envelope rather than every manufacturing fillet.
            var allArcs = path.Where(s => s.Curve.IsArc).ToList();
            if (allArcs.Count == 0)
                throw new InvalidOperationException("Thieu cung chinh tren mot mat ton.");

            foreach (Step s in allArcs)
                if (Math.Abs(s.Curve.SweepAngleRadians) > Math.PI + .001)
                    throw new InvalidOperationException("Chua ho tro cung chinh/bo lon hon 180 do.");

            int mainArcCount = allArcs.Count(s => IsMainCurvedArc(s, thickness, tolerance));
            if (mainArcCount == 0)
                throw new InvalidOperationException("Chuoi cung khong co cung tao hinh chinh tach biet voi bo goc.");

            var result = new List<Step>();
            for (int i = 0; i < path.Count; i++)
            {
                Step s = path[i];
                if (s.Curve.IsArc && !IsMainCurvedArc(s, thickness, tolerance))
                {
                    // A suppressed small arc must be an ordinary tangent fillet
                    // between two physical neighbours.  Never suppress an end arc
                    // or a non-tangent arc just to make the topology fit.
                    if (i == 0 || i == path.Count - 1 ||
                        !ForwardTangent(path[i - 1], s) ||
                        !ForwardTangent(s, path[i + 1]))
                        throw new InvalidOperationException("Bo noi cung chinh/canh be khong tiep tuyen hoac thieu canh.");
                    continue;
                }

                Step last = result.LastOrDefault();
                if (last != null && !last.Curve.IsArc && !s.Curve.IsArc &&
                    Parallel(last.End - last.Start, s.End - s.Start))
                {
                    if ((last.End - s.Start).Length > tolerance ||
                        Dot(last.End - last.Start, s.End - s.Start) <= 0)
                        throw new InvalidOperationException("Canh cong re nhanh/bo giua hai line song song.");
                    result[result.Count - 1] = new Step
                    {
                        Curve = last.Curve,
                        Start = last.Start,
                        End = s.End
                    };
                    continue;
                }

                // Consecutive large arcs are accepted only as a real tangent
                // chain with a common endpoint.  This is the generic replacement
                // for the old "pick one largest main arc" rule.
                if (last != null && last.Curve.IsArc && s.Curve.IsArc)
                {
                    double jointTol = Math.Max(tolerance * 3, thickness * .01);
                    if ((last.End - s.Start).Length > jointTol)
                        throw new InvalidOperationException("Hai cung chinh lien tiep khong chung nut.");
                    if (!ForwardTangent(last, s))
                        throw new InvalidOperationException("Hai cung chinh lien tiep khong tiep tuyen.");
                }

                result.Add(s);
            }

            if (result.Count < 2)
                throw new InvalidOperationException("Cung khong co canh be de do phu bi.");
            if (!result.Any(s => s.Curve.IsArc))
                throw new InvalidOperationException("Khong con cung tao hinh sau khi loc bo goc.");

            Debug.WriteLine("[DIM MAT CAT CURVED CHAIN] supports=" + result.Count
                + ", arcs=" + result.Count(s => s.Curve.IsArc)
                + ", sourceArcs=" + allArcs.Count);
            return result;
        }
        private static Point CurvedIntersection(Step a, Step b, double tolerance)
        {
            if (!a.Curve.IsArc && !b.Curve.IsArc)
                return Intersection(new Span { Start = a.Start, End = a.End }, new Span { Start = b.Start, End = b.End });

            if (a.Curve.IsArc && b.Curve.IsArc)
            {
                double jointTol = Math.Max(tolerance * 3, 1e-10);
                if ((a.End - b.Start).Length > jointTol)
                    throw new InvalidOperationException("Hai cung chinh lien tiep khong chung nut.");
                if (!ForwardTangent(a, b))
                    throw new InvalidOperationException("Hai cung chinh lien tiep khong tiep tuyen.");
                // Preserve the real common node.  Averaging only removes tiny
                // projected-coordinate noise and does not invent a remote join.
                return (a.End + b.Start) * .5;
            }

            Step arc = a.Curve.IsArc ? a : b, line = a.Curve.IsArc ? b : a;
            Point direction = (line.End - line.Start).Unit;
            Point foot = line.Start + direction * Dot(arc.Curve.Center - line.Start, direction);
            double r = (arc.Start - arc.Curve.Center).Length;
            double square = r * r - Dot(foot - arc.Curve.Center, foot - arc.Curve.Center);
            if (square < -tolerance * tolerance)
                throw new InvalidOperationException("Canh be keo dai khong giao cung chinh.");
            double d = Math.Sqrt(Math.Max(0, square));
            Point p = foot + direction * d, q = foot - direction * d;
            Point local = (a.End + b.Start) * .5;
            if (d > tolerance && Math.Abs((p - local).Length - (q - local).Length) < tolerance)
                throw new InvalidOperationException("Hai nghiem giao line/cung mo ho.");
            return (p - local).Length <= (q - local).Length ? p : q;
        }
        private static Plan BuildCurvedSection(List<Step> loop, List<Step> a, List<Step> b, object seed,
            double thickness, double tolerance, Curve capA, Curve capB)
        {
            bool onA = a.Any(s => ReferenceEquals(s.Curve.Source, seed)),
                 onB = b.Any(s => ReferenceEquals(s.Curve.Source, seed));
            if (onA == onB)
                throw new InvalidOperationException("Chon canh/cung tren mat ton, khong chon mep day.");

            var sa = CurvedSupports(a, thickness, tolerance);
            var sb = CurvedSupports(b, thickness, tolerance);
            if (sa.Count != sb.Count)
                throw new InvalidOperationException("Hai mat cong khong co nhanh tuong ung.");

            double tol = Math.Max(thickness * .01, 3 * tolerance);
            for (int i = 0; i < sa.Count; i++)
            {
                Step x = sa[i], y = sb[i];
                if (x.Curve.IsArc != y.Curve.IsArc)
                    throw new InvalidOperationException("Sai cap cung/line hai mat cong.");

                if (x.Curve.IsArc)
                {
                    double rx = (x.Start - x.Curve.Center).Length;
                    double ry = (y.Start - y.Curve.Center).Length;
                    if ((x.Curve.Center - y.Curve.Center).Length > tol ||
                        Math.Abs(Math.Abs(rx - ry) - thickness) > tol)
                        throw new InvalidOperationException("Cap cung chinh khong dong tam/cung chieu day.");

                    // The paired skins must traverse corresponding arcs in the
                    // same physical direction after pathB has been reversed.
                    Point tx = CurveTangent(x, false), ty = CurveTangent(y, false);
                    if (!Parallel(tx, ty) || Dot(tx, ty) <= 0)
                        throw new InvalidOperationException("Cap cung hai mat khong cung huong.");
                }
                else if (!Parallel(x.End - x.Start, y.End - y.Start) ||
                    Dot(x.End - x.Start, y.End - y.Start) <= 0 ||
                    Math.Abs(Math.Abs(Cross(y.Start - x.Start, (x.End - x.Start).Unit)) - thickness) > tol)
                    throw new InvalidOperationException("Canh be hai mat khong song song/cung chieu day.");
            }

            var supports = onA ? sa : sb;
            int selectedArcCount = supports.Count(s => s.Curve.IsArc);
            var curved = new CurvedSection();
            var plan = new Plan
            {
                Curved = curved,
                Thickness = thickness,
                SelectedContour = "locked-to-clicked-edge",
                Shape = selectedArcCount > 1
                    ? "Curved multi-arc chain + flanges / sketch envelope"
                    : "Curved web + flanges / sketch envelope"
            };
            plan.Boundary.AddRange(loop.Select(s => s.Curve));

            curved.Nodes.Add(new Reference
            {
                Position = supports[0].Start,
                First = supports[0].Curve,
                Second = capA,
                IsEnd = true
            });
            for (int i = 1; i < supports.Count; i++)
                curved.Nodes.Add(new Reference
                {
                    Position = CurvedIntersection(supports[i - 1], supports[i], tolerance),
                    First = supports[i - 1].Curve,
                    Second = supports[i].Curve
                });
            curved.Nodes.Add(new Reference
            {
                Position = supports.Last().End,
                First = supports.Last().Curve,
                Second = capB,
                IsEnd = true
            });

            for (int i = 0; i < supports.Count; i++)
            {
                Step s = supports[i];
                curved.Supports.Add(s.Curve);
                curved.SupportMids.Add(new Point());
                curved.SupportArcLengths.Add(0.0);

                Point start = curved.Nodes[i].Position,
                      end = curved.Nodes[i + 1].Position;
                if (s.Curve.IsArc)
                {
                    Point r1 = start - s.Curve.Center,
                          r2 = end - s.Curve.Center;
                    double sweep = Math.Atan2(Cross(r1, r2), Dot(r1, r2));
                    double sourceDirection = Math.Sign(Cross(
                        s.Start - s.Curve.Center,
                        s.End - s.Curve.Center));
                    if (sourceDirection != 0 && Math.Sign(sweep) != sourceDirection)
                        throw new InvalidOperationException("Cung phu bi bi dao chieu; khong noi sang nghiem xa.");

                    double radius = r1.Length;
                    if (radius <= tolerance)
                        throw new InvalidOperationException("Ban kinh cung phu bi khong hop le.");
                    double arcLength = radius * Math.Abs(sweep);
                    double theta = Math.Atan2(r1.Y, r1.X) + sweep * .5;
                    Point mid = s.Curve.Center +
                        new Point(Math.Cos(theta), Math.Sin(theta)) * radius;

                    curved.SupportMids[i] = mid;
                    curved.SupportArcLengths[i] = arcLength;

                    // Preserve the legacy fields for the one-arc path and for
                    // callers that only need a representative arc.
                    if (curved.Arc == null)
                    {
                        curved.Arc = s.Curve;
                        curved.Start = start;
                        curved.End = end;
                        curved.Mid = mid;
                        curved.ArcLength = arcLength;
                    }
                }
                else
                {
                    double value = Dot(end - start, (s.End - s.Start).Unit);
                    if (value <= tolerance)
                        throw new InvalidOperationException("Chieu dai canh be khong hop le.");
                    plan.Lengths.Add(new Length
                    {
                        Side = new Span
                        {
                            First = s.Curve,
                            Last = s.Curve,
                            Start = s.Start,
                            End = s.End
                        },
                        Start = curved.Nodes[i],
                        End = curved.Nodes[i + 1],
                        Value = value,
                        Rule = "curved-web/flange-envelope"
                    });
                }

                if (i > 0)
                {
                    // A curved chain may be split by SOLIDWORKS into several real
                    // circular edges.  At a smooth ARC->ARC or LINE->ARC transition
                    // the two travelling tangents point in the same direction.  The
                    // old interior-angle formula turns that smooth continuation into
                    // 180 degrees and later asks SOLIDWORKS to create a meaningless
                    // angular dimension.  Treat smooth tangent joints as topology
                    // continuity, not as bends to dimension.
                    Point previousTangent = CurveTangent(supports[i - 1], true);
                    Point nextTangent = CurveTangent(s, false);
                    bool smoothTangent = Parallel(previousTangent, nextTangent)
                        && Dot(previousTangent, nextTangent) > 0;

                    if (smoothTangent)
                    {
                        curved.Angles.Add(0);
                        Debug.WriteLine(
                            "[DIM MAT CAT CURVED JOINT] index=" + (i - 1)
                            + ", smoothTangent=True, angularDim=False");
                    }
                    else
                    {
                        double angle = Math.Acos(Math.Max(-1, Math.Min(1,
                            Dot(previousTangent * -1, nextTangent)))) * 180 / Math.PI;
                        bool ordinaryRightAngle = Math.Abs(angle - 90) <= AngularTolerance;
                        curved.Angles.Add(ordinaryRightAngle ? 0 : angle);
                        Debug.WriteLine(
                            "[DIM MAT CAT CURVED JOINT] index=" + (i - 1)
                            + ", smoothTangent=False, angle=" + angle.ToString("0.######")
                            + ", angularDim=" + (!ordinaryRightAngle));
                    }
                }
            }

            Debug.WriteLine("[DIM MAT CAT CURVED CHAIN] selected supports=" + curved.Supports.Count
                + ", selected arcs=" + selectedArcCount
                + ", lengths=" + plan.Lengths.Count);
            return plan;
        }
        private static Reference TerminalEnvelopeReference(Span measured, Span terminal)
        {
            return new Reference
            {
                Position = Intersection(measured, terminal),
                First = measured.First,
                Second = terminal.First
            };
        }
        private static List<Step> BetweenCaps(List<Step> loop, int first, int last)
        {
            var result = new List<Step>();
            for (int i = (first + 1) % loop.Count; i != last; i = (i + 1) % loop.Count) result.Add(loop[i]);
            return result;
        }
        private static List<Span> MakeSpans(List<Step> path, double tolerance)
        {
            var result = new List<Span>();
            for (int i = 0; i < path.Count; i++)
            {
                Step s = path[i];
                if (s.Curve.IsArc)
                {
                    if (Math.Abs(s.Curve.SweepAngleRadians) > Math.PI + .001)
                        throw new InvalidOperationException("Cung lon hon 180 do khong phai fillet goc be thong thuong.");
                    if (i == 0 || i == path.Count - 1 || path[i - 1].Curve.IsArc || path[i + 1].Curve.IsArc)
                        throw new InvalidOperationException("Cung khong nam giua hai canh thang; can kiem tra rieng bien dang cong.");
                    Point radial1 = s.Start - s.Curve.Center, radial2 = s.End - s.Curve.Center;
                    if (Math.Abs(radial1.Length - radial2.Length) > tolerance * 3 ||
                        !Perpendicular(radial1, path[i - 1].End - path[i - 1].Start) ||
                        !Perpendicular(radial2, path[i + 1].End - path[i + 1].Start))
                        throw new InvalidOperationException("Cung R khong tiep tuyen voi hai doan ke ben.");
                    continue;
                }
                Span previous = result.Count == 0 ? null : result[result.Count - 1];
                if (previous != null && i > 0 && !path[i - 1].Curve.IsArc &&
                    Parallel(previous.Direction, s.End - s.Start) && Dot(previous.Direction, s.End - s.Start) > 0)
                {
                    previous.End = s.End;
                    previous.Last = s.Curve;
                }
                else result.Add(new Span
                {
                    First = s.Curve,
                    Last = s.Curve,
                    Start = s.Start,
                    End = s.End,
                    BendBefore = i > 0 && path[i - 1].Curve.IsArc ? path[i - 1].Curve : null
                });
            }
            return result;
        }
        private static List<Reference> MakeReferences(List<Span> spans, Curve capA, Curve capB)
        {
            var result = new List<Reference> { new Reference { Position = spans[0].Start, First = spans[0].First, Second = capA, IsEnd = true } };
            for (int i = 1; i < spans.Count; i++)
                result.Add(new Reference { Position = Intersection(spans[i - 1], spans[i]), First = spans[i - 1].Last, Second = spans[i].First });
            result.Add(new Reference { Position = spans[spans.Count - 1].End, First = spans[spans.Count - 1].Last, Second = capB, IsEnd = true });
            return result;
        }
    }
}
