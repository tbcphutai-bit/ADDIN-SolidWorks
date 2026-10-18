# Issue #17 — xác minh công cụ, Windows 2026-10-18

Baseline source Phase 3D / PR #15: `283a7a77a3280dda6cc36b82412608cf518cb58b`.
Branch độc lập `feature/check-chan-issue17-data-audit`.
Tài liệu này chỉ báo cáo kiểm thử công cụ bằng dữ liệu tổng hợp. Kết quả thực tế
và baseline của công ty được bàn giao riêng tại máy, không nằm trong Git history.

| Kiểm tra công cụ | Kết quả |
| --- | --- |
| CLI .NET Framework 4.8 Release/x64, Optimize=true | Build PASS; không cần SOLIDWORKS/PIA. |
| Full managed suite, Release/x64, TRACE-only, Optimize=true | **231 nhóm PASS / 0 FAIL; 8.187 assertions**. Giữ toàn bộ 215 nhóm Phase 3D, thêm 16 nhóm audit. |
| Bend Planning regression kế thừa | **24 PASS / 0 FAIL; 609 assertions**. |
| Phạm vi source runtime | Không thay `Commands/`, `UI/`, `Data/`, `ADDIN.csproj`, `SWAddin.cs`; không sửa Check Min hoặc solver. |

Các nhóm audit mới bao gồm:

- Giữ 18 slots và từng blocker khi input thiếu, theo đúng contract ID kế thừa.
- Một fixture synthetic đủ toàn bộ external contracts đạt
  `READY_PRODUCTION_STATIC_CHECK`; slots thiếu fixture vẫn UNKNOWN.
- Simulation chỉ đạt `READY_GEOMETRY_ONLY`; workbook review là blocker riêng.
- Sai source hash/requested revision/manifest dù ký hợp lệ vẫn bị từ chối.
- Wrong purpose, signature hỏng, artifact-only edit và review expiry.
- Thiếu dài Holder không mượn dài Punch; hai pose XY đã ký khác nhau bị từ chối.
- NULL axial end, sai profile/frame và duplicate setup.
- Workbook/object/value/machine/seating mismatch và clearance-example promotion.
- Saved DTO rehydration + original Phase 3D compiler; Part source hash thay đổi,
  source/model lease hết hiệu lực đều chặn readiness.
- Minimized private summary loại path/hash/coords/reviewer/artifact/free text.
- Intake unknown/duplicate identities, ledger freshness, output trong checkout
  hoặc ghi đè bị từ chối; template không sinh phép đo/approval/signature/key.

`audit_company_report.py` là checker tổng quát, không có ID hoặc trạng thái
công ty hard-code. Nó đọc 18 ID từ `CompanyToolIds.All`, so report với baseline
riêng, kiểm tra source SHA, CSV, mapping followup packet và các template chưa ký.
Không dùng công cụ tổng hợp để thay source hoặc cấp approval cho dao thực.

Không chạy lại native SOLIDWORKS hoặc product build: source runtime, production
project và giao diện không đổi; công cụ mới chỉ link managed source để chạy
offline. Không tuyên bố lại kết quả native Phase 3D là kiểm thử của PR này.
Những shape ngoài subset Phase 3D giữ unsupported. Readiness của một saved static
context không phải collision CLEAR, Check Max PASS hay thứ tự chấn khả thi.

Toàn bộ output có dữ liệu công ty, kể cả summary/baseline/CSV, ở ngoài checkout
và ngoài GitHub. PR chỉ chứa source, test tổng hợp, cấu trúc checklist và hướng dẫn.
Những mục cần người kiểm định thật trong Issue #17 tiếp tục mở.
