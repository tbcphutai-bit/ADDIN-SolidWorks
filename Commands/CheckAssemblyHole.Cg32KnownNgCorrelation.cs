// CG3.2 - Read-only correlation of P1 candidates with EXISTING Stack/Pattern NG.
// C# 7.3 / .NET Framework 4.8 / SOLIDWORKS 2024 SP5.
// This class NEVER promotes candidates into Missing Hole NG, never changes existing NG,
// model, marker, CSV, CG25 FIX2 OpenGL preview, or any SOLIDWORKS COM state.
// Pair name alone is NOT evidence that two errors are identical.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
        // Diagnostic association radius ONLY. NOT a machining, positional, NG,
        // or production geometry tolerance. Use only to suggest a HUMAN review link.
        private const double Cg32ReviewRadialWindowM = 0.008; // 8 mm
        private const double Cg32ReviewAxialWindowM = 0.075; // 75 mm, diagnostic reach only

        private sealed class Cg32NgAnchor
        {
            public string First;
            public string Second;
            public string Kind;          // PATTERN or STACK
            public string Label;
            public double[] PositionWorldM;
            public bool IsMeasured;       // actual center, not expected-only center
        }

        private sealed class Cg32MatchResult
        {
            public string State;
            public string Kind;
            public string Label;
            public double DistanceMm = -1;
            public string Reason;
        }

        private static bool Cg32HasPoint(double[] value)
        {
            return value != null && value.Length >= 3 &&
                !double.IsNaN(value[0]) && !double.IsInfinity(value[0]) &&
                !double.IsNaN(value[1]) && !double.IsInfinity(value[1]) &&
                !double.IsNaN(value[2]) && !double.IsInfinity(value[2]);
        }

        private static bool Cg32EqualOccurrence(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) &&
                !string.IsNullOrWhiteSpace(second) &&
                string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Cg32SamePair(string source, string target, Cg32NgAnchor anchor)
        {
            return anchor != null &&
                ((Cg32EqualOccurrence(source, anchor.First) &&
                  Cg32EqualOccurrence(target, anchor.Second)) ||
                 (Cg32EqualOccurrence(source, anchor.Second) &&
                  Cg32EqualOccurrence(target, anchor.First)));
        }

        private static double Cg32RadialDistanceToRayM(
            double[] entry, double[] exit, double[] point)
        {
            if (!Cg32HasPoint(entry) || !Cg32HasPoint(exit) || !Cg32HasPoint(point))
                return double.PositiveInfinity;
            double dx = exit[0] - entry[0];
            double dy = exit[1] - entry[1];
            double dz = exit[2] - entry[2];
            double lengthSq = dx * dx + dy * dy + dz * dz;
            if (lengthSq <= 1e-14 || lengthSq > 0.25 * 0.25)
                return double.PositiveInfinity;
            double vx = point[0] - entry[0];
            double vy = point[1] - entry[1];
            double vz = point[2] - entry[2];
            double along = (vx * dx + vy * dy + vz * dz) / lengthSq;
            // Reject references far away ALONG the same infinite line. Merely
            // sharing a line elsewhere in the assembly is not an NG association.
            if (Math.Abs((along - 0.5) * Math.Sqrt(lengthSq)) > Cg32ReviewAxialWindowM)
                return double.PositiveInfinity;
            double rx = vx - along * dx;
            double ry = vy - along * dy;
            double rz = vz - along * dz;
            return Math.Sqrt(Math.Max(0, rx * rx + ry * ry + rz * rz));
        }

        private static void Cg32AddAnchor(List<Cg32NgAnchor> dest,
            string first, string second, string kind, string label,
            double[] point, bool measured)
        {
            if (!Cg32HasPoint(point)) return;
            dest.Add(new Cg32NgAnchor
            {
                First = first,
                Second = second,
                Kind = kind,
                Label = label,
                PositionWorldM = new[] { point[0], point[1], point[2] },
                IsMeasured = measured
            });
        }

        // Only consumes production calculations ALREADY completed in this run.
        // No new SOLIDWORKS API calls and no edits to HoleStackResult/PatternIssue.
        private static List<Cg32NgAnchor> Cg32CaptureKnownNgAnchors(
            List<HoleStackResult> stackResults, List<PatternIssue> patternIssues)
        {
            var anchors = new List<Cg32NgAnchor>();
            if (patternIssues != null)
            {
                int issueNo = 0;
                foreach (PatternIssue issue in patternIssues)
                {
                    ++issueNo;
                    if (issue == null) continue;
                    string label = "Pattern#" + issueNo.ToString(CultureInfo.InvariantCulture) +
                        ":" + (issue.Kind ?? "UNSPECIFIED");
                    // ActualCenter has strongest evidence that this is the affected hole row.
                    // Never use ExpectedCenter alone as proof of an existing problem at this spot.
                    Cg32AddAnchor(anchors, issue.FirstComponent, issue.SecondComponent,
                        "PATTERN", label + ":ACTUAL_CENTER", issue.ActualCenter, true);
                    if (issue.ActualHole != null)
                        Cg32AddAnchor(anchors, issue.FirstComponent, issue.SecondComponent,
                            "PATTERN", label + ":ACTUAL_HOLE", issue.ActualHole.Center, true);
                    // Retain the expected position only as a weaker, review-only association.
                    Cg32AddAnchor(anchors, issue.FirstComponent, issue.SecondComponent,
                        "PATTERN", label + ":EXPECTED_ONLY", issue.ExpectedCenter, false);
                }
            }
            if (stackResults != null)
            {
                int stackNo = 0;
                foreach (HoleStackResult stack in stackResults)
                {
                    ++stackNo;
                    if (stack == null || !stack.IsNg || stack.Reference == null) continue;
                    foreach (HoleAxis outlier in stack.Outliers)
                    {
                        if (outlier == null) continue;
                        string label = "Stack#" + stackNo.ToString(CultureInfo.InvariantCulture);
                        Cg32AddAnchor(anchors, stack.Reference.ComponentOccurrence,
                            outlier.ComponentOccurrence, "STACK", label + ":OUTLIER",
                            outlier.Center, true);
                        Cg32AddAnchor(anchors, stack.Reference.ComponentOccurrence,
                            outlier.ComponentOccurrence, "STACK", label + ":REFERENCE",
                            stack.Reference.Center, true);
                    }
                }
            }
            return anchors;
        }

        private static Cg32MatchResult Cg32MatchP1(string source, string target,
            double[] entry, double[] exit, IList<Cg32NgAnchor> anchors)
        {
            Cg32MatchResult output = new Cg32MatchResult
            {
                State = "NO_EXISTING_NG_FOR_PAIR_REVIEW",
                Kind = "NONE",
                Label = "",
                Reason = "No current production Stack/Pattern NG pair match. Hole requirement is still UNKNOWN."
            };
            bool validLine = Cg32HasPoint(entry) && Cg32HasPoint(exit) &&
                Cg32RadialDistanceToRayM(entry, exit, entry) < double.PositiveInfinity;
            bool anyPair = false;
            double best = double.PositiveInfinity;
            Cg32NgAnchor bestAnchor = null;
            if (anchors != null)
            {
                foreach (Cg32NgAnchor anchor in anchors)
                {
                    if (!Cg32SamePair(source, target, anchor)) continue;
                    anyPair = true;
                    if (!validLine) continue;
                    double dist = Cg32RadialDistanceToRayM(entry, exit, anchor.PositionWorldM);
                    if (dist > Cg32ReviewRadialWindowM) continue;
                    // Actual/measured geometries outrank inferred expected point;
                    // Pattern evidence outranks Stack, but only after spatial matching.
                    double score = dist + (!anchor.IsMeasured ? 0.020 : 0) +
                        (anchor.Kind == "STACK" ? 0.001 : 0);
                    if (score < best)
                    {
                        best = score;
                        bestAnchor = anchor;
                        output.DistanceMm = dist * 1000.0;
                    }
                }
            }
            if (!validLine)
            {
                output.State = "UNVERIFIED_ENTRY_EXIT_REVIEW";
                output.Reason = "Invalid entry/exit geometry; no spatial NG correlation possible.";
                return output;
            }
            if (bestAnchor != null)
            {
                output.Kind = bestAnchor.Kind;
                output.Label = bestAnchor.Label;
                output.State = !bestAnchor.IsMeasured
                    ? "EXPECTED_POSITION_ONLY_REVIEW"
                    : bestAnchor.Kind == "PATTERN"
                        ? "POSSIBLY_LINKED_PATTERN_NG_REVIEW"
                        : "POSSIBLY_LINKED_STACK_NG_REVIEW";
                output.Reason = "Same component pair and spatial proximity to an existing NG " +
                    "reference. POSSIBLE duplication, NOT proven identical; never suppress or auto-create NG.";
                return output;
            }
            if (anyPair)
            {
                output.State = "SAME_PAIR_NG_NO_SPATIAL_LINK_REVIEW";
                output.Reason = "An existing NG uses the same two components, but no " +
                    "measured reference near this candidate axis. Do NOT merge by pair name alone.";
            }
            return output;
        }

        private void Cg32CorrelateExistingNg(
            List<HoleStackResult> stackResults, List<PatternIssue> patternIssues,
            CheckHoleCg2Report report)
        {
            if (report == null || report.ExpectedHoleCandidates == null) return;
            List<Cg32NgAnchor> anchors = Cg32CaptureKnownNgAnchors(stackResults, patternIssues);
            foreach (CheckHoleCg2ExpectedHoleRow row in report.ExpectedHoleCandidates)
            {
                if (row == null) continue;
                row.Cg32Verdict = "REVIEW_ONLY_MISSING_HOLE_REQUIREMENT_UNVERIFIED";
                row.Cg32LinkedNg = "NONE";
                row.Cg32LinkedNgLabel = "";
                row.Cg32NearestNgRadialMm = -1;
                if (row.Cg31ReviewPriority != 1)
                {
                    row.Cg32State = "NOT_P1_CG31_REVIEW_UNCHANGED";
                    row.Cg32Reason = "No CG3.2 dedup assessment for non-P1 candidates.";
                    continue;
                }
                try
                {
                    Cg32MatchResult m = Cg32MatchP1(row.SourceOccurrence,
                        row.TargetOccurrence, row.Cg23EntryWorldM,
                        row.Cg23ExitWorldM, anchors);
                    row.Cg32State = m.State;
                    row.Cg32LinkedNg = m.Kind;
                    row.Cg32LinkedNgLabel = m.Label;
                    row.Cg32NearestNgRadialMm = m.DistanceMm;
                    row.Cg32Reason = m.Reason;
                }
                catch (Exception ex)
                {
                    row.Cg32State = "UNVERIFIED_CORRELATION_EXCEPTION";
                    row.Cg32Reason = "Diagnostic failed closed: " + ex.GetType().Name;
                }
            }
            Cg32WriteDebugReport(report);
            Cg32RunPureTestsOnce();
        }

        [Conditional("DEBUG")]
        private static void Cg32WriteDebugReport(CheckHoleCg2Report report)
        {
            List<CheckHoleCg2ExpectedHoleRow> rows = report.ExpectedHoleCandidates
                .Where(x => x != null).ToList();
            var p1 = rows.Where(x => x.Cg31ReviewPriority == 1).ToList();
            int linkedPattern = p1.Count(x => x.Cg32State == "POSSIBLY_LINKED_PATTERN_NG_REVIEW");
            int linkedStack = p1.Count(x => x.Cg32State == "POSSIBLY_LINKED_STACK_NG_REVIEW");
            int expectedOnly = p1.Count(x => x.Cg32State == "EXPECTED_POSITION_ONLY_REVIEW");
            int pairOnly = p1.Count(x => x.Cg32State == "SAME_PAIR_NG_NO_SPATIAL_LINK_REVIEW");
            int independent = p1.Count(x => x.Cg32State == "NO_EXISTING_NG_FOR_PAIR_REVIEW");
            int other = p1.Count - linkedPattern - linkedStack - expectedOnly - pairOnly - independent;
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG32][SUMMARY] P1={0} LinkedPattern={1} LinkedStack={2} ExpectedOnly={3} PairOnly={4} NoPairNg={5} Unverified={6} MissingHoleNgCreated=0 ProductionNgChanged=NO",
                p1.Count, linkedPattern, linkedStack, expectedOnly, pairOnly, independent, other));
            foreach (var g in p1.GroupBy(x => x.Cg32State ?? "NULL").OrderBy(x => x.Key))
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG32][STATE] {0}={1}", g.Key, g.Count()));
            foreach (var g in p1.GroupBy(x => new { x.SourceOccurrence, x.TargetOccurrence })
                .OrderByDescending(x => x.Count()).ThenBy(x => x.Key.SourceOccurrence))
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG32][PAIR] Source={0} Target={1} P1={2} CandidateIds={3}",
                    g.Key.SourceOccurrence, g.Key.TargetOccurrence, g.Count(),
                    string.Join(",", g.Select(x => x.Id.ToString(CultureInfo.InvariantCulture)).ToArray())));
            foreach (CheckHoleCg2ExpectedHoleRow row in p1)
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG32][ROW] Id={0} Source={1} Target={2} Hole={3} State={4} LinkedNg={5} Label={6} RadialMm={7:F3} HoleIntent=UNVERIFIED Verdict=REVIEW_ONLY",
                    row.Id, row.SourceOccurrence, row.TargetOccurrence,
                    row.SourceHoleLabel, row.Cg32State, row.Cg32LinkedNg,
                    row.Cg32LinkedNgLabel, row.Cg32NearestNgRadialMm));
        }

#if DEBUG
        private static bool cg32TestsDone;
        private static void Cg32RunPureTestsOnce()
        {
            if (cg32TestsDone) return;
            cg32TestsDone = true;
            int passed = 0, total = 0;
            double[] entry = { 0, 0, 0 };
            double[] exit = { 0, 0, 0.002 };
            var pattern = new Cg32NgAnchor { First = "A", Second = "B", Kind = "PATTERN",
                IsMeasured = true, Label = "P1", PositionWorldM = new[] { 0.004, 0.0, 0.003 } };
            var stack = new Cg32NgAnchor { First = "B", Second = "A", Kind = "STACK",
                IsMeasured = true, Label = "S1", PositionWorldM = new[] { 0.005, 0.0, 0.003 } };
            Cg32Test("PATTERN_GEOMETRIC", "A", "B", entry, exit,
                new List<Cg32NgAnchor> { pattern }, "POSSIBLY_LINKED_PATTERN_NG_REVIEW", ref passed, ref total);
            Cg32Test("REVERSED_PAIR", "B", "A", entry, exit,
                new List<Cg32NgAnchor> { pattern }, "POSSIBLY_LINKED_PATTERN_NG_REVIEW", ref passed, ref total);
            Cg32Test("STACK_GEOMETRIC", "A", "B", entry, exit,
                new List<Cg32NgAnchor> { stack }, "POSSIBLY_LINKED_STACK_NG_REVIEW", ref passed, ref total);
            Cg32Test("PATTERN_PREFERRED", "A", "B", entry, exit,
                new List<Cg32NgAnchor> { pattern, stack }, "POSSIBLY_LINKED_PATTERN_NG_REVIEW", ref passed, ref total);
            Cg32Test("UNRELATED_PAIR", "A", "C", entry, exit,
                new List<Cg32NgAnchor> { pattern }, "NO_EXISTING_NG_FOR_PAIR_REVIEW", ref passed, ref total);
            Cg32Test("SAME_PAIR_FAR", "A", "B", entry, exit,
                new List<Cg32NgAnchor> { new Cg32NgAnchor { First = "A", Second = "B",
                    Kind = "PATTERN", IsMeasured = true, PositionWorldM = new[] { 0.030, 0.0, 0.0 } } },
                "SAME_PAIR_NG_NO_SPATIAL_LINK_REVIEW", ref passed, ref total);
            Cg32Test("SAME_PAIR_AXIALLY_FAR", "A", "B", entry, exit,
                new List<Cg32NgAnchor> { new Cg32NgAnchor { First = "A", Second = "B",
                    Kind = "PATTERN", IsMeasured = true,
                    PositionWorldM = new[] { 0.004, 0.0, 0.200 } } },
                "SAME_PAIR_NG_NO_SPATIAL_LINK_REVIEW", ref passed, ref total);
            Cg32Test("INVALID_ENTRY_EXIT", "A", "B", entry, entry,
                new List<Cg32NgAnchor> { pattern }, "UNVERIFIED_ENTRY_EXIT_REVIEW", ref passed, ref total);
            Cg32Test("EXPECTED_POSITION_ONLY", "A", "B", entry, exit,
                new List<Cg32NgAnchor> { new Cg32NgAnchor { First = "A", Second = "B",
                    Kind = "PATTERN", IsMeasured = false,
                    PositionWorldM = new[] { 0.002, 0.0, 0.003 } } },
                "EXPECTED_POSITION_ONLY_REVIEW", ref passed, ref total);
            Cg32Test("NO_ANCHOR", "A", "B", entry, exit,
                new List<Cg32NgAnchor>(), "NO_EXISTING_NG_FOR_PAIR_REVIEW", ref passed, ref total);
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG32][TEST_SUMMARY] Passed={0}/{1}", passed, total));
        }

        private static void Cg32Test(string name, string a, string b, double[] entry,
            double[] exit, IList<Cg32NgAnchor> known, string expected,
            ref int passed, ref int total)
        {
            ++total;
            Cg32MatchResult result = Cg32MatchP1(a, b, entry, exit, known);
            bool good = result.State == expected;
            if (good) ++passed;
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG32][TEST] {0}={1} State={2}", name, good ? "PASS" : "FAIL", result.State));
        }
#else
        private static void Cg32RunPureTestsOnce() { }
#endif
    }
}
