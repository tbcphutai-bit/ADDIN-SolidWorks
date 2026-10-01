using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Helpers
{
    public sealed class SketchSupportSnapshot20
    {
        public double Area;
        public double[][][] Boundaries;
        public string Kind;
        public int ReferenceType;
        public byte[] PersistentReference;
        public double[] PlaneOrigin;
        public double[] PlaneNormal;
    }
    public sealed class CutAuditSnapshot21
    {
        public SketchSupportSnapshot20 BeforeSupport;
        public List<SketchPrimitiveSnapshot58> BeforePrimitives53;
        public string CaptureError;
        public Body2 BeforeBody23;
        public double SourceResidual23 = double.NaN;
        public bool BeforeChainVerified23;
        public string CircleInvariant26;
        public List<SketchPointSnapshot> CirclePoints26;
        public List<RelationReference30> References30;
        public string ProfileKind50;
        public int SegmentCount50;
        public int ContourCount50;
        public int SelectedContourCount50;
        public bool BothDirections50;
        public bool ReverseDirection50;
        public bool FlipSideToCut50;
        public bool FromOffsetReverse50;
        public ExtrudeDirectionAudit50 D1_50;
        public ExtrudeDirectionAudit50 D2_50;
        public ExtrudeCutRecipe44 Recipe44;
    }
    public sealed class ExtrudeDirectionAudit50
    {
        public bool Forward;
        public int EndCondition;
        public string EndConditionName;
        public double Depth;
        public bool ReferencePresent;
        public int ReferenceSelectionType;
        public string ReferenceEntityKind;
        public byte[] PersistentReference;
        public double[] ReferencePoint;
        public double[][] ReferenceEdgeSamples;
        public SketchSupportSnapshot20 ReferenceFace;
        public string CaptureError;
    }
    public sealed class RelationReference30
    {
        public string Key;
        public SketchRelation SourceRelation31;
        public int EntityIndex;
        public int EntityCount;
        public Func<ModelDoc2, PlaneData, object> Resolve;
        public Func<ModelDoc2, object, object, bool> Verify31;
        public Func<PlaneData, double[]> PickPoint31;
        public Func<PlaneData, double[][]> Geometry31;
        public string Kind31;
    }
    public class SketchPointSnapshot
    {
        public int Id1;
        public int Id2;
        public int Index;
        public double X;
        public double Y;
        public double ModelX;
        public double ModelY;
        public double ModelZ;
        public bool HasModelCoords;
    }

    public class SketchSlotSnapshot
    {
        public int CreationType;
        public int LengthType;
        public double Length;
        public double Width;
        public double X1, Y1, Z1;
        public double X2, Y2, Z2;
        public double X3, Y3, Z3;
        public double ModelX1, ModelY1, ModelZ1;
        public double ModelX2, ModelY2, ModelZ2;
        public double ModelX3, ModelY3, ModelZ3;
        public bool HasModelCoords;
        public int CenterArcDirection;
    }

    public sealed class SketchPrimitiveSnapshot58
    {
        public int Type;
        public bool Construction;
        public bool Circle;
        public double Length;
        public double[] StartModel;
        public double[] EndModel;
        public double[] CenterModel;
        public double[] MiddleModel;
    }

    public sealed class SketchPointMoveRejected58 : InvalidOperationException
    {
        public SketchPointMoveRejected58(string message) : base(message) { }
    }

    public static partial class SketchOperationsHelper
    {
        // Capture the original segment geometry, not the staging geometry: an upstream
        // feature rebuild can move only some of a downstream sketch's points.
        public static List<SketchPrimitiveSnapshot58> CapturePristineSketchPrimitives58(Sketch sketch)
        {
            var result = new List<SketchPrimitiveSnapshot58>();
            if (sketch == null) return result;
            MathTransform modelToSketch = sketch.ModelToSketchTransform as MathTransform;
            MathTransform sketchToModel = modelToSketch == null ? null : modelToSketch.Inverse() as MathTransform;
            ISldWorks app = SwAddin.InstanceSwApp;
            IMathUtility math = app == null ? null : app.GetMathUtility() as IMathUtility;
            if (sketchToModel == null || math == null) return result;
            foreach (object item in sketch.GetSketchSegments() as object[] ?? new object[0])
            {
                SketchSegment segment = item as SketchSegment;
                if (segment == null) continue;
                var entry = new SketchPrimitiveSnapshot58 { Type = segment.GetType() };
                try
                {
                    entry.Construction = segment.ConstructionGeometry;
                    entry.Length = segment.GetLength();
                    if (entry.Type == (int)swSketchSegments_e.swSketchLINE)
                    {
                        ISketchLine line = segment as ISketchLine;
                        if (line == null) throw new InvalidOperationException("Line interface unavailable.");
                        entry.StartModel = PrimitivePointToModel58(math, sketchToModel, line.GetStartPoint2() as SketchPoint);
                        entry.EndModel = PrimitivePointToModel58(math, sketchToModel, line.GetEndPoint2() as SketchPoint);
                    }
                    else if (entry.Type == (int)swSketchSegments_e.swSketchARC)
                    {
                        ISketchArc arc = segment as ISketchArc;
                        if (arc == null) throw new InvalidOperationException("Arc interface unavailable.");
                        entry.Circle = arc.IsCircle() != 0;
                        entry.StartModel = PrimitivePointToModel58(math, sketchToModel, arc.GetStartPoint2() as SketchPoint);
                        if (!entry.Circle)
                            entry.EndModel = PrimitivePointToModel58(math, sketchToModel, arc.GetEndPoint2() as SketchPoint);
                        entry.CenterModel = PrimitivePointToModel58(math, sketchToModel, arc.GetCenterPoint2() as SketchPoint);
                        if (!entry.Circle)
                        {
                            double radius = arc.GetRadius();
                            if (radius <= 0) throw new InvalidOperationException("Invalid arc radius.");
                            SketchPoint center = arc.GetCenterPoint2() as SketchPoint;
                            SketchPoint start = arc.GetStartPoint2() as SketchPoint;
                            double halfAngle = segment.GetLength() / radius * 0.5 *
                                (arc.GetRotationDir() >= 0 ? 1.0 : -1.0);
                            double vx = start.X - center.X, vy = start.Y - center.Y;
                            double[] middleLocal = {
                                center.X + vx * Math.Cos(halfAngle) - vy * Math.Sin(halfAngle),
                                center.Y + vx * Math.Sin(halfAngle) + vy * Math.Cos(halfAngle), 0.0
                            };
                            entry.MiddleModel = PrimitiveLocalToModel58(math, sketchToModel, middleLocal);
                        }
                    }
                }
                catch (Exception ex)
                {
                    CreateMirrorPartPackage.LogDebug("[PRIMITIVE58][CAPTURE_INCOMPLETE] type=" + entry.Type +
                        " reason=" + ex.Message);
                }
                // Preserve unsupported segment types in the snapshot so the fallback
                // rejects them explicitly before deleting anything.
                result.Add(entry);
            }
            return result;
        }

        private static double[] PrimitivePointToModel58(IMathUtility math, MathTransform transform, SketchPoint point)
        {
            if (point == null) throw new InvalidOperationException("PRIMITIVE58: Sketch point unavailable.");
            return PrimitiveLocalToModel58(math, transform, new[] { point.X, point.Y, point.Z });
        }

        private static double[] PrimitiveLocalToModel58(IMathUtility math, MathTransform transform, double[] local)
        {
            MathPoint p = math.CreatePoint(local) as MathPoint;
            MathPoint mapped = p == null ? null : p.MultiplyTransform(transform) as MathPoint;
            double[] data = mapped == null ? null : mapped.ArrayData as double[];
            if (data == null || data.Length < 3) throw new InvalidOperationException("PRIMITIVE58: Point transform failed.");
            return new[] { data[0], data[1], data[2] };
        }
        /// <summary>
        /// Lưu lại thông số các rãnh Slot nguyên bản trong Sketch trước khi rebuild
        /// </summary>
        public static List<SketchSlotSnapshot> CapturePristineSketchSlots(Sketch swSketch)
        {
            List<SketchSlotSnapshot> list = new List<SketchSlotSnapshot>();
            if (swSketch == null) return list;

            int slotCount = 0;
            try { slotCount = swSketch.GetSketchSlotCount(); } catch { }
            if (slotCount <= 0) return list;

            object[] slots = swSketch.GetSketchSlots() as object[];
            if (slots == null) return list;

            MathTransform s2m = null;
            try
            {
                MathTransform m2s = swSketch.ModelToSketchTransform;
                if (m2s != null) s2m = m2s.Inverse() as MathTransform;
            }
            catch { }

            ISldWorks swApp = SwAddin.InstanceSwApp;
            IMathUtility mathUtility = swApp != null ? swApp.GetMathUtility() as IMathUtility : null;

            for (int i = 0; i < slots.Length; i++)
            {
                ISketchSlot slot = slots[i] as ISketchSlot;
                if (slot == null) continue;

                object[] pts = slot.GetSlotPoints() as object[];
                SketchPoint p0 = (pts != null && pts.Length > 0) ? pts[0] as SketchPoint : null;
                SketchPoint p1 = (pts != null && pts.Length > 1) ? pts[1] as SketchPoint : null;
                SketchPoint p2 = (pts != null && pts.Length > 2) ? pts[2] as SketchPoint : null;

                double x1 = p0?.X ?? 0, y1 = p0?.Y ?? 0, z1 = p0?.Z ?? 0;
                double x2 = p1?.X ?? 0, y2 = p1?.Y ?? 0, z2 = p1?.Z ?? 0;
                double x3 = p2?.X ?? 0, y3 = p2?.Y ?? 0, z3 = p2?.Z ?? 0;

                double mx1 = 0, my1 = 0, mz1 = 0;
                double mx2 = 0, my2 = 0, mz2 = 0;
                double mx3 = 0, my3 = 0, mz3 = 0;
                bool hasModel = false;

                if (s2m != null && mathUtility != null)
                {
                    try
                    {
                        MathPoint mp1 = mathUtility.CreatePoint(new double[] { x1, y1, 0.0 }) as MathPoint;
                        MathPoint mpm1 = mp1 != null ? mp1.MultiplyTransform(s2m) as MathPoint : null;
                        double[] a1 = mpm1 != null ? mpm1.ArrayData as double[] : null;
                        if (a1 != null && a1.Length >= 3) { mx1 = a1[0]; my1 = a1[1]; mz1 = a1[2]; }

                        MathPoint mp2 = mathUtility.CreatePoint(new double[] { x2, y2, 0.0 }) as MathPoint;
                        MathPoint mpm2 = mp2 != null ? mp2.MultiplyTransform(s2m) as MathPoint : null;
                        double[] a2 = mpm2 != null ? mpm2.ArrayData as double[] : null;
                        if (a2 != null && a2.Length >= 3) { mx2 = a2[0]; my2 = a2[1]; mz2 = a2[2]; }

                        if (p2 != null)
                        {
                            MathPoint mp3 = mathUtility.CreatePoint(new double[] { x3, y3, 0.0 }) as MathPoint;
                            MathPoint mpm3 = mp3 != null ? mp3.MultiplyTransform(s2m) as MathPoint : null;
                            double[] a3 = mpm3 != null ? mpm3.ArrayData as double[] : null;
                            if (a3 != null && a3.Length >= 3) { mx3 = a3[0]; my3 = a3[1]; mz3 = a3[2]; }
                        }
                        hasModel = true;
                    }
                    catch { }
                }

                list.Add(new SketchSlotSnapshot
                {
                    CreationType = slot.CreationType,
                    LengthType = slot.LengthType,
                    Length = slot.Length,
                    Width = slot.Width,
                    X1 = x1, Y1 = y1, Z1 = z1,
                    X2 = x2, Y2 = y2, Z2 = z2,
                    X3 = x3, Y3 = y3, Z3 = z3,
                    ModelX1 = mx1, ModelY1 = my1, ModelZ1 = mz1,
                    ModelX2 = mx2, ModelY2 = my2, ModelZ2 = mz2,
                    ModelX3 = mx3, ModelY3 = my3, ModelZ3 = mz3,
                    HasModelCoords = hasModel,
                    CenterArcDirection = slot.CenterArcDirection
                });
            }

            return list;
        }

        /// <summary>
        /// Lưu lại bản đồ tọa độ sạch nguyên bản của mọi điểm trong Sketch trước khi có bất kỳ Feature nào Rebuild
        /// </summary>
        private static void Audit21(ModelDoc2 model, string message)
        {
            string line = "[CUTAUDIT21] " + message;
            CreateMirrorPartPackage.LogDebug(line);
            try
            {
                string path = model.GetPathName() + ".CutAudit21.txt";
                System.IO.File.AppendAllText(path, line + System.Environment.NewLine, System.Text.Encoding.UTF8);
            }
            catch (Exception ex) { CreateMirrorPartPackage.LogDebug("[CUTAUDIT21][REPORT_WRITE_FAILED] " + ex.Message); }
        }

        public static CutAuditSnapshot21 CaptureCutAudit21(ModelDoc2 model, Feature feature, Feature profile)
        {
            var snapshot = new CutAuditSnapshot21();
            var definition = feature.GetDefinition() as IExtrudeFeatureData2;
            bool access = false;
            try
            {
                if (definition == null) throw new InvalidOperationException("Not an extrusion definition");
                access = definition.AccessSelections(model, null);
                Audit21(model, "SOURCE feature=" + feature.Name + " access=" + access);
                if (!access) throw new InvalidOperationException("AccessSelections returned false");
                var sourceSketch50 = profile.GetSpecificFeature2() as Sketch;
                object[] segments50 = sourceSketch50 == null ? null : sourceSketch50.GetSketchSegments() as object[];
                object[] contours50 = sourceSketch50 == null ? null : sourceSketch50.GetSketchContours() as object[];
                object[] selectedContours50 = null;
                try { selectedContours50 = definition.Contours as object[]; } catch { }
                snapshot.ProfileKind50 = sourceSketch50 == null ? "NO_SKETCH" :
                    (IsOpenProfileSketch(sourceSketch50) ? "OPEN" : "CLOSED");
                snapshot.SegmentCount50 = segments50 == null ? 0 : segments50.Length;
                snapshot.ContourCount50 = contours50 == null ? 0 : contours50.Length;
                snapshot.SelectedContourCount50 = selectedContours50 == null ? 0 : selectedContours50.Length;
                // Required replay options must not silently become false on an API read error.
                snapshot.BothDirections50 = definition.BothDirections;
                snapshot.ReverseDirection50 = definition.ReverseDirection;
                snapshot.FlipSideToCut50 = definition.FlipSideToCut;
                if (definition.FromType == (int)swExtrudeFrom_e.swExtrudeFrom_Offset)
                    snapshot.FromOffsetReverse50 = definition.FromOffsetReverse;
                snapshot.D1_50 = CaptureExtrudeDirection50(model, definition, true);
                snapshot.D2_50 = CaptureExtrudeDirection50(model, definition, false);
                snapshot.Recipe44 = CaptureCutRecipe44(model, definition, sourceSketch50, snapshot);
                foreach (bool first in new[] { true, false })
                {
                    try { Audit21(model, "OPTION direction=" + (first ? "D1" : "D2") +
                        " endCondition=" + definition.GetEndCondition(first) + " depth_m=" + definition.GetDepth(first).ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
                    catch (Exception ex) { Audit21(model, "OPTION_READ_FAILED direction=" + first + " reason=" + ex.Message); }
                }
                foreach (var property in typeof(IExtrudeFeatureData2).GetProperties())
                {
                    if (!property.CanRead || property.GetIndexParameters().Length != 0 ||
                        !(property.PropertyType.IsPrimitive || property.PropertyType.IsEnum || property.PropertyType == typeof(string))) continue;
                    try { Audit21(model, "OPTION " + property.Name + "=" + Convert.ToString(property.GetValue(definition, null), System.Globalization.CultureInfo.InvariantCulture)); }
                    catch (Exception ex) { Audit21(model, "OPTION_READ_FAILED property=" + property.Name + " reason=" + ex.Message); }
                }
                var sourceSketch26 = profile.GetSpecificFeature2() as Sketch;
                snapshot.BeforeSupport = CaptureSupport20(sourceSketch26, model);
                snapshot.BeforePrimitives53 = CapturePristineSketchPrimitives58(sourceSketch26);
                if (snapshot.BeforePrimitives53.Count != snapshot.SegmentCount50)
                    throw new InvalidOperationException("PROFILE53 source primitive capture is incomplete.");
                Audit21(model, "[PROFILE53][SOURCE_CAPTURE] feature=" + feature.Name +
                    " checkpoint=BEFORE_CUT primitives=" + snapshot.BeforePrimitives53.Count);
                snapshot.CircleInvariant26 = CircleInvariant26(sourceSketch26);
                snapshot.CirclePoints26 = CapturePristineSketchPoints(sourceSketch26);
                try
                {
                    snapshot.References30 = CaptureReferences30(model, sourceSketch26);
                }
                catch (Exception referenceError56)
                {
                    // Geometry-first replay does not need the original constraint graph.
                    // A dangling/null relation entity must not discard the already captured
                    // profile, support, cut options and body oracle.
                    snapshot.References30 = new List<RelationReference30>();
                    Audit21(model, "[GEOMETRY_FIRST56][REFERENCE_CAPTURE_IGNORED] feature=" +
                        feature.Name + " reason=" + referenceError56.Message);
                }
                if (snapshot.BeforeSupport == null)
                    throw new InvalidOperationException("CUT23_SOURCE: sketch support unavailable");
                int referenceType23 = 0;
                var sourceFace23 = sourceSketch26.GetReferenceEntity(ref referenceType23) as Face2;
                if (sourceFace23 != null && snapshot.BeforeSupport.Boundaries != null)
                    snapshot.SourceResidual23 = BoundaryResidual23(sourceFace23, snapshot.BeforeSupport.Boundaries);
                string bodyError23;
                snapshot.BeforeBody23 = BodyOperationsHelper.GetSolidBodyCopyStrict(model, out bodyError23);
                if (snapshot.BeforeBody23 == null) throw new InvalidOperationException("CUT23_SOURCE_BODY: " + bodyError23);
                Audit21(model, "[CUT23] SOURCE_SELF_CHECK feature=" + feature.Name + " residual_m=" + snapshot.SourceResidual23.ToString("R") +
                    " result=" + (snapshot.BeforeSupport.Boundaries == null ? "NOT_APPLICABLE_NON_FACE" :
                        snapshot.SourceResidual23 <= 1e-7 ? "PASS" : "SOURCE_DATA_INVALID"));
                Audit21(model, "SOURCE_SUPPORT_BEFORE feature=" + feature.Name + " " + SupportLabel21(snapshot.BeforeSupport));
            }
            catch (Exception ex)
            {
                snapshot.CaptureError = ex.Message;
                Audit21(model, "SOURCE_CAPTURE_INCOMPLETE feature=" + feature.Name + " reason=" + ex.Message);
            }
            finally { if (access) definition.ReleaseSelectionAccess(); }
            return snapshot;
        }

        private static ExtrudeDirectionAudit50 CaptureExtrudeDirection50(
            ModelDoc2 model, IExtrudeFeatureData2 definition, bool forward)
        {
            var result = new ExtrudeDirectionAudit50 { Forward = forward, ReferenceEntityKind = "NONE" };
            try
            {
                result.EndCondition = definition.GetEndCondition(forward);
                result.EndConditionName = Enum.GetName(typeof(swEndConditions_e), result.EndCondition) ??
                    ("VALUE_" + result.EndCondition);
                try { result.Depth = definition.GetDepth(forward); } catch { }
                int selectionType;
                object reference = definition.GetEndConditionReference(forward, out selectionType);
                result.ReferenceSelectionType = selectionType;
                result.ReferencePresent = reference != null;
                result.ReferenceEntityKind = EndReferenceKind50(reference);
                if (reference != null)
                {
                    try { result.PersistentReference = model.Extension.GetPersistReference3(reference) as byte[]; }
                    catch { result.PersistentReference = null; }
                    var vertex = reference as Vertex;
                    var edge = reference as Edge;
                    var point = reference as SketchPoint;
                    var face = reference as Face2;
                    if (vertex != null) result.ReferencePoint = vertex.GetPoint() as double[];
                    else if (edge != null) result.ReferenceEdgeSamples = SampleEdge24(edge);
                    else if (point != null) result.ReferencePoint = SketchPointWorld50(point);
                    else if (face != null) result.ReferenceFace = CaptureFaceSupport24(face);
                }
            }
            catch (Exception ex)
            {
                result.CaptureError = ex.Message;
            }
            return result;
        }

        private static string EndReferenceKind50(object reference)
        {
            if (reference == null) return "NONE";
            if (reference is Vertex) return "Vertex";
            if (reference is Face2) return "Face";
            if (reference is Body2) return "Body";
            if (reference is Edge) return "Edge";
            if (reference is SketchPoint) return "SketchPoint";
            if (reference is Feature) return "Feature";
            return reference.GetType().FullName ?? reference.GetType().Name;
        }

        public static void RebindExtrudeEndReferences50(ModelDoc2 model, Feature feature,
            CutAuditSnapshot21 audit, PlaneData plane)
        {
            if (model == null || feature == null || audit == null) return;
            var directions = new[] { audit.D1_50, audit.D2_50 };
            if (!directions.Any(direction => direction != null && direction.ReferencePresent)) return;

            IExtrudeFeatureData2 definition = feature.GetDefinition() as IExtrudeFeatureData2;
            bool access = false;
            bool committed = false;
            try
            {
                if (definition == null) throw new InvalidOperationException("ENDREF50: Extrude definition unavailable.");
                access = definition.AccessSelections(model, null);
                if (!access) throw new InvalidOperationException("ENDREF50: AccessSelections returned false.");
                foreach (ExtrudeDirectionAudit50 direction in directions)
                {
                    if (direction == null || !direction.ReferencePresent) continue;
                    object target = ResolveEndReference50(model, direction, plane);
                    if (target == null) throw new InvalidOperationException("ENDREF50: Mapped target is null for " + direction.EndConditionName);
                    definition.SetEndConditionReference(direction.Forward, target);
                    int readbackSelectionType;
                    object readback = definition.GetEndConditionReference(direction.Forward, out readbackSelectionType);
                    if (readback == null)
                        throw new InvalidOperationException("ENDREF50: End reference readback is null for " + direction.EndConditionName);
                    CreateMirrorPartPackage.LogDebug("[ENDREF50][BOUND] direction=" + (direction.Forward ? "D1" : "D2") +
                        " end=" + direction.EndConditionName + " sourceKind=" + direction.ReferenceEntityKind +
                        " sourceSelectionType=" + direction.ReferenceSelectionType +
                        " readbackSelectionType=" + readbackSelectionType + " targetKind=" + EndReferenceKind50(readback));
                }
                if (!feature.ModifyDefinition(definition, model, null))
                    throw new InvalidOperationException("ENDREF50: ModifyDefinition returned false.");
                committed = true;
            }
            finally
            {
                if (access && !committed)
                {
                    try { definition.ReleaseSelectionAccess(); } catch { }
                }
            }
        }

        private static object ResolveEndReference50(ModelDoc2 model, ExtrudeDirectionAudit50 direction, PlaneData plane)
        {
            if (direction.ReferenceEdgeSamples != null)
                return ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindEdge(model,
                    direction.ReferenceEdgeSamples.Select(point => ReflectReference30(point, plane)).ToArray());
            if (direction.ReferenceFace != null && direction.ReferenceFace.Boundaries != null)
                return ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindFace(model,
                    direction.ReferenceFace.Area,
                    direction.ReferenceFace.Boundaries.Select(boundary => boundary
                        .Select(point => ReflectReference30(point, plane)).ToArray()).ToArray());
            if (direction.ReferencePoint != null)
            {
                if (string.Equals(direction.ReferenceEntityKind, "SketchPoint", StringComparison.Ordinal))
                    return FindReflectedSketchPoint50(model, direction.ReferencePoint, plane);
                return ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindVertex(model,
                    ReflectReference30(direction.ReferencePoint, plane));
            }
            throw new InvalidOperationException("ENDREF50: No geometry mapper for " + direction.ReferenceEntityKind +
                " (end=" + direction.EndConditionName + ").");
        }

        private static string CircleInvariant26(Sketch sketch)
        {
            // Conservative fast path: full circles only. An arc needs orientation/end-point checks.
            var segments = sketch.GetSketchSegments() as object[] ?? new object[0];
            if (segments.Length == 0) return null;
            var signature = new List<string>();
            foreach (SketchSegment segment in segments)
            {
                var arc = segment as SketchArc;
                if (arc == null || arc.IsCircle() != 1 || segment.ConstructionGeometry) return null;
                var ids = segment.GetID() as int[];
                if (ids == null || ids.Length < 2) return null;
                signature.Add("C:" + ids[0] + "," + ids[1] + ":" + arc.GetRadius().ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            foreach (SketchRelation relation in sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[] ?? new object[0])
            {
                var display = relation.GetDisplayDimension() as DisplayDimension;
                var dimension = display?.GetDimension() as Dimension;
                signature.Add("R:" + relation.GetRelationType());
                if (dimension != null) signature.Add("D:" + dimension.FullName + ":" + dimension.DrivenState + ":" +
                    dimension.SystemValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            signature.Sort(StringComparer.Ordinal);
            return string.Join("|", signature);
        }

        public static bool TryAlreadyReflectedCircles26(ISldWorks app, ModelDoc2 model, Feature profile,
            CutAuditSnapshot21 source, PlaneData plane)
        {
            if (source?.CircleInvariant26 == null || source.CirclePoints26 == null) return false;
            var sketch = profile.GetSpecificFeature2() as Sketch;
            if (sketch == null || CircleInvariant26(sketch) != source.CircleInvariant26) return false;
            var live = CapturePristineSketchPoints(sketch);
            if (live.Count != source.CirclePoints26.Count || live.Count == 0) return false;
            double norm2 = plane.Normal.Sum(v => v * v);
            if (norm2 < 1e-20) return false;
            foreach (var point in source.CirclePoints26)
            {
                var matches = live.Where(p => p.Id1 == point.Id1 && p.Id2 == point.Id2).ToArray();
                if (matches.Length != 1 || !point.HasModelCoords || !matches[0].HasModelCoords) return false;
                var original = new[] { point.ModelX, point.ModelY, point.ModelZ };
                var actual = new[] { matches[0].ModelX, matches[0].ModelY, matches[0].ModelZ };
                double d = Enumerable.Range(0, 3).Sum(i => (original[i] - plane.Origin[i]) * plane.Normal[i]) / norm2;
                double error = Math.Sqrt(Enumerable.Range(0, 3).Sum(i => Math.Pow(actual[i] - (original[i] - 2 * d * plane.Normal[i]), 2)));
                if (double.IsNaN(error) || error > 1e-7) return false;
            }
            bool warning;
            int sketchError = profile.GetErrorCode2(out warning);
            if (sketchError != 0) return false;
            CreateMirrorPartPackage.LogDebug("[SKETCH26][ALREADY_REFLECTED] sketch=" + profile.Name +
                " fullCircles=True centersVerified=True radiiDimensionsUnchanged=True constraintTypesUnchanged=True dimensionsReleased=0 cutValidationStillRequired=True");
            return true;
        }

        private static double BoundaryResidual23(Face2 face, double[][][] boundaries)
        {
            if (boundaries == null || boundaries.Length == 0 || boundaries.Any(e => e == null || e.Length == 0))
                throw new InvalidOperationException("Empty source boundary samples");
            double maximum = 0;
            foreach (var p in boundaries.SelectMany(e => e))
            {
                if (p.Length != 3 || p.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                    throw new InvalidOperationException("Non-finite boundary sample");
                var q = face.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                if (q == null || q.Length < 3 || q.Take(3).Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                    throw new InvalidOperationException("Closest point unavailable");
                maximum = Math.Max(maximum, Math.Sqrt(Enumerable.Range(0, 3).Sum(i => (p[i] - q[i]) * (p[i] - q[i]))));
            }
            return maximum;
        }

        private static int SupportCandidates23(Body2 body, SketchSupportSnapshot20 support, double[][][] boundaries, out double best)
        {
            best = double.PositiveInfinity;
            int count = 0;
            foreach (Face2 face in body.GetFaces() as object[] ?? new object[0])
            {
                double residual = BoundaryResidual23(face, boundaries);
                best = Math.Min(best, residual);
                if (residual <= 1e-7 && Math.Abs(face.GetArea() - support.Area) <= Math.Max(1e-12, support.Area * 1e-6) &&
                    (face.GetEdges() as object[] ?? new object[0]).Length == boundaries.Length) count++;
            }
            return count;
        }

        // Read-only diagnostic on the staging document. Never treats a nearest face as a mapping.
        public static void CheckCutChain23(ISldWorks app, ModelDoc2 model, Feature feature,
            CutAuditSnapshot21 source, PlaneData plane)
        {
            string stage = "SOURCE_DATA";
            IExtrudeFeatureData2 definition = null;
            bool access = false;
            if (source != null) source.BeforeChainVerified23 = false;
            try
            {
                if (source == null || source.CaptureError != null || source.BeforeBody23 == null ||
                    source.BeforeSupport == null ||
                    (source.BeforeSupport.Boundaries != null &&
                        (double.IsNaN(source.SourceResidual23) || source.SourceResidual23 > 1e-7)))
                    throw new InvalidOperationException(source?.CaptureError ?? "Source self-check failed");
                stage = "REFLECTION_ORACLE";
                var oracle = BodyOperationsHelper.MirrorBodyStrict(app, source.BeforeBody23, plane);
                if (oracle == null || !oracle.Success || oracle.Body == null)
                    throw new InvalidOperationException(oracle?.ErrorMessage ?? "Oracle unavailable");
                double norm2 = plane.Normal.Sum(v => v * v);
                if (norm2 < 1e-20) throw new InvalidOperationException("Invalid reflection normal");
                double[][][] reflected = null;
                if (source.BeforeSupport.Boundaries != null)
                {
                    reflected = source.BeforeSupport.Boundaries.Select(e => e.Select(p =>
                    {
                        double d = Enumerable.Range(0, 3).Sum(i => (p[i] - plane.Origin[i]) * plane.Normal[i]) / norm2;
                        return Enumerable.Range(0, 3).Select(i => p[i] - 2 * d * plane.Normal[i]).ToArray();
                    }).ToArray()).ToArray();
                    double oracleBest;
                    int oracleCount = SupportCandidates23(oracle.Body, source.BeforeSupport, reflected, out oracleBest);
                    Audit21(model, "[CUT23] ORACLE feature=" + feature.Name + " candidates=" + oracleCount + " bestResidual_m=" + oracleBest.ToString("R"));
                    if (oracleCount != 1) throw new InvalidOperationException("Source support does not uniquely match reflected source body");
                }
                else Audit21(model, "[CUT23] ORACLE feature=" + feature.Name + " supportKind=" +
                    source.BeforeSupport.Kind + " faceBoundaryCheck=NOT_APPLICABLE");
                stage = "BEFORE_CUT_STATE";
                definition = feature.GetDefinition() as IExtrudeFeatureData2;
                if (definition == null || !(access = definition.AccessSelections(model, null)))
                    throw new InvalidOperationException("Cannot access before-cut state");
                string error;
                var live = BodyOperationsHelper.GetSolidBodyCopyStrict(model, out error);
                if (live == null) throw new InvalidOperationException(error);
                var missing = BodyOperationsHelper.BooleanCutStrict(oracle.Body, live, "CUT23_BEFORE_MISSING");
                var extra = BodyOperationsHelper.BooleanCutStrict(live, oracle.Body, "CUT23_BEFORE_EXTRA");
                double tolerance = Math.Max(BodyOperationsHelper.ABSOLUTE_GEOMETRY_TOLERANCE,
                    BodyOperationsHelper.GetBodyVolume(oracle.Body) * BodyOperationsHelper.BODY_TRANSFORM_RELATIVE_TOLERANCE);
                if (!missing.Success || !extra.Success || BodyOperationsHelper.SumBodyVolumes(missing.Bodies) > tolerance ||
                    BodyOperationsHelper.SumBodyVolumes(extra.Bodies) > tolerance)
                    throw new InvalidOperationException("Live before-cut body differs from reflected source checkpoint");
                if (reflected != null)
                {
                    stage = "LIVE_SUPPORT";
                    double liveBest;
                    int liveCount = SupportCandidates23(live, source.BeforeSupport, reflected, out liveBest);
                    Audit21(model, "[CUT23] LIVE feature=" + feature.Name + " candidates=" + liveCount + " bestResidual_m=" + liveBest.ToString("R"));
                    if (liveCount != 1) throw new InvalidOperationException("Live support missing or ambiguous");
                }
                source.BeforeChainVerified23 = true;
                Audit21(model, "[CUT23] SUMMARY feature=" + feature.Name + " result=PRECHECK_PASS mappingApplied=False cutValidated=False");
            }
            catch (Exception ex)
            {
                Audit21(model, "[CUT23] SUMMARY feature=" + feature.Name + " result=FAIL firstFailedStage=" + stage + " reason=" + ex.Message);
                throw new InvalidOperationException("CUT23 " + stage + ": " + ex.Message, ex);
            }
            finally { if (access) definition.ReleaseSelectionAccess(); }
        }

        private static string SupportLabel21(SketchSupportSnapshot20 support)
        {
            return support == null ? "support=False" : "kind=" + (support.Kind ?? "Unknown") +
                " area_m2=" + support.Area.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                " boundaryCount=" + (support.Boundaries == null ? 0 : support.Boundaries.Length);
        }

        public static void ReportCutAudit21(ModelDoc2 model, Feature profile, PlaneData plane,
            SketchSupportSnapshot20 after, CutAuditSnapshot21 source, string phase)
        {
            Audit21(model, "BEGIN phase=" + phase + " sketch=" + profile.Name + " report=" + model.GetPathName() + ".CutAudit21.txt");
            try
            {
                var sketch = profile.GetSpecificFeature2() as Sketch;
                var frame = sketch == null ? null : sketch.ModelToSketchTransform.ArrayData as double[];
                Audit21(model, "FRAME=" + (frame == null ? "unavailable" : string.Join(",", frame.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))));
                if (source == null || source.CaptureError != null)
                    Audit21(model, "INCOMPLETE source=" + (source == null ? "not captured" : source.CaptureError));
                Func<double[], double[]> reflect = p =>
                {
                    double d = Enumerable.Range(0, 3).Sum(i => (p[i] - plane.Origin[i]) * plane.Normal[i]);
                    return Enumerable.Range(0, 3).Select(i => p[i] - 2 * d * plane.Normal[i]).ToArray();
                };
                foreach (var pair in new[] {
                    Tuple.Create("BEFORE_CUT", source == null ? null : source.BeforeSupport), Tuple.Create("AFTER_CUT", after) })
                {
                    Audit21(model, "EXPECTED phase=" + pair.Item1 + " " + SupportLabel21(pair.Item2));
                    if (pair.Item2 == null || pair.Item2.Boundaries == null)
                    {
                        Audit21(model, "SUMMARY expected=" + pair.Item1 + " result=NON_FACE_SUPPORT diagnosticOnly=True");
                        continue;
                    }
                    var expected = pair.Item2.Boundaries.SelectMany(edge => edge).Select(reflect).ToArray();
                    if (expected.Length == 0) { Audit21(model, "INCOMPLETE empty boundary"); continue; }
                    int count = 0, close = 0, errors = 0;
                    foreach (Body2 body in ((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0])
                    foreach (Face2 face in body.GetFaces() as object[] ?? new object[0])
                    {
                        int index = count++;
                        try
                        {
                            double maxDistance = 0;
                            foreach (var p in expected)
                            {
                                var q = face.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                                if (q == null || q.Length < 3) throw new InvalidOperationException("Closest point unavailable");
                                maxDistance = Math.Max(maxDistance, Math.Sqrt(Enumerable.Range(0, 3).Sum(i => (p[i] - q[i]) * (p[i] - q[i]))));
                            }
                            double areaError = Math.Abs(face.GetArea() - pair.Item2.Area);
                            int edgeCount = (face.GetEdges() as object[] ?? new object[0]).Length;
                            bool near = maxDistance <= 1e-7;
                            if (near) close++;
                            Audit21(model, "FACE expected=" + pair.Item1 + " index=" + index + " creator=" +
                                ((face.GetFeature() as Feature)?.Name ?? "unknown") + " edges=" + edgeCount +
                                " areaError_m2=" + areaError.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                                " maxBoundaryToFace_m=" + maxDistance.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " near=" + near);
                        }
                        catch (Exception ex) { errors++; Audit21(model, "FACE_READ_FAILED index=" + index + " reason=" + ex.Message); }
                    }
                    Audit21(model, "SUMMARY expected=" + pair.Item1 + " faces=" + count + " nearCandidates=" + close + " unreadable=" + errors +
                        " result=" + (errors > 0 ? "INCOMPLETE" : close == 0 ? "NO_NEAR_FACE" : close == 1 ? "ONE_NEAR_FACE" : "MULTIPLE_NEAR_FACES") +
                        " diagnosticOnly=True notProofOfExactMatch=True");
                }
            }
            catch (Exception ex) { Audit21(model, "INCOMPLETE phase=" + phase + " reason=" + ex.Message); }
            Audit21(model, "END phase=" + phase + " sketch=" + profile.Name);
        }

        public static SketchSupportSnapshot20 CaptureSupport20(Sketch sketch, ModelDoc2 model = null)
        {
            if (sketch == null) return null;
            int type = 0;
            object reference = sketch.GetReferenceEntity(ref type);
            var face = reference as Face2;
            SketchSupportSnapshot20 result = face != null
                ? CaptureFaceSupport24(face)
                : new SketchSupportSnapshot20();
            result.Kind = face != null ? "Face" : reference is Feature ? "ReferencePlane" :
                reference == null ? "ImplicitPlane" : reference.GetType().Name;
            result.ReferenceType = type;
            try { result.PersistentReference = reference == null || model == null ? null : model.Extension.GetPersistReference3(reference) as byte[]; }
            catch { result.PersistentReference = null; }
            CaptureSketchPlane50(sketch, out result.PlaneOrigin, out result.PlaneNormal);
            CreateMirrorPartPackage.LogDebug("[SUPPORT50][CAPTURE] kind=" + result.Kind +
                " selectionType=" + type + " persistent=" + (result.PersistentReference != null && result.PersistentReference.Length > 0));
            return result;
        }

        private static SketchSupportSnapshot20 CaptureFaceSupport24(Face2 face)
        {
            if (face == null) return null;
            var snapshot = new SketchSupportSnapshot20 { Kind = "Face", Area = face.GetArea(), Boundaries =
                (face.GetEdges() as object[] ?? new object[0]).Cast<Edge>().Select(SampleEdge24).ToArray() };
            double residual = BoundaryResidual23(face, snapshot.Boundaries);
            CreateMirrorPartPackage.LogDebug("[EDGE24] FACE_SELF_CHECK residual_m=" + residual.ToString("R") +
                " result=" + (residual <= 1e-7 ? "PASS" : "FAIL"));
            if (residual > 1e-7) throw new InvalidOperationException("EDGE24 sampled boundary is not on its source face.");
            return snapshot;
        }

        // Shared by source capture and target matching. Keep the public name for callers.
        // EDGE25: never mix ICurve parameter space with the trimmed IEdge evaluator.
        public static double[][] SampleEdge24(Edge edge)
        {
            var curve = edge.GetCurve() as Curve;
            if (curve == null) throw new InvalidOperationException("EDGE25 curve unavailable.");
            var startVertex = edge.GetStartVertex() as Vertex;
            var endVertex = edge.GetEndVertex() as Vertex;
            double[] start = startVertex?.GetPoint() as double[];
            double[] end = endVertex?.GetPoint() as double[];
            bool line = curve.IsLine();
            double[][] samples;
            if (line)
            {
                RequirePoint25(start); RequirePoint25(end);
                samples = Enumerable.Range(0, 9).Select(i => Enumerable.Range(0, 3)
                    .Select(axis => start[axis] + (end[axis] - start[axis]) * i / 8.0).ToArray()).ToArray();
            }
            else
            {
                // GetCurveParams2 and Edge.Evaluate2 are a documented pair. No Sense sign fix.
                var parameters = edge.GetCurveParams2() as double[];
                if (parameters == null || parameters.Length < 8 || parameters.Take(8).Any(v => double.IsNaN(v) || double.IsInfinity(v)) || parameters[6] == parameters[7])
                    throw new InvalidOperationException("EDGE25 invalid edge parameter interval; type=" + curve.Identity());
                // Closed edges can have no topological vertices; retain their seam endpoints.
                if (start == null) start = parameters.Take(3).ToArray();
                if (end == null) end = parameters.Skip(3).Take(3).ToArray();
                samples = Enumerable.Range(0, 9).Select(i =>
                {
                    var value = edge.Evaluate2(parameters[6] + (parameters[7] - parameters[6]) * i / 8.0, 0) as double[];
                    RequirePoint25(value);
                    // Current API appends a packed status; older versions return XYZ only.
                    if (value.Length > 3 && BitConverter.ToInt32(BitConverter.GetBytes(value[3]), 0) != 1)
                        throw new InvalidOperationException("EDGE25 edge evaluation status failed; type=" + curve.Identity());
                    return value.Take(3).ToArray();
                }).ToArray();
            }
            RequirePoint25(start); RequirePoint25(end);
            Func<double[], double[], double> distance = (a, b) => Math.Sqrt(Enumerable.Range(0, 3).Sum(i => (a[i] - b[i]) * (a[i] - b[i])));
            double endpointError = Math.Min(Math.Max(distance(samples[0], start), distance(samples[8], end)),
                Math.Max(distance(samples[0], end), distance(samples[8], start)));
            if (double.IsNaN(endpointError) || endpointError > 1e-7)
                throw new InvalidOperationException("EDGE25 endpoint mismatch: type=" + curve.Identity() + " residual_m=" + endpointError.ToString("R"));
            double edgeError = 0;
            foreach (var point in samples)
            {
                var nearest = edge.GetClosestPointOn(point[0], point[1], point[2]) as double[];
                RequirePoint25(nearest);
                edgeError = Math.Max(edgeError, distance(point, nearest));
            }
            if (edgeError > 1e-7) throw new InvalidOperationException("EDGE25 sample outside trimmed edge; type=" + curve.Identity() + " residual_m=" + edgeError.ToString("R"));
            CreateMirrorPartPackage.LogDebug("[EDGE25] SAMPLE_PASS mode=" + (line ? "LINE_VERTICES" : "EDGE_EVALUATE") +
                " type=" + curve.Identity() + " endpointResidual_m=" + endpointError.ToString("R") + " edgeResidual_m=" + edgeError.ToString("R"));
            return samples;
        }

        private static void RequirePoint25(double[] value)
        {
            if (value == null || value.Length < 3 || value.Take(3).Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                throw new InvalidOperationException("EDGE25 invalid point data.");
        }

        private static void CaptureSketchPlane50(Sketch sketch, out double[] origin, out double[] normal)
        {
            origin = null;
            normal = null;
            try
            {
                var math = SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
                MathTransform sketchToModel = sketch.ModelToSketchTransform.IInverse();
                var modelOrigin = ((MathPoint)math.CreatePoint(new[] { 0.0, 0.0, 0.0 }))
                    .MultiplyTransform(sketchToModel) as MathPoint;
                var modelNormal = ((MathVector)math.CreateVector(new[] { 0.0, 0.0, 1.0 }))
                    .MultiplyTransform(sketchToModel) as MathVector;
                origin = modelOrigin.ArrayData as double[];
                normal = modelNormal.ArrayData as double[];
                double length = Math.Sqrt(normal.Take(3).Sum(value => value * value));
                if (length <= 1e-12) throw new InvalidOperationException("zero sketch-plane normal");
                normal = normal.Take(3).Select(value => value / length).ToArray();
            }
            catch
            {
                origin = null;
                normal = null;
            }
        }

        // An oblique mirror has no 2D reflection axis in the original sketch plane.
        // It is safe to use the existing model-space point/primitive replay only after
        // the copied sketch has actually been rehosted on the reflected source plane.
        public static bool VerifyReflectedSketchPlane56(Sketch target,
            SketchSupportSnapshot20 source, List<SketchPointSnapshot> points,
            PlaneData plane, out double normalError, out double planeResidual)
        {
            normalError = double.PositiveInfinity;
            planeResidual = double.PositiveInfinity;
            if (target == null || source?.PlaneOrigin == null || source.PlaneNormal == null ||
                source.PlaneOrigin.Length < 3 || source.PlaneNormal.Length < 3 ||
                points == null || points.Count == 0 || plane == null)
                return false;
            double[] targetOrigin, targetNormal;
            CaptureSketchPlane50(target, out targetOrigin, out targetNormal);
            if (targetOrigin == null || targetNormal == null) return false;
            double[] reflectedOrigin = BodyOperationsHelper.ReflectPointAcrossPlane(source.PlaneOrigin, plane);
            if (reflectedOrigin == null) return false;
            double[] expectedNormal = CutOrientation44.Reflect(source.PlaneNormal, plane.Normal);
            double[] actualNormal = CutOrientation44.Unit(targetNormal);
            double dot = Enumerable.Range(0, 3).Sum(i => expectedNormal[i] * actualNormal[i]);
            normalError = 1.0 - Math.Abs(dot);
            planeResidual = Math.Abs(Enumerable.Range(0, 3).Sum(i =>
                (reflectedOrigin[i] - targetOrigin[i]) * actualNormal[i]));
            foreach (SketchPointSnapshot point in points)
            {
                if (!point.HasModelCoords) return false;
                double[] reflected = BodyOperationsHelper.ReflectPointAcrossPlane(
                    new[] { point.ModelX, point.ModelY, point.ModelZ }, plane);
                if (reflected == null) return false;
                planeResidual = Math.Max(planeResidual, Math.Abs(Enumerable.Range(0, 3).Sum(i =>
                    (reflected[i] - targetOrigin[i]) * actualNormal[i])));
            }
            return !double.IsNaN(normalError) && !double.IsInfinity(normalError) &&
                normalError <= 1e-8 && planeResidual <= 1e-7;
        }

        private static double[] SketchPointWorld50(SketchPoint point)
        {
            if (point == null) return null;
            var owner = point.GetSketch() as Sketch;
            if (owner == null) return null;
            var math = SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
            MathTransform transform = owner.ModelToSketchTransform.IInverse();
            var local = math.CreatePoint(new[] { point.X, point.Y, point.Z }) as MathPoint;
            var world = local == null ? null : local.MultiplyTransform(transform) as MathPoint;
            return world == null ? null : (world.ArrayData as double[])?.Take(3).ToArray();
        }

        private static IEnumerable<Feature> EnumerateFeatures50(ModelDoc2 model)
        {
            Feature current = model == null ? null : model.FirstFeature() as Feature;
            while (current != null)
            {
                yield return current;
                Feature child = current.GetFirstSubFeature() as Feature;
                while (child != null)
                {
                    yield return child;
                    child = child.GetNextSubFeature() as Feature;
                }
                current = current.GetNextFeature() as Feature;
            }
        }

        private static SketchPoint FindReflectedSketchPoint50(ModelDoc2 model, double[] sourceWorld, PlaneData plane)
        {
            if (sourceWorld == null || sourceWorld.Length < 3)
                throw new InvalidOperationException("REFERENCE50: SketchPoint world coordinates unavailable.");
            double[] expected = ReflectReference30(sourceWorld, plane);
            var candidates = new List<Tuple<SketchPoint, double>>();
            foreach (Feature feature in EnumerateFeatures50(model))
            {
                Sketch owner = null;
                try { owner = feature.GetSpecificFeature2() as Sketch; } catch { }
                if (owner == null) continue;
                foreach (SketchPoint candidate in (owner.GetSketchPoints2() as object[] ?? new object[0]).OfType<SketchPoint>())
                {
                    double[] actual;
                    try { actual = SketchPointWorld50(candidate); } catch { continue; }
                    if (actual == null) continue;
                    double distance = Math.Sqrt(Enumerable.Range(0, 3).Sum(i => Math.Pow(actual[i] - expected[i], 2)));
                    if (distance <= 1e-7)
                    {
                        bool duplicate = candidates.Any(existing =>
                        {
                            try { return SwAddin.InstanceSwApp.IsSame(existing.Item1, candidate) == (int)swObjectEquality.swObjectSame; }
                            catch { return false; }
                        });
                        if (!duplicate) candidates.Add(Tuple.Create(candidate, distance));
                    }
                }
            }
            var unique = candidates.OrderBy(item => item.Item2).ToArray();
            CreateMirrorPartPackage.LogDebug("[REFERENCE50][SKETCH_POINT_RESOLVE] matches=" + unique.Length +
                " expected_m=" + string.Join(",", expected.Select(value => value.ToString("R"))));
            if (unique.Length != 1)
                throw new InvalidOperationException("REFERENCE50: Reflected SketchPoint is not unique; matches=" + unique.Length);
            return unique[0].Item1;
        }

        public static void EnsureCutSupport52(ISldWorks app, ModelDoc2 model, Feature cut,
            Feature profile, CutAuditSnapshot21 snapshot, PlaneData plane)
        {
            if (snapshot == null || snapshot.CaptureError != null || snapshot.BeforeSupport == null ||
                snapshot.CirclePoints26 == null || snapshot.CirclePoints26.Count == 0)
                throw new InvalidOperationException("CUT52 before-cut support snapshot is incomplete.");
            string cutName = cut.Name, profileName = profile.Name;
            model.ClearSelection2(true);
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, cutName))
                throw new InvalidOperationException("CUT52 cannot enter before-cut checkpoint: " + cutName);
            try
            {
                // Reacquire inside this checkpoint; no face from AccessSelections
                // survives ReleaseSelectionAccess or a rollback transition here.
                var liveProfile = ((PartDoc)model).FeatureByName(profileName) as Feature;
                if (liveProfile == null) throw new InvalidOperationException("CUT52 profile unavailable before cut: " + profileName);
                Audit21(model, "[CUT52][SUPPORT_BEGIN] feature=" + cutName +
                    " checkpoint=BEFORE_CUT snapshot=BEFORE_CUT");
                EnsureReflectedSupport20(app, model, liveProfile, snapshot.CirclePoints26,
                    snapshot.BeforeSupport, plane);
                Audit21(model, "[CUT52][SUPPORT_PASS] feature=" + cutName);
            }
            finally
            {
                model.ClearSelection2(true);
                bool restored = model.FeatureManager.EditRollback(
                    (int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, cutName);
                Audit21(model, "[CUT52][CHECKPOINT_RESTORED] feature=" + cutName + " success=" + restored);
                if (!restored) throw new InvalidOperationException("CUT52 cannot restore after-cut checkpoint: " + cutName);
            }
        }

        public static void EnsureReflectedSupport20(ISldWorks app, ModelDoc2 model, Feature feature,
            List<SketchPointSnapshot> points, SketchSupportSnapshot20 support, PlaneData plane)
        {
            if (points == null || points.Count == 0)
                throw new InvalidOperationException("CUTSPACE20: No original sketch points.");
            var math = (IMathUtility)app.GetMathUtility();
            double normSquared52 = plane.Normal.Sum(v => v * v);
            if (double.IsNaN(normSquared52) || double.IsInfinity(normSquared52) || normSquared52 < 1e-20)
                throw new InvalidOperationException("CUT52 invalid reflection normal.");
            Func<double[], double[]> reflect = p =>
            {
                double d = Enumerable.Range(0, 3).Sum(i => (p[i] - plane.Origin[i]) * plane.Normal[i]) / normSquared52;
                return Enumerable.Range(0, 3).Select(i => p[i] - 2 * d * plane.Normal[i]).ToArray();
            };
            var targets = points.Select(p =>
            {
                if (!p.HasModelCoords) throw new InvalidOperationException("CUTSPACE20: Original model coordinates unavailable for point " + p.Id1 + "," + p.Id2);
                return reflect(new[] { p.ModelX, p.ModelY, p.ModelZ });
            }).ToArray();
            Func<double> residual = () =>
            {
                var sketch = (Sketch)feature.GetSpecificFeature2();
                return targets.Max(p =>
                {
                    var point = (MathPoint)math.CreatePoint(p);
                    var local = (MathPoint)point.MultiplyTransform(sketch.ModelToSketchTransform);
                    return Math.Abs(((double[])local.ArrayData)[2]);
                });
            };
            double before = residual();
            CreateMirrorPartPackage.LogDebug("[CUTSPACE20][PLANE_CHECK] sketch=" + feature.Name + " maxResidual_m=" + before.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (before <= 1e-7) return;
            if (support == null)
                throw new InvalidOperationException("CUTSPACE50: Source support was not captured: " + feature.Name);
            Face2 mappedFace = null;
            if (support.Boundaries != null)
            {
                var boundaries = support.Boundaries.Select(edges => edges.Select(reflect).ToArray()).ToArray();
                mappedFace = (Face2)ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindFace(model, support.Area, boundaries);
            }
            else
            {
                mappedFace = FindPlanarSupportForPoints50(model, targets);
            }
            if (mappedFace == null)
                throw new InvalidOperationException("CUTSPACE50: No unique reflected planar support for " + support.Kind + ": " + feature.Name);
            model.ClearSelection2(true);
            if (!feature.Select2(false, 0) || !((Entity)mappedFace).Select4(true, null) || !model.ChangeSketchPlane())
                throw new InvalidOperationException("CUTSPACE20: Cannot apply reflected support: " + feature.Name);
            double after = residual();
            CreateMirrorPartPackage.LogDebug("[CUTSPACE20][SUPPORT_REBOUND] sketch=" + feature.Name + " maxResidual_m=" + after.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (after > 1e-7) throw new InvalidOperationException("CUTSPACE20: Mapped support does not contain reflected points.");
        }

        private static Face2 FindPlanarSupportForPoints50(ModelDoc2 model, double[][] targets)
        {
            var matches = new List<Tuple<Face2, double>>();
            foreach (Body2 body in (object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0])
                foreach (Face2 face in (object[])body.GetFaces() ?? new object[0])
                {
                    try
                    {
                        Surface surface = face.GetSurface() as Surface;
                        if (surface == null || !surface.IsPlane()) continue;
                        double residual = 0.0;
                        foreach (double[] target in targets)
                        {
                            double[] closest = surface.GetClosestPointOn(target[0], target[1], target[2]) as double[];
                            if (closest == null || closest.Length < 3) { residual = double.PositiveInfinity; break; }
                            residual = Math.Max(residual, Math.Sqrt(Enumerable.Range(0, 3)
                                .Sum(i => Math.Pow(target[i] - closest[i], 2))));
                        }
                        if (residual <= 1e-7) matches.Add(Tuple.Create(face, residual));
                    }
                    catch { }
                }
            CreateMirrorPartPackage.LogDebug("[SUPPORT50][PLANAR_TARGET_SEARCH] candidates=" + matches.Count);
            return matches.Count == 1 ? matches[0].Item1 : null;
        }

        public static List<SketchPointSnapshot> CapturePristineSketchPoints(Sketch swSketch)
        {
            List<SketchPointSnapshot> list = new List<SketchPointSnapshot>();
            if (swSketch == null) return list;

            MathTransform s2m = null;
            try
            {
                MathTransform m2s = swSketch.ModelToSketchTransform;
                if (m2s != null) s2m = m2s.Inverse() as MathTransform;
            }
            catch { }

            object[] sketchPointsObj = EnumerateSketchPoints28(swSketch);
            if (sketchPointsObj.Length == 0) return list;

            ISldWorks swApp = SwAddin.InstanceSwApp;
            IMathUtility mathUtility = swApp != null ? swApp.GetMathUtility() as IMathUtility : null;

            for (int i = 0; i < sketchPointsObj.Length; i++)
            {
                SketchPoint pt = sketchPointsObj[i] as SketchPoint;
                if (pt == null) continue;

                int id1 = 0, id2 = 0;
                try
                {
                    int[] idArr = pt.GetID() as int[];
                    if (idArr != null && idArr.Length >= 2)
                    {
                        id1 = idArr[0];
                        id2 = idArr[1];
                    }
                }
                catch { }

                double mx = 0, my = 0, mz = 0;
                bool hasModel = false;
                if (s2m != null && mathUtility != null)
                {
                    try
                    {
                        MathPoint mp2D = mathUtility.CreatePoint(new double[] { pt.X, pt.Y, swSketch.Is3D() ? pt.Z : 0.0 }) as MathPoint;
                        MathPoint mp3D = mp2D != null ? mp2D.MultiplyTransform(s2m) as MathPoint : null;
                        double[] a = mp3D != null ? mp3D.ArrayData as double[] : null;
                        if (a != null && a.Length >= 3)
                        {
                            mx = a[0]; my = a[1]; mz = a[2];
                            hasModel = true;
                        }
                    }
                    catch { }
                }

                list.Add(new SketchPointSnapshot
                {
                    Id1 = id1,
                    Id2 = id2,
                    Index = i,
                    X = pt.X,
                    Y = pt.Y,
                    ModelX = mx,
                    ModelY = my,
                    ModelZ = mz,
                    HasModelCoords = hasModel
                });
            }

            return list;
        }

        // GetSketchPoints2 omits points owned by some construction/reference segments.
        // Dimensions can reference those points, so the reflection set must include them too.
        private static double[] ReflectReference30(double[] point, PlaneData plane)
        {
            if (plane == null) throw new InvalidOperationException("REFERENCE30: Missing reflection plane.");
            double norm = plane.Normal.Sum(x => x * x);
            if (norm < 1e-20) throw new InvalidOperationException("REFERENCE30: Invalid reflection normal.");
            double d = Enumerable.Range(0, 3).Sum(i => (point[i] - plane.Origin[i]) * plane.Normal[i]) / norm;
            return Enumerable.Range(0, 3).Select(i => point[i] - 2 * d * plane.Normal[i]).ToArray();
        }

        private static bool IsOwnedEntity31(Sketch owner, object entity)
        {
            if (owner == null || entity == null) return false;
            Func<object, object, bool> same = (a, b) =>
            {
                try { return SwAddin.InstanceSwApp.IsSame(a, b) == (int)swObjectEquality.swObjectSame; }
                catch { return false; }
            };
            Func<int[], int[], bool> ids = (a, b) => a != null && b != null && a.SequenceEqual(b);
            var segment = entity as SketchSegment;
            if (segment != null)
            {
                if (!same(segment.GetSketch(), owner)) return false;
                var source = SegmentPoints31(segment);
                var matches = (owner.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>().Count(candidate =>
                    candidate.GetType() == segment.GetType() && ids(candidate.GetID() as int[], segment.GetID() as int[]) &&
                    GeometryResidual31(source, SegmentPoints31(candidate)) <= 1e-9);
                return matches == 1;
            }
            var point = entity as SketchPoint;
            if (point != null)
            {
                if (!same(point.GetSketch(), owner)) return false;
                var matches = (owner.GetSketchPoints2() as object[] ?? new object[0]).Cast<SketchPoint>().Count(candidate =>
                    ids(candidate.GetID() as int[], point.GetID() as int[]) &&
                    Math.Sqrt(Math.Pow(candidate.X - point.X, 2) + Math.Pow(candidate.Y - point.Y, 2) + Math.Pow(candidate.Z - point.Z, 2)) <= 1e-9);
                return matches == 1;
            }
            return false;
        }

        private static double[][] SegmentPoints31(SketchSegment segment)
        {
            Func<SketchPoint, double[]> p = value => new[] { value.X, value.Y, value.Z };
            var line = segment as SketchLine;
            if (line != null) return new[] { p((SketchPoint)line.GetStartPoint2()), p((SketchPoint)line.GetEndPoint2()) };
            var arc = segment as SketchArc;
            if (arc != null) return new[] { p((SketchPoint)arc.GetStartPoint2()), p((SketchPoint)arc.GetCenterPoint2()), p((SketchPoint)arc.GetEndPoint2()) };
            return null;
        }

        private static double GeometryResidual31(double[][] a, double[][] b)
        {
            if (a == null || b == null || a.Length != b.Length) return double.PositiveInfinity;
            Func<double[], double[], double> distance = (x, y) => Math.Sqrt(Enumerable.Range(0, 3).Sum(i => Math.Pow(x[i] - y[i], 2)));
            double direct = Enumerable.Range(0, a.Length).Max(i => distance(a[i], b[i]));
            double reverse = Enumerable.Range(0, a.Length).Max(i => distance(a[i], b[b.Length - 1 - i]));
            return Math.Min(direct, reverse);
        }

        private static Face2 FindCarrierFace31(ModelDoc2 model, SketchLine line)
        {
            var math = SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
            var owner = ((SketchSegment)line).GetSketch();
            var transform = owner.ModelToSketchTransform.IInverse();
            Func<SketchPoint, double[]> world = point => (double[])((MathPoint)((MathPoint)math.CreatePoint(
                new[] { point.X, point.Y, point.Z })).MultiplyTransform(transform)).ArrayData;
            double[] a = world((SketchPoint)line.GetStartPoint2());
            double[] b = world((SketchPoint)line.GetEndPoint2());
            double[][] probes = Enumerable.Range(0, 9).Select(k => Enumerable.Range(0, 3)
                .Select(i => a[i] + (b[i] - a[i]) * k / 8.0).ToArray()).ToArray();
            var matches = new List<Face2>();
            var surfaceCandidates45 = new List<Tuple<Face2, double, double, string>>();
            foreach (Body2 body in (object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0])
                foreach (Face2 face in (object[])body.GetFaces() ?? new object[0])
                {
                    double trimmedResidual45 = 0.0;
                    bool trimmedReadable45 = true;
                    foreach (double[] probe in probes)
                    {
                        var hit = face.GetClosestPointOn(probe[0], probe[1], probe[2]) as double[];
                        if (hit == null || hit.Length < 3) { trimmedReadable45 = false; break; }
                        trimmedResidual45 = Math.Max(trimmedResidual45, Math.Sqrt(Enumerable.Range(0, 3)
                            .Sum(i => Math.Pow(probe[i] - hit[i], 2))));
                    }
                    if (trimmedReadable45 && trimmedResidual45 <= 1e-7) matches.Add(face);

                    // A solver proxy may lie on the untrimmed support surface but outside
                    // the trimmed boundary of its carrier face. Query ISurface directly so
                    // this also covers cylindrical bends, cones, spheres, tori and NURBS.
                    try
                    {
                        var surface45 = face.GetSurface() as Surface;
                        if (surface45 == null || !trimmedReadable45) continue;
                        double supportResidual45 = 0.0;
                        bool supportReadable45 = true;
                        foreach (double[] probe in probes)
                        {
                            var supportHit45 = surface45.GetClosestPointOn(probe[0], probe[1], probe[2]) as double[];
                            if (supportHit45 == null || supportHit45.Length < 3)
                            {
                                supportReadable45 = false;
                                break;
                            }
                            supportResidual45 = Math.Max(supportResidual45, Math.Sqrt(Enumerable.Range(0, 3)
                                .Sum(i => Math.Pow(probe[i] - supportHit45[i], 2))));
                        }
                        string surfaceKind45 = surface45.IsPlane() ? "Plane" :
                            surface45.IsCylinder() ? "Cylinder" :
                            surface45.IsCone() ? "Cone" :
                            surface45.IsSphere() ? "Sphere" :
                            surface45.IsTorus() ? "Torus" : "General";
                        if (supportReadable45 && supportResidual45 <= 1e-7)
                            surfaceCandidates45.Add(Tuple.Create(face, trimmedResidual45, supportResidual45, surfaceKind45));
                    }
                    catch { }
                }
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1)
                throw new InvalidOperationException("REFERENCE35: Proxy carrier Face is not unique; matches=" + matches.Count);

            var ranked45 = surfaceCandidates45.OrderBy(candidate => candidate.Item2).ToArray();
            if (ranked45.Length == 0)
                throw new InvalidOperationException("REFERENCE35: Proxy carrier Face is not unique; matches=0; surfaceSupportMatches=0; proxy=" +
                    string.Join(",", a.Select(value => value.ToString("R"))) + "->" +
                    string.Join(",", b.Select(value => value.ToString("R"))));
            double best45 = ranked45[0].Item2;
            double second45 = ranked45.Length > 1 ? ranked45[1].Item2 : double.PositiveInfinity;
            double uniquenessTolerance45 = Math.Max(1e-7, Math.Max(best45, 1e-7) * 1e-6);
            CreateMirrorPartPackage.LogDebug("[REFERENCE45][CARRIER_SURFACE_FALLBACK] candidates=" + ranked45.Length +
                " bestTrimmedResidual_m=" + best45.ToString("R") + " secondTrimmedResidual_m=" +
                second45.ToString("R") + " bestSurfaceResidual_m=" + ranked45[0].Item3.ToString("R") +
                " bestSurfaceKind=" + ranked45[0].Item4 + " uniquenessTolerance_m=" + uniquenessTolerance45.ToString("R"));
            if (ranked45.Length > 1 && second45 - best45 <= uniquenessTolerance45)
                throw new InvalidOperationException("REFERENCE45: Coplanar carrier Face is ambiguous; candidates=" +
                    ranked45.Length + " best=" + best45.ToString("R") + " second=" + second45.ToString("R"));
            return ranked45[0].Item1;
        }

        private static object[] DefinitionEntities31(SketchRelation relation)
        {
            var actual = relation.GetDefinitionEntities2() as object[];
            if (actual == null) throw new InvalidOperationException("REFERENCE31: Actual relation definition unavailable.");
            // Some dimensions expose their support only through a solver segment.
            // Keep that segment as an external reference, never as movable geometry.
            var internalEntities = relation.GetEntities() as object[];
            var result = (object[])actual.Clone();
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] != null) continue;
                if (internalEntities == null || internalEntities.Length != result.Length || internalEntities[i] == null)
                    throw new InvalidOperationException("REFERENCE31: Unresolved null relation entity at index " + i);
                result[i] = internalEntities[i];
            }
            return result;
        }

        private static string RelationKey30(SketchRelation relation, Sketch owner)
        {
            var display = relation.GetDisplayDimension() as DisplayDimension;
            var dim = display == null ? null : display.GetDimension() as Dimension;
            var parts = new List<string> { relation.GetRelationType().ToString(), dim == null ? "" : dim.FullName };
            foreach (object entity in DefinitionEntities31(relation))
            {
                var point = entity as SketchPoint;
                var segment = entity as SketchSegment;
                if (point != null && IsOwnedEntity31(owner, point))
                    parts.Add("P:" + string.Join(",", (int[])point.GetID()));
                else if (segment != null && IsOwnedEntity31(owner, segment))
                    parts.Add("S:" + string.Join(",", (int[])segment.GetID()));
                else parts.Add(entity == null ? "implicit" : "external");
            }
            return string.Join("|", parts);
        }

        private static List<RelationReference30> CaptureReferences30(ModelDoc2 model, Sketch sketch)
        {
            var result = new List<RelationReference30>();
            var relations = (sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll)
                as object[] ?? new object[0]).Cast<SketchRelation>().ToArray();
            foreach (var relation in relations)
            {
                var entities = DefinitionEntities31(relation);
                if (entities == null) throw new InvalidOperationException("REFERENCE30: Actual relation definition unavailable.");
                string key = RelationKey30(relation, sketch);
                var display31 = relation.GetDisplayDimension() as DisplayDimension;
                if (display31 != null)
                    CreateMirrorPartPackage.LogDebug("[REFERENCE31][DIMENSION_COVERAGE] relation=" + key +
                        " owned=" + entities.Count(e => IsOwnedEntity31(sketch, e)) +
                        " external=" + entities.Count(e => !IsOwnedEntity31(sketch, e)));
                for (int index = 0; index < entities.Length; index++)
                {
                    object entity = entities[index];
                    if (entity == null) continue; // implicit entity, not a movable proxy
                    var point = entity as SketchPoint;
                    var segment = entity as SketchSegment;
                    if (IsOwnedEntity31(sketch, entity)) continue;
                    Func<ModelDoc2, PlaneData, object> resolve;
                    Func<ModelDoc2, object, object, bool> verify = null;
                    Func<PlaneData, double[]> pickPoint31 = null;
                    Func<PlaneData, double[][]> geometry31 = null;
                    string kind31 = null;
                    var edge = entity as Edge;
                    var face = entity as Face2;
                    var vertex = entity as Vertex;
                    if (edge != null)
                    {
                        kind31 = "Edge";
                        double[][] samples = SampleEdge24(edge);
                        resolve = (m, p) => ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindEdge(
                            m, samples.Select(x => ReflectReference30(x, p)).ToArray());
                    }
                    else if (face != null)
                    {
                        kind31 = "Face";
                        var support = CaptureFaceSupport24(face);
                        resolve = (m, p) => ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindFace(
                            m, support.Area, support.Boundaries.Select(b => b.Select(x => ReflectReference30(x, p)).ToArray()).ToArray());
                    }
                    else if (vertex != null)
                    {
                        kind31 = "Vertex";
                        double[] position = (double[])vertex.GetPoint();
                        resolve = (m, p) => ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindVertex(m, ReflectReference30(position, p));
                    }
                    else if (point != null)
                    {
                        kind31 = "SketchPoint";
                        double[] sourceWorld50 = SketchPointWorld50(point);
                        byte[] persistent50 = null;
                        try { persistent50 = model.Extension.GetPersistReference3(point) as byte[]; } catch { }
                        resolve = (m, p) =>
                        {
                            double[] expected50 = ReflectReference30(sourceWorld50, p);
                            if (persistent50 != null && persistent50.Length > 0)
                            {
                                try
                                {
                                    int state50;
                                    var persisted50 = m.Extension.GetObjectByPersistReference3(persistent50, out state50) as SketchPoint;
                                    double[] persistedWorld50 = SketchPointWorld50(persisted50);
                                    if (persistedWorld50 != null && Math.Sqrt(Enumerable.Range(0, 3)
                                        .Sum(axis50 => Math.Pow(persistedWorld50[axis50] - expected50[axis50], 2))) <= 1e-7)
                                    {
                                        CreateMirrorPartPackage.LogDebug("[REFERENCE50][SKETCH_POINT_RESOLVE] mode=Persistent state=" + state50);
                                        return persisted50;
                                    }
                                }
                                catch { }
                            }
                            return FindReflectedSketchPoint50(m, sourceWorld50, p);
                        };
                    }
                    else if (segment is SketchLine)
                    {
                        var line = (SketchLine)segment;
                        var math = SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
                        var transform = segment.GetSketch().ModelToSketchTransform.IInverse();
                        Func<SketchPoint, double[]> world = p => (double[])((MathPoint)((MathPoint)math.CreatePoint(
                            new[] { p.X, p.Y, p.Z })).MultiplyTransform(transform)).ArrayData;
                        double[] a = world((SketchPoint)line.GetStartPoint2());
                        double[] b = world((SketchPoint)line.GetEndPoint2());
                        double[][] samples = Enumerable.Range(0, 9).Select(i => Enumerable.Range(0, 3)
                            .Select(j => a[j] + (b[j] - a[j]) * i / 8.0).ToArray()).ToArray();
                        try
                        {
                            ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindEdge(model, samples);
                            kind31 = "ProxyLineToEdge";
                            resolve = (m, p) => ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindEdge(
                                m, samples.Select(x => ReflectReference30(x, p)).ToArray());
                        }
                        catch (InvalidOperationException)
                        {
                            Face2 carrier = FindCarrierFace31(model, line);
                            var support = CaptureFaceSupport24(carrier);
                            double[][] reflectedSamples31 = null;
                            kind31 = "ProxyLineToFace";
                            pickPoint31 = p => ReflectReference30(samples[samples.Length / 2], p);
                            geometry31 = p => new[]
                            {
                                ReflectReference30(samples[0], p),
                                ReflectReference30(samples[samples.Length - 1], p)
                            };
                            resolve = (m, p) =>
                            {
                                reflectedSamples31 = samples.Select(x => ReflectReference30(x, p)).ToArray();
                                return ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindFace(
                                    m, support.Area, support.Boundaries.Select(boundary => boundary.Select(x => ReflectReference30(x, p)).ToArray()).ToArray());
                            };
                            verify = (m, actual, target) =>
                            {
                                try
                                {
                                    if (actual is Face2 && SwAddin.InstanceSwApp.IsSame(actual, target) == (int)swObjectEquality.swObjectSame)
                                        return true;
                                    var actualLine = actual as SketchLine;
                                    if (actualLine == null || reflectedSamples31 == null) return false;
                                    var actualOwner = ((SketchSegment)actualLine).GetSketch();
                                    var actualTransform = actualOwner.ModelToSketchTransform.IInverse();
                                    Func<SketchPoint, double[]> actualWorld = sketchPoint31 => (double[])((MathPoint)((MathPoint)math.CreatePoint(
                                        new[] { sketchPoint31.X, sketchPoint31.Y, sketchPoint31.Z })).MultiplyTransform(actualTransform)).ArrayData;
                                    double[][] actualEndpoints = new[]
                                    {
                                        actualWorld((SketchPoint)actualLine.GetStartPoint2()),
                                        actualWorld((SketchPoint)actualLine.GetEndPoint2())
                                    };
                                    double[][] expectedEndpoints = new[]
                                    {
                                        reflectedSamples31[0],
                                        reflectedSamples31[reflectedSamples31.Length - 1]
                                    };
                                    double residual = GeometryResidual31(actualEndpoints, expectedEndpoints);
                                    CreateMirrorPartPackage.LogDebug("[REFERENCE36][PROXY_GEOMETRY] residual_m=" + residual.ToString("R") +
                                        " carrierSame=" + (SwAddin.InstanceSwApp.IsSame(FindCarrierFace31(m, actualLine), target) == (int)swObjectEquality.swObjectSame));
                                    return residual <= 1e-7 &&
                                        SwAddin.InstanceSwApp.IsSame(FindCarrierFace31(m, actualLine), target) == (int)swObjectEquality.swObjectSame;
                                }
                                catch { return false; }
                            };
                        }
                        CreateMirrorPartPackage.LogDebug("[REFERENCE35][PROXY_SOURCE_VERIFIED] relation=" + key + " kind=" + kind31);
                    }
                    else
                    {
                        string description = DiagnosticEntity(entity);
                        kind31 = "Unsupported";
                        CreateMirrorPartPackage.LogDebug("[REFERENCE47][UNSUPPORTED_REFERENCE] relation=" + key +
                            " entity=" + index + " description=" + description);
                        // Never move an external sketch or a solver proxy to make a dimension fit.
                        resolve = (m, p) => { throw new InvalidOperationException("REFERENCE30: Unsupported actual reference: " + description); };
                    }
                    if (verify == null) verify = (m, actual, target) =>
                    {
                        try { return SwAddin.InstanceSwApp.IsSame(actual, target) == (int)swObjectEquality.swObjectSame; }
                        catch { return false; }
                    };
                    result.Add(new RelationReference30 { Key = key, SourceRelation31 = relation, EntityIndex = index,
                        EntityCount = entities.Length, Resolve = resolve, Verify31 = verify,
                        PickPoint31 = pickPoint31, Geometry31 = geometry31, Kind31 = kind31 });
                    CreateMirrorPartPackage.LogDebug("[REFERENCE30][CAPTURE] relation=" + key + " entity=" + index + " kind=" +
                        (kind31 ?? "Unsupported"));
                }
            }
            return result;
        }

        private static bool RelationEntityMatchesKind49(object entity, string kind)
        {
            if (string.Equals(kind, "Edge", StringComparison.Ordinal)) return entity is Edge;
            if (string.Equals(kind, "Face", StringComparison.Ordinal)) return entity is Face2;
            if (string.Equals(kind, "Vertex", StringComparison.Ordinal)) return entity is Vertex;
            if (string.Equals(kind, "SketchPoint", StringComparison.Ordinal)) return entity is SketchPoint;
            // A solver proxy is captured as a SketchLine. Replacing that shared proxy in
            // one relation can update the entity exposed by every relation that uses it.
            // Therefore later snapshots must recognize both the pre-rebind proxy and its
            // already-resolved topological target. This is not a feature-name special case;
            // it follows the target type encoded by the captured reference kind.
            if (string.Equals(kind, "ProxyLineToFace", StringComparison.Ordinal))
                return entity is SketchLine || entity is Face2;
            if (string.Equals(kind, "ProxyLineToEdge", StringComparison.Ordinal))
                return entity is SketchLine || entity is Edge;
            if (string.Equals(kind, "Unsupported", StringComparison.Ordinal)) return true;
            return false;
        }

        private static void RebindReferences30(ModelDoc2 model, Sketch sketch, CutAuditSnapshot21 audit, PlaneData plane, bool verifyOnly = false)
        {
            if (audit == null) return; // Base sketch has its separate mutation path.
            if (audit.References30 == null) throw new InvalidOperationException("REFERENCE30: Source references were not captured.");
            // Resolve every target first. No partial replacement on a missing/ambiguous target.
            var resolved = audit.References30.Select(r => Tuple.Create(r, r.Resolve(model, plane))).ToList();
            foreach (var pair in resolved)
            {
                var snapshot = pair.Item1;
                var liveRelations49 = (sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll)
                    as object[] ?? new object[0]).Cast<SketchRelation>().ToArray();
                var identityMatches49 = liveRelations49.Where(r => SameProbeObject(r, snapshot.SourceRelation31)).ToArray();
                string identityMethod49;
                SketchRelation[] matches;
                if (identityMatches49.Length == 1)
                {
                    matches = identityMatches49;
                    identityMethod49 = "ComIdentity";
                }
                else
                {
                    matches = liveRelations49.Where(r => RelationKey30(r, sketch) == snapshot.Key).ToArray();
                    identityMethod49 = "StructuralKey";
                    if (matches.Length > 1)
                    {
                        matches = matches.Where(candidate49 =>
                        {
                            try
                            {
                                object[] entities49 = DefinitionEntities31(candidate49);
                                return entities49 != null && entities49.Length == snapshot.EntityCount &&
                                    snapshot.EntityIndex >= 0 && snapshot.EntityIndex < entities49.Length &&
                                    RelationEntityMatchesKind49(entities49[snapshot.EntityIndex], snapshot.Kind31);
                            }
                            catch { return false; }
                        }).ToArray();
                        identityMethod49 = "StructuralKey+EntityKind";
                    }
                }
                if (matches.Length != 1) throw new InvalidOperationException("REFERENCE30: Live relation identity is not unique: " + snapshot.Key);
                var relation = matches[0];
                CreateMirrorPartPackage.LogDebug("[REFERENCE49][RELATION_IDENTITY] relation=" + snapshot.Key +
                    " method=" + identityMethod49 + " matches=" + matches.Length + " kind=" + snapshot.Kind31);
                if (RelationKey30(relation, sketch) != snapshot.Key)
                    throw new InvalidOperationException("REFERENCE31: Relation structure changed: " + snapshot.Key);
                var entities = DefinitionEntities31(relation);
                if (entities == null || entities.Length != snapshot.EntityCount || pair.Item2 == null)
                    throw new InvalidOperationException("REFERENCE30: Relation definition changed.");
                object oldEntity = entities[snapshot.EntityIndex];
                bool invariant = snapshot.Verify31(model, oldEntity, pair.Item2);
                if (verifyOnly && !invariant)
                    throw new InvalidOperationException("REFERENCE30: Reference changed after solver restoration: " + snapshot.Key);
                if (!invariant && !relation.ReplaceEntity(oldEntity, pair.Item2))
                    throw new InvalidOperationException("REFERENCE30: ReplaceEntity rejected: " + snapshot.Key);
                var readback = DefinitionEntities31(relation);
                if (readback == null || readback.Length != entities.Length || !snapshot.Verify31(model, readback[snapshot.EntityIndex], pair.Item2))
                    throw new InvalidOperationException("REFERENCE30: Reference readback mismatch: " + snapshot.Key);
                CreateMirrorPartPackage.LogDebug("[REFERENCE35][BOUND] relation=" + snapshot.Key + " kind=" + snapshot.Kind31 +
                    " invariant=" + invariant + " verified=True");
            }
        }

        private static bool HasReflectedProxyDimension37(CutAuditSnapshot21 audit, string dimensionFullName)
        {
            return audit != null && audit.References30 != null &&
                audit.References30.Any(reference => reference.Kind31 == "ProxyLineToFace" &&
                    reference.Key != null && reference.Key.IndexOf("|" + dimensionFullName + "|", StringComparison.Ordinal) >= 0);
        }

        private static Dimension RecreateReflectedProxyDimension38(ModelDoc2 model, Sketch sketch,
            CutAuditSnapshot21 audit, PlaneData plane, string oldFullName, double systemValue)
        {
            var snapshot = audit.References30.Single(reference => reference.Kind31 == "ProxyLineToFace" &&
                reference.Key.IndexOf("|" + oldFullName + "|", StringComparison.Ordinal) >= 0);
            var relations = (sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll)
                as object[] ?? new object[0]).Cast<SketchRelation>().ToArray();
            var matches = relations.Where(candidateRelation => RelationKey30(candidateRelation, sketch) == snapshot.Key).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("REFERENCE38: Proxy dimension relation is not unique: " + oldFullName);
            var relation = matches[0];
            var entities = DefinitionEntities31(relation);
            object owned = entities.SingleOrDefault(entity => entity != null && IsOwnedEntity31(sketch, entity));
            if (owned == null) throw new InvalidOperationException("REFERENCE38: Owned dimension entity was not found: " + oldFullName);
            var ownedSegmentBeforeDelete = owned as SketchSegment;
            var ownedPointBeforeDelete = owned as SketchPoint;
            int[] ownedId38 = ownedSegmentBeforeDelete != null
                ? ownedSegmentBeforeDelete.GetID() as int[]
                : ownedPointBeforeDelete == null ? null : ownedPointBeforeDelete.GetID() as int[];
            int ownedSegmentType38 = ownedSegmentBeforeDelete == null ? -1 : ownedSegmentBeforeDelete.GetType();
            double[][] ownedGeometry38 = ownedSegmentBeforeDelete == null ? null : SegmentPoints31(ownedSegmentBeforeDelete);
            double[] ownedPointGeometry38 = ownedPointBeforeDelete == null ? null :
                new[] { ownedPointBeforeDelete.X, ownedPointBeforeDelete.Y, ownedPointBeforeDelete.Z };
            Func<object> reacquireOwned38 = () =>
            {
                if (ownedSegmentBeforeDelete != null)
                {
                    var candidates = (sketch.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>()
                        .Where(candidate => candidate.GetType() == ownedSegmentType38).ToArray();
                    var byId = candidates.Where(candidate =>
                    {
                        int[] id = candidate.GetID() as int[];
                        return id != null && ownedId38 != null && id.SequenceEqual(ownedId38);
                    }).ToArray();
                    if (byId.Length == 1) return byId[0];
                    var byGeometry = candidates.Where(candidate =>
                        GeometryResidual31(ownedGeometry38, SegmentPoints31(candidate)) <= 1e-9).ToArray();
                    if (byGeometry.Length == 1) return byGeometry[0];
                }
                else if (ownedPointBeforeDelete != null)
                {
                    var candidates = EnumerateSketchPoints28(sketch, false).Cast<SketchPoint>().Where(candidate =>
                    {
                        int[] id = candidate.GetID() as int[];
                        return id != null && ownedId38 != null && id.SequenceEqual(ownedId38);
                    }).ToArray();
                    if (candidates.Length == 1) return candidates[0];
                    var byGeometry = EnumerateSketchPoints28(sketch, false).Cast<SketchPoint>().Where(candidate =>
                        Math.Sqrt(Math.Pow(candidate.X - ownedPointGeometry38[0], 2) +
                            Math.Pow(candidate.Y - ownedPointGeometry38[1], 2) +
                            Math.Pow(candidate.Z - ownedPointGeometry38[2], 2)) <= 1e-9).ToArray();
                    if (byGeometry.Length == 1) return byGeometry[0];
                }
                throw new InvalidOperationException("REFERENCE40: Cannot reacquire owned dimension entity: " + oldFullName);
            };
            object target = snapshot.Resolve(model, plane);
            var targetFace = target as Face2;
            double[] pick = snapshot.PickPoint31 == null ? null : snapshot.PickPoint31(plane);
            if (targetFace == null || pick == null || pick.Length < 3)
                throw new InvalidOperationException("REFERENCE38: Target face or pick point is unavailable: " + oldFullName);

            double[] textPosition = null;
            try
            {
                var display = relation.GetDisplayDimension() as DisplayDimension;
                var annotation = display == null ? null : display.GetAnnotation() as Annotation;
                textPosition = annotation == null ? null : annotation.GetPosition() as double[];
            }
            catch { }
            if (textPosition == null || textPosition.Length < 3) textPosition = pick.ToArray();
            else textPosition = ReflectReference30(textPosition, plane);

            if (!sketch.RelationManager.DeleteRelation(relation))
                throw new InvalidOperationException("REFERENCE38: DeleteRelation rejected: " + oldFullName);
            owned = reacquireOwned38();
            CreateMirrorPartPackage.LogDebug("[REFERENCE40][OWNED_REACQUIRED] dimension=" + oldFullName +
                " id=" + (ownedId38 == null ? "unavailable" : string.Join(",", ownedId38)));
            Func<object, bool, string, bool> selectSketchEntity40 = (value, append, label) =>
            {
                bool selected = false;
                string method = "none";
                SelectData selectionData40 = null;
                try
                {
                    var selectionManager40 = model.SelectionManager as SelectionMgr;
                    selectionData40 = selectionManager40 == null ? null : selectionManager40.CreateSelectData();
                }
                catch { selectionData40 = null; }
                try
                {
                    var entity = value as Entity;
                    if (entity != null) { selected = entity.Select4(append, selectionData40); method = "IEntity.Select4(SelectData)"; }
                }
                catch { selected = false; }
                if (!selected)
                {
                    try { selected = (bool)((dynamic)value).Select4(append, selectionData40); method = "dynamic.Select4(SelectData)"; }
                    catch { selected = false; }
                }
                if (!selected)
                {
                    var segment40 = value as SketchSegment;
                    var points40 = segment40 == null ? null : SegmentPoints31(segment40);
                    if (points40 != null && points40.Length >= 2)
                    {
                        var math40 = SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
                        var sketchToModel40 = sketch.ModelToSketchTransform.IInverse();
                        double[] middle40 = Enumerable.Range(0, 3).Select(index =>
                            (points40[0][index] + points40[points40.Length - 1][index]) * 0.5).ToArray();
                        double[] world40 = (double[])((MathPoint)((MathPoint)math40.CreatePoint(middle40))
                            .MultiplyTransform(sketchToModel40)).ArrayData;
                        selected = model.Extension.SelectByID2(string.Empty, "SKETCHSEGMENT",
                            world40[0], world40[1], world40[2], append, 0, null, 0);
                        method = "SelectByID2(SKETCHSEGMENT)";
                    }
                }
                CreateMirrorPartPackage.LogDebug("[REFERENCE40][SELECT] label=" + label + " selected=" + selected +
                    " method=" + method + " activeSketch=" + (model.SketchManager.ActiveSketch != null));
                return selected;
            };

            model.ClearSelection2(true);
            bool selectedOwned = false;
            bool selectedFace = false;
            SketchLine localCarrier42 = null;
            string faceSelectionMethod = "LocalConstructionCarrierRequired";
            // A face can report SelectByID2=True while AddDimension2 still rejects the
            // line/face pair in an active 2D sketch. That API behaviour is intermittent
            // and leaves the dimension transaction non-deterministic. ProxyLineToFace
            // already carries the exact reflected linear datum, so always materialise it
            // as a fixed construction line and create a point-pair linear dimension.
            // This keeps the complete operation inside sketch space and works for every
            // planar face orientation instead of depending on UI face selection state.
            if (!selectedFace)
            {
                double[][] reflectedGeometry = snapshot.Geometry31 == null ? null : snapshot.Geometry31(plane);
                if (reflectedGeometry == null || reflectedGeometry.Length != 2)
                    throw new InvalidOperationException("REFERENCE39: Reflected carrier geometry is unavailable: " + oldFullName);
                var math = SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
                var worldToSketch = sketch.ModelToSketchTransform;
                Func<double[], double[]> toSketch = world => (double[])((MathPoint)((MathPoint)math.CreatePoint(world))
                    .MultiplyTransform(worldToSketch)).ArrayData;
                double[] carrierStart = toSketch(reflectedGeometry[0]);
                double[] carrierEnd = toSketch(reflectedGeometry[1]);
                model.ClearSelection2(true);
                var sketchManager44 = model.SketchManager;
                bool previousAddToDb44 = sketchManager44.AddToDB;
                SketchLine carrier = null;
                try
                {
                    // AddToDB suppresses SOLIDWORKS inferencing while the temporary datum
                    // is created. Without it, a carrier only 0.1 mm from the profile is
                    // automatically snapped/coincident to that profile and its distance
                    // dimension becomes zero.
                    sketchManager44.AddToDB = true;
                    carrier = sketchManager44.CreateLine(carrierStart[0], carrierStart[1], carrierStart[2],
                        carrierEnd[0], carrierEnd[1], carrierEnd[2]) as SketchLine;
                    if (carrier != null) ((SketchSegment)carrier).ConstructionGeometry = true;
                }
                finally
                {
                    try { sketchManager44.AddToDB = previousAddToDb44; } catch { }
                }
                if (carrier == null) throw new InvalidOperationException("REFERENCE39: CreateLine returned null: " + oldFullName);
                int[] carrierId41 = ((SketchSegment)carrier).GetID() as int[];
                double[][] carrierGeometry41 = SegmentPoints31((SketchSegment)carrier);
                double carrierCreationResidual44 = GeometryResidual31(
                    new[] { carrierStart, carrierEnd }, carrierGeometry41);
                CreateMirrorPartPackage.LogDebug("[REFERENCE44][CARRIER_CREATED] actualStart=" +
                    string.Join(",", carrierGeometry41[0].Select(value => value.ToString("R"))) + " actualEnd=" +
                    string.Join(",", carrierGeometry41[carrierGeometry41.Length - 1].Select(value => value.ToString("R"))) +
                    " residual=" + carrierCreationResidual44.ToString("R") + " addToDB=True");
                if (carrierCreationResidual44 > 1.0e-9)
                    throw new InvalidOperationException("REFERENCE44: Construction carrier was altered during creation; residual=" +
                        carrierCreationResidual44.ToString("R"));
                Func<SketchLine> reacquireCarrier41 = () =>
                {
                    var candidates = (sketch.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>()
                        .OfType<SketchLine>().Where(candidate => ((SketchSegment)candidate).ConstructionGeometry).ToArray();
                    var byId = candidates.Where(candidate =>
                    {
                        int[] id = ((SketchSegment)candidate).GetID() as int[];
                        return id != null && carrierId41 != null && id.SequenceEqual(carrierId41);
                    }).ToArray();
                    if (byId.Length == 1) return byId[0];
                    var byGeometry = candidates.Where(candidate =>
                        GeometryResidual31(carrierGeometry41, SegmentPoints31((SketchSegment)candidate)) <= 1e-9).ToArray();
                    if (byGeometry.Length == 1) return byGeometry[0];
                    throw new InvalidOperationException("REFERENCE41: Cannot reacquire local carrier: " + oldFullName);
                };
                if (!selectSketchEntity40(carrier, false, "carrier-for-fix"))
                    throw new InvalidOperationException("REFERENCE39: Cannot select local carrier for fixing: " + oldFullName);
                model.SketchAddConstraints("sgFIXED");
                model.ClearSelection2(true);
                owned = reacquireOwned38();
                carrier = reacquireCarrier41();
                localCarrier42 = carrier;
                CreateMirrorPartPackage.LogDebug("[REFERENCE41][CARRIER_REACQUIRED] id=" +
                    (carrierId41 == null ? "unavailable" : string.Join(",", carrierId41)));
                bool selectedCarrier = selectSketchEntity40(carrier, false, "carrier-for-dimension");
                selectedOwned = selectSketchEntity40(owned, true, "owned-after-carrier");
                selectedFace = selectedOwned && selectedCarrier;
                faceSelectionMethod = "LocalConstructionCarrier";
                CreateMirrorPartPackage.LogDebug("[REFERENCE39][LOCAL_CARRIER] start=" +
                    string.Join(",", carrierStart.Select(value => value.ToString("R"))) + " end=" +
                    string.Join(",", carrierEnd.Select(value => value.ToString("R"))) +
                    " selected=" + selectedFace);
            }
            CreateMirrorPartPackage.LogDebug("[REFERENCE38][TARGET_SELECTION] owned=" + selectedOwned +
                " face=" + selectedFace + " method=" + faceSelectionMethod);
            if (!selectedOwned || !selectedFace)
                throw new InvalidOperationException("REFERENCE38: Target selection failed: owned=" + selectedOwned + " face=" + selectedFace);
            Func<Func<DisplayDimension>, DisplayDimension> createDimensionWithoutPrompt46 = create46 =>
            {
                var app46 = SwAddin.InstanceSwApp;
                bool previousInput46 = false;
                bool capturedInput46 = false;
                try
                {
                    previousInput46 = app46.GetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                    capturedInput46 = true;
                    app46.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
                    CreateMirrorPartPackage.LogDebug("[REFERENCE46][DIMENSION_PROMPT] suppressed=True previous=" + previousInput46);
                    return create46();
                }
                finally
                {
                    if (capturedInput46)
                    {
                        try
                        {
                            app46.SetUserPreferenceToggle(
                                (int)swUserPreferenceToggle_e.swInputDimValOnCreate, previousInput46);
                            CreateMirrorPartPackage.LogDebug("[REFERENCE46][DIMENSION_PROMPT] restored=" + previousInput46);
                        }
                        catch { }
                    }
                }
            };
            DisplayDimension replacementDisplay;
            if (localCarrier42 != null && owned is SketchLine)
            {
                double[][] ownedGeometry42 = SegmentPoints31((SketchSegment)owned);
                double[][] carrierGeometry42 = SegmentPoints31((SketchSegment)localCarrier42);
                double ownedDx42 = ownedGeometry42[ownedGeometry42.Length - 1][0] - ownedGeometry42[0][0];
                double ownedDy42 = ownedGeometry42[ownedGeometry42.Length - 1][1] - ownedGeometry42[0][1];
                double carrierDx42 = carrierGeometry42[carrierGeometry42.Length - 1][0] - carrierGeometry42[0][0];
                double carrierDy42 = carrierGeometry42[carrierGeometry42.Length - 1][1] - carrierGeometry42[0][1];
                double ownedLength42 = Math.Sqrt(ownedDx42 * ownedDx42 + ownedDy42 * ownedDy42);
                double carrierLength42 = Math.Sqrt(carrierDx42 * carrierDx42 + carrierDy42 * carrierDy42);
                if (ownedLength42 <= 1.0e-12 || carrierLength42 <= 1.0e-12)
                    throw new InvalidOperationException("REFERENCE42: Cannot dimension a zero-length line.");

                double normalizedCross42 = Math.Abs(ownedDx42 * carrierDy42 - ownedDy42 * carrierDx42) /
                    (ownedLength42 * carrierLength42);
                if (normalizedCross42 > 1.0e-5)
                    throw new InvalidOperationException("REFERENCE42: Owned line and reflected carrier are not parallel; cross=" +
                        normalizedCross42.ToString("R"));

                // The required linear dimension lies along the normal of the parallel lines.
                // Do not infer it from segment midpoints: unequal line lengths shift their midpoints
                // along the tangent and can incorrectly turn a linear constraint into an angle.
                double normalX42 = -ownedDy42 / ownedLength42;
                double normalY42 = ownedDx42 / ownedLength42;
                bool horizontalDistance42 = Math.Abs(normalX42) >= Math.Abs(normalY42);

                // Selecting two SketchLine objects makes SOLIDWORKS create an angular
                // dimension (0 or PI), even when AddHorizontal/VerticalDimension2 is used.
                // Select endpoints instead so the API creates the intended signed linear
                // coordinate constraint. The fixed construction carrier remains the datum.
                var ownedPoint43 = ((SketchLine)owned).GetStartPoint2() as SketchPoint;
                var carrierPoint43 = localCarrier42.GetStartPoint2() as SketchPoint;
                if (ownedPoint43 == null || carrierPoint43 == null)
                    throw new InvalidOperationException("REFERENCE43: Cannot resolve line endpoints for linear dimension.");
                model.ClearSelection2(true);
                bool selectedCarrierPoint43 = selectSketchEntity40(carrierPoint43, false, "carrier-point-for-dimension");
                bool selectedOwnedPoint43 = selectSketchEntity40(ownedPoint43, true, "owned-point-for-dimension");
                if (!selectedCarrierPoint43 || !selectedOwnedPoint43)
                    throw new InvalidOperationException("REFERENCE43: Endpoint selection failed: owned=" +
                        selectedOwnedPoint43 + " carrier=" + selectedCarrierPoint43);
                replacementDisplay = createDimensionWithoutPrompt46(() => horizontalDistance42
                    ? model.AddHorizontalDimension2(textPosition[0], textPosition[1], textPosition[2]) as DisplayDimension
                    : model.AddVerticalDimension2(textPosition[0], textPosition[1], textPosition[2]) as DisplayDimension);
                CreateMirrorPartPackage.LogDebug("[REFERENCE43][POINT_PAIR] owned=" +
                    ownedPoint43.X.ToString("R") + "," + ownedPoint43.Y.ToString("R") + " carrier=" +
                    carrierPoint43.X.ToString("R") + "," + carrierPoint43.Y.ToString("R") +
                    " selected=True");
                CreateMirrorPartPackage.LogDebug("[REFERENCE42][LINEAR_DIMENSION] orientation=" +
                    (horizontalDistance42 ? "Horizontal" : "Vertical") + " tangent=" +
                    ownedDx42.ToString("R") + "," + ownedDy42.ToString("R") + " normal=" +
                    normalX42.ToString("R") + "," + normalY42.ToString("R") + " parallelCross=" +
                    normalizedCross42.ToString("R"));
            }
            else replacementDisplay = createDimensionWithoutPrompt46(() =>
                model.AddDimension2(textPosition[0], textPosition[1], textPosition[2]) as DisplayDimension);
            var replacement = replacementDisplay == null ? null : replacementDisplay.GetDimension2(0) as Dimension;
            if (replacement == null) throw new InvalidOperationException("REFERENCE38: AddDimension2 returned null: " + oldFullName);
            replacement.DrivenState = (int)swDimensionDrivenState_e.swDimensionDriving;
            replacement.SystemValue = systemValue;
            CreateMirrorPartPackage.LogDebug("[REFERENCE38][DIMENSION_RECREATED] old=" + oldFullName +
                " new=" + replacement.FullName + " value=" + replacement.SystemValue.ToString("R") +
                " pick=" + string.Join(",", pick.Select(value => value.ToString("R"))));
            model.ClearSelection2(true);
            return replacement;
        }

        private static object[] EnumerateSketchPoints28(Sketch sketch, bool log = true)
        {
            var result = new List<SketchPoint>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            Action<object> add = value =>
            {
                var point = value as SketchPoint;
                if (point == null) return;
                if (!SameProbeObject(point.GetSketch(), sketch)) return;
                var id = point.GetID() as int[];
                if (id == null || id.Length < 2)
                    throw new InvalidOperationException("SKETCH28: Point identity unavailable.");
                string key = id[0] + ":" + id[1];
                if (ids.Add(key)) result.Add(point);
            };
            Action<SketchSegment> addSegment = segment =>
            {
                var line = segment as SketchLine;
                if (line != null) { add(line.GetStartPoint2()); add(line.GetEndPoint2()); return; }
                var arc = segment as SketchArc;
                if (arc != null) { add(arc.GetStartPoint2()); add(arc.GetEndPoint2()); add(arc.GetCenterPoint2()); return; }
                var spline = segment as SketchSpline;
                if (spline != null)
                    foreach (object value in spline.GetPoints2() as object[] ?? new object[0]) add(value);
            };
            foreach (object value in sketch.GetSketchPoints2() as object[] ?? new object[0]) add(value);
            foreach (SketchSegment segment in sketch.GetSketchSegments() as object[] ?? new object[0]) addSegment(segment);
            // GetEntities includes solver proxy geometry, NOT owned sketch geometry.
            if (log) CreateMirrorPartPackage.LogDebug("[REFERENCE30][OWNED_POINTS] count=" + result.Count +
                " relationProxiesIncluded=False");
            return result.Cast<object>().ToArray();
        }

        /// <summary>
        /// Mở Sketch và Xóa toàn bộ Ràng buộc (Relations) + Kích thước (Dimensions)
        /// Trả về đối tượng Sketch để tiếp tục thực hiện Bước 2 (Dịch chuyển Điểm)
        /// </summary>
        private static string DiagnosticEntity(object entity)
        {
            if (entity == null) return "null";
            var point = entity as SketchPoint;
            if (point != null)
            {
                int[] id = point.GetID() as int[];
                return "Point id=" + (id == null ? "unavailable" : string.Join(",", id)) +
                    " xyz_m=" + point.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    point.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    point.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
            var segment = entity as SketchSegment;
            if (segment != null)
            {
                int[] id = segment.GetID() as int[];
                string result = "Segment type=" + segment.GetType() + " id=" +
                    (id == null ? "unavailable" : string.Join(",", id));
                var line = entity as SketchLine;
                if (line != null) result += " start={" + DiagnosticEntity(line.GetStartPoint2()) +
                    "} end={" + DiagnosticEntity(line.GetEndPoint2()) + "}";
                return result;
            }
            return entity.GetType().FullName;
        }

        private static void LogConstraintSnapshot(Feature feature, Sketch sketch)
        {
            CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][BEGIN] sketch=" + feature.Name);
            try
            {
                var frame = sketch.ModelToSketchTransform.ArrayData as double[];
                CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][MODEL_TO_SKETCH] " +
                    (frame == null ? "unavailable" : string.Join(",", Array.ConvertAll(frame,
                        v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))));
                foreach (object point in sketch.GetSketchPoints2() as object[] ?? new object[0])
                    CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][POINT] " + DiagnosticEntity(point));
                foreach (object segment in sketch.GetSketchSegments() as object[] ?? new object[0])
                    CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][SEGMENT] " + DiagnosticEntity(segment));
                var dd = feature.GetFirstDisplayDimension() as DisplayDimension;
                while (dd != null)
                {
                    var dim = dd.GetDimension() as Dimension;
                    if (dim != null) CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][DIMENSION] name=" +
                        dim.FullName + " type=" + dim.GetType() + " value_SI=" +
                        dim.SystemValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    dd = feature.GetNextDisplayDimension(dd) as DisplayDimension;
                }
                var relations = sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[];
                int index = 0;
                foreach (object value in relations ?? new object[0])
                {
                    var relation = value as SketchRelation;
                    if (relation == null) continue;
                    try
                    {
                        string label = "[SKETCHDIAG14][RELATION] index=" + index++ + " type=" +
                            (swConstraintType_e)relation.GetRelationType();
                        var display = relation.GetDisplayDimension() as DisplayDimension;
                        var dimension = display == null ? null : display.GetDimension() as Dimension;
                        if (dimension != null) label += " dimension=" + dimension.FullName;
                        CreateMirrorPartPackage.LogDebug(label);
                        foreach (object entity in relation.GetEntities() as object[] ?? new object[0])
                            CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][RELATION_ENTITY] " + DiagnosticEntity(entity));
                    }
                    catch (Exception ex) { CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][RELATION_READ_FAILED] " + ex.Message); }
                }
            }
            catch (Exception ex) { CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][SNAPSHOT_INCOMPLETE] " + ex.Message); }
            CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][END] sketch=" + feature.Name);
        }

        public static Sketch FreeSketchForMutation(ModelDoc2 partDoc, Feature sketchFeat, bool preserveConstraints = true)
        {
            if (partDoc == null || sketchFeat == null) return null;

            Sketch swSketch = sketchFeat.GetSpecificFeature2() as Sketch;
            if (swSketch == null) return null;
            LogConstraintSnapshot(sketchFeat, swSketch);

            // 1. Kích hoạt chế độ Edit Sketch (Bắt buộc phải mở Sketch mới can thiệp được)
            if (!sketchFeat.Select2(false, 0))
                throw new InvalidOperationException("Cannot select sketch for mutation: " + sketchFeat.Name);
            partDoc.EditSketch();

            Sketch active = partDoc.SketchManager.ActiveSketch;
            bool sameSketch = false;
            if (active != null)
            {
                IntPtr expected = IntPtr.Zero, actual = IntPtr.Zero;
                try
                {
                    expected = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(swSketch);
                    actual = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(active);
                    sameSketch = expected == actual;
                }
                finally
                {
                    if (expected != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(expected);
                    if (actual != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(actual);
                }
            }
            CreateMirrorPartPackage.LogDebug("[POINTMOVE16][EDIT_STATE] sketch=" + sketchFeat.Name +
                " active=" + (active != null) + " identityMatches=" + sameSketch +
                " addToDB=" + partDoc.SketchManager.AddToDB);
            if (!sameSketch)
                throw new InvalidOperationException("POINTMOVE16: Expected sketch is not active; mutation cancelled.");

            // Default is non-destructive. Solver rejection must be diagnosed, never
            // worked around by deleting the user's dimensions and relations.
            if (preserveConstraints)
            {
                CreateMirrorPartPackage.LogDebug("[PARAMETRIC13][PRESERVE] sketch=" + sketchFeat.Name +
                    " dimensionsDeleted=0 relationsDeleted=0");
                return swSketch;
            }

            // 2. XÓA TOÀN BỘ KÍCH THƯỚC (DIMENSIONS)
            DisplayDimension dispDim = sketchFeat.GetFirstDisplayDimension() as DisplayDimension;
            List<string> dimNames = new List<string>();
            List<DisplayDimension> dispDims = new List<DisplayDimension>();
            
            while (dispDim != null)
            {
                Dimension dim = dispDim.GetDimension() as Dimension;
                if (dim != null)
                {
                    dimNames.Add(dim.Name + "@" + sketchFeat.Name);
                    dispDims.Add(dispDim);
                }
                dispDim = sketchFeat.GetNextDisplayDimension(dispDim) as DisplayDimension;
            }

            CreateMirrorPartPackage.LogDebug($"[MUTATION] Sketch {sketchFeat.Name} has {dimNames.Count} dimensions.");
            
            if (dimNames.Count > 0)
            {
                partDoc.ClearSelection2(true);
                foreach (string dimName in dimNames)
                {
                    bool sel = partDoc.Extension.SelectByID2(dimName, "DIMENSION", 0, 0, 0, true, 0, null, 0);
                    CreateMirrorPartPackage.LogDebug($"[MUTATION] Select {dimName} -> {sel}");
                }
                
                // Thử select qua DisplayDimension.Select
                foreach (DisplayDimension dd in dispDims)
                {
                    Annotation ann = dd.GetAnnotation() as Annotation;
                    if (ann != null)
                    {
                        bool selAnn = ann.Select3(true, null);
                        CreateMirrorPartPackage.LogDebug($"[MUTATION] Select Annotation -> {selAnn}");
                    }
                }

                bool delSuccess = partDoc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                CreateMirrorPartPackage.LogDebug($"[MUTATION] DeleteSelection2 -> {delSuccess}");
            }

            // 3. XÓA TOÀN BỘ RÀNG BUỘC HÌNH HỌC (RELATIONS) - NATIVE API
            ISketchRelationManager relMgr = swSketch.RelationManager;
            if (relMgr != null)
            {
                // Gọi API gốc của SolidWorks để tận diệt mọi Relation (kể cả external, dangling)
                relMgr.DeleteAllRelations();
                CreateMirrorPartPackage.LogDebug($"[MUTATION] Deleted all relations via native API.");
            }

            // CHÚ Ý: CHÚNG TA KHÔNG THOÁT SKETCH Ở ĐÂY!
            // Giữ nguyên trạng thái Edit Sketch để Bước 2 ngay lập tức can thiệp vào tọa độ điểm.
            
            return swSketch;
        }

        /// <summary>
        /// Di chuyển toàn bộ các điểm trong Sketch qua mặt phẳng đối xứng (Bảo toàn Internal ID)
        /// Mặc định: Lật đối xứng qua trục Y của Sketch (newX = -x, newY = y) hoặc theo tọa độ 3D bất biến
        /// </summary>
        public static void MutateSketchPoints(ModelDoc2 partDoc, Sketch swSketch, List<SketchPointSnapshot> pristinePoints = null, PlaneData mirrorPlane = null, CutAuditSnapshot21 audit = null)
        {
            MutateSketchPoints(partDoc, swSketch, 0.0, 0.0, 0.0, 0.0, pristinePoints, mirrorPlane, audit);
        }

        /// <summary>
        /// Di chuyển toàn bộ các điểm trong Sketch phản chiếu qua tọa độ 3D không gian bất biến (hoặc trục 2D ax1, ay1 -> ax2, ay2)
        /// </summary>
        public static void MutateSketchPoints(
            ModelDoc2 partDoc, 
            Sketch swSketch, 
            double ax1, double ay1, 
            double ax2, double ay2, 
            List<SketchPointSnapshot> pristinePoints = null,
            PlaneData mirrorPlane = null, CutAuditSnapshot21 audit = null)
        {
            if (partDoc == null || swSketch == null) return;

            object[] sketchPointsObj = EnumerateSketchPoints28(swSketch);
            if (sketchPointsObj.Length == 0) return;

            MathTransform m2s = null;
            try { m2s = swSketch.ModelToSketchTransform; } catch { }

            ISldWorks swApp = SwAddin.InstanceSwApp;
            if (swApp == null)
            {
                try
                {
                    swApp = System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
                }
                catch { }
            }
            IMathUtility mathUtility = swApp != null ? swApp.GetMathUtility() as IMathUtility : null;

            double dx = ax2 - ax1;
            double dy = ay2 - ay1;
            double len = Math.Sqrt(dx * dx + dy * dy);

            bool useGeneralLine = (len > 1e-9);
            double nx = 0, ny = 0;
            if (useGeneralLine)
            {
                double ux = dx / len;
                double uy = dy / len;
                nx = -uy;
                ny = ux;
            }

            bool isParallelPlane = false;
            if (mirrorPlane != null && mirrorPlane.Normal != null && mirrorPlane.Normal.Length >= 3 && m2s != null && mathUtility != null)
            {
                try
                {
                    MathTransform s2m = m2s.Inverse() as MathTransform;
                    if (s2m != null)
                    {
                        MathVector zVec = mathUtility.CreateVector(new double[] { 0, 0, 1 }) as MathVector;
                        MathVector nVec = (zVec != null) ? zVec.MultiplyTransform(s2m) as MathVector : null;
                        double[] n = (nVec != null) ? nVec.ArrayData as double[] : null;
                        if (n != null && n.Length >= 3)
                        {
                            double dot = n[0] * mirrorPlane.Normal[0] + n[1] * mirrorPlane.Normal[1] + n[2] * mirrorPlane.Normal[2];
                            isParallelPlane = (Math.Abs(Math.Abs(dot) - 1.0) < 0.05);
                        }
                    }
                }
                catch { }
            }

            CreateMirrorPartPackage.LogDebug($"[MUTATION_AXIS] axis1=({ax1:F6},{ay1:F6}) axis2=({ax2:F6},{ay2:F6}) len={len:F6} useGeneralLine={useGeneralLine} nx={nx:F6} ny={ny:F6} isParallel={isParallelPlane} pristineCount={pristinePoints?.Count ?? 0} hasMirrorPlane={mirrorPlane != null}");
            int[] sourceForLive47 = null;
            if (pristinePoints != null)
            {
                var live47 = new List<SketchPointSnapshot>();
                MathTransform sketchToModel47 = m2s == null ? null : m2s.Inverse() as MathTransform;
                foreach (object value in sketchPointsObj)
                {
                    var point47 = value as SketchPoint;
                    if (point47 == null) throw new InvalidOperationException("POINT47 owned point enumeration changed.");
                    var id47 = point47.GetID() as int[];
                    if (id47 == null || id47.Length < 2)
                        throw new InvalidOperationException("POINT47 live sketch point ID unavailable.");
                    var entry47 = new SketchPointSnapshot { Id1 = id47[0], Id2 = id47[1],
                        X = point47.X, Y = point47.Y, Index = live47.Count };
                    if (sketchToModel47 != null && mathUtility != null)
                    {
                        var local47 = mathUtility.CreatePoint(new[] { point47.X, point47.Y,
                            swSketch.Is3D() ? point47.Z : 0.0 }) as MathPoint;
                        var world47 = local47 == null ? null : local47.MultiplyTransform(sketchToModel47) as MathPoint;
                        var xyz47 = world47 == null ? null : world47.ArrayData as double[];
                        if (xyz47 != null && xyz47.Length >= 3)
                        { entry47.ModelX = xyz47[0]; entry47.ModelY = xyz47[1];
                          entry47.ModelZ = xyz47[2]; entry47.HasModelCoords = true; }
                    }
                    live47.Add(entry47);
                }
                string evidence47;
                sourceForLive47 = SketchPointMatcher47.Resolve(pristinePoints, live47, mirrorPlane, out evidence47);
                CreateMirrorPartPackage.LogDebug("[POINT47][MAP] mode=" + evidence47 +
                    " count=" + sourceForLive47.Length + " idsChanged=" + (evidence47 != "POINT_ID"));
            }
            int ptIdx = 0;
            var targets = new List<Tuple<SketchPoint, double, double>>();
            int liveIndex47 = 0;
            foreach (object ptObj in sketchPointsObj)
            {
                SketchPoint swPt = ptObj as SketchPoint;
                if (swPt == null) continue;

                SketchPointSnapshot snap = sourceForLive47 == null ? null :
                    pristinePoints[sourceForLive47[liveIndex47]];
                liveIndex47++;

                double x = (snap != null) ? snap.X : swPt.X;
                double y = (snap != null) ? snap.Y : swPt.Y;

                double newX = 0, newY = 0;
                bool mapped3D = false;

                // [3D INVARIANT POINT MAPPING]
                // Nếu có mirrorPlane và tọa độ Model 3D nguyên bản, chiếu điểm 3D qua mirrorPlane rồi chuyển về hệ tọa độ Sketch đang mở.
                // Giải pháp này độc lập 100% với việc SolidWorks có đảo trục UV của mặt phẳng hay không!
                if (mirrorPlane != null && snap != null && snap.HasModelCoords && m2s != null && mathUtility != null)
                {
                    try
                    {
                        double dot = (snap.ModelX - mirrorPlane.Origin[0]) * mirrorPlane.Normal[0]
                                   + (snap.ModelY - mirrorPlane.Origin[1]) * mirrorPlane.Normal[1]
                                   + (snap.ModelZ - mirrorPlane.Origin[2]) * mirrorPlane.Normal[2];
                        double mxMirr = snap.ModelX - 2.0 * dot * mirrorPlane.Normal[0];
                        double myMirr = snap.ModelY - 2.0 * dot * mirrorPlane.Normal[1];
                        double mzMirr = snap.ModelZ - 2.0 * dot * mirrorPlane.Normal[2];

                        MathPoint mpMirr = mathUtility.CreatePoint(new double[] { mxMirr, myMirr, mzMirr }) as MathPoint;
                        MathPoint mp2D = mpMirr != null ? mpMirr.MultiplyTransform(m2s) as MathPoint : null;
                        double[] arr2D = mp2D != null ? mp2D.ArrayData as double[] : null;
                        if (arr2D != null && arr2D.Length >= 3 && Math.Abs(arr2D[2]) <= 1e-7)
                        {
                            newX = arr2D[0];
                            newY = arr2D[1];
                            mapped3D = true;
                            CreateMirrorPartPackage.LogDebug($"[MUTATION_PT_3D_{ptIdx++}] (modelSrc: {snap.ModelX * 1000.0:F3},{snap.ModelY * 1000.0:F3},{snap.ModelZ * 1000.0:F3} -> modelTgt: {mxMirr * 1000.0:F3},{myMirr * 1000.0:F3},{mzMirr * 1000.0:F3}) -> skTgt: ({newX * 1000.0:F3}, {newY * 1000.0:F3}mm)");
                        }
                    }
                    catch { }
                }

                if (!mapped3D)
                {
                    if (mirrorPlane != null)
                        throw new InvalidOperationException("PARAMETRIC13: Cannot place reflected model point on the current sketch plane; " +
                            "explicit support-plane remapping is required. No 2D projection fallback allowed.");
                    if (useGeneralLine)
                    {
                        double dist = (x - ax1) * nx + (y - ay1) * ny;
                        newX = x - 2.0 * dist * nx;
                        newY = y - 2.0 * dist * ny;
                    }
                    else
                    {
                        if (isParallelPlane)
                        {
                            newX = x;
                            newY = y;
                        }
                        else
                        {
                            newX = -x;
                            newY = y;
                        }
                    }
                    CreateMirrorPartPackage.LogDebug($"[MUTATION_PT_{ptIdx++}] (source: {x * 1000.0:F3}, {y * 1000.0:F3}mm, live: {swPt.X * 1000.0:F3}, {swPt.Y * 1000.0:F3}mm) -> target: ({newX * 1000.0:F3}, {newY * 1000.0:F3}mm)");
                }

                targets.Add(Tuple.Create(swPt, newX, newY));
            }

            Action verifyDimensions = ApplyConstraintAwareTargets(partDoc, swSketch, targets, audit, mirrorPlane);
            partDoc.InsertSketch2(true);
            verifyDimensions();
            // Check after leaving the sketch: the solver may undo a coordinate
            // assignment even when the COM setter itself reports no exception.
            int rejectedPoints = 0;
            foreach (var target in targets)
            {
                double ex = target.Item1.X - target.Item2;
                double ey = target.Item1.Y - target.Item3;
                double error = Math.Sqrt(ex * ex + ey * ey);
                CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][POINT_RESULT] " + DiagnosticEntity(target.Item1) +
                    " targetXY_m=" + target.Item2.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    target.Item3.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                    " error_m=" + error.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                if (error > 1e-7) rejectedPoints++;
            }
            if (rejectedPoints > 0)
                throw new InvalidOperationException("PARAMETRIC13: Sketch solver rejected reflected points=" + rejectedPoints +
                    "; dimensions/relations retained. See SKETCHDIAG14 relation/entity and point results.");
            CreateMirrorPartPackage.LogDebug("[REFLECT19][SKETCH_COMMIT_PASS] points=" + targets.Count +
                " dimensionsRestored=True targetGeometryVerified=True");
        }

        private static bool IsReflectionInvariantConstraint47(swConstraintType_e type)
        {
            switch (type)
            {
                case swConstraintType_e.swConstraintType_HORIZONTAL:
                case swConstraintType_e.swConstraintType_VERTICAL:
                case swConstraintType_e.swConstraintType_HORIZPOINTS:
                case swConstraintType_e.swConstraintType_VERTPOINTS:
                case swConstraintType_e.swConstraintType_TANGENT:
                case swConstraintType_e.swConstraintType_PARALLEL:
                case swConstraintType_e.swConstraintType_PERPENDICULAR:
                case swConstraintType_e.swConstraintType_COINCIDENT:
                case swConstraintType_e.swConstraintType_CONCENTRIC:
                case swConstraintType_e.swConstraintType_SYMMETRIC:
                case swConstraintType_e.swConstraintType_ATMIDDLE:
                case swConstraintType_e.swConstraintType_ATINTERSECT:
                case swConstraintType_e.swConstraintType_SAMELENGTH:
                case swConstraintType_e.swConstraintType_COLINEAR:
                case swConstraintType_e.swConstraintType_CORADIAL:
                case swConstraintType_e.swConstraintType_USEEDGE:
                case swConstraintType_e.swConstraintType_ATPIERCE:
                case swConstraintType_e.swConstraintType_MERGEPOINTS:
                case swConstraintType_e.swConstraintType_NORMAL:
                case swConstraintType_e.swConstraintType_NORMALPOINTS:
                case swConstraintType_e.swConstraintType_INTERSECTION:
                case swConstraintType_e.swConstraintType_ISOBYPOINT:
                case swConstraintType_e.swConstraintType_SAMEISOPARAM:
                case swConstraintType_e.swConstraintType_FITSPLINE:
                case swConstraintType_e.swConstraintType_EQUALCURVATURE:
                case swConstraintType_e.swConstraintType_EQUALTANGENT:
                case swConstraintType_e.swConstraintType_TANGENTFACE:
                case swConstraintType_e.swConstraintType_EQUALCURV3DALIGN:
                case swConstraintType_e.swConstraintType_C3TOUCH:
                case swConstraintType_e.swConstraintType_SAMECURVELENGTH:
                case swConstraintType_e.swConstraintType_SAMESLOTS:
                    return true;
                default:
                    return false;
            }
        }

        private static Action ApplyConstraintAwareTargets(ModelDoc2 model, Sketch sketch,
            List<Tuple<SketchPoint, double, double>> targets, CutAuditSnapshot21 audit, PlaneData plane)
        {
            // Geometry-first replay deliberately keeps the reflected profile and does not
            // attempt to preserve the source sketch's parametric ownership.  A fully
            // constrained source sketch is often tied to source-side faces/edges; restoring
            // those relations after reflection pulls the profile back to its old position.
            // The output remains an editable native sketch, but dimensions are reference
            // dimensions and geometric relations remain suppressed.
            bool geometryFirst53 = true;
            // Dimensions are handled uniformly by release/restore. Non-dimensional
            // incidence and metric relations below are invariant under any reflection
            // isometry once their external references have been rebound.
            var equations = model.GetEquationMgr() as EquationMgr;
            if (!geometryFirst53 && equations != null && equations.GetCount() > 0)
                throw new InvalidOperationException("CONSTRAINT15: Equation-driven models need dependency-aware handling.");
            var dimensions = new List<Tuple<Dimension, int, double>>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll)
                as object[] ?? new object[0])
            {
                var relation = item as SketchRelation;
                if (relation == null) throw new InvalidOperationException("CONSTRAINT15: Cannot inspect sketch relation.");
                var type = (swConstraintType_e)relation.GetRelationType();
                var display = relation.GetDisplayDimension() as DisplayDimension;
                var dimension = display == null ? null : display.GetDimension() as Dimension;
                if (!geometryFirst53 && dimension == null && !IsReflectionInvariantConstraint47(type))
                    throw new InvalidOperationException("CONSTRAINT47: Directional or locking relation needs a dedicated transformer: " + type);
                if (dimension == null || !names.Add(dimension.FullName)) continue;
                int state = dimension.DrivenState;
                if (state == (int)swDimensionDrivenState_e.swDimensionDrivenUnknown ||
                    dimension.ReadOnly || dimension.IsDesignTableDimension())
                    throw new InvalidOperationException("CONSTRAINT15: Dimension is externally controlled or unavailable: " + dimension.FullName);
                dimensions.Add(Tuple.Create(dimension, state, dimension.SystemValue));
            }

            var changed = new List<Tuple<Dimension, int, double>>();
            var recreatedDimensions38 = new Dictionary<Dimension, Dimension>();
            var suspendedRelations52 = new List<SketchRelation>();
            bool automaticSolveBefore51 = true;
            bool automaticSolveSuspended51 = false;
            var targetIds = new List<int[]>();
            foreach (var target in targets)
            {
                int[] id = target.Item1.GetID() as int[];
                if (id == null || id.Length < 2)
                    throw new InvalidOperationException("REFLECT19: Target point identity unavailable.");
                targetIds.Add(new[] { id[0], id[1] });
            }
            try
            {
                foreach (var entry in dimensions)
                {
                    if (entry.Item2 != (int)swDimensionDrivenState_e.swDimensionDriving) continue;
                    changed.Add(entry); // Restore even when the setter partially succeeds.
                    entry.Item1.DrivenState = (int)swDimensionDrivenState_e.swDimensionDriven;
                    if (entry.Item1.DrivenState != (int)swDimensionDrivenState_e.swDimensionDriven)
                        throw new InvalidOperationException("CONSTRAINT15: Cannot temporarily release " + entry.Item1.FullName);
                    CreateMirrorPartPackage.LogDebug("[CONSTRAINT15][RELEASE] " + entry.Item1.FullName);
                }
                if (changed.Count > 0)
                {
                    if (geometryFirst53)
                    {
                        // Rebuilding here asks SOLIDWORKS to regenerate the dependent cut
                        // while its sketch still has the source-side geometry. That can fail
                        // and close sketch edit mode before any reflected point is assigned.
                        // In geometry-first mode the released dimensions stay driven, so the
                        // point IDs can be rebound without rebuilding until sketch commit.
                        Sketch activeSketch57 = model.SketchManager.ActiveSketch;
                        if (activeSketch57 == null || !SameProbeObject(activeSketch57, sketch))
                            throw new InvalidOperationException("REFLECT57: Sketch edit mode was lost after releasing dimensions.");
                        CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST57][REFRESH_DEFERRED] releasedDimensions=" +
                            changed.Count + " activeSketch=True rebuildAfterSketchCommit=True");
                    }
                    else
                    {
                        sketch = RefreshReleasedSketch(model, sketch, changed);
                    }
                }
                else
                {
                    // Rebuilding before reference rebinding is invalid after an upstream
                    // feature has already been reflected: downstream sketches still point
                    // at the source-side topology and their dependent feature can fail
                    // before mutation even starts. No solver refresh is needed when no
                    // driving dimension was released.
                    CreateMirrorPartPackage.LogDebug("[REFLECT48][REFRESH_SKIPPED] reason=NoReleasedDimensions referencesWillRebindFirst=True");
                }
                if (!geometryFirst53)
                    RebindReferences30(model, sketch, audit, plane);
                else
                    CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST53][REFERENCE_REBIND_SKIPPED] reason=IndependentProfile");
                for (int i = 0; i < targets.Count; i++)
                    targets[i] = Tuple.Create(ResolveProbePoint(sketch, targetIds[i]), targets[i].Item2, targets[i].Item3);
                // A point constrained Coincident/Collinear to an external entity cannot be
                // moved one-at-a-time while automatic solve is active.  After all external
                // references have been rebound, move the complete point set atomically and
                // let the solver validate the reflected, internally consistent state once.
                automaticSolveBefore51 = sketch.GetAutomaticSolve();
                bool needsMove52 = targets.Any(target =>
                    Math.Abs(target.Item1.X - target.Item2) > 1e-7 ||
                    Math.Abs(target.Item1.Y - target.Item3) > 1e-7);
                if (needsMove52)
                {
                    // SetCoords can still reject an endpoint when a non-dimensional
                    // Coincident/Collinear/etc. relation owns it. Suppress relations while
                    // automatic solve is still active so SOLIDWORKS actually releases their
                    // degrees of freedom. Disabling solve first only changes the Suppressed
                    // property; the active constraint graph may continue locking the point.
                    // Keep the relation objects and definitions, then restore them after all
                    // points and dimensions have been updated.
                    foreach (SketchRelation relation52 in
                        (sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll)
                            as object[] ?? new object[0]).OfType<SketchRelation>())
                    {
                        DisplayDimension display52 = relation52.GetDisplayDimension() as DisplayDimension;
                        if (display52 != null || relation52.Suppressed) continue;
                        swConstraintType_e type52 = (swConstraintType_e)relation52.GetRelationType();
                        if (!geometryFirst53 && !IsReflectionInvariantConstraint47(type52))
                            throw new InvalidOperationException("CONSTRAINT52: Relation cannot be suspended generically: " + type52);
                        relation52.Suppressed = true;
                        if (!relation52.Suppressed)
                            throw new InvalidOperationException("CONSTRAINT52: Cannot temporarily suppress relation: " + type52);
                        suspendedRelations52.Add(relation52);
                        CreateMirrorPartPackage.LogDebug("[CONSTRAINT52][SUPPRESS] type=" + type52);
                    }
                    CreateMirrorPartPackage.LogDebug("[CONSTRAINT52][SUPPRESS_SUMMARY] count=" + suspendedRelations52.Count +
                        " solverActiveDuringSuppression=" + automaticSolveBefore51);
                }
                if (automaticSolveBefore51 && needsMove52)
                {
                    sketch.SetAutomaticSolve(false);
                    if (sketch.GetAutomaticSolve())
                        throw new InvalidOperationException("REFLECT51: Cannot suspend automatic sketch solve for atomic reflection.");
                    automaticSolveSuspended51 = true;
                    CreateMirrorPartPackage.LogDebug("[REFLECT51][SOLVER_SUSPEND] automaticSolve=False points=" + targets.Count +
                        " constraintsAlreadySuppressed=" + suspendedRelations52.Count);
                }
                CreateMirrorPartPackage.LogDebug("[REFLECT19][APPLY_BEGIN] points=" + targets.Count);
                foreach (var target in targets)
                {
                    CreateMirrorPartPackage.LogDebug("[POINTMOVE16][BEFORE] " + DiagnosticEntity(target.Item1) +
                        " targetXY_m=" + target.Item2.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                        target.Item3.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    if (Math.Abs(target.Item1.X - target.Item2) <= 1e-7 &&
                        Math.Abs(target.Item1.Y - target.Item3) <= 1e-7)
                    {
                        CreateMirrorPartPackage.LogDebug("[POINTMOVE16][SKIP] alreadyAtTarget=True");
                        continue;
                    }
                    bool moved = target.Item1.SetCoords(target.Item2, target.Item3, target.Item1.Z);
                    CreateMirrorPartPackage.LogDebug("[POINTMOVE16][AFTER] apiReturned=" + moved + " " + DiagnosticEntity(target.Item1));
                    if (!moved)
                    {
                        throw new SketchPointMoveRejected58("REFLECT19: SetCoords rejected target after solver synchronization; " +
                            "segment-level geometry replay required. point=" + DiagnosticEntity(target.Item1));
                    }
                }
                foreach (var target in targets)
                    if (Math.Abs(target.Item1.X - target.Item2) > 1e-7 || Math.Abs(target.Item1.Y - target.Item3) > 1e-7)
                        throw new InvalidOperationException("REFLECT52: Coordinate assignment failed while constraints were suspended.");
                CreateMirrorPartPackage.LogDebug("[REFLECT52][TARGETS_ASSIGNED] verified=True constraintsPendingRestore=" +
                    suspendedRelations52.Count);
            }
            finally
            {
                var failures = new List<string>();
                if (!geometryFirst53)
                {
                    foreach (var entry in changed)
                    {
                        string originalFullName;
                        try { originalFullName = entry.Item1.FullName; }
                        catch { originalFullName = "unavailable-dimension"; }
                        try
                        {
                            bool recreateProxyDimension = HasReflectedProxyDimension37(audit, originalFullName);
                            Dimension activeDimension = entry.Item1;
                            if (recreateProxyDimension)
                            {
                                activeDimension = RecreateReflectedProxyDimension38(model, sketch, audit, plane,
                                    originalFullName, entry.Item3);
                                recreatedDimensions38[entry.Item1] = activeDimension;
                            }
                            else
                            {
                                activeDimension.DrivenState = entry.Item2;
                                if (activeDimension.DrivenState != entry.Item2) throw new InvalidOperationException("state mismatch");
                            }
                            if (entry.Item2 == (int)swDimensionDrivenState_e.swDimensionDriving)
                            {
                                double commandedValue = entry.Item3;
                                activeDimension.SystemValue = commandedValue;
                                double tolerance = Math.Max(1e-9, Math.Abs(entry.Item3) * 1e-7);
                                double restoredValue = activeDimension.SystemValue;
                                CreateMirrorPartPackage.LogDebug("[CONSTRAINT37][VALUE_RESTORE] name=" + activeDimension.FullName +
                                    " expected=" + entry.Item3.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                                    " commanded=" + commandedValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                                    " actual=" + restoredValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                                    " recreatedProxyDimension=" + recreateProxyDimension +
                                    " tolerance=" + tolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                                double valueResidual = Math.Abs(restoredValue - entry.Item3);
                                if (valueResidual > tolerance)
                                    throw new InvalidOperationException("value mismatch");
                            }
                            CreateMirrorPartPackage.LogDebug("[CONSTRAINT15][RESTORE] " + activeDimension.FullName);
                        }
                        catch (Exception ex) { failures.Add(originalFullName + ": " + ex.Message); }
                    }
                }
                else
                {
                    CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST53][DIMENSIONS_LEFT_DRIVEN] count=" + changed.Count);
                }
                if (!geometryFirst53)
                {
                    foreach (SketchRelation relation52 in suspendedRelations52)
                    {
                        try
                        {
                            relation52.Suppressed = false;
                            if (relation52.Suppressed) throw new InvalidOperationException("suppression state mismatch");
                            CreateMirrorPartPackage.LogDebug("[CONSTRAINT52][RESTORE] type=" +
                                (swConstraintType_e)relation52.GetRelationType());
                        }
                        catch (Exception ex) { failures.Add("RelationRestore: " + ex.Message); }
                    }
                }
                else
                {
                    CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST53][RELATIONS_LEFT_SUPPRESSED] count=" +
                        suspendedRelations52.Count);
                }
                if (automaticSolveSuspended51)
                {
                    try
                    {
                        sketch.SetAutomaticSolve(automaticSolveBefore51);
                        automaticSolveSuspended51 = false;
                        if (sketch.GetAutomaticSolve() != automaticSolveBefore51)
                            throw new InvalidOperationException("automatic solve state mismatch");
                        CreateMirrorPartPackage.LogDebug("[REFLECT51][SOLVER_RESTORE] automaticSolve=" +
                            automaticSolveBefore51 + " editRebuild=DEFERRED_UNTIL_SKETCH_COMMIT");
                    }
                    catch (Exception ex) { failures.Add("AutomaticSolve: " + ex.Message); }
                }
                if (failures.Count > 0)
                    throw new InvalidOperationException("CONSTRAINT15: Cannot restore driving dimensions; output forbidden: " + string.Join("; ", failures));
            }
            for (int i = 0; i < targets.Count; i++)
                targets[i] = Tuple.Create(ResolveProbePoint(sketch, targetIds[i]), targets[i].Item2, targets[i].Item3);
            foreach (var target in targets)
                if (Math.Abs(target.Item1.X - target.Item2) > 1e-7 || Math.Abs(target.Item1.Y - target.Item3) > 1e-7)
                    throw new InvalidOperationException("REFLECT52: Reflected sketch geometry moved after solver resume.");
            CreateMirrorPartPackage.LogDebug("[REFLECT19][TARGETS_REACHED] verified=True constraintsRestored=" +
                (geometryFirst53 ? 0 : suspendedRelations52.Count) + " dimensionsRestored=" +
                (geometryFirst53 ? 0 : changed.Count) + " geometryFirst=" + geometryFirst53);
            // Verify while still editing the profile; exiting may split its support edges.
            if (!geometryFirst53 && recreatedDimensions38.Count == 0)
                RebindReferences30(model, sketch, audit, plane, true);
            else if (!geometryFirst53)
                CreateMirrorPartPackage.LogDebug("[REFERENCE38][VERIFY_BY_RECREATION] count=" + recreatedDimensions38.Count);
            else
                CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST53][REFERENCE_VERIFY_SKIPPED] reason=IndependentProfile");
            Action verify = () =>
            {
                if (geometryFirst53)
                {
                    CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST53][VERIFY] dimensionsIgnored=" + dimensions.Count +
                        " relationsSuppressed=" + suspendedRelations52.Count);
                    return;
                }
                foreach (var entry in dimensions)
                {
                    Dimension activeDimension;
                    if (!recreatedDimensions38.TryGetValue(entry.Item1, out activeDimension)) activeDimension = entry.Item1;
                    int actualState = activeDimension.DrivenState;
                    double actualValue = activeDimension.SystemValue;
                    double tolerance = Math.Max(1e-9, Math.Abs(entry.Item3) * 1e-7);
                    double valueResidual = Math.Abs(actualValue - entry.Item3);
                    CreateMirrorPartPackage.LogDebug("[SKETCH28][DIMENSION_READBACK] name=" + activeDimension.FullName +
                        " expectedState=" + entry.Item2 + " actualState=" + actualState +
                        " expectedValue=" + entry.Item3.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                        " actualValue=" + actualValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                        " recreatedProxyDimension=" + recreatedDimensions38.ContainsKey(entry.Item1) +
                        " tolerance=" + tolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    if (actualState != entry.Item2 || valueResidual > tolerance)
                        throw new InvalidOperationException("CONSTRAINT15: Dimension state/value changed: " + activeDimension.FullName);
                }
                CreateMirrorPartPackage.LogDebug("[CONSTRAINT15][DIMENSIONS_VERIFIED] count=" + dimensions.Count +
                    " deleted=0 relationsDeleted=0");
            };
            verify();
            return verify;
        }

        private static SketchPoint ResolveProbePoint(Sketch sketch, int[] expectedId)
        {
            SketchPoint match = null;
            foreach (object value in EnumerateSketchPoints28(sketch))
            {
                var point = value as SketchPoint;
                int[] id = point == null ? null : point.GetID() as int[];
                if (id == null || id.Length < 2 || id[0] != expectedId[0] || id[1] != expectedId[1]) continue;
                if (match != null) throw new InvalidOperationException("POINTPROBE17: Point identity ambiguous.");
                match = point;
            }
            if (match == null) throw new InvalidOperationException("POINTPROBE17: Point identity missing.");
            return match;
        }

        private static bool SameProbeObject(object left, object right)
        {
            if (left == null || right == null) return false;
            IntPtr a = IntPtr.Zero, b = IntPtr.Zero;
            try
            {
                a = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(left);
                b = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(right);
                return a == b;
            }
            finally
            {
                if (a != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(a);
                if (b != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(b);
            }
        }

        private static Sketch RefreshReleasedSketch(ModelDoc2 model, Sketch sketch,
            List<Tuple<Dimension, int, double>> releasedDimensions)
        {
            // Synchronize solver state after releasing dimensions, then reacquire entities.
            Feature owner = null;
            foreach (var node in ADDIN.Commands.MirrorV7.FeatureTreeScannerV7.Scan(model).Nodes)
            {
                var candidate = node.Feature == null ? null : node.Feature.GetSpecificFeature2() as Sketch;
                if (SameProbeObject(candidate, sketch)) { owner = node.Feature; break; }
            }
            if (owner == null) throw new InvalidOperationException("REFLECT19: Cannot identify the sketch owner before rebuild.");
            var baseline = new List<Tuple<int[], double, double, double>>();
            foreach (object value in EnumerateSketchPoints28(sketch))
            {
                var point = value as SketchPoint;
                if (point == null) continue;
                int[] id = point.GetID() as int[];
                if (id == null || id.Length < 2) throw new InvalidOperationException("REFLECT19: Missing baseline point ID.");
                baseline.Add(Tuple.Create(id, point.X, point.Y, point.Z));
            }
            bool rebuild = model.EditRebuild3();
            CreateMirrorPartPackage.LogDebug("[REFLECT19][REFRESH] rebuild=" + rebuild +
                " active=" + (model.SketchManager.ActiveSketch != null) + " automaticSolve=" + sketch.GetAutomaticSolve());
            if (!rebuild)
            {
                bool ownerWarning;
                int ownerError = owner.GetErrorCode2(out ownerWarning);
                CreateMirrorPartPackage.LogDebug("[SKETCH26][REBUILD_FAILED] sketch=" + owner.Name +
                    " sketchError=" + ownerError + " warning=" + ownerWarning);
                foreach (Feature child in owner.GetChildren() as object[] ?? new object[0])
                {
                    bool childWarning;
                    int childError = child.GetErrorCode2(out childWarning);
                    CreateMirrorPartPackage.LogDebug("[SKETCH26][DEPENDENT_HEALTH] feature=" + child.Name +
                        " error=" + childError + " warning=" + childWarning);
                }
                throw new InvalidOperationException("REFLECT19: Rebuild failed; reflection cancelled. See SKETCH26 feature diagnostics.");
            }
            if (model.SketchManager.ActiveSketch == null)
            {
                model.ClearSelection2(true);
                if (!owner.Select2(false, 0))
                    throw new InvalidOperationException("REFLECT19: Cannot select original sketch for re-entry.");
                model.EditSketch();
            }
            Sketch reopened = model.SketchManager.ActiveSketch;
            bool identityMatches = SameProbeObject(reopened, owner.GetSpecificFeature2() as Sketch);
            CreateMirrorPartPackage.LogDebug("[REFLECT19][REENTER] sketch=" + owner.Name +
                " active=" + (reopened != null) + " identityMatches=" + identityMatches);
            if (!identityMatches) throw new InvalidOperationException("REFLECT19: Wrong or missing active sketch after rebuild.");
            sketch = reopened;
            foreach (var entry in releasedDimensions)
            {
                int state = entry.Item1.DrivenState;
                CreateMirrorPartPackage.LogDebug("[REFLECT19][DIMENSION_STATE] name=" + entry.Item1.FullName + " state=" + state);
                if (state != (int)swDimensionDrivenState_e.swDimensionDriven)
                    throw new InvalidOperationException("REFLECT19: Dimension did not remain driven after rebuild; reflection cancelled.");
            }
            foreach (var entry in baseline)
            {
                var point = ResolveProbePoint(sketch, entry.Item1);
                if (Math.Abs(point.X - entry.Item2) > 1e-7 || Math.Abs(point.Y - entry.Item3) > 1e-7 ||
                    Math.Abs(point.Z - entry.Item4) > 1e-7)
                    throw new InvalidOperationException("REFLECT19: Rebuild changed baseline coordinates; reflection cancelled.");
            }
            return sketch;
        }

        /// <summary>
        /// Phục hồi chính xác tọa độ các điểm Sketch về snapshot đã lưu (nếu bị xô lệch do Rebuild feature thất bại)
        /// </summary>
        public static bool RestoreSketchPoints(
            ModelDoc2 partDoc,
            Feature sketchFeat,
            List<SketchPointSnapshot> snapshots, bool preserveConstraints = true)
        {
            if (partDoc == null || sketchFeat == null || snapshots == null || snapshots.Count == 0) return false;
            Sketch swSketch = sketchFeat.GetSpecificFeature2() as Sketch;
            if (swSketch == null) return false;

            object[] sketchPointsObj = EnumerateSketchPoints28(swSketch);
            if (sketchPointsObj.Length == 0) return false;

            bool anyShifted = false;
            for (int i = 0; i < sketchPointsObj.Length; i++)
            {
                SketchPoint swPt = sketchPointsObj[i] as SketchPoint;
                if (swPt == null) continue;
                SketchPointSnapshot snap = null;
                try
                {
                    int[] idArr = swPt.GetID() as int[];
                    if (idArr != null && idArr.Length >= 2)
                    {
                        snap = snapshots.Find(p => p.Id1 == idArr[0] && p.Id2 == idArr[1]);
                    }
                }
                catch { }
                if (snap == null && i < snapshots.Count) snap = snapshots[i];
                if (snap != null)
                {
                    if (Math.Abs(swPt.X - snap.X) > 1e-6 || Math.Abs(swPt.Y - snap.Y) > 1e-6)
                    {
                        anyShifted = true;
                        break;
                    }
                }
            }

            if (!anyShifted) return true;

            CreateMirrorPartPackage.LogDebug($"[RESTORE_SKETCH] Sketch points in {sketchFeat.Name} shifted during candidate evaluation. Restoring pristine coordinates...");
            sketchFeat.Select2(false, 0);
            partDoc.EditSketch();

            Sketch activeSk = sketchFeat.GetSpecificFeature2() as Sketch;
            if (!preserveConstraints && activeSk != null && activeSk.RelationManager != null)
            {
                try { activeSk.RelationManager.DeleteAllRelations(); } catch { }
            }

            object[] activePts = activeSk != null ? activeSk.GetSketchPoints2() as object[] : sketchPointsObj;
            if (activePts != null)
            {
                for (int i = 0; i < activePts.Length; i++)
                {
                    SketchPoint swPt = activePts[i] as SketchPoint;
                    if (swPt == null) continue;
                    SketchPointSnapshot snap = null;
                    try
                    {
                        int[] idArr = swPt.GetID() as int[];
                        if (idArr != null && idArr.Length >= 2)
                        {
                            snap = snapshots.Find(p => p.Id1 == idArr[0] && p.Id2 == idArr[1]);
                        }
                    }
                    catch { }
                    if (snap == null && i < snapshots.Count) snap = snapshots[i];
                    if (snap != null)
                    {
                        swPt.X = snap.X;
                        swPt.Y = snap.Y;
                    }
                }
            }

            partDoc.InsertSketch2(true);
            partDoc.ForceRebuild3(false);
            return true;
        }

        /// <summary>
        /// Tái tạo các rãnh Slot đối xứng hoàn chỉnh với đầy đủ ràng buộc hình học và kích thước nguyên bản
        /// </summary>
        private sealed class MirroredPrimitive58
        {
            public SketchPrimitiveSnapshot58 Source;
            public double[] Start, End, Center, Middle;
        }

        private static double[] PrimitiveReflectedLocal58(IMathUtility math, MathTransform modelToSketch,
            PlaneData plane, double[] originalModel, double planeOffset, bool alreadyReflected = false)
        {
            if (originalModel == null) return null;
            double[] reflected = alreadyReflected ? originalModel : ReflectReference30(originalModel, plane);
            MathPoint point = math.CreatePoint(reflected) as MathPoint;
            MathPoint local = point == null ? null : point.MultiplyTransform(modelToSketch) as MathPoint;
            double[] values = local == null ? null : local.ArrayData as double[];
            if (values == null || values.Length < 3 || Math.Abs(values[2] - planeOffset) > 1e-7)
                throw new InvalidOperationException("PRIMITIVE58: Reflected segment does not lie on the active sketch plane.");
            return new[] { values[0], values[1], 0.0 };
        }

        private static double PositiveAngle58(double angle)
        {
            const double full = 2.0 * Math.PI;
            angle %= full;
            return angle < 0 ? angle + full : angle;
        }

        private static short ArcDirection58(double[] center, double[] start, double[] middle, double[] end)
        {
            double a = Math.Atan2(start[1] - center[1], start[0] - center[0]);
            double m = Math.Atan2(middle[1] - center[1], middle[0] - center[0]);
            double b = Math.Atan2(end[1] - center[1], end[0] - center[0]);
            double midCcw = PositiveAngle58(m - a);
            double endCcw = PositiveAngle58(b - a);
            if (Math.Abs(midCcw - endCcw) < 1e-10 || midCcw < 1e-10)
                throw new InvalidOperationException("PRIMITIVE58: Arc midpoint is degenerate.");
            return (short)(midCcw < endCcw ? 1 : -1);
        }

        private static double PrimitiveDistance58(double[] left, double[] right)
        {
            return Math.Sqrt(Math.Pow(left[0] - right[0], 2) + Math.Pow(left[1] - right[1], 2));
        }

        // Used only when a point setter rejects a valid reflected target. Recreate the
        // complete line/arc sketch from the original model-space snapshot, never from
        // the partly-mutated staging sketch. This remains a native driving sketch/cut.
        public static void RecreateMirroredPrimitives58(ModelDoc2 model, Sketch sketch,
            List<SketchPrimitiveSnapshot58> pristine, PlaneData plane, bool replaceGeneratedProfile = false,
            double planeOffset = 0, bool alreadyReflected = false, bool deferOwnerRebuild65 = false)
        {
            if (model == null || sketch == null || pristine == null || pristine.Count == 0 || plane == null)
                throw new InvalidOperationException("PRIMITIVE58: Original primitive snapshot is unavailable.");
            if (!SameProbeObject(model.SketchManager.ActiveSketch, sketch))
                throw new InvalidOperationException("PRIMITIVE58: Expected sketch is not being edited.");
            ISldWorks app = SwAddin.InstanceSwApp;
            IMathUtility math = app == null ? null : app.GetMathUtility() as IMathUtility;
            MathTransform modelToSketch = sketch.ModelToSketchTransform as MathTransform;
            if (math == null || modelToSketch == null)
                throw new InvalidOperationException("PRIMITIVE58: Sketch transform unavailable.");

            var mapped = new List<MirroredPrimitive58>();
            foreach (SketchPrimitiveSnapshot58 source in pristine)
            {
                if (source.Type != (int)swSketchSegments_e.swSketchLINE &&
                    source.Type != (int)swSketchSegments_e.swSketchARC)
                    throw new InvalidOperationException("PRIMITIVE58: Segment type " + source.Type +
                        " needs a dedicated geometry handler; original sketch retained.");
                var target = new MirroredPrimitive58
                {
                    Source = source,
                    Start = PrimitiveReflectedLocal58(math, modelToSketch, plane, source.StartModel, planeOffset, alreadyReflected),
                    End = PrimitiveReflectedLocal58(math, modelToSketch, plane, source.EndModel, planeOffset, alreadyReflected),
                    Center = PrimitiveReflectedLocal58(math, modelToSketch, plane, source.CenterModel, planeOffset, alreadyReflected),
                    Middle = PrimitiveReflectedLocal58(math, modelToSketch, plane, source.MiddleModel, planeOffset, alreadyReflected)
                };
                if (target.Start == null || (!source.Circle && target.End == null) ||
                    (source.Type == (int)swSketchSegments_e.swSketchARC && target.Center == null) ||
                    (source.Type == (int)swSketchSegments_e.swSketchARC && !source.Circle && target.Middle == null))
                    throw new InvalidOperationException("PRIMITIVE58: Incomplete primitive geometry; original sketch retained.");
                mapped.Add(target);
            }
            object[] existing = sketch.GetSketchSegments() as object[] ?? new object[0];
            if (!replaceGeneratedProfile && existing.Length != pristine.Count)
                throw new InvalidOperationException("PRIMITIVE58: Segment count changed before fallback; original sketch retained. " +
                    "pristine=" + pristine.Count + " live=" + existing.Length);

            bool oldAddToDb = model.SketchManager.AddToDB;
            // Attached, regenerated profiles are edited with their owner rolled back.
            // Do not toggle the native solver there: restoring it can trigger native
            // regeneration while the other profiles are still being replaced. Keep
            // the user's solver setting unchanged in this transaction mode.
            bool oldAutoSolve = !deferOwnerRebuild65 && sketch.GetAutomaticSolve();
            bool oldAutoRelations = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations);
            bool oldInference = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference);
            try
            {
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, false);
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, false);
                if (!deferOwnerRebuild65) sketch.SetAutomaticSolve(false);
                model.SketchManager.AddToDB = true;
                model.ClearSelection2(true);
                foreach (object item in existing)
                {
                    SketchSegment segment = item as SketchSegment;
                    if (segment == null || !segment.Select4(true, null))
                        throw new InvalidOperationException("PRIMITIVE58: Cannot select every original segment for replacement.");
                }
                int deletionOptions65 = deferOwnerRebuild65 ? 0 : (int)swDeleteSelectionOptions_e.swDelete_Absorbed;
                if (existing.Length > 0 && !model.Extension.DeleteSelection2(deletionOptions65))
                    throw new InvalidOperationException("PRIMITIVE58: Original segment deletion was rejected.");
                if ((sketch.GetSketchSegments() as object[] ?? new object[0]).Length != 0)
                    throw new InvalidOperationException("PRIMITIVE58: Original sketch segments remain after deletion.");

                const double tolerance = 1e-7;
                for (int i = 0; i < mapped.Count; i++)
                {
                    MirroredPrimitive58 entry = mapped[i];
                    SketchSegment created;
                    if (entry.Source.Type == (int)swSketchSegments_e.swSketchLINE)
                    {
                        created = model.SketchManager.CreateLine(entry.Start[0], entry.Start[1], 0,
                            entry.End[0], entry.End[1], 0) as SketchSegment;
                    }
                    else if (entry.Source.Circle)
                    {
                        created = model.SketchManager.CreateCircle(entry.Center[0], entry.Center[1], 0,
                            entry.Start[0], entry.Start[1], 0) as SketchSegment;
                    }
                    else
                    {
                        short direction = ArcDirection58(entry.Center, entry.Start, entry.Middle, entry.End);
                        created = model.SketchManager.CreateArc(entry.Center[0], entry.Center[1], 0,
                            entry.Start[0], entry.Start[1], 0, entry.End[0], entry.End[1], 0,
                            direction) as SketchSegment;
                    }
                    if (created == null) throw new InvalidOperationException("PRIMITIVE58: Segment creation failed at " + i);
                    created.ConstructionGeometry = entry.Source.Construction;
                    if (created.ConstructionGeometry != entry.Source.Construction ||
                        Math.Abs(created.GetLength() - entry.Source.Length) > tolerance)
                        throw new InvalidOperationException("PRIMITIVE58: Segment length/construction mismatch at " + i);
                    if (entry.Source.Type == (int)swSketchSegments_e.swSketchLINE)
                    {
                        ISketchLine line = created as ISketchLine;
                        double[] a = { ((SketchPoint)line.GetStartPoint2()).X, ((SketchPoint)line.GetStartPoint2()).Y };
                        double[] b = { ((SketchPoint)line.GetEndPoint2()).X, ((SketchPoint)line.GetEndPoint2()).Y };
                        double residual = Math.Min(Math.Max(PrimitiveDistance58(a, entry.Start), PrimitiveDistance58(b, entry.End)),
                            Math.Max(PrimitiveDistance58(a, entry.End), PrimitiveDistance58(b, entry.Start)));
                        if (residual > tolerance) throw new InvalidOperationException("PRIMITIVE58: Line geometry mismatch at " + i);
                    }
                    else
                    {
                        ISketchArc arc = created as ISketchArc;
                        SketchPoint center = arc.GetCenterPoint2() as SketchPoint;
                        if (center == null || PrimitiveDistance58(new[] { center.X, center.Y }, entry.Center) > tolerance)
                            throw new InvalidOperationException("PRIMITIVE58: Arc center mismatch at " + i);
                        if (!entry.Source.Circle)
                        {
                            SketchPoint start = arc.GetStartPoint2() as SketchPoint;
                            SketchPoint end = arc.GetEndPoint2() as SketchPoint;
                            double residual = Math.Min(Math.Max(PrimitiveDistance58(new[] { start.X, start.Y }, entry.Start),
                                PrimitiveDistance58(new[] { end.X, end.Y }, entry.End)),
                                Math.Max(PrimitiveDistance58(new[] { start.X, start.Y }, entry.End),
                                PrimitiveDistance58(new[] { end.X, end.Y }, entry.Start)));
                            if (residual > tolerance) throw new InvalidOperationException("PRIMITIVE58: Arc endpoints mismatch at " + i);
                        }
                    }
                    CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST58][PRIMITIVE_PASS] index=" + i +
                        " type=" + entry.Source.Type + " construction=" + entry.Source.Construction);
                }
                CreateMirrorPartPackage.LogDebug("[SKETCH65][COUNT_BEGIN] deferOwnerRebuild=" + deferOwnerRebuild65);
                var active65 = model.SketchManager.ActiveSketch;
                if (active65 == null || !SameProbeObject(active65, sketch))
                    throw new InvalidOperationException("PRIMITIVE65: Active sketch changed during primitive replacement.");
                int count65 = (active65.GetSketchSegments() as object[] ?? new object[0]).Length;
                CreateMirrorPartPackage.LogDebug("[SKETCH65][COUNT_END] count=" + count65);
                if (count65 != pristine.Count)
                    throw new InvalidOperationException("PRIMITIVE58: Recreated segment count mismatch.");
            }
            finally
            {
                CreateMirrorPartPackage.LogDebug("[SKETCH65][RESTORE_SETTINGS_BEGIN] solverToggled=" + !deferOwnerRebuild65);
                try { model.SketchManager.AddToDB = oldAddToDb; } catch { }
                if (!deferOwnerRebuild65)
                    try { sketch.SetAutomaticSolve(oldAutoSolve); } catch { }
                try { app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, oldAutoRelations); } catch { }
                try { app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, oldInference); } catch { }
                CreateMirrorPartPackage.LogDebug("[SKETCH65][RESTORE_SETTINGS_END]");
            }
            CreateMirrorPartPackage.LogDebug("[SKETCH65][EXIT_BEGIN] updateEditRebuild=" + !deferOwnerRebuild65);
            model.SketchManager.InsertSketch(!deferOwnerRebuild65);
            if (model.SketchManager.ActiveSketch != null)
                throw new InvalidOperationException("PRIMITIVE65: Sketch exit did not close the active edit.");
            CreateMirrorPartPackage.LogDebug("[SKETCH65][EXIT_END]");
            CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST58][PRIMITIVE_RECREATION_PASS] segments=" + pristine.Count +
                " nativeSketch=True rebuildPending=True");
        }

        public static void RecreateMirroredSlots(
            ModelDoc2 partDoc, 
            Sketch swSketch, 
            double ax1, double ay1, 
            double ax2, double ay2, 
            List<SketchSlotSnapshot> pristineSlots,
            PlaneData mirrorPlane = null)
        {
            if (partDoc == null || swSketch == null || pristineSlots == null || pristineSlots.Count == 0) return;

            // 1. Xóa các đoạn vẽ cũ và điểm cũ trong Sketch đang mở
            object[] segs = swSketch.GetSketchSegments() as object[];
            if (segs != null && segs.Length > 0)
            {
                partDoc.ClearSelection2(true);
                foreach (object sObj in segs)
                {
                    SketchSegment s = sObj as SketchSegment;
                    if (s != null)
                    {
                        s.Select4(true, null);
                    }
                }
                partDoc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }

            object[] remainingPts = swSketch.GetSketchPoints2() as object[];
            if (remainingPts != null && remainingPts.Length > 0)
            {
                partDoc.ClearSelection2(true);
                foreach (object pObj in remainingPts)
                {
                    SketchPoint p = pObj as SketchPoint;
                    if (p != null)
                    {
                        p.Select4(true, null);
                    }
                }
                partDoc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }

            // 2. Tắt tạm thời Automatic Relations và Inference để SolidWorks không bắt dính/lệch tâm vào các cạnh lân cận
            ISldWorks swApp = SwAddin.InstanceSwApp;
            if (swApp == null)
            {
                try
                {
                    swApp = System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
                }
                catch { }
            }
            bool oldAutoRel = true;
            bool oldInference = true;
            if (swApp != null)
            {
                try
                {
                    oldAutoRel = swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations);
                    oldInference = swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference);
                    swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, false);
                    swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, false);
                }
                catch { }
            }

            try
            {
                MathTransform m2s = null;
                try { m2s = swSketch.ModelToSketchTransform; } catch { }
                IMathUtility mathUtility = swApp != null ? swApp.GetMathUtility() as IMathUtility : null;

                // 3. Chuẩn bị trục đối xứng 2D
                double dx = ax2 - ax1;
                double dy = ay2 - ay1;
                double len = Math.Sqrt(dx * dx + dy * dy);

                bool useGeneralLine = (len > 1e-9);
                double nx = 0, ny = 0;
                if (useGeneralLine)
                {
                    double ux = dx / len;
                    double uy = dy / len;
                    nx = -uy;
                    ny = ux;
                }

                // 4. Tái tạo từng Slot bằng API chuẩn CreateSketchSlot
                for (int i = 0; i < pristineSlots.Count; i++)
                {
                    SketchSlotSnapshot slotSnap = pristineSlots[i];

                    double newX1 = 0, newY1 = 0;
                    double newX2 = 0, newY2 = 0;
                    double newX3 = 0, newY3 = 0;
                    bool mapped3D = false;

                    if (mirrorPlane != null && slotSnap.HasModelCoords && m2s != null && mathUtility != null)
                    {
                        try
                        {
                            // Map P1
                            double dot1 = (slotSnap.ModelX1 - mirrorPlane.Origin[0]) * mirrorPlane.Normal[0]
                                        + (slotSnap.ModelY1 - mirrorPlane.Origin[1]) * mirrorPlane.Normal[1]
                                        + (slotSnap.ModelZ1 - mirrorPlane.Origin[2]) * mirrorPlane.Normal[2];
                            double mx1 = slotSnap.ModelX1 - 2.0 * dot1 * mirrorPlane.Normal[0];
                            double my1 = slotSnap.ModelY1 - 2.0 * dot1 * mirrorPlane.Normal[1];
                            double mz1 = slotSnap.ModelZ1 - 2.0 * dot1 * mirrorPlane.Normal[2];
                            MathPoint mp1 = mathUtility.CreatePoint(new double[] { mx1, my1, mz1 }) as MathPoint;
                            MathPoint p2D1 = mp1 != null ? mp1.MultiplyTransform(m2s) as MathPoint : null;
                            double[] a1 = p2D1 != null ? p2D1.ArrayData as double[] : null;

                            // Map P2
                            double dot2 = (slotSnap.ModelX2 - mirrorPlane.Origin[0]) * mirrorPlane.Normal[0]
                                        + (slotSnap.ModelY2 - mirrorPlane.Origin[1]) * mirrorPlane.Normal[1]
                                        + (slotSnap.ModelZ2 - mirrorPlane.Origin[2]) * mirrorPlane.Normal[2];
                            double mx2 = slotSnap.ModelX2 - 2.0 * dot2 * mirrorPlane.Normal[0];
                            double my2 = slotSnap.ModelY2 - 2.0 * dot2 * mirrorPlane.Normal[1];
                            double mz2 = slotSnap.ModelZ2 - 2.0 * dot2 * mirrorPlane.Normal[2];
                            MathPoint mp2 = mathUtility.CreatePoint(new double[] { mx2, my2, mz2 }) as MathPoint;
                            MathPoint p2D2 = mp2 != null ? mp2.MultiplyTransform(m2s) as MathPoint : null;
                            double[] a2 = p2D2 != null ? p2D2.ArrayData as double[] : null;

                            if (a1 != null && a1.Length >= 2 && a2 != null && a2.Length >= 2)
                            {
                                newX1 = a1[0]; newY1 = a1[1];
                                newX2 = a2[0]; newY2 = a2[1];

                                if (slotSnap.CreationType == (int)swSketchSlotCreationType_e.swSketchSlotCreationType_3pointarc)
                                {
                                    double dot3 = (slotSnap.ModelX3 - mirrorPlane.Origin[0]) * mirrorPlane.Normal[0]
                                                + (slotSnap.ModelY3 - mirrorPlane.Origin[1]) * mirrorPlane.Normal[1]
                                                + (slotSnap.ModelZ3 - mirrorPlane.Origin[2]) * mirrorPlane.Normal[2];
                                    double mx3 = slotSnap.ModelX3 - 2.0 * dot3 * mirrorPlane.Normal[0];
                                    double my3 = slotSnap.ModelY3 - 2.0 * dot3 * mirrorPlane.Normal[1];
                                    double mz3 = slotSnap.ModelZ3 - 2.0 * dot3 * mirrorPlane.Normal[2];
                                    MathPoint mp3 = mathUtility.CreatePoint(new double[] { mx3, my3, mz3 }) as MathPoint;
                                    MathPoint p2D3 = mp3 != null ? mp3.MultiplyTransform(m2s) as MathPoint : null;
                                    double[] a3 = p2D3 != null ? p2D3.ArrayData as double[] : null;
                                    if (a3 != null && a3.Length >= 2) { newX3 = a3[0]; newY3 = a3[1]; }
                                }
                                mapped3D = true;
                            }
                        }
                        catch { }
                    }

                    if (!mapped3D)
                    {
                        if (useGeneralLine)
                        {
                            double dist1 = (slotSnap.X1 - ax1) * nx + (slotSnap.Y1 - ay1) * ny;
                            newX1 = slotSnap.X1 - 2.0 * dist1 * nx;
                            newY1 = slotSnap.Y1 - 2.0 * dist1 * ny;

                            double dist2 = (slotSnap.X2 - ax1) * nx + (slotSnap.Y2 - ay1) * ny;
                            newX2 = slotSnap.X2 - 2.0 * dist2 * nx;
                            newY2 = slotSnap.Y2 - 2.0 * dist2 * ny;

                            if (slotSnap.CreationType == (int)swSketchSlotCreationType_e.swSketchSlotCreationType_3pointarc)
                            {
                                double dist3 = (slotSnap.X3 - ax1) * nx + (slotSnap.Y3 - ay1) * ny;
                                newX3 = slotSnap.X3 - 2.0 * dist3 * nx;
                                newY3 = slotSnap.Y3 - 2.0 * dist3 * ny;
                            }
                        }
                        else
                        {
                            newX1 = -slotSnap.X1;
                            newY1 = slotSnap.Y1;

                            newX2 = -slotSnap.X2;
                            newY2 = slotSnap.Y2;

                            newX3 = -slotSnap.X3;
                            newY3 = slotSnap.Y3;
                        }
                    }

                    // Lưu ý: slotSnap.X1/Y1 và X2/Y2 từ GetSlotPoints() luôn là 2 tâm cung tròn (arc centers).
                    // Do đó với straight slot, bắt buộc phải dùng CenterCenter để SolidWorks không tự offset thêm Width/2.
                    bool straightSlot56 =
                        slotSnap.CreationType == (int)swSketchSlotCreationType_e.swSketchSlotCreationType_line ||
                        slotSnap.CreationType == (int)swSketchSlotCreationType_e.swSketchSlotCreationType_center_line;
                    int slotLenType = straightSlot56
                        ? (int)swSketchSlotLengthType_e.swSketchSlotLengthType_CenterCenter
                        : slotSnap.LengthType;

                    SketchSlot newSlot = partDoc.SketchManager.CreateSketchSlot(
                        slotSnap.CreationType,
                        slotLenType,
                        slotSnap.Width,
                        newX1, newY1, 0.0,
                        newX2, newY2, 0.0,
                        newX3, newY3, 0.0,
                        slotSnap.CenterArcDirection,
                        false);

                    if (newSlot == null)
                        throw new InvalidOperationException("GEOMETRY_FIRST56: CreateSketchSlot returned null at index " + i);

                    object[] createdPoints56 = newSlot.GetSlotPoints() as object[];
                    if (createdPoints56 == null || createdPoints56.Length < 2)
                        throw new InvalidOperationException("GEOMETRY_FIRST56: Recreated slot points unavailable at index " + i);
                    SketchPoint createdP1_56 = createdPoints56[0] as SketchPoint;
                    SketchPoint createdP2_56 = createdPoints56[1] as SketchPoint;
                    if (createdP1_56 == null || createdP2_56 == null)
                        throw new InvalidOperationException("GEOMETRY_FIRST56: Recreated slot center points invalid at index " + i);

                    double direct56 = Math.Max(
                        Math.Sqrt(Math.Pow(createdP1_56.X - newX1, 2) + Math.Pow(createdP1_56.Y - newY1, 2)),
                        Math.Sqrt(Math.Pow(createdP2_56.X - newX2, 2) + Math.Pow(createdP2_56.Y - newY2, 2)));
                    double reversed56 = Math.Max(
                        Math.Sqrt(Math.Pow(createdP1_56.X - newX2, 2) + Math.Pow(createdP1_56.Y - newY2, 2)),
                        Math.Sqrt(Math.Pow(createdP2_56.X - newX1, 2) + Math.Pow(createdP2_56.Y - newY1, 2)));
                    double pointResidual56 = Math.Min(direct56, reversed56);
                    double widthResidual56 = Math.Abs(newSlot.Width - slotSnap.Width);
                    double slotTolerance56 = Math.Max(1e-7, Math.Abs(slotSnap.Width) * 1e-6);
                    if (pointResidual56 > slotTolerance56 || widthResidual56 > slotTolerance56)
                        throw new InvalidOperationException("GEOMETRY_FIRST56: Recreated slot geometry mismatch at index " + i +
                            " pointResidual_m=" + pointResidual56.ToString("R") +
                            " widthResidual_m=" + widthResidual56.ToString("R") +
                            " tolerance_m=" + slotTolerance56.ToString("R"));

                    // Constraints and dimensions are intentionally not recreated. The exact
                    // slot geometry is certified above and the downstream cut/body oracle is
                    // still mandatory before publication.
                    CreateMirrorPartPackage.LogDebug($"[GEOMETRY_FIRST56][SLOT_GEOMETRY_PASS] index={i} type={slotSnap.CreationType} lengthType={slotLenType} W={slotSnap.Width * 1000.0:F3}mm P1=({newX1 * 1000.0:F3},{newY1 * 1000.0:F3}) P2=({newX2 * 1000.0:F3},{newY2 * 1000.0:F3}) pointResidual_m={pointResidual56:R} widthResidual_m={widthResidual56:R} mapped3D={mapped3D}");
                }
            }
            finally
            {
                if (swApp != null)
                {
                    try
                    {
                        swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, oldAutoRel);
                        swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, oldInference);
                    }
                    catch { }
                }
            }

            partDoc.InsertSketch2(true);
        }

        /// <summary>
        /// Lật hướng mũi tên lệnh Extrude Cut
        /// </summary>
        public static bool ReverseExtrudeDirection(ModelDoc2 partDoc, Feature cutFeature)
        {
            if (partDoc == null || cutFeature == null) return false;

            IExtrudeFeatureData2 def = cutFeature.GetDefinition() as IExtrudeFeatureData2;
            if (def != null)
            {
                bool access = def.AccessSelections(partDoc, null);
                CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] Feature {cutFeature.Name} AccessSelections={access}");
                if (access)
                {
                    // Chỉ đảo duy nhất hướng đùn, giữ nguyên mọi thông số khác
                    def.ReverseDirection = !def.ReverseDirection;
                    
                    // Thử check EndCondition
                    int endCond = def.GetEndCondition(true);
                    CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] EndCondition={endCond} ReverseDirection={def.ReverseDirection}");
                    
                    bool success = cutFeature.ModifyDefinition(def, partDoc, null);
                    if (!success && endCond == (int)swEndConditions_e.swEndCondUpToNext)
                    {
                        CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] UpToNext failed with reversed direction. Trying ThroughAll fallback...");
                        try
                        {
                            def.SetEndCondition(true, (int)swEndConditions_e.swEndCondThroughAll);
                            success = cutFeature.ModifyDefinition(def, partDoc, null);
                            CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] ThroughAll fallback result={success}");
                        }
                        catch (Exception ex)
                        {
                            CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] ThroughAll fallback exception: {ex.Message}");
                        }
                    }

                    if (!success)
                    {
                        CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] ModifyDefinition failed with reversed direction. Trying BothDirections fallback...");
                        try
                        {
                            def.BothDirections = true;
                            def.SetEndCondition(false, (int)swEndConditions_e.swEndCondThroughAll);
                            success = cutFeature.ModifyDefinition(def, partDoc, null);
                            CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] BothDirections fallback result={success}");
                        }
                        catch (Exception ex)
                        {
                            CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] BothDirections fallback exception: {ex.Message}");
                        }
                    }

                    def.ReleaseSelectionAccess();
                    
                    CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] ModifyDefinition={success}");
                    return success;
                }
            }
            return false;
        }

        /// <summary>
        /// Kiểm tra danh sách sketch segment có phải là biên dạng mở (open profile) hay không.
        /// Một biên dạng mở có ít nhất 1 đỉnh bậc 1 (chỉ nối với 1 đoạn thẳng, ví dụ đường cắt đơn hoặc polyline hở).
        /// </summary>
        public static bool IsOpenProfileSegments(List<SketchSegment> profileSegments)
        {
            if (profileSegments == null || profileSegments.Count == 0) return false;

            // Trường hợp phổ biến nhất: 1 đoạn sketch đơn (thường là SketchLine cắt mở)
            if (profileSegments.Count == 1 && profileSegments[0] is SketchLine)
            {
                return true;
            }

            try
            {
                Dictionary<SketchPoint, int> pointUsage = new Dictionary<SketchPoint, int>();
                foreach (var seg in profileSegments)
                {
                    SketchPoint sp = null, ep = null;
                    if (seg is SketchLine line)
                    {
                        sp = line.GetStartPoint2() as SketchPoint;
                        ep = line.GetEndPoint2() as SketchPoint;
                    }
                    else if (seg is SketchArc arc)
                    {
                        if (arc.IsCircle() == 1)
                        {
                            // Đường tròn hoàn chỉnh là biên dạng kín tuyệt đối, không có điểm mút mở
                            continue;
                        }
                        sp = arc.GetStartPoint2() as SketchPoint;
                        ep = arc.GetEndPoint2() as SketchPoint;
                    }
                    else if (seg is SketchSpline spl)
                    {
                        object[] splPts = spl.GetPoints2() as object[];
                        if (splPts != null && splPts.Length >= 2)
                        {
                            sp = splPts[0] as SketchPoint;
                            ep = splPts[splPts.Length - 1] as SketchPoint;
                        }
                    }

                    if (sp != null)
                    {
                        pointUsage[sp] = pointUsage.ContainsKey(sp) ? pointUsage[sp] + 1 : 1;
                    }
                    if (ep != null)
                    {
                        pointUsage[ep] = pointUsage.ContainsKey(ep) ? pointUsage[ep] + 1 : 1;
                    }
                }

                foreach (var kvp in pointUsage)
                {
                    if (kvp.Value == 1) return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Kiểm tra sketch có phải là biên dạng mở (open profile) hay không.
        /// </summary>
        public static bool IsOpenProfileSketch(Sketch sk)
        {
            if (sk == null) return false;
            try
            {
                object[] contours = sk.GetSketchContours() as object[];
                if (contours != null && contours.Length > 0)
                {
                    bool hasClosed = false;
                    bool hasOpen = false;
                    foreach (object item in contours)
                    {
                        SketchContour contour = item as SketchContour;
                        if (contour != null)
                        {
                            if (contour.IsClosed()) hasClosed = true;
                            else hasOpen = true;
                        }
                    }
                    if (hasClosed && !hasOpen) return false;
                    if (hasOpen && !hasClosed) return true;
                }

                object[] segs = sk.GetSketchSegments() as object[];
                if (segs == null || segs.Length == 0) return false;

                List<SketchSegment> activeSegs = new List<SketchSegment>();
                foreach (object s in segs)
                {
                    SketchSegment seg = s as SketchSegment;
                    if (seg != null && !seg.ConstructionGeometry)
                    {
                        activeSegs.Add(seg);
                    }
                }

                return IsOpenProfileSegments(activeSegs);
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Đảo chiều vùng cắt (Flip Side to Cut) của Extrude Cut
        /// </summary>
        public static bool ToggleFlipSideToCut(ModelDoc2 partDoc, Feature cutFeature)
        {
            if (partDoc == null || cutFeature == null) return false;

            IExtrudeFeatureData2 ext = cutFeature.GetDefinition() as IExtrudeFeatureData2;
            if (ext != null)
            {
                bool access = ext.AccessSelections(partDoc, null);
                CreateMirrorPartPackage.LogDebug($"[FLIP_SIDE_TO_CUT] Feature {cutFeature.Name} AccessSelections={access}");
                if (access)
                {
                    ext.FlipSideToCut = !ext.FlipSideToCut;
                    CreateMirrorPartPackage.LogDebug($"[FLIP_SIDE_TO_CUT] Feature {cutFeature.Name} new FlipSideToCut={ext.FlipSideToCut}");
                    bool success = cutFeature.ModifyDefinition(ext, partDoc, null);
                    ext.ReleaseSelectionAccess();
                    CreateMirrorPartPackage.LogDebug($"[FLIP_SIDE_TO_CUT] ModifyDefinition={success}");
                    return success;
                }
            }
            return false;
        }
    }
}
