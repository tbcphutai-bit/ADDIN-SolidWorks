# CG2 REVIEW #4 — HƯỚNG DẪN SỬA CHÍNH XÁC + MÃ KIỂM TRA BÁO CÁO

**Repo:** \`tbcphutai-bit/ADDIN-SolidWorks\`  
**Branch:** \`feat/check-hole-cg2-part-relations\`  
**PR:** https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33  
**Review:** CG2 Independent Review #4 (ChatGPT, commit \`8b80da40194c466b7054abb4b7e75f2b6ee38268\`)  
**Phạm vi:** ONLY DOCUMENTATION + QA reporting assertions. **Không thay đổi engine production, runner C#, CG1, CG2 hay dữ liệu CAD.**

## 0. Vì sao Review #4 không cần C# production mới?

ChatGPT đã đọc \`CG2_LIVE_CAD_RUNNER.cs\`, \`CG2_REVIEW3_REPEATABILITY.cs\`, \`CheckAssemblyHole.cs\`. Hai DLL chạy thực tế 4 lượt: CG1→CG1, CG2→CG2, CG1→CG2. Code có so bytes thật (\`SequenceEqual\`) và trả exit code 1 khi khác.

Lỗi hiện tại là **cách mô tả mức độ bằng chứng**, chứ chưa có bằng chứng code engine sai. Snapshot trong \`SnapshotComponentTraversal()\` gọi \`assembly.GetComponents(false)\` *riêng* trước \`CheckAssemblyHole.Run()\`. Còn production đọc danh sách khác trong \`ReadSkippedPatternHoles()\` và \`ReadAssemblyHoles()\`. Quan sát snapshot khác nhau giữa các lượt chưa phải bằng chứng xác định duy nhất nguyên nhân thứ tự row/CSV.

Tương tự, \`ReportRowSignature()\` canonicalize Reference/Actual, sort và làm tròn \`F6\`. Nó chỉ chứng minh **sau chuẩn hóa là khớp**, KHÔNG chứng minh raw rows, hướng Reference/Actual, thứ tự CSV hay full precision bằng nhau.

### Sự kiện KHÔNG được thay đổi

\`\`\`text
CG1_REPEATABLE_BYTES=NO
CG2_REPEATABLE_BYTES=NO
CG1_VS_CG2_BYTE_PARITY=FAIL
PRODUCTION_CANONICAL_COUNTERS=PASS
DIAGNOSTIC_CLASSIFICATION=BASELINE_NONDETERMINISM_OBSERVED
REPEATABILITY_EXIT_CODE=1
CANONICALIZED_ROW_SIGNATURE_EQUAL=True
RAW_ROW_ORDER_AND_EXACT_VALUES_EQUAL=False
SKETCH_MARKER_WRITE_PARITY=NOT_TESTED
\`\`\`

5 fixture nhỏ: CSV byte PASS theo log Anti. Assembly 916 lỗ: CSV byte FAIL; không biến FAIL thành PASS.

## 1. Sửa đúng \`docs/antigravity/cg2/CG2_REPORT.md\`

### 1A. Phần F: thay 3 hàng đánh giá gây hiểu nhầm

Hàng cũ \`Strict raw CSV byte parity (916-hole assembly)\` chứa kết luận trong ngoặc đơn \`(SOLIDWORKS COM GetComponents ordering)\`, dù chưa chứng minh nguyên nhân của chính lần đọc production.

**Thay bằng**:

\`\`\`markdown
| Strict raw CSV byte parity (916-hole assembly) | **FAIL (Exit 1)** | CG1→CG1, CG2→CG2 và CG1→CG2 đều khác byte; nguyên nhân duy nhất chưa xác minh |
| Canonicalized counters/row signatures | **PASS (normalized comparison only)** | 916 logical holes, 348 stacks, 0 NG, 568 rows; signature đã sort/swap Ref–Actual và làm tròn F6 |
| Exact ordered-row / Reference–Actual / full-precision parity | **NOT_VERIFIED** | Trace raw-ordered rows khác nhau; không được gọi 100% identical |
\`\`\`

Hàng cũ \`CG1 production results unaffected = VERIFIED\` nên ghi rõ **trên sáu fixture, counters/verdicts và canonicalized rows so được**; chưa bao phủ mọi model hay marker generation. Không tuyên bố \`EXACT_ALL_FIELD_GEOMETRY_PARITY=100%\`.

### 1B. Phần G.5: thay các lời kết luận nhân quả hoặc "100% physical parity"

**Thay toàn bộ nội dung bullet của G.5 bằng đoạn dưới (những con số là evidence Anti đã báo):**

\`\`\`markdown
### 5. Kết luận kỹ thuật — giới hạn bằng chứng

- **Quan sát được:** Trong 4 lần mở riêng của cùng bản sao Assembly 916 lỗ, CSV byte khác nhau cả khi chạy lại chính binary CG1 và chính binary CG2. Số logical holes = 916, stacks = 348, NG = 0 và report rows = 568 ở cả 4 lần.
- **Quan sát được:** Snapshot riêng của \`GetComponents(false)\` trước \`Run()\` trả về thứ tự Component khác nhau. Các raw ordered rows cũng khác. \`CANONICALIZED_ROW_SIGNATURE_EQUAL=True\` khi đã chuẩn hóa hướng Reference/Actual, sắp xếp dòng và làm tròn F6.
- **Chưa chứng minh:** Snapshot \`GetComponents(false)\` không phải array chính xác mà production \`ReadAssemblyHoles()\` sử dụng. Vì vậy chưa thể khẳng định API traversal là nguyên nhân duy nhất của raw CSV order difference, hay 223 reference/actual swaps đã được đếm bằng assertion.
- **Giới hạn:** Không có bằng chứng dữ liệu raw full-precision, role Reference/Actual và thứ tự từng dòng giống tuyệt đối; marker generation/update chưa kiểm thử. Kết quả này không chứng minh CG2 đã nhận diện thật vật liệu, mối ghép hoặc lỗ thiếu.
- **Điều đã được hỗ trợ:** CSV mismatch không chỉ xuất hiện do thêm CG2 vì CG1→CG1 cũng không ổn định trong lần thử. Không dùng quan sát này để kết luận có hoặc không có bug trong mọi model.
- **Kết quả kiểm thử:** \`REPEATABILITY_EXIT_CODE=1\` và \`RAW_CSV_BYTE_PARITY=FAIL\` được giữ nguyên; không đổi thành PASS.
\`\`\`

**Thêm block status ngay sau G.5:**

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

## 2. Sửa đúng \`docs/antigravity/cg2/CG2_REVIEW3_EVIDENCE.md\`

### 2A. Giữ nguyên toàn bộ số liệu đã đo

- SHA256 của cả bốn CSV \`cg1_1\`, \`cg1_2\`, \`cg2_1\`, \`cg2_2\`.
- FIRST_DIFFERENT_BYTE = 175 (theo evidence).
- Ba cặp \`CSV_BYTE_EQUAL=False\`.
- \`ORDERED_GETCOMPONENTS_EQUAL=False\`, \`RAW_ROW_ORDER_AND_EXACT_VALUES_EQUAL=False\`, \`CANONICALIZED_ROW_SIGNATURE_EQUAL=True\`.
- \`REPEATABILITY_EXIT_CODE=1\`.

**KHÔNG chỉnh log hoặc số liệu để giống một câu chuyện nhân quả.**

### 2B. Thay toàn bộ \`### Key Technical Findings\` ở cuối file

\`\`\`markdown
### Key Technical Findings — Evidence vs. Inference

1. **OBSERVED:** CG1→CG1 và CG2→CG2 đều tạo CSV có SHA256 khác nhau dù metrics 916 holes / 348 stacks / 0 NG / 568 rows giữ nguyên trong phiên thử này. CG1→CG2 cũng khác byte.
2. **OBSERVED:** \`SnapshotComponentTraversal()\` (gọi trước \`Run()\`) ghi nhận Component-order snapshots khác nhau; raw ordered row signatures khác nhau.
3. **NOT PROVEN:** Check Hole production gọi \`GetComponents(false)\` độc lập trong \`ReadAssemblyHoles()\`. Runner hiện không log chính array mà production đã duyệt. Vì vậy mối quan hệ nhân quả duy nhất giữa thứ tự GetComponents và thay đổi CSV chỉ là giả thuyết phù hợp dữ liệu, không phải causal proof.
4. **NORMALIZED MATCH:** \`ReportRowSignature()\` sort rows, canonicalize Reference/Actual và format số theo F6. Việc chữ ký khớp chứng minh normalized comparison khớp theo tiêu chí đã đặt, không chứng minh exact full-precision/ordered equality.
5. **ACCEPTANCE STATUS:** Strict byte parity FAIL (exit 1) như đo được; \`MARKER_GENERATION_PARITY=NOT_TESTED\`; full CG2 T01–T12 end-to-end chưa được xác minh đầy đủ; precise material/fastener intent not implemented.
\`\`\`

**Ngay dưới đó, thêm status block giống 9 dòng ở mục 1B**, chính xác cùng giá trị, để dễ kiểm tra bằng script.

## 3. MÃ KIỂM TRA TỰ ĐỘNG (đã commit)

File \`docs/antigravity/cg2/CG2_REVIEW4_DOC_CHECK.ps1\` đã được ChatGPT viết để kiểm tra:

- Cả 2 báo cáo đều có chính xác các status bắt buộc.
- Không sửa FAIL/exit code 1 thành PASS.
- Các cụm kết luận nhân quả/100% absolute đã được xóa khỏi phần báo cáo cuối.
- Giữ marker generation NOT_TESTED và \`READY_FOR_MERGE=NO\`.

Chạy tại root repo:

\`\`\`powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File docs\antigravity\cg2\CG2_REVIEW4_DOC_CHECK.ps1
if ($LASTEXITCODE -ne 0) { throw "CG2 Review4 documentation validation FAILED" }
\`\`\`

**Script PASS = tài liệu có các nhãn cần thiết; không phải proof SOLIDWORKS/CSV/CG2 production.** Bắt buộc đọc lại context thủ công và review diff.

## 4. Phân công và quy định Antigravity

1. Pull \`feat/check-hole-cg2-part-relations\`, đọc Review #4 và file này.
2. **Chỉ sửa 2 file .md** ở mục 1–2; script test ChatGPT đã tạo thì giữ nguyên.
3. Giữ raw numbers/hashes/evidence, không rewrite test để lấy PASS.
4. Chạy script PowerShell ở mục 3, ghi lại STDOUT + exit code trong comment PR #33.
5. Kiểm tra \`git diff --check\`; dùng \`git diff\` xác nhận không sửa \`Commands/\`, \`ADDIN.csproj\`, \`CG2_LIVE_CAD_RUNNER.cs\`, \`CG2_REVIEW3_REPEATABILITY.cs\` hoặc CG1/CG2 engine.
6. Commit và push 2 .md; trả full SHA + paths, nhận xét code review, cập nhật bảng nghiệm thu trung thực.
7. Giữ PR #33 Draft, \`READY_FOR_MERGE=NO\`, không merge \`main\`/CG1, không triển khai CG3. ChatGPT sẽ đọc lại các **nội dung báo cáo và code kiểm tra** thực tế trên GitHub.

## 5. Hạn chế rõ ràng

Review #4 **không yêu cầu C# production mới**. Nếu muốn chứng minh causal production traversal chính xác thì đó là công việc instrumentation diagnostic opt-in riêng, cần có phê duyệt. Không tự ý chỉnh \`ReadAssemblyHoles()\` hoặc ép thứ tự Component vì mục tiêu hiện tại là không thay đổi engine.
