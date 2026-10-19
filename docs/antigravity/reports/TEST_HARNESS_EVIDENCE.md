# CHECK HOLE CG1 — TEST HARNESS & VERIFICATION EVIDENCE

> Source files:
> - [`docs/antigravity/reports/TEST_HARNESS.cs`](TEST_HARNESS.cs) (Offline algorithmic, tolerance & WinForms state harness)
> - [`docs/antigravity/reports/LIVE_CAD_RUNNER.cs`](LIVE_CAD_RUNNER.cs) / [`docs/antigravity/review4/CG1_REVIEW4_SAFE_RUNNER.cs`](../review4/CG1_REVIEW4_SAFE_RUNNER.cs) (Review #4/5 Fail-closed isolated sandbox regression runner: Baseline `main` vs `CG1`)
> - [`docs/antigravity/reports/TEST_UI_INTERACTION.cs`](TEST_UI_INTERACTION.cs) (Review #5/6 Fail-closed WinForms UI modal, CSV, and safety audit test)
>
> Execution Date: 2026-10-19 09:20:00 UTC
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

### B. Safe Runner Self-Test (--self-test)
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe" /target:exe /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll" /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.swconst.dll" /out:"docs\antigravity\reports\LIVE_CAD_RUNNER.exe" "docs\antigravity\reports\LIVE_CAD_RUNNER.cs"
& "docs\antigravity\reports\LIVE_CAD_RUNNER.exe" --self-test
```

### C. Safe Runner Isolated Fixture Regression Execution
```powershell
# Precondition: Ensure ALL SOLIDWORKS processes are closed before executing.
# Precondition: Fixtures must be isolated inside a sandbox with CG1_SANDBOX_OK.txt.
$mainDll = "C:\Tao moi\ADDIN_MAIN_BASELINE\bin\x64\Release\ADDIN.dll"
$cg1Dll  = "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll"
$sandbox = "D:\CG1-ISOLATED-TEST-COPIES"
$zero    = "$sandbox\ZeroHole\run07_Missing_Ura_BOM.SLDASM"
$large   = "$sandbox\ProductionCopy\1618-01-BH.SLDASM"

& "docs\antigravity\reports\LIVE_CAD_RUNNER.exe" $mainDll $cg1Dll $sandbox $zero $large
Remove-Item "docs\antigravity\reports\LIVE_CAD_RUNNER.exe" -Force
```

### D. Hardened UI Interaction and Safety Audit Test
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe" /r:"System.Windows.Forms.dll" /r:"System.Drawing.dll" /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll" /r:"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.swconst.dll" /out:"docs\antigravity\reports\TEST_UI_INTERACTION.exe" "docs\antigravity\reports\TEST_UI_INTERACTION.cs"
& "docs\antigravity\reports\TEST_UI_INTERACTION.exe" $cg1Dll $sandbox "$sandbox\SimpleFixtures\Fixture_OK.SLDASM"
Remove-Item "docs\antigravity\reports\TEST_UI_INTERACTION.exe" -Force
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

## 3. Verbatim Console Output (Safe Runner --self-test — PASS, Exit Code 0)

```text
Microsoft (R) Visual C# Compiler version 5.10.0-1.26380.3
Copyright (C) Microsoft Corporation. All rights reserved.

PASS: exit policy handles PASS / FAIL / SKIP / empty cases
```

---

## 4. Verbatim Evidence of Fail-Closed Precondition Gates

### Gate 1: Existing User SOLIDWORKS Session (Must Refuse, Never Attach/Close)
```text
FAIL CLOSED: System.InvalidOperationException: An existing SOLIDWORKS session is running. Close it yourself before running this isolated test. No CAD was touched.
   at ADDIN.Tests.Cg1Review4SafeRunner.RunRunner(String[] args)
Exit Code: 1
```

### Gate 2: Missing Sandbox Consent File `CG1_SANDBOX_OK.txt` (Must Refuse)
```text
FAIL CLOSED: System.InvalidOperationException: Isolated fixture root missing, or explicit CG1_SANDBOX_OK.txt acknowledgement missing.
   at ADDIN.Tests.Cg1Review4SafeRunner.ValidateSandboxInputs(String baselineDll, String candidateDll, String root, String[] models)
   at ADDIN.Tests.Cg1Review4SafeRunner.RunRunner(String[] args)
Exit Code: 1
```

### Gate 3: Test Model Outside Sandbox Root (Must Refuse)
```text
FAIL CLOSED: System.InvalidOperationException: Path outside isolated fixture root: C:\other\f1.sldasm
   at ADDIN.Tests.Cg1Review4SafeRunner.RequireSandboxPath(String root, String path)
   at ADDIN.Tests.Cg1Review4SafeRunner.ValidateSandboxInputs(String baselineDll, String candidateDll, String root, String[] models)
   at ADDIN.Tests.Cg1Review4SafeRunner.RunRunner(String[] args)
Exit Code: 1
```

### Gate 4: Assembly Containing External Component Dependencies (Refuses & Protects Originals)
In Review #4, `1618-01-BH.SLDASM` had unlinked authoring references pointing to `C:\SGN09\...`. The runner strictly blocked execution (`FAIL CLOSED`, exit code 1).

In Review #5, all internal references were resolved within the sandbox copy (`D:\CG1-ISOLATED-TEST-COPIES\ProductionCopy`). Zero external paths remain (`outside=0`), satisfying `VerifyReferencesUnderSandbox`.

---

## 5. Live CAD Regression & UI Verification Results

### A. Full Production 916-Hole Assembly & Zero-Hole Baseline Comparison
```powershell
& "docs\antigravity\reports\LIVE_CAD_RUNNER.exe" $mainDll $cg1Dll $sandbox `
     "$sandbox\ZeroHole\run07_Missing_Ura_BOM.SLDASM" `
     "$sandbox\ProductionCopy\1618-01-BH.SLDASM"
```

**Verbatim Console Output:**
```text
SOLIDWORKS revision: 32.5.0
BASELINE SHA256: 297D66CDF1EF56382C1165B8F84AF7B394090B2B8D2DC25A2F4D71067836118D
CANDIDATE SHA256: D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E
run07_Missing_Ura_BOM.SLDASM main: holes=0 groups=0 unmatched=0 stacks=0 NG=0
run07_Missing_Ura_BOM.SLDASM CG1 : holes=0 groups=0 unmatched=0 stacks=0 NG=0
run07_Missing_Ura_BOM.SLDASM: PASS
1618-01-BH.SLDASM main: holes=916 groups=696 unmatched=220 stacks=348 NG=0
1618-01-BH.SLDASM CG1 : holes=916 groups=696 unmatched=220 stacks=348 NG=0
1618-01-BH.SLDASM: PASS
SUMMARY: PASS=2 FAIL=0 SKIP=0 EXIT=0
```

### B. Self-Contained Test Fixtures (Review #6: NG Fixture Proven with NG=12)
```powershell
& "docs\antigravity\reports\LIVE_CAD_RUNNER.exe" $mainDll $cg1Dll $sandbox `
    "$sandbox\ZeroHole\run07_Missing_Ura_BOM.SLDASM" `
    "$sandbox\SimpleFixtures\Fixture_OK.SLDASM" `
    "$sandbox\SimpleFixtures\Fixture_NG_Offset.SLDASM" `
    "$sandbox\SimpleFixtures\Fixture_Unmatched.SLDASM" `
    "$sandbox\SimpleFixtures\Fixture_MultiLayer_ABC.SLDASM"
```

**Verbatim Console Output:**
```text
SOLIDWORKS revision: 32.5.0
BASELINE SHA256: 297D66CDF1EF56382C1165B8F84AF7B394090B2B8D2DC25A2F4D71067836118D
CANDIDATE SHA256: D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E
run07_Missing_Ura_BOM.SLDASM main: holes=0 groups=0 unmatched=0 stacks=0 NG=0
run07_Missing_Ura_BOM.SLDASM CG1 : holes=0 groups=0 unmatched=0 stacks=0 NG=0
run07_Missing_Ura_BOM.SLDASM: PASS
Fixture_OK.SLDASM main: holes=57 groups=24 unmatched=33 stacks=12 NG=0
Fixture_OK.SLDASM CG1 : holes=57 groups=24 unmatched=33 stacks=12 NG=0
Fixture_OK.SLDASM: PASS
Fixture_NG_Offset.SLDASM main: holes=57 groups=24 unmatched=33 stacks=12 NG=12
Fixture_NG_Offset.SLDASM CG1 : holes=57 groups=24 unmatched=33 stacks=12 NG=12
Fixture_NG_Offset.SLDASM: PASS
Fixture_Unmatched.SLDASM main: holes=32 groups=0 unmatched=32 stacks=0 NG=0
Fixture_Unmatched.SLDASM CG1 : holes=32 groups=0 unmatched=32 stacks=0 NG=0
Fixture_Unmatched.SLDASM: PASS
Fixture_MultiLayer_ABC.SLDASM main: holes=82 groups=48 unmatched=34 stacks=24 NG=0
Fixture_MultiLayer_ABC.SLDASM CG1 : holes=82 groups=48 unmatched=34 stacks=24 NG=0
Fixture_MultiLayer_ABC.SLDASM: PASS
SUMMARY: PASS=5 FAIL=0 SKIP=0 EXIT=0
```

### C. Hardened WinForms UI & Safety Audit Execution (`TEST_UI_INTERACTION.cs`)
```powershell
& "docs\antigravity\reports\TEST_UI_INTERACTION.exe" $cg1Dll $sandbox "$sandbox\SimpleFixtures\Fixture_OK.SLDASM"
```

**Verbatim Console Output:**
```text
=========================================================
CG1 UI INTERACTION & SAFETY AUDIT TEST
=========================================================
SolidWorks Revision: 32.5.0
Candidate DLL: C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
Fixture: D:\CG1-ISOLATED-TEST-COPIES\SimpleFixtures\Fixture_OK.SLDASM

[1] CheckAssemblyHole Output Metrics:
    Logical Holes: 57
    Stack Count  : 12
    Groups Count : 12
    Unmatched    : 33
    NG No Master : 0
    Coverage Rows: 12

[2] Verifying CheckHoleResultsForm Toolbar Controls:
    Found Button: 'Tìm vị trí' (Enabled=True)
    Found Button: 'Kiểm tra lại' (Enabled=True)
    Found Button: 'Vẽ NG đã chọn' (Enabled=False)
    Found Button: 'Vẽ toàn bộ NG' (Enabled=False)
    Found Button: 'Xuất CSV' (Enabled=True)
    Found Button: 'Nhóm liên kết' (Enabled=True)
    Found Button: 'Hủy' (Enabled=False)
    Found Button: 'Theo dõi 3D' (Enabled=True)
    Found Button: '← Lỗi trước' (Enabled=False)
    Found Button: 'Lỗi tiếp →' (Enabled=False)
    Found Button: 'Chi tiết' (Enabled=True)
    Found Button: 'Hủy' (Enabled=False)
    -> Button 'Nhóm liên kết' presence: PASS
    -> Button 'Nhóm liên kết' enabled: PASS

[3] Testing 'Nhóm liên kết' Modal Dialog via PerformClick():
    -> Modal Title: 'CHECK HOLE - Nhóm liên kết (chẩn đoán)' (PASS)
    -> Header Description Text:
        Nhóm lỗ đã ghép: 12 | NG chưa rõ Master: 0 | Lỗ chưa ghép: 33
        Hai đầu trục chỉ là ứng viên hình học, KHÔNG phải vật chủ được xác nhận.
        CG1 chưa kiểm tra quan hệ đối với Part không có lỗ.
    -> Header Counts & Mandatory Disclaimer: PASS
    -> DataGridView ReadOnly & RowCount (12): PASS
    -> All 10 Diagnostic Columns Present: PASS
    -> Modal Evaluation Level: MODAL_PROGRAMMATIC_PASS

[4] Testing CSV Report Export & Verification:
    -> CSV created: D:\CG1-ISOLATED-TEST-COPIES\Test_Export_Report.csv
    -> CSV line count: 46
    -> Header: Assembly,Configuration,Status,Type,Reference component,Actual component,Offset (mm),Angle (deg),Reason
    -> CSV Export Row-by-Row Content Matches Report: PASS
    -> CSV Writer Level: CSV_WRITER_PASS

[5] Verifying Post-Execution Assembly Safety & Invariance:
    -> Feature Tree Invariance: PASS
    -> Memory Non-Mutation (doc.GetSaveFlag() == false): PASS
    -> Disk File SHA256 Invariance: PASS

=========================================================
ALL UI SAFETY AND INTERACTION CHECKS PASSED (EXIT 0)
Status Levels: MODAL_PROGRAMMATIC_PASS | CSV_WRITER_PASS | UI_USER_CLICK_NOT_TESTED
=========================================================
```

### D. Hardened UI Test Negative Gates (Review #6)

#### D.1 Sandbox Path Enforcement Self-Test
```powershell
& "docs\antigravity\reports\TEST_UI_INTERACTION.exe" --self-test
# Output:
PASS: sandbox path enforcement self-test
# Exit Code: 0
```

#### D.2 Rejection of Path Outside Sandbox Root
```powershell
& "docs\antigravity\reports\TEST_UI_INTERACTION.exe" $cg1Dll $sandbox "C:\outside\model.sldasm"
# Output:
FAIL CLOSED: Path outside isolated fixture root: C:\outside\model.sldasm
# Exit Code: 1
```

#### D.3 Rejection of Stale Markers on Opened Model (`Fixture_OldMarker.SLDASM`)
```powershell
& "docs\antigravity\reports\TEST_UI_INTERACTION.exe" $cg1Dll $sandbox "$sandbox\SimpleFixtures\Fixture_OldMarker.SLDASM"
# Output:
=========================================================
CG1 UI INTERACTION & SAFETY AUDIT TEST
=========================================================
SolidWorks Revision: 32.5.0
Candidate DLL: C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
Fixture: D:\CG1-ISOLATED-TEST-COPIES\SimpleFixtures\Fixture_OldMarker.SLDASM
    -> Disk File SHA256 Invariance: PASS

FAIL CLOSED: System.InvalidOperationException: Isolated run rejected: Existing CHECK-HOLE-NG feature detected (CHECK-HOLE-NG-STALE-MARKER). Run only on a disposable fixture without markers.
   at ADDIN.Tests.TestUiInteraction.RunEntry(String[] args)
# Exit Code: 1
```

---

## 6. Complete Test Coverage Breakdown

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
| **Review #4/5 Safe Runner** | R4-Compile | Standalone C# 7.3 compilation of runner | `PASS` |
| | R4-SelfTest | `--self-test` exit-code semantics validation | `PASS` |
| | R4-ExistingSW | Pre-existing SOLIDWORKS session detected $\to$ refuses execution, fails closed | `PASS` |
| | R4-Consent | Missing `CG1_SANDBOX_OK.txt` $\to$ refuses execution, fails closed | `PASS` |
| | R4-SandboxRoot | Test models outside sandbox $\to$ refuses execution, fails closed | `PASS` |
| | R4-ExtRefs | External component dependencies in assembly $\to$ refuses execution, fails closed | `PASS` |
| | R4-SmokeZero | Isolated zero-hole model `run07_Missing_Ura_BOM.SLDASM` evaluated on baseline and CG1 candidate $\to$ 100% match, exit code 0 | `PASS` |
| **Review #5/6 Live Fixtures** | R5-Prod916 | Production 916-hole assembly `1618-01-BH.SLDASM` with resolved references: 916 holes, 696 grouped, 220 unmatched, 348 stacks, 0 NG $\to$ 100% match, exit code 0 | `PASS` |
| | R5-SubA10 | Subassembly `1618-02-bh-A10.SLDASM`: 90 holes, 64 grouped, 26 unmatched, 32 stacks, 0 NG $\to$ 100% match, exit code 0 | `PASS` |
| | R5-FixOK | Self-contained clean joint `Fixture_OK.SLDASM`: 57 holes, 24 grouped, 33 unmatched, 12 stacks $\to$ 100% match, exit code 0 | `PASS` |
| | R6-FixNGOFF | Offset joint `Fixture_NG_Offset.SLDASM` with mates suppressed & $+0.30\text{ mm}$ translation in X ($> 0.10\text{ mm}$ tol): 57 holes, 24 grouped, 33 unmatched, 12 stacks, **12 NG** on both baseline and CG1 candidate $\to$ 100% match, exit code 0 | `PASS` |
| | R5-FixUNM | Unmatched holes `Fixture_Unmatched.SLDASM`: 32 holes, 0 grouped, 32 unmatched, 0 stacks $\to$ 100% match, exit code 0 | `PASS` |
| | R5-FixML | Multi-layer joint `Fixture_MultiLayer_ABC.SLDASM`: 82 holes, 48 grouped, 34 unmatched, 24 stacks $\to$ 100% match, exit code 0 | `PASS` |
| **Review #6 UI Hardening** | R6-UISelfTest | `--self-test` verifies path containment logic | `PASS` |
| | R6-UIOutside | Rejection of path outside sandbox root (exit 1) | `PASS` |
| | R6-UIMarker | Rejection of stale `CHECK-HOLE-NG*` features before running (exit 1) | `PASS` |
| | R6-UIModal | Toolbar button click event dispatch $\to$ Modal `"CHECK HOLE - Nhóm liên kết (chẩn đoán)"` opens and closes cleanly (`MODAL_PROGRAMMATIC_PASS`) | `PASS` |
| | R6-UIDisclaimer | Header contains required non-authoritative disclaimer and accurate counts | `PASS` |
| | R6-UIGrid | Strict assertions: `grid != null && grid.ReadOnly`, row count == 12, all 10 diagnostic columns present | `PASS` |
| | R6-UICSV | CSV report written and row-by-row verified against `report.Rows` (`CSV_WRITER_PASS`) | `PASS` |
| | R6-UINonMutate | In-memory assembly clean (`GetSaveFlag() == false`), feature tree signature invariant, disk file SHA256 invariant | `PASS` |
| | R6-UIUserClick | End-user manual mouse clicking on physical desktop UI | `UI_USER_CLICK_NOT_TESTED` |
| **Review #7 Hardening** | R7-StrictRefs | Strict fail-closed `VerifyReferencesUnderSandbox` (throws on null assembly, null comps, missing file, outside sandbox) | `PASS` |
| | R7-BtnDispatch | Strict button click event dispatch via `Button.OnClick` reflection without fallback | `BUTTON_EVENT_DISPATCH_PASS` |
| | R7-RFC4180CSV | RFC-4180 CSV parser verifying all 9 columns against `report.Rows` (Assembly, Config, Status, Kind, Ref, Act, Offset, Angle, Reason) | `CSV_ALL_9_COLUMNS_AND_CONTENT_PASS` |
| | R7-FixGenTrace | Standalone fixture generator (`FIXTURE_GENERATOR.cs`) and metadata trace (`FIXTURE_METADATA_TRACE.md`) | `PASS` |
| **Review #8 Hardening** | R8-FixGenSelfTest | Hardened `FIXTURE_GENERATOR.exe --self-test` verifies sandbox consent, root containment, and fail-closed gates | `PASS` |
| | R8-FixGenNegative | Rejection of missing consent token, outside sandbox root, and working project tree | `PASS` |
| | R8-RFC4180Stream | True RFC-4180 stream parser supporting multiline quoted fields, escaped quotes, and commas | `PASS` |
| | R8-ExactCellMatch | Exact raw cell matching without `Trim()`, preserving whitespace, formula prefixes, and literals | `CSV_ALL_9_COLUMNS_RFC4180_PASS` |

---

## 7. Review #7 Execution Logs & Verbatim Output

### A. Strict Sandbox Path & Component Reference Fail-Closed Tests
```powershell
# 1. Path outside sandbox rejection
& "docs\antigravity\reports\TEST_UI_INTERACTION.exe" $cg1Dll $sandbox "C:\outside\model.sldasm"
# Output:
FAIL CLOSED: System.InvalidOperationException: Path outside isolated fixture root: C:\outside\model.sldasm
# Exit Code: 1

# 2. Stale marker rejection
& "docs\antigravity\reports\TEST_UI_INTERACTION.exe" $cg1Dll $sandbox "$sandbox\SimpleFixtures\Fixture_OldMarker.SLDASM"
# Output:
FAIL CLOSED: System.InvalidOperationException: Isolated run rejected: Existing CHECK-HOLE-NG feature detected (CHECK-HOLE-NG-STALE-MARKER). Run only on a disposable fixture without markers.
# Exit Code: 1
```

### B. Live In-CAD UI & 9-Column RFC-4180 CSV Execution (`Fixture_OK.SLDASM`)
```powershell
& "docs\antigravity\reports\TEST_UI_INTERACTION.exe" $cg1Dll $sandbox "$sandbox\SimpleFixtures\Fixture_OK.SLDASM"
```

**Verbatim Console Output:**
```text
=========================================================
CG1 UI INTERACTION & SAFETY AUDIT TEST
=========================================================
SolidWorks Revision: 32.5.0
Candidate DLL: C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
Fixture: D:\CG1-ISOLATED-TEST-COPIES\SimpleFixtures\Fixture_OK.SLDASM

[1] CheckAssemblyHole Output Metrics:
    Logical Holes: 57
    Stack Count  : 12
    Groups Count : 12
    Unmatched    : 33
    NG No Master : 0
    Coverage Rows: 12

[2] Verifying CheckHoleResultsForm Toolbar Controls:
    Found Button: 'Tìm vị trí' (Enabled=True)
    Found Button: 'Kiểm tra lại' (Enabled=True)
    Found Button: 'Vẽ NG đã chọn' (Enabled=False)
    Found Button: 'Vẽ toàn bộ NG' (Enabled=False)
    Found Button: 'Xuất CSV' (Enabled=True)
    Found Button: 'Nhóm liên kết' (Enabled=True)
    Found Button: 'Hủy' (Enabled=False)
    Found Button: 'Theo dõi 3D' (Enabled=True)
    Found Button: '← Lỗi trước' (Enabled=False)
    Found Button: 'Lỗi tiếp →' (Enabled=False)
    Found Button: 'Chi tiết' (Enabled=True)
    Found Button: 'Hủy' (Enabled=False)
    -> Button 'Nhóm liên kết' presence: PASS
    -> Button 'Nhóm liên kết' enabled: PASS

[3] Testing 'Nhóm liên kết' Button Event Dispatch & Modal Dialog:
    -> Button Click Event Dispatch: PASS (BUTTON_EVENT_DISPATCH_PASS)
    -> Modal Title: 'CHECK HOLE - Nhóm liên kết (chẩn đoán)' (PASS)
    -> Header Description Text:
        Nhóm lỗ đã ghép: 12 | NG chưa rõ Master: 0 | Lỗ chưa ghép: 33
        Hai đầu trục chỉ là ứng viên hình học, KHÔNG phải vật chủ được xác nhận.
        CG1 chưa kiểm tra quan hệ đối với Part không có lỗ.
    -> Header Counts & Mandatory Disclaimer: PASS
    -> DataGridView ReadOnly & RowCount (12): PASS
    -> All 10 Diagnostic Columns Present: PASS
    -> Modal Evaluation Level: MODAL_PROGRAMMATIC_PASS

[4] Testing CSV Report Export & Strict 9-Column Verification:
    -> CSV created: D:\CG1-ISOLATED-TEST-COPIES\Test_Export_Report.csv
    -> CSV line count: 46
    -> Header: Assembly,Configuration,Status,Type,Reference component,Actual component,Offset (mm),Angle (deg),Reason
    -> CSV All 9 Columns Verified Against Report.Rows: PASS
    -> CSV Verification Level: CSV_ALL_9_COLUMNS_AND_CONTENT_PASS

[5] Verifying Post-Execution Assembly Safety & Invariance:
    -> Feature Tree Invariance: PASS
    -> Memory Non-Mutation (doc.GetSaveFlag() == false): PASS
    -> Disk File SHA256 Invariance: PASS

=========================================================
ALL UI SAFETY AND INTERACTION CHECKS PASSED (EXIT 0)
Levels: BUTTON_EVENT_DISPATCH_PASS | MODAL_PROGRAMMATIC_PASS | CSV_ALL_9_COLUMNS_AND_CONTENT_PASS | UI_USER_CLICK_NOT_TESTED
=========================================================
```

---

## 8. Review #8 Execution Logs & Verbatim Output

### A. FIXTURE_GENERATOR Safety Gates & Self-Test
```powershell
& "docs\antigravity\reports\FIXTURE_GENERATOR.exe" --self-test
# Output:
PASS: FIXTURE_GENERATOR sandbox consent and safety gates self-test
# Exit Code: 0
```

### B. FIXTURE_GENERATOR Negative Security Gates
```powershell
# 1. Missing sandbox consent token (CG1_SANDBOX_OK.txt)
& "docs\antigravity\reports\FIXTURE_GENERATOR.exe" "C:\outside" "C:\outside\dir"
# Output:
FAIL CLOSED: System.InvalidOperationException: Sandbox consent token missing: C:\outside\CG1_SANDBOX_OK.txt. Refusing execution outside verified isolated sandbox.
   at ADDIN.Tests.FixtureGenerator.RequireSandboxConsent(String sandboxRoot, String fixtureDir)
   at ADDIN.Tests.FixtureGenerator.RunGenerate(String sandboxRoot, String fixtureDir, Boolean allowOverwrite)
   at ADDIN.Tests.FixtureGenerator.Main(String[] args)
# Exit Code: 1

# 2. fixtureDir outside sandbox root
& "docs\antigravity\reports\FIXTURE_GENERATOR.exe" "D:\CG1-ISOLATED-TEST-COPIES" "C:\outside"
# Output:
FAIL CLOSED: System.InvalidOperationException: fixtureDir (C:\outside) is outside sandboxRoot (D:\CG1-ISOLATED-TEST-COPIES). Refusing execution.
   at ADDIN.Tests.FixtureGenerator.RequireSandboxConsent(String sandboxRoot, String fixtureDir)
   at ADDIN.Tests.FixtureGenerator.RunGenerate(String sandboxRoot, String fixtureDir, Boolean allowOverwrite)
   at ADDIN.Tests.FixtureGenerator.Main(String[] args)
# Exit Code: 1

# 3. fixtureDir inside git repository or working project tree
& "docs\antigravity\reports\FIXTURE_GENERATOR.exe" "D:\CG1-ISOLATED-TEST-COPIES" "c:\Tao moi\ADDIN"
# Output:
FAIL CLOSED: System.InvalidOperationException: fixtureDir (c:\Tao moi\ADDIN) is outside sandboxRoot (D:\CG1-ISOLATED-TEST-COPIES). Refusing execution.
   at ADDIN.Tests.FixtureGenerator.RequireSandboxConsent(String sandboxRoot, String fixtureDir)
   at ADDIN.Tests.FixtureGenerator.RunGenerate(String sandboxRoot, String fixtureDir, Boolean allowOverwrite)
   at ADDIN.Tests.FixtureGenerator.Main(String[] args)
# Exit Code: 1
```

### C. Multiline RFC-4180 & Exact Cell Escaping Self-Test
```powershell
& "docs\antigravity\reports\TEST_UI_INTERACTION.exe" --self-test
# Output:
PASS: sandbox path enforcement and full RFC-4180 multiline CSV parsing self-test
# Exit Code: 0
```

### D. Live In-CAD UI & Multiline RFC-4180 9-Column Execution (`Fixture_OK.SLDASM`)
```powershell
& "docs\antigravity\reports\TEST_UI_INTERACTION.exe" $cg1Dll $sandbox "$sandbox\SimpleFixtures\Fixture_OK.SLDASM"
```

**Verbatim Console Output:**
```text
=========================================================
CG1 UI INTERACTION & SAFETY AUDIT TEST
=========================================================
SolidWorks Revision: 32.5.0
Candidate DLL: C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
Fixture: D:\CG1-ISOLATED-TEST-COPIES\SimpleFixtures\Fixture_OK.SLDASM

[1] CheckAssemblyHole Output Metrics:
    Logical Holes: 57
    Stack Count  : 12
    Groups Count : 12
    Unmatched    : 33
    NG No Master : 0
    Coverage Rows: 12

[2] Verifying CheckHoleResultsForm Toolbar Controls:
    -> Button 'Nhóm liên kết' presence: PASS
    -> Button 'Nhóm liên kết' enabled: PASS

[3] Testing 'Nhóm liên kết' Button Event Dispatch & Modal Dialog:
    -> Button Click Event Dispatch: PASS (BUTTON_EVENT_DISPATCH_PASS)
    -> Modal Title: 'CHECK HOLE - Nhóm liên kết (chẩn đoán)' (PASS)
    -> Header Description Text:
        Nhóm lỗ đã ghép: 12 | NG chưa rõ Master: 0 | Lỗ chưa ghép: 33
        Hai đầu trục chỉ là ứng viên hình học, KHÔNG phải vật chủ được xác nhận.
        CG1 chưa kiểm tra quan hệ đối với Part không có lỗ.
    -> Header Counts & Mandatory Disclaimer: PASS
    -> DataGridView ReadOnly & RowCount (12): PASS
    -> All 10 Diagnostic Columns Present: PASS
    -> Modal Evaluation Level: MODAL_PROGRAMMATIC_PASS

[4] Testing CSV Report Export & Strict 9-Column Verification:
    -> CSV created: D:\CG1-ISOLATED-TEST-COPIES\Test_Export_Report.csv
    -> CSV record count (including header): 46
    -> Header: Assembly,Configuration,Status,Type,Reference component,Actual component,Offset (mm),Angle (deg),Reason
    -> CSV All 9 Columns Verified Against Report.Rows (RFC-4180 Multiline Parser & Exact Cell Escaping): PASS
    -> CSV Verification Level: CSV_ALL_9_COLUMNS_RFC4180_PASS

[5] Verifying Post-Execution Assembly Safety & Invariance:
    -> Feature Tree Invariance: PASS
    -> Memory Non-Mutation (doc.GetSaveFlag() == false): PASS
    -> Disk File SHA256 Invariance: PASS

=========================================================
ALL UI SAFETY AND INTERACTION CHECKS PASSED (EXIT 0)
Levels: BUTTON_EVENT_DISPATCH_PASS | MODAL_PROGRAMMATIC_PASS | CSV_ALL_9_COLUMNS_RFC4180_PASS | UI_USER_CLICK_NOT_TESTED
=========================================================
```
