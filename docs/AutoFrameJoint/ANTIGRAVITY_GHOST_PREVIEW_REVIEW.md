# Báo cáo Code Review: 3D Ghost Preview Engine (Source-Only)

**Người thực hiện:** Agent SOLIDWORKS-UI-DESIGNER (Antigravity)  
**Ngày review:** 2026-10-20  
**Repository:** `tbcphutai-bit/ADDIN-SolidWorks`  
**Nhánh nguồn:** `feat/afj-temporary-body-ghost-engine-source-only-20261020`  
**Commit gốc:** [`d72be71b99930a6b6c2d6e3d6fabb17e39298d34`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/d72be71b99930a6b6c2d6e3d6fabb17e39298d34)  
**Nhánh review:** `review/afj-ghost-preview-code-review-20261020`  
**Tài liệu tham chiếu:** `docs/AutoFrameJoint/30_TEMP_BODY_GHOST_CODE_HANDOFF.md`  
**File source chính:** `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`  

---

## 1. Tóm tắt Tuân thủ Phạm vi & Ranh giới (Scope Boundaries)
- [x] **SOURCE-ONLY REVIEW:** Không thực hiện bất kỳ lệnh `MSBuild`, `csc` hay Build/Rebuild nào.
- [x] **BẢO VỆ PHIÊN DEBUG:** Không tắt hoặc can thiệp vào tiến trình `SLDWORKS.exe` hay `devenv.exe` đang chạy của Command khác.
- [x] **KHÔNG DEPLOY/RESTORE:** Giữ nguyên 100% `C:\Tao moi\ADDIN.dll` và `ADDIN.pdb`.
- [x] **CHƯA ĐẤU NỐI UI:** Không sửa `AutoFrameJointSelectionPage.cs` hay `AutoFrameJointCommandEntry.cs`.
- [x] **KHÔNG ĐỤNG CHÉC KHÁC:** Check Hole, Check Bend, Mirror hoàn toàn bất biến.

---

## 2. Kết quả Đối chiếu Chi tiết 7 Hạng mục Yêu cầu

### 2.1. `IBody2.Copy()` có tạo Temporary Body hợp lệ hay không?
- **Vị trí code:** [AutoFrameJointGhostPreview.cs#L186-L189](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L186-L189)
  ```csharp
  Body2 copy = original.Copy() as Body2;
  if (copy == null || !copy.IsTemporaryBody())
      throw new InvalidOperationException(
          "Không tạo được bản sao Temporary Body.");
  ```
- **Đối chiếu API & Interop:**
  - Chữ ký COM: `System.Object Copy()` trên interface `IBody2`.
  - Đặc tả SOLIDWORKS API: Phương thức `Copy()` trên `IBody2` tạo một bản sao hình học nằm hoàn toàn trong bộ nhớ RAM và tự động đánh dấu cờ `IsTemporaryBody = true`. Bản sao này không gắn với bất kỳ Feature nào và không xuất hiện trong FeatureManager Tree.
  - Kiểm tra `copy.IsTemporaryBody()` ngay sau đó bảo đảm loại bỏ trường hợp copy thất bại hoặc trả về đối tượng COM rỗng.
- **Đánh giá:** **HỢP LỆ (PASS)**.

---

### 2.2. Chữ ký `IBody2.Display3()` có tương thích với Interop đang sử dụng không?
- **Vị trí code:** [AutoFrameJointGhostPreview.cs#L215-L220](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L215-L220)
  ```csharp
  int result = copy.Display3(
      component, colorRef,
      (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);
  if (result != 0)
      throw new InvalidOperationException(
          "IBody2.Display3 thất bại, mã lỗi=" + result);
  ```
- **Đối chiếu Reflection thực tế với `SolidWorks.Interop.sldworks.dll`:**
  - Chữ ký Interop: `System.Int32 Display3(System.Object Component, System.Int32 Color, System.Int32 Option)`.
  - Tham số 1 (`Component`): Truyền `component` (`Component2` cast ngầm sang `object`).
  - Tham số 2 (`Color`): Truyền `colorRef` (`int` sinh từ `ColorTranslator.ToWin32(Color)`, cấu trúc `0x00BBGGRR`).
  - Tham số 3 (`Option`): Truyền `(int)swTempBodySelectOptions_e.swTempBodySelectOptionNone` (= 0). Lựa chọn này giúp tránh kích hoạt trạng thái khoá mô hình `swModifyBlock` (vốn xảy ra khi chọn `swTempBodySelectable`).
  - Mã trả về: Trả về `int` (0: Thành công; 1: Không phải temp body; 2: Invalid component; 3: Not a part instance). Code đã bắt ngoại lệ nếu `result != 0`.
- **Đánh giá:** **HOÀN TOÀN TƯƠNG THÍCH (100% PASS)**.

---

### 2.3. Việc sử dụng `GetBodies3()` có đúng với Component được chọn không?
- **Vị trí code:** [AutoFrameJointGhostPreview.cs#L161-L177](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L161-L177)
  ```csharp
  ModelDoc2 part = component.GetModelDoc2() as ModelDoc2;
  if (part == null ||
      part.GetType() != (int)swDocumentTypes_e.swDocPART)
      throw new InvalidOperationException(
          "Part chưa được Resolve hoặc Component không phải Part.");

  object info;
  object raw = component.GetBodies3(
      (int)swBodyType_e.swSolidBody, out info);
  Array bodies = raw as Array;
  ...
  if (bodies.Length > MaxBodiesPerComponent)
      throw new InvalidOperationException(
          "Part có quá nhiều body cho Preview thử nghiệm.");
  ```
- **Đối chiếu API & Tính đúng đắn:**
  1. **Lọc Solid Body:** Tham số `swBodyType_e.swSolidBody` (= 0) lọc chính xác các body dạng khối của Frame (Weldment Structural Members), loại trừ các surface/sheet body.
  2. **Kiểm tra Resolve:** Tránh lỗi khi Component ở trạng thái Lightweight bằng cách kiểm tra `GetModelDoc2() != null` và `swDocPART`.
  3. **Tọa độ & Transform:** `GetBodies3()` trả về body trong hệ tọa độ Part. Khi gọi `Display3(component, ...)`, SOLIDWORKS tự động áp dụng ma trận biến đổi của `component` lên viewport Assembly. Code chủ động **không nhân thêm ma trận transform**, tránh lỗi double-transform (bị lệch hình hai lần).
  4. **Chặn Subassembly:** Tại L88, code kiểm tra `frameA.GetParent() != null || frameB.GetParent() != null`, từ chối subassembly lồng nhau vì `Display3` không hỗ trợ component cấp dưới.
  5. **Ngưỡng an toàn:** Giới hạn tối đa 12 Solid Body (`MaxBodiesPerComponent = 12`) chống treo máy khi gặp Part phức tạp.
- **Đánh giá:** **CHẶT CHẼ VÀ ĐÚNG ĐẮN (PASS)**.

---

### 2.4. Kiểm tra quản lý vòng đời `Body2`, đặc biệt việc `Hide()` khi Cancel
- **Vị trí code:** [AutoFrameJointGhostPreview.cs#L24-L37, L233-L256](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L233-L256)
  ```csharp
  private readonly List<ShownBody> shown = new List<ShownBody>();
  ...
  private bool ClearInternal(out string error)
  {
      error = null;
      List<ShownBody> failed = new List<ShownBody>();
      for (int i = shown.Count - 1; i >= 0; i--)
      {
          ShownBody entry = shown[i];
          try
          {
              entry.Copy.Hide(entry.Part);
          }
          catch (Exception ex)
          {
              failed.Add(entry);
              Trace.WriteLine("[AFJ-GHOST] Hide failed: " + ex);
              error = "Hide thất bại: " + ex.Message;
          }
      }
      shown.Clear();
      failed.Reverse();
      shown.AddRange(failed);
      return failed.Count == 0;
  }
  ```
- **Đối chiếu Quy tắc COM Lifecycle của SOLIDWORKS:**
  - Đặc tả API nêu rõ: Trong khi một `IBody2` đang được hiển thị bởi `Display3`, lập trình viên **tuyệt đối không được giải phóng con trỏ COM** (hoặc để GC thu hồi) trước khi gọi `Hide()`. Nếu không gọi `Hide()`, SOLIDWORKS có thể crash hoặc rò rỉ vùng nhớ đồ họa.
  - `AutoFrameJointGhostPreview` lưu giữ strong reference trong danh sách `shown`.
  - Khi dọn dẹp (`ClearInternal`), nếu bất kỳ body nào gọi `Hide` bị ném ngoại lệ, reference của body đó **không bị huỷ** mà được đưa vào `failed` và giữ lại trong `shown` để thử dọn dẹp lại lần sau.
- **Đánh giá:** **ĐẠT QUY TẮC BẢO VỆ VÒNG ĐỜI COM (PASS)**.
- *(Xem mục 3 bên dưới để biết các điểm cải tiến thêm về Redraw viewport).*

---

### 2.5. Kiểm tra tính an toàn khi lỗi xảy ra giữa chừng hoặc khi Assembly thay đổi
- **Vị trí code:** [AutoFrameJointGhostPreview.cs#L56-L119, L141-L156](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L56-L119)
- **Cơ chế an toàn:**
  1. **Chống Reentrancy:** Cờ `busy` chặn mọi cuộc gọi đan xen trên luồng UI khi đang dựng hình hoặc đang dọn dẹp.
  2. **Kiểm tra đổi Document:** Hàm `IsOriginalAssemblyActive()` kiểm tra `app.ActiveDoc` vẫn là Assembly ban đầu qua `app.IsSame(active, sourceAssembly) == swObjectSame`. Nếu người dùng chuyển tab CAD hoặc đóng Assembly, Preview lập tức từ chối và cảnh báo mở lại lệnh.
  3. **Rollback khi lỗi dở dang (Partial Render Rollback):** Nếu Frame A vẽ thành công nhưng Frame B thất bại, khối `catch (Exception ex)` trong `TryShow` lập tức gọi `ClearInternal` để gỡ bỏ toàn bộ body của Frame A đã hiển thị, bảo đảm không để lại "rác đồ họa" mồ côi.
  4. **Cách ly lỗi ngoại quan:** Việc gán thuộc tính trong suốt `copy.MaterialPropertyValues2` được bọc riêng trong `try/catch`. Nếu phiên bản SOLIDWORKS không hỗ trợ transparency trên temporary body, preview vẫn hiển thị màu sắc bình thường mà không bị crash.
- **Đánh giá:** **RẤT AN TOÀN, ĐẠT CHUẨN DEFENSIVE CODING (PASS)**.

---

### 2.6. Xác minh `ADDIN.csproj` đăng ký class đúng một lần
- **Kiểm tra file:** [ADDIN.csproj#L127](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/ADDIN.csproj#L127)
  ```xml
  <Compile Include="Commands\AutoFrameJoint\AutoFrameJointGhostPreview.cs" />
  ```
- **Kết quả rà soát regex:** Class xuất hiện chính xác duy nhất **1 lần**.
- Cấu hình Build Output: Giữ nguyên `<OutputPath>bin\x64\Debug\</OutputPath>` độc lập và rỗng các thẻ `PreBuildEvent`/`PostBuildEvent`.
- **Đánh giá:** **CHÍNH XÁC (PASS)**.

---

### 2.7. Đối chiếu toàn bộ API với bộ Interop thực tế
Đã kiểm tra reflection trực tiếp từ các file Interop trong thư mục tham chiếu `$(USERPROFILE)\Downloads\`:
- `SolidWorks.Interop.sldworks.dll`:
  - `IBody2.Copy()` $\rightarrow$ `System.Object Copy()` (Khớp)
  - `IBody2.IsTemporaryBody()` $\rightarrow$ `System.Boolean IsTemporaryBody()` (Khớp)
  - `IBody2.Display3(...)` $\rightarrow$ `System.Int32 Display3(System.Object, System.Int32, System.Int32)` (Khớp)
  - `IBody2.Hide(...)` $\rightarrow$ `System.Void Hide(System.Object)` (Khớp)
  - `IBody2.MaterialPropertyValues2` $\rightarrow$ `System.Object MaterialPropertyValues2 { get; set; }` (Khớp)
  - `IComponent2.GetBodies3(...)` $\rightarrow$ `System.Object GetBodies3(System.Int32, System.Object&)` (Khớp)
  - `IComponent2.GetModelDoc2()` $\rightarrow$ `System.Object GetModelDoc2()` (Khớp)
  - `IComponent2.GetParent()` $\rightarrow$ `SolidWorks.Interop.sldworks.Component2 GetParent()` (Khớp)
  - `ISldWorks.IsSame(...)` $\rightarrow$ `System.Int32 IsSame(System.Object, System.Object)` (Khớp)
  - `ISldWorks.ActiveDoc` $\rightarrow$ `System.Object get_ActiveDoc()` (Khớp)
- `SolidWorks.Interop.swconst.dll`:
  - `swObjectEquality`: `swObjectNotSame = 0`, `swObjectSame = 1`, `swObjectUnsupported = 2` (Khớp)
  - `swDocumentTypes_e`: `swDocPART = 1`, `swDocASSEMBLY = 2` (Khớp)
  - `swBodyType_e`: `swSolidBody = 0` (Khớp)
  - `swTempBodySelectOptions_e`: `swTempBodySelectOptionNone = 0` (Khớp)

---

## 3. Các Vấn đề Tiềm ẩn & Đề xuất gửi ChatGPT (Code Owner)

Mặc dù toàn bộ code đã viết rất bài bản và chuẩn xác, qua đối chiếu sâu với tài liệu kỹ thuật SOLIDWORKS API, Antigravity phát hiện **2 điểm tiềm ẩn** cần lưu ý trước khi đấu nối vào host thật:

### Vấn đề 1: Thiếu lệnh ép làm mới Viewport (`GraphicsRedraw2`) sau khi `Hide()` và `Display3()`
- **Vị trí:** Cuối hàm `ShowComponent` ([L225](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L225)) và cuối hàm `ClearInternal` ([L255](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L255)).
- **Nguyên nhân:** Khi gọi `IBody2.Hide(Part)`, SOLIDWORKS xóa temporary body khỏi bảng quản lý hiển thị nội bộ, nhưng **không tự động repaint vùng đồ họa 3D**. Nếu người dùng không xoay chuột (pan/rotate) hoặc zoom, hình ảnh ghost cũ có thể bị lưu vệt (ghost artifacts) trên màn hình.
- **Đề xuất sửa (để ChatGPT xem xét):**
  Bổ sung làm mới viewport an toàn cho Assembly sau khi vẽ hoặc dọn dẹp:
  ```csharp
  // Sau khi hoàn thành Show hoặc Clear:
  try
  {
      sourceAssembly.GraphicsRedraw2();
  }
  catch (Exception redrawEx)
  {
      Trace.WriteLine("[AFJ-GHOST] Viewport redraw warning: " + redrawEx.Message);
  }
  ```

### Vấn đề 2: Đối số của `IBody2.Hide()` trong bối cảnh Assembly
- **Vị trí:** [AutoFrameJointGhostPreview.cs#L243](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L243)
  ```csharp
  entry.Copy.Hide(entry.Part);
  ```
- **Nguyên nhân:** Khi gọi `Display3(component, ...)`, context hiển thị nằm trong cửa sổ tài liệu Assembly (`sourceAssembly`). Theo tài liệu SOLIDWORKS API, `Hide(Part)` nhận đối tượng Part nơi body được hiển thị. Tuy nhiên, ở một số phiên bản SOLIDWORKS khi chạy trong Assembly, nếu tài liệu Part không phải là active doc hoặc không mở cửa sổ riêng, SOLIDWORKS có thể yêu cầu đối số là `ModelDoc2` của Assembly chính (`sourceAssembly`).
- **Đề xuất sửa (defensive fallback):**
  Trong khối `catch` của `Hide`, có thể thử fallback gọi với `sourceAssembly`:
  ```csharp
  try
  {
      entry.Copy.Hide(entry.Part);
  }
  catch
  {
      // Fallback thử với context Assembly nếu Hide(Part) từ chối
      entry.Copy.Hide(sourceAssembly);
  }
  ```

---

## 4. Kết luận & Trạng thái Tiếp theo
- Code `AutoFrameJointGhostPreview.cs` đạt chất lượng rất tốt, tuân thủ nghiêm ngặt các rào chắn kiến trúc và COM safety.
- Dự án `ADDIN.csproj` được cấu hình độc lập đầu ra an toàn.
- Toàn bộ source code C# được giữ nguyên không chỉnh sửa.
- Sẵn sàng chuyển giao nhánh review và báo cáo lên GitHub cho ChatGPT xem xét.
