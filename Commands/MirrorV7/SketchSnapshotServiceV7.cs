using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7
{
    /// <summary>
    /// Phase 5A read-only sketch capture.
    ///
    /// Hard rules for this service:
    ///   - getters only. No InsertSketch/EditSketch, no selection, no rebuild, no delete,
    ///     no DeleteAllRelations, no dimension deletion, no Marshal.ReleaseComObject.
    ///   - the source Part must not become dirty and its feature count must not change.
    ///   - every SolidWorks call is individually guarded so that one unsupported entity
    ///     degrades the snapshot instead of aborting the audit.
    /// </summary>
    public static class SketchSnapshotServiceV7
    {
        /// <summary>Per-sketch cap for entity-level log lines. Counts are always complete.</summary>
        public const int EntityLogCap = 60;

        public static SketchSnapshotV7 Capture(MirrorV7Context context, MirrorV7FeatureNode node)
        {
            if (context == null) throw new ArgumentNullException("context");
            if (node == null) throw new ArgumentNullException("node");
            if (context.PartDoc == null) throw new InvalidOperationException("PHASE5 requires PartDoc.");

            SketchSnapshotV7 snapshot = new SketchSnapshotV7
            {
                FeatureName = node.Name ?? "",
                FeatureTypeName = node.TypeName ?? "",
                TreeOrder = node.TreeOrder,
                IsSuppressed = node.IsSuppressed
            };

            if (node.PersistentReference != null)
            {
                snapshot.FeaturePersistRefBytes = node.PersistentReference.ByteCount;
                snapshot.FeaturePersistRefSignature = HashBytes(node.PersistentReference.Data);
            }

            ModelDoc2 model = context.PartDoc;
            Sketch sketch = null;
            try { sketch = node.Feature == null ? null : node.Feature.GetSpecificFeature2() as Sketch; }
            catch (Exception ex) { snapshot.CaptureFailure = "GetSpecificFeature2:" + ex.Message; }

            if (sketch == null)
            {
                snapshot.CaptureSucceeded = false;
                if (string.IsNullOrEmpty(snapshot.CaptureFailure)) snapshot.CaptureFailure = "SketchObjectUnavailable";
                return snapshot;
            }

            MathUtility mathUtility = ResolveMathUtility(context);
            if (mathUtility == null)
                throw new InvalidOperationException(
                    "PHASE5 requires ISldWorks.GetMathUtility(); without it sketch coordinates cannot be converted to model coordinates.");
            MathTransform sketchToModel = null;

            CaptureFlags(snapshot, sketch);
            CaptureReferenceEntity(snapshot, sketch);
            sketchToModel = CaptureTransform(snapshot, sketch, mathUtility);
            CapturePoints(snapshot, model, sketch, sketchToModel, mathUtility);
            CaptureSegments(snapshot, model, sketch);
            CaptureRelations(snapshot, sketch);
            CaptureDimensions(snapshot, model, node.Feature);
            CaptureSlots(snapshot, sketch);

            snapshot.CaptureSucceeded = true;
            return snapshot;
        }

        // ------------------------------------------------------------------ flags

        private static void CaptureFlags(SketchSnapshotV7 snapshot, Sketch sketch)
        {
            try { snapshot.Is3D = sketch.Is3D(); }
            catch (Exception ex) { Warn(snapshot, "Is3D", ex); }
            try { snapshot.IsDerived = sketch.IsDerived(); }
            catch (Exception ex) { Warn(snapshot, "IsDerived", ex); }
            try { snapshot.IsShared = sketch.IsShared(); }
            catch (Exception ex) { Warn(snapshot, "IsShared", ex); }
        }

        // ------------------------------------------------------- plane / reference

        private static void CaptureReferenceEntity(SketchSnapshotV7 snapshot, Sketch sketch)
        {
            try
            {
                int referenceType = 0;
                object entity = sketch.GetReferenceEntity(ref referenceType);
                snapshot.ReferenceEntityType = referenceType;
                if (entity != null)
                {
                    snapshot.HasReferenceEntity = true;
                    Feature referenceFeature = entity as Feature;
                    if (referenceFeature != null) snapshot.ReferenceEntityName = SafeFeatureName(referenceFeature);
                    else snapshot.ReferenceEntityName = entity.GetType().Name;
                }
                else
                {
                    snapshot.HasReferenceEntity = false;
                    snapshot.ReferenceEntityName = "";
                }
            }
            catch (Exception ex) { Warn(snapshot, "GetReferenceEntity", ex); }
        }

        // ------------------------------------------------------- ModelToSketchXform

        private static MathTransform CaptureTransform(SketchSnapshotV7 snapshot, Sketch sketch, MathUtility mathUtility)
        {
            MathTransform modelToSketch = null;
            try { modelToSketch = sketch.ModelToSketchTransform; }
            catch (Exception ex) { Warn(snapshot, "ModelToSketchTransform", ex); }

            if (modelToSketch == null) return null;

            try
            {
                double[] arrayData = modelToSketch.ArrayData as double[];
                if (arrayData != null && arrayData.Length >= 16)
                {
                    snapshot.ModelToSketchTransform = arrayData;
                    snapshot.HasModelToSketchTransform = true;
                    snapshot.TransformRawScaleElement = arrayData[TransformRawScaleIndex];

                    double crossCheck;
                    snapshot.TransformScale = DeriveTransformScale(arrayData, out crossCheck);
                    snapshot.TransformScaleCrossCheck = crossCheck;
                    snapshot.TransformScaleConsistent =
                        Math.Abs(snapshot.TransformScale - crossCheck) <= ScaleConsistencyTolerance *
                        Math.Max(1.0, Math.Abs(snapshot.TransformScale));
                }
                else
                {
                    Warn(snapshot, "ModelToSketchTransformArray", "unexpected length=" + (arrayData == null ? -1 : arrayData.Length));
                }
            }
            catch (Exception ex) { Warn(snapshot, "ModelToSketchTransformArray", ex); }

            if (mathUtility == null) return null;

            try { return modelToSketch.Inverse() as MathTransform; }
            catch (Exception ex) { Warn(snapshot, "SketchToModelTransform", ex); return null; }
        }

        /// <summary>
        /// Offset of the 3x3 rotation block inside IMathTransform.ArrayData.
        /// Standard SolidWorks layout: [0..8] rotation (row-major), [9..11] translation,
        /// [12..14] unused, [15] scale factor.
        ///
        /// If a future run's raw-matrix log shows the rotation living elsewhere, this offset and
        /// <see cref="TransformRawScaleIndex"/> are the only two places that need to change.
        /// </summary>
        private const int RotationBlockOffset = 0;

        /// <summary>ArrayData element SolidWorks reserves for the scale factor.</summary>
        private const int TransformRawScaleIndex = 15;

        /// <summary>Relative tolerance for agreeing the two independent scale derivations.</summary>
        private const double ScaleConsistencyTolerance = 1e-9;

        /// <summary>
        /// Uniform scale of a transform, derived from its 3x3 rotation block two independent ways:
        ///   |det(R)|^(1/3)                     - the volume-scaling interpretation
        ///   mean of the three column norms     - the basis-vector interpretation
        ///
        /// Both are returned so the caller can detect a matrix that is not a similarity transform.
        /// The determinant form is authoritative; a reflection has det = -1, which the absolute
        /// value absorbs.
        /// </summary>
        private static double DeriveTransformScale(double[] m, out double columnNormScale)
        {
            double r00 = m[RotationBlockOffset + 0], r01 = m[RotationBlockOffset + 1], r02 = m[RotationBlockOffset + 2];
            double r10 = m[RotationBlockOffset + 3], r11 = m[RotationBlockOffset + 4], r12 = m[RotationBlockOffset + 5];
            double r20 = m[RotationBlockOffset + 6], r21 = m[RotationBlockOffset + 7], r22 = m[RotationBlockOffset + 8];

            double det = r00 * (r11 * r22 - r12 * r21)
                       - r01 * (r10 * r22 - r12 * r20)
                       + r02 * (r10 * r21 - r11 * r20);

            double col0 = Math.Sqrt(r00 * r00 + r10 * r10 + r20 * r20);
            double col1 = Math.Sqrt(r01 * r01 + r11 * r11 + r21 * r21);
            double col2 = Math.Sqrt(r02 * r02 + r12 * r12 + r22 * r22);
            columnNormScale = (col0 + col1 + col2) / 3.0;

            double absDet = Math.Abs(det);
            return absDet <= 0.0 ? 0.0 : Math.Pow(absDet, 1.0 / 3.0);
        }

        // ------------------------------------------------------------------ points

        private static void CapturePoints(
            SketchSnapshotV7 snapshot,
            ModelDoc2 model,
            Sketch sketch,
            MathTransform sketchToModel,
            MathUtility mathUtility)
        {
            try { snapshot.DeclaredPointCount = sketch.GetSketchPointsCount2(); }
            catch (Exception ex) { Warn(snapshot, "GetSketchPointsCount2", ex); }

            object[] points = null;
            try { points = sketch.GetSketchPoints2() as object[]; }
            catch (Exception ex) { Warn(snapshot, "GetSketchPoints2", ex); }
            if (points == null) return;

            for (int i = 0; i < points.Length; i++)
            {
                SketchPoint point = points[i] as SketchPoint;
                if (point == null)
                {
                    Warn(snapshot, "SketchPointCast", "index=" + i);
                    continue;
                }

                SketchEntitySnapshotV7 entity = new SketchEntitySnapshotV7
                {
                    Kind = SketchEntityKindV7.Point,
                    Index = i
                };

                ReadPointIds(point, entity);
                try { entity.SketchX = point.X; } catch (Exception ex) { Warn(snapshot, "Point.X", ex); }
                try { entity.SketchY = point.Y; } catch (Exception ex) { Warn(snapshot, "Point.Y", ex); }
                try { entity.SketchZ = point.Z; } catch (Exception ex) { Warn(snapshot, "Point.Z", ex); }
                try { entity.PointType = point.Type; } catch (Exception ex) { Warn(snapshot, "Point.Type", ex); }
                // SketchPoint exposes no construction flag in this interop; construction
                // geometry is a segment property and is captured in CaptureSegments.

                if (sketchToModel != null && mathUtility != null)
                    ApplySketchToModel(entity, sketchToModel, mathUtility, snapshot);

                CaptureEntityPersistRef(model, point, entity);
                snapshot.Entities.Add(entity);
            }
        }

        private static void ReadPointIds(SketchPoint point, SketchEntitySnapshotV7 entity)
        {
            try
            {
                int[] ids = point.GetID() as int[];
                if (ids != null && ids.Length >= 2)
                {
                    entity.Id1 = ids[0];
                    entity.Id2 = ids[1];
                    entity.HasIds = true;
                }
            }
            catch { }
        }

        private static void ReadSegmentIds(SketchSegment segment, SketchEntitySnapshotV7 entity)
        {
            try
            {
                int[] ids = segment.GetID() as int[];
                if (ids != null && ids.Length >= 2)
                {
                    entity.Id1 = ids[0];
                    entity.Id2 = ids[1];
                    entity.HasIds = true;
                }
            }
            catch { }
        }

        private static void ApplySketchToModel(
            SketchEntitySnapshotV7 entity,
            MathTransform sketchToModel,
            MathUtility mathUtility,
            SketchSnapshotV7 snapshot)
        {
            try
            {
                MathPoint sketchPoint = mathUtility.CreatePoint(
                    new double[] { entity.SketchX, entity.SketchY, entity.SketchZ }) as MathPoint;
                if (sketchPoint == null) return;
                MathPoint modelPoint = sketchPoint.MultiplyTransform(sketchToModel) as MathPoint;
                if (modelPoint == null) return;
                double[] xyz = modelPoint.ArrayData as double[];
                if (xyz == null || xyz.Length < 3) return;
                entity.ModelX = xyz[0];
                entity.ModelY = xyz[1];
                entity.ModelZ = xyz[2];
                entity.HasModelCoords = true;
            }
            catch (Exception ex) { Warn(snapshot, "SketchToModelPoint", ex); }
        }

        // ---------------------------------------------------------------- segments

        private static void CaptureSegments(SketchSnapshotV7 snapshot, ModelDoc2 model, Sketch sketch)
        {
            object[] segments = null;
            try { segments = sketch.GetSketchSegments() as object[]; }
            catch (Exception ex) { Warn(snapshot, "GetSketchSegments", ex); }
            if (segments == null) return;

            snapshot.DeclaredSegmentCount = segments.Length;

            for (int i = 0; i < segments.Length; i++)
            {
                SketchSegment segment = segments[i] as SketchSegment;
                if (segment == null)
                {
                    Warn(snapshot, "SketchSegmentCast", "index=" + i);
                    continue;
                }

                SketchEntitySnapshotV7 entity = new SketchEntitySnapshotV7
                {
                    Kind = SketchEntityKindV7.Segment,
                    Index = i
                };

                ReadSegmentIds(segment, entity);
                try { entity.SegmentType = segment.GetType(); }
                catch (Exception ex) { Warn(snapshot, "Segment.GetType", ex); }
                try { entity.IsConstruction = segment.ConstructionGeometry; }
                catch (Exception ex) { Warn(snapshot, "Segment.ConstructionGeometry", ex); }
                try { entity.IsBendLine = segment.IsBendLine(); }
                catch (Exception ex) { Warn(snapshot, "Segment.IsBendLine", ex); }
                try { entity.SegmentStatus = segment.Status; }
                catch (Exception ex) { Warn(snapshot, "Segment.Status", ex); }
                try { entity.Length = segment.GetLength(); }
                catch (Exception ex) { Warn(snapshot, "Segment.GetLength", ex); }
                try { entity.SegmentName = segment.GetName() ?? ""; }
                catch (Exception ex) { Warn(snapshot, "Segment.GetName", ex); }
                try { entity.BelongsToSlot = segment.GetSketchSlot() != null; }
                catch { }

                CaptureEntityPersistRef(model, segment, entity);
                snapshot.Entities.Add(entity);
            }
        }

        // -------------------------------------------------------- relations / refs

        private static void CaptureRelations(SketchSnapshotV7 snapshot, Sketch sketch)
        {
            SketchRelationManager manager = null;
            try { manager = sketch.RelationManager; }
            catch (Exception ex) { Warn(snapshot, "RelationManager", ex); }
            if (manager == null) return;

            snapshot.RelationCountAll = SafeRelationCount(manager, (int)swSketchRelationFilterType_e.swAll, snapshot, "swAll");
            snapshot.RelationCountDangling = SafeRelationCount(manager, (int)swSketchRelationFilterType_e.swDangling, snapshot, "swDangling");
            snapshot.RelationCountOverDefining = SafeRelationCount(manager, (int)swSketchRelationFilterType_e.swOverDefining, snapshot, "swOverDefining");
            snapshot.RelationCountExternal = SafeRelationCount(manager, (int)swSketchRelationFilterType_e.swExternal, snapshot, "swExternal");
            snapshot.RelationCountDefinedInContext = SafeRelationCount(manager, (int)swSketchRelationFilterType_e.swDefinedInContext, snapshot, "swDefinedInContext");
            snapshot.RelationCountLocked = SafeRelationCount(manager, (int)swSketchRelationFilterType_e.swLocked, snapshot, "swLocked");
            snapshot.RelationCountBroken = SafeRelationCount(manager, (int)swSketchRelationFilterType_e.swBroken, snapshot, "swBroken");

            ReadRelationBucket(manager, (int)swSketchRelationFilterType_e.swAll, snapshot.Constraints, snapshot);
            ReadRelationBucket(manager, (int)swSketchRelationFilterType_e.swExternal, snapshot.ExternalRelations, snapshot);
        }

        private static int SafeRelationCount(SketchRelationManager manager, int filter, SketchSnapshotV7 snapshot, string filterName)
        {
            try { return manager.GetRelationsCount(filter); }
            catch (Exception ex) { Warn(snapshot, "GetRelationsCount:" + filterName, ex); return 0; }
        }

        private static void ReadRelationBucket(
            SketchRelationManager manager,
            int filter,
            List<SketchConstraintSnapshotV7> target,
            SketchSnapshotV7 snapshot)
        {
            object[] relations = null;
            try { relations = manager.GetRelations(filter) as object[]; }
            catch (Exception ex) { Warn(snapshot, "GetRelations:filter=" + filter, ex); }
            if (relations == null) return;

            for (int i = 0; i < relations.Length; i++)
            {
                SketchRelation relation = relations[i] as SketchRelation;
                if (relation == null)
                {
                    Warn(snapshot, "SketchRelationCast", "filter=" + filter + " index=" + i);
                    continue;
                }

                SketchConstraintSnapshotV7 constraint = new SketchConstraintSnapshotV7
                {
                    Index = i,
                    FilterSource = filter
                };

                try { constraint.RelationType = relation.GetRelationType(); }
                catch (Exception ex) { Warn(snapshot, "Relation.GetRelationType", ex); }
                constraint.RelationTypeName = NameOfConstraintType(constraint.RelationType);

                try { constraint.EntityCount = relation.GetEntitiesCount(); }
                catch (Exception ex) { Warn(snapshot, "Relation.GetEntitiesCount", ex); }

                try
                {
                    int[] entityTypes = relation.GetEntitiesType() as int[];
                    if (entityTypes != null)
                        for (int t = 0; t < entityTypes.Length; t++) constraint.EntityTypes.Add(entityTypes[t]);
                }
                catch (Exception ex) { Warn(snapshot, "Relation.GetEntitiesType", ex); }

                try
                {
                    object[] entities = relation.GetEntities() as object[];
                    if (entities != null)
                        for (int e = 0; e < entities.Length; e++)
                            constraint.EntityKeys.Add(ResolveRelationEntityKey(entities[e]));
                }
                catch (Exception ex) { Warn(snapshot, "Relation.GetEntities", ex); }

                target.Add(constraint);
            }
        }

        private static string ResolveRelationEntityKey(object entity)
        {
            if (entity == null) return "<null>";

            SketchPoint point = entity as SketchPoint;
            if (point != null)
            {
                SketchEntitySnapshotV7 probe = new SketchEntitySnapshotV7 { Kind = SketchEntityKindV7.Point };
                ReadPointIds(point, probe);
                return probe.EntityKey;
            }

            SketchSegment segment = entity as SketchSegment;
            if (segment != null)
            {
                SketchEntitySnapshotV7 probe = new SketchEntitySnapshotV7 { Kind = SketchEntityKindV7.Segment };
                ReadSegmentIds(segment, probe);
                return probe.EntityKey;
            }

            Feature feature = entity as Feature;
            if (feature != null) return "F:" + SafeFeatureName(feature);

            Dimension dimension = entity as Dimension;
            if (dimension != null) return "D:" + SafeDimensionFullName(dimension);

            return "T:" + entity.GetType().Name;
        }

        // -------------------------------------------------------------- dimensions

        private static void CaptureDimensions(SketchSnapshotV7 snapshot, ModelDoc2 model, Feature sketchFeature)
        {
            if (sketchFeature == null) return;

            object current = null;
            try { current = sketchFeature.GetFirstDisplayDimension(); }
            catch (Exception ex) { Warn(snapshot, "GetFirstDisplayDimension", ex); }

            int guard = 0;
            int index = 0;
            while (current != null && guard++ < 100000)
            {
                object next = null;
                try { next = sketchFeature.GetNextDisplayDimension(current); }
                catch (Exception ex) { Warn(snapshot, "GetNextDisplayDimension", ex); }

                DisplayDimension display = current as DisplayDimension;
                if (display == null)
                {
                    Warn(snapshot, "DisplayDimensionCast", "index=" + index);
                    current = next;
                    index++;
                    continue;
                }

                SketchDimensionSnapshotV7 dimension = new SketchDimensionSnapshotV7 { Index = index };

                try { dimension.DisplayType = display.Type2; }
                catch (Exception ex) { Warn(snapshot, "DisplayDimension.Type2", ex); }

                Dimension raw = null;
                try { raw = display.GetDimension2(0); }
                catch (Exception ex) { Warn(snapshot, "DisplayDimension.GetDimension2", ex); }

                if (raw != null)
                {
                    try { dimension.Name = raw.Name ?? ""; } catch (Exception ex) { Warn(snapshot, "Dimension.Name", ex); }
                    try { dimension.FullName = raw.FullName ?? ""; } catch (Exception ex) { Warn(snapshot, "Dimension.FullName", ex); }
                    try { dimension.Value = raw.Value; } catch (Exception ex) { Warn(snapshot, "Dimension.Value", ex); }
                    try { dimension.SystemValue = raw.SystemValue; } catch (Exception ex) { Warn(snapshot, "Dimension.SystemValue", ex); }
                    try { dimension.DrivenState = raw.DrivenState; } catch (Exception ex) { Warn(snapshot, "Dimension.DrivenState", ex); }
                    try { dimension.IsReference = raw.IsReference(); } catch (Exception ex) { Warn(snapshot, "Dimension.IsReference", ex); }
                    CaptureEntityPersistRef(model, raw, dimension);
                }

                try
                {
                    Annotation annotation = display.GetAnnotation() as Annotation;
                    if (annotation != null) dimension.IsDangling = annotation.IsDangling();
                }
                catch { }

                snapshot.Dimensions.Add(dimension);
                current = next;
                index++;
            }
        }

        private static void CaptureEntityPersistRef(ModelDoc2 model, Dimension dimension, SketchDimensionSnapshotV7 target)
        {
            byte[] data = TryGetPersistBytes(model, dimension);
            if (data == null) return;
            target.PersistRefBytes = data.Length;
            target.PersistRefSignature = HashBytes(data);
        }

        // ------------------------------------------------------------------- slots

        private static void CaptureSlots(SketchSnapshotV7 snapshot, Sketch sketch)
        {
            try { snapshot.DeclaredSlotCount = sketch.GetSketchSlotCount(); }
            catch (Exception ex) { Warn(snapshot, "GetSketchSlotCount", ex); }
            if (snapshot.DeclaredSlotCount <= 0) return;

            object[] slots = null;
            try { slots = sketch.GetSketchSlots() as object[]; }
            catch (Exception ex) { Warn(snapshot, "GetSketchSlots", ex); }
            if (slots == null) return;

            for (int i = 0; i < slots.Length; i++)
            {
                SketchSlot slot = slots[i] as SketchSlot;
                if (slot == null)
                {
                    Warn(snapshot, "SketchSlotCast", "index=" + i);
                    continue;
                }

                SketchSlotSnapshotV7 captured = new SketchSlotSnapshotV7 { Index = i };
                try { captured.CreationType = slot.CreationType; } catch (Exception ex) { Warn(snapshot, "Slot.CreationType", ex); }
                try { captured.LengthType = slot.LengthType; } catch (Exception ex) { Warn(snapshot, "Slot.LengthType", ex); }
                try { captured.Length = slot.Length; } catch (Exception ex) { Warn(snapshot, "Slot.Length", ex); }
                try { captured.Width = slot.Width; } catch (Exception ex) { Warn(snapshot, "Slot.Width", ex); }
                try { captured.CenterArcDirection = slot.CenterArcDirection; } catch (Exception ex) { Warn(snapshot, "Slot.CenterArcDirection", ex); }

                try
                {
                    object[] slotPoints = slot.GetSlotPoints() as object[];
                    if (slotPoints != null)
                    {
                        for (int p = 0; p < slotPoints.Length; p++)
                        {
                            SketchPoint point = slotPoints[p] as SketchPoint;
                            if (point == null) continue;
                            SketchEntitySnapshotV7 probe = new SketchEntitySnapshotV7 { Kind = SketchEntityKindV7.Point };
                            ReadPointIds(point, probe);
                            captured.SlotPointKeys.Add(SlotPointKey(probe, i, p));
                            captured.SlotPointSketchCoords.Add(point.X);
                            captured.SlotPointSketchCoords.Add(point.Y);
                            captured.SlotPointSketchCoords.Add(point.Z);
                        }
                    }
                }
                catch (Exception ex) { Warn(snapshot, "Slot.GetSlotPoints", ex); }

                snapshot.Slots.Add(captured);
            }
        }

        /// <summary>
        /// Identity key for one point of a slot.
        ///
        /// A slot's interior points are synthesized by SolidWorks. Its centre point comes back
        /// with a negative second ID and its GetID() values shift between two reads of the same
        /// untouched sketch (observed: P:12|-1 then P:13|-1 on an unmodified part). Keying those
        /// by GetID therefore manufactures instability that does not exist in the geometry.
        ///
        /// When the IDs are unusable, fall back to the slot index plus the point's ordinal within
        /// GetSlotPoints() - which is deterministic - and mark the key with the SL: prefix so the
        /// log shows the fallback was taken.
        /// </summary>
        private static string SlotPointKey(SketchEntitySnapshotV7 probe, int slotIndex, int ordinal)
        {
            if (probe.HasIds && probe.Id1 >= 0 && probe.Id2 >= 0) return probe.EntityKey;
            return "SL:" + slotIndex.ToString(CultureInfo.InvariantCulture) +
                   "." + ordinal.ToString(CultureInfo.InvariantCulture);
        }

        // ----------------------------------------------------- entity persist refs

        private static void CaptureEntityPersistRef(ModelDoc2 model, object entity, SketchEntitySnapshotV7 target)
        {
            byte[] data = TryGetPersistBytes(model, entity);
            if (data == null) return;
            target.PersistRefBytes = data.Length;
            target.PersistRefSignature = HashBytes(data);
        }

        /// <summary>
        /// Entity-level persist references are "where available" per the Phase 5 scope.
        /// Failures are counted, not logged per entity, so the debug log stays readable.
        /// </summary>
        private static byte[] TryGetPersistBytes(ModelDoc2 model, object entity)
        {
            if (entity == null) return null;
            try
            {
                ModelDocExtension extension = model.Extension;
                if (extension == null) return null;
                byte[] data = extension.GetPersistReference3(entity) as byte[];
                return data == null || data.Length == 0 ? null : data;
            }
            catch { return null; }
        }

        // ----------------------------------------------------------------- helpers

        private static MathUtility ResolveMathUtility(MirrorV7Context context)
        {
            ISldWorks app = context.SwApp;
            if (app == null) app = SwAddin.InstanceSwApp;
            if (app == null) return null;
            try { return app.GetMathUtility() as MathUtility; }
            catch { return null; }
        }

        private static string SafeFeatureName(Feature feature)
        {
            try { return feature == null ? "" : feature.Name ?? ""; }
            catch { return ""; }
        }

        private static string SafeDimensionFullName(Dimension dimension)
        {
            try { return dimension == null ? "" : dimension.FullName ?? ""; }
            catch { return ""; }
        }

        private static string NameOfConstraintType(int value)
        {
            try
            {
                if (!Enum.IsDefined(typeof(swConstraintType_e), value)) return "<unknown:" + value + ">";
                return Enum.GetName(typeof(swConstraintType_e), value);
            }
            catch { return "<unknown:" + value + ">"; }
        }

        public static string NameOfSegmentType(int value)
        {
            try
            {
                if (!Enum.IsDefined(typeof(swSketchSegments_e), value)) return "<unknown:" + value + ">";
                return Enum.GetName(typeof(swSketchSegments_e), value);
            }
            catch { return "<unknown:" + value + ">"; }
        }

        public static string NameOfRelationEntityType(int value)
        {
            try
            {
                if (!Enum.IsDefined(typeof(swSketchRelationEntityTypes_e), value)) return "<unknown:" + value + ">";
                return Enum.GetName(typeof(swSketchRelationEntityTypes_e), value);
            }
            catch { return "<unknown:" + value + ">"; }
        }

        private static void Warn(SketchSnapshotV7 snapshot, string what, Exception ex)
        {
            Warn(snapshot, what, ex == null ? "<null exception>" : ex.Message);
        }

        private static void Warn(SketchSnapshotV7 snapshot, string what, string detail)
        {
            string entry = what + " :: " + (detail ?? "");
            if (!snapshot.Warnings.Contains(entry)) snapshot.Warnings.Add(entry);
        }

        /// <summary>FNV-1a 64-bit. Deterministic across runs and processes.</summary>
        public static string HashBytes(byte[] data)
        {
            if (data == null || data.Length == 0) return "<none>";
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                for (int i = 0; i < data.Length; i++)
                {
                    hash ^= data[i];
                    hash *= 1099511628211UL;
                }
                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>FNV-1a 64-bit over a text signature.</summary>
        public static string HashText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "<empty>";
            return HashBytes(Encoding.UTF8.GetBytes(text));
        }
    }
}
