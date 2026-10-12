using System;
using System.Collections.Generic;
using System.Diagnostics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal sealed class FlatBendMeasurement
    {
        public double? AllowanceMm;
        public double? AngleDeg;
        public double? RadiusMm;
        public double? FlatLengthMm;
        public double? StraightLengthMm;
        public int Samples;
        public string Source;
        public string Error;
        // A ready model can lack available developed geometry. Only this case
        // permits a separately labelled explicit scalar-setting fallback.
        public bool GeometryUnavailable;

        public void Add(double allowanceMm, double angleDeg, double radiusMm, string source,
            double? flatLengthMm = null, double? straightLengthMm = null)
        {
            if (!BendValueMath.IsFinite(allowanceMm) || allowanceMm <= 0
                || !BendValueMath.IsFinite(angleDeg) || angleDeg <= 0 || angleDeg >= 180
                || !BendValueMath.IsFinite(radiusMm) || radiusMm < 0)
            { Error = "Gia tri do tu Flat-Pattern khong hop le."; GeometryUnavailable = false; return; }
            if (AllowanceMm.HasValue && (Math.Abs(AllowanceMm.Value - allowanceMm) > 0.001
                || Math.Abs(AngleDeg.Value - angleDeg) > 0.01 || Math.Abs(RadiusMm.Value - radiusMm) > 0.001))
            { Error = "Cac mau do cua cung bend khong dong nhat; chua xac dinh duoc he so."; GeometryUnavailable = false; return; }
            AllowanceMm = allowanceMm; AngleDeg = angleDeg; RadiusMm = radiusMm;
            Source = source; GeometryUnavailable = false;
            if (flatLengthMm.HasValue && straightLengthMm.HasValue)
            { FlatLengthMm = flatLengthMm; StraightLengthMm = straightLengthMm; }
            Samples++;
        }
    }

    // Read only the geometry already available in the current configuration.
    // A folded model cannot establish its previously developed bend allowance.
    internal static class KegakiFlatPatternReader
    {
        public static Dictionary<int, FlatBendMeasurement> Measure(ModelDoc2 model,
            List<Feature> allFeatures, List<Feature> bendFeatures, List<KegakiBendResult> rows)
        {
            var result = new Dictionary<int, FlatBendMeasurement>();
            if (rows == null) return result;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].HasBendGeometry && !rows[i].IsLargeRadiusBend
                    && !KegakiChecker.IsAlpolicMaterial(rows[i].MaterialName))
                    result.Add(i, new FlatBendMeasurement());
            if (result.Count == 0) return result;
            try
            {
                if (model == null || allFeatures == null || bendFeatures == null || bendFeatures.Count != rows.Count)
                    throw new InvalidOperationException("Thieu model/anh xa feature de doc hinh hoc trai hien co.");
                Configuration configuration = model.ConfigurationManager.ActiveConfiguration;
                EnsureReady(model, configuration);
                int activeFlatCount = 0;
                foreach (Feature feature in allFeatures)
                {
                    if (!string.Equals(feature.GetTypeName2(), "FlatPattern", StringComparison.OrdinalIgnoreCase)) continue;
                    Array flags = feature.IsSuppressed2((int)swInConfigurationOpts_e.swThisConfiguration, null) as Array;
                    if (flags == null || flags.Length != 1)
                        throw new InvalidOperationException("Khong doc duoc trang thai Flat-Pattern hien tai.");
                    if (Convert.ToBoolean(flags.GetValue(flags.GetLowerBound(0)))) continue;
                    activeFlatCount++;
                    FlatPatternFeatureData definition = feature.GetDefinition() as FlatPatternFeatureData;
                    if (definition == null)
                        throw new InvalidOperationException("Khong doc duoc dinh nghia Flat-Pattern hien tai.");
                    if (definition.MergeFace || definition.SimplifyBends)
                    {
                        MarkUnavailable(result, "Flat-Pattern hien co gop mat/don gian bend; chua tach duoc vung bend rieng. CHECK khong doi feature va khong rebuild.");
                        EnsureReady(model, configuration);
                        return result;
                    }
                }
                if (activeFlatCount == 0)
                {
                    MarkUnavailable(result, "Khong co hinh hoc trai san trong configuration hien tai; CHECK khong mo Flat-Pattern va khong rebuild.");
                    EnsureReady(model, configuration);
                    return result;
                }
                var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (Feature feature in allFeatures)
                {
                    string name = feature.Name ?? "";
                    if (!nameCounts.ContainsKey(name)) nameCounts.Add(name, 0);
                    nameCounts[name]++;
                }
                foreach (KeyValuePair<int, FlatBendMeasurement> entry in result)
                {
                    Feature feature = bendFeatures[entry.Key];
                    if (string.Equals(feature.GetTypeName2(), "EdgeFlange", StringComparison.OrdinalIgnoreCase)) continue;
                    MeasureOwnedStrip(feature, rows[entry.Key], entry.Value, nameCounts);
                }
                ApplyParentMeasurements(bendFeatures, rows, result);
                foreach (FlatBendMeasurement measurement in result.Values)
                    if (!measurement.AllowanceMm.HasValue && string.IsNullOrWhiteSpace(measurement.Error))
                    {
                        measurement.GeometryUnavailable = true;
                        measurement.Error = "Khong co vung trai rieng cua bend nay de do; CHECK khong rebuild va khong suy doan he so tu ten bang.";
                    }
                // Detect a concurrent edit or an API read that exposed pending
                // configuration data. Never accept a sample from that snapshot.
                EnsureReady(model, configuration);
            }
            catch (Exception ex)
            {
                foreach (FlatBendMeasurement measurement in result.Values)
                {
                    measurement.AllowanceMm = null; measurement.Samples = 0;
                    measurement.Error = ex.Message; measurement.GeometryUnavailable = false;
                }
                Debug.WriteLine("[CHECK KEGAKI FLAT READ ONLY] " + ex);
            }
            return result;
        }

        private static void MarkUnavailable(Dictionary<int, FlatBendMeasurement> result, string detail)
        {
            foreach (FlatBendMeasurement measurement in result.Values)
            { measurement.Error = detail; measurement.GeometryUnavailable = true; }
        }

        private static void EnsureReady(ModelDoc2 model, Configuration configuration)
        {
            if (configuration == null || configuration.IsDirty() || configuration.NeedsRebuild
                || model.Extension.NeedsRebuild2 != 0 || model.GetSaveFlag())
                throw new InvalidOperationException("Model/configuration chua san sang hoac can cap nhat; CHECK khong rebuild va khong do hinh hoc cu.");
        }

        private static void MeasureOwnedStrip(Feature feature, KegakiBendResult row,
            FlatBendMeasurement measurement, Dictionary<string, int> nameCounts)
        {
            string name = feature.Name ?? "";
            int ownerCount;
            if (string.IsNullOrWhiteSpace(name) || !nameCounts.TryGetValue(name, out ownerCount) || ownerCount != 1)
            { Unavailable(measurement, "Ten feature bend khong duy nhat; khong anh xa vung trai bang suy doan."); return; }
            IOneBendFeatureData data = feature.GetDefinition() as IOneBendFeatureData;
            Array segments = data == null ? null : data.FlatPatternSketchSegments2 as Array;
            if (segments == null || segments.Length != 1)
            { Unavailable(measurement, "Khong co mot bend line rieng trong Flat-Pattern hien co."); return; }
            SketchSegment segment = segments.GetValue(segments.GetLowerBound(0)) as SketchSegment;
            SketchLine center = segment as SketchLine;
            if (center == null || !segment.IsBendLine())
            { Unavailable(measurement, "Bend line hien co khong phai duong thang rieng."); return; }
            Sketch sketch = segment.GetSketch();
            MathTransform transform = sketch == null ? null : sketch.ModelToSketchTransform as MathTransform;
            MathTransform inverse = transform == null ? null : transform.IInverse();
            double[] matrix = inverse == null ? null : inverse.ArrayData as double[];
            SketchPoint a = center.GetStartPoint2() as SketchPoint, b = center.GetEndPoint2() as SketchPoint;
            if (matrix == null || matrix.Length < 13 || a == null || b == null)
            { Unavailable(measurement, "Khong doc duoc toa do bend line hien co."); return; }
            double[] centerStart = FlatGeometry.Transform(new[] { a.X, a.Y, a.Z }, matrix);
            double[] centerEnd = FlatGeometry.Transform(new[] { b.X, b.Y, b.Z }, matrix);
            double[] planeNormal = FlatGeometry.Subtract(FlatGeometry.Transform(new double[] { 0, 0, 1 }, matrix),
                FlatGeometry.Transform(new double[] { 0, 0, 0 }, matrix));
            if (!ValidPoint(centerStart) || !ValidPoint(centerEnd) || !ValidPoint(planeNormal))
                throw new InvalidOperationException("Toa do bend line/plane hien co khong hop le.");
            Array faces = feature.GetFaces() as Array;
            if (faces == null)
            { Unavailable(measurement, "Feature bend khong co mat trai hien co."); return; }
            foreach (object item in faces)
            {
                Face2 face = item as Face2;
                Surface surface = face == null ? null : face.GetSurface() as Surface;
                if (surface == null || !surface.IsPlane()) continue;
                Feature owner = face.GetFeature() as Feature;
                if (owner == null || !string.Equals(owner.Name, name, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(owner.GetTypeName2(), feature.GetTypeName2(), StringComparison.OrdinalIgnoreCase)) continue;
                double[] plane = surface.PlaneParams as double[];
                if (plane == null || plane.Length < 6
                    || !FlatGeometry.Parallel(new[] { plane[0], plane[1], plane[2] }, planeNormal)) continue;
                Array edges = face.GetEdges() as Array;
                // Only a separate rectangular strip is safe without folded
                // topology. Holes, curved boundaries and merged faces need CHECK.
                if (edges == null || edges.Length != 4) continue;
                var starts = new List<double[]>(); var ends = new List<double[]>();
                foreach (object edgeItem in edges)
                {
                    Edge edge = edgeItem as Edge;
                    Curve curve = edge == null ? null : edge.GetCurve() as Curve;
                    Vertex first = edge == null ? null : edge.GetStartVertex() as Vertex;
                    Vertex last = edge == null ? null : edge.GetEndVertex() as Vertex;
                    if (curve == null || !curve.IsLine() || first == null || last == null) break;
                    double[] start = first.GetPoint() as double[], end = last.GetPoint() as double[];
                    if (!ValidPoint(start) || !ValidPoint(end))
                        throw new InvalidOperationException("Toa do canh vung bend hien co khong hop le.");
                    starts.Add(start); ends.Add(end);
                }
                double width;
                if (!TryBendStripWidth(centerStart, centerEnd, planeNormal, starts, ends, out width)) continue;
                measurement.Add(width * 1000.0, row.AngleDeg, row.RadiusMm,
                    "Do vung bend rieng trong Flat-Pattern hien co; goc/R tu feature; khong rebuild");
            }
            if (!measurement.AllowanceMm.HasValue && string.IsNullOrWhiteSpace(measurement.Error))
                Unavailable(measurement, "Chua co vung bend hinh chu nhat rieng, doi xung theo bend line va dung feature chu; CHECK khong rebuild.");
        }

        private static void Unavailable(FlatBendMeasurement measurement, string detail)
        { measurement.GeometryUnavailable = true; measurement.Error = detail; }

        // Geometry is in metres. Reject a flange rectangle, a thickness-side
        // face or an irregular/shared region rather than treating its span as BA.
        internal static bool TryBendStripWidth(double[] centerStart, double[] centerEnd, double[] normal,
            List<double[]> starts, List<double[]> ends, out double width)
        {
            width = 0;
            const double tolerance = 0.000001;
            if (!ValidPoint(centerStart) || !ValidPoint(centerEnd) || !ValidPoint(normal)
                || starts == null || ends == null || starts.Count != 4 || ends.Count != 4) return false;
            normal = (double[])normal.Clone();
            double[] axis = FlatGeometry.Subtract(centerEnd, centerStart);
            double length = Math.Sqrt(FlatGeometry.Dot(axis, axis));
            double normalLength = Math.Sqrt(FlatGeometry.Dot(normal, normal));
            if (length <= tolerance || normalLength <= 1e-12 || !FlatGeometry.Perpendicular(axis, normal)) return false;
            for (int i = 0; i < 3; i++) { axis[i] /= length; normal[i] /= normalLength; }
            double[] transverse = new[] { normal[1] * axis[2] - normal[2] * axis[1],
                normal[2] * axis[0] - normal[0] * axis[2], normal[0] * axis[1] - normal[1] * axis[0] };
            var u = new List<double>(); var v = new List<double>(); var planeOffsets = new List<double>();
            double minU = double.PositiveInfinity, maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                if (!ValidPoint(starts[i]) || !ValidPoint(ends[i])) return false;
                foreach (double[] point in new[] { starts[i], ends[i] })
                {
                    double[] delta = FlatGeometry.Subtract(point, centerStart);
                    double along = FlatGeometry.Dot(delta, axis), across = FlatGeometry.Dot(delta, transverse);
                    u.Add(along); v.Add(across); planeOffsets.Add(FlatGeometry.Dot(delta, normal));
                    minU = Math.Min(minU, along); maxU = Math.Max(maxU, along);
                    minV = Math.Min(minV, across); maxV = Math.Max(maxV, across);
                }
            }
            if (Math.Abs(minU) > tolerance || Math.Abs(maxU - length) > tolerance
                || minV >= -tolerance || maxV <= tolerance || Math.Abs(minV + maxV) > tolerance) return false;
            var corners = new int[4]; var edgeKeys = new HashSet<string>();
            int alongEdges = 0, acrossEdges = 0;
            for (int i = 0; i < 8; i++)
            {
                if (Math.Abs(planeOffsets[i] - planeOffsets[0]) > tolerance) return false;
                int cu = Math.Abs(u[i] - minU) <= tolerance ? 0 : Math.Abs(u[i] - maxU) <= tolerance ? 1 : -1;
                int cv = Math.Abs(v[i] - minV) <= tolerance ? 0 : Math.Abs(v[i] - maxV) <= tolerance ? 1 : -1;
                if (cu < 0 || cv < 0) return false;
                corners[cu * 2 + cv]++;
                if (i % 2 == 0) continue;
                int previousU = Math.Abs(u[i - 1] - minU) <= tolerance ? 0 : 1;
                int previousV = Math.Abs(v[i - 1] - minV) <= tolerance ? 0 : 1;
                int previous = previousU * 2 + previousV, current = cu * 2 + cv;
                if (previous == current || !edgeKeys.Add(Math.Min(previous, current) + ":" + Math.Max(previous, current))) return false;
                if (previousV == cv && previousU != cu) alongEdges++;
                else if (previousU == cu && previousV != cv) acrossEdges++;
                else return false;
            }
            foreach (int count in corners) if (count != 2) return false;
            if (alongEdges != 2 || acrossEdges != 2) return false;
            width = maxV - minV;
            return BendValueMath.IsFinite(width) && width > 2 * tolerance;
        }

        private static bool ValidPoint(double[] point)
        { return point != null && point.Length >= 3 && BendValueMath.IsFinite(point[0])
            && BendValueMath.IsFinite(point[1]) && BendValueMath.IsFinite(point[2]); }

        private static void ApplyParentMeasurements(List<Feature> features, List<KegakiBendResult> rows,
            Dictionary<int, FlatBendMeasurement> measurements)
        {
            foreach (KeyValuePair<int, FlatBendMeasurement> entry in measurements)
            {
                int index = entry.Key;
                if (!string.Equals(features[index].GetTypeName2(), "EdgeFlange", StringComparison.OrdinalIgnoreCase)) continue;
                var children = new List<int>();
                Feature child = features[index].GetFirstSubFeature() as Feature;
                while (child != null)
                {
                    string type = child.GetTypeName2();
                    if (string.Equals(type, "3DToroidalBend", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Value.Error = "Bend cong 3D: chua co phep do/quy doi rieng duoc xac nhan; CHECK khong rebuild.";
                        entry.Value.GeometryUnavailable = false;
                        children.Clear(); break;
                    }
                    if (string.Equals(type, "OneBend", StringComparison.OrdinalIgnoreCase))
                        for (int i = 0; i < features.Count; i++)
                            if (string.Equals(features[i].Name, child.Name, StringComparison.OrdinalIgnoreCase)) { children.Add(i); break; }
                    child = child.GetNextSubFeature() as Feature;
                }
                if (children.Count == 0) continue;
                var aggregate = new FlatBendMeasurement(); bool complete = true, unavailable = true;
                foreach (int childIndex in children)
                {
                    FlatBendMeasurement measurement;
                    if (!measurements.TryGetValue(childIndex, out measurement))
                    { complete = false; continue; }
                    if (!measurement.GeometryUnavailable && !string.IsNullOrWhiteSpace(measurement.Error)) unavailable = false;
                    if (!measurement.AllowanceMm.HasValue || !string.IsNullOrWhiteSpace(measurement.Error)
                        || !measurement.AngleDeg.HasValue || !measurement.RadiusMm.HasValue
                        || Math.Abs(rows[index].AngleDeg - measurement.AngleDeg.Value) > 0.01
                        || Math.Abs(rows[index].RadiusMm - measurement.RadiusMm.Value) > 0.001)
                    { complete = false; if (measurement.AllowanceMm.HasValue) unavailable = false; continue; }
                    aggregate.Add(measurement.AllowanceMm.Value, measurement.AngleDeg.Value, measurement.RadiusMm.Value,
                        "Do cac bend con hien co cua " + features[index].Name + "; khong rebuild");
                }
                if (complete && string.IsNullOrWhiteSpace(aggregate.Error) && aggregate.AllowanceMm.HasValue)
                    entry.Value.Add(aggregate.AllowanceMm.Value, aggregate.AngleDeg.Value, aggregate.RadiusMm.Value, aggregate.Source);
                else
                {
                    entry.Value.Error = string.IsNullOrWhiteSpace(aggregate.Error)
                        ? "Khong du mau hinh hoc rieng va dong nhat cua cac bend con." : aggregate.Error;
                    entry.Value.GeometryUnavailable = unavailable && string.IsNullOrWhiteSpace(aggregate.Error);
                }
            }
        }
    }

    internal static class FlatGeometry
    {
        public static double[] Subtract(double[] a, double[] b)
        { return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] }; }
        public static double[] Transform(double[] point, double[] matrix)
        {
            return new[] { matrix[12] * (point[0] * matrix[0] + point[1] * matrix[3] + point[2] * matrix[6]) + matrix[9],
                matrix[12] * (point[0] * matrix[1] + point[1] * matrix[4] + point[2] * matrix[7]) + matrix[10],
                matrix[12] * (point[0] * matrix[2] + point[1] * matrix[5] + point[2] * matrix[8]) + matrix[11] };
        }
        public static double[] Project(double[] point, double[] origin, double[] normal)
        {
            double distance = Dot(Subtract(point, origin), normal) / Dot(normal, normal);
            return new[] { point[0] - distance * normal[0], point[1] - distance * normal[1], point[2] - distance * normal[2] };
        }
        public static double Dot(double[] a, double[] b)
        { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
        public static double Angle(double[] a, double[] b)
        {
            double scale = Math.Sqrt(Dot(a, a) * Dot(b, b));
            return scale < 1e-20 ? double.NaN : Math.Acos(Math.Max(-1, Math.Min(1, Dot(a, b) / scale))) * 180.0 / Math.PI;
        }
        public static double Distance(double[] a, double[] b)
        { double[] d = Subtract(a, b); return Math.Sqrt(Dot(d, d)); }
        public static bool Parallel(double[] a, double[] b)
        { double scale = Math.Sqrt(Dot(a, a) * Dot(b, b)); return scale > 1e-16 && Math.Abs(Dot(a, b)) / scale >= 1 - 1e-8; }
        public static bool Perpendicular(double[] a, double[] b)
        { double scale = Math.Sqrt(Dot(a, a) * Dot(b, b)); return scale > 1e-16 && Math.Abs(Dot(a, b)) / scale < 1e-6; }
        public static double PlaneDistance(double[] point, double[] plane)
        {
            double[] normal = new[] { plane[0], plane[1], plane[2] };
            double[] origin = new[] { plane[3], plane[4], plane[5] };
            return Math.Abs(Dot(Subtract(point, origin), normal)) / Math.Sqrt(Dot(normal, normal));
        }
        public static bool TryStripWidth(List<double[]> starts, List<double[]> ends, out double width)
        {
            width = 0;
            if (starts.Count < 2 || starts.Count != ends.Count) return false;
            double[] axis = Subtract(ends[0], starts[0]); double axisLength = Math.Sqrt(Dot(axis, axis));
            if (axisLength <= 1e-12) return false;
            double[] transverse = null;
            double min = 0, max = 0;
            for (int i = 1; i < starts.Count; i++)
            {
                if (!Parallel(axis, Subtract(ends[i], starts[i]))) return false;
                double[] delta = Subtract(starts[i], starts[0]);
                double projection = Dot(delta, axis) / (axisLength * axisLength);
                double[] offset = new[] { delta[0] - projection * axis[0], delta[1] - projection * axis[1], delta[2] - projection * axis[2] };
                double distance = Math.Sqrt(Dot(offset, offset));
                if (distance < 1e-9) continue;
                if (transverse == null) transverse = new[] { offset[0] / distance, offset[1] / distance, offset[2] / distance };
                if (!Parallel(offset, transverse)) return false;
                double value = Dot(offset, transverse); min = Math.Min(min, value); max = Math.Max(max, value);
            }
            width = max - min;
            return BendValueMath.IsFinite(width) && width > 1e-9;
        }
        public static bool IsOpenChain(List<double[]> starts, List<double[]> ends)
        {
            if (starts.Count < 3 || starts.Count != ends.Count) return false;
            var vertices = new List<double[]>(); var degree = new List<int>(); var connections = new List<List<int>>();
            for (int i = 0; i < starts.Count; i++)
            {
                int a = FindVertex(vertices, starts[i]), b = FindVertex(vertices, ends[i]);
                while (degree.Count < vertices.Count) { degree.Add(0); connections.Add(new List<int>()); }
                if (a == b || ++degree[a] > 2 || ++degree[b] > 2) return false;
                connections[a].Add(b); connections[b].Add(a);
            }
            int endpoints = 0; foreach (int d in degree) if (d == 1) endpoints++;
            if (endpoints != 2) return false;
            var seen = new HashSet<int>(); var pending = new Queue<int>(); pending.Enqueue(0);
            while (pending.Count > 0) { int next = pending.Dequeue(); if (!seen.Add(next)) continue; foreach (int n in connections[next]) pending.Enqueue(n); }
            return seen.Count == vertices.Count;
        }
        private static int FindVertex(List<double[]> vertices, double[] point)
        { for (int i = 0; i < vertices.Count; i++) if (Distance(vertices[i], point) < 1e-7) return i; vertices.Add(point); return vertices.Count - 1; }
    }
}
