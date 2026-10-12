// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Cut-Extrude MARK (flatten-dxf và view2dxf):
    ///  - mô tả mặt kề của từng cạnh MARK (miệng lỗ / trong lòng, mặt thành) cho
    ///    <see cref="MarkRimSelector"/> — lỗ cắt xéo 2 vòng chỉ giữ vòng ngoài cùng;
    ///  - nhận lại bằng HÌNH HỌC các cạnh của feature MARK mà chủ sở hữu mặt không chỉ ra
    ///    (mặt đã trải phẳng có thể báo chủ khác);
    ///  - chẩn đoán khi một feature MARK vẫn không có cạnh nào trên view.
    /// Chỉ đọc model; mọi hình vẽ thêm nằm trên Drawing tạm.
    /// </summary>
    internal partial class DxfOutPut
    {
        /// <summary>Ghi vào <paramref name="target"/>: cạnh có phải miệng lỗ, và khoá các mặt thành cong thuộc MARK.</summary>
        private static void DescribeMarkFaces(
            IEdge edge,
            LaserMarkFeatureRecognizer recognizer,
            string source,
            VisibleEdgeReference target)
        {
            target.MarkSource = source ?? "";
            target.WallKeys.Clear();
            target.IsMouth = true;
            object[] faces = null;
            try { faces = edge?.GetTwoAdjacentFaces2() as object[]; } catch { }
            if (faces == null) return;

            int markFaces = 0, otherFaces = 0;
            foreach (IFace2 face in faces.OfType<IFace2>())
            {
                bool isMark = recognizer.TryResolveFace(face, out LaserMarkFeatureRecognizer.Resolution r) &&
                              string.Equals(r.SourceName, source, StringComparison.OrdinalIgnoreCase);
                if (!isMark) { otherFaces++; continue; }
                markFaces++;
                if (!IsPlanarFace(face))
                {
                    string key = FaceKey(face);
                    if (key.Length > 0) target.WallKeys.Add(key);
                }
            }
            // Hai mặt kề đều thuộc MARK = cạnh trong lòng hốc (đáy, chỗ gấp khúc của thành).
            target.IsMouth = otherFaces > 0 || markFaces < 2;
        }

        private static bool IsPlanarFace(IFace2 face)
        {
            try { return face.GetSurface() is Surface s && s.IsPlane(); }
            catch { return false; }
        }

        /// <summary>
        /// Khoá nhận diện một mặt: khung bao + diện tích (làm tròn 0,01 mm / 0,01 mm²). Cùng một mặt hỏi
        /// từ hai cạnh khác nhau trả RCW khác nhau nhưng số liệu y hệt.
        /// </summary>
        private static string FaceKey(IFace2 face)
        {
            try
            {
                if (!(face.GetBox() is double[] box) || box.Length < 6) return "";
                double area = face.GetArea();
                return string.Join("|", box.Take(6).Select(v => Math.Round(v * 1e5).ToString(CultureInfo.InvariantCulture))) +
                       "|" + Math.Round(area * 1e8).ToString(CultureInfo.InvariantCulture);
            }
            catch { return ""; }
        }

        // ───────────────────────────── vòng ngoài cùng ─────────────────────────────

        /// <summary>
        /// Các cạnh MARK là đường tròn KÍN, chiếu lên sketch view → ứng viên cho
        /// <see cref="MarkRimSelector"/>. Trả chỉ số (trong <paramref name="edges"/>) các vòng phải bỏ.
        /// </summary>
        private static HashSet<int> SelectDroppedMarkRims(
            IList<VisibleEdgeReference> edges,
            MathTransform modelToView,
            MathTransform modelToSketch,
            MathUtility mathUtility,
            string logPrefix)
        {
            var candidates = new List<MarkRimCandidate>();
            for (int i = 0; i < edges.Count; i++)
            {
                VisibleEdgeReference e = edges[i];
                if (e?.Edge == null || e.WallKeys.Count == 0) continue;
                ICurve curve = null;
                try
                {
                    curve = e.Edge.GetCurve() as ICurve;
                    if (curve == null || curve.IsLine() || !curve.IsCircle()) continue;
                    if (!(e.Edge.GetCurveParams2() is double[] p) || p.Length < 8) continue;
                    double[] start = { p[0], p[1], p[2] }, end = { p[3], p[4], p[5] };
                    bool closed = Distance3(start, end) <= 1e-9 || Math.Abs(p[7] - p[6]) >= 2 * Math.PI - 1e-5;
                    if (!closed || !(curve.CircleParams is double[] c) || c.Length < 7) continue;

                    double[] center = TransformModelPoint(new[] { c[0], c[1], c[2] }, e.Component, modelToView, modelToSketch, mathUtility);
                    double[] onRim = TransformModelPoint(start, e.Component, modelToView, modelToSketch, mathUtility);
                    if (!IsUsableSketchPoint(center) || !IsUsableSketchPoint(onRim)) continue;
                    candidates.Add(new MarkRimCandidate
                    {
                        Index = i,
                        Source = e.MarkSource,
                        X = center[0],
                        Y = center[1],
                        R = Distance2D(center, onRim),
                        Depth = ViewDepth(new[] { c[0], c[1], c[2] }, e.Component, modelToView, mathUtility),
                        IsMouth = e.IsMouth,
                    });
                    candidates[candidates.Count - 1].WallKeys.AddRange(e.WallKeys);
                }
                catch (Exception ex)
                {
                    DxfTrace.Debug("MARK rim inspection failed: " + ex.Message);
                }
                finally
                {
                    SafeReleaseComObject(curve);
                }
            }

            HashSet<int> dropped = MarkRimSelector.SelectDropped(candidates);
            if (dropped.Count > 0)
            {
                ErrorEvents.LogDebug(
                    logPrefix + "[LASER_MARK][RIM] " + candidates.Count + " vòng MARK kín, bỏ " + dropped.Count +
                    " vòng trong (lỗ cắt xéo): " + string.Join("; ", candidates.Where(c => dropped.Contains(c.Index))
                        .Select(c => c.Source + " R" + (c.R * 1000).ToString("0.###", CultureInfo.InvariantCulture) +
                                     (c.IsMouth ? " miệng" : " trong lòng"))));
            }
            return dropped;
        }

        /// <summary>
        /// «Một lỗ MARK chỉ khắc MỘT vòng» cho mọi biên dạng KHÔNG phải đường tròn kín (ô chữ nhật, rãnh,
        /// cung, spline của tấm cuộn — đường tròn kín đã do <see cref="SelectDroppedMarkRims"/> lo).
        ///
        /// Ô MARK xuyên tấm cắt xéo (Cut-Extrude thẳng qua tấm cuộn 1,5 mm: lệch ≈ t·tanφ, 0,002–0,05 mm) có
        /// vành mặt GẦN, vành mặt XA và cạnh tường-tường; cả ba đều là cạnh MARK nên trước v150 cùng lên
        /// LASER_MARK → hai đường gần trùng, bước gỡ nét chồng xé góc → ô hở (4119 chi tiết 48/49).
        ///
        /// Mặt «tấm» = mặt phẳng nhìn thẳng HOẶC mặt trụ có trục nằm trong mặt tấm (vùng uốn: trên view trải
        /// phẳng SolidWorks vẫn báo mặt BaseBend là trụ — đo 06/10 m02, xem <see cref="IsEdgeBetweenFacingFaces"/>;
        /// dải cuộn 48/49 là một BaseBend). Cạnh kề mặt tấm và có độ sâu KHÔNG ĐỔI dọc cạnh là vành (miệng lỗ,
        /// đáy hốc); cạnh không kề mặt tấm nào là cạnh trong lòng lỗ (tường-tường); cạnh kề mặt tấm mà độ sâu
        /// thay đổi (vùng uốn thật trên view gập) không xét — giữ nguyên.
        ///
        /// Nhóm cạnh theo LỖ (cùng feature, cùng component, chung một mặt thành): lỗ có ≥ 2 mức sâu thì chỉ
        /// giữ vành ở mức GẦN người nhìn (miệng lỗ — cũng là đường bao ngoài cùng nhìn thấy); vành xa, đáy hốc
        /// và cạnh trong lòng → Erase. Lỗ chỉ có một mức (rãnh khắc nông thành thẳng, ký hiệu ◎) giữ nguyên.
        /// Trả chỉ số (trong <paramref name="edges"/>) các cạnh phải bỏ.
        /// </summary>
        private static HashSet<int> SelectDroppedFarMarkEdges(
            IList<VisibleEdgeReference> edges,
            MathTransform modelToView,
            MathUtility mathUtility,
            double viewScale,
            string logPrefix)
        {
            var dropped = new HashSet<int>();
            if (edges == null || edges.Count < 2 || modelToView == null || mathUtility == null) return dropped;
            double toRealMm = 1000.0 / (viewScale > 1e-9 ? viewScale : 1.0);

            var infos = new List<FarRimEdge>();
            int nonCircular = 0, noWall = 0, curvedRim = 0, failed = 0;
            for (int i = 0; i < edges.Count; i++)
            {
                VisibleEdgeReference e = edges[i];
                try
                {
                    if (e?.Edge == null || IsClosedCircleEdge(e.Edge)) continue;
                    nonCircular++;
                    var info = new FarRimEdge { Index = i, Source = e.MarkSource ?? "", Component = ComponentKey(e.Component) };
                    object[] faces = e.Edge.GetTwoAdjacentFaces2() as object[];
                    if (faces == null) { failed++; continue; }
                    bool sheetLike = false;
                    foreach (IFace2 face in faces.OfType<IFace2>())
                    {
                        if (IsSheetLikeFace(face, e.Component, modelToView, mathUtility))
                        {
                            sheetLike = true;
                            continue;
                        }
                        string key = FaceKey(face);
                        if (key.Length > 0) info.WallKeys.Add(key);
                    }
                    if (info.WallKeys.Count == 0) { noWall++; continue; }   // không thuộc thành lỗ nào: để nguyên
                    if (sheetLike)
                    {
                        if (!TryFlatDepth(e, modelToView, mathUtility, toRealMm, out double depth))
                        {
                            curvedRim++;
                            continue;                                       // vành trên mặt cong thật: giữ
                        }
                        info.OnSheetFace = true;
                        info.DepthMm = depth;
                    }
                    infos.Add(info);
                }
                catch (Exception ex)
                {
                    failed++;
                    DxfTrace.Debug("MARK far-rim inspection failed: " + ex.Message);
                }
            }

            // Nhóm theo lỗ: cùng feature MARK, cùng component, chung một mặt thành.
            var parent = Enumerable.Range(0, infos.Count).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            var owner = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < infos.Count; i++)
            {
                foreach (string key in infos[i].WallKeys)
                {
                    string scoped = infos[i].Source + "\u0001" + infos[i].Component + "\u0001" + key;
                    if (owner.TryGetValue(scoped, out int first)) parent[Find(i)] = Find(first);
                    else owner[scoped] = i;
                }
            }

            var summary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int holes = 0, noLevel = 0, oneLevel = 0, twoLevels = 0;
            foreach (var hole in Enumerable.Range(0, infos.Count).GroupBy(Find))
            {
                holes++;
                List<FarRimEdge> members = hole.Select(k => infos[k]).ToList();
                List<double> depths = members.Where(m => m.OnSheetFace).Select(m => m.DepthMm).ToList();
                if (depths.Count == 0) { noLevel++; continue; }
                double near = depths.Max(), far = depths.Min();
                if (near - far < FarRimMinDepthMm) { oneLevel++; continue; }   // một mức sâu: không có vành xa
                twoLevels++;
                double split = (near + far) / 2;
                foreach (FarRimEdge m in members)
                {
                    if (m.OnSheetFace && m.DepthMm >= split) continue;   // miệng lỗ mặt gần: giữ
                    dropped.Add(m.Index);
                    summary[m.Source] = summary.TryGetValue(m.Source, out int n) ? n + 1 : 1;
                }
            }
            // Luôn ghi khi có cạnh MARK không tròn — bản P0 cần biết luật có chạy trên tấm cuộn thật không.
            if (nonCircular > 0)
            {
                ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][FAR] " + nonCircular + " cạnh MARK không tròn: " +
                                     infos.Count(m => m.OnSheetFace) + " vành, " + infos.Count(m => !m.OnSheetFace) + " trong lòng, " +
                                     curvedRim + " vành mặt cong (giữ), " + noWall + " không có mặt thành, " + failed + " lỗi; " +
                                     holes + " lỗ (" + noLevel + " không mức sâu, " + oneLevel + " một mức, " + twoLevels + " hai mức) → bỏ " +
                                     dropped.Count + (summary.Count > 0 ? ": " + string.Join("; ", summary.Select(p => p.Key + " ×" + p.Value)) : ""));
            }
            return dropped;
        }

        /// <summary>Hai mức sâu cách nhau ít hơn ngần này (mm thật) coi là cùng một mặt tấm.</summary>
        private const double FarRimMinDepthMm = 0.01;

        /// <summary>Độ sâu dọc một vành phẳng thay đổi không quá ngần này (mm thật).</summary>
        private const double FarRimFlatToleranceMm = 0.002;

        private sealed class FarRimEdge
        {
            public int Index;
            public string Source = "";
            public string Component = "";
            public bool OnSheetFace;
            public double DepthMm = double.NaN;
            public readonly List<string> WallKeys = new List<string>();
        }

        private static string ComponentKey(Component2 component)
        {
            if (component == null) return "";
            try { return component.Name2 ?? ""; } catch (COMException) { return "?"; }
        }

        /// <summary>
        /// Mặt «tấm»: mặt phẳng có pháp tuyến song song hướng nhìn (mặt tấm, đáy hốc), hoặc mặt trụ có trục
        /// nằm trong mặt tấm (mặt vùng uốn SolidWorks vẫn báo trên view trải phẳng).
        /// </summary>
        private static bool IsSheetLikeFace(IFace2 face, Component2 component, MathTransform modelToView, MathUtility mathUtility)
        {
            try
            {
                if (!(face.GetSurface() is ISurface surface)) return false;
                if (surface.IsPlane())
                {
                    double? z = ViewZ(face.Normal as double[], component, modelToView, mathUtility);
                    return z != null && Math.Abs(z.Value) >= 0.99;
                }
                if (surface.IsCylinder() && surface.CylinderParams is double[] c && c.Length >= 7)
                {
                    double? z = ViewZ(new[] { c[3], c[4], c[5] }, component, modelToView, mathUtility);
                    return z != null && Math.Abs(z.Value) <= 0.1;
                }
                return false;
            }
            catch (COMException) { return false; }
        }

        /// <summary>
        /// Độ sâu (mm thật, theo hướng nhìn) của một cạnh nằm trọn trong một mặt song song tờ giấy: trung bình
        /// tối đa 9 điểm mẫu; false khi độ sâu thay đổi quá <see cref="FarRimFlatToleranceMm"/> dọc cạnh.
        /// </summary>
        private static bool TryFlatDepth(VisibleEdgeReference e, MathTransform modelToView, MathUtility mathUtility,
            double toRealMm, out double depthMm)
        {
            depthMm = double.NaN;
            List<double[]> samples = SampleEdge3D(e.Edge).ToList();
            if (samples.Count == 0) return false;
            int stride = Math.Max(1, (samples.Count - 1) / 8);
            double min = double.MaxValue, max = double.MinValue, sum = 0;
            int count = 0;
            FastTransform fast = ProjectionFor(e.Component, modelToView, mathUtility);
            for (int k = 0; k < samples.Count; k += stride)
            {
                double depth = fast != null
                    ? fast.Point(samples[k])[2]
                    : ViewDepth(samples[k], e.Component, modelToView, mathUtility);
                if (double.IsNaN(depth)) return false;
                depth *= toRealMm;
                min = Math.Min(min, depth);
                max = Math.Max(max, depth);
                sum += depth;
                count++;
            }
            if (count == 0 || max - min > FarRimFlatToleranceMm) return false;
            depthMm = sum / count;
            return true;
        }

        private static bool IsClosedCircleEdge(IEdge edge)
        {
            ICurve curve = null;
            try
            {
                curve = edge.GetCurve() as ICurve;
                if (curve == null || curve.IsLine() || !curve.IsCircle()) return false;
                if (!(edge.GetCurveParams2() is double[] p) || p.Length < 8) return false;
                double[] start = { p[0], p[1], p[2] }, end = { p[3], p[4], p[5] };
                return Distance3(start, end) <= 1e-9 || Math.Abs(p[7] - p[6]) >= 2 * Math.PI - 1e-5;
            }
            catch (COMException) { return false; }
            finally { SafeReleaseComObject(curve); }
        }

        /// <summary>Toạ độ z trong không gian view (hướng về người nhìn) của một điểm model; NaN nếu không đo được.</summary>
        private static double ViewDepth(double[] point, Component2 component, MathTransform modelToView, MathUtility mathUtility)
        {
            if (point == null || modelToView == null || mathUtility == null) return double.NaN;
            MathPoint mp = null;
            try
            {
                mp = mathUtility.CreatePoint(point) as MathPoint;
                if (mp == null) return double.NaN;
                if (component != null)
                {
                    MathTransform ct = component.Transform2;
                    if (ct != null) mp = mp.MultiplyTransform(ct) as MathPoint;
                    SafeReleaseComObject(ct);
                }
                mp = mp?.MultiplyTransform(modelToView) as MathPoint;
                return mp?.ArrayData is double[] a && a.Length >= 3 ? a[2] : double.NaN;
            }
            catch { return double.NaN; }
            finally { SafeReleaseComObject(mp); }
        }

        // ───────────────────────────── nhận lại bằng hình học ─────────────────────────────

        /// <summary>
        /// Feature MARK chưa có cạnh nào được nhận (theo chủ sở hữu mặt) → so hình học các cạnh của
        /// chính feature (IFeature.GetFaces → cạnh) với các cạnh view CHƯA nhận. Khớp = đường tròn cùng
        /// tâm + bán kính, hoặc đoạn/đường cong cùng hai đầu (0,001 mm). Chỉ cho Part view.
        /// </summary>
        private static int RecoverMarkEdgesByGeometry(
            IView view,
            LaserMarkFeatureRecognizer recognizer,
            List<VisibleEdgeReference> unmarkedEdges,
            List<VisibleEdgeReference> markedEdges,
            HashSet<string> visibleMarkFeatureNames,
            Dictionary<string, int> recognizedDescriptions,
            string logPrefix)
        {
            if (unmarkedEdges == null || unmarkedEdges.Count == 0) return 0;
            int recovered = 0;
            try
            {
                List<(Feature Feature, LaserMarkFeatureRecognizer.Resolution Mark)> hidden =
                    FindHiddenMarkFeatures(view, recognizer, visibleMarkFeatureNames);
                if (hidden.Count == 0) return 0;

                bool partView = view?.ReferencedDocument is PartDoc;
                // Chữ ký các cạnh view chưa nhận (chỉ cạnh của Part — assembly cần ma trận component).
                var bySignature = new Dictionary<string, List<VisibleEdgeReference>>(StringComparer.Ordinal);
                foreach (VisibleEdgeReference v in unmarkedEdges)
                {
                    // A Part view can hand its edges out through the root
                    // drawing component: same Part coordinates.
                    if (v?.Edge == null || (v.Component != null && !partView)) continue;
                    string sig = EdgeSignature(v.Edge);
                    if (sig.Length == 0) continue;
                    if (!bySignature.TryGetValue(sig, out List<VisibleEdgeReference> list))
                        bySignature[sig] = list = new List<VisibleEdgeReference>();
                    list.Add(v);
                }
                // No signature to match: the recut-hole pass below still runs.

                foreach (var (feature, mark) in hidden)
                {
                    int before = recovered;
                    foreach (IFace2 face in (feature.GetFaces() as object[] ?? new object[0]).OfType<IFace2>())
                    {
                        foreach (IEdge edge in (face.GetEdges() as object[] ?? new object[0]).OfType<IEdge>())
                        {
                            string sig = EdgeSignature(edge);
                            if (sig.Length == 0 || !bySignature.TryGetValue(sig, out List<VisibleEdgeReference> hits)) continue;
                            foreach (VisibleEdgeReference hit in hits)
                            {
                                if (markedEdges.Contains(hit)) continue;
                                DescribeMarkFaces(hit.Edge, recognizer, mark.SourceName, hit);
                                // Mặt kề có chủ khác (đó là lý do không nhận được) → hỏi mặt của CHÍNH
                                // feature: thành cong của nó là khoá «cùng một lỗ».
                                if (hit.WallKeys.Count == 0 && !IsPlanarFace(face))
                                {
                                    string key = FaceKey(face);
                                    if (key.Length > 0) hit.WallKeys.Add(key);
                                }
                                markedEdges.Add(hit);
                                recovered++;
                            }
                            bySignature.Remove(sig);
                        }
                    }
                    if (recovered > before)
                    {
                        visibleMarkFeatureNames.Add(mark.SourceName);
                        visibleMarkFeatureNames.Add(mark.MarkedName);
                        recognizedDescriptions[mark.Description] =
                            (recognizedDescriptions.TryGetValue(mark.Description, out int n) ? n : 0) + recovered - before;
                        ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][GEOM] " + mark.Description + ": nhận lại " +
                                             (recovered - before) + " cạnh bằng đối chiếu hình học.");
                    }
                }
                unmarkedEdges.RemoveAll(markedEdges.Contains);

                // Still hidden: the MARK hole may have been cut again by an
                // ordinary feature in the flat configuration (rolled sheets).
                foreach (var (feature, mark) in hidden)
                {
                    if (visibleMarkFeatureNames.Contains(mark.SourceName)) continue;
                    int found = RecoverMarkHolesRecutInFlat(
                        view, recognizer, feature, mark, unmarkedEdges, markedEdges, logPrefix);
                    if (found <= 0) continue;
                    recovered += found;
                    visibleMarkFeatureNames.Add(mark.SourceName);
                    visibleMarkFeatureNames.Add(mark.MarkedName);
                    recognizedDescriptions[mark.Description] =
                        (recognizedDescriptions.TryGetValue(mark.Description, out int n) ? n : 0) + found;
                }
                unmarkedEdges.RemoveAll(markedEdges.Contains);
            }
            catch (Exception ex)
            {
                ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][GEOM] lỗi đối chiếu hình học: " + ex.Message);
            }
            return recovered;
        }

        private const double HoleRadiusToleranceMeters = 2e-5;   // 0.02 mm

        /// <summary>
        /// Rolled sheet: the MARK feature cuts a round hole in the FOLDED body,
        /// and the flat configuration cuts the same hole again with an ordinary
        /// feature (so it is perfectly round once flat). On the FlatPattern
        /// view the hole wall then belongs to that feature and the MARK feature
        /// has no face at all - the hole stayed on CUT and was cut through.
        /// The hole is matched by RADIUS, only when it is unambiguous: in the
        /// open (folded) configuration every hole of that radius belongs to a
        /// MARK feature, and the view shows exactly as many still-unmarked
        /// holes of that radius as there are MARK holes. Returns edges marked.
        /// </summary>
        private static int RecoverMarkHolesRecutInFlat(
            IView view,
            LaserMarkFeatureRecognizer recognizer,
            Feature feature,
            LaserMarkFeatureRecognizer.Resolution mark,
            List<VisibleEdgeReference> unmarkedEdges,
            List<VisibleEdgeReference> markedEdges,
            string logPrefix)
        {
            // Borrowed RCW shared with SolidWorks: never release it here.
            if (!(view?.ReferencedDocument is PartDoc part)) return 0;

            // Radii of the MARK feature's holes in the open configuration
            // (closed edge loops of its curved faces).
            List<double> markRadii = HoleRadiiOfFaces(
                (feature.GetFaces() as object[] ?? new object[0]).OfType<IFace2>());
            ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][RECUT] " + mark.Description + ": bán kính lỗ MARK ở cấu hình đang mở = " +
                                 (markRadii.Count == 0 ? "—" : string.Join(", ", markRadii.Select(r => (r * 1000).ToString("0.###", CultureInfo.InvariantCulture)))) + " mm");
            if (markRadii.Count == 0) return 0;

            int marked = 0;
            foreach (IGrouping<long, double> group in markRadii.GroupBy(r => (long)Math.Round(r / HoleRadiusToleranceMeters)))
            {
                double radius = group.Average();
                int markHoles = group.Count();

                // Any ordinary hole of the same radius in the open configuration
                // makes the match ambiguous.
                int ordinary = 0;
                foreach (object bodyObject in part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[] ?? new object[0])
                {
                    foreach (IFace2 face in ((bodyObject as Body2)?.GetFaces() as object[] ?? new object[0]).OfType<IFace2>())
                    {
                        if (IsPlanarFace(face)) continue;
                        if (!HoleRadiiOfFaces(new[] { face }).Any(r => Math.Abs(r - radius) <= HoleRadiusToleranceMeters)) continue;
                        Feature owner = null;
                        try { owner = face.GetFeature() as Feature; } catch { }
                        if (owner == null || !recognizer.TryResolve(owner, out _)) ordinary++;
                    }
                }

                // Unmarked round holes of that radius on the view (a Part view:
                // root-component edges are in Part coordinates too), grouped by
                // centre: a through hole shows both rims.
                var holes = new List<(double[] Centre, List<VisibleEdgeReference> Edges)>();
                foreach (VisibleEdgeReference v in unmarkedEdges)
                {
                    if (v?.Edge == null || markedEdges.Contains(v)) continue;
                    ICurve curve = null;
                    try
                    {
                        curve = v.Edge.GetCurve() as ICurve;
                        if (curve == null || curve.IsLine() || !curve.IsCircle() ||
                            !(curve.CircleParams is double[] c) || c.Length < 7) continue;
                        if (Math.Abs(c[6] - radius) > HoleRadiusToleranceMeters) continue;
                        var hole = holes.FirstOrDefault(h =>
                            Math.Sqrt(Math.Pow(h.Centre[0] - c[0], 2) + Math.Pow(h.Centre[1] - c[1], 2) +
                                      Math.Pow(h.Centre[2] - c[2], 2)) < radius);
                        if (hole.Edges == null)
                            holes.Add((new[] { c[0], c[1], c[2] }, new List<VisibleEdgeReference> { v }));
                        else
                            hole.Edges.Add(v);
                    }
                    catch { }
                    finally { SafeReleaseComObject(curve); }
                }

                if (holes.Count != markHoles)
                {
                    // Which round edges ARE on the view (radius mm / component?).
                    var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (VisibleEdgeReference v in unmarkedEdges)
                    {
                        ICurve curve = null;
                        try
                        {
                            curve = v?.Edge?.GetCurve() as ICurve;
                            if (curve == null || curve.IsLine() || !curve.IsCircle() ||
                                !(curve.CircleParams is double[] c) || c.Length < 7) continue;
                            string key = (c[6] * 1000).ToString("0.###", CultureInfo.InvariantCulture) +
                                         (v.Component == null ? "" : "(comp)");
                            seen[key] = (seen.TryGetValue(key, out int k) ? k : 0) + 1;
                        }
                        catch { }
                        finally { SafeReleaseComObject(curve); }
                    }
                    ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][RECUT] cạnh tròn chưa nhận trên view (r mm ×n): " +
                                         string.Join(", ", seen.OrderBy(x => x.Key).Select(x => x.Key + "×" + x.Value)));
                }

                string text = mark.Description + ": lỗ Ø" + (radius * 2000).ToString("0.##", CultureInfo.InvariantCulture) +
                              " — MARK " + markHoles + " lỗ, lỗ thường cùng Ø ở cấu hình gập " + ordinary +
                              ", lỗ cùng Ø còn ở CUT trên view " + holes.Count;
                if (ordinary > 0 || holes.Count != markHoles)
                {
                    ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][RECUT] " + text + " → không nhận (không chắc chắn).");
                    continue;
                }

                foreach (var hole in holes)
                {
                    foreach (VisibleEdgeReference hit in hole.Edges)
                    {
                        DescribeMarkFaces(hit.Edge, recognizer, mark.SourceName, hit);
                        // The rims of one hole share the key of its wall.
                        if (hit.WallKeys.Count == 0)
                        {
                            foreach (IFace2 face in (hit.Edge.GetTwoAdjacentFaces2() as object[] ?? new object[0]).OfType<IFace2>())
                            {
                                if (IsPlanarFace(face)) continue;
                                string key = FaceKey(face);
                                if (key.Length > 0) hit.WallKeys.Add(key);
                            }
                        }
                        markedEdges.Add(hit);
                        marked++;
                    }
                }
                ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][RECUT] " + text + " → nhận " + holes.Count +
                                     " lỗ (bị feature khác cắt lại ở cấu hình trải phẳng).");
            }
            return marked;
        }

        /// <summary>
        /// Radius (m) of each curved face seen as a hole wall: the longest
        /// edge LOOP of the face (a rim may be split into several edges on a
        /// rolled sheet) divided by 2π.
        /// </summary>
        private static List<double> HoleRadiiOfFaces(IEnumerable<IFace2> faces)
        {
            var radii = new List<double>();
            foreach (IFace2 face in faces)
            {
                if (face == null || IsPlanarFace(face)) continue;
                double best = 0;
                foreach (ILoop2 loop in (face.GetLoops() as object[] ?? new object[0]).OfType<ILoop2>())
                {
                    double length = 0;
                    foreach (IEdge edge in (loop.GetEdges() as object[] ?? new object[0]).OfType<IEdge>())
                        length += EdgeLength(edge);
                    if (length > best) best = length;
                }
                if (best > 0) radii.Add(best / (2 * Math.PI));
            }
            return radii;
        }

        private static double EdgeLength(IEdge edge)
        {
            ICurve curve = null;
            try
            {
                curve = edge.GetCurve() as ICurve;
                if (curve == null || !(edge.GetCurveParams2() is double[] p) || p.Length < 8) return 0;
                return curve.GetLength3(p[6], p[7]);
            }
            catch { return 0; }
            finally { SafeReleaseComObject(curve); }
        }

        /// <summary>
        /// Chữ ký hình học của một cạnh (mét, làm tròn 1e-6 = 0,001 mm): đường tròn = tâm + bán kính
        /// (+ hai đầu nếu là cung), đường khác = loại + hai đầu không phân biệt chiều.
        /// </summary>
        private static string EdgeSignature(IEdge edge)
        {
            ICurve curve = null;
            try
            {
                curve = edge.GetCurve() as ICurve;
                if (curve == null || !(edge.GetCurveParams2() is double[] p) || p.Length < 6) return "";
                string R(double v) => Math.Round(v * 1e6).ToString(CultureInfo.InvariantCulture);
                string a = R(p[0]) + "," + R(p[1]) + "," + R(p[2]);
                string b = R(p[3]) + "," + R(p[4]) + "," + R(p[5]);
                string ends = string.CompareOrdinal(a, b) <= 0 ? a + ";" + b : b + ";" + a;
                if (!curve.IsLine() && curve.IsCircle() && curve.CircleParams is double[] c && c.Length >= 7)
                {
                    bool closed = a == b;
                    return "C|" + R(c[0]) + "," + R(c[1]) + "," + R(c[2]) + "|" + R(c[6]) + (closed ? "" : "|" + ends);
                }
                return (curve.IsLine() ? "L|" : "U|") + ends;
            }
            catch { return ""; }
            finally { SafeReleaseComObject(curve); }
        }

        /// <summary>Feature MARK (không suppress ở cấu hình của view) chưa có cạnh nào trên view.</summary>
        private static List<(Feature Feature, LaserMarkFeatureRecognizer.Resolution Mark)> FindHiddenMarkFeatures(
            IView view,
            LaserMarkFeatureRecognizer recognizer,
            ICollection<string> visibleMarkFeatureNames)
        {
            var result = new List<(Feature, LaserMarkFeatureRecognizer.Resolution)>();
            // Borrowed RCW shared with SolidWorks: never release it here.
            ModelDoc2 part = view?.ReferencedDocument as ModelDoc2;
            if (part == null || part.GetType() != (int)swDocumentTypes_e.swDocPART) return result;

            string configuration = view.ReferencedConfiguration ?? "";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (Feature feature = part.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
            {
                // Only the feature itself is checked here (no owner walk):
                // sub-features/sketches are not geometry owners.
                string name = feature.Name ?? "";
                if (visibleMarkFeatureNames.Contains(name) || !seen.Add(name)) continue;
                if (!recognizer.TryResolve(feature, out LaserMarkFeatureRecognizer.Resolution mark)) continue;
                if (!string.Equals(mark.SourceName, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsFeatureSuppressedIn(feature, configuration)) continue;
                result.Add((feature, mark));
            }
            return result;
        }

        /// <summary>
        /// Chẩn đoán cho feature MARK vẫn không có cạnh trên view: số mặt của feature ở cấu hình đang mở,
        /// và chủ sở hữu mặt của các cạnh TRÒN chưa nhận trên view (cạnh lỗ thường là tròn). Ghi Debug log
        /// để lần sau biết chính xác vì sao.
        /// </summary>
        private static string DescribeHiddenMarkDiagnostics(
            List<(Feature Feature, LaserMarkFeatureRecognizer.Resolution Mark)> hidden,
            IEnumerable<VisibleEdgeReference> unmarkedEdges)
        {
            var parts = new List<string>();
            foreach (var (feature, mark) in hidden)
            {
                int faces = 0;
                try { faces = (feature.GetFaces() as object[])?.Length ?? 0; } catch { }
                parts.Add(mark.SourceName + ": " + faces + " mặt ở cấu hình đang mở");
            }

            var owners = new Dictionary<string, int>(StringComparer.Ordinal);
            int circles = 0;
            foreach (VisibleEdgeReference v in unmarkedEdges ?? Enumerable.Empty<VisibleEdgeReference>())
            {
                ICurve curve = null;
                try
                {
                    curve = v?.Edge?.GetCurve() as ICurve;
                    if (curve == null || curve.IsLine() || !curve.IsCircle()) continue;
                    circles++;
                    foreach (IFace2 face in (v.Edge.GetTwoAdjacentFaces2() as object[] ?? new object[0]).OfType<IFace2>())
                    {
                        string owner = LaserMarkFeatureRecognizer.OwnerName(face);
                        owners[owner] = (owners.TryGetValue(owner, out int n) ? n : 0) + 1;
                    }
                }
                catch { }
                finally { SafeReleaseComObject(curve); }
            }
            parts.Add(circles + " cạnh tròn chưa nhận trên view, chủ mặt kề: " +
                      (owners.Count == 0 ? "—" : string.Join(", ", owners.OrderByDescending(p => p.Value).Take(10)
                          .Select(p => p.Key + " ×" + p.Value))));
            return string.Join(" · ", parts);
        }
    }
}
