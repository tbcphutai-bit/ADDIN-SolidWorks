// CG2.2 - READ-ONLY BREP GEOMETRY EVIDENCE.
// C# 7.3, .NET Framework 4.8, SOLIDWORKS 2024 SP5 compatible syntax.
// Only diagnostic data. NO production NG / marker / CSV mutation.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
        private const double Cg22ProbeRadiusM = 0.00030; // 0.30 mm, DIAGNOSTIC only
        private const int Cg22ProbeCount = 4;
        private const int Cg22MaxProbeElapsedMs = 2000; // per-candidate soft budget

        private sealed class Cg22Hit
        {
            public int BodyIndex;
            public int Flags;
            public double T;
            public double[] PointLocal;
            public double[] NormalLocal;
        }

        private sealed class Cg22Span
        {
            public double LengthM;
            public double[] EntryLocal;
            public double[] ExitLocal;
            public double[] EntryNormalLocal;
        }

        // Diagnostic fields are populated ONLY from validated ray data.
        // A missing normal (6-double layout) is not treated as a zero normal.
        private void Cg22CaptureCandidateEvidence(
            CheckHoleCg2ExpectedHoleRow row,
            Cg2TargetBodyContext target,
            HoleAxis sourceHole,
            double[] primaryStartLocal,
            double[] primaryDirectionLocal,
            double primaryLimitM,
            string primaryEvidence,
            int primaryHitCount,
            Stopwatch overallBrepWatch, double[] primaryRawSnapshot)
        {
            if (row == null || target == null || sourceHole == null)
                return;

            row.Cg22GeometryStatus = "NOT_APPLICABLE_PRIMARY_NOT_SOLID";
            row.Cg22ProbeStatus = "NOT_TESTED";
            if (!string.Equals(primaryEvidence, "BREP_SOLID_ON_AXIS", StringComparison.Ordinal))
                return;

            // FIX1: use the EXACT primary ray payload from CG2.1.
            // A second GetRayIntersectionsPoints() may return consumed or stale
            // COM state, so do NOT call it here.
            double[] nativePoints = primaryRawSnapshot;
            Cg22Fix1TraceRaw(row, nativePoints, primaryHitCount,
                target.Bodies == null ? 0 : target.Bodies.Length,
                primaryEvidence);
            if (nativePoints == null)
            {
                row.Cg22GeometryStatus = "UNVERIFIED_PRIMARY_NO_SNAPSHOT";
                return;
            }

            Cg22Span span;
            string parseFailure;
            if (!Cg22TryExtractLongestSpan(nativePoints, primaryHitCount,
                target.Bodies.Length, primaryStartLocal, primaryDirectionLocal,
                primaryLimitM, out span, out parseFailure))
            {
                row.Cg22GeometryStatus = "UNVERIFIED_PRIMARY_" + parseFailure;
                return;
            }

            // Cg2LoadTargetBodyContext already checked the coordinate system.
            MathTransform placement = target.Component.Transform2;
            if (placement == null)
            {
                row.Cg22GeometryStatus = "UNVERIFIED_NO_PLACEMENT";
                return;
            }
            double[] entryWorld = TransformPoint(span.EntryLocal, placement);
            double[] exitWorld = TransformPoint(span.ExitLocal, placement);
            if (!Cg2ValidVector(entryWorld) || !Cg2ValidVector(exitWorld))
            {
                row.Cg22GeometryStatus = "UNVERIFIED_WORLD_POINTS";
                return;
            }
            row.Cg22HitEntryWorldMm = Cg22FormatPointMm(entryWorld);
            row.Cg22HitExitWorldMm = Cg22FormatPointMm(exitWorld);
            // CG2.3 receives unrounded coordinates in metres.
            // Do not reparse rounded GUI strings into geometry.
            row.Cg23EntryWorldM = (double[])entryWorld.Clone();
            row.Cg23ExitWorldM = (double[])exitWorld.Clone();
            row.Cg22GeometryStatus = "VALID_ENTRY_EXIT_DIAGNOSTIC_ONLY";

            if (Cg2ValidVector(span.EntryNormalLocal))
            {
                double[] normalWorld = TransformVector(span.EntryNormalLocal, placement);
                double[] n = Cg22Unit(normalWorld);
                double[] axis = Cg22Unit(sourceHole.Direction);
                if (n != null && axis != null)
                {
                    double absDot = Math.Min(1.0, Math.Abs(Cg22Dot(n, axis)));
                    row.Cg22EntryNormalWorld = Cg22FormatDirection(n);
                    row.Cg22AbsoluteNormalDotAxis = absDot;
                    row.Cg22IncidenceDeg = Math.Acos(absDot) * 180.0 / Math.PI;
                }
            }
            // NO conclusion of actual mate, fastening intent, missing hole,
            // or even complete local clearance from a finite number of rays.
            Cg22ProbeFourOffsets(row, target, sourceHole, overallBrepWatch);
        }

        private static string Cg22FormatPointMm(double[] p)
        {
            if (!Cg2ValidVector(p)) return "UNVERIFIED";
            return string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3},{2:F3})",
                p[0] * 1000.0, p[1] * 1000.0, p[2] * 1000.0);
        }

        private static string Cg22FormatDirection(double[] p)
        {
            double[] unit = Cg22Unit(p);
            if (unit == null) return "UNVERIFIED";
            return string.Format(CultureInfo.InvariantCulture, "({0:F6},{1:F6},{2:F6})",
                unit[0], unit[1], unit[2]);
        }

        private static double[] Cg22Unit(double[] v)
        {
            if (!Cg2ValidVector(v)) return null;
            double magnitude = Math.Sqrt(Cg22Dot(v, v));
            if (magnitude < 1e-12 || double.IsNaN(magnitude) || double.IsInfinity(magnitude))
                return null;
            return new[] { v[0] / magnitude, v[1] / magnitude, v[2] / magnitude };
        }

        private static double Cg22Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static double[] Cg22Cross(double[] a, double[] b)
        {
            return new[] {
                a[1] * b[2] - a[2] * b[1],
                a[2] * b[0] - a[0] * b[2],
                a[0] * b[1] - a[1] * b[0]
            };
        }

        // Parses the raw SOLIDWORKS ray result without relying on a fixed stride.
        // Requires a definite ENTER->EXIT on the SAME body, within the bounded ray.
        // This method is read-only and does not call SOLIDWORKS COM APIs.
        private static bool Cg22TryExtractLongestSpan(
            double[] raw, int hitCount, int bodyCount,
            double[] rayStart, double[] rayDirection, double maxDistanceM,
            out Cg22Span bestSpan, out string failure)
        {
            bestSpan = null;
            failure = "INVALID_INPUT";
            if (hitCount <= 0 || hitCount > 200 || bodyCount <= 0 || raw == null ||
                !Cg2ValidVector(rayStart) || !Cg2ValidVector(rayDirection) ||
                maxDistanceM <= 0 || double.IsNaN(maxDistanceM)) return false;
            int stride = raw.Length == hitCount * 9 ? 9 :
                raw.Length == hitCount * 6 ? 6 : 0;
            if (stride == 0) { failure = "RAY_LAYOUT"; return false; }
            double[] u = Cg22Unit(rayDirection);
            if (u == null) { failure = "ZERO_RAY"; return false; }

            var hits = new List<Cg22Hit>(hitCount);
            for (int i = 0; i < hitCount; ++i)
            {
                int p = i * stride;
                for (int k = 0; k < stride; ++k)
                    if (double.IsNaN(raw[p + k]) || double.IsInfinity(raw[p + k]))
                    { failure = "NONFINITE_POINT"; return false; }
                double bodyValue = raw[p];
                double rayValue = raw[p + 1];
                double flagValue = raw[p + 2];
                if (bodyValue != Math.Truncate(bodyValue) ||
                    rayValue != 0 || flagValue != Math.Truncate(flagValue) ||
                    bodyValue < 0 || bodyValue >= bodyCount ||
                    flagValue < 0 || flagValue > int.MaxValue)
                { failure = "RAY_TOPOLOGY"; return false; }
                double[] point = { raw[p + 3], raw[p + 4], raw[p + 5] };
                double[] delta = { point[0] - rayStart[0],
                    point[1] - rayStart[1], point[2] - rayStart[2] };
                double t = Cg22Dot(delta, u);
                if (t < -Cg2BrepNumericalToleranceM ||
                    t > maxDistanceM + Cg2BrepNumericalToleranceM) continue;
                hits.Add(new Cg22Hit
                {
                    BodyIndex = (int)bodyValue,
                    Flags = (int)flagValue,
                    T = t,
                    PointLocal = point,
                    NormalLocal = stride == 9
                        ? new[] { raw[p + 6], raw[p + 7], raw[p + 8] }
                        : null
                });
            }

            foreach (var bodyHits in hits.GroupBy(h => h.BodyIndex))
            {
                Cg22Hit entry = null;
                foreach (Cg22Hit hit in bodyHits.OrderBy(h => h.T))
                {
                    bool isEntry = (hit.Flags & (int)swRayPtsResults_e.swRayPtsResultsENTER) != 0;
                    bool isExit = (hit.Flags & (int)swRayPtsResults_e.swRayPtsResultsEXIT) != 0;
                    if (isEntry && !isExit) entry = hit;
                    else if (isExit && !isEntry && entry != null)
                    {
                        double length = hit.T - entry.T;
                        if (length >= Cg2MinMaterialSpanM &&
                            (bestSpan == null || length > bestSpan.LengthM))
                        {
                            bestSpan = new Cg22Span
                            {
                                LengthM = length,
                                EntryLocal = entry.PointLocal,
                                ExitLocal = hit.PointLocal,
                                EntryNormalLocal = entry.NormalLocal
                            };
                        }
                        entry = null;
                    }
                    else entry = null; // grazing or contradictory hit
                }
            }
            if (bestSpan == null)
            {
                failure = "NO_DEFINITE_ENTRY_EXIT";
                return false;
            }
            failure = "NONE";
            return true;
        }

        // Four points on a circle perpendicular to the SOURCE HOLE AXIS.
        // Classification is deliberately DIAGNOSTIC ONLY and NEVER returns a
        // missing-hole verdict. All 4 solid rays do NOT rule out an offset hole.
        private void Cg22ProbeFourOffsets(
            CheckHoleCg2ExpectedHoleRow row, Cg2TargetBodyContext target,
            HoleAxis sourceHole, Stopwatch overallWatch)
        {
            row.Cg22ProbeRadiusMm = Cg22ProbeRadiusM * 1000.0;
            row.Cg22ProbeSolidCount = 0;
            row.Cg22ProbeVoidCount = 0;
            row.Cg22ProbeUnknownCount = 0;
            double[] axis = Cg22Unit(sourceHole.Direction);
            if (axis == null || !Cg2ValidVector(sourceHole.Center))
            {
                row.Cg22ProbeStatus = "UNVERIFIED_PROBE_AXIS";
                row.Cg22ProbeUnknownCount = Cg22ProbeCount;
                return;
            }
            double[] seed = Math.Abs(axis[2]) < 0.9
                ? new[] { 0.0, 0.0, 1.0 }
                : new[] { 0.0, 1.0, 0.0 };
            double[] u = Cg22Unit(Cg22Cross(axis, seed));
            double[] v = u == null ? null : Cg22Unit(Cg22Cross(axis, u));
            if (u == null || v == null)
            {
                row.Cg22ProbeStatus = "UNVERIFIED_PROBE_BASIS";
                row.Cg22ProbeUnknownCount = Cg22ProbeCount;
                return;
            }

            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < Cg22ProbeCount; ++i)
            {
                // Diagnostic work has a separate, bounded time budget and
                // must not consume unlimited time inside the original CG2 loop.
                if (watch.ElapsedMilliseconds > Cg22MaxProbeElapsedMs ||
                    (overallWatch != null &&
                     overallWatch.ElapsedMilliseconds >= Cg2MaxBrepElapsedMs))
                {
                    row.Cg22ProbeUnknownCount += Cg22ProbeCount - i;
                    break;
                }
                ThrowIfCanceled();
                double sign = i < 2 ? (i == 0 ? 1.0 : -1.0) :
                    (i == 2 ? 1.0 : -1.0);
                double[] radial = i < 2 ? u : v;
                double[] shifted = new[] {
                    sourceHole.Center[0] + radial[0] * Cg22ProbeRadiusM * sign,
                    sourceHole.Center[1] + radial[1] * Cg22ProbeRadiusM * sign,
                    sourceHole.Center[2] + radial[2] * Cg22ProbeRadiusM * sign
                };
                double[] startWorld, dirWorld;
                double maxDistance;
                if (!Cg2PrepareBoundedRay(shifted, axis, target.WorldBox,
                    out startWorld, out dirWorld, out maxDistance))
                {
                    row.Cg22ProbeUnknownCount++;
                    continue;
                }
                double[] startLocal = TransformPoint(startWorld, target.FromWorldToPart);
                double[] dirLocal = TransformVector(dirWorld, target.FromWorldToPart);
                if (!Cg2ValidVector(startLocal) || Cg22Unit(dirLocal) == null)
                {
                    row.Cg22ProbeUnknownCount++;
                    continue;
                }
                try
                {
                    double materialSpan;
                    int count;
                    string result = Cg2ReadRayEvidence(target.PartModel, target.Bodies,
                        startLocal, dirLocal, maxDistance, out materialSpan, out count);
                    if (string.Equals(result, "BREP_SOLID_ON_AXIS", StringComparison.Ordinal))
                        row.Cg22ProbeSolidCount++;
                    else if (string.Equals(result, "BREP_NO_AXIS_HIT", StringComparison.Ordinal))
                        row.Cg22ProbeVoidCount++;
                    else row.Cg22ProbeUnknownCount++;
                }
                catch (OperationCanceledException) { throw; }
                catch { row.Cg22ProbeUnknownCount++; }
            }

            if (row.Cg22ProbeUnknownCount > 0)
                row.Cg22ProbeStatus = "PARTIAL_UNVERIFIED_DIAGNOSTIC_ONLY";
            else if (row.Cg22ProbeVoidCount > 0)
                row.Cg22ProbeStatus = "LOCAL_VOID_OBSERVED_DIAGNOSTIC_ONLY";
            else
                row.Cg22ProbeStatus = "FOUR_SOLID_RAYS_DIAGNOSTIC_ONLY";
        }

        // CG2.2 DEBUG ONLY.
        // Never changes production NG, CSV or CAD geometry.
        private static bool Cg22DebugFocus(
            CheckHoleCg2ExpectedHoleRow row)
        {
            return row != null &&
                string.Equals(row.SourceOccurrence, "2222-04-TI-b4-ks1-1",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.TargetOccurrence, "2222-04-TI-b4-fr1-1",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void Cg22WriteDiagnosticDebug(
            CheckHoleCg2Report report)
        {
#if DEBUG
            if (report == null)
            {
                Debug.WriteLine(
                    "[CG22][ERROR] Report is NULL");
                return;
            }

            var rows = report.ExpectedHoleCandidates;

            Debug.WriteLine(
                "[CG22] ========== DEBUG BEGIN ==========");

            Debug.WriteLine(
                "[CG22] MVID=" +
                typeof(CheckAssemblyHole)
                .Assembly.ManifestModule.ModuleVersionId);

            Debug.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "[CG22][SUMMARY] Candidates={0} " +
                "RayTested={1} Solid={2} NoHit={3} " +
                "Unknown={4} NotTested={5} Coverage={6}",
                report.CandidateCount,
                report.BrepRayTestedCount,
                report.BrepSolidEvidenceCount,
                report.BrepNoAxisHitCount,
                report.BrepUnknownCount,
                report.BrepNotTestedCount,
                report.CoverageStatus));

            // Count each primary BREP classification.
            foreach (var group in rows
                .GroupBy(r => r.MaterialEvidence ?? "NULL")
                .OrderBy(g => g.Key))
            {
                Debug.WriteLine(
                    "[CG22][BREP_STATUS] " +
                    group.Key + "=" + group.Count());
            }

            // Count geometry extraction outcomes.
            foreach (var group in rows
                .GroupBy(r => r.Cg22GeometryStatus ?? "NULL")
                .OrderBy(g => g.Key))
            {
                Debug.WriteLine(
                    "[CG22][GEOMETRY_STATUS] " +
                    group.Key + "=" + group.Count());
            }

            // Count four-probe outcomes.
            foreach (var group in rows
                .GroupBy(r => r.Cg22ProbeStatus ?? "NULL")
                .OrderBy(g => g.Key))
            {
                Debug.WriteLine(
                    "[CG22][PROBE_STATUS] " +
                    group.Key + "=" + group.Count());
            }

            // Log every candidate with physical BREP evidence.
            foreach (var r in rows
                .Where(x => x.MaterialEvidence ==
                    "BREP_SOLID_ON_AXIS")
                .OrderBy(x => x.Id))
            {
                Debug.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "[CG22][SOLID] ID={0} Hole={1} " +
                    "Source={2} Target={3} " +
                    "SpanMm={4:F3} Hits={5} " +
                    "Geometry={6} Probe={7}",
                    r.Id,
                    r.SourceHoleLabel,
                    r.SourceOccurrence,
                    r.TargetOccurrence,
                    r.MaterialSpanMm,
                    r.MaterialHitCount,
                    r.Cg22GeometryStatus ?? "NULL",
                    r.Cg22ProbeStatus ?? "NULL"));
            }

            // Detailed diagnostic for six known test holes.
            var focus = rows
                .Where(Cg22DebugFocus)
                .OrderBy(r => r.Id)
                .ToList();

            Debug.WriteLine(
                "[CG22][FOCUS_COUNT] " + focus.Count);

            foreach (var r in focus)
            {
                Debug.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "[CG22][FOCUS] ID={0} Hole={1} " +
                    "Source={2} Target={3} " +
                    "Evidence={4} SpanMm={5:F3} Hits={6} " +
                    "TargetAxes={7} RadialMm={8:F3} " +
                    "AngleDeg={9:F3}",
                    r.Id,
                    r.SourceHoleLabel,
                    r.SourceOccurrence,
                    r.TargetOccurrence,
                    r.MaterialEvidence ?? "NULL",
                    r.MaterialSpanMm,
                    r.MaterialHitCount,
                    r.TargetHoleAxisCount,
                    r.NearestTargetAxisRadialMm,
                    r.NearestTargetAxisAngleDeg));

                Debug.WriteLine(
                    "[CG22][WORLD] ID=" + r.Id +
                    " CENTER_MM=" +
                    (r.Cg22SourceCenterWorldMm ?? "NULL") +
                    " AXIS=" +
                    (r.Cg22SourceAxisWorld ?? "NULL") +
                    " ENTRY_MM=" +
                    (r.Cg22HitEntryWorldMm ?? "NULL") +
                    " EXIT_MM=" +
                    (r.Cg22HitExitWorldMm ?? "NULL"));

                Debug.WriteLine(
                    "[CG22][NORMAL] ID=" + r.Id +
                    " NORMAL=" +
                    (r.Cg22EntryNormalWorld ?? "NULL") +
                    " DOT=" +
                    (r.Cg22AbsoluteNormalDotAxis.HasValue
                        ? r.Cg22AbsoluteNormalDotAxis.Value
                            .ToString("F6",
                                CultureInfo.InvariantCulture)
                        : "NOT_AVAILABLE") +
                    " INCIDENCE_DEG=" +
                    (r.Cg22IncidenceDeg.HasValue
                        ? r.Cg22IncidenceDeg.Value
                            .ToString("F3",
                                CultureInfo.InvariantCulture)
                        : "NOT_AVAILABLE"));

                Debug.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "[CG22][PROBES] ID={0} RadiusMm={1:F3} " +
                    "Solid={2} Void={3} Unknown={4} " +
                    "Status={5}",
                    r.Id,
                    r.Cg22ProbeRadiusMm,
                    r.Cg22ProbeSolidCount,
                    r.Cg22ProbeVoidCount,
                    r.Cg22ProbeUnknownCount,
                    r.Cg22ProbeStatus ?? "NULL"));

                Debug.WriteLine(
                    "[CG22][RESULT] ID=" + r.Id +
                    " GeometryStatus=" +
                    (r.Cg22GeometryStatus ?? "NULL") +
                    " MissingHoleVerdict=NOT_VERIFIED");
            }

            if (focus.Count == 0)
            {
                Debug.WriteLine(
                    "[CG22][WARNING] Focus candidates not found. " +
                    "Check source/target occurrence and hole labels.");
            }

            Debug.WriteLine(
                "[CG22] ========== DEBUG END ==========");
#endif
        }
    }
}
