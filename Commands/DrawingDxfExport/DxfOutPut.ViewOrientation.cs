// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Hướng của DXF = hướng view trên giấy, ĐO bằng ma trận chứ không đọc góc.
    ///
    /// view.Angle chỉ là phần xoay cộng thêm; hướng thật = hướng gốc Flat-Pattern
    /// (fixed face, grain direction — SolidWorks tính lại khi rebuild/paste) × Flip ×
    /// Angle. Hai view có cùng Angle/Flip vẫn có thể xoay khác nhau 90°/180°.
    /// ModelToViewTransform là phép biến đổi SolidWorks dùng để VẼ view, nên nó gồm
    /// đủ cả ba phần. So/chỉnh view tạm theo nó, đo lại ngay trước khi lưu, rồi khớp
    /// hình DXF với cạnh của view gốc — sai hướng thì không xuất.
    /// Log: [ORIENT].
    /// </summary>
    internal partial class DxfOutPut
    {
        private const double FrameAngleToleranceRad = 1e-4;
        private const double OrientationMatchToleranceMm = 0.1;
        private const int MaxOrientationSamples = 6000;

        /// <summary>
        /// Hướng model ứng với trục giấy +X/+Y và +Z (về phía người xem) của một view.
        /// Đọc bằng MathVector nên không phụ thuộc quy ước hàng/cột của ArrayData.
        /// </summary>
        private sealed class ViewFrame
        {
            public double[] SheetX { get; private set; }
            public double[] SheetY { get; private set; }
            public double[] SheetZ { get; private set; }
            public double Angle { get; private set; }
            public bool Flip { get; private set; }

            public static ViewFrame Read(IView view)
            {
                if (view == null) return null;
                MathUtility math = null;
                MathTransform modelToView = null;
                try
                {
                    math = DxfExportContext.App?.GetMathUtility() as MathUtility;
                    modelToView = view.ModelToViewTransform;
                    if (math == null || modelToView == null) return null;

                    // Ảnh của các trục model trên giấy (có cả tỉ lệ view).
                    var images = new double[3][];
                    for (int axis = 0; axis < 3; axis++)
                    {
                        var unit = new double[3];
                        unit[axis] = 1;
                        MathVector vector = math.CreateVector(unit) as MathVector;
                        MathVector mapped = vector?.MultiplyTransform(modelToView) as MathVector;
                        images[axis] = mapped?.ArrayData as double[];
                        SafeReleaseComObject(mapped);
                        SafeReleaseComObject(vector);
                        if (images[axis] == null || images[axis].Length < 3) return null;
                    }

                    // Hàng của ma trận model→giấy = hướng model rơi vào trục giấy đó.
                    double[] Row(int sheetAxis) => Normalize(new[]
                    {
                        images[0][sheetAxis], images[1][sheetAxis], images[2][sheetAxis]
                    });

                    var frame = new ViewFrame
                    {
                        SheetX = Row(0),
                        SheetY = Row(1),
                        SheetZ = Row(2)
                    };
                    if (frame.SheetX == null || frame.SheetY == null || frame.SheetZ == null)
                        return null;
                    try { frame.Angle = view.Angle; } catch { }
                    try { frame.Flip = view.FlipView; } catch { }
                    return frame;
                }
                catch (Exception ex)
                {
                    ErrorEvents.LogDebug("[ORIENT] không đọc được ModelToViewTransform: " + ex.Message);
                    return null;
                }
                finally
                {
                    SafeReleaseComObject(modelToView);
                    SafeReleaseComObject(math);
                }
            }

            /// <summary>Hai view nhìn cùng phía và cùng hướng trên giấy.</summary>
            public bool Matches(ViewFrame other) =>
                other != null &&
                Dot(SheetZ, other.SheetZ) > Math.Cos(FrameAngleToleranceRad) &&
                Math.Abs(InPlaneAngleFrom(other)) <= FrameAngleToleranceRad;

            /// <summary>View kia nhìn từ phía đối diện (bị lật gương so với view này).</summary>
            public bool IsMirrorOf(ViewFrame other) =>
                other != null && Dot(SheetZ, other.SheetZ) < -Math.Cos(1e-3);

            public bool SameViewDirection(ViewFrame other) =>
                other != null && Dot(SheetZ, other.SheetZ) > Math.Cos(1e-3);

            /// <summary>Góc (rad) từ trục giấy X của view kia tới của view này, quanh hướng nhìn.</summary>
            public double InPlaneAngleFrom(ViewFrame other)
            {
                if (other == null) return double.NaN;
                return Math.Atan2(
                    Dot(Cross(other.SheetX, SheetX), SheetZ),
                    Dot(other.SheetX, SheetX));
            }

            public override string ToString()
            {
                return "Angle=" + Deg(Angle) + "°, Flip=" + Flip +
                       ", giấyX←model" + Vec(SheetX) + ", nhìn←model" + Vec(SheetZ);
            }

            private static string Vec(double[] v) =>
                v == null ? "(?)" : "(" + string.Join(",", v.Select(x =>
                    x.ToString("0.####", CultureInfo.InvariantCulture))) + ")";
        }

        private static string Deg(double radians) =>
            (radians * 180.0 / Math.PI).ToString("0.###", CultureInfo.InvariantCulture);

        private static double Dot(double[] a, double[] b) =>
            a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

        private static double[] Cross(double[] a, double[] b) => new[]
        {
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0]
        };

        private static double[] Normalize(double[] v)
        {
            double length = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            return length < 1e-12 ? null : new[] { v[0] / length, v[1] / length, v[2] / length };
        }

        /// <summary>
        /// Chỉnh view tạm cho tới khi ma trận của nó khớp view gốc: lật nếu nhìn từ phía
        /// ngược, rồi xoay bù phần lệch trong mặt giấy. Chiều dấu của view.Angle không
        /// được giả định — thử +delta, sai thì -delta, và luôn đo lại sau rebuild.
        /// Không khớp được thì ném lỗi: DXF xoay sai còn tệ hơn không có DXF.
        /// Trả về view (đã lấy lại sau rebuild).
        /// </summary>
        private static IView AlignTemporaryViewToSourceFrame(
            ModelDoc2 model,
            IDrawingDoc temporary,
            IView view,
            string viewName,
            ViewFrame source,
            DXFPropforExport properties)
        {
            string prefix = $"File:{properties?.FileName}-{properties?.PartNumber}: ";
            if (source == null)
            {
                ErrorEvents.LogDebug(prefix + "[ORIENT] không đọc được ma trận view gốc — " +
                    "chỉ chép Angle/Flip, KHÔNG kiểm chứng được hướng DXF.");
                return view;
            }

            ViewFrame current = ViewFrame.Read(view);
            if (current == null)
                throw new InvalidOperationException("Không đọc được ma trận hướng của view tạm.");

            ErrorEvents.LogDebug(prefix + "[ORIENT] view gốc: " + source +
                " | view tạm sau paste: " + current +
                " | lệch trong mặt giấy " + Deg(source.InPlaneAngleFrom(current)) + "°" +
                (source.IsMirrorOf(current) ? ", NGƯỢC MẶT" : ""));
            if (source.Matches(current)) return view;

            IView Refresh()
            {
                model.ForceRebuild3(false);
                SafeReleaseComObject(view);
                IView fresh = FindViewByName(model, viewName);
                if (fresh == null)
                    throw new InvalidOperationException("View tạm mất sau khi chỉnh hướng.");
                temporary.ActivateView(viewName);
                return fresh;
            }

            if (source.IsMirrorOf(current))
            {
                view.FlipView = !view.FlipView;
                view = Refresh();
                current = ViewFrame.Read(view);
                ErrorEvents.LogDebug(prefix + "[ORIENT] đảo Flip view tạm → " + current);
            }

            if (!source.SameViewDirection(current))
            {
                throw new InvalidOperationException(
                    "View tạm nhìn từ hướng khác view gốc (gốc: " + source +
                    "; tạm: " + current + ").");
            }

            double delta = source.InPlaneAngleFrom(current);
            if (Math.Abs(delta) > FrameAngleToleranceRad)
            {
                double original = view.Angle;
                foreach (double candidate in new[] { original + delta, original - delta })
                {
                    view.Angle = candidate;
                    view = Refresh();
                    current = ViewFrame.Read(view);
                    if (source.Matches(current)) break;
                }
            }

            if (!source.Matches(current))
            {
                throw new InvalidOperationException(
                    "Không xoay được view tạm cho khớp view gốc (còn lệch " +
                    Deg(source.InPlaneAngleFrom(current)) + "°; gốc: " + source +
                    "; tạm: " + current + ").");
            }

            ErrorEvents.LogDebug(prefix + "[ORIENT] đã chỉnh view tạm khớp view gốc: " + current);
            return view;
        }

        /// <summary>Đo lại ngay trước khi lưu: các bước xoá/rebuild sau đó không được làm đổi hướng.</summary>
        private static void VerifyTemporaryViewFrame(
            ModelDoc2 model,
            string viewName,
            ViewFrame source,
            DXFPropforExport properties)
        {
            if (source == null) return;
            IView view = FindViewByName(model, viewName);
            try
            {
                ViewFrame current = ViewFrame.Read(view);
                if (current == null || !source.Matches(current))
                {
                    throw new InvalidOperationException(
                        "Hướng view tạm đổi trong lúc chuẩn bị DXF (gốc: " + source +
                        "; tạm: " + (current?.ToString() ?? "không đọc được") + ").");
                }
            }
            finally
            {
                SafeReleaseComObject(view);
            }
        }

        // ───────────────────────── khớp hình DXF với view gốc ─────────────────────────

        private sealed class OrientationSamples
        {
            public List<double[]> Points { get; } = new List<double[]>();
            public string SkipReason { get; set; }

            /// <summary>
            /// Khung view gốc trên giấy (GetOutline ÷ tỉ lệ view, mm). Không đi qua
            /// ModelToViewTransform nên vẫn bắt được lỗi 90° nếu chính ma trận sai.
            /// </summary>
            public double OutlineWidthMm { get; set; }
            public double OutlineHeightMm { get; set; }
        }

        /// <summary>
        /// Điểm trên các cạnh view gốc đang hiển thị, chiếu lên trục giấy của view gốc
        /// (mm, không tỉ lệ, không tịnh tiến). Đọc TRƯỚC khi copy, trên Drawing gốc.
        /// </summary>
        private static OrientationSamples SampleSourceViewEdges(IView view, ViewFrame frame)
        {
            var samples = new OrientationSamples();
            try
            {
                double scale = view?.ScaleDecimal ?? 0;
                if (view?.GetOutline() is double[] outline && outline.Length >= 4 && scale > 0)
                {
                    samples.OutlineWidthMm = (outline[2] - outline[0]) / scale * 1000.0;
                    samples.OutlineHeightMm = (outline[3] - outline[1]) / scale * 1000.0;
                }
            }
            catch (COMException) { }

            if (view == null || frame == null)
            {
                samples.SkipReason = "không có ma trận view gốc";
                return samples;
            }
            try
            {
                object[] components = view.GetVisibleComponents() as object[];
                if (components != null && components.OfType<Component2>().Any())
                {
                    samples.SkipReason = "view assembly";
                    return samples;
                }

                object[] entities = TryGetVisibleEdgeEntities(view, null);
                if (entities == null || entities.Length == 0)
                {
                    samples.SkipReason = "view không trả cạnh nào";
                    return samples;
                }

                var raw = new List<double[]>();
                foreach (IEdge edge in entities.OfType<IEdge>())
                    raw.AddRange(SampleEdge3D(edge));

                int stride = Math.Max(1, (int)Math.Ceiling(raw.Count / (double)MaxOrientationSamples));
                for (int index = 0; index < raw.Count; index += stride)
                {
                    double[] p = raw[index];
                    samples.Points.Add(new[] { Dot(p, frame.SheetX) * 1000.0, Dot(p, frame.SheetY) * 1000.0 });
                }
                if (samples.Points.Count < 3) samples.SkipReason = "quá ít điểm trên cạnh view";
            }
            catch (Exception ex)
            {
                samples.SkipReason = "lỗi đọc cạnh view gốc: " + ex.Message;
            }
            return samples;
        }

        private static IEnumerable<double[]> SampleEdge3D(IEdge edge)
        {
            var points = new List<double[]>();
            ICurve curve = null;
            try
            {
                curve = edge.GetCurve() as ICurve;
                double[] p = edge.GetCurveParams2() as double[];
                if (curve == null || p == null || p.Length < 8) return points;
                double[] start = { p[0], p[1], p[2] };
                double[] end = { p[3], p[4], p[5] };

                if (curve.IsLine())
                {
                    for (int i = 0; i <= 8; i++)
                    {
                        double t = i / 8.0;
                        points.Add(new[]
                        {
                            start[0] + (end[0] - start[0]) * t,
                            start[1] + (end[1] - start[1]) * t,
                            start[2] + (end[2] - start[2]) * t
                        });
                    }
                    return points;
                }

                bool closed = Distance3(start, end) < 1e-9;
                if (closed && curve.IsCircle() && curve.CircleParams is double[] c && c.Length >= 7)
                {
                    double[] axis = Normalize(new[] { c[3], c[4], c[5] });
                    double[] radial = axis == null ? null : Normalize(Subtract3(start, new[] { c[0], c[1], c[2] }));
                    if (radial == null) return points;
                    double[] tangent = Cross(axis, radial);
                    // Dây cung lệch vòng ≤ 0,002 mm thật: 72 điểm cố định lệch R·(1−cos 2,5°) — vòng MARK bán kính
                    // giấy > 31,5 mm (vd Ø80 ở 1:1) lệch > 0,03 mm nên nét CIRCLE của nó không «nằm trên» cạnh MARK.
                    int count = c[6] > 2e-6
                        ? (int)Math.Max(72, Math.Min(1024, Math.Ceiling(Math.PI / Math.Acos(1 - 2e-6 / c[6]))))
                        : 72;
                    for (int i = 0; i <= count; i++)   // khép kín: điểm cuối = điểm đầu (bộ đặt layer cần vòng kín)
                    {
                        double a = 2 * Math.PI * i / count;
                        double cos = Math.Cos(a) * c[6], sin = Math.Sin(a) * c[6];
                        points.Add(new[]
                        {
                            c[0] + radial[0] * cos + tangent[0] * sin,
                            c[1] + radial[1] * cos + tangent[1] * sin,
                            c[2] + radial[2] * cos + tangent[2] * sin
                        });
                    }
                    return points;
                }
                if (closed)
                {
                    // Spline/ellipse khép kín: lấy theo tham số của chính cạnh.
                    double t0 = p[6], t1 = p[7];
                    for (int i = 0; i <= 72; i++)   // khép kín: điểm cuối = điểm đầu (bộ đặt layer cần vòng kín)
                    {
                        double[] e = curve.Evaluate2(t0 + (t1 - t0) * i / 72.0, 0) as double[];
                        if (e != null && e.Length >= 3) points.Add(new[] { e[0], e[1], e[2] });
                    }
                    return points;
                }

                double chord = Math.Max(1e-6, Distance3(start, end));
                double[] tess = curve.GetTessPts(1e-5, Math.Max(1e-3, chord / 50.0), start, end) as double[];
                if (tess != null && tess.Length >= 6)
                {
                    for (int i = 0; i + 2 < tess.Length; i += 3)
                        points.Add(new[] { tess[i], tess[i + 1], tess[i + 2] });
                    // Đường cong khép kín về mặt hình học (cung tròn) có thể bị chia theo
                    // phần bù của cạnh: giữ chiều nào có điểm giữa nằm trên cạnh.
                    if (DistanceToEdge(edge, points[points.Count / 2]) > 1e-4)
                    {
                        double[] reversed = curve.GetTessPts(1e-5, Math.Max(1e-3, chord / 50.0), end, start) as double[];
                        if (reversed != null && reversed.Length >= 6)
                        {
                            var other = new List<double[]>();
                            for (int i = 0; i + 2 < reversed.Length; i += 3)
                                other.Add(new[] { reversed[i], reversed[i + 1], reversed[i + 2] });
                            if (DistanceToEdge(edge, other[other.Count / 2]) <= 1e-4) return other;
                        }
                    }
                    return points;
                }

                points.Add(start);
                points.Add(end);
                return points;
            }
            catch (COMException)
            {
                return points;
            }
            finally
            {
                SafeReleaseComObject(curve);
            }
        }

        private sealed class OrientationCheckResult
        {
            /// <summary>Chắc chắn DXF bị xoay/lật so với view gốc — không được xuất.</summary>
            public bool Failed { get; set; }
            public string Message { get; set; } = "";
        }

        /// <summary>
        /// Khớp hình: điểm cạnh view gốc (đã chiếu lên trục giấy) phải nằm trên hình học
        /// DXF. Thử 8 phương án (4 góc × lật) — tịnh tiến theo tâm khung bao lớp CUT.
        /// Chỉ báo SAI khi một phương án khác khớp rõ ràng hơn hẳn phương án đúng; chi
        /// tiết đối xứng cho điểm bằng nhau nên không bao giờ bị chặn nhầm.
        /// Thêm phép so khung bao với GetOutline của view gốc — không dùng ma trận, nên
        /// vẫn bắt được lỗi 90° nếu chính ma trận đọc sai hoặc không đọc được.
        /// </summary>
        private static OrientationCheckResult VerifyDxfOrientation(
            string dxfPath,
            OrientationSamples source,
            string cutLayerName)
        {
            var result = new OrientationCheckResult();
            bool hasPoints = source != null && source.Points.Count >= 3;
            bool hasOutline = source != null && source.OutlineWidthMm > 0 && source.OutlineHeightMm > 0;
            if (!hasPoints && !hasOutline)
            {
                result.Message = "bỏ qua kiểm tra hướng (" + (source?.SkipReason ?? "không có dữ liệu view gốc") + ")";
                return result;
            }

            List<List<double[]>> paths;
            List<List<double[]>> cutPaths;
            try
            {
                if (!ReadDxfPaths(dxfPath, cutLayerName, out paths, out cutPaths))
                {
                    result.Message = "bỏ qua kiểm tra hướng (không đọc được DXF)";
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Message = "bỏ qua kiểm tra hướng (lỗi đọc DXF: " + ex.Message + ")";
                return result;
            }
            if (paths.Count == 0)
            {
                result.Message = "bỏ qua kiểm tra hướng (DXF không có hình học)";
                return result;
            }

            double[] dxfBox = Box(cutPaths.Count > 0 ? cutPaths.SelectMany(path => path) : paths.SelectMany(path => path));
            double dxfWidth = dxfBox[2] - dxfBox[0], dxfHeight = dxfBox[3] - dxfBox[1];

            // 1) Khung bao — độc lập với ModelToViewTransform. Lề của khung view cộng
            // đều hai chiều nên chỉ so CHIỀU DÀI/RỘNG ngược nhau rõ rệt (≥ 1,3 lần).
            string outlineText = "";
            bool outlineSwapped = false;
            if (hasOutline)
            {
                double w = source.OutlineWidthMm, h = source.OutlineHeightMm;
                outlineText = "; khung view gốc " + F(w) + "×" + F(h) + " mm";
                outlineSwapped = (dxfWidth >= 1.3 * dxfHeight && h >= 1.3 * w) ||
                                 (dxfHeight >= 1.3 * dxfWidth && w >= 1.3 * h);
            }
            string dxfText = "DXF(CUT) " + F(dxfWidth) + "×" + F(dxfHeight) + " mm";

            // 2) Khớp hình theo cạnh view gốc (cần ma trận view gốc).
            string scoreText = "";
            int best = 0;
            bool shapeDecisive = false, shapeOk = false;
            if (hasPoints)
            {
                double[] srcBox = Box(source.Points);
                var grid = new SegmentGrid(paths, dxfBox);
                var scores = new double[8];
                for (int k = 0; k < 8; k++)
                {
                    List<double[]> moved = source.Points.Select(point => Candidate(point, k)).ToList();
                    double[] box = Box(moved);
                    double dx = (dxfBox[0] + dxfBox[2]) / 2 - (box[0] + box[2]) / 2;
                    double dy = (dxfBox[1] + dxfBox[3]) / 2 - (box[1] + box[3]) / 2;
                    int hits = moved.Count(point =>
                        grid.Distance(point[0] + dx, point[1] + dy) <= OrientationMatchToleranceMm);
                    scores[k] = hits / (double)moved.Count;
                }

                best = Enumerable.Range(0, 8).OrderByDescending(k => scores[k]).First();
                scoreText = "khớp đúng hướng " + Pct(scores[0]) +
                            (best != 0 ? ", tốt nhất " + DescribeCandidate(best) + " " + Pct(scores[best]) : "") +
                            "; cạnh view " + F(srcBox[2] - srcBox[0]) + "×" + F(srcBox[3] - srcBox[1]) + " mm; ";

                // Đúng hướng thì mọi thứ có trong DXF đều khớp; hướng sai chỉ khớp bằng
                // khi chi tiết đối xứng. Điểm lấy theo CẠNH (lỗ nhỏ cũng đủ 72 điểm), nên
                // một lỗ lệch tâm trên tấm gần đối xứng vẫn tạo chênh lệch rõ.
                double lead = scores[best] - scores[0];
                shapeDecisive = lead >= 0.02 && lead * source.Points.Count >= 15 && scores[best] >= 0.6;
                shapeOk = !shapeDecisive && scores[0] >= 0.5;
            }
            else
            {
                scoreText = "không khớp hình được (" + source.SkipReason + "); ";
            }

            string detail = " (" + scoreText + dxfText + outlineText + ")";
            if (shapeDecisive)
            {
                result.Failed = true;
                result.Message = "DXF bị " + DescribeCandidate(best) + " so với Drawing View gốc" + detail;
            }
            else if (outlineSwapped)
            {
                result.Failed = true;
                result.Message = "khung DXF xoay 90° so với khung Drawing View gốc" + detail;
            }
            else if (shapeOk)
            {
                result.Message = "DXF đúng hướng view gốc" + detail;
            }
            else
            {
                result.Message = "kiểm tra hướng không kết luận được" + detail;
            }
            return result;
        }

        private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Pct(double value) => (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

        private static string DescribeCandidate(int k)
        {
            string turn = (k % 4) == 0 ? "" : "xoay " + ((k % 4) * 90) + "°";
            if (k < 4) return turn.Length == 0 ? "giữ nguyên" : turn;
            return "lật gương" + (turn.Length == 0 ? "" : " + " + turn);
        }

        /// <summary>k = 0..3: xoay k×90° ngược chiều kim đồng hồ; 4..7: lật X rồi xoay.</summary>
        private static double[] Candidate(double[] point, int k)
        {
            double x = k >= 4 ? -point[0] : point[0];
            double y = point[1];
            switch (k % 4)
            {
                case 1: return new[] { -y, x };
                case 2: return new[] { -x, -y };
                case 3: return new[] { y, -x };
                default: return new[] { x, y };
            }
        }

        private static double[] Box(IEnumerable<double[]> points)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (double[] p in points)
            {
                minX = Math.Min(minX, p[0]); minY = Math.Min(minY, p[1]);
                maxX = Math.Max(maxX, p[0]); maxY = Math.Max(maxY, p[1]);
            }
            return minX == double.MaxValue ? new double[4] : new[] { minX, minY, maxX, maxY };
        }

        /// <summary>Đọc LINE/ARC/CIRCLE/LWPOLYLINE/SPLINE/ELLIPSE thành đường gấp khúc (mm).</summary>
        private static bool ReadDxfPaths(string dxfPath, string cutLayerName,
            out List<List<double[]>> paths, out List<List<double[]>> cutPaths)
        {
            paths = new List<List<double[]>>(); cutPaths = new List<List<double[]>>();
            var document = ADDIN.Commands.DxfChecking.DxfRawDocument.Read(dxfPath);
            var geometry = new ADDIN.Commands.DxfChecking.DxfFileResult();
            document.Extract(geometry);
            string units = document.Header("$INSUNITS");
            double scale = units == "1" ? 25.4 : units == "2" ? 304.8 : units == "5" ? 10 : units == "6" ? 1000 : 1;
            if (units != null && units != "0" && units != "1" && units != "2" && units != "4" && units != "5" && units != "6")
                throw new System.IO.InvalidDataException("DXF có đơn vị chưa được hỗ trợ: " + units);
            foreach (var edge in geometry.Edges) {
                if (!string.Equals(edge.Space, "MODEL", StringComparison.OrdinalIgnoreCase)) continue;
                var sampled = ADDIN.Commands.DxfChecking.DxfGeometry.Sample(edge, 0.002 / scale);
                var path = sampled.Select(p => new[] { p.X * scale, p.Y * scale }).ToList();
                if (path.Count < 2) continue;
                paths.Add(path);
                if (string.Equals(edge.Layer, cutLayerName, StringComparison.OrdinalIgnoreCase)) cutPaths.Add(path);
            }
            return paths.Count > 0;
        }
        private static List<double[]> ArcPath(double cx, double cy, double radius, double start, double end, bool mirrored)
        {
            // Mũi tên cung ≤ 0,002 mm (đơn vị file, thường là mm).
            double sweep = end - start;
            double step = radius > 0.002 ? 2 * Math.Acos(1 - 0.002 / radius) : sweep;
            int count = Math.Max(8, Math.Min(4096, (int)Math.Ceiling(Math.Abs(sweep) / Math.Max(step, 1e-6))));
            var path = new List<double[]>(count + 1);
            for (int i = 0; i <= count; i++)
            {
                double angle = start + sweep * i / count;
                double x = radius * Math.Cos(angle);
                path.Add(new[] { mirrored ? cx - x : cx + x, cy + radius * Math.Sin(angle) });
            }
            return path;
        }

        /// <summary>Lưới đều để hỏi khoảng cách điểm → đoạn thẳng gần nhất.</summary>
        private sealed class SegmentGrid
        {
            private readonly Dictionary<long, List<double[]>> _cells = new Dictionary<long, List<double[]>>();
            private readonly double _cell;

            public SegmentGrid(List<List<double[]>> paths, double[] box, double cell = 0)
            {
                double diagonal = Math.Sqrt(Math.Pow(box[2] - box[0], 2) + Math.Pow(box[3] - box[1], 2));
                _cell = cell > 0 ? cell : Math.Max(1.0, diagonal / 200.0);
                foreach (List<double[]> path in paths)
                {
                    for (int i = 0; i + 1 < path.Count; i++)
                    {
                        double[] segment = { path[i][0], path[i][1], path[i + 1][0], path[i + 1][1] };
                        long x0 = Key(Math.Min(segment[0], segment[2])), x1 = Key(Math.Max(segment[0], segment[2]));
                        long y0 = Key(Math.Min(segment[1], segment[3])), y1 = Key(Math.Max(segment[1], segment[3]));
                        if ((x1 - x0 + 1) * (y1 - y0 + 1) > 40000) continue;
                        for (long x = x0; x <= x1; x++)
                            for (long y = y0; y <= y1; y++)
                            {
                                long key = (x << 32) ^ (y & 0xFFFFFFFF);
                                if (!_cells.TryGetValue(key, out List<double[]> list))
                                    _cells[key] = list = new List<double[]>();
                                list.Add(segment);
                            }
                    }
                }
            }

            private long Key(double value) => (long)Math.Floor(value / _cell);

            public double Distance(double px, double py)
            {
                double best = double.MaxValue;
                long cx = Key(px), cy = Key(py);
                for (long x = cx - 1; x <= cx + 1; x++)
                    for (long y = cy - 1; y <= cy + 1; y++)
                    {
                        if (!_cells.TryGetValue((x << 32) ^ (y & 0xFFFFFFFF), out List<double[]> list)) continue;
                        foreach (double[] s in list)
                            best = Math.Min(best, PointSegment(px, py, s));
                    }
                return best;
            }

            private static double PointSegment(double px, double py, double[] s)
            {
                double dx = s[2] - s[0], dy = s[3] - s[1];
                double length2 = dx * dx + dy * dy;
                double t = length2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((px - s[0]) * dx + (py - s[1]) * dy) / length2));
                double x = s[0] + t * dx - px, y = s[1] + t * dy - py;
                return Math.Sqrt(x * x + y * y);
            }
        }
    }
}
