// CHECK HOLE CG3.1 - EVIDENCE FUSION / REVIEW PRIORITY ONLY.
// C# 7.3 / .NET Framework 4.8 / SOLIDWORKS 2024.
// No new COM calls. No inferred fastening intent. NO production Missing Hole NG.
// No effects on Stack, Pattern, marker 3D sketch, CSV, viewport preview or model.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
        private sealed class Cg31Facts
        {
            public string Material;
            public string Geometry;
            public string Probes;
            public string Counterpart;
            public string Topology;
            public int InnerCircle;
            public int InnerComplex;
            public int Outer;
        }

        private sealed class Cg31Assessment
        {
            public int Priority;
            public string State;
            public string Reason;
        }

        // Pure evidence classification; priority is for the HUMAN REVIEW QUEUE only.
        // Important: absence of a detected opening != proof that a hole is required.
        private static Cg31Assessment Cg31Assess(Cg31Facts f)
        {
            if (f == null || f.Material != "BREP_SOLID_ON_AXIS")
                return Cg31Result(0, "UNVERIFIED_BREP",
                    "No confirmed solid-axis BREP evidence. Never infer missing hole from AABB.");

            if (f.Geometry != "VALID_ENTRY_EXIT_DIAGNOSTIC_ONLY")
                return Cg31Result(0, "UNVERIFIED_ENTRY_EXIT",
                    "Solid material reported, but entry/exit geometry is not verified.");

            if (f.Probes != "FOUR_SOLID_RAYS_DIAGNOSTIC_ONLY")
                return Cg31Result(0, "UNVERIFIED_OFFSET_PROBES",
                    "Four surrounding rays did not all confirm solid; could be a void or partial geometry.");

            if (string.IsNullOrEmpty(f.Counterpart) ||
                f.Counterpart.StartsWith("UNVERIFIED", StringComparison.Ordinal))
                return Cg31Result(0, "UNVERIFIED_COUNTERPART",
                    "Local target-axis analysis incomplete.");

            if (string.IsNullOrEmpty(f.Topology) ||
                f.Topology.StartsWith("UNVERIFIED", StringComparison.Ordinal) ||
                f.Topology == "NOT_APPLICABLE_BREP_NOT_SOLID")
                return Cg31Result(0, "UNVERIFIED_TOPOLOGY",
                    "Topology scan incomplete/unavailable; no negative inference permitted.");

            if (f.Counterpart == "COAXIAL_TARGET_AXIS_BREP_SOLID_CONFLICT")
                return Cg31Result(2, "COAXIAL_SOLID_CONFLICT_REVIEW",
                    "Target axis appears aligned but BREP reports solid; investigate conflicting evidence.");

            if (f.Counterpart == "POSSIBLE_OFFSET_TARGET_AXIS")
                return Cg31Result(2, "TARGET_AXIS_ALIGNMENT_REVIEW",
                    "Potential offset/ambiguous target axis; determine actual geometry and fit.");

            if (f.Counterpart != "SOLID_NO_DETECTED_LOCAL_TARGET_AXIS")
                return Cg31Result(0, "UNVERIFIED_COUNTERPART_STATE",
                    "Unknown counterpart state; no automatic verdict.");

            if (f.InnerCircle > 0 || f.InnerComplex > 0 ||
                f.Topology == "NEAR_CIRCULAR_INNER_LOOP_REVIEW" ||
                f.Topology == "NEAR_COMPLEX_INNER_LOOP_REVIEW" ||
                f.Topology == "NEAR_MIXED_INNER_LOOPS_REVIEW")
                return Cg31Result(2, "INNER_BOUNDARY_REVIEW",
                    "Nearby inner boundary exists; BREP solid may reflect a partial cut or other geometry.");

            if (f.Topology == "NEAR_OUTER_EDGE_ONLY_REVIEW" || f.Outer > 0)
                return Cg31Result(2, "OUTER_EDGE_REVIEW",
                    "Nearby outer contour; boundary is not equivalent to a counterpart hole.");

            if (f.Topology == "NO_NEAR_LOOP_DETECTED_NOT_PROOF_OF_MISSING")
                return Cg31Result(1, "SOLID_NO_OPENING_DETECTED_REVIEW",
                    "Entry/exit and four probes found material; no recognized local target axis or near inner loop. " +
                    "HIGH REVIEW PRIORITY only: actual fastening intent / hole requirement is UNKNOWN.");

            return Cg31Result(0, "UNVERIFIED_NEW_TOPOLOGY_STATE",
                "Unrecognized topology state; fails closed.");
        }

        private static Cg31Assessment Cg31Result(int priority, string state, string reason)
        {
            return new Cg31Assessment { Priority = priority, State = state, Reason = reason };
        }

        // Only reads CG2.2-CG2.4 row evidence (CG2.5 positive controls unchanged).
        private void Cg31AnalyzeReviewCandidates(CheckHoleCg2Report report)
        {
            if (report == null || report.ExpectedHoleCandidates == null) return;
            foreach (CheckHoleCg2ExpectedHoleRow row in report.ExpectedHoleCandidates)
            {
                if (row == null) continue;
                Cg31Assessment a;
                try
                {
                    a = Cg31Assess(new Cg31Facts
                    {
                        Material = row.MaterialEvidence,
                        Geometry = row.Cg22GeometryStatus,
                        Probes = row.Cg22ProbeStatus,
                        Counterpart = row.Cg23State,
                        Topology = row.Cg24State,
                        InnerCircle = row.Cg24NearbyCircularInnerLoops,
                        InnerComplex = row.Cg24NearbyComplexInnerLoops,
                        Outer = row.Cg24NearbyOuterLoops
                    });
                }
                catch (Exception ex)
                {
                    a = Cg31Result(0, "UNVERIFIED_FUSION_EXCEPTION", ex.GetType().Name);
                }
                row.Cg31ReviewPriority = a.Priority;
                row.Cg31State = a.State;
                row.Cg31Reason = a.Reason;
                row.Cg31Verdict = "REVIEW_ONLY_NOT_MISSING_HOLE_NG";
            }
            Cg31DebugReport(report);
            Cg31RunPureTestsOnce();
        }

        [Conditional("DEBUG")]
        private static void Cg31DebugReport(CheckHoleCg2Report report)
        {
            var rows = report.ExpectedHoleCandidates.Where(x => x != null).ToList();
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG31][SUMMARY] Candidates={0} P1={1} P2={2} Unverified={3} ProductionNgChanged=NO",
                rows.Count, rows.Count(x => x.Cg31ReviewPriority == 1),
                rows.Count(x => x.Cg31ReviewPriority == 2),
                rows.Count(x => x.Cg31ReviewPriority == 0)));
            foreach (var group in rows.GroupBy(x => x.Cg31State ?? "NULL").OrderBy(g => g.Key))
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG31][STATE] {0}={1}", group.Key, group.Count()));

            // The Id/Hole# are current-run diagnostics, NOT stable cross-run identifiers.
            foreach (var pair in rows.Where(x => x.Cg31ReviewPriority > 0)
                .GroupBy(x => new { x.SourceOccurrence, x.TargetOccurrence })
                .OrderByDescending(g => g.Count(x => x.Cg31ReviewPriority == 1))
                .ThenBy(g => g.Key.SourceOccurrence, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key.TargetOccurrence, StringComparer.OrdinalIgnoreCase))
            {
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG31][PAIR] Source={0} Target={1} P1={2} P2={3} CandidateIds={4}",
                    pair.Key.SourceOccurrence, pair.Key.TargetOccurrence,
                    pair.Count(x => x.Cg31ReviewPriority == 1),
                    pair.Count(x => x.Cg31ReviewPriority == 2),
                    string.Join(",", pair.Select(x => x.Id.ToString(CultureInfo.InvariantCulture)).ToArray())));
            }
            foreach (CheckHoleCg2ExpectedHoleRow row in rows.Where(x => x.Cg31ReviewPriority == 1))
            {
                string p = "NO_SLAB_POINTS";
                if (row.Cg23EntryWorldM != null && row.Cg23ExitWorldM != null &&
                    row.Cg23EntryWorldM.Length >= 3 && row.Cg23ExitWorldM.Length >= 3)
                {
                    p = string.Format(CultureInfo.InvariantCulture,
                        "EntryMm=({0:F3},{1:F3},{2:F3}) ExitMm=({3:F3},{4:F3},{5:F3})",
                        1000 * row.Cg23EntryWorldM[0], 1000 * row.Cg23EntryWorldM[1],
                        1000 * row.Cg23EntryWorldM[2], 1000 * row.Cg23ExitWorldM[0],
                        1000 * row.Cg23ExitWorldM[1], 1000 * row.Cg23ExitWorldM[2]);
                }
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG31][P1_ROW] Id={0} Source={1} Target={2} Hole={3} {4} State={5}",
                    row.Id, row.SourceOccurrence, row.TargetOccurrence,
                    row.SourceHoleLabel, p, row.Cg31State));
            }
        }

#if DEBUG
        private static bool cg31TestsDone;
        private static void Cg31RunPureTestsOnce()
        {
            if (cg31TestsDone) return;
            cg31TestsDone = true;
            var baseFact = new Cg31Facts
            {
                Material = "BREP_SOLID_ON_AXIS",
                Geometry = "VALID_ENTRY_EXIT_DIAGNOSTIC_ONLY",
                Probes = "FOUR_SOLID_RAYS_DIAGNOSTIC_ONLY",
                Counterpart = "SOLID_NO_DETECTED_LOCAL_TARGET_AXIS",
                Topology = "NO_NEAR_LOOP_DETECTED_NOT_PROOF_OF_MISSING"
            };
            int passed = 0;
            int total = 0;
            Cg31CheckTest("SOLID_NO_BOUNDARY", baseFact, "SOLID_NO_OPENING_DETECTED_REVIEW", 1, ref passed, ref total);
            Cg31CheckTest("AABB_ONLY", new Cg31Facts { Material = "UNVERIFIED_RAY_RANGE" }, "UNVERIFIED_BREP", 0, ref passed, ref total);
            baseFact.Geometry = "UNVERIFIED_PRIMARY_NO_SNAPSHOT";
            Cg31CheckTest("BAD_ENTRY_EXIT", baseFact, "UNVERIFIED_ENTRY_EXIT", 0, ref passed, ref total);
            baseFact.Geometry = "VALID_ENTRY_EXIT_DIAGNOSTIC_ONLY";
            baseFact.Probes = "LOCAL_VOID_OBSERVED_DIAGNOSTIC_ONLY";
            Cg31CheckTest("PROBE_VOID", baseFact, "UNVERIFIED_OFFSET_PROBES", 0, ref passed, ref total);
            baseFact.Probes = "FOUR_SOLID_RAYS_DIAGNOSTIC_ONLY";
            baseFact.Topology = "UNVERIFIED_TOPOLOGY_PARTIAL";
            Cg31CheckTest("PARTIAL_TOPOLOGY", baseFact, "UNVERIFIED_TOPOLOGY", 0, ref passed, ref total);
            baseFact.Topology = "NEAR_CIRCULAR_INNER_LOOP_REVIEW";
            baseFact.InnerCircle = 2;
            Cg31CheckTest("INNER_CIRCLE", baseFact, "INNER_BOUNDARY_REVIEW", 2, ref passed, ref total);
            baseFact.Topology = "NEAR_OUTER_EDGE_ONLY_REVIEW";
            baseFact.InnerCircle = 0;
            baseFact.Outer = 4;
            Cg31CheckTest("OUTER_ONLY", baseFact, "OUTER_EDGE_REVIEW", 2, ref passed, ref total);
            baseFact.Topology = "NO_NEAR_LOOP_DETECTED_NOT_PROOF_OF_MISSING";
            baseFact.Outer = 0;
            baseFact.Counterpart = "POSSIBLE_OFFSET_TARGET_AXIS";
            Cg31CheckTest("OFFSET", baseFact, "TARGET_AXIS_ALIGNMENT_REVIEW", 2, ref passed, ref total);
            baseFact.Counterpart = "COAXIAL_TARGET_AXIS_BREP_SOLID_CONFLICT";
            Cg31CheckTest("COAXIAL_CONFLICT", baseFact, "COAXIAL_SOLID_CONFLICT_REVIEW", 2, ref passed, ref total);
            baseFact.Counterpart = "UNVERIFIED_SOURCE_INDEX";
            Cg31CheckTest("SOURCE_UNVERIFIED", baseFact, "UNVERIFIED_COUNTERPART", 0, ref passed, ref total);
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG31][TEST_SUMMARY] Passed={0}/{1}", passed, total));
        }

        private static void Cg31CheckTest(string name, Cg31Facts f, string expected, int priority,
            ref int passed, ref int total)
        {
            ++total;
            Cg31Assessment got = Cg31Assess(f);
            bool ok = got.State == expected && got.Priority == priority;
            if (ok) ++passed;
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG31][TEST] {0}={1} Got={2} Priority={3}",
                name, ok ? "PASS" : "FAIL", got.State, got.Priority));
        }
#else
        private static void Cg31RunPureTestsOnce() { }
#endif
    }
}
