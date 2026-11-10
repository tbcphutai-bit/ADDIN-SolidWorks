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
        Console.WriteLine("PASS finite caps, crossing, 3D separation bounds, arc/chord regression");
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
        ExpectFailure(() => BoundaryRails.Find(pieces.Take(5).ToList(), Quiet), "disconnected loop");
        ExpectFailure(() => Piece(0, small), "zero-length edge");
        Console.WriteLine("PASS topology splits, reverse orientation, ambiguous and degenerate boundaries");
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
