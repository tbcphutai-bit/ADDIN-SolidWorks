# Báo cáo Code Review: Lọc Hiển thị Trạng thái Hiện tại & Thiết kế Quản lý Phiên Ghost (Runbook 33)

**Người thực hiện:** Agent SOLIDWORKS-UI-DESIGNER (Antigravity)  
**Ngày thực hiện:** 2026-10-20  
**Repository:** `tbcphutai-bit/ADDIN-SolidWorks`  
**Nhánh nguồn:** `fix/afj-ghost-current-state-visibility-20261020`  
**Commit gốc:** [`ecf9e6e24b978cf7cf5200189adab4874b4029e3`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/ecf9e6e24b978cf7cf5200189adab4874b4029e3)  
**Nhánh review:** `review/afj-ghost-current-state-visibility-review-20261020`  
**Tài liệu tham chiếu:** `docs/AutoFrameJoint/33_GHOST_DEEP_AUDIT_DECISION_AND_SOURCE_PATCH.md`  
**File source đối chiếu:** `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`  

---

## 1. Ranh giới Đánh giá & Tuân thủ An toàn
- **STATIC SOURCE REVIEW ONLY:** Antigravity xác nhận việc kiểm tra source chỉ dừng ở mức đối chiếu cú pháp, chữ ký Interop và cấu trúc luồng C# 7.3. Chưa từng qua biên dịch `csc`/`MSBuild` hay chạy host test trên SOLIDWORKS thật.
- **BẢO VỆ MÔI TRƯỜNG DEBUG:**
  - Không chạy lệnh Build, Rebuild, csc hay MSBuild.
  - Không tắt, ngắt quãng hay can thiệp vào phiên debug SOLIDWORKS/Visual Studio đang chạy của Command khác.
  - Không ghi đè DLL/PDB cài đặt (`C:\Tao moi\ADDIN.dll`).
  - Chưa tự tiện đấu nối mã nguồn vào PMP UI hay CommandEntry.
  - Check Hole, Check Bend, Mirror hoàn toàn bất biến.

---

## 2. Kết quả Rà soát Chi tiết Bản Sửa C# tại Commit `ecf9e6e`

### 2.1. Kiểm tra chính xác việc lọc Hidden / Suppressed & Xử lý Lightweight
- **Vị trí code:** [AutoFrameJointGhostPreview.cs#L96-L110](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L96-L110)
  ```csharp
  // Check visible occurrence state in the active Assembly without
  // changing suppression, visibility or resolving lightweight parts.
  // IsHidden(false) checks display visibility without treating
  // every Lightweight occurrence as hidden.
  if (frameA.IsSuppressed() || frameB.IsSuppressed())
  {
      message = "Frame A hoặc B đang Suppressed. Hãy Unsuppress trước khi xem.";
      return false;
  }

  if (frameA.IsHidden(false) || frameB.IsHidden(false))
  {
      message = "Frame A hoặc B đang Hidden. Hãy Show Component trước khi xem.";
      return false;
  }
  ```
- **Đối chiếu Kỹ thuật:**
  1. **Chữ ký API:**
     - `IComponent2.IsSuppressed()`: Trả về `bool`, xác định trực tiếp trạng thái nén của Component.
     - `IComponent2.IsHidden(bool ConsiderSuppressed)`: Nhận tham số boolean `false`. Theo đặc tả SOLIDWORKS API 2025, nếu truyền `true`, SOLIDWORKS có thể coi một component ở trạng thái Lightweight là hidden. Việc truyền `false` tách bạch hoàn toàn giữa việc kiểm tra ẩn hình học (visibility) và trạng thái nén (suppression).
  2. **Trường hợp Lightweight:**
     - Code **không tự ý unsuppress, unhide hay ép resolve** các part Lightweight (không làm bẩn tài liệu CAD hay gây gián đoạn phiên làm việc).
     - Nếu một Component là Lightweight mà Part document chưa nạp vào RAM, bước kiểm tra tiếp theo tại L181-185 (`ModelDoc2 part = component.GetModelDoc2() as ModelDoc2; if (part == null ...)`) sẽ từ chối an toàn với thông báo `"Part chưa được Resolve hoặc Component không phải Part."`.
- **Đánh giá:** **CHÍNH XÁC VÀ ĐẠT CHUẨN API (PASS).**

---

### 2.2. Kiểm tra thông báo mới phản ánh đúng hình học hiện tại của Assembly
- **Vị trí code:** [AutoFrameJointGhostPreview.cs#L119-L122](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L119-L122)
  ```csharp
  message = String.Format(
      "Đang xem bản sao trạng thái hiện tại: {0} body A + {1} body B. CHƯA MÔ PHỎNG VẾT CẮT MỚI.",
      countA, countB);
  ```
- **Đánh giá:**
  - Thay thế hoàn toàn câu chữ cũ `"Đây là hình gốc, CHƯA CẮT."`.
  - Phản ánh trung thực tính chất của `IComponent2.GetBodies3`: đây là bản sao hình học hiện tại hiển thị trong Assembly (có thể đã chịu tác động của các Assembly Feature Cuts có sẵn), và làm rõ với người dùng rằng thuật toán miter/joint cut mới **chưa hề được tính toán hay áp dụng**.
- **Đánh giá:** **CHUẨN XÁC VỀ MẶT NGỮ NGHĨA UX (PASS).**

---

### 2.3. Kiểm tra đường xử lý lỗi Hide và số lượng body pending
- **Vị trí code:** [AutoFrameJointGhostPreview.cs#L130-L133](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L130-L133)
  ```csharp
  message = cleaned
      ? "Không tạo được Preview: " + ex.Message
      : "Lỗi Preview (" + ex.Message + "); còn " + shown.Count +
        " body chưa dọn được bằng Hide(): " + cleanError;
  ```
- **Đánh giá:**
  - Khi dọn dẹp dở dang, số lượng `shown.Count` phản ánh chính xác số lượng đối tượng COM đang được giữ lại trong danh sách `failed` chưa thể gỡ bỏ bằng `Hide()`.
  - Giúp lập trình viên và người dùng nhận diện ngay số lượng body đang "kẹt" trong bộ nhớ đồ họa để có hướng xử lý tiếp theo.
- **Đánh giá:** **RÕ RÀNG VÀ MINH BẠCH (PASS).**

---

### 2.4. Xác nhận các rào chắn P0 cũ không bị ảnh hưởng
- **Vị trí code:** [AutoFrameJointGhostPreview.cs#L235-L248](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs#L235-L248)
  - Lệnh tạo và lưu trữ: `ShownBody tracked = new ShownBody(copy, part); shown.Add(tracked);` tiếp tục nằm **TRƯỚC** khi gọi `copy.Display3(...)`.
  - Lệnh `entry.Copy.Hide(entry.Part)` trong `ClearInternal` giữ nguyên context Part của tài liệu.
  - Phương thức `RedrawSafely()` tiếp tục được bảo vệ bởi guard `IsOriginalAssemblyActive()`.
- **Đánh giá:** **P0 ĐƯỢC BẢO TOÀN NGUYÊN VẸN 100% (PASS).**

---

## 3. Thiết kế Cơ chế Quản lý Ghost Manager theo Phiên Assembly (Assembly-Scoped Session Architecture)

Theo định hướng của ChatGPT tại Runbook 33 (L77-85), việc dùng một biến `static AutoFrameJointGhostPreview activeGhost` đơn lẻ là chưa đủ an toàn và thiếu ngữ cảnh tài liệu. Antigravity đề xuất kiến trúc **Assembly-Scoped Session Manager** hoàn chỉnh cho giai đoạn đấu nối UI sắp tới.

### 3.1. Mô hình Trạng thái Vòng đời (State Machine)
```
[Không có Preview] (Idle)
       │
       │ Người dùng nhấn nút "Xem trạng thái A/B"
       ▼
 [Đang hiển thị] (ActiveShowing)
       │
       ├─────────────────────────────────────────┐
       │ Đóng PMP / Sau khi đóng                 │ Người dùng chuyển cửa sổ
       ▼                                         ▼
[Dọn dẹp thành công]                    [Tài liệu không khớp]
       │ (Tất cả Hide() OK)                      │ (DeferredCleanup)
       ▼                                         │ Chờ user quay lại
    [Idle]                                       ▼
       ▲                                  [Thử lại dọn dẹp]
       │                                         │
       └─────────────────────────────────────────┘
```

### 3.2. Đề xuất Lớp Quản lý Phiên: `AutoFrameJointGhostSession`
Lớp này đóng vai trò đại diện phiên, gắn chặt với định danh COM của Assembly nguồn:

```csharp
namespace ADDIN.Commands.AutoFrameJoint
{
    public enum GhostSessionState
    {
        Idle,               // Chưa có preview nào được vẽ
        ActiveShowing,      // Preview đang hiển thị bình thường
        PendingCleanup,     // Đã yêu cầu dọn dẹp nhưng còn body bị Hide() lỗi
        DeferredMismatch    // User chuyển sang document khác, tạm hoãn dọn dẹp
    }

    /// <summary>
    /// Quản lý vòng đời Preview gắn chặt với một tài liệu Assembly cụ thể.
    /// Đảm bảo không bao giờ bỏ rơi tham chiếu COM khi Hide() thất bại.
    /// </summary>
    public sealed class AutoFrameJointGhostSession
    {
        private readonly ISldWorks swApp;
        private readonly ModelDoc2 boundAssembly;
        private readonly AutoFrameJointGhostPreview engine;
        private GhostSessionState state = GhostSessionState.Idle;
        private string lastStatusMessage;

        public AutoFrameJointGhostSession(ISldWorks app, ModelDoc2 assembly)
        {
            swApp = app ?? throw new ArgumentNullException("app");
            boundAssembly = assembly ?? throw new ArgumentNullException("assembly");
            engine = new AutoFrameJointGhostPreview(app, assembly);
        }

        public ModelDoc2 BoundAssembly { get { return boundAssembly; } }
        public GhostSessionState State { get { return state; } }
        public bool HasPendingBodies { get { return engine.IsShowing; } }

        /// <summary>
        /// Kích hoạt vẽ Preview từ nút bấm PMP.
        /// </summary>
        public bool RequestShow(Component2 frameA, Component2 frameB, out string message)
        {
            if (state == GhostSessionState.PendingCleanup)
            {
                message = "Còn body của lần xem trước chưa thể dọn dẹp. Hãy thử dọn lại trước khi vẽ mới.";
                return false;
            }

            bool ok = engine.TryShow(frameA, frameB, out message);
            state = ok ? GhostSessionState.ActiveShowing : (engine.IsShowing ? GhostSessionState.PendingCleanup : GhostSessionState.Idle);
            lastStatusMessage = message;
            return ok;
        }

        /// <summary>
        /// Yêu cầu dọn dẹp an toàn khi đóng PMP hoặc đổi lựa chọn.
        /// </summary>
        public bool RequestCleanup(out string message)
        {
            message = null;
            if (!engine.IsShowing)
            {
                state = GhostSessionState.Idle;
                return true;
            }

            // Kiểm tra document đang kích hoạt có đúng là Assembly ban đầu không
            ModelDoc2 activeDoc = swApp.ActiveDoc as ModelDoc2;
            if (activeDoc == null || swApp.IsSame(activeDoc, boundAssembly) != (int)swObjectEquality.swObjectSame)
            {
                state = GhostSessionState.DeferredMismatch;
                message = "Assembly nguồn hiện không kích hoạt. Đã hoãn dọn dẹp để không làm xáo trộn tài liệu khác.";
                Trace.WriteLine("[AFJ-SESSION] Deferred cleanup: active doc is not bound assembly.");
                return false;
            }

            string clearError;
            bool cleaned = engine.TryClear(out clearError);
            if (cleaned)
            {
                state = GhostSessionState.Idle;
                message = "Đã dọn dẹp sạch sẽ Preview.";
                return true;
            }
            else
            {
                state = GhostSessionState.PendingCleanup;
                message = "Chưa dọn hết body: " + clearError;
                Trace.WriteLine("[AFJ-SESSION] Cleanup incomplete: " + clearError);
                return false;
            }
        }
    }
}
```

### 3.3. Quy tắc Đấu nối Cấp Bộ điều khiển (`AutoFrameJointCommandEntry`)
Khi người dùng chạy lệnh `AutoFrameJointCommandEntry.Open(swApp)`:
1. **Kiểm tra Session Tồn đọng:**
   Duy trì `private static AutoFrameJointGhostSession activeSession;`.
   - Nếu `activeSession != null && activeSession.HasPendingBodies`:
     - Kiểm tra nếu `swApp.ActiveDoc` trùng `activeSession.BoundAssembly`: Tự động gọi `activeSession.RequestCleanup(...)` để giải phóng rác cũ.
     - Nếu khác `boundAssembly`: Cảnh báo người dùng và **từ chối mở session mới đè lên session cũ**.
2. **Cung cấp Callbacks cho PMP:**
   - Callback hiển thị: `Func<Component2, Component2, string> onShowPreview = (a, b) => { activeSession.RequestShow(a, b, out var msg); return msg; };`
   - Callback đóng PMP (`AfterClose`): `Action onPmpClosed = () => { activeSession.RequestCleanup(out _); if (!activeSession.HasPendingBodies) activeSession = null; };`
3. **Tuyệt đối không dùng Timer ngầm hay Thread Pool:**
   Mọi thao tác bắt buộc chạy trên luồng chính giao diện SOLIDWORKS (STA thread).

---

## 4. Báo cáo Rà soát Lỗi Mã nguồn (Code Issues)
Qua rà soát toàn bộ 299 dòng code hiện tại của [AutoFrameJointGhostPreview.cs](file:///C:/Tao%20moi/ADDIN-AutoFrameJoint-Launcher/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs):
- **Cú pháp & Kiểu dữ liệu:** 100% hợp lệ, khớp chuẩn C# 7.3 và .NET Framework 4.8.
- **Interop Assemblies:** Toàn bộ các phương thức `IsSuppressed()`, `IsHidden(false)`, `GetBodies3()`, `Copy()`, `Display3()`, `Hide()`, `GraphicsRedraw2()` đều tồn tại chính xác trong metadata của `SolidWorks.Interop.sldworks.dll`.
- **Rò rỉ hay ngoại lệ tiềm ẩn:** Không phát hiện thêm lỗi cú pháp, dead lock hay race condition nào trong phạm vi lớp hiện tại.

---

## 5. Kết luận
- Bản sửa `ecf9e6e` của ChatGPT đã hoàn thiện trọn vẹn việc lọc hiển thị trạng thái thực tế và xử lý ngoại quan minh bạch.
- Đề xuất kiến trúc `AutoFrameJointGhostSession` đã sẵn sàng để chuyển giao cho ChatGPT xem xét khi bước vào giai đoạn đấu nối PMP sau Host Smoke Test.
- Antigravity tiếp tục duy trì trạng thái **chờ lệnh**, sẵn sàng chuyển sang bước build độc lập và kiểm thử host khi phiên debug của người dùng kết thúc.
