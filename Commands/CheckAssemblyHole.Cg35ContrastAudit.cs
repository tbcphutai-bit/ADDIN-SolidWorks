// CG3.5: DEBUG-only, read-only contrast audit. NOT a production Missing Hole detector.
// Six user-confirmed positives are reused from CG3.4. No one-sided valid-hole negatives
// have yet been manually verified, so one-sided false-positive rate is UNMEASURABLE.
// Existing production OK coaxial hole pairs are GEOMETRIC controls, not one-sided labels.
// No model edits, NG changes, CSV, markers, UI, OpenGL or new COM geometry calls.
// SOLIDWORKS 2024 SP5 / C# 7.3 / .NET Framework 4.8.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
#if DEBUG
        // DEBUG matching tolerance only. It is NOT an NG/production tolerance.
        private const double Cg35OkPairNearbyM = 0.005; // 5 mm, conservative 3D screening
        private const int Cg35MaxDebugRows = 12;

        private sealed class Cg35ManualOneSidedNegative
        {
            public string AssemblyTitleHint;
            public string SourceOccurrence;
            public string TargetOccurrence;
            public double[] WorldMidpointM;
            public string UserLabel;
        }

        // IMPORTANT: NO valid one-sided holes have been manually confirmed yet.
        // Do not invent negative labels from 'unverified', P2 or Pattern NG rows.
        // Only a future independently verified, assembly-specific negative fixture
        // may populate this array. Anti must NOT guess or populate it.
        private static readonly Cg35ManualOneSidedNegative[] Cg35VerifiedOneSidedNegatives =
            new Cg35ManualOneSidedNegative[0];

        private enum Cg35ScreenResult
        {
            NoCandidate,
            MatchedPositiveP1Review,
            PositiveReviewIncomplete,
            NegativeOverlapsP1Review,
            NegativeNotP1,
            Ambiguous,
            InvalidLabel
        }

        private static bool Cg35ExactPair(CheckHoleCg2ExpectedHoleRow row,
            string source, string target)
        {
            return row != null && Cg32EqualOccurrence(row.SourceOccurrence, source) &&
                Cg32EqualOccurrence(row.TargetOccurrence, target);
        }

        // This is a comparison against existing REVIEW data, not a decision about
        // whether a real counterpart hole is required in a new Assembly.
        private static Cg35ScreenResult Cg35CheckLabeledPosition(
            IList<CheckHoleCg2ExpectedHoleRow> rows, string source, string target,
            double[] midpointM, bool isConfirmedMissing, out int numberMatched)
        {
            numberMatched = 0;
            if (rows == null || string.IsNullOrWhiteSpace(source) ||
                string.IsNullOrWhiteSpace(target) || !Cg33ValidPoint(midpointM))
                return Cg35ScreenResult.InvalidLabel;
            CheckHoleCg2ExpectedHoleRow selected = null;
            foreach (CheckHoleCg2ExpectedHoleRow row in rows)
            {
                if (!Cg35ExactPair(row, source, target)) continue;
                if (Cg33DistanceM(Cg33CandidateMiddle(row), midpointM) >
                    Cg34FixtureMatchToleranceM) continue;
                ++numberMatched;
                if (selected == null) selected = row;
            }
            if (numberMatched > 1) return Cg35ScreenResult.Ambiguous;
            if (numberMatched == 0) return Cg35ScreenResult.NoCandidate;
            bool p1Eligible = Cg34CandidateIsReviewEligible(selected);
            if (isConfirmedMissing)
                return p1Eligible ? Cg35ScreenResult.MatchedPositiveP1Review :
                    Cg35ScreenResult.PositiveReviewIncomplete;
            return p1Eligible ? Cg35ScreenResult.NegativeOverlapsP1Review :
                Cg35ScreenResult.NegativeNotP1;
        }

        private static bool Cg35HasP1CollisionAtObservedOK(
            Cg33ObservedPair okPair, IEnumerable<CheckHoleCg2ExpectedHoleRow> p1Rows)
        {
            if (okPair == null || p1Rows == null ||
                !Cg33ValidPoint(okPair.MidpointWorldM)) return false;
            return p1Rows.Any(r => r != null &&
                Cg33SamePair(r.SourceOccurrence, r.TargetOccurrence,
                    okPair.Source, okPair.Target) &&
                Cg33DistanceM(Cg33CandidateMiddle(r), okPair.MidpointWorldM) <=
                    Cg35OkPairNearbyM);
        }

        // Audit uses this run's ALREADY detected hole/stack/CG3.x evidence.
        // No new geometry queries or modifications to production decisions.
        private static void Cg35ContrastAudit(ModelDoc2 model,
            IList<HoleStackResult> stackResults, CheckHoleCg2Report report)
        {
            try
            {
                Cg35RunPureTestsOnce();
                string title = model == null ? null : model.GetTitle();
                if (string.IsNullOrWhiteSpace(title) ||
                    title.IndexOf(Cg34FixtureAssemblyHint, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Debug.WriteLine("[CG35][SKIP] Not the known positive benchmark assembly; no labels applied.");
                    return;
                }
                if (report == null || report.ExpectedHoleCandidates == null)
                {
                    Debug.WriteLine("[CG35][SKIP] Missing CG2/CG3 diagnostic report.");
                    return;
                }

                var all = report.ExpectedHoleCandidates.Where(x => x != null).ToList();
                var p1 = all.Where(x => x.Cg31ReviewPriority == 1).ToList();
                int positiveMatched = 0, positiveMissed = 0, positiveIncomplete = 0,
                    positiveAmbiguous = 0;
                var labeledP1 = new HashSet<CheckHoleCg2ExpectedHoleRow>();
                foreach (var known in Cg34KnownSix)
                {
                    int found;
                    Cg35ScreenResult state = Cg35CheckLabeledPosition(all,
                        Cg34SourceOccurrence, Cg34TargetOccurrence,
                        known.WorldMidpointM, true, out found);
                    if (state == Cg35ScreenResult.MatchedPositiveP1Review) ++positiveMatched;
                    else if (state == Cg35ScreenResult.NoCandidate) ++positiveMissed;
                    else if (state == Cg35ScreenResult.Ambiguous) ++positiveAmbiguous;
                    else ++positiveIncomplete;
                    foreach (var row in p1.Where(r => Cg34IsFixturePair(r) &&
                        Cg33DistanceM(Cg33CandidateMiddle(r), known.WorldMidpointM) <=
                        Cg34FixtureMatchToleranceM)) labeledP1.Add(row);
                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "[CG35][POSITIVE] Label={0} Matches={1} State={2}",
                        known.ReferenceNo, found, state));
                }

                // Valid one-sided cases MUST come from the human's independently
                // verified design intent. Currently zero; FPR cannot be computed.
                int negativeLabels = 0, negativeP1Overlap = 0, negativeNotP1 = 0,
                    negativeAmbiguous = 0, negativeInvalid = 0;
                foreach (Cg35ManualOneSidedNegative label in Cg35VerifiedOneSidedNegatives)
                {
                    if (label == null || string.IsNullOrWhiteSpace(label.AssemblyTitleHint) ||
                        title.IndexOf(label.AssemblyTitleHint, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    ++negativeLabels;
                    int count;
                    Cg35ScreenResult outcome = Cg35CheckLabeledPosition(all,
                        label.SourceOccurrence, label.TargetOccurrence,
                        label.WorldMidpointM, false, out count);
                    if (outcome == Cg35ScreenResult.NegativeOverlapsP1Review) ++negativeP1Overlap;
                    else if (outcome == Cg35ScreenResult.NoCandidate ||
                        outcome == Cg35ScreenResult.NegativeNotP1) ++negativeNotP1;
                    else if (outcome == Cg35ScreenResult.Ambiguous) ++negativeAmbiguous;
                    else ++negativeInvalid;
                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "[CG35][ONE_SIDED_NEGATIVE] Label={0} Matches={1} State={2}",
                        label.UserLabel ?? "UNNAMED", count, outcome));
                }

                // Geometric controls are REAL OK counterpart-hole pairs from this
                // run, NOT examples of valid one-sided machining (different class).
                List<Cg33ObservedPair> okPairs = Cg33CollectObservedPairs(stackResults);
                int structuralP1Conflicts = 0, printedConflicts = 0;
                foreach (Cg33ObservedPair okPair in okPairs)
                {
                    bool collision = Cg35HasP1CollisionAtObservedOK(okPair, p1);
                    if (!collision) continue;
                    ++structuralP1Conflicts;
                    if (++printedConflicts <= Cg35MaxDebugRows)
                        Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "[CG35][STRUCTURAL_CONFLICT] Source={0} Target={1} PointMm=({2:F3},{3:F3},{4:F3}) ReviewRequired=YES",
                            okPair.Source, okPair.Target,
                            1000 * okPair.MidpointWorldM[0],
                            1000 * okPair.MidpointWorldM[1],
                            1000 * okPair.MidpointWorldM[2]));
                }

                // Unlabeled P1 != false positive. Surface remaining instances
                // for the user's future independent design-intent verification.
                var unlabeled = p1.Where(r => !labeledP1.Contains(r)).ToList();
                foreach (var row in unlabeled.Take(Cg35MaxDebugRows))
                {
                    double[] mid = Cg33CandidateMiddle(row);
                    string point = mid == null ? "UNKNOWN" : string.Format(CultureInfo.InvariantCulture,
                        "({0:F3},{1:F3},{2:F3})", mid[0] * 1000.0,
                        mid[1] * 1000.0, mid[2] * 1000.0);
                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "[CG35][UNLABELED_P1] Source={0} Target={1} MidMm={2} Cg32={3} Action=HUMAN_REVIEW",
                        row.SourceOccurrence, row.TargetOccurrence, point, row.Cg32State));
                }
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG35][SUMMARY] PositiveLabels={0} PositiveP1={1} Missed={2} Incomplete={3} Ambiguous={4} ManualOneSidedNegativeLabels={5} NegativeP1Overlap={6} NegativeNotP1={7} NegativeAmbiguous={8} NegativeInvalid={9} StructuralOKPairs={10} StructuralP1Conflicts={11} UnlabeledP1={12} ExistingPatternLinkedP1={13} MissingHoleNgCreated=0 ProductionNgChanged=NO",
                    Cg34KnownSix.Length, positiveMatched, positiveMissed,
                    positiveIncomplete, positiveAmbiguous, negativeLabels,
                    negativeP1Overlap, negativeNotP1, negativeAmbiguous,
                    negativeInvalid, okPairs.Count, structuralP1Conflicts,
                    unlabeled.Count, p1.Count(r =>
                        string.Equals(r.Cg32State, "POSSIBLY_LINKED_PATTERN_NG_REVIEW", StringComparison.Ordinal))));
                Debug.WriteLine("[CG35][LIMITATION] OneSidedFalsePositiveRate=" +
                    (negativeLabels == 0 ? "NOT_MEASURABLE_NO_VERIFIED_NEGATIVES" :
                     "SEE_NEGATIVE_P1_OVERLAP_REVIEW_ONLY") +
                    " AutoMissingHoleNG=DISABLED GeometricOKPairsAreNotOneSidedNegatives=YES");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CG35][ERROR] Optional contrast audit skipped: " +
                    ex.GetType().Name + " " + ex.Message);
            }
        }

        private static bool cg35PureTestsCompleted;
        private static void Cg35RunPureTestsOnce()
        {
            if (cg35PureTestsCompleted) return;
            cg35PureTestsCompleted = true;
            int passed = 0, total = 0;
            Action<string, bool> check = (name, ok) => {
                ++total;
                if (ok) ++passed;
                Debug.WriteLine("[CG35][TEST] " + name + "=" + (ok ? "PASS" : "FAIL"));
            };
            string a = "fixture-A", b = "fixture-B";
            double[] center = { 1.0, 2.0, 3.0 };
            var row = new CheckHoleCg2ExpectedHoleRow {
                SourceOccurrence = a, TargetOccurrence = b,
                Cg23EntryWorldM = new[] { 1.0, 2.0, 2.999 },
                Cg23ExitWorldM = new[] { 1.0, 2.0, 3.001 },
                Cg31ReviewPriority = 1,
                Cg32State = "NO_EXISTING_NG_FOR_PAIR_REVIEW",
                Cg33Verdict = "REVIEW_ONLY_INTENT_NOT_PROVEN",
                MaterialEvidence = "BREP_SOLID_ON_AXIS"
            };
            int n;
            var rows = new List<CheckHoleCg2ExpectedHoleRow> { row };
            check("POSITIVE_FOUND_IN_REVIEW", Cg35CheckLabeledPosition(rows, a, b,
                center, true, out n) == Cg35ScreenResult.MatchedPositiveP1Review && n == 1);
            check("ONE_SIDED_P1_IS_WARNING_NOT_NG", Cg35CheckLabeledPosition(rows,
                a, b, center, false, out n) == Cg35ScreenResult.NegativeOverlapsP1Review);
            check("DIFFERENT_TARGET_REJECTED", Cg35CheckLabeledPosition(rows,
                a, "fixture-C", center, true, out n) == Cg35ScreenResult.NoCandidate);
            check("FAR_POINT_REJECTED", Cg35CheckLabeledPosition(rows,
                a, b, new[] { 1.01, 2.0, 3.0 }, true, out n) == Cg35ScreenResult.NoCandidate);
            check("DUPLICATE_IS_AMBIGUOUS", Cg35CheckLabeledPosition(
                new List<CheckHoleCg2ExpectedHoleRow> { row, row }, a, b, center,
                true, out n) == Cg35ScreenResult.Ambiguous && n == 2);
            row.Cg32State = "POSSIBLY_LINKED_PATTERN_NG_REVIEW";
            check("PATTERN_LINK_NOT_POSITIVE_P1", Cg35CheckLabeledPosition(rows,
                a, b, center, true, out n) == Cg35ScreenResult.PositiveReviewIncomplete);
            check("NON_P1_NOT_A_NEGATIVE_COLLISION", Cg35CheckLabeledPosition(rows,
                a, b, center, false, out n) == Cg35ScreenResult.NegativeNotP1);
            var knownOk = new Cg33ObservedPair { Source = a,
                Target = b, MidpointWorldM = center };
            check("KNOWN_OK_GEO_COLLISION_DETECTED",
                Cg35HasP1CollisionAtObservedOK(knownOk, rows));
            knownOk.Target = "fixture-C";
            check("KNOWN_OK_OTHER_PAIR_NOT_CONFLATED",
                !Cg35HasP1CollisionAtObservedOK(knownOk, rows));
            check("NO_NEGATIVE_LABELS_ARE_ASSUMED", Cg35VerifiedOneSidedNegatives.Length == 0);
            check("KNOWN_OK_PAIR_REQUIRES_BOTH_OCCURRENCES",
                !Cg33SamePair("A", "B", "A", "C") && Cg33SamePair("B", "A", "A", "B"));
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG35][TEST_SUMMARY] Passed={0}/{1}", passed, total));
        }
#else
        // Release builds do not execute any CG3.5 contrast audit.
        private static void Cg35ContrastAudit(ModelDoc2 model,
            IList<HoleStackResult> stackResults, CheckHoleCg2Report report) { }
#endif
    }
}
