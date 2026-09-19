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
    }
    public sealed class CutAuditSnapshot21
    {
        public SketchSupportSnapshot20 BeforeSupport;
        public string CaptureError;
        public Body2 BeforeBody23;
        public double SourceResidual23 = double.NaN;
        public string CircleInvariant26;
        public List<SketchPointSnapshot> CirclePoints26;
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

    public static class SketchOperationsHelper
    {
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
                int referenceType23 = 0;
                var sourceFace23 = ((Sketch)profile.GetSpecificFeature2()).GetReferenceEntity(ref referenceType23) as Face2;
                snapshot.BeforeSupport = CaptureFaceSupport24(sourceFace23);
                var sourceSketch26 = profile.GetSpecificFeature2() as Sketch;
                snapshot.CircleInvariant26 = CircleInvariant26(sourceSketch26);
                snapshot.CirclePoints26 = CapturePristineSketchPoints(sourceSketch26);
                if (snapshot.BeforeSupport == null || sourceFace23 == null)
                    throw new InvalidOperationException("CUT23_SOURCE: no face support");
                snapshot.SourceResidual23 = BoundaryResidual23(sourceFace23, snapshot.BeforeSupport.Boundaries);
                string bodyError23;
                snapshot.BeforeBody23 = BodyOperationsHelper.GetSolidBodyCopyStrict(model, out bodyError23);
                if (snapshot.BeforeBody23 == null) throw new InvalidOperationException("CUT23_SOURCE_BODY: " + bodyError23);
                Audit21(model, "[CUT23] SOURCE_SELF_CHECK feature=" + feature.Name + " residual_m=" + snapshot.SourceResidual23.ToString("R") +
                    " result=" + (snapshot.SourceResidual23 <= 1e-7 ? "PASS" : "SOURCE_DATA_INVALID"));
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
            try
            {
                if (source == null || source.CaptureError != null || source.BeforeBody23 == null ||
                    double.IsNaN(source.SourceResidual23) || source.SourceResidual23 > 1e-7)
                    throw new InvalidOperationException(source?.CaptureError ?? "Source self-check failed");
                stage = "REFLECTION_ORACLE";
                var oracle = BodyOperationsHelper.MirrorBodyStrict(app, source.BeforeBody23, plane);
                if (oracle == null || !oracle.Success || oracle.Body == null)
                    throw new InvalidOperationException(oracle?.ErrorMessage ?? "Oracle unavailable");
                double norm2 = plane.Normal.Sum(v => v * v);
                if (norm2 < 1e-20) throw new InvalidOperationException("Invalid reflection normal");
                var reflected = source.BeforeSupport.Boundaries.Select(e => e.Select(p =>
                {
                    double d = Enumerable.Range(0, 3).Sum(i => (p[i] - plane.Origin[i]) * plane.Normal[i]) / norm2;
                    return Enumerable.Range(0, 3).Select(i => p[i] - 2 * d * plane.Normal[i]).ToArray();
                }).ToArray()).ToArray();
                double oracleBest;
                int oracleCount = SupportCandidates23(oracle.Body, source.BeforeSupport, reflected, out oracleBest);
                Audit21(model, "[CUT23] ORACLE feature=" + feature.Name + " candidates=" + oracleCount + " bestResidual_m=" + oracleBest.ToString("R"));
                if (oracleCount != 1) throw new InvalidOperationException("Source support does not uniquely match reflected source body");
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
                stage = "LIVE_SUPPORT";
                double liveBest;
                int liveCount = SupportCandidates23(live, source.BeforeSupport, reflected, out liveBest);
                Audit21(model, "[CUT23] LIVE feature=" + feature.Name + " candidates=" + liveCount + " bestResidual_m=" + liveBest.ToString("R"));
                if (liveCount != 1) throw new InvalidOperationException("Live support missing or ambiguous");
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
            return support == null ? "faceSupport=False" : "area_m2=" + support.Area.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                " boundaryCount=" + support.Boundaries.Length;
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
                    if (pair.Item2 == null) continue;
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

        public static SketchSupportSnapshot20 CaptureSupport20(Sketch sketch)
        {
            if (sketch == null) return null;
            int type = 0;
            var face = sketch.GetReferenceEntity(ref type) as Face2;
            return CaptureFaceSupport24(face);
        }

        private static SketchSupportSnapshot20 CaptureFaceSupport24(Face2 face)
        {
            if (face == null) return null;
            var snapshot = new SketchSupportSnapshot20 { Area = face.GetArea(), Boundaries =
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

        public static void EnsureReflectedSupport20(ISldWorks app, ModelDoc2 model, Feature feature,
            List<SketchPointSnapshot> points, SketchSupportSnapshot20 support, PlaneData plane)
        {
            if (points == null || points.Count == 0)
                throw new InvalidOperationException("CUTSPACE20: No original sketch points.");
            var math = (IMathUtility)app.GetMathUtility();
            Func<double[], double[]> reflect = p =>
            {
                double d = Enumerable.Range(0, 3).Sum(i => (p[i] - plane.Origin[i]) * plane.Normal[i]);
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
            if (support == null || support.Boundaries == null)
                throw new InvalidOperationException("CUTSPACE20: Reflected reference plane requires a dedicated plane mapper: " + feature.Name);
            var boundaries = support.Boundaries.Select(edges => edges.Select(reflect).ToArray()).ToArray();
            var mappedFace = (Face2)ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindFace(model, support.Area, boundaries);
            model.ClearSelection2(true);
            if (!feature.Select2(false, 0) || !((Entity)mappedFace).Select4(true, null) || !model.ChangeSketchPlane())
                throw new InvalidOperationException("CUTSPACE20: Cannot apply reflected support: " + feature.Name);
            double after = residual();
            CreateMirrorPartPackage.LogDebug("[CUTSPACE20][SUPPORT_REBOUND] sketch=" + feature.Name + " maxResidual_m=" + after.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (after > 1e-7) throw new InvalidOperationException("CUTSPACE20: Mapped support does not contain reflected points.");
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

            object[] sketchPointsObj = swSketch.GetSketchPoints2() as object[];
            if (sketchPointsObj == null) return list;

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
                        MathPoint mp2D = mathUtility.CreatePoint(new double[] { pt.X, pt.Y, 0.0 }) as MathPoint;
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
        public static void MutateSketchPoints(ModelDoc2 partDoc, Sketch swSketch, List<SketchPointSnapshot> pristinePoints = null, PlaneData mirrorPlane = null)
        {
            MutateSketchPoints(partDoc, swSketch, 0.0, 0.0, 0.0, 0.0, pristinePoints, mirrorPlane);
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
            PlaneData mirrorPlane = null)
        {
            if (partDoc == null || swSketch == null) return;

            object[] sketchPointsObj = swSketch.GetSketchPoints2() as object[];
            if (sketchPointsObj == null) return;

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
            int ptIdx = 0;
            var targets = new List<Tuple<SketchPoint, double, double>>();
            foreach (object ptObj in sketchPointsObj)
            {
                SketchPoint swPt = ptObj as SketchPoint;
                if (swPt == null) continue;

                // Ưu tiên 1: Tìm tọa độ gốc nguyên bản theo ID
                SketchPointSnapshot snap = null;
                try
                {
                    int[] idArr = swPt.GetID() as int[];
                    if (idArr != null && idArr.Length >= 2 && pristinePoints != null)
                    {
                        snap = pristinePoints.Find(p => p.Id1 == idArr[0] && p.Id2 == idArr[1]);
                    }
                }
                catch { }

                // Reordered topology must not silently borrow another point's target.
                if (snap == null && pristinePoints != null)
                    throw new InvalidOperationException("PARAMETRIC13: Original sketch point identity is missing; index fallback disabled.");

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

            Action verifyDimensions = ApplyConstraintAwareTargets(partDoc, swSketch, targets);
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

        private static Action ApplyConstraintAwareTargets(ModelDoc2 model, Sketch sketch,
            List<Tuple<SketchPoint, double, double>> targets)
        {
            // Initial supported group: distance + horizontal/vertical/coincident.
            // Unknown dependencies are refused before temporarily releasing dimensions.
            var equations = model.GetEquationMgr() as EquationMgr;
            if (equations != null && equations.GetCount() > 0)
                throw new InvalidOperationException("CONSTRAINT15: Equation-driven models need dependency-aware handling.");
            var dimensions = new List<Tuple<Dimension, int, double>>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll)
                as object[] ?? new object[0])
            {
                var relation = item as SketchRelation;
                if (relation == null) throw new InvalidOperationException("CONSTRAINT15: Cannot inspect sketch relation.");
                var type = (swConstraintType_e)relation.GetRelationType();
                if (type != swConstraintType_e.swConstraintType_DISTANCE &&
                    type != swConstraintType_e.swConstraintType_HORIZONTAL &&
                    type != swConstraintType_e.swConstraintType_VERTICAL &&
                    type != swConstraintType_e.swConstraintType_DIAMETER &&
                    type != swConstraintType_e.swConstraintType_RADIUS &&
                    type != swConstraintType_e.swConstraintType_SAMELENGTH &&
                    type != swConstraintType_e.swConstraintType_COINCIDENT)
                    throw new InvalidOperationException("CONSTRAINT15: Relation needs a dedicated transformer: " + type);
                var display = relation.GetDisplayDimension() as DisplayDimension;
                var dimension = display == null ? null : display.GetDimension() as Dimension;
                if (dimension == null || !names.Add(dimension.FullName)) continue;
                int state = dimension.DrivenState;
                if (state == (int)swDimensionDrivenState_e.swDimensionDrivenUnknown ||
                    dimension.ReadOnly || dimension.IsDesignTableDimension())
                    throw new InvalidOperationException("CONSTRAINT15: Dimension is externally controlled or unavailable: " + dimension.FullName);
                dimensions.Add(Tuple.Create(dimension, state, dimension.SystemValue));
            }

            var changed = new List<Tuple<Dimension, int, double>>();
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
                sketch = RefreshReleasedSketch(model, sketch, changed);
                for (int i = 0; i < targets.Count; i++)
                    targets[i] = Tuple.Create(ResolveProbePoint(sketch, targetIds[i]), targets[i].Item2, targets[i].Item3);
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
                        throw new InvalidOperationException("REFLECT19: SetCoords rejected target after solver synchronization; output forbidden.");
                    }
                }
                foreach (var target in targets)
                    if (Math.Abs(target.Item1.X - target.Item2) > 1e-7 || Math.Abs(target.Item1.Y - target.Item3) > 1e-7)
                        throw new InvalidOperationException("POINTMOVE16: Coordinate readback does not match target before dimension restoration; cause not yet established.");
                CreateMirrorPartPackage.LogDebug("[REFLECT19][TARGETS_REACHED] verified=True");
            }
            finally
            {
                var failures = new List<string>();
                foreach (var entry in changed)
                {
                    try
                    {
                        entry.Item1.DrivenState = entry.Item2;
                        if (entry.Item1.DrivenState != entry.Item2) throw new InvalidOperationException("state mismatch");
                        CreateMirrorPartPackage.LogDebug("[CONSTRAINT15][RESTORE] " + entry.Item1.FullName);
                    }
                    catch (Exception ex) { failures.Add(entry.Item1.FullName + ": " + ex.Message); }
                }
                if (failures.Count > 0)
                    throw new InvalidOperationException("CONSTRAINT15: Cannot restore driving dimensions; output forbidden: " + string.Join("; ", failures));
            }
            Action verify = () =>
            {
                foreach (var entry in dimensions)
                {
                    if (entry.Item1.DrivenState != entry.Item2 ||
                        Math.Abs(entry.Item1.SystemValue - entry.Item3) > Math.Max(1e-9, Math.Abs(entry.Item3) * 1e-7))
                        throw new InvalidOperationException("CONSTRAINT15: Dimension state/value changed: " + entry.Item1.FullName);
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
            foreach (object value in sketch.GetSketchPoints2() as object[] ?? new object[0])
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
            foreach (object value in sketch.GetSketchPoints2() as object[] ?? new object[0])
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

            object[] sketchPointsObj = swSketch.GetSketchPoints2() as object[];
            if (sketchPointsObj == null) return false;

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
                    int slotLenType = (slotSnap.CreationType == (int)swSketchSlotLengthType_e.swSketchSlotLengthType_CenterCenter)
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

                    // Khóa cứng (Fix) 2 tâm cung tròn để Slot có đầy đủ ràng buộc vị trí, không bị dịch chuyển/dưới định nghĩa
                    if (newSlot != null)
                    {
                        try
                        {
                            object[] pts = newSlot.GetSlotPoints() as object[];
                            if (pts != null)
                            {
                                for (int pIdx = 0; pIdx < Math.Min(2, pts.Length); pIdx++)
                                {
                                    SketchPoint sp = pts[pIdx] as SketchPoint;
                                    if (sp != null)
                                    {
                                        partDoc.ClearSelection2(true);
                                        sp.Select4(false, null);
                                        partDoc.SketchAddConstraints("sgFIXED");
                                    }
                                }
                                partDoc.ClearSelection2(true);
                            }
                        }
                        catch { }
                    }

                    CreateMirrorPartPackage.LogDebug($"[SLOT_RECREATED_{i}] type={slotSnap.CreationType} L={slotSnap.Length * 1000.0:F2}mm W={slotSnap.Width * 1000.0:F2}mm P1=({newX1 * 1000.0:F2},{newY1 * 1000.0:F2}) P2=({newX2 * 1000.0:F2},{newY2 * 1000.0:F2}) mapped3D={mapped3D} created={newSlot != null}");
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
