# ANTIGRAVITY HOST SMOKE REPORT: KẾT QUẢ KIỂM TRA PREFLIGHT PHƯƠNG ÁN A

- **Vai trò Agent:** `SOLIDWORKS-UI-DESIGNER`
- **Repository:** `tbcphutai-bit/ADDIN-SolidWorks`
- **Working copy folder:** `C:\Tao moi\ADDIN-AutoFrameJoint-Launcher`
- **Base Branch:** `docs/afj-host-plan-a-debug-baseline-20261019`
- **Base Commit (HEAD):** `79d1c3ab3b9b7953c7e5cd2aef538959cc4b237d`
- **Review Branch:** `review/antigravity-afj-plan-a-preflight-20261019`
- **Thời gian:** `2026-10-19T21:15:00+07:00`
- **Đối tượng báo cáo:** ChatGPT (Code Owner & Reviewer) và Người dùng — Tham chiếu `docs/AutoFrameJoint/26_PLAN_A_DEBUG_BASELINE_HOST_SMOKE.md` & GitHub Issue #31

---

## 1. KẾT QUẢ KIỂM TRA TRẠNG THÁI TẬP TIN THỰC TẾ (PREFLIGHT EVIDENCE)

Đã chạy lệnh kiểm tra toàn diện không ghi đè (read-only preflight):

| Nhóm tập tin | Đường dẫn | Kích thước (Bytes) | SHA256 Hash |
| :--- | :--- | :--- | :--- |
| **Hiện đang cài đặt (Installed)** | `C:\Tao moi\ADDIN.dll`<br>`C:\Tao moi\ADDIN.pdb` | 6,565,376<br>9,123,328 | `D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E`<br>`A35F451847A41D1520B735EB0206C36567126153C966804BE73B7E9BE78DDCF7` |
| **Bản sao lưu Debug gốc (Plan A Baseline)** | `C:\Tao moi\backup_addin_pre_launcher_smoke\ADDIN.dll`<br>`C:\Tao moi\backup_addin_pre_launcher_smoke\ADDIN.pdb` | 7,170,048<br>10,345,984 | `F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126`<br>`754571AACA727A404C8802E4D569A86E12DDC6EA06E881D8866ED748682F81AE` |
| **Bản sao lưu Release CG1 độc lập** | `C:\Tao moi\backup_cg1_release_snapshot\ADDIN.dll`<br>`C:\Tao moi\backup_cg1_release_snapshot\ADDIN.pdb` | 6,565,376<br>9,123,328 | `D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E`<br>`A35F451847A41D1520B735EB0206C36567126153C9D23DA44D08C30B54ABAEF8` |
| **Bản DLL thử nghiệm Auto Frame Joint** | `C:\Tao moi\ADDIN-AutoFrameJoint-Launcher\bin\x64\Debug\ADDIN.dll`<br>`C:\Tao moi\ADDIN-AutoFrameJoint-Launcher\bin\x64\Debug\ADDIN.pdb` | 7,171,072<br>10,356,224 | `643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B`<br>`4E3D5AAB1537DA67F26A6EAC30AD77679441102EC96FF3DC8CFE1A8B28ED100C` |

- **Tiến trình SOLIDWORKS:** `SLDWORKS.exe` không chạy (đã thoát an toàn).
- **Trạng thái COM CodeBase:** Trỏ chính xác đến `C:\Tao moi\ADDIN.DLL`.

---

## 2. KẾT LUẬN & THỰC HIỆN TIÊU CHÍ DỪNG TRƯỚC KHI THAY FILE

Theo đúng yêu cầu tại Runbook 26 và chỉ dẫn của người dùng:
> *"Nếu vẫn đang cài Release CG1 hoặc có hash/PDB không khớp, phải dừng và báo tình trạng trước khi thay file."*

1. **Hiện trạng xác nhận:**
   - Tập tin `C:\Tao moi\ADDIN.dll` hiện tại **vẫn đang là bản Release CG1** (SHA256: `D9257D34...`).
   - Cả 2 bản build đã được lưu trữ an toàn trong các thư mục sao lưu độc lập:
     - Bản Debug F8: `C:\Tao moi\backup_addin_pre_launcher_smoke\`
     - Bản Release CG1: `C:\Tao moi\backup_cg1_release_snapshot\`
2. **Hành động của Antigravity:**
   - **Dừng lại ngay tại Gate 0 / Gate 1**, **CHƯA chép đè** bản Debug F8 vào `C:\Tao moi\ADDIN.dll`.
   - Báo cáo rõ ràng tình trạng tập tin và bằng chứng hash để ChatGPT và Người dùng xác nhận bước thiết lập baseline Debug `F8` trước khi thực thi `Deploy`.

---

## 3. BƯỚC TIẾP THEO KHI ĐƯỢC PHÊ DUYỆT

Ngay khi nhận được lệnh xác nhận thực hiện sao chép từ người dùng / ChatGPT:
1. Chép cặp file Debug từ `C:\Tao moi\backup_addin_pre_launcher_smoke\` vào `C:\Tao moi\`.
2. Chạy `Invoke-AutoFrameJointHostTest.ps1 -Mode Inspect` để xác nhận `Installed SHA256` đạt chuẩn `F8D0A4AC...`.
3. Chạy `Invoke-AutoFrameJointHostTest.ps1 -Mode Deploy -Approved` để nạp DLL thử nghiệm và sinh Manifest.
4. Mở SOLIDWORKS thực hiện Smoke Test trên bản sao Assembly.
5. Đóng SOLIDWORKS và chạy `Invoke-AutoFrameJointHostTest.ps1 -Mode Restore -Approved -ManifestPath ...` để khôi phục về Debug F8.
