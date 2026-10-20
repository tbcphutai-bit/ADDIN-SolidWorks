# CHECK HOLE CG2 — ANTIGRAVITY VERIFICATION REPORT

> **UPDATE — ChatGPT Review #3 Repeatability Suite Executed:**  
> Standalone runner combining `CG2_LIVE_CAD_RUNNER.cs` and `CG2_REVIEW3_REPEATABILITY.cs` was compiled with Roslyn C# 7.3 and executed with `--repeatability` on the 916-hole assembly `1618-01-BH.SLDASM` in sandbox `D:\CG1-ISOLATED-TEST-COPIES`.  
> **Key Results:** `CG1_REPEATABLE_BYTES=NO`, `CG2_REPEATABLE_BYTES=NO`, `CG1_VS_CG2_BYTE_PARITY=FAIL`, `PRODUCTION_CANONICAL_COUNTERS=PASS`, `DIAGNOSTIC_CLASSIFICATION=BASELINE_NONDETERMINISM_OBSERVED`, `REPEATABILITY_EXIT_CODE=1`.  
> Full sanitized runner trace and pairwise comparisons are recorded in [`CG2_REVIEW3_EVIDENCE.md`](CG2_REVIEW3_EVIDENCE.md).

---

> STATUS: **REVIEW #3 REPEATABILITY COMPLETE — FACTUAL EVIDENCE RECORDED**  
> Branch: `feat/check-hole-cg2-part-relations` | PR: [Draft PR #33](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33)  
> Broad-phase AABB spatial screening confirmed read-only (`CHECK_ONLY`); zero mutations to production Check Hole verdicts, tolerances, or 3D markers.  
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
| Candidate CG2 Release Binary | `C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll` (SHA256: `8D470DEA5B0900BF4E5FD50CFF3B219BB1F1AA9323A52CD2B9853F5377C1B046`) |
| Candidate CG2 Debug Binary | `C:\Tao moi\ADDIN.dll` (SHA256: `0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC`) |
| Test Harness | `docs/antigravity/cg2/CG2_TEST_HARNESS.cs` |
| Live CAD Runner | `docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs` & `docs/antigravity/cg2/CG2_REVIEW3_REPEATABILITY.cs` |
| Review #3 Reference Doc | `docs/antigravity/cg2/CG2_REVIEW3_CODE.md` |
| Review #3 Evidence Doc | `docs/antigravity/cg2/CG2_REVIEW3_EVIDENCE.md` |

---

## B. Rebuild Verification (Visual Studio MSBuild 18.10.1)

Both Release and Debug x64 configurations compile cleanly with zero errors and zero warnings introduced by CG2 code:

### 1. Release|x64 Build
```text
Command: MSBuild.exe ADDIN.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /v:minimal
MSBuild version: 18.10.1-1.26427.6+3cd27c13e (.NET Framework 4.8)
Errors: 0
CG2-introduced warnings: 0 (16 preexisting warnings in LenhDimCanhSongSong, BomDrawingReference, CheckAssemblyHole profiling fields)
Output DLL: C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
SHA256: 8D470DEA5B0900BF4E5FD50CFF3B219BB1F1AA9323A52CD2B9853F5377C1B046
```

### 2. Debug|x64 Build
```text
Command: MSBuild.exe ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:minimal
Errors: 0
CG2-introduced warnings: 0 (same 16 preexisting warnings)
Output DLL: C:\Tao moi\ADDIN.dll
SHA256: 0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC
```

---

## C. Offline Unit & Primitive Test Results (`CG2_TEST_HARNESS.cs`)

Per ChatGPT Review #2, primitive and synthetic tests are accurately classified with specific labels and explicit `NOT_TESTED` boundaries:

```text
===============================================================
ANTIGRAVITY CHECK HOLE CG2 — REPRODUCIBLE TEST HARNESS
Target Assembly: C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
Assembly SHA256: 8D470DEA5B0900BF4E5FD50CFF3B219BB1F1AA9323A52CD2B9853F5377C1B046
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
The negative candidate validation test (rejecting `Classification != "CHECK_ONLY"`, empty occurrence strings, or non-positive IDs) and the Review #3 multiline CSV parser passed cleanly.

---

## E. In-CAD Sandbox Regression Retest (6 Assemblies)

Executed in isolated sandbox `D:\CG1-ISOLATED-TEST-COPIES`:
```powershell
docs\antigravity\cg2\CG2_LIVE_CAD_RUNNER.exe "C:\Tao moi\backup_cg1_release_snapshot\ADDIN.dll" "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll" "D:\CG1-ISOLATED-TEST-COPIES" "D:\CG1-ISOLATED-TEST-COPIES\SimpleFixtures\Fixture_OK.SLDASM" "D:\CG1-ISOLATED-TEST-COPIES\SimpleFixtures\Fixture_NG_Offset.SLDASM" "D:\CG1-ISOLATED-TEST-COPIES\SimpleFixtures\Fixture_Unmatched.SLDASM" "D:\CG1-ISOLATED-TEST-COPIES\SimpleFixtures\Fixture_MultiLayer_ABC.SLDASM" "D:\CG1-ISOLATED-TEST-COPIES\ZeroHole\run07_Missing_Ura_BOM.SLDASM" "D:\CG1-ISOLATED-TEST-COPIES\ProductionCopy\1618-01-BH.SLDASM"
```

### Detailed Fixture Results Table

| Fixture | Logical Holes | Stacks | NG Stacks | Missing | Rows | Exact A->B Candidates | Strict CSV Byte Parity | Row Detailed Parity | Status |
|---|---|---|---|---|---|---|---|---|---|
| `Fixture_OK.SLDASM` | 57 | 12 | 0 | 0 | 45 | **13** (exact `fr1-2` -> `1-1`) | **PASS** (8,262 bytes, identical SHA256) | **PASS** (all 13 fields) | `PASS` |
| `Fixture_NG_Offset.SLDASM` | 57 | 12 | 12 | 0 | 45 | N/A (0 near pairs) | **PASS** (8,505 bytes, identical SHA256) | **PASS** (all 13 fields) | `PASS` |
| `Fixture_Unmatched.SLDASM` | 32 | 0 | 0 | 0 | 32 | **0** (asserted zero) | **PASS** (5,675 bytes, identical SHA256) | **PASS** (all 13 fields) | `PASS` |
| `Fixture_MultiLayer_ABC.SLDASM`| 82 | 24 | 0 | 0 | 58 | **13** (exact `fr1-2` -> `1-1`) | **PASS** (11,769 bytes, identical SHA256)| **PASS** (all 13 fields) | `PASS` |
| `run07_Missing_Ura_BOM.SLDASM` | 0 | 0 | 0 | 0 | 0 | **0** (asserted zero) | **PASS** (107 bytes, identical SHA256) | **PASS** (all 13 fields) | `PASS` |
| `1618-01-BH.SLDASM` (916 holes)| 916 | 348 | 0 | 0 | 568 | 1,297 candidates | **FAIL_BASELINE_NONDETERMINISTIC** (See Section G) | **PASS** (all 13 fields, 568 rows) | `BASELINE_NONDETERMINISTIC` |

---

## F. Limitations and Acceptance Criteria Mapping

| Item | Status | Verification Detail |
|---|---|---|
| Source C# candidate provenance assertions | **VERIFIED** | Strict checks on ID, occurrences, label, and `CHECK_ONLY` |
| Exact occurrence A->B screening evidence | **VERIFIED** | Exactly 13 A->B candidates observed in `Fixture_OK` and `Fixture_MultiLayer_ABC` |
| Strict raw CSV byte parity (small/simple fixtures) | **VERIFIED** | 5 of 5 fixtures pass byte-for-byte (`SequenceEqual == true`) |
| Strict raw CSV byte parity (916-hole assembly) | **FAIL (Exit 1)** | CG1→CG1, CG2→CG2 và CG1→CG2 đều khác byte; nguyên nhân duy nhất chưa xác minh |
| Canonicalized counters/row signatures | **PASS (normalized comparison only)** | 916 logical holes, 348 stacks, 0 NG, 568 rows; signature đã sort/swap Ref–Actual và làm tròn F6 |
| Exact ordered-row / Reference–Actual / full-precision parity | **NOT_VERIFIED** | Trace raw-ordered rows khác nhau; không được gọi 100% identical |
| Review #3 Repeatability diagnostic runner | **EXECUTED** | 4 fresh CAD opens, 3 pair comparisons executed; output in `CG2_REVIEW3_EVIDENCE.md` |
| Negative candidate validation | **VERIFIED** | Self-test asserts rejection of non-CHECK_ONLY candidates |
| Full 12 scenarios CG2 end-to-end | **NOT_VERIFIED** | Primitive tests pass; full integrated pipeline remains partially covered by fixtures |
| BREP/material contact & fastening intent | **NOT_IMPLEMENTED** | Broad-phase AABB screening only; disclaimer mandatory in UI |
| CG1 production results unaffected | **VERIFIED (fixtures)** | Trên sáu fixture, counters/verdicts và canonicalized rows so được; chưa bao phủ mọi model hay marker generation |
| 3D marker generation parity | **NOT_TESTED** | `AutoCreateMarkers=false` strictly enforced in safe sandbox mode |
| Ready for merge | **NO** | PR #33 kept strictly in Draft mode |

---

## G. Review #3 Repeatability Diagnostic Results (`1618-01-BH.SLDASM`)

### 1. Diagnostic Protocol
The diagnostic runner executed 4 sequential fresh read-only CAD document opens of `1618-01-BH.SLDASM` in the approved sandbox:
- `cg1_1`: Baseline CG1 open #1
- `cg1_2`: Baseline CG1 open #2
- `cg2_1`: Candidate CG2 open #1
- `cg2_2`: Candidate CG2 open #2

### 2. Run Metrics & SHA256 Signatures

| Run | DLL | CSV Length | CSV SHA256 | Holes | Stacks | NG | Rows |
|---|---|---|---|---|---|---|---|
| `cg1_1` | Baseline | 129,522 | `587E3D0FE823C30A3C9A239CF763F911DF304D205532D4D2CC2DE6EA624A070C` | 916 | 348 | 0 | 568 |
| `cg1_2` | Baseline | 129,522 | `40448A13CB1EE3EC530E5D5DACBD2D197D590BC6B1DDBED0CDEB89787C8CD63E` | 916 | 348 | 0 | 568 |
| `cg2_1` | Candidate | 129,522 | `CDE84C5DCDEFF197B3BCB9473F2C4E24D1CB89381B6584FB14BE952CF9531748` | 916 | 348 | 0 | 568 |
| `cg2_2` | Candidate | 129,522 | `D00892D549CB3F596B41E1985168CD6CCEEB11632EA9E3B1FC8A3F98C1180161` | 916 | 348 | 0 | 568 |

### 3. Pairwise Diagnostics
1. **`cg1_1` vs `cg1_2` (Baseline self-repeatability):**
   - `CSV_BYTE_EQUAL=False` (differs at byte 175)
   - `ORDERED_GETCOMPONENTS_EQUAL=False` (Component 0 was `1618-02-bh-A6-1` vs `1618-02-bh-A10-1`)
   - `RAW_ROW_ORDER_AND_EXACT_VALUES_EQUAL=False`
   - `CANONICALIZED_ROW_SIGNATURE_EQUAL=True`
2. **`cg2_1` vs `cg2_2` (Candidate self-repeatability):**
   - `CSV_BYTE_EQUAL=False` (differs at byte 175)
   - `ORDERED_GETCOMPONENTS_EQUAL=False` (Component 0 was `1618-02-bh-A10-1` vs `1618-04-wr-A02-new-1`)
   - `RAW_ROW_ORDER_AND_EXACT_VALUES_EQUAL=False`
   - `CANONICALIZED_ROW_SIGNATURE_EQUAL=True`
3. **`cg1_1` vs `cg2_1` (Cross-build comparison):**
   - `CSV_BYTE_EQUAL=False` (differs at byte 175)
   - `ORDERED_GETCOMPONENTS_EQUAL=False`
   - `RAW_ROW_ORDER_AND_EXACT_VALUES_EQUAL=False`
   - `CANONICALIZED_ROW_SIGNATURE_EQUAL=True`

### 4. Diagnostic Summary Output
```text
CG1_REPEATABLE_BYTES=NO
CG2_REPEATABLE_BYTES=NO
CG1_VS_CG2_BYTE_PARITY=FAIL
PRODUCTION_CANONICAL_COUNTERS=PASS
DIAGNOSTIC_CLASSIFICATION=BASELINE_NONDETERMINISM_OBSERVED
CAUSE_OF_ORDER_CHANGE=NOT_YET_PROVEN_BY_THIS_CLASSIFICATION
SKETCH_MARKER_WRITE_PARITY=NOT_TESTED

REPEATABILITY_EXIT_CODE=1
```

### 5. Kết luận kỹ thuật — giới hạn bằng chứng

- **Quan sát được:** Trong 4 lần mở riêng của cùng bản sao Assembly 916 lỗ, CSV byte khác nhau cả khi chạy lại chính binary CG1 và chính binary CG2. Số logical holes = 916, stacks = 348, NG = 0 và report rows = 568 ở cả 4 lần.
- **Quan sát được:** Snapshot riêng của `GetComponents(false)` trước `Run()` trả về thứ tự Component khác nhau. Các raw ordered rows cũng khác. `CANONICALIZED_ROW_SIGNATURE_EQUAL=True` khi đã chuẩn hóa hướng Reference/Actual, sắp xếp dòng và làm tròn F6.
- **Chưa chứng minh:** Snapshot `GetComponents(false)` không phải array chính xác mà production `ReadAssemblyHoles()` sử dụng. Vì vậy chưa thể khẳng định API traversal là nguyên nhân duy nhất của raw CSV order difference, hay 223 reference/actual swaps đã được đếm bằng assertion.
- **Giới hạn:** Không có bằng chứng dữ liệu raw full-precision, role Reference/Actual và thứ tự từng dòng giống tuyệt đối; marker generation/update chưa kiểm thử. Kết quả này không chứng minh CG2 đã nhận diện thật vật liệu, mối ghép hoặc lỗ thiếu.
- **Điều đã được hỗ trợ:** CSV mismatch không chỉ xuất hiện do thêm CG2 vì CG1→CG1 cũng không ổn định trong lần thử. Không dùng quan sát này để kết luận có hoặc không có bug trong mọi model.
- **Kết quả kiểm thử:** `REPEATABILITY_EXIT_CODE=1` và `RAW_CSV_BYTE_PARITY=FAIL` được giữ nguyên; không đổi thành PASS.

```text
RAW_CSV_BYTE_PARITY=FAIL
BASELINE_RAW_CSV_NONDETERMINISM=OBSERVED
PRE_RUN_GETCOMPONENTS_ORDER_VARIATION=OBSERVED
PRODUCTION_TRAVERSAL_CAUSE=NOT_PROVEN
EXACT_ALL_FIELD_GEOMETRY_PARITY=NOT_VERIFIED
CANONICALIZED_ROW_PARITY=PASS_WITH_NORMALIZATION
MARKER_GENERATION_PARITY=NOT_TESTED
CG2_END_TO_END_ALL_SCENARIOS=NOT_VERIFIED
READY_FOR_MERGE=NO
```
