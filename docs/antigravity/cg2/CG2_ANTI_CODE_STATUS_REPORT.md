# CG2 — ANTIGRAVITY: BÁO CÁO CODE ĐÃ VIẾT / ĐÃ SỬA

> **STATUS: CG2 ONE-SHOT BREP VERIFICATION EXECUTED — STRICT SOURCE PROTOCOL ENFORCED.**
> Theo quy định mới: **ChatGPT là tác giả 100% code C#.**
> Antigravity **không tự viết hoặc sửa bất kỳ dòng C# nào** (`C_SHARP_EDITED_WITHOUT_CHATGPT_PATCH=NO`).
> Đã build Debug|x64 và Release|x64 thành công (0 error).
> Đã phát hiện lỗi assertion string trong `CG2_TEST_HARNESS.cs` dòng 847, giữ nguyên code và báo cáo lỗi lên GitHub để ChatGPT cung cấp patch.
> Đã chạy toàn bộ regression 6 fixture và đo đạc CAD-M01..M06 trên SOLIDWORKS thực tế.
> PR #33 giữ nguyên trạng thái **Draft**, không merge vào main và không bắt đầu CG3.

---

## 1. Phạm vi và lịch sử Git

| Field | Value |
|---|---|
| Repository | `tbcphutai-bit/ADDIN-SolidWorks` |
| Branch | `feat/check-hole-cg2-part-relations` |
| PR | https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33 |
| BEFORE_SHA (bắt đầu vòng này) | `98fe510caa35446b49aa8968573489c416b4425e` |
| AFTER_SHA (commit đã push) | SEE_PR_HEAD (recorded after push in PR comment) |
| Current Git working tree status | Clean after push; committed files changed: 2 docs (`CG2_REPORT.md`, `CG2_ANTI_CODE_STATUS_REPORT.md`) |
| Git diff changed files | `docs/antigravity/cg2/CG2_REPORT.md`, `docs/antigravity/cg2/CG2_ANTI_CODE_STATUS_REPORT.md` |
| Việc được giao | CG2 One-Shot BREP implementation check, build, CAD live testing, reporting |
| Quy định tác giả code | ChatGPT tác giả 100% C#; Anti chỉ áp dụng và chạy kiểm thử |
| Tổng file C# sửa trong vòng này | **0** (`C_SHARP_FILES_CHANGED=0`) |
| Tổng file docs sửa trong vòng này | **2** (`CG2_REPORT.md`, `CG2_ANTI_CODE_STATUS_REPORT.md`) |

---

## 2. Danh sách code CG2 ĐÃ CÓ (Kiểm tra đối chiếu thực tế)

| File / method | Functionality đã thấy trong code | Tác giả đoạn sửa cuối | Đã sửa trong vòng này? | Build / Test evidence |
|---|---|---|---|---|
| `Commands/CheckAssemblyHole.MaterialEvidence.cs` | `EnrichCg2BrepMaterialEvidence`, `Cg2LoadTargetBodyContext`, `Cg2PrepareBoundedRay`, `Cg2ReadRayEvidence`, 500-ray budget cap | ChatGPT (One-Shot BREP) | **NO** (Anti không sửa C#) | BUILD_PASS; RUNTIME_TESTED trên SOLIDWORKS; đo được 187 `BREP_NO_AXIS_HIT`, 1,136 `UNVERIFIED` |
| `Commands/CheckAssemblyHole.PartRelations.cs` | Added material fields `MaterialEvidence`, `MaterialSpanMm`, `MaterialHitCount` và 5 BREP counters vào report | ChatGPT (One-Shot BREP) | **NO** (Anti không sửa C#) | BUILD_PASS (MSBuild x64 0 errors); reflection audit PASS |
| `Commands/CheckAssemblyHole.cs :: Run` | Thêm 1 lệnh gọi `EnrichCg2BrepMaterialEvidence` sau AABB screening, bọc non-fatal `try/catch` | ChatGPT (One-Shot BREP) | **NO** (Anti không sửa C#) | BUILD_PASS; RUNTIME_TESTED trên 6 Assembly sandbox; không ảnh hưởng CG1/NG/markers |
| `Commands/CheckHoleResultsForm.cs :: ShowPartRelations` | Hiển thị counts BREP có vật liệu/không giao trục/chưa xác minh, ưu tiên ray có solid trước, giữ nguyên candidates | ChatGPT (One-Shot BREP) | **NO** (Anti không sửa C#) | BUILD_PASS; Header format cập nhật chuỗi BREP |
| `ADDIN.csproj` | Khai báo compile entry `CheckAssemblyHole.MaterialEvidence.cs` | ChatGPT (One-Shot BREP) | **NO** (Anti không sửa C#) | BUILD_PASS (MSBuild 18.10.1 build sạch 0 error) |
| `docs/antigravity/cg2/CG2_TEST_HARNESS.cs` | Offline assertions cho `Cg2TryGetSourceIndex`, `Cg2PrepareBoundedRay`, BREP fields | ChatGPT (`f744e58`) | **NO** (Anti không sửa C#) | BUILD_PASS; RUNTIME_FAIL_ASSERTION dòng 847 do lệch chuỗi disclaimer header; báo lỗi chờ patch |
| `docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs` | Isolated CAD runner, `VerifyCsvParity`, `ValidateCg2ExpectedCandidates` | ChatGPT (Review #2 & #3) | **NO** (Anti không sửa C#) | BUILD_PASS; `--self-test` PASS; 6 fixture regression executed (2 PASS, 4 CSV_BYTE_PARITY_FAIL do traversal non-determinism) |
| `docs/antigravity/cg2/CG2_REVIEW3_REPEATABILITY.cs` | Repeatability runner (CG1↔CG1, CG2↔CG2, CG1↔CG2) trên `1618-01-BH.SLDASM` | ChatGPT (Review #3) | **NO** (Anti không sửa C#) | BUILD_PASS; RUNTIME_EXECUTED; STRICT_BYTE_PARITY=FAIL; NORMALIZED_COUNTERS=PASS; EXIT=1 |

---

## 3. CODE DO ANTIGRAVITY TỰ VIẾT / SỬA TRONG VÒNG NÀY

| File | Class.Method / function | Line range | BEFORE behavior | AFTER behavior | Author | Commit SHA |
|---|---|---|---|---|---|---|
| **NONE** | **NONE** (Không có file C# nào bị sửa trong vòng này) | - | - | - | - | - |

### Tuân thủ quy định mới:
- `C_SHARP_EDITED_WITHOUT_CHATGPT_PATCH=NO`
- `C_SHARP_FILES_CHANGED=0`
- `APPLIED_CHATGPT_CODE=YES` (Tất cả code C# do ChatGPT commit trực tiếp tại `a9e3e53`, `f744e58`, `cf8ebcb`)
- `NO_DUPLICATE_CLASSES_OR_METHODS=YES` (Không copy trùng code)
- `NO_PRODUCTION_CHANGES=YES` (Production Check Hole engine verdicts, NG criteria, CSV fields không đổi)

---

## 4. LỖI PHÁT HIỆN CHỜ CHATGPT CUNG CẤP PATCH

### Lỗi #1: Assertion string mismatch trong test harness
- **File:** `docs/antigravity/cg2/CG2_TEST_HARNESS.cs`
- **Method:** `ADDIN.Tests.Cg2TestHarness.RunWinFormsUITests`
- **Line:** 847
- **Code hiện tại của ChatGPT:**
  ```csharp
  if (modalHeader != null && modalHeader.Contains("CHỈ LÀ AABB/BROAD-PHASE: không chứng minh tiếp xúc vật liệu, không chứng minh thiếu lỗ, không xác định Master, không tạo NG."))
      Console.WriteLine("PASS: Mandatory broad-phase disclaimer present in header");
  else { Console.WriteLine("FAIL: Mandatory disclaimer missing in modal header: " + modalHeader); errors++; }
  ```
- **Triệu chứng lỗi khi chạy harness:**
  ```text
  FAIL: Mandatory disclaimer missing in modal header: Component có bounding box: 0 | Part chưa nhận diện lỗ: 1 | Cặp AABB gần: 5 | Ứng viên AABB: 2 | BREP có vật liệu: 0 | BREP không giao trục: 0 | Chưa xác minh: 0
  Độ phủ: BBOX_SCREENED_NOT_MATERIAL_VERIFIED | Đã thử cặp Part: 0 | Đã thử trục lỗ: 0
  BREP_SOLID_ON_AXIS: chỉ chứng minh có solid trên trục; KHÔNG khẳng định cần lỗ xuyên, không xác định Master, không tạo NG.
  ```
- **Nguyên nhân:** Trong `Commands/CheckHoleResultsForm.cs` dòng 658, ChatGPT đã cập nhật modal header thành chuỗi `"BREP_SOLID_ON_AXIS: chỉ chứng minh có solid trên trục; KHÔNG khẳng định cần lỗ xuyên, không xác định Master, không tạo NG."`, nhưng tại dòng 847 của `CG2_TEST_HARNESS.cs`, assertion vẫn kiểm tra chuỗi cũ `"CHỈ LÀ AABB/BROAD-PHASE:..."`.
- **Hành động của Anti:** Tuân thủ quy tắc mới, **Anti không tự sửa dòng 847**. Giữ nguyên mã nguồn của ChatGPT, báo cáo nguyên văn để ChatGPT viết commit patch.

---

## 5. KẾT QUẢ KIỂM THỬ CAD-M01 ĐẾN CAD-M06 TRÊN SANDBOX

Đo đạc trên các Assembly trong `D:\CG1-ISOLATED-TEST-COPIES`:

| Test ID | Assembly | Kết quả đo thực tế | Trạng thái |
|---|---|---|---|
| **CAD-M01** | `Fixture_OK.SLDASM` | 13 tia ứng viên quét vào `1618-04-bh-A10-1-1`. Tất cả 13 tia trả 0 hits (`BREP_NO_AXIS_HIT`) do mặt bích đối ứng có lỗ đồng trục/khoảng trống (không có thành solid chắn ngang trục). Tổng `BREP_SOLID_ON_AXIS` = 0. | `TESTED_NO_SOLID_HIT_OBSERVED` |
| **CAD-M02** | `Fixture_OK.SLDASM` | Part B có lỗ đồng trục: 13/13 ứng viên được phân loại `BREP_NO_AXIS_HIT` (span = 0.000mm, hits = 0). Không phát sinh NG mới (`NG=0`, `PatternNG=0`, `Missing=0`). | `PASS` |
| **CAD-M03** | `Fixture_MultiLayer_ABC.SLDASM` | Nhiều occurrence ở cao độ Z và transform khác nhau. `Cg2BodyCoordinatesCalibrated` xác nhận thành công tất cả transforms (`Unknown = 0`, `Failures = 0`). 13/13 ứng viên phân loại `BREP_NO_AXIS_HIT`. | `PASS` |
| **CAD-M04** | `1618-01-BH.SLDASM` | Xử lý sub-assembly/multi-body: 984 ứng viên nhắm vào target là sub-assembly (`.SLDASM`) được phân loại an toàn thành `UNVERIFIED_NOT_RESOLVED_PART`; 84 phân loại `UNVERIFIED_RAY_TOPOLOGY`; 68 phân loại `UNVERIFIED_AMBIGUOUS_HITS`. Không crash, không gán nhầm NG. | `VERIFIED_WITH_LIMITATIONS` |
| **CAD-M05** | `1618-01-BH.SLDASM` | Xử lý unresolved/budget: `IsPartial = true`, `CoverageStatus = PARTIAL_UNVERIFIED`. Tất cả 1,297 ứng viên giữ nguyên `CHECK_ONLY`, không ứng viên nào bị xóa, không bị quy kết NG. | `PASS` |
| **CAD-M06** | `1618-01-BH.SLDASM` | Assembly 916 lỗ, 348 cụm, 1,297 ứng viên: Ray tested: 313. `BREP_SOLID_ON_AXIS`: 0. `BREP_NO_AXIS_HIT`: 161. `UNVERIFIED`: 1,136. `NOT_TESTED_BUDGET`: 0. Thời gian: 197.607s. Bộ nhớ: delta 44 MB. | `PASS` |

### Tổng hợp số đo BREP trên cả 3 fixture:
- `BREP_SOLID_ON_AXIS`: **0**
- `BREP_NO_AXIS_HIT`: **187** (13 + 13 + 161)
- `BREP_UNKNOWN`: **1,136** (984 unresolved part + 84 topology + 68 ambiguous hits)
- `BREP_NOT_TESTED_BUDGET`: **0** (313 rays tested < 500 cap; 984 unresolved sub-assemblies được đánh dấu unverified trước khi tiêu tốn ray)

---

## 6. KẾT QUẢ HỒI QUY SẢN XUẤT (REGRESSION) TRÊN 6 FIXTURE

Chạy qua `CG2_LIVE_CAD_RUNNER.exe`:
- **Production counters (Holes, Stacks, NG, Missing, Rows):** 100% khớp tuyệt đối giữa Baseline CG1 và Candidate CG2 trên cả 6 fixture (`CG1_VS_CG2_PRODUCTION_VERDICTS=PASS`).
- **Normalized Row Signatures (13 fields):** 100% khớp sau khi chuẩn hóa hướng Reference/Actual và sắp xếp dòng.
- **Strict CSV Byte Parity:**
  - `Fixture_Unmatched.SLDASM`: **PASS** (5,675 bytes, SHA256 trùng khớp)
  - `run07_Missing_Ura_BOM.SLDASM`: **PASS** (107 bytes, SHA256 trùng khớp)
  - `Fixture_OK.SLDASM`: **FAIL** (8,262 bytes vs 8,262 bytes, diff @ byte 178 do thứ tự duyệt Component)
  - `Fixture_NG_Offset.SLDASM`: **FAIL** (8,505 bytes vs 8,505 bytes, diff @ byte 185 do thứ tự duyệt Component)
  - `Fixture_MultiLayer_ABC.SLDASM`: **FAIL** (11,769 bytes vs 11,769 bytes, diff @ byte 190 do thứ tự duyệt Component)
  - `1618-01-BH.SLDASM`: **FAIL** (129,522 bytes vs 129,522 bytes, diff @ byte 175 do thứ tự duyệt Component)
  - Exit code tổng: `SUMMARY: PASS=2 FAIL=4 SKIP=0 EXIT=1`
- **3D Marker generation:** `AutoCreateMarkers=false` áp dụng tuyệt đối trong safe sandbox (`MARKER_GENERATION_PARITY=NOT_TESTED`).

---

## 7. MẪU PHẢN HỒI BẮT BUỘC (GỬI PR #33 VÀ USER)

```text
CG2_BREP_ONE_SHOT=FINISHED
PR=33
HEAD_SHA=SEE_PR_HEAD
C_SHARP_FILES_CHANGED=0
BUILD_DEBUG_X64=PASS
BUILD_RELEASE_X64=PASS
OFFLINE_BREP_UNIT_TEST=FAIL_ASSERTION_STRING_MISMATCH (CG2_TEST_HARNESS.cs L847)
CAD_M01=TESTED_NO_SOLID_HIT_OBSERVED (0 solid hits on 3 test fixtures)
CAD_M02=PASS (13 BREP_NO_AXIS_HIT, 0 NG)
CAD_M03=PASS (coordinate calibration verified, 0 unverified)
CAD_M04=VERIFIED_WITH_LIMITATIONS (sub-assemblies yield UNVERIFIED_NOT_RESOLVED_PART)
CAD_M05=PASS (PARTIAL_UNVERIFIED set, candidate preserved)
CAD_M06=PASS (313 rays tested, 161 no-hit, 1136 unverified, 197.6s elapsed, 44MB ws)
BREP_SOLID_ON_AXIS=0
BREP_NO_AXIS_HIT=187 (13 Fixture_OK + 13 MultiLayer + 161 1618-01-BH)
BREP_UNKNOWN=1136 (984 unresolved part + 84 topology + 68 ambiguous)
BREP_NOT_TESTED_BUDGET=0
CG1_VS_CG2_PRODUCTION_VERDICTS=PASS
STRICT_CSV_BYTE_PARITY=FAIL (2 PASS, 4 FAIL due to baseline traversal non-determinism)
MARKER_GENERATION_PARITY=NOT_TESTED
READY_FOR_CHATGPT_SOURCE_REVIEW=YES
READY_FOR_MERGE=NO
```
