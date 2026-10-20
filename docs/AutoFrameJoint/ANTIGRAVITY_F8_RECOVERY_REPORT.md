# ANTIGRAVITY BÁO CÁO KHÔI PHỤC GUARDED RECOVER CG2 VỀ DEBUG F8 (PHASE R1)

- **Vai trò Agent:** `SOLIDWORKS-UI-DESIGNER`
- **Repository:** `tbcphutai-bit/ADDIN-SolidWorks`
- **Working copy folder:** `C:\Tao moi\ADDIN-AutoFrameJoint-Launcher`
- **Base Branch:** `fix/afj-guarded-recover-f8-from-cg2-20261020`
- **Base Commit (HEAD):** `3c88d8720b1a7e0bdb7467cd367236e1aec37a8f`
- **Review Branch:** `review/antigravity-f8-recovery-report-20261020`
- **Thời gian hoàn thành:** `2026-10-20T09:08:00+07:00`
- **Đối tượng báo cáo:** ChatGPT (Code Owner & Reviewer) và Người dùng — Tham chiếu `docs/AutoFrameJoint/28_GUARDED_RECOVER_CG2_TO_F8.md` & GitHub Issue #31

---

## 1. THÔNG TIN MÃ NGUỒN VÀ KỊCH BẢN KHÔI PHỤC

- **Nhánh chỉ đạo:** `fix/afj-guarded-recover-f8-from-cg2-20261020`
- **Commit:** `3c88d8720b1a7e0bdb7467cd367236e1aec37a8f`
- **Kịch bản thực thi:** `tools/AutoFrameJointHostSmoke/Recover-PlanAF8FromCG2.ps1`
- **C# Source Diffs:** **ZERO (Không thay đổi bất kỳ file mã nguồn C# hay dự án nào)**.

---

## 2. KẾT QUẢ KIỂM TRA CHỈ ĐỌC (INSPECT MODE)

Trước khi thực hiện khôi phục, tiến trình nền Visual Studio (PID 22120) đã được người dùng đóng an toàn qua Task Manager.
Lệnh thực thi Inspect:
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Tao moi\ADDIN-AutoFrameJoint-Launcher\tools\AutoFrameJointHostSmoke\Recover-PlanAF8FromCG2.ps1" -Mode Inspect
```

**Kết quả màn hình console:**
```text
COM CodeBase=C:\Tao moi\ADDIN.DLL
COM CodeBase=C:\Tao moi\ADDIN.DLL
CG2 archived DLL=0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC PDB=C73F9B713AB3279E068AA8F98D7E3C979BBB72EE992A57E6CF1C51054E84A128
F8 archived DLL=F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126 PDB=754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097
Manifest F8 DLL=F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126 PDB=754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097
CURRENT DLL=0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC
CURRENT PDB=C73F9B713AB3279E068AA8F98D7E3C979BBB72EE992A57E6CF1C51054E84A128
READ_ONLY_INSPECT_COMPLETED
```

- **Tiến trình cạnh tranh (Writers):** 0 (Không còn `SLDWORKS`, `devenv`, `MSBuild`, `dotnet`, `csc`, `VBCSCompiler`).
- **Mọi hash xác thực:** Khớp chính xác 100%.

---

## 3. KẾT QUẢ THỰC THI KHÔI PHỤC (RECOVER MODE)

Lệnh thực thi:
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Tao moi\ADDIN-AutoFrameJoint-Launcher\tools\AutoFrameJointHostSmoke\Recover-PlanAF8FromCG2.ps1" -Mode Recover -Approved -ConfirmReplaceInstalledCG2
```

**Nhật ký thực thi (Raw Console Output):**
```text
COM CodeBase=C:\Tao moi\ADDIN.DLL
COM CodeBase=C:\Tao moi\ADDIN.DLL
CG2 archived DLL=0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC PDB=C73F9B713AB3279E068AA8F98D7E3C979BBB72EE992A57E6CF1C51054E84A128
F8 archived DLL=F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126 PDB=754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097
Manifest F8 DLL=F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126 PDB=754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097
CURRENT DLL=0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC
CURRENT PDB=C73F9B713AB3279E068AA8F98D7E3C979BBB72EE992A57E6CF1C51054E84A128
Independent CG2 backup DLL=0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC PDB=C73F9B713AB3279E068AA8F98D7E3C979BBB72EE992A57E6CF1C51054E84A128
Staged F8 DLL=F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126 PDB=754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097
Last installed CG2 check DLL=0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC PDB=C73F9B713AB3279E068AA8F98D7E3C979BBB72EE992A57E6CF1C51054E84A128
Restored F8 DLL=F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126 PDB=754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097
RECOVERY_PASS: restored F8 DLL and PDB.
RECOVERY_JOURNAL=C:\Tao moi\ADDIN-AutoFrameJoint-HostBackup\recover-cg2-to-f8-20261020-090722-026\recovery.json
CG2 backup preserved. DO NOT run original AFJ Restore again.
```

- **Mã thoát (Exit Code):** `0` (**RECOVERY_PASS**)
- **Tệp nhật ký khôi phục:** `C:\Tao moi\ADDIN-AutoFrameJoint-HostBackup\recover-cg2-to-f8-20261020-090722-026\recovery.json`

---

## 4. XÁC NHẬN HASH SAU KHÔI PHỤC TẠI VỊ TRÍ CÀI ĐẶT

Lệnh kiểm tra độc lập trong PowerShell:
```powershell
Get-FileHash "C:\Tao moi\ADDIN.dll" -Algorithm SHA256
Get-FileHash "C:\Tao moi\ADDIN.pdb" -Algorithm SHA256
Get-Item "C:\Tao moi\ADDIN.dll", "C:\Tao moi\ADDIN.pdb" | Select-Object FullName, Length, LastWriteTime
```

**Kết quả thực tế:**
- **`C:\Tao moi\ADDIN.dll`:**
  - **SHA256:** `F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126`
  - **Kích thước:** `7,170,048 bytes`
  - **Thời gian gốc:** `2026/10/19 16:49:19`
- **`C:\Tao moi\ADDIN.pdb`:**
  - **SHA256:** `754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097`
  - **Kích thước:** `10,345,984 bytes`
  - **Thời gian gốc:** `2026/10/19 16:49:19`

=> **Khớp chính xác 100% với bản Debug F8 Baseline của Phương án A.**

---

## 5. BẢO TOÀN CÁC BẢN SAO LƯU CG1 VÀ CG2

Các bản sao lưu được lưu trữ độc lập và bảo vệ nguyên vẹn:
1. **Bản snapshot CG2 mới nhất (do kịch bản khôi phục tạo):**
   - Thư mục: `C:\Tao moi\ADDIN-AutoFrameJoint-HostBackup\recover-cg2-to-f8-20261020-090722-026\`
   - Tệp: `installed.pre-recovery.cg2.dll` (`0A41103C...`), `installed.pre-recovery.cg2.pdb` (`C73F9B71...`).
2. **Bản lưu trữ độc lập CG2 trước đó:**
   - Thư mục: `C:\Tao moi\backup_cg2_debug_20261019_2137\`
   - Tệp: `ADDIN.dll` (`0A41103C...`), `ADDIN.pdb` (`C73F9B71...`).
3. **Bản lưu trữ CG1 Release:**
   - Thư mục: `C:\Tao moi\backup_cg1_release_snapshot\`
   - Tệp: `ADDIN.dll` (`D9257D34...`), `ADDIN.pdb` (`A35F4518...`).

---

## 6. CÁC PHẦN CHƯA THỰC HIỆN (NOT TESTED) & TUÂN THỦ NGUYÊN TẮC

- **SOLIDWORKS Host Smoke Test:** **NOT TESTED** (Người dùng yêu cầu khôi phục về Debug F8 trước khi kiểm thử).
- **Auto Frame Joint Deploy:** Đã được thu hồi hoàn toàn về trạng thái an toàn.
- **Không chạy lại lệnh Restore cũ.**
- **Không chỉnh sửa mã nguồn bất kỳ Command nào, không merge vào main.**
