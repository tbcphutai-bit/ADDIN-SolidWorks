// CHECK HOLE CG2.3 — LOCAL COUNTERPART EVIDENCE ONLY
// C# 7.3 / .NET Framework 4.8; additive diagnostic partial class.
// NO production stack/NG/Pattern/CSV/marker mutations, no new SOLIDWORKS COM calls.
// Distances are in metres internally. Results are in millimetres for display/log.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
        // These are SEARCH WINDOWS for diagnostics only, NOT production NG thresholds.
        private const double Cg23LocalAxialWindowM = 0.005; // within 5 mm of BREP segment
        private const double Cg23LocalRadialWindowM = 0.010; // within 10 mm of BREP axis
        private const double Cg23LocalAngleWindowDeg = 5.0;
        private const double Cg23AmbiguousRadialGapM = 0.0005; // 0.5 mm
        private const double Cg23AxisConsistencyM = 0.0001; // 0.1 mm diagnostic check

        private sealed class Cg23AxisSample
        {
            public double[] CenterM;
            public double[] Direction;
        }

        private sealed class Cg23AxisMatch
        {
            public double RadialM;
            public double AxialGapM;
            public double AngleDeg;
        }

        private sealed class Cg23Assessment
        {
            public string State;
            public string Reason;
            public int LocalAxisCount;
            public double RadialMm = -1;
            public double AxialGapMm = -1;
            public double AngleDeg = -1;
        }

        // Pure geometry; deliberately avoids GetModelDoc2, mates, or further ray casting.
        // Presence of metal along source axis conflicts with an apparent coaxial
        // target axis, so that case is NOT labelled a matched through-hole.
        private static Cg23Assessment Cg23AssessGeometry(
            double[] sourceCenterM, double[] sourceDirection,
            double[] entryWorldM, double[] exitWorldM,
            IList<Cg23AxisSample> targetAxes)
        {
            var result = new Cg23Assessment
            {
                State = "UNVERIFIED_GEOMETRY",
                Reason = "Invalid source axis or BREP entry/exit."
            };
            double[] axis = Cg23Unit(sourceDirection);
            if (!Cg23Finite3(sourceCenterM) || axis == null ||
                !Cg23Finite3(entryWorldM) || !Cg23Finite3(exitWorldM))
                return result;
            double[] slab = Cg23Sub(exitWorldM, entryWorldM);
            double slabLength = Math.Abs(Cg23Dot(slab, axis));
            if (slabLength < 1e-7 || Cg23Norm(slab) < 1e-7)
            {
                result.Reason = "Zero or degenerate BREP slab.";
                return result;
            }

            // Entry/Exit must agree with the SOURCE axis, not merely be close in AABB.
            if (Cg23PerpendicularDistanceM(entryWorldM, sourceCenterM, axis) > Cg23AxisConsistencyM ||
                Cg23PerpendicularDistanceM(exitWorldM, sourceCenterM, axis) > Cg23AxisConsistencyM)
            {
                result.Reason = "BREP points not on source-hole axis within 0.1 mm.";
                return result;
            }

            double[] mid = Cg23Scale(Cg23Add(entryWorldM, exitWorldM), 0.5);
            var nearby = new List<Cg23AxisMatch>();
            foreach (Cg23AxisSample candidate in targetAxes ?? new Cg23AxisSample[0])
            {
                if (candidate == null || !Cg23Finite3(candidate.CenterM)) continue;
                double[] other = Cg23Unit(candidate.Direction);
                if (other == null) continue;
                double cosine = Math.Min(1.0, Math.Max(0.0,
                    Math.Abs(Cg23Dot(axis, other))));
                double angle = Math.Acos(cosine) * 180.0 / Math.PI;
                // Perpendicular or oblique holes are not counterparts of a round
                // through-hole along this source axis.
                if (angle > Cg23LocalAngleWindowDeg) continue;
                double[] d = Cg23Sub(candidate.CenterM, mid);
                double axial = Cg23Dot(d, axis);
                double axialGap = Math.Max(0.0, Math.Abs(axial) - 0.5 * slabLength);
                double radial = Cg23Norm(Cg23Sub(d, Cg23Scale(axis, axial)));
                if (axialGap > Cg23LocalAxialWindowM ||
                    radial > Cg23LocalRadialWindowM) continue;
                nearby.Add(new Cg23AxisMatch
                {
                    RadialM = radial,
                    AxialGapM = axialGap,
                    AngleDeg = angle
                });
            }

            result.LocalAxisCount = nearby.Count;
            if (nearby.Count == 0)
            {
                result.State = "SOLID_NO_DETECTED_LOCAL_TARGET_AXIS";
                result.Reason = "BREP solid; no recognized parallel target axis near slab. " +
                    "Slot/cut/geometry detection and fastening intent are NOT verified.";
                return result;
            }

            // Closest radial, then axial, then angle: deterministic and stable.
            var ordered = nearby.OrderBy(x => x.RadialM)
                .ThenBy(x => x.AxialGapM)
                .ThenBy(x => x.AngleDeg).ToArray();
            Cg23AxisMatch best = ordered[0];
            result.RadialMm = best.RadialM * 1000.0;
            result.AxialGapMm = best.AxialGapM * 1000.0;
            result.AngleDeg = best.AngleDeg;
            if (ordered.Length > 1 &&
                ordered[1].RadialM - best.RadialM <= Cg23AmbiguousRadialGapM)
            {
                result.State = "UNVERIFIED_MULTIPLE_LOCAL_TARGET_AXES";
                result.Reason = "Several parallel target axes equally close to source axis.";
                return result;
            }
            if (best.RadialM <= PositionToleranceM &&
                best.AngleDeg <= AngleToleranceDeg)
            {
                result.State = "COAXIAL_TARGET_AXIS_BREP_SOLID_CONFLICT";
                result.Reason = "Detected target axis is aligned, but primary BREP ray found solid; " +
                    "do not claim matched through-hole without resolving geometry.";
                return result;
            }
            result.State = "POSSIBLE_OFFSET_TARGET_AXIS";
            result.Reason = "Near parallel target-hole axis is outside production OK tolerance; " +
                "not a confirmed corresponding hole or a production NG.";
            return result;
        }

        // Called only after CG2 BREP evidence is populated. The hole list is
        // unchanged and remains ordered as it was when CG2 candidate labels were set.
        private void Cg23AnalyzeCounterparts(
            List<HoleAxis> holes, CheckHoleCg2Report report)
        {
            if (holes == null || report == null || report.ExpectedHoleCandidates == null)
                return;
            var targetIndex = holes.Where(h => h != null &&
                    !string.IsNullOrWhiteSpace(h.ComponentOccurrence))
                .GroupBy(h => h.ComponentOccurrence, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key,
                    g => g.Where(h => Cg23Finite3(h.Center) && Cg23Unit(h.Direction) != null)
                        .Select(h => new Cg23AxisSample
                        {
                            CenterM = (double[])h.Center.Clone(),
                            Direction = (double[])h.Direction.Clone()
                        }).ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            int processed = 0;
            foreach (CheckHoleCg2ExpectedHoleRow row in report.ExpectedHoleCandidates)
            {
                if (row == null) continue;
                row.Cg23State = "UNVERIFIED_BREP_NOT_SOLID";
                row.Cg23Reason = "Primary BREP is not a confirmed solid-axis hit.";
                row.Cg23LocalTargetAxisCount = 0;
                row.Cg23NearestLocalRadialMm = -1;
                row.Cg23NearestLocalAxialGapMm = -1;
                row.Cg23NearestLocalAngleDeg = -1;
                if (row.MaterialEvidence != "BREP_SOLID_ON_AXIS") continue;
                ++processed;
                try
                {
                    int sourceIndex;
                    if (!Cg2TryGetSourceIndex(row.SourceHoleLabel, holes.Count, out sourceIndex) ||
                        holes[sourceIndex] == null ||
                        !string.Equals(holes[sourceIndex].ComponentOccurrence,
                            row.SourceOccurrence, StringComparison.OrdinalIgnoreCase))
                    {
                        row.Cg23State = "UNVERIFIED_SOURCE_INDEX";
                        row.Cg23Reason = "Hole label/occurrence mismatch within current run.";
                        continue;
                    }
                    Cg23AxisSample[] targetAxes;
                    if (!targetIndex.TryGetValue(row.TargetOccurrence ?? "", out targetAxes))
                        targetAxes = new Cg23AxisSample[0];
                    Cg23Assessment assessment = Cg23AssessGeometry(
                        holes[sourceIndex].Center,
                        holes[sourceIndex].Direction,
                        row.Cg23EntryWorldM,
                        row.Cg23ExitWorldM,
                        targetAxes);
                    row.Cg23State = assessment.State;
                    row.Cg23Reason = assessment.Reason;
                    row.Cg23LocalTargetAxisCount = assessment.LocalAxisCount;
                    row.Cg23NearestLocalRadialMm = assessment.RadialMm;
                    row.Cg23NearestLocalAxialGapMm = assessment.AxialGapMm;
                    row.Cg23NearestLocalAngleDeg = assessment.AngleDeg;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    row.Cg23State = "UNVERIFIED_ANALYSIS_ERROR";
                    row.Cg23Reason = ex.GetType().Name;
                }
            }
            // No new production verdict, CSV row, mate requirement, or marker.
            Cg23DebugReport(report, processed);
        }

        [Conditional("DEBUG")]
        private static void Cg23DebugReport(CheckHoleCg2Report report, int processed)
        {
            Debug.WriteLine("[CG23] ===== DIAGNOSTICS BEGIN =====");
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG23][SUMMARY] Candidates={0} PrimarySolid={1} ReviewedSolid={2} " +
                "ProductionNgChanged=NO",
                report.ExpectedHoleCandidates.Count, report.BrepSolidEvidenceCount, processed));
            foreach (var group in report.ExpectedHoleCandidates
                .GroupBy(r => r.Cg23State ?? "NULL").OrderBy(g => g.Key))
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG23][STATE] {0}={1}", group.Key, group.Count()));
            foreach (var row in report.ExpectedHoleCandidates.Where(r =>
                r.MaterialEvidence == "BREP_SOLID_ON_AXIS").OrderBy(r => r.Id))
            {
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG23][ROW] Id={0} Hole={1} Source={2} Target={3} " +
                    "State={4} LocalAxes={5} RadialMm={6:F3} " +
                    "AxialGapMm={7:F3} AngleDeg={8:F3} Reason={9}",
                    row.Id, row.SourceHoleLabel, row.SourceOccurrence,
                    row.TargetOccurrence, row.Cg23State,
                    row.Cg23LocalTargetAxisCount,
                    row.Cg23NearestLocalRadialMm,
                    row.Cg23NearestLocalAxialGapMm,
                    row.Cg23NearestLocalAngleDeg,
                    row.Cg23Reason));
            }
            Cg23RunPureTestsOnce();
            Debug.WriteLine("[CG23] ===== DIAGNOSTICS END =====");
        }

        private static bool Cg23Finite3(double[] v)
        {
            return v != null && v.Length >= 3 &&
                v.Take(3).All(x => !double.IsNaN(x) && !double.IsInfinity(x));
        }
        private static double Cg23Dot(double[] a, double[] b)
        {
            return a[0]*b[0] + a[1]*b[1] + a[2]*b[2];
        }
        private static double Cg23Norm(double[] v)
        {
            return Math.Sqrt(Cg23Dot(v, v));
        }
        private static double[] Cg23Unit(double[] v)
        {
            if (!Cg23Finite3(v)) return null;
            double size = Cg23Norm(v);
            return size > 1e-12 ? new[] { v[0]/size, v[1]/size, v[2]/size } : null;
        }
        private static double[] Cg23Sub(double[] a, double[] b)
        {
            return new[] { a[0]-b[0], a[1]-b[1], a[2]-b[2] };
        }
        private static double[] Cg23Add(double[] a, double[] b)
        {
            return new[] { a[0]+b[0], a[1]+b[1], a[2]+b[2] };
        }
        private static double[] Cg23Scale(double[] a, double c)
        {
            return new[] { a[0]*c, a[1]*c, a[2]*c };
        }
        private static double Cg23PerpendicularDistanceM(double[] point, double[] origin, double[] unitAxis)
        {
            double[] d = Cg23Sub(point, origin);
            double t = Cg23Dot(d, unitAxis);
            return Cg23Norm(Cg23Sub(d, Cg23Scale(unitAxis, t)));
        }

#if DEBUG
        private static bool _cg23TestsExecuted;
        private static void Cg23RunPureTestsOnce()
        {
            if (_cg23TestsExecuted) return;
            _cg23TestsExecuted = true;
            double[] src = { 0.0, 0.0, 0.0 }, z = { 0.0, 0.0, 1.0 };
            double[] entry = { 0.0, 0.0, 0.009 }, exit = { 0.0, 0.0, 0.011 };
            int passed = 0, total = 0;
            Cg23CheckTest("NO_AXIS", "SOLID_NO_DETECTED_LOCAL_TARGET_AXIS", src, z,
                entry, exit, new Cg23AxisSample[0], ref passed, ref total);
            Cg23CheckTest("COAXIAL_CONFLICT", "COAXIAL_TARGET_AXIS_BREP_SOLID_CONFLICT",
                src, z, entry, exit, new[] { Cg23Sample(0, 0, .010, z) }, ref passed, ref total);
            Cg23CheckTest("OFFSET_AXIS", "POSSIBLE_OFFSET_TARGET_AXIS",
                src, z, entry, exit, new[] { Cg23Sample(.001, 0, .010, z) }, ref passed, ref total);
            Cg23CheckTest("PERPENDICULAR", "SOLID_NO_DETECTED_LOCAL_TARGET_AXIS",
                src, z, entry, exit,
                new[] { Cg23Sample(.001, 0, .010, new[] { 1.0, 0.0, 0.0 }) },
                ref passed, ref total);
            Cg23CheckTest("FAR_AXIAL", "SOLID_NO_DETECTED_LOCAL_TARGET_AXIS",
                src, z, entry, exit, new[] { Cg23Sample(.001, 0, .040, z) },
                ref passed, ref total);
            Cg23CheckTest("AMBIGUOUS", "UNVERIFIED_MULTIPLE_LOCAL_TARGET_AXES",
                src, z, entry, exit,
                new[] { Cg23Sample(.001, 0, .010, z), Cg23Sample(.0011, 0, .010, z) },
                ref passed, ref total);
            Cg23CheckTest("INVALID_BREP", "UNVERIFIED_GEOMETRY",
                src, z, null, exit, new Cg23AxisSample[0], ref passed, ref total);
            Cg23CheckTest("OFF_AXIS_ENTRY", "UNVERIFIED_GEOMETRY",
                src, z, new[] { 0.001, 0.0, 0.009 }, exit,
                new Cg23AxisSample[0], ref passed, ref total);
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG23][TEST_SUMMARY] Passed={0}/{1}", passed, total));
        }
        private static Cg23AxisSample Cg23Sample(double x, double y, double z, double[] dir)
        {
            return new Cg23AxisSample { CenterM = new[] { x, y, z }, Direction = dir };
        }
        private static void Cg23CheckTest(string name, string expected,
            double[] src, double[] dir, double[] entry, double[] exit,
            IList<Cg23AxisSample> targets, ref int passed, ref int total)
        {
            ++total;
            Cg23Assessment actual = Cg23AssessGeometry(src, dir, entry, exit, targets);
            bool ok = actual.State == expected;
            if (ok) ++passed;
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG23][TEST] {0}={1} Got={2} Expected={3}",
                name, ok ? "PASS" : "FAIL", actual.State, expected));
        }
#else
        private static void Cg23RunPureTestsOnce() { }
#endif
    }
}
