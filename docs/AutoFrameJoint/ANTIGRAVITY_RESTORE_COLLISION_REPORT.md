# ANTIGRAVITY BÁO CÁO RÀ SOÁT XUNG ĐỘT DLL/PDB (PHASE R0 - READ ONLY)

- **Vai trò Agent:** `SOLIDWORKS-UI-DESIGNER`
- **Repository:** `tbcphutai-bit/ADDIN-SolidWorks`
- **Working copy folder:** `C:\Tao moi\ADDIN-AutoFrameJoint-Launcher`
- **Base Branch:** `review/afj-restore-collision-preflight-20261019`
- **Base Commit (HEAD):** `b98e217c7c92fc1bbafefaef1b48994a5e5ee3a1`
- **Review Branch:** `review/antigravity-afj-collision-inspection-20261019`
- **Thời gian hoàn thành:** `2026-10-19T22:58:00+07:00`
- **Đối tượng báo cáo:** ChatGPT (Code Owner & Reviewer) và Người dùng — Tham chiếu `docs/AutoFrameJoint/27_RESTORE_COLLISION_CODE_REVIEW.md` & GitHub Issue #31

---

## 1. KẾT QUẢ THỰC THI SCRIPT KIỂM TRA CHỈ ĐỌC (RAW OUTPUT)

Lệnh thực thi trong PowerShell:
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\AutoFrameJointHostSmoke\Inspect-HostCollision.ps1"
```

Toàn văn kết quả xuất ra màn hình console:
```text
=== READ-ONLY COLLISION PREFLIGHT ===
Time: 2026-10-19 22:57:02 +07:00
No writes or restore calls occur in this script.
RUNNING devenv: 22120
RUNNING powershell: 54388,67772

## Currently installed (possible CG2 overwrite)
DLL exists=True bytes=7170048 SHA256=0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC
PDB exists=True bytes=10305024 SHA256=C73F9B713AB3279E068AA8F98D7E3C979BBB72EE992A57E6CF1C51054E84A128

## Archived Debug F8
DLL exists=True bytes=7170048 SHA256=F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126
PDB exists=True bytes=10345984 SHA256=754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097

## Archived CG1 Release
DLL exists=True bytes=6565376 SHA256=D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E
PDB exists=True bytes=9123328 SHA256=A35F451847A41D1520B735EB0206C36567126153C9D23DA44D08C30B54ABAEF8

## Archived CG2 Debug
DLL exists=True bytes=7170048 SHA256=0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC
PDB exists=True bytes=10305024 SHA256=C73F9B713AB3279E068AA8F98D7E3C979BBB72EE992A57E6CF1C51054E84A128

## Isolated AFJ test
DLL exists=True bytes=7171072 SHA256=643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B
PDB exists=True bytes=10356224 SHA256=4E3D5AAB1537DA67F26A6EAC30AD77679441102EC9B42A4572EE007A0DEE7A66

=== PINNED HASH CHECKS ===
Archived Debug F8 matches known DLL: True
Archived CG1 matches known DLL: True
Archived CG2 Debug matches known DLL: True
Isolated AFJ matches known DLL: True
Installed equals archived CG2 pair: True
Installed equals archived F8 pair: False
Installed equals AFJ test pair: False

=== DEPLOY MANIFEST READ ONLY ===
Manifest sourceGitCommit: 1c6f27d015bdc5814d6dd2a27d5a45871eaf2ea6
Manifest originalDllHash: F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126
Manifest originalPdbHash: 754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097
Manifest testDllHash: 643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B
Manifest testPdbHash: 4E3D5AAB1537DA67F26A6EAC30AD77679441102EC9B42A4572EE007A0DEE7A66
Manifest backup DLL hash actual: F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126
Manifest backup PDB hash actual: 754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097
Manifest backup pair matches recorded: True
Manifest original DLL corresponds to archived F8: True
Manifest original PDB corresponds to archived F8: True

END OF READ-ONLY INSPECTION.
DO NOT run legacy Restore while a different CG2 build is installed.
```

---

## 2. ĐỐI CHIẾU VÀ XÁC ĐỊNH BẢN BUILD ĐANG NẰM TẠI VỊ TRÍ CÀI ĐẶT

1. **Tập tin đang nằm tại `C:\Tao moi\ADDIN.dll` và `C:\Tao moi\ADDIN.pdb`:**
   - **Chính xác là cặp build CG2 Debug** biên dịch từ nhánh `feat/check-hole-cg2-part-relations` lúc `21:49:50`.
   - Cả DLL lẫn PDB đang cài đặt **trùng khớp 100% từng byte** với bản lưu trữ tại `C:\Tao moi\backup_cg2_debug_20261019_2137`:
     - DLL: `0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC` (7,170,048 bytes)
     - PDB: `C73F9B713AB3279E068AA8F98D7E3C979BBB72EE992A57E6CF1C51054E84A128` (10,305,024 bytes)
   - Kết quả đối chiếu script: `Installed equals archived CG2 pair: True`.
   - **Không có hiện tượng lệch cặp hay pha trộn (mixed pair)** giữa DLL và PDB của các bản build khác nhau.

2. **Xác nhận trạng thái Manifest và Bản sao lưu F8:**
   - Tệp Manifest `C:\Tao moi\ADDIN-AutoFrameJoint-HostBackup\host-test-20261019-214104-728\deployment.json` nguyên vẹn.
   - Bản sao lưu `ADDIN.original.dll` và `ADDIN.original.pdb` trong thư mục Manifest trùng khớp 100% với bản lưu trữ gốc `Archived Debug F8` (`F8D0A4AC...` và `754571AA...`).

---

## 3. TRẠNG THÁI TIẾN TRÌNH & TUÂN THỦ NGUYÊN TẮC AN TOÀN

1. **Tiến trình hệ thống:**
   - `SLDWORKS.exe`: **Không chạy** (Count = 0).
   - `MSBuild`, `csc`, `VBCSCompiler`: **Không có tiến trình biên dịch nào đang chạy**.
   - `devenv` (PID 22120): Đang mở giao diện Visual Studio.
2. **Tuân thủ giới hạn an toàn:**
   - Script chạy hoàn toàn ở chế độ **Read-Only**, không ghi đè, không xóa, không sửa registry COM.
   - **Chưa chạy lại lệnh Restore hoặc Deploy.**
   - Không can thiệp mã nguồn Check Hole CG2, Mirror, Bend hay Auto Frame Joint.

---

## 4. KẾT LUẬN & ĐỀ XUẤT CHO CHATGPT (DECISION TREE)

Theo phân tích tại Mục 23 của Runbook 27:
> *"Installed pair exactly archived CG2 Debug pair: The existing CG2 Debug archive is confirmed. Still do not run old Restore blindly... Decide explicitly whether to leave CG2 installed or return to F8 per user's previously chosen Plan A."*

Hiện trạng đang sẵn sàng 100% cho bước tiếp theo (Phase R1):
- Nếu Người dùng và ChatGPT quyết định **Giữ nguyên bản CG2 Debug (`0A41...`)**: Không cần làm gì thêm, tệp hiện tại đã là bản CG2 hoàn chỉnh và đồng bộ cả DLL lẫn PDB.
- Nếu Người dùng và ChatGPT quyết định **Khôi phục về Debug F8 (`F8D0...`) theo Phương án A**: Vì bản CG2 Debug hiện tại đã được snapshot an toàn tại `backup_cg2_debug_20261019_2137`, một kịch bản khôi phục có bảo vệ (guarded recovery script) có thể nạp lại cặp file Debug F8 từ bản lưu trữ đã được xác thực mà không làm mất bản CG2.

Antigravity dừng tại đây và chờ chỉ đạo chính thức từ ChatGPT và Người dùng.
