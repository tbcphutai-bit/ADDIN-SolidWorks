# CHECK HOLE CG1 — TEST HARNESS & VERIFICATION EVIDENCE

> Source files:
> - [`docs/antigravity/reports/TEST_HARNESS.cs`](TEST_HARNESS.cs) (Offline algorithmic, tolerance & WinForms state harness)
> - [`docs/antigravity/reports/LIVE_CAD_RUNNER.cs`](LIVE_CAD_RUNNER.cs) (Live SOLIDWORKS dual-assembly regression runner: Baseline `main` vs `CG1`)
>
> Execution Date: 2026-10-19 06:45:22 UTC
> Target Binary (CG1): `C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll`
> Target Binary SHA256: `D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E`
> Target MVID: `456f9ade-ac27-4395-854f-857ad2521fd0`
> Baseline Binary (`main`): `C:\Tao moi\ADDIN_MAIN_BASELINE\bin\x64\Release\ADDIN.dll`
> Baseline Binary SHA256: `297D66CDF1EF56382C1165B8F84AF7B394090B2B8D2DC25A2F4D71067836118D`
> Baseline MVID: `8008c4c2-2258-456a-8cf5-5b6fed1d742b`

---

## 1. How to Reproduce

### A. Offline Test Harness (17/17 Checks Passed)
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe" /r:"C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll" /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll" /out:"docs\antigravity\reports\TEST_HARNESS.exe" "docs\antigravity\reports\TEST_HARNESS.cs"
& "docs\antigravity\reports\TEST_HARNESS.exe" "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll"
Remove-Item "docs\antigravity\reports\TEST_HARNESS.exe" -Force
```

### B. Live SOLIDWORKS In-CAD Dual-Assembly Regression Benchmark
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe" /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll" /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.swconst.dll" /out:"docs\antigravity\reports\LIVE_CAD_RUNNER.exe" "docs\antigravity\reports\LIVE_CAD_RUNNER.cs"
& "docs\antigravity\reports\LIVE_CAD_RUNNER.exe" "C:\Tao moi\ADDIN_MAIN_BASELINE\bin\x64\Release\ADDIN.dll" "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll"
Remove-Item "docs\antigravity\reports\LIVE_CAD_RUNNER.exe" -Force
```

---

## 2. Verbatim Console Output (Offline Test Harness — 17/17 PASS)

```text
===============================================================
ANTIGRAVITY CHECK HOLE CG1 — REPRODUCIBLE TEST HARNESS
Timestamp: 2026-10-19 06:45:22 UTC
===============================================================
Target Assembly: C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
Assembly SHA256: D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E
Assembly LastWrite: 2026-10-19 06:24:14 UTC
Loaded Assembly: ADDIN, Version=1.0.0.0, Culture=neutral, PublicKeyToken=36eed1a140b4715f

--- 1. Testing CheckAssemblyHole.BuildConnectionCoverage (Display & Transfer) ---
PASS [E01]: Null arguments correctly throw ArgumentNullException
PASS [E02]: Empty input returns correct empty status
PASS [E03]: Two concentric holes display mapping OK with unassigned master
PASS [E04]: OK verdict transferred correctly to group row (MaxOffsetMm=0.09)
PASS [E05]: NG verdict transferred correctly (CHUA_XAC_DINH_MASTER, NgGroupsWithoutMaster=1)
PASS [E06]: Projection <= 0.05 mm diagnosed as 'Khong phan biet duoc hai dau truc'
PASS [E07]: 3-layer stack A-B-C diagnosed with endpoints A and C
PASS [E08]: Independent connections form distinct groups without global Part master
PASS [E09]: Unmatched hole preserved and reported in coverage status

--- 2. Testing Production EvaluateStack & CG1 Pipeline (Real Algorithms) ---
PASS [E10/T02]: Real EvaluateStack evaluated 0.09mm offset as OK; CG1 correctly recorded Status=OK and unassigned master
PASS [E11/T03]: Real EvaluateStack evaluated 0.11mm offset as NG; CG1 correctly recorded Status=NG, CHUA_XAC_DINH_MASTER, NgGroupsWithoutMaster=1
PASS [E12/T04]: Real EvaluateStack evaluated 0.35 deg angle as NG; CG1 correctly recorded Status=NG

--- 3. Testing CheckHoleResultsForm and WinForms Integration ---
PASS [UI01]: CheckHoleReport.Summary correctly formats diagnostic connection coverage
PASS [UI02]: Button created with text 'Nhóm liên kết' and initial state disabled
PASS [UI03]: Button enabled after valid report with ConnectionCoverage is set
PASS [UI04]: Button disabled during busy state
PASS [UI05]: Button re-enabled when busy=false

===============================================================
RESULT: ALL 17 AUTOMATED TESTS PASSED (0 FAILURES)
```

---

## 3. Verbatim Console Output (Live SOLIDWORKS In-CAD Regression Runner — 2/2 PASS, Exit Code 0)

```text
===============================================================================
ANTIGRAVITY CHECK HOLE CG1 — LIVE SOLIDWORKS REGRESSION & BENCHMARK RUNNER
Timestamp (UTC): 2026-10-19 06:39:27 UTC
===============================================================================
[SETUP] Baseline DLL:  C:\Tao moi\ADDIN_MAIN_BASELINE\bin\x64\Release\ADDIN.dll
[SETUP] CG1 Target DLL: C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
[SETUP] Baseline MVID: 8008c4c2-2258-456a-8cf5-5b6fed1d742b | SHA256: 297D66CDF1EF56382C1165B8F84AF7B394090B2B8D2DC25A2F4D71067836118D
[SETUP] CG1      MVID: 456f9ade-ac27-4395-854f-857ad2521fd0 | SHA256: D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E
[SAFETY] Pre-existing SOLIDWORKS session detected. Attaching to ROT/ProgID without closing user session.
[SOLIDWORKS] Connected version: 32.5.0

-------------------------------------------------------------------------------
Evaluating: run07_Missing_Ura_BOM.SLDASM
Path:       C:\Users\SGN26\Documents\addin\work\missing_ura_test_20261004\cases\run07\run07_Missing_Ura_BOM.SLDASM
-> Running Baseline (main branch code)...
   [Baseline] Logical=0, Grouped=0, Unmatched=0, Stacks=0, NG=0, PatNG=0, Miss=0
-> Running CG1 Candidate (feat/check-hole-connection-groups-cg1)...
   [CG1 Target] Logical=0, Grouped=0, Unmatched=0, Stacks=0, NG=0, PatNG=0, Miss=0
   [CG1 Diag]   Groups=0, CoverageStatus=KHONG_DOC_DUOC_LO_HOAC_MODEL_KHONG_CO_LO, NgWithoutMaster=0, UnmatchedHoles=0
STATUS: [PASS] 100% equivalence between baseline and CG1. All invariants satisfied.
[SAFETY VERIFIED] Original file SHA256 identical before and after test (3DFBB2CC...)

-------------------------------------------------------------------------------
Evaluating: 1618-01-BH.SLDASM
Path:       C:\Users\SGN26\Documents\addin\work\same_part_1618_20261010\all-models\1618-01-BH.SLDASM
-> Running Baseline (main branch code)...
   [Baseline] Logical=916, Grouped=696, Unmatched=220, Stacks=348, NG=0, PatNG=0, Miss=0
-> Running CG1 Candidate (feat/check-hole-connection-groups-cg1)...
   [CG1 Target] Logical=916, Grouped=696, Unmatched=220, Stacks=348, NG=0, PatNG=0, Miss=0
   [CG1 Diag]   Groups=348, CoverageStatus=CHUA_DAY_DU: CO_LO_CHUA_GHEP, NgWithoutMaster=0, UnmatchedHoles=220
STATUS: [PASS] 100% equivalence between baseline and CG1. All invariants satisfied.
[SAFETY VERIFIED] Original file SHA256 identical before and after test (45F500B4...)

===============================================================================
REGRESSION AND BENCHMARK RESULTS TABLE
===============================================================================
| Model | Status | Logical | Grouped | Unmatched | Stacks | NG Stacks | Pattern/Miss | CG1 Groups | Coverage Status | SHA Match |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| run07_Missing_Ura_BOM.SLDASM | **PASS** | 0 | 0 | 0 | 0 | 0 | 0/0 | 0 | KHONG_DOC_DUOC_LO_HOAC_MODEL_KHONG_CO_LO | YES |
| 1618-01-BH.SLDASM | **PASS** | 916 | 696 | 220 | 348 | 0 | 0/0 | 348 | CHUA_DAY_DU: CO_LO_CHUA_GHEP | YES |

SUMMARY: Total=2 | PASS=2 | FAIL=0 | SKIP=0
TEST OUTCOME: SUCCEEDED (Exit Code 0)
[SAFETY] Preserving user's pre-existing SOLIDWORKS session intact.
```

---

## 4. Test Coverage Breakdown

| Category | Test ID | Description & Assertion | Verdict |
|---|---|---|---|
| **Coverage Display** | E01 | `BuildConnectionCoverage(null, null)` throws `ArgumentNullException` | `PASS` |
| | E02 | Empty holes list produces `CoverageStatus="KHONG_DOC_DUOC_LO_HOAC_MODEL_KHONG_CO_LO"` | `PASS` |
| | E03 | Two concentric holes display mapping with unassigned master | `PASS` |
| | E04 | OK verdict correctly mapped to group row | `PASS` |
| | E05 | NG verdict correctly mapped with `CHUA_XAC_DINH_MASTER` | `PASS` |
| | E06 | Close projection separation ($\le 0.05\text{ mm}$) marked ambiguous | `PASS` |
| | E07 | 3-layer stack A–B–C endpoints mapped to A and C | `PASS` |
| | E08 | Independent B–D connections form distinct groups without global Part master | `PASS` |
| | E09 | Unmatched hole count and `CHUA_DAY_DU: CO_LO_CHUA_GHEP` preserved | `PASS` |
| **Real Production Engine** | E10 / T02 | Real `EvaluateStack`: offset $0.09\text{ mm} \le 0.10\text{ mm}$ evaluates to `IsNg=false`; CG1 pipeline yields `Status="OK"` | `PASS` |
| | E11 / T03 | Real `EvaluateStack`: offset $0.11\text{ mm} > 0.10\text{ mm}$ evaluates to `IsNg=true`; CG1 pipeline yields `Status="NG"`, `CHUA_XAC_DINH_MASTER` | `PASS` |
| | E12 / T04 | Real `EvaluateStack`: angle $0.35^\circ > 0.20^\circ$ evaluates to `IsNg=true`; CG1 pipeline yields `Status="NG"` | `PASS` |
| **WinForms Offline UI** | UI01 | `CheckHoleReport.Summary` format includes diagnostic metrics | `PASS` |
| | UI02 | Button `connectionGroups` created with text `"Nhóm liên kết"`, initially disabled | `PASS` |
| | UI03 | Button enabled after valid report with `ConnectionCoverage` is set | `PASS` |
| | UI04 | Button disabled during busy state | `PASS` |
| | UI05 | Button re-enabled when busy completes | `PASS` |
| **Live CAD Dual Regression** | CAD-Smoke | Zero-hole model `run07_Missing_Ura_BOM.SLDASM` evaluated on both baseline `main` and `CG1` candidate. Exact 0 across all metrics, coverage status verified, 0 errors. | `PASS` |
| | CAD-Prod | Production 916-hole assembly `1618-01-BH.SLDASM` evaluated on both baseline `main` and `CG1` candidate. Exact match: 916 logical holes, 696 grouped, 220 unmatched, 348 stacks, 0 NG stacks. CG1: 348 connection groups, status `CHUA_DAY_DU: CO_LO_CHUA_GHEP`. Pre/post SHA256 verified identical. | `PASS` |
| | CAD-T01–T10 | Synthetic fixtures matching T01–T10 exact geometric specifications | `NOT_TESTED` (fixtures pending user provision) |
