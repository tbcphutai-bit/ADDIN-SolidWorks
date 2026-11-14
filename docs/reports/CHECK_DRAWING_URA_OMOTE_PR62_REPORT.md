# CHECK DRAWING: BÁO CÁO HOÀN THIỆN & KIỂM THỬ THỰC TẾ 表/ウラ (PR #62)

- **Repository:** `tbcphutai-bit/ADDIN-SolidWorks`
- **PR:** `#62` (`https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/62` — **KHÔNG MERGE**)
- **Branch:** `fix/check-drawing-goban-display-retry-uraomote-20261108`
- **Base Commit (bản gốc PR):** `5d19539269c610a4363c095e72cf042a32f58a93` (cùng tài liệu tích hợp tại `f182754`)
- **Commit mã nguồn hoàn chỉnh:** `a8cec4f35b2c836d31dfe70b3b50a6a4eb92285f` (`a8cec4f`) *(báo cáo này được commit ngay sau trên cùng branch PR #62)*
- **Môi trường kiểm thử:** SOLIDWORKS 2024 SP5.0 (`SLDWORKS.exe 32.5.0.48`), `.NET Framework 4.8`, cấu hình `Debug|x64`
- **Cờ an toàn mặc định trên production:**
  - `CdEnableDirectFlatSideVerdict = false`
  - `CdEnableReviewedFlatSidePilot = false`
  - `CdReviewedFlatSidePilotOptInForTestsOnly = false`
- **Phạm vi sửa đổi (strict CHECK DRAWING ONLY):**
  - `ADDIN.csproj`
  - `Commands/CdFlatSidePilotPolicy.cs` *(mới)*
  - `Commands/CheckDrawingBom.cs`
  - `docs/reports/CHECK_DRAWING_URA_OMOTE_PR62_REPORT.md` *(báo cáo này)*
  - **Không thay đổi:** `CheckUraOmote.cs`, `CheckPartWorkingCopy.cs`, Check Same Part, Check Hole, DXF, Xuất Excel, hay bất kỳ command nào khác. Không ghi đè `C:\Tao moi\ADDIN.dll` (người dùng tự rebuild thủ công); đã đồng bộ cả 3 file nguồn trên sang `C:\Tao moi\ADDIN\`.

---

## 1. Tóm tắt kiến trúc & mã nguồn đã tích hợp (`a8cec4f`)

Đã tích hợp bộ giải pháp fail-closed từ `docs/anti/PR62_FlatSidePilotPolicy.cs.txt`, `docs/anti/PR62_FlatSideIntegration.cs.txt` và `docs/anti/PR62_ANTIGRAVITY_IMPLEMENTATION_PROMPT.md`, đồng thời sửa các lỗ hổng phát hiện khi kiểm thử thực tế trên SOLIDWORKS 2024 SP5.0:

### 1.1 `Commands/CdFlatSidePilotPolicy.cs`
- Tách logic quyết định thuần (COM-free, có unit self-test độc lập):
  - Enum `CdFlatSideChoice` (`UseWorkingCopy`, `UseDirectRead`, `Unknown`)
  - Class `CdFlatSideProof`: 7 điều kiện chứng minh độc lập theo chuỗi (`DrawingPathVerified`, `ReferencedPartPathVerified`, `ConfigurationVerified`, `DisplayStateVerified`, `FlatPatternBodyVerified`, `FaceColorCompleteAndUnambiguous`, `SourceFreshnessVerified`) $\rightarrow$ `IsComplete`
  - Class `CdFlatSidePolicyInput`, `CdFlatSidePolicyDecision`
  - Method `CdFlatSidePilotPolicy.Evaluate`:
    1. **`SHADOW_LEGACY`**: `PilotEnabled == false` giữ nguyên hoàn toàn hành vi Working Copy hiện hành.
    2. **`WC_BOTH_SIDES_PINK_AMBIGUOUS`**: nếu `WorkingCopyFrontPinkMm2 > 1e-6 && WorkingCopyBackPinkMm2 > 1e-6`, trả `Unknown` (`Warning`) — **không bao giờ** tự kết luận `ウラ` như hàm phân loại cũ khi cả hai mặt đều có màu hồng.
    3. **`DIRECT_WORKCOPY_DISAGREE`**: nếu cả Direct Read và Working Copy đều xác định được nhưng ngược cực, trả `Unknown` (`Warning`).
    4. **`LEGACY_CONFIRMED`**: khi Working Copy xác định được và không mâu thuẫn/không hai mặt hồng, giữ kết quả Working Copy.
    5. **`PILOT_DIRTY_ASSEMBLY_DIRECT_VERIFIED`**: **chỉ** cho phép Direct Read thay thế Working Copy khi `PilotEnabled == true` **VÀ** `WorkingCopyBlockedSpecificallyByDirtyAssembly == true` **VÀ** `DirectProof.IsComplete == true` (`7/7` điều kiện chứng minh đều `true`).
    6. **`DIRECT_PROMOTION_BLOCKED`**: mọi ca thiếu bằng chứng độc lập đều giữ nguyên `Warning` / kết quả Working Copy.
  - `RunSelfTests()`: 13 assertion đơn vị bao phủ Shadow OFF, controlled dirty-assembly pilot, stale source, unrelated WC block, clean agreement, disagree, both-sides-pink (`1B`, `1D`), two unknowns, part dirty, drawing dirty, unverified body, incomplete color.

### 1.2 `Commands/CheckDrawingBom.cs`
1. **Giữ Pilot TẮT mặc định + Opt-in nội bộ cho test:**
   - `CdEnableDirectFlatSideVerdict = false`, `CdEnableReviewedFlatSidePilot = false`, `internal static bool CdReviewedFlatSidePilotOptInForTestsOnly = false`, `CdIsFlatSidePilotEnabled()`.
2. **Sửa lỗi gốc rễ kế thừa màu Display State tầng `Body` / `Feature` trên SOLIDWORKS 2024 SP5.0:**
   - **Phát hiện thực nghiệm bằng COM probe:** trên SOLIDWORKS 2024 SP5.0, `ISldWorks.IsSame(viewBody, dsBody)` trả `0` (`swObjectNotSame`) khi so `Body2` từ `GetVisibleEntities2` với `Body2` từ `GetDisplayStateBodyProperties` (kể cả khi `activeConfigSame=False` lẫn `True`); tương tự `Feature` từ `face.GetFeature()` (`ﾌﾗｯﾄ-ﾊﾟﾀｰﾝ92`) khác đối tượng `Feature` trả về từ `GetDisplayStateFeatureProperties` (`ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1`), trong khi `Face2.IsSame` trên các mặt thuộc `Body2` / `Feature` đó lại khớp (`= 1`). Lỗi này trước đây làm override cấp `Body`/`Feature` bị trượt xuống `MODEL`.
   - **Cách sửa:** thêm cache `GetOwnedFaces()` vào `CdAppearanceEntry`, trích helper `CdEntryMatches(CdAppearanceEntry entry, object target, Face2 ownerFace, bool isFaceLevel)`, và cập nhật `CdResolveAppearanceLevel` để khớp theo `CdSameObject` **hoặc** `Face2.IsSame(ownerFace, f)` đối với entry cấp `Body2` / `Feature`.
3. **Phân loại block & chứng minh độ tươi độc lập (`CdClassifyWorkingCopyBlock`, `CdReadExternalReferences`, `CdBuildCandidateFlatSideProof`):**
   - Chỉ gắn `WorkingCopyBlockedSpecificallyByDirtyAssembly` khi đích danh `CheckPartWorkingCopy` báo lỗi assembly dirty (`Model tham chieu co thay doi chua luu:` trỏ `.SLDASM`), **không** bật khi Part dirty (`Part co thay doi chua luu`), Drawing dirty, Drawing lỗi mở/thiếu tham chiếu hoặc View out-of-date.
   - `FlatPatternBodyVerified` chỉ `true` khi `Probe.BodyResolutionMethod == "VISIBLE_ENTITIES2"` (bản vẽ ẩn dùng `HIDDEN_DS_FACE_BODY` chỉ được phép ghi log chẩn đoán, **bị chặn** thăng hạng trong Pilot).
   - `SourceFreshnessVerified` kiểm tra đầy đủ: `Visible`, `FlatPatternBodyVerified`, `!DrawingDirty`, `!PartDirty`, `!CfgDirty`, `!CfgNeedsRebuild`, `PartNeedsRebuild2 == 0`, `!ViewOutOfDate`, `!ViewDisableAutoUpdate`, `!PartIsLightweight`, và mọi in-context external reference trong `ListExternalFileReferences2` nếu không **Locked** (`status == 1`) đều không được tham chiếu tới assembly đang dirty. Không gán cứng `= true`.
4. **Thay thế `AddFlatPatternSideChecks`:** tích hợp trực tiếp `CdEvaluateFlatSidePilot`, log chi tiết `[CD PROVENANCE]`, `[CD PILOT DECISION]`, `[DIRECT_WORKCOPY_DISAGREE]`, `[FLAT SIDE NOTE]`, xử lý đúng multi-view / missing marker / unattached marker thành `Warning`.

---

## 2. Điều tra các trường hợp chênh lệch (`1B_TR11_rn1`, `1D_4332a_P1_l1`) & 37 WARNING

### 2.1 Nguyên nhân 37 WARNING trong bộ 55 Drawing (`4365-01-TI.SLDDRW`)
- Khi mở `4365-01-TI.SLDDRW` và chạy `LoadBom()`, SOLIDWORKS nạp **`11` assembly**, trong đó **`8` assembly bị đánh dấu `dirty=True`** sẵn ngay ở `BATCH_START`:
  - `4365-01-TI.SLDASM`, `4365-02-TI-UK1.SLDASM`, `4365-03-TI-SB1.SLDASM`, `4365-03-TI-SB2.SLDASM`, `4365-03-TI-SB3.SLDASM`, `4365-02-TI-UK2.SLDASM`, `4365-03-TI-FB1.SLDASM`, `4365-03-TI-SA1.SLDASM`.
- Trong **55 dòng BOM**:
  - `5` chi tiết không có view phẳng/lệnh kiểm tra `表/ウラ` (`NO_FLAT_FIELD=5`: dòng `51..55`).
  - `50` bản vẽ có flat view: `13` ca `WC=OK` (`11` ca `ITEM=OK` + `2` ca `4365-04-TI-siU1`, `4365-04-TI-siL1` bị `ITEM=Warning` do cảnh báo ở mục BOM khác), `0` ca `WC=NG`, và **`37` ca `WC=WARNING`** (`35` ca `DIRTY_ASSEMBLY_ONLY` + **`2` ca `DIRTY_DRAWING`** là dòng `18` `4365-04-TI-SB1-fr3` và dòng `28` `4365-04-TI-UK2-l2`).
  - Toàn bộ `37/37` ca `WC=WARNING` giữ `dirty` assembly y nguyên từ `BATCH_START` đến `BATCH_END`; không gây rò rỉ `.SLDDRW` và không làm dirty thêm part nào.

### 2.2 Giải mã `1B_TR11_rn1` (`4365-04-TI-TR11-rn1.slddrw`)
- **Direct Read + Render PNG (`4365-04-TI-TR11-rn1_shaded.png`):** Mặt phẳng chính nằm trong khung bản vẽ (`図面ﾋﾞｭｰ13`, nhãn `表`) hiển thị **màu vàng** (`MODEL rgb=1.00,1.00,0.50`, `frontPinkMm2=0.0`) và mặt sau (`z=-1.00`) sơn **hồng** (`FACE rgb=1.00,0.75,0.75`, `backPinkMm2=42239.0`) theo Display State `表示状態-18` (`SPEC_RM[0]=Face(-1.00) pink`, `SPEC_RM[1]=Model yellow`), kết luận `isUra=False` (`表`).
- **Working Copy (`dsaudit_stdout.log`):** Khi đọc `face.GetMaterialPropertyValues2(swThisConfiguration)` / `swSpecifyConfiguration` sau khi kích hoạt cấu hình `SM-FLAT-PATTERN` trên bản Working Copy, **cả hai mặt** (`z=+1.00` diện tích `42109.7 mm²` và `z=-1.00` diện tích `42239.0 mm²`) đều trả RGB hồng `(1.00, 0.7529, 0.7529)`. Hàm cũ thấy `front > 0` liền trả `true` (`ウラ`).
- **Hành vi mới với Pilot Policy:** Bị chặn ngay bởi **`WC_BOTH_SIDES_PINK_AMBIGUOUS`** (`Working Copy painted both sides; cannot certify polarity.`) $\rightarrow$ trả **`Warning` (`Unknown`)**, không mù quáng báo `ウラ` NG giả và không tự ý ghi đè theo Direct Read.

### 2.3 Giải mã `1D_4332a_P1_l1` (`4332-04-TI-P-1-l1.slddrw`)
- **Kiểm tra trực quan (`4332-04-TI-P-1-l1_shaded.png`) & BOM:** Trên cùng sheet `4379-68` có **`3` view phẳng** (`図面ﾋﾞｭｰ13` trong khung vẽ màu xanh ngọc `cyan` nhãn `表`; `図面ﾋﾞｭｰ17` ngoài khung phía trên màu hồng; `図面ﾋﾞｭｰ16` nhìn cạnh màu xanh ngọc). Ở cấp bản vẽ, `CheckOneDrawing` đã trả `Warning` (`Chữ 表 không gắn được với một view 展開 duy nhất.`).
- **Chẩn đoán từng view:** Direct Read phân biệt đúng từng view (`図面ﾋﾞｭｰ13 = 表 (cyan)`, `図面ﾋﾞｭｰ16 = UNKNOWN`, `図面ﾋﾞｭｰ17 = ウラ (pink)`), trong khi Working Copy `face.GetMaterialPropertyValues2` đọc cả hai mặt trên `図面ﾋﾞｭｰ13` và `図面ﾋﾞｭｰ17` là hồng `(51480.4 / 51480.4 mm²)` vì chỉ đọc face-level material trong Display State mà không áp override `Model cyan (0.50,1.00,1.00)` trong `表示状態-2`. Pilot Policy đánh dấu đây là **hai mặt hồng không xác định** (`WC_BOTH_SIDES_PINK_AMBIGUOUS`), giữ nguyên `Warning`.

---

## 3. Kết quả Build, Self-Test & Kiểm thử Runtime trên bản build cuối (`ADDIN.dll` SHA-256 `4AFF60B8...`)

- **Build (`Debug|x64`):** `MSBuild C:\Tao moi\ADDIN-CheckDrawing\ADDIN.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /p:OutputPath=bin\x64\Debug\ /p:PostBuildEvent=` $\rightarrow$ **`0 errors`** (`C:\Tao moi\ADDIN-CheckDrawing\bin\x64\Debug\ADDIN.dll`, SHA-256 `4AFF60B8F50BD4B31F94D5AC99D35AC3F995743B51C22E79692C359081B3B029`).
- **Đồng bộ mã nguồn sang `C:\Tao moi\ADDIN\`:** Trùng SHA-256 cả 3 file (`CheckDrawingBom.cs`: `D260CDA9...`, `CdFlatSidePilotPolicy.cs`: `596E0AFC...`, `ADDIN.csproj`: `5F9EDE15...`). `C:\Tao moi\ADDIN.dll` giữ nguyên không ghi đè (`20BB502B...`).
- **Unit Self-Tests (`run_selftest.ps1`):** Exit code **`0`** — toàn bộ đều pass:
  - `RunGobanLinkSelfTests PASSED ALL 29 CASES!`
  - `NormalizeFlatSideMarker` & `CompareFlatSideMarker` passed
  - `DEFAULT_FLAGS: CdEnableDirectFlatSideVerdict=False CdEnableReviewedFlatSidePilot=False CdReviewedFlatSidePilotOptInForTestsOnly=False`
  - `CdFlatSidePilotPolicy.RunSelfTests passed 13 synthetic policy assertions!`
  - `RunExcelAndPreviewSyncSelfTests passed!` & `RunWidthLengthSelfTests passed!`

### 3.1 Targeted Suite trên SOLIDWORKS 2024 SP5.0 (`targeted_v8_stdout.log` & `p4_3e_v8_stdout.log`)

> **Lưu ý về độ ổn định teardown của harness:** Khi chạy trên bản vẽ ẩn bằng `CloseAllDocsSafe` (gọi `QuitDoc` cho Drawing rồi `QuitDoc` cho Part/Assembly liên tiếp về 0 tài liệu trong `SLDWORKS.exe` chạy ngầm), SOLIDWORKS 2024 SP5.0 phát sinh crash native nội bộ (`0xc0000374` tại `ntdll.dll` / `0xc0000005` tại `sldapiu.dll` ở các lần chạy thử đầu); còn `p1` dùng `CloseAllDocsSafe` có 1 event lỗi `SLDMODU.dll` (`0xc0000005` lúc `18:11:30`, process vẫn hoàn tất `EXIT=0`). Khi harness chuyển các ca đọc đơn sang `closeAllAfter: false` (chỉ đóng Drawing bằng `QuitDoc` giống `RunBatch`) và tách riêng từng bản sao `SB1-fr8` theo phase, toàn bộ `p1b`, `p2_1e`..`p2_1g2`, `p3_2a`..`p3_2d`, `p4`, `p4_3e` chạy xong với **`EXIT=0` và `0` Application Error (`EventCount=0`)**. Quá trình chạy Batch 55 dòng (110 lượt mở Drawing ở Shadow + Pilot) có **`0` crash**. `6/6` file gốc trong `D:\CG1-ISOLATED-TEST-COPIES` giữ nguyên checksum SHA-256 (`unchanged=True`).

| Ca kiểm thử | Kịch bản & cấu hình | `wcBlock` / `Fresh` | Direct Read (`sources`, Front / Back Pink) | Kết quả cuối (`FlatFieldStatus` / Decision Code) |
| :--- | :--- | :--- | :--- | :--- |
| **`1A` (Shadow)** | `4365-04-TI-SB1-fr8.slddrw` (sạch, `表`) | `NOT_BLOCKED` / `True` | `False` (`MODEL+FACE`, `0.0 / 113191.8`) | **`OK`** (`SHADOW_LEGACY`) |
| **`1A` (Pilot)** | `4365-04-TI-SB1-fr8.slddrw` (sạch, `表`) | `NOT_BLOCKED` / `True` | `False` (`MODEL+FACE`, `0.0 / 113191.8`) | **`OK`** (`LEGACY_CONFIRMED`) |
| **`1B` (Shadow)** | `4365-04-TI-TR11-rn1.slddrw` (`flip=True`, `表`) | `NOT_BLOCKED` / `True` | `False` (`MODEL+FACE`, `0.0 / 42239.0`) | **`NG`** (`SHADOW_LEGACY` — WC đọc sai cả 2 mặt hồng `42109.7 / 42239.0`) |
| **`1B` (Pilot)** | `4365-04-TI-TR11-rn1.slddrw` (`flip=True`, `表`) | `NOT_BLOCKED` / `True` | `False` (`MODEL+FACE`, `0.0 / 42239.0`) | **`Warning`** (**`WC_BOTH_SIDES_PINK_AMBIGUOUS`** — chặn cả hai mặt hồng) |
| **`1C` (Shadow & Pilot)** | `4365-04-TI-FB1-fr3.slddrw` (không sơn hồng) | `DIRTY_DRAWING` / `False` | `UNKNOWN` (`no unambiguous one-sided pink paint`) | **`Warning`** (`SHADOW_LEGACY` / `DIRECT_PROMOTION_BLOCKED`) |
| **`1D` (Shadow & Pilot)** | `4332-04-TI-P-1-l1.slddrw` (3 view `13/16/17`) | Multi-view sheet warning | View `13`: `False`; View `16`: `UNKNOWN`; View `17`: `True` | **`Warning`** (`Chữ 表 không gắn được với một view 展開 duy nhất`) |
| **`1E` (Shadow)** | Clone `SB1-fr8` sơn hồng cả 2 mặt | `NOT_BLOCKED` / `True` | `UNKNOWN` (`113191.8 / 113191.8`) | **`NG`** (`SHADOW_LEGACY` — WC kết luận sai `ウラ`) |
| **`1E` (Pilot)** | Clone `SB1-fr8` sơn hồng cả 2 mặt | `NOT_BLOCKED` / `True` | `UNKNOWN` (`113191.8 / 113191.8`) | **`Warning`** (**`WC_BOTH_SIDES_PINK_AMBIGUOUS`** — được bảo vệ) |
| **`1F`** | Override **Body** cyan `(0.20,0.80,1.00)` + mặt sau hồng (`activeConfigSame=False`) | `NOT_BLOCKED` / `True` | `False` (**`BODY+FACE`**, `frontSrc=BODY`, `0.0 / 113191.8`) | **`OK`** (`LEGACY_CONFIRMED`) |
| **`1F2`** | Override **Body** cyan + mặt sau hồng (`activeConfigSame=True`) | `NOT_BLOCKED` / `True` | `False` (**`BODY+FACE`**, `frontSrc=BODY`, `0.0 / 113191.8`) | **`OK`** (`LEGACY_CONFIRMED`) |
| **`1G`** | Override **Feature** cyan `(0.20,0.80,1.00)` + mặt sau hồng (`activeConfigSame=False`) | `NOT_BLOCKED` / `True` | `False` (**`FEATURE+FACE`**, `frontSrc=FEATURE`, `0.0 / 113191.8`) | **`OK`** (`LEGACY_CONFIRMED`) |
| **`1G2`** | Override **Feature** cyan + mặt sau hồng (`activeConfigSame=True`) | `NOT_BLOCKED` / `True` | `False` (**`FEATURE+FACE`**, `frontSrc=FEATURE`, `0.0 / 113191.8`) | **`OK`** (`LEGACY_CONFIRMED`) |
| **`2A`** | Lật thật `IView.FlipView=True` + Save (`flip=True`, nhãn `表`) | `NOT_BLOCKED` / `True` | `True` (`FACE+MODEL`, `113191.8 / 0.0`) | **`NG`** (`LEGACY_CONFIRMED` — Direct & WC cùng phát hiện `ウラ` $\neq$ `表`) |
| **`2B`** | Đổi thật màu 2 mặt trên Part (`ウラ`), nhãn giữ `表` | `NOT_BLOCKED` / `True` | `True` (`FACE+MODEL`, `113191.8 / 0.0`) | **`NG`** (`LEGACY_CONFIRMED`) |
| **`2C`** | Đổi thật màu 2 mặt trên Part (`ウラ`) + nhãn `ウラ` | `NOT_BLOCKED` / `True` | `True` (`FACE+MODEL`, `113191.8 / 0.0`) | **`OK`** (`LEGACY_CONFIRMED` — `ウラ` đúng) |
| **`2D`** | Hình học `表` chuẩn + đảo nhãn thành `ウラ` | `NOT_BLOCKED` / `True` | `False` (`MODEL+FACE`, `0.0 / 113191.8`) | **`NG`** (`LEGACY_CONFIRMED`) |
| **`3A`** | Assembly `4365-03-TI-SB1.SLDASM` `dirty=True`, ext refs **chưa khóa** (`extStatuses=[3]`, `20/20`), Part sạch | `DIRTY_ASSEMBLY_ONLY` / **`False`** | `False` (`MODEL+FACE`, `0.0 / 113191.8`) | **`Warning`** (`DIRECT_PROMOTION_BLOCKED`; `asmDirty=True->True`, `readOnly=False`) |
| **`3B`** | Assembly `dirty=True` + sơn hồng cả 2 mặt Part trong RAM không Save | `DIRTY_PART` / **`False`** | `UNKNOWN` (`113191.8 / 113191.8`) | **`Warning`** (`DIRECT_PROMOTION_BLOCKED`) |
| **`3C`** | Assembly `dirty=True` + sửa Part anh em `SB1-rn1` chưa Save | `DIRTY_ASSEMBLY_ONLY` / **`False`** | `False` (`0.0 / 113191.8`) | **`Warning`** (`DIRECT_PROMOTION_BLOCKED`, `non_locked_ext_refs=20/20`) |
| **`3D1`** | Assembly đóng; chỉ **Part** `dirty=True` | `DIRTY_PART` / **`False`** | `False` (`0.0 / 113191.8`) | **`Warning`** (`DIRECT_PROMOTION_BLOCKED`) |
| **`3D2`** | Assembly đóng; Part sạch; **Drawing** `dirty=True` | `NOT_BLOCKED` / **`False`** | `False` (`0.0 / 113191.8`) | Direct proof `Fresh=False` (`UNVERIFIED: drawing_dirty`), UI giữ WC sạch trên đĩa (`LEGACY_CONFIRMED`) |
| **`3E1`** *(lưu ý)* | `SB1-fr8` khóa ext ref (`extStatuses=[1]`), Assembly `dirty=True`, Drawing mở qua `DocumentVisible(false)` nhưng mở 1 mình nên SW vẫn nạp `VISIBLE_ENTITIES2` (`vis=1/1`) | `DIRTY_ASSEMBLY_ONLY` / **`True`** | `False` (`VISIBLE_ENTITIES2`, `0.0 / 113191.8`) | **`OK`** (`PILOT_DIRTY_ASSEMBLY_DIRECT_VERIFIED`) — ghi chú: trường hợp `HIDDEN_DS_FACE_BODY -> Warning` được kiểm chứng trực tiếp qua Batch 55 bên dưới |
| **`3E2`** | `SB1-fr8` khóa ext ref (`extStatuses=[1]`, `20/20`), Part/Drawing sạch (`partDirty=False`, `drawDirty=False`), Assembly `dirty=True`, view hiển thị (`VISIBLE_ENTITIES2`, `vis=1/1`) | `DIRTY_ASSEMBLY_ONLY` / **`True`** (`VERIFIED_CLEAN_VISIBLE_AND_NO_DIRTY_INCONTEXT_EXTREF`) | `False` (`VISIBLE_ENTITIES2`, `MODEL+FACE`, `0.0 / 113191.8`) | **`OK`** (**`PILOT_DIRTY_ASSEMBLY_DIRECT_VERIFIED`** — cứu hộ có kiểm soát thành công) |
| **`3E3`** | Như `3E2` nhưng làm **Drawing** `dirty=True` (`SetSaveFlag`) | `DIRTY_DRAWING` / **`False`** (`UNVERIFIED: drawing_dirty`) | `False` (`0.0 / 113191.8`) | **`Warning`** (**`DIRECT_PROMOTION_BLOCKED`** — giữ chốt an toàn khi bản vẽ dirty) |

### 3.2 Bộ 55 Drawing BOM Batch (`batch55_shadow_v3_stdout.log` vs `batch55_pilot_v3_stdout.log`)

- **Thời gian thực thi:** Shadow OFF **`74.89` giây** / Pilot ON **`74.89` giây** (`canceled=False`, `0` crash, `appErrors=0`).
- **Tổng hợp kết quả:**
  - **Shadow OFF (`batch55`):** `55/55 dòng BOM: 11 OK, 0 NG, 44 Warning.` (`WC_OK=13`, `WC_NG=0`, `WC_WARNING=37`, `NO_FLAT_FIELD=5`; Decision: `50/50 SHADOW_LEGACY`; Direct: `43 DIRECT_OK`, `7 DIRECT_UNKNOWN`, `DISAGREE=0`).
  - **Pilot ON (`batch55_pilot`):** `55/55 dòng BOM: 11 OK, 0 NG, 44 Warning.` (`0` dòng bị thăng hạng sai từ `Warning` lên `OK`; Decision: **`13 LEGACY_CONFIRMED`**, **`37 DIRECT_PROMOTION_BLOCKED`**).
- **Vì sao `37` dòng WARNING trong Batch 55 giữ nguyên `DIRECT_PROMOTION_BLOCKED` khi bật Pilot ON?**
  1. Ở chế độ batch ẩn (`strictHiddenBatch = true`), cả 50 bản vẽ đều trả `GetVisibleEntities2` rỗng (`43` dùng `HIDDEN_DS_FACE_BODY`, `7` `NONE`), nên **`FlatPatternBodyVerified = false`** (không thỏa chứng minh hình học view hiển thị trực tiếp).
  2. Ngoài ra, **`34/35` ca `DIRTY_ASSEMBLY_ONLY`** trong batch còn có `non_locked_ext_refs_to_dirty_asm` (`1/1` đến `20/20`, status không Locked `[1]`) và **`2` ca** (`SB1-fr3` dòng 18, `UK2-l2` dòng 28) có `drawDirty=True`. Chính sách Pilot đã **từ chối thăng hạng giả**, đúng yêu cầu fail-closed.
- **Preview & Xuất Excel + Toàn vẹn phiên SOLIDWORKS:**
  - Xuất Excel thành công cả 2 lần (`PR62_BatchPreview_Shadow.xlsx`: `58,340` bytes; `PR62_BatchPreview_Pilot.xlsx`: `58,384` bytes).
  - `DOCS_AFTER_BATCH = 75` ở cả Shadow và Pilot (`0` bản vẽ `.SLDDRW` rò rỉ mở sót); `SOURCE_FILES_UNCHANGED checked=20 allEqual=True`.

#### Bảng đối chiếu đầy đủ 55/55 dòng (Shadow v3 vs Pilot v3 trên `4AFF60B8...`)

| # | Component | `ITEM` (Shadow = Pilot) | `WC` (`表/ウラ`) | `DIRECT` (chẩn đoán) | `wcBlock` & Lý do chưa chứng minh tươi trong Batch ẩn |
| :---: | :--- | :---: | :---: | :---: | :--- |
| 01 | `4365-04-TI-SB1-ks1` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=2/2` |
| 02 | `4365-04-TI-SB1-ks3` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=4/4` |
| 03 | `4365-04-TI-FB1-ks3` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=9/9` |
| 04 | `4365-04-TI-SB2-ks1` | OK | OK | `DIRECT_OK` | `NOT_BLOCKED` (`LEGACY_CONFIRMED`) |
| 05 | `4365-04-TI-SB2-ks3` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=4/4` |
| 06 | `4365-04-TI-SB3-ks3` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=4/4` |
| 07 | `4365-04-TI-SB1-ks2` | OK | OK | `DIRECT_OK` | `NOT_BLOCKED` (`LEGACY_CONFIRMED`; `drawing_dirty`) |
| 08 | `4365-04-TI-T1-ft1` | OK | OK | `DIRECT_OK` | `NOT_BLOCKED` (`LEGACY_CONFIRMED`) |
| 09 | `4365-04-TI-SB1-rn1` | OK | OK | `DIRECT_OK` | `NOT_BLOCKED` (`LEGACY_CONFIRMED`) |
| 10 | `4365-04-TI-TR1-u1` | OK | OK | `DIRECT_OK` | `NOT_BLOCKED` (`LEGACY_CONFIRMED`) |
| 11 | `4365-04-TI-SB1-rn2` | OK | OK | `DIRECT_OK` | `NOT_BLOCKED` (`LEGACY_CONFIRMED`) |
| 12 | `4365-04-TI-siU1` | Warning | OK | `DIRECT_OK` | `NOT_BLOCKED` (`LEGACY_CONFIRMED`; dòng BOM Warning ở mục khác) |
| 13 | `4365-04-TI-siL1` | Warning | OK | `DIRECT_OK` | `NOT_BLOCKED` (`LEGACY_CONFIRMED`; dòng BOM Warning ở mục khác) |
| 14 | `4365-04-TI-SB1-fr8` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=20/20` |
| 15 | `4365-04-TI-SB1-fr1` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=3/3` |
| 16 | `4365-04-TI-SB1-fr2` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=3/3` |
| 17 | `4365-04-TI-FB1-fr2` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=4/4` |
| 18 | `4365-04-TI-SB1-fr3` | Warning | Warning | `DIRECT_OK` | `DIRTY_DRAWING` \| `drawing_dirty`; `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=9/9` |
| 19 | `4365-04-TI-SA1-fr1` | OK | OK | `DIRECT_UNKNOWN` | `NOT_BLOCKED` (`LEGACY_CONFIRMED`; hidden view không có explicit DS face) |
| 20 | `4365-04-TI-SB1-fr2a` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=2/2` |
| 21 | `4365-04-TI-FB1-fr2a` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=3/3` |
| 22 | `4365-04-TI-SB1-fr5` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=5/5` |
| 23 | `4365-04-TI-SB1-fr4` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=8/8` |
| 24 | `4365-04-TI-FB1-fr4` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=9/9` |
| 25 | `4365-04-TI-FB1-fr4b` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=9/9` |
| 26 | `4365-04-TI-UK1-l1` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=1/1` |
| 27 | `4365-04-TI-UK1-l2` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=1/1` |
| 28 | `4365-04-TI-UK2-l2` | Warning | Warning | `DIRECT_OK` | `DIRTY_DRAWING` \| `drawing_dirty`; `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=2/2` |
| 29 | `4365-04-TI-SB1-fr6` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=6/6` |
| 30 | `4365-04-TI-SA2-fr4` | Warning | Warning | `DIRECT_UNKNOWN` | `DIRTY_ASSEMBLY_ONLY` \| `NONE`; `non_locked_ext_refs=1/1` |
| 31 | `4365-04-TI-SA2-fr5` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=1/1` |
| 32 | `4365-04-TI-SA1-fr4` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=3/3` |
| 33 | `4365-04-TI-SA1-fr2` | Warning | Warning | `DIRECT_UNKNOWN` | `DIRTY_ASSEMBLY_ONLY` \| `NONE`; `non_locked_ext_refs=4/4` |
| 34 | `4365-04-TI-SA1-fr5` | Warning | Warning | `DIRECT_UNKNOWN` | `DIRTY_ASSEMBLY_ONLY` \| `NONE`; `non_locked_ext_refs=1/1` |
| 35 | `4365-04-TI-SA1-fr3` | Warning | Warning | `DIRECT_UNKNOWN` | `DIRTY_ASSEMBLY_ONLY` \| `NONE`; `non_locked_ext_refs=1/1` |
| 36 | `4365-04-TI-SA2-fr1` | OK | OK | `DIRECT_UNKNOWN` | `NOT_BLOCKED` (`LEGACY_CONFIRMED`; hidden view không có explicit DS face) |
| 37 | `4365-04-TI-SA2-fr2` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY` (chỉ thiếu `VISIBLE_ENTITIES2` trong batch ẩn) |
| 38 | `4365-04-TI-SA1-fr7` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=1/1` |
| 39 | `4365-04-TI-SA1-fr6` | OK | OK | `DIRECT_OK` | `NOT_BLOCKED` (`LEGACY_CONFIRMED`) |
| 40 | `4365-04-TI-SB1-fr7` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=4/4` |
| 41 | `4365-04-TI-SB2-fr2` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=4/4` |
| 42 | `4365-04-TI-SB3-fr1` | Warning | Warning | `DIRECT_UNKNOWN` | `DIRTY_ASSEMBLY_ONLY` \| `NONE`; `non_locked_ext_refs=4/4` |
| 43 | `4365-04-TI-SB2-fr4` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=7/7` |
| 44 | `4365-04-TI-SB3-fr4` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=7/7` |
| 45 | `4365-04-TI-SB1-fr4a` | OK | OK | `DIRECT_OK` | `NOT_BLOCKED` (`LEGACY_CONFIRMED`) |
| 46 | `4365-04-TI-FB1-fr4a` | OK | OK | `DIRECT_OK` | `NOT_BLOCKED` (`LEGACY_CONFIRMED`) |
| 47 | `4365-04-TI-SB2-fr2a` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=5/5` |
| 48 | `4365-04-TI-SB3-fr1a` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=5/5` |
| 49 | `4365-04-TI-SB2-fr3` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=4/4` |
| 50 | `4365-04-TI-SB2-fr1` | Warning | Warning | `DIRECT_OK` | `DIRTY_ASSEMBLY_ONLY` \| `HIDDEN_DS_FACE_BODY`; `non_locked_ext_refs=2/2` |
| 51..55 | `T1-t1`, `T1-t2`, `SB2-v1`, `TR1-t1`, `FB1-co1` | Warning | `NONE` | `-` | Không phải chi tiết khai triển (`NO_FLAT_FIELD=5`) |

---

## 4. Bảng tổng hợp PASS / FAIL / BLOCKED / NOT TESTED & Khuyến nghị tiếp theo

| Hạng mục | Trạng thái | Ghi chú thực chứng |
| :--- | :---: | :--- |
| Build `Debug\|x64` + Cờ mặc định TẮT (`false`) | **PASS** | `0` errors; `ADDIN.dll` SHA-256 `4AFF60B8...`; 3 cờ đều `false` |
| Unit Self-Tests (`run_selftest.ps1`) | **PASS** | Goban 29 cases + Marker + 13 `CdFlatSidePilotPolicy` + Excel/Preview + W/L đều pass |
| Chặn 2 mặt hồng (`1B`, `1E`, `1D`) & Ngược cực | **PASS** | `WC_BOTH_SIDES_PINK_AMBIGUOUS` trả `Warning`, `0` false OK |
| Kế thừa Display State 4 tầng (`Face/Feature/Body/Model`) | **PASS** | `1F`, `1F2` (`BODY+FACE`), `1G`, `1G2` (`FEATURE+FACE`) đều `OK` cả khi `activeConfigSame=False` & `True` |
| Đảo mặt `FlipView` / đổi màu `ウラ` / đảo nhãn | **PASS** | `2A`, `2B`, `2D` trả `NG`; `2C` trả `OK` |
| Cứu hộ Assembly Dirty có kiểm soát (`3E2`) & Chốt stale (`3A..3D`, `3E3`) | **PASS** | `3E2` trả `PILOT_DIRTY_ASSEMBLY_DIRECT_VERIFIED -> OK`; `3A..3D1`, `3E3` giữ `Warning` |
| Bộ 55 Drawing BOM (`batch55` Shadow & Pilot) + Xuất Excel | **PASS** | 55/55 chạy trong `74.89s`, `0` crash, `20/20` file nguồn nguyên vẹn, Excel OK |
| Mở UI TaskPane tương tác thủ công của người dùng | **NOT TESTED** | Cố ý không đụng phiên SOLIDWORKS đang mở của người dùng (`PID 48072`) và không ghi đè `C:\Tao moi\ADDIN.dll`; chờ người dùng rebuild thủ công từ `C:\Tao moi\ADDIN\` |

### Khuyến nghị bước tiếp theo
1. Người dùng tự mở solution tại `C:\Tao moi\ADDIN\` để rebuild `C:\Tao moi\ADDIN.dll` và kiểm tra khói nhanh trên UI nếu cần.
2. Trên PR #62 (`a8cec4f` + commit báo cáo này), tiếp tục **giữ `CdEnableReviewedFlatSidePilot = false` và `CdEnableDirectFlatSideVerdict = false`** cho bản production hiện tại.
3. Nếu muốn cứu hộ các ca `DIRTY_ASSEMBLY_ONLY` ngay trong **chế độ batch ẩn** ở PR tiếp theo sau khi review xong: cần đánh giá riêng với ChatGPT hai điểm thiết kế là (a) cơ chế giải phóng/xác minh body của drawing mở ẩn cho nhóm `SA1-fr*` (`NONE`) và (b) quy trình khóa external references hoặc điều kiện cho phép đọc Direct với assembly dirty nhưng chi tiết tham chiếu sạch.
