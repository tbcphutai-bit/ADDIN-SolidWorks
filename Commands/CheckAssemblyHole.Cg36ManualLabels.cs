// CG3.6 — Optional DEBUG-only manual intent labels and review-screen confusion audit.
// Labels are NOT inferred from geometry; unverified remains unverified.
// Read-only: no model feature/edit, NG/CSV/marker/UI/OpenGL modification and no new BREP calls.
// Release build: completely disabled. C# 7.3 / .NET Framework 4.8.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
#if DEBUG
        private const string Cg36LabelFileName = "CG36_ManualLabels.tsv";
        private const int Cg36MaxLogRows = 16;
        private enum Cg36Intent { Unverified, MissingConfirmed, OneSidedOk }

        private sealed class Cg36ManualLabel
        {
            public int Line;
            public string AssemblyTitle;
            public string Configuration;
            public string SourceOccurrence;
            public string TargetOccurrence;
            public double[] WorldMidpointM;
            public Cg36Intent Intent;
            public string Evidence;
        }

        private static string Cg36NormalizeTitle(string title)
        {
            string value = (title ?? "").Trim();
            if (value.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - 7);
            return value;
        }

        private static bool Cg36SameDocument(Cg36ManualLabel label, string title, string configuration)
        {
            return label != null && string.Equals(Cg36NormalizeTitle(label.AssemblyTitle),
                Cg36NormalizeTitle(title), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(label.Configuration ?? "", configuration ?? "", StringComparison.Ordinal);
        }

        private static bool Cg36SamePosition(Cg36ManualLabel a, Cg36ManualLabel b)
        {
            return a != null && b != null &&
                Cg36SameDocument(a, b.AssemblyTitle, b.Configuration) &&
                Cg32EqualOccurrence(a.SourceOccurrence, b.SourceOccurrence) &&
                Cg32EqualOccurrence(a.TargetOccurrence, b.TargetOccurrence) &&
                Cg33ValidPoint(a.WorldMidpointM) && Cg33ValidPoint(b.WorldMidpointM) &&
                Cg33DistanceM(a.WorldMidpointM, b.WorldMidpointM) <= Cg34FixtureMatchToleranceM;
        }

        // Fixed nine-column TSV: no implicit defaults, no inferred label from blank cells.
        // Header and comment lines are handled by the reader. Evidence is mandatory
        // for a CONFIRMED label to discourage accidental classifications.
        private static bool Cg36TryParseLine(string raw, int lineNumber,
            out Cg36ManualLabel label, out string error)
        {
            label = null;
            error = null;
            string[] c = (raw ?? "").TrimStart('\uFEFF').Split('\t');
            if (c.Length != 9) { error = "EXPECTED_9_TSV_COLUMNS"; return false; }
            for (int i = 0; i < c.Length; i++) c[i] = c[i].Trim();
            double x, y, z;
            if (c[0].Length == 0 || c[1].Length == 0 || c[2].Length == 0 || c[3].Length == 0)
            { error = "MISSING_DOC_CONFIGURATION_OR_OCCURRENCE"; return false; }
            if (!double.TryParse(c[4], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !double.TryParse(c[5], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !double.TryParse(c[6], NumberStyles.Float, CultureInfo.InvariantCulture, out z) ||
                double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z) ||
                double.IsInfinity(x) || double.IsInfinity(y) || double.IsInfinity(z))
            { error = "INVALID_WORLD_MM_USE_DOT_DECIMALS"; return false; }
            Cg36Intent intent;
            if (string.Equals(c[7], "UNVERIFIED", StringComparison.OrdinalIgnoreCase))
                intent = Cg36Intent.Unverified;
            else if (string.Equals(c[7], "MISSING_CONFIRMED", StringComparison.OrdinalIgnoreCase))
                intent = Cg36Intent.MissingConfirmed;
            else if (string.Equals(c[7], "ONE_SIDED_OK", StringComparison.OrdinalIgnoreCase))
                intent = Cg36Intent.OneSidedOk;
            else { error = "INVALID_INTENT_USE_UNVERIFIED_MISSING_CONFIRMED_OR_ONE_SIDED_OK"; return false; }
            if (intent != Cg36Intent.Unverified && c[8].Length == 0)
            { error = "CONFIRMED_LABEL_REQUIRES_EVIDENCE"; return false; }
            label = new Cg36ManualLabel {
                Line = lineNumber, AssemblyTitle = c[0], Configuration = c[1],
                SourceOccurrence = c[2], TargetOccurrence = c[3],
                WorldMidpointM = new[] { x / 1000.0, y / 1000.0, z / 1000.0 },
                Intent = intent, Evidence = c[8]
            };
            return true;
        }

        private static List<Cg36ManualLabel> Cg36ReadManualLabels(string path,
            out int rejected, out int duplicate)
        {
            rejected = 0; duplicate = 0;
            var result = new List<Cg36ManualLabel>();
            if (!File.Exists(path))
            {
                Debug.WriteLine("[CG36][LABEL_FILE] ABSENT Path=" + path +
                    " Action=KEEP_UNVERIFIED_TEMPLATE_NOT_INSTALLED");
                return result;
            }
            string[] lines = File.ReadAllLines(path, new UTF8Encoding(false, true));
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i].TrimStart('\uFEFF').Trim();
                if (raw.Length == 0 || raw.StartsWith("#", StringComparison.Ordinal) ||
                    raw.StartsWith("AssemblyTitle\t", StringComparison.OrdinalIgnoreCase))
                    continue;
                Cg36ManualLabel parsed;
                string error;
                if (!Cg36TryParseLine(raw, i + 1, out parsed, out error))
                {
                    ++rejected;
                    Debug.WriteLine("[CG36][REJECT] Line=" + (i + 1) + " Reason=" + error);
                    continue;
                }
                if (result.Any(existing => Cg36SamePosition(existing, parsed)))
                {
                    ++duplicate;
                    Debug.WriteLine("[CG36][REJECT] Line=" + (i + 1) +
                        " Reason=DUPLICATE_DOCUMENT_PAIR_POSITION");
                    continue;
                }
                result.Add(parsed);
            }
            Debug.WriteLine("[CG36][LABEL_FILE] LOADED Count=" + result.Count +
                " Rejected=" + rejected + " Duplicates=" + duplicate +
                " ReadOnly=YES");
            return result;
        }

        private static bool Cg36OverlapsKnownPositive(Cg36ManualLabel label)
        {
            return label != null &&
                Cg36NormalizeTitle(label.AssemblyTitle).IndexOf(Cg34FixtureAssemblyHint,
                    StringComparison.OrdinalIgnoreCase) >= 0 &&
                Cg32EqualOccurrence(label.SourceOccurrence, Cg34SourceOccurrence) &&
                Cg32EqualOccurrence(label.TargetOccurrence, Cg34TargetOccurrence) &&
                Cg34KnownSix.Any(p => Cg33DistanceM(p.WorldMidpointM,
                    label.WorldMidpointM) <= Cg34FixtureMatchToleranceM);
        }

        // Independently confirmed intent is evaluated against *existing* P1 review
        // data. This DOES NOT convert it into production Missing Hole NG.
        private static void Cg36ManualIntentAudit(ModelDoc2 model, CheckHoleCg2Report report)
        {
            try
            {
                Cg36PureTestsOnce();
                if (model == null || report == null || report.ExpectedHoleCandidates == null)
                {
                    Debug.WriteLine("[CG36][SKIP] No valid model / CG2 report");
                    return;
                }
                string title = model.GetTitle() ?? "";
                string configuration = SafeConfigurationName(model) ?? "";
                string folder = Path.GetDirectoryName(typeof(CheckAssemblyHole).Assembly.Location);
                if (string.IsNullOrWhiteSpace(folder))
                {
                    Debug.WriteLine("[CG36][SKIP] Add-in DLL folder unavailable");
                    return;
                }
                string path = Path.Combine(folder, Cg36LabelFileName);
                int rejected, duplicates;
                List<Cg36ManualLabel> labels = Cg36ReadManualLabels(path, out rejected, out duplicates);
                var allRows = report.ExpectedHoleCandidates.Where(x => x != null).ToList();
                // CG3.3 can legitimately have incomplete/invalid Entry-Exit rows.
                // Never pass those to CG3.5 position matcher, which requires a midpoint.
                var rows = allRows.Where(x => Cg33ValidPoint(Cg33CandidateMiddle(x))).ToList();
                var p1 = rows.Where(x => x.Cg31ReviewPriority == 1).ToList();
                int invalidMidpointRows = allRows.Count - rows.Count;
                int applied = 0, unknown = 0, confirmedMissing = 0, confirmedOneSided = 0;
                int positiveP1 = 0, positiveNotP1 = 0, positiveMissed = 0;
                int negativeP1 = 0, negativeNotP1 = 0, negativeNoCandidate = 0;
                int ambiguous = 0, conflicts = 0;
                var coveredRows = new HashSet<CheckHoleCg2ExpectedHoleRow>();
                // Six positives already have independent user-confirmed labels in
                // CG3.4. They are not "unlabeled" even when the optional TSV is absent.
                if (Cg36NormalizeTitle(title).IndexOf(Cg34FixtureAssemblyHint,
                    StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foreach (var row in p1.Where(Cg34IsFixturePair))
                        if (Cg34KnownSix.Any(known => Cg33DistanceM(
                                Cg33CandidateMiddle(row), known.WorldMidpointM) <=
                            Cg34FixtureMatchToleranceM)) coveredRows.Add(row);
                }
                foreach (Cg36ManualLabel label in labels)
                {
                    if (!Cg36SameDocument(label, title, configuration))
                        continue;
                    ++applied;
                    if (label.Intent == Cg36Intent.Unverified)
                    {
                        ++unknown;
                        Debug.WriteLine("[CG36][LABEL] Line=" + label.Line +
                            " Intent=UNVERIFIED Verdict=NO_GROUND_TRUTH_NOT_SCORED");
                        continue;
                    }
                    // Never let an external label contradict or double-count the six
                    // independently documented CG3.4 positives on this benchmark.
                    if (Cg36OverlapsKnownPositive(label))
                    {
                        ++conflicts;
                        Debug.WriteLine("[CG36][CONFLICT] Line=" + label.Line +
                            " Reason=OVERLAPS_EXISTING_SIX_POSITIVE_FIXTURE Action=NOT_SCORED");
                        continue;
                    }
                    int count;
                    bool positive = label.Intent == Cg36Intent.MissingConfirmed;
                    Cg35ScreenResult state = Cg35CheckLabeledPosition(rows,
                        label.SourceOccurrence, label.TargetOccurrence,
                        label.WorldMidpointM, positive, out count);
                    if (state == Cg35ScreenResult.Ambiguous || state == Cg35ScreenResult.InvalidLabel)
                    {
                        ++ambiguous;
                    }
                    else if (positive)
                    {
                        ++confirmedMissing;
                        if (state == Cg35ScreenResult.MatchedPositiveP1Review) ++positiveP1;
                        else if (state == Cg35ScreenResult.NoCandidate) ++positiveMissed;
                        else ++positiveNotP1;
                    }
                    else
                    {
                        ++confirmedOneSided;
                        if (state == Cg35ScreenResult.NegativeOverlapsP1Review) ++negativeP1;
                        else if (state == Cg35ScreenResult.NoCandidate) ++negativeNoCandidate;
                        else ++negativeNotP1;
                    }
                    foreach (CheckHoleCg2ExpectedHoleRow row in p1)
                    {
                        if (Cg35ExactPair(row, label.SourceOccurrence, label.TargetOccurrence) &&
                            Cg33DistanceM(Cg33CandidateMiddle(row), label.WorldMidpointM) <=
                            Cg34FixtureMatchToleranceM) coveredRows.Add(row);
                    }
                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "[CG36][LABEL] Line={0} Intent={1} Source={2} Target={3} WorldMm=({4:F3},{5:F3},{6:F3}) MatchedCandidates={7} Screen={8} Action=HUMAN_REVIEW_ONLY",
                        label.Line, label.Intent, label.SourceOccurrence, label.TargetOccurrence,
                        label.WorldMidpointM[0] * 1000.0, label.WorldMidpointM[1] * 1000.0,
                        label.WorldMidpointM[2] * 1000.0, count, state));
                }
                var pending = p1.Where(r => !coveredRows.Contains(r)).ToList();
                int unlabeledP1 = pending.Count;
                foreach (var row in pending.Take(Cg36MaxLogRows))
                {
                    double[] mid = Cg33CandidateMiddle(row);
                    Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "[CG36][PENDING] Source={0} Target={1} MidMm=({2:F3},{3:F3},{4:F3}) Intent=UNVERIFIED Action=HUMAN_DESIGN_REVIEW_REQUIRED",
                        row.SourceOccurrence, row.TargetOccurrence,
                        mid[0] * 1000.0, mid[1] * 1000.0, mid[2] * 1000.0));
                }
                // This ratio concerns the P1 REVIEW SCREEN only (not the
                // production false-positive rate, which cannot be inferred).
                int negativeEvaluated = negativeP1 + negativeNotP1 + negativeNoCandidate;
                string negativeReviewRate = negativeEvaluated == 0 ? "NOT_MEASURABLE" :
                    (100.0 * negativeP1 / negativeEvaluated).ToString("F1", CultureInfo.InvariantCulture) + "%";
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG36][SUMMARY] Assembly={0} Configuration={1} FileLabels={2} Applied={3} Unknown={4} ConfirmedMissing={5} PositiveP1={6} PositiveNotP1={7} PositiveMissed={8} ConfirmedOneSidedOK={9} NegativeP1Review={10} NegativeNotP1={11} NegativeNoCandidate={12} Ambiguous={13} Conflicts={14} Rejected={15} Duplicates={16} UnlabeledP1={17} InvalidMidpointRowsExcluded={18} NegativeReviewFlagRate={19} MissingHoleNgCreated=0 ProductionNgChanged=NO",
                    title, configuration, labels.Count, applied, unknown,
                    confirmedMissing, positiveP1, positiveNotP1, positiveMissed,
                    confirmedOneSided, negativeP1, negativeNotP1, negativeNoCandidate,
                    ambiguous, conflicts, rejected, duplicates, unlabeledP1,
                    invalidMidpointRows, negativeReviewRate));
                Debug.WriteLine("[CG36][LIMITATION] NegativeReviewFlagRateIsNOTProductionFPR=YES " +
                    "OneSidedIntentFromManualEvidenceOnly=YES AutoMissingHoleNG=DISABLED " +
                    "UnverifiedCandidateNotFalsePositive=YES " +
                    "BenchmarkPositiveLabelsStillInCG34=YES");
            }
            catch (Exception ex)
            {
                // Optional DEBUG audit must never break the Check Hole command.
                Debug.WriteLine("[CG36][ERROR] Optional manual label audit skipped: " +
                    ex.GetType().Name + " " + ex.Message);
            }
        }

        private static bool cg36TestsRan;
        private static void Cg36PureTestsOnce()
        {
            if (cg36TestsRan) return;
            cg36TestsRan = true;
            int passed = 0, total = 0;
            Action<string, bool> check = (name, ok) => {
                ++total;
                if (ok) ++passed;
                Debug.WriteLine("[CG36][TEST] " + name + "=" + (ok ? "PASS" : "FAIL"));
            };
            Cg36ManualLabel l;
            string error;
            string prefix = "fixture.SLDASM\tDefault\tA\tB\t1000.000\t2000.000\t3000.000\t";
            check("UNKNOWN_PARSE_NOT_NEGATIVE", Cg36TryParseLine(prefix +
                "UNVERIFIED\tAwaiting user decision", 1, out l, out error) &&
                l.Intent == Cg36Intent.Unverified);
            check("ONE_SIDED_PARSE", Cg36TryParseLine(prefix +
                "ONE_SIDED_OK\tDrawing checked", 2, out l, out error) &&
                l.Intent == Cg36Intent.OneSidedOk);
            check("CONFIRMED_REQUIRES_EVIDENCE", !Cg36TryParseLine(prefix +
                "MISSING_CONFIRMED\t", 3, out l, out error));
            check("REJECT_INVALID_LABEL", !Cg36TryParseLine(prefix +
                "SUSPECTED\tEvidence", 4, out l, out error));
            check("REJECT_BAD_COLUMN_COUNT", !Cg36TryParseLine("a\tb", 5, out l, out error));
            check("REJECT_COMMA_DECIMAL", !Cg36TryParseLine(prefix.Replace("1000.000", "1000,000") +
                "ONE_SIDED_OK\tE", 6, out l, out error));
            Cg36ManualLabel a, b;
            Cg36TryParseLine(prefix + "UNVERIFIED\tPending", 7, out a, out error);
            Cg36TryParseLine(prefix + "ONE_SIDED_OK\tChecked", 8, out b, out error);
            check("DUPLICATE_POSITION_REJECT", Cg36SamePosition(a, b));
            check("SAME_DOC_EXTENSION_NORMALIZED", Cg36SameDocument(a, "fixture", "Default"));
            check("OTHER_CONFIGURATION_REJECTED", !Cg36SameDocument(a, "fixture", "Other"));
            Cg36TryParseLine(prefix.Replace("\tB\t", "\tC\t") +
                "ONE_SIDED_OK\tChecked", 9, out b, out error);
            check("OTHER_TARGET_NOT_DUPLICATE", !Cg36SamePosition(a, b));
            a = new Cg36ManualLabel { AssemblyTitle = Cg34FixtureAssemblyHint,
                Configuration = "Default", SourceOccurrence = Cg34SourceOccurrence,
                TargetOccurrence = Cg34TargetOccurrence,
                WorldMidpointM = (double[])Cg34KnownSix[0].WorldMidpointM.Clone() };
            check("PREVENT_CONFLICT_WITH_SIX_POSITIVES", Cg36OverlapsKnownPositive(a));
            a.WorldMidpointM[0] += 0.002;
            check("DISTINCT_POSITION_NOT_CONFLICT", !Cg36OverlapsKnownPositive(a));
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG36][TEST_SUMMARY] Passed={0}/{1}", passed, total));
        }
#else
        private static void Cg36ManualIntentAudit(ModelDoc2 model, CheckHoleCg2Report report) { }
#endif
    }
}
