# TỔNG HỢP GIT DIFF VÀ CÁC THAY ĐỔI MÃ NGUỒN

## 1. Git diff --stat
```text
 Commands/CheckSamePart.cs            | 384 ++++++++++++++++++++++++++++++++++-
 Commands/ThaoTacBomTaskPane.cs       |  36 ++--
 UI/SamePartCheckReviewForm.cs        | 177 ++++++++++++----
 UI/SamePartCheckReviewIntegration.cs |   7 +-
 4 files changed, 537 insertions(+), 67 deletions(-)
```

## 2. Chi tiết các hàm và lớp đã chỉnh sửa

### `Commands/CheckSamePart.cs`
* **Lớp `SamePartCheckResult`**:
  - Bổ sung `public string OutputDirectory { get; set; }` để chuyển tiếp thư mục làm việc của tài liệu SOLIDWORKS sang form kết quả.
  - Bổ sung `public List<SamePartItemResult> UnmatchedItems { get; }` ghi nhận các mục không thuộc nhóm trùng nào.
* **Tích hợp `SamePartReportV2.inc.cs`**:
  - `SamePartReportV2Row`: Mô hình dòng dữ liệu thống nhất cho cả bảng hiển thị và file Excel.
  - `SamePartReportV2Snapshot`: Đảm bảo tính nhất quán (Single Source of Truth), gán `ReportId` duy nhất.
  - `SamePartReportV2Snapshot.ClassifyFailure`: Phân định rõ ràng giữa `UNVERIFIED` (thiếu dữ liệu hình học, thuật toán từ chối an toàn) và `ERROR` (ngoại lệ, lỗi API).
  - `SamePartReportV2Excel.SaveWithDialog`: Hàm xuất Excel theo yêu cầu (chỉ kích hoạt khi người dùng bấm nút và chọn file).
  - Khắc phục xung đột `Environment.NewLine` thành `System.Environment.NewLine` tránh mơ hồ với `SolidWorks.Interop.sldworks.Environment`.

### `Commands/ThaoTacBomTaskPane.cs`
* **Hàm `CheckSamePart(SamePartToleranceOptions toleranceOptions)`**:
  - Gán `result.OutputDirectory = excelOutputDirectory;`.
* **Hàm `ShowSamePartResult(SamePartCheckResult result, string excelOutputDirectory)`**:
  - Loại bỏ hoàn toàn khối `ExcelSamePartExporter.Export(...)` tự động.
  - Cập nhật thông báo thanh trạng thái `lblStatus.Text` dựa trên `SamePartReportV2Snapshot`.

### `UI/SamePartCheckReviewIntegration.cs`
* **Hàm `ShowSamePartCheckReview(SamePartCheckResult result)`**:
  - Truyền `excelDir = result.OutputDirectory` vào `SamePartCheckReviewForm`.

### `UI/SamePartCheckReviewForm.cs`
* **Constructor**:
  - Nhận thêm `excelDir`, khởi tạo `reportSnapshot = SamePartReportV2Snapshot.From(result)`.
* **Bộ lọc và nút thống kê (Filter Pills)**:
  - Tách biệt `pillUnverified` ("Chưa xác minh", màu cam) và `pillError` ("Lỗi", màu đỏ).
  - Cập nhật số lượng và trạng thái lọc tương ứng.
* **Nút hành động Ribbon**:
  - Chuyển nút `report` ("Mở báo cáo") thành nút `btnExportExcel` ("Xuất Excel...").
  - Gắn sự kiện `btnExportExcel_Click` gọi `SamePartReportV2Excel.SaveWithDialog`.
* **Hàm `DisplayGroups()` và `FormatBuhinList()`**:
  - Giữ lại số lượng BOM trùng lặp (`22 x2`).
  - Hiển thị nhóm ảo `UNV` khi có chi tiết chưa xác minh và `ERR` khi có chi tiết lỗi.
* **Hàm `UpdateDifferenceCallout()`**:
  - Bổ sung giải thích chi tiết cho `UNVERIFIED`, `ERROR`, `SAME TOLERANCE`, `SAME FLAT`, `SAME GEOMETRY`, `SAME FULL`, `CHECK MIRROR`.
* **Giữ nguyên 100% các nút điều hướng 3D**:
  - `btnOpenPart` ("Mở Part 3D")
  - `btnOpenAssembly` ("Mở Assembly")
  - `btnOpenFolder` ("Mở thư mục")
  - Sự kiện double-click trên dòng chi tiết mở Part.
