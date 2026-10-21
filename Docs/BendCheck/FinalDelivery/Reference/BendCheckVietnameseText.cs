// C# 7.3/.NET Framework 4.8, reference-only presentation vocabulary.
// Not compiled into product; Codex must test and integrate with UI.
// Never replace underlying enums, reason IDs or signed/cached source payloads.
using System;
using System.Collections.Generic;

namespace ADDIN.UI
{
    internal sealed class BendUserText
    {
        public string Caption { get; private set; }
        public string Explanation { get; private set; }
        public string OriginalCode { get; private set; }
        public BendUserText(string caption, string explanation, string originalCode)
        {
            Caption = caption;
            Explanation = explanation;
            OriginalCode = originalCode;
        }
    }

    internal static class BendCheckVietnameseText
    {
        // Keys are exact existing enum.ToString() values. Unknown keys never
        // fall back to CLEAR, MeetsMinimum, Reviewed or manufacturing approval.
        private static readonly Dictionary<string, BendUserText> statuses =
            new Dictionary<string, BendUserText>(StringComparer.Ordinal)
        {
            {"CLEAR", new BendUserText("Chưa phát hiện va chạm",
                "Chỉ trong phạm vi dữ liệu đã được kiểm tra. Chưa xác minh điều kiện sản xuất.", "CLEAR")},
            {"COLLISION", new BendUserText("Phát hiện va chạm",
                "Va chạm của phương án dao/trạng thái cụ thể đã có bằng chứng; chưa kết luận cả Part.", "COLLISION")},
            {"UNKNOWN", new BendUserText("Chưa đủ dữ liệu",
                "Chưa thể xác định có hoặc không có va chạm. Xem lý do thiếu dữ liệu.", "UNKNOWN")},
            {"MeetsMinimum", new BendUserText("Cạnh chấn đạt tối thiểu",
                "Chỉ áp dụng cho kiểm tra chiều dài cạnh chấn tối thiểu.", "MeetsMinimum")},
            {"BelowMinimum", new BendUserText("Cạnh chấn không đạt tối thiểu",
                "Xem kích thước thực và giới hạn tương ứng A/B/C.", "BelowMinimum")},
            {"InsufficientData", new BendUserText("Thiếu dữ liệu kiểm tra cạnh chấn",
                "Không được coi là cạnh chấn đạt hay không đạt.", "InsufficientData")},
            {"ProvenInCheckedScope", new BendUserText("Có phương án đạt trong phạm vi đã kiểm tra",
                "Không phải chứng nhận đủ điều kiện sản xuất.", "ProvenInCheckedScope")},
            {"NeedsEvidence", new BendUserText("Thiếu dữ liệu để đánh giá",
                "Chưa có kết luận về phương án dao này.", "NeedsEvidence")},
            {"RejectedInDeclaredConditions", new BendUserText("Không phù hợp với điều kiện đã xét",
                "Không có nghĩa chi tiết không thể gia công trong mọi điều kiện.", "RejectedInDeclaredConditions")}
        };

        private static readonly Dictionary<string, BendUserText> phases =
            new Dictionary<string, BendUserText>(StringComparer.Ordinal)
        {
            {"ApproachLoad", new BendUserText("Đưa phôi vào vị trí", "Giai đoạn trước khi chày bắt đầu tạo góc.", "ApproachLoad")},
            {"PreBend", new BendUserText("Trước khi chấn", "Giai đoạn tiếp cận, trước khi tạo góc.", "PreBend")},
            {"DuringBend", new BendUserText("Trong khi chấn", "Giai đoạn biến dạng theo chuyển động đã được khai báo.", "DuringBend")},
            {"FormingEnd", new BendUserText("Kết thúc tạo góc chấn", "Trạng thái cuối của bước tạo góc.", "FormingEnd")},
            {"RetractUnload", new BendUserText("Rút chày và lấy phôi ra", "Giai đoạn rút chày/thoát phôi.", "RetractUnload")}
        };

        private static readonly Dictionary<string, BendUserText> reasonCodes =
            new Dictionary<string, BendUserText>(StringComparer.Ordinal)
        {
            {"TOOL_AXIAL_EXTENT_UNVERIFIED", new BendUserText(
                "Thiếu chiều dài làm việc của dao",
                "Chưa có chiều dài hữu dụng và vị trí dọc trục được xác minh cho bộ dao.", "TOOL_AXIAL_EXTENT_UNVERIFIED")},
            {"PROCESS_PROVIDER_NOT_CONFIGURED", new BendUserText(
                "Chưa cấu hình dữ liệu thứ tự chấn",
                "Cần dữ liệu quy trình có nguồn rõ ràng để đánh giá thứ tự.", "PROCESS_PROVIDER_NOT_CONFIGURED")},
            {"NO_STATE_INFERENCE_FROM_FINAL_MODEL", new BendUserText(
                "Thiếu hình dạng phôi giữa các lần chấn",
                "Không suy ra trạng thái trung gian chỉ từ Part đã chấn xong.", "NO_STATE_INFERENCE_FROM_FINAL_MODEL")},
            {"UNKNOWN_MACHINE_MOTION", new BendUserText(
                "Thiếu dữ liệu chuyển động của máy",
                "Chưa có chuyển động phù hợp để kiểm tra toàn bộ quá trình.", "UNKNOWN_MACHINE_MOTION")},
            {"UNKNOWN_PROCESS_GEOMETRY", new BendUserText(
                "Thiếu hình học quy trình chấn",
                "Chưa có các trạng thái hình học cần thiết để tính va chạm.", "UNKNOWN_PROCESS_GEOMETRY")},
            {"SOURCE_INVALIDATED", new BendUserText(
                "Dữ liệu đã thay đổi – cần quét lại",
                "Không sử dụng kết quả cũ sau khi nguồn thay đổi.", "SOURCE_INVALIDATED")},
            {"MISSING_CAD_SLOT_ASSOCIATION", new BendUserText(
                "Chưa xác nhận cặp chày và đầu kẹp trong CAD",
                "Cần ánh xạ cố định theo nguồn CAD, không chọn đầu kẹp gần nhất.", "MISSING_CAD_SLOT_ASSOCIATION")},
            {"OPEN_UNSUPPORTED_OR_INVALID_CAD_PUNCH", new BendUserText(
                "Biên dạng chày chưa kín hoặc chưa dùng được",
                "Không tự đóng đường hoặc biến hình học không hợp lệ thành vật liệu.", "OPEN_UNSUPPORTED_OR_INVALID_CAD_PUNCH")},
            {"FIXED_CAD_PAIR_REQUIRES_SOURCE_BOUND_SIMULATION_GEOMETRY", new BendUserText(
                "Chưa có vị trí mô phỏng gắn với cặp dao CAD",
                "Chỉ dùng vị trí mô phỏng có nguồn và phạm vi xác minh rõ ràng.", "FIXED_CAD_PAIR_REQUIRES_SOURCE_BOUND_SIMULATION_GEOMETRY")},
            {"HOLDER_MATERIAL_ROLES_UNREVIEWED", new BendUserText(
                "Chưa xác định đường bao vật liệu của đầu kẹp",
                "Cần phân loại đường bao, lỗ, đường tham chiếu theo CAD trước khi kiểm tra.", "HOLDER_MATERIAL_ROLES_UNREVIEWED")}
        };

        internal static BendUserText Collision(string code) { return Get(statuses, code); }
        internal static BendUserText CheckMin(string code) { return Get(statuses, code); }
        internal static BendUserText SequenceDecision(string code) { return Get(statuses, code); }
        internal static BendUserText ProcessPhase(string code) { return Get(phases, code); }
        internal static BendUserText Reason(string code) { return Get(reasonCodes, code); }

        private static BendUserText Get(Dictionary<string, BendUserText> map, string code)
        {
            BendUserText result;
            if (code != null && map.TryGetValue(code, out result)) return result;
            // Original code is retained only for technical detail/diagnostics.
            return new BendUserText("Chưa đủ dữ liệu – xem chi tiết kỹ thuật",
                "Mã này chưa có giải thích tiếng Việt. Hãy xem mã gốc trong chi tiết kỹ thuật.",
                code ?? "");
        }
    }
}
