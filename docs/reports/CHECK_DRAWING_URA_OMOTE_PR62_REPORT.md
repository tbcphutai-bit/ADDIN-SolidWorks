# Báo Cáo Kỹ Thuật PR #62: Xác Minh Mặt `表/ウラ` Trong `CHECK DRAWING`

- **Repository:** `tbcphutai-bit/ADDIN-SolidWorks`
- **Pull Request:** `#62` — Branch `fix/check-drawing-goban-display-retry-uraomote-20261108`
- **Base Commit đã kiểm tra:** `c98f277` (bản cập nhật mới sẽ được đính kèm commit SHA ngay trong bình luận PR #62 sau khi push)
- **Môi trường kiểm thử:** SOLIDWORKS 2024 SP5.0 (`SLDWORKS.exe 32.5.0.48`, x64 Debug), bộ dữ liệu bản sao cô lập tại `D:\CG1-ISOLATED-TEST-COPIES\4365` và `D:\CG1-ISOLATED-TEST-COPIES\4332a`
- **Phạm vi sửa đổi (chỉ `CHECK DRAWING`):**
  - [`ADDIN.csproj`](../../ADDIN.csproj)
  - [`Commands/CdFlatSidePilotPolicy.cs`](../../Commands/CdFlatSidePilotPolicy.cs)
  - [`Commands/CdIsolatedVisibleFlatSideVerifier.cs`](../../Commands/CdIsolatedVisibleFlatSideVerifier.cs)
  - [`Commands/CheckDrawingBom.cs`](../../Commands/CheckDrawingBom.cs)
  - [`docs/reports/CHECK_DRAWING_URA_OMOTE_PR62_REPORT.md`](CHECK_DRAWING_URA_OMOTE_PR62_REPORT.md)
  - Không sửa `CheckUraOmote.cs`, `CheckPartWorkingCopy.cs`, `ExcelDrawingCheckExporter.cs` hay bất kỳ command nào khác.

---

## 1. Trạng Thái Cờ An Toàn Production (Mặc Định TẮT)

Mọi cờ bật tính năng Direct Verdict, Reviewed Pilot và Isolated Visible Verification đều giữ **tắt mặc định (`false`)** trong mã nguồn commit:

- `CdEnableDirectFlatSideVerdict = false`
- `CdEnableReviewedFlatSidePilot = false`
- `CdReviewedFlatSidePilotOptInForTestsOnly = false`
- `CdEnableIsolatedVisibleVerification = false`
- `CdIsolatedVisibleVerificationOptInForTestsOnly = false`

Chế độ Shadow legacy giữ nguyên hành vi đối chiếu không can thiệp kết quả production cho đến khi người dùng/team review chốt chính sách nghiệp vụ cho các chi tiết có External Reference chưa khóa trỏ tới Assembly bẩn (`status=3`).

---

## 2. Phân Tích Nguyên Nhân Gốc Rễ (Theo Comment Review PR #62 `c98f277`)

### 2.1. 7 view trả về `GetVisibleEntities2 == 0` khi chạy batch ẩn (`strictHiddenBatch`)
Trong bộ 55 Drawing (`50` Drawing có ký hiệu `表/ウラ`, `5` Drawing không có trường phẳng), có `7` Drawing (`#19 SA1-fr1`, `#30 SA2-fr4`, `#33 SA1-fr2`, `#34 SA1-fr5`, `#35 SA1-fr3`, `#36 SA2-fr1`, `#42 SB3-fr1`) rơi vào `DIRECT_UNKNOWN` khi mở Drawing ẩn (`DocumentVisible(false)`):
- `IView.GetVisibleEntities2(null, swViewEntityType_Face)` trả về rỗng trong batch ẩn, đồng thời Display State của các Part này không có explicit face-level override (`display-state has no explicit face body`), nên đường đọc ẩn (`HIDDEN_DS_FACE_BODY`) không dựng được mặt để đọc màu.
- **Giải pháp đã chứng minh:** Dùng `CdIsolatedVisibleFlatSideVerifier` — một tiến trình `SLDWORKS.exe` out-of-process riêng (`_workerApp.Visible = false`, cửa sổ OS ẩn hoàn toàn, định danh ROT `SolidWorks_PID_<pid>`), chỉ mở **bản sao tạm (temp copy) của Drawing + Part** với `DocumentVisible(true)` (tuyệt đối không mở Assembly), đồng thời kiểm tra ranh giới đường dẫn cô lập (`IsPathInsideDirectory(actualRefPath, itemTempDir)`).
- **Fix lỗi `CopyAndRepointPartDependencies` trên `CdIsolatedVisibleFlatSideVerifier`:** Trước đây hàm key trùng lặp chỉ theo `normDep`, nên khi `GetDocumentDependencies2(..., searchFlag={true,false})` trả về cả đường dẫn resolved và đường dẫn lưu cũ, hàm tạo bản sao `*_1.SLDPRT` và bị `ISOLATED_PATH_GUARD_BLOCKED`. Đã sửa bằng cách copy `expectedPartPath` trước (`expectedTempPartPath`), map mọi dependency trùng tên `expectedPartName` về `expectedTempPartPath`, deduplicate các `.SLDPRT` khác qua `copiedBySourcePath`, và hỗ trợ đường dẫn tương đối trong `ResolveExistingPartFile`.
- **Kết quả sau fix:** `50/50` lượt gọi Isolated Worker thành công (`0` lỗi `ISO VERIFIER FAIL`, `0` lỗi `ISOLATED_PATH_GUARD_BLOCKED`, `50/50` đạt `bodySrc=VISIBLE_ENTITIES2`, `0` lần restart worker), toàn bộ `7/7` ca từng bị `DIRECT_UNKNOWN` trong chế độ ẩn đều đọc được màu `MODEL+FACE` rõ ràng qua worker cô lập.

### 2.2. Đối chiếu hình học 2D View <-> 3D Flat Pattern `Body2` (`DrawingViewCurveBuffer`)
- Để không tin mù quáng vào thuộc tính cấu hình/tên body khi Assembly đang dirty hoặc view lỗi thời, `CheckDrawingBom` thực hiện đối chiếu biên dạng 2D của Drawing View (qua `DrawingViewCurveBuffer.Read(rawBuffer, 1.0, out report, false)`) với `Box` chiếu của Flat Pattern `Body2`.
- Kết quả trên `50` Flat View trong Isolated Worker:
  - `49/50` đạt `geom2d=True` (`dW=0.000mm`, `dH=0.000mm`).
  - `1/50` phát hiện lệch thật là `#30 4365-04-TI-SA2-fr4`: `geom2d=False` (`dW=0.000mm`, `dH=10.000mm`, `arcs=1`, `view=369.00x83.50`, `body=369.00x73.50`), bị chặn fail-closed ở mức `Body=False` và ghi rõ `2D Geom LỆCH` trong Fact 1 & Fact 2.

### 2.3. Giải quyết dứt điểm nguyên nhân 35 WARNING Assembly Dirty và WARNING Drawing Dirty
1. **`35` WARNING liên quan Assembly Dirty trong bộ 55 Drawing:**
   - `34` dòng (`DIRECT_PROMOTION_BLOCKED`) vẫn bị giữ `WARNING` theo nguyên tắc an toàn **fail-closed** vì Part có External Reference In-Context đang ở trạng thái **chưa khóa** (`status=3`) trỏ trực tiếp tới các `.SLDASM` đang `dirty=True` (`GetSaveFlag=true`), ví dụ `4365-01-TI.SLDASM`, `4365-02-TI-UK1/UK2.SLDASM`, `4365-03-TI-FB1/SB1/SB2/SB3/SA1.SLDASM`. Hệ thống không tự ý khóa External Reference hay sửa/save file gốc.
   - `1` dòng là `#37 4365-04-TI-SA2-fr2` có External Reference **chỉ trỏ tới Assembly sạch** (`4365-03-TI-SA2.SLDASM`, `dirty=False`). Với Pilot + Isolated, `#37` chuyển từ **`Warning -> OK`** (`PILOT_DIRTY_ASSEMBLY_DIRECT_VERIFIED`), khớp đúng chứng minh có kiểm soát `3E1`/`3E2`.
2. **Drawing Dirty (`DIRTY_DRAWING_UNVERIFIED`):**
   - Toàn bộ `3` Drawing có `drawDirty=True` (`GetSaveFlag=true (NeedsRebuild2=0, OutOfDateViews=0)`) là `#07 4365-04-TI-SB1-ks2`, `#18 4365-04-TI-SB1-fr3`, `#28 4365-04-TI-UK2-l2` đều bị chặn fail-closed ở mức `WARNING` (`DIRTY_DRAWING_UNVERIFIED`). Riêng `#07 SB1-ks2` ở chế độ Shadow cũ từng cho `OK` dù Drawing dirty, nay ở Pilot+Isolated bị hạ an toàn từ **`OK -> Warning`** (do đó tổng đếm chung toàn lô 55 dòng không đổi: `OK=11, NG=0, Warning=44`, phản ánh `+1` từ `#37` bù `-1` từ `#07`).
3. **Ca hai mặt cùng hồng (`1B_TR11-rn1` / `1E_BOTH_PINK`):**
   - Trong Shadow cũ, Working Copy báo giả `NG` (`ウラ`). Với `CdFlatSidePilotPolicy` (`WC_BOTH_SIDES_PINK_AMBIGUOUS`), các ca này được phân loại chuẩn xác là **`Warning`** (chưa đủ căn cứ khẳng định chiều ngược từ Working Copy), đồng thời vẫn hiển thị kết quả quan sát Drawing View (Fact 1).

### 2.4. Tách bạch rõ Fact 1 và Fact 2 trên UI và Excel (không sửa `ExcelDrawingCheckExporter.cs`)
Hàm `CdFormatObservedViewSummary`, `CdShortSyncReason` và `AddFlatPatternSideChecks` trong [`Commands/CheckDrawingBom.cs`](../../Commands/CheckDrawingBom.cs) tách rõ hai sự thật cho mọi dòng bị chặn (`DIRECT_PROMOTION_BLOCKED`, `DIRTY_DRAWING_UNVERIFIED`, `WC_BOTH_SIDES_PINK_AMBIGUOUS`), ví dụ thực tế trích từ log:
- **`DrawingValue` (cột Drawing trên UI & Excel):**
  `図面ﾋﾞｭｰ13: 表 / View=mặt màu (表) (khớp chữ 表, 2D Geom khớp) | Đồng bộ: WARNING (ExtRef chưa khóa tới ASM bẩn: 4365-01-TI.SLDASM, 4365-02-TI-UK2.SLDASM, ...)`
  (Hoặc với `#30 SA2-fr4`: `2D Geom LỆCH | Đồng bộ: WARNING (2D Geom LỆCH; ExtRef chưa khóa tới ASM bẩn: ...)`)
- **`Message` (Banner chi tiết UI & Sheet Export):**
  `図面ﾋﾞｭｰ13: chữ 表; [Fact 1 - Mặt trên Drawing View] ... | [Fact 2 - Đồng bộ Model/Source] WARNING (<code>): ...`

---

## 3. Kết Quả Build, Self-Tests & Runtime Verification (Sau Lần Sửa Cuối Cùng)

### 3.1. Build & Toàn Vẹn File Gốc
- **Build Debug x64 (`MSBuild ADDIN.csproj /p:Configuration=Debug /p:Platform=x64`):** `0` errors.
  - Output `bin\x64\Debug\ADDIN.dll` SHA-256: `4EBEEAB324900DB2F33D37D8818AC7FCC8A91ABAED57EA53A7732834FE19BF3F`
- **`C:\Tao moi\ADDIN.dll`:** Giữ nguyên **không ghi đè**, SHA-256 unchanged `20BB502B7E8F37B0853F1104CDDFC49A991E266FAA38FE1BF63A8458D4C07CF5` (`2026-11-13 10:38:33`).
- **Tập tin nguồn thử nghiệm:** `SOURCE_INTEGRITY unchanged=True` (`6/6` file ở targeted suite), `SOURCE_FILES_UNCHANGED checked=20 allEqual=True` ở cả 2 lượt chạy batch 55 Drawing.

### 3.2. Unit / Regression Self-Tests (`run_selftest.ps1`)
- Kết quả: **Exit `0`**
  - `RunGobanLinkSelfTests PASSED ALL 29 CASES!`
  - `NormalizeFlatSideMarker` & `CompareFlatSideMarker`: passed
  - `DEFAULT_FLAGS`: tất cả `5` cờ đều `False` (`CdEnableDirectFlatSideVerdict=False`, `CdEnableReviewedFlatSidePilot=False`, `CdReviewedFlatSidePilotOptInForTestsOnly=False`, `CdEnableIsolatedVisibleVerification=False`, `CdIsolatedVisibleVerificationOptInForTestsOnly=False`)
  - `CdFlatSidePilotPolicy.RunSelfTests passed 15 synthetic policy assertions!`
  - `RunExcelAndPreviewSyncSelfTests passed!`
  - `RunWidthLengthSelfTests passed!`

### 3.3. Bộ Test Có Mục Tiêu Trên SOLIDWORKS 2024 SP5.0 (`targeted_post_final_stdout.log`)
Tất cả các phase (`p1`, `p1b`, `p1d`, `p2a`, `p2_1f2`, `p2_1g`, `p2c`, `p3_2a..p3_2d`, `p4`, `p4_3e`) hoàn thành với **Exit `0`**:

| Case / Phase | Kịch bản kiểm tra | Shadow | Pilot (+ Iso) | Ghi chú xác minh |
|---|---|---|---|---|
| `1A` (`SB1-fr8` clean) | Drawing `表` sạch | `OK` | `OK` | Khớp cả Direct & Working Copy |
| `1B` (`TR11-rn1`) | Working Copy cả 2 mặt hồng | `NG` (WC giả `ウラ`) | `Warning` | `WC_BOTH_SIDES_PINK_AMBIGUOUS`, Direct=`表 (OK)` |
| `1C` (`FB1-fr3`) | Không tô hồng + Drawing dirty | `Warning` | `Warning` | Shadow giữ fallback cũ; Pilot báo `DIRTY_DRAWING_UNVERIFIED` |
| `1D` (`4332-04-TI-P-1-l1`) | Project khác (`4332a`) thiếu ref | `Warning` | `Warning` | Cách ly phase riêng `p1d`, an toàn |
| `1E` (synthetic both pink) | Tô hồng cả 2 mặt Part | `NG` | `Warning` | `WC_BOTH_SIDES_PINK_AMBIGUOUS` |
| `1F` / `1F2` | Kế thừa màu `BODY+FACE` | `OK` | `OK` | Đọc đúng màu kế thừa |
| `1G` / `1G2` | Kế thừa màu `FEATURE+FACE` | `OK` | `OK` | Đọc đúng màu kế thừa |
| `2A` / `2B` / `2D` | Flip view / đảo mặt thật / đảo nhãn | `NG` | `NG` | Phát hiện đảo mặt / đảo nhãn chính xác (`表`/`ウラ`) |
| `2C` | Drawing `ウラ` đúng chuẩn | `OK` | `OK` | Xác nhận `ウラ` chuẩn không false NG |
| `3A` / `3B` / `3C` | ASM bẩn / sửa Part / sửa sibling chưa lưu | `Warning` | `Warning` | `3A/3C` giữ `Warning` tách rõ Fact 1 & Fact 2; `3B` hạ Direct=`Warning` |
| `3D1` / `3D2` | Part dirty / Drawing dirty riêng lẻ | `Warning` | `Warning` | `DIRTY_DRAWING_UNVERIFIED` chặn fail-closed |
| `3E1` / `3E2` | ASM bẩn nhưng ExtRef đã khóa sạch | `Warning` | **`OK`** | `PILOT_DIRTY_ASSEMBLY_DIRECT_VERIFIED` (chạy cả `p4` và `p4_3e`) |
| `3E3` | ExtRef khóa nhưng Drawing dirty | `Warning` | `Warning` | `DIRTY_DRAWING_UNVERIFIED` chặn promotion dù ExtRef sạch |

### 3.4. Đối Chiếu 55 Drawing Batch (`batch55_shadow` vs `batch55_pilot_iso`)

| Chỉ số | Shadow (Mặc định Production OFF) | Pilot + Isolated Worker | Chênh lệch / Ý nghĩa |
|---|---|---|---|
| Thời gian chạy / Exit code | `86.24s` / Exit `0` | `282.98s` / Exit `0` | Worker chỉ chạy 1 instance dùng lại cho cả lô |
| Phân bố kết quả (`55` dòng) | `OK=11`, `NG=0`, `Warning=44` | `OK=11`, `NG=0`, `Warning=44` | `#37 SA2-fr2` (`Warning -> OK`), `#07 SB1-ks2` (`OK -> Warning`) |
| Policy Code (`50` Flat View) | `50 SHADOW_LEGACY` | `34 DIRECT_PROMOTION_BLOCKED`, `12 LEGACY_CONFIRMED`, `3 DIRTY_DRAWING_UNVERIFIED`, `1 PILOT_DIRTY_ASSEMBLY_DIRECT_VERIFIED` | Phản ánh đúng từng nguyên nhân kỹ thuật |
| Hidden `GetVisibleEntities2` vs Worker | `43 DIRECT_OK`, `7 DIRECT_UNKNOWN` | **`50/50 VISIBLE_ENTITIES2`** (`0` restart, `0` fail) | `7/7` ca từng `UNKNOWN` trong hidden batch đều đọc được `MODEL+FACE` ở worker |
| Kiểm tra biên dạng 2D View | `LIVE/HIDDEN` (`geom2d` PASS) | **`49/50` PASS**, **`1/50` FAIL (`#30 SA2-fr4`)** | `#30 SA2-fr4` lệch chiều cao thật `dH=10.000mm` (`369.00x83.50` vs `369.00x73.50`) |
| Rò rỉ tài liệu SW (`DOCS_AFTER_BATCH`) | `75` | `75` | Không rò rỉ thêm doc chưa đóng |
| File Excel Preview xuất ra | `58,365` bytes (`PR62_BatchPreview_Shadow.xlsx`) | `59,296` bytes (`Batch55_PilotIso_Audit.xlsx`) | Kiểm tra XML Sheet: `sharedStrings=514`, `Fact1MsgCells=15`, `Fact2MsgCells=15`, `DualValCells=11` |

### 3.5. Trạng Thái Tiến Trình SOLIDWORKS & Event Log (Báo Cáo Trung Thực)
- Tiến trình SOLIDWORKS của người dùng mở sẵn từ trước (`PID 48072`) vẫn hoạt động nguyên vẹn suốt quá trình chạy và sau khi hoàn tất.
- Mọi harness kiểm thử (`targeted`, `batch55_shadow`, `batch55_pilot_iso`) đều kết thúc với **Exit `0`**, không treo, không đóng nhầm tài liệu gốc, và Isolated Worker (`PID 33468`) chạy hết `50/50` view không cần restart (`restarted=False` trên cả 50 lượt).
- Tuy nhiên, khi kiểm tra Windows Event Log (`Application`, Event ID 1000), ghi nhận `4` sự kiện exception `0xc0000005` thuộc nội bộ `SLDWORKS.exe 32.5.0.48` của các tiến trình test sở hữu bởi harness (không thuộc `PID 48072`):
  - `10:13:58` (`swccu.dll`, `PID 34112` — teardown cuối phase `p1b`),
  - `10:30:32` (`slduiu.dll`, `PID 49532` — trong giai đoạn khởi động/nạp batch Shadow),
  - `10:32:13` (`slduiu.dll`, `PID 40680` — phiên chính batch Pilot+Iso),
  - `10:33:09` (`sldapiu.dll`, `PID 33468` — Isolated Worker đang chạy).
  Cả ba tiến trình `49532`, `40680`, `33468` đều không bị chấm dứt giữa chừng (tiếp tục trả lời COM đầy đủ 55 bản vẽ tới cuối batch và thoát ở teardown bình thường), không ghi nhận dialog treo người dùng hay mất dữ liệu.

---

## 4. Kho Tài Liệu Hiện Có Trong `docs/`

Trong repository hiện giữ nguyên các tài liệu liên quan phục vụ đối chiếu và review PR #62:
- `docs/CheckDrawing_Link_Report_Code_For_Anti.txt`
- `docs/CHECK_DRAWING_PROPERTY_LINK_CHANGELOG.md`
- `docs/anti/PR62_ANTIGRAVITY_IMPLEMENTATION_PROMPT.md`
- `docs/anti/PR62_FlatSideIntegration.cs.txt`
- `docs/anti/PR62_FlatSidePilotPolicy.cs.txt`
- [`docs/diagnostics/`](../diagnostics) (`3` file chẩn đoán `20261103`)
- [`docs/reports/CHECK_DRAWING_URA_OMOTE_PR62_REPORT.md`](CHECK_DRAWING_URA_OMOTE_PR62_REPORT.md) (báo cáo này)

---

## 5. Điểm Mở Nghiệp Vụ Cần User / ChatGPT Chốt Trước Khi Bật Production

Về mặt kỹ thuật, **Isolated Visible Worker + 2D Geom + Policy tách 2 Fact** đã giải quyết trọn vẹn phần đọc mặt trên Drawing View (`50/50` view lấy được `VISIBLE_ENTITIES2`, `49/50` khớp hình học 2D, phát hiện đúng `1/50` ca lệch kích thước `#30 SA2-fr4`).

Điểm duy nhất còn chờ quyết định nghiệp vụ là **chính sách cho `34` Part sạch Drawing nhưng có In-Context External Reference chưa khóa (`status=3`) trỏ tới Assembly đang dirty (`DIRECT_PROMOTION_BLOCKED`)**:
1. **Giữ nguyên fail-closed hiện tại (khuyến nghị an toàn mặc định):** Coi Fact 1 đã chứng minh `表/ウラ` khớp trên Drawing View & 2D Geom, nhưng vẫn giữ Fact 2 = `WARNING` cho tới khi kỹ sư Save các Assembly bẩn hoặc khóa External Reference (`status=4`).
2. **Hoặc mở ngoại lệ có điều kiện (cần xác nhận quy trình xưởng):** Chấp nhận kết quả kiểm tra mặt phẳng khi `Isolated Visible + 2D Geom` đều PASS dù có `status=3` tới Assembly bẩn (vẫn giữ chặn `DIRTY_DRAWING_UNVERIFIED` và `geom2d=FAIL`).

> Hiện tại toàn bộ feature flag trong commit đều **tắt mặc định (`false`)** để không ảnh hưởng production cho đến khi chốt phương án trên và ChatGPT hoàn tất review PR #62 (chưa merge).
