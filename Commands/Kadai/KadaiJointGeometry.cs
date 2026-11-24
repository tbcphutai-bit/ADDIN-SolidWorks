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
            var specifications = plan.CandidateSlots.Where(slot => !plan.Settings.PanelRibDirection.HasValue ||
                HasCrossing(byId[slot.OwnerRibId], byId[slot.CrossingRibId])).ToLookup(slot => slot.OwnerRibId);
            var result = new Dictionary<string, List<KadaiJointCut>>();
            foreach (var profile in profiles)
            {
                var rib = ribs[profile.RibId];
                bool isLong = rib.Direction == KadaiRibDirection.Longitudinal;
                var settings = plan.Settings;
                bool opensAtBottom = settings.OpensAtBottom(rib.Direction);
                double expected = (isLong ? settings.LongThicknessMm : settings.CrossThicknessMm) * .001;
                if (profile.Direction != rib.Direction || !Finite(profile.ThicknessMeters)
                    || Math.Abs(profile.ThicknessMeters - expected) > GeometryTolerance)
                    throw new InvalidOperationException(rib.Id + ": sai phương hoặc chiều dày contour.");

                var points = LocalPoints(profile, settings.BottomMm);
                int h = settings.HeightAxis;
                int u = h == 0 ? 1 : 0, v = h == 2 ? 1 : 2;
                int across = isLong ? u : v;
                double station = ((isLong ? settings.OriginUmm : settings.OriginVmm) + rib.CoordinateMm) * .001;
                if (!IsUnit(profile.AlongDirection, isLong ? v : u) || !IsUnit(profile.HeightDirection, h, settings.HeightSign)
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
                        throw new InvalidOperationException("Rãnh " + rib.Id + " / " + mate.RibId
                            + " quá sát đầu thanh hoặc rãnh kế bên. Đổi khoảng chừa hoặc số thanh.");

                    var heights = points.Take(contourCount).Where(p => p[0] > left && p[0] < right)
                        .Select(p => p[1]).Concat(new[] { HeightAt(points, contourCount, left), HeightAt(points, contourCount, right) });
                    if (seam - clearance < minWeb || heights.Min() - seam < minWeb)
                        throw new InvalidOperationException(slot.Id + ": thiếu vật liệu quanh mặt ghép hoặc độ hở sâu quá lớn.");

                    cuts.Add(new KadaiJointCut { SlotId = slot.Id, MatingRibId = slot.CrossingRibId,
                        Left = left, Right = right, Bottom = opensAtBottom ? -SketchOverrun : seam - clearance,
                        Top = opensAtBottom ? seam : heights.Max() + SketchOverrun });
                    previousRight = right;
                }
                int expectedCuts = settings.PanelRibDirection.HasValue ? specifications[rib.Id].Count()
                    : (isLong ? plan.CrossRibs.Count : plan.LongRibs.Count);
                if (cuts.Count != expectedCuts || cuts.Select(cut => cut.MatingRibId).Distinct().Count() != expectedCuts)
                    throw new InvalidOperationException(rib.Id + ": thiếu hoặc trùng rãnh ghép.");
                result.Add(rib.Id, cuts);
            }
            return result;
        }

        private static bool HasCrossing(KadaiProfile3D a, KadaiProfile3D b)
        {
            double aStart = Dot(a.PointsWorldMeters[0], a.AlongDirection);
            double aEnd = Dot(a.PointsWorldMeters[a.PointsWorldMeters.Count - 2], a.AlongDirection);
            double atB = Dot(b.PointsWorldMeters[0], a.AlongDirection);
            double bStart = Dot(b.PointsWorldMeters[0], b.AlongDirection);
            double bEnd = Dot(b.PointsWorldMeters[b.PointsWorldMeters.Count - 2], b.AlongDirection);
            double atA = Dot(a.PointsWorldMeters[0], b.AlongDirection);
            if (atB >= aStart && atB <= aEnd && atA >= bStart && atA <= bEnd) return true;
            if (atB + b.ThicknessMeters / 2 > aStart + GeometryTolerance && atB - b.ThicknessMeters / 2 < aEnd - GeometryTolerance
                && atA + a.ThicknessMeters / 2 > bStart + GeometryTolerance && atA - a.ThicknessMeters / 2 < bEnd - GeometryTolerance)
                throw new InvalidOperationException(a.RibId + " / " + b.RibId
                    + " chạm sát đầu thanh, không đủ chỗ tạo rãnh. Đổi khoảng chừa.");
            return false;
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

        internal static List<KadaiProfile3D> NotchedProfiles(KadaiPlan plan, IList<KadaiProfile3D> blanks,
            Dictionary<string, List<KadaiJointCut>> joints)
        {
            double bottom = plan.Settings.BottomMm;
            var result = new List<KadaiProfile3D>(blanks.Count);
            foreach (var blank in blanks)
            {
                var points = LocalPoints(blank, bottom);
                int contourCount = points.Length - 2;
                var perimeter = new List<double[]>();
                if (plan.Settings.OpensAtBottom(blank.Direction))
                {
                    perimeter.AddRange(points.Take(contourCount + 1));
                    foreach (var cut in joints[blank.RibId].AsEnumerable().Reverse())
                    {
                        perimeter.Add(new[] { cut.Right, 0d });
                        perimeter.Add(new[] { cut.Right, cut.Top });
                        perimeter.Add(new[] { cut.Left, cut.Top });
                        perimeter.Add(new[] { cut.Left, 0d });
                    }
                    perimeter.Add(points[contourCount + 1]);
                }
                else
                {
                    int next = 0;
                    foreach (var cut in joints[blank.RibId])
                    {
                        // Ignore a sample coincident with a shoulder to avoid short kernel edges.
                        while (next < contourCount && points[next][0] < cut.Left - GeometryTolerance)
                            perimeter.Add(points[next++]);
                        perimeter.Add(new[] { cut.Left, HeightAt(points, contourCount, cut.Left) });
                        perimeter.Add(new[] { cut.Left, cut.Bottom });
                        perimeter.Add(new[] { cut.Right, cut.Bottom });
                        perimeter.Add(new[] { cut.Right, HeightAt(points, contourCount, cut.Right) });
                        while (next < contourCount && points[next][0] <= cut.Right + GeometryTolerance)
                            next++;
                    }
                    perimeter.AddRange(points.Skip(next));
                }

                var notched = new KadaiProfile3D { RibId = blank.RibId, Direction = blank.Direction,
                    Closed = true, ThicknessMeters = blank.ThicknessMeters, AlongDirection = blank.AlongDirection,
                    HeightDirection = blank.HeightDirection, ExtrusionDirection = blank.ExtrusionDirection,
                    HasPanelContact = blank.HasPanelContact, ValidationMessage = blank.ValidationMessage };
                if (plan.Settings.OrientationHole != null)
                {
                    blank.OrientationHole = PlaceOrientationHole(plan.Settings.OrientationHole, blank, perimeter,
                        points[contourCount][0]);
                    notched.OrientationHole = blank.OrientationHole;
                }
                var origin = blank.PointsWorldMeters[blank.PointsWorldMeters.Count - 1];
                foreach (var point in perimeter)
                    notched.PointsWorldMeters.Add(Enumerable.Range(0, 3).Select(axis => origin[axis]
                        + point[0] * blank.AlongDirection[axis] + point[1] * blank.HeightDirection[axis]).ToArray());
                result.Add(notched);
            }
            return result;
        }

        private static KadaiOrientationHole PlaceOrientationHole(KadaiOrientationHoleSettings options,
            KadaiProfile3D rib, List<double[]> perimeter, double length)
        {
            double diameter = options.DiameterMm * .001;
            double endDistance = options.FromEndMm * .001;
            double y = options.FromBaseMm * .001;
            if (!Finite(diameter) || !Finite(endDistance) || !Finite(y) || diameter <= 0 || endDistance <= 0 || y <= 0)
                throw new InvalidOperationException("Nhập đường kính và hai khoảng cách tâm lỗ lớn hơn 0.");
            bool atFinish = rib.Direction == KadaiRibDirection.Longitudinal
                ? options.LongitudinalAtFinish : options.TransverseAtFinish;
            double x = atFinish ? length - endDistance : endDistance;
            bool inside = false;
            double limit = diameter * .5 + rib.ThicknessMeters;
            for (int i = 0; i < perimeter.Count; i++)
            {
                var a = perimeter[i];
                var b = perimeter[(i + 1) % perimeter.Count];
                double dx = b[0] - a[0], dy = b[1] - a[1];
                double norm = dx * dx + dy * dy;
                if (norm < 1e-16) continue;
                if ((a[1] > y) != (b[1] > y) && x < a[0] + dx * (y - a[1]) / dy) inside = !inside;
                double t = Math.Max(0, Math.Min(1, ((x - a[0]) * dx + (y - a[1]) * dy) / norm));
                double ex = x - a[0] - t * dx, ey = y - a[1] - t * dy;
                if (ex * ex + ey * ey < limit * limit)
                    throw OrientationHoleOutside(rib.RibId);
            }
            if (!inside) throw OrientationHoleOutside(rib.RibId);
            return new KadaiOrientationHole(x, y, diameter, atFinish ? length : 0);
        }

        private static InvalidOperationException OrientationHoleOutside(string ribId) =>
            new InvalidOperationException(ribId + ": lỗ định hướng quá sát mép hoặc rãnh. Chỉnh đường kính hoặc vị trí lỗ.");

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

        private static bool IsUnit(double[] direction, int axis, int sign = 1)
        {
            return direction != null && direction.Length == 3
                && direction.Where((value, index) => value != (index == axis ? sign : 0d)).Count() == 0;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
