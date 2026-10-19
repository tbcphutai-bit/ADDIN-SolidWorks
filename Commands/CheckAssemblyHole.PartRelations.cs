// CG2 / PART RELATIONS V1 — READ-ONLY SPATIAL SCREENING.
// C# 7.3 / .NET Framework 4.8 / SOLIDWORKS.
// AABB is broad-phase ONLY: overlap or axis crossing does NOT prove solid
// contact, material removal, intended fastening, or a missing-hole NG.
// No selection, feature edits, Part edits, master decisions, or production NG.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands
{
    internal sealed class CheckHoleCg2RelationRow
    {
        public int Id { get; set; }
        public string FirstOccurrence { get; set; }
        public string SecondOccurrence { get; set; }
        public double BoxSeparationMm { get; set; }
        public string Evidence { get; set; }
        public string Confidence { get; set; }
    }

    internal sealed class CheckHoleCg2ExpectedHoleRow
    {
        public int Id { get; set; }
        public string SourceOccurrence { get; set; }
        public string TargetOccurrence { get; set; }
        public string SourceHoleLabel { get; set; }
        public double BoxSeparationMm { get; set; }
        public double AxisToBoxMm { get; set; }
        public string Classification { get; set; }
        public string Reason { get; set; }
    }

    internal sealed class CheckHoleCg2Report
    {
        public int TotalOccurrenceCount;
        public int EligibleComponentCount;
        public int ComponentsWithBounds;
        public int ComponentsWithoutDetectedHoles;
        public int SkippedOrUnreadableCount;
        public int ComparedComponentPairs;
        public int TestedHoleRelations;
        public int RecordedNearbyPairs;
        public int CandidateCount;
        public bool IsPartial;
        public string CoverageStatus;
        public readonly List<CheckHoleCg2RelationRow> NearbyPairs =
            new List<CheckHoleCg2RelationRow>();
        public readonly List<CheckHoleCg2ExpectedHoleRow> ExpectedHoleCandidates =
            new List<CheckHoleCg2ExpectedHoleRow>();
        public readonly List<string> Warnings = new List<string>();
    }

    public sealed partial class CheckAssemblyHole
    {
        // These CG2 diagnostic bounds are NOT position or angle NG tolerances.
        private const double Cg2NearBoxGapM = 0.002;    // broad-phase AABB gap: 2 mm
        private const double Cg2AxisBoxPadM = 0.0005;   // 0.5 mm for broad phase
        private const double Cg2MaxAxisReachM = 0.060;  // 60 mm around source hole
        private const double Cg2KnownHoleRadialM = 0.004; // suppress likely known counterpart
        private const double Cg2KnownHoleAngleDeg = 3.0;
        private const int Cg2MaxPairChecks = 300000;
        private const int Cg2MaxHoleChecks = 500000;
        private const int Cg2MaxStoredPairs = 1500;
        private const int Cg2MaxStoredCandidates = 2000;

        private sealed class Cg2Box
        {
            public string Occurrence;
            public double[] Min;
            public double[] Max;
        }

        /// <summary>
        /// Scans ALL eligible component OCCURRENCES including those with zero
        /// detected holes. Returns low-confidence AABB candidates for CG3/CG4
        /// investigation. Nothing here creates or changes production NG.
        /// </summary>
        private CheckHoleCg2Report BuildCg2PartRelations(
            AssemblyDoc assembly, List<HoleAxis> holes)
        {
            if (assembly == null) throw new ArgumentNullException("assembly");
            if (holes == null) throw new ArgumentNullException("holes");

            CheckHoleCg2Report report = new CheckHoleCg2Report();
            object[] components = ToObjectArray(assembly.GetComponents(false));
            report.TotalOccurrenceCount = components.Length;
            var boxes = new List<Cg2Box>();
            var indexed = new Dictionary<string, Cg2Box>(StringComparer.OrdinalIgnoreCase);
            var byOccurrence = holes.Where(h => h != null)
                .GroupBy(h => h.ComponentOccurrence ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < components.Length; i++)
            {
                if ((i & 31) == 0) ThrowIfCanceled();
                Component2 component = components[i] as Component2;
                if (!IsUsableComponent(component))
                {
                    report.SkippedOrUnreadableCount++;
                    continue;
                }
                report.EligibleComponentCount++;
                string occurrence = SafeComponentName(component);
                if (string.IsNullOrWhiteSpace(occurrence) || indexed.ContainsKey(occurrence))
                {
                    report.SkippedOrUnreadableCount++;
                    Cg2Warn(report, "Empty or repeated component occurrence identity; skipped.");
                    continue;
                }

                try
                {
                    // Component2.GetBox returns *approximate assembly-space* AABB.
                    // Do not use it as proof of actual material intersection.
                    double[] raw = ToDoubleArray(component.GetBox(false, false));
                    Cg2Box b = Cg2ReadBox(occurrence, raw);
                    if (b == null)
                    {
                        report.SkippedOrUnreadableCount++;
                        Cg2Warn(report, "Invalid bounding box: " + occurrence);
                        continue;
                    }
                    boxes.Add(b);
                    indexed.Add(occurrence, b);
                    if (!byOccurrence.ContainsKey(occurrence))
                        report.ComponentsWithoutDetectedHoles++;
                }
                catch (Exception ex)
                {
                    report.SkippedOrUnreadableCount++;
                    Cg2Warn(report, "Cannot read bounds for " + occurrence + ": " + ex.GetType().Name);
                }
            }

            boxes.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Occurrence, b.Occurrence));
            var adjacent = new Dictionary<string, List<Tuple<Cg2Box, double>>>(
                StringComparer.OrdinalIgnoreCase);
            bool pairBudgetExhausted = false;
            for (int i = 0; i < boxes.Count && !pairBudgetExhausted; i++)
            {
                if ((i & 31) == 0) ThrowIfCanceled();
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    if (report.ComparedComponentPairs >= Cg2MaxPairChecks)
                    {
                        report.IsPartial = true;
                        pairBudgetExhausted = true;
                        break;
                    }
                    report.ComparedComponentPairs++;
                    double gap = Cg2BoxGap(boxes[i], boxes[j]);
                    if (gap > Cg2NearBoxGapM) continue;
                    report.RecordedNearbyPairs++;
                    Cg2AddNeighbor(adjacent, boxes[i], boxes[j], gap);
                    Cg2AddNeighbor(adjacent, boxes[j], boxes[i], gap);
                    if (report.NearbyPairs.Count < Cg2MaxStoredPairs)
                    {
                        report.NearbyPairs.Add(new CheckHoleCg2RelationRow
                        {
                            Id = report.NearbyPairs.Count + 1,
                            FirstOccurrence = boxes[i].Occurrence,
                            SecondOccurrence = boxes[j].Occurrence,
                            BoxSeparationMm = gap * 1000.0,
                            Evidence = "AABB_NEAR_ONLY",
                            Confidence = "CANDIDATE_REVIEW"
                        });
                    }
                    else report.IsPartial = true;
                }
            }

            int sourceHoleId = 0;
            bool holeBudgetExhausted = false;
            foreach (HoleAxis hole in holes)
            {
                if ((sourceHoleId++ & 63) == 0) ThrowIfCanceled();
                if (!Cg2ValidVector(hole == null ? null : hole.Center) ||
                    !Cg2ValidVector(hole.Direction)) continue;

                string source = hole.ComponentOccurrence ?? "";
                List<Tuple<Cg2Box, double>> neighbors;
                if (!adjacent.TryGetValue(source, out neighbors)) continue;

                foreach (Tuple<Cg2Box, double> n in neighbors)
                {
                    if (report.TestedHoleRelations >= Cg2MaxHoleChecks)
                    {
                        report.IsPartial = true;
                        holeBudgetExhausted = true;
                        break;
                    }
                    report.TestedHoleRelations++;

                    double distanceOnAxis;
                    if (!Cg2AxisCrossesBoxNear(hole.Center, hole.Direction,
                        n.Item1, out distanceOnAxis)) continue;

                    List<HoleAxis> targetHoles;
                    if (byOccurrence.TryGetValue(n.Item1.Occurrence, out targetHoles)
                        && targetHoles.Any(h => Cg2LikelyKnownCounterpart(hole, h)))
                        continue;

                    report.CandidateCount++;
                    if (report.ExpectedHoleCandidates.Count < Cg2MaxStoredCandidates)
                    {
                        report.ExpectedHoleCandidates.Add(new CheckHoleCg2ExpectedHoleRow
                        {
                            Id = report.ExpectedHoleCandidates.Count + 1,
                            SourceOccurrence = source,
                            TargetOccurrence = n.Item1.Occurrence,
                            SourceHoleLabel = "Hole#" + sourceHoleId.ToString(CultureInfo.InvariantCulture),
                            BoxSeparationMm = n.Item2 * 1000.0,
                            AxisToBoxMm = distanceOnAxis * 1000.0,
                            Classification = "CHECK_ONLY",
                            Reason = "Axis crosses approximate component bounding box; " +
                                "actual material/contact/fastening intent NOT verified."
                        });
                    }
                    else report.IsPartial = true;
                }
                if (holeBudgetExhausted) break;
            }

            report.ComponentsWithBounds = boxes.Count;
            if (report.SkippedOrUnreadableCount > 0 ||
                report.IsPartial || boxes.Count == 0)
                report.CoverageStatus = "PARTIAL_UNVERIFIED";
            else
                report.CoverageStatus = "BBOX_SCREENED_NOT_MATERIAL_VERIFIED";

            // Explicitly communicate if all boxes are screened but there is
            // still NO proof of contact, mate, solid at axis, or missing hole.
            return report;
        }

        private static void Cg2Warn(CheckHoleCg2Report report, string warning)
        {
            if (report.Warnings.Count < 20)
                report.Warnings.Add(warning);
        }

        private static Cg2Box Cg2ReadBox(string occurrence, double[] raw)
        {
            if (raw == null || raw.Length < 6) return null;
            var min = new double[3];
            var max = new double[3];
            for (int k = 0; k < 3; k++)
            {
                if (double.IsNaN(raw[k]) || double.IsInfinity(raw[k]) ||
                    double.IsNaN(raw[k + 3]) || double.IsInfinity(raw[k + 3]))
                    return null;
                min[k] = Math.Min(raw[k], raw[k + 3]);
                max[k] = Math.Max(raw[k], raw[k + 3]);
            }
            return new Cg2Box { Occurrence = occurrence, Min = min, Max = max };
        }

        private static double Cg2BoxGap(Cg2Box a, Cg2Box b)
        {
            double squared = 0;
            for (int k = 0; k < 3; k++)
            {
                double sep = Math.Max(0, Math.Max(a.Min[k] - b.Max[k], b.Min[k] - a.Max[k]));
                squared += sep * sep;
            }
            return Math.Sqrt(squared);
        }

        private static void Cg2AddNeighbor(
            Dictionary<string, List<Tuple<Cg2Box, double>>> neighbors,
            Cg2Box from, Cg2Box to, double gap)
        {
            List<Tuple<Cg2Box, double>> list;
            if (!neighbors.TryGetValue(from.Occurrence, out list))
            {
                list = new List<Tuple<Cg2Box, double>>();
                neighbors.Add(from.Occurrence, list);
            }
            list.Add(Tuple.Create(to, gap));
        }

        private static bool Cg2ValidVector(double[] v)
        {
            return v != null && v.Length >= 3 &&
                !v.Take(3).Any(d => double.IsNaN(d) || double.IsInfinity(d));
        }

        private static bool Cg2AxisCrossesBoxNear(
            double[] center, double[] direction, Cg2Box target, out double minAbsAxialDistance)
        {
            minAbsAxialDistance = double.PositiveInfinity;
            double lengthSq = direction[0]*direction[0] +
                direction[1]*direction[1] + direction[2]*direction[2];
            if (lengthSq < 1e-16) return false;
            double invLen = 1.0 / Math.Sqrt(lengthSq);
            double tmin = -double.MaxValue, tmax = double.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                double d = direction[k] * invLen;
                double lo = target.Min[k] - Cg2AxisBoxPadM;
                double hi = target.Max[k] + Cg2AxisBoxPadM;
                if (Math.Abs(d) < 1e-12)
                {
                    if (center[k] < lo || center[k] > hi) return false;
                    continue;
                }
                double a = (lo - center[k]) / d;
                double b = (hi - center[k]) / d;
                if (a > b) { double swap = a; a = b; b = swap; }
                tmin = Math.Max(tmin, a);
                tmax = Math.Min(tmax, b);
                if (tmin > tmax) return false;
            }
            minAbsAxialDistance = tmin <= 0 && tmax >= 0
                ? 0 : Math.Min(Math.Abs(tmin), Math.Abs(tmax));
            return minAbsAxialDistance <= Cg2MaxAxisReachM;
        }

        private static bool Cg2LikelyKnownCounterpart(HoleAxis a, HoleAxis b)
        {
            if (b == null || !Cg2ValidVector(b.Center) ||
                !Cg2ValidVector(b.Direction)) return false;
            double alenSq = a.Direction.Take(3).Sum(d => d*d);
            double blenSq = b.Direction.Take(3).Sum(d => d*d);
            if (alenSq < 1e-16 || blenSq < 1e-16) return false;
            double dot = 0, projection = 0, distanceSq = 0;
            for (int k = 0; k < 3; k++)
            {
                dot += a.Direction[k] * b.Direction[k];
                double delta = b.Center[k] - a.Center[k];
                projection += delta * a.Direction[k];
                distanceSq += delta*delta;
            }
            double cosine = Math.Min(1.0, Math.Abs(dot) / Math.Sqrt(alenSq * blenSq));
            if (Math.Acos(cosine) * 180.0 / Math.PI > Cg2KnownHoleAngleDeg)
                return false;
            double radialSq = Math.Max(0, distanceSq - projection*projection/alenSq);
            return radialSq <= Cg2KnownHoleRadialM * Cg2KnownHoleRadialM;
        }
    }
}
