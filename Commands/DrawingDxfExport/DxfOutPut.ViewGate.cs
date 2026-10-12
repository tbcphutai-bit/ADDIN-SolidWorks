// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// «Cổng xác nhận view» (NGHIEN-CUU-XUAT-DXF.md mục 2) — giai đoạn P0 (v150): CHỈ GHI LOG, chưa chặn.
    ///
    /// DXF phải là ảnh của Drawing View đang hiển thị (cùng mặt, cùng góc, tỉ lệ 1:1, chỉ tịnh tiến về gốc).
    /// Phép kiểm KHÔNG tin ma trận ModelToViewTransform: so DXF cuối với các nét SolidWorks đang vẽ của
    /// chính view gốc (IView.GetPolylines7 — «view space», theo tài liệu chỉ khác toạ độ tờ giấy bởi vị trí
    /// và tỉ lệ, không có phép xoay). Thử 8 phương án (4 góc × lật gương), chấm hai chiều, dung sai mm thật.
    ///
    /// P0 ghi thêm số liệu để trả lời các câu hỏi chỉ SolidWorks trả lời được: khung toạ độ của
    /// GetPolylines7 (hệ số k), frame view gốc so với frame bản sao, thời gian đọc.
    /// </summary>
    internal partial class DxfOutPut
    {
        /// <summary>Ảnh tham chiếu đọc trên VIEW GỐC (sau lần dựng lại cuối).</summary>
        private sealed class ViewIdentityReference
        {
            public ViewFrame Frame;
            public List<List<double[]>> Paths = new List<List<double[]>>();
            public double ViewScale = double.NaN;
            public double[] Xform;
            public double[] Outline;
            public string Diagnostics = "";
            public long Milliseconds;
        }

        /// <summary>Kết quả so DXF với ảnh tham chiếu.</summary>
        internal sealed class ViewIdentityVerdict
        {
            /// <summary>"KHỚP", "LỆCH" hoặc "KHÔNG KẾT LUẬN".</summary>
            public string Status = "KHÔNG KẾT LUẬN";
            public bool Mismatch;
            public double[] Scores = new double[8];
            public int Best;
            public double Coverage = double.NaN;
            /// <summary>Hệ số ảnh tham chiếu → mm thật (đo bằng khung bao).</summary>
            public double Scale = double.NaN;
            public double P50 = double.NaN, P95 = double.NaN, Max = double.NaN;
            public int DxfSamples;
            public int ReferenceSamples;
            /// <summary>Khung toạ độ của ảnh tham chiếu suy từ k và tỉ lệ view: "giấy", "model" hoặc "chưa rõ".</summary>
            public string Frame = "chưa rõ";
            public bool TimedOut;
            /// <summary>Khác rỗng khi phương án đúng hướng chỉ khớp sau khi căn theo mép thay vì tâm khung.</summary>
            public string AlignedBy = "";
            public long Milliseconds;
            /// <summary>Số nét DXF khớp theo từng phương án (0 = đúng hướng).</summary>
            public int[] EntityHits = new int[8];
            public int EntityCount;
            public int CutEntityCount;
            public string Message = "";
        }

        /// <summary>Dung sai (mm thật) điểm DXF ↔ nét view. P0: nới để đo; P1 siết theo số liệu log.</summary>
        internal const double ViewIdentityToleranceMm = 0.1;
        /// <summary>Một nét DXF «khớp» khi ≥ ngần này phần điểm của nó nằm trên nét view.</summary>
        private const double ViewIdentityEntityShare = 0.95;

        /// <summary>Thời gian tối đa của cổng (chạy trên luồng SolidWorks — tấm đục 5 000 lỗ từng mất 10 phút).</summary>
        internal const long ViewIdentityBudgetMs = 1500;

        /// <summary>Số điểm mẫu tối đa trên mỗi nét DXF (đều theo chiều dài).</summary>
        private const int ViewIdentitySamplesPerEntity = 32;

        /// <summary>Ô lưới tra khoảng cách (mm) — dung sai 0,1 mm nên ô nhỏ là đủ, nét view chia ≤ 2 ô.</summary>
        private const double ViewIdentityGridCellMm = 1.0;

        private static ViewIdentityReference ReadViewIdentityReference(ModelDoc2 drawingModel, IView view)
        {
            var reference = new ViewIdentityReference();
            var clock = Stopwatch.StartNew();
            var notes = new List<string>();
            try
            {
                reference.Frame = ViewFrame.Read(view);
                try { reference.ViewScale = view.ScaleDecimal; } catch (COMException) { }
                try { reference.Xform = view.GetXform() as double[]; } catch (COMException) { }
                try { reference.Outline = view.GetOutline() as double[]; } catch (COMException) { }

                reference.Paths = ReadViewPolylinePaths(view, notes, out string failure);
                if (reference.Paths == null)
                {
                    // Nét ngoài vùng đang phóng có thể chưa được dựng (xem SectionBlockMarks). Chỉ khi đó mới
                    // phóng vừa màn hình — đổi zoom bản vẽ của người dùng (phản biện 08/10).
                    notes.Add("lần 1: " + failure + " → ViewZoomtofit2");
                    try { (drawingModel as IModelDoc2)?.ViewZoomtofit2(); } catch (Exception) { }
                    reference.Paths = ReadViewPolylinePaths(view, notes, out failure);
                }
                if (reference.Paths == null)
                {
                    notes.Add("polyline: " + failure);
                    reference.Paths = new List<List<double[]>>();
                }
                double[] box = Box(reference.Paths.SelectMany(path => path));
                notes.Add(reference.Paths.Count + " nét, khung thô " + F6(box[2] - box[0]) + "×" + F6(box[3] - box[1]) +
                          " tại (" + F6(box[0]) + ", " + F6(box[1]) + ")");
            }
            catch (Exception ex)
            {
                notes.Add("lỗi: " + ex.Message);
            }
            reference.Milliseconds = clock.ElapsedMilliseconds;
            reference.Diagnostics = string.Join("; ", notes);
            return reference;
        }

        private static List<List<double[]>> ReadViewPolylinePaths(IView view, List<string> notes, out string failure)
        {
            object raw = null;
            try { view.GetPolylines7(1, out raw); }
            catch (Exception ex) { notes.Add("GetPolylines7: " + ex.Message); }
            List<List<double[]>> paths = ParseViewPolylinePaths(ToPolylineData(raw), out failure);
            if (paths != null && paths.Count == 0)
            {
                failure = "view không trả về polyline nào";
                return null;
            }
            return paths;
        }

        /// <summary>
        /// Như <see cref="ParseViewPolylines"/> nhưng giữ từng nét riêng (x, y); cung/tròn (type 1) dựng lại
        /// từ tâm–bán kính khi đọc được, không thì dùng các điểm trả về.
        /// </summary>
        internal static List<List<double[]>> ParseViewPolylinePaths(double[] data, out string failure)
        {
            failure = "";
            if (data == null || data.Length == 0)
            {
                failure = "view không trả về polyline nào";
                return null;
            }
            var paths = new List<List<double[]>>();
            int index = 0;
            while (index < data.Length)
            {
                int itemStart = index;
                if (!TryReadPolylineCount(data, ref index, out int type) || type < 0 || type > 100 ||
                    !TryReadPolylineCount(data, ref index, out int geometrySize) || geometrySize < 0 ||
                    (long)index + geometrySize > data.Length)
                {
                    failure = "dữ liệu polyline sai định dạng ở vị trí " + itemStart;
                    return null;
                }
                double[] geometryData = null;
                if (geometrySize > 0)
                {
                    geometryData = new double[geometrySize];
                    Array.Copy(data, index, geometryData, 0, geometrySize);
                }
                index += geometrySize;
                index += 6;   // LineColor, LineStyle, LineFont, LineWeight, LayerID, LayerOverride
                if (index >= data.Length ||
                    !TryReadPolylineCount(data, ref index, out int pointCount) || pointCount < 0 ||
                    (long)index + 3L * pointCount > data.Length)
                {
                    failure = "dữ liệu polyline sai định dạng ở vị trí " + itemStart;
                    return null;
                }
                var points = new List<double[]>(pointCount);
                for (int item = 0; item < pointCount; item++, index += 3)
                {
                    double x = data[index], y = data[index + 1], z = data[index + 2];
                    if (IsFiniteValue(x) && IsFiniteValue(y) && IsFiniteValue(z))
                        points.Add(new[] { x, y, z });
                }

                List<double[]> path = null;
                if (type == 1)
                {
                    ViewPolylineArc arc = BuildPolylineArc(geometryData, points);
                    if (arc != null && arc.Radius > 0)
                    {
                        double sweep = Math.Abs(arc.Sweep) > 1e-9 ? arc.Sweep : 2 * Math.PI;
                        // Bước 0,01 rad: mũi tên = R·1,25e-5 (R 100 mm → 0,00125 mm), không phụ thuộc đơn vị.
                        int count = Math.Max(16, Math.Min(2048, (int)Math.Ceiling(Math.Abs(sweep) / 0.01)));
                        path = new List<double[]>(count + 1);
                        for (int i = 0; i <= count; i++) path.Add(arc.At(sweep * i / count));
                    }
                }
                if (path == null) path = points;
                if (path.Count >= 1) paths.Add(path.Select(p => new[] { p[0], p[1] }).ToList());
            }
            return paths;
        }

        /// <summary>
        /// So DXF cuối (mm thật) với ảnh tham chiếu (đơn vị bất kỳ — hệ số đo bằng khung bao). Không cần
        /// SolidWorks: chạy được ngoại tuyến (tools\DxfMarkOfflineTests).
        /// </summary>
        internal static ViewIdentityVerdict EvaluateViewIdentity(
            List<List<double[]>> referencePaths,
            string dxfPath,
            DxfManufacturingLayerSettings settings,
            double viewScale = double.NaN)
        {
            var verdict = new ViewIdentityVerdict();
            var clock = Stopwatch.StartNew();
            try { return EvaluateViewIdentityCore(referencePaths, dxfPath, settings, viewScale, verdict, clock); }
            finally { verdict.Milliseconds = clock.ElapsedMilliseconds; }
        }

        private static ViewIdentityVerdict EvaluateViewIdentityCore(
            List<List<double[]>> referencePaths,
            string dxfPath,
            DxfManufacturingLayerSettings settings,
            double viewScale,
            ViewIdentityVerdict verdict,
            Stopwatch clock)
        {
            settings = settings ?? DxfManufacturingLayerSettings.CreateDefault();
            if (referencePaths == null || referencePaths.Count == 0)
            {
                verdict.Message = "không có ảnh tham chiếu của view gốc";
                return verdict;
            }
            if (!ReadDxfPaths(dxfPath, settings.Cut.Name, out _, out List<List<double[]>> cut) ||
                !ReadDxfPaths(dxfPath, settings.LaserMark.Name, out _, out List<List<double[]>> mark))
            {
                verdict.Message = "không đọc được DXF";
                return verdict;
            }
            if (cut.Count == 0)
            {
                verdict.Message = "DXF không có nét CUT";
                return verdict;
            }

            // Chuẩn hoá: tâm khung bao CUT ↔ tâm khung bao ảnh (biên ngoài là phần cực trị của cả hai);
            // hệ số k theo tổng hai cạnh khung (bất biến khi xoay 90°).
            double[] cutBox = Box(cut.SelectMany(path => path));
            double[] refBox = Box(referencePaths.SelectMany(path => path));
            double cutSum = (cutBox[2] - cutBox[0]) + (cutBox[3] - cutBox[1]);
            double refSum = (refBox[2] - refBox[0]) + (refBox[3] - refBox[1]);
            if (cutSum <= 1e-9 || refSum <= 1e-12)
            {
                verdict.Message = "khung bao suy biến";
                return verdict;
            }
            double k = cutSum / refSum;
            verdict.Scale = k;
            // GetPolylines7: «view space» = mét giấy (k = 1000/tỉ lệ view) hay mét model (k = 1000)? Câu hỏi §7.1.
            bool paperFrame = viewScale > 0 && Math.Abs(k * viewScale / 1000.0 - 1) <= 0.01;
            bool modelFrame = Math.Abs(k / 1000.0 - 1) <= 0.01;
            verdict.Frame = paperFrame && modelFrame ? "giấy = model (1:1)" : paperFrame ? "giấy" : modelFrame ? "model" : "chưa rõ";
            // Khung nhận ra thì dùng hệ số ĐÚNG (nét view thừa ngoài biên CUT làm lệch khung bao, không làm lệch k).
            if (paperFrame) k = 1000.0 / viewScale;
            else if (modelFrame) k = 1000.0;
            double cgx = (cutBox[0] + cutBox[2]) / 2, cgy = (cutBox[1] + cutBox[3]) / 2;
            double crx = (refBox[0] + refBox[2]) / 2, cry = (refBox[1] + refBox[3]) / 2;

            // Ảnh về mm, bỏ điểm dày hơn 0,25 mm (cung 0,01 rad: Ø6 có 629 điểm — mũi tên sau khi bỏ ≤ 0,002 mm),
            // chia đoạn dài ≤ 2 mm để mỗi đoạn chỉ nằm trong vài ô lưới 1 mm.
            List<List<double[]>> refMm = referencePaths
                .Select(path => Thin(path.Select(p => new[] { (p[0] - crx) * k, (p[1] - cry) * k }).ToList(), 0.25))
                .ToList();
            List<List<double[]>> refGridPaths = refMm.Select(path => SplitLong(path, 2.0)).ToList();
            double half = Math.Max(cutBox[2] - cutBox[0], cutBox[3] - cutBox[1]);
            var refGrid = new SegmentGrid(refGridPaths, new[] { -half, -half, half, half }, ViewIdentityGridCellMm);

            // Mỗi NÉT DXF (CUT trước, rồi LASER_MARK) lấy mẫu riêng: chấm theo nét để lỗ nhỏ lệch chỗ không bị
            // biên ngoài dài hàng mét «nhấn chìm» (xoay 180° một tấm chữ nhật chỉ đổi chỗ vài lỗ).
            List<List<double[]>> dxfMm = cut.Concat(mark)
                .Select(path => path.Select(p => new[] { p[0] - cgx, p[1] - cgy }).ToList())
                .ToList();
            List<List<double[]>> entitySamples = dxfMm.Select(path => EvenSamples(path, ViewIdentitySamplesPerEntity)).ToList();
            int cutCount = cut.Count;
            verdict.DxfSamples = entitySamples.Sum(list => list.Count);
            if (verdict.DxfSamples < 3)
            {
                verdict.Message = "quá ít điểm DXF";
                return verdict;
            }

            // Mỗi phương án thử căn khung (tâm trước; nếu chưa khớp đủ CUT thì căn mép trái/phải × dưới/trên):
            // nét view thừa ngoài biên CUT (cạnh tách, gờ) làm lệch tâm khung bao (phản biện 08/10).
            double refHalfW = (refBox[2] - refBox[0]) * k / 2, refHalfH = (refBox[3] - refBox[1]) * k / 2;
            List<double[]> cutSamplePoints = entitySamples.Take(cutCount).SelectMany(list => list).ToList();
            var entityHits = new int[8];
            var cutHits = new int[8];
            var offsets = new double[8][];
            for (int c = 0; c < 8; c++)
            {
                double[] box = Box(cutSamplePoints.Select(point => Candidate(point, c)));
                var xs = new[] { 0.0, -refHalfW - box[0], refHalfW - box[2] };
                var ys = new[] { 0.0, -refHalfH - box[1], refHalfH - box[3] };
                int bestEntities = -1, bestCut = -1, bestPoints = 0, points = 0;
                foreach (double ox in xs)
                    foreach (double oy in ys)
                    {
                        if (clock.ElapsedMilliseconds > ViewIdentityBudgetMs)
                        {
                            verdict.TimedOut = true;
                            verdict.Message = "hết thời gian " + ViewIdentityBudgetMs + " ms ở phương án " + c +
                                              " (" + entitySamples.Count + " nét DXF, " + refMm.Sum(path => path.Count) + " điểm view)";
                            return verdict;
                        }
                        ScoreCandidate(entitySamples, cutCount, refGrid, c, ox, oy, out int entities, out int cuts, out int hits, out points);
                        if (cuts > bestCut || (cuts == bestCut && entities > bestEntities))
                        {
                            bestCut = cuts; bestEntities = entities; bestPoints = hits; offsets[c] = new[] { ox, oy };
                        }
                        if (bestCut == cutCount) break;
                    }
                entityHits[c] = bestEntities;
                cutHits[c] = bestCut;
                verdict.Scores[c] = points == 0 ? 0 : bestPoints / (double)points;
                if (bestCut == cutCount && offsets[c] != null && (offsets[c][0] != 0 || offsets[c][1] != 0) && c == 0)
                    verdict.AlignedBy = "căn mép (" + F4(offsets[c][0]) + ", " + F4(offsets[c][1]) + ") mm";
            }
            var identityDistances = new List<double>();
            foreach (List<double[]> samples in entitySamples)
                foreach (double[] point in samples)
                    identityDistances.Add(refGrid.Distance(point[0] + offsets[0][0], point[1] + offsets[0][1]));
            identityDistances.Sort();
            verdict.P50 = identityDistances[identityDistances.Count / 2];
            verdict.P95 = identityDistances[Math.Min(identityDistances.Count - 1, (int)(identityDistances.Count * 0.95))];
            verdict.Max = identityDistances[identityDistances.Count - 1];
            verdict.EntityHits = entityHits;
            verdict.EntityCount = entitySamples.Count;
            verdict.CutEntityCount = cutCount;

            // Chiều ngược lại: nét view có trong DXF (thiếu nét / bỏ nhầm). DXF cố ý bỏ một số cạnh view
            // (mặt tách, vành MARK xa, lỗ trong vòng MARK) nên chỉ ghi số, không dùng để kết luận ở P0.
            List<double[]> refSamples = Densify(refMm, SampleSpacing(refMm));
            if (refSamples.Count > 20000)
            {
                int step = (int)Math.Ceiling(refSamples.Count / 20000.0);
                refSamples = refSamples.Where((p, i) => i % step == 0).ToList();
            }
            verdict.ReferenceSamples = refSamples.Count;
            if (refSamples.Count > 0 && clock.ElapsedMilliseconds <= ViewIdentityBudgetMs)
            {
                var dxfGrid = new SegmentGrid(dxfMm.Select(path => SplitLong(path, 2.0)).ToList(),
                    new[] { -half, -half, half, half }, ViewIdentityGridCellMm);
                verdict.Coverage = refSamples.Count(p => dxfGrid.Distance(p[0], p[1]) <= ViewIdentityToleranceMm) / (double)refSamples.Count;
            }

            // KHỚP: đúng hướng thì MỌI nét CUT nằm trên nét view và không phương án nào khớp nhiều nét hơn
            // (chi tiết đối xứng: nhiều phương án bằng nhau — hình giống hệt nên vẫn khớp). Nét LASER_MARK do
            // người dùng vẽ không có trong ảnh view nên không bắt buộc khớp.
            // LỆCH: một phương án khác khớp nhiều nét CUT hơn, hoặc khớp đủ CUT và nhiều nét hơn đúng hướng.
            int bestOther = Enumerable.Range(1, 7)
                .OrderByDescending(c => entityHits[c]).ThenByDescending(c => cutHits[c]).ThenBy(c => c).First();
            verdict.Best = entityHits[bestOther] > entityHits[0] ? bestOther : 0;
            bool identityAllCut = cutHits[0] == cutCount;
            if (identityAllCut && entityHits[0] >= entityHits[bestOther])
            {
                verdict.Status = "KHỚP";
                verdict.Message = "DXF đúng hướng Drawing View gốc";
            }
            else if (cutHits[bestOther] > cutHits[0] ||
                     (cutHits[bestOther] == cutCount && entityHits[bestOther] > entityHits[0]))
            {
                verdict.Status = "LỆCH";
                verdict.Mismatch = true;
                verdict.Best = bestOther;
                verdict.Message = "DXF bị " + DescribeCandidate(bestOther) + " so với Drawing View gốc";
            }
            else
            {
                verdict.Message = "chưa đủ nét khớp để kết luận";
            }
            verdict.Message += " (nét khớp đúng hướng " + entityHits[0] + "/" + entitySamples.Count +
                               " [CUT " + cutHits[0] + "/" + cutCount + "]" +
                               ", tốt nhất khác " + DescribeCandidate(bestOther) + " " + entityHits[bestOther] + " [CUT " + cutHits[bestOther] + "]" +
                               "; điểm khớp " + Pct(verdict.Scores[0]) +
                               "; lệch điểm p50/p95/max " + F4(verdict.P50) + "/" + F4(verdict.P95) + "/" + F4(verdict.Max) + " mm" +
                               "; nét view có trong DXF " + (double.IsNaN(verdict.Coverage) ? "?" : Pct(verdict.Coverage)) +
                               "; k=" + k.ToString("G6", CultureInfo.InvariantCulture) + " (khung " + verdict.Frame + ")" +
                               (verdict.AlignedBy.Length > 0 ? "; " + verdict.AlignedBy : "") +
                               "; " + verdict.DxfSamples + "/" + verdict.ReferenceSamples + " điểm)";
            return verdict;
        }

        private static void ScoreCandidate(List<List<double[]>> entitySamples, int cutCount, SegmentGrid refGrid,
            int candidate, double ox, double oy, out int entityHits, out int cutHits, out int pointHits, out int points)
        {
            entityHits = cutHits = pointHits = points = 0;
            for (int e = 0; e < entitySamples.Count; e++)
            {
                int near = 0;
                foreach (double[] p in entitySamples[e])
                {
                    double[] q = Candidate(p, candidate);
                    if (refGrid.Distance(q[0] + ox, q[1] + oy) <= ViewIdentityToleranceMm) near++;
                }
                points += entitySamples[e].Count;
                pointHits += near;
                if (entitySamples[e].Count > 0 && near >= ViewIdentityEntityShare * entitySamples[e].Count)
                {
                    entityHits++;
                    if (e < cutCount) cutHits++;
                }
            }
        }

        /// <summary>Bỏ điểm cách điểm giữ trước &lt; <paramref name="minStep"/> (giữ điểm cuối).</summary>
        private static List<double[]> Thin(List<double[]> path, double minStep)
        {
            if (path.Count <= 2) return path;
            var kept = new List<double[]> { path[0] };
            double min2 = minStep * minStep;
            for (int i = 1; i < path.Count - 1; i++)
            {
                double[] last = kept[kept.Count - 1];
                double dx = path[i][0] - last[0], dy = path[i][1] - last[1];
                if ((dx * dx) + (dy * dy) >= min2) kept.Add(path[i]);
            }
            kept.Add(path[path.Count - 1]);
            return kept;
        }

        /// <summary>Chia đoạn dài hơn <paramref name="maxStep"/> thành các đoạn ngắn (để lưới chỉ đăng ký ít ô).</summary>
        private static List<double[]> SplitLong(List<double[]> path, double maxStep)
        {
            if (path.Count < 2) return path;
            var result = new List<double[]> { path[0] };
            for (int i = 1; i < path.Count; i++)
            {
                double dx = path[i][0] - path[i - 1][0], dy = path[i][1] - path[i - 1][1];
                int n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((dx * dx) + (dy * dy)) / maxStep));
                for (int s = 1; s <= n; s++)
                    result.Add(new[] { path[i - 1][0] + dx * s / n, path[i - 1][1] + dy * s / n });
            }
            return result;
        }

        /// <summary>Tối đa <paramref name="count"/> điểm cách đều theo chiều dài (gồm hai đầu).</summary>
        private static List<double[]> EvenSamples(List<double[]> path, int count)
        {
            var samples = new List<double[]>();
            if (path.Count == 0) return samples;
            double total = 0;
            for (int i = 1; i < path.Count; i++)
                total += Math.Sqrt(Math.Pow(path[i][0] - path[i - 1][0], 2) + Math.Pow(path[i][1] - path[i - 1][1], 2));
            if (path.Count == 1 || total <= 1e-9) { samples.Add(path[0]); return samples; }
            int n = Math.Max(2, Math.Min(count, (int)Math.Ceiling(total / 0.05) + 1));
            int segment = 1;
            double walked = 0;
            for (int s = 0; s < n; s++)
            {
                double target = total * s / (n - 1);
                while (segment < path.Count - 1)
                {
                    double length = Math.Sqrt(Math.Pow(path[segment][0] - path[segment - 1][0], 2) + Math.Pow(path[segment][1] - path[segment - 1][1], 2));
                    if (walked + length >= target) break;
                    walked += length;
                    segment++;
                }
                double[] a = path[segment - 1], b = path[segment];
                double l = Math.Sqrt(Math.Pow(b[0] - a[0], 2) + Math.Pow(b[1] - a[1], 2));
                double t = l <= 1e-12 ? 0 : Math.Max(0, Math.Min(1, (target - walked) / l));
                samples.Add(new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t });
            }
            return samples;
        }

        private static double SampleSpacing(List<List<double[]>> paths)
        {
            double total = 0;
            foreach (List<double[]> path in paths)
                for (int i = 1; i < path.Count; i++)
                    total += Math.Sqrt(Math.Pow(path[i][0] - path[i - 1][0], 2) + Math.Pow(path[i][1] - path[i - 1][1], 2));
            return Math.Max(0.5, total / 40000.0);
        }

        /// <summary>Điểm cách đều ≤ <paramref name="spacing"/> dọc các nét.</summary>
        private static List<double[]> Densify(List<List<double[]>> paths, double spacing)
        {
            var samples = new List<double[]>();
            foreach (List<double[]> path in paths)
            {
                if (path.Count == 0) continue;
                samples.Add(path[0]);
                for (int i = 1; i < path.Count; i++)
                {
                    double dx = path[i][0] - path[i - 1][0], dy = path[i][1] - path[i - 1][1];
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    int n = Math.Max(1, (int)Math.Ceiling(length / spacing));
                    for (int s = 1; s <= n; s++)
                        samples.Add(new[] { path[i - 1][0] + dx * s / n, path[i - 1][1] + dy * s / n });
                }
            }
            return samples;
        }

        /// <summary>Ghi kết quả cổng vào log (P0: không chặn; LỆCH → errorLog để người dùng thấy).</summary>
        private static void LogViewIdentity(
            DXFPropforExport properties,
            ViewIdentityReference reference,
            ViewFrame copyFrame,
            ViewFrame savedFrame,
            ViewIdentityVerdict verdict)
        {
            string prefix = $"File:{properties?.FileName}-{properties?.PartNumber}: ";
            if (reference != null)
            {
                ErrorEvents.LogDebug(prefix + "[ORIENT] view gốc: " + (reference.Frame?.ToString() ?? "(không đọc được)") +
                                     "; scale " + F6(reference.ViewScale) +
                                     "; xform " + Join(reference.Xform) + "; outline " + Join(reference.Outline) +
                                     "; polyline " + reference.Diagnostics + " (" + reference.Milliseconds + " ms)");
                LogCopyFrame(prefix, reference.Frame, copyFrame, "sau khi đọc model");
                LogCopyFrame(prefix, reference.Frame, savedFrame, "sau SaveAs DXF");
            }
            if (verdict == null) return;
            // P0 chỉ ghi nhật ký (khung toạ độ của GetPolylines7 chưa được đo trên SolidWorks thật — một khung
            // xoay so với tờ giấy sẽ báo LỆCH sai cho mọi view xoay): KHÔNG ghi errorLog.
            string line = "[VIEW_GATE] " + verdict.Status + ": " + verdict.Message + " [" + verdict.Milliseconds + " ms]";
            DxfTrace.Info(prefix + line);
            ErrorEvents.LogDebug(prefix + line);
        }

        private static void LogCopyFrame(string prefix, ViewFrame source, ViewFrame copy, string when)
        {
            if (source == null || copy == null) return;
            if (source.Matches(copy))
            {
                ErrorEvents.LogDebug(prefix + "[ORIENT] bản sao cùng hướng view gốc " + when);
                return;
            }
            // Hai ma trận cùng đọc bằng ModelToViewTransform — so được tin cậy: ghi errorLog để thấy ngay.
            ErrorEvents.LogError(prefix + "[ORIENT] CẢNH BÁO HƯỚNG: view bản sao KHÁC view gốc " + when + " (lệch " +
                                 Deg(copy.InPlaneAngleFrom(source)) + "°" + (copy.IsMirrorOf(source) ? ", nhìn từ mặt đối diện" : "") +
                                 ") — bản sao: " + copy + " (v150 chỉ ghi log, chưa chặn).");
        }

        private static string F4(double value) =>
            double.IsNaN(value) ? "?" : value > 1e6 ? "xa" : value.ToString("0.####", CultureInfo.InvariantCulture);

        private static string F6(double value) =>
            double.IsNaN(value) ? "?" : value.ToString("G6", CultureInfo.InvariantCulture);

        private static string Join(double[] values) =>
            values == null ? "?" : "[" + string.Join(", ", values.Select(F6)) + "]";
    }
}
