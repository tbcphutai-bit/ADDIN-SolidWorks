# CHECK DRAWING: BÁO CÁO TRIỂN KHAI & KIỂM THỬ THỰC TẾ 表/ウラ (PR #62)

- **Repository:** `tbcphutai-bit/ADDIN-SolidWorks`
- **Branch:** `fix/check-drawing-goban-display-retry-uraomote-20261108`
- **PR:** `#62` (**KHÔNG MERGE**)
- **Base Commit (trước bản cập nhật này):** `5d19539269c610a4363c095e72cf042a32f58a93`
- **Commit mã nguồn (`Commands/CheckDrawingBom.cs`):** `834d4b49934831c05e9fe3145ee18fadd78e3943` (`834d4b4`) *(commit báo cáo này nằm ngay sau trên cùng branch PR #62)*
- **Môi trường kiểm thử:** SOLIDWORKS 2024 SP5.0, `.NET Framework 4.8`, cấu hình `Debug|x64`
- **Chốt an toàn:** **`CdEnableDirectFlatSideVerdict = false`** *(giữ chế độ Shadow / chẩn đoán; chưa bật verdict Direct Read ra UI/Excel production)*
- **Phạm vi sửa đổi:** Chỉ cập nhật `Commands/CheckDrawingBom.cs` và báo cáo `docs/reports/CHECK_DRAWING_URA_OMOTE_PR62_REPORT.md`. Không sửa `CheckUraOmote.cs`, `CheckPartWorkingCopy.cs`, W/L, 合番, Quantity, Cut List, Xuất Excel hay các command khác.

---

## 1. Nguyên nhân gốc rễ của 37 WARNING liên quan Assembly Dirty

Kết quả chạy lại bộ BOM 55 dòng (`D:\CG1-ISOLATED-TEST-COPIES\4365\4365-01-TI.SLDDRW`, `RunBatch(..., silent: true)`) tái hiện đúng ảnh chụp màn hình **`55/55 dòng BOM: 11 OK, 0 NG, 44 Warning`** và ghi nhận nguyên nhân trực tiếp qua `[CD ASM SNAPSHOT]` ở `BATCH_START`:

1. Ngay khi mở `4365-01-TI.SLDDRW` và chạy `LoadBom()`, SOLIDWORKS nạp sẵn **`11` assembly** vào bộ nhớ và **tự động đánh dấu `8/11` assembly là `dirty=True`** trước khi `RunBatch` kiểm tra bất kỳ bản vẽ nào:
   - `dirty=True` (`8`): `4365-01-TI.SLDASM`, `4365-02-TI-UK1.SLDASM`, `4365-03-TI-SB1.SLDASM`, `4365-03-TI-SB2.SLDASM`, `4365-03-TI-SB3.SLDASM`, `4365-02-TI-UK2.SLDASM`, `4365-03-TI-FB1.SLDASM`, `4365-03-TI-SA1.SLDASM`
   - `dirty=False` (`3`): `4365-03-TI-TR1.SLDASM`, `4365-02-TI-T1.SLDASM`, `4365-03-TI-SA2.SLDASM`
2. Cơ chế an toàn của `CheckPartWorkingCopy.PrepareReferences()` chặn toàn bộ 37 component/drawing có tham chiếu tới các assembly đang `dirty=True` (`Model tham chieu co thay doi chua luu: ...`), dẫn đến đúng **37 WARNING** tại mục `表/ウラ ↔ 展開` (cộng với `13` OK và `5` chi tiết dạng ống không có trường `表/ウラ ↔ 展開`).
3. Trạng thái dirty của toàn bộ 11 assembly tại `BATCH_END` hoàn toàn trùng khớp với `BATCH_START` (`8 dirty=True / 3 dirty=False`): quá trình chạy batch không làm dirty thêm tài liệu nào, không Save, không Rebuild và không đóng tài liệu của người dùng.

---

## 2. Thay đổi mã nguồn trong `Commands/CheckDrawingBom.cs`

### 2.1 Hoàn thiện kế thừa màu 4 tầng theo Configuration & Display State (`Face > Feature > Body > Model`)
- Đối chiếu chữ ký COM Interop trên SOLIDWORKS 2024 SP5.0 (`IConfiguration`, `IModelDocExtension`, `IView`, `ISldWorks.IsSame`) và chuẩn hóa qua các helper mới:
  - `CdAppearanceEntry`, `CdSameObject`, `CdSameRgb`, `CdTryReadDisplayStateEntries`, `CdResolveAppearanceLevel`
- Phân giải màu cho từng face theo thứ tự ưu tiên **`Face (4) > Feature (3) > Body (2) > Model (1)`**, bị chặn phạm vi chặt vào đúng `(cfg = model.GetConfigurationByName(result.Configuration), result.DisplayState)`:
  - Tầng Face / Feature / Body / Model đọc từ legacy Display State API (`GetDisplayStateFaceProperties`, `GetDisplayStateFeatureProperties`, `GetDisplayStateBodyProperties`, `GetDisplayStateProperties`) và đối chiếu với `RenderMaterial` (`GetRenderMaterialsCount2`, `GetRenderMaterials2` với `swSpecifyDisplayState`).
  - Bảo vệ `activeConfigSame` cho `RenderMaterial`: chỉ đọc `swSpecifyDisplayState` khi `ActiveConfiguration` trùng `view.ReferencedConfiguration`, tránh rò màu nếu Active Config ở trạng thái uốn có Display State trùng tên (ví dụ `表示状態-18`).
  - Nếu có xung đột màu hoặc dữ liệu Display State mơ hồ ở bất kỳ tầng nào, hàm trả về fail-closed (`UNKNOWN`).

### 2.2 Phát hiện & xử lý `GetVisibleEntities2` rỗng khi mở bản vẽ ẩn trong Batch (`RunBatch(..., silent: true)`)
- **Phát hiện thực nghiệm (`hiddenprobe` / `batch55`):**
  - Khi bản vẽ được mở hiển thị (`Visible == true`), `GetVisibleEntities2(null, swViewEntityType_Face)` trả về face đang nhìn trong view (`bodySource=VISIBLE_ENTITIES2`).
  - Khi `RunBatch(..., silent: true)` dùng `OpenDrawingDocumentSilent` (`swApp.DocumentVisible(false, swDocDRAWING)`), SOLIDWORKS bỏ qua bước tessellation hiển thị nên `GetVisibleEntities2(null, swViewEntityType_Face)` trả về **0 thực thể trên toàn bộ 50/50 drawing** ở chế độ chạy ẩn (`LoadModel`, `ActivateView`, `GraphicsRedraw2` không tự nạp lại danh sách này khi drawing vẫn ẩn).
- **Giải pháp fail-closed đã bổ sung:**
  - Thêm `CdIsDrawingHiddenForView`, `CdResolveHiddenFlatBody` và `CdTryFlatSideReadOnlyCore(SolidWorks.Interop.sldworks.View view, ModelDoc2 ownerDrawing)` (giữ nguyên wrapper 1 tham số `CdTryFlatSideReadOnly(view)`).
  - Nhánh `HIDDEN_DS_FACE_BODY` **chỉ kích hoạt** khi `GetVisibleEntities2` rỗng **và** drawing đang mở ẩn (`!ownerDrawing.Visible`) **và** `view.IsModelLoaded() && view.SuppressState == 0`:
    1. Trích xuất `Body2` từ `dsFaceEntries` (của đúng `ReferencedConfiguration` + `DisplayState`), yêu cầu **duy nhất 1 body** (`candidateBodies.Count == 1`).
    2. Nếu `!activeConfigSame`, kiểm tra `Body2` này không được trùng bất kỳ body nào trong `part.GetBodies2(swSolidBody, true)` của active config.
    3. Xác minh hình học phẳng 2 mặt đối xứng theo trục Z view (`ModelToViewTransform`): `posA > 1e-12 && negA > 1e-12`, tỷ lệ mặt phẳng `(posA + negA) / totalA >= 0.60`, cân xứng hai mặt `Math.Min(posA, negA) >= Math.Max(posA, negA) * 0.70`.
  - Nếu `dsFaceEntries` không trỏ tới một body duy nhất (`candidateBodies.Count == 0` hoặc `> 1`), giữ nguyên trạng thái fail-closed `UNKNOWN`.
- **Logging chẩn đoán:** Bổ sung `[DIRECT_WORKCOPY_DISAGREE] shadow ...`, `[FLAT DIRECT BODY] ... bodySource=VISIBLE_ENTITIES2|HIDDEN_DS_FACE_BODY`, `[FLAT DIRECT OK] ... sources=...`.

---

## 3. Kết quả Build, Self-Test & Kiểm thử Runtime trên SOLIDWORKS 2024 SP5.0

### 3.1 Build & Unit Self-Tests
- **MSBuild (`Debug|x64`):** Biên dịch `C:\Tao moi\ADDIN-CheckDrawing\ADDIN.csproj` thành công với `/p:Configuration=Debug /p:Platform=x64 /p:OutputPath=bin\x64\Debug\ /p:PostBuildEvent=` (`0 errors` $\rightarrow$ `C:\Tao moi\ADDIN-CheckDrawing\bin\x64\Debug\ADDIN.dll`).
  - Tuân thủ yêu cầu tự rebuild của người dùng: **không ghi đè `C:\Tao moi\ADDIN.dll`**; đã đồng bộ mã nguồn sang `C:\Tao moi\ADDIN\Commands\CheckDrawingBom.cs` (SHA-256 trùng khớp `BBA8C9FABA3F308BC1D8132173F3C56DD237A4F94642C62FA6985CB599E502A9`).
- **Self-Test (`run_selftest.ps1`):**
  - `SUCCESS: RunGobanLinkSelfTests PASSED ALL 29 CASES!`
  - `SUCCESS: NormalizeFlatSideMarker passed all tests!`
  - `SUCCESS: CompareFlatSideMarker passed all polarity and fail-closed checks!`

### 3.2 Targeted Suite trên SOLIDWORKS 2024 SP5.0 (`Pr62Verify.exe targeted`, exit code `0`, 0 crash)

| Ca kiểm thử | Mô tả thực tế | Config / DisplayState | Nhãn | Direct Read (Front / Back Pink, `sources`) | Working Copy (Front / Back Pink) | Kết quả |
| :--- | :--- | :--- | :---: | :--- | :--- | :--- |
| **`1A_CLEAN_OMOTE_SB1_fr8`** | `4365-04-TI-SB1-fr8.slddrw` bản sạch | `Default<ﾏｼﾝ加工>SM-FLAT-PATTERN` / `表示状態-18` | `表` | `False` (`0.0 / 113191.8 mm²`, `MODEL+FACE`, `VISIBLE_ENTITIES2`) | `False` (`0.0 / 113191.8 mm²`) | Khớp (`OK`) |
| **`1B_TR11_rn1_DISAGREE_CHECK`** | `4365-04-TI-TR11-rn1.slddrw` (`flip=True`) | `Default<ﾏｼﾝ加工>SM-FLAT-PATTERN` / `表示状態-18` | `表` | **`False`** (`0.0 / 42239.0 mm²`, `MODEL+FACE`) $\rightarrow$ `OK` | **`True`** (`42109.7 / 42239.0 mm²`) $\rightarrow$ `NG` | `[DIRECT_WORKCOPY_DISAGREE] shadow` *(Direct đúng `表`; WC sai ở mặt `42109.7`)* |
| **`1C_FB1_fr3_NOPINK_UNKNOWN`** | `4365-04-TI-FB1-fr3.slddrw` không sơn hồng | `Default<ﾏｼﾝ加工>SM-FLAT-PATTERN` / `表示状態-18` | `表` | `UNKNOWN` (`0.0 / 0.0`, `no unambiguous one-sided pink paint`) | `UNKNOWN` (`0.0 / 0.0`) | Khớp (`Warning` fail-closed) |
| **`1D_4332a_P1_l1_DISAGREE_CHECK`** | `4332-04-TI-P-1-l1.slddrw` (3 view: `13`, `16`, `17`) | `ﾃﾞﾌｫﾙﾄSM-FLAT-PATTERN` / `表示状態-2` | `表` | View `13`: **`False`** (`0.0 / 51480.4`, `FACE+MODEL`)<br>View `16`: `UNKNOWN` (`0.0 / 0.0`)<br>View `17`: `True` (`51480.4 / 0.0`, `FACE+MODEL`) | View `13`: **`True`** (`51480.4 / 51480.4`)<br>View `16`: `UNKNOWN`<br>View `17`: `True` (`51480.4 / 51480.4`) | Direct phát hiện rõ view `13` (`表`), `16` (`UNKNOWN`), `17` (`ウラ`). Batch field trả `Warning` (`Chữ 表 không gắn được với một view 展開 duy nhất`) |
| **`2A_SAVED_FLIPVIEW_MARKER_OMOTE`** | Lật thật `IView.FlipView = True` + Rebuild + Save (`err=0 warn=0`) | `Default<ﾏｼﾝ加工>SM-FLAT-PATTERN` / `表示状態-18` | `表` | **`True`** (`113191.8 / 0.0 mm²`, `MODEL+FACE`) $\rightarrow$ **`NG`** | `True` (`113191.8 / 0.0 mm²`) $\rightarrow$ **`NG`** | Phát hiện view đảo mặt **NG** |
| **`2B_REAL_SWAPPED_URA_MARKER_OMOTE_EXPECT_NG`** | Đổi thật màu 2 mặt trên Part (`ウラ`), giữ nhãn `表` | `Default<ﾏｼﾝ加工>SM-FLAT-PATTERN` / `表示状態-18` | `表` | **`True`** (`113191.8 / 0.0 mm²`, `FACE+MODEL`) $\rightarrow$ **`NG`** | `True` (`113191.8 / 0.0 mm²`) $\rightarrow$ **`NG`** | Phát hiện sai cực **NG** |
| **`2C_GENUINE_URA_OK`** | Đổi thật màu 2 mặt trên Part (`ウラ`) + nhãn `ウラ` | `Default<ﾏｼﾝ加工>SM-FLAT-PATTERN` / `表示状態-18` | `ウラ` | **`True`** (`113191.8 / 0.0 mm²`, `FACE+MODEL`) $\rightarrow$ **`OK`** | `True` (`113191.8 / 0.0 mm²`) $\rightarrow$ **`OK`** | Nhận diện đúng **ウラ OK** |
| **`2D_MARKER_INVERTED_TO_URA_EXPECT_NG`** | Hình học `表` chuẩn + đảo nhãn thành `ウラ` | `Default<ﾏｼﾝ加工>SM-FLAT-PATTERN` / `表示状態-18` | `ウラ` | **`False`** (`0.0 / 113191.8 mm²`, `MODEL+FACE`) $\rightarrow$ **`NG`** | `False` (`0.0 / 113191.8 mm²`) $\rightarrow$ **`NG`** | Phát hiện nhãn sai cực **NG** |
| **`3A_DIRTY_ASM_UNCHANGED_GEOMETRY`** | `4365-03-TI-SB1.SLDASM` đặt `dirty=True`, hình học không đổi | `Default<ﾏｼﾝ加工>SM-FLAT-PATTERN` / `表示状態-18` | `表` | **`False`** (`0.0 / 113191.8 mm²`) $\rightarrow$ **`OK`** | `Warning` (`Model tham chieu co thay doi chua luu`) | **Cứu được Warning do Assembly Dirty**; `asmDirty` giữ nguyên `True -> True`, `docCount=22` không đổi |
| **`3B_DIRTY_ASM_PLUS_UNSAVED_INMEM_PART_CHANGE`** | Assembly `dirty=True` + sơn hồng cả 2 mặt Part trong RAM không Save | `Default<ﾏｼﾝ加工>SM-FLAT-PATTERN` / `表示状態-18` | `表` | **`UNKNOWN`** (`113191.8 / 113191.8 mm²`, `no unambiguous one-sided pink paint`) | `Warning` (`Part co thay doi chua luu`) | An toàn fail-closed, không báo false OK |

### 3.3 Bộ 55 Drawing BOM Batch (`D:\CG1-ISOLATED-TEST-COPIES\4365\4365-01-TI.SLDDRW`, `RunBatch(..., silent: true)`)
- **Thời gian thực thi:** **`67.66` giây** cho 55 dòng (`canceled=False`, 0 crash SOLIDWORKS).
- **Tổng hợp kết quả UI hiện tại (chế độ Shadow, quyết định vẫn do Working Copy):**
  - Toàn bộ dòng BOM: **`55/55 dòng BOM: 11 OK, 0 NG, 44 Warning.`**
  - Trường `表/ウラ ↔ 展開`: **`WC_OK=13`**, **`WC_NG=0`**, **`WC_WARNING=37`**, **`NO_FLAT_FIELD=5`** (5 ống/thanh: `4365-04-TI-T1-t1`, `T1-t2`, `SB2-v1`, `TR1-t1`, `FB1-co1`).
- **Kết quả chẩn đoán Direct Read trên 50 Drawing có flat view (độ lệch `DISAGREE=0` với Working Copy ở các ca đọc được cả hai):**
  - **`DIRECT_OK = 43 / 50`** (`bodySource=HIDDEN_DS_FACE_BODY` `43/43`, `VISIBLE_ENTITIES2=0`):
    - **Cứu được `32 / 37` ca đang bị `WC=Warning` do Assembly Dirty** (nhận diện chính xác `isUra=False`, `frontPinkMm2=0`, đo đúng diện tích mặt hồng đối diện cho từng chi tiết như `SB1-fr8 = 113191.8 mm²`, `SB1-ks1 = 1454125.4 mm²`, v.v.).
    - **Khớp `11 / 13` ca `WC=OK`** (`DISAGREE=0`).
  - **`DIRECT_UNKNOWN = 7 / 50`** (tất cả đều dừng fail-closed với lý do `GetVisibleEntities2 empty in hidden view and display-state has no explicit face body`):
    - `2` ca `WC=OK`: Dòng 19 (`4365-04-TI-SA1-fr1`), Dòng 36 (`4365-04-TI-SA2-fr1`).
    - `5` ca `WC=Warning`: Dòng 30 (`4365-04-TI-SA2-fr4`), Dòng 33 (`4365-04-TI-SA1-fr2`), Dòng 34 (`4365-04-TI-SA1-fr5`), Dòng 35 (`4365-04-TI-SA1-fr3`), Dòng 42 (`4365-04-TI-SB3-fr1`).
- **Kiểm tra rò rỉ tài liệu & độ nguyên vẹn phiên SOLIDWORKS sau Batch:**
  - `OPEN_DOCS_AFTER_BATCH count=75`: đúng `1` bản vẽ BOM (`4365-01-TI.SLDDRW`), `11` assembly và `63` part trong `D:\CG1-ISOLATED-TEST-COPIES\4365`. **0 bản vẽ chi tiết (`.SLDDRW`) bị rò rỉ mở sót**, không tự ý đóng tài liệu đang mở, không làm phát sinh dirty mới trên bất kỳ part (`63/63` đều `dirty=False`) hay assembly nào.

#### Trích đoạn log thực tế từ lần chạy mới nhất
```text
--- Batch 55 (67.66s) ---
[CHECK DRAWING BOM] [CD ASM SNAPSHOT] stage=BATCH_START path="D:\CG1-ISOLATED-TEST-COPIES\4365\4365-03-TI-SB1.SLDASM" dirty=True cfgDirty=False readOnly=False
[CHECK DRAWING BOM] [FLAT DIRECT BODY] view=図面ﾋﾞｭｰ13 bodySource=HIDDEN_DS_FACE_BODY visible=0 bodyFaces=20 viewConfig=Default<ﾏｼﾝ加工>SM-FLAT-PATTERN displayState=表示状態-18 activeConfigSame=False
[CHECK DRAWING BOM] [FLAT DIRECT OK] view=図面ﾋﾞｭｰ13 cfg=Default<ﾏｼﾝ加工>SM-FLAT-PATTERN state=表示状態-18 bodySource=HIDDEN_DS_FACE_BODY sources=MODEL+FACE frontPinkMm2=0 backPinkMm2=1454125.38700233 isUra=False
[CHECK DRAWING BOM] [FLAT SIDE NOTE] text=表 owner=図面ﾋﾞｭｰ13 status=Warning message=図面ﾋﾞｭｰ13: chữ 表; [Direct Drawing View / verified config+state] Mặt đang nhìn là mặt màu (表), mặt đối diện có màu hồng. | Working Copy: Không kiểm tra được mặt của view 展開: Model tham chieu co thay doi chua luu: D:\CG1-ISOLATED-TEST-COPIES\4365\4365-03-TI-SB1.SLDASM
SUMMARY_FLAT total=55 WC_OK=13 WC_NG=0 WC_WARNING=37 NO_FLAT_FIELD=5 DIRECT_OK=43 DIRECT_UNKNOWN=7 DISAGREE=0

--- Targeted 1B / 2A / 2C / 3A ---
[CHECK DRAWING BOM] [DIRECT_WORKCOPY_DISAGREE] shadow view=図面ﾋﾞｭｰ13 direct=False fallback=True ...
TRY_FLIP_VIEW 図面ﾋﾞｭｰ13: before=False after=True | TRY_FLIP_VIEW_SAVE saved=True err=0 warn=0
  FLAT_VIEW=図面ﾋﾞｭｰ13 sheet=14 flip=True -> DIRECT: val=True frontPink=113191.8 backPink=0.0 -> CheckMarker('表')=NG
SWAPPED_FLAT_COLORS saved=True ... | SET_MARKER_TEXT 表->ウラ changedCount=1
  FLAT_VIEW=図面ﾋﾞｭｰ13 sheet=14 flip=False -> DIRECT: val=True frontPink=113191.8 backPink=0.0 -> CheckMarker('ウラ')=OK
3A_DIRTY_ASM_UNCHANGED_GEOMETRY -> DIRECT: val=False frontPink=0.0 backPink=113191.8 | WC=Warning | asmDirty=True->True
```

---

## 4. Các trường hợp còn WARNING / UNKNOWN & Giới hạn cần lưu ý

1. **`CdEnableDirectFlatSideVerdict = false` (Shadow Mode):** Toàn bộ 37 ca Working Copy Warning vẫn hiện `Warning` trên bảng kết quả cho tới khi bật cờ quyết định trực tiếp; thông tin Direct Read đang được gắn kèm trong thông điệp chẩn đoán và trace log.
2. **7/50 bản vẽ ẩn cho `DIRECT_UNKNOWN` trong `batch55`:** Do cơ chế view ẩn hiện mới chấp nhận tìm body duy nhất từ explicit `dsFaceEntries` (`HIDDEN_DS_FACE_BODY`) để tránh đoán nhầm body ở active config hoặc đa body. Khi mở bản vẽ hiển thị (`Visible == true`), `GetVisibleEntities2` giải quyết trực tiếp, nhưng ở chế độ batch ẩn, các part không có `dsFaceEntries` (như sơn theo Feature/Body hoặc không sơn) dừng fail-closed.
3. **Bản vẽ chưa sơn / sơn hai mặt / note đa view:** Các bản vẽ không sơn một mặt rõ ràng (`1C`: `FB1-fr3`) hoặc có nhiều view `展開` khiến chữ `表/ウラ` không gắn được với một view duy nhất (`1D`: `4332-04-TI-P-1-l1`) tiếp tục trả `Warning` đúng thiết kế an toàn.
4. **Rủi ro Part chưa lưu trong RAM (Stale Cache):** Dù Direct Read giải quyết rất sạch vấn đề Assembly cha bị `dirty=True` trên disk/RAM, nếu chính **file Part** hoặc **Drawing** có sửa đổi hình học/View chưa Rebuild/chưa Save đồng bộ, đọc trực tiếp trên RAM cần cân nhắc kết hợp kiểm tra dirty ở cấp Part trước khi bật verdict cho production.

---

## 5. Đề xuất phương án tiếp theo

1. Đánh giá kết quả thực nghiệm trong báo cáo này trên PR #62 (không merge trước khi chốt).
2. Khi quyết định bật verdict Direct Read ở bước kế tiếp: ưu tiên giữ chế độ chỉ thay thế Working Copy khi `CheckPartWorkingCopy` bị chặn do **Assembly Dirty** (và Part tham chiếu không `dirty`), đồng thời cân nhắc bổ sung bước xác minh body từ Feature/Body Display State nếu muốn phủ tiếp 7 ca ẩn còn lại (`SA1-fr1`, `SA2-fr1`, v.v.) mà vẫn giữ fail-closed.
