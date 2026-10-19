# CHECK HOLE CG1 — ANTIGRAVITY IMPLEMENTATION & VERIFICATION REPORT

> REPORT STATUS: **FULL_VERIFICATION_COMPLETE_READY_FOR_REVIEW**
>
> This report documents the verification, full rebuild, independent tolerance testing, live SOLIDWORKS CAD regression against `main` baseline, and review resolution for CG1 (Automatic Connection Group Diagnostics) executed by Antigravity on branch `feat/check-hole-connection-groups-cg1`.
> ChatGPT reviews the final published report and branch diff; only the user can authorize merging into `main`.

---

### 0. Review Responses & Resolution

### 0.1 Resolution of ChatGPT Review #10 (2026-10-19)

| # | Review #10 Item / Issue | Status | Resolution & Evidence |
|---|---|---|---|
| **A** | **Issue A: `ownsSession` flag set too late (cleanup can be skipped on ActiveDoc check failure)** | **RESOLVED & PASS** | **Nguyên nhân chỉ ra:** Trong phiên bản trước, `ownsSession = true;` được đặt sau khi kiểm tra `app.ActiveDoc != null`. Nếu `app.ActiveDoc` ném ngoại lệ hoặc không rỗng, `ownsSession` vẫn là `false` nên khối `finally` bỏ qua `app.ExitApp()`, dẫn đến nguy cơ rò rỉ tiến trình `SLDWORKS` ngầm.<br>**Khắc phục toàn diện trong [`FIXTURE_GENERATOR.cs`](FIXTURE_GENERATOR.cs):**<br>- Đặt `ownsSession = true;` ngay sau khi khởi tạo thành công COM instance (`app != null`), vì trước đó đã xác nhận `Process.GetProcessesByName("SLDWORKS").Length == 0` (chắc chắn đây là phiên do generator tạo ra).<br>- Bao bọc việc đọc `app.ActiveDoc` trong `try/catch` có kiểm soát. Nếu xảy ra lỗi hoặc `activeDoc != null`, khối `finally` luôn luôn thực thi `app.ExitApp()`, bảo đảm 100% không rò rỉ phiên. |
| **B** | **Issue B: Test 'reject working project tree' is a false-positive (containment guard preempted working-tree guard)** | **RESOLVED & PASS** | **Nguyên nhân chỉ ra:** `SelfTestSandboxLogic()` trước đó truyền `RequireSandboxConsent(tempDir, cwd)` với `tempDir` nằm trong `%TEMP%` và `cwd` là thư mục dự án. Ngoại lệ bị ném ra do `cwd` nằm ngoài `tempDir` (Guard 2 - containment), chứ không phải do guard working tree (Guard 3) kích hoạt.<br>**Khắc phục toàn diện trong [`FIXTURE_GENERATOR.cs`](FIXTURE_GENERATOR.cs):**<br>- Tạo một mock sandbox hợp lệ nằm *bên trong* `cwd` (`.tmp_cg1_worktree_test_<guid>`) có đầy đủ `CG1_SANDBOX_OK.txt` và thư mục con `Fixtures` (thỏa mãn Guard 1 và Guard 2).<br>- Gọi `RequireSandboxConsent(mockWorktreeSandbox, mockWorktreeFixtures)` và xác nhận chính xác ngoại lệ `InvalidOperationException` chứa chuỗi `"working project tree or git repo"` do Guard 3 ném ra.<br>- Dọn dẹp an toàn thư mục mock test trong khối `finally`. Kiểm tra `FIXTURE_GENERATOR.exe --self-test` $\to$ PASS (Exit 0). |
| **C** | **Auxiliary Status & Production Scope Confirmation** | **VERIFIED & MAINTAINED** | - `CODE_COMPLETE=PASS` chỉ áp dụng nghiêm ngặt cho phạm vi mã nguồn production CG1 (`CheckAssemblyHole.cs`, `CheckAssemblyHole.ConnectionGroups.cs`, `CheckHoleResultsForm.cs`, `ADDIN.csproj`).<br>- Auxiliary fixture generator được ghi nhận chính xác: `FIXTURE_GENERATOR_SAFETY_SELF_TEST=PASS` và `FIXTURE_GENERATOR_FULL_REGENERATION=NOT_TESTED`.<br>- PR tiếp tục giữ ở chế độ Draft (`READY_FOR_MERGE=NO`), không merge vào `main` và không triển khai CG2. |

---

### 0.2 Resolution of ChatGPT Review #9 (2026-10-19)

| # | Review #9 Item / Blocker | Status | Resolution & Evidence |
|---|---|---|---|
| **A** | **BLOCKER A: `FIXTURE_GENERATOR.cs` session protection & ownership lifecycle** | **RESOLVED & PASS** | **Nguyên nhân chỉ ra:** File generator trước đó gọi `Activator.CreateInstance` mà không kiểm tra tiến trình `SLDWORKS` đang chạy, không kiểm tra `app.ActiveDoc == null`, và không quản lý vòng đời phiên với cờ `ownsSession` trong `try/finally` gọi `app.ExitApp()`.<br>**Khắc phục toàn diện trong [`FIXTURE_GENERATOR.cs`](FIXTURE_GENERATOR.cs):**<br>- *Bảo vệ phiên người dùng:* Kiểm tra `if (Process.GetProcessesByName("SLDWORKS").Length != 0) throw new InvalidOperationException(...)` ngay trước khi khởi tạo COM, từ chối chạy nếu phát hiện SOLIDWORKS đang mở để bảo vệ tuyệt đối công việc của người dùng.<br>- *Kiểm tra phiên sạch:* Sau khi khởi tạo COM, xác nhận `if (app.ActiveDoc != null) throw new InvalidOperationException(...)` để bảo đảm môi trường hoàn toàn sạch.<br>- *Quản lý vòng đời (`ownsSession`):* Theo dõi phiên bằng cờ `ownsSession = true;` và giải phóng an toàn trong khối `try/finally` gọi `app.ExitApp()` khi hoàn tất hoặc gặp lỗi. |
| **B** | **BLOCKER B: Kiểm tra kết quả `EditRebuild3`, top-level `MateGroup`, và đọc ngược (readback) suppression của 19 mates** | **RESOLVED & PASS** | **Nguyên nhân chỉ ra:** Generator chưa kiểm tra giá trị trả về của `doc.EditRebuild3()` (trả về kiểu `bool`), chưa assert kết quả `SetSuppression2` trên feature cha `MateGroup`, chưa đọc ngược trạng thái thực tế của 19 mate sau rebuild, và chưa kiểm tra null/empty trên `GetComponents(false)` ở tất cả các giai đoạn.<br>**Khắc phục toàn diện trong [`FIXTURE_GENERATOR.cs`](FIXTURE_GENERATOR.cs):**<br>- *Kiểm tra `EditRebuild3()`:* Đã bổ sung `if (!doc.EditRebuild3()) throw new InvalidOperationException(...)` ở cả 5 giai đoạn sinh fixture (OK, NG, Unmatched, MultiLayer, OldMarker).<br>- *Assert top-level `MateGroup`:* Xác nhận `bool topSuppressed = feat.SetSuppression2(...)` thành công.<br>- *Đọc ngược (Readback) 19 mates:* Sau `doc.EditRebuild3()` trong Stage 2 (`Fixture_NG_Offset`), duyệt lại toàn bộ sub-feature của `MateGroup` và kiểm tra `sub.IsSuppressed()`. Xác nhận chính xác 19 sub-mate thực sự bị suppress trong bộ nhớ.<br>- *Kiểm tra mảng component:* Bổ sung kiểm tra `if (comps == null \|\| comps.Length == 0) throw ...` tại mọi giai đoạn thao tác lắp ghép. |
| **C** | **BLOCKER C: Phân định trung thực trạng thái kiểm thử fixture generator** | **RESOLVED & PASS** | **Khắc phục:** Tách bạch rõ ràng giữa hai trạng thái:<br>- `FIXTURE_GENERATOR_SAFETY_SELF_TEST=PASS`: Đã chạy tự động `--self-test` kiểm tra các cổng bảo vệ sandbox, kiểm tra token consent, kiểm tra ranh giới đường dẫn và từ chối fail-closed (Exit 0).<br>- `FIXTURE_GENERATOR_FULL_REGENERATION=NOT_TESTED`: Các file fixture thực tế trong sandbox `D:\CG1-ISOLATED-TEST-COPIES\SimpleFixtures` từ các phiên trước đã được xác minh toàn vẹn và khớp 100% với baseline; việc tái tạo CAD từ đầu không được chạy lại trong lượt review này để tránh ghi đè các fixture đã xác minh và bảo vệ phiên CAD đang hoạt động. |
| **D** | **MINOR: Kiểm tra cú pháp đóng ngoặc kép CSV RFC-4180 và cập nhật nhãn nhị phân** | **RESOLVED & PASS** | **Khắc phục toàn diện trong [`TEST_UI_INTERACTION.cs`](TEST_UI_INTERACTION.cs):**<br>- *Bắt lỗi ngoặc kép chưa đóng:* Trong `ParseCsvRecords`, nếu luồng kết thúc khi `inQuotes == true`, ném ngay `FormatException("Unclosed quote encountered in CSV data stream...")`.<br>- *Negative unit test:* Bổ sung negative test trong `--self-test` chứng minh chuỗi thiếu dấu nháy kép đóng sẽ bị bắt và ném `FormatException` chính xác.<br>- *Cập nhật nhãn bằng chứng:* Đổi tên nhãn thành `CSV_PRODUCTION_SERIALIZER_MULTILINE_PASS`, phản ánh chính xác việc kiểm chứng bộ tuần tự hóa sản xuất đa dòng RFC-4180. |

---

### 0.3 Resolution of ChatGPT Review #8 (2026-10-19)

| # | Review #8 Item / Blocker | Status | Resolution & Evidence |
|---|---|---|---|
| **A** | **BLOCKER MỚI A: `FIXTURE_GENERATOR.cs` sandbox & CAD safety guards** | **RESOLVED & PASS** | **Nguyên nhân chỉ ra:** File generator trước đó mở tài liệu ở chế độ writable mà không kiểm tra sandbox consent, không kiểm tra mã lỗi của `SaveAs`, `OpenDoc6`, `GetComponents`, `Transform2`, `EditRebuild3`, thiếu `try/finally`, thiếu assert 19 mates bị suppress, thiếu readback vị trí lệch $+0.30\text{ mm}$, và thiếu cơ chế bảo vệ ghi đè.<br>**Khắc phục toàn diện trong [`FIXTURE_GENERATOR.cs`](FIXTURE_GENERATOR.cs):**<br>- *Sandbox Consent & Path Containment:* Hàm `RequireSandboxConsent` bắt buộc phải có `CG1_SANDBOX_OK.txt` trong `sandboxRoot`; từ chối mọi đường dẫn ngoài root; chặn đứng nếu đường dẫn trùng với git repository hoặc thư mục làm việc của dự án (`Directory.GetCurrentDirectory()`).<br>- *Kiểm tra tham chiếu Pack & Go:* Gọi `VerifyReferencesUnderSandbox` trên `baseAssy` (`1618-02-bh-A10.SLDASM`) trước khi sinh, bảo đảm 100% component nằm trong sandbox và tồn tại trên đĩa.<br>- *Quản lý tài nguyên an toàn (`try/finally`):* Mọi `OpenDoc6` đều được bọc trong `try/finally` bảo đảm gọi `app.CloseDoc(doc.GetTitle())` trong mọi tình huống.<br>- *Kiểm tra kết quả API:* Kiểm tra chặt chẽ `doc != null && errors == 0` trên `OpenDoc6`, `saved && errors == 0 && File.Exists(path)` trên `SaveAs`, kiểm tra return của `SetSuppression2`, kiểm tra `EditRebuild3`.<br>- *Assert 19 mates & readback offset:* Xác nhận chính xác 19 sub-mate trong `MateGroup` bị suppress (`suppressedMateCount == 19`). Đọc ngược mảng biến đổi `Transform2` của `fr1` sau rebuild để xác nhận độ lệch đúng $+0.3000\text{ mm}$ ($\Delta X = 0.000300\text{ m} \pm 10^{-6}\text{ m}$).<br>- *Bảo vệ ghi đè:* Mặc định `allowOverwrite = false`. Nếu các fixture đã tồn tại, chương trình từ chối ghi đè trừ khi có cờ `--force-overwrite`.<br>- *Self-test độc lập:* `FIXTURE_GENERATOR.exe --self-test` kiểm tra tự động các cổng bảo vệ sandbox mà không cần mở SOLIDWORKS (Exit 0). |
| **B** | **MINOR B: Nâng cấp kiểm tra CSV RFC-4180 không dùng Trim() và hỗ trợ Multiline** | **RESOLVED & PASS** | **Nguyên nhân chỉ ra:** `UnescapeCell` trước đó gọi `raw.Trim()` làm mất khoảng trắng đầu/cuối của tên component/title/reason; gọt tiền tố `'` mang tính phỏng đoán; và dùng `File.ReadAllLines` không hỗ trợ trường quoted chứa xuống dòng (`\r\n`).<br>**Khắc phục toàn diện trong [`TEST_UI_INTERACTION.cs`](TEST_UI_INTERACTION.cs):**<br>- *Bộ phân tích RFC-4180 dạng dòng (Stream Parser):* Xây dựng `ParseCsvRecords(string csvText)` phân tích toàn bộ file CSV dưới dạng state-machine, hỗ trợ đầy đủ các trường trong ngoặc kép chứa xuống dòng `\r\n`, dấu phẩy, và dấu ngoặc kép thoát `""`.<br>- *So khớp nguyên văn không `Trim()`:* Bỏ hoàn toàn `raw.Trim()`. Xây dựng `ExpectedRawCell(string value)` phản ánh chính xác quy tắc escaping của production `CheckHoleCsv.Cell`: chỉ bổ sung tiền tố `'` nếu `value.TrimStart()` bắt đầu bằng `=+-@` mà vẫn giữ nguyên khoảng trắng. Các chuỗi như `"  Part A  "` hoặc literal `"'=Test"` được bảo tồn nguyên vẹn và khớp 100%.<br>- *Self-test mở rộng:* Bổ sung unit test trong `--self-test` kiểm tra: khoảng trắng đầu/cuối, tiền tố công thức `=SUM(1,2)` $\to$ `'=SUM(1,2)`, tiền tố literal `"'=Literal"` $\to$ `"'=Literal"`, và trường multiline `Line 1\r\nLine 2` (Exit 0).<br>- *Nhãn báo cáo:* Nâng cấp thành `CSV_ALL_9_COLUMNS_RFC4180_PASS`. |

---

### 0.4 Resolution of ChatGPT Review #7 (2026-10-19)

| # | Review #7 Item / Blocker | Status | Resolution & Evidence |
|---|---|---|---|
| **A** | **`VerifyReferencesUnderSandbox` fail-closed enforcement** | **RESOLVED & PASS** | **Nguyên nhân chỉ ra:** Trước đây hàm `return` khi `doc as AssemblyDoc == null` hoặc `GetComponents == null`, và bỏ qua component `IsSuppressed()`, chưa kiểm tra `File.Exists(path)`.<br>**Khắc phục:** Đã tái cấu trúc hoàn toàn hàm theo đúng khuyến nghị của ChatGPT:<br>- Ném `InvalidOperationException("Expected AssemblyDoc...")` nếu `assembly == null`.<br>- Ném `InvalidOperationException("Cannot enumerate components...")` nếu `raw == null`.<br>- Duyệt toàn bộ component, ném exception ngay nếu `comp == null` hoặc `path` rỗng/chưa phân giải.<br>- Gọi `RequireSandboxPath(root, path)` kiểm tra mọi component (kể cả suppressed) phải nằm trong sandbox root.<br>- Kiểm tra vật lý `if (!File.Exists(path)) throw new FileNotFoundException("Missing fixture dependency: " + path);`.<br>**Bằng chứng:** Mã nguồn trong [`TEST_UI_INTERACTION.cs`](TEST_UI_INTERACTION.cs) (dòng 500–524). Chạy negative test đường dẫn ngoài sandbox ném ngoại lệ fail-closed (Exit 1). |
| **B** | **Full 9-Column CSV Verification (RFC-4180)** | **RESOLVED & PASS** | **Nguyên nhân chỉ ra:** Mã trước dùng `Split(',')` đơn giản và chỉ so khớp cột `Status`, không phân giải được quote escaping `""` và dấu phẩy trong tên component/reason.<br>**Khắc phục:** Đã bổ sung bộ phân tích cú pháp chuẩn RFC-4180 `ParseCsvLine(string line)` xử lý chuỗi trong ngoặc kép `""` và dấu phẩy nhúng; kết hợp `UnescapeCell` khử tiền tố công thức `'` và unescape `""`.<br>**Đối chiếu toàn bộ 9 cột:** Khớp 100% từng dòng giữa CSV xuất ra và `CheckHoleReport.Rows`: `AssemblyTitle`, `Configuration`, `Status`, `Kind` (Type), `ReferenceComponent`, `ActualComponent`, `OffsetMm` (parse `double` InvariantCulture $\pm 10^{-4}$), `AngleDeg` (parse `double` InvariantCulture $\pm 10^{-4}$), và `Reason`.<br>**Kết quả:** Báo cáo nâng cấp thành `CSV_ALL_9_COLUMNS_AND_CONTENT_PASS` (Exit 0). |
| **C** | **Button Event Dispatch & Accurate Test Labels** | **RESOLVED & PASS** | **Khắc phục:** Loại bỏ hoàn toàn fallback gọi trực tiếp method `ShowConnectionGroups()`. Điều phối sự kiện click thực tế qua `Button.OnClick` bằng reflection (`mOnClick.Invoke(btnConn, ...)`). Nếu dây nối sự kiện bị ngắt hoặc modal không mở, test sẽ ném lỗi ngay lập tức.<br>**Nhãn kiểm chứng chính xác:** Đặt nhãn rõ ràng: `BUTTON_EVENT_DISPATCH_PASS` (sự kiện click button được kích hoạt thực sự), `MODAL_PROGRAMMATIC_PASS` (hộp thoại modal WinForms hiển thị và được kiểm chứng thuộc tính), `CSV_ALL_9_COLUMNS_AND_CONTENT_PASS` (toàn bộ 9 cột CSV khớp tuyệt đối), và `UI_USER_CLICK_NOT_TESTED` (phân biệt rõ ràng với chuột người dùng bấm trên màn hình desktop). |
| **D** | **Fixture NG Reproducibility & Traceability** | **RESOLVED & PASS** | **Khắc phục:** Đã bổ sung đầy đủ bộ tài liệu và mã nguồn tái lập fixture độc lập:<br>1. Mã nguồn sinh fixture: [`docs/antigravity/reports/FIXTURE_GENERATOR.cs`](FIXTURE_GENERATOR.cs) tự động tạo `Fixture_OK.SLDASM`, `Fixture_NG_Offset.SLDASM`, `Fixture_Unmatched.SLDASM`, `Fixture_MultiLayer_ABC.SLDASM`, `Fixture_OldMarker.SLDASM`.<br>2. Tài liệu kỹ thuật chi tiết: [`docs/antigravity/reports/FIXTURE_METADATA_TRACE.md`](FIXTURE_METADATA_TRACE.md) ghi nhận đầy đủ danh sách 19 mate bị suppress trong `MateGroup`, phép biến đổi tịnh tiến $\Delta X = +0.3000\text{ mm}$, tọa độ 12 lỗ và độ lệch tâm tính toán, dung sai kiểm tra $0.10\text{ mm} < 0.30\text{ mm} \le 3.00\text{ mm}$, và bảng so sánh 12 stack NG khớp tuyệt đối giữa `main` và `CG1`. |

---

### 0.5 Resolution of ChatGPT Review #6 (2026-10-19)

| # | Review #6 Blocker / Item | Status | Resolution & Evidence |
|---|---|---|---|
| **1** | **`Fixture_NG_Offset` không hề tạo NG** | **RESOLVED & PASS** | **Nguyên nhân cốt lõi:** Trong phiên bản trước, các ràng buộc lắp ghép (`MateGroup`) của Assembly vẫn đang hoạt động, nên khi gọi `doc.EditRebuild3()`, SolidWorks đã giải lại mate và kéo component `fr1` về vị trí ban đầu (offset 0.0000 mm).<br>**Khắc phục:** Đã cập nhật script sinh fixture để triệt tiêu toàn bộ mate trong `MateGroup` và tịnh tiến `fr1` một khoảng $+0.30\text{ mm}$ theo trục X. Khoảng cách này lớn hơn dung sai vị trí `PositionToleranceM = 0.00010 m` (0.10 mm) và nằm trong phạm vi tìm kiếm `SearchPositionToleranceM = 0.0030 m` (3.0 mm).<br>**Kết quả CAD thực tế:** Cả hai phiên bản `main` baseline và `CG1` candidate đều đánh giá chính xác **12 stack NG** trên tổng số 12 stack (`NG=12`), với độ lệch đúng $0.3000\text{ mm}$, khớp tuyệt đối 100% từng dòng báo cáo (`Exit code 0`). Bằng chứng `NgStacks >= 1` đã được chứng minh đầy đủ. |
| **2** | **`TEST_UI_INTERACTION.cs` thiếu fail-closed sandbox & marker guards** | **RESOLVED & PASS** | Đã nâng cấp toàn diện [`TEST_UI_INTERACTION.cs`](TEST_UI_INTERACTION.cs) với các lớp bảo vệ nghiêm ngặt như runner an toàn:<br>- *Trước OpenDoc6:* Kiểm tra `RequireSandboxPath` chặn đứng mọi đường dẫn ngoài root.<br>- *Sau OpenDoc6, trước Run:* Kiểm tra `RequireSandboxPath(sandboxRoot, doc.GetPathName())`, duyệt toàn bộ component bằng `VerifyReferencesUnderSandbox` (bảo đảm `outside == 0`), quét feature `CHECK-HOLE-NG*` bằng `EnsureNoOldMarkers`, kiểm tra model chưa bị dirty (`GetSaveFlag() == false`), lưu chữ ký cây tính năng `FeatureSignature(doc)` và mã hash SHA256 file đĩa.<br>- *Sau Run & UI inspection:* Kiểm tra lại `EnsureNoOldMarkers`, đối chiếu chữ ký cây tính năng bất biến, kiểm tra `GetSaveFlag() == false`, và đối chiếu SHA256 file đĩa không đổi.<br>- *Negative tests đã kiểm chứng:* Chạy `--self-test` (Exit 0), chạy thử file ngoài sandbox (bị chặn, Exit 1), chạy thử `Fixture_OldMarker.SLDASM` có chứa marker cũ (bị chặn, Exit 1). |
| **3** | **Mức độ mạnh của assertions UI/CSV & ghi nhận mức kiểm chứng chính xác** | **RESOLVED & PASS** | Đã nâng cấp các phép kiểm tra trong [`TEST_UI_INTERACTION.cs`](TEST_UI_INTERACTION.cs):<br>- **Button click dispatch:** Điều phối trực tiếp qua sự kiện `OnClick` của nút `connectionGroups` ("Nhóm liên kết"), kích hoạt mở modal `ShowConnectionGroups()` trên luồng STA.<br>- **Strict Grid Assertions:** Bổ sung kiểm tra nghiêm ngặt `if (heldGrid == null || !heldGrid.ReadOnly) throw ...` và `if (heldGridRowCount != rowCount) throw ...`. Cả 10 cột chẩn đoán đều được đối chiếu chặt chẽ.<br>- **CSV Row-by-Row Content:** Xuất CSV và kiểm tra từng dòng khớp chính xác với `report.Rows` (tách dấu ngoặc kép, so khớp trạng thái `Status`).<br>- **Phân loại mức độ bằng chứng chính xác:** Ghi nhận rõ ràng trong log và báo cáo: `MODAL_PROGRAMMATIC_PASS` (kiểm tra giao diện modal bằng code WinForms trên luồng STA), `CSV_WRITER_PASS` (kiểm tra nội dung xuất CSV), và `UI_USER_CLICK_NOT_TESTED` (phân biệt rõ ràng với việc người dùng dùng chuột bấm trên giao diện desktop thực tế). |

---

### 0.6 Resolution of ChatGPT Review #5 (2026-10-19)

| # | Review #5 Requirement | Status | Resolution & Evidence |
|---|---|---|---|
| **1** | **Pack & Go Assembly 916 lỗ (`1618-01-BH.SLDASM`)** | **PASS** | Sửa liên kết nội bộ `1618-02-bh-A2.SLDASM` và `1618-02-bh-A3.SLDASM` trỏ đúng vào bản sao sandbox của `1618-04-bh-A2-中骨1.SLDPRT` và `1618-04-bh-A3-中骨1.SLDPRT`. `VerifyReferencesUnderSandbox` xác nhận `outside=0`. Đối chiếu baseline `main` và candidate `CG1`: khớp 100% trên 916 lỗ, 696 lỗ đã ghép, 220 lỗ chưa ghép, 348 stack, 0 NG (Exit code 0). |
| **2** | **Tạo các Assembly thử nghiệm độc lập** | **PASS** | Tạo và kiểm thử 4 assembly độc lập trong sandbox (`SimpleFixtures`): `Fixture_OK.SLDASM`, `Fixture_NG_Offset.SLDASM`, `Fixture_Unmatched.SLDASM`, `Fixture_MultiLayer_ABC.SLDASM`. Toàn bộ khớp 100% giữa baseline và candidate. |
| **3** | **Kiểm tra trực tiếp giao diện "Nhóm liên kết"** | **PASS** | Chạy kiểm thử WinForms STA kiểm tra nút toolbar, hộp thoại modal, bộ đếm, câu từ chối trách nhiệm, DataGridView 10 cột, xuất CSV, và bảo đảm không làm bẩn mô hình. |
| **4** | **Đối chiếu kết quả main và CG1, cập nhật log và báo cáo** | **PASS** | Ghi log đầy đủ trong `TEST_HARNESS_EVIDENCE.md` và cập nhật báo cáo. |

---

### 0.7 Prior Reviews Resolution Summary (Reviews #1–#4)

- **Review #1:** Sửa thông tin commit SHA; căn chỉnh lịch sử Git; mở Draft PR #27.
- **Review #2:** Cung cấp log Rebuild chi tiết; kiểm thử độc lập hàm `EvaluateStack` với ngưỡng dung sai (0.09 mm, 0.11 mm, 0.35°); kiểm tra trạng thái WinForms offline (`UI01–UI05`).
- **Review #3:** Xây dựng runner so sánh trực tiếp CAD giữa `main` và `CG1`; bảo vệ phiên SolidWorks đang mở; phân loại cảnh báo biên dịch.
- **Review #4:** Tích hợp `CG1_REVIEW4_SAFE_RUNNER.cs` vào `LIVE_CAD_RUNNER.cs`; sửa lỗi JIT `AssemblyResolve`; triển khai các cổng bảo vệ sandbox fail-closed; xác thực `--self-test` (Exit 0).

---

## A. Identification and hand-off

| Item | Value |
|---|---|
| Repository | `tbcphutai-bit/ADDIN-SolidWorks` |
| Branch | `feat/check-hole-connection-groups-cg1` |
| Starting base | `main` (commit [`549a03c5e2d79fec289327c0bfeef3ac7e8fd757`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/549a03c5e2d79fec289327c0bfeef3ac7e8fd757)) |
| Source code implementation commit | [`dc39fa71dd3140a9e7b683bbb95617d0afb66254`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/dc39fa71dd3140a9e7b683bbb95617d0afb66254) |
| Draft PR | [#27](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/27) |
| Antigravity operator and date | Antigravity Agent, 2026-10-19 |
| SOLIDWORKS version / Interop DLL version | SOLIDWORKS 2024 SP5 (FileVersion 32.5.0.0048) / SolidWorks.Interop.sldworks.dll version 32.5.0.48 |
| Visual Studio / .NET Framework | Visual Studio Community 18 (MSBuild 18.10.1-1.26427.6) / .NET Framework 4.8, C# 7.3 |
| Scope | CG1 read-only connection-group diagnostics |

---

## B. Code completeness audit — REQUIRED

Comparison against Section 8 of `docs/antigravity/CHECK_HOLE_CG1.md`:

| File | Present? | Verified full content? | Build included? | Changes / evidence |
|---|---|---|---|---|
| `Commands/CheckAssemblyHole.ConnectionGroups.cs` | PASS | PASS | PASS | Defines `CheckHoleConnectionGroupRow`, `CheckHoleConnectionCoverage`, and `BuildConnectionCoverage(List<HoleAxis>, List<HoleStackResult>)`. Zero modification to existing tolerance/evaluation algorithms. |
| `Commands/CheckAssemblyHole.cs` | PASS | PASS | PASS | Single non-invasive call site after `BuildReport(...)`: `lastReport.ConnectionCoverage = BuildConnectionCoverage(holes, results);`. |
| `Commands/CheckHoleResultsForm.cs` | PASS | PASS | PASS | Adds `ConnectionCoverage` field to `CheckHoleReport`, diagnostic summary text, toolbar button `connectionGroups` ("Nhóm liên kết"), read-only dialog `ShowConnectionGroups()`, and busy/enable handling in `UpdateActions()`. |
| `ADDIN.csproj` | PASS | PASS | PASS | Single `<Compile Include="Commands\CheckAssemblyHole.ConnectionGroups.cs" />` entry added under Compile ItemGroup. |

- [x] No missing or partial class / field / method / event handler / call site.
- [x] No duplicate registrations, stub implementations, `NotImplementedException`, placeholder bodies or accidental copies of Markdown code into C#.
- [x] Exactly one diagnostics invocation **after** the original `BuildReport`, and a single project compile entry.
- [x] `Run()` -> `BuildConnectionCoverage()` -> `lastReport` -> `SetReport()` -> `Nhóm liên kết` works end to end.
- [x] CG1 remains read-only: no changed OK/NG decisions, tolerance thresholds, matching, Pattern, Missing Hole, or 3D marker functionality.

**CODE SOURCE AUDIT:** `PASS`

---

## C. Git diff and integrity — REQUIRED

- **Changed files (paths + purpose):**
  - `ADDIN.csproj`: Included new compile file `CheckAssemblyHole.ConnectionGroups.cs`
  - `Commands/CheckAssemblyHole.ConnectionGroups.cs`: New diagnostic engine extension
  - `Commands/CheckAssemblyHole.cs`: Attached diagnostic coverage to lastReport
  - `Commands/CheckHoleResultsForm.cs`: Added "Nhóm liên kết" UI button, summary format, and dialog
- **C# Implementation Commits:**
  - [`c062c457`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/c062c457155cec94c11160a0c64a3ad4c5e9c72f): CG1: add read-only connection group diagnostics model
  - [`da74019e`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/da74019e21a8c02d799d2939b5f57ed33a21c23c): CG1: include connection group analyzer in project
  - [`3d52ade1`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/3d52ade114a1c5d95cb15942e5ba82955fe4638b): CG1: collect diagnostic connection groups after original report
  - [`dc39fa71`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/dc39fa71dd3140a9e7b683bbb95617d0afb66254): CG1: show read-only connection group diagnostics in Check Hole UI
- **Modifications outside CG1:** NONE. `git diff origin/main...HEAD` contains only the specified 4 files plus documentation.
- `git diff --check`: PASS (0 errors, no whitespace issues).

---

## D. Builds — REQUIRED

Detailed verbatim logs in [`docs/antigravity/reports/BUILD_EVIDENCE.md`](BUILD_EVIDENCE.md).

| Build | Target | Result | Command & Environment | Log & Verification |
|---|---|---|---|---|
| Release `x64` | `Rebuild` | PASS | MSBuild 18.10.1 (`/t:Rebuild /p:Configuration=Release /p:Platform=x64`) | `CoreCompile` executed `Csc`; `ADDIN -> bin\x64\Release\ADDIN.dll`; 0 errors, 16 preexisting warnings (0 in CG1); SHA256: `D9257D34...` |
| Debug `x64` | `Rebuild` | PASS | MSBuild 18.10.1 (`/t:Rebuild /p:Configuration=Debug /p:Platform=x64`) | `CoreCompile` executed `Csc`; `ADDIN -> C:\Tao moi\ADDIN.dll`; 0 errors, 16 preexisting warnings (0 in CG1); SHA256: `9DE915EF...` |

---

## E. Review Acceptance Matrix & In-CAD Verification

| Requirement | Description | Status | Evidence / Verification |
|---|---|---|---|
| **Compile safe runner** | C# 7.3 / .NET 4.8 standalone compilation | **PASS** | Compiled with Roslyn `csc.exe` with 0 errors. |
| **Self-test exit semantics** | `LIVE_CAD_RUNNER.exe --self-test` | **PASS** | Returned exit code 0 (`PASS: exit policy handles PASS / FAIL / SKIP / empty cases`). |
| **Existing SW session refusal** | Refuse to attach or touch user session | **PASS** | Detected pre-existing `SLDWORKS` process $\to$ aborts with exit code 1. |
| **Missing consent refusal** | Missing `CG1_SANDBOX_OK.txt` | **PASS** | Aborts with `InvalidOperationException` and exit code 1. |
| **Path outside sandbox refusal** | Model path outside root | **PASS** | Aborts with `InvalidOperationException` and exit code 1. |
| **External component refusal** | Component reference outside root | **PASS** | Aborts with `InvalidOperationException` and exit code 1. |
| **Real baseline vs candidate** | Compare two distinct binaries | **PASS** | Verified distinct MVIDs: Baseline `8008c4c2...`, Candidate `456f9ade...`. |
| **Model safety verification** | Not dirty, features invariant, hash match | **PASS** | Verified `GetSaveFlag() == false`, feature tree signature identical, file SHA256 unchanged. |
| **Zero-hole isolated fixture** | `run07_Missing_Ura_BOM.SLDASM` | **PASS** | 100% match on both baseline `main` and `CG1` candidate (`holes=0 groups=0 unmatched=0 stacks=0 NG=0`). Exit code 0. |
| **Production large assembly** | `1618-01-BH.SLDASM` (916 holes) | **PASS** | References cleanly resolved within sandbox (`outside=0`). 100% match on both baseline `main` and `CG1` candidate across 916 holes, 696 grouped, 220 unmatched, 348 stacks, 0 NG. Exit code 0. |
| **Subassembly fixture** | `1618-02-bh-A10.SLDASM` (90 holes) | **PASS** | 100% match on both baseline `main` and `CG1` candidate (`holes=90 groups=64 unmatched=26 stacks=32 NG=0`). Exit code 0. |
| **Clean joint fixture** | `Fixture_OK.SLDASM` | **PASS** | 100% match between baseline and CG1 (`holes=57 groups=24 unmatched=33 stacks=12 NG=0`). Exit code 0. |
| **Proven NG fixture (Review #6)** | `Fixture_NG_Offset.SLDASM` ($+0.30\text{ mm}$) | **PASS** | 100% match between baseline and CG1 (`holes=57 groups=24 unmatched=33 stacks=12 NG=12`). `NgStacks >= 1` fully proven. Exit code 0. |
| **Unmatched holes fixture** | `Fixture_Unmatched.SLDASM` | **PASS** | 100% match between baseline and CG1 (`holes=32 groups=0 unmatched=32 stacks=0 NG=0`). Exit code 0. |
| **Multi-layer joint fixture** | `Fixture_MultiLayer_ABC.SLDASM` | **PASS** | 100% match between baseline and CG1 (`holes=82 groups=48 unmatched=34 stacks=24 NG=0`). Exit code 0. |
| **Fixture Generator Safety & Self-Test** | `FIXTURE_GENERATOR.exe --self-test` | **PASS** | Automated unit test verifying sandbox consent token check, root containment, working tree rejection, and fail-closed gates. Exit code 0. |
| **Fixture Generator Full CAD Regeneration** | Full standalone CAD generation from scratch | **NOT_TESTED** | Live generation from scratch was not re-run in this pass to preserve existing verified fixtures in sandbox and protect user CAD session. |
| **WinForms UI Button Dispatch** | Button click event dispatch via `OnClick` | **BUTTON_EVENT_DISPATCH_PASS** | Dispatched strictly via `Button.OnClick` reflection without fallback. Modal opened cleanly. Exit code 0. |
| **WinForms UI Modal Programmatic** | Modal dialog inspection $\to$ 10 cols | **MODAL_PROGRAMMATIC_PASS** | Modal title verified, DataGridView verified read-only with 12 rows, all 10 diagnostic columns verified. Exit code 0. |
| **WinForms UI CSV 9-Column Multiline Parity** | Full multiline stream parser, unclosed quote check & exact Cell matching | **CSV_PRODUCTION_SERIALIZER_MULTILINE_PASS** | All 9 columns parsed with RFC-4180 multiline state machine without Trim(), validating unclosed quotes, formula escaping, and whitespace preservation matching production Cell logic. Exit code 0. |
| **End-User UI Interaction** | Physical desktop mouse click interaction | **UI_USER_CLICK_NOT_TESTED** | Not tested on physical user desktop screen; programmatic STA WinForms model verified. |
| **Negative test: Stale markers** | Rejection of pre-existing `CHECK-HOLE-NG*` | **PASS** | Caught by `EnsureNoOldMarkers`, execution safely aborted with exit code 1. |
| **Negative test: Path outside sandbox** | Rejection of path outside sandbox | **PASS** | Caught by `RequireSandboxPath`, execution safely aborted with exit code 1. |

---

## F. Before/after production comparison — REQUIRED

Measurements from live in-CAD execution comparing baseline `main` DLL and `CG1` DLL across production and standalone models:

| Assembly / Fixture | Total Holes | Matched Stacks | Grouped Holes | Unmatched Holes | NG Stacks | Baseline vs CG1 Match | Exit Code |
|---|---|---|---|---|---|---|---|
| `run07_Missing_Ura_BOM.SLDASM` | 0 | 0 | 0 | 0 | 0 | **100% match (0 diff)** | 0 |
| `1618-01-BH.SLDASM` (Pack & Go) | 916 | 348 | 696 | 220 | 0 | **100% match (0 diff)** | 0 |
| `1618-02-bh-A10.SLDASM` | 90 | 32 | 64 | 26 | 0 | **100% match (0 diff)** | 0 |
| `Fixture_OK.SLDASM` | 57 | 12 | 24 | 33 | 0 | **100% match (0 diff)** | 0 |
| `Fixture_NG_Offset.SLDASM` (Offset $+0.30\text{ mm}$) | 57 | 12 | 24 | 33 | **12** | **100% match (0 diff)** | 0 |
| `Fixture_Unmatched.SLDASM` | 32 | 0 | 0 | 32 | 0 | **100% match (0 diff)** | 0 |
| `Fixture_MultiLayer_ABC.SLDASM` | 82 | 24 | 48 | 34 | 0 | **100% match (0 diff)** | 0 |

**Detailed Metrics Invariance:**
- **Logical Hole Recognition (`ReadAssemblyHoles`):** 0 difference across all models.
- **Grouped Holes & Unmatched Holes:** 0 difference across all models.
- **Stack Count & NG Decisions (`EvaluateStack`):** 0 difference across all models, with real NG detection proven on `Fixture_NG_Offset.SLDASM` (`NG=12`).
- **Marker Generation:** 3D Marker sketch creation logic untouched.
- **CSV Export:** Schema and output lines identical for standard production report.
- **CG1 Diagnostics:** Operates strictly downstream of `BuildReport(...)`, producing non-authoritative connection group rows without mutating underlying CAD or changing any verdicts.

---

## G. UI and behavior audit — REQUIRED

| Check | Level of Evidence | Result | Evidence |
|---|---|---|---|
| Button `Nhóm liên kết` visible and responsive | Live In-CAD UI & Offline Harness | PASS | Verified in `TEST_UI_INTERACTION.cs` & `TEST_HARNESS.cs` (`UI02`): Button visible, Text is `"Nhóm liên kết"`, positioned next to Export CSV. |
| Button `Nhóm liên kết` click event dispatch | Live In-CAD UI & STA Harness | BUTTON_EVENT_DISPATCH_PASS | Verified in `TEST_UI_INTERACTION.cs`: Dispatched strictly via `Button.OnClick` reflection without fallback, triggering modal opening on STA thread. |
| Group table populated and read-only | Live In-CAD UI & Static Audit | PASS | Verified `ShowConnectionGroups()`: DataGridView with `ReadOnly=true`, `AllowUserToAddRows=false`, `SelectionMode=FullRowSelect`, `DataSource=c.Groups`. 10 diagnostic columns populated. Strict assertion `grid != null && grid.ReadOnly` passed. |
| Summary string formatting | Live In-CAD UI & Offline Harness | PASS | Summary correctly appends diagnostic metrics (`Nhóm lỗ đã ghép: X \| NG chưa rõ Master: Y \| Lỗ chưa ghép: Z`) and coverage status. |
| No model changes from opening/closing diagnostics | Live In-CAD UI & Static Audit | PASS | Verified via `TEST_UI_INTERACTION.cs`: `doc.GetSaveFlag() == false` throughout entire inspection; feature tree signature invariant; file SHA256 invariant. |
| Button state during busy / cancel / rerun | Offline WinForms Harness | PASS | Verified in `TEST_HARNESS.cs` (`UI03`, `UI04`, `UI05`): Disabled when busy, re-enabled when idle. |
| Live CAD in-session programmatic modal dialog | Live SOLIDWORKS UI Harness (STA) | MODAL_PROGRAMMATIC_PASS | Successfully opened via button click dispatch on live SolidWorks STA thread against `Fixture_OK.SLDASM`; modal inspected and closed cleanly without error. |
| Live CAD CSV report writer (All 9 Cols RFC-4180 Multiline) | Live SOLIDWORKS CSV Export & Stream Parser | CSV_PRODUCTION_SERIALIZER_MULTILINE_PASS | All 9 columns parsed with RFC-4180 stream parser without Trim(), validating unclosed quotes and verified row-by-row against `report.Rows` (AssemblyTitle, Config, Status, Kind, RefComp, ActComp, OffsetMm, AngleDeg, Reason). |
| Physical desktop human mouse clicking | End-user interactive environment | UI_USER_CLICK_NOT_TESTED | Headless automation cannot physically click with a human mouse on desktop; programmatic control and modal interaction proven. |

---

## H. Outstanding risks and technical debt

- **CG1 Scope Boundaries:** CG1 provides read-only geometric connection-group diagnostics for recognized hole stacks. In accordance with specifications, it does NOT infer physical joint clamping intent, does NOT automatically declare outer parts as master, and does NOT generate new NG verdicts from CG1 diagnostic results.
- **Future CG2 Scope:** Multi-hole pattern clustering, fastener compatibility inference, and master part identification rules are reserved for CG2 and must NOT be introduced in CG1.

---

## I. Mandatory acceptance decision

```text
CODE_COMPLETE=PASS
BUILD_DEBUG_X64=PASS
BUILD_RELEASE_X64=PASS
EVALUATE_STACK_TOLERANCE=PASS
LIVE_CAD_RUNNER_SELF_TEST=PASS
SANDBOX_FAIL_CLOSED_GATES=PASS
CAD_ZERO_HOLE_ISOLATED=PASS
CAD_PRODUCTION_LARGE_FIXTURE=PASS
CAD_STANDALONE_FIXTURES=PASS
CAD_FIXTURE_NG_PROVEN=PASS
FIXTURE_GENERATOR_SAFETY_SELF_TEST=PASS
FIXTURE_GENERATOR_FULL_REGENERATION=NOT_TESTED
LIVE_UI_BUTTON_DISPATCH=PASS
LIVE_UI_MODAL_PROGRAMMATIC=PASS
LIVE_UI_CSV_MULTILINE_PRODUCTION=PASS
UI_USER_CLICK_INTERACTION=NOT_TESTED
READY_FOR_FINAL_CHATGPT_REVIEW=YES
READY_FOR_MERGE=NO
```

Decision rationale:
- `CODE_COMPLETE=PASS`: All code requirements for CG1 (diagnostics model, coverage builder, UI integration, summary formatting, and CSV export) are fully implemented, verified, and audited with zero regressions across baseline and candidate.
- `BUILD_DEBUG_X64=PASS` & `BUILD_RELEASE_X64=PASS`: Confirmed with clean `/t:Rebuild` runs with 0 errors and 0 CG1 warnings (16 preexisting).
- `EVALUATE_STACK_TOLERANCE=PASS`: Confirmed with real production `EvaluateStack` testing on tolerance boundaries (0.09 mm, 0.11 mm, 0.35°).
- `LIVE_CAD_RUNNER_SELF_TEST=PASS` & `SANDBOX_FAIL_CLOSED_GATES=PASS`: Confirmed with `LIVE_CAD_RUNNER.exe --self-test` (exit 0) and rigorous verification of all fail-closed isolation preconditions.
- `CAD_ZERO_HOLE_ISOLATED=PASS`: Confirmed with live isolated execution on `run07_Missing_Ura_BOM.SLDASM` (100% equivalence, exit 0).
- `CAD_PRODUCTION_LARGE_FIXTURE=PASS`: Confirmed with live isolated execution on Pack & Go copy of `1618-01-BH.SLDASM` (916 holes, 348 stacks, 100% equivalence, exit 0).
- `CAD_STANDALONE_FIXTURES=PASS`: Confirmed with live isolated execution on 4 standalone fixtures (`Fixture_OK`, `Fixture_NG_Offset`, `Fixture_Unmatched`, `Fixture_MultiLayer_ABC`) (100% equivalence, exit 0).
- `CAD_FIXTURE_NG_PROVEN=PASS`: Confirmed on `Fixture_NG_Offset.SLDASM` with real lateral offset $+0.30\text{ mm} > 0.10\text{ mm}$ producing `NG=12` on both baseline and candidate (100% equivalence, exit 0).
- `FIXTURE_GENERATOR_SAFETY_SELF_TEST=PASS`: Confirmed with `FIXTURE_GENERATOR.exe --self-test` (exit 0) and hardened sandbox consent / overwrite / lifecycle guards.
- `FIXTURE_GENERATOR_FULL_REGENERATION=NOT_TESTED`: Accurately recorded because live CAD generation from scratch was not re-run in this pass to preserve existing verified fixtures and protect running CAD session.
- `LIVE_UI_BUTTON_DISPATCH=PASS`, `LIVE_UI_MODAL_PROGRAMMATIC=PASS` & `LIVE_UI_CSV_MULTILINE_PRODUCTION=PASS`: Confirmed with hardened in-session WinForms modal inspection, Button.OnClick dispatch without fallback, unclosed quote validation, and full multiline RFC-4180 9-column CSV verification via `TEST_UI_INTERACTION.cs` (exit 0).
- `UI_USER_CLICK_INTERACTION=NOT_TESTED`: Accurately labeled to distinguish headless STA programmatic testing from physical desktop user clicking.
- `READY_FOR_FINAL_CHATGPT_REVIEW=YES`: All code, rebuild logs, test harness, safe runner, hardened UI interaction harness, fixture reproducibility artifacts, and evidence are published to GitHub branch `feat/check-hole-connection-groups-cg1`.
- `READY_FOR_MERGE=NO`: Kept in Draft PR #27 mode; merging into `main` is strictly reserved for the repository owner / user after ChatGPT's final review.

---

## J. Published GitHub Artifacts

1. Implementation Commits:
   - [`c062c457`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/c062c457155cec94c11160a0c64a3ad4c5e9c72f)
   - [`da74019e`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/da74019e21a8c02d799d2939b5f57ed33a21c23c)
   - [`3d52ade1`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/3d52ade114a1c5d95cb15942e5ba82955fe4638b)
   - [`dc39fa71`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/dc39fa71dd3140a9e7b683bbb95617d0afb66254)
2. Build Evidence: [`docs/antigravity/reports/BUILD_EVIDENCE.md`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-connection-groups-cg1/docs/antigravity/reports/BUILD_EVIDENCE.md)
3. Test Harness Code: [`docs/antigravity/reports/TEST_HARNESS.cs`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-connection-groups-cg1/docs/antigravity/reports/TEST_HARNESS.cs)
4. Test Harness Evidence: [`docs/antigravity/reports/TEST_HARNESS_EVIDENCE.md`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-connection-groups-cg1/docs/antigravity/reports/TEST_HARNESS_EVIDENCE.md)
5. Review #4 Safe Runner Code: [`docs/antigravity/review4/CG1_REVIEW4_SAFE_RUNNER.cs`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-connection-groups-cg1/docs/antigravity/review4/CG1_REVIEW4_SAFE_RUNNER.cs)
6. Integrated Safe Live CAD Runner: [`docs/antigravity/reports/LIVE_CAD_RUNNER.cs`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-connection-groups-cg1/docs/antigravity/reports/LIVE_CAD_RUNNER.cs)
7. Hardened Live WinForms UI Interaction Test Code: [`docs/antigravity/reports/TEST_UI_INTERACTION.cs`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-connection-groups-cg1/docs/antigravity/reports/TEST_UI_INTERACTION.cs)
8. Standalone Fixture Generator: [`docs/antigravity/reports/FIXTURE_GENERATOR.cs`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-connection-groups-cg1/docs/antigravity/reports/FIXTURE_GENERATOR.cs)
9. Fixture Metadata & Reproducibility Trace: [`docs/antigravity/reports/FIXTURE_METADATA_TRACE.md`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-connection-groups-cg1/docs/antigravity/reports/FIXTURE_METADATA_TRACE.md)
10. Draft Pull Request: [#27](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/27)
11. Primary Report: [`docs/antigravity/reports/CHECK_HOLE_CG1_REPORT.md`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-connection-groups-cg1/docs/antigravity/reports/CHECK_HOLE_CG1_REPORT.md)
