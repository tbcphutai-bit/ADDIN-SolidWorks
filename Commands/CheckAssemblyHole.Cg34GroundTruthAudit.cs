// CG3.4: DEBUG-ONLY, READ-ONLY ground-truth audit of SIX user-verified missing holes.
// Fixture for the specific 2222-02-TI-b4 assembly state only; never a detector or NG rule.
// No COM geometry calls; no document edits, markers, UI, CSV or production NG modifications.
// Source + target OCCURRENCE and 3D world position, NOT unstable candidate IDs.
// C# 7.3 / .NET Framework 4.8.
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
        private const string Cg34FixtureAssemblyHint = "2222-02-TI-b4";
        private const string Cg34SourceOccurrence = "2222-04-TI-b4-ks1-1";
        private const string Cg34TargetOccurrence = "2222-04-TI-b4-fr1-1";
        // 0.5mm is strictly a DEBUG fixture-position matching tolerance;
        // it is NOT a production hole / coaxiality or manufacturing tolerance.
        private const double Cg34FixtureMatchToleranceM = 0.0005;

        private sealed class Cg34KnownHole
        {
            public int ReferenceNo;
            public double[] WorldMidpointM;
        }

        // Positions from CG3.3 (2026-11-03) log, manually confirmed by user.
        // Source ks1-1, target fr1-1; 2.000mm BREP material, midpoint Z=1909.288mm.
        private static readonly Cg34KnownHole[] Cg34KnownSix = new[] {
            Cg34Known(1, 1045.502, 1429.805, 1909.288),
            Cg34Known(2, 1115.502, 1427.300, 1909.288),
            Cg34Known(3, 1147.002, 1361.212, 1909.288),
            Cg34Known(4, 1147.002, 1111.212, 1909.288),
            Cg34Known(5, 1147.002,  861.212, 1909.288),
            Cg34Known(6, 1147.002,  611.212, 1909.288)
        };

        private static Cg34KnownHole Cg34Known(int id, double xMm, double yMm, double zMm)
        {
            return new Cg34KnownHole {
                ReferenceNo = id,
                WorldMidpointM = new[] { xMm / 1000.0, yMm / 1000.0, zMm / 1000.0 }
            };
        }

        private static bool Cg34IsFixturePair(CheckHoleCg2ExpectedHoleRow row)
        {
            return row != null &&
                string.Equals(row.SourceOccurrence, Cg34SourceOccurrence, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.TargetOccurrence, Cg34TargetOccurrence, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Cg34PointMatches(double[] rowPoint, Cg34KnownHole known)
        {
            return rowPoint != null && known != null &&
                Cg33DistanceM(rowPoint, known.WorldMidpointM) <= Cg34FixtureMatchToleranceM;
        }

        private static bool Cg34CandidateIsReviewEligible(CheckHoleCg2ExpectedHoleRow row)
        {
            return row != null && row.Cg31ReviewPriority == 1 &&
                string.Equals(row.Cg32State, "NO_EXISTING_NG_FOR_PAIR_REVIEW", StringComparison.Ordinal) &&
                string.Equals(row.Cg33Verdict, "REVIEW_ONLY_INTENT_NOT_PROVEN", StringComparison.Ordinal) &&
                string.Equals(row.MaterialEvidence, "BREP_SOLID_ON_AXIS", StringComparison.Ordinal);
        }

        // Called after CG3.3 has populated evidence. The six manual labels are
        // validation data: passing is NOT an algorithmic proof of design intent.
        private static void Cg34GroundTruthAudit(ModelDoc2 model, CheckHoleCg2Report report)
        {
            try
            {
                string title = model == null ? null : model.GetTitle();
                if (string.IsNullOrWhiteSpace(title) ||
                    title.IndexOf(Cg34FixtureAssemblyHint, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Debug.WriteLine("[CG34][SKIP] Not the known benchmark assembly; no labels applied.");
                    return;
                }
                if (report == null || report.ExpectedHoleCandidates == null ||
                    report.IsPartial || !string.Equals(report.CoverageStatus, "COMPLETE", StringComparison.OrdinalIgnoreCase))
                {
                    // CG2 broad-phase may properly report partial BREP coverage. We do NOT
                    // require global COMPLETE here because 143 other candidates are unverified.
                    if (report == null || report.ExpectedHoleCandidates == null)
                    {
                        Debug.WriteLine("[CG34][SKIP] No CG2 report.");
                        return;
                    }
                    Debug.WriteLine("[CG34][NOTE] CG2 overall coverage is partial; benchmark checks only six labeled points.");
                }

                var pairRows = report.ExpectedHoleCandidates.Where(Cg34IsFixturePair).ToList();
                int matched = 0, missed = 0, ambiguous = 0, foundButNotEligible = 0;
                var allMatched = new HashSet<CheckHoleCg2ExpectedHoleRow>();
                foreach (Cg34KnownHole known in Cg34KnownSix)
                {
                    var candidates = pairRows.Where(r => Cg34PointMatches(Cg33CandidateMiddle(r), known)).ToList();
                    string state;
                    if (candidates.Count == 0)
                    {
                        missed++;
                        state = "MISSING_FROM_CANDIDATE_SET";
                    }
                    else if (candidates.Count != 1)
                    {
                        ambiguous++;
                        state = "AMBIGUOUS_DUPLICATED_CANDIDATES";
                        foreach (var row in candidates) allMatched.Add(row);
                    }
                    else
                    {
                        CheckHoleCg2ExpectedHoleRow row = candidates[0];
                        allMatched.Add(row);
                        if (Cg34CandidateIsReviewEligible(row))
                        {
                            matched++;
                            state = "MANUAL_TRUE_POSITIVE_IN_REVIEW";
                        }
                        else
                        {
                            foundButNotEligible++;
                            state = "DETECTED_BUT_REVIEW_CHAIN_INCOMPLETE";
                        }
                    }
                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "[CG34][CASE] GroundTruthNo={0} WorldMm=({1:F3},{2:F3},{3:F3}) CandidateCount={4} State={5}",
                        known.ReferenceNo,
                        known.WorldMidpointM[0] * 1000.0,
                        known.WorldMidpointM[1] * 1000.0,
                        known.WorldMidpointM[2] * 1000.0,
                        candidates.Count, state));
                }
                int extras = pairRows.Count(r => r.Cg31ReviewPriority == 1 && !allMatched.Contains(r));
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG34][GROUND_TRUTH] KnownMissing=6 MatchedReviewP1={0} Missed={1} Ambiguous={2} DetectedIncomplete={3} ExtraUnlabeledP1SamePair={4} MissingHoleNgCreated=0 ProductionNgChanged=NO",
                    matched, missed, ambiguous, foundButNotEligible, extras));
                Debug.WriteLine("[CG34][NOTICE] Manually labeled benchmark only. Do NOT use labels as universal missing-hole rule. An unlabeled P1 is NOT automatically a false positive.");
                Cg34PureTestsOnce();
            }
            catch (Exception e)
            {
                // An optional fixture must never break production Check Hole.
                Debug.WriteLine("[CG34][ERROR] Benchmark audit skipped: " + e.GetType().Name + " " + e.Message);
            }
        }

        private static bool cg34Tested;
        private static void Cg34PureTestsOnce()
        {
            if (cg34Tested) return;
            cg34Tested = true;
            int passed = 0, total = 0;
            Action<string, bool> check = (name, ok) => {
                ++total;
                if (ok) ++passed;
                Debug.WriteLine("[CG34][TEST] " + name + "=" + (ok ? "PASS" : "FAIL"));
            };
            var p = Cg34KnownSix[0].WorldMidpointM;
            check("EXACT_COORDINATE_MATCH", Cg34PointMatches((double[])p.Clone(), Cg34KnownSix[0]));
            check("OFFSET_1MM_REJECTED", !Cg34PointMatches(new[] { p[0] + 0.001, p[1], p[2] }, Cg34KnownSix[0]));
            check("SIX_DISTINCT_POSITIONS", Cg34KnownSix.All(x => Cg34KnownSix.Count(y =>
                Cg34PointMatches(y.WorldMidpointM, x)) == 1));
            var row = new CheckHoleCg2ExpectedHoleRow {
                SourceOccurrence = Cg34SourceOccurrence,
                TargetOccurrence = Cg34TargetOccurrence,
                Cg31ReviewPriority = 1,
                Cg32State = "NO_EXISTING_NG_FOR_PAIR_REVIEW",
                Cg33Verdict = "REVIEW_ONLY_INTENT_NOT_PROVEN",
                MaterialEvidence = "BREP_SOLID_ON_AXIS"
            };
            check("EXACT_COMPONENT_OCCURRENCES", Cg34IsFixturePair(row));
            row.TargetOccurrence = "2222-04-TI-b4-fr4-1";
            check("OTHER_TARGET_REJECTED", !Cg34IsFixturePair(row));
            row.TargetOccurrence = Cg34TargetOccurrence;
            check("REVIEW_CHAIN_ACCEPTED", Cg34CandidateIsReviewEligible(row));
            row.Cg32State = "POSSIBLY_LINKED_PATTERN_NG_REVIEW";
            check("PATTERN_LINK_REJECTED", !Cg34CandidateIsReviewEligible(row));
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG34][TEST_SUMMARY] Passed={0}/{1}", passed, total));
        }
#else
        // DEBUG-only benchmark has no Release behavior.
        private static void Cg34GroundTruthAudit(ModelDoc2 model, CheckHoleCg2Report report) { }
#endif
    }
}
