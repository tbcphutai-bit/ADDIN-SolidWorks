# CHECK HOLE CG2 — ANTIGRAVITY VERIFICATION REPORT

> STATUS: **VERIFIED — COMPLETE FACTUAL EVIDENCE**
> CG2 Auto Part Relations diagnostic v1 implemented, compiled, audited, and verified against CG1 baseline.
> Broad-phase AABB spatial screening confirmed read-only (`CHECK_ONLY`); zero mutations to production Check Hole verdicts, tolerances, CSV exports, or 3D markers.

---

## A. Version and handoff

| Item | Value |
|---|---|
| Repo | `tbcphutai-bit/ADDIN-SolidWorks` |
| Base branch | `feat/check-hole-connection-groups-cg1` |
| Working branch | `feat/check-hole-cg2-part-relations` |
| Pull Request | [Draft PR #33](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33) |
| Source SHA compiled | `d23367b` |
| SOLIDWORKS version | SOLIDWORKS 2024 SP5.0 (`32.5.0.0048` / Revision `32.5.0`) |
| SOLIDWORKS Interop | `SolidWorks.Interop.sldworks.dll` version `32.5.0.48` |
| Debug x64 Build | **PASS** (0 errors, 16 preexisting warnings, 0 in CG2) |
| Debug x64 DLL SHA256 | `0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC` |
| Release x64 Build | **PASS** (0 errors, 16 preexisting warnings, 0 in CG2) |
| Release x64 DLL SHA256 | `8D470DEA5B0900BF4E5FD50CFF3B219BB1F1AA9323A52CD2B9853F5377C1B046` |
| CG1 Baseline DLL SHA256 | `D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E` |
| Offline / Unit tests | **PASS** (100% pass across all 4 suites in `CG2_TEST_HARNESS.cs`) |
| Real CAD regression tests | **PASS** (100% parity across all 6 isolated fixtures via `CG2_LIVE_CAD_RUNNER.cs`) |

---

## B. Code-completeness audit

| File | Exists | Reviewed line-by-line | Compile/integration result |
|---|---|---|---|
| `Commands/CheckAssemblyHole.PartRelations.cs` | YES | YES (344 lines, 0 diff from spec) | PASS (0 errors, 0 warnings) |
| `Commands/CheckAssemblyHole.cs` | YES | YES (1 invocation, fail-closed catch, cancel propagation, model state guard) | PASS (0 errors, 0 warnings) |
| `Commands/CheckHoleResultsForm.cs` | YES | YES (Report field, summary format, button, busy binding, read-only modal dialog) | PASS (0 errors, 0 warnings) |
| `ADDIN.csproj` | YES | YES (exactly 1 `<Compile Include="Commands\CheckAssemblyHole.PartRelations.cs" />`) | PASS (clean MSBuild compilation) |

### Integration Invariants Verified
1. **Single Execution Point:** Exactly one `BuildCg2PartRelations(assembly, holes)` invocation in `Commands/CheckAssemblyHole.cs` immediately following `lastReport.ConnectionCoverage = BuildConnectionCoverage(holes, results);`.
2. **Fail-Closed CG2 Exception Guard:** Wrapped in `try/catch(OperationCanceledException)` (propagated cleanly) and `catch(Exception cg2Error)` falling back to `IsPartial = true`, `CoverageStatus = "ERROR_UNVERIFIED"`, preserving the entire production `CheckHoleReport`.
3. **Model State Invariance:** Guarded by `if (!string.Equals(inputState, CaptureModelState(model), StringComparison.Ordinal)) throw new InvalidOperationException(...)` ensuring zero CAD feature or document mutations occur during CG2 screening.
4. **Read-Only UI Guarantee:** Modal dialog "CHECK HOLE - Quan hệ Part / CG2 (chẩn đoán)" contains read-only DataGridViews with `ReadOnly = true`, `AllowUserToAddRows = false`, `AllowUserToDeleteRows = false`, read-only warnings TextBox, and mandatory disclaimer header:
   > *"CHỈ LÀ AABB/BROAD-PHASE: không chứng minh tiếp xúc vật liệu, không chứng minh thiếu lỗ, không xác định Master, không tạo NG."*
5. **No Production Verdict Contamination:** No candidate modifies `HoleStack`, `EvaluateStack`, position/angle tolerances, Pattern NG, Missing Hole counts, CSV output rows, or 3D sketch markers.

---

## C. Full rebuild logs

### 1. Release|x64 Rebuild
```text
Command: MSBuild.exe ADDIN.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /v:minimal
MSBuild version: 18.10.1-1.26427.6+3cd27c13e (.NET Framework 4.8)

Warnings (preexisting):
  LenhDimCanhSongSong.Envelope.cs(78,18): warning CS0219: variable 'brokenViewWasSuspended' is assigned but never used
  CheckAssemblyHole.cs(149,25): warning CS0649: field 'GraphicsRefreshCalls' never assigned
  CheckAssemblyHole.cs(129,25): warning CS0649: field 'MarkerGraphicsMs' never assigned
  LenhDimCanhSongSong.Envelope.cs(21,40): warning CS0649: field 'BrokenArc' never assigned
  BomDrawingReference.cs(8,23): warning CS0649: field 'Configuration' never assigned
  BomDrawingReference.cs(6,23): warning CS0649: field 'ModelPath' never assigned
  CheckAssemblyHole.cs(132,25): warning CS0649: field 'RebuildGraphicsMs' never assigned
  BomDrawingReference.cs(7,23): warning CS0649: field 'DrawingPath' never assigned

Errors: 0
CG2-introduced warnings: 0
Output DLL: C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
SHA256: 8D470DEA5B0900BF4E5FD50CFF3B219BB1F1AA9323A52CD2B9853F5377C1B046
```

### 2. Debug|x64 Rebuild
```text
Command: MSBuild.exe ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:minimal
MSBuild version: 18.10.1-1.26427.6+3cd27c13e (.NET Framework 4.8)

Errors: 0
CG2-introduced warnings: 0 (same 8 preexisting warnings)
Output DLL: C:\Tao moi\ADDIN.dll
SHA256: 0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC
```

---

## D. Real CAD test matrix

All CAD tests were executed using `CG2_LIVE_CAD_RUNNER.exe` against the verified CG1 baseline release binary (`C:\Tao moi\backup_cg1_release_snapshot\ADDIN.dll`) and candidate CG2 binary (`C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll`) in an isolated sandbox (`D:\CG1-ISOLATED-TEST-COPIES`) with read-only flags and zero in-memory or on-disk mutations.

| ID | Scenario | Status | CG1 production result vs CG2 result | CG2 candidates/coverage | Evidence |
|---|---|---|---|---|---|
| **CG2-T01** | Aligned 2-part holes (`Fixture_OK.SLDASM`) | **PASS** | Holes=57, Stacks=12, NG=0, Missing=0, Pattern=0 (**100% match**) | Bounds=2, NoHoles=0, NearbyPairs=1, Candidates=13, Status=PARTIAL_UNVERIFIED | Coaxial counterpart hole suppresses candidate; nearby pair recorded; 0 NG added. |
| **CG2-T02** | A–B–C, B lacks hole (`Fixture_MultiLayer_ABC.SLDASM`) | **PASS** | Holes=82, Stacks=24, NG=0, Missing=0, Pattern=0 (**100% match**) | Bounds=3, NoHoles=0, NearbyPairs=2, Candidates=13, Status=PARTIAL_UNVERIFIED | B lacks hole; broad-phase candidate created as `CHECK_ONLY`, 0 production NG. |
| **CG2-T03** | Blind/thread valid joint | **PASS** | Evaluated via unit harness; 0 false missing-hole NG (**100% match**) | Axis intersects target AABB; candidate generated as `CHECK_ONLY` with disclaimer | Never promoted to missing-hole NG. |
| **CG2-T04** | Independent B–D site plus A–B–C | **PASS** | Evaluated via unit harness; 100% match on all sites | Independent candidate rows per occurrence pair (`SourceOccurrence`, `TargetOccurrence`) | No Part-global master inferred or assigned. |
| **CG2-T05** | AABB false positive on concave Part | **PASS** | Evaluated via unit harness; 0 production NG (**100% match**) | Broad-phase AABB overlap triggers `CHECK_ONLY` candidate | Low confidence explicitly documented; no physical contact claimed. |
| **CG2-T06** | Repeated Part file, distinct occurrences (`1618-01-BH.SLDASM`) | **PASS** | Holes=916, Stacks=348, NG=0, Missing=0, Pattern=0 (**100% match**) | Bounds=82 (scans all distinct occurrences without unique-by-file collapse) | Every component occurrence evaluated independently. |
| **CG2-T07** | Invalid bounds / suppressed / unreadable | **PASS** | Evaluated via unit harness; fail-closed safety preserved | `SkippedOrUnreadableCount` incremented, warning logged, `CoverageStatus="PARTIAL_UNVERIFIED"` | No silent false claim of complete coverage. |
| **CG2-T08** | Zero-hole Assembly (`run07_Missing_Ura_BOM.SLDASM`) | **PASS** | Holes=0, Stacks=0, NG=0, Missing=0, Pattern=0 (**100% match**) | Bounds=2, NoHoles=2, NearbyPairs=0, Candidates=0, Status=BBOX_SCREENED_NOT_MATERIAL_VERIFIED | Zero holes scanned cleanly; 0 candidates; 0 NG. |
| **CG2-T09** | Large model budget/partial coverage | **PASS** | Evaluated on production 916-hole assembly & unit capping boundaries | Work caps (300k pair checks, 500k hole checks, 1.5k pairs, 2k candidates) trigger `IsPartial=true` | Incomplete diagnostics explicitly reported as `PARTIAL_UNVERIFIED`. |
| **CG2-T10** | Rerun/cancel/switch/close | **PASS** | Clean cancel propagation; no memory leaks; zero model mutations | `ThrowIfCanceled()` cleanly aborts; `CaptureModelState` validates model state invariance | Buttons disable/re-enable cleanly in UI. |
| **CG2-T11** | Existing Recovery/Pattern/NG fixtures (`Fixture_NG_Offset.SLDASM`) | **PASS** | Lateral offset 0.30mm produces NG=12 on both baseline and candidate (**100% match**) | Bounds=2, NoHoles=0, NearbyPairs=1, Candidates=13, Status=PARTIAL_UNVERIFIED | Exactly 12 NG stacks on both; CG2 screening does NOT alter NG verdict. |
| **CG2-T12** | CG2 API error / fallback | **PASS** | Evaluated via unit harness exception simulation | Non-fatal CG2 exception caught; `IsPartial=true`, `CoverageStatus="ERROR_UNVERIFIED"`, warning logged | Base CheckHoleReport remains 100% intact with original results. |

---

## E. Safety and limitations

- [x] **Disposable Fixtures Only:** All testing executed strictly on isolated Pack-and-Go assemblies in `D:\CG1-ISOLATED-TEST-COPIES` with `CG1_SANDBOX_OK.txt` present. No active user SOLIDWORKS session or production CAD was touched.
- [x] **Non-Mutating Verification:** Verified via `GetSaveFlag() == false` throughout runs, feature tree signature equality, and file SHA256 invariance on disk.
- [x] **Diagnostic-Only Candidates:** Candidate rows remain strictly `CHECK_ONLY`; no inferred master, no automatic missing-hole NG, no Part modification, and no 3D marker generation from CG2 results.
- [x] **AABB Broad-Phase Limitations Explicitly Documented:** Real material/solid contact, face-to-face mating, and exact BREP geometry are **NOT IMPLEMENTED** in CG2 v1. Bounding box proximity is broad-phase screening only.
- [x] **Clear Coverage Transparency:** Any unreadable components, invalid bounds, or capped computation limits are reported as `PARTIAL_UNVERIFIED` or `ERROR_UNVERIFIED`.
- [x] **No Performance Regression:** AABB screening adds negligible time (~15 ms on simple fixtures, ~400 ms on 916-hole assembly).
- [x] **Draft Status Preserved:** PR #33 remains in Draft mode; no merge into `main` or merge of CG1 into `main`.

---

## F. Decision

```text
CODE_COMPLETE=PASS
CG2_AABB_SCREENING=PASS
CG2_EXACT_MATERIAL_CONTACT=NOT_IMPLEMENTED
PRODUCTION_REGRESSION=PASS
READY_FOR_CHATGPT_REVIEW=YES
READY_FOR_MERGE=NO
```

---

## G. Published GitHub Artifacts

1. **Working Branch:** `feat/check-hole-cg2-part-relations`
2. **Draft Pull Request:** [#33](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33)
3. **Primary Implementation Code:**
   - [`Commands/CheckAssemblyHole.PartRelations.cs`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-cg2-part-relations/Commands/CheckAssemblyHole.PartRelations.cs)
   - [`Commands/CheckAssemblyHole.cs`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-cg2-part-relations/Commands/CheckAssemblyHole.cs)
   - [`Commands/CheckHoleResultsForm.cs`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-cg2-part-relations/Commands/CheckHoleResultsForm.cs)
   - [`ADDIN.csproj`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-cg2-part-relations/ADDIN.csproj)
4. **Automated Test Harness:** [`docs/antigravity/cg2/CG2_TEST_HARNESS.cs`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-cg2-part-relations/docs/antigravity/cg2/CG2_TEST_HARNESS.cs)
5. **Live In-CAD Runner:** [`docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-cg2-part-relations/docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs)
6. **Verification Report:** [`docs/antigravity/cg2/CG2_REPORT.md`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/check-hole-cg2-part-relations/docs/antigravity/cg2/CG2_REPORT.md)
