# 05 — Acceptance / validation criteria

## A. Phase 1 là documentation & research gate (không có Apply Cut)
- [ ] Tài liệu có mục tiêu, phạm vi và API-independent input/output contract.
- [ ] Mọi rule có label **STANDARD / OBSERVED / HYPOTHESIS / UNRESOLVED**.
- [ ] KCC 26003 chỉ được áp dụng trong phạm vi đã nêu; Drawing override có ưu tiên.
- [ ] Danh mục nguồn tham chiếu bao gồm 1618a, 3615a T1/T2, 2216 W18/W22, 2354 và PDF KCC.
- [ ] Không viết tài liệu nào khẳng định đã tự tạo chính xác được góc khi chưa chạy test.
- [ ] Có trạng thái `NEEDS_REVIEW` khi không có quyết định duy nhất.
- [ ] Không đổi project/build/runtime/Feature Tree trong commit Phase 1.

## B. Geometry analyzer gate (Phase 2 dự kiến)
- [ ] Chọn đúng 2 component; xử lý trạng thái suppressed/lightweight/config/instance.
- [ ] Phân tích trục, length theo local axis, không phụ thuộc X/Y/Z hoặc camera.
- [ ] Phân loại Short/Long với tolerance, `EQUAL_OR_AMBIGUOUS` nếu gần bằng.
- [ ] Tạo corner coordinate system (axes, plane, inside/outside, handedness); xử lý mirror.
- [ ] Phân loại section region từ ít nhất ba station ổn định; không nhầm holes thành profile.
- [ ] Có diagnostic rõ khi không phát hiện được section/topology.

## C. Joint solver gate (chỉ sau B)
- [ ] Sinh các mặt cắt ứng viên có dẫn chứng theo Web/Wall/Lip/Bend.
- [ ] Trong điều kiện KCC có phạm vi hợp lệ: thành winner che end-thickness loser ở vùng tương ứng.
- [ ] Không ép mọi region về một cut plane 45°; không dùng Boolean subtract toàn thân như fallback tùy tiện.
- [ ] Không cắt các gờ không có lý do/ràng buộc rõ ràng; trả `UNDETERMINED` khi có nhiều nghiệm.
- [ ] So sánh đối chiếu vết cắt trái/phải, góc không vuông và các mẫu ngoại lệ.
- [ ] Kiểm tra không giao thể tích ngoài vùng cho phép, khoảng hở và continuity, tối thiểu cả ở Web/Wall/Lip.
- [ ] Bảo toàn hình học ngoài vùng joint: holes, slots, stud, unrelated features.
- [ ] Các test so sánh geometric difference (thể tích, bề mặt, khoảng cách mép) theo tolerance được chủ thiết kế phê duyệt.

## D. Apply gate (chưa được làm trong commit này)
- [ ] Preview-only hoàn thiện, người dùng xác nhận rõ.
- [ ] Snapshot/backup và undo/rollback khi feature fail hoặc rebuild fail.
- [ ] Không sửa mọi occurrence khi Part file được dùng nhiều nơi.
- [ ] Part-context transform và body ownership rõ.
- [ ] Lưu audit trail: rule, source feature, affected part/config, tolerance, validation result.

## Các chỉ số bắt buộc trong báo cáo
`CaseID`, `Input Model/Config/Hash`, `Axis Confidence`, `Topology Confidence`, `Rule Evidence`, `Candidate Count`, `Conflict Reason`, `IntersectionVolume`, `BoundaryDeviation`, `SymmetricDifference`, `PreservedRegions`, `ElapsedTime`.

Các phép đo có thể `null` nếu chưa chạy. Không biến kết quả thiếu dữ liệu thành 0.

## Các trạng thái chuẩn
- `PASS`: đủ bằng chứng và ngưỡng đã xác nhận.
- `FAIL`: đo được vi phạm điều kiện cụ thể.
- `INCONCLUSIVE`: không đủ dữ liệu/chưa duy nhất.
- `NOT_RUN`: chưa chạy test.
- `UNSUPPORTED`: hình học/hạ tầng không được hỗ trợ hiện tại.

**Quy tắc go/no-go:** Không bắt đầu Apply Cut cho đến khi những case đại diện xác minh bằng hình học đạt ngưỡng được ký xác nhận, và những case chưa có quy tắc đều có nhánh `NEEDS_REVIEW`.
