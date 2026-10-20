# CG2 — ANTIGRAVITY: BÁO CÁO CODE ĐÃ VIẾT / ĐÃ SỬA

> **STATUS: CG2 BREP COM 9-DOUBLE RAY FIX VERIFIED — STRICT SOURCE PROTOCOL ENFORCED.**
> Theo quy định mới: **ChatGPT là tác giả 100% code C#.**
> Antigravity **không tự viết hoặc sửa bất kỳ dòng C# nào** (`ANTI_CSHARP_MODIFICATION=NO`, `ANTI_C_SHARP_CHANGES=0`).
> Đã build Debug|x64 và Release|x64 thành công (0 error, 16 cảnh báo cũ không liên quan).
> Test harness offline `CG2_TEST_HARNESS.cs` đạt **100% PASS (0 failures)** với cả 6-double và 9-double COM layout, malformed payload fallback và invalid RayIndex.
> Đã chạy kiểm thử thực tế `Fixture_BREP_Positive.SLDASM` trên SOLIDWORKS CAD: xác nhận `BREP_SOLID_ON_AXIS` với độ dày chính xác `3.000 mm` và `2.000 mm`, `hits = 2`.
> Đã kiểm thử fixture đối chứng `Fixture_OK.SLDASM`: 13/13 ứng viên phân loại `BREP_NO_AXIS_HIT` (span = 0.000mm), phán quyết sản xuất `NG = 0` hoàn toàn không thay đổi.
> Đã chạy hồi quy sản xuất trên sandbox: phán quyết sản xuất và counters đạt **100% PASS**.
> PR #33 giữ nguyên trạng thái **Draft** (`READY_FOR_MERGE=NO`), không merge vào main và không bắt đầu CG3.

---

## 1. Phạm vi và lịch sử Git

| Field | Value |
|---|---|
| Repository | `tbcphutai-bit/ADDIN-SolidWorks` |
| Branch | `feat/check-hole-cg2-part-relations` |
| PR | https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33 |
| TESTED_REMOTE_SHA | `44d6fc8c454e99f1fa02c638e53aa8f244583161` |
| Current Git working tree status | Clean; committed files changed: 2 docs (`CG2_REPORT.md`, `CG2_ANTI_CODE_STATUS_REPORT.md`) |
| Git diff changed files | `docs/antigravity/cg2/CG2_REPORT.md`, `docs/antigravity/cg2/CG2_ANTI_CODE_STATUS_REPORT.md` |
| Việc được giao | CG2 BREP COM 9-double fix verification, build, offline tests, positive/negative CAD testing, regression, reporting |
| Quy định tác giả code | ChatGPT tác giả 100% C#; Anti chỉ áp dụng và chạy kiểm thử |
| Tổng file C# sửa trong vòng này | **0** (`ANTI_C_SHARP_CHANGES=0`) |
| Tổng file docs sửa trong vòng này | **2** (`CG2_REPORT.md`, `CG2_ANTI_CODE_STATUS_REPORT.md`) |

---

## 2. Danh sách code CG2 ĐÃ CÓ (Kiểm tra đối chiếu thực tế)

| File / method | Functionality đã thấy trong code | Tác giả đoạn sửa cuối | Đã sửa trong vòng này? | Build / Test evidence |
|---|---|---|---|---|
| `Commands/CheckAssemblyHole.MaterialEvidence.cs` | Hỗ trợ stride 6 và 9 double trong `Cg2ClassifyRayPointData`, kiểm tra tích hợp toàn vẹn dữ liệu COM, loại trừ layout lạ | ChatGPT (`5c1aafe`) | **NO** (Anti không sửa C#) | BUILD_PASS; RUNTIME_TESTED trên SOLIDWORKS; đo được `BREP_SOLID_ON_AXIS` với span `3.000mm` |
| `Commands/CheckAssemblyHole.PartRelations.cs` | Added material fields `MaterialEvidence`, `MaterialSpanMm`, `MaterialHitCount` và 5 BREP counters vào report | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS (MSBuild x64 0 errors); reflection audit PASS |
| `Commands/CheckAssemblyHole.cs :: Run` | Thêm 1 lệnh gọi `EnrichCg2BrepMaterialEvidence` sau AABB screening, bọc non-fatal `try/catch` | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS; RUNTIME_TESTED trên các Assembly sandbox; không ảnh hưởng CG1/NG/markers |
| `Commands/CheckHoleResultsForm.cs :: ShowPartRelations` | Hiển thị counts BREP có vật liệu/không giao trục/chưa xác minh, disclaimer header cập nhật | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS; Header disclaimer test PASS |
| `ADDIN.csproj` | Khai báo compile entry `CheckAssemblyHole.MaterialEvidence.cs` | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS (MSBuild 18.10.1 build sạch 0 error) |
| `docs/antigravity/cg2/CG2_TEST_HARNESS.cs` | Bổ sung test stride 9-double COM 3mm solid, test malformed layout 17-double, test invalid second RayIndex | ChatGPT (`4999c63`) | **NO** (Anti không sửa C#) | BUILD_PASS; **100% PASS (0 FAILURES)** |
| `docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs` | Isolated CAD runner, `VerifyCsvParity`, `ValidateCg2ExpectedCandidates` | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS; `--self-test` PASS; regression executed |
| `docs/antigravity/cg2/CG2_REVIEW3_REPEATABILITY.cs` | Repeatability runner (CG1↔CG1, CG2↔CG2, CG1↔CG2) trên `1618-01-BH.SLDASM` | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS; RUNTIME_EXECUTED; STRICT_BYTE_PARITY=FAIL; NORMALIZED_COUNTERS=PASS; EXIT=1 |

---

## 3. CODE DO ANTIGRAVITY TỰ VIẾT / SỬA TRONG VÒNG NÀY

| File | Class.Method / function | Line range | BEFORE behavior | AFTER behavior | Author | Commit SHA |
|---|---|---|---|---|---|---|
| **NONE** | **NONE** (Không có file C# nào bị sửa trong vòng này) | - | - | - | - | - |

### Tuân thủ quy định mới:
- `ANTI_CSHARP_MODIFICATION=NO`
- `ANTI_C_SHARP_CHANGES=0`
- `APPLIED_CHATGPT_CODE=YES` (Tất cả code C# do ChatGPT commit trực tiếp tại `5c1aafe`, `4999c63`)
- `NO_DUPLICATE_CLASSES_OR_METHODS=YES` (Không copy trùng code)
- `NO_PRODUCTION_CHANGES=YES` (Production Check Hole engine verdicts, NG criteria, CSV fields không đổi)

---

## 4. KẾT QUẢ KIỂM THỬ THỰC TẾ TRÊN SOLIDWORKS CAD SANDBOX

### A. Cụm dương tính (`Fixture_BREP_Positive.SLDASM`)
- Part B được dịch +10mm theo trục X, tấm kim loại 3mm chắn trực tiếp 12 tia trục lỗ của Part A.
- **Kết quả đo đạc thực tế:**
  - `Tested = 37`, `SolidEvidence = 24`, `NoAxisHit = 13`, `Unknown = 0`, `NotTestedBudget = 0`
  - Candidate #26..#37 (`fr1-2` -> `1-1`): **`BREP_SOLID_ON_AXIS`**, `MaterialHitCount = 2`, `MaterialSpanMm = 3.000 mm` (đúng độ dày tấm 3.0mm của component 1-1).
  - Candidate #14..#25 (`1-1` -> `fr1-2` chiều ngược lại): **`BREP_SOLID_ON_AXIS`**, `MaterialHitCount = 2`, `MaterialSpanMm = 2.000 mm` (đúng độ dày tấm 2.0mm của component fr1-2).
  - Candidate #1..#13 (lỗ đồng trục): **`BREP_NO_AXIS_HIT`**, `MaterialHitCount = 0`, `MaterialSpanMm = 0.000 mm`.
  - Phán quyết sản xuất không bị biến đổi sai lệch (`NG = 12` do dịch chuyển vị trí thực tế, không có lỗi giả, không có NG gán nhầm từ BREP).
  - **Trạng thái:** **`LIVE_3MM_SOLID_POSITIVE=PASS`**

### B. Cụm đối chứng có lỗ đối ứng (`Fixture_OK.SLDASM`)
- Part B có lỗ đồng trục đối ứng:
- **Kết quả đo đạc thực tế:**
  - `Tested = 13`, `SolidEvidence = 0`, `NoAxisHit = 13`, `Unknown = 0`, `NotTestedBudget = 0`
  - 13/13 ứng viên phân loại `BREP_NO_AXIS_HIT` (`Span = 0.000 mm`, `Hits = 0`).
  - Phán quyết sản xuất giữ nguyên tuyệt đối: `NG = 0`, `PatternNG = 0`, `Missing = 0`.
  - **Trạng thái:** **`NEGATIVE_HOLE_FIXTURE=PASS`**

---

## 5. MẪU PHẢN HỒI BẮT BUỘC (THEO SECTION C GIAO THỨC)

```text
CHATGPT_CSHARP_PATCH_APPLIED=YES
ANTI_CSHARP_MODIFICATION=NO
BUILD_DEBUG_X64=PASS
BUILD_RELEASE_X64=PASS
OFFLINE_6_AND_9_STRIDE_TEST=PASS
LIVE_3MM_SOLID_POSITIVE=PASS
LIVE_POSITIVE_MATERIAL_SPAN_MM=3.000
LIVE_POSITIVE_COM_HITS=2
NEGATIVE_HOLE_FIXTURE=PASS
CG1_VS_CG2_VERDICTS=PASS
RAW_CSV_BYTE_PARITY=FAIL (baseline traversal non-determinism observed)
SOURCE_SHA=44d6fc8c454e99f1fa02c638e53aa8f244583161
COMPILED_DLL_SHA256=6CE7B28B84890FF87C6BAC4C3454BF3965E4BC3B2EFD38D1918BE735B4854D78
READY_FOR_CHATGPT_SOURCE_REVIEW=YES
READY_FOR_MERGE=NO
```
