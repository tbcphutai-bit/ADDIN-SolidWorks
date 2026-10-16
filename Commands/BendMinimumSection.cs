using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands
{
    internal sealed class BendMinimumDatum
    {
        internal string Name;
        internal SectionProfilePlanner.Point Start, End;
        internal double ValueMm;
    }

    internal sealed class BendMinimumSection
    {
        internal string Shape;
        internal double ThicknessMm, A, B, C;
        internal List<SectionProfilePlanner.Curve> Boundary;
        internal List<SectionProfilePlanner.Curve> LocalBoundary;
        internal string Context;
        internal int FirstLeg, LastLeg, ProfileLegCount;
        internal string Dimension => Shape == "L" ? "A" : Shape == "Z" ? "B" : Shape == "C" || Shape == "U" ? "C" : null;
        internal double DimensionMm => Dimension == "A" ? A : Dimension == "B" ? B : C;
        internal readonly List<BendMinimumDatum> Datums = new List<BendMinimumDatum>();
    }

    // Measures a validated two-skin section in MODEL metres. Never use a world
    // bounding box, developed length, tangent length or the selected skin's DIM.
    internal static class BendMinimumSectionReader
    {
        internal const double GeometryTolerance = 0.00000001;
        private static double Dot(SectionProfilePlanner.Point a, SectionProfilePlanner.Point b) => SectionProfilePlanner.Dot(a, b);
        private static double Cross(SectionProfilePlanner.Point a, SectionProfilePlanner.Point b) => a.X * b.Y - a.Y * b.X;

        internal static bool TryMeasure(List<SectionProfilePlanner.Curve> curves, object seed,
            out BendMinimumSection section, out string reason)
        {
            List<BendMinimumSection> sections;
            bool measured = TryMeasureAll(curves, seed, out sections, out reason);
            section = sections.FirstOrDefault(); return measured;
        }

        private sealed class Leg
        {
            internal SectionProfilePlanner.Span Side;
            internal List<SectionProfilePlanner.Curve> Selected, Opposite;
            internal double[] Planes(SectionProfilePlanner.Point axis)
            { return new[] { Dot(Side.Start, axis), Dot(Opposite[0].A, axis) }; }
        }
        internal static bool TryMeasureAll(List<SectionProfilePlanner.Curve> curves, object seed,
            out List<BendMinimumSection> sections, out string reason)
        {
            sections = new List<BendMinimumSection>();
            // Accept only an actually closed, unbranched component through the
            // chosen bend. This prevents the shared drawing planner's topology
            // repair from inventing a missing arc or joining another section.
            var seeds = curves == null ? new List<SectionProfilePlanner.Curve>() : curves.Where(c => ReferenceEquals(c.Source, seed)).ToList();
            if (seeds.Count != 1) { reason = "Không xác định duy nhất được cung của vị trí chấn."; return false; }
            var component = new HashSet<SectionProfilePlanner.Curve>();
            var pending = new Queue<SectionProfilePlanner.Curve>();pending.Enqueue(seeds[0]);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();if (!component.Add(current)) continue;
                if ((current.B - current.A).Length <= GeometryTolerance)
                { reason = "Mặt cắt có đường tròn kín hoặc cạnh suy biến; chưa phải chuỗi L/U/Z."; return false; }
                foreach (var endpoint in new[] { current.A, current.B })
                {
                    var neighbors = curves.Where(c => !ReferenceEquals(c, current) &&
                        ((c.A - endpoint).Length <= GeometryTolerance || (c.B - endpoint).Length <= GeometryTolerance)).ToList();
                    if (neighbors.Count != 1)
                    { reason = "Mặt cắt bị hở hoặc phân nhánh; không tự nối cạnh/cung để suy đoán biên dạng."; return false; }
                    if (!component.Contains(neighbors[0])) pending.Enqueue(neighbors[0]);
                }
            }
            SectionProfilePlanner.Plan plan;
            // Disable the drawing planner's missing-arc repair: the solid kernel
            // must provide the actual boundary, without a guessed bend connection.
            if (!SectionProfilePlanner.TryBuild(component.ToList(), seed, GeometryTolerance, GeometryTolerance, out plan, out reason)) return false;
            if (plan.Curved != null || plan.Lengths.Count < 2)
            { reason = "Mặt cắt có vách cong hoặc chưa có chuỗi mặt phẳng tương ứng để nhận dạng cụm chấn."; return false; }
            var selectedJoins = Enumerable.Range(1, plan.Lengths.Count - 1)
                .Where(i => ReferenceEquals(plan.Lengths[i].Side.BendBefore?.Source, seed)).ToList();
            if (selectedJoins.Count != 1)
            { reason = "Không ánh xạ duy nhất được đúng nếp chấn trong chuỗi mặt cắt."; return false; }
            int join = selectedJoins[0];
            var errors = new List<string>();
            // A belongs only to a real free terminal flange at this bend. An
            // interior leg has its own B/C role; never reuse it as a local A.
            foreach (int terminal in new[] { 0, plan.Lengths.Count - 1 })
            {
                if (join != (terminal == 0 ? 1 : plan.Lengths.Count - 1)) continue;
                try { sections.Add(MeasureFreeEnd(plan, terminal)); }
                catch (InvalidOperationException ex) { errors.Add("Cánh tự do đoạn " + (terminal + 1) + ": " + ex.Message); }
            }
            for (int first = Math.Max(0, join - 2); first <= Math.Min(join - 1, plan.Lengths.Count - 3); first++)
            {
                try { sections.Add(MeasureWindow(plan, first)); }
                catch (InvalidOperationException ex) { errors.Add("Cụm đoạn " + (first + 1) + "–" + (first + 3) + ": " + ex.Message); }
            }
            reason = errors.Count == 0 ? null : string.Join(" ", errors);
            return sections.Count > 0;
        }
        private static BendMinimumSection MeasureFreeEnd(SectionProfilePlanner.Plan plan, int index)
        {
            var leg = PairLeg(plan, index);
            int neighbor = index == 0 ? 1 : index - 1;
            var web = PairLeg(plan, neighbor);
            var axis = leg.Side.Direction;
            if (Math.Abs(Dot(axis, web.Side.Direction)) > 0.000001)
                throw new InvalidOperationException("nếp chấn L khác 90 độ; bảng chưa có giới hạn cho góc này.");
            var end = index == 0 ? leg.Side.Start : leg.Side.End;
            double cap = Dot(end, axis);
            var dimension = Terminal(end, axis, web.Planes(axis), new[] { cap });
            dimension.Name = "A";
            var bend = plan.Lengths[index == 0 ? neighbor : index].Side.BendBefore;
            var result = new BendMinimumSection { Shape = "L", A = dimension.ValueMm, ThicknessMm = plan.Thickness * 1000,
                Boundary = plan.Boundary, FirstLeg = Math.Min(index, neighbor) + 1, LastLeg = Math.Max(index, neighbor) + 1,
                ProfileLegCount = plan.Lengths.Count, Context = "L / A, cánh tự do đoạn " + (index + 1) + " trong " + plan.Lengths.Count + " đoạn" };
            result.Datums.Add(dimension);
            result.LocalBoundary = leg.Selected.Concat(leg.Opposite).Concat(web.Selected).Concat(web.Opposite)
                .Concat(plan.Boundary.Where(c => c.IsArc && bend != null && (c.Center - bend.Center).Length <= GeometryTolerance * 3
                    || !c.IsArc && Math.Abs(Dot(c.A, axis) - cap) <= GeometryTolerance && Math.Abs(Dot(c.B, axis) - cap) <= GeometryTolerance
                    && (c.A - end).Length <= plan.Thickness + GeometryTolerance * 3)).Distinct().ToList();
            if (result.A <= 0 || !Finite(result.A)) throw new InvalidOperationException("A phủ bì đo được không hợp lệ.");
            return result;
        }
        private static BendMinimumSection MeasureWindow(SectionProfilePlanner.Plan plan, int first)
        {
            var spans = plan.Lengths.Select(l => l.Side).ToList();
            var before = spans[first].Direction; var axis = spans[first + 1].Direction; var after = spans[first + 2].Direction;
            if (Math.Abs(Dot(before, axis)) > 0.000001 || Math.Abs(Dot(axis, after)) > 0.000001)
                throw new InvalidOperationException("có nếp chấn khác 90 độ; chưa có giới hạn cho góc này.");
            string shape = Math.Sign(Cross(before, axis)) == Math.Sign(Cross(axis, after)) ? "C" : "Z";
            var left = PairLeg(plan, first); var web = PairLeg(plan, first + 1); var right = PairLeg(plan, first + 2);
            var result = new BendMinimumSection { Shape = shape, ThicknessMm = plan.Thickness * 1000, Boundary = plan.Boundary,
                FirstLeg = first + 1, LastLeg = first + 3, ProfileLegCount = spans.Count,
                Context = shape + " / " + (shape == "Z" ? "B" : "C") + ", đoạn " + (first + 2) + " giữa hai nếp chấn (" + spans.Count + " đoạn)" };
            // B/C is the distance between the far planes of this window's
            // terminal flanges. Other return lips cannot change its dimension.
            var heights = left.Planes(axis).Concat(right.Planes(axis)).ToList();
            var origin = (web.Side.Start + web.Side.End) * .5;
            var height = new BendMinimumDatum { Name = shape == "Z" ? "B" : "C", ValueMm = (heights.Max() - heights.Min()) * 1000,
                Start = origin + axis * (heights.Min() - Dot(origin, axis)), End = origin + axis * (heights.Max() - Dot(origin, axis)) };
            if (shape == "Z") result.B = height.ValueMm; else result.C = height.ValueMm;
            result.Datums.Add(height);
            result.LocalBoundary = left.Selected.Concat(left.Opposite).Concat(web.Selected).Concat(web.Opposite).Concat(right.Selected).Concat(right.Opposite)
                .Concat(plan.Boundary.Where(c => c.IsArc && new[] { spans[first + 1].BendBefore, spans[first + 2].BendBefore }
                    .Any(b => b != null && (b.Center - c.Center).Length <= GeometryTolerance * 3))).Distinct().ToList();
            if (height.ValueMm <= 0 || !Finite(height.ValueMm))
                throw new InvalidOperationException("kích thước phủ bì đo được không hợp lệ.");
            return result;
        }
        private static Leg PairLeg(SectionProfilePlanner.Plan plan, int index)
        {
            var span = plan.Lengths[index].Side; var direction = span.Direction;
            var normal = new SectionProfilePlanner.Point(-direction.Y, direction.X);
            double selected = Dot(span.Start, normal), minimum = Math.Min(Dot(span.Start, direction), Dot(span.End, direction)), maximum = Math.Max(Dot(span.Start, direction), Dot(span.End, direction));
            var lines = plan.Boundary.Where(c => !c.IsArc && (c.B - c.A).Length > GeometryTolerance && Math.Abs(Cross((c.B - c.A).Unit, direction)) < 0.000001
                && Math.Min(maximum, Math.Max(Dot(c.A, direction), Dot(c.B, direction))) - Math.Max(minimum, Math.Min(Dot(c.A, direction), Dot(c.B, direction))) > GeometryTolerance).ToList();
            var candidates = lines.Where(c => Math.Abs(Math.Abs(Dot(c.A, normal) - selected) - plan.Thickness) <= GeometryTolerance * 3).ToList();
            var offsets = candidates.Select(c => Dot(c.A, normal)).Distinct(new OffsetComparer()).ToList();
            if (offsets.Count != 1) throw new InvalidOperationException("chưa xác định duy nhất hai mặt của đoạn " + (index + 1) + " để đo phủ bì.");
            return new Leg { Side = span, Selected = lines.Where(c => Math.Abs(Dot(c.A, normal) - selected) <= GeometryTolerance * 3).ToList(),
                Opposite = candidates.Where(c => Math.Abs(Dot(c.A, normal) - offsets[0]) <= GeometryTolerance * 3).ToList() };
        }

        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        private sealed class OffsetComparer : IEqualityComparer<double>
        {
            public bool Equals(double a, double b) => Math.Abs(a - b) <= GeometryTolerance;
            public int GetHashCode(double value) => 0;
        }
        private static BendMinimumDatum Terminal(SectionProfilePlanner.Point reference, SectionProfilePlanner.Point normal, double[] walls, double[] ends)
        {
            var choice = walls.SelectMany(w => ends.Select(e => new { Wall = w, End = e, Distance = Math.Abs(w - e) })).OrderByDescending(c => c.Distance).First();
            return new BendMinimumDatum { Start = reference + normal * (choice.Wall - Dot(reference, normal)),
                End = reference + normal * (choice.End - Dot(reference, normal)), ValueMm = choice.Distance * 1000 };
        }
    }
}
