using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.MirrorV7
{
    public sealed class WorkingCopyEquivalenceResultV7
    {
        public bool Success { get; set; }
        public bool GeometryEquivalent { get; set; }
        public bool StructureVerified { get; set; }
        public bool MutationIdentityVerified { get; set; }
        public string UnverifiedDependencies { get; set; }
        public int SourceFeatureCount { get; set; }
        public int CopyFeatureCount { get; set; }
        public int SourceSketchCount { get; set; }
        public int CopySketchCount { get; set; }
        public int MatchedFeatures { get; set; }
        public int MatchedSketches { get; set; }
        public int SourceBodyCount { get; set; }
        public int CopyBodyCount { get; set; }
        public int MatchedBodies { get; set; }
        public int Errors { get; set; }
        public string VerificationCoverage { get; set; }
        public string DependencyBlockers { get; set; }
        public bool PlanBuilt { get; set; }
        public SketchMirrorPlanV7 Plan { get; set; }
        public string WorkingPath { get; set; }
        public string RunId { get; set; }
    }

    public static class WorkingCopyEquivalenceAuditV7
    {
        private const double LengthAbsoluteTolerance = 1.0e-9;
        private const double LengthRelativeTolerance = 1.0e-8;
        private const double AreaAbsoluteTolerance = 1.0e-10;
        private const double AreaRelativeTolerance = 1.0e-8;
        private const double VolumeAbsoluteTolerance = 1.0e-12;
        private const double VolumeRelativeTolerance = 1.0e-8;

        public static WorkingCopyEquivalenceResultV7 Run(
            MirrorV7Context sourceContext,
            ModelDoc2 copy,
            MirrorV7ModelGraph copyGraph)
        {
            if (sourceContext == null || sourceContext.PartDoc == null || sourceContext.Graph == null)
                throw new InvalidOperationException("PHASE6A requires a completed source audit.");
            if (copy == null || copyGraph == null) throw new ArgumentNullException("copy");
            WorkingCopyEquivalenceResultV7 result = new WorkingCopyEquivalenceResultV7();
            result.SourceFeatureCount = sourceContext.PartDoc.GetFeatureCount();
            result.CopyFeatureCount = copy.GetFeatureCount();
            result.SourceSketchCount = CountRole(sourceContext.Graph, MirrorV7FeatureRole.Sketch);
            result.CopySketchCount = CountRole(copyGraph, MirrorV7FeatureRole.Sketch);
            bool pass = result.SourceFeatureCount == result.CopyFeatureCount;
            Dictionary<string, MirrorV7FeatureNode> copyByIdentity = new Dictionary<string, MirrorV7FeatureNode>(StringComparer.Ordinal);
            foreach (MirrorV7FeatureNode node in copyGraph.Nodes)
            {
                string key = Identity(node);
                if (copyByIdentity.ContainsKey(key)) { pass = false; result.Errors++; MirrorV7Diagnostics.Log("[PHASE6A][FEATURE_AMBIGUOUS] key=" + key); }
                else copyByIdentity.Add(key, node);
            }
            foreach (MirrorV7FeatureNode sourceNode in sourceContext.Graph.Nodes)
            {
                if (sourceNode == null || sourceNode.Role == MirrorV7FeatureRole.System) continue;
                MirrorV7FeatureNode copyNode;
                if (!copyByIdentity.TryGetValue(Identity(sourceNode), out copyNode))
                { pass = false; result.Errors++; MirrorV7Diagnostics.Log("[PHASE6A][FEATURE_MISSING] name=\"" + sourceNode.Name + "\" type=\"" + sourceNode.TypeName + "\""); continue; }
                if (sourceNode.IsSuppressed != copyNode.IsSuppressed)
                { pass = false; result.Errors++; MirrorV7Diagnostics.Log("[PHASE6A][FEATURE_SUPPRESSION_MISMATCH] name=\"" + sourceNode.Name + "\""); }
                else result.MatchedFeatures++;
            }

            MirrorV7Context copyContext = new MirrorV7Context { SwApp = sourceContext.SwApp, PartDoc = copy, Graph = copyGraph, AuditOnly = true };
            Dictionary<string, SketchSnapshotV7> sourceSketches = CaptureSketches(sourceContext, result, true);
            Dictionary<string, SketchSnapshotV7> copySketches = CaptureSketches(copyContext, result, false);
            if (sourceSketches.Count != copySketches.Count) { pass = false; result.Errors++; }
            foreach (KeyValuePair<string, SketchSnapshotV7> pair in sourceSketches)
            {
                SketchSnapshotV7 other;
                if (!copySketches.TryGetValue(pair.Key, out other)) { pass = false; result.Errors++; continue; }
                string reason;
                LogSketchCounts(pair.Value, other);
                if (!CompareSketch(pair.Value, other, out reason)) { pass = false; result.Errors++; MirrorV7Diagnostics.Log("[PHASE6A][SKETCH_MISMATCH] name=\"" + pair.Key + "\" reason=" + reason); }
                else result.MatchedSketches++;
            }

            List<Body2> sourceBodies = GetBodies(sourceContext.PartDoc);
            List<Body2> copyBodies = GetBodies(copy);
            result.SourceBodyCount = sourceBodies.Count; result.CopyBodyCount = copyBodies.Count;
            string bodyError;
            int matchedBodies;
            if (!MatchBodies(sourceBodies, copyBodies, out matchedBodies, out bodyError)) { pass = false; result.Errors++; result.DependencyBlockers = bodyError; }
            result.MatchedBodies = matchedBodies;
            result.GeometryEquivalent = result.MatchedSketches == result.SourceSketchCount && result.MatchedBodies == result.SourceBodyCount;
            result.StructureVerified = pass;
            result.MutationIdentityVerified = false;
            result.UnverifiedDependencies = "Geometry matching is equivalence-only; mutation point identity is not established by this audit.";
            result.VerificationCoverage = "Feature type/suppression; sketch flags; slot correspondence/geometry; synthesized-slot-point normalization; one-to-one regular-point geometry; segment type/construction/length; dimensions/relations; body count, volume, centroid, surface area. Segment arc/spline full definitions, endpoint/center/radius and bbox are not authoritative in current snapshot/API.";
            MirrorV7Diagnostics.Log("[PHASE6A][EQUIVALENCE_SUMMARY] feature=" + result.MatchedFeatures + "/" + result.SourceFeatureCount + " sketch=" + result.MatchedSketches + "/" + result.SourceSketchCount + " body=" + result.MatchedBodies + "/" + result.SourceBodyCount + " geometryEquivalent=" + result.GeometryEquivalent + " structureVerified=" + result.StructureVerified + " mutationIdentityVerified=" + result.MutationIdentityVerified + " unverifiedDependencies=MutationIdentity " + " lengthTolSI=" + LengthAbsoluteTolerance.ToString("G", CultureInfo.InvariantCulture) + " areaTolSI=" + AreaAbsoluteTolerance.ToString("G", CultureInfo.InvariantCulture) + " volumeTolSI=" + VolumeAbsoluteTolerance.ToString("G", CultureInfo.InvariantCulture) + " result=" + (pass ? "PASS" : "FAIL"));
            result.Success = pass;
            return result;
        }

        private static Dictionary<string, SketchSnapshotV7> CaptureSketches(MirrorV7Context context, WorkingCopyEquivalenceResultV7 result, bool source)
        {
            Dictionary<string, SketchSnapshotV7> map = new Dictionary<string, SketchSnapshotV7>(StringComparer.Ordinal);
            foreach (MirrorV7FeatureNode node in context.Graph.Nodes)
            {
                if (node == null || node.Role != MirrorV7FeatureRole.Sketch) continue;
                SketchSnapshotV7 snapshot = SketchSnapshotServiceV7.Capture(context, node);
                if (!snapshot.CaptureSucceeded) { result.Errors++; MirrorV7Diagnostics.Log("[PHASE6A][SKETCH_CAPTURE_BLOCKER] name=\"" + node.Name + "\" reason=" + snapshot.CaptureFailure + " captureSucceeded=False"); continue; }
                if (map.ContainsKey(node.Name ?? "")) { result.Errors++; MirrorV7Diagnostics.Log("[PHASE6A][SKETCH_AMBIGUOUS] name=\"" + node.Name + "\""); continue; }
                map.Add(node.Name ?? "", snapshot);
            }
            return map;
        }

        private static void LogSketchCounts(SketchSnapshotV7 source, SketchSnapshotV7 copy)
        {
            MirrorV7Diagnostics.Log("[PHASE6A][SKETCH_COUNTS] name=\"" + source.FeatureName + "\" declaredPoints=" + source.DeclaredPointCount + "/" + copy.DeclaredPointCount + " capturedPoints=" + source.PointCount + "/" + copy.PointCount + " declaredSegments=" + source.DeclaredSegmentCount + "/" + copy.DeclaredSegmentCount + " capturedSegments=" + source.SegmentCount + "/" + copy.SegmentCount + " entities=" + source.Entities.Count + "/" + copy.Entities.Count + " declaredSlots=" + source.DeclaredSlotCount + "/" + copy.DeclaredSlotCount + " capturedSlots=" + source.Slots.Count + "/" + copy.Slots.Count + " relations=" + source.Constraints.Count + "/" + copy.Constraints.Count + " dimensions=" + source.Dimensions.Count + "/" + copy.Dimensions.Count);
        }

        private static bool CompareSketch(SketchSnapshotV7 a, SketchSnapshotV7 b, out string reason)
        {
            reason = null;
            if (a == null || b == null || !a.CaptureSucceeded || !b.CaptureSucceeded) { reason = "capture"; return false; }
            if (a.Is3D != b.Is3D || a.IsDerived != b.IsDerived || a.IsShared != b.IsShared || a.IsSuppressed != b.IsSuppressed) { reason = "flags"; return false; }
            if (a.SegmentCount != b.SegmentCount) { reason = "capturedSegmentCount"; return false; }
            if (a.Slots.Count != b.Slots.Count) { reason = "capturedSlotCount"; return false; }
            if (a.Constraints.Count != b.Constraints.Count) { reason = "constraintCount"; return false; }
            if (a.Dimensions.Count != b.Dimensions.Count) { reason = "dimensionCount"; return false; }

            bool hasSlots = a.Slots.Count > 0;
            if (hasSlots)
            {
                bool[] used = new bool[b.Slots.Count];
                for (int i = 0; i < a.Slots.Count; i++)
                {
                    int match = -1;
                    for (int j = 0; j < b.Slots.Count; j++) if (!used[j] && SlotCopyComparisonV7.SameSlotGeometry(a.Slots[i], b.Slots[j], LengthAbsoluteTolerance)) { if (match >= 0) { reason = "slotMappingAmbiguous"; MirrorV7Diagnostics.Log("[PHASE6A][SKETCH_MAPPING_AMBIGUOUS] name=\"" + a.FeatureName + "\" kind=slot sourceIndex=" + i); return false; } match = j; }
                    if (match < 0) { reason = "slotGeometry"; MirrorV7Diagnostics.Log("[PHASE6A][SKETCH_GEOMETRY_MISMATCH] name=\"" + a.FeatureName + "\" kind=slot sourceIndex=" + i); return false; }
                    used[match] = true;
                }
                int sourceSynthetic, copySynthetic;
                List<SketchEntitySnapshotV7> sourceRegular = SlotCopyComparisonV7.GetRegularPoints(a, LengthAbsoluteTolerance, out sourceSynthetic);
                List<SketchEntitySnapshotV7> copyRegular = SlotCopyComparisonV7.GetRegularPoints(b, LengthAbsoluteTolerance, out copySynthetic);
                if (sourceRegular.Count != copyRegular.Count) { reason = "regularPointCount"; return false; }
                if (a.PointCount - b.PointCount != sourceSynthetic - copySynthetic) { reason = "unexplainedPointCountDifference"; return false; }
                if (a.PointCount != b.PointCount) MirrorV7Diagnostics.Log("[PHASE6A][API_QUIRK] name=\"" + a.FeatureName + "\" rule=SynthesizedSlotPointEnumeration sourceSynthetic=" + sourceSynthetic + " copySynthetic=" + copySynthetic);
                if (!ComparePointSet(sourceRegular, copyRegular, a.FeatureName, out reason)) return false;
            }
            else
            {
                if (a.PointCount != b.PointCount) { reason = "capturedPointCount"; return false; }
                int ignoredSyntheticA, ignoredSyntheticB;
                List<SketchEntitySnapshotV7> ap = SlotCopyComparisonV7.GetRegularPoints(a, LengthAbsoluteTolerance, out ignoredSyntheticA);
                List<SketchEntitySnapshotV7> bp = SlotCopyComparisonV7.GetRegularPoints(b, LengthAbsoluteTolerance, out ignoredSyntheticB);
                if (!ComparePointSet(ap, bp, a.FeatureName, out reason)) return false;
            }
            if (a.DeclaredSegmentCount != b.DeclaredSegmentCount) MirrorV7Diagnostics.Log("[PHASE6A][API_QUIRK] name=\"" + a.FeatureName + "\" rule=DeclaredSegmentEnumeration");
            if (!CompareSegments(a, b, out reason)) return false;
            for (int i = 0; i < a.Dimensions.Count; i++) if (!NearlyEqual(a.Dimensions[i].SystemValue, b.Dimensions[i].SystemValue, LengthAbsoluteTolerance, LengthRelativeTolerance) || a.Dimensions[i].DrivenState != b.Dimensions[i].DrivenState || a.Dimensions[i].IsReference != b.Dimensions[i].IsReference) { reason = "dimension"; return false; }
            for (int i = 0; i < a.Constraints.Count; i++) if (a.Constraints[i].RelationType != b.Constraints[i].RelationType || a.Constraints[i].EntityCount != b.Constraints[i].EntityCount) { reason = "relation"; return false; }
            MirrorV7Diagnostics.Log("[PHASE6A][SKETCH_GEOMETRY_OK] name=\"" + a.FeatureName + "\" slots=" + a.Slots.Count + " regularPoints=" + a.PointCount);
            return true;
        }

        private static bool ComparePointSet(List<SketchEntitySnapshotV7> a, List<SketchEntitySnapshotV7> b, string name, out string reason)
        {
            EquivalenceMatchResultV7 matching = BipartiteEquivalenceMatcherV7.Match(a, b, SamePoint);
            MirrorV7Diagnostics.Log("[PHASE6A][POINT_EQUIVALENCE] sketch=\"" + name + "\" sourcePoints=" + a.Count + " copyPoints=" + b.Count + " perfectMatching=" + matching.HasPerfectMatching + " multiCandidatePoints=" + matching.LeftWithMultipleCandidates + " pointsWithoutCandidates=" + matching.LeftWithoutCandidates + " scope=GEOMETRY_ONLY");
            if (!matching.HasPerfectMatching) { reason = "pointGeometryNoPerfectMatching"; MirrorV7Diagnostics.Log("[PHASE6A][SKETCH_GEOMETRY_MISMATCH] name=\"" + name + "\" kind=point"); return false; }
            reason = null; return true;
        }
        private static bool SamePoint(SketchEntitySnapshotV7 a, SketchEntitySnapshotV7 b) { return Finite(a.SketchX) && Finite(a.SketchY) && Finite(a.SketchZ) && Finite(b.SketchX) && Finite(b.SketchY) && Finite(b.SketchZ) && a.PointType == b.PointType && a.IsConstruction == b.IsConstruction && a.HasModelCoords == b.HasModelCoords && NearlyEqual(a.SketchX, b.SketchX, LengthAbsoluteTolerance, LengthRelativeTolerance) && NearlyEqual(a.SketchY, b.SketchY, LengthAbsoluteTolerance, LengthRelativeTolerance) && NearlyEqual(a.SketchZ, b.SketchZ, LengthAbsoluteTolerance, LengthRelativeTolerance) && (!a.HasModelCoords || (Finite(a.ModelX) && Finite(a.ModelY) && Finite(a.ModelZ) && Finite(b.ModelX) && Finite(b.ModelY) && Finite(b.ModelZ) && NearlyEqual(a.ModelX, b.ModelX, LengthAbsoluteTolerance, LengthRelativeTolerance) && NearlyEqual(a.ModelY, b.ModelY, LengthAbsoluteTolerance, LengthRelativeTolerance) && NearlyEqual(a.ModelZ, b.ModelZ, LengthAbsoluteTolerance, LengthRelativeTolerance))); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool CompareSegments(SketchSnapshotV7 a, SketchSnapshotV7 b, out string reason) { reason = null; List<SketchEntitySnapshotV7> x = new List<SketchEntitySnapshotV7>(), y = new List<SketchEntitySnapshotV7>(); foreach (SketchEntitySnapshotV7 e in a.Entities) if (e.Kind == SketchEntityKindV7.Segment) x.Add(e); foreach (SketchEntitySnapshotV7 e in b.Entities) if (e.Kind == SketchEntityKindV7.Segment) y.Add(e); if (x.Count != y.Count) { reason = "segmentCount"; return false; } for (int i = 0; i < x.Count; i++) if (x[i].SegmentType != y[i].SegmentType || x[i].IsConstruction != y[i].IsConstruction || !NearlyEqual(x[i].Length, y[i].Length, LengthAbsoluteTolerance, LengthRelativeTolerance)) { reason = "segmentGeometry"; MirrorV7Diagnostics.Log("[PHASE6A][SKETCH_GEOMETRY_MISMATCH] name=\"" + a.FeatureName + "\" kind=segment index=" + i); return false; } return true; }

        private static bool MatchBodies(List<Body2> source, List<Body2> copy, out int matched, out string error)
        {
            matched = 0; error = null; if (source.Count != copy.Count) { error = "Body count mismatch."; return false; }
            bool[] used = new bool[copy.Count];
            for (int i = 0; i < source.Count; i++)
            {
                BodyMirrorVerifierV7.Measurements sm = BodyMirrorVerifierV7.Measure(source[i]); int candidate = -1;
                for (int j = 0; j < copy.Count; j++) { if (used[j]) continue; BodyMirrorVerifierV7.Measurements cm = BodyMirrorVerifierV7.Measure(copy[j]); if (NearlyEqual(sm.Volume, cm.Volume, VolumeAbsoluteTolerance, VolumeRelativeTolerance) && NearlyEqual(sm.Area, cm.Area, AreaAbsoluteTolerance, AreaRelativeTolerance) && NearlyEqual(sm.Centroid[0], cm.Centroid[0], LengthAbsoluteTolerance, LengthRelativeTolerance) && NearlyEqual(sm.Centroid[1], cm.Centroid[1], LengthAbsoluteTolerance, LengthRelativeTolerance) && NearlyEqual(sm.Centroid[2], cm.Centroid[2], LengthAbsoluteTolerance, LengthRelativeTolerance)) { if (candidate >= 0) { error = "Ambiguous body pairing at source index " + i; return false; } candidate = j; } }
                if (candidate < 0) { error = "No body candidate for source index " + i; return false; } used[candidate] = true; matched++;
            }
            return true;
        }
        private static List<Body2> GetBodies(ModelDoc2 doc) { List<Body2> list = new List<Body2>(); PartDoc p = doc as PartDoc; object[] a = p == null ? null : p.GetBodies2((int)SolidWorks.Interop.swconst.swBodyType_e.swSolidBody, false) as object[]; if (a != null) foreach (object o in a) { Body2 b = o as Body2; if (b != null) list.Add(b); } return list; }
        private static int CountRole(MirrorV7ModelGraph graph, MirrorV7FeatureRole role) { int n = 0; foreach (MirrorV7FeatureNode node in graph.Nodes) if (node != null && node.Role == role) n++; return n; }
        private static string Identity(MirrorV7FeatureNode node) { return (node == null ? "<null>" : (node.Name ?? "") + "|" + (node.TypeName ?? "")); }
        private static bool NearlyEqual(double a, double b, double absoluteTolerance, double relativeTolerance) { if (double.IsNaN(a) || double.IsNaN(b) || double.IsInfinity(a) || double.IsInfinity(b)) return false; return Math.Abs(a - b) <= absoluteTolerance + relativeTolerance * Math.Max(Math.Abs(a), Math.Abs(b)); }
    }
}
