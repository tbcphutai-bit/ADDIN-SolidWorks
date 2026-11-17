using System;
using System.Globalization;

namespace ADDIN.Commands.CircleFaceTubeLayout
{
    // Existing contact mode remains the default for automatic layouts.
    internal enum SmallEndSpacingMode { TangentContact, EqualPitch }

    internal struct RailStation
    {
        public readonly Point3 Point, Normal;
        public RailStation(Point3 point, Point3 normal) { Point = point; Normal = normal; }
    }

    internal sealed class TubeLayout
    {
        public readonly TubeAxis[] Axes;
        public readonly double[] SmallStations;
        public double LargePitch, Lift, MinimumGap, MinimumContact, MaximumContact;
        public double MinimumSheetEstimate = double.NaN;
        public double LargeLeftClearance, LargeRightClearance, FirstLargeStation;
        public string Issue;
        public SmallEndSpacingMode SmallMode = SmallEndSpacingMode.TangentContact;
        public double SmallPitch, SmallLeftClearance, SmallRightClearance, SmallRailLength;
        public int MinimumGapPairA, MinimumGapPairB, InterferingPairCount;
        public int FirstInterferingPairA, FirstInterferingPairB;
        public int SheetClashCount = 0, SheetInconclusiveCount = 0;
        public TubeLayout(int count)
        {
            Axes = new TubeAxis[count];
            SmallStations = new double[count];
        }
    }

    internal static class TubeLayoutSolver
    {
        public const double Mm = 0.001;
        public const int MaximumCount = 200;
        private const double TargetGap = 0.005 * Mm;
        internal const double ContactTolerance = 0.05 * Mm;
        private const double StationTolerance = 0.001 * Mm;
        private const double SymmetryTolerance = 0.02 * Mm;

        public static TubeLayout SolveAutomatic(Func<double, RailStation> small, double smallLength,
            Func<double, RailStation> large, double largeLength, double diameter, double margin, double lift,
            Action<string> log, int countLimit = MaximumCount)
        {
            if (!(largeLength > diameter + 2 * margin))
                throw new LayoutException("INFEASIBLE: large end has insufficient length for the requested margins.");

            // Spacing along the rail is not a bound on finite-cylinder contact.
            // Search the supported counts in order and verify each candidate in 3D.
            Action<string> quiet = _ => { };
            for (int count = countLimit; count >= 2; count--)
            {
                TubeLayout candidate = SolveCore(small, smallLength, large, largeLength, count, diameter, margin, lift, quiet, true, SmallEndSpacingMode.TangentContact);
                if (candidate == null)
                {
                    log(F("AUTO_COUNT rejected={0} reason=no_small_end_contact_sequence", count));
                    continue;
                }

                if (candidate.Issue != null)
                {
                    log(F("AUTO_COUNT rejected={0} reason={1}", count, candidate.Issue));
                    continue;
                }

                log(F("AUTO_COUNT selected={0} lift_mm={1:F6} search_limit={2}", count, lift / Mm, MaximumCount));
                return candidate;
            }
            throw new LayoutException("INFEASIBLE: no verified small-end contact sequence was found in automatic mode.");
        }

        public static TubeLayout Solve(Func<double, RailStation> small, double smallLength,
            Func<double, RailStation> large, double largeLength, int count, double diameter,
            double margin, double lift, Action<string> log, bool verify = true,
            SmallEndSpacingMode smallMode = SmallEndSpacingMode.TangentContact)
        {
            return SolveCore(small, smallLength, large, largeLength, count, diameter, margin, lift, log, verify, smallMode)
                ?? throw new LayoutException(smallMode == SmallEndSpacingMode.EqualPitch
                    ? "INFEASIBLE: equal-pitch Custom layout could not fit on the small end."
                    : "INFEASIBLE: no small-end contact sequence fits within the rail.");
        }

        private static TubeLayout SolveCore(Func<double, RailStation> small, double smallLength,
            Func<double, RailStation> large, double largeLength, int count, double diameter,
            double margin, double lift, Action<string> log, bool verify, SmallEndSpacingMode smallMode)
        {
            if (smallMode != SmallEndSpacingMode.TangentContact && smallMode != SmallEndSpacingMode.EqualPitch)
                throw new LayoutException("Unsupported small-end spacing mode.");
            log(F("SPACING_MODE mode={0} count={1} small_length_mm={2:F6} large_length_mm={3:F6} diameter_mm={4:F3} margin_mm={5:F3}",
                smallMode, count, smallLength / Mm, largeLength / Mm, diameter / Mm, margin / Mm));
            double radius = diameter / 2;
            if (count < 2 || !(diameter > 0) || !(smallLength > 0) || !(margin >= 0) || !(lift >= 0))
                throw new LayoutException("Invalid layout dimensions.");
            double first = radius + margin, last = radius + margin;
            if (!(largeLength > first + last))
                throw new LayoutException("INFEASIBLE: large end has insufficient length for the requested margins.");
            if (smallMode == SmallEndSpacingMode.EqualPitch && !(smallLength > diameter + 2 * margin))
                throw new LayoutException("INFEASIBLE: insufficient arc length for requested cap margins.");
            Point3 left = large(0).Point, right = large(largeLength).Point;
            double delta = Math.Min(0.01 * Mm, largeLength / 100);
            Point3 leftInward = (large(delta).Point - left).Unit();
            Point3 rightInward = (large(largeLength - delta).Point - right).Unit();
            TubeLayout result = null;
            for (int pass = 0; pass < 6; pass++)
            {
                result = smallMode == SmallEndSpacingMode.EqualPitch
                    ? SolveEqualStations(small, smallLength, large, largeLength, count, diameter,
                        margin, first, last, lift, log)
                    : SolveStations(small, smallLength, large, largeLength, count, diameter, first, last, lift, log);
                if (result == null) return null;
                result.LargeLeftClearance = CapClearance(result.Axes[0], left, leftInward, radius);
                result.LargeRightClearance = CapClearance(result.Axes[count - 1], right, rightInward, radius);
                double leftError = margin - result.LargeLeftClearance, rightError = margin - result.LargeRightClearance;
                log(F("LARGE_EDGE pass={0} left_clearance_mm={1:F6} right_clearance_mm={2:F6} left_center_s_mm={3:F6} right_center_margin_s_mm={4:F6}",
                    pass + 1, result.LargeLeftClearance / Mm, result.LargeRightClearance / Mm, first / Mm, last / Mm));
                if (Math.Abs(leftError) <= SymmetryTolerance && Math.Abs(rightError) <= SymmetryTolerance)
                {
                    if (verify) VerifyTubes(result, diameter, small, log);
                    return result;
                }
                first = Math.Max(0, first + leftError);
                last = Math.Max(0, last + rightError);
            }
            AddIssue(result, "Large-end 3D cap margins could not be balanced.");
            if (verify) VerifyTubes(result, diameter, small, log);
            return result;
        }

        public static void VerifyExact(TubeLayout layout, Func<double, RailStation> small,
            Func<double, RailStation> large, double largeLength, double diameter, double margin, Action<string> log)
        {
            double radius = diameter / 2;
            for (int i = 0; i < layout.Axes.Length; i++)
            {
                RailStation a = small(layout.SmallStations[i]);
                RailStation b = large(layout.FirstLargeStation + i * layout.LargePitch);
                var exact = new TubeAxis(a.Point + a.Normal * (radius + layout.Lift),
                    b.Point + b.Normal * (radius + layout.Lift));
                log(F("EXACT_STATION index={0} A_adjustment_mm={1:F6} B_adjustment_mm={2:F6}",
                    i + 1, (exact.A - layout.Axes[i].A).Length / Mm, (exact.B - layout.Axes[i].B).Length / Mm));
                layout.Axes[i] = exact;
            }
            double delta = Math.Min(0.01 * Mm, largeLength / 100);
            Point3 left = large(0).Point, right = large(largeLength).Point;
            layout.LargeLeftClearance = CapClearance(layout.Axes[0], left, (large(delta).Point - left).Unit(), radius);
            layout.LargeRightClearance = CapClearance(layout.Axes[layout.Axes.Length - 1], right,
                (large(largeLength - delta).Point - right).Unit(), radius);
            log(F("LARGE_EDGE_EXACT left_mm={0:F6} right_mm={1:F6} pitch_mm={2:F6}",
                layout.LargeLeftClearance / Mm, layout.LargeRightClearance / Mm, layout.LargePitch / Mm));
            if (Math.Abs(layout.LargeLeftClearance - margin) > SymmetryTolerance ||
                Math.Abs(layout.LargeRightClearance - margin) > SymmetryTolerance)
                AddIssue(layout, "Exact large-end cap clearance is outside tolerance.");
            if (layout.SmallMode == SmallEndSpacingMode.EqualPitch)
            {
                double ds = Math.Min(0.01 * Mm, layout.SmallRailLength / 100);
                Point3 start = small(0).Point, end = small(layout.SmallRailLength).Point;
                layout.SmallLeftClearance = CapClearance(layout.Axes[0], start,
                    (small(ds).Point - start).Unit(), radius, false);
                layout.SmallRightClearance = CapClearance(layout.Axes[layout.Axes.Length - 1], end,
                    (small(layout.SmallRailLength - ds).Point - end).Unit(), radius, false);
                log(F("SMALL_EDGE_EXACT left_mm={0:F6} right_mm={1:F6} pitch_mm={2:F6}",
                    layout.SmallLeftClearance / Mm, layout.SmallRightClearance / Mm, layout.SmallPitch / Mm));
                if (Math.Abs(layout.SmallLeftClearance - margin) > SymmetryTolerance ||
                    Math.Abs(layout.SmallRightClearance - margin) > SymmetryTolerance)
                    AddIssue(layout, "Exact small-end cap margins are outside tolerance.");
            }
            VerifyTubes(layout, diameter, small, log);
        }

        private static double CapClearance(TubeAxis axis, Point3 corner, Point3 inward, double radius, bool largeCap = true)
        {
            double along = inward.Dot(axis.Direction);
            double projection = radius * Math.Sqrt(Math.Max(0, 1 - along * along));
            return ((largeCap ? axis.B : axis.A) - corner).Dot(inward) - projection;
        }

        private static TubeLayout SolveEqualStations(Func<double, RailStation> small, double smallLength,
            Func<double, RailStation> large, double largeLength, int count, double diameter,
            double margin, double firstLarge, double lastLarge, double lift, Action<string> log)
        {
            double radius = diameter / 2;
            double largePitch = (largeLength - firstLarge - lastLarge) / (count - 1);
            if (!(largePitch > 0) || !(smallLength > 2 * margin))
                throw new LayoutException("INFEASIBLE: insufficient rail length for equal spacing.");
            var largeEnds = new Point3[count];
            for (int i = 0; i < count; i++)
            {
                RailStation station = large(firstLarge + i * largePitch);
                largeEnds[i] = station.Point + station.Normal * (radius + lift);
            }

            double first = radius + margin, last = radius + margin;
            TubeLayout result = null;
            Point3 smallStart = small(0).Point, smallEnd = small(smallLength).Point;
            double ds = Math.Min(0.01 * Mm, smallLength / 100);
            Point3 leftInward = (small(ds).Point - smallStart).Unit();
            Point3 rightInward = (small(smallLength - ds).Point - smallEnd).Unit();
            for (int pass = 0; pass < 8; pass++)
            {
                if (!(smallLength > first + last))
                    throw new LayoutException("INFEASIBLE: requested small-end margins leave no room for tube centers.");
                double pitch = (smallLength - first - last) / (count - 1);
                result = new TubeLayout(count)
                {
                    SmallMode = SmallEndSpacingMode.EqualPitch,
                    SmallPitch = pitch,
                    SmallRailLength = smallLength,
                    LargePitch = largePitch,
                    FirstLargeStation = firstLarge,
                    Lift = lift
                };
                for (int i = 0; i < count; i++)
                {
                    double s = first + i * pitch;
                    result.SmallStations[i] = s;
                    RailStation station = small(s);
                    result.Axes[i] = new TubeAxis(station.Point + station.Normal * (radius + lift), largeEnds[i]);
                }
                result.SmallLeftClearance = CapClearance(result.Axes[0], smallStart, leftInward, radius, false);
                result.SmallRightClearance = CapClearance(result.Axes[count - 1], smallEnd, rightInward, radius, false);
                double le = margin - result.SmallLeftClearance, re = margin - result.SmallRightClearance;
                log(F("SMALL_EQUAL pass={0} count={1} pitch_mm={2:F6} station_left_mm={3:F6} station_right_mm={4:F6} cap_left_mm={5:F6} cap_right_mm={6:F6}",
                    pass + 1, count, pitch / Mm, first / Mm, last / Mm,
                    result.SmallLeftClearance / Mm, result.SmallRightClearance / Mm));
                if (Math.Abs(le) <= SymmetryTolerance && Math.Abs(re) <= SymmetryTolerance)
                    return result;
                first = Math.Max(0, first + le);
                last = Math.Max(0, last + re);
            }
            AddIssue(result, "Could not balance small-end 3D cap margins in equal-pitch mode.");
            return result;
        }

        private static TubeLayout SolveStations(Func<double, RailStation> small, double smallLength,
            Func<double, RailStation> large, double largeLength, int count, double diameter,
            double firstLarge, double lastLarge, double lift, Action<string> log)
        {
            double radius = diameter / 2;
            double pitch = (largeLength - firstLarge - lastLarge) / (count - 1);
            if (pitch <= 0)
                throw new LayoutException("INFEASIBLE: large end has insufficient length for the requested margins.");

            var ends = new Point3[count];
            for (int i = 0; i < count; i++)
            {
                RailStation station = large(firstLarge + i * pitch);
                ends[i] = station.Point + station.Normal * (radius + lift);
                log(F("LARGE_STATION index={0} s_mm={1:F6} point_m={2} normal={3}",
                    i + 1, (firstLarge + i * pitch) / Mm, station.Point, station.Normal));
            }
            log(F("SOLVER count={0} diameter_mm={1:F4} lift_mm={2:F4} target_gap_mm={3:F4} contact_tol_mm={4:F4} station_tol_mm={5:F4}",
                count, diameter / Mm, lift / Mm, TargetGap / Mm, ContactTolerance / Mm, StationTolerance / Mm));

            TubeAxis At(int index, double s)
            {
                RailStation station = small(s);
                return new TubeAxis(station.Point + station.Normal * (radius + lift), ends[index]);
            }

            bool March(double first, TubeLayout result)
            {
                result.SmallStations[0] = first;
                result.Axes[0] = At(0, first);
                for (int i = 1; i < count; i++)
                {
                    TubeAxis previous = result.Axes[i - 1];
                    TubeAxis nearEnd = previous.StartSection(diameter);
                    bool Separated(double station)
                    {
                        TubeAxis next = At(i, station);
                        return TubeDistance.Between(nearEnd, next.StartSection(diameter), radius).Lower >= TargetGap &&
                            TubeDistance.Between(previous, next, radius).Lower >= TargetGap;
                    }
                    double from = result.SmallStations[i - 1], lo = from, hi = from;
                    bool bracketed = false;
                    // Advance only to the first locally separated interval; a fold or
                    // inconsistent pairing must not jump to a distant branch of the rail.
                    for (int step = 1; step <= 8 && hi < smallLength; step++)
                    {
                        hi = Math.Min(smallLength, from + step * diameter / 4);
                        if (Separated(hi))
                        {
                            bracketed = true;
                            break;
                        }
                        lo = hi;
                    }
                    if (!bracketed) return false;
                    while (hi - lo > StationTolerance)
                    {
                        double mid = (lo + hi) / 2;
                        if (Separated(mid))
                            hi = mid;
                        else
                            lo = mid;
                    }
                    result.SmallStations[i] = hi;
                    result.Axes[i] = At(i, hi);
                }
                return true;
            }

            var resultAtFirst = new TubeLayout(count);
            if (!March(0, resultAtFirst))
                return null;
            var best = resultAtFirst;
            double low = 0, high = smallLength / 2;
            for (int i = 0; i < 24; i++)
            {
                double first = (low + high) / 2;
                var trial = new TubeLayout(count);
                bool fits = March(first, trial);
                double residual = fits ? first + trial.SmallStations[count - 1] - smallLength : double.PositiveInfinity;
                log(F("CENTER iteration={0} start_mm={1:F6} residual_mm={2:F6}", i + 1, first / Mm, residual / Mm));
                if (fits && Math.Abs(residual) < Math.Abs(best.SmallStations[0] + best.SmallStations[count - 1] - smallLength))
                    best = trial;
                if (Math.Abs(residual) <= SymmetryTolerance) break;
                if (residual > 0) high = first; else low = first;
            }
            best.LargePitch = pitch;
            best.FirstLargeStation = firstLarge;
            best.SmallRailLength = smallLength;
            best.Lift = lift;
            double lastMargin = smallLength - best.SmallStations[count - 1];
            log(F("SMALL_CENTER_MARGINS left_mm={0:F6} right_mm={1:F6} nominal_overhang_left_mm={2:F6} nominal_overhang_right_mm={3:F6}",
                best.SmallStations[0] / Mm, lastMargin / Mm,
                Math.Max(0, radius - best.SmallStations[0]) / Mm, Math.Max(0, radius - lastMargin) / Mm));
            if (Math.Abs(best.SmallStations[0] - lastMargin) > SymmetryTolerance)
                AddIssue(best, "Small-end center margins could not be balanced.");
            return best;
        }

        private static void VerifyTubes(TubeLayout result, double diameter, Func<double, RailStation> small, Action<string> log)
        {
            result.MinimumGap = result.MinimumContact = double.PositiveInfinity;
            result.MaximumContact = 0;
            result.MinimumGapPairA = result.MinimumGapPairB = 0;
            result.InterferingPairCount = 0;
            result.FirstInterferingPairA = result.FirstInterferingPairB = 0;
            for (int i = 0; i < result.Axes.Length; i++)
            {
                TubeAxis a = result.Axes[i];
                RailStation station = small(result.SmallStations[i]);
                double largeStation = result.FirstLargeStation + i * result.LargePitch;
                log(F("AXIS index={0} A_m={1} B_m={2} length_mm={3:F6} small_s_mm={4:F6} large_s_mm={5:F6} rail_m={6} normal={7}",
                    i + 1, a.A, a.B, a.Length / Mm, result.SmallStations[i] / Mm, largeStation / Mm, station.Point, station.Normal));
                for (int j = i + 1; j < result.Axes.Length; j++)
                {
                    TubeAxis b = result.Axes[j];
                    Gap full = TubeDistance.Between(a, b, diameter / 2);
                    if (full.Lower < result.MinimumGap)
                    {
                        result.MinimumGap = full.Lower;
                        result.MinimumGapPairA = i + 1;
                        result.MinimumGapPairB = j + 1;
                    }
                    log(F("PAIR a={0} b={1} lower_mm={2:F6} upper_mm={3:F6}", i + 1, j + 1, full.Lower / Mm, full.Upper / Mm));
                    if (full.Lower <= TubeDistance.Resolution)
                    {
                        result.InterferingPairCount++;
                        if (result.FirstInterferingPairA == 0)
                        {
                            result.FirstInterferingPairA = i + 1;
                            result.FirstInterferingPairB = j + 1;
                        }
                        log(F("INTERFERING_PAIR a={0} b={1} lower_mm={2:F6} upper_mm={3:F6}",
                            i + 1, j + 1, full.Lower / Mm, full.Upper / Mm));
                    }
                    if (j != i + 1) continue;

                    Gap contact = TubeDistance.Between(a.StartSection(diameter), b.StartSection(diameter), diameter / 2);
                    result.MinimumContact = Math.Min(result.MinimumContact, contact.Lower);
                    result.MaximumContact = Math.Max(result.MaximumContact, contact.Upper);
                    log(F("SMALL_CONTACT a={0} b={1} lower_mm={2:F6} upper_mm={3:F6}", i + 1, j + 1,
                        contact.Lower / Mm, contact.Upper / Mm));
                    if (result.SmallMode == SmallEndSpacingMode.TangentContact &&
                        contact.Upper > ContactTolerance)
                        AddIssue(result, "The small-end gaps exceed the contact tolerance.");
                }
            }
            if (result.InterferingPairCount > 0)
            {
                string message = result.SmallMode == SmallEndSpacingMode.EqualPitch
                    ? F("Equal-pitch placement causes 3D tube interference ({0} pair(s), first {1}-{2}, min full gap {3:F4} mm); reduce Custom count, diameter or margin.",
                        result.InterferingPairCount, result.FirstInterferingPairA, result.FirstInterferingPairB, result.MinimumGap / Mm)
                    : F("Full-length tube separation could not be proven ({0} pair(s), first {1}-{2}, min full gap {3:F4} mm).",
                        result.InterferingPairCount, result.FirstInterferingPairA, result.FirstInterferingPairB, result.MinimumGap / Mm);
                AddIssue(result, message);
            }
        }

        internal static void AddIssue(TubeLayout layout, string issue)
        {
            if (layout == null || string.IsNullOrWhiteSpace(issue)) return;
            if (string.IsNullOrWhiteSpace(layout.Issue)) layout.Issue = issue;
            else if (!layout.Issue.Contains(issue)) layout.Issue += " " + issue;
        }

        internal static string F(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);
    }

    internal sealed class LayoutException : InvalidOperationException
    {
        public LayoutException(string message) : base(message) { }
    }
}
