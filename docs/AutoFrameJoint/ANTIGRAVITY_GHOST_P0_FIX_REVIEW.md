# Báo cáo Code Review: Khắc phục Lỗi Vòng đời P0 & Redraw của 3D Ghost Preview

**Người thực hiện:** Agent SOLIDWORKS-UI-DESIGNER (Antigravity)  
**Ngày review:** 2026-10-20  
**Repository:** `tbcphutai-bit/ADDIN-SolidWorks`  
**Nhánh nguồn:** `fix/afj-ghost-preview-lifecycle-p0-20261020`  
**Commit gốc:** [`03531fc67d5cf5b7d3d7860d0f7ec9698676f224`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/03531fc67d5cf5b7d3d7860d0f7ec9698676f224)  
**Nhánh review:** `review/afj-ghost-preview-p0-audit-20261020`  
**Tài liệu tham chiếu:** `docs/AutoFrameJoint/31_CHATGPT_GHOST_CODE_AUDIT_AND_FIX.md`  
**File source chính:** `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`  

---

## 1. Tuân thủ Ranh giới An toàn (Scope Boundaries)
- [x] **SOURCE-ONLY REVIEW:** Không chạy `MSBuild`, `csc` hay lệnh Build/Rebuild nào.
- [x] **BẢO VỆ PHIÊN DEBUG:** Không tắt hoặc can thiệp vào tiến trình `SLDWORKS.exe` hay `devenv.exe` đang chạy của Command khác.
- [x] **KHÔNG DEPLOY/RESTORE:** Giữ nguyên 100% `C:\Tao moi\ADDIN.dll` và `ADDIN.pdb`.
- [x] **CHƯA ĐẤU NỐI UI:** Không sửa `AutoFrameJointSelectionPage.cs` hay `AutoFrameJointCommandEntry.cs`.
- [x] **KHÔNG SỬA COMMAND KHÁC:** Check Hole, Check Bend, Mirror hoàn toàn bất biến.

---

## 2. Kết quả Đối chiếu Chi tiết 7 Hạng mục Yêu cầu

### 2.1. Đối chiếu từng thay đổi C# với commit gốc `d72be71`
So sánh trực tiếp `d72be71` $\rightarrow$ `03531fc` trên [AutoFrameJointGhostPreview.cs](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs) (+36 dòng, -6 dòng):

1. **L50-51:** Bổ sung comment chỉ dẫn cho `IsShowing`:
   `// Includes bodies with a pending cleanup after a COM display error.`
   `// Do not treat this property as proof all bodies are rendered.`
2. **L102:** Gọi `RedrawSafely()` một lần duy nhất sau khi hoàn thành dựng cả 2 component A và B (không gọi lặp lại trong vòng lặp từng body).
3. **L218-219:** **Khắc phục P0:** Khởi tạo `ShownBody tracked = new ShownBody(copy, part);` và gọi `shown.Add(tracked);` **TRƯỚC** khi vào hàm `Display3()`.
4. **L227-230:** Cải tiến nội dung thông báo lỗi khi `Display3` thất bại:
   `"IBody2.Display3 thất bại, mã lỗi=" + result + ". Body giữ lại để dọn bằng Hide()."`
5. **L240-252:** Bổ sung phương thức bảo vệ `RedrawSafely()` bọc toàn bộ trong `try/catch` và chỉ kích hoạt khi `IsOriginalAssemblyActive() == true`.
6. **L257:** Đặt cờ `bool attempted = shown.Count > 0;` ở đầu hàm `ClearInternal`.
7. **L277-278:** Gọi `RedrawSafely()` ở cuối `ClearInternal` nếu `attempted == true`.

---

### 2.2. Xác minh `shown.Add(tracked)` được gọi trước `Display3()` (Lỗi P0)
- **Vị trí code:** [AutoFrameJointGhostPreview.cs#L218-L226](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L218-L226)
  ```csharp
  // Register the COM body BEFORE entering Display3.
  // If COM throws after a partial draw, cleanup still has a
  // strong reference to the possibly displayed body.
  ShownBody tracked = new ShownBody(copy, part);
  shown.Add(tracked);

  int result = copy.Display3(
      component, colorRef,
      (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);
  ```
- **Phân tích kỹ thuật lỗi P0 trước đó:**
  Ở bản `d72be71`, lệnh `shown.Add` nằm ở dòng 223 (sau khi `Display3` trả về 0). Nếu SOLIDWORKS COM ném ngoại lệ trong lúc đang dựng dở hình học hoặc `Display3` trả về mã lỗi khác 0, ngoại lệ bị bắn ra trước khi `shown.Add` kịp chạy. Khi đó, khối `catch` trong `TryShow` gọi `ClearInternal()`, nhưng danh sách `shown` **hoàn toàn không có body này**. Tham chiếu mạnh duy nhất nằm ở biến cục bộ `copy` bị mất khi thoát hàm; khi Garbage Collector thu hồi wrapper này, SOLIDWORKS sẽ bị treo một temporary body đang hiển thị dở mà không cách nào dọn dẹp bằng `Hide()` được nữa, dẫn tới rò rỉ bộ nhớ đồ họa hoặc crash khi đóng Assembly.
- **Đánh giá bản sửa:** Việc đưa `shown.Add(tracked)` lên trước `Display3` triệt tiêu hoàn toàn kẽ hở rò rỉ tham chiếu này. Dù `Display3` ném ngoại lệ hay trả về mã lỗi, `tracked` đã nằm chắc chắn trong `shown` để hàm dọn dẹp xử lý.
- **Kết luận:** **XÁC MINH PASS HOÀN TOÀN.**

---

### 2.3. Kiểm tra đường dọn dẹp khi `Display3()` trả về mã lỗi hoặc ném Exception
Truy vết cả 2 nhánh lỗi:
1. **Trường hợp `Display3()` trả về mã lỗi khác 0 (1, 2, 3):**
   - `tracked` đã nằm trong `shown`.
   - `if (result != 0)` ném ngoại lệ `InvalidOperationException`.
   - Luồng chạy nhảy vào khối `catch (Exception ex)` của `TryShow` (L108).
   - `ClearInternal(out cleanError)` được gọi.
   - Vòng lặp `ClearInternal` duyệt qua `shown`, gọi `tracked.Copy.Hide(tracked.Part)`.
   - Nếu `Hide` thành công: `tracked` được gỡ bỏ khỏi `shown`, `RedrawSafely()` dọn sạch viewport.
   - Nếu `Hide` ném lỗi: `tracked` được đưa vào `failed` và giữ lại trong `shown` (fail-closed), ghi nhận trace và báo cáo `cleanup pending`.
2. **Trường hợp `Display3()` ném ngoại lệ COM / CLR bất ngờ:**
   - `tracked` đã nằm trong `shown`.
   - Ngoại lệ được bắt tại L108 của `TryShow` và kích hoạt đường dọn dẹp `ClearInternal()` tương tự như trên.
- **Kết luận:** **ĐƯỜNG DỌN DẸP CHẶT CHẼ, KHÔNG CÓ NHÁNH MỒ CÔI (PASS).**

---

### 2.4. Kiểm tra `RedrawSafely()` và việc không làm thay đổi Assembly khác
- **Vị trí code:** [AutoFrameJointGhostPreview.cs#L240-L252](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L240-L252)
  ```csharp
  private void RedrawSafely()
  {
      try
      {
          // Do not activate an Assembly or redraw a different document.
          if (IsOriginalAssemblyActive())
              sourceAssembly.GraphicsRedraw2();
      }
      catch (Exception ex)
      {
          Trace.WriteLine("[AFJ-GHOST] Graphics redraw warning: " + ex);
      }
  }
  ```
- **Tính an toàn tuyệt đối:**
  1. Chỉ gọi `sourceAssembly.GraphicsRedraw2()`, tuyệt đối không gọi `app.ActivateDoc` làm thay đổi document đang kích hoạt của người dùng.
  2. Guard `IsOriginalAssemblyActive()` kiểm tra:
     - `app.ActiveDoc` không null và là tài liệu `swDocASSEMBLY`.
     - `app.IsSame(active, sourceAssembly) == swObjectSame`.
     Nếu người dùng đã chuyển sang một Part hoặc Assembly khác, `RedrawSafely()` lập tức bỏ qua, không can thiệp vào tài liệu khác.
  3. Bọc trọn vẹn trong `try/catch`, nếu `GraphicsRedraw2()` có cảnh báo hoặc lỗi COM thì chỉ ghi `Trace.WriteLine`, không ngắt quãng tiến trình.
  4. Tần suất tối ưu: Chỉ gọi đúng 1 lần sau khi hoàn thành dựng cả 2 component (L102) và 1 lần sau khi dọn dẹp (L278).
- **Kết luận:** **AN TOÀN TUYỆT ĐỐI (PASS).**

---

### 2.5. Giữ `Hide(entry.Part)`, không tự bổ sung `Hide(sourceAssembly)`
- **Vị trí code:** [AutoFrameJointGhostPreview.cs#L265](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L265)
  ```csharp
  // Official Hide signature accepts ModelDoc2 of the Part.
  entry.Copy.Hide(entry.Part);
  ```
- **Đánh giá:**
  - Hoàn toàn nhất trí với phân tích của ChatGPT: Tài liệu chính thức của SOLIDWORKS API cho `IBody2.Hide(Part)` và `IBody2.IHide` chỉ định nghĩa nhận context `Part` / `PartDoc`.
  - Việc tự ý bổ sung fallback `Hide(sourceAssembly)` mà không có căn cứ runtime sẽ dẫn đến nguy cơ che giấu lỗi thật hoặc làm mất dấu đối tượng chưa được ẩn hợp lệ.
  - Mã nguồn hiện tại giữ nguyên `entry.Copy.Hide(entry.Part)`, bắt ngoại lệ nếu lỗi và giữ lại đối tượng trong `failed`.
- **Kết luận:** **TUÂN THỦ 100% CHỈ ĐẠO CỦA CODE OWNER (PASS).**

---

### 2.6. Kiểm tra các trường hợp thất bại của `TryClear()` và vòng đời COM
- **Phân tích các kịch bản của `TryClear`:**
  1. **Khi đang bận (`busy == true`):** Trả về `false`, báo `"Preview đang xử lý; chưa thể dọn."`.
  2. **Khi danh sách rỗng (`shown.Count == 0`):** `attempted = false`, vòng lặp không chạy, trả về `true` ngay lập tức, không tốn chi phí redraw.
  3. **Khi `entry.Copy.Hide(entry.Part)` thất bại:**
     - Ngoại lệ được bắt an toàn tại L267, ghi log `Trace.WriteLine`.
     - `entry` được đưa vào danh sách `failed`.
     - Cuối hàm, danh sách `shown` được khôi phục lại đúng các phần tử trong `failed`.
     - Do `shown.Count != 0`, thuộc tính `IsShowing` vẫn trả về `true` (như đã chú thích ở L50-51).
     - Điều này đảm bảo rằng các cuộc gọi dọn dẹp tiếp theo (hoặc khi đóng Command) vẫn còn nguyên tham chiếu để thử giải phóng lại, không bao giờ để con trỏ COM rơi vào trạng thái thu hồi rác tự do khi chưa được `Hide()`.
- **Kết luận:** **BẢO ĐẢM VÒNG ĐỜI COM VÀ QUẢN LÝ THẤT BẠI CHẶT CHẼ (PASS).**

---

### 2.7. Đọc lại toàn bộ code của class
Đã rà soát từng dòng trong 282 dòng của class [AutoFrameJointGhostPreview.cs](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs):
- Cấu trúc class `sealed`, các trường `readonly` rõ ràng.
- Toàn bộ các API `Copy()`, `IsTemporaryBody()`, `Display3()`, `Hide()`, `GetBodies3()`, `GetParent()`, `GetModelDoc2()`, `IsSame()` đều chính xác tuyệt đối về mặt kiểu dữ liệu và cú pháp C# 7.3.
- Chưa có bất kỳ dòng code nào đấu nối trái phép vào `AutoFrameJointSelectionPage.cs` hay `AutoFrameJointCommandEntry.cs`.
- Không phát hiện thêm lỗi cú pháp, rò rỉ COM hay race condition nào trong phạm vi source-only hiện tại.

---

## 3. Trạng thái & Kết luận
- Bản sửa tại commit `03531fc` đã giải quyết triệt để lỗi P0 về thứ tự đăng ký đối tượng COM và bổ sung cơ chế viewport refresh an toàn.
- Source code C# được giữ nguyên vẹn 100%, không tự ý chỉnh sửa backend.
- Đã sẵn sàng chuyển giao nhánh review lên GitHub cho ChatGPT kiểm tra lại.
