# ANTIGRAVITY HOST SMOKE REPORT: STOP TẠI BƯỚC INSPECT DO SHA256 KHÔNG KHỚP

- **Vai trò Agent:** `SOLIDWORKS-UI-DESIGNER`
- **Repository:** `tbcphutai-bit/ADDIN-SolidWorks`
- **Working copy folder:** `C:\Tao moi\ADDIN-AutoFrameJoint-Launcher`
- **Base Branch:** `docs/auto-frame-joint-host-deploy-restore-20261019`
- **Base Commit (HEAD):** `b9d128c3eede2ae2be6b7c1f39b9ee3717a03192`
- **Review Branch:** `review/antigravity-host-smoke-inspect-stop-20261019`
- **Thời gian:** `2026-10-19T18:07:00+07:00`
- **Đối tượng báo cáo:** ChatGPT (Code Owner & Reviewer) và Người dùng

---

## 1. KẾT QUẢ CHẠY INSPECT (READ ONLY)

Lệnh thực thi:
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\AutoFrameJointHostSmoke\Invoke-AutoFrameJointHostTest.ps1" -Mode Inspect
```

Kết quả xuất ra nguyên văn:
```text
=== READ ONLY INSPECTION ===
Installed: C:\Tao moi\ADDIN.dll
Test:      C:\Tao moi\ADDIN-AutoFrameJoint-Launcher\bin\x64\Debug\ADDIN.dll
SOLIDWORKS running: False
CodeBase(s): C:\Tao moi\ADDIN.DLL
Installed SHA256: D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E
Test SHA256:      643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B
Expected installed: F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126
Expected test:      643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B
NO FILES MODIFIED
```

---

## 2. PHÂN TÍCH NGUYÊN NHÂN LỆCH SHA256 CỦA INSTALLED DLL

1. **Khớp nối thành công:**
   - `CodeBase(s)` trong Windows Registry: Khớp chính xác `C:\Tao moi\ADDIN.DLL`.
   - `SOLIDWORKS running`: `False` (đã đóng hoàn toàn).
   - `Test SHA256`: Khớp 100% mã hash đã biên dịch `643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B`.

2. **Lệch SHA256 tại `Installed DLL`:**
   - **Kỳ vọng:** `F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126` (bản Debug 16:49:19, kích thước 7,170,048 bytes).
   - **Thực tế:** `D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E` (kích thước 6,565,376 bytes, thời gian ghi: `2026/10/19 17:50:29`).

3. **Truy vết nguồn gốc:**
   - Vào lúc `17:50:29`, một tác vụ biên dịch Release x64 cho phân hệ Check Hole CG1 (nhánh `feat/check-hole-connection-groups-cg1`, commit `f964be8`) đã thực thi trong thư mục `C:\Tao moi\ADDIN`.
   - Do `ADDIN.csproj` có lệnh `PostBuildEvent` mặc định copy file ra `C:\Tao moi\ADDIN.dll`, bản Release này đã ghi đè lên file cài đặt tại `C:\Tao moi\ADDIN.dll`.
   - Bản sao lưu nguyên bản của file có hash `F8D0A4AC...` đã được lưu trữ an toàn từ trước tại:
     `C:\Tao moi\backup_addin_pre_launcher_smoke\ADDIN.dll` (SHA256: `F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126`).

---

## 3. THỰC HIỆN TIÊU CHÍ DỪNG (STOP CRITERIA) THEO RUNBOOK 24

Theo đúng chỉ dẫn trong `docs/AutoFrameJoint/24_HOST_SMOKE_DEPLOY_RESTORE.md`:
> *"3. If installed DLL hash differs from report F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126, stop. If test DLL hash differs from 643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B, stop. Re-review current binaries rather than overriding checks."*
>
> *"Nếu có bất kỳ đường dẫn hoặc SHA256 nào không khớp, phải dừng và báo ChatGPT. Không được tự thay đổi script để bỏ qua kiểm tra an toàn."*

**Antigravity đã dừng lại ngay tại bước Inspect, KHÔNG chạy lệnh Deploy, KHÔNG ghi đè bất kỳ file nào.**

---

## 4. ĐỀ XUẤT HƯỚNG XỬ LÝ CHO CHATGPT & NGƯỜI DÙNG

1. **Phương án 1 (Khôi phục file Debug F8D0A4AC trước khi Deploy):**
   - Chép lại file từ `C:\Tao moi\backup_addin_pre_launcher_smoke\ADDIN.dll` và `ADDIN.pdb` vào `C:\Tao moi\`.
   - Khi đó hash khớp `F8D0A4AC...`, script `Invoke-AutoFrameJointHostTest.ps1` sẽ chạy Deploy và Backup/Restore đúng theo thiết kế ban đầu của Runbook 24.

2. **Phương án 2 (Cập nhật hash mong đợi cho bản Release D9257D34):**
   - Nếu người dùng muốn giữ bản Release CG1 hiện tại làm bản cài đặt gốc (để sau khi smoke test xong sẽ restore về bản này), ChatGPT cập nhật `$ExpectedInstalledSha256 = 'D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E'` trong script.

Chờ quyết định chính thức từ ChatGPT và Người dùng trước khi tiếp tục.
