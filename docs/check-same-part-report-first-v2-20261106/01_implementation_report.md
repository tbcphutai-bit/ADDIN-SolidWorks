# BÁO CÁO KỸ THUẬT: ĐỒNG BỘ BÁO CÁO VÀ PHÂN LOẠI CHECK SAME PART V2 (2026-11-06)

## 1. Thông tin nhánh và môi trường
* **Nhánh làm việc:** `fix/check-same-part-report-first-v2-20261106`
* **Môi trường:** .NET Framework 4.8 / Visual Studio 2022 Community MSBuild / SOLIDWORKS Add-in x64.
* **Output DLL:** `C:\Tao moi\ADDIN.dll` (Debug x64)
* **Dataset kiểm chứng thực tế:** `D:\CG1-ISOLATED-TEST-COPIES\4365`

---

## 2. Các vấn đề cốt lõi đã xử lý triệt để

### 2.1. Chuẩn hóa nhãn `NO_MATCH` thành "Chưa tìm thấy nhóm trùng"
* **Lý do điều chỉnh:** Thuật toán chưa chứng minh các chi tiết không thuộc nhóm là khác nhau về mặt hình học đối với mọi chi tiết bên ngoài phạm vi quét. Vì vậy, việc gán nhãn "Không trùng" hoặc coi là "Khác nhau" là thiếu chính xác về mặt kỹ thuật.
* **Quy chuẩn mới:**
  - Nhãn hiển thị trên UI và Excel: **"Chưa tìm thấy nhóm trùng"** (Excel: `CHƯA TÌM THẤY NHÓM TRÙNG`).
  - Mã nội bộ (Internal StatusCode): Giữ nguyên `NO_MATCH`.
  - Ghi chú chuẩn hóa: *"Chưa tìm thấy nhóm trùng trong lần quét này; chưa chứng minh khác nhau từng cặp."*
  - Bổ sung nhóm hiển thị `NO_MATCH` trong `groupGrid` và nạp toàn bộ chi tiết vào `partGrid` khi chọn dòng này, giúp người dùng tra cứu được từng chi tiết BOM tương ứng 1:1 với file Excel xuất ra.

### 2.2. Kiểm tra và xác nhận chính xác số lượng nhóm (9 nhóm trên UI)
* **Quy tắc đếm:**
  - **7 nhóm hình học (Geometry Groups):** Gồm 7 cặp chi tiết đồng dạng phát hiện được (G1 đến G7).
  - **1 nhóm thẻ tổng hợp Chưa xác minh (UNVERIFIED):** Gom 6 chi tiết chưa đủ dữ liệu hình học tự động (BOM 7, 9, 11, 38, 39, 41) thành 1 thẻ nhóm `UNV` trên `groupGrid`.
  - **1 nhóm thẻ tổng hợp Chưa tìm thấy nhóm trùng (NO_MATCH):** Gom 21 chi tiết không thuộc nhóm trùng nào thành 1 thẻ nhóm `-` trên `groupGrid`.
  - **Tổng số nhóm hiển thị trên UI Master Grid (`groupGrid`):** $7 + 1 + 1 = 9$ **nhóm (cards)**.
  - Bộ đếm `totalCards` và nhãn `pillAll` ("Tổng") hiển thị chính xác **9**.
  - Báo cáo tài liệu trước đây ghi nhầm số 10 do lỗi đánh máy đã được đính chính hoàn toàn thành 9.

### 2.3. Thống nhất duy nhất một `ReportId` (Single Source of Truth)
* Property `internal SamePartReportV2Snapshot Snapshot { get; set; }` và method `internal SamePartReportV2Snapshot EnsureSnapshot()` trong `SamePartCheckResult` đảm bảo chỉ tạo duy nhất 1 snapshot cho mỗi lần chạy kiểm tra.
* Mã GUID `ReportId` này được dùng chung đồng nhất tại:
  1. Nhãn trạng thái `lblStatus.Text` trên TaskPane.
  2. Bảng kết quả `SamePartCheckReviewForm` (Title/Ribbon).
  3. Cửa sổ xuất Excel và Sheet `SUMMARY` của file `.xlsx`.
  4. Trace log debug `[CHECK SAME PART][REPORT_FIRST]` và `[EXCEL_CLICK]`.

### 2.4. Tách biệt rành mạch phân loại dung sai (SAME TOLERANCE vs SAME FULL)
* Phân định rõ 8 trạng thái kỹ thuật:
  1. `SAME FULL` (Trùng 100%): Filter Pill xanh lá, chi tiết hoàn toàn trùng khớp cả 3D, vật liệu, bề dày, phôi trải.
  2. `SAME TOLERANCE` (Trùng trong dung sai): Filter Pill riêng màu Teal (`#0D9488`), độ lệch hình học B-Rep nằm trong giới hạn dung sai cho phép.
  3. `SAME GEOMETRY` (Trùng hình học 3D): Khác vật liệu hoặc bề dày tôn.
  4. `SAME FLAT` (Trùng phôi trải Flat-Pattern): Biên dạng trải phẳng giống nhau, uốn/chấn khác nhau.
  5. `CHECK MIRROR` (Biên dạng đối xứng Mirror): Phản chiếu qua mặt phẳng.
  6. `UNVERIFIED` (Chưa xác minh): Filter Pill màu cam, chưa đủ anchor hoặc mặt cong uốn phức tạp.
  7. `ERROR` (Lỗi đọc file hoặc API COM): Filter Pill màu đỏ.
  8. `NO_MATCH` (Chưa tìm thấy nhóm trùng): Filter Pill màu Slate (`#64748B`).

### 2.5. Bảo toàn cơ chế xuất Excel theo yêu cầu (On-Demand Export)
* Không xuất Excel tự động khi quét hoặc mở form.
* Chỉ xuất Excel khi người dùng bấm nút **"Xuất Excel..."**.
* Bấm **Cancel** -> Không ghi file ra đĩa.
* Bấm **Save** -> Ghi đúng 1 file `.xlsx` gồm 2 sheet: `CHECK SAME PART` và `SUMMARY`.

---

## 3. Danh sách file thay đổi trong commit này
1. `Commands/CheckSamePart.cs`:
   - Cập nhật nhãn `DisplayStatus("NO_MATCH")` thành `"CHƯA TÌM THẤY NHÓM TRÙNG"`.
   - Cập nhật ghi chú `NO_MATCH` thành *"Chưa tìm thấy nhóm trùng trong lần quét này; chưa chứng minh khác nhau từng cặp."*
   - Cập nhật fallback form nhãn `CHƯA TÌM THẤY NHÓM TRÙNG`.
2. `Commands/ThaoTacBomTaskPane.cs`:
   - Cập nhật thông điệp `lblStatus.Text` dùng cụm từ `"chưa tìm thấy nhóm trùng"`.
3. `UI/SamePartCheckReviewForm.cs`:
   - Đổi nhãn `pillNoMatch` thành `"Chưa tìm thấy nhóm trùng"`.
   - Điều chỉnh chiều rộng cột Status `groupGrid.Columns[0].Width = 175` và `pillW = 165` để hiển thị trọn vẹn nhãn mà không bị cắt chữ.
   - Cập nhật nhãn dòng trong `groupGrid`, tiêu đề banner, Callout Card header & body, `FormatStatusLabel`, `FormatStatusDescription`.
4. Các tài liệu kỹ thuật trong thư mục `docs/check-same-part-report-first-v2-20261106/`:
   - `01_implementation_report.md`: Báo cáo thiết kế và chuẩn hóa.
   - `02_build_test_results.md`: Kiểm thử thực tế, phân định 4 loại kết quả.
   - `03_debug_evidence.txt`: Trích xuất log gốc cho toàn bộ 7 cặp và 6 chi tiết Unverified.
   - `04_git_diff_summary.md`: Tổng hợp git diff.
