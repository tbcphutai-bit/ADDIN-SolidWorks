// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>Một view trải phẳng ứng viên trên bản vẽ (đọc từ SolidWorks, không giữ COM).</summary>
    internal sealed class FlatViewCandidate
    {
        public string SheetName = "";
        public string ViewName = "";
        /// <summary>Thứ tự gặp trên bản vẽ (sheet rồi view).</summary>
        public int Order;
        /// <summary>View tham chiếu ĐÚNG part đang xuất (null: không biết đường dẫn để so).</summary>
        public bool? ReferencesPart;
        public bool Visible = true;
        /// <summary>Tâm khung view nằm trong khổ giấy của sheet chứa nó.</summary>
        public bool CenterInsideSheet;
        /// <summary>Cả khung view nằm trong khổ giấy.</summary>
        public bool FullyInsideSheet;
        /// <summary>Cấu hình tham chiếu là cấu hình trải phẳng của cấu hình cần xuất (null: không biết).</summary>
        public bool? ConfigurationMatches;
        /// <summary>Diện tích khung view trên giấy (m²).</summary>
        public double PaperArea;
        public string ReferencedConfiguration = "";
    }

    /// <summary>Kết quả chọn view trải phẳng để xuất DXF.</summary>
    internal sealed class FlatViewChoice
    {
        public FlatViewCandidate Chosen;
        public List<FlatViewCandidate> Candidates = new List<FlatViewCandidate>();
        /// <summary>Cảnh báo cho người dùng (rỗng: chọn chắc chắn).</summary>
        public List<string> Warnings = new List<string>();
        public string Reason = "";
    }

    /// <summary>
    /// Chọn view trải phẳng để xuất DXF («tấm flatten trong khung bản vẽ»). Đo trên 35 bản vẽ thật của bộ
    /// 4115 (06/10/2026): mỗi bản vẽ có 1 view trải phẳng HIỆN trong khổ giấy (view cần xuất), 1 view
    /// «展開» ẨN ngoài khổ (tỉ lệ 0,1) và ~13 view palette ẩn ở toạ độ âm. Luật cũ «view trải phẳng
    /// đầu tiên có tâm trong khổ của sheet đang mở» không xét view ẩn, không đối chiếu part, bỏ qua
    /// sheet khác và chọn bừa khi có nhiều ứng viên. Thứ tự ưu tiên (từ trên xuống, so lần lượt):
    /// đúng part → đang hiện → tâm trong khổ → cả khung trong khổ → đúng cấu hình → khung lớn nhất trên
    /// giấy → gặp trước. Thuần, có test.
    /// </summary>
    internal static class FlatPatternViewSelector
    {
        internal static FlatViewChoice ChooseForBom(IEnumerable<FlatViewCandidate> candidates)
        {
            return Choose((candidates ?? Enumerable.Empty<FlatViewCandidate>())
                .Where(c => c != null && c.ReferencesPart == true && c.ConfigurationMatches != false));
        }

        public static FlatViewChoice Choose(IEnumerable<FlatViewCandidate> candidates)
        {
            var choice = new FlatViewChoice();
            choice.Candidates.AddRange((candidates ?? Enumerable.Empty<FlatViewCandidate>()).Where(c => c != null));
            if (choice.Candidates.Count == 0)
            {
                choice.Reason = "bản vẽ không có view trải phẳng";
                return choice;
            }

            List<FlatViewCandidate> ranked = choice.Candidates
                .OrderByDescending(c => c.ReferencesPart != false)
                .ThenByDescending(c => c.Visible)
                .ThenByDescending(c => c.CenterInsideSheet)
                .ThenByDescending(c => c.FullyInsideSheet)
                .ThenByDescending(c => c.ConfigurationMatches != false)
                .ThenByDescending(c => c.PaperArea)
                .ThenBy(c => c.Order)
                .ToList();
            FlatViewCandidate chosen = ranked[0];
            choice.Chosen = chosen;

            var why = new List<string>();
            if (chosen.ReferencesPart == true) why.Add("đúng part");
            why.Add(chosen.Visible ? "đang hiện" : "ĐANG ẨN");
            why.Add(chosen.CenterInsideSheet ? "trong khổ giấy" : "NGOÀI khổ giấy");
            if (chosen.ConfigurationMatches == true) why.Add("đúng cấu hình");
            choice.Reason = "chọn '" + chosen.ViewName + "' (sheet '" + chosen.SheetName + "', " + string.Join(", ", why) +
                            ") trong " + choice.Candidates.Count + " view trải phẳng";

            if (chosen.ReferencesPart == false)
                choice.Warnings.Add("Không view trải phẳng nào tham chiếu đúng part — xuất '" + chosen.ViewName + "' (part khác).");
            if (!chosen.Visible)
                choice.Warnings.Add("Không có view trải phẳng nào đang HIỆN — xuất view ẩn '" + chosen.ViewName + "'.");
            if (!chosen.CenterInsideSheet)
                choice.Warnings.Add("Không view trải phẳng nào nằm trong khổ giấy — xuất '" + chosen.ViewName + "'.");
            if (chosen.ConfigurationMatches == false)
                choice.Warnings.Add("View '" + chosen.ViewName + "' tham chiếu cấu hình '" + chosen.ReferencedConfiguration +
                                    "', không phải cấu hình trải phẳng của dòng BOM.");

            // Ứng viên ngang hạng (cùng mọi tiêu chí trừ diện tích/thứ tự) → người dùng nên biết đã chọn cái nào.
            int peers = ranked.Count(c => c != chosen &&
                                          (c.ReferencesPart != false) == (chosen.ReferencesPart != false) &&
                                          c.Visible == chosen.Visible &&
                                          c.CenterInsideSheet == chosen.CenterInsideSheet &&
                                          c.FullyInsideSheet == chosen.FullyInsideSheet &&
                                          (c.ConfigurationMatches != false) == (chosen.ConfigurationMatches != false));
            if (peers > 0)
            {
                choice.Warnings.Add("Bản vẽ có " + (peers + 1) + " view trải phẳng đang hiện trong khổ giấy — xuất '" +
                                    chosen.ViewName + "' (khung lớn nhất). Kiểm tra lại nếu cần view khác.");
            }
            return choice;
        }
    }
}
