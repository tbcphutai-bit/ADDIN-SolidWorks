using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands.CircleFaceTubeLayout;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.Kadai
{
    internal sealed class KadaiTubeSettings
    {
        internal readonly int? Count;
        internal readonly SmallEndSpacingMode Spacing;
        internal readonly double Diameter, Wall, Margin, SeatClearance;

        internal KadaiTubeSettings(int? count, SmallEndSpacingMode spacing, double diameterMm, double wallMm,
            double marginMm, double seatClearanceMm)
        {
            if (count < 2 || count > 150 || !(diameterMm > 2) || !(wallMm > 0 && wallMm * 2 < diameterMm)
                || marginMm < 0 || seatClearanceMm < 0 || seatClearanceMm > diameterMm / 4
                || (!count.HasValue && spacing != SmallEndSpacingMode.TangentContact))
                throw new ArgumentException("Kiểm tra số ống, đường kính, chiều dày và khe hở.");
            Count = count;
            Spacing = spacing;
            Diameter = diameterMm * .001;
            Wall = wallMm * .001;
            Margin = marginMm * .001;
            SeatClearance = seatClearanceMm * .001;
        }

        // Leave 2 mm of the nominal seat for the cylinder section to remove.
        internal double PanelOffset => Diameter - .002;
        internal double Radius => Diameter / 2;
    }

    internal sealed class KadaiTubeLayout
    {
        internal readonly KadaiTubeSettings Settings;
        internal readonly TubeAxis[] Axes;
        internal readonly Dictionary<string, List<TubeSeat>> Seats = new Dictionary<string, List<TubeSeat>>();

        internal KadaiTubeLayout(KadaiTubeSettings settings, TubeAxis[] axes)
        {
            Settings = settings;
            Axes = axes;
        }

        internal static string Id(int index) => "T-" + (index + 1).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);

        internal sealed class TubeSeat
        {
            internal readonly int TubeIndex;
            internal readonly double OutwardOffset, Radius;
            internal readonly Point3 Center, Normal, MajorDirection;

            internal TubeSeat(int tubeIndex, double outwardOffset, double radius, Point3 center, Point3 normal, Point3 majorDirection)
            {
                TubeIndex = tubeIndex;
                OutwardOffset = outwardOffset;
                Radius = radius;
                Center = center;
                Normal = normal;
                MajorDirection = majorDirection;
            }
        }
    }

    internal sealed partial class KadaiPreviewBackend
    {
        private KadaiTubeLayout CalculateTubes(KadaiTubeSettings settings, KadaiPanelSource panel, KadaiBaseDatum datum)
        {
            var face = Resolve(panel);
            var modeler = (Modeler)app.GetModeler();
            Action<string> log = message => System.Diagnostics.Trace.WriteLine("[KADAI TUBES] " + message);
            using (var existing = ExternalBodyContext.Create(app, panel.DocumentTransient, panel.PanelComponent, face, modeler, log))
            {
                Point3 ToAssembly(Point3 point) => Point3.Read(IntoPanelFrame(point.Array(), panel.ComponentPlacement, null, true));
                var solver = new SwFaceLayout(face, modeler, log, existing, ToAssembly);
                var result = solver.Analyze(settings.Count, settings.Diameter, settings.Margin, false, settings.Spacing);
                if (result.Issue != null) throw new InvalidOperationException(result.Issue);
                if (result.Axes.Length == 0 || result.Axes.Length > 150)
                    throw new InvalidOperationException("Số ống vượt giới hạn 150. Hãy nhập số ống.");
                foreach (var axis in result.Axes)
                {
                    foreach (var point in new[] { axis.A, (axis.A + axis.B) * .5, axis.B })
                    {
                        var nearest = face.GetClosestPointOn(point.X, point.Y, point.Z) as double[];
                        if (nearest == null || (point.Array()[datum.HeightAxis] - nearest[datum.HeightAxis]) * datum.HeightSign >= 0)
                            throw new InvalidOperationException("Chọn mặt dưới panel hướng về mặt đế để đặt ống.");
                    }
                }
                return new KadaiTubeLayout(settings, result.Axes);
            }
        }

        private void PlanTubeSeats(KadaiTubeLayout tubes, List<KadaiProfile3D> ribs)
        {
            var modeler = (Modeler)app.GetModeler();
            var envelopes = new List<Body2>();
            var supported = new bool[tubes.Axes.Length];
            try
            {
                foreach (var axis in tubes.Axes)
                    envelopes.Add(CreateTubeCylinder(modeler, axis, tubes.Settings.Radius));
                foreach (var profile in ribs)
                {
                    var seats = new List<KadaiTubeLayout.TubeSeat>();
                    tubes.Seats.Add(profile.RibId, seats);
                    var rib = CreatePreviewBody(profile);
                    try
                    {
                        for (int i = 0; i < tubes.Axes.Length; i++)
                        {
                            if (!Interferes(modeler, rib, envelopes[i])) continue;
                            if (!profile.HasPanelContact)
                                throw new InvalidOperationException("Ống chạm gân cố định " + profile.RibId + ". Hạ chiều cao gân cố định hoặc đổi phương đỡ.");
                            var seat = MakeTubeSeat(tubes, profile, i);
                            seats.Add(seat);
                            supported[i] = true;
                        }
                        if (seats.Count > 0)
                        {
                            var cut = CutTubePreview(rib, profile, tubes, seats);
                            rib = cut;
                        }
                        foreach (var tube in envelopes)
                            if (Interferes(modeler, rib, tube))
                                throw new InvalidOperationException("Preview còn giao thể tích giữa ống và gân " + profile.RibId + ".");
                    }
                    finally { ReleaseOwned(rib); }
                }
                int unsupported = Array.IndexOf(supported, false);
                if (unsupported >= 0)
                    throw new InvalidOperationException("Ống " + KadaiTubeLayout.Id(unsupported) + " chưa gặp gân đỡ. Kiểm tra phương, số gân và khoảng chừa.");
            }
            finally { foreach (var envelope in envelopes) ReleaseOwned(envelope); }
        }

        private static KadaiTubeLayout.TubeSeat MakeTubeSeat(KadaiTubeLayout tubes, KadaiProfile3D profile, int index)
        {
            var axis = tubes.Axes[index];
            var along = Point3.Read(profile.AlongDirection);
            var normal = along.Cross(Point3.Read(profile.HeightDirection));
            // The native front plane is the +normal face. Base Flange extrudes toward -normal.
            var faceOrigin = Point3.Read(profile.PointsWorldMeters[0]) + normal * (profile.ThicknessMeters / 2);
            double alignment = normal.Dot(axis.Direction);
            if (Math.Abs(alignment) < .1)
                throw new InvalidOperationException("Ống gần song song với " + profile.RibId + ". Chọn phương gân đỡ cắt ngang ống.");
            double distance = (faceOrigin - axis.A).Dot(normal) / alignment;
            double inPlane = Math.Sqrt(Math.Max(0, 1 - alignment * alignment));
            // Cover the oblique section through the whole thickness of a Normal Cut.
            double radialOffset = tubes.Settings.SeatClearance + profile.ThicknessMeters * inPlane + .00001;
            double radius = tubes.Settings.Radius + radialOffset;
            double extent = radius * inPlane / Math.Abs(alignment) + .00001;
            if (distance < extent || distance > axis.Length - extent)
                throw new InvalidOperationException("Mép gân " + profile.RibId + " quá gần đầu " + KadaiTubeLayout.Id(index) + ". Chỉnh khoảng chừa.");
            var projected = axis.Direction - normal * alignment;
            return new KadaiTubeLayout.TubeSeat(index, radialOffset, radius, axis.A + axis.Direction * distance,
                normal, projected.Length > 1e-8 ? projected.Unit() : along);
        }

        private static Body2 CreateTubeCylinder(Modeler modeler, TubeAxis axis, double radius)
        {
            return modeler.CreateBodyFromCyl(axis.A.Array().Concat(axis.Direction.Array())
                .Concat(new[] { radius, axis.Length }).ToArray()) as Body2
                ?? throw new InvalidOperationException("Không dựng được body trụ ống.");
        }

        private static bool Interferes(Modeler modeler, Body2 first, Body2 second)
        {
            object a = null, b = null, common = null;
            return modeler.CheckInterference3(new[] { first }, new[] { second }, 0, ref a, ref b, ref common);
        }

        private Body2 CutTubePreview(Body2 rib, KadaiProfile3D profile, KadaiTubeLayout tubes,
            List<KadaiTubeLayout.TubeSeat> seats)
        {
            var modeler = (Modeler)app.GetModeler();
            var math = (MathUtility)app.GetMathUtility();
            // Keep the input valid on failure. The caller owns the final result.
            var current = rib;
            try
            {
                foreach (var seat in seats)
                {
                    Curve ellipse = null;
                    Surface plane = null;
                    Body2 sheet = null, punch = null;
                    MathVector direction = null;
                    try
                    {
                        const double overrun = .00005;
                        var n = seat.Normal;
                        var major = seat.MajorDirection;
                        var minor = n.Cross(major);
                        var center = (seat.Center + n * overrun).Array();
                        double cosine = Math.Abs(tubes.Axes[seat.TubeIndex].Direction.Dot(n));
                        ellipse = modeler.CreateEllipse(center, seat.Radius / cosine, seat.Radius, major.Array(), minor.Array()) as Curve;
                        plane = modeler.CreatePlanarSurface2(center, n.Array(), major.Array()) as Surface;
                        if (ellipse == null || plane == null) throw new InvalidOperationException("Không dựng được tiết diện ống.");
                        sheet = plane.CreateTrimmedSheet5(new[] { ellipse }, true, 1e-7) as Body2;
                        direction = math.CreateVector((n * -1).Array()) as MathVector;
                        punch = sheet == null ? null : modeler.CreateExtrudedBody(sheet, direction, profile.ThicknessMeters + 2 * overrun);
                        if (punch == null) throw new InvalidOperationException("Không dựng được body cắt gân.");
                        var cut = SubtractBody(current, punch, "Không tạo được vết cắt Preview trên " + profile.RibId + ".");
                        if (!ReferenceEquals(current, rib)) ReleaseOwned(current);
                        current = cut;
                    }
                    finally
                    {
                        ReleaseOwned(punch);
                        ReleaseOwned(direction);
                        ReleaseOwned(sheet);
                        ReleaseOwned(plane);
                        ReleaseOwned(ellipse);
                    }
                }
            }
            catch
            {
                if (!ReferenceEquals(current, rib)) ReleaseOwned(current);
                throw;
            }
            if (!ReferenceEquals(current, rib)) ReleaseOwned(rib);
            return current;
        }

        private static Body2 SubtractBody(Body2 body, Body2 tool, string errorMessage)
        {
            var result = (body.Operations2((int)swBodyOperationType_e.SWBODYCUT, tool, out int error)
                as object[] ?? new object[0]).OfType<Body2>().ToArray();
            if (error == 0 && result.Length == 1) return result[0];
            foreach (var item in result) ReleaseOwned(item);
            throw new InvalidOperationException(errorMessage);
        }

        private Body2 CreateTubePreview(Modeler modeler, KadaiTubeLayout tubes, int index)
        {
            var axis = tubes.Axes[index];
            var outer = CreateTubeCylinder(modeler, axis, tubes.Settings.Radius);
            Body2 inner = null;
            try
            {
                const double overrun = .001;
                var bore = new TubeAxis(axis.A - axis.Direction * overrun, axis.B + axis.Direction * overrun);
                inner = CreateTubeCylinder(modeler, bore, tubes.Settings.Radius - tubes.Settings.Wall);
                return SubtractBody(outer, inner, "Không dựng được ống rỗng Preview " + KadaiTubeLayout.Id(index) + ".");
            }
            finally
            {
                ReleaseOwned(inner);
                ReleaseOwned(outer);
            }
        }
    }
}
