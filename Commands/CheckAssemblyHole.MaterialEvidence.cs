// CG2 BREP MATERIAL EVIDENCE — diagnostic-only, read-only.
// Runs AFTER existing CG2 AABB screening. Never assigns NG or required-hole intent.
// C# 7.3, SOLIDWORKS >= 2018 SP02 API, .NET Framework 4.8.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
        // The ray test is deliberately capped; it is NOT a full-assembly validator.
        private const int Cg2MaxBrepRayChecks = 500;
        private const int Cg2MaxBrepBodiesPerOccurrence = 40;
        private const double Cg2MinMaterialSpanM = 0.00010; // 0.10 mm, diagnostic evidence threshold
        private const double Cg2BrepStartMarginM = 0.010; // start outside approximate target box
        private const double Cg2BrepOffsetM = 0.000001; // 1 micron, SOLIDWORKS ray entry/exit
        private const double Cg2BrepHitRadiusM = 0.0; // exact axis, not tolerance-expanded cylinder
        private const double Cg2BrepNumericalToleranceM = 0.00002;

        private sealed class Cg2TargetBodyContext
        {
            public Component2 Component;
            public ModelDoc2 PartModel;
            public Body2[] Bodies;
            public MathTransform FromWorldToPart;
            public Cg2Box WorldBox;
            public string Failure;
        }

        private sealed class Cg2RayHit
        {
            public int BodyIndex;
            public int Flags;
            public double AlongRayM;
        }

        /// <summary>
        /// Adds actual SOLIDWORKS BREP ray-intersection evidence to CG2 candidates.
        /// Calibrates local-body vs assembly coordinates before each target is trusted;
        /// unresolvable targets stay UNVERIFIED. NEVER removes an AABB candidate or
        /// mutates production stack verdicts, sketch markers, Part, Assembly or CSV.
        /// </summary>
        private void EnrichCg2BrepMaterialEvidence(
            AssemblyDoc assembly, List<HoleAxis> holes, CheckHoleCg2Report report)
        {
            if (assembly == null || holes == null || report == null)
                return;

            foreach (CheckHoleCg2ExpectedHoleRow candidate in report.ExpectedHoleCandidates)
            {
                candidate.MaterialEvidence = "NOT_TESTED";
                candidate.MaterialSpanMm = 0.0;
                candidate.MaterialHitCount = 0;
            }

            object[] components = ToObjectArray(assembly.GetComponents(false));
            var lookup = new Dictionary<string, Component2>(StringComparer.OrdinalIgnoreCase);
            foreach (object obj in components)
            {
                Component2 component = obj as Component2;
                if (!IsUsableComponent(component)) continue;
                string key = SafeComponentName(component);
                if (!string.IsNullOrWhiteSpace(key) && !lookup.ContainsKey(key))
                    lookup.Add(key, component);
            }

            var cachedContexts = new Dictionary<string, Cg2TargetBodyContext>(
                StringComparer.OrdinalIgnoreCase);

            // Sort only the diagnostic traversal priority, never mutate candidate IDs,
            // the original hole list, CG1 stack order, or production CSV rows.
            List<CheckHoleCg2ExpectedHoleRow> prioritized =
                report.ExpectedHoleCandidates
                    .OrderBy(c => c.BoxSeparationMm)
                    .ThenBy(c => c.AxisToBoxMm)
                    .ThenBy(c => c.Id)
                    .ToList();

            foreach (CheckHoleCg2ExpectedHoleRow row in prioritized)
            {
                ThrowIfCanceled();
                if (report.BrepRayTestedCount >= Cg2MaxBrepRayChecks)
                {
                    row.MaterialEvidence = "NOT_TESTED_BUDGET";
                    report.BrepNotTestedCount++;
                    report.IsPartial = true;
                    continue;
                }

                int holeIndex;
                if (!Cg2TryGetSourceIndex(row.SourceHoleLabel, holes.Count, out holeIndex)
                    || holes[holeIndex] == null
                    || !string.Equals(holes[holeIndex].ComponentOccurrence,
                                      row.SourceOccurrence, StringComparison.OrdinalIgnoreCase)
                    || !Cg2ValidVector(holes[holeIndex].Center)
                    || !Cg2ValidVector(holes[holeIndex].Direction))
                {
                    Cg2MarkBrepUnknown(row, report, "UNVERIFIED_SOURCE_HOLE");
                    continue;
                }

                Component2 target;
                if (!lookup.TryGetValue(row.TargetOccurrence, out target))
                {
                    Cg2MarkBrepUnknown(row, report, "UNVERIFIED_TARGET_OCCURRENCE");
                    continue;
                }

                Cg2TargetBodyContext context;
                if (!cachedContexts.TryGetValue(row.TargetOccurrence, out context))
                {
                    context = Cg2LoadTargetBodyContext(target);
                    cachedContexts.Add(row.TargetOccurrence, context);
                }

                if (!string.IsNullOrEmpty(context.Failure))
                {
                    Cg2MarkBrepUnknown(row, report, context.Failure);
                    continue;
                }

                // Compute the segment of the candidate axis within the target's
                // assembly-space AABB. It must be within +-60 mm of the source hole.
                double[] rayStartWorld;
                double[] unitWorld;
                double maxDistanceM;
                if (!Cg2PrepareBoundedRay(holes[holeIndex].Center,
                    holes[holeIndex].Direction, context.WorldBox,
                    out rayStartWorld, out unitWorld, out maxDistanceM))
                {
                    Cg2MarkBrepUnknown(row, report, "UNVERIFIED_RAY_RANGE");
                    continue;
                }

                double[] rayStartLocal = TransformPoint(rayStartWorld, context.FromWorldToPart);
                double[] rayDirLocal = TransformVector(unitWorld, context.FromWorldToPart);
                if (!Cg2ValidVector(rayStartLocal) || !Cg2ValidVector(rayDirLocal))
                {
                    Cg2MarkBrepUnknown(row, report, "UNVERIFIED_TRANSFORM");
                    continue;
                }

                string evidence;
                double spanM;
                int hitCount;
                try
                {
                    // Ray intersections work in the target part-local frame.
                    // Component.GetBodies3 includes instance-level body changes;
                    // bounding-center calibration in Cg2LoadTargetBodyContext
                    // detects suspicious coordinate mismatch and refuses to claim.
                    evidence = Cg2ReadRayEvidence(context.PartModel, context.Bodies,
                        rayStartLocal, rayDirLocal, maxDistanceM, out spanM, out hitCount);
                }
                catch (Exception ex)
                {
                    Cg2Warn(report, "BREP ray API error on a target occurrence: "
                        + ex.GetType().Name);
                    evidence = "UNVERIFIED_RAY_API";
                    spanM = 0;
                    hitCount = 0;
                }

                row.MaterialEvidence = evidence;
                row.MaterialHitCount = hitCount;
                row.MaterialSpanMm = spanM * 1000.0;
                report.BrepRayTestedCount++;

                if (evidence == "BREP_SOLID_ON_AXIS")
                    report.BrepSolidEvidenceCount++;
                else if (evidence == "BREP_NO_AXIS_HIT")
                    report.BrepNoAxisHitCount++;
                else
                {
                    report.BrepUnknownCount++;
                    report.IsPartial = true;
                }
            }

            if (report.BrepNotTestedCount > 0)
                Cg2Warn(report, "BREP ray budget reached: "
                    + report.BrepNotTestedCount.ToString(CultureInfo.InvariantCulture)
                    + " candidates NOT TESTED. AABB remains CHECK_ONLY.");

            if (report.IsPartial)
                report.CoverageStatus = "PARTIAL_UNVERIFIED";

            // A hit proves BREP material along the tested axis only.
            // It does NOT prove a through-hole is required, or that a joint exists.
        }

        private static void Cg2MarkBrepUnknown(
            CheckHoleCg2ExpectedHoleRow row, CheckHoleCg2Report report, string why)
        {
            row.MaterialEvidence = why;
            report.BrepUnknownCount++;
            report.IsPartial = true;
        }

        private static bool Cg2TryGetSourceIndex(
            string label, int count, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(label) ||
                !label.StartsWith("Hole#", StringComparison.Ordinal))
                return false;
            int oneBased;
            if (!int.TryParse(label.Substring(5), NumberStyles.None,
                              CultureInfo.InvariantCulture, out oneBased)
                || oneBased < 1 || oneBased > count)
                return false;
            index = oneBased - 1;
            return true;
        }

        private Cg2TargetBodyContext Cg2LoadTargetBodyContext(Component2 component)
        {
            var result = new Cg2TargetBodyContext { Component = component };
            try
            {
                ModelDoc2 part = component.GetModelDoc2() as ModelDoc2;
                if (part == null || !(part is PartDoc))
                {
                    result.Failure = "UNVERIFIED_NOT_RESOLVED_PART";
                    return result;
                }
                MathTransform placement = component.Transform2;
                MathTransform inverse = placement == null
                    ? null : placement.Inverse() as MathTransform;
                if (placement == null || inverse == null)
                {
                    result.Failure = "UNVERIFIED_NO_COMPONENT_TRANSFORM";
                    return result;
                }

                object info;
                var bodies = ToObjectArray(component.GetBodies3(
                    (int)swBodyType_e.swSolidBody, out info))
                    .OfType<Body2>().ToArray();
                if (bodies.Length == 0 || bodies.Length > Cg2MaxBrepBodiesPerOccurrence)
                {
                    result.Failure = "UNVERIFIED_NO_OR_TOO_MANY_SOLIDS";
                    return result;
                }

                Cg2Box worldBox = Cg2ReadBox(SafeComponentName(component),
                    ToDoubleArray(component.GetBox(false, false)));
                if (worldBox == null)
                {
                    result.Failure = "UNVERIFIED_COMPONENT_BOX";
                    return result;
                }

                // Calibration checks the coordinate-space assumption; approximate
                // bounding boxes are used ONLY to reject suspicious COM geometry,
                // never to prove a material hit.
                if (!Cg2BodyCoordinatesCalibrated(bodies, placement, worldBox))
                {
                    result.Failure = "UNVERIFIED_BODY_COORDINATES";
                    return result;
                }

                result.PartModel = part;
                result.Bodies = bodies;
                result.FromWorldToPart = inverse;
                result.WorldBox = worldBox;
                return result;
            }
            catch (Exception ex)
            {
                result.Failure = "UNVERIFIED_BODY_API_" + ex.GetType().Name;
                return result;
            }
        }

        private bool Cg2BodyCoordinatesCalibrated(
            Body2[] bodies, MathTransform placement, Cg2Box expectedWorld)
        {
            var localMin = new[] {
                double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
            var localMax = new[] {
                double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };

            foreach (Body2 body in bodies)
            {
                double[] box = ToDoubleArray(body.GetBodyBox());
                if (box.Length < 6) return false;
                for (int k = 0; k < 3; k++)
                {
                    if (double.IsInfinity(box[k]) || double.IsNaN(box[k])
                        || double.IsInfinity(box[k+3]) || double.IsNaN(box[k+3]))
                        return false;
                    localMin[k] = Math.Min(localMin[k], Math.Min(box[k], box[k+3]));
                    localMax[k] = Math.Max(localMax[k], Math.Max(box[k], box[k+3]));
                }
            }
            double[] centerLocal = new[] {
                (localMin[0] + localMax[0])*0.5,
                (localMin[1] + localMax[1])*0.5,
                (localMin[2] + localMax[2])*0.5 };
            double[] world = TransformPoint(centerLocal, placement);
            if (!Cg2ValidVector(world)) return false;
            double sq = 0, diagSq = 0;
            for (int k = 0; k < 3; k++)
            {
                double center = (expectedWorld.Min[k] + expectedWorld.Max[k])*0.5;
                double delta = world[k] - center;
                sq += delta * delta;
                double dim = expectedWorld.Max[k] - expectedWorld.Min[k];
                diagSq += dim * dim;
            }
            // 2mm / 5% of total target diagonal: diagnostic rejection only.
            return Math.Sqrt(sq) <= Math.Max(0.002, Math.Sqrt(diagSq)*0.05);
        }

        private static bool Cg2PrepareBoundedRay(
            double[] source, double[] direction, Cg2Box box,
            out double[] start, out double[] unit, out double maxDistM)
        {
            start = null;
            unit = null;
            maxDistM = 0;
            if (!Cg2ValidVector(source) || !Cg2ValidVector(direction) || box == null)
                return false;
            double mag = Math.Sqrt(direction.Take(3).Sum(x => x*x));
            if (mag < 1e-12) return false;
            unit = direction.Take(3).Select(x => x/mag).ToArray();

            double minT = 0, maxT = 0;
            for (int k = 0; k < 3; k++)
            {
                double p = (box.Min[k] - source[k])*unit[k];
                double q = (box.Max[k] - source[k])*unit[k];
                minT += Math.Min(p, q);
                maxT += Math.Max(p, q);
            }
            minT = Math.Max(minT-Cg2BrepStartMarginM, -Cg2MaxAxisReachM);
            maxT = Math.Min(maxT+Cg2BrepStartMarginM, Cg2MaxAxisReachM);
            if (maxT <= minT + Cg2MinMaterialSpanM) return false;
            start = new[] {
                source[0] + unit[0]*minT,
                source[1] + unit[1]*minT,
                source[2] + unit[2]*minT };
            maxDistM = maxT - minT;
            return true;
        }

        private static string Cg2ReadRayEvidence(
            ModelDoc2 model, Body2[] bodies,
            double[] start, double[] direction, double limitM,
            out double longestSpanM, out int hitCount)
        {
            longestSpanM = 0;
            hitCount = 0;
            if (model == null || bodies == null || bodies.Length == 0)
                return "UNVERIFIED_NO_BODY";

            // With only ENTRY_EXIT option, GetRayIntersectionsPoints gives
            // six doubles per hit: BodyIndex, RayIndex, Type, x, y, z.
            // This is a read-only geometry operation, not a sketch/edit.
            int count = model.Extension.RayIntersections(
                bodies, start, direction,
                (int)swRayPtsOpts_e.swRayPtsOptsENTRY_EXIT,
                Cg2BrepHitRadiusM, Cg2BrepOffsetM, true);
            if (count < 0) return "UNVERIFIED_RAY_RETURN";
            hitCount = count;
            if (count == 0) return "BREP_NO_AXIS_HIT";
            if (count > 200) return "UNVERIFIED_TOO_MANY_RAY_HITS";
            double[] points = ToDoubleArray(model.GetRayIntersectionsPoints());
            if (points == null || points.Length < 6*count)
                return "UNVERIFIED_RAY_POINTS";

            double mag = Math.Sqrt(direction.Take(3).Sum(x => x*x));
            if (mag < 1e-12) return "UNVERIFIED_ZERO_RAY";
            var rays = new List<Cg2RayHit>();
            for (int i = 0; i < count; i++)
            {
                int offset = i * 6;
                int body = (int)Math.Round(points[offset]);
                int ray = (int)Math.Round(points[offset + 1]);
                int flags = (int)Math.Round(points[offset + 2]);
                if (body < 0 || body >= bodies.Length || ray != 0)
                    return "UNVERIFIED_RAY_TOPOLOGY";
                double t = 0;
                for (int k = 0; k < 3; k++)
                {
                    if (double.IsNaN(points[offset+3+k]) ||
                        double.IsInfinity(points[offset+3+k]))
                        return "UNVERIFIED_RAY_COORDINATES";
                    t += (points[offset+3+k]-start[k]) * direction[k]/mag;
                }
                if (t < -Cg2BrepNumericalToleranceM ||
                    t > limitM + Cg2BrepNumericalToleranceM)
                    continue;
                rays.Add(new Cg2RayHit { BodyIndex=body, Flags=flags, AlongRayM=t });
            }
            if (rays.Count == 0)
                return "UNVERIFIED_HITS_OUTSIDE_TEST_SPAN";

            // A positive result must have a definite ENTER => EXIT segment
            // on the SAME SOLID BODY, not a single touching edge/grazing hit.
            foreach (var group in rays.GroupBy(h => h.BodyIndex))
            {
                double? entry = null;
                foreach (Cg2RayHit hit in group.OrderBy(h => h.AlongRayM))
                {
                    bool entering = (hit.Flags & (int)swRayPtsResults_e.swRayPtsResultsENTER) != 0;
                    bool leaving  = (hit.Flags & (int)swRayPtsResults_e.swRayPtsResultsEXIT) != 0;
                    if (entering && !leaving) entry = hit.AlongRayM;
                    else if (leaving && !entering && entry.HasValue)
                    {
                        double span = hit.AlongRayM - entry.Value;
                        if (span > longestSpanM) longestSpanM = span;
                        entry = null;
                    }
                    else entry = null; // ambiguous grazing/edge events
                }
            }
            return longestSpanM >= Cg2MinMaterialSpanM
                ? "BREP_SOLID_ON_AXIS" : "UNVERIFIED_AMBIGUOUS_HITS";
        }
    }
}
