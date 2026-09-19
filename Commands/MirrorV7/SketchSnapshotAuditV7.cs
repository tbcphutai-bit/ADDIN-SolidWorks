using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.MirrorV7
{
    /// <summary>
    /// Phase 5A: read-only sketch snapshot + integrity audit.
    ///
    /// For every Sketch / 3D Sketch node in the Phase 2 graph this audit:
    ///   1. captures a full snapshot,
    ///   2. captures a second independent snapshot of the same untouched sketch,
    ///   3. validates the first snapshot against what SolidWorks declared,
    ///   4. proves the two captures agree exactly (structural stability).
    ///
    /// Nothing is mutated. No sketch is opened for edit. No selection is made.
    /// </summary>
    public static class SketchSnapshotAuditV7
    {
        private const int ConstraintLogCap = 60;
        private const int DimensionLogCap = 40;
        private const int SlotLogCap = 20;

        /// <summary>
        /// When true, a sketch feature that cannot be captured fails the audit even if it is
        /// suppressed. Default false: suppressed features may legitimately refuse their sketch
        /// object, and Phase 5A must report that rather than block on it.
        /// </summary>
        public static bool TreatSuppressedCaptureFailureAsFatal = false;

        public static SketchSnapshotAuditResultV7 RunOrThrow(MirrorV7Context context)
        {
            if (context == null) throw new ArgumentNullException("context");
            if (context.PartDoc == null) throw new InvalidOperationException("PHASE5 requires PartDoc.");
            if (context.Graph == null) throw new InvalidOperationException("PHASE5 requires the Phase 2 graph.");

            SketchSnapshotAuditResultV7 result = new SketchSnapshotAuditResultV7();
            bool matrixLogged = false;

            foreach (MirrorV7FeatureNode node in context.Graph.Nodes)
            {
                if (node == null || node.Role != MirrorV7FeatureRole.Sketch) continue;
                result.SketchNodes++;

                SketchSnapshotV7 first = SketchSnapshotServiceV7.Capture(context, node);
                SketchSnapshotV7 second = SketchSnapshotServiceV7.Capture(context, node);
                if (!matrixLogged && first.ModelToSketchTransform != null)
                {
                    MirrorV7Diagnostics.Log("[PHASE5][RAW_TRANSFORM] sketch=" + node.Name + " values=" + string.Join(",", Array.ConvertAll(first.ModelToSketchTransform, v => v.ToString("G17", CultureInfo.InvariantCulture))));
                    matrixLogged = true;
                }

                if (!first.CaptureSucceeded)
                {
                    HandleCaptureFailure(node, first, result);
                    continue;
                }

                result.Captured++;
                if (node.IsSuppressed) result.CapturedSuppressed++;
                if (first.Is3D) result.Captured3D++;
                context.SketchSnapshots.Add(first);

                SketchIntegrityReportV7 integrity = SketchIntegrityValidatorV7.Validate(first);
                SketchIntegrityReportV7 stability = SketchIntegrityValidatorV7.VerifyStability(first, second);

                bool structurallyStable = !stability.HasErrors;
                bool persistRefStable = !ContainsPersistRefInstability(stability);

                if (structurallyStable) result.Stable++;
                else result.Unstable++;
                if (!persistRefStable) result.PersistRefUnstable++;

                Accumulate(result, first);

                LogSketchSummary(node, first, integrity, structurallyStable, persistRefStable);
                LogSketchDetail(first);

                CountReport(integrity, result);
                CountReport(stability, result);

                LogReport("[PHASE5][INTEGRITY]", node.Name, integrity);
                LogReport("[PHASE5][STABILITY]", node.Name, stability);
            }

            if (result.SketchNodes == 0)
                MirrorV7Diagnostics.Log("[PHASE5][WARNING] No Sketch-role nodes in the Phase 2 graph; nothing was snapshotted.");

            LogSummary(result);

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    "PHASE5 sketch snapshot audit failed. sketchNodes=" + result.SketchNodes +
                    " captured=" + result.Captured +
                    " captureFailed=" + result.CaptureFailed +
                    " unstable=" + result.Unstable +
                    " integrityErrors=" + result.IntegrityErrors);
            }

            return result;
        }

        // ------------------------------------------------------------------ failure

        private static void HandleCaptureFailure(
            MirrorV7FeatureNode node,
            SketchSnapshotV7 snapshot,
            SketchSnapshotAuditResultV7 result)
        {
            string reason = snapshot.CaptureFailure ?? "<no reason>";
            bool tolerated = node.IsSuppressed && !TreatSuppressedCaptureFailureAsFatal;

            if (tolerated)
            {
                result.CaptureFailedOnSuppressed++;
                MirrorV7Diagnostics.Log(
                    "[PHASE5][SKETCH_CAPTURE_TOLERATED] name=\"" + node.Name + "\" type=\"" + node.TypeName +
                    "\" suppressed=True reason=" + reason +
                    " :: accepted because the feature is suppressed; flip TreatSuppressedCaptureFailureAsFatal to make this fatal.");
                return;
            }

            result.CaptureFailed++;
            result.IntegrityErrors++;
            MirrorV7Diagnostics.Log(
                "[PHASE5][SKETCH_CAPTURE_FAILED] name=\"" + node.Name + "\" type=\"" + node.TypeName +
                "\" suppressed=" + node.IsSuppressed + " reason=" + reason);
        }

        // ------------------------------------------------------------- accumulation

        private static void Accumulate(SketchSnapshotAuditResultV7 result, SketchSnapshotV7 snapshot)
        {
            result.TotalPoints += snapshot.PointCount;
            result.TotalSegments += snapshot.SegmentCount;
            result.TotalConstructionSegments += snapshot.ConstructionSegmentCount;
            result.TotalSlots += snapshot.Slots.Count;

            result.TotalRelations += snapshot.Constraints.Count;
            result.TotalDanglingRelations += snapshot.RelationCountDangling;
            result.TotalOverDefiningRelations += snapshot.RelationCountOverDefining;
            result.TotalExternalRelations += snapshot.RelationCountExternal;
            result.TotalLockedRelations += snapshot.RelationCountLocked;
            result.TotalBrokenRelations += snapshot.RelationCountBroken;

            result.TotalDimensions += snapshot.Dimensions.Count;
            if (snapshot.HasModelToSketchTransform) result.TransformAvailable++;

            foreach (SketchEntitySnapshotV7 entity in snapshot.Entities)
            {
                if (entity.HasIds) result.EntitiesWithIds++;
                else result.EntitiesWithoutIds++;

                if (entity.Kind == SketchEntityKindV7.Point)
                {
                    if (entity.HasModelCoords) result.PointsWithModelCoords++;
                    else result.PointsWithoutModelCoords++;
                }

                if (entity.PersistRefBytes > 0) result.EntityPersistCaptured++;
                else result.EntityPersistUnavailable++;
            }

            foreach (SketchDimensionSnapshotV7 dimension in snapshot.Dimensions)
            {
                if (dimension.IsDangling) result.TotalDanglingDimensions++;
                if (dimension.PersistRefBytes > 0) result.DimensionPersistCaptured++;
                else result.DimensionPersistUnavailable++;
            }
        }

        private static void CountReport(SketchIntegrityReportV7 report, SketchSnapshotAuditResultV7 result)
        {
            if (report == null) return;
            result.IntegrityErrors += report.Errors.Count;
            result.ApiQuirks += report.ApiQuirks.Count;
            result.IntegrityWarnings += report.Warnings.Count;
        }

        private static bool ContainsPersistRefInstability(SketchIntegrityReportV7 report)
        {
            if (report == null) return false;
            foreach (string warning in report.Warnings)
                if (warning != null && warning.IndexOf("persistRefSignature UNSTABLE", StringComparison.Ordinal) >= 0)
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ logging

        private static void LogSketchSummary(
            MirrorV7FeatureNode node,
            SketchSnapshotV7 snapshot,
            SketchIntegrityReportV7 integrity,
            bool structurallyStable,
            bool persistRefStable)
        {
            MirrorV7Diagnostics.Log(string.Format(
                "[PHASE5][SKETCH] #{0} name=\"{1}\" type=\"{2}\" role={3} suppressed={4} is3D={5} derived={6} shared={7} plane=\"{8}\"(swSelType={9}) transform={10} scale={11} featurePersistBytes={12}",
                node.TreeOrder.ToString(CultureInfo.InvariantCulture),
                node.Name ?? "",
                node.TypeName ?? "",
                node.Role,
                snapshot.IsSuppressed,
                snapshot.Is3D,
                snapshot.IsDerived,
                snapshot.IsShared,
                snapshot.ReferenceEntityName ?? "",
                snapshot.ReferenceEntityType.ToString(CultureInfo.InvariantCulture),
                snapshot.HasModelToSketchTransform ? "OK" : "MISSING",
                Num(snapshot.TransformScale),
                snapshot.FeaturePersistRefBytes.ToString(CultureInfo.InvariantCulture)));

            MirrorV7Diagnostics.Log(string.Format(
                "[PHASE5][SKETCH_STATS] name=\"{0}\" points={1}/{2} segments={3}/{4} constructionSegments={5} slots={6}/{7} relations={8}/{9} dangling={10} overDefining={11} external={12} locked={13} broken={14} dimensions={15} entityIds={16}/{17} modelCoords={18}/{19} captureWarnings={20}",
                node.Name ?? "",
                snapshot.PointCount.ToString(CultureInfo.InvariantCulture),
                snapshot.DeclaredPointCount.ToString(CultureInfo.InvariantCulture),
                snapshot.SegmentCount.ToString(CultureInfo.InvariantCulture),
                snapshot.DeclaredSegmentCount.ToString(CultureInfo.InvariantCulture),
                snapshot.ConstructionSegmentCount.ToString(CultureInfo.InvariantCulture),
                snapshot.Slots.Count.ToString(CultureInfo.InvariantCulture),
                snapshot.DeclaredSlotCount.ToString(CultureInfo.InvariantCulture),
                snapshot.Constraints.Count.ToString(CultureInfo.InvariantCulture),
                snapshot.RelationCountAll.ToString(CultureInfo.InvariantCulture),
                snapshot.RelationCountDangling.ToString(CultureInfo.InvariantCulture),
                snapshot.RelationCountOverDefining.ToString(CultureInfo.InvariantCulture),
                snapshot.RelationCountExternal.ToString(CultureInfo.InvariantCulture),
                snapshot.RelationCountLocked.ToString(CultureInfo.InvariantCulture),
                snapshot.RelationCountBroken.ToString(CultureInfo.InvariantCulture),
                snapshot.Dimensions.Count.ToString(CultureInfo.InvariantCulture),
                CountWithIds(snapshot).ToString(CultureInfo.InvariantCulture),
                snapshot.Entities.Count.ToString(CultureInfo.InvariantCulture),
                CountWithModelCoords(snapshot).ToString(CultureInfo.InvariantCulture),
                snapshot.PointCount.ToString(CultureInfo.InvariantCulture),
                snapshot.Warnings.Count.ToString(CultureInfo.InvariantCulture)));

            MirrorV7Diagnostics.Log(string.Format(
                "[PHASE5][SKETCH_SIGNATURE] name=\"{0}\" structural={1} stable={2} persistRef={3} persistRefStable={4} integrityErrors={5} integrityWarnings={6}",
                node.Name ?? "",
                SketchSnapshotServiceV7.HashText(snapshot.BuildStructuralSignature()),
                structurallyStable,
                SketchSnapshotServiceV7.HashText(snapshot.BuildPersistRefSignature()),
                persistRefStable,
                integrity.Errors.Count.ToString(CultureInfo.InvariantCulture),
                integrity.Warnings.Count.ToString(CultureInfo.InvariantCulture)));
        }

        private static void LogSketchDetail(SketchSnapshotV7 snapshot)
        {
            string who = snapshot.FeatureName ?? "";

            int cap = Math.Min(SketchSnapshotServiceV7.EntityLogCap, snapshot.Entities.Count);
            for (int i = 0; i < cap; i++)
            {
                SketchEntitySnapshotV7 entity = snapshot.Entities[i];
                if (entity.Kind == SketchEntityKindV7.Point)
                {
                    MirrorV7Diagnostics.Log(string.Format(
                        "[PHASE5][POINT] sketch=\"{0}\" idx={1} key={2} pointType={3} sketch=({4},{5},{6}) model=({7},{8},{9}) hasModel={10} persistBytes={11} persist={12}",
                        who,
                        entity.Index.ToString(CultureInfo.InvariantCulture),
                        entity.EntityKey,
                        entity.PointType.ToString(CultureInfo.InvariantCulture),
                        Num(entity.SketchX), Num(entity.SketchY), Num(entity.SketchZ),
                        Num(entity.ModelX), Num(entity.ModelY), Num(entity.ModelZ),
                        entity.HasModelCoords,
                        entity.PersistRefBytes.ToString(CultureInfo.InvariantCulture),
                        entity.PersistRefSignature ?? "<none>"));
                }
                else
                {
                    MirrorV7Diagnostics.Log(string.Format(
                        "[PHASE5][SEGMENT] sketch=\"{0}\" idx={1} key={2} segType={3} name=\"{4}\" length={5} construction={6} bendLine={7} slot={8} status={9} persistBytes={10} persist={11}",
                        who,
                        entity.Index.ToString(CultureInfo.InvariantCulture),
                        entity.EntityKey,
                        SketchSnapshotServiceV7.NameOfSegmentType(entity.SegmentType),
                        entity.SegmentName ?? "",
                        Num(entity.Length),
                        entity.IsConstruction,
                        entity.IsBendLine,
                        entity.BelongsToSlot,
                        entity.SegmentStatus.ToString(CultureInfo.InvariantCulture),
                        entity.PersistRefBytes.ToString(CultureInfo.InvariantCulture),
                        entity.PersistRefSignature ?? "<none>"));
                }
            }
            if (snapshot.Entities.Count > cap)
                MirrorV7Diagnostics.Log("[PHASE5][POINT/SEGMENT_TRUNCATED] sketch=\"" + who + "\" shown=" + cap + " total=" + snapshot.Entities.Count);

            cap = Math.Min(ConstraintLogCap, snapshot.Constraints.Count);
            for (int i = 0; i < cap; i++)
                LogConstraint(who, "[PHASE5][RELATION]", snapshot.Constraints[i]);
            if (snapshot.Constraints.Count > cap)
                MirrorV7Diagnostics.Log("[PHASE5][RELATION_TRUNCATED] sketch=\"" + who + "\" shown=" + cap + " total=" + snapshot.Constraints.Count);

            cap = Math.Min(ConstraintLogCap, snapshot.ExternalRelations.Count);
            for (int i = 0; i < cap; i++)
                LogConstraint(who, "[PHASE5][EXTERNAL_REF]", snapshot.ExternalRelations[i]);

            cap = Math.Min(DimensionLogCap, snapshot.Dimensions.Count);
            for (int i = 0; i < cap; i++)
            {
                SketchDimensionSnapshotV7 dimension = snapshot.Dimensions[i];
                MirrorV7Diagnostics.Log(string.Format(
                    "[PHASE5][DIMENSION] sketch=\"{0}\" idx={1} name=\"{2}\" fullName=\"{3}\" value={4} systemValue={5} displayType={6} drivenState={7} isReference={8} isDangling={9} persistBytes={10} persist={11}",
                    who,
                    dimension.Index.ToString(CultureInfo.InvariantCulture),
                    dimension.Name ?? "",
                    dimension.FullName ?? "",
                    Num(dimension.Value),
                    Num(dimension.SystemValue),
                    dimension.DisplayType.ToString(CultureInfo.InvariantCulture),
                    dimension.DrivenState.ToString(CultureInfo.InvariantCulture),
                    dimension.IsReference,
                    dimension.IsDangling,
                    dimension.PersistRefBytes.ToString(CultureInfo.InvariantCulture),
                    dimension.PersistRefSignature ?? "<none>"));
            }
            if (snapshot.Dimensions.Count > cap)
                MirrorV7Diagnostics.Log("[PHASE5][DIMENSION_TRUNCATED] sketch=\"" + who + "\" shown=" + cap + " total=" + snapshot.Dimensions.Count);

            cap = Math.Min(SlotLogCap, snapshot.Slots.Count);
            for (int i = 0; i < cap; i++)
            {
                SketchSlotSnapshotV7 slot = snapshot.Slots[i];
                MirrorV7Diagnostics.Log(string.Format(
                    "[PHASE5][SLOT] sketch=\"{0}\" idx={1} creationType={2} lengthType={3} length={4} width={5} centerArcDirection={6} pointKeys=[{7}]",
                    who,
                    slot.Index.ToString(CultureInfo.InvariantCulture),
                    slot.CreationType.ToString(CultureInfo.InvariantCulture),
                    slot.LengthType.ToString(CultureInfo.InvariantCulture),
                    Num(slot.Length),
                    Num(slot.Width),
                    slot.CenterArcDirection.ToString(CultureInfo.InvariantCulture),
                    string.Join(",", slot.SlotPointKeys.ToArray())));
            }

            foreach (string warning in snapshot.Warnings)
                MirrorV7Diagnostics.Log("[PHASE5][CAPTURE_WARNING] sketch=\"" + who + "\" " + warning);
        }

        private static void LogConstraint(string who, string tag, SketchConstraintSnapshotV7 constraint)
        {
            StringBuilder entityTypes = new StringBuilder();
            for (int i = 0; i < constraint.EntityTypes.Count; i++)
            {
                if (i > 0) entityTypes.Append(",");
                entityTypes.Append(SketchSnapshotServiceV7.NameOfRelationEntityType(constraint.EntityTypes[i]));
            }

            MirrorV7Diagnostics.Log(string.Format(
                "{0} sketch=\"{1}\" idx={2} type={3}({4}) entityCount={5} entityTypes=[{6}] entityKeys=[{7}]",
                tag,
                who,
                constraint.Index.ToString(CultureInfo.InvariantCulture),
                constraint.RelationTypeName ?? "",
                constraint.RelationType.ToString(CultureInfo.InvariantCulture),
                constraint.EntityCount.ToString(CultureInfo.InvariantCulture),
                entityTypes.ToString(),
                string.Join(",", constraint.EntityKeys.ToArray())));
        }

        private static void LogReport(string tag, string sketchName, SketchIntegrityReportV7 report)
        {
            if (report == null) return;
            foreach (string error in report.Errors)
                MirrorV7Diagnostics.Log(tag + "[ERROR] sketch=\"" + (sketchName ?? "") + "\" " + error);
            foreach (string warning in report.Warnings)
                MirrorV7Diagnostics.Log(tag + "[WARNING] sketch=\"" + (sketchName ?? "") + "\" " + warning);
            foreach (string quirk in report.ApiQuirks)
                MirrorV7Diagnostics.Log(tag + "[API_QUIRK] sketch=\"" + (sketchName ?? "") + "\" " + quirk);
        }

        private static void LogSummary(SketchSnapshotAuditResultV7 r)
        {
            MirrorV7Diagnostics.Log("[PHASE5][SUMMARY] sketchNodes=" + r.SketchNodes +
                                    " captured=" + r.Captured +
                                    " capturedSuppressed=" + r.CapturedSuppressed +
                                    " captured3D=" + r.Captured3D +
                                    " captureFailed=" + r.CaptureFailed +
                                    " captureToleratedSuppressed=" + r.CaptureFailedOnSuppressed);

            MirrorV7Diagnostics.Log("[PHASE5][SUMMARY_GEOMETRY] points=" + r.TotalPoints +
                                    " segments=" + r.TotalSegments +
                                    " constructionSegments=" + r.TotalConstructionSegments +
                                    " slots=" + r.TotalSlots +
                                    " transformAvailable=" + r.TransformAvailable +
                                    " pointsWithModelCoords=" + r.PointsWithModelCoords +
                                    " pointsWithoutModelCoords=" + r.PointsWithoutModelCoords);

            MirrorV7Diagnostics.Log("[PHASE5][SUMMARY_CONSTRAINTS] relations=" + r.TotalRelations +
                                    " dangling=" + r.TotalDanglingRelations +
                                    " overDefining=" + r.TotalOverDefiningRelations +
                                    " external=" + r.TotalExternalRelations +
                                    " locked=" + r.TotalLockedRelations +
                                    " broken=" + r.TotalBrokenRelations +
                                    " dimensions=" + r.TotalDimensions +
                                    " danglingDimensions=" + r.TotalDanglingDimensions);

            MirrorV7Diagnostics.Log("[PHASE5][SUMMARY_IDENTITY] entitiesWithIds=" + r.EntitiesWithIds +
                                    " entitiesWithoutIds=" + r.EntitiesWithoutIds +
                                    " entityPersistCaptured=" + r.EntityPersistCaptured +
                                    " entityPersistUnavailable=" + r.EntityPersistUnavailable +
                                    " dimensionPersistCaptured=" + r.DimensionPersistCaptured +
                                    " dimensionPersistUnavailable=" + r.DimensionPersistUnavailable);

            MirrorV7Diagnostics.Log("[PHASE5][SUMMARY_STABILITY] stable=" + r.Stable +
                                    " unstable=" + r.Unstable +
                                    " persistRefUnstable=" + r.PersistRefUnstable +
                                    " integrityErrors=" + r.IntegrityErrors +
                                    " integrityWarnings=" + r.IntegrityWarnings +
                                    " apiQuirks=" + r.ApiQuirks +
                                    " result=" + (r.Success ? "PASS" : "FAIL"));
        }

        private static int CountWithIds(SketchSnapshotV7 snapshot)
        {
            int n = 0;
            foreach (SketchEntitySnapshotV7 entity in snapshot.Entities) if (entity.HasIds) n++;
            return n;
        }

        private static int CountWithModelCoords(SketchSnapshotV7 snapshot)
        {
            int n = 0;
            foreach (SketchEntitySnapshotV7 entity in snapshot.Entities)
                if (entity.Kind == SketchEntityKindV7.Point && entity.HasModelCoords) n++;
            return n;
        }

        private static string Num(double value)
        {
            return value.ToString("G9", CultureInfo.InvariantCulture);
        }
    }
}
