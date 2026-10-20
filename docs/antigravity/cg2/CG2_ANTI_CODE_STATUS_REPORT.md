# CG2.1 — ANTIGRAVITY: BÁO CÁO CODE ĐÃ VIẾT / ĐÃ SỬA

> **STATUS: CG2.1 ONE-SHOT EXECUTION REPORTED — STRICT SOURCE PROTOCOL ENFORCED.**
> Theo quy định mới: **ChatGPT là tác giả 100% code C#.**
> Antigravity **không tự viết, sửa hay tối ưu bất kỳ dòng C# nào** (`C_SHARP_EDITED_BY_ANTI=NO`, `ANTI_C_SHARP_CHANGES=0`).
> Đã build Debug|x64 và Release|x64 thành công bằng MSBuild 18.10.1 (0 error, 16 cảnh báo cũ không liên quan).
> Test harness offline `CG2_TEST_HARNESS.cs` đạt **100% PASS (0 failures)** với các test mới (0.10mm/0.20deg suppression, 0.30mm not suppressed, 2mm/2deg diagnostic candidate, COM-9 và COM-6 layouts).
> Khi chạy kịch bản tự động `CG2_1_ONE_SHOT.ps1`, giai đoạn biên dịch runner (`COMPILE_RUNNER`) gặp lỗi compiler CS0136 trong file `docs/antigravity/cg2/CG2_1_SITE_ASSERT.cs` (trùng tên định danh `c` giữa lambda và biến cục bộ).
> Tuân thủ tuyệt đối quy định: Anti dừng lại, ghi nhận nguyên văn log lỗi để ChatGPT ra bản vá chính thức, không tự sửa mã nguồn C#.
> PR #33 giữ nguyên trạng thái **Draft** (`READY_FOR_MERGE=NO`), không merge vào main và không bắt đầu CG3.

---

## 1. Phạm vi và lịch sử Git

| Field | Value |
|---|---|
| Repository | `tbcphutai-bit/ADDIN-SolidWorks` |
| Branch | `feat/check-hole-cg2-part-relations` |
| PR | https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33 |
| TESTED_REMOTE_SHA | `fc7f5348de45de046726a139ef2685e57ab52aea` |
| Current Git working tree status | Clean; committed files changed: 2 docs (`CG2_REPORT.md`, `CG2_ANTI_CODE_STATUS_REPORT.md`) |
| Git diff changed files | `docs/antigravity/cg2/CG2_REPORT.md`, `docs/antigravity/cg2/CG2_ANTI_CODE_STATUS_REPORT.md` |
| Việc được giao | CG2.1 one-shot auto build, unit tests, runner compile, CAD sandbox execution & reporting |
| Quy định tác giả code | ChatGPT tác giả 100% C#; Anti chỉ áp dụng và chạy kiểm thử |
| Tổng file C# sửa trong vòng này | **0** (`ANTI_C_SHARP_CHANGES=0`) |
| Tổng file docs sửa trong vòng này | **2** (`CG2_REPORT.md`, `CG2_ANTI_CODE_STATUS_REPORT.md`) |

---

## 2. Danh sách code CG2.1 ĐÃ CÓ (Kiểm tra đối chiếu thực tế)

| File / method | Functionality đã thấy trong code | Tác giả đoạn sửa cuối | Đã sửa trong vòng này? | Build / Test evidence |
|---|---|---|---|---|
| `Commands/CheckAssemblyHole.PartRelations.cs` | `Cg2IsResolvedPartOccurrence`, đếm `SubassemblyContainerCount`, `UnresolvedLeafOccurrenceCount`, thu hẹp `Cg2LikelyKnownCounterpart` (0.10mm / 0.20deg), 60s cooperative budget | ChatGPT (`e6be5c5`) | **NO** (Anti không sửa C#) | BUILD_PASS (MSBuild x64 0 errors); reflection audit PASS |
| `Commands/CheckAssemblyHole.MaterialEvidence.cs` | Lookup Part cho leaf occurrences, 60s cooperative budget, giữ nguyên COM-9/COM-6 parser | ChatGPT (`e6be5c5`) | **NO** (Anti không sửa C#) | BUILD_PASS (MSBuild x64 0 errors); reflection audit PASS |
| `Commands/CheckHoleResultsForm.cs` | Counters Subassembly containers excluded và unresolved Part occurrences | ChatGPT (`e6be5c5`) | **NO** (Anti không sửa C#) | BUILD_PASS; Header disclaimer test PASS |
| `docs/antigravity/cg2/CG2_TEST_HARNESS.cs` | Unit assertions cho 0.10mm/0.20deg, 0.30mm không bị chặn, 2mm/2deg, reflection checks | ChatGPT (`e6be5c5`) | **NO** (Anti không sửa C#) | BUILD_PASS; **100% PASS (0 FAILURES)** |
| `docs/antigravity/cg2/CG2_1_SITE_ASSERT.cs` | Site manifest regression assertions (`CG2_1_EXPECTED_SITES.tsv`) | ChatGPT (`fc7f534`) | **NO** (Anti không sửa C#) | COMPILE_FAIL (CS0136 dòng 105); báo lỗi chờ patch |
| `docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs` | Runner tích hợp site assert self-test và enhanced logs | ChatGPT (`fc7f534`) | **NO** (Anti không sửa C#) | Chưa biên dịch được do phụ thuộc `CG2_1_SITE_ASSERT.cs` |
| `docs/antigravity/cg2/CG2_1_ONE_SHOT.ps1` | Kịch bản tự động hóa build, test harness, runner, CAD regression & repeatability | ChatGPT (`fc7f534`) | **NO** (Anti không sửa script) | Chạy tự động: bắt đúng lỗi compiler runner và xuất summary file |

---

## 3. CODE DO ANTIGRAVITY TỰ VIẾT / SỬA TRONG VÒNG NÀY

| File | Class.Method / function | Line range | BEFORE behavior | AFTER behavior | Author | Commit SHA |
|---|---|---|---|---|---|---|
| **NONE** | **NONE** (Không có file C# nào bị sửa trong vòng này) | - | - | - | - | - |

### Tuân thủ quy định mới:
- `C_SHARP_EDITED_BY_ANTI=NO`
- `ANTI_C_SHARP_CHANGES=0`
- `APPLIED_CHATGPT_CODE=YES` (Toàn bộ code C# do ChatGPT commit)
- `NO_DUPLICATE_CLASSES_OR_METHODS=YES` (Không copy trùng code)
- `NO_PRODUCTION_CHANGES=YES` (Production Check Hole engine verdicts, NG criteria, CSV fields không đổi)

---

## 4. CHI TIẾT LỖI COMPILER ĐƯỢC PHÁT HIỆN KHI CHẠY ONE-SHOT

- **Lệnh thực thi:**
  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\antigravity\cg2\CG2_1_ONE_SHOT.ps1" -RepoRoot "C:\Tao moi\ADDIN"
  ```
- **Giai đoạn gặp lỗi:** `COMPILE_RUNNER`
- **Thông báo lỗi nguyên văn:**
  ```text
  RUN COMPILE_RUNNER
  docs\antigravity\cg2\CG2_1_SITE_ASSERT.cs(105,67): error CS0136: ローカルまたはパラメーター 'c' は、その名前が外側のローカルのスコープでローカルやパラメーターの定義に使用されているため、このスコープでは宣言できません
  COMPILE_RUNNER EXIT=1 LOG=<temp>\COMPILE_RUNNER.log
  STOP_OR_LIMITATION=Runner compiler failed
  ```
- **Vị trí trong mã nguồn `docs/antigravity/cg2/CG2_1_SITE_ASSERT.cs`:**
  - Dòng 105: `var matches = metrics.Cg2ExpectedCandidates.Where(c => ...).ToList();` (khai báo tham số lambda tên `c`)
  - Dòng 122: `CandidateDetail c = matches[0];` (khai báo biến cục bộ tên `c` trong cùng khối scope `foreach`)
- **Nguyên nhân kỹ thuật:** Trình biên dịch C# không cho phép khai báo biến cục bộ trùng tên với tham số lambda trong cùng scope bao quanh.
- **Hành động của Anti:** Dừng lại theo đúng quy định, giữ nguyên mã nguồn C#, báo cáo đầy đủ file và dòng lỗi để ChatGPT phát hành commit bản vá.

---

## 5. MẪU PHẢN HỒI BẮT BUỘC (THEO SECTION D GIAO THỨC CG2.1)

```text
CG2_1_SOURCE_HEAD=fc7f5348de45de046726a139ef2685e57ab52aea
C_SHARP_EDITED_BY_ANTI=NO
BUILD_DEBUG_X64=PASS
BUILD_RELEASE_X64=PASS
OFFLINE_PATCH_TEST=PASS
NESTED_PART=NOT_TESTED (blocked by runner compiler error CS0136 in CG2_1_SITE_ASSERT.cs)
OFFSET_CANDIDATE=NOT_TESTED (blocked by runner compiler error CS0136 in CG2_1_SITE_ASSERT.cs)
BREP_POSITIVE=NOT_TESTED (blocked by runner compiler error CS0136 in CG2_1_SITE_ASSERT.cs)
BREP_NEGATIVE=NOT_TESTED (blocked by runner compiler error CS0136 in CG2_1_SITE_ASSERT.cs)
NESTED_ROTATED=NOT_TESTED (blocked by runner compiler error CS0136 in CG2_1_SITE_ASSERT.cs)
LARGE_ASSEMBLY=NOT_TESTED (blocked by runner compiler error CS0136 in CG2_1_SITE_ASSERT.cs)
CG1_VS_CG2_PRODUCTION_VERDICTS=NOT_TESTED (blocked by runner compiler error CS0136 in CG2_1_SITE_ASSERT.cs)
STRICT_RAW_CSV_BYTE_PARITY=NOT_TESTED (blocked by runner compiler error CS0136 in CG2_1_SITE_ASSERT.cs)
MARKER_GENERATION_PARITY=NOT_TESTED
COMPILLED_DLL_SHA256=59E9819D7729A09B97214747EF2ED59E2679EE49EA6FA14C0722D5868C2EF6A1
RUNNER_EXIT_CODE=1
READY_FOR_MERGE=NO
```
