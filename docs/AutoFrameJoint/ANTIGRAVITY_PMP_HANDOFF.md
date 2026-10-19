# ANTIGRAVITY PMP HANDOFF: BÁO CÁO XÁC MINH LAUNCHER SMOKE CHO CHATGPT REVIEW

- **Vai trò Agent:** `SOLIDWORKS-UI-DESIGNER`
- **Repository:** `tbcphutai-bit/ADDIN-SolidWorks`
- **Working copy folder:** `C:\Tao moi\ADDIN-AutoFrameJoint-Launcher`
- **Base Branch:** `feat/auto-frame-joint-host-launcher-smoke-20261019`
- **Base Commit (HEAD):** `1c6f27d015bdc5814d6dd2a27d5a45871eaf2ea6`
- **Parent Commit:** `edce4a555d1408d96a722b54668d60d9671ef14b`
- **Review Branch:** `review/antigravity-launcher-smoke-build-20261019`
- **Thời gian hoàn thành:** `2026-10-19T17:10:00+07:00`
- **Đối tượng review:** ChatGPT (Code Owner & Reviewer) — Tham chiếu `docs/AutoFrameJoint/23_LAUNCHER_SMOKE_RUNBOOK.md` & GitHub Issue #31

---

## 1. XÁC MINH DIFF MÃ NGUỒN CỦA COMMIT `1c6f27d`

Đã rà soát chi tiết 4 file C# / project được ChatGPT cập nhật trong commit `1c6f27d`:

### 1. `UI/AutoFrameJointTaskPaneLauncher.cs` (Mới tạo)
- `partial class BomTaskPaneControl`.
- Tạo một nút bấm duy nhất `btnAutoFrameJoint` dạng `ModernButtonKind.Tile` với biểu tượng `TaiIcon.Assembly`, tone `ModernButtonTone.Accent`, text `"Giao góc Frame"`, subtitle `"Chọn 2 thanh · chỉ phân tích"`.
- Guarded singleton: `EnsureAutoFrameJointButton()` kiểm tra `if (btnAutoFrameJoint != null || tabModelMacroPage == null) return;` đảm bảo nút chỉ được khởi tạo đúng 1 lần.
- Sự kiện Click:
  - Kiểm tra điều kiện kích hoạt an toàn qua `if (!CanRunModelMacro(btnAutoFrameJoint)) return;`.
  - Gọi `AutoFrameJointCommandEntry.Open(swApp)`.
  - Bọc khối `try/catch` bắt `Exception`, xuất `System.Diagnostics.Trace.WriteLine("[AFJ-LAUNCH] ...")` và hiển thị `MessageBox` cảnh báo, không làm crash tiến trình SOLIDWORKS.
- Gắn ToolTip mô tả rõ: *"Chọn Frame A và Frame B trong Assembly. Chỉ phân tích sơ bộ; chưa Preview 3D và chưa cắt Part."*.

### 2. `UI/BomCommandLayout.cs` (3 điểm sửa nhỏ)
- Trong `EnsureModelMacroControls()`: Gọi `EnsureAutoFrameJointButton()` ngay sau `EnsureBendMinimumButton()`.
- Thêm `btnAutoFrameJoint` vào danh sách chuyển sang thẻ `cardMacroCommands` ("Lệnh macro").
- Trong `LayoutModelMacroViewport()`: Thêm `btnAutoFrameJoint` vào mảng `macroTiles` để tính toán vị trí hiển thị và đồng bộ trạng thái ẩn/hiện khi thu gọn/mở rộng card macro.

### 3. `BomTaskPaneControl.cs` (2 điểm sửa nhỏ)
- Trong `IsModelMacroSupported(Control button, int documentType)`:
  ```csharp
  if (ReferenceEquals(button, btnCheckAssemblyHole) ||
      ReferenceEquals(button, btnAutoFrameJoint))
      return documentType == (int)swDocumentTypes_e.swDocASSEMBLY;
  ```
  => Nút `btnAutoFrameJoint` chỉ được hỗ trợ khi tài liệu hiện hành là Assembly (`swDocASSEMBLY`).
- Trong `UpdateModelMacroCommandAvailability(int documentType)`:
  Gọi `UpdateModelMacroButton(btnAutoFrameJoint, documentType, "Assembly");` => Khi không mở Assembly (Part/Drawing hoặc không có tài liệu), nút bị vô hiệu hóa (Disabled) và Tooltip hướng dẫn yêu cầu mở Assembly.

### 4. `ADDIN.csproj` (1 dòng cấu hình)
- Thêm `<Compile Include="UI\AutoFrameJointTaskPaneLauncher.cs" />`.
- Giữ nguyên toàn bộ cấu hình biên dịch và các file P0/P1 hiện có.

---

## 2. KIỂM SOÁT POSTBUILDEVENT & CÔ LẬP ĐẦU RA BIÊN DỊCH

### Phân tích cơ chế Build của `ADDIN.csproj`:
1. Dòng 42: `<PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Debug|x64'">` thiết lập `<OutputPath>C:\Tao moi\</OutputPath>`.
2. Dòng 599: `<PostBuildEvent>copy /Y "$(TargetDir)$(TargetName).dll" "C:\Tao moi\ADDIN.dll" &amp; copy /Y "$(TargetDir)$(TargetName).pdb" "C:\Tao moi\ADDIN.pdb" 2&gt;nul &amp; set errorlevel=0</PostBuildEvent>`.
3. Nếu chỉ chạy MSBuild thông thường hoặc chỉ tắt `PostBuildEvent`, compiler `Csc` vẫn sẽ ghi trực tiếp vào `C:\Tao moi\ADDIN.dll`.

### Giải pháp cô lập tuyệt đối theo Runbook 23:
- Sử dụng thuộc tính toàn cục MSBuild (Global Properties) để ghi đè:
  - `/p:OutputPath="bin\x64\Debug\\"` (hướng đầu ra vào thư mục nội bộ của worktree)
  - `'/p:PostBuildEvent='` và `/p:RunPostBuildEvent=Never` (vô hiệu hóa hoàn toàn lệnh copy post-build).
- Đã sao lưu dự phòng trước khi build vào `C:\Tao moi\backup_addin_pre_launcher_smoke`.
- Xác nhận sau build: Tệp cài đặt `C:\Tao moi\ADDIN.dll` và `C:\Tao moi\ADDIN.pdb` được bảo vệ nguyên vẹn 100%, không bị ghi đè.

---

## 3. BẰNG CHỨNG BIÊN DỊCH THỰC TẾ (BUILD EVIDENCE)

### Lệnh thực thi:
```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild "C:\Tao moi\ADDIN-AutoFrameJoint-Launcher\ADDIN.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /p:OutputPath="bin\x64\Debug\\" '/p:PostBuildEvent=' /p:RunPostBuildEvent=Never /v:minimal
```

### Kết quả biên dịch:
- **Thời điểm hoàn thành:** `2026-10-19 17:09:36 +07:00`
- **Trình biên dịch:** Microsoft (R) MSBuild version 18.10.1 (Visual Studio 2026 Community / .NET Framework 4.8 x64).
- **Mã thoát (Exit Code):** `0` (**BUILD PASS**)
- **Lỗi biên dịch:** `0 lỗi (Zero Errors)`
- **Cảnh báo (Warnings):** `0 cảnh báo` từ bất kỳ file nào thuộc phân hệ `AutoFrameJoint/`.
- **Tập tin đầu ra thử nghiệm (Isolated Output):**
  - **Đường dẫn:** `C:\Tao moi\ADDIN-AutoFrameJoint-Launcher\bin\x64\Debug\ADDIN.dll`
  - **Kích thước:** `7,171,072 bytes`
  - **Thời gian tạo:** `2026/10/19 17:08:50`
  - **SHA256:** `643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B`
- **Tập tin Add-in đang sử dụng tại máy (Installed Add-in - UNTOUCHED):**
  - **Đường dẫn:** `C:\Tao moi\ADDIN.dll`
  - **Kích thước:** `7,170,048 bytes`
  - **Thời gian ghi:** `2026/10/19 16:49:19`
  - **SHA256:** `F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126`

---

## 4. XÁC NHẬN MÃ NGUỒN C# (SOURCE DIFF AUDIT)

```powershell
git diff 1c6f27d015bdc5814d6dd2a27d5a45871eaf2ea6 -- UI/AutoFrameJointTaskPaneLauncher.cs UI/BomCommandLayout.cs BomTaskPaneControl.cs ADDIN.csproj
```

**KẾT QUẢ XÁC NHẬN:**
> **NO SOURCE CHANGES SINCE 1c6f27d**
>
> Toàn bộ mã nguồn C# và project của commit `1c6f27d` do ChatGPT viết đều chuẩn xác, biên dịch thành công mà không cần chỉnh sửa bất kỳ dòng code nào.

---

## 5. GIỚI HẠN KIỂM TRA TRONG SOLIDWORKS (HOST LIMITATIONS)

> **NOT TESTED IN SOLIDWORKS**
>
> Hiện tại DLL thử nghiệm mới biên dịch **CHƯA ĐƯỢC NẠP VÀO TIẾN TRÌNH SOLIDWORKS**.
> - Cần sự đồng ý rõ ràng từ người dùng trước khi tiến hành bước nạp thử nghiệm in-process.
> - Khi được phê duyệt, sẽ tiến hành quy trình Smoke Test theo Runbook 23 trên một bản sao của file Assembly thử nghiệm (không thao tác trên file sản xuất).
> - Tính năng hoàn toàn là READ ONLY: không tạo Preview 3D, không cắt Part, không thay đổi cấu hình hay trạng thái lưu của mô hình CAD.
