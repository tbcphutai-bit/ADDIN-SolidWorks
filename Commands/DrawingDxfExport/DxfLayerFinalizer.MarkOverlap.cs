// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.DrawingDxfExporting
{
    internal static partial class DxfLayerFinalizer
    {
        /// <summary>
        /// Nét LASER_MARK nằm TRỌN trên nét LASER_MARK khác → bỏ. Laser chỉ khắc một lần.
        /// Nguồn: rãnh MARK không xuyên có mép trên và đáy rãnh chiếu trùng nhau (SolidWorks
        /// ghi cả hai, một nét có thể bị chia đoạn), vòng MARK ghi vừa CIRCLE vừa ARC, sketch
        /// LAZER người dùng vẽ đè lên cạnh MARK. Giữ nét DÀI hơn; trùng khít thì giữ nét ghi
        /// trước. Nét chỉ chồng MỘT PHẦN (vươn ra ngoài) giữ cả hai. Thuần text, không cần
        /// SolidWorks — chạy sau <see cref="RemoveDuplicateGeometry(List{string})"/> (gỡ
        /// trùng khít theo số) để bắt cả trường hợp khác kiểu / khác cách chia.
        /// (Chép từ ITool src/ITool.SwCore/DXF/DxfLayerFinalizer.MarkOverlap.cs 06/10/2026.)
        /// </summary>
        internal static int RemoveLaserMarksCoveredByLaserMarks(
            List<string> lines,
            DxfManufacturingLayerSettings settings)
        {
            if (lines == null || lines.Count < 2) return 0;
            string layerName = (settings ?? DxfManufacturingLayerSettings.CreateDefault()).LaserMark.Name;

            var marks = new List<CoveredMarkCandidate>();
            // Đầu nét LASER_MARK không thành ứng viên (POLYLINE kiểu cũ — TryTessellateEntity không đọc): không bao
            // giờ bị bỏ ở bước này nên luôn tính là chỗ nối.
            var fixedEnds = new List<DxfPoint2D>();
            List<DxfRecord> records = CollectEntityRecords(lines);
            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                DxfRecord record = records[recordIndex];
                string type = (record.Type ?? "").Trim().ToUpperInvariant();
                if (type == "POLYLINE")
                {
                    string polylineLayer = GetGroupValue(lines, record.Start, record.EndExclusive, "8") ?? "0";
                    if (string.Equals(polylineLayer.Trim(), layerName, StringComparison.OrdinalIgnoreCase))
                        fixedEnds.AddRange(PolylineVertexEnds(lines, records, recordIndex));
                    continue;
                }
                if (!GeometryTypes.Contains(type)) continue;
                string layer = GetGroupValue(lines, record.Start, record.EndExclusive, "8") ?? "0";
                if (!string.Equals(layer.Trim(), layerName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!TryTessellateEntity(lines, record, LaserChordTolerance, CandidateSampleSpacing,
                        out List<List<DxfPoint2D>> polylines))
                    continue;

                var candidate = new CoveredMarkCandidate { Record = record, RecordIndex = recordIndex, Index = marks.Count };
                foreach (List<DxfPoint2D> polyline in polylines)
                {
                    if (polyline.Count == 0) continue;
                    candidate.Points.AddRange(polyline);
                    candidate.Ends.Add(polyline[0]);
                    candidate.Ends.Add(polyline[polyline.Count - 1]);
                    if (polyline.Count == 1)
                    {
                        candidate.Segments.Add(new DxfLineSegment2D { Start = polyline[0], End = polyline[0] });
                        continue;
                    }
                    for (int i = 1; i < polyline.Count; i++)
                    {
                        var segment = new DxfLineSegment2D { Start = polyline[i - 1], End = polyline[i] };
                        candidate.Segments.Add(segment);
                        double dx = segment.End.X - segment.Start.X, dy = segment.End.Y - segment.Start.Y;
                        candidate.Length += Math.Sqrt((dx * dx) + (dy * dy));
                    }
                }
                if (candidate.Points.Count > 0) marks.Add(candidate);
            }
            if (marks.Count < 2) return 0;

            var segments = new List<LaserSegment>();
            foreach (CoveredMarkCandidate candidate in marks)
            {
                foreach (DxfLineSegment2D segment in candidate.Segments)
                    segments.Add(new LaserSegment { Segment = segment, IsStraight = true, Owner = candidate.Index });
            }
            var index = new LaserSegmentIndex(segments, OverlapTolerance);
            double toleranceSquared = OverlapTolerance * OverlapTolerance;

            // Ngắn trước: khi xét một nét, mọi nét ngắn hơn đã quyết xong. Nét che phải
            // dài hơn (hoặc bằng và ghi trước) và chưa bị bỏ.
            // Phải nằm trọn trên CÙNG MỘT nét: một nét ngắn nối hai nét khác (bậc 0,146 mm ở góc ô MARK
            // của chi tiết 29/4115 — đầu này chạm cạnh dọc, đầu kia chạm cạnh ngang) KHÔNG phải nét chồng;
            // xoá nó là hở góc.
            var doomed = new bool[marks.Count];
            // Hai nét chồng nhau dài GẦN bằng nhau (lệch ≤ OverlapTolerance — mép trên/mép dưới của ô MARK
            // cắt xéo, SPLINE của tấm cuộn): giữ nét NỐI ĐƯỢC với các nét còn lại (mép gần), không phải nét ghi
            // trước — giữ mép xa thì bốn góc hở vài µm, ô MARK thành chuỗi hở (v150).
            // Lưới đầu nét (ô 1e-3 mm): «đầu này chạm đầu nét MARK khác còn giữ không» tra trong O(1) — trước đó
            // mỗi lần hỏi quét mọi nét, chuỗi 10 000 nét bằng nhau mất 27 s (phản biện 08/10).
            var endGrid = new Dictionary<long, List<KeyValuePair<int, DxfPoint2D>>>();
            long EndKey(double x, double y, int dx, int dy) =>
                ((long)((int)Math.Floor(x / EndGridCell) + dx) << 32) | (uint)((int)Math.Floor(y / EndGridCell) + dy);
            void AddEnd(int owner, DxfPoint2D end)
            {
                long key = EndKey(end.X, end.Y, 0, 0);
                if (!endGrid.TryGetValue(key, out List<KeyValuePair<int, DxfPoint2D>> bucket))
                    endGrid[key] = bucket = new List<KeyValuePair<int, DxfPoint2D>>();
                bucket.Add(new KeyValuePair<int, DxfPoint2D>(owner, end));
            }
            foreach (CoveredMarkCandidate mark in marks)
                foreach (DxfPoint2D end in mark.Ends) AddEnd(mark.Index, end);
            foreach (DxfPoint2D end in fixedEnds) AddEnd(-1, end);
            bool EndTouches(CoveredMarkCandidate mark, DxfPoint2D end, double tolerance)
            {
                double tolerance2 = tolerance * tolerance;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (!endGrid.TryGetValue(EndKey(end.X, end.Y, dx, dy), out List<KeyValuePair<int, DxfPoint2D>> bucket)) continue;
                        foreach (KeyValuePair<int, DxfPoint2D> other in bucket)
                        {
                            if (other.Key == mark.Index || (other.Key >= 0 && doomed[other.Key])) continue;
                            double ex = other.Value.X - end.X, ey = other.Value.Y - end.Y;
                            if ((ex * ex) + (ey * ey) <= tolerance2) return true;
                        }
                    }
                return false;
            }
            int Connected(CoveredMarkCandidate mark) => mark.Ends.Count(end => EndTouches(mark, end, ConnectedEndTolerance));
            // «Đầu tự do» theo dung sai nối của DXF Check (1e-3 mm): đầu cách nét kia 1e-4–1e-3 mm vẫn là chỗ nối.
            bool FreeEnd(CoveredMarkCandidate mark) => mark.Ends.Any(end => !EndTouches(mark, end, FreeEndTolerance));
            var coversCache = new Dictionary<long, bool>();
            bool CoversAll(CoveredMarkCandidate coverer, CoveredMarkCandidate target)
            {
                long key = ((long)coverer.Index << 32) | (uint)target.Index;
                if (coversCache.TryGetValue(key, out bool cached)) return cached;
                bool all = true;
                foreach (DxfPoint2D point in target.Points)
                {
                    bool near = false;
                    var probe = new DxfLineSegment2D { Start = point, End = point };
                    foreach (LaserSegment laser in index.QueryAlong(probe))
                    {
                        if (laser.Owner != coverer.Index) continue;
                        if (DistanceToSegmentSquared(point, laser.Segment) <= toleranceSquared) { near = true; break; }
                    }
                    if (!near) { all = false; break; }
                }
                coversCache[key] = all;
                return all;
            }
            bool Covered(CoveredMarkCandidate candidate)
            {
                int candidateConnected = -1;
                var otherConnected = new Dictionary<int, int>();
                HashSet<int> coverers = null;
                foreach (DxfPoint2D point in candidate.Points)
                {
                    var nearHere = new HashSet<int>();
                    var probe = new DxfLineSegment2D { Start = point, End = point };
                    foreach (LaserSegment laser in index.QueryAlong(probe))
                    {
                        if (laser.Owner == candidate.Index || doomed[laser.Owner]) continue;
                        if (coverers != null && !coverers.Contains(laser.Owner)) continue;
                        CoveredMarkCandidate other = marks[laser.Owner];
                        if (DistanceToSegmentSquared(point, laser.Segment) > toleranceSquared) continue;
                        bool longer;
                        if (Math.Abs(other.Length - candidate.Length) > OverlapTolerance)
                            longer = other.Length > candidate.Length;
                        else
                        {
                            if (candidateConnected < 0) candidateConnected = Connected(candidate);
                            if (!otherConnected.TryGetValue(other.Index, out int connected))
                                otherConnected[other.Index] = connected = Connected(other);
                            bool byLength = other.Length > candidate.Length + 1e-6 ||
                                            (Math.Abs(other.Length - candidate.Length) <= 1e-6 && other.Index < candidate.Index);
                            if (connected > candidateConnected) longer = true;
                            // Nét đang xét nối tốt hơn chỉ «thắng» khi nó PHỦ được nét kia (nét kia sẽ bị bỏ ở lượt
                            // của nó); nét kia thò ra quá dung sai thì giữ luật cũ (dài hơn thắng) — không thì cả hai
                            // cùng ở lại, laser khắc hai lần (phản biện 08/10, ca T5).
                            else if (connected < candidateConnected) longer = byLength && !CoversAll(candidate, other);
                            else longer = byLength;
                        }
                        if (longer) nearHere.Add(laser.Owner);
                    }
                    coverers = nearHere;
                    if (coverers.Count == 0) break;
                }
                return coverers != null && coverers.Count > 0;
            }

            // Nét gần như bằng 0 (cạnh 0,0015 mm ở góc ô MARK chi tiết 28/4115) là hình của model:
            // giữ — bỏ nó thì bộ nối nét (DXF Check) coi ô MARK là hở. Xét SAU CÙNG, khi các nét chồng
            // đã bỏ xong: nét tí hon nào còn một đầu TỰ DO (không chạm đầu nét MARK còn lại) là «gai»
            // thừa nằm đè lên cạnh ô — cạnh tường-tường 0,002–0,009 mm của ô MARK cắt xéo qua tấm cuộn,
            // nối vào mép xa vừa bị bỏ (v150) — bỏ không làm hở gì.
            foreach (CoveredMarkCandidate candidate in marks.OrderBy(m => m.Length).ThenBy(m => m.Index))
            {
                if (candidate.Length < PointLikeMarkLength) continue;
                if (Covered(candidate)) doomed[candidate.Index] = true;
            }
            foreach (CoveredMarkCandidate candidate in marks.Where(m => m.Length < PointLikeMarkLength).OrderBy(m => m.Length).ThenBy(m => m.Index))
            {
                if (!FreeEnd(candidate)) continue;
                if (Covered(candidate)) doomed[candidate.Index] = true;
            }

            // Xoá từ cuối lên để chỉ số không trôi; POLYLINE kéo theo VERTEX/SEQEND.
            List<CoveredMarkCandidate> removed = marks.Where(m => doomed[m.Index]).ToList();
            foreach (CoveredMarkCandidate candidate in removed.OrderByDescending(m => m.Record.Start))
            {
                int endExclusive = candidate.Record.EndExclusive;
                if (IsDxfType(candidate.Record, "POLYLINE"))
                {
                    int cursor = candidate.RecordIndex + 1;
                    while (cursor < records.Count && IsDxfType(records[cursor], "VERTEX")) cursor++;
                    if (cursor < records.Count && IsDxfType(records[cursor], "SEQEND")) cursor++;
                    endExclusive = records[cursor - 1].EndExclusive;
                }
                lines.RemoveRange(candidate.Record.Start, endExclusive - candidate.Record.Start);
            }
            return removed.Count;
        }

        private const double PointLikeMarkLength = 0.01;   // mm

        /// <summary>Đầu nét cách đầu nét khác ≤ ngần này (mm) thì KHÔNG tự do — bằng dung sai nối của DXF Check
        /// (DxfEntityJoiner 1e-3): bỏ một nét tí hon mà DXF Check coi là đã nối thì ô MARK hở (phản biện 08/10).</summary>
        private const double FreeEndTolerance = 1e-3;

        /// <summary>Ô lưới đầu nét (mm) — ≥ mọi dung sai tra cứu để chỉ cần xét 3×3 ô.</summary>
        private const double EndGridCell = 1e-3;

        /// <summary>Đỉnh đầu và đỉnh cuối (VERTEX 10/20) của POLYLINE kiểu cũ bắt đầu ở <paramref name="recordIndex"/>.</summary>
        private static IEnumerable<DxfPoint2D> PolylineVertexEnds(List<string> lines, List<DxfRecord> records, int recordIndex)
        {
            var vertices = new List<DxfPoint2D>();
            for (int cursor = recordIndex + 1; cursor < records.Count && IsDxfType(records[cursor], "VERTEX"); cursor++)
            {
                string x = GetGroupValue(lines, records[cursor].Start, records[cursor].EndExclusive, "10");
                string y = GetGroupValue(lines, records[cursor].Start, records[cursor].EndExclusive, "20");
                if (double.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double vx) &&
                    double.TryParse(y, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double vy))
                    vertices.Add(new DxfPoint2D { X = vx, Y = vy });
            }
            if (vertices.Count == 0) return vertices;
            return new[] { vertices[0], vertices[vertices.Count - 1] };
        }

        /// <summary>Hai đầu nét cách nhau ≤ ngần này (mm) là đã nối — dưới hẳn dung sai nối 0,001 mm của DXF Check.</summary>
        private const double ConnectedEndTolerance = 1e-4;

        /// <summary>
        /// Góc ô MARK hở ≤ <see cref="OverlapTolerance"/> sau bước gỡ nét chồng → nối lại. Ô MARK xuyên tấm có
        /// HAI mép (mặt trên, mặt dưới) lệch nhau vài µm trên tấm cuốn (4119 chi tiết 48/49: cạnh ngắn 2,019 /
        /// 2,003 so với 2,000). SolidWorks ghi cả hai mép; khi mép dưới được ghi trước, bước gỡ nét chồng giữ
        /// cạnh DÀI của mép dưới và cạnh NGẮN của mép trên → góc hở 0,002–0,02 mm, lớn hơn dung sai nối 0,001 mm
        /// của DXF Check → ô thành chuỗi hở, DXF Check MISMATCH. Hai đầu TỰ DO (không chạm đầu nào khác) của hai
        /// nét LASER_MARK khác nhau, gần nhau nhất của nhau và cách ≤ 0,02 mm được dồn về một điểm: hai đầu LINE
        /// về trung điểm, đầu LINE về đầu cung/đường cong (đầu cung không dời). Trả số góc đã nối.
        /// </summary>
        internal static int CloseLaserMarkCornerGaps(
            List<string> lines,
            DxfManufacturingLayerSettings settings)
        {
            if (lines == null || lines.Count < 2) return 0;
            string layerName = (settings ?? DxfManufacturingLayerSettings.CreateDefault()).LaserMark.Name;

            var ends = new List<MarkEnd>();
            foreach (DxfRecord record in CollectEntityRecords(lines))
            {
                string type = (record.Type ?? "").Trim().ToUpperInvariant();
                if (!GeometryTypes.Contains(type) || type == "CIRCLE") continue;
                string layer = GetGroupValue(lines, record.Start, record.EndExclusive, "8") ?? "0";
                if (!string.Equals(layer.Trim(), layerName, StringComparison.OrdinalIgnoreCase)) continue;
                if (type == "LINE")
                {
                    if (!TryGetLineSegment(lines, record, out DxfLineSegment2D segment)) continue;
                    ends.Add(new MarkEnd { Record = record, Point = segment.Start, XCode = "10", YCode = "20" });
                    ends.Add(new MarkEnd { Record = record, Point = segment.End, XCode = "11", YCode = "21" });
                    continue;
                }
                if (!TryTessellateEntity(lines, record, LaserChordTolerance, double.PositiveInfinity,
                        out List<List<DxfPoint2D>> polylines) ||
                    polylines.Count != 1 || polylines[0].Count < 2)
                    continue;
                DxfPoint2D first = polylines[0][0], last = polylines[0][polylines[0].Count - 1];
                if (Distance(first, last) <= ConnectedEndTolerance) continue;   // khép kín: không có đầu tự do
                ends.Add(new MarkEnd { Record = record, Point = first });
                ends.Add(new MarkEnd { Record = record, Point = last });
            }

            // Lưới ô cạnh = OverlapTolerance: mọi đầu cách ≤ 0,02 mm nằm trong 3×3 ô quanh nhau (nét khắc chữ
            // có thể có hàng nghìn đầu — không so từng cặp).
            var grid = new Dictionary<long, List<MarkEnd>>();
            long Cell(double x, double y, int dx = 0, int dy = 0) =>
                ((long)((int)Math.Floor(x / OverlapTolerance) + dx) << 32) | (uint)((int)Math.Floor(y / OverlapTolerance) + dy);
            foreach (MarkEnd end in ends)
            {
                long key = Cell(end.Point.X, end.Point.Y);
                if (!grid.TryGetValue(key, out List<MarkEnd> bucket)) grid[key] = bucket = new List<MarkEnd>();
                bucket.Add(end);
            }
            IEnumerable<MarkEnd> Around(MarkEnd end)
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (grid.TryGetValue(Cell(end.Point.X, end.Point.Y, dx, dy), out List<MarkEnd> bucket))
                            foreach (MarkEnd other in bucket)
                                if (!ReferenceEquals(other, end)) yield return other;
            }

            var free = new HashSet<MarkEnd>(ends.Where(end =>
                !Around(end).Any(other => Distance(other.Point, end.Point) <= ConnectedEndTolerance)));
            MarkEnd Nearest(MarkEnd end) => Around(end)
                .Where(other => free.Contains(other) && !ReferenceEquals(other.Record, end.Record))
                .Select(other => new { other, d = Distance(other.Point, end.Point) })
                .Where(x => x.d <= OverlapTolerance)
                .OrderBy(x => x.d)
                .Select(x => x.other)
                .FirstOrDefault();

            int closed = 0;
            var done = new HashSet<MarkEnd>();
            foreach (MarkEnd end in ends.Where(free.Contains))   // thứ tự trong file: kết quả ổn định
            {
                if (done.Contains(end)) continue;
                MarkEnd partner = Nearest(end);
                if (partner == null || done.Contains(partner) || !ReferenceEquals(Nearest(partner), end)) continue;
                DxfPoint2D target;
                if (end.Editable && partner.Editable)
                    target = new DxfPoint2D { X = (end.Point.X + partner.Point.X) / 2, Y = (end.Point.Y + partner.Point.Y) / 2 };
                else if (end.Editable) target = partner.Point;
                else if (partner.Editable) target = end.Point;
                else continue;
                foreach (MarkEnd moved in new[] { end, partner }.Where(item => item.Editable))
                {
                    SetGroupValue(lines, moved.Record.Start, moved.Record.EndExclusive, moved.XCode, FormatDxfNumber(target.X));
                    SetGroupValue(lines, moved.Record.Start, moved.Record.EndExclusive, moved.YCode, FormatDxfNumber(target.Y));
                }
                done.Add(end);
                done.Add(partner);
                closed++;
            }
            return closed;
        }

        private sealed class MarkEnd
        {
            public DxfRecord Record;
            public DxfPoint2D Point;
            /// <summary>Mã nhóm toạ độ của đầu này (LINE: 10/20 hoặc 11/21); null = đầu cung/đường cong, không dời.</summary>
            public string XCode;
            public string YCode;
            public bool Editable => XCode != null;
        }

        private sealed class CoveredMarkCandidate
        {
            public DxfRecord Record;
            public int RecordIndex;
            public int Index;
            public double Length;
            public List<DxfPoint2D> Points = new List<DxfPoint2D>();
            /// <summary>Hai đầu của từng đoạn tessellate (đầu = cuối khi nét kín).</summary>
            public List<DxfPoint2D> Ends = new List<DxfPoint2D>();
            public List<DxfLineSegment2D> Segments = new List<DxfLineSegment2D>();
        }
    }
}
