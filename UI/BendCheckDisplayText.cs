// Presentation-only adaptation of PR #36's vocabulary.
// Never replace underlying enums, reason IDs or signed/cached source payloads.
using System;
using System.Collections.Generic;
using System.Linq;

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

    internal static class BendCheckDisplayText
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

        internal static string Collision(Commands.BendCollision.CollisionStatus value) => Collision(value.ToString()).Caption;
        internal static string Phase(Commands.BendProcess.BendProcessPhase value) => ProcessPhase(value.ToString()).Caption;
        internal static string Clearance(double? mm) => mm.HasValue ? mm.Value.ToString("0.######")+" mm" : "Chưa tính được";
        internal static string Reasons(string raw, bool technical=false)
        {
            if(technical)return raw??"";
            if(string.IsNullOrWhiteSpace(raw))return "Chưa có ghi chú.";
            return string.Join("; ",raw.Split(new[]{';','\r','\n'},StringSplitOptions.RemoveEmptyEntries)
                .Select(s=>Reason(s.Trim()).Caption).Distinct());
        }
        internal static string Coverage(Commands.BendPlanning.SequenceSearchCoverage value)
        {
            switch(value)
            {
                case Commands.BendPlanning.SequenceSearchCoverage.ExhaustiveDeclaredSpace:return "Đã xét hết phương án được cung cấp";
                case Commands.BendPlanning.SequenceSearchCoverage.Bounded:return "Chưa duyệt hết phương án";
                case Commands.BendPlanning.SequenceSearchCoverage.Interrupted:return "Kiểm tra bị gián đoạn";
                default:return "Thiếu dữ liệu để đánh giá";
            }
        }
        internal static string Evidence(Commands.BendPlanning.SequenceEvidenceLevel value) =>
            value==Commands.BendPlanning.SequenceEvidenceLevel.SimulationOnly?"Dữ liệu mô phỏng":
            value==Commands.BendPlanning.SequenceEvidenceLevel.ReviewedSourceGraph?"Đã kiểm tra nguồn quy trình":"Chưa đủ dữ liệu";

        private static BendUserText Get(Dictionary<string, BendUserText> map, string code)
        {
            BendUserText result;
            if (code != null && map.TryGetValue(code, out result)) return result;
            if(ReferenceEquals(map,reasonCodes)&&code!=null)
            {
                var extra=new Dictionary<string,string>(StringComparer.Ordinal) {
                    {"HOLDER_MATERIAL_SOLID_HOLE_REFERENCE_ROLES_UNREVIEWED","Chưa phân loại vật liệu, lỗ và đường tham khảo của đầu kẹp chày"},
                    {"PHYSICAL_SEATING_DATUM_UNVERIFIED","Chưa xác minh mốc và vị trí gá thực tế"},
                    {"MISSING_PROCESS_STATE_GRAPH_AND_KINEMATIC_SOURCE","Thiếu hình dạng phôi trung gian và dữ liệu chuyển động"},
                    {"FINAL_STATIC_PROVIDER_NOT_CONFIGURED","Chưa cấu hình hồ sơ gá để kiểm tra vị trí tĩnh"},
                    {"FINAL_STATIC_NOT_MAPPED_FOR_NATIVE_PART","Chưa gắn hồ sơ gá với chi tiết và cấu hình này"},
                    {"OPEN_OR_INVALID_PUNCH_MATERIAL","Biên dạng chày chưa kín hoặc chưa hợp lệ"},
                    {"SOURCE_STALE_OR_UNMATCHED","Nguồn dao đã thay đổi hoặc chưa nhận đúng cặp"},
                    {"CANCELLED","Đã dừng kiểm tra"},{"STALE","Dữ liệu đã thay đổi – hãy quét lại"},
                    {"STALE_SOURCE","Dữ liệu đã thay đổi – hãy quét lại"},
                    {"SOURCE_CHANGED_DURING_DISPLAY","Dữ liệu thay đổi khi hiển thị – hãy quét lại"},
                    {"PROCESS_JOB_TIME_BUDGET_EXCEEDED","Chưa kiểm tra xong trong thời gian cho phép"}
                };
                string caption;if(extra.TryGetValue(code,out caption))return new BendUserText(caption,"Xem hồ sơ dao và chi tiết kỹ thuật để bổ sung đúng dữ liệu.",code);
            }
            // Original code is retained only for technical detail/diagnostics.
            return new BendUserText("Chưa đủ dữ liệu – xem chi tiết kỹ thuật",
                "Mã này chưa có giải thích tiếng Việt. Hãy xem mã gốc trong chi tiết kỹ thuật.",
                code ?? "");
        }
    }
}
