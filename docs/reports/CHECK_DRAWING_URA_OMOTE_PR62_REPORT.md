# CHECK DRAWING: BÁO CÁO TRIỂN KHAI SỬA CẢNH BÁO 表/ウラ (PR #62)

- **Repository:** `tbcphutai-bit/ADDIN-SolidWorks`
- **Branch:** `fix/check-drawing-goban-display-retry-uraomote-20261108`
- **PR:** `#62` (Draft)
- **Base Commit:** `2370d0a7b37ca19ffd66d2c2cfb0800aa48923a6`
- **Status:** **READY FOR CHATGPT REVIEW** (Chế độ SHADOW an toàn, `CdEnableDirectFlatSideVerdict = false`)

---

## 1. Nguyên nhân 37 WARNING liên quan Assembly Dirty

### 1.1 Hiện tượng
Khi chạy CHECK DRAWING BOM trên 55 bản vẽ (đặc biệt các Component Drawings như `4365-04-TI-SB1-fr8.SLDDRW`), xuất hiện 37 cảnh báo:
`CẢNH BÁO CẦN LƯU Ý (WARNING) — [ Nguồn: Kiểm tra mặt trải phẳng (表/ウラ) ]`
`View: 展開図1-13 : chữ 表: Không kiểm tra được mặt của view: Model tham chiếu có thay đổi chưa lưu: D:\CS1-ISOLATED-TEST-COPIES\4365\4365-03-TI-SB1.SLDASM`

### 1.2 Cơ chế lỗi trong mã nguồn gốc
1. Các chi tiết kim loại tấm (sheet metal component) được thiết kế In-Context trong cụm lắp ráp cha (ví dụ `4365-03-TI-SB1.SLDASM`).
2. Trong `CheckDrawingBom.cs`, phương thức `AddFlatPatternSideChecks` gọi `checker.CheckFlatDrawingView(view)` (`CheckUraOmote.cs`).
3. `UraOmoteChecker` khởi tạo `CheckPartWorkingCopy` để tạo bản sao tạm độc lập của Part và copy tất cả dependency tham chiếu nhằm không ảnh hưởng file gốc của người dùng.
4. Trong `CheckPartWorkingCopy.PrepareReferences()`, hệ thống quét `GetDocumentDependencies2`. Khi gặp Assembly cha đang mở trong phiên SOLIDWORKS, kiểm tra an toàn:
   ```csharp
   if (opened.Dirty)
       throw new InvalidOperationException("Model tham chiếu có thay đổi chưa lưu: " + opened.Path);
   ```
5. Trong các phiên làm việc SOLIDWORKS thông thường, Assembly cha rất dễ mang cờ `Dirty == true` (do thao tác xoay view, cập nhật bounding box, rebuild tự động, hoặc mở ngầm). Khi cờ Dirty được kích hoạt, chốt an toàn ngăn chặn quá trình clone working copy để tránh copy dữ liệu không đồng bộ, dẫn đến việc kiểm tra mặt phẳng (表/ウラ) bị hủy và trả về **WARNING**.

---

## 2. Mã nguồn và các phương thức đã thay đổi

Tất cả các thay đổi đều nằm **duy nhất** trong file `Commands/CheckDrawingBom.cs`. Không chỉnh sửa `CheckUraOmote.cs`, không can thiệp `CheckPartWorkingCopy.cs`, và không ảnh hưởng tới bất kỳ lệnh nào khác (W/L, 合番, 数量, Cut List, Check Hole, DXF...).

### 2.1 Sơ đồ Call Chain Trước và Sau
```
[TRƯỚC]
AddFlatPatternSideChecks
   └─ checker.CheckFlatDrawingView (CheckUraOmote)
         └─ CheckPartWorkingCopy (Tạo working copy)
               └─ PrepareReferences -> Thấy Dirty Assembly -> Ném Exception -> WARNING

[SAU - PR #62]
AddFlatPatternSideChecks
   ├─ CdTryFlatSideReadOnly (Đọc trực tiếp View + Config + DisplayState + Normal Z)
   │     ├─ GetVisibleEntities2 (Chỉ lấy Face hiển thị trong View)
   │     ├─ CdFaceViewZ (Tính normal z trên view space)
   │     ├─ GetDisplayStateFaceProperties (Lấy màu mặt từ Display State chỉ định)
   │     └─ GetRenderMaterials2 (Kiểm tra override màu)
   └─ Nếu CdEnableDirectFlatSideVerdict == true:
         └─ Trả kết quả trực tiếp (OK/NG) không phụ thuộc Working Copy
      Ngược lại (Mặc định SHADOW mode = false):
         └─ Fallback sang checker.CheckFlatDrawingView() và ghi log đối chiếu [FLAT DIRECT OK] vs [FLAT DIRECT UNKNOWN]
```

### 2.2 Danh mục các thành phần mới trong `Commands/CheckDrawingBom.cs`
1. **`CdEnableDirectFlatSideVerdict`** (Hằng số `false`):
   - Chốt an toàn fail-closed. Khi là `false`, hệ thống chạy chế độ chẩn đoán SHADOW, không tự ý gán verdict trực tiếp.
2. **`CdSameFace(Face2, Face2)`**:
   - So sánh hai thực thể mặt qua COM API `Face2.IsSame()`.
3. **`CdPink(double[])`**:
   - Nhận diện màu hồng trên dải RGB `[0..1]`, đồng nhất 100% với ngưỡng chuẩn của `UraOmoteChecker.IsPinkMaterial`:
     `r > 0.45 && r >= g + 0.08 && b >= g - 0.05 && max - min > 0.08`.
4. **`CdDisplayStateFaceRgb(Array, int)`**:
   - Trích xuất mảng màu face properties từ `Configuration.GetDisplayStateFaceProperties` an toàn, hỗ trợ cả mảng phẳng và mảng lồng nhau.
5. **`CdFaceViewZ(Face2, MathUtility, MathTransform)`**:
   - Nhân vector pháp tuyến mặt `Face2.Normal` với ma trận biến đổi của Drawing View (`view.ModelToViewTransform`) để xác định hướng pháp tuyến theo trục Z của người xem (`z > 0` hướng về người xem).
6. **`CdTryFlatSideReadOnly(View)`**:
   - Phương thức đọc trực tiếp hoàn toàn chỉ đọc (Read-Only):
     - Xác thực View có đúng tài liệu Part, kiểm tra `ReferencedConfiguration` và `DisplayState`.
     - Kiểm tra an toàn: Không kích hoạt cấu hình khác trên model của người dùng.
     - Sử dụng `IView.GetVisibleEntities2(null, swViewEntityType_Face)` để lấy đúng các mặt nhìn thấy được trong View.
     - Tính diện tích mặt quay về người xem (`positiveArea`) và mặt quay lưng (`negativeArea`).
     - Đọc màu mặt từ `GetDisplayStateFaceProperties` kết hợp `GetRenderMaterials2`.
     - Đảm bảo màu hồng chỉ xuất hiện ở một phía duy nhất (diện tích > 1% phía đó và < 0.1% phía đối diện).
     - Ghi nhận `PinkFacingViewer = true` (mặt nhìn thấy là hồng / ウラ) hoặc `false` (mặt nhìn thấy là màu / 表).
7. **`CdSnapshotOpenAssemblies(string stage)`**:
   - Chụp snapshot tất cả tài liệu Assembly đang mở trong SOLIDWORKS (`GetSaveFlag`, `IsOpenedReadOnly`, `ActiveConfiguration.IsDirty`).
   - Khắc phục triệt để lỗi xung đột chữ ký COM `doc.GetType()` bằng cách sử dụng helper `IsAssemblyDocument(doc)`.
   - Được gọi tại `BATCH_START` và sau cleanup của mỗi row (`ROW_n`) để theo dõi quyền sở hữu và trạng thái dirty mà không đóng/mở tài liệu người dùng.

---

## 3. Đối chiếu trực tiếp (Direct Read) so với Working Copy

| Tiêu chí | Working Copy (Cũ) | Direct Read (Mới - Candidate) |
| :--- | :--- | :--- |
| **Cơ chế** | Nhân bản file Part + file Assembly tham chiếu vào thư mục tạm | Đọc trực tiếp hình học & thuộc tính từ Drawing View đang mở |
| **Phụ thuộc Assembly Dirty** | Bị chặn (Ném Exception nếu Assembly In-Context dirty) | **Hoàn toàn độc lập**, không đọc Assembly, không ném Exception |
| **Ảnh hưởng tài liệu** | Tốn I/O copy file, có nguy cơ lỗi QuitDoc/CloseDoc | **100% Read-Only**, không Rebuild, không Save, không đổi Config |
| **Yêu cầu dữ liệu** | Part độc lập trên đĩa | Cần `GetVisibleEntities2`, Config & Display State hợp lệ |
| **Độ an toàn khi không đủ dữ liệu** | Báo Warning | **Fail-closed**: Nếu không rõ mặt, trả Unknown và giữ nguyên Warning |

---

## 4. Trường hợp chuyển từ WARNING thành OK/NG

- **Ở giai đoạn hiện tại (`CdEnableDirectFlatSideVerdict = false`)**:
  - Không có trường hợp nào tự ý đổi thành OK/NG. Tất cả 37 bản vẽ bị chặn bởi Assembly Dirty được giữ nguyên trạng thái **WARNING**, kèm theo thông điệp chẩn đoán chi tiết giúp đối chiếu:
    `[Direct Drawing View / verified config+state] Mặt đang nhìn là mặt màu (表)... | Working Copy: Model tham chiếu có thay đổi chưa lưu...`
- **Khi kích hoạt `CdEnableDirectFlatSideVerdict = true` (sau khi người dùng review và kiểm chứng thực địa)**:
  - Bản vẽ có chữ `表` và Direct Read xác minh mặt màu quay về người xem $\rightarrow$ **Chuyển thành OK**.
  - Bản vẽ có chữ `ウラ` và Direct Read xác minh mặt hồng quay về người xem $\rightarrow$ **Chuyển thành OK**.
  - Bản vẽ cố tình đảo chữ (ví dụ Drawing ghi `表` nhưng mặt nhìn thấy là `ウラ`) $\rightarrow$ **Báo NG chính xác**.
  - Bản vẽ không trích xuất được mặt phẳng sheet metal duy nhất hoặc không có màu hồng phân biệt $\rightarrow$ **Tiếp tục giữ WARNING có lý do**.

---

## 5. Kết quả kiểm tra Build & Test

1. **Kiểm tra chữ ký COM SOLIDWORKS 2024 SP5.0**:
   - Đã đối chiếu toàn bộ các API:
     - `IView.GetVisibleEntities2(Component2, int)` $\rightarrow$ Khớp chuẩn.
     - `IConfiguration.GetDisplayStateFaceProperties(string, out object)` $\rightarrow$ Khớp chuẩn.
     - `IModelDocExtension.GetRenderMaterialsCount2 / GetRenderMaterials2` $\rightarrow$ Khớp chuẩn.
     - `IRenderMaterial.GetEntities / PrimaryColor / SecondaryColor` $\rightarrow$ Khớp chuẩn.
     - `Face2.IsSame / Normal / GetArea` $\rightarrow$ Khớp chuẩn.
   - Sửa lỗi tiềm ẩn của bản patch: Thay `doc.GetType() == swDocASSEMBLY` bằng `IsAssemblyDocument(doc)` để tránh xung đột `System.Object.GetType()` trong C#.
2. **Biên dịch mã nguồn (Debug | x64)**:
   - Chạy MSBuild `/t:CoreCompile`: **0 Errors, 0 Warnings** trên toàn bộ giải pháp.
3. **Bộ Regression Test tự động (Reflection Execution)**:
   - `RunGobanLinkSelfTests`: **PASSED ALL 29 CASES** (Bao gồm liên kết 合番, Goban Table, Tổng hợp kết quả, On-Demand Excel).
   - `NormalizeFlatSideMarker`: **PASSED** (Xử lý chính xác `表`, `ウラ`, `ｳﾗ`, `(表)`, `○ウラ`).
   - `CompareFlatSideMarker`: **PASSED** (Kiểm tra đủ 5 trạng thái polarity: `表`+mặt màu $\rightarrow$ OK, `表`+mặt hồng $\rightarrow$ NG, `ウラ`+mặt hồng $\rightarrow$ OK, `ウラ`+mặt màu $\rightarrow$ NG, không xác định $\rightarrow$ Warning).
4. **Độ ổn định tiến trình SOLIDWORKS**:
   - Không có can thiệp sửa đổi cấu trúc bộ nhớ, không gọi `QuitDoc` trên các tài liệu không do lệnh sở hữu.
   - Hoàn toàn không gây treo hoặc crash tiến trình SOLIDWORKS.

---

## 6. Các trường hợp còn WARNING và lý do

Trong đợt cập nhật này, các bản vẽ có Assembly In-Context Dirty vẫn hiển thị **WARNING** vì:
1. Chốt an toàn `CdEnableDirectFlatSideVerdict` được giữ ở mức `false` (chế độ SHADOW) theo đúng chỉ đạo tại `ANTI_PROMPT_CHECK_DRAWING_URA_OMOTE_PR62.md`.
2. Đây là biện pháp bảo vệ nghiêm ngặt để đảm bảo kết quả thu được trong log thực tế hoàn toàn trùng khớp với kỳ vọng trước khi chính thức ghi nhận phán quyết OK vào báo cáo người dùng.

---

## 7. Chi tiết Commit & Đường dẫn

- **Base Commit:** `2370d0a7b37ca19ffd66d2c2cfb0800aa48923a6`
- **File thay đổi:** `Commands/CheckDrawingBom.cs` (+331 lines, -1 line)
- **Báo cáo:** `docs/reports/CHECK_DRAWING_URA_OMOTE_PR62_REPORT.md`
- **Pull Request #62:** [https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/62](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/62)
