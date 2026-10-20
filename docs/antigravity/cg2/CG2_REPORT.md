# CHECK HOLE CG2 — ANTIGRAVITY ONE-SHOT BREP VERIFICATION REPORT

> **CHATGPT COM-9 PATCH mới (chưa build/test):** Bản code vừa được ChatGPT sửa trong `CheckAssemblyHole.MaterialEvidence.cs` và `CG2_TEST_HARNESS.cs` đã hỗ trợ mảng 6/9 doubles mỗi ray hit và bổ sung test thực nghiệm COM giả lập. Tất cả kết quả PASS bên dưới là **log Anti của DLL trước patch này**. `COM9_PATCH_BUILD=NOT_TESTED`; `COM9_PATCH_LIVE_SOLID_POSITIVE=NOT_TESTED`; `READY_FOR_MERGE=NO`. Không suy ra CG2 sản xuất đã báo `BREP_SOLID_ON_AXIS` thành công cho tới khi Anti test lại. Xem [CG2_BREP_COM9_FIX.md](CG2_BREP_COM9_FIX.md).

> **STATUS: CG2 BREP REVIEW PATCH VERIFICATION COMPLETE — FACTUAL EVIDENCE RECORDED**
> Branch: `feat/check-hole-cg2-part-relations` | PR: [Draft PR #33](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33)  
> Remote HEAD SHA: `327d1026505411a7db1f9c2122afa1f658883435`  
> All C# code authored and committed by ChatGPT. Antigravity applied code verbatim (`C_SHARP_EDITED_BY_ANTI=NO`, `ANTI_C_SHARP_CHANGES=0`).  
> Broad-phase AABB and diagnostic BREP solid-body ray testing confirmed read-only (`CHECK_ONLY`); zero mutations to production Check Hole verdicts, tolerances, or 3D markers.  
> PR #33 remains strictly in **Draft** mode (`READY_FOR_MERGE=NO`). No merge into `main` and no CG3 implementation.

---

## A. Version and Verification Environment

| Item | Value |
|---|---|
| Repository | `tbcphutai-bit/ADDIN-SolidWorks` |
| Base branch | `feat/check-hole-connection-groups-cg1` |
| Working branch | `feat/check-hole-cg2-part-relations` |
| Pull Request | [Draft PR #33](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33) |
| SOLIDWORKS Environment | SOLIDWORKS 2024 SP5.0 (`32.5.0.0048` / Revision `32.5.0`) |
| SOLIDWORKS Interop | `SolidWorks.Interop.sldworks.dll` version `32.5.0.48` |
| Baseline CG1 Binary | `C:\Tao moi\backup_cg1_release_snapshot\ADDIN.dll` (SHA256: `D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E`) |
| Candidate CG2 Release Binary | `C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll` (SHA256: `23A35979079053A90E3C285A24F13F64CD274C126BBDC62E927054B8226C890A`) |
| Candidate CG2 Debug Binary | `C:\Tao moi\ADDIN.dll` (SHA256: `23A35979079053A90E3C285A24F13F64CD274C126BBDC62E927054B8226C890A`) |
| Test Harness | `docs/antigravity/cg2/CG2_TEST_HARNESS.cs` |
| Live CAD Runner | `docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs` & `docs/antigravity/cg2/CG2_REVIEW3_REPEATABILITY.cs` |
| Review Patch Specification | `docs/antigravity/cg2/CG2_BREP_REVIEW_PATCH.md` |
| Protocol Doc | `docs/antigravity/cg2/CG2_ANTI_REPORTING_PROTOCOL.md` |

---

## B. Rebuild Verification (Visual Studio MSBuild 18.10.1)

Both Release|x64 and Debug|x64 configurations compile cleanly with zero errors and zero warnings introduced by CG2/BREP code:

### 1. Release|x64 Build
```text
Command: MSBuild.exe ADDIN.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /v:minimal
MSBuild version: 18.10.1-1.26427.6+3cd27c13e (.NET Framework 4.8)
Errors: 0
CG2/BREP-introduced warnings: 0 (16 preexisting warnings in LenhDimCanhSongSong, BomDrawingReference, CheckAssemblyHole profiling fields)
Output DLL: C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
SHA256: 23A35979079053A90E3C285A24F13F64CD274C126BBDC62E927054B8226C890A
BUILD_RELEASE_X64=PASS
```

### 2. Debug|x64 Build
```text
Command: MSBuild.exe ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:minimal
Errors: 0
CG2/BREP-introduced warnings: 0 (same 16 preexisting warnings)
Output DLL: C:\Tao moi\ADDIN.dll
SHA256: 23A35979079053A90E3C285A24F13F64CD274C126BBDC62E927054B8226C890A
BUILD_DEBUG_X64=PASS
```

---

## C. Offline Unit & Test Harness Results (`CG2_TEST_HARNESS.cs`)

Executed using Roslyn C# 7.3 against `ADDIN\bin\x64\Release\ADDIN.dll`:

```text
===============================================================
ANTIGRAVITY CHECK HOLE CG2 — REPRODUCIBLE TEST HARNESS
Timestamp: 2026-10-20 06:58:53 UTC
===============================================================
Target Assembly: C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
Assembly SHA256: 23A35979079053A90E3C285A24F13F64CD274C126BBDC62E927054B8226C890A
Loaded Assembly: ADDIN, Version=1.0.0.0, Culture=neutral, PublicKeyToken=36eed1a140b4715f

--- 1. Structural & Reflection Audit of CG2 Types ---
PASS: CheckHoleReport.PartRelations exists
PASS: CheckHoleCg2Report class exists
PASS: CheckHoleCg2RelationRow class exists
PASS: CheckHoleCg2ExpectedHoleRow class exists
PASS: CheckAssemblyHole.BuildCg2PartRelations method exists

--- 2. Direct Geometric Broad-Phase Testing ---
PASS: Cg2ReadBox normalizes valid bounds
PASS: Cg2ReadBox rejects NaN bounds
PASS: Cg2ReadBox rejects Infinity bounds
PASS: Cg2ReadBox rejects short bounds array (< 6)
PASS: Cg2BoxGap calculates 1.0mm separation accurately
PASS: Cg2BoxGap detects overlapping AABBs (gap = 0.0mm)
PASS: Cg2BoxGap correctly identifies non-near boxes (> 2mm)
PASS: Cg2AxisCrossesBoxNear detects axis intersection at 9.5mm (including 0.5mm pad)
PASS: Cg2AxisCrossesBoxNear rejects non-intersecting hole axis
PASS: Cg2AxisCrossesBoxNear rejects box beyond 60mm reach
PASS: Cg2LikelyKnownCounterpart matches coaxial holes
PASS: Cg2LikelyKnownCounterpart matches hole within 4mm radial / 3 deg
PASS: Cg2LikelyKnownCounterpart rejects hole with > 4mm radial offset

--- CG2 BREP OFFLINE: source index, bounded ray, fields ---
PASS: Hole#2 -> zero-based 1; out-of-range source rejected
PASS: bounded source axis clipped to near-target range
PASS: ray outside target Y slab correctly rejected
PASS: diagonal ray-AABB slab intersection
PASS: incomplete ray range rejected, must remain UNVERIFIED
PASS: synthetic 4mm solid ENTRY/EXIT span confirmed
PASS: lone ENTRY remains UNVERIFIED
UNIT_OFFLINE_PASS: BREP field contracts & bounded source ray; real SOLIDWORKS material intersection NOT_TESTED in this harness

--- 3. CG2 primitive tests (NOT full CG2-T01..T12 integrated coverage) ---
UNIT_PRIMITIVE_PASS [CG2-T01]: Cg2LikelyKnownCounterpart returns expected values; full BuildCg2PartRelations test NOT_TESTED
UNIT_PRIMITIVE_PASS [CG2-T02]: Axis intersects box B at 5.50mm, but integrated ExpectedHoleCandidates for B NOT_TESTED
UNIT_PRIMITIVE_PASS [CG2-T03]: axis crosses box; production intent/NG decision NOT_TESTED
UNIT_PRIMITIVE_PASS [CG2-T04]: Two rays intersect B AABB; actual occurrence rows NOT_TESTED
UNIT_PRIMITIVE_PASS [CG2-T05]: Axis hits AABB; no real BREP material test conducted
UNIT_PRIMITIVE_PASS [CG2-T06]: Cg2ReadBox preserves occurrence strings (Bracket-1, Bracket-2) ; full assembly occurrence reconciliation NOT_TESTED
UNIT_PRIMITIVE_PASS [CG2-T07]: invalid bounds rejected by Cg2ReadBox; integrated partial status NOT_TESTED
UNIT_SANITY_PASS [CG2-T08]: literal zero-hole branch sanity; production scan validated separately by CAD runner
UNIT_CONSTANTS_PASS [CG2-T09]: cap constants checked (300000, 500000, 1500, 2000); actual capping branch NOT_TESTED
UNIT_SANITY_PASS [CG2-T10]: synthetic OperationCanceledException and string comparison; actual cancellation/rerun NOT_TESTED
UNIT_EVALUATESTACK_PASS [CG2-T11]: production EvaluateStack threshold 0.09/0.11mm; cross-binary parity tested separately
UNIT_OBJECT_SETUP_PASS [CG2-T12]: synthetic fallback object only; actual Run() error branch NOT_TESTED

--- 4. Testing CheckHoleResultsForm & CG2 WinForms Integration ---
PASS: CheckHoleReport.Summary correctly formats CG2 diagnostic line
PASS: Button 'partRelations' created with text 'Quan hệ Part (CG2)'
PASS: Button initial state is disabled
PASS: Button enabled after SetReport with valid PartRelations
PASS: Button disabled when form is busy
PASS: Button re-enabled when busy=false
PASS: Modal dialog opened via Button.OnClick event dispatch
PASS: Modal title is exactly 'CHECK HOLE - Quan hệ Part / CG2 (chẩn đoán)'
PASS: Mandatory broad-phase disclaimer present in header
PASS: All 3 tabs verified: ReadOnly candidate grid, ReadOnly relation grid, ReadOnly warnings box

===============================================================
RESULT: CG2 PRIMITIVE/SANITY CHECKS PASSED (0 FAILURES); integrated CAD scenarios separately graded
```

---

## D. CAD Runner Self-Test (`CG2_LIVE_CAD_RUNNER.exe --self-test`)

Executed with exit code 0:
```text
PASS: exit policy handles PASS / FAIL / SKIP / empty cases
PASS: malformed candidate was rejected
PASS: Review3 quoted-multiline CSV record splitter
```

---

## E. CAD-M01 through CAD-M06 Live CAD Verification

Measurements conducted in isolated sandbox `D:\CG1-ISOLATED-TEST-COPIES` using SOLIDWORKS 2024 SP5.0:

| Test Case | Target Assembly | Expected Behavior | Observed In-CAD Results | Status |
|---|---|---|---|---|
| **CAD-M01** | `Fixture_BREP_Positive.SLDASM` | Part B solid blocks hole axis; BREP returns `BREP_SOLID_ON_AXIS`, span >= 0.1mm | Component B shifted +10mm in X so 3.0mm solid sheet metal plate physically blocks 12 hole axes of Component A. `ModelDocExtension.RayIntersections` returned `hits=2` on all 12 candidate rays. Native COM `GetRayIntersectionsPoints()` returned 18 doubles with entry at `Y=0.000` and exit at `Y=0.003` (span = 3.000mm). In production code `CheckAssemblyHole.MaterialEvidence.cs:442-446`, 6-stride parser read normal `ny=-1` as `ray`, returning `UNVERIFIED_RAY_TOPOLOGY`. Actual 9-stride COM parsing proves positive solid material. | **SOLID_VERIFIED_IN_COM / CLASSIFIER_STRIDE_UNVERIFIED** |
| **CAD-M02** | `Fixture_OK.SLDASM` | Part B has coaxial hole or void matching ray: `BREP_NO_AXIS_HIT`, no NG assigned | 13 of 13 candidates classified as `BREP_NO_AXIS_HIT` (span = 0.000mm, hits = 0). NG count remained exactly 0 (`NG=0`, `PatternNG=0`, `Missing=0`). No NG assigned. | **PASS** |
| **CAD-M03** | `Fixture_MultiLayer_ABC.SLDASM` | Multiple occurrences of parts with different transforms (elevations / rotations); calibration verified | 13 candidate rays tested. `Cg2BodyCoordinatesCalibrated` verified all component transforms successfully (`Unknown = 0`, `Failures = 0`). All 13 candidates classified as `BREP_NO_AXIS_HIT`. | **PASS** |
| **CAD-M04** | `1618-01-BH.SLDASM` | Multi-body / sub-assembly occurrences; verify safe handling without crashing or false negatives | Sub-assembly components safely detected: 984 candidates marked `UNVERIFIED_NOT_RESOLVED_PART`; 84 marked `UNVERIFIED_RAY_TOPOLOGY`; 68 marked `UNVERIFIED_AMBIGUOUS_HITS`. Engine ran to completion with zero unhandled exceptions. | **VERIFIED_WITH_LIMITATIONS** (sub-assemblies yield UNVERIFIED_NOT_RESOLVED_PART) |
| **CAD-M05** | `1618-01-BH.SLDASM` | Unresolved / sub-assembly / partial handling; verify `PARTIAL_UNVERIFIED` and candidates preserved | `IsPartial = true`, `CoverageStatus = PARTIAL_UNVERIFIED`. All 1,297 candidates preserved as `CHECK_ONLY`, zero deleted, zero converted to NG. | **PASS** |
| **CAD-M06** | `1618-01-BH.SLDASM` | 916-hole assembly performance and diagnostic counters under 500-ray cap | 916 holes, 348 stacks, 0 NG, 1,297 candidates. Ray tested: 313. `BREP_SOLID_ON_AXIS`: 0. `BREP_NO_AXIS_HIT`: 161. `UNVERIFIED`: 1,136. `NOT_TESTED_BUDGET`: 0. Elapsed: 197.607s. Working set delta: 44 MB. | **PASS** |

### Critical Technical Discovery: SOLIDWORKS API `GetRayIntersectionsPoints` Return Layout

COM execution in SOLIDWORKS 2024 SP5.0 revealed the exact binary structure of `ModelDoc2.GetRayIntersectionsPoints()`:
- SOLIDWORKS returns **9 doubles per hit**, NOT 6 doubles!
  `[bodyIndex, rayIndex, flags, x, y, z, nx, ny, nz]`
  Where `(nx, ny, nz)` is the surface normal vector at the intersection point.
- For 2 hits, the array length is 18 doubles:
  - Hit 0: `[0]=0 (body), [1]=0 (ray), [2]=17 (ENTER|FACE), [3]=3.432, [4]=0.000, [5]=-1.094, [6]=0, [7]=-1 (ny), [8]=0`
  - Hit 1: `[9]=0 (body), [10]=0 (ray), [11]=33 (EXIT|FACE), [12]=3.432, [13]=0.003, [14]=-1.094, [15]=0, [16]=1 (ny), [17]=0`
- In `Commands/CheckAssemblyHole.MaterialEvidence.cs:442-446`:
  `int offset = i * 6;` was used.
  At `i = 1`, `offset = 6`, reading `points[6]=0 (body)` and `points[7]=-1 (ray)`.
  Line 446: `if (body < 0 || body >= bodyCount || ray != 0) return "UNVERIFIED_RAY_TOPOLOGY";`
  Because `ray == -1 != 0`, it returned `UNVERIFIED_RAY_TOPOLOGY`.
- With stride 9 (`int offset = i * 9`), Hit 0 enters at `Y=0.000` and Hit 1 exits at `Y=0.003`.
  Span is `0.003 m = 3.000 mm` (>= `0.0001 m`), proving `BREP_SOLID_ON_AXIS`!
- **Action taken per protocol:** Antigravity made ZERO changes to C# files (`C_SHARP_EDITED_BY_ANTI=NO`) and documents this exact finding for ChatGPT's review and official patch.

---

## F. Production Regression Retest (6 Assemblies)

Executed via `Cg2LiveCadRunner.exe`:
```powershell
docs\antigravity\cg2\CG2_LIVE_CAD_RUNNER.exe "C:\Tao moi\backup_cg1_release_snapshot\ADDIN.dll" "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll" "D:\CG1-ISOLATED-TEST-COPIES" "D:\CG1-ISOLATED-TEST-COPIES\SimpleFixtures\Fixture_OK.SLDASM" "D:\CG1-ISOLATED-TEST-COPIES\SimpleFixtures\Fixture_NG_Offset.SLDASM" "D:\CG1-ISOLATED-TEST-COPIES\SimpleFixtures\Fixture_Unmatched.SLDASM" "D:\CG1-ISOLATED-TEST-COPIES\SimpleFixtures\Fixture_MultiLayer_ABC.SLDASM" "D:\CG1-ISOLATED-TEST-COPIES\ZeroHole\run07_Missing_Ura_BOM.SLDASM" "D:\CG1-ISOLATED-TEST-COPIES\ProductionCopy\1618-01-BH.SLDASM"
```

### Detailed Fixture Regression Results:

| Fixture | Logical Holes | Stacks | NG Stacks | Missing | Rows | Strict CSV Byte Parity | Row Detailed Parity | Status |
|---|---|---|---|---|---|---|---|---|
| `Fixture_OK.SLDASM` | 57 | 12 | 0 | 0 | 45 | **CSV_BYTE_PARITY_FAIL** (8,262 bytes, diff @ 178) | **PASS** (all 13 fields) | `FAIL (CSV Order)` |
| `Fixture_NG_Offset.SLDASM` | 57 | 12 | 12 | 0 | 45 | **PASS** (8,505 bytes, identical SHA256) | **PASS** (all 13 fields) | `PASS` |
| `Fixture_Unmatched.SLDASM` | 32 | 0 | 0 | 0 | 32 | **PASS** (5,675 bytes, identical SHA256) | **PASS** (all 13 fields) | `PASS` |
| `Fixture_MultiLayer_ABC.SLDASM`| 82 | 24 | 0 | 0 | 58 | **PASS** (11,769 bytes, identical SHA256) | **PASS** (all 13 fields) | `PASS` |
| `run07_Missing_Ura_BOM.SLDASM` | 0 | 0 | 0 | 0 | 0 | **PASS** (107 bytes, identical SHA256) | **PASS** (all 13 fields) | `PASS` |
| `1618-01-BH.SLDASM` (916 holes)| 916 | 348 | 0 | 0 | 568 | **CSV_BYTE_PARITY_FAIL** (129,522 bytes, diff @ 175) | **PASS** (all 13 fields) | `FAIL (CSV Order)` |

### Regression Analysis:
1. **Production Counters & Verdicts:** 100% identical between CG1 baseline and CG2 candidate across all 6 fixtures (`CG1_VS_CG2_PRODUCTION_VERDICTS=PASS`).
2. **Normalized Row Detailed Parity:** 100% identical across all 13 fields when sorted and Reference/Actual canonicalized.
3. **Strict CSV Byte Comparison:**
   - Byte counts are 100% identical on every fixture (8,262 vs 8,262; 8,505 vs 8,505; 5,675 vs 5,675; 11,769 vs 11,769; 107 vs 107; 129,522 vs 129,522).
   - 4 of 6 fixtures passed 100% strict byte-for-byte SHA256 comparison (`Fixture_NG_Offset`, `Fixture_Unmatched`, `Fixture_MultiLayer_ABC`, `run07_Missing_Ura_BOM`).
   - 2 fixtures (`Fixture_OK` and `1618-01-BH`) differ at the first component block due to `AssemblyDoc.GetComponents(false)` baseline non-determinism between sessions, previously verified and classified as `BASELINE_NONDETERMINISM_OBSERVED`.
   - Overall runner summary: `SUMMARY: PASS=4 FAIL=2 SKIP=0 EXIT=1`.
4. **3D Marker Parity:** `AutoCreateMarkers=false` strictly enforced in safe sandbox mode (`MARKER_GENERATION_PARITY=NOT_TESTED`).

---

## G. Mandatory Status Summary (Section 7 Protocol)

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
