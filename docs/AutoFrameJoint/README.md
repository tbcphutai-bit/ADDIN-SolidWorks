# Auto Frame Joint — Phase 1: Geometry research & specification

**Trạng thái:** SPEC / RESEARCH — chưa có thuật toán cắt được chứng minh hoàn chỉnh, chưa có code thực thi.  
**Mục đích:** Tạo cơ sở kỹ thuật kiểm chứng được cho một SOLIDWORKS Add-in Command cắt giao góc hai Part độc lập trong Assembly (C#, WinForms).  
**Nguyên tắc phát triển:** Codex phụ trách hình học/backend; Antigravity chỉ thiết kế UI ở giai đoạn sau, không sửa backend/API/event handlers.

## Đọc theo thứ tự
1. [01_GEOMETRY_SPEC.md](01_GEOMETRY_SPEC.md) — bài toán hình học và hợp đồng dữ liệu.
2. [02_JOINT_RULES.md](02_JOINT_RULES.md) — quy tắc đã có chứng cứ và giả thuyết chưa xác minh.
3. [03_PROFILE_TOPOLOGY.md](03_PROFILE_TOPOLOGY.md) — nhận diện tiết diện bất kỳ.
4. [04_TEST_CASES.md](04_TEST_CASES.md) — bộ mẫu chuẩn và ca kiểm thử.
5. [05_VALIDATION_CRITERIA.md](05_VALIDATION_CRITERIA.md) — tiêu chí đạt/không đạt.
6. [06_UNRESOLVED_CASES.md](06_UNRESOLVED_CASES.md) — quyết định còn mở; không tự suy đoán.
7. [07_VS_COPILOT_HANDOFF.md](07_VS_COPILOT_HANDOFF.md) — trao đổi trực tiếp bằng GitHub Copilot Chat trong Visual Studio.

## Nguồn làm việc (chưa đưa CAD/PDF gốc vào repository)
- Bộ SOLIDWORKS/STEP: `1618a`, `3615a` (T1/T2), `2216a`, mẫu `2216-02-lc-W18.STEP`, hình ảnh Assembly `2216-02-lc-W22`.
- Bộ `2354 (4).zip`: bản vẽ lắp/trạng thái xuất hàng, Drawing/Part và các DXF (đặc biệt chi tiết 43, 47, 48, 59, 64).
- Tiêu chuẩn KCC `26003-パネルコーナー立ち上がり板厚勝ち負け方向の統一.pdf` (ngày 2026-05-15 theo tài liệu).

**Lưu ý nguồn:** Những file này được cung cấp ở hội thoại nghiên cứu; không giả định rằng chúng đã tồn tại trong checkout GitHub. Cần đăng ký đường dẫn thực + cấu hình + checksum trước khi chạy kiểm thử. Không đưa tài liệu nội bộ vào repo công khai khi chưa được phép.

## Phân loại độ tin cậy
- **STANDARD:** diễn giải trực tiếp từ tiêu chuẩn KCC 26003, có giới hạn phạm vi.
- **OBSERVED:** xu hướng quan sát được từ mẫu hình học; phải kiểm tra lại trên dữ liệu CAD.
- **HYPOTHESIS:** quy luật đề xuất chưa được chứng minh.
- **UNRESOLVED:** thiếu điều kiện để quyết định; phải dừng hoặc hỏi người dùng.

## Ranh giới của commit này
**Chỉ thêm tài liệu Markdown.** Không sửa `ADDIN.csproj`, SOLIDWORKS API, Commands, UI, handler, feature tree hay file Part. Không khẳng định tính khả thi hình học cho mọi profile. Không tạo Cut Feature hoặc tự sửa Assembly.

## Kết quả cần đạt trước Phase 2
- Tất cả quy tắc có phạm vi và bằng chứng; không trộn tiêu chuẩn với suy luận.
- Dữ liệu vào/ra, biến đổi hệ tọa độ và lý do dừng được định nghĩa.
- Có ma trận kiểm thử tái hiện được để so sánh CAD trước/sau.
- Có danh sách unresolved cần chủ thiết kế xác nhận.
