# CHECK CHẤN — Giao diện tiếng Việt dễ hiểu (bắt buộc ở đợt Codex tiếp theo)

Status: **UX requirements only, NOT a completed UI patch**. Based on actual PR #35 WinForms source, captured while preparing PR #36. The user explicitly reports that many displayed terms are not understandable.

## Goal

Make the entire Check Chấn workflow understandable to a shop-floor technician who knows chày chấn, đầu kẹp, nếp chấn, Part and Assembly, without requiring the person to know developer terms such as \`FeatureId\`, \`Witness\`, \`CLEAR\`, \`UNKNOWN\`, \`SourceRev\`, \`STALE\` or \`ProcessGraph\`.

**Vietnamese, concise, contextual and accurate is the default.** Detailed numeric evidence, original English enum/reason codes and stable source identifiers remain accessible through "Xem chi tiết kỹ thuật", copy/export or a secondary tooltip. Never change backend enum/status values, numeric solver, Check Min math, database/cache identities or source error codes as a side effect of localization. No fake production PASS.

## Source files to inspect

- \`UI/BendAssemblyBatchForm.Designer.cs\`: button captions, metric cards, column headers, scope controls and fake sample labels.
- \`UI/BendAssemblyBatchForm.cs\`: live status, tooltips, progress, phase and selected-tool detail, scope/layout, diagnostics and mocked UI.
- \`UI/BendSequencePlanningControl.cs\`: 18-tool list, sequence status, search coverage, phases and missing-evidence wording.
- \`UI/BendAssemblyBatchModels.cs\`: \`StatusDisplay\`, \`PunchStatusDisplay\`, \`HolderStatusDisplay\`, \`GeometricStatusDisplay\`; preserve underlying \`CollisionStatus\`.
- \`UI/BendAssemblyBatchController.cs\`: refresh/cancellation/stale text.
- \`UI/BendMinimumCheckForm.cs\`: legacy Check Min; consider safe label adjustments, but **do not alter calculation or old command**.
- \`Commands/BendPlanning/SequencePlanningModels.cs\`: \`SequenceSearchCoverage\`, \`SequenceToolDecision\`, \`SequenceEvidenceLevel\`.
- \`Commands/BendProcess/BendProcessModels.cs\`: real five \`BendProcessPhase\` values.

## Required wording map — use exact semantics, not loose machine translation

| Existing text/code seen in current source | Primary UI caption | Short explanation / tooltip |
| --- | --- | --- |
| Run Batch Check | **Bắt đầu kiểm tra** | Kiểm tra chi tiết theo phạm vi đã chọn |
| Entire Assembly | **Toàn bộ cụm lắp ráp** | Kiểm tra các chi tiết đủ điều kiện trong Assembly |
| Selected Components | **Các chi tiết đã chọn** | Chỉ kiểm tra các component đã chọn |
| Part hiện tại | **Chi tiết đang mở** | Chế độ Part, không phải Assembly |
| Component / Components | **Chi tiết trong cụm** | Các lần xuất hiện trong Assembly (có thể trùng Part) |
| Unique Parts | **Mẫu chi tiết khác nhau** | Đếm Part + cấu hình không trùng nhau |
| Configuration | **Cấu hình** | Cấu hình SOLIDWORKS của chi tiết |
| Occurrences | **Các vị trí lắp / chi tiết lặp lại** | Nhiều lần sử dụng cùng một Part trong cụm |
| Check Min | **Kiểm tra cạnh chấn tối thiểu** | Đối chiếu cạnh chấn với giới hạn hiện có |
| Punch | **Chày chấn** | Đúng hình học Punch của mã dao đang chọn |
| Upper Holder / Holder | **Đầu kẹp chày** | Bộ phận gá giữ chày; **KHÔNG** dịch là "cối chấn" hay "cối kẹp trên" (lower V-die is separate and not evaluated) |
| 18 tools / ToolId | **18 bộ chày–đầu kẹp** / **Mã bộ dao** | Giữ nguyên mã dao thực tế để tra thư viện |
| Process | **Quá trình chấn** | Kiểm tra chuyển động trong những giai đoạn đã được cung cấp |
| Static | **Va chạm tại vị trí đang xét** | Hình học tại một trạng thái đã biết; không suy ra cả quá trình |
| CLEAR | **Chưa phát hiện va chạm** | **Chỉ trong phạm vi dữ liệu và sai số đã kiểm tra.** Không phải sản xuất ĐẠT |
| COLLISION | **Phát hiện va chạm** | Va chạm đã có bằng chứng ở ứng viên/phase cụ thể; không đồng nghĩa cả Part không chấn được |
| UNKNOWN | **Chưa đủ dữ liệu** | Chưa thể kết luận an toàn va chạm; xem thiếu dữ liệu nào |
| CHUA_XAC_MINH / CHƯA XÁC MINH | **Chưa xác minh điều kiện sản xuất** | Trạng thái xác minh sản xuất độc lập với hình học |
| BelowMinimum | **Cạnh chấn không đạt tối thiểu** | Chỉ rõ A/B/C thực tế và giá trị giới hạn |
| MeetsMinimum | **Cạnh chấn đạt tối thiểu** | Chỉ là Check Min, không chứng minh không va chạm |
| InsufficientData | **Thiếu dữ liệu để kiểm tra cạnh chấn** | Khác với BelowMinimum |
| Clearance | **Khoảng hở nhỏ nhất được chứng minh (mm)** | Không hiển thị số 0 hoặc giá trị đoán nếu thiếu chứng cứ; có thể hiển thị "Chưa tính được" |
| Witness | **Bằng chứng vị trí va chạm** | Vị trí, cặp vật thể, thời điểm/phase; nếu là hình học không va chạm thì không gọi witness |
| u=0..1 | **Tiến độ chuyển động 0–100% (mô hình)** | Chỉ tham số chuẩn hóa của segment; không phải thời gian/cữ hành trình đo từ máy |
| margin / boundary error | **Độ dự phòng hình học / sai số kiểm tra** | Giá trị số giữ nguyên đơn vị mm và chính xác |
| FeatureId | **Mã nếp chấn trong SOLIDWORKS** | Giữ số/mã nguồn trong tooltip hoặc kỹ thuật; không thay bằng chỉ số B01 nếu ánh xạ không chắc chắn |
| PersistentId | **Mã liên kết bền vững với nếp chấn** | Thông tin kỹ thuật, không phải kích thước |
| Candidate / ứng viên | **Phương án dao đang kiểm tra** | Chưa phải dao đã được chọn sử dụng |
| Dao & thứ tự | **Chọn dao và thứ tự chấn** | Các lựa chọn trong phạm vi dữ liệu có chứng cứ |
| Simulation / SIMULATION | **Dữ liệu mô phỏng** | Không dùng để xác nhận gá/lắp thực tế |
| Reviewed / ReviewedSourceGraph | **Đã kiểm tra nguồn quy trình** | KHÔNG mặc định là đã xác minh gá dao hay có thể sản xuất |
| PREVIEW / mockup | **Dữ liệu minh họa** | Không phải kết quả CAD thật |
| LIVE | **Kết quả từ CAD đang mở** | Chỉ nguồn CAD thực; trạng thái va chạm vẫn tùy bằng chứng |
| Search coverage / ExhaustiveDeclaredSpace | **Đã xét hết các phương án được cung cấp** | Không đồng nghĩa đã xét hết mọi cách chấn trong thực tế |
| Bounded | **Chưa duyệt hết phương án (giới hạn kiểm tra)** | Không kết luận không tồn tại phương án |
| Interrupted | **Kiểm tra bị gián đoạn** | Hủy, dữ liệu thay đổi hoặc vượt giới hạn |
| MissingEvidence / NeedsEvidence | **Thiếu dữ liệu để đánh giá** | Chỉ rõ chính xác phần thiếu |
| ProvenInCheckedScope | **Có phương án đạt trong phạm vi đã kiểm tra** | Cần ghi "Chưa xác minh sản xuất" |
| RejectedInDeclaredConditions | **Không phù hợp với điều kiện đã xét** | Không suy ra Part không thể gia công bằng dao/điều kiện khác |
| Source changed / SOURCE_INVALIDATED / STALE | **Dữ liệu đã thay đổi – hãy quét lại** | Vô hiệu hóa kết quả cũ; đừng tô CLEAR cũ |
| Refresh | **Quét lại** | Chạy lại kiểm tra nguồn mới |
| Cancel | **Dừng kiểm tra** | Kết quả chưa hoàn tất không được coi là ĐẠT |
| Numeric queries | **Lượt tính va chạm** | 0 lượt nghĩa là chưa thực hiện tính va chạm số |
| Cache warm / cold | **Dùng lại dữ liệu hợp lệ / Kiểm tra lần đầu** | Chỉ hiển thị ở chế độ kỹ thuật |
| Datasource SHA256 / revision | **Phiên bản nguồn dữ liệu** | Hash hiển thị trong chi tiết kỹ thuật; không lược bỏ bằng chứng |
| Assembly source status / lightweight | **Chi tiết chưa nạp đầy đủ** | Cần Resolve trước khi xác minh |
| RPC / timeout / error code | **Lỗi kết nối SOLIDWORKS / Hết thời gian kiểm tra** | Chi tiết kỹ thuật vẫn có mã lỗi chính xác |

### Five real process phases — display labels only

| BendProcessPhase underlying enum | Vietnamese user display |
| --- | --- |
| ApproachLoad | **Đưa phôi vào vị trí** |
| PreBend | **Trước khi chấn** |
| DuringBend | **Trong khi chấn** |
| FormingEnd | **Kết thúc tạo góc chấn** |
| RetractUnload | **Rút chày và lấy phôi ra** |

A phase result UNKNOWN means the phase was not proved, not that the machine is clear.

## Critical status rules

1. Do not conflate \`CLEAR\` (numeric checked condition), \`MeetsMinimum\` (flange minimum), \`VerifiedSimulation\` (reviewed software simulation) and \`CHUA_XAC_MINH\` (manufacturing). Each has its own visibly separate status.
2. For any Part showing one tool's COLLISION, never display **"Chi tiết không chấn được"** at Part level. Instead say **"Có phương án dao phát hiện va chạm; xem từng dao"**.
3. The existing sample/mockup \`CLEAR\` and \`COLLISION\` rows must show the unmistakable **DỮ LIỆU MINH HỌA** marker. Do not mix mockup with live results.
4. When real company data has \`TOOL_AXIAL_EXTENT_UNVERIFIED\`, show **"Chưa có chiều dài làm việc và vị trí dọc trục được xác minh"** with an action hint **"Kiểm tra hồ sơ gá / chiều dài dao"**. Do not show guessed limit 4200/6000 mm.
5. \`NO_STATE_INFERENCE_FROM_FINAL_MODEL\`: **"Chưa có hình dạng phôi ở các bước chấn trung gian"**; \`UNKNOWN_MACHINE_MOTION\`: **"Chưa có dữ liệu chuyển động máy"**; \`UNKNOWN_PROCESS_GEOMETRY\`: **"Chưa có hình học quy trình đủ để tính va chạm"**; \`PROCESS_PROVIDER_NOT_CONFIGURED\`: **"Chưa cấu hình nguồn dữ liệu thứ tự chấn"**; \`FIXED_CAD_PAIR_REQUIRES_SOURCE_BOUND_SIMULATION_GEOMETRY\`: **"Đã nhận đúng cặp dao CAD nhưng thiếu vị trí mô phỏng gắn với nguồn"**. These are public-facing help messages, not replacements for internal reason IDs.
6. \`HOLDER_MATERIAL_ROLES_UNREVIEWED\`: **"Chưa xác định đường bao vật liệu của đầu kẹp"**; \`MISSING_CAD_SLOT_ASSOCIATION\`: **"Chưa xác nhận cặp chày và đầu kẹp trong CAD"**; \`OPEN_UNSUPPORTED_OR_INVALID_CAD_PUNCH\`: **"Biên dạng chày chưa kín hoặc chưa dùng được để tính va chạm"**.
7. Never translate unknown internal diagnostics to "Đạt". Unknown codes display **"Chưa đủ dữ liệu – xem chi tiết kỹ thuật"** plus the original code for diagnosis.
8. Every number must display its unit, e.g. mm, °, s, and distinguish thickness, cross-section W/L and *chiều dài đường chấn*.

## Desired screen example (real-case semantics; these are example text, not measured numeric results)

**CHI TIẾT XL-17 – 6.000 mm, W=50 mm, L=100 mm, AL 3,0 mm**

- Cạnh chấn tối thiểu: **Đạt** (this comes from existing Check Min).
- Bộ dao kiểm tra: **18/18 đã nhận diện từ CAD công ty**.
- Va chạm chày: **Chưa đủ dữ liệu – thiếu chiều dài làm việc**.
- Va chạm đầu kẹp: **Chưa đủ dữ liệu – thiếu phân loại vật liệu/vị trí gá**.
- Thứ tự chấn: **Chưa đủ dữ liệu trạng thái trung gian**.
- Điều kiện sản xuất: **CHƯA XÁC MINH**.

This UI distinguishes **software successfully checking a file** from **manufacturing suitability**. No green "ĐẠT" summary from 18 unknown tool checks.

## User interaction improvements

- Default tabs: **Tổng quan**, **Nếp chấn & 18 bộ dao**, **Chọn dao & thứ tự chấn**, **Lý do chưa kiểm tra được**, **Cạnh chấn tối thiểu** (or preserve current physical tab structure if improving navigation is unnecessary).
- One main CTA: **Bắt đầu kiểm tra**. Keep **Dừng** and **Quét lại**. Use **"Xem dao" / "Xem nếp" / "Xem bằng chứng"** beside the relevant result, not several ambiguous icons.
- If all 18 are UNKNOWN, put a simple, single-line explanation visible at Part level, and details listing what to provide. Avoid a wall of 18 identical raw error strings.
- Give two modes within the SAME form: **Dễ hiểu** (default) and **Chi tiết kỹ thuật** (expandable, not separate command); use original raw codes, IDs, SHA, timestamps in technical view.
- Tooltips should explain a term in <=2 simple sentences and not repeat the entire cell. Provide clear source status (mô phỏng / CAD thật / đã được kiểm chứng nguồn).
- If zero numeric queries, show **"Chưa thực hiện phép tính va chạm vì thiếu dữ liệu"**, never simply **"Không va chạm"**.
- Keep font readable at 1024×680; DPI 100/125/150 real OS tests if possible. Check clipping at 1024px, long Vietnamese terms, scrolling in tables and tooltips.
- Avoid "cối chấn" as a label for Upper Holder. The lower die (V-die) is a distinct part and currently not inspected; surface an explicit limitation.
- No green/red color alone: always use accompanying text, icons and evidence details.

## Implementation architecture to ask Codex for

1. Create a **presentation-only** formatter/dictionary (e.g. \`UI/BendCheckDisplayText.cs\`), keyed by existing enums and known reason codes. Store display text separately from native ids and serialized code strings.
2. Replace hardcoded English strings on view/UI bind paths; don't rewrite \`CollisionStatus\`, \`SequenceToolDecision\`, \`BendProcessPhase\` or \`Reason\` itself. Do not translate UI text inside backend result hashes.
3. Diagnostic lookup should use an explicit dictionary with safe fallback, not global \`string.Replace\` across arbitrary logs. Show original code in technical mode. Use localized labels for status and phase rows without losing click/tag/FeatureId binding.
4. Preserve actual null/missing numeric displays; never format a null clearance as \`0 mm\`.
5. Add tests for all label mappings, false-positive PASS prevention, UNKNOWN when no queries, mockup banner, check Min NG wording, source stale, phase names, 18 tool filters, selected/entire controls, full raw-code recovery.
6. Test Windows WinForms (not only data-model snapshot): 1024×680, 1264×821, 1920×1080; DPI 100/125/150 where actual test environment supports. Explicit NOT_RUN when unavailable. Use actual screens/screenshots, not just assertions on source text.
7. Do not mix this presentation-only change with the underlying geometry/numeric solver; keep it easy to review by diff. Antigravity may polish spacing/typography, but Codex owns functionality and must preserve event handlers.

## Definition of done

- Every user-facing Check Chấn button, tab, header, status, progress message, empty/error state, tooltip and diagnostic uses comprehensible Vietnamese. Retain technical terms *only* where necessary with Vietnamese explanations.
- A non-programmer can identify **what was checked**, **what is missing**, **whether a specific tool/phase collided**, **whether Check Min passed**, **what is still not proved**, and **what to do next**, without needing a developer.
- Internal codes, actual native identities, evidence and numeric results are unchanged; regression tests pass and overall release is not approved while native shutdown fails.
- Codex includes before/after screenshots and reports: fixed strings count, mapping code paths, UI DPI runs, unexplained terms still left, raw-code compatibility, and user-readable examples for XL-17 and 3 NG cases.
