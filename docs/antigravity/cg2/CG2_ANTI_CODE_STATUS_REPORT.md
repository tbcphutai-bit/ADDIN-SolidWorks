# CG2 — ANTIGRAVITY: BÁO CÁO CODE ĐÃ VIẾT / ĐÃ SỬA

> **STATUS: CG2 BREP REVIEW PATCH VERIFICATION EXECUTED — STRICT SOURCE PROTOCOL ENFORCED.**
> Theo quy định mới: **ChatGPT là tác giả 100% code C#.**
> Antigravity **không tự viết hoặc sửa bất kỳ dòng C# nào** (`C_SHARP_EDITED_BY_ANTI=NO`, `ANTI_C_SHARP_CHANGES=0`).
> Đã build Debug|x64 và Release|x64 thành công (0 error, 16 cảnh báo cũ không liên quan).
> Test harness offline `CG2_TEST_HARNESS.cs` đạt **100% PASS (0 failures)** bao gồm slab intersection và synthetic ENTER/EXIT.
> Đã tạo và đo đạc assembly dương tính `Fixture_BREP_Positive.SLDASM` có solid chắn ngang trục lỗ (hits = 2, span = 3.000mm).
> Phát hiện cấu trúc COM API thực tế của `GetRayIntersectionsPoints`: trả về **9 double/hit** thay vì 6 double/hit, dẫn đến phân loại `UNVERIFIED_RAY_TOPOLOGY`. Giữ nguyên mã C# và báo cáo phát hiện kỹ thuật này để ChatGPT xử lý.
> Đã chạy toàn bộ regression 6 fixture trên SOLIDWORKS thực tế (4 PASS, 2 FAIL CSV byte do thứ tự duyệt baseline; toàn bộ counter và phán quyết sản xuất PASS 100%).
> PR #33 giữ nguyên trạng thái **Draft** (`READY_FOR_MERGE=NO`), không merge vào main và không bắt đầu CG3.

---

## 1. Phạm vi và lịch sử Git

| Field | Value |
|---|---|
| Repository | `tbcphutai-bit/ADDIN-SolidWorks` |
| Branch | `feat/check-hole-cg2-part-relations` |
| PR | https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33 |
| TESTED_REMOTE_SHA | `327d1026505411a7db1f9c2122afa1f658883435` |
| Current Git working tree status | Clean; committed files changed: 2 docs (`CG2_REPORT.md`, `CG2_ANTI_CODE_STATUS_REPORT.md`) |
| Git diff changed files | `docs/antigravity/cg2/CG2_REPORT.md`, `docs/antigravity/cg2/CG2_ANTI_CODE_STATUS_REPORT.md` |
| Việc được giao | CG2 BREP Review Patch execution, build, offline tests, positive/negative CAD testing, regression, reporting |
| Quy định tác giả code | ChatGPT tác giả 100% C#; Anti chỉ áp dụng và chạy kiểm thử |
| Tổng file C# sửa trong vòng này | **0** (`ANTI_C_SHARP_CHANGES=0`) |
| Tổng file docs sửa trong vòng này | **2** (`CG2_REPORT.md`, `CG2_ANTI_CODE_STATUS_REPORT.md`) |

---

## 2. Danh sách code CG2 ĐÃ CÓ (Kiểm tra đối chiếu thực tế)

| File / method | Functionality đã thấy trong code | Tác giả đoạn sửa cuối | Đã sửa trong vòng này? | Build / Test evidence |
|---|---|---|---|---|
| `Commands/CheckAssemblyHole.MaterialEvidence.cs` | `Cg2PrepareBoundedRay` (slab intersection, outside margins), `Cg2ClassifyRayPointData`, `EnrichCg2BrepMaterialEvidence`, `Cg2LoadTargetBodyContext` | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS; RUNTIME_TESTED trên SOLIDWORKS; đo được 187 `BREP_NO_AXIS_HIT`, 1,136 `UNVERIFIED` |
| `Commands/CheckAssemblyHole.PartRelations.cs` | Added material fields `MaterialEvidence`, `MaterialSpanMm`, `MaterialHitCount` và 5 BREP counters vào report | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS (MSBuild x64 0 errors); reflection audit PASS |
| `Commands/CheckAssemblyHole.cs :: Run` | Thêm 1 lệnh gọi `EnrichCg2BrepMaterialEvidence` sau AABB screening, bọc non-fatal `try/catch` | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS; RUNTIME_TESTED trên 6 Assembly sandbox; không ảnh hưởng CG1/NG/markers |
| `Commands/CheckHoleResultsForm.cs :: ShowPartRelations` | Hiển thị counts BREP có vật liệu/không giao trục/chưa xác minh, disclaimer header cập nhật | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS; Header disclaimer test PASS |
| `ADDIN.csproj` | Khai báo compile entry `CheckAssemblyHole.MaterialEvidence.cs` | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS (MSBuild 18.10.1 build sạch 0 error) |
| `docs/antigravity/cg2/CG2_TEST_HARNESS.cs` | Sửa disclaimer assertion, bổ sung 4 test ray-slab và 2 test synthetic ENTER/EXIT | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS; **100% PASS (0 FAILURES)** |
| `docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs` | Isolated CAD runner, `VerifyCsvParity`, `ValidateCg2ExpectedCandidates` | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS; `--self-test` PASS; 6 fixture regression executed (4 PASS, 2 CSV_BYTE_PARITY_FAIL do traversal non-determinism) |
| `docs/antigravity/cg2/CG2_REVIEW3_REPEATABILITY.cs` | Repeatability runner (CG1↔CG1, CG2↔CG2, CG1↔CG2) trên `1618-01-BH.SLDASM` | ChatGPT (`327d102`) | **NO** (Anti không sửa C#) | BUILD_PASS; RUNTIME_EXECUTED; STRICT_BYTE_PARITY=FAIL; NORMALIZED_COUNTERS=PASS; EXIT=1 |

---

## 3. CODE DO ANTIGRAVITY TỰ VIẾT / SỬA TRONG VÒNG NÀY

| File | Class.Method / function | Line range | BEFORE behavior | AFTER behavior | Author | Commit SHA |
|---|---|---|---|---|---|---|
| **NONE** | **NONE** (Không có file C# nào bị sửa trong vòng này) | - | - | - | - | - |

### Tuân thủ quy định mới:
- `C_SHARP_EDITED_BY_ANTI=NO`
- `ANTI_C_SHARP_CHANGES=0`
- `APPLIED_CHATGPT_CODE=YES` (Tất cả code C# do ChatGPT commit trực tiếp tại `327d102`)
- `NO_DUPLICATE_CLASSES_OR_METHODS=YES` (Không copy trùng code)
- `NO_PRODUCTION_CHANGES=YES` (Production Check Hole engine verdicts, NG criteria, CSV fields không đổi)

---

## 4. PHÁT HIỆN KỸ THUẬT QUAN TRỌNG TRONG SOLIDWORKS COM API

### Phát hiện: Cấu trúc mảng trả về của `ModelDoc2.GetRayIntersectionsPoints()`
- **Vị trí code:** `Commands/CheckAssemblyHole.MaterialEvidence.cs`, dòng 417 và 442–446.
- **Hành vi quan sát được:**
  - Khi gọi `ModelDocExtension.RayIntersections` trên SOLIDWORKS 2024 SP5.0 với cờ `swRayPtsOptsENTRY_EXIT`, API trả về số điểm giao `hits = 2`.
  - Mảng doubles trả về từ `model.GetRayIntersectionsPoints()` có độ dài là **18 phần tử** (tương ứng **9 double cho mỗi điểm giao**, KHÔNG PHẢI 6 double):
    `[bodyIndex, rayIndex, flags, x, y, z, nx, ny, nz]`
    Trong đó 3 phần tử cuối là vector pháp tuyến bề mặt `(nx, ny, nz)` tại điểm giao cắt.
  - Cụ thể trên fixture dương tính `Fixture_BREP_Positive.SLDASM` (tấm kim loại 3mm chắn ngang trục):
    - Điểm giao 0: `[0]=0, [1]=0, [2]=17 (ENTER|FACE), [3]=3.432, [4]=0.000, [5]=-1.094, [6]=0, [7]=-1, [8]=0`
    - Điểm giao 1: `[9]=0, [10]=0, [11]=33 (EXIT|FACE), [12]=3.432, [13]=0.003, [14]=-1.094, [15]=0, [16]=1, [17]=0`
  - Do code hiện tại trong `CheckAssemblyHole.MaterialEvidence.cs` dùng bước nhảy `int offset = i * 6`, khi duyệt đến `i = 1`, `offset = 6`, code đọc:
    - `body = points[6] = 0`
    - `ray = points[7] = -1` (đọc nhầm pháp tuyến `ny` thành `rayIndex`)
    - Dòng 446 kiểm tra: `if (body < 0 || body >= bodyCount || ray != 0) return "UNVERIFIED_RAY_TOPOLOGY";`
    - Do `ray == -1 != 0`, hàm trả về `"UNVERIFIED_RAY_TOPOLOGY"`.
  - Khi phân tích với bước nhảy 9 double (`offset = i * 9`):
    - Tia đi vào tại `Y = 0.000` và thoát ra tại `Y = 0.003`.
    - Độ dày solid đo được là `3.000 mm` (>= 0.1 mm), khẳng định `BREP_SOLID_ON_AXIS`!
- **Hành động của Anti:** Tuân thủ quy định nghiêm ngặt, **Anti KHÔNG tự sửa file C#**. Giữ nguyên mã nguồn của ChatGPT và báo cáo đầy đủ dữ liệu thực tế này để ChatGPT quyết định và cập nhật bản vá C#.

---

## 5. KẾT QUẢ KIỂM THỬ CAD-M01 ĐẾN CAD-M06 TRÊN SANDBOX

Đo đạc trên các Assembly trong `D:\CG1-ISOLATED-TEST-COPIES`:

| Test ID | Assembly | Kết quả đo thực tế | Trạng thái |
|---|---|---|---|
| **CAD-M01** | `Fixture_BREP_Positive.SLDASM` | Part B được dịch +10mm theo trục X, tấm kim loại 3mm chắn trực tiếp 12 tia trục lỗ của Part A. `RayIntersections` trả về `hits=2` trên cả 12 tia. Điểm giao vào tại Y=0.000, ra tại Y=0.003 (span = 3.000mm). Parser bước 6 đọc nhầm `ny=-1` thành `rayIndex` nên phân loại `UNVERIFIED_RAY_TOPOLOGY`. Đã chứng minh có solid thực tế trong COM. | `SOLID_VERIFIED_IN_COM / CLASSIFIER_STRIDE_UNVERIFIED` |
| **CAD-M02** | `Fixture_OK.SLDASM` | Part B có lỗ đồng trục: 13/13 ứng viên được phân loại `BREP_NO_AXIS_HIT` (span = 0.000mm, hits = 0). Không phát sinh NG mới (`NG=0`, `PatternNG=0`, `Missing=0`). | `PASS` |
| **CAD-M03** | `Fixture_MultiLayer_ABC.SLDASM` | Nhiều occurrence ở cao độ Z và transform khác nhau. `Cg2BodyCoordinatesCalibrated` xác nhận thành công tất cả transforms (`Unknown = 0`, `Failures = 0`). 13/13 ứng viên phân loại `BREP_NO_AXIS_HIT`. | `PASS` |
| **CAD-M04** | `1618-01-BH.SLDASM` | Xử lý sub-assembly/multi-body: 984 ứng viên nhắm vào target là sub-assembly (`.SLDASM`) được phân loại an toàn thành `UNVERIFIED_NOT_RESOLVED_PART`; 84 phân loại `UNVERIFIED_RAY_TOPOLOGY`; 68 phân loại `UNVERIFIED_AMBIGUOUS_HITS`. Không crash, không gán nhầm NG. | `VERIFIED_WITH_LIMITATIONS` |
| **CAD-M05** | `1618-01-BH.SLDASM` | Xử lý unresolved/budget: `IsPartial = true`, `CoverageStatus = PARTIAL_UNVERIFIED`. Tất cả 1,297 ứng viên giữ nguyên `CHECK_ONLY`, không ứng viên nào bị xóa, không bị quy kết NG. | `PASS` |
| **CAD-M06** | `1618-01-BH.SLDASM` | Assembly 916 lỗ, 348 cụm, 1,297 ứng viên: Ray tested: 313. `BREP_SOLID_ON_AXIS`: 0. `BREP_NO_AXIS_HIT`: 161. `UNVERIFIED`: 1,136. `NOT_TESTED_BUDGET`: 0. Thời gian: 197.607s. Bộ nhớ: delta 44 MB. | `PASS` |

---

## 6. KẾT QUẢ HỒI QUY SẢN XUẤT (REGRESSION) TRÊN 6 FIXTURE

Chạy qua `Cg2LiveCadRunner.exe`:
- **Production counters (Holes, Stacks, NG, Missing, Rows):** 100% khớp tuyệt đối giữa Baseline CG1 và Candidate CG2 trên cả 6 fixture (`CG1_VS_CG2_PRODUCTION_VERDICTS=PASS`).
- **Normalized Row Signatures (13 fields):** 100% khớp sau khi chuẩn hóa hướng Reference/Actual và sắp xếp dòng.
- **Strict CSV Byte Parity:**
  - `Fixture_NG_Offset.SLDASM`: **PASS** (8,505 bytes, SHA256 trùng khớp)
  - `Fixture_Unmatched.SLDASM`: **PASS** (5,675 bytes, SHA256 trùng khớp)
  - `Fixture_MultiLayer_ABC.SLDASM`: **PASS** (11,769 bytes, SHA256 trùng khớp)
  - `run07_Missing_Ura_BOM.SLDASM`: **PASS** (107 bytes, SHA256 trùng khớp)
  - `Fixture_OK.SLDASM`: **FAIL** (8,262 bytes vs 8,262 bytes, diff @ byte 178 do thứ tự duyệt Component)
  - `1618-01-BH.SLDASM`: **FAIL** (129,522 bytes vs 129,522 bytes, diff @ byte 175 do thứ tự duyệt Component)
  - Exit code tổng: `SUMMARY: PASS=4 FAIL=2 SKIP=0 EXIT=1`
- **3D Marker generation:** `AutoCreateMarkers=false` áp dụng tuyệt đối trong safe sandbox (`MARKER_GENERATION_PARITY=NOT_TESTED`).

---

## 7. MẪU PHẢN HỒI BẮT BUỘC (THEO SECTION 7 GIAO THỨC)

```text
ANTI_CG2_BREP_PATCH_COMPLETE=YES
PR=33
TESTED_REMOTE_SHA=327d1026505411a7db1f9c2122afa1f658883435
SOURCE_AUTHOR=CHATGPT
ANTI_C_SHARP_CHANGES=0
COMPILER_ERRORS=NONE
WINFORMS_TEST=PASS
SYNTHETIC_ENTER_EXIT=PASS
REAL_SOLID_A_B_POSITIVE_SITE=UNVERIFIED_COM_STRIDE_9_VS_6 (RayIntersections hits=2, span=3.000mm confirmed in COM; Cg2ClassifyRayPointData offset=i*6 returned UNVERIFIED_RAY_TOPOLOGY)
REAL_HOLE_A_B_NEGATIVE_SITE=PASS (Fixture_OK coaxial voids return hits=0, BREP_NO_AXIS_HIT=13)
SUBASSEMBLY_MATERIAL_CHECK=NOT_IMPLEMENTED
LARGE_ASSEMBLY_SECONDS=197.6s
LARGE_ASSEMBLY_UNVERIFIED=1136
CG1_VS_CG2_NG_VERDICTS=PASS
CSV_RAW_BYTE_PARITY=FAIL (4 PASS, 2 FAIL due to baseline traversal non-determinism)
READY_FOR_CHATGPT_SOURCE_REVIEW=YES
READY_FOR_MERGE=NO
```
