# CHECK HOLE CG2 — ANTIGRAVITY ONE-SHOT BREP VERIFICATION REPORT

> **STATUS: CG2 BREP COM 9-DOUBLE RAY FIX VERIFIED — FACTUAL EVIDENCE RECORDED**
> Branch: `feat/check-hole-cg2-part-relations` | PR: [Draft PR #33](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33)  
> Remote HEAD SHA (Code): `44d6fc8c454e99f1fa02c638e53aa8f244583161`  
> All C# code authored and committed by ChatGPT. Antigravity applied code verbatim (`ANTI_CSHARP_MODIFICATION=NO`, `ANTI_C_SHARP_CHANGES=0`).  
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
| Candidate CG2 Release Binary | `C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll` (SHA256: `6CE7B28B84890FF87C6BAC4C3454BF3965E4BC3B2EFD38D1918BE735B4854D78`) |
| Candidate CG2 Debug Binary | `C:\Tao moi\ADDIN.dll` (SHA256: `93A868A4996076792A0DF103A854601D990C96017E44F9D9DF4AE712C43E7AD6`) |
| Test Harness | `docs/antigravity/cg2/CG2_TEST_HARNESS.cs` |
| Live CAD Runner | `docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs` & `docs/antigravity/cg2/CG2_REVIEW3_REPEATABILITY.cs` |
| Review Patch Specification | `docs/antigravity/cg2/CG2_BREP_COM9_FIX.md` |
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
SHA256: 6CE7B28B84890FF87C6BAC4C3454BF3965E4BC3B2EFD38D1918BE735B4854D78
BUILD_RELEASE_X64=PASS
```

### 2. Debug|x64 Build
```text
Command: MSBuild.exe ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:minimal
Errors: 0
CG2/BREP-introduced warnings: 0 (same 16 preexisting warnings)
Output DLL: C:\Tao moi\ADDIN.dll
SHA256: 93A868A4996076792A0DF103A854601D990C96017E44F9D9DF4AE712C43E7AD6
BUILD_DEBUG_X64=PASS
```

---

## C. Offline Unit & Test Harness Results (`CG2_TEST_HARNESS.cs`)

Executed using Roslyn C# 7.3 against `ADDIN\bin\x64\Release\ADDIN.dll`:

```text
===============================================================
ANTIGRAVITY CHECK HOLE CG2 — REPRODUCIBLE TEST HARNESS
Timestamp: 2026-10-20 07:31:21 UTC
===============================================================
Target Assembly: C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
Assembly SHA256: 6CE7B28B84890FF87C6BAC4C3454BF3965E4BC3B2EFD38D1918BE735B4854D78
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
PASS: 9-double COM layout returns 3.000mm solid span
PASS: unsupported ray-result stride remains UNVERIFIED
PASS: invalid second ray index remains UNVERIFIED
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

## E. Live CAD Verification of BREP Material Evidence

Measurements conducted in isolated sandbox `D:\CG1-ISOLATED-TEST-COPIES` using SOLIDWORKS 2024 SP5.0:

### 1. Positive Solid Test (`Fixture_BREP_Positive.SLDASM`)
- Part B was offset +10mm in X so its 3.0mm solid sheet metal plate directly blocks the 12 hole axes of Component A.
- **Observed In-CAD Results:**
  - `Tested = 37`, `SolidEvidence = 24`, `NoAxisHit = 13`, `Unknown = 0`, `NotTestedBudget = 0`
  - Candidate #26..#37 (`fr1-2` -> `1-1`): **`BREP_SOLID_ON_AXIS`**, `MaterialHitCount = 2`, `MaterialSpanMm = 3.000 mm` (exact 3.000mm sheet thickness).
  - Candidate #14..#25 (`1-1` -> `fr1-2` reverse): **`BREP_SOLID_ON_AXIS`**, `MaterialHitCount = 2`, `MaterialSpanMm = 2.000 mm` (exact 2.000mm sheet thickness).
  - Candidate #1..#13 (coaxial holes): **`BREP_NO_AXIS_HIT`**, `MaterialHitCount = 0`, `MaterialSpanMm = 0.000 mm`.
  - Zero `UNVERIFIED_RAY_TOPOLOGY` occurrences.
  - Production NG remained completely unaffected (no false NG, no mutations).
  - **Verdict:** **`LIVE_3MM_SOLID_POSITIVE=PASS`**

### 2. Negative Match Coaxial Hole Test (`Fixture_OK.SLDASM`)
- Part B has matching coaxial holes/voids aligned with source hole rays.
- **Observed In-CAD Results:**
  - `Tested = 13`, `SolidEvidence = 0`, `NoAxisHit = 13`, `Unknown = 0`, `NotTestedBudget = 0`
  - All 13 candidates classified as `BREP_NO_AXIS_HIT` (`Span = 0.000 mm`, `Hits = 0`).
  - Production NG remained exactly 0 (`NG=0`, `PatternNG=0`, `Missing=0`).
  - **Verdict:** **`NEGATIVE_HOLE_FIXTURE=PASS`**

---

## F. Production Regression Retest

Executed via `Cg2LiveCadRunner.exe` on sandbox fixtures:

| Fixture | Logical Holes | Stacks | NG Stacks | Missing | Rows | Strict CSV Byte Parity | Row Detailed Parity | Status |
|---|---|---|---|---|---|---|---|---|
| `Fixture_OK.SLDASM` | 57 | 12 | 0 | 0 | 45 | **CSV_BYTE_PARITY_FAIL** (8,262 bytes, diff @ 178) | **PASS** (all 13 fields) | `FAIL (CSV Order)` |
| `Fixture_NG_Offset.SLDASM` | 57 | 12 | 12 | 0 | 45 | **CSV_BYTE_PARITY_FAIL** (8,505 bytes, diff @ 185) | **PASS** (all 13 fields) | `FAIL (CSV Order)` |
| `Fixture_Unmatched.SLDASM` | 32 | 0 | 0 | 0 | 32 | **PASS** (5,675 bytes, identical SHA256) | **PASS** (all 13 fields) | `PASS` |
| `Fixture_MultiLayer_ABC.SLDASM`| 82 | 24 | 0 | 0 | 58 | **CSV_BYTE_PARITY_FAIL** (11,769 bytes, diff @ 190) | **PASS** (all 13 fields) | `FAIL (CSV Order)` |
| `run07_Missing_Ura_BOM.SLDASM` | 0 | 0 | 0 | 0 | 0 | **PASS** (107 bytes, identical SHA256) | **PASS** (all 13 fields) | `PASS` |

### Regression Analysis:
1. **Production Counters & Verdicts:** 100% identical between CG1 baseline and CG2 candidate across all fixtures (`CG1_VS_CG2_PRODUCTION_VERDICTS=PASS`).
2. **Normalized Row Detailed Parity:** 100% identical across all 13 fields when sorted and Reference/Actual canonicalized.
3. **Strict CSV Byte Comparison:**
   - Byte counts are 100% identical on every fixture.
   - 2 of 5 fixtures passed 100% strict byte-for-byte SHA256 comparison (`Fixture_Unmatched`, `run07_Missing_Ura_BOM`).
   - 3 fixtures differ at the first component block due to `AssemblyDoc.GetComponents(false)` baseline non-determinism between sessions (`BASELINE_NONDETERMINISM_OBSERVED`).
4. **3D Marker Parity:** `AutoCreateMarkers=false` strictly enforced in safe sandbox mode (`MARKER_GENERATION_PARITY=NOT_TESTED`).

---

## G. Mandatory Status Summary (Section C Protocol)

```text
CHATGPT_CSHARP_PATCH_APPLIED=YES
ANTI_CSHARP_MODIFICATION=NO
BUILD_DEBUG_X64=PASS
BUILD_RELEASE_X64=PASS
OFFLINE_6_AND_9_STRIDE_TEST=PASS
LIVE_3MM_SOLID_POSITIVE=PASS
LIVE_POSITIVE_MATERIAL_SPAN_MM=3.000
LIVE_POSITIVE_COM_HITS=2
NEGATIVE_HOLE_FIXTURE=PASS
CG1_VS_CG2_VERDICTS=PASS
RAW_CSV_BYTE_PARITY=FAIL (baseline traversal non-determinism observed)
SOURCE_SHA=44d6fc8c454e99f1fa02c638e53aa8f244583161
COMPILED_DLL_SHA256=6CE7B28B84890FF87C6BAC4C3454BF3965E4BC3B2EFD38D1918BE735B4854D78
READY_FOR_CHATGPT_SOURCE_REVIEW=YES
READY_FOR_MERGE=NO
```
