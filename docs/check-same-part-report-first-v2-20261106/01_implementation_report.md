# BÁO CÁO THỰC THI BẢN SỬA CHECK SAME PART REPORT-FIRST V2 (2026-11-06)

## 1. Thông tin nhánh và môi trường
* **Nhánh cơ sở (Base branch):** `fix/check-same-part-safe-geometry-20261106` (kế thừa commit `a1527f5` và `c6640fb` chứa `SAFE-GEOMETRY-20261106`).
* **Nhánh làm việc mới (Dedicated branch):** `fix/check-same-part-report-first-v2-20261106`
* **Commit gốc:** `c6640fb` - *feat(same-part): apply SAFE-GEOMETRY-20261106 B-Rep verifier patch*
* **Môi trường:** .NET Framework 4.8 / Visual Studio 2022 MSBuild / SOLIDWORKS Add-in x64.

---

## 2. Danh sách file thay đổi
1. `Commands/CheckSamePart.cs`:
   - Bổ sung property `OutputDirectory` cho `SamePartCheckResult`.
   - Bổ sung `UnmatchedItems` cho `SamePartCheckResult`.
   - Tích hợp `SamePartReportV2Row`, `SamePartReportV2Snapshot`, `SamePartReportV2Excel`, và `SamePartReportV2Form` (fallback).
   - Xử lý xung đột namespace `Environment.NewLine` thành `System.Environment.NewLine` tránh mơ hồ với `SolidWorks.Interop.sldworks.Environment`.
2. `Commands/ThaoTacBomTaskPane.cs`:
   - Gán `result.OutputDirectory = excelOutputDirectory;` khi chạy xong runner.
   - **Xóa bỏ hoàn toàn** lệnh gọi tự động `ExcelSamePartExporter.Export(...)` trong `ShowSamePartResult`.
   - Cập nhật thông báo trạng thái `lblStatus.Text` hiển thị số nhóm, số mục chưa xác minh, số mục lỗi và nhắc người dùng bấm nút xuất Excel trong bảng báo cáo.
3. `UI/SamePartCheckReviewIntegration.cs`:
   - Truyền `excelDir` (`result.OutputDirectory`) vào constructor của form `SamePartCheckReviewForm`.
4. `UI/SamePartCheckReviewForm.cs`:
   - **Giữ nguyên 100% giao diện SaaS Inspector Ribbon hiện đại** đã xây dựng (Style C: 3D Hexagonal branding, Master-Detail Workbench, Dark Navy banner, Callout Card).
   - Tích hợp `SamePartReportV2Snapshot.From(result)` làm nguồn dữ liệu chuẩn duy nhất (Single Source of Truth) cho cả UI và Excel.
   - Đổi nút `report` ("Mở báo cáo") thành nút **`btnExportExcel` ("Xuất Excel...")** với sự kiện `SamePartReportV2Excel.SaveWithDialog(reportSnapshot, this, excelOutputDirectory, out path)`.
   - Tách riêng filter pill **`pillUnverified` ("Chưa xác minh")** màu cam và **`pillError` ("Lỗi")** màu đỏ, phân biệt rõ ràng giữa thiếu dữ liệu hình học và lỗi hệ thống.
   - Hiển thị danh sách BOM trùng lặp (ví dụ chi tiết 22 xuất hiện 2 lần được ghi `22 x2`) trên lưới và trong callout.
   - Hỗ trợ xem chi tiết và giải thích cụ thể cho trường hợp `UNVERIFIED` và `ERROR` trong Callout Card.

---

## 3. Cách thức chặn tự động xuất Excel (No Auto-Export)
* Trước đây: `ThaoTacBomTaskPane.ShowSamePartResult` gọi trực tiếp `ExcelSamePartExporter.Export(...)` ngay khi Runner hoàn thành. Điều này khiến mỗi lần chạy kiểm tra đều tự động mở COM Excel và sinh file `.xlsx` trên đĩa.
* Bây giờ:
  1. Trong `ShowSamePartResult`: Loại bỏ hoàn toàn khối `ExcelSamePartExporter.Export`. Không có file `.xlsx` nào được tạo tự động.
  2. Bảng kết quả `SamePartCheckReviewForm` được mở trực tiếp ngay sau khi quét xong.
  3. Excel **CHỈ ĐƯỢC XUẤT** khi người dùng chủ động nhấn nút **"Xuất Excel..."** trên thanh công cụ của cửa sổ kết quả.
  4. Trình xuất file mở `SaveFileDialog`. Nếu người dùng nhấn **Cancel**, không có file nào được ghi. Nếu nhấn **Save**, file được xuất dựa trên `SamePartReportV2Snapshot` đã chụp, có kèm sheet `SUMMARY` chứa `ReportId` khớp tuyệt đối với dữ liệu đang hiển thị trên UI.

---

## 4. Cơ chế phân loại: CHƯA XÁC MINH vs LỖI HỆ THỐNG
* Sử dụng `SamePartReportV2Snapshot.ClassifyFailure(string message)`:
  * **CHƯA XÁC MINH (`UNVERIFIED` - Màu cam):**
    Áp dụng cho các trường hợp:
    - `KHONG DU DU LIEU HINH HOC`
    - `INSUFFICIENT ROTATION-INDEPENDENT ANCHORS`
    - `UNSUPPORTED CURVED B-REP`
    - `MANUAL REVIEW`
    - `NO VALID ALIGNMENT`
    - `NOT VERIFIED`
    - `INCONCLUSIVE`
    Đây là những chi tiết có hình học cong phức tạp hoặc chưa đủ anchor để so khớp tự động an toàn. Hệ thống từ chối kết luận giống nhau nhưng **KHÔNG gán nhãn là lỗi hệ thống** và **KHÔNG tự ý coi là chi tiết khác nhau**.
  * **LỖI HỆ THỐNG (`ERROR` - Màu đỏ):**
    Áp dụng cho các ngoại lệ đọc file, lỗi OpenDoc6, lỗi COM API SOLIDWORKS.
