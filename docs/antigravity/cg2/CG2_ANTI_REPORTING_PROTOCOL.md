# CG2 — QUY TRÌNH BẮT BUỘC: ANTIGRAVITY BÁO CÁO CODE / CHATGPT REVIEW CODE

**Repository:** `tbcphutai-bit/ADDIN-SolidWorks`  
**Branch:** `feat/check-hole-cg2-part-relations`  
**PR:** https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33  
**Hiệu lực:** áp dụng cho tất cả lần Anti báo "xong", "đã sửa", "PASS" từ Review #4 trở đi.  
**Trạng thái merge:** PR Draft, không merge cho đến khi chủ repository phê duyệt rõ ràng.

## A. Phân công cố định

**ChatGPT — kiến trúc / mã tham chiếu / kiểm soát kỹ thuật**
1. Đọc trực tiếp GitHub SOURCE C# ở commit Anti vừa push; không dùng tin nhắn/báo cáo của Anti làm chứng cứ đủ để nghiệm thu.
2. Đọc diff GitHub so với SHA trước đó và base CG1; kiểm tra hành vi từng method, điều kiện nhánh, ngoại lệ, trạng thái CAD, side effects, thứ tự COM, dung sai, phạm vi sản xuất, regression.
3. Đánh giá từng finding **PASS_STATIC / FAIL_CODE / NOT_VERIFIED_RUNTIME / BLOCKED**; đưa đường dẫn file, method, dòng, nguyên nhân, và **code C# sửa cụ thể** hoặc đúng văn bản thay thế nếu chỉ là docs. Nếu yêu cầu sửa code, ưu tiên tự commit patch/guide lên GitHub để Anti triển khai và kiểm thử.
4. Đăng review thực chất lên PR, chốt còn blocker nào; không phê duyệt merge chỉ vì Anti ghi CODE_COMPLETE=PASS.

**Antigravity — triển khai, build, chạy CAD và cung cấp bằng chứng**
1. Đọc chỉ thị và code đã được ChatGPT commit trên GitHub. Không chèn duplicate class/method, không âm thầm đổi engine production.
2. Mỗi lần làm xong **bắt buộc** cập nhật `docs/antigravity/cg2/CG2_ANTI_CODE_STATUS_REPORT.md` trước khi báo trong chat; báo **đã viết những file, class và method nào**, nêu ai là tác giả (ChatGPT/Anti), chính xác các lệnh test, và cả phần CHƯA LÀM.
3. Commit + push tất cả mã nguồn/QA code/tài liệu được dùng; không viện dẫn file local chưa push làm bằng chứng. Nếu chỉ sửa docs, ghi rõ **C# CHANGED = NONE**; không được nói "đã viết C#" ở commit chỉ đổi .md.
4. Mọi PASS phải có assertion thực sự, exit code, log và SHA binary. Báo FAIL/NOT_TESTED/BLOCKED trung thực; **không tự chuyển FAIL thành PASS**.
5. Không chạy trên CAD production hoặc phiên SOLIDWORKS người dùng. Sandbox chỉ chứa bản sao Pack-and-Go được xác nhận. Không đưa CAD gốc/CSV nhạy cảm lên repo.

## B. Anti phải báo cáo CODE ĐÃ VIẾT, không chỉ nhiệm vụ hoàn thành

Báo cả **toàn bộ CG2 hiện có** và **những thay đổi của commit hiện tại**; phân biệt rõ:
- **Code do ChatGPT tạo:** không nhận nhầm là Anti viết.
- **Code do Anti bổ sung/sửa:** chỉ rõ file và method, tác động trước/sau.
- **Code đã đọc nhưng không sửa:** ghi VERIFIED_BY_READING, không tự báo AUTHOR=ANTI.
- **Chưa xây dựng:** ghi NOT_IMPLEMENTED.
- **Đã viết nhưng chưa build/không chạy:** CODE_WRITTEN_NOT_TESTED.
- **Đã build nhưng chưa chạy CAD:** BUILD_PASS_RUNTIME_NOT_TESTED.
- **Chạy CAD thật trong sandbox:** có lệnh, mã thoát, dữ liệu kiểm chứng độc lập.

Các file cần kiểm tra đầu tiên:
| File | Điểm kiểm tra chính |
|---|---|
| `Commands/CheckAssemblyHole.PartRelations.cs` | `BuildCg2PartRelations`, broad-phase AABB, candidate `CHECK_ONLY`, partial caps |
| `Commands/CheckAssemblyHole.cs` | chính xác một CG2 diagnostic call, không ảnh hưởng `EvaluateStack`/NG/markers |
| `Commands/CheckHoleResultsForm.cs` | `ShowPartRelations`, read-only grids, không sửa Assembly |
| `ADDIN.csproj` | compile entry của source CG2 |
| `docs/antigravity/cg2/CG2_TEST_HARNESS.cs` | primitive tests, T01–T12; không fake assertions |
| `docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs` | `RunOneIsolated`, `VerifyCsvParity`, safeguards |
| `docs/antigravity/cg2/CG2_REVIEW3_REPEATABILITY.cs` | CG1→CG1 / CG2→CG2 / CG1→CG2, raw CSV & traversal evidence |
| `docs/antigravity/cg2/CG2_REVIEW4_DOC_CHECK.ps1` | text QA, **không** có nghĩa đã test CAD |

## C. Yêu cầu Git/GitHub trước và sau mỗi vòng Anti

Trước khi sửa, ghi vào báo cáo:
```powershell
git status --short
git branch --show-current
git rev-parse HEAD
git fetch origin
git diff --stat origin/feat/check-hole-connection-groups-cg1...HEAD
```

Sau khi sửa (thay `<BEFORE_SHA>` bằng SHA đã ghi trước vòng này):
```powershell
git diff --check
git diff --stat <BEFORE_SHA>..HEAD
git diff --name-status <BEFORE_SHA>..HEAD
git log -5 --format="%H %s"
git status --short
```

Nếu thay đổi chưa commit thì `git diff --stat <BEFORE_SHA>..HEAD` CHƯA chứa code đó: hãy commit/push và chạy lại. Không upload temporary executables/PDB/CAD secrets. Nếu branch đã tiến lên do ChatGPT push giữa chừng, ghi rõ base trước/giữa/sau; không overwrite.

Báo đủ:
- `BEFORE_SHA`, `AFTER_SHA`, `PR_URL`, branch đúng, changed file list;
- Với từng file **thêm/sửa/xóa**, ghi `class.method`, khoảng dòng, code behavior, `author`, lý do;
- Ghi `NO_PRODUCTION_CHANGES=YES/NO` và dẫn chứng diff;
- Nếu tài liệu chỉ yêu cầu correction thì báo **CODE_CHANGE_COUNT=0**, `DOC_CHANGE_COUNT` thực tế.

## D. Bằng chứng kiểm thử và quyết định

**Mỗi test** phải có: ID, input/fixture (định danh an toàn), phương pháp assert, trạng thái `PASS/FAIL/NOT_TESTED/BLOCKED`, lệnh đã chạy, exit code, link log, source SHA, binary SHA256 khi có.

Tách **năm loại bằng chứng**:
1. `SOURCE_STATIC`: code tồn tại, logic review tĩnh (ChatGPT xác nhận sau).
2. `COMPILATION`: thực sự build Debug|x64/Release|x64 với errors/warnings.
3. `UNIT_PRIMITIVE`: gọi primitive production, **không** tương đương end-to-end T01–T12.
4. `CAD_INTEGRATION`: thực sự gọi Check Hole trong isolated Assembly và assert site candidates/counters.
5. `REGRESSION_PARITY`: CSV raw bytes, raw ordered rows, canonicalized signatures, marker read-only và marker writes **tách riêng**.

**Review #4 bắt buộc báo đúng sự thật:**
```text
CG1_REPEATABLE_BYTES=NO
CG2_REPEATABLE_BYTES=NO
CG1_VS_CG2_BYTE_PARITY=FAIL
REPEATABILITY_EXIT_CODE=1
PRODUCTION_CANONICAL_COUNTERS=PASS
PRODUCTION_TRAVERSAL_CAUSE=NOT_PROVEN
EXACT_ALL_FIELD_GEOMETRY_PARITY=NOT_VERIFIED
CANONICALIZED_ROW_PARITY=PASS_WITH_NORMALIZATION
MARKER_GENERATION_PARITY=NOT_TESTED
CG2_END_TO_END_ALL_SCENARIOS=NOT_VERIFIED
READY_FOR_MERGE=NO
```

**Đừng viết `CG2_COMPLETE=PASS` nếu thiếu full integration evidence.** AABB proximity không chứng minh vật liệu/fastener hoặc missing-hole NG. Không auto chọn Master.

## E. Báo cáo bắt buộc + bình luận PR

- Tạo/ cập nhật duy nhất: **`docs/antigravity/cg2/CG2_ANTI_CODE_STATUS_REPORT.md`** theo template.
- Cập nhật `CG2_REPORT.md` và `CG2_REVIEW3_EVIDENCE.md` cho Review #4 theo [CG2_REVIEW4_GUIDE.md](CG2_REVIEW4_GUIDE.md).
- Chạy `CG2_REVIEW4_DOC_CHECK.ps1` và `git diff --check` trên Windows, ghi exit code.
- Phản hồi PR #33 bằng đường dẫn báo cáo + bảng file đã sửa + full SHA + test summary; **không chỉ thông báo qua Antigravity chat**.

Anti kết thúc bằng đúng mẫu:
```text
ANTI_DONE=YES
BRANCH=feat/check-hole-cg2-part-relations
BEFORE_SHA=<sha>
AFTER_SHA=<sha>
C_SHARP_FILES_CHANGED=<number>
C_SHARP_METHODS_CHANGED=<list or NONE>
DOC_FILES_CHANGED=<number>
WRITTEN_BY_CHATGPT=<paths>
WRITTEN_OR_EDITED_BY_ANTI=<paths>
NOT_IMPLEMENTED=<items>
BUILD_DEBUG_X64=<PASS|FAIL|NOT_TESTED>
BUILD_RELEASE_X64=<PASS|FAIL|NOT_TESTED>
REVIEW4_DOC_CHECK=<PASS|FAIL>
REVIEW4_DOC_CHECK_EXIT=<integer>
STRICT_CSV_BYTE_PARITY_916=FAIL_PREVIOUS_RUN
REPEATABILITY_EXIT_CODE=1
FULL_CG2_END_TO_END=NOT_VERIFIED
READY_FOR_CHATGPT_SOURCE_REVIEW=YES
READY_FOR_MERGE=NO
PR=https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33
REPORT_PATH=docs/antigravity/cg2/CG2_ANTI_CODE_STATUS_REPORT.md
```

## F. ChatGPT review tiếp theo (không giao Anti tự nghiệm thu)

Khi Anti báo xong, ChatGPT sẽ:
1. Lấy PR HEAD + commit diff từ GitHub.
2. Đọc từng file C# thực sự bị sửa, **đọc đúng method đã báo**, đối chiếu engine/marker/NG.
3. Xác nhận có/không bằng chứng code thực thi branch, kiểm tra assertions và các mã lỗi/thoát.
4. Đăng review gồm `ACCEPTED_STATIC`, `NEEDS_FIX`, `NOT_TESTED` theo từng file/method.
5. Nếu `NEEDS_FIX`, cung cấp mẫu code hoặc **tự tạo patch + .md trên GitHub** để Anti chỉ cần pull, build, test và báo cáo lại.
6. Chỉ chấp thuận Merge khi người dùng explicitly cho phép; không tự mở CG3.

**Không cần lặp review cho phần không thay đổi.** Chỉ ghi nhận regressions đã chứng minh và blocker đủ quan trọng.
