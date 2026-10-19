# 06 — Unresolved design questions & decision log

## Nguyên tắc quản lý
Các câu hỏi dưới đây không phải lỗi của CAD API. Chúng là **quyết định thiết kế** chưa thể suy ra duy nhất từ hai solid đã cắt hoàn thiện. Không cho solver tự đặt quy tắc mà không gắn `HYPOTHESIS` hoặc `NEEDS_REVIEW`.

| ID | Vấn đề | Tác động | Cách giải quyết dự kiến | Trạng thái |
|---|---|---|---|---|
| Q01 | Tiêu chuẩn nào xác định **gờ nào giữ chìa**? | Boolean clean-fit có thể cắt nhầm | Đối chiếu vùng lip trong nhiều profile + bản vẽ chế tạo | OPEN |
| Q02 | Khi Web hai frame có bề rộng/offset khác nhau, đường vát theo chuẩn nào? | Vát phân giác có thể sai | Suy điều kiện from outside/inside datums | OPEN |
| Q03 | Bend R giao tại thành thắng/thua xử lý đến đâu? | Tạo lỗ nhỏ hoặc overlap | 3D region comparison + manufacturing tolerances | OPEN |
| Q04 | Phạm vi winner/loser cho góc không vuông, khung tam giác, chữ T? | Dùng nhầm KCC 26003 | Cần tiêu chuẩn khác hoặc người thiết kế xác nhận | OPEN |
| Q05 | Khi panel vuông, chọn hướng thắng nào? | Hai phương án hợp lệ | Convention cố định trong dự án hoặc Drawing override | OPEN |
| Q06 | Nếu hai chiều dài gần nhau, ngưỡng equal là bao nhiêu? | Phân loại thiếu ổn định | Chủ thiết kế phê duyệt threshold + datum | OPEN |
| Q07 | Chọn datum inside/outside thế nào khi không có tấm panel? | Hướng cắt/giữ gờ bị đảo | Detect or require user-picked reference | OPEN |
| Q08 | Hai Part khác instance nhưng dùng chung 1 SLDPRT, lưu cut vào đâu? | Sửa lan sang assembly khác | Config-specific hoặc make independent được chấp thuận | OPEN |
| Q09 | Trường hợp frame cong, tapered, section không đồng nhất? | 'Extrude section' không hợp lệ | Giai đoạn đầu UNSUPPORTED | OPEN |
| Q10 | Các mẫu CAD nội bộ có được đưa vào repo public/test CI? | Bảo mật, quyền sở hữu | Cần người sở hữu phê duyệt | OPEN |

## Những điều **không** chốt vội
- `Short Frame thắng` không đồng nghĩa cắt phần Web của Long Frame theo plane 45°.
- Một gờ không giao thoa vẫn có thể cần cắt vì quy định lắp/thi công.
- Một mô hình không overlap không chứng minh joint đúng về thẩm mỹ, hướng che thickness, hay chế tạo.
- Hình STEP cuối cùng không đủ phân biệt trình tự/feature lịch sử của Part.
- Không dùng góc nhìn ảnh 2D làm bằng chứng chính về sở hữu vùng thành đứng.

## Quy trình chốt mỗi unresolved
1. Ghi nguồn gốc ảnh/STEP/PDF và Part/instance.
2. Liệt kê **ít nhất hai** phương án cắt còn hợp lệ từ hình học.
3. So chúng với bộ mẫu chuẩn, highlight vùng khác biệt.
4. Nếu model không phân biệt được -> hỏi người có thẩm quyền và lưu quyết định kèm phạm vi.
5. Đưa quyết định có chứng cứ vào `02_JOINT_RULES.md` + cập nhật test `04_TEST_CASES.md`.
6. Commit thay đổi riêng, không sửa retroactive mọi tiêu chí cũ khi chưa review.

## Decision record template
```text
DecisionID:
RelatedQuestions:
ApplicableProfileGeometry:
ApplicableJointType:
ReferenceDrawing/Standard:
ChosenRule:
Counterexamples:
VerifiedOnCases:
ApprovedBy/Date:
ImplementationStatus:
```
