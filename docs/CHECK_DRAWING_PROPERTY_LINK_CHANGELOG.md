# BÁO CÁO THAY ĐỔI KỸ THUẬT: CHECK DRAWING PROPERTY LINK REPORT & EXCEL EXPORT

> **Ngày thực hiện:** 06/11/2026  
> **Repository:** `tbcphutai-bit/ADDIN-SolidWorks`  
> **Branch triển khai:** `feat/goban-link-report-implementation-20261106`  
> **Base branch:** `feat/check-drawing-property-link-report-preview-20261105`  
> **Tác giả logic & thiết kế:** ChatGPT  
> **Thực thi & kiểm định tĩnh:** Antigravity  

---

## A. TỔNG QUAN

### 1. Mục đích sửa code
Trước đây, khi SOLIDWORKS Drawing mở ở chế độ nền/ẩn (hidden/silent), API `ITableAnnotation.get_DisplayedText2` có thể trả về chuỗi rỗng mặc dù trên giao diện đồ họa bản vẽ vẫn hiển thị giá trị `合番` bình thường. Để giải quyết triệt để vấn đề chẩn đoán và kiểm soát chất lượng dữ liệu:
- Cần một cơ chế kiểm tra tính toàn vẹn độc lập (Link Integrity) của công thức `$PRPSHEET:"合番"` trên Drawing.
- Phân tách rõ ràng 3 nguồn dữ liệu độc lập: **Raw Text trên Drawing**, **Custom Property từ 3D Part/Asm Model**, và **Dữ liệu cột trong BOM DataGridView**.
- Bổ sung một sheet thứ 3 trong báo cáo Excel có tên `"KIỂM TRA LIÊN KẾT"` để báo cáo chi tiết từng nguồn dữ liệu, trạng thái liên kết, trạng thái khớp thuộc tính và trạng thái hiển thị.
- Đồng bộ kết quả: Nếu phát hiện sự không đồng nhất đã được xác minh giữa Property và BOM thì phải đánh giá `NG` ngay trên trường `gobanComparison` để sheet `TỔNG HỢP` và sheet `CHI TIẾT KIỂM TRA` hoàn toàn thống nhất.

### 2. Những chức năng mới
- **Xác minh Raw Expression độc lập:** Đọc công thức thô của ô bảng mà không phụ thuộc vào chuỗi hiển thị đồ họa.
- **Kiểm định nguồn gốc Property theo Sheet:** Kiểm tra `Sheet.CustomPropertyView` để lấy đúng Model và Configuration của View nguồn, đối chiếu với PartPath trước khi đọc `CustomPropertyManager.Get6`.
- **Trích xuất trực tiếp cột BOM:** Đọc trực tiếp từ cột `"合番"` của dòng DataGridView đang kiểm tra, không fallback sang Custom Property của Part để đảm bảo tính độc lập tuyệt đối.
- **Sheet Excel thứ 3 "KIỂM TRA LIÊN KẾT":** Trình bày 9 cột chi tiết, gom nhóm theo từng Component, tự động tô màu trạng thái chuẩn (`OK`: xanh lá, `NG`: đỏ nhạt, `Warning`: vàng nhạt) và bảo vệ dữ liệu văn bản dạng chuỗi ký tự (`NumberFormat = "@"`).

### 3. Các file thay đổi
1. `Commands/CheckDrawingBom.cs`
2. `Commands/ExcelDrawingCheckExporter.cs`
3. `docs/CHECK_DRAWING_PROPERTY_LINK_CHANGELOG.md` (tài liệu này)

### 4. Số dòng thêm và xóa thực tế (so với baseline `feat/check-drawing-property-link-report-preview-20261105`)
- `Commands/CheckDrawingBom.cs`: +257 dòng thêm (bao gồm 232 dòng cho tính năng báo cáo mới và 25 dòng bảo tồn tùy biến cục bộ `SafeCloseDocument` / kiểm tra `Sheet.GetName()`), 3 dòng sửa/xóa.
- `Commands/ExcelDrawingCheckExporter.cs`: +112 dòng thêm, 1 dòng xóa (`PrepareSheetCount` từ 2 thành 3).

---

## B. CHI TIẾT THAY ĐỔI TRONG `CheckDrawingBom.cs`

### 1. Class dữ liệu: `DrawingPropertyLinkReportRow`
- **Vị trí:** Ngay sau `DrawingBomFieldResult` (ở cấp namespace `ADDIN.Commands`).
- **Chức năng:** Đại diện cho 1 dòng dữ liệu chi tiết kiểm tra liên kết thuộc tính của một hạng mục (hiện tại là `合番`).
- **Các thuộc tính:**
  - `FieldName` (string): Tên trường kiểm tra (ví dụ: `"合番"`).
  - `DrawingRaw` (string): Biểu thức thô đọc từ ô bảng Drawing (ví dụ: `$PRPSHEET:"合番"`).
  - `PropertyValue` (string): Giá trị Custom Property đọc từ 3D Model tham chiếu của View.
  - `BomValue` (string): Giá trị đọc trực tiếp từ cột BOM tương ứng.
  - `LinkStatus` (DrawingBomCheckStatus): Đánh giá tính toàn vẹn của công thức liên kết.
  - `PropertyStatus` (DrawingBomCheckStatus): Đánh giá độ khớp giữa Property và BOM.
  - `DisplayStatus` (DrawingBomCheckStatus): Đánh giá độ khớp giữa chữ hiển thị Drawing và Property.
  - `Note` (string): Lý do chi tiết hoặc mã nguyên nhân chẩn đoán.

### 2. Thuộc tính mới trong `DrawingCheckItemResult`
- **Vị trí:** Bên trong class `DrawingCheckItemResult`, ngay sau `public string Note`.
- **Khai báo:** `public List<DrawingPropertyLinkReportRow> PropertyLinkRows { get; } = new List<DrawingPropertyLinkReportRow>();`
- **Chức năng:** Lưu trữ danh sách các dòng kiểm tra liên kết thuộc tính của từng chi tiết để chuyển sang tầng xuất báo cáo Excel.

### 3. Tích hợp trong `CheckOneDrawing`
- **Vị trí:** Ngay sau dòng `ApplyGobanTemplateLinkAudit(gobanComparison, drawingData);` và `itemResult.Fields.Add(gobanComparison);`.
- **Đoạn mã:**
  ```csharp
  // Reporting-only: never replace Drawing displayed text with Model/BOM data.
  AddGobanPropertyLinkReportRow(
      itemResult, drawingData, gobanComparison, bomRow, referencedModel,
      referencedConfiguration, sheetName, drawing, activeSheet);
  ```
- **Chức năng:** Thực hiện thu thập dữ liệu và điền dòng báo cáo liên kết cho bản vẽ Part, đồng thời đồng bộ đánh giá `NG` nếu phát hiện lệch dữ liệu đã xác minh. Tuyệt đối không thay thế chữ hiển thị thực tế của Drawing.

### 4. Phương thức mới: `AddGobanPropertyLinkReportRow`
- **Vị trí:** Đặt bên trong class `CheckDrawingBom`, trước `EvaluateGobanLink`.
- **Dữ liệu đầu vào:**
  - `DrawingCheckItemResult item`: Đối tượng kết quả của dòng chi tiết.
  - `DrawingDisplayedData drawingData`: Dữ liệu hiển thị và audit đã quét từ bản vẽ.
  - `DrawingBomFieldResult comparison`: Kết quả so sánh trường `合番` hiện tại.
  - `DataGridViewRow bomRow`: Dòng dữ liệu BOM đang kiểm tra.
  - `ModelDoc2 referencedModel`: Model tham chiếu chính của Drawing.
  - `string referencedConfiguration`: Cấu hình tham chiếu chính.
  - `string sheetName`: Tên sheet hiện tại.
  - `DrawingDoc drawing`: Đối tượng bản vẽ SOLIDWORKS.
  - `Sheet sheet`: Đối tượng sheet hiện tại.
- **Dữ liệu đầu ra:** Thêm 1 đối tượng `DrawingPropertyLinkReportRow` vào `item.PropertyLinkRows`.
- **Logic đánh giá chi tiết:**
  - **Kiểm tra Raw Link (`LinkStatus`):**
    - `OK`: Biểu thức thô khớp chính xác `$PRPSHEET:"合番"` (qua trạng thái `DrawingGobanLinkAuditState.Valid`).
    - `NG`: Biểu thức thô bị gõ đè thành chuỗi cố định hoặc công thức sai (trạng thái `Invalid`). Báo lỗi: *"Mất hoặc sai công thức bắt buộc $PRPSHEET:\"合番\"."*
    - `Warning`: Không đọc được Raw text từ API hoặc có nhiều bảng trùng lặp (trạng thái `Unavailable` / `Ambiguous`).
  - **Kiểm tra Property ↔ BOM (`PropertyStatus`):**
    - `OK`: Cả Property và cột BOM đều đọc thành công độc lập và có nội dung chuẩn hóa giống nhau.
    - `NG`: Cả hai đọc thành công nhưng giá trị khác nhau. Báo lỗi: *"Giá trị Property khác BOM."*
    - `Warning`: Thiếu 1 trong 2 nguồn (BOM không có cột `合番`, hoặc không xác nhận được Property từ đúng model/config của Sheet).
  - **Kiểm tra Displayed ↔ Property (`DisplayStatus`):**
    - `OK`: Chữ hiển thị thực tế đọc được từ Drawing khớp với giá trị Property đã xác minh.
    - `NG`: Chữ hiển thị Drawing khác giá trị Property.
    - `Warning`: Chưa đọc được chữ hiển thị thực tế khi bản vẽ mở ẩn hoặc chưa xác minh được Property.
  - **Đồng bộ với kết quả cũ:** Nếu `PropertyStatus == NG` hoặc `DisplayStatus == NG`, phương thức sẽ gán `comparison.Status = DrawingBomCheckStatus.NG` và bổ sung thông điệp vào `comparison.Message`. Ngược lại, nếu kết quả cũ đã là `NG`, không bao giờ hạ cấp xuống `Warning` hay `OK`.

### 5. Phương thức mới: `TryReadBomGobanColumn`
- **Vị trí:** Đặt trong class `CheckDrawingBom`.
- **Dữ liệu đầu vào:** `DataGridViewRow row`, `out string value`.
- **Dữ liệu đầu ra:** `bool` (true nếu tìm thấy cột có HeaderText chuẩn hóa là `"合番"`, false nếu không có).
- **Chức năng:** Đọc trực tiếp từ DataGridView của giao diện BOM, loại bỏ khoảng trắng thừa. Tuyệt đối không fallback sang Custom Property của Model để đảm bảo tính độc lập.

### 6. Phương thức mới: `TryGetVerifiedSheetGobanProperty`
- **Vị trí:** Đặt trong class `CheckDrawingBom`.
- **Dữ liệu đầu vào:** `DrawingDoc drawing`, `Sheet sheet`, `string sheetName`, `ModelDoc2 fallbackModel`, `string fallbackConfiguration`, `string expectedPartPath`, `out string value`, `out string source`.
- **Dữ liệu đầu ra:** `bool` (true nếu trích xuất thành công giá trị `合番` đã xác minh nguồn gốc).
- **Quy trình xác minh nghiêm ngặt:**
  1. Kiểm tra `sheet.CustomPropertyView`. Nếu trả về chuỗi rỗng (`CUSTOM_PROPERTY_VIEW_UNSPECIFIED`), trả về `false` với nguyên nhân thận trọng, không tự ý đoán view đầu tiên hay fallback model.
  2. Duyệt danh sách View của Drawing bằng `GetFirstView()` và `GetNextView()`, so khớp `GetName2()` với `propertyView`.
  3. Lấy `targetView.ReferencedDocument as ModelDoc2`. Nếu null báo `REFERENCED_MODEL_NOT_LOADED`.
  4. Lấy `model.GetPathName()` và so sánh với `expectedPartPath` bằng `PathsEqualSafe()`. Nếu lệch báo `PROPERTY_SOURCE_MODEL_MISMATCH`.
  5. Lấy cấu hình tham chiếu của View `targetView.ReferencedConfiguration`.
  6. Duyệt các phạm vi thuộc tính: cấu hình tham chiếu trước, cấu hình rỗng (document level) sau.
  7. Gọi `CustomPropertyManager.Get6("合番", true, ...)` để lấy giá trị resolved từ cache (không kích hoạt tài liệu hay rebuild).

---

## C. CHI TIẾT THAY ĐỔI TRONG `ExcelDrawingCheckExporter.cs`

### 1. Khởi tạo Sheet 3: "KIỂM TRA LIÊN KẾT"
- Tăng số lượng sheet được tạo trong `PrepareSheetCount(excel, workbook, 3)`.
- Gán `propertyLinkSheet = workbook.Sheets[3];` và đặt tên `propertyLinkSheet.Name = "KIỂM TRA LIÊN KẾT";`.
- Đảm bảo giải phóng COM an toàn trong `finally`: `ReleaseExcelObject((object)propertyLinkSheet)`.

### 2. Cấu trúc 9 cột trong sheet "KIỂM TRA LIÊN KẾT"
| Cột | Tiêu đề | Nguồn dữ liệu | Mô tả |
| :---: | :--- | :--- | :--- |
| **A** | No. | Số thứ tự tăng dần trong nhóm (index) | Đánh số thứ tự các trường được kiểm tra trong chi tiết |
| **B** | Hạng mục | `field.FieldName` | Tên thuộc tính được kiểm tra (ví dụ: `"合番"`) |
| **C** | Raw Drawing | `field.DrawingRaw` | Biểu thức thô lấy từ ô Drawing (bảo vệ text định dạng `@`) |
| **D** | Property (Part/Asm) | `field.PropertyValue` | Giá trị đọc từ Model Custom Property đã xác minh |
| **E** | BOM | `field.BomValue` | Giá trị đọc từ cột BOM DataGridView |
| **F** | Link | `field.LinkStatus` | Trạng thái toàn vẹn của công thức (`OK` / `NG` / `Warning`) |
| **G** | Property | `field.PropertyStatus` | Trạng thái so sánh Property ↔ BOM (`OK` / `NG` / `Warning`) |
| **H** | Display | `field.DisplayStatus` | Trạng thái so sánh Displayed Text ↔ Property |
| **I** | Ghi chú | `field.Note` | Diễn giải nguyên nhân chi tiết nếu có cảnh báo hoặc lỗi |

### 3. Định dạng và gom nhóm
- **Tiêu đề bảng:** Dòng 1 merge `A1:I1`, nền xanh đậm `Rgb(31, 78, 120)`, chữ trắng đậm cỡ 14.
- **Tiêu đề cột:** Dòng 2 `A2:I2`, nền xanh vừa `Rgb(54, 95, 145)`, chữ trắng đậm cỡ 11.
- **Phân nhóm Component:** Mỗi Component có một dòng header phân cách merge `A:I`:  
  `"COMPONENT INo " + item.BomItemNumber + " | " + item.Component + " | " + file + " | Kết quả tổng: " + item.Status`, nền xanh nhạt `Rgb(221, 235, 247)`.
- **Bảo vệ định dạng công thức:** Áp dụng `sheet.Range["C:E"].NumberFormat = "@";` trước khi gán mảng dữ liệu 2 chiều `Value2 = data`, ngăn chặn triệt để tình trạng Excel hiểu nhầm biểu thức bắt đầu bằng `=` là công thức bị lỗi (`#NAME?`).
- **Tô màu trạng thái:** Áp dụng `ApplyBatchedStatusColors` cho các cột F, G, H với dải màu thống nhất:
  - `OK`: Nền xanh lá nhạt `Rgb(217, 234, 211)`, chữ xanh lá đậm `Rgb(39, 78, 19)`.
  - `NG`: Nền đỏ nhạt `Rgb(244, 204, 204)`, chữ đỏ đậm `Rgb(156, 0, 6)`.
  - `Warning`: Nền vàng nhạt `Rgb(255, 242, 204)`, chữ nâu đậm `Rgb(127, 96, 0)`.

### 4. Không ảnh hưởng 2 sheet cũ
- Sheet 1 `"TỔNG HỢP"` và Sheet 2 `"CHI TIẾT KIỂM TRA"` giữ nguyên hoàn toàn thứ tự, logic gom dữ liệu, định dạng cột và cấu trúc tính toán cũ.
- Khi mở file Excel, con trỏ vẫn kích hoạt tại Sheet 1 ô `A1` (`summarySheet.Activate(); summarySheet.Range["A1"].Select();`).

---

## D. CÁC CHỨC NĂNG VÀ THÀNH PHẦN ĐƯỢC BẢO TỒN NGUYÊN VẸN

1. **Phương thức `SafeCloseDocument`:** Bảo toàn 100% với thuộc tính `[HandleProcessCorruptedStateExceptions]` và `[SecurityCritical]`.
2. **Logic so sánh BOM cũ (`CompareGobanField`, `CompareOptionalStringField`, `CompareBomNumbers`, v.v.):** Giữ nguyên thuật toán, dung sai kích thước và quy tắc so khớp.
3. **Chức năng kiểm tra 表/ウラ (`AddFlatPatternSideChecks`, `CheckUraOmote.cs`):** Hoàn toàn không bị sửa đổi.
4. **Các lệnh và công cụ phụ trợ:** `CheckPartWorkingCopy.cs`, `DrawingCommandDisplayScope.cs`, `CheckAssemblyHole.*`, `BomTaskPaneControl.cs`, `ADDIN.csproj` không bị thay đổi.
5. **Đường dẫn xuất file và cấu hình COM Add-in:** Giữ nguyên toàn bộ.

---

## E. KẾT QUẢ KIỂM TRA VÀ XÁC MINH

| Nội dung kiểm tra | Kết quả | Chi tiết ghi nhận |
| :--- | :---: | :--- |
| **Static Verification (Syntax & Braces)** | **PASS** | Kiểm tra toàn bộ 8.564 dòng `CheckDrawingBom.cs` và 1.761 dòng `ExcelDrawingCheckExporter.cs` bằng bộ phân tích cú pháp ký tự: số lượng đóng/mở ngoặc `{}` cân bằng tuyệt đối (0 lỗi). |
| **Git Diff Format Check (`git diff --check`)** | **PASS** | Không có khoảng trắng thừa cuối dòng (trailing whitespace) hay ký tự xuống dòng xung đột. |
| **Logic Non-Intrusiveness Check** | **PASS** | Không ghi đè hay thay thế các sửa đổi cục bộ khác của người dùng; chỉ thêm đúng các đoạn mã thuộc Section A, B, C, D, E, F. |
| **C# Project Compilation** | **NOT TESTED** | Chưa thực hiện lệnh build dự án (tuân thủ nguyên tắc không tự ý build khi chưa có chỉ thị từ người dùng). |
| **SOLIDWORKS Runtime Execution** | **NOT TESTED** | Chưa chạy trên SOLIDWORKS thực tế. |
| **Excel Export Runtime Execution** | **NOT TESTED** | Chưa mở tiến trình Excel COM thực tế để ghi file. |

---

## F. NHỮNG VẤN ĐỀ CẦN LƯU Ý KHI KIỂM THỬ THỰC TẾ

1. **SOLIDWORKS CustomPropertyView:**  
   Trên một số phiên bản SOLIDWORKS, nếu người dùng vẽ Drawing mà không thiết lập rõ `CustomPropertyView` trong thuộc tính Sheet (để mặc định giá trị rỗng `""`), SOLIDWORKS hiển thị `$PRPSHEET` theo View 3D đầu tiên đặt vào bản vẽ. Tuy nhiên, API không thể chắc chắn đó là view nào nếu chưa chỉ định. Logic mới chọn giải pháp an toàn là trả về `CUSTOM_PROPERTY_VIEW_UNSPECIFIED` (Warning) thay vì tự ý đoán sai view. Người dùng cần quan sát log này trên các bản vẽ thực tế.
2. **Kích hoạt tài liệu Drawing khi đọc chữ:**  
   Trong chế độ chẩn đoán `strictHiddenBatch`, SOLIDWORKS không kích hoạt giao diện đồ họa nên các hàm hiển thị bảng Drawing trả về chuỗi rỗng. Báo cáo mới đã phản ánh đúng bản chất này bằng `DisplayStatus = Warning` và `LinkStatus = OK` khi biểu thức Raw là `$PRPSHEET:"合番"`.
3. **Môi trường Debug x64 khuyến nghị:**  
   Khi người dùng build và chạy thử nghiệm bằng Debug x64 trong Visual Studio kết nối SOLIDWORKS, hãy chú ý theo dõi các dòng log dạng:  
   `[GOBAN REPORT ROW] link=... property=... display=... source=... raw="..."` trong cửa sổ Output để xác nhận hoạt động thực tế của API COM.
