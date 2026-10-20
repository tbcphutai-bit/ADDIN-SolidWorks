# CG2 REVIEW #3 — REPEATABILITY DIAGNOSTIC EVIDENCE

**Date:** 2026-10-20  
**Repository:** `tbcphutai-bit/ADDIN-SolidWorks`  
**Branch:** `feat/check-hole-cg2-part-relations`  
**Pull Request:** [Draft PR #33](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33)  
**Fixture:** `D:\CG1-ISOLATED-TEST-COPIES\ProductionCopy\1618-01-BH.SLDASM` (916 holes, 82 components)  
**Sandbox Consent File:** `D:\CG1-ISOLATED-TEST-COPIES\CG1_SANDBOX_OK.txt` (Present and verified)

---

## 1. Compilation & Self-Test Verification

### A. Roslyn C# Compilation
Compiled standalone runner combining `CG2_LIVE_CAD_RUNNER.cs` and `CG2_REVIEW3_REPEATABILITY.cs` using Microsoft Roslyn C# Compiler (C# 7.3 / .NET Framework 4.8):

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe" `
    /nologo /langversion:7.3 /target:exe /platform:x64 `
    /out:"docs\antigravity\cg2\CG2_LIVE_CAD_RUNNER.exe" `
    /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll" `
    /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.swconst.dll" `
    "docs\antigravity\cg2\CG2_LIVE_CAD_RUNNER.cs" "docs\antigravity\cg2\CG2_REVIEW3_REPEATABILITY.cs"
```
- **Compiler Exit Code:** `0` (Zero errors, zero warnings)

### B. Runner Self-Test
```text
Command: docs\antigravity\cg2\CG2_LIVE_CAD_RUNNER.exe --self-test
Output:
PASS: exit policy handles PASS / FAIL / SKIP / empty cases
PASS: malformed candidate was rejected
PASS: Review3 quoted-multiline CSV record splitter
Exit Code: 0
```

---

## 2. Test Execution Details

- **SOLIDWORKS Version:** 2024 SP5.0 (Revision `32.5.0`, build `32.5.0.0048`)
- **Baseline CG1 Binary:** `C:\Tao moi\backup_cg1_release_snapshot\ADDIN.dll`  
  `SHA256: D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E`
- **Candidate CG2 Binary:** `C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll`  
  `SHA256: 8D470DEA5B0900BF4E5FD50CFF3B219BB1F1AA9323A52CD2B9853F5377C1B046`
- **Safety Flags:** `AutoCreateMarkers=false`, `MARKER_GENERATION_PARITY=NOT_TESTED`
- **Execution Command:**
```powershell
docs\antigravity\cg2\CG2_LIVE_CAD_RUNNER.exe `
    "C:\Tao moi\backup_cg1_release_snapshot\ADDIN.dll" `
    "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll" `
    "D:\CG1-ISOLATED-TEST-COPIES" `
    "D:\CG1-ISOLATED-TEST-COPIES\ProductionCopy\1618-01-BH.SLDASM" `
    --repeatability
```

---

## 3. Four Isolated CAD Runs (Fresh Read-Only Document Opens)

| Run ID | Binary Version | CSV Bytes | CSV SHA256 | Rows | Holes | Stacks | NG Stacks |
|---|---|---|---|---|---|---|---|
| `cg1_1` | Baseline CG1 | 129,522 | `587E3D0FE823C30A3C9A239CF763F911DF304D205532D4D2CC2DE6EA624A070C` | 568 | 916 | 348 | 0 |
| `cg1_2` | Baseline CG1 | 129,522 | `40448A13CB1EE3EC530E5D5DACBD2D197D590BC6B1DDBED0CDEB89787C8CD63E` | 568 | 916 | 348 | 0 |
| `cg2_1` | Candidate CG2 | 129,522 | `CDE84C5DCDEFF197B3BCB9473F2C4E24D1CB89381B6584FB14BE952CF9531748` | 568 | 916 | 348 | 0 |
| `cg2_2` | Candidate CG2 | 129,522 | `D00892D549CB3F596B41E1985168CD6CCEEB11632EA9E3B1FC8A3F98C1180161` | 568 | 916 | 348 | 0 |

---

## 4. Pairwise Diagnostic Analysis

### A. Comparison 1: `cg1_1` vs `cg1_2` (Baseline Repeatability)
- **CSV_BYTE_EQUAL:** `False`
- **CSV SHA256 Left:** `587E3D0FE823C30A3C9A239CF763F911DF304D205532D4D2CC2DE6EA624A070C`
- **CSV SHA256 Right:** `40448A13CB1EE3EC530E5D5DACBD2D197D590BC6B1DDBED0CDEB89787C8CD63E`
- **FIRST_DIFFERENT_BYTE:** `175`
- **ORDERED_GETCOMPONENTS_EQUAL:** `False`
- **RAW_ROW_ORDER_AND_EXACT_VALUES_EQUAL:** `False`
- **CANONICALIZED_ROW_SIGNATURE_EQUAL:** `True`
- **Component Traversal Differences (First 5):**
  - Index 0: `0|1618-02-bh-A6-1` vs `0|1618-02-bh-A10-1`
  - Index 1: `1|1618-02-bh-A6-1/1618-04-bh-A6-fr3-1` vs `1|1618-02-bh-A10-1/1618-04-bh-A10-fr1-2`
  - Index 2: `2|1618-02-bh-A6-1/1618-04-bh-A6-1-1` vs `2|1618-02-bh-A10-1/1618-04-bh-A10-fr4-2`
  - Index 3: `3|1618-02-bh-A6-1/1618-04-bh-A6-fr1-2` vs `3|1618-02-bh-A10-1/1618-04-bh-A10-fr3-1`
  - Index 4: `4|1618-02-bh-A6-1/1618-04-bh-A6-fr2-2` vs `4|1618-02-bh-A10-1/1618-04-bh-A10-中骨1-2`

### B. Comparison 2: `cg2_1` vs `cg2_2` (Candidate Repeatability)
- **CSV_BYTE_EQUAL:** `False`
- **CSV SHA256 Left:** `CDE84C5DCDEFF197B3BCB9473F2C4E24D1CB89381B6584FB14BE952CF9531748`
- **CSV SHA256 Right:** `D00892D549CB3F596B41E1985168CD6CCEEB11632EA9E3B1FC8A3F98C1180161`
- **FIRST_DIFFERENT_BYTE:** `175`
- **ORDERED_GETCOMPONENTS_EQUAL:** `False`
- **RAW_ROW_ORDER_AND_EXACT_VALUES_EQUAL:** `False`
- **CANONICALIZED_ROW_SIGNATURE_EQUAL:** `True`
- **Component Traversal Differences (First 5):**
  - Index 0: `0|1618-02-bh-A10-1` vs `0|1618-04-wr-A02-new-1`
  - Index 1: `1|1618-02-bh-A10-1/1618-04-bh-A10-fr1-2` vs `1|1618-02-bh-A6-1`
  - Index 2: `2|1618-02-bh-A10-1/1618-04-bh-A10-fr3-1` vs `2|1618-02-bh-A6-1/1618-04-bh-A6-fr1-2`
  - Index 3: `3|1618-02-bh-A10-1/1618-04-bh-A10-fr4-2` vs `3|1618-02-bh-A6-1/1618-04-bh-A6-1-1`
  - Index 4: `4|1618-02-bh-A10-1/1618-04-bh-A10-1-1` vs `4|1618-02-bh-A6-1/1618-04-bh-A6-fr2-2`

### C. Comparison 3: `cg1_1` vs `cg2_1` (Cross-Build Comparison)
- **CSV_BYTE_EQUAL:** `False`
- **CSV SHA256 Left:** `587E3D0FE823C30A3C9A239CF763F911DF304D205532D4D2CC2DE6EA624A070C`
- **CSV SHA256 Right:** `CDE84C5DCDEFF197B3BCB9473F2C4E24D1CB89381B6584FB14BE952CF9531748`
- **FIRST_DIFFERENT_BYTE:** `175`
- **ORDERED_GETCOMPONENTS_EQUAL:** `False`
- **RAW_ROW_ORDER_AND_EXACT_VALUES_EQUAL:** `False`
- **CANONICALIZED_ROW_SIGNATURE_EQUAL:** `True`

---

## 5. Summary Flags and Formal Diagnostic Classification

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

### Key Technical Findings — Evidence vs. Inference

1. **OBSERVED:** CG1→CG1 và CG2→CG2 đều tạo CSV có SHA256 khác nhau dù metrics 916 holes / 348 stacks / 0 NG / 568 rows giữ nguyên trong phiên thử này. CG1→CG2 cũng khác byte.
2. **OBSERVED:** `SnapshotComponentTraversal()` (gọi trước `Run()`) ghi nhận Component-order snapshots khác nhau; raw ordered row signatures khác nhau.
3. **NOT PROVEN:** Check Hole production gọi `GetComponents(false)` độc lập trong `ReadAssemblyHoles()`. Runner hiện không log chính array mà production đã duyệt. Vì vậy mối quan hệ nhân quả duy nhất giữa thứ tự GetComponents và thay đổi CSV chỉ là giả thuyết phù hợp dữ liệu, không phải causal proof.
4. **NORMALIZED MATCH:** `ReportRowSignature()` sort rows, canonicalize Reference/Actual và format số theo F6. Việc chữ ký khớp chứng minh normalized comparison khớp theo tiêu chí đã đặt, không chứng minh exact full-precision/ordered equality.
5. **ACCEPTANCE STATUS:** Strict byte parity FAIL (exit 1) như đo được; `MARKER_GENERATION_PARITY=NOT_TESTED`; full CG2 T01–T12 end-to-end chưa được xác minh đầy đủ; precise material/fastener intent not implemented.

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
