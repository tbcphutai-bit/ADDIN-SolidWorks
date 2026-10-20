# Báo cáo Phản hồi & Đề xuất Kỹ thuật: 3D Ghost Preview (Hậu Audit Runbook 32)

**Người thực hiện:** Agent SOLIDWORKS-UI-DESIGNER (Antigravity)  
**Ngày thực hiện:** 2026-10-20  
**Repository:** `tbcphutai-bit/ADDIN-SolidWorks`  
**Nhánh nguồn:** `review/afj-ghost-p0-chatgpt-source-audit-20261020`  
**Commit gốc:** [`bf5e21add3058b1906412fa092c8afe3798728f4`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/bf5e21add3058b1906412fa092c8afe3798728f4)  
**Nhánh review:** `review/afj-ghost-p0-deep-audit-20261020`  
**Tài liệu tham chiếu:** `docs/AutoFrameJoint/32_CHATGPT_INDEPENDENT_GHOST_P0_SOURCE_REVIEW.md`  
**File source đối chiếu:** `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`  

---

## 1. Tuyên bố Ranh giới & Nguyên tắc Đánh giá (Runtime vs. Static)
- **STATIC SOURCE REVIEW ONLY:** Antigravity xác nhận toàn bộ các đánh giá trong các lượt trước đây chỉ mang tính chất **kiểm tra tĩnh cú pháp, kiểu dữ liệu C# 7.3 và chữ ký Interop (Static / Reflection Checks)**. Tuyệt đối **KHÔNG** được xem là bằng chứng cho việc code đã "PASS" trong môi trường runtime SOLIDWORKS.
- **TUÂN THỦ RÀO CHẮN:**
  - Không chạy `MSBuild`, `csc` hay lệnh Build/Rebuild nào.
  - Không tắt, ngắt quãng hay can thiệp vào phiên debug SOLIDWORKS/Visual Studio đang chạy của Command khác.
  - Không copy đè, Deploy hay Restore DLL/PDB cài đặt (`C:\Tao moi\ADDIN.dll`).
  - Không tự ý sửa backend, không đấu nối UI/PMP khi chưa có quyết định của Code Owner.
  - Check Hole, Check Bend, Mirror hoàn toàn bất biến.

---

## 2. Đối chiếu Từng Phát hiện của ChatGPT (Runbook 32) với Source Thực tế

| Phát hiện trong Runbook 32 | Đối chiếu Source Code Thực tế | Đánh giá & Tiếp thu của Antigravity |
|---|---|---|
| **P1: "Loại bỏ hoàn toàn rò rỉ COM" là chưa được chứng minh** | Code đăng ký `shown.Add(tracked)` trước `Display3()`. Nếu `Hide()` ném ngoại lệ, reference được giữ trong `failed` và repopulate vào `shown`. | **Tiếp thu hoàn toàn.** Đăng ký trước `Display3` chỉ tạo *cơ hội dọn dẹp*, không bảo đảm COM runtime sẽ giải phóng 100%. Nếu instance `AutoFrameJointGhostPreview` bị GC thu hồi khi đóng lệnh, rác COM vẫn có thể tồn tại. Cần cơ chế giữ instance còn sống (xem mục 3). |
| **P1: UI/PMP Cleanup chưa được đấu nối** | `AutoFrameJointCommandEntry.cs` hiện chưa khởi tạo `AutoFrameJointGhostPreview`; `AutoFrameJointSelectionPage.cs` chưa có nút bấm Preview. | **Chính xác.** Hiện tại người dùng chưa thể kích hoạt Ghost Preview từ SOLIDWORKS host. Mọi khẳng định về "Cancel/AfterClose an toàn" trước đây chỉ là logic nội bộ của class, chưa phải kịch bản host hoàn chỉnh. |
| **P1: Cụm từ "Hình gốc, CHƯA CẮT" có thể gây hiểu lầm** | L103-105: `message = String.Format("Đã hiển thị {0} body A + {1} body B. Đây là hình gốc, CHƯA CẮT.", countA, countB);` | **Chính xác.** `IComponent2.GetBodies3` lấy body từ Component trong Assembly, có thể đã bao gồm các vết cắt ở cấp Assembly (Assembly Feature Cuts). Cần sửa lại câu chữ chính xác (xem mục 4). |
| **P1: Vị trí và hướng không gian chưa được xác minh** | Code gọi `copy.Display3(component, colorRef, ...)` mà không áp ma trận biến đổi. | **Chính xác.** Tài liệu SOLIDWORKS và các ví dụ thực tế cho thấy hành vi của `Display3(Component2, ...)` với các Part bị xoay/nghiêng/dời gốc cần được kiểm chứng trong host để biết có cần bù `Transform2` hay không. |
| **P2: Chưa lọc Component đang Bị ẩn (Hidden) hoặc Bị nén (Suppressed)** | L78-95 chỉ kiểm tra null, `app.IsSame`, `GetParent() != null`, `GetModelDoc2() != null` và `part.GetType() == swDocPART`. | **Chính xác.** Thiếu kiểm tra `IsSuppressed()` và `IsHidden(false)`. Có thể vô tình dựng hình ghost của một component đang bị ẩn (xem mục 5). |
| **P2: Chưa tận dụng `BodiesInfo` từ `GetBodies3`** | L167-169: `object info; component.GetBodies3(..., out info);` bỏ qua biến `info`. | **Chính xác.** `BodiesInfo` phân biệt user bodies và sheet bodies. Thuật toán cắt joint sau này cần lưu ý. |
| **P2: Cờ `busy` không phải đồng bộ đa luồng** | L25: `private bool busy;` là biến boolean thông thường. | **Chính xác.** Cờ này chỉ bảo vệ reentrancy trên luồng STA UI của SOLIDWORKS. Không dùng cho background thread. |
| **P2: `RedrawSafely` chỉ mang tính Best-effort** | L240-252: Bọc trong `try/catch` và chỉ ghi trace nếu lỗi. | **Chính xác.** Không bảo đảm 100% viewport được repaint trên mọi phiên bản nếu hàm ném lỗi COM. |

---

## 3. Đề xuất Cơ chế Giữ Ghost Manager Còn Sống khi `Hide()` Thất bại (Lifetime Protocol)

### Vấn đề:
Khi người dùng đóng PMP hoặc thoát lệnh trong khi một số body gọi `Hide(entry.Part)` bị lỗi:
Nếu đối tượng `AutoFrameJointGhostPreview` chỉ là biến cục bộ trong phương thức `Open()`, nó sẽ bị GC thu hồi. Khi đó, các COM body đang hiển thị dở trong SOLIDWORKS sẽ vĩnh viễn mất con trỏ điều khiển, gây rò rỉ vùng nhớ đồ họa.

### Đề xuất Kiến trúc Quản lý Vòng đời:
1. **Duy trì Tham chiếu Cấp Phiên (Session-level Lifecycle):**
   Trong `AutoFrameJointCommandEntry`, duy trì một biến static lưu giữ manager đang có body chưa dọn dẹp xong:
   ```csharp
   // Lưu giữ manager nếu còn body pending cleanup
   private static AutoFrameJointGhostPreview activeGhost;
   ```
2. **Quy tắc Bàn giao và Thử lại (Retry on Document/Command Event):**
   - Khi PMP đóng (`OnClose` / `AfterClose`), gọi `activeGhost.TryClear(out string err)`.
   - Nếu `activeGhost.IsShowing == false` (tất cả đã ẩn thành công): gán `activeGhost = null` để giải phóng.
   - Nếu `activeGhost.IsShowing == true` (còn body lỗi): **GIỮ NGUYÊN tham chiếu `activeGhost`**, ghi Trace cảnh báo, không gán `null`.
   - Khi người dùng mở lại lệnh Auto Frame Joint hoặc khi Assembly active trở lại: hàm entry kiểm tra `if (activeGhost != null && activeGhost.IsShowing)` và tự động kích hoạt `activeGhost.TryClear(...)` để dọn dẹp tiếp.
3. **Tuyệt đối không cưỡng ép giải phóng COM:**
   Không gọi `Marshal.FinalReleaseComObject` lên các body đang nằm trong danh sách `failed` vì hành vi này sẽ phá hủy COM wrapper trong khi SOLIDWORKS vẫn đang render con trỏ nội bộ.

---

## 4. Đề xuất Sửa Thông báo Preview để Tránh Hiểu lầm

### Vị trí:
[Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L103-L105](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L103-L105)

### Đề xuất Code cụ thể:
- **Code hiện tại:**
  ```csharp
  message = String.Format(
      "Đã hiển thị {0} body A + {1} body B. Đây là hình gốc, CHƯA CẮT.",
      countA, countB);
  ```
- **Code đề xuất sửa:**
  ```csharp
  message = String.Format(
      "Đã hiển thị bản sao trạng thái hiện tại ({0} body A, {1} body B). Đây là hình học thực tế từ Assembly, CHƯA MÔ PHỎNG VẾT CẮT MỚI.",
      countA, countB);
  ```
- **Thông báo khi dọn dẹp dở dang (L113-116):**
  ```csharp
  message = cleaned
      ? "Không tạo được Preview: " + ex.Message
      : String.Format("Lỗi Preview ({0}); còn {1} body chưa thể ẩn bằng Hide(). Hãy giữ phiên làm việc và thử dọn lại.", ex.Message, shown.Count);
  ```

---

## 5. Đánh giá & Đề xuất Kiểm tra Component Đang Hidden / Suppressed

### Phân tích API:
Qua đối chiếu trực tiếp Reflection từ `SolidWorks.Interop.sldworks.dll`:
- `System.Boolean IComponent2.IsSuppressed()`
- `System.Boolean IComponent2.IsHidden(System.Boolean ConsiderSuppressed)`

### Hành vi & Khuyến nghị:
1. **Component bị Suppressed:**
   Khi component bị Suppressed, `GetModelDoc2()` và `GetBodies3()` trả về null. Thay vì để rơi vào ngoại lệ chung `"Part chưa được Resolve"`, kiểm tra `IsSuppressed()` sớm sẽ đưa ra thông báo rõ ràng cho người dùng.
2. **Component bị Hidden:**
   Nếu component đang bị ẩn trong Assembly, việc hiển thị một Ghost Preview bán trong suốt đè lên vị trí đó có thể làm người dùng bối rối hoặc làm lộ chi tiết họ đang chủ động ẩn.
   Do đó, khuyến nghị từ chối Preview nếu 1 trong 2 Frame đang bị ẩn và yêu cầu người dùng Hiện (Unhide) trước.

### Đề xuất Code cụ thể:
Chèn vào đầu hàm `TryShow` ([AutoFrameJointGhostPreview.cs#L92](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L92)):
```csharp
// Kiểm tra trạng thái Suppressed
if (frameA.IsSuppressed() || frameB.IsSuppressed())
{
    message = "Một trong hai Frame đang bị Suppressed. Hãy Unsuppress trước khi xem Preview.";
    return false;
}

// Kiểm tra trạng thái Hidden (false = kiểm tra trực tiếp trong view hiện tại)
if (frameA.IsHidden(false) || frameB.IsHidden(false))
{
    message = "Một trong hai Frame đang bị ẩn (Hidden). Hãy hiển thị Component trước khi xem Preview.";
    return false;
}
```

---

## 6. Ma trận Ca Kiểm thử Vị trí & Hướng Frame trong Assembly (Host Matrix)

Khi bước vào giai đoạn Host Test trên SOLIDWORKS thật, cần chuẩn bị bộ dữ liệu Assembly kiểm thử các trường hợp không gian sau:

| Mã Ca | Tình huống Hình học & Ràng buộc | Mục tiêu Xác minh Runtime |
|:---:|---|---|
| **TC-GEO-01** | **Gốc tọa độ chuẩn:** 2 Frame vuông góc đặt ngay tại gốc (0,0,0) của Assembly, không xoay. | Kiểm tra cơ chế vẽ cơ bản, màu sắc Cyan/Orange và độ trong suốt. |
| **TC-GEO-02** | **Dời gốc (Translation):** 2 Frame được Mate cách xa gốc Assembly (ví dụ: X=1000mm, Y=500mm, Z=300mm). | Xác minh `Display3(Component2, ...)` có tự động offset theo component hay vẽ nhầm về (0,0,0). |
| **TC-GEO-03** | **Xoay góc bất kỳ (Rotation / Angle):** 2 Frame giao nhau ở góc 45° hoặc 60°, trục nghiêng trong không gian 3D. | Xác minh ma trận xoay có được áp dụng chuẩn xác hay temporary body bị lệch hướng so với thanh gốc. |
| **TC-GEO-04** | **Lật đối xứng (Mirrored Component):** Frame B là bản sao đối xứng (Transform có định thức âm: $det(M) < 0$). | Kiểm tra hiện tượng đảo pháp tuyến (inverted normals) hoặc lệch vị trí đối xứng. |
| **TC-GEO-05** | **Multi-body Structural Member:** Frame A là Part Weldment gồm 2 body khối (thanh chính + bản mã/end plate). | Kiểm tra `GetBodies3` vẽ đủ 2 body, bộ đếm tăng chính xác và không vượt `MaxBodiesPerComponent`. |
| **TC-GEO-06** | **Assembly-level Cut:** Frame A bị cắt bởi 1 Feature Cut tạo trong môi trường Assembly. | Kiểm tra xem ghost body phản ánh hình dạng trước hay sau vết cắt của Assembly, đối chiếu với `BodiesInfo`. |

---

## 7. Danh sách Vấn đề BẮT BUỘC Phải Chạy SOLIDWORKS Mới Xác minh Được

Các điểm kỹ thuật sau **không thể kết luận qua review mã nguồn**:
1. **Tọa độ thực tế của `Display3` trong Assembly:**
   - Liệu SOLIDWORKS có tự động dùng `Component2.Transform2` khi hiển thị temporary body với context là `Component2`, hay cần lập trình viên gọi `Body2.ApplyTransform`? (Review tĩnh không thể đoán thay kernel của Parasolid/SOLIDWORKS).
2. **Khả năng hiển thị độ trong suốt (`MaterialPropertyValues2`):**
   - Phiên bản SOLIDWORKS cụ thể trên máy người dùng có hỗ trợ transparency trên temporary body được hiển thị qua `Display3` hay không, hay chỉ thể hiện màu khối đặc (solid color)?
3. **Hiệu lực dọn dẹp của `entry.Copy.Hide(entry.Part)`:**
   - Trong môi trường Assembly, khi Part không mở cửa sổ độc lập, lệnh `Hide` trên context PartDoc có giải phóng sạch sẽ 100% hình ảnh trên viewport hay không?
4. **Mức độ ảnh hưởng tới Selectionbox của PropertyManager Page:**
   - Việc vẽ 2 temporary body với cờ `swTempBodySelectOptionNone` có cản trở người dùng click chọn lại Component A/B trên đồ họa 3D hay không?
5. **Hiệu năng và độ trễ đồ họa:**
   - Thời gian thực thi chuỗi `GetBodies3 -> Copy -> MaterialPropertyValues2 -> Display3 -> GraphicsRedraw2` trên mô hình khung thực tế có gây giật/lag giao diện hay không?

---

## 8. Kết luận & Kiến nghị
- Antigravity hoàn toàn nhất trí với toàn bộ 8 điểm nhận định và rào chắn an toàn trong Runbook 32 của ChatGPT.
- Các đề xuất sửa code nêu trên được cung cấp để ChatGPT trực tiếp xem xét, quyết định và tích hợp.
- Antigravity tiếp tục duy trì trạng thái **chờ lệnh**, không can thiệp vào mã nguồn, dự án build hay tiến trình SOLIDWORKS của người dùng.
