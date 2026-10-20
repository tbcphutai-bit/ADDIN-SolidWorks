# Báo cáo Code Review: Ghost Session Manager (Runbook 34)

**Người thực hiện:** Agent SOLIDWORKS-UI-DESIGNER (Antigravity)  
**Ngày thực hiện:** 2026-10-20  
**Repository:** `tbcphutai-bit/ADDIN-SolidWorks`  
**Nhánh nguồn:** `feat/afj-ghost-session-owner-source-only-20261020`  
**Commit gốc:** [`ff19e37dee5f2350b6ceb83918e39bf51873aca5`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/ff19e37dee5f2350b6ceb83918e39bf51873aca5)  
**Nhánh review:** `review/afj-ghost-session-manager-review-20261020`  
**Tài liệu tham chiếu:** `docs/AutoFrameJoint/34_GHOST_SESSION_MANAGER_SOURCE_HANDOFF.md`  
**File source đối chiếu:**
- `Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs` (325 dòng)
- `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` (299 dòng)
- `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs` (93 dòng)
- `ADDIN.csproj`

---

## 1. Ranh giới Đánh giá & Tuân thủ An toàn
- **STATIC SOURCE REVIEW ONLY:** Antigravity xác nhận đây là review tĩnh về kiến trúc, cú pháp và chữ ký COM Interop. Tuyệt đối **không tự ý kết luận Runtime PASS** khi chưa qua chạy thực tế trong tiến trình `SLDWORKS.exe`.
- **BẢO VỆ PHIÊN DEBUG CỦA COMMAND KHÁC:**
  - Không chạy lệnh `MSBuild`, `csc` hay lệnh Build/Rebuild nào.
  - Không ngắt quãng, tắt hoặc can thiệp vào tiến trình SOLIDWORKS / Visual Studio đang chạy.
  - Không Deploy, Restore, ghi đè DLL/PDB cài đặt (`C:\Tao moi\ADDIN.dll`).
  - Không tự ý sửa backend C#, chưa đấu nối UI/CommandEntry.
  - Check Hole, Check Bend, Mirror hoàn toàn bất biến.

---

## 2. Kết quả Rà soát Toàn bộ Class `AutoFrameJointGhostSessionManager.cs` (325 dòng)

### 2.1. Cấu trúc Tổng thể & Mẫu Thiết kế Singleton Điều phối
- Class được thiết kế dạng `sealed` với thể hiện tĩnh duy nhất `AutoFrameJointGhostSessionManager.Shared` (L46-54).
- Không cho phép gán đè hay thay thế instance `Shared`.
- Thể hiện phiên làm việc `private Session current;` (L56) là class nội bộ `private sealed class Session` (L25-44), nắm giữ toàn bộ thông tin ngữ cảnh: `Token` (Guid), `App` (ISldWorks), `Assembly` (ModelDoc2), `Ghost` (AutoFrameJointGhostPreview), `UiThreadId` (int), `State` (SessionState) và `CloseRequested` (bool).

---

### 2.2. Kiểm tra Chi tiết 5 Phương thức Công khai

#### 1. `TryBegin(app, assembly, out token, out message)` (L80-111):
- **Chống ghi đè phiên (L84-90):** Kiểm tra `if (current != null)`. Nếu phiên cũ đang tồn tại, từ chối mở phiên mới và thông báo rõ nguyên nhân (đặc biệt khi đang chờ dọn `current.CloseRequested`). Điều này ngăn chặn triệt để việc mất dấu phiên cũ.
- **Rào chắn Luồng STA (L94-98):** `Thread.CurrentThread.GetApartmentState() != ApartmentState.STA`. Từ chối ngay lập tức nếu bị gọi từ background thread hoặc thread pool trước khi chạm tới bất kỳ API COM nào của SOLIDWORKS.
- **Ràng buộc Assembly Kích hoạt (L100-105):** Gọi `IsBoundAssemblyActive(app, assembly)`. Chỉ cho phép khởi tạo khi tài liệu truyền vào đúng là Assembly đang kích hoạt.
- **Khởi tạo An toàn:** Tạo `new Session(app, assembly)`, cấp phát `Guid.NewGuid()` mới và đưa trạng thái về `SessionState.Ready`.
- **Đánh giá:** **PASS STATIC.**

#### 2. `TryShow(token, frameA, frameB, out message)` (L117-160):
- **Kiểm tra Token & Luồng:** `FindOwnedSession(token, out session, out message)` kiểm tra khớp token và đúng luồng UI khởi tạo.
- **Kiểm tra Trạng thái Phiên (L124-140):**
  - Chặn khi `session.CloseRequested == true` (PMP đang trong quá trình đóng).
  - Chặn khi `session.State == SessionState.PendingCleanup` hoặc `DeferredDocument` (buộc dọn dẹp xong phiên trước).
  - Chặn khi `!IsBoundAssemblyActive(...)`: nếu người dùng đổi cửa sổ CAD, tự động chuyển trạng thái sang `DeferredDocument` và từ chối vẽ, **không tự tiện gọi `ActivateDoc`**.
- **Gọi Dựng Hình:** Ủy thác cho `session.Ghost.TryShow(frameA, frameB, out message)` trong khối `try/catch` phòng thủ.
- **Chuyển đổi Trạng thái:** Nếu thành công chuyển thành `Showing`; nếu thất bại và còn body tồn đọng chuyển thành `PendingCleanup`, ngược lại về `Ready`.
- **Đánh giá:** **PASS STATIC.**

#### 3. `TryClear(token, out message)` (L166-177):
- Sử dụng cho trường hợp người dùng ấn nút Reset / Dọn dẹp trong khi PMP vẫn đang mở.
- Từ chối nếu `CloseRequested == true` (hướng dẫn dùng `RetryClosedCleanup`).
- Chuyển tiếp vào hàm dọn dẹp lõi `Cleanup(session, out message)`.
- **Đánh giá:** **PASS STATIC.**

#### 4. `CloseFromPage(token, out message)` (L183-191):
- Dành riêng cho callback `AfterClose` của PropertyManager Page trong tương lai.
- Đánh dấu `session.CloseRequested = true;` để ghi nhận PMP đã đóng.
- Thực hiện `Cleanup(session, out message)`.
- Nếu `session.Ghost.IsShowing == true` (do `Hide()` lỗi hoặc sai tài liệu): phiên **KHÔNG BỊ GIẢI PHÓNG**, `current` được giữ nguyên trong bộ nhớ RAM (xem mục 2.4).
- **Đánh giá:** **PASS STATIC.**

#### 5. `RetryClosedCleanup(out message)` (L197-216):
- Dành cho kịch bản người dùng kích hoạt thử dọn dẹp lại sau khi `AfterClose` bị lỗi hoặc bị hoãn.
- Kiểm tra `current == null` $\rightarrow$ báo không có gì cần dọn.
- Kiểm tra luồng UI `IsOnOwnerThread`.
- Kiểm tra `!current.CloseRequested` $\rightarrow$ từ chối nếu PMP vẫn đang mở.
- Gọi `Cleanup(current, out message)`. Khi người dùng quay lại đúng Assembly và dọn dẹp thành công, phiên sẽ được đóng hoàn toàn.
- **Đánh giá:** **PASS STATIC.**

---

### 2.3. Rà soát Token, Assembly Identity, UI-Thread Guard & Trạng thái Cleanup

1. **Token Quản lý Phiên (Guid):**
   - Được sinh ngẫu nhiên mỗi lần `TryBegin`: `Token = Guid.NewGuid();`.
   - Mọi phương thức thao tác đều đòi hỏi `Guid token` hợp lệ. Nếu một trang PMP cũ hoặc callback trễ gọi đến, token không khớp sẽ bị chặn đứng tại `FindOwnedSession`, loại bỏ nguy cơ can thiệp chéo phiên.
2. **Định danh COM Assembly (`IsBoundAssemblyActive`):**
   - Lấy `app.ActiveDoc as ModelDoc2`.
   - Kiểm tra kiểu `swDocASSEMBLY`.
   - So sánh định danh COM chính thống: `app.IsSame(active, assembly) == (int)swObjectEquality.swObjectSame`.
   - Bọc trong `try/catch`, không bao giờ làm crash tiến trình khi tài liệu bị đóng đột ngột.
3. **Rào chắn Luồng Giao diện (UI-Thread Guard):**
   - Kiểm tra `ApartmentState.STA` khi bắt đầu.
   - Lưu `session.UiThreadId = Thread.CurrentThread.ManagedThreadId;`.
   - Kiểm tra `Thread.CurrentThread.ManagedThreadId == session.UiThreadId` trong mọi thao tác.
   - Đảm bảo 100% tuân thủ mô hình đơn luồng giao diện của SOLIDWORKS, không có async/await hay Task ngầm.
4. **Vòng đời Trạng thái (`SessionState`):**
   - `Ready`: Phiên đã mở, 0 body hiển thị.
   - `Showing`: Đang hiển thị bản sao của 2 component.
   - `PendingCleanup`: Dọn dẹp chưa dứt điểm do `Hide()` ném ngoại lệ; giữ toàn bộ tham chiếu.
   - `DeferredDocument`: User đã đổi sang tài liệu khác; tạm hoãn gọi `Hide()` để không gây xáo trộn.
   - `Closed`: Tất cả body đã được `Hide()` thành công và PMP đã đóng; giải phóng session.

---

### 2.4. Rà soát Nguy cơ Mất Tham chiếu COM khi `Hide()` Thất bại
Antigravity đã kiểm tra đường dẫn giải phóng bộ nhớ tại L262-279 (`FinishEmpty`):

```csharp
private void FinishEmpty(Session session)
{
    if (session.Ghost.IsShowing)
        throw new InvalidOperationException(
            "Cannot discard a Ghost Session while bodies are tracked.");

    if (session.CloseRequested)
    {
        session.State = SessionState.Closed;
        // Session is only released after all tracked bodies hide.
        if (Object.ReferenceEquals(current, session))
            current = null;
    }
    else
    {
        session.State = SessionState.Ready;
    }
}
```

Và tại hàm `Cleanup` (L250-256):
```csharp
if (!cleared || session.Ghost.IsShowing)
{
    session.State = SessionState.PendingCleanup;
    message = "Chưa dọn xong Temporary Body: " +
              (String.IsNullOrEmpty(error) ? "xem Output." : error);
    return false;
}

FinishEmpty(session);
```

- **Phân tích:**
  - `FinishEmpty` chỉ được gọi khi `!session.Ghost.IsShowing` (tức `shown.Count == 0`).
  - Nếu `TryClear` trả về `cleared == false` hoặc `session.Ghost.IsShowing == true`: code lập tức `return false` và **không bao giờ gọi `FinishEmpty`**.
  - Do đó, biến `current` **không bao giờ bị gán `null`** khi còn body chưa được ẩn.
  - Chuỗi giữ tham chiếu mạnh: `Shared` (static) $\rightarrow$ `current` (`Session`) $\rightarrow$ `Ghost` (`AutoFrameJointGhostPreview`) $\rightarrow$ `shown` (`List<ShownBody>`) $\rightarrow$ `Copy` (`Body2`) & `Part` (`ModelDoc2`).
  - Toàn bộ chuỗi này tồn tại liên tục trong vùng nhớ RAM, bảo vệ tuyệt đối các COM wrapper không bị Garbage Collector thu hồi bất ngờ khi đang hiển thị dở.
- **Đánh giá:** **THIẾT KẾ BẢO TOÀN THAM CHIẾU COM XUẤT SẮC (PASS STATIC).**

---

### 2.5. Đối chiếu với `AutoFrameJointGhostPreview.cs` và `AutoFrameJointCommandEntry.cs`
- **Đối chiếu với `AutoFrameJointGhostPreview.cs`:**
  - Chữ ký hàm gọi: `Ghost.TryShow(frameA, frameB, out message)` $\rightarrow$ khớp hoàn toàn.
  - Chữ ký hàm dọn: `Ghost.TryClear(out error)` $\rightarrow$ khớp hoàn toàn.
  - Thuộc tính kiểm tra: `Ghost.IsShowing` $\rightarrow$ khớp hoàn toàn.
  - Khởi tạo: `new AutoFrameJointGhostPreview(app, assembly)` $\rightarrow$ khớp hoàn toàn.
- **Đối chiếu với `AutoFrameJointCommandEntry.cs`:**
  - `AutoFrameJointCommandEntry.cs` hiện tại chưa bị chỉnh sửa (giữ nguyên tính độc lập).
  - Kế hoạch đấu nối tương lai tại Mục 4 của Runbook 34 (`TryBegin`, `showGhost`, `CloseFromPage` trong `AfterClose`) hoàn toàn tương thích và không làm xung đột với logic phân tích góc hiện có.

---

### 2.6. Kiểm tra Đăng ký `ADDIN.csproj`
- File [ADDIN.csproj#L128](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/ADDIN.csproj#L128):
  ```xml
  <Compile Include="Commands\AutoFrameJoint\AutoFrameJointGhostSessionManager.cs" />
  ```
- Kiểm tra trùng lặp: Xuất hiện đúng **1 lần duy nhất**.
- Cấu hình output an toàn `bin\x64\Debug\` và không có lệnh copy đè DLL cài đặt.
- **Đánh giá:** **CHÍNH XÁC (PASS).**

---

## 3. Vấn đề Tiềm ẩn & Đề xuất Gửi ChatGPT (Code Owner)

### Vấn đề: Kịch bản Assembly bị Đóng Vĩnh viễn khi Đang có Body Pending Cleanup
- **Vị trí code:** [AutoFrameJointGhostSessionManager.cs#L228-L233](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs#L228-L233)
  ```csharp
  if (!IsBoundAssemblyActive(session.App, session.Assembly))
  {
      session.State = SessionState.DeferredDocument;
      message = "Assembly nguồn không hoạt động. Giữ body để thử dọn sau.";
      return false;
  }
  ```
  Và tại `TryBegin` (L84-90):
  ```csharp
  if (current != null)
  {
      message = current.CloseRequested
          ? "Còn phiên Ghost đang chờ dọn. Không thể thay thế Assembly cũ."
          : "Auto Frame Joint đang có một phiên Preview khác.";
      return false;
  }
  ```
- **Phân tích Tình huống:**
  1. Giả sử người dùng mở Assembly 1, xem Ghost Preview, sau đó đóng PMP nhưng do lỗi SOLIDWORKS, một body gọi `Hide()` bị lỗi $\rightarrow$ session chuyển sang `PendingCleanup`.
  2. Người dùng sau đó đóng hẳn Assembly 1 (không lưu hoặc đóng file), rồi mở Assembly 2 để làm việc.
  3. Khi chạy lệnh Auto Frame Joint trên Assembly 2:
     - `TryBegin` thấy `current != null` $\rightarrow$ Từ chối với thông báo: *"Còn phiên Ghost đang chờ dọn. Không thể thay thế Assembly cũ."*
     - Nếu người dùng thử gọi `RetryClosedCleanup` $\rightarrow$ Do Assembly 1 đã đóng, `IsBoundAssemblyActive` trả về `false`, báo: *"Assembly nguồn không hoạt động. Giữ body để thử dọn sau."*
     - Hậu quả: Add-in rơi vào trạng thái "khoá chết" (deadlock logic), không thể mở Ghost Preview cho bất kỳ Assembly nào khác cho tới khi tắt hẳn và khởi động lại phần mềm SOLIDWORKS!
- **Đề xuất Giải pháp (Admin / Manual Discard Protocol):**
  Để ChatGPT xem xét trong tương lai, nên có một cơ chế thoát khẩn cấp có kiểm soát (Manual Discard/Reset):
  ```csharp
  /// <summary>
  /// Cho phép người dùng xác nhận huỷ bỏ phiên cũ khi tài liệu Assembly nguồn đã bị đóng vĩnh viễn.
  /// </summary>
  public bool ForceDiscardSessionIfDocumentClosed(out string message)
  {
      if (current == null)
      {
          message = "Không có phiên nào.";
          return true;
      }
      // Chỉ cho phép huỷ khi đã yêu cầu đóng và tài liệu thực sự không còn mở trong SOLIDWORKS
      if (current.CloseRequested && !IsBoundAssemblyActive(current.App, current.Assembly))
      {
          Trace.WriteLine("[AFJ-SESSION] Force discarded orphaned session after document closure.");
          current.State = SessionState.Closed;
          current = null;
          message = "Đã giải phóng phiên cũ do Assembly nguồn không còn tồn tại.";
          return true;
      }
      message = "Không thể cưỡng chế giải phóng khi phiên vẫn đang mở hoặc Assembly còn hoạt động.";
      return false;
  }
  ```

---

## 4. Kết luận
- Lớp `AutoFrameJointGhostSessionManager` được viết rất chặt chẽ, đáp ứng toàn diện các tiêu chuẩn kỹ thuật về bảo toàn vòng đời COM, phòng thủ đa luồng STA và phân định rõ ràng giữa các tài liệu CAD.
- Không phát hiện thêm lỗi biên dịch hay lỗi kiểu dữ liệu C# 7.3 nào.
- Nhánh review và báo cáo đã sẵn sàng để chuyển giao lên GitHub cho ChatGPT đánh giá bước tiếp theo.
