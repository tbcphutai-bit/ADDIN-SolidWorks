// CG3.3: READ-ONLY diagnostic evidence about suspected counterpart-hole intent.
// No SOLIDWORKS API calls, changes to model, production NG, marker, CSV or preview.
// Even a regular pitch and observed paired holes elsewhere do NOT prove a requirement.
// C# 7.3 / .NET Framework 4.8 / SOLIDWORKS 2024 SP5.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
        // Review-only geometry thresholds, NOT tolerances for production NG.
        private const double Cg33LineReviewToleranceM = 0.0015; // 1.5 mm
        private const double Cg33PitchReviewToleranceM = 0.001; // 1 mm
        private const double Cg33DuplicatePointM = 0.0005; // 0.5 mm
        private const double Cg33MinPitchM = 0.005; // 5 mm
        private const int Cg33MaxGroupForPattern = 80;

        private sealed class Cg33ObservedPair
        {
            public string Source;
            public string Target;
            public double[] MidpointWorldM;
        }

        private sealed class Cg33RowEvidence
        {
            public readonly HashSet<CheckHoleCg2ExpectedHoleRow> Members =
                new HashSet<CheckHoleCg2ExpectedHoleRow>();
            public double PitchM;
        }

        private static bool Cg33ValidPoint(double[] p)
        {
            return p != null && p.Length >= 3 &&
                !double.IsNaN(p[0]) && !double.IsInfinity(p[0]) &&
                !double.IsNaN(p[1]) && !double.IsInfinity(p[1]) &&
                !double.IsNaN(p[2]) && !double.IsInfinity(p[2]);
        }

        private static double Cg33DistanceM(double[] a, double[] b)
        {
            if (!Cg33ValidPoint(a) || !Cg33ValidPoint(b))
                return double.PositiveInfinity;
            double x = a[0] - b[0], y = a[1] - b[1], z = a[2] - b[2];
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private static double[] Cg33CandidateMiddle(CheckHoleCg2ExpectedHoleRow row)
        {
            if (row == null || !Cg33ValidPoint(row.Cg23EntryWorldM) ||
                !Cg33ValidPoint(row.Cg23ExitWorldM)) return null;
            // Entry/Exit must be physically distinct; do not assume arbitrary
            // output positions represent a real target material slab.
            double length = Cg33DistanceM(row.Cg23EntryWorldM, row.Cg23ExitWorldM);
            if (length < 0.000001 || length > 0.25) return null;
            return new[] {
                (row.Cg23EntryWorldM[0] + row.Cg23ExitWorldM[0]) * 0.5,
                (row.Cg23EntryWorldM[1] + row.Cg23ExitWorldM[1]) * 0.5,
                (row.Cg23EntryWorldM[2] + row.Cg23ExitWorldM[2]) * 0.5
            };
        }

        private static bool Cg33UsableProductionHole(HoleAxis h)
        {
            return h != null && !h.IsSlot && !h.IsSlotEnd &&
                !h.IsCurvedBoundaryFallback && h.Source != HoleSource.RecoveredDeformed &&
                Cg33ValidPoint(h.Center) && Cg33ValidPoint(h.Direction);
        }

        private static bool Cg33CoaxialProductionPair(HoleAxis a, HoleAxis b)
        {
            if (!Cg33UsableProductionHole(a) || !Cg33UsableProductionHole(b)) return false;
            double[] av = a.Direction, bv = b.Direction;
            double a2 = av[0] * av[0] + av[1] * av[1] + av[2] * av[2];
            double b2 = bv[0] * bv[0] + bv[1] * bv[1] + bv[2] * bv[2];
            if (a2 < 1e-15 || b2 < 1e-15) return false;
            double dot = Math.Abs((av[0] * bv[0] + av[1] * bv[1] + av[2] * bv[2]) /
                Math.Sqrt(a2 * b2));
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, dot))) * 180.0 / Math.PI;
            if (angle > AngleToleranceDeg) return false;
            // Use the existing production transverse-axis metric and tolerance
            // ONLY to recognize an already observed OK pair, never to create NG.
            return TransverseCenterDistance(a, b) <= PositionToleranceM;
        }

        private static List<Cg33ObservedPair> Cg33CollectObservedPairs(
            IList<HoleStackResult> stacks)
        {
            var output = new List<Cg33ObservedPair>();
            if (stacks == null) return output;
            foreach (HoleStackResult stack in stacks)
            {
                if (stack == null || stack.IsNg || stack.Stack == null ||
                    stack.Stack.MatchSource != HoleMatchSource.Normal) continue;
                var hs = stack.Stack.Holes.Where(Cg33UsableProductionHole).ToList();
                for (int i = 0; i < hs.Count; ++i)
                for (int j = i + 1; j < hs.Count; ++j)
                {
                    HoleAxis a = hs[i], b = hs[j];
                    if (Cg32EqualOccurrence(a.ComponentOccurrence, b.ComponentOccurrence) ||
                        !Cg33CoaxialProductionPair(a, b)) continue;
                    double[] p = new[] {
                        (a.Center[0] + b.Center[0]) * 0.5,
                        (a.Center[1] + b.Center[1]) * 0.5,
                        (a.Center[2] + b.Center[2]) * 0.5
                    };
                    // Count physical locations, not duplicate face/hole records.
                    if (output.Any(x => Cg33SamePair(x.Source, x.Target,
                            a.ComponentOccurrence, b.ComponentOccurrence) &&
                        Cg33DistanceM(x.MidpointWorldM, p) < Cg33DuplicatePointM)) continue;
                    output.Add(new Cg33ObservedPair {
                        Source = a.ComponentOccurrence, Target = b.ComponentOccurrence,
                        MidpointWorldM = p
                    });
                }
            }
            return output;
        }

        private static bool Cg33SamePair(string a, string b, string x, string y)
        {
            return (Cg32EqualOccurrence(a, x) && Cg32EqualOccurrence(b, y)) ||
                   (Cg32EqualOccurrence(a, y) && Cg32EqualOccurrence(b, x));
        }

        // Examine each candidate line, detect a COLLINEAR AND EQUAL-PITCH subset.
        // We do not infer missing-hole intent from this geometric pattern.
        private static Cg33RowEvidence Cg33FindRegularRow(
            IList<CheckHoleCg2ExpectedHoleRow> rows)
        {
            var best = new Cg33RowEvidence();
            if (rows == null || rows.Count < 3 || rows.Count > Cg33MaxGroupForPattern)
                return best;
            var valid = rows.Select(r => new { Row = r, Point = Cg33CandidateMiddle(r) })
                .Where(v => v.Point != null).ToList();
            if (valid.Count < 3) return best;
            for (int i = 0; i < valid.Count; ++i)
            for (int j = i + 1; j < valid.Count; ++j)
            {
                double[] p = valid[i].Point, q = valid[j].Point;
                double dx = q[0] - p[0], dy = q[1] - p[1], dz = q[2] - p[2];
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d < Cg33MinPitchM) continue;
                double ux = dx / d, uy = dy / d, uz = dz / d;
                var onLine = new List<Tuple<double, CheckHoleCg2ExpectedHoleRow>>();
                foreach (var item in valid)
                {
                    double vx = item.Point[0] - p[0], vy = item.Point[1] - p[1], vz = item.Point[2] - p[2];
                    double t = vx * ux + vy * uy + vz * uz;
                    double ex = vx - t * ux, ey = vy - t * uy, ez = vz - t * uz;
                    if (Math.Sqrt(ex * ex + ey * ey + ez * ez) <= Cg33LineReviewToleranceM)
                        onLine.Add(Tuple.Create(t, item.Row));
                }
                onLine.Sort((a, b) => a.Item1.CompareTo(b.Item1));
                // Discard repeated points, which otherwise manufacture false pitch.
                var unique = new List<Tuple<double, CheckHoleCg2ExpectedHoleRow>>();
                foreach (var item in onLine)
                    if (unique.Count == 0 || item.Item1 - unique[unique.Count - 1].Item1 > Cg33DuplicatePointM)
                        unique.Add(item);
                if (unique.Count < 3) continue;
                // Search the largest consecutive equal-pitch run, not only the
                // entire collinear set (other independent hole rows may intersect).
                for (int start = 0; start < unique.Count - 2; ++start)
                {
                    double pitch = unique[start + 1].Item1 - unique[start].Item1;
                    if (pitch < Cg33MinPitchM) continue;
                    int end = start + 2;
                    for (; end < unique.Count; ++end)
                    {
                        double gap = unique[end].Item1 - unique[end - 1].Item1;
                        if (Math.Abs(gap - pitch) > Math.Max(Cg33PitchReviewToleranceM, pitch * 0.01))
                            break;
                    }
                    int count = end - start;
                    if (count < 3 || count <= best.Members.Count) continue;
                    var candidate = new Cg33RowEvidence { PitchM = pitch };
                    for (int n = start; n < end; ++n) candidate.Members.Add(unique[n].Item2);
                    best = candidate;
                }
            }
            return best;
        }

        private void Cg33AssessIntentEvidence(
            IList<HoleStackResult> stackResults, CheckHoleCg2Report report)
        {
            if (report == null || report.ExpectedHoleCandidates == null) return;
            List<Cg33ObservedPair> observed = Cg33CollectObservedPairs(stackResults);
            var p1 = report.ExpectedHoleCandidates.Where(x => x != null && x.Cg31ReviewPriority == 1).ToList();
            foreach (var group in p1.GroupBy(x => new { x.SourceOccurrence, x.TargetOccurrence }))
            {
                var rows = group.ToList();
                // Only no-existing-NG candidates contribute to an inferred row.
                var noNgRows = rows.Where(x => x.Cg32State == "NO_EXISTING_NG_FOR_PAIR_REVIEW").ToList();
                Cg33RowEvidence pattern = Cg33FindRegularRow(noNgRows);
                var paired = observed.Where(x => Cg33SamePair(x.Source, x.Target,
                    group.Key.SourceOccurrence, group.Key.TargetOccurrence)).ToList();
                foreach (CheckHoleCg2ExpectedHoleRow row in rows)
                {
                    row.Cg33ObservedPairCount = paired.Count;
                    row.Cg33RowCount = pattern.Members.Contains(row) ? pattern.Members.Count : 0;
                    row.Cg33RowPitchMm = row.Cg33RowCount >= 3 ? pattern.PitchM * 1000.0 : -1;
                    row.Cg33NearestPairedHoleMm = -1;
                    row.Cg33Verdict = "REVIEW_ONLY_INTENT_NOT_PROVEN";
                    double[] mid = Cg33CandidateMiddle(row);
                    if (mid == null)
                    {
                        row.Cg33State = "UNVERIFIED_GEOMETRY";
                        row.Cg33Reason = "No usable CG2.3 entry/exit midpoint.";
                        continue;
                    }
                    if (paired.Count > 0)
                        row.Cg33NearestPairedHoleMm = paired.Min(x => Cg33DistanceM(x.MidpointWorldM, mid)) * 1000.0;
                    if (row.Cg32State != "NO_EXISTING_NG_FOR_PAIR_REVIEW")
                    {
                        row.Cg33State = "EXISTING_NG_RELATION_REVIEW";
                        row.Cg33Reason = "CG3.2 has another review state: " + row.Cg32State +
                            ". Do not treat this as a new missing hole.";
                    }
                    else if (noNgRows.Count > Cg33MaxGroupForPattern)
                    {
                        row.Cg33State = "UNVERIFIED_ROW_SCAN_LIMIT_REVIEW";
                        row.Cg33Reason = "Too many P1 candidates in this exact occurrence pair for bounded " +
                            "row detection. Retained observed-pair count but no pitch inference.";
                    }
                    else if (paired.Count >= 2)
                    {
                        row.Cg33State = "OBSERVED_PAIRED_HOLES_ELSEWHERE_REVIEW";
                        row.Cg33Reason = "At least two existing co-axial source/target hole pairs " +
                            "occur in production OK stacks. This suggests a joint convention, " +
                            "but cannot establish hole intent at this candidate.";
                    }
                    else if (paired.Count == 1)
                    {
                        row.Cg33State = "SINGLE_PAIRED_HOLE_REFERENCE_REVIEW";
                        row.Cg33Reason = "Only one observed source/target hole pair; design requirement is unknown.";
                    }
                    else if (row.Cg33RowCount >= 3)
                    {
                        row.Cg33State = "REGULAR_SOURCE_ROW_NO_PAIRED_REFERENCE_REVIEW";
                        row.Cg33Reason = "Regular geometric candidate spacing but zero observed source/target " +
                            "paired-hole references in OK stacks. NOT proof of intended holes.";
                    }
                    else
                    {
                        row.Cg33State = "NO_INDEPENDENT_HOLE_INTENT_EVIDENCE_REVIEW";
                        row.Cg33Reason = "No current-run paired-hole reference or verified design rule.";
                    }
                }
            }
            foreach (var row in report.ExpectedHoleCandidates.Where(x => x != null && x.Cg31ReviewPriority != 1))
            {
                row.Cg33State = "NOT_P1_REVIEW_UNCHANGED";
                row.Cg33Reason = "Not assessed in CG3.3; existing CG3.1 evidence remains unchanged.";
                row.Cg33Verdict = "REVIEW_ONLY_INTENT_NOT_PROVEN";
                row.Cg33ObservedPairCount = 0;
                row.Cg33RowCount = 0;
                row.Cg33RowPitchMm = -1;
                row.Cg33NearestPairedHoleMm = -1;
            }
            Cg33WriteDebugReport(report);
            Cg33RunPureTestsOnce();
        }

        [Conditional("DEBUG")]
        private static void Cg33WriteDebugReport(CheckHoleCg2Report report)
        {
            var p1 = report.ExpectedHoleCandidates.Where(x => x != null && x.Cg31ReviewPriority == 1).ToList();
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG33][SUMMARY] P1={0} RowMembers={1} WithPairReferences={2} WithoutPairReferences={3} MissingHoleNgCreated=0 ProductionNgChanged=NO",
                p1.Count, p1.Count(x => x.Cg33RowCount >= 3),
                p1.Count(x => x.Cg33ObservedPairCount > 0), p1.Count(x => x.Cg33ObservedPairCount == 0)));
            foreach (var pair in p1.GroupBy(x => new { x.SourceOccurrence, x.TargetOccurrence })
                .OrderByDescending(x => x.Count()).ThenBy(x => x.Key.SourceOccurrence))
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG33][PAIR] Source={0} Target={1} P1={2} ReferencePairsMax={3} RegularRowMax={4}",
                    pair.Key.SourceOccurrence, pair.Key.TargetOccurrence, pair.Count(),
                    pair.Max(x => x.Cg33ObservedPairCount), pair.Max(x => x.Cg33RowCount)));
            foreach (var row in p1)
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG33][ROW] Id={0} Source={1} Target={2} State={3} ReferencePairs={4} NearestPairedMm={5:F3} RowSize={6} PitchMm={7:F3} Intent=UNVERIFIED Verdict=REVIEW_ONLY",
                    row.Id, row.SourceOccurrence, row.TargetOccurrence, row.Cg33State,
                    row.Cg33ObservedPairCount, row.Cg33NearestPairedHoleMm,
                    row.Cg33RowCount, row.Cg33RowPitchMm));
        }

#if DEBUG
        private static bool cg33TestsDone;
        private static void Cg33RunPureTestsOnce()
        {
            if (cg33TestsDone) return;
            cg33TestsDone = true;
            int passed = 0, total = 0;
            var rows = new List<CheckHoleCg2ExpectedHoleRow>();
            double[] yy = { 0, 0.25, 0.5, 0.75 };
            for (int i = 0; i < yy.Length; ++i)
                rows.Add(Cg33FakeRow(i + 1, 0.01, yy[i]));
            Cg33Check("FOUR_REGULAR_POINTS", Cg33FindRegularRow(rows).Members.Count == 4,
                ref passed, ref total);
            Cg33Check("PITCH_250MM", Math.Abs(Cg33FindRegularRow(rows).PitchM - 0.25) < 0.0001,
                ref passed, ref total);
            rows.Add(Cg33FakeRow(5, 0.03, 0.37));
            Cg33Check("IGNORE_OFF_LINE", Cg33FindRegularRow(rows).Members.Count == 4,
                ref passed, ref total);
            rows = new List<CheckHoleCg2ExpectedHoleRow> {
                Cg33FakeRow(1, 0.01, 0), Cg33FakeRow(2, 0.01, 0.25),
                Cg33FakeRow(3, 0.01, 0.6)
            };
            Cg33Check("UNEVEN_SPACING", Cg33FindRegularRow(rows).Members.Count == 0,
                ref passed, ref total);
            rows[2].Cg23ExitWorldM = rows[2].Cg23EntryWorldM;
            Cg33Check("INVALID_ENTRY_EXIT_EXCLUDED", Cg33FindRegularRow(rows).Members.Count == 0,
                ref passed, ref total);
            Cg33Check("PAIR_ORDER", Cg33SamePair("a", "b", "B", "A"), ref passed, ref total);
            Cg33Check("UNRELATED_PAIR", !Cg33SamePair("a", "b", "a", "c"), ref passed, ref total);
            Cg33Check("EMPTY_ROW", Cg33FindRegularRow(
                new List<CheckHoleCg2ExpectedHoleRow>()).Members.Count == 0,
                ref passed, ref total);
            var bad = Cg33FakeRow(99, 0.01, 0.3);
            bad.Cg23EntryWorldM[1] = double.NaN;
            Cg33Check("INVALID_POINT", Cg33CandidateMiddle(bad) == null,
                ref passed, ref total);
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG33][TEST_SUMMARY] Passed={0}/{1}", passed, total));
        }

        private static CheckHoleCg2ExpectedHoleRow Cg33FakeRow(int id, double x, double y)
        {
            return new CheckHoleCg2ExpectedHoleRow {
                Id = id, Cg23EntryWorldM = new[] { x, y, 0.0 },
                Cg23ExitWorldM = new[] { x, y, 0.002 }
            };
        }

        private static void Cg33Check(string name, bool ok, ref int passed, ref int total)
        {
            total++; if (ok) passed++;
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG33][TEST] {0}={1}", name, ok ? "PASS" : "FAIL"));
        }
#else
        private static void Cg33RunPureTestsOnce() { }
#endif
    }
}
