using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands
{
    // Paper-space geometry only. The command owns COM reads and mutations.
    internal sealed class PartSectionLayoutSize
    {
        public string Name;
        public bool Vertical;
        public double Width, Height;
    }

    internal static class PartSectionLayoutPlanner
    {
        private const double Margin = 0.015;
        private const double Gap = 0.008;
        private const double Epsilon = 0.00000001;

        internal static double CalculatePreferredScale(double unitWidth, double unitHeight,
            double sheetWidth, double sheetHeight)
        {
            if (!Positive(unitWidth) || !Positive(unitHeight) ||
                !Positive(sheetWidth - 0.030) || !Positive(sheetHeight - 0.045)) return 0;
            double fit = Math.Min((sheetWidth - 0.030) / unitWidth,
                (sheetHeight - 0.045) / unitHeight);
            return RoundScale(0.8 * Math.Sqrt(fit), true);
        }

        // Ordinary ratios use one decimal. Below 0.1, use a one-decimal
        // reciprocal ratio (1:N); rounding toward fit can never exceed its cap.
        internal static double RoundScale(double value, bool fitInside)
        {
            if (!Positive(value)) return 0;
            if (value < 0.1)
            {
                double denominator = 1.0 / value;
                if (!Positive(denominator) || denominator > double.MaxValue / 10) return 0;
                denominator = fitInside ? Math.Ceiling(denominator * 10 - 1e-9) / 10 :
                    Math.Round(denominator, 1, MidpointRounding.AwayFromZero);
                double result = 1.0 / denominator;
                // Protect the fit contract at a decimal boundary affected by
                // floating point error; the native caller may use this as a cap.
                if (fitInside && result > value + Epsilon)
                    result = 1.0 / (denominator + 0.1);
                return result;
            }
            return fitInside ? Math.Floor(value * 10 + 1e-9) / 10 :
                Math.Round(value, 1, MidpointRounding.AwayFromZero);
        }

        internal static void GetTargets(double sheetWidth, double sheetHeight, bool vertical,
            out double width, out double height)
        {
            width = height = 0;
            double usableWidth = sheetWidth - 0.030;
            double usableHeight = sheetHeight - 0.045;
            if (!Positive(usableWidth) || !Positive(usableHeight)) return;
            width = usableWidth * (vertical ? 0.22 : 0.55);
            height = usableHeight * (vertical ? 0.60 : 0.30);
        }

        internal static bool TryArrange(IList<PartSectionLayoutSize> sizes,
            double[] parentBounds, double sheetWidth, double sheetHeight,
            IList<double[]> occupied, out Dictionary<string, double[]> centers)
        {
            centers = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            if (sizes == null || sizes.Count == 0 || !Positive(sheetWidth) || !Positive(sheetHeight)) return false;
            var ordered = sizes.OrderBy(size => size == null ? "" : size.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(size => size == null ? "" : size.Name, StringComparer.Ordinal).ToList();
            if (ordered.Any(size => size == null || string.IsNullOrWhiteSpace(size.Name) ||
                !Positive(size.Width) || !Positive(size.Height)) ||
                ordered.GroupBy(size => size.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)) return false;
            double[] body = BodyBounds(sheetWidth, sheetHeight);
            if (body[2] <= body[0] || body[3] <= body[1]) return false;
            var obstacles = (occupied ?? new List<double[]>()).Where(ValidBounds).Select(CloneBounds).ToList();
            var vertical = ordered.Where(size => size.Vertical).ToList();
            var horizontal = ordered.Where(size => !size.Vertical).ToList();
            if (ValidBounds(parentBounds))
            {
                // Even a partially off-sheet parent must not be covered by a
                // newly placed section. Fully parked construction views do not
                // intersect the sheet body and therefore impose no restriction.
                if (Intersects(parentBounds, body, 0)) obstacles.Add(CloneBounds(parentBounds));
                if (Contains(body, parentBounds))
                {
                    foreach (bool onLeft in new[] { true, false })
                    foreach (bool below in new[] { true, false })
                    {
                        var projected = ArrangeAroundParent(vertical, horizontal, parentBounds, onLeft, below);
                        if (Fits(projected, ordered, body, obstacles))
                        { centers = projected; return true; }
                    }
                    // Keep alignment with the parent; reducing the common scale
                    // is preferable to clamping one section over another view.
                    return false;
                }
            }

            double groupWidth, groupHeight;
            var relative = ArrangeParkedGroup(vertical, horizontal, out groupWidth, out groupHeight);
            if (groupWidth > body[2] - body[0] + Epsilon || groupHeight > body[3] - body[1] + Epsilon) return false;
            double preferredX = (body[0] + body[2]) / 2;
            double preferredY = (body[1] + body[3]) / 2;
            var xPositions = CandidateCenters(body[0], body[2], groupWidth, preferredX, obstacles, 0, relative, ordered);
            var yPositions = CandidateCenters(body[1], body[3], groupHeight, preferredY, obstacles, 1, relative, ordered);
            // Center the coherent group first. Obstacle-boundary candidates allow
            // moving the group without losing its internal relative positions.
            var placements = from x in xPositions from y in yPositions
                orderby Math.Abs(x - preferredX) + Math.Abs(y - preferredY),
                    Math.Abs(y - preferredY), x, y
                select new[] { x, y };
            foreach (double[] position in placements)
            {
                var candidate = relative.ToDictionary(pair => pair.Key,
                    pair => new[] { pair.Value[0] + position[0], pair.Value[1] + position[1] },
                    StringComparer.OrdinalIgnoreCase);
                if (Fits(candidate, ordered, body, obstacles))
                { centers = candidate; return true; }
            }
            return false;
        }

        private static Dictionary<string, double[]> ArrangeAroundParent(
            List<PartSectionLayoutSize> vertical, List<PartSectionLayoutSize> horizontal,
            double[] parent, bool onLeft, bool below)
        {
            var result = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            double parentX = (parent[0] + parent[2]) / 2;
            double parentY = (parent[1] + parent[3]) / 2;
            double cursor = onLeft ? parent[0] - Gap : parent[2] + Gap;
            foreach (PartSectionLayoutSize size in vertical)
            {
                double x = cursor + (onLeft ? -size.Width / 2 : size.Width / 2);
                result.Add(size.Name, new[] { x, parentY });
                cursor += (onLeft ? -1 : 1) * (size.Width + Gap);
            }
            cursor = below ? parent[1] - Gap : parent[3] + Gap;
            foreach (PartSectionLayoutSize size in horizontal)
            {
                double y = cursor + (below ? -size.Height / 2 : size.Height / 2);
                result.Add(size.Name, new[] { parentX, y });
                cursor += (below ? -1 : 1) * (size.Height + Gap);
            }
            return result;
        }

        private static Dictionary<string, double[]> ArrangeParkedGroup(
            List<PartSectionLayoutSize> vertical, List<PartSectionLayoutSize> horizontal,
            out double width, out double height)
        {
            double verticalWidth = vertical.Sum(size => size.Width) + Math.Max(0, vertical.Count - 1) * Gap;
            double verticalHeight = vertical.Select(size => size.Height).DefaultIfEmpty(0).Max();
            double horizontalWidth = horizontal.Select(size => size.Width).DefaultIfEmpty(0).Max();
            double horizontalHeight = horizontal.Sum(size => size.Height) + Math.Max(0, horizontal.Count - 1) * Gap;
            double groupGap = vertical.Count > 0 && horizontal.Count > 0 ? Gap : 0;
            width = verticalWidth + horizontalWidth + groupGap;
            height = Math.Max(verticalHeight, horizontalHeight);
            var result = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            double cursor = -width / 2;
            foreach (PartSectionLayoutSize size in vertical)
            {
                result.Add(size.Name, new[] { cursor + size.Width / 2, 0.0 });
                cursor += size.Width + Gap;
            }
            double horizontalCenterX = -width / 2 + verticalWidth + groupGap + horizontalWidth / 2;
            double verticalCursor = horizontalHeight / 2;
            foreach (PartSectionLayoutSize size in horizontal)
            {
                result.Add(size.Name, new[] { horizontalCenterX, verticalCursor - size.Height / 2 });
                verticalCursor -= size.Height + Gap;
            }
            return result;
        }

        private static List<double> CandidateCenters(double minimum, double maximum,
            double extent, double preferred, IList<double[]> obstacles, int axis,
            Dictionary<string, double[]> relative, IList<PartSectionLayoutSize> sizes)
        {
            double low = minimum + extent / 2, high = maximum - extent / 2;
            var values = new List<double> { Math.Max(low, Math.Min(high, preferred)), low, high };
            foreach (double[] obstacle in obstacles)
            {
                values.Add(obstacle[axis] - Gap - extent / 2);
                values.Add(obstacle[axis + 2] + Gap + extent / 2);
                foreach (PartSectionLayoutSize size in sizes)
                {
                    double half = (axis == 0 ? size.Width : size.Height) / 2;
                    double offset = relative[size.Name][axis];
                    values.Add(obstacle[axis] - Gap - half - offset);
                    values.Add(obstacle[axis + 2] + Gap + half - offset);
                }
            }
            return values.Where(value => value >= low - Epsilon && value <= high + Epsilon)
                .Select(value => Math.Max(low, Math.Min(high, value))).Distinct().ToList();
        }

        private static bool Fits(Dictionary<string, double[]> positions,
            List<PartSectionLayoutSize> sizes, double[] body, IList<double[]> obstacles)
        {
            var placed = new List<double[]>();
            foreach (PartSectionLayoutSize size in sizes)
            {
                double[] position;
                if (!positions.TryGetValue(size.Name, out position)) return false;
                var rectangle = new[] { position[0] - size.Width / 2, position[1] - size.Height / 2,
                    position[0] + size.Width / 2, position[1] + size.Height / 2 };
                if (!Contains(body, rectangle) || obstacles.Any(other => Intersects(rectangle, other, Gap)) ||
                    placed.Any(other => Intersects(rectangle, other, Gap))) return false;
                placed.Add(rectangle);
            }
            return true;
        }

        private static double[] BodyBounds(double width, double height)
        {
            // Header/title reserves grow on larger sheets and stay useful at A4.
            double bottom = Math.Max(0.025, height * 0.08);
            double top = height - Math.Max(0.030, height * 0.12);
            return new[] { Margin, bottom, width - Margin, top };
        }
        private static bool Contains(double[] outer, double[] inner)
        { return inner[0] >= outer[0] - Epsilon && inner[1] >= outer[1] - Epsilon &&
            inner[2] <= outer[2] + Epsilon && inner[3] <= outer[3] + Epsilon; }
        private static bool Intersects(double[] a, double[] b, double clearance)
        { return a[0] < b[2] + clearance - Epsilon && a[2] > b[0] - clearance + Epsilon &&
            a[1] < b[3] + clearance - Epsilon && a[3] > b[1] - clearance + Epsilon; }
        private static bool ValidBounds(double[] value)
        { return value != null && value.Length >= 4 && value.Take(4).All(Finite) &&
            value[2] > value[0] && value[3] > value[1]; }
        private static double[] CloneBounds(double[] value)
        { return new[] { value[0], value[1], value[2], value[3] }; }
        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Positive(double value)
        { return Finite(value) && value > 0; }
    }
}
