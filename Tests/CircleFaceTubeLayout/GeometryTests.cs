using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands.CircleFaceTubeLayout;
using static ADDIN.Commands.CircleFaceTubeLayout.TubeLayoutSolver;

internal static class GeometryTests
{
    private const double SmallRadius = 3.247, LargeRadius = 4.32316;
    private const double SmallLength = 0.81135, LargeLength = 1.42197, Diameter = 0.020;
    private static readonly Action<string> Quiet = _ => { };

    public static int Main()
    {
        try
        {
            TestCylinders();
            TestRails();
            TestEqualPitchCustom();
            TestV3OverhangGeometry();
            var flat = Layout(41, 0, false, Console.WriteLine);
            AssertLayout(flat, 41);
            Require(Math.Abs(flat.LargePitch / Mm - 34.79925) < 0.01, "reference large-end pitch");
            Require(flat.SmallStations[0] < Diameter / 2, "small-end overhang");
            Summary("flat", flat);
            var auto = Layout(null, 0.07, false, Quiet);
            AssertLayout(auto, 41);
            ExpectFailure(() => Layout(42, 0.07, false, Quiet), "42 tubes exceed the small-end 3D fit");
            Summary("auto_twisted", auto);
            TestAutoDifferentCount();
            var forty = Layout(40, 0, false, Quiet);
            AssertLayout(forty, 40);
            Summary("base40", forty);
            var twisted = Layout(41, 0.07, false, Quiet);
            AssertLayout(twisted, 41);
            Summary("twisted", twisted);
            var flipped = Layout(41, 0.07, true, Quiet);
            AssertLayout(flipped, 41);
            Summary("twisted_flip", flipped);
            Require(flipped.Axes[20].A.Z < twisted.Axes[20].A.Z, "flip offsets on opposite sides");
            ExpectFailure(() => Layout(50, 0, false, Quiet), "insufficient small-end room");
            ExpectFailure(() => Layout(MaximumCount + 1, 0, false, Quiet), "exceeding MaximumCount must fail");
            ExpectFailure(() => Solve(_ => new RailStation(), 1, _ => new RailStation(new Point3(1, 0, 0), new Point3(0, 0, 1)),
                0.001, 41, Diameter, 0.005, 0, Quiet), "invalid/insufficient large rail");
            Console.WriteLine("PASS all geometry tests");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void TestV3OverhangGeometry()
    {
        // Pure geometry: no SolidWorks COM, therefore these tests verify
        // station extension, cap projection and 3D tube separation only.
        // Real solid/surface B-rep checks belong to the integration suite.
        const double shortLength = 0.81135, longLength = 1.42197;
        const double d = 0.020, requestedMargin = 0.005, lift = 0.00001;
        var normal = new Point3(0, 0, 1);
        RailStation Small(double s) => new RailStation(
            new Point3(s - shortLength / 2, 0, 0), normal);
        RailStation Large(double s) => new RailStation(
            new Point3(s - longLength / 2, 3, 0), normal);

        TubeLayout thirty = Solve(Small, shortLength, Large, longLength, 30, d,
            requestedMargin, lift, Quiet, smallMode: SmallEndSpacingMode.EqualPitch);
        Require(thirty.Issue == null && thirty.SmallStations[0] > 0,
            "N=30 baseline fits without overhang");

        TubeLayout fortyBaseline = Solve(Small, shortLength, Large, longLength, 40, d,
            requestedMargin, lift, Quiet, smallMode: SmallEndSpacingMode.EqualPitch);
        Require(fortyBaseline.Issue != null && fortyBaseline.InterferingPairCount > 0,
            "N=40 with margins is blocked for sloped straight fixture");

        TubeLayout forty = Solve(Small, shortLength, Large, longLength, 40, d,
            -0.002, lift, Quiet, smallMode: SmallEndSpacingMode.EqualPitch, allowOverhang: true);
        Require(forty.Issue == null && forty.MinimumGap > TubeDistance.Resolution,
            "N=40 H=2mm must clear in synthetic tapered fixture");
        AssertEqualPitchLayout(forty, 40, -0.002);

        TubeLayout fortyOne = Solve(Small, shortLength, Large, longLength, 41, d,
            -0.012, lift, Quiet, smallMode: SmallEndSpacingMode.EqualPitch, allowOverhang: true);
        Require(fortyOne.Issue == null && fortyOne.MinimumGap > TubeDistance.Resolution,
            "N=41 H=12mm must clear in synthetic fixture");
        AssertEqualPitchLayout(fortyOne, 41, -0.012);
        Require(fortyOne.SmallStations[0] < 0 &&
            fortyOne.SmallStations[40] > shortLength,
            "N=41 center stations extend beyond both small-rail ends");

        ExpectFailure(() => Solve(Small, shortLength, Large, longLength, 41, d,
            -0.012, lift, Quiet, smallMode: SmallEndSpacingMode.EqualPitch),
            "negative margin without opt-in must fail");
        ExpectFailure(() => Solve(Small, shortLength, Large, longLength, 41, d,
            -0.021, lift, Quiet, smallMode: SmallEndSpacingMode.EqualPitch, allowOverhang: true),
            "overhang > diameter must fail");
        ExpectFailure(() => Solve(Small, shortLength, Large, longLength, 41, d,
            -0.012, lift, Quiet, smallMode: SmallEndSpacingMode.TangentContact, allowOverhang: true),
            "overhang in touching mode must fail");

        Console.WriteLine("PASS V3 opt-in negative cap margins, N30 baseline, N40/H2 and N41/H12 synthetic geometry");
    }

    private static void TestEqualPitchCustom()
    {
        const double shortRail = 0.81135, longRail = 1.42197, margin = 0.005;
        var normal = new Point3(0, 0, 1);

        // 1. Low-taper straight rails verify exact nominal pitch formula up to N=40 and rejection at N=41.
        const double nearParallelLong = 0.820;
        RailStation StraightSmall(double s) => new RailStation(new Point3(s - shortRail / 2, 0, 0), normal);
        RailStation StraightLarge(double s) => new RailStation(new Point3(s - nearParallelLong / 2, 2.0, 0), normal);
        foreach (int customCount in new[] { 20, 30, 40 })
        {
            TubeLayout custom = Solve(StraightSmall, shortRail, StraightLarge, nearParallelLong, customCount, Diameter,
                margin, 0.00001, Quiet, smallMode: SmallEndSpacingMode.EqualPitch);
            Require(custom.Issue == null, "Equal-pitch low-taper custom " + customCount + ": " + custom.Issue);
            Require(custom.SmallMode == SmallEndSpacingMode.EqualPitch, "equal-pitch mode persisted");
            double nominalSmallPitch = (shortRail - Diameter - 2 * margin) / (customCount - 1);
            double nominalLargePitch = (nearParallelLong - Diameter - 2 * margin) / (customCount - 1);
            Require(Math.Abs(custom.SmallPitch - nominalSmallPitch) < 0.00002 &&
                Math.Abs(custom.LargePitch - nominalLargePitch) < 0.00002,
                "Independent pitches for low-taper Custom " + customCount);
            AssertEqualPitchLayout(custom, customCount, margin);
        }
        TubeLayout lowTaper41 = Solve(StraightSmall, shortRail, StraightLarge, nearParallelLong, 41, Diameter,
            margin, 0.00001, Quiet, smallMode: SmallEndSpacingMode.EqualPitch);
        Require(lowTaper41.Issue != null && lowTaper41.InterferingPairCount > 0,
            "low-taper N=41 (pitch 19.534 mm < 20 mm) must be rejected");

        // 2. Reference curved & twisted sheet (R3247 L=811.35 mm -> R4323.16 L=1421.97 mm, OD=20, M=5):
        //    N=20 and N=30 pass cleanly with nonzero small-end gap and balanced L/R cap margins;
        //    N=40 and N=41 must be rejected because 3D tilted outer pairs interfere (< 20 mm perpendicular).
        foreach (double twist in new[] { 0.0, 0.07 })
        {
            foreach (int n in new[] { 20, 30 })
            {
                TubeLayout ok = SolveEqualArc(n, Diameter, margin, twist, false, Quiet);
                Require(ok.Issue == null, F("Equal-pitch arc twist={0} N={1} failed: {2}", twist, n, ok.Issue));
                AssertEqualPitchLayout(ok, n, margin);
                Require(ok.MinimumContact > Diameter / 20, "EqualPitch allows nonzero small-end gap");
            }
            foreach (int n in new[] { 40, 41 })
            {
                TubeLayout clash = SolveEqualArc(n, Diameter, margin, twist, false, Quiet);
                Require(clash.Issue != null && clash.InterferingPairCount > 0 &&
                    clash.MinimumGap <= TubeDistance.Resolution,
                    F("Equal-pitch arc twist={0} N={1} must detect 3D interference", twist, n));
            }
        }

        // Flip Side in EqualPitch on twisted arc (N=30) must place axes on opposite sides cleanly.
        TubeLayout twisted30 = SolveEqualArc(30, Diameter, margin, 0.07, false, Quiet);
        TubeLayout twisted30Flip = SolveEqualArc(30, Diameter, margin, 0.07, true, Quiet);
        Require(twisted30Flip.Issue == null, twisted30Flip.Issue ?? "flipped equal-pitch N=30 failed");
        AssertEqualPitchLayout(twisted30Flip, 30, margin);
        Require(twisted30Flip.Axes[15].A.Z < twisted30.Axes[15].A.Z, "EqualPitch Flip Side reverses offset direction");

        // 3. Feasible N=40 with 811.35 / 1421.97 rails when margin=0 mm (or long 5.5 m span with margin=5 mm),
        //    while N=41 still fails.
        TubeLayout arc40Margin0 = SolveEqualArc(40, Diameter, 0.0, 0.07, false, Quiet);
        Require(arc40Margin0.Issue == null, arc40Margin0.Issue ?? "twisted arc N=40 with 0mm margin should fit");
        AssertEqualPitchLayout(arc40Margin0, 40, 0.0);
        TubeLayout arc41Margin0 = SolveEqualArc(41, Diameter, 0.0, 0.07, false, Quiet);
        Require(arc41Margin0.Issue != null && arc41Margin0.InterferingPairCount > 0,
            "twisted arc N=41 with 0mm margin still exceeds small-end 3D fit");

        RailStation SpanSmall(double s) => new RailStation(new Point3(s - shortRail / 2, 0, 0), normal);
        RailStation SpanLarge(double s) => new RailStation(new Point3(s - longRail / 2, 5.5, 0), normal);
        TubeLayout span40 = Solve(SpanSmall, shortRail, SpanLarge, longRail, 40, Diameter, margin,
            0.00001, Quiet, smallMode: SmallEndSpacingMode.EqualPitch);
        Require(span40.Issue == null, span40.Issue ?? "low-angle 811.35/1421.97 N=40 should fit");
        AssertEqualPitchLayout(span40, 40, margin);
        TubeLayout span41 = Solve(SpanSmall, shortRail, SpanLarge, longRail, 41, Diameter, margin,
            0.00001, Quiet, smallMode: SmallEndSpacingMode.EqualPitch);
        Require(span41.Issue != null && span41.InterferingPairCount > 0,
            "low-angle 811.35/1421.97 N=41 must fail 3D clearance");

        // 4. Varied diameters, margins, minimum count N=2, and insufficient rail length guards.
        TubeLayout smallDia = Solve(StraightSmall, shortRail, StraightLarge, nearParallelLong, 2, 0.016,
            0.010, 0.00001, Quiet, smallMode: SmallEndSpacingMode.EqualPitch);
        Require(smallDia.Issue == null, smallDia.Issue ?? "N=2 OD=16 M=10 failed");
        AssertEqualPitchLayout(smallDia, 2, 0.010);

        TubeLayout zeroMargin = Solve(StraightSmall, shortRail, StraightLarge, nearParallelLong, 20, 0.025,
            0.0, 0.00001, Quiet, smallMode: SmallEndSpacingMode.EqualPitch);
        Require(zeroMargin.Issue == null, zeroMargin.Issue ?? "N=20 OD=25 M=0 failed");
        AssertEqualPitchLayout(zeroMargin, 20, 0.0);

        ExpectFailure(() => Solve(StraightSmall, 0.030, StraightLarge, 0.100, 2, Diameter, 0.005, 0.00001, Quiet,
            smallMode: SmallEndSpacingMode.EqualPitch), "EqualPitch smallLength == D + 2M");
        ExpectFailure(() => Solve(StraightSmall, 0.025, StraightLarge, 0.100, 2, Diameter, 0.005, 0.00001, Quiet,
            smallMode: SmallEndSpacingMode.EqualPitch), "EqualPitch smallLength < D + 2M");
        ExpectFailure(() => Solve(StraightSmall, 0.100, StraightLarge, 0.030, 2, Diameter, 0.005, 0.00001, Quiet,
            smallMode: SmallEndSpacingMode.EqualPitch), "EqualPitch largeLength == D + 2M");

        // 5. VerifyExact must catch exact-station discrepancies (margins or collisions).
        TubeLayout tampered = SolveEqualArc(30, Diameter, margin, 0.07, false, Quiet);
        tampered.SmallStations[0] = 0.001; // force exact small left cap margin violation
        VerifyExact(tampered, s => ArcStation(s / SmallLength, false, 0.07, false),
            s => ArcStation(s / LargeLength, true, 0.07, false), LargeLength, Diameter, margin, Quiet);
        Require(tampered.Issue != null, "VerifyExact must reject out-of-tolerance small cap margin");

        Console.WriteLine("PASS custom equal-pitch small/large ends, balanced margins, twist/flip, and 3D collision checks");
    }

    private static RailStation ArcStation(double fraction, bool outer, double twist, bool flip)
    {
        Point3 Small(double s) => Arc(SmallRadius, SmallLength, s, 0);
        Point3 Large(double s) => Arc(LargeRadius, LargeLength, s, twist);
        Point3 p = Small(fraction * SmallLength), q = Large(fraction * LargeLength);
        double length = outer ? LargeLength : SmallLength;
        Func<double, Point3> point = outer ? (Func<double, Point3>)Large : Small;
        double sVal = length * fraction;
        var tangent = point(sVal + 1e-7) - point(sVal - 1e-7);
        return new RailStation(outer ? q : p, (q - p).Cross(tangent).Unit() * (flip ? -1 : 1));
    }

    private static TubeLayout SolveEqualArc(int count, double diameter, double margin, double twist, bool flip, Action<string> log)
    {
        return Solve(s => ArcStation(s / SmallLength, false, twist, flip), SmallLength,
            s => ArcStation(s / LargeLength, true, twist, flip), LargeLength,
            count, diameter, margin, 0.00001, log, smallMode: SmallEndSpacingMode.EqualPitch);
    }

    private static void AssertEqualPitchLayout(TubeLayout layout, int count, double margin)
    {
        Require(layout.Axes.Length == count, "axis count matches requested count");
        Require(layout.MinimumGap > TubeDistance.Resolution && layout.InterferingPairCount == 0,
            "all tubes have proven 3D separation");
        Require(Math.Abs(layout.SmallLeftClearance - margin) <= 0.00002 &&
            Math.Abs(layout.SmallRightClearance - margin) <= 0.00002,
            "symmetric small cap margins");
        Require(Math.Abs(layout.LargeLeftClearance - margin) <= 0.00002 &&
            Math.Abs(layout.LargeRightClearance - margin) <= 0.00002,
            "symmetric large cap margins");
        for (int i = 1; i < count; i++)
        {
            double step = layout.SmallStations[i] - layout.SmallStations[i - 1];
            Require(step > 0 && Math.Abs(step - layout.SmallPitch) < 1e-9, "strictly equal increasing small stations");
        }
    }

    private static void TestCylinders()
    {
        var a = Axis(0, 0, 1);
        Gap touching = TubeDistance.Between(a, Axis(0.020, 0, 1), 0.010);
        Require(touching.Upper <= TubeDistance.Resolution, "touching parallel cylinders");
        Gap clear = TubeDistance.Between(a, Axis(0.023, 0, 1), 0.010);
        Require(clear.Lower >= 0.003 - TubeDistance.Resolution, "parallel separation");
        Gap axial = TubeDistance.Between(a, Axis(0, 1.004, 2), 0.010);
        Require(axial.Lower >= 0.004 - TubeDistance.Resolution, "flat caps separated on coincident axes");
        Gap crossing = TubeDistance.Between(a,
            new TubeAxis(new Point3(-0.05, 0, 0.5), new Point3(0.05, 0, 0.5)), 0.010);
        Require(crossing.Upper <= TubeDistance.Resolution, "crossing tube collision");
        var corner = new TubeAxis(new Point3(0.022, 0, 1.003), new Point3(0.022, 0, 2));
        Gap cornerGap = TubeDistance.Between(a, corner, 0.010);
        Require(Math.Abs(cornerGap.Lower - Math.Sqrt(13) * Mm) <= TubeDistance.Resolution,
            "combined radial and axial cap gap");

        // Two stations exactly 20 mm apart on a small circular rail have a
        // shorter 3D chord, even before the two tube directions tilt.
        var p = Arc(0.100, 0.020, 0, 0);
        var q = Arc(0.100, 0.020, 0.020, 0);
        Gap arc = TubeDistance.Between(new TubeAxis(p, p + new Point3(0, 0, 1)),
            new TubeAxis(q, q + new Point3(0, 0, 1)), 0.010);
        Require((p - q).Length < Diameter && arc.Lower == 0, "20 mm arc pitch does not certify contact");

        // Two tilted parallel cylinders spaced 20.04 mm along X but tilted 10 deg in XY
        // have perpendicular distance 20.04 * cos(10 deg) = 19.735 mm < 20 mm -> collision.
        double dx = Math.Sin(10 * Math.PI / 180), dy = Math.Cos(10 * Math.PI / 180);
        var t0 = new TubeAxis(new Point3(0, 0, 0), new Point3(dx, dy, 0));
        var t1 = new TubeAxis(new Point3(0.02004, 0, 0), new Point3(0.02004 + dx, dy, 0));
        Gap tiltedClash = TubeDistance.Between(t0, t1, 0.010);
        Require(tiltedClash.Upper <= TubeDistance.Resolution && tiltedClash.Lower == 0,
            "tilted cylinders with 20.04 mm edge pitch still collide in 3D");

        // Near-touching parallel cylinders with 0.001 mm gap must prove separation > Resolution.
        Gap tightClear = TubeDistance.Between(a, Axis(0.020001, 0, 1), 0.010);
        Require(tightClear.Lower > TubeDistance.Resolution, "0.001 mm parallel separation proven");
        Console.WriteLine("PASS finite caps, crossing, 3D separation bounds, tilted overlap, arc/chord regression");
    }

    private static TubeAxis Axis(double x, double from, double to) =>
        new TubeAxis(new Point3(x, 0, from), new Point3(x, 0, to));

    private static void TestRails()
    {
        Func<double, Point3> small = s => Arc(SmallRadius, SmallLength, s, 0);
        Func<double, Point3> large = s => Arc(LargeRadius, LargeLength, s, 0.07);
        var pieces = new List<BoundaryPiece>
        {
            Piece(SmallLength / 2, small),
            Piece(SmallLength / 2, s => small(SmallLength / 2 + s)),
            Line(small(SmallLength), large(LargeLength)),
            Piece(LargeLength / 3, s => large(LargeLength - s)),
            Piece(2 * LargeLength / 3, s => large(2 * LargeLength / 3 - s)),
            Line(large(0), small(0))
        };
        var rails = BoundaryRails.Find(pieces.Skip(1).Concat(pieces.Take(1)).ToList(), Quiet);
        Require(rails.Small.PieceCount == 2 && rails.Large.PieceCount == 2, "split chains");
        Require(Math.Abs(rails.Large.Length - LargeLength) < 1e-10, "measured rail length");
        var reversed = pieces.AsEnumerable().Reverse().Select(p => new BoundaryPiece(p.Length,
            p.End, p.Start, p.EndTangent * -1, p.StartTangent * -1, s => p.At(p.Length - s))).ToList();
        var reversedRails = BoundaryRails.Find(reversed, Quiet);
        Require((reversedRails.Small.Start - rails.Small.End).Length < 1e-10, "reversed traversal");
        var rectangle = new[] { new Point3(0, 0, 0), new Point3(0, 1, 0), new Point3(1, 1, 0), new Point3(1, 0, 0) };
        ExpectFailure(() => BoundaryRails.Find(Enumerable.Range(0, 4)
            .Select(i => Line(rectangle[i], rectangle[(i + 1) % 4])).ToList(), Quiet), "ambiguous rectangle");
        var triangle = new[] { new Point3(0, 0, 0), new Point3(0.5, 1, 0), new Point3(1, 0, 0) };
        ExpectFailure(() => BoundaryRails.Find(Enumerable.Range(0, 3)
            .Select(i => Line(triangle[i], triangle[(i + 1) % 3])).ToList(), Quiet), "3-sided face");
        var pentagon = new[] { new Point3(0, 0, 0), new Point3(0, 0.6, 0), new Point3(0.5, 1, 0), new Point3(1, 0.6, 0), new Point3(1, 0, 0) };
        ExpectFailure(() => BoundaryRails.Find(Enumerable.Range(0, 5)
            .Select(i => Line(pentagon[i], pentagon[(i + 1) % 5])).ToList(), Quiet), "5-sided face");
        ExpectFailure(() => BoundaryRails.Find(pieces.Take(5).ToList(), Quiet), "disconnected loop");
        ExpectFailure(() => Piece(0, small), "zero-length edge");
        Console.WriteLine("PASS topology splits, reverse orientation, non-four-sided, ambiguous and degenerate boundaries");
    }

    private static BoundaryPiece Line(Point3 a, Point3 b)
    {
        var d = b - a;
        return new BoundaryPiece(d.Length, a, b, d, d, s => a + d.Unit() * s);
    }

    private static BoundaryPiece Piece(double length, Func<double, Point3> point)
    {
        if (length == 0) return new BoundaryPiece(0, point(0), point(0), new Point3(), new Point3(), point);
        return new BoundaryPiece(length, point(0), point(length), point(1e-7) - point(0),
            point(length) - point(length - 1e-7), point);
    }

    private static Point3 Arc(double radius, double length, double s, double rotation)
    {
        double angle = (s - length / 2) / radius;
        double y = radius * Math.Sin(angle);
        return new Point3(radius * Math.Cos(angle), y * Math.Cos(rotation), y * Math.Sin(rotation));
    }

    private static TubeLayout Layout(int? count, double twist, bool flip, Action<string> log)
    {
        Point3 Small(double s) => Arc(SmallRadius, SmallLength, s, 0);
        Point3 Large(double s) => Arc(LargeRadius, LargeLength, s, twist);
        RailStation Station(double fraction, bool outer)
        {
            Point3 p = Small(fraction * SmallLength), q = Large(fraction * LargeLength);
            double length = outer ? LargeLength : SmallLength;
            Func<double, Point3> point = outer ? (Func<double, Point3>)Large : Small;
            double s = length * fraction;
            var tangent = point(s + 1e-7) - point(s - 1e-7);
            return new RailStation(outer ? q : p, (q - p).Cross(tangent).Unit() * (flip ? -1 : 1));
        }
        RailStation SmallStation(double s) => Station(s / SmallLength, false);
        RailStation LargeStation(double s) => Station(s / LargeLength, true);
        return count.HasValue
            ? Solve(SmallStation, SmallLength, LargeStation, LargeLength, count.Value, Diameter, 0.005, 0.00001, log)
            : SolveAutomatic(SmallStation, SmallLength, LargeStation, LargeLength, Diameter, 0.005, 0.00001, log);
    }

    private static void TestAutoDifferentCount()
    {
        const double smallLength = 0.062, largeLength = 0.115;
        var normal = new Point3(0, 0, 1);
        RailStation Small(double s) => new RailStation(new Point3(s - smallLength / 2, 0, 0), normal);
        RailStation Large(double s) => new RailStation(new Point3(s - largeLength / 2, 1, 0), normal);
        var layout = SolveAutomatic(Small, smallLength, Large, largeLength, Diameter, 0.005, 0.00001, Quiet);
        Require(layout.Axes.Length == 4 && layout.Issue == null, "automatic count must adapt to a shorter small rail");
        Summary("auto_short_rail", layout);
    }

    private static void AssertLayout(TubeLayout layout, int count)
    {
        Require(layout.Issue == null, layout.Issue ?? "verified layout");
        Require(layout.Axes.Length == count && layout.MinimumGap > 0, "all tubes separated");
        Require(layout.MaximumContact <= ContactTolerance, "small-end contact tolerance");
        Require(Math.Abs(layout.SmallStations[0] + layout.SmallStations[count - 1] - SmallLength) <= 0.00002,
            "symmetric small-end station margins");
        Require(Math.Abs(layout.LargeLeftClearance - 0.005) <= 0.00002 &&
            Math.Abs(layout.LargeRightClearance - 0.005) <= 0.00002, "large cap margins");
    }

    private static void Summary(string name, TubeLayout l) => Console.WriteLine(F(
        "PASS {0} count={1} large_pitch_mm={2:F6} large_left_mm={3:F6} large_right_mm={4:F6} small_first_mm={5:F6} small_gap_range_mm={6:F6}..{7:F6} min_full_gap_mm={8:F6}",
        name, l.Axes.Length, l.LargePitch / Mm, l.LargeLeftClearance / Mm, l.LargeRightClearance / Mm,
        l.SmallStations[0] / Mm, l.MinimumContact / Mm, l.MaximumContact / Mm, l.MinimumGap / Mm));

    private static void Require(bool ok, string description)
    {
        if (!ok) throw new Exception("FAIL " + description);
    }

    private static void ExpectFailure(Action action, string description)
    {
        try { action(); }
        catch (LayoutException) { return; }
        throw new Exception("FAIL expected rejection: " + description);
    }
}
