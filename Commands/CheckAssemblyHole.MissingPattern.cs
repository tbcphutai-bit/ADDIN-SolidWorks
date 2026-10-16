using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
        private readonly Dictionary<string, MissingPatternBody> missingPatternBodies =
            new Dictionary<string, MissingPatternBody>(StringComparer.OrdinalIgnoreCase);

        private sealed class MissingPatternBody
        {
            public object[] Faces;
            public MathTransform Transform;
        }

        private static string MissingBodyKey(string occurrence, string body)
        {
            return (occurrence ?? "") + "|" + (body ?? "");
        }

        // A gap alone is not an NG. Require a row shared by at least two known
        // concentric pairs and real target material at the expected missing position.
        private static int AppendMissingPatternIssues(List<HoleAxis> holes, List<HoleStackResult> results,
            List<PatternIssue> issues, HashSet<string> issueKeys,
            Func<HoleAxis, HoleAxis, double[], double[]> locateTargetSurface)
        {
            int added = 0;
            var pairs = BuildPatternAnchorPairs(results.Where(x => !x.IsNg && x.Stack != null && x.Stack.Holes.Count >= 2).ToList());
            foreach (var pair in pairs.Values)
            {
                if (pair.Count < 2) continue;
                var firstHoles = holes.Where(x => x.ComponentOccurrence == pair[0].First.ComponentOccurrence).ToList();
                var secondHoles = holes.Where(x => x.ComponentOccurrence == pair[0].Second.ComponentOccurrence).ToList();
                var accepted = new List<PatternRowCandidate>();
                foreach (var candidate in BuildMissingPatternRowCandidates(pair, firstHoles, secondHoles).OrderByDescending(x => x.Score))
                {
                    if (accepted.Any(x => SamePhysicalPatternRow(x, candidate)
                        && x.Anchor.First.BodyName == candidate.Anchor.First.BodyName
                        && x.Anchor.Second.BodyName == candidate.Anchor.Second.BodyName)) continue;
                    var firstRow = candidate.FirstRow.Select(x => x.Hole).Where(x => SameMissingPatternLayer(x, candidate.Anchor.First)).ToList();
                    var secondRow = candidate.SecondRow.Select(x => x.Hole).Where(x => SameMissingPatternLayer(x, candidate.Anchor.Second)).ToList();
                    int support = pair.Count(x => firstRow.Contains(x.First) && secondRow.Contains(x.Second)
                        && AxisAngleDeg(x.First.Direction, x.Second.Direction) <= SearchAngleToleranceDeg
                        && TransverseCenterDistance(x.First, x.Second) <= PositionToleranceM
                        && AreMissingPatternLayersAdjacent(x.First, x.Second));
                    if (support < 2) continue;
                    accepted.Add(candidate);
                    added += AppendMissingPatternSide(firstRow, secondRow, candidate.Anchor.First, candidate.Anchor.Second,
                        candidate.Direction, holes, results, issues, issueKeys, locateTargetSurface);
                    added += AppendMissingPatternSide(secondRow, firstRow, candidate.Anchor.Second, candidate.Anchor.First,
                        candidate.Direction, holes, results, issues, issueKeys, locateTargetSurface);
                }
            }
            return added;
        }

        private static List<PatternRowCandidate> BuildMissingPatternRowCandidates(List<PatternAnchorPair> anchors,
            List<HoleAxis> firstHoles, List<HoleAxis> secondHoles)
        {
            var candidates = new List<PatternRowCandidate>();
            foreach (PatternAnchorPair origin in anchors)
            {
                var directions = new List<double[]>();
                foreach (PatternAnchorPair other in anchors)
                {
                    if (ReferenceEquals(origin, other)) continue;
                    double[] delta = Subtract(other.First.Center, origin.First.Center);
                    delta = Subtract(delta, Scale(origin.First.Direction, Dot(delta, origin.First.Direction)));
                    if (Length(delta) < PatternMinimumStepM) continue;
                    double[] direction = Normalize(delta);
                    if (directions.Any(x => OrientedAxisAngleDeg(x, direction) <= PatternDirectionMergeToleranceDeg)) continue;
                    directions.Add(direction);
                    var firstRow = BuildPatternRowSigned(firstHoles, origin.First, direction);
                    var secondRow = BuildPatternRowSigned(secondHoles, origin.Second, direction);
                    candidates.Add(new PatternRowCandidate { Anchor = origin, Direction = direction, FirstRow = firstRow,
                        SecondRow = secondRow, Score = Math.Min(firstRow.Count, secondRow.Count) * 100 + Math.Max(firstRow.Count, secondRow.Count) });
                }
            }
            return candidates;
        }

        private static bool AreMissingPatternLayersAdjacent(HoleAxis first, HoleAxis second)
        {
            double gap;
            return AreHoleLayersAdjacent(first, second, out gap);
        }

        private static bool SameMissingPatternLayer(HoleAxis hole, HoleAxis anchor)
        {
            return hole != null && anchor != null && string.Equals(hole.BodyName, anchor.BodyName, StringComparison.OrdinalIgnoreCase)
                && AxisAngleDeg(hole.Direction, anchor.Direction) <= SearchAngleToleranceDeg
                && Math.Abs(Dot(Subtract(hole.Center, anchor.Center), anchor.Direction)) <= SlotPairToleranceM
                && Math.Abs(hole.RadiusM - anchor.RadiusM) <= SlotPairToleranceM
                && hole.Source != HoleSource.RecoveredDeformed;
        }

        private static bool TryGetMissingPatternPitch(List<HoleAxis> row, double[] direction, out double pitch)
        {
            pitch = 0;
            if (row.Count < 3) return false;
            double[] positions = row.Select(x => Dot(x.Center, direction)).OrderBy(x => x).ToArray();
            var steps = new List<double>();
            for (int i = 1; i < positions.Length; i++)
            {
                double step = positions[i] - positions[i - 1];
                if (step < PatternMinimumStepM) return false;
                steps.Add(step);
            }
            pitch = steps.Min();
            double basePitch = pitch;
            // Allow a skipped interior slot in either reference row, but never
            // invent a hole where both components intentionally omit that slot.
            return steps.All(step => Math.Abs(step - Math.Round(step / basePitch) * basePitch) <= PositionToleranceM);
        }

        private static int AppendMissingPatternSide(List<HoleAxis> sourceRow, List<HoleAxis> targetRow,
            HoleAxis sourceAnchor, HoleAxis targetAnchor, double[] direction, List<HoleAxis> allHoles,
            List<HoleStackResult> results, List<PatternIssue> issues, HashSet<string> keys,
            Func<HoleAxis, HoleAxis, double[], double[]> locateTargetSurface)
        {
            double pitch;
            if (targetRow.Count < 2 || !TryGetMissingPatternPitch(sourceRow, direction, out pitch)) return 0;
            int added = 0;
            double nearbyGate = Math.Min(RecoveryMaximumPositionM, pitch * RecoveryPitchFraction);
            foreach (HoleAxis source in sourceRow)
            {
                // Existing paired/misaligned holes remain the responsibility of
                // the original engine; do not report them again as missing.
                if (results.Any(x => x.Stack.Holes.Contains(source) && x.Stack.Holes.Any(other =>
                    other.ComponentOccurrence == targetAnchor.ComponentOccurrence
                    && string.Equals(other.BodyName, targetAnchor.BodyName, StringComparison.OrdinalIgnoreCase)))) continue;
                if (issues.Any(x => !x.IsMissingHole && ((ReferenceEquals(x.ExpectedHole, source) && x.SecondComponent == targetAnchor.ComponentOccurrence)
                    || (ReferenceEquals(x.ActualHole, source) && x.FirstComponent == targetAnchor.ComponentOccurrence)))) continue;
                double[] delta = Subtract(source.Center, sourceAnchor.Center);
                double[] expected = Add(targetAnchor.Center, Subtract(delta, Scale(sourceAnchor.Direction, Dot(delta, sourceAnchor.Direction))));
                if (allHoles.Any(x => x.ComponentOccurrence == targetAnchor.ComponentOccurrence
                    && string.Equals(x.BodyName, targetAnchor.BodyName, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(Dot(Subtract(x.Center, targetAnchor.Center), targetAnchor.Direction)) <= SlotPairToleranceM
                    && Length(Subtract(Subtract(x.Center, expected), Scale(targetAnchor.Direction, Dot(Subtract(x.Center, expected), targetAnchor.Direction)))) <= nearbyGate)) continue;
                double[] surfacePoint = locateTargetSurface(source, targetAnchor, expected);
                if (!IsPoint(surfacePoint)) continue;
                string key = "MISSING|" + MissingBodyKey(targetAnchor.ComponentOccurrence, targetAnchor.BodyName)
                    + "|" + QuantizePoint(surfacePoint, SameHolePositionToleranceM);
                if (!keys.Add(key)) continue;
                issues.Add(new PatternIssue
                {
                    FirstComponent = source.ComponentOccurrence, SecondComponent = targetAnchor.ComponentOccurrence,
                    Kind = "NG THIEU LO", IsMissingHole = true, ExpectedPitchM = pitch,
                    ExpectedHole = source, ExpectedCenter = ClonePoint(surfacePoint),
                    AnchorDirection = ClonePoint(targetAnchor.Direction)
                });
                added++;
                Debug.WriteLine("[CHECK HOLE ASSY] MISSING_HOLE reference=" + source.ComponentOccurrence
                    + ", missingOn=" + targetAnchor.ComponentOccurrence + ", center=" + FormatPointMm(surfacePoint)
                    + ", expectedPitch=" + FormatMm(pitch) + "mm, evidence=SHARED_ROW_AND_TARGET_MATERIAL");
            }
            return added;
        }

        private double[] LocateMissingHoleSurface(HoleAxis source, HoleAxis target, double[] expected)
        {
            ThrowIfCanceled();
            MissingPatternBody body;
            if (!missingPatternBodies.TryGetValue(MissingBodyKey(target.ComponentOccurrence, target.BodyName), out body)) return null;
            MathTransform inverse = body.Transform == null ? null : body.Transform.Inverse() as MathTransform;
            double[] localExpected = inverse == null ? null : TransformPoint(expected, inverse);
            if (!IsPoint(localExpected)) return null;
            double[] best = null;
            double bestGap = double.PositiveInfinity;
            foreach (Face2 face in body.Faces.OfType<Face2>())
            {
                ThrowIfCanceled();
                try
                {
                    Surface surface = face.GetSurface() as Surface;
                    if (surface == null || !surface.IsPlane()) continue;
                    double[] plane = ToDoubleArray(surface.PlaneParams);
                    if (plane.Length < 6) continue;
                    double[] normal = TransformVector(new[] { plane[0], plane[1], plane[2] }, body.Transform);
                    if (!IsDirection(normal) || AxisAngleDeg(normal, target.Direction) > SearchAngleToleranceDeg) continue;
                    double[] raw = ToDoubleArray(face.GetClosestPointOn(localExpected[0], localExpected[1], localExpected[2]));
                    if (raw.Length < 3) continue;
                    double[] point = TransformPoint(new[] { raw[0], raw[1], raw[2] }, body.Transform);
                    if (!IsPoint(point)) continue;
                    double[] delta = Subtract(point, expected);
                    double axial = Dot(delta, target.Direction);
                    if (Length(Subtract(delta, Scale(target.Direction, axial))) > SameHolePositionToleranceM
                        || Math.Abs(axial) > target.AxialLength * 0.5 + SlotPairToleranceM) continue;
                    double gap = Math.Min(Distance(point, Add(source.Point, Scale(source.Direction, source.MinProjection))),
                        Distance(point, Add(source.Point, Scale(source.Direction, source.MaxProjection))));
                    if (gap > AdjacentFaceGapM + PositionToleranceM || gap >= bestGap) continue;
                    best = point; bestGap = gap;
                }
                catch (Exception ex) { Debug.WriteLine("[CHECK HOLE ASSY] Missing-hole surface unavailable: " + ex.Message); }
            }
            return best;
        }

        private static List<double[][]> BuildMissingHoleMarkerLines(PatternIssue issue)
        {
            var lines = new List<double[][]>();
            if (issue == null || !issue.IsMissingHole || !IsPoint(issue.ExpectedCenter) || !IsDirection(issue.AnchorDirection)) return lines;
            double[] axis = Normalize(issue.AnchorDirection);
            double[] center = Add(issue.ExpectedCenter, Scale(MissingHoleViewAxis(issue), 0.00003));
            double[] u, v;
            BuildPerpendicularFrame(axis, out u, out v);
            double half = Math.Max(0.0015, Math.Min(0.005, issue.ExpectedHole == null ? 0.002 : issue.ExpectedHole.RadiusM * 0.8));
            foreach (double[] diagonal in new[] { Normalize(Add(u, v)), Normalize(Subtract(u, v)) })
                lines.Add(new[] { Add(center, Scale(diagonal, -half)), Add(center, Scale(diagonal, half)) });
            return lines;
        }

        private static double[] MissingHoleViewAxis(PatternIssue issue)
        {
            if (issue == null || !IsDirection(issue.AnchorDirection)) return null;
            if (IsDirection(issue.DisplayNormal)) return Normalize(issue.DisplayNormal);
            double[] axis = Normalize(issue.AnchorDirection);
            if (issue.ExpectedHole != null && IsPoint(issue.ExpectedCenter)
                && Dot(Subtract(issue.ExpectedHole.Center, issue.ExpectedCenter), axis) < 0) axis = Scale(axis, -1);
            return axis;
        }
    }
}
