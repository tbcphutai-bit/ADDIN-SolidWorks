// KADAI P0 - geometry-independent layout planner; units: millimeters.
// This planner DOES NOT read SolidWorks geometry or manufacture any cuts.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ADDIN.Commands.Kadai
{
    internal enum KadaiRibDirection { Longitudinal, Transverse }

    internal sealed class KadaiRequest
    {
        public string Preset = "KDA1";
        public double LengthXmm = 2250;
        public double LengthZmm = 1450;
        public int LongRibCount = 7;
        public int CrossRibCount = 4;
        public double LongEndMarginMm = 50;
        public double CrossEndMarginMm = 150.9;
        public double? StartClearanceMm;
        public double? EndClearanceMm;
        public KadaiRibDirection? PanelRibDirection;
        public double FixedHeightMm = 170;
        public double? JointHeightMm;
        public bool OpensAtBottom(KadaiRibDirection direction)
        {
            return direction == (PanelRibDirection ?? KadaiRibDirection.Longitudinal);
        }
        public double LongThicknessMm = 1.6;
        public double CrossThicknessMm = 1.6;
        public double LongSlotExtraMm = 0.2; // TOTAL extra width in notch on long rib
        public double CrossSlotExtraMm = 0.2; // TOTAL extra width in notch on cross rib
        public double GapToPanelMm = 0;
        public int HeightAxis = 1; // 0=X, 1=Y, 2=Z; U/V are the other two axes
        public int HeightSign = 1;
        public double OriginUmm = 0;
        public double OriginVmm = 0;
        public double BottomMm = 0; // coordinate projected onto the signed height axis
        public double PreviewChordToleranceMm = 0.05;
        internal KadaiRequest Snapshot() { return (KadaiRequest)MemberwiseClone(); }

        public static KadaiRequest ForPreset(string name)
        {
            if (string.Equals(name, "KF3", StringComparison.OrdinalIgnoreCase))
                return new KadaiRequest
                {
                    Preset = "KF3", LengthXmm = 780, LengthZmm = 200,
                    LongRibCount = 5, CrossRibCount = 2,
                    LongEndMarginMm = 0, CrossEndMarginMm = 25.8,
                    LongThicknessMm = 1.6, CrossThicknessMm = 1.6,
                    LongSlotExtraMm = 0.2, CrossSlotExtraMm = 0,
                    GapToPanelMm = 0
                };
            return new KadaiRequest();
        }
    }

    internal sealed class KadaiRibPosition
    {
        public string Id;
        public string StationKey;
        public KadaiRibDirection Direction;
        public double CoordinateMm;
        public double NormalizedPosition;
    }

    internal sealed class KadaiSlotPlan
    {
        public string Id;
        public string OwnerRibId;
        public string CrossingRibId;
        public double CenterAlongRibMm;
        public double WidthMm;
        public double MatingThicknessMm;
        public double TotalClearanceMm;
        // Shape, depth and entry direction MUST be resolved from real 3D joint.
    }

    internal sealed class KadaiPlan
    {
        public KadaiRequest Settings;
        public readonly List<KadaiRibPosition> LongRibs = new List<KadaiRibPosition>();
        public readonly List<KadaiRibPosition> CrossRibs = new List<KadaiRibPosition>();
        public readonly List<KadaiSlotPlan> CandidateSlots = new List<KadaiSlotPlan>();
        public readonly List<string> Warnings = new List<string>();
        public int CrossingCount { get { return checked(LongRibs.Count * CrossRibs.Count); } }
    }

    internal static class KadaiPlanner
    {
        public static KadaiPlan Build(KadaiRequest settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            settings = settings.Snapshot();
            Positive(settings.LengthXmm, "LengthXmm");
            Positive(settings.LengthZmm, "LengthZmm");
            Positive(settings.LongThicknessMm, "LongThicknessMm");
            Positive(settings.CrossThicknessMm, "CrossThicknessMm");
            Nonnegative(settings.LongEndMarginMm, "LongEndMarginMm");
            Nonnegative(settings.CrossEndMarginMm, "CrossEndMarginMm");
            Nonnegative(settings.LongSlotExtraMm, "LongSlotExtraMm");
            Nonnegative(settings.CrossSlotExtraMm, "CrossSlotExtraMm");
            Nonnegative(settings.GapToPanelMm, "GapToPanelMm");
            Positive(settings.PreviewChordToleranceMm, "PreviewChordToleranceMm");
            if (settings.HeightAxis < 0 || settings.HeightAxis > 2)
                throw new ArgumentOutOfRangeException("HeightAxis");
            if (settings.HeightSign != 1 && settings.HeightSign != -1)
                throw new ArgumentOutOfRangeException("HeightSign");
            foreach (double coordinate in new[] { settings.OriginUmm, settings.OriginVmm, settings.BottomMm })
                if (double.IsNaN(coordinate) || double.IsInfinity(coordinate))
                    throw new ArgumentException("Datum coordinates must be finite.");
            if (settings.LongRibCount < 1 || settings.CrossRibCount < 1
                || settings.LongRibCount > 100 || settings.CrossRibCount > 100)
                throw new ArgumentOutOfRangeException("count", "Count must be 1..100.");
            if ((long)settings.LongRibCount * settings.CrossRibCount > 2000)
                throw new InvalidOperationException("Maximum preview budget is 2000 crossings.");
            if (settings.StartClearanceMm.HasValue != settings.EndClearanceMm.HasValue)
                throw new ArgumentException("Nhập cả khoảng chừa đầu và khoảng chừa cuối.");
            double uStart = settings.StartClearanceMm ?? settings.LongEndMarginMm;
            double uEnd = settings.EndClearanceMm ?? settings.LongEndMarginMm;
            double vStart = settings.StartClearanceMm ?? settings.CrossEndMarginMm;
            double vEnd = settings.EndClearanceMm ?? settings.CrossEndMarginMm;
            Nonnegative(uStart, "Khoảng chừa đầu");
            Nonnegative(uEnd, "Khoảng chừa cuối");
            if (uStart + uEnd >= settings.LengthXmm || vStart + vEnd >= settings.LengthZmm)
                throw new ArgumentException("Hai khoảng chừa chiếm hết chiều dài base. Giảm khoảng chừa đầu hoặc cuối.");

            var plan = new KadaiPlan { Settings = settings };
            var xs = EvenPositions(settings.LengthXmm, uStart, uEnd, settings.LongRibCount);
            var zs = EvenPositions(settings.LengthZmm, vStart, vEnd, settings.CrossRibCount);
            for (int i = 0; i < xs.Count; i++)
                plan.LongRibs.Add(new KadaiRibPosition
                {
                    Id = "L-" + (i + 1).ToString("D3", CultureInfo.InvariantCulture),
                    StationKey = StationKey("L", settings.HeightAxis, settings.OriginUmm + xs[i]),
                    Direction = KadaiRibDirection.Longitudinal, CoordinateMm = xs[i],
                    NormalizedPosition = xs[i] / settings.LengthXmm
                });
            for (int i = 0; i < zs.Count; i++)
                plan.CrossRibs.Add(new KadaiRibPosition
                {
                    Id = "C-" + (i + 1).ToString("D3", CultureInfo.InvariantCulture),
                    StationKey = StationKey("C", settings.HeightAxis, settings.OriginVmm + zs[i]),
                    Direction = KadaiRibDirection.Transverse, CoordinateMm = zs[i],
                    NormalizedPosition = zs[i] / settings.LengthZmm
                });

            foreach (var l in plan.LongRibs)
                foreach (var c in plan.CrossRibs)
                {
                    string joint = l.Id + "_" + c.Id;
                    plan.CandidateSlots.Add(new KadaiSlotPlan
                    {
                        Id = "LONG_" + joint, OwnerRibId = l.Id, CrossingRibId = c.Id,
                        CenterAlongRibMm = c.CoordinateMm,
                        MatingThicknessMm = settings.CrossThicknessMm,
                        TotalClearanceMm = settings.LongSlotExtraMm,
                        WidthMm = settings.CrossThicknessMm + settings.LongSlotExtraMm
                    });
                    plan.CandidateSlots.Add(new KadaiSlotPlan
                    {
                        Id = "CROSS_" + joint, OwnerRibId = c.Id, CrossingRibId = l.Id,
                        CenterAlongRibMm = l.CoordinateMm,
                        MatingThicknessMm = settings.LongThicknessMm,
                        TotalClearanceMm = settings.CrossSlotExtraMm,
                        WidthMm = settings.LongThicknessMm + settings.CrossSlotExtraMm
                    });
                }

            if (settings.LongSlotExtraMm == 0) plan.Warnings.Add("Long notch has zero nominal width clearance.");
            if (settings.CrossSlotExtraMm == 0) plan.Warnings.Add("Cross notch has zero nominal width clearance.");
            if (settings.LongRibCount == 1 || settings.CrossRibCount == 1)
                plan.Warnings.Add("One-rib configuration is centered; review the mating arrangement.");
            if (settings.LongRibCount > 1
                && (settings.LengthXmm - uStart - uEnd) / (settings.LongRibCount - 1)
                   <= settings.LongThicknessMm)
                plan.Warnings.Add("Long rib pitch is not greater than thickness.");
            if (settings.CrossRibCount > 1
                && (settings.LengthZmm - vStart - vEnd) / (settings.CrossRibCount - 1)
                   <= settings.CrossThicknessMm)
                plan.Warnings.Add("Cross rib pitch is not greater than thickness.");
            plan.Warnings.Add("Widths are orthogonal nominal PREVIEW formulas, not verified cuts at oblique joints.");
            plan.Warnings.Add("Sample dimensions are observations, NOT a validated company material specification.");
            return plan;
        }

        internal static List<double> EvenPositions(double extent, double margin, int count)
        {
            return EvenPositions(extent, margin, margin, count);
        }

        internal static List<double> EvenPositions(double extent, double start, double end, int count)
        {
            var result = new List<double>();
            double span = extent - start - end;
            if (count == 1) { result.Add((extent + (start - end)) / 2); return result; }
            result.Add(start);
            for (int i = 1; i < count - 1; i++) result.Add(start + span * i / (count - 1));
            result.Add(extent - end);
            return result;
        }
        private static string StationKey(string direction, int heightAxis, double worldCoordinateMm)
        {
            return direction + heightAxis.ToString(CultureInfo.InvariantCulture) + "@" +
                Math.Round(worldCoordinateMm * 1000, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }

        // Compare center planes on the same captured face. Ordinal file labels
        // may change; this diff alone does not authorize updating saved Parts.
        internal static string DescribeStationChanges(KadaiPlan before, KadaiPlan after)
        {
            var oldKeys = before.LongRibs.Concat(before.CrossRibs).Select(rib => rib.StationKey).ToList();
            var newKeys = after.LongRibs.Concat(after.CrossRibs).Select(rib => rib.StationKey).ToList();
            if (oldKeys.Distinct().Count() != oldKeys.Count || newKeys.Distinct().Count() != newKeys.Count)
                return "Không thể đối chiếu vị trí: hai thanh cùng khóa tọa độ 0.001 mm.";
            return "So với Preview trước: giữ " + oldKeys.Intersect(newKeys).Count()
                + " mặt phẳng thanh, thêm " + newKeys.Except(oldKeys).Count()
                + ", bỏ " + oldKeys.Except(newKeys).Count() + ". Part đã lưu chưa được cập nhật.";
        }
        private static void Positive(double x, string label)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || x <= 0)
                throw new ArgumentOutOfRangeException(label, "Must be positive and finite.");
        }
        private static void Nonnegative(double x, string label)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || x < 0)
                throw new ArgumentOutOfRangeException(label, "Must be nonnegative and finite.");
        }
    }
}
