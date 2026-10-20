# CG2 — ANTIGRAVITY: BÁO CÁO CODE ĐÃ VIẾT / ĐÃ SỬA

> **STATUS: REVIEW #5 CORRECTIONS APPLIED — CODE INVENTORY AUDITED & ALIGNED WITH C# SOURCE.**
> Đã cập nhật 5 điểm hiệu chỉnh theo ChatGPT Review #5 (AFTER_SHA remote reference, CoverageStatus=PARTIAL_UNVERIFIED, 3rd WinForms tab conditional, found.Count > 0 provenance vs 13 log rows, RUNTIME_EXECUTED / EXIT=1).
> **Chỉ sửa duy nhất file báo cáo này; không sửa C# production, QA runner hay các file tài liệu đã đạt của Review #4.**

---

## 1. Phạm vi và lịch sử Git

| Field | Value |
|---|---|
| Repository | `tbcphutai-bit/ADDIN-SolidWorks` |
| Branch | `feat/check-hole-cg2-part-relations` |
| PR | https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33 |
| BEFORE_SHA (bắt đầu vòng này) | `200ff05a472753500c433a1fcf0da929642959ab` |
| AFTER_SHA (commit đã push) | SEE_PR_HEAD (recorded after push in PR comment; previous push was `200ff05a472753500c433a1fcf0da929642959ab`) |
| Current Git working tree status | Clean after push; committed files changed: 1 doc (`CG2_ANTI_CODE_STATUS_REPORT.md` in Review #5; 3 docs in Review #4) |
| Git diff changed files | `CG2_ANTI_CODE_STATUS_REPORT.md` |
| Việc được giao | Review #5: fix 5 code-inventory discrepancies per ChatGPT Review #5 |
| Tổng file C# sửa trong vòng này | **0** |
| Tổng file docs sửa trong vòng này | **1** (`CG2_ANTI_CODE_STATUS_REPORT.md`) |

---

## 2. Danh sách code CG2 ĐÃ CÓ (Kiểm tra đối chiếu thực tế)

| File / method | Functionality đã thấy trong code | Tác giả đoạn sửa cuối | Đã sửa trong vòng này? | Build / Test evidence |
|---|---|---|---|---|
| `Commands/CheckAssemblyHole.PartRelations.cs :: BuildCg2PartRelations` | Quét occurrence Component, xây dựng AABB near graph, phát hiện ứng viên `CHECK_ONLY`, cơ chế cap partial an toàn | ChatGPT (initial CG2) | **NO** (0 dòng sửa) | BUILD_PASS theo log Anti; CG2 AABB diagnostic đã chạy trên các fixture sandbox; strict CSV byte parity trên fixture lớn vẫn FAIL |
| `Commands/CheckAssemblyHole.cs :: Run` | Một lệnh chẩn đoán CG2 sau khi hoàn tất CG1, bọc `try/catch` fallback gán `ERROR_UNVERIFIED` an toàn | ChatGPT (CG2 integration) | **NO** (0 dòng sửa) | BUILD_PASS theo log Anti; RUNTIME_EXECUTED trên 6 Assembly sandbox; counters/verdicts sau chuẩn hóa khớp, CSV byte trên Assembly lớn FAIL |
| `Commands/CheckHoleResultsForm.cs :: ShowPartRelations` | WinForms modal hiển thị quan hệ Part; 2 tabs always; 3rd warning tab conditional on Warnings.Count>0 | ChatGPT (CG2 integration) | **NO** (0 dòng sửa) | BUILD_PASS; Offline WinForms test with injected warning passed according to Anti logs; real user-click UI test not independently verified. |
| `ADDIN.csproj` | Khai báo compile entry `CheckAssemblyHole.PartRelations.cs` | ChatGPT (CG2 integration) | **NO** (0 dòng sửa) | BUILD_PASS (MSBuild 18.10.1 build sạch 0 error) |
| `CG2_TEST_HARNESS.cs :: RunScenarioTestsT01ToT12` | Kiểm thử primitive hình học và logic biên CG2-T01..T12 với nhãn `NOT_TESTED` rõ ràng | Mixed: Anti initial / ChatGPT Review #2 | **NO** (0 dòng sửa) | BUILD_PASS; UNIT_PRIMITIVE_PASS (`SELF_TEST_EXIT_CODE=0`) |
| `CG2_LIVE_CAD_RUNNER.cs :: VerifyCsvParity` | So sánh byte thô (`SequenceEqual`) giữa baseline CG1 và candidate CG2 | ChatGPT (Review #2) | **NO** (0 dòng sửa) | BUILD_PASS; RUNTIME_TESTED (5 fixture nhỏ byte PASS, 1 fixture lớn byte FAIL exit 1) |
| `CG2_LIVE_CAD_RUNNER.cs :: ValidateCg2ExpectedCandidates` | Kiểm tra provenance ứng viên (ID, occurrence, label, `Classification == CHECK_ONLY`) | ChatGPT (Review #2) | **NO** (0 dòng sửa) | BUILD_PASS; Observed 13 A->B candidate rows in Anti's fixture log; runner assertion requires found.Count > 0 (not exactly 13). AABB_CANDIDATE_OBSERVED, MISSING_HOLE_PROVEN=NO. |
| `CG2_LIVE_CAD_RUNNER.cs :: RunOneIsolated` | Thực thi sandbox an toàn, kiểm tra sandbox consent, old markers, dirty state, SHA256 file | Mixed: Anti initial / ChatGPT Review #2 & #3 | **NO** (0 dòng sửa) | BUILD_PASS theo log Anti; isolated runner đã chạy trên 6 fixture; không coi raw CSV parity toàn bộ là PASS |
| `CG2_REVIEW3_REPEATABILITY.cs :: RunRepeatability` | 4 lần mở fresh read-only, đối chiếu 3 cặp (CG1↔CG1, CG2↔CG2, CG1↔CG2), xuất raw rows và component traversal | ChatGPT (Review #3) | **NO** (0 dòng sửa) | BUILD_PASS; RUNTIME_EXECUTED; STRICT_BYTE_PARITY=FAIL; NORMALIZED_COUNTERS=PASS; EXIT=1 |
| `CG2_REVIEW4_DOC_CHECK.ps1` | Script kiểm tra tự động văn bản báo cáo và các status line bắt buộc | ChatGPT (Review #4) | **NO** (0 dòng sửa) | SCRIPT_PASS (`CG2_REVIEW4_DOC_CHECK=PASS`, exit code 0) |

---

## 3. CODE DO ANTIGRAVITY TỰ VIẾT / SỬA TRONG VÒNG NÀY

| File | Class.Method / function | Line range | BEFORE behavior | AFTER behavior | Author | Commit SHA |
|---|---|---|---|---|---|---|
| **NONE** | **NONE** (Không có file C# nào bị sửa trong Review #5) | - | - | - | - | - |

### Bằng chứng code-completeness
- `git diff --name-status BEFORE_SHA..HEAD`: Sẽ ghi nhận sau khi commit (chỉ có duy nhất `CG2_ANTI_CODE_STATUS_REPORT.md`)
- `git diff --check`: **PASS** (0 lỗi whitespace / conflict markers)
- `NO_PRODUCTION_CHANGES=YES` (Diff so với base CG1 hoàn toàn không có thay đổi nào ngoài CG2 diagnostic đã review)
- `NO_DUPLICATE_CLASSES_OR_METHODS=YES` (Không tạo thêm duplicate class/method)
- `CG2_CHECK_ONLY_NEVER_NG=VERIFIED_STATIC` (Code audit xác nhận `Classification = "CHECK_ONLY"`, không ghi đè verdict NG)
- `CG2_DATA_COVERAGE_PARTIAL_ERRORS_VISIBLE=VERIFIED_STATIC` (CG2 cap -> IsPartial=true, CoverageStatus=PARTIAL_UNVERIFIED; UI header shows CoverageStatus. Cap-specific warning text is NOT automatically appended.)

---

## 4. CÁC HẠNG MỤC ĐÃ LÀM, CHƯA LÀM VÀ LÝ DO

| Item | Implemented? | Actually verified? | Notes / evidence |
|---|---|---|---|
| Quét tự động occurrence Component trong Assembly | EXISTS_IN_SOURCE | VERIFIED_IN_CAD | `BuildCg2PartRelations` đã chạy trên 6 Assembly thực tế trong sandbox |
| Sàng lọc AABB gần (gap <= 2mm) và trục lỗ (reach <= 60mm) | EXISTS_IN_SOURCE | VERIFIED_IN_CAD | Observed 13 A->B candidate rows in Anti's fixture log; runner assertion requires found.Count > 0 (not exactly 13). AABB_CANDIDATE_OBSERVED, MISSING_HOLE_PROVEN=NO. |
| Giao diện WinForms xem quan hệ Part CG2 (Read-Only) | EXISTS_IN_SOURCE | VERIFIED_OFFLINE | 2 tabs always; 3rd warning tab conditional on Warnings.Count>0. Offline WinForms test with injected warning passed according to Anti logs; real user-click UI test not independently verified. |
| Nhận diện tiếp xúc vật liệu thực (BREP solid contact) | **NOT_IMPLEMENTED** | N/A | CG2 chỉ là sàng lọc không gian AABB broad-phase; disclaimer hiển thị bắt buộc |
| Suy diễn ý đồ thiết kế bắt buộc phải có lỗ đối ứng | **NOT_IMPLEMENTED** | N/A | Ứng viên AABB gần không phải bằng chứng thiếu lỗ; cần ý đồ kỹ thuật |
| Tự động suy diễn Part Chủ (Master) cho từng mối ghép | **NOT_IMPLEMENTED** | N/A | Nằm ngoài phạm vi CG2; dành cho CG3 nếu được duyệt |
| Kiểm thử tích hợp toàn diện 12 kịch bản CG2-T01..T12 | **NOT_VERIFIED** | NO | Các bài test unit primitive đã pass nhưng chưa bao phủ toàn bộ pipeline CAD |
| So sánh byte thô CSV trên Assembly 916 lỗ | **FAIL_PREVIOUS_RUN** | YES (Đo thực tế) | Exit code 1; byte khác nhau tại byte 175; snapshot thứ tự Component thay đổi nhưng nguyên nhân trực tiếp trong ReadAssemblyHoles chưa được chứng minh |
| Kiểm thử tạo/cập nhật 3D Sketch Marker | **NOT_TESTED** | NO | Chính sách an toàn sandbox: `AutoCreateMarkers=false` bắt buộc |
| Hiệu chỉnh văn bản báo cáo theo Review #4 & Review #5 | **COMPLETED** | YES | Đã sửa `CG2_REPORT.md`, `CG2_REVIEW3_EVIDENCE.md` và `CG2_ANTI_CODE_STATUS_REPORT.md`; `CG2_REVIEW4_DOC_CHECK=PASS` |

---

## 5. Build + test evidence từ máy Anti

| Case | SHA source / DLL | Command | Assertions / inputs | Exit code | Status | Evidence |
|---|---|---|---|---|---|---|
| Debug x64 full rebuild | `0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC` | `MSBuild.exe ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:minimal` | 0 compiler errors, 0 CG2 warnings | 0 | **PASS** | `docs/antigravity/cg2/CG2_REPORT.md` Section B.2 |
| Release x64 full rebuild | `8D470DEA5B0900BF4E5FD50CFF3B219BB1F1AA9323A52CD2B9853F5377C1B046` | `MSBuild.exe ADDIN.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /v:minimal` | 0 compiler errors, 0 CG2 warnings | 0 | **PASS** | `docs/antigravity/cg2/CG2_REPORT.md` Section B.1 |
| CG2 runner --self-test | `CG2_LIVE_CAD_RUNNER.exe` | `CG2_LIVE_CAD_RUNNER.exe --self-test` | Exit policy, malformed candidate, quoted multiline CSV | 0 | **PASS** | `docs/antigravity/cg2/CG2_REPORT.md` Section D |
| 6 fixture regression | `8D470DEA5B09...` (Candidate) vs `D9257D34...` (Baseline) | `CG2_LIVE_CAD_RUNNER.exe <CG1> <CG2> <root> <6 fixtures>` | Holes, Stacks, NG, Rows khớp 100% trên cả 6 fixture | 0 (nhỏ) / 1 (lớn) | **VERIFIED_EVIDENCE** | `docs/antigravity/cg2/CG2_REPORT.md` Section E |
| CG1→CG1 / CG2→CG2 / CG1→CG2 | Review #3 snapshot (`1618-01-BH.SLDASM`) | `CG2_LIVE_CAD_RUNNER.exe ... --repeatability` | Byte, raw row, component order qua 4 lần mở fresh read-only | 1 | **RUNTIME_EXECUTED; STRICT_BYTE_PARITY=FAIL; NORMALIZED_COUNTERS=PASS; EXIT=1** | `docs/antigravity/cg2/CG2_REVIEW3_EVIDENCE.md` |
| Review #4 doc check | `CG2_REVIEW4_DOC_CHECK.ps1` | `powershell.exe ... -File docs\antigravity\cg2\CG2_REVIEW4_DOC_CHECK.ps1` | 13 status lines bắt buộc, 6 prohibited claim checks, section G.5 | 0 | **PASS** | `docs/antigravity/cg2/CG2_REVIEW4_DOC_CHECK.ps1` |
| git diff --check | Working tree | `git diff --check` | Whitespace errors, conflict markers | 0 | **PASS** | 0 errors |

---

## 6. Review #4 & Review #5 — Trạng thái trung thực không được đổi

```text
RAW_CSV_BYTE_PARITY=FAIL
BASELINE_RAW_CSV_NONDETERMINISM=OBSERVED
PRE_RUN_GETCOMPONENTS_ORDER_VARIATION=OBSERVED
PRODUCTION_TRAVERSAL_CAUSE=NOT_PROVEN
EXACT_ALL_FIELD_GEOMETRY_PARITY=NOT_VERIFIED
CANONICALIZED_ROW_PARITY=PASS_WITH_NORMALIZATION
MARKER_GENERATION_PARITY=NOT_TESTED
CG2_END_TO_END_ALL_SCENARIOS=NOT_VERIFIED
READY_FOR_MERGE=NO
```

---

## 7. Anti's final response to ChatGPT via PR #33

```text
ANTI_DONE=YES
BRANCH=feat/check-hole-cg2-part-relations
BEFORE_SHA=200ff05a472753500c433a1fcf0da929642959ab
AFTER_SHA=SEE_PR_HEAD
C_SHARP_FILES_CHANGED=0
C_SHARP_METHODS_CHANGED=NONE
DOC_FILES_CHANGED=1
WRITTEN_BY_CHATGPT=Commands/CheckAssemblyHole.PartRelations.cs, Commands/CheckAssemblyHole.cs, Commands/CheckHoleResultsForm.cs, docs/antigravity/cg2/CG2_REVIEW3_REPEATABILITY.cs, docs/antigravity/cg2/CG2_REVIEW4_DOC_CHECK.ps1, docs/antigravity/cg2/CG2_REVIEW4_GUIDE.md, docs/antigravity/cg2/CG2_ANTI_REPORTING_PROTOCOL.md
WRITTEN_OR_EDITED_BY_ANTI=docs/antigravity/cg2/CG2_ANTI_CODE_STATUS_REPORT.md (Review #5); docs/antigravity/cg2/CG2_REPORT.md, docs/antigravity/cg2/CG2_REVIEW3_EVIDENCE.md (Review #4)
NOT_IMPLEMENTED=Real BREP solid contact, Required counterpart-hole design intent, Automatic joint Master inference
BUILD_DEBUG_X64=PASS
BUILD_RELEASE_X64=PASS
REVIEW4_DOC_CHECK=PASS
REVIEW4_DOC_CHECK_EXIT=0
STRICT_CSV_BYTE_PARITY_916=FAIL_PREVIOUS_RUN
REPEATABILITY_EXIT_CODE=1
FULL_CG2_END_TO_END=NOT_VERIFIED
READY_FOR_CHATGPT_SOURCE_REVIEW=YES
READY_FOR_MERGE=NO
PR=https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33
REPORT_PATH=docs/antigravity/cg2/CG2_ANTI_CODE_STATUS_REPORT.md
```
