# Tích hợp và kiểm tra thủ công

## Source đã áp dụng

Bản local được patch từng method, không chép toàn bộ class của branch PR #42. Baseline local có nhãn P1/P2, vị trí helper và một số chi tiết khác với branch; các khác biệt không liên quan được giữ nguyên. Bốn patch local và hash tương ứng ở `evidence/local_*.patch` / `SOURCE_PATCH_AUDIT.json`. **Đã áp dụng local, không apply lại.** Backup byte gốc giữ private.

Patch xuất bản là zero-context unified diff; Git cần `--unidiff-zero` khi kiểm tra/apply trên đúng baseline. Hash trước/sau là cơ sở xác định baseline, không dựa riêng tên class.

R52 branch dựa trên commit `450f79b9ef34261ef8d8e8b68c6282e8f25c5712` của PR #42. Production diff gồm bốn class đang có; Entry/Session chỉ thêm log. Không command hoặc Preview engine thứ hai, không thêm Compile item/project change.

Tác vụ không Build/Rebuild/F5/Deploy, không đổi COM registration/output, không Merge. DLL giữ MVID R51 hiện tại. Source chỉnh sửa không thay thế code đã nằm trong DLL hoặc đã load vào SOLIDWORKS.

## Lượt kiểm tra native sau khi Code Owner review

Dùng quy trình Build thủ công hiện có của người dùng, đúng cấu hình/output đã đối chiếu. Không lấy `R52Managed_*.exe` làm Add-in, không copy binary harness vào output. Mở phiên SOLIDWORKS phù hợp và Assembly như workflow hiện có.

1. Mở command Auto Frame Joint hiện tại. Log phải có `stage=COMMAND_OPEN`, `stage=SHOW source=R52`, MVID thực và callback Preview đã bind. Nếu chỉ có R50/R51 marker mà không R52, chưa xác minh Build đã chứa patch.
2. Chọn A, ghi submit/list/read mark1. Kiểm tra `AUTO_FOCUS_END B_focus=...`; sau đó chọn B và ghi đủ submit/list/read mark2. Auto-focus không phải B commit.
3. Chỉ khi READY=True với hai occurrence khác nhau, nhấn nút 107 rồi 108. Theo dõi BUTTON → ACTION → PREVIEW_CALLBACK → ENTRY → SESSION → GHOST → SOLVER → DISPLAY3 → REDRAW. Mỗi chỗ reject có reason, không ép bỏ gate.
4. Kiểm tra hai Body sau cắt thực và màu/vị trí +120 mm; đối chiếu viewport log và quan sát. Có SOLVER_END/Display3 status=0 chưa đủ gán PASS hiển thị.
5. Đóng trang bằng workflow cũ. Kiểm tra epoch/drop stale work, `AFTER_CLOSE`, Hide/cleanup; lưu log liền mạch và event nếu process thoát. Không dùng catch để tiếp tục phiên đã báo lỗi bộ nhớ native.

Kết quả cần trả cho Code Owner: MVID và DLL hash sau Build, loại selection/thứ tự A/B, mốc đầu tiên thiếu hoặc reject reason, mode 107/108, Body/Display3/redraw status, ảnh hoặc xác nhận Preview nhìn thấy và trạng thái cleanup. Không cần gửi CAD gốc lên GitHub.
