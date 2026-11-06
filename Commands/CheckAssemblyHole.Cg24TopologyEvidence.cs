// CHECK HOLE CG2.4 - BREP LOOP/EDGE SCREENING (DIAGNOSTIC ONLY)
// C# 7.3 / .NET Framework 4.8 / SOLIDWORKS 2024.
// No production NG, no editing geometry, no CSV/marker/Stack/Pattern changes.
// An inner loop is possible opening/cut-boundary evidence, NOT proof of a through-hole.
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
        // Windows and budgets are diagnostics, NOT production tolerances.
        private const double Cg24RadialWindowM = 0.015; // 15 mm around tested axis
        private const double Cg24AxialWindowM = 0.005; // 5 mm beyond slab faces
        private const int Cg24MaxSolidCandidates = 60;
        private const int Cg24MaxFacesPerCandidate = 500;
        private const int Cg24MaxEdgeSamplesPerCandidate = 1800;
        private const int Cg24TotalTimeBudgetMs = 12000;

        private sealed class Cg24LoopSample
        {
            public bool Inner;
            public bool SingleCircularEdge;
            public double RadialM;
            public double AxialGapM;
        }

        private sealed class Cg24ScanResult
        {
            public string State = "UNVERIFIED_NOT_RUN";
            public string Reason = "";
            public int Faces;
            public int EdgeSamples;
            public int InnerCircle;
            public int InnerComplex;
            public int Outer;
            public double NearestRadialMm = -1;
            public double NearestAxialGapMm = -1;
        }

        private static string Cg24ClassifyEvidence(
            int innerCircle, int innerComplex, int outer, bool complete)
        {
            if (!complete) return "UNVERIFIED_TOPOLOGY_PARTIAL";
            if (innerCircle > 0 && innerComplex > 0)
                return "NEAR_MIXED_INNER_LOOPS_REVIEW";
            if (innerCircle > 0) return "NEAR_CIRCULAR_INNER_LOOP_REVIEW";
            if (innerComplex > 0) return "NEAR_COMPLEX_INNER_LOOP_REVIEW";
            if (outer > 0) return "NEAR_OUTER_EDGE_ONLY_REVIEW";
            return "NO_NEAR_LOOP_DETECTED_NOT_PROOF_OF_MISSING";
        }

        // Called AFTER CG2.2 + CG2.3. This only updates CG2.4 diagnostic fields.
        private void Cg24AnalyzeTopology(
            AssemblyDoc assembly, List<HoleAxis> holes, CheckHoleCg2Report report)
        {
            if (assembly == null || holes == null || report == null) return;
            Stopwatch timer = Stopwatch.StartNew();
            var targetCounts = holes.Where(h => h != null &&
                    !string.IsNullOrWhiteSpace(h.ComponentOccurrence))
                .GroupBy(h => h.ComponentOccurrence, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var components = new Dictionary<string, Component2>(StringComparer.OrdinalIgnoreCase);
            foreach (object obj in ToObjectArray(assembly.GetComponents(false)))
            {
                Component2 component = obj as Component2;
                if (!IsUsableComponent(component)) continue;
                bool isSubassembly;
                if (!Cg2IsResolvedPartOccurrence(component, out isSubassembly)) continue;
                string key = SafeComponentName(component);
                if (!string.IsNullOrWhiteSpace(key) && !components.ContainsKey(key))
                    components.Add(key, component);
            }
            var contexts = new Dictionary<string, Cg2TargetBodyContext>(StringComparer.OrdinalIgnoreCase);
            int reviewed = 0;
            foreach (CheckHoleCg2ExpectedHoleRow row in report.ExpectedHoleCandidates)
            {
                ThrowIfCanceled();
                if (row == null) continue;
                row.Cg24State = "NOT_APPLICABLE_BREP_NOT_SOLID";
                row.Cg24Reason = "BREP primary result is not a confirmed solid hit.";
                row.Cg24TargetDetectedAxesTotal = targetCounts.ContainsKey(row.TargetOccurrence ?? "")
                    ? targetCounts[row.TargetOccurrence] : 0;
                row.Cg24NearbyCircularInnerLoops = 0;
                row.Cg24NearbyComplexInnerLoops = 0;
                row.Cg24NearbyOuterLoops = 0;
                row.Cg24NearestBoundaryRadialMm = -1;
                row.Cg24NearestBoundaryAxialGapMm = -1;
                row.Cg24FacesVisited = 0;
                row.Cg24EdgeSamples = 0;
                if (row.MaterialEvidence != "BREP_SOLID_ON_AXIS") continue;

                if (reviewed >= Cg24MaxSolidCandidates ||
                    timer.ElapsedMilliseconds >= Cg24TotalTimeBudgetMs)
                {
                    row.Cg24State = "UNVERIFIED_SCAN_BUDGET";
                    row.Cg24Reason = "CG2.4 overall diagnostic budget reached.";
                    continue;
                }
                ++reviewed;
                try
                {
                    if (!Cg23Finite3(row.Cg23EntryWorldM) ||
                        !Cg23Finite3(row.Cg23ExitWorldM))
                    {
                        row.Cg24State = "UNVERIFIED_ENTRY_EXIT";
                        row.Cg24Reason = "CG2.2 world slab points unavailable.";
                        continue;
                    }
                    Component2 component;
                    if (!components.TryGetValue(row.TargetOccurrence ?? "", out component))
                    {
                        row.Cg24State = "UNVERIFIED_TARGET";
                        row.Cg24Reason = "Target component occurrence unavailable.";
                        continue;
                    }
                    Cg2TargetBodyContext context;
                    if (!contexts.TryGetValue(row.TargetOccurrence, out context))
                    {
                        context = Cg2LoadTargetBodyContext(component);
                        contexts.Add(row.TargetOccurrence, context);
                    }
                    if (context == null || !string.IsNullOrEmpty(context.Failure) ||
                        context.Bodies == null || context.FromWorldToPart == null)
                    {
                        row.Cg24State = "UNVERIFIED_TARGET_BODY";
                        row.Cg24Reason = context == null ? "Context missing" :
                            (context.Failure ?? "Target transform/body missing");
                        continue;
                    }
                    double[] entry = TransformPoint(row.Cg23EntryWorldM, context.FromWorldToPart);
                    double[] exit = TransformPoint(row.Cg23ExitWorldM, context.FromWorldToPart);
                    if (!Cg23Finite3(entry) || !Cg23Finite3(exit) ||
                        Cg23Norm(Cg23Sub(exit, entry)) < 1e-7)
                    {
                        row.Cg24State = "UNVERIFIED_LOCAL_TRANSFORM";
                        row.Cg24Reason = "Invalid/degenerate part-local slab.";
                        continue;
                    }
                    Cg24ScanResult result = Cg24ScanBodyLoops(context.Bodies, entry, exit, timer);
                    row.Cg24State = result.State;
                    row.Cg24Reason = result.Reason;
                    row.Cg24NearbyCircularInnerLoops = result.InnerCircle;
                    row.Cg24NearbyComplexInnerLoops = result.InnerComplex;
                    row.Cg24NearbyOuterLoops = result.Outer;
                    row.Cg24NearestBoundaryRadialMm = result.NearestRadialMm;
                    row.Cg24NearestBoundaryAxialGapMm = result.NearestAxialGapMm;
                    row.Cg24FacesVisited = result.Faces;
                    row.Cg24EdgeSamples = result.EdgeSamples;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // This is diagnostic only; errors must not modify production verdicts.
                    row.Cg24State = "UNVERIFIED_TOPOLOGY_API_ERROR";
                    row.Cg24Reason = ex.GetType().Name + ": " + ex.Message;
                }
            }
            Cg24WriteDebug(report, reviewed, timer.ElapsedMilliseconds);
        }

        private Cg24ScanResult Cg24ScanBodyLoops(
            Body2[] bodies, double[] entry, double[] exit, Stopwatch runTimer)
        {
            var result = new Cg24ScanResult();
            double[] mid = Cg23Scale(Cg23Add(entry, exit), 0.5);
            double[] axis = Cg23Unit(Cg23Sub(exit, entry));
            if (axis == null)
            {
                result.State = "UNVERIFIED_LOCAL_AXIS";
                return result;
            }
            double halfSlab = 0.5 * Cg23Norm(Cg23Sub(exit, entry));
            bool complete = true;
            foreach (Body2 body in bodies)
            {
                if (body == null) continue;
                foreach (object faceObject in ToObjectArray(body.GetFaces()))
                {
                    if (runTimer.ElapsedMilliseconds >= Cg24TotalTimeBudgetMs ||
                        result.Faces >= Cg24MaxFacesPerCandidate ||
                        result.EdgeSamples >= Cg24MaxEdgeSamplesPerCandidate)
                    { complete = false; break; }
                    if ((result.Faces & 31) == 0) ThrowIfCanceled();
                    ++result.Faces;
                    Face2 face = faceObject as Face2;
                    if (face == null) continue;
                    object[] loops;
                    try { loops = ToObjectArray(face.GetLoops()); }
                    catch { complete = false; continue; }
                    foreach (object loopObject in loops)
                    {
                        if (runTimer.ElapsedMilliseconds >= Cg24TotalTimeBudgetMs ||
                            result.EdgeSamples >= Cg24MaxEdgeSamplesPerCandidate)
                        { complete = false; break; }
                        Loop2 loop = loopObject as Loop2;
                        if (loop == null) continue;
                        bool isOuter;
                        object[] edges;
                        try
                        {
                            isOuter = loop.IsOuter();
                            edges = ToObjectArray(loop.GetEdges());
                        }
                        catch { complete = false; continue; }
                        if (edges.Length == 0) continue;
                        double bestRadial = double.PositiveInfinity;
                        double bestAxialGap = double.PositiveInfinity;
                        bool anyEdgeNear = false;
                        bool onlyCircle = edges.Length == 1;
                        foreach (object edgeObject in edges)
                        {
                            if (runTimer.ElapsedMilliseconds >= Cg24TotalTimeBudgetMs ||
                                result.EdgeSamples >= Cg24MaxEdgeSamplesPerCandidate)
                            { complete = false; break; }
                            Edge edge = edgeObject as Edge;
                            if (edge == null) { complete = false; continue; }
                            ++result.EdgeSamples;
                            try
                            {
                                // Closest point to slab midpoint; coordinates are PART LOCAL.
                                double[] point = ToDoubleArray(edge.GetClosestPointOn(
                                    mid[0], mid[1], mid[2]));
                                if (!Cg23Finite3(point)) { complete = false; continue; }
                                double[] delta = Cg23Sub(point, mid);
                                double axial = Cg23Dot(delta, axis);
                                double radial = Cg23Norm(Cg23Sub(delta,
                                    Cg23Scale(axis, axial)));
                                double gap = Math.Max(0.0, Math.Abs(axial) - halfSlab);
                                if (gap <= Cg24AxialWindowM && radial <= Cg24RadialWindowM)
                                {
                                    anyEdgeNear = true;
                                    if (radial < bestRadial ||
                                        (Math.Abs(radial-bestRadial) < 1e-10 && gap < bestAxialGap))
                                    { bestRadial = radial; bestAxialGap = gap; }
                                }
                                if (onlyCircle)
                                {
                                    Curve curve = edge.GetCurve() as Curve;
                                    if (curve == null || !curve.IsCircle()) onlyCircle = false;
                                }
                            }
                            catch { complete = false; }
                        }
                        if (anyEdgeNear)
                        {
                            if (isOuter) ++result.Outer;
                            else if (onlyCircle) ++result.InnerCircle;
                            else ++result.InnerComplex;
                            if (result.NearestRadialMm < 0 ||
                                bestRadial * 1000.0 < result.NearestRadialMm)
                            {
                                result.NearestRadialMm = bestRadial * 1000.0;
                                result.NearestAxialGapMm = bestAxialGap * 1000.0;
                            }
                        }
                        if (!complete) break;
                    }
                    if (!complete) break;
                }
                if (!complete) break;
            }
            result.State = Cg24ClassifyEvidence(result.InnerCircle,
                result.InnerComplex, result.Outer, complete);
            result.Reason = complete
                ? "Nearby topological boundaries only. Does NOT prove a through-hole, " +
                  "slot, joint intent or missing-hole NG."
                : "Incomplete face/loop/edge scan; do NOT interpret absence of loops.";
            return result;
        }

        [Conditional("DEBUG")]
        private static void Cg24WriteDebug(CheckHoleCg2Report report, int reviewed, long elapsedMs)
        {
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG24][SUMMARY] Candidates={0} PrimarySolid={1} Reviewed={2} ElapsedMs={3} ProductionNgChanged=NO",
                report.ExpectedHoleCandidates.Count, report.BrepSolidEvidenceCount, reviewed, elapsedMs));
            foreach (var group in report.ExpectedHoleCandidates
                .GroupBy(r => r.Cg24State ?? "NULL").OrderBy(g => g.Key))
                Debug.WriteLine("[CG24][STATE] " + group.Key + "=" + group.Count());
            foreach (var row in report.ExpectedHoleCandidates.Where(r =>
                r.MaterialEvidence == "BREP_SOLID_ON_AXIS").OrderBy(r => r.Id))
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG24][ROW] ID={0} Hole={1} Source={2} Target={3} State={4} " +
                    "TotalTargetAxes={5} CG23LocalAxes={6} InnerCircle={7} InnerComplex={8} " +
                    "Outer={9} NearestRadialMm={10:F3} AxialGapMm={11:F3} " +
                    "Faces={12} EdgeSamples={13} Reason={14}",
                    row.Id, row.SourceHoleLabel, row.SourceOccurrence, row.TargetOccurrence,
                    row.Cg24State, row.Cg24TargetDetectedAxesTotal,
                    row.Cg23LocalTargetAxisCount, row.Cg24NearbyCircularInnerLoops,
                    row.Cg24NearbyComplexInnerLoops, row.Cg24NearbyOuterLoops,
                    row.Cg24NearestBoundaryRadialMm, row.Cg24NearestBoundaryAxialGapMm,
                    row.Cg24FacesVisited, row.Cg24EdgeSamples, row.Cg24Reason));
            Cg24RunPureTestsOnce();
        }

#if DEBUG
        private static bool cg24TestsRun;
        private static void Cg24RunPureTestsOnce()
        {
            if (cg24TestsRun) return;
            cg24TestsRun = true;
            int passed = 0, total = 0;
            Cg24Test("CIRCULAR", 1, 0, 0, true, "NEAR_CIRCULAR_INNER_LOOP_REVIEW", ref passed, ref total);
            Cg24Test("COMPLEX", 0, 1, 0, true, "NEAR_COMPLEX_INNER_LOOP_REVIEW", ref passed, ref total);
            Cg24Test("MIXED", 1, 1, 0, true, "NEAR_MIXED_INNER_LOOPS_REVIEW", ref passed, ref total);
            Cg24Test("OUTER_ONLY", 0, 0, 1, true, "NEAR_OUTER_EDGE_ONLY_REVIEW", ref passed, ref total);
            Cg24Test("NO_LOOP", 0, 0, 0, true, "NO_NEAR_LOOP_DETECTED_NOT_PROOF_OF_MISSING", ref passed, ref total);
            Cg24Test("PARTIAL", 0, 0, 0, false, "UNVERIFIED_TOPOLOGY_PARTIAL", ref passed, ref total);
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG24][TEST_SUMMARY] Passed={0}/{1}", passed, total));
        }
        private static void Cg24Test(string name, int c, int n, int o, bool complete,
            string expected, ref int passed, ref int total)
        {
            ++total;
            string actual = Cg24ClassifyEvidence(c, n, o, complete);
            bool ok = string.Equals(expected, actual, StringComparison.Ordinal);
            if (ok) ++passed;
            Debug.WriteLine("[CG24][TEST] " + name + "=" + (ok ? "PASS" : "FAIL") +
                " Got=" + actual + " Expected=" + expected);
        }
#else
        private static void Cg24RunPureTestsOnce() { }
#endif
    }
}
