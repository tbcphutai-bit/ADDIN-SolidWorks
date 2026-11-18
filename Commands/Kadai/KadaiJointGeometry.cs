using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.Kadai
{
    // Cut rectangles in the rib sketch frame, in meters.
    internal sealed class KadaiJointCut
    {
        internal string SlotId, MatingRibId;
        internal double Left, Right, Bottom, Top;
    }

    internal static class KadaiJointGeometry
    {
        private const double SketchOverrun = .001;
        private const double GeometryTolerance = .0000001;

        internal static Dictionary<string, List<KadaiJointCut>> Build(
            KadaiPlan plan, IList<KadaiProfile3D> profiles, double seamMm, double depthClearanceMm)
        {
            if (!Finite(seamMm) || seamMm <= 0 || !Finite(depthClearanceMm) || depthClearanceMm <= 0)
                throw new ArgumentException("Cao độ ghép tính từ đáy và độ hở theo chiều cao phải lớn hơn 0.");
            var ribs = plan.LongRibs.Concat(plan.CrossRibs).ToDictionary(rib => rib.Id);
            var byId = profiles.ToDictionary(profile => profile.RibId);
            if (byId.Count != ribs.Count || ribs.Keys.Any(id => !byId.ContainsKey(id)))
                throw new InvalidOperationException("Bộ contour thiếu hoặc thừa thanh.");

            double seam = seamMm * .001;
            double clearance = depthClearanceMm * .001;
            var specifications = plan.CandidateSlots.ToLookup(slot => slot.OwnerRibId);
            var result = new Dictionary<string, List<KadaiJointCut>>();
            foreach (var profile in profiles)
            {
                var rib = ribs[profile.RibId];
                bool isLong = rib.Direction == KadaiRibDirection.Longitudinal;
                var settings = plan.Settings;
                double expected = (isLong ? settings.LongThicknessMm : settings.CrossThicknessMm) * .001;
                if (profile.Direction != rib.Direction || !Finite(profile.ThicknessMeters)
                    || Math.Abs(profile.ThicknessMeters - expected) > GeometryTolerance)
                    throw new InvalidOperationException(rib.Id + ": sai phương hoặc chiều dày contour.");

                var points = LocalPoints(profile, settings.BottomMm);
                int h = settings.HeightAxis;
                int u = h == 0 ? 1 : 0, v = h == 2 ? 1 : 2;
                int across = isLong ? u : v;
                double station = ((isLong ? settings.OriginUmm : settings.OriginVmm) + rib.CoordinateMm) * .001;
                if (!IsUnit(profile.AlongDirection, isLong ? v : u) || !IsUnit(profile.HeightDirection, h)
                    || !IsUnit(profile.ExtrusionDirection, across)
                    || profile.PointsWorldMeters.Any(p => Math.Abs(p[across] - station) > GeometryTolerance))
                    throw new InvalidOperationException(rib.Id + ": contour lệch mặt phẳng định vị thanh.");
                int contourCount = points.Length - 2;
                double end = points[contourCount - 1][0];
                for (int i = 0; i < contourCount; i++)
                    if (points[i][1] <= 0 || (i > 0 && points[i][0] <= points[i - 1][0] + GeometryTolerance))
                        throw new InvalidOperationException(rib.Id + ": contour không tiến đều theo trục thanh.");
                if (Math.Abs(points[contourCount][0] - end) > GeometryTolerance
                    || Math.Abs(points[contourCount + 1][0]) > GeometryTolerance
                    || Math.Abs(points[contourCount][1]) > GeometryTolerance
                    || Math.Abs(points[contourCount + 1][1]) > GeometryTolerance)
                    throw new InvalidOperationException(rib.Id + ": đáy contour lệch datum.");

                double origin = Dot(profile.PointsWorldMeters[0], profile.AlongDirection);
                double alongDatum = (isLong ? settings.OriginVmm : settings.OriginUmm) * .001;
                double minWeb = expected; // Screening bound, independent of manufacturing approval.
                double previousRight = 0;
                var cuts = new List<KadaiJointCut>();
                foreach (var slot in specifications[rib.Id].OrderBy(slot => slot.CenterAlongRibMm))
                {
                    var mate = byId[slot.CrossingRibId];
                    if (mate.Direction == profile.Direction || !Finite(slot.WidthMm) || !Finite(slot.CenterAlongRibMm)
                        || slot.WidthMm * .001 <= mate.ThicknessMeters + GeometryTolerance)
                        throw new InvalidOperationException(slot.Id + ": rãnh cần độ hở dương với thanh đối ứng.");
                    double center = alongDatum + slot.CenterAlongRibMm * .001 - origin;
                    double halfWidth = slot.WidthMm * .0005;
                    double left = center - halfWidth, right = center + halfWidth;
                    if (left - previousRight < minWeb || end - right < minWeb)
                        throw new InvalidOperationException(slot.Id + ": rãnh vượt biên hoặc quá gần đầu thanh/rãnh kế bên.");

                    var heights = points.Take(contourCount).Where(p => p[0] > left && p[0] < right)
                        .Select(p => p[1]).Concat(new[] { HeightAt(points, contourCount, left), HeightAt(points, contourCount, right) });
                    if (seam - clearance < minWeb || heights.Min() - seam < minWeb)
                        throw new InvalidOperationException(slot.Id + ": thiếu vật liệu quanh mặt ghép hoặc độ hở sâu quá lớn.");

                    // Long ribs have an open bottom slot. Cross ribs move up from below,
                    // with an open top slot that overlaps the lower cut by clearance.
                    cuts.Add(new KadaiJointCut { SlotId = slot.Id, MatingRibId = slot.CrossingRibId,
                        Left = left, Right = right, Bottom = isLong ? -SketchOverrun : seam - clearance,
                        Top = isLong ? seam : heights.Max() + SketchOverrun });
                    previousRight = right;
                }
                int expectedCuts = isLong ? plan.CrossRibs.Count : plan.LongRibs.Count;
                if (cuts.Count != expectedCuts || cuts.Select(cut => cut.MatingRibId).Distinct().Count() != expectedCuts)
                    throw new InvalidOperationException(rib.Id + ": thiếu hoặc trùng rãnh ghép.");
                result.Add(rib.Id, cuts);
            }
            return result;
        }

        internal static double[][] LocalPoints(KadaiProfile3D profile, double bottomMm)
        {
            if (!profile.Closed || profile.AlongDirection == null || profile.AlongDirection.Length != 3
                || profile.HeightDirection == null || profile.HeightDirection.Length != 3 || profile.PointsWorldMeters.Count < 4
                || profile.PointsWorldMeters.Any(p => p == null || p.Length != 3 || p.Any(v => !Finite(v))))
                throw new InvalidOperationException(profile.RibId + ": contour chưa khép kín hoặc tọa độ không hợp lệ.");
            double origin = Dot(profile.PointsWorldMeters[0], profile.AlongDirection);
            return profile.PointsWorldMeters.Select(p => new[] {
                Dot(p, profile.AlongDirection) - origin, Dot(p, profile.HeightDirection) - bottomMm * .001 }).ToArray();
        }

        internal static string SuggestMargins(KadaiPlan plan, IList<KadaiProfile3D> profiles)
        {
            var settings = plan.Settings;
            double longMargin = settings.LongEndMarginMm;
            double crossMargin = settings.CrossEndMarginMm;
            foreach (var profile in profiles)
            {
                bool isLong = profile.Direction == KadaiRibDirection.Longitudinal;
                double datum = isLong ? settings.OriginVmm : settings.OriginUmm;
                double span = isLong ? settings.LengthZmm : settings.LengthXmm;
                var coordinates = profile.PointsWorldMeters.Select(p => Dot(p, profile.AlongDirection) * 1000 - datum);
                double slotWidth = isLong ? settings.CrossThicknessMm + settings.LongSlotExtraMm
                    : settings.LongThicknessMm + settings.CrossSlotExtraMm;
                double reserve = slotWidth / 2 + profile.ThicknessMeters * 1000 + .01;
                double margin = Math.Ceiling(Math.Max(coordinates.Min() + reserve, span - coordinates.Max() + reserve) * 10) / 10;
                if (isLong) crossMargin = Math.Max(crossMargin, margin);
                else longMargin = Math.Max(longMargin, margin);
            }
            if (longMargin >= settings.LengthXmm / 2 || crossMargin >= settings.LengthZmm / 2)
                return "Không tìm được khoảng chung đối xứng trên các contour hiện tại.";
            if (longMargin == settings.LongEndMarginMm && crossMargin == settings.CrossEndMarginMm)
                return string.Empty;
            return "Gợi ý từ các contour hiện tại: lề dọc " + longMargin.ToString("0.0")
                + " mm, lề ngang " + crossMargin.ToString("0.0") + " mm. Đổi lề rồi Preview lại để kiểm tra.";
        }

        private static double HeightAt(double[][] points, int count, double coordinate)
        {
            for (int i = 1; i < count; i++)
            {
                var a = points[i - 1];
                var b = points[i];
                if (coordinate > b[0]) continue;
                return a[1] + (b[1] - a[1]) * (coordinate - a[0]) / (b[0] - a[0]);
            }
            throw new InvalidOperationException("Vị trí rãnh nằm ngoài contour.");
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static bool IsUnit(double[] direction, int axis)
        {
            return direction != null && direction.Length == 3
                && direction.Where((value, index) => value != (index == axis ? 1d : 0d)).Count() == 0;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
