# BÁO CÁO TRIỂN KHAI & KIỂM THỬ PR #62 — CHECK DRAWING (`表/ウラ ↔ 展開`)

**Repository:** `tbcphutai-bit/ADDIN-SolidWorks`  
**Pull Request:** #62 (`fix/check-drawing-goban-display-retry-uraomote-20261108`)  
**Phạm vi sửa đổi:** Chỉ trong `CHECK DRAWING` (`Commands/CheckDrawingBom.cs`, `Commands/CdIsolatedVisibleFlatSideVerifier.cs`, `Commands/CdFlatSidePilotPolicy.cs`, `docs/reports/CHECK_DRAWING_URA_OMOTE_PR62_REPORT.md`). **Không sửa** `ADDIN.csproj`, `CheckUraOmote.cs`, `CheckPartWorkingCopy.cs`, `ExcelDrawingCheckExporter.cs`, `DxfCheckGeometry.cs` hay bất kỳ command nào khác.  
**Trạng thái PR & Cờ Production:** **KHÔNG MERGE PR #62.** Tất cả 5 cờ feature/test giữ nguyên **`false`** mặc định. Không ghi đè live `C:\Tao moi\ADDIN.dll` (trong toàn bộ đợt build và kiểm thử tối 15/11/2026 giữ nguyên `SHA-256: 20BB502B7E8F37B0853F1104CDDFC49A991E266FAA38FE1BF63A8458D4C07CF5`; sáng 16/11/2026 tệp không còn ở `C:\Tao moi\ADDIN.dll`).

---

## 0. Cập nhật hoàn thiện an toàn Isolated Worker (sau commit `34dbf21`)

Tiếp nhận review trên commit `34dbf21` (`issuecomment-6893067875`), toàn bộ cơ chế an toàn cho Isolated Worker đã được gia cố fail-closed mà **không thay đổi thuật toán nhận diện `表/ウラ`**:

### 0.1. Thay đổi mã nguồn (`Commands/CdIsolatedVisibleFlatSideVerifier.cs`, `Commands/CheckDrawingBom.cs`)
1. **`AuditWorkerDocs()` fail-closed tường minh (`CdWorkerDocAuditResult`):**
   - Không còn dùng ngưỡng đơn giản `outsideTemp == 0 && asmCount == 0` bỏ qua lỗi COM. Trả về `AuditSucceeded` dựa trên toàn bộ `GetDocumentCount()`, `GetDocuments()`, kiểm tra `reportedCount == enumeratedCount`, kiểm tra không có phần tử `null`, và đọc thuộc tính (`GetPathName`, `GetTitle`, `GetType`, `GetSaveFlag`, `IsOpenedReadOnly`) không phát sinh lỗi COM (`UNREADABLE_DOC_PROPERTY`).
   - Áp dụng đồng bộ tại `beforeOpen`, `afterOpen`, `afterDelegate` và giai đoạn kiểm kê trước/sau đóng tài liệu.
2. **`CloseAllWorkerDocuments()` kiểm tra quyền sở hữu trước khi đóng & xác nhận chính xác `remainingDocs == 0`:**
   - Rà soát mọi tài liệu đang mở trong worker trước khi gọi `QuitDoc` / `CloseDoc` / `CloseAllDocuments`. Nếu có bất kỳ tài liệu ngoài `itemTempDir`, tài liệu `.SLDASM`, hoặc thuộc tính không đọc được (`UNOWNED_OR_UNEXPECTED_DOC_IN_CLEANUP`), **dừng an toàn ngay lập tức và KHÔNG gọi API đóng lên tài liệu chưa xác minh quyền sở hữu**.
   - Yêu cầu nghiêm ngặt `remainingCount == 0 && enumeratedCount == 0`; mọi giá trị `-1`, `> 0`, hoặc lỗi COM enumerator (`POST_CLOSE_ENUM_FAILED`, `POST_CLOSE_NONZERO_REMAINING`) đều kết luận thất bại.
   - Đưa `VerifyViewOnce` ghi nhận thất bại sau khối `finally`: nếu cleanup thất bại sau khi delegate thành công, kết quả của view vẫn bị ghi đè thành fail-closed.
   - Reset thư mục làm việc (`_baseTempRoot`) trước `TryDeleteDirectory` để tránh tiến trình worker giữ khóa thư mục con vừa đóng.
3. **Cách ly & chấm dứt chỉ đúng Worker PID sở hữu (`QuarantineAndTerminateOwnedWorker`):**
   - Theo dõi PID worker trong `_ownedWorkerPids`. Khi phát hiện lỗi an toàn hoặc dọn dẹp, không dùng chung worker đó nữa (`SafetyViolation = true`, `QuarantinedWorkerPid`), không đóng tài liệu chưa rõ chủ và chỉ kill đúng tiến trình `SLDWORKS` thuộc `_ownedWorkerPids` (tuyệt đối không chạm phiên làm việc của user).
4. **Ép `Warning` khi Isolated Worker lỗi & chuẩn hóa câu chữ `Fact 1` (`Commands/CheckDrawingBom.cs`):**
   - Bổ sung trạng thái probe `IsolatedWorkerAttempted`, `IsolatedWorkerSucceeded`, `IsolatedWorkerSafetyViolation`, `IsolatedWorkerErrorReason`, `IsolatedWorkerQuarantinedPid`.
   - Thêm `CdResolveViewSideOutcome` và `CdResolveAuditDirectPink`: bất cứ khi nào `IsolatedWorkerAttempted && !IsolatedWorkerSucceeded`, ép kết quả trường `表/ウラ ↔ 展開` về **`Warning`** (`ISOLATED_WORKER_SAFETY_FAILED` / `ISOLATED_WORKER_FAILED`), không nâng `OK` theo Pilot và không rơi về `OK` của Working Copy; đồng bộ cột `Drawing Value` (`ActualText`) và `Ghi chú` (`Message`) cùng thể hiện `Direct (ẩn): chưa xác định mặt`.
   - Chuẩn hóa câu chữ `Fact 1` trong `CdBuildSeparatedFlatSideMessage`: thay `"Drawing không sai mặt"` bằng **`"Mặt quan sát trong bản sao Drawing khớp nhãn <expected>; chưa xác minh đồng bộ in-memory của Assembly hiện tại nên giữ WARNING chung"`**. Giữ `Warning` cho toàn bộ 34 ca Part có ExtRef `status=3` trỏ tới Assembly đang dirty.
5. **Tách `ICdWorkerRuntime` & bổ sung Self-test `Case 33` (`RunIsolatedWorkerSafetySelfTests`):**
   - Dùng `FakeCdWorkerRuntimeForSelfTest` tiêm lỗi để kiểm chứng trực tiếp: lỗi COM `beforeOpen` (`0x80010105`), lỗi đọc thuộc tính tài liệu `afterOpen`, lệch số lượng `GetDocumentCount` vs `GetDocuments` `afterDelegate`, nạp nhầm `.SLDASM` ngoài temp, tài liệu ngoài temp trong pha cleanup (xác minh `0` lệnh `QuitDoc`/`CloseDoc`/`CloseAllDocuments` được gọi lên tài liệu ngoài), lỗi enumerator hậu cleanup (`-1`), `remainingDocs == 1`, và ép `Warning` nhất quán trên Preview/Excel dù `Pilot ON/OFF` hay WC `OK`.

### 0.2. Điều tra `QuitDoc` / `CloseDoc` & A/B Cleanup vs Event `0xc0000005` (`diag_ab`)
Thử nghiệm A/B có kiểm soát qua `CdWorkerCleanupMode` (`CleanupModeForTestsOnly`) trên tập 6 bản vẽ cô lập (`SB1-ks1`, `SB1-ks3`, `FB1-ks3`, `SB2-ks1`, `SB2-ks3`, `SA2-fr4`):

| Chế độ Cleanup thử nghiệm (`diag_ab`) | `CLEANUP WARN` | Worker PID | Win32 Event ID `1000` (`0xc0000005`) & Timing millisecond |
| :--- | :---: | :---: | :--- |
| `DefaultQuitThenClose` (`diag_ab_quit.log`) | `0` | — | `0` sự kiện trong lượt 6 file này (nhưng vẫn tái hiện ở batch 50 view bên dưới). |
| `CloseDocFirst` (`diag_ab_closedoc.log`) | `0` | `0x65c8` | `1` sự kiện (`18:00:44`, `sldapiu.dll+0x9a9bb0`) + `2` `Id=1001`. |
| `CloseDocFirst` + ms stage log (`diag_ab_closedoc_ms.log`) | `0` | `50764` (`0xc64c`) | `1` sự kiện **`sldapiu.dll+0x9a9bb0` lúc `18:07:24`**, xảy ra **`~2.94s` SAU khi `AFTER_CLOSE` của `SB1-ks1` đã hoàn tất ở `18:07:21.211` (`elapsedMs=39`, `remainingDocs=0`, `success=True`)** trong lúc không còn file nào mở. |
| `CloseAllOwnedDocs` (`diag_ab_closeall.log`) | `0` | `0xc864` | `1` sự kiện (`18:02:56`, `sldapiu.dll+0x9a9bb0`) + `2` `Id=1001`. |

- **Kết luận điều tra cleanup:** Việc đổi `QuitDoc` sang `CloseDoc` hoặc `CloseAllDocuments(true)` **không loại bỏ được `sldapiu.dll+0x9a9bb0` (`0xc0000005`)** (do ngoại lệ xảy ra nội bộ trong worker SOLIDWORKS kể cả khi mọi tài liệu đã đóng về `remainingDocs=0`). Vì vậy giữ nguyên mặc định `DefaultQuitThenClose` (ít rủi ro hộp thoại lưu file nhất) và kết hợp với hàng rào audit/quarantine fail-closed. Reset CWD về `_baseTempRoot` đã đưa `CLEANUP WARN` khóa thư mục tạm trong các lượt sạch xuống `0`.

### 0.3. Kết quả build & kiểm thử mới nhất (Debug x64, bản build cuối sau chỉnh sửa cuối cùng)
- **Build `ADDIN.csproj` (`/p:Configuration=Debug /p:Platform=x64 /p:OutputPath="bin\x64\Debug\"`):** `0` errors; Output `C:\Tao moi\ADDIN-CheckDrawing\bin\x64\Debug\ADDIN.dll` (`SHA-256: BBE4C85855EBB050134B0A48962AE3D4405E0751143487AFB06134C031268BE6`).
- **Self-tests (`run_selftest.ps1`):** **Exit `0`** — PASS `RunGobanLinkSelfTests` (`Cases 1..33`, bao gồm `Case 33_ISOLATED_WORKER_SAFETY_AUDIT_CLEANUP_GUARDS` mới), `NormalizeFlatSideMarker`, `CompareFlatSideMarker`, `DEFAULT_FLAGS` (`5/5 = False`), `CdFlatSidePilotPolicy` (`15` assertions), `RunExcelAndPreviewSyncSelfTests`, `RunWidthLengthSelfTests`.
- **Batch 55 trên bản sao cô lập `D:\CG1-ISOLATED-TEST-COPIES\4365`:**
  - **`batch55` Shadow (cả 5 cờ OFF, `batch55_shadow_final2.log`):** `exit=0`, `99.4s`, Main SW `PID 2512`, `DOCS_BEFORE=0 -> DOCS_AFTER=75`, `SOURCE_FILES_UNCHANGED=20/20`, `SUMMARY_FLAT total=55 WC_OK=13 WC_NG=0 WC_WARNING=37 NO_FLAT_FIELD=5 DIRECT_OK=43 DIRECT_UNKNOWN=7 DISAGREE=0`, `50 SHADOW_LEGACY`, kết quả tổng hợp (`[DRAWING RESULT]`) **`OK=11 / NG=0 / Warning=44`**, Excel export `59,480 bytes`, `Fact 1=74`, `"Drawing không sai mặt"=0`. Win32 Event ID `1000`: `1` sự kiện (`slduiu.dll+0x1184d4a` trong main SW `PID 2512`).
  - **`batch55_pilot_iso` (opt-in Pilot + Isolated cho test, `batch55_pilot_iso_final2.log`):** `exit=0`, `322.4s`, Main SW `PID 33276`, `DOCS_BEFORE=0 -> DOCS_AFTER=75`, `SOURCE_FILES_UNCHANGED=20/20`, `0` thư mục `ADDIN_CdIso_*` còn sót sau test.
    - `SUMMARY_FLAT`: `total=55 WC_OK=13 WC_NG=0 WC_WARNING=37 NO_FLAT_FIELD=5 DIRECT_OK=43 DIRECT_UNKNOWN=7 DISAGREE=0`; phân loại Pilot: `34 DIRECT_PROMOTION_BLOCKED`, `12 LEGACY_CONFIRMED`, `3 DIRTY_DRAWING_UNVERIFIED`, `1 PILOT_DIRTY_ASSEMBLY_DIRECT_VERIFIED`. Kết quả tổng hợp (`[DRAWING RESULT]`) **`OK=11 / NG=0 / Warning=44`**, Excel export `59,990 bytes`; `"Mặt quan sát trong bản sao Drawing khớp nhãn"=36`, `"Drawing không sai mặt"=0`.
    - **Hành vi fail-closed thực tế trong runtime (`ROW 38 | 4365-04-TI-SA1-fr7`):** Tổng `50` view cần Isolated Worker (`2` lần khởi tạo worker: `PID 8960` rồi `PID 47500`). Worker `PID 8960` đã hoàn tất `37` view đầu và kiểm tra thành công `afterDelegate` cho `#38 SA1-fr7`, nhưng ở bước đóng tài liệu hậu kiểm tra đã gặp lỗi RPC **`GetDocumentCount_ERR: RPC サーバーを利用できません。 (0x800706BA)`** (`remainingDocs=-1`). Hệ thống lập tức **quarantine `PID 8960` (`ISOLATED_WORKER_SAFETY_FAILED`)**, **không dùng lại worker lỗi**, mở worker mới `PID 47500` hoàn tất `12` view còn lại (`49/50 ISO OK`, `49/50 2D BBOX MATCH`, `rt_auditOk=201`, `rt_auditFail=0`, `rt_remZero=49`, `rt_remFail=1`), đồng thời ép riêng `#38 SA1-fr7` sang **`Warning`** và đồng bộ cả `VAL` (`Fact 1=Direct (ẩn): chưa xác định mặt (chưa đủ dữ liệu view ẩn)`) lẫn cột `Ghi chú`. *(Ghi chú: file Excel đối chiếu XML trực tiếp `Batch55_PilotIso_Audit.xlsx` `60,001 bytes` được xuất ở lượt ngay trước bước đồng bộ `CdResolveAuditDirectPink`; trên bản build cuối `final2`, `EXCEL_EXPORT_TEST` tạo thành công `59,990 bytes` và log `ROW 38` xác nhận đồng bộ `Fact 1` đúng chuẩn.)*
    - **Win32 Event ID `1000` (`0xc0000005`):** Ghi nhận **`3` sự kiện** trong `batch55_pilot_iso`: `1` sự kiện ở main SW `PID 33276` (`18:27:07`, `slduiu.dll+0x1184d4a`) và `2` sự kiện trên worker (`18:27:57` ở `PID 8960` và `18:31:52` ở `PID 47500`, đều là `sldapiu.dll+0x9a9bb0`). **Không tuyên bố đã loại bỏ crash native.**
- **`Pr62Verify.exe targeted` (`1A..3E3`) trên bản build cuối:** Dừng ở ca `1C_FB1_fr3_NOPINK_SHADOW` do SW harness `PID 33548` gặp ngoại lệ heap native `ntdll.dll+0x112fc5 (0xc0000374)` -> `RPC 0x800706BA` tại `FindOpenDoc` của harness (`exit=1`; trước đó `1A` và `1B` hoạt động đúng, sau đó một lượt chạy lại bị treo do máy sleep qua đêm và đã được dọn sạch). Bộ ma trận `1A..3E3` đầy đủ nhất trước khi tách runtime Audit/Cleanup thuộc lượt `targeted` ở commit `34dbf21` (Mục 3.2 bên dưới).

> Phần Mục 1–5 bên dưới giữ nguyên hồ sơ kiểm thử từ các mốc `9f0e28c` / `34dbf21`.

---

## 1. Tóm tắt thay đổi trong đợt hoàn thiện sau review `9f0e28c`

Tiếp nhận quyết định nghiệp vụ và các yêu cầu rà soát từ review của ChatGPT trên commit `9f0e28c`, các điểm sau đã được hoàn thiện và kiểm chứng đồng bộ:

1. **Chuẩn hóa tách bạch Fact 1 / Fact 2 và từ ngữ `2D BBOX` trên Preview & Excel (`Commands/CheckDrawingBom.cs`):**
   - Với các part có External Reference In-Context chưa khóa (`status=3`) trỏ tới Assembly đang dirty (`34` ca trong Batch 55) và các bản vẽ tự thân chưa lưu (`3` ca `DIRTY_DRAWING_UNVERIFIED`), **giữ nguyên kết quả chung `Warning`** (fail-closed, không tự nâng `OK`, không tự khóa ExtRef, không Save/Rebuild Part hay Assembly gốc).
   - Chuẩn hóa `DrawingValue` (cột `Drawing` trên Preview & Excel `CHI TIẾT KIỂM TRA`) và `Message` (cột `Thông báo` / hộp cảnh báo) để không khiến kỹ sư hiểu nhầm mặt bản vẽ bị sai:
     - **`[Fact 1 - Mặt 表/ウラ quan sát được]`**: Nêu rõ nguồn quan sát (`Direct (ẩn) / LIVE_HIDDEN` hoặc `Bản sao Drawing / ISOLATED_VISIBLE_COPY`), mặt quan sát so với nhãn trên view (`khớp nhãn 表`), và kết quả **`2D BBOX MATCH dW=.../dH=...`** kèm chú thích tường minh `(chỉ kiểm Bounding Box W/H, không khẳng định toàn bộ biên dạng/lỗ)`. Khi mặt quan sát khớp nhãn, ghi rõ: `Mặt quan sát khớp nhãn 表 (Drawing không sai mặt; giữ WARNING chung do đồng bộ Model/Assembly)`; khi view ẩn chưa đủ dữ liệu, ghi rõ `(không khẳng định Drawing bị sai mặt)`.
     - **`[Fact 2 - Đồng bộ Drawing/Part/Assembly]`**: Nêu rõ nguyên nhân `WARNING` (`SHADOW_LEGACY`, `DIRECT_PROMOTION_BLOCKED` kèm danh sách Assembly dirty của ExtRef `status=3`, hoặc `DIRTY_DRAWING_UNVERIFIED` kèm `GetSaveFlag=true`), đồng thời **cảnh báo provenance tường minh**: `"Worker chỉ copy bản sao Drawing+Part, không copy Assembly nên chưa chứng minh đồng bộ in-memory của Assembly."`
   - Loại bỏ hoàn toàn cụm từ cũ dễ gây hiểu nhầm (`"2D Geom PASS"` / `"2D Geom LỆCH"`); toàn bộ Preview, Excel và log sử dụng nhất quán **`2D BBOX MATCH` / `2D BBOX MISMATCH`** (`(chỉ kiểm Bounding Box W/H)`).

2. **Giải thích gốc rễ & sửa triệt để lệch `dH = 10.000 mm` ở `#30 4365-04-TI-SA2-fr4` (`Commands/CheckDrawingBom.cs`):**
   - **Nguyên nhân gốc rễ:** Không phải lỗi hình học CAD trên `#30 4365-04-TI-SA2-fr4`. Trong `Commands/DxfCheckGeometry.cs` (dòng 31), `DxfGeometry.Circle` lưu `e.Bounds` theo bounding box của **cả đường tròn đầy đủ** (`center.X ± radius`, `center.Y ± radius`) kể cả khi đối tượng là cung tròn không khép kín, trong khi `e.Evaluate` (dòng 28) tham số hóa đúng theo `start + sweep * t`. Bản vẽ `#30 4365-04-TI-SA2-fr4` có 1 cung tròn bán kính `10 mm` (`arcs=1`, nửa đường tròn), làm `e.Bounds` tính dư `10.000 mm` theo phương Y (`83.50 mm` thay vì `73.50 mm`).
   - **Cách sửa (giữ nguyên `DxfCheckGeometry.cs`):** Bổ sung helper nội bộ `CdAccumulate2DPoint` và `CdAccumulateTightEdgeBounds` trong `Commands/CheckDrawingBom.cs` để tính bounding box khít cho từng `DxfEdge` dựa trên điểm đầu/cuối, các góc cực trị `k * pi/2` qua `e.At(t)` và lấy mẫu dày (`32`/`64` điểm), thay thế việc đọc `e.Bounds` trong `CdVerifyView2DGeometryMatchesBody`.
   - **Kết quả kiểm chứng:** Self-test `Case 31` (`SA2-fr4` semicircular arc + negative sweep) đạt PASS, và trong run thực tế `batch55_pilot_iso`, `#30 4365-04-TI-SA2-fr4` đạt **`bbox2d=MATCH(0.000/0.000,arcs=1)`**, đưa tỷ lệ **`2D BBOX MATCH`** toàn lô lên **`50/50`** (`dW=0.000 mm`, `dH=0.000 mm` ở mọi view).

3. **Gia cố định danh ROT, kiểm kê tài liệu mở và cleanup trong Isolated Worker (`Commands/CdIsolatedVisibleFlatSideVerifier.cs`):**
   - **`TryGetAppFromRot(string expectedName, int expectedPid)`:** Bắt buộc khớp chính xác normalized moniker (`(name ?? "").Trim().TrimStart('!')`) **và** xác minh `sw.GetProcessID() == expectedPid` (từ chối và giải phóng COM nếu không khớp) trước khi gắn vào tiến trình worker.
   - **`AuditWorkerDocs` (`[CD ISO VERIFIER DOC AUDIT]`):** Kiểm kê toàn bộ `_workerApp.GetDocuments()` tại `beforeOpen`, `afterOpen`, `afterDelegate` (và `afterCloseNonZero` nếu còn sót tài liệu). Tự động fail-closed (`ISOLATED_UNEXPECTED_DOC_LOADED_AFTER_OPEN` / `AFTER_DELEGATE`) nếu có bất kỳ tài liệu nào nằm ngoài `itemTempDir` (`outsideTemp > 0`) hoặc bất kỳ Assembly nào (`asmCount > 0`) bị nạp ngầm vào worker.
   - **Stage timing & Teardown:** Ghi log timestamp + `elapsedMs` theo từng chặng (`BEFORE_REPOINT`, `BEFORE_OPENDOC`, `AFTER_OPENDOC`, `BEFORE_DELEGATE`, `AFTER_DELEGATE`, `BEFORE_CLOSE`, `AFTER_CLOSE`); `CloseAllWorkerDocuments()` đóng Drawing trước, rồi Assembly, rồi Part bằng `QuitDoc` (fallback `CloseDoc`); `TryDeleteDirectory` ghi `[CD ISO VERIFIER CLEANUP WARN]` nếu OS tạm khóa thư mục con vừa đóng, và `Dispose()` dọn sạch `_sessionTempRoot` sau khi worker thoát.

---

## 2. Xác nhận trạng thái các cờ an toàn Production

Kiểm tra trực tiếp lúc nạp assembly mới (`run_selftest.ps1`) xác nhận cả 5 cờ đều mặc định `False`:
- `CdEnableDirectFlatSideVerdict = False`
- `CdEnableReviewedFlatSidePilot = False`
- `CdReviewedFlatSidePilotOptInForTestsOnly = False`
- `CdEnableIsolatedVisibleVerification = False`
- `CdIsolatedVisibleVerificationOptInForTestsOnly = False`

---

## 3. Kết quả build & kiểm thử (SOLIDWORKS 2024 SP5.0, Debug x64)

- **Build mục tiêu:** `C:\Tao moi\ADDIN-CheckDrawing\ADDIN.csproj` (`Debug|x64`, Output `C:\Tao moi\ADDIN-CheckDrawing\bin\x64\Debug\ADDIN.dll`, SHA-256 `B24B0DECF76E7C02C496307514FCFF84D4C6E9F39292A05FDBC18DD5459B4148`, `0` errors).
- **Bảo toàn môi trường user:** `C:\Tao moi\ADDIN.dll` giữ nguyên SHA-256 `20BB502B7E8F37B0853F1104CDDFC49A991E266FAA38FE1BF63A8458D4C07CF5`; tiến trình SOLIDWORKS của user (`PID 48072`, `StartTime 2026/11/13 18:11:08`) hoàn toàn nguyên vẹn; `0` thư mục `ADDIN_CdIso_*` và `0` tiến trình SOLIDWORKS test còn sót lại sau chạy kiểm thử.

### 3.1. Self-tests (sau lần sửa code cuối cùng)
- **`run_selftest.ps1`:** Exit `0`
  - `RunGobanLinkSelfTests`: PASS `29/29` case + toàn bộ `RunUraOmoteSelfTests` gồm **Case 31** (tight BBox cung tròn `SA2-fr4` và sweep âm) và **Case 32** (câu chữ `Fact 1`, `Fact 2`, `2D BBOX MATCH`, và kiểm 3 hằng số production flag `= False`).
  - `NormalizeFlatSideMarker` & `CompareFlatSideMarker`: PASS.
  - `DEFAULT_FLAGS`: cả 5 cờ đều `False`.
  - `CdFlatSidePilotPolicy.RunSelfTests`: PASS `15` assertions.
  - `RunExcelAndPreviewSyncSelfTests` & `RunWidthLengthSelfTests`: PASS.

### 3.2. Targeted SOLIDWORKS Suite (`1A..3E3`)
> *Lưu ý phạm vi bản build:* Lệnh `Pr62Verify.exe targeted` (`1379.4s`, Exit `0`) chạy ngay trước chỉnh sửa 1 dòng cuối ở mục assertion runtime của `Case 32` trong hàm tự kiểm `RunUraOmoteSelfTests` (không thay đổi logic production; bản thân `Case 32` và toàn bộ `RunUraOmoteSelfTests` đã được chạy lại và PASS trên build cuối trong `run_selftest.ps1` và `batch55_pilot_iso`).
- **Tính toàn vẹn nguồn:** `SOURCE_INTEGRITY unchanged=True` (`6/6` phase).
- **Kết quả trường `FlatFieldStatus` (`CheckOneDrawing` đánh dấu `Status=NG` tổng thể ở một số ca đơn lẻ do `hasBom=false` thiếu cột BOM, không phải lỗi `表/ウラ`):**
  - `1A` (`SB1-fr8` chuẩn `表`): `Direct=OK`, `WC=OK`, `FlatFieldStatus=OK`.
  - `1B` (`TR11-rn1`) & `1E` (hai mặt cùng hồng): Shadow `FlatFieldStatus=NG`, Pilot fail-closed giữ `FlatFieldStatus=Warning` (không tự kết luận `ウラ`).
  - `1C` (`FB1-fr3` thiếu hồng): `FlatFieldStatus=Warning`.
  - `1F`, `1F2`, `1G`, `1G2` (kế thừa màu `BODY` / `FEATURE` + `FACE`): `FlatFieldStatus=OK`.
  - `2A` (lật view), `2B` (đảo màu thật gắn nhãn `表`), `2D` (mặt `表` gắn nhầm nhãn `ウラ`): `FlatFieldStatus=NG`.
  - `2C` (bản vẽ `ウラ` chuẩn gắn nhãn `ウラ`): `FlatFieldStatus=OK`.
  - `3A` (ExtRef `status=3` tới ASM dirty), `3B` (Part dirty in-memory), `3C` (sibling edit cùng ASM dirty), `3D1` (Part dirty đơn lẻ), `3D2` (Drawing dirty `GetSaveFlag=true`): đều fail-closed `FlatFieldStatus=Warning` (riêng `3A`/`3D2` xuất đúng tách bạch `Fact 1 / Fact 2 / 2D BBOX MATCH`).
  - `3E1` & `3E2` (ExtRef đã khóa `extStatuses=[1]`, Drawing & Part sạch, `LIVE_HIDDEN vis=1/1` & `Fresh=True`): `PILOT_DIRTY_ASSEMBLY_DIRECT_VERIFIED -> OK`.
  - `3E3` (ExtRef khóa `[1]` nhưng `drawDirty=True`): fail-closed `DIRTY_DRAWING_UNVERIFIED -> Warning`.

### 3.3. Batch 55 Drawing (Bản sao cô lập `D:\CG1-ISOLATED-TEST-COPIES\4365`) — Sau lần sửa cuối cùng

| Hạng mục đo | `batch55` Shadow (Tắt Pilot & Worker) | `batch55_pilot_iso` (Opt-in Pilot + Worker cho test) |
| :--- | :---: | :---: |
| Exit code / Thời lượng / Main SW PID | `0` / `101.0s` / `47048` | `0` / `317.5s` / `49876` (Worker `48328`) |
| `DOCS_BEFORE_BATCH` -> `DOCS_AFTER_BATCH` | `0` -> `75` (`SOURCE_FILES_UNCHANGED 20/20`) | `0` -> `75` (`SOURCE_FILES_UNCHANGED 20/20`) |
| Tổng Drawing Excel (`TỔNG HỢP`) | **OK `11` / NG `0` / Warning `44`** (`Tổng field NG=0`) | **OK `11` / NG `0` / Warning `44`** (`Tổng field NG=0`) |
| Trường `表/ウラ ↔ 展開` (Excel cột `Q` / `Flat`) | **OK `13` / NG `0` / Warning `37` / `-` `5`** | **OK `13` / NG `0` / Warning `37` / `-` `5`** |
| `DIRECT_OK` / `DIRECT_UNKNOWN` / `DISAGREE` | `43` / `7` / `0` | `43` / `7` (`LIVE_HIDDEN`) -> **`50/50`** qua worker / `0` |
| Phân loại `PILOT_POLICY_CODE` (`50` flat views) | `50 SHADOW_LEGACY` | `34 DIRECT_PROMOTION_BLOCKED`, `12 LEGACY_CONFIRMED`, `3 DIRTY_DRAWING_UNVERIFIED` (`#07, #18, #28`), `1 PILOT_DIRTY_ASSEMBLY_DIRECT_VERIFIED` (`#37 SA2-fr2`) |
| `ISOLATED_VISIBLE_COPY` & Restart worker | Chưa kích hoạt | **`50/50 OK`**, **`restarted=False` (`0` restart)** |
| `[CD ISO VERIFIER DOC AUDIT]` (`150` lượt kiểm tra) | N/A | **`outsideTemp=0` (`150/150`)**, **`asmCount=0` (`150/150`)** |
| Đối chiếu `2D BBOX` (`CdAccumulateTightEdgeBounds`) | N/A | **`50/50 2D BBOX MATCH`** (`dW=0.000mm`, `dH=0.000mm`, gồm `#30 SA2-fr4 arcs=1`) |
| Kiểm tra XML Excel (`CHI TIẾT KIỂM TRA`, `50` dòng flat) | `Fact 1=37`, `2D BBOX MATCH=0`, `"2D Geom PASS"=0` | `Fact 1=37`, `2D BBOX MATCH=38` (`37 Warning + #37`), `"2D Geom PASS"=0` |
| Windows Event Log ID `1000` (`0xc0000005`) | `1` sự kiện (`slduiu.dll+0x1184d4a`) | `2` sự kiện (`slduiu.dll+0x1184d4a`, `sldapiu.dll+0x9a9bb0`) |

- **Xác minh XML Excel (`Batch55_PilotIso_Audit.xlsx`, `59,676` bytes) & Preview parity:**
  - `#14 4365-04-TI-SB1-fr8`: `Kết quả=Warning`; Cột `Drawing`: `図面ﾋﾞｭｰ13: Nhãn 表 | Fact 1=Bản sao Drawing: mặt màu (表) (khớp nhãn 表, 2D BBOX MATCH) | Fact 2=Đồng bộ WARNING (ExtRef chưa khóa (status=3) tới ASM bẩn: ...)`; Cột `Thông báo`: nêu đầy đủ Fact 1 (`2D BBOX MATCH dW=0.000mm/dH=0.000mm (chỉ kiểm Bounding Box W/H, không khẳng định toàn bộ biên dạng/lỗ)`, `Drawing không sai mặt`) và Fact 2 (`WARNING (DIRECT_PROMOTION_BLOCKED)... Worker chỉ copy bản sao Drawing+Part, không copy Assembly nên chưa chứng minh đồng bộ in-memory của Assembly.`).
  - `#30 4365-04-TI-SA2-fr4`: `Kết quả=Warning`; **`Fact 1`** hiển thị `2D BBOX MATCH dW=0.000mm/dH=0.000mm` thay vì lỗi `10mm` giả do arc `e.Bounds`.
  - `#18 4365-04-TI-SB1-fr3`: `Kết quả=Warning`; **`Fact 2`** báo `WARNING (DIRTY_DRAWING_UNVERIFIED)` (`GetSaveFlag=true, NeedsRebuild2=0, OutOfDateViews=0`), trong khi Fact 1 vẫn ghi nhận bản sao Drawing khớp nhãn `表`.
  - `#37 4365-04-TI-SA2-fr2`: `Kết quả=OK` (`PILOT_DIRTY_ASSEMBLY_DIRECT_VERIFIED`), thông báo ghi rõ `2D BBOX MATCH (chỉ kiểm Bounding Box W/H) dW=0.000mm/dH=0.000mm`.

---

## 4. Phân tích 4 chữ ký Windows Event Log `0xc0000005` & Cleanup Worker

Từ đối chiếu chéo Windows Application Event Log (`Id=1000/1001`) với timestamp trong raw log và `[CD ISO VERIFIER STAGE]`, ghi nhận **4 chữ ký ngoại lệ truy cập bộ nhớ (`0xc0000005`) hoàn toàn khác nhau về nguồn phát sinh**:

| Thời điểm | PID test (Hex) | Tiến trình / Chế độ | Module & Offset | Bối cảnh chính xác trong log |
| :--- | :--- | :--- | :--- | :--- |
| `15:45:13.402` | `22188` (`0x56AC`) | Main hidden SW (`targeted` phase `p3_2d`) | `SLDMODU.dll` + `0x0000000001200e63` | Đóng tài liệu copy của **`CheckPartWorkingCopy`** cũ (`[CHECK COPY] Close: 0x80010105 (RPC_E_SERVERFAULT)` / `Cleanup: ディレクトリが空ではありません`), **không chạy Isolated Worker**. |
| `15:50:45.524` | `22908` (`0x597C`) | Main hidden SW (`targeted` phase `p4`) | `ntdll.dll` + `0x0000000000053809` | Xảy ra **2 giây sau dòng log cuối** của `targeted_p4_raw.log` (`15:50:43`), trong pha `ExitApp` / thoát tiến trình SW ẩn sau khi mở 22 tài liệu Assembly dirty. |
| `15:52:42.294` / `15:55:44.905` / `16:02:05` | `41484` / `49876` / `47048` | Main hidden SW (`batch55` & `batch55_pilot_iso`) | `slduiu.dll` + `0x0000000001184d4a` | Xảy ra **12–13 giây sau khi khởi tạo SW ẩn** (`15:52:30` -> `15:52:42`; `15:55:31` -> `15:55:44`; `16:01:53` -> `16:02:05`), cả khi **tắt hoàn toàn Isolated Worker** (`batch55` Shadow OFF). Do SEH nội bộ SOLIDWORKS bắt lại nên batch vẫn chạy tiếp (`rc=0`). |
| `15:17:49.868` / `15:56:41.508` | `48016` (`diag_events`) / `48328` (`batch55_pilot_iso`) | **Isolated Worker** (`Visible=false`, `DocumentVisible=true`) | `sldapiu.dll` + `0x00000000009a9bb0` | Xảy ra **1 lần duy nhất mỗi phiên worker**, tại bước đầu đọc/đóng view trong worker (`SB1-ks1` ở `diag_events`; `FB1-ks3` ở `batch55_pilot_iso` với delegate kéo dài `14,422 ms` giữa `15:56:26.497` và `15:56:40.919` rồi ghi WER lúc `15:56:41.508` sát `AFTER_DELEGATE`/`BEFORE_CLOSE`). Worker vẫn trả COM tiếp 47 mã sau đó (`restarted=False`, `remainingDocs=0`). |

**Kết luận trung thực về độ ổn định:**  
- Worker đã chứng minh **không nạp bất kỳ `.SLDASM` hoặc file gốc nào** (`150/150` doc audit `outsideTemp=0`, `asmCount=0`), chỉ attach theo exact moniker + PID, luôn `remainingDocs=0` sau mỗi lượt, và xóa sạch thư mục tạm khi `Dispose()` (trên disk không còn `ADDIN_CdIso_*`, mặc dù `TryDeleteDirectory(itemTempDir)` ngay sau `QuitDoc` từng file ghi `[CD ISO VERIFIER CLEANUP WARN]` vì tiến trình worker còn giữ file lock đến khi thoát).
- Tuy nhiên, **chúng tôi không tuyên bố đã loại bỏ crash native**: các sự kiện `0xc0000005` nội bộ của SOLIDWORKS (`slduiu.dll`, `sldapiu.dll`, `SLDMODU.dll`, `ntdll.dll`) vẫn xuất hiện trong Event Log khi tự động hóa COM ẩn/worker. Vì vậy **không bật bất kỳ cờ production nào**.

---

## 5. Giới hạn còn mở & Đề xuất bước tiếp theo

1. **Chưa test TaskPane UI tương tác trực tiếp trên live `C:\Tao moi\ADDIN.dll`:** Toàn bộ kiểm thử chạy qua `Pr62Verify.exe` trên bản sao cô lập (`D:\CG1-ISOLATED-TEST-COPIES\` và `%TEMP%\PR62_Verify_*`); `C:\Tao moi\ADDIN.dll` cố ý không bị ghi đè vì đang được `PID 48072` của user giữ lock. Sau khi team đóng SOLIDWORKS và tự rebuild/cài DLL, cần test một pilot UI nhỏ để chụp screenshot thực tế trên TaskPane.
2. **Độ ổn định native COM:** Cần theo dõi thêm xem `sldapiu.dll+0x9a9bb0` trong worker hay `slduiu.dll+0x1184d4a` trong SW ẩn có giảm khi chạy trong phiên SOLIDWORKS tương tác (`Visible=true`) hoặc khi tắt preview add-in phụ hay không trước khi cân nhắc bật bất kỳ cờ opt-in nào.
3. **2D check hiện tại là `2D BBOX`:** Đối chiếu hiện kiểm W/H khít (`<= 0.35 mm`), không phải so sánh toàn bộ biên dạng 2D hay lỗ/rãnh, và đã được hiển thị đúng như vậy trên UI/Excel.
