// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Một cạnh tròn KÍN đã nhận là MARK, đã chiếu lên sketch của view (đơn vị sketch = mét ở tỉ lệ 1:1).
    /// </summary>
    internal sealed class MarkRimCandidate
    {
        public int Index { get; set; }
        /// <summary>Feature MARK sở hữu (pattern / cut) — chỉ so các vòng của CÙNG một feature.</summary>
        public string Source { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double R { get; set; }
        /// <summary>Độ sâu dọc hướng nhìn của view (lớn = gần người nhìn); NaN = không đo được.</summary>
        public double Depth { get; set; } = double.NaN;
        /// <summary>
        /// Cạnh MIỆNG lỗ: một mặt kề KHÔNG thuộc feature MARK (mặt tấm). Cạnh trong lòng (đáy hốc khắc,
        /// chỗ thành gặp đáy) có cả hai mặt kề thuộc MARK.
        /// </summary>
        public bool IsMouth { get; set; }
        /// <summary>Khoá các mặt THÀNH (mặt cong — trụ, côn) kề cạnh này và thuộc feature MARK.</summary>
        public List<string> WallKeys { get; } = new List<string>();
    }

    /// <summary>
    /// «Lỗ có 2 vòng bao do bị cắt xéo → lấy vòng ngoài cùng».
    ///
    /// Lỗ / hốc MARK cắt xéo (thành côn do góc thoát, hoặc trục nghiêng) nhìn thẳng vào tấm thấy HAI
    /// đường tròn: miệng lỗ và vòng kia (đáy hốc hoặc miệng mặt bên kia). Cả hai đều là cạnh của feature
    /// MARK nên trước đây cả hai lên LASER_MARK → máy khắc hai vòng.
    ///
    /// Hai vòng là CÙNG MỘT LỖ khi chúng là hai mép của cùng một mặt thành (mặt cong của feature MARK) và
    /// chồng lên nhau trên hình chiếu. Chỉ xét mặt CONG: đáy phẳng của rãnh khắc hình vành khuyên nối
    /// vòng trong và vòng ngoài của một ký hiệu ◎ cố ý — hai vòng đó phải giữ nguyên.
    ///
    /// Trong một lỗ: ưu tiên cạnh MIỆNG (mặt tấm), rồi vòng LỚN nhất (vòng ngoài cùng); bằng nhau (lỗ trục
    /// nghiêng) thì vòng gần người nhìn hơn. Các vòng còn lại bị bỏ.
    /// </summary>
    internal static class MarkRimSelector
    {
        /// <summary>Dung sai so bán kính / khoảng cách (mét, sketch tỉ lệ 1:1) — 0,01 mm.</summary>
        public const double Tolerance = 1e-5;

        /// <summary>Chỉ số (<see cref="MarkRimCandidate.Index"/>) các vòng phải bỏ.</summary>
        public static HashSet<int> SelectDropped(IList<MarkRimCandidate> candidates)
        {
            var dropped = new HashSet<int>();
            if (candidates == null || candidates.Count < 2) return dropped;

            foreach (var bySource in candidates
                         .Where(c => c != null && c.R > Tolerance && !double.IsNaN(c.X) && !double.IsNaN(c.Y))
                         .GroupBy(c => c.Source ?? "", StringComparer.OrdinalIgnoreCase))
            {
                List<MarkRimCandidate> list = bySource.ToList();
                if (list.Count < 2) continue;

                var parent = Enumerable.Range(0, list.Count).ToArray();
                int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }

                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                        if (SameHole(list[i], list[j])) parent[Find(i)] = Find(j);

                foreach (var hole in Enumerable.Range(0, list.Count).GroupBy(Find))
                {
                    List<MarkRimCandidate> rims = hole.Select(k => list[k]).ToList();
                    if (rims.Count < 2) continue;
                    MarkRimCandidate keep = PickOuter(rims);
                    // Vòng chiếu TRÙNG vòng giữ (lỗ MARK thẳng xuyên tấm: vòng mặt trên và mặt dưới) không phải
                    // «vòng trong» — bỏ nó vào danh sách xoá là xoá luôn vòng giữ (4119 chi tiết 22/30/46/50/51).
                    foreach (MarkRimCandidate rim in rims)
                        if (!ReferenceEquals(rim, keep) && !Coincides(rim, keep)) dropped.Add(rim.Index);
                }
            }
            return dropped;
        }

        /// <summary>Hai vòng là hai mép của cùng một mặt thành và chồng nhau trên hình chiếu.</summary>
        internal static bool SameHole(MarkRimCandidate a, MarkRimCandidate b)
        {
            if (a.WallKeys.Count == 0 || b.WallKeys.Count == 0) return false;
            if (!a.WallKeys.Intersect(b.WallKeys, StringComparer.Ordinal).Any()) return false;
            double d = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
            return d < a.R + b.R - Tolerance;
        }

        /// <summary>Hai vòng trùng nhau trên hình chiếu (cùng tâm, cùng bán kính trong dung sai).</summary>
        internal static bool Coincides(MarkRimCandidate a, MarkRimCandidate b)
        {
            double d = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
            return d <= Tolerance && Math.Abs(a.R - b.R) <= Tolerance;
        }

        internal static MarkRimCandidate PickOuter(IList<MarkRimCandidate> rims)
        {
            IEnumerable<MarkRimCandidate> pool = rims.Any(r => r.IsMouth) ? rims.Where(r => r.IsMouth) : rims;
            MarkRimCandidate best = null;
            foreach (MarkRimCandidate r in pool)
            {
                if (best == null) { best = r; continue; }
                if (r.R > best.R + Tolerance) { best = r; continue; }
                if (r.R < best.R - Tolerance) continue;
                // Bằng nhau: lỗ trục nghiêng — giữ miệng gần người nhìn (mặt đang thấy).
                if (!double.IsNaN(r.Depth) && !double.IsNaN(best.Depth) && r.Depth > best.Depth + Tolerance) best = r;
            }
            return best;
        }
    }
}
