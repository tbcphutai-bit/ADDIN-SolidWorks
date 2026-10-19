# CHECK HOLE CG1 — TEST HARNESS & VERIFICATION EVIDENCE

> Source files:
> - [`docs/antigravity/reports/TEST_HARNESS.cs`](TEST_HARNESS.cs) (Offline algorithmic & UI harness)
> - [`docs/antigravity/reports/LIVE_CAD_RUNNER.cs`](LIVE_CAD_RUNNER.cs) (Live SOLIDWORKS CAD benchmark)
>
> Execution Date: 2026-10-19 05:32:40 UTC
> Target Binary: `C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll`
> Binary SHA256: `D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E`
> Binary LastWrite: 2026-10-19 05:29:39 UTC

---

## 1. How to Reproduce

### A. Offline Test Harness (17/17 Checks Passed)
```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /r:"C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll" /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll" /out:"docs\antigravity\reports\TEST_HARNESS.exe" "docs\antigravity\reports\TEST_HARNESS.cs"
& "docs\antigravity\reports\TEST_HARNESS.exe" "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll"
Remove-Item "docs\antigravity\reports\TEST_HARNESS.exe" -Force
```

### B. Live SOLIDWORKS In-CAD Smoke Test & Benchmark
```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /r:"C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll" /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll" /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.swconst.dll" /out:"docs\antigravity\reports\LIVE_CAD_RUNNER.exe" "docs\antigravity\reports\LIVE_CAD_RUNNER.cs"
& "docs\antigravity\reports\LIVE_CAD_RUNNER.exe"
Remove-Item "docs\antigravity\reports\LIVE_CAD_RUNNER.exe" -Force
```

---

## 2. Verbatim Console Output (Offline Test Harness)

```text
===============================================================
ANTIGRAVITY CHECK HOLE CG1 — REPRODUCIBLE TEST HARNESS
Timestamp: 2026-10-19 05:32:40 UTC
===============================================================
Target Assembly: C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
Assembly SHA256: D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E
Assembly LastWrite: 2026-10-19 05:29:39 UTC
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

## 3. Verbatim Console Output (Live SOLIDWORKS In-CAD Benchmark)

```text
===============================================================
ANTIGRAVITY CHECK HOLE CG1 — LIVE SOLIDWORKS CAD RUNNER
Timestamp: 2026-10-19 05:29:08 UTC
===============================================================
Connected to SOLIDWORKS: 32.5.0 (SOLIDWORKS 2024 SP5)

--- Testing Model: run07_Missing_Ura_BOM.SLDASM ---
Results: LogicalHoles=0, GroupedHoles=0, UnmatchedHoles=0, Stacks=0, NgStacks=0
CG1 Diagnostic: Groups=0, CoverageStatus=KHONG_DOC_DUOC_LO_HOAC_MODEL_KHONG_CO_LO, NgWithoutMaster=0, Unmatched=0
PASS: Zero-hole assembly diagnosed correctly without errors

--- Testing Model: 1618-01-BH.SLDASM (Production Model) ---
Results: LogicalHoles=916, GroupedHoles=696, UnmatchedHoles=220, Stacks=348, NgStacks=0
CG1 Diagnostic: Groups=348, CoverageStatus=CHUA_DAY_DU: CO_LO_CHUA_GHEP, NgWithoutMaster=0, Unmatched=220
Report Summary:
  1618-01-BH | Default
  Lỗ: 916 | Đã ghép: 696 | Chưa ghép: 220
  Cụm: 348 | OK: 348 | NG: 0 | Thiếu lỗ: 0 | Xử lý: 171.50 s
  Nhóm lỗ đã ghép (chẩn đoán): 348 | NG chưa rõ Master: 0 | Lỗ chưa ghép: 220 | CHUA_DAY_DU: CO_LO_CHUA_GHEP
  Chưa vẽ sketch NG.
PASS: All production metrics and CG1 connection groups match expectation 100%

LIVE CAD BENCHMARK SUCCEEDED!
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
| **Live CAD Execution** | CAD-Smoke | Zero-hole model `run07_Missing_Ura_BOM.SLDASM` executed live in SOLIDWORKS 2024 SP5 | `PASS` |
| | CAD-Prod | Production 916-hole assembly `1618-01-BH.SLDASM` executed live: 916 holes, 348 stacks, 348 groups, 220 unmatched | `PASS` |
| | CAD-T01–T10 | Synthetic fixtures matching T01–T10 exact geometric specifications | `NOT_TESTED` (fixtures pending user provision) |
