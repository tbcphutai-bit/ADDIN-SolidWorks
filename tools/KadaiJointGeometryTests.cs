using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands.Kadai;

internal static class KadaiJointGeometryTests
{
    private static int Main()
    {
        var request = new KadaiRequest { LengthXmm = 1000, LengthZmm = 650,
            OriginUmm = -150, OriginVmm = 20, BottomMm = -80, LongEndMarginMm = 40,
            CrossEndMarginMm = 35, LongThicknessMm = 2, CrossThicknessMm = 1.6,
            LongSlotExtraMm = .3, CrossSlotExtraMm = .25 };
        foreach (var counts in new[] { new[] { 3, 1 }, new[] { 7, 4 }, new[] { 10, 6 }, new[] { 7, 4 }, new[] { 5, 2 } })
        {
            request.LongRibCount = counts[0];
            request.CrossRibCount = counts[1];
            var plan = KadaiPlanner.Build(request);
            var profiles = Profiles(plan);
            var joints = KadaiJointGeometry.Build(plan, profiles, 100, .1);
            Require(joints.Count == counts[0] + counts[1], "Wrong rib count.");
            Require(joints.Values.Sum(cuts => cuts.Count) == 2 * counts[0] * counts[1], "Wrong number of slots.");
            foreach (var cut in joints.Values.SelectMany(cuts => cuts))
            {
                bool bottomEntry = cut.SlotId.StartsWith("LONG_", StringComparison.Ordinal);
                Require(Math.Abs((cut.Right - cut.Left) * 1000 - (bottomEntry ? 1.9 : 2.25)) < 1e-8, "Wrong slot width.");
                Require(bottomEntry ? cut.Bottom < 0 && Math.Abs(cut.Top - .1) < 1e-8
                    : cut.Top > .16 && Math.Abs(cut.Bottom - .0999) < 1e-8, "Incorrect entry and height clearance.");
            }
            foreach (var rib in KadaiJointGeometry.NotchedProfiles(plan, profiles, joints))
            {
                var points = KadaiJointGeometry.LocalPoints(rib, request.BottomMm);
                var blank = KadaiJointGeometry.LocalPoints(profiles.Single(p => p.RibId == rib.RibId), request.BottomMm);
                Require(Area(points) < Area(blank), "Preview did not remove material.");
                foreach (var cut in joints[rib.RibId])
                {
                    bool bottomEntry = rib.Direction == KadaiRibDirection.Longitudinal;
                    double level = bottomEntry ? cut.Top : cut.Bottom;
                    double from = bottomEntry ? cut.Right : cut.Left;
                    double to = bottomEntry ? cut.Left : cut.Right;
                    Require(Enumerable.Range(0, points.Length - 1).Any(i =>
                        Math.Abs(points[i][0] - from) < 1e-9 && Math.Abs(points[i + 1][0] - to) < 1e-9
                        && Math.Abs(points[i][1] - level) < 1e-9 && Math.Abs(points[i + 1][1] - level) < 1e-9),
                        "Missing reciprocal slot floor in preview.");
                }
            }
            Console.WriteLine("PASS joint layout and notched preview " + counts[0] + "x" + counts[1]);
        }
        var valid = KadaiPlanner.Build(request);
        var reversedRequest = request.Snapshot();
        reversedRequest.HeightSign = -1;
        var reversed = KadaiPlanner.Build(reversedRequest);
        var reversedProfiles = Profiles(reversed);
        var reversedCuts = KadaiJointGeometry.Build(reversed, reversedProfiles, 100, .1);
        Require(KadaiJointGeometry.NotchedProfiles(reversed, reversedProfiles, reversedCuts).All(rib =>
            rib.HeightDirection[1] == -1 && rib.PointsWorldMeters.All(point => point[1] <= -reversedRequest.BottomMm * .001)),
            "Wrong datum or height on negative axis.");
        Console.WriteLine("PASS signed height axis and notch perimeter");
        Reject(() => KadaiJointGeometry.Build(valid, Profiles(valid), 100, 0), "zero depth clearance");
        Reject(() => KadaiJointGeometry.Build(valid, Profiles(valid), 159.5, .1), "insufficient top web");
        Reject(() => KadaiJointGeometry.Build(valid, Profiles(valid), 2, .3), "insufficient bottom web");
        var offStation = Profiles(valid);
        offStation[0].PointsWorldMeters[0][0] += .001;
        Reject(() => KadaiJointGeometry.Build(valid, offStation, 100, .1), "off-station contour");
        var missing = Profiles(valid);
        missing.RemoveAt(0);
        Reject(() => KadaiJointGeometry.Build(valid, missing, 100, .1), "missing rib");
        request.CrossEndMarginMm = 0;
        var trimmed = KadaiPlanner.Build(request);
        Reject(() => KadaiJointGeometry.Build(trimmed, Profiles(trimmed), 100, .1), "slot outside trimmed end");
        request.CrossEndMarginMm = 35;
        request.LongSlotExtraMm = 0;
        var zero = KadaiPlanner.Build(request);
        Reject(() => KadaiJointGeometry.Build(zero, Profiles(zero), 100, .1), "zero width clearance");
        Console.WriteLine("PASS joint geometry preflight");
        return 0;
    }

    private static double Area(double[][] loop)
    {
        return Math.Abs(Enumerable.Range(0, loop.Length).Sum(i =>
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Length];
            return a[0] * b[1] - a[1] * b[0];
        })) / 2;
    }

    private static List<KadaiProfile3D> Profiles(KadaiPlan plan)
    {
        var settings = plan.Settings;
        double u = settings.OriginUmm * .001, v = settings.OriginVmm * .001, b = settings.BottomMm * .001;
        var list = new List<KadaiProfile3D>();
        foreach (var rib in plan.LongRibs.Concat(plan.CrossRibs))
        {
            bool isLong = rib.Direction == KadaiRibDirection.Longitudinal;
            double fixedCoordinate = rib.CoordinateMm * .001 + (isLong ? u : v);
            double along = isLong ? v : u;
            double end = along + (isLong ? settings.LengthZmm : settings.LengthXmm) * .001;
            var profile = new KadaiProfile3D { RibId = rib.Id, Direction = rib.Direction, Closed = true,
                AlongDirection = isLong ? new[] { 0d, 0, 1 } : new[] { 1d, 0, 0 },
                HeightDirection = new[] { 0d, settings.HeightSign, 0 }, ExtrusionDirection = isLong ? new[] { 1d, 0, 0 } : new[] { 0d, 0, 1 },
                ThicknessMeters = (isLong ? settings.LongThicknessMm : settings.CrossThicknessMm) * .001 };
            foreach (var point in new[] { new[] { along, b + .19 }, new[] { (end + along) / 2, b + .16 },
                new[] { end, b + .22 }, new[] { end, b }, new[] { along, b } })
                profile.PointsWorldMeters.Add(isLong ? new[] { fixedCoordinate, settings.HeightSign * point[1], point[0] }
                    : new[] { point[0], settings.HeightSign * point[1], fixedCoordinate });
            list.Add(profile);
        }
        return list;
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { Console.WriteLine("PASS reject " + message); return; }
        catch (InvalidOperationException) { Console.WriteLine("PASS reject " + message); return; }
        throw new InvalidOperationException("Accepted " + message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
