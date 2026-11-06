// CG2.2 FIX1: pure diagnostics and synthetic ray-parser regression tests.
// C# 7.3 / .NET Framework 4.8. No CAD mutation, production NG or CSV writes.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
#if DEBUG
        private static bool _cg22Fix1TestsExecuted;

        private static void Cg22Fix1TraceRaw(
            CheckHoleCg2ExpectedHoleRow row,
            double[] raw,
            int hitCount,
            int bodyCount,
            string primaryEvidence)
        {
            if (row == null || !string.Equals(primaryEvidence,
                "BREP_SOLID_ON_AXIS", StringComparison.Ordinal)) return;

            int size = raw == null ? -1 : raw.Length;
            int stride = raw == null || hitCount <= 0 ? 0 :
                size == hitCount * 9 ? 9 : size == hitCount * 6 ? 6 : 0;
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG22][FIX1_RAW] Id={0} Source={1} Target={2} Hole={3} " +
                "Hits={4} Bodies={5} RawLength={6} Stride={7}",
                row.Id, row.SourceOccurrence, row.TargetOccurrence,
                row.SourceHoleLabel, hitCount, bodyCount, size, stride));
            if (stride == 0) return;

            int n = Math.Min(hitCount, 12);
            for (int i = 0; i < n; ++i)
            {
                int p = i * stride;
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG22][FIX1_HIT] Id={0} i={1} Body={2} Ray={3} Flags={4} " +
                    "X={5:R} Y={6:R} Z={7:R}",
                    row.Id, i, raw[p], raw[p + 1], raw[p + 2],
                    raw[p + 3], raw[p + 4], raw[p + 5]));
            }
            if (hitCount > n)
                Debug.WriteLine("[CG22][FIX1_HIT] TruncatedRemaining=" + (hitCount - n));
        }

        private static void Cg22Fix1WriteSummary(CheckHoleCg2Report report)
        {
            if (report == null || report.ExpectedHoleCandidates == null) return;
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG22][FIX1_SUMMARY] Candidates={0} BREP_SOLID={1} " +
                "NO_HIT={2} UNKNOWN={3} NOT_TESTED={4} Coverage={5}",
                report.ExpectedHoleCandidates.Count,
                report.BrepSolidEvidenceCount, report.BrepNoAxisHitCount,
                report.BrepUnknownCount, report.BrepNotTestedCount,
                report.CoverageStatus));
            foreach (var group in report.ExpectedHoleCandidates
                .GroupBy(r => (r.Cg22GeometryStatus ?? "NULL") + " | " +
                              (r.Cg22ProbeStatus ?? "NULL"))
                .OrderBy(g => g.Key))
            {
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG22][FIX1_GEOMETRY] {0} = {1}", group.Key, group.Count()));
            }
            // Focus is a PART PAIR, not a volatile Hole# index or Candidate ID.
            foreach (var r in report.ExpectedHoleCandidates.Where(r =>
                string.Equals(r.SourceOccurrence, "2222-04-TI-b4-ks1-1",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.TargetOccurrence, "2222-04-TI-b4-fr1-1",
                    StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.Id))
            {
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG22][FIX1_FOCUS] Id={0} Hole={1} Material={2} " +
                    "SpanMm={3:F4} Hits={4} Geometry={5} Probe={6} " +
                    "Entry={7} Exit={8}",
                    r.Id, r.SourceHoleLabel, r.MaterialEvidence,
                    r.MaterialSpanMm, r.MaterialHitCount,
                    r.Cg22GeometryStatus, r.Cg22ProbeStatus,
                    r.Cg22HitEntryWorldMm ?? "NULL",
                    r.Cg22HitExitWorldMm ?? "NULL"));
            }
            Cg22Fix1RunTestsOnce();
        }

        private static void Cg22Fix1RunTestsOnce()
        {
            if (_cg22Fix1TestsExecuted) return;
            _cg22Fix1TestsExecuted = true;
            int total = 0, passed = 0;
            int enter = (int)swRayPtsResults_e.swRayPtsResultsENTER;
            int exit = (int)swRayPtsResults_e.swRayPtsResultsEXIT;
            double[] start = { 0.0, 0.0, 0.0 };
            double[] dir = { 0.0, 0.0, 1.0 };

            // Each tuple: body,ray,flags,xyz,(normal optional).
            double[] six = { 0,0,enter, 0,0,.010, 0,0,exit, 0,0,.020 };
            Cg22Fix1Test("SIX_DOUBLES", six, 2, 1, start, dir, 1, true,
                .010, false, ref total, ref passed);
            double[] nine = {
                0,0,enter, 0,0,.010, 0,0,-1,
                0,0,exit,  0,0,.020, 0,0, 1
            };
            Cg22Fix1Test("NINE_DOUBLES", nine, 2, 1, start, dir, 1, true,
                .010, true, ref total, ref passed);
            double[] multi = {
                0,0,enter, 0,0,.010, 0,0,exit, 0,0,.020,
                0,0,enter, 0,0,.030, 0,0,exit, 0,0,.050
            };
            Cg22Fix1Test("FOUR_HITS", multi, 4, 1, start, dir, 1, true,
                .020, false, ref total, ref passed);
            double[] separate = {
                0,0,enter, 0,0,.010,
                1,0,exit,  0,0,.030
            };
            Cg22Fix1Test("DIFFERENT_BODIES", separate, 2, 2,
                start, dir, 1, false, 0, false, ref total, ref passed);
            double[] orphan = { 0,0,enter, 0,0,.010 };
            Cg22Fix1Test("UNPAIRED_ENTRY", orphan, 1, 1, start, dir,
                1, false, 0, false, ref total, ref passed);
            Cg22Fix1Test("INVALID_LAYOUT", new double[] { 0,0,enter,0,0 },
                1, 1, start, dir, 1, false, 0, false, ref total, ref passed);
            Cg22Fix1Test("OUTSIDE_LIMIT", six, 2, 1, start, dir,
                .005, false, 0, false, ref total, ref passed);
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG22][FIX1_TEST_SUMMARY] Passed={0}/{1}", passed, total));
        }

        private static void Cg22Fix1Test(string name, double[] payload,
            int hits, int bodies, double[] start, double[] direction, double maxDist,
            bool expectSuccess, double expectSpan, bool expectNormal,
            ref int total, ref int passed)
        {
            ++total;
            Cg22Span parsed;
            string error;
            bool actual = Cg22TryExtractLongestSpan(payload, hits, bodies,
                start, direction, maxDist, out parsed, out error);
            bool ok = actual == expectSuccess;
            if (expectSuccess)
                ok &= parsed != null &&
                    Math.Abs(parsed.LengthM - expectSpan) < 1e-7 &&
                    (parsed.EntryNormalLocal != null) == expectNormal;
            if (ok) ++passed;
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG22][FIX1_TEST] Name={0} Status={1} Reason={2}",
                name, ok ? "PASS" : "FAIL", error));
        }
#else
        // Release builds never emit these diagnostic logs or run synthetic tests.
        private static void Cg22Fix1TraceRaw(
            CheckHoleCg2ExpectedHoleRow row, double[] raw,
            int hitCount, int bodyCount, string evidence) { }
        private static void Cg22Fix1WriteSummary(CheckHoleCg2Report report) { }
#endif
    }
}
