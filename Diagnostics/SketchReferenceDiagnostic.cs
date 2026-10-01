using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Diagnostics
{
    // Chỉ chẩn đoán trạng thái hiện tại.
    // Không edit, rollback, save, thay reference hoặc di chuyển điểm.
    public static class SketchReferenceDiagnostic
    {
        private class SketchCandidateInfo
        {
            public Feature SketchFeature { get; set; }
            public Sketch Sketch { get; set; }
            public Feature ParentFeature { get; set; }
            public bool IsSuppressed { get; set; }
            public string Name { get; set; }
        }

        private class TraceTarget
        {
            public object Entity { get; set; }
            public string DimensionName { get; set; }
            public string RelationLabel { get; set; }
            public string SourceArray { get; set; }
            public int IndexInRelation { get; set; }
        }

        private class TraceCandidateMatch
        {
            public SketchCandidateInfo SketchInfo { get; set; }
            public string EntityType { get; set; }
            public string EntityId { get; set; }
            public string Status { get; set; }
            public string Evidence { get; set; }
            public bool IsProven { get; set; }
        }

        private static string Read(Func<object> read)
        {
            try
            {
                return Format(read());
            }
            catch (Exception ex)
            {
                return "READ_ERROR:" + ex.GetType().Name
                    + ":" + ex.Message;
            }
        }

        private static string Format(object value)
        {
            if (value == null)
                return "NULL";

            var array = value as Array;

            if (array != null)
            {
                return "[" + string.Join(
                    ",",
                    array.Cast<object>().Select(Format)) + "]";
            }

            return Convert.ToString(
                value,
                CultureInfo.InvariantCulture);
        }

        private static object[] Items(object value)
        {
            if (value == null)
                return new object[0];

            var array = value as Array;

            if (array == null)
                throw new InvalidOperationException(
                    "Expected API array.");

            return array.Cast<object>().ToArray();
        }

        private static string SketchToken(Sketch sketch)
        {
            if (sketch == null)
                return "NULL";

            // Chỉ là bằng chứng hỗ trợ để 
            // Hai Sketch có thể có cùng transform.
            return Read(
                () => sketch.ModelToSketchTransform.ArrayData);
        }

        private static bool SameComObject(object left, object right)
        {
            if (left == null || right == null) return false;
            try
            {
                // SOLIDWORKS object equality works across different COM wrappers.
                // Unsupported/failed comparisons remain unproven.
                var app = SwAddin.InstanceSwApp;
                return app != null && app.IsSame(left, right) == (int)swObjectEquality.swObjectSame;
            }
            catch { return false; }
        }

        private static double[] TransformPoint(double[] pt, MathTransform xform)
        {
            if (xform == null || pt == null || pt.Length < 3)
                throw new InvalidOperationException("REFTRACE32: Missing coordinate frame.");
            var math = SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
            return (double[])((MathPoint)((MathPoint)math.CreatePoint(pt)).MultiplyTransform(xform)).ArrayData;
        }

        private static double[] ComputeModelXYZ(SketchPoint pt, Sketch sketch)
        {
            if (pt == null || sketch == null) throw new InvalidOperationException("REFTRACE32: Missing point owner.");
            double[] local = new double[] { pt.X, pt.Y, pt.Z };
            if (sketch.Is3D()) return local;
            return TransformPoint(local, sketch.ModelToSketchTransform.IInverse());
        }

        private static double Distance(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length < 3 || b.Length < 3) return double.MaxValue;
            double dx = a[0] - b[0];
            double dy = a[1] - b[1];
            double dz = a[2] - b[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static bool ArrayEquals(int[] a, int[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static bool IsInInventory(object entity, object[] points, object[] segments)
        {
            if (entity == null) return true;
            if (entity is SketchPoint pt)
            {
                if (points != null)
                {
                    foreach (var p in points)
                    {
                        if (SameComObject(p, pt)) return true;
                    }
                }
                return false;
            }
            if (entity is SketchSegment seg)
            {
                if (segments != null)
                {
                    foreach (var s in segments)
                    {
                        if (SameComObject(s, seg)) return true;
                    }
                }
                return false;
            }
            return false;
        }

        private static double[][] SegmentModelGeometry(SketchSegment segment)
        {
            var owner = segment.GetSketch();
            var line = segment as SketchLine;
            if (line != null) return new[] {
                ComputeModelXYZ((SketchPoint)line.GetStartPoint2(), owner),
                ComputeModelXYZ((SketchPoint)line.GetEndPoint2(), owner) };
            var arc = segment as SketchArc;
            if (arc != null) return new[] {
                ComputeModelXYZ((SketchPoint)arc.GetStartPoint2(), owner),
                ComputeModelXYZ((SketchPoint)arc.GetCenterPoint2(), owner),
                ComputeModelXYZ((SketchPoint)arc.GetEndPoint2(), owner) };
            return null; // Unsupported geometry is not a zero-distance match.
        }

        private static double GeometryResidual(double[][] a, double[][] b)
        {
            if (a == null || b == null || a.Length != b.Length || a.Length == 0)
                return double.PositiveInfinity;
            double direct = 0, reversed = 0;
            for (int i = 0; i < a.Length; i++)
            {
                direct = Math.Max(direct, Distance(a[i], b[i]));
                reversed = Math.Max(reversed, Distance(a[i], b[b.Length - 1 - i]));
            }
            return Math.Min(direct, reversed);
        }

        private static double[] Unit(double[] vector)
        {
            double n = Math.Sqrt(vector.Sum(x => x * x));
            if (n <= 1e-12) throw new InvalidOperationException("REFCARRIER33: Degenerate direction.");
            return vector.Select(x => x / n).ToArray();
        }

        private static string TraceCarrierEdge(ModelDoc2 model, SketchSegment proxy, Action<string> log)
        {
            var line = proxy as SketchLine;
            if (line == null) return "UNSUPPORTED_PROXY_TYPE";
            var owner = proxy.GetSketch();
            double[] start = ComputeModelXYZ((SketchPoint)line.GetStartPoint2(), owner);
            double[] end = ComputeModelXYZ((SketchPoint)line.GetEndPoint2(), owner);
            double[] proxyDirection = Unit(Enumerable.Range(0, 3).Select(i => end[i] - start[i]).ToArray());
            double[][] probes = Enumerable.Range(0, 5).Select(k => Enumerable.Range(0, 3)
                .Select(i => start[i] + (end[i] - start[i]) * k / 4.0).ToArray()).ToArray();
            var accepted = new List<Tuple<Edge, int, int, double, double>>();
            int bodyIndex = -1;
            foreach (Body2 body in Items(((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false)))
            {
                bodyIndex++;
                int edgeIndex = -1;
                foreach (Edge edge in Items(body.GetEdges()))
                {
                    edgeIndex++;
                    var closest = new List<double[]>();
                    double residual = 0;
                    bool readable = true;
                    foreach (double[] probe in probes)
                    {
                        double[] hit = edge.GetClosestPointOn(probe[0], probe[1], probe[2]) as double[];
                        if (hit == null || hit.Length < 3) { readable = false; break; }
                        hit = new[] { hit[0], hit[1], hit[2] };
                        closest.Add(hit);
                        residual = Math.Max(residual, Distance(probe, hit));
                    }
                    if (!readable || residual > 1e-7) continue;
                    double[] carrierDirection;
                    try { carrierDirection = Unit(Enumerable.Range(0, 3)
                        .Select(i => closest[closest.Count - 1][i] - closest[0][i]).ToArray()); }
                    catch { continue; }
                    double absDot = Math.Abs(Enumerable.Range(0, 3).Sum(i => proxyDirection[i] * carrierDirection[i]));
                    if (absDot < 0.999999) continue;
                    accepted.Add(Tuple.Create(edge, bodyIndex, edgeIndex, residual, absDot));
                    log("[REFCARRIER33][CANDIDATE] body=" + bodyIndex + " edge=" + edgeIndex +
                        " maxResidual_m=" + residual.ToString("R", CultureInfo.InvariantCulture) +
                        " absDirectionDot=" + absDot.ToString("R", CultureInfo.InvariantCulture) +
                        " curveParams=" + Read(() => edge.GetCurveParams2()));
                }
            }
            string result = accepted.Count == 1 ? "UNIQUE_CARRIER_EDGE" :
                accepted.Count == 0 ? "NO_CARRIER_EDGE" : "AMBIGUOUS_CARRIER_EDGE";
            log("[REFCARRIER33][RESULT] status=" + result + " matches=" + accepted.Count +
                " proxyStart=" + Format(start) + " proxyEnd=" + Format(end));
            return result;
        }

        private static string TraceCarrierFace(ModelDoc2 model, SketchSegment proxy, Action<string> log)
        {
            var line = proxy as SketchLine;
            if (line == null) return "UNSUPPORTED_PROXY_TYPE";
            var owner = proxy.GetSketch();
            double[] start = ComputeModelXYZ((SketchPoint)line.GetStartPoint2(), owner);
            double[] end = ComputeModelXYZ((SketchPoint)line.GetEndPoint2(), owner);
            double[][] probes = Enumerable.Range(0, 9).Select(k => Enumerable.Range(0, 3)
                .Select(i => start[i] + (end[i] - start[i]) * k / 8.0).ToArray()).ToArray();
            var accepted = new List<Tuple<Face2, int, int, double>>();
            int bodyIndex = -1;
            foreach (Body2 body in Items(((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false)))
            {
                bodyIndex++;
                int faceIndex = -1;
                foreach (Face2 face in Items(body.GetFaces()))
                {
                    faceIndex++;
                    double residual = 0;
                    bool readable = true;
                    foreach (double[] probe in probes)
                    {
                        double[] hit = face.GetClosestPointOn(probe[0], probe[1], probe[2]) as double[];
                        if (hit == null || hit.Length < 3) { readable = false; break; }
                        residual = Math.Max(residual, Distance(probe, new[] { hit[0], hit[1], hit[2] }));
                    }
                    if (!readable || residual > 1e-7) continue;
                    accepted.Add(Tuple.Create(face, bodyIndex, faceIndex, residual));
                    var surface = face.GetSurface() as Surface;
                    log("[REFCARRIER34][FACE_CANDIDATE] body=" + bodyIndex + " face=" + faceIndex +
                        " maxResidual_m=" + residual.ToString("R", CultureInfo.InvariantCulture) +
                        " area_m2=" + face.GetArea().ToString("R", CultureInfo.InvariantCulture) +
                        " planar=" + (surface != null && surface.IsPlane()) +
                        " uvBounds=" + Read(() => face.GetUVBounds()));
                }
            }
            string result = accepted.Count == 1 ? "UNIQUE_CARRIER_FACE" :
                accepted.Count == 0 ? "NO_CARRIER_FACE" : "AMBIGUOUS_CARRIER_FACE";
            log("[REFCARRIER34][RESULT] status=" + result + " matches=" + accepted.Count +
                " proxyStart=" + Format(start) + " proxyEnd=" + Format(end));
            return result;
        }

        private static List<SketchCandidateInfo> EnumerateAllSketches(ModelDoc2 model)
        {
            var list = new List<SketchCandidateInfo>();
            if (model == null) return list;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Action<Feature, Feature> processFeature = null;
            processFeature = (feat, parent) =>
            {
                if (feat == null) return;
                string featName = feat.Name;

                var sketch = feat.GetSpecificFeature2() as Sketch;
                if (sketch != null)
                {
                    string key = featName + "|" + Read(() => feat.GetID());
                    if (visited.Add(key))
                    {
                        bool suppressed = false;
                        try
                        {
                            object[] suppArr = Items(feat.IsSuppressed2((int)swInConfigurationOpts_e.swThisConfiguration, null));
                            if (suppArr.Length > 0 && suppArr[0] is bool b)
                                suppressed = b;
                        }
                        catch { }

                        list.Add(new SketchCandidateInfo
                        {
                            SketchFeature = feat,
                            Sketch = sketch,
                            ParentFeature = parent,
                            IsSuppressed = suppressed,
                            Name = featName
                        });
                    }
                }

                Feature subFeat = null;
                try { subFeat = feat.GetFirstSubFeature() as Feature; } catch { }
                while (subFeat != null)
                {
                    processFeature(subFeat, feat);
                    Feature next = null;
                    try { next = subFeat.GetNextSubFeature() as Feature; } catch { }
                    subFeat = next;
                }
            };

            Feature top = model.FirstFeature() as Feature;
            while (top != null)
            {
                processFeature(top, null);
                top = top.GetNextFeature() as Feature;
            }

            return list;
        }

        private static string Entity(object entity)
        {
            if (entity == null)
                return "NULL";

            var point = entity as SketchPoint;

            if (point != null)
            {
                return "SketchPoint id="
                    + Read(() => point.GetID())
                    + " localXYZ="
                    + Read(() => new[]
                    {
                        point.X,
                        point.Y,
                        point.Z
                    })
                    + " ownerTransform="
                    + Read(() => SketchToken(point.GetSketch()));
            }

            var segment = entity as SketchSegment;

            if (segment != null)
            {
                string result =
                    "SketchSegment id="
                    + Read(() => segment.GetID())
                    + " segmentType="
                    + Read(() => segment.GetType())
                    + " construction="
                    + Read(() => segment.ConstructionGeometry)
                    + " ownerTransform="
                    + Read(() => SketchToken(segment.GetSketch()));

                var line = entity as SketchLine;

                if (line != null)
                {
                    result +=
                        " start={"
                        + Read(() => Entity(line.GetStartPoint2()))
                        + "} end={"
                        + Read(() => Entity(line.GetEndPoint2()))
                        + "}";
                }

                var arc = entity as SketchArc;

                if (arc != null)
                {
                    result +=
                        " radius="
                        + Read(() => arc.GetRadius())
                        + " center={"
                        + Read(() => Entity(arc.GetCenterPoint2()))
                        + "}";
                }

                return result;
            }

            var edge = entity as Edge;

            if (edge != null)
            {
                return "Edge curveParams="
                    + Read(() => edge.GetCurveParams2());
            }

            var vertex = entity as Vertex;

            if (vertex != null)
            {
                return "Vertex modelXYZ="
                    + Read(() => vertex.GetPoint());
            }

            var face = entity as Face2;

            if (face != null)
            {
                return "Face area="
                    + Read(() => face.GetArea());
            }

            return "UNCLASSIFIED runtimeType="
                + entity.GetType().FullName;
        }

        private static void DumpArray(
            string label,
            Func<object> read,
            Action<string> log)
        {
            try
            {
                object raw = read();

                if (raw == null)
                {
                    log(label + " array=NULL");
                    return;
                }

                object[] values = Items(raw);

                log(label + " count=" + values.Length);

                for (int i = 0; i < values.Length; i++)
                {
                    object value = values[i];

                    log(
                        label
                        + " index=" + i
                        + " "
                        + Read(() => Entity(value)));
                }
            }
            catch (Exception ex)
            {
                log(label + " ARRAY_READ_ERROR=" + ex.Message);
            }
        }

        private static void TraceReferenceOwner(
            ModelDoc2 model,
            Sketch subjectSketch,
            Feature subjectSketchFeature,
            object[] relations,
            HashSet<string> relationDimensions,
            object[] inventoryPoints,
            object[] inventorySegments,
            Action<string> log)
        {
            var externalReferences = new List<TraceTarget>();

            for (int i = 0; i < relations.Length; i++)
            {
                var relation = relations[i] as SketchRelation;
                if (relation == null) continue;

                var display = relation.GetDisplayDimension() as DisplayDimension;
                var dim = display == null ? null : display.GetDimension() as Dimension;

                if (dim == null) continue;

                string relLabel = "RELATION[" + i + "](DIM=" + dim.FullName + ")";

                object[] defEnts = Items(relation.GetDefinitionEntities2());
                for (int d = 0; d < defEnts.Length; d++)
                {
                    object ent = defEnts[d];
                    log("[REFTRACE32][SUBJECT_MEMBERSHIP] relation=" + relLabel + " index=" + d +
                        " subject=" + subjectSketchFeature.Name + " sameObjectInInventory=" +
                        IsInInventory(ent, inventoryPoints, inventorySegments) + " entity=" + Read(() => Entity(ent)));
                    if (ent != null && !IsInInventory(ent, inventoryPoints, inventorySegments))
                    {
                        if (!externalReferences.Any(x => SameComObject(x.Entity, ent)))
                        {
                            externalReferences.Add(new TraceTarget
                            {
                                Entity = ent,
                                DimensionName = dim.FullName,
                                RelationLabel = relLabel,
                                SourceArray = "DEFINITION",
                                IndexInRelation = d
                            });
                        }
                    }
                }

                object[] intEnts = Items(relation.GetEntities());
                for (int n = 0; n < intEnts.Length; n++)
                {
                    object ent = intEnts[n];
                    if (ent != null && !IsInInventory(ent, inventoryPoints, inventorySegments))
                    {
                        if (!externalReferences.Any(x => SameComObject(x.Entity, ent)))
                        {
                            externalReferences.Add(new TraceTarget
                            {
                                Entity = ent,
                                DimensionName = dim.FullName,
                                RelationLabel = relLabel,
                                SourceArray = "INTERNAL",
                                IndexInRelation = n
                            });
                        }
                    }
                }
            }

            log("TRACE_REFERENCE_OWNER BEGIN count=" + externalReferences.Count);

            if (externalReferences.Count == 0)
            {
                log("TRACE_REFERENCE_OWNER message=All dimension relation entities belong to subject sketch true inventory.");
                return;
            }

            var allSketches = EnumerateAllSketches(model);
            log("TRACE_SKETCHES_SCANNED total=" + allSketches.Count);

            for (int t = 0; t < externalReferences.Count; t++)
            {
                var target = externalReferences[t];
                log("--------------------------------------------------");
                log("TRACE_TARGET[" + t + "] dim=" + target.DimensionName
                    + " relation=" + target.RelationLabel
                    + " source=" + target.SourceArray
                    + " entity=" + Entity(target.Entity));

                var targetOwner = target.Entity is SketchSegment
                    ? ((SketchSegment)target.Entity).GetSketch()
                    : target.Entity is SketchPoint ? ((SketchPoint)target.Entity).GetSketch() : null;
                log("[REFTRACE32][OWNER_MATCHES] " + string.Join(";", allSketches.Select(s =>
                    s.Name + ":IsSame=" + Read(() => SwAddin.InstanceSwApp.IsSame(targetOwner, s.Sketch)))));

                var candidates = new List<TraceCandidateMatch>();

                foreach (var candSketchInfo in allSketches)
                {
                    if (target.Entity is SketchPoint refPt)
                    {
                        int[] refId = refPt.GetID() as int[];
                        double[] refModel = ComputeModelXYZ(refPt, refPt.GetSketch());

                        object[] candPoints = Items(candSketchInfo.Sketch.GetSketchPoints2());
                        for (int pIdx = 0; pIdx < candPoints.Length; pIdx++)
                        {
                            var candPt = candPoints[pIdx] as SketchPoint;
                            if (candPt == null) continue;

                            bool comMatch = SameComObject(refPt, candPt);
                            int[] candId = candPt.GetID() as int[];
                            bool idMatch = ArrayEquals(refId, candId);
                            double[] candLocal = new[] { candPt.X, candPt.Y, candPt.Z };
                            double[] candModel = ComputeModelXYZ(candPt, candSketchInfo.Sketch);
                            double dist = Distance(refModel, candModel);
                            bool coordsMatch = dist < 1e-6;

                            if (comMatch || idMatch || coordsMatch)
                            {
                                string status;
                                string evidence;
                                string reason;

                                if (comMatch)
                                {
                                    status = "ACCEPTED";
                                    evidence = "SOLIDWORKS_IS_SAME";
                                    reason = "SOLIDWORKS identifies the same point.";
                                }
                                else if (idMatch && coordsMatch)
                                {
                                    status = "CANDIDATE_UNPROVEN";
                                    evidence = "ID_AND_COORDS_MATCH";
                                    reason = "ID and model coordinates match; SOLIDWORKS identity not confirmed.";
                                }
                                else if (idMatch)
                                {
                                    status = "REJECTED";
                                    evidence = "ID_MATCH_ONLY";
                                    reason = "ID matches, but model coordinates differ by " + dist.ToString("G7", CultureInfo.InvariantCulture) + "m.";
                                }
                                else
                                {
                                    status = "REJECTED";
                                    evidence = "COORDS_MATCH_ONLY";
                                    reason = "Model coordinates coincident (dist=" + dist.ToString("G7", CultureInfo.InvariantCulture) + "m), but entity ID differs.";
                                }

                                log("  CANDIDATE sketch=" + candSketchInfo.Name
                                    + " parentFeature=" + (candSketchInfo.ParentFeature?.Name ?? "NONE")
                                    + " suppressed=" + candSketchInfo.IsSuppressed
                                    + " entityType=SketchPoint"
                                    + " entityId=" + Format(candId)
                                    + " localXYZ=" + Format(candLocal)
                                    + " ownerTransform=" + SketchToken(candSketchInfo.Sketch)
                                    + " modelXYZ=" + Format(candModel)
                                    + " status=" + status
                                    + " evidence=" + evidence
                                    + " reason=" + reason);

                                if (status == "ACCEPTED" || status == "CANDIDATE_UNPROVEN")
                                {
                                    candidates.Add(new TraceCandidateMatch
                                    {
                                        SketchInfo = candSketchInfo,
                                        EntityType = "SketchPoint",
                                        EntityId = Format(candId),
                                        Status = status,
                                        Evidence = evidence,
                                        IsProven = (status == "ACCEPTED")
                                    });
                                }
                            }
                        }
                    }
                    else if (target.Entity is SketchSegment refSeg)
                    {
                        int[] refId = refSeg.GetID() as int[];
                        object[] candSegs = Items(candSketchInfo.Sketch.GetSketchSegments());
                        for (int sIdx = 0; sIdx < candSegs.Length; sIdx++)
                        {
                            var candSeg = candSegs[sIdx] as SketchSegment;
                            if (candSeg == null) continue;

                            bool comMatch = SameComObject(refSeg, candSeg);
                            int[] candId = candSeg.GetID() as int[];
                            bool idMatch = ArrayEquals(refId, candId);
                            bool typeMatch = refSeg.GetType() == candSeg.GetType();
                            bool ownerMatch = SameComObject(refSeg.GetSketch(), candSketchInfo.Sketch);
                            double[][] sourceGeometry = null, candidateGeometry = null;
                            string geometryError = null;
                            try
                            {
                                sourceGeometry = SegmentModelGeometry(refSeg);
                                candidateGeometry = SegmentModelGeometry(candSeg);
                            }
                            catch (Exception ex) { geometryError = ex.Message; }
                            double residual = typeMatch ? GeometryResidual(sourceGeometry, candidateGeometry) : double.PositiveInfinity;
                            bool geometryMatch = !double.IsNaN(residual) && residual <= 1e-7;

                            if (comMatch || idMatch || geometryMatch)
                            {
                                string status = comMatch && typeMatch ? "ACCEPTED" :
                                    ownerMatch && typeMatch && idMatch && geometryMatch ? "CANDIDATE_UNPROVEN" : "REJECTED";
                                string evidence = comMatch ? "SOLIDWORKS_IS_SAME" : "OWNER_TYPE_ID_GEOMETRY";
                                string reason = "ownerMatch=" + ownerMatch + " typeMatch=" + typeMatch + " idMatch=" + idMatch +
                                    " residual_m=" + residual.ToString("R", CultureInfo.InvariantCulture) + " geometryError=" + geometryError;

                                log("  CANDIDATE sketch=" + candSketchInfo.Name
                                    + " parentFeature=" + (candSketchInfo.ParentFeature?.Name ?? "NONE")
                                    + " suppressed=" + candSketchInfo.IsSuppressed
                                    + " entityType=SketchSegment(" + candSeg.GetType() + ")"
                                    + " entityId=" + Format(candId)
                                    + " ownerTransform=" + SketchToken(candSketchInfo.Sketch)
                                    + " sourceModelGeometry=" + Format(sourceGeometry)
                                    + " candidateModelGeometry=" + Format(candidateGeometry)
                                    + " status=" + status
                                    + " evidence=" + evidence
                                    + " reason=" + reason);

                                if (status == "ACCEPTED" || status == "CANDIDATE_UNPROVEN")
                                {
                                    candidates.Add(new TraceCandidateMatch
                                    {
                                        SketchInfo = candSketchInfo,
                                        EntityType = "SketchSegment",
                                        EntityId = Format(candId),
                                        Status = status,
                                        Evidence = evidence,
                                        IsProven = (status == "ACCEPTED")
                                    });
                                }
                            }
                        }
                    }
                }

                var proven = candidates.Where(c => c.IsProven).ToList();
                if (proven.Count == 1)
                {
                    var p = proven[0];
                    log("  TRACE_RESULT=UNIQUE_OWNER_PROVEN sketch=" + p.SketchInfo.Name
                        + " parentFeature=" + (p.SketchInfo.ParentFeature?.Name ?? "NONE")
                        + " entityType=" + p.EntityType
                        + " entityId=" + p.EntityId
                        + " evidence=" + p.Evidence);
                }
                else if (candidates.Count > 1)
                {
                    log("  TRACE_RESULT=MULTIPLE_CANDIDATES count=" + candidates.Count
                        + " candidates=["
                        + string.Join("; ", candidates.Select(c => c.SketchInfo.Name + ":" + c.EntityType + ":" + c.EntityId + "(" + c.Evidence + ")"))
                        + "]");
                }
                else if (candidates.Count == 1 && !candidates[0].IsProven)
                {
                    var c = candidates[0];
                    log("  TRACE_RESULT=SINGLE_UNPROVEN_CANDIDATE sketch=" + c.SketchInfo.Name
                        + " parentFeature=" + (c.SketchInfo.ParentFeature?.Name ?? "NONE")
                        + " entityType=" + c.EntityType
                        + " entityId=" + c.EntityId
                        + " evidence=" + c.Evidence
                        + " note=Unproven; does NOT meet criteria for proven unique owner");
                }
                else
                {
                    log("  TRACE_RESULT=NOT_FOUND_IN_SKETCH_INVENTORIES note=Target entity was not found in any sketch inventory; does NOT imply proxy or model edge");
                    var unresolvedSegment = target.Entity as SketchSegment;
                    if (unresolvedSegment != null && SameComObject(unresolvedSegment.GetSketch(), subjectSketch))
                    {
                        string edgeResult = TraceCarrierEdge(model, unresolvedSegment, log);
                        if (edgeResult == "NO_CARRIER_EDGE") TraceCarrierFace(model, unresolvedSegment, log);
                    }
                }
            }

            log("TRACE_REFERENCE_OWNER END");
        }

        public static void Run(
            ModelDoc2 model,
            Feature sketchFeature,
            Action<string> output)
        {
            Run(model, sketchFeature, output, null, "current-state");
        }

        public static void Run(
            ModelDoc2 model,
            Feature sketchFeature,
            Action<string> output,
            Feature contextFeature,
            string contextState = "current-state")
        {
            if (model == null ||
                sketchFeature == null ||
                output == null)
            {
                throw new ArgumentNullException(
                    "model/sketchFeature/output");
            }

            var sketch =
                sketchFeature.GetSpecificFeature2() as Sketch;

            if (sketch == null)
            {
                throw new InvalidOperationException(
                    "Feature is not a sketch.");
            }

            string runId = Guid.NewGuid().ToString("N");

            Action<string> log = text =>
                output("[REFDIAG][" + runId + "] " + text);

            bool dirtyBefore = model.GetSaveFlag();

            var relationDimensions =
                new HashSet<string>(StringComparer.Ordinal);

            log(
                "BEGIN path=" + model.GetPathName()
                + " sketch=" + sketchFeature.Name
                + " featureId="
                + Read(() => sketchFeature.GetID()));
            log("VERSION=REFTRACE32 equality=ISldWorks.IsSame geometry=MathPoint.MultiplyTransform");

            log(
                "CONFIG="
                + Read(() =>
                    model.ConfigurationManager
                        .ActiveConfiguration.Name));

            log(
                "CONTEXT=" + contextState + "; "
                + (contextState.EndsWith("before-cut", StringComparison.Ordinal)
                    ? ("rollback to before feature=" + (contextFeature?.Name ?? "cut") + "; ")
                    : "no rollback requested; ")
                + "coordinates in metres");

            try
            {
                object[] inventoryPoints = Items(sketch.GetSketchPoints2());
                object[] inventorySegments = Items(sketch.GetSketchSegments());

                DumpArray(
                    "INVENTORY_POINTS",
                    () => inventoryPoints,
                    log);

                DumpArray(
                    "INVENTORY_SEGMENTS",
                    () => inventorySegments,
                    log);

                object[] relations = Items(
                    sketch.RelationManager.GetRelations(
                        (int)swSketchRelationFilterType_e.swAll));

                for (int i = 0; i < relations.Length; i++)
                {
                    var relation =
                        relations[i] as SketchRelation;

                    // Index chỉ để đánh số log, không phải ID bền vững.
                    string label = "RELATION[" + i + "]";

                    if (relation == null)
                    {
                        log(label + " UNAVAILABLE");
                        continue;
                    }

                    log(
                        label
                        + " type="
                        + Read(() => relation.GetRelationType())
                        + " suppressed="
                        + Read(() => relation.Suppressed));

                    // Giữ hai nguồn dữ liệu riêng biệt.
                    // Không thay null bằng phần tử cùng index
                    // trong mảng còn lại.
                    DumpArray(
                        label + ".INTERNAL",
                        () => relation.GetEntities(),
                        log);

                    DumpArray(
                        label + ".DEFINITION",
                        () => relation.GetDefinitionEntities2(),
                        log);

                    log(
                        label + ".ENTITY_TYPES="
                        + Read(() => relation.GetEntitiesType()));

                    try
                    {
                        var display =
                            relation.GetDisplayDimension()
                                as DisplayDimension;

                        var dim = display == null
                            ? null
                            : display.GetDimension() as Dimension;

                        if (dim != null)
                        {
                            relationDimensions.Add(dim.FullName);

                            log(
                                label
                                + " DIM=" + dim.FullName
                                + " valueSI="
                                + Read(() => dim.SystemValue)
                                + " drivenState="
                                + Read(() => dim.DrivenState));
                        }
                    }
                    catch (Exception ex)
                    {
                        log(
                            label
                            + " DIM_READ_ERROR="
                            + ex.Message);
                    }
                }

                // Kiểm kê dimension độc lập với danh sách relation.
                // Không im lặng bỏ qua dimension chưa có coverage.
                var seen =
                    new HashSet<string>(StringComparer.Ordinal);

                var current =
                    sketchFeature.GetFirstDisplayDimension()
                        as DisplayDimension;

                while (current != null)
                {
                    var dim =
                        current.GetDimension() as Dimension;

                    if (dim == null)
                    {
                        throw new InvalidOperationException(
                            "Dimension unavailable.");
                    }

                    if (!seen.Add(dim.FullName))
                    {
                        throw new InvalidOperationException(
                            "Repeated dimension; enumeration stopped.");
                    }

                    log(
                        "DIMENSION_INVENTORY name=" + dim.FullName
                        + " foundInRelations="
                        + relationDimensions.Contains(dim.FullName)
                        + " valueSI="
                        + Read(() => dim.SystemValue));

                    current =
                        sketchFeature.GetNextDisplayDimension(current)
                            as DisplayDimension;
                }

                // BƯỚC MỚI: TRACE_REFERENCE_OWNER
                TraceReferenceOwner(
                    model,
                    sketch,
                    sketchFeature,
                    relations,
                    relationDimensions,
                    inventoryPoints,
                    inventorySegments,
                    log);

                log(
                    "RESULT=OBSERVATION_ONLY; "
                    + "ownership and mapping NOT PROVEN");
            }
            finally
            {
                bool dirtyAfter = model.GetSaveFlag();

                log(
                    "END dirtyBefore=" + dirtyBefore
                    + " dirtyAfter=" + dirtyAfter);

                if (dirtyBefore != dirtyAfter)
                {
                    throw new InvalidOperationException(
                        "Document dirty state changed during "
                        + "diagnostic; do not save.");
                }
            }
        }
    }
}
