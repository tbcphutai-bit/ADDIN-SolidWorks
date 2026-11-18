using System;
using System.Linq;
using ADDIN.Commands.Kadai;
public static class KadaiPlannerTests
{
    static int checks;
    static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    static void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { checks++; return; }
        catch (InvalidOperationException) { checks++; return; } throw new Exception("Did not reject " + name); }
    public static int Main()
    {
        foreach (var pair in new[] { new[] { 7, 4 }, new[] { 10, 6 }, new[] { 7, 4 }, new[] { 5, 2 }, new[] { 3, 1 } }) {
            var r = new KadaiRequest { LongRibCount = pair[0], CrossRibCount = pair[1] };
            var p = KadaiPlanner.Build(r);
            Check(p.CrossingCount == pair[0] * pair[1], "Crossing count");
            Check(p.CandidateSlots.Count == 2 * p.CrossingCount, "Two candidate slots per crossing");
            Check(p.CandidateSlots.Select(s => s.Id).Distinct().Count() == p.CandidateSlots.Count, "Unique slot identities");
            Check(p.CandidateSlots.All(s => Math.Abs(s.WidthMm - s.MatingThicknessMm - s.TotalClearanceMm) < 1e-10), "Width clearance contract");
        }
        var singleton = KadaiPlanner.Build(new KadaiRequest { LongRibCount = 1, CrossRibCount = 1 });
        Check(singleton.LongRibs[0].CoordinateMm == 1125 && singleton.CrossRibs[0].CoordinateMm == 725, "Centered singleton");
        var kf = KadaiPlanner.Build(KadaiRequest.ForPreset("KF3"));
        Check(kf.Warnings.Any(x => x.Contains("zero nominal")), "Zero clearance warning");
        var mutable = new KadaiRequest(); var snapshot = KadaiPlanner.Build(mutable); mutable.LongRibCount = 99;
        Check(snapshot.Settings.LongRibCount == 7, "Plan keeps its request snapshot");
        var increased = KadaiPlanner.Build(new KadaiRequest { LongRibCount = 10, CrossRibCount = 6 });
        Check(snapshot.LongRibs.Last().Id != increased.LongRibs.Last().Id &&
            snapshot.LongRibs.Last().StationKey == increased.LongRibs.Last().StationKey, "Fixed end plane keeps station key");
        Check(KadaiPlanner.DescribeStationChanges(snapshot, increased).Contains("giữ 6 mặt phẳng thanh, thêm 10, bỏ 5"), "Station diff");
        Reject(() => KadaiPlanner.Build(new KadaiRequest { LongRibCount = 0 }), "zero count");
        Reject(() => KadaiPlanner.Build(new KadaiRequest { LongRibCount = 100, CrossRibCount = 100 }), "budget");
        Reject(() => KadaiPlanner.Build(new KadaiRequest { LengthXmm = double.NaN }), "NaN span");
        Reject(() => KadaiPlanner.Build(new KadaiRequest { OriginUmm = double.PositiveInfinity }), "infinite datum");
        Reject(() => KadaiPlanner.Build(new KadaiRequest { LongSlotExtraMm = -.1 }), "negative clearance");
        Reject(() => KadaiPlanner.Build(new KadaiRequest { LongEndMarginMm = 1125 }), "overlapping margins");
        Reject(() => KadaiPlanner.Build(new KadaiRequest { HeightAxis = 3 }), "invalid frame");
        Console.WriteLine("PASS actual C# planner: " + checks + " behavioral checks; CAD not covered."); return 0;
    }
}
