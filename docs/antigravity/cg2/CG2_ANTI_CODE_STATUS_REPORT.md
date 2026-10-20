# CG2 — ANTIGRAVITY: BÁO CÁO CODE ĐÃ VIẾT / ĐÃ SỬA

> **STATUS: AWAITING ANTIGRAVITY UPDATE — TEMPLATE, NOT TEST EVIDENCE.**
> Anti phải điền báo cáo này bằng thực tế GitHub + build/test và commit lên PR #33.
> **Không được đổi giá trị NOT_VERIFIED thành PASS bằng cảm nhận.**
>
> BẮT BUỘC ĐỌC: [CG2_ANTI_REPORTING_PROTOCOL.md](CG2_ANTI_REPORTING_PROTOCOL.md)  
> HƯỚNG DẪN REVIEW #4: [CG2_REVIEW4_GUIDE.md](CG2_REVIEW4_GUIDE.md)

## 1. Phạm vi và lịch sử Git

| Field | Value (Anti fill) |
|---|---|
| Repository | \`tbcphutai-bit/ADDIN-SolidWorks\` |
| Branch | \`feat/check-hole-cg2-part-relations\` |
| PR | https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33 |
| BEFORE_SHA (bắt đầu vòng này) | NOT_RECORDED |
| AFTER_SHA (commit đã push) | NOT_RECORDED |
| Current Git working tree status | NOT_RECORDED |
| Git diff changed files | NOT_RECORDED |
| Việc được giao | Review #4: corrected evidence wording, no production C# changes |
| Tổng file C# sửa trong vòng này | NOT_VERIFIED |
| Tổng file docs sửa trong vòng này | NOT_VERIFIED |

## 2. Danh sách code CG2 ĐÃ CÓ (phải kiểm tra code thật)

Đây là **danh sách đã thấy trong source GitHub** do ChatGPT đọc tĩnh; Anti phải ghi riêng *tác giả*, *có sửa vòng này không*, *compile/runtime verified*.

| File / method | Functionality đã thấy trong code | Tác giả đoạn sửa cuối | Đã sửa trong vòng này? | Build / Test evidence |
|---|---|---|---|---|
| \`Commands/CheckAssemblyHole.PartRelations.cs :: BuildCg2PartRelations\` | scan Component occurrences, AABB near graph, CHECK_ONLY candidates, capped partial coverage | ChatGPT (initial CG2) | NOT_RECORDED | NOT_VERIFIED |
| \`Commands/CheckAssemblyHole.cs :: Run\` | một CG2 diagnostics call sau CG1, vẫn có fallback \`ERROR_UNVERIFIED\` | ChatGPT (CG2 integration) | NOT_RECORDED | NOT_VERIFIED |
| \`Commands/CheckHoleResultsForm.cs :: ShowPartRelations\` | WinForms read-only candidate/relationship view | ChatGPT (CG2 integration) | NOT_RECORDED | NOT_VERIFIED |
| \`ADDIN.csproj\` | compile entry \`CheckAssemblyHole.PartRelations.cs\` | ChatGPT (CG2 integration) | NOT_RECORDED | NOT_VERIFIED |
| \`CG2_TEST_HARNESS.cs :: RunScenarioTestsT01ToT12\` | primitive/sanity test logic; not full end-to-end coverage | Mixed: Anti initial / ChatGPT subsequent edits | NOT_RECORDED | NOT_VERIFIED |
| \`CG2_LIVE_CAD_RUNNER.cs :: VerifyCsvParity\` | strict production CSV byte comparison | ChatGPT (Review #2), other runner code mixed | NOT_RECORDED | NOT_VERIFIED |
| \`CG2_LIVE_CAD_RUNNER.cs :: ValidateCg2ExpectedCandidates\` | exact occurrence/column provenance checks | ChatGPT (Review #2) | NOT_RECORDED | NOT_VERIFIED |
| \`CG2_LIVE_CAD_RUNNER.cs :: RunOneIsolated\` | read-only sandbox run, flags/markers/hashes safeguards | Mixed, review individual diff | NOT_RECORDED | NOT_VERIFIED |
| \`CG2_REVIEW3_REPEATABILITY.cs :: RunRepeatability\` | 4 fresh opens, 3 pairwise repeatability checks | ChatGPT (Review #3) | NOT_RECORDED | NOT_VERIFIED |
| \`CG2_REVIEW4_DOC_CHECK.ps1\` | review docs assertions only | ChatGPT (Review #4) | NOT_RECORDED | NOT_EXECUTED_BY_CHATGPT |

## 3. CODE DO ANTIGRAVITY TỰ VIẾT / SỬA TRONG VÒNG NÀY

**BẮT BUỘC:** ít nhất 1 dòng với \`NONE\` khi không đổi C#; không bỏ trống.

| File | Class.Method / function | Line range | BEFORE behavior | AFTER behavior | Author | Commit SHA |
|---|---|---|---|---|---|---|
| NONE UNTIL VERIFIED | NONE | - | - | - | - | - |

### Bằng chứng code-completeness
- \`git diff --name-status BEFORE_SHA..AFTER_SHA\`: NOT_RECORDED
- \`git diff --check\`: NOT_TESTED
- \`NO_PRODUCTION_CHANGES=NOT_VERIFIED\`
- \`NO_DUPLICATE_CLASSES_OR_METHODS=NOT_VERIFIED\`
- \`CG2_CHECK_ONLY_NEVER_NG=NOT_VERIFIED_STATIC\`
- \`CG2_DATA_COVERAGE_PARTIAL_ERRORS_VISIBLE=NOT_VERIFIED_STATIC\`

## 4. CÁC HẠNG MỤC ĐÃ LÀM, CHƯA LÀM VÀ LÝ DO

| Item | Implemented? | Actually verified? | Notes / evidence |
|---|---|---|---|
| Auto-scan Assembly component occurrences | EXISTS_IN_SOURCE | NOT_VERIFIED_BY_ANTI | |
| Candidate AABB near + axis screen | EXISTS_IN_SOURCE | NOT_VERIFIED_BY_ANTI | |
| CG2 UI read-only results | EXISTS_IN_SOURCE | NOT_VERIFIED_BY_ANTI | |
| Real SOLIDWORKS material/solid contact | NOT_IMPLEMENTED | N/A | CG2 AABB only |
| Required counterpart-hole design intent | NOT_IMPLEMENTED | N/A | AABB candidate != missing-hole proof |
| Automatic per-joint Master inference | NOT_IMPLEMENTED | N/A | CG3 not started |
| End-to-end CG2 T01–T12 verified | NOT_VERIFIED | NO | Different primitive tests do not establish full pipeline |
| Strict CSV byte equality large Assembly | FAIL_PREVIOUS_RUN | NO | Exit 1; raw bytes differ |
| Marker generation/update parity | NOT_TESTED | NO | Guarded: AutoCreateMarkers=false |
| Review #4 documentation correction | NOT_STARTED | NO | Fill actual result |

## 5. Build + test evidence từ máy Anti

| Case | SHA source / DLL | Command | Assertions / inputs | Exit code | Status | Evidence |
|---|---|---|---|---|---|---|
| Debug x64 full rebuild | NOT_RECORDED | NOT_RECORDED | compiler errors/warnings | NOT_RECORDED | NOT_TESTED | |
| Release x64 full rebuild | NOT_RECORDED | NOT_RECORDED | compiler errors/warnings | NOT_RECORDED | NOT_TESTED | |
| CG2 runner --self-test | NOT_RECORDED | NOT_RECORDED | real self-test asserts | NOT_RECORDED | NOT_TESTED_IN_THIS_ROUND | |
| 6 fixture regression | NOT_RECORDED | NOT_RECORDED | rows/counters/CSV | NOT_RECORDED | NOT_TESTED_IN_THIS_ROUND | |
| CG1→CG1 / CG2→CG2 / CG1→CG2 | See previous Review #3 log | REPORT_LINK_REQUIRED | byte, raw row, component order | 1 (past evidence) | FAIL_RAW_BYTE_PARITY | |
| Review #4 doc check | NOT_RECORDED | \`powershell.exe -NoProfile -ExecutionPolicy Bypass -File docs/antigravity/cg2/CG2_REVIEW4_DOC_CHECK.ps1\` | required status lines + unsupported absolute claims | NOT_RECORDED | NOT_TESTED | |
| git diff --check | NOT_RECORDED | \`git diff --check\` | whitespace/conflicts | NOT_RECORDED | NOT_TESTED | |

**Không làm mới test CAD khi chỉ sửa docs, trừ khi có yêu cầu riêng.** Trích dẫn log cũ phải nói rõ SHA/binary của phiên chạy cũ.

## 6. Review #4 — trạng thái trung thực không được đổi

\`\`\`text
RAW_CSV_BYTE_PARITY=FAIL
BASELINE_RAW_CSV_NONDETERMINISM=OBSERVED
PRE_RUN_GETCOMPONENTS_ORDER_VARIATION=OBSERVED
PRODUCTION_TRAVERSAL_CAUSE=NOT_PROVEN
EXACT_ALL_FIELD_GEOMETRY_PARITY=NOT_VERIFIED
CANONICALIZED_ROW_PARITY=PASS_WITH_NORMALIZATION
MARKER_GENERATION_PARITY=NOT_TESTED
CG2_END_TO_END_ALL_SCENARIOS=NOT_VERIFIED
READY_FOR_MERGE=NO
\`\`\`

## 7. Anti's final response to ChatGPT via PR #33

\`\`\`text
ANTI_DONE=NOT_YET
BEFORE_SHA=NOT_RECORDED
AFTER_SHA=NOT_RECORDED
C_SHARP_FILES_CHANGED=NOT_VERIFIED
C_SHARP_METHODS_CHANGED=NOT_RECORDED
DOC_FILES_CHANGED=NOT_VERIFIED
WRITTEN_OR_EDITED_BY_ANTI=NOT_RECORDED
BUILD_DEBUG_X64=NOT_TESTED_IN_THIS_ROUND
BUILD_RELEASE_X64=NOT_TESTED_IN_THIS_ROUND
REVIEW4_DOC_CHECK=NOT_TESTED
REVIEW4_DOC_CHECK_EXIT=NOT_RECORDED
REVIEW4_DOC_CHECK_LOG=NOT_RECORDED
FULL_CG2_END_TO_END=NOT_VERIFIED
READY_FOR_CHATGPT_SOURCE_REVIEW=NO
READY_FOR_MERGE=NO
PR=https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33
REPORT_PATH=docs/antigravity/cg2/CG2_ANTI_CODE_STATUS_REPORT.md
\`\`\`

**Sau khi điền phải push lên GitHub; chỉ khi đó ChatGPT mới tiến hành nghiệm thu độc lập code và tài liệu thực tế.**
