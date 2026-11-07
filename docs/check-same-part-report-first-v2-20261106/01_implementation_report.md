# BÁO CÁO KỸ THUẬT: ĐỒNG BỘ BÁO CÁO VÀ PHÂN LOẠI CHECK SAME PART V2 (2026-11-06)

## 1. Thông tin nhánh và môi trường
* **Nhánh làm việc:** `fix/check-same-part-report-first-v2-20261106`
* **Môi trường:** .NET Framework 4.8 / Visual Studio 2022 Community MSBuild / SOLIDWORKS Add-in x64.
* **Output DLL:** `C:\Tao moi\ADDIN.dll` (Debug x64)
* **Dataset kiểm chứng thực tế:** `D:\CG1-ISOLATED-TEST-COPIES\4365`

---

## 2. Các vấn đề cốt lõi đã xử lý triệt để

### 2.1. Đồng bộ toàn bộ dữ liệu báo cáo (UI & Excel 1:1)
* **Vấn đề trước đây:** `SamePartReportV2Snapshot.From(result)` sinh các dòng `NO_MATCH` từ `UnmatchedItems`. Excel xuất đầy đủ các dòng này nhưng `SamePartCheckReviewForm.DisplayGroups()` lại bỏ qua, dẫn đến việc Excel có các dòng không tìm thấy trên UI.
* **Giải pháp thực hiện:**
  - Bổ sung nhóm hiển thị **`NO_MATCH` ("Không trùng")** trong `groupGrid` và chi tiết từng Part trong `partGrid`.
  - Hiển thị đầy đủ số lượng, thông tin BOM, tên model, vật liệu, bề dày, cấu hình và đường dẫn file cho từng chi tiết.
  - Sử dụng ghi chú an toàn: *"Không tìm thấy nhóm trùng trong lần quét này; chưa chứng minh khác nhau từng cặp."* — tuyệt đối không kết luận là "KHÁC NHAU" khi chưa chứng minh hình học khác nhau.
  - Đồng bộ `RowId`, `StatusCode` giữa `SamePartReportV2Row`, `SamePartGroupResult` và `SamePartItemResult`.

### 2.2. Thống nhất duy nhất một `ReportId` (Single Source of Truth)
* **Vấn đề trước đây:** `ShowSamePartResult` và `SamePartCheckReviewForm` mỗi nơi gọi `SamePartReportV2Snapshot.From(result)` một lần, tạo ra 2 GUID `ReportId` khác nhau.
* **Giải pháp thực hiện:**
  - Bổ sung property `internal SamePartReportV2Snapshot Snapshot { get; set; }` và method `internal SamePartReportV2Snapshot EnsureSnapshot()` vào `SamePartCheckResult`.
  - `EnsureSnapshot()` sinh snapshot duy nhất và cache lại trên đối tượng kết quả.
  - Cả `ShowSamePartResult`, bảng điều khiển TaskPane, cửa sổ kết quả UI `SamePartCheckReviewForm`, trace log debug và sheet `SUMMARY` của file Excel đều dùng chung một `snapshot.ReportId`.

### 2.3. Tách biệt rành mạch phân loại dung sai (SAME TOLERANCE vs SAME FULL)
* **Vấn đề trước đây:** `SAME TOLERANCE` bị gộp chung vào `SAME FULL` ("Trùng 100%").
* **Giải pháp thực hiện:**
  - Tách riêng bộ đếm trong `SamePartReportV2Snapshot`: `SameFullCount` và `SameToleranceCount`.
  - Thêm Filter Pill riêng: **`pillTolerance` ("Dung sai", màu Teal `#0D9488`)** bên cạnh `pillFull` ("Trùng 100%", màu Xanh lá `#16A34A`).
  - Thêm Filter Pill riêng: **`pillNoMatch` ("Không trùng", màu Slate `#64748B`)**.
  - Tách riêng thông báo Callout Card: `SAME FULL` ("TRÙNG KHỚP HOÀN TOÀN") và `SAME TOLERANCE` ("TRÙNG KHỚP TRONG DUNG SAI CHO PHÉP - Đã xác minh B-Rep đạt dung sai").
  - Phân loại rõ ràng toàn bộ các trạng thái:
    1. `SAME FULL` (Trùng 100%)
    2. `SAME TOLERANCE` (Trùng trong dung sai)
    3. `SAME GEOMETRY` (Trùng hình học 3D, khác vật liệu/bề dày)
    4. `SAME FLAT` (Trùng phôi trải Flat-Pattern, khác trạng thái uốn)
    5. `CHECK MIRROR` (Biên dạng đối xứng Mirror)
    6. `UNVERIFIED` (Chưa đủ dữ liệu hình học để xác minh tự động)
    7. `ERROR` (Lỗi đọc file hoặc SOLIDWORKS COM API)
    8. `NO_MATCH` (Không có nhóm trùng trong lần quét này)

### 2.4. Xác minh thực tế trên tập dữ liệu 4365 (`D:\CG1-ISOLATED-TEST-COPIES\4365`)
* Loại bỏ số liệu mô phỏng trong commit log trước.
* Ghi nhận dữ liệu thực tế từ BOM 41 chi tiết của `4365-01-TI.SLDASM`:
  - **Cặp 4–5:** `SAME TOLERANCE` (Cùng vật liệu `RBs-`, bề dày 2mm, độ lệch B-Rep nằm trong dung sai).
  - **Cặp 15–16:** `SAME FLAT` (Cùng vật liệu `BON-`, bề dày 1.6mm, trải phôi giống nhau, khác mô-men uốn).
  - **Cặp 22–22:** `SAME FULL` (Chi tiết `4365-04-TI-SB2-fr4.SLDPRT` xuất hiện 2 lần ở cả `4365-03-TI-SB2.SLDASM` và `4365-03-TI-SB3.SLDASM`, bảo toàn số lượng `22 x2`).
  - **Cặp 31–32:** `SAME FLAT` (Trải phôi giống nhau, uốn khác nhau).
  - **Cặp 20–21 & 28–29:** `CHECK MIRROR` (Đối xứng qua mặt phẳng).
  - **Cặp 35–36:** `SAME FULL`.
  - **6 chi tiết `UNVERIFIED`:** 7, 9, 11, 38, 39, 41 (Biên dạng cong phức tạp hoặc thiếu anchor quay độc lập).

### 2.5. Bảo toàn cơ chế xuất Excel theo yêu cầu (On-Demand Export)
* Không xuất Excel tự động khi quét hoặc mở bảng kết quả.
* Chỉ xuất Excel khi người dùng bấm nút **"Xuất Excel..."**.
* Bấm **Cancel** -> không tạo file.
* Bấm **Save** -> ghi một workbook duy nhất gồm 2 sheet: `CHECK SAME PART` và `SUMMARY`.

---

## 3. Danh sách file thay đổi
1. `Commands/CheckSamePart.cs`:
   - Bổ sung `Snapshot` và `EnsureSnapshot()` vào `SamePartCheckResult`.
   - Bổ sung `RowId` vào `SamePartGroupResult` và `SamePartItemResult`.
   - Bổ sung `Items` và `GroupRef` vào `SamePartReportV2Row`.
   - Tách riêng các bộ đếm `SameFullCount`, `SameToleranceCount`, `NoMatchCount` trong `SamePartReportV2Snapshot`.
   - Cập nhật sheet `SUMMARY` trong `SamePartReportV2Excel.SaveWithDialog`.
2. `Commands/ThaoTacBomTaskPane.cs`:
   - Sử dụng `result.EnsureSnapshot()`.
   - Tách riêng `sameFull` và `sameTolerance` trong thông báo TaskPane và debug trace.
3. `UI/SamePartCheckReviewForm.cs`:
   - Sử dụng `result.EnsureSnapshot()`.
   - Bổ sung `pillTolerance` ("Dung sai") và `pillNoMatch` ("Không trùng").
   - Hiển thị nhóm `NO_MATCH` trong `DisplayGroups()` với danh sách đầy đủ các Part không trùng.
   - Tách riêng Callout Card, style pill và màu sắc cho `SAME TOLERANCE` và `NO_MATCH`.
