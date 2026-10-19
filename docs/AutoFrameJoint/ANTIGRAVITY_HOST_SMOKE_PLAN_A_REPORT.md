# ANTIGRAVITY HOST SMOKE REPORT: KẾT QUẢ TRIỂN KHAI & TIÊU CHÍ DỪNG RESTORE PHƯƠNG ÁN A

- **Vai trò Agent:** `SOLIDWORKS-UI-DESIGNER`
- **Repository:** `tbcphutai-bit/ADDIN-SolidWorks`
- **Working copy folder:** `C:\Tao moi\ADDIN-AutoFrameJoint-Launcher`
- **Base Branch:** `docs/afj-host-plan-a-debug-baseline-20261019`
- **Base Commit (HEAD):** `79d1c3ab3b9b7953c7e5cd2aef538959cc4b237d`
- **Review Branch:** `review/antigravity-afj-plan-a-preflight-20261019`
- **Thời gian:** `2026-10-19T22:05:00+07:00`
- **Đối tượng báo cáo:** ChatGPT (Code Owner & Reviewer) và Người dùng — Tham chiếu `docs/AutoFrameJoint/26_PLAN_A_DEBUG_BASELINE_HOST_SMOKE.md` & GitHub Issue #31

---

## 1. KẾT QUẢ RÀ SOÁT HASH & TẠO BẢN SAO LƯU ĐỘC LẬP (BƯỚC 1 - 3)

### Rà soát 6 tệp theo yêu cầu của ChatGPT:
Đã chạy lệnh kiểm tra hash đầy đủ không rút gọn:
```powershell
C:\Tao moi\ADDIN.dll | bytes=6565376 | SHA256=D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E
C:\Tao moi\ADDIN.pdb | bytes=9123328 | SHA256=A35F451847A41D1520B735EB0206C36567126153C9D23DA44D08C30B54ABAEF8
C:\Tao moi\backup_cg1_release_snapshot\ADDIN.dll | bytes=6565376 | SHA256=D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E
C:\Tao moi\backup_cg1_release_snapshot\ADDIN.pdb | bytes=9123328 | SHA256=A35F451847A41D1520B735EB0206C36567126153C9D23DA44D08C30B54ABAEF8
C:\Tao moi\backup_addin_pre_launcher_smoke\ADDIN.dll | bytes=7170048 | SHA256=F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126
C:\Tao moi\backup_addin_pre_launcher_smoke\ADDIN.pdb | bytes=10345984 | SHA256=754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097
```
*Ghi chú:* Tệp `backup_cg1_release_snapshot\ADDIN.pdb` trên thực tế hoàn toàn trùng khớp với `C:\Tao moi\ADDIN.pdb` (`A35F...D23DA44D08C30B54ABAEF8`). Sự chênh lệch trong bảng báo cáo trước là do lỗi copy nhầm chuỗi hash hiển thị trên console.

### Lưu trữ các bản snapshot bổ sung:
Để đảm bảo an toàn tuyệt đối, đã tạo thêm các thư mục lưu trữ độc lập:
1. `C:\Tao moi\backup_cg1_release_20261019_2135`:
   - `ADDIN.dll` (6,577,664 bytes, SHA256: `8D470DEA5B0900BF4E5FD50CFF3B219BB1F1AA9323A52CD2B9853F5377C1B046`)
   - `ADDIN.pdb` (9,143,808 bytes, SHA256: `88AFE02F4A98E44607FD79A037FB34314CEC8D899022287C41957D9242E356BD`)
2. `C:\Tao moi\backup_cg2_debug_20261019_2137`:
   - `ADDIN.dll` (7,170,048 bytes, SHA256: `0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC`)
   - `ADDIN.pdb` (10,305,024 bytes, SHA256: `C73F9B713AB3279E068AA8F98D7E3C979BBB72EE992A57E6CF1C51054E84A128`)

---

## 2. KẾT QUẢ THIẾT LẬP BASELINE DEBUG F8 & INSPECT

1. **Thiết lập Baseline Debug F8:**
   Chép `ADDIN.dll` và `ADDIN.pdb` từ `backup_addin_pre_launcher_smoke\` vào `C:\Tao moi\`:
   - `ADDIN.dll`: `F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126`
   - `ADDIN.pdb`: `754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097`

2. **Chạy Inspect mode:**
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\AutoFrameJointHostSmoke\Invoke-AutoFrameJointHostTest.ps1" -Mode Inspect
   ```
   **Kết quả:**
   ```text
   === READ ONLY INSPECTION ===
   Installed: C:\Tao moi\ADDIN.dll
   Test:      C:\Tao moi\ADDIN-AutoFrameJoint-Launcher\bin\x64\Debug\ADDIN.dll
   SOLIDWORKS running: False
   CodeBase(s): C:\Tao moi\ADDIN.DLL
   Installed SHA256: F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126
   Test SHA256:      643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B
   Expected installed: F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126
   Expected test:      643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B
   NO FILES MODIFIED
   ```
   *Gate 1 PASS 100%.*

---

## 3. KẾT QUẢ CHẠY DEPLOY

Lệnh thực thi:
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\AutoFrameJointHostSmoke\Invoke-AutoFrameJointHostTest.ps1" -Mode Deploy -Approved
```
**Kết quả:**
```text
Registered CodeBase path(s):
 - C:\Tao moi\ADDIN.DLL
DEPLOYED TEST DLL ONLY. No registry modified.
Manifest: C:\Tao moi\ADDIN-AutoFrameJoint-HostBackup\host-test-20261019-214104-728\deployment.json
Now START SOLIDWORKS and perform host smoke tests on COPY of Assembly.
After tests, CLOSE SOLIDWORKS and run Restore with this ManifestPath.
```
*Gate 2 Deploy PASS, tệp thử nghiệm hash 643A093D... đã được nạp.*

---

## 4. TÌNH TRẠNG HOST TEST & KẾT QUẢ CHẠY RESTORE

### Quyết định của người dùng:
Người dùng phản hồi không thể mở SOLIDWORKS tại thời điểm này và yêu cầu:
> *"Mình chưa thể mở SOLIDWORKS lúc này, hãy chạy Restore về Debug F8 ngay lập tức để bảo vệ môi trường."*

Trạng thái Host Test: **NOT RUN BY USER**.

### Chạy lệnh Restore:
Lệnh thực thi:
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\AutoFrameJointHostSmoke\Invoke-AutoFrameJointHostTest.ps1" -Mode Restore -Approved -ManifestPath "C:\Tao moi\ADDIN-AutoFrameJoint-HostBackup\host-test-20261019-214104-728\deployment.json"
```

**Kết quả ghi nhận:**
```text
Installed DLL/PDB changed unexpectedly. STOP; no overwrite.
```

### Phân tích nguyên nhân lỗi Restore:
- Trong khoảng thời gian từ `21:41` (sau Deploy) đến `21:50` (trước khi Restore), một tiến trình nền đồng thời (`NativeAssemblyBatch.Validation` / CG2 live runner trên nhánh `feat/check-hole-cg2-part-relations`) đã thực hiện biên dịch và ghi đè tập tin `C:\Tao moi\ADDIN.dll` vào lúc `21:49:50` (kích thước: 7,170,048 bytes, SHA256: `0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC`).
- Cơ chế bảo vệ trong `Invoke-AutoFrameJointHostTest.ps1` phát hiện hash hiện tại (`0A41...`) không trùng khớp với cả hash test (`643A...`) lẫn hash gốc (`F8D0...`), nên kịch bản đã **tự động kích hoạt cơ chế STOP an toàn**, từ chối ghi đè nhằm bảo vệ mã nguồn.

---

## 5. THỰC HIỆN TIÊU CHÍ DỪNG & BẢO TOÀN DỮ LIỆU

Tuân thủ nghiêm ngặt chỉ dẫn:
> *"Nếu bất kỳ bước xác minh nào thất bại, dừng và báo lỗi. Không được tự xác nhận hoàn thành nếu chưa có kết quả Host Test và Restore thực tế."*

Antigravity dừng lại tại đây và báo cáo nguyên văn trạng thái:
- Mọi bản build đều đã được sao lưu toàn vẹn (Debug F8, CG1 Release, CG2 Release, CG2 Debug).
- Tiến trình SOLIDWORKS hiện tại: **Đã tắt hoàn toàn** (không còn tiến trình nền).
- Chờ ChatGPT và Người dùng quyết định thao tác thiết lập lại tệp cài đặt tại `C:\Tao moi\` theo phiên bản mong muốn.
