// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Một đường (mm, hệ toạ độ GIẤY của file DXF thô SolidWorks ghi, tức
    /// sheet × 1000 ở tỉ lệ view đang có) kèm nguồn gốc để báo cáo.
    /// </summary>
    internal sealed class DxfSemanticItem
    {
        public string Source { get; set; } = "";

        /// <summary>LINE, CIRCLE (tâm, điểm trên vòng), ARC (đầu, cuối, giữa), POLYLINE.</summary>
        public string Kind { get; set; } = "POLYLINE";

        /// <summary>Điểm mẫu dọc đường (mm). Với LINE: hai đầu.</summary>
        public List<double[]> Points { get; } = new List<double[]>();
    }

    /// <summary>
    /// «Ý nghĩa» của hình trong một view, do XToolPro đọc thẳng từ model SolidWorks
    /// trước khi lưu DXF, để bộ chuẩn hoá đặt layer BẰNG HÌNH HỌC thay vì nhờ
    /// SolidWorks vẽ lại sketch tạm. Mọi toạ độ ở khung DXF thô (mm).
    /// </summary>
    internal sealed class DxfSemanticPlan
    {
        /// <summary>Mọi cạnh nhìn thấy của view (kiểm chứng phép chiếu, cắt bỏ hình của view khác).</summary>
        public List<DxfSemanticItem> VisibleEdges { get; } = new List<DxfSemanticItem>();

        /// <summary>Cạnh feature MARK → LASER_MARK.</summary>
        public List<DxfSemanticItem> Marks { get; } = new List<DxfSemanticItem>();

        /// <summary>Vòng trong lỗ MARK xiên, miệng lỗ côn/bậc khi không khắc… → xoá khỏi DXF.</summary>
        public List<DxfSemanticItem> Erase { get; } = new List<DxfSemanticItem>();

        /// <summary>Đường uốn (hai đầu) → BEND; không có trong DXF thì tự ghi.</summary>
        public List<DxfSemanticItem> Bends { get; } = new List<DxfSemanticItem>();

        /// <summary>Sketch MARK người dùng vẽ trên view → tự ghi lên LASER_MARK.</summary>
        public List<DxfSemanticItem> UserMarks { get; } = new List<DxfSemanticItem>();

        /// <summary>Tỉ lệ view (DXF thô = model × tỉ lệ). Kết quả cuối chia cho tỉ lệ này.</summary>
        public double ViewScale { get; set; } = 1.0;

        // Immutable analytical Drawing curves, in physical view-local coordinates.
        public double[] ReferenceCurveBuffer { get; set; }

        // Exact Drawing curves whose resolved line font has no gaps or dots.
        public double[] ContinuousReferenceCurveBuffer { get; set; }

        /// <summary>Số cạnh model view cho thấy lúc đọc kế hoạch (-1: chưa đọc); so lại sau khi lưu DXF.</summary>
        public int ViewEdgeCount { get; set; } = -1;

        /// <summary>Chỉ giữ hình nằm trong khung của view (view phụ thuộc còn view cha trên sheet).</summary>
        public bool CropToView { get; set; }

        /// <summary>Xoá chữ không thuộc PART_INFO (chú thích uốn, nhãn view…).</summary>
        public bool RemoveStrayText { get; set; } = true;

        /// <summary>Dời hình để góc dưới-trái của hình cắt về (0,0).</summary>
        public bool MoveToOrigin { get; set; } = true;

        /// <summary>Số cạnh giữa hai mặt cùng hướng nhìn đã đưa vào Erase (chẩn đoán).</summary>
        public int FaceSplitEdges { get; set; }

        /// <summary>Ghi kế hoạch ra text (một mục một dòng) để chạy lại ngoài SolidWorks.</summary>
        public void Save(string path)
        {
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            var lines = new List<string>
            {
                "scale=" + ViewScale.ToString("R", ic),
                "crop=" + CropToView,
                "text=" + RemoveStrayText,
                "origin=" + MoveToOrigin
            };
            foreach (var group in new[] { ("V", VisibleEdges), ("M", Marks), ("E", Erase), ("B", Bends), ("U", UserMarks) })
            {
                foreach (DxfSemanticItem item in group.Item2)
                {
                    lines.Add(group.Item1 + "\t" + item.Kind + "\t" + (item.Source ?? "").Replace('\t', ' ') + "\t" +
                              string.Join(" ", item.Points.Select(p => p[0].ToString("R", ic) + "," + p[1].ToString("R", ic))));
                }
            }
            if (ReferenceCurveBuffer != null)
                lines.Add("curvebuffer=" + string.Join(" ", ReferenceCurveBuffer.Select(v => v.ToString("R", ic))));
            if (ContinuousReferenceCurveBuffer != null)
                lines.Add("cutcurvebuffer=" + string.Join(" ", ContinuousReferenceCurveBuffer.Select(v => v.ToString("R", ic))));
            System.IO.File.WriteAllLines(path, lines, System.Text.Encoding.UTF8);
        }

        public static DxfSemanticPlan Load(string path)
        {
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            var plan = new DxfSemanticPlan();
            foreach (string line in System.IO.File.ReadAllLines(path, System.Text.Encoding.UTF8))
            {
                if (line.StartsWith("cutcurvebuffer=")) { plan.ContinuousReferenceCurveBuffer = line.Substring(15).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, ic)).ToArray(); continue; }
                if (line.StartsWith("curvebuffer=")) { plan.ReferenceCurveBuffer = line.Substring(12).Split(' ').Select(v => double.Parse(v, ic)).ToArray(); continue; }
                if (line.StartsWith("scale=")) { plan.ViewScale = double.Parse(line.Substring(6), ic); continue; }
                if (line.StartsWith("crop=")) { plan.CropToView = bool.Parse(line.Substring(5)); continue; }
                if (line.StartsWith("text=")) { plan.RemoveStrayText = bool.Parse(line.Substring(5)); continue; }
                if (line.StartsWith("origin=")) { plan.MoveToOrigin = bool.Parse(line.Substring(7)); continue; }
                string[] f = line.Split('\t');
                if (f.Length < 4) continue;
                var item = new DxfSemanticItem { Kind = f[1], Source = f[2] };
                foreach (string pair in f[3].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] xy = pair.Split(',');
                    item.Points.Add(new[] { double.Parse(xy[0], ic), double.Parse(xy[1], ic), 0.0 });
                }
                (f[0] == "V" ? plan.VisibleEdges : f[0] == "M" ? plan.Marks : f[0] == "E" ? plan.Erase : f[0] == "B" ? plan.Bends : plan.UserMarks).Add(item);
            }
            return plan;
        }
    }

    internal sealed class DxfSemanticResult
    {
        /// <summary>Tỉ lệ điểm mẫu của hình DXF nằm trên cạnh nhìn thấy đã chiếu (0–1).</summary>
        public double MatchRatio { get; set; }

        /// <summary>Phép chiếu đúng: hình DXF khớp cạnh view trong dung sai.</summary>
        public bool Verified { get; set; }

        /// <summary>Độ lệch tịnh tiến đã bù (mm) giữa DXF thô và phép chiếu.</summary>
        public double OffsetXMm { get; set; }
        public double OffsetYMm { get; set; }

        public int MarkEntities { get; set; }
        public int ErasedEntities { get; set; }
        public int BendEntitiesFound { get; set; }
        public int BendEntitiesInjected { get; set; }

        /// <summary>Nét nằm trên đường uốn nhưng cũng là cạnh model (biên) → giữ CUT.</summary>
        public int BendOnBoundary { get; set; }
        public int UserMarkEntitiesInjected { get; set; }
        public int CroppedEntities { get; set; }

        /// <summary>Cạnh giữa hai mặt cùng hướng về người nhìn (vùng uốn, mặt tách) đưa vào Erase.</summary>
        public int FaceSplitEdges { get; set; }
        public int StrayTextRemoved { get; set; }
        public int CutEntities { get; set; }
        public int NonVisibleEntitiesRemoved { get; set; }

        /// <summary>Cạnh MARK của model không tìm thấy nét DXF nào (chiếu ra ngoài / SW không vẽ).</summary>
        public int MarksWithoutEntity { get; set; }

        /// <summary>Nguồn + vị trí (mm) của các cạnh MARK không có nét — để chẩn đoán.</summary>
        public string MissingMarkDetail { get; set; } = "";

        /// <summary>Cạnh MARK không có nét mà XToolPro đã tự ghi lên LASER_MARK từ hình chiếu model.</summary>
        public int MarkEntitiesInjected { get; set; }

        /// <summary>Vòng xoá trùng khít một cạnh MARK (vòng mặt sau của lỗ MARK thẳng) — bỏ qua, không xoá.</summary>
        public int CoincidentEraseIgnored { get; set; }

        /// <summary>Nét nằm trên cả vòng xoá lẫn cạnh MARK, giữ làm MARK vì khớp MARK sát hơn hoặc ngang.</summary>
        public int MarkOverEraseEntities { get; set; }

        /// <summary>Hình xoá trong lòng vòng MARK thật (lỗ «đánh dấu để khoan sau»).</summary>
        public int RingInteriorRemoved { get; set; }

        /// <summary>Vòng MARK thay lỗ xuyên đồng tâm (R vòng, r lỗ, mm) — DXF Check bù lại.</summary>
        public List<KeyValuePair<double, double>> MarkRingSubstitutions { get; } = new List<KeyValuePair<double, double>>();

        public string Message { get; set; } = "";

        public string Summary =>
            "semantic: match=" + MatchRatio.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
            " verified=" + Verified +
            " offset=(" + OffsetXMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
            OffsetYMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ")" +
            " cut=" + CutEntities + " mark=" + MarkEntities + " (missing " + MarksWithoutEntity + ", injected " + MarkEntitiesInjected + ")" +
            (CoincidentEraseIgnored > 0 ? " erase-on-mark-ignored=" + CoincidentEraseIgnored : "") +
            (MarkOverEraseEntities > 0 ? " mark-over-erase=" + MarkOverEraseEntities : "") +
            " erased=" + ErasedEntities + " bend=" + BendEntitiesFound + "+" + BendEntitiesInjected + (BendOnBoundary > 0 ? " bend-on-boundary=" + BendOnBoundary : "") +
            " removed-hidden-or-unmarked=" + NonVisibleEntitiesRemoved + " userMark=" + UserMarkEntitiesInjected + " cropped=" + CroppedEntities + " face-split=" + FaceSplitEdges +
            " ring-interior=" + RingInteriorRemoved + (MissingMarkDetail.Length > 0 ? " missing-detail=[" + MissingMarkDetail + "]" : "") + " text-removed=" + StrayTextRemoved;
    }
}
