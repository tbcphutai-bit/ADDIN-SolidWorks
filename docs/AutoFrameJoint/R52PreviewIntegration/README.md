# R52 — Kiểm tra tích hợp Preview vào Add-in

## Cập nhật mới nhất: Preview đã nhìn thấy, sửa tính lặp và Kết thúc

Ngày 2026-10-22. Draft PR #43, tiếp nối [R51 / PR #42](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/42) và [R50 / PR #41](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/41).

Người dùng xác nhận **đã nhìn thấy Preview**, còn chậm và gặp vấn đề Kết thúc. Log/DLL mới MVID `023f24dd-0f95-4cdd-9352-2629defdfab7` chứa bản sửa Caption cũ, khớp 267/267 AFJ normalized IL methods. Có 4 lần P1 solve, 8 Display3 status=0, 3 Close/AfterClose và 8 Hide end. Không có nút 106 trong log này. Cuối log process thoát `0xffffffff`; nguyên nhân vẫn UNVERIFIED.

**Source mới:** cache có kiểm tra API source state, tái sử dụng KEEP khi đổi P1/P2, bỏ redraw trung gian, giữ yêu cầu Finish khi bận và giữ session tới lúc backend đóng xong. Thêm phase timings. Chỉ sửa ba class hiện tại; không đổi R51 cutters/rules/validators hoặc A/B selection callbacks. **30/30 managed controls mỗi biến thể local/branch; bản sửa mới chưa chạy native trong Add-in.** Không kết luận đã giải quyết xong độ mượt hay exit chỉ từ harness.

Xem **[báo cáo mới nhất](08_SMOOTH_PREVIEW_AND_FINISH.md)**, [matrix CSV](SMOOTH_FINISH_MATRIX.csv), [JSON](SMOOTH_FINISH_MATRIX.json), [summary](SMOOTH_FINISH_SUMMARY.json).

## Trả lời các câu hỏi tích hợp

| Câu hỏi | Kết luận hiện tại |
| --- | --- |
| R51 và sửa Caption đã nằm trong DLL chưa? | Có, MVID trên khớp log và baseline IL; DLL/PDB khớp GUID/age. Source checksum PDB NOT_AVAILABLE. |
| Có nhận Frame B và bắt Preview không? | Có trong log người dùng, đúng A fr04 / B fr03; bốn nút 107. |
| Có gọi solver và tạo Body2 không? | Bốn SOLVER_END, keptA/B=1, removed volume dương; native log, không phải snapshot B-rep mới của Codex. |
| Display3 có chạy không? | Tám call status=0 trong log mới. |
| Preview nhìn thấy chưa? | USER_CONFIRMED_VISIBLE; xác nhận của người dùng, không suy từ viewport estimate. |
| Kết thúc đã xác minh thành công chưa? | Log có Close/Hide xong nhưng cuối process exit; 106 không xuất hiện. Bản sửa Finish mới chỉ kiểm tra managed. |
| Cache/Finish mới đã nằm trong DLL chưa? | Chưa. Local source đã cập nhật; DLL/PDB giữ nguyên, chờ chủ dự án Build thủ công. |
| Kiểm thử loại nào? | Bản Caption cũ: log Add-in thực. Follow-up cache/Finish: 30 managed controls mỗi biến thể, chưa native kernel/display/performance test. |

## Files sửa ở follow-up mới

- `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`: reuse một validated solution với API stamp/config/transform/identity guards, đổi mode chỉ thêm/ẩn CUT, timing và close latch.
- `Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs`: giữ owner khi backend bận, hoàn tất pending Close sau backend.
- `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`: giữ yêu cầu Finish, ưu tiên Close trước công việc mới, timing cleanup. Không runtime Label.Caption/Control.Enabled setter.

CommandEntry và hai helper khác không sửa trong follow-up này. Không đổi `.csproj`, DLL/PDB, output/deployment, branch/HEAD tích hợp hoặc CAD gốc; không merge. Kết thúc hiện chỉ đóng Preview, **chưa áp dụng Cut thật vào Part**.

## Hồ sơ theo thời điểm

Các báo cáo 01–07 giữ lại bằng chứng và kết luận tại thời điểm cũ; những trạng thái NOT_RUN hoặc “chưa nhìn thấy” ở đó không mô tả log mới nhất.

1. [Snapshot source / DLL ban đầu](01_SOURCE_DLL_VERIFICATION.md)
2. [Luồng Preview ban đầu](02_PREVIEW_FLOW.md)
3. [Selection diagnostics](03_SELECTION_DIAGNOSTICS.md)
4. [Display / viewport snapshot](04_DISPLAY_VISIBILITY.md)
5. [Initial tests and limits](05_TESTS_AND_LIMITS.md)
6. [Initial manual integration](06_MANUAL_INTEGRATION.md)
7. [Native Caption crash và sửa trước](07_NATIVE_LOG_AND_CAPTION_FIX.md)
8. **[Preview smooth / Finish follow-up hiện tại](08_SMOOTH_PREVIEW_AND_FINISH.md)**

[Manifest](REPORT_MANIFEST.csv), [bằng chứng mới](evidence/smooth_finish/native_log_metrics.json), [harness source](evidence/smooth_finish/SmoothFinishHarness.cs).

`BINARY_VERIFIED` / `SOURCE_VERIFIED` là đọc file thật; `USER_NATIVE_LOG` là log chạy của người dùng; `USER_CONFIRMED_VISIBLE` là xác nhận nhìn thấy; `PASS_MANAGED_CONTRACT` dùng proxy và không có hình học kernel; `NOT_RUN` / `UNVERIFIED` giữ nguyên nghĩa.
